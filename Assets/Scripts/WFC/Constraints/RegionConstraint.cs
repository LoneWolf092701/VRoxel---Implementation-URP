// Assets/Scripts/WFC/Core/RegionConstraint.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;

namespace WFC.Core
{
    /// <summary>
    /// Represents a medium-scale constraint that operates at the chunk level.
    /// These constraints help bridge the gap between global semantic constraints
    /// and local cell constraints, ensuring coherent transitions between chunks.
    /// </summary>
    [Serializable]
    public class RegionConstraint
    {
        // Region identification
        public string Name;
        public RegionType Type;

        // Region area (in chunk coordinates)
        public Vector3Int ChunkPosition;
        public Vector3Int ChunkSize = Vector3Int.one; // Default to one chunk

        // Internal position within chunk (0-1 normalized coordinates)
        public Vector3 InternalOrigin = Vector3.zero;
        public Vector3 InternalSize = Vector3.one; // Default to entire chunk

        // Constraint parameters
        public float Strength = 1.0f;
        [SerializeField] private SerializableDictionary<int, float> stateBiases = new SerializableDictionary<int, float>();

        // Public property to access the dictionary
        public Dictionary<int, float> StateBiases => stateBiases;
        //public Dictionary<int, float> StateBiases = new Dictionary<int, float>();

        // Special parameters for different region types
        public float Gradient = 0.2f;
        public int SourceState = -1;
        public int TargetState = -1;

        // Cache for faster lookups
        private Bounds _internalBounds;
        private bool _boundsInitialized = false;

        /// <summary>
        /// Gets the influence of this region constraint at a specific local position within a chunk
        /// </summary>
        public float GetInfluenceAt(Vector3Int chunkPos, Vector3 localPosition, int chunkSize)
        {
            // First check if this constraint applies to this chunk
            if (chunkPos.x < ChunkPosition.x || chunkPos.x >= ChunkPosition.x + ChunkSize.x ||
                chunkPos.y < ChunkPosition.y || chunkPos.y >= ChunkPosition.y + ChunkSize.y ||
                chunkPos.z < ChunkPosition.z || chunkPos.z >= ChunkPosition.z + ChunkSize.z)
            {
                return 0f; // Not in this chunk region
            }

            // Initialize bounds if needed
            if (!_boundsInitialized)
            {
                Vector3 boundsCenter = InternalOrigin + InternalSize * 0.5f;
                Vector3 boundsSize = InternalSize;
                _internalBounds = new Bounds(boundsCenter, boundsSize);
                _boundsInitialized = true;
            }

            // Normalize local position (0-1 range)
            Vector3 normalizedPos = new Vector3(
                localPosition.x / chunkSize,
                localPosition.y / chunkSize,
                localPosition.z / chunkSize
            );

            // Fast check if position is within bounds plus gradient
            float distance = DistanceToBounds(normalizedPos, _internalBounds);

            // If within core bounds, full influence
            if (distance <= 0)
                return Strength;

            // If beyond gradient distance, no influence
            if (distance > Gradient)
                return 0f;

            // Linear falloff within gradient
            float influence = 1.0f - (distance / Gradient);
            return influence * Strength;
        }

        /// <summary>
        /// Gets state bias at a specific position within a chunk
        /// </summary>
        public float GetStateBias(int state, Vector3Int chunkPos, Vector3 localPosition, int chunkSize)
        {
            float influence = GetInfluenceAt(chunkPos, localPosition, chunkSize);

            if (influence <= 0f)
                return 0f;

            // Apply type-specific modifications
            switch (Type)
            {
                case RegionType.Transition:
                    // Handle transition regions (e.g., biome transitions)
                    if (state == SourceState || state == TargetState)
                    {
                        // Determine transition progress based on position along gradient
                        Vector3 relativePos = localPosition / chunkSize; // Normalize to 0-1
                        float transitionProgress = Vector3.Dot(relativePos, Vector3.right); // Simplification - transition along X

                        if (state == SourceState)
                            return influence * Mathf.Lerp(1.0f, 0.2f, transitionProgress);
                        else // TargetState
                            return influence * Mathf.Lerp(0.2f, 1.0f, transitionProgress);
                    }
                    return 0f;

                case RegionType.Feature:
                    // Handle specific feature placement
                    if (StateBiases.TryGetValue(state, out float bias))
                        return bias * influence;
                    return 0f;

                default:
                    // Default behavior - use explicit state biases
                    if (StateBiases.TryGetValue(state, out float defaultBias))
                        return defaultBias * influence;
                    return 0f;
            }
        }

        /// <summary>
        /// Calculate distance to bounds, negative if inside
        /// </summary>
        private float DistanceToBounds(Vector3 point, Bounds bounds)
        {
            Vector3 distVec = new Vector3(
                Mathf.Max(bounds.min.x - point.x, point.x - bounds.max.x),
                Mathf.Max(bounds.min.y - point.y, point.y - bounds.max.y),
                Mathf.Max(bounds.min.z - point.z, point.z - bounds.max.z)
            );

            // If inside bounds, return negative distance to closest edge
            if (point.x >= bounds.min.x && point.x <= bounds.max.x &&
                point.y >= bounds.min.y && point.y <= bounds.max.y &&
                point.z >= bounds.min.z && point.z <= bounds.max.z)
            {
                // Find closest distance to any edge
                float minDist = Mathf.Min(
                    Mathf.Min(point.x - bounds.min.x, bounds.max.x - point.x),
                    Mathf.Min(point.y - bounds.min.y, bounds.max.y - point.y),
                    Mathf.Min(point.z - bounds.min.z, bounds.max.z - point.z)
                );
                return -minDist;
            }

            // If any component is negative, it means the point is inside bounds along that axis
            distVec.x = Mathf.Max(0, distVec.x);
            distVec.y = Mathf.Max(0, distVec.y);
            distVec.z = Mathf.Max(0, distVec.z);

            // Return distance to bounds
            return distVec.magnitude;
        }
    }

    /// <summary>
    /// Types of region constraints
    /// </summary>
    public enum RegionType
    {
        Transition,  // Handles transitions between different biomes/features
        Feature,     // Places a specific feature within chunk(s)
        Elevation,   // Controls elevation changes
        Pattern      // Defines repeating patterns
    }
}