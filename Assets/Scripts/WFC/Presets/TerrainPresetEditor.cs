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
    }
}