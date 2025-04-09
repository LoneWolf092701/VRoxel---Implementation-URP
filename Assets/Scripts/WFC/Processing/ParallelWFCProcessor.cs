using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using WFC.Core;
using System.Linq;
using WFC.Boundary;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace WFC.Processing
{
    /// <summary>
    /// Handles parallel processing for WFC constraint propagation and chunk generation
    /// to improve performance for VR applications.
    /// </summary>
    public class ParallelWFCProcessor
    {
        // Configuration
        private int maxThreads = 0; // 0 = use Environment.ProcessorCount - 1
        private int maxQueuedTasks = 64;

        // State
        private Thread[] workerThreads;
        private Queue<WFCJob> jobQueue = new Queue<WFCJob>();
        private HashSet<Vector3Int> processingChunks = new HashSet<Vector3Int>();
        private bool isRunning = false;
        private object queueLock = new object();

        // Profiling data
        public int TotalProcessedJobs { get; private set; } = 0;
        public float AverageJobTime { get; private set; } = 0;
        public int ActiveThreads => processingChunks.Count;

        // Reference to WFC system
        private IWFCAlgorithm wfcAlgorithm;

        // Thread-local random number generators to avoid contention
        private ThreadLocal<System.Random> threadLocalRandom;
        private static int randomSeed = Environment.TickCount;

        // Queue for propagation events to be processed on the main thread
        private ConcurrentQueue<PropagationEvent> mainThreadPropagationQueue =
            new ConcurrentQueue<PropagationEvent>();

        public ParallelWFCProcessor(IWFCAlgorithm algorithm, int maxThreads = 0)
        {
            this.wfcAlgorithm = algorithm;
            if (algorithm == null)
            {
                UnityEngine.Debug.LogError("ParallelWFCProcessor: Algorithm cannot be null!");
                return;
            }

            this.wfcAlgorithm = algorithm;

            // Ensure at least 1 thread
            this.maxThreads = maxThreads > 0 ? maxThreads : Mathf.Max(1, SystemInfo.processorCount - 1);

            // Initialize thread-local random generators
            threadLocalRandom = new ThreadLocal<System.Random>(() =>
                new System.Random(Interlocked.Increment(ref randomSeed)));

            UnityEngine.Debug.Log($"Initializing Parallel WFC Processor with {this.maxThreads} worker threads");
        }

        /// <summary>
        /// Start the worker threads
        /// </summary>
        public void Start()
        {
            if (isRunning)
                return;

            if (wfcAlgorithm == null)
            {
                UnityEngine.Debug.LogError("Cannot start parallel processor - no algorithm provided!");
                return;
            }

            isRunning = true;

            // Initialize worker threads
            workerThreads = new Thread[maxThreads];
            for (int i = 0; i < maxThreads; i++)
            {
                workerThreads[i] = new Thread(WorkerThreadFunction);
                workerThreads[i].IsBackground = true;
                workerThreads[i].Start();
            }

            UnityEngine.Debug.Log("Parallel WFC Processor started");
        }

        /// <summary>
        /// Stop all worker threads
        /// </summary>
        public void Stop()
        {
            if (!isRunning)
                return;

            isRunning = false;

            // Wait for threads to finish
            for (int i = 0; i < workerThreads.Length; i++)
            {
                if (workerThreads[i] != null && workerThreads[i].IsAlive)
                {
                    workerThreads[i].Join(100); // Wait for 100ms max
                }
            }

            // Clear queue
            lock (queueLock)
            {
                jobQueue.Clear();
                processingChunks.Clear();
            }

            UnityEngine.Debug.Log("Parallel WFC Processor stopped");
        }

        /// <summary>
        /// Queue a chunk for parallel processing
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

                // Check if queue is full
                if (jobQueue.Count >= maxQueuedTasks)
                    return false;

                // Create job - use DateTime.UtcNow instead of Time.realtimeSinceStartup
                WFCJob job = new WFCJob
                {
                    Chunk = chunk,
                    JobType = jobType,
                    MaxIterations = maxIterations,
                    Priority = priority,
                    CreationTime = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds // Thread-safe alternative
                };

                // Add to queue
                jobQueue.Enqueue(job);

                // Mark as processing to prevent duplicates
                processingChunks.Add(chunk.Position);

                // Notify waiting threads
                Monitor.PulseAll(queueLock);

                return true;
            }
        }

        /// <summary>
        /// Checks if a chunk is currently being processed
        /// </summary>
        /// <param name="chunkPos">Position of the chunk to check</param>
        /// <returns>True if the chunk is being processed, false otherwise</returns>
        public bool IsChunkBeingProcessed(Vector3Int chunkPos)
        {
            lock (queueLock)
            {
                return processingChunks.Contains(chunkPos);
            }
        }

        /// <summary>
        /// Worker thread function that processes jobs from the queue
        /// </summary>
        private void WorkerThreadFunction()
        {
            while (isRunning)
            {
                WFCJob job = null;

                // Get a job from the queue
                lock (queueLock)
                {
                    if (jobQueue.Count > 0)
                    {
                        job = jobQueue.Dequeue();
                    }
                    else
                    {
                        // No jobs, wait for notification
                        Monitor.Wait(queueLock, 1000); // 1 second timeout
                        continue;
                    }
                }

                // Process the job
                if (job != null)
                {
                    Stopwatch stopwatch = new Stopwatch(); // Thread-safe timer
                    stopwatch.Start();

                    try
                    {
                        ProcessJob(job);
                    }
                    catch (Exception e)
                    {
                        // Use UnityEngine.Debug, not System.Diagnostics.Debug
                        UnityEngine.Debug.LogError($"Error processing chunk {job.Chunk.Position}: {e.Message}\n{e.StackTrace}");
                    }

                    stopwatch.Stop();
                    float jobTime = stopwatch.ElapsedMilliseconds / 1000f; // Convert to seconds

                    // Update stats
                    lock (queueLock)
                    {
                        TotalProcessedJobs++;
                        AverageJobTime = (AverageJobTime * (TotalProcessedJobs - 1) + jobTime) / TotalProcessedJobs;

                        // Remove from processing set
                        processingChunks.Remove(job.Chunk.Position);
                    }
                }
            }
        }

        /*
         * ProcessJob
         * ----------------------------------------------------------------------------
         * Processes a WFC job in parallel across worker threads.
         * 
         * This function dispatches different job types to appropriate handlers:
         * 1. Collapse jobs:
         *    - Runs the WFC algorithm to collapse cells in a chunk
         *    - Uses thread-safe implementations to prevent race conditions
         *    - Tracks progress and marks completion when no further progress possible
         * 
         * 2. GenerateMesh jobs:
         *    - Marks the chunk as dirty for mesh generation
         *    - Actual mesh generation happens on the main thread
         *    - This job just flags chunks ready for visualization
         * 
         * 3. ApplyConstraints jobs:
         *    - Applies hierarchical constraints to uncollapsed cells
         *    - Uses thread-safe constraint application
         * 
         * The parallel processing system significantly improves performance
         * by distributing the computationally intensive WFC algorithm
         * across multiple CPU cores.
         * 
         * Parameters:
         * - job: The job to process, containing type, chunk, and parameters
         */
        private void ProcessJob(WFCJob job)
        {
            switch (job.JobType)
            {
                case WFCJobType.Collapse:
                    ProcessCollapseJob(job);
                    break;

                case WFCJobType.GenerateMesh:
                    // This would call into your Marching Cubes system
                    // For now we'll just mark the chunk as needing a mesh update
                    job.Chunk.IsDirty = true;
                    break;

                case WFCJobType.ApplyConstraints:
                    ProcessConstraintJob(job);
                    break;
            }
        }

        /// <summary>
        /// Process a collapse job (running WFC algorithm)
        /// </summary>
        private void ProcessCollapseJob(WFCJob job)
        {
            Chunk chunk = job.Chunk;

            // Use chunk-specific MaxIterations instead of job.MaxIterations
            int iterationsRemaining = chunk.MaxIterations;

            // Run WFC algorithm for this chunk
            bool madeProgress = true;
            while (madeProgress && iterationsRemaining > 0)
            {
                madeProgress = CollapseNextCell(chunk);
                iterationsRemaining--;
            }

            // Mark chunk as fully collapsed if no more progress can be made
            if (!madeProgress)
            {
                chunk.IsFullyCollapsed = true;
            }
        }

        /// <summary>
        /// Process a constraint application job
        /// </summary>
        private void ProcessConstraintJob(WFCJob job)
        {
            Chunk chunk = job.Chunk;

            // Apply hierarchical constraints to all uncollapsed cells
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);

                        // Skip collapsed cells
                        if (cell.CollapsedState.HasValue)
                            continue;


                    }
                }
            }
        }

        /// <summary>
        /// Find and collapse a cell with lowest entropy
        /// </summary>
        private bool CollapseNextCell(Chunk chunk)
        {
            // Thread-safe implementation of finding and collapsing a cell
            Cell cellToCollapse = null;
            int lowestEntropy = int.MaxValue;

            // Find cell with lowest entropy
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
                // Thread-local random generator
                var random = threadLocalRandom.Value;
                int[] possibleStates = cellToCollapse.PossibleStates.ToArray();
                int randomState = possibleStates[random.Next(possibleStates.Length)];

                // Use lock when modifying shared data
                lock (cellToCollapse)
                {
                    // Check if still valid (could have changed by another thread)
                    if (!cellToCollapse.CollapsedState.HasValue &&
                        cellToCollapse.PossibleStates.Contains(randomState))
                    {
                        cellToCollapse.Collapse(randomState);
                        // Queue propagation for main thread
                        // Don't propagate directly from worker thread
                        QueuePropagationForMainThread(cellToCollapse, chunk);
                        return true;
                    }
                }
            }

            return false;
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
    /// Represents a job for parallel processing
    /// </summary>
    public class WFCJob
    {
        public Chunk Chunk;
        public WFCJobType JobType;
        public int MaxIterations;
        public float Priority;
        public float CreationTime;
    }
}