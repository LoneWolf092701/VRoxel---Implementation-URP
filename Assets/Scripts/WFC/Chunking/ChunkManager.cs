using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using System.Linq;
using Utils;
using WFC.Processing;
using WFC.Generation;
using WFC.Configuration;
using System.Collections;
using Debug = UnityEngine.Debug;
using WFC.Performance;
using WFC.MarchingCubes;
using WFC.Boundary;
using System;
using WFC.Terrain;

namespace WFC.Chunking
{
    /// <summary>
    /// Manages the dynamic loading, unloading, and processing of terrain chunks
    /// for an infinite procedural world using Wave Function Collapse algorithm.
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        #region Configuration and References

        [Header("Configuration")]
        [SerializeField] private WFCConfiguration configOverride;

        [Header("References")]
        [SerializeField] public Transform viewer;
        [SerializeField] private WFCGenerator wfcGenerator;
        [SerializeField] private PerformanceMonitor performanceMonitor;
        [SerializeField] private MeshGenerator meshGenerator;
        [SerializeField] private GlobalHeightmapController heightmapController;
        private BoundaryBufferManager boundaryManager;

        [Header("Prediction Settings")]
        [SerializeField] private int movementHistorySize = 60;
        [SerializeField] private float basePredictionTime = 2.0f;

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogging = false;
        [SerializeField] private string currentTerrainType = "MountainValley";
        private ITerrainGenerator terrainGenerator;

        #endregion

        #region Private Fields

        // State management
        private Dictionary<Vector3Int, ChunkState> chunkStates = new Dictionary<Vector3Int, ChunkState>();
        private Dictionary<Vector3Int, Chunk> loadedChunks = new Dictionary<Vector3Int, Chunk>();
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
        private float predictionTime = 2.0f;
        private Queue<MovementSample> movementHistory = new Queue<MovementSample>();

        // Adaptive loading parameters
        private float currentLoadDistance;
        private int maxConcurrentChunks;
        private HierarchicalConstraintSystem hierarchicalConstraints;
        private WFCConfiguration activeConfig;
        private float lastChunkStateCleanupTime = 0f;
        private Vector3 lastChunkGenerationPosition;
        private float chunkGenerationDistance = 10f;

        // Chunk state change event
        public delegate void ChunkStateChangeHandler(Vector3Int chunkPos, ChunkLifecycleState oldState, ChunkLifecycleState newState);
        public event ChunkStateChangeHandler OnChunkStateChanged;

        #endregion

        #region Properties

        private int ChunkSize => activeConfig.World.chunkSize;
        private float LoadDistance => currentLoadDistance;
        private float UnloadDistance => activeConfig.Performance.unloadDistance;
        private int MaxConcurrentChunks => maxConcurrentChunks;

        public Vector3Int WorldSize => new Vector3Int(
            activeConfig.World.worldSizeX,
            activeConfig.World.worldSizeY,
            activeConfig.World.worldSizeZ
        );

        #endregion

        #region Initialization

        private void Start()
        {
            // Get configuration
            activeConfig = configOverride != null ? configOverride : WFCConfigManager.Config;

            if (activeConfig == null)
            {
                Debug.LogError("ChunkManager: No configuration available. Please assign a WFCConfiguration asset.");
                enabled = false;
                return;
            }

            if (!activeConfig.Validate())
                Debug.LogWarning("ChunkManager: Using configuration with validation issues.");

            InitializeReferences();

            // Initialize adaptive parameters
            currentLoadDistance = activeConfig.Performance.loadDistance;
            maxConcurrentChunks = activeConfig.Performance.maxConcurrentChunks;

            // Start coroutines
            StartCoroutine(AdaptiveStrategyUpdateCoroutine());
            StartCoroutine(RobustDelayedChunkGeneration());

            // Create initial chunk at viewer position
            Vector3Int viewerChunk = new Vector3Int(
                Mathf.FloorToInt(viewerPosition.x / ChunkSize),
                Mathf.FloorToInt(viewerPosition.y / ChunkSize),
                Mathf.FloorToInt(viewerPosition.z / ChunkSize)
            );
            lastChunkGenerationPosition = Vector3.zero;
            CreateChunkAt(viewerChunk);

            // Initialize boundary manager
            boundaryManager = new BoundaryBufferManager((IWFCAlgorithm)wfcGenerator);
        }

        private IEnumerator RobustDelayedChunkGeneration()
        {
            // Wait for frames to ensure complete initialization
            for (int i = 0; i < 3; i++)
                yield return null;

            // Force update viewer position
            if (viewer != null)
            {
                viewerPosition = viewer.position;
                lastViewerPosition = viewer.position;

                if (viewerPosition.magnitude < 0.001f)
                {
                    yield return new WaitForSeconds(0.5f);
                    viewerPosition = viewer.position;
                    lastViewerPosition = viewer.position;
                }
            }
            else
            {
                Debug.LogError("No viewer reference found before generating chunks!");
                yield break;
            }

            // Clear existing chunks except those near player
            foreach (var chunkPos in loadedChunks.Keys.ToList())
            {
                if (Vector3.Distance(
                    new Vector3(chunkPos.x * ChunkSize, chunkPos.y * ChunkSize, chunkPos.z * ChunkSize),
                    viewerPosition) < ChunkSize * 5)
                    continue;

                UnloadChunk(chunkPos);
            }

            // Generate chunks around player
            CreateChunksAroundPlayer();
        }

        private void InitializeReferences()
        {
            if (viewer == null)
                viewer = Camera.main?.transform;

            if (viewer == null)
            {
                Debug.LogError("ChunkManager: No viewer reference found. Please assign a viewer transform.");
                enabled = false;
                return;
            }

            viewerPosition = viewer.position;
            lastViewerPosition = viewerPosition;

            // Find required components if not assigned
            if (wfcGenerator == null)
                wfcGenerator = FindAnyObjectByType<WFCGenerator>();

            if (meshGenerator == null)
                meshGenerator = FindAnyObjectByType<MeshGenerator>();

            if (performanceMonitor == null)
                performanceMonitor = FindAnyObjectByType<PerformanceMonitor>();

            // Get hierarchical constraint system
            if (wfcGenerator != null)
                hierarchicalConstraints = wfcGenerator.GetHierarchicalConstraintSystem();
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            UpdateViewerData();

            // Connect to parallel manager if available
            if (parallelProcessor == null)
            {
                ParallelWFCManager parallelManager = FindObjectOfType<ParallelWFCManager>();
                if (parallelManager != null)
                    parallelProcessor = parallelManager.GetParallelProcessor();
            }

            // Check if player has moved far enough to generate new chunks
            if (Vector3.Distance(viewerPosition, lastChunkGenerationPosition) > chunkGenerationDistance)
            {
                CreateChunksAroundPlayer();
                lastChunkGenerationPosition = viewerPosition;
            }

            // Calculate predicted position using advanced prediction
            predictedViewerPosition = PredictFuturePosition(predictionTime);

            // Update chunk management
            UpdateChunkPriorities();
            AssignLODLevels();
            ManageChunks();
            ProcessChunkTasks();
            ProcessDirtyChunks();
            OptimizeInactiveChunks();
            CleanupStaleChunkStates();

            // Player input for manual chunk generation
            if (Input.GetKeyDown(KeyCode.P))
                CreateChunksAroundPlayer();
        }

        private void ProcessDirtyChunks()
        {
            foreach (var chunkEntry in loadedChunks)
            {
                Vector3Int chunkPos = chunkEntry.Key;
                Chunk chunk = chunkEntry.Value;

                // Check if chunk needs mesh generation
                if (chunk.IsDirty &&
                    (GetChunkState(chunkPos) == ChunkLifecycleState.Active ||
                     GetChunkState(chunkPos) == ChunkLifecycleState.Collapsing))
                {
                    // Queue mesh generation task
                    ChunkTask meshTask = new ChunkTask
                    {
                        Type = ChunkTaskType.GenerateMesh,
                        Chunk = chunk,
                        Position = chunkPos,
                        Priority = chunk.Priority * 0.9f
                    };

                    chunkTasks.Enqueue(meshTask, meshTask.Priority);
                    chunk.IsDirty = false;
                }
            }
        }

        /// <summary>
        /// Gets a read-only dictionary of all loaded chunks
        /// </summary>
        public IReadOnlyDictionary<Vector3Int, Chunk> GetLoadedChunks()
        {
            return loadedChunks;
        }

        private void UpdateViewerData()
        {
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
        }

        #endregion

        #region Chunk State Management

        /// <summary>
        /// Represents the possible states in a chunk's lifecycle
        /// </summary>
        public enum ChunkLifecycleState
        {
            None,           // No state (not yet tracked)
            Pending,        // Creation task has been queued but not processed
            Loading,        // Currently being created/initialized
            Active,         // Fully loaded and ready
            Collapsing,     // WFC algorithm is running on this chunk
            GeneratingMesh, // Mesh is being generated
            Unloading,      // Being removed from active chunks
            Error           // An error occurred during processing
        }

        /// <summary>
        /// Holds state and timing information for a chunk
        /// </summary>
        private class ChunkState
        {
            public ChunkLifecycleState State { get; set; } = ChunkLifecycleState.None;
            public float LastStateChangeTime { get; set; }
            public float CreationTime { get; set; }
            public int ProcessingAttempts { get; set; } = 0;
            public string LastError { get; set; }

            public ChunkState()
            {
                LastStateChangeTime = Time.time;
                CreationTime = Time.time;
            }

            public void TransitionTo(ChunkLifecycleState newState)
            {
                State = newState;
                LastStateChangeTime = Time.time;
            }

            public float GetTimeInCurrentState()
            {
                return Time.time - LastStateChangeTime;
            }
        }

        /// <summary>
        /// Updates the state of a chunk and triggers any necessary events
        /// </summary>
        private void UpdateChunkState(Vector3Int chunkPos, ChunkLifecycleState newState)
        {
            ChunkLifecycleState oldState = ChunkLifecycleState.None;

            // Get or create the chunk state
            if (!chunkStates.TryGetValue(chunkPos, out ChunkState state))
            {
                state = new ChunkState();
                chunkStates[chunkPos] = state;
            }
            else
            {
                oldState = state.State;
            }

            // Update the state
            state.TransitionTo(newState);

            // Trigger events
            OnChunkStateChanged?.Invoke(chunkPos, oldState, newState);
        }

        /// <summary>
        /// Gets the current state of a chunk
        /// </summary>
        public ChunkLifecycleState GetChunkState(Vector3Int chunkPos)
        {
            if (chunkStates.TryGetValue(chunkPos, out ChunkState state))
                return state.State;

            return ChunkLifecycleState.None;
        }

        /// <summary>
        /// Cleanup stale chunk states to prevent memory leaks
        /// </summary>
        private void CleanupStaleChunkStates()
        {
            // Only run periodically
            if (Time.time - lastChunkStateCleanupTime < 30f)
                return;

            lastChunkStateCleanupTime = Time.time;
            List<Vector3Int> stateKeysToRemove = new List<Vector3Int>();

            foreach (var entry in chunkStates)
            {
                Vector3Int chunkPos = entry.Key;
                ChunkState state = entry.Value;

                // Remove states for chunks that have been unloaded for more than 60 seconds
                if (state.State == ChunkLifecycleState.Unloading &&
                    state.GetTimeInCurrentState() > 60f)
                {
                    stateKeysToRemove.Add(chunkPos);
                }
                // Reset chunks that have been stuck in Pending or Loading state for too long
                else if ((state.State == ChunkLifecycleState.Pending ||
                     state.State == ChunkLifecycleState.Loading) &&
                     state.GetTimeInCurrentState() > 30f)
                {
                    if (state.ProcessingAttempts < 3)
                    {
                        // Retry up to 3 times
                        state.ProcessingAttempts++;
                        state.TransitionTo(ChunkLifecycleState.None);
                    }
                    else
                    {
                        // After 3 attempts, mark as error
                        state.TransitionTo(ChunkLifecycleState.Error);
                        state.LastError = "Exceeded maximum processing attempts";
                    }
                }
                // Clean up error states after a while
                else if (state.State == ChunkLifecycleState.Error &&
                    state.GetTimeInCurrentState() > 120f)
                {
                    stateKeysToRemove.Add(chunkPos);
                }
            }

            // Remove the states
            foreach (var key in stateKeysToRemove)
                chunkStates.Remove(key);
        }

        #endregion

        #region Movement Prediction

        /// <summary>
        /// Predicts future viewer position based on movement history
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

            // Calculate acceleration trends
            Vector3 avgAcceleration = CalculateAverageAcceleration(movementHistory);

            // Detect movement consistency and adjust prediction time
            float directionConsistency = CalculateDirectionConsistency(movementHistory);
            float adjustedPredictionTime = predictionTimeSeconds * Mathf.Lerp(0.5f, 1.5f, directionConsistency);

            // Enhanced prediction with jerk (rate of change of acceleration)
            Vector3 jerk = CalculateJerk(movementHistory);

            // Apply complete prediction formula with jerk
            return viewerPosition +
                   viewerVelocity * adjustedPredictionTime +
                   0.5f * avgAcceleration * adjustedPredictionTime * adjustedPredictionTime +
                   (1f / 6f) * jerk * Mathf.Pow(adjustedPredictionTime, 3);
        }

        // Calculate direction consistency based on movement history
        private float CalculateDirectionConsistency(Queue<MovementSample> history)
        {
            if (history.Count < 3) return 0.8f;

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
            return Mathf.Clamp01(avgDirection.magnitude);
        }

        // Calculate average acceleration from movement history
        private Vector3 CalculateAverageAcceleration(Queue<MovementSample> history)
        {
            if (history.Count < 3) return viewerAcceleration;

            Vector3 avgAcceleration = Vector3.zero;
            int count = 0;
            MovementSample[] samples = history.ToArray();

            for (int i = 2; i < samples.Length; i++)
            {
                float timeDelta = samples[i].timestamp - samples[i - 1].timestamp;
                if (timeDelta > 0.001f)
                {
                    Vector3 accel = (samples[i].velocity - samples[i - 1].velocity) / timeDelta;
                    avgAcceleration += accel;
                    count++;
                }
            }

            return count > 0 ? avgAcceleration / count : viewerAcceleration;
        }

        // Calculate jerk (rate of change of acceleration) from movement history
        private Vector3 CalculateJerk(Queue<MovementSample> history)
        {
            if (history.Count < 4) return Vector3.zero;

            Vector3 avgJerk = Vector3.zero;
            int count = 0;
            MovementSample[] samples = history.ToArray();

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

            return count > 0 ? avgJerk / count : Vector3.zero;
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

        #region Chunk Interest and Prioritization

        /// <summary>
        /// Calculates how interesting a chunk is for prioritized loading
        /// </summary>
        private float CalculateChunkInterest(Vector3Int chunkPos)
        {
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
            if (loadedChunks.TryGetValue(chunkPos, out Chunk chunk) && !chunk.IsFullyCollapsed)
            {
                float collapsedPercentage = CalculateCollapsedPercentage(chunk);
                contentInterest = 1f - Mathf.Abs(collapsedPercentage - 0.5f) * 2f;
            }

            // 4. Feature-based interest (chunks with special features = more interesting)
            float featureInterest = 0f;
            if (hierarchicalConstraints != null)
            {
                foreach (var constraint in hierarchicalConstraints.GetGlobalConstraints())
                {
                    float influence = constraint.GetInfluenceAt(chunkWorldPos + new Vector3(ChunkSize / 2, ChunkSize / 2, ChunkSize / 2));
                    featureInterest = Mathf.Max(featureInterest, influence);
                }
            }

            // Combine interest factors with weights
            return distanceInterest * 0.4f +
                   viewInterest * 0.3f +
                   contentInterest * 0.2f +
                   featureInterest * 0.1f;
        }

        private float GetViewAlignment(Vector3Int chunkPos)
        {
            Vector3 chunkCenter = GetChunkWorldPosition(chunkPos) +
                                  new Vector3(ChunkSize / 2, ChunkSize / 2, ChunkSize / 2);

            Vector3 dirToChunk = (chunkCenter - viewerPosition).normalized;
            Vector3 viewDirection = viewer.forward.normalized;

            return Vector3.Dot(dirToChunk, viewDirection);
        }

        private float CalculateCollapsedPercentage(Chunk chunk)
        {
            if (chunk == null) return 0f;

            int sampleSize = Mathf.Min(27, chunk.Size * chunk.Size * chunk.Size);
            int samplesPerDimension = Mathf.CeilToInt(Mathf.Pow(sampleSize, 1f / 3f));
            float step = chunk.Size / (float)samplesPerDimension;
            int collapsedCells = 0;

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
                            collapsedCells++;
                    }
                }
            }

            return (float)collapsedCells / sampleSize;
        }

        #endregion

        #region Adaptive Chunk Generation Strategy

        // Adaptive strategy update coroutine
        private IEnumerator AdaptiveStrategyUpdateCoroutine()
        {
            while (true)
            {
                AdaptChunkGenerationStrategy();
                yield return new WaitForSeconds(0.5f);
            }
        }

        // Adaptive chunk generation strategy
        private void AdaptChunkGenerationStrategy()
        {
            // 1. Adjust prediction time based on viewer speed
            float viewerSpeed = viewerVelocity.magnitude;
            predictionTime = Mathf.Lerp(basePredictionTime, basePredictionTime * 2.5f, Mathf.Clamp01(viewerSpeed / 20f));

            // 2. Adjust generation radius based on performance
            float fps = 1f / Time.smoothDeltaTime;
            float targetFps = 100f;

            if (fps > targetFps * 1.2f)
            {
                currentLoadDistance = Mathf.Min(
                    currentLoadDistance + 10f * Time.deltaTime,
                    activeConfig.Performance.loadDistance * 1.5f);
            }
            else if (fps < targetFps * 0.8f)
            {
                currentLoadDistance = Mathf.Max(
                    currentLoadDistance - 20f * Time.deltaTime,
                    activeConfig.Performance.loadDistance * 0.6f);
            }

            // 3. Adjust max concurrent chunks based on performance
            float systemLoadFactor = fps < 30 ? 0.6f : fps < 45 ? 0.8f : fps > 100 ? 1.2f : 1.0f;
            maxConcurrentChunks = Mathf.Clamp(
                Mathf.FloorToInt(activeConfig.Performance.maxConcurrentChunks * systemLoadFactor),
                4, 32);
        }

        private void OptimizeInactiveChunks()
        {
            foreach (var chunkEntry in loadedChunks)
            {
                Vector3Int chunkPos = chunkEntry.Key;
                Chunk chunk = chunkEntry.Value;

                // Calculate distance from viewer
                float distance = Vector3.Distance(GetChunkWorldPosition(chunkPos), viewerPosition);

                // Far chunks (beyond unload distance but still kept)
                if (distance > UnloadDistance * 0.8f)
                {
                    bool isProcessing = parallelProcessor != null && parallelProcessor.IsChunkBeingProcessed(chunkPos);

                    if (!isProcessing && !chunk.IsMemoryOptimized)
                    {
                        chunk.OptimizeMemory();
                        chunk.IsMemoryOptimized = true;
                    }
                }
                else if (chunk.IsMemoryOptimized && distance < UnloadDistance * 0.7f)
                {
                    // Restore full chunk data when coming back into range
                    chunk.RestoreFromOptimized();
                    chunk.IsMemoryOptimized = false;
                }
            }
        }

        #endregion

        #region LOD Management

        // Assign LOD levels based on distance from viewer
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
                        lodLevel = i + 1;
                    else
                        break;
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

        // Apply LOD settings based on the current LOD level
        private void ApplyLODSettings(Chunk chunk)
        {
            // Get LOD-specific settings
            int lodLevel = chunk.LODLevel;
            int maxIterations;
            float constraintInfluence;

            // Get max iterations for this LOD level
            if (lodLevel < activeConfig.Performance.lodSettings.maxIterationsPerLOD.Length)
                maxIterations = activeConfig.Performance.lodSettings.maxIterationsPerLOD[lodLevel];
            else if (activeConfig.Performance.lodSettings.maxIterationsPerLOD.Length > 0)
                maxIterations = activeConfig.Performance.lodSettings.maxIterationsPerLOD[
                    activeConfig.Performance.lodSettings.maxIterationsPerLOD.Length - 1];
            else
                maxIterations = activeConfig.Algorithm.maxIterationsPerChunk;

            // Get constraint influence for this LOD level
            if (lodLevel < activeConfig.Performance.lodSettings.constraintInfluencePerLOD.Length)
                constraintInfluence = activeConfig.Performance.lodSettings.constraintInfluencePerLOD[lodLevel];
            else if (activeConfig.Performance.lodSettings.constraintInfluencePerLOD.Length > 0)
                constraintInfluence = activeConfig.Performance.lodSettings.constraintInfluencePerLOD[
                    activeConfig.Performance.lodSettings.constraintInfluencePerLOD.Length - 1];
            else
                constraintInfluence = 1.0f;

            // Apply the settings to the chunk
            chunk.MaxIterations = maxIterations;
            chunk.ConstraintInfluence = constraintInfluence;

            // Mark for mesh update if needed
            if (chunk.IsFullyCollapsed && chunk.IsDirty == false)
                chunk.IsDirty = true;
        }

        #endregion

        #region Chunk Priority Management

        private void UpdateChunkPriorities()
        {
            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("ChunkPriorities");

            foreach (var chunk in loadedChunks.Values)
                chunk.Priority = CalculateChunkPriority(chunk);

            if (performanceMonitor != null)
                performanceMonitor.EndComponentTiming("ChunkPriorities");
        }

        private float CalculateChunkPriority(Chunk chunk)
        {
            // Convert chunk position to world position
            Vector3 chunkWorldPos = GetChunkWorldPosition(chunk.Position);

            // Distance from viewer
            float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

            // View alignment with predicted movement
            Vector3 dirToChunk = (chunkWorldPos - viewerPosition).normalized;
            Vector3 predictedDirection = (predictedViewerPosition - viewerPosition).normalized;
            float viewAlignment = Vector3.Dot(dirToChunk, predictedDirection);

            // Interest-based factor
            float interestFactor = CalculateChunkInterest(chunk.Position);

            // Calculate priority factors
            float distanceFactor = 1.0f / (distance + 1.0f);
            float alignmentFactor = viewAlignment > 0 ? (1.0f + viewAlignment) : 0.5f;

            // Combine factors with weights
            float priority = distanceFactor * 0.4f +
                             alignmentFactor * 0.2f +
                             interestFactor * 0.4f;

            // Bonus for chunks that are partially collapsed
            if (!chunk.IsFullyCollapsed)
                priority *= 1.2f;

            return priority;
        }

        #endregion

        #region Chunk Management

        private void ManageChunks()
        {
            // Get chunks to load
            List<Vector3Int> chunksToLoad = GetChunksToLoad();

            foreach (var chunkPos in chunksToLoad)
            {
                ChunkLifecycleState currentState = GetChunkState(chunkPos);

                // Only create chunks that are not already being processed
                if (currentState == ChunkLifecycleState.None || currentState == ChunkLifecycleState.Error)
                {
                    // Create chunk load task
                    ChunkTask task = new ChunkTask
                    {
                        Type = ChunkTaskType.Create,
                        Position = chunkPos,
                        Priority = CalculateLoadPriority(chunkPos)
                    };

                    // Enqueue the task
                    chunkTasks.Enqueue(task, task.Priority);
                    UpdateChunkState(chunkPos, ChunkLifecycleState.Pending);
                }
            }

            // Get chunks to unload
            List<Vector3Int> chunksToUnload = GetChunksToUnload();

            foreach (var chunkPos in chunksToUnload)
            {
                if (loadedChunks.ContainsKey(chunkPos))
                {
                    ChunkLifecycleState currentState = GetChunkState(chunkPos);

                    // Only unload chunks that are active (not being processed)
                    if (currentState == ChunkLifecycleState.Active)
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
                        UpdateChunkState(chunkPos, ChunkLifecycleState.Unloading);
                    }
                }
            }
        }

        // Get chunks to load based on viewer position and distance
        private List<Vector3Int> GetChunksToLoad()
        {
            // Calculate chunk coordinates of viewer
            Vector3Int viewerChunk = new Vector3Int(
                Mathf.FloorToInt(viewerPosition.x / ChunkSize),
                Mathf.FloorToInt(viewerPosition.y / ChunkSize),
                Mathf.FloorToInt(viewerPosition.z / ChunkSize)
            );

            List<Vector3Int> chunksToLoad = new List<Vector3Int>();
            int initialRadius = 2; // Start with a 5x5 grid around player

            for (int x = viewerChunk.x - initialRadius; x <= viewerChunk.x + initialRadius; x++)
            {
                for (int z = viewerChunk.z - initialRadius; z <= viewerChunk.z + initialRadius; z++)
                {
                    Vector3Int pos = new Vector3Int(x, 0, z);

                    // Skip if already loaded
                    if (loadedChunks.ContainsKey(pos))
                        continue;

                    // Prioritize chunks close to the player
                    float distance = Vector3.Distance(
                        new Vector3(pos.x * ChunkSize, 0, pos.z * ChunkSize),
                        new Vector3(viewerPosition.x, 0, viewerPosition.z)
                    );

                    if (distance <= LoadDistance)
                        chunksToLoad.Add(pos);
                }
            }

            // Limit concurrent chunk generation
            if (chunksToLoad.Count > 8)
            {
                // Sort by distance and take only closest 8
                chunksToLoad.Sort((a, b) => {
                    float distA = Vector3.Distance(new Vector3(a.x * ChunkSize, 0, a.z * ChunkSize), viewerPosition);
                    float distB = Vector3.Distance(new Vector3(b.x * ChunkSize, 0, b.z * ChunkSize), viewerPosition);
                    return distA.CompareTo(distB);
                });

                chunksToLoad = chunksToLoad.GetRange(0, 8);
            }

            return chunksToLoad;
        }

        private List<Vector3Int> GetChunksToUnload()
        {
            // Find chunks that are too far from current position
            return loadedChunks.Keys.Where(chunkPos => {
                Vector3 chunkWorldPos = GetChunkWorldPosition(chunkPos);
                float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

                // Check if chunk Y position is non-zero (above ground)
                bool isAboveGround = chunkPos.y > 0;

                // Always unload chunks above ground or beyond distance threshold
                return isAboveGround || distance > UnloadDistance;
            }).ToList();
        }

        // Calculate load priority for a chunk based on multiple factors
        private float CalculateLoadPriority(Vector3Int chunkPos)
        {
            // Convert to world position
            Vector3 chunkWorldPos = GetChunkWorldPosition(chunkPos);

            // Calculate interest factor
            float interestFactor = CalculateChunkInterest(chunkPos);

            // Calculate distances and alignment
            float currentDistance = Vector3.Distance(chunkWorldPos, viewerPosition);
            float predictedDistance = Vector3.Distance(chunkWorldPos, predictedViewerPosition);

            Vector3 dirToChunk = (chunkWorldPos - viewerPosition).normalized;
            Vector3 predictedDirection = (predictedViewerPosition - viewerPosition).normalized;
            float viewAlignment = Vector3.Dot(dirToChunk, predictedDirection);

            // Calculate combined factors
            float distanceFactor = 1.0f / (currentDistance + 1.0f);
            float predictedFactor = 1.0f / (predictedDistance + 1.0f);
            float alignmentFactor = viewAlignment > 0 ? (1.0f + viewAlignment) : 0.5f;

            // Combine factors (weighted)
            return interestFactor * 0.4f + (distanceFactor * 0.2f + predictedFactor * 0.3f) * alignmentFactor;
        }

        #endregion

        #region Task Processing

        private void ProcessChunkTasks()
        {
            // Find or connect to parallel processor if available
            if (useParallelProcessing && parallelProcessor == null)
            {
                ParallelWFCManager parallelManager = FindObjectOfType<ParallelWFCManager>();
                if (parallelManager != null)
                    parallelProcessor = parallelManager.GetParallelProcessor();
            }

            // Process a limited number of tasks per frame
            int tasksProcessed = 0;
            int maxTasksPerFrame = 2;

            while (chunkTasks.Count > 0 && tasksProcessed < maxTasksPerFrame)
            {
                ChunkTask task = chunkTasks.Dequeue();

                try
                {
                    // Try parallel processing for collapse tasks
                    if (useParallelProcessing && parallelProcessor != null && task.Type == ChunkTaskType.Collapse)
                    {
                        bool queued = parallelProcessor.QueueChunkForProcessing(
                            task.Chunk,
                            WFCJobType.Collapse,
                            task.MaxIterations,
                            task.Priority);

                        if (queued)
                        {
                            UpdateChunkState(task.Chunk.Position, ChunkLifecycleState.Collapsing);
                            tasksProcessed++;
                            continue;
                        }
                        else
                        {
                            // Requeue with lower priority if couldn't be processed
                            task.Priority *= 0.9f;
                            chunkTasks.Enqueue(task, task.Priority);
                            continue;
                        }
                    }

                    // Handle tasks directly
                    switch (task.Type)
                    {
                        case ChunkTaskType.Create:
                            CreateChunk(task.Position);
                            break;

                        case ChunkTaskType.Unload:
                            UnloadChunk(task.Position);
                            break;

                        case ChunkTaskType.Collapse:
                            UpdateChunkState(task.Chunk.Position, ChunkLifecycleState.Collapsing);
                            CollapseChunk(task.Chunk, task.MaxIterations);
                            break;

                        case ChunkTaskType.GenerateMesh:
                            UpdateChunkState(task.Chunk.Position, ChunkLifecycleState.GeneratingMesh);
                            GenerateChunkMesh(task.Chunk);
                            break;
                    }

                    tasksProcessed++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in {task.Type} task at {task.Position}: {e.Message}");
                    if (task.Type == ChunkTaskType.Create)
                    {
                        if (chunkStates.TryGetValue(task.Position, out ChunkState state))
                            state.LastError = e.Message;

                        UpdateChunkState(task.Position, ChunkLifecycleState.Error);
                    }
                    tasksProcessed++;
                }
            }
        }

        #endregion

        #region Chunk Creation and Operations

        // Method to create a chunk
        public void CreateChunk(Vector3Int position)
        {
            // Update chunk state to loading
            UpdateChunkState(position, ChunkLifecycleState.Loading);

            // Check if chunk already exists
            if (loadedChunks.ContainsKey(position))
            {
                UpdateChunkState(position, ChunkLifecycleState.Active);
                return;
            }

            try
            {
                // Create new chunk with the configured size
                Chunk chunk = new Chunk(position, ChunkSize);

                // Add to loaded chunks dictionary
                loadedChunks[position] = chunk;

                // Connect to existing neighbors
                ConnectChunkNeighbors(chunk);

                // Initialize boundary buffers
                InitializeBoundaryBuffers(chunk);
                boundaryManager.SynchronizeAllBuffers();

                // Get WFC generator if needed
                if (wfcGenerator == null)
                    wfcGenerator = FindObjectOfType<WFCGenerator>();

                // Initialize cells with possible states
                if (wfcGenerator != null)
                {
                    // Access the WFCGenerator to get possible states
                    var possibleStates = Enumerable.Range(0, WFCConfigManager.Config.World.maxStates);
                    chunk.InitializeCells(possibleStates);

                    // Apply initial constraints based on chunk position
                    int chunkSeed = GenerateChunkSeed(position);
                    System.Random random = new System.Random(chunkSeed);
                    ApplyInitialConstraints(chunk, random);
                }
                else
                {
                    // Default initialization with all possible states
                    var allStates = Enumerable.Range(0, activeConfig.World.maxStates);
                    chunk.InitializeCells(allStates);
                }

                // Set dirty flag to ensure mesh gets generated
                chunk.IsDirty = true;

                // Create a task to collapse the chunk
                ChunkTask task = new ChunkTask
                {
                    Type = ChunkTaskType.Collapse,
                    Chunk = chunk,
                    MaxIterations = activeConfig.Algorithm.maxIterationsPerChunk,
                    Priority = chunk.Priority
                };

                chunkTasks.Enqueue(task, task.Priority);

                // Update chunk state
                UpdateChunkState(position, ChunkLifecycleState.Active);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error creating chunk at {position}: {e.Message}");

                // Update chunk state to error
                if (chunkStates.TryGetValue(position, out ChunkState state))
                    state.LastError = e.Message;

                UpdateChunkState(position, ChunkLifecycleState.Error);
                throw;
            }
        }

        // Method to unload a chunk from the world
        private void UnloadChunk(Vector3Int position)
        {
            // Update chunk state
            UpdateChunkState(position, ChunkLifecycleState.Unloading);

            try
            {
                if (loadedChunks.TryGetValue(position, out Chunk chunk))
                {
                    // Disconnect from neighbors
                    foreach (var neighborEntry in chunk.Neighbors.ToList())
                    {
                        Direction direction = neighborEntry.Key;
                        Chunk neighbor = neighborEntry.Value;

                        // Find and remove reverse connections
                        Direction oppositeDir = direction.GetOpposite();
                        if (neighbor != null && neighbor.Neighbors.ContainsKey(oppositeDir))
                            neighbor.Neighbors.Remove(oppositeDir);
                    }

                    // Clear neighbors and boundary buffers
                    chunk.Neighbors.Clear();
                    chunk.BoundaryBuffers.Clear();

                    // Remove any associated mesh or game objects
                    RemoveChunkMeshObject(position);

                    // Remove from loaded chunks dictionary
                    loadedChunks.Remove(position);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error unloading chunk at {position}: {e.Message}");
                throw;
            }
        }

        // Method to remove the mesh object associated with a chunk
        private void RemoveChunkMeshObject(Vector3Int position)
        {
            // Find mesh objects with matching position
            string chunkName = $"Terrain_Chunk_{position.x}_{position.y}_{position.z}";
            GameObject meshObject = GameObject.Find(chunkName);

            if (meshObject != null)
            {
                if (Application.isPlaying)
                    Destroy(meshObject);
                else
                    DestroyImmediate(meshObject);
            }
        }

        // Method to collapse a chunk
        private void CollapseChunk(Chunk chunk, int maxIterations)
        {
            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("ChunkCollapse");

            // Update chunk state
            UpdateChunkState(chunk.Position, ChunkLifecycleState.Collapsing);

            try
            {
                // Try parallel processing first
                if (parallelProcessor != null)
                {
                    bool queued = parallelProcessor.QueueChunkForProcessing(
                        chunk,
                        WFCJobType.Collapse,
                        maxIterations,
                        chunk.Priority);

                    if (queued)
                    {
                        if (performanceMonitor != null)
                            performanceMonitor.EndComponentTiming("ChunkCollapse");
                        return;
                    }
                }

                // Fallback to direct processing
                bool madeProgress = true;
                int iterations = 0;

                // Main WFC algorithm loop
                while (madeProgress && iterations < maxIterations && !chunk.IsFullyCollapsed)
                {
                    madeProgress = CollapseNextCell(chunk);
                    iterations++;
                }

                // Mark as fully collapsed if no more progress
                if (!madeProgress || iterations >= maxIterations)
                    chunk.IsFullyCollapsed = true;

                // Queue mesh generation task after collapse
                ChunkTask meshTask = new ChunkTask
                {
                    Type = ChunkTaskType.GenerateMesh,
                    Chunk = chunk,
                    Priority = chunk.Priority * 0.9f
                };

                chunkTasks.Enqueue(meshTask, meshTask.Priority);

                // Update chunk state
                UpdateChunkState(chunk.Position, ChunkLifecycleState.Active);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error collapsing chunk at {chunk.Position}: {e.Message}");

                // Update chunk state to error
                if (chunkStates.TryGetValue(chunk.Position, out ChunkState state))
                    state.LastError = e.Message;

                UpdateChunkState(chunk.Position, ChunkLifecycleState.Error);
                throw;
            }
            finally
            {
                if (performanceMonitor != null)
                    performanceMonitor.EndComponentTiming("ChunkCollapse");
            }
        }

        // Method to collapse the next cell in the chunk
        private bool CollapseNextCell(Chunk chunk)
        {
            // Find the cell with lowest entropy
            Cell cellToCollapse = null;
            int lowestEntropy = int.MaxValue;
            float closestDistance = float.MaxValue;

            // Apply hierarchical constraints if available
            if (hierarchicalConstraints != null)
                hierarchicalConstraints.PrecomputeChunkConstraints(chunk, WFCConfigManager.Config.World.maxStates);

            // Find cell with lowest effective entropy
            FindCellWithLowestEntropy(chunk, ref cellToCollapse, ref lowestEntropy, ref closestDistance);

            // If found a cell, collapse it
            if (cellToCollapse != null)
                return CollapseCell(cellToCollapse, chunk);

            return false;
        }

        private void FindCellWithLowestEntropy(Chunk chunk, ref Cell cellToCollapse, ref int lowestEntropy, ref float closestDistance)
        {
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);

                        // Skip already collapsed cells or cells with only one state
                        if (cell.CollapsedState.HasValue || cell.PossibleStates.Count <= 1)
                            continue;

                        // Apply constraints to calculate effective entropy
                        int effectiveEntropy = CalculateEffectiveEntropy(cell, chunk);

                        // First priority: Lowest entropy
                        if (effectiveEntropy < lowestEntropy)
                        {
                            lowestEntropy = effectiveEntropy;
                            cellToCollapse = cell;

                            // Recalculate distance for the new candidate
                            Vector3 cellWorldPos = CalculateCellWorldPosition(cell, chunk);
                            closestDistance = Vector3.Distance(cellWorldPos, viewerPosition);
                        }
                        // Tie-breaking: Same entropy but closer to viewer
                        else if (effectiveEntropy == lowestEntropy)
                        {
                            Vector3 cellWorldPos = CalculateCellWorldPosition(cell, chunk);
                            float distanceToViewer = Vector3.Distance(cellWorldPos, viewerPosition);

                            if (distanceToViewer < closestDistance)
                            {
                                cellToCollapse = cell;
                                closestDistance = distanceToViewer;
                            }
                        }
                    }
                }
            }
        }

        private Vector3 CalculateCellWorldPosition(Cell cell, Chunk chunk)
        {
            return new Vector3(
                chunk.Position.x * chunk.Size + cell.Position.x,
                chunk.Position.y * chunk.Size + cell.Position.y,
                chunk.Position.z * chunk.Size + cell.Position.z
            );
        }

        private int CalculateEffectiveEntropy(Cell cell, Chunk chunk)
        {
            int effectiveEntropy = cell.Entropy;

            if (hierarchicalConstraints != null)
            {
                Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(
                    cell, chunk, WFCConfigManager.Config.World.maxStates);

                if (biases.Count > 0)
                {
                    float maxBias = biases.Values.Max(Mathf.Abs);

                    // Strong bias reduces effective entropy
                    if (maxBias > 0.7f)
                        effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.4f);
                    else if (maxBias > 0.4f)
                        effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.6f);
                    else if (maxBias > 0.2f)
                        effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.8f);
                }
            }

            return effectiveEntropy;
        }

        private bool CollapseCell(Cell cell, Chunk chunk)
        {
            int[] possibleStates = cell.PossibleStates.ToArray();
            int chosenState;

            if (hierarchicalConstraints != null && possibleStates.Length > 1)
            {
                // Get biases from constraints
                Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(
                    cell, chunk, WFCConfigManager.Config.World.maxStates);

                // Create weighted selection
                float[] weights = new float[possibleStates.Length];
                float totalWeight = 0;

                for (int i = 0; i < possibleStates.Length; i++)
                {
                    int state = possibleStates[i];
                    weights[i] = 1.0f; // Base weight

                    if (biases.TryGetValue(state, out float bias))
                    {
                        float multiplier = Mathf.Pow(2, bias * 2);
                        weights[i] *= multiplier;
                    }

                    weights[i] = Mathf.Max(0.1f, weights[i]);
                    totalWeight += weights[i];
                }

                // Random selection based on weights
                float randomValue = UnityEngine.Random.Range(0, totalWeight);
                float cumulativeWeight = 0;
                chosenState = possibleStates[0];

                for (int i = 0; i < possibleStates.Length; i++)
                {
                    cumulativeWeight += weights[i];
                    if (randomValue <= cumulativeWeight)
                    {
                        chosenState = possibleStates[i];
                        break;
                    }
                }
            }
            else
            {
                // No significant biases, use standard random selection
                chosenState = possibleStates[UnityEngine.Random.Range(0, possibleStates.Length)];
            }

            // Collapse to chosen state
            cell.Collapse(chosenState);

            // Create propagation event
            PropagationEvent evt = new PropagationEvent(
                cell,
                chunk,
                new HashSet<int>(cell.PossibleStates),
                new HashSet<int> { chosenState },
                cell.IsBoundary
            );

            // Propagate to neighbors
            if (wfcGenerator != null)
                wfcGenerator.AddPropagationEvent(evt);
            else
                PropagateCollapse(cell, chunk);

            return true;
        }

        // Method to propagate collapse to neighboring cells
        private void PropagateCollapse(Cell cell, Chunk chunk)
        {
            // Find cell position in chunk
            Vector3Int cellPos = cell.Position;

            // For each neighboring cell direction
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                // Calculate neighbor position
                Vector3Int offset = dir.ToVector3Int();
                Vector3Int neighborPos = cellPos + offset;

                // Check if within this chunk
                if (neighborPos.x >= 0 && neighborPos.x < chunk.Size &&
                    neighborPos.y >= 0 && neighborPos.y < chunk.Size &&
                    neighborPos.z >= 0 && neighborPos.z < chunk.Size)
                {
                    Cell neighbor = chunk.GetCell(neighborPos.x, neighborPos.y, neighborPos.z);
                    ApplyConstraintToNeighbor(cell, neighbor, dir, chunk);
                }
                // Check if in adjacent chunk
                else if (chunk.Neighbors.TryGetValue(dir, out Chunk adjacentChunk))
                {
                    // Convert to position in adjacent chunk
                    Vector3Int adjacentChunkPos = new Vector3Int(
                        (neighborPos.x + chunk.Size) % chunk.Size,
                        (neighborPos.y + chunk.Size) % chunk.Size,
                        (neighborPos.z + chunk.Size) % chunk.Size
                    );

                    Cell neighbor = adjacentChunk.GetCell(adjacentChunkPos.x, adjacentChunkPos.y, adjacentChunkPos.z);
                    if (neighbor != null)
                        ApplyConstraintToNeighbor(cell, neighbor, dir, adjacentChunk);
                }
            }
        }

        // Method to apply constraints to a neighboring cell
        private void ApplyConstraintToNeighbor(Cell source, Cell target, Direction direction, Chunk targetChunk)
        {
            // Skip if target already collapsed or source not collapsed
            if (target.CollapsedState.HasValue || source.CollapsedState == null)
                return;

            // Store old states for propagation
            HashSet<int> oldStates = new HashSet<int>(target.PossibleStates);
            HashSet<int> compatibleStates = new HashSet<int>();

            foreach (int targetState in target.PossibleStates)
            {
                bool isCompatible = true;

                if (wfcGenerator != null)
                    isCompatible = wfcGenerator.AreStatesCompatible(targetState, source.CollapsedState.Value, direction);
                else
                    isCompatible = Mathf.Abs(targetState - source.CollapsedState.Value) <= 1;

                if (isCompatible)
                    compatibleStates.Add(targetState);
            }

            // Update target's possible states if needed
            if (compatibleStates.Count > 0 && !compatibleStates.SetEquals(oldStates))
            {
                bool changed = target.SetPossibleStates(compatibleStates);

                // If changed, propagate further
                if (changed)
                    PropagateCollapse(target, targetChunk);
            }
        }

        // Method to generate the mesh for a chunk
        private void GenerateChunkMesh(Chunk chunk)
        {
            if (performanceMonitor != null)
                performanceMonitor.StartComponentTiming("MeshGeneration");

            // Update chunk state
            UpdateChunkState(chunk.Position, ChunkLifecycleState.GeneratingMesh);

            try
            {
                // Find mesh generator in the scene
                if (meshGenerator == null)
                    meshGenerator = FindObjectOfType<MeshGenerator>();

                if (meshGenerator != null)
                    meshGenerator.GenerateChunkMesh(chunk.Position, chunk);
                else
                    Debug.LogError("Generate Chunk Mesh not found!!!");

                // Mark chunk as processed
                chunk.IsDirty = false;

                // Update chunk state
                UpdateChunkState(chunk.Position, ChunkLifecycleState.Active);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error generating mesh for chunk at {chunk.Position}: {e.Message}");

                // Update chunk state to error
                if (chunkStates.TryGetValue(chunk.Position, out ChunkState state))
                    state.LastError = e.Message;

                UpdateChunkState(chunk.Position, ChunkLifecycleState.Error);
                throw;
            }
            finally
            {
                if (performanceMonitor != null)
                    performanceMonitor.EndComponentTiming("MeshGeneration");
            }
        }
        #endregion

        #region Helper Methods

        private void ApplyInitialConstraints(Chunk chunk, System.Random random)
        {
            if (terrainGenerator == null)
            {
                // Fallback to very basic terrain
                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            Cell cell = chunk.GetCell(x, y, z);
                            if (cell != null)
                            {
                                // Simple flat terrain at half height
                                if (y < chunk.Size / 2)
                                    cell.Collapse(TerrainStateRegistry.STATE_GROUND);
                                else
                                    cell.Collapse(TerrainStateRegistry.STATE_AIR);
                            }
                        }
                    }
                }
                return;
            }

            // Use the terrain generator to apply constraints
            terrainGenerator.GenerateTerrain(chunk, random);
        }

        // Method to connect chunk neighbors
        private void ConnectChunkNeighbors(Chunk chunk)
        {
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

        // Method to initialize boundary buffers for a chunk
        private void InitializeBoundaryBuffers(Chunk chunk)
        {
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                if (!chunk.Neighbors.ContainsKey(dir))
                    continue;

                Chunk neighbor = chunk.Neighbors[dir];

                // Create buffer for this boundary
                BoundaryBuffer buffer = new BoundaryBuffer(dir, chunk);
                buffer.AdjacentChunk = neighbor;

                // Get cells at this boundary
                List<Cell> boundaryCells = GetBoundaryCells(chunk, dir);
                buffer.BoundaryCells = boundaryCells;

                // Mark all boundary cells explicitly
                foreach (var cell in boundaryCells)
                {
                    cell.IsBoundary = true;
                    cell.BoundaryDirection = dir;
                }

                // Create buffer cells to mirror neighbor
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

            // Immediately synchronize buffers after initialization
            foreach (var buffer in chunk.BoundaryBuffers.Values)
                SynchronizeBuffer(buffer);
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

                // If either cell is already collapsed, propagate that state
                if (cell1.CollapsedState.HasValue && !cell2.CollapsedState.HasValue)
                    cell2.Collapse(cell1.CollapsedState.Value);
                else if (cell2.CollapsedState.HasValue && !cell1.CollapsedState.HasValue)
                    cell1.Collapse(cell2.CollapsedState.Value);
                // Otherwise ensure they have compatible states
                else if (!cell1.CollapsedState.HasValue && !cell2.CollapsedState.HasValue)
                {
                    // Find compatible states
                    HashSet<int> compatibleStates = new HashSet<int>();
                    foreach (int state1 in cell1.PossibleStates)
                    {
                        foreach (int state2 in cell2.PossibleStates)
                        {
                            if (wfcGenerator.AreStatesCompatible(state1, state2, buffer.Direction))
                            {
                                compatibleStates.Add(state1);
                                break;
                            }
                        }
                    }

                    // If found compatible states, update cell1
                    if (compatibleStates.Count > 0)
                        cell1.SetPossibleStates(compatibleStates);
                }
            }
        }

        // Helper method to get cells at the boundary of a chunk
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

        // Helper method to calculate world position of the chunk
        private Vector3 GetChunkWorldPosition(Vector3Int chunkPos)
        {
            return new Vector3(
                chunkPos.x * ChunkSize,
                chunkPos.y * ChunkSize,
                chunkPos.z * ChunkSize
            );
        }

        // Helper method to generate a consistent seed based on chunk position
        private int GenerateChunkSeed(Vector3Int chunkPos)
        {
            int baseSeed = activeConfig.World.randomSeed;
            return baseSeed +
                   chunkPos.x * 73856093 ^
                   chunkPos.y * 19349663 ^
                   chunkPos.z * 83492791;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Method to update configuration at runtime
        /// </summary>
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

            // Log changes requiring updates
            if (oldLoadDistance != LoadDistance || oldUnloadDistance != UnloadDistance)
                Debug.Log("ChunkManager: Load/unload distances updated. Refreshing chunk management.");

            if (oldChunkSize != ChunkSize)
                Debug.LogWarning("ChunkManager: Chunk size changed. This will require world regeneration.");
        }

        /// <summary>
        /// Reset the chunk system completely
        /// </summary>
        public void ResetChunkSystem()
        {
            // Stop processing while reset
            StopAllCoroutines();

            // Clear the task queue
            while (chunkTasks.Count > 0)
                chunkTasks.Dequeue();

            // Remove all mesh objects
            foreach (var chunkPos in loadedChunks.Keys.ToList())
                RemoveChunkMeshObject(chunkPos);

            // Clear chunk collections
            loadedChunks.Clear();
            chunkStates.Clear();

            // Restart
            StartCoroutine(AdaptiveStrategyUpdateCoroutine());
        }

        /// <summary>
        /// Force creation of a chunk at a specific position
        /// </summary>
        public void CreateChunkAt(Vector3Int position)
        {
            if (position.y > 0 && heightmapController != null &&
            !heightmapController.ShouldGenerateMeshAt(position, ChunkSize))
                return;

            // Only create at ground level
            position.y = 0;

            // Skip if already loaded or in the process of loading
            ChunkLifecycleState currentState = GetChunkState(position);
            if (currentState != ChunkLifecycleState.None && currentState != ChunkLifecycleState.Error)
                return;

            // Create high priority task
            ChunkTask task = new ChunkTask
            {
                Type = ChunkTaskType.Create,
                Position = position,
                Priority = 100f
            };

            chunkTasks.Enqueue(task, task.Priority);
            UpdateChunkState(position, ChunkLifecycleState.Pending);
        }

        /// <summary>
        /// Force generation of a mesh for a specific chunk
        /// </summary>
        public void GenerateMeshForChunkAt(Vector3Int position)
        {
            if (!loadedChunks.TryGetValue(position, out Chunk chunk))
            {
                Debug.LogWarning($"Cannot generate mesh - no chunk at {position}");
                return;
            }

            // Skip if already generating
            ChunkLifecycleState currentState = GetChunkState(position);
            if (currentState == ChunkLifecycleState.GeneratingMesh)
                return;

            // Queue high-priority mesh generation task
            ChunkTask task = new ChunkTask
            {
                Type = ChunkTaskType.GenerateMesh,
                Chunk = chunk,
                Priority = 100f
            };

            chunkTasks.Enqueue(task, task.Priority);
        }

        #endregion

        // Helper class for chunk tasks
        private class ChunkTask
        {
            public ChunkTaskType Type { get; set; }
            public Vector3Int Position { get; set; }
            public Chunk Chunk { get; set; }
            public int MaxIterations { get; set; }
            public float Priority { get; set; }
        }

        // Helper enum for task types
        private enum ChunkTaskType
        {
            Create,
            Unload,
            Collapse,
            GenerateMesh
        }

        /// <summary>
        /// Initiates chunk generation around the player
        /// </summary>
        public void CreateChunksAroundPlayer()
        {
            // Initialize heightmap controller if needed
            if (heightmapController != null && !heightmapController.IsInitialized)
                heightmapController.Initialize();

            Vector3Int viewerChunk = new Vector3Int(
                Mathf.FloorToInt(viewer.position.x / ChunkSize),
                0,
                Mathf.FloorToInt(viewer.position.z / ChunkSize)
            );

            // Start with creating the immediate chunk first
            CreateChunkAt(viewerChunk);

            // Queue others to be created over time
            StartCoroutine(GradualChunkCreation(viewerChunk));
        }

        // Coroutine to create chunks gradually
        private IEnumerator GradualChunkCreation(Vector3Int centerChunk)
        {
            // Create ground level chunks first in a radius around player
            for (int x = -2; x <= 2; x++)
            {
                for (int z = -2; z <= 2; z++)
                {
                    Vector3Int groundChunkPos = new Vector3Int(centerChunk.x + x, 0, centerChunk.z + z);
                    if (!loadedChunks.ContainsKey(groundChunkPos))
                    {
                        CreateChunkAt(groundChunkPos);
                        yield return null;
                    }
                }
            }

            // Create vertical chunks based on the heightmap
            int maxVerticalChunks = 8;

            for (int x = -3; x <= 3; x++)
            {
                for (int z = -3; z <= 3; z++)
                {
                    for (int y = 1; y < maxVerticalChunks; y++)
                    {
                        Vector3Int chunkPos = new Vector3Int(centerChunk.x + x, y, centerChunk.z + z);

                        // Only create chunk if heightmap indicates terrain should be here
                        if (heightmapController != null &&
                            heightmapController.ShouldGenerateMeshAt(chunkPos, ChunkSize) &&
                            !loadedChunks.ContainsKey(chunkPos))
                        {
                            CreateChunkAt(chunkPos);
                            yield return null;
                        }
                    }
                }
            }
        }
    }
}