using UnityEngine;

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
    }

    [Header("References")]
    public Transform exitPoint;
    public SphericalPendulumController pendulum;
    public PaintingSurface paintingSurface;

    [Header("Paint")]
    public Color paintColor = new Color(0.1f, 0.25f, 1f, 1f);
    public float initialPaintAmount = 1f;
    public float remainingPaintAmount = 1f;
    public float holeDiameter = 0.08f;
    public float viscosity = 0.5f;
    public float flowSpeed = 3f;
    public float humidity = 0.35f;

    [Header("Simulation")]
    public int maxParticles = 1500;
    public float particleMass = 0.00035f;
    public float particleLifetime = 5f;
    public float airDrag = 0.05f;
    public float gravity = 9.81f;
    public bool isPaused = true;

    [Header("Visual")]
    public float particleVisualScale = 0.035f;
    public Material particleMaterial;

    public float CurrentFlowRateKgPerSecond { get; private set; }
    public float PaintUsed => Mathf.Max(0f, initialPaintAmount - remainingPaintAmount);
    public int EmittedParticleCount { get; private set; }
    public int DepositedParticleCount { get; private set; }
    public int ActiveAirborneParticleCount { get; private set; }

    private PaintParticle[] particles;
    private float emissionAccumulator;
    private Vector3 previousExitPosition;
    private Mesh particleMesh;
    private Material runtimeParticleMaterial;
    private readonly Matrix4x4[] drawMatrices = new Matrix4x4[1023];

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

        if (exitPoint != null)
        {
            previousExitPosition = exitPoint.position;
        }

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
            runtimeParticleMaterial.color = paintColor;
        }
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
        if (remainingPaintAmount <= 0f || remainingPaintAmount > initialPaintAmount)
        {
            remainingPaintAmount = initialPaintAmount;
        }

        holeDiameter = Mathf.Max(0.001f, newHoleDiameter);
        viscosity = Mathf.Max(0.001f, newViscosity);
        flowSpeed = Mathf.Max(0f, newFlowSpeed);
        gravity = Mathf.Max(0f, newGravity);
        airDrag = Mathf.Max(0f, newAirDrag);
        humidity = Mathf.Clamp01(newHumidity);
        paintColor = newPaintColor;

        if (runtimeParticleMaterial != null)
        {
            runtimeParticleMaterial.color = paintColor;
        }
    }

    public void SetPaintAmount(float amount)
    {
        initialPaintAmount = Mathf.Max(0f, amount);
        remainingPaintAmount = initialPaintAmount;
        CurrentFlowRateKgPerSecond = 0f;
        emissionAccumulator = 0f;
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
        }

        if (resetPaintAmount)
        {
            remainingPaintAmount = initialPaintAmount;
        }

        EmittedParticleCount = 0;
        DepositedParticleCount = 0;
        ActiveAirborneParticleCount = 0;
        CurrentFlowRateKgPerSecond = 0f;
        emissionAccumulator = 0f;

        if (exitPoint != null)
        {
            previousExitPosition = exitPoint.position;
        }
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

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f)
        {
            return;
        }

        Vector3 exitVelocity = Vector3.zero;
        if (exitPoint != null)
        {
            exitVelocity = (exitPoint.position - previousExitPosition) / dt;
            previousExitPosition = exitPoint.position;
        }

        if (!isPaused)
        {
            Emit(dt, exitVelocity);
            SimulateParticles(dt);
        }

        DrawParticles();
    }

    private void Emit(float dt, Vector3 exitVelocity)
    {
        CurrentFlowRateKgPerSecond = CalculateFlowRate(exitVelocity.magnitude);
        if (exitPoint == null || remainingPaintAmount <= 0f || CurrentFlowRateKgPerSecond <= 0f)
        {
            CurrentFlowRateKgPerSecond = 0f;
            return;
        }

        float paintToUse = Mathf.Min(remainingPaintAmount, CurrentFlowRateKgPerSecond * dt);
        remainingPaintAmount -= paintToUse;

        float particlesToEmit = paintToUse / Mathf.Max(0.00001f, particleMass);
        emissionAccumulator += particlesToEmit;
        int emitCount = Mathf.FloorToInt(emissionAccumulator);
        emissionAccumulator -= emitCount;
        emitCount = Mathf.Min(emitCount, Mathf.CeilToInt(maxParticles * 0.2f));

        for (int i = 0; i < emitCount; i++)
        {
            SpawnParticle(exitVelocity);
        }
    }

    private float CalculateFlowRate(float bucketSpeed)
    {
        float diameter = Mathf.Max(0.001f, holeDiameter);
        float area = Mathf.PI * diameter * diameter * 0.25f;
        float gravityFactor = Mathf.Sqrt(Mathf.Max(0.001f, gravity) / 9.81f);
        float viscosityFactor = 1f / (1f + viscosity * 2.5f);
        float remainingFraction = Mathf.Clamp01(remainingPaintAmount / Mathf.Max(0.001f, initialPaintAmount));
        float paintHeadFactor = Mathf.SmoothStep(0.15f, 1f, remainingFraction);
        float motionFactor = 1f + bucketSpeed * 0.08f;
        float humidityFactor = Mathf.Lerp(1.05f, 0.75f, humidity);

        return area * 35f * Mathf.Max(0.1f, flowSpeed) * gravityFactor * viscosityFactor * paintHeadFactor * motionFactor * humidityFactor;
    }

    private void SpawnParticle(Vector3 exitVelocity)
    {
        int index = FindFreeParticleIndex();
        if (index < 0)
        {
            return;
        }

        Vector3 randomOffset = Random.insideUnitSphere * holeDiameter * 0.25f;
        Vector3 downward = exitPoint.TransformDirection(Vector3.down);
        Vector3 tangentJitter = Random.insideUnitSphere * holeDiameter * 2f;

        particles[index].active = true;
        particles[index].position = exitPoint.position + randomOffset;
        particles[index].previousPosition = particles[index].position;
        particles[index].velocity = downward.normalized * flowSpeed + exitVelocity * 0.8f + tangentJitter;
        particles[index].age = 0f;
        particles[index].deposited = false;
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

            bool painted = false;
            if (paintingSurface != null)
            {
                painted = paintingSurface.TryPaintSegment(
                    particle.previousPosition,
                    particle.position,
                    paintColor,
                    paintingSurface.brushRadius,
                    paintingSurface.opacity,
                    particle.velocity.magnitude,
                    viscosity,
                    holeDiameter,
                    flowSpeed
                );
            }

            if (painted)
            {
                particle.deposited = true;
                DepositedParticleCount++;
            }

            bool expired = particle.age >= particleLifetime || particle.position.y < -10f || particle.deposited;
            particles[i] = particle;

            if (expired)
            {
                particles[i].active = false;
            }
        }
    }

    private void DrawParticles()
    {
        ActiveAirborneParticleCount = 0;

        if (particleMesh == null || runtimeParticleMaterial == null || particles == null)
        {
            return;
        }

        int batchCount = 0;
        int activeCount = 0;
        Vector3 scale = Vector3.one * particleVisualScale;

        for (int i = 0; i < particles.Length; i++)
        {
            if (!particles[i].active)
            {
                continue;
            }

            activeCount++;
            drawMatrices[batchCount] = Matrix4x4.TRS(particles[i].position, Quaternion.identity, scale);
            batchCount++;

            if (batchCount == drawMatrices.Length)
            {
                Graphics.DrawMeshInstanced(particleMesh, 0, runtimeParticleMaterial, drawMatrices, batchCount);
                batchCount = 0;
            }
        }

        if (batchCount > 0)
        {
            Graphics.DrawMeshInstanced(particleMesh, 0, runtimeParticleMaterial, drawMatrices, batchCount);
        }

        ActiveAirborneParticleCount = activeCount;
    }

    private void ValidateSettings()
    {
        maxParticles = Mathf.Clamp(maxParticles, 32, 8000);
        particleMass = Mathf.Max(0.00001f, particleMass);
        particleLifetime = Mathf.Max(0.1f, particleLifetime);
        particleVisualScale = Mathf.Max(0.001f, particleVisualScale);
        initialPaintAmount = Mathf.Max(0f, initialPaintAmount);
        remainingPaintAmount = Mathf.Clamp(remainingPaintAmount <= 0f ? initialPaintAmount : remainingPaintAmount, 0f, initialPaintAmount);
        holeDiameter = Mathf.Max(0.001f, holeDiameter);
        viscosity = Mathf.Max(0.001f, viscosity);
        flowSpeed = Mathf.Max(0f, flowSpeed);
        airDrag = Mathf.Max(0f, airDrag);
        gravity = Mathf.Max(0f, gravity);
        humidity = Mathf.Clamp01(humidity);
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
    }
}
