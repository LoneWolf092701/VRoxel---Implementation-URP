using UnityEngine;
using System.Collections.Generic;
using WFC.Core;
using System;

namespace WFC.Terrain
{
    [CreateAssetMenu(fileName = "MountainValleyTerrain", menuName = "WFC/Terrain Definitions/Mountain Valley")]
    public class MountainValleyTerrainDefinition : TerrainDefinition
    {
        [Header("Mountain Valley Parameters")]
        public float mountainHeight = 2.5f;
        public float valleyWidth = 0.3f;
        public float forestDensity = 0.6f;

        [Header("Visual Settings")]
        [Range(0.1f, 5.0f)] public float terrainRoughness = 1.0f;
        [Range(0.1f, 2.0f)] public float grassDensity = 1.0f;
        [Range(0.1f, 3.0f)] public float rockSharpness = 1.0f;
        [Range(0.1f, 1.0f)] public float snowCoverage = 0.3f;

        public enum TerrainStateId
        {
            Air = 0,
            Ground = 1,
            Grass = 2,
            Water = 3,
            Rock = 4,
            Sand = 5,
            Forest = 6,
            SnowCap = 7,
            Cliff = 8,
            DeepWater = 9
        }

        protected override bool[,,] GenerateAdjacencyRules()
        {
            // State count includes all states in TerrainStateId enum
            int stateCount = System.Enum.GetValues(typeof(TerrainStateId)).Length;
            int directionCount = System.Enum.GetValues(typeof(Direction)).Length;

            bool[,,] rules = new bool[stateCount, stateCount, directionCount];

            // Initialize all to false
            for (int i = 0; i < stateCount; i++)
                for (int j = 0; j < stateCount; j++)
                    for (int d = 0; d < directionCount; d++)
                        rules[i, j, d] = false;

            // Group solid terrain states
            int[] solidStates = {
                (int)TerrainStateId.Ground, (int)TerrainStateId.Grass,
                (int)TerrainStateId.Rock, (int)TerrainStateId.Sand,
                (int)TerrainStateId.Cliff, (int)TerrainStateId.SnowCap,
                (int)TerrainStateId.Forest
            };

            // Allow solid states to connect to each other
            foreach (int stateA in solidStates)
            {
                foreach (int stateB in solidStates)
                {
                    for (int d = 0; d < directionCount; d++)
                    {
                        rules[stateA, stateB, d] = true;
                    }
                }
            }

            // Air above everything
            foreach (int state in solidStates)
            {
                rules[(int)TerrainStateId.Air, state, (int)Direction.Down] = true;
                rules[state, (int)TerrainStateId.Air, (int)Direction.Up] = true;
            }

            // Create special adjacency rules for valley floor transitions
            // Ensuringgggggggggggg sand can connect to grass and ground properly for natural transitions
            rules[(int)TerrainStateId.Sand, (int)TerrainStateId.Grass, (int)Direction.Left] = true;
            rules[(int)TerrainStateId.Sand, (int)TerrainStateId.Grass, (int)Direction.Right] = true;
            rules[(int)TerrainStateId.Sand, (int)TerrainStateId.Grass, (int)Direction.Forward] = true;
            rules[(int)TerrainStateId.Sand, (int)TerrainStateId.Grass, (int)Direction.Back] = true;

            rules[(int)TerrainStateId.Grass, (int)TerrainStateId.Sand, (int)Direction.Left] = true;
            rules[(int)TerrainStateId.Grass, (int)TerrainStateId.Sand, (int)Direction.Right] = true;
            rules[(int)TerrainStateId.Grass, (int)TerrainStateId.Sand, (int)Direction.Forward] = true;
            rules[(int)TerrainStateId.Grass, (int)TerrainStateId.Sand, (int)Direction.Back] = true;

            // Special rule for Forest adjacency
            rules[(int)TerrainStateId.Forest, (int)TerrainStateId.Grass, (int)Direction.Down] = true;
            rules[(int)TerrainStateId.Grass, (int)TerrainStateId.Forest, (int)Direction.Up] = true;

            // SnowCap only on high rocks
            rules[(int)TerrainStateId.SnowCap, (int)TerrainStateId.Rock, (int)Direction.Down] = true;
            rules[(int)TerrainStateId.Rock, (int)TerrainStateId.SnowCap, (int)Direction.Up] = true;

            // Same state adjacencies (each state can connect to itself in all directions)
            for (int state = 0; state < stateCount; state++)
            {
                for (int d = 0; d < directionCount; d++)
                {
                    rules[state, state, d] = true;
                }
            }

            return rules;
        }

        protected override Dictionary<int, float> GenerateStateDensities()
        {
            Dictionary<int, float> densities = new Dictionary<int, float>();

            // For marching cubes algorithm:
            // - Values ABOVE the surface level (0.5) = inside terrain (solid)
            // - Values BELOW the surface level (0.5) = outside terrain (air)

            // Air should be high density (outside terrain)
            densities[(int)TerrainStateId.Air] = 0.1f;

            // Ground states should be low density (inside terrain)
            densities[(int)TerrainStateId.Ground] = 0.8f;
            densities[(int)TerrainStateId.Grass] = 0.75f;
            densities[(int)TerrainStateId.Rock] = 0.85f; // More solid
            densities[(int)TerrainStateId.Cliff] = 0.9f; // Very solid
            densities[(int)TerrainStateId.SnowCap] = 0.8f;
            densities[(int)TerrainStateId.Sand] = 0.7f;
            densities[(int)TerrainStateId.Forest] = 0.8f;

            return densities;
        }

        public override ITerrainGenerator CreateGenerator()
        {
            return new MountainValleyGenerator(this);
        }

        public override void RegisterStates()
        {
            base.RegisterStates();

            var registry = TerrainStateRegistry.Instance;

            // Register all terrain states for the mountain valley terrain
            RegisterTerrainState(registry, TerrainStateId.Air, "Air", Color.clear, 0.1f);
            RegisterTerrainState(registry, TerrainStateId.Ground, "Ground", new Color(0.5f, 0.4f, 0.3f), 0.8f, groundMaterial);
            RegisterTerrainState(registry, TerrainStateId.Grass, "Grass", new Color(0.2f, 0.8f, 0.2f), 0.85f, grassMaterial);
            RegisterTerrainState(registry, TerrainStateId.Rock, "Rock", new Color(0.6f, 0.6f, 0.6f), 0.9f, groundMaterial);
            RegisterTerrainState(registry, TerrainStateId.Sand, "Sand", new Color(0.9f, 0.8f, 0.5f), 0.82f, sandMaterial);
            RegisterTerrainState(registry, TerrainStateId.Forest, "Forest", new Color(0.1f, 0.6f, 0.1f), 0.85f, grassMaterial);
            RegisterTerrainState(registry, TerrainStateId.SnowCap, "Snow", new Color(0.9f, 0.9f, 0.95f), 0.85f, groundMaterial);
            RegisterTerrainState(registry, TerrainStateId.Cliff, "Cliff", new Color(0.4f, 0.4f, 0.4f), 0.95f, groundMaterial);
        }

        private void RegisterTerrainState(TerrainStateRegistry registry, TerrainStateId stateId, string name,
                                         Color color, float density, Material material = null)
        {
            // Check if the material is already assigned in the registry
            if (material != null)
                registry.StateMaterials[(int)stateId] = material;

            // Register the state with complete configuration
            registry.RegisterState(new TerrainStateConfig
            {
                Name = name,
                Id = (int)stateId,
                Density = density,
                Color = color,
                IsTraversable = stateId != TerrainStateId.Water && stateId != TerrainStateId.Cliff,
            }, "MountainValley");
        }
    }
}