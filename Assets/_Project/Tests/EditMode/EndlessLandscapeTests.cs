using System;
using System.Collections.Generic;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The endless road's landscape pass (2026-10-02): passes rise out of foothills instead of walls dropped on the meadow,
    /// lowland hills are grass with outcrops rather than bare mounds, lake beds have no underwater cliffs, the countryside is
    /// planted in structures (roadside stretches, hedgerows, copses, crest trees) with bushes drawn instanced, the flowers
    /// follow the flower-meadow paint and the sward has broad tonal drifts without small blotches. Pure (no Unity objects).
    /// </summary>
    public sealed class EndlessLandscapeTests
    {
        private static readonly Dictionary<int, EndlessGenerator> Generators = new Dictionary<int, EndlessGenerator>();

        private static EndlessGenerator Gen(int seed)
        {
            lock (Generators)
            {
                if (!Generators.TryGetValue(seed, out EndlessGenerator g))
                {
                    g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
                    Generators[seed] = g;
                }

                return g;
            }
        }

        private static Region FirstPass(EndlessGenerator g)
        {
            Region r = g.Regions.At(0.0);
            while (r.Kind != RegionKind.HighlandPass) r = g.Regions.Get(r.Index + 1);
            return r;
        }

        /// <summary>Share of ground steeper than 30° and the highest ground above the road, within 300 m of the road over [s0, s1].</summary>
        private static (double steep, float maxAbove) Stretch(EndlessGenerator g, double s0, double s1)
        {
            int n = 0, steep = 0;
            float maxAbove = float.MinValue;
            float tan30 = Mathf.Tan(30f * Mathf.Deg2Rad);
            for (double s = s0; s <= s1; s += 10.0)
            {
                RoadPoint rp = g.Road(s);
                double rad = rp.HeadingDeg * Math.PI / 180.0;
                double nx = Math.Cos(rad), nz = -Math.Sin(rad);
                for (double v = -300.0; v <= 300.0; v += 10.0)
                {
                    if (Math.Abs(v) < 14.0) continue;   // the road bed and its banks
                    double x = rp.X + nx * v, z = rp.Z + nz * v;
                    float h = g.Height(x, z);
                    float gx = (g.Height(x + 1.0, z) - g.Height(x - 1.0, z)) * 0.5f;
                    float gz = (g.Height(x, z + 1.0) - g.Height(x, z - 1.0)) * 0.5f;
                    n++;
                    if (Mathf.Sqrt(gx * gx + gz * gz) >= tan30) steep++;
                    maxAbove = Mathf.Max(maxAbove, h - rp.Height);
                }
            }

            return ((double)steep / n, maxAbove);
        }

        [Test]
        public void PassEnvelope_IsFoothillsAtTheEnds_AndTheRangeInside()
        {
            var r = new Region { Kind = RegionKind.HighlandPass, Start = 1000.0, Length = 3200.0 };
            Assert.That(TerrainField.PassEnvelope(r, 800.0), Is.EqualTo(0f));
            Assert.That(TerrainField.PassEnvelope(r, 1000.0), Is.EqualTo(0f));
            Assert.That(TerrainField.PassEnvelope(r, 1000.0 + TerrainField.FoothillReach), Is.EqualTo(1f));
            Assert.That(TerrainField.PassEnvelope(r, 2600.0), Is.EqualTo(1f));
            Assert.That(TerrainField.PassEnvelope(r, r.End), Is.EqualTo(0f));
            float previous = -1f;
            for (double s = r.Start; s <= r.Start + TerrainField.FoothillReach; s += 50.0)
            {
                float e = TerrainField.PassEnvelope(r, s);
                Assert.That(e, Is.GreaterThanOrEqualTo(previous), "rises steadily into the range");
                previous = e;
            }
        }

        [Test, Category("Slow")]
        public void Passes_RiseThroughFoothills_AndKeepTheirWallsAtTheCol([Values(12345, 42, 777)] int seed)
        {
            // Measured before the change: 30-44 % of the ground within 300 m was 30° or more right at the pass entrance, with
            // ground 110-140 m above the road (a wall rising out of the meadow beside an inn); after: 1-6 %, under 75 m.
            EndlessGenerator g = Gen(seed);
            Region pass = FirstPass(g);
            (double steepIn, float aboveIn) = Stretch(g, pass.Start - 200.0, pass.Start + 250.0);
            (double steepOut, float aboveOut) = Stretch(g, pass.End - 250.0, pass.End + 200.0);
            Assert.That(steepIn, Is.LessThan(0.08), $"entering the pass at {pass.Start:0}: {100 * steepIn:0.0} % steep");
            Assert.That(steepOut, Is.LessThan(0.08), $"leaving the pass at {pass.End:0}: {100 * steepOut:0.0} % steep");
            Assert.That(aboveIn, Is.LessThan(80f), "foothills at the entrance, not the range");
            _ = aboveOut;   // beyond the exit the downs or the wood have their own (gentle) hills
            (double steepCol, float aboveCol) = Stretch(g, pass.Col - 300.0, pass.Col + 300.0);
            Assert.That(steepCol, Is.GreaterThan(0.12), $"the range keeps its walls at the col ({100 * steepCol:0.0} % steep)");
            Assert.That(aboveCol, Is.GreaterThan(80f), "and its height");
        }

        [Test, Category("Slow")]
        public void LowlandHills_AreGrassWithOutcrops_NotBareMounds([Values(12345, 42, 777)] int seed)
        {
            // Rock share and the largest bare patch (rock paint over half, 4-connected, 1 m texels) of near tiles along the
            // lowland road (away from the passes). Measured before: bare faces up to 7,940 m² in the downs.
            EndlessGenerator g = Gen(seed);
            EndlessParams p = g.Params;
            double rock = 0.0;
            long texels = 0;
            int largest = 0, tiles = 0;
            for (double s = 500.0; s < 20000.0; s += 1100.0)
            {
                Region r = g.Regions.At(s);
                Region a = g.Regions.At(s - 700.0), b = g.Regions.At(s + 700.0);
                if (r.Kind == RegionKind.HighlandPass || a.Kind == RegionKind.HighlandPass || b.Kind == RegionKind.HighlandPass) continue;
                foreach (double lateral in new[] { -260.0, 260.0 })
                {
                    RoadPoint rp = g.Road(s);
                    double rad = rp.HeadingDeg * Math.PI / 180.0;
                    TileCoord c = TileCoord.Containing(rp.X + Math.Cos(rad) * lateral, rp.Z - Math.Sin(rad) * lateral, p.tileSize);
                    TileBlueprint t = g.GenerateTile(c, TileLod.Near);
                    int res = t.Splat.GetLength(0);
                    var bare = new bool[res * res];
                    for (int z = 0; z < res; z++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            float w = t.Splat[z, x, TerrainShaper.RockLayer];
                            rock += w;
                            bare[z * res + x] = w > 0.5f;
                        }
                    }

                    texels += res * res;
                    largest = Math.Max(largest, LargestPatch(bare, res));
                    tiles++;
                }
            }

            Assert.That(tiles, Is.GreaterThan(10));
            Assert.That(rock / texels, Is.LessThan(0.05), "lowland ground painted as rock");
            Assert.That(largest, Is.LessThan(3000), "largest bare face in a lowland tile, m²");
        }

        private static int LargestPatch(bool[] bare, int res)
        {
            var seen = new bool[bare.Length];
            var stack = new Stack<int>();
            int best = 0;
            for (int i = 0; i < bare.Length; i++)
            {
                if (!bare[i] || seen[i]) continue;
                int count = 0;
                seen[i] = true;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    count++;
                    int x = k % res, z = k / res;
                    if (x > 0 && bare[k - 1] && !seen[k - 1]) { seen[k - 1] = true; stack.Push(k - 1); }
                    if (x < res - 1 && bare[k + 1] && !seen[k + 1]) { seen[k + 1] = true; stack.Push(k + 1); }
                    if (z > 0 && bare[k - res] && !seen[k - res]) { seen[k - res] = true; stack.Push(k - res); }
                    if (z < res - 1 && bare[k + res] && !seen[k + res]) { seen[k + res] = true; stack.Push(k + res); }
                }

                best = Math.Max(best, count);
            }

            return best;
        }

        [Test]
        public void LakeBeds_AreBowls_WithoutUnderwaterCliffs()
        {
            EndlessGenerator g = Gen(42);
            var wps = new List<Waypoint>();
            g.Waypoints(0.0, 20000.0, wps);
            int checkedPoints = 0;
            foreach (Waypoint lake in wps.Where(w => w.Kind == WaypointKind.Lake))
            {
                float level = lake.Height;
                for (double dz = -lake.Radius; dz <= lake.Radius; dz += 12.0)
                {
                    for (double dx = -lake.Radius; dx <= lake.Radius; dx += 12.0)
                    {
                        double x = lake.X + dx, z = lake.Z + dz;
                        if (g.WaterHeight(x, z) != level) continue;
                        float h = g.Height(x, z);
                        checkedPoints++;
                        Assert.That(h, Is.GreaterThanOrEqualTo(level - 3.5f - 0.012f * 400f - TerrainField.LakeBedSlack - 0.01f), "deeper than any designed bed");
                    }
                }
            }

            Assert.That(checkedPoints, Is.GreaterThan(100));
        }

        [Test]
        public void Bushes_AreDrawnInstanced_AndOnlyOnNearTiles()
        {
            EndlessGenerator g = Gen(12345);
            EndlessParams p = g.Params;
            int bushes = 0;
            foreach (double s in new[] { 700.0, 2600.0, 9500.0, 16000.0 })
            {
                RoadPoint rp = g.Road(s);
                TileCoord c = TileCoord.Containing(rp.X + 120.0, rp.Z, p.tileSize);
                TileBlueprint near = g.GenerateTile(c, TileLod.Near);
                foreach (PropPlacement prop in near.Props)
                {
                    if (prop.settlementId < 0) Assert.That(PropRole.IsInstancedPlant(prop.role), Is.False, $"{prop.role} is a GameObject at s {s}");
                }

                bushes += near.Trees.Count(t => PropRole.IsInstancedPlant(t.role));
                foreach (TileLod lod in new[] { TileLod.Mid, TileLod.Far })
                {
                    Assert.That(g.GenerateTile(c, lod).Trees.Any(t => PropRole.IsInstancedPlant(t.role)), Is.False, $"{lod} tiles draw no bushes");
                }
            }

            Assert.That(bushes, Is.GreaterThan(200), "the near tiles have their understorey");
        }

        [Test]
        public void RoadsideTrees_ComeInStretches_ThatAlternateWithOpenGround([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            var planting = new Planting(g.Params, g.Keys, g.Curve, g.Regions, g.Plan, g.Profile, g.Terrain, g.Fields, g.Features);
            int lowland = 0, lined = 0;
            double longestOpen = 0.0, longestLined = 0.0, runStart = 0.0;
            bool previous = false;
            for (double s = 0.0; s < 20000.0; s += 10.0)
            {
                if (g.Regions.At(s).Kind == RegionKind.HighlandPass)
                {
                    // A pass is open ground: a stretch ends at it.
                    if (!previous) longestOpen = Math.Max(longestOpen, s - runStart);
                    previous = true;
                    runStart = s;
                    continue;
                }

                bool line = planting.Enclosure(s) >= Planting.LineThreshold;
                lowland++;
                if (line) lined++;
                if (line != previous)
                {
                    if (previous) longestLined = Math.Max(longestLined, s - runStart);
                    else longestOpen = Math.Max(longestOpen, s - runStart);
                    runStart = s;
                    previous = line;
                }
            }

            double share = (double)lined / lowland;
            Assert.That(share, Is.InRange(0.2, 0.6), "share of the lowland road lined with trees");
            Assert.That(longestOpen, Is.LessThan(2000.0), "the open stretches end");
            Assert.That(longestLined, Is.LessThan(1600.0), "the lined stretches end and the views open again");
        }

        [Test]
        public void TheEndlessScatterTable_IsLusher_AndTheFiniteOneIsUnchanged()
        {
            List<ScatterRule> finite = ScatterRules.Default(EndlessGenerator.TreeLine), endless = ScatterRules.Endless(EndlessGenerator.TreeLine);
            ScatterRule meadow = finite.First(r => r.family == PropRole.MeadowTree), bush = finite.First(r => r.family == PropRole.Bush);
            Assert.That(meadow.density, Is.EqualTo(2.5f), "finite world values are pinned (golden hashes)");
            Assert.That(bush.density, Is.EqualTo(16f));
            Assert.That(bush.biomes.meadow, Is.EqualTo(0.12f));
            Assert.That(endless.First(r => r.family == PropRole.MeadowTree).density, Is.GreaterThan(meadow.density));
            Assert.That(endless.First(r => r.family == PropRole.Bush).biomes.meadow, Is.GreaterThan(bush.biomes.meadow));
            Assert.That(endless.Any(r => PropRole.IsTree(r.family) && r.biomes.shore >= 1f && r.biomes.meadow == 0f), Is.True, "trees along the streams and lake shores");
            for (int i = 0; i < finite.Count; i++) Assert.That(endless.Any(r => r.family == finite[i].family), Is.True);
        }

        [Test]
        public void FlowerDetails_FollowTheFlowerPaint_OnlyOnTheEndlessRoad()
        {
            const int res = 64;
            var p = new WorldGenParams { seed = 11, worldSize = 128f, heightmapResolution = 65, hillAmplitude = 100f, window = new TileWindow(2048.0, 4096.0) };
            LandCover cover = LandCover.Create(res, 128f, -1f);
            for (int i = 0; i < res * res; i++) { cover.Height[i] = 30f; cover.Moisture[i] = 0.6f; }
            RoadDistanceField roads = RoadDistanceField.Build(new List<IReadOnlyList<Vector3>> { new[] { new Vector3(-900f, 0f, -900f), new Vector3(-899f, 0f, -900f) } }, res, 128f, 40f);
            var splat = new float[res, res, TerrainShaper.LayerCount];
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++) splat[z, x, x < res / 2 ? TerrainShaper.FlowerLayer : TerrainShaper.LushGrassLayer] = 1f;
            }

            (long painted, long plain) Flowers(List<DetailRule> rules, WorldGenParams wp)
            {
                long a = 0, b = 0;
                foreach (DetailLayer layer in DetailPlanner.Plan(wp, roads, cover, splat, new List<SettlementSite>(), new List<FieldPatch>(), PaletteKeys.Default, rules))
                {
                    if (layer.family != PropRole.Wildflowers && layer.family != PropRole.FlatFlowers && layer.family != PropRole.GenFlowers) continue;
                    for (int i = 0; i < layer.density.Length; i++)
                    {
                        if (i % res < res / 2) a += layer.density[i];
                        else b += layer.density[i];
                    }
                }

                return (a, b);
            }

            (long painted, long plain) = Flowers(DetailPlanner.EndlessRules(), p);
            Assert.That(painted, Is.GreaterThan(plain * 2), $"flowers on flower-meadow paint {painted}, on plain grass {plain}");
            Assert.That(plain, Is.GreaterThan(0), "a few flowers still dot the plain grass");
            Assert.That(DetailPlanner.DefaultRules().All(r => r.followFlowerPaint == 0f), Is.True, "the finite world's flowers keep their own patches");
        }

        [Test]
        public void Sward_HasBroadTonalDrifts_WithoutSmallBlotches()
        {
            // A uniform meadow 512 m across: the endless paint's dry share varies by swathes of ~150 m, with no step between
            // neighbouring 2 m texels; the finite paint stays as it was (only its own small patch noise).
            float Spread(bool endless, out float step)
            {
                const int res = 256;
                var p = new WorldGenParams { seed = 21, worldSize = 512f, heightmapResolution = 257, hillAmplitude = 100f, window = endless ? new TileWindow(8192.0, 16384.0) : null };
                LandCover cover = LandCover.Create(res, 512f, -1f);
                for (int i = 0; i < res * res; i++) { cover.Height[i] = 30f; cover.Moisture[i] = 0.58f; }
                RoadDistanceField roads = RoadDistanceField.Build(new List<IReadOnlyList<Vector3>> { new[] { new Vector3(-900f, 0f, -900f), new Vector3(-899f, 0f, -900f) } }, res, 512f, 40f);
                float[,,] splat = TerrainShaper.ComputeSplat(p, roads, cover, new List<SettlementSite>(), new List<FieldPatch>(), 0f, 0f);
                var dry = new List<float>();
                var steps = new List<float>();
                for (int z = 0; z < res; z++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float d = splat[z, x, TerrainShaper.DryGrassLayer];
                        dry.Add(d);
                        if (x > 0) steps.Add(Mathf.Abs(d - splat[z, x - 1, TerrainShaper.DryGrassLayer]));
                    }
                }

                dry.Sort();
                steps.Sort();
                step = steps[steps.Count * 99 / 100];
                return dry[dry.Count * 9 / 10] - dry[dry.Count / 10];
            }

            float endlessSpread = Spread(true, out float endlessStep), finiteSpread = Spread(false, out _);
            Assert.That(endlessSpread, Is.GreaterThan(0.3f), "lusher and drier swathes");
            Assert.That(endlessSpread, Is.GreaterThan(finiteSpread + 0.15f));
            // The lush-to-dry ground difference is ΔE 10, so a step of 0.08 between 2 m texels is under ΔE 1.
            Assert.That(endlessStep, Is.LessThan(0.08f), "no blotches: 99 % of neighbouring texels differ by under 8 % dry grass");
        }
    }
}
