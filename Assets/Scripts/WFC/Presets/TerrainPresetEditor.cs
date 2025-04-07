// Assets/Scripts/Editor/TerrainPresetEditor.cs
using UnityEditor;
using UnityEngine;
using WFC.Presets;

namespace WFC.Editor
{
    /// <summary>
    /// Custom editor for TerrainConstraintPreset assets to improve usability
    /// </summary>
    [CustomEditor(typeof(TerrainConstraintPreset))]
    public class TerrainPresetEditor : UnityEditor.Editor
    {
        // Foldout states
        private bool showPreviewSection = true;
        private bool showMountainSettings = true;
        private bool showRiverSettings = true;
        private bool showForestSettings = true;
        private bool showBeachSettings = true;
        private bool showAdvancedSettings = false;

        // Preview rendering
        private PreviewRenderUtility previewRenderer;
        private Camera previewCamera;
        private Mesh previewMesh;
        private float rotationAngle = 0f;

        private void OnEnable()
        {
            // Initialize preview renderer if needed
            if (previewRenderer == null)
            {
                previewRenderer = new PreviewRenderUtility();
                previewCamera = previewRenderer.camera;
                previewCamera.transform.position = new Vector3(0, 10, -15);
                previewCamera.transform.LookAt(Vector3.zero);

                // Create simple preview mesh
                CreatePreviewMesh();
            }
        }

        private void OnDisable()
        {
            // Clean up preview renderer
            if (previewRenderer != null)
            {
                previewRenderer.Cleanup();
                previewRenderer = null;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            TerrainConstraintPreset preset = (TerrainConstraintPreset)target;

            // Title
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Terrain Constraint Preset", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Configure terrain features and constraints for WFC generation", MessageType.Info);
            GUILayout.Space(10);

            // Basic information
            EditorGUILayout.PropertyField(serializedObject.FindProperty("presetName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("previewImage"));

            // Preview section
            showPreviewSection = EditorGUILayout.Foldout(showPreviewSection, "Preview", true);
            if (showPreviewSection)
            {
                EditorGUI.indentLevel++;
                GUILayout.Space(5);

                // Draw preview image if available, otherwise show 3D preview
                var previewImage = serializedObject.FindProperty("previewImage").objectReferenceValue as Texture2D;
                if (previewImage != null)
                {
                    // Get preview rect
                    Rect previewRect = GUILayoutUtility.GetRect(400, 200);

                    // Draw the image
                    GUI.DrawTexture(previewRect, previewImage, ScaleMode.ScaleToFit);
                }
                else
                {
                    // Draw 3D preview
                    Rect previewRect = GUILayoutUtility.GetRect(400, 200);
                    DrawTerrainPreview(previewRect);
                }

                GUILayout.Space(5);
                EditorGUI.indentLevel--;
            }

            // Global features
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Global Features", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("includeMountainRange"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("includeRiver"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("includeForest"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("includeBeach"));

            // Mountain settings
            GUILayout.Space(10);
            SerializedProperty includeMountainRange = serializedObject.FindProperty("includeMountainRange");
            EditorGUI.BeginDisabledGroup(!includeMountainRange.boolValue);

            showMountainSettings = EditorGUILayout.Foldout(showMountainSettings, "Mountain Settings", true);
            if (showMountainSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mountainHeight"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mountainCoverage"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mountainRockiness"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mountainPosition"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mountainBiases"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndDisabledGroup();

            // River settings
            GUILayout.Space(5);
            SerializedProperty includeRiver = serializedObject.FindProperty("includeRiver");
            EditorGUI.BeginDisabledGroup(!includeRiver.boolValue);

            showRiverSettings = EditorGUILayout.Foldout(showRiverSettings, "River Settings", true);
            if (showRiverSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("riverWidth"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("riverDepth"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("riverMeandering"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("riverControlPoints"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("riverBiases"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndDisabledGroup();

            // Forest settings
            GUILayout.Space(5);
            SerializedProperty includeForest = serializedObject.FindProperty("includeForest");
            EditorGUI.BeginDisabledGroup(!includeForest.boolValue);

            showForestSettings = EditorGUILayout.Foldout(showForestSettings, "Forest Settings", true);
            if (showForestSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("forestDensity"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("forestCoverage"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("forestPosition"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("forestBiases"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndDisabledGroup();

            // Beach settings
            GUILayout.Space(5);
            SerializedProperty includeBeach = serializedObject.FindProperty("includeBeach");
            EditorGUI.BeginDisabledGroup(!includeBeach.boolValue);

            showBeachSettings = EditorGUILayout.Foldout(showBeachSettings, "Beach Settings", true);
            if (showBeachSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("beachWidth"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("beachSmoothness"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("beachBiases"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndDisabledGroup();

            // Advanced settings
            GUILayout.Space(10);
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultGroundHeight"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("noiseScale"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("featureBlending"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("createTransitionZones"));
                EditorGUI.indentLevel--;
            }

            // Apply changes
            serializedObject.ApplyModifiedProperties();

            // Show buttons for testing the preset
            GUILayout.Space(20);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Test Scene", GUILayout.Height(30)))
            {
                CreateTestScene();
            }

            if (GUILayout.Button("Apply to Current Scene", GUILayout.Height(30)))
            {
                ApplyToCurrentScene();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTerrainPreview(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                if (previewRenderer != null && previewMesh != null)
                {
                    // Set up renderer
                    previewRenderer.BeginPreview(rect, GUIStyle.none);

                    // Rotate the preview
                    rotationAngle += Time.deltaTime * 10f;
                    Quaternion rotation = Quaternion.Euler(0, rotationAngle, 0);

                    // Draw mesh
                    TerrainConstraintPreset preset = (TerrainConstraintPreset)target;
                    Material previewMaterial = new Material(Shader.Find("Standard"));

                    // Set color based on terrain features
                    if (preset != null)
                    {
                        previewMaterial.color = new Color(0.7f, 0.7f, 0.7f);

                        SerializedProperty includeMountain = serializedObject.FindProperty("includeMountainRange");
                        if (includeMountain.boolValue)
                        {
                            previewMaterial.color = new Color(0.5f, 0.5f, 0.5f);
                        }

                        SerializedProperty includeForest = serializedObject.FindProperty("includeForest");
                        if (includeForest.boolValue)
                        {
                            previewMaterial.color = new Color(0.2f, 0.6f, 0.2f);
                        }
                    }

                    previewRenderer.DrawMesh(previewMesh, Matrix4x4.TRS(Vector3.zero, rotation, Vector3.one), previewMaterial, 0);

                    // Render the preview
                    previewRenderer.camera.Render();
                    Texture resultRender = previewRenderer.EndPreview();
                    GUI.DrawTexture(rect, resultRender);

                    // Request repaint to keep the rotation animation going
                    EditorUtility.SetDirty(target);
                    Repaint();
                }
            }
        }

        private void CreatePreviewMesh()
        {
            // Create a simple terrain mesh for preview
            previewMesh = new Mesh();

            // Size of the terrain grid
            int gridSize = 10;
            float size = 10f;

            // Create vertices
            Vector3[] vertices = new Vector3[(gridSize + 1) * (gridSize + 1)];
            for (int z = 0; z <= gridSize; z++)
            {
                for (int x = 0; x <= gridSize; x++)
                {
                    float xPos = x * size / gridSize - size / 2;
                    float zPos = z * size / gridSize - size / 2;

                    // Create simple perlin noise height
                    float height = Mathf.PerlinNoise(x * 0.3f, z * 0.3f) * 2f;

                    vertices[z * (gridSize + 1) + x] = new Vector3(xPos, height, zPos);
                }
            }

            // Create triangles
            int[] triangles = new int[gridSize * gridSize * 6];
            int index = 0;
            for (int z = 0; z < gridSize; z++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    triangles[index++] = z * (gridSize + 1) + x;
                    triangles[index++] = z * (gridSize + 1) + x + 1;
                    triangles[index++] = (z + 1) * (gridSize + 1) + x;

                    triangles[index++] = (z + 1) * (gridSize + 1) + x;
                    triangles[index++] = z * (gridSize + 1) + x + 1;
                    triangles[index++] = (z + 1) * (gridSize + 1) + x + 1;
                }
            }

            // Create UVs
            Vector2[] uvs = new Vector2[(gridSize + 1) * (gridSize + 1)];
            for (int z = 0; z <= gridSize; z++)
            {
                for (int x = 0; x <= gridSize; x++)
                {
                    uvs[z * (gridSize + 1) + x] = new Vector2((float)x / gridSize, (float)z / gridSize);
                }
            }

            // Setup the mesh
            previewMesh.vertices = vertices;
            previewMesh.triangles = triangles;
            previewMesh.uv = uvs;
            previewMesh.RecalculateNormals();
        }

        private void CreateTestScene()
        {
            // TODO: Implement creating a test scene with this preset
            EditorUtility.DisplayDialog("Create Test Scene",
                "This would create a new scene with WFC components configured to use this preset.\n\n" +
                "Implementation pending.", "OK");
        }

        private void ApplyToCurrentScene()
        {
            TerrainConstraintPreset preset = (TerrainConstraintPreset)target;

            // Find TerrainPresetManager in the scene
            TerrainPresetManager presetManager = Object.FindObjectOfType<TerrainPresetManager>();

            if (presetManager != null)
            {
                // Apply the preset
                presetManager.ApplyPreset(preset);
                EditorUtility.DisplayDialog("Success", "Preset applied to the current scene.", "OK");
            }
            else
            {
                // No manager found, suggest creating one
                if (EditorUtility.DisplayDialog("No TerrainPresetManager Found",
                    "There is no TerrainPresetManager in the current scene. Would you like to create one?",
                    "Yes", "No"))
                {
                    // Create a TerrainPresetManager
                    GameObject managerObject = new GameObject("TerrainPresetManager");
                    presetManager = managerObject.AddComponent<TerrainPresetManager>();

                    // Try to find WFC components
                    presetManager.wfcGenerator = Object.FindObjectOfType<WFC.Generation.WFCGenerator>();

                    // Apply the preset
                    presetManager.ApplyPreset(preset);

                    EditorUtility.DisplayDialog("Success",
                        "Created TerrainPresetManager and applied the preset to the current scene.", "OK");
                }
            }
        }
    }
}