/*
 * BoundaryBufferManager.cs
 * -----------------------------
 * Manages the boundaries between chunks to ensure seamless terrain generation.
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
        private const int MAX_PROPAGATION_COUNT = 15;

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

        /// <summary>
        /// Updates boundary buffers when a cell collapses at a chunk boundary.
        /// </summary>
        public void UpdateBuffersAfterCollapse(Cell cell, Chunk chunk)
        {
            // Skip if not a boundary cell
            if (!cell.IsBoundary || !cell.BoundaryDirection.HasValue)
                return;

            // Check propagation limit
            Vector3Int chunkPos = chunk.Position;
            if (!propagationCount.TryGetValue(chunkPos, out int count))
                count = 0;

            if (++count > MAX_PROPAGATION_COUNT)
            {
                Debug.LogWarning($"Maximum propagation count reached for chunk {chunkPos}");
                propagationCount[chunkPos] = 0;
                return;
            }
            propagationCount[chunkPos] = count;

            Direction direction = cell.BoundaryDirection.Value;

            // Get the buffer for this direction
            if (!chunk.BoundaryBuffers.TryGetValue(direction, out BoundaryBuffer buffer) ||
                buffer.AdjacentChunk == null)
                return;

            // Find cell in boundary array
            int index = buffer.BoundaryCells.IndexOf(cell);
            if (index == -1)
                return;

            Direction oppositeDir = direction.GetOpposite();

            // Get adjacent buffer
            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return;

            // Forward propagation (this chunk to adjacent chunk)
            if (index < adjacentBuffer.BufferCells.Count)
            {
                Cell adjacentBufferCell = adjacentBuffer.BufferCells[index];
                bool adjacentBufferChanged = false;

                if (cell.CollapsedState.HasValue)
                {
                    // Update buffer cell to match this cell's state
                    HashSet<int> oldStates = new HashSet<int>(adjacentBufferCell.PossibleStates);
                    adjacentBufferChanged = adjacentBufferCell.SetPossibleStates(
                        new HashSet<int> { cell.CollapsedState.Value });

                    // Apply constraints to corresponding boundary cell
                    Cell adjacentBoundaryCell = adjacentBuffer.BoundaryCells[index];
                    bool adjacentBoundaryChanged = ApplyBufferConstraints(
                        adjacentBoundaryCell, adjacentBufferCell, oppositeDir, 0, 1.2f);

                    // Create propagation event if boundary changed
                    if (adjacentBoundaryChanged)
                    {
                        var propagationEvent = new PropagationEvent(
                            adjacentBoundaryCell,
                            buffer.AdjacentChunk,
                            new HashSet<int>(),
                            adjacentBoundaryCell.PossibleStates,
                            true
                        );
                        propagationHandler.AddPropagationEvent(propagationEvent);
                    }
                }
            }
            else if (cell.PossibleStates.Count > 0)
            {
                // Handle uncollapsed cells
                Cell adjacentBufferCell = adjacentBuffer.BufferCells[index];
                Cell adjacentBoundaryCell = adjacentBuffer.BoundaryCells[index];
                HashSet<int> oldStates = new HashSet<int>(adjacentBufferCell.PossibleStates);

                // Update buffer with current possible states
                bool adjacentBufferChanged = adjacentBufferCell.SetPossibleStates(
                    new HashSet<int>(cell.PossibleStates));

                if (adjacentBufferChanged)
                {
                    bool adjacentBoundaryChanged = ApplyBufferConstraints(
                        adjacentBoundaryCell, adjacentBufferCell, oppositeDir);

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

            // Backward propagation (adjacent chunk to this chunk)
            if (index < buffer.BufferCells.Count)
            {
                Cell adjacentBoundaryCell = adjacentBuffer.BoundaryCells[index];

                if (adjacentBoundaryCell.PossibleStates.Count < cell.PossibleStates.Count)
                {
                    Cell bufferCell = buffer.BufferCells[index];
                    HashSet<int> oldBufferStates = new HashSet<int>(bufferCell.PossibleStates);

                    bool bufferChanged = bufferCell.SetPossibleStates(
                        new HashSet<int>(adjacentBoundaryCell.PossibleStates));

                    if (bufferChanged && !cell.CollapsedState.HasValue)
                    {
                        // Find compatible states
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

        /// <summary>
        /// Apply constraints from buffer cells to boundary cells
        /// </summary>
        private bool ApplyBufferConstraints(Cell boundaryCell, Cell bufferCell, Direction direction,
                                          int depth = 0, float strengthMultiplier = 1.0f)
        {
            // Prevent infinite recursion
            if (depth > 15) return false;

            HashSet<int> newPossibleStates = new HashSet<int>();

            // Find states compatible with buffer
            foreach (int boundaryState in boundaryCell.PossibleStates)
            {
                foreach (int bufferState in bufferCell.PossibleStates)
                {
                    if (compatibilityChecker.AreStatesCompatible(boundaryState, bufferState, direction))
                    {
                        newPossibleStates.Add(boundaryState);
                        break;
                    }
                }
            }

            // Update boundary cell if valid states found
            if (newPossibleStates.Count > 0)
            {
                HashSet<int> oldStates = new HashSet<int>(boundaryCell.PossibleStates);

                // Enhance strength for entropy reduction
                if (newPossibleStates.Count < oldStates.Count && depth < 3)
                {
                    strengthMultiplier *= 1.1f + (0.1f * (3 - depth));
                }

                return boundaryCell.SetPossibleStates(newPossibleStates);
            }
            else if (boundaryCell.PossibleStates.Count > 0)
            {
                Debug.LogWarning("Boundary constraint conflict: No compatible states");
                return true;
            }

            return false;
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

            // Update each cell at the boundary
            for (int i = 0; i < buffer.BoundaryCells.Count; i++)
            {
                // Update buffer cell to match adjacent boundary cell
                HashSet<int> adjacentStates = new HashSet<int>(adjacentBuffer.BoundaryCells[i].PossibleStates);
                buffer.BufferCells[i].SetPossibleStates(adjacentStates);

                // Apply constraints to boundary cell
                ApplyBufferConstraints(buffer.BoundaryCells[i], buffer.BufferCells[i], buffer.Direction);

                // Queue propagation for collapsed cells
                if (buffer.BoundaryCells[i].PossibleStates.Count == 1 &&
                    buffer.BoundaryCells[i].CollapsedState.HasValue)
                {
                    var propagationEvent = new PropagationEvent(
                        buffer.BoundaryCells[i],
                        buffer.OwnerChunk,
                        new HashSet<int>(),
                        buffer.BoundaryCells[i].PossibleStates,
                        true
                    );
                    propagationHandler.AddPropagationEvent(propagationEvent);
                }
            }
        }

        /// <summary>
        /// Validates that a chunk boundary is consistent with adjacent chunks
        /// </summary>
        public bool ValidateBoundaryConsistency(Chunk chunk)
        {
            bool isConsistent = true;

            foreach (var buffer in chunk.BoundaryBuffers.Values)
            {
                if (!ValidateBoundary(buffer))
                {
                    isConsistent = false;
                    Debug.LogWarning($"Boundary inconsistency at {chunk.Position}, direction {buffer.Direction}");
                }
            }

            return isConsistent;
        }

        /// <summary>
        /// Validates that a boundary is consistent with its adjacent chunk
        /// </summary>
        private bool ValidateBoundary(BoundaryBuffer buffer)
        {
            if (buffer.AdjacentChunk == null)
                return true;

            for (int i = 0; i < buffer.BoundaryCells.Count; i++)
            {
                Cell boundaryCell = buffer.BoundaryCells[i];
                if (!boundaryCell.CollapsedState.HasValue)
                    continue;

                Direction oppositeDir = buffer.Direction.GetOpposite();
                if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                    continue;

                if (i >= adjacentBuffer.BoundaryCells.Count)
                    continue;

                Cell adjacentCell = adjacentBuffer.BoundaryCells[i];

                // Check compatibility for collapsed adjacent cell
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
                // Check if any compatible states exist for uncollapsed adjacent cell
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
                        Debug.LogWarning($"Boundary conflict: No compatible states with adjacent cell");
                        return false;
                    }
                }
            }

            return true;
        }
    }
}