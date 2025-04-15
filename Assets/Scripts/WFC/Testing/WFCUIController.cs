using UnityEngine;
using WFC.MarchingCubes;
using WFC.Generation;
using WFC.Chunking;

public class WFCUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WFCGenerator wfcGenerator;
    [SerializeField] private WFCVisualizer wfcVisualizer;
    [SerializeField] private MeshGenerator meshGenerator;
    [SerializeField] private ChunkManager chunkGenerator;

    [Header("Visualization")]
    [SerializeField] private bool showCellVisualization = true;
    [SerializeField] private bool showMeshVisualization = true;

    [Header("Keyboard Controls")]
    [Tooltip("Key to execute a generation step")]
    [SerializeField] private KeyCode stepKey = KeyCode.Alpha1;       // Key 1

    [Tooltip("Key to reset the WFC algorithm")]
    [SerializeField] private KeyCode resetKey = KeyCode.Alpha2;      // Key 2

    [Tooltip("Key to generate mesh from current WFC state")]
    [SerializeField] private KeyCode generateMeshKey = KeyCode.Alpha3; // Key 3

    [Tooltip("Key to toggle cell visualization")]
    [SerializeField] private KeyCode toggleCellsKey = KeyCode.Alpha4;  // Key 4

    [Tooltip("Key to toggle mesh visualization")]
    [SerializeField] private KeyCode toggleMeshKey = KeyCode.Alpha5;   // Key 5

    private void Start()
    {
        // Find references if not set
        if (wfcGenerator == null)
            wfcGenerator = FindObjectOfType<WFCGenerator>();

        if (wfcVisualizer == null)
            wfcVisualizer = FindObjectOfType<WFCVisualizer>();

        if (meshGenerator == null)
            meshGenerator = FindObjectOfType<MeshGenerator>();

        // Set initial visualization states
        if (wfcVisualizer != null)
            wfcVisualizer.ToggleVisualization(showCellVisualization);

        if (meshGenerator != null && meshGenerator.gameObject != null)
            meshGenerator.gameObject.SetActive(showMeshVisualization);

        // Log available controls for testing
        LogKeyboardControls();
    }

    private void LogKeyboardControls()
    {
        Debug.Log("WFC UI Controller - Keyboard Controls:");
        Debug.Log($"  {stepKey} - Step WFC Generation");
        Debug.Log($"  {resetKey} - Reset WFC");
        Debug.Log($"  {generateMeshKey} - Generate Mesh");
        Debug.Log($"  {toggleCellsKey} - Toggle Cell Visualization");
        Debug.Log($"  {toggleMeshKey} - Toggle Mesh Visualization");
    }

    private void Update()
    {
        // Handle keyboard controls
        if (Input.GetKeyDown(stepKey))
        {
            StepWFC();
        }

        if (Input.GetKeyDown(resetKey))
        {
            ResetWFC();
        }

        if (Input.GetKeyDown(generateMeshKey))
        {
            GenerateMesh();
        }

        if (Input.GetKeyDown(toggleCellsKey))
        {
            ToggleCellVisibility();
        }

        if (Input.GetKeyDown(toggleMeshKey))
        {
            ToggleMeshVisibility();
        }
        if (Input.GetKeyDown(KeyCode.K))
        {
            ForceCreateChunk();
        }

        // In WFCUIController's Update method
        if (Input.GetKeyDown(KeyCode.M))
        {
            Debug.Log("Manual mesh generation triggered");
            if (meshGenerator != null)
            {
                var chunks = wfcGenerator.GetChunks();
                Debug.Log($"Generating meshes for {chunks.Count} chunks");
                foreach (var chunkEntry in chunks)
                {
                    meshGenerator.GenerateChunkMesh(chunkEntry.Key, chunkEntry.Value);
                }
            }
        }
    }

    [ContextMenu("Step WFC")]
    public void StepWFC()
    {
        if (wfcGenerator == null)
        {
            Debug.LogWarning("WFC Generator reference is missing");
            return;
        }

        if (wfcGenerator.GetType().GetMethod("RunSingleStep") != null)
        {
            wfcGenerator.GetType().GetMethod("RunSingleStep").Invoke(wfcGenerator, null);
            Debug.Log("WFC step executed");
        }
        else
        {
            Debug.LogWarning("WFCGenerator does not have a RunSingleStep method. Add this method to your WFCGenerator.");
        }

        // Update visualizations
        if (wfcVisualizer != null)
        {
            wfcVisualizer.UpdateVisualization();
        }
    }

    [ContextMenu("Reset WFC")]
    public void ResetWFC()
    {
        if (wfcGenerator != null)
        {
            //wfcGenerator.ResetGeneration();
            Debug.Log("WFC algorithm reset");
        }
        else
        {
            Debug.LogWarning("WFC Generator reference is missing");
        }

        if (meshGenerator != null)
        {
            meshGenerator.ClearMeshes();
            Debug.Log("Meshes cleared");
        }

        // Refresh visualization
        if (wfcVisualizer != null)
        {
            wfcVisualizer.RefreshVisualization();
        }
    }

    [ContextMenu("Generate Mesh")]
    public void GenerateMesh()
    {
        if (meshGenerator != null)
        {
            meshGenerator.GenerateAllMeshes();
            Debug.Log("Generating meshes from current WFC state");
        }
        else
        {
            Debug.LogWarning("Mesh Generator reference is missing");
        }
    }

    [ContextMenu("Toggle Cell Visualization")]
    public void ToggleCellVisibility()
    {
        showCellVisualization = !showCellVisualization;

        if (wfcVisualizer != null)
        {
            wfcVisualizer.ToggleVisualization(showCellVisualization);
            Debug.Log(showCellVisualization ?
                "Cell visualization enabled" :
                "Cell visualization disabled");
        }
        else
        {
            Debug.LogWarning("WFC Visualizer reference is missing");
        }
    }

    [ContextMenu("Toggle Mesh Visualization")]
    public void ToggleMeshVisibility()
    {
        showMeshVisualization = !showMeshVisualization;

        if (meshGenerator != null && meshGenerator.gameObject != null)
        {
            meshGenerator.gameObject.SetActive(showMeshVisualization);
            Debug.Log(showMeshVisualization ?
                "Mesh visualization enabled" :
                "Mesh visualization disabled");
        }
        else
        {
            Debug.LogWarning("Mesh Generator reference is missing");
        }
    }
    [ContextMenu("Force Generation Chunk")]
    public void ForceCreateChunk()
    {
        chunkGenerator.CreateChunk(Vector3Int.zero);
        Debug.Log("Manually created chunk at (0,0,0)");
    }
}