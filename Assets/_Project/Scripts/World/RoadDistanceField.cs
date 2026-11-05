using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Horizontal distance (metres) from every texel centre of a square grid to the nearest road centre line,
    /// clamped to <see cref="MaxDistance"/>. Texel (x, z) centre = ((x + 0.5) * size / res, (z + 0.5) * size / res),
    /// which matches how the terrain alphamap is laid out, so the same grid drives splat painting and scatter.
    /// </summary>
    public sealed class RoadDistanceField
    {
        private readonly float[] distance;

        private RoadDistanceField(int resolution, float worldSize, float maxDistance)
        {
            Resolution = resolution;
            WorldSize = worldSize;
            MaxDistance = maxDistance;
            TexelSize = worldSize / resolution;
            distance = new float[resolution * resolution];
            for (int i = 0; i < distance.Length; i++) distance[i] = maxDistance;
        }

        /// <summary>Texels per side.</summary>
        public int Resolution { get; }

        /// <summary>World side length in metres.</summary>
        public float WorldSize { get; }

        /// <summary>Texel size in metres.</summary>
        public float TexelSize { get; }

        /// <summary>Distances are clamped to this value.</summary>
        public float MaxDistance { get; }

        /// <summary>Distance at texel (x, z).</summary>
        public float this[int x, int z] => distance[z * Resolution + x];

        /// <summary>
        /// Stamps every span of every polyline in <paramref name="roads"/> (y ignored) into a new field.
        /// Cost is proportional to road length * (maxDistance / texel)^2.
        /// </summary>
        public static RoadDistanceField Build(IReadOnlyList<IReadOnlyList<Vector3>> roads, int resolution, float worldSize, float maxDistance)
        {
            var field = new RoadDistanceField(resolution, worldSize, maxDistance);
            float texel = field.TexelSize;
            foreach (IReadOnlyList<Vector3> road in roads)
            {
                for (int i = 1; i < road.Count; i++)
                {
                    Vector3 a = road[i - 1];
                    Vector3 b = road[i];
                    float abx = b.x - a.x;
                    float abz = b.z - a.z;
                    float lengthSqr = abx * abx + abz * abz;
                    int x0 = FastMathf.Max(0, FastMathf.FloorToInt((FastMathf.Min(a.x, b.x) - maxDistance) / texel - 0.5f));
                    int x1 = FastMathf.Min(resolution - 1, Mathf.CeilToInt((FastMathf.Max(a.x, b.x) + maxDistance) / texel - 0.5f));
                    int z0 = FastMathf.Max(0, FastMathf.FloorToInt((FastMathf.Min(a.z, b.z) - maxDistance) / texel - 0.5f));
                    int z1 = FastMathf.Min(resolution - 1, Mathf.CeilToInt((FastMathf.Max(a.z, b.z) + maxDistance) / texel - 0.5f));
                    for (int z = z0; z <= z1; z++)
                    {
                        float pz = (z + 0.5f) * texel;
                        int row = z * resolution;
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = (x + 0.5f) * texel;
                            float t = lengthSqr > 0f ? FastMathf.Clamp01(((px - a.x) * abx + (pz - a.z) * abz) / lengthSqr) : 0f;
                            float dx = px - (a.x + abx * t);
                            float dz = pz - (a.z + abz * t);
                            float d = FastMathf.Sqrt(dx * dx + dz * dz);
                            if (d < field.distance[row + x]) field.distance[row + x] = d;
                        }
                    }
                }
            }

            return field;
        }

        /// <summary>Bilinearly interpolated distance at world (x, z).</summary>
        public float Sample(float x, float z)
        {
            float gx = FastMathf.Clamp(x / TexelSize - 0.5f, 0f, Resolution - 1);
            float gz = FastMathf.Clamp(z / TexelSize - 0.5f, 0f, Resolution - 1);
            int x0 = FastMathf.Min((int)gx, Resolution - 2);
            int z0 = FastMathf.Min((int)gz, Resolution - 2);
            float tx = gx - x0;
            float tz = gz - z0;
            int i = z0 * Resolution + x0;
            float a = distance[i] + (distance[i + 1] - distance[i]) * tx;
            float b = distance[i + Resolution] + (distance[i + Resolution + 1] - distance[i + Resolution]) * tx;
            return a + (b - a) * tz;
        }

        /// <summary>
        /// Conservative "is (x, z) at least <paramref name="clearance"/> from every road": interpolation of a
        /// sampled distance field can overestimate by up to half a texel diagonal, which is subtracted here.
        /// </summary>
        public bool IsClear(float x, float z, float clearance)
        {
            return Sample(x, z) - TexelSize * 0.75f >= clearance;
        }
    }
}
