using UnityEngine;

[ExecuteAlways]
public class SphericalPendulumController : MonoBehaviour
{
    [Header("Mass Settings")]
    [Tooltip("Empty bucket mass in kilograms.")]
    public float bucketEmptyMass = 2.0f;

    [Tooltip("Paint mass currently inside the bucket in kilograms.")]
    public float paintMass = 1.0f;

    [Tooltip("Paint mass loss rate in kg/s.")]
    public float paintMassFlowRate = 0.02f;

    [Tooltip("When enabled, paint mass decreases over time.")]
    public bool simulatePaintLoss = true;

    [Header("Pendulum Physical Settings")]
    public float ropeLength = 4.0f;
    public float gravity = 9.81f;

    [Header("Initial State - Radians")]
    public float thetaDot = 0.0f;

    [Tooltip("0 means released from rest. Use 0.1 - 0.25 for a slight 3D side push.")]
    public float phiDot = 0.0f;

    [Tooltip("Initial displacement angle in radians. Example: 0.55 rad is about 31.5 degrees.")]
    public float theta = 0.55f;

    public float phi = 0.0f;

    [Header("Non-Ideal Damping")]
    [Tooltip("Base damping. A small value such as 0.003 to 0.01 usually works well.")]
    public float damping = 0.005f;

    [Tooltip("Air resistance. The effect becomes clearer as mass decreases.")]
    public float airResistanceCoefficient = 0.02f;

    [Tooltip("Pivot friction. Its effect depends on mass and rope length.")]
    public float pivotFrictionCoefficient = 0.03f;

    [Header("Paint Sloshing Inside Bucket")]
    public bool enablePaintSloshing = true;
    public float sloshingStrength = 0.06f;
    public float sloshingStiffness = 3.0f;
    public float sloshingDamping = 0.8f;
    public float sloshingMotionCoupling = 0.35f;

    [Header("Simulation Settings")]
    [Tooltip("Fallback step used if Unity's fixed delta time is unavailable.")]
    public float deltaT = 0.02f;

    [Header("References")]
    public Transform pivotPoint;
    public Transform ropeAttachPoint;

    [Header("Bucket Tilt")]
    public bool tiltBucketWithRope = true;

    [Range(0f, 1f)]
    public float tiltAmount = 1.0f;

    [Header("Debug - Motion")]
    public Vector3 attachPointVelocity;

    [Header("Debug - Physical Outputs")]
    public float totalMass;
    public float effectiveDamping;
    public float ropeTension;
    public float kineticEnergy;
    public float potentialEnergy;

    [Header("Debug - Sloshing")]
    public float sloshThetaOffset;
    public float sloshPhiOffset;

    private Vector4 state;
    private Vector3 origin;
    private Vector3 attachLocalOffset;
    private Vector3 previousAttachPosition;
    private float time;
    private bool initialized;

    private float initialPaintMass;
    private float sloshThetaVelocity;
    private float sloshPhiVelocity;

    public float InitialPaintMass => Mathf.Max(0.001f, initialPaintMass);
    public float PaintRemainingFraction => Mathf.Clamp01(paintMass / InitialPaintMass);

    private void OnValidate()
    {
        bucketEmptyMass = Mathf.Max(0.001f, bucketEmptyMass);
        paintMass = Mathf.Max(0f, paintMass);
        paintMassFlowRate = Mathf.Max(0f, paintMassFlowRate);
        ropeLength = Mathf.Max(0.001f, ropeLength);
        gravity = Mathf.Max(0f, gravity);
        deltaT = Mathf.Max(0.001f, deltaT);
        damping = Mathf.Max(0f, damping);
        airResistanceCoefficient = Mathf.Max(0f, airResistanceCoefficient);
        pivotFrictionCoefficient = Mathf.Max(0f, pivotFrictionCoefficient);
        sloshingStrength = Mathf.Max(0f, sloshingStrength);
        sloshingStiffness = Mathf.Max(0f, sloshingStiffness);
        sloshingDamping = Mathf.Max(0f, sloshingDamping);
        sloshingMotionCoupling = Mathf.Max(0f, sloshingMotionCoupling);
    }

    private void Start()
    {
        initialPaintMass = paintMass;
        InitializeSimulation();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            PreviewInitialPoseInEditor();
        }
    }

    private void FixedUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!initialized)
        {
            InitializeSimulation();
        }

        float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : deltaT;

        UpdateMass(dt);
        UpdateEffectiveDamping();
        UpdatePaintSloshing(dt);

        state += RK4Step(state, time, dt);
        time += dt;

        Vector3 attachPosition = GetPendulumPosition(state.z, state.w);

        attachPointVelocity = (attachPosition - previousAttachPosition) / dt;
        previousAttachPosition = attachPosition;

        UpdatePhysicalOutputs(state.z);
        MoveBucketByAttachPoint(attachPosition);
    }

    private void InitializeSimulation()
    {
        if (pivotPoint == null || ropeAttachPoint == null)
        {
            if (Application.isPlaying)
            {
                Debug.LogError("SphericalPendulumController needs PivotPoint and RopeAttachPoint.");
                enabled = false;
            }

            return;
        }

        origin = pivotPoint.position;
        attachLocalOffset = transform.InverseTransformPoint(ropeAttachPoint.position);
        state = new Vector4(thetaDot, phiDot, theta, phi);

        totalMass = Mathf.Max(0.001f, bucketEmptyMass + paintMass);
        UpdateEffectiveDamping();

        sloshThetaOffset = 0f;
        sloshPhiOffset = 0f;
        sloshThetaVelocity = 0f;
        sloshPhiVelocity = 0f;

        Vector3 attachPosition = GetPendulumPosition(state.z, state.w);
        MoveBucketByAttachPoint(attachPosition);

        previousAttachPosition = attachPosition;
        attachPointVelocity = Vector3.zero;

        UpdatePhysicalOutputs(state.z);

        time = 0f;
        initialized = true;
    }

    private void PreviewInitialPoseInEditor()
    {
        if (pivotPoint == null || ropeAttachPoint == null)
        {
            return;
        }

        origin = pivotPoint.position;
        attachLocalOffset = transform.InverseTransformPoint(ropeAttachPoint.position);

        Vector3 initialAttachPosition = GetPendulumPosition(theta, phi);
        MoveBucketByAttachPoint(initialAttachPosition);
    }

    private void UpdateMass(float dt)
    {
        if (simulatePaintLoss)
        {
            paintMass = Mathf.Max(0f, paintMass - paintMassFlowRate * dt);
        }

        totalMass = Mathf.Max(0.001f, bucketEmptyMass + paintMass);
    }

    private void UpdateEffectiveDamping()
    {
        float safeLength = Mathf.Max(0.001f, ropeLength);
        float airDamping = airResistanceCoefficient / totalMass;
        float pivotDamping = pivotFrictionCoefficient / (totalMass * safeLength * safeLength);

        effectiveDamping = damping + airDamping + pivotDamping;
    }

    private void UpdatePaintSloshing(float dt)
    {
        if (!enablePaintSloshing || paintMass <= 0.001f)
        {
            sloshThetaOffset = 0f;
            sloshPhiOffset = 0f;
            sloshThetaVelocity = 0f;
            sloshPhiVelocity = 0f;
            return;
        }

        float paintRatio = paintMass / totalMass;

        float thetaDriver = -state.x * sloshingMotionCoupling * paintRatio;
        float phiDriver = -state.y * sloshingMotionCoupling * paintRatio;

        float thetaAcceleration =
            thetaDriver
            - sloshingStiffness * sloshThetaOffset
            - sloshingDamping * sloshThetaVelocity;

        float phiAcceleration =
            phiDriver
            - sloshingStiffness * sloshPhiOffset
            - sloshingDamping * sloshPhiVelocity;

        sloshThetaVelocity += thetaAcceleration * dt;
        sloshPhiVelocity += phiAcceleration * dt;

        sloshThetaOffset = Mathf.Clamp(sloshThetaOffset + sloshThetaVelocity * dt, -0.25f, 0.25f);
        sloshPhiOffset = Mathf.Clamp(sloshPhiOffset + sloshPhiVelocity * dt, -0.25f, 0.25f);
    }

    private Vector4 G(Vector4 currentState, float currentTime)
    {
        float th_d = currentState.x;
        float ph_d = currentState.y;
        float th = currentState.z;

        float tanTheta = Mathf.Tan(th);

        if (Mathf.Abs(tanTheta) < 0.001f)
        {
            tanTheta = tanTheta >= 0f ? 0.001f : -0.001f;
        }

        float safeLength = Mathf.Max(0.001f, ropeLength);
        float paintRatio = totalMass > 0.001f ? paintMass / totalMass : 0f;

        float sloshThetaEffect = enablePaintSloshing
            ? sloshingStrength * paintRatio * sloshThetaOffset
            : 0f;

        float sloshPhiEffect = enablePaintSloshing
            ? sloshingStrength * paintRatio * sloshPhiOffset
            : 0f;

        float thetaDDot =
            (ph_d * ph_d) * Mathf.Cos(th) * Mathf.Sin(th)
            - (gravity / safeLength) * Mathf.Sin(th)
            - effectiveDamping * th_d
            + sloshThetaEffect;

        float phiDDot =
            -2.0f * th_d * ph_d / tanTheta
            - effectiveDamping * ph_d
            + sloshPhiEffect;

        return new Vector4(thetaDDot, phiDDot, th_d, ph_d);
    }

    private Vector4 RK4Step(Vector4 currentState, float currentTime, float dt)
    {
        Vector4 k1 = G(currentState, currentTime);
        Vector4 k2 = G(currentState + 0.5f * dt * k1, currentTime + 0.5f * dt);
        Vector4 k3 = G(currentState + 0.5f * dt * k2, currentTime + 0.5f * dt);
        Vector4 k4 = G(currentState + dt * k3, currentTime + dt);

        return (dt / 6.0f) * (k1 + 2.0f * k2 + 2.0f * k3 + k4);
    }

    private Vector3 GetPendulumPosition(float th, float ph)
    {
        float safeLength = Mathf.Max(0.001f, ropeLength);
        float x = safeLength * Mathf.Sin(th) * Mathf.Cos(ph);
        float y = -safeLength * Mathf.Cos(th);
        float z = safeLength * Mathf.Sin(th) * Mathf.Sin(ph);

        return origin + new Vector3(x, y, z);
    }

    private void UpdatePhysicalOutputs(float currentTheta)
    {
        float safeLength = Mathf.Max(0.001f, ropeLength);
        float speed = attachPointVelocity.magnitude;

        ropeTension =
            totalMass * gravity * Mathf.Cos(currentTheta)
            + totalMass * speed * speed / safeLength;

        float height = safeLength * (1f - Mathf.Cos(currentTheta));
        potentialEnergy = totalMass * gravity * height;
        kineticEnergy = 0.5f * totalMass * speed * speed;
    }

    private void MoveBucketByAttachPoint(Vector3 targetAttachPosition)
    {
        Vector3 ropeDirection = pivotPoint.position - targetAttachPosition;

        if (ropeDirection.sqrMagnitude < 0.000001f)
        {
            return;
        }

        ropeDirection.Normalize();

        Quaternion ropeRotation = Quaternion.FromToRotation(Vector3.up, ropeDirection);

        Quaternion targetRotation = tiltBucketWithRope
            ? Quaternion.Slerp(Quaternion.identity, ropeRotation, tiltAmount)
            : Quaternion.identity;

        Vector3 rotatedAttachOffset = targetRotation * attachLocalOffset;

        transform.position = targetAttachPosition - rotatedAttachOffset;
        transform.rotation = targetRotation;
    }

    public void ResetPendulum()
    {
        paintMass = initialPaintMass;

        initialized = false;
        InitializeSimulation();
    }

    public void SetPaintAmount(float amount)
    {
        initialPaintMass = Mathf.Max(0f, amount);
        paintMass = initialPaintMass;
    }
}
