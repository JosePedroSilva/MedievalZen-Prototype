using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>Spatial hash of footprint circles (XZ) so placements do not overlap each other.</summary>
    public sealed class OccupancyMap
    {
        private const float CellSize = 16f;

        private readonly int width;
        private readonly List<int>[] cells;
        private readonly List<Vector3> circles = new List<Vector3>(); // x, radius, z

        /// <summary>Creates an empty map for a square world of <paramref name="worldSize"/> metres.</summary>
        public OccupancyMap(float worldSize)
        {
            width = Mathf.Max(1, Mathf.CeilToInt(worldSize / CellSize));
            cells = new List<int>[width * width];
        }

        /// <summary>Number of circles added.</summary>
        public int Count => circles.Count;

        /// <summary>Circle <paramref name="index"/> in insertion order as (x, radius, z).</summary>
        public Vector3 Circle(int index) => circles[index];

        /// <summary>Adds a circle.</summary>
        public void Add(Vector3 center, float radius)
        {
            int index = circles.Count;
            circles.Add(new Vector3(center.x, radius, center.z));
            Range(center.x, center.z, radius, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * width + x;
                    (cells[c] ??= new List<int>(4)).Add(index);
                }
            }
        }

        /// <summary>True when a circle at (x, z) with <paramref name="radius"/> overlaps any stored circle.</summary>
        public bool Overlaps(float x, float z, float radius)
        {
            Range(x, z, radius, out int x0, out int x1, out int z0, out int z1);
            for (int gz = z0; gz <= z1; gz++)
            {
                for (int gx = x0; gx <= x1; gx++)
                {
                    List<int> list = cells[gz * width + gx];
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Vector3 c = circles[list[i]];
                        float dx = c.x - x;
                        float dz = c.z - z;
                        float r = c.y + radius;
                        if (dx * dx + dz * dz < r * r) return true;
                    }
                }
            }

            return false;
        }

        private void Range(float x, float z, float radius, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((x - radius) / CellSize), 0, width - 1);
            x1 = Mathf.Clamp(Mathf.FloorToInt((x + radius) / CellSize), 0, width - 1);
            z0 = Mathf.Clamp(Mathf.FloorToInt((z - radius) / CellSize), 0, width - 1);
            z1 = Mathf.Clamp(Mathf.FloorToInt((z + radius) / CellSize), 0, width - 1);
        }
    }
}
