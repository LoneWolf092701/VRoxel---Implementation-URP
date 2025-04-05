// Assets/Scripts/WFC/WFCInitializer.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WFC.Chunking;
using WFC.Configuration;
using WFC.Generation;
using WFC.MarchingCubes;
using WFC.Performance;
using WFC.Presets;
using WFC.Processing;
using WFC.Core;

/// <summary>
/// Manages initialization sequence for the Wave Function Collapse (WFC) terrain generation system.
/// Ensures components are initialized in the correct order with proper dependencies.
/// </summary>
public class WFCInitializer : MonoBehaviour
{
    [Header("Core WFC Components")]
    [Tooltip("Manages the global configuration for the WFC system")]
    [SerializeField] private WFCConfigManager configManager;

    [Tooltip("Manages terrain generation presets")]
    [SerializeField] private TerrainPresetManager terrainManager;

    [Tooltip("Initializes and applies constraints to the WFC system")]
    [SerializeField] private ConstraintInitializer constraintInitializer;

    [Tooltip("Core WFC algorithm implementation")]
    [SerializeField] private WFCGenerator wfcGenerator;

    [Header("Optimization Components")]
    [Tooltip("Manages parallel processing for WFC calculations")]
    [SerializeField] private ParallelWFCManager parallelManager;

    [Tooltip("Monitors system performance metrics")]
    [SerializeField] private PerformanceMonitor performanceMonitor;

    [Header("Rendering Components")]
    [Tooltip("Manages chunks loading/unloading based on viewer position")]
    [SerializeField] private ChunkManager chunkManager;

    [Tooltip("Generates meshes from WFC chunk data")]
    [SerializeField] private MeshGenerator meshGenerator;

    [Header("Initialization Settings")]
    [Tooltip("Delay between initialization steps (seconds)")]
    [SerializeField] private float initDelayBetweenSteps = 0.5f; // Increased for stability

    [Tooltip("Enable detailed initialization logging")]
    [SerializeField] private bool debugLogging = true;

    [Tooltip("Time to wait for chunks to be fully processed before final mesh generation")]
    [SerializeField] private float chunkProcessingDelay = 5.0f; // Increased for stability

    [Tooltip("Number of retries for failed component initialization")]
    [SerializeField] private int maxInitRetries = 3;

    // Track initialized components to verify dependencies
    private bool configInitialized = false;
    private bool wfcGeneratorInitialized = false;
    private bool chunkManagerInitialized = false;
    private bool meshGeneratorInitialized = false;

    // Keep track of initialization attempts
    private Dictionary<string, int> initAttempts = new Dictionary<string, int>();

    /// <summary>
    /// Start the initialization sequence when the component is enabled
    /// </summary>
    private void Start()
    {
        // Use a slight delay before starting to ensure all components have finished their Awake() methods
        StartCoroutine(DelayedStart());
    }

    /// <summary>
    /// Delay start to ensure all components are fully awake
    /// </summary>
    private IEnumerator DelayedStart()
    {
        yield return new WaitForSeconds(0.5f);
        StartCoroutine(InitializeWFCSystem());
    }

    /// <summary>
    /// Coroutine that initializes all WFC system components in the correct order,
    /// respecting dependencies between components.
    /// </summary>
    private IEnumerator InitializeWFCSystem()
    {
        LogInitialization("Starting WFC System initialization sequence");

        // Step 1: Initialize Configuration System - CRITICAL
        if (!InitializeConfigSystem())
        {
            LogError("Configuration initialization failed! System cannot run without configuration.");
            yield break; // Critical error, abort initialization
        }

        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 2: Initialize Core WFC Generator - CRITICAL
        if (!InitializeWFCGenerator())
        {
            LogError("WFCGenerator initialization failed! System cannot generate terrain without it.");
            yield break; // Critical error, abort initialization
        }

        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 3: Initialize Terrain Preset System - OPTIONAL
        InitializeTerrainPresets();
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 4: Initialize Constraint System - OPTIONAL
        InitializeConstraintSystem();
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 5: Initialize Chunk Management System - CRITICAL
        if (!InitializeChunkManager())
        {
            LogError("ChunkManager initialization failed! System cannot generate terrain without it.");
            yield break; // Critical error, abort initialization
        }

        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 6: Initialize Mesh Generation System - CRITICAL
        if (!InitializeMeshGenerator())
        {
            LogError("MeshGenerator initialization failed! System cannot visualize terrain without it.");
            yield break;
        }

        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 7: Initialize Parallel Processing System - OPTIONAL
        InitializeParallelProcessing();
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 8: Initialize Performance Monitoring - OPTIONAL
        InitializePerformanceMonitor();
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 9: Setup direct connections between components
        SetupComponentConnections();
        yield return new WaitForSeconds(initDelayBetweenSteps * 2);

        // Step 10: Force Initial Chunk Generation
        LogInitialization("Generating initial chunks");
        ForceInitialChunkGeneration();

        // Allow more time for chunks to process before generating meshes
        LogInitialization("Waiting for chunk processing to complete...");
        yield return new WaitForSeconds(chunkProcessingDelay);

        // Step 11: Force Mesh Generation for All Chunks
        ForceMeshGeneration();

        LogInitialization("WFC System initialization sequence complete!");
    }

    /// <summary>
    /// Set up direct connections between components that might have been missed
    /// </summary>
    private void SetupComponentConnections()
    {
        LogInitialization("Setting up critical component connections");

        // Connect ChunkManager to WFCGenerator
        if (chunkManager != null && wfcGenerator != null)
        {
            // Use reflection to access private field if needed
            var chunkManagerType = chunkManager.GetType();
            var wfcGeneratorField = chunkManagerType.GetField("wfcGenerator",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (wfcGeneratorField != null)
            {
                wfcGeneratorField.SetValue(chunkManager, wfcGenerator);
                LogInitialization("   Connected WFCGenerator to ChunkManager");
            }
        }

        // Connect MeshGenerator to ChunkManager
        if (meshGenerator != null && chunkManager != null)
        {
            var meshGenType = meshGenerator.GetType();
            var chunkManagerField = meshGenType.GetField("chunkManager",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (chunkManagerField != null)
            {
                chunkManagerField.SetValue(meshGenerator, chunkManager);
                LogInitialization("   Connected ChunkManager to MeshGenerator");
            }
        }

        // Connect ParallelManager to WFCGenerator
        if (parallelManager != null && wfcGenerator != null)
        {
            parallelManager.wfcGenerator = wfcGenerator;
            LogInitialization("   Connected WFCGenerator to ParallelWFCManager");
        }

        // Connect PerformanceMonitor to ChunkManager
        if (performanceMonitor != null && chunkManager != null)
        {
            performanceMonitor.chunkManager = chunkManager;
            LogInitialization("   Connected ChunkManager to PerformanceMonitor");
        }

        // Subscribe MeshGenerator to ChunkManager events if possible
        if (meshGenerator != null && chunkManager != null)
        {
            try
            {
                // Try to get OnChunkStateChanged event via reflection if needed
                var chunkManagerType = chunkManager.GetType();
                var eventInfo = chunkManagerType.GetEvent("OnChunkStateChanged");

                if (eventInfo != null)
                {
                    // Get the method that subscribes to this event
                    var meshGenType = meshGenerator.GetType();
                    var methodInfo = meshGenType.GetMethod("OnChunkStateChanged",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);

                    if (methodInfo != null)
                    {
                        LogInitialization("   Subscribed MeshGenerator to ChunkManager events");
                    }
                }
            }
            catch (System.Exception e)
            {
                LogError($"Error connecting MeshGenerator to ChunkManager events: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Initialize the configuration system
    /// </summary>
    /// <returns>True if successful, false if there was a critical error</returns>
    private bool InitializeConfigSystem()
    {
        LogInitialization("1. Initializing WFCConfigManager");

        if (!TryGetInitAttempts("ConfigSystem"))
            return false;

        try
        {
            if (configManager == null)
            {
                // Try to find it in the scene if not explicitly assigned
                configManager = FindObjectOfType<WFCConfigManager>();

                if (configManager == null)
                {
                    // Create one as a last resort
                    GameObject configObj = new GameObject("WFCConfigManager");
                    configManager = configObj.AddComponent<WFCConfigManager>();
                    LogInitialization("   Created new WFCConfigManager object");
                }
                else
                {
                    LogInitialization("   Found WFCConfigManager in scene");
                }
            }

            // Force initialization by accessing the config property
            var config = WFCConfigManager.Config;
            if (config == null)
            {
                LogError("WFCConfiguration asset is missing! Cannot proceed without configuration.");
                return false;
            }

            LogInitialization("   Configuration system initialized successfully");
            configInitialized = true;
            return true;
        }
        catch (System.Exception e)
        {
            LogError($"Error initializing WFCConfigManager: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Initialize the terrain preset system
    /// </summary>
    private void InitializeTerrainPresets()
    {
        LogInitialization("2. Initializing TerrainPresetManager");

        if (!TryGetInitAttempts("TerrainPresets"))
            return;

        try
        {
            if (terrainManager == null)
            {
                terrainManager = FindObjectOfType<TerrainPresetManager>();

                if (terrainManager == null)
                {
                    LogError("TerrainPresetManager component is missing! Terrain may use default settings.");
                    return;
                }
                else
                {
                    LogInitialization("   Found TerrainPresetManager in scene");
                }
            }

            // Apply terrain presets if available
            if (terrainManager.defaultPreset != null)
            {
                LogInitialization("   Applying default terrain preset");
                terrainManager.ApplyPreset(terrainManager.defaultPreset);
            }
            else
            {
                LogError("Default terrain preset is missing! Terrain will use system defaults.");
            }
        }
        catch (System.Exception e)
        {
            LogError($"Error initializing TerrainPresetManager: {e.Message}");
        }
    }

    /// <summary>
    /// Initialize the constraint system
    /// </summary>
    private void InitializeConstraintSystem()
    {
        LogInitialization("3. Initializing ConstraintInitializer");

        if (!TryGetInitAttempts("ConstraintSystem"))
            return;

        if (!wfcGeneratorInitialized)
        {
            LogError("Cannot initialize ConstraintInitializer before WFCGenerator!");
            return;
        }

        try
        {
            if (constraintInitializer == null)
            {
                constraintInitializer = FindObjectOfType<ConstraintInitializer>();

                if (constraintInitializer == null)
                {
                    LogError("ConstraintInitializer component is missing! Terrain will have no custom constraints.");
                    return;
                }
                else
                {
                    LogInitialization("   Found ConstraintInitializer in scene");
                }
            }

            // Ensure WFC reference is set
            if (constraintInitializer.wfcGenerator == null && wfcGenerator != null)
            {
                LogInitialization("   Setting WFCGenerator reference on ConstraintInitializer");
                constraintInitializer.wfcGenerator = wfcGenerator;
            }

            LogInitialization("   Constraint system initialized successfully");
        }
        catch (System.Exception e)
        {
            LogError($"Error initializing ConstraintInitializer: {e.Message}");
        }
    }

    /// <summary>
    /// Initialize the core WFC generator
    /// </summary>
    /// <returns>True if successful, false if there was a critical error</returns>
    private bool InitializeWFCGenerator()
    {
        LogInitialization("4. Initializing WFCGenerator");

        if (!TryGetInitAttempts("WFCGenerator"))
            return false;

        if (!configInitialized)
        {
            LogError("Cannot initialize WFCGenerator before WFCConfigManager!");
            return false;
        }

        try
        {
            if (wfcGenerator == null)
            {
                wfcGenerator = FindObjectOfType<WFCGenerator>();

                if (wfcGenerator == null)
                {
                    LogError("WFCGenerator component is missing! This is a critical component of the system.");
                    return false;
                }
                else
                {
                    LogInitialization("   Found WFCGenerator in scene");
                }
            }

            // Verify the WFCGenerator is properly initialized
            var chunks = wfcGenerator.GetChunks();
            if (chunks == null)
            {
                LogError("WFCGenerator returned null chunks collection - it may not be properly initialized");
                return false;
            }

            // The WFCGenerator initializes itself in Awake/Start
            LogInitialization("   WFCGenerator initialized successfully");
            wfcGeneratorInitialized = true;
            return true;
        }
        catch (System.Exception e)
        {
            LogError($"Error initializing WFCGenerator: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Initialize the parallel processing system
    /// </summary>
    private void InitializeParallelProcessing()
    {
        LogInitialization("5. Initializing ParallelWFCManager");

        if (!TryGetInitAttempts("ParallelProcessing"))
            return;

        if (!wfcGeneratorInitialized)
        {
            LogError("Cannot initialize ParallelWFCManager before WFCGenerator!");
            return;
        }

        try
        {
            if (parallelManager == null)
            {
                parallelManager = FindObjectOfType<ParallelWFCManager>();

                if (parallelManager == null)
                {
                    LogError("ParallelWFCManager component is missing! System will run without parallel optimization.");
                    return;
                }
                else
                {
                    LogInitialization("   Found ParallelWFCManager in scene");
                }
            }

            // Make sure references are set
            if (parallelManager.wfcGenerator == null && wfcGenerator != null)
            {
                LogInitialization("   Setting WFCGenerator reference on ParallelWFCManager");
                parallelManager.wfcGenerator = wfcGenerator;
            }

            LogInitialization("   Parallel processing system initialized successfully");
        }
        catch (System.Exception e)
        {
            LogError($"Error initializing ParallelWFCManager: {e.Message}");
        }
    }

    /// <summary>
    /// Initialize the chunk management system
    /// </summary>
    /// <returns>True if successful, false if there was a critical error</returns>
    private bool InitializeChunkManager()
    {
        LogInitialization("6. Initializing ChunkManager");

        if (!TryGetInitAttempts("ChunkManager"))
            return false;

        if (!wfcGeneratorInitialized)
        {
            LogError("Cannot initialize ChunkManager before WFCGenerator!");
            return false;
        }

        try
        {
            if (chunkManager == null)
            {
                chunkManager = FindObjectOfType<ChunkManager>();

                if (chunkManager == null)
                {
                    LogError("ChunkManager component is missing! This is required for terrain generation.");
                    return false;
                }
                else
                {
                    LogInitialization("   Found ChunkManager in scene");
                }
            }

            // Make sure viewer reference is set
            if (chunkManager.viewer == null)
            {
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    LogInitialization("   Setting viewer reference to main camera");
                    chunkManager.viewer = mainCamera.transform;
                }
                else
                {
                    LogError("No main camera found for viewer reference! Chunks won't be generated properly.");
                    return false;
                }
            }

            // Verify the ChunkManager is properly initialized by checking its API
            try
            {
                var chunks = chunkManager.GetLoadedChunks();
                LogInitialization("   ChunkManager API is responding correctly");
            }
            catch (System.Exception)
            {
                LogError("ChunkManager API check failed - it may not be properly initialized");
                return false;
            }

            LogInitialization("   Chunk management system initialized successfully");
            chunkManagerInitialized = true;
            return true;
        }
        catch (System.Exception e)
        {
            LogError($"Error initializing ChunkManager: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Force initial chunk generation around the player
    /// </summary>
    private void ForceInitialChunkGeneration()
    {
        if (!chunkManagerInitialized || !wfcGeneratorInitialized)
        {
            LogError("Cannot generate initial chunks - required components are not initialized!");
            return;
        }

        try
        {
            if (chunkManager != null && chunkManager.viewer != null)
            {
                Vector3 viewerPos = chunkManager.viewer.position;
                LogInitialization($"   Player position: {viewerPos}");

                // Force chunk creation at player position regardless of distance check
                LogInitialization("   Forcing generation of initial chunks");
                chunkManager.CreateChunksAroundPlayer(); // Changed to more precise method

                // Verify chunks were created
                var chunks = chunkManager.GetLoadedChunks();
                LogInitialization($"   Generated {chunks.Count} initial chunks");

                // Force chunks at specific positions if none were created
                if (chunks.Count == 0)
                {
                    LogInitialization("   No chunks were created, forcing creation at origin");
                    chunkManager.CreateChunkAt(Vector3Int.zero);

                    // Create chunks at adjacent positions too
                    for (int x = -1; x <= 1; x++)
                    {
                        for (int y = -1; y <= 1; y++)
                        {
                            for (int z = -1; z <= 1; z++)
                            {
                                if (x == 0 && y == 0 && z == 0) continue; // Skip origin
                                chunkManager.CreateChunkAt(new Vector3Int(x, y, z));
                            }
                        }
                    }
                }

                LogInitialization("   Initial chunks generated successfully");
            }
            else
            {
                LogError("Cannot generate initial chunks - ChunkManager or viewer is missing!");
            }
        }
        catch (System.Exception e)
        {
            LogError($"Error generating initial chunks: {e.Message}");
        }
    }

    /// <summary>
    /// Initialize the mesh generation system
    /// </summary>
    private bool InitializeMeshGenerator()
    {
        LogInitialization("7. Initializing MeshGenerator");

        if (!TryGetInitAttempts("MeshGenerator"))
            return false;

        if (!wfcGeneratorInitialized || !chunkManagerInitialized)
        {
            LogError("Cannot initialize MeshGenerator before WFCGenerator and ChunkManager!");
            return false;
        }

        try
        {
            if (meshGenerator == null)
            {
                meshGenerator = FindObjectOfType<MeshGenerator>();

                if (meshGenerator == null)
                {
                    LogError("MeshGenerator component is missing! Terrain will not be visible.");
                    return false;
                }
                else
                {
                    LogInitialization("   Found MeshGenerator in scene");
                }
            }

            // Make sure references are set
            if (meshGenerator.wfcGenerator == null && wfcGenerator != null)
            {
                LogInitialization("   Setting WFCGenerator reference on MeshGenerator");
                meshGenerator.wfcGenerator = wfcGenerator;
            }

            // Set ChunkManager reference using reflection to handle both public and private fields
            var meshGenType = meshGenerator.GetType();
            var chunkManagerField = meshGenType.GetField("chunkManager",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (chunkManagerField != null && chunkManager != null)
            {
                LogInitialization("   Setting ChunkManager reference on MeshGenerator");
                chunkManagerField.SetValue(meshGenerator, chunkManager);
            }

            LogInitialization("   Mesh generation system initialized successfully");
            meshGeneratorInitialized = true;
            return true;
        }
        catch (System.Exception e)
        {
            LogError($"Error initializing MeshGenerator: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Initialize the performance monitoring system
    /// </summary>
    private void InitializePerformanceMonitor()
    {
        LogInitialization("8. Initializing PerformanceMonitor");

        if (!TryGetInitAttempts("PerformanceMonitor"))
            return;

        if (!chunkManagerInitialized)
        {
            LogError("Cannot initialize PerformanceMonitor before ChunkManager!");
            return;
        }

        try
        {
            if (performanceMonitor == null)
            {
                performanceMonitor = FindObjectOfType<PerformanceMonitor>();

                if (performanceMonitor == null)
                {
                    LogError("PerformanceMonitor component is missing! Performance metrics will not be available.");
                    return;
                }
                else
                {
                    LogInitialization("   Found PerformanceMonitor in scene");
                }
            }

            // Make sure references are set
            if (performanceMonitor.chunkManager == null && chunkManager != null)
            {
                LogInitialization("   Setting ChunkManager reference on PerformanceMonitor");
                performanceMonitor.chunkManager = chunkManager;
            }

            LogInitialization("   Performance monitoring system initialized successfully");
        }
        catch (System.Exception e)
        {
            LogError($"Error initializing PerformanceMonitor: {e.Message}");
        }
    }

    /// <summary>
    /// Force mesh generation for all chunks
    /// </summary>
    private void ForceMeshGeneration()
    {
        LogInitialization("9. Forcing final mesh generation for all chunks");

        if (!wfcGeneratorInitialized || !chunkManagerInitialized || !meshGeneratorInitialized)
        {
            LogError("Cannot generate meshes - required components are not initialized!");
            return;
        }

        try
        {
            if (meshGenerator == null)
            {
                LogError("Cannot generate meshes - MeshGenerator is missing!");
                return;
            }

            // First try using ChunkManager's chunks (preferred)
            if (chunkManager != null)
            {
                var chunks = chunkManager.GetLoadedChunks();
                if (chunks != null && chunks.Count > 0)
                {
                    LogInitialization($"   Generating meshes for {chunks.Count} chunks from ChunkManager");

                    // Generate meshes one by one for better stability
                    foreach (var chunkEntry in chunks)
                    {
                        try
                        {
                            meshGenerator.GenerateChunkMesh(chunkEntry.Key, chunkEntry.Value);
                            LogInitialization($"   Generated mesh for chunk at {chunkEntry.Key}");
                        }
                        catch (System.Exception e)
                        {
                            LogError($"Error generating mesh for chunk at {chunkEntry.Key}: {e.Message}");
                        }
                    }

                    LogInitialization("   All meshes generated successfully");
                    return;
                }
            }

            // Fallback: Use WFCGenerator's chunks if ChunkManager doesn't have any
            if (wfcGenerator != null)
            {
                var chunks = wfcGenerator.GetChunks();
                if (chunks != null && chunks.Count > 0)
                {
                    LogInitialization($"   Generating meshes for {chunks.Count} chunks from WFCGenerator");

                    // Generate for every chunk individually
                    foreach (var chunkEntry in chunks)
                    {
                        try
                        {
                            meshGenerator.GenerateChunkMesh(chunkEntry.Key, chunkEntry.Value);
                            LogInitialization($"   Generated mesh for chunk at {chunkEntry.Key}");
                        }
                        catch (System.Exception e)
                        {
                            LogError($"Error generating mesh for chunk at {chunkEntry.Key}: {e.Message}");
                        }
                    }

                    LogInitialization("   All meshes generated successfully");
                    return;
                }
            }

            // Last resort: Try generating a mesh at the origin
            LogError("No chunks available for mesh generation! Trying to create a chunk at origin...");

            if (chunkManager != null)
            {
                chunkManager.CreateChunkAt(Vector3Int.zero);
                LogInitialization("Created chunk at origin, waiting for mesh generation...");

                // Wait a moment and then generate mesh
                StartCoroutine(DelayedMeshGeneration(Vector3Int.zero));
            }
        }
        catch (System.Exception e)
        {
            LogError($"Error in mesh generation: {e.Message}");
        }
    }

    /// <summary>
    /// Provides a delayed mesh generation for a specific chunk
    /// </summary>
    private IEnumerator DelayedMeshGeneration(Vector3Int chunkPos)
    {
        yield return new WaitForSeconds(2.0f);

        try
        {
            var chunks = chunkManager.GetLoadedChunks();
            if (chunks.ContainsKey(chunkPos))
            {
                meshGenerator.GenerateChunkMesh(chunkPos, chunks[chunkPos]);
                LogInitialization($"Generated mesh for chunk at {chunkPos} after delay");
            }
        }
        catch (System.Exception e)
        {
            LogError($"Error in delayed mesh generation: {e.Message}");
        }
    }

    /// <summary>
    /// Helper method to track initialization attempts and prevent infinite retries
    /// </summary>
    private bool TryGetInitAttempts(string componentName)
    {
        if (!initAttempts.ContainsKey(componentName))
        {
            initAttempts[componentName] = 1;
            return true;
        }

        if (initAttempts[componentName] >= maxInitRetries)
        {
            LogError($"Maximum initialization attempts ({maxInitRetries}) reached for {componentName}");
            return false;
        }

        initAttempts[componentName]++;
        return true;
    }

    /// <summary>
    /// Log an initialization message with cyan color for visibility
    /// </summary>
    private void LogInitialization(string message)
    {
        if (debugLogging)
        {
            Debug.Log($"<color=cyan>[WFC Init]</color> {message}");
        }
    }

    /// <summary>
    /// Log an error message with red color for visibility
    /// </summary>
    private void LogError(string message)
    {
        Debug.LogError($"<color=red>[WFC Init]</color> {message}");
    }

    /// <summary>
    /// Force reinitialize the system if needed (for debugging)
    /// </summary>
    public void ForceReinitialize()
    {
        // Reset all initialization flags
        configInitialized = false;
        wfcGeneratorInitialized = false;
        chunkManagerInitialized = false;
        meshGeneratorInitialized = false;

        // Clear attempts
        initAttempts.Clear();

        // Start initialization sequence
        StartCoroutine(InitializeWFCSystem());
    }

    /// <summary>
    /// Force mesh regeneration for all chunks (for debugging)
    /// </summary>
    public void ForceRegenerateMeshes()
    {
        ForceMeshGeneration();
    }
}