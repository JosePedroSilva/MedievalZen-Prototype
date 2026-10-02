using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MedievalZen.Endless;
using MedievalZen.Roads;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>Tile selection, priorities and hysteresis of the endless road's streamer (pure planning).</summary>
    public sealed class EndlessTileStreamingTests
    {
        private static StreamingRanges Ranges() => new StreamingRanges { TileSize = 256f, Near = 420f, Far = 2200f, Margin = 160f, AheadBias = 0.35f };

        [Test]
        public void DistanceToTileIsZeroInsideAndMeasuresToTheNearestEdge()
        {
            var c = new TileCoord(0, 0);
            Assert.That(TileStreaming.DistanceToTile(100.0, 100.0, c, 256f), Is.EqualTo(0f));
            Assert.That(TileStreaming.DistanceToTile(300.0, 100.0, c, 256f), Is.EqualTo(44f).Within(1e-3f));
            Assert.That(TileStreaming.DistanceToTile(-30.0, -40.0, c, 256f), Is.EqualTo(50f).Within(1e-3f));
        }

        [Test]
        public void PlanSelectsNearTilesInsideTheNearDistanceAndFarTilesBeyond()
        {
            StreamingRanges r = Ranges();
            var wants = new List<TileWant>();
            TileStreaming.Plan(128.0, 128.0, 0f, 1f, r, null, wants);
            Assert.That(wants.Count, Is.GreaterThan(100));
            var seen = new HashSet<TileCoord>();
            foreach (TileWant w in wants)
            {
                Assert.That(seen.Add(w.Coord), Is.True, "each tile once");
                float d = TileStreaming.DistanceToTile(128.0, 128.0, w.Coord, r.TileSize);
                Assert.That(w.Lod, Is.EqualTo(d <= r.Near ? TileLod.Near : TileLod.Far), $"{w.Coord} at {d:0} m");
                Assert.That(d, Is.LessThanOrEqualTo(r.Far));
            }

            Assert.That(wants[0].Coord, Is.EqualTo(new TileCoord(0, 0)), "the tile under the traveller comes first");
        }

        [Test]
        public void NearTilesComeBeforeFarTilesAndAheadBeforeBehind()
        {
            StreamingRanges r = Ranges();
            var wants = new List<TileWant>();
            TileStreaming.Plan(128.0, 128.0, 0f, 1f, r, null, wants);
            bool sawFar = false;
            int ahead = -1, behind = -1;
            for (int i = 0; i < wants.Count; i++)
            {
                if (wants[i].Lod == TileLod.Far) sawFar = true;
                else Assert.That(sawFar, Is.False, "a near tile after a far one");
                if (wants[i].Coord == new TileCoord(0, 2)) ahead = i;
                if (wants[i].Coord == new TileCoord(0, -2)) behind = i;
            }

            Assert.That(ahead, Is.GreaterThanOrEqualTo(0));
            Assert.That(behind, Is.GreaterThanOrEqualTo(0));
            Assert.That(ahead, Is.LessThan(behind), "equally distant tiles: the one ahead along the direction of travel first");
        }

        [Test]
        public void HysteresisKeepsTheCurrentLevelOfDetailInsideTheMargin()
        {
            StreamingRanges r = Ranges();
            Assert.That(TileStreaming.Desired(null, r.Near - 1f, r), Is.EqualTo(TileLod.Near));
            Assert.That(TileStreaming.Desired(null, r.Near + 50f, r), Is.EqualTo(TileLod.Far), "a new tile only asks for near inside the near distance");
            Assert.That(TileStreaming.Desired(TileLod.Near, r.Near + 50f, r), Is.EqualTo(TileLod.Near), "a near tile stays near inside the margin");
            Assert.That(TileStreaming.Desired(TileLod.Near, r.Near + r.Margin + 1f, r), Is.EqualTo(TileLod.Far));
            Assert.That(TileStreaming.Desired(TileLod.Far, r.Near - 1f, r), Is.EqualTo(TileLod.Near), "far upgrades to near inside the near distance");
            Assert.That(TileStreaming.Desired(null, r.Far + 50f, r), Is.Null);
            Assert.That(TileStreaming.Desired(TileLod.Far, r.Far + 50f, r), Is.EqualTo(TileLod.Far), "a far tile stays inside the margin");
            Assert.That(TileStreaming.Desired(TileLod.Far, r.Far + r.Margin + 1f, r), Is.Null);
            Assert.That(TileStreaming.Desired(TileLod.Near, r.Far + r.Margin + 1f, r), Is.Null);
        }

        [Test]
        public void StandingOnABoundaryDoesNotThrash()
        {
            StreamingRanges r = Ranges();
            var current = new Dictionary<TileCoord, TileLod>();
            var wants = new List<TileWant>();
            int changes = 0;

            // Tile (0, 2) starts at z = 512: the near boundary for it is z = 92. Wobble around it.
            for (int step = 0; step < 200; step++)
            {
                double z = 92.0 + (step % 2 == 0 ? 4.0 : -4.0) + Math.Sin(step * 0.7) * 3.0;
                TileStreaming.Plan(128.0, z, 0f, 1f, r, c => current.TryGetValue(c, out TileLod l) ? l : (TileLod?)null, wants);
                var next = new Dictionary<TileCoord, TileLod>();
                foreach (TileWant w in wants) next[w.Coord] = w.Lod;
                if (step > 0)
                {
                    foreach (KeyValuePair<TileCoord, TileLod> pair in next)
                    {
                        if (current.TryGetValue(pair.Key, out TileLod before) && before != pair.Value) changes++;
                    }

                    foreach (TileCoord c in current.Keys)
                    {
                        if (!next.ContainsKey(c)) changes++;
                    }
                }

                current = next;
            }

            Assert.That(changes, Is.LessThanOrEqualTo(2), "a tile flipped back and forth at a boundary");
        }

        [Test]
        public void TurningRoundKeepsTheTilesJustPassed()
        {
            StreamingRanges r = Ranges();
            var current = new Dictionary<TileCoord, TileLod>();
            var wants = new List<TileWant>();
            for (double z = 0.0; z <= 600.0; z += 10.0) Step(z, 1f);
            var atTurn = new Dictionary<TileCoord, TileLod>(current);
            for (double z = 600.0; z >= 500.0; z -= 10.0) Step(z, -1f);

            // 100 m back: everything that was near at the turn is still at least far, and still near if within the margin.
            foreach (KeyValuePair<TileCoord, TileLod> pair in atTurn)
            {
                Assert.That(current.ContainsKey(pair.Key), Is.True, $"{pair.Key} was released after turning round");
            }

            void Step(double z, float dir)
            {
                TileStreaming.Plan(128.0, z, 0f, dir, r, c => current.TryGetValue(c, out TileLod l) ? l : (TileLod?)null, wants);
                current = new Dictionary<TileCoord, TileLod>();
                foreach (TileWant w in wants) current[w.Coord] = w.Lod;
            }
        }
    }

    /// <summary>The floating origin's maths.</summary>
    public sealed class FloatingOriginTests
    {
        [Test]
        public void SceneAndAbsoluteRoundTripFarFromTheStart()
        {
            var origin = new FloatingOrigin(256f, 1_000_000.3, -2_000_000.7);
            Assert.That(origin.X % 256.0, Is.EqualTo(0.0), "origin on a tile corner");
            Assert.That(origin.Z % 256.0, Is.EqualTo(0.0));
            double x = 1_000_123.456, z = -2_000_045.125;
            Vector3 scene = origin.ToScene(x, 12.5, z);
            Assert.That(Math.Abs(scene.x), Is.LessThan(400f), "scene coordinates stay small");
            (double ax, double az) = origin.ToAbsolute(scene);
            Assert.That(ax, Is.EqualTo(x).Within(1e-3));
            Assert.That(az, Is.EqualTo(z).Within(1e-3));
            Assert.That(scene.y, Is.EqualTo(12.5f));
        }

        [Test]
        public void ShiftsAreWholeTilesAndKeepAbsolutePositions()
        {
            var origin = new FloatingOrigin(256f);
            var traveller = new Vector3(4123.7f, 80f, -1000.2f);
            (double beforeX, double beforeZ) = origin.ToAbsolute(traveller);
            Assert.That(FloatingOrigin.NeedsShift(traveller, 4000f), Is.True);
            Assert.That(FloatingOrigin.NeedsShift(new Vector3(100f, 0f, 3900f), 4000f), Is.False);

            Vector3 delta = origin.DeltaToward(traveller);
            Assert.That(delta.x % 256f, Is.EqualTo(0f));
            Assert.That(delta.z % 256f, Is.EqualTo(0f));
            Assert.That(delta.y, Is.EqualTo(0f));
            Vector3 applied = origin.Shift(delta);
            Assert.That(applied, Is.EqualTo(delta));
            Vector3 moved = traveller + applied;
            Assert.That(Mathf.Abs(moved.x), Is.LessThanOrEqualTo(128f));
            Assert.That(Mathf.Abs(moved.z), Is.LessThanOrEqualTo(128f));
            (double afterX, double afterZ) = origin.ToAbsolute(moved);
            Assert.That(afterX, Is.EqualTo(beforeX).Within(1e-3), "the traveller's absolute position does not change");
            Assert.That(afterZ, Is.EqualTo(beforeZ).Within(1e-3));
        }

        [Test]
        public void ShiftRoundsAnOffGridDeltaToWholeTiles()
        {
            var origin = new FloatingOrigin(256f);
            Vector3 applied = origin.Shift(new Vector3(-300f, 0f, 130f));
            Assert.That(applied, Is.EqualTo(new Vector3(-256f, 0f, 256f)));
            Assert.That(origin.X, Is.EqualTo(256.0));
            Assert.That(origin.Z, Is.EqualTo(-256.0));
            Vector3 back = origin.MoveTo(0.0, 0.0);
            Assert.That(back, Is.EqualTo(new Vector3(256f, 0f, -256f)));
        }
    }

    /// <summary>The stub generator obeys the contract (so the runtime is tested against something honest).</summary>
    public sealed class StubEndlessGeneratorTests
    {
        private static EndlessParams Params(int seed = 7) => new EndlessParams { seed = seed };

        private static StubEndlessGenerator Stub(int seed = 7) => new StubEndlessGenerator(Params(seed), PaletteKeys.Default);

        [Test]
        public void NeighbouringTilesShareTheirEdgeHeightsExactly()
        {
            StubEndlessGenerator g = Stub();
            foreach (TileLod lod in new[] { TileLod.Near, TileLod.Far })
            {
                TileBlueprint a = g.GenerateTile(new TileCoord(0, 3), lod);
                TileBlueprint right = g.GenerateTile(new TileCoord(1, 3), lod);
                TileBlueprint up = g.GenerateTile(new TileCoord(0, 4), lod);
                int n = a.Heights.GetLength(0);
                for (int i = 0; i < n; i++)
                {
                    Assert.That(right.Heights[i, 0], Is.EqualTo(a.Heights[i, n - 1]), $"{lod} x-edge row {i}");
                    Assert.That(up.Heights[0, i], Is.EqualTo(a.Heights[n - 1, i]), $"{lod} z-edge column {i}");
                }
            }
        }

        [Test]
        public void TilesAreDeterministicAndOrderIndependent()
        {
            StubEndlessGenerator first = Stub(11);
            TileBlueprint a = first.GenerateTile(new TileCoord(0, 5), TileLod.Near);

            StubEndlessGenerator second = Stub(11);
            second.GenerateTile(new TileCoord(3, -40), TileLod.Far);   // touch the road far behind first
            second.Road(9000.0);
            TileBlueprint b = second.GenerateTile(new TileCoord(0, 5), TileLod.Near);
            AssertSame(a, b);
        }

        [Test]
        public void ConcurrentGenerationMatchesSequential()
        {
            var coords = new List<TileCoord>();
            for (int z = -2; z <= 5; z++) coords.Add(new TileCoord(z % 2, z));
            StubEndlessGenerator sequential = Stub(3);
            var expected = new List<TileBlueprint>();
            foreach (TileCoord c in coords) expected.Add(sequential.GenerateTile(c, TileLod.Near));

            StubEndlessGenerator shared = Stub(3);
            var results = new TileBlueprint[coords.Count];
            Parallel.For(0, coords.Count, i => results[coords.Count - 1 - i] = shared.GenerateTile(coords[coords.Count - 1 - i], TileLod.Near));
            for (int i = 0; i < coords.Count; i++) AssertSame(expected[i], results[i]);
        }

        [Test]
        public void TheRoadIsGradeLimitedWindsGentlyAndProjectsBackToItself()
        {
            StubEndlessGenerator g = Stub(5);
            float maxGrade = g.Params.maxRoadGrade;
            RoadPoint previous = g.Road(-3000.0);
            for (double s = -2990.0; s <= 9000.0; s += 10.0)
            {
                RoadPoint p = g.Road(s);
                Assert.That(Mathf.Abs(p.Grade), Is.LessThanOrEqualTo(maxGrade), $"grade at {s}");
                Assert.That(Mathf.Abs(p.HeadingDeg), Is.LessThan(60f), $"heading at {s}");
                double run = Math.Sqrt((p.X - previous.X) * (p.X - previous.X) + (p.Z - previous.Z) * (p.Z - previous.Z));
                Assert.That(run, Is.EqualTo(10.0).Within(0.05), $"arc length at {s}");
                Assert.That(Math.Abs(p.Height - previous.Height) / run, Is.LessThanOrEqualTo(maxGrade + 1e-3), $"height change at {s}");
                Assert.That(g.Height(p.X, p.Z), Is.EqualTo(p.Height).Within(0.01f), $"road bed at {s}");
                double h = p.HeadingDeg * Math.PI / 180.0;
                Assert.That(g.NearestS(p.X + Math.Cos(h) * 3.0, p.Z - Math.Sin(h) * 3.0, s), Is.EqualTo(s).Within(0.25), $"projection at {s}");
                previous = p;
            }
        }

        [Test]
        public void TilesAreWellFormed()
        {
            StubEndlessGenerator g = Stub(9);
            var startInns = new List<Waypoint>();
            g.Waypoints(-1.0, 1.0, startInns);
            Waypoint first = startInns.Find(w => w.Kind == WaypointKind.Inn);
            Assert.That(first, Is.Not.Null);
            TileBlueprint near = g.GenerateTile(TileCoord.Containing(first.X, first.Z, g.Params.tileSize), TileLod.Near);
            TileBlueprint far = g.GenerateTile(new TileCoord(4, 9), TileLod.Far);
            Assert.That(near.Heights.GetLength(0), Is.EqualTo(g.Params.nearHeightResolution));
            Assert.That(far.Heights.GetLength(0), Is.EqualTo(g.Params.farHeightResolution));
            Assert.That(far.Details, Is.Empty);
            Assert.That(far.Props, Is.Empty);
            Assert.That(near.DetailResolution, Is.EqualTo(g.Params.nearDetailResolution));
            foreach (TileBlueprint bp in new[] { near, far })
            {
                foreach (float h in bp.Heights) Assert.That(h, Is.InRange(0f, 1f));
                int res = bp.Splat.GetLength(0);
                for (int z = 0; z < res; z += 7)
                {
                    for (int x = 0; x < res; x += 7)
                    {
                        float sum = 0f;
                        for (int l = 0; l < bp.Splat.GetLength(2); l++) sum += bp.Splat[z, x, l];
                        Assert.That(sum, Is.EqualTo(1f).Within(1e-3f));
                    }
                }
            }

            Assert.That(near.Waypoints.Exists(w => w.Kind == WaypointKind.Inn && w.S == 0.0), Is.True, "the journey starts at an inn");
            Assert.That(near.Props.Exists(p => p.role == PropRole.Cabin), Is.True, "the first inn's cabin");
        }

        [Test]
        public void InnsComeEveryKilometreAndAHalfOrSo()
        {
            StubEndlessGenerator g = Stub(4);
            var found = new List<Waypoint>();
            g.Waypoints(-5000.0, 15000.0, found);
            var inns = found.FindAll(w => w.Kind == WaypointKind.Inn);
            Assert.That(inns.Count, Is.InRange(11, 16));
            for (int i = 1; i < inns.Count; i++)
            {
                double gap = inns[i].S - inns[i - 1].S;
                Assert.That(gap, Is.InRange(1000.0, 2000.0));
                Assert.That(inns[i].Name, Is.Not.Null.And.Not.Empty);
            }

            for (int i = 1; i < found.Count; i++) Assert.That(found[i].S, Is.GreaterThanOrEqualTo(found[i - 1].S), "ordered by distance");
            Assert.That(g.Region(0.0).SStart, Is.LessThan(0.0));
            Assert.That(g.Region(0.0).Index, Is.EqualTo(0));
            RegionInfo next = g.Region(g.Region(0.0).SEnd + 1.0);
            Assert.That(next.Index, Is.EqualTo(1));
            Assert.That(next.Kind, Is.EqualTo(RegionKind.LakeCountry));
        }

        private static void AssertSame(TileBlueprint a, TileBlueprint b)
        {
            Assert.That(b.Heights, Is.EqualTo(a.Heights));
            Assert.That(b.Splat, Is.EqualTo(a.Splat));
            Assert.That(b.WaterHeight, Is.EqualTo(a.WaterHeight));
            Assert.That(b.Trees.Count, Is.EqualTo(a.Trees.Count));
            for (int i = 0; i < a.Trees.Count; i++) Assert.That(b.Trees[i].position, Is.EqualTo(a.Trees[i].position));
            Assert.That(b.Props.Count, Is.EqualTo(a.Props.Count));
            for (int i = 0; i < a.Props.Count; i++)
            {
                Assert.That(b.Props[i].role, Is.EqualTo(a.Props[i].role));
                Assert.That(b.Props[i].position, Is.EqualTo(a.Props[i].position));
            }

            Assert.That(b.Details.Count, Is.EqualTo(a.Details.Count));
            for (int i = 0; i < a.Details.Count; i++) Assert.That(b.Details[i].density, Is.EqualTo(a.Details[i].density));
        }
    }

    /// <summary>The sliding road window (a RoadNetwork for travel) and the inn choices, on the stub generator.</summary>
    public sealed class EndlessRoadWindowTests
    {
        private static StubEndlessGenerator Stub() => new StubEndlessGenerator(new EndlessParams { seed = 21 }, PaletteKeys.Default);

        [Test]
        public void TheWindowIsOneConnectedRoadWithASettlementAtEveryInn()
        {
            StubEndlessGenerator g = Stub();
            RoadWindow w = RoadWindow.Build(g, -600.0, 2600.0, 0.0, 0.0);
            RoadNetwork net = w.Network;

            // A chain: every node reachable from node 0, n - 1 segments, ends have one segment.
            Assert.That(net.Segments.Count, Is.EqualTo(net.Nodes.Count - 1));
            var reached = new HashSet<RoadNode> { net.Nodes[0] };
            var open = new Queue<RoadNode>(reached);
            while (open.Count > 0)
            {
                RoadNode n = open.Dequeue();
                foreach (RoadSegment s in n.Segments)
                {
                    RoadNode other = s.GetOther(n);
                    if (reached.Add(other)) open.Enqueue(other);
                }
            }

            Assert.That(reached.Count, Is.EqualTo(net.Nodes.Count));
            float total = 0f;
            foreach (RoadSegment s in net.Segments) total += s.Length;
            Assert.That(total, Is.EqualTo(3200f).Within(10f), "the network follows the road's arc length");

            var found = new List<Waypoint>();
            g.Waypoints(-600.0, 2600.0, found);
            var inns = found.FindAll(x => x.Kind == WaypointKind.Inn);
            Assert.That(inns.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(net.Settlements.Count, Is.EqualTo(inns.Count));
            foreach (Waypoint inn in inns)
            {
                Settlement s = w.SettlementFor(inn.Id);
                Assert.That(s, Is.Not.Null, inn.Name);
                Assert.That(s.Type, Is.EqualTo(SettlementType.Inn));
                Assert.That(s.Name, Is.EqualTo(inn.Name));
                Assert.That(w.WaypointOf(s).Id, Is.EqualTo(inn.Id));
                RoadPoint p = g.Road(inn.S);
                Assert.That(Vector3.Distance(s.Position, new Vector3((float)p.X, p.Height, (float)p.Z)), Is.LessThan(0.01f), "settlement on the road at the inn");
            }
        }

        [Test]
        public void ReissuedRoutesKeepTheirLengthAcrossWindowsAndOriginShifts()
        {
            StubEndlessGenerator g = Stub();
            Waypoint inn = InnFinder.Find(g, 1000.0, +1);
            Assert.That(inn, Is.Not.Null);
            RoadPoint start = g.Road(1000.0);

            RoadWindow a = RoadWindow.Build(g, -600.0, 2600.0, 0.0, 0.0);
            RoadWindow b = RoadWindow.Build(g, 400.0, 3900.0, 0.0, 0.0);
            float la = RouteLength(a, inn, new Vector3((float)start.X, start.Height, (float)start.Z));
            float lb = RouteLength(b, inn, new Vector3((float)start.X, start.Height, (float)start.Z));
            Assert.That(lb, Is.EqualTo(la).Within(0.5f), "a rebuilt window gives the same remaining distance");
            Assert.That(la, Is.EqualTo((float)(inn.S - 1000.0)).Within(1f));

            // After an origin shift of (-2048, -4096) the translated window gives the same route from the moved position.
            var delta = new Vector3(-2048f, 0f, -4096f);
            RoadWindow moved = a.Translate(delta, 2048.0, 4096.0);
            float lm = RouteLength(moved, inn, new Vector3((float)start.X, start.Height, (float)start.Z) + delta);
            Assert.That(lm, Is.EqualTo(la).Within(0.05f));
            RoadWindow rebuilt = RoadWindow.Build(g, -600.0, 2600.0, 2048.0, 4096.0);
            Assert.That(Vector3.Distance(rebuilt.SettlementFor(inn.Id).Position, moved.SettlementFor(inn.Id).Position), Is.LessThan(0.01f));
        }

        [Test]
        public void TheNextInnIsChosenByDirection()
        {
            var inns = new List<Waypoint>
            {
                new Waypoint { Id = 1, Kind = WaypointKind.Inn, S = -1500.0 },
                new Waypoint { Id = 2, Kind = WaypointKind.Inn, S = 0.0 },
                new Waypoint { Id = 3, Kind = WaypointKind.Milestone, S = 500.0 },
                new Waypoint { Id = 4, Kind = WaypointKind.Inn, S = 1400.0 },
                new Waypoint { Id = 5, Kind = WaypointKind.Inn, S = 2900.0 },
            };

            Assert.That(InnFinder.Select(inns, 10.0, +1).Id, Is.EqualTo(4), "standing at an inn, the next one ahead");
            Assert.That(InnFinder.Select(inns, 10.0, -1).Id, Is.EqualTo(1), "and behind");
            Assert.That(InnFinder.Select(inns, 100.0, -1).Id, Is.EqualTo(2));
            Assert.That(InnFinder.Select(inns, 1380.0, +1).Id, Is.EqualTo(5), "within the skip distance the inn counts as reached");
            Assert.That(InnFinder.Select(inns, 3000.0, +1), Is.Null);

            Assert.That(InnFinder.DirectionFor(Vector3.forward, 0f), Is.EqualTo(1));
            Assert.That(InnFinder.DirectionFor(Vector3.back, 0f), Is.EqualTo(-1));
            Assert.That(InnFinder.DirectionFor(Vector3.right, 30f), Is.EqualTo(1));
            Assert.That(InnFinder.DirectionFor(Vector3.left, 30f), Is.EqualTo(-1));

            StubEndlessGenerator g = Stub();
            Waypoint ahead = InnFinder.Find(g, 5.0, +1);
            Waypoint behind = InnFinder.Find(g, 5.0, -1);
            Assert.That(ahead.S, Is.GreaterThan(1000.0));
            Assert.That(behind.S, Is.LessThan(-1000.0));
            Assert.That(InnFinder.Find(g, ahead.S + 1.0, -1).S, Is.EqualTo(0.0), "from just past the next inn, back is the start inn");
        }

        private static float RouteLength(RoadWindow w, Waypoint inn, Vector3 from)
        {
            RoadLocation start = w.Network.Project(from);
            Assert.That(w.Network.TryFindRoute(start, w.SettlementFor(inn.Id), out RoadRoute route), Is.True);
            return route.TotalLength;
        }
    }

    /// <summary>The saved journey's JSON round trip and version rules.</summary>
    public sealed class EndlessJourneyTests
    {
        [Test]
        public void RoundTripKeepsSeedDistanceAndTime()
        {
            var j = new EndlessJourney { Seed = -12345, S = 15321.123456789, Day = 4, Hour = 17.25f };
            Assert.That(EndlessJourney.TryFromJson(j.ToJson(), out EndlessJourney back), Is.True);
            Assert.That(back.Seed, Is.EqualTo(-12345));
            Assert.That(back.S, Is.EqualTo(15321.123456789));
            Assert.That(back.Day, Is.EqualTo(4));
            Assert.That(back.Hour, Is.EqualTo(17.25f).Within(1e-4f));
            Assert.That(back.HasTime, Is.True);
        }

        [Test]
        public void ANewJourneyHasNoSavedTime()
        {
            EndlessJourney j = EndlessJourney.New(77);
            Assert.That(j.S, Is.EqualTo(0.0));
            Assert.That(j.HasTime, Is.False);
            Assert.That(EndlessJourney.TryFromJson(j.ToJson(), out EndlessJourney back), Is.True);
            Assert.That(back.Seed, Is.EqualTo(77));
            Assert.That(back.HasTime, Is.False);
            Assert.That(j.ToJson(), Does.Not.Contain("hour"));
        }

        [Test]
        public void VersionsAndBadBlobsAreHandled()
        {
            Assert.That(EndlessJourney.TryFromJson("{\"seed\":5,\"s\":12.5}", out EndlessJourney old), Is.True, "no version = oldest, read field by field");
            Assert.That(old.Seed, Is.EqualTo(5));
            Assert.That(old.S, Is.EqualTo(12.5));
            Assert.That(EndlessJourney.TryFromJson("{\"version\":2,\"seed\":5}", out EndlessJourney newer), Is.False, "a newer format is not half-read");
            Assert.That(newer, Is.Null);
            Assert.That(EndlessJourney.TryFromJson("{\"version\":1,\"s\":12.5}", out _), Is.False, "no seed, no journey");
            Assert.That(EndlessJourney.TryFromJson("{\"version\":1,\"seed\":5.5}", out _), Is.False);
            Assert.That(EndlessJourney.TryFromJson("not json", out _), Is.False);
            Assert.That(EndlessJourney.TryFromJson(string.Empty, out _), Is.False);
            Assert.That(EndlessJourney.TryFromJson("{\"version\":1,\"seed\":5,\"s\":1e300,\"day\":0,\"hour\":30,\"extra\":true}", out EndlessJourney odd), Is.True);
            Assert.That(odd.S, Is.EqualTo(0.0), "absurd distances fall back to the start");
            Assert.That(odd.HasTime, Is.False, "day 0 drops the time");
            Assert.That(EndlessJourney.TryFromJson("{\"version\":1,\"seed\":5,\"day\":2,\"hour\":30}", out EndlessJourney wrapped), Is.True);
            Assert.That(wrapped.Hour, Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void EnterToastReadsNaturally()
        {
            Assert.That(EndlessWorld.EnterToast("The Wexmoor Downs"), Is.EqualTo("You enter the Wexmoor Downs"));
            Assert.That(EndlessWorld.EnterToast("Ashford Vale"), Is.EqualTo("You enter Ashford Vale"));
        }
    }
}
