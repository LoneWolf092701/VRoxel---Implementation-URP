using UnityEngine;
using System.Collections.Generic;
using WFC.Core;

namespace WFC.Terrain
{
    [CreateAssetMenu(fileName = "MountainValleyTerrain", menuName = "WFC/Terrain Definitions/Mountain Valley")]
    public class MountainValleyTerrainDefinition : TerrainDefinition
    {
        [Header("Mountain Valley Parameters")]
        public float mountainHeight = 1.0f;
        public float valleyWidth = 0.3f;
        public float riverWidth = 0.1f;
        public float forestDensity = 0.6f;

        [Header("Material References")]
        public Material groundMaterial;
        public Material grassMaterial;
        public Material waterMaterial;
        public Material rockMaterial;
        public Material sandMaterial;
        public Material forestMaterial;
        public Material snowMaterial;
        public Material cliffMaterial;
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
                (int)TerrainStateId.Cliff, (int)TerrainStateId.SnowCap
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

            // Water next to solids
            int[] waterStates = { (int)TerrainStateId.Water, (int)TerrainStateId.DeepWater };
            foreach (int waterState in waterStates)
            {
                foreach (int solidState in solidStates)
                {
                    // Horizontal adjacency
                    rules[waterState, solidState, (int)Direction.Left] = true;
                    rules[waterState, solidState, (int)Direction.Right] = true;
                    rules[waterState, solidState, (int)Direction.Forward] = true;
                    rules[waterState, solidState, (int)Direction.Back] = true;

                    rules[solidState, waterState, (int)Direction.Left] = true;
                    rules[solidState, waterState, (int)Direction.Right] = true;
                    rules[solidState, waterState, (int)Direction.Forward] = true;
                    rules[solidState, waterState, (int)Direction.Back] = true;
                }
            }

            // SnowCap only on high rocks
            rules[(int)TerrainStateId.SnowCap, (int)TerrainStateId.Rock, (int)Direction.Down] = true;
            rules[(int)TerrainStateId.Rock, (int)TerrainStateId.SnowCap, (int)Direction.Up] = true;

            // Same state adjacencies
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

            // Air is empty
            densities[(int)TerrainStateId.Air] = 0.1f;

            // Ground types are solid
            densities[(int)TerrainStateId.Ground] = 0.85f;
            densities[(int)TerrainStateId.Grass] = 0.8f;
            densities[(int)TerrainStateId.Rock] = 0.9f;
            densities[(int)TerrainStateId.Cliff] = 0.95f;
            densities[(int)TerrainStateId.SnowCap] = 0.85f;

            // Water is in between
            densities[(int)TerrainStateId.Water] = 0.65f;
            densities[(int)TerrainStateId.DeepWater] = 0.7f;

            // Sand is solid
            densities[(int)TerrainStateId.Sand] = 0.75f;

            // Forest is a bit less dense than ground
            densities[(int)TerrainStateId.Forest] = 0.82f;

            return densities;
        }

        public override ITerrainGenerator CreateGenerator()
        {
            return new MountainValleyGenerator(this);
        }

        public override void RegisterStates()
        {
            base.RegisterStates();

            // Register mountain valley specific states
            var registry = TerrainStateRegistry.Instance;

            // Set up materials array if needed
            if (registry.StateMaterials.Length <= (int)TerrainStateId.Cliff)
            {
                Debug.LogWarning("StateMaterials array too small, materials may not be properly assigned");
            }

            // Add reference to base materials
            if (registry.StateMaterials[(int)TerrainStateId.Ground] == null && groundMaterial != null)
                registry.StateMaterials[(int)TerrainStateId.Ground] = groundMaterial;

            // Assign materials to states
            if (grassMaterial != null)
                registry.StateMaterials[(int)TerrainStateId.Grass] = grassMaterial;

            if (waterMaterial != null)
                registry.StateMaterials[(int)TerrainStateId.Water] = waterMaterial;

            if (rockMaterial != null)
                registry.StateMaterials[(int)TerrainStateId.Rock] = rockMaterial;

            if (sandMaterial != null)
                registry.StateMaterials[(int)TerrainStateId.Sand] = sandMaterial;

            if (forestMaterial != null)
                registry.StateMaterials[(int)TerrainStateId.Forest] = forestMaterial;

            if (snowMaterial != null)
                registry.StateMaterials[(int)TerrainStateId.SnowCap] = snowMaterial;

            if (cliffMaterial != null)
                registry.StateMaterials[(int)TerrainStateId.Cliff] = cliffMaterial;

            // Register all terrain states for the mountain valley terrain
            registry.RegisterState(new TerrainStateConfig
            {
                Name = "Grass",
                Id = (int)TerrainStateId.Grass,
                Density = 0.8f,
                Color = new Color(0.2f, 0.8f, 0.2f),
            }, "MountainValley");

            registry.RegisterState(new TerrainStateConfig
            {
                Name = "Water",
                Id = (int)TerrainStateId.Water,
                Density = 0.65f,
                Color = new Color(0.2f, 0.4f, 0.8f),
                IsLiquid = true
            }, "MountainValley");

            registry.RegisterState(new TerrainStateConfig
            {
                Name = "Rock",
                Id = (int)TerrainStateId.Rock,
                Density = 0.9f,
                Color = new Color(0.6f, 0.6f, 0.6f)
            }, "MountainValley");
        }
    }
}