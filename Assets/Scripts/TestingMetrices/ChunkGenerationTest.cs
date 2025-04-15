using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using WFC.Boundary;
using WFC.Core;
using WFC.Generation;
using WFC.MarchingCubes;

namespace WFC.Metrics
{
    #region Chunk Generation Test

    /// <summary>
    /// Tests the performance of the WFC chunk generation and collapse process
    /// </summary>
    public class ChunkGenerationTest : IWFCTestCase
    {
        public string TestName => "Chunk Generation Performance";
        public string TestDescription => "Measures processing time, memory usage, and collapse efficiency for chunks of different sizes";

        // Dependencies
        private WFCGenerator wfcGenerator;

        // Test configuration
        private int[] chunkSizes = new int[] { 8, 16, 32, 64 };

        // Test results
        private List<ChunkGenerationResult> results = new List<ChunkGenerationResult>();

        public void Initialize(ReferenceLocator references)
        {
            wfcGenerator = references.WfcGenerator;
        }

        public bool CanRun()
        {
            // Add detailed logging to diagnose the issue
            if (wfcGenerator == null)
            {
                Debug.LogWarning($"{TestName}: wfcGenerator is null");
            }
            return wfcGenerator != null;
        }

        public IEnumerator RunTest()
        {
            Debug.Log($"Starting {TestName} test...");
            results.Clear();

            // Run test for each chunk size
            foreach (int chunkSize in chunkSizes)
            {
                yield return RunChunkSizeTest(chunkSize);

                // Brief pause between tests
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log($"{TestName} test completed.");
        }

        private IEnumerator RunChunkSizeTest(int chunkSize)
        {
            Debug.Log($"Testing chunk size: {chunkSize}x{chunkSize}x{chunkSize}");

            // Create test chunk
            Vector3Int chunkPos = new Vector3Int(0, 0, 0);
            Chunk chunk = new Chunk(chunkPos, chunkSize);

            // Initialize with all possible states
            var possibleStates = Enumerable.Range(0, wfcGenerator.MaxCellStates);
            chunk.InitializeCells(possibleStates);

            // Measure memory before
            float memoryBefore = (float)System.GC.GetTotalMemory(true) / (1024 * 1024);

            // Prepare counters
            int propagationEvents = 0;
            int iterations = 0;

            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // Collapse chunk (run up to 1000 iterations to prevent infinite loops)
            bool madeProgress = true;
            while (madeProgress && iterations < 1000)
            {
                madeProgress = CollapseNextCell(chunk, ref propagationEvents);
                iterations++;

                // Yield every few iterations to prevent freezing
                if (iterations % 10 == 0)
                    yield return null;
            }

            // Stop timing
            stopwatch.Stop();

            // Measure memory after
            float memoryAfter = (float)System.GC.GetTotalMemory(false) / (1024 * 1024);
            float memoryUsage = memoryAfter - memoryBefore;

            // Calculate collapsed percentage
            float collapsedPercentage = CalculateCollapsedPercentage(chunk) * 100f;

            // Store result
            ChunkGenerationResult result = new ChunkGenerationResult
            {
                ChunkSize = chunkSize,
                ProcessingTime = stopwatch.ElapsedMilliseconds,
                MemoryUsage = memoryUsage,
                CellsCollapsedPercent = collapsedPercentage,
                PropagationEvents = propagationEvents,
                IterationsRequired = iterations
            };

            results.Add(result);

            Debug.Log($"Chunk size {chunkSize} test completed: " +
                     $"{stopwatch.ElapsedMilliseconds:F2}ms, " +
                     $"{collapsedPercentage:F2}% cells collapsed");
        }

        private bool CollapseNextCell(Chunk chunk, ref int propagationEvents)
        {
            // Find the cell with lowest entropy
            Cell cellToCollapse = null;
            int lowestEntropy = int.MaxValue;

            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);

                        // Skip already collapsed cells
                        if (cell.CollapsedState.HasValue)
                            continue;

                        // Skip cells with only one state (will be collapsed in propagation)
                        if (cell.PossibleStates.Count <= 1)
                            continue;

                        if (cell.Entropy < lowestEntropy)
                        {
                            lowestEntropy = cell.Entropy;
                            cellToCollapse = cell;
                        }
                    }
                }
            }

            // If found a cell, collapse it
            if (cellToCollapse != null)
            {
                int[] possibleStates = cellToCollapse.PossibleStates.ToArray();
                int randomState = possibleStates[UnityEngine.Random.Range(0, possibleStates.Length)];

                cellToCollapse.Collapse(randomState);

                // Count as a propagation event
                propagationEvents++;

                // Simple propagation to neighboring cells
                PropagateCollapse(cellToCollapse, chunk, ref propagationEvents);

                return true;
            }

            return false;
        }

        private void PropagateCollapse(Cell cell, Chunk chunk, ref int propagationEvents)
        {
            // Find cell position in chunk
            Vector3Int? cellPos = FindCellPosition(cell, chunk);
            if (!cellPos.HasValue)
                return;

            int state = cell.CollapsedState.Value;

            // Check each direction
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                // Calculate neighbor position
                Vector3Int offset = dir.ToVector3Int();
                Vector3Int neighborPos = cellPos.Value + offset;

                // Check if neighbor is within chunk
                if (neighborPos.x >= 0 && neighborPos.x < chunk.Size &&
                    neighborPos.y >= 0 && neighborPos.y < chunk.Size &&
                    neighborPos.z >= 0 && neighborPos.z < chunk.Size)
                {
                    // Get neighbor cell and apply constraint
                    Cell neighbor = chunk.GetCell(neighborPos.x, neighborPos.y, neighborPos.z);
                    bool changed = ApplyConstraint(neighbor, state, dir, chunk);

                    // If neighbor changed, count as propagation event
                    if (changed)
                        propagationEvents++;
                }
            }
        }

        private bool ApplyConstraint(Cell cell, int constraintState, Direction direction, Chunk chunk)
        {
            // Skip if already collapsed
            if (cell.CollapsedState.HasValue)
                return false;

            // Store old states for checking if changed
            HashSet<int> oldStates = new HashSet<int>(cell.PossibleStates);

            // Find compatible states based on adjacency rules
            HashSet<int> compatibleStates = new HashSet<int>();

            foreach (int targetState in cell.PossibleStates)
            {
                // Check if this state can be adjacent to the source state
                bool isCompatible = false;

                if (wfcGenerator != null)
                {
                    isCompatible = wfcGenerator.AreStatesCompatible(targetState, constraintState, direction);
                }
                else
                {
                    // Simple compatibility rule: states can be adjacent to themselves and neighbors
                    isCompatible = (targetState == constraintState ||
                                   Math.Abs(targetState - constraintState) <= 1);
                }

                if (isCompatible)
                {
                    compatibleStates.Add(targetState);
                }
            }

            // Update possible states if needed
            if (compatibleStates.Count > 0 && !compatibleStates.SetEquals(oldStates))
            {
                cell.SetPossibleStates(compatibleStates);
                return true;
            }

            return false;
        }

        private Vector3Int? FindCellPosition(Cell cell, Chunk chunk)
        {
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int y = 0; y < chunk.Size; y++)
                {
                    for (int z = 0; z < chunk.Size; z++)
                    {
                        if (chunk.GetCell(x, y, z) == cell)
                        {
                            return new Vector3Int(x, y, z);
                        }
                    }
                }
            }

            return null;
        }

        private float CalculateCollapsedPercentage(Chunk chunk)
        {
            if (chunk == null) return 0f;

            int totalCells = chunk.Size * chunk.Size * chunk.Size;
            int collapsedCells = 0;

            // Sample cells to estimate collapse percentage (checking every cell could be expensive)
            int sampleSize = Mathf.Min(27, chunk.Size * chunk.Size * chunk.Size);
            int samplesPerDimension = Mathf.CeilToInt(Mathf.Pow(sampleSize, 1f / 3f));
            float step = chunk.Size / (float)samplesPerDimension;

            for (int x = 0; x < samplesPerDimension; x++)
            {
                for (int y = 0; y < samplesPerDimension; y++)
                {
                    for (int z = 0; z < samplesPerDimension; z++)
                    {
                        int sampleX = Mathf.Min(chunk.Size - 1, Mathf.FloorToInt(x * step));
                        int sampleY = Mathf.Min(chunk.Size - 1, Mathf.FloorToInt(y * step));
                        int sampleZ = Mathf.Min(chunk.Size - 1, Mathf.FloorToInt(z * step));

                        Cell cell = chunk.GetCell(sampleX, sampleY, sampleZ);
                        if (cell != null && cell.CollapsedState.HasValue)
                        {
                            collapsedCells++;
                        }
                    }
                }
            }

            return (float)collapsedCells / sampleSize;
        }

        public ITestResult GetResults()
        {
            return new ChunkGenerationResults(results);
        }

        public void ExportResults(string filePath)
        {
            if (results.Count == 0) return;

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("ChunkSize,ProcessingTime(ms),MemoryUsage(MB),CellsCollapsedPercent,PropagationEvents,IterationsRequired");

                foreach (var result in results)
                {
                    writer.WriteLine($"{result.ChunkSize},{result.ProcessingTime:F2},{result.MemoryUsage:F2}," +
                                    $"{result.CellsCollapsedPercent:F2},{result.PropagationEvents},{result.IterationsRequired}");
                }
            }

            Debug.Log($"Chunk Generation results exported to: {filePath}");
        }

        public void Cleanup()
        {
            // Force garbage collection to clean up test chunks
            System.GC.Collect();
        }
    }

    public class ChunkGenerationResult
    {
        public int ChunkSize { get; set; }
        public float ProcessingTime { get; set; }
        public float MemoryUsage { get; set; }
        public float CellsCollapsedPercent { get; set; }
        public int PropagationEvents { get; set; }
        public int IterationsRequired { get; set; }
    }

    public class ChunkGenerationResults : ITestResult
    {
        private List<ChunkGenerationResult> results;

        public ChunkGenerationResults(List<ChunkGenerationResult> results)
        {
            this.results = new List<ChunkGenerationResult>(results);
        }

        public string TestName => "Chunk Generation Performance";

        public string GetCsvHeader()
        {
            return "ChunkSize,ProcessingTime(ms),MemoryUsage(MB),CellsCollapsedPercent,PropagationEvents,IterationsRequired";
        }

        public IEnumerable<string> GetCsvData()
        {
            foreach (var result in results)
            {
                yield return $"{result.ChunkSize},{result.ProcessingTime:F2},{result.MemoryUsage:F2}," +
                           $"{result.CellsCollapsedPercent:F2},{result.PropagationEvents},{result.IterationsRequired}";
            }
        }

        public bool HasResults()
        {
            return results != null && results.Count > 0;
        }
    }

    #endregion

    #region Boundary Coherence Test

    /// <summary>
    /// Tests the performance and coherence of chunk boundaries
    /// </summary>
    public class BoundaryCoherenceTest : IWFCTestCase
    {
        public string TestName => "Boundary Coherence Performance";
        public string TestDescription => "Measures boundary updates, conflicts, and coherence between chunks";

        // Dependencies
        private WFCGenerator wfcGenerator;
        private BoundaryBufferManager boundaryManager;

        // Test configuration
        private Vector3Int[] worldSizes = new Vector3Int[] {
            new Vector3Int(2, 1, 1),  // 2 chunks
            new Vector3Int(2, 2, 1),  // 4 chunks
            new Vector3Int(3, 3, 1),  // 9 chunks
            new Vector3Int(4, 4, 1)   // 16 chunks
        };
        private int testChunkSize = 16;

        // Test results
        private List<BoundaryCoherenceResult> results = new List<BoundaryCoherenceResult>();

        public void Initialize(ReferenceLocator references)
        {
            wfcGenerator = references.WfcGenerator;
            boundaryManager = references.BoundaryManager;
        }

        public bool CanRun()
        {
            return wfcGenerator != null;
        }

        public IEnumerator RunTest()
        {
            Debug.Log($"Starting {TestName} test...");
            results.Clear();

            // Run test for each world size
            foreach (var worldSize in worldSizes)
            {
                yield return RunBoundaryTest(worldSize);

                // Brief pause between tests
                yield return new WaitForSeconds(1.0f);
            }

            Debug.Log($"{TestName} test completed.");
        }

        private IEnumerator RunBoundaryTest(Vector3Int worldSize)
        {
            Debug.Log($"Testing boundary coherence for world size: {worldSize}");

            // Create test chunks
            Dictionary<Vector3Int, Chunk> testChunks = new Dictionary<Vector3Int, Chunk>();

            // Create chunks
            for (int x = 0; x < worldSize.x; x++)
            {
                for (int y = 0; y < worldSize.y; y++)
                {
                    for (int z = 0; z < worldSize.z; z++)
                    {
                        Vector3Int chunkPos = new Vector3Int(x, y, z);
                        Chunk chunk = new Chunk(chunkPos, testChunkSize);

                        // Initialize with all possible states
                        var possibleStates = Enumerable.Range(0, wfcGenerator.MaxCellStates);
                        chunk.InitializeCells(possibleStates);

                        testChunks[chunkPos] = chunk;
                    }
                }
            }

            // Connect neighboring chunks
            foreach (var entry in testChunks)
            {
                Vector3Int pos = entry.Key;
                Chunk chunk = entry.Value;

                foreach (Direction dir in Enum.GetValues(typeof(Direction)))
                {
                    Vector3Int neighborPos = pos + dir.ToVector3Int();
                    if (testChunks.TryGetValue(neighborPos, out Chunk neighbor))
                    {
                        // Connect chunks both ways
                        chunk.Neighbors[dir] = neighbor;
                        neighbor.Neighbors[dir.GetOpposite()] = chunk;
                    }
                }
            }

            // Create and initialize boundary buffers
            foreach (var chunk in testChunks.Values)
            {
                InitializeBoundaryBuffers(chunk);
                yield return null; // Yield to prevent freezing
            }

            // Create a test boundary manager
            MockBoundaryManager testBoundaryManager = new MockBoundaryManager();

            // Metrics to track
            int boundaryUpdates = 0;
            int bufferSynchronizations = 0;
            int conflictsDetected = 0;
            int conflictsResolved = 0;

            // Partially collapse chunks to create boundaries
            foreach (var chunk in testChunks.Values)
            {
                // Collapse ~20% of cells randomly
                int cellsToCollapse = (chunk.Size * chunk.Size * chunk.Size) / 5;
                for (int i = 0; i < cellsToCollapse; i++)
                {
                    int x = UnityEngine.Random.Range(0, chunk.Size);
                    int y = UnityEngine.Random.Range(0, chunk.Size);
                    int z = UnityEngine.Random.Range(0, chunk.Size);

                    Cell cell = chunk.GetCell(x, y, z);
                    if (cell != null && !cell.CollapsedState.HasValue)
                    {
                        // Collapse to a random state
                        int[] possibleStates = cell.PossibleStates.ToArray();
                        int randomState = possibleStates[UnityEngine.Random.Range(0, possibleStates.Length)];
                        cell.Collapse(randomState);

                        // If this is a boundary cell, update boundary
                        if (cell.IsBoundary && cell.BoundaryDirection.HasValue)
                        {
                            boundaryUpdates++;
                            bufferSynchronizations++;

                            // Handle boundary conflicts
                            if (IsBoundaryConflict(cell, chunk))
                            {
                                conflictsDetected++;

                                // Try to resolve conflict
                                if (ResolveBoundaryConflict(cell, chunk))
                                {
                                    conflictsResolved++;
                                }
                            }
                        }
                    }
                }

                // Yield every few chunks to prevent freezing
                yield return null;
            }

            // Calculate boundary coherence score
            float coherenceScore = CalculateBoundaryCoherenceScore(testChunks.Values);

            // Store results
            BoundaryCoherenceResult result = new BoundaryCoherenceResult
            {
                WorldSize = worldSize,
                BoundaryUpdates = boundaryUpdates,
                BufferSynchronizations = bufferSynchronizations,
                ConflictsDetected = conflictsDetected,
                ConflictsResolved = conflictsResolved,
                CoherenceScore = coherenceScore
            };

            results.Add(result);

            Debug.Log($"Boundary coherence test for world size {worldSize} completed: " +
                     $"{coherenceScore:F2}% coherence");

            // Clean up test chunks
            testChunks.Clear();
            System.GC.Collect();
        }

        private void InitializeBoundaryBuffers(Chunk chunk)
        {
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                if (!chunk.Neighbors.ContainsKey(dir))
                    continue;

                Chunk neighbor = chunk.Neighbors[dir];

                // Create buffer for this boundary
                BoundaryBuffer buffer = new BoundaryBuffer(dir, chunk);
                buffer.AdjacentChunk = neighbor;

                // Get boundary cells based on direction
                List<Cell> boundaryCells = GetBoundaryCells(chunk, dir);
                buffer.BoundaryCells = boundaryCells;

                // Mark all boundary cells
                foreach (var cell in boundaryCells)
                {
                    cell.IsBoundary = true;
                    cell.BoundaryDirection = dir;
                }

                // Create buffer cells
                List<Cell> bufferCells = new List<Cell>();
                for (int i = 0; i < boundaryCells.Count; i++)
                {
                    Cell bufferCell = new Cell(new Vector3Int(-1, -1, -1), boundaryCells[i].PossibleStates);
                    bufferCells.Add(bufferCell);
                }
                buffer.BufferCells = bufferCells;

                // Add to chunk's boundary buffers
                chunk.BoundaryBuffers[dir] = buffer;
            }

            // Synchronize buffers after initialization
            foreach (var buffer in chunk.BoundaryBuffers.Values)
            {
                SynchronizeBuffer(buffer);
            }
        }

        private List<Cell> GetBoundaryCells(Chunk chunk, Direction direction)
        {
            List<Cell> cells = new List<Cell>();
            int size = chunk.Size;

            // Based on direction, get cells at the boundary face
            switch (direction)
            {
                case Direction.Left: // X = 0
                    for (int y = 0; y < size; y++)
                        for (int z = 0; z < size; z++)
                            cells.Add(chunk.GetCell(0, y, z));
                    break;

                case Direction.Right: // X = size-1
                    for (int y = 0; y < size; y++)
                        for (int z = 0; z < size; z++)
                            cells.Add(chunk.GetCell(size - 1, y, z));
                    break;

                case Direction.Down: // Y = 0
                    for (int x = 0; x < size; x++)
                        for (int z = 0; z < size; z++)
                            cells.Add(chunk.GetCell(x, 0, z));
                    break;

                case Direction.Up: // Y = size-1
                    for (int x = 0; x < size; x++)
                        for (int z = 0; z < size; z++)
                            cells.Add(chunk.GetCell(x, size - 1, z));
                    break;

                case Direction.Back: // Z = 0
                    for (int x = 0; x < size; x++)
                        for (int y = 0; y < size; y++)
                            cells.Add(chunk.GetCell(x, y, 0));
                    break;

                case Direction.Forward: // Z = size-1
                    for (int x = 0; x < size; x++)
                        for (int y = 0; y < size; y++)
                            cells.Add(chunk.GetCell(x, y, size - 1));
                    break;
            }

            return cells;
        }

        private void SynchronizeBuffer(BoundaryBuffer buffer)
        {
            if (buffer.AdjacentChunk == null)
                return;

            Direction oppositeDir = buffer.Direction.GetOpposite();

            // Get adjacent buffer
            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return;

            // For each cell at the boundary
            for (int i = 0; i < buffer.BoundaryCells.Count && i < adjacentBuffer.BoundaryCells.Count; i++)
            {
                Cell cell1 = buffer.BoundaryCells[i];
                Cell cell2 = adjacentBuffer.BoundaryCells[i];

                // Update buffer cells to reflect adjacent boundary cells
                HashSet<int> adjacentStates = new HashSet<int>(cell2.PossibleStates);
                buffer.BufferCells[i].SetPossibleStates(adjacentStates);

                HashSet<int> localStates = new HashSet<int>(cell1.PossibleStates);
                adjacentBuffer.BufferCells[i].SetPossibleStates(localStates);
            }
        }

        private bool IsBoundaryConflict(Cell cell, Chunk chunk)
        {
            if (!cell.IsBoundary || !cell.BoundaryDirection.HasValue || !cell.CollapsedState.HasValue)
                return false;

            Direction dir = cell.BoundaryDirection.Value;

            if (!chunk.BoundaryBuffers.TryGetValue(dir, out BoundaryBuffer buffer) ||
                buffer.AdjacentChunk == null)
                return false;

            // Find this cell in the boundary array
            int index = buffer.BoundaryCells.IndexOf(cell);
            if (index == -1)
                return false;

            Direction oppositeDir = dir.GetOpposite();

            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return false;

            // Check if adjacent cell is collapsed and incompatible
            if (index < adjacentBuffer.BoundaryCells.Count)
            {
                Cell adjacentCell = adjacentBuffer.BoundaryCells[index];
                if (adjacentCell.CollapsedState.HasValue)
                {
                    bool compatible = wfcGenerator != null ?
                        wfcGenerator.AreStatesCompatible(cell.CollapsedState.Value, adjacentCell.CollapsedState.Value, dir) :
                        (cell.CollapsedState.Value == adjacentCell.CollapsedState.Value ||
                         Math.Abs(cell.CollapsedState.Value - adjacentCell.CollapsedState.Value) <= 1);

                    return !compatible;
                }
            }

            return false;
        }

        private bool ResolveBoundaryConflict(Cell cell, Chunk chunk)
        {
            if (!cell.IsBoundary || !cell.BoundaryDirection.HasValue || !cell.CollapsedState.HasValue)
                return false;

            Direction dir = cell.BoundaryDirection.Value;

            if (!chunk.BoundaryBuffers.TryGetValue(dir, out BoundaryBuffer buffer) ||
                buffer.AdjacentChunk == null)
                return false;

            // Find this cell in the boundary array
            int index = buffer.BoundaryCells.IndexOf(cell);
            if (index == -1)
                return false;

            Direction oppositeDir = dir.GetOpposite();

            if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                return false;

            // Reset adjacent cell to compatible state
            if (index < adjacentBuffer.BoundaryCells.Count)
            {
                Cell adjacentCell = adjacentBuffer.BoundaryCells[index];
                if (adjacentCell.CollapsedState.HasValue)
                {
                    // Find a compatible state
                    int compatibleState = cell.CollapsedState.Value;
                    adjacentCell.Collapse(compatibleState);
                    return true;
                }
            }

            return false;
        }

        private float CalculateBoundaryCoherenceScore(IEnumerable<Chunk> chunks)
        {
            int totalBoundaryCells = 0;
            int compatibleCells = 0;

            foreach (var chunk in chunks)
            {
                foreach (var bufferEntry in chunk.BoundaryBuffers)
                {
                    BoundaryBuffer buffer = bufferEntry.Value;
                    if (buffer.AdjacentChunk == null)
                        continue;

                    Direction oppositeDir = buffer.Direction.GetOpposite();

                    if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                        continue;

                    for (int i = 0; i < buffer.BoundaryCells.Count && i < adjacentBuffer.BoundaryCells.Count; i++)
                    {
                        Cell cell1 = buffer.BoundaryCells[i];
                        Cell cell2 = adjacentBuffer.BoundaryCells[i];

                        if (cell1.CollapsedState.HasValue && cell2.CollapsedState.HasValue)
                        {
                            totalBoundaryCells++;

                            bool compatible = wfcGenerator != null ?
                                wfcGenerator.AreStatesCompatible(cell1.CollapsedState.Value, cell2.CollapsedState.Value, buffer.Direction) :
                                (cell1.CollapsedState.Value == cell2.CollapsedState.Value ||
                                 Math.Abs(cell1.CollapsedState.Value - cell2.CollapsedState.Value) <= 1);

                            if (compatible)
                                compatibleCells++;
                        }
                    }
                }
            }

            return totalBoundaryCells > 0 ? (float)compatibleCells / totalBoundaryCells * 100f : 100f;
        }

        public ITestResult GetResults()
        {
            return new BoundaryCoherenceResults(results);
        }

        public void ExportResults(string filePath)
        {
            if (results.Count == 0) return;

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("WorldSize,BoundaryUpdates,BufferSynchronizations,ConflictsDetected,ConflictsResolved,CoherenceScore");

                foreach (var result in results)
                {
                    writer.WriteLine($"{result.WorldSize.x}x{result.WorldSize.y}x{result.WorldSize.z}," +
                                    $"{result.BoundaryUpdates},{result.BufferSynchronizations}," +
                                    $"{result.ConflictsDetected},{result.ConflictsResolved},{result.CoherenceScore:F2}");
                }
            }

            Debug.Log($"Boundary Coherence results exported to: {filePath}");
        }

        public void Cleanup()
        {
            // Force garbage collection to clean up test chunks
            System.GC.Collect();
        }

        // Simple mock implementation for testing
        private class MockBoundaryManager : IBoundaryCompatibilityChecker, IPropagationHandler, IChunkProvider
        {
            public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
            {
                // Simple compatibility rule
                return stateA == stateB || Math.Abs(stateA - stateB) <= 1;
            }

            public void AddPropagationEvent(PropagationEvent evt)
            {
                // No-op for testing
            }

            public Dictionary<Vector3Int, Chunk> GetChunks()
            {
                return new Dictionary<Vector3Int, Chunk>();
            }
        }
    }

    public class BoundaryCoherenceResult
    {
        public Vector3Int WorldSize { get; set; }
        public int BoundaryUpdates { get; set; }
        public int BufferSynchronizations { get; set; }
        public int ConflictsDetected { get; set; }
        public int ConflictsResolved { get; set; }
        public float CoherenceScore { get; set; }
    }

    public class BoundaryCoherenceResults : ITestResult
    {
        private List<BoundaryCoherenceResult> results;

        public BoundaryCoherenceResults(List<BoundaryCoherenceResult> results)
        {
            this.results = new List<BoundaryCoherenceResult>(results);
        }

        public string TestName => "Boundary Coherence Performance";

        public string GetCsvHeader()
        {
            return "WorldSize,BoundaryUpdates,BufferSynchronizations,ConflictsDetected,ConflictsResolved,CoherenceScore";
        }

        public IEnumerable<string> GetCsvData()
        {
            foreach (var result in results)
            {
                yield return $"{result.WorldSize.x}x{result.WorldSize.y}x{result.WorldSize.z}," +
                           $"{result.BoundaryUpdates},{result.BufferSynchronizations}," +
                           $"{result.ConflictsDetected},{result.ConflictsResolved},{result.CoherenceScore:F2}";
            }
        }

        public bool HasResults()
        {
            return results != null && results.Count > 0;
        }
    }

    #endregion

    #region Mesh Generation Test

    /// <summary>
    /// Tests the performance of mesh generation from WFC cells
    /// </summary>
    public class MeshGenerationTest : IWFCTestCase
    {
        public string TestName => "Mesh Generation Performance";
        public string TestDescription => "Measures density field generation time, marching cubes algorithm time, and mesh metrics";

        // Dependencies
        private WFCGenerator wfcGenerator;
        private MeshGenerator meshGenerator;
        private DensityFieldGenerator densityGenerator;

        // Test configuration
        private int[] chunkSizes = new int[] { 8, 16, 32, 64 };

        // Test results
        private List<MeshGenerationResult> results = new List<MeshGenerationResult>();

        public void Initialize(ReferenceLocator references)
        {
            wfcGenerator = references.WfcGenerator;
            meshGenerator = references.MeshGenerator;
            densityGenerator = references.DensityGenerator;

            // Create a new density generator if none is available
            if (densityGenerator == null)
            {
                densityGenerator = new DensityFieldGenerator();
            }
        }

        public bool CanRun()
        {
            return wfcGenerator != null;
        }

        public IEnumerator RunTest()
        {
            Debug.Log($"Starting {TestName} test...");
            results.Clear();

            // Run test for each chunk size
            foreach (int chunkSize in chunkSizes)
            {
                yield return RunMeshGenerationTest(chunkSize);

                // Brief pause between tests
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log($"{TestName} test completed.");
        }

        private IEnumerator RunMeshGenerationTest(int chunkSize)
        {
            Debug.Log($"Testing mesh generation for size: {chunkSize}x{chunkSize}x{chunkSize}");

            // Create test chunk
            Vector3Int chunkPos = new Vector3Int(0, 0, 0);
            Chunk chunk = new Chunk(chunkPos, chunkSize);

            // Initialize with all possible states
            var possibleStates = Enumerable.Range(0, wfcGenerator.MaxCellStates);
            chunk.InitializeCells(possibleStates);

            // Create simple terrain pattern (ground with air above it)
            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    for (int y = 0; y < chunkSize; y++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);

                        // Bottom half is ground, top half is air
                        if (y < chunkSize / 2)
                            cell.Collapse(1); // Ground
                        else
                            cell.Collapse(0); // Air
                    }
                }
            }

            // Add some noise to make the terrain more interesting
            int noiseCount = chunkSize * chunkSize / 4;
            for (int i = 0; i < noiseCount; i++)
            {
                int x = UnityEngine.Random.Range(0, chunkSize);
                int y = UnityEngine.Random.Range(chunkSize / 3, 2 * chunkSize / 3);
                int z = UnityEngine.Random.Range(0, chunkSize);

                Cell cell = chunk.GetCell(x, y, z);

                // Change state
                cell.Collapse(y < chunkSize / 2 ? 0 : 1);
            }

            // Mark chunk as fully collapsed
            chunk.IsFullyCollapsed = true;

            // Create marching cubes generator if needed
            MarchingCubesGenerator marchingCubes = new MarchingCubesGenerator();

            // Memory before
            float memoryBefore = (float)System.GC.GetTotalMemory(true) / (1024 * 1024);

            // Measure density field generation time
            Stopwatch densityStopwatch = new Stopwatch();
            densityStopwatch.Start();

            float[,,] densityField = densityGenerator.GenerateDensityField(chunk);

            densityStopwatch.Stop();
            float densityTime = densityStopwatch.ElapsedMilliseconds;

            // Yield to prevent freezing
            yield return null;

            // Measure marching cubes time
            Stopwatch marchingCubesStopwatch = new Stopwatch();
            marchingCubesStopwatch.Start();

            Mesh mesh = marchingCubes.GenerateMesh(densityField);

            marchingCubesStopwatch.Stop();
            float marchingCubesTime = marchingCubesStopwatch.ElapsedMilliseconds;

            // Memory after
            float memoryAfter = (float)System.GC.GetTotalMemory(false) / (1024 * 1024);
            float memoryUsage = memoryAfter - memoryBefore;

            // Store results
            MeshGenerationResult result = new MeshGenerationResult
            {
                ChunkSize = chunkSize,
                DensityFieldGenerationTime = densityTime,
                MarchingCubesTime = marchingCubesTime,
                TotalMeshTime = densityTime + marchingCubesTime,
                Vertices = mesh.vertexCount,
                Triangles = mesh.triangles.Length / 3,
                MemoryUsage = memoryUsage
            };

            results.Add(result);

            Debug.Log($"Mesh generation test for size {chunkSize} completed: " +
                     $"{densityTime + marchingCubesTime:F2}ms, " +
                     $"{mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles");
        }

        public ITestResult GetResults()
        {
            return new MeshGenerationResults(results);
        }

        public void ExportResults(string filePath)
        {
            if (results.Count == 0) return;

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("ChunkSize,DensityFieldGeneration(ms),MarchingCubes(ms),TotalMeshTime(ms),Vertices,Triangles,MemoryUsage(MB)");

                foreach (var result in results)
                {
                    writer.WriteLine($"{result.ChunkSize},{result.DensityFieldGenerationTime:F2}," +
                                    $"{result.MarchingCubesTime:F2},{result.TotalMeshTime:F2}," +
                                    $"{result.Vertices},{result.Triangles},{result.MemoryUsage:F2}");
                }
            }

            Debug.Log($"Mesh Generation results exported to: {filePath}");
        }

        public void Cleanup()
        {
            // Force garbage collection to clean up test chunks and meshes
            System.GC.Collect();
        }
    }

    public class MeshGenerationResult
    {
        public int ChunkSize { get; set; }
        public float DensityFieldGenerationTime { get; set; }
        public float MarchingCubesTime { get; set; }
        public float TotalMeshTime { get; set; }
        public int Vertices { get; set; }
        public int Triangles { get; set; }
        public float MemoryUsage { get; set; }
    }

    public class MeshGenerationResults : ITestResult
    {
        private List<MeshGenerationResult> results;

        public MeshGenerationResults(List<MeshGenerationResult> results)
        {
            this.results = new List<MeshGenerationResult>(results);
        }

        public string TestName => "Mesh Generation Performance";

        public string GetCsvHeader()
        {
            return "ChunkSize,DensityFieldGeneration(ms),MarchingCubes(ms),TotalMeshTime(ms),Vertices,Triangles,MemoryUsage(MB)";
        }

        public IEnumerable<string> GetCsvData()
        {
            foreach (var result in results)
            {
                yield return $"{result.ChunkSize},{result.DensityFieldGenerationTime:F2}," +
                           $"{result.MarchingCubesTime:F2},{result.TotalMeshTime:F2}," +
                           $"{result.Vertices},{result.Triangles},{result.MemoryUsage:F2}";
            }
        }

        public bool HasResults()
        {
            return results != null && results.Count > 0;
        }
    }
    #endregion
}
