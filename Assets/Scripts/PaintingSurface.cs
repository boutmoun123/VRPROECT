using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Renderer))]
public class PaintingSurface : MonoBehaviour
{
    public enum TrailRenderMode
    {
        Dots,
        Trails,
        Ribbon
    }

    public enum BoardMappingPlane
    {
        LocalXY,
        LocalXZ,
        LocalYZ
    }

    public enum PaintRenderMode
    {
        WorldDecals,
        TextureUvLegacy
    }

    public enum DebugMarkerHistoryMode
    {
        LastHitOnly,
        Last10Hits
    }

    [Header("Texture")]
    public int textureWidth = 1024;
    public int textureHeight = 1024;
    public Color clearColor = Color.white;

    [Header("Painting")]
    public Color paintColor = new Color(0.1f, 0.25f, 1f, 1f);
    [Range(0.001f, 0.25f)] public float brushRadius = 0.035f;
    [Range(0.01f, 1f)] public float opacity = 0.45f;
    [Range(0f, 1f)] public float spreading = 0.25f;
    [Range(0.001f, 0.2f)] public float splatRadius = 0.025f;
    [Range(0.01f, 1f)] public float splatOpacity = 0.55f;
    [Range(0.01f, 2f)] public float accumulationStrength = 0.55f;
    [Range(0.1f, 4f)] public float maxWetness = 1.6f;
    [Range(0f, 2f)] public float spreadStrength = 0.45f;
    [Range(0f, 0.25f)] public float dryingRate = 0f;
    [Range(0.001f, 0.5f)] public float paintedAreaThreshold = 0.035f;

    [Header("Trails")]
    public TrailRenderMode trailMode = TrailRenderMode.Ribbon;
    [Range(0.001f, 0.2f)] public float connectDistanceThreshold = 0.04f;
    [Range(0.001f, 0.08f)] public float strokeStepSpacing = 0.008f;
    [Range(0.3f, 3f)] public float strokeRadius = 1f;
    [Range(0.01f, 1f)] public float strokeOpacity = 0.45f;
    [Range(0f, 1f)] public float strokeSmoothing = 0.72f;
    [Range(0.01f, 0.5f)] public float maxStrokeTimeGap = 0.1f;
    [Range(-1f, 1f)] public float minDirectionDot = 0.35f;
    [Range(0f, 30f)] public float maxConnectImpactSpeed = 18f;

    [Header("UV Mapping")]
    public PaintRenderMode paintRenderMode = PaintRenderMode.WorldDecals;
    public BoardMappingPlane mappingPlane = BoardMappingPlane.LocalXZ;
    public bool invertRightAxis = false;
    public bool invertUpAxis = false;
    public bool swapAxes = false;
    public bool showMappingDebugMarkers = true;
    public DebugMarkerHistoryMode debugMarkerHistoryMode = DebugMarkerHistoryMode.LastHitOnly;
    [Range(0.02f, 0.2f)] public float mappingDebugMarkerOffset = 0.08f;
    [Range(0.05f, 0.25f)] public float mappingDebugMarkerSize = 0.12f;
    [Range(0.001f, 0.08f)] public float paintSurfaceOffset = 0.02f;
    public bool invertPaintNormal;
    public bool invertBoardNormalForCollision;
    public bool paintDecalsAlwaysVisibleDebug;
    public bool worldPaintFallbackGeometry;

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
    public float EstimatedPaintedArea01 => paintRenderMode == PaintRenderMode.WorldDecals && worldPaintRenderer != null
        ? worldPaintRenderer.EstimatedPaintedArea01
        : paintedPixelCount / (float)(textureWidth * textureHeight);
    public Texture2D PaintTexture => paintTexture;
    public Vector3 BoardRotationEuler => transform.localEulerAngles;
    public Vector3 BoardScale => transform.localScale;
    public float AverageWetness => wetness != null && wetness.Length > 0 ? totalWetness / wetness.Length : 0f;
    public float LastAppliedSplatRadius => lastAppliedSplatRadius;
    public float LastViscosityEffect => lastViscosityEffect;
    public float LastImpactSpeed => lastImpactSpeed;
    public int ConnectedStrokeCount => paintRenderMode == PaintRenderMode.WorldDecals && worldPaintRenderer != null ? worldPaintRenderer.ConnectedStrokeCount : connectedStrokeCount;
    public int RejectedConnectionCount => paintRenderMode == PaintRenderMode.WorldDecals && worldPaintRenderer != null ? worldPaintRenderer.RejectedConnectionCount : rejectedConnectionCount;
    public float AverageStrokeLength => connectedStrokeCount > 0 ? totalConnectedStrokeLength / connectedStrokeCount : 0f;
    public int ActiveStreamCount => CountActiveStreams();
    public TrailRenderMode ActiveTrailMode => trailMode;
    public string LastDepositModeUsed => lastDepositModeUsed.ToString();
    public int TotalDepositCount => totalDepositCount;
    public int TextureUpdatedCount => textureUpdatedCount;
    public Vector2 LastHitUv => lastValidHitUv;
    public Vector2 CurrentHitUv => currentHitUv;
    public Vector3 LastWorldHit => lastWorldHit;
    public Vector3 LastLocalHit => lastLocalHit;
    public Vector2 LastPixelHit => lastPixelHit;
    public Vector3 LastUvToWorldCheck => lastUvToWorldCheck;
    public float MappingErrorDistance => mappingErrorDistance;
    public float LastEffectiveDepositRadius => lastEffectiveDepositRadius;
    public Vector3 LastParticlePreviousPosition => lastParticlePreviousPosition;
    public Vector3 LastParticleCurrentPosition => lastParticleCurrentPosition;
    public float LastCollisionDPrev => lastCollisionDPrev;
    public float LastCollisionDCurr => lastCollisionDCurr;
    public float LastCollisionT => lastCollisionT;
    public bool LastHitInsideBoard => lastHitInsideBoard;
    public Vector3 LastCollisionWorldPosition => lastCollisionWorldPosition;
    public Vector3 BoardCenter => GetBoardCenter();
    public Vector3 BoardRightAxis => GetBoardRightAxis();
    public Vector3 BoardUpAxis => GetBoardUpAxis();
    public Vector3 BoardNormal => GetBoardNormal();
    public bool HitDebugMarkerActive => IsMarkerActive(hitPointMarker);
    public bool UvDebugMarkerActive => IsMarkerActive(uvBackToWorldMarker);
    public bool ParticleDebugMarkerActive => IsMarkerActive(particleCurrentMarker);
    public float DebugMarkerOffset => mappingDebugMarkerOffset;
    public int DebugMarkerCount => CountActiveDebugMarkers();
    public float LastCollisionTime => lastDebugCollisionTime;
    public string MappingDebugSummary => BuildMappingDebugSummary();
    public Color BoardBaseColor => GetSurfaceStyle(surfaceType).color;
    public SurfaceBehavior CurrentSurfaceBehavior => GetSurfaceBehavior(surfaceType);
    public int WorldPaintDrawCalls => worldPaintDrawCalls;
    public int WorldPaintObjectCount => worldPaintRenderer != null ? worldPaintRenderer.PaintObjectCount : 0;
    public Vector3 LastPaintObjectPosition => worldPaintRenderer != null ? worldPaintRenderer.LastPaintObjectPosition : Vector3.zero;
    public float LastPaintRadius => worldPaintRenderer != null ? worldPaintRenderer.LastPaintRadius : 0f;
    public float LastPaintAlpha => worldPaintRenderer != null ? worldPaintRenderer.LastPaintAlpha : 0f;
    public Color LastPaintMaterialColor => worldPaintRenderer != null ? worldPaintRenderer.LastPaintMaterialColor : Color.clear;
    public float ActivePaintSurfaceOffset => worldPaintRenderer != null ? worldPaintRenderer.paintSurfaceOffset : paintSurfaceOffset;
    public Vector3 LastVisiblePaintNormal => worldPaintRenderer != null ? worldPaintRenderer.LastVisibleNormal : BoardNormal;
    public string WorldPaintRendererDiagnostic => worldPaintRenderer != null ? worldPaintRenderer.LastDiagnostic : "WorldPaintRenderer missing.";

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
    private float lastEffectiveDepositRadius;
    private float lastViscosityEffect = 1f;
    private float lastImpactSpeed;
    private Vector2 lastValidHitUv;
    private Vector2 currentHitUv;
    private float lastValidHitTime = -999f;
    private bool hasLastValidHit;
    private int connectedStrokeCount;
    private int rejectedConnectionCount;
    private int totalDepositCount;
    private int textureUpdatedCount;
    private float totalConnectedStrokeLength;
    private TrailRenderMode lastDepositModeUsed = TrailRenderMode.Ribbon;
    private Vector3 lastWorldHit;
    private Vector3 lastLocalHit;
    private Vector2 lastPixelHit;
    private Vector3 lastUvToWorldCheck;
    private Vector3 lastParticlePreviousPosition;
    private Vector3 lastParticleCurrentPosition;
    private float lastCollisionDPrev;
    private float lastCollisionDCurr;
    private float lastCollisionT = -1f;
    private bool lastHitInsideBoard;
    private Vector3 lastCollisionWorldPosition;
    private float mappingErrorDistance;
    private int worldPaintDrawCalls;
    private GameObject hitPointMarker;
    private GameObject uvBackToWorldMarker;
    private GameObject particleCurrentMarker;
    private float lastDebugCollisionTime = -999f;
    private int nextDebugMarkerHistoryIndex;
    private readonly List<DebugMarkerSet> debugMarkerHistorySets = new List<DebugMarkerSet>();
    private WorldPaintRenderer worldPaintRenderer;
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
        opacity = Mathf.Clamp(opacity, 0.01f, 1f);
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
        strokeRadius = Mathf.Clamp(strokeRadius <= 0.2f ? 1f : strokeRadius, 0.3f, 3f);
        strokeOpacity = Mathf.Clamp01(strokeOpacity);
        strokeSmoothing = Mathf.Clamp01(strokeSmoothing);
        maxStrokeTimeGap = Mathf.Clamp(maxStrokeTimeGap, 0.01f, 0.5f);
        minDirectionDot = Mathf.Clamp(minDirectionDot, -1f, 1f);
        maxConnectImpactSpeed = Mathf.Max(0f, maxConnectImpactSpeed);
        mappingDebugMarkerOffset = Mathf.Clamp(mappingDebugMarkerOffset, 0.02f, 0.2f);
        mappingDebugMarkerSize = Mathf.Clamp(mappingDebugMarkerSize, 0.05f, 0.25f);
        paintSurfaceOffset = Mathf.Clamp(paintSurfaceOffset, 0.001f, 0.08f);
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
        strokeRadius = Mathf.Clamp(strokeRadius <= 0.2f ? 1f : strokeRadius, 0.3f, 3f);
        opacity = Mathf.Clamp(opacity, 0.01f, 1f);
        strokeOpacity = Mathf.Clamp(strokeOpacity, 0.01f, 1f);
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

        EnsureWorldPaintRenderer();

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
        ResetStrokeStreams();
    }

    public void ClearPainting()
    {
        CreateTextureIfNeeded();
        EnsureWorldPaintRenderer();

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
        totalDepositCount = 0;
        textureUpdatedCount = 0;
        worldPaintDrawCalls = 0;
        lastDepositModeUsed = trailMode;
        totalConnectedStrokeLength = 0f;
        currentHitUv = Vector2.zero;
        ResetStrokeStreams();
        ResetMappingDebug();
        textureDirty = false;
        if (worldPaintRenderer != null)
        {
            worldPaintRenderer.Clear();
        }
    }

    public void ClearWorldPaint()
    {
        EnsureWorldPaintRenderer();
        if (worldPaintRenderer != null)
        {
            worldPaintRenderer.Clear();
        }
        ResetStrokeStreams();
        MarkCount = 0;
        totalDepositCount = 0;
        connectedStrokeCount = 0;
        rejectedConnectionCount = 0;
        worldPaintDrawCalls = 0;
        totalConnectedStrokeLength = 0f;
    }

    public void SetTrailMode(TrailRenderMode mode)
    {
        trailMode = mode;
        ResetStrokeStreams();
    }

    public void SetPaintRenderMode(PaintRenderMode mode, bool clearCanvas = false)
    {
        if (paintRenderMode == mode)
        {
            return;
        }

        paintRenderMode = mode;
        EnsureWorldPaintRenderer();
        ApplySurfaceMaterial();
        ResetAfterMappingChange(clearCanvas);
    }

    public void SetMappingPlane(BoardMappingPlane plane, bool clearCanvas = false)
    {
        if (mappingPlane == plane)
        {
            return;
        }

        mappingPlane = plane;
        ResetAfterMappingChange(clearCanvas);
    }

    public void SetInvertRightAxis(bool value, bool clearCanvas = false)
    {
        if (invertRightAxis == value)
        {
            return;
        }

        invertRightAxis = value;
        ResetAfterMappingChange(clearCanvas);
    }

    public void SetInvertUpAxis(bool value, bool clearCanvas = false)
    {
        if (invertUpAxis == value)
        {
            return;
        }

        invertUpAxis = value;
        ResetAfterMappingChange(clearCanvas);
    }

    public void SetSwapAxes(bool value, bool clearCanvas = false)
    {
        if (swapAxes == value)
        {
            return;
        }

        swapAxes = value;
        ResetAfterMappingChange(clearCanvas);
    }

    public void SetShowMappingDebugMarkers(bool value)
    {
        showMappingDebugMarkers = value;
        if (!showMappingDebugMarkers)
        {
            ResetMappingDebug();
        }
    }

    public void SetInvertPaintNormal(bool value)
    {
        invertPaintNormal = value;
        SyncWorldPaintRendererSettings();
    }

    public void SetInvertBoardNormalForCollision(bool value)
    {
        invertBoardNormalForCollision = value;
        ResetMappingDebug();
    }

    public void SetPaintDecalsAlwaysVisibleDebug(bool value)
    {
        paintDecalsAlwaysVisibleDebug = value;
        SyncWorldPaintRendererSettings();
    }

    public void SetWorldPaintFallbackGeometry(bool value)
    {
        worldPaintFallbackGeometry = value;
        SyncWorldPaintRendererSettings();
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

        if (!TryGetSegmentHit(
            previousWorldPosition,
            currentWorldPosition,
            out Vector3 worldHit,
            out _,
            out _,
            out _,
            out bool insideBoard
        ) || !insideBoard)
        {
            return false;
        }

        return DepositSegmentHit(
            previousWorldPosition,
            currentWorldPosition,
            worldHit,
            color,
            radius,
            alpha,
            velocityMagnitude,
            viscosity,
            holeDiameter,
            exitSpeed,
            gravityMagnitude,
            streamId,
            emitterVelocityWorld
        );
    }

    public bool DepositSegmentHit(
        Vector3 previousWorldPosition,
        Vector3 currentWorldPosition,
        Vector3 worldHit,
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
        if (!WorldToBoardUv(worldHit, out _, out Vector2 boardPoint))
        {
            return false;
        }

        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        Vector2 impactDirectionUv = WorldDeltaToUvDelta(currentWorldPosition - previousWorldPosition);

        if (impactDirectionUv.sqrMagnitude < 0.000001f && emitterVelocityWorld.sqrMagnitude > 0.0001f)
        {
            impactDirectionUv = WorldDeltaToUvDelta(emitterVelocityWorld);
        }

        float safeViscosity = Mathf.Max(0.001f, viscosity);
        float velocitySpread = Mathf.Clamp01(velocityMagnitude / Mathf.Max(0.001f, exitSpeed + 4f));
        float effectiveRadius = GetEffectivePaintDepositRadius(holeDiameter, safeViscosity, velocityMagnitude, behavior, strokeRadius);
        float radius01 = WorldRadiusToUvRadius(effectiveRadius);
        float holeSpread = Mathf.Clamp(holeDiameter / 0.013f, 0.35f, 3.8f);
        float viscosityThickness = Mathf.Lerp(1.22f, 0.72f, Mathf.Clamp01(safeViscosity));
        float absorptionDrying = Mathf.Lerp(1.05f, 0.48f, behavior.absorption);
        float impactOpacity = Mathf.Clamp01(alpha * splatOpacity * behavior.opacityMultiplier * Mathf.Lerp(0.82f, 0.42f, Mathf.Clamp01(safeViscosity)));
        float impactAmount = Mathf.Clamp(
            accumulationStrength * 0.55f *
            behavior.wetnessRetention *
            absorptionDrying *
            Mathf.Lerp(0.68f, 1.05f, velocitySpread) *
            Mathf.Lerp(0.55f, 1.2f, Mathf.InverseLerp(0.35f, 3.8f, holeSpread)) *
            viscosityThickness,
            0.01f,
            maxWetness * 0.65f
        );

        lastViscosityEffect = Mathf.Clamp01(safeViscosity);
        lastImpactSpeed = velocityMagnitude;
        lastEffectiveDepositRadius = effectiveRadius;
        lastAppliedSplatRadius = radius01;
        return DepositWorldHit(worldHit, color, lastAppliedSplatRadius, impactOpacity, impactAmount, behavior, safeViscosity, gravityMagnitude, velocitySpread, streamId, impactDirectionUv, currentWorldPosition);
    }

    public float GetEffectivePaintDepositRadius(float holeDiameter, float viscosity, float impactSpeed, SurfaceBehavior currentSurface, float strokeRadiusMultiplier)
    {
        float baseRadius = Mathf.Max(0.001f, holeDiameter) * 0.35f;
        float viscosityFactor = Mathf.Lerp(1.4f, 0.65f, Mathf.Clamp01(viscosity));
        float impactFactor = Mathf.Lerp(0.8f, 1.4f, Mathf.InverseLerp(0f, 6f, impactSpeed));
        float surfaceFactor = Mathf.Max(0.2f, currentSurface.spreadMultiplier);
        float multiplier = Mathf.Clamp(strokeRadiusMultiplier, 0.3f, 3f);
        float effectiveRadius = baseRadius * viscosityFactor * impactFactor * surfaceFactor * multiplier;
        return Mathf.Clamp(effectiveRadius, 0.003f, 0.035f);
    }

    private float WorldRadiusToUvRadius(float worldRadius)
    {
        float minBoardSize = Mathf.Max(0.001f, Mathf.Min(currentWidth, currentHeight));
        return Mathf.Clamp(worldRadius / minBoardSize, 0.0001f, 0.05f);
    }

    public bool TryGetSegmentHit(
        Vector3 previousWorldPosition,
        Vector3 currentWorldPosition,
        out Vector3 worldHit,
        out Vector3 localHit,
        out Vector2 uvHit,
        out Vector2 pixelHit,
        out bool insideBoard
    )
    {
        worldHit = Vector3.zero;
        localHit = Vector3.zero;
        uvHit = Vector2.zero;
        pixelHit = Vector2.zero;
        insideBoard = false;

        Vector3 boardCenter = BoardCenter;
        Vector3 boardNormal = GetCollisionBoardNormal();
        Vector3 boardRightAxis = BoardRightAxis;
        Vector3 boardUpAxis = BoardUpAxis;
        float distanceA = Vector3.Dot(previousWorldPosition - boardCenter, boardNormal);
        float distanceB = Vector3.Dot(currentWorldPosition - boardCenter, boardNormal);
        const float planeEpsilon = 0.0001f;
        lastCollisionDPrev = distanceA;
        lastCollisionDCurr = distanceB;
        lastCollisionT = -1f;
        lastHitInsideBoard = false;
        lastCollisionWorldPosition = Vector3.zero;

        float denominator = distanceA - distanceB;
        if (Mathf.Abs(denominator) <= planeEpsilon)
        {
            if (Mathf.Abs(distanceB) > planeEpsilon)
            {
                return false;
            }

            denominator = 1f;
            distanceA = 0f;
        }

        float t = distanceA / denominator;
        if (Mathf.Abs(lastCollisionDCurr) <= planeEpsilon)
        {
            t = 1f;
        }
        else if (Mathf.Abs(lastCollisionDPrev) <= planeEpsilon)
        {
            t = 0f;
        }

        if (t < 0f || t > 1f)
        {
            return false;
        }

        bool crossesPlane =
            lastCollisionDPrev * lastCollisionDCurr <= 0f ||
            Mathf.Abs(lastCollisionDPrev) <= planeEpsilon ||
            Mathf.Abs(lastCollisionDCurr) <= planeEpsilon;
        if (!crossesPlane)
        {
            return false;
        }

        worldHit = Vector3.Lerp(previousWorldPosition, currentWorldPosition, Mathf.Clamp01(t));
        localHit = transform.InverseTransformPoint(worldHit);
        WorldToBoardUv(worldHit, out uvHit, out Vector2 boardPoint);
        pixelHit = new Vector2(
            Mathf.RoundToInt(uvHit.x * (textureWidth - 1)),
            Mathf.RoundToInt(uvHit.y * (textureHeight - 1))
        );
        lastParticlePreviousPosition = previousWorldPosition;
        lastParticleCurrentPosition = currentWorldPosition;
        Vector3 delta = worldHit - boardCenter;
        float boardX = Vector3.Dot(delta, boardRightAxis);
        float boardY = Vector3.Dot(delta, boardUpAxis);
        Vector2 halfSize = GetBoardWorldHalfExtents();
        insideBoard = Mathf.Abs(boardX) <= halfSize.x && Mathf.Abs(boardY) <= halfSize.y;
        lastCollisionT = t;
        lastHitInsideBoard = insideBoard;
        lastCollisionWorldPosition = worldHit;
        return true;
    }

    public bool TestParticleBoardCollision()
    {
        Vector3 normal = GetCollisionBoardNormal();
        Vector3 start = BoardCenter + normal * 1.5f;
        Vector3 end = BoardCenter - normal * 1.5f;
        bool hit = TryGetSegmentHit(start, end, out Vector3 worldHit, out _, out _, out _, out bool insideBoard);
        if (!hit || !insideBoard)
        {
            return false;
        }

        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        bool deposited = DepositWorldHit(
            worldHit,
            Color.red,
            WorldRadiusToUvRadius(0.02f),
            1f,
            maxWetness,
            behavior,
            0.5f,
            9.81f,
            0.2f,
            90001,
            WorldDeltaToUvDelta(end - start),
            end
        );
        ApplyIfDirty();
        return deposited;
    }

    public bool DepositWorldHit(Vector3 worldHit, Color color, float radius01, float alpha, float impactAmount, SurfaceBehavior behavior, float viscosity, float gravityMagnitude, float velocitySpread, int streamId, Vector2 impactDirectionUv)
    {
        return DepositWorldHit(worldHit, color, radius01, alpha, impactAmount, behavior, viscosity, gravityMagnitude, velocitySpread, streamId, impactDirectionUv, worldHit);
    }

    public bool DepositWorldHit(Vector3 worldHit, Color color, float radius01, float alpha, float impactAmount, SurfaceBehavior behavior, float viscosity, float gravityMagnitude, float velocitySpread, int streamId, Vector2 impactDirectionUv, Vector3 particleCurrentPosition)
    {
        if (!WorldToBoardUv(worldHit, out Vector2 uv, out _))
        {
            return false;
        }

        RecordMappingDebug(worldHit, uv, particleCurrentPosition);
        if (paintRenderMode == PaintRenderMode.WorldDecals)
        {
            EnsureWorldPaintRenderer();
            if (worldPaintRenderer != null)
            {
                float worldRadius = Mathf.Clamp(radius01 * Mathf.Min(currentWidth, currentHeight), 0.003f, 0.035f);
                Color decalColor = color;
                decalColor.a = Mathf.Clamp(alpha <= 0f ? 1f : alpha, 0.65f, 1f);
                worldRadius = Mathf.Clamp(worldRadius, 0.008f, 0.035f);
                worldPaintRenderer.DrawHit(this, worldHit, decalColor, trailMode, worldRadius, streamId);
                worldPaintDrawCalls++;
            }

            MarkCount++;
            totalDepositCount++;
            lastDepositModeUsed = trailMode;
            currentHitUv = uv;
            lastValidHitUv = uv;
            lastValidHitTime = Time.unscaledTime;
            hasLastValidHit = true;
            return true;
        }

        PaintAtUV(uv, color, radius01, alpha, impactAmount, behavior, viscosity, gravityMagnitude, velocitySpread, streamId, impactDirectionUv);
        return true;
    }

    public string SavePng(string directory, string filePrefix)
    {
        if (paintRenderMode == PaintRenderMode.WorldDecals)
        {
            EnsureWorldPaintRenderer();
            if (worldPaintRenderer != null)
            {
                return worldPaintRenderer.SavePng(this, directory, filePrefix, textureWidth, textureHeight, BoardBaseColor);
            }
        }

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
        float radius = WorldRadiusToUvRadius(0.012f);
        Vector3 center = BoardCenter;
        Vector3 right = BoardRightAxis;
        Vector3 up = BoardUpAxis;
        Vector2 halfSize = GetBoardWorldHalfExtents();
        DepositWorldHit(center - right * halfSize.x * 0.8f, Color.red, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10001, WorldDeltaToUvDelta(-right));
        DepositWorldHit(center + right * halfSize.x * 0.8f, Color.green, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10002, WorldDeltaToUvDelta(right));
        DepositWorldHit(center + up * halfSize.y * 0.8f, Color.blue, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10003, WorldDeltaToUvDelta(up));
        DepositWorldHit(center - up * halfSize.y * 0.8f, Color.yellow, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10004, WorldDeltaToUvDelta(-up));
        DepositWorldHit(center, Color.white, radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 10005, Vector2.right);
        ApplyIfDirty();
    }

    public void DrawWorldHitMappingTest()
    {
        ClearPainting();

        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        float radius = WorldRadiusToUvRadius(0.012f);
        Vector3 center = BoardCenter;
        Vector3 right = BoardRightAxis;
        Vector3 up = BoardUpAxis;
        Vector2 halfSize = GetBoardWorldHalfExtents();
        Vector3[] samples =
        {
            center,
            center - right * halfSize.x * 0.7f,
            center + right * halfSize.x * 0.7f,
            center + up * halfSize.y * 0.7f,
            center - up * halfSize.y * 0.7f
        };
        Color[] colors = { Color.white, Color.red, Color.green, Color.blue, Color.yellow };

        for (int i = 0; i < samples.Length; i++)
        {
            DepositWorldHit(samples[i], colors[i], radius, 0.95f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 11000 + i, Vector2.right);
        }

        ApplyIfDirty();
    }

    public void DrawVisibleWorldPaintTest()
    {
        TrailRenderMode previousTrailMode = trailMode;
        paintRenderMode = PaintRenderMode.WorldDecals;
        SetTrailMode(TrailRenderMode.Dots);
        ClearPainting();
        EnsureWorldPaintRenderer();

        float radius = Mathf.Clamp(Mathf.Min(currentWidth, currentHeight) * 0.018f, 0.08f, 0.2f);
        Vector3 center = BoardCenter;
        Vector3 right = BoardRightAxis;
        Vector3 up = BoardUpAxis;
        Vector2 halfSize = GetBoardWorldHalfExtents();

        DrawVisibleWorldPaintDot(center, Color.magenta, radius, 13000);
        DrawVisibleWorldPaintDot(center - right * halfSize.x * 0.65f, Color.red, radius, 13001);
        DrawVisibleWorldPaintDot(center + right * halfSize.x * 0.65f, Color.green, radius, 13002);
        DrawVisibleWorldPaintDot(center + up * halfSize.y * 0.65f, Color.blue, radius, 13003);
        DrawVisibleWorldPaintDot(center - up * halfSize.y * 0.65f, Color.yellow, radius, 13004);

        ApplyIfDirty();
        SetTrailMode(previousTrailMode);
        ApplySurfaceMaterial();
    }

    private void DrawVisibleWorldPaintDot(Vector3 worldHit, Color color, float radius, int streamId)
    {
        if (worldPaintRenderer == null)
        {
            return;
        }

        color.a = 1f;
        worldPaintRenderer.DrawHit(this, worldHit, color, TrailRenderMode.Dots, radius, streamId);
        worldPaintDrawCalls++;
        MarkCount++;
        totalDepositCount++;
        lastDepositModeUsed = TrailRenderMode.Dots;
        if (WorldToBoardUv(worldHit, out Vector2 uv, out _))
        {
            currentHitUv = uv;
            lastValidHitUv = uv;
            lastValidHitTime = Time.unscaledTime;
            hasLastValidHit = true;
            RecordMappingDebug(worldHit, uv, worldHit);
        }
    }

    public bool PaintUnderBucketTest(Vector3 bucketWorldPosition)
    {
        ClearPainting();

        Vector3 normal = BoardNormal;
        float distance = Vector3.Dot(bucketWorldPosition - BoardCenter, normal);
        Vector3 projectedHit = bucketWorldPosition - normal * distance;
        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        float radius = WorldRadiusToUvRadius(0.014f);
        bool deposited = DepositWorldHit(projectedHit, Color.white, radius, 1f, maxWetness, behavior, 0.5f, 9.81f, 0.1f, 12000, Vector2.right, bucketWorldPosition);
        ApplyIfDirty();
        return deposited;
    }

    public void AutoTryNextMappingMode()
    {
        int mode = (invertRightAxis ? 4 : 0) + (invertUpAxis ? 2 : 0) + (swapAxes ? 1 : 0);
        mode = (mode + 1) % 8;
        invertRightAxis = (mode & 4) != 0;
        invertUpAxis = (mode & 2) != 0;
        swapAxes = (mode & 1) != 0;
        ResetAfterMappingChange(false);
        DrawMappingTestPattern();
    }

    public void DrawTestDots(Color color)
    {
        TrailRenderMode previousMode = trailMode;
        SetTrailMode(TrailRenderMode.Dots);
        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        Color[] colors =
        {
            color,
            Color.red,
            Color.blue,
            Color.green,
            Color.yellow,
            new Color(1f, 0.35f, 0.05f, 1f),
            new Color(0.1f, 0.9f, 0.9f, 1f),
            new Color(0.8f, 0.1f, 0.9f, 1f),
            Color.white,
            new Color(1f, 0.2f, 0.75f, 1f)
        };

        for (int i = 0; i < 10; i++)
        {
            float u = 0.12f + (i % 5) * 0.19f;
            float v = i < 5 ? 0.32f : 0.68f;
            Vector2 uv = new Vector2(u, v);
            DepositWorldHit(UvToWorld(uv), colors[i], WorldRadiusToUvRadius(0.01f), strokeOpacity, maxWetness, behavior, 0.5f, 9.81f, 0.2f, 70000 + i, Vector2.right);
        }

        ApplyIfDirty();
        SetTrailMode(previousMode);
    }

    public void DrawTestTrails(Color color)
    {
        TrailRenderMode previousMode = trailMode;
        float previousConnectDistance = connectDistanceThreshold;
        SetTrailMode(TrailRenderMode.Trails);
        connectDistanceThreshold = Mathf.Max(connectDistanceThreshold, 0.32f);
        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        int streamId = 71000;
        Vector2 previous = Vector2.zero;
        for (int i = 0; i < 9; i++)
        {
            float u = 0.12f + i * 0.095f;
            float v = i % 2 == 0 ? 0.32f : 0.58f;
            Vector2 uv = new Vector2(u, v);
            Vector2 direction = i > 0 ? uv - previous : Vector2.right;
            DepositWorldHit(UvToWorld(uv), color, WorldRadiusToUvRadius(0.008f), strokeOpacity, maxWetness, behavior, 0.5f, 9.81f, 0.2f, streamId, direction);
            previous = uv;
        }

        ApplyIfDirty();
        connectDistanceThreshold = previousConnectDistance;
        SetTrailMode(previousMode);
    }

    public void DrawTestRibbon(Color color)
    {
        TrailRenderMode previousMode = trailMode;
        float previousConnectDistance = connectDistanceThreshold;
        SetTrailMode(TrailRenderMode.Ribbon);
        connectDistanceThreshold = Mathf.Max(connectDistanceThreshold, 0.12f);
        SurfaceBehavior behavior = GetSurfaceBehavior(surfaceType);
        int streamId = 72000;
        Vector2 previous = Vector2.zero;
        for (int i = 0; i < 32; i++)
        {
            float t = i / 31f;
            Vector2 uv = new Vector2(0.08f + t * 0.84f, 0.5f + Mathf.Sin(t * Mathf.PI * 2.2f) * 0.18f);
            Vector2 direction = i > 0 ? uv - previous : Vector2.right;
            DepositWorldHit(UvToWorld(uv), color, WorldRadiusToUvRadius(0.011f), strokeOpacity, maxWetness, behavior, 0.45f, 9.81f, 0.2f, streamId, direction);
            previous = uv;
        }

        ApplyIfDirty();
        connectDistanceThreshold = previousConnectDistance;
        SetTrailMode(previousMode);
    }

    public void FlushTextureUpdates()
    {
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
        if (!IsFinite(uv) || paintTexture == null)
        {
            return;
        }

        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);
        currentHitUv = uv;
        int centerX = Mathf.RoundToInt(uv.x * (textureWidth - 1));
        int centerY = Mathf.RoundToInt(uv.y * (textureHeight - 1));
        lastPixelHit = new Vector2(centerX, centerY);
        int minX = textureWidth - 1;
        int maxX = 0;
        int minY = textureHeight - 1;
        int maxY = 0;

        Color source = color;
        source.a = Mathf.Clamp01(alpha);
        Vector2 anchorUv = uv;
        TrailRenderMode mode = trailMode;
        lastDepositModeUsed = mode;
        bool canDrawTrail = mode != TrailRenderMode.Dots;
        bool connected = canDrawTrail && TryFindStrokeAnchor(streamId, uv, impactDirectionUv, lastImpactSpeed, behavior, mode, out anchorUv);
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
                mode == TrailRenderMode.Ribbon
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
        float ribbonBlend = mode == TrailRenderMode.Ribbon ? 0.62f : 1f;
        float splatImpact = impactAmount * Mathf.Lerp(1.25f, 0.75f, velocitySpread) * ribbonBlend;
        float impactRadius = mode == TrailRenderMode.Dots
            ? radius01
            : radius01 * Mathf.Lerp(1.08f, 0.86f, velocitySpread) * Mathf.Lerp(1f, 0.78f, mode == TrailRenderMode.Ribbon ? 1f : 0f);
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
        totalDepositCount++;
        lastValidHitUv = uv;
        lastValidHitTime = Time.unscaledTime;
        hasLastValidHit = true;
        if (mode != TrailRenderMode.Dots)
        {
            UpdateStrokeStream(streamId, uv, GetSmoothedDirection(streamId, impactDirectionUv.sqrMagnitude > 0.000001f ? impactDirectionUv.normalized : directionUv.normalized, mode), radius01);
        }
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
        float radius = Mathf.Max(impactRadius01 * 0.72f, impactRadius01 * 0.85f) *
            Mathf.Lerp(0.82f, 1.28f, viscositySpread) *
            surfaceConnection *
            Mathf.Lerp(1.18f, 0.88f, velocitySpread);
        if (ribbon)
        {
            radius = Mathf.Clamp(radius, impactRadius01 * 0.72f, impactRadius01 * 1.35f);
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

    private bool TryFindStrokeAnchor(int streamId, Vector2 uv, Vector2 impactDirectionUv, float impactSpeed, SurfaceBehavior behavior, TrailRenderMode mode, out Vector2 anchorUv)
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

        if (mode == TrailRenderMode.Trails)
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

    private Vector2 GetSmoothedDirection(int streamId, Vector2 direction, TrailRenderMode mode)
    {
        if (direction.sqrMagnitude <= 0.000001f)
        {
            return Vector2.right;
        }

        direction.Normalize();
        if (mode != TrailRenderMode.Ribbon)
        {
            return direction;
        }

        StrokeStreamState state = streamStates[GetStreamIndex(streamId)];
        if (!state.valid || !state.hasDirection)
        {
            return direction;
        }

        Vector2 smoothed = Vector2.Lerp(direction, state.direction, strokeSmoothing);
        return smoothed.sqrMagnitude > 0.000001f ? smoothed.normalized : direction;
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

    private void ResetStrokeStreams()
    {
        hasLastValidHit = false;
        lastValidHitTime = -999f;
        for (int i = 0; i < streamStates.Length; i++)
        {
            streamStates[i] = default;
        }
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

        Vector3 normal = BoardNormal;
        Vector3 downhillWorld = Vector3.ProjectOnPlane(Vector3.down, normal);
        if (downhillWorld.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector2 downhillUv = WorldDeltaToUvDelta(downhillWorld.normalized);
        Vector2 downhillPixels = new Vector2(downhillUv.x * textureWidth, downhillUv.y * textureHeight);

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

    public Vector2 LocalToUv(Vector3 localPoint)
    {
        return WorldToUv(transform.TransformPoint(localPoint));
    }

    public Vector3 UvToLocal(Vector2 uv)
    {
        return transform.InverseTransformPoint(UvToWorld(uv));
    }

    public Vector2 LocalDeltaToUvDelta(Vector3 localDelta)
    {
        return WorldDeltaToUvDelta(transform.TransformVector(localDelta));
    }

    public Vector3 UvToWorld(Vector2 uv)
    {
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);

        if (invertUpAxis)
        {
            uv.y = 1f - uv.y;
        }

        if (invertRightAxis)
        {
            uv.x = 1f - uv.x;
        }

        if (swapAxes)
        {
            uv = new Vector2(uv.y, uv.x);
        }

        Vector2 halfSize = GetBoardWorldHalfExtents();
        float x = (uv.x - 0.5f) * halfSize.x * 2f;
        float y = (uv.y - 0.5f) * halfSize.y * 2f;
        return BoardCenter + BoardRightAxis * x + BoardUpAxis * y;
    }

    public Vector2 WorldToUv(Vector3 worldHit)
    {
        WorldToBoardUv(worldHit, out Vector2 uv, out _);
        return uv;
    }

    public bool WorldToBoardUv(Vector3 worldHit, out Vector2 uv, out Vector2 localXY)
    {
        localXY = WorldToBoardPoint(worldHit);
        Vector2 halfSize = GetBoardWorldHalfExtents();
        float u = localXY.x / Mathf.Max(0.001f, halfSize.x * 2f) + 0.5f;
        float v = localXY.y / Mathf.Max(0.001f, halfSize.y * 2f) + 0.5f;
        uv = new Vector2(u, v);

        if (swapAxes)
        {
            uv = new Vector2(uv.y, uv.x);
        }

        if (invertRightAxis)
        {
            uv.x = 1f - uv.x;
        }

        if (invertUpAxis)
        {
            uv.y = 1f - uv.y;
        }

        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);
        return Mathf.Abs(localXY.x) <= halfSize.x && Mathf.Abs(localXY.y) <= halfSize.y;
    }

    public Vector2 WorldDeltaToUvDelta(Vector3 worldDelta)
    {
        Vector2 halfSize = GetBoardWorldHalfExtents();
        Vector2 delta = new Vector2(
            Vector3.Dot(worldDelta, BoardRightAxis) / Mathf.Max(0.001f, halfSize.x * 2f),
            Vector3.Dot(worldDelta, BoardUpAxis) / Mathf.Max(0.001f, halfSize.y * 2f)
        );

        if (swapAxes)
        {
            delta = new Vector2(delta.y, delta.x);
        }

        if (invertRightAxis)
        {
            delta.x = -delta.x;
        }

        if (invertUpAxis)
        {
            delta.y = -delta.y;
        }

        return delta;
    }

    private Vector2 GetBoardWorldHalfExtents()
    {
        Vector3 scale = transform.lossyScale;
        switch (mappingPlane)
        {
            case BoardMappingPlane.LocalXY:
                return new Vector2(
                    Mathf.Max(0.001f, localHalfExtents.x * Mathf.Abs(scale.x)),
                    Mathf.Max(0.001f, localHalfExtents.y * Mathf.Abs(scale.y))
                );
            case BoardMappingPlane.LocalYZ:
                return new Vector2(
                    Mathf.Max(0.001f, localHalfExtents.x * Mathf.Abs(scale.z)),
                    Mathf.Max(0.001f, localHalfExtents.y * Mathf.Abs(scale.y))
                );
            default:
                return new Vector2(
                    Mathf.Max(0.001f, localHalfExtents.x * Mathf.Abs(scale.x)),
                    Mathf.Max(0.001f, localHalfExtents.y * Mathf.Abs(scale.z))
                );
        }
    }

    private Vector2 WorldToBoardPoint(Vector3 worldHit)
    {
        Vector3 localVector = worldHit - BoardCenter;
        return new Vector2(
            Vector3.Dot(localVector, BoardRightAxis),
            Vector3.Dot(localVector, BoardUpAxis)
        );
    }

    private Vector3 GetBoardCenter()
    {
        return transform.position;
    }

    private Vector3 GetBoardRightAxis()
    {
        GetRawBoardAxes(out Vector3 right, out Vector3 up, out _);
        return right;
    }

    private Vector3 GetBoardUpAxis()
    {
        GetRawBoardAxes(out Vector3 right, out Vector3 up, out _);
        return up;
    }

    private Vector3 GetBoardNormal()
    {
        GetRawBoardAxes(out _, out _, out Vector3 normal);
        return normal;
    }

    private Vector3 GetCollisionBoardNormal()
    {
        Vector3 normal = GetBoardNormal();
        return invertBoardNormalForCollision ? -normal : normal;
    }

    private void GetRawBoardAxes(out Vector3 right, out Vector3 up, out Vector3 normal)
    {
        switch (mappingPlane)
        {
            case BoardMappingPlane.LocalXY:
                right = transform.right;
                up = transform.up;
                normal = transform.forward;
                break;
            case BoardMappingPlane.LocalYZ:
                right = transform.forward;
                up = transform.up;
                normal = transform.right;
                break;
            default:
                right = transform.right;
                up = transform.forward;
                normal = transform.up;
                break;
        }

        right = right.sqrMagnitude > 0.000001f ? right.normalized : Vector3.right;
        up = up.sqrMagnitude > 0.000001f ? up.normalized : Vector3.up;
        if (normal.sqrMagnitude <= 0.000001f)
        {
            normal = transform.up.sqrMagnitude > 0.000001f ? transform.up.normalized : Vector3.up;
        }
        else
        {
            normal.Normalize();
        }
    }

    private void RecordMappingDebug(Vector3 worldHit, Vector2 uv, Vector3 particleCurrentPosition)
    {
        lastWorldHit = worldHit;
        lastLocalHit = transform.InverseTransformPoint(worldHit);
        lastPixelHit = new Vector2(
            Mathf.RoundToInt(uv.x * (textureWidth - 1)),
            Mathf.RoundToInt(uv.y * (textureHeight - 1))
        );
        lastUvToWorldCheck = UvToWorld(uv);
        mappingErrorDistance = Vector3.Distance(lastWorldHit, lastUvToWorldCheck);
        lastDebugCollisionTime = Application.isPlaying ? Time.time : 0f;
        DrawMappingDebugMarkers(lastWorldHit, lastUvToWorldCheck, particleCurrentPosition);
    }

    private void ResetAfterMappingChange(bool clearCanvas)
    {
        ResetStrokeStreams();
        ResetMappingDebug();
        if (clearCanvas)
        {
            ClearPainting();
        }
    }

    private void ResetMappingDebug()
    {
        lastWorldHit = Vector3.zero;
        lastLocalHit = Vector3.zero;
        lastPixelHit = Vector2.zero;
        lastUvToWorldCheck = Vector3.zero;
        lastParticlePreviousPosition = Vector3.zero;
        lastParticleCurrentPosition = Vector3.zero;
        mappingErrorDistance = 0f;
        lastDebugCollisionTime = -999f;
        HideDebugMarkers();
    }

    private void EnsureWorldPaintRenderer()
    {
        if (worldPaintRenderer != null)
        {
            SyncWorldPaintRendererSettings();
            return;
        }

        worldPaintRenderer = GetComponent<WorldPaintRenderer>();
        if (worldPaintRenderer == null)
        {
            worldPaintRenderer = gameObject.AddComponent<WorldPaintRenderer>();
        }
        SyncWorldPaintRendererSettings();
    }

    private void SyncWorldPaintRendererSettings()
    {
        if (worldPaintRenderer == null)
        {
            return;
        }

        paintSurfaceOffset = Mathf.Clamp(paintSurfaceOffset, 0.001f, 0.08f);
        worldPaintRenderer.paintSurfaceOffset = paintSurfaceOffset;
        worldPaintRenderer.invertPaintNormal = invertPaintNormal;
        worldPaintRenderer.alwaysVisibleDebug = paintDecalsAlwaysVisibleDebug;
        worldPaintRenderer.worldPaintFallbackGeometry = worldPaintFallbackGeometry;
    }

    private void DrawMappingDebugMarkers(Vector3 hitPosition, Vector3 uvPosition, Vector3 particlePosition)
    {
        if (!showMappingDebugMarkers)
        {
            HideDebugMarkers();
            return;
        }

        if (debugMarkerHistoryMode == DebugMarkerHistoryMode.Last10Hits)
        {
            DrawHistoryDebugMarkers(hitPosition, uvPosition, particlePosition);
            return;
        }

        HideHistoryDebugMarkers();
        DrawHitPointMarker(hitPosition, true);
        DrawUvBackToWorldMarker(uvPosition, true);
        DrawParticleCurrentMarker(particlePosition, false);
    }

    private void DrawHistoryDebugMarkers(Vector3 hitPosition, Vector3 uvPosition, Vector3 particlePosition)
    {
        EnsureDebugMarkerHistory();
        if (debugMarkerHistorySets.Count == 0)
        {
            return;
        }

        DebugMarkerSet markerSet = debugMarkerHistorySets[nextDebugMarkerHistoryIndex];
        nextDebugMarkerHistoryIndex = (nextDebugMarkerHistoryIndex + 1) % debugMarkerHistorySets.Count;
        DrawDebugMarker(ref markerSet.hit, "PaintComputedHitPointMarker", Color.red, hitPosition, true);
        DrawDebugMarker(ref markerSet.uv, "PaintUvBackToWorldMarker", Color.green, uvPosition, true);
        DrawDebugMarker(ref markerSet.particle, "PaintParticleCurrentMarker", Color.yellow, particlePosition, false);
        debugMarkerHistorySets[(nextDebugMarkerHistoryIndex + debugMarkerHistorySets.Count - 1) % debugMarkerHistorySets.Count] = markerSet;

        DrawHitPointMarker(hitPosition, true);
        DrawUvBackToWorldMarker(uvPosition, true);
        DrawParticleCurrentMarker(particlePosition, false);
    }

    private void DrawHitPointMarker(Vector3 position, bool offsetFromBoard)
    {
        DrawDebugMarker(ref hitPointMarker, "PaintComputedHitPointMarker", Color.red, position, offsetFromBoard);
    }

    private void DrawUvBackToWorldMarker(Vector3 position, bool offsetFromBoard)
    {
        DrawDebugMarker(ref uvBackToWorldMarker, "PaintUvBackToWorldMarker", Color.green, position, offsetFromBoard);
    }

    private void DrawParticleCurrentMarker(Vector3 position, bool offsetFromBoard)
    {
        DrawDebugMarker(ref particleCurrentMarker, "PaintParticleCurrentMarker", Color.yellow, position, offsetFromBoard);
    }

    private void DrawDebugMarker(ref GameObject marker, string markerName, Color color, Vector3 position, bool offsetFromBoard)
    {
        if (!showMappingDebugMarkers)
        {
            if (marker != null)
            {
                marker.SetActive(false);
            }
            return;
        }

        if (marker == null)
        {
            marker = CreateDebugRingCross(markerName, color);
            marker.transform.SetParent(transform, true);
        }

        Vector3 markerPosition = offsetFromBoard ? position + GetBoardNormal() * mappingDebugMarkerOffset : position;
        marker.transform.position = markerPosition;
        marker.transform.rotation = Quaternion.identity;
        marker.transform.localScale = Vector3.one * mappingDebugMarkerSize;
        marker.SetActive(true);
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

    private void EnsureDebugMarkerHistory()
    {
        while (debugMarkerHistorySets.Count < 10)
        {
            debugMarkerHistorySets.Add(new DebugMarkerSet());
        }
    }

    private void HideDebugMarkers()
    {
        HideMarker(hitPointMarker);
        HideMarker(uvBackToWorldMarker);
        HideMarker(particleCurrentMarker);
        HideHistoryDebugMarkers();
    }

    private void HideHistoryDebugMarkers()
    {
        for (int i = 0; i < debugMarkerHistorySets.Count; i++)
        {
            DebugMarkerSet markerSet = debugMarkerHistorySets[i];
            HideMarker(markerSet.hit);
            HideMarker(markerSet.uv);
            HideMarker(markerSet.particle);
        }
    }

    private void HideMarker(GameObject marker)
    {
        if (marker == null)
        {
            return;
        }

        marker.transform.position = transform.position + GetBoardNormal() * 100f;
        marker.SetActive(false);
    }

    private static bool IsMarkerActive(GameObject marker)
    {
        return marker != null && marker.activeInHierarchy;
    }

    private int CountActiveDebugMarkers()
    {
        int count = 0;
        if (HitDebugMarkerActive) count++;
        if (UvDebugMarkerActive) count++;
        if (ParticleDebugMarkerActive) count++;
        for (int i = 0; i < debugMarkerHistorySets.Count; i++)
        {
            DebugMarkerSet markerSet = debugMarkerHistorySets[i];
            if (IsMarkerActive(markerSet.hit)) count++;
            if (IsMarkerActive(markerSet.uv)) count++;
            if (IsMarkerActive(markerSet.particle)) count++;
        }
        return count;
    }

    private string BuildMappingDebugSummary()
    {
        return "markers enabled: " + (showMappingDebugMarkers ? "yes" : "no") +
            "\nred marker active: " + (HitDebugMarkerActive ? "yes" : "no") +
            "\ngreen marker active: " + (UvDebugMarkerActive ? "yes" : "no") +
            "\nlast hit position: " + FormatVector3(lastWorldHit) +
            "\nmarker offset from board: " + mappingDebugMarkerOffset.ToString("0.000") + " m" +
            "\nmarker count: " + DebugMarkerCount +
            "\nlast collision time: " + (lastDebugCollisionTime >= 0f ? lastDebugCollisionTime.ToString("0.000") : "none");
    }

    private static string FormatVector3(Vector3 value)
    {
        return value.x.ToString("0.000") + ", " + value.y.ToString("0.000") + ", " + value.z.ToString("0.000");
    }

    private void ApplyIfDirty()
    {
        if (!textureDirty || paintTexture == null)
        {
            return;
        }

        paintTexture.Apply();
        textureUpdatedCount++;
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

        if (paintTexture != null && paintRenderMode == PaintRenderMode.TextureUvLegacy)
        {
            materialInstance.mainTexture = paintTexture;
            materialInstance.mainTextureScale = Vector2.one;
            materialInstance.mainTextureOffset = Vector2.zero;
            SetMaterialTexture("_BaseMap", paintTexture);
            SetMaterialTexture("_MainTex", paintTexture);
            SetMaterialTextureScale("_BaseMap", Vector2.one);
            SetMaterialTextureScale("_MainTex", Vector2.one);
            SetMaterialTextureOffset("_BaseMap", Vector2.zero);
            SetMaterialTextureOffset("_MainTex", Vector2.zero);
        }
        else if (paintRenderMode == PaintRenderMode.WorldDecals)
        {
            materialInstance.mainTexture = null;
            SetMaterialTexture("_BaseMap", null);
            SetMaterialTexture("_MainTex", null);
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

    private void SetMaterialTexture(string propertyName, Texture texture)
    {
        if (materialInstance.HasProperty(propertyName))
        {
            materialInstance.SetTexture(propertyName, texture);
        }
    }

    private void SetMaterialTextureScale(string propertyName, Vector2 scale)
    {
        if (materialInstance.HasProperty(propertyName))
        {
            materialInstance.SetTextureScale(propertyName, scale);
        }
    }

    private void SetMaterialTextureOffset(string propertyName, Vector2 offset)
    {
        if (materialInstance.HasProperty(propertyName))
        {
            materialInstance.SetTextureOffset(propertyName, offset);
        }
    }

    private static string NormalizeSurfaceType(string value)
    {
        if (string.Equals(value, "Wood", System.StringComparison.OrdinalIgnoreCase)) return "Wood";
        if (string.Equals(value, "Metal", System.StringComparison.OrdinalIgnoreCase)) return "Metal";
        if (string.Equals(value, "Paper", System.StringComparison.OrdinalIgnoreCase)) return "Paper";
        return "Canvas";
    }

    private static bool IsFinite(Vector2 value)
    {
        return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.x) || float.IsInfinity(value.y));
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

    private struct DebugMarkerSet
    {
        public GameObject hit;
        public GameObject uv;
        public GameObject particle;
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
        if (!showMappingDebugMarkers || lastDebugCollisionTime < 0f)
        {
            return;
        }

        float markerSize = Mathf.Max(0.05f, mappingDebugMarkerSize);
        Vector3 normal = GetBoardNormal();
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(lastWorldHit + normal * mappingDebugMarkerOffset, markerSize);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(lastUvToWorldCheck + normal * mappingDebugMarkerOffset, markerSize * 1.25f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(lastParticleCurrentPosition, markerSize);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(lastWorldHit + normal * mappingDebugMarkerOffset, lastUvToWorldCheck + normal * mappingDebugMarkerOffset);
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
