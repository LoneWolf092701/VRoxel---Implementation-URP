// Assets/Scripts/WFC/MarchingCubes/DensityFieldGenerator.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using System.Linq;

namespace WFC.MarchingCubes
{
    public class DensityFieldGenerator
    {
        // Configuration
        private float surfaceLevel = 0.5f;
        private int chunkSize = 16;

        // Default density range for cells without states
        private float defaultEmptyDensity = 0.1f;  // For empty/air (below surface)
        private float defaultSolidDensity = 0.9f;  // For solid terrain (above surface)

        // State density values (how "solid" each state is)
        private Dictionary<int, float> stateDensityValues = new Dictionary<int, float>();

        // Cache to prevent infinite recursion and improve performance
        private Dictionary<Vector3Int, float[,,]> densityFieldCache = new Dictionary<Vector3Int, float[,,]>();
        private HashSet<Vector3Int> processingChunks = new HashSet<Vector3Int>();

        // Cache management
        private int maxCacheSize = 100; // Adjust based on expected world size
        private Queue<Vector3Int> cacheEvictionQueue = new Queue<Vector3Int>();
        public DensityFieldGenerator()
        {
            // Simplify - keep only essential states
            stateDensityValues.Add(0, 0.1f);   // Empty (air)
            stateDensityValues.Add(1, 0.8f);   // Ground 
            stateDensityValues.Add(3, 0.6f);   // Water
            stateDensityValues.Add(4, 0.85f);  // Rock

            chunkSize = 16;
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
         * 1. Initializes a 3D grid of density values with dimensions (size+1)�
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
                        if (densityField[x, y, z] > surfaceLevel)
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
                Debug.Log($"Cache full - Evicting chunk {oldestChunk} from density field cache");
            }

            // Add new entry to cache
            densityFieldCache[chunkPos] = densityField;
            cacheEvictionQueue.Enqueue(chunkPos);
        }

        /// <summary>
        /// Evict distant chunks from the cache to free up memory
        /// </summary>
        public void EvictDistantChunks(Vector3 viewerPosition, float maxDistance)
        {
            List<Vector3Int> chunksToEvict = new List<Vector3Int>();

            // Find chunks beyond the max distance
            foreach (var entry in densityFieldCache)
            {
                Vector3 chunkCenter = new Vector3(
                    entry.Key.x * chunkSize + chunkSize / 2,
                    entry.Key.y * chunkSize + chunkSize / 2,
                    entry.Key.z * chunkSize + chunkSize / 2
                );

                float distance = Vector3.Distance(chunkCenter, viewerPosition);
                if (distance > maxDistance)
                {
                    chunksToEvict.Add(entry.Key);
                }
            }

            // Evict chunks
            foreach (var chunkPos in chunksToEvict)
            {
                densityFieldCache.Remove(chunkPos);

                // Also remove from eviction queue
                cacheEvictionQueue = new Queue<Vector3Int>(
                    cacheEvictionQueue.Where(pos => !pos.Equals(chunkPos)));

                Debug.Log($"Distance-based eviction: Removed chunk {chunkPos} from density field cache");
            }
        }

        /// <summary>
        /// Apply additional processing to the density field to create more interesting terrain
        /// </summary>
        private void PreProcessDensityField(float[,,] densityField, Chunk chunk)
        {
            int size = chunk.Size;
            Vector3Int chunkPos = chunk.Position;

            for (int x = 0; x <= size; x++)
            {
                for (int y = 0; y <= size; y++)
                {
                    for (int z = 0; z <= size; z++)
                    {
                        // First apply height variation to ensure consistent base terrain
                        densityField[x, y, z] = ApplyHeightVariation(
                            densityField[x, y, z], x, y, z, chunkPos, size);

                        // Then apply additional terrain features
                        densityField[x, y, z] = ApplyTerrainFeatures(
                            densityField[x, y, z], x, y, z, chunkPos, size);
                    }
                }
            }
        }

        /// <summary>
        /// Apply height-based variations to the density field
        /// </summary>
        private float ApplyHeightVariation(float baseDensity, int x, int y, int z, Vector3Int chunkPos, int chunkSize)
        {
            // Use global coordinates to ensure consistent heights across chunks
            float globalX = chunkPos.x * chunkSize + x;
            float globalZ = chunkPos.z * chunkSize + z;

            // Create a consistent height map across all chunks
            // Lower frequency noise creates smoother, more gradual height changes
            float heightMap = Mathf.PerlinNoise(globalX * 0.03f, globalZ * 0.03f);

            // Apply vertical gradient
            // This creates a consistent height basis across all chunks
            float baseHeight = 3.0f + heightMap * 4.0f; // Base height between 3-7 units
            float heightInfluence = Mathf.Clamp01((baseHeight - y) * 0.2f);

            // Blend the original density with the height influence
            return Mathf.Lerp(baseDensity, baseDensity * heightInfluence + 0.5f, 0.7f);
        }

        /// <summary>
        /// Apply additional terrain features like caves, mountain ridges, and rivers
        /// </summary>
        private float ApplyTerrainFeatures(float density, int x, int y, int z, Vector3Int chunkPos, int chunkSize)
        {
            // Simplified version - no caves or complex features
            // Just add minor variation for natural look
            float globalX = chunkPos.x * chunkSize + x;
            float globalZ = chunkPos.z * chunkSize + z;

            // Add slight variation to density
            float variation = Mathf.PerlinNoise(globalX * 0.05f, globalZ * 0.05f) * 0.1f;
            return density + variation - 0.05f;
        }
        /// <summary>
        /// Smooth corner points where multiple chunks meet
        /// </summary>
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

                        // Average the corners
                        float cornerAverage = (densityField[cornerX, cornerY, cornerZ] +
                                             cornerNeighborField[neighborX, neighborY, neighborZ]) / 2.0f;

                        // CRITICAL: Use identical values at the corner point
                        densityField[cornerX, cornerY, cornerZ] = cornerAverage;
                        cornerNeighborField[neighborX, neighborY, neighborZ] = cornerAverage;

                        // Apply with a wider influence radius for smoother transition
                        int blendRadius = 3; // How far from corner to blend
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

                                    // Calculate blend factor based on distance from corner
                                    float distance = Mathf.Sqrt(x * x + y * y + z * z);
                                    if (distance > blendRadius) continue;

                                    float blendFactor = 1.0f - (distance / blendRadius);

                                    // Apply blended value
                                    int blendX = cornerX - dx * x;
                                    int blendY = cornerY - dy * y;
                                    int blendZ = cornerZ - dz * z;

                                    densityField[blendX, blendY, blendZ] = Mathf.Lerp(
                                        densityField[blendX, blendY, blendZ],
                                        cornerAverage,
                                        blendFactor * 0.7f // Reduce influence for subtlety
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
            // For grid points, sample from ALL 8 surrounding cells
            float density = 0.0f;
            int sampleCount = 0;
            float defaultValue = 0.5f; // Use neutral value when no samples

            // Sample from all adjacent cells (up to 8 for interior points)
            for (int dx = -1; dx <= 0; dx++)
            {
                for (int dy = -1; dy <= 0; dy++)
                {
                    for (int dz = -1; dz <= 0; dz++)
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

                            // CRITICAL: Ensure solid states have consistent density values
                            float stateDensity;
                            if (state == 0) // Air
                                stateDensity = 0.1f;
                            else if (state == 3) // Water
                                stateDensity = 0.7f;
                            else // Ground, rock, etc. - all solid
                                stateDensity = 0.9f;

                            density += stateDensity;
                            sampleCount++;
                        }
                    }
                }
            }

            // Calculate average with fallback to default
            return sampleCount > 0 ? density / sampleCount : defaultValue;
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
            // Get chunk size
            int size = chunk.Size;

            // If no neighbors, nothing to smooth
            if (chunk.Neighbors == null) return;

            // Iterate through all six directions
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                // Skip if no neighbor in this direction
                if (!chunk.Neighbors.ContainsKey(dir))
                    continue;

                Chunk neighbor = chunk.Neighbors[dir];

                // Skip if already processing this neighbor (prevents infinite recursion)
                if (processingChunks.Contains(neighbor.Position))
                    continue;
                try
                {
                    // Safely get or generate neighbor's density field
                    float[,,] neighborDensity = GetNeighborDensityField(neighbor, recursionDepth);

                    // Smoothing logic varies by direction
                    switch (dir)
                    {
                        case Direction.Left: // Negative X boundary
                            SmoothXBoundary(densityField, neighborDensity, 0, size);
                            break;

                        case Direction.Right: // Positive X boundary
                            SmoothXBoundary(densityField, neighborDensity, size, 0);
                            break;

                        case Direction.Down: // Negative Y boundary
                            SmoothYBoundary(densityField, neighborDensity, 0, size);
                            break;

                        case Direction.Up: // Positive Y boundary
                            SmoothYBoundary(densityField, neighborDensity, size, 0);
                            break;

                        case Direction.Back: // Negative Z boundary
                            SmoothZBoundary(densityField, neighborDensity, 0, size);
                            break;

                        case Direction.Forward: // Positive Z boundary
                            SmoothZBoundary(densityField, neighborDensity, size, 0);
                            break;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error smoothing boundary with neighbor at {neighbor.Position}: {e.Message}");
                    // Continue with other neighbors instead of letting the exception propagate
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

            // Perform smoothing only on the common area
            for (int x = 0; x < commonWidth; x++)
            {
                for (int z = 0; z < commonDepth; z++)
                {
                    // CRITICAL: Use identical values exactly at the boundary
                    float averageDensity = (densityField1[x, index1, z] + densityField2[x, index2, z]) / 2.0f;

                    // Set both fields to the same value at the boundary
                    densityField1[x, index1, z] = averageDensity;
                    densityField2[x, index2, z] = averageDensity;

                    // Also smooth adjacent cells with a falloff gradient
                    if (index1 > 0 && index1 < densityField1.GetLength(1) - 1)
                    {
                        float blendFactor = 0.9f; // Less influence as move away from boundary
                        int inwardIndex = (index1 == 0) ? 1 : index1 - 1;
                        densityField1[x, inwardIndex, z] = Mathf.Lerp(
                            densityField1[x, inwardIndex, z],
                            averageDensity,
                            blendFactor);
                    }

                    if (index2 > 0 && index2 < densityField2.GetLength(1) - 1)
                    {
                        float blendFactor = 0.7f;
                        int inwardIndex = (index2 == 0) ? 1 : index2 - 1;
                        densityField2[x, inwardIndex, z] = Mathf.Lerp(
                            densityField2[x, inwardIndex, z],
                            averageDensity,
                            blendFactor);
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

            // Perform smoothing only on the common area
            for (int y = 0; y < commonHeight; y++)
            {
                for (int z = 0; z < commonDepth; z++)
                {
                    // CRITICAL: Use identical values exactly at the boundary
                    float averageDensity = (densityField1[index1, y, z] + densityField2[index2, y, z]) / 2.0f;

                    // Set both fields to the same value at the boundary
                    densityField1[index1, y, z] = averageDensity;
                    densityField2[index2, y, z] = averageDensity;

                    // Also smooth adjacent cells with a falloff gradient
                    if (index1 > 0 && index1 < densityField1.GetLength(0) - 1)
                    {
                        float blendFactor = 0.9f;
                        int inwardIndex = (index1 == 0) ? 1 : index1 - 1;
                        densityField1[inwardIndex, y, z] = Mathf.Lerp(
                            densityField1[inwardIndex, y, z],
                            averageDensity,
                            blendFactor);
                    }

                    if (index2 > 0 && index2 < densityField2.GetLength(0) - 1)
                    {
                        float blendFactor = 0.7f;
                        int inwardIndex = (index2 == 0) ? 1 : index2 - 1;
                        densityField2[inwardIndex, y, z] = Mathf.Lerp(
                            densityField2[inwardIndex, y, z],
                            averageDensity,
                            blendFactor);
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

                    // Also smooth adjacent cells with a falloff gradient
                    if (index1 > 0 && index1 < densityField1.GetLength(2) - 1)
                    {
                        float blendFactor = 0.9f;
                        int inwardIndex = (index1 == 0) ? 1 : index1 - 1;
                        densityField1[x, y, inwardIndex] = Mathf.Lerp(
                            densityField1[x, y, inwardIndex],
                            averageDensity,
                            blendFactor);
                    }

                    if (index2 > 0 && index2 < densityField2.GetLength(2) - 1)
                    {
                        float blendFactor = 0.7f;
                        int inwardIndex = (index2 == 0) ? 1 : index2 - 1;
                        densityField2[x, y, inwardIndex] = Mathf.Lerp(
                            densityField2[x, y, inwardIndex],
                            averageDensity,
                            blendFactor);
                    }
                }
            }
        }

        /// <summary>
        /// Get or create a field for a neighbor with continuation from existing chunks
        /// </summary>
        private float[,,] GetNeighborDensityField(Chunk neighbor, int recursionDepth=0)
        {
            if (recursionDepth > 3)
            {
                Debug.LogWarning($"Recursion depth exceeded for chunk {neighbor.Position}");
                return CreateContinuationField(neighbor);
            }

            // Get or generate neighbor density field safely
            if (densityFieldCache.TryGetValue(neighbor.Position, out float[,,] cachedField))
                return cachedField;

            if (processingChunks.Contains(neighbor.Position))
            {
                // Create a temporary field that continues the gradient from this chunk
                // rather than using a default value
                return CreateContinuationField(neighbor);
            }

            return GenerateDensityField(neighbor, recursionDepth + 1);
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