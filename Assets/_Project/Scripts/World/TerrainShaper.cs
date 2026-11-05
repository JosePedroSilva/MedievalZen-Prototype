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

        /// <summary>
        /// Endless road only: grey mountain rock on the pass ranges' faces (first-party cool rock layer; the finite world and the
        /// lowland outcrops keep <see cref="RockLayer"/>).
        /// </summary>
        public const int MountainRockLayer = 8;

        /// <summary>Endless road only: scree on the talus fans, the aprons at the foot of the walls and the gully floors of the passes.</summary>
        public const int ScreeLayer = 9;

        /// <summary>Endless road only: heath and dry upland turf on the ledges and lower slopes of the passes.</summary>
        public const int HeathLayer = 10;

        /// <summary>
        /// Endless road only (2026-10-06): the farmland's ripe-grain gold (the first of the three field channels, painted only by
        /// <c>FieldPatchwork.Paint</c>; the tiles that use them take the larger terrain layer set).
        /// </summary>
        public const int FieldGoldLayer = 11;

        /// <summary>Endless road only: the rapeseed yellow of a flowering field.</summary>
        public const int FieldYellowLayer = 12;

        /// <summary>Endless road only: the fresh blue-green of a young crop (distinct from the lawn's yellow-green).</summary>
        public const int FieldGreenLayer = 13;

        /// <summary>
        /// Endless road only (2026-10-06): the woods' leaf litter under the crowns (first-party tinted Grass_Leaves_Heavy; painted only by
        /// <c>Planting.PaintWoodFloor</c>). A tile that paints it draws it in the cobblestone's slot (<c>EndlessLayerSets</c>): no extra pass.
        /// </summary>
        public const int LeafLitterLayer = 14;

        /// <summary>Number of splat channels of the endless road's tiles (the eight shared layers, the three mountain layers, the three field layers, the leaf litter).</summary>
        public const int EndlessLayerCount = 15;

        /// <summary>Short names of the endless road's splat channels (logs).</summary>
        public static readonly string[] EndlessLayerNames = { "lush", "dry", "dirt", "rock", "forest floor", "flowers", "mud", "cobble", "mountain rock", "scree", "heath", "field gold", "field yellow", "field green", "leaf litter" };

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
                        float d = FastMathf.Sqrt(dx * dx + dz * dz);
                        if (d >= outer) continue;
                        float w = d <= inner ? 1f : 1f - SmoothStep01((d - inner) / (outer - inner));
                        map[z, x] = FastMathf.Lerp(map[z, x], mean, w);
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
            float amplitude = FastMathf.Max(1e-4f, p.hillAmplitude);

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
                    int x0 = FastMathf.Max(0, FastMathf.FloorToInt((FastMathf.Min(a.x, b.x) - outer) / spacing));
                    int x1 = FastMathf.Min(n - 1, Mathf.CeilToInt((FastMathf.Max(a.x, b.x) + outer) / spacing));
                    int z0 = FastMathf.Max(0, FastMathf.FloorToInt((FastMathf.Min(a.z, b.z) - outer) / spacing));
                    int z1 = FastMathf.Min(n - 1, Mathf.CeilToInt((FastMathf.Max(a.z, b.z) + outer) / spacing));
                    for (int z = z0; z <= z1; z++)
                    {
                        float pz = z * spacing;
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x * spacing;
                            float t = lengthSqr > 0f ? FastMathf.Clamp01(((px - a.x) * abx + (pz - a.z) * abz) / lengthSqr) : 0f;
                            float dx = px - (a.x + abx * t);
                            float dz = pz - (a.z + abz * t);
                            float d = FastMathf.Sqrt(dx * dx + dz * dz);
                            if (d >= outer) continue;
                            int index = z * n + x;
                            float y = a.y + (b.y - a.y) * t;
                            float beyond = FastMathf.Max(0f, d - flat);
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
                        float beyond = FastMathf.Max(0f, best[index] - flat);
                        lo = nearestHeight[index] - Bank(beyond, FillSlope);
                        hi = nearestHeight[index] + Bank(beyond, CutSlope);
                    }

                    float h = map[z, x] * amplitude;
                    map[z, x] = FastMathf.Clamp01(FastMathf.Clamp(h, lo, hi) / amplitude);
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
        /// Endless road, open hills: slope (rise over run) from which the ground is solid rock: tan 50° (the finite world and the
        /// pass ranges keep <see cref="RockSlopeFull"/>). Between 30° and 50° the rock comes as outcrops on a minority of the face.
        /// </summary>
        public const float OutcropSlopeFull = 1.1918f;

        /// <summary>Endless road: highland weight below which hills get rock as outcrops (<see cref="OutcropRock"/>), fully.</summary>
        public const float OutcropHighlandStart = 0.45f;

        /// <summary>Endless road: highland weight from which the pass ranges keep the plain 30°–40° rock blend.</summary>
        public const float OutcropHighlandEnd = 0.6f;

        /// <summary>Endless road: frequency (per metre) of the outcrop noise: patches of about 20 m.</summary>
        public const float OutcropFrequency = 0.045f;

        /// <summary>Endless road: share of 30°–40° ground that is rock, as a power of the position in the band (1 = linear).</summary>
        public const float OutcropCoverageExponent = 2f;

        /// <summary>
        /// Endless road, open hills: the rock weight of ground of slope <paramref name="slope"/> (rise over run) between
        /// <see cref="RockSlopeStart"/> and <see cref="OutcropSlopeFull"/>, as outcrops: <paramref name="noise"/> (a two-octave fbm,
        /// about normal with s.d. 0.16) is turned into a near-uniform 0..1 value and the ground is rock where it exceeds 1 − coverage,
        /// so the rock share of the band rises from 0 at 30° as t^<see cref="OutcropCoverageExponent"/> (4 % at 35°, 18 % at 40°,
        /// 47 % at 45°) to solid at 50°, in patches with a ragged (<paramref name="fine"/>) edge rather than a uniform blend, so a
        /// lowland hill's steep face is grass with crags and scars, not a bare mound (2026-10-02: solid from 40° made faces of up
        /// to 8,000 m² bare in the downs).
        /// </summary>
        public static float OutcropRock(float slope, float noise, float fine)
        {
            if (slope >= OutcropSlopeFull) return 1f;
            float t = (slope - RockSlopeStart) / (OutcropSlopeFull - RockSlopeStart);
            if (t <= 0f) return 0f;
            float coverage = Mathf.Pow(t, OutcropCoverageExponent);
            float u = 1f / (1f + Mathf.Exp(-10.6f * (noise + 0.04f * (fine - 0.5f))));
            return SmoothStep01((u - (1f - coverage)) / 0.1f + 0.5f);
        }

        /// <summary>
        /// Endless road: the broad tonal drifts of the sward, as the standard deviation of the shift they give the dry-grass share:
        /// swathes of about 150 m, lusher or drier (in every region, the dry downs and the lush lake shores too), so open country
        /// does not read as one flat green beyond the grass details. Large-scale only (no patches of a few metres: clumps that
        /// stood out from the ground made the blotchy look of 2026-10-02; the grass details follow the paint, see dryAffinity).
        /// </summary>
        public const float DriftAmplitude = 0.3f;

        /// <summary>Endless road: frequency (per metre) of the tonal drifts.</summary>
        public const float DriftFrequency = 0.0065f;

        /// <summary>Endless road: standard deviation of the drifts' shift of the flower-meadow field (the lusher swathes flower more).</summary>
        public const float DriftFlowers = 0.06f;

        /// <summary>Endless road: flower-meadow paint starts at this value of its field (0.1 in the finite world): about twice the area.</summary>
        public const float EndlessFlowerStart = 0.02f;

        /// <summary>Endless road, pass ranges: share of the turf on level ground (the valley floor) that is heath (the rest upland grass).</summary>
        public const float HeathFloorShare = 0.45f;

        /// <summary>Endless road, pass ranges: frequency (per metre) of the heath patches on the floor and the lower slopes.</summary>
        public const float HeathPatchFrequency = 1f / 60f;

        /// <summary>Endless road, pass ranges: how much of the scree weight under the cliff bands (<see cref="CoverSample.scree"/>) is painted as scree.</summary>
        public const float MountainScreeUnderCliffs = 0.85f;

        /// <summary>Endless road, pass ranges: scree lift per metre of height-sample spacing over 2 m (distant tiles; see the near/far paint test).</summary>
        public const float MountainScreeFarGain = 0.35f;

        /// <summary>Endless road, pass ranges: how strongly the upland-grass drifts break up the heath (normalised drift noise × this).</summary>
        public const float HeathDriftStrength = 0.34f;

        /// <summary>Endless road, pass ranges (2026-10-06): frequency (per metre) of the mountain-rock outcrops on the moderate slopes.</summary>
        public const float MountainOutcropFrequency = 1f / 45f;

        /// <summary>Outcrop noise (normalised, s.d. about 1) at which an outcrop's rock starts and is full; the scree skirt starts at <see cref="MountainSkirtStart"/>.</summary>
        public const float MountainOutcropStart = 0.95f, MountainOutcropFull = 1.35f, MountainSkirtStart = 0.45f;

        /// <summary>
        /// Most of the turf on the moderate slopes that the dark peat patches (mud layer) take: 0.45 read as dark stains in the
        /// 08:12 tour of 2026-10-06 (P0072, P0073), so now only a soft darkening of the heath (with the faceted rock skin).
        /// </summary>
        public const float MountainPeat = 0.18f;

        /// <summary>
        /// The pass ranges' outcrop noise at noise-space point (<paramref name="nx"/>, <paramref name="nz"/>) (window position + noise
        /// origin; absolute on the endless road), normalised to a standard deviation of about 1, from the paint's outcrop noise
        /// (<c>GradientNoise(seed + 16)</c>). The tile generator seats the outcrop crags with the same function.
        /// </summary>
        public static float MountainOutcrop(GradientNoise outcropNoise, float nx, float nz) =>
            outcropNoise.Fbm(nx * MountainOutcropFrequency - 213f, nz * MountainOutcropFrequency + 157f, 2) / 0.16f;

        /// <summary>Weight of the moderate slopes (rise over run): from about 9 degrees, full from 16 to 37, gone by 47.</summary>
        public static float ModerateSlope(float slope) => SmoothStep01((slope - 0.16f) / 0.12f) * (1f - SmoothStep01((slope - 0.75f) / 0.32f));

        /// <summary>Endless road: an inn's worn yard is solid out to this radius from the inn site, metres.</summary>
        public const float InnYardCore = 9f;

        /// <summary>Endless road: the inn yard fades to grass over this width beyond <see cref="InnYardCore"/>, metres.</summary>
        public const float InnYardFade = 9f;

        /// <summary>Earth showing through a stubble field (<see cref="PropRole.StubbleField"/>), on average.</summary>
        public const float StubbleDirt = 0.22f;

        /// <summary>How much drier a pasture (<see cref="PropRole.PastureField"/>) is than the meadow around it.</summary>
        public const float PastureDryness = 0.25f;

        /// <summary>Flower-meadow weight of a meadow strip (<see cref="PropRole.MeadowStrip"/>).</summary>
        public const float MeadowStripFlowers = 0.55f;

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
            // The finite world (no window): the frozen copy, bit-identical to its golden hashes on Mono and CoreCLR.
            if (p.window == null) return ComputeFiniteSplatAsOf2026_10_02(p, roads, cover, sites, fields, 0f, 0f);
            return ComputeSplat(p, roads, cover, sites, fields, 0f, 0f);
        }

        /// <summary>
        /// The finite world's splat: <see cref="ComputeSplat(WorldGenParams, RoadDistanceField, LandCover, IReadOnlyList{SettlementSite}, IReadOnlyList{FieldPatch}, float, float)"/>
        /// exactly as it was on 2026-10-02, frozen. Unity's Mono (the editor's runtime) computes float arithmetic in double and rounds
        /// where the JIT stores a value, so any reshaping of the shared method (a new local, a branch, an inlined helper) moved the
        /// finite splat's last bits under Mono while CoreCLR stayed bit-identical (the endless drifts of 2026-10-02 did). The endless
        /// road keeps evolving the method above; the finite world, held to golden hashes on both runtimes, calls this copy.
        /// Do not edit it (see Docs/DESIGN.md, "Golden hashes under Mono").
        /// </summary>
        private static float[,,] ComputeFiniteSplatAsOf2026_10_02(WorldGenParams p, RoadDistanceField roads, LandCover cover,
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
            bool endless = p.window != null;
            GradientNoise outcropNoise = endless ? new GradientNoise(p.seed + 16) : null;

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
                        // The endless road's inns: a worn yard round the building, grass beyond (not a bare disc).
                        if (endless && sites[s].Type == SettlementType.Inn) inside = 1f - SmoothStep01((d - InnYardCore) / InnYardFade);
                        dirt = Mathf.Max(dirt, inside * Mathf.Lerp(0.4f, 0.85f, patch));
                        if (sites[s].Type == SettlementType.Town)
                        {
                            cobble = Mathf.Max(cobble, (1f - SmoothStep01((d - 14f) / 6f)) * 0.9f);
                        }
                    }

                    float pasture = 0f, hay = 0f, flowerStrip = 0f;
                    for (int f = 0; f < fields.Count; f++)
                    {
                        if (!fields[f].Contains(wx, wz, 0f)) continue;
                        string crop = fields[f].crop;
                        if (crop == PropRole.PastureField) pasture = 1f;
                        else if (crop == PropRole.StubbleField)
                        {
                            hay = 1f;
                            dirt = Mathf.Max(dirt, StubbleDirt * (0.6f + 0.8f * fine));
                        }
                        else if (crop == PropRole.MeadowStrip) flowerStrip = 1f;
                        else dirt = Mathf.Max(dirt, crop == PropRole.CropField ? 0.7f : 0.4f);
                        break;
                    }

                    float mud = cv.isWater ? 1f : cv.shore * 0.85f * (0.6f + 0.4f * fine);
                    mud = Mathf.Max(mud, vergeMud);

                    // Rock: blends in from 30° (ragged edge from the fine noise), solid from 40° in every biome, plus
                    // bare ridgelines high in the highlands.
                    float rock = cv.slope >= RockSlopeFull ? 1f
                        : SmoothStep01((cv.slope - RockSlopeStart) / (RockSlopeFull - RockSlopeStart) + 0.3f * (fine - 0.5f));
                    if (endless && cv.slope > RockSlopeStart - 0.05f && cv.slope < OutcropSlopeFull)
                    {
                        // Endless road, open hills (not the pass ranges): between 30° and 50° the rock comes as outcrops of grass-free
                        // stone among grass instead of a uniform half-and-half blend (or a solid face) that reads as brown ground from afar.
                        float open = 1f - LandCover.Smooth(OutcropHighlandStart, OutcropHighlandEnd, cv.highland);
                        if (open > 0f)
                        {
                            float outcrop = OutcropRock(cv.slope, outcropNoise.Fbm(nx * OutcropFrequency, nz * OutcropFrequency, 2), fine);
                            rock += (outcrop - rock) * open;
                        }
                    }

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
                    if (pasture > 0f) dry = Mathf.Clamp01(dry + PastureDryness);
                    if (hay > 0f) dry = Mathf.Max(dry, 0.9f);
                    if (flowerStrip > 0f)
                    {
                        flowers = Mathf.Max(flowers, MeadowStripFlowers);
                        dry = Mathf.Min(dry, 0.3f);
                    }

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
            // The endless road paints the eight shared layers plus its three mountain layers (EndlessLayerCount); the finite world eight.
            int channels = p.window != null ? EndlessLayerCount : LayerCount;
            var splat = new float[res, res, channels];
            float half = p.roadWidth * 0.5f;
            float core = FastMathf.Max(0.5f, half - RoadCoreInset);
            float amplitude = FastMathf.Max(1e-3f, p.hillAmplitude);
            var noise = new GradientNoise(p.seed + 9);
            var flowerNoise = new GradientNoise(p.seed + 14);
            var fleckNoise = new GradientNoise(p.seed + 15);
            var layers = new float[channels];
            float ox = p.NoiseOriginX;
            float oz = p.NoiseOriginZ;
            bool endless = p.window != null;
            GradientNoise outcropNoise = endless ? new GradientNoise(p.seed + 16) : null;
            GradientNoise driftNoise = endless ? new GradientNoise(p.seed + 17) : null;
            float heightSpacing = p.worldSize / FastMathf.Max(1, p.heightmapResolution - 1);
            float screeGain = 1f + MountainScreeFarGain * FastMathf.Max(0f, heightSpacing - 2f);

            // The fields' containment tests with their rotation worked out once (FieldPatch.Contains takes a sine and a cosine per
            // call), and per row only the fields whose bounding circle reaches it, in their original order (the first field wins).
            var fieldTests = new FieldTest[fields.Count];
            for (int f = 0; f < fields.Count; f++) fieldTests[f] = new FieldTest(fields[f]);
            var rowFields = new int[fields.Count];
            for (int z = 0; z < res; z++)
            {
                float wz = offsetZ + (z + 0.5f) * texel;
                float nz = wz + oz;
                int rowFieldCount = 0;
                for (int f = 0; f < fieldTests.Length; f++)
                {
                    if (fieldTests[f].ReachesRow(wz)) rowFields[rowFieldCount++] = f;
                }

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
                        roadDirt = FastMathf.Max(roadDirt, verge * 0.7f * fleck);
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
                        float d = FastMathf.Sqrt(dSqr);
                        float inside = 1f - SmoothStep01((d - r * 0.55f) / (r * 0.4f));
                        // The endless road's inns: a worn yard round the building, grass beyond (not a bare disc).
                        if (endless && sites[s].Type == SettlementType.Inn) inside = 1f - SmoothStep01((d - InnYardCore) / InnYardFade);
                        dirt = FastMathf.Max(dirt, inside * FastMathf.Lerp(0.4f, 0.85f, patch));
                        if (sites[s].Type == SettlementType.Town)
                        {
                            cobble = FastMathf.Max(cobble, (1f - SmoothStep01((d - 14f) / 6f)) * 0.9f);
                        }
                    }

                    float pasture = 0f, hay = 0f, flowerStrip = 0f;
                    for (int r = 0; r < rowFieldCount; r++)
                    {
                        int f = rowFields[r];
                        if (!fieldTests[f].Contains(wx, wz)) continue;
                        string crop = fields[f].crop;
                        if (crop == PropRole.PastureField) pasture = 1f;
                        else if (crop == PropRole.StubbleField)
                        {
                            hay = 1f;
                            dirt = FastMathf.Max(dirt, StubbleDirt * (0.6f + 0.8f * fine));
                        }
                        else if (crop == PropRole.MeadowStrip) flowerStrip = 1f;
                        else dirt = FastMathf.Max(dirt, crop == PropRole.CropField ? 0.7f : 0.4f);
                        break;
                    }

                    float mud = cv.isWater ? 1f : cv.shore * 0.85f * (0.6f + 0.4f * fine);
                    mud = FastMathf.Max(mud, vergeMud);

                    // Rock: blends in from 30° (ragged edge from the fine noise), solid from 40° in every biome, plus
                    // bare ridgelines high in the highlands.
                    float rock = cv.slope >= RockSlopeFull ? 1f
                        : SmoothStep01((cv.slope - RockSlopeStart) / (RockSlopeFull - RockSlopeStart) + 0.3f * (fine - 0.5f));
                    if (endless && cv.slope > RockSlopeStart - 0.05f && cv.slope < OutcropSlopeFull)
                    {
                        // Endless road, open hills (not the pass ranges): between 30° and 50° the rock comes as outcrops of grass-free
                        // stone among grass instead of a uniform half-and-half blend (or a solid face) that reads as brown ground from afar.
                        float open = 1f - LandCover.Smooth(OutcropHighlandStart, OutcropHighlandEnd, cv.highland);
                        if (open > 0f)
                        {
                            float outcrop = OutcropRock(cv.slope, outcropNoise.Fbm(nx * OutcropFrequency, nz * OutcropFrequency, 2), fine);
                            rock += (outcrop - rock) * open;
                        }
                    }

                    float hn = cv.height / amplitude;
                    rock = FastMathf.Max(rock, cv.highland * SmoothStep01((hn - 0.8f) / 0.12f) * (0.35f + 0.4f * fine));

                    // Scree under the cliff bands: gravelly dirt with rock flecks (dirt + rock layers, no extra layer).
                    float scree = cv.scree * (0.55f + 0.45f * patch);
                    float screeRock = scree * 0.35f * SmoothStep01((fine - 0.35f) / 0.3f);
                    float screeDirt = scree * 0.6f;

                    // Endless road, the pass ranges (cv.mountain: 1 in a range, less in its foothills, 0 elsewhere): grey mountain rock
                    // instead of the warm rock, scree (its own layer) on the talus fans and aprons, in the gully floors and in flecks
                    // under the cliff bands, and heath on the slopes and ledges, in patches on the floor.
                    float mountain = endless ? cv.mountain : 0f;
                    float mountainScree = 0f, heath = 0f;
                    if (mountain > 0f)
                    {
                        float chute = cv.gully * SmoothStep01((1.35f - cv.slope) / 0.4f);
                        float shed = FastMathf.Max(FastMathf.Max(FastMathf.Clamp01(cv.talus * 1.3f), 0.85f * chute), MountainScreeUnderCliffs * scree);
                        // Distant tiles (height samples over 2 m apart) see the fans, chutes and cliff feet blurred: their scree is lifted so
                        // near and far agree on average (calibrated on the passes of seeds 12345, 42 and 777).
                        shed *= screeGain;
                        mountainScree = mountain * SmoothStep01((shed * (0.75f + 0.5f * fine) - 0.2f) / 0.5f);
                        screeRock *= 1f - mountain;
                        screeDirt *= 1f - mountain;
                        float heathPatch = outcropNoise.Fbm(nx * HeathPatchFrequency + 71f, nz * HeathPatchFrequency - 37f, 2) / 0.16f;
                        heath = mountain * FastMathf.Clamp01(HeathFloorShare + (1f - HeathFloorShare) * SmoothStep01((cv.slope - 0.1f) / 0.3f) + HeathDriftStrength * heathPatch);

                        // The moderate slopes (2026-10-06; they read as smooth tan dunes): outcrops of the mountain rock, a skirt of scree
                        // round each, and dark peat patches in the heath, so a slope has structure from 200 m and from 1 km.
                        float moderate = mountain * ModerateSlope(cv.slope);
                        if (moderate > 0f)
                        {
                            float o = MountainOutcrop(outcropNoise, nx, nz);
                            float outcrop = moderate * SmoothStep01((o - MountainOutcropStart) / (MountainOutcropFull - MountainOutcropStart) + 0.35f * (fine - 0.5f));
                            float skirt = moderate * (1f - outcrop) * (0.35f + 0.65f * fine) * SmoothStep01((o - MountainSkirtStart) / 0.35f);
                            rock = FastMathf.Max(rock, outcrop);
                            mountainScree = FastMathf.Max(mountainScree, skirt);
                            float peat = outcropNoise.Fbm(nx * (1f / 38f) + 311f, nz * (1f / 38f) - 97f, 2) / 0.16f;
                            mud = FastMathf.Max(mud, moderate * MountainPeat * SmoothStep01((peat - 0.5f) / 0.8f));
                        }
                    }

                    // Above the tree line nothing lush: dry grass on ledges and shoulders, no flower patches.
                    float alpine = LandCover.Smooth(p.treeLine - 0.1f, p.treeLine, hn);
                    BiomeWeights w = cv.weights;
                    float floor = SmoothStep01((w.forest - 0.3f) / 0.45f) * 0.9f;
                    float flowerField = flowerNoise.Fbm(nx * 0.008f, nz * 0.008f, 2);
                    float flowerStart = 0.1f;
                    float drift = 0f;
                    if (endless)
                    {
                        // Broad tonal drifts (endless road): lusher and drier swathes, the lusher ones more flowery.
                        drift = driftNoise.Fbm(nx * DriftFrequency, nz * DriftFrequency, 2) / 0.16f;   // the two-octave fbm has a s.d. of about 0.16
                        flowerField -= DriftFlowers * drift;
                        flowerStart = EndlessFlowerStart;
                    }

                    float flowers = w.meadow * SmoothStep01((flowerField - flowerStart) / 0.25f) * 0.75f * (1f - alpine);
                    float dry = FastMathf.Clamp01(1f - LandCover.Smooth(0.42f, 0.66f, cv.moisture + 0.12f * (patch - 0.5f)) + 0.4f * cv.highland);
                    if (endless) dry = FastMathf.Clamp01(dry + DriftAmplitude * drift);
                    dry = FastMathf.Max(dry, alpine);
                    if (pasture > 0f) dry = FastMathf.Clamp01(dry + PastureDryness);
                    if (hay > 0f) dry = FastMathf.Max(dry, 0.9f);
                    if (flowerStrip > 0f)
                    {
                        flowers = FastMathf.Max(flowers, MeadowStripFlowers);
                        dry = FastMathf.Min(dry, 0.3f);
                    }

                    for (int l = 0; l < channels; l++) layers[l] = 0f;
                    float remaining = 1f;
                    Take(CobbleLayer, cobble);
                    Take(DirtLayer, roadDirt);
                    if (mountain > 0f)
                    {
                        // Fans and aprons lie at 30-33 degrees, where the slope rule starts rock: the scree comes first.
                        Take(ScreeLayer, mountainScree);
                        float before = remaining;
                        Take(MountainRockLayer, rock);
                        float taken = before - remaining;
                        layers[MountainRockLayer] -= taken * (1f - mountain);
                        layers[RockLayer] += taken * (1f - mountain);
                    }
                    else
                    {
                        Take(RockLayer, rock);
                    }

                    Take(RockLayer, screeRock);
                    Take(DirtLayer, screeDirt);
                    Take(DirtLayer, dirt);
                    Take(MudLayer, mud);
                    Take(DryGrassLayer, verge * (0.55f + 0.3f * fine));
                    Take(ForestFloorLayer, floor);
                    Take(FlowerLayer, flowers);
                    if (heath > 0f) Take(HeathLayer, heath);
                    layers[DryGrassLayer] += remaining * dry;
                    layers[LushGrassLayer] += remaining * (1f - dry);
                    for (int l = 0; l < channels; l++) splat[z, x, l] = layers[l];

                    void Take(int layer, float amount)
                    {
                        float a = FastMathf.Clamp01(amount) * remaining;
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
                int sz = FastMathf.Min(src - 1, (int)((z + 0.5f) * src / resolution));
                for (int x = 0; x < resolution; x++)
                {
                    int sx = FastMathf.Min(src - 1, (int)((x + 0.5f) * src / resolution));
                    for (int l = 0; l < layerCount; l++) result[z, x, l] = splat[sz, sx, l];
                }
            }

            return result;
        }

        private static void GridBounds(float cx, float cz, float radius, float spacing, int n, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = FastMathf.Max(0, FastMathf.FloorToInt((cx - radius) / spacing));
            x1 = FastMathf.Min(n - 1, Mathf.CeilToInt((cx + radius) / spacing));
            z0 = FastMathf.Max(0, FastMathf.FloorToInt((cz - radius) / spacing));
            z1 = FastMathf.Min(n - 1, Mathf.CeilToInt((cz + radius) / spacing));
        }

        /// <summary>Smoothstep of <paramref name="t"/> clamped to 0..1.</summary>
        public static float SmoothStep01(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;   // Mathf.Clamp01, written out: hot, and the call is not inlined
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// <see cref="FieldPatch.Contains"/> with no margin, its sine and cosine taken once: the same float operations in the same
        /// order, so every answer is bit-identical.
        /// </summary>
        private readonly struct FieldTest
        {
            private readonly float cx, cz, hx, hz, r2, s, c;

            public FieldTest(FieldPatch f)
            {
                cx = f.center.x;
                cz = f.center.z;
                hx = f.halfExtents.x + 0f;
                hz = f.halfExtents.y + 0f;
                r2 = hx * hx + hz * hz;
                float rad = f.yaw * Mathf.Deg2Rad;
                s = Mathf.Sin(rad);
                c = Mathf.Cos(rad);
            }

            /// <summary>False only where no point of row <paramref name="z"/> passes the bounding-circle test.</summary>
            public bool ReachesRow(float z)
            {
                float dz = z - cz;
                return !(dz * dz > r2);
            }

            public bool Contains(float x, float z)
            {
                float dx = x - cx;
                float dz = z - cz;
                if (dx * dx + dz * dz > r2) return false;
                float lx = dx * c - dz * s;
                float lz = dx * s + dz * c;
                return FastMathf.Abs(lx) <= hx && FastMathf.Abs(lz) <= hz;
            }
        }
    }
}
