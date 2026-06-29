using Unity.Mathematics;
using UnityEngine;

public class Spawner3D : MonoBehaviour
{
    public int numParticlesPerAxis;
    public bool useTargetParticleCount = true;
    public int targetParticleCount = 8000;
    public Vector3 centre;
    public float size;
    public float3 initialVel;
    public float jitterStrength;
    public bool showSpawnBounds;

    [Header("Info")]
    public int debug_numParticles;

    public SpawnData GetSpawnData()
    {
        ValidateSettings();

        int numPoints = useTargetParticleCount
            ? targetParticleCount
            : numParticlesPerAxis * numParticlesPerAxis * numParticlesPerAxis;

        float3[] points = new float3[numPoints];
        float3[] velocities = new float3[numPoints];
        int gridResolution = useTargetParticleCount
            ? Mathf.Max(2, Mathf.CeilToInt(Mathf.Pow(numPoints, 1f / 3f)))
            : numParticlesPerAxis;

        int i = 0;

        for (int x = 0; x < gridResolution && i < numPoints; x++)
        {
            for (int y = 0; y < gridResolution && i < numPoints; y++)
            {
                for (int z = 0; z < gridResolution && i < numPoints; z++)
                {
                    float tx = x / (gridResolution - 1f);
                    float ty = y / (gridResolution - 1f);
                    float tz = z / (gridResolution - 1f);

                    float px = (tx - 0.5f) * size + centre.x;
                    float py = (ty - 0.5f) * size + centre.y;
                    float pz = (tz - 0.5f) * size + centre.z;
                    float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
                    points[i] = new float3(px, py, pz) + jitter;
                    velocities[i] = initialVel;
                    i++;
                }
            }
        }

        return new SpawnData() { points = points, velocities = velocities };
    }

    public struct SpawnData
    {
        public float3[] points;
        public float3[] velocities;
    }

    void OnValidate()
    {
        ValidateSettings();
    }

    private void ValidateSettings()
    {
        numParticlesPerAxis = Mathf.Clamp(numParticlesPerAxis, 2, 64);
        targetParticleCount = Mathf.Clamp(targetParticleCount, 2, 1000000);
        size = Mathf.Max(0.001f, size);
        jitterStrength = Mathf.Max(0f, jitterStrength);
        debug_numParticles = useTargetParticleCount
            ? targetParticleCount
            : numParticlesPerAxis * numParticlesPerAxis * numParticlesPerAxis;
    }

    void OnDrawGizmos()
    {
        if (showSpawnBounds && !Application.isPlaying)
        {
            Gizmos.color = new Color(1, 1, 0, 0.5f);
            Gizmos.DrawWireCube(centre, Vector3.one * size);
        }
    }
}
