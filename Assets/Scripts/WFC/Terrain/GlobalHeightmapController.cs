// GlobalHeightmapController.cs - Add this as a new script
using UnityEngine;
using WFC.Core;
using WFC.Configuration;

namespace WFC.Terrain
{
    public class GlobalHeightmapController : MonoBehaviour
    {
        [Header("Heightmap Settings")]
        [SerializeField] private int resolution = 4; // Points per chunk
        [SerializeField] private float baseHeight = 32f;
        [SerializeField] private float mountainHeight = 96f;

        [Header("Noise Settings")]
        [SerializeField] private float baseNoiseScale = 0.02f;
        [SerializeField] private float detailNoiseScale = 0.08f;
        [SerializeField] private float mountainNoiseScale = 0.015f;
        [SerializeField] private float valleyDepth = 0.3f;
        [SerializeField] private bool visualizeHeightmap = false;

        private float[,] globalHeightmap;
        private int worldSizeX, worldSizeZ;
        private int seed;
        private int chunkSize;
        private bool isInitialized = false;

        public bool IsInitialized => isInitialized;

        private void Awake()
        {
            // Get configuration from WFCConfigManager
            var config = WFCConfigManager.Config;
            if (config != null)
            {
                worldSizeX = config.World.worldSizeX;
                worldSizeZ = config.World.worldSizeZ;
                chunkSize = config.World.chunkSize;
                seed = config.World.randomSeed;
            }
            else
            {
                // Default values if config not available
                worldSizeX = 32;
                worldSizeZ = 32;
                chunkSize = 32;
                seed = 42;
            }
        }

        public void Initialize()
        {
            if (isInitialized) return;

            // Calculate heightmap size based on world size and resolution
            int hmSizeX = worldSizeX * resolution;
            int hmSizeZ = worldSizeZ * resolution;
            globalHeightmap = new float[hmSizeX, hmSizeZ];

            // Generate the heightmap
            GenerateHeightmap(seed);

            isInitialized = true;
            Debug.Log($"GlobalHeightmapController initialized: {hmSizeX}x{hmSizeZ} resolution");

            if (visualizeHeightmap)
                VisualizeHeightmap();
        }

        private void GenerateHeightmap(int seed)
        {
            // CRITICAL: Use a more efficient approach
            System.Random random = new System.Random(seed);
            float randOffset = (float)random.NextDouble() * 100f;

            // Calculate heightmap size
            int hmSizeX = globalHeightmap.GetLength(0);
            int hmSizeZ = globalHeightmap.GetLength(1);

            // CRITICAL: Use single-pass approach instead of multiple arrays
            for (int x = 0; x < hmSizeX; x++)
            {
                for (int z = 0; z < hmSizeZ; z++)
                {
                    // Calculate normalized coordinates (0-1)
                    float nx = x / (float)hmSizeX;
                    float nz = z / (float)hmSizeZ;

                    // Valley factor - affects height distribution
                    float distFromCenter = Vector2.Distance(new Vector2(nx, nz), new Vector2(0.5f, 0.5f));
                    float valleyFactor = Mathf.Clamp01(distFromCenter * 2.5f);

                    // Base noise (large features)
                    float baseNoise = Mathf.PerlinNoise(
                        x * baseNoiseScale + randOffset,
                        z * baseNoiseScale + randOffset
                    );

                    // Mountain noise (apply power curve for sharper peaks)
                    float mountainNoise = Mathf.PerlinNoise(
                        x * mountainNoiseScale + randOffset + 100f,
                        z * mountainNoiseScale + randOffset + 100f
                    );
                    mountainNoise = Mathf.Pow(mountainNoise, 2.5f);

                    // Combine with valley influence
                    float baseHeightComponent = baseNoise * this.baseHeight * (0.7f + valleyFactor * 0.3f);
                    float mountainHeightComponent = mountainNoise * this.mountainHeight * Mathf.Lerp(0.1f, 1.0f, valleyFactor);

                    // Calculate final height
                    float height = baseHeightComponent + mountainHeightComponent;

                    // Apply valley depression
                    if (valleyFactor < 0.4f)
                    {
                        height *= Mathf.Lerp(1.0f - valleyDepth, 1.0f, valleyFactor / 0.4f);
                    }

                    globalHeightmap[x, z] = height;
                }
            }
        }

        public float GetHeightAt(float worldX, float worldZ)
        {
            if (!isInitialized) Initialize();

            // Convert world coordinates to heightmap indices
            float x = worldX / (worldSizeX * chunkSize) * globalHeightmap.GetLength(0);
            float z = worldZ / (worldSizeZ * chunkSize) * globalHeightmap.GetLength(1);

            // Bilinear sampling for smooth interpolation
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, globalHeightmap.GetLength(0) - 1);
            int z1 = Mathf.Min(z0 + 1, globalHeightmap.GetLength(1) - 1);

            float xFrac = x - x0;
            float zFrac = z - z0;

            float h00 = globalHeightmap[x0, z0];
            float h01 = globalHeightmap[x0, z1];
            float h10 = globalHeightmap[x1, z0];
            float h11 = globalHeightmap[x1, z1];

            // Interpolate
            float h0 = Mathf.Lerp(h00, h01, zFrac);
            float h1 = Mathf.Lerp(h10, h11, zFrac);
            float height = Mathf.Lerp(h0, h1, xFrac);

            return height;
        }

        public bool ShouldGenerateMeshAt(Vector3Int chunkPos, int chunkSize)
        {
            if (!isInitialized) Initialize();

            float chunkBottom = chunkPos.y * chunkSize;
            float chunkTop = (chunkPos.y + 1) * chunkSize;

            // If this is a ground-level chunk, always generate
            if (chunkPos.y == 0) return true;

            // Sample multiple points across the chunk's footprint
            int sampleCount = 4; // 4x4 grid of sample points

            for (int x = 0; x <= sampleCount; x++)
            {
                for (int z = 0; z <= sampleCount; z++)
                {
                    float worldX = (chunkPos.x + x / (float)sampleCount) * chunkSize;
                    float worldZ = (chunkPos.z + z / (float)sampleCount) * chunkSize;
                    float terrainHeight = GetHeightAt(worldX, worldZ);

                    // If any point in the chunk has terrain, generate a mesh
                    if (terrainHeight >= chunkBottom && terrainHeight < chunkTop)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void VisualizeHeightmap()
        {
            GameObject visualizer = new GameObject("HeightmapVisualizer");
            visualizer.transform.parent = transform;

            for (int x = 0; x < globalHeightmap.GetLength(0); x += 4)
            {
                for (int z = 0; z < globalHeightmap.GetLength(1); z += 4)
                {
                    float worldX = x * worldSizeX * chunkSize / (float)globalHeightmap.GetLength(0);
                    float worldZ = z * worldSizeZ * chunkSize / (float)globalHeightmap.GetLength(1);
                    float height = globalHeightmap[x, z];

                    GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    point.transform.parent = visualizer.transform;
                    point.transform.localScale = Vector3.one * 0.5f;
                    point.transform.position = new Vector3(worldX, height, worldZ);

                    // Color based on height
                    float heightRatio = height / (baseHeight + mountainHeight);
                    Renderer renderer = point.GetComponent<Renderer>();
                    renderer.material.color = Color.Lerp(Color.blue, Color.red, heightRatio);
                }
            }
        }
    }
}