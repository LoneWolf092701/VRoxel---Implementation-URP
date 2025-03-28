// Assets/Scripts/Visualization/WFCVisualizer.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Boundary;

namespace Visualization
{
    public class WFCVisualizer : MonoBehaviour
    {
        [Header("WFC Component")]
        [SerializeField] private MonoBehaviour wfcComponent;
        [SerializeField] private int maxCellStates = 7; // Fallback if not available from component

        [Header("Visualization Settings")]
        [SerializeField] private bool showBoundaries = true;
        [SerializeField] private bool showUncollapsedCells = true;

        [Header("Appearance")]
        [SerializeField] private float cellSize = 1.0f;
        [SerializeField] private Material[] stateMaterials;
        [SerializeField] private Material uncollapsedMaterial;
        [SerializeField] private Material boundaryMaterial;

        // Interfaces for accessing WFC data
        private IChunkProvider chunkProvider;
        private int chunkSize = 8; // Default value if not available from component

        // Visualization data
        private Dictionary<Vector3Int, GameObject> visualCells = new Dictionary<Vector3Int, GameObject>();
        private Transform visualizationRoot;

        private void Start()
        {
            // Get interface from the component
            chunkProvider = wfcComponent as IChunkProvider;

            if (chunkProvider == null)
            {
                Debug.LogError("WFC component doesn't implement IChunkProvider! Please assign a component that implements this interface.");
                enabled = false;
                return;
            }

            // Try to get chunk size and max states from component if available
            if (wfcComponent is WFCGenerator generator)
            {
                chunkSize = generator.ChunkSize;
                maxCellStates = generator.MaxCellStates;
            }
            else if (wfcComponent is WFC.Testing.WFCTestController testController)
            {
                chunkSize = testController.ChunkSize;
                maxCellStates = testController.MaxStates;
            }

            // Create initial visualization
            CreateVisualization();
        }

        private void CreateVisualization()
        {
            // Clear any existing visualization
            ClearVisualization();

            // Create parent object for organization
            visualizationRoot = new GameObject("WFC_Visualization").transform;
            visualizationRoot.SetParent(transform);

            // Create a visualization for each chunk
            foreach (var chunkEntry in chunkProvider.GetChunks())
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
        }

        private void CreateCellVisualization(Cell cell, Vector3Int chunkPos, Vector3Int localPos, Transform parent, int localChunkSize)
        {
            // Create a global position key
            Vector3Int globalPos = new Vector3Int(
                chunkPos.x * localChunkSize + localPos.x,
                chunkPos.y * localChunkSize + localPos.y,
                chunkPos.z * localChunkSize + localPos.z
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
            Renderer renderer = cellObject.GetComponent<Renderer>();

            if (cell.CollapsedState.HasValue)
            {
                int state = cell.CollapsedState.Value;
                if (state < stateMaterials.Length)
                {
                    renderer.material = stateMaterials[state];
                }
            }
            else if (showUncollapsedCells)
            {
                renderer.material = uncollapsedMaterial;

                // Scale based on entropy
                float entropyFactor = Mathf.Max(0.1f, (float)cell.Entropy / maxCellStates);
                cellObject.transform.localScale = Vector3.one * cellSize * 0.5f * entropyFactor;
            }
            else
            {
                cellObject.SetActive(false);
            }

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

        private void ClearVisualization()
        {
            foreach (var cell in visualCells.Values)
            {
                if (cell != null)
                {
                    Destroy(cell);
                }
            }

            visualCells.Clear();

            // Find and destroy any existing visualization root
            if (visualizationRoot != null)
            {
                Destroy(visualizationRoot.gameObject);
                visualizationRoot = null;
            }
            else
            {
                // Find and destroy any existing visualization parent
                Transform oldViz = transform.Find("WFC_Visualization");
                if (oldViz != null)
                {
                    Destroy(oldViz.gameObject);
                }
            }
        }

        public void UpdateVisualization()
        {
            // Update all cell visualizations based on current state
            foreach (var chunkEntry in chunkProvider.GetChunks())
            {
                Vector3Int chunkPos = chunkEntry.Key;
                Chunk chunk = chunkEntry.Value;
                int localChunkSize = chunk.Size;

                for (int x = 0; x < localChunkSize; x++)
                {
                    for (int y = 0; y < localChunkSize; y++)
                    {
                        for (int z = 0; z < localChunkSize; z++)
                        {
                            // Calculate global position
                            Vector3Int globalPos = new Vector3Int(
                                chunkPos.x * localChunkSize + x,
                                chunkPos.y * localChunkSize + y,
                                chunkPos.z * localChunkSize + z
                            );

                            if (visualCells.TryGetValue(globalPos, out GameObject cellObject))
                            {
                                UpdateCellVisualization(chunk.GetCell(x, y, z), cellObject);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateCellVisualization(Cell cell, GameObject cellObject)
        {
            Renderer renderer = cellObject.GetComponent<Renderer>();

            if (cell.CollapsedState.HasValue)
            {
                int state = cell.CollapsedState.Value;
                if (state < stateMaterials.Length)
                {
                    renderer.material = stateMaterials[state];
                }
                cellObject.transform.localScale = Vector3.one * cellSize * 0.9f;
                cellObject.SetActive(true);
            }
            else if (showUncollapsedCells)
            {
                renderer.material = uncollapsedMaterial;

                // Scale based on entropy
                float entropyFactor = Mathf.Max(0.1f, (float)cell.Entropy / maxCellStates);
                cellObject.transform.localScale = Vector3.one * cellSize * 0.5f * entropyFactor;
                cellObject.SetActive(true);
            }
            else
            {
                cellObject.SetActive(false);
            }

            // Update boundary marker
            Transform boundaryMarker = cellObject.transform.Find("BoundaryMarker");
            if (boundaryMarker != null)
            {
                boundaryMarker.gameObject.SetActive(showBoundaries && cell.IsBoundary);
            }
        }

        [ContextMenu("Refresh Visualization")]
        public void RefreshVisualization()
        {
            ClearVisualization();
            CreateVisualization();
        }
    }
}