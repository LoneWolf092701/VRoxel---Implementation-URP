using System;
using System.Collections.Generic;
using System.Text;
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

        // Memory optimization flags and storage
        public bool IsMemoryOptimized { get; set; } = false;

        // LOD properties - New additions for the LOD system
        public int LODLevel { get; set; } = 0; // 0 = highest detail
        public int MaxIterations { get; set; } = 100; // Controlled by LOD
        public float ConstraintInfluence { get; set; } = 1.0f; // Multiplier for constraints

        private int[,,] compressedStates;
        private List<Tuple<Vector3Int, int>> sparseCompressedStates;

        /// <summary>
        /// Creates a new chunk at the specified position with the given size.
        /// <summary>
        /// <param name="position"> Chunk Position in the world grid </param>
        /// <param name="size"> Chunk Size in the world grid </param>
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

            // Initialize LOD properties with default values
            LODLevel = 0;
            MaxIterations = 100;
            ConstraintInfluence = 1.0f;
        }

        // Getters and Setters for cells
        public Cell GetCell(int x, int y, int z)
        {
            // If memory is optimized and compressed states, need to restore cells first
            if (IsMemoryOptimized && cells == null)
            {
                if (compressedStates != null || sparseCompressedStates != null)
                {
                    RestoreFromOptimized();
                }
                else
                {
                    Debug.LogError($"Chunk at {Position} is memory optimized but has no compressed data");
                    return null;
                }
            }

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
            // If memory is optimized, restore cells first
            if (IsMemoryOptimized && cells == null)
            {
                RestoreFromOptimized();
            }

            if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size)
                return;

            cells[x, y, z] = cell;
            IsDirty = true;
        }

        // Check if a cell is on the boundary of the chunk
        public bool IsOnBoundary(int x, int y, int z)
        {
            return x == 0 || x == Size - 1 || y == 0 || y == Size - 1 || z == 0 || z == Size - 1;
        }

        // Get the direction of the boundary for a given cell
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
        // Get the neighboring chunk in a specific direction
        public void InitializeCells(IEnumerable<int> possibleStates)
        {
            // If memory is optimized, restore first or recreate cells array
            if (IsMemoryOptimized)
            {
                if (cells == null)
                {
                    cells = new Cell[Size, Size, Size];
                }
                IsMemoryOptimized = false;
                compressedStates = null;
                sparseCompressedStates = null;
            }

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

        /// <summary>
        /// Optimizes chunk memory usage for distant chunks
        /// </summary>
        public void OptimizeMemory()
        {
            // Already optimized
            if (IsMemoryOptimized) return;

            if (IsFullyCollapsed)
            {
                // For fully collapsed chunks, use a simple 3D array of states
                CompressToStateArray();
            }
            else
            {
                // For partially collapsed chunks, use a sparse representation
                CompressPartiallyCollapsed();
            }

            IsMemoryOptimized = true;
        }

        /// <summary>
        /// Restores chunk from optimized representation
        /// </summary>
        public void RestoreFromOptimized()
        {
            if (!IsMemoryOptimized) return;

            if (compressedStates != null)
            {
                RestoreFromStateArray();
            }
            else if (sparseCompressedStates != null)
            {
                RestoreFromSparseRepresentation();
            }

            IsMemoryOptimized = false;
        }

        private void CompressToStateArray()
        {
            // Make sure cells array exists
            if (cells == null) return;

            // Store only collapsed states in a simple array
            compressedStates = new int[Size, Size, Size];

            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int z = 0; z < Size; z++)
                    {
                        Cell cell = GetCell(x, y, z);
                        compressedStates[x, y, z] = cell.CollapsedState ?? -1;
                    }
                }
            }

            // Release full cell data to save memory
            cells = null;
        }

        // Compresses the chunk to a sparse representation for partially collapsed chunks
        private void CompressPartiallyCollapsed()
        {
            // Make sure cells array exists
            if (cells == null) return;

            // Use sparse representation for partially collapsed chunks
            sparseCompressedStates = new List<Tuple<Vector3Int, int>>();

            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int z = 0; z < Size; z++)
                    {
                        Cell cell = GetCell(x, y, z);
                        if (cell.CollapsedState.HasValue)
                        {
                            // Only store collapsed cells
                            sparseCompressedStates.Add(
                                new Tuple<Vector3Int, int>(
                                    new Vector3Int(x, y, z),
                                    cell.CollapsedState.Value));
                        }
                    }
                }
            }

            // Release full cell data to save memory
            cells = null;
        }

        // Restores the chunk from a compressed state array
        private void RestoreFromStateArray()
        {
            // Create cells from compressed state array
            if (compressedStates == null) return;

            // Recreate cells array if needed
            if (cells == null)
            {
                cells = new Cell[Size, Size, Size];

                // Initialize with empty cells
                for (int x = 0; x < Size; x++)
                {
                    for (int y = 0; y < Size; y++)
                    {
                        for (int z = 0; z < Size; z++)
                        {
                            cells[x, y, z] = new Cell(new Vector3Int(x, y, z), new int[0]);

                            // Restore boundary flags
                            if (IsOnBoundary(x, y, z))
                            {
                                cells[x, y, z].IsBoundary = true;
                                cells[x, y, z].BoundaryDirection = GetBoundaryDirection(x, y, z);
                            }

                            // Restore collapsed state
                            int state = compressedStates[x, y, z];
                            if (state >= 0)
                            {
                                cells[x, y, z].Collapse(state);
                            }
                        }
                    }
                }
            }
            else
            {
                // Cells already exist, just update states
                for (int x = 0; x < Size; x++)
                {
                    for (int y = 0; y < Size; y++)
                    {
                        for (int z = 0; z < Size; z++)
                        {
                            int state = compressedStates[x, y, z];
                            if (state >= 0)
                            {
                                cells[x, y, z].Collapse(state);
                            }
                        }
                    }
                }
            }

            // Clear compressed data to free memory
            compressedStates = null;
        }
        // Restores the chunk from a sparse representation
        private void RestoreFromSparseRepresentation()
        {
            if (sparseCompressedStates == null) return;

            // Recreate cells array if needed
            if (cells == null)
            {
                cells = new Cell[Size, Size, Size];

                // Initialize with empty cells
                for (int x = 0; x < Size; x++)
                {
                    for (int y = 0; y < Size; y++)
                    {
                        for (int z = 0; z < Size; z++)
                        {
                            cells[x, y, z] = new Cell(new Vector3Int(x, y, z), new int[0]);

                            // Restore boundary flags
                            if (IsOnBoundary(x, y, z))
                            {
                                cells[x, y, z].IsBoundary = true;
                                cells[x, y, z].BoundaryDirection = GetBoundaryDirection(x, y, z);
                            }
                        }
                    }
                }
            }

            // Apply collapsed states from sparse representation
            foreach (var item in sparseCompressedStates)
            {
                Vector3Int pos = item.Item1;
                int state = item.Item2;

                if (pos.x >= 0 && pos.x < Size &&
                    pos.y >= 0 && pos.y < Size &&
                    pos.z >= 0 && pos.z < Size)
                {
                    cells[pos.x, pos.y, pos.z].Collapse(state);
                }
            }

            // Clear compressed data to free memory
            sparseCompressedStates = null;
        }

        /// <summary>
        /// Gets the status of the chunk
        /// </summary>
        public ChunkStatus GetStatus()
        {
            if (IsMemoryOptimized)
                return ChunkStatus.Optimized;

            if (IsFullyCollapsed)
                return ChunkStatus.FullyCollapsed;

            // Count how many cells are collapsed
            int totalCells = Size * Size * Size;
            int collapsedCount = 0;

            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int z = 0; z < Size; z++)
                    {
                        if (GetCell(x, y, z).CollapsedState.HasValue)
                            collapsedCount++;
                    }
                }
            }

            float collapsePercentage = (float)collapsedCount / totalCells;

            if (collapsePercentage == 0)
                return ChunkStatus.Initialized;
            else if (collapsePercentage < 0.5f)
                return ChunkStatus.PartiallyCollapsed;
            else
                return ChunkStatus.MostlyCollapsed;
        }

        /// <summary>
        /// Serializes the chunk state to a string
        /// </summary>
        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();

            // Write header
            sb.AppendLine($"CHUNK:{Position.x},{Position.y},{Position.z}:{Size}");

            // Write cells
            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int z = 0; z < Size; z++)
                    {
                        Cell cell = GetCell(x, y, z);
                        if (cell.CollapsedState.HasValue)
                        {
                            sb.AppendLine($"C:{x},{y},{z}:{cell.CollapsedState.Value}");
                        }
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Deserializes a chunk from a string
        /// </summary>
        public static Chunk Deserialize(string data)
        {
            Chunk chunk = null;

            // Split into lines
            string[] lines = data.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Process header
            if (lines.Length > 0 && lines[0].StartsWith("CHUNK:"))
            {
                string[] headerParts = lines[0].Substring(6).Split(':');
                if (headerParts.Length == 2)
                {
                    // Parse chunk position
                    string[] posParts = headerParts[0].Split(',');
                    if (posParts.Length == 3)
                    {
                        int posX = int.Parse(posParts[0]);
                        int posY = int.Parse(posParts[1]);
                        int posZ = int.Parse(posParts[2]);

                        // Parse chunk size
                        int size = int.Parse(headerParts[1]);

                        // Create chunk
                        chunk = new Chunk(new Vector3Int(posX, posY, posZ), size);

                        // Process cell data
                        for (int i = 1; i < lines.Length; i++)
                        {
                            if (lines[i].StartsWith("C:"))
                            {
                                string[] cellParts = lines[i].Substring(2).Split(':');
                                if (cellParts.Length == 2)
                                {
                                    string[] posParts2 = cellParts[0].Split(',');
                                    if (posParts2.Length == 3)
                                    {
                                        int x = int.Parse(posParts2[0]);
                                        int y = int.Parse(posParts2[1]);
                                        int z = int.Parse(posParts2[2]);

                                        int state = int.Parse(cellParts[1]);

                                        // Set cell state
                                        Cell cell = chunk.GetCell(x, y, z);
                                        cell.Collapse(state);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return chunk;
        }
    }

    /// <summary>
    /// Enum representing the current status of a chunk
    /// </summary>
    public enum ChunkStatus
    {
        Uninitialized,
        Initialized,
        PartiallyCollapsed,
        MostlyCollapsed,
        FullyCollapsed,
        Optimized
    }
}