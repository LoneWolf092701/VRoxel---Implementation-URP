// Assets/Scripts/WFC/Generation/WFCGenerator.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Boundary;
using System.Linq;
using Utils;
using WFC.Configuration;

namespace WFC.Generation
    {
    /// <summary>
    /// Updated WFCGenerator class that properly integrates with the configuration system
    /// </summary>
    public class WFCGenerator : MonoBehaviour, WFC.Boundary.IWFCAlgorithm

    {
        [Header("Configuration")]
            [Tooltip("Override the global configuration with a specific asset")]
            [SerializeField] private WFCConfiguration configOverride;

            [Header("State Settings")]
            [SerializeField] private Material[] stateMaterials;

            // World data
            private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
            private PriorityQueue<PropagationEvent, float> propagationQueue = new PriorityQueue<PropagationEvent, float>();
            private bool[,,] adjacencyRules;

            private HierarchicalConstraintSystem hierarchicalConstraints;

            // Boundary manager
            private BoundaryBufferManager boundaryManager;

            // Cache for config access
            private WFCConfiguration activeConfig;

            // Properties that now use the configuration
            public int MaxCellStates => activeConfig.World.maxStates;
            public int ChunkSize => activeConfig.World.chunkSize;
            public Vector3Int WorldSize => new Vector3Int(
                activeConfig.World.worldSizeX,
                activeConfig.World.worldSizeY,
                activeConfig.World.worldSizeZ
            );

            public Dictionary<Vector3Int, Chunk> GetChunks()
            {
                return chunks;
            }

            public void AddPropagationEvent(PropagationEvent evt)
            {
                propagationQueue.Enqueue(evt, evt.Priority);
            }

            private void Awake()
            {
                // Get the configuration - use override if specified, otherwise use global config
                activeConfig = configOverride != null ? configOverride : WFCConfigManager.Config;

                if (activeConfig == null)
                {
                    Debug.LogError("WFCGenerator: No configuration available. Please assign a WFCConfiguration asset.");
                    enabled = false;
                    return;
                }

                // Validate configuration
                if (!activeConfig.Validate())
                {
                    Debug.LogWarning("WFCGenerator: Using configuration with validation issues.");
                }

                InitializeWorld();
            }

            private void InitializeWorld()
            {
                // Initialize adjacency rules
                adjacencyRules = new bool[MaxCellStates, MaxCellStates, 6]; // 6 directions
                SetupAdjacencyRules();

                // Create chunks based on configuration
                for (int x = 0; x < WorldSize.x; x++)
                {
                    for (int y = 0; y < WorldSize.y; y++)
                    {
                        for (int z = 0; z < WorldSize.z; z++)
                        {
                            Vector3Int chunkPos = new Vector3Int(x, y, z);
                            Chunk chunk = new Chunk(chunkPos, ChunkSize);

                            // Initialize with all possible states
                            var allStates = Enumerable.Range(0, MaxCellStates);
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
                boundaryManager = new BoundaryBufferManager((IWFCAlgorithm)this);


                InitializeHierarchicalConstraints();
            }

            private void InitializeHierarchicalConstraints()
            {
                // Create the hierarchical constraint system
                hierarchicalConstraints = new HierarchicalConstraintSystem(ChunkSize);

                // Generate default constraints based on world size
                hierarchicalConstraints.GenerateDefaultConstraints(WorldSize);

                // Precompute constraints for each chunk
                foreach (var chunk in chunks.Values)
                {
                    hierarchicalConstraints.PrecomputeChunkConstraints(chunk, MaxCellStates);
                }
            }

            private void SetupAdjacencyRules()
            {
                // This would remain largely the same, but could reference configuration for specific rules
                // For this example, we'll use a simplified version

                // For example: empty(0) can only be next to empty
                adjacencyRules[0, 0, 0] = true;
                // ... other rules ...

                // Make rules symmetric
                for (int i = 0; i < MaxCellStates; i++)
                {
                    for (int j = 0; j < MaxCellStates; j++)
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
            private void ApplyConstraintsToCell(Cell cell, Chunk chunk)
            {
                // Skip if constraints are disabled
                if (!activeConfig.Algorithm.useConstraints || hierarchicalConstraints == null)
                    return;

                // Skip if cell is already collapsed
                if (cell.CollapsedState.HasValue)
                    return;

                // Get constraint biases, scaled by chunk's LOD constraint influence
                Dictionary<int, float> biases = new Dictionary<int, float>();
                var rawBiases = hierarchicalConstraints.CalculateConstraintInfluence(cell, chunk, MaxCellStates);

                foreach (var bias in rawBiases)
                {
                    // Scale the bias by the chunk's LOD constraint influence factor
                    biases[bias.Key] = bias.Value * chunk.ConstraintInfluence;
                }

                // If no significant biases, return without modifying cell
                if (!biases.Values.Any(v => Mathf.Abs(v) > 0.01f))
                    return;

                // Get current possible states
                HashSet<int> currentStates = new HashSet<int>(cell.PossibleStates);

                // Calculate adjustment probability for each state based on biases
                Dictionary<int, float> stateWeights = new Dictionary<int, float>();

                foreach (int state in currentStates)
                {
                    // Base weight is 1.0
                    float weight = 1.0f;

                    // Apply bias adjustment if exists
                    if (biases.TryGetValue(state, out float bias))
                    {
                        // Positive bias increases weight (more likely)
                        // Negative bias decreases weight (less likely)
                        weight *= (1.0f + bias);
                    }

                    // Ensure weight is positive
                    weight = Mathf.Max(0.01f, weight);
                    stateWeights[state] = weight;
                }

                // If cell has more than one possible state, consider constraint-based collapse
                if (currentStates.Count > 1)
                {
                    // Get total weight
                    float totalWeight = stateWeights.Values.Sum();

                    // Calculate collapse threshold based on strongest bias and LOD
                    float maxBias = biases.Values.Max(Mathf.Abs);
                    float collapseThreshold = Mathf.Lerp(0.9f, 0.5f, maxBias * chunk.ConstraintInfluence);

                    // Find if we have a highly dominant state
                    foreach (var state in stateWeights.Keys.ToList())
                    {
                        float normalizedWeight = stateWeights[state] / totalWeight;

                        // If one state is strongly preferred, collapse to it
                        if (normalizedWeight > collapseThreshold)
                        {
                            cell.Collapse(state);
                            return; // Cell is now collapsed, we're done
                        }
                    }
                }

                // If we didn't collapse, just adjust the cell's entropy for future WFC decisions
                // This can be done by setting a custom entropy value beyond just the count
                // But our current Cell class doesn't support this directly
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
                int size = chunk.Size;

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
                                Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(cell, chunk, MaxCellStates);

                                // Calculate effective entropy based on biases using the configuration weights
                                int effectiveEntropy = cell.Entropy;

                                // If we have strong biases, reduce effective entropy
                                if (biases.Count > 0)
                                {
                                    float maxBias = biases.Values.Max(Mathf.Abs);

                                    // Use configuration weights to determine entropy modification
                                    float constraintWeight = activeConfig.Algorithm.constraintWeight;
                                    float boundaryWeight = activeConfig.Algorithm.boundaryCoherenceWeight;

                                    // Apply weights to bias
                                    float adjustedBias = maxBias * constraintWeight;

                                    // If cell is on boundary, apply boundary weight
                                    if (cell.IsBoundary)
                                    {
                                        adjustedBias *= boundaryWeight;
                                    }

                                    // Strong bias reduces effective entropy
                                    if (adjustedBias > 0.7f)
                                        effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.7f);
                                    else if (adjustedBias > 0.3f)
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
                        cellToCollapse, chunkWithCell, MaxCellStates);

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
                if (activeConfig.Algorithm.useConstraints)
                {
                    hierarchicalConstraints.ApplyConstraintsToCell(cell, chunk, MaxCellStates);
                }
            }

            public HierarchicalConstraintSystem GetHierarchicalConstraintSystem()
            {
                return hierarchicalConstraints;
            }

            // Runtime configuration update method
            public void UpdateConfiguration(WFCConfiguration newConfig)
            {
                if (newConfig == null)
                {
                    Debug.LogError("WFCGenerator: Cannot update to null configuration.");
                    return;
                }

                // Store old values for comparison
                int oldChunkSize = ChunkSize;
                int oldMaxStates = MaxCellStates;
                Vector3Int oldWorldSize = WorldSize;

                // Update configuration
                configOverride = newConfig;
                activeConfig = newConfig;

                // Handle changes that require regeneration
                if (oldChunkSize != ChunkSize ||
                    oldMaxStates != MaxCellStates ||
                    oldWorldSize != WorldSize)
                {
                    Debug.LogWarning("WFCGenerator: Configuration change requires world regeneration. " +
                                    "Consider reinitializing the world.");

                    // Could offer to automatically reinitialize here
                }
            }
        }
    }