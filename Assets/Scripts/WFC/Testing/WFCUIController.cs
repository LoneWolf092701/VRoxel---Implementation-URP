// Assets/Scripts/WFC/Testing/WFCUIController.cs
using UnityEngine;
using UnityEngine.UI;
using WFC.MarchingCubes;

namespace WFC.Testing
{
    public class WFCUIController : MonoBehaviour
    {
        [SerializeField] private WFCTestController wfcController;
        [SerializeField] private MeshGenerator meshGenerator;

        [SerializeField] private Button stepButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button generateMeshButton;
        [SerializeField] private Toggle showCellsToggle;
        [SerializeField] private Toggle showMeshToggle;

        [Header("Keyboard Controls")]
        [SerializeField] private KeyCode stepKey = KeyCode.Alpha1;       // Key 1
        [SerializeField] private KeyCode resetKey = KeyCode.Alpha2;      // Key 2
        [SerializeField] private KeyCode generateMeshKey = KeyCode.Alpha3; // Key 3
        [SerializeField] private KeyCode toggleCellsKey = KeyCode.Alpha4;  // Key 4
        [SerializeField] private KeyCode toggleMeshKey = KeyCode.Alpha5;   // Key 5
        [SerializeField] private KeyCode runTestKey = KeyCode.Alpha6;      // Key 6

        private void Start()
        {
            // Setup button listeners
            if (stepButton != null)
            {
                stepButton.onClick.AddListener(StepWFC);
            }

            if (resetButton != null)
            {
                resetButton.onClick.AddListener(ResetWFC);
            }

            if (generateMeshButton != null)
            {
                generateMeshButton.onClick.AddListener(GenerateMesh);
            }

            if (showCellsToggle != null)
            {
                showCellsToggle.onValueChanged.AddListener(ToggleCellVisibility);
            }

            if (showMeshToggle != null)
            {
                showMeshToggle.onValueChanged.AddListener(ToggleMeshVisibility);
            }

            Debug.Log("WFC UI Controller initialized. Keyboard controls:");
            Debug.Log($"  {stepKey} - Step WFC");
            Debug.Log($"  {resetKey} - Reset WFC");
            Debug.Log($"  {generateMeshKey} - Generate Mesh");
            Debug.Log($"  {toggleCellsKey} - Toggle Cell Visibility");
            Debug.Log($"  {toggleMeshKey} - Toggle Mesh Visibility");
            Debug.Log($"  {runTestKey} - Run Boundary Test");
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

            if (Input.GetKeyDown(toggleCellsKey) && showCellsToggle != null)
            {
                showCellsToggle.isOn = !showCellsToggle.isOn;
            }

            if (Input.GetKeyDown(toggleMeshKey) && showMeshToggle != null)
            {
                showMeshToggle.isOn = !showMeshToggle.isOn;
            }

            if (Input.GetKeyDown(runTestKey))
            {
                RunBoundaryTest();
            }

            // Note: Space and R keys are already handled by WFCTestController
            // This provides alternative keys and additional functionality
        }

        private void StepWFC()
        {
            // Run one step of the WFC algorithm
            if (wfcController != null)
            {
                wfcController.RunOneStep();
            }
        }

        private void ResetWFC()
        {
            // Reset the WFC algorithm
            if (wfcController != null)
            {
                wfcController.ResetGeneration();
            }

            // Clear meshes
            if (meshGenerator != null)
            {
                meshGenerator.ClearMeshes();
            }
        }

        private void GenerateMesh()
        {
            if (meshGenerator != null)
            {
                meshGenerator.GenerateAllMeshes();
            }
        }

        private void RunBoundaryTest()
        {
            // Find and run boundary test if available
            var boundaryTest = FindAnyObjectByType<BoundaryCoherenceTest>();
            if (boundaryTest != null)
            {
                Debug.Log("Running boundary coherence test...");
                boundaryTest.RunBoundaryTest();
            }
            else
            {
                Debug.LogWarning("BoundaryCoherenceTest component not found in the scene.");
            }
        }

        private void ToggleCellVisibility(bool visible)
        {
            // Show/hide cell visualizations
            Transform vizParent = wfcController.transform.Find("WFC_Visualization");
            if (vizParent != null)
            {
                vizParent.gameObject.SetActive(visible);
                Debug.Log(visible ? "Cell visualization enabled" : "Cell visualization disabled");
            }
        }

        private void ToggleMeshVisibility(bool visible)
        {
            // Show/hide generated meshes
            if (meshGenerator != null)
            {
                meshGenerator.gameObject.SetActive(visible);
                Debug.Log(visible ? "Mesh visualization enabled" : "Mesh visualization disabled");
            }
        }
    }
}