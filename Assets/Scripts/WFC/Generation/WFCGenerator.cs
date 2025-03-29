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
    public class WFCGenerator : MonoBehaviour, WFC.Boundary.IChunkProvider, WFC.Boundary.IWFCAlgorithm
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

        private void Update()
        {
            // Process propagation queue
            ProcessPropagationQueue();

            // Check if we need to collapse more cells
            if (propagationQueue.Count == 0)
            {
                CollapseNextCell();
            }
        }

        private void ProcessPropagationQueue()
        {
            // Process a fixed number of events per frame
            int maxEventsPerFrame = 20; // This could be a configuration setting
            int processedEvents = 0;

            while (propagationQueue.Count > 0 && processedEvents < maxEventsPerFrame)
            {
                PropagationEvent evt = propagationQueue.Dequeue();
                ProcessPropagationEvent(evt);
                processedEvents++;
            }
        }

        private void ProcessPropagationEvent(PropagationEvent evt)
        {
            // Skip if the event is invalid
            if (evt.Cell == null || evt.Chunk == null)
                return;

            Cell cell = evt.Cell;
            Chunk chunk = evt.Chunk;

            // Apply constraints from hierarchy
            ApplyConstraintsToCell(cell, chunk);

            // Skip if not collapsed
            if (!cell.CollapsedState.HasValue)
                return;

            // Find neighbors and propagate constraints
            PropagateToNeighbors(cell, chunk);

            // Update boundary buffers if needed
            if (cell.IsBoundary && cell.BoundaryDirection.HasValue)
            {
                boundaryManager.UpdateBuffersAfterCollapse(cell, chunk);
            }
        }

        private void PropagateToNeighbors(Cell cell, Chunk chunk)
        {
            // Find cell position in chunk
            Vector3Int? cellPos = FindCellPosition(cell, chunk);
            if (!cellPos.HasValue)
                return;

            int state = cell.CollapsedState.Value;

            // Check each direction
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                // Calculate neighbor position
                Vector3Int offset = dir.ToVector3Int();
                Vector3Int neighborPos = cellPos.Value + offset;

                // Check if neighbor is within chunk
                if (IsPositionInChunk(neighborPos, chunk.Size))
                {
                    // Get neighbor cell and apply constraint
                    Cell neighbor = chunk.GetCell(neighborPos.x, neighborPos.y, neighborPos.z);
                    ApplyConstraint(neighbor, state, dir, chunk);
                }
                else
                {
                    // Handle neighbor in adjacent chunk (through boundary system)
                    // This is already handled by boundaryManager.UpdateBuffersAfterCollapse
                }
            }
        }

        private Vector3Int? FindCellPosition(Cell cell, Chunk chunk)
        {
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        if (chunk.GetCell(x, y, z) == cell)
                        {
                            return new Vector3Int(x, y, z);
                        }
                    }
                }
            }

            return null;
        }

        private bool IsPositionInChunk(Vector3Int pos, int chunkSize)
        {
            return pos.x >= 0 && pos.x < chunkSize &&
                   pos.y >= 0 && pos.y < chunkSize &&
                   pos.z >= 0 && pos.z < chunkSize;
        }

        private void ApplyConstraint(Cell cell, int constraintState, Direction direction, Chunk chunk)
        {
            // Skip if already collapsed
            if (cell.CollapsedState.HasValue)
                return;

            // Store old states
            HashSet<int> oldStates = new HashSet<int>(cell.PossibleStates);

            // Find compatible states
            HashSet<int> compatibleStates = new HashSet<int>();

            foreach (int state in cell.PossibleStates)
            {
                if (AreStatesCompatible(state, constraintState, direction))
                {
                    compatibleStates.Add(state);
                }
            }

            // Update possible states
            if (compatibleStates.Count > 0 && !compatibleStates.SetEquals(oldStates))
            {
                cell.SetPossibleStates(compatibleStates);

                // Create propagation event
                PropagationEvent evt = new PropagationEvent(
                    cell,
                    chunk,
                    oldStates,
                    compatibleStates,
                    cell.IsBoundary
                );

                AddPropagationEvent(evt);
            }
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
            // Initialize all to false
            for (int i = 0; i < MaxCellStates; i++)
            {
                for (int j = 0; j < MaxCellStates; j++)
                {
                    for (int d = 0; d < 6; d++)
                    {
                        adjacencyRules[i, j, d] = false;
                    }
                }
            }

            // Define rules based on the configuration or hardcoded rules
            // For example: empty(0) can only be next to empty
            SetAdjacentAll(0, 0, true);

            // Ground(1) can be next to most things
            SetAdjacentAll(1, 1, true);
            SetAdjacentAll(1, 2, true);
            SetAdjacentAll(1, 4, true);

            // Grass(2) adjacent rules
            SetAdjacentAll(2, 2, true);
            SetAdjacentAll(2, 3, true);
            SetAdjacentAll(2, 5, true);
            SetAdjacentAll(2, 6, true);

            // Water(3) adjacent rules
            SetAdjacentAll(3, 3, true);
            SetAdjacentAll(3, 5, true);

            // Rock(4) adjacent rules
            SetAdjacentAll(4, 4, true);

            // Sand(5) adjacent rules
            SetAdjacentAll(5, 5, true);

            // Tree(6) adjacent rules
            SetAdjacentAll(6, 6, true);
        }

        private void SetAdjacentAll(int stateA, int stateB, bool canBeAdjacent)
        {
            for (int d = 0; d < 6; d++)
            {
                adjacencyRules[stateA, stateB, d] = canBeAdjacent;
                adjacencyRules[stateB, stateA, d] = canBeAdjacent;
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

        private bool CollapseNextCell()
        {
            // Find the cell with lowest entropy
            Cell cellToCollapse = null;
            Chunk chunkWithCell = null;
            int lowestEntropy = int.MaxValue;
            float closestDistance = float.MaxValue;  // Initialize to max value

            // Get viewer position - either from a reference or camera
            Vector3 viewerPosition = Camera.main != null ?
                Camera.main.transform.position : Vector3.zero;

            // Option: Use a prioritized area if no camera
            // Vector3 priorityCenter = new Vector3(worldSize.x * chunkSize / 2, 0, worldSize.z * chunkSize / 2);
            // Vector3 viewerPosition = priorityCenter;

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
                            if (cell.PossibleStates.Count <= 1)
                                continue;

                            // Calculate effective entropy based on constraints
                            int effectiveEntropy = CalculateEffectiveEntropy(cell, chunk);

                            // First priority: Lowest entropy
                            if (effectiveEntropy < lowestEntropy)
                            {
                                lowestEntropy = effectiveEntropy;
                                cellToCollapse = cell;
                                chunkWithCell = chunk;

                                // Recalculate distance for the new candidate
                                Vector3 cellWorldPos = CalculateWorldPosition(cell.Position, chunk.Position);
                                closestDistance = Vector3.Distance(cellWorldPos, viewerPosition);
                            }
                            // Tie-breaking: Same entropy but closer to viewer
                            else if (effectiveEntropy == lowestEntropy)
                            {
                                Vector3 cellWorldPos = CalculateWorldPosition(cell.Position, chunk.Position);
                                float distanceToViewer = Vector3.Distance(cellWorldPos, viewerPosition);

                                if (distanceToViewer < closestDistance)
                                {
                                    cellToCollapse = cell;
                                    chunkWithCell = chunk;
                                    closestDistance = distanceToViewer;
                                }
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

        // Helper method to calculate world position of the cell
        private Vector3 CalculateWorldPosition(Vector3Int localPosition, Vector3Int chunkPosition)
        {
            return new Vector3(
                chunkPosition.x * ChunkSize + localPosition.x,
                chunkPosition.y * ChunkSize + localPosition.y,
                chunkPosition.z * ChunkSize + localPosition.z
            );
        }

        // Helper to calculate effective entropy with constraints
        private int CalculateEffectiveEntropy(Cell cell, Chunk chunk)
        {
            // Get constraint biases
            Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(
                cell, chunk, MaxCellStates);

            // Calculate effective entropy based on biases
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

            return effectiveEntropy;
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

        public bool ValidateAlgorithmState()
        {
            bool isValid = true;

            // Check all chunks for constraint violations
            foreach (var chunk in chunks.Values)
            {
                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            Cell cell = chunk.GetCell(x, y, z);
                            if (cell.CollapsedState.HasValue)
                            {
                                // Validate this cell doesn't violate constraints with neighbors
                                if (!ValidateCellConstraints(cell, chunk, x, y, z))
                                {
                                    isValid = false;
                                    Debug.LogError($"Constraint violation at {chunk.Position}, local: {x},{y},{z}");
                                }
                            }
                        }
                    }
                }
            }

            return isValid;
        }

        private bool ValidateCellConstraints(Cell cell, Chunk chunk, int x, int y, int z)
        {
            if (!cell.CollapsedState.HasValue)
                return true;

            int state = cell.CollapsedState.Value;

            // Check all neighboring cells
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                Vector3Int offset = dir.ToVector3Int();
                Vector3Int neighborPos = new Vector3Int(x, y, z) + offset;

                // Skip if outside chunk
                if (!IsPositionInChunk(neighborPos, chunk.Size))
                    continue;

                Cell neighbor = chunk.GetCell(neighborPos.x, neighborPos.y, neighborPos.z);

                // Skip if neighbor not collapsed
                if (!neighbor.CollapsedState.HasValue)
                    continue;

                // Check compatibility
                if (!AreStatesCompatible(state, neighbor.CollapsedState.Value, dir))
                {
                    return false;
                }
            }

            return true;
        }

        public void ResetGeneration()
        {
            // Clear the propagation queue
            propagationQueue = new PriorityQueue<PropagationEvent, float>();

            // Reset all chunks and cells
            foreach (var chunk in chunks.Values)
            {
                chunk.IsFullyCollapsed = false;

                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            Cell cell = chunk.GetCell(x, y, z);
                            cell.SetPossibleStates(Enumerable.Range(0, MaxCellStates).ToHashSet());
                        }
                    }
                }

                // Reset boundary buffers
                foreach (var buffer in chunk.BoundaryBuffers.Values)
                {
                    foreach (var bufferCell in buffer.BufferCells)
                    {
                        bufferCell.SetPossibleStates(Enumerable.Range(0, MaxCellStates).ToHashSet());
                    }
                }
            }

            // Re-initialize hierarchical constraints
            InitializeHierarchicalConstraints();

            // Synchronize all buffers
            foreach (var chunk in chunks.Values)
            {
                foreach (var buffer in chunk.BoundaryBuffers.Values)
                {
                    SynchronizeBuffer(buffer);
                }
            }

            Debug.Log("WFC generation reset");
        }
    }
}