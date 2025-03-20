// Assets/Scripts/WFC/Chunking/ChunkManager.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using System.Linq;
using Utils; // For PriorityQueue

namespace WFC.Chunking
{
    public class ChunkManager : MonoBehaviour
    {
        [Header("Chunk Settings")]
        [SerializeField] private int chunkSize = 8;
        [SerializeField] private float loadDistance = 32f;
        [SerializeField] private float unloadDistance = 48f;
        [SerializeField] private int maxConcurrentChunks = 4;

        [Header("References")]
        [SerializeField] private Transform viewer;

        // Loaded chunks
        private Dictionary<Vector3Int, Chunk> loadedChunks = new Dictionary<Vector3Int, Chunk>();

        // Tasks queue
        private PriorityQueue<ChunkTask, float> chunkTasks = new PriorityQueue<ChunkTask, float>();

        // Viewer data
        private Vector3 viewerPosition;
        private Vector3 viewerVelocity;
        private Vector3 lastViewerPosition;

        private void Start()
        {
            if (viewer == null)
                viewer = Camera.main.transform;

            viewerPosition = viewer.position;
            lastViewerPosition = viewerPosition;
        }

        private void Update()
        {
            // Update viewer position and velocity
            viewerPosition = viewer.position;
            viewerVelocity = (viewerPosition - lastViewerPosition) / Time.deltaTime;
            lastViewerPosition = viewerPosition;

            // Update chunk priorities
            UpdateChunkPriorities();

            // Manage chunks (load/unload)
            ManageChunks();

            // Process chunk tasks
            ProcessChunkTasks();
        }

        private void UpdateChunkPriorities()
        {
            foreach (var chunk in loadedChunks.Values)
            {
                chunk.Priority = CalculateChunkPriority(chunk);
            }
        }

        private float CalculateChunkPriority(Chunk chunk)
        {
            // Convert chunk position to world position
            Vector3 chunkWorldPos = new Vector3(
                chunk.Position.x * chunkSize,
                chunk.Position.y * chunkSize,
                chunk.Position.z * chunkSize
            );

            // Distance from viewer
            float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

            // View alignment (dot product of direction to chunk and viewer velocity)
            Vector3 dirToChunk = (chunkWorldPos - viewerPosition).normalized;
            float viewAlignment = Vector3.Dot(dirToChunk, viewerVelocity.normalized);

            // Calculate priority (higher is more important)
            float priority = (1.0f / (distance + 1.0f)) * (viewAlignment > 0 ? (1.0f + viewAlignment) : 0.5f);

            // Bonus for chunks that are partially collapsed
            if (!chunk.IsFullyCollapsed)
            {
                priority *= 1.2f;
            }

            return priority;
        }

        private void ManageChunks()
        {
            // Get chunks to load
            List<Vector3Int> chunksToLoad = GetChunksToLoad();

            foreach (var chunkPos in chunksToLoad)
            {
                if (!loadedChunks.ContainsKey(chunkPos))
                {
                    // Create chunk load task
                    ChunkTask task = new ChunkTask
                    {
                        Type = ChunkTaskType.Create,
                        Position = chunkPos,
                        Priority = CalculateLoadPriority(chunkPos)
                    };

                    chunkTasks.Enqueue(task, task.Priority);
                }
            }

            // Get chunks to unload
            List<Vector3Int> chunksToUnload = GetChunksToUnload();

            foreach (var chunkPos in chunksToUnload)
            {
                if (loadedChunks.ContainsKey(chunkPos))
                {
                    // Create chunk unload task
                    ChunkTask task = new ChunkTask
                    {
                        Type = ChunkTaskType.Unload,
                        Position = chunkPos,
                        Chunk = loadedChunks[chunkPos],
                        Priority = -1f // Low priority
                    };

                    chunkTasks.Enqueue(task, task.Priority);
                }
            }
        }

        private List<Vector3Int> GetChunksToLoad()
        {
            // Calculate chunk coordinates of viewer
            Vector3Int viewerChunk = new Vector3Int(
                Mathf.FloorToInt(viewerPosition.x / chunkSize),
                Mathf.FloorToInt(viewerPosition.y / chunkSize),
                Mathf.FloorToInt(viewerPosition.z / chunkSize)
            );

            // Calculate predicted position
            Vector3 predictedPos = viewerPosition + viewerVelocity * 1.0f; // 1 second prediction
            Vector3Int predictedChunk = new Vector3Int(
                Mathf.FloorToInt(predictedPos.x / chunkSize),
                Mathf.FloorToInt(predictedPos.y / chunkSize),
                Mathf.FloorToInt(predictedPos.z / chunkSize)
            );

            // Calculate load distance in chunks
            int loadChunks = Mathf.CeilToInt(loadDistance / chunkSize);

            // Find all chunk positions within load distance
            List<Vector3Int> chunksToLoad = new List<Vector3Int>();

            for (int x = viewerChunk.x - loadChunks; x <= viewerChunk.x + loadChunks; x++)
            {
                for (int y = viewerChunk.y - loadChunks; y <= viewerChunk.y + loadChunks; y++)
                {
                    for (int z = viewerChunk.z - loadChunks; z <= viewerChunk.z + loadChunks; z++)
                    {
                        Vector3Int pos = new Vector3Int(x, y, z);

                        // Skip if already loaded
                        if (loadedChunks.ContainsKey(pos))
                            continue;

                        // Check if within load distance
                        Vector3 chunkWorldPos = new Vector3(x * chunkSize, y * chunkSize, z * chunkSize);
                        float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

                        if (distance <= loadDistance)
                        {
                            chunksToLoad.Add(pos);
                        }
                    }
                }
            }

            // Sort by priority (distance to predicted position)
            chunksToLoad.Sort((a, b) => {
                Vector3 posA = new Vector3(a.x * chunkSize, a.y * chunkSize, a.z * chunkSize);
                Vector3 posB = new Vector3(b.x * chunkSize, b.y * chunkSize, b.z * chunkSize);

                float distA = Vector3.Distance(posA, predictedPos);
                float distB = Vector3.Distance(posB, predictedPos);

                return distA.CompareTo(distB);
            });

            // Limit to max concurrent chunks
            if (chunksToLoad.Count > maxConcurrentChunks)
            {
                chunksToLoad = chunksToLoad.GetRange(0, maxConcurrentChunks);
            }

            return chunksToLoad;
        }

        private List<Vector3Int> GetChunksToUnload()
        {
            // Find chunks that are too far from both current and predicted positions
            Vector3 predictedPos = viewerPosition + viewerVelocity * 1.0f; // 1 second prediction

            return loadedChunks.Keys.Where(chunkPos => {
                Vector3 chunkWorldPos = new Vector3(
                    chunkPos.x * chunkSize,
                    chunkPos.y * chunkSize,
                    chunkPos.z * chunkSize
                );

                float distToCurrent = Vector3.Distance(chunkWorldPos, viewerPosition);
                float distToPredicted = Vector3.Distance(chunkWorldPos, predictedPos);

                return distToCurrent > unloadDistance && distToPredicted > unloadDistance;
            }).ToList();
        }

        private float CalculateLoadPriority(Vector3Int chunkPos)
        {
            // Convert to world position
            Vector3 chunkWorldPos = new Vector3(
                chunkPos.x * chunkSize,
                chunkPos.y * chunkSize,
                chunkPos.z * chunkSize
            );

            // Distance from viewer
            float distance = Vector3.Distance(chunkWorldPos, viewerPosition);

            // View alignment
            Vector3 dirToChunk = (chunkWorldPos - viewerPosition).normalized;
            float viewAlignment = Vector3.Dot(dirToChunk, viewerVelocity.normalized);

            // Priority (higher for chunks closer and in view direction)
            return (1.0f / (distance + 1.0f)) * (viewAlignment > 0 ? (1.0f + viewAlignment) : 0.5f);
        }

        private void ProcessChunkTasks()
        {
            // Process a limited number of tasks per frame
            int tasksProcessed = 0;
            int maxTasksPerFrame = 2;

            while (chunkTasks.Count > 0 && tasksProcessed < maxTasksPerFrame)
            {
                ChunkTask task = chunkTasks.Dequeue();

                switch (task.Type)
                {
                    case ChunkTaskType.Create:
                        CreateChunk(task.Position);
                        break;

                    case ChunkTaskType.Unload:
                        UnloadChunk(task.Position);
                        break;

                    case ChunkTaskType.Collapse:
                        CollapseChunk(task.Chunk, task.MaxIterations);
                        break;

                    case ChunkTaskType.GenerateMesh:
                        GenerateChunkMesh(task.Chunk);
                        break;
                }

                tasksProcessed++;
            }
        }

        private void CreateChunk(Vector3Int position)
        {
            // Create new chunk
            Chunk chunk = new Chunk(position, chunkSize);

            // Add to loaded chunks
            loadedChunks[position] = chunk;

            // Connect to neighbors
            ConnectChunkNeighbors(chunk);

            // Initialize boundary buffers
            InitializeBoundaryBuffers(chunk);

            // TODO: Initialize with WFC state

            // Create collapse task
            ChunkTask task = new ChunkTask
            {
                Type = ChunkTaskType.Collapse,
                Chunk = chunk,
                MaxIterations = 100,
                Priority = chunk.Priority
            };

            chunkTasks.Enqueue(task, task.Priority);
        }

        private void UnloadChunk(Vector3Int position)
        {
            if (loadedChunks.TryGetValue(position, out Chunk chunk))
            {
                // Clean up any resources

                // Remove from loaded chunks
                loadedChunks.Remove(position);
            }
        }

        private void ConnectChunkNeighbors(Chunk chunk)
        {
            // Check each direction for neighbors
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                Vector3Int offset = dir.ToVector3Int();
                Vector3Int neighborPos = chunk.Position + offset;

                if (loadedChunks.TryGetValue(neighborPos, out Chunk neighbor))
                {
                    // Connect chunks both ways
                    chunk.Neighbors[dir] = neighbor;
                    neighbor.Neighbors[dir.GetOpposite()] = chunk;
                }
            }
        }

        private void InitializeBoundaryBuffers(Chunk chunk)
        {
            // TODO: Implement boundary buffer initialization
            // This should be similar to the WFCGenerator implementation
        }

        private void CollapseChunk(Chunk chunk, int maxIterations)
        {
            // TODO: Implement WFC collapse
            // This would call into the WFCGenerator to execute WFC on this chunk
        }

        private void GenerateChunkMesh(Chunk chunk)
        {
            // TODO: Implement mesh generation
            // This would connect to your Marching Cubes implementation
        }

        // Helper class for chunk tasks
        private class ChunkTask
        {
            public ChunkTaskType Type { get; set; }
            public Vector3Int Position { get; set; }
            public Chunk Chunk { get; set; }
            public int MaxIterations { get; set; }
            public float Priority { get; set; }
        }

        private enum ChunkTaskType
        {
            Create,
            Unload,
            Collapse,
            GenerateMesh
        }
    }
}