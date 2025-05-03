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
  * WFCGenerator.cs
  * -----------------------------
  * Core implementation of the Wave Function Collapse algorithm for 3D terrain generation.
  * 
  * Algorithm Overview:
  * 1. Initialize all cells with all possible states
  * 2. Repeatedly:
  *    a. Find the cell with lowest entropy (fewest possible states)
  *    b. Collapse it to a single state based on weighted probabilities
  *    c. Propagate constraints to neighboring cells
  *    d. Continue until all cells are collapsed or no further progress is possible
  * 
  * This implementation extends the original WFC with:
  * - Hierarchical constraints for terrain features
  * - Chunk-based generation for infinite worlds
  * - Boundary management for seamless transitions
  * - Parallel processing for performance optimization
  *
  */using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WFC.Boundary;
using WFC.Chunking;
using WFC.Configuration;
using WFC.Core;
using WFC.Processing;
using WFC.Terrain;
using Utils;

namespace WFC.Generation
{
    public class WFCGenerator : MonoBehaviour, IChunkProvider, IWFCAlgorithm
    {
        [Header("Configuration")]
        [SerializeField] private WFCConfiguration configOverride;

        [Header("State Settings")]
        [SerializeField] private Material[] stateMaterials;
        [SerializeField] private TerrainManager terrainManager;

        // World data
        private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
        private PriorityQueue<PropagationEvent, float> propagationQueue = new PriorityQueue<PropagationEvent, float>();
        private bool[,,] adjacencyRules;
        private HierarchicalConstraintSystem hierarchicalConstraints;
        private BoundaryBufferManager boundaryManager;

        // System references
        private TerrainDefinition currentTerrainDefinition;
        private TerrainStateRegistry stateRegistry;
        private WFCConfiguration activeConfig;
        private ParallelWFCProcessor parallelProcessor;
        private ITerrainGenerator terrainGenerator;

        // Performance optimizations
        private int frameSkipCounter = 0;
        private Dictionary<Vector3Int, Dictionary<int, float>> constraintCache = new Dictionary<Vector3Int, Dictionary<int, float>>();
        private const int MAX_EVENTS_PER_FRAME = 20;

        // Properties
        public int MaxCellStates => activeConfig.World.maxStates;
        public int ChunkSize => activeConfig.World.chunkSize;
        public Vector3Int WorldSize => new Vector3Int(
            activeConfig.World.worldSizeX,
            activeConfig.World.worldSizeY,
            activeConfig.World.worldSizeZ
        );

        // IChunkProvider implementation
        public Dictionary<Vector3Int, Chunk> GetChunks() => chunks;

        // IWFCAlgorithm implementation
        public void AddPropagationEvent(PropagationEvent evt) => propagationQueue.Enqueue(evt, evt.Priority);

        private void Awake()
        {
            InitializeComponents();
            StartCoroutine(DelayedInitialization());
        }

        private void InitializeComponents()
        {
            // Initialize registry and terrain components
            stateRegistry = TerrainStateRegistry.Instance;
            if (TerrainManager.Current == null && terrainManager != null)
            {
                terrainManager.InitializeTerrainGenerator();
                currentTerrainDefinition = terrainManager.CurrentTerrain;
                terrainGenerator = terrainManager.TerrainGenerator;
            }

            if (stateRegistry == null)
            {
                Debug.LogError("WFCGenerator: TerrainStateRegistry not found! Creating default instance.");
                stateRegistry = new TerrainStateRegistry();
            }

            // Get configuration
            activeConfig = configOverride != null ? configOverride : WFCConfigManager.Config;
            if (activeConfig == null)
            {
                Debug.LogError("WFCGenerator: No configuration available. Please assign a WFCConfiguration asset.");
                enabled = false;
                return;
            }

            if (!activeConfig.Validate())
                Debug.LogWarning("WFCGenerator: Using configuration with validation issues.");

            // Initialize terrain generator if needed
            if (terrainGenerator == null && TerrainManager.Current?.CurrentTerrain is MountainValleyTerrainDefinition mountainValley)
                terrainGenerator = new MountainValleyGenerator(mountainValley);
        }

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
        }

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
            // Process every 3rd frame for performance
            frameSkipCounter++;
            if (frameSkipCounter % 3 != 0) return;

            // Process propagation queue
            ProcessPropagationQueue();

            // Process parallel processor events
            if (parallelProcessor != null)
                parallelProcessor.ProcessMainThreadEvents();

            // Check if we need to collapse more cells
            if (propagationQueue.Count == 0)
                CollapseNextCell();
        }

        private void ProcessPropagationQueue()
        {
            int processedEvents = 0;

            while (propagationQueue.Count > 0 && processedEvents < MAX_EVENTS_PER_FRAME)
            {
                PropagationEvent evt = propagationQueue.Dequeue();
                ProcessPropagationEvent(evt);
                processedEvents++;

                // Force boundary updates for boundary events
                if (evt.IsBoundaryEvent && evt.Cell.IsBoundary)
                    boundaryManager.SynchronizeAllBuffers();
            }
        }

        private void ProcessPropagationEvent(PropagationEvent evt)
        {
            // Skip if the event is invalid
            if (evt.Cell == null || evt.Chunk == null) return;

            Cell cell = evt.Cell;
            Chunk chunk = evt.Chunk;

            // Apply constraints from hierarchy
            ApplyConstraintsToCell(cell, chunk);

            // Skip if not collapsed
            if (!cell.CollapsedState.HasValue) return;

            // Find neighbors and propagate constraints
            PropagateToNeighbors(cell, chunk);

            // Update boundary buffers if needed
            if (cell.IsBoundary && cell.BoundaryDirection.HasValue)
                boundaryManager.UpdateBuffersAfterCollapse(cell, chunk);
        }

        /*
         * PropagateToNeighbors
         * ----------------------------------------------------------------------------
         * Propagates constraints from a newly collapsed cell to its neighbors.
         * 
         * For each adjacent cell in all six directions:
         * 1. Determines if the neighbor is within the same chunk or in an adjacent chunk
         * 2. Applies constraints to in-chunk neighbors directly
         * 3. For neighbors in adjacent chunks, relies on the boundary system
         *    to handle the cross-chunk propagation
         * 
         * This is a key mechanism in the WFC algorithm that ensures collapsed states
         * create a ripple effect of constraints, gradually reducing entropy across
         * the grid in a coherent manner.
         * 
         * Parameters:
         * - cell: The newly collapsed cell that is the source of constraints
         * - chunk: The chunk containing the cell
         */
        private void PropagateToNeighbors(Cell cell, Chunk chunk)
        {
            // Find cell position in chunk
            Vector3Int? cellPos = FindCellPosition(cell, chunk);
            if (!cellPos.HasValue) return;

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
                // Neighbors in adjacent chunks are handled by the boundary system
            }
        }

        private Vector3Int? FindCellPosition(Cell cell, Chunk chunk)
        {
            for (int x = 0; x < chunk.Size; x++)
                for (int y = 0; y < chunk.Size; y++)
                    for (int z = 0; z < chunk.Size; z++)
                        if (chunk.GetCell(x, y, z) == cell)
                            return new Vector3Int(x, y, z);

            return null;
        }

        private bool IsPositionInChunk(Vector3Int pos, int chunkSize)
        {
            return pos.x >= 0 && pos.x < chunkSize &&
                   pos.y >= 0 && pos.y < chunkSize &&
                   pos.z >= 0 && pos.z < chunkSize;
        }

        /*
        * ApplyConstraint
        * -----------------------------
        * Applies constraints from a collapsed cell to a neighboring cell.
        * 
        * This is a key part of the constraint propagation system in WFC:
        * 1. Start with the full set of possible states for the target cell
        * 2. Filter out states that are incompatible with the source cell's state
        * 3. Update the target cell's possible states
        * 4. If the cell's possible states change, queue a new propagation event
        * 
        * The compatibility between states is determined by the adjacency rules
        * defined in the SetupAdjacencyRules method. These rules define which
        * states can be placed adjacent to each other in each direction.
        */
        private void ApplyConstraint(Cell cell, int constraintState, Direction direction, Chunk chunk)
        {
            // Skip if already collapsed
            if (cell.CollapsedState.HasValue) return;

            // Store old states
            HashSet<int> oldStates = new HashSet<int>(cell.PossibleStates);
            HashSet<int> compatibleStates = new HashSet<int>();

            // Find compatible states
            foreach (int state in cell.PossibleStates)
                if (AreStatesCompatible(state, constraintState, direction))
                    compatibleStates.Add(state);

            // Update possible states if changed
            if (compatibleStates.Count > 0 && !compatibleStates.SetEquals(oldStates))
            {
                cell.SetPossibleStates(compatibleStates);

                // Create propagation event
                PropagationEvent evt = new PropagationEvent(
                    cell, chunk, oldStates, compatibleStates, cell.IsBoundary);
                AddPropagationEvent(evt);
            }
        }

        private void InitializeHierarchicalConstraints()
        {
            // Create the hierarchical constraint system
            hierarchicalConstraints = new HierarchicalConstraintSystem(ChunkSize);

            // Apply constraints from terrain definition if available
            if (terrainGenerator != null)
                terrainGenerator.ApplyConstraints(hierarchicalConstraints, WorldSize, ChunkSize);
            else
                ApplyBasicConstraints(hierarchicalConstraints, WorldSize, ChunkSize);
        }

        private void ApplyBasicConstraints(HierarchicalConstraintSystem system, Vector3Int worldSize, int chunkSize)
        {
            // Simple ground level constraint
            GlobalConstraint groundConstraint = new GlobalConstraint
            {
                Name = "Ground Level",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(worldSize.x * chunkSize / 2, 0, worldSize.z * chunkSize / 2),
                WorldSize = new Vector3(worldSize.x * chunkSize, 0, worldSize.z * chunkSize),
                BlendRadius = chunkSize,
                Strength = 0.8f,
                MinHeight = 0,
                MaxHeight = worldSize.y * chunkSize
            };

            // Simple ground state biases
            groundConstraint.StateBiases[0] = -0.9f; // Empty (negative bias)
            groundConstraint.StateBiases[1] = 0.6f;  // Ground (positive bias)

            system.AddGlobalConstraint(groundConstraint);
        }

        /*
         * SetupAdjacencyRules
         * ----------------------------------------------------------------------------
         * Defines the adjacency rules that determine which states can be neighbors.
         * 
         * The rules are stored in a 3D array [stateA, stateB, direction] where:
         * - stateA, stateB: The two states being checked for compatibility
         * - direction: One of six directions (Up, Down, Left, Right, Forward, Back)
         * 
         * The rules define the physical constraints of the terrain, such as:
         * - Air can only be above solid states, not adjacent horizontally
         * - Ground/rock/sand/grass can all connect to each other
         * - Water must have solid states beneath and can border other solid states
         * - Trees can only grow on grass/ground
         * 
         * These rules ensure that the generated terrain is physically plausible
         * and has natural transitions between different elements.
         */
        private void SetupAdjacencyRules()
        {
            // Initialize all to false
            for (int i = 0; i < MaxCellStates; i++)
                for (int j = 0; j < MaxCellStates; j++)
                    for (int d = 0; d < 6; d++)
                        adjacencyRules[i, j, d] = false;

            // Load adjacency rules from active terrain definition
            TerrainDefinition currentTerrain = TerrainManager.Current?.CurrentTerrain;

            if (currentTerrain != null && currentTerrain.AdjacencyRules != null)
            {
                // Copy rules from terrain definition
                var rules = currentTerrain.AdjacencyRules;
                int minI = Math.Min(MaxCellStates, rules.GetLength(0));
                int minJ = Math.Min(MaxCellStates, rules.GetLength(1));
                int minD = Math.Min(6, rules.GetLength(2));

                for (int i = 0; i < minI; i++)
                    for (int j = 0; j < minJ; j++)
                        for (int d = 0; d < minD; d++)
                            adjacencyRules[i, j, d] = rules[i, j, d];

                Debug.Log("Loaded adjacency rules from terrain definition");
            }
            else
            {
                // Fallback to very basic adjacency rules
                Debug.LogWarning("No terrain definition found, using default adjacency rules");

                // Air can be adjacent to any state
                for (int i = 0; i < MaxCellStates; i++)
                    SetAdjacentAll(0, i, true);

                // Same state adjacency is always allowed
                for (int i = 0; i < MaxCellStates; i++)
                    SetAdjacentAll(i, i, true);
            }
        }

        private void SetAdjacentAll(int stateA, int stateB, bool canBeAdjacent)
        {
            for (int d = 0; d < 6; d++)
            {
                adjacencyRules[stateA, stateB, d] = canBeAdjacent;
                adjacencyRules[stateB, stateA, d] = canBeAdjacent;
            }
        }

        /*
         * ApplyConstraintsToCell
         * ----------------------------------------------------------------------------
         * Applies hierarchical constraints to influence a cell's possible states.
         * 
         * This function does several important things:
         * 1. Checks if cell is already collapsed (exits if so)
         * 2. Gets the world position of the cell
         * 3. Gets constraint biases from the hierarchical constraint system
         * 4. Uses a strong height-based constraint system to eliminate holes:
         *    - Forces air above certain height
         *    - Forces solid terrain below certain height
         * 5. For mid-heights, applies weighted biases to state probabilities
         * 6. May directly collapse cells if constraint bias is very strong
         * 
         * The height-based constraints ensure proper terrain layering vertically,
         * while preserving horizontal variation guided by the biome constraints.
         * 
         * Parameters:
         * - cell: The cell to apply constraints to
         * - chunk: The chunk containing the cell
         */
        private void ApplyConstraintsToCell(Cell cell, Chunk chunk)
        {
            // Skip if constraints are disabled or cell is already collapsed
            if (!activeConfig.Algorithm.useConstraints || hierarchicalConstraints == null || cell.CollapsedState.HasValue)
                return;

            // Get cached constraints or calculate new ones
            Vector3Int cacheKey = new Vector3Int(chunk.Position.x, chunk.Position.y, chunk.Position.z);
            if (!constraintCache.TryGetValue(cacheKey, out var biases))
            {
                var rawBiases = hierarchicalConstraints.CalculateConstraintInfluence(cell, chunk, MaxCellStates);
                biases = new Dictionary<int, float>();

                foreach (var bias in rawBiases)
                    biases[bias.Key] = bias.Value * chunk.ConstraintInfluence;

                // Cache for later use
                constraintCache[cacheKey] = biases;
            }

            // World position for height-based logic
            Vector3 worldPos = CalculateWorldPosition(cell.Position, chunk.Position);
            float heightRatio = worldPos.y / (WorldSize.y * ChunkSize);

            // Force air above certain height
            if (heightRatio > 0.8f)
            {
                cell.Collapse(0); // Force air at heights
                return;
            }

            // Force solid below certain height
            if (heightRatio < 0.2f)
            {
                // Look at biases to decide which solid state
                if (biases.TryGetValue(4, out float rockBias) && rockBias > 0.3f)
                    cell.Collapse(4); // Rock
                else if (biases.TryGetValue(3, out float waterBias) && waterBias > 0.3f)
                    cell.Collapse(3); // Water
                else
                    cell.Collapse(1); // Default to ground
                return;
            }

            // Normal processing for middle heights - if no significant biases, return
            if (!biases.Values.Any(v => Mathf.Abs(v) > 0.01f))
                return;

            // Get current possible states
            HashSet<int> currentStates = new HashSet<int>(cell.PossibleStates);
            if (currentStates.Count <= 1) return;

            // Calculate adjustment probability for each state based on biases
            Dictionary<int, float> stateWeights = new Dictionary<int, float>();
            float totalWeight = 0;

            foreach (int state in currentStates)
            {
                float weight = 1.0f; // Base weight

                // Apply bias adjustment if exists
                if (biases.TryGetValue(state, out float bias))
                    weight *= (1.0f + bias);

                // Ensure weight is positive
                weight = Mathf.Max(0.01f, weight);
                stateWeights[state] = weight;
                totalWeight += weight;
            }

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
                    break;
                }
            }
        }

        public bool AreStatesCompatible(int stateA, int stateB, Direction direction)
        {
            // Initialize rules lazily if needed
            if (adjacencyRules == null)
            {
                adjacencyRules = new bool[MaxCellStates, MaxCellStates, 6];
                SetupAdjacencyRules();
            }

            // Check bounds
            if (stateA >= MaxCellStates || stateB >= MaxCellStates)
                return false;

            // Check the adjacency rules table
            return adjacencyRules[stateA, stateB, (int)direction];
        }

        /*
         * CollapseNextCell
         * -----------------------------
         * Core function of the WFC algorithm that selects and collapses one cell.
         * 
         * Algorithm Steps:
         * 1. Find cell with minimum entropy (fewest possible states)
         * 2. If multiple cells tie for minimum entropy, select the one closest to viewer
         * 3. Apply constraint biases to influence state selection probabilities
         * 4. Randomly select a state based on weighted probabilities
         * 5. Collapse the cell to the selected state
         * 6. Create a propagation event to update neighboring cells
         * 
         * The weighted probability calculation uses a bias-to-weight conversion:
         * weight = 2^(bias * 2)
         * This gives an exponential effect where:
         * - Bias of 0 = weight of 1 (neutral)
         * - Bias of 0.5 = weight of 2 (twice as likely)
         * - Bias of -0.5 = weight of 0.5 (half as likely)
         */
        private bool CollapseNextCell()
        {
            // Performance optimization - cache viewer position
            Vector3 viewerPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

            // Find the cell with lowest entropy
            Cell cellToCollapse = null;
            Chunk chunkWithCell = null;
            int lowestEntropy = int.MaxValue;
            float closestDistance = float.MaxValue;

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

                            // Skip already collapsed cells or cells with only one state
                            if (cell.CollapsedState.HasValue || cell.Entropy <= 1)
                                continue;

                            // Calculate effective entropy based on constraints
                            int effectiveEntropy = CalculateEffectiveEntropy(cell, chunk);

                            // First priority: Lowest entropy
                            if (effectiveEntropy < lowestEntropy)
                            {
                                lowestEntropy = effectiveEntropy;
                                cellToCollapse = cell;
                                chunkWithCell = chunk;

                                // Calculate distance for the new candidate
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

                // Create propagation event
                PropagationEvent evt = new PropagationEvent(
                    cellToCollapse,
                    chunkWithCell,
                    new HashSet<int>(possibleStates),
                    new HashSet<int> { chosenState },
                    cellToCollapse.IsBoundary
                );

                // Add to queue
                AddPropagationEvent(evt);
                return true;
            }

            return false;
        }

        private Vector3 CalculateWorldPosition(Vector3Int localPosition, Vector3Int chunkPosition)
        {
            return new Vector3(
                chunkPosition.x * ChunkSize + localPosition.x,
                chunkPosition.y * ChunkSize + localPosition.y,
                chunkPosition.z * ChunkSize + localPosition.z
            );
        }

        private int CalculateEffectiveEntropy(Cell cell, Chunk chunk)
        {
            // Get constraint biases
            Dictionary<int, float> biases = hierarchicalConstraints.CalculateConstraintInfluence(
                cell, chunk, MaxCellStates);

            // Base entropy
            int effectiveEntropy = cell.Entropy;

            // If have strong biases, reduce effective entropy
            if (biases.Count > 0)
            {
                float maxBias = biases.Values.Max(Mathf.Abs);
                float adjustedBias = maxBias * activeConfig.Algorithm.constraintWeight;

                // If cell is on boundary, apply boundary weight
                if (cell.IsBoundary)
                    adjustedBias *= activeConfig.Algorithm.boundaryCoherenceWeight;

                // Reduce entropy based on bias strength
                if (adjustedBias > 0.7f)
                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.4f);
                else if (adjustedBias > 0.4f)
                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.6f);
                else if (adjustedBias > 0.2f)
                    effectiveEntropy = Mathf.FloorToInt(effectiveEntropy * 0.8f);
            }

            return effectiveEntropy;
        }

        // Get the current hierarchical constraint system (public API)
        public HierarchicalConstraintSystem GetHierarchicalConstraintSystem() => hierarchicalConstraints;

        // Runtime configuration update method (public API)
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

            // Clear constraint cache when configuration changes
            constraintCache.Clear();

            // Handle changes that require regeneration
            if (oldChunkSize != ChunkSize || oldMaxStates != MaxCellStates || oldWorldSize != WorldSize)
            {
                Debug.LogWarning("WFCGenerator: Configuration change requires world regeneration. " +
                                "Consider reinitializing the world.");
            }
        }

        // Generate tree models for a chunk (public API)
        public void GenerateTreeModels(Chunk chunk)
        {
            // Skip if not a ground-level chunk
            if (chunk.Position.y != 0) return;

            // Get references to tree prefabs
            GameObject[] treePrefabs = Resources.LoadAll<GameObject>("TreePrefabs");
            if (treePrefabs.Length == 0)
            {
                Debug.LogWarning("No tree prefabs found in Resources/TreePrefabs folder");
                return;
            }

            // Create parent object for trees
            string treeParentName = $"Trees_Chunk_{chunk.Position.x}_{chunk.Position.y}_{chunk.Position.z}";
            GameObject treeParent = new GameObject(treeParentName);
            treeParent.transform.position = new Vector3(
                chunk.Position.x * ChunkSize,
                chunk.Position.y * ChunkSize,
                chunk.Position.z * ChunkSize
            );

            // Sample for forest cells (state 6)
            for (int x = 0; x < chunk.Size; x++)
            {
                for (int z = 0; z < chunk.Size; z++)
                {
                    // Find top forest cell in column
                    int maxForestY = -1;
                    for (int y = chunk.Size - 1; y >= 0; y--)
                    {
                        Cell cell = chunk.GetCell(x, y, z);
                        if (cell?.CollapsedState == 6) // Forest state
                        {
                            maxForestY = y;
                            break;
                        }
                    }

                    // Skip if no forest cell found
                    if (maxForestY < 0) continue;

                    // Place tree with randomization
                    if (UnityEngine.Random.value < 0.3f) // Control density
                    {
                        // Calculate position with slight randomization
                        Vector3 treePos = new Vector3(
                            chunk.Position.x * ChunkSize + x + UnityEngine.Random.Range(-0.3f, 0.3f),
                            chunk.Position.y * ChunkSize + maxForestY + 0.5f, // Offset above ground
                            chunk.Position.z * ChunkSize + z + UnityEngine.Random.Range(-0.3f, 0.3f)
                        );

                        // Select random tree prefab
                        GameObject treePrefab = treePrefabs[UnityEngine.Random.Range(0, treePrefabs.Length)];

                        // Instantiate and randomize
                        GameObject tree = Instantiate(treePrefab, treePos, Quaternion.identity, treeParent.transform);
                        float scale = UnityEngine.Random.Range(0.8f, 1.2f);
                        tree.transform.localScale = Vector3.one * scale;
                        tree.transform.Rotate(0, UnityEngine.Random.Range(0, 360), 0);
                    }
                }
            }
        }
    }
}