using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;

namespace WFC.Presets
{
    /// <summary>
    /// Manager component that loads and applies terrain constraint presets
    /// to the WFC generation system.
    /// </summary>
    public class TerrainPresetManager : MonoBehaviour
    {
        [Header("WFC References")]
        [SerializeField] public WFCGenerator wfcGenerator;

        [Header("Presets")]
        [SerializeField] public TerrainConstraintPreset defaultPreset;      // changed
        [SerializeField] private List<TerrainConstraintPreset> availablePresets = new List<TerrainConstraintPreset>();

        [Header("Debug")]
        [SerializeField] private bool applyOnStart = false;
        [SerializeField] private bool autoRegenerateOnPresetChange = false;

        // Currently active preset
        private TerrainConstraintPreset activePreset;

        private void Start()
        {
            if (applyOnStart && defaultPreset != null)
            {
                ApplyPreset(defaultPreset);
            }
        }

        /// <summary>
        /// Apply a terrain preset to the current WFC system
        /// </summary>
        public void ApplyPreset(TerrainConstraintPreset preset)
        {
            if (preset == null)
            {
                Debug.LogError("TerrainPresetManager: Cannot apply null preset");
                return;
            }

            HierarchicalConstraintSystem constraintSystem = GetConstraintSystem();
            if (constraintSystem == null)
            {
                Debug.LogError("TerrainPresetManager: Could not find HierarchicalConstraintSystem");
                return;
            }

            // Get world parameters
            Vector3Int worldSize = GetWorldSize();
            int chunkSize = GetChunkSize();

            // Apply the preset
            preset.ApplyToConstraintSystem(constraintSystem, worldSize, chunkSize);
            activePreset = preset;

            Debug.Log($"Applied terrain preset: {preset.name}");

            // Auto-regenerate if needed
            if (autoRegenerateOnPresetChange)
            {
                RegenerateWorld();
            }
        }

        /// <summary>
        /// Regenerate the world with the current settings
        /// </summary>
        public void RegenerateWorld()
        {
            if (wfcGenerator != null)
            {
                // This would need a method like ResetGeneration on WFCGenerator
                // or you could destroy and recreate chunks
                Debug.Log("Regenerating world with WFCGenerator");

                // For now, just log that full regeneration isn't implemented
                Debug.LogWarning("Full regeneration for WFCGenerator not implemented");
            }
        }

        /// <summary>
        /// Get a reference to the active HierarchicalConstraintSystem
        /// </summary>
        private HierarchicalConstraintSystem GetConstraintSystem()
        {
            if (wfcGenerator != null)
            {
                return wfcGenerator.GetHierarchicalConstraintSystem();
            }

            return null;
        }

        /// <summary>
        /// Get the current world size
        /// </summary>
        private Vector3Int GetWorldSize()
        { 
            if (wfcGenerator != null)
            {
                return wfcGenerator.WorldSize;
            }

            return new Vector3Int(2, 2, 1); // Default fallback
        }

        /// <summary>
        /// Get the current chunk size
        /// </summary>
        private int GetChunkSize()
        {
            if (wfcGenerator != null)
            {
                return wfcGenerator.ChunkSize;
            }

            return 8; // Default fallback
        }

        /// <summary>
        /// Find all available presets in the project
        /// </summary>
        [ContextMenu("Find All Presets")]
        public void FindAllPresets()
        {
            availablePresets.Clear();
            TerrainConstraintPreset[] presets = Resources.LoadAll<TerrainConstraintPreset>("Presets/Terrain");
            availablePresets.AddRange(presets);
            Debug.Log($"Found {availablePresets.Count} terrain presets");
        }

        /// <summary>
        /// Create default presets in the project
        /// </summary>
        [ContextMenu("Create Default Presets")]
        public void CreateDefaultPresets()
        {
#if UNITY_EDITOR
            // Ensure directories exist
            if (!System.IO.Directory.Exists("Assets/Resources"))
                System.IO.Directory.CreateDirectory("Assets/Resources");

            if (!System.IO.Directory.Exists("Assets/Resources/Presets"))
                System.IO.Directory.CreateDirectory("Assets/Resources/Presets");

            if (!System.IO.Directory.Exists("Assets/Resources/Presets/Terrain"))
                System.IO.Directory.CreateDirectory("Assets/Resources/Presets/Terrain");

            // Create mountain preset
            var mountainPreset = TerrainConstraintPreset.CreateMountainPreset();
            UnityEditor.AssetDatabase.CreateAsset(mountainPreset, "Assets/Resources/Presets/Terrain/Mountain_Terrain.asset");

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();

            Debug.Log("Created default terrain presets in Resources/Presets/Terrain folder");

            // Reload available presets
            FindAllPresets();
#else
            Debug.LogWarning("CreateDefaultPresets can only be used in the Unity Editor");
#endif
        }
    }
}