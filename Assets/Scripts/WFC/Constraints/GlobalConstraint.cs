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

        // NEW: Structure-specific parameters
        public Vector3 StructureOrientation = Vector3.forward; // Forward direction of structure
        public Vector3 StructureScale = Vector3.one; // Scale of structure
        public int StructureRotation = 0; // Rotation in degrees

        // NEW: Advanced parameters for constraints
        public AnimationCurve BlendCurve; // Custom blend falloff curve
        public AnimationCurve HeightCurve; // Custom height influence curve
        public bool UseNoise = false; // Whether to apply noise to influence
        public int NoiseSeed = 0; // Seed for noise generation

        // NEW: Biome parameters
        public float Temperature = 0.5f; // 0 = cold, 1 = hot
        public float Humidity = 0.5f; // 0 = dry, 1 = wet
        public Dictionary<string, float> BiomeParameters = new Dictionary<string, float>();

        // NEW: Noise generator for consistent patterns
        private System.Random _random;
        private float[,] _noiseCache;
        private int _noiseCacheSize = 32;
        private bool _noiseCacheInitialized = false;

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

            // Apply noise if enabled
            if (UseNoise && NoiseAmplitude > 0)
            {
                InitializeNoiseCache();

                // Convert world position to noise space
                Vector3 noisePos = worldPosition * NoiseScale;

                // Sample noise
                float noise = SampleNoise(noisePos) * 2.0f - 1.0f; // -1 to 1 range

                // Apply noise to influence
                influence += noise * NoiseAmplitude;

                // Clamp to 0-1 range
                influence = Mathf.Clamp01(influence);
            }

            // Apply strength multiplier
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
                    // For biome regions, apply biome-specific adjustments
                    return ApplyBiomeAdjustment(state, StateBiases[state] * influence, worldPosition);

                case ConstraintType.HeightMap:
                    // Height-based constraint (e.g., mountains, valleys)
                    float normalizedHeight = GetNormalizedHeight(worldPosition);
                    return AdjustBiasByHeight(state, StateBiases[state], normalizedHeight) * influence;

                case ConstraintType.RiverPath:
                    // Calculate distance to nearest point on path
                    float distanceToPath = GetDistanceToPath(worldPosition);
                    if (distanceToPath > PathWidth + BlendRadius)
                        return 0;

                    float pathInfluence = CalculatePathInfluence(distanceToPath);
                    return StateBiases[state] * pathInfluence * influence;

                case ConstraintType.Structure:
                    // Calculate structure influence
                    float structureInfluence = CalculateStructureInfluence(worldPosition);
                    return StateBiases[state] * structureInfluence * influence;

                default:
                    return StateBiases[state] * influence;
            }
        }

        /// <summary>
        /// Get normalized height value at world position
        /// </summary>
        private float GetNormalizedHeight(Vector3 worldPosition)
        {
            // Calculate normalized height (0-1 range)
            float rawHeight = (worldPosition.y - MinHeight) / (MaxHeight - MinHeight);
            float normalizedHeight = Mathf.Clamp01(rawHeight);

            // Apply height curve if available
            if (HeightCurve != null && HeightCurve.length > 0)
            {
                normalizedHeight = HeightCurve.Evaluate(normalizedHeight);
            }

            return normalizedHeight;
        }

        /// <summary>
        /// Apply biome-specific adjustments to state bias
        /// </summary>
        private float ApplyBiomeAdjustment(int state, float baseBias, Vector3 worldPosition)
        {
            // Default to base bias
            float adjustedBias = baseBias;

            // Apply temperature and humidity influence if state is affected by these parameters

            // Example: Higher temperature increases grass and reduces snow
            if (state == 2) // Grass
            {
                adjustedBias *= Mathf.Lerp(0.5f, 1.5f, Temperature);
            }
            else if (state == 7) // Snow (assuming state 7 is snow)
            {
                adjustedBias *= Mathf.Lerp(1.5f, 0.2f, Temperature);
            }

            // Example: Higher humidity increases water and vegetation
            if (state == 3) // Water
            {
                adjustedBias *= Mathf.Lerp(0.5f, 1.2f, Humidity);
            }
            else if (state == 6) // Tree
            {
                adjustedBias *= Mathf.Lerp(0.3f, 1.5f, Humidity);
            }

            // Apply custom biome parameters if any
            if (BiomeParameters.Count > 0)
            {
                // Example: "erosion" parameter might affect rock and sand
                if (BiomeParameters.TryGetValue("erosion", out float erosion))
                {
                    if (state == 4) // Rock
                    {
                        adjustedBias *= Mathf.Lerp(1.2f, 0.5f, erosion);
                    }
                    else if (state == 5) // Sand
                    {
                        adjustedBias *= Mathf.Lerp(0.5f, 1.5f, erosion);
                    }
                }
            }

            return adjustedBias;
        }

        /// <summary>
        /// Adjusts bias based on height for height-based constraints
        /// </summary>
        private float AdjustBiasByHeight(int state, float baseBias, float normalizedHeight)
        {
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
        /// Calculates the influence of the path at a given distance
        /// </summary>
        private float CalculatePathInfluence(float distance)
        {
            // If within path width, full influence
            if (distance <= PathWidth * 0.5f)
                return 1.0f;

            // Calculate falloff in blend area
            float blendDistance = distance - PathWidth * 0.5f;

            // Use blend curve if available
            if (BlendCurve != null && BlendCurve.length > 0)
            {
                return BlendCurve.Evaluate(1.0f - (blendDistance / BlendRadius));
            }

            // Linear falloff
            return 1.0f - (blendDistance / BlendRadius);
        }

        /// <summary>
        /// Calculate structure influence at world position
        /// </summary>
        private float CalculateStructureInfluence(Vector3 worldPosition)
        {
            // Transform world position to structure space
            Vector3 localPos = worldPosition - WorldCenter;

            // Apply rotation if needed
            if (StructureRotation != 0)
            {
                Quaternion rotation = Quaternion.Euler(0, StructureRotation, 0);
                localPos = Quaternion.Inverse(rotation) * localPos;
            }

            // Apply scale
            localPos.x /= StructureScale.x;
            localPos.y /= StructureScale.y;
            localPos.z /= StructureScale.z;

            // Check if within structure bounds (normalized to -0.5 to 0.5)
            Vector3 halfSize = WorldSize * 0.5f;
            halfSize.x /= StructureScale.x;
            halfSize.y /= StructureScale.y;
            halfSize.z /= StructureScale.z;

            if (Mathf.Abs(localPos.x) <= halfSize.x &&
                Mathf.Abs(localPos.y) <= halfSize.y &&
                Mathf.Abs(localPos.z) <= halfSize.z)
            {
                return 1.0f;
            }

            // Calculate distance to structure bounds
            float dx = Mathf.Max(0, Mathf.Abs(localPos.x) - halfSize.x);
            float dy = Mathf.Max(0, Mathf.Abs(localPos.y) - halfSize.y);
            float dz = Mathf.Max(0, Mathf.Abs(localPos.z) - halfSize.z);

            float distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            // Apply blend radius (adjusted for scale)
            float avgScale = (StructureScale.x + StructureScale.y + StructureScale.z) / 3.0f;
            float adjustedBlendRadius = BlendRadius / avgScale;

            if (distance > adjustedBlendRadius)
                return 0f;

            // Use blend curve if available
            if (BlendCurve != null && BlendCurve.length > 0)
            {
                return BlendCurve.Evaluate(1.0f - (distance / adjustedBlendRadius));
            }

            // Linear falloff
            return 1.0f - (distance / adjustedBlendRadius);
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

        /// <summary>
        /// Initialize the noise cache for consistent patterns
        /// </summary>
        private void InitializeNoiseCache()
        {
            if (_noiseCacheInitialized)
                return;

            _random = new System.Random(NoiseSeed);
            _noiseCache = new float[_noiseCacheSize, _noiseCacheSize];

            // Generate perlin-like noise
            for (int y = 0; y < _noiseCacheSize; y++)
            {
                for (int x = 0; x < _noiseCacheSize; x++)
                {
                    _noiseCache[x, y] = (float)_random.NextDouble();
                }
            }

            // Smooth the noise
            float[,] smoothed = new float[_noiseCacheSize, _noiseCacheSize];
            for (int y = 0; y < _noiseCacheSize; y++)
            {
                for (int x = 0; x < _noiseCacheSize; x++)
                {
                    // Average with neighbors
                    float sum = 0;
                    int count = 0;

                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int nx = (x + ox + _noiseCacheSize) % _noiseCacheSize;
                            int ny = (y + oy + _noiseCacheSize) % _noiseCacheSize;

                            sum += _noiseCache[nx, ny];
                            count++;
                        }
                    }

                    smoothed[x, y] = sum / count;
                }
            }

            // Copy back
            _noiseCache = smoothed;
            _noiseCacheInitialized = true;
        }

        /// <summary>
        /// Sample the noise at a position
        /// </summary>
        private float SampleNoise(Vector3 pos)
        {
            // Convert to 0-1 range and wrap around
            float x = (pos.x % 1.0f + 1.0f) % 1.0f;
            float y = (pos.z % 1.0f + 1.0f) % 1.0f; // Use Z for 2D noise

            // Convert to array indices
            int ix = Mathf.FloorToInt(x * (_noiseCacheSize - 1));
            int iy = Mathf.FloorToInt(y * (_noiseCacheSize - 1));

            // Get next indices for interpolation
            int ix1 = (ix + 1) % _noiseCacheSize;
            int iy1 = (iy + 1) % _noiseCacheSize;

            // Calculate fractional part for interpolation
            float fx = x * (_noiseCacheSize - 1) - ix;
            float fy = y * (_noiseCacheSize - 1) - iy;

            // Bilinear interpolation
            float v00 = _noiseCache[ix, iy];
            float v10 = _noiseCache[ix1, iy];
            float v01 = _noiseCache[ix, iy1];
            float v11 = _noiseCache[ix1, iy1];

            // Interpolate along x
            float v0 = Mathf.Lerp(v00, v10, fx);
            float v1 = Mathf.Lerp(v01, v11, fx);

            // Interpolate along y
            return Mathf.Lerp(v0, v1, fy);
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
                PathWidth = this.PathWidth,
                StructureOrientation = this.StructureOrientation,
                StructureScale = this.StructureScale,
                StructureRotation = this.StructureRotation,
                UseNoise = this.UseNoise,
                NoiseSeed = this.NoiseSeed,
                Temperature = this.Temperature,
                Humidity = this.Humidity
            };

            // Copy control points
            foreach (var point in this.ControlPoints)
            {
                clone.ControlPoints.Add(point);
            }

            // Copy state biases
            foreach (var pair in this.StateBiases)
            {
                clone.StateBiases[pair.Key] = pair.Value;
            }

            // Copy biome parameters
            foreach (var pair in this.BiomeParameters)
            {
                clone.BiomeParameters[pair.Key] = pair.Value;
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
                Temperature = temperature,
                Humidity = humidity
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

        /// <summary>
        /// Create river path constraint
        /// </summary>
        public static GlobalConstraint CreateRiverPath(
            List<Vector3> controlPoints, float pathWidth, Dictionary<int, float> biases,
            string name = "River", float strength = 0.8f, float blendRadius = 10f)
        {
            GlobalConstraint constraint = new GlobalConstraint
            {
                Name = name,
                Type = ConstraintType.RiverPath,
                Strength = strength,
                BlendRadius = blendRadius,
                PathWidth = pathWidth
            };

            // Add control points
            foreach (var point in controlPoints)
            {
                constraint.ControlPoints.Add(point);
            }

            // Add biases
            foreach (var pair in biases)
            {
                constraint.StateBiases[pair.Key] = pair.Value;
            }

            return constraint;
        }

        /// <summary>
        /// Create structure constraint
        /// </summary>
        public static GlobalConstraint CreateStructure(
            Vector3 center, Vector3 size, Vector3 scale, int rotation,
            Dictionary<int, float> biases, string name = "Structure",
            float strength = 0.8f, float blendRadius = 10f)
        {
            GlobalConstraint constraint = new GlobalConstraint
            {
                Name = name,
                Type = ConstraintType.Structure,
                WorldCenter = center,
                WorldSize = size,
                BlendRadius = blendRadius,
                Strength = strength,
                StructureScale = scale,
                StructureRotation = rotation
            };

            // Add biases
            foreach (var pair in biases)
            {
                constraint.StateBiases[pair.Key] = pair.Value;
            }

            return constraint;
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