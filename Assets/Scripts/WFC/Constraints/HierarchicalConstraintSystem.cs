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
 */
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using NUnit.Framework;

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

        // Local cell constraints - NEW: explicit local constraint support 
        private Dictionary<Vector3Int, Dictionary<Vector3Int, Dictionary<int, float>>> localConstraints =
            new Dictionary<Vector3Int, Dictionary<Vector3Int, Dictionary<int, float>>>();

        // Configuration
        private int chunkSize;
        private float globalWeight = 0.7f;  //1.0f old
        private float regionWeight = 1.0f;
        private float localWeight = 1.0f;

        // Cache for performance optimization
        private Dictionary<Vector3Int, Dictionary<int, float>> chunkStateBiasCache =
            new Dictionary<Vector3Int, Dictionary<int, float>>();

        // Constraint change tracking for efficient updates
        private bool constraintsChanged = false;
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
            constraintsChanged = true;
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
        /// Remove a local constraint
        /// </summary>
        public void RemoveLocalConstraint(Vector3Int chunkPos, Vector3Int cellPos, int state)
        {
            if (localConstraints.TryGetValue(chunkPos, out var cellConstraints) &&
                cellConstraints.TryGetValue(cellPos, out var stateBiases))
            {
                stateBiases.Remove(state);

                // Clean up empty dictionaries
                if (stateBiases.Count == 0)
                {
                    cellConstraints.Remove(cellPos);

                    if (cellConstraints.Count == 0)
                    {
                        localConstraints.Remove(chunkPos);
                    }
                }

                dirtyChunks.Add(chunkPos);
                constraintsChanged = true;
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
        /// Get all constraints affecting a chunk
        /// </summary>
        public List<object> GetConstraintsForChunk(Vector3Int chunkPos)
        {
            List<object> result = new List<object>();

            // Add global constraints that affect this chunk
            foreach (var constraint in globalConstraints)
            {
                // Calculate chunk bounds in world space
                Vector3 chunkMin = new Vector3(
                    chunkPos.x * chunkSize,
                    chunkPos.y * chunkSize,
                    chunkPos.z * chunkSize
                );

                Vector3 chunkMax = new Vector3(
                    (chunkPos.x + 1) * chunkSize,
                    (chunkPos.y + 1) * chunkSize,
                    (chunkPos.z + 1) * chunkSize
                );

                // Check if chunk overlaps with constraint influence area
                Vector3 constraintMin = constraint.WorldCenter - constraint.WorldSize * 0.5f - Vector3.one * constraint.BlendRadius;
                Vector3 constraintMax = constraint.WorldCenter + constraint.WorldSize * 0.5f + Vector3.one * constraint.BlendRadius;

                // Test for overlap
                if (chunkMax.x >= constraintMin.x && chunkMin.x <= constraintMax.x &&
                    chunkMax.y >= constraintMin.y && chunkMin.y <= constraintMax.y &&
                    chunkMax.z >= constraintMin.z && chunkMin.z <= constraintMax.z)
                {
                    result.Add(constraint);
                }
            }

            // Add region constraints that affect this chunk
            foreach (var constraint in regionConstraints)
            {
                // Check if region constraint includes this chunk
                Vector3Int regionEnd = constraint.ChunkPosition + constraint.ChunkSize;

                if (chunkPos.x >= constraint.ChunkPosition.x && chunkPos.x < regionEnd.x &&
                    chunkPos.y >= constraint.ChunkPosition.y && chunkPos.y < regionEnd.y &&
                    chunkPos.z >= constraint.ChunkPosition.z && chunkPos.z < regionEnd.z)
                {
                    result.Add(constraint);
                }
            }

            // Add local constraints if any
            if (localConstraints.TryGetValue(chunkPos, out var cellConstraints))
            {
                foreach (var entry in cellConstraints)
                {
                    result.Add(new { CellPosition = entry.Key, Biases = entry.Value });
                }
            }

            return result;
        }

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
                    totalInfluence += Mathf.Abs(bias);
                }

                // Ensure weight is positive
                weight = Mathf.Max(0.01f, weight);
                stateWeights[state] = weight;
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
                // Get total weight
                float totalWeight = stateWeights.Values.Sum();

                // Calculate collapse threshold based on strongest bias
                float maxBias = biases.Values.Max(Mathf.Abs);
                float collapseThreshold = Mathf.Lerp(0.9f, 0.5f, maxBias);

                // Find if have a highly dominant state
                foreach (var state in stateWeights.Keys.ToList())
                {
                    float normalizedWeight = stateWeights[state] / totalWeight;

                    // If one state is strongly preferred, collapse to it
                    if (normalizedWeight > collapseThreshold)
                    {
                        cell.Collapse(state);
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
                MaxHeight = chunkSize,    // worldSize.y * chunkSize,
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
                MinHeight = 0,              // worldSize.y * chunkSize * 0.3f,
                MaxHeight = chunkSize,      //worldSize.y * chunkSize
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
        /// NEW: Create gradient transition between two regions
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

        /// <summary>
        /// Create a feature at a specific location
        /// </summary>
        public void CreateFeature(Vector3Int chunkPos, Vector3 internalOrigin, Vector3 internalSize,
                                 Dictionary<int, float> stateBiases, string name = "Feature", float strength = 0.8f)
        {
            RegionConstraint feature = new RegionConstraint()
            {
                Name = name,
                Type = RegionType.Feature,
                ChunkPosition = chunkPos,
                ChunkSize = Vector3Int.one, // Single chunk
                InternalOrigin = internalOrigin,
                InternalSize = internalSize,
                Strength = strength
            };

            // Add biases
            foreach (var pair in stateBiases)
            {
                feature.StateBiases[pair.Key] = pair.Value;
            }

            AddRegionConstraint(feature);
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

            // Group constraints by type for weighted normalization
            Dictionary<string, List<KeyValuePair<int, float>>> constraintTypeGroups =
                new Dictionary<string, List<KeyValuePair<int, float>>>();

            // Track constraint weights by type
            Dictionary<string, float> typeWeights = new Dictionary<string, float>();
            typeWeights["global"] = globalWeight;
            typeWeights["region"] = regionWeight;
            typeWeights["local"] = localWeight;

            // Get global constraint influences
            constraintTypeGroups["global"] = new List<KeyValuePair<int, float>>();
            foreach (var constraint in globalConstraints)
            {
                for (int state = 0; state < maxStates; state++)
                {
                    float bias = constraint.GetStateBias(state, worldPosition);
                    if (Mathf.Abs(bias) > 0.01f)
                    {
                        constraintTypeGroups["global"].Add(
                            new KeyValuePair<int, float>(state, bias));
                    }
                }
            }

            // Get region constraint influences
            constraintTypeGroups["region"] = new List<KeyValuePair<int, float>>();
            foreach (var constraint in regionConstraints)
            {
                for (int state = 0; state < maxStates; state++)
                {
                    float bias = constraint.GetStateBias(state, chunkPosition,
                                 localPosition, chunkSize);
                    if (Mathf.Abs(bias) > 0.01f)
                    {
                        constraintTypeGroups["region"].Add(
                            new KeyValuePair<int, float>(state, bias));
                    }
                }
            }

            // Get local constraint influences
            constraintTypeGroups["local"] = new List<KeyValuePair<int, float>>();
            if (localConstraints.TryGetValue(chunkPosition, out var cellConstraints) &&
                cellConstraints.TryGetValue(localPosition, out var stateBiases))
            {
                foreach (var pair in stateBiases)
                {
                    constraintTypeGroups["local"].Add(
                        new KeyValuePair<int, float>(pair.Key, pair.Value));
                }
            }

            // Normalize and combine biases with conflict resolution
            // Normalize within each constraint type
            Dictionary<string, Dictionary<int, float>> normalizedTypeGroups =
                new Dictionary<string, Dictionary<int, float>>();

            foreach (var groupEntry in constraintTypeGroups)
            {
                string type = groupEntry.Key;
                var biases = groupEntry.Value;

                if (biases.Count == 0) continue;

                // Group by state
                Dictionary<int, List<float>> stateGroups = new Dictionary<int, List<float>>();
                foreach (var bias in biases)
                {
                    if (!stateGroups.ContainsKey(bias.Key))
                        stateGroups[bias.Key] = new List<float>();

                    stateGroups[bias.Key].Add(bias.Value);
                }

                // Combine biases for each state (using sign-preserving average)
                normalizedTypeGroups[type] = new Dictionary<int, float>();
                foreach (var stateGroup in stateGroups)
                {
                    int state = stateGroup.Key;
                    List<float> values = stateGroup.Value;

                    // Sign-preserving average (preserves bias direction)
                    float sum = 0;
                    foreach (float val in values)
                        sum += val;

                    float avg = sum / values.Count;
                    normalizedTypeGroups[type][state] = avg;
                }
            }

            // Combine across constraint types, with conflict detection
            foreach (var typeGroup in normalizedTypeGroups)
            {
                string type = typeGroup.Key;
                float typeWeight = typeWeights[type];

                foreach (var stateBias in typeGroup.Value)
                {
                    int state = stateBias.Key;
                    float bias = stateBias.Value * typeWeight;

                    if (!normalizedBiases.ContainsKey(state))
                        normalizedBiases[state] = 0;

                    // Check for potential conflict (opposing signs)
                    if (normalizedBiases[state] * bias < 0)
                    {
                        // Conflict detected - take the stronger bias
                        if (Mathf.Abs(bias) > Mathf.Abs(normalizedBiases[state]))
                            normalizedBiases[state] = bias;
                        // Otherwise keep existing bias
                    }
                    else
                    {
                        // No conflict - weighted blend (not simple addition)
                        float existingWeight = Mathf.Abs(normalizedBiases[state]);
                        float newWeight = Mathf.Abs(bias);
                        float totalWeight = existingWeight + newWeight;

                        if (totalWeight > 0)
                        {
                            normalizedBiases[state] =
                                (normalizedBiases[state] * existingWeight + bias * newWeight) / totalWeight;
                        }
                        else
                        {
                            normalizedBiases[state] += bias;
                        }
                    }
                }
            }

            // Clamp final values to reasonable range
            foreach (int state in normalizedBiases.Keys.ToList())
            {
                normalizedBiases[state] = Mathf.Clamp(normalizedBiases[state], -1f, 1f);
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
            constraintsChanged = true;

            // Mark all chunks as dirty
            foreach (var chunk in chunkStateBiasCache.Keys)
            {
                dirtyChunks.Add(chunk);
            }
        }

        /// <summary>
        /// Visualize constraints at a given world position
        /// </summary>
        public Dictionary<string, object> VisualizeAtPosition(Vector3 worldPos, int maxStates)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            // Calculate chunk position
            Vector3Int chunkPos = new Vector3Int(
                Mathf.FloorToInt(worldPos.x / chunkSize),
                Mathf.FloorToInt(worldPos.y / chunkSize),
                Mathf.FloorToInt(worldPos.z / chunkSize)
            );

            // Calculate local position
            Vector3Int localPos = new Vector3Int(
                Mathf.FloorToInt(worldPos.x) % chunkSize,
                Mathf.FloorToInt(worldPos.y) % chunkSize,
                Mathf.FloorToInt(worldPos.z) % chunkSize
            );

            // Get state biases
            Dictionary<int, float> biases = GetStateBiases(worldPos, chunkPos, localPos, maxStates);

            // Add position info
            result["worldPosition"] = worldPos;
            result["chunkPosition"] = chunkPos;
            result["localPosition"] = localPos;

            // Add biases
            Dictionary<int, float> stateBiases = new Dictionary<int, float>();
            foreach (var pair in biases)
            {
                stateBiases[pair.Key] = pair.Value;
            }
            result["stateBiases"] = stateBiases;

            // Add active constraints
            List<Dictionary<string, object>> activeConstraints = new List<Dictionary<string, object>>();
            
            // Check global constraints
            foreach (var constraint in globalConstraints)
            {
                float influence = constraint.GetInfluenceAt(worldPos);
                if (influence > 0.01f)
                {
                    Dictionary<string, object> constraintInfo = new Dictionary<string, object>();
                    constraintInfo["name"] = constraint.Name;
                    constraintInfo["type"] = constraint.Type.ToString();
                    constraintInfo["strength"] = constraint.Strength;
                    constraintInfo["influence"] = influence;
                    
                    Dictionary<int, float> constraintBiases = new Dictionary<int, float>();
                    for (int state = 0; state < maxStates; state++)
                    {
                        float bias = constraint.GetStateBias(state, worldPos);
                        if (Mathf.Abs(bias) > 0.01f)
                        {
                            constraintBiases[state] = bias;
                        }
                    }
                    
                    constraintInfo["biases"] = constraintBiases;
                    activeConstraints.Add(constraintInfo);
                }
            }
            
            // Check region constraints
            foreach (var constraint in regionConstraints)
            {
                float influence = constraint.GetInfluenceAt(chunkPos, (Vector3)localPos, chunkSize);
                if (influence > 0.01f)
                {
                    Dictionary<string, object> constraintInfo = new Dictionary<string, object>();
                    constraintInfo["name"] = constraint.Name;
                    constraintInfo["type"] = constraint.Type.ToString();
                    constraintInfo["strength"] = constraint.Strength;
                    constraintInfo["influence"] = influence;
                    
                    Dictionary<int, float> constraintBiases = new Dictionary<int, float>();
                    for (int state = 0; state < maxStates; state++)
                    {
                        float bias = constraint.GetStateBias(state, chunkPos, (Vector3)localPos, chunkSize);
                        if (Mathf.Abs(bias) > 0.01f)
                        {
                            constraintBiases[state] = bias;
                        }
                    }
                    
                    constraintInfo["biases"] = constraintBiases;
                    activeConstraints.Add(constraintInfo);
                }
            }
            
            // Check local constraints
            if (localConstraints.TryGetValue(chunkPos, out var cellConstraints) &&
                cellConstraints.TryGetValue(localPos, out var stateConstraints))
            {
                Dictionary<string, object> constraintInfo = new Dictionary<string, object>();
                constraintInfo["name"] = "Local Cell Constraint";
                constraintInfo["type"] = "LocalCell";
                constraintInfo["strength"] = 1.0f;
                constraintInfo["influence"] = 1.0f;
                
                Dictionary<int, float> constraintBiases = new Dictionary<int, float>();
                foreach (var pair in stateConstraints)
                {
                    constraintBiases[pair.Key] = pair.Value;
                }
                
                constraintInfo["biases"] = constraintBiases;
                activeConstraints.Add(constraintInfo);
            }
            
            result["activeConstraints"] = activeConstraints;
            
            return result;
        }
        
        /// <summary>
        /// Export constraints to a serializable format
        /// </summary>
        public string ExportConstraints()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            
            // Export global constraints
            sb.AppendLine("# Global Constraints");
            foreach (var constraint in globalConstraints)
            {
                sb.AppendLine($"G:{constraint.Name}:{constraint.Type}");
                sb.AppendLine($"  Center:{constraint.WorldCenter.x},{constraint.WorldCenter.y},{constraint.WorldCenter.z}");
                sb.AppendLine($"  Size:{constraint.WorldSize.x},{constraint.WorldSize.y},{constraint.WorldSize.z}");
                sb.AppendLine($"  Blend:{constraint.BlendRadius}");
                sb.AppendLine($"  Strength:{constraint.Strength}");
                
                // Export biases
                sb.Append("  Biases:");
                foreach (var bias in constraint.StateBiases)
                {
                    sb.Append($"{bias.Key}:{bias.Value},");
                }
                sb.AppendLine();
                sb.AppendLine();
            }
            
            // Export region constraints
            sb.AppendLine("# Region Constraints");
            foreach (var constraint in regionConstraints)
            {
                sb.AppendLine($"R:{constraint.Name}:{constraint.Type}");
                sb.AppendLine($"  Position:{constraint.ChunkPosition.x},{constraint.ChunkPosition.y},{constraint.ChunkPosition.z}");
                sb.AppendLine($"  Size:{constraint.ChunkSize.x},{constraint.ChunkSize.y},{constraint.ChunkSize.z}");
                sb.AppendLine($"  Internal:{constraint.InternalOrigin.x},{constraint.InternalOrigin.y},{constraint.InternalOrigin.z}:{constraint.InternalSize.x},{constraint.InternalSize.y},{constraint.InternalSize.z}");
                sb.AppendLine($"  Gradient:{constraint.Gradient}");
                sb.AppendLine($"  Strength:{constraint.Strength}");
                sb.AppendLine($"  States:{constraint.SourceState}:{constraint.TargetState}");
                
                // Export biases
                sb.Append("  Biases:");
                foreach (var bias in constraint.StateBiases)
                {
                    sb.Append($"{bias.Key}:{bias.Value},");
                }
                sb.AppendLine();
                sb.AppendLine();
            }
            
            // Export local constraints
            sb.AppendLine("# Local Constraints");
            foreach (var chunkEntry in localConstraints)
            {
                Vector3Int chunkPos = chunkEntry.Key;
                
                foreach (var cellEntry in chunkEntry.Value)
                {
                    Vector3Int cellPos = cellEntry.Key;
                    
                    sb.AppendLine($"L:{chunkPos.x},{chunkPos.y},{chunkPos.z}:{cellPos.x},{cellPos.y},{cellPos.z}");
                    
                    // Export biases
                    sb.Append("  Biases:");
                    foreach (var bias in cellEntry.Value)
                    {
                        sb.Append($"{bias.Key}:{bias.Value},");
                    }
                    sb.AppendLine();
                }
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Import constraints from serialized format
        /// </summary>
        public bool ImportConstraints(string data)
        {
            try
            {
                // Clear existing constraints
                ClearConstraints();
                
                // Split into lines
                string[] lines = data.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                
                // Parse each line
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    
                    // Skip comments and empty lines
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;
                        
                    // Parse global constraint
                    if (line.StartsWith("G:"))
                    {
                        // Parse constraint header
                        string[] parts = line.Substring(2).Split(':');
                        string name = parts[0];
                        ConstraintType type = (ConstraintType)System.Enum.Parse(typeof(ConstraintType), parts[1]);
                        
                        GlobalConstraint constraint = new GlobalConstraint
                        {
                            Name = name,
                            Type = type
                        };
                        
                        // Parse parameters from next lines
                        while (++i < lines.Length && lines[i].StartsWith("  "))
                        {
                            string paramLine = lines[i].Trim();
                            
                            if (paramLine.StartsWith("Center:"))
                            {
                                string[] coords = paramLine.Substring(7).Split(',');
                                constraint.WorldCenter = new Vector3(
                                    float.Parse(coords[0]),
                                    float.Parse(coords[1]),
                                    float.Parse(coords[2])
                                );
                            }
                            else if (paramLine.StartsWith("Size:"))
                            {
                                string[] dims = paramLine.Substring(5).Split(',');
                                constraint.WorldSize = new Vector3(
                                    float.Parse(dims[0]),
                                    float.Parse(dims[1]),
                                    float.Parse(dims[2])
                                );
                            }
                            else if (paramLine.StartsWith("Blend:"))
                            {
                                constraint.BlendRadius = float.Parse(paramLine.Substring(6));
                            }
                            else if (paramLine.StartsWith("Strength:"))
                            {
                                constraint.Strength = float.Parse(paramLine.Substring(9));
                            }
                            else if (paramLine.StartsWith("Biases:"))
                            {
                                string[] biases = paramLine.Substring(7).Split(',');
                                foreach (string bias in biases)
                                {
                                    if (string.IsNullOrEmpty(bias))
                                        continue;
                                        
                                    string[] biasParts = bias.Split(':');
                                    int state = int.Parse(biasParts[0]);
                                    float value = float.Parse(biasParts[1]);
                                    
                                    constraint.StateBiases[state] = value;
                                }
                            }
                        }
                        
                        // Adjust index since increment at the end of the loop
                        i--;
                        
                        // Add constraint
                        AddGlobalConstraint(constraint);
                    }
                    // Parse region constraint
                    else if (line.StartsWith("R:"))
                    {
                        // Parse constraint header
                        string[] parts = line.Substring(2).Split(':');
                        string name = parts[0];
                        RegionType type = (RegionType)System.Enum.Parse(typeof(RegionType), parts[1]);
                        
                        RegionConstraint constraint = new RegionConstraint
                        {
                            Name = name,
                            Type = type
                        };
                        
                        // Parse parameters from next lines
                        while (++i < lines.Length && lines[i].StartsWith("  "))
                        {
                            string paramLine = lines[i].Trim();
                            
                            if (paramLine.StartsWith("Position:"))
                            {
                                string[] coords = paramLine.Substring(9).Split(',');
                                constraint.ChunkPosition = new Vector3Int(
                                    int.Parse(coords[0]),
                                    int.Parse(coords[1]),
                                    int.Parse(coords[2])
                                );
                            }
                            else if (paramLine.StartsWith("Size:"))
                            {
                                string[] dims = paramLine.Substring(5).Split(',');
                                constraint.ChunkSize = new Vector3Int(
                                    int.Parse(dims[0]),
                                    int.Parse(dims[1]),
                                    int.Parse(dims[2])
                                );
                            }
                            else if (paramLine.StartsWith("Internal:"))
                            {
                                string[] parts2 = paramLine.Substring(9).Split(':');
                                
                                string[] origin = parts2[0].Split(',');
                                constraint.InternalOrigin = new Vector3(
                                    float.Parse(origin[0]),
                                    float.Parse(origin[1]),
                                    float.Parse(origin[2])
                                );
                                
                                string[] size = parts2[1].Split(',');
                                constraint.InternalSize = new Vector3(
                                    float.Parse(size[0]),
                                    float.Parse(size[1]),
                                    float.Parse(size[2])
                                );
                            }
                            else if (paramLine.StartsWith("Gradient:"))
                            {
                                constraint.Gradient = float.Parse(paramLine.Substring(9));
                            }
                            else if (paramLine.StartsWith("Strength:"))
                            {
                                constraint.Strength = float.Parse(paramLine.Substring(9));
                            }
                            else if (paramLine.StartsWith("States:"))
                            {
                                string[] states = paramLine.Substring(7).Split(':');
                                constraint.SourceState = int.Parse(states[0]);
                                constraint.TargetState = int.Parse(states[1]);
                            }
                            else if (paramLine.StartsWith("Biases:"))
                            {
                                string[] biases = paramLine.Substring(7).Split(',');
                                foreach (string bias in biases)
                                {
                                    if (string.IsNullOrEmpty(bias))
                                        continue;
                                        
                                    string[] biasParts = bias.Split(':');
                                    int state = int.Parse(biasParts[0]);
                                    float value = float.Parse(biasParts[1]);
                                    
                                    constraint.StateBiases[state] = value;
                                }
                            }
                        }
                        
                        // Adjust index since increment at the end of the loop
                        i--;
                        
                        // Add constraint
                        AddRegionConstraint(constraint);
                    }
                    // Parse local constraint
                    else if (line.StartsWith("L:"))
                    {
                        // Parse position
                        string[] parts = line.Substring(2).Split(':');
                        string[] chunkCoords = parts[0].Split(',');
                        Vector3Int chunkPos = new Vector3Int(
                            int.Parse(chunkCoords[0]),
                            int.Parse(chunkCoords[1]),
                            int.Parse(chunkCoords[2])
                        );
                        
                        string[] cellCoords = parts[1].Split(',');
                        Vector3Int cellPos = new Vector3Int(
                            int.Parse(cellCoords[0]),
                            int.Parse(cellCoords[1]),
                            int.Parse(cellCoords[2])
                        );
                        
                        // Parse biases
                        if (++i < lines.Length && lines[i].TrimStart().StartsWith("Biases:"))
                        {
                            string paramLine = lines[i].Trim();
                            string[] biases = paramLine.Substring(7).Split(',');
                            
                            foreach (string bias in biases)
                            {
                                if (string.IsNullOrEmpty(bias))
                                    continue;
                                    
                                string[] biasParts = bias.Split(':');
                                int state = int.Parse(biasParts[0]);
                                float value = float.Parse(biasParts[1]);
                                
                                // Add local constraint
                                AddLocalConstraint(chunkPos, cellPos, state, value);
                            }
                        }
                    }
                }
                
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error importing constraints: {ex.Message}");
                return false;
            }
        }
    }
}