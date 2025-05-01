using System;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Performance;
using WFC.Chunking;
using WFC.Terrain;
using System.Linq;

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

        [Header("Visualization Options")]
        [SerializeField] private bool showChunkBoundaries = true;
        [SerializeField] private bool showCells = false;
        [SerializeField] private bool showCollapsedCellInfo = false;
        [SerializeField] private Color chunkBoundaryColor = Color.green;
        [SerializeField] private Color cellColor = new Color(1, 0, 0, 0.3f);
        [SerializeField] private float cellVisualizationDistance = 100f;

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

        [SerializeField] private GlobalHeightmapController heightmapController;

        private void Start()
        {
            TerrainDefinition terrainDef = TerrainManager.Current?.CurrentTerrain;

            // Initialize with proper terrain definition
            densityGenerator = new DensityFieldGenerator(terrainDef);
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

        // 
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

        // This method generates all meshes for the loaded chunks
        public void GenerateAllMeshes()
        {
            if (densityGenerator != null)       // Added after submission and a test change
                densityGenerator.ClearCache();

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

        // This method generates the mesh for a specific chunk
        public void GenerateChunkMesh(Vector3Int chunkPos, Chunk chunk)
        {
            if (chunk.Position.y > 0 && heightmapController != null &&
            !heightmapController.ShouldGenerateMeshAt(chunkPos, chunk.Size))
            {
                // Mark as not dirty so no more mesh generation attempts occur
                chunk.IsDirty = false;
                return;
            }

            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("MeshGeneration");

            try
            {
                // Generate density field
                float[,,] densityField = densityGenerator.GenerateDensityField(chunk);

                // Generate mesh using appropriate LOD level
                Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

                ProcessBoundaryVertices(mesh, chunkPos, chunk.Size);

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

                // Reference to the renderer
                MeshRenderer meshRenderer = meshObject.GetComponent<MeshRenderer>();

                // Apply the appropriate material based on dominant state
                int dominantState = GetDominantState(chunk);
                if (wfcGenerator != null && dominantState >= 0)
                {
                    Material[] stateMaterials = TerrainStateRegistry.Instance.StateMaterials;

                    if (stateMaterials != null && stateMaterials.Length > dominantState && stateMaterials[dominantState] != null)
                    {
                        meshRenderer.material = stateMaterials[dominantState];
                        if (enableDetailedLogging) Debug.Log($"Applied material for state {dominantState} to chunk {chunkPos}");
                    }
                    else
                    {
                        meshRenderer.material = terrainMaterial;
                        Debug.LogError($"Material for state {dominantState} not found. Using default material.");
                        if (enableDetailedLogging) Debug.Log($"Using fallback material for chunk {chunkPos} - state {dominantState}");
                    }
                }
                else
                {
                    meshRenderer.material = terrainMaterial;
                    if (enableDetailedLogging) Debug.Log($"Using default material for chunk {chunkPos}");
                }

                // Apply trees if WFCGenerator is available
                if (wfcGenerator != null && chunk.Position.y == 0)
                {
                    wfcGenerator.GenerateTreeModels(chunk);
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

        private void ProcessBoundaryVertices(Mesh mesh, Vector3Int chunkPos, int chunkSize)
        {
            Vector3[] vertices = mesh.vertices;

            // Calculate exact chunk boundary positions in local space
            float minX = 0;
            float maxX = chunkSize;
            float minY = 0;
            float maxY = chunkSize;
            float minZ = 0;
            float maxZ = chunkSize;

            // Small threshold for identifying boundary vertices
            float threshold = 0.001f;

            // Process each vertex
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                bool isBoundaryVertex = false;

                // Check if this vertex is on a boundary (x, y, or z)
                if (Mathf.Abs(v.x - minX) < threshold || Mathf.Abs(v.x - maxX) < threshold ||
                    Mathf.Abs(v.y - minY) < threshold || Mathf.Abs(v.y - maxY) < threshold ||
                    Mathf.Abs(v.z - minZ) < threshold || Mathf.Abs(v.z - maxZ) < threshold)
                {
                    isBoundaryVertex = true;
                }

                if (isBoundaryVertex)
                {
                    // Snap to exact boundary positions to ensure perfect alignment
                    if (Mathf.Abs(v.x - minX) < threshold) v.x = minX;
                    if (Mathf.Abs(v.x - maxX) < threshold) v.x = maxX;
                    if (Mathf.Abs(v.y - minY) < threshold) v.y = minY;
                    if (Mathf.Abs(v.y - maxY) < threshold) v.y = maxY;
                    if (Mathf.Abs(v.z - minZ) < threshold) v.z = minZ;
                    if (Mathf.Abs(v.z - maxZ) < threshold) v.z = maxZ;

                    // Update the vertex
                    vertices[i] = v;
                }
            }

            // Apply the modified vertices
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        // This method determines the dominant state of a chunk based on the cells within it
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

        // This method sets up the Unity LOD group for the mesh object
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

        // Gizmos for debugging
        private void OnDrawGizmos()
        {
            if (!showDebugInfo || chunkManager == null)
                return;

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

                // Draw chunk boundaries
                if (showChunkBoundaries)
                {
                    Gizmos.color = chunkBoundaryColor;
                    Gizmos.DrawWireCube(worldPos + size * 0.5f, size);
                }

                // Draw cells
                if (showCells)
                {
                    Gizmos.color = cellColor;

                    // Only draw cells for chunks close to the camera
                    float distanceToCamera = Vector3.Distance(Camera.main.transform.position, worldPos);
                    if (distanceToCamera < cellVisualizationDistance)
                    {
                        for (int x = 0; x < chunk.Size; x++)
                        {
                            for (int y = 0; y < chunk.Size; y++)
                            {
                                for (int z = 0; z < chunk.Size; z++)
                                {
                                    Vector3 cellPos = worldPos + new Vector3(
                                        x * cellSize,
                                        y * cellSize,
                                        z * cellSize
                                    );

                                    Gizmos.DrawWireCube(cellPos + Vector3.one * cellSize * 0.5f,
                                                       Vector3.one * cellSize * 0.9f);

                                    // Show collapsed cell info
                                    if (showCollapsedCellInfo)
                                    {
                                        Cell cell = chunk.GetCell(x, y, z);
                                        if (cell != null && cell.CollapsedState.HasValue)
                                        {
                                            // Change color based on state
                                            switch (cell.CollapsedState.Value)
                                            {
                                                case 0: // Air
                                                    Gizmos.color = Color.cyan;
                                                    break;
                                                case 1: // Ground
                                                    Gizmos.color = Color.green;
                                                    break;
                                                default:
                                                    Gizmos.color = new Color(
                                                        cell.CollapsedState.Value / (float)chunk.Size,
                                                        0.5f,
                                                        1.0f - cell.CollapsedState.Value / (float)chunk.Size
                                                    );
                                                    break;
                                            }

                                            Gizmos.DrawSphere(cellPos + Vector3.one * cellSize * 0.5f,
                                                             cellSize * 0.2f);

                                            Gizmos.color = cellColor; // Reset color
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}