using System.IO;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class PaintingSurface : MonoBehaviour
{
    public enum TrailRenderMode
    {
        Dots,
        Trails,
        Ribbon
    }

    [Header("Texture")]
    public int textureWidth = 1024;
    public int textureHeight = 1024;
    public Color clearColor = Color.white;

    [Header("Painting")]
    public Color paintColor = new Color(0.1f, 0.25f, 1f, 1f);
    [Range(0.001f, 0.25f)] public float brushRadius = 0.035f;
    [Range(0.01f, 1f)] public float opacity = 0.75f;
    [Range(0f, 1f)] public float spreading = 0.25f;
    [Range(0.001f, 0.2f)] public float splatRadius = 0.025f;
    [Range(0.01f, 1f)] public float splatOpacity = 0.55f;
    [Range(0.01f, 2f)] public float accumulationStrength = 0.55f;
    [Range(0.1f, 4f)] public float maxWetness = 1.6f;
    [Range(0f, 2f)] public float spreadStrength = 0.45f;
    [Range(0f, 0.25f)] public float dryingRate = 0f;
    [Range(0.001f, 0.5f)] public float paintedAreaThreshold = 0.035f;

    [Header("Trails")]
    public bool trailModeEnabled = true;
    public TrailRenderMode trailMode = TrailRenderMode.Ribbon;
    [Range(0.001f, 0.2f)] public float connectDistanceThreshold = 0.04f;
    [Range(0.001f, 0.08f)] public float strokeStepSpacing = 0.008f;
    [Range(0.001f, 0.2f)] public float strokeRadius = 0.02f;
    [Range(0.01f, 1f)] public float strokeOpacity = 0.65f;
    [Range(0f, 1f)] public float strokeSmoothing = 0.72f;
    [Range(0.01f, 0.5f)] public float maxStrokeTimeGap = 0.1f;
    [Range(-1f, 1f)] public float minDirectionDot = 0.35f;
    [Range(0f, 30f)] public float maxConnectImpactSpeed = 18f;

    [Header("UV Mapping")]
    public bool flipU = true;
    public bool flipV = false;
    public bool swapUV = false;
    public bool showMappingDebugMarkers = true;
    public float mappingDebugMarkerSeconds = 1.2f;

    [Header("Surface")]
    public Vector2 localHalfExtents = new Vector2(5f, 5f);
    public string surfaceType = "Canvas";
    public string orientation = "Horizontal";
    public float currentWidth = 10f;
    public float currentHeight = 10f;
    public float minCanvasSize = 1f;
    public float maxCanvasSize = 20f;
    public float tiltAngle = 28f;

    public int MarkCount { get; private set; }
    public float EstimatedPaintedArea01 => paintedPixelCount / (float)(textureWidth * textureHeight);
    public Texture2D PaintTexture => paintTexture;
    public Vector3 BoardRotationEuler => transform.localEulerAngles;
    public Vector3 BoardScale => transform.localScale;
    public float AverageWetness => wetness != null && wetness.Length > 0 ? totalWetness / wetness.Length : 0f;
    public float LastAppliedSplatRadius => lastAppliedSplatRadius;
    public float LastViscosityEffect => lastViscosityEffect;
    public float LastImpactSpeed => lastImpactSpeed;
    public int ConnectedStrokeCount => connectedStrokeCount;
    public int RejectedConnectionCount => rejectedConnectionCount;
    public float AverageStrokeLength => connectedStrokeCount > 0 ? totalConnectedStrokeLength / connectedStrokeCount : 0f;
    public int ActiveStreamCount => CountActiveStreams();
    public Vector2 LastHitUv => lastValidHitUv;
    public Vector2 CurrentHitUv => currentHitUv;
    public Vector3 LastWorldHit => lastWorldHit;
    public Vector3 LastLocalHit => lastLocalHit;
    public Vector2 LastPixelHit => lastPixelHit;
    public Vector3 LastUvToWorldCheck => lastUvToWorldCheck;
    public float MappingErrorDistance => mappingErrorDistance;
    public SurfaceBehavior CurrentSurfaceBehavior => GetSurfaceBehavior(surfaceType);

    private Texture2D paintTexture;
    private Material materialInstance;
    private Renderer cachedRenderer;
    private bool textureDirty;
    private bool[] paintedPixels;
    private float[] wetness;
    private Color[] paintPixels;
    private int paintedPixelCount;
    private float totalWetness;
    private float dryingTimer;
    private float lastAppliedSplatRadius;
    private float lastViscosityEffect = 1f;
    private float lastImpactSpeed;
    private Vector2 lastValidHitUv;
    private Vector2 currentHitUv;
    private float lastValidHitTime = -999f;
    private bool hasLastValidHit;
    private int connectedStrokeCount;
    private int rejectedConnectionCount;
    private float totalConnectedStrokeLength;
    private Vector3 lastWorldHit;
    private Vector3 lastLocalHit;
    private Vector2 lastPixelHit;
    private Vector3 lastUvToWorldCheck;
    private float mappingErrorDistance;
    private float debugMarkerTimer;
    private bool hasBaseTransform;
    private Vector3 baseLocalPosition;
    private readonly StrokeStreamState[] streamStates = new StrokeStreamState[96];

    private void Awake()
    {
        Initialize();
    }

    private void OnValidate()
    {
        textureWidth = Mathf.Clamp(textureWidth, 128, 4096);
        textureHeight = Mathf.Clamp(textureHeight, 128, 4096);
        brushRadius = Mathf.Max(0.001f, brushRadius);
        opacity = Mathf.Clamp01(opacity);
        spreading = Mathf.Clamp01(spreading);
        splatRadius = Mathf.Max(0.001f, splatRadius);
        splatOpacity = Mathf.Clamp01(splatOpacity);
        accumulationStrength = Mathf.Max(0.01f, accumulationStrength);
        maxWetness = Mathf.Max(0.1f, maxWetness);
        spreadStrength = Mathf.Max(0f, spreadStrength);
        dryingRate = Mathf.Clamp(dryingRate, 0f, 0.25f);
        paintedAreaThreshold = Mathf.Clamp(paintedAreaThreshold, 0.001f, maxWetness);
        connectDistanceThreshold = Mathf.Clamp(connectDistanceThreshold, 0.001f, 0.2f);
        strokeStepSpacing = Mathf.Clamp(strokeStepSpacing, 0.001f, 0.08f);
        strokeRadius = Mathf.Clamp(strokeRadius, 0.001f, 0.2f);
        strokeOpacity = Mathf.Clamp01(strokeOpacity);
        strokeSmoothing = Mathf.Clamp01(strokeSmoothing);
        maxStrokeTimeGap = Mathf.Clamp(maxStrokeTimeGap, 0.01f, 0.5f);
        minDirectionDot = Mathf.Clamp(minDirectionDot, -1f, 1f);
        maxConnectImpactSpeed = Mathf.Max(0f, maxConnectImpactSpeed);
        mappingDebugMarkerSeconds = Mathf.Max(0.05f, mappingDebugMarkerSeconds);
        localHalfExtents.x = Mathf.Max(0.001f, localHalfExtents.x);
        localHalfExtents.y = Mathf.Max(0.001f, localHalfExtents.y);
        minCanvasSize = Mathf.Max(0.1f, minCanvasSize);
        maxCanvasSize = Mathf.Max(minCanvasSize, maxCanvasSize);
        currentWidth = Mathf.Clamp(currentWidth, minCanvasSize, maxCanvasSize);
        currentHeight = Mathf.Clamp(currentHeight, minCanvasSize, maxCanvasSize);
        tiltAngle = Mathf.Clamp(tiltAngle, 0f, 60f);
    }

    public void Initialize()
    {
        cachedRenderer = GetComponent<Renderer>();
        if (cachedRenderer == null)
        {
            Debug.LogWarning("PaintingSurface needs a Renderer.");
            return;
        }

        if (!hasBaseTransform)
        {
            baseLocalPosition = transform.localPosition;
            hasBaseTransform = true;
        }

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Bounds bounds = meshFilter.sharedMesh.bounds;
            localHalfExtents = new Vector2(
                Mathf.Max(0.001f, bounds.extents.x),
                Mathf.Max(0.001f, bounds.extents.z)
            );
        }

        if (materialInstance == null)
        {
            if (cachedRenderer.sharedMaterial != null)
            {
                materialInstance = new Material(cachedRenderer.sharedMaterial);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                materialInstance = shader != null ? new Material(shader) : null;
            }

            if (materialInstance == null)
            {
                Debug.LogWarning("PaintingSurface could not create a material for the painting texture.");
                return;
            }

            cachedRenderer.sharedMaterial = materialInstance;
        }

        bool needsInitialClear = paintTexture == null || paintedPixels == null;
        CreateTextureIfNeeded();
        ApplySurfaceMaterial();

        if (currentWidth <= 0.001f)
        {
            currentWidth = localHalfExtents.x * 2f * transform.localScale.x;
        }

        if (currentHeight <= 0.001f)
        {
            currentHeight = localHalfExtents.y * 2f * transform.localScale.z;
        }

        if (needsInitialClear)
        {
            ClearPainting();
        }
    }

    public void ApplyCanvasSettings(float width, float height, string newSurfaceType, string newOrientation)
    {
        float requestedTilt = string.Equals(newOrientation, "Tilted", System.StringComparison.OrdinalIgnoreCase)
            ? (tiltAngle > 0.1f ? tiltAngle : 28f)
            : 0f;
        ApplyCanvasSettings(width, height, newSurfaceType, newOrientation, requestedTilt);
    }

    public void ApplyCanvasSettings(float width, float height, string newSurfaceType, string newOrientation, float requestedTiltAngle)
    {
        if (cachedRenderer == null || materialInstance == null)
        {
            Initialize();
        }

        currentWidth = Mathf.Clamp(width, minCanvasSize, maxCanvasSize);
        currentHeight = Mathf.Clamp(height, minCanvasSize, maxCanvasSize);
        surfaceType = NormalizeSurfaceType(newSurfaceType);
        orientation = string.Equals(newOrientation, "Tilted", System.StringComparison.OrdinalIgnoreCase)
            ? "Tilted"
            : "Horizontal";
        tiltAngle = orientation == "Tilted" ? Mathf.Clamp(requestedTiltAngle, 0f, 60f) : 0f;

        Vector3 scale = transform.localScale;
        scale.x = currentWidth / Mathf.Max(0.001f, localHalfExtents.x * 2f);
        scale.z = currentHeight / Mathf.Max(0.001f, localHalfExtents.y * 2f);
        transform.localScale = scale;

        Vector3 euler = transform.localEulerAngles;
        euler.x = orientation == "Tilted" ? tiltAngle : 0f;
        transform.localEulerAngles = euler;

        if (hasBaseTransform)
        {
            Vector3 position = baseLocalPosition;
            if (orientation == "Tilted")
            {
                float edgeDrop = Mathf.Sin(tiltAngle * Mathf.Deg2Rad) * currentHeight * 0.5f;
                position.y = Mathf.Max(position.y, edgeDrop + 0.05f);
            }

            transform.localPosition = position;
        }

        CreateTextureIfNeeded();
        ApplySurfaceMaterial();
    }

    public void ClearPainting()
    {
        CreateTextureIfNeeded();

        Color[] pixels = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = clearColor;
        }

        paintTexture.SetPixels(pixels);
        paintTexture.Apply();
        materialInstance.mainTexture = paintTexture;
        materialInstance.mainTextureScale = Vector2.one;
        materialInstance.mainTextureOffset = Vector2.zero;
        ApplySurfaceMaterial();

        MarkCount = 0;
        paintedPixelCount = 0;
        paintedPixels = new bool[textureWidth * textureHeight];
        wetness = new float[textureWidth * textureHeight];
        paintPixels = pixels;
        totalWetness = 0f;
        hasLastValidHit = false;
        lastValidHitTime = -999f;
        connectedStrokeCount = 0;
        rejectedConnectionCount = 0;
        totalConnectedStrokeLength = 0f;
        currentHitUv = Vector2.zero;
        for (int i = 0; i < streamStates.Length; i++)
        {
            streamStates[i] = default;
        }
        textureDirty = false;
    }

    public bool TryPaintSegment(Vector3 previousWorldPosition, Vector3 currentWorldPosition, Color color, float radius, float alpha, float velocityMagnitude, float viscosity)
    {
        return TryPaintSegment(previousWorldPosition, currentWorldPosition, color, radius, alpha, velocityMagnitude, viscosity, 0.08f, 3f);
    }

    public bool TryPaintSegment(
        Vector3 previousWorldPosition,
        Vector3 currentWorldPosition,
        Color color,
        float radius,
        float alpha,
        float velocityMagnitude,
        float viscosity,
        float holeDiameter,
        float exitSpeed,
        float gravityMagnitude = 9.81f
    )
    {
        return TryPaintSegment(previousWorldPosition, currentWorldPosition, color, radius, alpha, velocityMagnitude, viscosity, holeDiameter, exitSpeed, gravityMagnitude, 0, Vector3.zero);
    }

    public bool TryPaintSegment(
        Vector3 previousWorldPosition,
        Vector3 currentWorldPosition,
        Color color,
        float radius,
        float alpha,
        float velocityMagnitude,
        float viscosity,
        float holeDiameter,
        float exitSpeed,
        float gravityMagnitude,
        int streamId,
        Vector3 emitterVelocityWorld
    )
    {
        CreateTextureIfNeeded();

        Vector3 localA = transform.InverseTransformPoint(previousWorldPosition);
        Vector3 localB = transform.InverseTransformPoint(currentWorldPosition);
        bool crossesPlane = (localA.y > 0f && localB.y <= 0f) || (localA.y < 0f && localB.y >= 0f);

        if (!crossesPlane)
        {
            return false;
        }

        float denominator = localA.y - localB.y;
        if (Mathf.Abs(denominator) <= 0.0001f)
        {
            return false;
        }

        float t = localA.y / denominator;
        if (t < 0f || t > 1f)
        {
            return false;
        }

        t = Mathf.Clamp01(t);
        Vector3 hitLocal = Vector3.Lerp(localA, localB, t);

        if (Mathf.Abs(hitLocal.x) > localHalfExtents.x || Mathf.Abs(hitLocal.z) > localHalfExtents.y)
        {
            return false;
        }

        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        Vector2 uv = LocalToUv(hitLocal);
        RecordMappingDebug(hitLocal, uv);
        Vector3 localMotion = transform.InverseTransformVector(currentWorldPosition - previousWorldPosition);
        Vector2 impactDirectionUv = LocalDeltaToUvDelta(localMotion);

        if (impactDirectionUv.sqrMagnitude < 0.000001f && emitterVelocityWorld.sqrMagnitude > 0.0001f)
        {
            Vector3 localEmitterVelocity = transform.InverseTransformVector(emitterVelocityWorld);
            impactDirectionUv = LocalDeltaToUvDelta(localEmitterVelocity);
        }

        float safeViscosity = Mathf.Max(0.001f, viscosity);
        float viscositySpread = 1f / (1f + safeViscosity * 0.9f);
        float velocitySpread = Mathf.Clamp01(velocityMagnitude / Mathf.Max(0.001f, exitSpeed + 4f));
        float holeSpread = Mathf.Clamp(holeDiameter / 0.08f, 0.5f, 2.2f);
        float radius01 = Mathf.Max(radius, splatRadius);
        float impactSpeedFactor = Mathf.Lerp(0.75f, 1.45f, velocitySpread);
        float viscosityThickness = Mathf.Lerp(1.25f, 0.72f, viscositySpread);
        float absorptionDrying = Mathf.Lerp(1.05f, 0.48f, behavior.absorption);
        float frictionDrag = Mathf.Lerp(1.14f, 0.76f, behavior.friction);
        float restitutionSpread = 1f + behavior.bounce * 0.35f;
        float spreadFactor =
            behavior.spreadMultiplier *
            frictionDrag *
            restitutionSpread *
            (0.58f + viscositySpread * spreadStrength) *
            impactSpeedFactor *
            Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(0.5f, 2.2f, holeSpread));
        float impactOpacity = Mathf.Clamp01(alpha * splatOpacity * behavior.opacityMultiplier * Mathf.Lerp(1.12f, 0.78f, viscositySpread));
        float impactAmount = Mathf.Clamp(
            accumulationStrength *
            behavior.wetnessRetention *
            absorptionDrying *
            Mathf.Lerp(0.75f, 1.25f, velocitySpread) *
            Mathf.Lerp(0.8f, 1.2f, holeSpread) *
            viscosityThickness,
            0.01f,
            maxWetness
        );

        lastViscosityEffect = viscositySpread;
        lastImpactSpeed = velocityMagnitude;
        lastAppliedSplatRadius = radius01 * spreadFactor;
        PaintAtUV(uv, color, lastAppliedSplatRadius, impactOpacity, impactAmount, behavior, safeViscosity, gravityMagnitude, velocitySpread, streamId, impactDirectionUv);
        return true;
    }

    public string SavePng(string directory, string filePrefix)
    {
        CreateTextureIfNeeded();
        Directory.CreateDirectory(directory);

        ApplyIfDirty();

        string safePrefix = string.IsNullOrWhiteSpace(filePrefix) ? "painting" : filePrefix;
        string path = Path.Combine(directory, safePrefix + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        File.WriteAllBytes(path, paintTexture.EncodeToPNG());
        return path;
    }

    public void DrawMappingTestPattern()
    {
        ClearPainting();

        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        float radius = Mathf.Max(0.018f, strokeRadius);
        PaintAtUV(LocalToUv(new Vector3(-localHalfExtents.x * 0.82f, 0f, 0f)), Color.red, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10001, Vector2.right);
        PaintAtUV(LocalToUv(new Vector3(localHalfExtents.x * 0.82f, 0f, 0f)), Color.green, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10002, Vector2.left);
        PaintAtUV(LocalToUv(new Vector3(0f, 0f, localHalfExtents.y * 0.82f)), Color.blue, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10003, Vector2.down);
        PaintAtUV(LocalToUv(new Vector3(0f, 0f, -localHalfExtents.y * 0.82f)), Color.yellow, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10004, Vector2.up);
        PaintAtUV(LocalToUv(Vector3.zero), Color.white, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10005, Vector2.right);
        ApplyIfDirty();
    }

    private void LateUpdate()
    {
        ApplyDrying();
        ApplyIfDirty();
    }

    private void PaintAtUV(Vector2 uv, Color color, float radius01, float alpha, float impactAmount, SurfaceBehavior behavior, float viscosity, float gravityMagnitude, float velocitySpread, int streamId, Vector2 impactDirectionUv)
    {
        EnsurePaintBuffers();

        currentHitUv = uv;
        int centerX = Mathf.RoundToInt(uv.x * (textureWidth - 1));
        int centerY = Mathf.RoundToInt(uv.y * (textureHeight - 1));
        int minX = textureWidth - 1;
        int maxX = 0;
        int minY = textureHeight - 1;
        int maxY = 0;

        Color source = color;
        source.a = Mathf.Clamp01(alpha);
        Vector2 anchorUv = uv;
        bool canDrawTrail = trailModeEnabled && trailMode != TrailRenderMode.Dots;
        bool connected = canDrawTrail && TryFindStrokeAnchor(streamId, uv, impactDirectionUv, lastImpactSpeed, behavior, out anchorUv);
        Vector2 directionUv = connected ? uv - anchorUv : GetFallbackDirection(uv, impactDirectionUv);
        float directionLength = directionUv.magnitude;
        Vector2 directionPixels = directionLength > 0.0001f
            ? new Vector2(directionUv.x * textureWidth, directionUv.y * textureHeight).normalized
            : Vector2.right;

        if (connected)
        {
            DrawStrokeSegment(
                anchorUv,
                uv,
                source,
                radius01,
                impactAmount,
                behavior,
                viscosity,
                velocitySpread,
                ref minX,
                ref maxX,
                ref minY,
                ref maxY,
                trailMode == TrailRenderMode.Ribbon
            );
            connectedStrokeCount++;
            float strokePixels = Vector2.Distance(
                new Vector2(anchorUv.x * textureWidth, anchorUv.y * textureHeight),
                new Vector2(uv.x * textureWidth, uv.y * textureHeight)
            );
            totalConnectedStrokeLength += strokePixels;
        }

        float speedStretch = Mathf.Lerp(0.08f, 1.1f, velocitySpread);
        float viscosityStretch = 1f / (1f + viscosity * 0.8f);
        float surfaceStretch = Mathf.Lerp(0.75f, 1.35f, behavior.flowDownSlope);
        float stretchAmount = canDrawTrail ? speedStretch * viscosityStretch * surfaceStretch : 0f;
        float ribbonBlend = trailMode == TrailRenderMode.Ribbon ? 0.62f : 1f;
        float splatImpact = impactAmount * Mathf.Lerp(1.25f, 0.75f, velocitySpread) * ribbonBlend;
        float impactRadius = radius01 * Mathf.Lerp(1.14f, 0.92f, velocitySpread) * Mathf.Lerp(1f, 0.72f, trailMode == TrailRenderMode.Ribbon ? 1f : 0f);
        StampSoftBrush(
            centerX,
            centerY,
            impactRadius,
            source,
            splatImpact,
            behavior,
            directionPixels,
            stretchAmount,
            ref minX,
            ref maxX,
            ref minY,
            ref maxY
        );

        MarkCount++;
        UpdateStrokeStream(streamId, uv, impactDirectionUv.sqrMagnitude > 0.000001f ? impactDirectionUv.normalized : directionUv.normalized, radius01);
        ApplySlopeFlow(minX, maxX, minY, maxY, behavior, viscosity, gravityMagnitude);
        textureDirty = true;
    }

    private void DrawStrokeSegment(
        Vector2 fromUv,
        Vector2 toUv,
        Color color,
        float impactRadius01,
        float impactAmount,
        SurfaceBehavior behavior,
        float viscosity,
        float velocitySpread,
        ref int minX,
        ref int maxX,
        ref int minY,
        ref int maxY,
        bool ribbon
    )
    {
        Vector2 deltaUv = toUv - fromUv;
        float distanceUv = deltaUv.magnitude;
        if (distanceUv <= 0.0001f)
        {
            return;
        }

        Vector2 directionPixels = new Vector2(deltaUv.x * textureWidth, deltaUv.y * textureHeight).normalized;
        float viscositySpread = 1f / (1f + viscosity * 0.9f);
        float denseSlowPaint = Mathf.Lerp(1.25f, 0.7f, velocitySpread);
        float surfaceConnection = Mathf.Lerp(0.72f, 1.28f, behavior.wetnessRetention) * Mathf.Lerp(1.1f, 0.62f, behavior.absorption);
        float radius = Mathf.Max(impactRadius01 * 0.72f, strokeRadius) *
            Mathf.Lerp(0.82f, 1.28f, viscositySpread) *
            surfaceConnection *
            Mathf.Lerp(1.18f, 0.88f, velocitySpread);
        if (ribbon)
        {
            radius = Mathf.Clamp(radius, strokeRadius * 0.85f, strokeRadius * 1.45f);
        }

        float step = Mathf.Max(0.001f, strokeStepSpacing * Mathf.Lerp(1.15f, 0.45f, strokeSmoothing) * (ribbon ? 0.72f : 1f));
        int steps = Mathf.Clamp(Mathf.CeilToInt(distanceUv / step), 1, 96);
        float segmentOpacity = Mathf.Clamp01(strokeOpacity * color.a * surfaceConnection * Mathf.Lerp(1.18f, 0.78f, velocitySpread) * (ribbon ? 0.82f : 1f));
        float segmentAmount = impactAmount * (ribbon ? 0.52f : 0.72f) * denseSlowPaint * surfaceConnection;
        float stretchAmount = Mathf.Lerp(0.15f, ribbon ? 0.72f : 1.45f, velocitySpread) * (1f / (1f + viscosity * 0.65f)) * Mathf.Lerp(0.75f, 1.35f, behavior.flowDownSlope);

        Color strokeColor = color;
        strokeColor.a = segmentOpacity;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 sample = Vector2.Lerp(fromUv, toUv, t);
            float middleWeight = Mathf.Sin(t * Mathf.PI);
            float localRadius = radius * Mathf.Lerp(ribbon ? 0.95f : 0.82f, ribbon ? 1.04f : 1.08f, middleWeight);
            int x = Mathf.RoundToInt(sample.x * (textureWidth - 1));
            int y = Mathf.RoundToInt(sample.y * (textureHeight - 1));
            StampSoftBrush(
                x,
                y,
                localRadius,
                strokeColor,
                segmentAmount,
                behavior,
                directionPixels,
                stretchAmount,
                ref minX,
                ref maxX,
                ref minY,
                ref maxY
            );
        }
    }

    private void StampSoftBrush(
        int centerX,
        int centerY,
        float radius01,
        Color source,
        float impactAmount,
        SurfaceBehavior behavior,
        Vector2 stretchDirection,
        float stretchAmount,
        ref int minX,
        ref int maxX,
        ref int minY,
        ref int maxY
    )
    {
        int pixelRadius = Mathf.Max(1, Mathf.RoundToInt(radius01 * Mathf.Min(textureWidth, textureHeight)));
        int stretchPadding = Mathf.CeilToInt(pixelRadius * Mathf.Clamp(stretchAmount, 0f, 2.2f));
        int localMinX = Mathf.Max(0, centerX - pixelRadius - stretchPadding);
        int localMaxX = Mathf.Min(textureWidth - 1, centerX + pixelRadius + stretchPadding);
        int localMinY = Mathf.Max(0, centerY - pixelRadius - stretchPadding);
        int localMaxY = Mathf.Min(textureHeight - 1, centerY + pixelRadius + stretchPadding);
        minX = Mathf.Min(minX, localMinX);
        maxX = Mathf.Max(maxX, localMaxX);
        minY = Mathf.Min(minY, localMinY);
        maxY = Mathf.Max(maxY, localMaxY);

        Vector2 tangent = stretchDirection.sqrMagnitude > 0.001f ? stretchDirection.normalized : Vector2.right;
        Vector2 normal = new Vector2(-tangent.y, tangent.x);
        float majorScale = 1f + Mathf.Clamp(stretchAmount, 0f, 2.2f);
        float minorScale = Mathf.Lerp(1.08f, 0.78f, Mathf.Clamp01(stretchAmount));

        for (int y = localMinY; y <= localMaxY; y++)
        {
            for (int x = localMinX; x <= localMaxX; x++)
            {
                Vector2 offset = new Vector2(x - centerX, y - centerY);
                float along = Vector2.Dot(offset, tangent) / (pixelRadius * majorScale);
                float across = Vector2.Dot(offset, normal) / (pixelRadius * minorScale);
                float distance01 = Mathf.Sqrt(along * along + across * across);

                if (distance01 > 1f)
                {
                    continue;
                }

                float falloff = Mathf.SmoothStep(0f, 1f, 1f - distance01);
                falloff *= falloff;
                if (behavior.roughness > 0f && distance01 > 0.42f)
                {
                    float noise = Mathf.PerlinNoise((x + MarkCount * 17) * 0.073f, (y - MarkCount * 11) * 0.073f);
                    float grain = behavior.name == "Wood" ? Mathf.PerlinNoise(y * 0.025f, MarkCount * 0.13f) : 0.5f;
                    float irregularity = Mathf.Lerp(noise, grain, behavior.name == "Wood" ? 0.35f : 0f);
                    float edgeWeight = Mathf.InverseLerp(0.42f, 1f, distance01);
                    falloff *= Mathf.Lerp(1f, Mathf.Lerp(0.68f, 1.18f, irregularity), behavior.roughness * edgeWeight);
                }

                int index = y * textureWidth + x;
                float oldWetness = wetness[index];
                float addedWetness = impactAmount * falloff;
                float newWetness = Mathf.Min(maxWetness, oldWetness + addedWetness);
                float wetnessDelta = newWetness - oldWetness;
                if (wetnessDelta <= 0f)
                {
                    continue;
                }

                wetness[index] = newWetness;
                totalWetness += wetnessDelta;

                float blendWeight = Mathf.Clamp01(source.a * falloff * (0.35f + wetnessDelta));
                Color existing = paintPixels[index];
                Color mixedPaint = Color.Lerp(existing, source, blendWeight);
                float saturation = Mathf.Clamp01(newWetness / maxWetness);
                Color blended = Color.Lerp(existing, mixedPaint, Mathf.Clamp01(0.45f + saturation * 0.55f));
                blended = Color.Lerp(blended, blended * 0.72f, saturation * 0.28f);
                blended.a = 1f;

                paintPixels[index] = blended;
                paintTexture.SetPixel(x, y, blended);

                bool isPainted = newWetness >= paintedAreaThreshold;
                if (isPainted && !paintedPixels[index])
                {
                    paintedPixels[index] = true;
                    paintedPixelCount++;
                }
            }
        }
    }

    private bool TryFindStrokeAnchor(int streamId, Vector2 uv, Vector2 impactDirectionUv, float impactSpeed, SurfaceBehavior behavior, out Vector2 anchorUv)
    {
        anchorUv = uv;
        float now = Time.unscaledTime;
        float surfaceConnect = Mathf.Lerp(0.62f, 1.35f, behavior.wetnessRetention) * Mathf.Lerp(1.18f, 0.58f, behavior.absorption);
        float maxDistance = connectDistanceThreshold * surfaceConnect;
        int streamIndex = GetStreamIndex(streamId);
        StrokeStreamState state = streamStates[streamIndex];
        if (!state.valid || state.streamId != streamId)
        {
            return false;
        }

        float timeGap = now - state.time;
        float distance = Vector2.Distance(state.uv, uv);
        if (timeGap > maxStrokeTimeGap || distance > maxDistance || impactSpeed > maxConnectImpactSpeed)
        {
            rejectedConnectionCount++;
            return false;
        }

        Vector2 segmentDirection = uv - state.uv;
        if (segmentDirection.sqrMagnitude < 0.000001f)
        {
            anchorUv = state.uv;
            return true;
        }

        segmentDirection.Normalize();
        Vector2 impactDirection = impactDirectionUv.sqrMagnitude > 0.000001f ? impactDirectionUv.normalized : segmentDirection;
        float segmentDot = Vector2.Dot(segmentDirection, impactDirection);
        float streamDot = state.hasDirection ? Vector2.Dot(state.direction, impactDirection) : 1f;
        if (segmentDot < minDirectionDot || streamDot < minDirectionDot)
        {
            rejectedConnectionCount++;
            return false;
        }

        anchorUv = state.uv;
        return true;
    }

    private Vector2 GetFallbackDirection(Vector2 uv, Vector2 impactDirectionUv)
    {
        if (impactDirectionUv.sqrMagnitude > 0.000001f)
        {
            return impactDirectionUv;
        }

        if (hasLastValidHit)
        {
            Vector2 delta = uv - lastValidHitUv;
            if (delta.sqrMagnitude > 0.000001f)
            {
                return delta;
            }
        }

        return Vector2.right;
    }

    private void UpdateStrokeStream(int streamId, Vector2 uv, Vector2 direction, float radius)
    {
        int streamIndex = GetStreamIndex(streamId);
        streamStates[streamIndex] = new StrokeStreamState(streamId, uv, Time.unscaledTime, direction, direction.sqrMagnitude > 0.000001f, radius);
        lastValidHitUv = uv;
        lastValidHitTime = Time.unscaledTime;
        hasLastValidHit = true;
    }

    private int GetStreamIndex(int streamId)
    {
        return Mathf.Abs(streamId) % streamStates.Length;
    }

    private int CountActiveStreams()
    {
        float now = Time.unscaledTime;
        int count = 0;
        for (int i = 0; i < streamStates.Length; i++)
        {
            if (streamStates[i].valid && now - streamStates[i].time <= maxStrokeTimeGap)
            {
                count++;
            }
        }

        return count;
    }

    private void ApplySlopeFlow(int minX, int maxX, int minY, int maxY, SurfaceBehavior behavior, float viscosity, float gravityMagnitude)
    {
        if (orientation != "Tilted" || tiltAngle <= 0.1f || behavior.flowDownSlope <= 0.001f || wetness == null || paintPixels == null)
        {
            return;
        }

        Vector3 normal = transform.up;
        Vector3 downhillWorld = Vector3.ProjectOnPlane(Vector3.down, normal);
        if (downhillWorld.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 downhillLocal = transform.InverseTransformDirection(downhillWorld.normalized);
        Vector2 downhillPixels = new Vector2(
            downhillLocal.x / Mathf.Max(0.001f, localHalfExtents.x * 2f) * textureWidth,
            downhillLocal.z / Mathf.Max(0.001f, localHalfExtents.y * 2f) * textureHeight
        );

        if (downhillPixels.sqrMagnitude < 0.0001f)
        {
            return;
        }

        downhillPixels.Normalize();
        float viscosityFactor = 1f / (1f + viscosity * 1.35f);
        float gravityFactor = Mathf.Clamp(gravityMagnitude / 9.81f, 0f, 2.5f);
        float flowStrength = Mathf.Sin(tiltAngle * Mathf.Deg2Rad) * gravityFactor * behavior.flowDownSlope * viscosityFactor * (1f - behavior.absorption * 0.65f);
        int steps = Mathf.Clamp(Mathf.RoundToInt(flowStrength * 4f), 1, 4);
        float transfer = Mathf.Clamp01(flowStrength * 0.18f);
        if (transfer <= 0.002f)
        {
            return;
        }

        int padding = steps + 2;
        int fromMinX = Mathf.Max(0, minX - padding);
        int fromMaxX = Mathf.Min(textureWidth - 1, maxX + padding);
        int fromMinY = Mathf.Max(0, minY - padding);
        int fromMaxY = Mathf.Min(textureHeight - 1, maxY + padding);

        for (int y = fromMinY; y <= fromMaxY; y++)
        {
            for (int x = fromMinX; x <= fromMaxX; x++)
            {
                int sourceIndex = y * textureWidth + x;
                float sourceWetness = wetness[sourceIndex];
                if (sourceWetness <= paintedAreaThreshold)
                {
                    continue;
                }

                float wetFactor = Mathf.Clamp01(sourceWetness / maxWetness);
                int targetX = Mathf.Clamp(Mathf.RoundToInt(x + downhillPixels.x * steps * wetFactor), 0, textureWidth - 1);
                int targetY = Mathf.Clamp(Mathf.RoundToInt(y + downhillPixels.y * steps * wetFactor), 0, textureHeight - 1);
                if (targetX == x && targetY == y)
                {
                    continue;
                }

                int targetIndex = targetY * textureWidth + targetX;
                float movedWetness = sourceWetness * transfer * wetFactor;
                float oldTargetWetness = wetness[targetIndex];
                float newTargetWetness = Mathf.Min(maxWetness, oldTargetWetness + movedWetness);
                wetness[targetIndex] = newTargetWetness;
                totalWetness += newTargetWetness - oldTargetWetness;

                wetness[sourceIndex] = Mathf.Max(0f, sourceWetness - movedWetness * 0.45f);
                totalWetness -= sourceWetness - wetness[sourceIndex];
                if (paintedPixels[sourceIndex] && wetness[sourceIndex] < paintedAreaThreshold)
                {
                    paintedPixels[sourceIndex] = false;
                    paintedPixelCount = Mathf.Max(0, paintedPixelCount - 1);
                }

                Color flowed = Color.Lerp(paintPixels[targetIndex], paintPixels[sourceIndex], transfer * 1.8f);
                flowed = Color.Lerp(flowed, flowed * 0.86f, Mathf.Clamp01(newTargetWetness / maxWetness) * 0.18f);
                flowed.a = 1f;
                paintPixels[targetIndex] = flowed;
                paintTexture.SetPixel(targetX, targetY, flowed);

                if (newTargetWetness >= paintedAreaThreshold && !paintedPixels[targetIndex])
                {
                    paintedPixels[targetIndex] = true;
                    paintedPixelCount++;
                }
            }
        }
    }

    private Vector2 LocalToUv(Vector3 localPoint)
    {
        float u = Mathf.InverseLerp(-localHalfExtents.x, localHalfExtents.x, localPoint.x);
        float v = Mathf.InverseLerp(-localHalfExtents.y, localHalfExtents.y, localPoint.z);
        Vector2 uv = new Vector2(u, v);

        if (swapUV)
        {
            uv = new Vector2(uv.y, uv.x);
        }

        if (flipU)
        {
            uv.x = 1f - uv.x;
        }

        if (flipV)
        {
            uv.y = 1f - uv.y;
        }

        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);
        return uv;
    }

    private Vector3 UvToLocal(Vector2 uv)
    {
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);

        if (flipU)
        {
            uv.x = 1f - uv.x;
        }

        if (flipV)
        {
            uv.y = 1f - uv.y;
        }

        if (swapUV)
        {
            uv = new Vector2(uv.y, uv.x);
        }

        return new Vector3(
            Mathf.Lerp(-localHalfExtents.x, localHalfExtents.x, uv.x),
            0f,
            Mathf.Lerp(-localHalfExtents.y, localHalfExtents.y, uv.y)
        );
    }

    private Vector2 LocalDeltaToUvDelta(Vector3 localDelta)
    {
        Vector2 delta = new Vector2(
            localDelta.x / Mathf.Max(0.001f, localHalfExtents.x * 2f),
            localDelta.z / Mathf.Max(0.001f, localHalfExtents.y * 2f)
        );

        if (swapUV)
        {
            delta = new Vector2(delta.y, delta.x);
        }

        if (flipU)
        {
            delta.x = -delta.x;
        }

        if (flipV)
        {
            delta.y = -delta.y;
        }

        return delta;
    }

    private Vector3 UvToWorld(Vector2 uv)
    {
        return transform.TransformPoint(UvToLocal(uv));
    }

    private void RecordMappingDebug(Vector3 localHit, Vector2 uv)
    {
        lastLocalHit = localHit;
        lastWorldHit = transform.TransformPoint(localHit);
        lastPixelHit = new Vector2(
            Mathf.RoundToInt(uv.x * (textureWidth - 1)),
            Mathf.RoundToInt(uv.y * (textureHeight - 1))
        );
        lastUvToWorldCheck = UvToWorld(uv);
        mappingErrorDistance = Vector3.Distance(lastWorldHit, lastUvToWorldCheck);
        debugMarkerTimer = mappingDebugMarkerSeconds;
    }

    private void ApplyIfDirty()
    {
        if (!textureDirty || paintTexture == null)
        {
            return;
        }

        paintTexture.Apply();
        textureDirty = false;
    }

    private void CreateTextureIfNeeded()
    {
        if (paintTexture != null && paintTexture.width == textureWidth && paintTexture.height == textureHeight)
        {
            EnsurePaintBuffers();
            return;
        }

        if (paintTexture != null)
        {
            DestroyTexture(paintTexture);
        }

        paintTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        paintTexture.wrapMode = TextureWrapMode.Clamp;
        paintTexture.filterMode = FilterMode.Bilinear;
        paintedPixels = new bool[textureWidth * textureHeight];
        wetness = new float[textureWidth * textureHeight];
        paintPixels = new Color[textureWidth * textureHeight];
        totalWetness = 0f;

        if (materialInstance != null)
        {
            materialInstance.mainTexture = paintTexture;
            materialInstance.mainTextureScale = Vector2.one;
            materialInstance.mainTextureOffset = Vector2.zero;
        }
    }

    private void EnsurePaintBuffers()
    {
        int pixelCount = textureWidth * textureHeight;
        if (paintedPixels == null || paintedPixels.Length != pixelCount)
        {
            paintedPixels = new bool[pixelCount];
            paintedPixelCount = 0;
        }

        if (wetness == null || wetness.Length != pixelCount)
        {
            wetness = new float[pixelCount];
            totalWetness = 0f;
        }

        if (paintPixels == null || paintPixels.Length != pixelCount)
        {
            paintPixels = new Color[pixelCount];
            for (int i = 0; i < paintPixels.Length; i++)
            {
                paintPixels[i] = clearColor;
            }
        }
    }

    private void ApplyDrying()
    {
        if (dryingRate <= 0f || wetness == null || paintPixels == null)
        {
            return;
        }

        dryingTimer += Time.unscaledDeltaTime;
        if (dryingTimer < 0.25f)
        {
            return;
        }

        float dryAmount = dryingRate * dryingTimer;
        dryingTimer = 0f;

        for (int i = 0; i < wetness.Length; i++)
        {
            if (wetness[i] <= 0f)
            {
                continue;
            }

            float oldWetness = wetness[i];
            float newWetness = Mathf.Max(0f, oldWetness - dryAmount);
            wetness[i] = newWetness;
            totalWetness -= oldWetness - newWetness;

            if (paintedPixels[i] && newWetness < paintedAreaThreshold)
            {
                paintedPixels[i] = false;
                paintedPixelCount = Mathf.Max(0, paintedPixelCount - 1);
            }
        }
    }

    private void ApplySurfaceMaterial()
    {
        if (materialInstance == null)
        {
            return;
        }

        SurfaceStyle style = GetSurfaceStyle(surfaceType);
        SetMaterialColor("_BaseColor", style.color);
        SetMaterialColor("_Color", style.color);
        SetMaterialFloat("_Metallic", style.metallic);
        SetMaterialFloat("_Smoothness", style.smoothness);
        SetMaterialFloat("_Glossiness", style.smoothness);

        if (paintTexture != null)
        {
            materialInstance.mainTexture = paintTexture;
            materialInstance.mainTextureScale = Vector2.one;
            materialInstance.mainTextureOffset = Vector2.zero;
        }
    }

    private void SetMaterialColor(string propertyName, Color color)
    {
        if (materialInstance.HasProperty(propertyName))
        {
            materialInstance.SetColor(propertyName, color);
        }
    }

    private void SetMaterialFloat(string propertyName, float value)
    {
        if (materialInstance.HasProperty(propertyName))
        {
            materialInstance.SetFloat(propertyName, value);
        }
    }

    private static string NormalizeSurfaceType(string value)
    {
        if (string.Equals(value, "Wood", System.StringComparison.OrdinalIgnoreCase)) return "Wood";
        if (string.Equals(value, "Metal", System.StringComparison.OrdinalIgnoreCase)) return "Metal";
        if (string.Equals(value, "Paper", System.StringComparison.OrdinalIgnoreCase)) return "Paper";
        return "Canvas";
    }

    private static SurfaceStyle GetSurfaceStyle(string value)
    {
        switch (NormalizeSurfaceType(value))
        {
            case "Wood":
                return new SurfaceStyle(new Color(0.55f, 0.32f, 0.16f, 1f), 0f, 0.34f);
            case "Metal":
                return new SurfaceStyle(new Color(0.62f, 0.65f, 0.68f, 1f), 1f, 0.78f);
            case "Paper":
                return new SurfaceStyle(new Color(0.93f, 0.90f, 0.82f, 1f), 0f, 0.12f);
            default:
                return new SurfaceStyle(new Color(0.78f, 0.82f, 0.76f, 1f), 0f, 0.22f);
        }
    }

    public static SurfaceBehavior GetSurfaceBehavior(string value)
    {
        switch (NormalizeSurfaceType(value))
        {
            case "Wood":
                return new SurfaceBehavior("Wood", 0.45f, 1.05f, 0.52f, 0.08f, 0.62f, 0.92f, 0.48f, 0.48f);
            case "Metal":
                return new SurfaceBehavior("Metal", 0.08f, 1.55f, 0.18f, 0.18f, 0.95f, 0.86f, 0.12f, 0.95f);
            case "Paper":
                return new SurfaceBehavior("Paper", 0.78f, 0.72f, 0.88f, 0.015f, 0.42f, 1.12f, 0.38f, 0.12f);
            default:
                return new SurfaceBehavior("Canvas", 0.48f, 1.0f, 0.68f, 0.035f, 0.58f, 0.94f, 0.55f, 0.35f);
        }
    }

    public readonly struct SurfaceBehavior
    {
        public readonly string name;
        public readonly float absorption;
        public readonly float spreadMultiplier;
        public readonly float friction;
        public readonly float bounce;
        public readonly float wetnessRetention;
        public readonly float opacityMultiplier;
        public readonly float roughness;
        public readonly float flowDownSlope;

        public SurfaceBehavior(string name, float absorption, float spreadMultiplier, float friction, float bounce, float wetnessRetention, float opacityMultiplier, float roughness, float flowDownSlope)
        {
            this.name = name;
            this.absorption = absorption;
            this.spreadMultiplier = spreadMultiplier;
            this.friction = friction;
            this.bounce = bounce;
            this.wetnessRetention = wetnessRetention;
            this.opacityMultiplier = opacityMultiplier;
            this.roughness = roughness;
            this.flowDownSlope = flowDownSlope;
        }
    }

    private readonly struct SurfaceStyle
    {
        public readonly Color color;
        public readonly float metallic;
        public readonly float smoothness;

        public SurfaceStyle(Color color, float metallic, float smoothness)
        {
            this.color = color;
            this.metallic = metallic;
            this.smoothness = smoothness;
        }
    }

    private readonly struct StrokeStreamState
    {
        public readonly bool valid;
        public readonly int streamId;
        public readonly Vector2 uv;
        public readonly float time;
        public readonly Vector2 direction;
        public readonly bool hasDirection;
        public readonly float radius;

        public StrokeStreamState(int streamId, Vector2 uv, float time, Vector2 direction, bool hasDirection, float radius)
        {
            valid = true;
            this.streamId = streamId;
            this.uv = uv;
            this.time = time;
            this.direction = hasDirection ? direction.normalized : Vector2.zero;
            this.hasDirection = hasDirection;
            this.radius = radius;
        }
    }

    private void OnDestroy()
    {
        DestroyTexture(paintTexture);

        if (materialInstance != null)
        {
            if (Application.isPlaying)
            {
                Destroy(materialInstance);
            }
            else
            {
                DestroyImmediate(materialInstance);
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!showMappingDebugMarkers || debugMarkerTimer <= 0f)
        {
            return;
        }

        debugMarkerTimer -= Time.deltaTime;
        float markerSize = Mathf.Max(0.035f, Mathf.Min(currentWidth, currentHeight) * 0.012f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(lastWorldHit, markerSize);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(lastUvToWorldCheck, markerSize * 1.25f);
        Gizmos.DrawLine(lastWorldHit, lastUvToWorldCheck);
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(texture);
        }
    }
}
