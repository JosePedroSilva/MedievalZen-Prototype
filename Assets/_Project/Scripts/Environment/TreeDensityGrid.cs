using System;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Coarse grid of tree counts over a square area, sampled as trees per 100 m² (bilinear). Built once from a Terrain's
    /// tree instances; used by the ambient FX spawner (meadow vs forest) and the map renderer (forest stipple).
    /// </summary>
    public sealed class TreeDensityGrid
    {
        private readonly float[] counts;
        private readonly float originX;
        private readonly float originZ;

        /// <summary>Creates an empty grid covering [origin, origin + size] on X/Z with square cells of <paramref name="cellSize"/> metres.</summary>
        public TreeDensityGrid(float originX, float originZ, float size, float cellSize)
        {
            if (size <= 0f) throw new ArgumentOutOfRangeException(nameof(size));
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            this.originX = originX;
            this.originZ = originZ;
            Size = size;
            Resolution = Math.Max(1, (int)Math.Ceiling(size / cellSize));
            CellSize = size / Resolution;
            counts = new float[Resolution * Resolution];
        }

        /// <summary>Side length of the covered square in metres.</summary>
        public float Size { get; }

        /// <summary>Cells per side.</summary>
        public int Resolution { get; }

        /// <summary>Actual cell size in metres.</summary>
        public float CellSize { get; }

        /// <summary>Largest density of any cell, in trees per 100 m².</summary>
        public float MaxDensity { get; private set; }

        /// <summary>Counts one tree at world (<paramref name="x"/>, <paramref name="z"/>); outside points are ignored.</summary>
        public void Add(float x, float z)
        {
            int cx = (int)Math.Floor((x - originX) / CellSize);
            int cz = (int)Math.Floor((z - originZ) / CellSize);
            if (cx < 0 || cz < 0 || cx >= Resolution || cz >= Resolution) return;
            float value = ++counts[cz * Resolution + cx];
            MaxDensity = Math.Max(MaxDensity, value * 100f / (CellSize * CellSize));
        }

        /// <summary>Trees per 100 m² in the cell containing (x, z), or 0 outside the grid.</summary>
        public float CellDensity(float x, float z)
        {
            int cx = (int)Math.Floor((x - originX) / CellSize);
            int cz = (int)Math.Floor((z - originZ) / CellSize);
            if (cx < 0 || cz < 0 || cx >= Resolution || cz >= Resolution) return 0f;
            return counts[cz * Resolution + cx] * 100f / (CellSize * CellSize);
        }

        /// <summary>Bilinearly interpolated trees per 100 m² at (x, z) (cell centres carry the cell values).</summary>
        public float Sample(float x, float z)
        {
            float gx = (x - originX) / CellSize - 0.5f;
            float gz = (z - originZ) / CellSize - 0.5f;
            int x0 = (int)Math.Floor(gx);
            int z0 = (int)Math.Floor(gz);
            float tx = gx - x0;
            float tz = gz - z0;
            float a = Get(x0, z0) + (Get(x0 + 1, z0) - Get(x0, z0)) * tx;
            float b = Get(x0, z0 + 1) + (Get(x0 + 1, z0 + 1) - Get(x0, z0 + 1)) * tx;
            return (a + (b - a) * tz) * 100f / (CellSize * CellSize);
        }

        private float Get(int cx, int cz)
        {
            cx = Math.Max(0, Math.Min(Resolution - 1, cx));
            cz = Math.Max(0, Math.Min(Resolution - 1, cz));
            return counts[cz * Resolution + cx];
        }

        /// <summary>Builds a grid from <paramref name="terrain"/>'s tree instances (null terrain → empty 1-cell grid).</summary>
        public static TreeDensityGrid FromTerrain(Terrain terrain, float cellSize)
        {
            if (terrain == null || terrain.terrainData == null) return new TreeDensityGrid(0f, 0f, 1f, 1f);
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.GetPosition();
            Vector3 size = data.size;
            var grid = new TreeDensityGrid(origin.x, origin.z, Mathf.Max(size.x, size.z), cellSize);
            TreeInstance[] trees = data.treeInstances;
            for (int i = 0; i < trees.Length; i++)
            {
                Vector3 p = trees[i].position;
                grid.Add(origin.x + p.x * size.x, origin.z + p.z * size.z);
            }

            return grid;
        }
    }
}
