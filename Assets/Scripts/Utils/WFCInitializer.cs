using System.Collections;
using UnityEngine;
using WFC.Chunking;
using WFC.Configuration;
using WFC.Generation;
using WFC.MarchingCubes;
using WFC.Performance;
using WFC.Presets;
using WFC.Processing;

public class WFCInitializer : MonoBehaviour
{
    [Header("WFC System Components")]
    [SerializeField] private WFCConfigManager configManager;
    [SerializeField] private TerrainPresetManager terrainManager;
    [SerializeField] private ConstraintInitializer constraintInitializer;
    [SerializeField] private WFCGenerator wfcGenerator;
    [SerializeField] private ParallelWFCManager parallelManager;
    [SerializeField] private ChunkManager chunkManager;
    [SerializeField] private MeshGenerator meshGenerator;
    [SerializeField] private PerformanceMonitor perfMonitor;

    [Header("Settings")]
    [SerializeField] private float initDelayBetweenSteps = 0.1f;
    [SerializeField] private bool debugLogging = true;

    private void Start()
    {
        StartCoroutine(InitializeWFCSystem());
    }

    private IEnumerator InitializeWFCSystem()
    {
        LogInitialization("Starting WFC System initialization sequence");

        // Step 1: WFCConfigManager
        LogInitialization("1. Initializing WFCConfigManager");
        if (configManager != null)
        {
            // Force initialization by accessing config
            var config = WFCConfigManager.Config;
            if (config == null)
            {
                LogError("WFCConfiguration asset is missing!");
                yield break; // Stop if config is missing
            }
        }
        else
        {
            LogError("WFCConfigManager component is missing!");
            yield break;
        }
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 2: TerrainPresetManager
        LogInitialization("2. Initializing TerrainPresetManager");
        if (terrainManager != null)
        {
            // Apply terrain presets
            if (terrainManager.defaultPreset != null)
            {
                terrainManager.ApplyPreset(terrainManager.defaultPreset);
            }
            else
            {
                LogError("Default terrain preset is missing!");
            }
        }
        else
        {
            LogError("TerrainPresetManager component is missing!");
        }
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 3: ConstraintInitializer
        LogInitialization("3. Initializing ConstraintInitializer");
        if (constraintInitializer != null)
        {
            // Ensure WFC reference is set
            if (constraintInitializer.wfcGenerator == null && wfcGenerator != null)
            {
                LogInitialization("   Setting WFC reference on ConstraintInitializer");
                constraintInitializer.wfcGenerator = wfcGenerator;
            }

            // We need to wait for it to initialize on its own via Start()
            // but we can force a delay to ensure it happens
        }
        else
        {
            LogError("ConstraintInitializer component is missing!");
        }
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 4: WFCGenerator
        LogInitialization("4. Initializing WFCGenerator");
        if (wfcGenerator != null)
        {
            // The WFCGenerator initializes itself in Awake/Start
            // We just need to wait for it to complete
        }
        else
        {
            LogError("WFCGenerator component is missing!");
            yield break; // Critical component missing
        }
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 5: ParallelWFCManager
        LogInitialization("5. Initializing ParallelWFCManager");
        if (parallelManager != null)
        {
            // Make sure references are set
            if (parallelManager.wfcGenerator == null && wfcGenerator != null)
            {
                LogInitialization("   Setting WFCGenerator reference on ParallelWFCManager");
                parallelManager.wfcGenerator = wfcGenerator;
            }
        }
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 6: ChunkManager
        LogInitialization("6. Initializing ChunkManager");
        if (chunkManager != null)
        {
            // Make sure references are set
            if (chunkManager.viewer == null)
            {
                LogInitialization("   Setting viewer reference to main camera");
                chunkManager.viewer = Camera.main.transform;
            }
        }
        else
        {
            LogError("ChunkManager component is missing!");
        }
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 7: MeshGenerator
        LogInitialization("7. Initializing MeshGenerator");
        if (meshGenerator != null)
        {
            // Make sure references are set
            if (meshGenerator.wfcGenerator == null && wfcGenerator != null)
            {
                LogInitialization("   Setting WFCGenerator reference on MeshGenerator");
                meshGenerator.wfcGenerator = wfcGenerator;
            }

            // Generate initial meshes
            LogInitialization("   Generating initial meshes");
            meshGenerator.GenerateAllMeshes();
        }
        else
        {
            LogError("MeshGenerator component is missing!");
        }
        yield return new WaitForSeconds(initDelayBetweenSteps);

        // Step 8: PerformanceMonitor
        LogInitialization("8. Initializing PerformanceMonitor");
        if (perfMonitor != null)
        {
            // Make sure references are set
            if (perfMonitor.chunkManager == null && chunkManager != null)
            {
                LogInitialization("   Setting ChunkManager reference on PerformanceMonitor");
                perfMonitor.chunkManager = chunkManager;
            }
        }

        LogInitialization("WFC System initialization complete!");
    }

    private void LogInitialization(string message)
    {
        if (debugLogging)
        {
            Debug.Log($"<color=cyan>[WFC Init]</color> {message}");
        }
    }

    private void LogError(string message)
    {
        Debug.LogError($"<color=red>[WFC Init]</color> {message}");
    }
}