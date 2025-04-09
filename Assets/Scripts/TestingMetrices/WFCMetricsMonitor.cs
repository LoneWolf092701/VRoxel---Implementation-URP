using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using WFC.Boundary;
using WFC.Core;
using WFC.Generation;
using WFC.Chunking;
using WFC.MarchingCubes;

namespace WFC.Metrics
{
    /// <summary>
    /// Enhanced metrics monitor that collects and saves performance data for the WFC system
    /// </summary>
    public class WFCMetricsMonitor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WFCGenerator wfcGenerator;
        [SerializeField] private ChunkManager chunkManager;
        [SerializeField] private MeshGenerator meshGenerator;

        [Header("Monitoring Settings")]
        [SerializeField] private bool enableMonitoring = true;
        [SerializeField] private bool logMetricsToConsole = true;
        [SerializeField] private bool saveMetricsToFile = true;
        [SerializeField] private string metricsFilePath = "WFC_Metrics.txt";
        [SerializeField] private float monitoringInterval = 5.0f; // seconds between measurements

        [Header("Test Settings")]
        [SerializeField] private int testChunkSize = 16;
        [SerializeField] private int testWorldSizeX = 4;
        [SerializeField] private int testWorldSizeY = 2;
        [SerializeField] private int testWorldSizeZ = 4;

        // Metrics data containers
        private Dictionary<string, float> performanceMetrics = new Dictionary<string, float>();
        private Dictionary<string, int> countMetrics = new Dictionary<string, int>();
        private Dictionary<string, float> ratioMetrics = new Dictionary<string, float>();

        // Measurement timing
        private float lastMeasurementTime = 0f;
        private int measurementCount = 0;

        // References to internal components
        private BoundaryBufferManager boundaryManager;
        private HierarchicalConstraintSystem constraintSystem;
        private DensityFieldGenerator densityFieldGenerator;

        private void Start()
        {
            FindReferences();
            InitializeMetrics();
            lastMeasurementTime = Time.time;
        }

        private void Update()
        {
            if (!enableMonitoring)
                return;

            // Take measurements at interval
            if (Time.time - lastMeasurementTime >= monitoringInterval)
            {
                CollectMetrics();
                lastMeasurementTime = Time.time;
                measurementCount++;

                // Log or save metrics
                if (logMetricsToConsole)
                    LogMetricsToConsole();

                if (saveMetricsToFile && measurementCount % 5 == 0) // Save every 5 measurements
                    SaveMetricsToFile();
            }
        }

        private void FindReferences()
        {
            // Find main components if not assigned
            if (wfcGenerator == null)
                wfcGenerator = FindObjectOfType<WFCGenerator>();

            if (chunkManager == null)
                chunkManager = FindObjectOfType<ChunkManager>();

            if (meshGenerator == null)
                meshGenerator = FindObjectOfType<MeshGenerator>();

            // Get internal references via reflection
            if (wfcGenerator != null)
            {
                // Get boundary manager
                var boundaryField = wfcGenerator.GetType().GetField("boundaryManager",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (boundaryField != null)
                    boundaryManager = boundaryField.GetValue(wfcGenerator) as BoundaryBufferManager;

                // Get constraint system
                constraintSystem = wfcGenerator.GetHierarchicalConstraintSystem();
            }

            // Get density field generator
            if (meshGenerator != null)
            {
                var densityField = meshGenerator.GetType().GetField("densityGenerator",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (densityField != null)
                    densityFieldGenerator = densityField.GetValue(meshGenerator) as DensityFieldGenerator;
            }

            // Subscribe to events
            if (chunkManager != null)
                chunkManager.OnChunkStateChanged += OnChunkStateChanged;
        }

        private void InitializeMetrics()
        {
            // Initialize performance metrics
            performanceMetrics["ChunkGenerationTime"] = 0f;
            performanceMetrics["MeshGenerationTime"] = 0f;
            performanceMetrics["PropagationTime"] = 0f;
            performanceMetrics["FrameTime"] = 0f;
            performanceMetrics["MemoryUsage"] = 0f;
            performanceMetrics["BoundaryCoherence"] = 0f;

            // Initialize count metrics
            countMetrics["LoadedChunks"] = 0;
            countMetrics["ProcessingChunks"] = 0;
            countMetrics["TotalCells"] = 0;
            countMetrics["CollapsedCells"] = 0;
            countMetrics["BoundaryUpdates"] = 0;
            countMetrics["BoundaryConflicts"] = 0;
            countMetrics["TotalVertices"] = 0;
            countMetrics["TotalTriangles"] = 0;
            countMetrics["ConstraintsApplied"] = 0;

            // Initialize ratio metrics
            ratioMetrics["ConstraintSatisfactionRate"] = 0f;
            ratioMetrics["CollapsedCellPercentage"] = 0f;
            ratioMetrics["BoundaryCoherenceRate"] = 0f;
            ratioMetrics["ProcessingEfficiency"] = 0f;
        }

        private void CollectMetrics()
        {
            // Get memory usage
            performanceMetrics["MemoryUsage"] = (float)System.GC.GetTotalMemory(false) / (1024 * 1024); // MB

            // Get frame time
            performanceMetrics["FrameTime"] = Time.deltaTime * 1000f; // ms

            // Get chunk counts
            if (chunkManager != null)
            {
                var chunks = chunkManager.GetLoadedChunks();
                countMetrics["LoadedChunks"] = chunks.Count;

                // Count processing chunks
                countMetrics["ProcessingChunks"] = chunks.Count(c =>
                    GetChunkState(c.Key) == ChunkManager.ChunkLifecycleState.Collapsing ||
                    GetChunkState(c.Key) == ChunkManager.ChunkLifecycleState.Loading);

                // Count total and collapsed cells
                int totalCells = 0;
                int collapsedCells = 0;

                foreach (var chunk in chunks.Values)
                {
                    int chunkCellCount = chunk.Size * chunk.Size * chunk.Size;
                    totalCells += chunkCellCount;

                    // Sample some cells to estimate collapse percentage
                    int sampleSize = Mathf.Min(27, chunkCellCount);
                    int samplesPerDimension = Mathf.CeilToInt(Mathf.Pow(sampleSize, 1f / 3f));
                    float step = chunk.Size / (float)samplesPerDimension;

                    int sampleCollapsed = 0;
                    int samplesChecked = 0;

                    for (int x = 0; x < samplesPerDimension; x++)
                    {
                        for (int y = 0; y < samplesPerDimension; y++)
                        {
                            for (int z = 0; z < samplesPerDimension; z++)
                            {
                                int sampleX = Mathf.Min(chunk.Size - 1, Mathf.FloorToInt(x * step));
                                int sampleY = Mathf.Min(chunk.Size - 1, Mathf.FloorToInt(y * step));
                                int sampleZ = Mathf.Min(chunk.Size - 1, Mathf.FloorToInt(z * step));

                                Cell cell = chunk.GetCell(sampleX, sampleY, sampleZ);
                                if (cell != null && cell.CollapsedState.HasValue)
                                    sampleCollapsed++;

                                samplesChecked++;
                            }
                        }
                    }

                    // Extrapolate to full chunk
                    if (samplesChecked > 0)
                        collapsedCells += Mathf.RoundToInt(chunkCellCount * ((float)sampleCollapsed / samplesChecked));
                }

                countMetrics["TotalCells"] = totalCells;
                countMetrics["CollapsedCells"] = collapsedCells;
                ratioMetrics["CollapsedCellPercentage"] = totalCells > 0 ? (float)collapsedCells / totalCells : 0f;
            }

            // Get boundary metrics
            if (boundaryManager != null && chunkManager != null)
            {
                var chunks = chunkManager.GetLoadedChunks();
                int totalBoundaries = 0;
                float totalCoherence = 0f;
                int boundaryConflicts = 0;

                foreach (var chunkEntry in chunks)
                {
                    Chunk chunk = chunkEntry.Value;
                    foreach (var bufferEntry in chunk.BoundaryBuffers)
                    {
                        BoundaryBuffer buffer = bufferEntry.Value;
                        if (buffer.AdjacentChunk != null)
                        {
                            var metrics = CalculateBoundaryMetrics(buffer);
                            if (metrics.CollapsedCells > 0)
                            {
                                totalCoherence += metrics.CoherenceScore;
                                totalBoundaries++;
                                boundaryConflicts += metrics.ConflictCells;
                            }
                        }
                    }
                }

                performanceMetrics["BoundaryCoherence"] = totalBoundaries > 0 ?
                    totalCoherence / totalBoundaries : 1f;
                countMetrics["BoundaryUpdates"] = totalBoundaries;
                countMetrics["BoundaryConflicts"] = boundaryConflicts;
                ratioMetrics["BoundaryCoherenceRate"] = totalBoundaries > 0 ?
                    1f - ((float)boundaryConflicts / totalBoundaries) : 1f;
            }

            // Get constraint metrics
            if (constraintSystem != null)
            {
                var stats = constraintSystem.Statistics;
                countMetrics["ConstraintsApplied"] = stats.GlobalConstraintsApplied +
                                                      stats.RegionConstraintsApplied +
                                                      stats.LocalConstraintsApplied;

                ratioMetrics["ConstraintSatisfactionRate"] = stats.CellsAffected > 0 ?
                    (float)stats.CellsCollapsed / stats.CellsAffected : 1f;
            }

            // Get mesh metrics
            if (meshGenerator != null)
            {
                int totalVertices = 0;
                int totalTriangles = 0;
                int meshCount = 0;

                // Try to get mesh objects through reflection
                var meshObjectsField = meshGenerator.GetType().GetField("chunkMeshObjects",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (meshObjectsField != null)
                {
                    var meshObjects = meshObjectsField.GetValue(meshGenerator) as Dictionary<Vector3Int, GameObject>;
                    if (meshObjects != null)
                    {
                        foreach (var obj in meshObjects.Values)
                        {
                            if (obj != null)
                            {
                                MeshFilter filter = obj.GetComponent<MeshFilter>();
                                if (filter != null && filter.sharedMesh != null)
                                {
                                    totalVertices += filter.sharedMesh.vertexCount;
                                    totalTriangles += filter.sharedMesh.triangles.Length / 3;
                                    meshCount++;
                                }
                            }
                        }
                    }
                }

                countMetrics["TotalVertices"] = totalVertices;
                countMetrics["TotalTriangles"] = totalTriangles;

                // Get mesh generation time from component timing if available
                if (meshGenerator.GetType().GetField("averageMeshGenerationTime",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) is var field && field != null)
                {
                    performanceMetrics["MeshGenerationTime"] = (float)field.GetValue(meshGenerator) * 1000f; // ms
                }
            }

            // Calculate efficiency metrics
            ratioMetrics["ProcessingEfficiency"] = countMetrics["ProcessingChunks"] > 0 ?
                (float)countMetrics["LoadedChunks"] / countMetrics["ProcessingChunks"] : 1f;
        }

        private void LogMetricsToConsole()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== WFC METRICS ==========");
            sb.AppendLine($"Measurement #{measurementCount} at time {Time.time:F1}s\n");

            sb.AppendLine("PERFORMANCE METRICS:");
            foreach (var metric in performanceMetrics.OrderBy(m => m.Key))
            {
                string unit = metric.Key.Contains("Time") ? "ms" :
                              metric.Key.Contains("Memory") ? "MB" : "";
                sb.AppendLine($"  {metric.Key}: {metric.Value:F2} {unit}");
            }

            sb.AppendLine("\nCOUNT METRICS:");
            foreach (var metric in countMetrics.OrderBy(m => m.Key))
            {
                sb.AppendLine($"  {metric.Key}: {metric.Value}");
            }

            sb.AppendLine("\nRATIO METRICS:");
            foreach (var metric in ratioMetrics.OrderBy(m => m.Key))
            {
                sb.AppendLine($"  {metric.Key}: {metric.Value:P1}");
            }

            sb.AppendLine("=================================");

            Debug.Log(sb.ToString());
        }

        private void SaveMetricsToFile()
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                // Add header if file doesn't exist
                if (!File.Exists(metricsFilePath))
                {
                    sb.Append("Timestamp,");

                    // Add performance metrics headers
                    foreach (var metric in performanceMetrics.Keys.OrderBy(k => k))
                    {
                        sb.Append($"{metric},");
                    }

                    // Add count metrics headers
                    foreach (var metric in countMetrics.Keys.OrderBy(k => k))
                    {
                        sb.Append($"{metric},");
                    }

                    // Add ratio metrics headers
                    foreach (var metric in ratioMetrics.Keys.OrderBy(k => k))
                    {
                        sb.Append($"{metric},");
                    }

                    sb.AppendLine();
                }

                // Add data row
                sb.Append($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")},");

                // Add performance metrics values
                foreach (var metric in performanceMetrics.Keys.OrderBy(k => k))
                {
                    sb.Append($"{performanceMetrics[metric]:F2},");
                }

                // Add count metrics values
                foreach (var metric in countMetrics.Keys.OrderBy(k => k))
                {
                    sb.Append($"{countMetrics[metric]},");
                }

                // Add ratio metrics values
                foreach (var metric in ratioMetrics.Keys.OrderBy(k => k))
                {
                    sb.Append($"{ratioMetrics[metric]:F4},");
                }

                sb.AppendLine();

                // Write to file (append)
                File.AppendAllText(metricsFilePath, sb.ToString());

                Debug.Log($"Metrics saved to {metricsFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving metrics to file: {e.Message}");
            }
        }

        // Event handler for chunk state changes
        private void OnChunkStateChanged(Vector3Int chunkPos, ChunkManager.ChunkLifecycleState oldState, ChunkManager.ChunkLifecycleState newState)
        {
            // Track chunk generation time
            if (oldState == ChunkManager.ChunkLifecycleState.Loading &&
                newState == ChunkManager.ChunkLifecycleState.Active)
            {
                // Chunk finished processing
                // Could add timing logic here
            }
        }

        // Manual test functions
        [ContextMenu("Run Performance Test")]
        public void RunPerformanceTest()
        {
            if (chunkManager == null || wfcGenerator == null)
            {
                Debug.LogError("Cannot run test: ChunkManager or WFCGenerator is missing");
                return;
            }

            Debug.Log("Running performance test...");

            // Force generation of test chunks
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

            // Create a grid of test chunks
            int halfSizeX = testWorldSizeX / 2;
            int halfSizeZ = testWorldSizeZ / 2;

            for (int x = -halfSizeX; x <= halfSizeX; x++)
            {
                for (int y = 0; y < testWorldSizeY; y++)
                {
                    for (int z = -halfSizeZ; z <= halfSizeZ; z++)
                    {
                        Vector3Int chunkPos = centerChunk + new Vector3Int(x, y, z);
                        chunkManager.CreateChunkAt(chunkPos);
                    }
                }
            }

            // Collect and save metrics immediately
            CollectMetrics();
            LogMetricsToConsole();
            SaveMetricsToFile();

            // Schedule periodic collections
            StartCoroutine(CollectTestMetrics());
        }

        private System.Collections.IEnumerator CollectTestMetrics()
        {
            for (int i = 0; i < 10; i++) // Collect 10 samples
            {
                yield return new WaitForSeconds(1.0f);
                CollectMetrics();
                LogMetricsToConsole();
                if (i % 2 == 0) // Save every other sample
                    SaveMetricsToFile();
            }
        }

        [ContextMenu("Save Current Metrics")]
        public void SaveCurrentMetrics()
        {
            CollectMetrics();
            LogMetricsToConsole();
            SaveMetricsToFile();
        }

        [ContextMenu("Reset Metrics")]
        public void ResetMetrics()
        {
            InitializeMetrics();
            measurementCount = 0;
            Debug.Log("Metrics reset");
        }

        // Helper functions

        private ChunkManager.ChunkLifecycleState GetChunkState(Vector3Int chunkPos)
        {
            if (chunkManager == null)
                return ChunkManager.ChunkLifecycleState.None;

            // Use reflection to access the protected method
            var method = chunkManager.GetType().GetMethod("GetChunkState",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            if (method != null)
                return (ChunkManager.ChunkLifecycleState)method.Invoke(chunkManager, new object[] { chunkPos });

            return ChunkManager.ChunkLifecycleState.None;
        }

        private struct BoundaryMetricsData
        {
            public int TotalCells;
            public int CollapsedCells;
            public int CompatibleCells;
            public int ConflictCells;
            public float CoherenceScore;
        }

        private BoundaryMetricsData CalculateBoundaryMetrics(BoundaryBuffer buffer)
        {
            BoundaryMetricsData metrics = new BoundaryMetricsData();

            if (buffer.AdjacentChunk == null || buffer.BoundaryCells.Count == 0)
                return metrics;

            Direction oppositeDir = buffer.Direction.GetOpposite();

            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out var adjacentBuffer))
                return metrics;

            metrics.TotalCells = buffer.BoundaryCells.Count;

            for (int i = 0; i < buffer.BoundaryCells.Count; i++)
            {
                if (i >= adjacentBuffer.BoundaryCells.Count)
                    break;

                Cell cell1 = buffer.BoundaryCells[i];
                Cell cell2 = adjacentBuffer.BoundaryCells[i];

                if (cell1.CollapsedState.HasValue && cell2.CollapsedState.HasValue)
                {
                    metrics.CollapsedCells++;

                    // Check compatibility
                    bool isCompatible = AreStatesCompatible(
                        cell1.CollapsedState.Value,
                        cell2.CollapsedState.Value,
                        buffer.Direction);

                    if (isCompatible)
                        metrics.CompatibleCells++;
                    else
                        metrics.ConflictCells++;
                }
            }

            metrics.CoherenceScore = metrics.CollapsedCells > 0 ?
                (float)metrics.CompatibleCells / metrics.CollapsedCells : 1f;

            return metrics;
        }

        private bool AreStatesCompatible(int stateA, int stateB, Direction direction)
        {
            if (wfcGenerator != null)
                return wfcGenerator.AreStatesCompatible(stateA, stateB, direction);

            // Default compatibility rules as fallback
            if (stateA == stateB)
                return true; // Same states are compatible

            // Default: allow transitions between ground (1) and other states
            if (stateA == 1 || stateB == 1)
                return true;

            return false;
        }
    }
}