//// Assets/Scripts/WFC/Testing/WFCTestController.cs
//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using WFC.Core;
//using WFC.Generation;
//using WFC.Boundary;
//using Utils;
//using System.Linq;

//namespace WFC.Testing
//{
//    public class WFCTestController : MonoBehaviour, WFC.Boundary.IWFCAlgorithm
//    {
//        private HierarchicalConstraintSystem hierarchicalConstraints;

//        [Header("Test Configuration")]
//        [SerializeField] private int worldSizeX = 2;
//        [SerializeField] private int worldSizeY = 2;
//        [SerializeField] private int worldSizeZ = 1; // Start with 2D (z=1)
//        [SerializeField] private int chunkSize = 8;
//        [SerializeField] private int maxStates = 7;

//        [Header("Visualization")]
//        [SerializeField] private float cellSize = 1.0f;
//        [SerializeField] private Material[] stateMaterials;
//        [SerializeField] private Material uncollapsedMaterial;
//        [SerializeField] private Material boundaryMaterial;
//        [SerializeField] private bool showUncollapsedCells = true;
//        [SerializeField] private bool highlightBoundaries = true;

//        [Header("Runtime Control")]
//        [SerializeField] private bool autoRun = false;
//        [SerializeField] private float stepDelay = 0.1f;
//        [SerializeField] private int stepsPerUpdate = 1;

//        [Header("Testing Controls")]
//        [SerializeField] private int randomSeed = 0;
//        [SerializeField] private bool useRandomSeed = true;

//        // Add to WFCTestController class
//        [Header("Hierarchical Constraints")]
//        [SerializeField] private HierarchicalConstraintSystem constraintSystem;
//        private bool usingConstraints = true;

//        [SerializeField] private WFCConfiguration config;


//        // Define state names for better understanding
//        private enum TileState
//        {
//            Empty = 0,
//            Ground = 1,
//            Grass = 2,
//            Water = 3,
//            Rock = 4,
//            Sand = 5,
//            Tree = 6
//        }

//        // Internal state
//        private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
//        private PriorityQueue<PropagationEvent, float> propagationQueue = new PriorityQueue<PropagationEvent, float>();
//        private bool[,,] adjacencyRules;
//        private BoundaryBufferManager boundaryManager;
//        private Dictionary<Vector3Int, GameObject> cellVisualizers = new Dictionary<Vector3Int, GameObject>();
//        private int generationStep = 0;

//        private void Awake()
//        {
//            InitializeHierarchicalConstraints();
//        }
//        private void Start()
//        {
//            // random number generator with seed
//            if (!useRandomSeed)
//            {
//                UnityEngine.Random.InitState(randomSeed);
//                Debug.Log($"Using fixed random seed: {randomSeed}");
//            }
//            else
//            {
//                randomSeed = System.Environment.TickCount;
//                UnityEngine.Random.InitState(randomSeed);
//                Debug.Log($"Using random seed: {randomSeed}");
//            }

//            // Setup the WFC environment
//            SetupWorld();

//            // Create visualizations
//            CreateVisualizations();

//            // Start generation coroutine if auto-run is enabled
//            if (autoRun)
//            {
//                StartCoroutine(RunGenerationProcess());
//            }
//        }

//        private void Update()
//        {
//            // Allow manual stepping through the algorithm
//            if (Input.GetKeyDown(KeyCode.Space))
//            {
//                RunGenerationSteps(stepsPerUpdate);
//                UpdateVisualizations();
//            }

//            // Reset the generation
//            if (Input.GetKeyDown(KeyCode.R))
//            {
//                ResetGeneration();
//            }

//            // Reset with random seed
//            if (Input.GetKeyDown(KeyCode.N))
//            {
//                ResetWithNewSeed();
//            }
//        }

//        private void InitializeHierarchicalConstraints()
//        {
//            // Create the hierarchical constraint system
//            hierarchicalConstraints = new HierarchicalConstraintSystem(chunkSize);

//            // Generate default constraints based on world configuration
//            hierarchicalConstraints.GenerateDefaultConstraints(
//                new Vector3Int(worldSizeX, worldSizeY, worldSizeZ));

//            // Precompute constraints for each chunk
//            foreach (var chunk in chunks.Values)
//            {
//                hierarchicalConstraints.PrecomputeChunkConstraints(chunk, maxStates);
//            }
//        }

//        private void ApplyHierarchicalConstraints(Cell cell, Chunk chunk)
//        {
//            if (hierarchicalConstraints != null)
//            {
//                hierarchicalConstraints.ApplyConstraintsToCell(cell, chunk, maxStates);
//            }
//        }

//        public void ResetWithNewSeed(int seed = -1)
//        {
//            // Clean up existing generation
//            ResetGeneration();

//            // Set new seed
//            if (seed < 0)
//            {
//                randomSeed = System.Environment.TickCount;
//            }
//            else
//            {
//                randomSeed = seed;
//            }

//            UnityEngine.Random.InitState(randomSeed);
//            Debug.Log($"Reset with new seed: {randomSeed}");

//            // Reinitialize
//            SetupWorld();

//            // Start generation if auto-run enabled
//            if (autoRun)
//            {
//                StartCoroutine(RunGenerationProcess());
//            }
//        }

//        private void SetupWorld()
//        {
//            // Initialize adjacency rules
//            adjacencyRules = new bool[maxStates, maxStates, 6]; // 6 directions
//            SetupAdjacencyRules();

//            // Create chunks
//            for (int x = 0; x < worldSizeX; x++)
//            {
//                for (int y = 0; y < worldSizeY; y++)
//                {
//                    for (int z = 0; z < worldSizeZ; z++)
//                    {
//                        Vector3Int chunkPos = new Vector3Int(x, y, z);
//                        Chunk chunk = new Chunk(chunkPos, chunkSize);

//                        // Initialize cells with all possible states
//                        chunk.InitializeCells(Enumerable.Range(0, maxStates));

//                        chunks.Add(chunkPos, chunk);
//                    }
//                }
//            }

//            // Connect chunk neighbors
//            ConnectChunkNeighbors();

//            // Initialize boundary buffers
//            InitializeBoundaryBuffers();

//            // Create boundary manager
//            boundaryManager = new BoundaryBufferManager(this);

//            // Synchronize buffers
//            foreach (var chunk in chunks.Values)
//            {
//                foreach (var buffer in chunk.BoundaryBuffers.Values)
//                {
//                    boundaryManager.SynchronizeBuffer(buffer);
//                }
//            }
//        }

//        private void SetupAdjacencyRules()
//        {
//            // Clear all rules
//            for (int i = 0; i < maxStates; i++)
//            {
//                for (int j = 0; j < maxStates; j++)
//                {
//                    for (int d = 0; d < 6; d++)
//                    {
//                        adjacencyRules[i, j, d] = false;
//                    }
//                }
//            }

//            // Define basic rules
//            // Empty (0) can only be next to empty
//            SetAdjacent((int)TileState.Empty, (int)TileState.Empty, true);

//            // Ground (1) can be next to ground, grass, rock
//            SetAdjacent((int)TileState.Ground, (int)TileState.Ground, true);
//            SetAdjacent((int)TileState.Ground, (int)TileState.Grass, true);
//            SetAdjacent((int)TileState.Ground, (int)TileState.Rock, true);

//            // Grass (2) can be next to ground, grass, water, sand, tree
//            SetAdjacent((int)TileState.Grass, (int)TileState.Ground, true);
//            SetAdjacent((int)TileState.Grass, (int)TileState.Grass, true);
//            SetAdjacent((int)TileState.Grass, (int)TileState.Water, true);
//            SetAdjacent((int)TileState.Grass, (int)TileState.Sand, true);
//            SetAdjacent((int)TileState.Grass, (int)TileState.Tree, true);

//            // Water (3) can be next to water, grass, sand
//            SetAdjacent((int)TileState.Water, (int)TileState.Water, true);
//            SetAdjacent((int)TileState.Water, (int)TileState.Grass, true);
//            SetAdjacent((int)TileState.Water, (int)TileState.Sand, true);

//            // Rock (4) can be next to ground, rock
//            SetAdjacent((int)TileState.Rock, (int)TileState.Ground, true);
//            SetAdjacent((int)TileState.Rock, (int)TileState.Rock, true);

//            // Sand (5) can be next to grass, water, sand
//            SetAdjacent((int)TileState.Sand, (int)TileState.Grass, true);
//            SetAdjacent((int)TileState.Sand, (int)TileState.Water, true);
//            SetAdjacent((int)TileState.Sand, (int)TileState.Sand, true);

//            // Tree (6) can be next to grass, tree
//            SetAdjacent((int)TileState.Tree, (int)TileState.Grass, true);
//            SetAdjacent((int)TileState.Tree, (int)TileState.Tree, true);
//        }

//        private void SetAdjacent(int stateA, int stateB, bool canBeAdjacent)
//        {
//            // Set for all directions
//            for (int d = 0; d < 6; d++)
//            {
//                adjacencyRules[stateA, stateB, d] = canBeAdjacent;
//                adjacencyRules[stateB, stateA, d] = canBeAdjacent;
//            }
//        }

//        private void ConnectChunkNeighbors()
//        {
//            foreach (var chunkEntry in chunks)
//            {
//                Vector3Int pos = chunkEntry.Key;
//                Chunk chunk = chunkEntry.Value;

//                // Check each direction for neighbors
//                foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
//                {
//                    Vector3Int offset = dir.ToVector3Int();
//                    Vector3Int neighborPos = pos + offset;

//                    if (chunks.TryGetValue(neighborPos, out Chunk neighbor))
//                    {
//                        chunk.Neighbors[dir] = neighbor;
//                    }
//                }
//            }
//        }

//        private void InitializeBoundaryBuffers()
//        {
//            foreach (var chunk in chunks.Values)
//            {
//                foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
//                {
//                    if (!chunk.Neighbors.ContainsKey(dir))
//                        continue;

//                    Chunk neighbor = chunk.Neighbors[dir];

//                    // Create buffer for this boundary
//                    BoundaryBuffer buffer = new BoundaryBuffer(dir, chunk);
//                    buffer.AdjacentChunk = neighbor;

//                    // Get boundary cells based on direction
//                    List<Cell> boundaryCells = GetBoundaryCells(chunk, dir);
//                    buffer.BoundaryCells = boundaryCells;

//                    // Create buffer cells
//                    buffer.BufferCells = new List<Cell>();
//                    for (int i = 0; i < boundaryCells.Count; i++)
//                    {
//                        Vector3Int pos = new Vector3Int(-1, -1, -1); // Invalid position
//                        Cell bufferCell = new Cell(pos, Enumerable.Range(0, maxStates));
//                        buffer.BufferCells.Add(bufferCell);
//                    }

//                    chunk.BoundaryBuffers[dir] = buffer;
//                }
//            }
//        }

//        private List<Cell> GetBoundaryCells(Chunk chunk, Direction direction)
//        {
//            List<Cell> cells = new List<Cell>();

//            // Handle each direction differently
//            switch (direction)
//            {
//                case Direction.Left: // -X
//                    for (int y = 0; y < chunk.Size; y++)
//                    {
//                        for (int z = 0; z < chunk.Size; z++)
//                        {
//                            cells.Add(chunk.GetCell(0, y, z));
//                        }
//                    }
//                    break;

//                case Direction.Right: // +X
//                    for (int y = 0; y < chunk.Size; y++)
//                    {
//                        for (int z = 0; z < chunk.Size; z++)
//                        {
//                            cells.Add(chunk.GetCell(chunk.Size - 1, y, z));
//                        }
//                    }
//                    break;

//                case Direction.Down: // -Y
//                    for (int x = 0; x < chunk.Size; x++)
//                    {
//                        for (int z = 0; z < chunk.Size; z++)
//                        {
//                            cells.Add(chunk.GetCell(x, 0, z));
//                        }
//                    }
//                    break;

//                case Direction.Up: // +Y
//                    for (int x = 0; x < chunk.Size; x++)
//                    {
//                        for (int z = 0; z < chunk.Size; z++)
//                        {
//                            cells.Add(chunk.GetCell(x, chunk.Size - 1, z));
//                        }
//                    }
//                    break;

//                case Direction.Back: // -Z
//                    for (int x = 0; x < chunk.Size; x++)
//                    {
//                        for (int y = 0; y < chunk.Size; y++)
//                        {
//                            cells.Add(chunk.GetCell(x, y, 0));
//                        }
//                    }
//                    break;

//                case Direction.Forward: // +Z
//                    for (int x = 0; x < chunk.Size; x++)
//                    {
//                        for (int y = 0; y < chunk.Size; y++)
//                        {
//                            cells.Add(chunk.GetCell(x, y, chunk.Size - 1));
//                        }
//                    }
//                    break;
//            }

//            return cells;
//        }

//        private void CreateVisualizations()
//        {
//            // Create parent object
//            GameObject visualizationParent = new GameObject("WFC_Visualization");
//            visualizationParent.transform.parent = transform;

//            // Create chunk visualizations
//            foreach (var chunkEntry in chunks)
//            {
//                Vector3Int chunkPos = chunkEntry.Key;
//                Chunk chunk = chunkEntry.Value;

//                GameObject chunkObject = new GameObject($"Chunk_{chunkPos.x}_{chunkPos.y}_{chunkPos.z}");
//                chunkObject.transform.parent = visualizationParent.transform;

//                // Set position
//                chunkObject.transform.position = new Vector3(
//                    chunkPos.x * chunk.Size * cellSize,
//                    chunkPos.y * chunk.Size * cellSize,
//                    chunkPos.z * chunk.Size * cellSize
//                );

//                // Create cells
//                for (int x = 0; x < chunk.Size; x++)
//                {
//                    for (int y = 0; y < chunk.Size; y++)
//                    {
//                        for (int z = 0; z < chunk.Size; z++)
//                        {
//                            Cell cell = chunk.GetCell(x, y, z);
//                            CreateCellVisualization(cell, chunkPos, new Vector3Int(x, y, z), chunkObject.transform);
//                        }
//                    }
//                }
//            }
//        }

//        private void CreateCellVisualization(Cell cell, Vector3Int chunkPos, Vector3Int localPos, Transform parent)
//        {
//            // Create a unique ID for this cell
//            Vector3Int globalPos = new Vector3Int(
//                chunkPos.x * chunkSize + localPos.x,
//                chunkPos.y * chunkSize + localPos.y,
//                chunkPos.z * chunkSize + localPos.z
//            );

//            // Create visual object
//            GameObject cellObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
//            cellObject.name = $"Cell_{localPos.x}_{localPos.y}_{localPos.z}";
//            cellObject.transform.parent = parent;

//            // Position correctly
//            cellObject.transform.localPosition = new Vector3(
//                localPos.x * cellSize,
//                localPos.y * cellSize,
//                localPos.z * cellSize
//            );
//            cellObject.transform.localScale = Vector3.one * cellSize * 0.9f;

//            // Set material based on cell state
//            Renderer renderer = cellObject.GetComponent<Renderer>();
//            UpdateCellVisualization(cell, cellObject);

//            // Add boundary marker if needed
//            if (highlightBoundaries && cell.IsBoundary)
//            {
//                GameObject boundaryMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
//                boundaryMarker.name = "BoundaryMarker";
//                boundaryMarker.transform.parent = cellObject.transform;
//                boundaryMarker.transform.localPosition = Vector3.zero;
//                boundaryMarker.transform.localScale = Vector3.one * 0.2f;

//                Renderer boundaryRenderer = boundaryMarker.GetComponent<Renderer>();
//                boundaryRenderer.material = boundaryMaterial;
//            }

//            // Store for updates
//            cellVisualizers[globalPos] = cellObject;
//        }

//        private void UpdateCellVisualization(Cell cell, GameObject cellObject)
//        {
//            Renderer renderer = cellObject.GetComponent<Renderer>();

//            if (cell.CollapsedState.HasValue)
//            {
//                // Cell is collapsed to a specific state
//                int state = cell.CollapsedState.Value;
//                if (state < stateMaterials.Length)
//                {
//                    renderer.material = stateMaterials[state];
//                }
//                cellObject.transform.localScale = Vector3.one * cellSize * 0.9f;
//            }
//            else if (showUncollapsedCells)
//            {
//                // Show uncollapsed cells, scale by entropy
//                renderer.material = uncollapsedMaterial;
//                float entropyFactor = Mathf.Max(0.1f, (float)cell.Entropy / maxStates);
//                cellObject.transform.localScale = Vector3.one * cellSize * 0.5f * entropyFactor;
//            }
//            else
//            {
//                // Hide uncollapsed cells
//                cellObject.SetActive(false);
//            }
//        }

//        private void UpdateVisualizations()
//        {
//            // Update all cell visualizations
//            foreach (var chunkEntry in chunks)
//            {
//                Vector3Int chunkPos = chunkEntry.Key;
//                Chunk chunk = chunkEntry.Value;

//                for (int x = 0; x < chunk.Size; x++)
//                {
//                    ScreenCapture.CaptureScreenshot($"Seed_{x}_Test.png");          // for testing
//                    for (int y = 0; y < chunk.Size; y++)
//                    {
//                        for (int z = 0; z < chunk.Size; z++)
//                        {
//                            Cell cell = chunk.GetCell(x, y, z);

//                            // Get global position
//                            Vector3Int globalPos = new Vector3Int(
//                                chunkPos.x * chunkSize + x,
//                                chunkPos.y * chunkSize + y,
//                                chunkPos.z * chunkSize + z
//                            );

//                            // Update visualization if it exists
//                            if (cellVisualizers.TryGetValue(globalPos, out GameObject cellObject))
//                            {
//                                UpdateCellVisualization(cell, cellObject);
//                            }
//                        }
//                    }
//                }
//            }
//        }

//        private IEnumerator RunGenerationProcess()
//        {
//            while (true)
//            {
//                // Run a few steps
//                bool continueGenerating = RunGenerationSteps(stepsPerUpdate);

//                // Update visuals
//                UpdateVisualizations();

//                // Stop if generation is complete
//                if (!continueGenerating)
//                {
//                    Debug.Log("Generation complete!");
//                    break;
//                }

//                // Wait before next update
//                yield return new WaitForSeconds(stepDelay);
//            }
//        }

//        private bool RunGenerationSteps(int steps)
//        {
//            bool madeProgress = false;

//            for (int i = 0; i < steps; i++)
//            {
//                // Check if propagation queue has events
//                if (propagationQueue.Count > 0)
//                {
//                    // Process next event
//                    ProcessNextPropagationEvent();
//                    madeProgress = true;
//                }
//                else if (generationStep == 0)
//                {
//                    // First step - create initial constraints
//                    CreateInitialConstraints();
//                    madeProgress = true;
//                    generationStep++;
//                }
//                else if (FindCellToCollapse())
//                {
//                    // Found a cell to collapse
//                    madeProgress = true;
//                }
//                else
//                {
//                    // No more events, no cells to collapse
//                    return false;
//                }
//            }

//            return madeProgress;
//        }

//        private void CreateInitialConstraints()
//        {
//            // Instead of large blocks of the same state, create smaller, varied patterns
//            // For example, in the bottom-left chunk, create a mix of water and sand:
//            if (chunks.TryGetValue(new Vector3Int(0, 0, 0), out Chunk bottomLeftChunk))
//            {
//                for (int x = 2; x < 6; x++)
//                {
//                    for (int y = 2; y < 6; y++)
//                    {
//                        // Create a pattern rather than all the same state
//                        int stateToUse;
//                        if ((x + y) % 3 == 0)
//                            stateToUse = (int)TileState.Water;
//                        else if ((x + y) % 3 == 1)
//                            stateToUse = (int)TileState.Sand;
//                        else
//                            stateToUse = (int)TileState.Ground;

//                        Cell cell = bottomLeftChunk.GetCell(x, y, 0);
//                        CollapseCell(cell, bottomLeftChunk, stateToUse);
//                    }
//                }
//            }

//            // Create a mountain in the top-left chunk
//            if (worldSizeY > 1 && chunks.TryGetValue(new Vector3Int(0, 1, 0), out Chunk topLeftChunk))
//            {
//                // Collapse some cells to rock
//                for (int x = 3; x < 5; x++)
//                {
//                    for (int y = 3; y < 5; y++)
//                    {
//                        Cell cell = topLeftChunk.GetCell(x, y, 0);
//                        CollapseCell(cell, topLeftChunk, (int)TileState.Rock);
//                    }
//                }
//            }

//            // Create a forest in the top-right chunk
//            if (worldSizeX > 1 && worldSizeY > 1 && chunks.TryGetValue(new Vector3Int(1, 1, 0), out Chunk topRightChunk))
//            {
//                // Collapse some cells to trees
//                for (int x = 3; x < 5; x++)
//                {
//                    for (int y = 3; y < 5; y++)
//                    {
//                        Cell cell = topRightChunk.GetCell(x, y, 0);
//                        CollapseCell(cell, topRightChunk, (int)TileState.Tree);
//                    }
//                }
//            }

//            // Create a beach in the bottom-right chunk
//            if (worldSizeX > 1 && chunks.TryGetValue(new Vector3Int(1, 0, 0), out Chunk bottomRightChunk))
//            {
//                // Collapse some cells to sand
//                for (int x = 3; x < 5; x++)
//                {
//                    for (int y = 3; y < 5; y++)
//                    {
//                        Cell cell = bottomRightChunk.GetCell(x, y, 0);
//                        CollapseCell(cell, bottomRightChunk, (int)TileState.Sand);
//                    }
//                }
//            }
//        }

//        private void CollapseCell(Cell cell, Chunk chunk, int state)
//        {
//            // Check if state is valid
//            if (!cell.PossibleStates.Contains(state))
//                return;

//            // Store old states
//            HashSet<int> oldStates = new HashSet<int>(cell.PossibleStates);

//            // Collapse cell
//            cell.Collapse(state);

//            // Create propagation event
//            PropagationEvent evt = new PropagationEvent(
//                cell,
//                chunk,
//                oldStates,
//                new HashSet<int> { state },
//                cell.IsBoundary
//            );

//            // Add to queue
//            AddPropagationEvent(evt);
//        }

//        // Modify the FindCellToCollapse method
//        private bool FindCellToCollapse()
//        {
//            // Find the cell with lowest entropy (but not already collapsed)
//            Cell cellToCollapse = null;
//            Chunk chunkWithCell = null;
//            int lowestEntropy = int.MaxValue;
//            float highestConstraintInfluence = 0f;

//            foreach (var chunk in chunks.Values)
//            {
//                for (int x = 0; x < chunk.Size; x++)
//                {
//                    for (int y = 0; y < chunk.Size; y++)
//                    {
//                        for (int z = 0; z < chunk.Size; z++)
//                        {
//                            Cell cell = chunk.GetCell(x, y, z);

//                            // Skip already collapsed cells
//                            if (cell.CollapsedState.HasValue)
//                                continue;

//                            // Skip cells with only one state (will be collapsed in propagation)
//                            if (cell.PossibleStates.Count == 1)
//                                continue;

//                            // Calculate effective entropy based on constraint influence
//                            int effectiveEntropy = cell.Entropy;
//                            float constraintInfluence = 0f;

//                            if (usingConstraints && constraintSystem != null)
//                            {
//                                // Get constraint influence for this cell
//                                Dictionary<int, float> biases = constraintSystem.CalculateConstraintInfluence(
//                                    cell, chunk, maxStates);

//                                // Calculate maximum bias as influence measure
//                                foreach (var bias in biases.Values)
//                                {
//                                    if (Mathf.Abs(bias) > constraintInfluence)
//                                        constraintInfluence = Mathf.Abs(bias);
//                                }

//                                // Reduce effective entropy based on constraint influence
//                                if (constraintInfluence > 0.7f)
//                                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.5f);
//                                else if (constraintInfluence > 0.4f)
//                                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.7f);
//                                else if (constraintInfluence > 0.2f)
//                                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.9f);
//                            }

//                            // Selection criteria: prefer low entropy with high constraint influence
//                            bool betterCell = false;

//                            if (effectiveEntropy < lowestEntropy)
//                            {
//                                betterCell = true;
//                            }
//                            else if (effectiveEntropy == lowestEntropy && constraintInfluence > highestConstraintInfluence)
//                            {
//                                betterCell = true;
//                            }

//                            if (betterCell)
//                            {
//                                lowestEntropy = effectiveEntropy;
//                                highestConstraintInfluence = constraintInfluence;
//                                cellToCollapse = cell;
//                                chunkWithCell = chunk;
//                            }
//                        }
//                    }
//                }
//            }

//            // If found a cell, collapse it
//            if (cellToCollapse != null && chunkWithCell != null)
//            {
//                // Choose a state based on constraints and entropy
//                int stateToCollapse = ChooseConstrainedState(cellToCollapse, chunkWithCell);

//                // Collapse the cell
//                CollapseCell(cellToCollapse, chunkWithCell, stateToCollapse);
//                return true;
//            }

//            return false;
//        }

//        // Add this new method to select a state based on constraints
//        private int ChooseConstrainedState(Cell cell, Chunk chunk)
//        {
//            int[] possibleStates = cell.PossibleStates.ToArray();

//            // If no constraints or only one possible state, select randomly
//            if (!usingConstraints || constraintSystem == null || possibleStates.Length == 1)
//            {
//                return possibleStates[Random.Range(0, possibleStates.Length)];
//            }

//            // Get constraint biases
//            Dictionary<int, float> biases = constraintSystem.CalculateConstraintInfluence(
//                cell, chunk, maxStates);

//            // Calculate selection weights for each state
//            float[] weights = new float[possibleStates.Length];
//            float totalWeight = 0f;

//            for (int i = 0; i < possibleStates.Length; i++)
//            {
//                int state = possibleStates[i];
//                float weight = 1.0f; // Base weight

//                // Apply bias if available
//                if (biases.TryGetValue(state, out float bias))
//                {
//                    // Convert bias (-1 to 1) to weight multiplier (0.1 to 5)
//                    float multiplier = Mathf.Pow(10, bias);
//                    weight *= multiplier;
//                }

//                // Ensure weight is positive
//                weights[i] = Mathf.Max(0.1f, weight);
//                totalWeight += weights[i];
//            }

//            // Weighted random selection
//            float randomValue = Random.Range(0, totalWeight);
//            float accumulatedWeight = 0f;

//            for (int i = 0; i < possibleStates.Length; i++)
//            {
//                accumulatedWeight += weights[i];
//                if (randomValue <= accumulatedWeight)
//                {
//                    return possibleStates[i];
//                }
//            }

//            // Fallback (should never reach here)
//            return possibleStates[0];
//        }

//        private void ProcessNextPropagationEvent()
//        {
//            if (propagationQueue.Count == 0)
//                return;

//            PropagationEvent evt = propagationQueue.Dequeue();
//            Cell cell = evt.Cell;
//            Chunk chunk = evt.Chunk;

//            // Propagate to neighbors
//            PropagateConstraints(cell, chunk);

//            // Update boundary buffers if needed
//            if (cell.IsBoundary)
//            {
//                boundaryManager.UpdateBuffersAfterCollapse(cell, chunk);
//            }
//        }

//        private void PropagateConstraints(Cell cell, Chunk chunk)
//        {
//            ApplyHierarchicalConstraints(cell, chunk);
//            if (usingConstraints && constraintSystem != null)
//            {
//                constraintSystem.ApplyConstraintsToCell(cell, chunk, maxStates);
//            }
//            // Skip if cell isn't collapsed
//            if (!cell.CollapsedState.HasValue)
//                return;

//            int state = cell.CollapsedState.Value;

//            // Find cell position
//            Vector3Int? cellPos = null;
//            for (int x = 0; x < chunk.Size; x++)
//            {
//                for (int y = 0; y < chunk.Size; y++)
//                {
//                    for (int z = 0; z < chunk.Size; z++)
//                    {
//                        if (chunk.GetCell(x, y, z) == cell)
//                        {
//                            cellPos = new Vector3Int(x, y, z);
//                            break;
//                        }
//                    }
//                    if (cellPos != null) break;
//                }
//                if (cellPos != null) break;
//            }

//            if (!cellPos.HasValue)
//                return;

//            // Check each direction
//            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
//            {
//                // Calculate neighbor position
//                Vector3Int offset = dir.ToVector3Int();
//                Vector3Int neighborPos = cellPos.Value + offset;

//                // Check if neighbor is within this chunk
//                if (neighborPos.x >= 0 && neighborPos.x < chunk.Size &&
//                    neighborPos.y >= 0 && neighborPos.y < chunk.Size &&
//                    neighborPos.z >= 0 && neighborPos.z < chunk.Size)
//                {
//                    // Get neighbor cell
//                    Cell neighbor = chunk.GetCell(neighborPos.x, neighborPos.y, neighborPos.z);

//                    // Skip if already collapsed
//                    if (neighbor.CollapsedState.HasValue)
//                        continue;

//                    // Apply constraints
//                    ApplyConstraintToCell(neighbor, state, dir, chunk);
//                }
//            }
//        }

//        private void ApplyConstraintToCell(Cell cell, int constraintState, Direction direction, Chunk chunk)
//        {
//            // Calculate new possible states based on adjacency rules
//            HashSet<int> newPossibleStates = new HashSet<int>();

//            foreach (int state in cell.PossibleStates)
//            {
//                if (AreStatesCompatible(state, constraintState, direction))
//                {
//                    newPossibleStates.Add(state);
//                }
//            }

//            // Skip if no change
//            if (newPossibleStates.SetEquals(cell.PossibleStates))
//                return;

//            // Store old states
//            HashSet<int> oldStates = new HashSet<int>(cell.PossibleStates);

//            // Update cell's possible states
//            bool changed = cell.SetPossibleStates(newPossibleStates);

//            // If changed, create propagation event
//            if (changed)
//            {
//                PropagationEvent evt = new PropagationEvent(
//                    cell,
//                    chunk,
//                    oldStates,
//                    newPossibleStates,
//                    cell.IsBoundary
//                );

//                AddPropagationEvent(evt);
//            }
//        }

//        public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
//        {
//            return adjacencyRules[stateA, stateB, (int)direction];
//        }

//        public void AddPropagationEvent(PropagationEvent evt)
//        {
//            propagationQueue.Enqueue(evt, evt.Priority);
//        }

//        public void ResetGeneration()
//        {
//            // Clear propagation queue
//            propagationQueue = new PriorityQueue<PropagationEvent, float>();

//            // Reset all cells
//            foreach (var chunk in chunks.Values)
//            {
//                for (int x = 0; x < chunk.Size; x++)
//                {
//                    for (int y = 0; y < chunk.Size; y++)
//                    {
//                        for (int z = 0; z < chunk.Size; z++)
//                        {
//                            Cell cell = chunk.GetCell(x, y, z);
//                            cell.SetPossibleStates(new HashSet<int>(Enumerable.Range(0, maxStates)));
//                        }
//                    }
//                }
//            }

//            // Reset generation step
//            generationStep = 0;

//            // Update visualizations
//            UpdateVisualizations();

//            Debug.Log("Generation reset");
//        }
//        // Add to WFCTestController.cs
//        public void RunOneStep()
//        {
//            RunGenerationSteps(1);
//            UpdateVisualizations();
//        }

//        // Accessor methods for BoundaryBufferManager
//        public Dictionary<Vector3Int, Chunk> GetChunks()
//        {
//            return chunks;
//        }

//        public int MaxStates => maxStates;
//        public int ChunkSize => chunkSize;


//        // for API calls
//        public HierarchicalConstraintSystem GetHierarchicalConstraintSystem()
//        {
//            return hierarchicalConstraints;
//        }
//    }
//}