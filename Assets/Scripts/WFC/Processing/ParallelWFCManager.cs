using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using WFC.Core;
using WFC.Generation;
using WFC.Boundary;
using Unity.Profiling;

namespace WFC.Processing
{
    /// <summary>
    /// Manages parallel processing of WFC algorithm with Unity's Burst compiler.
    /// This component acts as a bridge between your ChunkManager and the parallel processor.
    /// </summary>
    public class ParallelWFCManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] public WFCGenerator wfcGenerator;
        [SerializeField] private WFC.Chunking.ChunkManager chunkManager;

        [Header("Settings")]
        [SerializeField] private bool enableParallelProcessing = true;
        [SerializeField] private int maxThreads = 0; // 0 = auto based on CPU cores
        [SerializeField] private bool showDebugInfo = false;
        [SerializeField] private bool useBurst = true;

        private ParallelWFCProcessor parallelProcessor;

        // Performance stats
        private int activeThreads = 0;
        private float avgProcessingTime = 0;
        private int completedJobs = 0;

        // Profiling markers
        private static readonly ProfilerMarker s_ProcessorUpdateMarker =
            new ProfilerMarker("ParallelWFCManager.UpdateProcessor");
        private static readonly ProfilerMarker s_MainThreadEventsMarker =
            new ProfilerMarker("ParallelWFCManager.ProcessMainThreadEvents");

        private void Start()
        {
            if (!enableParallelProcessing)
                return;

            // Validate references
            if (wfcGenerator == null)
                wfcGenerator = FindAnyObjectByType<WFCGenerator>();

            if (chunkManager == null)
                chunkManager = FindAnyObjectByType<WFC.Chunking.ChunkManager>();

            if (wfcGenerator == null)
            {
                wfcGenerator = FindAnyObjectByType<WFCGenerator>();

                if (wfcGenerator == null)
                {
                    Debug.LogError("ParallelWFCManager: Missing required WFC implementation");
                    enabled = false;
                    return;
                }
            }

            // Create appropriate adapter
            IWFCAlgorithm algorithmAdapter;
            if (wfcGenerator != null)
            {
                algorithmAdapter = new WFCAlgorithmAdapter(wfcGenerator);
                Debug.Log($"Using WFCGenerator for parallel processing with Burst compilation: {useBurst}");
            }
            else
            {
                Debug.LogError("ParallelWFCManager: WFCGenerator is required but not found!");
                enabled = false;
                return;
            }

            // Auto-configure maxThreads if not set
            if (maxThreads <= 0)
            {
                // Use logical processor count, but leave 1-2 cores for the main thread
                int processorCount = SystemInfo.processorCount;
                maxThreads = Mathf.Max(1, processorCount > 4 ? processorCount - 2 : processorCount - 1);
            }

            // Initialize the parallel processor with the adapter
            parallelProcessor = new ParallelWFCProcessor(algorithmAdapter, maxThreads);
            parallelProcessor.Start();

            Debug.Log($"ParallelWFCManager: Initialized parallel processing with {maxThreads} worker threads");
        }

        private void Update()
        {
            if (parallelProcessor == null)
            {
                ParallelWFCManager parallelManager = FindObjectOfType<ParallelWFCManager>();
                if (parallelManager != null)
                {
                    parallelProcessor = parallelManager.GetParallelProcessor();
                    Debug.Log("Connected to ParallelWFCManager for parallel processing");
                }
                return;
            }

            // Update processor and handle job scheduling
            s_ProcessorUpdateMarker.Begin();
            parallelProcessor.Update();
            s_ProcessorUpdateMarker.End();

            // Process events that need to happen on the main thread
            s_MainThreadEventsMarker.Begin();
            parallelProcessor.ProcessMainThreadEvents();
            s_MainThreadEventsMarker.End();

            // Update performance stats
            activeThreads = parallelProcessor.ActiveThreads;
            avgProcessingTime = parallelProcessor.AverageJobTime;
            completedJobs = parallelProcessor.TotalProcessedJobs;
        }

        // Expose the processor for other scripts
        public ParallelWFCProcessor GetParallelProcessor()
        {
            return parallelProcessor;
        }

        /// <summary>
        /// Queue a chunk for parallel processing
        /// </summary>
        public bool QueueChunkForProcessing(Chunk chunk, WFCJobType jobType, int maxIterations = 100, float priority = 0)
        {
            if (parallelProcessor == null)
                return false;

            return parallelProcessor.QueueChunkForProcessing(chunk, jobType, maxIterations, priority);
        }

        private void OnDestroy()
        {
            // Stop and clean up parallel processor
            if (parallelProcessor != null)
            {
                parallelProcessor.Stop();
                parallelProcessor.Dispose();
                parallelProcessor = null;
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo || parallelProcessor == null)
                return;

            // Show basic stats on screen
            GUI.Label(new Rect(10, Screen.height - 60, 300, 20),
                $"Parallel: {parallelProcessor.ActiveThreads} threads, " +
                $"{parallelProcessor.AverageJobTime * 1000:F1}ms/job, " +
                $"{parallelProcessor.TotalProcessedJobs} completed");

            GUI.Label(new Rect(10, Screen.height - 40, 300, 20),
                $"Processing: {parallelProcessor.ProcessingChunksCount} chunks, " +
                $"Burst Compilation: {useBurst}");
        }
    }
}