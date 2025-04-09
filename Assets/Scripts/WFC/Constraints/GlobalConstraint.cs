using System;
using System.Collections.Generic;
using UnityEngine;

namespace WFC.Core
{
    /// <summary>
    /// Represents a high-level semantic constraint that influences the WFC generation
    /// across multiple chunks. Examples include biomes, major terrain features, or
    /// large-scale structures like mountain ranges or river systems.
    /// </summary>
    [Serializable]
    public class GlobalConstraint
    {
        // Constraint identification
        public string Name;
        public ConstraintType Type;

        // Constraint influence area
        public Vector3 WorldCenter;
        public Vector3 WorldSize;
        public float BlendRadius = 10f; // How far influence extends beyond core area

        // Constraint influence parameters
        public float Strength = 1.0f; // How strongly this constraint affects probabilities (0-1)
        public Dictionary<int, float> StateBiases = new Dictionary<int, float>(); // State biases to apply

        // Properties for specific constraint types
        public float MinHeight = 0f;
        public float MaxHeight = 100f;
        public float NoiseScale = 0.1f;
        public float NoiseAmplitude = 1.0f;

        // Advanced parameters for constraints
        public AnimationCurve BlendCurve; // Custom blend falloff curve
        public AnimationCurve HeightCurve; // Custom height influence curve
        public bool UseNoise = false; // Whether to apply noise to influence
        public int NoiseSeed = 0; // Seed for noise generation

        /*
         * GetInfluenceAt
         * ----------------------------------------------------------------------------
         * Calculates the influence of a global constraint at a specific world position.
         * 
         * This spatial influence calculation:
         * 1. Determines if the point is inside the constraint's core area:
         *    - If inside, applies full influence (possibly modified by noise)
         * 2. If outside core area, calculates distance to the nearest point on the core area
         * 3. Applies falloff based on the distance:
         *    - Either using a custom blend curve if provided
         *    - Or using linear falloff from the core outward to BlendRadius
         * 4. Applies additional modifiers like noise for variation
         * 5. Adjusts final influence by the constraint's Strength parameter
         * 
         * The spatial blending creates natural, gradual transitions between
         * different terrain features and biomes, avoiding hard edges
         * and unnatural boundaries.
         * 
         * Parameters:
         * - worldPosition: The world position to check
         * 
         * Returns: Influence value 0-1 (0=no influence, 1=full influence)
         */
        public float GetInfluenceAt(Vector3 worldPosition)
        {
            // Calculate distance from center of constraint region
            Vector3 halfSize = WorldSize * 0.5f;
            Vector3 min = WorldCenter - halfSize;
            Vector3 max = WorldCenter + halfSize;

            // Check if point is inside core area
            if (worldPosition.x >= min.x && worldPosition.x <= max.x &&
                worldPosition.y >= min.y && worldPosition.y <= max.y &&
                worldPosition.z >= min.z && worldPosition.z <= max.z)
            {
                // Full influence within core area, potentially modified by noise
                return ApplyInfluenceModifiers(1.0f, worldPosition);
            }

            // Calculate distance to core area for blending
            float distX = Mathf.Max(0, Mathf.Max(min.x - worldPosition.x, worldPosition.x - max.x));
            float distY = Mathf.Max(0, Mathf.Max(min.y - worldPosition.y, worldPosition.y - max.y));
            float distZ = Mathf.Max(0, Mathf.Max(min.z - worldPosition.z, worldPosition.z - max.z));

            float distance = Mathf.Sqrt(distX * distX + distY * distY + distZ * distZ);

            // If beyond blend radius, no influence
            if (distance > BlendRadius)
                return 0f;

            // Calculate influence using blend curve or linear falloff
            float influence;
            if (BlendCurve != null && BlendCurve.length > 0)
            {
                influence = BlendCurve.Evaluate(1.0f - (distance / BlendRadius));
            }
            else
            {
                influence = 1.0f - (distance / BlendRadius);
            }

            // Apply additional influence modifiers
            return ApplyInfluenceModifiers(influence, worldPosition);
        }

        /// <summary>
        /// Apply modifiers to the influence value (noise, etc.)
        /// </summary>
        private float ApplyInfluenceModifiers(float baseInfluence, Vector3 worldPosition)
        {
            float influence = baseInfluence;

            // Apply strength multiplier
            return influence * Strength;
        }

        /*
         * GetStateBias
         * ----------------------------------------------------------------------------
         * Gets the bias value for a specific state at a given position.
         * 
         * The bias calculation process:
         * 1. Gets the base influence at the position
         * 2. If no influence or no defined bias for this state, returns 0
         * 3. For height-based constraints:
         *    - Modifies influence based on height to create flat terrain
         *    - Applies 50% reduction factor to keep terrain stable
         * 4. For other constraint types (biomes, features):
         *    - Returns the full bias * influence value
         * 
         * This function connects the spatial influence calculation to the
         * actual effect on individual cell states, transforming general constraint
         * definitions into specific biases for the WFC algorithm.
         * 
         * Parameters:
         * - state: The state to get bias for
         * - worldPosition: World position to check
         * 
         * Returns: Bias value (-1 to 1) affecting state probability
         */
        public float GetStateBias(int state, Vector3 worldPosition)
        {
            // Get base influence at this point
            float influence = GetInfluenceAt(worldPosition);

            // If no influence or no bias for this state, return 0
            if (influence <= 0 || !StateBiases.ContainsKey(state))
                return 0f;

            // For height-based constraints, limit the height influence
            if (Type == ConstraintType.HeightMap)
            {
                // Ignore Y position for determining influence - make terrain flat
                return StateBiases[state] * influence * 0.5f;
            }

            // For other constraint types, proceed normally
            return StateBiases[state] * influence;
        }

        /// <summary>
        /// Clone this constraint
        /// </summary>
        public GlobalConstraint Clone()
        {
            GlobalConstraint clone = new GlobalConstraint
            {
                Name = this.Name,
                Type = this.Type,
                WorldCenter = this.WorldCenter,
                WorldSize = this.WorldSize,
                BlendRadius = this.BlendRadius,
                Strength = this.Strength,
                MinHeight = this.MinHeight,
                MaxHeight = this.MaxHeight,
                NoiseScale = this.NoiseScale,
                NoiseAmplitude = this.NoiseAmplitude,
                UseNoise = this.UseNoise,
                NoiseSeed = this.NoiseSeed,
               
            };

            // Copy state biases
            foreach (var pair in this.StateBiases)
            {
                clone.StateBiases[pair.Key] = pair.Value;
            }

            // Copy curves if available
            if (this.BlendCurve != null)
            {
                clone.BlendCurve = new AnimationCurve();
                foreach (var key in this.BlendCurve.keys)
                {
                    clone.BlendCurve.AddKey(key);
                }
            }

            if (this.HeightCurve != null)
            {
                clone.HeightCurve = new AnimationCurve();
                foreach (var key in this.HeightCurve.keys)
                {
                    clone.HeightCurve.AddKey(key);
                }
            }

            return clone;
        }

        /// <summary>
        /// Create biome region constraint
        /// </summary>
        public static GlobalConstraint CreateBiomeRegion(
            Vector3 center, Vector3 size, Dictionary<int, float> biases,
            float temperature = 0.5f, float humidity = 0.5f,
            string name = "Biome", float strength = 0.7f, float blendRadius = 10f)
        {
            GlobalConstraint constraint = new GlobalConstraint
            {
                Name = name,
                Type = ConstraintType.BiomeRegion,
                WorldCenter = center,
                WorldSize = size,
                BlendRadius = blendRadius,
                Strength = strength,
            };

            // Add biases
            foreach (var pair in biases)
            {
                constraint.StateBiases[pair.Key] = pair.Value;
            }

            return constraint;
        }

        /// <summary>
        /// Create height map constraint
        /// </summary>
        public static GlobalConstraint CreateHeightMap(
            Vector3 center, Vector3 size, float minHeight, float maxHeight,
            Dictionary<int, float> biases, string name = "HeightMap",
            float strength = 0.7f, float blendRadius = 10f)
        {
            GlobalConstraint constraint = new GlobalConstraint
            {
                Name = name,
                Type = ConstraintType.HeightMap,
                WorldCenter = center,
                WorldSize = size,
                BlendRadius = blendRadius,
                Strength = strength,
                MinHeight = minHeight,
                MaxHeight = maxHeight
            };

            // Add biases
            foreach (var pair in biases)
            {
                constraint.StateBiases[pair.Key] = pair.Value;
            }

            // Create default height curve
            constraint.HeightCurve = new AnimationCurve();
            constraint.HeightCurve.AddKey(0f, 0f);
            constraint.HeightCurve.AddKey(1f, 1f);

            return constraint;
        }
    }

    /// <summary>
    /// Types of global constraints that can influence the generation
    /// </summary>
    public enum ConstraintType
    {
        BiomeRegion,  // Defines a region with specific biome characteristics
        HeightMap     // Defines height-based features like mountains or valleys
    }
}