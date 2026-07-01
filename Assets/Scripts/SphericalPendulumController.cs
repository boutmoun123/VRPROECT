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

    [Header("Bucket Attachment")]
    public bool autoBucketVisualOffsetFromAttachPoint = true;
    public Vector3 bucketVisualOffset = new Vector3(0f, -0.7f, 0f);
    public float maxAllowedDistanceError = 0.03f;
    public bool showBucketDebug;

    [Header("Debug - Motion")]
    public Vector3 attachPointVelocity;
    public bool motionHeldByMouse;
    public float actualPivotToAttachDistance;
    public float pivotAttachDistanceError;
    public Vector3 debugPivotPosition;
    public Vector3 debugAttachPosition;
    public Vector3 debugBucketPosition;
    public Vector3 debugBucketVisualOffset;

    [Header("Debug - Physical Outputs")]
    public float totalMass;
    public float effectiveDamping;
    public float massResponseScale = 1f;
    public float ropeTension;
    public float kineticEnergy;
    public float potentialEnergy;

    [Header("Debug - Sloshing")]
    public float sloshThetaOffset;
    public float sloshPhiOffset;

    private Vector4 state;
    private Vector3 origin;
    private Vector3 previousAttachPosition;
    private LineRenderer pivotAttachDebugLine;
    private LineRenderer attachBucketDebugLine;
    private Transform pivotDebugMarker;
    private Transform attachDebugMarker;
    private Transform bucketDebugMarker;
    private Material debugMaterial;
    private float time;
    private bool initialized;

    private float initialPaintMass;
    private float sloshThetaVelocity;
    private float sloshPhiVelocity;

    public float InitialPaintMass => Mathf.Max(0.001f, initialPaintMass);
    public float PaintRemainingFraction => Mathf.Clamp01(paintMass / InitialPaintMass);
    public float CurrentTheta => initialized ? state.z : theta;
    public float CurrentPhi => initialized ? state.w : phi;
    public float CurrentThetaDot => initialized ? state.x : thetaDot;
    public float CurrentPhiDot => initialized ? state.y : phiDot;

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
        maxAllowedDistanceError = Mathf.Max(0.001f, maxAllowedDistanceError);
        bucketVisualOffset = SafeVector(bucketVisualOffset, new Vector3(0f, -0.7f, 0f));
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

        UpdateBucketDebug();
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

        if (motionHeldByMouse)
        {
            attachPointVelocity = Vector3.zero;
            previousAttachPosition = ropeAttachPoint.position;
            UpdateAttachmentDebugValues();
            return;
        }

        if (!ValidateRuntimeGeometry())
        {
            ResetBucketToPivot();
            return;
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
        EnforceAttachmentConstraint();
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
        ResolveBucketVisualOffset();
        state = new Vector4(thetaDot, phiDot, theta, phi);

        totalMass = Mathf.Max(0.001f, bucketEmptyMass + paintMass);
        UpdateMassResponseScale();
        UpdateEffectiveDamping();

        sloshThetaOffset = 0f;
        sloshPhiOffset = 0f;
        sloshThetaVelocity = 0f;
        sloshPhiVelocity = 0f;

        Vector3 attachPosition = GetPendulumPosition(state.z, state.w);
        MoveBucketByAttachPoint(attachPosition);
        EnforceAttachmentConstraint();

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
        ResolveBucketVisualOffset();

        Vector3 initialAttachPosition = GetPendulumPosition(theta, phi);
        MoveBucketByAttachPoint(initialAttachPosition);
        EnforceAttachmentConstraint();
    }

    private void UpdateMass(float dt)
    {
        if (simulatePaintLoss)
        {
            paintMass = Mathf.Max(0f, paintMass - paintMassFlowRate * dt);
        }

        totalMass = Mathf.Max(0.001f, bucketEmptyMass + paintMass);
        UpdateMassResponseScale();
    }

    private void UpdateEffectiveDamping()
    {
        float safeLength = Mathf.Max(0.001f, ropeLength);
        float airDamping = airResistanceCoefficient / totalMass;
        float pivotDamping = pivotFrictionCoefficient / (totalMass * safeLength * safeLength);

        effectiveDamping = damping + airDamping + pivotDamping;
    }

    private void UpdateMassResponseScale()
    {
        float normalizedMass = Mathf.InverseLerp(0.2f, 10f, bucketEmptyMass);
        massResponseScale = Mathf.Lerp(1.18f, 0.78f, normalizedMass);
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
            ((ph_d * ph_d) * Mathf.Cos(th) * Mathf.Sin(th)
            - (gravity / safeLength) * Mathf.Sin(th)) * massResponseScale
            - effectiveDamping * th_d
            + sloshThetaEffect;

        float phiDDot =
            (-2.0f * th_d * ph_d / tanTheta) * massResponseScale
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
        targetAttachPosition = ClampAttachToRopeLength(targetAttachPosition);
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

        transform.rotation = targetRotation;
        transform.position = targetAttachPosition + targetRotation * bucketVisualOffset;
        ropeAttachPoint.position = targetAttachPosition;
        UpdateAttachmentDebugValues();
    }

    private void ResolveBucketVisualOffset()
    {
        if (!autoBucketVisualOffsetFromAttachPoint || ropeAttachPoint == null)
        {
            bucketVisualOffset = SafeVector(bucketVisualOffset, new Vector3(0f, -0.7f, 0f));
            return;
        }

        if (ropeAttachPoint.IsChildOf(transform))
        {
            bucketVisualOffset = -ropeAttachPoint.localPosition;
            if (bucketVisualOffset.sqrMagnitude > 0.000001f)
            {
                return;
            }
        }

        OpenBucketMesh bucketMesh = GetComponent<OpenBucketMesh>();
        float halfHeight = bucketMesh != null ? Mathf.Max(0.05f, bucketMesh.height * 0.5f) : 0.7f;
        bucketVisualOffset = Vector3.down * halfHeight;
    }

    private Vector3 ClampAttachToRopeLength(Vector3 attachPosition)
    {
        Vector3 pivot = pivotPoint != null ? pivotPoint.position : origin;
        float safeLength = Mathf.Max(0.001f, ropeLength);
        Vector3 direction = attachPosition - pivot;

        if (!IsFinite(direction) || direction.sqrMagnitude < 0.000001f)
        {
            direction = GetInitialDirection(theta, phi);
        }

        return pivot + direction.normalized * safeLength;
    }

    public void EnforceAttachmentConstraint()
    {
        if (pivotPoint == null || ropeAttachPoint == null)
        {
            return;
        }

        if (ropeLength <= 0f || !float.IsFinite(ropeLength))
        {
            ropeLength = 4f;
        }

        Vector3 correctedAttach = ClampAttachToRopeLength(ropeAttachPoint.position);
        float error = Mathf.Abs(Vector3.Distance(pivotPoint.position, ropeAttachPoint.position) - ropeLength);
        if (error > maxAllowedDistanceError || !IsFinite(transform.position) || !IsFinite(ropeAttachPoint.position))
        {
            MoveBucketByAttachPoint(correctedAttach);
        }
        else
        {
            UpdateAttachmentDebugValues();
        }
    }

    private bool ValidateRuntimeGeometry()
    {
        if (pivotPoint == null || ropeAttachPoint == null)
        {
            return false;
        }

        if (!IsFinite(pivotPoint.position) || !IsFinite(ropeAttachPoint.position) || !IsFinite(transform.position))
        {
            return false;
        }

        if (ropeLength <= 0f || !float.IsFinite(ropeLength))
        {
            return false;
        }

        float distance = Vector3.Distance(pivotPoint.position, ropeAttachPoint.position);
        return float.IsFinite(distance) && distance < ropeLength * 4f + 10f;
    }

    public void ResetBucketToPivot()
    {
        if (pivotPoint == null || ropeAttachPoint == null)
        {
            return;
        }

        ropeLength = Mathf.Max(0.001f, float.IsFinite(ropeLength) ? ropeLength : 4f);
        origin = pivotPoint.position;
        ResolveBucketVisualOffset();
        Vector3 attachPosition = origin + GetInitialDirection(theta, phi) * ropeLength;
        state = new Vector4(0f, 0f, theta, phi);
        thetaDot = 0f;
        phiDot = 0f;
        sloshThetaOffset = 0f;
        sloshPhiOffset = 0f;
        sloshThetaVelocity = 0f;
        sloshPhiVelocity = 0f;
        attachPointVelocity = Vector3.zero;
        previousAttachPosition = attachPosition;
        MoveBucketByAttachPoint(attachPosition);
        UpdatePhysicalOutputs(theta);
        initialized = true;
    }

    private static Vector3 GetInitialDirection(float th, float ph)
    {
        return new Vector3(
            Mathf.Sin(th) * Mathf.Cos(ph),
            -Mathf.Cos(th),
            Mathf.Sin(th) * Mathf.Sin(ph)).normalized;
    }

    private void UpdateAttachmentDebugValues()
    {
        if (pivotPoint == null || ropeAttachPoint == null)
        {
            actualPivotToAttachDistance = 0f;
            pivotAttachDistanceError = 0f;
            return;
        }

        debugPivotPosition = pivotPoint.position;
        debugAttachPosition = ropeAttachPoint.position;
        debugBucketPosition = transform.position;
        debugBucketVisualOffset = transform.rotation * bucketVisualOffset;
        actualPivotToAttachDistance = Vector3.Distance(debugPivotPosition, debugAttachPosition);
        pivotAttachDistanceError = actualPivotToAttachDistance - Mathf.Max(0.001f, ropeLength);
    }

    private void UpdateBucketDebug()
    {
        bool visible = showBucketDebug && pivotPoint != null && ropeAttachPoint != null;
        if (!visible)
        {
            SetDebugObjectVisible(pivotDebugMarker, false);
            SetDebugObjectVisible(attachDebugMarker, false);
            SetDebugObjectVisible(bucketDebugMarker, false);
            SetDebugLineVisible(pivotAttachDebugLine, false);
            SetDebugLineVisible(attachBucketDebugLine, false);
            return;
        }

        EnsureBucketDebugObjects();
        SetDebugObjectVisible(pivotDebugMarker, visible);
        SetDebugObjectVisible(attachDebugMarker, visible);
        SetDebugObjectVisible(bucketDebugMarker, visible);
        SetDebugLineVisible(pivotAttachDebugLine, visible);
        SetDebugLineVisible(attachBucketDebugLine, visible);

        UpdateAttachmentDebugValues();
        pivotDebugMarker.position = debugPivotPosition;
        attachDebugMarker.position = debugAttachPosition;
        bucketDebugMarker.position = debugBucketPosition;
        pivotAttachDebugLine.SetPosition(0, debugPivotPosition);
        pivotAttachDebugLine.SetPosition(1, debugAttachPosition);
        attachBucketDebugLine.SetPosition(0, debugAttachPosition);
        attachBucketDebugLine.SetPosition(1, debugBucketPosition);
    }

    private void EnsureBucketDebugObjects()
    {
        if (debugMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            debugMaterial = new Material(shader);
            debugMaterial.name = "Bucket Attachment Debug Material";
        }

        pivotDebugMarker = EnsureDebugMarker(pivotDebugMarker, "Bucket Debug Pivot Marker", Color.yellow);
        attachDebugMarker = EnsureDebugMarker(attachDebugMarker, "Bucket Debug Attach Marker", Color.cyan);
        bucketDebugMarker = EnsureDebugMarker(bucketDebugMarker, "Bucket Debug Center Marker", Color.magenta);
        pivotAttachDebugLine = EnsureDebugLine(pivotAttachDebugLine, "Bucket Debug Pivot To Attach", Color.yellow);
        attachBucketDebugLine = EnsureDebugLine(attachBucketDebugLine, "Bucket Debug Attach To Center", Color.cyan);
    }

    private Transform EnsureDebugMarker(Transform marker, string markerName, Color color)
    {
        if (marker != null)
        {
            return marker;
        }

        GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerObject.name = markerName;
        markerObject.transform.SetParent(null);
        markerObject.transform.localScale = Vector3.one * 0.11f;
        DestroyRuntime(markerObject.GetComponent<Collider>());
        MeshRenderer renderer = markerObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material material = new Material(debugMaterial);
            material.color = color;
            renderer.sharedMaterial = material;
        }
        markerObject.SetActive(false);
        return markerObject.transform;
    }

    private LineRenderer EnsureDebugLine(LineRenderer line, string lineName, Color color)
    {
        if (line != null)
        {
            return line;
        }

        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(null);
        line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = 0.025f;
        line.endWidth = 0.025f;
        line.sharedMaterial = debugMaterial;
        line.startColor = color;
        line.endColor = color;
        line.enabled = false;
        return line;
    }

    private static void SetDebugObjectVisible(Transform marker, bool visible)
    {
        if (marker != null && marker.gameObject.activeSelf != visible)
        {
            marker.gameObject.SetActive(visible);
        }
    }

    private static void SetDebugLineVisible(LineRenderer line, bool visible)
    {
        if (line != null)
        {
            line.enabled = visible;
        }
    }

    public void SetMouseHoldActive(bool active)
    {
        motionHeldByMouse = active;
        if (active && ropeAttachPoint != null)
        {
            previousAttachPosition = ropeAttachPoint.position;
            attachPointVelocity = Vector3.zero;
        }
    }

    public void SetStateFromAttachDirection(Vector3 pivotToAttach, Vector3 releaseVelocity)
    {
        if (pivotPoint == null || ropeAttachPoint == null)
        {
            return;
        }

        if (!initialized)
        {
            InitializeSimulation();
        }

        origin = pivotPoint.position;
        float safeLength = Mathf.Max(0.001f, ropeLength);
        Vector3 direction = pivotToAttach.sqrMagnitude > 0.000001f ? pivotToAttach.normalized : Vector3.down;
        Vector3 attachPosition = origin + direction * safeLength;

        float newTheta = Mathf.Acos(Mathf.Clamp(-direction.y, -1f, 1f));
        float newPhi = Mathf.Atan2(direction.z, direction.x);
        float sinTheta = Mathf.Sin(newTheta);

        Vector3 eTheta = new Vector3(
            Mathf.Cos(newTheta) * Mathf.Cos(newPhi),
            Mathf.Sin(newTheta),
            Mathf.Cos(newTheta) * Mathf.Sin(newPhi)
        );
        Vector3 ePhi = new Vector3(-Mathf.Sin(newPhi), 0f, Mathf.Cos(newPhi));

        float newThetaDot = Vector3.Dot(releaseVelocity, eTheta) / safeLength;
        float newPhiDot = sinTheta > 0.001f ? Vector3.Dot(releaseVelocity, ePhi) / (safeLength * sinTheta) : 0f;

        if (!float.IsFinite(newThetaDot)) newThetaDot = 0f;
        if (!float.IsFinite(newPhiDot)) newPhiDot = 0f;

        theta = newTheta;
        phi = newPhi;
        thetaDot = Mathf.Clamp(newThetaDot, -6f, 6f);
        phiDot = Mathf.Clamp(newPhiDot, -6f, 6f);
        state = new Vector4(thetaDot, phiDot, theta, phi);
        attachPointVelocity = releaseVelocity;
        previousAttachPosition = attachPosition;
        MoveBucketByAttachPoint(attachPosition);
        UpdatePhysicalOutputs(theta);
    }

    public void SetStateFromAttachDirection(Vector3 pivotToAttach)
    {
        SetStateFromAttachDirection(pivotToAttach, Vector3.zero);
    }

    public void ResetPendulum()
    {
        motionHeldByMouse = false;
        paintMass = initialPaintMass;

        initialized = false;
        InitializeSimulation();
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private static Vector3 SafeVector(Vector3 value, Vector3 fallback)
    {
        return IsFinite(value) ? value : fallback;
    }

    private static void DestroyRuntime(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void OnDestroy()
    {
        DestroyRuntime(debugMaterial);
        if (pivotDebugMarker != null) DestroyRuntime(pivotDebugMarker.gameObject);
        if (attachDebugMarker != null) DestroyRuntime(attachDebugMarker.gameObject);
        if (bucketDebugMarker != null) DestroyRuntime(bucketDebugMarker.gameObject);
        if (pivotAttachDebugLine != null) DestroyRuntime(pivotAttachDebugLine.gameObject);
        if (attachBucketDebugLine != null) DestroyRuntime(attachBucketDebugLine.gameObject);
    }

    public void SetPaintAmount(float amount)
    {
        initialPaintMass = Mathf.Max(0f, amount);
        paintMass = initialPaintMass;
    }
}
