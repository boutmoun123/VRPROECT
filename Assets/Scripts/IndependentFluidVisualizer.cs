using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class IndependentFluidVisualizer : MonoBehaviour
{
    public enum PreviewMode
    {
        FollowBucketLiquid,
        ManualDebug
    }

    public enum PreviewQuality
    {
        Simple,
        Enhanced
    }

    public enum ParticlePreset
    {
        Low,
        Medium,
        High,
        Ultra
    }

    public enum PreviewRenderMode
    {
        Auto,
        CpuParticleSystem,
        GpuPreview
    }

    public enum PreviewFollowModel
    {
        ExactSloshOffsets,
        AccelerationResponse,
        EnhancedEducational
    }

    [Header("Visibility")]
    public bool showPreview = true;
    public bool hidePreviewOnStart;

    [Header("Placement")]
    public Vector3 previewPosition = new Vector3(-4.0f, 1.0f, 0f);
    public Vector3 previewSize = new Vector3(2.6f, 1.25f, 1.2f);
    [Header("Preview Box Controls")]
    public float previewBoxWidth = 2.6f;
    public float previewBoxHeight = 1.25f;
    public float previewBoxDepth = 1.2f;
    public float previewPositionX = -4.0f;
    public float previewPositionY = 1.0f;
    public float previewPositionZ = 0.0f;
    [Range(0.05f, 1f)] public float fillPercent = 0.62f;

    [Header("Follow Bucket")]
    public PreviewMode previewMode = PreviewMode.FollowBucketLiquid;
    public SphericalPendulumController pendulumController;
    public bool showInternalParticles = true;

    [Header("Motion")]
    [Range(0f, 1.5f)] public float sloshStrength = 0.55f;
    [Range(0.1f, 8f)] public float sloshDamping = 2.2f;
    [Range(0.1f, 5f)] public float motionSpeed = 1.2f;

    [Header("Density")]
    public int previewParticleCount = 50000;
    public ParticlePreset particlePreset = ParticlePreset.Medium;
    public PreviewQuality previewQuality = PreviewQuality.Enhanced;
    public PreviewRenderMode previewRenderMode = PreviewRenderMode.Auto;
    public bool autoDensityMode = true;
    public bool showDensityColors = true;
    public bool showCollisionFlashes = true;
    public bool showFlowLayers = true;
    public bool showParticleCountProof;
    public bool colorSync = true;
    public Color previewColor = new Color(0.1f, 0.25f, 1f, 1f);
    [Range(0.05f, 0.35f)] public float internalLiquidOpacity = 0.2f;
    [Range(0.05f, 0.10f)] public float wallOpacity = 0.08f;
    [Range(0.35f, 0.75f)] public float surfaceOpacity = 0.58f;

    [Header("Preview Visual Controls")]
    public float previewParticleSizeMultiplier = 1f;
    public float previewParticleAlphaMultiplier = 1.2f;
    public float previewGlassAlpha = 0.07f;
    public float previewLiquidSurfaceAlpha = 0.35f;

    [Header("Preview Motion Controls")]
    public PreviewFollowModel previewFollowModel = PreviewFollowModel.AccelerationResponse;
    public float previewMotionStrength = 1.6f;
    public float previewWaveStrength = 1.4f;
    public float previewTurbulenceStrength = 0.35f;
    public float previewWallBounceStrength = 0.8f;
    public float previewFlowLayerStrength = 1f;

    [Header("Preview Follow Calibration")]
    public bool swapSloshAxes;
    public bool invertSloshX;
    public bool invertSloshZ = true;
    public float sloshGainX = 1f;
    public float sloshGainZ = 1f;
    public float liquidLag = 0.12f;
    public float liquidDamping = 2.5f;
    public float maxSurfaceTilt = 16f;
    public bool showLiquidFollowDebug;

    [Header("Color Sources")]
    public BucketPaintReservoir paintReservoir;
    public PaintParticleEmitter paintEmitter;

    public int VisibleParticleCount => visibleParticleCount;
    public bool IsFollowMode => previewMode == PreviewMode.FollowBucketLiquid;
    public float RealFillPercent => realFillPercent;
    public Color RealPaintColor => realPaintColor;
    public float BucketMotionAmount => bucketMotionAmount;
    public Vector3 BucketAcceleration => bucketAcceleration;
    public Vector2 LiquidResponse => liquidResponse;
    public Vector2 SurfaceTilt => liquidTilt;
    public float SourceSloshTheta => sourceSloshTheta;
    public float SourceSloshPhi => sourceSloshPhi;
    public float CurrentViscosity => currentViscosity;
    public int AutoParticleCount => autoParticleCount;
    public string MotionState => bucketMotionAmount <= 0.05f ? "Calm" : bucketMotionAmount < 0.75f ? "Sloshing" : "Active Slosh";
    public string DensityModeText => autoDensityMode ? "Auto" : "Manual";
    public string WarningText => warningText;
    public string StatsText => BuildStatsText();
    public int WallCollisionCount => wallCollisionCount;
    public int InternalCollisionEstimate => internalCollisionEstimate;
    public float AverageDensity => averageDensity;
    public float AveragePressure => averagePressure;
    public string VisualDensityText => previewParticleCount < 20000 ? "Sparse" : previewParticleCount < 100000 ? "Medium" : previewParticleCount < 500000 ? "Dense" : "Ultra Dense";

    private const int minParticles = 25;
    private const int maxParticles = 1000000;
    private const int cpuParticleLimit = 8000;
    private const int mediumParticleCount = 50000;
    private const float edgeThickness = 0.025f;
    private const int densityGridX = 16;
    private const int densityGridY = 8;
    private const int densityGridZ = 12;
    private const int densityGridSize = densityGridX * densityGridY * densityGridZ;
    private static readonly Vector3 defaultPreviewPosition = new Vector3(-4.0f, 1.0f, 0f);
    private static readonly Vector3 defaultPreviewSize = new Vector3(2.6f, 1.25f, 1.2f);

    private Transform visualRoot;
    private Transform wallRoot;
    private Transform edgeRoot;
    private ParticleSystem previewParticleSystem;
    private ParticleSystem.Particle[] particles;
    private Vector3[] particlePositions;
    private Vector3[] particleVelocities;
    private Vector3[] particleFlowDirections;
    private float[] particleSeeds;
    private float[] densityValues;
    private float[] pressureValues;
    private float[] collisionFlashes;
    private float[] layerFactors;
    private int[] densityGridCounts;
    private Transform liquidVolumeTransform;
    private MeshRenderer liquidVolumeRenderer;
    private MeshFilter surfaceFilter;
    private MeshRenderer surfaceRenderer;
    private Mesh surfaceMesh;
    private LineRenderer bucketMotionLine;
    private LineRenderer bucketAccelerationLine;
    private LineRenderer liquidResponseLine;
    private LineRenderer previewTiltLine;
    private Material wallMaterial;
    private Material edgeMaterial;
    private Material liquidVolumeMaterial;
    private Material surfaceMaterial;
    private Material particleMaterial;
    private Material debugLineMaterial;
    private Material gpuParticleMaterial;
    private Mesh gpuParticleMesh;
    private ComputeBuffer gpuParticleBuffer;
    private ComputeBuffer gpuArgsBuffer;
    private Bounds gpuDrawBounds;
    private TMP_Text labelText;
    private TMP_Text statsText;
    private TMP_Text particleCountProofText;
    private int visibleParticleCount;
    private int requestedParticleCount;
    private int renderedParticleCount;
    private int gpuBufferCount;
    private int lastParticleCount = -1;
    private PreviewRenderMode activeRenderMode;
    private PreviewRenderMode lastActiveRenderMode;
    private string lastRebuildReason = "Not rebuilt yet";
    private Vector3 lastSize;
    private float lastFill = -1f;
    private PreviewQuality lastQuality;
    private string warningText = "";
    private float lastRebuildTime = -1f;
    private float lastMotionTime;
    private float motionSeedOffset;
    private float realFillPercent = 0.62f;
    private Color realPaintColor = new Color(0.1f, 0.25f, 1f, 1f);
    private float bucketMotionAmount;
    private float sourceSloshTheta;
    private float sourceSloshPhi;
    private float smoothedSloshTheta;
    private float smoothedSloshPhi;
    private Vector3 bucketPosition;
    private Vector3 previousBucketPosition;
    private Vector3 previousBucketVelocity;
    private bool hasPreviousBucketSample;
    private Vector3 bucketVelocity;
    private Vector3 bucketAcceleration;
    private Vector3 smoothedBucketVelocity;
    private Vector3 smoothedBucketAcceleration;
    private Vector2 rawLiquidResponse;
    private Vector2 liquidResponse;
    private Vector2 liquidResponseVelocity;
    private Vector2 liquidTilt;
    private Vector2 liquidTiltVelocity;
    private Vector2 previewTestResponse;
    private float currentViscosity = 0.65f;
    private int autoParticleCount;
    private float followMotionTime;
    private bool realSourceAvailable;
    private int wallCollisionCount;
    private int wallCollisionsThisFrame;
    private int internalCollisionEstimate;
    private float averageDensity;
    private float averagePressure;
    private float visualDensityStrength = 0.5f;
    private Vector3 lastPresentationSize;

    private void Awake()
    {
        EnsurePreview();
    }

    private void Start()
    {
        if (hidePreviewOnStart)
        {
            showPreview = false;
        }
        ApplyVisibility();
    }

    private void OnValidate()
    {
        SanitizePreviewSettings();
        fillPercent = Mathf.Clamp01(fillPercent);
        sloshStrength = Mathf.Max(0f, sloshStrength);
        sloshDamping = Mathf.Max(0.1f, sloshDamping);
        motionSpeed = Mathf.Max(0.1f, motionSpeed);
        previewParticleCount = Mathf.Clamp(previewParticleCount, minParticles, maxParticles);
    }

    private void LateUpdate()
    {
        EnsurePreview();
        SanitizePreviewSettings();
        if (lastPresentationSize == Vector3.zero)
        {
            lastPresentationSize = previewSize;
        }
        else if ((lastPresentationSize - previewSize).sqrMagnitude > 0.000001f)
        {
            RebuildPreviewPresentation("Preview box dimensions changed", true);
        }
        UpdateFollowBucketState();
        SyncColor();
        UpdateAdaptiveSettings();
        UpdateTransformAndMaterials();
        UpdateSurfaceMesh();
        UpdateParticles();
        UpdateText();
        UpdateDebugLines();
        ApplyVisibility();
        DrawGpuPreview();
    }

    public void SetShowPreview(bool value)
    {
        showPreview = value;
        ApplyVisibility();
    }

    public void SetPreviewParticleCount(int count)
    {
        previewParticleCount = Mathf.Clamp(count, minParticles, maxParticles);
        particlePreset = ClosestPreset(previewParticleCount);
        RebuildParticles(force: true, "Manual particle count changed");
        UpdateParticles();
        UpdateText();
    }

    public void ApplyPreset(ParticlePreset preset)
    {
        particlePreset = preset;
        previewParticleCount = PresetToCount(preset);
        RebuildParticles(force: true, "Preset changed to " + preset);
        UpdateParticles();
        UpdateText();
    }

    public void ForceRebuildParticles()
    {
        EnsurePreview();
        previewParticleCount = PresetToCount(particlePreset);
        RebuildParticles(force: true, "ForceRebuildParticles");
        UpdateParticles();
        UpdateText();
    }

    public void SetPreviewBoxSize(float width, float height, float depth)
    {
        previewBoxWidth = width;
        previewBoxHeight = height;
        previewBoxDepth = depth;
        SanitizePreviewSettings();
        RebuildPreviewPresentation("Preview box size changed", true);
    }

    public void SetPreviewPosition(float x, float y, float z)
    {
        previewPositionX = SafeFloat(x, defaultPreviewPosition.x);
        previewPositionY = SafeFloat(y, defaultPreviewPosition.y);
        previewPositionZ = SafeFloat(z, defaultPreviewPosition.z);
        SanitizePreviewSettings();
        UpdateTransformAndMaterials();
    }

    public void ResetBoxTransform()
    {
        previewBoxWidth = defaultPreviewSize.x;
        previewBoxHeight = defaultPreviewSize.y;
        previewBoxDepth = defaultPreviewSize.z;
        previewPositionX = defaultPreviewPosition.x;
        previewPositionY = defaultPreviewPosition.y;
        previewPositionZ = defaultPreviewPosition.z;
        SanitizePreviewSettings();
        RebuildPreviewPresentation("Preview box transform reset", true);
    }

    public void ResetPreviewVisuals()
    {
        previewParticleSizeMultiplier = 1f;
        previewParticleAlphaMultiplier = 1.2f;
        previewGlassAlpha = 0.07f;
        previewLiquidSurfaceAlpha = 0.35f;
        previewMotionStrength = 1.6f;
        previewWaveStrength = 1.4f;
        previewTurbulenceStrength = 0.35f;
        previewWallBounceStrength = 0.8f;
        previewFlowLayerStrength = 1f;
        SanitizePreviewSettings();
        ResetPreviewMotion();
    }

    public void ForceRebuildPreview()
    {
        EnsurePreview();
        SanitizePreviewSettings();
        RebuildPreviewPresentation("ForceRebuildPreview", true);
        UpdateParticles();
        UpdateText();
    }

    public void ResetPreviewMotion()
    {
        EnsurePreview();
        motionSeedOffset = Random.value * 1000f;
        lastMotionTime = 0f;
        hasPreviousBucketSample = false;
        RebuildParticles(force: true, "Preview motion reset");
        UpdateParticles();
        UpdateText();
    }

    public static int PresetToCount(ParticlePreset preset)
    {
        switch (preset)
        {
            case ParticlePreset.Low:
                return 8000;
            case ParticlePreset.Medium:
                return 50000;
            case ParticlePreset.High:
                return 200000;
            case ParticlePreset.Ultra:
                return 1000000;
            default:
                return 50000;
        }
    }

    private static ParticlePreset ClosestPreset(int count)
    {
        if (count <= 29000) return ParticlePreset.Low;
        if (count <= 125000) return ParticlePreset.Medium;
        if (count <= 600000) return ParticlePreset.High;
        return ParticlePreset.Ultra;
    }

    private void EnsurePreview()
    {
        if (visualRoot == null)
        {
            GameObject root = new GameObject("Fluid Preview");
            root.transform.SetParent(transform, false);
            visualRoot = root.transform;
        }

        if (wallRoot == null)
        {
            wallRoot = new GameObject("Transparent Prism Walls").transform;
            wallRoot.SetParent(visualRoot, false);
            CreateWalls();
        }

        if (edgeRoot == null)
        {
            edgeRoot = new GameObject("Prism Frame Edges").transform;
            edgeRoot.SetParent(visualRoot, false);
            CreateEdges();
        }

        if (liquidVolumeTransform == null)
        {
            GameObject liquidVolume = GameObject.CreatePrimitive(PrimitiveType.Cube);
            liquidVolume.name = "Preview Liquid Volume";
            DestroyRuntime(liquidVolume.GetComponent<Collider>());
            liquidVolume.transform.SetParent(visualRoot, false);
            liquidVolumeTransform = liquidVolume.transform;
            liquidVolumeRenderer = liquidVolume.GetComponent<MeshRenderer>();
            liquidVolumeMaterial = CreateTransparentMaterial(
                "Fluid Preview Liquid Volume",
                new Color(previewColor.r, previewColor.g, previewColor.b, internalLiquidOpacity),
                0.82f,
                3005,
                true);
            liquidVolumeRenderer.sharedMaterial = liquidVolumeMaterial;
        }

        if (surfaceFilter == null)
        {
            GameObject surface = new GameObject("Preview Liquid Surface");
            surface.transform.SetParent(visualRoot, false);
            surfaceFilter = surface.AddComponent<MeshFilter>();
            surfaceRenderer = surface.AddComponent<MeshRenderer>();
            surfaceMaterial = CreateTransparentMaterial(
                "Fluid Preview Surface",
                new Color(previewColor.r, previewColor.g, previewColor.b, surfaceOpacity),
                0.92f,
                3010,
                false);
            surfaceRenderer.sharedMaterial = surfaceMaterial;
        }

        if (previewParticleSystem == null)
        {
            GameObject particleObject = new GameObject("Preview Internal Flow Particles");
            particleObject.transform.SetParent(visualRoot, false);
            previewParticleSystem = particleObject.AddComponent<ParticleSystem>();
            ConfigureParticleSystem();
        }

        if (labelText == null)
        {
            labelText = CreateWorldText("Fluid Preview Label", "Fluid Preview", 0.18f, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        if (statsText == null)
        {
            statsText = CreateWorldText("Fluid Preview Stats", "", 0.095f, FontStyles.Normal, TextAlignmentOptions.Left);
        }

        if (particleCountProofText == null)
        {
            particleCountProofText = CreateWorldText("Fluid Preview Particle Count Proof", "", 0.11f, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        EnsureDebugLines();

        EnsureGpuPreviewResources();
    }

    private void EnsureDebugLines()
    {
        if (debugLineMaterial == null)
        {
            debugLineMaterial = CreateTransparentMaterial("Fluid Preview Debug Lines", Color.white, 0.35f, 3600, true);
        }

        bucketMotionLine = EnsureDebugLine(bucketMotionLine, "Bucket Motion Vector", new Color(0.2f, 0.75f, 1f, 0.95f));
        bucketAccelerationLine = EnsureDebugLine(bucketAccelerationLine, "Bucket Acceleration Vector", new Color(1f, 0.35f, 0.25f, 0.95f));
        liquidResponseLine = EnsureDebugLine(liquidResponseLine, "Liquid Response Vector", new Color(0.25f, 1f, 0.55f, 0.95f));
        previewTiltLine = EnsureDebugLine(previewTiltLine, "Preview Tilt Direction", new Color(1f, 0.9f, 0.25f, 0.95f));
    }

    private LineRenderer EnsureDebugLine(LineRenderer line, string objectName, Color color)
    {
        if (line == null)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(visualRoot, false);
            line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = 0.025f;
            line.endWidth = 0.008f;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.sharedMaterial = debugLineMaterial;
        }

        line.startColor = color;
        line.endColor = color;
        return line;
    }

    private void CreateWalls()
    {
        wallMaterial = CreateTransparentMaterial("Fluid Preview Glass Walls", new Color(0.58f, 0.86f, 1f, wallOpacity), 0.96f, 3000, true);
        CreateWall("Back Wall", new Vector3(0f, 0f, 0.5f), new Vector3(1f, 1f, edgeThickness));
        CreateWall("Left Wall", new Vector3(-0.5f, 0f, 0f), new Vector3(edgeThickness, 1f, 1f));
        CreateWall("Right Wall", new Vector3(0.5f, 0f, 0f), new Vector3(edgeThickness, 1f, 1f));
        CreateWall("Bottom Wall", new Vector3(0f, -0.5f, 0f), new Vector3(1f, edgeThickness, 1f));
        CreateWall("Front Wall", new Vector3(0f, 0f, -0.5f), new Vector3(1f, 1f, edgeThickness));
    }

    private void CreateWall(string wallName, Vector3 localPosition, Vector3 localScale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = wallName;
        DestroyRuntime(wall.GetComponent<Collider>());
        wall.transform.SetParent(wallRoot, false);
        wall.transform.localPosition = localPosition;
        wall.transform.localScale = localScale;
        wall.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;
    }

    private void CreateEdges()
    {
        edgeMaterial = CreateTransparentMaterial("Fluid Preview Frame", new Color(0.82f, 0.96f, 1f, 0.95f), 0.7f, 3100, true);
        Vector3[] corners =
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
        };

        int[,] edges =
        {
            {0, 1}, {1, 2}, {2, 3}, {3, 0},
            {4, 5}, {5, 6}, {6, 7}, {7, 4},
            {0, 4}, {1, 5}, {2, 6}, {3, 7}
        };

        for (int i = 0; i < edges.GetLength(0); i++)
        {
            GameObject edge = new GameObject("Frame Edge " + i);
            edge.transform.SetParent(edgeRoot, false);
            LineRenderer line = edge.AddComponent<LineRenderer>();
            line.sharedMaterial = edgeMaterial;
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = 0.02f;
            line.endWidth = 0.02f;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.SetPosition(0, corners[edges[i, 0]]);
            line.SetPosition(1, corners[edges[i, 1]]);
        }
    }

    private TMP_Text CreateWorldText(string objectName, string text, float size, FontStyles style, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(visualRoot, false);
        TMP_Text tmp = textObject.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.color = new Color(0.9f, 0.97f, 1f, 1f);
        return tmp;
    }

    private void ConfigureParticleSystem()
    {
        ParticleSystem.MainModule main = previewParticleSystem.main;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = cpuParticleLimit;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 999999f;
        main.startSpeed = 0f;
        main.startSize = 0.035f;

        ParticleSystem.EmissionModule emission = previewParticleSystem.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = previewParticleSystem.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer = previewParticleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.enabled = true;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.sortingFudge = -10f;
        particleMaterial = CreateTransparentMaterial(
            "Fluid Preview Particles",
            new Color(previewColor.r, previewColor.g, previewColor.b, 0.78f),
            0.65f,
            3400,
            true);
        renderer.sharedMaterial = particleMaterial;
        previewParticleSystem.Clear();
        previewParticleSystem.Play();
    }

    private void UpdateFollowBucketState()
    {
        if (!IsFollowMode)
        {
            float manualDt = Mathf.Max(0.001f, Time.unscaledDeltaTime);
            realFillPercent = fillPercent;
            realPaintColor = CurrentColor();
            bucketMotionAmount = Mathf.Clamp01(sloshStrength);
            bucketVelocity = Vector3.zero;
            bucketAcceleration = Vector3.zero;
            smoothedBucketVelocity = Vector3.zero;
            smoothedBucketAcceleration = Vector3.zero;
            rawLiquidResponse = Vector2.zero;
            liquidResponse = Vector2.SmoothDamp(liquidResponse, previewTestResponse, ref liquidResponseVelocity, 0.12f, Mathf.Infinity, manualDt);
            liquidTilt = Vector2.SmoothDamp(liquidTilt, ResponseToTilt(liquidResponse), ref liquidTiltVelocity, 0.12f, Mathf.Infinity, manualDt);
            previewTestResponse = Vector2.Lerp(previewTestResponse, Vector2.zero, 1f - Mathf.Exp(-manualDt * 1.8f));
            autoParticleCount = previewParticleCount;
            realSourceAvailable = false;
            return;
        }

        if (pendulumController == null)
        {
            pendulumController = FindFirstObjectByType<SphericalPendulumController>();
        }
        if (paintReservoir == null)
        {
            paintReservoir = FindFirstObjectByType<BucketPaintReservoir>();
        }
        if (paintEmitter == null)
        {
            paintEmitter = FindFirstObjectByType<PaintParticleEmitter>();
        }

        realSourceAvailable = pendulumController != null;
        realFillPercent = ResolveRealFillPercent();
        fillPercent = realFillPercent;
        realPaintColor = ResolveRealPaintColor();
        previewColor = realPaintColor;
        currentViscosity = ResolveRealViscosity();

        sourceSloshTheta = 0f;
        sourceSloshPhi = 0f;
        bucketVelocity = Vector3.zero;
        bucketAcceleration = Vector3.zero;
        if (pendulumController != null)
        {
            sourceSloshTheta = pendulumController.sloshThetaOffset;
            sourceSloshPhi = pendulumController.sloshPhiOffset;
            bucketPosition = pendulumController.ropeAttachPoint != null
                ? pendulumController.ropeAttachPoint.position
                : pendulumController.transform.position;

            if (Mathf.Abs(sourceSloshTheta) < 0.0005f && Mathf.Abs(sourceSloshPhi) < 0.0005f)
            {
                sourceSloshTheta = -pendulumController.CurrentThetaDot * 0.035f;
                sourceSloshPhi = -pendulumController.CurrentPhiDot * 0.035f;
            }
        }

        sourceSloshTheta = SafeFloat(sourceSloshTheta);
        sourceSloshPhi = SafeFloat(sourceSloshPhi);

        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);
        if (!hasPreviousBucketSample)
        {
            previousBucketPosition = bucketPosition;
            previousBucketVelocity = pendulumController != null ? SafeVector3(pendulumController.attachPointVelocity) : Vector3.zero;
            hasPreviousBucketSample = true;
        }

        Vector3 measuredVelocity = (bucketPosition - previousBucketPosition) / dt;
        Vector3 controllerVelocity = pendulumController != null ? SafeVector3(pendulumController.attachPointVelocity) : measuredVelocity;
        bucketVelocity = controllerVelocity.sqrMagnitude > 0.000001f ? controllerVelocity : measuredVelocity;
        bucketAcceleration = (bucketVelocity - previousBucketVelocity) / dt;
        bucketVelocity = SafeVector3(bucketVelocity);
        bucketAcceleration = SafeVector3(bucketAcceleration);
        previousBucketPosition = bucketPosition;
        previousBucketVelocity = bucketVelocity;

        float viscosity01 = Mathf.InverseLerp(0.01f, 3f, currentViscosity);
        float sloshResponse = Mathf.Lerp(10f, 3.2f, viscosity01);
        float velocityResponse = Mathf.Lerp(7f, 2.4f, viscosity01);
        float accelerationResponse = Mathf.Lerp(5.8f, 1.9f, viscosity01);
        smoothedSloshTheta = Mathf.Lerp(smoothedSloshTheta, sourceSloshTheta, 1f - Mathf.Exp(-sloshResponse * dt));
        smoothedSloshPhi = Mathf.Lerp(smoothedSloshPhi, sourceSloshPhi, 1f - Mathf.Exp(-sloshResponse * dt));
        smoothedBucketVelocity = Vector3.Lerp(smoothedBucketVelocity, bucketVelocity, 1f - Mathf.Exp(-velocityResponse * dt));
        smoothedBucketAcceleration = Vector3.Lerp(smoothedBucketAcceleration, bucketAcceleration, 1f - Mathf.Exp(-accelerationResponse * dt));
        smoothedSloshTheta = SafeFloat(smoothedSloshTheta);
        smoothedSloshPhi = SafeFloat(smoothedSloshPhi);
        smoothedBucketVelocity = SafeVector3(smoothedBucketVelocity);
        smoothedBucketAcceleration = SafeVector3(smoothedBucketAcceleration);

        rawLiquidResponse = BuildTargetLiquidResponse(viscosity01);
        Vector2 targetResponse = ApplySloshCalibration(rawLiquidResponse) + previewTestResponse;
        float smoothTime = Mathf.Max(0.015f, liquidLag * Mathf.Lerp(0.72f, 2.2f, viscosity01));
        float maxResponseSpeed = Mathf.Max(0.1f, liquidDamping);
        liquidResponse = Vector2.SmoothDamp(liquidResponse, targetResponse, ref liquidResponseVelocity, smoothTime, maxResponseSpeed, dt);
        liquidResponse = SafeVector2(Vector2.ClampMagnitude(liquidResponse, 1.25f));
        Vector2 targetTilt = ResponseToTilt(liquidResponse);
        liquidTilt = Vector2.SmoothDamp(liquidTilt, targetTilt, ref liquidTiltVelocity, smoothTime * 0.85f, Mathf.Max(10f, maxSurfaceTilt * liquidDamping), dt);
        liquidTilt = SafeVector2(new Vector2(
            Mathf.Clamp(liquidTilt.x, -maxSurfaceTilt, maxSurfaceTilt),
            Mathf.Clamp(liquidTilt.y, -maxSurfaceTilt, maxSurfaceTilt)));
        previewTestResponse = Vector2.Lerp(previewTestResponse, Vector2.zero, 1f - Mathf.Exp(-dt * 1.8f));

        float sloshAmount = Mathf.Clamp01(liquidResponse.magnitude);
        float velocityAmount = Mathf.Clamp01(smoothedBucketVelocity.magnitude / 3.5f);
        float accelerationAmount = Mathf.Clamp01(new Vector2(smoothedBucketAcceleration.x, smoothedBucketAcceleration.z).magnitude / 9f);
        bucketMotionAmount = Mathf.Clamp01(sloshAmount * 0.62f + accelerationAmount * 0.28f + velocityAmount * 0.1f);
        bucketMotionAmount *= Mathf.Lerp(1.18f, 0.58f, viscosity01);

        float clockSpeed = Mathf.Lerp(0.08f, 2.8f, bucketMotionAmount) * Mathf.Lerp(1.25f, 0.45f, viscosity01);
        followMotionTime += dt * clockSpeed;

        autoParticleCount = PresetToCount(particlePreset);
        if (previewParticleCount != autoParticleCount)
        {
            previewParticleCount = autoParticleCount;
        }
    }

    private float ResolveRealFillPercent()
    {
        if (paintReservoir != null)
        {
            return paintReservoir.capacity > 0.001f
                ? Mathf.Clamp01(paintReservoir.currentPaintAmount / paintReservoir.capacity)
                : paintReservoir.FillPercent;
        }

        if (paintEmitter != null)
        {
            return Mathf.Clamp01(paintEmitter.remainingPaintAmount / Mathf.Max(0.001f, paintEmitter.initialPaintAmount));
        }

        return fillPercent;
    }

    private Color ResolveRealPaintColor()
    {
        if (paintReservoir != null)
        {
            return paintReservoir.VisiblePaintColor;
        }

        if (paintEmitter != null)
        {
            return paintEmitter.paintColor;
        }

        Color color = previewColor;
        color.a = 1f;
        return color;
    }

    private float ResolveRealViscosity()
    {
        if (paintEmitter != null)
        {
            return Mathf.Max(0.001f, paintEmitter.viscosity);
        }

        Simulation3D simulation = FindFirstObjectByType<Simulation3D>();
        if (simulation != null)
        {
            return Mathf.Max(0.001f, simulation.viscosityStrength);
        }

        return Mathf.Max(0.001f, currentViscosity);
    }

    private Vector2 BuildTargetLiquidResponse(float viscosity01)
    {
        Vector2 response;
        switch (previewFollowModel)
        {
            case PreviewFollowModel.ExactSloshOffsets:
                response = new Vector2(smoothedSloshPhi * 5.2f, -smoothedSloshTheta * 5.2f);
                break;
            case PreviewFollowModel.EnhancedEducational:
                response = AccelerationLiquidResponse(viscosity01) * 1.18f +
                    new Vector2(smoothedSloshPhi, -smoothedSloshTheta) * 0.85f;
                break;
            default:
                response = AccelerationLiquidResponse(viscosity01);
                break;
        }

        return SafeVector2(Vector2.ClampMagnitude(response, 1.15f));
    }

    private Vector2 AccelerationLiquidResponse(float viscosity01)
    {
        Vector2 horizontalAcceleration = new Vector2(smoothedBucketAcceleration.x, smoothedBucketAcceleration.z);
        Vector2 horizontalVelocity = new Vector2(smoothedBucketVelocity.x, smoothedBucketVelocity.z);
        float accelerationScale = Mathf.Lerp(0.042f, 0.026f, viscosity01);
        float velocityScale = Mathf.Lerp(0.018f, 0.008f, viscosity01);
        Vector2 response = -horizontalAcceleration * accelerationScale - horizontalVelocity * velocityScale;
        return Vector2.ClampMagnitude(response, 1.1f);
    }

    private Vector2 ApplySloshCalibration(Vector2 response)
    {
        if (swapSloshAxes)
        {
            response = new Vector2(response.y, response.x);
        }

        if (invertSloshX)
        {
            response.x = -response.x;
        }

        if (invertSloshZ)
        {
            response.y = -response.y;
        }

        response.x *= sloshGainX;
        response.y *= sloshGainZ;
        return SafeVector2(Vector2.ClampMagnitude(response, 1.25f));
    }

    private Vector2 ResponseToTilt(Vector2 response)
    {
        return new Vector2(
            Mathf.Clamp(response.y * maxSurfaceTilt, -maxSurfaceTilt, maxSurfaceTilt),
            Mathf.Clamp(-response.x * maxSurfaceTilt, -maxSurfaceTilt, maxSurfaceTilt));
    }

    public void TestLiquidTiltX()
    {
        previewTestResponse = new Vector2(0.85f, 0f);
    }

    public void TestLiquidTiltZ()
    {
        previewTestResponse = new Vector2(0f, 0.85f);
    }

    public void ResetLiquidCalibration()
    {
        previewFollowModel = PreviewFollowModel.AccelerationResponse;
        swapSloshAxes = false;
        invertSloshX = false;
        invertSloshZ = true;
        sloshGainX = 1f;
        sloshGainZ = 1f;
        liquidLag = 0.12f;
        liquidDamping = 2.5f;
        maxSurfaceTilt = 16f;
        previewTestResponse = Vector2.zero;
        liquidResponse = Vector2.zero;
        liquidResponseVelocity = Vector2.zero;
        liquidTilt = Vector2.zero;
        liquidTiltVelocity = Vector2.zero;
        hasPreviousBucketSample = false;
    }

    private static int AutoCountForPreset(ParticlePreset preset, float fill)
    {
        return PresetToCount(preset);
    }

    private void UpdateAdaptiveSettings()
    {
        previewParticleCount = Mathf.Clamp(previewParticleCount, minParticles, maxParticles);
        wallOpacity = Mathf.Clamp(wallOpacity, 0.05f, 0.10f);
        activeRenderMode = ResolveRenderMode();
        visibleParticleCount = showInternalParticles ? (activeRenderMode == PreviewRenderMode.CpuParticleSystem ? Mathf.Min(previewParticleCount, cpuParticleLimit) : previewParticleCount) : 0;
        renderedParticleCount = showInternalParticles ? previewParticleCount : 0;
        gpuBufferCount = activeRenderMode == PreviewRenderMode.GpuPreview && showInternalParticles ? previewParticleCount : 0;
        visualDensityStrength = VisualDensityStrengthForPreset(particlePreset);
        warningText = "";

        if (IsFollowMode)
        {
            autoDensityMode = true;
            previewQuality = previewParticleCount <= 8000 ? PreviewQuality.Simple : PreviewQuality.Enhanced;
        }

        if (activeRenderMode == PreviewRenderMode.GpuPreview)
        {
            warningText = "High detail preview: GPU visual approximation active.";
        }

        if (autoDensityMode)
        {
            if (previewParticleCount <= 8000 && previewQuality == PreviewQuality.Enhanced)
            {
                previewQuality = PreviewQuality.Simple;
            }
        }
    }

    private void UpdateTransformAndMaterials()
    {
        visualRoot.localPosition = previewPosition;
        visualRoot.localRotation = Quaternion.identity;
        wallRoot.localScale = previewSize;
        edgeRoot.localScale = previewSize;
        previewParticleSystem.transform.localScale = Vector3.one;

        Color fluid = CurrentColor();
        float density = Mathf.InverseLerp(8000f, 1000000f, previewParticleCount);
        Color wall = new Color(0.58f, 0.86f, 1f, previewGlassAlpha);
        Color liquid = new Color(fluid.r, fluid.g, fluid.b, Mathf.Lerp(internalLiquidOpacity * 0.85f, internalLiquidOpacity, density));
        Color surface = new Color(fluid.r, fluid.g, fluid.b, previewQuality == PreviewQuality.Enhanced ? previewLiquidSurfaceAlpha : previewLiquidSurfaceAlpha * 0.82f);
        Color particlesColor = new Color(fluid.r, fluid.g, fluid.b, Mathf.Clamp(AlphaForCount(previewParticleCount) * previewParticleAlphaMultiplier, 0.02f, 0.98f));
        SetMaterialColor(wallMaterial, wall);
        SetMaterialColor(liquidVolumeMaterial, liquid);
        SetMaterialColor(surfaceMaterial, surface);
        SetMaterialColor(particleMaterial, particlesColor);
        UpdateGpuMaterial();
        UpdateLiquidVolume();
    }

    private void SyncColor()
    {
        if (!colorSync || IsFollowMode)
        {
            return;
        }

        if (paintReservoir != null)
        {
            previewColor = paintReservoir.VisiblePaintColor;
        }
        else if (paintEmitter != null)
        {
            previewColor = paintEmitter.paintColor;
        }
    }

    private Color CurrentColor()
    {
        Color color = previewColor;
        color.a = 1f;
        return color;
    }

    private void UpdateSurfaceMesh()
    {
        if (surfaceMesh == null || lastSize != previewSize || Mathf.Abs(lastFill - fillPercent) > 0.001f || lastQuality != previewQuality)
        {
            RebuildSurfaceMesh();
        }

        Vector3 local = new Vector3(0f, Mathf.Lerp(-0.48f, 0.47f, fillPercent) * previewSize.y, 0f);
        float detail = Mathf.InverseLerp(8000f, 1000000f, previewParticleCount);
        float damping = IsFollowMode
            ? Mathf.Lerp(1.15f, 0.38f, Mathf.InverseLerp(0.01f, 3f, currentViscosity))
            : 1f / Mathf.Max(1f, sloshDamping * 0.55f);
        float motionAmount = IsFollowMode ? bucketMotionAmount : sloshStrength;
        float presentationMotion = motionAmount * previewMotionStrength;
        float waveMultiplier = previewFollowModel == PreviewFollowModel.EnhancedEducational ? 1.25f : 0.72f;
        float wave = presentationMotion * damping * previewWaveStrength * waveMultiplier * Mathf.Lerp(0.014f, 0.052f, detail);
        float t = SafeFloat(IsFollowMode ? followMotionTime : Time.unscaledTime * motionSpeed);
        float tiltX = IsFollowMode
            ? Mathf.Clamp(liquidTilt.x * previewMotionStrength, -maxSurfaceTilt, maxSurfaceTilt)
            : Mathf.Sin(t * 1.23f) * sloshStrength * 5f / sloshDamping;
        float tiltZ = IsFollowMode
            ? Mathf.Clamp(liquidTilt.y * previewMotionStrength, -maxSurfaceTilt, maxSurfaceTilt)
            : Mathf.Cos(t * 0.97f) * sloshStrength * 4f / sloshDamping;
        tiltX = SafeFloat(tiltX);
        tiltZ = SafeFloat(tiltZ);
        surfaceFilter.transform.localScale = new Vector3(previewSize.x * 0.92f, previewSize.y, previewSize.z * 0.92f);
        surfaceFilter.transform.localPosition = local;
        surfaceFilter.transform.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);

        Vector3[] vertices = surfaceMesh.vertices;
        int resolution = Mathf.RoundToInt(Mathf.Sqrt(vertices.Length));
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x;
                Vector3 v = vertices[i];
                float directional = IsFollowMode
                    ? liquidResponse.x * v.x * 0.18f + liquidResponse.y * v.z * 0.18f
                    : 0f;
                float travelPhase = liquidResponse.x * v.x * 8f + liquidResponse.y * v.z * 8f;
                float ripple = Mathf.Sin(travelPhase + t * 2.2f) +
                    Mathf.Cos(travelPhase * 0.7f - t * 1.7f) * 0.55f +
                    Mathf.Sin((v.x * 15.5f - v.z * 11.2f) + t * 3.1f) * previewTurbulenceStrength * 0.16f;
                v.y = SafeFloat(Mathf.Clamp(ripple * wave * 0.45f, -0.055f, 0.055f) + directional * previewMotionStrength);
                vertices[i] = v;
            }
        }
        surfaceMesh.vertices = vertices;
        surfaceMesh.RecalculateNormals();
        surfaceMesh.RecalculateBounds();
    }

    private void RebuildSurfaceMesh()
    {
        if (surfaceMesh != null)
        {
            DestroyRuntime(surfaceMesh);
        }

        int resolution = previewQuality == PreviewQuality.Enhanced ? Mathf.RoundToInt(Mathf.Lerp(12, 24, Mathf.InverseLerp(8000f, 1000000f, previewParticleCount))) : 6;
        resolution = Mathf.Clamp(resolution, 4, 24);
        surfaceMesh = new Mesh();
        surfaceMesh.name = "Independent Fluid Preview Wobble Surface";
        Vector3[] vertices = new Vector3[resolution * resolution];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (float)(resolution - 1);
                float v = z / (float)(resolution - 1);
                int i = z * resolution + x;
                vertices[i] = new Vector3(u - 0.5f, 0f, v - 0.5f);
                uvs[i] = new Vector2(u, v);
            }
        }

        int t = 0;
        for (int z = 0; z < resolution - 1; z++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int a = z * resolution + x;
                int b = a + 1;
                int c = a + resolution;
                int d = c + 1;
                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = b;
                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = d;
            }
        }

        surfaceMesh.vertices = vertices;
        surfaceMesh.uv = uvs;
        surfaceMesh.triangles = triangles;
        surfaceMesh.RecalculateNormals();
        surfaceFilter.sharedMesh = surfaceMesh;
        lastSize = previewSize;
        lastFill = fillPercent;
        lastQuality = previewQuality;
    }

    private void RebuildParticles(bool force, string reason = "Settings changed")
    {
        previewParticleCount = Mathf.Clamp(previewParticleCount, minParticles, maxParticles);
        requestedParticleCount = showInternalParticles ? previewParticleCount : 0;
        activeRenderMode = ResolveRenderMode();
        int targetParticleCount = showInternalParticles
            ? (activeRenderMode == PreviewRenderMode.CpuParticleSystem ? Mathf.Min(previewParticleCount, cpuParticleLimit) : previewParticleCount)
            : 0;

        bool needsGpuRebuild = activeRenderMode == PreviewRenderMode.GpuPreview && (gpuParticleBuffer == null || gpuBufferCount != targetParticleCount || lastActiveRenderMode != activeRenderMode);
        bool needsCpuRebuild = activeRenderMode == PreviewRenderMode.CpuParticleSystem && (particles == null || particles.Length != targetParticleCount || lastActiveRenderMode != activeRenderMode);
        if (!force && lastParticleCount == previewParticleCount && !needsCpuRebuild && !needsGpuRebuild)
        {
            return;
        }

        lastRebuildReason = reason;
        visibleParticleCount = targetParticleCount;
        renderedParticleCount = targetParticleCount;
        if (densityGridCounts == null || densityGridCounts.Length != densityGridSize)
        {
            densityGridCounts = new int[densityGridSize];
        }
        lastParticleCount = previewParticleCount;
        lastActiveRenderMode = activeRenderMode;
        lastRebuildTime = Time.unscaledTime;
        wallCollisionCount = 0;
        wallCollisionsThisFrame = 0;
        internalCollisionEstimate = 0;
        averageDensity = 0f;
        averagePressure = 0f;

        if (activeRenderMode == PreviewRenderMode.GpuPreview)
        {
            particles = null;
            particlePositions = null;
            particleVelocities = null;
            particleFlowDirections = null;
            particleSeeds = null;
            densityValues = null;
            pressureValues = null;
            collisionFlashes = null;
            layerFactors = null;
            if (previewParticleSystem != null)
            {
                previewParticleSystem.Clear();
            }
            RebuildGpuPreviewBuffer(targetParticleCount);
            return;
        }

        ReleaseGpuPreviewBuffers();
        gpuBufferCount = 0;
        particles = new ParticleSystem.Particle[visibleParticleCount];
        particlePositions = new Vector3[visibleParticleCount];
        particleVelocities = new Vector3[visibleParticleCount];
        particleFlowDirections = new Vector3[visibleParticleCount];
        particleSeeds = new float[visibleParticleCount];
        densityValues = new float[visibleParticleCount];
        pressureValues = new float[visibleParticleCount];
        collisionFlashes = new float[visibleParticleCount];
        layerFactors = new float[visibleParticleCount];

        InitializeParticleState();

        ParticleSystem.MainModule main = previewParticleSystem.main;
        main.maxParticles = Mathf.Max(main.maxParticles, visibleParticleCount);
        previewParticleSystem.Clear();
        previewParticleSystem.SetParticles(particles, visibleParticleCount);
        previewParticleSystem.Play();
    }

    private void InitializeParticleState()
    {
        if (particles == null)
        {
            return;
        }

        float fillTop = Mathf.Lerp(-0.44f, 0.43f, fillPercent);
        float top = Mathf.Max(-0.42f, fillTop - 0.018f);
        float countDetail = Mathf.InverseLerp(250f, 8000f, Mathf.Max(visibleParticleCount, 1));
        float jitter = Mathf.Lerp(0.018f, 0.006f, countDetail);
        Color baseColor = CurrentColor();
        baseColor.a = Mathf.Clamp(Mathf.Lerp(0.78f, 0.26f, countDetail) * previewParticleAlphaMultiplier, 0.02f, 0.98f);
        float particleSize = ParticleSizeForCount(visibleParticleCount);

        for (int i = 0; i < particles.Length; i++)
        {
            float seed = Hash01(i * 17 + 23);
            particleSeeds[i] = seed;
            Vector3 position = new Vector3(
                Mathf.Lerp(-0.43f, 0.43f, Halton(i + 1, 2)),
                Mathf.Lerp(-0.44f, top, Halton(i + 1, 3)),
                Mathf.Lerp(-0.43f, 0.43f, Halton(i + 1, 5)));
            position += new Vector3(HashSigned(i * 31 + 7), HashSigned(i * 47 + 11), HashSigned(i * 59 + 13)) * jitter;
            position.x = Mathf.Clamp(position.x, -0.46f, 0.46f);
            position.y = Mathf.Clamp(position.y, -0.46f, top);
            position.z = Mathf.Clamp(position.z, -0.46f, 0.46f);

            float layer = Mathf.InverseLerp(-0.46f, top, position.y);
            layerFactors[i] = layer;
            particlePositions[i] = position;
            particleVelocities[i] = new Vector3(HashSigned(i * 67 + 5), HashSigned(i * 71 + 19) * 0.35f, HashSigned(i * 73 + 29)) * Mathf.Lerp(0.015f, 0.055f, countDetail);
            particleFlowDirections[i] = Vector3.right;
            densityValues[i] = 0f;
            pressureValues[i] = 0f;
            collisionFlashes[i] = 0f;

            particles[i].position = ScaleParticlePosition(position);
            particles[i].startColor = baseColor;
            particles[i].startSize = particleSize * previewParticleSizeMultiplier * Mathf.Lerp(0.88f, 1.14f, seed);
            particles[i].remainingLifetime = 999999f;
            particles[i].startLifetime = 999999f;
            particles[i].velocity = Vector3.zero;
        }
    }

    private void UpdateParticles()
    {
        RebuildParticles(force: false);
        if (activeRenderMode == PreviewRenderMode.GpuPreview)
        {
            if (previewParticleSystem != null)
            {
                previewParticleSystem.Clear();
            }
            UpdateGpuMaterial();
            return;
        }

        if (!showInternalParticles || particles == null || particles.Length == 0)
        {
            if (previewParticleSystem != null)
            {
                previewParticleSystem.Clear();
            }
            return;
        }

        visibleParticleCount = Mathf.Min(Mathf.Clamp(previewParticleCount, minParticles, maxParticles), cpuParticleLimit);
        renderedParticleCount = visibleParticleCount;
        float detail = Mathf.InverseLerp(8000f, 1000000f, previewParticleCount);
        float t = IsFollowMode ? followMotionTime : (Time.unscaledTime + motionSeedOffset) * motionSpeed;
        lastMotionTime = t;
        float fillTop = Mathf.Lerp(-0.44f, 0.43f, fillPercent);
        float topBound = Mathf.Max(-0.42f, fillTop - 0.018f);
        float damping = IsFollowMode
            ? Mathf.Lerp(1.18f, 0.35f, Mathf.InverseLerp(0.01f, 3f, currentViscosity))
            : 1f / Mathf.Max(1f, sloshDamping * 0.45f);
        float particleSize = ParticleSizeForCount(visibleParticleCount);
        Color color = CurrentColor();
        color.a = Mathf.Clamp(AlphaForCount(visibleParticleCount) * previewParticleAlphaMultiplier, 0.02f, 0.98f);
        float motionAmount = (IsFollowMode ? bucketMotionAmount : sloshStrength) * damping * previewMotionStrength;
        float baseWave = motionAmount * previewWaveStrength * Mathf.Lerp(0.22f, 0.52f, detail);
        float swirlAmount = motionAmount * Mathf.Lerp(0.04f, previewFollowModel == PreviewFollowModel.EnhancedEducational ? 0.34f : 0.14f, detail);
        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.004f, 0.033f) * Mathf.Lerp(0.8f, 1.3f, detail);
        float viscosity01 = Mathf.InverseLerp(0.01f, 3f, currentViscosity);
        float drag = Mathf.Lerp(2.4f, 7.8f, viscosity01) + Mathf.Lerp(1.3f, 0.3f, detail);
        float wallDamping = Mathf.Lerp(0.58f, 0.74f, 1f - viscosity01);
        float neighborTarget = Mathf.Lerp(2.3f, 8.8f, detail);
        float compressionScale = Mathf.Lerp(0.45f, 0.9f, detail);
        Vector3 realShift = IsFollowMode
            ? new Vector3(liquidResponse.x, 0f, liquidResponse.y) * previewMotionStrength
            : Vector3.zero;

        BuildDensityGrid();
        wallCollisionsThisFrame = 0;
        internalCollisionEstimate = 0;
        float densitySum = 0f;
        float pressureSum = 0f;

        for (int i = 0; i < visibleParticleCount; i++)
        {
            float seed = particleSeeds[i];
            float phase = PositionPhase(seed);
            Vector3 normalized = particlePositions[i];
            Vector3 velocity = particleVelocities[i];
            float layer = Mathf.InverseLerp(-0.46f, topBound, normalized.y);
            layerFactors[i] = layer;
            int neighborCount = CountNeighborCells(normalized);
            float density = Mathf.Clamp01((neighborCount / Mathf.Max(0.001f, neighborTarget)) * compressionScale + Mathf.Lerp(0.02f, 0.25f, detail));
            float wallPressure = WallPressure(normalized, topBound);
            float sloshCompression = Mathf.Clamp01(Mathf.Abs(realShift.x) + Mathf.Abs(realShift.z) + motionAmount * 0.35f);
            float pressure = Mathf.Clamp01(density * 0.72f + wallPressure * 0.38f + sloshCompression * Mathf.Lerp(0.1f, 0.32f, layer));
            densityValues[i] = density;
            pressureValues[i] = pressure;
            internalCollisionEstimate += Mathf.Max(0, Mathf.RoundToInt(neighborCount - neighborTarget));
            densitySum += density;
            pressureSum += pressure;

            float lowerDamping = Mathf.Lerp(0.38f, 1f, layer);
            float layerResponse = showFlowLayers ? Mathf.Lerp(0.22f, 1.45f, layer) * previewFlowLayerStrength : 0.82f;
            float angle = t * (0.8f + seed * 1.2f) + phase + layer * Mathf.PI * Mathf.Lerp(0.75f, 3.25f, detail);
            Vector3 surfaceDownhill = new Vector3(liquidResponse.x, 0f, liquidResponse.y);
            Vector3 inertiaDrive = realShift * Mathf.Lerp(0.42f, 1.85f, layer);
            Vector3 drive = new Vector3(
                inertiaDrive.x + surfaceDownhill.x * layer * 0.48f + Mathf.Sin(angle + normalized.z * 7f) * baseWave * 0.18f,
                Mathf.Sin(angle * 1.7f + normalized.x * 4f) * baseWave * 0.18f * layer,
                inertiaDrive.z + surfaceDownhill.z * layer * 0.48f + Mathf.Cos(angle * 0.9f + normalized.x * 6f) * baseWave * 0.18f);
            Vector3 circulation = new Vector3(-normalized.z, 0f, normalized.x) * swirlAmount * Mathf.Lerp(0.22f, 1.45f, layer) * previewFlowLayerStrength * (0.55f + density);
            Vector3 pressurePush = new Vector3(normalized.x, Mathf.Lerp(-0.08f, 0.18f, layer), normalized.z).normalized * pressure * Mathf.Lerp(0.02f, 0.12f, detail);
            velocity += (drive + circulation + pressurePush) * layerResponse * dt;
            velocity += new Vector3(
                HashSigned(i * 97 + Mathf.FloorToInt(t * 17f)),
                HashSigned(i * 89 + Mathf.FloorToInt(t * 19f)) * 0.35f * layer,
                HashSigned(i * 101 + Mathf.FloorToInt(t * 13f))) * (0.35f + detail) * previewTurbulenceStrength * (previewFollowModel == PreviewFollowModel.EnhancedEducational ? 0.038f : 0.018f) * dt;
            velocity *= Mathf.Exp(-drag * dt * (1.15f - lowerDamping * 0.45f));

            normalized += velocity * dt;
            bool collided = ResolveWallCollision(ref normalized, ref velocity, topBound, wallDamping * previewWallBounceStrength);
            if (collided)
            {
                collisionFlashes[i] = 1f;
                wallCollisionCount++;
                wallCollisionsThisFrame++;
            }
            else
            {
                collisionFlashes[i] = Mathf.Max(0f, collisionFlashes[i] - dt * 4.8f);
            }

            particlePositions[i] = normalized;
            particleVelocities[i] = velocity;
            particleFlowDirections[i] = velocity.sqrMagnitude > 0.000001f ? velocity.normalized : drive.normalized;

            Color particleColor = BuildParticleColor(color, density, pressure, collisionFlashes[i], layer, detail);
            particles[i].position = ScaleParticlePosition(normalized);
            particles[i].startColor = particleColor;
            particles[i].startSize = particleSize * previewParticleSizeMultiplier * Mathf.Lerp(1.14f, 0.82f, seed) * Mathf.Lerp(0.9f, 1.22f, Mathf.Max(pressure, collisionFlashes[i] * 0.75f));
            particles[i].remainingLifetime = 999999f;
            particles[i].startLifetime = 999999f;
            particles[i].rotation3D = new Vector3(
                Mathf.Sin(angle) * 28f * layerResponse,
                Mathf.Repeat((angle + phase) * Mathf.Rad2Deg, 360f),
                Mathf.Cos(angle * 0.7f) * 28f * layerResponse);
            particles[i].velocity = ScaleParticlePosition(velocity);
        }

        averageDensity = visibleParticleCount > 0 ? densitySum / visibleParticleCount : 0f;
        averagePressure = visibleParticleCount > 0 ? pressureSum / visibleParticleCount : 0f;
        previewParticleSystem.SetParticles(particles, visibleParticleCount);
    }

    private PreviewRenderMode ResolveRenderMode()
    {
        if (!showInternalParticles)
        {
            return PreviewRenderMode.CpuParticleSystem;
        }

        if (previewRenderMode == PreviewRenderMode.CpuParticleSystem)
        {
            return previewParticleCount <= cpuParticleLimit ? PreviewRenderMode.CpuParticleSystem : PreviewRenderMode.GpuPreview;
        }

        if (previewRenderMode == PreviewRenderMode.GpuPreview)
        {
            return PreviewRenderMode.GpuPreview;
        }

        return previewParticleCount <= cpuParticleLimit ? PreviewRenderMode.CpuParticleSystem : PreviewRenderMode.GpuPreview;
    }

    private static float VisualDensityStrengthForPreset(ParticlePreset preset)
    {
        switch (preset)
        {
            case ParticlePreset.Low:
                return 0.25f;
            case ParticlePreset.High:
                return 0.85f;
            case ParticlePreset.Ultra:
                return 1.25f;
            default:
                return 0.5f;
        }
    }

    private void EnsureGpuPreviewResources()
    {
        if (gpuParticleMesh == null)
        {
            gpuParticleMesh = CreateBillboardQuadMesh();
        }

        if (gpuParticleMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/IndependentFluidPreviewGpu");
            if (shader != null)
            {
                gpuParticleMaterial = new Material(shader);
                gpuParticleMaterial.name = "Independent Fluid Preview GPU Particles";
                gpuParticleMaterial.enableInstancing = true;
            }
            else
            {
                warningText = "GPU Preview shader missing: Hidden/IndependentFluidPreviewGpu";
            }
        }
    }

    private void RebuildGpuPreviewBuffer(int count)
    {
        EnsureGpuPreviewResources();
        ReleaseGpuPreviewBuffers();
        gpuBufferCount = Mathf.Max(0, count);
        renderedParticleCount = gpuBufferCount;
        if (gpuBufferCount <= 0 || gpuParticleMesh == null || gpuParticleMaterial == null)
        {
            return;
        }

        Vector4[] data = new Vector4[gpuBufferCount];
        float fillTop = Mathf.Lerp(-0.44f, 0.43f, fillPercent);
        float top = Mathf.Max(-0.42f, fillTop - 0.018f);
        float detail = Mathf.InverseLerp(8000f, 1000000f, gpuBufferCount);
        float jitter = Mathf.Lerp(0.02f, 0.0025f, detail);

        for (int i = 0; i < data.Length; i++)
        {
            float x = Mathf.Lerp(-0.43f, 0.43f, Halton(i + 1, 2));
            float y = Mathf.Lerp(-0.44f, top, Halton(i + 1, 3));
            float z = Mathf.Lerp(-0.43f, 0.43f, Halton(i + 1, 5));
            x = Mathf.Clamp(x + HashSigned(i * 31 + 7) * jitter, -0.46f, 0.46f);
            y = Mathf.Clamp(y + HashSigned(i * 47 + 11) * jitter, -0.46f, top);
            z = Mathf.Clamp(z + HashSigned(i * 59 + 13) * jitter, -0.46f, 0.46f);
            data[i] = new Vector4(x, y, z, Hash01(i * 17 + 23));
        }

        gpuParticleBuffer = new ComputeBuffer(gpuBufferCount, sizeof(float) * 4);
        gpuParticleBuffer.SetData(data);
        gpuArgsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5]
        {
            gpuParticleMesh.GetIndexCount(0),
            (uint)gpuBufferCount,
            gpuParticleMesh.GetIndexStart(0),
            gpuParticleMesh.GetBaseVertex(0),
            0
        };
        gpuArgsBuffer.SetData(args);
        gpuParticleMaterial.SetBuffer("_ParticleData", gpuParticleBuffer);
        gpuDrawBounds = new Bounds(visualRoot != null ? visualRoot.position : transform.position, previewSize * 4f + Vector3.one * 2f);
        UpdateGpuMaterial();
    }

    private void UpdateGpuMaterial()
    {
        if (gpuParticleMaterial == null)
        {
            return;
        }

        Color color = CurrentColor();
        float t = IsFollowMode ? followMotionTime : (Time.unscaledTime + motionSeedOffset) * motionSpeed;
        lastMotionTime = t;
        Vector4 slosh = IsFollowMode
            ? new Vector4(
                liquidResponse.x * 0.16f * previewMotionStrength,
                liquidResponse.y * 0.16f * previewMotionStrength,
                smoothedBucketVelocity.x * 0.018f * previewMotionStrength,
                smoothedBucketVelocity.z * 0.018f * previewMotionStrength)
            : new Vector4(
                Mathf.Sin(t * 0.7f) * sloshStrength * 0.06f * previewMotionStrength,
                Mathf.Cos(t * 0.62f) * sloshStrength * 0.06f * previewMotionStrength,
                0f,
                0f);

        gpuParticleMaterial.SetColor("_BaseColor", color);
        gpuParticleMaterial.SetVector("_PreviewSize", previewSize);
        gpuParticleMaterial.SetFloat("_FillPercent", fillPercent);
        gpuParticleMaterial.SetFloat("_TimeValue", t);
        gpuParticleMaterial.SetFloat("_ParticleScale", ParticleSizeForCount(previewParticleCount) * previewParticleSizeMultiplier);
        gpuParticleMaterial.SetFloat("_ParticleAlpha", Mathf.Clamp(AlphaForCount(previewParticleCount) * previewParticleAlphaMultiplier, 0.02f, 0.98f));
        gpuParticleMaterial.SetFloat("_DensityStrength", showDensityColors ? visualDensityStrength : 0f);
        gpuParticleMaterial.SetFloat("_CollisionStrength", showCollisionFlashes ? previewWallBounceStrength : 0f);
        gpuParticleMaterial.SetFloat("_FlowStrength", showFlowLayers ? previewFlowLayerStrength + previewTurbulenceStrength * 0.4f : 0f);
        gpuParticleMaterial.SetVector("_Slosh", slosh);
        gpuParticleMaterial.SetMatrix("_LocalToWorld", visualRoot != null ? visualRoot.localToWorldMatrix : transform.localToWorldMatrix);
        gpuDrawBounds = new Bounds(visualRoot != null ? visualRoot.position : transform.position, previewSize * 4f + Vector3.one * 2f);
        averageDensity = Mathf.Clamp01(visualDensityStrength * Mathf.Lerp(0.38f, 0.82f, fillPercent));
        averagePressure = Mathf.Clamp01(averageDensity * 0.7f + Mathf.Clamp01(new Vector2(slosh.x, slosh.y).magnitude * 2.4f) * 0.3f);
        wallCollisionsThisFrame = showCollisionFlashes ? Mathf.RoundToInt(renderedParticleCount * Mathf.Clamp01(new Vector2(slosh.x, slosh.y).magnitude) * 0.002f) : 0;
        internalCollisionEstimate = Mathf.RoundToInt(renderedParticleCount * averageDensity * 0.12f);
    }

    private void DrawGpuPreview()
    {
        if (!showPreview || !showInternalParticles || activeRenderMode != PreviewRenderMode.GpuPreview ||
            gpuParticleBuffer == null || gpuArgsBuffer == null || gpuParticleMaterial == null || gpuParticleMesh == null ||
            visualRoot == null || !visualRoot.gameObject.activeInHierarchy)
        {
            return;
        }

        Graphics.DrawMeshInstancedIndirect(gpuParticleMesh, 0, gpuParticleMaterial, gpuDrawBounds, gpuArgsBuffer);
    }

    private static Mesh CreateBillboardQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Independent Fluid Preview GPU Particle Quad";
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void ReleaseGpuPreviewBuffers()
    {
        if (gpuParticleBuffer != null)
        {
            gpuParticleBuffer.Release();
            gpuParticleBuffer = null;
        }

        if (gpuArgsBuffer != null)
        {
            gpuArgsBuffer.Release();
            gpuArgsBuffer = null;
        }
    }

    private void BuildDensityGrid()
    {
        if (densityGridCounts == null || densityGridCounts.Length != densityGridSize)
        {
            densityGridCounts = new int[densityGridSize];
        }

        for (int i = 0; i < densityGridCounts.Length; i++)
        {
            densityGridCounts[i] = 0;
        }

        if (particlePositions == null)
        {
            return;
        }

        for (int i = 0; i < visibleParticleCount && i < particlePositions.Length; i++)
        {
            densityGridCounts[CellIndex(particlePositions[i])]++;
        }
    }

    private int CountNeighborCells(Vector3 normalizedPosition)
    {
        if (densityGridCounts == null)
        {
            return 0;
        }

        int cx = NormalizedToCell(normalizedPosition.x, densityGridX);
        int cy = NormalizedToCell(normalizedPosition.y, densityGridY);
        int cz = NormalizedToCell(normalizedPosition.z, densityGridZ);
        int count = 0;

        for (int z = Mathf.Max(0, cz - 1); z <= Mathf.Min(densityGridZ - 1, cz + 1); z++)
        {
            for (int y = Mathf.Max(0, cy - 1); y <= Mathf.Min(densityGridY - 1, cy + 1); y++)
            {
                for (int x = Mathf.Max(0, cx - 1); x <= Mathf.Min(densityGridX - 1, cx + 1); x++)
                {
                    count += densityGridCounts[x + densityGridX * (y + densityGridY * z)];
                }
            }
        }

        return count;
    }

    private int CellIndex(Vector3 normalizedPosition)
    {
        int x = NormalizedToCell(normalizedPosition.x, densityGridX);
        int y = NormalizedToCell(normalizedPosition.y, densityGridY);
        int z = NormalizedToCell(normalizedPosition.z, densityGridZ);
        return x + densityGridX * (y + densityGridY * z);
    }

    private static int NormalizedToCell(float value, int cellCount)
    {
        float normalized = Mathf.InverseLerp(-0.46f, 0.46f, value);
        return Mathf.Clamp(Mathf.FloorToInt(normalized * cellCount), 0, cellCount - 1);
    }

    private bool ResolveWallCollision(ref Vector3 position, ref Vector3 velocity, float topBound, float damping)
    {
        bool collided = false;
        ResolveAxis(ref position.x, ref velocity.x, -0.46f, 0.46f, damping, ref collided);
        ResolveAxis(ref position.y, ref velocity.y, -0.46f, topBound, damping * 0.92f, ref collided);
        ResolveAxis(ref position.z, ref velocity.z, -0.46f, 0.46f, damping, ref collided);
        return collided;
    }

    private static void ResolveAxis(ref float position, ref float velocity, float min, float max, float damping, ref bool collided)
    {
        if (position < min)
        {
            position = min;
            velocity = Mathf.Abs(velocity) * damping;
            collided = true;
        }
        else if (position > max)
        {
            position = max;
            velocity = -Mathf.Abs(velocity) * damping;
            collided = true;
        }
    }

    private static float WallPressure(Vector3 position, float topBound)
    {
        float xDistance = Mathf.Min(position.x + 0.46f, 0.46f - position.x);
        float yDistance = Mathf.Min(position.y + 0.46f, topBound - position.y);
        float zDistance = Mathf.Min(position.z + 0.46f, 0.46f - position.z);
        float nearest = Mathf.Min(xDistance, Mathf.Min(yDistance, zDistance));
        return Mathf.Clamp01(1f - nearest / 0.11f);
    }

    private Color BuildParticleColor(Color baseColor, float density, float pressure, float flash, float layer, float detail)
    {
        Color color = baseColor;
        float alpha = AlphaForCount(visibleParticleCount) * previewParticleAlphaMultiplier;

        if (showFlowLayers)
        {
            Color lower = Color.Lerp(baseColor, new Color(0.02f, 0.12f, 0.24f, 1f), 0.25f);
            Color upper = Color.Lerp(baseColor, new Color(0.42f, 0.86f, 1f, 1f), 0.28f);
            color = Color.Lerp(lower, upper, layer);
        }

        if (showDensityColors)
        {
            Color dense = Color.Lerp(baseColor, new Color(1f, 0.2f, 0.46f, 1f), Mathf.Clamp01(density * 0.75f));
            Color pressureHot = Color.Lerp(dense, Color.white, Mathf.Clamp01((pressure - 0.62f) * 1.85f));
            color = Color.Lerp(color, pressureHot, Mathf.Lerp(0.45f, 0.88f, detail));
            alpha *= Mathf.Lerp(0.72f, 1.18f, Mathf.Max(density, pressure));
        }

        if (showCollisionFlashes && flash > 0f)
        {
            color = Color.Lerp(color, Color.white, Mathf.Clamp01(flash * 0.85f));
            alpha = Mathf.Min(0.92f, alpha + flash * 0.22f);
        }

        color.a = Mathf.Clamp(alpha, 0.02f, 0.98f);
        return color;
    }

    private float ParticleSizeForCount(int count)
    {
        if (count <= 8000)
        {
            float lowDetail = Mathf.InverseLerp(25f, 8000f, count);
            return Mathf.Lerp(0.085f, 0.026f, lowDetail) * Mathf.Min(previewSize.x, previewSize.z);
        }

        float detail = Mathf.InverseLerp(8000f, 1000000f, count);
        return Mathf.Lerp(0.018f, 0.0048f, detail) * Mathf.Min(previewSize.x, previewSize.z);
    }

    private static float AlphaForCount(int count)
    {
        if (count <= 8000)
        {
            float lowDetail = Mathf.InverseLerp(25f, 8000f, count);
            return Mathf.Lerp(0.82f, 0.52f, lowDetail);
        }

        float detail = Mathf.InverseLerp(8000f, 1000000f, count);
        return Mathf.Lerp(0.30f, 0.115f, detail);
    }

    private Vector3 ScaleParticlePosition(Vector3 normalizedPosition)
    {
        return new Vector3(
            normalizedPosition.x * previewSize.x,
            normalizedPosition.y * previewSize.y,
            normalizedPosition.z * previewSize.z);
    }

    private static float Halton(int index, int radix)
    {
        float result = 0f;
        float fraction = 1f / radix;
        while (index > 0)
        {
            result += (index % radix) * fraction;
            index /= radix;
            fraction /= radix;
        }
        return result;
    }

    private static float Hash01(int value)
    {
        float hash = Mathf.Sin(value * 12.9898f) * 43758.5453f;
        return hash - Mathf.Floor(hash);
    }

    private static float HashSigned(int value)
    {
        return Hash01(value) * 2f - 1f;
    }

    private static float SafeFloat(float value, float fallback = 0f)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private static Vector3 SafeVector3(Vector3 value)
    {
        return new Vector3(SafeFloat(value.x), SafeFloat(value.y), SafeFloat(value.z));
    }

    private static Vector2 SafeVector2(Vector2 value)
    {
        return new Vector2(SafeFloat(value.x), SafeFloat(value.y));
    }

    private static float NormalizedLayer(float value)
    {
        return value * Mathf.PI * 2f;
    }

    private static float PositionPhase(float seed)
    {
        return seed * Mathf.PI * 2f;
    }

    private void UpdateText()
    {
        if (labelText != null)
        {
            labelText.transform.localPosition = new Vector3(0f, previewSize.y * 0.62f + 0.2f, -previewSize.z * 0.58f);
            labelText.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            labelText.text = "Fluid Preview";
        }

        if (statsText != null)
        {
            statsText.transform.localPosition = new Vector3(-previewSize.x * 0.5f, -previewSize.y * 0.62f - 0.22f, -previewSize.z * 0.58f);
            statsText.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            statsText.text = StatsText;
        }

        if (particleCountProofText != null)
        {
            particleCountProofText.transform.localPosition = new Vector3(0f, previewSize.y * 0.62f + 0.42f, 0f);
            particleCountProofText.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            particleCountProofText.gameObject.SetActive(showParticleCountProof);
            particleCountProofText.text = particlePreset + ": " +
                requestedParticleCount.ToString("N0") + " requested / " +
                renderedParticleCount.ToString("N0") + " rendered\n" +
                RenderModeText(activeRenderMode) + " | " + EstimatedGpuBufferMegabytes().ToString("0.0") + " MB";
        }
    }

    private void UpdateDebugLines()
    {
        SetDebugLine(bucketMotionLine, showLiquidFollowDebug, Vector3.zero, new Vector3(smoothedBucketVelocity.x, 0f, smoothedBucketVelocity.z) * 0.22f);
        SetDebugLine(bucketAccelerationLine, showLiquidFollowDebug, Vector3.up * 0.08f, new Vector3(smoothedBucketAcceleration.x, 0f, smoothedBucketAcceleration.z) * 0.045f);
        SetDebugLine(liquidResponseLine, showLiquidFollowDebug, Vector3.up * 0.16f, new Vector3(liquidResponse.x, 0f, liquidResponse.y) * 0.75f);
        SetDebugLine(previewTiltLine, showLiquidFollowDebug, Vector3.up * 0.24f, new Vector3(-liquidTilt.y, 0f, liquidTilt.x) * 0.035f);
    }

    private static void SetDebugLine(LineRenderer line, bool visible, Vector3 offset, Vector3 vector)
    {
        if (line == null)
        {
            return;
        }

        line.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        Vector3 start = new Vector3(0f, 0.54f, 0f) + offset;
        Vector3 end = start + Vector3.ClampMagnitude(vector, 1.15f);
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    private void UpdateLiquidVolume()
    {
        if (liquidVolumeTransform == null)
        {
            return;
        }

        float height = Mathf.Max(0.02f, fillPercent);
        liquidVolumeTransform.localScale = new Vector3(previewSize.x * 0.88f, previewSize.y * height * 0.94f, previewSize.z * 0.88f);
        liquidVolumeTransform.localPosition = new Vector3(0f, (-0.5f + height * 0.5f) * previewSize.y, 0f);
        liquidVolumeTransform.localRotation = IsFollowMode
            ? Quaternion.Euler(
                SafeFloat(Mathf.Clamp(liquidTilt.x * 0.45f, -8f, 8f)),
                0f,
                SafeFloat(Mathf.Clamp(liquidTilt.y * 0.45f, -8f, 8f)))
            : Quaternion.identity;
    }

    private void SanitizePreviewSettings()
    {
        previewBoxWidth = Mathf.Clamp(SafeFloat(previewBoxWidth, defaultPreviewSize.x), 0.8f, 5f);
        previewBoxHeight = Mathf.Clamp(SafeFloat(previewBoxHeight, defaultPreviewSize.y), 0.4f, 3f);
        previewBoxDepth = Mathf.Clamp(SafeFloat(previewBoxDepth, defaultPreviewSize.z), 0.5f, 3f);
        previewPositionX = SafeFloat(previewPositionX, defaultPreviewPosition.x);
        previewPositionY = SafeFloat(previewPositionY, defaultPreviewPosition.y);
        previewPositionZ = SafeFloat(previewPositionZ, defaultPreviewPosition.z);
        previewPosition = new Vector3(previewPositionX, previewPositionY, previewPositionZ);
        previewSize = new Vector3(
            previewBoxWidth,
            previewBoxHeight,
            previewBoxDepth);
        fillPercent = Mathf.Clamp01(SafeFloat(fillPercent, 0.62f));
        sloshStrength = Mathf.Max(0f, SafeFloat(sloshStrength, 0.55f));
        sloshDamping = Mathf.Max(0.1f, SafeFloat(sloshDamping, 2.2f));
        motionSpeed = Mathf.Max(0.1f, SafeFloat(motionSpeed, 1.2f));
        previewParticleSizeMultiplier = Mathf.Clamp(SafeFloat(previewParticleSizeMultiplier, 1f), 0.3f, 3f);
        previewParticleAlphaMultiplier = Mathf.Clamp(SafeFloat(previewParticleAlphaMultiplier, 1.2f), 0.2f, 3f);
        previewGlassAlpha = Mathf.Clamp(SafeFloat(previewGlassAlpha, 0.07f), 0.02f, 0.25f);
        previewLiquidSurfaceAlpha = Mathf.Clamp(SafeFloat(previewLiquidSurfaceAlpha, 0.35f), 0.02f, 0.75f);
        internalLiquidOpacity = Mathf.Clamp(SafeFloat(internalLiquidOpacity, 0.2f), 0.05f, 0.35f);
        wallOpacity = previewGlassAlpha;
        surfaceOpacity = Mathf.Clamp(previewLiquidSurfaceAlpha, 0.35f, 0.75f);
        previewMotionStrength = Mathf.Clamp(SafeFloat(previewMotionStrength, 1.6f), 0f, 4f);
        previewWaveStrength = Mathf.Clamp(SafeFloat(previewWaveStrength, 1.4f), 0f, 4f);
        previewTurbulenceStrength = Mathf.Clamp(SafeFloat(previewTurbulenceStrength, 0.35f), 0f, 2f);
        previewWallBounceStrength = Mathf.Clamp(SafeFloat(previewWallBounceStrength, 0.8f), 0f, 2f);
        previewFlowLayerStrength = Mathf.Clamp(SafeFloat(previewFlowLayerStrength, 1f), 0f, 3f);
        sloshGainX = Mathf.Clamp(SafeFloat(sloshGainX, 1f), 0.1f, 3f);
        sloshGainZ = Mathf.Clamp(SafeFloat(sloshGainZ, 1f), 0.1f, 3f);
        liquidLag = Mathf.Clamp(SafeFloat(liquidLag, 0.12f), 0.02f, 0.8f);
        liquidDamping = Mathf.Clamp(SafeFloat(liquidDamping, 2.5f), 0.2f, 8f);
        maxSurfaceTilt = Mathf.Clamp(SafeFloat(maxSurfaceTilt, 16f), 12f, 18f);
        currentViscosity = Mathf.Max(0.001f, SafeFloat(currentViscosity, 0.65f));
        bucketMotionAmount = Mathf.Clamp01(SafeFloat(bucketMotionAmount));
        followMotionTime = SafeFloat(followMotionTime);
    }

    private void RebuildPreviewPresentation(string reason, bool redistributeParticles)
    {
        EnsurePreview();
        DestroyRuntime(wallMaterial);
        DestroyRuntime(edgeMaterial);
        RebuildChildren(wallRoot, CreateWalls);
        RebuildChildren(edgeRoot, CreateEdges);
        RebuildSurfaceMesh();
        lastPresentationSize = previewSize;
        if (redistributeParticles)
        {
            RebuildParticles(force: true, reason);
        }
        UpdateTransformAndMaterials();
        UpdateSurfaceMesh();
    }

    private void RebuildChildren(Transform root, System.Action rebuildAction)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroyRuntime(root.GetChild(i).gameObject);
        }
        rebuildAction();
    }

    private string BuildStatsText()
    {
        int arrayLength = particles != null ? particles.Length : 0;
        int systemParticleCount = previewParticleSystem != null ? previewParticleSystem.particleCount : 0;
        int systemMaxParticles = previewParticleSystem != null ? previewParticleSystem.main.maxParticles : 0;
        string rebuildTime = lastRebuildTime >= 0f ? lastRebuildTime.ToString("0.00") + "s" : "-";
        string performanceNote = activeRenderMode == PreviewRenderMode.GpuPreview
            ? "GPU visual approximation avoids CPU SetParticles for large counts."
            : "CPU ParticleSystem active; per-particle flow update enabled.";
        return "Current Preset: " + particlePreset +
            "\nPreview Mode: " + (IsFollowMode ? "Follow Bucket Liquid" : "Manual Debug") +
            "\nPreview Render Mode Setting: " + previewRenderMode +
            "\nRender Mode: " + RenderModeText(activeRenderMode) +
            "\nSource: " + (IsFollowMode ? (realSourceAvailable ? "Real Bucket" : "Real Bucket Missing") : "Manual Debug") +
            "\nReal Fill Percent: " + (realFillPercent * 100f).ToString("0") + "%" +
            "\nReal Paint Color: #" + ColorUtility.ToHtmlStringRGB(realPaintColor) +
            "\nPreview Follow Model: " + previewFollowModel +
            "\nBucket Motion Amount: " + bucketMotionAmount.ToString("0.00") +
            "\nBucket Velocity: " + FormatVector3(bucketVelocity) +
            "\nBucket Acceleration: " + FormatVector3(bucketAcceleration) +
            "\nLiquid Response X/Z: " + liquidResponse.x.ToString("0.000") + ", " + liquidResponse.y.ToString("0.000") +
            "\nSurface Tilt X/Z: " + liquidTilt.x.ToString("0.00") + ", " + liquidTilt.y.ToString("0.00") +
            "\nSlosh Theta: " + sourceSloshTheta.ToString("0.000") +
            "\nSlosh Phi: " + sourceSloshPhi.ToString("0.000") +
            "\nSwap Axes: " + (swapSloshAxes ? "On" : "Off") +
            "\nInvert X: " + (invertSloshX ? "On" : "Off") +
            "\nInvert Z: " + (invertSloshZ ? "On" : "Off") +
            "\nLiquid Lag: " + liquidLag.ToString("0.00") +
            "\nLiquid Damping: " + liquidDamping.ToString("0.00") +
            "\nViscosity: " + currentViscosity.ToString("0.00") +
            "\nMotion Strength: " + previewMotionStrength.ToString("0.00") +
            "\nWave Strength: " + previewWaveStrength.ToString("0.00") +
            "\nShow Internal Particles: " + (showInternalParticles ? "On" : "Off") +
            "\nRequested Particle Count: " + previewParticleCount.ToString("N0") +
            "\nRendered / Visible Particle Count: " + renderedParticleCount.ToString("N0") +
            "\nInternal Particle Data Length: " + (activeRenderMode == PreviewRenderMode.CpuParticleSystem ? arrayLength.ToString("N0") : "N/A") +
            "\nParticleSystem Max Particles: " + (activeRenderMode == PreviewRenderMode.CpuParticleSystem ? systemMaxParticles.ToString("N0") : "N/A") +
            "\nParticleSystem Current Particle Count: " + (activeRenderMode == PreviewRenderMode.CpuParticleSystem ? systemParticleCount.ToString("N0") : "N/A") +
            "\nGPU Buffer Count: " + (activeRenderMode == PreviewRenderMode.GpuPreview ? gpuBufferCount.ToString("N0") : "N/A") +
            "\nEstimated GPU Buffer Memory: " + EstimatedGpuBufferMegabytes().ToString("0.0") + " MB" +
            "\nLast Rebuild Reason: " + lastRebuildReason +
            "\nLast Rebuild Time: " + rebuildTime +
            "\nFPS / Performance Note: " + performanceNote +
            "\nVisual Density: " + VisualDensityText +
            "\nVisual Density Strength: " + visualDensityStrength.ToString("0.00") +
            "\nWall Collision Count: " + wallCollisionCount.ToString("N0") +
            "\nWall Collisions / Frame: " + wallCollisionsThisFrame.ToString("N0") +
            "\nInternal Collision Estimate: " + internalCollisionEstimate.ToString("N0") +
            "\nAverage Density: " + averageDensity.ToString("0.00") +
            "\nAverage Pressure: " + averagePressure.ToString("0.00") +
            "\nMotion State: " + MotionState +
            "\nFollow Source: " + (IsFollowMode ? "Bucket Liquid" : "Manual Debug") +
            "\nDensity Colors: " + (showDensityColors ? "On" : "Off") +
            "\nCollision Flashes: " + (showCollisionFlashes ? "On" : "Off") +
            "\nFlow Layers: " + (showFlowLayers ? "On" : "Off") +
            "\nShow Particle Count Proof: " + (showParticleCountProof ? "On" : "Off") +
            "\nMotion Time: " + lastMotionTime.ToString("0.00") +
            "\nQuality: " + previewQuality +
            "\nDensity Mode: " + (IsFollowMode ? "Auto Follow" : DensityModeText) +
            "\nPreview Fill Percent: " + (fillPercent * 100f).ToString("0") + "%" +
            "\nPreview Motion State: " + MotionState +
            (string.IsNullOrEmpty(warningText) ? "" : "\n" + warningText);
    }

    private static string RenderModeText(PreviewRenderMode mode)
    {
        return mode == PreviewRenderMode.GpuPreview ? "GPU Preview" : "CPU ParticleSystem";
    }

    private static string FormatVector3(Vector3 value)
    {
        return value.x.ToString("0.00") + ", " + value.y.ToString("0.00") + ", " + value.z.ToString("0.00");
    }

    private float EstimatedGpuBufferMegabytes()
    {
        if (activeRenderMode != PreviewRenderMode.GpuPreview)
        {
            return 0f;
        }

        float particleDataBytes = gpuBufferCount * sizeof(float) * 4f;
        float argsBytes = sizeof(uint) * 5f;
        return (particleDataBytes + argsBytes) / (1024f * 1024f);
    }

    private void ApplyVisibility()
    {
        if (visualRoot != null && visualRoot.gameObject.activeSelf != showPreview)
        {
            visualRoot.gameObject.SetActive(showPreview);
        }
    }

    private static Material CreateTransparentMaterial(string materialName, Color color, float smoothness, int renderQueue, bool preferUnlit)
    {
        Shader shader = preferUnlit ? Shader.Find("Universal Render Pipeline/Unlit") : Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find(preferUnlit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.name = materialName;
        SetMaterialColor(material, color);
        SetMaterialFloat(material, "_Surface", 1f);
        SetMaterialFloat(material, "_Blend", 0f);
        SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetMaterialFloat(material, "_ZWrite", 0f);
        SetMaterialFloat(material, "_AlphaClip", 0f);
        SetMaterialFloat(material, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetOverrideTag("Queue", "Transparent");
        material.renderQueue = renderQueue;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        SetMaterialFloat(material, "_Smoothness", smoothness);
        SetMaterialFloat(material, "_Metallic", 0f);
        return material;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        material.color = color;
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
        ReleaseGpuPreviewBuffers();
        DestroyRuntime(surfaceMesh);
        DestroyRuntime(gpuParticleMesh);
        DestroyRuntime(wallMaterial);
        DestroyRuntime(edgeMaterial);
        DestroyRuntime(liquidVolumeMaterial);
        DestroyRuntime(surfaceMaterial);
        DestroyRuntime(particleMaterial);
        DestroyRuntime(gpuParticleMaterial);
        DestroyRuntime(debugLineMaterial);
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
}
