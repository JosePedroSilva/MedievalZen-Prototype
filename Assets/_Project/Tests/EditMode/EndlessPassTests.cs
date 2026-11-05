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
    /// The endless road's mountain passes (2026-10-05): the range's walls are grey rock broken into cliff bands with ledges, gullies
    /// and talus fans instead of smooth brown slopes with faint stripes; scree and heath paint, pines on the lower flanks thinning
    /// upward, the pass's own grey rocks; the road on a shelf where the valley allows and higher peaks behind the walls. The road,
    /// its grade, the col's walls and the foothills stay as they were. Measured before (seeds 12345, 42, 777, the range of the
    /// first pass): walls 19-22 % rock, 0 % scree, 77-80 % one dry-grass paint (a single patch of 0.85-1.2 km²), 0.44-0.55 ledges
    /// per 100 m of face, 1.8-2.0 pines per hectare on the lower flanks. Pure (no Unity objects).
    /// </summary>
    public sealed class EndlessPassTests
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

        /// <summary>Distinct near tiles beside the road through the range (the part of the pass past its foothills), out to 384 m.</summary>
        private static List<TileCoord> RangeTiles(EndlessGenerator g, Region pass)
        {
            float ts = g.Params.tileSize;
            var set = new List<TileCoord>();
            for (double s = pass.Start + TerrainField.FoothillReach; s <= pass.End - TerrainField.FoothillReach; s += 160.0)
            {
                RoadPoint rp = g.Road(s);
                double rad = rp.HeadingDeg * Math.PI / 180.0;
                foreach (double v in new[] { -300.0, -128.0, 0.0, 128.0, 300.0 })
                {
                    TileCoord c = TileCoord.Containing(rp.X + Math.Cos(rad) * v, rp.Z - Math.Sin(rad) * v, ts);
                    if (!set.Contains(c)) set.Add(c);
                }
            }

            return set;
        }

        /// <summary>Road distance, lateral offset and height above the road of absolute (x, z).</summary>
        private static (double s, double v, float above) Locate(EndlessGenerator g, double x, double z, float h)
        {
            TerrainField.Row row = g.Terrain.BaseRow(z);
            return (row.S, (x - row.RoadX) * row.Cos, h - row.Road);
        }

        [Test]
        public void Pass_IsDeterministic()
        {
            var a = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            var b = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            Region pass = FirstPass(a);
            RoadPoint rp = a.Road(pass.Col - 400.0);
            TileCoord c = TileCoord.Containing(rp.X + 90.0, rp.Z, a.Params.tileSize);
            TileBlueprint ta = a.GenerateTile(c, TileLod.Near), tb = b.GenerateTile(c, TileLod.Near);
            Assert.That(ta.Heights.Cast<float>().SequenceEqual(tb.Heights.Cast<float>()), Is.True, "heights");
            Assert.That(ta.Splat.Cast<float>().SequenceEqual(tb.Splat.Cast<float>()), Is.True, "paint");
            Assert.That(ta.Trees.Count, Is.EqualTo(tb.Trees.Count), "trees");
            for (int i = 0; i < ta.Trees.Count; i++) Assert.That(ta.Trees[i].position, Is.EqualTo(tb.Trees[i].position));
            Assert.That(ta.Props.Select(p => p.role + p.position), Is.EqualTo(tb.Props.Select(p => p.role + p.position)), "props");
            Assert.That(ta.MountainPaint, Is.True, "a range tile uses the mountain layers");
            for (int i = 0; i < 200; i++)
            {
                double x = rp.X - 400.0 + 4.1 * i, z = rp.Z + 3.7 * i - 300.0;
                Assert.That(a.Height(x, z), Is.EqualTo(b.Height(x, z)));
            }
        }

        [Test, Category("Slow")]
        public void Walls_AreRockScreeAndHeath_NotOneBrownSlope([Values(12345, 42, 777)] int seed)
        {
            // Measured: walls (8 m and more above the road, 20 m and more from it) 21-24 % rock, 8-9 % scree, 67-70 % heath and grass;
            // before 19-22 % rock, no scree, 77-80 % dry grass. The largest disc of one paint: 54-84 m across (before 209-250 m).
            EndlessGenerator g = Gen(seed);
            Region pass = FirstPass(g);
            double rock = 0, mountainRock = 0, scree = 0, turf = 0, n = 0;
            float widest = 0f;
            int mountainTiles = 0, tiles = 0;
            foreach (TileCoord c in RangeTiles(g, pass))
            {
                TileBlueprint t = g.GenerateTile(c, TileLod.Near);
                tiles++;
                if (t.MountainPaint) mountainTiles++;
                Assert.That(t.Splat.GetLength(2), Is.EqualTo(TerrainShaper.EndlessLayerCount));
                int res = t.Splat.GetLength(0);
                float texel = t.TileSize / res;
                double ox = c.OriginX(t.TileSize), oz = c.OriginZ(t.TileSize);
                var dominant = new int[res * res];
                for (int z = 0; z < res; z++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        dominant[z * res + x] = -1;
                        for (int l = 0; l < TerrainShaper.EndlessLayerCount; l++)
                        {
                            if (t.Splat[z, x, l] >= 0.6f) dominant[z * res + x] = l;
                        }

                        if ((x & 3) != 0 || (z & 3) != 0) continue;
                        double ax = ox + (x + 0.5) * texel, az = oz + (z + 0.5) * texel;
                        int hi = Mathf.Min(t.Heights.GetLength(0) - 1, Mathf.RoundToInt((x + 0.5f) * texel / (t.TileSize / (t.Heights.GetLength(0) - 1))));
                        int hj = Mathf.Min(t.Heights.GetLength(0) - 1, Mathf.RoundToInt((z + 0.5f) * texel / (t.TileSize / (t.Heights.GetLength(0) - 1))));
                        var (s, v, above) = Locate(g, ax, az, t.Heights[hj, hi] * t.HeightRange);
                        if (TerrainField.PassEnvelope(pass, s) < 0.99f || Math.Abs(v) < 20.0 || Math.Abs(v) > 400.0 || above < 8f) continue;
                        n++;
                        rock += t.Splat[z, x, TerrainShaper.RockLayer] + t.Splat[z, x, TerrainShaper.MountainRockLayer];
                        mountainRock += t.Splat[z, x, TerrainShaper.MountainRockLayer];
                        scree += t.Splat[z, x, TerrainShaper.ScreeLayer];
                        turf += t.Splat[z, x, TerrainShaper.LushGrassLayer] + t.Splat[z, x, TerrainShaper.DryGrassLayer] + t.Splat[z, x, TerrainShaper.HeathLayer]
                                + t.Splat[z, x, TerrainShaper.FlowerLayer] + t.Splat[z, x, TerrainShaper.ForestFloorLayer];
                    }
                }

                widest = Mathf.Max(widest, WidestDisc(dominant, res) * texel);
            }

            Assert.That(n, Is.GreaterThan(1000), "wall samples");
            Assert.That(mountainTiles, Is.EqualTo(tiles), "every range tile uses the mountain layers");
            Assert.That(rock / n, Is.InRange(0.15, 0.40), "bare rock on the walls");
            Assert.That(mountainRock / rock, Is.GreaterThan(0.9), "the range's rock is the grey mountain rock");
            Assert.That(scree / n, Is.InRange(0.04, 0.16), "scree on the walls");
            Assert.That(turf / n, Is.GreaterThan(0.5), "heath and grass on the walls");
            Assert.That(widest, Is.LessThan(130f), "largest disc of one paint, metres across");
        }

        /// <summary>Diameter (texels) of the largest disc inside a region of one dominant paint (two-pass chamfer distance; tile edges are boundaries).</summary>
        private static float WidestDisc(int[] dominant, int res)
        {
            var d = new float[res * res];
            for (int i = 0; i < d.Length; i++) d[i] = dominant[i] < 0 ? 0f : 1e6f;
            float best = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0; k < res * res; k++)
                {
                    int idx = pass == 0 ? k : res * res - 1 - k;
                    int x = idx % res, z = idx / res;
                    if (d[idx] == 0f) continue;
                    int step = pass == 0 ? -1 : 1;
                    float m = x == 0 || z == 0 || x == res - 1 || z == res - 1 ? 1f : d[idx];
                    int[] nx = { x + step, x, x + step, x - step };
                    int[] nz = { z, z + step, z + step, z + step };
                    float[] cost = { 1f, 1f, 1.414f, 1.414f };
                    for (int q = 0; q < 4; q++)
                    {
                        if (nx[q] < 0 || nz[q] < 0 || nx[q] >= res || nz[q] >= res) continue;
                        int j = nz[q] * res + nx[q];
                        m = Mathf.Min(m, dominant[j] == dominant[idx] ? d[j] + cost[q] : 1f);
                    }

                    d[idx] = m;
                    if (pass == 1) best = Mathf.Max(best, m);
                }
            }

            return 2f * best;
        }

        [Test, Category("Slow")]
        public void Walls_AreBrokenIntoLedgesAndRisers([Values(12345, 42, 777)] int seed)
        {
            // Across the road every 50 m of the range: faces (slope averaged over ±14 m of 28 degrees or more, ledges of up to 16 m
            // bridged) carry 1.6-2.0 ledges (4 m and more under 22 degrees) and 2.0-2.4 risers (4 m and more over 50 degrees) per
            // 100 m, and 18-26 % of the face sections have no ledge; before 0.44-0.55 ledges, 0.9-1.2 risers, 64-67 % without.
            EndlessGenerator g = Gen(seed);
            Region pass = FirstPass(g);
            double length = 0;
            int ledges = 0, risers = 0, faces = 0, bare = 0;
            for (double s = pass.Start + TerrainField.FoothillReach; s <= pass.End - TerrainField.FoothillReach; s += 50.0)
            {
                RoadPoint rp = g.Road(s);
                double rad = rp.HeadingDeg * Math.PI / 180.0;
                double nx = Math.Cos(rad), nz = -Math.Sin(rad);
                foreach (int side in new[] { 1, -1 })
                {
                    var slope = new List<float>();
                    var h = new List<float>();
                    for (double v = 8.0; v <= 600.0; v += 2.0)
                    {
                        double x = rp.X + nx * v * side, z = rp.Z + nz * v * side;
                        float gx = g.Height(x + 1.0, z) - g.Height(x - 1.0, z), gz = g.Height(x, z + 1.0) - g.Height(x, z - 1.0);
                        slope.Add(Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz) * 0.5f) * Mathf.Rad2Deg);
                        h.Add(g.Height(x, z));
                    }

                    int count = slope.Count;
                    var face = new bool[count];
                    for (int i = 0; i < count; i++)
                    {
                        float m = 0f;
                        int k = 0;
                        for (int j = Math.Max(0, i - 7); j <= Math.Min(count - 1, i + 7); j++, k++) m += slope[j];
                        face[i] = m / k >= 28f;
                    }

                    for (int i = 1; i < count; i++)
                    {
                        if (face[i] || !face[i - 1]) continue;
                        int j = i;
                        while (j < count && !face[j]) j++;
                        if (j < count && j - i <= 8) for (int q = i; q < j; q++) face[q] = true;
                        i = j;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        if (!face[i] || (i > 0 && face[i - 1])) continue;
                        int e = i;
                        while (e + 1 < count && face[e + 1]) e++;
                        if ((e - i) * 2 < 20) continue;
                        faces++;
                        int found = 0, run = 0, steep = 0;
                        for (int q = i; q <= e + 1; q++)
                        {
                            if (q < e) length += Math.Sqrt(4.0 + (h[q + 1] - h[q]) * (h[q + 1] - h[q]));
                            bool flat = q <= e && slope[q] < 22f, cliff = q <= e && slope[q] >= 50f;
                            if (flat) run++;
                            else
                            {
                                if (run >= 2) found++;
                                run = 0;
                            }

                            if (cliff) steep++;
                            else
                            {
                                if (steep >= 2) risers++;
                                steep = 0;
                            }
                        }

                        ledges += found;
                        if (found == 0) bare++;
                    }
                }
            }

            Assert.That(faces, Is.GreaterThan(20));
            Assert.That(100.0 * ledges / length, Is.GreaterThan(1.2), "ledges per 100 m of face");
            Assert.That(100.0 * risers / length, Is.GreaterThan(1.5), "cliff risers per 100 m of face");
            Assert.That((double)bare / faces, Is.LessThan(0.35), "face sections without a ledge");
        }

        [Test, Category("Slow")]
        public void Pines_StandOnTheLowerFlanks_ThinningUpward_OffTheRoad([Values(12345, 42, 777)] int seed)
        {
            // Measured: 22-23 pines per hectare on the lower flanks (3-100 m above the road), 29-32 below 35 m, about 1 above 100 m;
            // before 1.8-2.0 (the scatter's pines stop at an altitude the passes start at).
            EndlessGenerator g = Gen(seed);
            Region pass = FirstPass(g);
            float corridor = g.Params.roadWidth * 0.5f + 8f;
            var low = new int[2];
            var area = new double[2];
            foreach (TileCoord c in RangeTiles(g, pass))
            {
                TileBlueprint t = g.GenerateTile(c, TileLod.Near);
                double ox = c.OriginX(t.TileSize), oz = c.OriginZ(t.TileSize);
                int hres = t.Heights.GetLength(0);
                float spacing = t.TileSize / (hres - 1);
                for (int j = 0; j < hres - 1; j++)
                {
                    for (int i = 0; i < hres - 1; i++)
                    {
                        float hc = t.Heights[j, i] * t.HeightRange;
                        var (s, v, above) = Locate(g, ox + (i + 0.5) * spacing, oz + (j + 0.5) * spacing, hc);
                        if (TerrainField.PassEnvelope(pass, s) < 0.99f || Math.Abs(v) < 12.0 || Math.Abs(v) > 400.0) continue;
                        if (above >= 3f && above < 35f) area[0] += spacing * spacing;
                        else if (above >= 100f) area[1] += spacing * spacing;
                    }
                }

                foreach (TreePlacement tree in t.Trees)
                {
                    double x = ox + tree.position.x, z = oz + tree.position.z;
                    var (s, v, above) = Locate(g, x, z, tree.position.y);
                    double sNear = g.NearestS(x, z, s);
                    RoadPoint rp = g.Road(sNear);
                    double d = Math.Sqrt((x - rp.X) * (x - rp.X) + (z - rp.Z) * (z - rp.Z));
                    if (PropRole.IsTree(tree.role)) Assert.That(d, Is.GreaterThan(corridor - 0.5), $"{tree.role} {d:0.0} m from the road at s {sNear:0}");
                    if (tree.role != PropRole.PineTree || TerrainField.PassEnvelope(pass, s) < 0.99f || Math.Abs(v) < 12.0 || Math.Abs(v) > 400.0) continue;
                    if (above >= 3f && above < 35f) low[0]++;
                    else if (above >= 100f) low[1]++;
                }
            }

            double lowDensity = low[0] / (area[0] / 10000.0), highDensity = low[1] / Math.Max(1.0, area[1] / 10000.0);
            Assert.That(area[0], Is.GreaterThan(20000.0));
            Assert.That(lowDensity, Is.GreaterThan(12.0), "pines per hectare 3-35 m above the road");
            Assert.That(highDensity, Is.LessThan(0.25 * lowDensity), "fewer far up the walls");
        }

        [Test]
        public void Road_KeepsItsLine_ItsGrade_AndAClearBed()
        {
            foreach (int seed in new[] { 12345, 777 })
            {
                EndlessGenerator g = Gen(seed);
                Region pass = FirstPass(g);
                float maxGrade = g.Params.maxRoadGrade;
                var crossings = new List<Crossing>();
                g.Plan.Crossings.Crossings(pass.Start, pass.End, crossings);
                RoadPoint previous = g.Road(pass.Start);
                for (double s = pass.Start + 4.0; s <= pass.End; s += 4.0)
                {
                    RoadPoint rp = g.Road(s);
                    if (crossings.Any(c => Math.Abs(c.S - s) < 25.0)) continue;   // fords dip, bridges carry the road over the channel
                    Assert.That(Mathf.Abs(rp.Grade), Is.LessThanOrEqualTo(maxGrade + 1e-4f), $"grade at {s:0}");
                    Assert.That(g.Height(rp.X, rp.Z), Is.EqualTo(rp.Height).Within(0.06f), $"the ground is the road at {s:0}");
                    double rad = rp.HeadingDeg * Math.PI / 180.0;
                    double nx = Math.Cos(rad), nz = -Math.Sin(rad);
                    foreach (int side in new[] { 1, -1 })
                    {
                        // The bed and its verge stay level with the road; a shelf only drops beyond them.
                        float edge = g.Height(rp.X + nx * 2.4 * side, rp.Z + nz * 2.4 * side);
                        Assert.That(edge, Is.EqualTo(rp.Height).Within(0.15f), $"road edge at {s:0}");
                        float verge = g.Height(rp.X + nx * 8.0 * side, rp.Z + nz * 8.0 * side);
                        Assert.That(verge, Is.GreaterThan(rp.Height - 2.5f), $"verge at {s:0}");
                    }

                    previous = rp;
                }

                _ = previous;
            }
        }

        [Test, Category("Slow")]
        public void NothingStandsOnTheRoad([Values(12345, 42)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            Region pass = FirstPass(g);
            float corridor = g.Params.roadWidth * 0.5f + 8f;
            foreach (TileCoord c in RangeTiles(g, pass))
            {
                TileBlueprint t = g.GenerateTile(c, TileLod.Near);
                double ox = c.OriginX(t.TileSize), oz = c.OriginZ(t.TileSize);
                foreach (PropPlacement prop in t.Props)
                {
                    if (!PropRole.IsRock(prop.role)) continue;
                    double x = ox + prop.position.x, z = oz + prop.position.z;
                    RoadPoint rp = g.Road(g.NearestS(x, z, g.Terrain.BaseRow(z).S));
                    double d = Math.Sqrt((x - rp.X) * (x - rp.X) + (z - rp.Z) * (z - rp.Z));
                    Assert.That(d, Is.GreaterThan(corridor - 2f), $"{prop.role} {d:0.0} m from the road");
                }
            }
        }

        [Test, Category("Slow")]
        public void Range_UsesItsOwnGreyRocks([Values(12345, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            Region pass = FirstPass(g);
            int own = 0, meadow = 0;
            foreach (TileCoord c in RangeTiles(g, pass))
            {
                TileBlueprint t = g.GenerateTile(c, TileLod.Near);
                double ox = c.OriginX(t.TileSize), oz = c.OriginZ(t.TileSize);
                foreach (PropPlacement prop in t.Props)
                {
                    if (!PropRole.IsRock(prop.role) || prop.role == PropRole.RockSmallPile || prop.role == PropRole.Mound) continue;
                    double z = oz + prop.position.z;
                    double s = g.Terrain.BaseRow(z).S;
                    if (TerrainField.PassEnvelope(pass, s) < 0.99f) continue;
                    if (PropRole.IsPassRock(prop.role)) own++;
                    else meadow++;
                    Assert.That(g.Params.tileSize, Is.GreaterThan(0f));
                    _ = ox;
                }
            }

            Assert.That(own, Is.GreaterThan(50), "the range's grey rocks");
            Assert.That(meadow, Is.LessThanOrEqualTo(own / 20), "Meadow rocks left in the range");
            foreach (string family in new[] { PropRole.PassCliff, PropRole.PassBoulder })
            {
                PaletteKeys keys = PaletteKeys.Default;
                Assert.That(keys.Count(family), Is.GreaterThan(1));
                for (int v = 0; v < keys.Count(family); v++) Assert.That(keys.TryGetMetrics(family, v, out PropMetrics m) && m.IsValid, Is.True, $"{family} {v} measured");
            }

            Assert.That(PaletteKeys.Default.TryGetMetrics(PropRole.Cliff, 0, out PropMetrics meadowCliff) && meadowCliff.Height < 10f, Is.True,
                "the Meadow cliffs keep their own metrics (the Fantasy Kingdom rocks repeat their names)");
        }

        [Test]
        public void Shelf_DropsBesideTheRoad_WhereTheValleyAllows()
        {
            // Measured: 16-24 % of the road 600 m and more inside the pass runs on a shelf (the ground 45 m out at least 6 m below the
            // ground 12 m out), drops up to 12-16 m; before none.
            EndlessGenerator g = Gen(42);
            Region pass = FirstPass(g);
            int n = 0, shelf = 0;
            var crossings = new List<Crossing>();
            g.Plan.Crossings.Crossings(pass.Start, pass.End, crossings);
            for (double s = pass.Start + 600.0; s <= pass.End - 600.0; s += 10.0)
            {
                RoadPoint rp = g.Road(s);
                double rad = rp.HeadingDeg * Math.PI / 180.0;
                double nx = Math.Cos(rad), nz = -Math.Sin(rad);
                bool drop = false;
                foreach (int side in new[] { 1, -1 })
                {
                    float near = g.Height(rp.X + nx * 12.0 * side, rp.Z + nz * 12.0 * side);
                    float far = g.Height(rp.X + nx * 45.0 * side, rp.Z + nz * 45.0 * side);
                    if (near - far >= 6f) drop = true;
                }

                n++;
                if (drop) shelf++;
                if (Math.Abs(s - pass.Col) < TerrainField.ShelfColClearance - 120.0 || crossings.Any(c => Math.Abs(s - c.S) < TerrainField.ShelfCrossingClearance - 80.0))
                {
                    Assert.That(g.Terrain.ShelfAt(s, null, out _), Is.EqualTo(0f), $"no shelf by the col or a crossing ({s:0})");
                }
            }

            Assert.That((double)shelf / n, Is.GreaterThan(0.1), "road on a shelf");
        }

        [Test]
        public void Foothills_KeepTheirShape_AtTheEnds()
        {
            // The new rock architecture, talus, shelf and back ranges all scale with the range (PassEnvelope), so at a pass's ends the
            // foothills of 2026-10-02 are untouched: no talus, no gullies, no shelf, no mountain paint.
            foreach (int seed in new[] { 12345, 42, 777 })
            {
                EndlessGenerator g = Gen(seed);
                Region pass = FirstPass(g);
                foreach (double s in new[] { pass.Start + 1.0, pass.End - 1.0 })
                {
                    TerrainField.Row row = g.Terrain.RowAt(g.Road(s).Z);
                    Assert.That(g.Terrain.ShelfAt(s, null, out _), Is.EqualTo(0f));
                    for (double v = -400.0; v <= 400.0; v += 8.0)
                    {
                        double x = row.RoadX + v;
                        g.Terrain.Height(row, x, out float talus, out float gully);
                        Assert.That(talus, Is.LessThan(0.02f), $"talus at {s:0}, {v:0}");
                        Assert.That(gully, Is.LessThan(0.02f), $"gully at {s:0}, {v:0}");
                        g.Terrain.Cover(row, x, g.Terrain.Height(row, x), 0.3f, out _, out _, out _, out _, out float mountain);
                        Assert.That(mountain, Is.LessThan(0.05f), "mountain paint");
                    }
                }
            }
        }

        [Test]
        public void MountainColours_ReadAsGreyRockPaleScreeAndHeath()
        {
            Vector3 warm = GroundLook.Lab(GroundLook.LayerColor(TerrainShaper.RockLayer));
            Vector3 rock = GroundLook.Lab(GroundLook.LayerColor(TerrainShaper.MountainRockLayer));
            Vector3 scree = GroundLook.Lab(GroundLook.LayerColor(TerrainShaper.ScreeLayer));
            Vector3 heath = GroundLook.Lab(GroundLook.LayerColor(TerrainShaper.HeathLayer));
            Vector3 dry = GroundLook.Lab(GroundLook.LayerColor(TerrainShaper.DryGrassLayer));
            Assert.That(Mathf.Abs(rock.z), Is.LessThan(3f), "the mountain rock is neutral grey (b*)");
            Assert.That(warm.z, Is.GreaterThan(10f), "the shared rock stays warm");
            Assert.That(Mathf.Abs(rock.x - warm.x), Is.LessThan(2f), "as light as the shared rock, so it lights the same");
            Assert.That(scree.x, Is.GreaterThan(rock.x + 4f), "scree paler than the rock");
            Assert.That((heath - dry).magnitude, Is.GreaterThan(15f), "heath distinct from the dry grass");
            Assert.That((heath - rock).magnitude, Is.GreaterThan(20f), "heath distinct from the rock");
            Assert.That(GroundLook.LayerColors().Length, Is.EqualTo(TerrainShaper.EndlessLayerCount));
            Assert.That(FarTileMeshData.ZoneCanopy, Is.EqualTo(TerrainShaper.EndlessLayerCount), "distant zones after every endless channel");
        }

        [Test, Category("Slow")]
        public void NearAndDistantPaint_Agree_InTheRange()
        {
            // Measured over the range tiles of three seeds: rock near 21-25 %, mid 20-23 %, far 18-22 %; scree near 10-12 %, mid 10-12 %,
            // far 8-9 % (distant scree is lifted, TerrainShaper.MountainScreeFarGain).
            EndlessGenerator g = Gen(12345);
            Region pass = FirstPass(g);
            var sums = new Dictionary<TileLod, double[]>();
            foreach (TileLod lod in new[] { TileLod.Near, TileLod.Mid, TileLod.Far }) sums[lod] = new double[3];
            int tiles = 0;
            foreach (TileCoord c in RangeTiles(g, pass))
            {
                tiles++;
                foreach (TileLod lod in new[] { TileLod.Near, TileLod.Mid, TileLod.Far })
                {
                    TileBlueprint t = g.GenerateTile(c, lod);
                    int res = t.Splat.GetLength(0);
                    double rock = 0, scree = 0;
                    for (int z = 0; z < res; z++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            rock += t.Splat[z, x, TerrainShaper.RockLayer] + t.Splat[z, x, TerrainShaper.MountainRockLayer];
                            scree += t.Splat[z, x, TerrainShaper.ScreeLayer];
                        }
                    }

                    sums[lod][0] += rock / (res * res);
                    sums[lod][1] += scree / (res * res);
                }
            }

            double Share(TileLod lod, int i) => sums[lod][i] / tiles;
            Assert.That(Math.Abs(Share(TileLod.Mid, 0) - Share(TileLod.Near, 0)), Is.LessThan(0.03), "rock, mid tiles");
            Assert.That(Math.Abs(Share(TileLod.Far, 0) - Share(TileLod.Near, 0)), Is.LessThan(0.04), "rock, far tiles");
            Assert.That(Math.Abs(Share(TileLod.Mid, 1) - Share(TileLod.Near, 1)), Is.LessThan(0.03), "scree, mid tiles");
            Assert.That(Math.Abs(Share(TileLod.Far, 1) - Share(TileLod.Near, 1)), Is.LessThan(0.04), "scree, far tiles");
        }
    }
}
