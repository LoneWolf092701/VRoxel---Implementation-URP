// Assets/Scripts/WFC/Core/Cell.cs
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

        public Cell(Vector3Int position, IEnumerable<int> initialStates)
        {
            Position = position;
            PossibleStates = new HashSet<int>(initialStates);
            CollapsedState = null;
            IsBoundary = false;
            BoundaryDirection = null;
        }

        public bool Collapse(int state)
        {
            if (!PossibleStates.Contains(state))
                return false;

            PossibleStates.Clear();
            PossibleStates.Add(state);
            CollapsedState = state;
            return true;
        }

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