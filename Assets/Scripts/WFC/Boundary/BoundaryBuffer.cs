// Assets/Scripts/WFC/Boundary/BoundaryBuffer.cs
using System.Collections.Generic;
using WFC.Core;

namespace WFC.Boundary
{
    public class BoundaryBuffer
    {
        // Direction of this boundary
        public Direction Direction { get; set; }

        // Owner chunk
        public Chunk OwnerChunk { get; set; }

        // Adjacent chunk
        public Chunk AdjacentChunk { get; set; }

        // Cells on the boundary
        public List<Cell> BoundaryCells { get; set; }

        // Virtual buffer cells that mirror adjacent chunk's boundary
        public List<Cell> BufferCells { get; set; }

        // Consistency flag
        public bool IsConsistent { get; set; }

        public BoundaryBuffer(Direction direction, Chunk ownerChunk)
        {
            Direction = direction;
            OwnerChunk = ownerChunk;
            AdjacentChunk = null;
            BoundaryCells = new List<Cell>();
            BufferCells = new List<Cell>();
            IsConsistent = true;
        }
    }
}