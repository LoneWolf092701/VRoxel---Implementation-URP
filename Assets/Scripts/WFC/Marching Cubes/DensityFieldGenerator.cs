using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using System.Linq;
using WFC.Terrain;
using System;

namespace WFC.MarchingCubes
{
    public class DensityFieldGenerator
    {
        // Configuration
        private float surfaceLevel = 0.5f;
        private int chunkSize = 16;

        // Default density range for cells without states
        private float defaultEmptyDensity = 0.8f;  // For empty/air (below surface)         // Changed this after submission
        private float defaultSolidDensity = 0.2f;  // For solid terrain (above surface)     // Changed this after submission

        // Cache to prevent infinite recursion and improve performance
        private Dictionary<Vector3Int, float[,,]> densityFieldCache = new Dictionary<Vector3Int, float[,,]>();
        private HashSet<Vector3Int> processingChunks = new HashSet<Vector3Int>();

        // Cache management
        private int maxCacheSize = 100; // Adjust based on expected world size
        private Queue<Vector3Int> cacheEvictionQueue = new Queue<Vector3Int>();

        private TerrainDefinition terrainDefinition;
        private Dictionary<int, float> stateDensityValues = new Dictionary<int, float>();
        public DensityFieldGenerator(TerrainDefinition terrainDef = null)
        {
            // Initialize with definition if provided, otherwise use defaults
            if (terrainDef != null)
            {
                terrainDefinition = terrainDef;
                stateDensityValues = terrainDef.StateDensities;
            }
            else
            {
                // Default density values as fallback
                stateDensityValues.Add(0, 0.1f);   // Empty (air)
                stateDensityValues.Add(1, 0.8f);   // Ground 
                stateDensityValues.Add(3, 0.6f);   // Water
                stateDensityValues.Add(4, 0.85f);  // Rock
            }

            chunkSize = 32;
        }

        public void SetTerrainDefinition(TerrainDefinition newTerrainDef)
        {
            if (newTerrainDef != null)
            {
                terrainDefinition = newTerrainDef;
                stateDensityValues = newTerrainDef.StateDensities;
            }
        }

        private void ApplyGaussianSmoothing(float[,,] densityField, int size, float sigma = 1.0f)
        {
            // Create a temporary array to hold smoothed values
            float[,,] smoothed = new float[size + 1, size + 1, size + 1];

            // Kernel size (use odd number)
            int kernelSize = Mathf.CeilToInt(sigma * 3) * 2 + 1;
            int kernelRadius = kernelSize / 2;

            // Create Gaussian kernel
            float[,,] kernel = new float[kernelSize, kernelSize, kernelSize];
            float kernelSum = 0.0f;

            // Fill kernel with Gaussian values
            for (int x = 0; x < kernelSize; x++)
            {
                for (int y = 0; y < kernelSize; y++)
                {
                    for (int z = 0; z < kernelSize; z++)
                    {
                        int dx = x - kernelRadius;
                        int dy = y - kernelRadius;
                        int dz = z - kernelRadius;

                        // Gaussian function
                        float value = Mathf.Exp(-(dx * dx + dy * dy + dz * dz) / (2.0f * sigma * sigma));
                        kernel[x, y, z] = value;
                        kernelSum += value;
                    }
                }
            }

            // Normalize kernel
            for (int x = 0; x < kernelSize; x++)
                for (int y = 0; y < kernelSize; y++)
                    for (int z = 0; z < kernelSize; z++)
                        kernel[x, y, z] /= kernelSum;

            // Apply convolution
            for (int x = 0; x <= size; x++)
            {
                for (int y = 0; y <= size; y++)
                {
                    for (int z = 0; z <= size; z++)
                    {
                        float sum = 0.0f;
                        float weightSum = 0.0f;

                        // Apply kernel
                        for (int kx = 0; kx < kernelSize; kx++)
                        {
                            for (int ky = 0; ky < kernelSize; ky++)
                            {
                                for (int kz = 0; kz < kernelSize; kz++)
                                {
                                    int sampleX = x + kx - kernelRadius;
                                    int sampleY = y + ky - kernelRadius;
                                    int sampleZ = z + kz - kernelRadius;

                                    // Check bounds
                                    if (sampleX >= 0 && sampleX <= size &&
                                        sampleY >= 0 && sampleY <= size &&
                                        sampleZ >= 0 && sampleZ <= size)
                                    {
                                        float weight = kernel[kx, ky, kz];
                                        sum += densityField[sampleX, sampleY, sampleZ] * weight;
                                        weightSum += weight;
                                    }
                                }
                            }
                        }

                        // Normalize by actual weight sum
                        if (weightSum > 0.0f)
                        {
                            smoothed[x, y, z] = sum / weightSum;
                        }
                        else
                        {
                            smoothed[x, y, z] = densityField[x, y, z];
                        }
                    }
                }
            }

            // Copy back to original
            for (int x = 0; x <= size; x++)
                for (int y = 0; y <= size; y++)
                    for (int z = 0; z <= size; z++)
                        densityField[x, y, z] = smoothed[x, y, z];
        }

        private void AddMultiFrequencyNoise(float[,,] densityField, Chunk chunk, int size)
        {
            Vector3Int chunkPos = chunk.Position;

            for (int x = 0; x <= size; x++)
            {
                for (int y = 0; y <= size; y++)
                {
                    for (int z = 0; z <= size; z++)
                    {
                        // Global coordinates for consistent noise
                        float worldX = chunkPos.x * size + x;
                        float worldY = chunkPos.y * size + y;
                        float worldZ = chunkPos.z * size + z;

                        // Add multiple octaves of noise with increased amplitude
                        float noise = 0;

                        // First octave - large features
                        float amplitude1 = 0.25f; // Increased from 0.05f
                        float frequency1 = 0.05f;
                        noise += Mathf.PerlinNoise(worldX * frequency1, worldZ * frequency1) * amplitude1;

                        // Second octave - medium features
                        float amplitude2 = amplitude1 * 0.5f;
                        float frequency2 = frequency1 * 2.0f;
                        noise += Mathf.PerlinNoise(worldX * frequency2, worldZ * frequency2) * amplitude2;

                        // Third octave - small details
                        float amplitude3 = amplitude2 * 0.5f;
                        float frequency3 = frequency2 * 2.0f;
                        noise += Mathf.PerlinNoise(worldX * frequency3, worldZ * frequency3) * amplitude3;

                        // Vertical variation for mountain areas - enhanced
                        float verticalNoise = Mathf.PerlinNoise(worldX * 0.03f, worldZ * 0.03f);
                        if (verticalNoise > 0.6f && densityField[x, y, z] > 0.7f)
                        {
                            noise *= 3.0f; // Further enhanced for more dramatic mountains (was 2.0f)
                        }

                        // Apply noise to terrain with greater effect
                        densityField[x, y, z] += noise - 0.05f; // Adjusted offset to maintain balance
                    }
                }
            }
        }

        /// <summary>
        /// Get a cached density field or generate a new one
        /// </summary>
        public float[,,] GetOrGenerateDensityField(Chunk chunk)
        {
            // Check if already cached - if so, update its position in the LRU queue
            if (densityFieldCache.TryGetValue(chunk.Position, out float[,,] cachedField))
            {
                // Update LRU status - move to end of queue (most recently used)
                if (cacheEvictionQueue.Contains(chunk.Position))
                {
                    // Remove from current position and add to end
                    cacheEvictionQueue = new Queue<Vector3Int>(
                        cacheEvictionQueue.Where(pos => !pos.Equals(chunk.Position)));
                    cacheEvictionQueue.Enqueue(chunk.Position);
                }
                return cachedField;
            }

            // Not cached, generate new field
            float[,,] newField = GenerateDensityField(chunk);

            // Add to cache with LRU tracking
            AddToCache(chunk.Position, newField);

            return newField;
        }

        /*
         * GenerateDensityField
         * ----------------------------------------------------------------------------
         * Creates a 3D density field from WFC cell states for mesh generation.
         * 
         * Field generation process:
         * 1. Initializes a 3D grid of density values with dimensions (size+1)³
         * 2. Performs diagnostic analysis of existing density values
         * 3. For each grid point in the density field:
         *    a. Samples surrounding cells to determine local density
         *    b. Uses stateDensityValues mapping to convert cell states to densities
         *    c. Handles uncollapsed cells by averaging possible state densities
         * 4. Applies terrain-specific preprocessing for natural features
         * 5. Smooths boundaries between adjacent chunks for seamless meshes
         * 6. Handles corner points where multiple chunks meet
         * 
         * The result is a continuous scalar field where values above surfaceLevel
         * represent "solid" terrain and values below represent "empty" space.
         * This field is used by the Marching Cubes algorithm to generate the mesh.
         * 
         * Parameters:
         * - chunk: Chunk containing the WFC cells to convert
         * - recursionDepth: Current recursion depth for boundary handling
         * 
         * Returns: 3D array of density values
         */
        public float[,,] GenerateDensityField(Chunk chunk, int recursionDepth = 0)
        {
            chunkSize = chunk.Size;

            // Exit condition for recursion - if already processing this chunk
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

            // Check if system already computed this field
            if (densityFieldCache.TryGetValue(chunk.Position, out float[,,] cachedField))
            {
                return cachedField;
            }

            // Mark this chunk as currently being processed
            processingChunks.Add(chunk.Position);

            // Create a new density field
            int size = chunk.Size;
            float[,,] densityField = new float[size + 1, size + 1, size + 1];

            // Debug flag to ensure have a surface
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
                        if (densityField[x, y, z] < surfaceLevel)           // Changed this after submission
                            hasSolidCells = true;
                        else
                            hasEmptyCells = true;
                    }
                }
            }

            // Debug - if don't have both solid and empty cells, won't get a surface
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

            // Apply pre-processing to improve terrain features
            PreProcessDensityField(densityField, chunk);

            if (!hasSolidCells || !hasEmptyCells)
            {
                Debug.LogWarning($"Chunk {chunk.Position} still lacks a proper surface after processing: hasSolid={hasSolidCells}, hasEmpty={hasEmptyCells}");
            }

            // Handle boundaries to ensure seamless meshes - but only AFTER calculated the main density field
            SmoothBoundaries(densityField, chunk, recursionDepth);

            // Handle corner points where multiple chunks meet
            SmoothCorners(densityField, chunk);

            // Add to cache - using the helper method that manages the LRU queue
            AddToCache(chunk.Position, densityField);

            // Remove from processing set
            processingChunks.Remove(chunk.Position);

            return densityField;
        }

        /// <summary>
        /// Add a density field to the cache with LRU tracking
        /// </summary>
        private void AddToCache(Vector3Int chunkPos, float[,,] densityField)
        {
            // If at capacity, remove the least recently used item
            if (densityFieldCache.Count >= maxCacheSize && cacheEvictionQueue.Count > 0)
            {
                Vector3Int oldestChunk = cacheEvictionQueue.Dequeue();
                densityFieldCache.Remove(oldestChunk);
            }

            // Add new entry to cache
            densityFieldCache[chunkPos] = densityField;
            cacheEvictionQueue.Enqueue(chunkPos);

            // Instead of using dirtyChunks, directly remove adjacent chunks from cache
            for (int i = 0; i < 6; i++)
            {
                Direction dir = (Direction)i;
                Vector3Int adjacentPos = chunkPos + dir.ToVector3Int();

                // If adjacent chunk is in cache but not being processed, remove it so it will be regenerated
                if (densityFieldCache.ContainsKey(adjacentPos) && !processingChunks.Contains(adjacentPos))
                {
                    densityFieldCache.Remove(adjacentPos);

                    // Also remove from eviction queue if present
                    if (cacheEvictionQueue.Contains(adjacentPos))
                    {
                        cacheEvictionQueue = new Queue<Vector3Int>(
                            cacheEvictionQueue.Where(pos => !pos.Equals(adjacentPos)));
                    }
                }
            }
        }

        /// <summary>
        /// Apply additional processing to the density field to create more interesting terrain
        /// </summary>
        private void PreProcessDensityField(float[,,] densityField, Chunk chunk)
        {
            int size = chunk.Size;
            Vector3Int chunkPos = chunk.Position;

            // Smoothing pass for more natural terrain - especially mountains
            float[,,] smoothedField = new float[size + 1, size + 1, size + 1];

            // Copy original field
            for (int x = 0; x <= size; x++)
            {
                for (int y = 0; y <= size; y++)
                {
                    for (int z = 0; z <= size; z++)
                    {
                        smoothedField[x, y, z] = densityField[x, y, z];
                    }
                }
            }

            // Simple smoothing algorithm - smooth density values slightly
            for (int x = 1; x < size; x++)
            {
                for (int y = 1; y < size; y++)
                {
                    for (int z = 1; z < size; z++)
                    {
                        // Get average of neighbors
                        float sum = 0;
                        int count = 0;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    int nx = x + dx;
                                    int ny = y + dy;
                                    int nz = z + dz;

                                    if (nx >= 0 && nx <= size &&
                                        ny >= 0 && ny <= size &&
                                        nz >= 0 && nz <= size)
                                    {
                                        sum += densityField[nx, ny, nz];
                                        count++;
                                    }
                                }
                            }
                        }

                        // Apply smoothing with a strength factor (0.3 = 30% smoothing)
                        float avgValue = sum / count;
                        float smoothingStrength = 0.3f;
                        smoothedField[x, y, z] = Mathf.Lerp(densityField[x, y, z], avgValue, smoothingStrength);
                    }
                }
            }

            // Copy smoothed field back to original
            for (int x = 0; x <= size; x++)
            {
                for (int y = 0; y <= size; y++)
                {
                    for (int z = 0; z <= size; z++)
                    {
                        densityField[x, y, z] = smoothedField[x, y, z];
                    }
                }
            }

            // Apply terrain features like mountains and valleys
            for (int x = 0; x <= size; x++)
            {
                for (int z = 0; z <= size; z++)
                {
                    // Global coordinates for coherent features across chunks
                    float globalX = chunkPos.x * size + x;
                    float globalZ = chunkPos.z * size + z;
                }
            }

            ApplyGaussianSmoothing(densityField, size, 1.2f);

            // Add multi-frequency noise to break up regular patterns
            AddMultiFrequencyNoise(densityField, chunk, size);
        }
        private void SmoothCorners(float[,,] densityField, Chunk chunk)
        {
            int size = chunk.Size;

            // Get all diagonal neighbors (corner neighbors)
            for (int dx = -1; dx <= 1; dx += 2)
            {
                for (int dy = -1; dy <= 1; dy += 2)
                {
                    for (int dz = -1; dz <= 1; dz += 2)
                    {
                        Vector3Int diagonalOffset = new Vector3Int(dx, dy, dz);
                        Vector3Int neighborPos = chunk.Position + diagonalOffset;

                        // Check if this diagonal neighbor exists in the cache
                        if (!densityFieldCache.TryGetValue(neighborPos, out float[,,] cornerNeighborField))
                            continue;

                        // Determine which corner to smooth
                        int cornerX = dx > 0 ? size : 0;
                        int cornerY = dy > 0 ? size : 0;
                        int cornerZ = dz > 0 ? size : 0;

                        // Get the opposite corner in the neighbor
                        int neighborX = dx > 0 ? 0 : size;
                        int neighborY = dy > 0 ? 0 : size;
                        int neighborZ = dz > 0 ? 0 : size;

                        // CRITICAL FIX: Calculate average including all 8 chunks that meet at this corner
                        float cornerSum = densityField[cornerX, cornerY, cornerZ];
                        int cornerCount = 1;

                        cornerSum += cornerNeighborField[neighborX, neighborY, neighborZ];
                        cornerCount++;

                        // Try to find the other 6 chunks that share this corner
                        // (This additional code is crucial for corner consistency)
                        for (int edx = -1; edx <= 1; edx += 2)
                        {
                            for (int edy = -1; edy <= 1; edy += 2)
                            {
                                for (int edz = -1; edz <= 1; edz += 2)
                                {
                                    // Skip current chunk and direct diagonal
                                    if ((edx == dx && edy == dy && edz == dz) || (edx == 0 && edy == 0 && edz == 0))
                                        continue;

                                    Vector3Int edgeOffset = new Vector3Int(edx, edy, edz);
                                    Vector3Int edgeNeighborPos = chunk.Position + edgeOffset;

                                    if (densityFieldCache.TryGetValue(edgeNeighborPos, out float[,,] edgeField))
                                    {
                                        int edgeX = edx > 0 ? 0 : size;
                                        int edgeY = edy > 0 ? 0 : size;
                                        int edgeZ = edz > 0 ? 0 : size;

                                        cornerSum += edgeField[edgeX, edgeY, edgeZ];
                                        cornerCount++;
                                    }
                                }
                            }
                        }

                        float cornerAverage = cornerSum / cornerCount;

                        cornerAverage = Mathf.Round(cornerAverage * 10000000f) / 10000000f; // Changed after submission
 
                        densityField[cornerX, cornerY, cornerZ] = cornerAverage;
                        cornerNeighborField[neighborX, neighborY, neighborZ] = cornerAverage;

                        int blendRadius = 16; // Increased from 3

                        // Apply smoothing with stronger non-linear falloff
                        for (int x = 0; x <= blendRadius; x++)
                        {
                            for (int y = 0; y <= blendRadius; y++)
                            {
                                for (int z = 0; z <= blendRadius; z++)
                                {
                                    // Skip if out of bounds
                                    if (cornerX - dx * x < 0 || cornerX - dx * x > size ||
                                        cornerY - dy * y < 0 || cornerY - dy * y > size ||
                                        cornerZ - dz * z < 0 || cornerZ - dz * z > size)
                                        continue;

                                    // Calculate blend factor based on distance from corner with non-linear falloff
                                    float distance = Mathf.Sqrt(x * x + y * y + z * z);
                                    if (distance > blendRadius) continue;

                                    // CRITICAL FIX: Use non-linear falloff
                                    float blendFactor = 1.0f - Mathf.Pow(distance / blendRadius, 1.5f);

                                    // Apply blended value
                                    int blendX = cornerX - dx * x;
                                    int blendY = cornerY - dy * y;
                                    int blendZ = cornerZ - dz * z;

                                    densityField[blendX, blendY, blendZ] = Mathf.Lerp(
                                        densityField[blendX, blendY, blendZ],
                                        cornerAverage,
                                        blendFactor
                                    );
                                }
                            }
                        }
                    }
                }
            }
        }

        /*
         * CalculateDensity
         * ----------------------------------------------------------------------------
         * Calculates the density value for a grid point based on surrounding cells.
         * 
         * Density calculation approach:
         * 1. Grid points are at corners of cells, so each point is affected by
         *    up to 8 surrounding cells (fewer at edges)
         * 2. For each adjacent cell:
         *    a. Gets the cell's state (if collapsed) or possible states
         *    b. Converts state to density using stateDensityValues mapping
         *    c. For uncollapsed cells, averages densities of all possible states
         *    d. Adds variation based on noise for natural-looking terrain
         * 3. Averages all sampled densities for the final value
         * 
         * This sampling approach creates a smooth gradient between different
         * terrain features and ensures the generated mesh represents the
         * underlying voxel data accurately.
         * 
         * Parameters:
         * - chunk: Chunk containing the cells
         * - x, y, z: Coordinates of the grid point
         * 
         * Returns: Calculated density value (typically 0.0-1.0)
         */
        private float CalculateDensity(Chunk chunk, int x, int y, int z)
        {
            float density = 0.0f;
            int sampleCount = 0;
            float defaultValue = 0.5f;

            // Sample from all adjacent cells
            for (int dx = -1; dx <= 0; dx++)
            {
                for (int dy = -1; dy <= 0; dy++)
                {
                    for (int dz = -1; dz <= 0; dz++)
                    {
                        int sampleX = x + dx;
                        int sampleY = chunk.Size - 1 - (y + dy);        // Changed this after submission
                        int sampleZ = z + dz;

                        // Skip if outside chunk
                        if (sampleX < 0 || sampleX >= chunk.Size ||
                            sampleY < 0 || sampleY >= chunk.Size ||
                            sampleZ < 0 || sampleZ >= chunk.Size)
                            continue;

                        Cell cell = chunk.GetCell(sampleX, sampleY, sampleZ);
                        if (cell != null && cell.CollapsedState.HasValue)
                        {
                            int state = cell.CollapsedState.Value;

                            // Get density directly from state density values
                            if (stateDensityValues.TryGetValue(state, out float stateDensity))
                            {
                                // For marching cubes: 
                                // - Lower density values (<0.5) = inside terrain (solid)
                                // - Higher density values (>0.5) = outside terrain (air)
                                density += stateDensity;
                            }
                            else
                            {
                                density += 0.5f; // Default to surface threshold
                            }

                            sampleCount++;
                        }
                    }
                }
            }

            if (x == 0 || x == chunk.Size || y == 0 || y == chunk.Size || z == 0 || z == chunk.Size)
            {
                if (sampleCount > 0)
                {
                    // Round to 7 decimal places for numerical stability across chunks
                    float result = 1.0f - (density / sampleCount);
                    return Mathf.Round(result * 10000000f) / 10000000f;
                }
            }

            // Regular computation for interior points
            return sampleCount > 0 ? 1.0f - (density / sampleCount) : 1.0f - defaultValue;
        }


        /*
         * SmoothBoundaries
         * ----------------------------------------------------------------------------
         * Smooths density values at chunk boundaries for seamless terrain.
         * 
         * Critical for eliminating visible seams in terrain, this function:
         * 1. Identifies neighboring chunks in all six directions
         * 2. Gets or generates density fields for neighboring chunks
         * 3. For each shared boundary face:
         *    a. Identifies the exact boundary planes to smooth
         *    b. Sets identical density values at the boundary interface
         *    c. Creates a smooth gradient extending inward from the boundary
         * 4. Ensures special handling for edges where three chunks meet
         * 5. Ensures special handling for corners where eight chunks meet
         * 
         * The seamless boundary handling is achieved by making density values
         * identical at the interface and gradually blending them with distance
         * from the boundary, ensuring continuous isosurfaces across chunks.
         * 
         * Parameters:
         * - densityField: The density field to smooth
         * - chunk: The chunk containing this density field
         * - recursionDepth: Current recursion depth to prevent infinite loops
         */
        private void SmoothBoundaries(float[,,] densityField, Chunk chunk, int recursionDepth = 0)
        {
            // CRITICAL: Restore recursion depth check to prevent infinite recursion
            if (recursionDepth > 2)
            {
                Debug.LogWarning($"Maximum boundary recursion depth reached for chunk {chunk.Position}");
                return;
            }

            // Skip if the chunk is invalid
            if (chunk == null || densityField == null)
            {
                return;
            }

            int size = chunk.Size;

            // Process each direction only once
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                // Skip if no neighbor exists in this direction
                if (!chunk.Neighbors.ContainsKey(dir))
                    continue;

                Chunk neighbor = chunk.Neighbors[dir];

                // Skip if neighbor is null or already being processed (prevents cascading calculations)
                if (neighbor == null || processingChunks.Contains(neighbor.Position))
                    continue;

                // First check for cached neighbor density field
                float[,,] neighborDensity;
                if (densityFieldCache.TryGetValue(neighbor.Position, out neighborDensity))
                {
                    // Using cached density field - no need to regenerate
                }
                else if (recursionDepth < 2) // Limit recursive generation depth
                {
                    try
                    {
                        // Mark as processing to prevent recursive loops
                        processingChunks.Add(neighbor.Position);

                        // Generate the neighbor's density field
                        neighborDensity = GenerateDensityField(neighbor, recursionDepth + 1);

                        // Remove from processing list after generation
                        processingChunks.Remove(neighbor.Position);
                    }
                    catch (Exception e)
                    {
                        // Handle any errors during generation
                        Debug.LogError($"Error generating density field for neighbor at {neighbor.Position}: {e.Message}");
                        processingChunks.Remove(neighbor.Position);
                        continue;
                    }
                }
                else
                {
                    // Create a simple continuation field instead of full generation
                    neighborDensity = CreateContinuationField(neighbor);
                }

                // Skip if failed to get neighbor density field
                if (neighborDensity == null)
                    continue;

                // Apply appropriate boundary smoothing based on direction
                try
                {
                    switch (dir)
                    {
                        case Direction.Left:
                            SmoothXBoundary(densityField, neighborDensity, 0, size);
                            break;
                        case Direction.Right:
                            SmoothXBoundary(densityField, neighborDensity, size, 0);
                            break;
                        case Direction.Down:
                            SmoothYBoundary(densityField, neighborDensity, 0, size);
                            break;
                        case Direction.Up:
                            SmoothYBoundary(densityField, neighborDensity, size, 0);
                            break;
                        case Direction.Back:
                            SmoothZBoundary(densityField, neighborDensity, 0, size);
                            break;
                        case Direction.Forward:
                            SmoothZBoundary(densityField, neighborDensity, size, 0);
                            break;
                    }
                }
                catch (Exception e)
                {
                    // Handle smoothing errors gracefully
                    Debug.LogError($"Error smoothing boundary for chunk {chunk.Position} in direction {dir}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Smooth the Y boundary between chunks
        /// </summary>
        private void SmoothYBoundary(float[,,] densityField1, float[,,] densityField2, int index1, int index2)
        {
            // Check if arrays have compatible dimensions
            if (densityField1 == null || densityField2 == null)
                return;

            int width1 = densityField1.GetLength(0);
            int depth1 = densityField1.GetLength(2);
            int width2 = densityField2.GetLength(0);
            int depth2 = densityField2.GetLength(2);

            // Determine common dimensions to avoid index out of bounds
            int commonWidth = Mathf.Min(width1, width2);
            int commonDepth = Mathf.Min(depth1, depth2);

            // Validate indices
            if (index1 < 0 || index1 >= densityField1.GetLength(1) ||
                index2 < 0 || index2 >= densityField2.GetLength(1))
            {
                Debug.LogWarning($"SmoothYBoundary: Invalid indices - index1: {index1}, max: {densityField1.GetLength(1) - 1}, index2: {index2}, max: {densityField2.GetLength(1) - 1}");
                return;
            }

            int gradientWidth = 8;      // Changed this after submission 3 -> 8

            // Perform smoothing only on the common area
            for (int x = 0; x < commonWidth; x++)
            {
                for (int z = 0; z < commonDepth; z++)
                {
                    float averageDensity = (densityField1[x, index1, z] + densityField2[x, index2, z]) / 2.0f;

                    // Set boundary values
                    densityField1[x, index1, z] = averageDensity;
                    densityField2[x, index2, z] = averageDensity;

                    // Apply gradient with non-linear falloff
                    for (int i = 1; i <= gradientWidth; i++)
                    {
                        // Apply to field 1
                        if (index1 - i >= 0 && index1 - i < densityField1.GetLength(1))
                        {
                            float blendFactor = 1.0f - Mathf.Pow(i / (float)(gradientWidth + 1), 2);
                            densityField1[x, index1 - i, z] = Mathf.Lerp(
                                densityField1[x, index1 - i, z],
                                averageDensity,
                                blendFactor);
                        }

                        // Apply to field 2
                        if (index2 - i >= 0 && index2 - i < densityField2.GetLength(1))
                        {
                            float blendFactor = 1.0f - Mathf.Pow(i / (float)(gradientWidth + 1), 2);
                            densityField2[x, index2 - i, z] = Mathf.Lerp(
                                densityField2[x, index2 - i, z],
                                averageDensity,
                                blendFactor);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Smooth the X boundary between chunks
        /// </summary>
        private void SmoothXBoundary(float[,,] densityField1, float[,,] densityField2, int index1, int index2)
        {
            // Check if arrays have compatible dimensions
            if (densityField1 == null || densityField2 == null)
                return;

            int height1 = densityField1.GetLength(1); // Y dimension
            int depth1 = densityField1.GetLength(2); // Z dimension
            int height2 = densityField2.GetLength(1);
            int depth2 = densityField2.GetLength(2);

            // Determine common dimensions to avoid index out of bounds
            int commonHeight = Mathf.Min(height1, height2);
            int commonDepth = Mathf.Min(depth1, depth2);

            // Validate indices
            if (index1 < 0 || index1 >= densityField1.GetLength(0) ||
                index2 < 0 || index2 >= densityField2.GetLength(0))
            {
                Debug.LogWarning($"SmoothXBoundary: Invalid indices - index1: {index1}, max: {densityField1.GetLength(0) - 1}, index2: {index2}, max: {densityField2.GetLength(0) - 1}");
                return;
            }

            int gradientWidth = 8; // Increased from implicit 3 -> 8

            // Perform smoothing only on the common area
            for (int y = 0; y < commonHeight; y++)
            {
                for (int z = 0; z < commonDepth; z++)
                {
                    // CRITICAL FIX: Use identical values exactly at the boundary
                    float averageDensity = (densityField1[index1, y, z] + densityField2[index2, y, z]) / 2.0f;

                    // Set both fields to the same value at the boundary
                    densityField1[index1, y, z] = averageDensity;
                    densityField2[index2, y, z] = averageDensity;

                    // CRITICAL FIX: Apply gradient over multiple cells inward with non-linear falloff
                    for (int i = 1; i <= gradientWidth; i++)
                    {
                        // Apply smoothing inward for field 1
                        if (index1 - i >= 0 && index1 - i < densityField1.GetLength(0))
                        {
                            // Use non-linear falloff (smoother transition)
                            float t = (float)i / gradientWidth;
                            float blendFactor = 1.0f - (t * t * (3.0f - 2.0f * t));
                            //float blendFactor = 1.0f - Mathf.Pow(i / (float)(gradientWidth + 1), 2);
                            densityField1[index1 - i, y, z] = Mathf.Lerp(
                                densityField1[index1 - i, y, z],
                                averageDensity,
                                blendFactor);
                        }

                        // Apply smoothing inward for field 2
                        if (index2 - i >= 0 && index2 - i < densityField2.GetLength(0))
                        {
                            float t = (float)i / gradientWidth;
                            float blendFactor = 1.0f - (t * t * (3.0f - 2.0f * t));
                            //float blendFactor = 1.0f - Mathf.Pow(i / (float)(gradientWidth + 1), 2);
                            densityField2[index2 - i, y, z] = Mathf.Lerp(
                                densityField2[index2 - i, y, z],
                                averageDensity,
                                blendFactor);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Smooth the Z boundary between chunks
        /// </summary>
        private void SmoothZBoundary(float[,,] densityField1, float[,,] densityField2, int index1, int index2)
        {
            // Check if arrays have compatible dimensions
            if (densityField1 == null || densityField2 == null)
                return;

            int width1 = densityField1.GetLength(0); // X dimension
            int height1 = densityField1.GetLength(1); // Y dimension
            int width2 = densityField2.GetLength(0);
            int height2 = densityField2.GetLength(1);

            // Determine common dimensions to avoid index out of bounds
            int commonWidth = Mathf.Min(width1, width2);
            int commonHeight = Mathf.Min(height1, height2);

            // Validate indices
            if (index1 < 0 || index1 >= densityField1.GetLength(2) ||
                index2 < 0 || index2 >= densityField2.GetLength(2))
            {
                Debug.LogWarning($"SmoothZBoundary: Invalid indices - index1: {index1}, max: {densityField1.GetLength(2) - 1}, index2: {index2}, max: {densityField2.GetLength(2) - 1}");
                return;
            }

            int gradientWidth = 8;      // Changed this after submission 3 -> 8

            // Perform smoothing only on the common area
            for (int x = 0; x < commonWidth; x++)
            {
                for (int y = 0; y < commonHeight; y++)
                {
                    // CRITICAL: Use identical values exactly at the boundary
                    float averageDensity = (densityField1[x, y, index1] + densityField2[x, y, index2]) / 2.0f;

                    // Set both fields to the same value at the boundary
                    densityField1[x, y, index1] = averageDensity;
                    densityField2[x, y, index2] = averageDensity;

                    for (int i = 1; i <= gradientWidth; i++)
                    {
                        // Apply to field 1
                        if (index1 - i >= 0 && index1 - i < densityField1.GetLength(2))
                        {
                            float t = (float)i / gradientWidth;
                            float blendFactor = 1.0f - (t * t * (3.0f - 2.0f * t));
                            //float blendFactor = 0.9f - Mathf.Pow(i / (float)(gradientWidth + 1), 2);
                            //int inwardIndex = (index1 == 0) ? 1 : index1 - 1;
                            densityField1[x, y, index1 - i] = Mathf.Lerp(
                                densityField1[x, y, index1 - i],
                                averageDensity,
                                blendFactor);
                        }

                        if (index2-i >= 0 && index2-i < densityField2.GetLength(2))
                        {
                            float t = (float)i / gradientWidth;
                            float blendFactor = 1.0f - (t * t * (3.0f - 2.0f * t));
                            //float blendFactor = 0.70f - Mathf.Pow(i / (float)(gradientWidth + 1), 2);
                            ////int inwardIndex = (index2 == 0) ? 1 : index2 - 1;
                            densityField2[x, y, index2 - i] = Mathf.Lerp(
                                densityField2[x, y, index2 - i],
                                averageDensity,
                                blendFactor);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Create a temporary continuation field for a neighbor that's being processed
        /// </summary>
        private float[,,] CreateContinuationField(Chunk chunk)
        {
            // Create a field that continues trends from adjacent chunks
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

        /// <summary>
        /// Clear the cache completely
        /// </summary>
        public void ClearCache()
        {
            densityFieldCache.Clear();
            processingChunks.Clear();
            cacheEvictionQueue.Clear();
            Debug.Log("Density field cache cleared");
        }

        /// <summary>
        /// Set the surface level threshold
        /// </summary>
        public void SetSurfaceLevel(float level)
        {
            surfaceLevel = level;
        }

        /// <summary>
        /// Set the maximum cache size
        /// </summary>
        public void SetMaxCacheSize(int size)
        {
            maxCacheSize = Mathf.Max(1, size);

            // If current cache exceeds new size, trim it
            while (densityFieldCache.Count > maxCacheSize && cacheEvictionQueue.Count > 0)
            {
                Vector3Int oldestChunk = cacheEvictionQueue.Dequeue();
                densityFieldCache.Remove(oldestChunk);
            }
        }

    }
}