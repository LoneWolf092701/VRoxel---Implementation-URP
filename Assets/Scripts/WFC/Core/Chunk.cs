// Assets/Scripts/WFC/Core/Chunk.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Boundary; // For BoundaryBuffer

namespace WFC.Core
{
    public class Chunk
    {
        // Chunk position in world (in chunk coordinates)
        public Vector3Int Position { get; private set; }

        // Size of chunk in each dimension
        public int Size { get; private set; }

        // 3D array of cells
        private Cell[,,] cells;

        // Neighboring chunks
        public Dictionary<Direction, Chunk> Neighbors { get; private set; }

        // Boundary buffers
        public Dictionary<Direction, BoundaryBuffer> BoundaryBuffers { get; private set; }

        // Status flags
        public bool IsFullyCollapsed { get; set; }
        public bool IsDirty { get; set; }
        public float Priority { get; set; }
        public bool HasVisualCorrections { get; set; }

        public Chunk(Vector3Int position, int size)
        {
            Position = position;
            Size = size;

            // Initialize cells
            cells = new Cell[size, size, size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int z = 0; z < size; z++)
                    {
                        cells[x, y, z] = new Cell(new Vector3Int(x, y, z), new int[0]);
                    }
                }
            }

            Neighbors = new Dictionary<Direction, Chunk>();
            BoundaryBuffers = new Dictionary<Direction, BoundaryBuffer>();

            IsFullyCollapsed = false;
            IsDirty = true;
            HasVisualCorrections = false;
        }

        public Cell GetCell(int x, int y, int z)
        {
            if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size)
                return null;

            return cells[x, y, z];
        }

        public Cell GetCell(Vector3Int position)
        {
            return GetCell(position.x, position.y, position.z);
        }

        public void SetCell(int x, int y, int z, Cell cell)
        {
            if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size)
                return;

            cells[x, y, z] = cell;
            IsDirty = true;
        }

        public bool IsOnBoundary(int x, int y, int z)
        {
            return x == 0 || x == Size - 1 || y == 0 || y == Size - 1 || z == 0 || z == Size - 1;
        }

        public Direction? GetBoundaryDirection(int x, int y, int z)
        {
            if (x == 0) return Direction.Left;
            if (x == Size - 1) return Direction.Right;
            if (y == 0) return Direction.Down;
            if (y == Size - 1) return Direction.Up;
            if (z == 0) return Direction.Back;
            if (z == Size - 1) return Direction.Forward;

            return null;
        }

        public void InitializeCells(IEnumerable<int> possibleStates)
        {
            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int z = 0; z < Size; z++)
                    {
                        cells[x, y, z] = new Cell(new Vector3Int(x, y, z), possibleStates);

                        // Set boundary flags
                        if (IsOnBoundary(x, y, z))
                        {
                            cells[x, y, z].IsBoundary = true;
                            cells[x, y, z].BoundaryDirection = GetBoundaryDirection(x, y, z);
                        }
                    }
                }
            }
        }
    }
}