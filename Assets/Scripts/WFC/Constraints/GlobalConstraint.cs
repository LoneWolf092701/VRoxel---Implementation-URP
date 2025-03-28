// Assets/Scripts/WFC/Core/GlobalConstraint.cs
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

        // For river/path constraints
        public List<Vector3> ControlPoints = new List<Vector3>();
        public float PathWidth = 5f;

        /// <summary>
        /// Calculates the influence of this global constraint at a given world position.
        /// Returns 0-1 value representing the constraint's strength at this point.
        /// </summary>
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
                return Strength; // Full influence within core area
            }

            // Calculate distance to core area for blending
            float distX = Mathf.Max(0, Mathf.Max(min.x - worldPosition.x, worldPosition.x - max.x));
            float distY = Mathf.Max(0, Mathf.Max(min.y - worldPosition.y, worldPosition.y - max.y));
            float distZ = Mathf.Max(0, Mathf.Max(min.z - worldPosition.z, worldPosition.z - max.z));

            float distance = Mathf.Sqrt(distX * distX + distY * distY + distZ * distZ);

            // If beyond blend radius, no influence
            if (distance > BlendRadius)
                return 0f;

            // Linear falloff within blend radius
            float influence = 1.0f - (distance / BlendRadius);
            return influence * Strength;
        }

        /// <summary>
        /// Gets the state bias for a specific state at a given position
        /// </summary>
        public float GetStateBias(int state, Vector3 worldPosition)
        {
            // Get base influence at this point
            float influence = GetInfluenceAt(worldPosition);

            // If no influence or no bias for this state, return 0
            if (influence <= 0 || !StateBiases.ContainsKey(state))
                return 0f;

            // Apply type-specific modifications based on constraint type
            switch (Type)
            {
                case ConstraintType.BiomeRegion:
                    // For biome regions, just use the stored bias
                    return StateBiases[state] * influence;

                case ConstraintType.HeightMap:
                    // Height-based constraint (e.g., mountains, valleys)
                    float normalizedHeight = Mathf.InverseLerp(0, 1, worldPosition.y / MaxHeight);
                    return AdjustBiasByHeight(state, normalizedHeight) * influence;

                case ConstraintType.RiverPath:
                    // Calculate distance to nearest point on path
                    float distanceToPath = GetDistanceToPath(worldPosition);
                    if (distanceToPath > PathWidth + BlendRadius)
                        return 0;

                    float pathInfluence = 1.0f - Mathf.Clamp01(distanceToPath / (PathWidth + BlendRadius));
                    return StateBiases[state] * pathInfluence * influence;

                default:
                    return StateBiases[state] * influence;
            }
        }

        /// <summary>
        /// Adjusts bias based on height for height-based constraints
        /// </summary>
        private float AdjustBiasByHeight(int state, float normalizedHeight)
        {
            // This would be implemented with a more sophisticated curve system
            // But for simplicity we'll use a basic approach
            if (!StateBiases.ContainsKey(state))
                return 0f;

            float baseBias = StateBiases[state];

            // Apply a basic height-based adjustment
            // Different states have different height preferences
            switch (state)
            {
                case 0: // Empty/air - prefers higher elevations
                    return baseBias * Mathf.Lerp(0.2f, 1.0f, normalizedHeight);

                case 1: // Ground - prefers middle elevations
                    return baseBias * (1.0f - Mathf.Abs(normalizedHeight - 0.5f) * 2);

                case 4: // Rock - prefers higher elevations
                    return baseBias * Mathf.Lerp(0.3f, 1.0f, normalizedHeight);

                case 3: // Water - prefers lower elevations
                    return baseBias * Mathf.Lerp(1.0f, 0.1f, normalizedHeight);

                default:
                    return baseBias;
            }
        }

        /// <summary>
        /// Calculates the minimum distance from a point to the path (for river/path constraints)
        /// </summary>
        private float GetDistanceToPath(Vector3 position)
        {
            if (ControlPoints.Count < 2)
                return float.MaxValue;

            float minDistance = float.MaxValue;

            // Find distance to each line segment
            for (int i = 0; i < ControlPoints.Count - 1; i++)
            {
                Vector3 a = ControlPoints[i];
                Vector3 b = ControlPoints[i + 1];

                // Find nearest point on line segment
                Vector3 ap = position - a;
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Vector3.Dot(ab, ab));
                Vector3 closest = a + t * ab;

                float distance = Vector3.Distance(position, closest);
                minDistance = Mathf.Min(minDistance, distance);
            }

            return minDistance;
        }
    }

    /// <summary>
    /// Types of global constraints that can influence the generation
    /// </summary>
    public enum ConstraintType
    {
        BiomeRegion,  // Defines a region with specific biome characteristics
        HeightMap,    // Defines height-based features like mountains or valleys
        RiverPath,    // Defines a path for river generation
        Structure     // Defines a large structure placement
    }
}