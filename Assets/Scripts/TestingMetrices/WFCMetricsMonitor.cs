using UnityEngine;
using WFC.Boundary;
using WFC.Core;
using WFC.Generation;
using WFC.Chunking;
using System.Collections.Generic;
using System.Linq;

namespace WFC.Metrics
{
    [ExecuteInEditMode]
    public class WFCMetricsMonitor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WFCGenerator wfcGenerator;
        [SerializeField] private ChunkManager chunkManager;
        [SerializeField] private WFC.Performance.PerformanceMonitor performanceMonitor;

        [Header("Boundary Coherence Settings")]
        [SerializeField, Range(0f, 1f)] private float targetCoherenceScore = 0.9f;
        [SerializeField] private bool visualizeBoundaries = false;
        [SerializeField] private Color goodBoundaryColor = Color.green;
        [SerializeField] private Color badBoundaryColor = Color.red;
        [SerializeField] private float minAcceptableCoherence = 0.75f;

        [Header("Performance Metrics")]
        [SerializeField] private int testChunkCount = 9;
        [SerializeField] private bool trackGenerationLatency = true;
        [SerializeField] private float maxAcceptableChunkGenTime = 1.0f;
        [SerializeField] private float maxAcceptableFrameTimeMs = 16.7f; // 60fps

        [Header("Constraint Settings")]
        [SerializeField, Range(0f, 1f)] private float targetConstraintSatisfaction = 0.95f;
        [SerializeField] private bool visualizeConstraints = false;
        [SerializeField] private float minAcceptableConstraintRate = 0.8f;

        // Runtime metrics
        private float boundaryCoherenceScore = 0f;
        private int boundaryTotalCells = 0;
        private int boundaryCompatibleCells = 0;

        private float avgChunkGenerationTime = 0f;
        private float peakChunkGenerationTime = 0f;
        private Dictionary<Vector3Int, float> chunkGenTimes = new Dictionary<Vector3Int, float>();

        private float constraintSatisfactionRate = 0f;
        private int constraintsApplied = 0;
        private int constraintsSatisfied = 0;

        // Cached components
        private BoundaryBufferManager boundaryManager;
        private HierarchicalConstraintSystem constraintSystem;

        // Public getters
        public float BoundaryCoherence => boundaryCoherenceScore;
        public float AverageGenerationTime => avgChunkGenerationTime;
        public float ConstraintSatisfactionRate => constraintSatisfactionRate;

        private void Start()
        {
            FindReferences();
            InitializeMetrics();
        }

        private void Update()
        {
            if (boundaryManager != null && wfcGenerator != null)
                UpdateBoundaryMetrics();

            if (constraintSystem != null)
                UpdateConstraintMetrics();
        }

        private void FindReferences()
        {
            if (wfcGenerator == null)
                wfcGenerator = FindAnyObjectByType<WFCGenerator>();

            if (chunkManager == null)
                chunkManager = FindAnyObjectByType<ChunkManager>();

            if (performanceMonitor == null)
                performanceMonitor = FindAnyObjectByType<WFC.Performance.PerformanceMonitor>();

            // Get internal references via reflection
            if (wfcGenerator != null)
            {
                var boundaryField = wfcGenerator.GetType().GetField("boundaryManager",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (boundaryField != null)
                    boundaryManager = boundaryField.GetValue(wfcGenerator) as BoundaryBufferManager;

                constraintSystem = wfcGenerator.GetHierarchicalConstraintSystem();
            }

            // Subscribe to events
            if (chunkManager != null)
                chunkManager.OnChunkStateChanged += OnChunkStateChanged;
        }

        private void InitializeMetrics()
        {
            boundaryCoherenceScore = 0f;
            boundaryTotalCells = 0;
            boundaryCompatibleCells = 0;

            avgChunkGenerationTime = 0f;
            peakChunkGenerationTime = 0f;
            chunkGenTimes.Clear();

            constraintSatisfactionRate = 0f;
            constraintsApplied = 0;
            constraintsSatisfied = 0;
        }

        private void UpdateBoundaryMetrics()
        {
            if (chunkManager == null || boundaryManager == null)
                return;

            var chunks = chunkManager.GetLoadedChunks();
            int totalBoundaries = 0;
            float totalCoherence = 0f;
            boundaryTotalCells = 0;
            boundaryCompatibleCells = 0;

            foreach (var chunkEntry in chunks)
            {
                Chunk chunk = chunkEntry.Value;

                foreach (var bufferEntry in chunk.BoundaryBuffers)
                {
                    BoundaryBuffer buffer = bufferEntry.Value;

                    if (buffer.AdjacentChunk != null)
                    {
                        var metrics = boundaryManager.CalculateBoundaryMetrics(buffer);

                        if (metrics.CollapsedCells > 0)
                        {
                            totalCoherence += metrics.CoherenceScore;
                            totalBoundaries++;

                            boundaryTotalCells += metrics.TotalCells;
                            boundaryCompatibleCells += metrics.CompatibleCells;
                        }
                    }
                }
            }

            boundaryCoherenceScore = totalBoundaries > 0 ?
                totalCoherence / totalBoundaries : 1f;
        }

        private void UpdateConstraintMetrics()
        {
            if (constraintSystem == null)
                return;

            var stats = constraintSystem.Statistics;

            if (stats.CellsAffected > 0)
            {
                constraintSatisfactionRate = (float)stats.CellsCollapsed / stats.CellsAffected;
                constraintsApplied = stats.CellsAffected;
                constraintsSatisfied = stats.CellsCollapsed;
            }
        }

        private void OnChunkStateChanged(Vector3Int chunkPos, ChunkManager.ChunkLifecycleState oldState, ChunkManager.ChunkLifecycleState newState)
        {
            if (!trackGenerationLatency)
                return;

            if (oldState == ChunkManager.ChunkLifecycleState.Loading &&
                newState == ChunkManager.ChunkLifecycleState.Active)
            {
                // Chunk finished loading
                if (chunkGenTimes.TryGetValue(chunkPos, out float startTime))
                {
                    float genTime = Time.realtimeSinceStartup - startTime;

                    // Record time and update stats
                    peakChunkGenerationTime = Mathf.Max(peakChunkGenerationTime, genTime);

                    // Calculate new average
                    float totalTime = 0f;
                    foreach (var time in chunkGenTimes.Values)
                        totalTime += time;

                    avgChunkGenerationTime = chunkGenTimes.Count > 0 ?
                        totalTime / chunkGenTimes.Count : 0f;
                }
            }
            else if (oldState == ChunkManager.ChunkLifecycleState.None &&
                     newState == ChunkManager.ChunkLifecycleState.Loading)
            {
                // Chunk started loading
                chunkGenTimes[chunkPos] = Time.realtimeSinceStartup;
            }
        }

        // Test methods
        public void RunBoundaryCoherenceTest()
        {
            if (chunkManager == null || wfcGenerator == null)
            {
                Debug.LogError("Cannot run test: ChunkManager or WFCGenerator is missing");
                return;
            }

            Debug.Log("Running boundary coherence test...");

            // Safer alternative to CreateChunksAroundPlayer
            Vector3Int centerChunk = Vector3Int.zero;

            // Try to get viewer position if available
            if (chunkManager.viewer != null)
            {
                Vector3 pos = chunkManager.viewer.position;
                centerChunk = new Vector3Int(
                    Mathf.FloorToInt(pos.x / wfcGenerator.ChunkSize),
                    0, // Ground level
                    Mathf.FloorToInt(pos.z / wfcGenerator.ChunkSize)
                );
            }

            // Create center chunk
            chunkManager.CreateChunkAt(centerChunk);

            // Create surrounding chunks
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && z == 0) continue; // Skip center
                    Vector3Int pos = centerChunk + new Vector3Int(x, 0, z);
                    chunkManager.CreateChunkAt(pos);
                }
            }
        }

        public void RunPerformanceTest()
        {
            if (chunkManager == null || wfcGenerator == null)
            {
                Debug.LogError("Cannot run test: ChunkManager or WFCGenerator is missing");
                return;
            }

            Debug.Log("Running generation performance test...");

            // Clear previous data
            chunkGenTimes.Clear();

            // Create a test grid centered at origin or near viewer
            Vector3Int centerChunk = Vector3Int.zero;

            // Try to get viewer position if available
            if (chunkManager.viewer != null)
            {
                Vector3 pos = chunkManager.viewer.position;
                centerChunk = new Vector3Int(
                    Mathf.FloorToInt(pos.x / wfcGenerator.ChunkSize),
                    0, // Always at ground level
                    Mathf.FloorToInt(pos.z / wfcGenerator.ChunkSize)
                );
            }

            // Determine grid size based on test chunk count
            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(testChunkCount));
            int halfSize = gridSize / 2;

            // Create chunks in a grid pattern
            for (int x = -halfSize; x <= halfSize; x++)
            {
                for (int z = -halfSize; z <= halfSize; z++)
                {
                    if (x * x + z * z <= testChunkCount) // Create in a rough circle
                    {
                        Vector3Int chunkPos = centerChunk + new Vector3Int(x, 0, z);
                        chunkManager.CreateChunkAt(chunkPos);
                    }
                }
            }
        }

        public void RunConstraintTest()
        {
            FindReferences(); // Ensure references are up to date

            if (wfcGenerator == null || constraintSystem == null)
                return;

            Debug.Log("Running constraint satisfaction test...");

            // Force constraint application on existing chunks
            var chunks = chunkManager.GetLoadedChunks();
            int cellsProcessed = 0;

            foreach (var chunkEntry in chunks)
            {
                Chunk chunk = chunkEntry.Value;

                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            Cell cell = chunk.GetCell(x, y, z);

                            if (cell != null && !cell.CollapsedState.HasValue)
                            {
                                constraintSystem.ApplyConstraintsToCell(cell, chunk, wfcGenerator.MaxCellStates);
                                cellsProcessed++;
                            }
                        }
                    }
                }
            }

            Debug.Log($"Applied constraints to {cellsProcessed} cells");
            UpdateConstraintMetrics();
        }

        // Helper for UI
        public Color GetCoherenceColor(float score)
        {
            return Color.Lerp(badBoundaryColor, goodBoundaryColor, score);
        }
    }
}