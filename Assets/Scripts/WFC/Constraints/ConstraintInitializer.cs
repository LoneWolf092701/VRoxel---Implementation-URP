using UnityEngine;
using WFC.Core;
using WFC.Testing;
using System.Collections.Generic;
using System.Collections;

public class ConstraintInitializer : MonoBehaviour
{
    [SerializeField] private WFCTestController wfcController;

    [Header("Global Constraint Settings")]
    [SerializeField] private bool createMountainRange = true;
    [SerializeField] private bool createRiver = true;
    [SerializeField] private bool createForest = true;

    [Header("Region Constraint Settings")]
    [SerializeField] private bool createTransitionZones = true;

    // Serialized fields for world size
    [Header("World Size Fallbacks")]
    [SerializeField] private int fallbackWorldSizeX = 2;
    [SerializeField] private int fallbackWorldSizeY = 2;
    [SerializeField] private int fallbackWorldSizeZ = 1;

    // Reference to the constraint system
    private HierarchicalConstraintSystem constraintSystem;

    private void Start()
    {
        if (wfcController == null)
        {
            // Use the newer API instead of the deprecated FindObjectOfType
            wfcController = Object.FindAnyObjectByType<WFCTestController>();
            if (wfcController == null)
            {
                Debug.LogError("ConstraintInitializer: No WFCTestController found!");
                return;
            }
        }
        StartCoroutine(DelayedInitialization());

        // Clear existing constraints
        constraintSystem.ClearConstraints();

        // Create new constraints
        CreateGlobalConstraints();

        if (createTransitionZones)
            CreateRegionConstraints();
    }

    private IEnumerator DelayedInitialization() 
    {

        yield return null;
        // Get constraint system from the test controller
        constraintSystem = wfcController.GetHierarchicalConstraintSystem();
        if (constraintSystem == null)
        {
            Debug.LogError("ConstraintInitializer: No HierarchicalConstraintSystem available in WFCTestController!");
            yield break;
        }
    }

    private void CreateGlobalConstraints()
    {
        // Get world dimensions using public properties or methods
        // We'll use safe extraction methods for world sizes
        Vector3Int worldSize = GetWorldSize();
        int worldX = worldSize.x * wfcController.ChunkSize;
        int worldY = worldSize.y * wfcController.ChunkSize;
        int worldZ = worldSize.z * wfcController.ChunkSize;

        // Create ground level constraint (always present)
        GlobalConstraint groundConstraint = new GlobalConstraint
        {
            Name = "Ground Level",
            Type = ConstraintType.HeightMap,
            WorldCenter = new Vector3(worldX / 2, 0, worldZ / 2),
            WorldSize = new Vector3(worldX, 0, worldZ),
            BlendRadius = wfcController.ChunkSize,
            Strength = 0.8f,
            MinHeight = 0,
            MaxHeight = worldY
        };

        // Add state biases for ground
        groundConstraint.StateBiases[0] = -0.8f; // Empty (negative bias)
        groundConstraint.StateBiases[1] = 0.6f;  // Ground (positive bias)

        constraintSystem.AddGlobalConstraint(groundConstraint);

        // The rest of your implementation remains the same...
        // Adding mountain range, river, and forest constraints

        // Mountain range implementation
        if (createMountainRange)
        {
            GlobalConstraint mountainConstraint = new GlobalConstraint
            {
                Name = "Mountain Range",
                Type = ConstraintType.HeightMap,
                WorldCenter = new Vector3(worldX * 0.25f, worldY * 0.5f, worldZ * 0.5f),
                WorldSize = new Vector3(worldX * 0.3f, worldY * 0.7f, worldZ * 0.3f),
                BlendRadius = wfcController.ChunkSize * 2,
                Strength = 0.7f,
                MinHeight = worldY * 0.3f,
                MaxHeight = worldY
            };

            mountainConstraint.StateBiases[0] = -0.5f; // Empty (negative at lower elevations)
            mountainConstraint.StateBiases[1] = 0.2f;  // Ground (some ground)
            mountainConstraint.StateBiases[4] = 0.7f;  // Rock (strong positive bias)

            constraintSystem.AddGlobalConstraint(mountainConstraint);
        }

        // River implementation
        if (createRiver)
        {
            GlobalConstraint riverConstraint = new GlobalConstraint
            {
                Name = "River",
                Type = ConstraintType.RiverPath,
                Strength = 0.8f,
                PathWidth = wfcController.ChunkSize * 0.5f,
                BlendRadius = wfcController.ChunkSize
            };

            // Add control points for the river path
            riverConstraint.ControlPoints.Add(new Vector3(0, 2, worldZ * 0.3f));
            riverConstraint.ControlPoints.Add(new Vector3(worldX * 0.3f, 1, worldZ * 0.5f));
            riverConstraint.ControlPoints.Add(new Vector3(worldX * 0.7f, 0, worldZ * 0.6f));
            riverConstraint.ControlPoints.Add(new Vector3(worldX, 0, worldZ * 0.7f));

            riverConstraint.StateBiases[3] = 0.9f;  // Water (strong positive bias)
            riverConstraint.StateBiases[5] = 0.4f;  // Sand (moderate positive bias)

            constraintSystem.AddGlobalConstraint(riverConstraint);
        }

        // Forest implementation
        if (createForest)
        {
            GlobalConstraint forestConstraint = new GlobalConstraint
            {
                Name = "Forest",
                Type = ConstraintType.BiomeRegion,
                WorldCenter = new Vector3(worldX * 0.7f, worldY * 0.2f, worldZ * 0.3f),
                WorldSize = new Vector3(worldX * 0.4f, worldY * 0.3f, worldZ * 0.4f),
                BlendRadius = wfcController.ChunkSize * 1.5f,
                Strength = 0.6f
            };

            forestConstraint.StateBiases[2] = 0.5f;  // Grass (moderate positive bias)
            forestConstraint.StateBiases[6] = 0.7f;  // Tree (strong positive bias)

            constraintSystem.AddGlobalConstraint(forestConstraint);
        }
    }

    // Get world size safely
    private Vector3Int GetWorldSize()
    {
        // Try to extract values from the test controller
        Vector3Int size = new Vector3Int();

        // Safe way to get world sizes - check if your WFCTestController has any of these methods
        size.x = TryGetWorldSizeX() ?? fallbackWorldSizeX;
        size.y = TryGetWorldSizeY() ?? fallbackWorldSizeY;
        size.z = TryGetWorldSizeZ() ?? fallbackWorldSizeZ;

        return size;
    }

    // Safe accessor methods that check for available methods
    private int? TryGetWorldSizeX()
    {
        // Check if WFCTestController has a public GetWorldSizeX method
        var method = wfcController.GetType().GetMethod("GetWorldSizeX", System.Type.EmptyTypes);
        if (method != null)
        {
            return (int)method.Invoke(wfcController, null);
        }

        // Your WFCTestController might have different public accessor
        // For example, it might have a property called WorldSize or similar
        return null;
    }

    private int? TryGetWorldSizeY()
    {
        // Similar implementation
        return null;
    }

    private int? TryGetWorldSizeZ()
    {
        // Similar implementation
        return null;
    }

    private void CreateRegionConstraints()
    {
        // Create transition zones between different biomes
        // Implementation remains the same

        // Mountain to Forest transition
        RegionConstraint mountainForestTransition = new RegionConstraint
        {
            Name = "Mountain-Forest Transition",
            Type = RegionType.Transition,
            ChunkPosition = new Vector3Int(1, 1, 0),
            ChunkSize = new Vector3Int(1, 1, 1),
            Strength = 0.7f,
            Gradient = 0.5f,
            SourceState = 4, // Rock
            TargetState = 6  // Tree
        };

        constraintSystem.AddRegionConstraint(mountainForestTransition);

        // River to Ground transition
        RegionConstraint riverGroundTransition = new RegionConstraint
        {
            Name = "River-Ground Transition",
            Type = RegionType.Transition,
            ChunkPosition = new Vector3Int(1, 0, 0),
            ChunkSize = new Vector3Int(1, 1, 1),
            InternalOrigin = new Vector3(0.3f, 0, 0.3f),
            InternalSize = new Vector3(0.4f, 1, 0.4f),
            Strength = 0.6f,
            Gradient = 0.3f,
            SourceState = 3, // Water
            TargetState = 1  // Ground
        };

        constraintSystem.AddRegionConstraint(riverGroundTransition);
    }
}