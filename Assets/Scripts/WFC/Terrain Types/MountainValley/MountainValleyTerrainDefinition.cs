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

        [Serializable]
        public class TerrainMaterialSettings
        {
            public Material material;
            public float smoothness = 0.5f;
            public float metallic = 0.0f;
            public Texture2D mainTexture;
            public Texture2D normalMap;
            public Color tintColor = Color.white;
            public float tiling = 1.0f;
        }

        [Header("Visual Settings")]
        [Range(0.1f, 5.0f)] public float terrainRoughness = 1.0f;
        [Range(0.1f, 2.0f)] public float grassDensity = 1.0f;
        [Range(0.1f, 3.0f)] public float rockSharpness = 1.0f;
        [Range(0.1f, 1.0f)] public float snowCoverage = 0.3f;

        [Header("Material Settings")]
        public TerrainMaterialSettings groundMaterialSettings;
        public TerrainMaterialSettings grassMaterialSettings;
        public TerrainMaterialSettings rockMaterialSettings;
        public TerrainMaterialSettings sandMaterialSettings;
        public TerrainMaterialSettings forestMaterialSettings;
        public TerrainMaterialSettings snowMaterialSettings;
        public TerrainMaterialSettings cliffMaterialSettings;
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
            // Ensure sand can connect to grass and ground properly for natural transitions
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
            // - Values BELOW the surface level (0.5) = inside terrain (solid)
            // - Values ABOVE the surface level (0.5) = outside terrain (air)

            // Air should be high density (outside terrain)
            densities[(int)TerrainStateId.Air] = 0.9f;

            // Ground states should be low density (inside terrain)
            densities[(int)TerrainStateId.Ground] = 0.2f;
            densities[(int)TerrainStateId.Grass] = 0.25f;
            densities[(int)TerrainStateId.Rock] = 0.15f; // More solid
            densities[(int)TerrainStateId.Cliff] = 0.1f; // Very solid
            densities[(int)TerrainStateId.SnowCap] = 0.2f;
            densities[(int)TerrainStateId.Sand] = 0.3f;
            densities[(int)TerrainStateId.Forest] = 0.2f;

            // Water slightly below surface level
            //densities[(int)TerrainStateId.Water] = 0.45f;
            //densities[(int)TerrainStateId.DeepWater] = 0.35f;

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
            RegisterTerrainState(registry, TerrainStateId.Ground, "Ground", new Color(0.5f, 0.4f, 0.3f), 0.8f, groundMaterialSettings?.material);
            RegisterTerrainState(registry, TerrainStateId.Grass, "Grass", new Color(0.2f, 0.8f, 0.2f), 0.85f, grassMaterialSettings?.material);
            //RegisterTerrainState(registry, TerrainStateId.Water, "Water", new Color(0.0f, 0.4f, 0.8f), 0.7f, waterMaterialSettings?.material);
            RegisterTerrainState(registry, TerrainStateId.Rock, "Rock", new Color(0.6f, 0.6f, 0.6f), 0.9f, rockMaterialSettings?.material);
            RegisterTerrainState(registry, TerrainStateId.Sand, "Sand", new Color(0.9f, 0.8f, 0.5f), 0.82f, sandMaterialSettings?.material);
            RegisterTerrainState(registry, TerrainStateId.Forest, "Forest", new Color(0.1f, 0.6f, 0.1f), 0.85f, forestMaterialSettings?.material);
            RegisterTerrainState(registry, TerrainStateId.SnowCap, "Snow", new Color(0.9f, 0.9f, 0.95f), 0.85f, snowMaterialSettings?.material);
            RegisterTerrainState(registry, TerrainStateId.Cliff, "Cliff", new Color(0.4f, 0.4f, 0.4f), 0.95f, cliffMaterialSettings?.material);
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

        public void ApplyMaterialSettings()
        {
            for (int i = 0; i < (int)TerrainStateId.DeepWater + 1; i++)
            {
                Material material = TerrainStateRegistry.Instance.StateMaterials[i];
                if (material != null)
                {
                    // Get corresponding material settings
                    TerrainMaterialSettings settings = GetMaterialSettingsForState((TerrainStateId)i);
                    if (settings != null && settings.material != null)
                    {
                        // Apply material settings
                        material.SetFloat("_Smoothness", settings.smoothness);
                        material.SetFloat("_Metallic", settings.metallic);

                        if (settings.mainTexture != null)
                            material.SetTexture("_MainTex", settings.mainTexture);

                        if (settings.normalMap != null)
                            material.SetTexture("_BumpMap", settings.normalMap);

                        material.SetColor("_Color", settings.tintColor);
                        material.SetFloat("_Tiling", settings.tiling);
                    }
                }
            }
        }

        private TerrainMaterialSettings GetMaterialSettingsForState(TerrainStateId state)
        {
            switch (state)
            {
                case TerrainStateId.Ground: return groundMaterialSettings;
                case TerrainStateId.Grass: return grassMaterialSettings;
                //case TerrainStateId.Water: return waterMaterialSettings;
                case TerrainStateId.Rock: return rockMaterialSettings;
                case TerrainStateId.Sand: return sandMaterialSettings;
                case TerrainStateId.Forest: return forestMaterialSettings;
                case TerrainStateId.SnowCap: return snowMaterialSettings;
                case TerrainStateId.Cliff: return cliffMaterialSettings;
                default: return null;
            }
        }
    }
}