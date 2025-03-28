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
        private float smoothingFactor = 1.0f;
        private int chunkSize = 8;

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
            // Set more extreme density values to ensure there's a clear surface
            // Values below 0.5 are "air", values above 0.5 are "solid"
            stateDensityValues.Add(0, 0.1f);   // Empty (definitely air)
            stateDensityValues.Add(1, 0.8f);   // Ground (definitely solid)
            stateDensityValues.Add(2, 0.73f);  // Grass (solid)
            stateDensityValues.Add(3, 0.35f);  // Water (slightly solid for water surface)
            stateDensityValues.Add(4, 0.92f);  // Rock (very solid)
            stateDensityValues.Add(5, 0.68f);  // Sand (moderately solid)
            stateDensityValues.Add(6, 0.78f);  // Tree (solid)
            stateDensityValues.Add(7, 0.8f);   // Forest (solid)

            chunkSize = 8;
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

        /// <summary>
        /// Generate a density field for a chunk
        /// </summary>
        public float[,,] GenerateDensityField(Chunk chunk)
        {
            chunkSize = chunk.Size;

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

            // Check if we've already computed this field (belt-and-suspenders check)
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

            // Apply pre-processing to improve terrain features
            PreProcessDensityField(densityField, chunk);

            if (!hasSolidCells || !hasEmptyCells)
            {
                Debug.LogWarning($"Chunk {chunk.Position} still lacks a proper surface after processing: hasSolid={hasSolidCells}, hasEmpty={hasEmptyCells}");
            }

            // Handle boundaries to ensure seamless meshes - but only AFTER we've calculated the main density field
            SmoothBoundaries(densityField, chunk);

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
            // If we're at capacity, remove the least recently used item
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

            // Apply vertical gradient (things get less solid as we go up)
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
            // Use global coordinates for consistency across chunks
            float globalX = chunkPos.x * chunkSize + x;
            float globalY = chunkPos.y * chunkSize + y;
            float globalZ = chunkPos.z * chunkSize + z;

            // 1. Cross-chunk cave system
            if (density > 0.6f)
            {
                // Use 3D Perlin noise for caves with a scale that ensures they cross chunks
                float caveNoiseX = Mathf.PerlinNoise(globalX * 0.05f, globalY * 0.05f);
                float caveNoiseZ = Mathf.PerlinNoise(globalY * 0.05f, globalZ * 0.05f);
                float caveNoise = (caveNoiseX + caveNoiseZ) * 0.5f;

                // Make caves follow a specific pattern that will visibly cross boundaries
                float caveTunnel = Mathf.Sin(globalX * 0.1f) * Mathf.Sin(globalZ * 0.1f);
                caveTunnel = Mathf.Abs(caveTunnel);

                if (caveNoise > 0.6f && caveTunnel < 0.3f)
                {
                    density *= 0.3f; // Create more pronounced caves
                }
            }

            // 2. Cross-chunk mountain range
            float mountainRange = Mathf.PerlinNoise(globalX * 0.02f, globalZ * 0.02f);
            // Create ridge lines that will definitely cross chunk boundaries
            float ridgeLine = Mathf.Abs(Mathf.Sin(globalX * 0.05f + globalZ * 0.05f));

            if (mountainRange > 0.6f && ridgeLine < 0.2f && y > 3)
            {
                density += 0.3f; // Create more pronounced mountain ridges
            }

            // 3. Cross-chunk river system
            float riverNoise = Mathf.PerlinNoise(globalX * 0.03f, globalZ * 0.03f);
            float riverChannel = Mathf.Abs(Mathf.Sin(globalX * 0.02f + globalZ * 0.04f));

            if (riverNoise > 0.5f && riverChannel < 0.1f && y < chunkSize / 2)
            {
                density -= 0.3f; // Create river channels
            }

            return density;
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

                        // Apply with a wider influence radius for smoother transition
                        int blendRadius = 2; // How far from corner to blend
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

        /// <summary>
        /// Calculate the density value for a grid point based on surrounding cells
        /// </summary>
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

        /// <summary>
        /// Smooth boundaries between chunks to ensure seamless meshes
        /// </summary>
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

        /// <summary>
        /// Get or create a field for a neighbor with continuation from existing chunks
        /// </summary>
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