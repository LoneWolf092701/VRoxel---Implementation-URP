/*
 * HierarchicalConstraintSystem.cs
 * -----------------------------
 * Implements a multi-scale constraint system for guiding the WFC terrain generation.
 * 
 * This system allows for control at different scales:
 * - Global constraints: Large-scale terrain features like mountains, rivers, biomes
 * - Region constraints: Medium-scale features like forests, lakes, transition zones
 * - Local constraints: Fine-grained control for specific detailed features
 * 
 * Each constraint applies biases to the state selection probabilities during WFC collapse,
 * influencing but not strictly enforcing the terrain generation. This creates a balance
 * between controlled structure and procedural variation.
 * 
 * The hierarchical approach enables both top-down design control and emergent details,
 * with constraints having varying influence based on strength, distance, and priority.
 * 
 */using System.Collections.Generic;
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

        // Local cell constraints
        private Dictionary<Vector3Int, Dictionary<Vector3Int, Dictionary<int, float>>> localConstraints =
            new Dictionary<Vector3Int, Dictionary<Vector3Int, Dictionary<int, float>>>();

        // Configuration
        private readonly int chunkSize;
        private float globalWeight = 0.7f;
        private float regionWeight = 1.0f;
        private float localWeight = 1.0f;

        // Cache for performance optimization
        private Dictionary<Vector3Int, Dictionary<int, float>> chunkStateBiasCache =
            new Dictionary<Vector3Int, Dictionary<int, float>>();

        // Constraint change tracking for efficient updates
        private HashSet<Vector3Int> dirtyChunks = new HashSet<Vector3Int>();

        // Constraint application statistics
        public struct ConstraintStats
        {
            public int GlobalConstraintsApplied;
            public int RegionConstraintsApplied;
            public int LocalConstraintsApplied;
            public int CellsAffected;
            public int CellsCollapsed;
            public float AverageInfluenceStrength;

            public void Reset()
            {
                GlobalConstraintsApplied = 0;
                RegionConstraintsApplied = 0;
                LocalConstraintsApplied = 0;
                CellsAffected = 0;
                CellsCollapsed = 0;
                AverageInfluenceStrength = 0;
            }
        }

        private ConstraintStats _statistics = new ConstraintStats();
        public ConstraintStats Statistics => _statistics;

        public HierarchicalConstraintSystem(int chunkSize)
        {
            this.chunkSize = chunkSize;
            _statistics = new ConstraintStats();
        }

        /// <summary>
        /// Set weights for different constraint levels
        /// </summary>
        public void SetWeights(float global, float region, float local)
        {
            globalWeight = Mathf.Clamp01(global);
            regionWeight = Mathf.Clamp01(region);
            localWeight = Mathf.Clamp01(local);
        }

        /// <summary>
        /// Add a global constraint to the system
        /// </summary>
        public void AddGlobalConstraint(GlobalConstraint constraint)
        {
            globalConstraints.Add(constraint);
            InvalidateCache();
        }

        /// <summary>
        /// Add a region constraint to the system
        /// </summary>
        public void AddRegionConstraint(RegionConstraint constraint)
        {
            regionConstraints.Add(constraint);
            InvalidateCache();

            // Mark affected chunks as dirty
            for (int x = 0; x < constraint.ChunkSize.x; x++)
            {
                for (int y = 0; y < constraint.ChunkSize.y; y++)
                {
                    for (int z = 0; z < constraint.ChunkSize.z; z++)
                    {
                        Vector3Int chunkPos = constraint.ChunkPosition + new Vector3Int(x, y, z);
                        dirtyChunks.Add(chunkPos);
                    }
                }
            }
        }

        /// <summary>
        /// Add or update a local constraint for a specific cell
        /// </summary>
        public void AddLocalConstraint(Vector3Int chunkPos, Vector3Int cellPos, int state, float bias)
        {
            if (!localConstraints.TryGetValue(chunkPos, out var cellConstraints))
            {
                cellConstraints = new Dictionary<Vector3Int, Dictionary<int, float>>();
                localConstraints[chunkPos] = cellConstraints;
            }

            if (!cellConstraints.TryGetValue(cellPos, out var stateBiases))
            {
                stateBiases = new Dictionary<int, float>();
                cellConstraints[cellPos] = stateBiases;
            }

            stateBiases[state] = Mathf.Clamp(bias, -1f, 1f);

            // Mark chunk as dirty
            dirtyChunks.Add(chunkPos);
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

            // Mark affected chunks as dirty
            for (int x = 0; x < constraint.ChunkSize.x; x++)
            {
                for (int y = 0; y < constraint.ChunkSize.y; y++)
                {
                    for (int z = 0; z < constraint.ChunkSize.z; z++)
                    {
                        Vector3Int chunkPos = constraint.ChunkPosition + new Vector3Int(x, y, z);
                        dirtyChunks.Add(chunkPos);
                    }
                }
            }
        }

        /// <summary>
        /// Clear all constraints
        /// </summary>
        public void ClearConstraints()
        {
            globalConstraints.Clear();
            regionConstraints.Clear();
            localConstraints.Clear();
            InvalidateCache();
        }

        /// <summary>
        /// Get all global constraints
        /// </summary>
        public IReadOnlyList<GlobalConstraint> GetGlobalConstraints()
        {
            return globalConstraints;
        }

        /// <summary>
        /// Get all region constraints
        /// </summary>
        public IReadOnlyList<RegionConstraint> GetRegionConstraints()
        {
            return regionConstraints;
        }

        /// <summary>
        /// Calculate the combined influence of all constraints on a specific cell
        /// </summary>
        /*
        * CalculateConstraintInfluence
        * -----------------------------
        * Computes the combined influence of all constraints on a specific cell.
        * 
        * This is a key function in the constraint-based guidance system that:
        * 1. Gets the world position for the cell
        * 2. Queries all relevant constraints that might affect this position
        * 3. Combines their influences into a set of biases for each possible state
        * 
        * The biases are numeric values typically in the range [-1,1] that modify
        * the probability of selecting each state during the collapse operation:
        * - Positive bias: increases likelihood of selecting the state
        * - Negative bias: decreases likelihood of selecting the state
        * - Zero bias: no influence on the state selection
        * 
        * The method handles resolving conflicts between competing constraints
        * through various weighting and prioritization strategies.
        */
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

            // Reset statistics if needed
            if (Statistics.CellsAffected == 0)
            {
                _statistics.Reset();
            }

            // Get constraint biases for this cell
            Dictionary<int, float> biases = CalculateConstraintInfluence(cell, chunk, maxStates);

            // Track statistics
            _statistics.CellsAffected++;
            float totalInfluence = 0;

            // If no significant biases, return without modifying cell
            bool hasBiases = false;
            foreach (var bias in biases.Values)
            {
                if (Mathf.Abs(bias) > 0.01f)
                {
                    hasBiases = true;
                    break;
                }
            }

            if (!hasBiases)
                return;

            // Get current possible states
            HashSet<int> currentStates = new HashSet<int>(cell.PossibleStates);

            // Calculate adjustment probability for each state based on biases
            Dictionary<int, float> stateWeights = new Dictionary<int, float>();
            float totalWeight = 0;

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
                    totalInfluence += Mathf.Abs(bias);
                }

                // Ensure weight is positive
                weight = Mathf.Max(0.01f, weight);
                stateWeights[state] = weight;
                totalWeight += weight;
            }

            // Update statistics
            if (biases.Count > 0)
            {
                _statistics.AverageInfluenceStrength =
                    ((Statistics.AverageInfluenceStrength * (Statistics.CellsAffected - 1)) +
                    (totalInfluence / biases.Count)) / Statistics.CellsAffected;
            }

            // If cell has more than one possible state, consider constraint-based collapse
            if (currentStates.Count > 1)
            {
                // Calculate collapse threshold based on strongest bias
                float maxBias = 0f;
                foreach (var bias in biases.Values)
                {
                    if (Mathf.Abs(bias) > maxBias)
                        maxBias = Mathf.Abs(bias);
                }

                float collapseThreshold = Mathf.Lerp(0.9f, 0.5f, maxBias);

                // Find if have a highly dominant state
                foreach (var pair in stateWeights)
                {
                    float normalizedWeight = pair.Value / totalWeight;

                    // If one state is strongly preferred, collapse to it
                    if (normalizedWeight > collapseThreshold)
                    {
                        cell.Collapse(pair.Key);
                        _statistics.CellsCollapsed++;
                        return; // Cell is now collapsed
                    }
                }
            }
        }

        /// <summary>
        /// Precompute constraints for a chunk to improve performance
        /// </summary>
        public void PrecomputeChunkConstraints(Chunk chunk, int maxStates)
        {
            // Skip if already in cache and not marked dirty
            if (chunkStateBiasCache.ContainsKey(chunk.Position) && !dirtyChunks.Contains(chunk.Position))
                return;

            Dictionary<int, float> chunkBiases = new Dictionary<int, float>();

            // Calculate aggregate bias for the chunk as a whole
            Vector3 chunkWorldCenter = CalculateWorldPosition(
                new Vector3(chunkSize / 2, chunkSize / 2, chunkSize / 2),
                chunk.Position
            );

            // Global constraints influence
            foreach (var constraint in globalConstraints)
            {
                _statistics.GlobalConstraintsApplied++;
                for (int state = 0; state < maxStates; state++)
                {
                    float bias = constraint.GetStateBias(state, chunkWorldCenter) * globalWeight;

                    if (Mathf.Abs(bias) > 0.01f)
                    {
                        if (!chunkBiases.ContainsKey(state))
                            chunkBiases[state] = 0;

                        chunkBiases[state] += bias;
                    }
                }
            }

            // Region constraints influence
            foreach (var constraint in regionConstraints)
            {
                // Check if this chunk is affected by this region constraint
                Vector3Int relativePos = chunk.Position - constraint.ChunkPosition;

                if (relativePos.x >= 0 && relativePos.x < constraint.ChunkSize.x &&
                    relativePos.y >= 0 && relativePos.y < constraint.ChunkSize.y &&
                    relativePos.z >= 0 && relativePos.z < constraint.ChunkSize.z)
                {
                    _statistics.RegionConstraintsApplied++;

                    // Get center cell position
                    Vector3 centerLocal = new Vector3(chunkSize / 2, chunkSize / 2, chunkSize / 2);

                    // Check influence at chunk center
                    for (int state = 0; state < maxStates; state++)
                    {
                        float bias = constraint.GetStateBias(state, chunk.Position, centerLocal, chunkSize) * regionWeight;

                        if (Mathf.Abs(bias) > 0.01f)
                        {
                            if (!chunkBiases.ContainsKey(state))
                                chunkBiases[state] = 0;

                            chunkBiases[state] += bias;
                        }
                    }
                }
            }

            // Store in cache
            chunkStateBiasCache[chunk.Position] = chunkBiases;

            // Remove from dirty list
            dirtyChunks.Remove(chunk.Position);
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
                MinHeight = 0,
                MaxHeight = chunkSize,
            };

            // Add biases for mountain constraint
            mountainConstraint.StateBiases[0] = -0.5f; // Empty (negative at lower elevations)
            mountainConstraint.StateBiases[1] = 0.2f;  // Ground (some ground)
            mountainConstraint.StateBiases[4] = 0.7f;  // Rock (strong positive bias)

            AddGlobalConstraint(mountainConstraint);

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
        }

        /// <summary>
        /// Create gradient transition between two regions
        /// </summary>
        public void CreateGradientTransition(Vector3Int startChunk, Vector3Int endChunk, int startState, int endState,
                                             string name = "Transition", float strength = 0.7f, float gradient = 0.5f)
        {
            // Calculate direction vector
            Vector3Int direction = endChunk - startChunk;
            Vector3Int size = new Vector3Int(
                Mathf.Abs(direction.x) + 1,
                Mathf.Abs(direction.y) + 1,
                Mathf.Abs(direction.z) + 1
            );

            // Get starting position (min coordinates)
            Vector3Int position = new Vector3Int(
                Mathf.Min(startChunk.x, endChunk.x),
                Mathf.Min(startChunk.y, endChunk.y),
                Mathf.Min(startChunk.z, endChunk.z)
            );

            RegionConstraint transition = new RegionConstraint()
            {
                Name = name,
                Type = RegionType.Transition,
                ChunkPosition = position,
                ChunkSize = size,
                Strength = strength,
                Gradient = gradient,
                SourceState = startState,
                TargetState = endState,
                // Direction is normalized vector from start to end
                TransitionDirection = new Vector3(direction.x, direction.y, direction.z).normalized
            };

            AddRegionConstraint(transition);
        }

        /*
         * GetStateBiases
         * ----------------------------------------------------------------------------
         * Computes and combines state biases from all applicable constraints.
         * 
         * This detailed constraint resolution:
         * 1. Organizes constraints by type (global, region, local)
         * 2. Assigns appropriate weights to each constraint type
         * 3. For each constraint type:
         *    a. Groups biases by state
         *    b. Uses sign-preserving averaging for same-state biases
         * 4. Combines across constraint types with sophisticated conflict handling:
         *    a. Detects opposing biases (conflicts)
         *    b. Resolves conflicts by taking the stronger bias
         *    c. For compatible biases, uses weighted blending
         *    d. Applies normalization to keep values in valid range
         * 
         * The conflict resolution is critical for creating coherent terrain
         * when different constraints might have opposing effects on the same cell.
         * 
         * Parameters:
         * - worldPosition: World position to check
         * - chunkPosition: Position of the chunk
         * - localPosition: Position within the chunk
         * - maxStates: Maximum number of possible states
         * 
         * Returns: Dictionary mapping states to combined bias values
         */
        private Dictionary<int, float> GetStateBiases(
            Vector3 worldPosition,
            Vector3Int chunkPosition,
            Vector3Int localPosition,
            int maxStates)
        {
            Dictionary<int, float> normalizedBiases = new Dictionary<int, float>();

            // Group biases by state for each constraint type
            Dictionary<int, float> globalStateBiases = new Dictionary<int, float>();
            Dictionary<int, float> regionStateBiases = new Dictionary<int, float>();
            Dictionary<int, float> localStateBiases = new Dictionary<int, float>();

            // Apply global constraints
            foreach (var constraint in globalConstraints)
            {
                for (int state = 0; state < maxStates; state++)
                {
                    float bias = constraint.GetStateBias(state, worldPosition);
                    if (Mathf.Abs(bias) > 0.01f)
                    {
                        if (!globalStateBiases.ContainsKey(state))
                            globalStateBiases[state] = 0;

                        globalStateBiases[state] += bias;
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
                        if (!regionStateBiases.ContainsKey(state))
                            regionStateBiases[state] = 0;

                        regionStateBiases[state] += bias;
                    }
                }
            }

            // Apply local constraints
            if (localConstraints.TryGetValue(chunkPosition, out var cellConstraints) &&
                cellConstraints.TryGetValue(localPosition, out var stateBiases))
            {
                foreach (var pair in stateBiases)
                {
                    if (Mathf.Abs(pair.Value) > 0.01f)
                        localStateBiases[pair.Key] = pair.Value;
                }
            }

            // Average biases within each type to avoid over-influence from multiple constraints
            foreach (var pair in globalStateBiases)
            {
                globalStateBiases[pair.Key] = pair.Value * globalWeight;
            }

            foreach (var pair in regionStateBiases)
            {
                regionStateBiases[pair.Key] = pair.Value * regionWeight;
            }

            foreach (var pair in localStateBiases)
            {
                localStateBiases[pair.Key] = pair.Value * localWeight;
            }

            // Combine all bias types with conflict resolution
            HashSet<int> allStates = new HashSet<int>();
            foreach (var state in globalStateBiases.Keys) allStates.Add(state);
            foreach (var state in regionStateBiases.Keys) allStates.Add(state);
            foreach (var state in localStateBiases.Keys) allStates.Add(state);

            foreach (int state in allStates)
            {
                float bias = 0f;
                bool hasGlobal = globalStateBiases.TryGetValue(state, out float globalBias);
                bool hasRegion = regionStateBiases.TryGetValue(state, out float regionBias);
                bool hasLocal = localStateBiases.TryGetValue(state, out float localBias);

                // Simple case: only one source
                if (hasGlobal && !hasRegion && !hasLocal)
                    bias = globalBias;
                else if (!hasGlobal && hasRegion && !hasLocal)
                    bias = regionBias;
                else if (!hasGlobal && !hasRegion && hasLocal)
                    bias = localBias;
                else
                {
                    // Complex case: multiple sources
                    // Start with global bias if available
                    if (hasGlobal)
                        bias = globalBias;

                    // Add region bias with sign checking
                    if (hasRegion)
                    {
                        if (bias * regionBias < 0) // Opposing signs
                        {
                            // Keep the stronger bias
                            if (Mathf.Abs(regionBias) > Mathf.Abs(bias))
                                bias = regionBias;
                        }
                        else
                        {
                            // Same sign or one is zero, use weighted blend
                            float weight1 = Mathf.Abs(bias);
                            float weight2 = Mathf.Abs(regionBias);
                            float total = weight1 + weight2;

                            if (total > 0)
                                bias = (bias * weight1 + regionBias * weight2) / total;
                        }
                    }

                    // Add local bias with sign checking (local has highest priority)
                    if (hasLocal)
                    {
                        if (bias * localBias < 0) // Opposing signs
                        {
                            // Local bias always wins conflicts
                            bias = localBias;
                        }
                        else
                        {
                            // Same sign or one is zero, use weighted blend with local priority
                            float weight1 = Mathf.Abs(bias);
                            float weight2 = Mathf.Abs(localBias) * 1.5f; // Local priority multiplier
                            float total = weight1 + weight2;

                            if (total > 0)
                                bias = (bias * weight1 + localBias * weight2) / total;
                        }
                    }
                }

                // Only add significant biases
                if (Mathf.Abs(bias) > 0.01f)
                    normalizedBiases[state] = Mathf.Clamp(bias, -1f, 1f);
            }

            return normalizedBiases;
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

            // Reset dirty chunks
            dirtyChunks.Clear();

        }
    }
}