using UnityEngine;

public struct SpringRopeNode
{
    public Vector3 position;
    public Vector3 velocity;
    public Vector3 force;
    public float mass;
}

[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class MassSpringRope : MonoBehaviour
{
    public const string RopeModelName = "Mass-Spring Rope";

    [Header("References")]
    public Transform pivotPoint;
    public Transform ropeAttachPoint;

    [Header("Rope Settings")]
    public string ropeType = RopeModelName;
    public float ropeLength = 4.1f;
    public int nodeCount = 45;

    [Header("Mass-Spring Physics")]
    [Tooltip("Keep this very small because bucket mass is much bigger than rope mass.")]
    public float nodeMass = 0.003f;

    [Tooltip("Higher = tighter rope.")]
    public float stiffness = 3000f;

    [Tooltip("Higher = less rope vibration.")]
    public float springDamping = 70f;

    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

    [Tooltip("Gravity effect on rope only. Small value keeps rope taut.")]
    [Range(0f, 1f)]
    public float ropeGravityScale = 0.08f;

    [Range(0.90f, 1.0f)]
    public float globalDamping = 0.995f;

    [Tooltip("Higher = more stable rope.")]
    public int subSteps = 20;

    [Header("Taut Rope Control")]
    [Tooltip("Near 1 means rope is almost not stretchable.")]
    public float maxStretchRatio = 1.03f;

    [Tooltip("Higher = rope keeps length better.")]
    public int lengthCorrectionIterations = 18;

    [Tooltip("Limits excessive velocity to prevent rope explosion.")]
    public float maxVelocity = 6f;

    [Tooltip("How much the rope is allowed to visually sag. Lower = straighter.")]
    [Range(0f, 1f)]
    public float bendAmount = 0.18f;

    [Header("Debug - Mass-Spring")]
    public float currentAverageStretch;
    public float currentMaxStretch;
    public float currentAverageStretchPercent;

    [Header("Visual")]
    public float ropeWidth = 0.045f;
    public Material ropeMaterial;

    [Header("Editor Preview")]
    public bool drawInEditor = true;

    private SpringRopeNode[] nodes;
    private LineRenderer lineRenderer;
    private float restLength;
    private bool initialized;
    private int previousNodeCount;

    private void Awake()
    {
        Setup();
    }

    private void OnEnable()
    {
        Setup();
    }

    private void OnValidate()
    {
        ropeLength = Mathf.Max(0.1f, ropeLength);
        nodeCount = Mathf.Max(3, nodeCount);
        ropeType = RopeModelName;
        nodeMass = Mathf.Max(0.0001f, nodeMass);
        stiffness = Mathf.Max(1f, stiffness);
        springDamping = Mathf.Max(0f, springDamping);
        subSteps = Mathf.Max(1, subSteps);
        maxStretchRatio = Mathf.Max(1.001f, maxStretchRatio);
        lengthCorrectionIterations = Mathf.Max(0, lengthCorrectionIterations);
        maxVelocity = Mathf.Max(0.1f, maxVelocity);
        ropeWidth = Mathf.Max(0.001f, ropeWidth);

        if (!Application.isPlaying && drawInEditor)
        {
            DrawEditorPreview();
        }
    }

    private void Update()
    {
        if (!Application.isPlaying && drawInEditor)
        {
            DrawEditorPreview();
        }
    }

    private void FixedUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!Ready())
        {
            return;
        }

        SyncLengthToEndpoints();

        if (!initialized || nodes == null || nodes.Length != nodeCount)
        {
            Setup();
        }

        float dt = Time.fixedDeltaTime / subSteps;

        for (int step = 0; step < subSteps; step++)
        {
            Simulate(dt);

            for (int i = 0; i < lengthCorrectionIterations; i++)
            {
                CorrectLengthConstraints();
            }

            BlendTowardTautLine();
        }

        UpdateStretchDebug();
        UpdateLineRenderer();
    }

    private bool Ready()
    {
        return pivotPoint != null && ropeAttachPoint != null;
    }

    private void Setup()
    {
        lineRenderer = GetComponent<LineRenderer>();
        SetupLineRenderer();

        if (!Ready())
        {
            return;
        }

        SyncLengthToEndpoints();
        nodes = new SpringRopeNode[nodeCount];
        restLength = ropeLength / (nodeCount - 1);
        previousNodeCount = nodeCount;

        Vector3 start = pivotPoint.position;
        Vector3 end = ropeAttachPoint.position;

        for (int i = 0; i < nodeCount; i++)
        {
            float t = (float)i / (nodeCount - 1);
            Vector3 pos = Vector3.Lerp(start, end, t);

            nodes[i].position = pos;
            nodes[i].velocity = Vector3.zero;
            nodes[i].force = Vector3.zero;
            nodes[i].mass = nodeMass;
        }

        initialized = true;
        UpdateStretchDebug();
        UpdateLineRenderer();
    }

    private void Simulate(float dt)
    {
        int last = nodeCount - 1;

        nodes[0].position = pivotPoint.position;
        nodes[0].velocity = Vector3.zero;

        nodes[last].position = ropeAttachPoint.position;
        nodes[last].velocity = Vector3.zero;

        ClearForces();
        ApplyGravity();
        ApplySpringForces();
        Integrate(dt);

        nodes[0].position = pivotPoint.position;
        nodes[0].velocity = Vector3.zero;

        nodes[last].position = ropeAttachPoint.position;
        nodes[last].velocity = Vector3.zero;
    }

    private void ClearForces()
    {
        for (int i = 0; i < nodeCount; i++)
        {
            nodes[i].force = Vector3.zero;
        }
    }

    private void ApplyGravity()
    {
        int last = nodeCount - 1;

        for (int i = 1; i < last; i++)
        {
            nodes[i].force += gravity * nodes[i].mass * ropeGravityScale;
        }
    }

    private void ApplySpringForces()
    {
        for (int i = 0; i < nodeCount - 1; i++)
        {
            int a = i;
            int b = i + 1;

            Vector3 delta = nodes[b].position - nodes[a].position;
            float length = delta.magnitude;

            if (length < 0.0001f)
            {
                continue;
            }

            Vector3 dir = delta / length;

            float maxLength = restLength * maxStretchRatio;
            float safeLength = Mathf.Min(length, maxLength);

            float stretch = safeLength - restLength;

            // Hooke's law between neighboring rope nodes:
            // F = -k * (currentLength - restLength), applied along the spring axis.
            Vector3 springForce = stiffness * stretch * dir;

            Vector3 relativeVelocity = nodes[b].velocity - nodes[a].velocity;
            float velocityAlongSpring = Vector3.Dot(relativeVelocity, dir);

            // Damping opposes relative motion along the same spring axis.
            Vector3 dampingForce = springDamping * velocityAlongSpring * dir;

            Vector3 totalForce = springForce + dampingForce;

            bool aPinned = a == 0;
            bool bPinned = b == nodeCount - 1;

            if (!aPinned)
            {
                nodes[a].force += totalForce;
            }

            if (!bPinned)
            {
                nodes[b].force -= totalForce;
            }
        }
    }

    private void Integrate(float dt)
    {
        int last = nodeCount - 1;

        for (int i = 1; i < last; i++)
        {
            Vector3 acceleration = nodes[i].force / nodes[i].mass;

            nodes[i].velocity += acceleration * dt;
            nodes[i].velocity *= globalDamping;

            if (nodes[i].velocity.magnitude > maxVelocity)
            {
                nodes[i].velocity = nodes[i].velocity.normalized * maxVelocity;
            }

            nodes[i].position += nodes[i].velocity * dt;

            if (!IsFinite(nodes[i].position))
            {
                ResetRope();
                return;
            }
        }
    }

    private void CorrectLengthConstraints()
    {
        int last = nodeCount - 1;

        nodes[0].position = pivotPoint.position;
        nodes[last].position = ropeAttachPoint.position;

        for (int i = 0; i < nodeCount - 1; i++)
        {
            int a = i;
            int b = i + 1;

            Vector3 delta = nodes[b].position - nodes[a].position;
            float length = delta.magnitude;

            if (length < 0.0001f)
            {
                continue;
            }

            Vector3 dir = delta / length;
            float error = length - restLength;
            Vector3 correction = error * dir;

            bool aPinned = a == 0;
            bool bPinned = b == last;

            if (aPinned && !bPinned)
            {
                nodes[b].position -= correction;
            }
            else if (!aPinned && bPinned)
            {
                nodes[a].position += correction;
            }
            else if (!aPinned && !bPinned)
            {
                nodes[a].position += correction * 0.5f;
                nodes[b].position -= correction * 0.5f;
            }
        }

        nodes[0].position = pivotPoint.position;
        nodes[last].position = ropeAttachPoint.position;
    }

    private void BlendTowardTautLine()
    {
        int last = nodeCount - 1;

        Vector3 start = pivotPoint.position;
        Vector3 end = ropeAttachPoint.position;

        Vector3 ropeVector = end - start;

        if (ropeVector.magnitude < 0.0001f)
        {
            return;
        }

        Vector3 ropeDir = ropeVector.normalized;

        float angleFromVertical = Vector3.Angle(ropeDir, Vector3.down);

        // يبدأ الانحناء بعد 35 درجة، ويصل لأقصاه عند 60 درجة
        float angleFactor = Mathf.InverseLerp(35f, 60f, angleFromVertical);

        // اتجاه الانحناء: باتجاه الجاذبية لكن عمودي على اتجاه الحبل
        Vector3 bendDirection = Vector3.ProjectOnPlane(Vector3.down, ropeDir);

        if (bendDirection.magnitude < 0.0001f)
        {
            bendDirection = Vector3.zero;
        }
        else
        {
            bendDirection.Normalize();
        }

        // مقدار الانحناء عند الزاوية الكبيرة
        float stiffnessFlex = Mathf.InverseLerp(6000f, 200f, stiffness);
        float maxSag = bendAmount * Mathf.Lerp(0.35f, 1.65f, stiffnessFlex);
        float lineBlend = Mathf.Lerp(0.92f, 0.55f, bendAmount) * Mathf.Lerp(1f, 0.7f, stiffnessFlex);

        for (int i = 1; i < last; i++)
        {
            float t = (float)i / (nodeCount - 1);

            Vector3 straightPosition = Vector3.Lerp(start, end, t);

            // الانحناء أكبر بالمنتصف وأصغر عند الأطراف
            float middleWeight = Mathf.Sin(t * Mathf.PI);

            Vector3 sagOffset = bendDirection * maxSag * angleFactor * middleWeight;

            Vector3 targetPosition = straightPosition + sagOffset;
            nodes[i].position = Vector3.Lerp(nodes[i].position, targetPosition, lineBlend);

            // نخفف السرعة حتى ما يرجع يهتز بعنف
            nodes[i].velocity *= 0.7f;
        }

        nodes[0].position = start;
        nodes[last].position = end;
    }

    private bool IsFinite(Vector3 v)
    {
        return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }

    private void SetupLineRenderer()
    {
        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = false;
        lineRenderer.positionCount = nodeCount;

        lineRenderer.startWidth = ropeWidth;
        lineRenderer.endWidth = ropeWidth;

        lineRenderer.numCornerVertices = 6;
        lineRenderer.numCapVertices = 6;

        if (ropeMaterial != null)
        {
            lineRenderer.sharedMaterial = ropeMaterial;
        }
    }

    private void UpdateLineRenderer()
    {
        if (lineRenderer == null || nodes == null)
        {
            return;
        }

        SetupLineRenderer();

        for (int i = 0; i < nodeCount; i++)
        {
            if (!IsFinite(nodes[i].position))
            {
                ResetRope();
                return;
            }

            lineRenderer.SetPosition(i, nodes[i].position);
        }
    }

    private void UpdateStretchDebug()
    {
        if (nodes == null || nodes.Length < 2 || restLength <= 0f)
        {
            currentAverageStretch = 0f;
            currentMaxStretch = 0f;
            currentAverageStretchPercent = 0f;
            return;
        }

        float totalStretch = 0f;
        float maxStretch = 0f;
        for (int i = 0; i < nodes.Length - 1; i++)
        {
            float length = Vector3.Distance(nodes[i].position, nodes[i + 1].position);
            float stretch = Mathf.Abs(length - restLength);
            totalStretch += stretch;
            maxStretch = Mathf.Max(maxStretch, stretch);
        }

        int segmentCount = nodes.Length - 1;
        currentAverageStretch = totalStretch / Mathf.Max(1, segmentCount);
        currentMaxStretch = maxStretch;
        currentAverageStretchPercent = currentAverageStretch / restLength * 100f;
    }

    private void DrawEditorPreview()
    {
        if (!Ready())
        {
            return;
        }

        lineRenderer = GetComponent<LineRenderer>();
        SetupLineRenderer();

        Vector3 start = pivotPoint.position;
        Vector3 end = ropeAttachPoint.position;

        for (int i = 0; i < nodeCount; i++)
        {
            float t = (float)i / (nodeCount - 1);
            lineRenderer.SetPosition(i, Vector3.Lerp(start, end, t));
        }
    }

    public void ResetRope()
    {
        initialized = false;
        Setup();
    }

    public void SyncLengthToEndpoints()
    {
        if (!Ready())
        {
            return;
        }

        float endpointDistance = Vector3.Distance(pivotPoint.position, ropeAttachPoint.position);
        if (float.IsFinite(endpointDistance) && endpointDistance > 0.001f)
        {
            ropeLength = endpointDistance;
            restLength = ropeLength / Mathf.Max(1, nodeCount - 1);
        }
    }

    public void SnapToCurrentEndpoints()
    {
        if (!Ready())
        {
            return;
        }

        SyncLengthToEndpoints();
        if (!initialized || nodes == null || nodes.Length != nodeCount)
        {
            Setup();
        }

        Vector3 start = pivotPoint.position;
        Vector3 end = ropeAttachPoint.position;
        for (int i = 0; i < nodeCount; i++)
        {
            float t = (float)i / (nodeCount - 1);
            nodes[i].position = Vector3.Lerp(start, end, t);
            nodes[i].velocity = Vector3.zero;
            nodes[i].force = Vector3.zero;
        }

        UpdateStretchDebug();
        UpdateLineRenderer();
    }

    public void ApplyMassSpringSettings(int segments, float newStiffness, float newDamping, float newFlexibility, int constraintIterations)
    {
        int requestedNodeCount = Mathf.Max(3, segments + 1);
        bool topologyChanged = requestedNodeCount != nodeCount || previousNodeCount != requestedNodeCount;

        nodeCount = requestedNodeCount;
        stiffness = Mathf.Max(1f, newStiffness);
        springDamping = Mathf.Max(0f, newDamping);
        bendAmount = Mathf.Clamp01(newFlexibility);
        lengthCorrectionIterations = Mathf.Max(0, constraintIterations);
        ropeType = RopeModelName;

        if (topologyChanged || nodes == null || nodes.Length != nodeCount)
        {
            ResetRope();
        }
    }
}
