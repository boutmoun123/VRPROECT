using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public class Simulation3D : MonoBehaviour
{
    public enum ParticleCountPreset
    {
        Low,
        Medium,
        High,
        Ultra
    }

    public event System.Action SimulationStepCompleted;

    [Header("Settings")]
    public float timeScale = 1;
    public bool fixedTimeStep;
    public int iterationsPerFrame;
    public float gravity = -10;
    [Range(0, 1)] public float collisionDamping = 0.05f;
    public float smoothingRadius = 0.2f;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength;

    [Header("Particle Count Presets")]
    public bool showLegacyFluidParticles = false;
    public ParticleCountPreset particlePreset = ParticleCountPreset.Low;
    public int currentParticleCount = 8000;
    public float estimatedGpuBufferMegabytes;
    public string particlePresetWarning = "";

    [Header("Paint Controls")]
    public float emissionRate = 120f;
    public float exitSpeed = 3f;
    public float holeDiameter = 0.08f;

    [Header("References")]
    public ComputeShader compute;
    public Spawner3D spawner;
    public ParticleDisplay3D display;
    public Transform floorDisplay;

    // Buffers
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityBuffer { get; private set; }
    public ComputeBuffer predictedPositionsBuffer;
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;

    // Kernel IDs
    const int externalForcesKernel = 0;
    const int spatialHashKernel = 1;
    const int densityKernel = 2;
    const int pressureKernel = 3;
    const int viscosityKernel = 4;
    const int updatePositionsKernel = 5;

    GPUSort gpuSort;

    // State
    bool isPaused;
    bool pauseNextFrame;
    bool initialized;
    Spawner3D.SpawnData spawnData;

    const int lowParticleCount = 8000;
    const int mediumParticleCount = 50000;
    const int highParticleCount = 200000;
    const int ultraParticleCount = 1000000;
    const int estimatedBytesPerParticle = 48;
    const int recommendedUltraGpuMemoryMb = 4096;

    void Start()
    {
        float deltaTime = 1 / 60f;

        // The fluid solver expects a stable fixed step. This project has one scene-level
        // simulation controller, so the global fixed timestep is set here deliberately.
        Time.fixedDeltaTime = deltaTime;

        if (spawner == null || compute == null || display == null)
        {
            Debug.LogError("Simulation3D needs a compute shader, spawner, and particle display.");
            enabled = false;
            return;
        }

        ApplyLegacyParticleVisibility();
        ApplyParticlePreset(particlePreset);
    }

    void OnValidate()
    {
        timeScale = Mathf.Max(0f, timeScale);
        iterationsPerFrame = Mathf.Max(1, iterationsPerFrame);
        collisionDamping = Mathf.Clamp01(collisionDamping);
        smoothingRadius = Mathf.Max(0.001f, smoothingRadius);
        targetDensity = Mathf.Max(0.001f, targetDensity);
        pressureMultiplier = Mathf.Max(0f, pressureMultiplier);
        nearPressureMultiplier = Mathf.Max(0f, nearPressureMultiplier);
        viscosityStrength = Mathf.Max(0f, viscosityStrength);
        emissionRate = Mathf.Max(0f, emissionRate);
        exitSpeed = Mathf.Max(0f, exitSpeed);
        holeDiameter = Mathf.Max(0.001f, holeDiameter);
        currentParticleCount = GetParticleCount(particlePreset);
        estimatedGpuBufferMegabytes = EstimateBufferMegabytes(currentParticleCount);
        ApplyLegacyParticleVisibility();
    }

    void FixedUpdate()
    {
        // Run simulation if in fixed timestep mode
        if (fixedTimeStep)
        {
            RunSimulationFrame(Time.fixedDeltaTime);
        }
    }

    void Update()
    {
        // Run simulation if not in fixed timestep mode
        // (skip running for first few frames as timestep can be a lot higher than usual)
        if (!fixedTimeStep && Time.frameCount > 10)
        {
            RunSimulationFrame(Time.deltaTime);
        }

        if (pauseNextFrame)
        {
            isPaused = true;
            pauseNextFrame = false;
        }

        if (floorDisplay != null)
        {
            float safeYScale = Mathf.Max(0.001f, Mathf.Abs(transform.localScale.y));
            floorDisplay.transform.localScale = new Vector3(1, 1 / safeYScale * 0.1f, 1);
        }

        ApplyLegacyParticleVisibility();
    }

    void RunSimulationFrame(float frameTime)
    {
        if (!initialized || isPaused || iterationsPerFrame <= 0)
        {
            return;
        }

        float timeStep = frameTime / iterationsPerFrame * timeScale;
        UpdateSettings(timeStep);

        for (int i = 0; i < iterationsPerFrame; i++)
        {
            RunSimulationStep();
            SimulationStepCompleted?.Invoke();
        }
    }

    void RunSimulationStep()
    {
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: viscosityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionsKernel);

    }

    void UpdateSettings(float deltaTime)
    {
        Vector3 simBoundsSize = transform.localScale;
        Vector3 simBoundsCentre = transform.position;

        compute.SetFloat("deltaTime", deltaTime);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("targetDensity", targetDensity);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscosityStrength", viscosityStrength);
        compute.SetVector("boundsSize", simBoundsSize);
        compute.SetVector("centre", simBoundsCentre);

        compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
        compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);
    }

    void SetInitialBufferData(Spawner3D.SpawnData spawnData)
    {
        if (positionBuffer == null || predictedPositionsBuffer == null || velocityBuffer == null)
        {
            return;
        }

        float3[] allPoints = new float3[spawnData.points.Length];
        System.Array.Copy(spawnData.points, allPoints, spawnData.points.Length);

        positionBuffer.SetData(allPoints);
        predictedPositionsBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
    }

    public void ApplyPaintSettings(float newEmissionRate, float newExitSpeed, float newViscosity, float newHoleDiameter)
    {
        emissionRate = Mathf.Max(1f, newEmissionRate);
        exitSpeed = Mathf.Max(0f, newExitSpeed);
        viscosityStrength = Mathf.Max(0f, newViscosity);
        holeDiameter = Mathf.Max(0.001f, newHoleDiameter);

        timeScale = Mathf.Clamp(emissionRate / 120f, 0.1f, 3f);

        if (spawner != null)
        {
            ApplyPaintSpawnerSettings();
        }
    }

    public void ResetFluid()
    {
        if (spawner == null)
        {
            return;
        }

        ApplyPaintSpawnerSettings();
        spawnData = spawner.GetSpawnData();

        bool recreatedBuffers = positionBuffer == null || spawnData.points.Length != positionBuffer.count;
        if (recreatedBuffers)
        {
            CreateSimulationBuffers(spawnData.points.Length);
        }

        SetInitialBufferData(spawnData);

        if (recreatedBuffers && display != null)
        {
            display.Init(this);
            ApplyLegacyParticleVisibility();
        }
    }

    public void ApplyParticlePreset(ParticleCountPreset requestedPreset)
    {
        if (requestedPreset == ParticleCountPreset.Ultra && !CanUseUltraPreset(out string reason))
        {
            particlePresetWarning = "Ultra unavailable: " + reason + ". Falling back to High.";
            requestedPreset = ParticleCountPreset.High;
        }
        else
        {
            particlePresetWarning = "";
        }

        particlePreset = requestedPreset;
        int particleCount = GetParticleCount(particlePreset);
        ApplyParticleCount(particleCount);
    }

    public static int GetParticleCount(ParticleCountPreset preset)
    {
        switch (preset)
        {
            case ParticleCountPreset.Medium:
                return mediumParticleCount;
            case ParticleCountPreset.High:
                return highParticleCount;
            case ParticleCountPreset.Ultra:
                return ultraParticleCount;
            default:
                return lowParticleCount;
        }
    }

    public static float EstimateBufferMegabytes(int particleCount)
    {
        return particleCount * estimatedBytesPerParticle / (1024f * 1024f);
    }

    public bool CanUseUltraPreset(out string reason)
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            reason = "compute shaders are not supported";
            return false;
        }

        int graphicsMemoryMb = SystemInfo.graphicsMemorySize;
        float requiredMb = EstimateBufferMegabytes(ultraParticleCount);

        if (graphicsMemoryMb <= 0)
        {
            reason = "GPU memory size could not be detected";
            return false;
        }

        int recommendedMb = Mathf.Max(recommendedUltraGpuMemoryMb, Mathf.CeilToInt(requiredMb * 4f));
        if (graphicsMemoryMb < recommendedMb)
        {
            reason = "GPU memory is " + graphicsMemoryMb + " MB, recommended is at least " + recommendedMb + " MB";
            return false;
        }

        reason = "";
        return true;
    }

    void ApplyParticleCount(int particleCount)
    {
        if (spawner == null || compute == null || display == null)
        {
            return;
        }

        particleCount = Mathf.Clamp(particleCount, 2, ultraParticleCount);
        currentParticleCount = particleCount;
        estimatedGpuBufferMegabytes = EstimateBufferMegabytes(currentParticleCount);

        ApplyPaintSpawnerSettings();
        spawner.useTargetParticleCount = true;
        spawner.targetParticleCount = particleCount;
        spawnData = spawner.GetSpawnData();

        CreateSimulationBuffers(spawnData.points.Length);
        SetInitialBufferData(spawnData);

        if (display != null)
        {
            display.Init(this);
            ApplyLegacyParticleVisibility();
        }

        initialized = true;
    }

    void CreateSimulationBuffers(int numParticles)
    {
        ComputeHelper.Release(positionBuffer, predictedPositionsBuffer, velocityBuffer, densityBuffer, spatialIndices, spatialOffsets);

        positionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        predictedPositionsBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        velocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(numParticles);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);

        ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionsKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionsBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updatePositionsKernel);
        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionsKernel);

        compute.SetInt("numParticles", numParticles);

        gpuSort = new GPUSort();
        gpuSort.SetBuffers(spatialIndices, spatialOffsets);
    }

    public void SetPaused(bool paused)
    {
        isPaused = paused;
        pauseNextFrame = false;
    }

    public void SetLegacyFluidParticlesVisible(bool visible)
    {
        showLegacyFluidParticles = visible;
        ApplyLegacyParticleVisibility();
    }

    void ApplyLegacyParticleVisibility()
    {
        if (display != null)
        {
            display.showParticles = showLegacyFluidParticles;
            display.enabled = showLegacyFluidParticles;
        }
    }

    void ApplyPaintSpawnerSettings()
    {
        spawner.initialVel = new float3(0f, -exitSpeed, 0f);
        spawner.size = Mathf.Clamp(holeDiameter * 12.5f, 0.15f, 2.5f);
    }

    void OnDestroy()
    {
        ComputeHelper.Release(positionBuffer, predictedPositionsBuffer, velocityBuffer, densityBuffer, spatialIndices, spatialOffsets);
    }

    void OnDrawGizmos()
    {
        // Draw Bounds
        var m = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0, 1, 0, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = m;

    }
}
