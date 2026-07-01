using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public class WorldPaintRenderer : MonoBehaviour
{
    private struct PaintMark
    {
        public Vector3 a;
        public Vector3 b;
        public Color color;
        public float radius;
        public bool segment;
    }

    private struct StreamState
    {
        public Vector3 position;
        public Vector3 direction;
        public Color color;
        public float time;
        public bool valid;
    }

    public int MarkCount => marks.Count;
    public int ConnectedStrokeCount { get; private set; }
    public int RejectedConnectionCount { get; private set; }
    public float EstimatedPaintedArea01 { get; private set; }
    public int PaintObjectCount => paintObjects.Count;
    public Vector3 LastPaintObjectPosition { get; private set; }
    public float LastPaintRadius { get; private set; }
    public float LastPaintAlpha { get; private set; }
    public Color LastPaintMaterialColor { get; private set; } = Color.clear;
    public int LastPaintRenderQueue { get; private set; }
    public Vector3 LastVisibleNormal { get; private set; } = Vector3.up;
    public string LastPaintObjectName { get; private set; } = "None";
    public bool LastRendererEnabled { get; private set; }
    public string LastMaterialName { get; private set; } = "None";
    public string LastDiagnostic { get; private set; } = "No world paint objects created yet.";

    [Range(0.001f, 0.08f)] public float paintSurfaceOffset = 0.02f;
    public bool invertPaintNormal;
    public bool alwaysVisibleDebug;
    public bool worldPaintFallbackGeometry;

    private const int CircleSegments = 18;
    private readonly List<GameObject> paintObjects = new List<GameObject>();
    private readonly List<PaintMark> marks = new List<PaintMark>();
    private readonly Dictionary<int, StreamState> streams = new Dictionary<int, StreamState>();
    private Material paintMaterial;
    private Mesh diskMesh;
    private Mesh fallbackMesh;

    public void Clear()
    {
        for (int i = 0; i < paintObjects.Count; i++)
        {
            if (paintObjects[i] == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(paintObjects[i]);
            }
            else
            {
                DestroyImmediate(paintObjects[i]);
            }
        }

        paintObjects.Clear();
        marks.Clear();
        streams.Clear();
        ConnectedStrokeCount = 0;
        RejectedConnectionCount = 0;
        EstimatedPaintedArea01 = 0f;
        LastPaintObjectPosition = Vector3.zero;
        LastPaintRadius = 0f;
        LastPaintAlpha = 0f;
        LastPaintMaterialColor = Color.clear;
        LastPaintRenderQueue = 0;
        LastPaintObjectName = "None";
        LastRendererEnabled = false;
        LastMaterialName = "None";
        LastDiagnostic = "World paint cleared.";
    }

    public void DrawHit(PaintingSurface surface, Vector3 worldHit, Color color, PaintingSurface.TrailRenderMode mode, float strokeRadius, int streamId)
    {
        if (surface == null)
        {
            return;
        }

        EnsureResources();

        float width = Mathf.Clamp(strokeRadius, 0.003f, 0.2f);
        if (mode == PaintingSurface.TrailRenderMode.Dots)
        {
            DrawDot(surface, worldHit, color, width);
            AddMark(surface, worldHit, worldHit, color, width, false);
            streams[streamId] = NewStreamState(worldHit, surface.BoardRightAxis, color);
            return;
        }

        float modeWidth = width;
        if (TryConnect(surface, worldHit, mode, streamId, out StreamState previous))
        {
            Vector3 direction = worldHit - previous.position;
            if (mode == PaintingSurface.TrailRenderMode.Ribbon && previous.direction.sqrMagnitude > 0.0001f)
            {
                direction = Vector3.Lerp(direction.normalized, previous.direction.normalized, Mathf.Clamp01(surface.strokeSmoothing)).normalized;
            }

            DrawStrip(surface, previous.position, worldHit, color, modeWidth);
            AddMark(surface, previous.position, worldHit, color, modeWidth, true);
            ConnectedStrokeCount++;
            streams[streamId] = NewStreamState(worldHit, direction, color);
        }
        else
        {
            DrawDot(surface, worldHit, color, modeWidth);
            AddMark(surface, worldHit, worldHit, color, modeWidth, false);
            RejectedConnectionCount++;
            streams[streamId] = NewStreamState(worldHit, surface.BoardRightAxis, color);
        }
    }

    public string SavePng(PaintingSurface surface, string directory, string filePrefix, int width, int height, Color clearColor)
    {
        Directory.CreateDirectory(directory);
        Texture2D output = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = clearColor;
        }

        for (int i = 0; i < marks.Count; i++)
        {
            PaintMark mark = marks[i];
            if (mark.segment)
            {
                RasterizeSegment(surface, pixels, width, height, mark);
            }
            else
            {
                RasterizeDot(surface, pixels, width, height, mark.a, mark.color, mark.radius);
            }
        }

        output.SetPixels(pixels);
        output.Apply();
        string safePrefix = string.IsNullOrWhiteSpace(filePrefix) ? "painting" : filePrefix;
        string path = Path.Combine(directory, safePrefix + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        File.WriteAllBytes(path, output.EncodeToPNG());

        if (Application.isPlaying)
        {
            Destroy(output);
        }
        else
        {
            DestroyImmediate(output);
        }

        return path;
    }

    private bool TryConnect(PaintingSurface surface, Vector3 worldHit, PaintingSurface.TrailRenderMode mode, int streamId, out StreamState previous)
    {
        if (!streams.TryGetValue(streamId, out previous) || !previous.valid)
        {
            return false;
        }

        float timeGap = Time.unscaledTime - previous.time;
        float minBoardSize = Mathf.Max(0.001f, Mathf.Min(surface.currentWidth, surface.currentHeight));
        float maxDistance = surface.connectDistanceThreshold * minBoardSize;
        if (mode == PaintingSurface.TrailRenderMode.Ribbon)
        {
            maxDistance *= 1.5f;
        }

        return timeGap <= surface.maxStrokeTimeGap && Vector3.Distance(previous.position, worldHit) <= maxDistance;
    }

    private StreamState NewStreamState(Vector3 position, Vector3 direction, Color color)
    {
        return new StreamState
        {
            position = position,
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right,
            color = color,
            time = Time.unscaledTime,
            valid = true
        };
    }

    private void DrawDot(PaintingSurface surface, Vector3 worldHit, Color color, float radius)
    {
        Vector3 visibleNormal = GetVisibleBoardNormal(surface);
        GameObject dot = new GameObject("WorldPaintDot");
        dot.transform.SetParent(surface.transform, true);
        dot.transform.position = worldHit + visibleNormal * paintSurfaceOffset;
        dot.transform.rotation = GetPaintRotation(surface, visibleNormal);
        dot.transform.localScale = Vector3.one * radius * 2f;

        MeshFilter filter = dot.AddComponent<MeshFilter>();
        MeshRenderer renderer = dot.AddComponent<MeshRenderer>();
        filter.sharedMesh = diskMesh;
        renderer.sharedMaterial = CreateMaterialInstance(color);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        paintObjects.Add(dot);
        RecordDiagnostics(dot, renderer, color, radius, surface);
    }

    private void DrawStrip(PaintingSurface surface, Vector3 start, Vector3 end, Color color, float width)
    {
        Vector3 normal = GetVisibleBoardNormal(surface);
        Vector3 direction = end - start;
        if (direction.sqrMagnitude <= 0.000001f)
        {
            DrawDot(surface, end, color, width);
            return;
        }

        Vector3 side = Vector3.Cross(normal, direction).normalized * width * 0.5f;
        Vector3 offset = normal * paintSurfaceOffset;
        Vector3 v0 = start - side + offset;
        Vector3 v1 = start + side + offset;
        Vector3 v2 = end - side + offset;
        Vector3 v3 = end + side + offset;
        Mesh mesh = new Mesh();
        mesh.name = "WorldPaintStripMesh";
        mesh.vertices = new[]
        {
            surface.transform.InverseTransformPoint(v0),
            surface.transform.InverseTransformPoint(v1),
            surface.transform.InverseTransformPoint(v2),
            surface.transform.InverseTransformPoint(v3)
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateBounds();

        GameObject strip = new GameObject("WorldPaintStroke");
        strip.transform.SetParent(surface.transform, false);
        strip.transform.localPosition = Vector3.zero;
        strip.transform.localRotation = Quaternion.identity;
        strip.transform.localScale = Vector3.one;
        MeshFilter filter = strip.AddComponent<MeshFilter>();
        MeshRenderer renderer = strip.AddComponent<MeshRenderer>();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = CreateMaterialInstance(color);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        paintObjects.Add(strip);
        RecordDiagnostics(strip, renderer, color, width, surface);
    }

    private void AddMark(PaintingSurface surface, Vector3 a, Vector3 b, Color color, float radius, bool segment)
    {
        color.a = Mathf.Clamp(color.a <= 0f ? 1f : color.a, 0.65f, 1f);
        marks.Add(new PaintMark { a = a, b = b, color = color, radius = radius, segment = segment });
        float boardArea = Mathf.Max(0.001f, surface.currentWidth * surface.currentHeight);
        float markArea = segment
            ? Vector3.Distance(a, b) * radius + Mathf.PI * radius * radius
            : Mathf.PI * radius * radius;
        EstimatedPaintedArea01 = Mathf.Clamp01(EstimatedPaintedArea01 + markArea / boardArea);
    }

    private void EnsureResources()
    {
        if (paintMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            paintMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
            paintMaterial.name = "World Paint Material";
            paintMaterial.renderQueue = alwaysVisibleDebug ? 5000 : 3500;
            SetBlendMode(paintMaterial);
        }

        if (diskMesh == null)
        {
            diskMesh = BuildDiskMesh();
        }

        if (fallbackMesh == null)
        {
            fallbackMesh = BuildFallbackMesh();
        }
    }

    private Material CreateMaterialInstance(Color color)
    {
        Material material = new Material(paintMaterial);
        color.a = Mathf.Clamp(color.a <= 0f ? 1f : color.a, 0.65f, 1f);

        material.name = alwaysVisibleDebug ? "World Paint Material Debug Always Visible" : "World Paint Material";
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);

        material.renderQueue = alwaysVisibleDebug ? 5000 : 3500;
        material.SetOverrideTag("RenderType", "Transparent");

        SetBlendMode(material);
        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", alwaysVisibleDebug ? (int)CompareFunction.Always : (int)CompareFunction.LessEqual);
        }

        return material;
    }

    private Vector3 GetVisibleBoardNormal(PaintingSurface surface)
    {
        Vector3 normal = surface.BoardNormal.sqrMagnitude > 0.0001f ? surface.BoardNormal.normalized : Vector3.up;
        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 toCamera = camera.transform.position - surface.BoardCenter;
            if (Vector3.Dot(normal, toCamera) < 0f)
            {
                normal = -normal;
            }
        }

        if (invertPaintNormal)
        {
            normal = -normal;
        }

        LastVisibleNormal = normal;
        return normal;
    }

    private Quaternion GetPaintRotation(PaintingSurface surface, Vector3 visibleNormal)
    {
        Vector3 up = surface.BoardUpAxis.sqrMagnitude > 0.0001f ? surface.BoardUpAxis.normalized : Vector3.up;
        if (Mathf.Abs(Vector3.Dot(up, visibleNormal)) > 0.98f)
        {
            up = surface.BoardRightAxis.sqrMagnitude > 0.0001f ? surface.BoardRightAxis.normalized : Vector3.right;
        }

        return Quaternion.LookRotation(visibleNormal, up);
    }

    private void RecordDiagnostics(GameObject paintObject, Renderer renderer, Color color, float radius, PaintingSurface surface)
    {
        LastPaintObjectPosition = paintObject != null ? paintObject.transform.position : Vector3.zero;
        LastPaintRadius = radius;
        LastPaintAlpha = Mathf.Clamp(color.a <= 0f ? 1f : color.a, 0.65f, 1f);
        color.a = LastPaintAlpha;
        LastPaintMaterialColor = color;
        LastPaintRenderQueue = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : 0;
        LastPaintObjectName = paintObject != null ? paintObject.name : "None";
        LastRendererEnabled = renderer != null && renderer.enabled;
        LastMaterialName = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "None";
        LastDiagnostic =
            "paint object created: " + (paintObject != null ? "yes" : "no") +
            "\nobject name: " + LastPaintObjectName +
            "\nrenderer enabled: " + (LastRendererEnabled ? "yes" : "no") +
            "\nmaterial name: " + LastMaterialName +
            "\nmaterial color: " + FormatColor(LastPaintMaterialColor) +
            "\nalpha: " + LastPaintAlpha.ToString("0.000") +
            "\nrenderQueue: " + LastPaintRenderQueue +
            "\nworld position: " + FormatVector3(LastPaintObjectPosition) +
            "\nscale: " + (paintObject != null ? FormatVector3(paintObject.transform.lossyScale) : "0.000, 0.000, 0.000") +
            "\nvisible normal: " + FormatVector3(LastVisibleNormal) +
            "\nsurface offset: " + paintSurfaceOffset.ToString("0.000") +
            "\npaint object count: " + PaintObjectCount;

        if (worldPaintFallbackGeometry && paintObject != null)
        {
            CreateFallbackGeometry(surface, paintObject.transform.position, color, radius);
        }
    }

    private void CreateFallbackGeometry(PaintingSurface surface, Vector3 position, Color color, float radius)
    {
        GameObject fallback = new GameObject("WorldPaintFallbackGeometry");
        fallback.transform.SetParent(surface != null ? surface.transform : transform, true);
        fallback.transform.position = position;
        fallback.transform.rotation = Quaternion.identity;
        fallback.transform.localScale = Vector3.one * Mathf.Max(0.01f, radius * 1.6f);
        MeshFilter filter = fallback.AddComponent<MeshFilter>();
        MeshRenderer renderer = fallback.AddComponent<MeshRenderer>();
        filter.sharedMesh = fallbackMesh;
        Color opaqueColor = color;
        opaqueColor.a = 1f;
        renderer.sharedMaterial = CreateMaterialInstance(opaqueColor);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        paintObjects.Add(fallback);
    }

    private static string FormatVector3(Vector3 value)
    {
        return value.x.ToString("0.000") + ", " + value.y.ToString("0.000") + ", " + value.z.ToString("0.000");
    }

    private static string FormatColor(Color color)
    {
        return color.r.ToString("0.000") + ", " + color.g.ToString("0.000") + ", " + color.b.ToString("0.000") + ", " + color.a.ToString("0.000");
    }
    private static void SetBlendMode(Material material)
    {
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
    }

    private static Mesh BuildDiskMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "WorldPaintDiskMesh";
        Vector3[] vertices = new Vector3[CircleSegments + 1];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[CircleSegments * 3];
        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i / (float)CircleSegments * Mathf.PI * 2f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f, 0f);
            uvs[i + 1] = new Vector2(vertices[i + 1].x + 0.5f, vertices[i + 1].y + 0.5f);
            int tri = i * 3;
            triangles[tri] = 0;
            triangles[tri + 1] = i + 1;
            triangles[tri + 2] = i == CircleSegments - 1 ? 1 : i + 2;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh BuildFallbackMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "WorldPaintFallbackOctahedron";
        mesh.vertices = new[]
        {
            new Vector3(0f, 0.6f, 0f),
            new Vector3(0.6f, 0f, 0f),
            new Vector3(0f, 0f, 0.6f),
            new Vector3(-0.6f, 0f, 0f),
            new Vector3(0f, 0f, -0.6f),
            new Vector3(0f, -0.6f, 0f)
        };
        mesh.triangles = new[]
        {
            0, 1, 2,
            0, 2, 3,
            0, 3, 4,
            0, 4, 1,
            5, 2, 1,
            5, 3, 2,
            5, 4, 3,
            5, 1, 4
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void RasterizeDot(PaintingSurface surface, Color[] pixels, int width, int height, Vector3 world, Color color, float radius)
    {
        Vector2 uv = surface.WorldToUv(world);
        Vector2 uvRadius = surface.WorldToUv(world + surface.BoardRightAxis * radius);
        float radiusPixels = Mathf.Max(1f, Mathf.Abs(uvRadius.x - uv.x) * width);
        int cx = Mathf.RoundToInt(uv.x * (width - 1));
        int cy = Mathf.RoundToInt(uv.y * (height - 1));
        int r = Mathf.CeilToInt(radiusPixels);

        for (int y = cy - r; y <= cy + r; y++)
        {
            if (y < 0 || y >= height) continue;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= width) continue;
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                if (distance > radiusPixels) continue;
                float alpha = Mathf.Clamp01(1f - distance / Mathf.Max(0.001f, radiusPixels));
                BlendPixel(pixels, width, x, y, color, Mathf.Lerp(color.a, color.a * 0.35f, 1f - alpha));
            }
        }
    }

    private static void RasterizeSegment(PaintingSurface surface, Color[] pixels, int width, int height, PaintMark mark)
    {
        Vector2 a = surface.WorldToUv(mark.a);
        Vector2 b = surface.WorldToUv(mark.b);
        Vector2 ap = new Vector2(a.x * (width - 1), a.y * (height - 1));
        Vector2 bp = new Vector2(b.x * (width - 1), b.y * (height - 1));
        Vector2 uvRadius = surface.WorldToUv(mark.a + surface.BoardRightAxis * mark.radius);
        float radiusPixels = Mathf.Max(1f, Mathf.Abs(uvRadius.x - a.x) * width);
        int minX = Mathf.FloorToInt(Mathf.Min(ap.x, bp.x) - radiusPixels);
        int maxX = Mathf.CeilToInt(Mathf.Max(ap.x, bp.x) + radiusPixels);
        int minY = Mathf.FloorToInt(Mathf.Min(ap.y, bp.y) - radiusPixels);
        int maxY = Mathf.CeilToInt(Mathf.Max(ap.y, bp.y) + radiusPixels);

        for (int y = minY; y <= maxY; y++)
        {
            if (y < 0 || y >= height) continue;
            for (int x = minX; x <= maxX; x++)
            {
                if (x < 0 || x >= width) continue;
                float distance = DistancePointToSegment(new Vector2(x, y), ap, bp);
                if (distance > radiusPixels) continue;
                float edge = Mathf.Clamp01(1f - distance / Mathf.Max(0.001f, radiusPixels));
                BlendPixel(pixels, width, x, y, mark.color, Mathf.Lerp(mark.color.a, mark.color.a * 0.4f, 1f - edge));
            }
        }
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0.000001f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(point, a + ab * t);
    }

    private static void BlendPixel(Color[] pixels, int width, int x, int y, Color color, float alpha)
    {
        int index = y * width + x;
        color.a = Mathf.Clamp01(alpha);
        pixels[index] = Color.Lerp(pixels[index], color, color.a);
        pixels[index].a = 1f;
    }

    private void OnDestroy()
    {
        Clear();
        if (Application.isPlaying)
        {
            if (diskMesh != null) Destroy(diskMesh);
            if (fallbackMesh != null) Destroy(fallbackMesh);
            if (paintMaterial != null) Destroy(paintMaterial);
        }
        else
        {
            if (diskMesh != null) DestroyImmediate(diskMesh);
            if (fallbackMesh != null) DestroyImmediate(fallbackMesh);
            if (paintMaterial != null) DestroyImmediate(paintMaterial);
        }
    }
}
