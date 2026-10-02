using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Cinematic
{
    /// <summary>
    /// The finite world's terrain trees in a coarse grid for the cinematic camera's sight tests (terrain trees have no
    /// GameObjects to raycast against reliably). Built once from the terrain's tree instances; positions in scene space.
    /// </summary>
    internal sealed class TerrainTreeIndex
    {
        private const float CellSize = 32f;

        private readonly Vector3[] positions;
        private readonly float[] heights;
        private readonly float[] crowns;
        private readonly float[] trunks;
        private readonly int[] cellStart;
        private readonly int[] cellItems;
        private readonly int cellsX, cellsZ;
        private readonly Vector3 origin;

        private TerrainTreeIndex(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            origin = terrain.GetPosition();
            Vector3 size = data.size;
            TreePrototype[] prototypes = data.treePrototypes;
            var protoHeight = new float[prototypes.Length];
            var protoCrown = new float[prototypes.Length];
            var protoTrunk = new float[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++)
            {
                GameObject prefab = prototypes[i].prefab;
                Bounds b = WorldBuilder.LocalBounds(prefab);
                protoHeight[i] = Mathf.Max(1f, b.max.y);
                protoCrown[i] = Mathf.Max(0.5f, Mathf.Max(b.extents.x, b.extents.z));
                CapsuleCollider capsule = prefab != null ? prefab.GetComponentInChildren<CapsuleCollider>(true) : null;
                protoTrunk[i] = capsule != null ? Mathf.Clamp(capsule.radius, 0.1f, 1f) : Mathf.Clamp(b.size.y * 0.025f, 0.15f, 0.45f);
            }

            TreeInstance[] trees = data.treeInstances;
            int n = trees.Length;
            positions = new Vector3[n];
            heights = new float[n];
            crowns = new float[n];
            trunks = new float[n];
            cellsX = Mathf.Max(1, Mathf.CeilToInt(size.x / CellSize));
            cellsZ = Mathf.Max(1, Mathf.CeilToInt(size.z / CellSize));
            var counts = new int[cellsX * cellsZ];
            var cellOf = new int[n];
            for (int i = 0; i < n; i++)
            {
                TreeInstance t = trees[i];
                int p = Mathf.Clamp(t.prototypeIndex, 0, prototypes.Length - 1);
                positions[i] = origin + Vector3.Scale(t.position, size);
                heights[i] = prototypes.Length > 0 ? protoHeight[p] * t.heightScale : 8f;
                crowns[i] = prototypes.Length > 0 ? protoCrown[p] * t.widthScale : 3f;
                trunks[i] = prototypes.Length > 0 ? protoTrunk[p] * t.widthScale : 0.3f;
                int cx = Mathf.Clamp((int)((positions[i].x - origin.x) / CellSize), 0, cellsX - 1);
                int cz = Mathf.Clamp((int)((positions[i].z - origin.z) / CellSize), 0, cellsZ - 1);
                cellOf[i] = cz * cellsX + cx;
                counts[cellOf[i]]++;
            }

            cellStart = new int[counts.Length + 1];
            for (int c = 0; c < counts.Length; c++) cellStart[c + 1] = cellStart[c] + counts[c];
            cellItems = new int[n];
            var fill = new int[counts.Length];
            for (int i = 0; i < n; i++)
            {
                int c = cellOf[i];
                cellItems[cellStart[c] + fill[c]++] = i;
            }
        }

        private static readonly Dictionary<Terrain, TerrainTreeIndex> Cache = new Dictionary<Terrain, TerrainTreeIndex>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        /// <summary>The index of <paramref name="terrain"/> (built on first use), or null without terrain trees.</summary>
        public static TerrainTreeIndex For(Terrain terrain)
        {
            if (terrain == null || terrain.terrainData == null) return null;
            if (Cache.TryGetValue(terrain, out TerrainTreeIndex index)) return index;
            index = terrain.terrainData.treeInstanceCount > 0 ? new TerrainTreeIndex(terrain) : null;
            Cache[terrain] = index;
            return index;
        }

        /// <summary>True when the segment passes through a tree's trunk or crown core.</summary>
        public bool SegmentBlocked(Vector3 a, Vector3 b)
        {
            const float pad = 6f;   // crowns reach over cell edges
            int x0 = Cell(Mathf.Min(a.x, b.x) - pad, origin.x, cellsX), x1 = Cell(Mathf.Max(a.x, b.x) + pad, origin.x, cellsX);
            int z0 = Cell(Mathf.Min(a.z, b.z) - pad, origin.z, cellsZ), z1 = Cell(Mathf.Max(a.z, b.z) + pad, origin.z, cellsZ);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    int c = cz * cellsX + cx;
                    for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
                    {
                        int i = cellItems[k];
                        if (CinematicMath.TreeBlocks(a, b, positions[i], heights[i], crowns[i], trunks[i])) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>True when a point is near a trunk or inside a crown core.</summary>
        public bool PointInside(Vector3 p, float pad)
        {
            int x0 = Cell(p.x - 6f, origin.x, cellsX), x1 = Cell(p.x + 6f, origin.x, cellsX);
            int z0 = Cell(p.z - 6f, origin.z, cellsZ), z1 = Cell(p.z + 6f, origin.z, cellsZ);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    int c = cz * cellsX + cx;
                    for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
                    {
                        int i = cellItems[k];
                        if (CinematicMath.InsideTree(p, pad, positions[i], heights[i], crowns[i], trunks[i])) return true;
                    }
                }
            }

            return false;
        }

        private static int Cell(float v, float o, int count) => Mathf.Clamp((int)Mathf.Floor((v - o) / CellSize), 0, count - 1);
    }
}
