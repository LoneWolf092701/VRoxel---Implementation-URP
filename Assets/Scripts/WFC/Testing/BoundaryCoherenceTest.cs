using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;
using WFC.Core;
using WFC.Testing;
using WFC.Boundary;
using System.Linq;

/// <summary>
/// Test harness for evaluating boundary coherence in Wave Function Collapse generation
/// </summary>
public class BoundaryCoherenceTest : MonoBehaviour
{
    [SerializeField] private WFCTestController testController;

    [Header("Boundary Test Configuration")]
    [SerializeField] private Vector3Int testChunkCoordinate1;
    [SerializeField] private Vector3Int testChunkCoordinate2;
    [SerializeField] private Direction boundaryDirection;
    [SerializeField] private bool forceBoundaryConflict = false;
    [SerializeField] private int conflictStateA = 0;
    [SerializeField] private int conflictStateB = 0;

    [Header("Advanced Metrics")]
    [SerializeField] private bool enableAdvancedMetrics = true;
    [SerializeField] private string outputFilePath = "boundary_metrics.csv";
    [SerializeField] private bool automaticallyTestAllBoundaries = false;
    [SerializeField] private int sampleFrequency = 10; // Test every N frames

    [Header("Visualization")]
    [SerializeField] private bool highlightBoundaryCells = true;
    [SerializeField] private Material boundaryHighlightMaterial;
    [SerializeField] private Color conflictColor = Color.red;
    [SerializeField] private Color compatibleColor = Color.green;
    [SerializeField] private Color lowEntropyColor = Color.yellow;
    [SerializeField] private Color highEntropyColor = Color.blue;

    // Statistics
    private int boundaryConflictCount = 0;
    private int successfulBoundaryTransitions = 0;
    private float coherenceScore = 0f;
    private int frameCounter = 0;

    // Cached data
    private Dictionary<Cell, GameObject> highlightObjects = new Dictionary<Cell, GameObject>();
    private List<BoundaryMetrics> historicalMetrics = new List<BoundaryMetrics>();

    /// <summary>
    /// Class to store comprehensive boundary metrics
    /// </summary>
    private class BoundaryMetrics
    {
        public Vector3Int ChunkPosition;
        public Vector3Int NeighborPosition;
        public Direction Direction;
        public float OverallCoherence;
        public float StateDistributionEntropy;
        public float PatternContinuity;
        public float TransitionSmoothness;
        public int ConflictCount;
        public int TotalCells;
        public int CollapsedCellCount;
        public float CollapsedPercentage;
        public float LocalStateVariety;
        public string Timestamp;

        public BoundaryMetrics()
        {
            Timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public override string ToString()
        {
            return $"Boundary {ChunkPosition}-{NeighborPosition} ({Direction}): " +
                   $"Coherence={OverallCoherence:F3}, Conflicts={ConflictCount}/{TotalCells}, " +
                   $"Entropy={StateDistributionEntropy:F3}, Smoothness={TransitionSmoothness:F3}";
        }
    }

    private void Update()
    {
        // Automatic testing if enabled
        if (automaticallyTestAllBoundaries && frameCounter++ % sampleFrequency == 0)
        {
            RunComprehensiveTest();
        }
    }

    /// <summary>
    /// Runs a comprehensive test on all chunk boundaries in the world
    /// </summary>
    public void RunComprehensiveTest()
    {
        List<BoundaryMetrics> allMetrics = new List<BoundaryMetrics>();

        // Test all boundaries in the world
        var chunks = testController.GetChunks();

        foreach (var chunkEntry in chunks)
        {
            Vector3Int chunkPos = chunkEntry.Key;
            Chunk chunk = chunkEntry.Value;

            // Test each direction that has a neighbor
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                if (!chunk.Neighbors.ContainsKey(dir))
                    continue;

                Chunk neighbor = chunk.Neighbors[dir];

                // Get boundary cells
                var boundaryCells1 = GetBoundaryCells(chunk, dir);
                var boundaryCells2 = GetBoundaryCells(neighbor, dir.GetOpposite());

                // Calculate metrics
                var metrics = CalculateAdvancedMetrics(boundaryCells1, boundaryCells2, dir);
                metrics.ChunkPosition = chunkPos;
                metrics.NeighborPosition = neighbor.Position;
                metrics.Direction = dir;
                metrics.TotalCells = boundaryCells1.Count;

                // Count collapsed cells
                int collapsedCount = 0;
                foreach (var cell in boundaryCells1.Concat(boundaryCells2))
                {
                    if (cell.CollapsedState.HasValue)
                        collapsedCount++;
                }
                metrics.CollapsedCellCount = collapsedCount;
                metrics.CollapsedPercentage = collapsedCount / (float)(boundaryCells1.Count * 2);

                allMetrics.Add(metrics);
            }
        }

        // Calculate overall statistics
        float avgCoherence = allMetrics.Average(m => m.OverallCoherence);
        float avgTransitionSmoothness = allMetrics.Average(m => m.TransitionSmoothness);
        float avgEntropy = allMetrics.Average(m => m.StateDistributionEntropy);
        float avgPatternContinuity = allMetrics.Average(m => m.PatternContinuity);
        int totalConflicts = allMetrics.Sum(m => m.ConflictCount);

        Debug.Log($"Comprehensive Boundary Analysis:");
        Debug.Log($"Overall boundary coherence: {avgCoherence:F3}");
        Debug.Log($"Average transition smoothness: {avgTransitionSmoothness:F3}");
        Debug.Log($"Average state distribution entropy: {avgEntropy:F3}");
        Debug.Log($"Average pattern continuity: {avgPatternContinuity:F3}");
        Debug.Log($"Total conflicts across all boundaries: {totalConflicts}");
        Debug.Log($"Boundaries analyzed: {allMetrics.Count}");

        // Output to CSV if requested
        if (!string.IsNullOrEmpty(outputFilePath))
        {
            OutputMetricsToCSV(allMetrics, outputFilePath);
        }

        // Store historical data
        historicalMetrics.AddRange(allMetrics);
    }

    /// <summary>
    /// Runs a basic boundary test between two specific chunks
    /// </summary>
    public void RunBoundaryTest()
    {
        ClearPreviousHighlights();

        // Get chunks to test
        if (!testController.GetChunks().TryGetValue(testChunkCoordinate1, out Chunk chunk1) ||
            !testController.GetChunks().TryGetValue(testChunkCoordinate2, out Chunk chunk2))
        {
            Debug.LogError("Invalid test chunk coordinates!");
            return;
        }

        // Get boundary cells
        var boundaryCells1 = GetBoundaryCells(chunk1, boundaryDirection);
        var boundaryCells2 = GetBoundaryCells(chunk2, boundaryDirection.GetOpposite());

        // Calculate and log statistics
        coherenceScore = CalculateBoundaryCoherence(boundaryCells1, boundaryCells2);
        LogBoundaryStatistics(boundaryCells1, boundaryCells2);

        // Visualize boundaries
        if (highlightBoundaryCells)
        {
            HighlightBoundaryCells(boundaryCells1, boundaryCells2);
        }

        // Optionally force a conflict for testing resolution strategies
        if (forceBoundaryConflict)
        {
            if (boundaryCells1.Count > 0 && boundaryCells2.Count > 0)
            {
                CreateBoundaryConflict(boundaryCells1[0], boundaryCells2[0]);
                Debug.Log("Boundary conflict created for testing");
            }
        }

        // Calculate advanced metrics if enabled
        if (enableAdvancedMetrics)
        {
            var metrics = CalculateAdvancedMetrics(boundaryCells1, boundaryCells2, boundaryDirection);
            Debug.Log($"Advanced Metrics:");
            Debug.Log($"State Distribution Entropy: {metrics.StateDistributionEntropy:F3}");
            Debug.Log($"Pattern Continuity: {metrics.PatternContinuity:F3}");
            Debug.Log($"Transition Smoothness: {metrics.TransitionSmoothness:F3}");
        }
    }

    /// <summary>
    /// Calculate comprehensive metrics for a boundary
    /// </summary>
    private BoundaryMetrics CalculateAdvancedMetrics(List<Cell> cells1, List<Cell> cells2, Direction dir)
    {
        BoundaryMetrics metrics = new BoundaryMetrics();

        // Basic coherence (existing calculation)
        metrics.OverallCoherence = CalculateBoundaryCoherence(cells1, cells2);
        metrics.ConflictCount = boundaryConflictCount;

        // Calculate state distribution entropy
        metrics.StateDistributionEntropy = CalculateStateDistributionEntropy(cells1, cells2);

        // Calculate pattern continuity
        metrics.PatternContinuity = CalculatePatternContinuity(cells1, cells2);

        // Calculate transition smoothness
        metrics.TransitionSmoothness = CalculateTransitionSmoothness(cells1, cells2, dir);

        // Calculate local state variety
        metrics.LocalStateVariety = CalculateLocalStateVariety(cells1, cells2);

        return metrics;
    }

    /// <summary>
    /// Gets boundary cells for a specific direction
    /// </summary>
    private List<Cell> GetBoundaryCells(Chunk chunk, Direction direction)
    {
        List<Cell> cells = new List<Cell>();
        int size = chunk.Size;

        switch (direction)
        {
            case Direction.Left: // -X boundary
                for (int y = 0; y < size; y++)
                {
                    for (int z = 0; z < size; z++)
                    {
                        cells.Add(chunk.GetCell(0, y, z));
                    }
                }
                break;

            case Direction.Right: // +X boundary
                for (int y = 0; y < size; y++)
                {
                    for (int z = 0; z < size; z++)
                    {
                        cells.Add(chunk.GetCell(size - 1, y, z));
                    }
                }
                break;

            case Direction.Down: // -Y boundary
                for (int x = 0; x < size; x++)
                {
                    for (int z = 0; z < size; z++)
                    {
                        cells.Add(chunk.GetCell(x, 0, z));
                    }
                }
                break;

            case Direction.Up: // +Y boundary
                for (int x = 0; x < size; x++)
                {
                    for (int z = 0; z < size; z++)
                    {
                        cells.Add(chunk.GetCell(x, size - 1, z));
                    }
                }
                break;

            case Direction.Back: // -Z boundary
                for (int x = 0; x < size; x++)
                {
                    for (int y = 0; y < size; y++)
                    {
                        cells.Add(chunk.GetCell(x, y, 0));
                    }
                }
                break;

            case Direction.Forward: // +Z boundary
                for (int x = 0; x < size; x++)
                {
                    for (int y = 0; y < size; y++)
                    {
                        cells.Add(chunk.GetCell(x, y, size - 1));
                    }
                }
                break;
        }

        return cells;
    }

    /// <summary>
    /// Calculate basic boundary coherence metrics
    /// </summary>
    private float CalculateBoundaryCoherence(List<Cell> cells1, List<Cell> cells2)
    {
        if (cells1.Count != cells2.Count || cells1.Count == 0)
            return 0f;

        int compatibleCount = 0;
        boundaryConflictCount = 0;
        successfulBoundaryTransitions = 0;

        // Check compatibility of corresponding cells
        for (int i = 0; i < cells1.Count; i++)
        {
            Cell cell1 = cells1[i];
            Cell cell2 = cells2[i];

            // If both cells are collapsed, check direct compatibility
            if (cell1.CollapsedState.HasValue && cell2.CollapsedState.HasValue)
            {
                if (AreStatesCompatible(cell1, cell2, boundaryDirection))
                {
                    compatibleCount++;
                    successfulBoundaryTransitions++;
                }
                else
                {
                    boundaryConflictCount++;
                }
            }
            // If one or both are uncollapsed, check potential compatibility
            else
            {
                bool potentiallyCompatible = CheckPotentialCompatibility(cell1, cell2, boundaryDirection);
                if (potentiallyCompatible)
                {
                    compatibleCount++;
                }
            }
        }

        // Calculate coherence score (0.0 to 1.0)
        return (float)compatibleCount / cells1.Count;
    }

    /// <summary>
    /// Calculate state distribution entropy across the boundary
    /// </summary>
    private float CalculateStateDistributionEntropy(List<Cell> cells1, List<Cell> cells2)
    {
        // Count state occurrences
        Dictionary<int, int> stateCounts = new Dictionary<int, int>();

        foreach (var cell in cells1.Concat(cells2))
        {
            if (cell.CollapsedState.HasValue)
            {
                int state = cell.CollapsedState.Value;
                if (!stateCounts.ContainsKey(state))
                    stateCounts[state] = 0;
                stateCounts[state]++;
            }
        }

        // Calculate entropy
        float entropy = 0;
        int totalStates = stateCounts.Values.Sum();

        if (totalStates > 0)
        {
            foreach (var count in stateCounts.Values)
            {
                float probability = count / (float)totalStates;
                entropy -= probability * Mathf.Log(probability, 2);
            }
        }

        return entropy;
    }

    /// <summary>
    /// Calculate pattern continuity across the boundary
    /// </summary>
    private float CalculatePatternContinuity(List<Cell> cells1, List<Cell> cells2)
    {
        // This metric measures how well patterns continue across the boundary
        // For example, if a pattern of alternating states continues across the boundary

        if (cells1.Count != cells2.Count || cells1.Count < 3)
            return 0f;

        int continuityScore = 0;
        int totalPairs = 0;

        // Check for pattern continuity in collapsed cells
        for (int i = 0; i < cells1.Count - 1; i++)
        {
            // Check for pattern on first side
            if (cells1[i].CollapsedState.HasValue && cells1[i + 1].CollapsedState.HasValue)
            {
                int pattern1 = cells1[i + 1].CollapsedState.Value - cells1[i].CollapsedState.Value;

                // Check corresponding cells on second side
                if (cells2[i].CollapsedState.HasValue && cells2[i + 1].CollapsedState.HasValue)
                {
                    int pattern2 = cells2[i + 1].CollapsedState.Value - cells2[i].CollapsedState.Value;

                    // If patterns match (same difference between states)
                    if (pattern1 == pattern2)
                        continuityScore++;

                    totalPairs++;
                }
            }
        }

        return totalPairs > 0 ? (float)continuityScore / totalPairs : 0f;
    }

    /// <summary>
    /// Calculate transition smoothness across the boundary
    /// </summary>
    private float CalculateTransitionSmoothness(List<Cell> cells1, List<Cell> cells2, Direction direction)
    {
        // This metric measures how smooth the transition is across the boundary
        // Smooth transitions have similar states on both sides

        if (cells1.Count != cells2.Count || cells1.Count == 0)
            return 0f;

        float smoothTransitions = 0;
        int totalTransitions = 0;

        for (int i = 0; i < cells1.Count; i++)
        {
            if (cells1[i].CollapsedState.HasValue && cells2[i].CollapsedState.HasValue)
            {
                int state1 = cells1[i].CollapsedState.Value;
                int state2 = cells2[i].CollapsedState.Value;

                // Calculate similarity (difference between states)
                // The smaller the difference, the smoother the transition
                float difference = Mathf.Abs(state1 - state2);
                float maxStateDifference = testController.MaxStates - 1; // Maximum possible difference

                // Normalize to 0-1 range and invert (1 is smooth, 0 is abrupt)
                float smoothness = 1.0f - (difference / maxStateDifference);

                smoothTransitions += smoothness;
                totalTransitions++;
            }
        }

        return totalTransitions > 0 ? smoothTransitions / totalTransitions : 0f;
    }

    /// <summary>
    /// Calculate local state variety on each side of the boundary
    /// </summary>
    private float CalculateLocalStateVariety(List<Cell> cells1, List<Cell> cells2)
    {
        // This measures the variety of states within each side of the boundary
        // Higher values indicate more diverse states

        HashSet<int> uniqueStates1 = new HashSet<int>();
        HashSet<int> uniqueStates2 = new HashSet<int>();

        foreach (var cell in cells1)
        {
            if (cell.CollapsedState.HasValue)
                uniqueStates1.Add(cell.CollapsedState.Value);
        }

        foreach (var cell in cells2)
        {
            if (cell.CollapsedState.HasValue)
                uniqueStates2.Add(cell.CollapsedState.Value);
        }

        // Calculate variety as ratio of unique states to maximum possible states
        float variety1 = uniqueStates1.Count / (float)testController.MaxStates;
        float variety2 = uniqueStates2.Count / (float)testController.MaxStates;

        // Average variety on both sides
        return (variety1 + variety2) / 2.0f;
    }

    /// <summary>
    /// Check if two cells are compatible across a boundary
    /// </summary>
    private bool AreStatesCompatible(Cell cell1, Cell cell2, Direction direction)
    {
        if (!cell1.CollapsedState.HasValue || !cell2.CollapsedState.HasValue)
            return true; // Uncollapsed cells are potentially compatible

        return testController.AreStatesCompatible(cell1.CollapsedState.Value, cell2.CollapsedState.Value, direction);
    }

    /// <summary>
    /// Check if uncollapsed cells have potential to be compatible
    /// </summary>
    private bool CheckPotentialCompatibility(Cell cell1, Cell cell2, Direction direction)
    {
        foreach (int state1 in cell1.PossibleStates)
        {
            foreach (int state2 in cell2.PossibleStates)
            {
                if (testController.AreStatesCompatible(state1, state2, direction))
                {
                    return true; // Found at least one compatible state pair
                }
            }
        }
        return false; // No compatible state pairs found
    }

    /// <summary>
    /// Output metrics to CSV file
    /// </summary>
    private void OutputMetricsToCSV(List<BoundaryMetrics> metrics, string filePath)
    {
        StringBuilder csvContent = new StringBuilder();
        bool fileExists = File.Exists(filePath);

        // Write header if file doesn't exist
        if (!fileExists)
        {
            csvContent.AppendLine("Timestamp,ChunkX,ChunkY,ChunkZ,NeighborX,NeighborY,NeighborZ,Direction," +
                                 "Coherence,Conflicts,TotalCells,StateEntropy,PatternContinuity,TransitionSmoothness," +
                                 "CollapsedCells,CollapsedPercentage,StateVariety");
        }

        // Write data rows
        foreach (var metric in metrics)
        {
            csvContent.AppendLine(
                $"{metric.Timestamp}," +
                $"{metric.ChunkPosition.x},{metric.ChunkPosition.y},{metric.ChunkPosition.z}," +
                $"{metric.NeighborPosition.x},{metric.NeighborPosition.y},{metric.NeighborPosition.z}," +
                $"{metric.Direction}," +
                $"{metric.OverallCoherence:F4},{metric.ConflictCount},{metric.TotalCells}," +
                $"{metric.StateDistributionEntropy:F4},{metric.PatternContinuity:F4},{metric.TransitionSmoothness:F4}," +
                $"{metric.CollapsedCellCount},{metric.CollapsedPercentage:F4},{metric.LocalStateVariety:F4}");
        }

        // Append to existing file or create new one
        try
        {
            File.AppendAllText(filePath, csvContent.ToString());
            Debug.Log($"Metrics successfully written to {filePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error writing metrics to CSV: {e.Message}");
        }
    }

    /// <summary>
    /// Log test statistics to the console
    /// </summary>
    private void LogBoundaryStatistics(List<Cell> cells1, List<Cell> cells2)
    {
        int totalCells = cells1.Count;
        int collapsedCount = 0;

        // Count collapsed cells
        foreach (var cell in cells1.Concat(cells2))
        {
            if (cell.CollapsedState.HasValue)
                collapsedCount++;
        }

        // Log statistics
        Debug.Log($"Boundary Coherence Test Results:");
        Debug.Log($"- Total boundary cells: {totalCells} per side");
        Debug.Log($"- Collapsed cells: {collapsedCount}/{totalCells * 2} ({collapsedCount / (float)(totalCells * 2) * 100:F1}%)");
        Debug.Log($"- Successful transitions: {successfulBoundaryTransitions}");
        Debug.Log($"- Boundary conflicts: {boundaryConflictCount}");
        Debug.Log($"- Coherence score: {coherenceScore:F3} (0.0-1.0)");
    }

    /// <summary>
    /// Force a conflict between two cells for testing resolution
    /// </summary>
    private void CreateBoundaryConflict(Cell cell1, Cell cell2)
    {
        // Force specific states that are incompatible
        if (testController.AreStatesCompatible(conflictStateA, conflictStateB, boundaryDirection))
        {
            Debug.LogWarning("The selected states are actually compatible! Choose different states for conflict testing.");
            return;
        }

        // Collapse the cells to incompatible states
        cell1.Collapse(conflictStateA);
        cell2.Collapse(conflictStateB);

        // Update visualization
        if (highlightBoundaryCells)
        {
            HighlightConflict(cell1, cell2);
        }
    }

    /// <summary>
    /// Highlight the boundary cells to visualize compatibility
    /// </summary>
    private void HighlightBoundaryCells(List<Cell> cells1, List<Cell> cells2)
    {
        if (cells1.Count != cells2.Count)
            return;

        for (int i = 0; i < cells1.Count; i++)
        {
            Cell cell1 = cells1[i];
            Cell cell2 = cells2[i];

            bool compatible = AreStatesCompatible(cell1, cell2, boundaryDirection);

            // Create or update highlight for cell1
            CreateHighlight(cell1, compatible ? compatibleColor : conflictColor);

            // Create or update highlight for cell2
            CreateHighlight(cell2, compatible ? compatibleColor : conflictColor);
        }
    }

    /// <summary>
    /// Create a highlight object for a cell
    /// </summary>
    private void CreateHighlight(Cell cell, Color color)
    {
        if (highlightObjects.TryGetValue(cell, out GameObject highlight))
        {
            // Update existing highlight
            highlight.GetComponent<Renderer>().material.color = color;
        }
        else
        {
            // Find world position
            Vector3 worldPos = FindCellWorldPosition(cell);

            // Create new highlight
            GameObject newHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            newHighlight.name = "BoundaryHighlight";
            newHighlight.transform.position = worldPos;
            newHighlight.transform.localScale = Vector3.one * 0.3f;
            newHighlight.transform.parent = transform;

            // Set material and color
            Renderer renderer = newHighlight.GetComponent<Renderer>();
            renderer.material = boundaryHighlightMaterial != null ?
                boundaryHighlightMaterial : new Material(Shader.Find("Standard"));
            renderer.material.color = color;

            // Store reference
            highlightObjects[cell] = newHighlight;
        }
    }

    /// <summary>
    /// Highlight a specific conflict
    /// </summary>
    private void HighlightConflict(Cell cell1, Cell cell2)
    {
        CreateHighlight(cell1, conflictColor);
        CreateHighlight(cell2, conflictColor);
    }

    /// <summary>
    /// Helper to find world position of a cell
    /// </summary>
    private Vector3 FindCellWorldPosition(Cell cell)
    {
        foreach (var chunkEntry in testController.GetChunks())
        {
            Chunk chunk = chunkEntry.Value;
            int size = chunk.Size;

            // Check if cell belongs to this chunk
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int z = 0; z < size; z++)
                    {
                        if (chunk.GetCell(x, y, z) == cell)
                        {
                            // Calculate world position
                            return new Vector3(
                                chunkEntry.Key.x * size + x,
                                chunkEntry.Key.y * size + y,
                                chunkEntry.Key.z * size + z
                            );
                        }
                    }
                }
            }
        }

        // Default fallback if cell not found
        return Vector3.zero;
    }

    /// <summary>
    /// Clean up highlight objects
    /// </summary>
    private void ClearPreviousHighlights()
    {
        foreach (var highlight in highlightObjects.Values)
        {
            if (highlight != null)
            {
                DestroyImmediate(highlight);
            }
        }

        highlightObjects.Clear();
    }

    private void OnDestroy()
    {
        ClearPreviousHighlights();

        // Save any unsaved metrics
        if (historicalMetrics.Count > 0 && !string.IsNullOrEmpty(outputFilePath))
        {
            OutputMetricsToCSV(historicalMetrics, outputFilePath);
        }
    }
}