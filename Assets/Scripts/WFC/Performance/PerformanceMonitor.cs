// Assets/Scripts/WFC/Performance/PerformanceMonitor.cs

using System.Collections.Generic;
using UnityEngine;
using WFC.Chunking;
using WFC.Core;
using WFC.Processing;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace WFC.Performance
{
    /// <summary>
    /// Monitors performance of the WFC system to help with optimization
    /// </summary>
    public class PerformanceMonitor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ChunkManager chunkManager;

        [Header("Settings")]
        [SerializeField] private int logFrequency = 60; // Log every 60 frames
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private bool showDetailedTimings = true;

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

        // Component timing data
        private Dictionary<string, float> componentTimings = new Dictionary<string, float>();
        private Dictionary<string, int> componentCalls = new Dictionary<string, int>();
        private Stopwatch componentTimer = new Stopwatch();
        private string currentTimingComponent = null;

        private ParallelWFCProcessor parallelProcessor;

        private void Start()
        {
            // Get reference to parallel processor if available
            var field = chunkManager.GetType().GetField("parallelProcessor",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                parallelProcessor = field.GetValue(chunkManager) as ParallelWFCProcessor;
            }
        }

        private void Update()
        {
            // Track frame time
            float frameTime = Time.deltaTime;
            frameTimes[frameIndex] = frameTime;
            frameIndex = (frameIndex + 1) % frameTimes.Length;
            frameCount++;

            // Calculate stats
            if (frameCount % logFrequency == 0 && enableLogging)
            {
                CalculateStats();
                LogPerformanceData();

                // Reset component timings after logging
                ResetComponentTimings();
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
            // Only proceed if we're timing the specified component
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

            // Get parallel processing data if available
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
            sb.AppendLine($"Chunk Processing Time: {chunkProcessingTime * 1000:F1}ms avg");
            sb.AppendLine($"Memory: {System.GC.GetTotalMemory(false) / (1024 * 1024):F1} MB");

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
            GUILayout.BeginArea(new Rect(10, yPos, 300, showDetailedTimings ? 400 : 100));
            GUILayout.Label($"FPS: {1.0f / avgFrameTime:F1}");
            GUILayout.Label($"Chunks: {loadedChunkCount}");
            GUILayout.Label($"Processing: {processingChunkCount}");

            // Display component timings if enabled
            if (showDetailedTimings && componentTimings.Count > 0)
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

            GUILayout.EndArea();
        }
    }
}