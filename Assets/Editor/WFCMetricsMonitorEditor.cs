using UnityEngine;
using UnityEditor;
using WFC.Metrics;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(WFCMetricsMonitor))]
public class WFCMetricsMonitorEditor : Editor
{
    private bool showPerformanceMetrics = true;
    private bool showCountMetrics = true;
    private bool showRatioMetrics = true;
    private bool showTestControls = true;

    // Cache for performance metrics
    private Dictionary<string, float> performanceMetrics = new Dictionary<string, float>();
    private Dictionary<string, int> countMetrics = new Dictionary<string, int>();
    private Dictionary<string, float> ratioMetrics = new Dictionary<string, float>();

    // Cache for time series data (simple graphs)
    private Dictionary<string, List<float>> metricHistory = new Dictionary<string, List<float>>();
    private int maxHistoryPoints = 30;

    public override void OnInspectorGUI()
    {
        WFCMetricsMonitor monitor = (WFCMetricsMonitor)target;

        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("WFC System Metrics", EditorStyles.boldLabel);

        // Get metrics through reflection
        UpdateMetricsCache(monitor);

        // Performance Metrics Section
        showPerformanceMetrics = EditorGUILayout.Foldout(showPerformanceMetrics, "Performance Metrics", true);
        if (showPerformanceMetrics)
        {
            EditorGUI.indentLevel++;

            foreach (var metric in performanceMetrics.OrderBy(m => m.Key))
            {
                string unit = metric.Key.Contains("Time") ? "ms" :
                              metric.Key.Contains("Memory") ? "MB" : "";

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(metric.Key + ":", GUILayout.Width(180));

                // Get color based on value (green = good, red = bad)
                Color valueColor = Color.white;
                if (metric.Key.Contains("Time"))
                {
                    valueColor = metric.Value < 16.7f ? Color.green :
                                (metric.Value < 33.3f ? Color.yellow : Color.red);
                }
                else if (metric.Key.Contains("Coherence"))
                {
                    valueColor = metric.Value > 0.9f ? Color.green :
                                (metric.Value > 0.7f ? Color.yellow : Color.red);
                }

                GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = valueColor;

                EditorGUILayout.LabelField($"{metric.Value:F2} {unit}", style);
                EditorGUILayout.EndHorizontal();

                // Draw progress bar for appropriate metrics
                if (metric.Key.Contains("Time") || metric.Key.Contains("Coherence"))
                {
                    float normalizedValue;

                    if (metric.Key.Contains("Time"))
                    {
                        // Normalize time to 60fps (16.7ms) target
                        normalizedValue = Mathf.Clamp01(metric.Value / 33.3f);
                    }
                    else if (metric.Key.Contains("Coherence"))
                    {
                        normalizedValue = Mathf.Clamp01(metric.Value);
                    }
                    else
                    {
                        normalizedValue = 0.5f; // Default
                    }

                    Rect rect = EditorGUILayout.GetControlRect(false, 5);
                    EditorGUI.DrawRect(rect, Color.gray);

                    // Draw colored progress
                    Color barColor = Color.Lerp(Color.red, Color.green,
                        metric.Key.Contains("Time") ? 1 - normalizedValue : normalizedValue);

                    Rect fillRect = new Rect(rect.x, rect.y, rect.width * normalizedValue, rect.height);
                    EditorGUI.DrawRect(fillRect, barColor);
                }

                // Show history graph for certain metrics
                if (metricHistory.ContainsKey(metric.Key) && metricHistory[metric.Key].Count > 1)
                {
                    DrawSimpleGraph(metric.Key, metricHistory[metric.Key]);
                }
            }

            EditorGUI.indentLevel--;
        }

        // Count Metrics Section
        showCountMetrics = EditorGUILayout.Foldout(showCountMetrics, "Count Metrics", true);
        if (showCountMetrics)
        {
            EditorGUI.indentLevel++;

            foreach (var metric in countMetrics.OrderBy(m => m.Key))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(metric.Key + ":", GUILayout.Width(180));
                EditorGUILayout.LabelField(metric.Value.ToString("N0"), EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;
        }

        // Ratio Metrics Section
        showRatioMetrics = EditorGUILayout.Foldout(showRatioMetrics, "Ratio Metrics", true);
        if (showRatioMetrics)
        {
            EditorGUI.indentLevel++;

            foreach (var metric in ratioMetrics.OrderBy(m => m.Key))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(metric.Key + ":", GUILayout.Width(180));

                // Get color based on value
                Color valueColor = metric.Value > 0.9f ? Color.green :
                                  (metric.Value > 0.7f ? Color.yellow : Color.red);

                GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = valueColor;

                EditorGUILayout.LabelField($"{metric.Value:P1}", style);
                EditorGUILayout.EndHorizontal();

                // Draw progress bar
                Rect rect = EditorGUILayout.GetControlRect(false, 5);
                EditorGUI.DrawRect(rect, Color.gray);

                Rect fillRect = new Rect(rect.x, rect.y, rect.width * metric.Value, rect.height);
                EditorGUI.DrawRect(fillRect, valueColor);
            }

            EditorGUI.indentLevel--;
        }

        // Test Controls Section
        showTestControls = EditorGUILayout.Foldout(showTestControls, "Test Controls", true);
        if (showTestControls)
        {
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run Performance Test", GUILayout.Height(30)))
            {
                monitor.RunPerformanceTest();
            }

            if (GUILayout.Button("Save Current Metrics", GUILayout.Height(30)))
            {
                monitor.SaveCurrentMetrics();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Reset Metrics", GUILayout.Height(30)))
            {
                monitor.ResetMetrics();
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Export Options", EditorStyles.boldLabel);

            if (GUILayout.Button("Export Metrics Table", GUILayout.Height(25)))
            {
                ExportMetricsTable();
            }
        }

        // Repaint the inspector to keep metrics updated
        Repaint();
    }

    private void UpdateMetricsCache(WFCMetricsMonitor monitor)
    {
        try
        {
            // Get metrics through reflection
            var performanceField = monitor.GetType().GetField("performanceMetrics",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var countField = monitor.GetType().GetField("countMetrics",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var ratioField = monitor.GetType().GetField("ratioMetrics",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (performanceField != null)
                performanceMetrics = performanceField.GetValue(monitor) as Dictionary<string, float> ?? new Dictionary<string, float>();

            if (countField != null)
                countMetrics = countField.GetValue(monitor) as Dictionary<string, int> ?? new Dictionary<string, int>();

            if (ratioField != null)
                ratioMetrics = ratioField.GetValue(monitor) as Dictionary<string, float> ?? new Dictionary<string, float>();

            // Update history for each metric (simple time series tracking)
            foreach (var metric in performanceMetrics)
            {
                if (!metricHistory.ContainsKey(metric.Key))
                    metricHistory[metric.Key] = new List<float>();

                var history = metricHistory[metric.Key];
                history.Add(metric.Value);

                // Keep only last N points
                if (history.Count > maxHistoryPoints)
                    history.RemoveAt(0);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error updating metrics cache: {e.Message}");
        }
    }

    private void DrawSimpleGraph(string metricName, List<float> values)
    {
        if (values.Count < 2)
            return;

        // Get min and max for scaling
        float min = values.Min();
        float max = values.Max();
        if (max - min < 0.01f)
            max = min + 1f; // Prevent division by zero

        // Graph area
        Rect graphRect = EditorGUILayout.GetControlRect(false, 40);
        EditorGUI.DrawRect(graphRect, new Color(0.1f, 0.1f, 0.1f, 0.5f));

        // Draw graph lines
        for (int i = 1; i < values.Count; i++)
        {
            // Calculate positions
            float prevX = graphRect.x + graphRect.width * ((i - 1) / (float)(values.Count - 1));
            float prevY = graphRect.y + graphRect.height * (1 - Mathf.InverseLerp(min, max, values[i - 1]));

            float currX = graphRect.x + graphRect.width * (i / (float)(values.Count - 1));
            float currY = graphRect.y + graphRect.height * (1 - Mathf.InverseLerp(min, max, values[i]));

            // Determine color based on metric type
            Color lineColor = Color.cyan;
            if (metricName.Contains("Time"))
                lineColor = Color.yellow;
            else if (metricName.Contains("Memory"))
                lineColor = Color.magenta;
            else if (metricName.Contains("Coherence"))
                lineColor = Color.green;

            // Draw line
            Handles.color = lineColor;
            Handles.DrawLine(new Vector3(prevX, prevY), new Vector3(currX, currY));
        }

        // Draw min/max labels
        GUIStyle smallLabel = new GUIStyle(EditorStyles.miniLabel);
        smallLabel.normal.textColor = Color.white;

        EditorGUI.LabelField(
            new Rect(graphRect.x, graphRect.y, 50, 15),
            max.ToString("F1"),
            smallLabel);

        EditorGUI.LabelField(
            new Rect(graphRect.x, graphRect.y + graphRect.height - 15, 50, 15),
            min.ToString("F1"),
            smallLabel);
    }

    private void ExportMetricsTable()
    {
        WFCMetricsMonitor monitor = (WFCMetricsMonitor)target;

        // Call the SaveCurrentMetrics method to ensure values are up to date
        monitor.SaveCurrentMetrics();

        EditorUtility.DisplayDialog(
            "Metrics Export",
            "Metrics have been exported to the file specified in the WFCMetricsMonitor component.",
            "OK");
    }
}