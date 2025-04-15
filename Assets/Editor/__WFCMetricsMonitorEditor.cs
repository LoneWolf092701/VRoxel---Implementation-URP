using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using WFC.Metrics;
using System.Linq;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using System;

namespace WFC.Metrics.Editor
{
    [CustomEditor(typeof(WFCMetricsManager))]
    public class WFCMetricsMonitorEditor : UnityEditor.Editor
    {
        // Editor state
        private bool showTestConfiguration = true;
        private bool showTestResults = true;
        private bool showStatus = true;
        private List<bool> showResultsTables = new List<bool>();

        // Test result display
        private Vector2 resultsScrollPosition;

        // Initialization
        private void OnEnable()
        {
            WFCMetricsManager manager = (WFCMetricsManager)target;

            // Initialize results tables visibility
            showResultsTables = new List<bool>();
            foreach (var test in manager.TestCases)
            {
                showResultsTables.Add(true);
            }
        }

        public override void OnInspectorGUI()
        {
            WFCMetricsManager manager = (WFCMetricsManager)target;

            // Draw default inspector for base fields
            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            // Draw test configuration section
            DrawTestConfiguration(manager);

            // Draw test status section
            DrawTestStatus(manager);

            // Draw test results section
            DrawTestResults(manager);

            // Draw test control buttons
            DrawTestControls(manager);

            // Auto-repaint the inspector every frame while tests are running
            if (manager.IsRunning)
            {
                Repaint();
            }
        }

        private void DrawTestConfiguration(WFCMetricsManager manager)
        {
            if (manager.TestCases.Count == 0)
            {
                manager.RegisterTestCases();
            }

            showTestConfiguration = EditorGUILayout.Foldout(showTestConfiguration, "Test Configuration", true, EditorStyles.foldoutHeader);

            if (showTestConfiguration)
            {
                EditorGUI.indentLevel++;

                // Global enable/disable option
                EditorGUI.BeginChangeCheck();
                bool newEnableAll = EditorGUILayout.Toggle("Enable All Tests", manager.enableAllTests);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(manager, "Toggle All Tests");
                    manager.enableAllTests = newEnableAll;

                    // Set all individual tests to match
                    for (int i = 0; i < manager.enabledTests.Length; i++)
                    {
                        manager.enabledTests[i] = newEnableAll;
                    }

                    EditorUtility.SetDirty(manager);
                }

                // Show individual test selection
                EditorGUILayout.LabelField("Select tests to run:", EditorStyles.boldLabel);

                // Make sure we have test cases
                if (manager.TestCases != null && manager.TestCases.Count > 0)
                {
                    // Ensure array length matches
                    if (manager.enabledTests.Length != manager.TestCases.Count)
                    {
                        Undo.RecordObject(manager, "Resize Test Array");
                        Array.Resize(ref manager.enabledTests, manager.TestCases.Count);
                        EditorUtility.SetDirty(manager);
                    }

                    // Display each test with checkbox
                    for (int i = 0; i < manager.TestCases.Count; i++)
                    {
                        string testName = manager.TestCases[i].TestName;
                        string testDescription = manager.TestCases[i].TestDescription;

                        GUIContent content = new GUIContent(testName, testDescription);

                        EditorGUI.BeginChangeCheck();
                        bool isEnabled = EditorGUILayout.ToggleLeft(content,
                            i < manager.enabledTests.Length ? manager.enabledTests[i] : false);

                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(manager, "Toggle Test");
                            // Make sure array is sized correctly
                            if (i >= manager.enabledTests.Length)
                            {
                                Array.Resize(ref manager.enabledTests, manager.TestCases.Count);
                            }
                            manager.enabledTests[i] = isEnabled;
                            EditorUtility.SetDirty(manager);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No test cases found. Make sure test cases are registered properly.", MessageType.Warning);
                }

                // Output directory selection
                EditorGUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Output Directory");

                EditorGUILayout.LabelField(manager.outputDirectory, EditorStyles.textField);

                if (GUILayout.Button("Browse", GUILayout.Width(80)))
                {
                    string newPath = EditorUtility.OpenFolderPanel("Select Output Directory", manager.outputDirectory, "");
                    if (!string.IsNullOrEmpty(newPath))
                    {
                        manager.outputDirectory = newPath;
                    }
                }

                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
            }
        }

        private void DrawTestStatus(WFCMetricsManager manager)
        {
            showStatus = EditorGUILayout.Foldout(showStatus, "Test Status", true, EditorStyles.foldoutHeader);

            if (showStatus)
            {
                EditorGUI.indentLevel++;

                // Display status message
                EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(manager.StatusMessage, MessageType.Info);

                // Display progress bar if running
                if (manager.IsRunning)
                {
                    Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
                    EditorGUI.ProgressBar(progressRect, manager.Progress, $"{(manager.Progress * 100):F0}%");

                    EditorGUILayout.Space(5);
                }

                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
            }
        }

        private void DrawTestResults(WFCMetricsManager manager)
        {
            showTestResults = EditorGUILayout.Foldout(showTestResults, "Test Results", true, EditorStyles.foldoutHeader);

            if (showTestResults)
            {
                EditorGUI.indentLevel++;

                // Scrollable results area
                resultsScrollPosition = EditorGUILayout.BeginScrollView(resultsScrollPosition,
                    GUILayout.Height(Mathf.Min(600, manager.TestResults.Count * 120 + 50)));

                // Check if any results are available
                if (manager.TestResults.Count == 0)
                {
                    EditorGUILayout.HelpBox("No test results available. Run tests to generate results.", MessageType.Info);
                }
                else
                {
                    // Draw result tables
                    int resultIndex = 0;
                    foreach (var result in manager.TestResults)
                    {
                        // Make sure the list is long enough
                        while (showResultsTables.Count <= resultIndex)
                        {
                            showResultsTables.Add(true);
                        }

                        // Draw foldout header for this result set
                        showResultsTables[resultIndex] = EditorGUILayout.Foldout(
                            showResultsTables[resultIndex],
                            result.Key,
                            true);

                        // Draw table if expanded
                        if (showResultsTables[resultIndex])
                        {
                            DrawResultTable(result.Value);
                        }

                        resultIndex++;
                    }
                }

                EditorGUILayout.EndScrollView();

                EditorGUI.indentLevel--;
            }
        }

        private void DrawResultTable(ITestResult result)
        {
            EditorGUI.indentLevel++;

            // Get header and data
            string header = result.GetCsvHeader();
            var data = result.GetCsvData().ToList();

            if (data.Count == 0)
            {
                EditorGUILayout.LabelField("No data available for this test.");
                EditorGUI.indentLevel--;
                return;
            }

            // Split header into columns
            string[] headerColumns = header.Split(',');

            // Create table header
            EditorGUILayout.BeginHorizontal();

            foreach (string column in headerColumns)
            {
                DrawHeaderCell(column);
            }

            EditorGUILayout.EndHorizontal();

            // Create table rows
            foreach (string row in data)
            {
                string[] cells = row.Split(',');

                EditorGUILayout.BeginHorizontal();

                for (int i = 0; i < cells.Length; i++)
                {
                    // Use proper alignment based on content
                    bool isNumeric = i > 0 && int.TryParse(cells[i].Split('.')[0], out _);
                    DrawCell(cells[i], isNumeric);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);
        }

        private void DrawHeaderCell(string text)
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.alignment = TextAnchor.MiddleCenter;
            style.wordWrap = true;

            EditorGUILayout.LabelField(text, style, GUILayout.MinWidth(80));
        }

        private void DrawCell(string text, bool rightAlign = false)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label);
            style.alignment = rightAlign ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;

            EditorGUILayout.LabelField(text, style, GUILayout.MinWidth(80));
        }

        private void DrawTestControls(WFCMetricsManager manager)
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !manager.IsRunning;

            if (GUILayout.Button("Run All Enabled Tests", GUILayout.Height(30)))
            {
                manager.RunAllTests();
            }

            if (GUILayout.Button("Export Results", GUILayout.Height(30)))
            {
                manager.ExportAllResults();

                // Show success message
                if (manager.TestResults.Count > 0)
                {
                    EditorUtility.DisplayDialog("Export Complete",
                        $"Test results have been exported to:\n{manager.outputDirectory}",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Export Failed",
                        "No test results available to export. Run tests first.",
                        "OK");
                }
            }

            GUI.enabled = manager.IsRunning;

            if (GUILayout.Button("Cancel Tests", GUILayout.Height(30)))
            {
                manager.CancelTests();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // Individual test buttons
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Run Individual Tests:", EditorStyles.boldLabel);

            int buttonsPerRow = 2; // Number of buttons per row
            int testCount = manager.TestCases.Count;

            for (int i = 0; i < testCount; i += buttonsPerRow)
            {
                EditorGUILayout.BeginHorizontal();

                for (int j = 0; j < buttonsPerRow && (i + j) < testCount; j++)
                {
                    int testIndex = i + j;
                    GUI.enabled = !manager.IsRunning;

                    if (GUILayout.Button($"Run: {manager.TestCases[testIndex].TestName}",
                        GUILayout.Height(25)))
                    {
                        manager.RunTest(testIndex);
                    }
                }

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    /// <summary>
    /// Provides utility functions for the metrics editor
    /// </summary>
    public static class MetricsEditorUtility
    {
        /// <summary>
        /// Opens the folder containing test results
        /// </summary>
        [MenuItem("Tools/WFC/Open Metrics Results Folder")]
        public static void OpenResultsFolder()
        {
            WFCMetricsManager metricsManager = GameObject.FindObjectOfType<WFCMetricsManager>();

            string path = metricsManager != null ?
                metricsManager.outputDirectory :
                Path.Combine(Application.persistentDataPath, "WFCMetrics");

            // Create directory if it doesn't exist
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // Open the folder
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>
        /// Creates a new metrics manager in the scene
        /// </summary>
        [MenuItem("Tools/WFC/Create Metrics Manager")]
        public static void CreateMetricsManager()
        {
            // Check if one already exists
            WFCMetricsManager existing = GameObject.FindObjectOfType<WFCMetricsManager>();

            if (existing != null)
            {
                EditorUtility.DisplayDialog("Metrics Manager Exists",
                    "A WFCMetricsManager already exists in the scene. Select it to configure and run tests.",
                    "OK");

                // Select the existing manager
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            // Create a new manager
            GameObject managerObject = new GameObject("WFC Metrics Manager");
            WFCMetricsManager manager = managerObject.AddComponent<WFCMetricsManager>();

            // Set default output directory
            manager.outputDirectory = Path.Combine(Application.persistentDataPath, "WFCMetrics");

            // Select the new manager
            Selection.activeGameObject = managerObject;

            Debug.Log("Created new WFC Metrics Manager. Select it to configure and run tests.");
        }
    }
}