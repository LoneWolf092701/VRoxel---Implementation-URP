using WFC.Core;
using UnityEngine;

namespace WFC.Terrain
{
    public interface ITerrainGenerator
    {
        void GenerateTerrain(Chunk chunk, System.Random random);
        void ApplyConstraints(HierarchicalConstraintSystem system, Vector3Int worldSize, int chunkSize);
    }
}