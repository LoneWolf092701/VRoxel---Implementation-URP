// Assets/Scripts/WFC/Processing/ParallelWFCManager.cs

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Testing;
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
        [SerializeField] private WFCGenerator wfcGenerator;
        [SerializeField] private WFC.Chunking.ChunkManager chunkManager;
        [SerializeField] private WFCTestController wfcTestController;

        [Header("Settings")]
        [SerializeField] private bool enableParallelProcessing = true;
        [SerializeField] private int maxThreads = 0; // 0 = auto
        [SerializeField] private bool showDebugInfo = true;

        // Parallel processor
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

            if (wfcGenerator == null && wfcTestController == null)
            {
                // Try to find WFCTestController if WFCGenerator not found
                wfcTestController = FindAnyObjectByType<WFCTestController>();

                if (wfcTestController == null)
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
                algorithmAdapter = new WFCAlgorithmAdapter(wfcTestController);
                Debug.Log("Using WFCTestController for parallel processing");
            }

            // Initialize the parallel processor with the adapter
            parallelProcessor = new ParallelWFCProcessor(algorithmAdapter, maxThreads);
            parallelProcessor.Start();

            Debug.Log("ParallelWFCManager: Initialized parallel processing");
        }

        private void Update()
        {
            if (parallelProcessor == null)
                return;

            // Update performance stats
            activeThreads = parallelProcessor.ActiveThreads;
            avgProcessingTime = parallelProcessor.AverageJobTime;
            completedJobs = parallelProcessor.TotalProcessedJobs;

            // Check if chunks need processing
            // This would require accessing the ChunkManager's task queue
            // For now, let's assume you modify ChunkManager to expose a method
            // CheckForParallelProcessingTasks();
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
            GUILayout.BeginArea(new Rect(10, Screen.height - 100, 250, 90));
            GUILayout.Label("<b>Parallel WFC Stats</b>");
            GUILayout.Label($"Active Threads: {activeThreads}");
            GUILayout.Label($"Avg. Processing Time: {avgProcessingTime * 1000:F2}ms");
            GUILayout.Label($"Completed Jobs: {completedJobs}");
            GUILayout.EndArea();
        }
    }
}