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

        // Special parameters for different region types
        public float Gradient = 0.2f;
        public int SourceState = -1;
        public int TargetState = -1;

        // NEW: Direction vector for transitions
        public Vector3 TransitionDirection = Vector3.right;

        // NEW: Noise pattern settings for varied constraints
        public float NoiseScale = 0.1f;
        public float NoiseAmplitude = 0.5f;
        public int NoiseSeed = 0;

        // NEW: Pattern type for repeating patterns
        public enum PatternType { Stripes, Checkerboard, Radial, Spiral, Noise }
        public PatternType Pattern = PatternType.Stripes;
        public float PatternScale = 1.0f;

        // NEW: Terrain feature parameters
        public float ElevationOffset = 0f;
        public float ElevationScale = 1.0f;
        public AnimationCurve HeightCurve;

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

            // NEW: Apply pattern influence based on type
            influence *= GetPatternInfluence(chunkPos, normalizedPos, chunkSize);

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
                        // Determine transition progress based on transition direction
                        Vector3 relativePos = localPosition / chunkSize; // Normalize to 0-1

                        // Calculate relative chunk position
                        Vector3 relativeChunkPos = new Vector3(
                            (chunkPos.x - ChunkPosition.x) / (float)ChunkSize.x,
                            (chunkPos.y - ChunkPosition.y) / (float)ChunkSize.y,
                            (chunkPos.z - ChunkPosition.z) / (float)ChunkSize.z
                        );

                        // Combine chunk position and local position
                        Vector3 globalRelativePos = relativeChunkPos + new Vector3(
                            relativePos.x / ChunkSize.x,
                            relativePos.y / ChunkSize.y,
                            relativePos.z / ChunkSize.z
                        );

                        // Project onto transition direction
                        float transitionProgress = Vector3.Dot(globalRelativePos, TransitionDirection.normalized);

                        // Normalize to 0-1 range
                        transitionProgress = Mathf.Clamp01(transitionProgress);

                        // Add some noise for natural transitions
                        if (NoiseAmplitude > 0)
                        {
                            // Sample noise based on position
                            float noise = HashNoise(globalRelativePos);

                            // Apply noise to transition progress
                            transitionProgress = Mathf.Clamp01(transitionProgress + noise * NoiseAmplitude);
                        }

                        if (state == SourceState)
                            return influence * Mathf.Lerp(1.0f, 0.0f, transitionProgress);
                        else // TargetState
                            return influence * Mathf.Lerp(0.0f, 1.0f, transitionProgress);
                    }
                    return 0f;

                case RegionType.Feature:
                    // Handle specific feature placement
                    if (StateBiases.TryGetValue(state, out float bias))
                        return bias * influence;
                    return 0f;

                case RegionType.Elevation:
                    // Handle elevation-based constraints
                    float elevationVal = ElevationOffset + localPosition.y * ElevationScale / chunkSize;

                    // Use height curve if available
                    if (HeightCurve != null && HeightCurve.length > 0)
                    {
                        elevationVal = HeightCurve.Evaluate(elevationVal);
                    }

                    // Apply state-specific elevation bias
                    if (StateBiases.TryGetValue(state, out float elevBias))
                    {
                        return elevBias * influence * elevationVal;
                    }
                    return 0f;

                case RegionType.Pattern:
                    // Handle pattern-based constraints
                    if (StateBiases.TryGetValue(state, out float patternBias))
                    {
                        // Get pattern value
                        float patternValue = GetPatternValue(chunkPos, localPosition / chunkSize, chunkSize);
                        return patternBias * influence * patternValue;
                    }
                    return 0f;

                default:
                    // Default behavior - use explicit state biases
                    if (StateBiases.TryGetValue(state, out float defaultBias))
                        return defaultBias * influence;
                    return 0f;
            }
        }

        /// <summary>
        /// Get the influence of the pattern at a position
        /// </summary>
        private float GetPatternInfluence(Vector3Int chunkPos, Vector3 normalizedPos, int chunkSize)
        {
            // Default to full influence
            if (Type != RegionType.Pattern)
                return 1f;

            // Apply pattern-specific influence
            float patternValue = GetPatternValue(chunkPos, normalizedPos, chunkSize);

            // Scale for smoother transitions
            return Mathf.SmoothStep(0.5f, 1f, patternValue);
        }

        /// <summary>
        /// Get the pattern value at a position (0-1 range)
        /// </summary>
        private float GetPatternValue(Vector3Int chunkPos, Vector3 normalizedPos, int chunkSize)
        {
            // Calculate global world position for consistent pattern generation
            Vector3 globalPos = new Vector3(
                (chunkPos.x * chunkSize) + (normalizedPos.x * chunkSize),
                (chunkPos.y * chunkSize) + (normalizedPos.y * chunkSize),
                (chunkPos.z * chunkSize) + (normalizedPos.z * chunkSize)
            );

            // Apply pattern scale
            globalPos *= PatternScale;

            // Apply pattern based on global position, ensuring continuity across chunks
            switch (Pattern)
            {
                case PatternType.Stripes:
                    // Use global position for consistent stripes
                    return Mathf.Abs(Mathf.Sin(globalPos.x * Mathf.PI * 2)) > 0.5f ? 1f : 0f;

                case PatternType.Checkerboard:
                    // Use global position for consistent checkerboard
                    int ix = Mathf.FloorToInt(globalPos.x * 2);
                    int iy = Mathf.FloorToInt(globalPos.y * 2);
                    int iz = Mathf.FloorToInt(globalPos.z * 2);
                    return ((ix + iy + iz) % 2 == 0) ? 1f : 0f;

                case PatternType.Radial:
                    // Calculate from a global reference point
                    Vector2 globalCenter = new Vector2(
                        ChunkPosition.x * chunkSize + ChunkSize.x * chunkSize / 2f,
                        ChunkPosition.z * chunkSize + ChunkSize.z * chunkSize / 2f
                    );
                    Vector2 pos2d = new Vector2(globalPos.x, globalPos.z);
                    float distance = Vector2.Distance(globalCenter, pos2d) * 2;
                    return Mathf.Abs(Mathf.Sin(distance * Mathf.PI * 2)) > 0.5f ? 1f : 0f;

                case PatternType.Spiral:
                    // Use consistent global reference point
                    Vector2 globalCenterOffset = new Vector2(
                        globalPos.x - (ChunkPosition.x * chunkSize + ChunkSize.x * chunkSize / 2f),
                        globalPos.z - (ChunkPosition.z * chunkSize + ChunkSize.z * chunkSize / 2f)
                    );
                    float angle = Mathf.Atan2(globalCenterOffset.y, globalCenterOffset.x);
                    float radius = globalCenterOffset.magnitude * 10f;
                    float spiral = Mathf.Sin(angle * 5f + radius * 5f);
                    return spiral > 0f ? 1f : 0f;

                case PatternType.Noise:
                    // Replace cached noise with hash-based noise for better consistency
                    return HashNoise(globalPos);

                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Replace cached noise with deterministic hash-based noise
        /// </summary>
        private float HashNoise(Vector3 pos)
        {
            // A simple but effective hash function for noise
            const float scale = 0.1f;
            uint seed = (uint)NoiseSeed;

            // Scale the position
            float x = pos.x * scale;
            float y = pos.y * scale;
            float z = pos.z * scale;

            // Get cell coordinates
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            int zi = Mathf.FloorToInt(z);

            // Get fractional parts
            float xf = x - xi;
            float yf = y - yi;
            float zf = z - zi;

            // Smoothing function
            float u = xf * xf * (3.0f - 2.0f * xf);
            float v = yf * yf * (3.0f - 2.0f * yf);
            float w = zf * zf * (3.0f - 2.0f * zf);

            // Generate hash values for each corner
            float val000 = Hash(xi, yi, zi, seed);
            float val100 = Hash(xi + 1, yi, zi, seed);
            float val010 = Hash(xi, yi + 1, zi, seed);
            float val110 = Hash(xi + 1, yi + 1, zi, seed);
            float val001 = Hash(xi, yi, zi + 1, seed);
            float val101 = Hash(xi + 1, yi, zi + 1, seed);
            float val011 = Hash(xi, yi + 1, zi + 1, seed);
            float val111 = Hash(xi + 1, yi + 1, zi + 1, seed);

            // Trilinear interpolation
            return Mathf.Lerp(
                Mathf.Lerp(
                    Mathf.Lerp(val000, val100, u),
                    Mathf.Lerp(val010, val110, u),
                    v
                ),
                Mathf.Lerp(
                    Mathf.Lerp(val001, val101, u),
                    Mathf.Lerp(val011, val111, u),
                    v
                ),
                w
            );
        }

        /// <summary>
        /// Deterministic hash function
        /// </summary>
        private float Hash(int x, int y, int z, uint seed)
        {
            uint hash = seed + (uint)x * 73856093 + (uint)y * 19349663 + (uint)z * 83492791;

            // Jenkins hash function
            hash = (hash + 0x7ed55d16) + (hash << 12);
            hash = (hash ^ 0xc761c23c) ^ (hash >> 19);
            hash = (hash + 0x165667b1) + (hash << 5);
            hash = (hash + 0xd3a2646c) ^ (hash << 9);
            hash = (hash + 0xfd7046c5) + (hash << 3);
            hash = (hash ^ 0xb55a4f09) ^ (hash >> 16);

            // Convert to 0-1 range
            return (hash & 0xFFFFFFF) / (float)0xFFFFFFF;
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

        /// <summary>
        /// Clone this constraint
        /// </summary>
        public RegionConstraint Clone()
        {
            RegionConstraint clone = new RegionConstraint
            {
                Name = this.Name,
                Type = this.Type,
                ChunkPosition = this.ChunkPosition,
                ChunkSize = this.ChunkSize,
                InternalOrigin = this.InternalOrigin,
                InternalSize = this.InternalSize,
                Strength = this.Strength,
                Gradient = this.Gradient,
                SourceState = this.SourceState,
                TargetState = this.TargetState,
                TransitionDirection = this.TransitionDirection,
                NoiseScale = this.NoiseScale,
                NoiseAmplitude = this.NoiseAmplitude,
                NoiseSeed = this.NoiseSeed,
                Pattern = this.Pattern,
                PatternScale = this.PatternScale,
                ElevationOffset = this.ElevationOffset,
                ElevationScale = this.ElevationScale
            };

            // Copy height curve if available
            if (this.HeightCurve != null)
            {
                clone.HeightCurve = new AnimationCurve();
                foreach (var key in this.HeightCurve.keys)
                {
                    clone.HeightCurve.AddKey(key);
                }
            }

            // Copy state biases
            foreach (var pair in this.StateBiases)
            {
                clone.StateBiases[pair.Key] = pair.Value;
            }

            return clone;
        }

        /// <summary>
        /// Create transition constraint between states
        /// </summary>
        public static RegionConstraint CreateTransition(
            Vector3Int position, Vector3Int size, int sourceState, int targetState,
            Vector3 direction, string name = "Transition", float strength = 0.7f)
        {
            RegionConstraint constraint = new RegionConstraint
            {
                Name = name,
                Type = RegionType.Transition,
                ChunkPosition = position,
                ChunkSize = size,
                Strength = strength,
                Gradient = 0.5f,
                SourceState = sourceState,
                TargetState = targetState,
                TransitionDirection = new Vector3(direction.x, direction.y, direction.z).normalized
            };

            return constraint;
        }

        /// <summary>
        /// Create feature constraint
        /// </summary>
        public static RegionConstraint CreateFeature(
            Vector3Int position, Vector3 origin, Vector3 size,
            Dictionary<int, float> biases, string name = "Feature", float strength = 0.8f)
        {
            RegionConstraint constraint = new RegionConstraint
            {
                Name = name,
                Type = RegionType.Feature,
                ChunkPosition = position,
                ChunkSize = Vector3Int.one,
                InternalOrigin = origin,
                InternalSize = size,
                Strength = strength
            };

            // Add biases
            foreach (var pair in biases)
            {
                constraint.StateBiases[pair.Key] = pair.Value;
            }

            return constraint;
        }

        /// <summary>
        /// Create pattern constraint
        /// </summary>
        public static RegionConstraint CreatePattern(
            Vector3Int position, Vector3Int size, PatternType pattern,
            Dictionary<int, float> biases, string name = "Pattern", float strength = 0.7f)
        {
            RegionConstraint constraint = new RegionConstraint
            {
                Name = name,
                Type = RegionType.Pattern,
                ChunkPosition = position,
                ChunkSize = size,
                Strength = strength,
                Pattern = pattern,
                PatternScale = 1.0f
            };

            // Add biases
            foreach (var pair in biases)
            {
                constraint.StateBiases[pair.Key] = pair.Value;
            }

            return constraint;
        }

        /// <summary>
        /// Create elevation constraint
        /// </summary>
        public static RegionConstraint CreateElevation(
            Vector3Int position, Vector3Int size, float offset, float scale,
            Dictionary<int, float> biases, string name = "Elevation", float strength = 0.7f)
        {
            RegionConstraint constraint = new RegionConstraint
            {
                Name = name,
                Type = RegionType.Elevation,
                ChunkPosition = position,
                ChunkSize = size,
                Strength = strength,
                ElevationOffset = offset,
                ElevationScale = scale
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