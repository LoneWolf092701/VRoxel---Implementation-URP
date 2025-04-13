using UnityEngine;
using System.Collections.Generic;
using WFC.Core;
using WFC.Terrain;

namespace WFC.Terrain
{
    public class TerrainManager : MonoBehaviour
    {
        [SerializeField] private TerrainDefinition currentTerrain;

        private ITerrainGenerator terrainGenerator;

        // Static access for system-wide availability
        public static TerrainManager Current { get; private set; }

        public TerrainDefinition CurrentTerrain => currentTerrain;
        public ITerrainGenerator TerrainGenerator => terrainGenerator;

        private void Awake()
        {
            // Set the static reference
            Current = this;

            // Initialize generator if terrain is set
            if (currentTerrain != null)
            {
                InitializeTerrainGenerator();
            }
        }

        public void InitializeTerrainGenerator()
        {
            if (currentTerrain is MountainValleyTerrainDefinition mountainValley)
            {
                terrainGenerator = new MountainValleyGenerator(mountainValley);
                Debug.Log($"Initialized Mountain Valley terrain generator with {mountainValley.name}");
            }
            // Add more terrain type checks here
            else
            {
                Debug.LogWarning("Unknown terrain type, no generator initialized");
            }
        }

        public void SetTerrain(TerrainDefinition terrainDef)
        {
            currentTerrain = terrainDef;
            InitializeTerrainGenerator();
        }
    }
}