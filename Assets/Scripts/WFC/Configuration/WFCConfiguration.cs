using System;
using UnityEngine;

[CreateAssetMenu(fileName = "WFCConfig", menuName = "WFC/Configuration")]
public class WFCConfiguration : ScriptableObject
{
    [Serializable]
    public class WorldSettings
    {
        public int worldSizeX = 2;
        public int worldSizeY = 2;
        public int worldSizeZ = 1;
        public int chunkSize = 8;
        public int maxStates = 7;
        public int randomSeed = 0;
    }

    [Serializable]
    public class AlgorithmSettings
    {
        public float boundaryCoherenceWeight = 1.0f;
        public float entropyWeight = 1.0f;
        public float constraintWeight = 1.0f;
        public bool useConstraints = true;
        public int maxIterationsPerChunk = 100;
    }

    [Serializable]
    public class VisualizationSettings
    {
        public float cellSize = 1.0f;
        public bool showUncollapsedCells = true;
        public bool highlightBoundaries = true;
    }

    [Serializable]
    public class PerformanceSettings
    {
        public int maxThreads = 4;
        public int meshGenerationPriority = 50;
        public int cacheSizeLimit = 100;
        public float loadDistance = 100f;
        public float unloadDistance = 150f;
        public int maxConcurrentChunks = 16;

        // LOD Settings - New addition for the LOD system
        [Serializable]
        public class LODSettings
        {
            [Tooltip("Distance thresholds for each LOD level (in world units)")]
            public float[] lodDistanceThresholds = new float[] { 50f, 100f, 200f };

            [Tooltip("Maximum iterations per chunk for each LOD level")]
            public int[] maxIterationsPerLOD = new int[] { 100, 50, 25 };

            [Tooltip("Constraint influence multiplier per LOD level (0-1)")]
            public float[] constraintInfluencePerLOD = new float[] { 1.0f, 0.7f, 0.4f };

            [Tooltip("Use simplified mesh generation for distant LODs")]
            public bool useSimplifiedMeshes = true;

            [Tooltip("Vertex reduction percentage per LOD level (0-1)")]
            public float[] vertexReductionPerLOD = new float[] { 0.0f, 0.4f, 0.7f };

            [Tooltip("Use Unity's built-in LOD system for rendering")]
            public bool useUnityLODSystem = false;
        }

        public LODSettings lodSettings = new LODSettings();
    }

    [Header("World Settings")]
    public WorldSettings World = new WorldSettings();

    [Header("Algorithm Settings")]
    public AlgorithmSettings Algorithm = new AlgorithmSettings();

    [Header("Visualization Settings")]
    public VisualizationSettings Visualization = new VisualizationSettings();

    [Header("Performance Settings")]
    public PerformanceSettings Performance = new PerformanceSettings();

    public bool Validate()
    {
        bool valid = true;

        // Validate world settings
        if (World.chunkSize <= 0)
        {
            Debug.LogError("Configuration Error: Chunk size must be greater than 0");
            valid = false;
        }

        if (World.maxStates <= 1)
        {
            Debug.LogError("Configuration Error: Max states must be greater than 1");
            valid = false;
        }

        // Validate performance settings
        if (Performance.maxThreads < 0)
        {
            Debug.LogError("Configuration Error: Max threads cannot be negative");
            valid = false;
        }

        // Validate LOD settings
        if (Performance.lodSettings.lodDistanceThresholds.Length == 0)
        {
            Debug.LogWarning("Configuration Warning: No LOD distance thresholds defined");
        }

        if (Performance.lodSettings.maxIterationsPerLOD.Length == 0)
        {
            Debug.LogWarning("Configuration Warning: No LOD iteration limits defined");
        }

        return valid;
    }
}