using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using WFC.Boundary;
using WFC.Chunking;
using WFC.Core;
using WFC.Generation;
using WFC.MarchingCubes;
using WFC.Performance;
using WFC.Processing;

namespace WFC.Metrics
{
    /// <summary>
    /// Base interface for all WFC test cases
    /// </summary>
    public interface IWFCTestCase
    {
        /// <summary>
        /// Name of the test case
        /// </summary>
        string TestName { get; }

        /// <summary>
        /// Description of what the test measures
        /// </summary>
        string TestDescription { get; }

        /// <summary>
        /// Initialize the test with references
        /// </summary>
        void Initialize(ReferenceLocator references);

        /// <summary>
        /// Check if this test can run with the available references
        /// </summary>
        bool CanRun();

        /// <summary>
        /// Run the test asynchronously
        /// </summary>
        IEnumerator RunTest();

        /// <summary>
        /// Get results of the test
        /// </summary>
        ITestResult GetResults();

        /// <summary>
        /// Export test results to a file
        /// </summary>
        void ExportResults(string filePath);

        /// <summary>
        /// Clean up resources after testing
        /// </summary>
        void Cleanup();
    }

    /// <summary>
    /// Base interface for test results
    /// </summary>
    public interface ITestResult
    {
        /// <summary>
        /// Test name for these results
        /// </summary>
        string TestName { get; }

        /// <summary>
        /// Get CSV header line for these results
        /// </summary>
        string GetCsvHeader();

        /// <summary>
        /// Get CSV data lines for these results
        /// </summary>
        IEnumerable<string> GetCsvData();

        /// <summary>
        /// Check if results are available
        /// </summary>
        bool HasResults();
    }

    /// <summary>
    /// Manages component references for tests
    /// </summary>
    public class ReferenceLocator
    {
        public WFCGenerator WfcGenerator { get; private set; }
        public ChunkManager ChunkManager { get; private set; }
        public MeshGenerator MeshGenerator { get; private set; }
        public ParallelWFCManager ParallelManager { get; private set; }
        public PerformanceMonitor PerformanceMonitor { get; private set; }

        // Reflection-accessed components
        public BoundaryBufferManager BoundaryManager { get; private set; }
        public HierarchicalConstraintSystem ConstraintSystem { get; private set; }
        public DensityFieldGenerator DensityGenerator { get; private set; }
        public ParallelWFCProcessor ParallelProcessor { get; private set; }

        public void SetReferences(
        WFCGenerator wfcGenerator,
        ChunkManager chunkManager,
        MeshGenerator meshGenerator,
        ParallelWFCManager parallelManager)
        {
            // Set the references directly
            WfcGenerator = wfcGenerator;
            ChunkManager = chunkManager;
            MeshGenerator = meshGenerator;
            ParallelManager = parallelManager;

            // Now find the nested references
            if (WfcGenerator != null)
            {
                // Get boundary manager via reflection
                var boundaryField = WfcGenerator.GetType().GetField("boundaryManager",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (boundaryField != null)
                    BoundaryManager = boundaryField.GetValue(WfcGenerator) as BoundaryBufferManager;

                // Get constraint system
                try
                {
                    ConstraintSystem = WfcGenerator.GetHierarchicalConstraintSystem();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to get constraint system: {e.Message}");
                }
            }

            // Get MeshGenerator internal references
            if (MeshGenerator != null)
            {
                var densityField = MeshGenerator.GetType().GetField("densityGenerator",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (densityField != null)
                    DensityGenerator = densityField.GetValue(MeshGenerator) as DensityFieldGenerator;
            }

            // Get ParallelManager internal references
            if (ParallelManager != null)
            {
                try
                {
                    ParallelProcessor = ParallelManager.GetParallelProcessor();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to get parallel processor: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Find all required components in the scene
        /// </summary>
        public void FindReferences(WFCGenerator customWfcGenerator = null, ChunkManager customChunkManager = null,
                                  MeshGenerator customMeshGenerator = null, ParallelWFCManager customParallelManager = null)
        {
            // Use provided references or find them
            WfcGenerator = customWfcGenerator ?? GameObject.FindObjectOfType<WFCGenerator>();
            ChunkManager = customChunkManager ?? GameObject.FindObjectOfType<ChunkManager>();
            MeshGenerator = customMeshGenerator ?? GameObject.FindObjectOfType<MeshGenerator>();
            ParallelManager = customParallelManager ?? GameObject.FindObjectOfType<ParallelWFCManager>();
            PerformanceMonitor = GameObject.FindObjectOfType<PerformanceMonitor>();

            // Get WFCGenerator internal references
            if (WfcGenerator != null)
            {
                // Get boundary manager via reflection
                var boundaryField = WfcGenerator.GetType().GetField("boundaryManager",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (boundaryField != null)
                    BoundaryManager = boundaryField.GetValue(WfcGenerator) as BoundaryBufferManager;

                // Get constraint system
                ConstraintSystem = WfcGenerator.GetHierarchicalConstraintSystem();
            }

            // Get MeshGenerator internal references
            if (MeshGenerator != null)
            {
                var densityField = MeshGenerator.GetType().GetField("densityGenerator",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (densityField != null)
                    DensityGenerator = densityField.GetValue(MeshGenerator) as DensityFieldGenerator;
            }

            // Get ParallelManager internal references
            if (ParallelManager != null)
            {
                ParallelProcessor = ParallelManager.GetParallelProcessor();
            }
        }

        /// <summary>
        /// Get a debug report of available references
        /// </summary>
        public string GetReferenceReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("===== WFC Reference Status =====");
            sb.AppendLine($"WFCGenerator: {(WfcGenerator != null ? "Found" : "Missing")}");
            sb.AppendLine($"ChunkManager: {(ChunkManager != null ? "Found" : "Missing")}");
            sb.AppendLine($"MeshGenerator: {(MeshGenerator != null ? "Found" : "Missing")}");
            sb.AppendLine($"ParallelManager: {(ParallelManager != null ? "Found" : "Missing")}");
            sb.AppendLine($"BoundaryManager: {(BoundaryManager != null ? "Found" : "Missing")}");
            sb.AppendLine($"ConstraintSystem: {(ConstraintSystem != null ? "Found" : "Missing")}");
            sb.AppendLine($"DensityGenerator: {(DensityGenerator != null ? "Found" : "Missing")}");
            sb.AppendLine($"ParallelProcessor: {(ParallelProcessor != null ? "Found" : "Missing")}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Base test manager for WFC performance tests
    /// </summary>
    public class WFCMetricsManager : MonoBehaviour
    {
        [Header("Test Configuration")]
        [SerializeField] public bool enableAllTests = true;
        [SerializeField] public bool[] enabledTests = new bool[6] { true, true, true, true, true, true };
        [SerializeField] public string outputDirectory = "";

        [Header("References")]
        [SerializeField] private WFCGenerator wfcGenerator;
        [SerializeField] private ChunkManager chunkManager;
        [SerializeField] private MeshGenerator meshGenerator;
        [SerializeField] private ParallelWFCManager parallelManager;

        // Test registry
        private List<IWFCTestCase> testCases = new List<IWFCTestCase>();
        private ReferenceLocator references = new ReferenceLocator();

        // Test state
        private bool isRunning = false;
        private int currentTestIndex = -1;
        private float testProgress = 0f;
        private string statusMessage = "Ready to run tests";

        // Test result storage
        private Dictionary<string, ITestResult> testResults = new Dictionary<string, ITestResult>();

        // Properties for UI
        public bool IsRunning => isRunning;
        public float Progress => testProgress;
        public string StatusMessage => statusMessage;
        public IReadOnlyList<IWFCTestCase> TestCases => testCases.AsReadOnly();
        public IReadOnlyDictionary<string, ITestResult> TestResults => testResults;

        private void Awake()
        {
            // Register test cases
            RegisterTestCases();

            // Make sure the enabledTests array is correct length
            if (enabledTests.Length != testCases.Count)
            {
                Array.Resize(ref enabledTests, testCases.Count);
                // Initialize with current enableAllTests value
                for (int i = 0; i < enabledTests.Length; i++)
                {
                    enabledTests[i] = enableAllTests;
                }
            }

            // Create default output directory if needed
            if (string.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = Path.Combine(Application.persistentDataPath, "WFCMetrics");
            }

            // Ensure directory exists
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
        }

        private void Start()
        {
            // First, explicitly copy references from inspector to reference locator
            references = new ReferenceLocator();

            // IMPORTANT: This is where we directly set the references
            references.SetReferences(
                wfcGenerator,
                chunkManager,
                meshGenerator,
                parallelManager
            );

            Debug.Log("References status:");
            Debug.Log($"WfcGenerator: {(references.WfcGenerator != null ? "Found" : "Missing")}");
            Debug.Log($"ChunkManager: {(references.ChunkManager != null ? "Found" : "Missing")}");
            Debug.Log($"MeshGenerator: {(references.MeshGenerator != null ? "Found" : "Missing")}");
            Debug.Log($"ParallelManager: {(references.ParallelManager != null ? "Found" : "Missing")}");

            // Initialize the tests with these references
            InitializeTests();

            // Debug test status
            foreach (var test in testCases)
            {
                Debug.Log($"Test '{test.TestName}' can run: {test.CanRun()}");
                if (!test.CanRun())
                {
                    statusMessage = $"Test '{test.TestName}' cannot run. Missing required references.";
                }
            }
        }


        private void UpdateStatusMessage()
        {
            int enabledCount = 0;
            for (int i = 0; i < enabledTests.Length; i++)
            {
                if (enabledTests[i]) enabledCount++;
            }

            if (enabledCount == 0)
            {
                statusMessage = "No tests enabled. Please enable at least one test.";
            }
            else
            {
                statusMessage = $"Ready to run {enabledCount} test(s).";
            }
        }

        public void SetEnableAllTests(bool enable)
        {
            enableAllTests = enable;

            // Set all individual tests to match
            for (int i = 0; i < enabledTests.Length; i++)
            {
                enabledTests[i] = enable;
            }

            UpdateStatusMessage();
        }

        // Method to toggle a specific test
        public void ToggleTest(int index, bool enabled)
        {
            if (index >= 0 && index < enabledTests.Length)
            {
                enabledTests[index] = enabled;

                // Check if all tests match the enableAllTests setting
                bool allEnabled = true;
                for (int i = 0; i < enabledTests.Length; i++)
                {
                    if (!enabledTests[i])
                    {
                        allEnabled = false;
                        break;
                    }
                }

                enableAllTests = allEnabled;
                UpdateStatusMessage();
            }
        }

        /// <summary>
        /// Register all test cases
        /// </summary>
        public void RegisterTestCases()
        {
            // Clear existing test cases first
            testCases.Clear();

            // Add test cases with explicit debug logs
            testCases.Add(new ChunkGenerationTest());
            Debug.Log($"Registered test: {testCases[testCases.Count - 1].TestName}");

            testCases.Add(new BoundaryCoherenceTest());
            Debug.Log($"Registered test: {testCases[testCases.Count - 1].TestName}");

            testCases.Add(new MeshGenerationTest());
            Debug.Log($"Registered test: {testCases[testCases.Count - 1].TestName}");

            testCases.Add(new ParallelProcessingTest());
            Debug.Log($"Registered test: {testCases[testCases.Count - 1].TestName}");

            testCases.Add(new WorldSizeScalingTest());
            Debug.Log($"Registered test: {testCases[testCases.Count - 1].TestName}");

            testCases.Add(new LODPerformanceTest());
            Debug.Log($"Registered test: {testCases[testCases.Count - 1].TestName}");

            Debug.Log($"Total registered tests: {testCases.Count}");
        }

        /// <summary>
        /// Initialize references for test cases
        /// </summary>
        private void InitializeReferences()
        {
            // Pass the inspector-provided references to FindReferences
            references.FindReferences(wfcGenerator, chunkManager, meshGenerator, parallelManager);

            // Log reference status
            Debug.Log(references.GetReferenceReport());
        }

        /// <summary>
        /// Initialize all test cases
        /// </summary>
        private void InitializeTests()
        {
            foreach (var test in testCases)
            {
                test.Initialize(references);
            }
        }

        /// <summary>
        /// Run all enabled tests
        /// </summary>
        public void RunAllTests()
        {
            if (isRunning) return;

            // Start test coroutine
            StartCoroutine(RunTestsCoroutine());
        }

        /// <summary>
        /// Run a specific test by index
        /// </summary>
        public void RunTest(int index)
        {
            if (isRunning || index < 0 || index >= testCases.Count) return;

            // Start specific test
            StartCoroutine(RunSingleTestCoroutine(index));
        }

        /// <summary>
        /// Coroutine to run all enabled tests
        /// </summary>
        private IEnumerator RunTestsCoroutine()
        {
            isRunning = true;
            testProgress = 0f;
            statusMessage = "Starting test suite...";

            Debug.Log($"Starting test suite with {testCases.Count} registered tests");

            // Count enabled tests
            int enabledTestCount = 0;
            for (int i = 0; i < enabledTests.Length && i < testCases.Count; i++)
            {
                if (enabledTests[i])
                {
                    enabledTestCount++;
                    Debug.Log($"Test enabled: {testCases[i].TestName}");
                }
            }

            if (enabledTestCount == 0)
            {
                statusMessage = "No tests enabled. Please enable at least one test.";
                isRunning = false;
                yield break;
            }

            int testsCompleted = 0;

            // Run each enabled test
            for (int i = 0; i < testCases.Count && i < enabledTests.Length; i++)
            {
                if (!enabledTests[i]) continue;

                currentTestIndex = i;
                IWFCTestCase test = testCases[i];

                // Verify test can run
                if (!test.CanRun())
                {
                    Debug.LogWarning($"Test '{test.TestName}' cannot run. Missing required references.");
                    continue;
                }

                // Run test
                statusMessage = $"Running test: {test.TestName}";
                yield return StartCoroutine(test.RunTest());

                // Store results
                ITestResult result = test.GetResults();
                if (result.HasResults())
                {
                    testResults[test.TestName] = result;

                    // Export results
                    string filePath = Path.Combine(outputDirectory, $"{test.TestName.Replace(" ", "")}.csv");
                    test.ExportResults(filePath);
                }

                // Clean up
                test.Cleanup();

                // Update progress
                testsCompleted++;
                testProgress = (float)testsCompleted / enabledTestCount;
                yield return null;
            }

            // Complete
            currentTestIndex = -1;
            statusMessage = $"Test suite complete. {testsCompleted} of {enabledTestCount} tests completed.";
            testProgress = 1.0f;
            isRunning = false;
        }

        /// <summary>
        /// Coroutine to run a single test
        /// </summary>
        private IEnumerator RunSingleTestCoroutine(int index)
        {
            isRunning = true;
            testProgress = 0f;
            currentTestIndex = index;

            IWFCTestCase test = testCases[index];

            // Verify test can run
            if (!test.CanRun())
            {
                statusMessage = $"Test '{test.TestName}' cannot run. Missing required references.";
                isRunning = false;
                yield break;
            }

            // Run test
            statusMessage = $"Running test: {test.TestName}";
            yield return StartCoroutine(test.RunTest());

            // Store and export results
            ITestResult result = test.GetResults();
            if (result.HasResults())
            {
                testResults[test.TestName] = result;

                // Export results
                string filePath = Path.Combine(outputDirectory, $"{test.TestName.Replace(" ", "")}.csv");
                test.ExportResults(filePath);
            }

            // Clean up
            test.Cleanup();

            // Complete
            currentTestIndex = -1;
            statusMessage = $"Test '{test.TestName}' complete.";
            testProgress = 1.0f;
            isRunning = false;
        }

        /// <summary>
        /// Cancel all running tests
        /// </summary>
        public void CancelTests()
        {
            if (!isRunning) return;

            // Stop all coroutines
            StopAllCoroutines();

            // Clean up current test
            if (currentTestIndex >= 0 && currentTestIndex < testCases.Count)
            {
                testCases[currentTestIndex].Cleanup();
            }

            // Reset state
            isRunning = false;
            currentTestIndex = -1;
            statusMessage = "Tests cancelled.";
        }

        /// <summary>
        /// Export all test results to CSV files
        /// </summary>
        public void ExportAllResults()
        {
            foreach (var result in testResults)
            {
                string filePath = Path.Combine(outputDirectory, $"{result.Key.Replace(" ", "")}.csv");

                // Find the associated test case
                IWFCTestCase test = testCases.Find(t => t.TestName == result.Key);
                if (test != null)
                {
                    test.ExportResults(filePath);
                }
                else
                {
                    // Fall back to manual export
                    ExportResultManually(result.Value, filePath);
                }
            }
        }

        /// <summary>
        /// Manually export a test result to a CSV file
        /// </summary>
        private void ExportResultManually(ITestResult result, string filePath)
        {
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                // Write header
                writer.WriteLine(result.GetCsvHeader());

                // Write data
                foreach (string line in result.GetCsvData())
                {
                    writer.WriteLine(line);
                }
            }
        }
    }

    /// <summary>
    /// Utility class for measuring time in milliseconds
    /// </summary>
    public class Stopwatch
    {
        private float startTime;
        private float stopTime;
        private bool isRunning;

        public float ElapsedMilliseconds
        {
            get
            {
                if (isRunning)
                    return (Time.realtimeSinceStartup - startTime) * 1000f;
                else
                    return (stopTime - startTime) * 1000f;
            }
        }

        public void Start()
        {
            startTime = Time.realtimeSinceStartup;
            isRunning = true;
        }

        public void Stop()
        {
            stopTime = Time.realtimeSinceStartup;
            isRunning = false;
        }

        public void Reset()
        {
            startTime = 0;
            stopTime = 0;
            isRunning = false;
        }
    }
}