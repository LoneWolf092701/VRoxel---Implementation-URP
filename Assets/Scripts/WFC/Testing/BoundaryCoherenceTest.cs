using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;
using WFC.Core;
using WFC.Testing;
using WFC.Boundary;
using System.Linq;
using System.Collections;

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

    [Header("NEW: Test Configuration")]
    [SerializeField] private bool runTestOnStart = false;
    [SerializeField] private bool runContinuousMonitoring = false;
    [SerializeField] private int continuousTestInterval = 30; // Frames between tests
    [SerializeField] private bool autoResolveConflicts = false;
    [SerializeField] private float conflictResolutionThreshold = 0.3f; // Coherence score below this triggers resolution

    [Header("Visualization")]
    [SerializeField] private bool highlightBoundaryCells = true;
    [SerializeField] private Material boundaryHighlightMaterial;
    [SerializeField] private Color conflictColor = Color.red;
    [SerializeField] private Color compatibleColor = Color.green;
    [SerializeField] private Color lowEntropyColor = Color.yellow;
    [SerializeField] private Color highEntropyColor = Color.blue;

    [Header("NEW: Visual Debug")]
    [SerializeField] private bool showBoundaryLabels = true;
    [SerializeField] private bool showCoherenceScores = true;
    [SerializeField] private bool showConflictCount = true;
    [SerializeField] private bool colorizeByCoherence = true;
    [SerializeField] private Gradient coherenceGradient;
    [SerializeField] private float visualScale = 1.0f;
    [SerializeField] private GameObject boundaryMarkerPrefab;

    // Statistics
    private int boundaryConflictCount = 0;
    private int successfulBoundaryTransitions = 0;
    private float coherenceScore = 0f;
    private int frameCounter = 0;

    // Cached data
    private Dictionary<Cell, GameObject> highlightObjects = new Dictionary<Cell, GameObject>();
    private List<BoundaryMetrics> historicalMetrics = new List<BoundaryMetrics>();

    // NEW: Reference to boundary manager for conflict resolution
    private BoundaryBufferManager boundaryManager;

    // NEW: Visual debug elements
    private Dictionary<Direction, Dictionary<Vector3Int, GameObject>> boundaryMarkers =
        new Dictionary<Direction, Dictionary<Vector3Int, GameObject>>();

    // NEW: Test status tracking
    private float overallCoherenceScore = 0f;
    private int totalConflicts = 0;
    private int totalBoundaries = 0;
    private float lowestCoherenceScore = 1.0f;
    private Direction worstBoundaryDirection;
    private Vector3Int worstBoundaryChunk;

    // NEW: Runtime test parameters
    [System.Serializable]
    public struct TestParameters
    {
        public bool RunAutomatically;
        public float CoherenceThreshold;
        public bool UseRandomSeed;
        public int Seed;
        public float ConflictTolerance;
        public int MaxRuns;
        public bool GenerateReport;
    }

    [Header("NEW: Runtime Test Parameters")]
    [SerializeField] private TestParameters testParams;

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

    private void Start()
    {
        // Set up boundary manager reference by getting it from WFCTestController
        if (testController != null)
        {
            // Use reflection to get private boundaryManager field
            var field = testController.GetType().GetField("boundaryManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                boundaryManager = field.GetValue(testController) as BoundaryBufferManager;
            }

            if (boundaryManager == null)
            {
                Debug.LogWarning("Could not get boundary manager from test controller. Auto-conflict resolution disabled.");
                autoResolveConflicts = false;
            }
        }

        // Initialize coherence gradient if not set
        if (coherenceGradient == null || coherenceGradient.colorKeys.Length == 0)
        {
            coherenceGradient = new Gradient();
            var colorKeys = new GradientColorKey[3];
            colorKeys[0] = new GradientColorKey(Color.red, 0.0f);
            colorKeys[1] = new GradientColorKey(Color.yellow, 0.5f);
            colorKeys[2] = new GradientColorKey(Color.green, 1.0f);

            var alphaKeys = new GradientAlphaKey[2];
            alphaKeys[0] = new GradientAlphaKey(1.0f, 0.0f);
            alphaKeys[1] = new GradientAlphaKey(1.0f, 1.0f);

            coherenceGradient.SetKeys(colorKeys, alphaKeys);
        }

        // Create boundary marker material if needed
        if (boundaryHighlightMaterial == null)
        {
            boundaryHighlightMaterial = new Material(Shader.Find("Standard"));
            boundaryHighlightMaterial.SetFloat("_Glossiness", 0.0f); // Make matte
        }

        // Run test on start if requested
        if (runTestOnStart)
        {
            StartCoroutine(DelayedTest());
        }

        // Start continuous monitoring if requested
        if (runContinuousMonitoring)
        {
            StartCoroutine(ContinuousMonitoring());
        }

        // Start automated testing if requested
        if (testParams.RunAutomatically)
        {
            StartCoroutine(AutomatedTestSuite());
        }
    }

    private IEnumerator DelayedTest()
    {
        // Wait a frame to ensure WFC system is initialized
        yield return null;
        RunBoundaryTest();
    }

    private IEnumerator ContinuousMonitoring()
    {
        while (true)
        {
            yield return new WaitForFrames(continuousTestInterval);

            // Run comprehensive test
            RunComprehensiveTest();

            // Auto-resolve conflicts if enabled
            if (autoResolveConflicts && overallCoherenceScore < conflictResolutionThreshold)
            {
                ResolveAllConflicts();
            }
        }
    }

    private IEnumerator AutomatedTestSuite()
    {
        Debug.Log("Starting automated boundary coherence test suite...");

        int runCount = 0;
        List<float> coherenceScores = new List<float>();
        List<int> conflictCounts = new List<int>();

        while (runCount < testParams.MaxRuns)
        {
            runCount++;
            Debug.Log($"Test run {runCount}/{testParams.MaxRuns}");

            // Reset WFC with new seed if requested
            if (testParams.UseRandomSeed)
            {
                int seed = testParams.Seed > 0 ? testParams.Seed + runCount : Random.Range(1, 10000);
                testController.ResetWithNewSeed(seed);
                Debug.Log($"Reset with seed: {seed}");
            }
            else
            {
                testController.ResetGeneration();
            }

            // Wait for generation to complete some steps
            yield return new WaitForSeconds(1.0f);

            // Run test and record results
            RunComprehensiveTest();
            coherenceScores.Add(overallCoherenceScore);
            conflictCounts.Add(totalConflicts);

            // Wait between runs
            yield return new WaitForSeconds(0.5f);
        }

        // Generate final report
        if (testParams.GenerateReport)
        {
            GenerateTestReport(coherenceScores, conflictCounts);
        }

        Debug.Log("Automated test suite complete.");
    }

    private void GenerateTestReport(List<float> coherenceScores, List<int> conflictCounts)
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("# Boundary Coherence Test Report");
        report.AppendLine($"Date: {System.DateTime.Now}");
        report.AppendLine($"Test Runs: {coherenceScores.Count}");
        report.AppendLine();

        // Calculate statistics
        float avgCoherence = coherenceScores.Average();
        float minCoherence = coherenceScores.Min();
        float maxCoherence = coherenceScores.Max();
        float stdDevCoherence = CalculateStdDev(coherenceScores);

        int avgConflicts = (int)conflictCounts.Average();
        int minConflicts = conflictCounts.Min();
        int maxConflicts = conflictCounts.Max();

        report.AppendLine("## Coherence Scores");
        report.AppendLine($"Average: {avgCoherence:F4}");
        report.AppendLine($"Min: {minCoherence:F4}");
        report.AppendLine($"Max: {maxCoherence:F4}");
        report.AppendLine($"StdDev: {stdDevCoherence:F4}");
        report.AppendLine();

        report.AppendLine("## Conflict Counts");
        report.AppendLine($"Average: {avgConflicts}");
        report.AppendLine($"Min: {minConflicts}");
        report.AppendLine($"Max: {maxConflicts}");
        report.AppendLine();

        report.AppendLine("## Test Parameters");
        report.AppendLine($"Coherence Threshold: {testParams.CoherenceThreshold}");
        report.AppendLine($"Seed: {(testParams.UseRandomSeed ? "Random" : testParams.Seed.ToString())}");
        report.AppendLine($"Conflict Tolerance: {testParams.ConflictTolerance}");
        report.AppendLine();

        report.AppendLine("## Raw Data");
        report.AppendLine("Run,Coherence,Conflicts");
        for (int i = 0; i < coherenceScores.Count; i++)
        {
            report.AppendLine($"{i + 1},{coherenceScores[i]:F4},{conflictCounts[i]}");
        }

        // Save report
        string reportPath = "boundary_test_report.txt";
        try
        {
            File.WriteAllText(reportPath, report.ToString());
            Debug.Log($"Test report saved to {reportPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error saving test report: {e.Message}");
        }
    }

    private float CalculateStdDev(List<float> values)
    {
        float avg = values.Average();
        float sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
        return Mathf.Sqrt(sumOfSquares / values.Count);
    }

    private void Update()
    {
        // Automatic testing if enabled
        if (automaticallyTestAllBoundaries && frameCounter++ % sampleFrequency == 0)
        {
            RunComprehensiveTest();
        }

        // Debug key controls
        if (Input.GetKeyDown(KeyCode.F5))
        {
            RunBoundaryTest();
        }

        if (Input.GetKeyDown(KeyCode.F6))
        {
            RunComprehensiveTest();
        }

        if (Input.GetKeyDown(KeyCode.F7))
        {
            ResolveAllConflicts();
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            ToggleAllBoundaryVisualizations();
        }
    }

    private void ResolveAllConflicts()
    {
        if (boundaryManager == null || testController == null)
            return;

        Debug.Log("Attempting to resolve all boundary conflicts...");

        var chunks = testController.GetChunks();
        int resolvedCount = 0;

        foreach (var chunkEntry in chunks)
        {
            Chunk chunk = chunkEntry.Value;

            foreach (var bufferEntry in chunk.BoundaryBuffers)
            {
                Direction dir = bufferEntry.Key;
                BoundaryBuffer buffer = bufferEntry.Value;

                // Skip if no adjacent chunk
                if (buffer.AdjacentChunk == null)
                    continue;

                // Check for conflicts in each cell pair
                for (int i = 0; i < buffer.BoundaryCells.Count; i++)
                {
                    Cell boundaryCell = buffer.BoundaryCells[i];
                    Direction oppositeDir = dir.GetOpposite();

                    if (!buffer.AdjacentChunk.BoundaryBuffers.TryGetValue(oppositeDir, out BoundaryBuffer adjacentBuffer))
                        continue;

                    if (i >= adjacentBuffer.BoundaryCells.Count)
                        continue;

                    Cell adjacentCell = adjacentBuffer.BoundaryCells[i];

                    // Check for conflict
                    if (boundaryCell.CollapsedState.HasValue && adjacentCell.CollapsedState.HasValue)
                    {
                        if (!testController.AreStatesCompatible(
                            boundaryCell.CollapsedState.Value,
                            adjacentCell.CollapsedState.Value,
                            dir))
                        {
                            // Resolve conflict
                            boundaryManager.ResolveBoundaryConflict(buffer, i);
                            resolvedCount++;
                        }
                    }
                }
            }
        }

        Debug.Log($"Resolved {resolvedCount} boundary conflicts.");

        // Update visualization
        if (highlightBoundaryCells)
        {
            ClearPreviousHighlights();
            UpdateAllBoundaryVisualizations();
        }
    }

    private void ToggleAllBoundaryVisualizations()
    {
        highlightBoundaryCells = !highlightBoundaryCells;

        if (highlightBoundaryCells)
        {
            UpdateAllBoundaryVisualizations();
        }
        else
        {
            ClearPreviousHighlights();
            ClearAllBoundaryMarkers();
        }
    }

    private void UpdateAllBoundaryVisualizations()
    {
        ClearPreviousHighlights();
        ClearAllBoundaryMarkers();

        // Run boundary test to update all visualizations
        RunComprehensiveTest();
    }

    private void ClearAllBoundaryMarkers()
    {
        foreach (var dirEntry in boundaryMarkers)
        {
            foreach (var marker in dirEntry.Value.Values)
            {
                if (marker != null)
                {
                    DestroyImmediate(marker);
                }
            }
        }

        boundaryMarkers.Clear();
    }

    /// <summary>
    /// Runs a comprehensive test on all chunk boundaries in the world
    /// </summary>
    public void RunComprehensiveTest()
    {
        List<BoundaryMetrics> allMetrics = new List<BoundaryMetrics>();

        // Test all boundaries in the world
        var chunks = testController.GetChunks();

        // Reset statistics
        totalConflicts = 0;
        totalBoundaries = 0;
        lowestCoherenceScore = 1.0f;
        overallCoherenceScore = 0f;

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

                // Update overall statistics
                totalBoundaries++;
                totalConflicts += metrics.ConflictCount;
                overallCoherenceScore += metrics.OverallCoherence;

                // Track worst boundary
                if (metrics.OverallCoherence < lowestCoherenceScore)
                {
                    lowestCoherenceScore = metrics.OverallCoherence;
                    worstBoundaryDirection = dir;
                    worstBoundaryChunk = chunkPos;
                }

                // Create boundary visualization if enabled
                if (highlightBoundaryCells)
                {
                    CreateBoundaryMarker(chunkPos, dir, metrics);
                    HighlightBoundaryCells(boundaryCells1, boundaryCells2, dir);
                }
            }
        }

        // Calculate average coherence
        if (totalBoundaries > 0)
        {
            overallCoherenceScore /= totalBoundaries;
        }

        // Calculate overall statistics
        float avgCoherence = allMetrics.Average(m => m.OverallCoherence);
        float avgTransitionSmoothness = allMetrics.Average(m => m.TransitionSmoothness);
        float avgEntropy = allMetrics.Average(m => m.StateDistributionEntropy);
        float avgPatternContinuity = allMetrics.Average(m => m.PatternContinuity);

        Debug.Log($"Comprehensive Boundary Analysis:");
        Debug.Log($"Overall boundary coherence: {avgCoherence:F3} ({totalBoundaries} boundaries)");
        Debug.Log($"Average transition smoothness: {avgTransitionSmoothness:F3}");
        Debug.Log($"Average state distribution entropy: {avgEntropy:F3}");
        Debug.Log($"Average pattern continuity: {avgPatternContinuity:F3}");
        Debug.Log($"Total conflicts across all boundaries: {totalConflicts}");
        Debug.Log($"Worst boundary: {worstBoundaryChunk} ({worstBoundaryDirection}) - coherence: {lowestCoherenceScore:F3}");

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
            HighlightBoundaryCells(boundaryCells1, boundaryCells2, boundaryDirection);
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
    private void HighlightBoundaryCells(List<Cell> cells1, List<Cell> cells2, Direction direction)
    {
        if (cells1.Count != cells2.Count)
            return;

        for (int i = 0; i < cells1.Count; i++)
        {
            Cell cell1 = cells1[i];
            Cell cell2 = cells2[i];

            bool compatible = AreStatesCompatible(cell1, cell2, direction);
            bool bothCollapsed = cell1.CollapsedState.HasValue && cell2.CollapsedState.HasValue;
            bool potentiallyCompatible = CheckPotentialCompatibility(cell1, cell2, direction);

            Color color;
            if (bothCollapsed)
            {
                color = compatible ? compatibleColor : conflictColor;
            }
            else
            {
                // Use entropy-based colors for uncollapsed cells
                float entropyFactor = (float)(cell1.Entropy + cell2.Entropy) / (2 * testController.MaxStates);
                color = potentiallyCompatible ?
                    Color.Lerp(lowEntropyColor, highEntropyColor, entropyFactor) :
                    conflictColor;
            }

            // Create or update highlight for cell1
            CreateHighlight(cell1, color);

            // Create or update highlight for cell2
            CreateHighlight(cell2, color);
        }
    }

    /// <summary>
    /// Create a boundary marker to show the overall coherence of a boundary
    /// </summary>
    private void CreateBoundaryMarker(Vector3Int chunkPos, Direction direction, BoundaryMetrics metrics)
    {
        // Skip if not showing boundary markers
        if (!showBoundaryLabels && !showCoherenceScores && !showConflictCount)
            return;

        // Calculate world position for marker
        Vector3 chunkWorldPos = new Vector3(
            chunkPos.x * testController.ChunkSize,
            chunkPos.y * testController.ChunkSize,
            chunkPos.z * testController.ChunkSize
        );

        // Calculate marker position based on direction
        Vector3 dirVector = direction.ToVector3Int();
        Vector3 markerPos = chunkWorldPos + new Vector3(
            (direction == Direction.Right ? testController.ChunkSize : (direction == Direction.Left ? 0 : testController.ChunkSize / 2f)),
            (direction == Direction.Up ? testController.ChunkSize : (direction == Direction.Down ? 0 : testController.ChunkSize / 2f)),
            (direction == Direction.Forward ? testController.ChunkSize : (direction == Direction.Back ? 0 : testController.ChunkSize / 2f))
        );

        // Get or create dictionary for this direction
        if (!boundaryMarkers.TryGetValue(direction, out var dirMarkers))
        {
            dirMarkers = new Dictionary<Vector3Int, GameObject>();
            boundaryMarkers[direction] = dirMarkers;
        }

        // Check if marker already exists
        GameObject marker;
        if (!dirMarkers.TryGetValue(chunkPos, out marker) || marker == null)
        {
            // Create new marker
            if (boundaryMarkerPrefab != null)
            {
                marker = Instantiate(boundaryMarkerPrefab, markerPos, Quaternion.identity);
            }
            else
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.transform.position = markerPos;
                marker.transform.localScale = Vector3.one * visualScale * 0.5f;
            }

            marker.name = $"BoundaryMarker_{direction}_{chunkPos.x}_{chunkPos.y}_{chunkPos.z}";
            marker.transform.parent = transform;

            dirMarkers[chunkPos] = marker;
        }
        else
        {
            // Update position
            marker.transform.position = markerPos;
        }

        // Set color based on coherence
        Renderer renderer = marker.GetComponent<Renderer>();
        if (renderer != null && colorizeByCoherence)
        {
            if (coherenceGradient != null)
            {
                renderer.material.color = coherenceGradient.Evaluate(metrics.OverallCoherence);
            }
            else
            {
                // Fallback color scheme
                renderer.material.color = Color.Lerp(Color.red, Color.green, metrics.OverallCoherence);
            }
        }

        // Add TextMesh component for labels if needed
        TextMesh textMesh = marker.GetComponent<TextMesh>();
        if (textMesh == null && (showBoundaryLabels || showCoherenceScores || showConflictCount))
        {
            GameObject textObj = new GameObject("Label");
            textObj.transform.parent = marker.transform;
            textObj.transform.localPosition = Vector3.up * 0.5f;
            textObj.transform.localRotation = Quaternion.identity;

            textMesh = textObj.AddComponent<TextMesh>();
            textMesh.fontSize = 36;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.LowerCenter;
            textMesh.characterSize = 0.1f * visualScale;
        }

        // Update text content
        if (textMesh != null)
        {
            StringBuilder sb = new StringBuilder();

            if (showBoundaryLabels)
            {
                sb.AppendLine($"{direction}");
            }

            if (showCoherenceScores)
            {
                sb.AppendLine($"Coherence: {metrics.OverallCoherence:F2}");
            }

            if (showConflictCount && metrics.ConflictCount > 0)
            {
                sb.AppendLine($"Conflicts: {metrics.ConflictCount}");
            }

            textMesh.text = sb.ToString();
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
            newHighlight.transform.localScale = Vector3.one * 0.3f * visualScale;
            newHighlight.transform.parent = transform;

            // Set material and color
            Renderer renderer = newHighlight.GetComponent<Renderer>();
            renderer.material = boundaryHighlightMaterial != null ?
                new Material(boundaryHighlightMaterial) : // Create a clone to avoid sharing the material
                new Material(Shader.Find("Standard"));
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
        ClearAllBoundaryMarkers();

        // Save any unsaved metrics
        if (historicalMetrics.Count > 0 && !string.IsNullOrEmpty(outputFilePath))
        {
            OutputMetricsToCSV(historicalMetrics, outputFilePath);
        }
    }

    /// <summary>
    /// Waits for the specified number of frames
    /// </summary>
    private class WaitForFrames : CustomYieldInstruction
    {
        private int framesRemaining;

        public WaitForFrames(int frames)
        {
            framesRemaining = frames;
        }

        public override bool keepWaiting
        {
            get
            {
                framesRemaining--;
                return framesRemaining > 0;
            }
        }
    }

    /// <summary>
    /// Export metrics to JSON format
    /// </summary>
    public string ExportMetricsToJson()
    {
        StringBuilder jsonBuilder = new StringBuilder();
        jsonBuilder.AppendLine("{");
        jsonBuilder.AppendLine("  \"timestamp\": \"" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\",");
        jsonBuilder.AppendLine("  \"worldSeed\": " + testController.GetType().GetField("randomSeed")?.GetValue(testController) + ",");
        jsonBuilder.AppendLine("  \"overallCoherence\": " + overallCoherenceScore.ToString("F4") + ",");
        jsonBuilder.AppendLine("  \"totalConflicts\": " + totalConflicts + ",");
        jsonBuilder.AppendLine("  \"totalBoundaries\": " + totalBoundaries + ",");
        jsonBuilder.AppendLine("  \"metrics\": [");

        for (int i = 0; i < historicalMetrics.Count; i++)
        {
            var metric = historicalMetrics[i];
            jsonBuilder.AppendLine("    {");
            jsonBuilder.AppendLine("      \"timestamp\": \"" + metric.Timestamp + "\",");
            jsonBuilder.AppendLine("      \"chunkPosition\": [" + metric.ChunkPosition.x + ", " + metric.ChunkPosition.y + ", " + metric.ChunkPosition.z + "],");
            jsonBuilder.AppendLine("      \"neighborPosition\": [" + metric.NeighborPosition.x + ", " + metric.NeighborPosition.y + ", " + metric.NeighborPosition.z + "],");
            jsonBuilder.AppendLine("      \"direction\": \"" + metric.Direction + "\",");
            jsonBuilder.AppendLine("      \"coherence\": " + metric.OverallCoherence.ToString("F4") + ",");
            jsonBuilder.AppendLine("      \"conflicts\": " + metric.ConflictCount + ",");
            jsonBuilder.AppendLine("      \"totalCells\": " + metric.TotalCells + ",");
            jsonBuilder.AppendLine("      \"collapsedPercentage\": " + metric.CollapsedPercentage.ToString("F4") + ",");
            jsonBuilder.AppendLine("      \"stateEntropy\": " + metric.StateDistributionEntropy.ToString("F4") + ",");
            jsonBuilder.AppendLine("      \"patternContinuity\": " + metric.PatternContinuity.ToString("F4") + ",");
            jsonBuilder.AppendLine("      \"transitionSmoothness\": " + metric.TransitionSmoothness.ToString("F4"));

            if (i < historicalMetrics.Count - 1)
                jsonBuilder.AppendLine("    },");
            else
                jsonBuilder.AppendLine("    }");
        }

        jsonBuilder.AppendLine("  ]");
        jsonBuilder.AppendLine("}");

        return jsonBuilder.ToString();
    }
}