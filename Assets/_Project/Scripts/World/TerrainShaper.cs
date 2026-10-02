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

        /// <summary>Slope (rise over run) of a road cutting's bank: 45°, so cut faces paint as rock.</summary>
        public const float CutSlope = 1f;

        /// <summary>Slope (rise over run) of a road embankment: about 35°.</summary>
        public const float FillSlope = 0.7f;

        /// <summary>Width over which a bank rounds into its constant slope at the road edge, metres.</summary>
        public const float BankRounding = 2f;

        /// <summary>Furthest a cutting or embankment reaches beyond the flat road bed, metres.</summary>
        public const float MaxBankReach = 16f;

        /// <summary>
        /// Benches each road into the terrain at its (smoothed, grade-limited) profile. The road bed (half width + 0.5 m) is
        /// set to the profile height, level across. Beyond it the terrain is left alone unless it rises above a cutting bank
        /// (<see cref="CutSlope"/>, rounded over <see cref="BankRounding"/>) or falls below an embankment (<see cref="FillSlope"/>):
        /// then it is cut down or filled up to that bank. So a road across a hillside gets a cut on the uphill side and a modest
        /// fill below, with no step across the road, and a road on gentle ground barely touches the terrain beside it.
        /// Constraints from all roads are intersected; where two roads disagree (rare: parallel legs of a switchback close
        /// together) the nearest road's banks win.
        /// </summary>
        public static void CarveRoads(float[,] map, WorldGenParams p, IReadOnlyList<Vector3[]> profiles)
        {
            int n = map.GetLength(0);
            float spacing = p.worldSize / (n - 1);
            float half = p.roadWidth * 0.5f;
            float flat = half + 0.5f;
            float outer = flat + MaxBankReach;
            float amplitude = Mathf.Max(1e-4f, p.hillAmplitude);

            var best = new float[n * n];
            var nearestHeight = new float[n * n];
            var lowerBound = new float[n * n];
            var upperBound = new float[n * n];
            for (int i = 0; i < best.Length; i++)
            {
                best[i] = float.PositiveInfinity;
                lowerBound[i] = float.NegativeInfinity;
                upperBound[i] = float.PositiveInfinity;
            }

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
                            if (d >= outer) continue;
                            int index = z * n + x;
                            float y = a.y + (b.y - a.y) * t;
                            float beyond = Mathf.Max(0f, d - flat);
                            float up = y + Bank(beyond, CutSlope);
                            float down = y - Bank(beyond, FillSlope);
                            if (up < upperBound[index]) upperBound[index] = up;
                            if (down > lowerBound[index]) lowerBound[index] = down;
                            if (d < best[index])
                            {
                                best[index] = d;
                                nearestHeight[index] = y;
                            }
                        }
                    }
                }
            }

            for (int z = 0; z < n; z++)
            {
                int row = z * n;
                for (int x = 0; x < n; x++)
                {
                    int index = row + x;
                    if (float.IsPositiveInfinity(best[index])) continue;
                    float lo = lowerBound[index];
                    float hi = upperBound[index];
                    if (lo > hi)
                    {
                        // Conflicting roads: keep the nearest road's banks.
                        float beyond = Mathf.Max(0f, best[index] - flat);
                        lo = nearestHeight[index] - Bank(beyond, FillSlope);
                        hi = nearestHeight[index] + Bank(beyond, CutSlope);
                    }

                    float h = map[z, x] * amplitude;
                    map[z, x] = Mathf.Clamp01(Mathf.Clamp(h, lo, hi) / amplitude);
                }
            }
        }

        /// <summary>Height of a bank <paramref name="beyond"/> metres out from the road bed: rounded at first, then <paramref name="slope"/>.</summary>
        public static float Bank(float beyond, float slope)
        {
            if (beyond <= BankRounding) return slope * beyond * beyond / (2f * BankRounding);
            return slope * (beyond - BankRounding * 0.5f);
        }

        /// <summary>Short names of the splat layers, in layer order (for logs).</summary>
        public static readonly string[] LayerNames = { "lush", "dry", "dirt", "rock", "forest floor", "flowers", "mud", "cobble" };

        /// <summary>Road dirt is solid up to this far inside the road edge (core width = roadWidth − 2 × this), metres.</summary>
        public const float RoadCoreInset = 0.5f;

        /// <summary>Soft dirt shoulder beyond the solid core, metres.</summary>
        public const float RoadSoftEdge = 1.5f;

        /// <summary>Width of the worn verge (dry grass, dirt and mud flecks) beside the road edge, metres.</summary>
        public const float RoadVergeWidth = 3f;

        /// <summary>Slope (rise over run) where rock starts to replace the ground cover: tan 30°.</summary>
        public const float RockSlopeStart = 0.5774f;

        /// <summary>Slope (rise over run) from which the ground is solid rock whatever the biome: tan 40°.</summary>
        public const float RockSlopeFull = 0.8391f;

        /// <summary>
        /// Splat weights at the resolution of <paramref name="roads"/> (its grid may be finer than <paramref name="cover"/>'s;
        /// cover fields are then interpolated), composed by priority: cobblestone in town centres; road dirt (solid core
        /// <see cref="RoadCoreInset"/> inside the edge, <see cref="RoadSoftEdge"/> shoulder, dirt flecks on the verge); rock on
        /// slopes steeper than 30° (solid from 40°, any biome) and high ridgelines; dirt on settlements / tilled fields; mud on
        /// lake beds, shores and in verge flecks; worn dry grass along the verge; forest floor under woods; flower patches in
        /// meadows; the rest is grass split into lush and dry by moisture and altitude. Every texel sums to 1.
        /// </summary>
        public static float[,,] ComputeSplat(WorldGenParams p, RoadDistanceField roads, LandCover cover,
            IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields)
        {
            return ComputeSplat(p, roads, cover, sites, fields, 0f, 0f);
        }

        /// <summary>
        /// <see cref="ComputeSplat(WorldGenParams, RoadDistanceField, LandCover, IReadOnlyList{SettlementSite}, IReadOnlyList{FieldPatch})"/>
        /// for a sub-rectangle of the window: the grid of <paramref name="roads"/> starts at (<paramref name="offsetX"/>,
        /// <paramref name="offsetZ"/>) in the window's local coordinates (those of <paramref name="cover"/>, the sites and fields).
        /// Noise is looked up at window position + <see cref="WorldGenParams.NoiseOriginX"/>, so tiles of an endless world
        /// continue each other.
        /// </summary>
        public static float[,,] ComputeSplat(WorldGenParams p, RoadDistanceField roads, LandCover cover,
            IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields, float offsetX, float offsetZ)
        {
            int res = roads.Resolution;
            float texel = roads.TexelSize;
            var splat = new float[res, res, LayerCount];
            float half = p.roadWidth * 0.5f;
            float core = Mathf.Max(0.5f, half - RoadCoreInset);
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);
            var noise = new GradientNoise(p.seed + 9);
            var flowerNoise = new GradientNoise(p.seed + 14);
            var fleckNoise = new GradientNoise(p.seed + 15);
            var layers = new float[LayerCount];
            float ox = p.NoiseOriginX;
            float oz = p.NoiseOriginZ;

            for (int z = 0; z < res; z++)
            {
                float wz = offsetZ + (z + 0.5f) * texel;
                float nz = wz + oz;
                for (int x = 0; x < res; x++)
                {
                    float wx = offsetX + (x + 0.5f) * texel;
                    float nx = wx + ox;
                    CoverSample cv = cover.Sample(wx, wz);
                    float road = roads[x, z];
                    float fine = 0.5f + 0.5f * noise.Sample(nx * 0.15f, nz * 0.15f);
                    float patch = 0.5f + 0.5f * noise.Sample(nx * 0.05f, nz * 0.05f);

                    // Road: solid dirt core, short soft shoulder, then a worn verge of dry grass with dirt / mud flecks.
                    float roadDirt = 1f - SmoothStep01((road - core) / RoadSoftEdge);
                    float verge = 1f - SmoothStep01((road - half - 1f) / (RoadVergeWidth - 0.5f));
                    float vergeMud = 0f;
                    if (verge > 0f)
                    {
                        float fleck = SmoothStep01((fleckNoise.Sample(nx * 0.45f, nz * 0.45f) - 0.02f) / 0.15f);
                        vergeMud = verge * 0.4f * SmoothStep01((fleckNoise.Sample(nx * 0.3f + 40f, nz * 0.3f) - 0.1f) / 0.15f);
                        roadDirt = Mathf.Max(roadDirt, verge * 0.7f * fleck);
                    }

                    float dirt = 0f;
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

                    float mud = cv.isWater ? 1f : cv.shore * 0.85f * (0.6f + 0.4f * fine);
                    mud = Mathf.Max(mud, vergeMud);

                    // Rock: blends in from 30° (ragged edge from the fine noise), solid from 40° in every biome, plus
                    // bare ridgelines high in the highlands.
                    float rock = cv.slope >= RockSlopeFull ? 1f
                        : SmoothStep01((cv.slope - RockSlopeStart) / (RockSlopeFull - RockSlopeStart) + 0.3f * (fine - 0.5f));
                    float hn = cv.height / amplitude;
                    rock = Mathf.Max(rock, cv.highland * SmoothStep01((hn - 0.8f) / 0.12f) * (0.35f + 0.4f * fine));

                    // Scree under the cliff bands: gravelly dirt with rock flecks (dirt + rock layers, no extra layer).
                    float scree = cv.scree * (0.55f + 0.45f * patch);
                    float screeRock = scree * 0.35f * SmoothStep01((fine - 0.35f) / 0.3f);
                    float screeDirt = scree * 0.6f;

                    // Above the tree line nothing lush: dry grass on ledges and shoulders, no flower patches.
                    float alpine = LandCover.Smooth(p.treeLine - 0.1f, p.treeLine, hn);
                    BiomeWeights w = cv.weights;
                    float floor = SmoothStep01((w.forest - 0.3f) / 0.45f) * 0.9f;
                    float flowers = w.meadow * SmoothStep01((flowerNoise.Fbm(nx * 0.008f, nz * 0.008f, 2) - 0.1f) / 0.25f) * 0.75f * (1f - alpine);
                    float dry = Mathf.Clamp01(1f - LandCover.Smooth(0.42f, 0.66f, cv.moisture + 0.12f * (patch - 0.5f)) + 0.4f * cv.highland);
                    dry = Mathf.Max(dry, alpine);

                    for (int l = 0; l < LayerCount; l++) layers[l] = 0f;
                    float remaining = 1f;
                    Take(CobbleLayer, cobble);
                    Take(DirtLayer, roadDirt);
                    Take(RockLayer, rock);
                    Take(RockLayer, screeRock);
                    Take(DirtLayer, screeDirt);
                    Take(DirtLayer, dirt);
                    Take(MudLayer, mud);
                    Take(DryGrassLayer, verge * (0.55f + 0.3f * fine));
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

        /// <summary>
        /// The splat at <paramref name="resolution"/> texels per side: the same array when it already is, a box average
        /// when the source is an integer multiple finer, otherwise the nearest source texel. Weights still sum to 1.
        /// </summary>
        public static float[,,] Resample(float[,,] splat, int resolution)
        {
            int src = splat.GetLength(0);
            if (src == resolution) return splat;
            int layerCount = splat.GetLength(2);
            var result = new float[resolution, resolution, layerCount];
            int k = src / resolution;
            if (k >= 1 && k * resolution == src)
            {
                float inv = 1f / (k * k);
                for (int z = 0; z < resolution; z++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        for (int dz = 0; dz < k; dz++)
                        {
                            for (int dx = 0; dx < k; dx++)
                            {
                                for (int l = 0; l < layerCount; l++) result[z, x, l] += splat[z * k + dz, x * k + dx, l] * inv;
                            }
                        }
                    }
                }

                return result;
            }

            for (int z = 0; z < resolution; z++)
            {
                int sz = Mathf.Min(src - 1, (int)((z + 0.5f) * src / resolution));
                for (int x = 0; x < resolution; x++)
                {
                    int sx = Mathf.Min(src - 1, (int)((x + 0.5f) * src / resolution));
                    for (int l = 0; l < layerCount; l++) result[z, x, l] = splat[sz, sx, l];
                }
            }

            return result;
        }

        private static void GridBounds(float cx, float cz, float radius, float spacing, int n, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = Mathf.Max(0, Mathf.FloorToInt((cx - radius) / spacing));
            x1 = Mathf.Min(n - 1, Mathf.CeilToInt((cx + radius) / spacing));
            z0 = Mathf.Max(0, Mathf.FloorToInt((cz - radius) / spacing));
            z1 = Mathf.Min(n - 1, Mathf.CeilToInt((cz + radius) / spacing));
        }

        /// <summary>Smoothstep of <paramref name="t"/> clamped to 0..1.</summary>
        public static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
