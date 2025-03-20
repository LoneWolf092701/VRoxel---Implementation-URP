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
        }

        private void StepWFC()
        {
            // Run one step of the WFC algorithm
            // This would require adding a public method to WFCTestController
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

        private void ToggleCellVisibility(bool visible)
        {
            // Show/hide cell visualizations
            Transform vizParent = wfcController.transform.Find("WFC_Visualization");
            if (vizParent != null)
            {
                vizParent.gameObject.SetActive(visible);
            }
        }

        private void ToggleMeshVisibility(bool visible)
        {
            // Show/hide generated meshes
            if (meshGenerator != null)
            {
                meshGenerator.gameObject.SetActive(visible);
            }
        }
    }
}