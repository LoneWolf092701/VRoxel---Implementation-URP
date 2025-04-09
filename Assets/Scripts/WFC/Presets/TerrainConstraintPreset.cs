using System;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;

namespace WFC.Presets
{
    /// <summary>
    /// ScriptableObject for storing terrain-specific constraint configurations.
    /// Create assets from this class to define reusable terrain presets.
    /// </summary>
    [CreateAssetMenu(fileName = "New Terrain Preset", menuName = "WFC/Terrain Constraint Preset")]
    public class TerrainConstraintPreset : ScriptableObject
    {
        [Header("Preset Information")]
        [SerializeField] private string presetName = "New Terrain Preset";
        [SerializeField][TextArea(3, 5)] private string description = "Enter description of this terrain preset...";
        [SerializeField] private Texture2D previewImage;

        [Header("Global Features")]
        [SerializeField] private bool includeMountainRange = true;
        [SerializeField] private bool includeRiver = true;

        [Header("Mountain Settings")]
        [SerializeField] private float mountainHeight = 0.7f;
        [SerializeField] private float mountainCoverage = 0.3f;
        [SerializeField][Range(0f, 1f)] private float mountainRockiness = 0.7f;
        [SerializeField] private Vector3 mountainPosition = new Vector3(0.25f, 0.5f, 0.5f);

        [Header("Advanced Settings")]
        [SerializeField] private float defaultGroundHeight = 0.2f;
        [SerializeField][Range(0f, 1f)] private float noiseScale = 0.05f;
        [SerializeField][Range(0f, 1f)] private float featureBlending = 0.7f;
        [SerializeField] private bool createTransitionZones = true;

        // State biases for different terrain features
        [Serializable]
        private class StateBiasMapping
        {
            public string name;
            public int stateId;
            public float bias;
        }

        [Header("State Biases")]
        [SerializeField] private List<StateBiasMapping> mountainBiases = new List<StateBiasMapping>();
        [SerializeField] private List<StateBiasMapping> riverBiases = new List<StateBiasMapping>();

        /// <summary>
        /// Applies this preset to a HierarchicalConstraintSystem
        /// </summary>
        public void ApplyToConstraintSystem(HierarchicalConstraintSystem constraintSystem, Vector3Int worldSize, int chunkSize)
        {
            if (constraintSystem == null)
                return;

            // Clear existing constraints first
            constraintSystem.ClearConstraints();

            // Create ground level constraint (always present)
            CreateGroundLevelConstraint(constraintSystem, worldSize, chunkSize);

            // Apply each enabled feature
            if (includeMountainRange)
                CreateMountainRangeConstraint(constraintSystem, worldSize, chunkSize);

            // Create transition zones between different biomes if enabled
            if (createTransitionZones)
                CreateTransitionZones(constraintSystem, worldSize, chunkSize);
        }

        private void CreateGroundLevelConstraint(HierarchicalConstraintSystem constraintSystem, Vector3Int worldSize, int chunkSize)
        {
            GlobalConstraint groundConstraint = new GlobalConstraint
            {
                Name = "Ground Level",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(worldSize.x * chunkSize / 2, 0, worldSize.z * chunkSize / 2),
                WorldSize = new Vector3(worldSize.x * chunkSize, 0, worldSize.z * chunkSize),
                BlendRadius = chunkSize,
                Strength = 0.8f,
                MinHeight = 0,
                MaxHeight = worldSize.y * chunkSize,
                NoiseScale = noiseScale
            };

            // Add default state biases for ground
            groundConstraint.StateBiases[0] = -0.8f; // Empty (negative bias)
            groundConstraint.StateBiases[1] = 0.6f;  // Ground (positive bias)

            constraintSystem.AddGlobalConstraint(groundConstraint);
        }

        private void CreateMountainRangeConstraint(HierarchicalConstraintSystem constraintSystem, Vector3Int worldSize, int chunkSize)
        {
            // Calculate dimensions based on settings
            float worldSizeX = worldSize.x * chunkSize;
            float worldSizeY = worldSize.y * chunkSize;
            float worldSizeZ = worldSize.z * chunkSize;

            GlobalConstraint mountainConstraint = new GlobalConstraint
            {
                Name = "Mountain Range",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(
                    worldSizeX * mountainPosition.x,
                    worldSizeY * mountainPosition.y,
                    worldSizeZ * mountainPosition.z
                ),
                WorldSize = new Vector3(
                    worldSizeX * mountainCoverage,
                    worldSizeY * mountainHeight,
                    worldSizeZ * mountainCoverage
                ),
                BlendRadius = chunkSize * 2,
                Strength = 0.7f,
                MinHeight = worldSizeY * 0.3f,
                MaxHeight = worldSizeY
            };

            // Apply state biases from preset
            if (mountainBiases.Count > 0)
            {
                foreach (var bias in mountainBiases)
                {
                    mountainConstraint.StateBiases[bias.stateId] = bias.bias;
                }
            }
            else
            {
                // Default biases if none specified
                mountainConstraint.StateBiases[0] = -0.5f; // Empty (negative at lower elevations)
                mountainConstraint.StateBiases[1] = 0.2f;  // Ground (some ground)
                mountainConstraint.StateBiases[4] = mountainRockiness;  // Rock (stronger bias based on rockiness setting)
            }

            constraintSystem.AddGlobalConstraint(mountainConstraint);
        }
        private void CreateTransitionZones(HierarchicalConstraintSystem constraintSystem, Vector3Int worldSize, int chunkSize)
        {
            // Only create transition zones if relevant features are enabled
            if (includeMountainRange)
            {
                RegionConstraint mountainForestTransition = new RegionConstraint
                {
                    Name = "Mountain-Forest Transition",
                    Type = RegionType.Transition,
                    ChunkPosition = new Vector3Int(1, 1, 0),
                    ChunkSize = new Vector3Int(1, 1, 1),
                    Strength = featureBlending,
                    Gradient = 0.5f,
                    SourceState = 4, // Rock
                    TargetState = 6  // Tree
                };

                constraintSystem.AddRegionConstraint(mountainForestTransition);
            }

            if (includeRiver)
            {
                RegionConstraint riverBeachTransition = new RegionConstraint
                {
                    Name = "River-Beach Transition",
                    Type = RegionType.Transition,
                    ChunkPosition = new Vector3Int(1, 0, 0),
                    ChunkSize = new Vector3Int(1, 1, 2),
                    InternalOrigin = new Vector3(0.3f, 0, 0.3f),
                    InternalSize = new Vector3(0.4f, 1, 0.4f),
                    Strength = featureBlending,
                    Gradient = 0.3f,
                    SourceState = 3, // Water
                    TargetState = 5  // Sand
                };

                constraintSystem.AddRegionConstraint(riverBeachTransition);
            }
        }

        /// <summary>
        /// Create a preset with default settings for mountains
        /// </summary>
        public static TerrainConstraintPreset CreateMountainPreset()
        {
            var preset = CreateInstance<TerrainConstraintPreset>();
            preset.presetName = "Mountain Terrain";
            preset.description = "Rugged mountain terrain with rocky peaks and forest valleys";
            preset.includeMountainRange = true;
            preset.includeRiver = true;
            preset.mountainHeight = 0.8f;
            preset.mountainRockiness = 0.85f;
            return preset;
        }

    }
}