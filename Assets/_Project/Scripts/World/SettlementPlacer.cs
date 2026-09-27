using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Picks town and village sites: Poisson-disk candidates scored by local flatness and distance from the
    /// map edge, then a greedy best-first pick that keeps every pair at least
    /// <see cref="WorldGenParams.minSettlementSpacing"/> apart (towns first, so they get the flattest ground).
    /// Inns are added later by <see cref="RoadPlanner"/>.
    /// </summary>
    public static class SettlementPlacer
    {
        /// <summary>Minimum distance from a settlement centre to the map edge, in metres.</summary>
        public const float EdgeMargin = 150f;

        /// <summary>Radius over which flatness (height standard deviation) is measured, in metres.</summary>
        public const float FlatnessRadius = 120f;

        /// <summary>Metres of height standard deviation traded per settlement-spacing of distance to the nearest pick.</summary>
        private const float SpreadWeight = 1.5f;

        /// <summary>Places towns and villages for <paramref name="settings"/>.</summary>
        public static List<SettlementSite> Place(WorldSettings settings, float[,] heightmap)
        {
            var p = WorldGenParams.From(settings);
            return Place(p, Relief.Dry(p, heightmap), new SettlementNames(p.seed + 7));
        }

        /// <summary>Places towns and villages; names come from a <see cref="SettlementNames"/> seeded with seed + 7.</summary>
        public static List<SettlementSite> Place(WorldGenParams p, float[,] heightmap)
        {
            return Place(p, Relief.Dry(p, heightmap), new SettlementNames(p.seed + 7));
        }

        /// <summary>Places towns and villages for a generated relief (avoids lakes); names seeded with seed + 7.</summary>
        public static List<SettlementSite> Place(WorldGenParams p, Relief relief)
        {
            return Place(p, relief, new SettlementNames(p.seed + 7));
        }

        /// <summary>
        /// Places towns and villages using <paramref name="names"/>. Sites stay clear of water; when there are two or
        /// more villages the last one is placed in the highlands (the flattest spot there) if one exists.
        /// </summary>
        public static List<SettlementSite> Place(WorldGenParams p, Relief relief, SettlementNames names)
        {
            float[,] heightmap = relief.Map;
            var rng = new System.Random(p.seed + 2);
            float size = p.worldSize;
            float margin = Mathf.Min(EdgeMargin, size * 0.25f);
            float spacing = Mathf.Max(10f, p.minSettlementSpacing);

            // Dense candidates (a third of the settlement spacing) give the greedy pick room to find flat ground;
            // any subset it keeps still honours the full spacing.
            float candidateSpacing = Mathf.Max(20f, spacing * 0.33f);
            var candidates = new List<Vector2>();
            new PoissonDiskSampler().Sample(rng, new Vector2(margin, margin), new Vector2(size - margin, size - margin), candidateSpacing, candidates);

            var scores = new float[candidates.Count];
            var highland = new float[candidates.Count];
            var order = new int[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2 c = candidates[i];
                float edge = Mathf.Min(Mathf.Min(c.x, c.y), Mathf.Min(size - c.x, size - c.y));
                float edgePenalty = Mathf.Max(0f, 300f - edge) * 0.01f;
                scores[i] = WetWithin(relief, c, SettlementSite.TownRadius + 30f)
                    ? float.PositiveInfinity
                    : HeightStdDev(heightmap, p, c, FlatnessRadius) + edgePenalty;
                highland[i] = relief.Mask.Evaluate(c.x, c.y);
                order[i] = i;
            }

            System.Array.Sort(order, (a, b) =>
            {
                int cmp = scores[a].CompareTo(scores[b]);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            var chosen = new List<SettlementSite>(p.townCount + p.villageCount);
            var taken = new bool[candidates.Count];
            Pick(SettlementType.Town, p.townCount);
            bool hillVillage = p.villageCount >= 2;
            Pick(SettlementType.Village, hillVillage ? p.villageCount - 1 : p.villageCount);
            if (hillVillage)
            {
                int before = chosen.Count;
                Pick(SettlementType.Village, 1, 0.45f);
                if (chosen.Count == before) Pick(SettlementType.Village, 1);
            }

            return chosen;

            // Greedy: each pick minimises flatness + edge penalty - a spread bonus that grows with the distance to
            // the nearest settlement already chosen (capped), so settlements cover the map instead of clustering.
            void Pick(SettlementType type, int count, float minHighland = -1f)
            {
                for (int placed = 0; placed < count; placed++)
                {
                    int best = -1;
                    float bestScore = float.PositiveInfinity;
                    for (int k = 0; k < order.Length; k++)
                    {
                        int i = order[k];
                        if (taken[i] || float.IsPositiveInfinity(scores[i])) continue;
                        if (minHighland >= 0f ? highland[i] < minHighland : highland[i] > 0.6f) continue;
                        Vector2 c = candidates[i];
                        float nearest = float.PositiveInfinity;
                        for (int j = 0; j < chosen.Count; j++)
                        {
                            Vector3 other = chosen[j].Position;
                            float dx = other.x - c.x;
                            float dz = other.z - c.y;
                            nearest = Mathf.Min(nearest, dx * dx + dz * dz);
                        }

                        nearest = Mathf.Sqrt(nearest);
                        if (nearest < spacing) continue;
                        float spread = chosen.Count == 0 ? 0f : SpreadWeight * Mathf.Min(nearest / spacing, 2.5f);
                        float score = scores[i] - spread;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = i;
                        }
                    }

                    if (best < 0) return;
                    taken[best] = true;
                    Vector2 site = candidates[best];
                    float y = HeightmapGenerator.SampleHeight(heightmap, p, site.x, site.y);
                    chosen.Add(new SettlementSite(names.NextPlace(), type, new Vector3(site.x, y, site.y)));
                }
            }
        }

        /// <summary>True when any point within <paramref name="radius"/> of <paramref name="center"/> is (nearly) under water.</summary>
        public static bool WetWithin(Relief relief, Vector2 center, float radius)
        {
            if (!relief.HasWater) return false;
            const float Step = 15f;
            int steps = Mathf.CeilToInt(radius / Step);
            for (int iz = -steps; iz <= steps; iz++)
            {
                for (int ix = -steps; ix <= steps; ix++)
                {
                    float ox = ix * Step;
                    float oz = iz * Step;
                    if (ox * ox + oz * oz > radius * radius) continue;
                    if (relief.IsWater(center.x + ox, center.y + oz, 1f)) return true;
                }
            }

            return false;
        }

        /// <summary>Standard deviation of terrain height (metres) within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static float HeightStdDev(float[,] heightmap, WorldGenParams p, Vector2 center, float radius)
        {
            const float Step = 15f;
            double sum = 0.0;
            double sumSqr = 0.0;
            int count = 0;
            int steps = Mathf.CeilToInt(radius / Step);
            for (int iz = -steps; iz <= steps; iz++)
            {
                for (int ix = -steps; ix <= steps; ix++)
                {
                    float ox = ix * Step;
                    float oz = iz * Step;
                    if (ox * ox + oz * oz > radius * radius) continue;
                    double h = HeightmapGenerator.SampleHeight(heightmap, p, center.x + ox, center.y + oz);
                    sum += h;
                    sumSqr += h * h;
                    count++;
                }
            }

            if (count == 0) return 0f;
            double mean = sum / count;
            return (float)System.Math.Sqrt(System.Math.Max(0.0, sumSqr / count - mean * mean));
        }
    }
}
