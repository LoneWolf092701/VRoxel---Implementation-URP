
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Boundary;

namespace WFC.Processing
{
    /// <summary>
    /// Manages parallel processing of WFC algorithm with your existing systems.
    /// This component acts as a bridge between your ChunkManager and the parallel processor.
    /// </summary>
    public class ParallelWFCManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] public WFCGenerator wfcGenerator;      // changed
        [SerializeField] private WFC.Chunking.ChunkManager chunkManager;

        [Header("Settings")]
        [SerializeField] private bool enableParallelProcessing = true;
        [SerializeField] private int maxThreads = 0; // 0 = auto
        [SerializeField] private bool showDebugInfo = false;

        private ParallelWFCProcessor parallelProcessor;

        // Performance stats
        private int activeThreads = 0;
        private float avgProcessingTime = 0;
        private int completedJobs = 0;

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
                Debug.Log("Using WFCGenerator for parallel processing");
            }
            else
            {
                Debug.LogError("ParallelWFCManager: WFCGenerator is required but not found!");
                enabled = false;
                return;
            }

            if (maxThreads <= 0)
                maxThreads = Mathf.Max(1, SystemInfo.processorCount - 1);

            // Initialize the parallel processor with the adapter
            parallelProcessor = new ParallelWFCProcessor(algorithmAdapter, maxThreads);
            parallelProcessor.Start();

            Debug.Log("ParallelWFCManager: Initialized parallel processing");
        }

        private void Update()
        {
            if (parallelProcessor == null) {
                ParallelWFCManager parallelManager = FindObjectOfType<ParallelWFCManager>();
                if (parallelManager != null)
                {
                    parallelProcessor = parallelManager.GetParallelProcessor();
                    Debug.Log("Connected to ParallelWFCManager for parallel processing");
                }
            }

            // Update performance stats
            activeThreads = parallelProcessor.ActiveThreads;
            avgProcessingTime = parallelProcessor.AverageJobTime;
            completedJobs = parallelProcessor.TotalProcessedJobs;

        }

        // expose the processor for other scripts
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
            // Stop parallel processor
            if (parallelProcessor != null)
            {
                parallelProcessor.Stop();
                parallelProcessor = null;
            }
        }

        private void OnGUI()
        {
            if (!showDebugInfo || parallelProcessor == null)
                return;

            // Show basic stats on screen
            GUI.Label(new Rect(10, Screen.height - 60, 200, 20), $"Parallel: {parallelProcessor.ActiveThreads} threads, {parallelProcessor.AverageJobTime * 1000:F1}ms/job");
        }
    }
}