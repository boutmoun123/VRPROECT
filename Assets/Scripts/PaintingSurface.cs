using System.IO;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class PaintingSurface : MonoBehaviour
{
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
    private bool hasBaseTransform;
    private Vector3 baseLocalPosition;

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
                position.y = Mathf.Max(position.y, 0.35f);
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
        ApplySurfaceMaterial();

        MarkCount = 0;
        paintedPixelCount = 0;
        paintedPixels = new bool[textureWidth * textureHeight];
        wetness = new float[textureWidth * textureHeight];
        paintPixels = pixels;
        totalWetness = 0f;
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
        float exitSpeed
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

        float u = Mathf.InverseLerp(-localHalfExtents.x, localHalfExtents.x, hitLocal.x);
        float v = Mathf.InverseLerp(-localHalfExtents.y, localHalfExtents.y, hitLocal.z);
        float safeViscosity = Mathf.Max(0.001f, viscosity);
        float viscositySpread = 1f / (1f + safeViscosity * 0.85f);
        float velocitySpread = Mathf.Clamp01(velocityMagnitude / Mathf.Max(0.001f, exitSpeed + 4f));
        float holeSpread = Mathf.Clamp(holeDiameter / 0.08f, 0.5f, 2.2f);
        float radius01 = Mathf.Max(radius, splatRadius);
        float spreadFactor = (0.65f + viscositySpread * spreadStrength) * (1f + velocitySpread * 0.35f) * Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(0.5f, 2.2f, holeSpread));
        float impactOpacity = Mathf.Clamp01(alpha * splatOpacity * Mathf.Lerp(1.15f, 0.75f, viscositySpread));
        float impactAmount = Mathf.Clamp(accumulationStrength * Mathf.Lerp(0.75f, 1.25f, velocitySpread) * Mathf.Lerp(0.8f, 1.2f, holeSpread), 0.01f, 2f);

        lastViscosityEffect = viscositySpread;
        lastAppliedSplatRadius = radius01 * spreadFactor;
        PaintAtUV(new Vector2(u, v), color, lastAppliedSplatRadius, impactOpacity, impactAmount);
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

    private void LateUpdate()
    {
        ApplyDrying();
        ApplyIfDirty();
    }

    private void PaintAtUV(Vector2 uv, Color color, float radius01, float alpha, float impactAmount)
    {
        EnsurePaintBuffers();

        int centerX = Mathf.RoundToInt(uv.x * (textureWidth - 1));
        int centerY = Mathf.RoundToInt(uv.y * (textureHeight - 1));
        int pixelRadius = Mathf.Max(1, Mathf.RoundToInt(radius01 * Mathf.Min(textureWidth, textureHeight)));
        int minX = Mathf.Max(0, centerX - pixelRadius);
        int maxX = Mathf.Min(textureWidth - 1, centerX + pixelRadius);
        int minY = Mathf.Max(0, centerY - pixelRadius);
        int maxY = Mathf.Min(textureHeight - 1, centerY + pixelRadius);

        Color source = color;
        source.a = Mathf.Clamp01(alpha);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = (x - centerX) / (float)pixelRadius;
                float dy = (y - centerY) / (float)pixelRadius;
                float distance01 = Mathf.Sqrt(dx * dx + dy * dy);

                if (distance01 > 1f)
                {
                    continue;
                }

                float falloff = Mathf.SmoothStep(0f, 1f, 1f - distance01);
                falloff *= falloff;

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

        MarkCount++;
        textureDirty = true;
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
