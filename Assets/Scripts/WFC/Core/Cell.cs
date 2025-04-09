/*
# ===================================================================
# Wave Function Collapse Algorithm Implementation with Hierarchical Constraints
# ===================================================================
# Original WFC Algorithm:
#   - Maxim Gumin (https://github.com/mxgmn/WaveFunctionCollapse)
#
# This implementation incorporates elements from:
#   - Oskar Stålberg's techniques for boundary handling in Townscaper (adopted)
#   - Martin O'Leary's approaches for constraint-based terrain generation (adopted)
#   - Claude Shannon's entropy calculation from information theory
#
# This code implements a 2D version of WFC with hierarchical constraints,
# chunking, and boundary management systems for coherent region transitions.
# ===================================================================
*/
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WFC.Core
{
    [Serializable]
    public class Cell
    {
        // Position within chunk
        public Vector3Int Position { get; private set; }

        // Possible states for this cell
        public HashSet<int> PossibleStates { get; private set; }

        // Collapsed state (null if not collapsed)
        public int? CollapsedState { get; private set; }

        // Entropy (count of possible states)
        public int Entropy => PossibleStates.Count;

        // Boundary flags
        public bool IsBoundary { get; set; }
        public Direction? BoundaryDirection { get; set; }

        // Visual correction flag for mesh generation
        public bool NeedsVisualCorrection { get; set; }

        /// <summary>
        /// Creates a new cell at the specified position with the given initial possible states.
        /// </summary>
        /// <param name="position">3D position within a chunk</param>
        /// <param name="initialStates">Collection of initially allowed states for this cell</param>
        public Cell(Vector3Int position, IEnumerable<int> initialStates)
        {
            Position = position;
            PossibleStates = new HashSet<int>(initialStates);
            CollapsedState = null;
            IsBoundary = false;
            BoundaryDirection = null;
        }

        /// <summary>
        /// Collapses this cell to a specific state, removing all other possibilities.
        /// The core operation of the Wave Function Collapse algorithm.
        /// </summary>
        /// <param name="state">The state to collapse to</param>
        /// <returns>True if collapse was successful, false if the state wasn't possible</returns>
        public bool Collapse(int state)
        {
            if (!PossibleStates.Contains(state))
                return false;

            PossibleStates.Clear();
            PossibleStates.Add(state);
            CollapsedState = state;
            return true;
        }
        /// <summary>
        /// Removes a specific state from the cell's possibilities.
        /// Automatically collapses the cell if only one state remains.
        /// </summary>
        /// <param name="state">The state to remove</param>
        /// <returns>True if state was removed, false if already collapsed or state not present</returns>
        public bool RemoveState(int state)
        {
            if (CollapsedState.HasValue)
                return false;

            if (!PossibleStates.Contains(state))
                return false;

            PossibleStates.Remove(state);

            if (PossibleStates.Count == 1)
            {
                var enumerator = PossibleStates.GetEnumerator();
                enumerator.MoveNext();
                CollapsedState = enumerator.Current;
            }

            return true;
        }

        /// <summary>
        /// Set possible states to be the given hash set.
        /// </summary>
        /// <param name="state">The hash set of states to remove</param>
        /// <returns>True if state is possible, false if already collapsed or state not present</returns>
        public bool SetPossibleStates(HashSet<int> states)
        {
            if (CollapsedState.HasValue)
                return false;

            if (states.Count == 0)
                return false;

            var oldCount = PossibleStates.Count;
            PossibleStates = new HashSet<int>(states);

            if (PossibleStates.Count == 1)
            {
                var enumerator = PossibleStates.GetEnumerator();
                enumerator.MoveNext();
                CollapsedState = enumerator.Current;
            }

            return oldCount != PossibleStates.Count;
        }
    }
}