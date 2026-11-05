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
    /// The hill town's crisp terraces (2026-10-09, after the user's first walk through Durncaster): what the eye takes for ground. The
    /// earlier tests checked where things are; these measure the faults the walk found, on the terrain as the near tiles build it (the
    /// plan sampled on the 2 m heightmap lattice and interpolated, both ways a cell can be split): the low terrace level right up to each
    /// wall's face, the high level on firm ground past each deck, decks flush with the walls' tops, no wall doubled beside another, every
    /// run ended by a pillar, no cliff in the streets, and everything seated on the planned surface.
    /// </summary>
    public class CityTerraceTests
    {
        private sealed class City
        {
            public int Seed;
            public EndlessGenerator G;
            public Place Place;
            public Dressing D;
            public CityHill Hill;
            public readonly Dictionary<long, float> Lattice = new Dictionary<long, float>();
            public string Name => $"{Place.Name} (seed {Seed}, {Place.S:0} m)";

            /// <summary>The built terrain at absolute (x, z): the lattice's heights interpolated over the cell's two triangles, both diagonals.</summary>
            public void Built(double x, double z, out float a, out float b)
            {
                const double cell = CityHill.Cell;
                long i = (long)Math.Floor(x / cell), j = (long)Math.Floor(z / cell);
                float fx = (float)(x / cell - i), fz = (float)(z / cell - j);
                float h00 = At(i, j), h10 = At(i + 1, j), h01 = At(i, j + 1), h11 = At(i + 1, j + 1);
                // Diagonal (0,0)-(1,1).
                a = fx >= fz ? h00 + (h10 - h00) * fx + (h11 - h10) * fz : h00 + (h01 - h00) * fz + (h11 - h01) * fx;
                // Diagonal (1,0)-(0,1).
                b = fx + fz <= 1f ? h00 + (h10 - h00) * fx + (h01 - h00) * fz : h11 + (h01 - h11) * (1f - fx) + (h10 - h11) * (1f - fz);
            }

            private float At(long i, long j)
            {
                long key = (i << 32) ^ (j & 0xffffffffL);
                if (!Lattice.TryGetValue(key, out float h)) Lattice[key] = h = G.Terrain.Height(i * CityHill.Cell, j * CityHill.Cell);
                return h;
            }

            public float Surface(double x, double z)
            {
                Hill.Local(x, z, out float u, out float v);
                return Hill.Level + (Hill.DeckAt(u, v, out float top) ? top : Hill.SurfaceAt(u, v));
            }
        }

        private static readonly Dictionary<int, List<City>> cache = new Dictionary<int, List<City>>();

        private static List<City> Cities(int seed)
        {
            if (cache.TryGetValue(seed, out List<City> list)) return list;
            list = new List<City>();
            var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            for (Region r = g.Regions.At(0); r.Start < 60000.0; r = g.Regions.Get(r.Index + 1))
            {
                foreach (Place p in g.Plan.PlacesOf(r.Index).Where(p => p.City))
                {
                    Dressing d = g.Dressings.Settlement(p);
                    CityHill hill = g.Terrain.Disc(p).Hill;
                    list.Add(new City { Seed = seed, G = g, Place = p, D = d, Hill = hill });
                }
            }

            cache[seed] = list;
            return list;
        }

        [Test]
        public void DurncasterIsAmongTheCities()
        {
            Assert.That(Cities(12345).Any(c => Math.Abs(c.Place.S - 18500.0) < 1.0), Is.True, "the city the user walked");
        }

        /// <summary>Along every retaining run: the low side's ground 1 m out from the wall's face is the low level; 0.5 m past the deck's inner edge, the high level.</summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void TerraceEdgesAreTrueSteps(int seed)
        {
            foreach (City c in Cities(seed))
            {
                CityHill h = c.Hill;
                int outs = 0, ins = 0;
                float worstOut = 0f, worstIn = 0f;
                string whereOut = "", whereIn = "";
                foreach (CityHill.Run run in h.Runs)
                {
                    for (float s = 1f; s <= run.Length - 1f; s += 1f)
                    {
                        run.At(s, out float u, out float v, out float nu, out float nv);
                        if (h.OutsideUV(u, v) > -1.5f) continue;
                        // 1 m out from the wall's face, on its low side.
                        float ou = u - nu, ov = v - nv;
                        if (!h.DeckAt(ou, ov, out _) && !h.OnWay(ou, ov, out _) && h.OutsideUV(ou, ov) < -1f)
                        {
                            h.World(ou, ov, out double x, out double z);
                            c.Built(x, z, out float a, out float b);
                            float want = h.Level + run.LoAt(s);
                            Assert.That(h.SurfaceAt(ou, ov), Is.EqualTo(run.LoAt(s)).Within(1e-3f), $"{c.Name}: the plan's low side of a {run.Kind} run");
                            float err = Math.Max(Math.Abs(a - want), Math.Abs(b - want));
                            outs++;
                            if (err > worstOut) { worstOut = err; whereOut = $"{run.Kind} face {run.Face} s {s:0} ({u:0},{v:0})"; }
                        }

                        // 0.5 m past the deck's inner edge, on firm ground (faces only: a ramp's deck spans its whole corridor).
                        if (run.Kind != CityHill.RunKind.Face) continue;
                        h.OffsetPoint(run.Face, u, v, nu, nv, CityHill.DeckReach + 0.5f, out float iu, out float iv);
                        if (h.OutsideUV(iu, iv) > -1f || h.OnWay(iu, iv, out _) || h.DeckAt(iu, iv, out _)) continue;
                        if (h.Castle != null && h.Rock(h.CX + iu * h.TX + iv * h.NX, h.CZ + iu * h.TZ + iv * h.NZ) > 0f) continue;
                        h.World(iu, iv, out double ix, out double iz);
                        c.Built(ix, iz, out float ia, out float ib);
                        float hi = h.Level + run.HiAt(s);
                        float e = Math.Max(Math.Abs(ia - hi), Math.Abs(ib - hi));
                        ins++;
                        if (e > worstIn) { worstIn = e; whereIn = $"face {run.Face} s {s:0} ({u:0},{v:0})"; }
                    }
                }

                Console.WriteLine($"{c.Name}: {outs} low-side samples, worst {worstOut:0.00} m ({whereOut}); {ins} firm-ground samples, worst {worstIn:0.00} m ({whereIn})");
                Assert.That(outs, Is.GreaterThan(200), $"{c.Name}: low-side samples");
                Assert.That(ins, Is.GreaterThan(200), $"{c.Name}: firm-ground samples");
                Assert.That(worstOut, Is.LessThanOrEqualTo(0.3f), $"{c.Name}: the built ground 1 m out from a wall's face ({whereOut})");
                Assert.That(worstIn, Is.LessThanOrEqualTo(0.3f), $"{c.Name}: the built ground 0.5 m past a deck's inner edge ({whereIn})");
            }
        }

        /// <summary>Each deck's outer edge is the wall's top (±0.1 m), and no built terrain rises through a deck.</summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void DecksAreFlushWithTheWallsAndCoverTheBanks(int seed)
        {
            foreach (City c in Cities(seed))
            {
                Dressing d = c.D;
                var modules = d.Props.Where(p => p.role == PropRole.CityWall).Select(p => Module(p, d)).ToList();
                Assert.That(d.City.Decks.Count, Is.GreaterThanOrEqualTo(c.Hill.Faces + c.Hill.Ways.Count(w => !w.Stair)), $"{c.Name}: a deck along each face and over each ramp");
                int flush = 0, under = 0;
                float worstFlush = 0f, worstUnder = 0f;
                string whereFlush = "", whereUnder = "";
                foreach (TerraceDeck deck in d.City.Decks)
                {
                    for (int k = 0; k < deck.Outer.Length; k++)
                    {
                        Vector3 o = deck.Outer[k] + new Vector3((float)d.X0, 0f, (float)d.Z0);
                        if (c.Hill.Outside(o.x, o.z) > -1f) continue;   // where a face meets the town wall (its modules stand on the outline)
                        // The module whose top the outer edge lies on (none at the openings: landings, flights).
                        float best = float.MaxValue, top = 0f;
                        foreach (var m in modules)
                        {
                            float dist = m.TopDistance(o, out float t);
                            if (dist < best) { best = dist; top = t; }
                        }

                        if (best < 0.35f)
                        {
                            flush++;
                            if (Math.Abs(o.y - top) > worstFlush) { worstFlush = Math.Abs(o.y - top); c.Hill.Local(o.x, o.z, out float wu, out float wv); whereFlush = $"{(deck.Ramp ? "ramp" : "face")} deck k {k} u,v ({wu:0.0},{wv:0.0})"; }
                        }

                        // Terrain under the deck (across the strip) never above it.
                        if (k + 1 >= deck.Outer.Length) continue;
                        for (float f = 0.1f; f < 1f; f += 0.2f)
                        {
                            Vector3 p = Vector3.Lerp(deck.Outer[k], deck.Inner[k], f) + new Vector3((float)d.X0, 0f, (float)d.Z0);
                            c.Built(p.x, p.z, out float a, out float b);
                            under++;
                            if (Math.Max(a, b) - p.y > worstUnder) { worstUnder = Math.Max(a, b) - p.y; c.Hill.Local(p.x, p.z, out float wu, out float wv); whereUnder = $"{(deck.Ramp ? "ramp" : "face")} deck k {k} f {f:0.0} u,v ({wu:0.0},{wv:0.0})"; }
                        }
                    }
                }

                Console.WriteLine($"{c.Name}: {flush} deck edges on a wall's top, worst {worstFlush:0.000} m; {under} samples under decks, terrain at most {worstUnder:0.00} m above");
                Console.WriteLine($"  worst flush at {whereFlush}; worst under at {whereUnder}");
                Assert.That(flush, Is.GreaterThan(300), $"{c.Name}: deck edges on walls");
                Assert.That(worstFlush, Is.LessThanOrEqualTo(0.1f), $"{c.Name}: a deck's outer edge is the wall's top ({whereFlush})");
                Assert.That(worstUnder, Is.LessThanOrEqualTo(0.02f), $"{c.Name}: terrain through a deck ({whereUnder})");
            }
        }

        /// <summary>No wall doubled within 3 m of a parallel one; every run of modules ends in a pillar or a tower; a wall taller than a module has pillars.</summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void WallsAreSingleAndEnded(int seed)
        {
            foreach (City c in Cities(seed))
            {
                Dressing d = c.D;
                CityHill h = c.Hill;
                // The castle's own walls (the mined plan, or the keep's ring) are the castle's design: the town's walls only.
                // (The gatehouses' spans over the road stand beside the town wall by design.)
                var modules = d.Props.Where(p => p.role == PropRole.CityWall && !OnCastle(h, d, p.position) && Flat(p.position, d.City.GateIn) > 16f && Flat(p.position, d.City.GateOut) > 16f)
                    .Select(p => Module(p, d)).ToList();
                for (int i = 0; i < modules.Count; i++)
                {
                    for (int j = i + 1; j < modules.Count; j++)
                    {
                        var a = modules[i];
                        var b = modules[j];
                        if (Flat(a.Centre, b.Centre) > 8f) continue;
                        float dot = Math.Abs(a.Dir.x * b.Dir.x + a.Dir.z * b.Dir.z);
                        if (dot < Mathf.Cos(15f * Mathf.Deg2Rad)) continue;
                        Vector3 rel = b.Centre - a.Centre;
                        float across = Math.Abs(rel.x * a.Dir.z - rel.z * a.Dir.x), along = Math.Abs(rel.x * a.Dir.x + rel.z * a.Dir.z);
                        if (across < 0.6f || along > CityComposer.ModuleLength - 1f) continue;   // the same line (stacked or neighbours), or not side by side
                        h.Local(a.Centre.x, a.Centre.z, out float au, out float av);
                        h.Local(b.Centre.x, b.Centre.z, out float bu, out float bv);
                        Assert.That(across, Is.GreaterThanOrEqualTo(3f), $"{c.Name}: two parallel walls {across:0.0} m apart at u,v ({au:0.0},{av:0.0}) and ({bu:0.0},{bv:0.0})");
                    }
                }

                var posts = d.Props.Where(p => (p.role == PropRole.CityPillar && p.variant == 0) || p.role == PropRole.CityTower)
                    .Select(p => new Vector3((float)(d.X0 + p.position.x), 0f, (float)(d.Z0 + p.position.z))).ToList();
                int ends = 0;
                foreach (CityHill.Run run in h.Runs)
                {
                    if (run.Length < 1f) continue;
                    foreach (float s in new[] { 0f, run.Length })
                    {
                        run.At(s, out float u, out float v, out float nu, out float nv);
                        h.World(u + nu * 0.25f, v + nv * 0.25f, out double x, out double z);
                        var at = new Vector3((float)x, 0f, (float)z);
                        ends++;
                        Assert.That(posts.Any(p => Flat(p, at) < 1.0f), Is.True, $"{c.Name}: a {run.Kind} run of face {run.Face} ends without a pillar at ({u:0},{v:0})");
                    }
                }

                Assert.That(ends, Is.GreaterThanOrEqualTo(2 * h.Faces), $"{c.Name}: runs");
                Console.WriteLine($"{c.Name}: {modules.Count} wall modules ({d.City.RetainingModules} retaining), {d.City.Pillars} pillars, {d.City.Flights} flights, {d.City.Decks.Count} decks ({d.City.DeckTriangles} triangles), {h.Runs.Count} runs");
            }
        }

        /// <summary>No cliff inside the town wall or within 6 m of a way; nothing over 2.5 m of rock inside the wall at all.</summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void NoCliffsInTheStreets(int seed)
        {
            PaletteKeys keys = PaletteKeys.Default;
            foreach (City c in Cities(seed))
            {
                Dressing d = c.D;
                CityHill h = c.Hill;
                int rocks = 0;
                foreach (PropPlacement p in d.Props)
                {
                    if (!PropRole.IsRock(p.role) && !PropRole.IsCliff(p.role)) continue;
                    double x = d.X0 + p.position.x, z = d.Z0 + p.position.z;
                    float size = keys.TryGetMetrics(p.role, p.variant, out PropMetrics m) ? m.Length * p.scale : 99f;
                    float outside = h.Outside(x, z);
                    h.Local(x, z, out float u, out float v);
                    if (PropRole.IsCliff(p.role))
                    {
                        Assert.That(outside, Is.GreaterThan(0f), $"{c.Name}: a {p.role} inside the town wall at ({u:0},{v:0})");
                        Assert.That(WayDistance(h, u, v), Is.GreaterThanOrEqualTo(6f), $"{c.Name}: a {p.role} within 6 m of a way up");
                    }

                    if (outside < 0f) Assert.That(size, Is.LessThanOrEqualTo(2.5f), $"{c.Name}: a {size:0.0} m {p.role} inside the town wall");
                    rocks++;
                }

                Console.WriteLine($"{c.Name}: {rocks} rocks, all outside the wall or small");
            }
        }

        /// <summary>Every house and every ground-standing prop on the hill sits on the planned surface (the terrace's level, a deck's top), never part-way down a bank.</summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void EverythingOnTheHillSitsOnTheSurface(int seed)
        {
            var ground = new HashSet<string> { PropRole.Bench, PropRole.Well, PropRole.Lamp, PropRole.MarketStall };   // (set pieces' parts: Job B's mined heights, checked there)
            foreach (City c in Cities(seed))
            {
                Dressing d = c.D;
                CityHill h = c.Hill;
                int houses = 0, props = 0;
                foreach (PropPlacement p in d.Props)
                {
                    double x = d.X0 + p.position.x, z = d.Z0 + p.position.z;
                    h.Local(x, z, out float u, out float v);
                    if (v < CityHill.FootV - 4f || h.Outside(x, z) > -2f || h.Rock(x, z) > 0f) continue;
                    BuildingShape shape = PropRole.IsBuilding(p.role) || p.role == PropRole.Tavern ? BuildingTable.For(p.role, p.variant) : p.role == PropRole.LandmarkChapel ? BuildingTable.Church : null;
                    if (shape != null)
                    {
                        if (shape.BaseDepth <= 0.5f) continue;
                        float hi = float.MinValue;
                        for (int ix = 0; ix <= 2; ix++)
                        {
                            for (int iz = 0; iz <= 2; iz++)
                            {
                                Vector3 w = BuildingTable.ToWorld(p.position, p.yaw, new Vector3(Mathf.Lerp(shape.BodyX0, shape.BodyX1, ix * 0.5f), 0f, Mathf.Lerp(shape.BodyZ0, shape.BodyZ1, iz * 0.5f)));
                                hi = Math.Max(hi, c.Surface(d.X0 + w.x, d.Z0 + w.z));
                            }
                        }

                        houses++;
                        Assert.That(p.position.y + 0.55f - hi, Is.InRange(-0.15f, 0.15f), $"{c.Name}: a {p.role} at ({u:0},{v:0}) off the surface");
                        continue;
                    }

                    if (!ground.Contains(p.role)) continue;
                    props++;
                    Assert.That(p.position.y - c.Surface(x, z), Is.InRange(-0.15f, 0.15f), $"{c.Name}: a {p.role} at ({u:0},{v:0}) off the surface");
                }

                foreach (TreePlacement t in d.Trees)
                {
                    double x = d.X0 + t.position.x, z = d.Z0 + t.position.z;
                    h.Local(x, z, out float u, out float v);
                    if (v < CityHill.FootV - 4f || h.Outside(x, z) > -2f) continue;
                    Assert.That(t.position.y + 0.1f - c.Surface(x, z), Is.InRange(-0.15f, 0.15f), $"{c.Name}: a tree at ({u:0},{v:0}) off the surface");
                }

                Console.WriteLine($"{c.Name}: {houses} houses and {props} props on the hill, all on the surface");
                Assert.That(houses, Is.GreaterThan(10), $"{c.Name}: houses on the hill");
            }
        }

        /// <summary>
        /// Every terrace is lived on (the pack's demo packs its terraces shoulder to shoulder): houses on each bench above the lower town
        /// (between two faces) on both halves of the hill (either side of its centre line), at least <see cref="MinPerHalf"/> each.
        /// </summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void TerracesAreFullOnBothHalves(int seed)
        {
            foreach (City c in Cities(seed))
            {
                int[,] n = BuildingsPerTerrace(c);
                CityHill h = c.Hill;
                var line = new System.Text.StringBuilder();
                int all = 0;
                for (int b = 0; b <= h.Faces; b++)
                {
                    line.Append($" T{b} {n[b, 0]}+{n[b, 1]}");
                    all += n[b, 0] + n[b, 1];
                }

                Console.WriteLine($"{c.Name}: {all} buildings (left+right per terrace, T0 the lower town, T{h.Faces} the plateau):{line}");
                for (int b = 1; b < h.Faces; b++)
                    for (int half = 0; half < 2; half++)
                        Assert.That(n[b, half], Is.GreaterThanOrEqualTo(MinPerHalf), $"{c.Name}: terrace {b}, {(half == 0 ? "left" : "right")} half");
            }
        }

        /// <summary>
        /// Houses a bench's half must hold at least (see <see cref="TerracesAreFullOnBothHalves"/>). A bench is 15-18 m deep inside the
        /// town wall, room for one row (backs to the riser, the deck their street); half its line is 60-75 m long, and on one half the
        /// ramp at the riser's foot, its hairpin and a flight take up to 60 m where only the shallowest preset fits in front of the ramp's
        /// outer wall. Five cities on three seeds hold 5 to 11 per half (2026-10-09); 4 leaves a house of margin.
        /// </summary>
        private const int MinPerHalf = 4;

        /// <summary>Battlements stand on the town wall, its gatehouses and the castle only: never on a terrace's retaining wall.</summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void NoBattlementOnTheTerraces(int seed)
        {
            foreach (City c in Cities(seed))
            {
                Dressing d = c.D;
                CityHill h = c.Hill;
                int town = 0, gates = 0, castle = 0;
                foreach (PropPlacement p in d.Props)
                {
                    if (p.role != PropRole.CityBattlement) continue;
                    double x = d.X0 + p.position.x, z = d.Z0 + p.position.z;
                    h.Local(x, z, out float u, out float v);
                    if (OnCastle(h, d, p.position)) castle++;
                    else if (Flat(p.position, d.City.GateIn) < 16f || Flat(p.position, d.City.GateOut) < 16f) gates++;
                    else
                    {
                        // On the town wall's line (where a terrace's wall meets the town wall the town's battlements run on).
                        Assert.That(Math.Abs(h.Outside(x, z)), Is.LessThan(1.5f), $"{c.Name}: a battlement at ({u:0},{v:0}) on a terrace (neither on the town wall, a gatehouse nor the castle)");
                        town++;
                    }
                }

                Assert.That(d.City.RetainingBattlements, Is.EqualTo(0), $"{c.Name}: retaining battlements");
                Console.WriteLine($"{c.Name}: battlements {town} on the town wall, {gates} at the gates, {castle} on the castle, none on the terraces");
            }
        }

        /// <summary>
        /// Every terrace edge (along each retaining run, wherever a deck lies behind it) carries the parapet: the drawn lip 0.4-1.1 m above
        /// the deck over the wall's top, and a collider box there reaching 0.4-1.1 m above the deck, so the traveller is stopped at the edge.
        /// </summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void EveryTerraceEdgeHasAParapet(int seed)
        {
            foreach (City c in Cities(seed))
            {
                CityHill h = c.Hill;
                float tile = c.G.Params.tileSize;
                // The near tiles' deck data over the whole town, in absolute coordinates.
                var boxes = new List<(Vector3 centre, BridgeWallBox box)>();
                var tops = new List<(Vector3 a, Vector3 b, Vector3 c)>();   // the drawn level faces (deck tops and lip tops)
                var seen = new HashSet<(int, int)>();
                for (float v = CityHill.FootV - 10f; v <= 110f; v += 20f)
                {
                    for (float u = -110f; u <= 110f; u += 20f)
                    {
                        h.World(u, v, out double wx, out double wz);
                        int tx = (int)Math.Floor(wx / tile), tz = (int)Math.Floor(wz / tile);
                        if (!seen.Add((tx, tz))) continue;
                        TerraceDeckMeshData m = TerraceDecks.Build(new List<Dressing> { c.D }, tx * (double)tile, tz * (double)tile, tile);
                        if (m == null) continue;
                        var o = new Vector3(tx * tile, 0f, tz * tile);
                        foreach (BridgeWallBox b in m.Boxes) boxes.Add((b.Centre + o, b));
                        for (int i = 0; i + 2 < m.Indices.Length; i += 3)
                        {
                            Vector3 a = m.Positions[m.Indices[i]] + o, b = m.Positions[m.Indices[i + 1]] + o, e = m.Positions[m.Indices[i + 2]] + o;
                            Vector3 n = Vector3.Cross(b - a, e - a).normalized;
                            if (Math.Abs(n.y) > 0.5f) tops.Add((a, b, e));
                        }
                    }
                }

                int samples = 0, kinds = 0;
                var byKind = new Dictionary<CityHill.RunKind, int>();
                foreach (CityHill.Run run in h.Runs)
                {
                    for (float s = 0.75f; s <= run.Length - 0.75f; s += 1f)
                    {
                        run.At(s, out float u, out float v, out float nu, out float nv);
                        if (h.OutsideUV(u, v) > -1.5f) continue;
                        // Only where a deck lies behind the wall (a ramp's outer wall below MinWall carries none).
                        if (!h.DeckAt(u + nu * 0.7f, v + nv * 0.7f, out float top)) continue;
                        float deck = h.Level + top;
                        // The lip's middle: 0.2 m behind the wall's low face.
                        h.World(u + nu * 0.2f, v + nv * 0.2f, out double x, out double z);
                        var at = new Vector3((float)x, deck, (float)z);
                        float lip = float.MinValue;
                        foreach (var (a, b, e) in tops)
                        {
                            if (Math.Abs(a.y - deck) > 2f || !OverTriangle(a, b, e, at, out float y)) continue;
                            lip = Math.Max(lip, y - deck);
                        }
                        Assert.That(lip, Is.InRange(0.4f, 1.1f), $"{c.Name}: the drawn parapet over a {run.Kind} run of face {run.Face} at ({u:0},{v:0}), s {s:0.0}");
                        bool held = false;
                        foreach (var (centre, b) in boxes)
                        {
                            if (Flat(centre, at) > 0.5f * b.Size.z + 2f) continue;
                            float top0 = BoxTop(centre, b, at);
                            if (top0 - deck >= 0.4f && top0 - deck <= 1.1f) { held = true; break; }
                        }

                        Assert.That(held, Is.True, $"{c.Name}: no parapet collider over a {run.Kind} run of face {run.Face} at ({u:0},{v:0}), s {s:0.0}");
                        samples++;
                        byKind.TryGetValue(run.Kind, out int k);
                        byKind[run.Kind] = k + 1;
                    }
                }

                // The openings stay open: no lip or lip collider where a ramp's landing or a flight's top meets the terrace above.
                int openings = 0;
                foreach (CityHill.Way w in h.Ways)
                {
                    float sm = w.Stair ? w.SA : w.SB - w.Dir * 0.5f * CityHill.LandLen;
                    h.Lines[w.Face].At(sm, out float u, out float v, out float nu, out float nv);
                    if (!h.DeckAt(u + nu * 0.7f, v + nv * 0.7f, out float top)) continue;
                    float deck = h.Level + top;
                    h.World(u + nu * 0.2f, v + nv * 0.2f, out double x, out double z);
                    var at = new Vector3((float)x, deck, (float)z);
                    foreach (var (a, b, e) in tops)
                    {
                        if (Math.Abs(a.y - deck) > 2f || !OverTriangle(a, b, e, at, out float y)) continue;
                        Assert.That(y - deck, Is.LessThan(0.1f), $"{c.Name}: a lip across the opening of the {(w.Stair ? "flight" : "landing")} on face {w.Face}");
                    }

                    foreach (var (centre, b) in boxes)
                    {
                        if (Flat(centre, at) > 0.5f * b.Size.z + 2f) continue;
                        Assert.That(BoxTop(centre, b, at) - deck, Is.LessThan(0.1f), $"{c.Name}: a lip collider across the opening of the {(w.Stair ? "flight" : "landing")} on face {w.Face}");
                    }

                    openings++;
                }

                Assert.That(openings, Is.GreaterThanOrEqualTo(h.Faces), $"{c.Name}: openings checked");
                kinds = byKind.Count;
                Assert.That(byKind.ContainsKey(CityHill.RunKind.Face) && byKind.ContainsKey(CityHill.RunKind.RampSide) && byKind.ContainsKey(CityHill.RunKind.RampEnd), Is.True, $"{c.Name}: edges of every kind sampled");
                Console.WriteLine($"{c.Name}: parapet over {samples} m of terrace edge (" + string.Join(", ", byKind.Select(k => $"{k.Key} {k.Value}")) + $"), {boxes.Count} deck and lip boxes");
            }
        }

        /// <summary>
        /// The ground queries find every deck's top (2026-10-09, Job F: a segment across a tile seam was in no tile's height lookup on the
        /// far side, so <c>EndlessWorld.GroundHeightAbsolute</c> returned the bank under it): along every deck at 0.5 m, a quarter, half
        /// and three quarters of the way across, the near tile holding the point gives the deck's top within 0.1 m through its lookup.
        /// </summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void DeckTopsAreFoundAcrossTileSeams(int seed)
        {
            foreach (City c in Cities(seed))
            {
                Dressing d = c.D;
                float tile = c.G.Params.tileSize;
                var tiles = new Dictionary<(int, int), TerraceDeckMeshData>();
                int samples = 0, seams = 0;
                foreach (TerraceDeck deck in d.City.Decks)
                {
                    for (int k = 0; k + 1 < deck.Outer.Length; k++)
                    {
                        Vector3 o0 = deck.Outer[k], o1 = deck.Outer[k + 1], i0 = deck.Inner[k], i1 = deck.Inner[k + 1];
                        float len = Math.Max(Flat(o0, o1), Flat(i0, i1));
                        int steps = Math.Max(1, (int)Math.Ceiling(len / 0.5f));
                        for (int a = 0; a < steps; a++)
                        {
                            float t = (a + 0.5f) / steps;
                            Vector3 o = Vector3.Lerp(o0, o1, t), i = Vector3.Lerp(i0, i1, t);
                            if (Flat(o, i) < 1f) continue;
                            // The lookup holds a cell where its centre lies on the deck: within a cell's half diagonal of a deck's ends it may not.
                            foreach (float across in new[] { 0.25f, 0.5f, 0.75f })
                            {
                                Vector3 p = Vector3.Lerp(o, i, across);
                                int last = deck.Outer.Length - 1;
                                if (EdgeDistance(p, deck.Outer[0], deck.Inner[0]) < 0.4f || EdgeDistance(p, deck.Outer[last], deck.Inner[last]) < 0.4f) continue;
                                double x = d.X0 + p.x, z = d.Z0 + p.z;
                                // Where a deck runs into the town wall its last samples are clipped to the wall's line (a thin wedge
                                // inside the wall's own body): not ground anyone stands on.
                                if (c.Hill.Outside(x, z) > -1.5f) continue;
                                int tx = (int)Math.Floor(x / tile), tz = (int)Math.Floor(z / tile);
                                if (!tiles.TryGetValue((tx, tz), out TerraceDeckMeshData m))
                                    tiles[(tx, tz)] = m = TerraceDecks.Build(new List<Dressing> { d }, tx * (double)tile, tz * (double)tile, tile);
                                // Whether the segment's middle lies in another tile (the case that failed).
                                Vector3 mid = (o0 + o1 + i0 + i1) * 0.25f;
                                if ((int)Math.Floor((d.X0 + mid.x) / tile) != tx || (int)Math.Floor((d.Z0 + mid.z) / tile) != tz) seams++;
                                Assert.That(m, Is.Not.Null, $"{c.Name}: no deck data in tile ({tx},{tz}) under a deck at ({x:0.0},{z:0.0})");
                                Assert.That(m.TryHeight((float)(x - tx * (double)tile), (float)(z - tz * (double)tile), out float y), Is.True, $"{c.Name}: no deck height at ({x:0.0},{z:0.0}), segment {k} of {deck.Outer.Length - 1} at {t:0.00} along, {across:0.00} across, tile-local ({x - tx * (double)tile:0.00},{z - tz * (double)tile:0.00}), ramp {deck.Ramp}, width {Flat(o, i):0.0}, segment length {len:0.00}, its middle's tile ({Math.Floor((d.X0 + ((o0 + o1 + i0 + i1) * 0.25f).x) / tile)},{Math.Floor((d.Z0 + ((o0 + o1 + i0 + i1) * 0.25f).z) / tile)}) vs ({tx},{tz}); grid {(m.Grid == null ? "none" : "yes")}");
                                Assert.That(y, Is.EqualTo(p.y).Within(0.1f), $"{c.Name}: the deck's top at ({x:0.0},{z:0.0})");
                                samples++;
                            }
                        }
                    }
                }

                Console.WriteLine($"{c.Name}: {samples} deck points found through {tiles.Count} tiles' lookups, {seams} of them across a seam from their segment's tile");
            }
        }

        /// <summary>Flat distance from <paramref name="p"/> to segment a-b.</summary>
        private static float EdgeDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            float ex = b.x - a.x, ez = b.z - a.z, l2 = ex * ex + ez * ez;
            float f = l2 > 1e-9f ? Mathf.Clamp01(((p.x - a.x) * ex + (p.z - a.z) * ez) / l2) : 0f;
            return Mathf.Sqrt((a.x + ex * f - p.x) * (a.x + ex * f - p.x) + (a.z + ez * f - p.z) * (a.z + ez * f - p.z));
        }

        /// <summary>Whether the flat point <paramref name="p"/> lies over triangle a-b-c (seen from above), and the triangle's height there.</summary>
        private static bool OverTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 p, out float y)
        {
            y = 0f;
            float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Math.Abs(den) < 1e-8f) return false;
            float wa = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / den;
            float wb = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / den;
            float wc = 1f - wa - wb;
            if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) return false;
            y = wa * a.y + wb * b.y + wc * c.y;
            return true;
        }

        /// <summary>The top of collider box <paramref name="b"/> (centred at <paramref name="centre"/>, absolute) over the flat point <paramref name="at"/>, or -inf when the point lies outside its footprint.</summary>
        private static float BoxTop(Vector3 centre, BridgeWallBox b, Vector3 at)
        {
            float ya = b.Yaw * Mathf.Deg2Rad, pa = b.Pitch * Mathf.Deg2Rad;
            var right = new Vector3(Mathf.Cos(ya), 0f, -Mathf.Sin(ya));
            var fwd = new Vector3(Mathf.Sin(ya) * Mathf.Cos(pa), -Mathf.Sin(pa), Mathf.Cos(ya) * Mathf.Cos(pa));
            var up = new Vector3(Mathf.Sin(pa) * Mathf.Sin(ya), Mathf.Cos(pa), Mathf.Sin(pa) * Mathf.Cos(ya));
            // Walk up the vertical through the point to the box's top face (its local +y face), if the vertical passes through the box.
            Vector3 rel = new Vector3(at.x - centre.x, 0f, at.z - centre.z);
            float lx = Vector3.Dot(rel, right);
            if (Math.Abs(lx) > 0.5f * b.Size.x) return float.NegativeInfinity;
            // Point on the vertical: rel + (0, y, 0); top face: dot(p, up) = Size.y / 2.
            float y = (0.5f * b.Size.y - Vector3.Dot(rel, up)) / up.y;
            Vector3 p = rel + new Vector3(0f, y, 0f);
            if (Math.Abs(Vector3.Dot(p, fwd)) > 0.5f * b.Size.z) return float.NegativeInfinity;
            return centre.y + y;
        }

        /// <summary>Buildings per terrace (0 the lower town, 1..Faces-1 the benches, Faces the plateau) and half (0: u &lt; 0).</summary>
        private static int[,] BuildingsPerTerrace(City c)
        {
            CityHill h = c.Hill;
            Dressing d = c.D;
            var n = new int[h.Faces + 1, 2];
            foreach (PropPlacement p in d.Props)
            {
                BuildingShape shape = PropRole.IsBuilding(p.role) || p.role == PropRole.Tavern ? BuildingTable.For(p.role, p.variant) : p.role == PropRole.LandmarkChapel ? BuildingTable.Church : null;
                if (shape == null) continue;
                Vector3 mid = BuildingTable.ToWorld(p.position, p.yaw, new Vector3(0.5f * (shape.BodyX0 + shape.BodyX1), 0f, 0.5f * (shape.BodyZ0 + shape.BodyZ1)));
                double x = d.X0 + mid.x, z = d.Z0 + mid.z;
                if (h.Outside(x, z) > 0f) continue;
                h.Local(x, z, out float u, out float v);
                int b = 0;
                if (v > 0f) { float q = h.QAt(u, v); for (int i = 0; i < h.Faces; i++) if (q <= h.Q[i]) b = i + 1; }
                n[b, u < 0f ? 0 : 1]++;
            }

            return n;
        }

        // ------------------------------------------------------------------ helpers

        private struct ModuleShape
        {
            public Vector3 Centre, Dir, TopCentre;
            public float Slope;

            /// <summary>Flat distance from <paramref name="p"/> to the module's top (its 5 m line, the half thickness either side counts as on it), and the top's height there.</summary>
            public float TopDistance(Vector3 p, out float top)
            {
                Vector3 rel = p - TopCentre;
                float along = rel.x * Dir.x + rel.z * Dir.z, across = Math.Abs(rel.x * Dir.z - rel.z * Dir.x);
                float half = 0.5f * CityComposer.ModuleLength;
                top = TopCentre.y + Slope * Mathf.Clamp(along, -half, half);
                float off = Math.Max(0f, Math.Abs(along) - half);
                return Mathf.Sqrt(off * off + Math.Max(0f, across - 0.5f * CityHill.WallThick) * Math.Max(0f, across - 0.5f * CityHill.WallThick));
            }
        }

        /// <summary>A wall module's line (absolute), from its base pose: its top's centre, the run's direction and the top's slope.</summary>
        private static ModuleShape Module(PropPlacement p, Dressing d)
        {
            float a = p.yaw * Mathf.Deg2Rad, b = p.pitch * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            // Up axis Ry(yaw) Rx(pitch) (0, 1, 0).
            var up = new Vector3(Mathf.Sin(b) * dir.x, Mathf.Cos(b), Mathf.Sin(b) * dir.z);
            var basePos = new Vector3((float)(d.X0 + p.position.x), p.position.y, (float)(d.Z0 + p.position.z));
            Vector3 top = basePos + up * CityComposer.WallHeight;
            return new ModuleShape { Centre = basePos, Dir = dir, TopCentre = top, Slope = -Mathf.Tan(b) };
        }

        private static bool OnCastle(CityHill h, Dressing d, Vector3 local)
        {
            h.Local(d.X0 + local.x, d.Z0 + local.z, out float u, out float v);
            return h.OnCastle(u, v, 2f) || (h.Castle == null && v > CityHill.PlateauV + 1f);
        }

        private static float Flat(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

        private static float WayDistance(CityHill h, float u, float v)
        {
            float best = float.MaxValue;
            foreach (CityHill.Way w in h.Ways)
            {
                for (int i = 1; i < w.U.Length; i++)
                {
                    float ex = w.U[i] - w.U[i - 1], ez = w.V[i] - w.V[i - 1], l2 = ex * ex + ez * ez;
                    float f = l2 > 1e-9f ? Mathf.Clamp01(((u - w.U[i - 1]) * ex + (v - w.V[i - 1]) * ez) / l2) : 0f;
                    float dx = w.U[i - 1] + ex * f - u, dz = w.V[i - 1] + ez * f - v;
                    best = Math.Min(best, Mathf.Sqrt(dx * dx + dz * dz) - w.Half);
                }
            }

            return best;
        }
    }
}
