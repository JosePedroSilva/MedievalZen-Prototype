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
    /// <summary>The walled city (2026-10-06): where it stands (a village's slot) and what the composer builds there.</summary>
    public class EndlessCityTests
    {
        private static List<Place> Cities(EndlessGenerator g, double s1)
        {
            var list = new List<Place>();
            for (Region r = g.Regions.At(0); r.Start < s1; r = g.Regions.Get(r.Index + 1))
            {
                list.AddRange(g.Plan.PlacesOf(r.Index).Where(p => p.City));
            }

            return list;
        }

        [Test]
        public void CitiesTakeVillageSlotsInFarmlandAndKeepTheirDistance()
        {
            var expected = new Dictionary<int, double[]> { { 12345, new[] { 18500.0, 54141.9 } }, { 42, new[] { 19805.6, 51288.6 } }, { 777, new[] { 39092.5 } } };
            foreach (var pair in expected)
            {
                var g = new EndlessGenerator(new EndlessParams { seed = pair.Key }, PaletteKeys.Default);
                List<Place> cities = Cities(g, 60000.0);
                Assert.That(cities.Select(c => c.S).ToArray(), Is.EqualTo(pair.Value), $"seed {pair.Key}");
                for (int i = 0; i < cities.Count; i++)
                {
                    Assert.That(g.Regions.At(cities[i].S).Kind, Is.EqualTo(RegionKind.Farmland));
                    Assert.That(cities[i].Kind, Is.EqualTo(PlaceKind.Village));
                    Assert.That(cities[i].Radius, Is.EqualTo(JourneyPlan.CityRadius));
                    if (i > 0) Assert.That(cities[i].S - cities[i - 1].S, Is.GreaterThanOrEqualTo(JourneyPlan.CityGap));
                }

                var wps = new List<Waypoint>();
                g.Waypoints(0.0, 60000.0, wps);
                Assert.That(wps.Count(w => w.Kind == WaypointKind.City), Is.EqualTo(cities.Count));
                Assert.That(wps.Where(w => w.Kind == WaypointKind.City).All(w => w.IsSettlement && w.IsVillageLike && !string.IsNullOrEmpty(w.Name)), Is.True);
            }
        }

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void CityIsWalledGatedAndClear(int seed)
        {
            var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            Place city = Cities(g, 60000.0).First();
            Dressing d = g.Dressings.Settlement(city);
            Assert.That(g.Dressings.Settlement(city), Is.SameAs(d));
            CityStats st = d.City;
            Assert.That(st, Is.Not.Null);
            float half = g.Params.roadWidth * 0.5f;
            Assert.That(CityComposer.GateClearWidth, Is.GreaterThanOrEqualTo(2f * (half + SettlementKit.Verge)), "the gate clears the road and its verges");
            Assert.That(st.BuildingCount, Is.InRange(85, CityComposer.MaxBuildings + 1), "buildings (the hill town is built up close)");
            Assert.That(st.Triangles, Is.LessThan(2500000L), "triangles at LOD0 (counted, reported; the look first, the budget later)");
            CityHill hill = CityHill.For(g.Params.seed, city.Id, d.CentreX, d.CentreZ, g.Curve.Heading(d.CentreZ));
            float towerHalf = 0.5f * CityComposer.TowerDiameter * CityComposer.TowerScale;
            Assert.That(st.WallRadiusMin, Is.GreaterThan(78f), "the wall's outline");
            Assert.That(st.WallRadiusMax, Is.LessThanOrEqualTo(CityHill.MaxWallRadius + 0.01f), "the wall's outline: what the blocks fill");
            Assert.That(st.WallRadiusMax + towerHalf, Is.LessThan(0.85f * JourneyPlan.CityRadius), "the wall stands on the levelled disc");
            Assert.That(st.WallRadiusMax + towerHalf, Is.LessThan((float)SettlementFootprint.CityKeepOut), "no field against the wall");
            Assert.That(st.Buildings.ContainsKey(PropRole.LandmarkChapel) && st.Buildings.ContainsKey(PropRole.LandmarkTower), Is.True, "church and castle");
            Assert.That(st.Buildings.ContainsKey(PropRole.Tavern) && st.Buildings.ContainsKey(PropRole.Smithy), Is.True, "tavern and smithy");

            Func<Vector3, double> roadDistance = v =>
            {
                g.Curve.Nearest(d.X0 + v.x, d.Z0 + v.z, out double rd);
                return rd;
            };

            // Buildings: off the road and verge, no two overlapping, each reached by a street.
            var rects = new List<Vector3[]>();
            foreach (PropPlacement p in d.Props)
            {
                BuildingShape shape = p.role == PropRole.LandmarkChapel ? BuildingTable.Church : PropRole.IsBuilding(p.role) ? BuildingTable.For(p.role, p.variant) : null;
                if (shape == null) continue;
                Vector3[] outline = shape.ExtentOutline().Select(c => BuildingTable.ToWorld(p.position, p.yaw, c)).ToArray();
                foreach (Vector3 c in outline) Assert.That(roadDistance(c), Is.GreaterThanOrEqualTo(half + SettlementKit.Verge - 0.05), $"{p.role} off the verge");
                rects.Add(outline);
            }

            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++) Assert.That(Overlap(rects[i], rects[j]), Is.False, $"buildings {i} and {j} overlap");
            }

            // The town wall's modules (on its outline); the retaining walls and the castle's ring are CityWall modules too.
            // The castle on its rock closes the wall between its flanks (the town wall runs up to them): its own modules are not the town wall's.
            Func<Vector3, float, bool> onCastle = (q, m) => { hill.Local(d.X0 + q.x, d.Z0 + q.z, out float qu, out float qv); return hill.OnCastle(qu, qv, m); };
            // (A terrace's retaining modules meet the town wall's inner face: they are the hill's runs, not the town wall's.)
            var walls = d.Props.Where(p => p.role == PropRole.CityWall && Math.Abs(hill.Outside(d.X0 + p.position.x, d.Z0 + p.position.z)) < 2.5f && !onCastle(p.position, 1f)
                && !OnRun(hill, d.X0 + p.position.x, d.Z0 + p.position.z)).ToList();
            foreach (Vector3[] r in rects)
            {
                foreach (PropPlacement w in walls) Assert.That(Inside(r, w.position, 1.0f), Is.False, "a building on the wall");
            }

            foreach (var anchor in d.Anchors.Where(a => a.Kind == MedievalZen.People.AnchorKind.Door))
            {
                Vector3 at = anchor.Position;
                bool reached = roadDistance(at) < half + 8f || d.Cobbles.Any(c => Segment(at, c.x0, c.z0, c.x1, c.z1) < c.width * 0.5f + 6f);
                Assert.That(reached, Is.True, "a door off every street");
            }

            // The wall is closed apart from the two gates: walking round the centre, gaps between ground modules only at the gates.
            float cx = (float)(d.CentreX - d.X0), cz = (float)(d.CentreZ - d.Z0);
            var ring = walls.Where(w => Math.Min(Flat(w.position, st.GateIn), Flat(w.position, st.GateOut)) > 13f).OrderBy(w => Mathf.Atan2(w.position.z - cz, w.position.x - cx)).ToList();
            int gaps = 0, castleGaps = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector3 a = ring[i].position, b = ring[(i + 1) % ring.Count].position;
                if (Flat(a, b) <= CityComposer.Chord + 0.3f) continue;
                gaps++;
                Vector3 mid = (a + b) * 0.5f;
                if (hill.Castle != null && onCastle(mid, 4f)) { castleGaps++; gaps--; continue; }
                Assert.That(Math.Min(Flat(mid, st.GateIn), Flat(mid, st.GateOut)), Is.LessThan(16f), $"a gap away from the gates ({a.x:0},{a.z:0})-({b.x:0},{b.z:0}) gates ({st.GateIn.x:0},{st.GateIn.z:0}) ({st.GateOut.x:0},{st.GateOut.z:0})");
            }

            Assert.That(gaps, Is.EqualTo(2), "two gates");
            Assert.That(castleGaps, Is.EqualTo(hill.Castle != null ? 1 : 0), "the castle closes the wall between its flanks");

            // The gates: the span over the road clears it by 4.5 m; the gate towers leave the road and both verges free.
            Func<Vector3, double> roadHeight = v => g.Road(g.NearestS(d.X0 + v.x, d.Z0 + v.z, city.S)).Height;
            int spans = 0;
            foreach (PropPlacement w in walls.Where(w => roadDistance(w.position) < half + SettlementKit.Verge))
            {
                spans++;
                Assert.That(w.position.y - roadHeight(w.position), Is.GreaterThanOrEqualTo(CityComposer.MinGateClearance), "the gate's span clears the road");
            }

            Assert.That(spans, Is.GreaterThanOrEqualTo(4), "a span over the road at each gate");
            Assert.That(st.GateTowers.Count, Is.EqualTo(4));
            foreach (Vector3 tower in st.GateTowers)
            {
                Assert.That(roadDistance(tower) - 0.5 * CityComposer.TowerDiameter * CityComposer.GateTowerScale, Is.GreaterThanOrEqualTo(half + SettlementKit.Verge), "a gate tower off the road and its verge");
            }

            // Closed street fronts: neighbours on the main street, round the square and along the lanes 0.5-2 m apart (measured extents).
            Assert.That(st.FrontPairs.Count, Is.GreaterThanOrEqualTo(18), "closed street fronts");
            foreach (var (a, b) in st.FrontPairs)
            {
                Vector3[] ra = Outline(d.Props[a]), rb = Outline(d.Props[b]);
                double gap = double.MaxValue;
                for (int i = 0; i < ra.Length; i++)
                {
                    for (int j = 0; j < rb.Length; j++)
                    {
                        Vector3 b0 = rb[j], b1 = rb[(j + 1) % rb.Length], a0 = ra[i], a1 = ra[(i + 1) % ra.Length];
                        gap = Math.Min(gap, Math.Min(Segment(ra[i], b0.x, b0.z, b1.x, b1.z), Segment(rb[j], a0.x, a0.z, a1.x, a1.z)));
                    }
                }

                Assert.That(gap, Is.InRange(CityComposer.GapMin - 0.05, 4.5), "neighbours in a street front (a curving terrace opens up to 4.5 m at the back)");
            }

            // The keep in its walled yard; the orchard; the houses never the same preset twice in a row.
            Assert.That(st.OpenGrassShare, Is.LessThan(0.15f), "stone underfoot: open grass inside the wall");
            List<PropPlacement> houses = d.Props.Where(p => p.role == PropRole.House).ToList();
            for (int i = 1; i < houses.Count; i++) Assert.That(houses[i].variant, Is.Not.EqualTo(houses[i - 1].variant), "the same preset twice in a row");
            Assert.That(roadDistance(st.GateIn), Is.LessThan(0.5) );
            Assert.That(roadDistance(st.GateOut), Is.LessThan(0.5));
        }

        /// <summary>
        /// No other place moves when a village slot becomes a city: against the plan with cities switched off, over the first 60 km, every
        /// waypoint that is not a city keeps its id, kind, name, road distance and position. Intended differences: the city's slot keeps its
        /// id and road distance (kind, radius and name change); a milestone within the city's reach (radius + 20 m, as for any settlement)
        /// is not set; ground heights from 600 m before a city to 10 km after it may shift by up to 1 m (the road's grade-limited profile
        /// re-settles after the city's level stretch; measured at most 0.72 m).
        /// </summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void NoOtherPlaceMoves_WhereASlotBecomesACity(int seed)
        {
            var with = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            var without = new EndlessGenerator(new EndlessParams { seed = seed, walledCities = false }, PaletteKeys.Default);
            var a = new List<Waypoint>();
            var b = new List<Waypoint>();
            with.Waypoints(-2000.0, 60000.0, a);
            without.Waypoints(-2000.0, 60000.0, b);
            List<Waypoint> cities = a.Where(w => w.Kind == WaypointKind.City).ToList();
            Assert.That(cities.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(b.Count(w => w.Kind == WaypointKind.City), Is.EqualTo(0));
            var after = a.GroupBy(w => w.Id).ToDictionary(x => x.Key, x => x.First());
            var before = new HashSet<int>(b.Select(w => w.Id));
            int gone = 0;
            foreach (Waypoint w in b)
            {
                bool reach = cities.Any(c => Math.Abs(c.S - w.S) < c.Radius + 20.0);
                if (!after.TryGetValue(w.Id, out Waypoint v))
                {
                    Assert.That(w.Kind == WaypointKind.Milestone && reach, Is.True, $"seed {seed}: {w.Kind} {w.Name} at {w.S:0} m is gone");
                    gone++;
                    continue;
                }

                if (v.Kind == WaypointKind.City)
                {
                    Assert.That(w.Kind == WaypointKind.Village || w.Kind == WaypointKind.Hamlet, Is.True);
                    Assert.That(v.S, Is.EqualTo(w.S), "the slot keeps its road distance");
                    continue;
                }

                Assert.That(v.Kind, Is.EqualTo(w.Kind), $"seed {seed}: {w.Name} at {w.S:0} m");
                Assert.That(v.Name, Is.EqualTo(w.Name), $"seed {seed}: {w.Kind} at {w.S:0} m");
                Assert.That(v.S, Is.EqualTo(w.S), $"seed {seed}: {w.Kind} {w.Name}");
                Assert.That(v.X, Is.EqualTo(w.X).Within(1e-6), $"seed {seed}: {w.Kind} {w.Name}");
                Assert.That(v.Z, Is.EqualTo(w.Z).Within(1e-6), $"seed {seed}: {w.Kind} {w.Name}");
                bool settles = cities.Any(c => w.S > c.S - 600.0 && w.S < c.S + 10000.0);
                Assert.That(Math.Abs(v.Height - w.Height), Is.LessThanOrEqualTo(settles ? 1.0 : 0.005), $"seed {seed}: {w.Kind} {w.Name} at {w.S:0} m: height");
            }

            Assert.That(a.All(v => before.Contains(v.Id)), Is.True, $"seed {seed}: no new place");
            Assert.That(gone, Is.LessThanOrEqualTo(cities.Count), "at most one milestone per city");
        }

        /// <summary>
        /// The hill town (2026-10-06; 5 m terraces 2026-10-09): one side of the road rises in 4 terraces to a plateau 20 m up where the castle stands; every
        /// terrace face is held by a retaining wall; switchback ramps of at most 12 % climb from the main street to the castle's gate; the
        /// layout offers slots for the mined set pieces and fills them from the tables.
        /// </summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void CityStandsOnATerracedHill(int seed)
        {
            var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            Place city = Cities(g, 60000.0).First();
            Dressing d = g.Dressings.Settlement(city);
            CityStats st = d.City;
            CityHill hill = CityHill.For(g.Params.seed, city.Id, d.CentreX, d.CentreZ, g.Curve.Heading(d.CentreZ));
            float level = g.Terrain.Height(d.CentreX, d.CentreZ);
            Assert.That(hill.Faces, Is.InRange(3, 4), "terraces");
            Assert.That(hill.Top, Is.InRange(18f, 26f), "the plateau above the road (5 m terraces since 2026-10-09)");
            Assert.That(st.HillHeight, Is.EqualTo(hill.Top).Within(1e-3f));
            hill.World(0f, CityHill.PlateauV + 6f, out double px, out double pz);
            Assert.That(g.Terrain.Height(px, pz) - level, Is.EqualTo(hill.Top).Within(1.0f), "the terrain carries the plateau");
            for (int i = 0; i + 1 < hill.Faces; i++) Assert.That(hill.Q[i] - hill.Q[i + 1], Is.InRange(14f, 24f), "a bench's depth");

            // Every face held by a retaining wall: along each contour inside the wall, away from the ways and the road, a wall module within 3.5 m.
            var walls = d.Props.Where(p => p.role == PropRole.CityWall).Select(p => new Vector3(p.position.x + (float)(d.X0 - d.CentreX), 0f, p.position.z + (float)(d.Z0 - d.CentreZ))).ToList();
            for (int f = 0; f < hill.Faces; f++)
            {
                int n = 0, held = 0;
                for (float phi = -1.4f; phi <= 1.4f; phi += 0.02f)
                {
                    hill.Contour(hill.Q[f], phi, out float u, out float v);
                    hill.World(u, v, out double x, out double z);
                    if (hill.Outside(x, z) > -6f || hill.OnWay(u, v, out _) || Near(hill, u, v, 4f)) continue;
                    g.Curve.Nearest(x, z, out double rd);
                    if (rd < 10.0) continue;
                    n++;
                    var at = new Vector3((float)(x - d.CentreX), 0f, (float)(z - d.CentreZ));
                    if (walls.Any(w => Flat(w, at) < 3.5f)) held++;
                }

                if (n < 6) continue;   // the top face is mostly the last ramp
                Assert.That(held / (double)n, Is.GreaterThanOrEqualTo(0.9), $"face {f} held by a retaining wall");
            }

            // The ramps: at most 12 %, from the lower town (level 0) to the plateau, a switchback chain whose top reaches the castle's gate.
            var ramps = hill.Ways.Where(w => !w.Stair).ToList();
            Assert.That(ramps.Count, Is.EqualTo(hill.Faces));
            Assert.That(st.RampGradeMax, Is.LessThanOrEqualTo(0.12f), "the ramps' grade");
            Assert.That(ramps[0].H[0], Is.EqualTo(0f).Within(1e-3f));
            Assert.That(ramps[ramps.Count - 1].H.Last(), Is.EqualTo(hill.Top).Within(1e-3f));
            for (int i = 0; i + 1 < ramps.Count; i++) Assert.That(ramps[i].H.Last(), Is.EqualTo(ramps[i + 1].H[0]).Within(1e-3f), "one ramp's top is the next one's foot");
            Assert.That(ramps[0].V[0], Is.LessThan(CityHill.FootV + 8f), "the first ramp starts at the lower town");
            Assert.That(Flat(st.RampTop, st.CastleGate), Is.LessThan(70f), "the last ramp ends by the castle's gate");
            Assert.That(d.Cobbles.Any(c => Segment(st.CastleGate, c.x0, c.z0, c.x1, c.z1) < c.width * 0.5f + 1f), Is.True, "a street to the castle's gate");
            Assert.That(hill.Ways.Count(w => w.Stair), Is.InRange(1, 2), "flights of steps");

            // The set pieces: slots of every kind the layout has, filled from the tables when they hold any.
            foreach (VignetteKind kind in new[] { VignetteKind.Stall, VignetteKind.Plaza, VignetteKind.WallFoot, VignetteKind.DoorSide, VignetteKind.Corner, VignetteKind.Yard })
                Assert.That(st.Slots.ContainsKey(kind) && st.Slots[kind] > 0, Is.True, $"{kind} slots");
            foreach (var kv in st.SetPieces) Assert.That(kv.Value, Is.LessThanOrEqualTo(st.Slots[kv.Key]));
            if (CitySetPieces.Vignettes.Count > 0) Assert.That(st.SetPieces.Values.Sum(), Is.GreaterThanOrEqualTo(10), "set pieces placed");
            Assert.That(st.Castle, Is.Not.Null.And.Not.Empty, "a castle on the plateau");
        }

        /// <summary>Whether an absolute point is a retaining module's centre: 0.25 m behind one of the hill's runs, away from its ends (a terrace's wall, not the town wall).</summary>
        private static bool OnRun(CityHill h, double x, double z)
        {
            h.Local(x, z, out float u, out float v);
            foreach (CityHill.Run r in h.Runs)
            {
                for (int i = 1; i < r.U.Length; i++)
                {
                    float ex = r.U[i] - r.U[i - 1], ez = r.V[i] - r.V[i - 1], l2 = ex * ex + ez * ez;
                    float t = l2 > 1e-9f ? ((u - r.U[i - 1]) * ex + (v - r.V[i - 1]) * ez) / l2 : 0f;
                    if (t < 0f || t > 1f) continue;
                    float du = r.U[i - 1] + ex * t - u, dv = r.V[i - 1] + ez * t - v;
                    if (du * du + dv * dv < 0.45f * 0.45f) return true;
                }
            }

            return false;
        }

        private static bool Near(CityHill h, float u, float v, float margin)
        {
            foreach (CityHill.Way w in h.Ways)
            {
                for (int i = 0; i < w.U.Length; i++) if ((w.U[i] - u) * (w.U[i] - u) + (w.V[i] - v) * (w.V[i] - v) < (w.Half + margin) * (w.Half + margin)) return true;
            }

            return false;
        }

        private static Vector3[] Outline(PropPlacement p) =>
            (p.role == PropRole.LandmarkChapel ? BuildingTable.Church : BuildingTable.For(p.role, p.variant)).ExtentOutline().Select(c => BuildingTable.ToWorld(p.position, p.yaw, c)).ToArray();

        private static float Flat(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

        private static double Segment(Vector3 p, float x0, float z0, float x1, float z1)
        {
            double ex = x1 - x0, ez = z1 - z0, len = ex * ex + ez * ez;
            double t = len > 1e-9 ? Math.Max(0.0, Math.Min(1.0, ((p.x - x0) * ex + (p.z - z0) * ez) / len)) : 0.0;
            double dx = x0 + ex * t - p.x, dz = z0 + ez * t - p.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static bool Inside(Vector3[] poly, Vector3 p, float margin)
        {
            bool left = true, right = true;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Length];
                float ex = b.x - a.x, ez = b.z - a.z, len = Mathf.Sqrt(ex * ex + ez * ez);
                float cross = (ex * (p.z - a.z) - ez * (p.x - a.x)) / Mathf.Max(1e-6f, len);
                if (cross < -margin) left = false;
                if (cross > margin) right = false;
            }

            return left || right;
        }

        private static bool Overlap(Vector3[] a, Vector3[] b) => !Separated(a, b) && !Separated(b, a);

        private static bool Separated(Vector3[] a, Vector3[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                Vector3 p = a[i], q = a[(i + 1) % a.Length];
                float nx = -(q.z - p.z), nz = q.x - p.x;
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (Vector3 v in a) { float t = v.x * nx + v.z * nz; amin = Math.Min(amin, t); amax = Math.Max(amax, t); }
                foreach (Vector3 v in b) { float t = v.x * nx + v.z * nz; bmin = Math.Min(bmin, t); bmax = Math.Max(bmax, t); }
                if (amax <= bmin + 1e-3f || bmax <= amin + 1e-3f) return true;
            }

            return false;
        }
    }
}
