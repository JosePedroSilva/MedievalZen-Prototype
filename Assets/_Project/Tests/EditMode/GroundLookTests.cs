using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// One look for grassland at every distance (<see cref="GroundLook"/>): sward colours, the endless road's even grass cover,
    /// fallow fields as pasture, distant fields as a patchwork, inn yards, far paint that agrees with the near paint, and the far
    /// tiles' colour texture (determinism, seams, noise). Pure.
    /// </summary>
    public sealed class GroundLookTests
    {
        private static EndlessGenerator generator;

        private static EndlessGenerator Gen() => generator ??= new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);

        [Test]
        public void SwardColoursMatchTheGroundUnderThem()
        {
            // Lush ground under tall/medium clumps, dry ground under short clumps: close (ΔE 3 and 4) where they were 17 and 38 apart.
            Vector3 lushGround = GroundLook.LayerColor(TerrainShaper.LushGrassLayer), dryGround = GroundLook.LayerColor(TerrainShaper.DryGrassLayer);
            Vector3 tall = Vector3.Scale(GroundLook.MeasuredGrass(PropRole.TallGrass), GroundLook.GrassTint(PropRole.TallGrass));
            Vector3 medium = Vector3.Scale(GroundLook.MeasuredGrass(PropRole.MedGrass), GroundLook.GrassTint(PropRole.MedGrass));
            Vector3 shortGrass = Vector3.Scale(GroundLook.MeasuredGrass(PropRole.ShortGrass), GroundLook.GrassTint(PropRole.ShortGrass));
            Assert.That(GroundLook.DeltaE(tall, GroundLook.LushSward), Is.LessThan(0.01f));
            Assert.That(GroundLook.DeltaE(medium, GroundLook.LushSward), Is.LessThan(0.01f));
            Assert.That(GroundLook.DeltaE(shortGrass, GroundLook.DrySward), Is.LessThan(0.01f));
            Assert.That(GroundLook.DeltaE(lushGround, tall), Is.LessThan(4f));
            Assert.That(GroundLook.DeltaE(dryGround, shortGrass), Is.LessThan(6f));
            Assert.That(GroundLook.DeltaE(lushGround, shortGrass), Is.LessThan(8f), "short tufts on lush ground are a mild accent");
            Assert.That(GroundLook.DeltaE(GroundLook.TextureMeans[TerrainShaper.LushGrassLayer], GroundLook.MeasuredGrass(PropRole.TallGrass)), Is.GreaterThan(15f), "the measured mismatch this fixes");

            // The ground is a little darker than its clumps (they still read), and the two zones stay distinct.
            Assert.That(Luminance(lushGround), Is.InRange(0.85f * Luminance(tall), Luminance(tall)));
            Assert.That(Luminance(dryGround), Is.InRange(0.85f * Luminance(shortGrass), Luminance(shortGrass)));
            Assert.That(GroundLook.DeltaE(lushGround, dryGround), Is.GreaterThan(8f));

            // Only the grass layers are tinted; the distant colour of a layer is its texture mean times its tint.
            for (int c = 0; c < TerrainShaper.LayerCount; c++)
            {
                Assert.That(GroundLook.IsTinted(c), Is.EqualTo(GroundLook.LayerTints[c] != Vector3.one), $"channel {c}");
                Assert.That((GroundLook.LayerColor(c) - Vector3.Scale(GroundLook.TextureMeans[c], GroundLook.LayerTints[c])).magnitude, Is.LessThan(1e-7f));
            }

            Assert.That(GroundLook.GrassTint(PropRole.Wildflowers), Is.EqualTo(Vector3.one), "flowers keep their colours");
            Assert.That(GroundLook.IsSward(PropRole.ShortGrass), Is.True);
            Assert.That(GroundLook.IsSward(PropRole.SunflowerDetail), Is.False, "sunflowers keep their shadows and normals");
            Assert.That(GroundLook.DeltaE(Vector3.one, Vector3.one), Is.EqualTo(0f));
            Assert.That(GroundLook.Lab(Vector3.one).x, Is.EqualTo(100f).Within(0.05f));
        }

        [Test]
        public void EndlessGrassIsAnEvenSward()
        {
            // Grass-zone cells (2 m) of real near tiles: almost none without a grass clump (the old patch noise left 3 %).
            long cells = 0, empty = 0;
            foreach (TileBlueprint near in NearTilesAlongTheRoad(8))
            {
                int dres = near.DetailResolution, k = near.Splat.GetLength(0) / dres;
                var grass = new int[dres * dres];
                foreach (DetailLayer layer in near.Details)
                {
                    if (layer.family != PropRole.TallGrass && layer.family != PropRole.MedGrass && layer.family != PropRole.ShortGrass) continue;
                    for (int i = 0; i < grass.Length; i++) grass[i] += layer.density[i];
                }

                for (int z = 0; z < dres; z++)
                {
                    for (int x = 0; x < dres; x++)
                    {
                        float g = 0f, forest = 0f;
                        for (int dz = 0; dz < k; dz++)
                        {
                            for (int dx = 0; dx < k; dx++)
                            {
                                int sz = z * k + dz, sx = x * k + dx;
                                g += near.Splat[sz, sx, TerrainShaper.LushGrassLayer] + near.Splat[sz, sx, TerrainShaper.DryGrassLayer] + near.Splat[sz, sx, TerrainShaper.FlowerLayer];
                                forest += near.Splat[sz, sx, TerrainShaper.ForestFloorLayer];
                            }
                        }

                        if (g < 0.75f * k * k || forest > 0.2f * k * k) continue;
                        cells++;
                        if (grass[z * dres + x] == 0) empty++;
                    }
                }

                Assert.That(near.Details.Count, Is.LessThanOrEqualTo(16), "per-tile detail layer cap");
            }

            Assert.That(cells, Is.GreaterThan(20000));
            Assert.That((double)empty / cells, Is.LessThan(0.01), $"{empty} of {cells} grass cells bare");

            // The endless table has the same families in the same order as the finite one (same layers, same noise streams).
            List<DetailRule> endless = DetailPlanner.EndlessRules(), finite = DetailPlanner.DefaultRules();
            Assert.That(endless.Count, Is.EqualTo(finite.Count));
            for (int r = 0; r < endless.Count; r++) Assert.That(endless[r].family, Is.EqualTo(finite[r].family));
        }

        [Test]
        public void FallowFieldsArePastureAndDistantFieldsAPatchwork()
        {
            int pastures = 0, checkedTexels = 0;
            var looks = new Dictionary<string, int>();
            var fields = new List<MosaicField>();
            for (double s = 0; s < 40000 && pastures < 3; s += 400)
            {
                if (Gen().Region(s).Kind != RegionKind.Farmland) continue;
                RoadPoint r = Gen().Road(s);
                fields.Clear();
                Gen().Fields.Overlapping(r.X - 300, r.Z - 300, 600, fields);
                foreach (MosaicField f in fields)
                {
                    string look = TileGenerator.FieldLook(f, Gen().Params.seed);
                    looks[look] = looks.TryGetValue(look, out int n) ? n + 1 : 1;
                    if (f.Fallow) Assert.That(look, Is.EqualTo(PropRole.PastureField));
                    else if (f.Planted) Assert.That(look, Is.EqualTo(f.Crop), "planted fields keep their crop (tilled, fenced)");
                    else Assert.That(look == PropRole.StubbleField || look == PropRole.MeadowStrip || look == PropRole.CropField, Is.True, look);
                    Assert.That(PropRole.IsGrassField(look), Is.EqualTo(look == PropRole.PastureField || look == PropRole.StubbleField || look == PropRole.MeadowStrip));
                    if (!f.Fallow || pastures >= 3) continue;

                    // The pasture's tile: grass paint and grass clumps inside (no tall grass, grazed), no bare earth but the gateway.
                    TileCoord c = TileCoord.Containing(f.X, f.Z, Gen().Params.tileSize);
                    TileBlueprint near = Gen().GenerateTile(c, TileLod.Near);
                    float ts = near.TileSize;
                    FieldPatch patch = FieldMosaic.ToPatch(f, c.OriginX(ts), c.OriginZ(ts), 0f);
                    var gateway = TileGenerator.Gateway(f);
                    int sres = near.Splat.GetLength(0), inside = 0;
                    double dirt = 0;
                    for (int z = 0; z < sres; z++)
                    {
                        for (int x = 0; x < sres; x++)
                        {
                            float lx = (x + 0.5f) * ts / sres, lz = (z + 0.5f) * ts / sres;
                            if (!patch.Contains(lx, lz, -2f)) continue;
                            double ax = c.OriginX(ts) + lx, az = c.OriginZ(ts) + lz;
                            if (Distance(ax, az, gateway.x0, gateway.z0, gateway.x1, gateway.z1) < 4.0) continue;
                            dirt += near.Splat[z, x, TerrainShaper.DirtLayer];
                            inside++;
                        }
                    }

                    if (inside < 400) continue;   // mostly in a neighbouring tile
                    pastures++;
                    checkedTexels += inside;
                    Assert.That(dirt / inside, Is.LessThan(0.05), "a pasture is grass, not ploughed earth");

                    int dres = near.DetailResolution, grass = 0, tall = 0;
                    foreach (DetailLayer layer in near.Details)
                    {
                        for (int z = 0; z < dres; z++)
                        {
                            for (int x = 0; x < dres; x++)
                            {
                                if (layer.density[z * dres + x] == 0 || !patch.Contains((x + 0.5f) * ts / dres, (z + 0.5f) * ts / dres, -1f)) continue;
                                if (layer.family == PropRole.TallGrass) tall++;
                                if (layer.family == PropRole.ShortGrass || layer.family == PropRole.MedGrass) grass++;
                            }
                        }
                    }

                    Assert.That(tall, Is.EqualTo(0), "grazed: no tall grass");
                    Assert.That(grass, Is.GreaterThan(inside / 8), "short grass covers the pasture");
                }
            }

            Assert.That(pastures, Is.GreaterThan(0), "found a fallow field to check");
            Assert.That(checkedTexels, Is.GreaterThan(1000));
            Assert.That(looks.ContainsKey(PropRole.StubbleField) || looks.ContainsKey(PropRole.MeadowStrip), Is.True, "distant fields are a patchwork");
        }

        [Test]
        public void FieldLooksPaintAsDesigned()
        {
            // One field of each look on a flat window: earth for tilled fields only.
            var p = new WorldGenParams { seed = 9, worldSize = 64f, heightmapResolution = 33, hillAmplitude = 100f, window = new TileWindow(1024.0, 2048.0) };
            var map = new float[33, 33];
            for (int z = 0; z < 33; z++) for (int x = 0; x < 33; x++) map[z, x] = 0.3f;
            LandCover cover = LandCover.Create(32, 64f, -1f);
            for (int i = 0; i < 32 * 32; i++) { cover.Height[i] = 30f; cover.Moisture[i] = 0.7f; }
            RoadDistanceField roads = RoadDistanceField.Build(new List<IReadOnlyList<Vector3>> { new[] { new Vector3(-500f, 0f, -500f), new Vector3(-499f, 0f, -500f) } }, 32, 64f, 40f);
            float Dirt(string crop)
            {
                var field = new FieldPatch { center = new Vector3(32f, 0f, 32f), halfExtents = new Vector2(20f, 20f), yaw = 0f, settlementId = -1, crop = crop };
                float[,,] splat = TerrainShaper.ComputeSplat(p, roads, cover, new List<SettlementSite>(), new List<FieldPatch> { field }, 0f, 0f);
                return splat[16, 16, TerrainShaper.DirtLayer];
            }

            Assert.That(Dirt(PropRole.CropField), Is.EqualTo(0.7f).Within(1e-4f), "planted wheat: tilled earth");
            Assert.That(Dirt(PropRole.Rapeseed), Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(Dirt(PropRole.PastureField), Is.EqualTo(0f));
            Assert.That(Dirt(PropRole.MeadowStrip), Is.EqualTo(0f));
            Assert.That(Dirt(PropRole.StubbleField), Is.InRange(0.1f, 0.4f), "stubble shows some earth");
        }

        [Test]
        public void FarPaintAgreesWithNearPaint([Values(TileLod.Far, TileLod.Mid)] TileLod lod)
        {
            // The same tiles at both levels: per-layer means and the distant colour agree (rock was under-painted far away
            // before the far slopes were scaled; settlement discs were over-painted).
            var near = new double[TerrainShaper.LayerCount];
            var far = new double[TerrainShaper.LayerCount];
            var deltas = new List<float>();
            Vector3[] colors = GroundLook.LayerColors();
            int n = 0;
            foreach (TileCoord c in TilesAlongTheRoad(10, true))
            {
                double[] a = Means(Gen().GenerateTile(c, TileLod.Near).Splat), b = Means(Gen().GenerateTile(c, lod).Splat);
                Vector3 ca = Vector3.zero, cb = Vector3.zero;
                for (int l = 0; l < TerrainShaper.LayerCount; l++)
                {
                    near[l] += a[l];
                    far[l] += b[l];
                    ca += colors[l] * (float)a[l];
                    cb += colors[l] * (float)b[l];
                }

                deltas.Add(GroundLook.DeltaE(ca, cb));
                n++;
            }

            for (int l = 0; l < TerrainShaper.LayerCount; l++)
            {
                Assert.That(Math.Abs(near[l] - far[l]) / n, Is.LessThan(0.02), $"layer {TerrainShaper.LayerNames[l]}: near {near[l] / n:0.000}, far {far[l] / n:0.000}");
            }

            deltas.Sort();
            float mean = 0f;
            foreach (float d in deltas) mean += d / deltas.Count;
            Assert.That(mean, Is.LessThan(1f), "near and far ground look the same on average (CIE76 ΔE)");
            Assert.That(deltas[deltas.Count - 1], Is.LessThan(5f));
        }

        [Test]
        public void FarFaceZonesAgreeWithTheNearPaint([Values(TileLod.Far, TileLod.Mid)] TileLod lod)
        {
            // Per zone, the share of the ground the distant faces take matches the share of near texels whose strongest channel it
            // is (trees and streams left out: they add the canopy and water zones on purpose).
            var near = new double[FarTileMeshData.ZoneCount];
            var far = new double[FarTileMeshData.ZoneCount];
            foreach (TileCoord c in TilesAlongTheRoad(8, true))
            {
                float[,,] s = Gen().GenerateTile(c, TileLod.Near).Splat;
                int r = s.GetLength(0), layers = s.GetLength(2);
                var texel = new float[layers];
                for (int z = 0; z < r; z++)
                {
                    for (int x = 0; x < r; x++)
                    {
                        // The strongest channel, with the wood floor's mix (forest floor, mud, leaf litter) as one zone, as the faces take it.
                        for (int l = 0; l < layers; l++) texel[l] = s[z, x, l];
                        int best = FarTileMeshData.ChooseZone(texel, 0f, 0f, 0u, 0f);
                        near[best] += 1.0 / (r * r);
                    }
                }

                TileBlueprint bp = Gen().GenerateTile(c, lod);
                bp.Trees.Clear();
                bp.Streams.Clear();
                FarTileMeshData d = FarTileMeshData.Build(bp, FarTreeStyles.Defaults, new FarMeshSettings());
                float total = 0f;
                foreach (float a in d.ZoneArea) total += a;
                for (int zn = 0; zn < FarTileMeshData.ZoneCount; zn++) far[zn] += d.ZoneArea[zn] / total;
            }

            for (int zn = 0; zn < TerrainShaper.LayerCount; zn++)
            {
                Assert.That(Math.Abs(near[zn] - far[zn]) / 8.0, Is.LessThan(0.04), $"zone {FarTileMeshData.ZoneNames[zn]}: near {near[zn] / 8:0.000}, far {far[zn] / 8:0.000}");
            }
        }

        [Test]
        public void SwardMaterialsAreMatte()
        {
            // The grass looked frosted at dusk: up-facing blades with smoothness 0.2 mirror the sky. The palette (v6) makes them matte.
            foreach (string name in new[] { "_Leaf_Smoothness", "_Trunk_Smoothness", "_Smoothness" }) Assert.That(GroundLook.SwardSmoothnessProperties, Has.Member(name));
            foreach (string name in new[] { "_Leaf_Metallic", "_Trunk_Metallic", "_Metallic" }) Assert.That(GroundLook.SwardMetallicProperties, Has.Member(name));
            Assert.That(GroundLook.SwardSmoothness, Is.EqualTo(0f));
            Assert.That(GroundLook.SwardMetallic, Is.EqualTo(0f));
            Assert.That(WorldPalette.CurrentDetailVersion, Is.GreaterThanOrEqualTo(6), "the palette version is bumped so the sward materials are rebuilt");
        }

        [Test]
        public void OpenHillsKeepRockToSteepFacesAndOutcrops()
        {
            // The rule itself: none below 30°, solid from 50°, a share rising as t² in between, in patches.
            float Slope(double deg) => (float)Math.Tan(deg * Math.PI / 180.0);
            Assert.That(TerrainShaper.OutcropRock(Slope(25), 0.4f, 0.5f), Is.EqualTo(0f));
            Assert.That(TerrainShaper.OutcropRock(Slope(29.9), 0.4f, 0.5f), Is.EqualTo(0f));
            Assert.That(TerrainShaper.OutcropRock(Slope(50.5), -0.4f, 0.5f), Is.EqualTo(1f));
            Assert.That(TerrainShaper.OutcropRock(Slope(40.5), -0.4f, 0.5f), Is.LessThan(0.5f), "a 40° face is not solid rock any more");
            var noise = new GradientNoise(77);
            double previous = -1.0;
            foreach (double deg in new[] { 33.0, 37.0, 41.0, 45.0, 49.0 })
            {
                double mean = 0.0;
                int solid = 0, bare = 0;
                for (int i = 0; i < 4000; i++)
                {
                    float n = noise.Fbm(i * 0.37f * TerrainShaper.OutcropFrequency * 20f, i * 0.11f, 2);
                    float r = TerrainShaper.OutcropRock(Slope(deg), n, 0.5f);
                    mean += r / 4000.0;
                    if (r > 0.95f) solid++;
                    if (r < 0.05f) bare++;
                }

                double t = (Slope(deg) - TerrainShaper.RockSlopeStart) / (TerrainShaper.OutcropSlopeFull - TerrainShaper.RockSlopeStart);
                Assert.That(mean, Is.EqualTo(Math.Pow(t, TerrainShaper.OutcropCoverageExponent)).Within(0.12), $"{deg}°");
                Assert.That(mean, Is.GreaterThan(previous), "more rock the steeper");
                Assert.That(solid + bare, Is.GreaterThan(2400), $"{deg}°: patches of rock and of grass, not a uniform blend");
                previous = mean;
            }

            // Through ComputeSplat: endless open hills get patchy rock (here a 43° face), pass ranges and the finite world the old
            // blend (a 35° face).
            float RockMean(bool endless, float highland, out int patchy, double deg = 35.0)
            {
                var p = new WorldGenParams { seed = 5, worldSize = 128f, heightmapResolution = 65, hillAmplitude = 400f, window = endless ? new TileWindow(4096.0, 8192.0) : null };
                LandCover cover = LandCover.Create(64, 128f, -1f);
                for (int i = 0; i < 64 * 64; i++)
                {
                    cover.Height[i] = 60f;
                    cover.Slope[i] = Slope(deg);
                    cover.Moisture[i] = 0.6f;
                    cover.Highland[i] = highland;
                }

                RoadDistanceField roads = RoadDistanceField.Build(new List<IReadOnlyList<Vector3>> { new[] { new Vector3(-900f, 0f, -900f), new Vector3(-899f, 0f, -900f) } }, 64, 128f, 40f);
                float[,,] splat = TerrainShaper.ComputeSplat(p, roads, cover, new List<SettlementSite>(), new List<FieldPatch>(), 0f, 0f);
                double sum = 0.0;
                patchy = 0;
                for (int z = 0; z < 64; z++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        float r = splat[z, x, TerrainShaper.RockLayer];
                        sum += r;
                        if (r < 0.1f || r > 0.9f) patchy++;
                    }
                }

                return (float)(sum / 4096.0);
            }

            float open = RockMean(true, 0f, out int openPatchy, 43.0), pass = RockMean(true, 1f, out int passPatchy), finite = RockMean(false, 0f, out int finitePatchy);
            Assert.That(open, Is.InRange(0.15f, 0.55f), "open hills at 43°: outcrops on a minority of the face");
            Assert.That(openPatchy, Is.GreaterThan(4096 * 6 / 10), "in patches");
            Assert.That(pass, Is.InRange(0.3f, 0.7f), "the pass ranges keep the 30°–40° blend");
            Assert.That(finite, Is.EqualTo(pass).Within(0.15f), "the finite world keeps its rule (guarded bit-exactly by the golden hashes)");
        }

        [Test]
        public void InnYardIsWornGroundNotABareDisc()
        {
            // Endless inns paint a worn yard of about 9 + 9 m round the site; the old disc reached 33 m.
            Assert.That(TerrainShaper.InnYardCore + TerrainShaper.InnYardFade, Is.LessThanOrEqualTo(20f));
            var p = new WorldGenParams { seed = 3, worldSize = 128f, heightmapResolution = 65, hillAmplitude = 100f, window = new TileWindow(0.0, 0.0) };
            LandCover cover = LandCover.Create(64, 128f, -1f);
            for (int i = 0; i < 64 * 64; i++) { cover.Height[i] = 30f; cover.Moisture[i] = 0.7f; }
            RoadDistanceField roads = RoadDistanceField.Build(new List<IReadOnlyList<Vector3>> { new[] { new Vector3(-900f, 0f, -900f), new Vector3(-899f, 0f, -900f) } }, 64, 128f, 40f);
            var inn = new SettlementSite("Inn", MedievalZen.Roads.SettlementType.Inn, new Vector3(64f, 30f, 64f));
            float[,,] endless = TerrainShaper.ComputeSplat(p, roads, cover, new List<SettlementSite> { inn }, new List<FieldPatch>(), 0f, 0f);
            p.window = null;
            float[,,] finite = TerrainShaper.ComputeSplat(p, roads, cover, new List<SettlementSite> { inn }, new List<FieldPatch>(), 0f, 0f);
            // 2 m texels: (64 + 25 m) is texel 44.
            Assert.That(endless[32, 44, TerrainShaper.DirtLayer], Is.EqualTo(0f), "grass 25 m from an endless inn");
            Assert.That(finite[32, 44, TerrainShaper.DirtLayer], Is.GreaterThan(0.1f), "the finite world keeps its disc");
            Assert.That(endless[32, 33, TerrainShaper.DirtLayer], Is.GreaterThan(0.3f), "a worn yard at the inn");
        }

        // ------------------------------------------------------------------ helpers

        private static FarTileMeshData Build(TileCoord c, bool trees = true)
        {
            TileBlueprint bp = Gen().GenerateTile(c, TileLod.Far);
            if (!trees) bp.Trees.Clear();
            return FarTileMeshData.Build(bp, FarTreeStyles.Defaults, new FarMeshSettings(), Gen().Height);
        }

        private static List<TileCoord> TilesAlongTheRoad(int count, bool spread)
        {
            var list = new List<TileCoord>();
            float ts = Gen().Params.tileSize;
            for (int i = 0; list.Count < count; i++)
            {
                RoadPoint r = Gen().Road(900.0 + i * (spread ? 2300.0 : 700.0));
                TileCoord c = TileCoord.Containing(r.X, r.Z, ts);
                list.Add(new TileCoord(c.X + (i % 2 == 0 ? 1 : -2), c.Z));
            }

            return list;
        }

        private static IEnumerable<TileBlueprint> NearTilesAlongTheRoad(int count)
        {
            foreach (TileCoord c in TilesAlongTheRoad(count, true)) yield return Gen().GenerateTile(c, TileLod.Near);
        }

        private static double[] Means(float[,,] splat)
        {
            int r = splat.GetLength(0), layers = splat.GetLength(2);
            var m = new double[layers];
            for (int z = 0; z < r; z++) for (int x = 0; x < r; x++) for (int l = 0; l < layers; l++) m[l] += splat[z, x, l];
            for (int l = 0; l < layers; l++) m[l] /= r * r;
            return m;
        }

        private static float Difference(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));

        private static float Luminance(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;

        private static double Distance(double px, double pz, double ax, double az, double bx, double bz)
        {
            double ex = bx - ax, ez = bz - az, len2 = ex * ex + ez * ez;
            double t = len2 > 1e-9 ? Math.Max(0.0, Math.Min(1.0, ((px - ax) * ex + (pz - az) * ez) / len2)) : 0.0;
            double dx = px - (ax + ex * t), dz = pz - (az + ez * t);
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
