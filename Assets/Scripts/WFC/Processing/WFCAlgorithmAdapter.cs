using System.Collections.Generic;
using UnityEngine;
using WFC.Boundary;
using WFC.Core;
using WFC.Generation;
using Unity.Collections;

namespace WFC.Processing
{
    /// <summary>
    /// Adapter class that makes different WFC implementations compatible with the parallel processor.
    /// This acts as a bridge between different WFC implementations and the IWFCAlgorithm interface,
    /// with additional support for Burst-compiled jobs.
    /// </summary>
    public class WFCAlgorithmAdapter : IWFCAlgorithm
    {
        private WFCGenerator wfcGenerator;

        // Cache for adjacent rules to avoid excessive lookups
        private bool[,,] adjacencyRulesCache;

        /// <summary>
        /// Create an adapter for a WFCGenerator
        /// </summary>
        public WFCAlgorithmAdapter(WFCGenerator generator)
        {
            this.wfcGenerator = generator;

            // Initialize adjacency rules cache
            int maxStates = 16; // Reasonable default, adjust based on your config
            int directions = 6; // Up, Down, Left, Right, Forward, Back

            adjacencyRulesCache = new bool[maxStates, maxStates, directions];

            // Pre-populate cache
            for (int stateA = 0; stateA < maxStates; stateA++)
            {
                for (int stateB = 0; stateB < maxStates; stateB++)
                {
                    for (int dir = 0; dir < directions; dir++)
                    {
                        Direction direction = (Direction)dir;
                        adjacencyRulesCache[stateA, stateB, dir] =
                            wfcGenerator.AreStatesCompatible(stateA, stateB, direction);
                    }
                }
            }
        }

        /// <summary>
        /// Check if two states are compatible in a given direction
        /// </summary>
        public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
        {
            // First check cache for performance
            int dir = (int)direction;
            if (stateA < adjacencyRulesCache.GetLength(0) &&
                stateB < adjacencyRulesCache.GetLength(1) &&
                dir < adjacencyRulesCache.GetLength(2))
            {
                return adjacencyRulesCache[stateA, stateB, dir];
            }

            // Fallback to direct query
            return wfcGenerator.AreStatesCompatible(stateA, stateB, direction);
        }

        /// <summary>
        /// Add a propagation event to the queue
        /// </summary>
        public void AddPropagationEvent(PropagationEvent evt)
        {
            wfcGenerator.AddPropagationEvent(evt);
        }

        /// <summary>
        /// Get all chunks in the system
        /// </summary>
        public Dictionary<Vector3Int, Chunk> GetChunks()
        {
            return wfcGenerator.GetChunks();
        }

        /// <summary>
        /// Create a NativeArray containing adjacency rules for Burst jobs
        /// </summary>
        public NativeArray<int> CreateAdjacencyRulesArray(Allocator allocator)
        {
            int maxStates = adjacencyRulesCache.GetLength(0);
            int directions = adjacencyRulesCache.GetLength(2);

            // Create a flattened array
            NativeArray<int> nativeRules = new NativeArray<int>(
                maxStates * maxStates * directions,
                allocator
            );

            // Fill with rule data (1 for compatible, 0 for incompatible)
            for (int stateA = 0; stateA < maxStates; stateA++)
            {
                for (int stateB = 0; stateB < maxStates; stateB++)
                {
                    for (int dir = 0; dir < directions; dir++)
                    {
                        int index = (stateA * maxStates * directions) + (stateB * directions) + dir;
                        nativeRules[index] = adjacencyRulesCache[stateA, stateB, dir] ? 1 : 0;
                    }
                }
            }

            return nativeRules;
        }

        /// <summary>
        /// Get a hierarchical constraint system data for Burst jobs
        /// </summary>
        public NativeArray<float> CreateConstraintDataArray(Chunk chunk, Allocator allocator)
        {
            // Implementation would depend on your constraints system
            // This is a simplified placeholder that creates empty constraint data
            NativeArray<float> constraintData = new NativeArray<float>(
                chunk.Size * chunk.Size * chunk.Size,
                allocator
            );

            // In a real implementation, you would fill this with actual constraint values
            for (int i = 0; i < constraintData.Length; i++)
            {
                constraintData[i] = 0.5f; // Neutral constraint value
            }

            return constraintData;
        }

        /// <summary>
        /// Apply the results of a Burst job back to the chunk
        /// </summary>
        public void ApplyJobResultsToChunk(Chunk chunk, NativeArray<int> cellStates)
        {
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        int index = (x * chunk.Size * chunk.Size) + (y * chunk.Size) + z;
                        int state = cellStates[index];

                        if (state >= 0) // Valid state
                        {
                            Cell cell = chunk.GetCell(x, y, z);
                            if (cell != null && !cell.CollapsedState.HasValue)
                            {
                                cell.Collapse(state);
                            }
                        }
                    }
                }
            }
        }
    }
}