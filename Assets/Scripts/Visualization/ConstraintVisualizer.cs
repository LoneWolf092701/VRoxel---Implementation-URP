// Assets/Scripts/Visualization/ConstraintVisualizer.cs
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;
using WFC.Generation;
using WFC.Testing;

namespace Visualization
{
    /// <summary>
    /// Visualizes the hierarchical constraints in the scene view for debugging
    /// </summary>
    [RequireComponent(typeof(WFCGenerator))]
    public class ConstraintVisualizer : MonoBehaviour
    {
        [Header("Visualization Settings")]
        [SerializeField] private bool showGlobalConstraints = true;
        [SerializeField] private bool showRegionConstraints = true;
        [SerializeField] private float alpha = 0.3f;

        [Header("Color Settings")]
        [SerializeField] private Color biomeRegionColor = new Color(0, 0.8f, 0, 0.3f);
        [SerializeField] private Color heightMapColor = new Color(0.7f, 0.4f, 0, 0.3f);
        [SerializeField] private Color riverPathColor = new Color(0, 0.4f, 0.8f, 0.3f);
        [SerializeField] private Color structureColor = new Color(0.8f, 0.8f, 0, 0.3f);
        [SerializeField] private Color transitionColor = new Color(0.8f, 0, 0.8f, 0.3f);
        [SerializeField] private Color featureColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        private WFCGenerator generator;
        private HierarchicalConstraintSystem constraintSystem;

        private void Awake()
        {
            generator = GetComponent<WFCGenerator>();
            if (generator == null)
            {
                var testController = GetComponent<WFCTestController>();
                if (testController != null)
                {
                    // Use the test controller instead
                    constraintSystem = testController.GetHierarchicalConstraintSystem();
                }
            }

        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;

            // Get constraint system when it's available
            if (constraintSystem == null)
            {
                if (generator != null)
                    constraintSystem = generator.GetHierarchicalConstraintSystem();
                else
                {
                    var testController = GetComponent<WFCTestController>();
                    if (testController != null)
                        constraintSystem = testController.GetHierarchicalConstraintSystem();
                }
            }

            if (constraintSystem == null)
                return;

            // Draw global constraints
            if (showGlobalConstraints)
                DrawGlobalConstraints();

            // Draw region constraints
            if (showRegionConstraints)
                DrawRegionConstraints();
        }

        private void DrawGlobalConstraints()
        {
            var constraints = constraintSystem.GetGlobalConstraints();

            foreach (var constraint in constraints)
            {
                // Set color based on constraint type
                Color color = GetColorForGlobalConstraint(constraint);

                // Apply alpha and strength
                color.a = alpha * constraint.Strength;
                Gizmos.color = color;

                // Draw main region
                Vector3 center = constraint.WorldCenter;
                Vector3 size = constraint.WorldSize;

                Gizmos.DrawCube(center, size);

                // Draw wireframe for blend radius
                Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
                Gizmos.DrawWireCube(center, size + new Vector3(constraint.BlendRadius * 2, constraint.BlendRadius * 2, constraint.BlendRadius * 2));

                // Handle special visualizations based on type
                if (constraint.Type == ConstraintType.RiverPath)
                {
                    DrawRiverPath(constraint);
                }
            }
        }

        private void DrawRiverPath(GlobalConstraint constraint)
        {
            List<Vector3> points = constraint.ControlPoints;

            if (points.Count < 2)
                return;

            // Draw lines between control points
            Gizmos.color = riverPathColor;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Gizmos.DrawLine(points[i], points[i + 1]);

                // Draw spheres at control points
                Gizmos.DrawSphere(points[i], constraint.PathWidth * 0.2f);
            }

            // Draw last point
            Gizmos.DrawSphere(points[points.Count - 1], constraint.PathWidth * 0.2f);

            // Draw path width
            Gizmos.color = new Color(riverPathColor.r, riverPathColor.g, riverPathColor.b, 0.2f);

            for (int i = 0; i < points.Count - 1; i++)
            {
                // Calculate direction perpendicular to path
                Vector3 direction = (points[i + 1] - points[i]).normalized;
                Vector3 perpendicular = new Vector3(-direction.z, 0, direction.x);

                // Draw width lines
                Gizmos.DrawLine(
                    points[i] + perpendicular * constraint.PathWidth * 0.5f,
                    points[i] - perpendicular * constraint.PathWidth * 0.5f
                );

                Gizmos.DrawLine(
                    points[i + 1] + perpendicular * constraint.PathWidth * 0.5f,
                    points[i + 1] - perpendicular * constraint.PathWidth * 0.5f
                );

                // Draw side lines
                Gizmos.DrawLine(
                    points[i] + perpendicular * constraint.PathWidth * 0.5f,
                    points[i + 1] + perpendicular * constraint.PathWidth * 0.5f
                );

                Gizmos.DrawLine(
                    points[i] - perpendicular * constraint.PathWidth * 0.5f,
                    points[i + 1] - perpendicular * constraint.PathWidth * 0.5f
                );
            }
        }

        private void DrawRegionConstraints()
        {
            var constraints = constraintSystem.GetRegionConstraints();
            int chunkSize = generator.ChunkSize;

            foreach (var constraint in constraints)
            {
                // Set color based on constraint type
                Color color = GetColorForRegionConstraint(constraint);

                // Apply alpha and strength
                color.a = alpha * constraint.Strength;
                Gizmos.color = color;

                // Calculate world position and size
                Vector3 chunkWorldPos = new Vector3(
                    constraint.ChunkPosition.x * chunkSize,
                    constraint.ChunkPosition.y * chunkSize,
                    constraint.ChunkPosition.z * chunkSize
                );

                Vector3 chunkWorldSize = new Vector3(
                    constraint.ChunkSize.x * chunkSize,
                    constraint.ChunkSize.y * chunkSize,
                    constraint.ChunkSize.z * chunkSize
                );

                // If constraint affects specific internal region of the chunk
                if (constraint.InternalSize != Vector3.one || constraint.InternalOrigin != Vector3.zero)
                {
                    // Calculate internal region
                    Vector3 internalPos = new Vector3(
                        chunkWorldPos.x + constraint.InternalOrigin.x * chunkSize * constraint.ChunkSize.x,
                        chunkWorldPos.y + constraint.InternalOrigin.y * chunkSize * constraint.ChunkSize.y,
                        chunkWorldPos.z + constraint.InternalOrigin.z * chunkSize * constraint.ChunkSize.z
                    );

                    Vector3 internalSize = new Vector3(
                        constraint.InternalSize.x * chunkSize * constraint.ChunkSize.x,
                        constraint.InternalSize.y * chunkSize * constraint.ChunkSize.y,
                        constraint.InternalSize.z * chunkSize * constraint.ChunkSize.z
                    );

                    // Draw internal region
                    Vector3 center = internalPos + internalSize * 0.5f;
                    Gizmos.DrawCube(center, internalSize);

                    // Draw wireframe for chunk
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.2f);
                    Gizmos.DrawWireCube(
                        chunkWorldPos + chunkWorldSize * 0.5f,
                        chunkWorldSize
                    );
                }
                else
                {
                    // Draw entire chunk
                    Vector3 center = chunkWorldPos + chunkWorldSize * 0.5f;
                    Gizmos.DrawCube(center, chunkWorldSize);
                }

                // Handle transition regions specially
                if (constraint.Type == RegionType.Transition)
                {
                    DrawTransitionRegion(constraint, chunkSize);
                }
            }
        }

        private void DrawTransitionRegion(RegionConstraint constraint, int chunkSize)
        {
            // Define additional visualization for transition regions
            // This is a simplified visualization

            Vector3 worldPos = new Vector3(
                constraint.ChunkPosition.x * chunkSize,
                constraint.ChunkPosition.y * chunkSize,
                constraint.ChunkPosition.z * chunkSize
            );

            Vector3 worldSize = new Vector3(
                constraint.ChunkSize.x * chunkSize,
                constraint.ChunkSize.y * chunkSize,
                constraint.ChunkSize.z * chunkSize
            );

            // Draw an arrow indicating direction of transition
            Vector3 center = worldPos + worldSize * 0.5f;

            // Simple arrow (just a line for now)
            Vector3 arrowStart = center - new Vector3(worldSize.x * 0.3f, 0, 0);
            Vector3 arrowEnd = center + new Vector3(worldSize.x * 0.3f, 0, 0);

            Gizmos.color = Color.white;
            Gizmos.DrawLine(arrowStart, arrowEnd);

            // Arrow head
            Vector3 arrowDir = (arrowEnd - arrowStart).normalized;
            Vector3 right = Quaternion.Euler(0, 30, 0) * -arrowDir;
            Vector3 left = Quaternion.Euler(0, -30, 0) * -arrowDir;

            Gizmos.DrawLine(arrowEnd, arrowEnd + right * worldSize.x * 0.1f);
            Gizmos.DrawLine(arrowEnd, arrowEnd + left * worldSize.x * 0.1f);

            // Label the states somehow
            // Would be better with handles/GUI but those aren't available in OnDrawGizmos
        }

        private Color GetColorForGlobalConstraint(GlobalConstraint constraint)
        {
            switch (constraint.Type)
            {
                case ConstraintType.BiomeRegion:
                    return biomeRegionColor;
                case ConstraintType.HeightMap:
                    return heightMapColor;
                case ConstraintType.RiverPath:
                    return riverPathColor;
                case ConstraintType.Structure:
                    return structureColor;
                default:
                    return Color.white;
            }
        }

        private Color GetColorForRegionConstraint(RegionConstraint constraint)
        {
            switch (constraint.Type)
            {
                case RegionType.Transition:
                    return transitionColor;
                case RegionType.Feature:
                    return featureColor;
                case RegionType.Elevation:
                    return heightMapColor;
                case RegionType.Pattern:
                    return biomeRegionColor;
                default:
                    return Color.white;
            }
        }
    }
}