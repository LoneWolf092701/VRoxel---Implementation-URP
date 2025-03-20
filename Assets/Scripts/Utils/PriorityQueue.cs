// Assets/Scripts/Utils/PriorityQueue.cs
using System;
using System.Collections.Generic;
using Utils; // For PriorityQueue

namespace Utils
{
    public class PriorityQueue<TElement, TPriority> where TPriority : IComparable<TPriority>
    {
        private List<(TElement Element, TPriority Priority)> elements = new List<(TElement, TPriority)>();

        public int Count => elements.Count;

        public void Enqueue(TElement element, TPriority priority)
        {
            elements.Add((element, priority));
            int currentIndex = elements.Count - 1;

            while (currentIndex > 0)
            {
                int parentIndex = (currentIndex - 1) / 2;

                if (elements[parentIndex].Priority.CompareTo(elements[currentIndex].Priority) <= 0)
                    break;

                // Swap elements
                var temp = elements[currentIndex];
                elements[currentIndex] = elements[parentIndex];
                elements[parentIndex] = temp;

                currentIndex = parentIndex;
            }
        }

        public TElement Dequeue()
        {
            if (elements.Count == 0)
                throw new InvalidOperationException("Queue is empty");

            TElement result = elements[0].Element;

            // Move the last element to the front
            elements[0] = elements[elements.Count - 1];
            elements.RemoveAt(elements.Count - 1);

            // Heapify
            int currentIndex = 0;
            while (true)
            {
                int leftChildIndex = currentIndex * 2 + 1;
                int rightChildIndex = currentIndex * 2 + 2;

                if (leftChildIndex >= elements.Count)
                    break;

                int smallestChildIndex = leftChildIndex;
                if (rightChildIndex < elements.Count &&
                    elements[rightChildIndex].Priority.CompareTo(elements[leftChildIndex].Priority) < 0)
                {
                    smallestChildIndex = rightChildIndex;
                }

                if (elements[smallestChildIndex].Priority.CompareTo(elements[currentIndex].Priority) >= 0)
                    break;

                // Swap elements
                var temp = elements[currentIndex];
                elements[currentIndex] = elements[smallestChildIndex];
                elements[smallestChildIndex] = temp;

                currentIndex = smallestChildIndex;
            }

            return result;
        }

        public TElement Peek()
        {
            if (elements.Count == 0)
                throw new InvalidOperationException("Queue is empty");

            return elements[0].Element;
        }
    }
}