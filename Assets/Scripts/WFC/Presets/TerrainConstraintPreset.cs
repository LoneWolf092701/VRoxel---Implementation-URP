// Assets/Scripts/WFC/Presets/TerrainConstraintPreset.cs
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
        [SerializeField] private bool includeForest = true;
        [SerializeField] private bool includeBeach = true;

        [Header("Mountain Settings")]
        [SerializeField] private float mountainHeight = 0.7f;
        [SerializeField] private float mountainCoverage = 0.3f;
        [SerializeField][Range(0f, 1f)] private float mountainRockiness = 0.7f;
        [SerializeField] private Vector3 mountainPosition = new Vector3(0.25f, 0.5f, 0.5f);

        [Header("River Settings")]
        [SerializeField] private float riverWidth = 4f;
        [SerializeField] private float riverDepth = 2f;
        [SerializeField] private bool riverMeandering = true;
        [SerializeField][Range(2, 8)] private int riverControlPoints = 4;

        [Header("Forest Settings")]
        [SerializeField][Range(0f, 1f)] private float forestDensity = 0.6f;
        [SerializeField] private float forestCoverage = 0.4f;
        [SerializeField] private Vector3 forestPosition = new Vector3(0.7f, 0.2f, 0.3f);

        [Header("Beach Settings")]
        [SerializeField] private float beachWidth = 3f;
        [SerializeField] private float beachSmoothness = 0.5f;

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
        [SerializeField] private List<StateBiasMapping> forestBiases = new List<StateBiasMapping>();
        [SerializeField] private List<StateBiasMapping> beachBiases = new List<StateBiasMapping>();

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

            if (includeRiver)
                CreateRiverConstraint(constraintSystem, worldSize, chunkSize);

            if (includeForest)
                CreateForestConstraint(constraintSystem, worldSize, chunkSize);

            if (includeBeach)
                CreateBeachConstraint(constraintSystem, worldSize, chunkSize);

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

        private void CreateRiverConstraint(HierarchicalConstraintSystem constraintSystem, Vector3Int worldSize, int chunkSize)
        {
            float worldSizeX = worldSize.x * chunkSize;
            float worldSizeY = worldSize.y * chunkSize;
            float worldSizeZ = worldSize.z * chunkSize;

            GlobalConstraint riverConstraint = new GlobalConstraint
            {
                Name = "River",
                Type = ConstraintType.RiverPath,
                Strength = 0.8f,
                PathWidth = riverWidth,
                BlendRadius = chunkSize
            };

            // Add control points for the river path
            if (riverMeandering)
            {
                // Create a meandering river with more control points
                for (int i = 0; i <= riverControlPoints; i++)
                {
                    float t = i / (float)riverControlPoints;
                    float xPos = Mathf.Lerp(0, worldSizeX, t);

                    // Add some randomized meandering
                    float zOffset = Mathf.Sin(t * Mathf.PI * 2) * worldSizeZ * 0.15f;
                    float zPos = worldSizeZ * 0.5f + zOffset;

                    // Gradually decrease height
                    float yPos = Mathf.Lerp(riverDepth, 0, t);

                    riverConstraint.ControlPoints.Add(new Vector3(xPos, yPos, zPos));
                }
            }
            else
            {
                // Create a straighter river with fewer control points
                riverConstraint.ControlPoints.Add(new Vector3(0, riverDepth, worldSizeZ * 0.3f));
                riverConstraint.ControlPoints.Add(new Vector3(worldSizeX * 0.3f, riverDepth * 0.5f, worldSizeZ * 0.5f));
                riverConstraint.ControlPoints.Add(new Vector3(worldSizeX * 0.7f, riverDepth * 0.25f, worldSizeZ * 0.6f));
                riverConstraint.ControlPoints.Add(new Vector3(worldSizeX, 0, worldSizeZ * 0.7f));
            }

            // Apply state biases from preset
            if (riverBiases.Count > 0)
            {
                foreach (var bias in riverBiases)
                {
                    riverConstraint.StateBiases[bias.stateId] = bias.bias;
                }
            }
            else
            {
                // Default biases if none specified
                riverConstraint.StateBiases[3] = 0.9f;  // Water (strong positive bias)
                riverConstraint.StateBiases[5] = 0.4f;  // Sand (moderate positive bias)
            }

            constraintSystem.AddGlobalConstraint(riverConstraint);
        }

        private void CreateForestConstraint(HierarchicalConstraintSystem constraintSystem, Vector3Int worldSize, int chunkSize)
        {
            float worldSizeX = worldSize.x * chunkSize;
            float worldSizeY = worldSize.y * chunkSize;
            float worldSizeZ = worldSize.z * chunkSize;

            GlobalConstraint forestConstraint = new GlobalConstraint
            {
                Name = "Forest",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(
                    worldSizeX * forestPosition.x,
                    worldSizeY * forestPosition.y,
                    worldSizeZ * forestPosition.z
                ),
                WorldSize = new Vector3(
                    worldSizeX * forestCoverage,
                    worldSizeY * 0.3f,
                    worldSizeZ * forestCoverage
                ),
                BlendRadius = chunkSize * 1.5f,
                Strength = forestDensity
            };

            // Apply state biases from preset
            if (forestBiases.Count > 0)
            {
                foreach (var bias in forestBiases)
                {
                    forestConstraint.StateBiases[bias.stateId] = bias.bias;
                }
            }
            else
            {
                // Default biases if none specified
                forestConstraint.StateBiases[2] = 0.5f;  // Grass (moderate positive bias)
                forestConstraint.StateBiases[6] = forestDensity;  // Tree (bias based on density setting)
            }

            constraintSystem.AddGlobalConstraint(forestConstraint);
        }

        private void CreateBeachConstraint(HierarchicalConstraintSystem constraintSystem, Vector3Int worldSize, int chunkSize)
        {
            float worldSizeX = worldSize.x * chunkSize;
            float worldSizeY = worldSize.y * chunkSize;
            float worldSizeZ = worldSize.z * chunkSize;

            GlobalConstraint beachConstraint = new GlobalConstraint
            {
                Name = "Beach",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(worldSizeX * 0.6f, worldSizeY * 0.1f, worldSizeZ * 0.8f),
                WorldSize = new Vector3(worldSizeX * 0.3f, worldSizeY * 0.1f, worldSizeZ * 0.3f),
                BlendRadius = beachWidth,
                Strength = beachSmoothness
            };

            // Apply state biases from preset
            if (beachBiases.Count > 0)
            {
                foreach (var bias in beachBiases)
                {
                    beachConstraint.StateBiases[bias.stateId] = bias.bias;
                }
            }
            else
            {
                // Default biases if none specified
                beachConstraint.StateBiases[5] = 0.9f;  // Sand (strong positive bias)
                beachConstraint.StateBiases[3] = 0.5f;  // Water (moderate positive bias near beach)
            }

            constraintSystem.AddGlobalConstraint(beachConstraint);
        }

        private void CreateTransitionZones(HierarchicalConstraintSystem constraintSystem, Vector3Int worldSize, int chunkSize)
        {
            // Only create transition zones if relevant features are enabled
            if (includeMountainRange && includeForest)
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

            if (includeRiver && includeBeach)
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
            preset.includeForest = true;
            preset.includeBeach = false;
            preset.mountainHeight = 0.8f;
            preset.mountainRockiness = 0.85f;
            preset.forestDensity = 0.4f;
            return preset;
        }

        /// <summary>
        /// Create a preset with default settings for beaches and islands
        /// </summary>
        public static TerrainConstraintPreset CreateIslandPreset()
        {
            var preset = CreateInstance<TerrainConstraintPreset>();
            preset.presetName = "Tropical Island";
            preset.description = "Island terrain with sandy beaches and tropical forests";
            preset.includeMountainRange = true;
            preset.includeRiver = true;
            preset.includeForest = true;
            preset.includeBeach = true;
            preset.mountainHeight = 0.5f;
            preset.mountainCoverage = 0.2f;
            preset.mountainRockiness = 0.6f;
            preset.beachWidth = 5f;
            preset.forestDensity = 0.8f;
            return preset;
        }

        /// <summary>
        /// Create a preset with default settings for flat plains
        /// </summary>
        public static TerrainConstraintPreset CreatePlainsPreset()
        {
            var preset = CreateInstance<TerrainConstraintPreset>();
            preset.presetName = "Rolling Plains";
            preset.description = "Flat to gently rolling terrain with scattered forests and rivers";
            preset.includeMountainRange = false;
            preset.includeRiver = true;
            preset.includeForest = true;
            preset.includeBeach = false;
            preset.defaultGroundHeight = 0.15f;
            preset.noiseScale = 0.03f;
            preset.forestCoverage = 0.6f;
            preset.forestDensity = 0.3f;
            return preset;
        }
    }
}