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
using System.Collections;
using WFC.Performance;
using WFC.Processing;
using WFC.Configuration;

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
        [SerializeField] private ParallelWFCManager parallelManager;
        [SerializeField] private PerformanceMonitor performanceMonitor;

        [Header("Monitoring Settings")]
        [SerializeField] private bool enableMonitoring = true;
        [SerializeField] private bool logMetricsToConsole = true;
        [SerializeField] private bool saveMetricsToFile = true;
        [SerializeField] private string metricsFilePath = "WFC_Metrics.txt";
        [SerializeField] private float monitoringInterval = 5.0f; // seconds between measurements

        [Header("Test Settings")]
        [SerializeField] private int testChunkSize = 16;
        [SerializeField] private int[] testChunkSizes = new int[] { 8, 16, 32, 64 };
        [SerializeField] private int[] testThreadCounts = new int[] { 1, 2, 4, 8 };
        [SerializeField]
        private Vector3Int[] testWorldSizes = new Vector3Int[] {
            new Vector3Int(4, 1, 4),
            new Vector3Int(8, 1, 8),
            new Vector3Int(16, 1, 16),
            new Vector3Int(32, 1, 32)
        };
        private bool[,,] internalAdjacencyRules;
        private int internalMaxStates = 10;

        [Header("Test Results")]
        // Chunk Generation Performance
        public List<ChunkGenerationResult> chunkGenerationResults = new List<ChunkGenerationResult>();

        // Boundary Coherence Performance
        public List<BoundaryCoherenceResult> boundaryCoherenceResults = new List<BoundaryCoherenceResult>();

        // Mesh Generation Performance
        public List<MeshGenerationResult> meshGenerationResults = new List<MeshGenerationResult>();

        // Parallel Processing Efficiency
        public List<ParallelProcessingResult> parallelProcessingResults = new List<ParallelProcessingResult>();

        // World Size Scaling
        public List<WorldSizeScalingResult> worldSizeScalingResults = new List<WorldSizeScalingResult>();

        // LOD Performance Impact
        public List<LODPerformanceResult> lodPerformanceResults = new List<LODPerformanceResult>();

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
        private ParallelWFCProcessor parallelProcessor;

        // Testing state
        private bool isTestRunning = false;
        private TestType currentTestType = TestType.None;
        private int currentTestIndex = 0;

        private Stopwatch testStopwatch = new Stopwatch();

        // Default adjacency rules for when WFCGenerator is not available
        private bool[,,] defaultAdjacencyRules;

        // Test types enum
        public enum TestType
        {
            None,
            ChunkGeneration,
            BoundaryCoherence,
            MeshGeneration,
            ParallelProcessing,
            WorldSizeScaling,
            LODPerformance
        }

        // Define result structs for each test type
        [System.Serializable]
        public struct ChunkGenerationResult
        {
            public int chunkSize;
            public float processingTime;
            public float memoryUsage;
            public float cellsCollapsedPercent;
            public int propagationEvents;
            public int iterationsRequired;
        }

        [System.Serializable]
        public struct BoundaryCoherenceResult
        {
            public Vector3Int worldSize;
            public int boundaryUpdates;
            public int bufferSynchronizations;
            public int conflictsDetected;
            public int conflictsResolved;
            public float coherenceScore;
        }

        [System.Serializable]
        public struct MeshGenerationResult
        {
            public int chunkSize;
            public float densityFieldGenerationTime;
            public float marchingCubesTime;
            public float totalMeshTime;
            public int vertices;
            public int triangles;
            public float memoryUsage;
        }

        [System.Serializable]
        public struct ParallelProcessingResult
        {
            public int threadCount;
            public float processingTime;
            public float speedupFactor;
            public float synchronizationOverhead;
            public float memoryOverheadPercent;
            public int maxConcurrentChunks;
        }

        [System.Serializable]
        public struct WorldSizeScalingResult
        {
            public Vector3Int worldSize;
            public float totalMemoryUsage;
            public float generationTime;
            public float fpsImpact;
            public int chunksLoaded;
            public float chunksProcessedPerFrame;
            public float loadingDistance;
        }

        [System.Serializable]
        public struct LODPerformanceResult
        {
            public int lodLevel;
            public float distanceRange;
            public float vertexReductionPercent;
            public float generationSpeedIncreasePercent;
            public float memoryReductionPercent;
            public int visualQualityImpact;
        }

        private void Awake()
        {
            // Initialize default adjacency rules for fallback
            InitializeDefaultAdjacencyRules();

            // Initialize internal adjacency rules
            InitializeInternalAdjacencyRules();
        }

        private void Start()
        {
            FindReferences();
            EnsureWFCGenerator();
            InitializeMetrics();
            lastMeasurementTime = Time.time;
        }

        public void InitializeDefaultAdjacencyRules()
        {
            int maxStates = 10; // Reasonable default if config not available
            if (WFCConfigManager.Config != null)
            {
                maxStates = WFCConfigManager.Config.World.maxStates;
            }

            defaultAdjacencyRules = new bool[maxStates, maxStates, 6]; // 6 directions

            // Initialize with simple adjacency rules
            // Rule 1: Same states can be adjacent
            for (int i = 0; i < maxStates; i++)
            {
                for (int d = 0; d < 6; d++)
                {
                    defaultAdjacencyRules[i, i, d] = true;
                }
            }

            // Rule 2: State 0 (air) can be adjacent to any state
            for (int i = 0; i < maxStates; i++)
            {
                for (int d = 0; d < 6; d++)
                {
                    defaultAdjacencyRules[0, i, d] = true;
                    defaultAdjacencyRules[i, 0, d] = true;
                }
            }

            // Rule 3: Neighboring states can be adjacent
            for (int i = 0; i < maxStates - 1; i++)
            {
                for (int d = 0; d < 6; d++)
                {
                    defaultAdjacencyRules[i, i + 1, d] = true;
                    defaultAdjacencyRules[i + 1, i, d] = true;
                }
            }
        }

        private void Update()
        {
            if (!enableMonitoring)
                return;

            // Take measurements at interval when not running tests
            if (!isTestRunning && Time.time - lastMeasurementTime >= monitoringInterval)
            {
                CollectMetrics();
                lastMeasurementTime = Time.time;
                measurementCount++;

                // Log or save metrics
                if (logMetricsToConsole)
                    LogMetricsToConsole();

                // Save every 5 measurements
                if (saveMetricsToFile && measurementCount % 5 == 0) 
                    SaveMetricsToFile();
            }

            // Handle ongoing test runs
            if (isTestRunning)
            {
                UpdateTestProgress();
            }
        }

        private void FindReferences()
        {
            // Find main components if not assigned
            if (wfcGenerator == null)
            {
                wfcGenerator = FindObjectOfType<WFCGenerator>(true);
                Debug.Log($"Found WFCGenerator: {(wfcGenerator != null ? "Yesss :)" : "No!")}");
            }

            if (chunkManager == null)
                chunkManager = FindAnyObjectByType<ChunkManager>();

            if (meshGenerator == null)
                meshGenerator = FindAnyObjectByType<MeshGenerator>();

            if (parallelManager == null)
                parallelManager = FindAnyObjectByType<ParallelWFCManager>();

            if (performanceMonitor == null)
                performanceMonitor = FindAnyObjectByType<PerformanceMonitor>();

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

            // Get parallel processor
            if (parallelManager != null)
            {
                parallelProcessor = parallelManager.GetParallelProcessor();
            }

            // Subscribe to events if chunkManager is available
            if (chunkManager != null)
            {
                // Use reflection to access the event
                var eventInfo = chunkManager.GetType().GetEvent("OnChunkStateChanged");
                if (eventInfo != null)
                {
                    // Create a delegate
                    var method = this.GetType().GetMethod("OnChunkStateChanged",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                    if (method != null)
                    {
                        var eventDelegate = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, method);
                        eventInfo.AddEventHandler(chunkManager, eventDelegate);
                    }
                }

                if (WFCConfigManager.Config == null)
                {
                    Debug.LogWarning("WFCConfigManager.Config is null");
                    // Try to find and initialize it if possible
                    var configManager = FindObjectOfType<WFCConfigManager>(true);
                }
            }
        }
        public void SetDependencies(WFCGenerator generator, ChunkManager manager, MeshGenerator meshGen, ParallelWFCManager parallel, PerformanceMonitor perfMon)
        {
            wfcGenerator = generator;
            chunkManager = manager;
            meshGenerator = meshGen;
            parallelManager = parallel;
            performanceMonitor = perfMon;

            Debug.Log("Dependencies manually set");
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

                performanceMetrics["BoundaryCoherence"] = totalBoundaries > 0 ? totalCoherence / totalBoundaries : 1f;
                countMetrics["BoundaryUpdates"] = totalBoundaries;
                countMetrics["BoundaryConflicts"] = boundaryConflicts;
                ratioMetrics["BoundaryCoherenceRate"] = totalBoundaries > 0 ? 1f - ((float)boundaryConflicts / totalBoundaries) : 1f;
            }

            // Get constraint metrics
            if (constraintSystem != null)
            {
                var stats = constraintSystem.Statistics;
                countMetrics["ConstraintsApplied"] = stats.GlobalConstraintsApplied + stats.RegionConstraintsApplied + stats.LocalConstraintsApplied;

                ratioMetrics["ConstraintSatisfactionRate"] = stats.CellsAffected > 0 ? (float)stats.CellsCollapsed / stats.CellsAffected : 1f;
            }

            // Get mesh metrics
            if (meshGenerator != null)
            {
                int totalVertices = 0;
                int totalTriangles = 0;
                int meshCount = 0;

                // Try to get mesh objects through reflection
                var meshObjectsField = meshGenerator.GetType().GetField("chunkMeshObjects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

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
                if (meshGenerator.GetType().GetField("averageMeshGenerationTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) is var field && field != null)
                {
                    performanceMetrics["MeshGenerationTime"] = (float)field.GetValue(meshGenerator) * 1000f; // ms
                }
            }

            // Calculate efficiency metrics
            ratioMetrics["ProcessingEfficiency"] = countMetrics["ProcessingChunks"] > 0 ? (float)countMetrics["LoadedChunks"] / countMetrics["ProcessingChunks"] : 1f;
        }

        #region Test Methods

        // Run all tests in sequence
        //public void RunAllTests()
        //{
        //    StartCoroutine(RunAllTestsCoroutine());
        //}
        public void InitializeInternalAdjacencyRules()
        {
            internalMaxStates = 10;
            if (WFCConfigManager.Config != null && WFCConfigManager.Config.World != null)
            {
                internalMaxStates = WFCConfigManager.Config.World.maxStates;
            }

            internalAdjacencyRules = new bool[internalMaxStates, internalMaxStates, 6]; // 6 directions

            // Initialize all to false
            for (int i = 0; i < internalMaxStates; i++)
                for (int j = 0; j < internalMaxStates; j++)
                    for (int d = 0; d < 6; d++)
                        internalAdjacencyRules[i, j, d] = false;

            // Set basic rules
            // Rule 1: Same states can be adjacent
            for (int i = 0; i < internalMaxStates; i++)
            {
                for (int d = 0; d < 6; d++)
                {
                    internalAdjacencyRules[i, i, d] = true;
                }
            }

            // Rule 2: State 0 (air) can be adjacent to any state
            for (int i = 0; i < internalMaxStates; i++)
            {
                for (int d = 0; d < 6; d++)
                {
                    internalAdjacencyRules[0, i, d] = true;
                    internalAdjacencyRules[i, 0, d] = true;
                }
            }

            // Rule 3: Neighboring states can be adjacent
            for (int i = 0; i < internalMaxStates - 1; i++)
            {
                for (int d = 0; d < 6; d++)
                {
                    internalAdjacencyRules[i, i + 1, d] = true;
                    internalAdjacencyRules[i + 1, i, d] = true;
                }
            }
        }
        private IEnumerator RunAllTestsCoroutine()
        {
            // Clear previous results
            ClearAllTestResults();

            // Run chunk generation test
            yield return StartCoroutine(RunChunkGenerationTest());
            yield return new WaitForSeconds(2.0f);

            // Run boundary coherence test
            yield return StartCoroutine(RunBoundaryCoherenceTest());
            yield return new WaitForSeconds(2.0f);

            // Run mesh generation test
            yield return StartCoroutine(RunMeshGenerationTest());
            yield return new WaitForSeconds(2.0f);

            // Run parallel processing test
            yield return StartCoroutine(RunParallelProcessingTest());
            yield return new WaitForSeconds(2.0f);

            // Run world size scaling test
            yield return StartCoroutine(RunWorldSizeScalingTest());
            yield return new WaitForSeconds(2.0f);

            // Run LOD performance test
            yield return StartCoroutine(RunLODPerformanceTest());

            Debug.Log("All tests completed!");
            SaveAllTestResults();
        }

        private void ClearAllTestResults()
        {
            chunkGenerationResults.Clear();
            boundaryCoherenceResults.Clear();
            meshGenerationResults.Clear();
            parallelProcessingResults.Clear();
            worldSizeScalingResults.Clear();
            lodPerformanceResults.Clear();
        }

        private void SaveAllTestResults()
        {
            SaveTestResults("ChunkGenerationResults.csv", FormatChunkGenerationResults());
            SaveTestResults("BoundaryCoherenceResults.csv", FormatBoundaryCoherenceResults());
            SaveTestResults("MeshGenerationResults.csv", FormatMeshGenerationResults());
            SaveTestResults("ParallelProcessingResults.csv", FormatParallelProcessingResults());
            SaveTestResults("WorldSizeScalingResults.csv", FormatWorldSizeScalingResults());
            SaveTestResults("LODPerformanceResults.csv", FormatLODPerformanceResults());
        }

        private void SaveTestResults(string filename, string content)
        {
            string path = Path.Combine(Application.persistentDataPath, filename);
            File.WriteAllText(path, content);
            Debug.Log($"Test results saved to: {path}");
        }

        #region Formatting Results for CSV

        private string FormatChunkGenerationResults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ChunkSize,ProcessingTime(ms),MemoryUsage(MB),CellsCollapsedPercent,PropagationEvents,IterationsRequired");

            foreach (var result in chunkGenerationResults)
            {
                sb.AppendLine($"{result.chunkSize},{result.processingTime:F2},{result.memoryUsage:F2},{result.cellsCollapsedPercent:F2},{result.propagationEvents},{result.iterationsRequired}");
            }

            return sb.ToString();
        }

        private string FormatBoundaryCoherenceResults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("WorldSize,BoundaryUpdates,BufferSynchronizations,ConflictsDetected,ConflictsResolved,CoherenceScore");

            foreach (var result in boundaryCoherenceResults)
            {
                sb.AppendLine($"{result.worldSize.x}x{result.worldSize.y}x{result.worldSize.z},{result.boundaryUpdates},{result.bufferSynchronizations},{result.conflictsDetected},{result.conflictsResolved},{result.coherenceScore:F2}");
            }

            return sb.ToString();
        }

        private string FormatMeshGenerationResults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ChunkSize,DensityFieldGeneration(ms),MarchingCubes(ms),TotalMeshTime(ms),Vertices,Triangles,MemoryUsage(MB)");

            foreach (var result in meshGenerationResults)
            {
                sb.AppendLine($"{result.chunkSize},{result.densityFieldGenerationTime:F2},{result.marchingCubesTime:F2},{result.totalMeshTime:F2},{result.vertices},{result.triangles},{result.memoryUsage:F2}");
            }

            return sb.ToString();
        }

        private string FormatParallelProcessingResults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ThreadCount,ProcessingTime(ms),SpeedupFactor,SynchronizationOverhead(ms),MemoryOverheadPercent,MaxConcurrentChunks");

            foreach (var result in parallelProcessingResults)
            {
                sb.AppendLine($"{result.threadCount},{result.processingTime:F2},{result.speedupFactor:F2},{result.synchronizationOverhead:F2},{result.memoryOverheadPercent:F2},{result.maxConcurrentChunks}");
            }

            return sb.ToString();
        }

        private string FormatWorldSizeScalingResults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("WorldSize,TotalMemoryUsage(MB),GenerationTime(s),FPSImpact,ChunksLoaded,ChunksProcessedPerFrame,LoadingDistance");

            foreach (var result in worldSizeScalingResults)
            {
                sb.AppendLine($"{result.worldSize.x}x{result.worldSize.y}x{result.worldSize.z},{result.totalMemoryUsage:F2},{result.generationTime:F2},{result.fpsImpact:F2},{result.chunksLoaded},{result.chunksProcessedPerFrame:F2},{result.loadingDistance:F2}");
            }

            return sb.ToString();
        }

        private string FormatLODPerformanceResults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("LODLevel,DistanceRange,VertexReductionPercent,GenerationSpeedIncreasePercent,MemoryReductionPercent,VisualQualityImpact");

            foreach (var result in lodPerformanceResults)
            {
                sb.AppendLine($"{result.lodLevel},{result.distanceRange:F0},{result.vertexReductionPercent:F2},{result.generationSpeedIncreasePercent:F2},{result.memoryReductionPercent:F2},{result.visualQualityImpact}");
            }

            return sb.ToString();
        }

        #endregion

        #region Chunk Generation Test

        public IEnumerator RunChunkGenerationTest()
        {
            Debug.Log("Running Chunk Generation Test...");
            isTestRunning = true;
            currentTestType = TestType.ChunkGeneration;
            currentTestIndex = 0;

            // Important: Create a new list if null
            if (chunkGenerationResults == null)
                chunkGenerationResults = new List<ChunkGenerationResult>();

            chunkGenerationResults.Clear(); // Start fresh

            // Run tests for each chunk size
            foreach (int chunkSize in testChunkSizes)
            {
                yield return StartCoroutine(TestChunkGeneration(chunkSize));
                currentTestIndex++;

                // Add debug log to verify each test completed and results added
                Debug.Log($"Chunk generation test completed for size {chunkSize}. Results count: {chunkGenerationResults.Count}");

                yield return new WaitForSeconds(1.0f);
            }

            Debug.Log($"Chunk Generation Test completed with {chunkGenerationResults.Count} results!");
            isTestRunning = false;
            currentTestType = TestType.None;
        }

        public IEnumerator TestChunkGeneration(int chunkSize)
        {
            Debug.Log($"Testing chunk generation for size: {chunkSize}");

            // Create a test chunk
            Vector3Int chunkPos = new Vector3Int(0, 0, 0);
            Chunk chunk = new Chunk(chunkPos, chunkSize);

            // Initialize chunk cells
            int maxStates = WFCConfigManager.Config != null && WFCConfigManager.Config.World != null ?
                WFCConfigManager.Config.World.maxStates : 10;
            var possibleStates = Enumerable.Range(0, maxStates);
            chunk.InitializeCells(possibleStates);

            // Prepare to measure performance
            float memoryBefore = (float)GC.GetTotalMemory(true) / (1024 * 1024);
            int propagationEventCount = 0;
            int iterations = 0;
            bool madeProgress = true;
            Exception caughtException = null;

            // Start timing
            testStopwatch = new Stopwatch();
            testStopwatch.Start();

            // Perform WFC algorithm on the chunk with manual iteration to count steps
            while (madeProgress && iterations < 1000) // Limit iterations to prevent infinite loops
            {
                try
                {
                    madeProgress = CollapseNextCell(chunk, ref propagationEventCount);
                    iterations++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Error during collapse: {e.Message}");
                    caughtException = e; // Store the exception
                    break;
                }

                // Yield every few iterations to prevent freezing
                if (iterations % 10 == 0)
                {
                    yield return null;
                }
            }

            // Stop timing
            testStopwatch.Stop();
            float processingTime = testStopwatch.ElapsedMilliseconds;

            // Calculate memory usage and collapsed cell percentage
            float memoryAfter = (float)GC.GetTotalMemory(false) / (1024 * 1024);
            float memoryUsage = memoryAfter - memoryBefore;
            float collapsedCellPercentage = CalculateCollapsedPercentage(chunk) * 100f;

            // Store result
            ChunkGenerationResult result = new ChunkGenerationResult
            {
                chunkSize = chunkSize,
                processingTime = processingTime,
                memoryUsage = memoryUsage,
                cellsCollapsedPercent = collapsedCellPercentage,
                propagationEvents = propagationEventCount,
                iterationsRequired = iterations
            };

            // Ensure this result gets added even if previous results exist
            chunkGenerationResults.Add(result);
            Debug.Log($"Chunk generation test for size {chunkSize} completed: {processingTime:F2}ms, {collapsedCellPercentage:F2}% cells collapsed");

            //Re-thow the exception
            if (caughtException != null)
            {
                throw caughtException;
            }
        }

        private bool CollapseNextCell(Chunk chunk, ref int propagationEventCount)
        {
            // Find the cell with lowest entropy
            Cell cellToCollapse = null;
            int lowestEntropy = int.MaxValue;

            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);

                        // Skip already collapsed cells
                        if (cell.CollapsedState.HasValue)
                            continue;

                        // Skip cells with only one state
                        if (cell.PossibleStates.Count <= 1)
                            continue;

                        if (cell.Entropy < lowestEntropy)
                        {
                            lowestEntropy = cell.Entropy;
                            cellToCollapse = cell;
                        }
                    }
                }
            }

            // If found a cell, collapse it
            if (cellToCollapse != null)
            {
                int[] possibleStates = cellToCollapse.PossibleStates.ToArray();
                int randomState = possibleStates[UnityEngine.Random.Range(0, possibleStates.Length)];

                cellToCollapse.Collapse(randomState);

                // Count as a propagation event
                propagationEventCount++;

                // Simple propagation to neighboring cells
                PropagateCollapse(cellToCollapse, chunk, ref propagationEventCount);

                return true;
            }

            return false;
        }

        private void PropagateCollapse(Cell cell, Chunk chunk, ref int propagationEventCount)
        {
            // Find cell position in chunk
            Vector3Int? cellPos = FindCellPosition(cell, chunk);
            if (!cellPos.HasValue)
                return;

            int state = cell.CollapsedState.Value;

            // Check each direction
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                // Calculate neighbor position
                Vector3Int offset = dir.ToVector3Int();
                Vector3Int neighborPos = cellPos.Value + offset;

                // Check if neighbor is within chunk
                if (neighborPos.x >= 0 && neighborPos.x < chunk.Size &&
                    neighborPos.y >= 0 && neighborPos.y < chunk.Size &&
                    neighborPos.z >= 0 && neighborPos.z < chunk.Size)
                {
                    // Get neighbor cell and apply constraint
                    Cell neighbor = chunk.GetCell(neighborPos.x, neighborPos.y, neighborPos.z);
                    bool changed = ApplyConstraint(neighbor, state, dir);

                    // If neighbor changed, count as propagation event
                    if (changed)
                        propagationEventCount++;
                }
            }
        }

        private bool ApplyConstraint(Cell cell, int constraintState, Direction direction)
        {
            // Skip if already collapsed
            if (cell.CollapsedState.HasValue)
                return false;

            // Store old states for checking if changed
            HashSet<int> oldStates = new HashSet<int>(cell.PossibleStates);

            // Find compatible states based on adjacency rules
            HashSet<int> compatibleStates = new HashSet<int>();

            foreach (int targetState in cell.PossibleStates)
            {
                bool isCompatible = AreStatesCompatible(targetState, constraintState, direction);

                if (isCompatible)
                {
                    compatibleStates.Add(targetState);
                }
            }

            // Update possible states if needed
            if (compatibleStates.Count > 0 && !compatibleStates.SetEquals(oldStates))
            {
                cell.SetPossibleStates(compatibleStates);
                return true;
            }

            return false;
        }

        #endregion

        #region Boundary Coherence Test

        public IEnumerator RunBoundaryCoherenceTest()
        {
            UnityEngine.Debug.Log("Running Boundary Coherence Test...");
            isTestRunning = true;
            currentTestType = TestType.BoundaryCoherence;
            currentTestIndex = 0;

            boundaryCoherenceResults.Clear();

            // Run tests for world sizes 2x2x1, 3x3x1, 4x4x1
            Vector3Int[] worldSizes = new Vector3Int[] {
                new Vector3Int(2, 1, 2),
                new Vector3Int(2, 2, 2),
                new Vector3Int(3, 3, 3),
                new Vector3Int(4, 4, 4)
            };

            foreach (var worldSize in worldSizes)
            {
                yield return StartCoroutine(TestBoundaryCoherence(worldSize));
                currentTestIndex++;

                // Wait between tests
                yield return new WaitForSeconds(2.0f);
            }

            UnityEngine.Debug.Log("Boundary Coherence Test completed!");
            isTestRunning = false;
            currentTestType = TestType.None;
        }

        public IEnumerator TestBoundaryCoherence(Vector3Int worldSize)
        {
            Debug.Log($"Testing boundary coherence for world size: {worldSize}");

            Dictionary<Vector3Int, Chunk> testChunks = new Dictionary<Vector3Int, Chunk>();
            int boundaryUpdates = 0;
            int bufferSynchronizations = 0;
            int conflictsDetected = 0;
            int conflictsResolved = 0;
            bool testSuccessful = true;
            string errorMessage = null;

            try
            {
                // Create a test world with specified dimensions
                // Create chunks
                for (int x = 0; x < worldSize.x; x++)
                {
                    for (int y = 0; y < worldSize.y; y++)
                    {
                        for (int z = 0; z < worldSize.z; z++)
                        {
                            Vector3Int chunkPos = new Vector3Int(x, y, z);
                            Chunk chunk = new Chunk(chunkPos, testChunkSize);

                            // Initialize with all possible states
                            int maxStates = 10; // Fallback
                            if (WFCConfigManager.Config != null && WFCConfigManager.Config.World != null)
                                maxStates = WFCConfigManager.Config.World.maxStates;

                            var possibleStates = Enumerable.Range(0, maxStates);
                            chunk.InitializeCells(possibleStates);

                            testChunks[chunkPos] = chunk;
                        }
                    }
                }

                // Connect neighboring chunks
                foreach (var entry in testChunks)
                {
                    Vector3Int pos = entry.Key;
                    Chunk chunk = entry.Value;

                    foreach (Direction dir in Enum.GetValues(typeof(Direction)))
                    {
                        Vector3Int neighborPos = pos + dir.ToVector3Int();
                        if (testChunks.TryGetValue(neighborPos, out Chunk neighbor))
                        {
                            // Connect chunks both ways
                            chunk.Neighbors[dir] = neighbor;
                            neighbor.Neighbors[dir.GetOpposite()] = chunk;
                        }
                    }
                }

                // Create and initialize boundary buffers
                foreach (var chunk in testChunks.Values)
                {
                    InitializeBoundaryBuffers(chunk);
                }

                // Collapse some cells to create boundaries
                int cellsCollapsed = 0;
                // Partially collapse chunks to create boundaries
                foreach (var chunk in testChunks.Values)
                {
                    // Collapse 20% of cells randomly
                    int cellsToCollapse = (chunk.Size * chunk.Size * chunk.Size) / 5;
                    for (int i = 0; i < cellsToCollapse; i++)
                    {
                        int x = UnityEngine.Random.Range(0, chunk.Size);
                        int y = UnityEngine.Random.Range(0, chunk.Size);
                        int z = UnityEngine.Random.Range(0, chunk.Size);

                        Cell cell = chunk.GetCell(x, y, z);
                        if (cell != null && !cell.CollapsedState.HasValue)
                        {
                            // Collapse to a random state
                            int[] possibleStates = cell.PossibleStates.ToArray();
                            int randomState = possibleStates[UnityEngine.Random.Range(0, possibleStates.Length)];
                            cell.Collapse(randomState);
                            cellsCollapsed += 1;

                            // If this is a boundary cell, update boundary
                            if (cell.IsBoundary && cell.BoundaryDirection.HasValue)
                            {
                                boundaryUpdates++;
                                bufferSynchronizations++;

                                // Handle boundary conflicts
                                if (IsBoundaryConflict(cell, chunk))
                                {
                                    conflictsDetected++;

                                    // Try to resolve conflict
                                    if (ResolveBoundaryConflict(cell, chunk))
                                    {
                                        conflictsResolved++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                testSuccessful = false;
                errorMessage = $"Error in boundary test: {e.Message}";
                Debug.LogError(errorMessage);
            }

            // Calculate boundary coherence score if the test was successful
            float coherenceScore = -1f;
            if (testSuccessful)
            {
                coherenceScore = CalculateBoundaryCoherenceScore(testChunks.Values);
            }

            // Store results
            BoundaryCoherenceResult result = new BoundaryCoherenceResult
            {
                worldSize = worldSize,
                boundaryUpdates = boundaryUpdates,
                bufferSynchronizations = bufferSynchronizations,
                conflictsDetected = conflictsDetected,
                conflictsResolved = conflictsResolved,
                coherenceScore = coherenceScore
            };

            if (boundaryCoherenceResults == null)
                boundaryCoherenceResults = new List<BoundaryCoherenceResult>();

            boundaryCoherenceResults.Add(result);

            Debug.Log($"Boundary coherence test for {worldSize} ADDED TO RESULTS. Count: {boundaryCoherenceResults.Count}. Success: {testSuccessful}, Coherence Score: {coherenceScore:F2}");

            yield return null;
        }


        private void InitializeBoundaryBuffers(Chunk chunk)
        {
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                if (!chunk.Neighbors.ContainsKey(dir))
                    continue;

                Chunk neighbor = chunk.Neighbors[dir];

                // Create buffer for this boundary
                BoundaryBuffer buffer = new BoundaryBuffer(dir, chunk);
                buffer.AdjacentChunk = neighbor;

                // Get boundary cells based on direction
                List<Cell> boundaryCells = GetBoundaryCells(chunk, dir);
                buffer.BoundaryCells = boundaryCells;

                // Mark all boundary cells
                foreach (var cell in boundaryCells)
                {
                    cell.IsBoundary = true;
                    cell.BoundaryDirection = dir;
                }

                // Create buffer cells
                List<Cell> bufferCells = new List<Cell>();
                for (int i = 0; i < boundaryCells.Count; i++)
                {
                    Cell bufferCell = new Cell(new Vector3Int(-1, -1, -1), boundaryCells[i].PossibleStates);
                    bufferCells.Add(bufferCell);
                }
                buffer.BufferCells = bufferCells;

                // Add to chunk's boundary buffers
                chunk.BoundaryBuffers[dir] = buffer;
            }

            // Synchronize buffers after initialization
            foreach (var buffer in chunk.BoundaryBuffers.Values)
            {
                SynchronizeBuffer(buffer);
            }
        }

        private List<Cell> GetBoundaryCells(Chunk chunk, Direction direction)
        {
            List<Cell> cells = new List<Cell>();
            int size = chunk.Size;

            // Based on direction, get cells at the boundary face
            switch (direction)
            {
                case Direction.Left: // X = 0
                    for (int y = 0; y < size; y++)
                        for (int z = 0; z < size; z++)
                            cells.Add(chunk.GetCell(0, y, z));
                    break;

                case Direction.Right: // X = size-1
                    for (int y = 0; y < size; y++)
                        for (int z = 0; z < size; z++)
                            cells.Add(chunk.GetCell(size - 1, y, z));
                    break;

                case Direction.Down: // Y = 0
                    for (int x = 0; x < size; x++)
                        for (int z = 0; z < size; z++)
                            cells.Add(chunk.GetCell(x, 0, z));
                    break;

                case Direction.Up: // Y = size-1
                    for (int x = 0; x < size; x++)
                        for (int z = 0; z < size; z++)
                            cells.Add(chunk.GetCell(x, size - 1, z));
                    break;

                case Direction.Back: // Z = 0
                    for (int x = 0; x < size; x++)
                        for (int y = 0; y < size; y++)
                            cells.Add(chunk.GetCell(x, y, 0));
                    break;

                case Direction.Forward: // Z = size-1
                    for (int x = 0; x < size; x++)
                        for (int y = 0; y < size; y++)
                            cells.Add(chunk.GetCell(x, y, size - 1));
                    break;
            }

            return cells;
        }

        private void SynchronizeBuffer(BoundaryBuffer buffer)
        {
            if (buffer.AdjacentChunk == null)
                return;

            Direction oppositeDir = buffer.Direction.GetOpposite();

            // Get adjacent buffer
            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return;

            // For each cell at the boundary
            for (int i = 0; i < buffer.BoundaryCells.Count && i < adjacentBuffer.BoundaryCells.Count; i++)
            {
                Cell cell1 = buffer.BoundaryCells[i];
                Cell cell2 = adjacentBuffer.BoundaryCells[i];

                // Update buffer cells to reflect adjacent boundary cells
                HashSet<int> adjacentStates = new HashSet<int>(cell2.PossibleStates);
                buffer.BufferCells[i].SetPossibleStates(adjacentStates);

                HashSet<int> localStates = new HashSet<int>(cell1.PossibleStates);
                adjacentBuffer.BufferCells[i].SetPossibleStates(localStates);
            }
        }

        private bool IsBoundaryConflict(Cell cell, Chunk chunk)
        {
            if (!cell.IsBoundary || !cell.BoundaryDirection.HasValue || !cell.CollapsedState.HasValue)
                return false;

            Direction dir = cell.BoundaryDirection.Value;

            if (!chunk.BoundaryBuffers.TryGetValue(dir, out BoundaryBuffer buffer) ||
                buffer.AdjacentChunk == null)
                return false;

            // Find this cell in the boundary array
            int index = buffer.BoundaryCells.IndexOf(cell);
            if (index == -1)
                return false;

            Direction oppositeDir = dir.GetOpposite();

            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return false;

            // Check if adjacent cell is collapsed and incompatible
            if (index < adjacentBuffer.BoundaryCells.Count)
            {
                Cell adjacentCell = adjacentBuffer.BoundaryCells[index];
                if (adjacentCell.CollapsedState.HasValue)
                {
                    bool compatible = AreStatesCompatible(
                        cell.CollapsedState.Value,
                        adjacentCell.CollapsedState.Value,
                        dir);

                    return !compatible;
                }
            }

            return false;
        }

        private bool ResolveBoundaryConflict(Cell cell, Chunk chunk)
        {
            if (!cell.IsBoundary || !cell.BoundaryDirection.HasValue || !cell.CollapsedState.HasValue)
                return false;

            Direction dir = cell.BoundaryDirection.Value;

            if (!chunk.BoundaryBuffers.TryGetValue(dir, out BoundaryBuffer buffer) ||
                buffer.AdjacentChunk == null)
                return false;

            // Find this cell in the boundary array
            int index = buffer.BoundaryCells.IndexOf(cell);
            if (index == -1)
                return false;

            Direction oppositeDir = dir.GetOpposite();

            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return false;

            // Reset adjacent cell to compatible state
            if (index < adjacentBuffer.BoundaryCells.Count)
            {
                Cell adjacentCell = adjacentBuffer.BoundaryCells[index];
                if (adjacentCell.CollapsedState.HasValue)
                {
                    // Find a compatible state
                    int compatibleState = cell.CollapsedState.Value;
                    adjacentCell.Collapse(compatibleState);
                    return true;
                }
            }

            return false;
        }

        private float CalculateBoundaryCoherenceScore(IEnumerable<Chunk> chunks)
        {
            int totalBoundaryCells = 0;
            int compatibleCells = 0;

            foreach (var chunk in chunks)
            {
                foreach (var bufferEntry in chunk.BoundaryBuffers)
                {
                    BoundaryBuffer buffer = bufferEntry.Value;
                    if (buffer.AdjacentChunk == null)
                        continue;

                    Direction oppositeDir = buffer.Direction.GetOpposite();

                    if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                        continue;

                    for (int i = 0; i < buffer.BoundaryCells.Count && i < adjacentBuffer.BoundaryCells.Count; i++)
                    {
                        Cell cell1 = buffer.BoundaryCells[i];
                        Cell cell2 = adjacentBuffer.BoundaryCells[i];

                        if (cell1.CollapsedState.HasValue && cell2.CollapsedState.HasValue)
                        {
                            totalBoundaryCells++;

                            bool compatible = AreStatesCompatible(
                                cell1.CollapsedState.Value,
                                cell2.CollapsedState.Value,
                                buffer.Direction);

                            if (compatible)
                                compatibleCells++;
                        }
                    }
                }
            }

            return totalBoundaryCells > 0 ? (float)compatibleCells / totalBoundaryCells * 100f : 100f;
        }

        #endregion

        #region Mesh Generation Test

        public IEnumerator RunMeshGenerationTest()
        {
            UnityEngine.Debug.Log("Running Mesh Generation Test...");
            isTestRunning = true;
            currentTestType = TestType.MeshGeneration;
            currentTestIndex = 0;

            meshGenerationResults.Clear();

            // Run tests for each chunk size
            foreach (int chunkSize in testChunkSizes)
            {
                yield return StartCoroutine(TestMeshGeneration(chunkSize));
                currentTestIndex++;

                // Wait between tests
                yield return new WaitForSeconds(1.0f);
            }

            UnityEngine.Debug.Log("Mesh Generation Test completed!");
            isTestRunning = false;
            currentTestType = TestType.None;
        }

        private IEnumerator TestMeshGeneration(int chunkSize)
        {
            UnityEngine.Debug.Log($"Testing mesh generation for size: {chunkSize}");

            // Create a test chunk and generate terrain
            Vector3Int chunkPos = new Vector3Int(0, 0, 0);
            Chunk chunk = new Chunk(chunkPos, chunkSize);

            // Initialize with all possible states
            var possibleStates = Enumerable.Range(0, WFCConfigManager.Config.World.maxStates);
            chunk.InitializeCells(possibleStates);

            // Create simple terrain pattern
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    for (int y = 0; y < chunkSize; y++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);

                        // Bottom half is ground, top half is air
                        if (y < chunkSize / 2)
                            cell.Collapse(1); // Ground
                        else
                            cell.Collapse(0); // Air
                    }
                }
            }

            // Add some noise
            int noiseCount = chunkSize * chunkSize / 4;
            for (int i = 0; i < noiseCount; i++)
            {
                int x = UnityEngine.Random.Range(0, chunkSize);
                int y = UnityEngine.Random.Range(chunkSize / 3, 2 * chunkSize / 3);
                int z = UnityEngine.Random.Range(0, chunkSize);

                Cell cell = chunk.GetCell(x, y, z);

                // Change state
                cell.Collapse(y < chunkSize / 2 ? 0 : 1);
            }

            // Mark chunk as fully collapsed
            chunk.IsFullyCollapsed = true;

            // Create test density field generator
            DensityFieldGenerator densityGenerator = new DensityFieldGenerator();

            // Create test marching cubes generator
            MarchingCubesGenerator marchingCubes = new MarchingCubesGenerator();

            // Memory before
            float memoryBefore = (float)GC.GetTotalMemory(true) / (1024 * 1024);

            // Measure density field generation time
            testStopwatch = new Stopwatch();
            testStopwatch.Start();

            float[,,] densityField = densityGenerator.GenerateDensityField(chunk);

            testStopwatch.Stop();
            float densityTime = testStopwatch.ElapsedMilliseconds;

            // Yield to prevent freezing
            yield return null;

            // Measure marching cubes time
            testStopwatch = new Stopwatch();
            testStopwatch.Start();

            Mesh mesh = marchingCubes.GenerateMesh(densityField);

            testStopwatch.Stop();
            float marchingCubesTime = testStopwatch.ElapsedMilliseconds;

            // Memory after
            float memoryAfter = (float)GC.GetTotalMemory(false) / (1024 * 1024);
            float memoryUsage = memoryAfter - memoryBefore;

            // Store results
            MeshGenerationResult result = new MeshGenerationResult
            {
                chunkSize = chunkSize,
                densityFieldGenerationTime = densityTime,
                marchingCubesTime = marchingCubesTime,
                totalMeshTime = densityTime + marchingCubesTime,
                vertices = mesh.vertexCount,
                triangles = mesh.triangles.Length / 3,
                memoryUsage = memoryUsage
            };

            meshGenerationResults.Add(result);

            UnityEngine.Debug.Log($"Mesh generation test for size {chunkSize} completed: {result.totalMeshTime:F2}ms, {result.vertices} vertices");
        }

        #endregion

        #region Parallel Processing Test

        public IEnumerator RunParallelProcessingTest()
        {
            Debug.Log("Running Parallel Processing Test...");
            isTestRunning = true;
            currentTestType = TestType.ParallelProcessing;
            currentTestIndex = 0;

            // Create result list if null
            if (parallelProcessingResults == null)
                parallelProcessingResults = new List<ParallelProcessingResult>();

            parallelProcessingResults.Clear();

            // Run tests for each thread count
            foreach (int threadCount in testThreadCounts)
            {
                yield return StartCoroutine(TestParallelProcessing(threadCount));
                currentTestIndex++;

                Debug.Log($"Parallel test completed for {threadCount} threads. Results count: {parallelProcessingResults.Count}");
                yield return new WaitForSeconds(2.0f);
            }

            Debug.Log($"Parallel Processing Test completed with {parallelProcessingResults.Count} results!");
            isTestRunning = false;
            currentTestType = TestType.None;
        }

        private IEnumerator TestParallelProcessing(int threadCount)
        {
            UnityEngine.Debug.Log($"Testing parallel processing with {threadCount} threads");

            // Create a fallback if needed
            if (parallelProcessor == null && wfcGenerator != null)
            {
                Debug.LogWarning("ParallelProcessor not found - creating a mock version for testing");

                try
                {
                    // Create adapter for WFCGenerator
                    IWFCAlgorithm algorithm = new WFCAlgorithmAdapter(wfcGenerator);
                    parallelProcessor = new ParallelWFCProcessor(algorithm, threadCount);
                    parallelProcessor.Start();
                    Debug.Log("Created mock ParallelProcessor for testing");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to create mock ParallelProcessor: {e.Message}");
                }
            }

            if (parallelProcessor == null)
            {
                Debug.LogError("Could not set up parallel processor - adding default result instead");

                // Add a dummy result instead of skipping
                ParallelProcessingResult dummyResult = new ParallelProcessingResult
                {
                    threadCount = threadCount,
                    processingTime = 0,
                    speedupFactor = threadCount == 1 ? 1 : 0,
                    synchronizationOverhead = 0,
                    memoryOverheadPercent = 0,
                    maxConcurrentChunks = 0
                };

                parallelProcessingResults.Add(dummyResult);
                yield break;
            }

            // Create a set of test chunks
            List<Chunk> testChunks = new List<Chunk>();
            int chunkSize = 32; // Use fixed size for this test

            for (int i = 0; i < threadCount; i++)
            {
                Vector3Int chunkPos = new Vector3Int(i, 0, 0);
                Chunk chunk = new Chunk(chunkPos, chunkSize);

                // Initialize with all possible states
                var possibleStates = Enumerable.Range(0, WFCConfigManager.Config.World.maxStates);
                chunk.InitializeCells(possibleStates);

                testChunks.Add(chunk);
            }

            // Set up parallel processor with specified thread count
            ParallelWFCProcessor testProcessor = null;
            if (parallelManager != null)
            {
                // Try to reflect into parallelManager to get/set processor
                var processorField = parallelManager.GetType().GetField("parallelProcessor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (processorField != null)
                {
                    // Save original processor
                    var originalProcessor = processorField.GetValue(parallelManager);

                    // Create test processor with specified thread count
                    try
                    {
                        // Create adapter for WFCGenerator
                        IWFCAlgorithm algorithm = new WFCAlgorithmAdapter(wfcGenerator);
                        testProcessor = new ParallelWFCProcessor(algorithm, threadCount);
                        testProcessor.Start();

                        // Set it on the manager
                        processorField.SetValue(parallelManager, testProcessor);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError($"Failed to create test processor: {e.Message}");
                        yield break;
                    }

                    // Reset to original at end of test
                    yield return new WaitForEndOfFrame();

                    // Use the test processor
                    parallelProcessor = testProcessor;
                }
            }

            if (parallelProcessor == null)
            {
                UnityEngine.Debug.LogWarning("Could not set up test parallel processor");
                yield break;
            }

            // Memory before
            float memoryBefore = (float)GC.GetTotalMemory(true) / (1024 * 1024);

            // Track synchronization overhead
            float syncOverhead = 0f;

            // Measure processing time
            testStopwatch = new Stopwatch();
            testStopwatch.Start();

            // Queue all chunks for processing
            int chunksQueued = 0;
            foreach (var chunk in testChunks)
            {
                bool queued = parallelProcessor.QueueChunkForProcessing(
                    chunk, WFCJobType.Collapse, 100, 1.0f);

                if (queued)
                    chunksQueued++;
            }

            // Wait for all chunks to be processed
            bool processing = true;
            int completedChunks = 0;
            int lastCompleted = 0;

            while (processing)
            {
                // Process main thread events - measure sync overhead
                Stopwatch syncWatch = new Stopwatch();
                syncWatch.Start();
                parallelProcessor.ProcessMainThreadEvents();
                syncWatch.Stop();
                syncOverhead += syncWatch.ElapsedMilliseconds;

                // Update processing status
                completedChunks = parallelProcessor.TotalProcessedJobs - lastCompleted;

                // Check if done
                if (completedChunks >= chunksQueued && chunksQueued > 0)
                {
                    processing = false;
                }

                // Prevent infinite loop
                yield return new WaitForSeconds(0.1f);

                // Timeout after 10 seconds
                if (testStopwatch.ElapsedMilliseconds > 10000)
                {
                    UnityEngine.Debug.LogWarning("Parallel processing test timed out");
                    processing = false;
                }
            }

            testStopwatch.Stop();
            float processingTime = testStopwatch.ElapsedMilliseconds;

            // Memory after
            float memoryAfter = (float)GC.GetTotalMemory(false) / (1024 * 1024);
            float memoryOverhead = memoryAfter - memoryBefore;

            // Calculate memory overhead percentage
            float memoryOverheadPercent = (memoryOverhead / memoryBefore) * 100f;

            // Calculate speedup factor based on whether this is the baseline or not
            float speedupFactor = 1.0f;
            float baselineTime = 0f;

            // Get existing results to check for baseline
            if (parallelProcessingResults != null && parallelProcessingResults.Count > 0)
            {
                // Find baseline result (thread count = 1)
                var baselineResult = parallelProcessingResults.FirstOrDefault(r => r.threadCount == 1);
                if (baselineResult.threadCount == 1)
                {
                    baselineTime = baselineResult.processingTime;
                    if (threadCount > 1 && baselineTime > 0)
                    {
                        speedupFactor = baselineTime / processingTime;
                    }
                }
            }

            // If this is the baseline (thread count = 1), record it
            if (threadCount == 1)
            {
                baselineTime = processingTime;
            }
            // Otherwise calculate speedup using existing baseline
            else if (baselineTime > 0)
            {
                speedupFactor = baselineTime / processingTime;
            }

            // Store results
            ParallelProcessingResult result = new ParallelProcessingResult
            {
                threadCount = threadCount,
                processingTime = processingTime,
                speedupFactor = speedupFactor,
                synchronizationOverhead = syncOverhead,
                memoryOverheadPercent = memoryOverheadPercent,
                maxConcurrentChunks = parallelProcessor.ProcessingChunksCount
            };

            parallelProcessingResults.Add(result);

            UnityEngine.Debug.Log($"Parallel processing test with {threadCount} threads completed: {processingTime:F2}ms, speedup: {speedupFactor:F2}x");

            // Reset original processor if needed
            if (parallelManager != null && testProcessor != null)
            {
                var processorField = parallelManager.GetType().GetField("parallelProcessor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (processorField != null)
                {
                    // Stop test processor
                    testProcessor.Stop();

                    // Restore original processor
                    parallelProcessor = parallelManager.GetParallelProcessor();
                }
            }
        }

        #endregion

        #region World Size Scaling Test

        public IEnumerator RunWorldSizeScalingTest()
        {
            Debug.Log("Running World Size Scaling Test :)");
            isTestRunning = true;
            currentTestType = TestType.WorldSizeScaling;
            currentTestIndex = 0;

            // Create result list if null
            if (worldSizeScalingResults == null)
                worldSizeScalingResults = new List<WorldSizeScalingResult>();

            worldSizeScalingResults.Clear();

            // Run tests for each world size
            foreach (var worldSize in testWorldSizes)
            {
                yield return StartCoroutine(TestWorldSizeScaling(worldSize));
                currentTestIndex++;

                Debug.Log($"World size test completed for {worldSize}. Results count: {worldSizeScalingResults.Count}");
                yield return new WaitForSeconds(2.0f);
            }

            Debug.Log($"World Size Scaling Test completed with {worldSizeScalingResults.Count} results!");
            isTestRunning = false;
            currentTestType = TestType.None;
        }

        private IEnumerator TestWorldSizeScaling(Vector3Int worldSize)
        {
            UnityEngine.Debug.Log($"Testing world size scaling for: {worldSize}");

            // Skip if chunk manager is not available
            if (chunkManager == null)
                yield break;

            // Save original configuration
            var originalConfig = WFCConfigManager.Config;
            float originalLoadDistance = GetChunkManagerLoadDistance();

            // Create a test set of chunks
            Dictionary<Vector3Int, Chunk> testChunks = new Dictionary<Vector3Int, Chunk>();

            // Memory before
            float memoryBefore = (float)GC.GetTotalMemory(true) / (1024 * 1024);

            // Measure FPS before
            float fpsBefore = 1.0f / Time.smoothDeltaTime;

            // Measure generation time
            testStopwatch = new Stopwatch();
            testStopwatch.Start();

            // Create chunks
            int totalChunks = worldSize.x * worldSize.y * worldSize.z;
            int chunksCreated = 0;

            for (int x = 0; x < worldSize.x; x++)
            {
                for (int y = 0; y < worldSize.y; y++)
                {
                    for (int z = 0; z < worldSize.z; z++)
                    {
                        Vector3Int chunkPos = new Vector3Int(x, y, z);

                        // Call chunk creation on the ChunkManager
                        chunkManager.CreateChunkAt(chunkPos);
                        chunksCreated++;

                        // Only create a subset of chunks for very large worlds
                        if (totalChunks > 64 && chunksCreated >= 64)
                            break;
                    }

                    if (totalChunks > 64 && chunksCreated >= 64)
                        break;
                }

                if (totalChunks > 64 && chunksCreated >= 64)
                    break;

                // Yield every few chunks to prevent freezing
                yield return null;
            }

            // Wait for chunks to process
            yield return new WaitForSeconds(2.0f);

            // Wait for chunks to be fully loaded
            float timeout = 20.0f;
            float elapsed = 0.0f;
            while (elapsed < timeout)
            {
                // Check if chunks are loaded
                var loadedChunks = chunkManager.GetLoadedChunks();
                int activeChunks = loadedChunks.Count(c => GetChunkState(c.Key) == ChunkManager.ChunkLifecycleState.Active);

                if (activeChunks >= chunksCreated * 0.9f)
                    break;

                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            testStopwatch.Stop();
            float generationTime = testStopwatch.ElapsedMilliseconds / 1000f; // Convert to seconds

            // Memory after
            float memoryAfter = (float)GC.GetTotalMemory(false) / (1024 * 1024);
            float memoryUsage = memoryAfter - memoryBefore;

            // Measure FPS after
            float fpsAfter = 1.0f / Time.smoothDeltaTime;
            float fpsImpact = fpsBefore - fpsAfter;

            // Get chunks loaded and processing stats
            var chunks = chunkManager.GetLoadedChunks();
            int chunksLoaded = chunks.Count;
            float chunksProcessedPerFrame = chunksLoaded / (generationTime * 60f); // Assuming 60 FPS

            // Get loading distance
            float loadingDistance = GetChunkManagerLoadDistance();

            // Store results
            WorldSizeScalingResult result = new WorldSizeScalingResult
            {
                worldSize = worldSize,
                totalMemoryUsage = memoryUsage,
                generationTime = generationTime,
                fpsImpact = fpsImpact,
                chunksLoaded = chunksLoaded,
                chunksProcessedPerFrame = chunksProcessedPerFrame,
                loadingDistance = loadingDistance
            };

            worldSizeScalingResults.Add(result);

            UnityEngine.Debug.Log($"World size scaling test for {worldSize} completed: {chunksLoaded} chunks, {generationTime:F2}s generation time");

            // Restore original configuration
            SetChunkManagerLoadDistance(originalLoadDistance);

            // Clean up created chunks
            StartCoroutine(CleanupTestChunks());
        }

        private float GetChunkManagerLoadDistance()
        {
            if (chunkManager == null)
                return 0f;

            // Try to get the load distance via reflection
            var field = chunkManager.GetType().GetField("currentLoadDistance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
                return (float)field.GetValue(chunkManager);

            return 0f;
        }

        private void SetChunkManagerLoadDistance(float distance)
        {
            if (chunkManager == null)
                return;

            // Try to set the load distance via reflection
            var field = chunkManager.GetType().GetField("currentLoadDistance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
                field.SetValue(chunkManager, distance);
        }

        private IEnumerator CleanupTestChunks()
        {
            if (chunkManager == null)
                yield break;

            // Try to call reset method on chunk manager
            var resetMethod = chunkManager.GetType().GetMethod("ResetChunkSystem", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (resetMethod != null)
            {
                resetMethod.Invoke(chunkManager, null);
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                // Manually remove chunks
                var chunks = chunkManager.GetLoadedChunks();
                foreach (var chunkPos in chunks.Keys.ToList())
                {
                    // Try to call unload method
                    var unloadMethod = chunkManager.GetType().GetMethod("UnloadChunk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (unloadMethod != null)
                        unloadMethod.Invoke(chunkManager, new object[] { chunkPos });

                    yield return null;
                }
            }
        }

        #endregion

        #region LOD Performance Test

        public IEnumerator RunLODPerformanceTest()
        {
            UnityEngine.Debug.Log("Running LOD Performance Test :)");
            isTestRunning = true;
            currentTestType = TestType.LODPerformance;
            currentTestIndex = 0;

            // Skip if configuration is not available
            if (WFCConfigManager.Config == null || WFCConfigManager.Config.Performance == null || WFCConfigManager.Config.Performance.lodSettings == null)
            {
                UnityEngine.Debug.LogWarning("LOD performance test skipped - no LOD settings found in configuration");
                isTestRunning = false;
                currentTestType = TestType.None;
                yield break;
            }

            lodPerformanceResults.Clear();

            // Get LOD levels from configuration
            int lodLevels = WFCConfigManager.Config.Performance.lodSettings.lodDistanceThresholds.Length + 1;

            // Run tests for each LOD level
            for (int lodLevel = 0; lodLevel < lodLevels; lodLevel++)
            {
                yield return StartCoroutine(TestLODPerformance(lodLevel));
                currentTestIndex++;

                // Wait between tests
                yield return new WaitForSeconds(1.0f);
            }

            UnityEngine.Debug.Log("LOD Performance Test completed!");
            isTestRunning = false;
            currentTestType = TestType.None;
        }

        private IEnumerator TestLODPerformance(int lodLevel)
        {
            UnityEngine.Debug.Log($"Testing LOD performance for level: {lodLevel}");
            EnsureWFCGenerator(); // Add this line

            // Get LOD configuration
            var lodSettings = WFCConfigManager.Config.Performance.lodSettings;

            // Get distance range for this LOD level
            float distanceMin = lodLevel == 0 ? 0f : lodSettings.lodDistanceThresholds[lodLevel - 1];
            float distanceMax = lodLevel < lodSettings.lodDistanceThresholds.Length ?
                lodSettings.lodDistanceThresholds[lodLevel] : float.MaxValue;

            // Create test chunks with different LOD levels
            Vector3Int chunkPos = new Vector3Int(0, 0, 0);
            Chunk baselineChunk = new Chunk(chunkPos, testChunkSize);
            Chunk lodChunk = new Chunk(chunkPos, testChunkSize);

            // Set LOD levels
            baselineChunk.LODLevel = 0; // Highest quality
            lodChunk.LODLevel = lodLevel;

            // Apply LOD settings to chunks
            ApplyLODSettings(baselineChunk);
            ApplyLODSettings(lodChunk);

            // Initialize with all possible states
            var possibleStates = Enumerable.Range(0, WFCConfigManager.Config.World.maxStates);
            baselineChunk.InitializeCells(possibleStates);
            lodChunk.InitializeCells(possibleStates);

            // Measure baseline performance (LOD 0)
            float baselineGenerationTime = MeasureGenerationTime(baselineChunk);
            int baselineVertexCount = MeasureMeshVertexCount(baselineChunk);
            float baselineMemoryUsage = MeasureMemoryUsage(baselineChunk);

            // Yield to prevent freezing
            yield return null;

            // Measure LOD performance
            float lodGenerationTime = MeasureGenerationTime(lodChunk);
            int lodVertexCount = MeasureMeshVertexCount(lodChunk);
            float lodMemoryUsage = MeasureMemoryUsage(lodChunk);

            // Calculate performance metrics
            float vertexReductionPercent = baselineVertexCount > 0 ?
                ((float)(baselineVertexCount - lodVertexCount) / baselineVertexCount) * 100f : 0f;

            float generationSpeedIncreasePercent = baselineGenerationTime > 0 ?
                ((baselineGenerationTime - lodGenerationTime) / baselineGenerationTime) * 100f : 0f;

            float memoryReductionPercent = baselineMemoryUsage > 0 ?
                ((baselineMemoryUsage - lodMemoryUsage) / baselineMemoryUsage) * 100f : 0f;

            // Estimate visual quality impact (0-10, where 0 is no impact, 10 is severe)
            int visualQualityImpact = lodLevel * 2; // Simple estimation

            // Store results
            LODPerformanceResult result = new LODPerformanceResult
            {
                lodLevel = lodLevel,
                distanceRange = distanceMin,
                vertexReductionPercent = vertexReductionPercent,
                generationSpeedIncreasePercent = generationSpeedIncreasePercent,
                memoryReductionPercent = memoryReductionPercent,
                visualQualityImpact = visualQualityImpact
            };

            lodPerformanceResults.Add(result);

            UnityEngine.Debug.Log($"LOD performance test for level {lodLevel} completed: " + $"{vertexReductionPercent:F2}% vertex reduction, " + $"{generationSpeedIncreasePercent:F2}% generation speed increase");
        }

        private void ApplyLODSettings(Chunk chunk)
        {
            // Get LOD-specific settings
            int lodLevel = chunk.LODLevel;
            var lodSettings = WFCConfigManager.Config.Performance.lodSettings;

            // Set max iterations for this LOD level
            if (lodLevel < lodSettings.maxIterationsPerLOD.Length)
            {
                chunk.MaxIterations = lodSettings.maxIterationsPerLOD[lodLevel];
            }
            else if (lodSettings.maxIterationsPerLOD.Length > 0)
            {
                // Use the last defined value
                chunk.MaxIterations = lodSettings.maxIterationsPerLOD[
                    lodSettings.maxIterationsPerLOD.Length - 1];
            }
            else
            {
                // Fallback
                chunk.MaxIterations = WFCConfigManager.Config.Algorithm.maxIterationsPerChunk;
            }

            // Set constraint influence
            if (lodLevel < lodSettings.constraintInfluencePerLOD.Length)
            {
                chunk.ConstraintInfluence = lodSettings.constraintInfluencePerLOD[lodLevel];
            }
            else if (lodSettings.constraintInfluencePerLOD.Length > 0)
            {
                chunk.ConstraintInfluence = lodSettings.constraintInfluencePerLOD[
                    lodSettings.constraintInfluencePerLOD.Length - 1];
            }
            else
            {
                chunk.ConstraintInfluence = 1.0f;
            }
        }

        private float MeasureGenerationTime(Chunk chunk)
        {
            // Perform WFC algorithm
            int propagationEvents = 0;
            int iterations = 0;
            bool madeProgress = true;

            testStopwatch = new Stopwatch();
            testStopwatch.Start();

            while (madeProgress && iterations < chunk.MaxIterations)
            {
                madeProgress = CollapseNextCell(chunk, ref propagationEvents);
                iterations++;
            }

            testStopwatch.Stop();
            return testStopwatch.ElapsedMilliseconds;
        }

        private int MeasureMeshVertexCount(Chunk chunk)
        {
            try
            {
                // Mark as fully collapsed
                chunk.IsFullyCollapsed = true;

                // Generate mesh
                DensityFieldGenerator densityGenerator = new DensityFieldGenerator();
                MarchingCubesGenerator marchingCubes = new MarchingCubesGenerator();

                float[,,] densityField = densityGenerator.GenerateDensityField(chunk);
                Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

                return mesh.vertexCount;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error measuring mesh vertex count: {e.Message}");
                return 0;
            }
        }

        private float MeasureMemoryUsage(Chunk chunk)
        {
            float memoryBefore = (float)GC.GetTotalMemory(true) / (1024 * 1024);

            // Generate density field and mesh
            DensityFieldGenerator densityGenerator = new DensityFieldGenerator();
            MarchingCubesGenerator marchingCubes = new MarchingCubesGenerator();

            float[,,] densityField = densityGenerator.GenerateDensityField(chunk);
            Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

            float memoryAfter = (float)GC.GetTotalMemory(false) / (1024 * 1024);
            return memoryAfter - memoryBefore;
        }

        #endregion

        #endregion

        private void UpdateTestProgress()
        {
            switch (currentTestType)
            {
                case TestType.ChunkGeneration:
                    break;

                case TestType.BoundaryCoherence:
                    break;

                case TestType.MeshGeneration:
                    break;

                case TestType.ParallelProcessing:
                    break;

                case TestType.WorldSizeScaling:
                    break;

                case TestType.LODPerformance:
                    break;
            }
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

            UnityEngine.Debug.Log(sb.ToString());
        }

        private void SaveMetricsToFile()
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                // Add header
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

                UnityEngine.Debug.Log($"Metrics saved to {metricsFilePath}");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error saving metrics to file: {e.Message}");
            }
        }

        private Vector3Int? FindCellPosition(Cell cell, Chunk chunk)
        {
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        if (chunk.GetCell(x, y, z) == cell)
                        {
                            return new Vector3Int(x, y, z);
                        }
                    }
                }
            }

            return null;
        }

        private float CalculateCollapsedPercentage(Chunk chunk)
        {
            if (chunk == null) return 0f;

            int totalCells = chunk.Size * chunk.Size * chunk.Size;
            int collapsedCells = 0;

            // Sample cells to estimate collapse percentage (checking every cell could be expensive)
            int sampleSize = Mathf.Min(27, chunk.Size * chunk.Size * chunk.Size);
            int samplesPerDimension = Mathf.CeilToInt(Mathf.Pow(sampleSize, 1f / 3f));
            float step = chunk.Size / (float)samplesPerDimension;

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
                        {
                            collapsedCells++;
                        }
                    }
                }
            }

            return (float)collapsedCells / sampleSize;
        }

        private ChunkManager.ChunkLifecycleState GetChunkState(Vector3Int chunkPos)
        {
            if (chunkManager == null)
                return ChunkManager.ChunkLifecycleState.None;

            // Try to get the method via reflection
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
            try
            {
                if (wfcGenerator != null)
                    return wfcGenerator.AreStatesCompatible(stateA, stateB, direction);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error using WFCGenerator.AreStatesCompatible: {e.Message}. Using fallback rules.");
            }

            // Enhanced fallback rules
            // Rule 1: Same states are always compatible
            if (stateA == stateB)
                return true;

            // Rule 2: State 0 (air) can be adjacent to any state
            if (stateA == 0 || stateB == 0)
                return true;

            // Rule 3: Ground states (1-4) can be adjacent to each other
            if ((stateA >= 1 && stateA <= 4) && (stateB >= 1 && stateB <= 4))
                return true;

            // Rule 4: Allow transitions between neighboring states
            if (Math.Abs(stateA - stateB) <= 1)
                return true;

            return false;
        }

        private void EnsureWFCGenerator()
        {
            if (wfcGenerator == null)
            {
                Debug.Log("Creating mock WFCGenerator for testing");
                wfcGenerator = FindObjectOfType<WFCGenerator>();

                if (wfcGenerator == null)
                {
                    // Try to create a mock generator
                    try
                    {
                        GameObject mockObj = new GameObject("MockWFCGenerator");
                        wfcGenerator = mockObj.AddComponent<WFCGenerator>();

                        // Force initialization of adjacency rules
                        var initMethod = wfcGenerator.GetType().GetMethod("InitializeRulesOnly",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        if (initMethod != null)
                            initMethod.Invoke(wfcGenerator, null);

                        Debug.Log("Mock WFCGenerator created successfully");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to create mock WFCGenerator: {e.Message}");
                        wfcGenerator = null;
                    }
                }
            }
        }

        // Simple mock implementation of BoundaryManager for testing
        private class MockBoundaryManager : IBoundaryCompatibilityChecker, IPropagationHandler, IChunkProvider
        {
            private WFCMetricsMonitor parent;

            public MockBoundaryManager(WFCMetricsMonitor parent = null)
            {
                this.parent = parent;
            }

            public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
            {
                if (parent != null)
                    return parent.AreStatesCompatible(stateA, stateB, direction);

                // Simple compatibility rule
                return stateA == stateB || Math.Abs(stateA - stateB) <= 1;
            }

            public void AddPropagationEvent(PropagationEvent evt)
            {
                // for testing
            }

            public Dictionary<Vector3Int, Chunk> GetChunks()
            {
                return new Dictionary<Vector3Int, Chunk>();
            }
        }

        // Simple Stopwatch class
        private class Stopwatch
        {
            private float startTime;
            private float stopTime;
            private bool isRunning;

            public float ElapsedMilliseconds
            {
                get
                {
                    if (isRunning)
                        return (Time.realtimeSinceStartup - startTime) * 1000f;
                    else
                        return (stopTime - startTime) * 1000f;
                }
            }

            public void Start()
            {
                startTime = Time.realtimeSinceStartup;
                isRunning = true;
            }

            public void Stop()
            {
                stopTime = Time.realtimeSinceStartup;
                isRunning = false;
            }

            public void Reset()
            {
                startTime = 0;
                stopTime = 0;
                isRunning = false;
            }
        }
    }
}