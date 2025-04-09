using System;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Boundary;
using System.Linq;
using Utils;
using WFC.Configuration;
using System.Collections;
using WFC.Chunking;
using WFC.Processing;

namespace WFC.Generation
{
    /// <summary>
    /// Updated WFCGenerator class that properly integrates with the configuration system
    /// </summary>
    public class WFCGenerator : MonoBehaviour, WFC.Boundary.IChunkProvider, WFC.Boundary.IWFCAlgorithm
    {
        [Header("Configuration")]
        [Tooltip("Override the global configuration with a specific asset")]
        [SerializeField] private WFCConfiguration configOverride;

        [Header("State Settings")]
        [SerializeField] private Material[] stateMaterials;

        // World data
        private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
        private PriorityQueue<PropagationEvent, float> propagationQueue = new PriorityQueue<PropagationEvent, float>();
        private bool[,,] adjacencyRules;

        private HierarchicalConstraintSystem hierarchicalConstraints;

        // Boundary manager
        private BoundaryBufferManager boundaryManager;

        // Cache for config access
        private WFCConfiguration activeConfig;
        private ParallelWFCProcessor parallelProcessor;


        private int frameSkipCounter = 0;

        // Properties that now use the configuration
        public int MaxCellStates => activeConfig.World.maxStates;
        public int ChunkSize => activeConfig.World.chunkSize;
        public Vector3Int WorldSize => new Vector3Int(
            activeConfig.World.worldSizeX,
            activeConfig.World.worldSizeY,
            activeConfig.World.worldSizeZ
        );

        public Dictionary<Vector3Int, Chunk> GetChunks()
        {
            return chunks;
        }

        public void AddPropagationEvent(PropagationEvent evt)
        {
            propagationQueue.Enqueue(evt, evt.Priority);
        }

        private void Awake()
        {
            // Get the configuration - use override if specified, otherwise use global config
            activeConfig = configOverride != null ? configOverride : WFCConfigManager.Config;

            if (activeConfig == null)
            {
                Debug.LogError("WFCGenerator: No configuration available. Please assign a WFCConfiguration asset.");
                enabled = false;
                return;
            }

            // Validate configuration
            if (!activeConfig.Validate())
            {
                Debug.LogWarning("WFCGenerator: Using configuration with validation issues.");
            }

            StartCoroutine(DelayedInitialization());

        }

        // Initializes the WFC algorithm and starts the generation process
        private IEnumerator DelayedInitialization()
        {
            int maxRetries = 5;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                yield return new WaitForSeconds(0.2f * (retryCount + 1));

                ChunkManager chunkManager = FindObjectOfType<ChunkManager>();
                if (chunkManager != null)
                {
                    Debug.Log("ChunkManager found, initializing rules only");
                    InitializeRulesOnly();
                    yield break;
                }

                retryCount++;
                Debug.Log($"ChunkManager not found, retry {retryCount}/{maxRetries}");
            }

            Debug.Log("No ChunkManager found after retries, initializing WFC grid at origin");
            InitializeWorld();
        }

        // Initializes rules without creating chunks
        private void InitializeRulesOnly()
        {
            // Initialize adjacency rules but don't create chunks
            adjacencyRules = new bool[MaxCellStates, MaxCellStates, 6];
            SetupAdjacencyRules();

            // Create boundary manager
            boundaryManager = new BoundaryBufferManager((IWFCAlgorithm)this);

            InitializeHierarchicalConstraints();
        }

        private void Update()
        {
            // Skip frames for heavy operations
            frameSkipCounter++;
            if (frameSkipCounter % 3 != 0) // Only process every 3rd frame
                return;

            // Process propagation queue
            ProcessPropagationQueue();

            // Process parallel processor events
            if (parallelProcessor != null)
            {
                parallelProcessor.ProcessMainThreadEvents();
            }


            // Check if it need to collapse more cells
            if (propagationQueue.Count == 0)
            {
                CollapseNextCell();
            }
        }

        private void ProcessPropagationQueue()
        {
            // Process a fixed number of events per frame
            int maxEventsPerFrame = 20;
            int processedEvents = 0;

            while (propagationQueue.Count > 0 && processedEvents < maxEventsPerFrame)
            {
                PropagationEvent evt = propagationQueue.Dequeue();
                ProcessPropagationEvent(evt);
                processedEvents++;
            }
        }

        // Collapse the next cell in the queue
        private void ProcessPropagationEvent(PropagationEvent evt)
        {
            // Skip if the event is invalid
            if (evt.Cell == null || evt.Chunk == null)
                return;

            Cell cell = evt.Cell;
            Chunk chunk = evt.Chunk;

            // Apply constraints from hierarchy
            ApplyConstraintsToCell(cell, chunk);

            // Skip if not collapsed
            if (!cell.CollapsedState.HasValue)
                return;

            // Find neighbors and propagate constraints
            PropagateToNeighbors(cell, chunk);

            // Update boundary buffers if needed
            if (cell.IsBoundary && cell.BoundaryDirection.HasValue)
            {
                boundaryManager.UpdateBuffersAfterCollapse(cell, chunk);
            }
        }

        // Collapse the next cell in the queue
        private void PropagateToNeighbors(Cell cell, Chunk chunk)
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
                if (IsPositionInChunk(neighborPos, chunk.Size))
                {
                    // Get neighbor cell and apply constraint
                    Cell neighbor = chunk.GetCell(neighborPos.x, neighborPos.y, neighborPos.z);
                    ApplyConstraint(neighbor, state, dir, chunk);
                }
                else
                {
                    // Handle neighbor in adjacent chunk (through boundary system)
                }
            }
        }

        // Find the position of a cell within a chunk
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

        // Apply constraints to a cell based on its collapsed state
        private bool IsPositionInChunk(Vector3Int pos, int chunkSize)
        {
            return pos.x >= 0 && pos.x < chunkSize &&
                   pos.y >= 0 && pos.y < chunkSize &&
                   pos.z >= 0 && pos.z < chunkSize;
        }

        // Apply constraints to a cell based on its collapsed state
        private void ApplyConstraint(Cell cell, int constraintState, Direction direction, Chunk chunk)
        {
            // Skip if already collapsed
            if (cell.CollapsedState.HasValue)
                return;

            // Store old states
            HashSet<int> oldStates = new HashSet<int>(cell.PossibleStates);

            // Find compatible states
            HashSet<int> compatibleStates = new HashSet<int>();

            foreach (int state in cell.PossibleStates)
            {
                if (AreStatesCompatible(state, constraintState, direction))
                {
                    compatibleStates.Add(state);
                }
            }

            // Update possible states
            if (compatibleStates.Count > 0 && !compatibleStates.SetEquals(oldStates))
            {
                cell.SetPossibleStates(compatibleStates);

                // Create propagation event
                PropagationEvent evt = new PropagationEvent(
                    cell,
                    chunk,
                    oldStates,
                    compatibleStates,
                    cell.IsBoundary
                );

                AddPropagationEvent(evt);
            }
        }

        /// <summary>
        /// Initializes the world grid and adjacent rules.
        /// Creates the initial structure and rules for the WFC algorithm.
        /// </summary>
        private void InitializeWorld()
        {
            // Initialize adjacency rules
            adjacencyRules = new bool[MaxCellStates, MaxCellStates, 6]; // 6 directions
            SetupAdjacencyRules();

            // Create chunks based on configuration
            for (int x = 0; x < WorldSize.x; x++)
            {
                for (int y = 0; y < WorldSize.y; y++)
                {
                    for (int z = 0; z < WorldSize.z; z++)
                    {
                        Vector3Int chunkPos = new Vector3Int(x, y, z);
                        Chunk chunk = new Chunk(chunkPos, ChunkSize);

                        // Initialize with all possible states
                        var allStates = Enumerable.Range(0, MaxCellStates);
                        chunk.InitializeCells(allStates);

                        chunks.Add(chunkPos, chunk);
                    }
                }
            }

            // Connect chunk neighbors
            ConnectChunkNeighbors();

            // Initialize boundary buffers
            InitializeBoundaryBuffers();

            // Create boundary manager
            boundaryManager = new BoundaryBufferManager((IWFCAlgorithm)this);

            InitializeHierarchicalConstraints();
        }

        private int GenerateChunkSeed(Vector3Int chunkPos)
        {
            // Create a deterministic seed from chunk position
            // This ensures the same terrain is always generated for the same chunk
            int baseSeed = WFCConfigManager.Config.World.randomSeed;
            int chunkSeed = baseSeed +
                            chunkPos.x * 73856093 ^
                            chunkPos.y * 19349663 ^
                            chunkPos.z * 83492791;
            return chunkSeed;
        }

        // In WFCGenerator.cs - InitializeHierarchicalConstraints method
        private void InitializeHierarchicalConstraints()
        {
            // Create the hierarchical constraint system
            hierarchicalConstraints = new HierarchicalConstraintSystem(ChunkSize);

            // CRITICAL: Add a strong ground level constraint first
            GlobalConstraint groundConstraint = new GlobalConstraint
            {
                Name = "Ground Level",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(WorldSize.x * ChunkSize / 2, WorldSize.y * ChunkSize / 4, WorldSize.z * ChunkSize / 2),
                WorldSize = new Vector3(WorldSize.x * ChunkSize, 0, WorldSize.z * ChunkSize),
                BlendRadius = ChunkSize * 2,
                Strength = 0.9f,
                MinHeight = 0,
                MaxHeight = WorldSize.y * ChunkSize
            };

            // Add strong biases for ground formation
            groundConstraint.StateBiases[0] = -0.9f; // Strong negative bias for air below ground level
            groundConstraint.StateBiases[1] = 0.8f;  // Strong positive bias for ground
            groundConstraint.StateBiases[2] = 0.6f;  // Positive bias for grass near surface

            hierarchicalConstraints.AddGlobalConstraint(groundConstraint);

            // Add mountain range constraint
            GlobalConstraint mountainConstraint = new GlobalConstraint
            {
                Name = "Mountain Range",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(WorldSize.x * ChunkSize / 4, WorldSize.y * ChunkSize / 2, WorldSize.z * ChunkSize / 2),
                WorldSize = new Vector3(WorldSize.x * ChunkSize / 3, WorldSize.y * ChunkSize * 0.7f, WorldSize.z * ChunkSize / 3),
                BlendRadius = ChunkSize * 3,
                Strength = 0.85f,
                MinHeight = WorldSize.y * ChunkSize / 4,
                MaxHeight = WorldSize.y * ChunkSize
            };

            mountainConstraint.StateBiases[0] = -0.9f; // Strong negative bias for air inside mountains
            mountainConstraint.StateBiases[4] = 0.9f;  // Strong positive bias for rock
            mountainConstraint.StateBiases[1] = 0.4f;  // Moderate bias for ground (base of mountains)

            hierarchicalConstraints.AddGlobalConstraint(mountainConstraint);

            // Add river/water body constraint
            GlobalConstraint riverConstraint = new GlobalConstraint
            {
                Name = "River",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(WorldSize.x * ChunkSize * 0.6f, WorldSize.y * ChunkSize * 0.15f, WorldSize.z * ChunkSize / 2),
                WorldSize = new Vector3(WorldSize.x * ChunkSize * 0.25f, WorldSize.y * ChunkSize * 0.1f, WorldSize.z * ChunkSize * 0.8f),
                BlendRadius = ChunkSize * 2,
                Strength = 0.85f
            };

            riverConstraint.StateBiases[3] = 0.95f;  // Very strong bias for water
            riverConstraint.StateBiases[5] = 0.7f;   // Strong bias for sand (river banks)
            riverConstraint.StateBiases[0] = -0.8f;  // Negative bias for air underwater

            hierarchicalConstraints.AddGlobalConstraint(riverConstraint);

            // Precompute constraints for each chunk
            foreach (var chunk in chunks.Values)
            {
                hierarchicalConstraints.PrecomputeChunkConstraints(chunk, MaxCellStates);
            }
        }

        private void ApplyRegionalConstraints(Chunk chunk, System.Random random)
        {
            // Global coordinates for consistent noise
            Vector3 worldPos = new Vector3(
                chunk.Position.x * ChunkSize,
                chunk.Position.y * ChunkSize,
                chunk.Position.z * ChunkSize
            );

            // Use noise functions to determine biome types but NOT height variation
            float elevationNoise = Mathf.PerlinNoise(worldPos.x * 0.005f, worldPos.z * 0.005f);
            float moistureNoise = Mathf.PerlinNoise(worldPos.x * 0.01f + 500, worldPos.z * 0.01f + 500);

            // Apply FLAT terrain by default - this ensures a single height layer
            ApplyFlatGroundTerrain(chunk, random);

            // Apply biome-specific features without significant height changes
            if (elevationNoise > 0.7f && chunk.Position.y == 0)
            {
                // Only allow terrain features on the bottom layer (y=0)
                ApplyMountainFeatures(chunk, elevationNoise, random);
            }
            else if (moistureNoise > 0.7f && elevationNoise < 0.4f && chunk.Position.y == 0)
            {
                // Only allow water on the bottom layer (y=0)
                ApplyWaterFeatures(chunk, moistureNoise, random);
            }
        }

        // Create flat ground terrain
        private void ApplyFlatGroundTerrain(Chunk chunk, System.Random random)
        {
            // Only generate terrain at the bottom layer (y=0)
            if (chunk.Position.y != 0)
            {
                // For all other y-levels, just fill with air
                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            Cell cell = chunk.GetCell(x, y, z);
                            if (!cell.CollapsedState.HasValue)
                            {
                                cell.Collapse(0); // Air
                            }
                        }
                    }
                }
                return;
            }

            // Create a ground constraint
            GlobalConstraint groundConstraint = new GlobalConstraint
            {
                Name = $"Ground_{chunk.Position}",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(
                    chunk.Position.x * ChunkSize + ChunkSize / 2,
                    chunk.Position.y * ChunkSize + ChunkSize / 2,
                    chunk.Position.z * ChunkSize + ChunkSize / 2
                ),
                WorldSize = new Vector3(ChunkSize, ChunkSize, ChunkSize),
                BlendRadius = ChunkSize * 1.5f,
                Strength = 0.8f
            };

            // Add bias for ground
            groundConstraint.StateBiases[1] = 0.9f;  // Strong bias for ground

            // Simple ground terrain - flat with small noise variation for interest
            int baseHeight = chunk.Size / 2; // Fixed base height for all terrain

            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    // Small height variation for natural look, but keep it modest
                    float heightVar = Mathf.PerlinNoise(
                        (chunk.Position.x * chunk.Size + x) * 0.05f,
                        (chunk.Position.z * chunk.Size + z) * 0.05f) * 2.0f;

                    int terrainHeight = Mathf.FloorToInt(baseHeight + heightVar);

                    // Set cells based on height, ensuring a single main layer
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);
                        if (!cell.CollapsedState.HasValue)
                        {
                            if (y <= terrainHeight)
                            {
                                cell.Collapse(1); // Ground
                            }
                            else
                            {
                                cell.Collapse(0); // Air above ground
                            }
                        }
                    }
                }
            }

            hierarchicalConstraints.AddGlobalConstraint(groundConstraint);
        }


        // Mountains as features without excessive height
        private void ApplyMountainFeatures(Chunk chunk, float intensity, System.Random random)
        {
            // Never make terrain in upper chunks
            if (chunk.Position.y > 0) return;

            GlobalConstraint mountainConstraint = new GlobalConstraint
            {
                Name = $"Mountain_{chunk.Position}",
                Type = ConstraintType.BiomeRegion, // Changed from HeightMap to BiomeRegion
                WorldCenter = new Vector3(
                    chunk.Position.x * ChunkSize + ChunkSize / 2,
                    chunk.Position.y * ChunkSize + ChunkSize / 2,
                    chunk.Position.z * ChunkSize + ChunkSize / 2
                ),
                WorldSize = new Vector3(ChunkSize, ChunkSize, ChunkSize),
                BlendRadius = ChunkSize * 1.5f,
                Strength = 0.7f
            };

            // Add biases for mountain terrain
            mountainConstraint.StateBiases[0] = -0.9f; // Discourage empty
            mountainConstraint.StateBiases[4] = 0.8f;  // Encourage rock

            // Base height adjustment for mountains - still keep it relatively flat
            int baseHeight = (chunk.Size / 2) + 1;

            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    // Use noise for small variations
                    float noise = Mathf.PerlinNoise(
                        (chunk.Position.x * ChunkSize + x) * 0.025f,
                        (chunk.Position.z * ChunkSize + z) * 0.025f);

                    // Limit vertical height variation
                    int rockHeight = Mathf.FloorToInt(baseHeight + (noise * 3.0f));

                    // Create terrain with rock features
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);
                        if (!cell.CollapsedState.HasValue)
                        {
                            if (y <= rockHeight)
                            {
                                if (y > baseHeight - 2)
                                {
                                    cell.Collapse(4); // Rock for upper portions
                                }
                                else
                                {
                                    cell.Collapse(1); // Ground for lower portions
                                }
                            }
                            else
                            {
                                cell.Collapse(0); // Air above
                            }
                        }
                    }
                }
            }

            hierarchicalConstraints.AddGlobalConstraint(mountainConstraint);
        }

        // Water features without excessive depth
        private void ApplyWaterFeatures(Chunk chunk, float intensity, System.Random random)
        {
            // Never make terrain in upper chunks
            if (chunk.Position.y > 0) return;

            GlobalConstraint waterConstraint = new GlobalConstraint
            {
                Name = $"Water_{chunk.Position}",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(
                    chunk.Position.x * ChunkSize + ChunkSize / 2,
                    chunk.Position.y * ChunkSize + ChunkSize / 3,
                    chunk.Position.z * ChunkSize + ChunkSize / 2
                ),
                WorldSize = new Vector3(ChunkSize, ChunkSize / 2, ChunkSize),
                BlendRadius = ChunkSize * 1.5f,
                Strength = 0.8f
            };

            // Add biases for water terrain
            waterConstraint.StateBiases[3] = 0.9f;  // Water
            waterConstraint.StateBiases[5] = 0.6f;  // Sand for shores

            // Fixed water level for consistency
            int waterLevel = chunk.Size / 3;

            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    // Calculate distance from edge for shore formation
                    float distFromEdge = Mathf.Min(
                        Mathf.Min(x, chunk.Size - 1 - x),
                        Mathf.Min(z, chunk.Size - 1 - z)
                    );

                    // Apply noise to shore distance
                    float shoreNoise = Mathf.PerlinNoise(
                        (chunk.Position.x * chunk.Size + x) * 0.1f + 500f,
                        (chunk.Position.z * chunk.Size + z) * 0.1f + 500f);

                    float shoreWidth = 2.0f + (shoreNoise * 1.5f);

                    // Create water and shores
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        Cell cell = chunk.GetCell(x, y, z);
                        if (!cell.CollapsedState.HasValue)
                        {
                            if (y <= waterLevel)
                            {
                                if (y == waterLevel && distFromEdge < shoreWidth)
                                {
                                    cell.Collapse(5); // Sand shores
                                }
                                else if (y <= waterLevel - 1)
                                {
                                    cell.Collapse(3); // Water (only 1 block deep)
                                }
                                else
                                {
                                    cell.Collapse(3); // Water surface
                                }
                            }
                            else
                            {
                                cell.Collapse(0); // Air above water
                            }
                        }
                    }
                }
            }

            hierarchicalConstraints.AddGlobalConstraint(waterConstraint);
        }
        private void CreateBiomeTransitions()
        {
            foreach (var chunk in chunks.Values)
            {
                foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
                {
                    // Skip if no neighbor in this direction
                    if (!chunk.Neighbors.ContainsKey(dir))
                        continue;

                    Chunk neighbor = chunk.Neighbors[dir];

                    // Determine chunk biome types and create appropriate transitions
                    string chunkBiomeName = GetDominantBiome(chunk);
                    string neighborBiomeName = GetDominantBiome(neighbor);

                    // Only create transitions between different biomes
                    if (chunkBiomeName != neighborBiomeName)
                    {
                        // Define source and target states based on biome types
                        int sourceState = GetPrimaryStateForBiome(chunkBiomeName);
                        int targetState = GetPrimaryStateForBiome(neighborBiomeName);

                        // Create a transition constraint
                        CreateBiomeTransition(chunk.Position, dir, sourceState, targetState);
                    }
                }
            }
        }

        private void CreateBiomeTransition(Vector3Int chunkPos, Direction direction, int sourceState, int targetState)
        {
            // Create a transition constraint between biomes
            RegionConstraint transitionConstraint = new RegionConstraint
            {
                Name = $"Transition_{chunkPos}_{direction}",
                Type = RegionType.Transition,
                ChunkPosition = chunkPos,
                ChunkSize = Vector3Int.one,
                Strength = 0.7f,
                Gradient = 0.7f, // Higher gradient for smoother transitions
                SourceState = sourceState,
                TargetState = targetState,
                TransitionDirection = direction.ToVector3Int()
            };

            // Add the transition to the constraint system
            hierarchicalConstraints.AddRegionConstraint(transitionConstraint);
        }

        // Helper methods for biome transitions
        private string GetDominantBiome(Chunk chunk)
        {
            // Check for constraint names to identify biome type
            foreach (var constraint in hierarchicalConstraints.GetGlobalConstraints())
            {
                if (constraint.Name.Contains($"_{chunk.Position}"))
                {
                    if (constraint.Name.StartsWith("Mountain_"))
                        return "Mountain";
                    else if (constraint.Name.StartsWith("Water_"))
                        return "Water";
                    else if (constraint.Name.StartsWith("Forest_"))
                        return "Forest";
                    else if (constraint.Name.StartsWith("Desert_"))
                        return "Desert";
                    else if (constraint.Name.StartsWith("Plains_"))
                        return "Plains";
                }
            }
            return "Unknown";
        }

        private int GetPrimaryStateForBiome(string biomeName)
        {
            switch (biomeName)
            {
                case "Mountain": return 4; // Rock
                case "Water": return 3;    // Water
                case "Forest": return 6;   // Tree
                case "Desert": return 5;   // Sand
                case "Plains": return 2;   // Grass
                default: return 1;         // Ground as default
            }
        }

        private void SetupAdjacencyRules()
        {
            // Initialize all to false
            for (int i = 0; i < MaxCellStates; i++)
                for (int j = 0; j < MaxCellStates; j++)
                    for (int d = 0; d < 6; d++)
                        adjacencyRules[i, j, d] = false;

            // CRITICAL: Group solid states (1, 2, 4, 5) - they should all connect
            int[] solidStates = { 1, 2, 4, 5 }; // Ground, grass, rock, sand

            // All solid states can connect to each other
            foreach (int stateA in solidStates)
            {
                foreach (int stateB in solidStates)
                {
                    SetAdjacentAll(stateA, stateB, true);
                }
            }

            // Air can only be above solid states and water, not adjacent horizontally
            foreach (int state in solidStates)
            {
                adjacencyRules[0, state, (int)Direction.Down] = true; // Air above solid
                adjacencyRules[state, 0, (int)Direction.Up] = true;   // Solid below air
            }

            // Water adjacencies - only horizontal with solid states
            foreach (int state in solidStates)
            {
                adjacencyRules[3, state, (int)Direction.Left] = true;
                adjacencyRules[3, state, (int)Direction.Right] = true;
                adjacencyRules[3, state, (int)Direction.Forward] = true;
                adjacencyRules[3, state, (int)Direction.Back] = true;

                adjacencyRules[state, 3, (int)Direction.Left] = true;
                adjacencyRules[state, 3, (int)Direction.Right] = true;
                adjacencyRules[state, 3, (int)Direction.Forward] = true;
                adjacencyRules[state, 3, (int)Direction.Back] = true;
            }

            // Water is below air
            adjacencyRules[0, 3, (int)Direction.Down] = true;
            adjacencyRules[3, 0, (int)Direction.Up] = true;

            // Same state adjacencies
            SetAdjacentAll(0, 0, true); // Air to air
            SetAdjacentAll(1, 1, true); // Ground to ground
            SetAdjacentAll(2, 2, true); // Grass to grass
            SetAdjacentAll(3, 3, true); // Water to water
            SetAdjacentAll(4, 4, true); // Rock to rock
            SetAdjacentAll(5, 5, true); // Sand to sand
        }

        private void SetAdjacentAll(int stateA, int stateB, bool canBeAdjacent)
        {
            for (int d = 0; d < 6; d++)
            {
                adjacencyRules[stateA, stateB, d] = canBeAdjacent;
                adjacencyRules[stateB, stateA, d] = canBeAdjacent;
            }
        }

        private void ConnectChunkNeighbors()
        {
            foreach (var chunkEntry in chunks)
            {
                Vector3Int pos = chunkEntry.Key;
                Chunk chunk = chunkEntry.Value;

                // Check each direction for neighbors
                foreach (Direction dir in Enum.GetValues(typeof(Direction)))
                {
                    Vector3Int offset = dir.ToVector3Int();
                    Vector3Int neighborPos = pos + offset;

                    if (chunks.TryGetValue(neighborPos, out Chunk neighbor))
                    {
                        chunk.Neighbors[dir] = neighbor;
                    }
                }
            }

            foreach (var chunk in chunks.Values)
            {
                int chunkSeed = GenerateChunkSeed(chunk.Position);
                System.Random random = new System.Random(chunkSeed);
                ApplyRegionalConstraints(chunk, random);
            }
        }

        // To strengthen height-based constraints
        private void ApplyConstraintsToCell(Cell cell, Chunk chunk)
        {
            // Skip if constraints are disabled
            if (!activeConfig.Algorithm.useConstraints || hierarchicalConstraints == null)
                return;

            // Skip if cell is already collapsed
            if (cell.CollapsedState.HasValue)
                return;

            // World position for height-based logic
            Vector3 worldPos = CalculateWorldPosition(cell.Position, chunk.Position);
            float worldHeight = worldPos.y;

            // Get constraint biases for decision making
            Dictionary<int, float> biases = new Dictionary<int, float>();
            var rawBiases = hierarchicalConstraints.CalculateConstraintInfluence(cell, chunk, MaxCellStates);

            foreach (var bias in rawBiases)
            {
                // Scale the bias by the chunk's LOD constraint influence factor
                biases[bias.Key] = bias.Value * chunk.ConstraintInfluence;
            }

            // Apply STRONG height-based constraints to eliminate holes
            float heightRatio = worldHeight / (WorldSize.y * ChunkSize);

            // Force air above certain height
            if (heightRatio > 0.6f)
            {
                cell.Collapse(0); // Force air at heights
                return;
            }

            // Force solid below certain height
            if (heightRatio < 0.3f)
            {
                // Look at biases to decide which solid state
                if (biases.ContainsKey(4) && biases[4] > 0.3f)
                    cell.Collapse(4); // Rock
                else if (biases.ContainsKey(3) && biases[3] > 0.3f)
                    cell.Collapse(3); // Water
                else
                    cell.Collapse(1); // Default to ground
                return;
            }

            // Normal processing for middle heights

            // If no significant biases, return without modifying cell
            if (!biases.Values.Any(v => Mathf.Abs(v) > 0.01f))
                return;

            // Get current possible states
            HashSet<int> currentStates = new HashSet<int>(cell.PossibleStates);

            // Calculate adjustment probability for each state based on biases
            Dictionary<int, float> stateWeights = new Dictionary<int, float>();

            foreach (int state in currentStates)
            {
                // Base weight is 1.0
                float weight = 1.0f;

                // Apply bias adjustment if exists
                if (biases.TryGetValue(state, out float bias))
                {
                    // Positive bias increases weight (more likely)
                    // Negative bias decreases weight (less likely)
                    weight *= (1.0f + bias);
                }

                // Ensure weight is positive
                weight = Mathf.Max(0.01f, weight);
                stateWeights[state] = weight;
            }

            // If cell has more than one possible state, consider constraint-based collapse
            if (currentStates.Count > 1)
            {
                // Get total weight
                float totalWeight = stateWeights.Values.Sum();

                // Calculate collapse threshold based on strongest bias and LOD
                float maxBias = biases.Values.Max(Mathf.Abs);
                float collapseThreshold = Mathf.Lerp(0.9f, 0.5f, maxBias * chunk.ConstraintInfluence);

                // Find if we have a highly dominant state
                foreach (var state in stateWeights.Keys.ToList())
                {
                    float normalizedWeight = stateWeights[state] / totalWeight;

                    // If one state is strongly preferred, collapse to it
                    if (normalizedWeight > collapseThreshold)
                    {
                        cell.Collapse(state);
                        return; // Cell is now collapsed, we're done
                    }
                }
            }
        }

        private void InitializeBoundaryBuffers()
        {
            foreach (var chunk in chunks.Values)
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

                    // Create buffer cells (virtual cells that mirror neighbor's boundary)
                    Direction oppositeDir = dir.GetOpposite();
                    List<Cell> neighborBoundaryCells = GetBoundaryCells(neighbor, oppositeDir);

                    // Create buffer cells with same number of elements as boundary cells
                    for (int i = 0; i < boundaryCells.Count; i++)
                    {
                        Vector3Int pos = new Vector3Int(-1, -1, -1); // Invalid position for buffer cells
                        Cell bufferCell = new Cell(pos, new int[0]);
                        buffer.BufferCells.Add(bufferCell);
                    }

                    chunk.BoundaryBuffers[dir] = buffer;
                }
            }

            // Now sync buffer cells with their corresponding boundaries
            foreach (var chunk in chunks.Values)
            {
                foreach (var buffer in chunk.BoundaryBuffers.Values)
                {
                    SynchronizeBuffer(buffer);
                }
            }
        }

        private List<Cell> GetBoundaryCells(Chunk chunk, Direction direction)
        {
            List<Cell> cells = new List<Cell>();
            int size = chunk.Size;

            switch (direction)
            {
                case Direction.Left:
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            cells.Add(chunk.GetCell(0, y, z));
                        }
                    }
                    break;

                case Direction.Right:
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            cells.Add(chunk.GetCell(chunk.Size - 1, y, z));
                        }
                    }
                    break;

                case Direction.Down:
                    for (int x = 0; x < chunk.Size; x++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            cells.Add(chunk.GetCell(x, 0, z));
                        }
                    }
                    break;

                case Direction.Up:
                    for (int x = 0; x < chunk.Size; x++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            cells.Add(chunk.GetCell(x, chunk.Size - 1, z));
                        }
                    }
                    break;

                case Direction.Back:
                    for (int x = 0; x < chunk.Size; x++)
                    {
                        for (int y = 0; y < chunk.Size; y++)
                        {
                            cells.Add(chunk.GetCell(x, y, 0));
                        }
                    }
                    break;

                case Direction.Forward:
                    for (int x = 0; x < chunk.Size; x++)
                    {
                        for (int y = 0; y < chunk.Size; y++)
                        {
                            cells.Add(chunk.GetCell(x, y, chunk.Size - 1));
                        }
                    }
                    break;
            }

            return cells;
        }

        private void SynchronizeBuffer(BoundaryBuffer buffer)
        {
            if (buffer.AdjacentChunk == null)
                return;

            Direction oppositeDir = buffer.Direction.GetOpposite();
            BoundaryBuffer adjacentBuffer = buffer.AdjacentChunk.BoundaryBuffers[oppositeDir];

            for (int i = 0; i < buffer.BoundaryCells.Count; i++)
            {
                // Update buffer cells to reflect adjacent boundary cells
                HashSet<int> adjacentStates = new HashSet<int>(adjacentBuffer.BoundaryCells[i].PossibleStates);
                buffer.BufferCells[i].SetPossibleStates(adjacentStates);

                // And vice versa
                HashSet<int> localStates = new HashSet<int>(buffer.BoundaryCells[i].PossibleStates);
                adjacentBuffer.BufferCells[i].SetPossibleStates(localStates);
            }
        }

        public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
        {
            return adjacencyRules[stateA, stateB, (int)direction];
        }

        private bool CollapseNextCell()
        {
            // Find the cell with lowest entropy
            Cell cellToCollapse = null;
            Chunk chunkWithCell = null;
            int lowestEntropy = int.MaxValue;
            float closestDistance = float.MaxValue;  // Initialize to max value

            // Get viewer position - either from a reference or camera
            Vector3 viewerPosition = Camera.main != null ?
                Camera.main.transform.position : Vector3.zero;

            // Option: Use a prioritized area if no camera
            // Vector3 priorityCenter = new Vector3(worldSize.x * chunkSize / 2, 0, worldSize.z * chunkSize / 2);
            // Vector3 viewerPosition = priorityCenter;

            foreach (var chunkEntry in chunks)
            {
                var chunk = chunkEntry.Value;

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

                            // Calculate effective entropy based on constraints
                            int effectiveEntropy = CalculateEffectiveEntropy(cell, chunk);

                            // First priority: Lowest entropy
                            if (effectiveEntropy < lowestEntropy)
                            {
                                lowestEntropy = effectiveEntropy;
                                cellToCollapse = cell;
                                chunkWithCell = chunk;

                                // Recalculate distance for the new candidate
                                Vector3 cellWorldPos = CalculateWorldPosition(cell.Position, chunk.Position);
                                closestDistance = Vector3.Distance(cellWorldPos, viewerPosition);
                            }
                            // Tie-breaking: Same entropy but closer to viewer
                            else if (effectiveEntropy == lowestEntropy)
                            {
                                Vector3 cellWorldPos = CalculateWorldPosition(cell.Position, chunk.Position);
                                float distanceToViewer = Vector3.Distance(cellWorldPos, viewerPosition);

                                if (distanceToViewer < closestDistance)
                                {
                                    cellToCollapse = cell;
                                    chunkWithCell = chunk;
                                    closestDistance = distanceToViewer;
                                }
                            }
                        }
                    }
                }
            }

            // If found a cell, collapse it
            if (cellToCollapse != null && chunkWithCell != null)
            {
                // Get constraint biases
                Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(
                    cellToCollapse, chunkWithCell, MaxCellStates);

                // Choose a state based on biases
                int[] possibleStates = cellToCollapse.PossibleStates.ToArray();
                int chosenState;

                if (biases.Count > 0 && possibleStates.Length > 1)
                {
                    // Create weighted selection based on biases
                    float[] weights = new float[possibleStates.Length];
                    float totalWeight = 0;

                    for (int i = 0; i < possibleStates.Length; i++)
                    {
                        int state = possibleStates[i];
                        weights[i] = 1.0f; // Base weight

                        // Apply bias if available
                        if (biases.TryGetValue(state, out float bias))
                        {
                            // Convert bias (-1 to 1) to weight multiplier (0.25 to 4)
                            float multiplier = Mathf.Pow(2, bias * 2);
                            weights[i] *= multiplier;
                        }

                        // Ensure weight is positive
                        weights[i] = Mathf.Max(0.1f, weights[i]);
                        totalWeight += weights[i];
                    }

                    // Random selection based on weights
                    float randomValue = UnityEngine.Random.Range(0, totalWeight);
                    float cumulativeWeight = 0;

                    chosenState = possibleStates[0]; // Default to first state

                    for (int i = 0; i < possibleStates.Length; i++)
                    {
                        cumulativeWeight += weights[i];
                        if (randomValue <= cumulativeWeight)
                        {
                            chosenState = possibleStates[i];
                            break;
                        }
                    }
                }
                else
                {
                    // No significant biases, use standard random selection
                    chosenState = possibleStates[UnityEngine.Random.Range(0, possibleStates.Length)];
                }

                // Collapse to chosen state
                cellToCollapse.Collapse(chosenState);
                PropagationEvent evt = new PropagationEvent(
                    cellToCollapse,
                    chunkWithCell,
                    new HashSet<int>(cellToCollapse.PossibleStates),
                    new HashSet<int> { chosenState },
                    cellToCollapse.IsBoundary
                );

                // Add to queue
                AddPropagationEvent(evt);
                return true;
            }

            return false;
        }

        // Helper method to calculate world position of the cell
        private Vector3 CalculateWorldPosition(Vector3Int localPosition, Vector3Int chunkPosition)
        {
            return new Vector3(
                chunkPosition.x * ChunkSize + localPosition.x,
                chunkPosition.y * ChunkSize + localPosition.y,
                chunkPosition.z * ChunkSize + localPosition.z
            );
        }

        // Helper to calculate effective entropy with constraints
        private int CalculateEffectiveEntropy(Cell cell, Chunk chunk)
        {
            // Get constraint biases
            Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(
                cell, chunk, MaxCellStates);

            // Calculate effective entropy based on biases
            int effectiveEntropy = cell.Entropy;

            // If we have strong biases, reduce effective entropy
            if (biases.Count > 0)
            {
                float maxBias = biases.Values.Max(Mathf.Abs);

                // Use configuration weights to determine entropy modification
                float constraintWeight = activeConfig.Algorithm.constraintWeight;
                float boundaryWeight = activeConfig.Algorithm.boundaryCoherenceWeight;

                // Apply weights to bias
                float adjustedBias = maxBias * constraintWeight;

                // If cell is on boundary, apply boundary weight
                if (cell.IsBoundary)
                {
                    adjustedBias *= boundaryWeight;
                }

                // Strong bias reduces effective entropy
                //if (adjustedBias > 0.7f)
                //    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.7f);
                //else if (adjustedBias > 0.3f)
                //    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.85f);
                // Find constraint influence by increasing weight for biome constraints
                if (adjustedBias > 0.7f)
                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.4f); // Stronger reduction
                else if (adjustedBias > 0.4f)
                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.6f); // Moderate reduction
                else if (adjustedBias > 0.2f)
                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.8f); // Slight reduction
            }

            return effectiveEntropy;
        }

        public HierarchicalConstraintSystem GetHierarchicalConstraintSystem()
        {
            return hierarchicalConstraints;
        }

        // Runtime configuration update method
        public void UpdateConfiguration(WFCConfiguration newConfig)
        {
            if (newConfig == null)
            {
                Debug.LogError("WFCGenerator: Cannot update to null configuration.");
                return;
            }

            // Store old values for comparison
            int oldChunkSize = ChunkSize;
            int oldMaxStates = MaxCellStates;
            Vector3Int oldWorldSize = WorldSize;

            // Update configuration
            configOverride = newConfig;
            activeConfig = newConfig;

            // Handle changes that require regeneration
            if (oldChunkSize != ChunkSize ||
                oldMaxStates != MaxCellStates ||
                oldWorldSize != WorldSize)
            {
                Debug.LogWarning("WFCGenerator: Configuration change requires world regeneration. " +
                                "Consider reinitializing the world.");

                // Could offer to automatically reinitialize here
            }
        }

        public bool ValidateAlgorithmState()
        {
            bool isValid = true;

            // Check all chunks for constraint violations
            foreach (var chunk in chunks.Values)
            {
                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            Cell cell = chunk.GetCell(x, y, z);
                            if (cell.CollapsedState.HasValue)
                            {
                                // Validate this cell doesn't violate constraints with neighbors
                                if (!ValidateCellConstraints(cell, chunk, x, y, z))
                                {
                                    isValid = false;
                                    Debug.LogError($"Constraint violation at {chunk.Position}, local: {x},{y},{z}");
                                }
                            }
                        }
                    }
                }
            }

            return isValid;
        }

        private bool ValidateCellConstraints(Cell cell, Chunk chunk, int x, int y, int z)
        {
            if (!cell.CollapsedState.HasValue)
                return true;

            int state = cell.CollapsedState.Value;

            // Check all neighboring cells
            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                Vector3Int offset = dir.ToVector3Int();
                Vector3Int neighborPos = new Vector3Int(x, y, z) + offset;

                // Skip if outside chunk
                if (!IsPositionInChunk(neighborPos, chunk.Size))
                    continue;

                Cell neighbor = chunk.GetCell(neighborPos.x, neighborPos.y, neighborPos.z);

                // Skip if neighbor not collapsed
                if (!neighbor.CollapsedState.HasValue)
                    continue;

                // Check compatibility
                if (!AreStatesCompatible(state, neighbor.CollapsedState.Value, dir))
                {
                    return false;
                }
            }

            return true;
        }

        public void ResetGeneration()
        {
            // Clear the propagation queue
            propagationQueue = new PriorityQueue<PropagationEvent, float>();

            // Reset all chunks and cells
            foreach (var chunk in chunks.Values)
            {
                chunk.IsFullyCollapsed = false;

                for (int x = 0; x < chunk.Size; x++)
                {
                    for (int y = 0; y < chunk.Size; y++)
                    {
                        for (int z = 0; z < chunk.Size; z++)
                        {
                            Cell cell = chunk.GetCell(x, y, z);
                            cell.SetPossibleStates(Enumerable.Range(0, MaxCellStates).ToHashSet());
                        }
                    }
                }

                // Reset boundary buffers
                foreach (var buffer in chunk.BoundaryBuffers.Values)
                {
                    foreach (var bufferCell in buffer.BufferCells)
                    {
                        bufferCell.SetPossibleStates(Enumerable.Range(0, MaxCellStates).ToHashSet());
                    }
                }
            }

            // Re-initialize hierarchical constraints
            InitializeHierarchicalConstraints();

            // Synchronize all buffers
            foreach (var chunk in chunks.Values)
            {
                foreach (var buffer in chunk.BoundaryBuffers.Values)
                {
                    SynchronizeBuffer(buffer);
                }
            }

            Debug.Log("WFC generation reset");
        }

        public Material[] GetStateMaterials()
        {
            return stateMaterials;
        }
    }
}