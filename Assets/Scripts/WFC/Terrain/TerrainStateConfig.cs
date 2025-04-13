// TerrainStateConfig.cs
using UnityEngine;

namespace WFC.Terrain
{
    /// <summary>
    /// Configuration for a terrain state/type in the WFC system
    /// </summary>
    [System.Serializable]
    public class TerrainStateConfig
    {
        // Basic properties
        public string Name;
        public int Id = -1; // -1 means auto-assign

        // Gameplay properties
        public bool IsTraversable = true;

        // Visual properties
        public Color Color = Color.white;
        public string DefaultMaterial;

        // Mesh generation properties
        public float Density = 0.5f; // For marching cubes (0-1)

        // Environmental properties
        public float Temperature = 0.5f; // 0 = coldest, 1 = hottest
        public float Moisture = 0.5f; // 0 = driest, 1 = wettest
        public float Fertility = 0.5f; // 0 = barren, 1 = fertile

        // Special flags
        public bool IsLiquid = false;
        public bool IsVegetation = false;
    }
}