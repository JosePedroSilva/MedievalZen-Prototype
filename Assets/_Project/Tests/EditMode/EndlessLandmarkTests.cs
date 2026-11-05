using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The endless road's landmarks and field boundaries (2026-10-02): landmarks are planned deterministically, agree with the
    /// waypoints and between near and distant tiles, keep off the road, water, streams, villages and fields, stand on ground
    /// level enough for them and are seen from the road; field boundaries are deterministic, each piece belongs to one tile,
    /// they keep off the road, water, streams, settlements and the fields themselves, their gates are open, and the distant
    /// meshes that draw the structures and boundary ridges stay within their triangle caps. Pure (no Unity objects).
    /// </summary>
    public sealed class EndlessLandmarkTests
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

        private static List<LandmarkSite> Planned(EndlessGenerator g, double s0, double s1)
        {
            var list = new List<LandmarkSite>();
            g.Landmarks.Around(s0, s1, list);
            return list;
        }

        // ------------------------------------------------------------------ landmarks

        [Test]
        public void Landmarks_AreDeterministic_AndAreTheLandmarkWaypoints()
        {
            var a = new EndlessGenerator(new EndlessParams { seed = 42 }, PaletteKeys.Default);
            var b = new EndlessGenerator(new EndlessParams { seed = 42 }, PaletteKeys.Default);
            List<LandmarkSite> forward = Planned(a, 0.0, 16000.0);
            // The other generator plans the regions backwards, concurrently.
            var regions = new List<long>();
            for (Region r = b.Regions.At(0.0); r.Start <= 16000.0; r = b.Regions.Get(r.Index + 1)) regions.Add(r.Index);
            Parallel.ForEach(regions.AsEnumerable().Reverse(), i => b.Landmarks.Of(i));
            List<LandmarkSite> backward = Planned(b, 0.0, 16000.0);
            Assert.That(forward.Count, Is.GreaterThanOrEqualTo(5), "About one landmark every 1-2 km.");
            Assert.That(backward.Select(Key), Is.EqualTo(forward.Select(Key)));

            var waypoints = new List<Waypoint>();
            a.Waypoints(0.0, 16000.0, waypoints);
            var marks = waypoints.Where(w => w.Kind == WaypointKind.Landmark).ToList();
            Assert.That(marks.Select(w => (w.Id, w.X, w.Z, w.Name)), Is.EqualTo(forward.Select(l => (l.Id, l.X, l.Z, l.Name))), "Every landmark is a waypoint, and only they are.");
            Assert.That(marks.All(w => !string.IsNullOrEmpty(w.Name)), Is.True, "Landmarks are named.");
        }

        private static string Key(LandmarkSite l) => $"{l.Id} {l.Type} {l.X:R} {l.Z:R} {l.Ground:R} {l.Yaw:R} {l.Name}";

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void Landmarks_StandOnLevelDryGround_AwayFromRoadStreamsVillagesAndFields(int seed)
        {
            EndlessGenerator g = Gen(seed);
            List<LandmarkSite> list = Planned(g, -2000.0, 20000.0);
            Assert.That(list.Count, Is.GreaterThanOrEqualTo(8), $"seed {seed}: landmarks over 22 km.");
            var places = new List<Place>();
            g.Plan.Inns(-6000.0, 26000.0, places);
            g.Plan.RegionPlaces(-6000.0, 26000.0, places);
            foreach (LandmarkSite l in list)
            {
                string at = $"seed {seed} {l.Type} '{l.Name}' at s {l.S:0}";
                g.Curve.Nearest(l.X, l.Z, out double roadDistance);
                Assert.That(roadDistance, Is.GreaterThanOrEqualTo(LandmarkPlan.MinOffset - 0.01), at + ": off the road.");
                Assert.That(g.WaterHeight(l.X, l.Z), Is.LessThanOrEqualTo(0f), at + ": not in a lake.");
                if (g.Streams != null) Assert.That(g.Streams.Clearance(l.X, l.Z, l.Radius + 40.0), Is.GreaterThan(l.Radius + 29.0), at + ": clear of streams.");
                foreach (Place place in places)
                {
                    if (!place.IsSettlement) continue;
                    SettlementDisc disc = g.Terrain.Disc(place);
                    double d = Math.Sqrt((disc.X - l.X) * (disc.X - l.X) + (disc.Z - l.Z) * (disc.Z - l.Z));
                    Assert.That(d, Is.GreaterThan(place.Radius + l.Radius + 100.0), at + $": clear of {place.Name}.");
                }

                var fields = new List<MosaicField>();
                g.Fields.Overlapping(l.X - 60.0, l.Z - 60.0, 120.0, fields);
                foreach (MosaicField f in fields) Assert.That(FieldMosaic.Distance(f, l.X, l.Z), Is.GreaterThan(l.Radius + 11.9), at + ": clear of the fields.");

                // Level enough: the ground under the base varies by no more than the type allows, and the floor sits within it.
                float r = LandmarkPlan.BaseRadiusOf(l.Type), lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i <= 8; i++)
                {
                    double a = i * Math.PI / 4.0, rr = i == 8 ? 0.0 : r;
                    float h = g.Height(l.X + Math.Cos(a) * rr, l.Z + Math.Sin(a) * rr);
                    lo = Math.Min(lo, h);
                    hi = Math.Max(hi, h);
                }

                Assert.That(hi - lo, Is.LessThanOrEqualTo(LandmarkPlan.FlatnessOf(l.Type) + 1e-3f), at + ": level ground.");
                if (l.Type != LandmarkType.Stones)
                {
                    Assert.That(l.Ground, Is.LessThanOrEqualTo(hi + 1e-3f), at + ": the floor is not above the ground.");
                    Assert.That(lo - (l.Ground - LandmarkPlan.FoundationOf(l.Type)), Is.GreaterThan(0f), at + ": the foundation reaches below the lowest ground.");
                }

                Assert.That(l.VisibleRoad, Is.GreaterThanOrEqualTo(100f), at + ": seen from the road.");
            }
        }

        [Test]
        public void LandmarkStructure_IsInOneNearTile_AndDrawnOnTheDistantTile()
        {
            EndlessGenerator g = Gen(12345);
            float T = g.Params.tileSize;
            List<LandmarkSite> list = Planned(g, 0.0, 20000.0).Where(l => l.Type != LandmarkType.Stones).Take(3).ToList();
            Assert.That(list, Is.Not.Empty);
            foreach (LandmarkSite l in list)
            {
                TileCoord c = TileCoord.Containing(l.X, l.Z, T);
                var structures = new List<(TileCoord tile, PropPlacement prop)>();
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var t = new TileCoord(c.X + dx, c.Z + dz);
                        TileBlueprint near = g.GenerateTile(t, TileLod.Near);
                        foreach (PropPlacement p in near.Props)
                        {
                            if (p.settlementId == l.Id && PropRole.IsLandmark(p.role)) structures.Add((t, p));
                        }
                    }
                }

                Assert.That(structures.Count, Is.EqualTo(1), $"{l.Name}: exactly one structure among the near tiles.");
                (TileCoord tile, PropPlacement s) = structures[0];
                Assert.That(tile.X == c.X && tile.Z == c.Z, Is.True, $"{l.Name}: in the tile that contains its centre.");
                Assert.That(tile.OriginX(T) + s.position.x, Is.EqualTo(l.X).Within(0.01));
                Assert.That(tile.OriginZ(T) + s.position.z, Is.EqualTo(l.Z).Within(0.01));
                Assert.That(s.position.y, Is.EqualTo(l.Ground).Within(1e-4f));

                foreach (TileLod lod in new[] { TileLod.Mid, TileLod.Far })
                {
                    TileBlueprint far = g.GenerateTile(c, lod);
                    PropPlacement[] solids = far.Props.Where(p => p.role == s.role && p.settlementId == l.Id).ToArray();
                    Assert.That(solids.Length, Is.EqualTo(1), $"{l.Name}: drawn on the {lod} tile.");
                    Assert.That(Vector3.Distance(solids[0].position, s.position), Is.LessThan(0.01f), $"{l.Name}: where the near tile has it.");
                    FarTileMeshData mesh = FarTileMeshData.Build(far, FarTreeStyles.Defaults, new FarMeshSettings());
                    Assert.That(mesh.HousesDrawn, Is.GreaterThanOrEqualTo(1));
                    Assert.That(mesh.Triangles, Is.LessThanOrEqualTo(FarTileMeshData.CapFor(mesh.GridResolution, mesh.TileSize, 0)));
                }
            }
        }

        [Test]
        public void LandmarkSolids_HaveTheirCountedTriangles_AndTheMeasuredHeight()
        {
            foreach (string role in new[] { PropRole.LandmarkTower, PropRole.LandmarkChapel, PropRole.LandmarkMill, PropRole.LandmarkRuin, PropRole.StoneRunes })
            {
                for (int variant = 0; variant < 2; variant++)
                {
                    var bp = new TileBlueprint { TileSize = 256f, HeightRange = 400f, Heights = new float[33, 33] };
                    bp.Props.Add(new PropPlacement { role = role, variant = variant, position = new Vector3(128f, 0f, 128f), yaw = 30f, scale = 1f, settlementId = 10 });
                    var settings = new FarMeshSettings { Drop = 0f };
                    FarTileMeshData mesh = FarTileMeshData.Build(bp, FarTreeStyles.Defaults, settings);
                    FarPropStyle style = FarPropStyles.Defaults.Resolve(role, variant);
                    Assert.That(mesh.HouseTriangles, Is.EqualTo(FarTileMeshData.HouseTrianglesOf(style.Kind)), role);
                    if (style.Kind == FarPropKind.Ruin)
                    {
                        Assert.That(mesh.BoundsMax.y, Is.InRange(0.45f * style.Max.y, style.Max.y + 0.01f), $"{role} {variant}: the broken walls stand no higher than the prefab's.");
                        continue;
                    }

                    float expected = style.Kind == FarPropKind.Tower || style.Kind == FarPropKind.Chapel ? style.Peak : style.Kind == FarPropKind.Mill ? Math.Max(style.Ridge, style.Hub.y + style.Arm * 0.71f) : style.Max.y;
                    Assert.That(mesh.BoundsMax.y, Is.EqualTo(expected).Within(0.6f), $"{role} {variant}: the solid is as tall as the prefab.");
                }
            }
        }

        // ------------------------------------------------------------------ field boundaries

        private static TileCoord FarmlandTile(EndlessGenerator g, out double s)
        {
            float T = g.Params.tileSize;
            for (s = 0.0; s < 60000.0; s += 200.0)
            {
                if (g.Region(s).Kind != RegionKind.Farmland) continue;
                RoadPoint rp = g.Road(s);
                foreach (double off in new[] { 180.0, -180.0, 320.0, -320.0 })
                {
                    double rad = rp.HeadingDeg * Math.PI / 180.0;
                    TileCoord c = TileCoord.Containing(rp.X + Math.Cos(rad) * off, rp.Z - Math.Sin(rad) * off, T);
                    if (g.GenerateTile(c, TileLod.Near).Pieces.Count > 150) return c;
                }
            }

            Assert.Fail("No farmland tile with field boundaries found.");
            return default;
        }

        [Test]
        public void Boundaries_AreDeterministic_AndEachPieceBelongsToOneTile()
        {
            EndlessGenerator g = Gen(12345);
            TileCoord c = FarmlandTile(g, out _);
            float T = g.Params.tileSize;
            var fresh = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);
            var coords = new List<TileCoord> { new TileCoord(c.X + 1, c.Z), c, new TileCoord(c.X, c.Z + 1), new TileCoord(c.X + 1, c.Z + 1) };
            foreach (TileCoord t in coords)
            {
                TileBlueprint a = g.GenerateTile(t, TileLod.Near), b = fresh.GenerateTile(t, TileLod.Near);
                Assert.That(b.Pieces.Count, Is.EqualTo(a.Pieces.Count));
                for (int i = 0; i < a.Pieces.Count; i++)
                {
                    Assert.That(b.Pieces[i].role, Is.EqualTo(a.Pieces[i].role));
                    Assert.That(b.Pieces[i].position, Is.EqualTo(a.Pieces[i].position));
                    Assert.That(b.Pieces[i].yaw, Is.EqualTo(a.Pieces[i].yaw));
                    Assert.That(b.Pieces[i].pitch, Is.EqualTo(a.Pieces[i].pitch));
                }

                foreach (PropPlacement p in a.Pieces)
                {
                    // Ivy (forest pass) hangs on the face of a wall piece of this tile: within a metre of it.
                    float slack = PropRole.IsOvergrowth(p.role) ? 1f : 0f;
                    Assert.That(p.position.x, Is.InRange(-slack, T + slack), "A piece lies in the tile that owns it.");
                    Assert.That(p.position.z, Is.InRange(-slack, T + slack));
                    Assert.That(PropRole.IsBoundaryPiece(p.role) || PropRole.IsOvergrowth(p.role), Is.True, p.role);
                }
            }

            // No piece twice across the four tiles (in absolute coordinates).
            var seen = new HashSet<(long, long, string)>();
            foreach (TileCoord t in coords)
            {
                foreach (PropPlacement p in g.GenerateTile(t, TileLod.Near).Pieces)
                {
                    var key = ((long)Math.Round((t.OriginX(T) + p.position.x) * 100.0), (long)Math.Round((t.OriginZ(T) + p.position.z) * 100.0), p.role);
                    Assert.That(seen.Add(key), Is.True, "A piece is emitted by one tile only.");
                }
            }
        }

        [TestCase(12345)]
        [TestCase(777)]
        public void Boundaries_KeepOffRoadWaterStreamsSettlementsAndFields_AndFollowTheGround(int seed)
        {
            EndlessGenerator g = Gen(seed);
            TileCoord c = FarmlandTile(g, out double s);
            float T = g.Params.tileSize;
            float corridor = g.Params.roadWidth * 0.5f + 8f;
            var kinds = new HashSet<string>();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var t = new TileCoord(c.X + dx, c.Z + dz);
                    TileBlueprint bp = g.GenerateTile(t, TileLod.Near);
                    var fields = new List<MosaicField>();
                    g.Fields.Overlapping(t.OriginX(T) - 20.0, t.OriginZ(T) - 20.0, T + 40.0, fields);
                    foreach (PropPlacement p in bp.Pieces)
                    {
                        kinds.Add(p.role);
                        double ax = t.OriginX(T) + p.position.x, az = t.OriginZ(T) + p.position.z;
                        g.Curve.Nearest(ax, az, out double d);
                        Assert.That(d, Is.GreaterThan(corridor), $"{p.role} at ({ax:0},{az:0}) is {d:0.0} m from the road centre.");
                        Assert.That(g.WaterHeight(ax, az), Is.LessThanOrEqualTo(0f), "No piece in a lake.");
                        if (g.Streams != null) Assert.That(g.Streams.Clearance(ax, az, 10.0), Is.GreaterThan(3.9f), "No piece on a stream or its banks.");
                        foreach (MosaicField f in fields) Assert.That(InsideDepth(f, ax, az), Is.LessThan(0.5), "No piece inside a field (a piece may touch its edge).");
                        float ground = g.Height(ax, az);
                        // Ivy (forest pass) hangs from a wall's top edge, 1.1 m up.
                        float lift = PropRole.IsOvergrowth(p.role) ? 1.3f : 0f;
                        Assert.That(p.position.y, Is.InRange(ground - 1.0f, ground + 0.15f + lift), $"{p.role} sits on the ground (sunk a little), not floating.");
                        Assert.That(Math.Abs(p.pitch), Is.LessThan(20f), "Pieces tilt with gentle ground only.");
                    }
                }
            }

            Assert.That(kinds.Count, Is.GreaterThanOrEqualTo(3), "Several kinds of piece (walls, hedges, fences and their posts, pillars, gates).");
        }

        /// <summary>How deep (x, z) lies inside the field's rectangle, metres (negative outside).</summary>
        private static double InsideDepth(MosaicField f, double x, double z)
        {
            double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            double dx = x - f.X, dz = z - f.Z;
            double u = dx * cs - dz * sn, v = dx * sn + dz * cs;
            return Math.Min(f.HalfX - Math.Abs(u), f.HalfZ - Math.Abs(v));
        }

        [Test]
        public void Boundaries_LeaveEveryGateOpen()
        {
            EndlessGenerator g = Gen(42);
            TileCoord c = FarmlandTile(g, out _);
            float T = g.Params.tileSize;
            var edges = new List<BoundaryEdge>();
            g.Boundaries.Overlapping(c.OriginX(T), c.OriginZ(T), T, 0.0, edges);
            TileBlueprint bp = g.GenerateTile(c, TileLod.Near);
            int gates = 0;
            foreach (BoundaryEdge e in edges)
            {
                foreach (float gate in new[] { e.Gate0, e.Gate1 })
                {
                    if (float.IsNaN(gate)) continue;
                    double len = e.Length, dx = (e.X1 - e.X0) / len, dz = (e.Z1 - e.Z0) / len;
                    double gx = e.X0 + dx * gate, gz = e.Z0 + dz * gate;
                    if (gx < c.OriginX(T) || gx >= c.OriginX(T) + T || gz < c.OriginZ(T) || gz >= c.OriginZ(T) + T) continue;
                    gates++;
                    foreach (PropPlacement p in bp.Pieces)
                    {
                        if (p.role != PropRole.StoneWall && p.role != PropRole.StoneWallSmall && p.role != PropRole.Fence && p.role != PropRole.Hedge) continue;
                        double px = c.OriginX(T) + p.position.x - gx, pz = c.OriginZ(T) + p.position.z - gz;
                        double along = px * dx + pz * dz, across = Math.Abs(-px * dz + pz * dx);
                        if (across > 1.5) continue;
                        // Half the piece's length: the palette's wall and fence pieces, the largest hedge piece (5.03 m x 1.3 x 1.12).
                        float half = (p.role == PropRole.StoneWall ? g.Keys.wallLength : p.role == PropRole.StoneWallSmall ? 2.85f : p.role == PropRole.Fence ? g.Keys.fenceLength : 5.03f * p.scale) * 0.5f;
                        Assert.That(Math.Abs(along) - half, Is.GreaterThan(FieldBoundaries.GateWidth * 0.5 - 0.6), $"A {p.role} closes the gate at ({gx:0},{gz:0}).");
                    }
                }
            }

            Assert.That(gates, Is.GreaterThan(0), "The farmland tile has gates.");
        }

        [Test]
        public void FarmlandFields_HaveNoFenceOfTheirOwn_AndHaystacksStandInStubble()
        {
            EndlessGenerator g = Gen(12345);
            TileCoord c = FarmlandTile(g, out _);
            float T = g.Params.tileSize;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var t = new TileCoord(c.X + dx, c.Z + dz);
                    TileBlueprint bp = g.GenerateTile(t, TileLod.Near);
                    var fields = new List<MosaicField>();
                    g.Fields.Overlapping(t.OriginX(T), t.OriginZ(T), T, fields);
                    foreach (PropPlacement p in bp.Props)
                    {
                        if (p.settlementId >= 0) continue;   // villages keep their garden and field fences
                        Assert.That(p.role == PropRole.Fence || p.role == PropRole.FenceGate || p.role == PropRole.FencePost, Is.False, "The patchwork's fields are enclosed by the boundary pieces, not by GameObject fences.");
                        if (p.role != PropRole.Haystack) continue;
                        double ax = t.OriginX(T) + p.position.x, az = t.OriginZ(T) + p.position.z;
                        // ... or on a village's hay plot (lived-in closes, CloseContents).
                        Assert.That(fields.Any(f => (InsideDepth(f, ax, az) > 5.0 && TileGenerator.FieldLook(f, 12345) == PropRole.StubbleField) ||
                                                    (f.Close && f.Look == GroundLook.FieldHayMeadow && InsideDepth(f, ax, az) > 3.0)), Is.True, "A haystack stands well inside a stubble field.");
                    }
                }
            }
        }

        [Test]
        public void MidTiles_DrawTheBoundaryRidges_WithinTheirCap()
        {
            EndlessGenerator g = Gen(12345);
            TileCoord c = FarmlandTile(g, out _);
            TileBlueprint mid = g.GenerateTile(c, TileLod.Mid);
            Assert.That(mid.FieldEdges.Count, Is.GreaterThan(20), "The farmland mid tile carries its boundaries.");
            foreach (FieldEdge e in mid.FieldEdges)
            {
                float len = Mathf.Sqrt((e.X1 - e.X0) * (e.X1 - e.X0) + (e.Z1 - e.Z0) * (e.Z1 - e.Z0));
                Assert.That(len, Is.LessThanOrEqualTo(FieldBoundaries.FarChunk + 0.01f));
                Assert.That((e.X0 + e.X1) * 0.5f, Is.InRange(0f, mid.TileSize));
                Assert.That((e.Z0 + e.Z1) * 0.5f, Is.InRange(0f, mid.TileSize));
            }

            FarTileMeshData mesh = FarTileMeshData.Build(mid, FarTreeStyles.Defaults, new FarMeshSettings());
            Assert.That(mesh.EdgesDrawn, Is.EqualTo(Math.Min(mid.FieldEdges.Count, FarTileMeshData.MaxFieldEdges)));
            Assert.That(mesh.EdgeTriangles, Is.EqualTo(mesh.EdgesDrawn * FarTileMeshData.EdgeTris));
            Assert.That(mesh.Triangles, Is.LessThanOrEqualTo(FarTileMeshData.MidMaxTriangles));
            Assert.That(g.GenerateTile(c, TileLod.Far).FieldEdges, Is.Empty, "The 8 m far tiles do not draw them (under a pixel).");
        }

        // ------------------------------------------------------------------ instanced pose

        [Test]
        public void PiecePose_FollowsUnitysEulerConvention()
        {
            Matrix4x4 m = TreeCellSet.PiecePose(new Vector3(1f, 2f, 3f), 90f, 0f, 0f, 1f);
            Vector3 f = m.MultiplyVector(Vector3.forward);
            Assert.That(f.x, Is.EqualTo(1f).Within(1e-5f), "Yaw 90° turns +Z toward +X.");
            Assert.That(m.MultiplyPoint3x4(Vector3.zero), Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Vector3 down = TreeCellSet.PiecePose(Vector3.zero, 0f, 10f, 0f, 1f).MultiplyVector(Vector3.forward);
            Assert.That(down.y, Is.EqualTo(-Mathf.Sin(10f * Mathf.Deg2Rad)).Within(1e-5f), "A positive pitch tilts the run's far end down.");
            Vector3 lean = TreeCellSet.PiecePose(Vector3.zero, 0f, 0f, 10f, 1f).MultiplyVector(Vector3.right);
            Assert.That(lean.y, Is.EqualTo(Mathf.Sin(10f * Mathf.Deg2Rad)).Within(1e-5f), "Roll leans +X up.");
            // Yaw after pitch: the tilted run points along the yaw.
            Vector3 both = TreeCellSet.PiecePose(Vector3.zero, 90f, 10f, 0f, 2f).MultiplyVector(Vector3.forward);
            Assert.That(both.x, Is.EqualTo(2f * Mathf.Cos(10f * Mathf.Deg2Rad)).Within(1e-4f));
            Assert.That(both.y, Is.EqualTo(-2f * Mathf.Sin(10f * Mathf.Deg2Rad)).Within(1e-4f));
        }

        [Test]
        public void TreeCells_CarryThePiecesWithTheirPose()
        {
            var index = new TreePrototypeIndex();
            int wall = index.AddPrototype(1.2f, 2.6f);
            index.SetFamily(PropRole.StoneWall, new[] { wall });
            var pieces = new List<PropPlacement>
            {
                new PropPlacement { role = PropRole.StoneWall, position = new Vector3(10f, 5f, 10f), yaw = 30f, pitch = 4f, roll = 0f, scale = 1f },
                new PropPlacement { role = PropRole.Fence, position = new Vector3(20f, 5f, 20f), yaw = 0f, scale = 1f },   // no prototype: skipped
            };
            TreeCellSet set = TreeCellSet.Build(new List<TreePlacement>(), 256f, index, TreeCellSet.DefaultCellSize, pieces);
            Assert.That(set.Pieces, Is.EqualTo(1));
            Assert.That(set.Skipped, Is.EqualTo(1));
            Matrix4x4 m = set.Cells.Single().ByPrototype[wall].Single();
            Matrix4x4 expected = TreeCellSet.PiecePose(pieces[0].position, 30f, 4f, 0f, 1f);
            for (int i = 0; i < 16; i++) Assert.That(m[i], Is.EqualTo(expected[i]).Within(1e-6f));
        }
    }
}
