// Assets/Scripts/Editor/WFCConfigurationEditor.cs
using UnityEditor;
using UnityEngine;
using WFC.Configuration;

namespace WFC.Editor
{
    /// <summary>
    /// Custom editor for WFCConfiguration to improve usability and provide validation
    /// </summary>
    [CustomEditor(typeof(WFCConfiguration))]
    public class WFCConfigurationEditor : UnityEditor.Editor
    {
        // Serialized properties
        private SerializedProperty worldProp;
        private SerializedProperty algorithmProp;
        private SerializedProperty visualizationProp;
        private SerializedProperty performanceProp;

        // Foldout states
        private bool showWorldSettings = true;
        private bool showAlgorithmSettings = true;
        private bool showVisualizationSettings = true;
        private bool showPerformanceSettings = true;
        private bool showDebugTools = false;

        private void OnEnable()
        {
            // Get serialized properties
            worldProp = serializedObject.FindProperty("world");
            algorithmProp = serializedObject.FindProperty("algorithm");
            visualizationProp = serializedObject.FindProperty("visualization");
            performanceProp = serializedObject.FindProperty("performance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            WFCConfiguration config = (WFCConfiguration)target;

            // Title and description
            GUILayout.Space(10);
            EditorGUILayout.LabelField("WFC Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Central configuration for Wave Function Collapse system. This asset provides settings for all WFC components.", MessageType.Info);
            GUILayout.Space(10);

            // Validation button
            if (GUILayout.Button("Validate Configuration"))
            {
                bool isValid = config.Validate();
                if (isValid)
                {
                    EditorUtility.DisplayDialog("Configuration Valid", "All configuration settings are valid.", "OK");
                }
            }
            GUILayout.Space(10);

            // World settings
            DrawWorldSettings();

            // Algorithm settings
            DrawAlgorithmSettings();

            // Visualization settings
            DrawVisualizationSettings();

            // Performance settings
            DrawPerformanceSettings();

            // Debug tools
            DrawDebugTools();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawWorldSettings()
        {
            showWorldSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showWorldSettings, "World Settings");
            if (showWorldSettings)
            {
                EditorGUI.indentLevel++;

                // Show world size visualization
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("World Size Preview", EditorStyles.boldLabel);

                WFCConfiguration config = (WFCConfiguration)target;
                float worldSizeX = config.World.worldSizeX;
                float worldSizeY = config.World.worldSizeY;
                float worldSizeZ = config.World.worldSizeZ;

                // Draw a 2D representation of the world size
                Rect rect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 40, 100);
                DrawWorldSizePreview(rect, worldSizeX, worldSizeY, worldSizeZ);

                EditorGUILayout.EndVertical();

                EditorGUILayout.PropertyField(worldProp, true);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAlgorithmSettings()
        {
            showAlgorithmSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showAlgorithmSettings, "Algorithm Settings");
            if (showAlgorithmSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(algorithmProp, true);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawVisualizationSettings()
        {
            showVisualizationSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showVisualizationSettings, "Visualization Settings");
            if (showVisualizationSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(visualizationProp, true);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPerformanceSettings()
        {
            showPerformanceSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showPerformanceSettings, "Performance Settings");
            if (showPerformanceSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(performanceProp, true);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDebugTools()
        {
            showDebugTools = EditorGUILayout.BeginFoldoutHeaderGroup(showDebugTools, "Debug Tools");
            if (showDebugTools)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("These tools can help debug and test your configuration.", MessageType.Info);

                if (GUILayout.Button("Print Configuration Summary"))
                {
                    WFCConfiguration config = (WFCConfiguration)target;
                    Debug.Log(GetConfigSummary(config));
                }

                if (GUILayout.Button("Create Default Resources Asset"))
                {
                    CreateDefaultResourcesAsset();
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawWorldSizePreview(Rect rect, float worldSizeX, float worldSizeY, float worldSizeZ)
        {
            // Calculate scale to fit in the rect while preserving aspect ratio
            float maxSize = Mathf.Max(worldSizeX, worldSizeY, worldSizeZ);
            float scale = Mathf.Min(rect.width / worldSizeX, rect.height / worldSizeY) * 0.8f;

            // Calculate center position
            Vector2 center = new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);

            // Draw background
            Handles.color = new Color(0.2f, 0.2f, 0.2f, 0.3f);
            Handles.DrawSolidRectangleWithOutline(rect, new Color(0.2f, 0.2f, 0.2f, 0.3f), Color.gray);

            // Draw X and Z axes
            Handles.color = Color.white;
            Vector2 start = center - new Vector2(worldSizeX * scale * 0.5f, -worldSizeY * scale * 0.5f);

            // Draw grid
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            for (int x = 0; x <= (int)worldSizeX; x++)
            {
                Handles.DrawLine(
                    start + new Vector2(x * scale, 0),
                    start + new Vector2(x * scale, -worldSizeY * scale)
                );
            }

            for (int y = 0; y <= (int)worldSizeY; y++)
            {
                Handles.DrawLine(
                    start + new Vector2(0, -y * scale),
                    start + new Vector2(worldSizeX * scale, -y * scale)
                );
            }

            // Draw outline
            Handles.color = Color.yellow;
            Vector2[] points = new Vector2[]
            {
                start,
                start + new Vector2(worldSizeX * scale, 0),
                start + new Vector2(worldSizeX * scale, -worldSizeY * scale),
                start + new Vector2(0, -worldSizeY * scale),
                start
            };
            for (int i = 0; i < points.Length - 1; i++)
            {
                Handles.DrawLine(points[i], points[i + 1]);
            }
            // Draw depth indicator for Z
            if (worldSizeZ > 1)
            {
                Handles.color = new Color(0.7f, 0.7f, 1.0f, 0.5f);
                Vector2 depthStart = start + new Vector2(worldSizeX * scale, 0);
                Vector2 depthOffset = new Vector2(worldSizeZ * scale * 0.3f, -worldSizeZ * scale * 0.3f);

                // Draw depth lines
                Handles.DrawLine(depthStart, depthStart + depthOffset);
                Handles.DrawLine(
                    depthStart + new Vector2(0, -worldSizeY * scale),
                    depthStart + new Vector2(0, -worldSizeY * scale) + depthOffset
                );
                Handles.DrawLine(
                    depthStart + depthOffset,
                    depthStart + depthOffset + new Vector2(0, -worldSizeY * scale)
                );
            }

            // Draw labels
            Handles.Label(start + new Vector2(-15, 0), "0,0");
            Handles.Label(start + new Vector2(worldSizeX * scale, 0) + new Vector2(5, 0), $"X: {worldSizeX}");
            Handles.Label(start + new Vector2(0, -worldSizeY * scale) + new Vector2(-15, -15), $"Y: {worldSizeY}");

            if (worldSizeZ > 1)
            {
                Handles.Label(start + new Vector2(worldSizeX * scale, 0) + new Vector2(worldSizeZ * scale * 0.3f, -worldSizeZ * scale * 0.3f) + new Vector2(5, 0), $"Z: {worldSizeZ}");
            }

            // Draw chunk size indicator
            WFCConfiguration config = (WFCConfiguration)target;
            float chunkSize = config.World.chunkSize;
            float cellSize = config.Visualization.cellSize;

            // Draw text showing the total world size in cells
            Handles.Label(rect.position + new Vector2(10, rect.height - 20),
                $"World Size: {worldSizeX * chunkSize}×{worldSizeY * chunkSize}×{worldSizeZ * chunkSize} cells " +
                $"({worldSizeX * chunkSize * cellSize}×{worldSizeY * chunkSize * cellSize}×{worldSizeZ * chunkSize * cellSize} units)");
        }

        private string GetConfigSummary(WFCConfiguration config)
        {
            return
                $"WFC Configuration Summary:\n" +
                $"---------------\n" +
                $"World: {config.World.worldSizeX}×{config.World.worldSizeY}×{config.World.worldSizeZ} chunks\n" +
                $"Chunk Size: {config.World.chunkSize}\n" +
                $"Max States: {config.World.maxStates}\n" +
                $"Random Seed: {config.World.randomSeed}\n" +
                $"---------------\n" +
                $"Algorithm Settings:\n" +
                $"Boundary Weight: {config.Algorithm.boundaryCoherenceWeight}\n" +
                $"Entropy Weight: {config.Algorithm.entropyWeight}\n" +
                $"Constraint Weight: {config.Algorithm.constraintWeight}\n" +
                $"Use Constraints: {config.Algorithm.useConstraints}\n" +
                $"---------------\n" +
                $"Performance:\n" +
                $"Max Threads: {config.Performance.maxThreads}\n" +
                $"Load Distance: {config.Performance.loadDistance}\n" +
                $"Unload Distance: {config.Performance.unloadDistance}";
        }

        private void CreateDefaultResourcesAsset()
        {
            // Make sure Resources directory exists
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            // Create a copy of the current configuration
            WFCConfiguration config = Instantiate((WFCConfiguration)target);

            // Save to Resources folder
            string path = "Assets/Resources/DefaultWFCConfig.asset";
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "Default Configuration Created",
                "Created default configuration at:\n" + path +
                "\n\nThis will be used when no configuration is explicitly assigned.",
                "OK");

            EditorGUIUtility.PingObject(config);
        }
    }
}