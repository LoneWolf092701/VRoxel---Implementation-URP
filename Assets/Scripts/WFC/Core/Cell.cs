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
/*
 * Cell.cs
 * -----------------------------
 * Implements the fundamental unit for the Wave Function Collapse algorithm.
 * Each cell represents a discrete volume in 3D space that can be in multiple
 * possible states until "collapsed" to a single definite state.
 * 
 * Key WFC Concepts:
 * - Superposition: Cells start with multiple possible states
 * - Collapse: The act of selecting a single definite state
 * - Entropy: Measure of uncertainty (number of possible states)
 * - Propagation: Constraints spreading from collapsed cells to neighbors
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

        /*
         * Collapse
         * ----------------------------------------------------------------------------
         * Collapses a cell to a single definite state.
         * 
         * This is the core state transformation in the WFC algorithm:
         * 1. Verifies the requested state is in the cell's possible states
         * 2. Clears all possible states and sets only the chosen state
         * 3. Sets the CollapsedState property to mark the cell as definite
         * 
         * A collapsed cell has reached its final state and won't change further
         * during the WFC algorithm, but it will continue to influence neighboring
         * cells through constraint propagation.
         * 
         * Parameters:
         * - state: The state to collapse this cell to
         * 
         * Returns: true if collapse succeeded, false if the state wasn't possible
         */
        public bool Collapse(int state)
        {
            if (!PossibleStates.Contains(state))
                return false;

            PossibleStates.Clear();
            PossibleStates.Add(state);
            CollapsedState = state;
            return true;
        }
        /*
         * RemoveState
         * ----------------------------------------------------------------------------
         * Removes a possible state from a cell's superposition.
         * 
         * This is how constraints remove impossible states during propagation:
         * 1. Checks if cell is already collapsed (no changes possible)
         * 2. Checks if the state is actually in the possible states
         * 3. Removes the state from possibilities
         * 4. If only one state remains, automatically collapses to that state
         * 
         * The auto-collapse on last state is an optimization that immediately
         * finalizes cells once they have only one option left, allowing them
         * to start propagating constraints to their neighbors.
         * 
         * Parameters:
         * - state: The state to remove from possibilities
         * 
         * Returns: true if state was removed, false if cell was already collapsed
         *          or state wasn't in the possible states
         */
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

        /*
         * SetPossibleStates
         * ----------------------------------------------------------------------------
         * Sets a new collection of possible states for the cell.
         * 
         * This is used during constraint propagation and initialization:
         * 1. Checks if cell is already collapsed (no changes possible)
         * 2. Ensures the new state set isn't empty
         * 3. Records the old number of states for change detection
         * 4. Replaces the current possible states with the new set
         * 5. If only one state remains, automatically collapses to that state
         * 
         * The function compares the old and new counts to detect if the entropy
         * actually changed, which is important for determining if further
         * propagation is needed.
         * 
         * Parameters:
         * - states: New set of possible states
         * 
         * Returns: true if the number of states changed, false otherwise
         */
        public bool SetPossibleStates(HashSet<int> states)
        {
            if (CollapsedState.HasValue)
                return false;

            if (states.Count == 0)
                return false;

            // Performance optimization: Compare count first, then contents only if needed
            int oldCount = PossibleStates.Count;
            bool countChanged = oldCount != states.Count;

            // Only do expensive set comparison if counts match
            bool contentsChanged = !countChanged && !PossibleStates.SetEquals(states);

            // Replace states if anything changed
            if (countChanged || contentsChanged)
            {
                PossibleStates = new HashSet<int>(states);

                // Auto-collapse if only one state remains
                if (PossibleStates.Count == 1)
                {
                    var enumerator = PossibleStates.GetEnumerator();
                    enumerator.MoveNext();
                    CollapsedState = enumerator.Current;
                }

                return true;
            }

            return false;
        }
    }
}