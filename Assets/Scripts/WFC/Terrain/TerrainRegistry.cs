using System.Collections.Generic;
using UnityEngine;

namespace WFC.Terrain
{
    public class TerrainRegistry
    {
        private static TerrainRegistry _instance;
        public static TerrainRegistry Instance => _instance ?? (_instance = new TerrainRegistry());

        private Dictionary<string, TerrainDefinition> terrainDefinitions = new Dictionary<string, TerrainDefinition>();

        private TerrainRegistry()
        {
            // Initialize with default terrain definitions
            RegisterDefaultTerrains();
        }

        private void RegisterDefaultTerrains()
        {
            // Load terrain definitions from resources
            TerrainDefinition[] terrains = Resources.LoadAll<TerrainDefinition>("TerrainDefinitions");
            foreach (var terrain in terrains)
            {
                RegisterTerrainDefinition(terrain);
            }

            // Create default if none found
            if (terrainDefinitions.Count == 0)
            {
                var defaultTerrain = ScriptableObject.CreateInstance<MountainValleyTerrainDefinition>();
                defaultTerrain.name = "DefaultMountainValley";
                RegisterTerrainDefinition(defaultTerrain);
            }
        }

        public void RegisterTerrainDefinition(TerrainDefinition terrain)
        {
            if (terrain == null) return;
            terrainDefinitions[terrain.terrainName] = terrain;
        }

        public TerrainDefinition GetTerrainDefinition(string terrainType)
        {
            if (string.IsNullOrEmpty(terrainType)) return null;

            if (terrainDefinitions.TryGetValue(terrainType, out TerrainDefinition terrain))
                return terrain;

            // Return first available as fallback
            foreach (var def in terrainDefinitions.Values)
                return def;

            return null;
        }
    }
}