using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PaintParticleEmitter : MonoBehaviour
{
    private struct PaintParticle
    {
        public bool active;
        public Vector3 position;
        public Vector3 previousPosition;
        public Vector3 velocity;
        public float age;
        public bool deposited;
        public int streamId;
        public Color color;
    }

    [Header("References")]
    public Transform exitPoint;
    public SphericalPendulumController pendulum;
    public PaintingSurface paintingSurface;
    public BucketPaintReservoir paintReservoir;

    [Header("Paint")]
    public Color paintColor = new Color(0.1f, 0.25f, 1f, 1f);
    public float initialPaintAmount = 20f;
    public float remainingPaintAmount = 20f;
    public float holeDiameter = 0.01f;
    public float viscosity = 0.65f;
    public float flowSpeed = 1.8f;
    public float flowMultiplier = 15f;
    public float humidity = 0.35f;

    [Header("Simulation")]
    public int maxParticles = 1500;
    public float particleMass = 0.00035f;
    public float particleLifetime = 5f;
    public float airDrag = 0.05f;
    public float gravity = 9.81f;
    public bool isPaused = true;
    public bool emissionEnabled = true;
    public int maxDepositsPerFrame = 80;
    public float maxParticlesPerSecond = 240f;
    public float particleEmissionScale = 16000f;

    [Header("Trail Test Mode")]
    public bool deterministicTrailTestMode = false;
    public float testPathWidth = 4f;
    public float testPathDepth = 1.4f;
    public float testPathHeight = 3f;
    public float testPathSpeed = 1.1f;

    [Header("Visual")]
    public float particleVisualScale = 0.015f;
    public float airborneParticleSize = 0.015f;
    public bool showAirborneParticles = true;
    public Material particleMaterial;

    public float CurrentFlowRateKgPerSecond { get; private set; }
    public float LastEmittedMassThisFrame { get; private set; }
    public float PaintUsed => Mathf.Max(0f, initialPaintAmount - remainingPaintAmount);
    public int EmittedParticleCount { get; private set; }
    public int DepositedParticleCount { get; private set; }
    public int DepositedThisFrameCount { get; private set; }
    public int ActiveAirborneParticleCount { get; private set; }
    public int RecycledParticleCount { get; private set; }
    public int MissedBoardParticleCount { get; private set; }
    public int TotalCollisionCount { get; private set; }
    public int RecycledAfterHitCount { get; private set; }
    public float CurrentParticlesPerSecond { get; private set; }
    public string ActiveDepositMode => paintingSurface != null ? paintingSurface.ActiveTrailMode.ToString() : "No Surface";
    public string LastDepositModeUsed => paintingSurface != null ? paintingSurface.LastDepositModeUsed : "None";
    public bool HasPaintingSurface => paintingSurface != null;
    public bool LastStreamClippedByBoard { get; private set; }
    public bool IsBucketEmpty => remainingPaintAmount <= 0.0001f;
    public string BucketState
    {
        get
        {
            if (IsBucketEmpty) return "Empty";
            float fraction = Mathf.Clamp01(remainingPaintAmount / Mathf.Max(0.001f, initialPaintAmount));
            if (CurrentFlowRateKgPerSecond > 0f && !isPaused) return fraction < 0.18f ? "Low Paint" : "Flowing";
            return fraction < 0.18f ? "Low Paint" : "Full";
        }
    }
    public string UsedPaintColorSummary => BuildUsedPaintColorSummary();

    [Header("Debug")]
    public bool debugPaintAmountSync = true;
    public float debugPaintAmountLogInterval = 0.5f;

    private PaintParticle[] particles;
    private float emissionAccumulator;
    private Vector3 previousExitPosition;
    private Vector3 testExitPosition;
    private Vector3 previousTestExitPosition;
    private int streamBaseId;
    private const int StreamLaneCount = 16;
    private Mesh particleMesh;
    private Material runtimeParticleMaterial;
    private GameObject hitWorldMarker;
    private bool warnedMissingPaintingSurface;
    private float nextPaintAmountDebugLogTime;
    private readonly Matrix4x4[] drawMatrices = new Matrix4x4[1023];
    private readonly List<Color> usedPaintColors = new List<Color>();

    private void Awake()
    {
        Initialize();
    }

    private void OnValidate()
    {
        ValidateSettings();
    }

    public void Initialize()
    {
        ValidateSettings();
        particles = new PaintParticle[maxParticles];
        EnsurePaintingSurfaceReference();

        if (exitPoint != null)
        {
            previousExitPosition = exitPoint.position;
        }

        previousTestExitPosition = GetDeterministicTestExitPosition(0f);
        testExitPosition = previousTestExitPosition;

        if (particleMesh == null)
        {
            particleMesh = SebStuff.SphereGenerator.GenerateSphereMesh(0);
        }

        if (runtimeParticleMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (particleMaterial != null)
            {
                runtimeParticleMaterial = new Material(particleMaterial);
            }
            else if (shader != null)
            {
                runtimeParticleMaterial = new Material(shader);
            }
            else
            {
                Debug.LogWarning("PaintParticleEmitter could not find a particle shader. Particles will still paint, but will not be drawn in the air.");
            }
        }

        if (runtimeParticleMaterial != null)
        {
            runtimeParticleMaterial.enableInstancing = true;
            SetRuntimeParticleColor(paintColor);
        }

        RegisterPaintColor(paintColor);
    }

    public void ApplySettings(
        float newInitialPaintAmount,
        float newHoleDiameter,
        float newViscosity,
        float newFlowSpeed,
        float newGravity,
        float newAirDrag,
        float newHumidity,
        Color newPaintColor
    )
    {
        initialPaintAmount = Mathf.Max(0f, newInitialPaintAmount);
        bool runtimeActive = Application.isPlaying && !isPaused;
        if (!runtimeActive && initialPaintAmount <= 0f)
        {
            remainingPaintAmount = 0f;
        }
        else if (!runtimeActive && remainingPaintAmount > initialPaintAmount)
        {
            remainingPaintAmount = initialPaintAmount;
        }

        holeDiameter = Mathf.Max(0.001f, newHoleDiameter);
        viscosity = Mathf.Max(0.001f, newViscosity);
        flowSpeed = Mathf.Max(0f, newFlowSpeed);
        gravity = Mathf.Max(0f, newGravity);
        airDrag = Mathf.Max(0f, newAirDrag);
        humidity = Mathf.Clamp01(newHumidity);
        flowMultiplier = Mathf.Clamp(flowMultiplier <= 0f ? 15f : flowMultiplier, 1f, 120f);
        paintColor = newPaintColor;

        if (runtimeParticleMaterial != null)
        {
            runtimeParticleMaterial.enableInstancing = true;
            SetRuntimeParticleColor(paintColor);
        }

        RegisterPaintColor(paintColor);
    }

    public void SetPaintAmount(float amount)
    {
        SetInitialPaintAmount(amount, refillRemaining: true);
        CurrentFlowRateKgPerSecond = 0f;
        CurrentParticlesPerSecond = 0f;
        emissionAccumulator = 0f;
    }

    public void SetInitialPaintAmount(float amount, bool refillRemaining)
    {
        initialPaintAmount = Mathf.Max(0f, amount);
        if (refillRemaining)
        {
            remainingPaintAmount = initialPaintAmount;
        }
        else if (!Application.isPlaying)
        {
            remainingPaintAmount = Mathf.Clamp(remainingPaintAmount, 0f, initialPaintAmount);
        }
    }

    public void SetRemainingPaintAmount(float amount)
    {
        remainingPaintAmount = Mathf.Clamp(amount, 0f, initialPaintAmount);
        if (IsBucketEmpty)
        {
            CurrentFlowRateKgPerSecond = 0f;
            emissionAccumulator = 0f;
        }
    }

    public void ResetEmitter(bool resetPaintAmount)
    {
        if (particles == null || particles.Length != maxParticles)
        {
            particles = new PaintParticle[maxParticles];
        }

        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].active = false;
            particles[i].deposited = false;
            particles[i].streamId = 0;
            particles[i].color = paintColor;
        }

        if (resetPaintAmount)
        {
            remainingPaintAmount = initialPaintAmount;
        }

        LastEmittedMassThisFrame = 0f;
        EmittedParticleCount = 0;
        DepositedParticleCount = 0;
        DepositedThisFrameCount = 0;
        ActiveAirborneParticleCount = 0;
        RecycledParticleCount = 0;
        MissedBoardParticleCount = 0;
        TotalCollisionCount = 0;
        RecycledAfterHitCount = 0;
        LastStreamClippedByBoard = false;
        CurrentFlowRateKgPerSecond = 0f;
        CurrentParticlesPerSecond = 0f;
        emissionAccumulator = 0f;
        emissionEnabled = true;
        RegisterPaintColor(paintColor);

        if (exitPoint != null)
        {
            previousExitPosition = exitPoint.position;
        }

        previousTestExitPosition = GetDeterministicTestExitPosition(Time.unscaledTime);
        testExitPosition = previousTestExitPosition;
        streamBaseId += StreamLaneCount;
    }

    public void SetPaused(bool paused)
    {
        isPaused = paused;
    }

    private void Update()
    {
        if (particles == null)
        {
            Initialize();
        }

        EnsurePaintingSurfaceReference();

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f)
        {
            return;
        }

        if ((paintingSurface == null || !paintingSurface.showMappingDebugMarkers) && hitWorldMarker != null)
        {
            hitWorldMarker.SetActive(false);
        }

        Vector3 currentExitPosition = GetCurrentExitPosition();
        Vector3 exitVelocity = Vector3.zero;
        if (deterministicTrailTestMode)
        {
            exitVelocity = (currentExitPosition - previousTestExitPosition) / dt;
            previousTestExitPosition = currentExitPosition;
            testExitPosition = currentExitPosition;
        }
        else if (exitPoint != null)
        {
            exitVelocity = (currentExitPosition - previousExitPosition) / dt;
            previousExitPosition = currentExitPosition;
        }

        if (!isPaused)
        {
            DepositedThisFrameCount = 0;
            Emit(dt, exitVelocity);
            SimulateParticles(dt);
            if (paintingSurface != null)
            {
                paintingSurface.FlushTextureUpdates();
            }
        }
        else
        {
            DepositedThisFrameCount = 0;
        }

        DrawParticles();
    }

    private void Emit(float dt, Vector3 exitVelocity)
    {
        LastEmittedMassThisFrame = 0f;
        CurrentFlowRateKgPerSecond = CalculateFlowRate(exitVelocity.magnitude);
        CurrentParticlesPerSecond = CalculateParticlesPerSecond(exitVelocity.magnitude);
        if (!emissionEnabled || (!deterministicTrailTestMode && exitPoint == null) || remainingPaintAmount <= 0f || CurrentFlowRateKgPerSecond <= 0f)
        {
            CurrentFlowRateKgPerSecond = 0f;
            CurrentParticlesPerSecond = 0f;
            remainingPaintAmount = Mathf.Max(0f, remainingPaintAmount);
            return;
        }

        float paintToUse = Mathf.Min(remainingPaintAmount, CurrentFlowRateKgPerSecond * dt);
        remainingPaintAmount = Mathf.Max(0f, remainingPaintAmount - paintToUse);
        LastEmittedMassThisFrame = paintToUse;
        LogPaintAmountDebugIfNeeded();

        float particlesToEmit = CurrentParticlesPerSecond * dt;
        emissionAccumulator += particlesToEmit;
        int emitCount = Mathf.FloorToInt(emissionAccumulator);
        emissionAccumulator -= emitCount;
        int frameEmissionLimit = Mathf.Min(Mathf.CeilToInt(maxParticlesPerSecond * dt) + 1, Mathf.CeilToInt(maxParticles * 0.08f));
        emitCount = Mathf.Min(emitCount, frameEmissionLimit);

        for (int i = 0; i < emitCount; i++)
        {
            SpawnParticle(exitVelocity, i);
        }
    }

    private float CalculateFlowRate(float bucketSpeed)
    {
        float rawFlow = CalculateRawFlow(bucketSpeed);
        float remainingFraction = Mathf.Clamp01(remainingPaintAmount / Mathf.Max(0.001f, initialPaintAmount));
        float paintHeadFactor = Mathf.SmoothStep(0.12f, 1f, remainingFraction);
        return rawFlow * paintHeadFactor;
    }

    private void LogPaintAmountDebugIfNeeded()
    {
        if (!debugPaintAmountSync || !Application.isPlaying || Time.unscaledTime < nextPaintAmountDebugLogTime)
        {
            return;
        }

        nextPaintAmountDebugLogTime = Time.unscaledTime + Mathf.Max(0.05f, debugPaintAmountLogInterval);
        float reservoirAmount = paintReservoir != null ? paintReservoir.currentPaintAmount : -1f;
        Debug.Log(
            "[PaintAmountSync] emittedMassThisFrame=" + LastEmittedMassThisFrame.ToString("0.000000") +
            " kg, remainingPaintAmount=" + remainingPaintAmount.ToString("0.000000") +
            " kg, sliderPaintAmount=" + initialPaintAmount.ToString("0.000000") +
            " kg, reservoirCurrentAmount=" + reservoirAmount.ToString("0.000000") + " kg"
        );
    }

    private float CalculateParticlesPerSecond(float bucketSpeed)
    {
        float rawFlow = CalculateRawFlow(bucketSpeed);
        return Mathf.Clamp(rawFlow * Mathf.Max(1f, particleEmissionScale), 1f, Mathf.Max(1f, maxParticlesPerSecond));
    }

    private float CalculateRawFlow(float bucketSpeed)
    {
        float holeRadius = Mathf.Max(0.0005f, holeDiameter * 0.5f);
        float holeArea = Mathf.PI * holeRadius * holeRadius;
        float viscosityResistance = Mathf.Lerp(1.2f, 0.25f, Mathf.Clamp01(viscosity));
        float motionFactor = 1f + Mathf.Clamp(bucketSpeed, 0f, 8f) * 0.04f;
        float humidityFactor = Mathf.Lerp(1.05f, 0.78f, humidity);
        return holeArea * Mathf.Max(0.05f, flowSpeed) * viscosityResistance * Mathf.Clamp(flowMultiplier, 1f, 120f) * motionFactor * humidityFactor;
    }

    private void SpawnParticle(Vector3 exitVelocity, int emissionIndex)
    {
        int index = FindFreeParticleIndex();
        if (index < 0)
        {
            return;
        }

        bool ribbonMode = paintingSurface != null && paintingSurface.ActiveTrailMode == PaintingSurface.TrailRenderMode.Ribbon;
        float offsetScale = ribbonMode || deterministicTrailTestMode ? 0.12f : 0.25f;
        float jitterScale = ribbonMode || deterministicTrailTestMode ? 0.45f : 2f;
        Vector3 randomOffset = Random.insideUnitSphere * holeDiameter * offsetScale;
        Vector3 downward = deterministicTrailTestMode || exitPoint == null ? Vector3.down : exitPoint.TransformDirection(Vector3.down);
        Vector3 tangentJitter = Random.insideUnitSphere * holeDiameter * jitterScale;
        Vector3 spawnPosition = GetCurrentExitPosition();
        int lane = Mathf.Abs(EmittedParticleCount + emissionIndex) % StreamLaneCount;

        particles[index].active = true;
        particles[index].position = spawnPosition + randomOffset;
        particles[index].previousPosition = particles[index].position;
        particles[index].velocity = downward.normalized * flowSpeed + exitVelocity * 0.8f + tangentJitter;
        particles[index].age = 0f;
        particles[index].deposited = false;
        particles[index].streamId = streamBaseId + lane;
        Color emittedColor = paintReservoir != null ? paintReservoir.GetEmissionColor(EmittedParticleCount) : paintColor;
        particles[index].color = emittedColor;
        RegisterPaintColor(emittedColor);
        EmittedParticleCount++;
    }

    private int FindFreeParticleIndex()
    {
        for (int i = 0; i < particles.Length; i++)
        {
            if (!particles[i].active)
            {
                return i;
            }
        }

        return -1;
    }

    private void SimulateParticles(float dt)
    {
        for (int i = 0; i < particles.Length; i++)
        {
            if (!particles[i].active)
            {
                continue;
            }

            PaintParticle particle = particles[i];
            if (particle.deposited)
            {
                particle.active = false;
                particles[i] = particle;
                continue;
            }

            particle.previousPosition = particle.position;
            particle.velocity += Vector3.down * gravity * dt;
            particle.velocity *= Mathf.Exp(-(airDrag + humidity * 0.08f) * dt);
            particle.position += particle.velocity * dt;
            particle.age += dt;

            bool hitBoardPlane = false;
            bool hitInsideBoard = false;
            bool painted = false;
            Vector3 worldHit = Vector3.zero;
            if (paintingSurface != null)
            {
                hitBoardPlane = paintingSurface.TryGetSegmentHit(
                    particle.previousPosition,
                    particle.position,
                    out worldHit,
                    out _,
                    out _,
                    out _,
                    out hitInsideBoard
                );

                if (hitBoardPlane && !hitInsideBoard)
                {
                    MissedBoardParticleCount++;
                    LastStreamClippedByBoard = true;
                    particles[i] = particle;
                    RecycleParticle(i);
                    continue;
                }

                if (hitBoardPlane)
                {
                    TotalCollisionCount++;
                    LastStreamClippedByBoard = true;
                    DrawHitWorldMarker(worldHit);
                    painted = paintingSurface.DepositSegmentHit(
                        particle.previousPosition,
                        particle.position,
                        worldHit,
                        particle.color,
                        paintingSurface.brushRadius,
                        paintingSurface.opacity,
                        particle.velocity.magnitude,
                        viscosity,
                        holeDiameter,
                        flowSpeed,
                        gravity,
                        particle.streamId,
                        particle.velocity
                    );
                }
            }
            else
            {
                LastStreamClippedByBoard = false;
            }

            if (painted)
            {
                particle.deposited = true;
                DepositedParticleCount++;
                DepositedThisFrameCount++;
            }

            if (hitBoardPlane && hitInsideBoard)
            {
                particle.position = worldHit;
                particles[i] = particle;
                RecycleParticle(i);
                RecycledAfterHitCount++;
                continue;
            }

            bool expired = particle.age >= particleLifetime || particle.position.y < -10f || particle.deposited;
            particles[i] = particle;

            if (expired)
            {
                RecycleParticle(i);
            }
        }
    }

    private void DrawParticles()
    {
        ActiveAirborneParticleCount = 0;

        if (particles == null)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i].active)
            {
                ActiveAirborneParticleCount++;
            }
        }

        if (!showAirborneParticles || particleMesh == null || runtimeParticleMaterial == null)
        {
            return;
        }

        runtimeParticleMaterial.enableInstancing = true;

        int batchCount = 0;
        Vector3 scale = Vector3.one * airborneParticleSize;
        Color batchColor = Color.clear;

        for (int i = 0; i < particles.Length; i++)
        {
            if (!particles[i].active)
            {
                continue;
            }

            if (batchCount > 0 && !SameColor(batchColor, particles[i].color))
            {
                SetRuntimeParticleColor(batchColor);
                Graphics.DrawMeshInstanced(particleMesh, 0, runtimeParticleMaterial, drawMatrices, batchCount);
                batchCount = 0;
            }

            if (batchCount == 0)
            {
                batchColor = particles[i].color;
            }

            drawMatrices[batchCount] = Matrix4x4.TRS(particles[i].position, Quaternion.identity, scale);
            batchCount++;

            if (batchCount == drawMatrices.Length)
            {
                SetRuntimeParticleColor(batchColor);
                Graphics.DrawMeshInstanced(particleMesh, 0, runtimeParticleMaterial, drawMatrices, batchCount);
                batchCount = 0;
            }
        }

        if (batchCount > 0)
        {
            SetRuntimeParticleColor(batchColor);
            Graphics.DrawMeshInstanced(particleMesh, 0, runtimeParticleMaterial, drawMatrices, batchCount);
        }

    }

    private void RecycleParticle(int index)
    {
        if (particles == null || index < 0 || index >= particles.Length || !particles[index].active)
        {
            return;
        }

        particles[index].active = false;
        RecycledParticleCount++;
    }

    private void ValidateSettings()
    {
        maxParticles = Mathf.Clamp(maxParticles, 32, 8000);
        particleMass = Mathf.Max(0.00001f, particleMass);
        particleLifetime = Mathf.Max(0.1f, particleLifetime);
        particleVisualScale = Mathf.Max(0.001f, particleVisualScale);
        airborneParticleSize = Mathf.Clamp(airborneParticleSize <= 0f ? particleVisualScale : airborneParticleSize, 0.005f, 0.08f);
        initialPaintAmount = Mathf.Max(0f, initialPaintAmount);
        remainingPaintAmount = Mathf.Clamp(remainingPaintAmount, 0f, initialPaintAmount);
        holeDiameter = Mathf.Max(0.001f, holeDiameter);
        viscosity = Mathf.Max(0.001f, viscosity);
        flowSpeed = Mathf.Max(0f, flowSpeed);
        flowMultiplier = Mathf.Clamp(flowMultiplier <= 0f ? 15f : flowMultiplier, 1f, 120f);
        maxDepositsPerFrame = Mathf.Clamp(maxDepositsPerFrame <= 0 ? 80 : maxDepositsPerFrame, 1, 500);
        maxParticlesPerSecond = Mathf.Clamp(maxParticlesPerSecond <= 0f ? 240f : maxParticlesPerSecond, 1f, 1000f);
        particleEmissionScale = Mathf.Clamp(particleEmissionScale <= 0f ? 16000f : particleEmissionScale, 1f, 100000f);
        airDrag = Mathf.Max(0f, airDrag);
        gravity = Mathf.Max(0f, gravity);
        humidity = Mathf.Clamp01(humidity);
        testPathWidth = Mathf.Max(0.1f, testPathWidth);
        testPathDepth = Mathf.Max(0.1f, testPathDepth);
        testPathHeight = Mathf.Max(0.1f, testPathHeight);
        testPathSpeed = Mathf.Max(0.01f, testPathSpeed);
    }

    private void EnsurePaintingSurfaceReference()
    {
        if (paintingSurface != null)
        {
            warnedMissingPaintingSurface = false;
            return;
        }

        paintingSurface = FindFirstObjectByType<PaintingSurface>();
        if (paintingSurface != null)
        {
            warnedMissingPaintingSurface = false;
            return;
        }

        if (!warnedMissingPaintingSurface)
        {
            Debug.LogWarning("PaintingSurface missing - paint cannot collide.");
            warnedMissingPaintingSurface = true;
        }
    }

    private void SetRuntimeParticleColor(Color color)
    {
        if (runtimeParticleMaterial == null)
        {
            return;
        }

        runtimeParticleMaterial.color = color;
        if (runtimeParticleMaterial.HasProperty("_BaseColor"))
        {
            runtimeParticleMaterial.SetColor("_BaseColor", color);
        }
        if (runtimeParticleMaterial.HasProperty("_Color"))
        {
            runtimeParticleMaterial.SetColor("_Color", color);
        }
    }

    private void RegisterPaintColor(Color color)
    {
        for (int i = 0; i < usedPaintColors.Count; i++)
        {
            if (SameColor(usedPaintColors[i], color))
            {
                return;
            }
        }

        usedPaintColors.Add(color);
    }

    private string BuildUsedPaintColorSummary()
    {
        if (usedPaintColors.Count == 0)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(paintColor);
        }

        string summary = "";
        for (int i = 0; i < usedPaintColors.Count; i++)
        {
            if (i > 0)
            {
                summary += ", ";
            }

            summary += "#" + ColorUtility.ToHtmlStringRGB(usedPaintColors[i]);
        }

        return summary;
    }

    private static bool SameColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.002f &&
            Mathf.Abs(a.g - b.g) < 0.002f &&
            Mathf.Abs(a.b - b.b) < 0.002f &&
            Mathf.Abs(a.a - b.a) < 0.002f;
    }

    private Vector3 GetCurrentExitPosition()
    {
        if (deterministicTrailTestMode)
        {
            return GetDeterministicTestExitPosition(Time.unscaledTime);
        }

        return exitPoint != null ? exitPoint.position : transform.position;
    }

    private void DrawHitWorldMarker(Vector3 position)
    {
        if (paintingSurface == null || !paintingSurface.showMappingDebugMarkers)
        {
            if (hitWorldMarker != null)
            {
                hitWorldMarker.SetActive(false);
            }
            return;
        }

        if (hitWorldMarker == null)
        {
            hitWorldMarker = CreateDebugRingCross("PaintHitWorldMarker", Color.red);
        }

        float markerSize = paintingSurface != null ? paintingSurface.mappingDebugMarkerSize : 0.12f;
        Vector3 markerPosition = position + paintingSurface.BoardNormal * paintingSurface.mappingDebugMarkerOffset;
        hitWorldMarker.transform.position = markerPosition;
        hitWorldMarker.transform.rotation = Quaternion.identity;
        hitWorldMarker.transform.localScale = Vector3.one * markerSize;
        hitWorldMarker.SetActive(true);
    }

    private static GameObject CreateDebugRingCross(string markerName, Color color)
    {
        GameObject marker = new GameObject(markerName);
        Material material = CreateDebugMarkerMaterial(color);

        Vector3[][] linePoints =
        {
            new[] { Vector3.left, Vector3.right },
            new[] { Vector3.down, Vector3.up },
            new[] { Vector3.back, Vector3.forward }
        };

        for (int i = 0; i < linePoints.Length; i++)
        {
            CreateDebugLine(marker.transform, "Cross" + i, linePoints[i], color, material, false);
        }

        CreateDebugRing(marker.transform, "RingXY", Vector3.forward, color, material);
        CreateDebugRing(marker.transform, "RingXZ", Vector3.up, color, material);

        marker.SetActive(false);
        return marker;
    }

    private static void CreateDebugRing(Transform parent, string name, Vector3 normal, Color color, Material material)
    {
        const int segments = 40;
        Vector3 axisA = normal == Vector3.up ? Vector3.right : Vector3.up;
        Vector3 axisB = Vector3.Cross(normal, axisA).normalized;
        Vector3[] points = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            points[i] = (Mathf.Cos(angle) * axisA + Mathf.Sin(angle) * axisB) * 0.82f;
        }

        CreateDebugLine(parent, name, points, color, material, true);
    }

    private static void CreateDebugLine(Transform parent, string name, Vector3[] points, Color color, Material material, bool loop)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(parent, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = points.Length;
        for (int i = 0; i < points.Length; i++)
        {
            line.SetPosition(i, points[i]);
        }
        line.startWidth = 0.08f;
        line.endWidth = 0.08f;
        line.startColor = color;
        line.endColor = color;
        line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = 32767;
    }

    private static Material CreateDebugMarkerMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        Material material = shader != null ? new Material(shader) : null;
        if (material == null)
        {
            return null;
        }

        material.color = color;
        material.renderQueue = 5000;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        }
        return material;
    }

    private Vector3 GetDeterministicTestExitPosition(float time)
    {
        Vector3 center = paintingSurface != null
            ? paintingSurface.transform.TransformPoint(new Vector3(0f, testPathHeight, 0f))
            : transform.position + Vector3.up * testPathHeight;

        float phase = time * testPathSpeed;
        Vector3 localPath = new Vector3(
            Mathf.Sin(phase) * testPathWidth * 0.5f,
            0f,
            Mathf.Sin(phase * 0.5f + 0.7f) * testPathDepth * 0.5f
        );

        return paintingSurface != null
            ? paintingSurface.transform.TransformPoint(new Vector3(localPath.x, testPathHeight, localPath.z))
            : center + localPath;
    }

    private void OnDestroy()
    {
        if (runtimeParticleMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(runtimeParticleMaterial);
            }
            else
            {
                DestroyImmediate(runtimeParticleMaterial);
            }
        }

        if (particleMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(particleMesh);
            }
            else
            {
                DestroyImmediate(particleMesh);
            }
        }

        if (hitWorldMarker != null)
        {
            if (Application.isPlaying)
            {
                Destroy(hitWorldMarker);
            }
            else
            {
                DestroyImmediate(hitWorldMarker);
            }
        }
    }
}
