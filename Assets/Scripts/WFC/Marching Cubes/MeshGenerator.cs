// Assets/Scripts/WFC/MarchingCubes/MeshGenerator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Configuration;
using WFC.Performance;
using WFC.Chunking;

namespace WFC.MarchingCubes
{
    public class MeshGenerator : MonoBehaviour
    {
        [SerializeField] public WFCGenerator wfcGenerator;
        [SerializeField] private ChunkManager chunkManager; // Add this field
        [SerializeField] private PerformanceMonitor performanceMonitor;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private bool autoUpdate = false;
        [SerializeField] private float cellSize = 1.0f;

        [Header("Density Configuration")]
        [SerializeField] private float surfaceLevel = 0.5f;
        [SerializeField] private bool forceTerrainSurface = true;

        [Header("Debug Options")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private bool enableDetailedLogging = false;
        [SerializeField] private Color gizmoColor = new Color(0, 1, 0, 0.5f); // Semi-transparent green

        // LOD settings override (if not using the global configuration)
        [Header("LOD Settings")]
        [SerializeField] private bool useUnityLODSystem = false;
        [SerializeField] private float[] lodDistanceThresholds = new float[] { 50f, 100f, 200f };

        // Reuse these instances instead of creating new ones every time
        private DensityFieldGenerator densityGenerator;
        private MarchingCubesGenerator marchingCubes;
        private Dictionary<Vector3Int, GameObject> chunkMeshObjects = new Dictionary<Vector3Int, GameObject>();

        // Debug data
        private Dictionary<Vector3Int, float[,,]> debugDensityFields = new Dictionary<Vector3Int, float[,,]>();
        private const int MAX_DEBUG_FIELDS = 10; // Limit the number of debug fields to prevent memory leaks
        private Queue<Vector3Int> debugFieldsEvictionQueue = new Queue<Vector3Int>();

        // Event for auto-update
        public delegate void MeshGeneratedHandler(Vector3Int chunkPos);
        public event MeshGeneratedHandler OnMeshGenerated;

        private void Start()
        {
            // Initialize generators once and reuse them
            densityGenerator = new DensityFieldGenerator();
            marchingCubes = new MarchingCubesGenerator();

            // Apply surface level settings
            densityGenerator.SetSurfaceLevel(surfaceLevel);
            marchingCubes.SetSurfaceLevel(surfaceLevel);
            marchingCubes.enableDebugInfo = showDebugInfo;

            // Find references if not set
            InitializeReferences();

            if (autoUpdate && chunkManager != null)
            {
                // Find and subscribe to chunk state events
                SubscribeToChunkEvents();
            }
        }

        private void InitializeReferences()
        {
            if (wfcGenerator == null)
                wfcGenerator = FindAnyObjectByType<WFCGenerator>();

            if (chunkManager == null)
                chunkManager = FindAnyObjectByType<ChunkManager>();

            if (performanceMonitor == null)
                performanceMonitor = FindAnyObjectByType<PerformanceMonitor>();
        }

        private void SubscribeToChunkEvents()
        {
            if (chunkManager != null)
            {
                // Subscribe to the event
                chunkManager.OnChunkStateChanged += OnChunkStateChanged;
                Debug.Log("MeshGenerator: Successfully subscribed to chunk state change events");
            }
        }
        // Add this handler method
        private void OnChunkStateChanged(Vector3Int chunkPos, ChunkManager.ChunkLifecycleState oldState, ChunkManager.ChunkLifecycleState newState)
        {
            // Generate mesh when a chunk is fully processed
            if (newState == ChunkManager.ChunkLifecycleState.Active &&
                (oldState == ChunkManager.ChunkLifecycleState.Collapsing ||
                 oldState == ChunkManager.ChunkLifecycleState.GeneratingMesh))
            {
                var chunks = chunkManager.GetLoadedChunks();
                if (chunks.TryGetValue(chunkPos, out Chunk chunk))
                {
                    if (showDebugInfo) Debug.Log($"Chunk state changed to Active, generating mesh for {chunkPos}");
                    GenerateChunkMesh(chunkPos, chunk);
                }
            }
        }
        private void Update()
        {
            // Keyboard controls for mesh generation and debugging
            if (Input.GetKeyDown(KeyCode.M))
            {
                if (showDebugInfo) Debug.Log("M key pressed - Generating meshes");
                GenerateAllMeshes();
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                if (showDebugInfo) Debug.Log("L key pressed - Clearing meshes");
                ClearMeshes();
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                if (showDebugInfo) Debug.Log("G key pressed - Debugging density field");
                // Debug the first chunk by default
                var firstChunk = wfcGenerator.GetChunks().FirstOrDefault();
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
                if (showDebugInfo) Debug.Log("T key pressed - Creating test mesh");
                GenerateSimpleTerrainMesh();
            }

            //// Force regeneration of all meshes
            //if (Input.GetKeyDown(KeyCode.F))
            //{
            //    if (showDebugInfo) Debug.Log("F key pressed - Forcing new mesh generation");
            //    RegenerateAllMeshes();
            //}
        }

        public void GenerateAllMeshes()
        {
            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("GenerateAllMeshes");

            try
            {
                // Get chunks from ChunkManager instead of WFCGenerator
                if (chunkManager == null)
                {
                    Debug.LogError("ChunkManager reference is missing!");
                    return;
                }

                var chunks = chunkManager.GetLoadedChunks();
                Debug.Log($"Found {chunks.Count} chunks to process from ChunkManager");

                foreach (var chunkEntry in chunks)
                {
                    try
                    {
                        if (showDebugInfo) Debug.Log($"Generating mesh for chunk at {chunkEntry.Key}");
                        GenerateChunkMesh(chunkEntry.Key, chunkEntry.Value);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error generating mesh for chunk at {chunkEntry.Key}: {e.Message}");
                        // Continue with other chunks despite this error
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in GenerateAllMeshes: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                if (performanceMonitor != null)
                    performanceMonitor.EndComponentTiming("GenerateAllMeshes");
            }
        }
        private void ClearDensityFieldCache()
        {
            densityGenerator.ClearCache();
            debugDensityFields.Clear();
            debugFieldsEvictionQueue.Clear();
        }

        // Add this test method to create a simple mesh for debugging rendering issues
        public void GenerateSimpleTerrainMesh()
        {
            Debug.Log("Generating simple test terrain mesh...");

            try
            {
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
                mesh.RecalculateBounds();

                // Create a GameObject to hold the mesh
                GameObject meshObject = new GameObject("TestTerrainMesh");
                meshObject.transform.parent = transform;

                // Add MeshFilter and MeshRenderer
                MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
                meshFilter.mesh = mesh;

                MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
                meshRenderer.material = terrainMaterial;

                // Add to tracked meshes
                Vector3Int testPos = new Vector3Int(-999, -999, -999); // Special position for test mesh
                chunkMeshObjects[testPos] = meshObject;

                Debug.Log("Test terrain mesh created successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error creating test mesh: {e.Message}\n{e.StackTrace}");
            }
        }

        public void GenerateChunkMesh(Vector3Int chunkPos, Chunk chunk)
        {
            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("MeshGeneration");

            try
            {
                // Step 1: Generate density field using our reused generator
                float[,,] densityField = densityGenerator.GenerateDensityField(chunk);

                // Step 2: Generate mesh using appropriate LOD level
                Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

                // Step 3: Create or update the GameObject for this chunk
                string chunkName = $"Terrain_Chunk_{chunkPos.x}_{chunkPos.y}_{chunkPos.z}";
                GameObject meshObject;

                // Try to get from dictionary first
                if (!chunkMeshObjects.TryGetValue(chunkPos, out meshObject) || meshObject == null)
                {
                    // Not in dictionary or destroyed, check by name
                    meshObject = GameObject.Find(chunkName);

                    if (meshObject == null)
                    {
                        // Create new mesh object
                        meshObject = new GameObject(chunkName);
                        meshObject.transform.parent = transform;
                        meshObject.transform.position = new Vector3(
                            chunkPos.x * chunk.Size * cellSize,
                            chunkPos.y * chunk.Size * cellSize,
                            chunkPos.z * chunk.Size * cellSize
                        );
                        meshObject.AddComponent<MeshFilter>();
                        meshObject.AddComponent<MeshRenderer>();
                    }

                    // Update dictionary
                    chunkMeshObjects[chunkPos] = meshObject;
                }

                // Step 4: Assign the mesh
                MeshFilter meshFilter = meshObject.GetComponent<MeshFilter>();
                meshFilter.mesh = mesh;

                // Step 5: Get a reference to the renderer
                MeshRenderer meshRenderer = meshObject.GetComponent<MeshRenderer>();

                // Step 6: Apply the appropriate material based on dominant state
                int dominantState = GetDominantState(chunk);
                if (wfcGenerator != null && dominantState >= 0)
                {
                    // Get state materials directly instead of using reflection
                    Material[] stateMaterials = wfcGenerator.GetStateMaterials();

                    // Check if we got valid materials
                    if (stateMaterials != null && stateMaterials.Length > dominantState && stateMaterials[dominantState] != null)
                    {
                        meshRenderer.material = stateMaterials[dominantState];
                        if (enableDetailedLogging) Debug.Log($"Applied material for state {dominantState} to chunk {chunkPos}");
                    }
                    else
                    {
                        // Fallback to terrainMaterial field
                        meshRenderer.material = terrainMaterial;
                        if (enableDetailedLogging) Debug.Log($"Using fallback material for chunk {chunkPos} - state {dominantState}");
                    }
                }
                else
                {
                    // Use the default terrain material as fallback
                    meshRenderer.material = terrainMaterial;
                    if (enableDetailedLogging) Debug.Log($"Using default material for chunk {chunkPos}");
                }

                // Step 7: Set up LOD if enabled
                if (useUnityLODSystem || (WFCConfigManager.Config != null &&
                    WFCConfigManager.Config.Performance.lodSettings.useUnityLODSystem))
                {
                    SetupUnityLODGroup(meshObject, chunk);
                }

                // Mark chunk as processed
                chunk.IsDirty = false;

                // Notify subscribers about the generated mesh
                OnMeshGenerated?.Invoke(chunkPos);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error generating mesh for chunk at {chunkPos}: {e.Message}\n{e.StackTrace}");
                throw; // Rethrow for proper handling by caller
            }
            finally
            {
                if (performanceMonitor != null)
                    performanceMonitor.EndComponentTiming("MeshGeneration");
            }
        }

        // Helper method to determine the dominant state in a chunk
        private int GetDominantState(Chunk chunk)
        {
            Dictionary<int, int> stateCounts = new Dictionary<int, int>();
            int totalCells = 0;

            // Use a sampling approach for performance (check every other cell)
            for (int x = 0; x < chunk.Size; x += 2)
            {
                for (int y = 0; y < chunk.Size; y += 2)
                {
                    for (int z = 0; z < chunk.Size; z += 2)
                    {
                        Cell cell = chunk.GetCell(x, y, z);
                        if (cell != null && cell.CollapsedState.HasValue)
                        {
                            int state = cell.CollapsedState.Value;

                            // Skip empty/air state for material determination
                            if (state != 0)
                            {
                                if (!stateCounts.ContainsKey(state))
                                    stateCounts[state] = 0;

                                stateCounts[state]++;
                                totalCells++;
                            }
                        }
                    }
                }
            }

            // Only log states if detailed logging is enabled
            if (enableDetailedLogging)
            {
                Debug.Log($"Chunk {chunk.Position} has states: {string.Join(", ", stateCounts.Keys)}");

                foreach (var pair in stateCounts)
                {
                    Debug.Log($"  State {pair.Key}: {pair.Value} cells ({(float)pair.Value / totalCells * 100:F1}%)");
                }
            }

            // Find the dominant state (skip air/state 0)
            int maxCount = 0;
            int dominantState = 1; // Default to ground

            foreach (var pair in stateCounts)
            {
                if (pair.Value > maxCount)
                {
                    maxCount = pair.Value;
                    dominantState = pair.Key;
                }
            }

            if (enableDetailedLogging) Debug.Log($"Dominant state: {dominantState}");
            return dominantState;
        }

        private void SetupUnityLODGroup(GameObject meshObject, Chunk chunk)
        {
            // Get configuration from WFCConfigManager
            var config = WFCConfigManager.Config;

            // Get LOD system settings from config or local settings
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

        public void UpdateChunkMesh(Vector3Int chunkPos)
        {
            try
            {
                if (wfcGenerator.GetChunks().TryGetValue(chunkPos, out Chunk chunk))
                {
                    GenerateChunkMesh(chunkPos, chunk);
                }
                else
                {
                    Debug.LogWarning($"Cannot update mesh - chunk not found at {chunkPos}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error updating chunk mesh at {chunkPos}: {e.Message}");
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
            foreach (var entry in chunkMeshObjects)
            {
                Vector3Int chunkPos = entry.Key;
                GameObject meshObject = entry.Value;

                if (meshObject != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(meshObject); // Use Destroy during gameplay
                    }
                    else
                    {
                        DestroyImmediate(meshObject); // Use DestroyImmediate only in editor
                    }
                }
            }

            chunkMeshObjects.Clear();
        }

        public void DebugDensityField(Vector3Int chunkPos)
        {
            try
            {
                if (!wfcGenerator.GetChunks().TryGetValue(chunkPos, out Chunk chunk))
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
                    // Add to eviction queue and remove oldest if at limit
                    if (debugDensityFields.Count >= MAX_DEBUG_FIELDS)
                    {
                        Vector3Int oldestPos = debugFieldsEvictionQueue.Dequeue();
                        debugDensityFields.Remove(oldestPos);
                        Debug.Log($"Removed old debug density field for chunk {oldestPos}");
                    }

                    densityField = densityGenerator.GenerateDensityField(chunk);
                    debugDensityFields[chunkPos] = densityField;
                    debugFieldsEvictionQueue.Enqueue(chunkPos);
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
            catch (Exception e)
            {
                Debug.LogError($"Error debugging density field: {e.Message}\n{e.StackTrace}");
            }
        }

        // Support for specific chunks
        // Update this method to use ChunkManager too
        public void GenerateMeshForChunk(Vector3Int chunkPos)
        {
            try
            {
                if (chunkManager == null)
                {
                    Debug.LogError("MeshGenerator: ChunkManager reference is missing!");
                    return;
                }

                var chunks = chunkManager.GetLoadedChunks();
                if (!chunks.TryGetValue(chunkPos, out Chunk chunk))
                {
                    Debug.LogWarning($"MeshGenerator: Chunk not found at {chunkPos}");
                    return;
                }

                Debug.Log($"Generating mesh for chunk {chunkPos}...");
                GenerateChunkMesh(chunkPos, chunk);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error generating mesh for chunk {chunkPos}: {e.Message}");
            }
        }

        // Also update OnDrawGizmos to use ChunkManager's chunks
        private void OnDrawGizmos()
        {
            if (!showDebugInfo || chunkManager == null)
                return;

            Gizmos.color = gizmoColor;

            // Draw chunk boundaries
            foreach (var chunkEntry in chunkManager.GetLoadedChunks())
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