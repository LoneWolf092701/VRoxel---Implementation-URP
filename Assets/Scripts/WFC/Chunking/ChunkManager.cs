// Assets/Scripts/WFC/Chunking/ChunkManagerUpdated.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using System.Linq;
using Utils; // For PriorityQueue
using WFC.Processing; // For WFCGenerator
using WFC.Generation;
using WFC.Configuration; // For configuration
using System.Collections;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using WFC.Performance;

namespace WFC.Chunking
{
    /// <summary>
    /// Updated ChunkManager that uses the central configuration system
    /// and implements advanced predictive generation
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Override the global configuration with a specific asset")]
        [SerializeField] private WFCConfiguration configOverride;

        [Header("References")]
        [SerializeField] private Transform viewer;
        [SerializeField] private WFCGenerator wfcGenerator; // Reference from inspector
        [SerializeField] private PerformanceMonitor performanceMonitor;

        [Header("Prediction Settings")]
        [SerializeField] private int movementHistorySize = 60; // Store 60 frames of movement history
        [SerializeField] private float basePredictionTime = 2.0f; // Default look ahead time

        // Loaded chunks
        private Dictionary<Vector3Int, Chunk> loadedChunks = new Dictionary<Vector3Int, Chunk>();

        // Tasks queue
        private PriorityQueue<ChunkTask, float> chunkTasks = new PriorityQueue<ChunkTask, float>();

        // Parallel processing
        private ParallelWFCProcessor parallelProcessor;
        private bool useParallelProcessing = true;

        // Viewer data
        private Vector3 viewerPosition;
        private Vector3 viewerVelocity;
        private Vector3 lastViewerPosition;

        private Vector3 predictedViewerPosition;
        private Vector3 viewerAcceleration;
        private Vector3 lastViewerVelocity;
        private float predictionTime = 2.0f; // Look ahead time (adaptive)

        // Movement history for advanced prediction
        private Queue<MovementSample> movementHistory = new Queue<MovementSample>();

        // Adaptive loading parameters
        private float currentLoadDistance;
        private int maxConcurrentChunks;
        private HierarchicalConstraintSystem hierarchicalConstraints;

        // Cache for configuration access
        private WFCConfiguration activeConfig;

        // Properties that now use configuration
        private int ChunkSize => activeConfig.World.chunkSize;
        private float LoadDistance => currentLoadDistance; // Now using adaptive distance
        private float UnloadDistance => activeConfig.Performance.unloadDistance;
        private int MaxConcurrentChunks => maxConcurrentChunks; // Now adaptive

        private void Start()
        {
            // Get the configuration - use override if specified, otherwise use global config
            activeConfig = configOverride != null ? configOverride : WFCConfigManager.Config;

            if (activeConfig == null)
            {
                Debug.LogError("ChunkManager: No configuration available. Please assign a WFCConfiguration asset.");
                enabled = false;
                return;
            }

            // Validate configuration
            if (!activeConfig.Validate())
            {
                Debug.LogWarning("ChunkManager: Using configuration with validation issues.");
            }

            if (viewer == null)
                viewer = Camera.main.transform;

            viewerPosition = viewer.position;
            lastViewerPosition = viewerPosition;

            // Initialize adaptive parameters
            currentLoadDistance = activeConfig.Performance.loadDistance;
            maxConcurrentChunks = activeConfig.Performance.maxConcurrentChunks;

            var adapter = new WFCAlgorithmAdapter(wfcGenerator);

            // Get hierarchical constraint system reference
            if (wfcGenerator != null)
            {
                hierarchicalConstraints = wfcGenerator.GetHierarchicalConstraintSystem();
            }

            if (useParallelProcessing && wfcGenerator != null)
            {
                // Create processor with WFC algorithm reference and thread count from config
                parallelProcessor = new ParallelWFCProcessor(adapter, activeConfig.Performance.maxThreads);
                parallelProcessor.Start();
            }

            // Start adaptive strategy coroutine
            StartCoroutine(AdaptiveStrategyUpdateCoroutine());
        }

        private void Update()
        {
            // Update viewer position and velocity
            viewerPosition = viewer.position;
            Vector3 newVelocity = (viewerPosition - lastViewerPosition) / Time.deltaTime;
            viewerAcceleration = (newVelocity - viewerVelocity) / Time.deltaTime;
            lastViewerVelocity = viewerVelocity;
            viewerVelocity = newVelocity;
            lastViewerPosition = viewerPosition;

            // Add to movement history for advanced prediction
            if (movementHistory.Count >= movementHistorySize)
                movementHistory.Dequeue();

            movementHistory.Enqueue(new MovementSample(viewerPosition, viewerVelocity, Time.time));

            // Calculate predicted position using advanced prediction
            predictedViewerPosition = PredictFuturePosition(predictionTime);

            // Update chunk priorities using the improved prediction
            UpdateChunkPriorities();

            // NEW: Assign LOD levels based on distance
            AssignLODLevels();

            // Manage chunks (load/unload)
            ManageChunks();

            // Process chunk tasks
            ProcessChunkTasks();

            // Optimize memory for inactive chunks
            OptimizeInactiveChunks();
        }

        // Add this method to ChunkManager.cs
        private void AssignLODLevels()
        {
            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("AssignLODLevels");

            foreach (var chunk in loadedChunks.Values)
            {
                // Calculate distance from viewer
                Vector3 chunkWorldPos = GetChunkWorldPosition(chunk.Position);
                float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

                // Determine LOD level based on distance
                int lodLevel = 0; // Default to highest detail

                for (int i = 0; i < activeConfig.Performance.lodSettings.lodDistanceThresholds.Length; i++)
                {
                    if (distance > activeConfig.Performance.lodSettings.lodDistanceThresholds[i])
                    {
                        lodLevel = i + 1;
                    }
                    else
                    {
                        break;
                    }
                }

                // Only update if LOD level changed
                if (chunk.LODLevel != lodLevel)
                {
                    chunk.LODLevel = lodLevel;
                    ApplyLODSettings(chunk);
                }
            }

            if (performanceMonitor != null)
                performanceMonitor.EndComponentTiming("AssignLODLevels");
        }

        private void ApplyLODSettings(Chunk chunk)
        {
            // Get LOD-specific settings
            int lodLevel = chunk.LODLevel;
            int maxIterations;
            float constraintInfluence;

            // Get max iterations for this LOD level
            if (lodLevel < activeConfig.Performance.lodSettings.maxIterationsPerLOD.Length)
            {
                maxIterations = activeConfig.Performance.lodSettings.maxIterationsPerLOD[lodLevel];
            }
            else if (activeConfig.Performance.lodSettings.maxIterationsPerLOD.Length > 0)
            {
                // Use the last defined value
                maxIterations = activeConfig.Performance.lodSettings.maxIterationsPerLOD[
                    activeConfig.Performance.lodSettings.maxIterationsPerLOD.Length - 1];
            }
            else
            {
                // Fallback to default
                maxIterations = activeConfig.Algorithm.maxIterationsPerChunk;
            }

            // Get constraint influence for this LOD level
            if (lodLevel < activeConfig.Performance.lodSettings.constraintInfluencePerLOD.Length)
            {
                constraintInfluence = activeConfig.Performance.lodSettings.constraintInfluencePerLOD[lodLevel];
            }
            else if (activeConfig.Performance.lodSettings.constraintInfluencePerLOD.Length > 0)
            {
                // Use the last defined value
                constraintInfluence = activeConfig.Performance.lodSettings.constraintInfluencePerLOD[
                    activeConfig.Performance.lodSettings.constraintInfluencePerLOD.Length - 1];
            }
            else
            {
                // Fallback to full influence
                constraintInfluence = 1.0f;
            }

            // Apply the settings to the chunk
            chunk.MaxIterations = maxIterations;
            chunk.ConstraintInfluence = constraintInfluence;

            // Mark for mesh update if needed
            if (chunk.IsFullyCollapsed && chunk.IsDirty == false)
            {
                chunk.IsDirty = true;
            }
        }

        // Helper method to get chunk world position
        private Vector3 GetChunkWorldPosition(Vector3Int chunkPos)
        {
            return new Vector3(
                chunkPos.x * ChunkSize,
                chunkPos.y * ChunkSize,
                chunkPos.z * ChunkSize
            );
        }

        #region Advanced Movement Prediction

        /// <summary>
        /// Advanced prediction system that analyzes movement patterns to predict future position
        /// </summary>
        private Vector3 PredictFuturePosition(float predictionTimeSeconds)
        {
            // If not enough history, use simple prediction
            if (movementHistory.Count < 3)
            {
                return viewerPosition +
                       viewerVelocity * predictionTimeSeconds +
                       0.5f * viewerAcceleration * predictionTimeSeconds * predictionTimeSeconds;
            }

            // Calculate acceleration trends (not just instantaneous)
            Vector3 avgAcceleration = CalculateAverageAcceleration(movementHistory);

            // Detect if player is following a path vs random movement
            float directionConsistency = CalculateDirectionConsistency(movementHistory);

            // Adjust prediction time based on movement consistency
            float adjustedPredictionTime = predictionTimeSeconds * Mathf.Lerp(0.5f, 1.5f, directionConsistency);

            // Enhanced prediction with jerk (rate of change of acceleration)
            Vector3 jerk = CalculateJerk(movementHistory);

            // Apply complete prediction formula with jerk
            return viewerPosition +
                   viewerVelocity * adjustedPredictionTime +
                   0.5f * avgAcceleration * adjustedPredictionTime * adjustedPredictionTime +
                   (1f / 6f) * jerk * Mathf.Pow(adjustedPredictionTime, 3);
        }

        private float CalculateDirectionConsistency(Queue<MovementSample> history)
        {
            // Higher values (closer to 1.0) indicate more consistent direction
            // Lower values indicate erratic movement
            if (history.Count < 3) return 0.8f; // Default value

            Vector3 avgDirection = Vector3.zero;
            float totalSamples = 0;

            foreach (var sample in history)
            {
                if (sample.velocity.magnitude > 0.1f)
                {
                    avgDirection += sample.velocity.normalized;
                    totalSamples++;
                }
            }

            if (totalSamples < 1) return 0.8f;

            avgDirection /= totalSamples;
            return Mathf.Clamp01(avgDirection.magnitude); // 1.0 = perfectly consistent
        }

        private Vector3 CalculateAverageAcceleration(Queue<MovementSample> history)
        {
            if (history.Count < 3) return viewerAcceleration;

            Vector3 avgAcceleration = Vector3.zero;
            int count = 0;

            // Get array representation for easier index access
            MovementSample[] samples = history.ToArray();

            // Calculate acceleration between consecutive samples
            for (int i = 2; i < samples.Length; i++)
            {
                float timeDelta = samples[i].timestamp - samples[i - 1].timestamp;
                if (timeDelta > 0.001f) // Avoid division by very small numbers
                {
                    Vector3 accel = (samples[i].velocity - samples[i - 1].velocity) / timeDelta;
                    avgAcceleration += accel;
                    count++;
                }
            }

            if (count > 0)
                return avgAcceleration / count;

            return viewerAcceleration; // Fallback
        }

        private Vector3 CalculateJerk(Queue<MovementSample> history)
        {
            if (history.Count < 4) return Vector3.zero;

            Vector3 avgJerk = Vector3.zero;
            int count = 0;

            // Get array representation for easier index access
            MovementSample[] samples = history.ToArray();

            // Calculate jerk (derivative of acceleration) between samples
            for (int i = 3; i < samples.Length; i++)
            {
                float timeDelta1 = samples[i - 1].timestamp - samples[i - 2].timestamp;
                float timeDelta2 = samples[i].timestamp - samples[i - 1].timestamp;

                if (timeDelta1 > 0.001f && timeDelta2 > 0.001f)
                {
                    Vector3 accel1 = (samples[i - 1].velocity - samples[i - 2].velocity) / timeDelta1;
                    Vector3 accel2 = (samples[i].velocity - samples[i - 1].velocity) / timeDelta2;
                    float jerkTimeDelta = samples[i].timestamp - samples[i - 2].timestamp;

                    if (jerkTimeDelta > 0.001f)
                    {
                        Vector3 jerk = (accel2 - accel1) / jerkTimeDelta;
                        avgJerk += jerk;
                        count++;
                    }
                }
            }

            if (count > 0)
                return avgJerk / count;

            return Vector3.zero; // Fallback
        }

        // Class to store movement samples for prediction
        private class MovementSample
        {
            public Vector3 position;
            public Vector3 velocity;
            public float timestamp;

            public MovementSample(Vector3 position, Vector3 velocity, float timestamp)
            {
                this.position = position;
                this.velocity = velocity;
                this.timestamp = timestamp;
            }
        }

        #endregion

        #region Interest-Based Prioritization

        /// <summary>
        /// Calculate how interesting a chunk is based on multiple factors
        /// </summary>
        private float CalculateChunkInterest(Vector3Int chunkPos)
        {
            float interestScore = 0f;

            // Convert to world position
            Vector3 chunkWorldPos = GetChunkWorldPosition(chunkPos);

            // 1. Distance-based interest (closer = more interesting)
            float distance = Vector3.Distance(chunkWorldPos, viewerPosition);
            float distanceInterest = 1f / (1f + distance * 0.01f);

            // 2. View-based interest (in field of view = more interesting)
            float viewAlignment = GetViewAlignment(chunkPos);
            float viewInterest = Mathf.Clamp01((viewAlignment + 1) * 0.5f);

            // 3. Content-based interest (partially generated chunks = more interesting)
            float contentInterest = 0f;
            if (loadedChunks.TryGetValue(chunkPos, out Chunk chunk))
            {
                // Partially collapsed chunks are more interesting to finish
                if (!chunk.IsFullyCollapsed)
                {
                    // Calculate percentage of collapsed cells
                    float collapsedPercentage = CalculateCollapsedPercentage(chunk);
                    // Chunks that are 30-70% complete get priority
                    contentInterest = 1f - Mathf.Abs(collapsedPercentage - 0.5f) * 2f;
                }
            }

            // 4. Feature-based interest (chunks with special features = more interesting)
            float featureInterest = 0f;
            // Check if this chunk is part of a global constraint region
            if (hierarchicalConstraints != null)
            {
                foreach (var constraint in hierarchicalConstraints.GetGlobalConstraints())
                {
                    float influence = constraint.GetInfluenceAt(chunkWorldPos + new Vector3(ChunkSize / 2, ChunkSize / 2, ChunkSize / 2));
                    featureInterest = Mathf.Max(featureInterest, influence);
                }
            }

            // Combine interest factors with weights
            interestScore = distanceInterest * 0.4f +
                            viewInterest * 0.3f +
                            contentInterest * 0.2f +
                            featureInterest * 0.1f;

            return interestScore;
        }

        private float GetViewAlignment(Vector3Int chunkPos)
        {
            // Calculate how aligned the chunk is with the viewer's look direction
            Vector3 chunkCenter = GetChunkWorldPosition(chunkPos) +
                                  new Vector3(ChunkSize / 2, ChunkSize / 2, ChunkSize / 2);

            Vector3 dirToChunk = (chunkCenter - viewerPosition).normalized;
            Vector3 viewDirection = viewer.forward.normalized;

            return Vector3.Dot(dirToChunk, viewDirection);
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

        //private Vector3 GetChunkWorldPosition(Vector3Int chunkPos)
        //{
        //    return new Vector3(
        //        chunkPos.x * ChunkSize,
        //        chunkPos.y * ChunkSize,
        //        chunkPos.z * ChunkSize
        //    );
        //}

        #endregion

        #region Adaptive Chunk Generation Strategy

        private IEnumerator AdaptiveStrategyUpdateCoroutine()
        {
            // Update strategy less frequently than every frame
            while (true)
            {
                AdaptChunkGenerationStrategy();
                yield return new WaitForSeconds(0.5f); // Update every half second
            }
        }

        private void AdaptChunkGenerationStrategy()
        {
            // Update strategy based on performance metrics and viewer behavior

            // 1. Adjust prediction time based on viewer speed
            float viewerSpeed = viewerVelocity.magnitude;
            predictionTime = Mathf.Lerp(basePredictionTime, basePredictionTime * 2.5f,
                                        Mathf.Clamp01(viewerSpeed / 20f));

            // 2. Adjust generation radius based on performance
            float fps = 1f / Time.smoothDeltaTime;
            float targetFps = 60f;

            if (fps > targetFps * 1.2f) // Performance is good, can increase load distance
            {
                currentLoadDistance = Mathf.Min(
                    currentLoadDistance + 10f * Time.deltaTime,
                    activeConfig.Performance.loadDistance * 1.5f);
            }
            else if (fps < targetFps * 0.8f) // Performance is struggling, reduce load distance
            {
                currentLoadDistance = Mathf.Max(
                    currentLoadDistance - 20f * Time.deltaTime,
                    activeConfig.Performance.loadDistance * 0.6f);
            }

            // 3. Adjust max concurrent chunks based on performance
            float systemLoadFactor = 1.0f;

            // Estimate system load based on frame time
            if (fps < 30)
                systemLoadFactor = 0.6f;
            else if (fps < 45)
                systemLoadFactor = 0.8f;
            else if (fps > 100)
                systemLoadFactor = 1.2f;

            int newConcurrentChunks = Mathf.FloorToInt(
                activeConfig.Performance.maxConcurrentChunks * systemLoadFactor);

            maxConcurrentChunks = Mathf.Clamp(newConcurrentChunks, 4, 32);

            // Log strategy updates (once in a while)
            if (Time.frameCount % 300 == 0)
            {
                Debug.Log($"Adaptive strategy update: Prediction={predictionTime:F1}s, " +
                          $"LoadDistance={currentLoadDistance:F1}, MaxChunks={maxConcurrentChunks}");
            }
        }

        private void OptimizeInactiveChunks()
        {
            foreach (var chunkEntry in loadedChunks)
            {
                Vector3Int chunkPos = chunkEntry.Key;
                Chunk chunk = chunkEntry.Value;

                // Calculate distance from viewer
                float distance = Vector3.Distance(
                    GetChunkWorldPosition(chunkPos),
                    viewerPosition);

                // Far chunks (beyond unload distance but still kept)
                if (distance > UnloadDistance * 0.8f)
                {
                    // Check if chunk is being processed
                    bool isProcessing = false;
                    if (parallelProcessor != null)
                    {
                        isProcessing = parallelProcessor.IsChunkBeingProcessed(chunkPos);
                    }

                    if (!isProcessing && !chunk.IsMemoryOptimized)
                    {
                        OptimizeChunkMemory(chunk);
                        chunk.IsMemoryOptimized = true;
                    }
                }
                else if (chunk.IsMemoryOptimized && distance < UnloadDistance * 0.7f)
                {
                    // Restore full chunk data when coming back into range
                    RestoreChunkFromOptimized(chunk);
                    chunk.IsMemoryOptimized = false;
                }
            }
        }

        private void OptimizeChunkMemory(Chunk chunk)
        {
            // This would be implemented in the Chunk class
            chunk.OptimizeMemory();
        }

        private void RestoreChunkFromOptimized(Chunk chunk)
        {
            // This would be implemented in the Chunk class
            chunk.RestoreFromOptimized();
        }

        #endregion

        private void UpdateChunkPriorities()
        {
            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("ChunkPriorities");

            foreach (var chunk in loadedChunks.Values)
            {
                chunk.Priority = CalculateChunkPriority(chunk);
            }

            if (performanceMonitor != null)
                performanceMonitor.EndComponentTiming("ChunkPriorities");
        }

        private float CalculateChunkPriority(Chunk chunk)
        {
            // Convert chunk position to world position
            Vector3 chunkWorldPos = new Vector3(
                chunk.Position.x * ChunkSize,
                chunk.Position.y * ChunkSize,
                chunk.Position.z * ChunkSize
            );

            // Distance from viewer
            float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

            // View alignment (dot product of direction to chunk and predicted movement)
            Vector3 dirToChunk = (chunkWorldPos - viewerPosition).normalized;
            Vector3 predictedDirection = (predictedViewerPosition - viewerPosition).normalized;
            float viewAlignment = Vector3.Dot(dirToChunk, predictedDirection);

            // Interest-based factor
            float interestFactor = CalculateChunkInterest(chunk.Position);

            // Calculate priority (higher is more important)
            float distanceFactor = 1.0f / (distance + 1.0f);
            float alignmentFactor = viewAlignment > 0 ? (1.0f + viewAlignment) : 0.5f;

            // Combine factors with weights
            float priority = distanceFactor * 0.4f +
                             alignmentFactor * 0.2f +
                             interestFactor * 0.4f;

            // Bonus for chunks that are partially collapsed
            if (!chunk.IsFullyCollapsed)
            {
                priority *= 1.2f;
            }

            return priority;
        }

        private void ManageChunks()
        {
            // Get chunks to load
            List<Vector3Int> chunksToLoad = GetChunksToLoad();

            foreach (var chunkPos in chunksToLoad)
            {
                if (!loadedChunks.ContainsKey(chunkPos))
                {
                    // Create chunk load task
                    ChunkTask task = new ChunkTask
                    {
                        Type = ChunkTaskType.Create,
                        Position = chunkPos,
                        Priority = CalculateLoadPriority(chunkPos)
                    };

                    chunkTasks.Enqueue(task, task.Priority);
                }
            }

            // Get chunks to unload
            List<Vector3Int> chunksToUnload = GetChunksToUnload();

            foreach (var chunkPos in chunksToUnload)
            {
                if (loadedChunks.ContainsKey(chunkPos))
                {
                    // Create chunk unload task
                    ChunkTask task = new ChunkTask
                    {
                        Type = ChunkTaskType.Unload,
                        Position = chunkPos,
                        Chunk = loadedChunks[chunkPos],
                        Priority = -1f // Low priority
                    };

                    chunkTasks.Enqueue(task, task.Priority);
                }
            }
        }

        private List<Vector3Int> GetChunksToLoad()
        {
            // Calculate chunk coordinates of viewer
            Vector3Int viewerChunk = new Vector3Int(
                Mathf.FloorToInt(viewerPosition.x / ChunkSize),
                Mathf.FloorToInt(viewerPosition.y / ChunkSize),
                Mathf.FloorToInt(viewerPosition.z / ChunkSize)
            );

            // Calculate predicted position
            Vector3Int predictedChunk = new Vector3Int(
                Mathf.FloorToInt(predictedViewerPosition.x / ChunkSize),
                Mathf.FloorToInt(predictedViewerPosition.y / ChunkSize),
                Mathf.FloorToInt(predictedViewerPosition.z / ChunkSize)
            );

            // Calculate load distance in chunks
            int loadChunks = Mathf.CeilToInt(LoadDistance / ChunkSize);

            // Find all chunk positions within load distance
            List<Vector3Int> chunksToLoad = new List<Vector3Int>();

            for (int x = viewerChunk.x - loadChunks; x <= viewerChunk.x + loadChunks; x++)
            {
                for (int y = viewerChunk.y - loadChunks; y <= viewerChunk.y + loadChunks; y++)
                {
                    for (int z = viewerChunk.z - loadChunks; z <= viewerChunk.z + loadChunks; z++)
                    {
                        Vector3Int pos = new Vector3Int(x, y, z);

                        // Skip if already loaded
                        if (loadedChunks.ContainsKey(pos))
                            continue;

                        // Check if within load distance
                        Vector3 chunkWorldPos = new Vector3(x * ChunkSize, y * ChunkSize, z * ChunkSize);
                        float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

                        if (distance <= LoadDistance)
                        {
                            chunksToLoad.Add(pos);
                        }
                    }
                }
            }

            // Sort by interest and priority
            chunksToLoad.Sort((a, b) => {
                float interestA = CalculateChunkInterest(a);
                float interestB = CalculateChunkInterest(b);

                // Primary sort by interest (higher first)
                if (Mathf.Abs(interestA - interestB) > 0.01f)
                    return interestB.CompareTo(interestA);

                // Secondary sort by distance to predicted position
                Vector3 posA = new Vector3(a.x * ChunkSize, a.y * ChunkSize, a.z * ChunkSize);
                Vector3 posB = new Vector3(b.x * ChunkSize, b.y * ChunkSize, b.z * ChunkSize);

                float distA = Vector3.Distance(posA, predictedViewerPosition);
                float distB = Vector3.Distance(posB, predictedViewerPosition);

                return distA.CompareTo(distB);
            });

            // Limit to max concurrent chunks (adaptive)
            if (chunksToLoad.Count > MaxConcurrentChunks)
            {
                chunksToLoad = chunksToLoad.GetRange(0, MaxConcurrentChunks);
            }

            return chunksToLoad;
        }

        private List<Vector3Int> GetChunksToUnload()
        {
            // Find chunks that are too far from both current and predicted positions
            return loadedChunks.Keys.Where(chunkPos => {
                Vector3 chunkWorldPos = new Vector3(
                    chunkPos.x * ChunkSize,
                    chunkPos.y * ChunkSize,
                    chunkPos.z * ChunkSize
                );

                float distToCurrent = Vector3.Distance(chunkWorldPos, viewerPosition);
                float distToPredicted = Vector3.Distance(chunkWorldPos, predictedViewerPosition);

                return distToCurrent > UnloadDistance && distToPredicted > UnloadDistance;
            }).ToList();
        }

        private float CalculateLoadPriority(Vector3Int chunkPos)
        {
            // Convert to world position
            Vector3 chunkWorldPos = new Vector3(
                chunkPos.x * ChunkSize,
                chunkPos.y * ChunkSize,
                chunkPos.z * ChunkSize
            );

            // Calculate interest factor
            float interestFactor = CalculateChunkInterest(chunkPos);

            // Calculate distances to both current and predicted positions
            float currentDistance = Vector3.Distance(chunkWorldPos, viewerPosition);
            float predictedDistance = Vector3.Distance(chunkWorldPos, predictedViewerPosition);

            // Calculate view alignment with acceleration-aware prediction
            Vector3 dirToChunk = (chunkWorldPos - viewerPosition).normalized;
            Vector3 predictedDirection = (predictedViewerPosition - viewerPosition).normalized;
            float viewAlignment = Vector3.Dot(dirToChunk, predictedDirection);

            // Higher priority for chunks:
            // 1. With higher interest
            // 2. Closer to current position
            // 3. Closer to predicted position
            // 4. More aligned with predicted movement direction
            float distanceFactor = 1.0f / (currentDistance + 1.0f);
            float predictedFactor = 1.0f / (predictedDistance + 1.0f);
            float alignmentFactor = viewAlignment > 0 ? (1.0f + viewAlignment) : 0.5f;

            // Combine factors (weighted)
            float priority = interestFactor * 0.4f +
                            (distanceFactor * 0.2f + predictedFactor * 0.3f) * alignmentFactor;

            // Add a bonus if the chunk is partially collapsed (gives priority to finishing work already started)
            if (loadedChunks.TryGetValue(chunkPos, out Chunk chunk) && !chunk.IsFullyCollapsed)
            {
                priority *= 1.5f;
            }

            return priority;
        }

        private void ProcessChunkTasks()
        {
            // Process a limited number of tasks per frame
            int tasksProcessed = 0;
            int maxTasksPerFrame = 2; // Could also come from config

            while (chunkTasks.Count > 0 && tasksProcessed < maxTasksPerFrame)
            {
                ChunkTask task = chunkTasks.Dequeue();

                // Use parallel processing if enabled
                if (useParallelProcessing && parallelProcessor != null)
                {
                    switch (task.Type)
                    {
                        case ChunkTaskType.Create:
                            CreateChunk(task.Position);
                            break;

                        case ChunkTaskType.Unload:
                            UnloadChunk(task.Position);
                            break;

                        case ChunkTaskType.Collapse:
                            // Queue for parallel processing
                            if (!parallelProcessor.QueueChunkForProcessing(
                                task.Chunk,
                                WFCJobType.Collapse,
                                task.MaxIterations,
                                task.Priority))
                            {
                                // If queueing failed, add back to our queue
                                chunkTasks.Enqueue(task, task.Priority);
                            }
                            break;

                        case ChunkTaskType.GenerateMesh:
                            if (!parallelProcessor.QueueChunkForProcessing(
                           task.Chunk,
                           WFCJobType.GenerateMesh,
                           100,
                           task.Priority))
                            {
                                // If queueing failed, add back to our queue
                                chunkTasks.Enqueue(task, task.Priority);
                            }
                            break;
                    }
                }
                else
                {
                    // Original non-parallel processing
                    switch (task.Type)
                    {
                        case ChunkTaskType.Create:
                            CreateChunk(task.Position);
                            break;

                        case ChunkTaskType.Unload:
                            UnloadChunk(task.Position);
                            break;

                        case ChunkTaskType.Collapse:
                            CollapseChunk(task.Chunk, task.MaxIterations);
                            break;

                        case ChunkTaskType.GenerateMesh:
                            GenerateChunkMesh(task.Chunk);
                            break;
                    }
                }

                tasksProcessed++;
            }
        }

        private void CreateChunk(Vector3Int position)
        {
            // Create new chunk
            Chunk chunk = new Chunk(position, ChunkSize);

            // Add to loaded chunks
            loadedChunks[position] = chunk;

            // Connect to neighbors
            ConnectChunkNeighbors(chunk);

            // Initialize boundary buffers
            InitializeBoundaryBuffers(chunk);

            // Create collapse task
            ChunkTask task = new ChunkTask
            {
                Type = ChunkTaskType.Collapse,
                Chunk = chunk,
                MaxIterations = activeConfig.Algorithm.maxIterationsPerChunk, // Use config value
                Priority = chunk.Priority
            };

            chunkTasks.Enqueue(task, task.Priority);
        }

        private void UnloadChunk(Vector3Int position)
        {
            if (loadedChunks.TryGetValue(position, out Chunk chunk))
            {
                // Clean up any resources

                // Remove from loaded chunks
                loadedChunks.Remove(position);
            }
        }

        private void OnDestroy()
        {
            // Stop parallel processor
            if (parallelProcessor != null)
            {
                parallelProcessor.Stop();
                parallelProcessor = null;
            }
        }

        private void ConnectChunkNeighbors(Chunk chunk)
        {
            // Check each direction for neighbors
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                Vector3Int offset = dir.ToVector3Int();
                Vector3Int neighborPos = chunk.Position + offset;

                if (loadedChunks.TryGetValue(neighborPos, out Chunk neighbor))
                {
                    // Connect chunks both ways
                    chunk.Neighbors[dir] = neighbor;
                    neighbor.Neighbors[dir.GetOpposite()] = chunk;
                }
            }
        }

        private void InitializeBoundaryBuffers(Chunk chunk)
        {
            // TODO: Implement boundary buffer initialization
            // This should be similar to the WFCGenerator implementation
        }

        private void CollapseChunk(Chunk chunk, int maxIterations)
        {
            // TODO: Implement WFC collapse
            // This would call into the WFCGenerator to execute WFC on this chunk
        }

        private void GenerateChunkMesh(Chunk chunk)
        {
            // TODO: Implement mesh generation
            // This would connect to your Marching Cubes implementation
        }

        // Method to update configuration at runtime
        public void UpdateConfiguration(WFCConfiguration newConfig)
        {
            if (newConfig == null)
            {
                Debug.LogError("ChunkManager: Cannot update to null configuration.");
                return;
            }

            // Store old values for comparison
            float oldLoadDistance = LoadDistance;
            float oldUnloadDistance = UnloadDistance;
            int oldChunkSize = ChunkSize;

            // Update configuration
            configOverride = newConfig;
            activeConfig = newConfig;

            // Reset adaptive params to defaults from new config
            currentLoadDistance = activeConfig.Performance.loadDistance;
            maxConcurrentChunks = activeConfig.Performance.maxConcurrentChunks;

            // Handle changes that require updates
            if (oldLoadDistance != LoadDistance || oldUnloadDistance != UnloadDistance)
            {
                Debug.Log("ChunkManager: Load/unload distances updated. Refreshing chunk management.");
                // Could trigger a refresh here
            }

            if (oldChunkSize != ChunkSize)
            {
                Debug.LogWarning("ChunkManager: Chunk size changed. This will require world regeneration.");
                // This is a more significant change requiring regeneration
            }
        }

        // Helper class for chunk tasks
        private class ChunkTask
        {
            public ChunkTaskType Type { get; set; }
            public Vector3Int Position { get; set; }
            public Chunk Chunk { get; set; }
            public int MaxIterations { get; set; }
            public float Priority { get; set; }
        }

        private enum ChunkTaskType
        {
            Create,
            Unload,
            Collapse,
            GenerateMesh
        }
    }
}