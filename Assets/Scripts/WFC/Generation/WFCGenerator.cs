// Assets/Scripts/WFC/Generation/WFCGenerator.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Boundary;
using System.Linq;
using Utils; // For PriorityQueue
//using WFC.Boundary; // For BoundaryBufferManager

namespace WFC.Generation
{
    public class WFCGenerator : MonoBehaviour
    {
        [Header("Generation Settings")]
        [SerializeField] private int chunkSize = 8;
        [SerializeField] private Vector3Int worldSize = new Vector3Int(2, 2, 2);
        [SerializeField] private int maxCellStates = 7;

        [Header("State Settings")]
        [SerializeField] private Material[] stateMaterials;

        // World data
        private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
        private PriorityQueue<PropagationEvent, float> propagationQueue = new PriorityQueue<PropagationEvent, float>();
        private bool[,,] adjacencyRules;

        private HierarchicalConstraintSystem hierarchicalConstraints;
        public Dictionary<Vector3Int, Chunk> GetChunks()
        {
            return chunks;
        }

        public void AddPropagationEvent(PropagationEvent evt)
        {
            propagationQueue.Enqueue(evt, evt.Priority);
        }

        // Also make sure to add this property
        public int MaxCellStates => maxCellStates;
        public int ChunkSize => chunkSize;


        // Boundary manager
        private BoundaryBufferManager boundaryManager;

        private void Awake()
        {
            InitializeWorld();
        }

        private void InitializeWorld()
        {
            // Initialize adjacency rules
            adjacencyRules = new bool[maxCellStates, maxCellStates, 6]; // 6 directions
            SetupAdjacencyRules();

            // Create chunks
            for (int x = 0; x < worldSize.x; x++)
            {
                for (int y = 0; y < worldSize.y; y++)
                {
                    for (int z = 0; z < worldSize.z; z++)
                    {
                        Vector3Int chunkPos = new Vector3Int(x, y, z);
                        Chunk chunk = new Chunk(chunkPos, chunkSize);

                        // Initialize with all possible states
                        var allStates = Enumerable.Range(0, maxCellStates);
                        chunk.InitializeCells(allStates);

                        chunks.Add(chunkPos, chunk);
                    }
                }
            }

            // Connect chunk neighbors
            ConnectChunkNeighbors();

            // Initialize boundary buffers
            InitializeBoundaryBuffers();

            // Create boundary manager
            //boundaryManager = new
            //BoundaryBufferManager(this);

            InitializeHierarchicalConstraints();
        }

        private void InitializeHierarchicalConstraints()
        {
            // Create the hierarchical constraint system
            hierarchicalConstraints = new HierarchicalConstraintSystem(chunkSize);

            // Generate default constraints based on world size
            hierarchicalConstraints.GenerateDefaultConstraints(worldSize);

            // Precompute constraints for each chunk
            foreach (var chunk in chunks.Values)
            {
                hierarchicalConstraints.PrecomputeChunkConstraints(chunk, maxCellStates);
            }
        }

        private void SetupAdjacencyRules()
        {
            // This is where you'd set up your specific adjacency rules
            // For now, we'll just set some basic rules

            // For example: empty(0) can only be next to empty
            adjacencyRules[0, 0, 0] = true;
            // Add more rules here based on your state definitions

            // Make rules symmetric
            for (int i = 0; i < maxCellStates; i++)
            {
                for (int j = 0; j < maxCellStates; j++)
                {
                    for (int dir = 0; dir < 6; dir++)
                    {
                        if (adjacencyRules[i, j, dir])
                        {
                            int oppositeDir = (dir + 3) % 6; // Opposite direction
                            adjacencyRules[j, i, oppositeDir] = true;
                        }
                    }
                }
            }
        }

        private void ConnectChunkNeighbors()
        {
            foreach (var chunkEntry in chunks)
            {
                Vector3Int pos = chunkEntry.Key;
                Chunk chunk = chunkEntry.Value;

                // Check each direction for neighbors
                foreach (Direction dir in Enum.GetValues(typeof(Direction)))
                {
                    Vector3Int offset = dir.ToVector3Int();
                    Vector3Int neighborPos = pos + offset;

                    if (chunks.TryGetValue(neighborPos, out Chunk neighbor))
                    {
                        chunk.Neighbors[dir] = neighbor;
                    }
                }
            }
        }

        private void InitializeBoundaryBuffers()
        {
            foreach (var chunk in chunks.Values)
            {
                foreach (Direction dir in Enum.GetValues(typeof(Direction)))
                {
                    if (!chunk.Neighbors.ContainsKey(dir))
                        continue;

                    Chunk neighbor = chunk.Neighbors[dir];

                    // Create buffer for this boundary
                    BoundaryBuffer buffer = new BoundaryBuffer(dir, chunk);
                    buffer.AdjacentChunk = neighbor;

                    // Get boundary cells based on direction
                    List<Cell> boundaryCells = GetBoundaryCells(chunk, dir);
                    buffer.BoundaryCells = boundaryCells;

                    // Create buffer cells (virtual cells that mirror neighbor's boundary)
                    Direction oppositeDir = dir.GetOpposite();
                    List<Cell> neighborBoundaryCells = GetBoundaryCells(neighbor, oppositeDir);

                    // Create buffer cells with same number of elements as boundary cells
                    for (int i = 0; i < boundaryCells.Count; i++)
                    {
                        Vector3Int pos = new Vector3Int(-1, -1, -1); // Invalid position for buffer cells
                        Cell bufferCell = new Cell(pos, new int[0]);
                        buffer.BufferCells.Add(bufferCell);
                    }

                    chunk.BoundaryBuffers[dir] = buffer;
                }
            }

            // Now sync buffer cells with their corresponding boundaries
            foreach (var chunk in chunks.Values)
            {
                foreach (var buffer in chunk.BoundaryBuffers.Values)
                {
                    SynchronizeBuffer(buffer);
                }
            }
        }

        private List<Cell> GetBoundaryCells(Chunk chunk, Direction direction)
        {
            List<Cell> cells = new List<Cell>();

            switch (direction)
            {
                case Direction.Left:
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            cells.Add(chunk.GetCell(0, y, z));
                        }
                    }
                    break;

                case Direction.Right:
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            cells.Add(chunk.GetCell(chunk.Size - 1, y, z));
                        }
                    }
                    break;

                case Direction.Down:
                    for (int x = 0; x < chunk.Size; x++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            cells.Add(chunk.GetCell(x, 0, z));
                        }
                    }
                    break;

                case Direction.Up:
                    for (int x = 0; x < chunk.Size; x++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            cells.Add(chunk.GetCell(x, chunk.Size - 1, z));
                        }
                    }
                    break;

                case Direction.Back:
                    for (int x = 0; x < chunk.Size; x++)
                    {
                        for (int y = 0; y < chunk.Size; y++)
                        {
                            cells.Add(chunk.GetCell(x, y, 0));
                        }
                    }
                    break;

                case Direction.Forward:
                    for (int x = 0; x < chunk.Size; x++)
                    {
                        for (int y = 0; y < chunk.Size; y++)
                        {
                            cells.Add(chunk.GetCell(x, y, chunk.Size - 1));
                        }
                    }
                    break;
            }

            return cells;
        }

        private void SynchronizeBuffer(BoundaryBuffer buffer)
        {
            if (buffer.AdjacentChunk == null)
                return;

            Direction oppositeDir = buffer.Direction.GetOpposite();
            BoundaryBuffer adjacentBuffer = buffer.AdjacentChunk.BoundaryBuffers[oppositeDir];

            for (int i = 0; i < buffer.BoundaryCells.Count; i++)
            {
                // Update buffer cells to reflect adjacent boundary cells
                HashSet<int> adjacentStates = new HashSet<int>(adjacentBuffer.BoundaryCells[i].PossibleStates);
                buffer.BufferCells[i].SetPossibleStates(adjacentStates);

                // And vice versa
                HashSet<int> localStates = new HashSet<int>(buffer.BoundaryCells[i].PossibleStates);
                adjacentBuffer.BufferCells[i].SetPossibleStates(localStates);
            }
        }

        public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
        {
            return adjacencyRules[stateA, stateB, (int)direction];
        }

        // Other methods for WFC generation will go here
        // Such as Collapse(), PropagateConstraints(), etc.

        private bool FindCellToCollapse()
        {
            // Find the cell with lowest entropy
            Cell cellToCollapse = null;
            Chunk chunkWithCell = null;
            int lowestEntropy = int.MaxValue;

            foreach (var chunkEntry in chunks)
            {
                var chunk = chunkEntry.Value;

                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            Cell cell = chunk.GetCell(x, y, z);

                            // Skip already collapsed cells
                            if (cell.CollapsedState.HasValue)
                                continue;

                            // Skip cells with only one state (will be collapsed in propagation)
                            if (cell.Entropy == 1)
                                continue;

                            // Apply hierarchical constraints to cell's entropy calculation
                            Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(cell, chunk, maxCellStates);

                            // Calculate effective entropy based on biases
                            int effectiveEntropy = cell.Entropy;

                            // If we have strong biases, reduce effective entropy
                            if (biases.Count > 0)
                            {
                                float maxBias = biases.Values.Max(Mathf.Abs);

                                // Strong bias reduces effective entropy
                                if (maxBias > 0.5f)
                                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.7f);
                                else if (maxBias > 0.3f)
                                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.85f);
                            }

                            // Check if this cell has lower effective entropy
                            if (effectiveEntropy < lowestEntropy)
                            {
                                lowestEntropy = effectiveEntropy;
                                cellToCollapse = cell;
                                chunkWithCell = chunk;
                            }
                        }
                    }
                }
            }

            // If found a cell, collapse it
            if (cellToCollapse != null && chunkWithCell != null)
            {
                // Get constraint biases
                Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(
                    cellToCollapse, chunkWithCell, maxCellStates);

                // Choose a state based on biases
                int[] possibleStates = cellToCollapse.PossibleStates.ToArray();
                int chosenState;

                if (biases.Count > 0 && possibleStates.Length > 1)
                {
                    // Create weighted selection based on biases
                    float[] weights = new float[possibleStates.Length];
                    float totalWeight = 0;

                    for (int i = 0; i < possibleStates.Length; i++)
                    {
                        int state = possibleStates[i];
                        weights[i] = 1.0f; // Base weight

                        // Apply bias if available
                        if (biases.TryGetValue(state, out float bias))
                        {
                            // Convert bias (-1 to 1) to weight multiplier (0.25 to 4)
                            float multiplier = Mathf.Pow(2, bias * 2);
                            weights[i] *= multiplier;
                        }

                        // Ensure weight is positive
                        weights[i] = Mathf.Max(0.1f, weights[i]);
                        totalWeight += weights[i];
                    }

                    // Random selection based on weights
                    float randomValue = UnityEngine.Random.Range(0, totalWeight);
                    float cumulativeWeight = 0;

                    chosenState = possibleStates[0]; // Default to first state

                    for (int i = 0; i < possibleStates.Length; i++)
                    {
                        cumulativeWeight += weights[i];
                        if (randomValue <= cumulativeWeight)
                        {
                            chosenState = possibleStates[i];
                            break;
                        }
                    }
                }
                else
                {
                    // No significant biases, use standard random selection
                    chosenState = possibleStates[UnityEngine.Random.Range(0, possibleStates.Length)];
                }

                // Collapse to chosen state
                cellToCollapse.Collapse(chosenState);
                PropagationEvent evt = new PropagationEvent(
                cellToCollapse,
                chunkWithCell,
                new HashSet<int>(cellToCollapse.PossibleStates),
                new HashSet<int> { chosenState },
                cellToCollapse.IsBoundary
                );

                // Add to queue
                AddPropagationEvent(evt);
                return true;
            }

            return false;
        }

        // Add this method to apply constraints to a cell
        private void ApplyHierarchicalConstraints(Cell cell, Chunk chunk)
        {
            hierarchicalConstraints.ApplyConstraintsToCell(cell, chunk, maxCellStates);
        }

        public HierarchicalConstraintSystem GetHierarchicalConstraintSystem()
        {
            return hierarchicalConstraints;
        }
    }
}