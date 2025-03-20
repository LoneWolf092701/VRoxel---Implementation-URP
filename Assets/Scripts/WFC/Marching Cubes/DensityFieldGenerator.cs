// Assets/Scripts/WFC/MarchingCubes/DensityFieldGenerator.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;

namespace WFC.MarchingCubes
{
    public class DensityFieldGenerator
    {
        // Configuration
        private float surfaceLevel = 0.5f;
        private float smoothingFactor = 1.0f;

        // Default density range for cells without states
        private float defaultEmptyDensity = 0.1f;  // For empty/air (below surface)
        private float defaultSolidDensity = 0.9f;  // For solid terrain (above surface)

        // State density values (how "solid" each state is)
        private Dictionary<int, float> stateDensityValues = new Dictionary<int, float>();

        // Cache to prevent infinite recursion
        private Dictionary<Vector3Int, float[,,]> densityFieldCache = new Dictionary<Vector3Int, float[,,]>();
        private HashSet<Vector3Int> processingChunks = new HashSet<Vector3Int>();

        public DensityFieldGenerator()
        {
            // Set more extreme density values to ensure there's a clear surface
            // Values below 0.5 are "air", values above 0.5 are "solid"
            stateDensityValues.Add(0, 0.1f);   // Empty (definitely air)
            stateDensityValues.Add(1, 0.8f);   // Ground (definitely solid)
            stateDensityValues.Add(2, 0.73f);   // Grass (solid)
            stateDensityValues.Add(3, 0.35f);   // Water (slightly solid for water surface)
            stateDensityValues.Add(4, 0.92f);  // Rock (very solid)
            stateDensityValues.Add(5, 0.68f);   // Sand (moderately solid)
            stateDensityValues.Add(6, 0.78f);  // Tree (solid)
            stateDensityValues.Add(7, 0.8f);   // Forest (solid)
        }

        public float[,,] GenerateDensityField(Chunk chunk)
        {
            // Exit condition for recursion - if we're already processing this chunk
            if (processingChunks.Contains(chunk.Position))
            {
                // Return a temporary density field with default values
                float[,,] tempField = new float[chunk.Size + 1, chunk.Size + 1, chunk.Size + 1];
                for (int x = 0; x <= chunk.Size; x++)
                {
                    for (int y = 0; y <= chunk.Size; y++)
                    {
                        for (int z = 0; z <= chunk.Size; z++)
                        {
                            tempField[x, y, z] = 0.5f; // Default value at surface threshold
                        }
                    }
                }
                return tempField;
            }

            // Check if we've already computed this field
            if (densityFieldCache.TryGetValue(chunk.Position, out float[,,] cachedField))
            {
                return cachedField;
            }

            // Mark this chunk as currently being processed
            processingChunks.Add(chunk.Position);

            // Create a new density field
            int size = chunk.Size;
            float[,,] densityField = new float[size + 1, size + 1, size + 1];

            // Debug flag to ensure we have a surface
            bool hasSolidCells = false;
            bool hasEmptyCells = false;

            // For each grid point in the density field
            for (int x = 0; x <= size; x++)
            {
                for (int y = 0; y <= size; y++)
                {
                    for (int z = 0; z <= size; z++)
                    {
                        densityField[x, y, z] = CalculateDensity(chunk, x, y, z);

                        // Keep track of solid/empty cells for debugging
                        if (densityField[x, y, z] > surfaceLevel)
                            hasSolidCells = true;
                        else
                            hasEmptyCells = true;
                    }
                }
            }

            // Debug - if we don't have both solid and empty cells, we won't get a surface
            if (!hasSolidCells || !hasEmptyCells)
            {
                Debug.LogWarning($"Chunk {chunk.Position} lacks a proper surface: hasSolid={hasSolidCells}, hasEmpty={hasEmptyCells}");

                // Force creation of a simple surface if there would be none otherwise
                if (!hasSolidCells)
                {
                    // Add some solid cells at the bottom half
                    for (int x = 0; x <= size; x++)
                    {
                        for (int z = 0; z <= size; z++)
                        {
                            for (int y = 0; y <= size / 2; y++)
                            {
                                densityField[x, y, z] = defaultSolidDensity;
                            }
                        }
                    }
                    Debug.Log($"Forcing solid cells in bottom half of chunk {chunk.Position}");
                }
                else if (!hasEmptyCells)
                {
                    // Add some empty cells at the top half
                    for (int x = 0; x <= size; x++)
                    {
                        for (int z = 0; z <= size; z++)
                        {
                            for (int y = size / 2; y <= size; y++)
                            {
                                densityField[x, y, z] = defaultEmptyDensity;
                            }
                        }
                    }
                    Debug.Log($"Forcing empty cells in top half of chunk {chunk.Position}");
                }
            }

            // Handle boundaries to ensure seamless meshes - but only AFTER we've
            // calculated the main density field to avoid infinite recursion
            SmoothBoundaries(densityField, chunk);

            // Store in cache for future requests
            densityFieldCache[chunk.Position] = densityField;

            // Remove from processing set
            processingChunks.Remove(chunk.Position);

            return densityField;
        }

        private float CalculateDensity(Chunk chunk, int x, int y, int z)
        {
            // Grid points are at corners of cells, so we need to sample from surrounding cells
            float density = 0.0f;
            int sampleCount = 0;

            // Sample from all adjacent cells (up to 8 for interior points)
            for (int dx = -1; dx < 1; dx++)
            {
                for (int dy = -1; dy < 1; dy++)
                {
                    for (int dz = -1; dz < 1; dz++)
                    {
                        int sampleX = x + dx;
                        int sampleY = y + dy;
                        int sampleZ = z + dz;

                        // Skip if outside chunk
                        if (sampleX < 0 || sampleX >= chunk.Size ||
                            sampleY < 0 || sampleY >= chunk.Size ||
                            sampleZ < 0 || sampleZ >= chunk.Size)
                            continue;

                        // Get cell and its state
                        Cell cell = chunk.GetCell(sampleX, sampleY, sampleZ);

                        if (cell != null && cell.CollapsedState.HasValue)
                        {
                            int state = cell.CollapsedState.Value;

                            // Get density value for this state
                            if (stateDensityValues.TryGetValue(state, out float baseDensity))
                            {
                                // Add variation based on position
                                float variation = Mathf.PerlinNoise(
                                    (chunk.Position.x * chunk.Size + sampleX) * 0.1f,
                                    (chunk.Position.z * chunk.Size + sampleZ) * 0.1f) * 0.15f;

                                density += baseDensity + variation;
                                sampleCount++;
                            }
                            //else
                            //{
                            //    // Default to solid for unknown states
                            //    density += defaultSolidDensity;
                            //    sampleCount++;
                            //}
                        }
                        else if (cell != null)
                        {
                            // For uncollapsed cells, use average of possible states
                            float avgDensity = 0.0f;
                            foreach (int state in cell.PossibleStates)
                            {
                                if (stateDensityValues.TryGetValue(state, out float stateDensity))
                                {
                                    avgDensity += stateDensity;
                                }
                                else
                                {
                                    // Default to solid for unknown states
                                    avgDensity += defaultSolidDensity;
                                }
                            }

                            if (cell.PossibleStates.Count > 0)
                            {
                                avgDensity /= cell.PossibleStates.Count;
                                density += avgDensity;
                                sampleCount++;
                            }
                            else
                            {
                                // No possible states? Default to empty
                                density += defaultEmptyDensity;
                                sampleCount++;
                            }
                        }
                        else
                        {
                            // No cell? Default to empty space
                            density += defaultEmptyDensity;
                            sampleCount++;
                        }
                    }
                }
            }

            // Calculate average density from samples
            return sampleCount > 0 ? density / sampleCount : defaultEmptyDensity;
        }

        // Modified to avoid infinite recursion
        private void SmoothBoundaries(float[,,] densityField, Chunk chunk)
        {
            int size = chunk.Size;

            // For each direction, smooth boundary if there's a neighbor
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                if (!chunk.Neighbors.ContainsKey(dir))
                    continue;

                Chunk neighbor = chunk.Neighbors[dir];

                // Skip if we're already processing this neighbor (prevents infinite recursion)
                if (processingChunks.Contains(neighbor.Position))
                    continue;

                // Get density field for neighbor - this can recursively call back to this chunk
                // but we've added protection with the processingChunks set
                float[,,] neighborDensity;
                if (densityFieldCache.TryGetValue(neighbor.Position, out float[,,] cachedNeighborField))
                {
                    neighborDensity = cachedNeighborField;
                }
                else
                {
                    // Only generate neighbor fields for adjacent chunks that aren't already being processed
                    neighborDensity = GenerateDensityField(neighbor);
                }

                // Smooth boundaries based on direction
                switch (dir)
                {
                    case Direction.Left: // -X
                        for (int y = 0; y <= size; y++)
                        {
                            for (int z = 0; z <= size; z++)
                            {
                                // Average with neighbor's density
                                densityField[0, y, z] = (densityField[0, y, z] + neighborDensity[size, y, z]) / 2.0f;
                            }
                        }
                        break;

                    case Direction.Right: // +X
                        for (int y = 0; y <= size; y++)
                        {
                            for (int z = 0; z <= size; z++)
                            {
                                densityField[size, y, z] = (densityField[size, y, z] + neighborDensity[0, y, z]) / 2.0f;
                            }
                        }
                        break;

                    case Direction.Down: // -Y
                        for (int x = 0; x <= size; x++)
                        {
                            for (int z = 0; z <= size; z++)
                            {
                                densityField[x, 0, z] = (densityField[x, 0, z] + neighborDensity[x, size, z]) / 2.0f;
                            }
                        }
                        break;

                    case Direction.Up: // +Y
                        for (int x = 0; x <= size; x++)
                        {
                            for (int z = 0; z <= size; z++)
                            {
                                densityField[x, size, z] = (densityField[x, size, z] + neighborDensity[x, 0, z]) / 2.0f;
                            }
                        }
                        break;

                    case Direction.Back: // -Z
                        for (int x = 0; x <= size; x++)
                        {
                            for (int y = 0; y <= size; y++)
                            {
                                densityField[x, y, 0] = (densityField[x, y, 0] + neighborDensity[x, y, size]) / 2.0f;
                            }
                        }
                        break;

                    case Direction.Forward: // +Z
                        for (int x = 0; x <= size; x++)
                        {
                            for (int y = 0; y <= size; y++)
                            {
                                densityField[x, y, size] = (densityField[x, y, size] + neighborDensity[x, y, 0]) / 2.0f;
                            }
                        }
                        break;
                }
            }
        }
        private float[,,] GetNeighborDensityField(Chunk neighbor)
        {
            // Get or generate neighbor density field safely
            if (densityFieldCache.TryGetValue(neighbor.Position, out float[,,] cachedField))
                return cachedField;

            if (processingChunks.Contains(neighbor.Position))
            {
                // Create a temporary field that continues the gradient from this chunk
                // rather than using a default value
                return CreateContinuationField(neighbor);
            }

            return GenerateDensityField(neighbor);
        }

        private float[,,] CreateContinuationField(Chunk chunk)
        {
            // Create a field that continues trends from adjacent chunks
            // This is a simplified version - you would implement a more 
            // sophisticated continuation based on your terrain needs
            int size = chunk.Size;
            float[,,] field = new float[size + 1, size + 1, size + 1];

            // Initialize with neutral values
            for (int x = 0; x <= size; x++)
            {
                for (int y = 0; y <= size; y++)
                {
                    for (int z = 0; z <= size; z++)
                    {
                        field[x, y, z] = 0.5f; // Neutral density at surface threshold
                    }
                }
            }

            return field;
        }

        // Call this before generating a new batch of density fields to clear the cache
        public void ClearCache()
        {
            densityFieldCache.Clear();
            processingChunks.Clear();
        }
        public void SetSurfaceLevel(float level)
        {
            surfaceLevel = level;
        }
    }
}