using System;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Performance;
using WFC.Chunking;

namespace WFC.MarchingCubes
{
    public class MeshGenerator : MonoBehaviour
    {
        [SerializeField] public WFCGenerator wfcGenerator;
        [SerializeField] private ChunkManager chunkManager;
        [SerializeField] private PerformanceMonitor performanceMonitor;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private bool autoUpdate = false;
        [SerializeField] private float cellSize = 1.0f;

        [Header("Density Configuration")]
        [SerializeField] private float surfaceLevel = 0.5f;

        [Header("Debug Options")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private bool enableDetailedLogging = false;
        [SerializeField] private Color gizmoColor = new Color(0, 1, 0, 0.5f);

        // LOD settings override (if not using the global configuration)
        [Header("LOD Settings")]
        [SerializeField] private bool useUnityLODSystem = false;
        [SerializeField] private float[] lodDistanceThresholds = new float[] { 50f, 100f, 200f };

        private DensityFieldGenerator densityGenerator;
        private MarchingCubesGenerator marchingCubes;
        private Dictionary<Vector3Int, GameObject> chunkMeshObjects = new Dictionary<Vector3Int, GameObject>();

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

        public void GenerateChunkMesh(Vector3Int chunkPos, Chunk chunk)
        {
            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("MeshGeneration");

            try
            {
                // Generate density field
                float[,,] densityField = densityGenerator.GenerateDensityField(chunk);

                // Generate mesh using appropriate LOD level
                Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

                // Create or update the GameObject for this chunk
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

                // Assign the mesh
                MeshFilter meshFilter = meshObject.GetComponent<MeshFilter>();
                meshFilter.mesh = mesh;

                // Get a reference to the renderer
                MeshRenderer meshRenderer = meshObject.GetComponent<MeshRenderer>();

                // Apply the appropriate material based on dominant state
                int dominantState = GetDominantState(chunk);
                if (wfcGenerator != null && dominantState >= 0)
                {
                    Material[] stateMaterials = wfcGenerator.GetStateMaterials();

                    if (stateMaterials != null && stateMaterials.Length > dominantState && stateMaterials[dominantState] != null)
                    {
                        meshRenderer.material = stateMaterials[dominantState];
                        if (enableDetailedLogging) Debug.Log($"Applied material for state {dominantState} to chunk {chunkPos}");
                    }
                    else
                    {
                        meshRenderer.material = terrainMaterial;
                        if (enableDetailedLogging) Debug.Log($"Using fallback material for chunk {chunkPos} - state {dominantState}");
                    }
                }
                else
                {
                    meshRenderer.material = terrainMaterial;
                    if (enableDetailedLogging) Debug.Log($"Using default material for chunk {chunkPos}");
                }

                // Set up LOD if enabled
                if (useUnityLODSystem)
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
                throw;
            }
            finally
            {
                if (performanceMonitor != null)
                    performanceMonitor.EndComponentTiming("MeshGeneration");
            }
        }

        private int GetDominantState(Chunk chunk)
        {
            Dictionary<int, int> stateCounts = new Dictionary<int, int>();
            int totalCells = 0;

            // Sample cells for performance (check every other cell)
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

                            // Skip empty/air state
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

            return dominantState;
        }

        private void SetupUnityLODGroup(GameObject meshObject, Chunk chunk)
        {
            LODGroup lodGroup = meshObject.GetComponent<LODGroup>();
            if (lodGroup == null)
                lodGroup = meshObject.AddComponent<LODGroup>();

            int lodLevelCount = lodDistanceThresholds.Length + 1;
            LOD[] lods = new LOD[lodLevelCount];

            // Get renderers
            Renderer renderer = meshObject.GetComponent<Renderer>();

            // Set up LOD levels
            for (int i = 0; i < lodLevelCount; i++)
            {
                // Calculate screen relative transition height
                float screenRelativeTransitionHeight = 1.0f - (i / (float)lodLevelCount);
                lods[i] = new LOD(screenRelativeTransitionHeight, new Renderer[] { renderer });
            }

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
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
                GameObject meshObject = entry.Value;
                if (meshObject != null)
                {
                    if (Application.isPlaying)
                        Destroy(meshObject);
                    else
                        DestroyImmediate(meshObject);
                }
            }

            chunkMeshObjects.Clear();
        }

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

                GenerateChunkMesh(chunkPos, chunk);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error generating mesh for chunk {chunkPos}: {e.Message}");
            }
        }

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