// Assets/Scripts/WFC/MarchingCubes/MeshGenerator.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Configuration; // Add this import for accessing configuration

namespace WFC.MarchingCubes
{
    public class MeshGenerator : MonoBehaviour
    {
        [SerializeField] public WFCGenerator wfcController;     // changed
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private bool autoUpdate = false;
        [SerializeField] private float cellSize = 1.0f;

        [Header("Density Configuration")]
        [SerializeField] private float surfaceLevel = 0.5f;
        [SerializeField] private bool forceTerrainSurface = true;

        [Header("Debug Options")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private Color gizmoColor = new Color(0, 1, 0, 0.5f); // Semi-transparent green

        // LOD settings override (if not using the global configuration)
        [Header("LOD Settings")]
        [SerializeField] private bool useUnityLODSystem = false;
        [SerializeField] private float[] lodDistanceThresholds = new float[] { 50f, 100f, 200f };

        private DensityFieldGenerator densityGenerator;
        private MarchingCubesGenerator marchingCubes;
        private Dictionary<Vector3Int, GameObject> chunkMeshObjects = new Dictionary<Vector3Int, GameObject>();

        // Debug data
        private Dictionary<Vector3Int, float[,,]> debugDensityFields = new Dictionary<Vector3Int, float[,,]>();

        private void Start()
        {
            densityGenerator = new DensityFieldGenerator();
            marchingCubes = new MarchingCubesGenerator();

            // Apply surface level settings
            densityGenerator.SetSurfaceLevel(surfaceLevel);
            marchingCubes.SetSurfaceLevel(surfaceLevel);
            marchingCubes.enableDebugInfo = showDebugInfo;

            if (autoUpdate)
            {
                // Automatically generate meshes when WFC makes progress
                // This would require a callback from the WFC controller
            }
        }

        private void Update()
        {
            // Keyboard controls for mesh generation and debugging
            if (Input.GetKeyDown(KeyCode.M))
            {
                Debug.Log("M key pressed - Generating meshes");
                GenerateAllMeshes();
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                Debug.Log("L key pressed - Clearing meshes");
                ClearMeshes();
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                Debug.Log("G key pressed - Debugging density field");
                // Debug the first chunk by default
                var firstChunk = wfcController.GetChunks().FirstOrDefault();
                if (firstChunk.Value != null)
                {
                    DebugDensityField(firstChunk.Key);
                }
                else
                {
                    Debug.LogWarning("No chunks available to debug");
                }
            }

            // Add simple mesh for debugging purposes
            if (Input.GetKeyDown(KeyCode.T))
            {
                Debug.Log("T key pressed - Creating test mesh");
                GenerateSimpleTerrainMesh();
            }

            // Force regeneration of all meshes
            if (Input.GetKeyDown(KeyCode.F))
            {
                Debug.Log("F key pressed - Forcing new mesh generation");
                RegenerateAllMeshes();
            }
        }

        private void RegenerateAllMeshes()
        {
            // Clear existing meshes and cached density fields
            ClearMeshes();
            densityGenerator.ClearCache();
            debugDensityFields.Clear();

            // Generate new meshes
            GenerateAllMeshes();
        }

        // Add this test method to create a simple mesh for debugging rendering issues
        public void GenerateSimpleTerrainMesh()
        {
            Debug.Log("Generating simple test terrain mesh...");

            // Create a simple plane mesh
            Mesh mesh = new Mesh();

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(0, 0, 0),
                new Vector3(10, 0, 0),
                new Vector3(0, 0, 10),
                new Vector3(10, 0, 10)
            };

            int[] triangles = new int[]
            {
                0, 2, 1,
                1, 2, 3
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.up,
                Vector3.up,
                Vector3.up,
                Vector3.up
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;

            // Create a GameObject to hold the mesh
            GameObject meshObject = new GameObject("TestTerrainMesh");
            meshObject.transform.parent = transform;

            // Add MeshFilter and MeshRenderer
            MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.material = terrainMaterial;

            Debug.Log("Test terrain mesh created successfully");
        }

        public void GenerateAllMeshes()
        {
            Debug.Log("Starting mesh generation for all chunks...");

            if (wfcController == null)
            {
                Debug.LogError("WFC Controller reference is missing!");
                return;
            }

            var chunks = wfcController.GetChunks();
            Debug.Log($"Found {chunks.Count} chunks to process");

            foreach (var chunkEntry in chunks)
            {
                Debug.Log($"Generating mesh for chunk at {chunkEntry.Key}");
                GenerateChunkMesh(chunkEntry.Key, chunkEntry.Value);
            }
        }

        // Add this overload to fix the "no overload for method takes 2 arguments" error
        public void GenerateChunkMesh(Vector3Int chunkPos, Chunk chunk)
        {
            GenerateChunkMesh(chunk);
        }

        public void GenerateChunkMesh(Chunk chunk)
        {
            Debug.Log($"Generating mesh for chunk {chunk.Position}...");

            try
            {
                // Generate density field
                float[,,] densityField = densityGenerator.GenerateDensityField(chunk);

                // Store for debugging
                debugDensityFields[chunk.Position] = densityField;

                // Generate mesh with appropriate LOD level
                Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

                // Debug mesh info
                if (mesh != null)
                {
                    Debug.Log($"Mesh generated with {mesh.vertexCount} vertices and {mesh.triangles.Length / 3} triangles (LOD {chunk.LODLevel})");
                }
                else
                {
                    Debug.LogError("Failed to generate mesh!");
                    return;
                }

                if (mesh.vertexCount == 0)
                {
                    Debug.LogWarning("Mesh has 0 vertices - no visible surface will be created");

                    if (forceTerrainSurface)
                    {
                        // Create a simple flat terrain instead
                        Debug.Log("Forcing simple terrain mesh as fallback");
                        mesh = CreateFallbackTerrainMesh(chunk.Size);
                    }
                }

                // Create or update GameObject
                if (!chunkMeshObjects.TryGetValue(chunk.Position, out GameObject meshObject))
                {
                    // Create new mesh object
                    meshObject = new GameObject($"Terrain_Chunk_{chunk.Position.x}_{chunk.Position.y}_{chunk.Position.z}");
                    meshObject.transform.parent = transform;
                    meshObject.transform.position = new Vector3(
                        chunk.Position.x * chunk.Size * cellSize,
                        chunk.Position.y * chunk.Size * cellSize,
                        chunk.Position.z * chunk.Size * cellSize
                    );

                    // Add MeshFilter and MeshRenderer
                    meshObject.AddComponent<MeshFilter>();
                    meshObject.AddComponent<MeshRenderer>();

                    chunkMeshObjects[chunk.Position] = meshObject;
                }

                // Update mesh
                MeshFilter meshFilter = meshObject.GetComponent<MeshFilter>();
                meshFilter.mesh = mesh;

                // Update material
                MeshRenderer meshRenderer = meshObject.GetComponent<MeshRenderer>();
                meshRenderer.material = terrainMaterial;

                // Setup LOD group if using Unity's LOD system
                SetupUnityLODGroup(meshObject, chunk);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error generating mesh for chunk {chunk.Position}: {e.Message}\n{e.StackTrace}");
            }
        }

        private void SetupUnityLODGroup(GameObject meshObject, Chunk chunk)
        {
            // Get configuration from WFCConfigManager instead of a non-existent ConfigurationManager
            var config = WFCConfigManager.Config;

            // Check if we should use Unity's LOD system (either from config or local setting)
            bool useLODSystem = (config != null && config.Performance.lodSettings.useUnityLODSystem) || useUnityLODSystem;
            if (!useLODSystem)
                return;

            LODGroup lodGroup = meshObject.GetComponent<LODGroup>();
            if (lodGroup == null)
                lodGroup = meshObject.AddComponent<LODGroup>();

            // Create LOD levels based on the configuration or local settings
            float[] thresholds = config != null && config.Performance.lodSettings.lodDistanceThresholds.Length > 0
                ? config.Performance.lodSettings.lodDistanceThresholds
                : lodDistanceThresholds;

            int lodLevelCount = thresholds.Length + 1;
            LOD[] lods = new LOD[lodLevelCount];

            // Get renderers
            Renderer renderer = meshObject.GetComponent<Renderer>();

            // Set up LOD levels
            for (int i = 0; i < lodLevelCount; i++)
            {
                // Calculate screen relative transition height
                float screenRelativeTransitionHeight = 1.0f - (i / (float)lodLevelCount);

                // For simplicity, we'll use the same renderer for all LOD levels
                lods[i] = new LOD(screenRelativeTransitionHeight, new Renderer[] { renderer });
            }

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
        }

        private Mesh CreateFallbackTerrainMesh(int size)
        {
            // Create a simple flat terrain mesh as a fallback
            Mesh mesh = new Mesh();

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(0, size/2, 0),
                new Vector3(size, size/2, 0),
                new Vector3(0, size/2, size),
                new Vector3(size, size/2, size)
            };

            int[] triangles = new int[]
            {
                0, 2, 1,
                1, 2, 3
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.up,
                Vector3.up,
                Vector3.up,
                Vector3.up
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;

            return mesh;
        }

        public void UpdateChunkMesh(Vector3Int chunkPos)
        {
            if (wfcController.GetChunks().TryGetValue(chunkPos, out Chunk chunk))
            {
                GenerateChunkMesh(chunk);
            }
        }

        [ContextMenu("Generate Meshes")]
        public void GenerateMeshesNow()
        {
            GenerateAllMeshes();
        }

        [ContextMenu("Clear Meshes")]
        public void ClearMeshes()
        {
            foreach (var meshObject in chunkMeshObjects.Values)
            {
                if (meshObject != null)
                {
                    DestroyImmediate(meshObject);
                }
            }

            chunkMeshObjects.Clear();
        }

        public void DebugDensityField(Vector3Int chunkPos)
        {
            if (!wfcController.GetChunks().TryGetValue(chunkPos, out Chunk chunk))
            {
                Debug.LogError($"Chunk not found at {chunkPos}");
                return;
            }

            Debug.Log($"Debugging density field for chunk {chunkPos}");

            // Generate or retrieve density field
            float[,,] densityField;
            if (debugDensityFields.TryGetValue(chunkPos, out float[,,] cachedField))
            {
                densityField = cachedField;
                Debug.Log("Using cached density field");
            }
            else
            {
                densityField = densityGenerator.GenerateDensityField(chunk);
                debugDensityFields[chunkPos] = densityField;
                Debug.Log("Generated new density field");
            }

            int size = densityField.GetLength(0) - 1;

            // Count values above and below threshold
            int aboveThreshold = 0;
            int belowThreshold = 0;
            float minValue = float.MaxValue;
            float maxValue = float.MinValue;

            for (int x = 0; x <= size; x++)
            {
                for (int y = 0; y <= size; y++)
                {
                    for (int z = 0; z <= size; z++)
                    {
                        float value = densityField[x, y, z];

                        if (value >= surfaceLevel)
                            aboveThreshold++;
                        else
                            belowThreshold++;

                        minValue = Mathf.Min(minValue, value);
                        maxValue = Mathf.Max(maxValue, value);
                    }
                }
            }

            Debug.Log($"Density field analysis: {aboveThreshold} points above threshold, {belowThreshold} below threshold");
            Debug.Log($"Density range: min={minValue:F3}, max={maxValue:F3}, threshold={surfaceLevel:F3}");
            Debug.Log($"Surface will only generate if there are points on both sides of the threshold");

            // Sample some values
            Debug.Log("Sample density values:");
            for (int x = 0; x <= size; x += size / 4)
            {
                for (int y = 0; y <= size; y += size / 4)
                {
                    for (int z = 0; z <= size; z += size / 4)
                    {
                        Debug.Log($"  Position ({x},{y},{z}): {densityField[x, y, z]:F3}");
                    }
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!showDebugInfo || wfcController == null)
                return;

            Gizmos.color = gizmoColor;

            // Draw chunk boundaries
            foreach (var chunkEntry in wfcController.GetChunks())
            {
                Vector3Int chunkPos = chunkEntry.Key;
                Chunk chunk = chunkEntry.Value;

                Vector3 worldPos = new Vector3(
                    chunkPos.x * chunk.Size * cellSize,
                    chunkPos.y * chunk.Size * cellSize,
                    chunkPos.z * chunk.Size * cellSize
                );

                Vector3 size = new Vector3(
                    chunk.Size * cellSize,
                    chunk.Size * cellSize,
                    chunk.Size * cellSize
                );

                Gizmos.DrawWireCube(worldPos + size * 0.5f, size);
            }
        }
    }
}