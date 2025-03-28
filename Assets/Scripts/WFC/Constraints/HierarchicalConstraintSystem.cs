// Assets/Scripts/WFC/Core/HierarchicalConstraintSystem.cs
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace WFC.Core
{
    /// <summary>
    /// Core manager for the hierarchical multi-scale constraint system.
    /// This class manages constraints at different scales and calculates their
    /// combined influence on the WFC algorithm.
    /// </summary>
    public class HierarchicalConstraintSystem
    {
        // The global constraints (highest level)
        private List<GlobalConstraint> globalConstraints = new List<GlobalConstraint>();

        // Medium-scale region constraints
        private List<RegionConstraint> regionConstraints = new List<RegionConstraint>();

        // Configuration
        private int chunkSize;

        // Cache for performance optimization
        private Dictionary<Vector3Int, Dictionary<int, float>> chunkStateBiasCache =
            new Dictionary<Vector3Int, Dictionary<int, float>>();

        public HierarchicalConstraintSystem(int chunkSize)
        {
            this.chunkSize = chunkSize;
        }

        /// <summary>
        /// Add a global constraint to the system
        /// </summary>
        public void AddGlobalConstraint(GlobalConstraint constraint)
        {
            globalConstraints.Add(constraint);
            InvalidateCache(); // Clear cache as constraints have changed
        }

        /// <summary>
        /// Add a region constraint to the system
        /// </summary>
        public void AddRegionConstraint(RegionConstraint constraint)
        {
            regionConstraints.Add(constraint);
            InvalidateCache();
        }

        /// <summary>
        /// Remove a global constraint
        /// </summary>
        public void RemoveGlobalConstraint(GlobalConstraint constraint)
        {
            globalConstraints.Remove(constraint);
            InvalidateCache();
        }

        /// <summary>
        /// Remove a region constraint
        /// </summary>
        public void RemoveRegionConstraint(RegionConstraint constraint)
        {
            regionConstraints.Remove(constraint);
            InvalidateCache();
        }

        /// <summary>
        /// Clear all constraints
        /// </summary>
        public void ClearConstraints()
        {
            globalConstraints.Clear();
            regionConstraints.Clear();
            InvalidateCache();
        }

        /// <summary>
        /// Get all global constraints
        /// </summary>
        public IReadOnlyList<GlobalConstraint> GetGlobalConstraints()
        {
            return globalConstraints.AsReadOnly();
        }

        /// <summary>
        /// Get all region constraints
        /// </summary>
        public IReadOnlyList<RegionConstraint> GetRegionConstraints()
        {
            return regionConstraints.AsReadOnly();
        }

        /// <summary>
        /// Calculate the combined influence of all constraints on a specific cell
        /// </summary>
        public Dictionary<int, float> CalculateConstraintInfluence(Cell cell, Chunk chunk, int maxStates)
        {
            // Get world position for this cell
            Vector3 worldPosition = CalculateWorldPosition(cell.Position, chunk.Position);

            // Get all constraints that might affect this cell
            return GetStateBiases(worldPosition, chunk.Position, cell.Position, maxStates);
        }

        /// <summary>
        /// Apply constraint influences to cell's possible states
        /// </summary>
        public void ApplyConstraintsToCell(Cell cell, Chunk chunk, int maxStates)
        {
            // Skip if cell is already collapsed
            if (cell.CollapsedState.HasValue)
                return;

            // Get constraint biases for this cell
            Dictionary<int, float> biases = CalculateConstraintInfluence(cell, chunk, maxStates);

            // If no significant biases, return without modifying cell
            if (!biases.Values.Any(v => Mathf.Abs(v) > 0.01f))
                return;

            // Get current possible states
            HashSet<int> currentStates = new HashSet<int>(cell.PossibleStates);

            // Calculate adjustment probability for each state based on biases
            Dictionary<int, float> stateWeights = new Dictionary<int, float>();

            foreach (int state in currentStates)
            {
                // Base weight is 1.0
                float weight = 1.0f;

                // Apply bias adjustment if exists
                if (biases.TryGetValue(state, out float bias))
                {
                    // Positive bias increases weight (more likely)
                    // Negative bias decreases weight (less likely)
                    weight *= (1.0f + bias);
                }

                // Ensure weight is positive
                weight = Mathf.Max(0.01f, weight);
                stateWeights[state] = weight;
            }

            // If cell has more than one possible state, consider constraint-based collapse
            if (currentStates.Count > 1)
            {
                // Get total weight
                float totalWeight = stateWeights.Values.Sum();

                // Calculate collapse threshold based on strongest bias
                float maxBias = biases.Values.Max(Mathf.Abs);
                float collapseThreshold = Mathf.Lerp(0.9f, 0.5f, maxBias);

                // Find if we have a highly dominant state
                foreach (var state in stateWeights.Keys.ToList())
                {
                    float normalizedWeight = stateWeights[state] / totalWeight;

                    // If one state is strongly preferred, collapse to it
                    if (normalizedWeight > collapseThreshold)
                    {
                        cell.Collapse(state);
                        return; // Cell is now collapsed, we're done
                    }
                }
            }

            // If we didn't collapse, just adjust the cell's entropy for future WFC decisions
            // This can be done by setting a custom entropy value beyond just the count
            // But our current Cell class doesn't support this directly

            // A more advanced implementation would extend the Cell class to handle
            // weighted probability distributions for states
        }

        /// <summary>
        /// Precompute constraints for a chunk to improve performance
        /// </summary>
        public void PrecomputeChunkConstraints(Chunk chunk, int maxStates)
        {
            Dictionary<int, float> chunkBiases = new Dictionary<int, float>();

            // Calculate aggregate bias for the chunk as a whole
            Vector3 chunkWorldCenter = CalculateWorldPosition(
                new Vector3(chunkSize / 2, chunkSize / 2, chunkSize / 2),
                chunk.Position
            );

            // Global constraints influence
            foreach (var constraint in globalConstraints)
            {
                for (int state = 0; state < maxStates; state++)
                {
                    float bias = constraint.GetStateBias(state, chunkWorldCenter);

                    if (Mathf.Abs(bias) > 0.01f)
                    {
                        if (!chunkBiases.ContainsKey(state))
                            chunkBiases[state] = 0;

                        chunkBiases[state] += bias;
                    }
                }
            }

            // Store in cache
            chunkStateBiasCache[chunk.Position] = chunkBiases;
        }

        /// <summary>
        /// Generate initial constraints based on the world requirements
        /// </summary>
        public void GenerateDefaultConstraints(Vector3Int worldSize)
        {
            // Clear existing constraints
            ClearConstraints();

            // Add a ground level constraint
            GlobalConstraint groundConstraint = new GlobalConstraint()
            {
                Name = "Ground Level",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(worldSize.x * chunkSize / 2, 0, worldSize.z * chunkSize / 2),
                WorldSize = new Vector3(worldSize.x * chunkSize, 0, worldSize.z * chunkSize),
                BlendRadius = chunkSize,
                Strength = 0.8f,
                MinHeight = 0,
                MaxHeight = worldSize.y * chunkSize,
                NoiseScale = 0.05f
            };

            // Add biases for ground constraint
            groundConstraint.StateBiases[0] = -0.8f; // Empty (negative bias)
            groundConstraint.StateBiases[1] = 0.6f;  // Ground (positive bias)
            groundConstraint.StateBiases[3] = -0.2f; // Water (slight negative bias)
            groundConstraint.StateBiases[4] = 0.3f;  // Rock (slight positive bias)

            AddGlobalConstraint(groundConstraint);

            // Add a mountain range constraint
            GlobalConstraint mountainConstraint = new GlobalConstraint()
            {
                Name = "Mountain Range",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(worldSize.x * chunkSize * 0.25f, worldSize.y * chunkSize * 0.5f, worldSize.z * chunkSize * 0.5f),
                WorldSize = new Vector3(worldSize.x * chunkSize * 0.3f, worldSize.y * chunkSize * 0.7f, worldSize.z * chunkSize * 0.3f),
                BlendRadius = chunkSize * 2,
                Strength = 0.7f,
                MinHeight = worldSize.y * chunkSize * 0.3f,
                MaxHeight = worldSize.y * chunkSize
            };

            // Add biases for mountain constraint
            mountainConstraint.StateBiases[0] = -0.5f; // Empty (negative at lower elevations)
            mountainConstraint.StateBiases[1] = 0.2f;  // Ground (some ground)
            mountainConstraint.StateBiases[4] = 0.7f;  // Rock (strong positive bias)

            AddGlobalConstraint(mountainConstraint);

            // Add a river constraint
            GlobalConstraint riverConstraint = new GlobalConstraint()
            {
                Name = "River",
                Type = ConstraintType.RiverPath,
                Strength = 0.8f,
                PathWidth = chunkSize * 0.5f,
                BlendRadius = chunkSize
            };

            // Add control points for the river
            riverConstraint.ControlPoints.Add(new Vector3(0, 2, worldSize.z * chunkSize * 0.3f));
            riverConstraint.ControlPoints.Add(new Vector3(worldSize.x * chunkSize * 0.3f, 1, worldSize.z * chunkSize * 0.5f));
            riverConstraint.ControlPoints.Add(new Vector3(worldSize.x * chunkSize * 0.7f, 0, worldSize.z * chunkSize * 0.6f));
            riverConstraint.ControlPoints.Add(new Vector3(worldSize.x * chunkSize, 0, worldSize.z * chunkSize * 0.7f));

            // Add biases for river constraint
            riverConstraint.StateBiases[3] = 0.9f;  // Water (strong positive bias)
            riverConstraint.StateBiases[5] = 0.4f;  // Sand (moderate positive bias)

            AddGlobalConstraint(riverConstraint);

            // Add a forest region
            GlobalConstraint forestConstraint = new GlobalConstraint()
            {
                Name = "Forest",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(worldSize.x * chunkSize * 0.7f, worldSize.y * chunkSize * 0.2f, worldSize.z * chunkSize * 0.3f),
                WorldSize = new Vector3(worldSize.x * chunkSize * 0.4f, worldSize.y * chunkSize * 0.3f, worldSize.z * chunkSize * 0.4f),
                BlendRadius = chunkSize * 1.5f,
                Strength = 0.6f
            };

            // Add biases for forest constraint
            forestConstraint.StateBiases[2] = 0.5f;  // Grass (moderate positive bias)
            forestConstraint.StateBiases[6] = 0.7f;  // Tree (strong positive bias)

            AddGlobalConstraint(forestConstraint);

            // Add transition regions between biomes
            // Example: Mountain to Forest transition
            RegionConstraint mountainForestTransition = new RegionConstraint()
            {
                Name = "Mountain-Forest Transition",
                Type = RegionType.Transition,
                ChunkPosition = new Vector3Int(1, 1, 0),
                ChunkSize = new Vector3Int(1, 1, 1),
                Strength = 0.7f,
                Gradient = 0.5f,
                SourceState = 4, // Rock
                TargetState = 6  // Tree
            };

            AddRegionConstraint(mountainForestTransition);

            // River to Ground transition
            RegionConstraint riverGroundTransition = new RegionConstraint()
            {
                Name = "River-Ground Transition",
                Type = RegionType.Transition,
                ChunkPosition = new Vector3Int(1, 0, 0),
                ChunkSize = new Vector3Int(1, 1, 2),
                InternalOrigin = new Vector3(0.3f, 0, 0.3f),
                InternalSize = new Vector3(0.4f, 1, 0.4f),
                Strength = 0.6f,
                Gradient = 0.3f,
                SourceState = 3, // Water
                TargetState = 1  // Ground
            };

            AddRegionConstraint(riverGroundTransition);
        }

        /// <summary>
        /// Get combined state biases at a specific position
        /// </summary>
        private Dictionary<int, float> GetStateBiases(
            Vector3 worldPosition,
            Vector3Int chunkPosition,
            Vector3Int localPosition,
            int maxStates)
        {
            Dictionary<int, float> combinedBiases = new Dictionary<int, float>();

            // Get cached chunk-level biases
            if (chunkStateBiasCache.TryGetValue(chunkPosition, out var chunkBiases))
            {
                // Start with chunk-level biases
                foreach (var pair in chunkBiases)
                {
                    combinedBiases[pair.Key] = pair.Value;
                }
            }

            // Apply global constraints
            foreach (var constraint in globalConstraints)
            {
                for (int state = 0; state < maxStates; state++)
                {
                    float bias = constraint.GetStateBias(state, worldPosition);

                    if (Mathf.Abs(bias) > 0.01f)
                    {
                        if (!combinedBiases.ContainsKey(state))
                            combinedBiases[state] = 0;

                        combinedBiases[state] += bias;
                    }
                }
            }

            // Apply region constraints
            foreach (var constraint in regionConstraints)
            {
                for (int state = 0; state < maxStates; state++)
                {
                    float bias = constraint.GetStateBias(state, chunkPosition, localPosition, chunkSize);

                    if (Mathf.Abs(bias) > 0.01f)
                    {
                        if (!combinedBiases.ContainsKey(state))
                            combinedBiases[state] = 0;

                        combinedBiases[state] += bias;
                    }
                }
            }

            // Clamp bias values to reasonable range
            foreach (int state in combinedBiases.Keys.ToList())
            {
                combinedBiases[state] = Mathf.Clamp(combinedBiases[state], -1f, 1f);
            }

            return combinedBiases;
        }

        /// <summary>
        /// Calculate world position from local position within a chunk
        /// </summary>
        private Vector3 CalculateWorldPosition(Vector3 localPosition, Vector3Int chunkPosition)
        {
            return new Vector3(
                chunkPosition.x * chunkSize + localPosition.x,
                chunkPosition.y * chunkSize + localPosition.y,
                chunkPosition.z * chunkSize + localPosition.z
            );
        }

        /// <summary>
        /// Clear the cached bias data
        /// </summary>
        private void InvalidateCache()
        {
            chunkStateBiasCache.Clear();
        }
    }
}