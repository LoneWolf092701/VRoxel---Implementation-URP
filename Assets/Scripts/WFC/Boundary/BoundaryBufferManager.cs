/*
 * BoundaryBufferManager.cs
 * -----------------------------
 * Manages the boundaries between chunks to ensure seamless terrain generation.
 * 
 * A key challenge in chunk-based procedural generation is maintaining consistency
 * across chunk boundaries. This manager implements a buffer system that:
 * 
 * 1. Creates "buffer zones" at chunk boundaries that mirror adjacent chunks
 * 2. Propagates constraints bidirectionally between chunks when cells collapse
 * 3. Resolves conflicts to maintain coherence across boundaries
 * 4. Prevents infinite recursion in constraint propagation
 * 
 * The buffer system uses a virtual cell approach where each boundary has:
 * - Boundary cells (real cells at the edge of a chunk)
 * - Buffer cells (virtual cells that represent the adjacent chunk's boundary)
 * 
 * This creates an overlap between chunks that allows the WFC algorithm to
 * operate seamlessly across chunk boundaries without needing to process
 * the entire world at once.
 */

using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;

namespace WFC.Boundary
{
    /// <summary>
    /// Interface for checking compatibility between states across boundaries
    /// </summary>
    public interface IBoundaryCompatibilityChecker
    {
        bool AreStatesCompatible(int stateA, int stateB, Direction direction);
    }

    /// <summary>
    /// Interface for propagating constraint events
    /// </summary>
    public interface IPropagationHandler
    {
        void AddPropagationEvent(PropagationEvent evt);
    }

    /// <summary>
    /// Interface for accessing chunks
    /// </summary>
    public interface IChunkProvider
    {
        Dictionary<Vector3Int, Chunk> GetChunks();
    }

    /// <summary>
    /// Combined interface for backward compatibility
    /// </summary>
    public interface IWFCAlgorithm : IBoundaryCompatibilityChecker, IPropagationHandler, IChunkProvider
    {
        // Empty - combines the other interfaces
    }

    /// <summary>
    /// Manages boundary buffers and coherence between chunks
    /// </summary>
    public class BoundaryBufferManager
    {
        private IBoundaryCompatibilityChecker compatibilityChecker;
        private IPropagationHandler propagationHandler;
        private IChunkProvider chunkProvider;
        private Dictionary<Vector3Int, int> propagationCount = new Dictionary<Vector3Int, int>();
        private const int MaxPropagationCount = 10;


        /// <summary>
        /// Constructor using individual interfaces
        /// </summary>
        public BoundaryBufferManager(
            IBoundaryCompatibilityChecker compatibilityChecker,
            IPropagationHandler propagationHandler,
            IChunkProvider chunkProvider)
        {
            this.compatibilityChecker = compatibilityChecker;
            this.propagationHandler = propagationHandler;
            this.chunkProvider = chunkProvider;
        }

        /// <summary>
        /// Constructor using combined interface (for backward compatibility)
        /// </summary>
        public BoundaryBufferManager(IWFCAlgorithm algorithm)
        {
            this.compatibilityChecker = algorithm;
            this.propagationHandler = algorithm;
            this.chunkProvider = algorithm;
        }

        /*
         * UpdateBuffersAfterCollapse
         * -----------------------------
         * Updates boundary buffers when a cell collapses at a chunk boundary.
         * 
         * This is a critical method for maintaining coherence between chunks that:
         * 1. Propagates collapsed state information from one chunk to adjacent chunks
         * 2. Updates buffer cells to reflect changed boundary cells
         * 3. Applies constraints across chunk boundaries
         * 4. Creates propagation events to continue the WFC algorithm in adjacent chunks
         * 
         * The method implements bidirectional propagation to ensure that constraints
         * flow correctly in both directions across chunk boundaries. It also includes
         * a propagation limit to prevent infinite loops in the constraint system.
         * 
         * This boundary system is what enables the generation of a seamless, infinite
         * world without requiring the entire world to be processed simultaneously.
         */
        public void UpdateBuffersAfterCollapse(Cell cell, Chunk chunk)
        {
            // If cell is not a boundary cell, nothing to do
            if (!cell.IsBoundary || !cell.BoundaryDirection.HasValue)
                return;

            // Check propagation limit
            Vector3Int chunkPos = chunk.Position;
            if (!propagationCount.ContainsKey(chunkPos))
                propagationCount[chunkPos] = 0;

            propagationCount[chunkPos]++;
            if (propagationCount[chunkPos] > MaxPropagationCount)
            {
                Debug.LogWarning($"Maximum propagation count reached for chunk {chunkPos}");
                propagationCount[chunkPos] = 0;
                return;
            }

            Direction direction = cell.BoundaryDirection.Value;

            // No buffer or adjacent chunk for this direction
            if (!chunk.BoundaryBuffers.TryGetValue(direction, out BoundaryBuffer buffer) ||
                buffer.AdjacentChunk == null)
                return;

            // Find this cell in the boundary array
            int index = buffer.BoundaryCells.IndexOf(cell);
            if (index == -1)
                return;

            // Get the opposite direction
            Direction oppositeDir = direction.GetOpposite();

            // Get the corresponding buffer in adjacent chunk
            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return;

            // PHASE 1: Forward propagation (this chunk to adjacent chunk)
            if (index < adjacentBuffer.BufferCells.Count)
            {
                // 1. Update the buffer cell in the adjacent chunk to match this cell
                Cell adjacentBufferCell = adjacentBuffer.BufferCells[index];
                bool adjacentBufferChanged = false;

                if (cell.CollapsedState.HasValue)
                {
                    // Set buffer cell to match this cell's state
                    HashSet<int> oldStates = new HashSet<int>(adjacentBufferCell.PossibleStates);
                    adjacentBufferChanged = adjacentBufferCell.SetPossibleStates(new HashSet<int> { cell.CollapsedState.Value });

                    // 2. Apply constraints to the corresponding boundary cell in adjacent chunk
                    Cell adjacentBoundaryCell = adjacentBuffer.BoundaryCells[index];
                    bool adjacentBoundaryChanged = ApplyBufferConstraints(adjacentBoundaryCell, adjacentBufferCell, oppositeDir);

                    // 3. If adjacent boundary cell changed, create a propagation event for adjacent chunk
                    if (adjacentBoundaryChanged)
                    {
                        var oldStatesAdj = new HashSet<int>(); // We don't have accurate old states, but it's OK for most implementations
                        var propagationEvent = new PropagationEvent(
                            adjacentBoundaryCell,
                            buffer.AdjacentChunk,
                            oldStatesAdj,
                            adjacentBoundaryCell.PossibleStates,
                            true // is boundary event
                        );

                        propagationHandler.AddPropagationEvent(propagationEvent);
                    }
                }
                else
                {
                    // If cell isn't collapsed, update buffer with current possible states
                    HashSet<int> oldStates = new HashSet<int>(adjacentBufferCell.PossibleStates);
                    adjacentBufferChanged = adjacentBufferCell.SetPossibleStates(new HashSet<int>(cell.PossibleStates));

                    // Apply constraints if buffer cell changed
                    if (adjacentBufferChanged)
                    {
                        Cell adjacentBoundaryCell = adjacentBuffer.BoundaryCells[index];
                        bool adjacentBoundaryChanged = ApplyBufferConstraints(adjacentBoundaryCell, adjacentBufferCell, oppositeDir);

                        if (adjacentBoundaryChanged)
                        {
                            var propagationEvent = new PropagationEvent(
                                adjacentBoundaryCell,
                                buffer.AdjacentChunk,
                                oldStates,
                                adjacentBoundaryCell.PossibleStates,
                                true
                            );

                            propagationHandler.AddPropagationEvent(propagationEvent);
                        }
                    }
                }
            }

            // PHASE 2: Backward propagation (adjacent chunk back to this chunk)
            // Only needed if we're doing recursive propagation at boundaries
            if (index < buffer.BufferCells.Count)
            {
                // Get the adjacent boundary cell
                Cell adjacentBoundaryCell = adjacentBuffer.BoundaryCells[index];

                // If adjacent boundary cell is collapsed or has limited states, reflect back to this chunk
                if (adjacentBoundaryCell.PossibleStates.Count < cell.PossibleStates.Count)
                {
                    // Update this chunk's buffer cell based on adjacent boundary cell
                    Cell bufferCell = buffer.BufferCells[index];
                    HashSet<int> oldBufferStates = new HashSet<int>(bufferCell.PossibleStates);

                    bool bufferChanged = bufferCell.SetPossibleStates(
                        new HashSet<int>(adjacentBoundaryCell.PossibleStates));

                    // If buffer changed, check if we need to update the original cell
                    if (bufferChanged && !cell.CollapsedState.HasValue)
                    {
                        // Get compatible states between cell and updated buffer
                        HashSet<int> compatibleStates = new HashSet<int>();
                        foreach (int cellState in cell.PossibleStates)
                        {
                            foreach (int bufferState in bufferCell.PossibleStates)
                            {
                                if (compatibilityChecker.AreStatesCompatible(cellState, bufferState, direction))
                                {
                                    compatibleStates.Add(cellState);
                                    break;
                                }
                            }
                        }

                        // If compatible states are different than current states, update and propagate
                        if (compatibleStates.Count > 0 && !compatibleStates.SetEquals(cell.PossibleStates))
                        {
                            HashSet<int> oldCellStates = new HashSet<int>(cell.PossibleStates);
                            bool cellChanged = cell.SetPossibleStates(compatibleStates);

                            if (cellChanged)
                            {
                                var propagationEvent = new PropagationEvent(
                                    cell,
                                    chunk,
                                    oldCellStates,
                                    compatibleStates,
                                    true
                                );

                                propagationHandler.AddPropagationEvent(propagationEvent);
                            }
                        }
                    }
                }
            }
        }

        /*
         * ApplyBufferConstraints
         * ----------------------------------------------------------------------------
         * Applies constraints from buffer cells to boundary cells.
         * 
         * This is the core of cross-chunk constraint propagation:
         * 1. For each state in the boundary cell:
         *    a. Checks compatibility with each state in the buffer cell
         *    b. Adds compatible states to a new possible states set
         * 2. Updates the boundary cell with the filtered possible states
         * 3. Handles constraint conflicts with appropriate error handling
         * 4. Limits recursion depth to prevent infinite propagation loops
         * 
         * The function ensures that cells at chunk boundaries have states
         * that are compatible with their neighbors in adjacent chunks,
         * maintaining consistency across the entire world.
         * 
         * Parameters:
         * - boundaryCell: Cell at the edge of a chunk
         * - bufferCell: Virtual cell representing adjacent chunk's boundary
         * - direction: Direction from boundary to buffer
         * - depth: Current recursion depth (preventing infinite loops)
         * 
         * Returns: true if boundary cell changed, false otherwise
         */
        private bool ApplyBufferConstraints(Cell boundaryCell, Cell bufferCell, Direction direction, int depth = 0)
        {
            // To prevent infinite recursion
            if (depth > 10) return false;

            HashSet<int> newPossibleStates = new HashSet<int>();

            // Keep only states compatible with buffer
            foreach (int boundaryState in boundaryCell.PossibleStates)
            {
                foreach (int bufferState in bufferCell.PossibleStates)
                {
                    // Check if these states can be adjacent
                    if (compatibilityChecker.AreStatesCompatible(boundaryState, bufferState, direction))
                    {
                        newPossibleStates.Add(boundaryState);
                        break;
                    }
                }
            }

            // Update boundary cell if we have valid states
            if (newPossibleStates.Count > 0)
            {
                // Store original states to check if they changed
                HashSet<int> oldStates = new HashSet<int>(boundaryCell.PossibleStates);

                // Update the cell - SetPossibleStates returns whether the states actually changed
                bool changed = boundaryCell.SetPossibleStates(newPossibleStates);

                // Return whether states changed
                return changed;
            }
            else if (boundaryCell.PossibleStates.Count > 0)
            {
                Debug.LogWarning($"Boundary constraint conflict: No compatible states between boundary and buffer");

                return true;
            }

            // No change was made
            return false;
        }

        // to check boundary consistancy
        public bool ValidateBoundaryConsistency(Chunk chunk)
        {
            bool isConsistent = true;

            foreach (var buffer in chunk.BoundaryBuffers.Values)
            {
                if (!ValidateBoundary(buffer))
                {
                    isConsistent = false;
                    Debug.LogWarning($"Boundary inconsistency detected at {chunk.Position}, direction {buffer.Direction}");
                }
            }

            return isConsistent;
        }

        /// <summary>
        /// Synchronizes all buffers across all chunks
        /// </summary>
        public void SynchronizeAllBuffers()
        {
            foreach (var chunk in chunkProvider.GetChunks().Values)
            {
                foreach (var buffer in chunk.BoundaryBuffers.Values)
                {
                    SynchronizeBuffer(buffer);
                }
            }
        }

        /// <summary>
        /// Synchronizes a single buffer with its adjacent chunk
        /// </summary>
        public void SynchronizeBuffer(BoundaryBuffer buffer)
        {
            if (buffer.AdjacentChunk == null)
                return;

            Direction oppositeDir = buffer.Direction.GetOpposite();

            // Get adjacent buffer
            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return;

            // For each cell at the boundary
            for (int i = 0; i < buffer.BoundaryCells.Count; i++)
            {
                // Update buffer cell to reflect adjacent boundary cell
                HashSet<int> adjacentStates = new HashSet<int>(adjacentBuffer.BoundaryCells[i].PossibleStates);
                buffer.BufferCells[i].SetPossibleStates(adjacentStates);

                // Apply buffer constraints to boundary cell
                ApplyBufferConstraints(buffer.BoundaryCells[i], buffer.BufferCells[i], buffer.Direction);

                // If this causes a collapse, add to propagation queue
                if (buffer.BoundaryCells[i].PossibleStates.Count == 1 &&
                    buffer.BoundaryCells[i].CollapsedState.HasValue)
                {
                    var propagationEvent = new PropagationEvent(
                        buffer.BoundaryCells[i],
                        buffer.OwnerChunk,
                        new HashSet<int>(), // Not accurate but sufficient
                        buffer.BoundaryCells[i].PossibleStates,
                        true // is boundary event
                    );

                    propagationHandler.AddPropagationEvent(propagationEvent);
                }
            }
        }

        /*
         * ValidateBoundary
         * ----------------------------------------------------------------------------
         * Validates that a chunk boundary is consistent with adjacent chunks.
         * 
         * This validation ensures world coherence by:
         * 1. Checking each cell pair across chunk boundaries
         * 2. For collapsed cells, verifying state compatibility using adjacency rules
         * 3. For uncollapsed cells, verifying they have at least one compatible possibility
         * 4. Identifying and reporting any boundary conflicts
         * 
         * This is both a diagnostic function and a safety check to ensure
         * that the chunk boundary system is working correctly and that
         * seams won't appear in the final terrain.
         * 
         * Parameters:
         * - buffer: The boundary buffer to validate
         * 
         * Returns: true if boundary is consistent, false if conflicts found
         */
        public bool ValidateBoundary(BoundaryBuffer buffer)
        {
            if (buffer.AdjacentChunk == null)
                return true;

            for (int i = 0; i < buffer.BoundaryCells.Count; i++)
            {
                Cell boundaryCell = buffer.BoundaryCells[i];

                // Skip if not collapsed
                if (!boundaryCell.CollapsedState.HasValue)
                    continue;

                // Get corresponding cell in adjacent chunk
                Direction oppositeDir = buffer.Direction.GetOpposite();

                if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                    continue;

                if (i >= adjacentBuffer.BoundaryCells.Count)
                    continue;

                Cell adjacentCell = adjacentBuffer.BoundaryCells[i];

                // If adjacent cell is collapsed, check compatibility
                if (adjacentCell.CollapsedState.HasValue)
                {
                    if (!compatibilityChecker.AreStatesCompatible(
                        boundaryCell.CollapsedState.Value,
                        adjacentCell.CollapsedState.Value,
                        buffer.Direction))
                    {
                        Debug.LogWarning($"Boundary conflict: {boundaryCell.CollapsedState} cannot be adjacent to {adjacentCell.CollapsedState}");
                        return false;
                    }
                }

                // If adjacent cell has possible states, check if any are compatible
                else if (adjacentCell.PossibleStates.Count > 0)
                {
                    bool anyCompatible = false;
                    foreach (int state in adjacentCell.PossibleStates)
                    {
                        if (compatibilityChecker.AreStatesCompatible(boundaryCell.CollapsedState.Value, state, buffer.Direction))
                        {
                            anyCompatible = true;
                            break;
                        }
                    }

                    if (!anyCompatible)
                    {
                        Debug.LogWarning($"Boundary conflict: {boundaryCell.CollapsedState} has no compatible states with adjacent cell options");
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Calculate boundary coherence metrics
        /// </summary>
        public BoundaryMetrics CalculateBoundaryMetrics(BoundaryBuffer buffer)
        {
            BoundaryMetrics metrics = new BoundaryMetrics();
            metrics.Direction = buffer.Direction;
            metrics.ChunkPosition = buffer.OwnerChunk.Position;

            // Skip if no adjacent chunk
            if (buffer.AdjacentChunk == null)
            {
                metrics.HasAdjacentChunk = false;
                return metrics;
            }

            metrics.HasAdjacentChunk = true;
            metrics.AdjacentChunkPosition = buffer.AdjacentChunk.Position;

            int totalCells = buffer.BoundaryCells.Count;
            int collapsedCells = 0;
            int compatibleCells = 0;
            int conflictCells = 0;

            for (int i = 0; i < buffer.BoundaryCells.Count; i++)
            {
                Cell cell1 = buffer.BoundaryCells[i];
                Direction oppositeDir = buffer.Direction.GetOpposite();

                if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                    continue;

                if (i >= adjacentBuffer.BoundaryCells.Count)
                    continue;

                Cell cell2 = adjacentBuffer.BoundaryCells[i];

                if (cell1.CollapsedState.HasValue && cell2.CollapsedState.HasValue)
                {
                    collapsedCells++;

                    if (compatibilityChecker.AreStatesCompatible(
                        cell1.CollapsedState.Value,
                        cell2.CollapsedState.Value,
                        buffer.Direction))
                    {
                        compatibleCells++;
                    }
                    else
                    {
                        conflictCells++;
                    }
                }
            }

            metrics.TotalCells = totalCells;
            metrics.CollapsedCells = collapsedCells;
            metrics.CompatibleCells = compatibleCells;
            metrics.ConflictCells = conflictCells;

            if (collapsedCells > 0)
            {
                metrics.CoherenceScore = (float)compatibleCells / collapsedCells;
            }

            return metrics;
        }

        /// <summary>
        /// Resolve a boundary conflict by resetting one side
        /// </summary>
        public void ResolveBoundaryConflict(BoundaryBuffer buffer, int cellIndex)
        {
            if (buffer.AdjacentChunk == null || cellIndex >= buffer.BoundaryCells.Count)
                return;

            Cell cell1 = buffer.BoundaryCells[cellIndex];
            Direction oppositeDir = buffer.Direction.GetOpposite();

            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return;

            if (cellIndex >= adjacentBuffer.BoundaryCells.Count)
                return;

            Cell cell2 = adjacentBuffer.BoundaryCells[cellIndex];

            // Skip if either cell is not collapsed
            if (!cell1.CollapsedState.HasValue || !cell2.CollapsedState.HasValue)
                return;

            // Check if there's a conflict
            if (compatibilityChecker.AreStatesCompatible(
                cell1.CollapsedState.Value, cell2.CollapsedState.Value, buffer.Direction))
                return; // No conflict

            // We have a conflict - choose which cell to reset based on chunk priorities
            if (buffer.OwnerChunk.Priority >= buffer.AdjacentChunk.Priority)
            {
                // Reset cell2 (in adjacent chunk)
                ResetCell(cell2, buffer.AdjacentChunk);
            }
            else
            {
                // Reset cell1 (in this chunk)
                ResetCell(cell1, buffer.OwnerChunk);
            }
        }

        /// <summary>
        /// Reset a cell to its initial state and create propagation event
        /// </summary>
        private void ResetCell(Cell cell, Chunk chunk)
        {
            // Store the old state for propagation
            int oldState = cell.CollapsedState.Value;

            // Reset to initial state set (all possible states)
            // Since we don't have direct access to maxStates, use reflection to get it
            int maxStates = GetMaxStates();
            cell.SetPossibleStates(CreateSequentialHashSet(maxStates));

            // Create propagation event
            PropagationEvent evt = new PropagationEvent(
                cell,
                chunk,
                new HashSet<int> { oldState },
                cell.PossibleStates,
                cell.IsBoundary
            );

            propagationHandler.AddPropagationEvent(evt);
        }

        /// <summary>
        /// Helper method to get max states count
        /// </summary>
        private int GetMaxStates()
        {
            // Try to get from WFCGenerator if possible
            var wfcGenerator = compatibilityChecker as WFC.Generation.WFCGenerator;
            if (wfcGenerator != null)
            {
                var prop = wfcGenerator.GetType().GetProperty("MaxCellStates");
                if (prop != null)
                {
                    return (int)prop.GetValue(wfcGenerator);
                }
            }

            // Default fallback
            return 7; // Common default
        }

        /// <summary>
        /// Helper to create a HashSet with sequential integers from 0 to count-1
        /// </summary>
        private HashSet<int> CreateSequentialHashSet(int count)
        {
            HashSet<int> result = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                result.Add(i);
            }
            return result;
        }
    }

    /// <summary>
    /// Class to store boundary coherence metrics
    /// </summary>
    public class BoundaryMetrics
    {
        public Direction Direction;
        public Vector3Int ChunkPosition;
        public bool HasAdjacentChunk;
        public Vector3Int AdjacentChunkPosition;
        public int TotalCells;
        public int CollapsedCells;
        public int CompatibleCells;
        public int ConflictCells;
        public float CoherenceScore;
    }
}