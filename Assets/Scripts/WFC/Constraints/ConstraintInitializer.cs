//using UnityEngine;
//using WFC.Core;
//using WFC.Generation;
//using System.Collections.Generic;
//using System.Collections;

///// <summary>
///// This class initializes constraints for the Wave Function Collapse (WFC) algorithm.
///// </summary>
//public class ConstraintInitializer : MonoBehaviour
//{
//    [SerializeField] public WFCGenerator wfcGenerator;

//    [Header("Global Constraint Settings")]
//    [SerializeField] private bool createMountainRange = true;
//    [SerializeField] private bool createForest = true;

//    [Header("Region Constraint Settings")]
//    [SerializeField] private bool createTransitionZones = true;

//    // Serialized fields for world size
//    [Header("World Size Fallbacks")]
//    [SerializeField] private int fallbackWorldSizeX = 2;
//    [SerializeField] private int fallbackWorldSizeY = 2;
//    [SerializeField] private int fallbackWorldSizeZ = 1;

//    // Reference to the constraint system
//    private HierarchicalConstraintSystem constraintSystem;

//    private void Start()
//    {
//        if (wfcGenerator == null)
//        {
//            // Use the newer API instead of the deprecated FindObjectOfType
//            wfcGenerator = Object.FindAnyObjectByType<WFCGenerator>();
//            if (wfcGenerator == null)
//            {
//                Debug.LogError("ConstraintInitializer: No WFCGenerator found!");
//                return;
//            }
//        }
//        StartCoroutine(DelayedInitialization());
//    }

//    // Coroutine to delay initialization until WFCGenerator is ready
//    private IEnumerator DelayedInitialization()
//    {
//        // Wait a few frames to ensure other components are initialized
//        yield return new WaitForSeconds(0.2f);

//        // Get constraint system from the controller
//        if (wfcGenerator != null)
//        {
//            constraintSystem = wfcGenerator.GetHierarchicalConstraintSystem();
//            if (constraintSystem == null)
//            {
//                Debug.LogWarning("ConstraintInitializer: No HierarchicalConstraintSystem available in WFCController!");
//                yield break;
//            }

//            // Only now create constraints
//            Debug.Log("ConstraintInitializer: Successfully got constraint system, creating constraints...");

//            // Clear existing constraints
//            constraintSystem.ClearConstraints();

//            // Create new constraints
//            CreateGlobalConstraints();

//            if (createTransitionZones)
//                CreateRegionConstraints();
//        }
//        else
//        {
//            Debug.LogError("ConstraintInitializer: WFCController reference is null!");
//        }
//    }

//    // Create global constraints for the WFC algorithm
//    private void CreateGlobalConstraints()
//    {
//        // Get world dimensions using public properties or methods
//        // We'll use safe extraction methods for world sizes
//        Vector3Int worldSize = GetWorldSize();
//        int worldX = worldSize.x * wfcGenerator.ChunkSize;
//        int worldY = worldSize.y * wfcGenerator.ChunkSize;
//        int worldZ = worldSize.z * wfcGenerator.ChunkSize;

//        // Create ground level constraint (always present)
//        GlobalConstraint groundConstraint = new GlobalConstraint
//        {
//            Name = "Ground Level",
//            Type = ConstraintType.HeightMap,
//            WorldCenter = new Vector3(worldX / 2, 0, worldZ / 2),
//            WorldSize = new Vector3(worldX, 0, worldZ),
//            BlendRadius = wfcGenerator.ChunkSize,
//            Strength = 0.8f,
//            MinHeight = 0,
//            MaxHeight = worldY
//        };

//        // Add state biases for ground
//        groundConstraint.StateBiases[0] = -0.8f; // Empty (negative bias)
//        groundConstraint.StateBiases[1] = 0.6f;  // Ground (positive bias)

//        constraintSystem.AddGlobalConstraint(groundConstraint);

//        // Mountain range implementation
//        if (createMountainRange)
//        {
//            GlobalConstraint mountainConstraint = new GlobalConstraint
//            {
//                Name = "Mountain Range",
//                Type = ConstraintType.HeightMap,
//                WorldCenter = new Vector3(worldX * 0.25f, worldY * 0.5f, worldZ * 0.5f),
//                WorldSize = new Vector3(worldX * 0.3f, worldY * 0.7f, worldZ * 0.3f),
//                BlendRadius = wfcGenerator.ChunkSize * 2,
//                Strength = 0.7f,
//                MinHeight = worldY * 0.3f,
//                MaxHeight = worldY
//            };

//            mountainConstraint.StateBiases[0] = -0.5f; // Empty (negative at lower elevations)
//            mountainConstraint.StateBiases[1] = 0.2f;  // Ground (some ground)
//            mountainConstraint.StateBiases[4] = 0.7f;  // Rock (strong positive bias)

//            constraintSystem.AddGlobalConstraint(mountainConstraint);
//        }

//        // Forest implementation
//        if (createForest)
//        {
//            GlobalConstraint forestConstraint = new GlobalConstraint
//            {
//                Name = "Forest",
//                Type = ConstraintType.BiomeRegion,
//                WorldCenter = new Vector3(worldX * 0.7f, worldY * 0.2f, worldZ * 0.3f),
//                WorldSize = new Vector3(worldX * 0.4f, worldY * 0.3f, worldZ * 0.4f),
//                BlendRadius = wfcGenerator.ChunkSize * 1.5f,
//                Strength = 0.6f
//            };

//            forestConstraint.StateBiases[2] = 0.5f;  // Grass (moderate positive bias)
//            forestConstraint.StateBiases[6] = 0.7f;  // Tree (strong positive bias)

//            constraintSystem.AddGlobalConstraint(forestConstraint);
//        }
//    }

//    // Get world size safely
//    private Vector3Int GetWorldSize()
//    {
//        // Try to extract values from the test controller
//        Vector3Int size = new Vector3Int();

//        // Safe way to get world sizes - check if your WFCTestController has any of these methods
//        size.x = TryGetWorldSizeX() ?? fallbackWorldSizeX;
//        size.y = TryGetWorldSizeY() ?? fallbackWorldSizeY;
//        size.z = TryGetWorldSizeZ() ?? fallbackWorldSizeZ;

//        return size;
//    }

//    // Safe accessor methods that check for available methods
//    private int? TryGetWorldSizeX()
//    {
//        // Check if WFCTestController has a public GetWorldSizeX method
//        var method = wfcGenerator.GetType().GetMethod("GetWorldSizeX", System.Type.EmptyTypes);
//        if (method != null)
//        {
//            return (int)method.Invoke(wfcGenerator, null);
//        }
//        return null;
//    }

//    private int? TryGetWorldSizeY()
//    {
//        // Check if WFCTestController has a public GetWorldSizeX method
//        var method = wfcGenerator.GetType().GetMethod("GetWorldSizeY", System.Type.EmptyTypes);
//        if (method != null)
//        {
//            return (int)method.Invoke(wfcGenerator, null);
//        }
//        return null;
//    }

//    private int? TryGetWorldSizeZ()
//    {
//        // Check if WFCTestController has a public GetWorldSizeX method
//        var method = wfcGenerator.GetType().GetMethod("GetWorldSizeZ", System.Type.EmptyTypes);
//        if (method != null)
//        {
//            return (int)method.Invoke(wfcGenerator, null);
//        }
//        return null;
//    }

//    // Create region constraints for specific transitions
//    private void CreateRegionConstraints()
//    {
//        // Mountain to Forest transition
//        RegionConstraint mountainForestTransition = new RegionConstraint
//        {
//            Name = "Mountain-Forest Transition",
//            Type = RegionType.Transition,
//            ChunkPosition = new Vector3Int(1, 1, 0),
//            ChunkSize = new Vector3Int(1, 1, 1),
//            Strength = 0.7f,
//            Gradient = 0.5f,
//            SourceState = 4, // Rock
//            TargetState = 6  // Tree
//        };

//        constraintSystem.AddRegionConstraint(mountainForestTransition);

//        // Ground transition
//        RegionConstraint groundTransition = new RegionConstraint
//        {
//            Name = "Ground Transition",
//            Type = RegionType.Transition,
//            ChunkPosition = new Vector3Int(1, 0, 0),
//            ChunkSize = new Vector3Int(1, 1, 1),
//            InternalOrigin = new Vector3(0.3f, 0, 0.3f),
//            InternalSize = new Vector3(0.4f, 1, 0.4f),
//            Strength = 0.6f,
//            Gradient = 0.3f,
//            SourceState = 1, // Ground
//            TargetState = 2  // Grass
//        };

//        constraintSystem.AddRegionConstraint(groundTransition);
//    }
//}