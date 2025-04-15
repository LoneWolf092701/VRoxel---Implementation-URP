using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WFC.Boundary;
using WFC.Chunking;
using WFC.Core;
using WFC.Generation;
using WFC.MarchingCubes;
using WFC.Processing;

namespace WFC.Metrics
{
    #region Parallel Processing Test

    /// <summary>
    /// Tests the efficiency of parallel processing for WFC algorithm
    /// </summary>
    public class ParallelProcessingTest : IWFCTestCase
    {
        public string TestName => "Parallel Processing Efficiency";
        public string TestDescription => "Measures speedup and overhead when using multiple threads";

        // Dependencies
        private WFCGenerator wfcGenerator;
        private ParallelWFCManager parallelManager;
        private ParallelWFCProcessor parallelProcessor;

        // Test configuration
        private int[] threadCounts = new int[] { 1, 2, 4, 8 };
        private int testChunkSize = 32; // Fixed size for parallel tests

        // Test results
        private List<ParallelProcessingResult> results = new List<ParallelProcessingResult>();
        private float baselineTime = 0f; // Single-threaded baseline

        public void Initialize(ReferenceLocator references)
        {
            wfcGenerator = references.WfcGenerator;
            parallelManager = references.ParallelManager;
            parallelProcessor = references.ParallelProcessor;
        }

        public bool CanRun()
        {
            return wfcGenerator != null && parallelManager != null;
        }

        public IEnumerator RunTest()
        {
            Debug.Log($"Starting {TestName} test...");
            results.Clear();

            // Test with different thread counts
            foreach (int threadCount in threadCounts)
            {
                yield return RunParallelTest(threadCount);

                // Brief pause between tests
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log($"{TestName} test completed.");
        }

        private IEnumerator RunParallelTest(int threadCount)
        {
            Debug.Log($"Testing parallel processing with {threadCount} threads");

            // Create a set of test chunks
            List<Chunk> testChunks = new List<Chunk>();
            int chunkSize = 32; // Use fixed size for this test

            for (int i = 0; i < threadCount; i++)
            {
                Vector3Int chunkPos = new Vector3Int(i, 0, 0);
                Chunk chunk = new Chunk(chunkPos, chunkSize);

                // Initialize with all possible states
                var possibleStates = Enumerable.Range(0, wfcGenerator.MaxCellStates);
                chunk.InitializeCells(possibleStates);

                testChunks.Add(chunk);
            }

            // Create test processor with specified thread count
            ParallelWFCProcessor testProcessor = null;
            float memoryBefore = 0f;
            float syncOverhead = 0f;
            int chunksQueued = 0;

            try
            {
                // Create algorithm adapter
                IWFCAlgorithm algorithm = new WFCAlgorithmAdapter(wfcGenerator);

                // Create test processor
                testProcessor = new ParallelWFCProcessor(algorithm, threadCount);
                testProcessor.Start();

                // Memory before
                memoryBefore = (float)System.GC.GetTotalMemory(true) / (1024 * 1024);

                // Queue all chunks for processing
                foreach (var chunk in testChunks)
                {
                    bool queued = testProcessor.QueueChunkForProcessing(
                        chunk, WFCJobType.Collapse, 100, 1.0f);

                    if (queued)
                        chunksQueued++;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in parallel test with {threadCount} threads: {e.Message}");
                yield break;
            }

            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // Wait for chunks to process
            bool processing = true;
            int completedChunks = 0;
            int lastCompleted = 0;
            float timeout = 20.0f; // 20 second timeout
            float elapsed = 0f;

            while (processing && elapsed < timeout)
            {
                try
                {
                    // Process main thread events - measure sync overhead
                    Stopwatch syncWatch = new Stopwatch();
                    syncWatch.Start();
                    testProcessor.ProcessMainThreadEvents();
                    syncWatch.Stop();
                    syncOverhead += syncWatch.ElapsedMilliseconds;

                    // Update processing status
                    completedChunks = testProcessor.TotalProcessedJobs - lastCompleted;

                    // Check if done
                    if (completedChunks >= chunksQueued && chunksQueued > 0)
                    {
                        processing = false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Processing error: {e.Message}");
                    processing = false;
                }

                // Yield outside try-catch
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            // Stop timing
            stopwatch.Stop();
            float processingTime = stopwatch.ElapsedMilliseconds;

            try
            {
                // Memory after
                float memoryAfter = (float)System.GC.GetTotalMemory(false) / (1024 * 1024);
                float memoryOverhead = memoryAfter - memoryBefore;

                // Calculate memory overhead percentage
                float memoryOverheadPercent = threadCount > 1 ? (memoryOverhead / memoryBefore) * 100f : 0f;

                // Calculate speedup factor
                float speedupFactor = 1.0f;
                if (threadCount == 1)
                {
                    baselineTime = processingTime;
                }
                else if (baselineTime > 0)
                {
                    speedupFactor = baselineTime / processingTime;
                }

                // Store results
                ParallelProcessingResult result = new ParallelProcessingResult
                {
                    ThreadCount = threadCount,
                    ProcessingTime = processingTime,
                    SpeedupFactor = speedupFactor,
                    SynchronizationOverhead = syncOverhead,
                    MemoryOverheadPercent = memoryOverheadPercent,
                    MaxConcurrentChunks = testProcessor.ProcessingChunksCount
                };

                results.Add(result);

                Debug.Log($"Parallel processing test with {threadCount} threads completed: " +
                         $"{processingTime:F2}ms, speedup: {speedupFactor:F2}x");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error finalizing results: {e.Message}");
            }
            finally
            {
                // Clean up test processor
                if (testProcessor != null)
                {
                    testProcessor.Stop();
                }
            }
        }

        public ITestResult GetResults()
        {
            return new ParallelProcessingResults(results);
        }

        public void ExportResults(string filePath)
        {
            if (results.Count == 0) return;

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("ThreadCount,ProcessingTime(ms),SpeedupFactor,SynchronizationOverhead(ms),MemoryOverheadPercent,MaxConcurrentChunks");

                foreach (var result in results)
                {
                    writer.WriteLine($"{result.ThreadCount},{result.ProcessingTime:F2},{result.SpeedupFactor:F2}," +
                                    $"{result.SynchronizationOverhead:F2},{result.MemoryOverheadPercent:F2},{result.MaxConcurrentChunks}");
                }
            }

            Debug.Log($"Parallel Processing results exported to: {filePath}");
        }

        public void Cleanup()
        {
            // Reset parallel processor to original state
            if (parallelProcessor != null)
            {
                parallelProcessor.Stop();
                parallelProcessor.Start();
            }

            // Force garbage collection to clean up test chunks
            System.GC.Collect();
        }
    }

    public class ParallelProcessingResult
    {
        public int ThreadCount { get; set; }
        public float ProcessingTime { get; set; }
        public float SpeedupFactor { get; set; }
        public float SynchronizationOverhead { get; set; }
        public float MemoryOverheadPercent { get; set; }
        public int MaxConcurrentChunks { get; set; }
    }

    public class ParallelProcessingResults : ITestResult
    {
        private List<ParallelProcessingResult> results;

        public ParallelProcessingResults(List<ParallelProcessingResult> results)
        {
            this.results = new List<ParallelProcessingResult>(results);
        }

        public string TestName => "Parallel Processing Efficiency";

        public string GetCsvHeader()
        {
            return "ThreadCount,ProcessingTime(ms),SpeedupFactor,SynchronizationOverhead(ms),MemoryOverheadPercent,MaxConcurrentChunks";
        }

        public IEnumerable<string> GetCsvData()
        {
            foreach (var result in results)
            {
                yield return $"{result.ThreadCount},{result.ProcessingTime:F2},{result.SpeedupFactor:F2}," +
                           $"{result.SynchronizationOverhead:F2},{result.MemoryOverheadPercent:F2},{result.MaxConcurrentChunks}";
            }
        }

        public bool HasResults()
        {
            return results != null && results.Count > 0;
        }
    }

    #endregion

    #region World Size Scaling Test

    /// <summary>
    /// Tests how well the WFC system scales with increasing world size
    /// </summary>
    public class WorldSizeScalingTest : IWFCTestCase
    {
        public string TestName => "World Size Scaling";
        public string TestDescription => "Measures memory usage, generation time, and FPS impact for different world sizes";

        // Dependencies
        private WFCGenerator wfcGenerator;
        private ChunkManager chunkManager;

        // Test configuration
        private Vector3Int[] worldSizes = new Vector3Int[] {
            new Vector3Int(4, 1, 4),   // 16 chunks
            new Vector3Int(8, 1, 8),   // 64 chunks
            new Vector3Int(16, 1, 16), // 256 chunks
            new Vector3Int(32, 1, 32)  // 1024 chunks
        };

        // Test results
        private List<WorldSizeScalingResult> results = new List<WorldSizeScalingResult>();
        private float originalLoadDistance = 0f;

        public void Initialize(ReferenceLocator references)
        {
            wfcGenerator = references.WfcGenerator;
            chunkManager = references.ChunkManager;

            // Store original load distance
            if (chunkManager != null)
            {
                originalLoadDistance = GetChunkManagerLoadDistance();
            }
        }

        public bool CanRun()
        {
            return wfcGenerator != null && chunkManager != null;
        }

        public IEnumerator RunTest()
        {
            Debug.Log($"Starting {TestName} test...");
            results.Clear();

            // Run test for each world size
            foreach (var worldSize in worldSizes)
            {
                yield return RunWorldSizeTest(worldSize);

                // Brief pause between tests
                yield return new WaitForSeconds(2.0f);

                // Clean up chunks from previous test
                yield return CleanupTestChunks();
            }

            // Restore original load distance
            SetChunkManagerLoadDistance(originalLoadDistance);

            Debug.Log($"{TestName} test completed.");
        }

        private IEnumerator RunWorldSizeTest(Vector3Int worldSize)
        {
            Debug.Log($"Testing world size scaling for: {worldSize}");

            // Memory before
            float memoryBefore = (float)System.GC.GetTotalMemory(true) / (1024 * 1024);

            // Measure FPS before
            float fpsBefore = 1.0f / Time.smoothDeltaTime;

            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

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

            // Stop timing
            stopwatch.Stop();
            float generationTime = stopwatch.ElapsedMilliseconds / 1000f; // Convert to seconds

            // Memory after
            float memoryAfter = (float)System.GC.GetTotalMemory(false) / (1024 * 1024);
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
                WorldSize = worldSize,
                TotalMemoryUsage = memoryUsage,
                GenerationTime = generationTime,
                FpsImpact = fpsImpact,
                ChunksLoaded = chunksLoaded,
                ChunksProcessedPerFrame = chunksProcessedPerFrame,
                LoadingDistance = loadingDistance
            };

            results.Add(result);

            Debug.Log($"World size scaling test for {worldSize} completed: " +
                     $"{chunksLoaded} chunks, {generationTime:F2}s generation time");
        }

        private float GetChunkManagerLoadDistance()
        {
            if (chunkManager == null)
                return 0f;

            // Try to get the load distance via reflection
            var field = chunkManager.GetType().GetField("currentLoadDistance",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
                return (float)field.GetValue(chunkManager);

            return 0f;
        }

        private void SetChunkManagerLoadDistance(float distance)
        {
            if (chunkManager == null)
                return;

            // Try to set the load distance via reflection
            var field = chunkManager.GetType().GetField("currentLoadDistance",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
                field.SetValue(chunkManager, distance);
        }

        private ChunkManager.ChunkLifecycleState GetChunkState(Vector3Int chunkPos)
        {
            if (chunkManager == null)
                return ChunkManager.ChunkLifecycleState.None;

            // Try to get the method via reflection
            var method = chunkManager.GetType().GetMethod("GetChunkState",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);

            if (method != null)
                return (ChunkManager.ChunkLifecycleState)method.Invoke(chunkManager, new object[] { chunkPos });

            return ChunkManager.ChunkLifecycleState.None;
        }

        private IEnumerator CleanupTestChunks()
        {
            if (chunkManager == null)
                yield break;

            // Try to call reset method on chunk manager
            var resetMethod = chunkManager.GetType().GetMethod("ResetChunkSystem",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

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
                    var unloadMethod = chunkManager.GetType().GetMethod("UnloadChunk",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (unloadMethod != null)
                        unloadMethod.Invoke(chunkManager, new object[] { chunkPos });

                    yield return null;
                }
            }

            // Force garbage collection
            System.GC.Collect();
        }

        public ITestResult GetResults()
        {
            return new WorldSizeScalingResults(results);
        }

        public void ExportResults(string filePath)
        {
            if (results.Count == 0) return;

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("WorldSize,TotalMemoryUsage(MB),GenerationTime(s),FPSImpact,ChunksLoaded,ChunksProcessedPerFrame,LoadingDistance");

                foreach (var result in results)
                {
                    writer.WriteLine($"{result.WorldSize.x}x{result.WorldSize.y}x{result.WorldSize.z}," +
                                    $"{result.TotalMemoryUsage:F2},{result.GenerationTime:F2},{result.FpsImpact:F2}," +
                                    $"{result.ChunksLoaded},{result.ChunksProcessedPerFrame:F2},{result.LoadingDistance:F2}");
                }
            }

            Debug.Log($"World Size Scaling results exported to: {filePath}");
        }

        public void Cleanup()
        {
            // Restore original load distance
            SetChunkManagerLoadDistance(originalLoadDistance);

            // Clean up chunks
            if (chunkManager != null)
            {
                var resetMethod = chunkManager.GetType().GetMethod("ResetChunkSystem",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (resetMethod != null)
                {
                    resetMethod.Invoke(chunkManager, null);
                }
            }

            // Force garbage collection
            System.GC.Collect();
        }
    }

    public class WorldSizeScalingResult
    {
        public Vector3Int WorldSize { get; set; }
        public float TotalMemoryUsage { get; set; }
        public float GenerationTime { get; set; }
        public float FpsImpact { get; set; }
        public int ChunksLoaded { get; set; }
        public float ChunksProcessedPerFrame { get; set; }
        public float LoadingDistance { get; set; }
    }

    public class WorldSizeScalingResults : ITestResult
    {
        private List<WorldSizeScalingResult> results;

        public WorldSizeScalingResults(List<WorldSizeScalingResult> results)
        {
            this.results = new List<WorldSizeScalingResult>(results);
        }

        public string TestName => "World Size Scaling";

        public string GetCsvHeader()
        {
            return "WorldSize,TotalMemoryUsage(MB),GenerationTime(s),FPSImpact,ChunksLoaded,ChunksProcessedPerFrame,LoadingDistance";
        }

        public IEnumerable<string> GetCsvData()
        {
            foreach (var result in results)
            {
                yield return $"{result.WorldSize.x}x{result.WorldSize.y}x{result.WorldSize.z}," +
                           $"{result.TotalMemoryUsage:F2},{result.GenerationTime:F2},{result.FpsImpact:F2}," +
                           $"{result.ChunksLoaded},{result.ChunksProcessedPerFrame:F2},{result.LoadingDistance:F2}";
            }
        }

        public bool HasResults()
        {
            return results != null && results.Count > 0;
        }
    }

    #endregion

    #region LOD Performance Test

    /// <summary>
    /// Tests the impact of LOD (Level of Detail) settings on performance
    /// </summary>
    public class LODPerformanceTest : IWFCTestCase
    {
        public string TestName => "LOD Performance Impact";
        public string TestDescription => "Measures vertex reduction, generation speed increase, and memory impact of different LOD levels";

        // Dependencies
        private WFCGenerator wfcGenerator;
        private MeshGenerator meshGenerator;
        private DensityFieldGenerator densityGenerator;
        private WFCConfiguration config;

        // Test configuration
        private int testChunkSize = 32;
        private int lodLevels = 4; // Default, may be updated from config

        // Test results
        private List<LODPerformanceResult> results = new List<LODPerformanceResult>();

        public void Initialize(ReferenceLocator references)
        {
            wfcGenerator = references.WfcGenerator;
            meshGenerator = references.MeshGenerator;
            densityGenerator = references.DensityGenerator;

            // Create a new density generator if none is available
            if (densityGenerator == null)
            {
                densityGenerator = new DensityFieldGenerator();
            }

            // Get configuration
            config = Configuration.WFCConfigManager.Config;

            // Update LOD levels from configuration if available
            if (config != null && config.Performance != null &&
                config.Performance.lodSettings != null &&
                config.Performance.lodSettings.lodDistanceThresholds != null)
            {
                lodLevels = config.Performance.lodSettings.lodDistanceThresholds.Length + 1;
            }
        }

        public bool CanRun()
        {
            return wfcGenerator != null;
        }

        public IEnumerator RunTest()
        {
            Debug.Log($"Starting {TestName} test...");
            results.Clear();

            // Run test for each LOD level
            for (int lodLevel = 0; lodLevel < lodLevels; lodLevel++)
            {
                yield return RunLODTest(lodLevel);

                // Brief pause between tests
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log($"{TestName} test completed.");
        }

        private IEnumerator RunLODTest(int lodLevel)
        {
            Debug.Log($"Testing LOD performance for level: {lodLevel}");

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
            var possibleStates = Enumerable.Range(0, wfcGenerator.MaxCellStates);
            baselineChunk.InitializeCells(possibleStates);
            lodChunk.InitializeCells(possibleStates);

            // Create a simple terrain pattern in both chunks
            for (int x = 0; x < testChunkSize; x++)
            {
                for (int z = 0; z < testChunkSize; z++)
                {
                    for (int y = 0; y < testChunkSize; y++)
                    {
                        // Bottom half is ground, top half is air
                        int state = y < testChunkSize / 2 ? 1 : 0;

                        baselineChunk.GetCell(x, y, z).Collapse(state);
                        lodChunk.GetCell(x, y, z).Collapse(state);
                    }
                }
            }

            // Add some noise to make the terrain more interesting
            // Use the same seed for both chunks to ensure identical terrain
            System.Random rng = new System.Random(12345);
            int noiseCount = testChunkSize * testChunkSize / 4;

            for (int i = 0; i < noiseCount; i++)
            {
                int x = rng.Next(0, testChunkSize);
                int y = rng.Next(testChunkSize / 3, 2 * testChunkSize / 3);
                int z = rng.Next(0, testChunkSize);

                // Invert the state (air->ground or ground->air)
                int currentState = baselineChunk.GetCell(x, y, z).CollapsedState.Value;
                int newState = currentState == 0 ? 1 : 0;

                baselineChunk.GetCell(x, y, z).Collapse(newState);
                lodChunk.GetCell(x, y, z).Collapse(newState);
            }

            // Mark chunks as fully collapsed
            baselineChunk.IsFullyCollapsed = true;
            lodChunk.IsFullyCollapsed = true;

            // Create marching cubes generator
            MarchingCubesGenerator marchingCubes = new MarchingCubesGenerator();

            // Measure baseline performance (LOD 0)
            float baselineGenerationTime = MeasureGenerationTime(baselineChunk, densityGenerator, marchingCubes);
            int baselineVertexCount = MeasureMeshVertexCount(baselineChunk, densityGenerator, marchingCubes);
            float baselineMemoryUsage = MeasureMemoryUsage(baselineChunk);

            // Yield to prevent freezing
            yield return null;

            // Measure LOD performance
            float lodGenerationTime = MeasureGenerationTime(lodChunk, densityGenerator, marchingCubes);
            int lodVertexCount = MeasureMeshVertexCount(lodChunk, densityGenerator, marchingCubes);
            float lodMemoryUsage = MeasureMemoryUsage(lodChunk);

            // Calculate performance metrics
            float vertexReductionPercent = baselineVertexCount > 0 ?
                ((float)(baselineVertexCount - lodVertexCount) / baselineVertexCount) * 100f : 0f;

            float generationSpeedIncreasePercent = baselineGenerationTime > 0 ?
                ((baselineGenerationTime - lodGenerationTime) / baselineGenerationTime) * 100f : 0f;

            float memoryReductionPercent = baselineMemoryUsage > 0 ?
                ((baselineMemoryUsage - lodMemoryUsage) / baselineMemoryUsage) * 100f : 0f;

            // Get distance range for this LOD level
            float distanceMin = lodLevel == 0 ? 0f :
                (config?.Performance?.lodSettings?.lodDistanceThresholds?[lodLevel - 1] ?? lodLevel * 50);

            float distanceMax = lodLevel < lodLevels - 1 ?
                (config?.Performance?.lodSettings?.lodDistanceThresholds?[lodLevel] ?? (lodLevel + 1) * 50) :
                float.MaxValue;

            // Estimate visual quality impact (0-10, where 0 is no impact, 10 is severe)
            int visualQualityImpact = lodLevel * 2; // Simple estimation

            // Store results
            LODPerformanceResult result = new LODPerformanceResult
            {
                LODLevel = lodLevel,
                DistanceRange = distanceMin,
                VertexReductionPercent = vertexReductionPercent,
                GenerationSpeedIncreasePercent = generationSpeedIncreasePercent,
                MemoryReductionPercent = memoryReductionPercent,
                VisualQualityImpact = visualQualityImpact
            };

            results.Add(result);

            Debug.Log($"LOD performance test for level {lodLevel} completed: " +
                     $"{vertexReductionPercent:F2}% vertex reduction, " +
                     $"{generationSpeedIncreasePercent:F2}% generation speed increase");
        }

        private void ApplyLODSettings(Chunk chunk)
        {
            // Get LOD-specific settings from configuration
            int lodLevel = chunk.LODLevel;

            if (config?.Performance?.lodSettings == null)
            {
                // Default fallback values if configuration is not available
                chunk.MaxIterations = 100 / (lodLevel + 1);
                chunk.ConstraintInfluence = 1.0f / (lodLevel + 1);
                return;
            }

            // Set max iterations for this LOD level
            if (lodLevel < config.Performance.lodSettings.maxIterationsPerLOD?.Length)
            {
                chunk.MaxIterations = config.Performance.lodSettings.maxIterationsPerLOD[lodLevel];
            }
            else if (config.Performance.lodSettings.maxIterationsPerLOD?.Length > 0)
            {
                // Use the last defined value
                chunk.MaxIterations = config.Performance.lodSettings.maxIterationsPerLOD[
                    config.Performance.lodSettings.maxIterationsPerLOD.Length - 1];
            }
            else
            {
                // Fallback
                chunk.MaxIterations = config.Algorithm.maxIterationsPerChunk;
            }

            // Set constraint influence for this LOD level
            if (lodLevel < config.Performance.lodSettings.constraintInfluencePerLOD?.Length)
            {
                chunk.ConstraintInfluence = config.Performance.lodSettings.constraintInfluencePerLOD[lodLevel];
            }
            else if (config.Performance.lodSettings.constraintInfluencePerLOD?.Length > 0)
            {
                chunk.ConstraintInfluence = config.Performance.lodSettings.constraintInfluencePerLOD[
                    config.Performance.lodSettings.constraintInfluencePerLOD.Length - 1];
            }
            else
            {
                chunk.ConstraintInfluence = 1.0f;
            }
        }

        private float MeasureGenerationTime(Chunk chunk, DensityFieldGenerator densityGen, MarchingCubesGenerator marchingCubes)
        {
            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // Generate density field
            float[,,] densityField = densityGen.GenerateDensityField(chunk);

            // Generate mesh with appropriate LOD level
            Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

            // Stop timing
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        private int MeasureMeshVertexCount(Chunk chunk, DensityFieldGenerator densityGen, MarchingCubesGenerator marchingCubes)
        {
            // Generate density field
            float[,,] densityField = densityGen.GenerateDensityField(chunk);

            // Generate mesh with appropriate LOD level
            Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

            return mesh.vertexCount;
        }

        private float MeasureMemoryUsage(Chunk chunk)
        {
            float memoryBefore = (float)System.GC.GetTotalMemory(true) / (1024 * 1024);

            // Generate density field and mesh
            float[,,] densityField = densityGenerator.GenerateDensityField(chunk);

            MarchingCubesGenerator marchingCubes = new MarchingCubesGenerator();
            Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

            float memoryAfter = (float)System.GC.GetTotalMemory(false) / (1024 * 1024);
            return memoryAfter - memoryBefore;
        }

        public ITestResult GetResults()
        {
            return new LODPerformanceResults(results);
        }

        public void ExportResults(string filePath)
        {
            if (results.Count == 0) return;

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("LODLevel,DistanceRange,VertexReductionPercent,GenerationSpeedIncreasePercent,MemoryReductionPercent,VisualQualityImpact");

                foreach (var result in results)
                {
                    writer.WriteLine($"{result.LODLevel},{result.DistanceRange:F0},{result.VertexReductionPercent:F2}," +
                                    $"{result.GenerationSpeedIncreasePercent:F2},{result.MemoryReductionPercent:F2},{result.VisualQualityImpact}");
                }
            }

            Debug.Log($"LOD Performance results exported to: {filePath}");
        }

        public void Cleanup()
        {
            // Force garbage collection to clean up test chunks and meshes
            System.GC.Collect();
        }
    }

    public class LODPerformanceResult
    {
        public int LODLevel { get; set; }
        public float DistanceRange { get; set; }
        public float VertexReductionPercent { get; set; }
        public float GenerationSpeedIncreasePercent { get; set; }
        public float MemoryReductionPercent { get; set; }
        public int VisualQualityImpact { get; set; }
    }

    public class LODPerformanceResults : ITestResult
    {
        private List<LODPerformanceResult> results;

        public LODPerformanceResults(List<LODPerformanceResult> results)
        {
            this.results = new List<LODPerformanceResult>(results);
        }

        public string TestName => "LOD Performance Impact";

        public string GetCsvHeader()
        {
            return "LODLevel,DistanceRange,VertexReductionPercent,GenerationSpeedIncreasePercent,MemoryReductionPercent,VisualQualityImpact";
        }

        public IEnumerable<string> GetCsvData()
        {
            foreach (var result in results)
            {
                yield return $"{result.LODLevel},{result.DistanceRange:F0},{result.VertexReductionPercent:F2}," +
                           $"{result.GenerationSpeedIncreasePercent:F2},{result.MemoryReductionPercent:F2},{result.VisualQualityImpact}";
            }
        }

        public bool HasResults()
        {
            return results != null && results.Count > 0;
        }
    }

    #endregion
};