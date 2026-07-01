using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class BucketPaintReservoir : MonoBehaviour
{
    public enum PaintMixMode
    {
        SingleColor,
        MixedBucket,
        SequentialColors
    }

    public enum LiquidAxis
    {
        LocalX,
        LocalY,
        LocalZ
    }

    [System.Serializable]
    public struct PaintComponent
    {
        public Color color;
        public float amount;

        public PaintComponent(Color color, float amount)
        {
            this.color = color;
            this.amount = Mathf.Max(0f, amount);
        }
    }

    [Header("Reservoir")]
    public float capacity = 5f;
    public float initialPaintAmount = 1f;
    public float currentPaintAmount = 1f;
    public float remainingPaintAmount = 1f;
    public Color selectedPaintColor = new Color(0.1f, 0.25f, 1f, 1f);
    public Color mixedPaintColor = new Color(0.1f, 0.25f, 1f, 1f);
    public PaintMixMode mixMode = PaintMixMode.SingleColor;
    [Range(0f, 1f)] public float colorMixStrength = 0.55f;

    [Header("Visual")]
    public bool createVisualIfMissing = true;
    public string liquidAnchorName = "BucketLiquidAnchor";
    public string liquidObjectName = "BucketLiquidVisual";
    public float bottomOffset = 0.08f;
    public float topOffset = 0.12f;
    public float surfaceThickness = 0.035f;
    public float smoothFillSpeed = 8f;
    [Range(0f, 1f)] public float sloshAmount = 0.08f;
    public bool forceShowLiquidVisual;
    public LiquidAxis liquidAxis = LiquidAxis.LocalY;
    public bool flipAxis;
    public Vector3 liquidLocalCenterOffset = Vector3.zero;
    public Vector3 liquidAnchorLocalPosition = Vector3.zero;
    [Range(0.02f, 2f)] public float liquidRadius = 0.45f;
    [Range(0.1f, 1.5f)] public float liquidRadiusMultiplier = 0.82f;
    public float liquidHeightOffset;
    [Range(0.1f, 3f)] public float liquidDebugScale = 1f;
    public Color forceShowColor = new Color(1f, 0f, 1f, 1f);

    public float FillPercent => capacity > 0f ? Mathf.Clamp01(currentPaintAmount / capacity) : 0f;
    public bool IsEmpty => currentPaintAmount <= 0.0001f;
    public bool IsLowPaint => !IsEmpty && FillPercent <= 0.15f;
    public string BucketState
    {
        get
        {
            if (IsEmpty) return "Empty";
            if (IsLowPaint) return "Low Paint";
            if (FillPercent >= 0.95f) return "Full";
            return isFlowing ? "Flowing" : "Full";
        }
    }
    public Color VisiblePaintColor => GetVisiblePaintColor();
    public string ColorComponentsSummary => BuildColorComponentsSummary();
    public int ColorComponentCount => paintComponents.Count;
    public bool HasLiquidAnchor => liquidAnchor != null;
    public bool HasLiquidVisual => liquidVisual != null;
    public bool LiquidRendererEnabled => liquidRenderer != null && liquidRenderer.enabled;
    public Vector3 LiquidLocalPosition => liquidAnchor != null ? liquidAnchor.localPosition : Vector3.zero;
    public Vector3 LiquidWorldPosition => liquidAnchor != null ? liquidAnchor.position : Vector3.zero;
    public Vector3 LiquidLocalScale => liquidVisual != null ? liquidVisual.localScale : Vector3.zero;
    public Color LiquidMaterialColor => liquidMaterial != null ? liquidMaterial.color : Color.clear;
    public string LiquidDebugSummary => BuildLiquidDebugSummary();

    private readonly List<PaintComponent> paintComponents = new List<PaintComponent>();
    private Transform liquidAnchor;
    private Transform liquidVisual;
    private MeshRenderer liquidRenderer;
    private Material liquidMaterial;
    private Mesh liquidMesh;
    private OpenBucketMesh bucketMesh;
    private PaintParticleEmitter emitter;
    private Vector3 previousPosition;
    private Vector3 velocity;
    private bool isFlowing;
    private int sequentialIndex;
    private float visualFillPercent;

    private void Awake()
    {
        Initialize();
    }

    private void OnValidate()
    {
        capacity = Mathf.Max(0.001f, capacity);
        initialPaintAmount = Mathf.Clamp(initialPaintAmount, 0f, capacity);
        currentPaintAmount = Mathf.Clamp(currentPaintAmount, 0f, capacity);
        remainingPaintAmount = currentPaintAmount;
        bottomOffset = Mathf.Max(0f, bottomOffset);
        topOffset = Mathf.Max(0f, topOffset);
        surfaceThickness = Mathf.Max(0.001f, surfaceThickness);
        smoothFillSpeed = Mathf.Max(0.01f, smoothFillSpeed);
        liquidRadius = Mathf.Clamp(liquidRadius, 0.02f, 2f);
        liquidRadiusMultiplier = Mathf.Clamp(liquidRadiusMultiplier, 0.1f, 1.5f);
        liquidDebugScale = Mathf.Clamp(liquidDebugScale, 0.1f, 3f);
    }

    private void LateUpdate()
    {
        SyncFromEmitter();
        UpdateVisual(Time.unscaledDeltaTime);
    }

    public void Initialize()
    {
        bucketMesh = GetComponentInChildren<OpenBucketMesh>();
        emitter = FindAnyObjectByType<PaintParticleEmitter>();
        capacity = Mathf.Max(0.001f, capacity);
        currentPaintAmount = Mathf.Clamp(currentPaintAmount <= 0f ? initialPaintAmount : currentPaintAmount, 0f, capacity);
        remainingPaintAmount = currentPaintAmount;
        visualFillPercent = FillPercent;
        EnsureColorComponent();
        RecalculateMixedColor();
        EnsureLiquidVisual();
        RebuildLiquidMesh();
        previousPosition = transform.position;
        AutoFitLiquidToBucket();
        UpdateVisual(999f);
    }

    public void BindEmitter(PaintParticleEmitter newEmitter)
    {
        emitter = newEmitter;
        SyncFromEmitter();
    }

    public void SetCapacity(float newCapacity)
    {
        capacity = Mathf.Max(0.001f, newCapacity);
        currentPaintAmount = Mathf.Clamp(currentPaintAmount, 0f, capacity);
        remainingPaintAmount = currentPaintAmount;
        initialPaintAmount = Mathf.Clamp(initialPaintAmount, 0f, capacity);
        NormalizeComponentAmountsToCurrentPaint();
        RecalculateMixedColor();
        RebuildLiquidMesh();
        UpdateVisual(999f);
    }

    public void SetPaintAmount(float amount)
    {
        SetInitialPaintAmount(amount, refillCurrent: true);
        visualFillPercent = Mathf.Clamp01(visualFillPercent);
        if (emitter != null)
        {
            emitter.SetPaintAmount(currentPaintAmount);
        }
        NormalizeComponentAmountsToCurrentPaint();
        RecalculateMixedColor();
        UpdateVisual(999f);
    }

    public void SetInitialPaintAmount(float amount, bool refillCurrent)
    {
        initialPaintAmount = Mathf.Clamp(amount, 0f, capacity);
        if (refillCurrent)
        {
            currentPaintAmount = initialPaintAmount;
            remainingPaintAmount = currentPaintAmount;
            NormalizeComponentAmountsToCurrentPaint();
            RecalculateMixedColor();
            UpdateVisual(999f);
        }
    }

    public void SyncFromEmitter()
    {
        if (emitter == null)
        {
            return;
        }

        selectedPaintColor = emitter.paintColor;
        capacity = Mathf.Max(capacity, emitter.initialPaintAmount);
        initialPaintAmount = Mathf.Clamp(emitter.initialPaintAmount, 0f, capacity);
        currentPaintAmount = Mathf.Clamp(emitter.remainingPaintAmount, 0f, capacity);
        remainingPaintAmount = currentPaintAmount;
        isFlowing = emitter.CurrentFlowRateKgPerSecond > 0f && !emitter.isPaused && !IsEmpty;
        EnsureColorComponent();
        RecalculateMixedColor();
    }

    public void SetSelectedColor(Color color)
    {
        selectedPaintColor = color;
        if (emitter != null)
        {
            emitter.paintColor = color;
        }
        if (mixMode == PaintMixMode.SingleColor)
        {
            mixedPaintColor = color;
        }
        UpdateVisual(999f);
    }

    public void SetMixMode(PaintMixMode mode)
    {
        mixMode = mode;
        RecalculateMixedColor();
        UpdateVisual(0.1f);
    }

    public void AddColorToBucket(Color color, float amount)
    {
        float safeAmount = Mathf.Clamp(amount, 0.001f, Mathf.Max(0.001f, capacity));
        paintComponents.Add(new PaintComponent(color, safeAmount));
        selectedPaintColor = color;
        RecalculateMixedColor();
        UpdateVisual(0.1f);
    }

    public void ClearColors()
    {
        paintComponents.Clear();
        paintComponents.Add(new PaintComponent(selectedPaintColor, Mathf.Max(0.001f, currentPaintAmount)));
        sequentialIndex = 0;
        RecalculateMixedColor();
        UpdateVisual(0.1f);
    }

    public Color GetEmissionColor(int emissionIndex)
    {
        if (mixMode == PaintMixMode.MixedBucket)
        {
            return mixedPaintColor;
        }

        if (mixMode == PaintMixMode.SequentialColors && paintComponents.Count > 0)
        {
            int index = Mathf.Abs(sequentialIndex++) % paintComponents.Count;
            return paintComponents[index].color;
        }

        return selectedPaintColor;
    }

    public string GetColorsUsedSummary()
    {
        return BuildColorComponentsSummary();
    }

    private Color GetVisiblePaintColor()
    {
        if (mixMode == PaintMixMode.MixedBucket)
        {
            return mixedPaintColor;
        }

        if (mixMode == PaintMixMode.SequentialColors && paintComponents.Count > 0)
        {
            return paintComponents[Mathf.Abs(sequentialIndex) % paintComponents.Count].color;
        }

        return selectedPaintColor;
    }

    private void EnsureColorComponent()
    {
        if (paintComponents.Count == 0)
        {
            paintComponents.Add(new PaintComponent(selectedPaintColor, Mathf.Max(0.001f, currentPaintAmount)));
        }
    }

    private void NormalizeComponentAmountsToCurrentPaint()
    {
        EnsureColorComponent();
        float total = 0f;
        for (int i = 0; i < paintComponents.Count; i++)
        {
            total += Mathf.Max(0f, paintComponents[i].amount);
        }

        if (total <= 0.0001f)
        {
            ClearColors();
            return;
        }

        for (int i = 0; i < paintComponents.Count; i++)
        {
            PaintComponent component = paintComponents[i];
            component.amount = currentPaintAmount * component.amount / total;
            paintComponents[i] = component;
        }
    }

    private void RecalculateMixedColor()
    {
        EnsureColorComponent();
        if (mixMode == PaintMixMode.SingleColor)
        {
            mixedPaintColor = selectedPaintColor;
            return;
        }

        float total = 0f;
        Vector3 linear = Vector3.zero;
        for (int i = 0; i < paintComponents.Count; i++)
        {
            PaintComponent component = paintComponents[i];
            float amount = Mathf.Max(0f, component.amount);
            Color c = component.color.linear;
            linear += new Vector3(c.r, c.g, c.b) * amount;
            total += amount;
        }

        if (total <= 0.0001f)
        {
            mixedPaintColor = selectedPaintColor;
            return;
        }

        linear /= total;
        Color average = new Color(linear.x, linear.y, linear.z, 1f).gamma;
        float componentCountFactor = Mathf.Clamp01((paintComponents.Count - 1) / 4f);
        Color darker = Color.Lerp(average, average * 0.58f, componentCountFactor);
        float gray = darker.grayscale;
        Color pigment = Color.Lerp(darker, new Color(gray * 0.75f, gray * 0.62f, gray * 0.45f, 1f), componentCountFactor * 0.45f);
        mixedPaintColor = Color.Lerp(average, pigment, colorMixStrength);
        mixedPaintColor.a = 1f;
    }

    private void EnsureLiquidVisual()
    {
        if (!createVisualIfMissing)
        {
            return;
        }

        EnsureLiquidAnchor();

        Transform existing = liquidAnchor != null ? liquidAnchor.Find(liquidObjectName) : null;
        if (existing == null)
        {
            existing = FindDeepChild(transform, liquidObjectName);
            if (existing != null && liquidAnchor != null)
            {
                existing.SetParent(liquidAnchor, false);
            }
        }

        if (existing == null)
        {
            GameObject liquid = new GameObject(liquidObjectName);
            liquid.transform.SetParent(liquidAnchor != null ? liquidAnchor : transform, false);
            liquid.AddComponent<MeshFilter>();
            liquidRenderer = liquid.AddComponent<MeshRenderer>();
            liquidVisual = liquid.transform;
        }
        else
        {
            liquidVisual = existing;
            liquidRenderer = liquidVisual.GetComponent<MeshRenderer>();
            if (liquidRenderer == null)
            {
                liquidRenderer = liquidVisual.gameObject.AddComponent<MeshRenderer>();
            }
            if (liquidVisual.GetComponent<MeshFilter>() == null)
            {
                liquidVisual.gameObject.AddComponent<MeshFilter>();
            }
        }

        liquidVisual.localPosition = Vector3.zero;
        RebuildLiquidMesh();

        if (liquidMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            liquidMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            liquidMaterial.name = "Bucket Liquid Paint Material";
            SetMaterialFloat(liquidMaterial, "_Smoothness", 0.78f);
            SetMaterialFloat(liquidMaterial, "_Metallic", 0f);
        }

        liquidRenderer.sharedMaterial = liquidMaterial;
        liquidRenderer.enabled = true;
    }

    private void EnsureLiquidAnchor()
    {
        if (liquidAnchor != null)
        {
            return;
        }

        liquidAnchor = FindDeepChild(transform, liquidAnchorName);
        if (liquidAnchor == null)
        {
            GameObject anchor = new GameObject(liquidAnchorName);
            anchor.transform.SetParent(transform, false);
            liquidAnchor = anchor.transform;
            AutoFitLiquidToBucket();
        }
    }

    public void AutoFitLiquidToBucket()
    {
        EnsureLiquidAnchor();
        if (liquidAnchor == null)
        {
            return;
        }

        Bounds bounds = GetBucketLocalBounds();
        liquidAxis = GetLargestBoundsAxis(bounds);

        int axisIndex = AxisIndex(liquidAxis);
        float height = Mathf.Max(0.01f, GetAxisSize(bounds.size, axisIndex));
        float bottom = GetAxisMin(bounds, axisIndex) + Mathf.Min(bottomOffset, height * 0.25f);
        float top = GetAxisMax(bounds, axisIndex) - Mathf.Min(topOffset, height * 0.25f);
        float targetFillPercent = forceShowLiquidVisual ? Mathf.Max(FillPercent, 0.5f) : FillPercent;
        float axisPosition = Mathf.Lerp(bottom, top, Mathf.Clamp01(targetFillPercent)) + liquidHeightOffset;

        Vector3 anchorPosition = bounds.center + liquidLocalCenterOffset;
        SetAxisValue(ref anchorPosition, axisIndex, axisPosition);
        liquidAnchorLocalPosition = anchorPosition;
        liquidAnchor.localPosition = liquidAnchorLocalPosition;
        liquidAnchor.localRotation = AxisRotation();

        liquidRadius = EstimateOpeningRadius(bounds, axisIndex) * 0.9f;
        liquidRadiusMultiplier = bucketMesh != null && bucketMesh.topRadius > 0.001f
            ? liquidRadius / bucketMesh.topRadius
            : liquidRadiusMultiplier;
        RebuildLiquidMesh();
        UpdateVisual(999f);
    }

    public void SetLiquidAnchorLocalPosition(Vector3 targetLocalPosition)
    {
        Vector3 current = liquidAnchorLocalPosition;
        int axisIndex = AxisIndex(liquidAxis);

        liquidLocalCenterOffset += new Vector3(
            axisIndex == 0 ? 0f : targetLocalPosition.x - current.x,
            axisIndex == 1 ? 0f : targetLocalPosition.y - current.y,
            axisIndex == 2 ? 0f : targetLocalPosition.z - current.z
        );

        liquidHeightOffset += GetAxisValue(targetLocalPosition, axisIndex) - GetAxisValue(current, axisIndex);
        liquidAnchorLocalPosition = targetLocalPosition;
        if (liquidAnchor != null)
        {
            liquidAnchor.localPosition = liquidAnchorLocalPosition;
        }
        UpdateVisual(999f);
    }

    public void RebuildLiquidMesh()
    {
        if (liquidVisual == null)
        {
            return;
        }

        MeshFilter meshFilter = liquidVisual.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = liquidVisual.gameObject.AddComponent<MeshFilter>();
        }

        if (liquidMesh != null)
        {
            if (Application.isPlaying) Destroy(liquidMesh);
            else DestroyImmediate(liquidMesh);
        }

        liquidMesh = BuildLiquidMesh();
        meshFilter.sharedMesh = liquidMesh;
    }

    private Mesh BuildLiquidMesh()
    {
        int segments = bucketMesh != null ? Mathf.Clamp(bucketMesh.segments, 24, 96) : 64;
        float radius = Mathf.Max(0.02f, liquidRadius);
        float halfThickness = Mathf.Max(0.002f, surfaceThickness * 0.5f);
        Mesh mesh = new Mesh();
        mesh.name = "Bucket Liquid Thick Double Sided Surface";
        Vector3[] vertices = new Vector3[segments * 2 + 2];
        int[] triangles = new int[segments * 12];
        vertices[0] = new Vector3(0f, halfThickness, 0f);
        vertices[1] = new Vector3(0f, -halfThickness, 0f);
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            vertices[2 + i] = new Vector3(x, halfThickness, z);
            vertices[2 + segments + i] = new Vector3(x, -halfThickness, z);
        }

        int t = 0;
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int topA = 2 + i;
            int topB = 2 + next;
            int bottomA = 2 + segments + i;
            int bottomB = 2 + segments + next;

            triangles[t++] = 0;
            triangles[t++] = topA;
            triangles[t++] = topB;

            triangles[t++] = 1;
            triangles[t++] = bottomB;
            triangles[t++] = bottomA;

            triangles[t++] = topA;
            triangles[t++] = bottomA;
            triangles[t++] = topB;

            triangles[t++] = topB;
            triangles[t++] = bottomA;
            triangles[t++] = bottomB;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void UpdateVisual(float dt)
    {
        EnsureLiquidVisual();
        if (liquidVisual == null || liquidMaterial == null)
        {
            return;
        }

        float targetFillPercent = forceShowLiquidVisual ? 0.5f : FillPercent;
        visualFillPercent = Mathf.Lerp(visualFillPercent, targetFillPercent, 1f - Mathf.Exp(-smoothFillSpeed * Mathf.Max(0f, dt)));
        Bounds bounds = GetBucketLocalBounds();
        int axisIndex = AxisIndex(liquidAxis);
        float height = Mathf.Max(0.01f, GetAxisSize(bounds.size, axisIndex));
        float low = GetAxisMin(bounds, axisIndex) + Mathf.Min(bottomOffset, height * 0.25f);
        float high = GetAxisMax(bounds, axisIndex) - Mathf.Min(topOffset, height * 0.25f);
        float bottom = flipAxis ? high : low;
        float top = flipAxis ? low : high;
        float axisPosition = Mathf.Lerp(bottom, top, visualFillPercent) + liquidHeightOffset;
        liquidAnchorLocalPosition = bounds.center + liquidLocalCenterOffset;
        SetAxisValue(ref liquidAnchorLocalPosition, axisIndex, axisPosition);
        if (liquidAnchor != null)
        {
            liquidAnchor.localPosition = liquidAnchorLocalPosition;
            liquidAnchor.localRotation = AxisRotation();
        }

        Vector3 currentVelocity = dt > 0f ? (transform.position - previousPosition) / dt : Vector3.zero;
        Vector3 acceleration = dt > 0f ? (currentVelocity - velocity) / dt : Vector3.zero;
        velocity = currentVelocity;
        previousPosition = transform.position;
        Vector3 localAcceleration = transform.InverseTransformDirection(acceleration);
        float tiltX = Mathf.Clamp(localAcceleration.z * sloshAmount, -6f, 6f);
        float tiltZ = Mathf.Clamp(-localAcceleration.x * sloshAmount, -6f, 6f);
        liquidVisual.localPosition = Vector3.zero;
        liquidVisual.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);
        liquidVisual.localScale = Vector3.one * liquidDebugScale;

        bool visible = forceShowLiquidVisual || (!IsEmpty && visualFillPercent > 0.003f);
        liquidVisual.gameObject.SetActive(visible);
        if (liquidRenderer != null)
        {
            liquidRenderer.enabled = visible;
        }
        Color visibleColor = forceShowLiquidVisual ? forceShowColor : VisiblePaintColor;
        visibleColor.a = 1f;
        liquidMaterial.color = visibleColor;
        SetMaterialColor(liquidMaterial, "_BaseColor", visibleColor);
        SetMaterialColor(liquidMaterial, "_Color", visibleColor);
        SetMaterialFloat(liquidMaterial, "_Surface", 0f);
        liquidMaterial.renderQueue = 2450;
    }

    private Vector3 AxisVector(float value)
    {
        float signed = flipAxis ? -value : value;
        switch (liquidAxis)
        {
            case LiquidAxis.LocalX:
                return new Vector3(signed, 0f, 0f);
            case LiquidAxis.LocalZ:
                return new Vector3(0f, 0f, signed);
            default:
                return new Vector3(0f, signed, 0f);
        }
    }

    private Bounds GetBucketLocalBounds()
    {
        MeshFilter meshFilter = bucketMesh != null ? bucketMesh.GetComponent<MeshFilter>() : GetComponentInChildren<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null || meshFilter.sharedMesh.vertexCount == 0)
        {
            return new Bounds(Vector3.zero, new Vector3(1f, 1.4f, 1f));
        }

        Vector3[] vertices = meshFilter.sharedMesh.vertices;
        Vector3 first = transform.InverseTransformPoint(meshFilter.transform.TransformPoint(vertices[0]));
        Bounds bounds = new Bounds(first, Vector3.zero);
        for (int i = 1; i < vertices.Length; i++)
        {
            bounds.Encapsulate(transform.InverseTransformPoint(meshFilter.transform.TransformPoint(vertices[i])));
        }
        return bounds;
    }

    private LiquidAxis GetLargestBoundsAxis(Bounds bounds)
    {
        Vector3 size = bounds.size;
        if (size.x >= size.y && size.x >= size.z)
        {
            return LiquidAxis.LocalX;
        }
        if (size.z >= size.x && size.z >= size.y)
        {
            return LiquidAxis.LocalZ;
        }
        return LiquidAxis.LocalY;
    }

    private float EstimateOpeningRadius(Bounds bounds, int axisIndex)
    {
        MeshFilter meshFilter = bucketMesh != null ? bucketMesh.GetComponent<MeshFilter>() : GetComponentInChildren<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null || meshFilter.sharedMesh.vertexCount == 0)
        {
            return Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.z));
        }

        float top = GetAxisMax(bounds, axisIndex);
        float height = Mathf.Max(0.01f, GetAxisSize(bounds.size, axisIndex));
        float topBandMin = top - height * 0.12f;
        Vector3 center = bounds.center;
        float radius = 0f;
        Vector3[] vertices = meshFilter.sharedMesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 point = transform.InverseTransformPoint(meshFilter.transform.TransformPoint(vertices[i]));
            if (GetAxisValue(point, axisIndex) < topBandMin)
            {
                continue;
            }
            Vector2 radial = GetRadial(point - center, axisIndex);
            radius = Mathf.Max(radius, radial.magnitude);
        }

        if (radius <= 0.001f)
        {
            Vector3 extents = bounds.extents;
            radius = axisIndex == 0 ? Mathf.Min(extents.y, extents.z) : axisIndex == 1 ? Mathf.Min(extents.x, extents.z) : Mathf.Min(extents.x, extents.y);
        }
        return Mathf.Max(0.02f, radius);
    }

    private Quaternion AxisRotation()
    {
        Quaternion flip = flipAxis ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;
        switch (liquidAxis)
        {
            case LiquidAxis.LocalX:
                return Quaternion.Euler(0f, 0f, -90f) * flip;
            case LiquidAxis.LocalZ:
                return Quaternion.Euler(90f, 0f, 0f) * flip;
            default:
                return flip;
        }
    }

    private string BuildColorComponentsSummary()
    {
        EnsureColorComponent();
        string summary = "";
        for (int i = 0; i < paintComponents.Count; i++)
        {
            if (i > 0)
            {
                summary += ", ";
            }
            PaintComponent component = paintComponents[i];
            summary += "#" + ColorUtility.ToHtmlStringRGB(component.color) + " (" + component.amount.ToString("0.00") + ")";
        }
        return summary;
    }

    private string BuildLiquidDebugSummary()
    {
        return
            "Reservoir component found: Yes" +
            "\nBucketLiquidAnchor found: " + (HasLiquidAnchor ? "Yes" : "No") +
            "\nBucketLiquidVisual exists: " + (HasLiquidVisual ? "Yes" : "No") +
            "\nRenderer enabled: " + (LiquidRendererEnabled ? "Yes" : "No") +
            "\nLiquid local position: " + FormatVector3(LiquidLocalPosition) +
            "\nLiquid world position: " + FormatVector3(LiquidWorldPosition) +
            "\nLiquid local scale: " + FormatVector3(LiquidLocalScale) +
            "\nLiquid material color: #" + ColorUtility.ToHtmlStringRGB(LiquidMaterialColor) +
            "\nCurrent paint amount: " + currentPaintAmount.ToString("0.000") +
            "\nCapacity: " + capacity.ToString("0.000") +
            "\nFill percent: " + (FillPercent * 100f).ToString("0.0") + "%" +
            "\nBucket state: " + BucketState +
            "\nForce show liquid: " + (forceShowLiquidVisual ? "On" : "Off") +
            "\nLiquid axis: " + liquidAxis;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }

            Transform found = FindDeepChild(child, childName);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static int AxisIndex(LiquidAxis axis)
    {
        return axis == LiquidAxis.LocalX ? 0 : axis == LiquidAxis.LocalZ ? 2 : 1;
    }

    private static float GetAxisValue(Vector3 value, int axisIndex)
    {
        return axisIndex == 0 ? value.x : axisIndex == 1 ? value.y : value.z;
    }

    private static void SetAxisValue(ref Vector3 value, int axisIndex, float axisValue)
    {
        if (axisIndex == 0) value.x = axisValue;
        else if (axisIndex == 1) value.y = axisValue;
        else value.z = axisValue;
    }

    private static float GetAxisSize(Vector3 size, int axisIndex)
    {
        return axisIndex == 0 ? size.x : axisIndex == 1 ? size.y : size.z;
    }

    private static float GetAxisMin(Bounds bounds, int axisIndex)
    {
        return GetAxisValue(bounds.min, axisIndex);
    }

    private static float GetAxisMax(Bounds bounds, int axisIndex)
    {
        return GetAxisValue(bounds.max, axisIndex);
    }

    private static Vector2 GetRadial(Vector3 value, int axisIndex)
    {
        if (axisIndex == 0) return new Vector2(value.y, value.z);
        if (axisIndex == 1) return new Vector2(value.x, value.z);
        return new Vector2(value.x, value.y);
    }

    private static string FormatVector3(Vector3 value)
    {
        return value.x.ToString("0.00") + ", " + value.y.ToString("0.00") + ", " + value.z.ToString("0.00");
    }

    private static void SetMaterialColor(Material material, string propertyName, Color color)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, color);
        }
    }

    private static void SetMaterialFloat(Material material, string propertyName, float value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private void OnDestroy()
    {
        if (liquidMesh != null)
        {
            if (Application.isPlaying) Destroy(liquidMesh);
            else DestroyImmediate(liquidMesh);
        }

        if (liquidMaterial != null)
        {
            if (Application.isPlaying) Destroy(liquidMaterial);
            else DestroyImmediate(liquidMaterial);
        }
    }
}
