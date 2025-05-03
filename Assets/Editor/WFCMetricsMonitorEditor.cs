using UnityEngine;
using UnityEditor;
using WFC.Metrics;
using System.Collections.Generic;
using System.Linq;
using WFC.Core;
using WFC.Generation;
using System.IO;
using WFC.Chunking;
using System;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using WFC.MarchingCubes;
using WFC.Performance;
using WFC.Processing;
using WFC.Configuration;

[CustomEditor(typeof(WFCMetricsMonitor))]
public class WFCMetricsMonitorEditor : Editor
{
    private bool showPerformanceMetrics = true;
    private bool showCountMetrics = true;
    private bool showRatioMetrics = true;
    private bool showTestControls = true;

    // Test configuration
    private bool[] enabledTests = new bool[] { true, true, true, true, true, true };
    private string[] testNames = new string[] {
        "Chunk Generation Performance",
        "Boundary Coherence Performance",
        "Mesh Generation Performance",
        "Parallel Processing Efficiency",
        "World Size Scaling",
        "LOD Performance Impact"
    };

    // Test result holders
    private List<WFCMetricsMonitor.ChunkGenerationResult> chunkGenResults;
    private List<WFCMetricsMonitor.BoundaryCoherenceResult> boundaryResults;
    private List<WFCMetricsMonitor.MeshGenerationResult> meshGenResults;
    private List<WFCMetricsMonitor.ParallelProcessingResult> parallelResults;
    private List<WFCMetricsMonitor.WorldSizeScalingResult> worldSizeResults;
    private List<WFCMetricsMonitor.LODPerformanceResult> lodResults;
    private List<EditorCoroutine> activeCoroutines = new List<EditorCoroutine>();

    // Status tracking
    private string currentTestStatus = "Ready to run tests";
    private float testProgress = 0f;
    private bool isRunningTests = false;

    public override void OnInspectorGUI()
    {
        WFCMetricsMonitor monitor = (WFCMetricsMonitor)target;

        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("WFC System Metrics", EditorStyles.boldLabel);

        // Draw performance metrics sections
        DrawMetricsSections(monitor);

        // Test Configuration Section
        showTestControls = EditorGUILayout.Foldout(showTestControls, "Performance Test Configuration", true);
        if (showTestControls)
        {
            EditorGUI.indentLevel++;
            DrawTestConfiguration();
            EditorGUI.indentLevel--;
        }

        // Test Controls and Status
        DrawTestControls(monitor);

        // Repaint the inspector to keep metrics updated
        Repaint();
    }

    private EditorCoroutine StartTrackedCoroutine(IEnumerator routine)
    {
        EditorCoroutine coroutine = EditorCoroutineUtility.StartCoroutine(routine, this);
        activeCoroutines.Add(coroutine);
        return coroutine;
    }

    private void DrawMetricsSections(WFCMetricsMonitor monitor)
    {
        // Get metrics through reflection
        Dictionary<string, float> performanceMetrics = GetField<Dictionary<string, float>>(monitor, "performanceMetrics");
        Dictionary<string, int> countMetrics = GetField<Dictionary<string, int>>(monitor, "countMetrics");
        Dictionary<string, float> ratioMetrics = GetField<Dictionary<string, float>>(monitor, "ratioMetrics");

        // Performance Metrics Section
        showPerformanceMetrics = EditorGUILayout.Foldout(showPerformanceMetrics, "Performance Metrics", true);
        if (showPerformanceMetrics && performanceMetrics != null)
        {
            EditorGUI.indentLevel++;
            DrawMetricsTable(performanceMetrics);
            EditorGUI.indentLevel--;
        }

        // Count Metrics Section
        showCountMetrics = EditorGUILayout.Foldout(showCountMetrics, "Count Metrics", true);
        if (showCountMetrics && countMetrics != null)
        {
            EditorGUI.indentLevel++;
            DrawMetricsTable(countMetrics);
            EditorGUI.indentLevel--;
        }

        // Ratio Metrics Section
        showRatioMetrics = EditorGUILayout.Foldout(showRatioMetrics, "Ratio Metrics", true);
        if (showRatioMetrics && ratioMetrics != null)
        {
            EditorGUI.indentLevel++;
            DrawMetricsTable(ratioMetrics);
            EditorGUI.indentLevel--;
        }
    }

    private void DrawMetricsTable<T>(Dictionary<string, T> metrics)
    {
        if (metrics == null || metrics.Count == 0)
        {
            EditorGUILayout.LabelField("No metrics available");
            return;
        }

        foreach (var metric in metrics.OrderBy(m => m.Key))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(metric.Key + ":", GUILayout.Width(180));

            string valueStr;
            if (metric.Value is float floatVal)
            {
                valueStr = floatVal.ToString("F2");
                if (metric.Key.Contains("Time"))
                    valueStr += " ms";
                else if (metric.Key.Contains("Memory"))
                    valueStr += " MB";
                else if (metric.Key.Contains("Percentage") || metric.Key.Contains("Rate"))
                    valueStr += "%";
            }
            else
            {
                valueStr = metric.Value.ToString();
            }

            EditorGUILayout.LabelField(valueStr, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawTestConfiguration()
    {
        EditorGUILayout.LabelField("Select tests to run:", EditorStyles.boldLabel);

        for (int i = 0; i < testNames.Length; i++)
        {
            enabledTests[i] = EditorGUILayout.ToggleLeft(testNames[i], enabledTests[i]);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox("Note: Running all tests may take several minutes. Each test series will be saved to a CSV file.", MessageType.Info);

        // Get the test configuration from the monitor
        WFCMetricsMonitor monitor = (WFCMetricsMonitor)target;

        // Show chunk sizes from monitor
        var testChunkSizes = GetField<int[]>(monitor, "testChunkSizes");
        if (testChunkSizes != null)
        {
            EditorGUILayout.LabelField("Chunk Sizes to Test:", string.Join(", ", testChunkSizes));
        }

        // Show thread counts from monitor
        var testThreadCounts = GetField<int[]>(monitor, "testThreadCounts");
        if (testThreadCounts != null)
        {
            EditorGUILayout.LabelField("Thread Counts to Test:", string.Join(", ", testThreadCounts));
        }

        // Show world sizes from monitor
        var testWorldSizes = GetField<Vector3Int[]>(monitor, "testWorldSizes");
        if (testWorldSizes != null)
        {
            string worldSizesStr = string.Join(", ", testWorldSizes.Select(v => $"{v.x}×{v.y}×{v.z}"));
            EditorGUILayout.LabelField("World Sizes to Test:", worldSizesStr);
        }
    }

    private void DrawTestControls(WFCMetricsMonitor monitor)
    {
        EditorGUILayout.Space(10);

        if (GUILayout.Button("Connect Dependencies", GUILayout.Height(30)))
        {
            ConnectDependencies(monitor);
        }

        // Test status area
        EditorGUILayout.LabelField("Test Status:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(currentTestStatus);

        if (isRunningTests)
        {
            // Progress bar
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(progressRect, testProgress, $"{(testProgress * 100):F0}%");
        }

        EditorGUILayout.Space(5);

        // Test control buttons
        EditorGUILayout.BeginHorizontal();

        if (!isRunningTests)
        {
            if (GUILayout.Button("Run Selected Tests", GUILayout.Height(30)))
            {
                RunSelectedTests(monitor);
            }

            if (GUILayout.Button("Export Results", GUILayout.Height(30)))
            {
                ExportAllResults(monitor);
            }
        }
        else
        {
            if (GUILayout.Button("Cancel Tests", GUILayout.Height(30)))
            {
                CancelTests(monitor);
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void ConnectDependencies(WFCMetricsMonitor monitor)
    {
        Debug.Log("Attempting to connect dependencies");

        // Force create a mock WFCGenerator if needed
        GameObject mockObj = new GameObject("WFC_TestGenerator");
        var generator = mockObj.AddComponent<WFCGenerator>();

        // Create other required components
        var chunkManagerObj = new GameObject("MockChunkManager");
        var chunkManager = chunkManagerObj.AddComponent<ChunkManager>();

        // Create a mock ParallelWFCManager
        var parallelObj = new GameObject("MockParallelManager");
        var parallelManager = parallelObj.AddComponent<ParallelWFCManager>();
        parallelManager.wfcGenerator = generator; // Connect dependencies

        // Create a MeshGenerator
        var meshGenObj = new GameObject("MockMeshGenerator");
        var meshGen = meshGenObj.AddComponent<MeshGenerator>();
        meshGen.wfcGenerator = generator; // Connect dependencies

        // Connect all components
        SetField(monitor, "wfcGenerator", generator);
        SetField(monitor, "chunkManager", chunkManager);
        SetField(monitor, "meshGenerator", meshGen);
        SetField(monitor, "parallelManager", parallelManager);

        // Initialize WFCGenerator
        var initMethod = generator.GetType().GetMethod("InitializeRulesOnly",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (initMethod != null)
            initMethod.Invoke(generator, null);

        // Initialize test fields
        monitor.InitializeDefaultAdjacencyRules();
        monitor.InitializeInternalAdjacencyRules();

        Debug.Log("Dependencies connected!");
    }

    private void SetField(object obj, string fieldName, object value)
    {
        if (obj == null) return;

        var field = obj.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (field != null)
            field.SetValue(obj, value);
    }

    private void RunSelectedTests(WFCMetricsMonitor monitor)
    {
        // Count enabled tests for progress tracking
        int totalEnabledTests = enabledTests.Count(t => t);
        if (totalEnabledTests == 0)
        {
            currentTestStatus = "No tests selected. Please select at least one test to run.";
            return;
        }

        isRunningTests = true;
        testProgress = 0f;
        currentTestStatus = "Preparing to run tests...";

        // Start test coroutine
        EditorCoroutineUtility.StartCoroutine(RunTestsCoroutine(monitor, totalEnabledTests), this);
    }

    private System.Collections.IEnumerator RunTestsCoroutine(WFCMetricsMonitor monitor, int totalEnabledTests)
    {
        int completedTests = 0;

        // Run Chunk Generation test
        if (enabledTests[0])
        {
            currentTestStatus = "Running Chunk Generation Performance tests...";
            yield return StartTrackedCoroutine(
                InvokeTestMethod(monitor, "RunChunkGenerationTest"));

            // Get results through reflection
            chunkGenResults = GetField<List<WFCMetricsMonitor.ChunkGenerationResult>>(monitor, "chunkGenerationResults");

            completedTests++;
            testProgress = (float)completedTests / totalEnabledTests;
        }

        // Run Boundary Coherence test
        if (enabledTests[1])
        {
            currentTestStatus = "Running Boundary Coherence Performance tests...";
            yield return StartTrackedCoroutine(
                InvokeTestMethod(monitor, "RunBoundaryCoherenceTest"));
            boundaryResults = GetField<List<WFCMetricsMonitor.BoundaryCoherenceResult>>(monitor, "boundaryCoherenceResults");

            completedTests++;
            testProgress = (float)completedTests / totalEnabledTests;
        }

        // Run Mesh Generation test
        if (enabledTests[2])
        {
            currentTestStatus = "Running Mesh Generation Performance tests...";
            yield return StartTrackedCoroutine(
                InvokeTestMethod(monitor, "RunMeshGenerationTest"));
            meshGenResults = GetField<List<WFCMetricsMonitor.MeshGenerationResult>>(monitor, "meshGenerationResults");

            completedTests++;
            testProgress = (float)completedTests / totalEnabledTests;
        }

        // Run Parallel Processing test
        if (enabledTests[3])
        {
            currentTestStatus = "Running Parallel Processing Efficiency tests...";
            yield return StartTrackedCoroutine(
                InvokeTestMethod(monitor, "RunParallelProcessingTest"));
            parallelResults = GetField<List<WFCMetricsMonitor.ParallelProcessingResult>>(monitor, "parallelProcessingResults");

            completedTests++;
            testProgress = (float)completedTests / totalEnabledTests;
        }

        // Run World Size Scaling test
        if (enabledTests[4])
        {
            currentTestStatus = "Running World Size Scaling tests...";
            yield return StartTrackedCoroutine(
                InvokeTestMethod(monitor, "RunWorldSizeScalingTest"));
            worldSizeResults = GetField<List<WFCMetricsMonitor.WorldSizeScalingResult>>(monitor, "worldSizeScalingResults");

            completedTests++;
            testProgress = (float)completedTests / totalEnabledTests;
        }

        // Run LOD Performance test
        if (enabledTests[5])
        {
            currentTestStatus = "Running LOD Performance Impact tests...";
            yield return StartTrackedCoroutine(
                InvokeTestMethod(monitor, "RunLODPerformanceTest"));
            lodResults = GetField<List<WFCMetricsMonitor.LODPerformanceResult>>(monitor, "lodPerformanceResults");

            completedTests++;
            testProgress = (float)completedTests / totalEnabledTests;
        }

        // Tests complete
        isRunningTests = false;
        testProgress = 1.0f;
        currentTestStatus = $"Test suite complete. Ran {completedTests} of {totalEnabledTests} selected tests.";

        // Export results
        ExportAllResults(monitor);

        yield return null;
    }

    private System.Collections.IEnumerator InvokeTestMethod(WFCMetricsMonitor monitor, string methodName)
    {
        var method = monitor.GetType().GetMethod(methodName);
        if (method != null)
        {
            var coroutine = (System.Collections.IEnumerator)method.Invoke(monitor, null);
            yield return StartTrackedCoroutine(coroutine);
        }
        else
        {
            Debug.LogError($"Method {methodName} not found on WFCMetricsMonitor");
            yield return null;
        }
    }

    private void CancelTests(WFCMetricsMonitor monitor)
    {
        // Stop all coroutines
        foreach (var coroutine in activeCoroutines)
        {
            EditorCoroutineUtility.StopCoroutine(coroutine);
        }
        activeCoroutines.Clear();

        // Set the isTestRunning field to false
        var field = monitor.GetType().GetField("isTestRunning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (field != null)
            field.SetValue(monitor, false);

        isRunningTests = false;
        currentTestStatus = "Tests cancelled by user.";
    }

    private void ExportAllResults(WFCMetricsMonitor monitor)
    {
        // Check if any results exist
        bool hasResults = false;

        if (chunkGenResults != null && chunkGenResults.Count > 0)
        {
            ExportChunkGenerationResults();
            hasResults = true;
        }

        if (boundaryResults != null && boundaryResults.Count > 0)
        {
            ExportBoundaryCoherenceResults();
            hasResults = true;
        }

        if (meshGenResults != null && meshGenResults.Count > 0)
        {
            ExportMeshGenerationResults();
            hasResults = true;
        }

        if (parallelResults != null && parallelResults.Count > 0)
        {
            ExportParallelProcessingResults();
            hasResults = true;
        }

        if (worldSizeResults != null && worldSizeResults.Count > 0)
        {
            ExportWorldSizeScalingResults();
            hasResults = true;
        }

        if (lodResults != null && lodResults.Count > 0)
        {
            ExportLODPerformanceResults();
            hasResults = true;
        }

        if (hasResults)
        {
            EditorUtility.DisplayDialog("Export Complete",
                "Test results have been exported to CSV files", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Export Failed",
                "No test results available to export. Run tests first.", "OK");
        }
    }

    private void ExportChunkGenerationResults()
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "ChunkGenerationResults.csv");

        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("ChunkSize,ProcessingTime(ms),MemoryUsage(MB),CellsCollapsedPercent,PropagationEvents,IterationsRequired");

            foreach (var result in chunkGenResults)
            {
                writer.WriteLine($"{result.chunkSize},{result.processingTime:F2},{result.memoryUsage:F2}," +
                                $"{result.cellsCollapsedPercent:F2},{result.propagationEvents},{result.iterationsRequired}");
            }
        }

        Debug.Log($"Chunk Generation results exported to: {outputPath}");
    }

    private void ExportBoundaryCoherenceResults()
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "BoundaryCoherenceResults.csv");

        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("WorldSize,BoundaryUpdates,BufferSynchronizations,ConflictsDetected,ConflictsResolved,CoherenceScore");

            foreach (var result in boundaryResults)
            {
                writer.WriteLine($"{result.worldSize.x}x{result.worldSize.y}x{result.worldSize.z}," +
                                $"{result.boundaryUpdates},{result.bufferSynchronizations}," +
                                $"{result.conflictsDetected},{result.conflictsResolved},{result.coherenceScore:F2}");
            }
        }

        Debug.Log($"Boundary Coherence results exported to: {outputPath}");
    }

    private void ExportMeshGenerationResults()
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "MeshGenerationResults.csv");

        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("ChunkSize,DensityFieldGeneration(ms),MarchingCubes(ms),TotalMeshTime(ms),Vertices,Triangles,MemoryUsage(MB)");

            foreach (var result in meshGenResults)
            {
                writer.WriteLine($"{result.chunkSize},{result.densityFieldGenerationTime:F2},{result.marchingCubesTime:F2}," +
                                $"{result.totalMeshTime:F2},{result.vertices},{result.triangles},{result.memoryUsage:F2}");
            }
        }

        Debug.Log($"Mesh Generation results exported to: {outputPath}");
    }

    private void ExportParallelProcessingResults()
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "ParallelProcessingResults.csv");

        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("ThreadCount,ProcessingTime(ms),SpeedupFactor,SynchronizationOverhead(ms),MemoryOverheadPercent,MaxConcurrentChunks");

            foreach (var result in parallelResults)
            {
                writer.WriteLine($"{result.threadCount},{result.processingTime:F2},{result.speedupFactor:F2}," +
                                $"{result.synchronizationOverhead:F2},{result.memoryOverheadPercent:F2},{result.maxConcurrentChunks}");
            }
        }

        Debug.Log($"Parallel Processing results exported to: {outputPath}");
    }

    private void ExportWorldSizeScalingResults()
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "WorldSizeScalingResults.csv");

        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("WorldSize,TotalMemoryUsage(MB),GenerationTime(s),FPSImpact,ChunksLoaded,ChunksProcessedPerFrame,LoadingDistance");

            foreach (var result in worldSizeResults)
            {
                writer.WriteLine($"{result.worldSize.x}x{result.worldSize.y}x{result.worldSize.z}," +
                                $"{result.totalMemoryUsage:F2},{result.generationTime:F2},{result.fpsImpact:F2}," +
                                $"{result.chunksLoaded},{result.chunksProcessedPerFrame:F2},{result.loadingDistance:F2}");
            }
        }

        Debug.Log($"World Size Scaling results exported to: {outputPath}");
    }

    private void ExportLODPerformanceResults()
    {
        string outputPath = Path.Combine(Application.persistentDataPath, "LODPerformanceResults.csv");

        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("LODLevel,DistanceRange,VertexReductionPercent,GenerationSpeedIncreasePercent,MemoryReductionPercent,VisualQualityImpact");

            foreach (var result in lodResults)
            {
                writer.WriteLine($"{result.lodLevel},{result.distanceRange:F0},{result.vertexReductionPercent:F2}," +
                                $"{result.generationSpeedIncreasePercent:F2},{result.memoryReductionPercent:F2},{result.visualQualityImpact}");
            }
        }

        Debug.Log($"LOD Performance results exported to: {outputPath}");
    }

    /// <summary>
    /// Helper method to get a field value using reflection
    /// </summary>
    private T GetField<T>(object obj, string fieldName)
    {
        if (obj == null)
            return default;

        var field = obj.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public);

        if (field != null)
            return (T)field.GetValue(obj);

        return default;
    }
}