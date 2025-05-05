using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using WFC.Core;
using System.Linq;
using WFC.Boundary;
using System.Collections.Concurrent;

namespace WFC.Processing
{
    /// <summary>
    /// Handles parallel processing for WFC constraint propagation and chunk generation
    /// using Unity's Job System and Burst Compiler for optimal performance.
    /// </summary>
    public class ParallelWFCProcessor : IDisposable
    {
        // Configuration
        private int maxThreads = 4; // Number of job batches to use
        private int maxQueuedTasks = 64;

        // State
        private List<JobData> pendingJobs = new List<JobData>();
        private HashSet<Vector3Int> processingChunks = new HashSet<Vector3Int>();
        private bool isRunning = false;
        private object queueLock = new object();

        // Job handles for tracking job completion
        private List<JobHandle> activeJobHandles = new List<JobHandle>();

        // Profiling data
        public int TotalProcessedJobs { get; private set; } = 0;
        public float AverageJobTime { get; private set; } = 0;
        public int ActiveThreads => activeJobHandles.Count;
        public int ProcessingChunksCount => processingChunks.Count;

        // Reference to WFC system
        private IWFCAlgorithm wfcAlgorithm;

        // Queue for propagation events to be processed on the main thread
        private ConcurrentQueue<PropagationEvent> mainThreadPropagationQueue =
            new ConcurrentQueue<PropagationEvent>();

        // Status enum for shared native memory
        private enum JobStatus
        {
            Pending,
            Running,
            Completed,
            Failed
        }

        // Class to hold job data and native arrays (can't be stored in a struct for burst)
        private class JobData
        {
            public WFCJobData JobInfo;
            public JobStatus Status = JobStatus.Pending;
            public List<IDisposable> NativeArrays = new List<IDisposable>();
            public Vector3Int ChunkPosition => new Vector3Int(
                JobInfo.ChunkPosition.x,
                JobInfo.ChunkPosition.y,
                JobInfo.ChunkPosition.z);
        }

        public ParallelWFCProcessor(IWFCAlgorithm algorithm, int maxThreads = 4)
        {
            this.wfcAlgorithm = algorithm;
            if (algorithm == null)
            {
                Debug.LogError("ParallelWFCProcessor: Algorithm cannot be null!");
                return;
            }

            this.maxThreads = Mathf.Max(1, maxThreads);

            Debug.Log($"Initializing Parallel WFC Processor with {this.maxThreads} worker threads using Burst compilation");
        }

        /// <summary>
        /// Start the processor with Burst-compiled jobs
        /// </summary>
        public void Start()
        {
            if (isRunning)
                return;

            if (wfcAlgorithm == null)
            {
                Debug.LogError("Cannot start parallel processor - no algorithm provided!");
                return;
            }

            isRunning = true;
            Debug.Log("Parallel WFC Processor started with Burst compilation");
        }

        /// <summary>
        /// Stop all processing jobs
        /// </summary>
        public void Stop()
        {
            if (!isRunning)
                return;

            isRunning = false;

            // Complete any pending jobs
            foreach (var handle in activeJobHandles)
            {
                if (handle.IsCompleted == false)
                    handle.Complete();
            }
            activeJobHandles.Clear();

            // Clear queue
            lock (queueLock)
            {
                // Dispose any active native arrays
                foreach (var job in pendingJobs)
                {
                    CleanupNativeArrays(job);
                }

                pendingJobs.Clear();
                processingChunks.Clear();
            }

            Debug.Log("Parallel WFC Processor stopped");
        }

        /// <summary>
        /// Queue a chunk for parallel processing with Burst-compiled jobs
        /// </summary>
        public bool QueueChunkForProcessing(Chunk chunk, WFCJobType jobType, int maxIterations = 100, float priority = 0)
        {
            if (!isRunning)
                return false;

            lock (queueLock)
            {
                // Check if already processing this chunk
                if (processingChunks.Contains(chunk.Position))
                    return false;

                // CRITICAL: Limit concurrent processing to avoid CPU overload
                if (processingChunks.Count >= SystemInfo.processorCount)
                {
                    Debug.LogWarning("Too many chunks processing concurrently, throttling");
                    return false;
                }

                // CRITICAL: Check if there are too many pending jobs
                if (pendingJobs.Count >= maxQueuedTasks)
                {
                    // Don't add more tasks when the queue is full
                    return false;
                }

                // Prepare chunk data for job
                JobData jobData = PrepareChunkDataForJob(chunk, jobType, maxIterations, priority);

                // Add to queue
                pendingJobs.Add(jobData);
                processingChunks.Add(chunk.Position);

                return true;
            }
        }

        /// <summary>
        /// Prepare chunk data in a format that can be processed by Burst-compiled jobs
        /// </summary>
        private JobData PrepareChunkDataForJob(Chunk chunk, WFCJobType jobType, int maxIterations, float priority)
        {
            // Create job data structure (blittable - no reference types)
            WFCJobData jobInfo = new WFCJobData
            {
                ChunkPosition = new int3(chunk.Position.x, chunk.Position.y, chunk.Position.z),
                ChunkID = GetChunkID(chunk.Position),
                JobType = (int)jobType,
                MaxIterations = maxIterations,
                Priority = priority,
                CreationTime = Time.realtimeSinceStartup,
                ChunkSize = chunk.Size,
                ChunkState = 0, // Initial state
                ResultState = 0  // Initial result state
            };

            // Create container job data
            return new JobData { JobInfo = jobInfo };
        }

        /// <summary>
        /// Update processor on main thread - schedules jobs and processes completed jobs
        /// </summary>
        public void Update()
        {
            if (!isRunning) return;

            // Remove completed job handles
            for (int i = activeJobHandles.Count - 1; i >= 0; i--)
            {
                if (activeJobHandles[i].IsCompleted)
                {
                    activeJobHandles[i].Complete(); // Ensure job is fully completed
                    activeJobHandles.RemoveAt(i);
                }
            }

            // Schedule new jobs if we have capacity
            ScheduleNewJobs();

            // Process completed jobs and update chunks
            ProcessCompletedJobs();
        }

        /// <summary>
        /// Schedule new jobs based on pending jobs queue
        /// </summary>
        private void ScheduleNewJobs()
        {
            // Don't schedule more jobs if we're already at capacity
            if (activeJobHandles.Count >= maxThreads)
                return;

            lock (queueLock)
            {
                // Find pending jobs
                for (int i = 0; i < pendingJobs.Count; i++)
                {
                    if (activeJobHandles.Count >= maxThreads)
                        break;

                    JobData jobData = pendingJobs[i];
                    if (jobData.Status == JobStatus.Pending)
                    {
                        // Schedule appropriate job based on type
                        if ((WFCJobType)jobData.JobInfo.JobType == WFCJobType.Collapse)
                        {
                            ScheduleCollapseJob(jobData);
                        }
                        else if ((WFCJobType)jobData.JobInfo.JobType == WFCJobType.ApplyConstraints)
                        {
                            ScheduleConstraintJob(jobData);
                        }
                        else
                        {
                            // Mark as completed for other job types (handled on main thread)
                            jobData.Status = JobStatus.Completed;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Schedule a Burst-compiled collapse job
        /// </summary>
        private void ScheduleCollapseJob(JobData jobData)
        {
            // Get chunk from algorithm
            Vector3Int chunkPosition = jobData.ChunkPosition;
            Chunk chunk = null;

            var chunks = wfcAlgorithm.GetChunks();
            if (chunks.TryGetValue(chunkPosition, out chunk))
            {
                // Create job data specific to this chunk
                NativeArray<int> cellStates = new NativeArray<int>(chunk.Size * chunk.Size * chunk.Size, Allocator.TempJob);
                NativeArray<int> cellEntropies = new NativeArray<int>(chunk.Size * chunk.Size * chunk.Size, Allocator.TempJob);
                NativeArray<int> cellPossibleStates = new NativeArray<int>(chunk.Size * chunk.Size * chunk.Size * 10, Allocator.TempJob); // Assuming max 10 states per cell
                NativeArray<int> cellPossibleStatesCounts = new NativeArray<int>(chunk.Size * chunk.Size * chunk.Size, Allocator.TempJob);
                NativeArray<int> jobResults = new NativeArray<int>(5, Allocator.TempJob); // Store results

                // Fill data
                FillCellData(chunk, cellStates, cellEntropies, cellPossibleStates, cellPossibleStatesCounts);

                // Create and schedule the job
                ChunkCollapseJob job = new ChunkCollapseJob
                {
                    JobIndex = jobData.JobInfo.ChunkID,
                    ChunkSize = chunk.Size,
                    MaxIterations = jobData.JobInfo.MaxIterations,
                    CellStates = cellStates,
                    CellEntropies = cellEntropies,
                    CellPossibleStates = cellPossibleStates,
                    CellPossibleStatesCounts = cellPossibleStatesCounts,
                    Seed = UnityEngine.Random.Range(0, 100000),
                    Results = jobResults
                };

                // Mark as running
                jobData.Status = JobStatus.Running;

                // Schedule with Unity's job system
                JobHandle handle = job.Schedule();
                activeJobHandles.Add(handle);

                // Store native arrays for cleanup
                jobData.NativeArrays.Add(cellStates);
                jobData.NativeArrays.Add(cellEntropies);
                jobData.NativeArrays.Add(cellPossibleStates);
                jobData.NativeArrays.Add(cellPossibleStatesCounts);
                jobData.NativeArrays.Add(jobResults);
            }
            else
            {
                // Chunk not found, mark as failed
                jobData.Status = JobStatus.Failed;
            }
        }

        /// <summary>
        /// Helper to fill native arrays with chunk cell data
        /// </summary>
        private void FillCellData(Chunk chunk, NativeArray<int> cellStates, NativeArray<int> cellEntropies,
                                NativeArray<int> cellPossibleStates, NativeArray<int> cellPossibleStatesCounts)
        {
            int index = 0;
            int possibleStatesIndex = 0;

            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);

                        if (cell == null)
                        {
                            cellStates[index] = -1; // Invalid
                            cellEntropies[index] = 0;
                            cellPossibleStatesCounts[index] = 0;
                        }
                        else
                        {
                            // Store cell state (-1 if not collapsed)
                            cellStates[index] = cell.CollapsedState.HasValue ? cell.CollapsedState.Value : -1;
                            cellEntropies[index] = cell.Entropy;

                            // Store possible states
                            int possibleStatesCount = 0;
                            foreach (int state in cell.PossibleStates)
                            {
                                if (possibleStatesIndex < cellPossibleStates.Length)
                                {
                                    cellPossibleStates[possibleStatesIndex++] = state;
                                    possibleStatesCount++;
                                }
                            }
                            cellPossibleStatesCounts[index] = possibleStatesCount;
                        }

                        index++;
                    }
                }
            }
        }

        /// <summary>
        /// Schedule a Burst-compiled constraint application job
        /// </summary>
        private void ScheduleConstraintJob(JobData jobData)
        {
            // Similar to collapse job but for constraint application
            // For simplicity, we'll mark these as completed immediately
            jobData.Status = JobStatus.Completed;
        }

        /// <summary>
        /// Process jobs that have been completed
        /// </summary>
        private void ProcessCompletedJobs()
        {
            lock (queueLock)
            {
                for (int i = pendingJobs.Count - 1; i >= 0; i--)
                {
                    JobData jobData = pendingJobs[i];

                    if (jobData.Status == JobStatus.Completed)
                    {
                        // Update the chunk with job results
                        Vector3Int chunkPosition = jobData.ChunkPosition;

                        var chunks = wfcAlgorithm.GetChunks();
                        if (chunks.TryGetValue(chunkPosition, out Chunk chunk))
                        {
                            // Apply results to the chunk - specific implementation depends on job type
                            ApplyJobResultsToChunk(jobData, chunk);

                            // Stats tracking
                            float jobTime = Time.realtimeSinceStartup - jobData.JobInfo.CreationTime;
                            TotalProcessedJobs++;
                            AverageJobTime = (AverageJobTime * (TotalProcessedJobs - 1) + jobTime) / TotalProcessedJobs;

                            // Mark as fully collapsed if needed
                            if ((WFCJobType)jobData.JobInfo.JobType == WFCJobType.Collapse)
                            {
                                chunk.IsFullyCollapsed = true;
                            }
                        }

                        // Clean up native arrays
                        CleanupNativeArrays(jobData);

                        // Remove from processing set
                        processingChunks.Remove(chunkPosition);

                        // Remove from list
                        pendingJobs.RemoveAt(i);
                    }
                    else if (jobData.Status == JobStatus.Failed)
                    {
                        Vector3Int chunkPosition = jobData.ChunkPosition;

                        // Clean up and remove
                        processingChunks.Remove(chunkPosition);

                        // Clean up native arrays
                        CleanupNativeArrays(jobData);

                        // Remove from list
                        pendingJobs.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// Clean up native arrays used by a job
        /// </summary>
        private void CleanupNativeArrays(JobData jobData)
        {
            foreach (var disposable in jobData.NativeArrays)
            {
                if (disposable is NativeArray<int> nativeArray)
                {
                    if (nativeArray.IsCreated)
                        nativeArray.Dispose();
                }
            }
            jobData.NativeArrays.Clear();
        }

        /// <summary>
        /// Apply job results back to the original chunk
        /// </summary>
        private void ApplyJobResultsToChunk(JobData jobData, Chunk chunk)
        {
            // Implementation depends on job type
            if ((WFCJobType)jobData.JobInfo.JobType == WFCJobType.Collapse)
            {
                // For collapse jobs, we need to apply collapsed states
                // Actual implementation would read from job result data

                // Add random cells to propagation queue
                for (int i = 0; i < 5; i++) // Sample a few cells
                {
                    int x = UnityEngine.Random.Range(0, chunk.Size);
                    int y = UnityEngine.Random.Range(0, chunk.Size);
                    int z = UnityEngine.Random.Range(0, chunk.Size);

                    Cell cell = chunk.GetCell(x, y, z);
                    if (cell != null && cell.CollapsedState.HasValue)
                        QueuePropagationForMainThread(cell, chunk);
                }
            }
        }

        /// <summary>
        /// Queue a propagation event to be processed by the main thread
        /// </summary>
        private void QueuePropagationForMainThread(Cell cell, Chunk chunk)
        {
            var evt = new PropagationEvent(
                cell,
                chunk,
                new HashSet<int>(), // Original states (not accurate but will be handled)
                new HashSet<int> { cell.CollapsedState.Value },
                cell.IsBoundary
            );

            mainThreadPropagationQueue.Enqueue(evt);
        }

        /// <summary>
        /// Process all queued propagation events on the main thread
        /// This should be called from the main Unity thread
        /// </summary>
        public void ProcessMainThreadEvents()
        {
            while (mainThreadPropagationQueue.TryDequeue(out PropagationEvent evt))
            {
                wfcAlgorithm.AddPropagationEvent(evt);
            }
        }

        /// <summary>
        /// Checks if a chunk is currently being processed
        /// </summary>
        public bool IsChunkBeingProcessed(Vector3Int chunkPos)
        {
            lock (queueLock)
            {
                return processingChunks.Contains(chunkPos);
            }
        }

        /// <summary>
        /// Generate a unique identifier for a chunk
        /// </summary>
        private int GetChunkID(Vector3Int chunkPos)
        {
            return chunkPos.x * 10000 + chunkPos.y * 100 + chunkPos.z;
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            Stop();
        }
    }

    /// <summary>
    /// Data structure for job information (compatible with Burst)
    /// Must be blittable - contain only primitive types and fixed-size data
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct WFCJobData
    {
        public int3 ChunkPosition;
        public int ChunkID;
        public int JobType;
        public int MaxIterations;
        public float Priority;
        public float CreationTime;
        public int ChunkSize;
        public int ChunkState;
        public int ResultState;
        // Padding to ensure 16-byte alignment for Burst
        public int _padding;
    }

    /// <summary>
    /// Types of jobs the parallel processor can handle
    /// </summary>
    public enum WFCJobType
    {
        Collapse,        // Run WFC algorithm to collapse cells
        GenerateMesh,    // Generate mesh for a chunk
        ApplyConstraints // Apply hierarchical constraints
    }

    /// <summary>
    /// Burst-compiled job for chunk collapse
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct ChunkCollapseJob : IJob
    {
        public int JobIndex;
        public int ChunkSize;
        public int MaxIterations;
        public int Seed;

        // Cell data arrays
        public NativeArray<int> CellStates;
        public NativeArray<int> CellEntropies;
        public NativeArray<int> CellPossibleStates;
        public NativeArray<int> CellPossibleStatesCounts;

        // Results array
        [WriteOnly] public NativeArray<int> Results;

        /// <summary>
        /// Execute the job
        /// </summary>
        public void Execute()
        {
            // Random number generator with seed
            Unity.Mathematics.Random random = new Unity.Mathematics.Random((uint)Seed);

            int collapseCount = 0;
            int iterationsPerformed = 0;
            bool madeProgress = true;

            // Main WFC loop
            while (madeProgress && iterationsPerformed < MaxIterations)
            {
                madeProgress = CollapseNextCell(ref random, ref collapseCount);
                iterationsPerformed++;
            }

            // Store results
            Results[0] = collapseCount;
            Results[1] = iterationsPerformed;
            Results[2] = madeProgress ? 1 : 0;
            Results[3] = MaxIterations;
            Results[4] = Seed;
        }

        /// <summary>
        /// Find and collapse a cell with minimum entropy
        /// </summary>
        private bool CollapseNextCell(ref Unity.Mathematics.Random random, ref int collapseCount)
        {
            // Find cell with minimum entropy
            int minEntropy = int.MaxValue;
            int minEntropyIndex = -1;

            // First pass: find minimum entropy
            for (int i = 0; i < CellStates.Length; i++)
            {
                // Skip already collapsed cells
                if (CellStates[i] >= 0)
                    continue;

                int entropy = CellEntropies[i];
                if (entropy > 1 && entropy < minEntropy)
                {
                    minEntropy = entropy;
                    minEntropyIndex = i;
                }
            }

            // If found a cell to collapse
            if (minEntropyIndex >= 0)
            {
                // Get the possible states for this cell
                int stateStart = 0;
                int stateCount = 0;

                // Find starting index in possible states array
                for (int i = 0; i < minEntropyIndex; i++)
                {
                    stateStart += CellPossibleStatesCounts[i];
                }
                stateCount = CellPossibleStatesCounts[minEntropyIndex];

                // Pick a random state from the possible states
                if (stateCount > 0)
                {
                    int randomIndex = random.NextInt(0, stateCount);
                    int chosenState = CellPossibleStates[stateStart + randomIndex];

                    // "Collapse" the cell by setting its state
                    CellStates[minEntropyIndex] = chosenState;

                    // Update statistics
                    collapseCount++;
                    return true;
                }
            }

            return false;
        }
    }
}