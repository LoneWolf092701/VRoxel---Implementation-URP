// Assets/Scripts/WFC/Processing/WFCAlgorithmAdapter.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Boundary;
using WFC.Core;
using WFC.Generation;
// Remove this import if you're not using it anymore
//using WFC.Testing;

namespace WFC.Processing
{
    /// <summary>
    /// Adapter class that makes different WFC implementations compatible with the parallel processor.
    /// This acts as a bridge between different WFC implementations and the IWFCAlgorithm interface.
    /// </summary>
    public class WFCAlgorithmAdapter : IWFCAlgorithm
    {
        private WFCGenerator wfcGenerator;
        private bool useTestController;

        /// <summary>
        /// Create an adapter for a WFCGenerator
        /// </summary>
        public WFCAlgorithmAdapter(WFCGenerator generator)
        {
            this.wfcGenerator = generator;
            this.useTestController = false;
        }

        /// <summary>
        /// Check if two states are compatible in a given direction
        /// </summary>
        public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
        {
            // Since we're only using WFCGenerator now, we can simplify this
            return wfcGenerator.AreStatesCompatible(stateA, stateB, direction);
        }

        /// <summary>
        /// Add a propagation event to the queue
        /// </summary>
        public void AddPropagationEvent(PropagationEvent evt)
        {
            // Since we're only using WFCGenerator now, we can simplify this
            wfcGenerator.AddPropagationEvent(evt);
        }

        /// <summary>
        /// Get all chunks in the system
        /// </summary>
        public Dictionary<Vector3Int, Chunk> GetChunks()
        {
            // Since we're only using WFCGenerator now, we can simplify this
            return wfcGenerator.GetChunks();
        }
    }
}