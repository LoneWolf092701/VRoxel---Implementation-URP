// Assets/Scripts/WFC/Processing/WFCAlgorithmAdapter.cs

using System.Collections.Generic;
using UnityEngine;
using WFC.Boundary;
using WFC.Core;
using WFC.Generation;
using WFC.Testing;

namespace WFC.Processing
{
    /// <summary>
    /// Adapter class that makes different WFC implementations compatible with the parallel processor.
    /// This acts as a bridge between different WFC implementations and the IWFCAlgorithm interface.
    /// </summary>
    public class WFCAlgorithmAdapter : IWFCAlgorithm
    {
        private WFCGenerator wfcGenerator;
        private WFCTestController wfcTestController;
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
        /// Create an adapter for a WFCTestController
        /// </summary>
        public WFCAlgorithmAdapter(WFCTestController testController)
        {
            this.wfcTestController = testController;
            this.useTestController = true;
        }

        /// <summary>
        /// Check if two states are compatible in a given direction
        /// </summary>
        public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
        {
            if (useTestController)
            {
                return wfcTestController.AreStatesCompatible(stateA, stateB, direction);
            }
            else
            {
                // Access WFCGenerator's compatibility check
                // If wfcGenerator's method is private, use reflection to access it
                // or modify WFCGenerator to expose this functionality
                return wfcGenerator.AreStatesCompatible(stateA, stateB, direction);
            }
        }

        /// <summary>
        /// Add a propagation event to the queue
        /// </summary>
        public void AddPropagationEvent(PropagationEvent evt)
        {
            if (useTestController)
            {
                wfcTestController.AddPropagationEvent(evt);
            }
            else
            {
                wfcGenerator.AddPropagationEvent(evt);
            }
        }

        /// <summary>
        /// Get all chunks in the system
        /// </summary>
        public Dictionary<Vector3Int, Chunk> GetChunks()
        {
            if (useTestController)
            {
                return wfcTestController.GetChunks();
            }
            else
            {
                return wfcGenerator.GetChunks();
            }
        }
    }
}