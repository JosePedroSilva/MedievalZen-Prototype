using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Roads;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The endless road's pure generator (phase E1): the finite world is unchanged by the shared-stage refactor (golden
    /// hashes), every answer is deterministic whatever the order or thread, neighbouring tiles meet exactly, the road keeps
    /// its grade, stays dry and bends gently, regions and inns follow their rules, waypoint ids are stable, altitudes stay
    /// in range for ±100 km, nothing stands in the road corridor, rocks are seated, and a tile 2,000 km out is as exact as
    /// one at the start. Pure (no Unity objects).
    /// </summary>
    public sealed class EndlessGeneratorTests
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

        private static TileCoord TileAt(EndlessGenerator g, double s, double lateral = 0.0)
        {
            RoadPoint rp = g.Road(s);
            double rad = rp.HeadingDeg * Math.PI / 180.0;
            return TileCoord.Containing(rp.X + Math.Cos(rad) * lateral, rp.Z - Math.Sin(rad) * lateral, g.Params.tileSize);
        }

        // ------------------------------------------------------------------ the finite world is unchanged

        /// <summary>
        /// Hashes of the default finite world before the refactor (computed 2026-10-01 from the unmodified stages). Parts: heights,
        /// splat, details, trees, props, settlements, roads, landmarks. CoreCLR and Mono differ (Unity's Mono evaluates float arithmetic
        /// in double, rounding only where the JIT stores a value, and has its own libm), so each runtime has its own set.
        /// <see cref="GoldenMono"/> was re-recorded 2026-10-06: the set of 2026-10-01 came from the standalone MonoBleedingEdge\bin\mono.exe,
        /// a different runtime build from the one the editor embeds, and never held in the editor. The new set is the 2026-10-02 sources
        /// (the earliest copy kept) run in the editor's own runtime (EmbedRuntime\mono-2.0-bdwgc.dll, float32 off), which reproduces
        /// the editor's Test Runner bit for bit; today's stages give the same. See Docs/DESIGN.md, "Golden hashes under Mono".
        /// </summary>
        private static readonly Dictionary<int, ulong[]> GoldenCoreClr = new Dictionary<int, ulong[]>
        {
            { 12345, new[] { 0xC58F1E3BDB233D1AUL, 0x6BF3A1F3F2DEA3A0UL, 0x0EBCBAF99BCFE856UL, 0x00C70AFF39D158E4UL, 0xF45B6CC441ABA02CUL, 0xF52276F51C395DB9UL, 0xE30461D377348194UL, 0x394FCF2BA48D3427UL } },
            { 42, new[] { 0x934E09C87D466AD9UL, 0xDCACF25019831608UL, 0x7EBBF90F61285283UL, 0x57F25E1F6ADE7C8DUL, 0x9DAA50EBBF2BBAE1UL, 0xAC47DA965649C185UL, 0x5A6312F01741FD29UL, 0x3FD72049642A4272UL } },
            { 777, new[] { 0xBA9B571D46AA8C17UL, 0x9F908EC9DDDB6AADUL, 0xA670EBE586786B20UL, 0xFEE5063631953FC4UL, 0x3EB05E8925BD1670UL, 0x67D456D3BF75B2A7UL, 0xC82974C2ACC6EDA5UL, 0x456ABC21597B5942UL } },
        };

        private static readonly Dictionary<int, ulong[]> GoldenMono = new Dictionary<int, ulong[]>
        {
            { 12345, new[] { 0xBD7F99885A2D4328UL, 0xD0728A71FAAD1E3BUL, 0xEE5116E71B70EB1EUL, 0x3FD663A1120F40AAUL, 0x479CC0A038D2C83BUL, 0x6CC1F5CF3F7F31B9UL, 0x86B0865649CBA5FEUL, 0x71AE61668A19D39BUL } },
            { 42, new[] { 0xFD9E2AD55F457024UL, 0xE61BF5F9DA0A9250UL, 0x9B3309C5D7BCE72EUL, 0x86015C9504571902UL, 0xC1EBDC2F1D6B402CUL, 0xAC12AF1236081D19UL, 0x441F04C5E3CBF035UL, 0xDE59ADF2DBDE14A9UL } },
            { 777, new[] { 0x535D2FFF3CEEEB7CUL, 0xD176AFC8981C675AUL, 0x5441F061B841B855UL, 0x106F9E7FD33E79F4UL, 0x704FFB6BD31CBC8DUL, 0x7AC312EA2E9F25F3UL, 0x0FBFCB91788AC171UL, 0x8BBF023128479C4FUL } },
        };

        [Test, Category("Slow")]
        public void FiniteWorld_IsBitIdenticalToBeforeTheRefactor([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = WorldGenerator.Generate(new WorldGenParams { seed = seed }, PaletteKeys.Default);
            ulong[] actual = FiniteWorldHash.Compute(bp);
            bool mono = Type.GetType("Mono.Runtime") != null;
            ulong[] expected = (mono ? GoldenMono : GoldenCoreClr)[seed];
            var mismatches = new List<string>();
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected[i]) mismatches.Add($"{FiniteWorldHash.Parts[i]}: 0x{actual[i]:X16} (expected 0x{expected[i]:X16})");
            }

            Assert.That(mismatches, Is.Empty, $"Finite world seed {seed} changed ({(mono ? "Mono" : "CoreCLR")} hashes): " + string.Join("; ", mismatches) +
                ". If the change is intended, recompute the hashes with FiniteWorldHash.Compute.");
        }

        // ------------------------------------------------------------------ determinism and seams

        [Test]
        public void Tiles_AreIdentical_AloneAfterNeighbours_ShuffledAndConcurrent()
        {
            const int seed = 42;
            TileCoord centre = TileAt(Gen(seed), 2300.0);
            var coords = new List<TileCoord>();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++) coords.Add(new TileCoord(centre.X + dx, centre.Z + dz));
            }

            // Alone, on a fresh generator.
            TileBlueprint alone = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default).GenerateTile(centre, TileLod.Near);

            // After its neighbours, in shuffled order, on another fresh generator.
            var shuffled = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            var order = coords.OrderBy(c => (c.X * 7919 + c.Z * 104729) % 13).ToList();
            var sequential = order.ToDictionary(c => c, c => shuffled.GenerateTile(c, TileLod.Near));

            // Concurrently on 8 threads, on a third fresh generator.
            var concurrent = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            var results = new TileBlueprint[coords.Count];
            Parallel.For(0, coords.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => results[i] = concurrent.GenerateTile(coords[i], TileLod.Near));

            string a = Signature(alone);
            Assert.That(Signature(sequential[centre]), Is.EqualTo(a), "Tile generated after its neighbours differs from the tile generated alone.");
            Assert.That(Signature(results[coords.IndexOf(centre)]), Is.EqualTo(a), "Tile generated concurrently differs from the tile generated alone.");
            for (int i = 0; i < coords.Count; i++)
            {
                Assert.That(Signature(results[i]), Is.EqualTo(Signature(sequential[coords[i]])), $"Tile {coords[i]} differs between sequential and concurrent generation.");
            }
        }

        [Test]
        public void RoadRegionWaypointsAndHeight_DoNotDependOnCallOrder()
        {
            var forward = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            var backward = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            var samples = new List<double>();
            for (double s = -20000.0; s <= 20000.0; s += 1234.5) samples.Add(s);
            var a = samples.Select(s => (forward.Road(s), forward.Region(s).Name, forward.Height(forward.Road(s).X + 37.0, forward.Road(s).Z))).ToList();
            var b = samples.AsEnumerable().Reverse().Select(s => (backward.Road(s), backward.Region(s).Name, backward.Height(backward.Road(s).X + 37.0, backward.Road(s).Z))).Reverse().ToList();
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Item1.X, Is.EqualTo(a[i].Item1.X));
                Assert.That(b[i].Item1.Z, Is.EqualTo(a[i].Item1.Z));
                Assert.That(b[i].Item1.Height, Is.EqualTo(a[i].Item1.Height));
                Assert.That(b[i].Item2, Is.EqualTo(a[i].Item2));
                Assert.That(b[i].Item3, Is.EqualTo(a[i].Item3));
            }

            var wa = new List<Waypoint>();
            var wb = new List<Waypoint>();
            forward.Waypoints(-20000.0, 20000.0, wa);
            for (double s = 20000.0; s > -20000.0; s -= 3000.0) backward.Waypoints(Math.Max(-20000.0, s - 3000.0), s, wb);
            var ka = wa.Select(w => (w.Id, w.Kind, w.S, w.X, w.Z, w.Name)).Distinct().OrderBy(k => k.S).ThenBy(k => k.Id).ToList();
            var kb = wb.Select(w => (w.Id, w.Kind, w.S, w.X, w.Z, w.Name)).Distinct().OrderBy(k => k.S).ThenBy(k => k.Id).ToList();
            Assert.That(kb, Is.EqualTo(ka), "Waypoints differ when queried in other windows or another order.");
            Assert.That(ka.Select(k => k.Id).Distinct().Count(), Is.EqualTo(ka.Count), "Waypoint ids must be unique.");
        }

        [Test]
        public void NeighbouringTiles_ShareBitIdenticalEdges([Values(TileLod.Near, TileLod.Far)] TileLod lod)
        {
            EndlessGenerator g = Gen(12345);
            foreach (double s in new[] { 800.0, 9500.0 })
            {
                TileCoord c = TileAt(g, s);
                TileBlueprint t = g.GenerateTile(c, lod);
                TileBlueprint east = g.GenerateTile(new TileCoord(c.X + 1, c.Z), lod);
                TileBlueprint north = g.GenerateTile(new TileCoord(c.X, c.Z + 1), lod);
                int n = t.Heights.GetLength(0);
                for (int i = 0; i < n; i++)
                {
                    Assert.That(east.Heights[i, 0], Is.EqualTo(t.Heights[i, n - 1]), $"East edge row {i} differs ({lod}, s {s}).");
                    Assert.That(north.Heights[0, i], Is.EqualTo(t.Heights[n - 1, i]), $"North edge column {i} differs ({lod}, s {s}).");
                }
            }
        }

        [Test]
        public void Tiles_AreWellFormed()
        {
            EndlessGenerator g = Gen(12345);
            EndlessParams p = g.Params;
            TileBlueprint near = g.GenerateTile(TileAt(g, 1500.0), TileLod.Near);
            Assert.That(near.Heights.GetLength(0), Is.EqualTo(p.nearHeightResolution));
            Assert.That(near.Splat.GetLength(0), Is.EqualTo(p.nearSplatResolution));
            Assert.That(near.Splat.GetLength(2), Is.EqualTo(TerrainShaper.EndlessLayerCount));
            Assert.That(near.DetailResolution, Is.EqualTo(p.nearDetailResolution));
            Assert.That(near.Details.Count, Is.InRange(1, 16), "Detail layers per tile (non-empty, variants folded).");
            foreach (DetailLayer layer in near.Details) Assert.That(layer.density.Any(d => d > 0), Is.True, "Empty detail layers are not emitted.");
            for (int z = 0; z < p.nearSplatResolution; z += 7)
            {
                for (int x = 0; x < p.nearSplatResolution; x += 7)
                {
                    float sum = 0f;
                    for (int l = 0; l < TerrainShaper.EndlessLayerCount; l++) sum += near.Splat[z, x, l];
                    Assert.That(sum, Is.EqualTo(1f).Within(1e-3f));
                }
            }

            foreach (float h in near.Heights) Assert.That(h, Is.InRange(0f, 1f));
            TileBlueprint far = g.GenerateTile(TileAt(g, 1500.0, 900.0), TileLod.Far);
            Assert.That(far.Heights.GetLength(0), Is.EqualTo(p.farHeightResolution));
            Assert.That(far.Splat.GetLength(0), Is.EqualTo(p.farSplatResolution + 1), "far paint is sampled on the grid points: one more texel per edge");
            Assert.That(far.Details, Is.Empty);
            foreach (PropPlacement prop in far.Props)
            {
                // Far tiles carry only what they draw: large rocks, the houses of places and the landmarks' structures and stones.
                Assert.That(TileGenerator.FarRock(prop.role) || TileGenerator.FarHouse(prop.role) || TileGenerator.FarLandmark(prop.role), Is.True, prop.role);
                if (TileGenerator.FarRock(prop.role) && g.Keys.TryGetMetrics(prop.role, prop.variant, out PropMetrics m))
                {
                    Assert.That(m.Height * prop.scale, Is.GreaterThanOrEqualTo(TileGenerator.FarRockMinHeight - 1e-3f));
                }
            }

            // Height() agrees with the tile heightmap at its samples.
            double ox = near.Coord.OriginX(p.tileSize), oz = near.Coord.OriginZ(p.tileSize);
            float step = p.tileSize / (p.nearHeightResolution - 1);
            for (int k = 0; k < 20; k++)
            {
                int i = (k * 37) % p.nearHeightResolution, j = (k * 53) % p.nearHeightResolution;
                Assert.That(g.Height(ox + i * step, oz + j * step), Is.EqualTo(near.Heights[j, i] * p.heightRange).Within(1e-3f));
            }
        }

        // ------------------------------------------------------------------ the road

        [Test]
        public void Road_KeepsItsGrade_StaysDry_AndBendsGently([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            float maxGrade = g.Params.maxRoadGrade;
            double minRadius = double.PositiveInfinity;
            float worstGrade = 0f;
            RoadPoint prev = g.Road(-15000.0);
            float prevGround = g.Height(prev.X, prev.Z);
            var ground = new List<float>();
            var underDeck = new List<bool>();
            var crossings = new List<Crossing>();
            g.Streams.Crossings(-15100.0, 15100.0, crossings);
            for (double s = -15000.0; s <= 15000.0; s += 1.0)
            {
                RoadPoint rp = g.Road(s);
                float h = g.Height(rp.X, rp.Z);
                ground.Add(h);
                Assert.That(Mathf.Abs(rp.HeadingDeg), Is.LessThan(60f), $"Heading at s {s}.");
                // A ford: a few centimetres of water over the road at its bottom; a bridge: the deck carries the road over the channel.
                Crossing at = crossings.Find(c => Math.Abs(c.S - s) <= (c.Kind == CrossingKind.Ford ? StreamNetwork.FordWaterHalf + 1.0 : 10.0));
                bool ford = at != null && at.Kind == CrossingKind.Ford, bridge = at != null && at.Kind == CrossingKind.Bridge;
                underDeck.Add(bridge);
                float water = g.WaterHeight(rp.X, rp.Z);
                if (ford) Assert.That(water < 0f || water - rp.Height <= StreamPlan.FordWater + 0.02f, Is.True, $"The ford at s {s} is deeper than a few centimetres ({water - rp.Height:0.00} m).");
                else if (!bridge) Assert.That(water, Is.EqualTo(-1f), $"The road is under water at s {s}.");
                if (!bridge) Assert.That(Mathf.Abs(h - rp.Height), Is.LessThan(0.05f), $"Ground on the centre line differs from the road height at s {s}.");
                if (((long)s) % 4 == 0)
                {
                    RoadPoint a = g.Road(s - 4.0), b = g.Road(s + 4.0);
                    double h1 = Math.Atan2(rp.X - a.X, rp.Z - a.Z), h2 = Math.Atan2(b.X - rp.X, b.Z - rp.Z);
                    double turn = Math.Abs(Math.IEEERemainder(h2 - h1, 2.0 * Math.PI));
                    if (turn > 1e-9) minRadius = Math.Min(minRadius, 4.0 / turn);
                }

                prev = rp;
                prevGround = h;
            }

            for (int i = 10; i < ground.Count; i++)
            {
                if (underDeck[i] || underDeck[i - 10]) continue;
                worstGrade = Mathf.Max(worstGrade, Mathf.Abs(ground[i] - ground[i - 10]) / 10f);
            }
            Assert.That(worstGrade, Is.LessThanOrEqualTo(maxGrade + 0.005f), "Steepest 10 m of the carved road.");
            Assert.That(minRadius, Is.GreaterThan(20.0), "Tightest bend radius.");
        }

        [Test]
        public void RoadHeight_StaysWithinRange_Over100KmEachWay()
        {
            EndlessGenerator g = Gen(42);
            float top = g.Params.heightRange;
            for (double s = -100000.0; s <= 100000.0; s += 25.0)
            {
                RoadPoint rp = g.Road(s);
                Assert.That(rp.Height, Is.InRange(20f, top - 120f), $"Road height at s {s}.");
                Assert.That(Mathf.Abs(rp.Grade), Is.LessThanOrEqualTo(g.Params.maxRoadGrade), $"Grade at s {s}.");
                if (((long)s) % 1000 == 0)
                {
                    foreach (double lateral in new[] { -900.0, -250.0, 60.0, 400.0, 1800.0 })
                    {
                        double rad = rp.HeadingDeg * Math.PI / 180.0;
                        float h = g.Height(rp.X + Math.Cos(rad) * lateral, rp.Z - Math.Sin(rad) * lateral);
                        Assert.That(h, Is.InRange(0f, top), $"Ground at s {s}, {lateral} m off the road.");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ regions and places

        [Test]
        public void Regions_FollowTheirRules([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            EndlessParams p = g.Params;
            RegionKind first = g.Regions.Get(0).Kind;
            Assert.That(first == RegionKind.Meadow || first == RegionKind.Farmland, Is.True, "The journey starts in meadow or farmland.");
            Assert.That(g.Region(0.0).Index, Is.EqualTo(0));
            var kinds = new List<RegionKind>();
            for (long i = -40; i <= 40; i++)
            {
                Region r = g.Regions.Get(i);
                Region next = g.Regions.Get(i + 1);
                kinds.Add(r.Kind);
                Assert.That(r.Length, Is.InRange(p.regionLengthMin, p.regionLengthMax), $"Region {i} length.");
                Assert.That(next.Start, Is.EqualTo(r.End), "Regions are contiguous.");
                Assert.That(next.Kind, Is.Not.EqualTo(r.Kind), "Never the same kind twice in a row.");
                Assert.That(r.Blend, Is.InRange(300.0, 500.0));
                if (r.Kind == RegionKind.HighlandPass)
                {
                    Region before = g.Regions.Get(i - 1);
                    Assert.That(before.Kind == RegionKind.Downs || before.Kind == RegionKind.Forest, Is.True, $"Pass {i} is entered from {before.Kind}.");
                    Assert.That(next.Kind == RegionKind.Downs || next.Kind == RegionKind.Forest, Is.True, $"Pass {i} is left into {next.Kind}.");
                    Assert.That(r.Col, Is.InRange(r.Start, r.End));
                }

                Assert.That(string.IsNullOrEmpty(r.Name), Is.False);
            }

            // Every kind within any eight consecutive regions (≈ 15-20 km).
            for (int i = 0; i + 8 <= kinds.Count; i++)
            {
                Assert.That(kinds.Skip(i).Take(8).Distinct().Count(), Is.EqualTo(6), $"Kinds in regions {i - 40}..{i - 33}: {string.Join(", ", kinds.Skip(i).Take(8))}");
            }
        }

        [Test]
        public void Inns_AreEvenlySpaced_StartAndEveryColAreInns([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            var wps = new List<Waypoint>();
            g.Waypoints(-40000.0, 40000.0, wps);
            var inns = wps.Where(w => w.Kind == WaypointKind.Inn).Select(w => w.S).ToList();
            Assert.That(inns, Does.Contain(0.0), "The journey starts at an inn.");
            for (int i = 1; i < inns.Count; i++)
            {
                double gap = inns[i] - inns[i - 1];
                Assert.That(gap, Is.InRange(g.Params.innSpacingMin - 1.0, g.Params.innSpacingMax + 1.0), $"Inns at {inns[i - 1]} and {inns[i]}.");
            }

            foreach (Waypoint pass in wps.Where(w => w.Kind == WaypointKind.Pass))
            {
                Assert.That(inns, Does.Contain(pass.S), $"No refuge inn at the col at {pass.S}.");
            }

            foreach (Waypoint w in wps.Where(w => w.IsSettlement))
            {
                Assert.That(Math.Abs(g.Road(w.S).Grade), Is.LessThan(0.005f), $"The road through {w.Kind} '{w.Name}' is level.");
            }
        }

        [Test]
        public void Lakes_HaveOneLevelPerTile_AndKeepOffTheRoad()
        {
            EndlessGenerator g = Gen(12345);
            var wps = new List<Waypoint>();
            g.Waypoints(-60000.0, 60000.0, wps);
            List<Waypoint> lakes = wps.Where(w => w.Kind == WaypointKind.Lake).ToList();
            Assert.That(lakes.Count, Is.GreaterThan(2), "Lake country has lakes.");
            for (int i = 0; i < lakes.Count; i++)
            {
                for (int j = i + 1; j < lakes.Count; j++)
                {
                    double d = Math.Sqrt((lakes[i].X - lakes[j].X) * (lakes[i].X - lakes[j].X) + (lakes[i].Z - lakes[j].Z) * (lakes[i].Z - lakes[j].Z));
                    Assert.That(d - lakes[i].Radius - lakes[j].Radius, Is.GreaterThan(363.0), "Two lakes could share a tile.");
                }
            }

            Waypoint lake = lakes[0];
            TileCoord c = TileCoord.Containing(lake.X, lake.Z, g.Params.tileSize);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    TileBlueprint t = g.GenerateTile(new TileCoord(c.X + dx, c.Z + dz), TileLod.Far);
                    if (t.WaterHeight < 0f) continue;
                    Assert.That(t.WaterHeight, Is.EqualTo(lake.Height), "A tile's water level is its lake's.");
                }
            }
        }

        // ------------------------------------------------------------------ props

        [Test]
        public void Props_KeepOutOfTheRoadCorridor_AndRocksAreSeated()
        {
            EndlessGenerator g = Gen(777);
            EndlessParams p = g.Params;
            float corridor = p.roadWidth * 0.5f + 8f;
            int rocks = 0;
            foreach (double s in new[] { 600.0, 4200.0, 8800.0, 12600.0, 16400.0 })
            {
                TileCoord c = TileAt(g, s);
                TileBlueprint t = g.GenerateTile(c, TileLod.Near);
                double ox = c.OriginX(p.tileSize), oz = c.OriginZ(p.tileSize);
                GenWindow w = GenWindow.Create(p, g.Terrain, g.Curve, ox - 32.0, oz - 32.0, 160, 2f, -1f);
                var ctx = new RockPlacer.Context(w.Params, w.Map, g.Keys, null);
                foreach (PropPlacement prop in t.Props)
                {
                    double ax = ox + prop.position.x, az = oz + prop.position.z;
                    if (PropRole.IsRock(prop.role) && g.Keys.TryGetMetrics(prop.role, prop.variant, out PropMetrics m))
                    {
                        PropPlacement local = prop;
                        local.position = new Vector3((float)(ax - w.X0), prop.position.y, (float)(az - w.Z0));
                        RockAudit audit = RockPlacer.Inspect(ctx, m, local);
                        // Rocks on a stream's drawn bank are seated on the bank, which lies a little above the ground there.
                        StreamSample bank = StreamNetwork.Nearest(g.Terrain.RowAt(az).Streams, ax, az);
                        bool onBank = bank.Course != null && bank.Distance <= bank.Outer;
                        if (!onBank) Assert.That(audit.gap, Is.LessThanOrEqualTo(0.02f), $"{prop.role} at s {s} has an air gap of {audit.gap:0.00} m.");
                        if (PropRole.IsCliff(prop.role)) Assert.That(audit.convex, Is.False, $"{prop.role} at s {s} stands on convex ground.");
                        rocks++;
                        continue;
                    }

                    if (prop.settlementId >= 0 && prop.settlementId % 16 <= 2) continue;   // settlement dressing straddles the road on purpose (arches)
                    if (prop.settlementId >= 0 && prop.settlementId % 16 >= 8) continue;   // a bridge carries the road, stepping stones lie in the ford beside it
                    double sNear = g.NearestS(ax, az, s);
                    RoadPoint rp = g.Road(sNear);
                    double d = Math.Sqrt((rp.X - ax) * (rp.X - ax) + (rp.Z - az) * (rp.Z - az));
                    // Forest pass: in the woods the corridor is the region's (logs lie from 6 m of the road centre).
                    float propCorridor = Math.Min(corridor, Planting.TrunkCorridor(g.Regions.At(sNear).Kind, corridor));
                    Assert.That(d, Is.GreaterThan(Math.Min(corridor - 1.0, propCorridor)), $"{prop.role} at s {s} stands {d:0.0} m from the road centre.");
                }

                foreach (TreePlacement tree in t.Trees)
                {
                    double ax = ox + tree.position.x, az = oz + tree.position.z;
                    double sTree = g.NearestS(ax, az, s);
                    RoadPoint rp = g.Road(sTree);
                    double d = Math.Sqrt((rp.X - ax) * (rp.X - ax) + (rp.Z - az) * (rp.Z - az));
                    // Forest pass: the trunk corridor is per region (5.5 m in a forest; the open country keeps the scatter's).
                    float treeCorridor = Planting.TrunkCorridor(g.Regions.At(sTree).Kind, corridor);
                    float limit = PropRole.IsTree(tree.role) ? Math.Min(corridor - 1f, treeCorridor - 0.1f) : treeCorridor < corridor ? Planting.PlantVerge - 0.5f : corridor - 1f;
                    Assert.That(d, Is.GreaterThan(limit), $"A {tree.role} at s {s} stands {d:0.0} m from the road centre.");
                }
            }

            Assert.That(rocks, Is.GreaterThan(0), "The sampled tiles contain seated rocks.");
        }

        [Test]
        public void SettlementProps_CarryTheirWaypointId()
        {
            EndlessGenerator g = Gen(12345);
            var wps = new List<Waypoint>();
            g.Waypoints(-500.0, 500.0, wps);
            Waypoint start = wps.First(w => w.Kind == WaypointKind.Inn && w.S == 0.0);
            TileBlueprint t = g.GenerateTile(TileCoord.Containing(start.X, start.Z, g.Params.tileSize), TileLod.Near);
            var tileOwners = new List<TileBlueprint> { t };
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 || dz != 0) tileOwners.Add(g.GenerateTile(new TileCoord(t.Coord.X + dx, t.Coord.Z + dz), TileLod.Near));
                }
            }

            List<PropPlacement> inn = tileOwners.SelectMany(b => b.Props).Where(pp => pp.settlementId == start.Id).ToList();
            Assert.That(inn.Any(pp => pp.role == PropRole.Tavern), Is.True, "The start inn has its tavern (since 2026-10-05; the cabin before).");
            Assert.That(inn.Any(pp => pp.role == PropRole.Bench), Is.True, "The start inn has its benches.");
        }

        // ------------------------------------------------------------------ far from the start

        [Test]
        public void FarFromTheStart_TilesAreExact()
        {
            EndlessGenerator g = Gen(42);
            double s = 2000000.0;
            RoadPoint rp = g.Road(s);
            Assert.That(rp.Z, Is.GreaterThan(1500000.0));
            TileCoord c = TileCoord.Containing(rp.X, rp.Z, g.Params.tileSize);
            TileBlueprint t = g.GenerateTile(c, TileLod.Near);
            TileBlueprint east = g.GenerateTile(new TileCoord(c.X + 1, c.Z), TileLod.Near);
            int n = t.Heights.GetLength(0);
            for (int i = 0; i < n; i++) Assert.That(east.Heights[i, 0], Is.EqualTo(t.Heights[i, n - 1]));

            // The road is smooth there too: its bed is level and continuous at 0.5 m steps.
            float last = g.Height(rp.X, rp.Z);
            for (double ds = 0.5; ds <= 60.0; ds += 0.5)
            {
                RoadPoint q = g.Road(s + ds);
                float h = g.Height(q.X, q.Z);
                Assert.That(Mathf.Abs(h - last), Is.LessThan(0.1f), $"Road surface jumps at s {s + ds}.");
                last = h;
            }

            Assert.That(t.Trees.Count + t.Props.Count, Is.GreaterThan(0));
            foreach (TreePlacement tree in t.Trees) Assert.That(tree.position.x, Is.InRange(-0.01f, g.Params.tileSize + 0.01f));
        }

        // ------------------------------------------------------------------ helpers

        private static string Signature(TileBlueprint t)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                void Add(int v) { h = (h ^ (uint)v) * 1099511628211UL; }
                void AddF(float v) => Add(BitConverter.SingleToInt32Bits(v));
                foreach (float v in t.Heights) AddF(v);
                foreach (float v in t.Splat) AddF(v);
                foreach (DetailLayer layer in t.Details)
                {
                    Add(layer.family.GetHashCode());
                    Add(layer.variant);
                    foreach (byte b in layer.density) Add(b);
                }

                foreach (TreePlacement tree in t.Trees)
                {
                    AddF(tree.position.x);
                    AddF(tree.position.y);
                    AddF(tree.position.z);
                    AddF(tree.scale);
                    Add(tree.variant);
                }

                foreach (PropPlacement prop in t.Props)
                {
                    AddF(prop.position.x);
                    AddF(prop.position.y);
                    AddF(prop.position.z);
                    AddF(prop.yaw);
                    AddF(prop.scale);
                    Add(prop.variant);
                    Add(prop.settlementId);
                }

                AddF(t.WaterHeight);
                return $"{h:X16} trees {t.Trees.Count} props {t.Props.Count} details {t.Details.Count}";
            }
        }
    }
}
