using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>Kind of named landmark.</summary>
    public enum LandmarkKind
    {
        /// <summary>The highest point of the map.</summary>
        Peak = 0,
        /// <summary>A lake (connected body of water).</summary>
        Lake = 1,
        /// <summary>The col the pass road crosses (position = highest point of the pass road).</summary>
        Pass = 2,
        /// <summary>A lookout beside the pass road with a view over the lowland.</summary>
        Viewpoint = 3,
    }

    /// <summary>A named landmark for the map (the parchment map draws a symbol and a label for each).</summary>
    [System.Serializable]
    public struct Landmark
    {
        /// <summary>Display name.</summary>
        public string name;
        /// <summary>Peak, lake, pass or viewpoint.</summary>
        public LandmarkKind kind;
        /// <summary>Peak summit; lake centroid at water height; where the pass road crosses the col; the viewpoint clearing.</summary>
        public Vector3 position;
        /// <summary>Lake: radius of a circle with the lake's area. Others: 0.</summary>
        public float radius;

        /// <inheritdoc />
        public override string ToString() => $"{kind} '{name}' at ({position.x:0}, {position.y:0.0}, {position.z:0}) r {radius:0}";
    }

    /// <summary>
    /// Finds the tallest peak and every lake larger than <see cref="MinLakeArea"/>. (The pass and viewpoint landmarks are added
    /// with the pass features, <see cref="ScatterPlanner.PlacePassFeatures"/>.)
    /// </summary>
    public static class LandmarkFinder
    {
        /// <summary>Smallest lake that gets a name, in square metres.</summary>
        public const float MinLakeArea = 2500f;

        private const float Cell = 10f;

        /// <summary>Returns the peak followed by lakes (largest first).</summary>
        public static List<Landmark> Find(Relief relief, SettlementNames names)
        {
            WorldGenParams p = relief.Params;
            float[,] map = relief.Map;
            var result = new List<Landmark>();

            int n = map.GetLength(0);
            float spacing = p.worldSize / (n - 1);
            int bestX = 0;
            int bestZ = 0;
            float best = float.NegativeInfinity;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (map[z, x] > best)
                    {
                        best = map[z, x];
                        bestX = x;
                        bestZ = z;
                    }
                }
            }

            result.Add(new Landmark
            {
                name = names.NextPeak(),
                kind = LandmarkKind.Peak,
                position = new Vector3(bestX * spacing, best * p.hillAmplitude, bestZ * spacing),
            });

            if (!relief.HasWater) return result;

            int w = Mathf.Max(1, Mathf.CeilToInt(p.worldSize / Cell));
            float cs = p.worldSize / w;
            var wet = new bool[w * w];
            for (int z = 0; z < w; z++)
            {
                for (int x = 0; x < w; x++) wet[z * w + x] = relief.IsWater((x + 0.5f) * cs, (z + 0.5f) * cs);
            }

            var label = new int[w * w];
            var stack = new Stack<int>();
            var lakes = new List<(float area, Vector2 centre)>();
            for (int start = 0; start < wet.Length; start++)
            {
                if (!wet[start] || label[start] != 0) continue;
                int id = lakes.Count + 1;
                int count = 0;
                double sx = 0.0;
                double sz = 0.0;
                label[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    int cx = c % w;
                    int cz = c / w;
                    count++;
                    sx += cx;
                    sz += cz;
                    if (cx > 0) Visit(c - 1);
                    if (cx < w - 1) Visit(c + 1);
                    if (cz > 0) Visit(c - w);
                    if (cz < w - 1) Visit(c + w);
                }

                lakes.Add((count * cs * cs, new Vector2((float)((sx / count + 0.5) * cs), (float)((sz / count + 0.5) * cs))));

                void Visit(int cell)
                {
                    if (!wet[cell] || label[cell] != 0) return;
                    label[cell] = id;
                    stack.Push(cell);
                }
            }

            lakes.Sort((a, b) => b.area.CompareTo(a.area));
            foreach (var lake in lakes)
            {
                if (lake.area < MinLakeArea) break;
                result.Add(new Landmark
                {
                    name = names.NextLake(),
                    kind = LandmarkKind.Lake,
                    position = new Vector3(lake.centre.x, relief.WaterHeight, lake.centre.y),
                    radius = Mathf.Sqrt(lake.area / Mathf.PI),
                });
            }

            return result;
        }
    }
}
