using UnityEngine;
using System.Collections.Generic;
using WFC.Core;

namespace WFC.Terrain
{
    public abstract class TerrainDefinition : ScriptableObject
    {
        [Header("Basic Configuration")]
        public string terrainName = "Default Terrain";
        public int maxStateCount = 12;

        // Adjacency rules - can be null for default
        public bool[,,] AdjacencyRules { get; protected set; }

        // State densities for marching cubes
        public Dictionary<int, float> StateDensities { get; protected set; }

        protected virtual void OnEnable()
        {
            // Generate the adjacency rules and densities when loaded
            AdjacencyRules = GenerateAdjacencyRules();
            StateDensities = GenerateStateDensities();
        }

        // Must be implemented by concrete terrains
        protected abstract bool[,,] GenerateAdjacencyRules();
        protected abstract Dictionary<int, float> GenerateStateDensities();
        public virtual ITerrainGenerator CreateGenerator()
        {
            Debug.LogWarning($"CreateGenerator not implemented for {terrainName}. Using fallback.");
            return new DefaultTerrainGenerator(this);
        }

        public virtual void RegisterStates()
        {
            // Base implementation registers common states
            Debug.Log($"Registered basic states for terrain: {terrainName}");
        }

        // Add a simple default generator as fallback
        private class DefaultTerrainGenerator : ITerrainGenerator
        {
            private TerrainDefinition terrainDefinition;

            public DefaultTerrainGenerator(TerrainDefinition definition)
            {
                terrainDefinition = definition;
            }

            public void GenerateTerrain(Chunk chunk, System.Random random)
            {
                // Simple flat terrain
                int baseHeight = chunk.Size / 2;

                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        for (int y = 0; y < chunk.Size; y++)
                        {
                            var cell = chunk.GetCell(x, y, z);
                            if (cell != null)
                            {
                                cell.Collapse(y < baseHeight ? 1 : 0); // Ground or Air
                            }
                        }
                    }
                }
            }

            public void ApplyConstraints(HierarchicalConstraintSystem system, Vector3Int worldSize, int chunkSize)
            {
                // Basic ground constraint
                GlobalConstraint groundConstraint = new GlobalConstraint
                {
                    Name = "Ground Level",
                    Type = ConstraintType.HeightMap,
                    WorldCenter = new Vector3(worldSize.x * chunkSize / 2, 0, worldSize.z * chunkSize / 2),
                    WorldSize = new Vector3(worldSize.x * chunkSize, 0, worldSize.z * chunkSize),
                    BlendRadius = chunkSize,
                    Strength = 0.8f
                };

                system.AddGlobalConstraint(groundConstraint);
            }


        }
    }
}