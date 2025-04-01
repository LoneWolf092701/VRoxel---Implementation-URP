using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Boundary;

public class WFCVisualizer : MonoBehaviour
{
    [Header("WFC Component")]
    [SerializeField] private WFCGenerator wfcGenerator;
    [SerializeField] private int maxCellStates = 7; // Fallback if not available from component

    [Header("Visualization Settings")]
    [SerializeField] private bool showBoundaries = true;
    [SerializeField] private bool showUncollapsedCells = true;

    [Header("Appearance")]
    [SerializeField] private float cellSize = 1.0f;
    [SerializeField] private Material[] stateMaterials;
    [SerializeField] private Material uncollapsedMaterial;
    [SerializeField] private Material boundaryMaterial;

    // Visualization data
    private Dictionary<Vector3Int, GameObject> visualCells = new Dictionary<Vector3Int, GameObject>();
    private Transform visualizationRoot;
    private bool isVisualizationCreated = false;

    private void Start()
    {
        // Find WFCGenerator if not set
        if (wfcGenerator == null)
        {
            wfcGenerator = FindObjectOfType<WFCGenerator>();
            if (wfcGenerator == null)
            {
                Debug.LogError("WFCVisualizer: WFCGenerator not found! Please assign a WFCGenerator reference.");
                return;
            }
        }

        // Try to get max states from component
        maxCellStates = wfcGenerator.MaxCellStates;

        // Create initial visualization
        CreateVisualization();
    }

    public void CreateVisualization()
    {
        // Clear any existing visualization
        ClearVisualization();

        if (wfcGenerator == null)
        {
            Debug.LogError("WFCVisualizer: Cannot create visualization, WFCGenerator is null!");
            return;
        }

        // Get chunks from WFCGenerator
        var chunks = wfcGenerator.GetChunks();
        if (chunks == null || chunks.Count == 0)
        {
            Debug.LogWarning("WFCVisualizer: No chunks available to visualize.");
            return;
        }

        // Create parent object for organization
        visualizationRoot = new GameObject("WFC_Visualization").transform;
        visualizationRoot.SetParent(transform);

        // Create a visualization for each chunk
        foreach (var chunkEntry in chunks)
        {
            Vector3Int chunkPos = chunkEntry.Key;
            Chunk chunk = chunkEntry.Value;

            GameObject chunkObject = new GameObject($"Chunk_{chunkPos.x}_{chunkPos.y}_{chunkPos.z}");
            chunkObject.transform.SetParent(visualizationRoot);

            // Set position based on chunk position and size
            chunkObject.transform.position = new Vector3(
                chunkPos.x * chunk.Size * cellSize,
                chunkPos.y * chunk.Size * cellSize,
                chunkPos.z * chunk.Size * cellSize
            );

            // Create visualization for each cell
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);
                        CreateCellVisualization(cell, chunkPos, new Vector3Int(x, y, z), chunkObject.transform, chunk.Size);
                    }
                }
            }
        }

        isVisualizationCreated = true;
        Debug.Log($"WFCVisualizer: Created visualization with {visualCells.Count} cells");
    }

    private void CreateCellVisualization(Cell cell, Vector3Int chunkPos, Vector3Int localPos, Transform parent, int chunkSize)
    {
        // Create a global position key
        Vector3Int globalPos = new Vector3Int(
            chunkPos.x * chunkSize + localPos.x,
            chunkPos.y * chunkSize + localPos.y,
            chunkPos.z * chunkSize + localPos.z
        );

        // Create cell object
        GameObject cellObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cellObject.name = $"Cell_{localPos.x}_{localPos.y}_{localPos.z}";
        cellObject.transform.SetParent(parent);

        // Position within the chunk
        cellObject.transform.localPosition = new Vector3(
            localPos.x * cellSize,
            localPos.y * cellSize,
            localPos.z * cellSize
        );

        // Scale to cell size
        cellObject.transform.localScale = Vector3.one * cellSize * 0.9f; // Slightly smaller to see grid

        // Set material based on cell state
        UpdateCellVisualization(cell, cellObject);

        // Mark boundary cells
        if (showBoundaries && cell.IsBoundary)
        {
            GameObject boundaryMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            boundaryMarker.name = "BoundaryMarker";
            boundaryMarker.transform.SetParent(cellObject.transform);
            boundaryMarker.transform.localPosition = Vector3.zero;
            boundaryMarker.transform.localScale = Vector3.one * 0.3f;

            Renderer boundaryRenderer = boundaryMarker.GetComponent<Renderer>();
            boundaryRenderer.material = boundaryMaterial;
        }

        // Store for later updates
        visualCells[globalPos] = cellObject;
    }

    private void UpdateCellVisualization(Cell cell, GameObject cellObject)
    {
        if (cellObject == null || cell == null) return;

        Renderer renderer = cellObject.GetComponent<Renderer>();

        if (cell.CollapsedState.HasValue)
        {
            // Cell is collapsed to a specific state
            int state = cell.CollapsedState.Value;
            if (state < stateMaterials.Length && stateMaterials[state] != null)
            {
                renderer.material = stateMaterials[state];
            }
            else
            {
                // Fallback if material not available
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = new Color(
                    (float)state / maxCellStates,
                    0.5f,
                    1.0f - (float)state / maxCellStates
                );
            }
            cellObject.transform.localScale = Vector3.one * cellSize * 0.9f;
            cellObject.SetActive(true);
        }
        else if (showUncollapsedCells)
        {
            // Show uncollapsed cells, scale by entropy
            if (uncollapsedMaterial != null)
            {
                renderer.material = uncollapsedMaterial;
            }
            else
            {
                // Fallback material
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = new Color(0.7f, 0.7f, 0.7f, 0.5f);
            }

            // Scale based on entropy
            float entropyFactor = Mathf.Max(0.1f, (float)cell.Entropy / maxCellStates);
            cellObject.transform.localScale = Vector3.one * cellSize * 0.5f * entropyFactor;
            cellObject.SetActive(true);
        }
        else
        {
            // Hide uncollapsed cells
            cellObject.SetActive(false);
        }
    }

    public void UpdateVisualization()
    {
        if (wfcGenerator == null) return;

        // Create visualization if it doesn't exist
        if (!isVisualizationCreated)
        {
            CreateVisualization();
            return;
        }

        // Get updated chunks
        var chunks = wfcGenerator.GetChunks();
        if (chunks == null) return;

        // Update all cell visualizations
        foreach (var chunkEntry in chunks)
        {
            Vector3Int chunkPos = chunkEntry.Key;
            Chunk chunk = chunkEntry.Value;

            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);

                        // Calculate global position
                        Vector3Int globalPos = new Vector3Int(
                            chunkPos.x * chunk.Size + x,
                            chunkPos.y * chunk.Size + y,
                            chunkPos.z * chunk.Size + z
                        );

                        // Update visualization if it exists
                        if (visualCells.TryGetValue(globalPos, out GameObject cellObject))
                        {
                            UpdateCellVisualization(cell, cellObject);
                        }
                    }
                }
            }
        }
    }

    [ContextMenu("Refresh Visualization")]
    public void RefreshVisualization()
    {
        ClearVisualization();
        CreateVisualization();
    }

    public void ClearVisualization()
    {
        foreach (var cell in visualCells.Values)
        {
            if (cell != null)
            {
                DestroyImmediate(cell);
            }
        }

        visualCells.Clear();

        // Find and destroy any existing visualization root
        if (visualizationRoot != null)
        {
            DestroyImmediate(visualizationRoot.gameObject);
            visualizationRoot = null;
        }
        else
        {
            // Find and destroy any existing visualization parent
            Transform oldViz = transform.Find("WFC_Visualization");
            if (oldViz != null)
            {
                DestroyImmediate(oldViz.gameObject);
            }
        }

        isVisualizationCreated = false;
    }

    public void ToggleVisualization(bool visible)
    {
        if (visualizationRoot != null)
        {
            visualizationRoot.gameObject.SetActive(visible);
        }
    }

    private void OnValidate()
    {
        // Update the visualization if properties change in the inspector
        if (isVisualizationCreated && Application.isPlaying)
        {
            UpdateVisualization();
        }
    }

    private void OnDestroy()
    {
        // Clean up visualization when destroyed
        ClearVisualization();
    }
}