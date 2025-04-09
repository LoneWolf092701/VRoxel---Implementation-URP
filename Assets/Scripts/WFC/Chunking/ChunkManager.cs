/*
 * ChunkManager.cs
 * -----------------------------
 * Manages the dynamic loading, unloading, and processing of terrain chunks.
 * 
 * This system enables an "infinite" procedural world by:
 * 1. Tracking the viewer's position and movement
 * 2. Predictively loading chunks in the viewer's path
 * 3. Unloading distant chunks to conserve memory
 * 4. Prioritizing chunks based on distance, view alignment, and other factors
 * 5. Managing the full lifecycle of chunks from creation through collapse to mesh generation
 * 
 * The chunk management approach is adaptive, adjusting its behavior based on:
 * - System performance (FPS)
 * - Viewer movement patterns
 * - Distance from viewer
 * - Level of detail requirements
 * 
 * This allows the system to scale across different hardware capabilities
 * while maintaining a smooth experience.
 */
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
using WFC.MarchingCubes;
using WFC.Boundary;
using System;

namespace WFC.Chunking
{
    /// <summary>
    /// Updated ChunkManager that uses the central configuration system,
    /// implements advanced predictive generation, and properly manages chunk lifecycle
    /// </summary>
    public class ChunkManager : MonoBehaviour
    {
        #region Configuration and References

        [Header("Configuration")]
        [Tooltip("Override the global configuration with a specific asset")]
        [SerializeField] private WFCConfiguration configOverride;

        [Header("References")]
        [SerializeField] public Transform viewer;
        [SerializeField] private WFCGenerator wfcGenerator;
        [SerializeField] private PerformanceMonitor performanceMonitor;
        [SerializeField] private MeshGenerator meshGenerator;

        [Header("Prediction Settings")]
        [SerializeField] private int movementHistorySize = 60; // Store 60 frames of movement history
        [SerializeField] private float basePredictionTime = 2.0f; // Default look ahead time

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogging = false;

        #endregion

        #region Private Fields

        // State management
        private Dictionary<Vector3Int, ChunkState> chunkStates = new Dictionary<Vector3Int, ChunkState>();

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

        // Events and timing
        private float lastChunkStateCleanupTime = 0f;

        // Chunk state change event
        public delegate void ChunkStateChangeHandler(Vector3Int chunkPos, ChunkLifecycleState oldState, ChunkLifecycleState newState);
        public event ChunkStateChangeHandler OnChunkStateChanged;

        #endregion

        #region Properties

        // Properties use configuration
        private int ChunkSize => activeConfig.World.chunkSize;
        private float LoadDistance => currentLoadDistance; // Using adaptive distance
        private float UnloadDistance => activeConfig.Performance.unloadDistance;
        private int MaxConcurrentChunks => maxConcurrentChunks; // Adaptive

        private Vector3 lastChunkGenerationPosition;
        private float chunkGenerationDistance = 10f; // Distance player must move before generating new chunks


        #endregion

        #region Initialization

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

            // Initialize references
            InitializeReferences();

            // Initialize adaptive parameters
            currentLoadDistance = activeConfig.Performance.loadDistance;
            maxConcurrentChunks = activeConfig.Performance.maxConcurrentChunks;

            // Start adaptive strategy coroutine
            StartCoroutine(AdaptiveStrategyUpdateCoroutine());

            // Use a more robust delayed initialization approach
            StartCoroutine(RobustDelayedChunkGeneration());

            // Explicitly prevent origin chunk generation during startup
            //Vector3Int originChunk = Vector3Int.zero;
            //UpdateChunkState(originChunk, ChunkLifecycleState.Error);
            Vector3Int viewerChunk = new Vector3Int(
                Mathf.FloorToInt(viewerPosition.x / ChunkSize),
                Mathf.FloorToInt(viewerPosition.y / ChunkSize),
                Mathf.FloorToInt(viewerPosition.z / ChunkSize)
            );
            lastChunkGenerationPosition = Vector3.zero; // Initialize this to ensure generation happens
            Debug.Log($"Initial viewer chunk: {viewerChunk}");
            CreateChunkAt(viewerChunk); // Force creation of the initial chunk

            lastChunkGenerationPosition = Vector3.zero; // Initialize this to ensure the generation happens

            Debug.Log($"ChunkManager Start completed. Initial viewer position: {(viewer != null ? viewer.position.ToString() : "No viewer")}");
        }

        /// <summary>
        /// A more robust coroutine that waits multiple frames and verifies the viewer position
        /// before generating any chunks
        /// </summary>
        private IEnumerator RobustDelayedChunkGeneration()
        {
            // Wait for 3 frames to ensure more complete initialization
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            // Force update the viewer position
            if (viewer != null)
            {
                // Update both position trackers
                viewerPosition = viewer.position;
                lastViewerPosition = viewer.position;

                Debug.Log($"Delayed chunk generation - Viewer position: {viewerPosition}");

                // Verify we have a non-zero position
                if (viewerPosition.magnitude < 0.001f)
                {
                    Debug.LogWarning("Player position is still at or near origin. Waiting longer...");
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

            // Clear any existing chunks (especially at origin)
            foreach (var chunkPos in loadedChunks.Keys.ToList())
            {
                // Skip if it's a chunk around the player
                if (Vector3.Distance(
                    new Vector3(chunkPos.x * ChunkSize, chunkPos.y * ChunkSize, chunkPos.z * ChunkSize),
                    viewerPosition) < ChunkSize * 5)
                {
                    continue;
                }

                // Otherwise, unload it
                Debug.Log($"Removing unwanted chunk at {chunkPos}");
                UnloadChunk(chunkPos);
            }

            // Generate chunks around the player
            Debug.Log($"Generating chunks around player at position: {viewerPosition}");
            CreateChunksAroundPlayer();
        }

        // Reference initialization
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

            // Get hierarchical constraint system reference
            if (wfcGenerator != null)
            {
                hierarchicalConstraints = wfcGenerator.GetHierarchicalConstraintSystem();
            }
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            UpdateViewerData();

            // Check if player has moved far enough to generate new chunks
            if (Vector3.Distance(viewerPosition, lastChunkGenerationPosition) > chunkGenerationDistance)
            {
                CreateChunksAroundPlayer();
                lastChunkGenerationPosition = viewerPosition;
                Debug.Log($"Player moved to {viewerPosition}, generating new chunks");
            }

            // Calculate predicted position using advanced prediction
            predictedViewerPosition = PredictFuturePosition(predictionTime);

            // Update chunk priorities
            UpdateChunkPriorities();

            // Assign LOD levels based on distance
            AssignLODLevels();

            // Manage chunks (load/unload)
            ManageChunks();

            CleanupNonGroundChunks();

            // Process chunk tasks
            ProcessChunkTasks();

            // Process dirty chunks that need mesh generation
            ProcessDirtyChunks();

            ParallelWFCManager parallelManager = FindObjectOfType<ParallelWFCManager>();
            if (parallelManager != null && parallelProcessor == null)
            {
                parallelProcessor = parallelManager.GetParallelProcessor();
                Debug.Log("Connected to ParallelWFCManager for parallel processing");
            }

            // Optimize memory for inactive chunks
            OptimizeInactiveChunks();

            // Periodically clean up chunk states
            CleanupStaleChunkStates();

            // Debug output
            Debug.Log($"Viewer position: {viewerPosition}, Predicted: {predictedViewerPosition}, Chunk: {new Vector3Int(Mathf.FloorToInt(viewerPosition.x / ChunkSize), Mathf.FloorToInt(viewerPosition.y / ChunkSize), Mathf.FloorToInt(viewerPosition.z / ChunkSize))}");
            if (Input.GetKeyDown(KeyCode.P))
            {
                CreateChunksAroundPlayer();
            }
        }

        // Chunk management
        private void ProcessDirtyChunks()
        {
            // Find chunks that need mesh generation
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

                    // Mark as not dirty to prevent duplicate tasks
                    chunk.IsDirty = false;

                    if (enableDebugLogging)
                    {
                        Debug.Log($"Queued mesh generation for dirty chunk at {chunkPos}");
                    }
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

            // Debug.Log state change if debug logging is enabled
            if (enableDebugLogging && (newState == ChunkLifecycleState.Error || oldState == ChunkLifecycleState.Error))
            {
                Debug.Log($"Chunk {chunkPos} state changed: {oldState} -> {newState}");
            }

            // Trigger events
            OnChunkStateChanged?.Invoke(chunkPos, oldState, newState);
        }

        /// <summary>
        /// Gets the current state of a chunk
        /// </summary>
        public ChunkLifecycleState GetChunkState(Vector3Int chunkPos)
        {
            if (chunkStates.TryGetValue(chunkPos, out ChunkState state))
            {
                return state.State;
            }

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
                if ((state.State == ChunkLifecycleState.Pending ||
                     state.State == ChunkLifecycleState.Loading) &&
                     state.GetTimeInCurrentState() > 30f)
                {
                    if (state.ProcessingAttempts < 3)
                    {
                        // Retry up to 3 times
                        state.ProcessingAttempts++;
                        state.TransitionTo(ChunkLifecycleState.None);

                        Debug.Log($"Resetting stuck chunk {chunkPos} (Attempt {state.ProcessingAttempts})");
                    }
                    else
                    {
                        // After 3 attempts, mark as error
                        state.TransitionTo(ChunkLifecycleState.Error);
                        state.LastError = "Exceeded maximum processing attempts";
                        Debug.LogWarning($"Chunk {chunkPos} failed to load after {state.ProcessingAttempts} attempts");
                    }
                }

                // Clean up error states after a while
                if (state.State == ChunkLifecycleState.Error &&
                    state.GetTimeInCurrentState() > 120f)
                {
                    stateKeysToRemove.Add(chunkPos);
                }
            }

            // Remove the states
            foreach (var key in stateKeysToRemove)
            {
                chunkStates.Remove(key);
            }

            if (stateKeysToRemove.Count > 0)
            {
                Debug.Log($"Cleaned up {stateKeysToRemove.Count} stale chunk states");
            }
        }

        #endregion

        #region Advanced Movement Prediction

        /*
         * PredictFuturePosition
         * -----------------------------
         * Advanced movement prediction system to anticipate where the viewer will be.
         * 
         * This predictive loading approach uses motion analysis to:
         * 1. Analyze recent movement history for patterns
         * 2. Calculate instantaneous velocity and acceleration
         * 3. Evaluate longer-term direction consistency
         * 4. Apply a physics-based prediction model including jerk (rate of change of acceleration)
         * 
         * The prediction formula is an extended version of the standard kinematic equation:
         * P = P0 + V*t + 1/2A*t2 + 1/6J*t3
         * 
         * Where:
         * - P = predicted position
         * - P0 = current position
         * - V = velocity
         * - A = acceleration
         * - J = jerk
         * - t = time (adjusted based on movement consistency)
         * 
         * This predictive approach significantly improves terrain loading responsiveness
         * by anticipating viewer movement and preparing terrain before it's needed.
         */
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

        // Calculate direction consistency based on movement history
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

        // Calculate average acceleration from movement history
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

        // Calculate jerk (rate of change of acceleration) from movement history
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

        /*
        * CalculateChunkInterest
        * ----------------------------------------------------------------------------
        * Calculates how interesting a chunk is for prioritized loading.
        * 
        * Multi-factorial interest calculation:
        * 1. Distance-based interest: closer chunks are more interesting
        *    interestDistance = 1 / (1 + distance * 0.01)
        * 
        * 2. View-based interest: chunks in field of view are prioritized
        *    viewAlignment = dot(dirToChunk, viewDirection)
        *    interestView = (viewAlignment + 1) * 0.5  // normalized to 0-1
        * 
        * 3. Content-based interest: partially collapsed chunks get priority
        *    - Calculates percentage of collapsed cells
        *    - Maximum interest for chunks 30-70% complete (reduces thrashing)
        * 
        * 4. Feature-based interest: chunks with special terrain features
        *    - Checks global constraints affecting the chunk
        *    - Higher priority for chunks with mountains, rivers, etc.
        * 
        * The combined weighted score determines chunk processing order,
        * creating a more natural and visually focused loading pattern.
        * 
        * Parameters:
        * - chunkPos: Position of the chunk to evaluate
        * 
        * Returns: Interest score between 0-1
        */
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

        // Calculate the world position of a chunk based on its grid position
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

        #endregion

        #region Adaptive Chunk Generation Strategy

        // Adaptive strategy update coroutine
        private IEnumerator AdaptiveStrategyUpdateCoroutine()
        {
            // Update strategy less frequently than every frame
            while (true)
            {
                AdaptChunkGenerationStrategy();
                yield return new WaitForSeconds(0.5f); // Update every half second
            }
        }

        // Adaptive chunk generation strategy
        private void AdaptChunkGenerationStrategy()
        {
            // Update strategy based on performance metrics and viewer behavior

            // 1. Adjust prediction time based on viewer speed
            float viewerSpeed = viewerVelocity.magnitude;
            predictionTime = Mathf.Lerp(basePredictionTime, basePredictionTime * 2.5f, Mathf.Clamp01(viewerSpeed / 20f));

            // 2. Adjust generation radius based on performance
            float fps = 1f / Time.smoothDeltaTime;
            float targetFps = 90f;   // Target FPS

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
            // This is implemented in the Chunk class
            chunk.OptimizeMemory();
        }

        private void RestoreChunkFromOptimized(Chunk chunk)
        {
            // This is implemented in the Chunk class
            chunk.RestoreFromOptimized();
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

        #endregion

        #region Chunk Priority Management

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
            Vector3 chunkWorldPos = GetChunkWorldPosition(chunk.Position);

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

        #endregion

        #region Chunk Management

        /*
         * ManageChunks
         * ----------------------------------------------------------------------------
         * Core function that handles loading and unloading chunks dynamically.
         * 
         * The active chunk management process:
         * 1. Identifies chunks to load based on distance from viewer
         *    and predicted movement direction
         * 2. Prioritizes chunks based on interest value and distance
         * 3. Limits the number of concurrent chunks based on performance
         * 4. Creates chunk loading tasks with appropriate priorities
         * 5. Identifies distant chunks for unloading
         * 6. Creates unload tasks for chunks that are too far away
         * 
         * This dynamic loading system is what enables an infinite world
         * experience while keeping memory and processing requirements manageable.
         * 
         * The function manages the chunk lifecycle through state transitions
         * and queued tasks to ensure orderly creation and removal.
         */
        private void ManageChunks()
        {
            // Get chunks to load
            List<Vector3Int> chunksToLoad = GetChunksToLoad();

            if (enableDebugLogging)
            {
                Debug.Log($"Found {chunksToLoad.Count} chunks to load");
            }

            foreach (var chunkPos in chunksToLoad)
            {
                ChunkLifecycleState currentState = GetChunkState(chunkPos);

                // Only create chunks that are not already being processed
                if (currentState == ChunkLifecycleState.None ||
                    currentState == ChunkLifecycleState.Error)
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

                    // Update chunk state
                    UpdateChunkState(chunkPos, ChunkLifecycleState.Pending);

                    if (enableDebugLogging)
                    {
                        Debug.Log($"Queued chunk creation at {chunkPos}");
                    }
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

                        // Update chunk state
                        UpdateChunkState(chunkPos, ChunkLifecycleState.Unloading);

                        if (enableDebugLogging)
                        {
                            Debug.Log($"Queued chunk unload at {chunkPos}");
                        }
                    }
                }
            }
        }

        public void ForceGenerateInitialChunks()
        {
            // Force create chunks around the starting position
            Vector3Int viewerChunk = new Vector3Int(
                Mathf.FloorToInt(viewerPosition.x / ChunkSize),
                Mathf.FloorToInt(viewerPosition.y / ChunkSize),
                Mathf.FloorToInt(viewerPosition.z / ChunkSize)
            );

            Debug.Log($"Generating initial chunks around viewer at {viewerChunk}");

            // Create a 3x3x3 grid of chunks around the player
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        Vector3Int chunkPos = new Vector3Int(
                            viewerChunk.x + x,
                            viewerChunk.y + y,
                            viewerChunk.z + z
                        );

                        // Already loaded or pending?
                        ChunkLifecycleState currentState = GetChunkState(chunkPos);
                        if (currentState != ChunkLifecycleState.None &&
                            currentState != ChunkLifecycleState.Error)
                            continue;

                        // Create task to generate this chunk
                        ChunkTask task = new ChunkTask
                        {
                            Type = ChunkTaskType.Create,
                            Position = chunkPos,
                            Priority = 100f // High priority
                        };

                        chunkTasks.Enqueue(task, task.Priority);

                        // Update chunk state
                        UpdateChunkState(chunkPos, ChunkLifecycleState.Pending);
                    }
                }
            }

            Debug.Log("Forced creation of initial chunks around player");
        }

        // Get chunks to load based on viewer position and distance
        private List<Vector3Int> GetChunksToLoad()
        {
            // Calculate chunk coordinates of viewer
            Vector3Int viewerChunk = new Vector3Int(
            Mathf.FloorToInt(viewerPosition.x / ChunkSize),
            0, // IMPORTANT: Force y=0 to only create ground-level chunks
            Mathf.FloorToInt(viewerPosition.z / ChunkSize)
            );

            // Find all chunk positions within load distance
            List<Vector3Int> chunksToLoad = new List<Vector3Int>();

            // Calculate load distance in chunks - ensure it's at least 2 for a minimum surrounding grid
            int loadChunks = Mathf.Max(2, Mathf.CeilToInt(LoadDistance / ChunkSize));

            for (int x = viewerChunk.x - loadChunks; x <= viewerChunk.x + loadChunks; x++)
            {
                int y = 0;

                //for (int y = viewerChunk.y - loadChunks; y <= viewerChunk.y + loadChunks; y++)
                //{
                for (int z = viewerChunk.z - loadChunks; z <= viewerChunk.z + loadChunks; z++)
                    {
                        Vector3Int pos = new Vector3Int(x, y, z);

                        // Skip if already loaded
                        if (loadedChunks.ContainsKey(pos))
                            continue;

                        // Check if within load distance (with a buffer)
                        Vector3 chunkWorldPos = GetChunkWorldPosition(pos);
                        float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

                        if (distance <= LoadDistance * 1.2f) // 20% buffer to ensure we get enough chunks
                        {
                            chunksToLoad.Add(pos);
                        }
                    }
                //}
            }

            // If no chunks found, create a 3x3x3 grid around the player
            if (chunksToLoad.Count == 0)
            {
                Debug.LogWarning("No chunks to load found! Creating a grid around the viewer.");

                // Create a 3x3x3 grid of chunks around the player
                for (int x = -1; x <= 1; x++)
                {
                    //for (int y = -1; y <= 1; y++)
                    //{
                        for (int z = -1; z <= 1; z++)
                        {
                            Vector3Int pos = new Vector3Int(
                                viewerChunk.x + x,
                                0,
                                //viewerChunk.y + y,
                                viewerChunk.z + z
                            );

                            // Skip if already loaded
                            if (loadedChunks.ContainsKey(pos))
                                continue;

                            chunksToLoad.Add(pos);
                        }
                    //}
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
                Vector3 posA = GetChunkWorldPosition(a);
                Vector3 posB = GetChunkWorldPosition(b);

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
            // Get current viewer position
            Vector3 viewerPos = viewer.position;

            // Find chunks that are too far from current position only
            return loadedChunks.Keys.Where(chunkPos => {
                Vector3 chunkWorldPos = GetChunkWorldPosition(chunkPos);
                float distance = Vector3.Distance(chunkWorldPos, viewerPos);

                // Check if chunk Y position is non-zero (above ground)
                bool isAboveGround = chunkPos.y > 0;

                // Always unload chunks above ground level (y > 0)
                // For ground level chunks, only unload if beyond distance threshold
                return isAboveGround || distance > UnloadDistance;
            }).ToList();
        }

        private void CleanupNonGroundChunks()
        {
            List<Vector3Int> chunksToRemove = new List<Vector3Int>();

            foreach (var chunkPos in loadedChunks.Keys)
            {
                // Identify chunks above ground level
                if (chunkPos.y > 0)
                {
                    chunksToRemove.Add(chunkPos);
                }
            }

            foreach (var chunkPos in chunksToRemove)
            {
                UnloadChunk(chunkPos);
                Debug.Log($"Forcibly removed above-ground chunk at {chunkPos}");
            }
        }

        // Calculate load priority for a chunk based on multiple factors
        private float CalculateLoadPriority(Vector3Int chunkPos)
        {
            // Convert to world position
            Vector3 chunkWorldPos = GetChunkWorldPosition(chunkPos);

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

            return priority;
        }

        #endregion

        #region Task Processing

        // Queue to hold chunk tasks
        private void ProcessChunkTasks()
        {
            // Process a limited number of tasks per frame
            int tasksProcessed = 0;
            int maxTasksPerFrame = 2; // Could also come from config

            while (chunkTasks.Count > 0 && tasksProcessed < maxTasksPerFrame)
            {
                ChunkTask task = chunkTasks.Dequeue();

                try
                {
                    // Only use parallel processing for collapse tasks, not mesh generation
                    if (parallelProcessor != null && task.Type == ChunkTaskType.Collapse)
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
                        {
                            state.LastError = e.Message;
                        }
                        UpdateChunkState(task.Position, ChunkLifecycleState.Error);
                    }
                    tasksProcessed++;
                }
            }
        }

        #endregion

        #region Chunk Operations

        // Method to create a chunk
        public void CreateChunk(Vector3Int position)
        {
            // Update chunk state to loading
            UpdateChunkState(position, ChunkLifecycleState.Loading);

            if (enableDebugLogging)
            {
                Debug.Log($"Creating chunk at {position}");
            }

            // Check if chunk already exists
            if (loadedChunks.ContainsKey(position))
            {
                Debug.LogWarning($"Chunk at {position} already exists! Skipping creation.");

                // Update chunk state to active
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

                // Get WFC generator reference if not already set
                if (wfcGenerator == null)
                {
                    wfcGenerator = FindObjectOfType<WFCGenerator>();
                }

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

                Debug.Log($"Chunk creation completed for {position}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error creating chunk at {position}: {e.Message}");

                // Update chunk state to error
                if (chunkStates.TryGetValue(position, out ChunkState state))
                {
                    state.LastError = e.Message;
                }

                UpdateChunkState(position, ChunkLifecycleState.Error);
                throw; // Re-throw to allow the task processing system to handle it
            }
        }

        // Method to unload a chunk from the world
        private void UnloadChunk(Vector3Int position)
        {
            // Update chunk state
            UpdateChunkState(position, ChunkLifecycleState.Unloading);

            if (enableDebugLogging)
            {
                Debug.Log($"Unloading chunk at {position}");
            }

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
                        {
                            neighbor.Neighbors.Remove(oppositeDir);
                        }
                    }

                    // Clear neighbors and boundary buffers
                    chunk.Neighbors.Clear();
                    chunk.BoundaryBuffers.Clear();

                    // Remove any associated mesh or game objects
                    RemoveChunkMeshObject(position);

                    // Remove from loaded chunks dictionary
                    loadedChunks.Remove(position);

                    Debug.Log($"Chunk at {position} unloaded successfully");
                }
                else
                {
                    Debug.LogWarning($"Tried to unload non-existent chunk at {position}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error unloading chunk at {position}: {e.Message}");
                throw; // Re-throw to allow the task processing system to handle it
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

                Debug.Log($"Removed mesh object for chunk at {position}");
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
                // If using parallel processing, try to queue for parallel execution
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

                // Fallback to direct processing if parallel processing is unavailable or queue is full
                bool madeProgress = true;
                int iterations = 0;

                // Main WFC algorithm loop
                while (madeProgress && iterations < maxIterations && !chunk.IsFullyCollapsed)
                {
                    madeProgress = CollapseNextCell(chunk);
                    iterations++;
                }

                // Mark as fully collapsed if no more progress can be made
                if (!madeProgress || iterations >= maxIterations)
                {
                    chunk.IsFullyCollapsed = true;
                }

                // Queue mesh generation task after collapse
                ChunkTask meshTask = new ChunkTask
                {
                    Type = ChunkTaskType.GenerateMesh,
                    Chunk = chunk,
                    Priority = chunk.Priority * 0.9f // Slightly lower priority than collapse
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
                {
                    state.LastError = e.Message;
                }

                UpdateChunkState(chunk.Position, ChunkLifecycleState.Error);
                throw; // Re-throw to allow the task processing system to handle it
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
            {
                hierarchicalConstraints.PrecomputeChunkConstraints(chunk, WFCConfigManager.Config.World.maxStates);
            }

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

                        // Skip cells with only one state (will be collapsed in propagation)
                        if (cell.PossibleStates.Count <= 1)
                            continue;

                        // Apply constraints to calculate effective entropy
                        int effectiveEntropy = cell.Entropy;

                        // Apply constraints if available
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

                        // First priority: Lowest entropy
                        if (effectiveEntropy < lowestEntropy)
                        {
                            lowestEntropy = effectiveEntropy;
                            cellToCollapse = cell;

                            // Recalculate distance for the new candidate
                            Vector3 cellWorldPos = new Vector3(
                                chunk.Position.x * chunk.Size + cell.Position.x,
                                chunk.Position.y * chunk.Size + cell.Position.y,
                                chunk.Position.z * chunk.Size + cell.Position.z
                            );
                            closestDistance = Vector3.Distance(cellWorldPos, viewerPosition);
                        }
                        // Tie-breaking: Same entropy but closer to viewer
                        else if (effectiveEntropy == lowestEntropy)
                        {
                            Vector3 cellWorldPos = new Vector3(
                                chunk.Position.x * chunk.Size + cell.Position.x,
                                chunk.Position.y * chunk.Size + cell.Position.y,
                                chunk.Position.z * chunk.Size + cell.Position.z
                            );
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

            // If found a cell, collapse it
            if (cellToCollapse != null)
            {
                // Choose a state based on constraints if available
                int[] possibleStates = cellToCollapse.PossibleStates.ToArray();
                int chosenState;

                if (hierarchicalConstraints != null && possibleStates.Length > 1)
                {
                    Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(
                        cellToCollapse, chunk, WFCConfigManager.Config.World.maxStates);

                    // Create weighted selection based on biases
                    float[] weights = new float[possibleStates.Length];
                    float totalWeight = 0;

                    for (int i = 0; i < possibleStates.Length; i++)
                    {
                        int state = possibleStates[i];
                        weights[i] = 1.0f; // Base weight

                        // Apply bias if available
                        if (biases.TryGetValue(state, out float bias))
                        {
                            // Convert bias (-1 to 1) to weight multiplier (0.25 to 4)
                            float multiplier = Mathf.Pow(2, bias * 2);
                            weights[i] *= multiplier;
                        }

                        // Ensure weight is positive
                        weights[i] = Mathf.Max(0.1f, weights[i]);
                        totalWeight += weights[i];
                    }

                    // Random selection based on weights
                    float randomValue = UnityEngine.Random.Range(0, totalWeight);
                    float cumulativeWeight = 0;

                    chosenState = possibleStates[0]; // Default to first state

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
                cellToCollapse.Collapse(chosenState);

                // Create propagation event
                PropagationEvent evt = new PropagationEvent(
                    cellToCollapse,
                    chunk,
                    new HashSet<int>(cellToCollapse.PossibleStates),
                    new HashSet<int> { chosenState },
                    cellToCollapse.IsBoundary
                );

                // Propagate to neighbors - this requires a proper propagation handler
                if (wfcGenerator != null)
                {
                    wfcGenerator.AddPropagationEvent(evt);
                }
                else
                {
                    // Local propagation for standalone usage
                    PropagateCollapse(cellToCollapse, chunk);
                }

                return true;
            }

            return false;
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
                    {
                        ApplyConstraintToNeighbor(cell, neighbor, dir, adjacentChunk);
                    }
                }
            }
        }

        // Method to apply constraints to a neighboring cell
        private void ApplyConstraintToNeighbor(Cell source, Cell target, Direction direction, Chunk targetChunk)
        {
            // Skip if target already collapsed
            if (target.CollapsedState.HasValue || source.CollapsedState == null)
                return;

            // Store old states for propagation
            HashSet<int> oldStates = new HashSet<int>(target.PossibleStates);

            // Find compatible states based on adjacency rules
            HashSet<int> compatibleStates = new HashSet<int>();

            foreach (int targetState in target.PossibleStates)
            {
                // Check if this state can be adjacent to the source state
                // requires access to adjacency rules which would be in WFCGenerator
                bool isCompatible = true;

                if (wfcGenerator != null)
                {
                    isCompatible = wfcGenerator.AreStatesCompatible(targetState, source.CollapsedState.Value, direction);
                }
                else
                {
                    // Simple compatibility rule: states can be adjacent to themselves and neighbors
                    isCompatible = Mathf.Abs(targetState - source.CollapsedState.Value) <= 1;
                }

                if (isCompatible)
                {
                    compatibleStates.Add(targetState);
                }
            }

            // Update target's possible states if needed
            if (compatibleStates.Count > 0 && !compatibleStates.SetEquals(oldStates))
            {
                bool changed = target.SetPossibleStates(compatibleStates);

                // If changed, propagate further
                if (changed)
                {
                    PropagateCollapse(target, targetChunk);
                }
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
                {
                    meshGenerator = FindObjectOfType<MeshGenerator>();
                }

                if (meshGenerator != null)
                {
                    // Call the mesh generator to create or update the chunk mesh
                    meshGenerator.GenerateChunkMesh(chunk.Position, chunk);
                }
                else
                {
                    // Create the mesh directly as a fallback
                    GenerateFallbackMesh(chunk);
                }

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
                {
                    state.LastError = e.Message;
                }

                UpdateChunkState(chunk.Position, ChunkLifecycleState.Error);
                throw; // Re-throw to allow the task processing system to handle it
            }
            finally
            {
                if (performanceMonitor != null)
                    performanceMonitor.EndComponentTiming("MeshGeneration");
            }
        }

        // Fallback mesh generation method
        private void GenerateFallbackMesh(Chunk chunk)
        {
            // Fallback - create a simple mesh directly
            // Step 1: Create a density field generator
            var densityGenerator = new WFC.MarchingCubes.DensityFieldGenerator();

            // Step 2: Generate a density field from the chunk
            float[,,] densityField = densityGenerator.GenerateDensityField(chunk);

            // Step 3: Create a marching cubes generator
            var marchingCubes = new WFC.MarchingCubes.MarchingCubesGenerator();

            // Step 4: Generate mesh using appropriate LOD level
            Mesh mesh = marchingCubes.GenerateMesh(densityField, chunk.LODLevel);

            // Step 5: Create or update the GameObject for this chunk
            string chunkName = $"Terrain_Chunk_{chunk.Position.x}_{chunk.Position.y}_{chunk.Position.z}";
            GameObject meshObject = GameObject.Find(chunkName);

            if (meshObject == null)
            {
                meshObject = new GameObject(chunkName);
                meshObject.transform.position = GetChunkWorldPosition(chunk.Position);
                meshObject.AddComponent<MeshFilter>();
                meshObject.AddComponent<MeshRenderer>();

                // Parent to this object
                meshObject.transform.parent = transform;
            }

            // Step 6: Assign the mesh and material
            MeshFilter meshFilter = meshObject.GetComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            // Find and assign a material
            MeshRenderer meshRenderer = meshObject.GetComponent<MeshRenderer>();

            Material terrainMat = null;

            // Try to get materials from WFCGenerator
            if (wfcGenerator != null)
            {
                Material[] stateMaterials = wfcGenerator.GetStateMaterials();

                if (stateMaterials != null && stateMaterials.Length > 0)
                {
                    int dominantState = GetDominantState(chunk);

                    if (dominantState >= 0 && dominantState < stateMaterials.Length)
                    {
                        terrainMat = stateMaterials[dominantState];
                    }
                }
            }

            // If no material found, try resources or create default
            if (terrainMat == null)
            {
                terrainMat = Resources.Load<Material>("TerrainMaterial");

                if (terrainMat == null)
                {
                    // Fallback to a default material
                    terrainMat = new Material(Shader.Find("Standard"));
                    terrainMat.color = new Color(0.5f, 0.5f, 0.5f);
                }
            }

            meshRenderer.material = terrainMat;
        }

        // Helper method to determine the dominant state in a chunk
        private int GetDominantState(Chunk chunk)
        {
            if (chunk == null) return 1; // Default to ground

            Dictionary<int, int> stateCounts = new Dictionary<int, int>();
            int totalCells = 0;

            // Sample cells to estimate state distribution
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

        #endregion

        #region Helper Methods

        /*
         * ApplyInitialConstraints
         * ----------------------------------------------------------------------------
         * Applies initial terrain constraints to a newly created chunk.
         * 
         * This comprehensive terrain initialization:
         * 1. Generates noise-based heightmap using multiple octaves:
         *    - Base noise for overall elevation
         *    - Detail noise for small variations
         *    - Feature noise for medium-scale terrain features
         * 2. Determines water level from separate noise function
         * 3. Creates terrain height and material variations:
         *    - Deep underground: solid ground
         *    - Near surface: mix of ground and occasional rock
         *    - Surface layer: varied terrain types (grass, sand, rock) based on moisture
         *    - Above surface: occasional trees in suitable areas
         * 4. Smooths chunk boundaries for seamless transitions
         * 
         * This initial terrain provides a foundational structure that the WFC algorithm
         * then refines through constraint propagation, creating more detailed and
         * coherent terrain features.
         * 
         * Parameters:
         * - chunk: The newly created chunk to initialize
         * - random: Seeded random generator for deterministic generation
         */
        private void ApplyInitialConstraints(Chunk chunk, System.Random random)
        {
            // Constants that control terrain generation characteristics
            float baseHeightScale = 0.01f;    // Controls how gradually height changes (smaller = more gradual)
            float detailScale = 0.05f;        // Controls small terrain details
            float featureScale = 0.025f;      // Controls medium terrain features
            float materialNoiseScale = 0.08f; // Controls how materials are distributed

            // Base ground level - adjust as needed for your world
            int baseGroundLevel = chunk.Size / 2;

            // Noise offsets to create different patterns
            float heightOffset = 0;
            float detailOffset = 500;
            float materialOffset = 1000;
            float featureOffset = 2000;
            float waterOffset = 3000;

            // Initialize terrain heights across the chunk
            float[,] heightMap = new float[chunk.Size, chunk.Size];
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            // Step 1: Generate base height map using global coordinates for consistency across chunks
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    // Calculate world coordinates for consistent noise sampling
                    float worldX = (chunk.Position.x * chunk.Size + x);
                    float worldZ = (chunk.Position.z * chunk.Size + z);

                    // Sample multiple noise octaves for natural-looking terrain
                    float baseNoise = Mathf.PerlinNoise(
                        worldX * baseHeightScale + heightOffset,
                        worldZ * baseHeightScale + heightOffset
                    );

                    float detailNoise = Mathf.PerlinNoise(
                        worldX * detailScale + detailOffset,
                        worldZ * detailScale + detailOffset
                    ) * 0.25f; // Reduced strength for detail noise

                    float featureNoise = Mathf.PerlinNoise(
                        worldX * featureScale + featureOffset,
                        worldZ * featureScale + featureOffset
                    ) * 0.5f; // Medium strength for feature noise

                    // Combine noise for final height
                    float combinedNoise = baseNoise * 0.6f + detailNoise + featureNoise * 0.4f;

                    // Store normalized height (0-1 range)
                    heightMap[x, z] = combinedNoise;

                    // Track min and max heights for normalization
                    minHeight = Mathf.Min(minHeight, combinedNoise);
                    maxHeight = Mathf.Max(maxHeight, combinedNoise);
                }
            }

            // Step 2: Normalize heights and convert to actual terrain heights
            float heightRange = maxHeight - minHeight;
            if (heightRange < 0.001f) heightRange = 1.0f; // Avoid division by zero

            int[,] terrainHeight = new int[chunk.Size, chunk.Size];
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    // Normalize to 0-1 range
                    float normalizedHeight = (heightMap[x, z] - minHeight) / heightRange;

                    // Apply curve to create more flat areas and steeper mountains
                    normalizedHeight = Mathf.Pow(normalizedHeight, 1.5f);

                    // Convert to actual height in cells
                    int height = Mathf.FloorToInt(baseGroundLevel + normalizedHeight * chunk.Size * 0.6f);

                    // Ensure height is in bounds
                    terrainHeight[x, z] = Mathf.Clamp(height, 1, chunk.Size - 1);
                }
            }

            // Step 3: Apply water level determination - consistent across chunks
            float waterNoise = Mathf.PerlinNoise(
                chunk.Position.x * baseHeightScale * 5 + waterOffset,
                chunk.Position.z * baseHeightScale * 5 + waterOffset
            );

            int waterLevel = (waterNoise > 0.6f) ?
                baseGroundLevel - Mathf.FloorToInt((waterNoise - 0.6f) * 10) :
                -1; // No water

            // Step 4: Assign states to cells based on terrain height and material patterns
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    // Get world coordinates for material selection
                    float worldX = (chunk.Position.x * chunk.Size + x);
                    float worldZ = (chunk.Position.z * chunk.Size + z);

                    // Material variation noise
                    float materialNoise = Mathf.PerlinNoise(
                        worldX * materialNoiseScale + materialOffset,
                        worldZ * materialNoiseScale + materialOffset
                    );

                    // Additional noise for specific features
                    float specialFeatureNoise = Mathf.PerlinNoise(
                        worldX * (materialNoiseScale * 2) + featureOffset,
                        worldZ * (materialNoiseScale * 2) + featureOffset
                    );

                    // Get target height for this column
                    int height = terrainHeight[x, z];

                    // Iterate through all cells in this vertical column
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);
                        if (cell == null) continue;

                        // Water layer check - must happen first
                        if (waterLevel > 0 && y <= waterLevel)
                        {
                            cell.Collapse(3); // Water state
                            continue;
                        }

                        // Deep underground - always solid ground
                        if (y < height - 3)
                        {
                            cell.Collapse(1); // Ground state
                        }
                        // Underground near surface
                        else if (y < height - 1)
                        {
                            // Some rock veins underground
                            if (materialNoise > 0.7f)
                            {
                                cell.Collapse(4); // Rock state
                            }
                            else
                            {
                                cell.Collapse(1); // Ground state
                            }
                        }
                        // Surface layer
                        else if (y == height - 1)
                        {
                            // Create varied surface types
                            if (waterLevel > 0 && y <= waterLevel + 1)
                            {
                                // Near water - create beaches
                                cell.Collapse(5); // Sand
                            }
                            else if (materialNoise < 0.3f)
                            {
                                // Sandy areas
                                cell.Collapse(5); // Sand
                            }
                            else if (materialNoise < 0.7f)
                            {
                                // Grassy areas (most common)
                                cell.Collapse(2); // Grass
                            }
                            else if (materialNoise < 0.9f)
                            {
                                // Rocky outcrops
                                cell.Collapse(4); // Rock
                            }
                            else
                            {
                                // Occasional dirt patches
                                cell.Collapse(1); // Ground
                            }
                        }
                        // Surface features
                        else if (y == height)
                        {
                            // Only add features in non-water areas
                            if (waterLevel < 0 || y > waterLevel)
                            {
                                // Occasionally add trees - more likely in grassy areas
                                if (specialFeatureNoise > 0.8f && materialNoise > 0.4f && materialNoise < 0.8f)
                                {
                                    cell.Collapse(6); // Tree
                                }
                                else
                                {
                                    cell.Collapse(0); // Air
                                }
                            }
                            else
                            {
                                cell.Collapse(3); // Water near surface
                            }
                        }
                        // Above surface - air
                        else
                        {
                            cell.Collapse(0); // Air
                        }
                    }
                }
            }

            // Step 5: Smooth edges at chunk boundaries
            SmoothChunkBoundaries(chunk, terrainHeight);
        }

        // Method to smooth chunk boundaries with neighbors
        private void SmoothChunkBoundaries(Chunk chunk, int[,] terrainHeight)
        {
            // Check each direction for neighbors
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                // Skip if no neighbor in this direction
                if (!chunk.Neighbors.TryGetValue(dir, out Chunk neighbor))
                    continue;

                // We only need to smooth X and Z boundaries for height
                if (dir != Direction.Left && dir != Direction.Right &&
                    dir != Direction.Forward && dir != Direction.Back)
                    continue;

                // Process cells at the boundary
                switch (dir)
                {
                    case Direction.Left: // X = 0 boundary
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            // Make boundary heights match with some random variation
                            int thisHeight = terrainHeight[0, z];
                            Cell boundaryCell = chunk.GetCell(0, thisHeight, z);

                            // See if there's already a cell from the neighboring chunk we should match
                            Cell neighborCell = GetNeighborBoundaryCell(chunk, neighbor, dir, z);

                            if (neighborCell != null && neighborCell.CollapsedState.HasValue)
                            {
                                // Match state with neighbor for consistency
                                if (boundaryCell != null)
                                {
                                    boundaryCell.Collapse(neighborCell.CollapsedState.Value);
                                }
                            }
                        }
                        break;

                    case Direction.Right: // X = Size-1 boundary
                        // Similar logic for right boundary
                        break;

                    case Direction.Back: // Z = 0 boundary
                        // Similar logic for back boundary
                        break;

                    case Direction.Forward: // Z = Size-1 boundary
                        // Similar logic for forward boundary
                        break;
                }
            }
        }

        // Helper method to get the boundary cell from a neighbor chunk
        private Cell GetNeighborBoundaryCell(Chunk chunk, Chunk neighbor, Direction dir, int index)
        {
            // Convert boundary position to neighbor's coordinate system
            switch (dir)
            {
                case Direction.Left:
                    return neighbor.GetCell(neighbor.Size - 1, 0, index); // Right edge of neighbor

                case Direction.Right:
                    return neighbor.GetCell(0, 0, index); // Left edge of neighbor

                case Direction.Down:
                    return neighbor.GetCell(index, neighbor.Size - 1, 0); // Top edge of neighbor

                case Direction.Up:
                    return neighbor.GetCell(index, 0, 0); // Bottom edge of neighbor

                case Direction.Back:
                    return neighbor.GetCell(index, 0, neighbor.Size - 1); // Front edge of neighbor

                case Direction.Forward:
                    return neighbor.GetCell(index, 0, 0); // Back edge of neighbor

                default:
                    return null;
            }
        }

        // Method to connect chunk neighbors
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

                // Create buffer cells to mirror neighbor
                List<Cell> bufferCells = new List<Cell>();
                for (int i = 0; i < boundaryCells.Count; i++)
                {
                    Cell bufferCell = new Cell(new Vector3Int(-1, -1, -1), new int[0]);
                    bufferCells.Add(bufferCell);
                }
                buffer.BufferCells = bufferCells;

                // Add to chunk's boundary buffers
                chunk.BoundaryBuffers[dir] = buffer;
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

            // Handle changes that require updates
            if (oldLoadDistance != LoadDistance || oldUnloadDistance != UnloadDistance)
            {
                Debug.Log("ChunkManager: Load/unload distances updated. Refreshing chunk management.");
            }

            if (oldChunkSize != ChunkSize)
            {
                Debug.LogWarning("ChunkManager: Chunk size changed. This will require world regeneration.");
            }
        }

        /// <summary>
        /// Reset the chunk system completely
        /// </summary>
        public void ResetChunkSystem()
        {
            // Stop processing while we reset
            StopAllCoroutines();

            // Clear the task queue
            while (chunkTasks.Count > 0)
                chunkTasks.Dequeue();

            // Remove all mesh objects
            foreach (var chunkPos in loadedChunks.Keys.ToList())
            {
                RemoveChunkMeshObject(chunkPos);
            }

            // Clear chunk collections
            loadedChunks.Clear();
            chunkStates.Clear();

            Debug.Log("Chunk system reset complete");

            // Restart
            StartCoroutine(AdaptiveStrategyUpdateCoroutine());
            ForceGenerateInitialChunks();
        }

        /// <summary>
        /// Force creation of a chunk at a specific position
        /// </summary>
        public void CreateChunkAt(Vector3Int position)
        {
            // Force position to be at ground level
            position.y = 0;

            // Skip if already loaded or in the process of loading
            ChunkLifecycleState currentState = GetChunkState(position);
            if (currentState != ChunkLifecycleState.None &&
                currentState != ChunkLifecycleState.Error)
            {
                Debug.Log($"Chunk at {position} is already being handled (state: {currentState})");
                return;
            }

            // Create the chunk with high priority
            ChunkTask task = new ChunkTask
            {
                Type = ChunkTaskType.Create,
                Position = position,
                Priority = 100f // High priority
            };

            chunkTasks.Enqueue(task, task.Priority);
            UpdateChunkState(position, ChunkLifecycleState.Pending);

            Debug.Log($"Forced creation of chunk at {position}");
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
            {
                Debug.Log($"Already generating mesh for chunk at {position}");
                return;
            }

            // Queue the task
            ChunkTask task = new ChunkTask
            {
                Type = ChunkTaskType.GenerateMesh,
                Chunk = chunk,
                Priority = 100f // High priority
            };

            chunkTasks.Enqueue(task, task.Priority);
            Debug.Log($"Queued mesh generation for chunk at {position}");
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

        /*
        * CreateChunksAroundPlayer
        * ----------------------------------------------------------------------------
        * Initiates chunk generation in the vicinity of the player/viewer.
        * 
        * Process:
        * 1. Determines the chunk coordinate at the viewer's current position
        * 2. Directly creates the chunk the player is standing in with high priority
        * 3. Starts a coroutine for gradual creation of surrounding chunks to avoid
        *    frame rate drops from creating too many chunks at once
        * 4. Updates the last chunk generation position for incremental checks
        * 
        * This function is called both during initialization and whenever the player
        * moves significantly from the last chunk generation position, ensuring
        * terrain is always available ahead of the player's movement.
        * 
        * The gradual creation through coroutines helps maintain performance
        * even when many chunks need to be created at once.
        */
        public void CreateChunksAroundPlayer()
        {
            Vector3Int viewerChunk = new Vector3Int(
                Mathf.FloorToInt(viewer.position.x / ChunkSize),
                0,
                //Mathf.FloorToInt(viewer.position.y / ChunkSize),
                Mathf.FloorToInt(viewer.position.z / ChunkSize)
            );

            Debug.Log($"Creating chunks around player at chunk {viewerChunk}");

            // Start with just creating the immediate chunk first
            CreateChunkAt(viewerChunk);

            // Then queue others to be created over time
            StartCoroutine(GradualChunkCreation(viewerChunk));
        }

        // Coroutine to create chunks gradually
        private IEnumerator GradualChunkCreation(Vector3Int centerChunk)
        {
            for (int x = -2; x <= 2; x++)
            {
               int y = 0; // Only create in the same Y plane for now
                          //for (int y = -2; y <= 2; y++)
                          //{
                for (int z = -2; z <= 2; z++)
                    {
                        // Skip the center chunk - already created
                        if (x == 0 && y == 0 && z == 0) continue;

                        Vector3Int chunkPos = new Vector3Int(
                                   centerChunk.x + x,
                                   y, // Keep at y=0
                                   centerChunk.z + z
                               ); 
                        CreateChunkAt(chunkPos);

                        // Wait a frame between chunks to prevent freezing
                        yield return null;
                }
            }
        }
    }
}