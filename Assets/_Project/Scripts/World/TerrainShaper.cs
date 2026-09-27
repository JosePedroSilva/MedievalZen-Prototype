using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Modifies the heightmap in place (settlement discs, road beds) and paints the splat weights.
    /// Splat layers (see <see cref="SyntyCatalog.TerrainLayers"/>): lush grass, dry grass, dirt, rock, forest floor,
    /// flower meadow, mud, cobblestone. Splat layout matches <c>TerrainData.SetAlphamaps</c>: <c>splat[z, x, layer]</c>.
    /// </summary>
    public static class TerrainShaper
    {
        /// <summary>Splat layer: lush lowland grass.</summary>
        public const int LushGrassLayer = 0;

        /// <summary>Splat layer: dry / upland grass.</summary>
        public const int DryGrassLayer = 1;

        /// <summary>Splat layer: road, settlement and field dirt.</summary>
        public const int DirtLayer = 2;

        /// <summary>Splat layer: rock on steep slopes and ridgelines.</summary>
        public const int RockLayer = 3;

        /// <summary>Splat layer: forest floor.</summary>
        public const int ForestFloorLayer = 4;

        /// <summary>Splat layer: flowery meadow patches.</summary>
        public const int FlowerLayer = 5;

        /// <summary>Splat layer: mud on lake shores and beds.</summary>
        public const int MudLayer = 6;

        /// <summary>Splat layer: cobblestone in town centres.</summary>
        public const int CobbleLayer = 7;

        /// <summary>Number of splat layers.</summary>
        public const int LayerCount = 8;

        /// <summary>Width of the blended shoulder on each side of the road surface, in metres.</summary>
        public const float RoadShoulder = 3f;

        /// <summary>Soft edge around a flattened settlement disc, in metres.</summary>
        public const float SettlementBlend = 25f;

        /// <summary>
        /// Flattens a disc under each settlement to the mean height inside it, with a smooth
        /// <see cref="SettlementBlend"/> edge; writes the resulting height into each site's position.
        /// </summary>
        public static void FlattenSettlements(float[,] map, WorldGenParams p, IReadOnlyList<SettlementSite> sites)
        {
            int n = map.GetLength(0);
            float spacing = p.worldSize / (n - 1);
            foreach (SettlementSite site in sites)
            {
                Vector3 c = site.Position;
                float inner = site.Radius * 0.85f;
                float outer = site.Radius + SettlementBlend;
                GridBounds(c.x, c.z, outer, spacing, n, out int x0, out int x1, out int z0, out int z1);

                double sum = 0.0;
                int count = 0;
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x * spacing - c.x;
                        float dz = z * spacing - c.z;
                        if (dx * dx + dz * dz > site.Radius * site.Radius) continue;
                        sum += map[z, x];
                        count++;
                    }
                }

                float mean = count > 0 ? (float)(sum / count) : HeightmapGenerator.SampleNormalized(map, p.worldSize, c.x, c.z);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x * spacing - c.x;
                        float dz = z * spacing - c.z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d >= outer) continue;
                        float w = d <= inner ? 1f : 1f - SmoothStep01((d - inner) / (outer - inner));
                        map[z, x] = Mathf.Lerp(map[z, x], mean, w);
                    }
                }

                site.Position = new Vector3(c.x, mean * p.hillAmplitude, c.z);
            }
        }

        /// <summary>
        /// Blends terrain toward each road's (already smoothed) elevation profile: full weight across the road
        /// surface, smooth falloff over <see cref="RoadShoulder"/> metres. Where roads overlap the nearest wins.
        /// </summary>
        public static void CarveRoads(float[,] map, WorldGenParams p, IReadOnlyList<Vector3[]> profiles)
        {
            int n = map.GetLength(0);
            float spacing = p.worldSize / (n - 1);
            float half = p.roadWidth * 0.5f;
            float flat = half + 0.5f;
            float outer = half + RoadShoulder;
            float amplitude = Mathf.Max(1e-4f, p.hillAmplitude);

            var best = new float[n * n];
            var target = new float[n * n];
            for (int i = 0; i < best.Length; i++) best[i] = float.PositiveInfinity;

            foreach (Vector3[] road in profiles)
            {
                for (int i = 1; i < road.Length; i++)
                {
                    Vector3 a = road[i - 1];
                    Vector3 b = road[i];
                    float abx = b.x - a.x;
                    float abz = b.z - a.z;
                    float lengthSqr = abx * abx + abz * abz;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - outer) / spacing));
                    int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + outer) / spacing));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - outer) / spacing));
                    int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + outer) / spacing));
                    for (int z = z0; z <= z1; z++)
                    {
                        float pz = z * spacing;
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x * spacing;
                            float t = lengthSqr > 0f ? Mathf.Clamp01(((px - a.x) * abx + (pz - a.z) * abz) / lengthSqr) : 0f;
                            float dx = px - (a.x + abx * t);
                            float dz = pz - (a.z + abz * t);
                            float d = Mathf.Sqrt(dx * dx + dz * dz);
                            int index = z * n + x;
                            if (d >= outer || d >= best[index]) continue;
                            best[index] = d;
                            target[index] = (a.y + (b.y - a.y) * t) / amplitude;
                        }
                    }
                }
            }

            for (int z = 0; z < n; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    float d = best[row + x];
                    if (d >= outer) continue;
                    float w = d <= flat ? 1f : 1f - SmoothStep01((d - flat) / (outer - flat));
                    map[z, x] = Mathf.Clamp01(Mathf.Lerp(map[z, x], target[row + x], w));
                }
            }
        }

        /// <summary>
        /// Splat weights, composed by priority: cobblestone in town centres, dirt on roads (soft edge from the
        /// distance field) / settlements / tilled fields, mud on lake beds and shores, rock on steep slopes and
        /// high ridgelines, forest floor under woods, flower patches in meadows; the rest is grass split into lush
        /// and dry by moisture and altitude. Every texel sums to 1.
        /// </summary>
        public static float[,,] ComputeSplat(WorldGenParams p, RoadDistanceField roads, LandCover cover,
            IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields)
        {
            int res = roads.Resolution;
            float texel = roads.TexelSize;
            var splat = new float[res, res, LayerCount];
            float half = p.roadWidth * 0.5f;
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);
            var noise = new GradientNoise(p.seed + 9);
            var flowerNoise = new GradientNoise(p.seed + 14);
            var layers = new float[LayerCount];

            for (int z = 0; z < res; z++)
            {
                float wz = (z + 0.5f) * texel;
                for (int x = 0; x < res; x++)
                {
                    float wx = (x + 0.5f) * texel;
                    int i = z * res + x;
                    float road = roads[x, z];
                    float fine = 0.5f + 0.5f * noise.Sample(wx * 0.15f, wz * 0.15f);
                    float patch = 0.5f + 0.5f * noise.Sample(wx * 0.05f, wz * 0.05f);

                    float dirt = 1f - SmoothStep01((road - (half - 0.75f)) / 2f);
                    float verge = 1f - SmoothStep01((road - half) / 3f);
                    dirt = Mathf.Max(dirt, verge * 0.3f * fine);

                    float cobble = 0f;
                    for (int s = 0; s < sites.Count; s++)
                    {
                        Vector3 c = sites[s].Position;
                        float dx = wx - c.x;
                        float dz = wz - c.z;
                        float r = sites[s].Radius;
                        float dSqr = dx * dx + dz * dz;
                        if (dSqr >= r * r) continue;
                        float d = Mathf.Sqrt(dSqr);
                        float inside = 1f - SmoothStep01((d - r * 0.55f) / (r * 0.4f));
                        dirt = Mathf.Max(dirt, inside * Mathf.Lerp(0.4f, 0.85f, patch));
                        if (sites[s].Type == SettlementType.Town)
                        {
                            cobble = Mathf.Max(cobble, (1f - SmoothStep01((d - 14f) / 6f)) * 0.9f);
                        }
                    }

                    for (int f = 0; f < fields.Count; f++)
                    {
                        if (!fields[f].Contains(wx, wz, 0f)) continue;
                        dirt = Mathf.Max(dirt, fields[f].crop == PropRole.CropField ? 0.7f : 0.4f);
                        break;
                    }

                    float mud = 0f;
                    if (cover.IsWater(i)) mud = 1f;
                    else mud = cover.Shore[i] * 0.85f * (0.6f + 0.4f * fine);

                    float slope = cover.Slope[i];
                    float hn = cover.Height[i] / amplitude;
                    float rock = SmoothStep01((slope - 0.55f) / 0.3f);
                    rock = Mathf.Max(rock, cover.Highland[i] * SmoothStep01((hn - 0.8f) / 0.12f) * (0.35f + 0.4f * fine));

                    BiomeWeights w = cover.Weights(i);
                    float floor = SmoothStep01((w.forest - 0.3f) / 0.45f) * 0.9f;
                    float flowers = w.meadow * SmoothStep01((flowerNoise.Fbm(wx * 0.008f, wz * 0.008f, 2) - 0.1f) / 0.25f) * 0.75f;
                    float dry = Mathf.Clamp01(1f - LandCover.Smooth(0.42f, 0.66f, cover.Moisture[i] + 0.12f * (patch - 0.5f)) + 0.4f * cover.Highland[i]);

                    for (int l = 0; l < LayerCount; l++) layers[l] = 0f;
                    float remaining = 1f;
                    Take(CobbleLayer, cobble);
                    Take(DirtLayer, dirt);
                    Take(MudLayer, mud);
                    Take(RockLayer, rock);
                    Take(ForestFloorLayer, floor);
                    Take(FlowerLayer, flowers);
                    layers[DryGrassLayer] += remaining * dry;
                    layers[LushGrassLayer] += remaining * (1f - dry);
                    for (int l = 0; l < LayerCount; l++) splat[z, x, l] = layers[l];

                    void Take(int layer, float amount)
                    {
                        float a = Mathf.Clamp01(amount) * remaining;
                        layers[layer] += a;
                        remaining -= a;
                    }
                }
            }

            return splat;
        }

        /// <summary>Distance fields need the road centre lines; this extracts them from a network.</summary>
        public static List<IReadOnlyList<Vector3>> CenterLines(RoadNetwork network)
        {
            var lines = new List<IReadOnlyList<Vector3>>(network.Segments.Count);
            foreach (RoadSegment segment in network.Segments) lines.Add(segment.Points);
            return lines;
        }

        private static void GridBounds(float cx, float cz, float radius, float spacing, int n, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = Mathf.Max(0, Mathf.FloorToInt((cx - radius) / spacing));
            x1 = Mathf.Min(n - 1, Mathf.CeilToInt((cx + radius) / spacing));
            z0 = Mathf.Max(0, Mathf.FloorToInt((cz - radius) / spacing));
            z1 = Mathf.Min(n - 1, Mathf.CeilToInt((cz + radius) / spacing));
        }

        internal static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
