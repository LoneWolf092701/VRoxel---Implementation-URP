// Assets/Scripts/WFC/Core/PropagationEvent.cs
using System;
using System.Collections.Generic;

namespace WFC.Core
{
    public class PropagationEvent : IComparable<PropagationEvent>
    {
        public Cell Cell { get; set; }
        public Chunk Chunk { get; set; }
        public HashSet<int> OldStates { get; set; }
        public HashSet<int> NewStates { get; set; }
        public bool IsBoundaryEvent { get; set; }
        public float Priority { get; private set; }

        public PropagationEvent(Cell cell, Chunk chunk, HashSet<int> oldStates, HashSet<int> newStates, bool isBoundaryEvent)
        {
            Cell = cell;
            Chunk = chunk;
            OldStates = new HashSet<int>(oldStates);
            NewStates = new HashSet<int>(newStates);
            IsBoundaryEvent = isBoundaryEvent;

            CalculatePriority();
        }

        private void CalculatePriority()
        {
            // Higher priority for:
            // 1. Boundary events
            // 2. Events with fewer possible states
            Priority = NewStates.Count;

            if (IsBoundaryEvent)
                Priority -= 100; // Make boundary events higher priority
        }

        public int CompareTo(PropagationEvent other)
        {
            return Priority.CompareTo(other.Priority);
        }
    }
}