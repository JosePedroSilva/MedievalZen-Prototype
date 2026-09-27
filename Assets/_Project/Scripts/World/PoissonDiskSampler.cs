using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Bridson Poisson-disk sampling in a rectangle. Reuses its internal buffers between calls so it can be
    /// run once per scatter chunk without per-sample allocations.
    /// </summary>
    public sealed class PoissonDiskSampler
    {
        private const int Attempts = 24;

        private int[] grid = new int[0];
        private readonly List<int> active = new List<int>(256);

        /// <summary>
        /// Appends to <paramref name="results"/> points in [min, max] that are at least <paramref name="radius"/> apart
        /// (only points added by this call are considered).
        /// </summary>
        public void Sample(System.Random rng, Vector2 min, Vector2 max, float radius, List<Vector2> results)
        {
            float width = max.x - min.x;
            float height = max.y - min.y;
            if (width <= 0f || height <= 0f || radius <= 0f) return;

            float cell = radius / 1.41421356f;
            int gw = Mathf.Max(1, Mathf.CeilToInt(width / cell));
            int gh = Mathf.Max(1, Mathf.CeilToInt(height / cell));
            if (grid.Length < gw * gh) grid = new int[gw * gh];
            for (int i = 0; i < gw * gh; i++) grid[i] = -1;

            int first = results.Count;
            active.Clear();
            float radiusSqr = radius * radius;

            Add(new Vector2(min.x + (float)rng.NextDouble() * width, min.y + (float)rng.NextDouble() * height));
            while (active.Count > 0)
            {
                int pick = rng.Next(active.Count);
                Vector2 origin = results[active[pick]];
                bool found = false;
                for (int k = 0; k < Attempts; k++)
                {
                    float angle = (float)(rng.NextDouble() * 6.283185307);
                    float dist = radius * (1f + (float)rng.NextDouble());
                    var candidate = new Vector2(origin.x + Mathf.Cos(angle) * dist, origin.y + Mathf.Sin(angle) * dist);
                    if (candidate.x < min.x || candidate.y < min.y || candidate.x > max.x || candidate.y > max.y) continue;
                    if (!IsFar(candidate)) continue;
                    Add(candidate);
                    found = true;
                    break;
                }

                if (!found)
                {
                    active[pick] = active[active.Count - 1];
                    active.RemoveAt(active.Count - 1);
                }
            }

            void Add(Vector2 point)
            {
                int gx = Mathf.Min(gw - 1, (int)((point.x - min.x) / cell));
                int gy = Mathf.Min(gh - 1, (int)((point.y - min.y) / cell));
                grid[gy * gw + gx] = results.Count - first;
                active.Add(results.Count);
                results.Add(point);
            }

            bool IsFar(Vector2 point)
            {
                int gx = Mathf.Min(gw - 1, (int)((point.x - min.x) / cell));
                int gy = Mathf.Min(gh - 1, (int)((point.y - min.y) / cell));
                int x0 = Mathf.Max(0, gx - 2);
                int x1 = Mathf.Min(gw - 1, gx + 2);
                int y0 = Mathf.Max(0, gy - 2);
                int y1 = Mathf.Min(gh - 1, gy + 2);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int index = grid[y * gw + x];
                        if (index >= 0 && (results[first + index] - point).sqrMagnitude < radiusSqr) return false;
                    }
                }

                return true;
            }
        }
    }
}
