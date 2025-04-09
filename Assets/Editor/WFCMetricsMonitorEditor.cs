using UnityEngine;
using UnityEditor;
using WFC.Metrics;

[CustomEditor(typeof(WFCMetricsMonitor))]
public class WFCMetricsMonitorEditor : Editor
{
    private bool showBoundaryMetrics = true;
    private bool showPerformanceMetrics = true;
    private bool showConstraintMetrics = true;

    public override void OnInspectorGUI()
    {
        WFCMetricsMonitor monitor = (WFCMetricsMonitor)target;

        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("WFC System Metrics", EditorStyles.boldLabel);

        // Boundary Coherence Section
        showBoundaryMetrics = EditorGUILayout.Foldout(showBoundaryMetrics, "Boundary Coherence", true);
        if (showBoundaryMetrics)
        {
            EditorGUI.indentLevel++;

            // Current coherence score
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Coherence Score:", GUILayout.Width(150));

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = monitor.GetCoherenceColor(monitor.BoundaryCoherence);
            EditorGUILayout.LabelField($"{monitor.BoundaryCoherence:F3}", style);
            EditorGUILayout.EndHorizontal();

            // Draw progress bar
            Rect rect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(rect, monitor.BoundaryCoherence, "Boundary Coherence");

            if (GUILayout.Button("Run Boundary Coherence Test"))
            {
                monitor.RunBoundaryCoherenceTest();
            }

            EditorGUI.indentLevel--;
        }

        // Performance Metrics Section
        showPerformanceMetrics = EditorGUILayout.Foldout(showPerformanceMetrics, "Generation Performance", true);
        if (showPerformanceMetrics)
        {
            EditorGUI.indentLevel++;

            // Current timing data
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Avg. Generation Time:", GUILayout.Width(150));
            EditorGUILayout.LabelField($"{monitor.AverageGenerationTime * 1000:F1} ms", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // Draw progress bar (normalized to target - 16.7ms for 60fps)
            float normalizedTime = Mathf.Clamp01(monitor.AverageGenerationTime * 1000 / 16.7f);
            Rect timeRect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(timeRect, normalizedTime, "Generation Time");

            if (GUILayout.Button("Run Performance Test"))
            {
                monitor.RunPerformanceTest();
            }

            EditorGUI.indentLevel--;
        }

        // Constraint Satisfaction Section
        showConstraintMetrics = EditorGUILayout.Foldout(showConstraintMetrics, "Constraint Satisfaction", true);
        if (showConstraintMetrics)
        {
            EditorGUI.indentLevel++;

            // Current constraint data
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Satisfaction Rate:", GUILayout.Width(150));
            EditorGUILayout.LabelField($"{monitor.ConstraintSatisfactionRate:P1}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // Draw progress bar
            Rect cRect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(cRect, monitor.ConstraintSatisfactionRate, "Constraint Satisfaction");

            if (GUILayout.Button("Run Constraint Test"))
            {
                monitor.RunConstraintTest();
            }

            EditorGUI.indentLevel--;
        }

        // Run all tests button
        EditorGUILayout.Space(10);
        if (GUILayout.Button("Run All Tests"))
        {
            monitor.RunBoundaryCoherenceTest();
            monitor.RunPerformanceTest();
            monitor.RunConstraintTest();
        }
    }
}