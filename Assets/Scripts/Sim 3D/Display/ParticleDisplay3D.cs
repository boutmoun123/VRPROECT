using UnityEngine;

public class ParticleDisplay3D : MonoBehaviour
{
    [Header("Presentation")]
    public bool showParticles = false;

    public Shader shader;
    public float scale = 0.035f;
    public Color col = Color.blue;

    private Mesh mesh;
    private Material mat;

    private ComputeBuffer argsBuffer;
    private Bounds bounds;

    public Gradient colourMap;
    public int gradientResolution = 128;
    public float velocityDisplayMax = 5f;
    private Texture2D gradientTexture;
    private bool needsUpdate = true;

    public int meshResolution = 1;
    public int debug_MeshTriCount;

    private bool initialized = false;

    public void Init(Simulation3D sim)
    {
        if (sim == null)
        {
            Debug.LogError("ParticleDisplay3D Init failed: Simulation3D is null.");
            return;
        }

        if (shader == null)
        {
            Debug.LogError("ParticleDisplay3D Init failed: Shader is not assigned.");
            return;
        }

        if (mat != null)
        {
            if (Application.isPlaying)
            {
                Destroy(mat);
            }
            else
            {
                DestroyImmediate(mat);
            }
        }

        if (mesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }

        mat = new Material(shader);
        mat.enableInstancing = true;

        mat.SetBuffer("Positions", sim.positionBuffer);
        mat.SetBuffer("Velocities", sim.velocityBuffer);

        mesh = SebStuff.SphereGenerator.GenerateSphereMesh(meshResolution);
        debug_MeshTriCount = mesh.triangles.Length / 3;

        ComputeHelper.Release(argsBuffer);
        argsBuffer = ComputeHelper.CreateArgsBuffer(mesh, sim.positionBuffer.count);

        bounds = new Bounds(Vector3.zero, Vector3.one * 10000);

        initialized = true;
        needsUpdate = true;
    }

    private void LateUpdate()
    {
        if (!showParticles || !initialized || mat == null || mesh == null || argsBuffer == null)
        {
            return;
        }

        UpdateSettings();

        Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, bounds, argsBuffer);
    }

    private void UpdateSettings()
    {
        if (mat == null)
        {
            return;
        }

        if (needsUpdate)
        {
            needsUpdate = false;
            TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
            mat.SetTexture("ColourMap", gradientTexture);
        }

        mat.SetFloat("scale", scale);
        mat.SetColor("colour", col);
        mat.SetFloat("velocityMax", velocityDisplayMax);

        Vector3 s = transform.localScale;
        transform.localScale = Vector3.one;
        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        transform.localScale = s;

        mat.SetMatrix("localToWorld", localToWorld);
    }

    public static void TextureFromGradient(
        ref Texture2D texture,
        int width,
        Gradient gradient,
        FilterMode filterMode = FilterMode.Bilinear
    )
    {
        if (width <= 0)
        {
            width = 128;
        }

        if (texture == null)
        {
            texture = new Texture2D(width, 1);
        }
        else if (texture.width != width)
        {
            texture.Reinitialize(width, 1);
        }

        if (gradient == null)
        {
            gradient = new Gradient();

            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.blue, 0f),
                    new GradientColorKey(Color.red, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = filterMode;

        Color[] colors = new Color[width];

        for (int i = 0; i < colors.Length; i++)
        {
            float t = i / (colors.Length - 1f);
            colors[i] = gradient.Evaluate(t);
        }

        texture.SetPixels(colors);
        texture.Apply();
    }

    private void OnValidate()
    {
        needsUpdate = true;
    }

    private void OnDestroy()
    {
        ComputeHelper.Release(argsBuffer);

        if (mat != null)
        {
            if (Application.isPlaying)
            {
                Destroy(mat);
            }
            else
            {
                DestroyImmediate(mat);
            }

            mat = null;
        }

        if (gradientTexture != null)
        {
            if (Application.isPlaying)
            {
                Destroy(gradientTexture);
            }
            else
            {
                DestroyImmediate(gradientTexture);
            }

            gradientTexture = null;
        }

        if (mesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }

            mesh = null;
        }
    }
}
