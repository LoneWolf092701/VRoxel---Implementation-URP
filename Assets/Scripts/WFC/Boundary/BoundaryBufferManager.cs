// Assets/Scripts/WFC/Boundary/BoundaryBufferManager.cs
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
        // Empty - just combines the other interfaces
    }

    /// <summary>
    /// Manages boundary buffers and coherence between chunks
    /// </summary>
    public class BoundaryBufferManager
    {
        private IBoundaryCompatibilityChecker compatibilityChecker;
        private IPropagationHandler propagationHandler;
        private IChunkProvider chunkProvider;

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
        /// Updates buffer states when a cell collapses at the boundary
        /// </summary>
        public void UpdateBuffersAfterCollapse(Cell cell, Chunk chunk)
        {
            // If cell is not a boundary cell, nothing to do
            if (!cell.IsBoundary || !cell.BoundaryDirection.HasValue)
                return;

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

            // Update the buffer cell in the adjacent chunk
            if (index < adjacentBuffer.BufferCells.Count)
            {
                Cell bufferCell = adjacentBuffer.BufferCells[index];

                // Set buffer cell to match this cell's state
                if (cell.CollapsedState.HasValue)
                {
                    bufferCell.SetPossibleStates(new HashSet<int> { cell.CollapsedState.Value });
                }
                else
                {
                    bufferCell.SetPossibleStates(new HashSet<int>(cell.PossibleStates));
                }

                // Apply buffer constraints to the corresponding boundary cell
                Cell adjacentBoundaryCell = adjacentBuffer.BoundaryCells[index];
                ApplyBufferConstraints(adjacentBoundaryCell, bufferCell, oppositeDir);

                // If this causes a collapse, add to propagation queue
                if (adjacentBoundaryCell.CollapsedState.HasValue &&
                    adjacentBoundaryCell.PossibleStates.Count == 1)
                {
                    var oldStates = new HashSet<int>(); // Not accurate but sufficient
                    var newStates = adjacentBoundaryCell.PossibleStates;

                    var propagationEvent = new PropagationEvent(
                        adjacentBoundaryCell,
                        buffer.AdjacentChunk,
                        oldStates,
                        newStates,
                        true // is boundary event
                    );

                    propagationHandler.AddPropagationEvent(propagationEvent);
                }
            }
        }

        /// <summary>
        /// Applies constraints from buffer cells to boundary cells
        /// </summary>
        public void ApplyBufferConstraints(Cell boundaryCell, Cell bufferCell, Direction direction)
        {
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

            // Update boundary cell
            if (newPossibleStates.Count > 0)
            {
                boundaryCell.SetPossibleStates(newPossibleStates);
            }
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

        /// <summary>
        /// Validates that boundaries between chunks are coherent
        /// </summary>
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
    }
}