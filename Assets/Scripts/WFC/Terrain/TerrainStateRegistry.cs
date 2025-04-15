// TerrainStateRegistry.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Core; // For Direction enum
using WFC.Configuration; // For WFCConfigManager

namespace WFC.Terrain
{
    /// <summary>
    /// Central registry for terrain states across all terrain types
    /// </summary>
    public class TerrainStateRegistry
    {
        // Singleton instance
        private static TerrainStateRegistry _instance;
        public static TerrainStateRegistry Instance => _instance ?? (_instance = new TerrainStateRegistry());

        // Maps state IDs to their configuration
        private Dictionary<int, TerrainStateConfig> stateConfigs = new Dictionary<int, TerrainStateConfig>();

        // Maps state names to IDs for convenient lookup
        private Dictionary<string, int> stateNameToId = new Dictionary<string, int>();

        // Tracks which terrain definition each state belongs to
        private Dictionary<int, string> stateToTerrainType = new Dictionary<int, string>();

        // Materials for each state
        private Material[] stateMaterials;
        public Material[] StateMaterials => stateMaterials;

        // Adjacency rules between states
        private bool[,,] adjacencyRules;

        // Next available ID for dynamic registration
        private int nextStateId = 0;

        // Core states that should be available in all terrain types
        public const int STATE_AIR = 0;
        public const int STATE_GROUND = 1;

        // Initialize with core states
        public TerrainStateRegistry()
        {
            // Initialize adjacency rules array
            int maxStates = WFCConfigManager.Config.World.maxStates;
            adjacencyRules = new bool[maxStates, maxStates, 6]; // 6 directions

            // Initialize materials array
            stateMaterials = new Material[maxStates];

            // Register core states
            RegisterState(new TerrainStateConfig
            {
                Name = "Air",
                Id = STATE_AIR,
                IsTraversable = true,
                Density = 0.1f,
                //DefaultMaterial = "Materials/Air"
            }, "Core");

            RegisterState(new TerrainStateConfig
            {
                Name = "Ground",
                Id = STATE_GROUND,
                IsTraversable = false,
                Density = 0.8f,
                //DefaultMaterial = "Materials/Ground"
            }, "Core");

            // Initialize basic materials
            //InitializeDefaultMaterials();
        }

        /// <summary>
        /// Register a terrain state with the registry
        /// </summary>
        public int RegisterState(TerrainStateConfig config, string terrainType)
        {
            // If ID is not specified, assign the next available
            if (config.Id < 0)
            {
                config.Id = nextStateId++;
            }
            else
            {
                // Update nextStateId if needed
                nextStateId = Mathf.Max(nextStateId, config.Id + 1);
            }

            stateConfigs[config.Id] = config;
            stateNameToId[config.Name] = config.Id;
            stateToTerrainType[config.Id] = terrainType;

            Debug.Log($"Registered terrain state: {config.Name} (ID: {config.Id}) for terrain: {terrainType}");

            return config.Id;
        }

        /// <summary>
        /// Register adjacency rule between two states
        /// </summary>
        public void RegisterAdjacencyRule(int stateA, int stateB, Direction direction, bool canBeAdjacent)
        {
            if (stateA >= 0 && stateA < adjacencyRules.GetLength(0) &&
                stateB >= 0 && stateB < adjacencyRules.GetLength(1))
            {
                adjacencyRules[stateA, stateB, (int)direction] = canBeAdjacent;
            }
        }

        /// <summary>
        /// Check if two states can be adjacent in a given direction
        /// </summary>
        public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
        {
            if (stateA < 0 || stateA >= adjacencyRules.GetLength(0) ||
                stateB < 0 || stateB >= adjacencyRules.GetLength(1))
            {
                return false;
            }

            return adjacencyRules[stateA, stateB, (int)direction];
        }

        /// <summary>
        /// Get terrain state config by ID
        /// </summary>
        public TerrainStateConfig GetStateConfig(int stateId)
        {
            return stateConfigs.TryGetValue(stateId, out var config) ? config : null;
        }

        /// <summary>
        /// Get terrain state ID by name
        /// </summary>
        public int GetStateId(string stateName)
        {
            return stateNameToId.TryGetValue(stateName, out var id) ? id : -1;
        }

        /// <summary>
        /// Get density value for a state
        /// </summary>
        public float GetStateDensity(int stateId)
        {
            return stateConfigs.TryGetValue(stateId, out var config) ? config.Density : 0.5f;
        }

        /// <summary>
        /// Clear all registered states and rules
        /// </summary>
        public void ResetRegistry()
        {
            stateConfigs.Clear();
            stateNameToId.Clear();
            stateToTerrainType.Clear();

            // Reset adjacency rules
            for (int i = 0; i < adjacencyRules.GetLength(0); i++)
                for (int j = 0; j < adjacencyRules.GetLength(1); j++)
                    for (int d = 0; d < adjacencyRules.GetLength(2); d++)
                        adjacencyRules[i, j, d] = false;

            nextStateId = 0;

            // Re-register core states
            RegisterState(new TerrainStateConfig
            {
                Name = "Air",
                Id = STATE_AIR,
                IsTraversable = true,
                Density = 0.1f,
                //DefaultMaterial = "Materials/Air"
            }, "Core");

            RegisterState(new TerrainStateConfig
            {
                Name = "Ground",
                Id = STATE_GROUND,
                IsTraversable = false,
                Density = 0.8f,
                //DefaultMaterial = "Materials/Ground"
            }, "Core");
        }
    }
}