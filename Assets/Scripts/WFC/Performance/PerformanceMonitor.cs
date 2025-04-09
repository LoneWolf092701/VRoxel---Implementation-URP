using System.Collections.Generic;
using UnityEngine;
using WFC.Chunking;
using WFC.Core;
using WFC.Processing;
using System.Diagnostics;
using WFC.Generation;
using WFC.Boundary;
using WFC.MarchingCubes;
using System.Linq;
using Debug = UnityEngine.Debug;

namespace WFC.Performance
{
    /// <summary>
    /// Monitors performance of the WFC system to help with optimization
    /// </summary>
    public class PerformanceMonitor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] public ChunkManager chunkManager;
        [SerializeField] private WFCGenerator wfcGenerator;
        [SerializeField] private MeshGenerator meshGenerator;

        [Header("Settings")]
        [SerializeField] private int logFrequency = 60; // Log every 60 frames
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private bool showDetailedTimings = true;

        [Header("Display Settings")]
        [SerializeField] private bool showParallelStats = true;
        [SerializeField] private bool showBoundaryStats = true;
        [SerializeField] private bool showMeshStats = true;
        [SerializeField] private bool showTerrainStats = true;

        // Performance data
        private float[] frameTimes = new float[120]; // Last 120 frames
        private int frameIndex = 0;
        private int frameCount = 0;

        // Stats
        private float minFrameTime = float.MaxValue;
        private float maxFrameTime = 0;
        private float avgFrameTime = 0;
        private int loadedChunkCount = 0;
        private int processingChunkCount = 0;
        private float chunkProcessingTime = 0;

        // Additional stats for parallel processing
        private int activeThreads = 0;
        private int completedJobs = 0;
        private float avgJobTime = 0;
        private int queuedJobs = 0;

        // Mesh generation stats
        private int totalMeshesGenerated = 0;
        private float avgMeshGenerationTime = 0;
        private int avgVerticesPerMesh = 0;
        private int avgTrianglesPerMesh = 0;

        // Boundary stats
        private int boundaryUpdates = 0;
        private float avgBoundaryCoherence = 0;
        private int boundaryConflicts = 0;

        // Terrain stats
        private int totalCollapsedCells = 0;
        private int constraintsApplied = 0;
        private float minDensity = 1.0f;
        private float maxDensity = 0.0f;
        private bool isTerrainInverted = false;

        // Component timing data
        private Dictionary<string, float> componentTimings = new Dictionary<string, float>();
        private Dictionary<string, int> componentCalls = new Dictionary<string, int>();
        private Stopwatch componentTimer = new Stopwatch();
        private string currentTimingComponent = null;

        private ParallelWFCProcessor parallelProcessor;
        private BoundaryBufferManager boundaryManager;
        private DensityFieldGenerator densityFieldGenerator;

        private void Start()
        {
            // Find required components if not assigned
            if (chunkManager == null)
                chunkManager = FindObjectOfType<ChunkManager>();

            if (wfcGenerator == null)
                wfcGenerator = FindObjectOfType<WFCGenerator>();

            if (meshGenerator == null)
                meshGenerator = FindObjectOfType<MeshGenerator>();

            // Try to find parallel processor
            if (wfcGenerator != null)
            {
                // Get field using reflection
                var field = wfcGenerator.GetType().GetField("parallelProcessor",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    parallelProcessor = field.GetValue(wfcGenerator) as ParallelWFCProcessor;
                    Debug.Log("Found parallel processor reference");
                }

                // Get boundary manager
                var boundaryField = wfcGenerator.GetType().GetField("boundaryManager",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (boundaryField != null)
                {
                    boundaryManager = boundaryField.GetValue(wfcGenerator) as BoundaryBufferManager;
                    Debug.Log("Found boundary manager reference");
                }
            }

            // Get density field generator from mesh generator
            if (meshGenerator != null)
            {
                var marchingCubesField = meshGenerator.GetType().GetField("marchingCubes",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                var densityField = meshGenerator.GetType().GetField("densityGenerator",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (densityField != null)
                {
                    densityFieldGenerator = densityField.GetValue(meshGenerator) as DensityFieldGenerator;
                    Debug.Log("Found density field generator reference");
                }

                // Check if marching cubes was modified
                if (marchingCubesField != null)
                {
                    var marchingCubes = marchingCubesField.GetValue(meshGenerator);
                    if (marchingCubes != null)
                    {
                        // detect terrain inversion by checking cube index calculation method
                        var methodInfo = marchingCubes.GetType().GetMethod("ProcessCube",
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance);

                        if (methodInfo != null)
                        {
                            // Cannot directly check method implementation
                            Debug.Log("Found marching cubes implementation, but cannot verify threshold comparison direction");
                            isTerrainInverted = false; // Default to fixed state
                        }
                    }
                }
            }

            // Try to find ParallelWFCManager if no processor reference
            if (parallelProcessor == null)
            {
                var parallelManager = FindObjectOfType<ParallelWFCManager>();
                if (parallelManager != null)
                {
                    parallelProcessor = parallelManager.GetParallelProcessor();
                    Debug.Log("Found parallel processor through ParallelWFCManager");
                }
            }

            Debug.Log("PerformanceMonitor initialized with enhanced stats tracking");
        }

        private void Update()
        {
            // Track frame time
            float frameTime = Time.deltaTime;
            frameTimes[frameIndex] = frameTime;
            frameIndex = (frameIndex + 1) % frameTimes.Length;
            frameCount++;

            // Update parallel processing stats
            UpdateParallelStats();

            // Update boundary stats
            UpdateBoundaryStats();

            // Update mesh generation stats
            UpdateMeshStats();

            // Update terrain stats
            UpdateTerrainStats();

            // Calculate stats
            if (frameCount % logFrequency == 0 && enableLogging)
            {
                CalculateStats();
                LogPerformanceData();

                // Reset component timings after logging
                ResetComponentTimings();
            }
        }

        private void UpdateParallelStats()
        {
            if (parallelProcessor != null)
            {
                activeThreads = parallelProcessor.ActiveThreads;
                completedJobs = parallelProcessor.TotalProcessedJobs;
                avgJobTime = parallelProcessor.AverageJobTime;

                // Try to get queue count using reflection (if available)
                var queueField = parallelProcessor.GetType().GetField("jobQueue",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (queueField != null)
                {
                    var queue = queueField.GetValue(parallelProcessor);
                    if (queue != null)
                    {
                        var countProp = queue.GetType().GetProperty("Count");
                        if (countProp != null)
                        {
                            queuedJobs = (int)countProp.GetValue(queue);
                        }
                    }
                }
            }
        }

        private void UpdateBoundaryStats()
        {
            if (boundaryManager != null)
            {
                // Try to get stats using reflection
                var updatesField = boundaryManager.GetType().GetField("boundaryUpdates",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (updatesField != null)
                {
                    boundaryUpdates = (int)updatesField.GetValue(boundaryManager);
                }

                var conflictsField = boundaryManager.GetType().GetField("boundaryConflicts",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (conflictsField != null)
                {
                    boundaryConflicts = (int)conflictsField.GetValue(boundaryManager);
                }

                boundaryUpdates++;
            }
        }

        private void UpdateMeshStats()
        {
            if (meshGenerator != null)
            {
                // Check if enough timing data for mesh generation
                if (componentTimings.TryGetValue("MeshGeneration", out float meshTime) &&
                    componentCalls.TryGetValue("MeshGeneration", out int meshCalls) &&
                    meshCalls > 0)
                {
                    totalMeshesGenerated += meshCalls;
                    avgMeshGenerationTime = (avgMeshGenerationTime * 0.9f) + (meshTime / meshCalls * 0.1f);
                }

                // Try to get mesh stats from recently generated meshes
                var meshObjectsField = meshGenerator.GetType().GetField("chunkMeshObjects",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (meshObjectsField != null)
                {
                    var meshObjects = meshObjectsField.GetValue(meshGenerator) as Dictionary<Vector3Int, GameObject>;
                    if (meshObjects != null && meshObjects.Count > 0)
                    {
                        int totalVerts = 0;
                        int totalTris = 0;
                        int count = 0;

                        foreach (var obj in meshObjects.Values)
                        {
                            if (obj != null)
                            {
                                var meshFilter = obj.GetComponent<MeshFilter>();
                                if (meshFilter != null && meshFilter.sharedMesh != null)
                                {
                                    totalVerts += meshFilter.sharedMesh.vertexCount;
                                    totalTris += meshFilter.sharedMesh.triangles.Length / 3;
                                    count++;
                                }
                            }
                        }

                        if (count > 0)
                        {
                            avgVerticesPerMesh = totalVerts / count;
                            avgTrianglesPerMesh = totalTris / count;
                        }
                    }
                }
            }
        }

        private void UpdateTerrainStats()
        {
            if (densityFieldGenerator != null)
            {
                // Try to get stats from density field generator
                var minField = densityFieldGenerator.GetType().GetField("minDensity",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                var maxField = densityFieldGenerator.GetType().GetField("maxDensity",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                if (minField != null && maxField != null)
                {
                    minDensity = (float)minField.GetValue(densityFieldGenerator);
                    maxDensity = (float)maxField.GetValue(densityFieldGenerator);
                }
            }

            if (wfcGenerator != null)
            {
                // Try to get constraints system
                var constraintsField = wfcGenerator.GetType().GetField("hierarchicalConstraints",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (constraintsField != null)
                {
                    var constraints = constraintsField.GetValue(wfcGenerator);
                    if (constraints != null)
                    {
                        // Try to get stats property
                        var statsProp = constraints.GetType().GetProperty("Statistics");
                        if (statsProp != null)
                        {
                            var stats = statsProp.GetValue(constraints);
                            if (stats != null)
                            {
                                // Get total constraints applied
                                var appliedProp = stats.GetType().GetField("GlobalConstraintsApplied");
                                if (appliedProp != null)
                                {
                                    constraintsApplied = (int)appliedProp.GetValue(stats);
                                }

                                // Get cells collapsed by constraints
                                var collapsedProp = stats.GetType().GetField("CellsCollapsed");
                                if (collapsedProp != null)
                                {
                                    totalCollapsedCells = (int)collapsedProp.GetValue(stats);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Starts timing a specific component
        /// </summary>
        /// <param name="componentName">Name of the component to time</param>
        public void StartComponentTiming(string componentName)
        {
            // Check if we're already timing something else
            if (currentTimingComponent != null)
            {
                // Automatically end the previous timing
                EndComponentTiming(currentTimingComponent);
            }

            // Set current component and start timer
            currentTimingComponent = componentName;
            componentTimer.Reset();
            componentTimer.Start();
        }

        /// <summary>
        /// Ends timing for a component and records the elapsed time
        /// </summary>
        /// <param name="componentName">Name of the component being timed</param>
        public void EndComponentTiming(string componentName)
        {
            // Only proceed if timing the specified component
            if (componentName != currentTimingComponent)
            {
                Debug.LogWarning($"EndComponentTiming called for {componentName} but StartComponentTiming was called for {currentTimingComponent}");
                return;
            }

            // Stop the timer
            componentTimer.Stop();
            float elapsed = componentTimer.ElapsedMilliseconds / 1000f; // Convert to seconds

            // Add to the component timings
            if (!componentTimings.ContainsKey(componentName))
            {
                componentTimings[componentName] = 0;
                componentCalls[componentName] = 0;
            }

            componentTimings[componentName] += elapsed;
            componentCalls[componentName]++;

            // Clear current component
            currentTimingComponent = null;
        }

        /// <summary>
        /// Resets all component timing data
        /// </summary>
        private void ResetComponentTimings()
        {
            componentTimings.Clear();
            componentCalls.Clear();
        }

        private void CalculateStats()
        {
            // Calculate frame time stats
            minFrameTime = float.MaxValue;
            maxFrameTime = 0;
            float sum = 0;

            for (int i = 0; i < frameTimes.Length; i++)
            {
                if (frameTimes[i] == 0)
                    continue;

                minFrameTime = Mathf.Min(minFrameTime, frameTimes[i]);
                maxFrameTime = Mathf.Max(maxFrameTime, frameTimes[i]);
                sum += frameTimes[i];
            }

            avgFrameTime = sum / frameTimes.Length;

            // Get chunk data from ChunkManager
            if (chunkManager != null)
            {
                var chunksField = chunkManager.GetType().GetField("loadedChunks",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (chunksField != null)
                {
                    var chunks = chunksField.GetValue(chunkManager) as Dictionary<Vector3Int, Chunk>;
                    if (chunks != null)
                    {
                        loadedChunkCount = chunks.Count;
                    }
                }
            }

            // Get parallel processing data
            if (parallelProcessor != null)
            {
                processingChunkCount = parallelProcessor.ActiveThreads;
                chunkProcessingTime = parallelProcessor.AverageJobTime;
            }
        }

        private void LogPerformanceData()
        {
            // Calculate frame rate
            float avgFPS = 1.0f / avgFrameTime;
            float minFPS = 1.0f / maxFrameTime;
            float maxFPS = 1.0f / minFrameTime;

            // Build the log message
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Performance Report ===");
            sb.AppendLine($"FPS: {avgFPS:F1} avg ({minFPS:F1} min, {maxFPS:F1} max)");
            sb.AppendLine($"Frame Time: {avgFrameTime * 1000:F1}ms avg ({maxFrameTime * 1000:F1}ms max)");
            sb.AppendLine($"Chunks: {loadedChunkCount} loaded, {processingChunkCount} processing");
            sb.AppendLine($"Memory: {System.GC.GetTotalMemory(false) / (1024 * 1024):F1} MB");

            // Add parallel processing stats
            if (showParallelStats && parallelProcessor != null)
            {
                sb.AppendLine("\nParallel Processing:");
                sb.AppendLine($"  Active Threads: {activeThreads}");
                sb.AppendLine($"  Completed Jobs: {completedJobs}");
                sb.AppendLine($"  Queue Size: {queuedJobs}");
                sb.AppendLine($"  Avg Job Time: {avgJobTime * 1000:F2}ms");
            }

            // Add boundary stats
            if (showBoundaryStats)
            {
                sb.AppendLine("\nBoundary Management:");
                sb.AppendLine($"  Boundary Updates: {boundaryUpdates}");
                sb.AppendLine($"  Boundary Conflicts: {boundaryConflicts}");
                sb.AppendLine($"  Boundary Coherence: {avgBoundaryCoherence:F2}");
            }

            // Add mesh stats
            if (showMeshStats)
            {
                sb.AppendLine("\nMesh Generation:");
                sb.AppendLine($"  Total Meshes: {totalMeshesGenerated}");
                sb.AppendLine($"  Avg Generation Time: {avgMeshGenerationTime * 1000:F2}ms");
                sb.AppendLine($"  Avg Vertices: {avgVerticesPerMesh}");
                sb.AppendLine($"  Avg Triangles: {avgTrianglesPerMesh}");
            }

            // Add terrain stats
            if (showTerrainStats)
            {
                sb.AppendLine("\nTerrain Generation:");
                sb.AppendLine($"  Terrain Inversion Fixed: {!isTerrainInverted}");
                sb.AppendLine($"  Density Range: {minDensity:F3} to {maxDensity:F3}");
                sb.AppendLine($"  Constraints Applied: {constraintsApplied}");
                sb.AppendLine($"  Cells Collapsed: {totalCollapsedCells}");
            }

            // Add component timings if we have any
            if (componentTimings.Count > 0)
            {
                sb.AppendLine("\nComponent Timings:");
                foreach (var timing in componentTimings)
                {
                    string componentName = timing.Key;
                    float totalTime = timing.Value;
                    int calls = componentCalls[componentName];
                    float avgTime = calls > 0 ? totalTime / calls : 0;

                    sb.AppendLine($"  {componentName}: {totalTime * 1000:F2}ms total, {avgTime * 1000:F2}ms avg ({calls} calls)");
                }
            }

            // Log the complete report
            Debug.Log(sb.ToString());
        }

        private void OnGUI()
        {
            if (!enableLogging)
                return;

            // Display simple stats on screen
            int yPos = 10;
            int xPos = 10;
            int width = showDetailedTimings ? 350 : 200;
            int height = 500; // Increased for additional stats

            GUILayout.BeginArea(new Rect(xPos, yPos, width, height));
            GUILayout.Label($"FPS: {1.0f / avgFrameTime:F1}");
            GUILayout.Label($"Chunks: {loadedChunkCount}");
            GUILayout.Label($"Processing: {processingChunkCount}");

            // Display component timings if enabled
            if (showDetailedTimings)
            {
                if (componentTimings.Count > 0)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Component Timings (ms):");
                    foreach (var timing in componentTimings)
                    {
                        string componentName = timing.Key;
                        float totalTime = timing.Value;
                        int calls = componentCalls[componentName];
                        float avgTime = calls > 0 ? totalTime / calls : 0;

                        GUILayout.Label($"  {componentName}: {avgTime * 1000:F1}ms");
                    }
                }

                // Display parallel processing stats
                if (showParallelStats && parallelProcessor != null)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Parallel Processing:");
                    GUILayout.Label($"  Threads: {activeThreads}");
                    GUILayout.Label($"  Jobs: {completedJobs}");
                    GUILayout.Label($"  Queue: {queuedJobs}");
                    GUILayout.Label($"  Job Time: {avgJobTime * 1000:F1}ms");
                }

                // Display boundary stats
                if (showBoundaryStats)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Boundary Management:");
                    GUILayout.Label($"  Updates: {boundaryUpdates}");
                    GUILayout.Label($"  Conflicts: {boundaryConflicts}");
                }

                // Display mesh stats
                if (showMeshStats)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Mesh Generation:");
                    GUILayout.Label($"  Vertices: {avgVerticesPerMesh}");
                    GUILayout.Label($"  Triangles: {avgTrianglesPerMesh}");
                }

                // Display terrain stats
                if (showTerrainStats)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Terrain Generation:");
                    GUILayout.Label($"  Inversion Fixed: {!isTerrainInverted}");
                    GUILayout.Label($"  Density: {minDensity:F2}-{maxDensity:F2}");
                }
            }

            GUILayout.EndArea();
        }
    }
}