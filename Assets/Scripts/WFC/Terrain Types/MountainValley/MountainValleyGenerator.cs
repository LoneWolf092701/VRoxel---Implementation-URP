using UnityEngine;
using System.Collections.Generic;
using WFC.Core;
using WFC.Terrain;

using static WFC.Terrain.MountainValleyTerrainDefinition;

namespace WFC.Terrain
{
    public class MountainValleyGenerator : ITerrainGenerator
    {
        private MountainValleyTerrainDefinition terrainDefinition;

        public MountainValleyGenerator(MountainValleyTerrainDefinition definition)
        {
            this.terrainDefinition = definition;
        }

        public void GenerateTerrain(Chunk chunk, System.Random random)
        {
            // Multi-octave noise parameters
            float[] noiseScales = { 0.005f, 0.02f, 0.1f };
            float[] noiseWeights = { 0.7f, 0.25f, 0.05f };

            // Generate height map
            int[,] terrainHeight = GenerateTerrainHeightMap(chunk, random, noiseScales, noiseWeights);

            // Generate river path
            HashSet<Vector2Int> riverCells = GenerateRiverPath(chunk, terrainHeight, random);

            // Generate slopes and moisture
            float[,] slopeMap = CalculateSlopes(terrainHeight);
            float[,] moistureMap = GenerateMoistureMap(chunk, random, riverCells);

            // Apply terrain to cells
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    int height = terrainHeight[x, z];
                    float slope = slopeMap[x, z];
                    float moisture = moistureMap[x, z];

                    // Apply different states based on environment
                    bool isRiver = riverCells.Contains(new Vector2Int(x, z));

                    ApplyColumnStates(chunk, x, z, height, slope, moisture, isRiver);
                }
            }
        }

        private int[,] GenerateTerrainHeightMap(Chunk chunk, System.Random random, float[] scales, float[] weights)
        {
            int[,] heightMap = new int[chunk.Size, chunk.Size];

            // Base height parameters
            int baseGroundLevel = chunk.Size / 3;
            int heightRange = chunk.Size * 2 / 3;

            // Get mountain height factor from definition
            float mountainFactor = terrainDefinition.mountainHeight * 1.5f;

            // Global world position for consistent generation
            float worldOffsetX = chunk.Position.x * chunk.Size;
            float worldOffsetZ = chunk.Position.z * chunk.Size;

            // Valley shape factor
            float valleyWidth = terrainDefinition.valleyWidth * chunk.Size;

            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    float worldX = worldOffsetX + x;
                    float worldZ = worldOffsetZ + z;

                    // Mountain height (multi-octave noise)
                    float combinedNoise = 0f;
                    for (int i = 0; i < scales.Length; i++)
                    {
                        float noise = Mathf.PerlinNoise(
                            worldX * scales[i] + (i * 100),
                            worldZ * scales[i] + (i * 100));
                        combinedNoise += noise * weights[i];
                    }

                    // Apply power curve for more dramatic mountains
                    combinedNoise = Mathf.Pow(combinedNoise, 1.8f);

                    // Create valley by depressing height in the center of the world
                    float worldSizeEstimate = 32 * 16; // Rough world size estimate
                    float distanceFromCenter = Mathf.Abs(worldX - worldSizeEstimate / 2);
                    float valleyFactor = 1.0f;

                    if (distanceFromCenter < valleyWidth)
                    {
                        // Valley depth increases toward center
                        valleyFactor = distanceFromCenter / valleyWidth;
                        valleyFactor = Mathf.SmoothStep(0.2f, 1.0f, valleyFactor);
                    }


                    // Apply valley and mountain factors
                    float heightFactor = combinedNoise * valleyFactor * mountainFactor;

                    if (valleyFactor > 0.7f)
                    {
                        heightFactor = combinedNoise * Mathf.Pow(valleyFactor, 0.7f) * mountainFactor;
                    }
                    else
                    {
                        heightFactor = combinedNoise * valleyFactor * mountainFactor;
                    }

                    // Convert to actual height
                    heightMap[x, z] = baseGroundLevel + Mathf.FloorToInt(heightFactor * heightRange);
                }
            }

            return heightMap;
        }

        private HashSet<Vector2Int> GenerateRiverPath(Chunk chunk, int[,] heightMap, System.Random random)
        {
            HashSet<Vector2Int> riverCells = new HashSet<Vector2Int>();

            // Only generate river in chunks near the center of the valley
            float worldOffsetX = chunk.Position.x * chunk.Size;
            float worldSizeEstimate = 32 * 16; // Rough world size estimate
            float distanceFromCenter = Mathf.Abs(worldOffsetX - worldSizeEstimate / 2);

            if (distanceFromCenter > terrainDefinition.valleyWidth * worldSizeEstimate / 2)
            {
                return riverCells; // No river in this chunk
            }

            // River width parameter from definition
            float riverWidthCells = terrainDefinition.riverWidth * chunk.Size;
            int riverWidth = Mathf.Max(3, Mathf.FloorToInt(riverWidthCells));

            // Create river along Z axis in center of chunk
            int centerX = chunk.Size / 2;

            // Apply some meandering
            float[] riverOffsets = new float[chunk.Size];
            for (int z = 0; z < chunk.Size; z++)
            {
                float worldZ = chunk.Position.z * chunk.Size + z;
                float meander = Mathf.PerlinNoise(worldZ * 0.05f, 0) * 2 - 1;
                riverOffsets[z] = meander * (chunk.Size / 10); // Limit meandering
            }

            // Mark river cells
            for (int z = 0; z < chunk.Size; z++)
            {
                int riverCenterX = centerX + Mathf.FloorToInt(riverOffsets[z]);

                for (int rx = -riverWidth / 2; rx <= riverWidth / 2; rx++)
                {
                    int x = riverCenterX + rx;
                    if (x >= 0 && x < chunk.Size)
                    {
                        riverCells.Add(new Vector2Int(x, z));

                        // Lower the terrain for the river
                        if (heightMap[x, z] > chunk.Size / 4)
                        {
                            heightMap[x, z] = chunk.Size / 5; // Lower river bed (was /4)
                        }
                    }
                }
            }

            return riverCells;
        }

        private float[,] CalculateSlopes(int[,] heightMap)
        {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            float[,] slopes = new float[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    // Sample neighbors (with bounds checking)
                    int left = (x > 0) ? heightMap[x - 1, z] : heightMap[x, z];
                    int right = (x < width - 1) ? heightMap[x + 1, z] : heightMap[x, z];
                    int top = (z > 0) ? heightMap[x, z - 1] : heightMap[x, z];
                    int bottom = (z < height - 1) ? heightMap[x, z + 1] : heightMap[x, z];

                    // Calculate max gradient
                    int currentHeight = heightMap[x, z];
                    int maxDiff = Mathf.Max(
                        Mathf.Abs(currentHeight - left),
                        Mathf.Abs(currentHeight - right),
                        Mathf.Abs(currentHeight - top),
                        Mathf.Abs(currentHeight - bottom)
                    );

                    // Normalize to 0-1
                    slopes[x, z] = Mathf.Clamp01(maxDiff / 5.0f);
                }
            }

            return slopes;
        }

        private float[,] GenerateMoistureMap(Chunk chunk, System.Random random, HashSet<Vector2Int> riverCells)
        {
            int size = chunk.Size;
            float[,] moisture = new float[size, size];

            // Base moisture from noise
            for (int x = 0; x < size; x++)
            {
                for (int z = 0; z < size; z++)
                {
                    float worldX = chunk.Position.x * size + x;
                    float worldZ = chunk.Position.z * size + z;

                    // Base moisture from noise
                    moisture[x, z] = Mathf.PerlinNoise(worldX * 0.02f, worldZ * 0.02f);

                    // Increase moisture near rivers
                    if (riverCells.Count > 0)
                    {
                        float closestDistance = float.MaxValue;
                        foreach (var riverPos in riverCells)
                        {
                            float distance = Vector2.Distance(
                                new Vector2(x, z),
                                new Vector2(riverPos.x, riverPos.y));
                            closestDistance = Mathf.Min(closestDistance, distance);
                        }

                        // Apply moisture gradient near rivers
                        float riverInfluence = Mathf.Max(0, 1 - (closestDistance / (size * 0.2f)));
                        moisture[x, z] = Mathf.Lerp(moisture[x, z], 1.0f, riverInfluence);
                    }
                }
            }

            return moisture;
        }

        private void ApplyColumnStates(Chunk chunk, int x, int z, int height, float slope, float moisture, bool isRiver)
        {
            // Height bounds check
            height = Mathf.Clamp(height, 1, chunk.Size - 1);

            // Determine surface type based on height, slope and moisture
            TerrainStateId surfaceState;

            // First check if this is a river cell
            if (isRiver)
            {
                // Apply water and riverbed
                for (int y = 0; y < chunk.Size; y++)
                {
                    Cell cell = chunk.GetCell(x, y, z);
                    if (cell == null) continue;

                    if (y < height - 1)
                    {
                        // River bottom is sand
                        if (y == height - 2)
                            cell.Collapse((int)TerrainStateId.Sand);
                        else
                            cell.Collapse((int)TerrainStateId.Ground);
                    }
                    else if (y == height - 1)
                    {
                        cell.Collapse((int)TerrainStateId.Water);
                    }
                    else
                    {
                        cell.Collapse((int)TerrainStateId.Air);
                    }
                }
                return;
            }

            // Not river - determine surface type
            if (slope > 0.7f)
            {
                surfaceState = TerrainStateId.Cliff;
            }
            else if (height > chunk.Size * 0.8f)
            {
                surfaceState = TerrainStateId.SnowCap;
            }
            else if (height > chunk.Size * 0.6f)
            {
                surfaceState = TerrainStateId.Rock;
            }
            else if (moisture > 0.7f && height < chunk.Size * 0.5f)
            {
                // Random mix of grass and forest based on forestDensity
                float forestRandom = UnityEngine.Random.value;
                if (forestRandom < terrainDefinition.forestDensity)
                    surfaceState = TerrainStateId.Forest;
                else
                    surfaceState = TerrainStateId.Grass;
            }
            else if (moisture > 0.4f)
            {
                surfaceState = TerrainStateId.Grass;
            }
            else if (moisture < 0.3f && height < chunk.Size * 0.4f)
            {
                surfaceState = TerrainStateId.Sand;
            }
            else
            {
                surfaceState = TerrainStateId.Ground;
            }

            // Apply states to entire column
            for (int y = 0; y < chunk.Size; y++)
            {
                Cell cell = chunk.GetCell(x, y, z);
                if (cell == null) continue;

                if (y < height - 2)
                {
                    // Deep underground - always ground
                    cell.Collapse((int)TerrainStateId.Ground);
                }
                else if (y == height - 2)
                {
                    // Layer below surface
                    if (surfaceState == TerrainStateId.Rock || surfaceState == TerrainStateId.Cliff)
                        cell.Collapse((int)TerrainStateId.Rock);
                    else
                        cell.Collapse((int)TerrainStateId.Ground);
                }
                else if (y == height - 1)
                {
                    // Surface layer
                    cell.Collapse((int)surfaceState);
                }
                else
                {
                    // Above surface - always air
                    cell.Collapse((int)TerrainStateId.Air);
                }
            }
        }

        public void ApplyConstraints(HierarchicalConstraintSystem system, Vector3Int worldSize, int chunkSize)
        {
            // Clear existing constraints
            system.ClearConstraints();

            // 1. Mountain range constraint
            GlobalConstraint mountainRange = new GlobalConstraint
            {
                Name = "Mountain Range",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(
                    worldSize.x * chunkSize * 0.5f,
                    worldSize.y * chunkSize * 0.6f,
                    worldSize.z * chunkSize * 0.5f
                ),
                WorldSize = new Vector3(
                    worldSize.x * chunkSize * 0.8f,
                    worldSize.y * chunkSize * terrainDefinition.mountainHeight,
                    worldSize.z * chunkSize * 0.8f
                ),
                BlendRadius = chunkSize * 2f,
                Strength = 0.85f,
                MinHeight = worldSize.y * chunkSize * 0.3f,
                MaxHeight = worldSize.y * chunkSize * 0.9f
            };

            mountainRange.StateBiases[(int)TerrainStateId.Air] = -0.9f;
            mountainRange.StateBiases[(int)TerrainStateId.Rock] = 0.8f;
            mountainRange.StateBiases[(int)TerrainStateId.Cliff] = 0.7f;
            mountainRange.StateBiases[(int)TerrainStateId.SnowCap] = 0.6f;

            // 2. River valley constraint
            GlobalConstraint riverValley = new GlobalConstraint
            {
                Name = "River Valley",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(
                    worldSize.x * chunkSize * 0.5f,
                    worldSize.y * chunkSize * 0.2f,
                    worldSize.z * chunkSize * 0.5f
                ),
                WorldSize = new Vector3(
                    worldSize.x * chunkSize * terrainDefinition.valleyWidth,
                    worldSize.y * chunkSize * 0.3f,
                    worldSize.z * chunkSize * 0.8f
                ),
                BlendRadius = chunkSize * 3f,
                Strength = 0.8f
            };

            riverValley.StateBiases[(int)TerrainStateId.Water] = 0.9f;
            riverValley.StateBiases[(int)TerrainStateId.Sand] = 0.7f;
            riverValley.StateBiases[(int)TerrainStateId.Grass] = 0.6f;

            // 3. Forest distribution constraint
            GlobalConstraint forestDistribution = new GlobalConstraint
            {
                Name = "Forest Distribution",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(
                    worldSize.x * chunkSize * 0.5f,
                    worldSize.y * chunkSize * 0.3f,
                    worldSize.z * chunkSize * 0.3f
                ),
                WorldSize = new Vector3(
                    worldSize.x * chunkSize * 0.7f,
                    worldSize.y * chunkSize * 0.2f,
                    worldSize.z * chunkSize * 0.5f
                ),
                BlendRadius = chunkSize * 2f,
                Strength = 0.7f * terrainDefinition.forestDensity
            };

            forestDistribution.StateBiases[(int)TerrainStateId.Forest] = 0.8f;
            forestDistribution.StateBiases[(int)TerrainStateId.Grass] = 0.4f;

            // Add all constraints
            system.AddGlobalConstraint(mountainRange);
            system.AddGlobalConstraint(riverValley);
            system.AddGlobalConstraint(forestDistribution);
        }
    }
}