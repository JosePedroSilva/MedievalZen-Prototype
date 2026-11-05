using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// Streams, fords and bridges, climbs with shelves, composed villages and pass walls of the endless road (pure, no Unity
    /// objects): courses are deterministic and never run uphill, channels and ribbons meet across tile edges, the water is where
    /// the channels are, every crossing can be walked along the road's centre line, banks can be climbed out of, crossings keep
    /// their spacing and stay out of settlements, climbs rest on shelves, villages are full and keep the road clear.
    /// </summary>
    public sealed class EndlessStreamTests
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

        private static List<Crossing> Crossings(EndlessGenerator g, double from, double to)
        {
            var list = new List<Crossing>();
            g.Streams.Crossings(from, to, list);
            return list;
        }

        /// <summary>
        /// Height of the measured deck of the Fantasy Kingdom stone bridge above its pivot (top surface on the centre line) at
        /// <paramref name="along"/> metres from its middle, as placed (scaled); NaN beyond its ends.
        /// </summary>
        private static float DeckHeight(float along) => BridgeShape.DeckHeight(along);

        // ------------------------------------------------------------------ courses

        [Test]
        public void Courses_AreDeterministic_AndNeverRunUphill([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            var fresh = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            List<Crossing> list = Crossings(g, -6000.0, 30000.0);
            Assert.That(list.Count, Is.GreaterThan(6), "Streams cross the road.");
            // The fresh generator meets them in reverse order (other courses first).
            var other = new Dictionary<int, StreamCourse>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Crossing c = fresh.Streams.Plan.Cell(list[i].Cell);
                other[c.Id] = fresh.Streams.Course(c);
            }

            foreach (Crossing c in list)
            {
                StreamCourse a = g.Streams.Course(c), b = other[c.Id];
                Assert.That(a.Accepted && b.Accepted, Is.True);
                Assert.That(b.Count, Is.EqualTo(a.Count), $"{c.Name}: point count differs between generators.");
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.That(b.X[i], Is.EqualTo(a.X[i]));
                    Assert.That(b.Bed[i], Is.EqualTo(a.Bed[i]));
                    Assert.That(b.Surface[i], Is.EqualTo(a.Surface[i]));
                    if (i == 0) continue;
                    Assert.That(a.Bed[i], Is.LessThanOrEqualTo(a.Bed[i - 1]), $"{c.Name}: the bed rises downstream at point {i}.");
                    Assert.That(a.Surface[i], Is.LessThanOrEqualTo(a.Surface[i - 1]), $"{c.Name}: the water rises downstream at point {i}.");
                }

                for (int i = 0; i < a.Count; i++)
                {
                    Assert.That(a.Surface[i], Is.GreaterThanOrEqualTo(a.Bed[i] + 0.02f), $"{c.Name}: no water over the bed at point {i}.");
                    Assert.That(a.Width[i], Is.InRange(0.8f, 6.01f), $"{c.Name}: bed width at point {i}.");
                }
            }
        }

        [Test]
        public void Crossings_KeepTheirSpacing_AndStayOutOfSettlements([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            List<Crossing> list = Crossings(g, -20000.0, 30000.0);
            double span = 50.0;
            Assert.That(span * 1000.0 / Math.Max(1, list.Count), Is.InRange(1500.0, 4000.0), $"{list.Count} crossings in {span} km: about one every 1.5-4 km.");
            var wps = new List<Waypoint>();
            g.Waypoints(-20500.0, 30500.0, wps);
            for (int i = 0; i < list.Count; i++)
            {
                Crossing c = list[i];
                Assert.That(Math.Abs(c.S), Is.GreaterThanOrEqualTo(StreamPlan.StartClearance), "No crossing in the first 300 m.");
                if (i > 0) Assert.That(c.S - list[i - 1].S, Is.GreaterThanOrEqualTo(400.0), $"Crossings at {list[i - 1].S} and {c.S}.");
                foreach (Waypoint w in wps.Where(w => w.IsSettlement))
                {
                    float radius = w.Radius > 1f ? w.Radius : 35f;
                    Assert.That(Math.Abs(w.S - c.S), Is.GreaterThan(radius + TerrainShaper.SettlementBlend), $"{c.Name} lies in {w.Kind} '{w.Name}'.");
                }

                Waypoint wp = wps.FirstOrDefault(w => w.Id == c.Id);
                Assert.That(wp, Is.Not.Null, $"{c.Name} has a waypoint.");
                Assert.That(wp.Kind, Is.EqualTo(c.Kind == CrossingKind.Bridge ? WaypointKind.Bridge : WaypointKind.Ford));
                Assert.That(string.IsNullOrEmpty(wp.Name), Is.False);
                Assert.That(wp.IsSettlement, Is.False);
            }

            Assert.That(list.Any(c => c.Kind == CrossingKind.Ford) && list.Any(c => c.Kind == CrossingKind.Bridge), Is.True, "Both fords and bridges occur.");
        }

        // ------------------------------------------------------------------ water and banks

        [Test]
        public void WaterHeight_IsTheStreamInsideItsChannel_AndDryBesideIt([Values(12345, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            int wet = 0, samples = 0;
            foreach (Crossing c in Crossings(g, 0.0, 20000.0))
            {
                StreamCourse course = g.Streams.Course(c);
                for (int i = 0; i < course.Count; i += 7)
                {
                    if (Math.Abs(course.Along[i] - course.Along[course.CrossingIndex]) < 12f || course.EndsInLake && i > course.Count - 4) continue;
                    samples++;
                    float water = g.WaterHeight(course.X[i], course.Z[i]);
                    if (Math.Abs(water - course.Surface[i]) < 0.02f) wet++;
                    double rx = course.DirZ[i], rz = -course.DirX[i];
                    double off = course.HalfRibbon[i] + 12.0;
                    float dry = g.WaterHeight(course.X[i] + rx * off, course.Z[i] + rz * off);
                    if (dry >= 0f) Assert.That(Math.Abs(dry - course.Surface[i]) > 0.01f, Is.True, $"{c.Name}: water 12 m beside the stream at point {i}.");
                }
            }

            Assert.That(samples, Is.GreaterThan(50));
            Assert.That(wet / (double)samples, Is.GreaterThan(0.97), $"WaterHeight returns the stream's surface on its centre line ({wet} of {samples}).");
        }

        [Test]
        public void Banks_CanBeWalkedOutOf([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            float worst = 0f, both = 0f;
            string where = "";
            foreach (Crossing c in Crossings(g, 0.0, 30000.0))
            {
                StreamCourse course = g.Streams.Course(c);
                for (int i = 2; i < course.Count - 2; i += 2)
                {
                    float fromRoad = Math.Abs(course.Along[i] - course.Along[course.CrossingIndex]);
                    if (fromRoad < 12f) continue;   // the road's own banks
                    double rx = course.DirZ[i], rz = -course.DirX[i];
                    float reach = course.HalfWater[i] + 2.5f;
                    // The steepest step on each bank from the middle of the stream outwards; the traveller needs one way out.
                    float[] side = new float[2];
                    for (int k = 0; k < 2; k++)
                    {
                        float sign = k == 0 ? -1f : 1f;
                        float prev = g.Height(course.X[i], course.Z[i]);
                        for (float u = 0.5f; u <= reach; u += 0.5f)
                        {
                            float h = g.Height(course.X[i] + rx * u * sign, course.Z[i] + rz * u * sign);
                            side[k] = Math.Max(side[k], Math.Abs(h - prev) / 0.5f);
                            prev = h;
                        }
                    }

                    float escape = Math.Min(side[0], side[1]);
                    both = Math.Max(both, Math.Max(side[0], side[1]));
                    if (escape > worst)
                    {
                        worst = escape;
                        where = $"{c.Name} point {i}";
                    }
                }
            }

            // The character controller climbs 45°; the carved banks are at most 1 in 1.67 (31°), steeper only where they cut into a
            // hillside: one bank is always walkable.
            Assert.That(worst, Is.LessThan(0.95f), $"Steepest way out of a stream {Mathf.Atan(worst) * Mathf.Rad2Deg:0}° at {where} (steepest bank on either side {Mathf.Atan(both) * Mathf.Rad2Deg:0}°).");
        }

        // ------------------------------------------------------------------ crossings can be walked

        [Test]
        public void EveryCrossing_CanBeWalked_AlongTheCentreLine([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            const float StepOffset = 0.3f, SlopeLimit = 1f;   // PlayerRig: step offset 0.3 m, slope limit 45°
            const double Speed = 1.4, Dt = 1.0 / 60.0;
            float worstStep = 0f, worstSlope = 0f, worstWater = 0f;
            int walked = 0;
            foreach (Crossing c in Crossings(g, 0.0, 30000.0))
            {
                walked++;
                float last = float.NaN;
                double lastS = double.NaN;
                for (double s = c.S - 40.0; s <= c.S + 40.0; s += Speed * Dt)
                {
                    RoadPoint rp = g.Road(s);
                    float ground = g.Height(rp.X, rp.Z);
                    float surface = ground;
                    if (c.Kind == CrossingKind.Bridge)
                    {
                        // On the bridge the deck carries the traveller (its pivot sits TileStreams.BridgeSink below the road).
                        float deck = DeckHeight((float)(s - c.S));
                        if (!float.IsNaN(deck)) surface = Math.Max(ground, rp.Height - TileStreams.BridgeSink + deck);
                        float water = g.WaterHeight(rp.X, rp.Z);
                        if (water >= 0f) Assert.That(surface - water, Is.GreaterThan(0.4f), $"{c.Name}: the deck is not above the water at s {s:0.0}.");
                    }
                    else
                    {
                        float water = g.WaterHeight(rp.X, rp.Z);
                        if (water >= 0f) worstWater = Math.Max(worstWater, water - ground);
                    }

                    if (!float.IsNaN(last))
                    {
                        float rise = Math.Abs(surface - last);
                        worstStep = Math.Max(worstStep, rise);
                        worstSlope = Math.Max(worstSlope, rise / (float)(s - lastS));
                    }

                    last = surface;
                    lastS = s;
                }
            }

            Assert.That(walked, Is.GreaterThan(4));
            Assert.That(worstStep, Is.LessThan(StepOffset * 0.5f), "Largest rise between two frames of the auto-traveller.");
            Assert.That(worstSlope, Is.LessThan(SlopeLimit), "Steepest walking surface at a crossing.");
            Assert.That(worstWater, Is.LessThanOrEqualTo(StreamPlan.FordWater + 0.02f), "Water over a ford's road.");
        }

        [Test]
        public void Bridges_MeetTheRoadFlush_AndFordsDipGently([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            foreach (Crossing c in Crossings(g, -10000.0, 30000.0))
            {
                if (c.Kind == CrossingKind.Bridge)
                {
                    for (double d = -16.0; d <= 16.0; d += 1.0) Assert.That(Math.Abs(g.Road(c.S + d).Grade), Is.LessThan(0.002f), $"{c.Name}: the road is level across the bridge (s {c.S + d:0}).");
                    foreach (double end in new[] { -(double)BridgeShape.HalfLength, BridgeShape.HalfLength })
                    {
                        RoadPoint rp = g.Road(c.S + end);
                        float deckEnd = rp.Height - TileStreams.BridgeSink + DeckHeight(BridgeShape.HalfLength - 0.01f);
                        Assert.That(Math.Abs(g.Height(rp.X, rp.Z) - deckEnd), Is.LessThan(0.05f), $"{c.Name}: the deck's end meets the road (s {c.S + end:0}).");
                    }
                }
                else
                {
                    RoadPoint bottom = g.Road(c.S);
                    Assert.That(g.Profile.BaseHeight(c.S) - bottom.Height, Is.EqualTo(StreamPlan.FordDip).Within(0.01f), $"{c.Name}: the road dips into the ford.");
                    for (double d = -12.0; d <= 12.0; d += 0.5) Assert.That(Math.Abs(g.Road(c.S + d).Grade), Is.LessThan(g.Params.maxRoadGrade), $"{c.Name}: dip grade at s {c.S + d:0.0}.");
                }
            }
        }

        [Test]
        public void Bridges_OpenTheirArchOverDrawnWater([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            // The placed shape: a deck that clears the road between its parapets, a hump gentler than the mesh's own 47 %, ends inside
            // the level stretch of road, and a bed that passes through the arch.
            Assert.That(2f * (BridgeShape.MeshHalfWidth - 0.54f) * BridgeShape.ScaleX, Is.GreaterThanOrEqualTo(g.Params.roadWidth), "The deck between the parapets is narrower than the road.");
            Assert.That(BridgeShape.SteepestGrade, Is.LessThan(0.32f), "The deck's hump.");
            Assert.That(BridgeShape.HalfLength + 2f, Is.LessThan((float)StreamPlan.BridgeFlatHalf), "The bridge is longer than the level road.");
            Assert.That(StreamNetwork.BridgeBedWidth * 0.5f, Is.LessThan(BridgeShape.ArchHalf - 0.8f), "The bed is wider than the arch.");
            foreach (Crossing c in Crossings(g, 0.0, 30000.0))
            {
                if (c.Kind != CrossingKind.Bridge) continue;
                StreamCourse course = g.Streams.Course(c);
                if (course == null || !course.Accepted) continue;
                RoadPoint rp = g.Road(c.S);
                float pivot = BridgeShape.PivotY(rp.Height);
                int ic = course.CrossingIndex;
                float water = course.Surface[ic];
                Assert.That(pivot + BridgeShape.SoffitHeight(0f) - water, Is.GreaterThan(1.4f), $"{c.Name}: the arch opens too little above the water.");
                Assert.That(pivot + BridgeShape.SoffitHeight(BridgeShape.ArchHalf * 0.6f) - water, Is.GreaterThan(0.6f), $"{c.Name}: the arch's sides stand in the water.");
                // The drawn stream (its water and banks) runs up to the bridge's faces: the ground makes the edge only under the deck.
                Assert.That(course.RoadZone + course.RoadFade, Is.LessThanOrEqualTo(BridgeShape.HalfWidth + 3.5f), $"{c.Name}: the road's part of the stream reaches past the bridge.");
                // No sample of the 2 m height grid inside the water near the bridge rises above it.
                for (double x = Math.Floor((rp.X - 12.0) / 2.0) * 2.0; x <= rp.X + 12.0; x += 2.0)
                {
                    for (double z = Math.Floor((rp.Z - 12.0) / 2.0) * 2.0; z <= rp.Z + 12.0; z += 2.0)
                    {
                        StreamSample sm = StreamNetwork.Nearest(course.Pieces, x, z);
                        if (sm.Course != course || Math.Abs(sm.FromCrossing) > 7f || sm.Distance > sm.HalfWater - 0.2f) continue;
                        Assert.That(g.Height(x, z), Is.LessThanOrEqualTo(sm.Surface), $"{c.Name}: ground above the water at ({x:0}, {z:0}).");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ tiles

        [Test]
        public void StreamRibbons_MeetAcrossTileEdges_AndTheMeshMatchesThem()
        {
            EndlessGenerator g = Gen(12345);
            float ts = g.Params.tileSize;
            int checkedEdges = 0;
            foreach (Crossing c in Crossings(g, 0.0, 15000.0).Take(5))
            {
                StreamCourse course = g.Streams.Course(c);
                var coords = new HashSet<TileCoord>();
                for (int i = 0; i < course.Count; i += 3) coords.Add(TileCoord.Containing(course.X[i], course.Z[i], ts));
                var tiles = coords.ToDictionary(k => k, k => g.GenerateTile(k, TileLod.Near));
                foreach (KeyValuePair<TileCoord, TileBlueprint> pair in tiles)
                {
                    TileBlueprint bp = pair.Value;
                    Assert.That(bp.Streams.Count, Is.GreaterThan(0), $"Tile {pair.Key} has no ribbon of {c.Name}.");
                    StreamRibbonMeshData mesh = bp.StreamMesh;
                    Assert.That(mesh, Is.Not.Null);
                    int verts = bp.Streams.Sum(r => r.Vertices.Length);
                    int quads = bp.Streams.Sum(r => r.Vertices.Length - 1);
                    Assert.That(mesh.DeckVertexStart, Is.EqualTo(TileStreams.SectionVertices * verts), "One section of the drawn stream per ribbon vertex.");
                    Assert.That(mesh.WaterIndexCount, Is.EqualTo(6 * (TileStreams.WaterColumns - 1) * quads));
                    Assert.That(mesh.DeckIndexStart - mesh.WaterIndexCount, Is.EqualTo(6 * 2 * (TileStreams.BankColumns - 1) * quads));
                    Assert.That(mesh.ColliderIndices.Length, Is.EqualTo(mesh.BankIndexCount), "The collider is the banks.");
                    int v = 0;
                    foreach (StreamRibbon r in bp.Streams)
                    {
                        foreach (StreamRibbonVertex rv in r.Vertices)
                        {
                            float width = (mesh.Vertices[v].position - mesh.Vertices[v + TileStreams.WaterColumns - 1].position).magnitude;
                            Assert.That(width, Is.EqualTo(2f * rv.HalfWidth).Within(1e-3f));
                            Assert.That(mesh.Vertices[v].position.y, Is.EqualTo(rv.Surface));
                            Assert.That(mesh.Vertices[v].uv.x, Is.EqualTo(-1f), "The water's left edge is at u = -1 (the shader's edge foam and fade).");
                            Assert.That(mesh.Vertices[v + TileStreams.WaterColumns - 1].uv.x, Is.EqualTo(1f), "The water's right edge is at u = 1.");
                            v += TileStreams.SectionVertices;
                        }
                    }

                    // Every vertex on an edge has its twin in the neighbouring tile, and so has its whole section of the mesh.
                    foreach (StreamRibbon r in bp.Streams.Where(r => r.StreamId == c.Id))
                    {
                        foreach (StreamRibbonVertex rv in new[] { r.Vertices[0], r.Vertices[r.Vertices.Length - 1] })
                        {
                            int dx = rv.X <= 1e-3f ? -1 : rv.X >= ts - 1e-3f ? 1 : 0;
                            int dz = rv.Z <= 1e-3f ? -1 : rv.Z >= ts - 1e-3f ? 1 : 0;
                            if (dx == 0 && dz == 0) continue;
                            var n = new TileCoord(pair.Key.X + dx, pair.Key.Z + dz);
                            TileBlueprint nb = tiles.TryGetValue(n, out TileBlueprint t) ? t : g.GenerateTile(n, TileLod.Near);
                            int twin = -1, at = 0;
                            foreach (StreamRibbon o in nb.Streams)
                            {
                                foreach (StreamRibbonVertex ov in o.Vertices)
                                {
                                    if (twin < 0 && o.StreamId == c.Id && Math.Abs(ov.X - (rv.X - dx * ts)) < 1e-3f && Math.Abs(ov.Z - (rv.Z - dz * ts)) < 1e-3f &&
                                        ov.Surface == rv.Surface && ov.HalfWidth == rv.HalfWidth && ov.DirX == rv.DirX && ov.DirZ == rv.DirZ) twin = at;
                                    at++;
                                }
                            }

                            Assert.That(twin, Is.GreaterThanOrEqualTo(0), $"{c.Name}: the ribbon leaving tile {pair.Key} at ({rv.X:0.00}, {rv.Z:0.00}) does not continue in {n}.");
                            int mine = SectionOf(bp, rv);
                            var shift = new Vector3(dx * ts, 0f, dz * ts);
                            for (int k = 0; k < TileStreams.SectionVertices; k++)
                            {
                                StreamVertex a = mesh.Vertices[mine * TileStreams.SectionVertices + k];
                                StreamVertex b = nb.StreamMesh.Vertices[twin * TileStreams.SectionVertices + k];
                                Assert.That((a.position - (b.position + shift)).magnitude, Is.LessThan(2e-3f), $"{c.Name}: mesh vertex {k} of the section on the edge of {pair.Key} differs in {n}.");
                                Assert.That((a.normal - b.normal).magnitude, Is.LessThan(1e-2f), $"{c.Name}: normal of vertex {k} on the edge of {pair.Key} differs in {n}.");
                            }

                            checkedEdges++;
                        }
                    }
                }
            }

            Assert.That(checkedEdges, Is.GreaterThan(3));
        }

        /// <summary>Index (over all of the tile's ribbon vertices, in order) of <paramref name="rv"/>: its section in the stream mesh.</summary>
        private static int SectionOf(TileBlueprint bp, StreamRibbonVertex rv)
        {
            int at = 0;
            foreach (StreamRibbon r in bp.Streams)
            {
                foreach (StreamRibbonVertex v in r.Vertices)
                {
                    if (v.X == rv.X && v.Z == rv.Z && v.Along == rv.Along) return at;
                    at++;
                }
            }

            return -1;
        }

        /// <summary>The ground a near tile renders at tile-local (x, z): the higher of the two ways a cell can be split into triangles.</summary>
        private static float RenderedGround(TileBlueprint bp, float x, float z, bool highest = true)
        {
            int n = bp.Heights.GetLength(0);
            float sp = bp.TileSize / (n - 1);
            float gx = x / sp, gz = z / sp;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, n - 2), z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, n - 2);
            float fx = Mathf.Clamp01(gx - x0), fz = Mathf.Clamp01(gz - z0), r = bp.HeightRange;
            float a = bp.Heights[z0, x0] * r, b = bp.Heights[z0, x0 + 1] * r, c = bp.Heights[z0 + 1, x0] * r, d = bp.Heights[z0 + 1, x0 + 1] * r;
            float h1 = fx >= fz ? a + (b - a) * fx + (d - b) * fz : a + (c - a) * fz + (d - c) * fx;
            float h2 = fx + fz <= 1f ? a + (b - a) * fx + (c - a) * fz : d + (c - d) * (1f - fx) + (b - d) * (1f - fz);
            return highest ? Math.Max(h1, h2) : Math.Min(h1, h2);
        }

        /// <summary>Near tiles a stream crosses (every third point of its course).</summary>
        private static Dictionary<TileCoord, TileBlueprint> StreamTiles(EndlessGenerator g, StreamCourse course)
        {
            var coords = new HashSet<TileCoord>();
            for (int i = 0; i < course.Count; i += 3) coords.Add(TileCoord.Containing(course.X[i], course.Z[i], g.Params.tileSize));
            return coords.ToDictionary(k => k, k => g.GenerateTile(k, TileLod.Near));
        }

        [Test]
        public void DrawnStreams_LieOnTopOfTheGround([Values(12345, 777)] int seed)
        {
            // The visible edges of a stream are the mesh's own: away from the road's part and the faded ends, the ground the tile
            // renders (2 m height map, either triangulation) stays under the water and under the bank, and every skirt is buried.
            EndlessGenerator g = Gen(seed);
            float ts = g.Params.tileSize;
            int waterPoints = 0, waterAbove = 0, bankPoints = 0, bankAbove = 0, skirts = 0;
            float waterWorst = 0f, bankWorst = 0f;
            foreach (Crossing c in Crossings(g, 0.0, 15000.0).Take(4))
            {
                StreamCourse course = g.Streams.Course(c);
                foreach (KeyValuePair<TileCoord, TileBlueprint> pair in StreamTiles(g, course))
                {
                    StreamRibbonMeshData mesh = pair.Value.StreamMesh;
                    if (mesh == null) continue;
                    for (int k = 0; k < mesh.DeckIndexStart; k += 3)
                    {
                        bool isWater = k < mesh.WaterIndexCount;
                        StreamVertex a = mesh.Vertices[mesh.Indices[k]], b = mesh.Vertices[mesh.Indices[k + 1]], d = mesh.Vertices[mesh.Indices[k + 2]];
                        if (IsSkirt(mesh.Indices[k]) || IsSkirt(mesh.Indices[k + 1]) || IsSkirt(mesh.Indices[k + 2])) continue;
                        // The road's part of the stream and the ends, where the bank sinks under the ground, are not checked.
                        float along = a.uv.y * StreamRibbonMeshData.TextureLength;
                        float fromCrossing = along - course.Along[course.CrossingIndex];
                        if (TileStreams.Fade(course, along, fromCrossing) < 0.999f || Math.Abs(fromCrossing) <= course.RoadZone + course.RoadFade) continue;
                        for (int i = 0; i <= 3; i++)
                        {
                            for (int j = 0; i + j <= 3; j++)
                            {
                                float u = i / 3f, w = j / 3f;
                                Vector3 p = a.position + (b.position - a.position) * u + (d.position - a.position) * w;
                                if (p.x < 0f || p.z < 0f || p.x > ts || p.z > ts) continue;
                                float ground = RenderedGround(pair.Value, p.x, p.z);
                                if (isWater)
                                {
                                    waterPoints++;
                                    if (ground > p.y + 0.005f) waterAbove++;
                                    waterWorst = Math.Max(waterWorst, ground - p.y);
                                }
                                else
                                {
                                    bankPoints++;
                                    if (ground > p.y + 0.02f) bankAbove++;
                                    bankWorst = Math.Max(bankWorst, ground - p.y);
                                }
                            }
                        }
                    }

                    for (int v = 0; v < mesh.DeckVertexStart; v++)
                    {
                        if (!IsSkirt(v)) continue;
                        Vector3 p = mesh.Vertices[v].position;
                        if (p.x < 0f || p.z < 0f || p.x > ts || p.z > ts) continue;
                        Assert.That(p.y, Is.LessThan(RenderedGround(pair.Value, p.x, p.z, false)), $"{c.Name}: a bank's skirt shows above the ground in {pair.Key}.");
                        skirts++;
                    }
                }
            }

            Assert.That(waterPoints, Is.GreaterThan(1000));
            Assert.That(skirts, Is.GreaterThan(100));
            // Measured 2026-10-02: none on seed 12345, 0.03 % (a stream beside the road, whose bed the road's fill lifts) on 777.
            Assert.That(waterAbove / (double)waterPoints, Is.LessThan(0.002), $"Ground above the water at {waterAbove} of {waterPoints} points (worst {waterWorst:0.000} m).");
            Assert.That(waterWorst, Is.LessThan(0.15f), "Highest ground above the water.");
            Assert.That(bankAbove / (double)bankPoints, Is.LessThan(0.002), $"Ground more than 2 cm above the bank at {bankAbove} of {bankPoints} points (worst {bankWorst:0.000} m).");
            Assert.That(bankWorst, Is.LessThan(0.2f), "Highest ground above the bank.");
        }

        private static bool IsSkirt(int vertex)
        {
            int local = vertex % TileStreams.SectionVertices;
            return local == TileStreams.WaterColumns || local == TileStreams.SectionVertices - 1;
        }

        [Test]
        public void GrassAndReeds_KeepToTheBanks()
        {
            // No grass or flowers can stand in the water or on the wet lip; reeds stand on the drawn bank at the water's edge (never
            // buried under it, never floating above the water).
            EndlessGenerator g = Gen(12345);
            int cells = 0, reeds = 0;
            foreach (Crossing c in Crossings(g, 0.0, 15000.0).Take(4))
            {
                StreamCourse course = g.Streams.Course(c);
                foreach (KeyValuePair<TileCoord, TileBlueprint> pair in StreamTiles(g, course))
                {
                    TileBlueprint bp = pair.Value;
                    float texel = bp.TileSize / bp.DetailResolution;
                    foreach (DetailLayer layer in bp.Details)
                    {
                        for (int i = 0; i < layer.density.Length; i++)
                        {
                            if (layer.density[i] == 0) continue;
                            float x = (i % bp.DetailResolution + 0.5f) * texel, z = (i / bp.DetailResolution + 0.5f) * texel;
                            NearestRibbon(bp, x, z, out float distance, out float half, out _);
                            Assert.That(distance, Is.GreaterThan(half + StreamNetwork.LipWidth + texel * 0.7f - 0.05f), $"{layer.family} in cell ({x:0}, {z:0}) of {pair.Key} reaches into {c.Name}.");
                            cells++;
                        }
                    }

                    foreach (PropPlacement prop in bp.Props.Where(pp => pp.role == PropRole.Reeds))
                    {
                        NearestRibbon(bp, prop.position.x, prop.position.z, out float distance, out float half, out float surface);
                        if (distance > half + 2f) continue;
                        Assert.That(prop.position.y, Is.GreaterThanOrEqualTo(RenderedGround(bp, prop.position.x, prop.position.z, false) - 0.01f), "Reeds under the ground.");
                        Assert.That(prop.position.y, Is.InRange(surface - 0.06f, surface + 0.8f), "Reeds at the water's edge.");
                        reeds++;
                    }
                }
            }

            Assert.That(cells, Is.GreaterThan(1000), "Grass grows around the streams.");
            Assert.That(reeds, Is.GreaterThan(5), "Reeds grow by the streams.");
        }

        /// <summary>Distance from tile-local (x, z) to the nearest stream ribbon of the tile, its drawn half width and surface there.</summary>
        private static void NearestRibbon(TileBlueprint bp, float x, float z, out float distance, out float half, out float surface)
        {
            distance = float.MaxValue;
            half = 0f;
            surface = 0f;
            foreach (StreamRibbon r in bp.Streams)
            {
                for (int i = 0; i + 1 < r.Vertices.Length; i++)
                {
                    StreamRibbonVertex a = r.Vertices[i], b = r.Vertices[i + 1];
                    float ex = b.X - a.X, ez = b.Z - a.Z, len2 = ex * ex + ez * ez;
                    float t = len2 > 1e-8f ? Mathf.Clamp01(((x - a.X) * ex + (z - a.Z) * ez) / len2) : 0f;
                    float dx = x - (a.X + ex * t), dz = z - (a.Z + ez * t);
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d >= distance) continue;
                    distance = d;
                    half = a.HalfWidth + (b.HalfWidth - a.HalfWidth) * t;
                    surface = a.Surface + (b.Surface - a.Surface) * t;
                }
            }
        }

        [Test]
        public void Fords_HaveSteppingStones_BesideTheRoad([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            int fords = 0;
            foreach (Crossing c in Crossings(g, 0.0, 30000.0).Where(c => c.Kind == CrossingKind.Ford).Take(3))
            {
                StreamCourse course = g.Streams.Course(c);
                var stones = new List<Vector3>();
                foreach (KeyValuePair<TileCoord, TileBlueprint> pair in StreamTiles(g, course))
                {
                    double ox = pair.Key.OriginX(g.Params.tileSize), oz = pair.Key.OriginZ(g.Params.tileSize);
                    foreach (PropPlacement prop in pair.Value.Props.Where(pp => pp.settlementId == c.Id && PropRole.IsRock(pp.role)))
                        stones.Add(new Vector3((float)(ox + prop.position.x), prop.position.y, (float)(oz + prop.position.z)));
                }

                Assert.That(stones.Count, Is.GreaterThanOrEqualTo(3), $"{c.Name} has stepping stones.");
                foreach (Vector3 stone in stones)
                {
                    g.Curve.Nearest(stone.x, stone.z, out double toRoad);
                    Assert.That(toRoad, Is.InRange(g.Params.roadWidth * 0.5, g.Params.roadWidth * 0.5 + 5.0), $"{c.Name}: a stepping stone {toRoad:0.0} m from the road's centre line.");
                    StreamSample s = StreamNetwork.Nearest(course.Pieces, stone.x, stone.z);
                    Assert.That(s.Distance, Is.LessThan(s.HalfWater + 0.6f), $"{c.Name}: a stepping stone off the line across the water.");
                }

                fords++;
            }

            Assert.That(fords, Is.GreaterThan(0));
        }

        [Test]
        public void StreamTiles_AreIdentical_InAnyOrder()
        {
            EndlessGenerator g = Gen(42);
            Crossing c = Crossings(g, 2000.0, 20000.0)[0];
            StreamCourse course = g.Streams.Course(c);
            TileCoord at = TileCoord.Containing(course.X[course.CrossingIndex], course.Z[course.CrossingIndex], g.Params.tileSize);
            TileBlueprint a = g.GenerateTile(at, TileLod.Near);
            TileBlueprint b = new EndlessGenerator(new EndlessParams { seed = 42 }, PaletteKeys.Default).GenerateTile(at, TileLod.Near);
            Assert.That(b.Streams.Count, Is.EqualTo(a.Streams.Count));
            for (int r = 0; r < a.Streams.Count; r++)
            {
                Assert.That(b.Streams[r].Vertices.Length, Is.EqualTo(a.Streams[r].Vertices.Length));
                for (int i = 0; i < a.Streams[r].Vertices.Length; i++) Assert.That(b.Streams[r].Vertices[i].Surface, Is.EqualTo(a.Streams[r].Vertices[i].Surface));
            }

            Assert.That(b.Props.Count, Is.EqualTo(a.Props.Count));
            for (int i = 0; i < a.Props.Count; i++) Assert.That(b.Props[i].position, Is.EqualTo(a.Props[i].position));
            Assert.That(a.Props.Count(pp => pp.settlementId == c.Id), Is.GreaterThan(0), $"{c.Name} has its bridge or stepping stones.");
        }

        // ------------------------------------------------------------------ climbs

        [Test]
        public void Climbs_RestOnShelves_AndKeepTheirGrade([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            int passes = 0;
            for (Region r = g.Regions.At(0.0); r.Start < 40000.0; r = g.Regions.Get(r.Index + 1))
            {
                if (r.Kind != RegionKind.HighlandPass) continue;
                passes++;
                var shelves = new List<FlatZone>();
                g.Profile.Shelves(r.Start - ElevationProfile.ClimbLead, r.End + 300.0, shelves);
                Assert.That(shelves.Count, Is.GreaterThanOrEqualTo(3), $"Pass {r.Name} has shelves.");
                foreach (FlatZone z in shelves)
                {
                    Assert.That(z.To - z.From, Is.InRange(39.0, 121.0));
                    Assert.That(Math.Abs(g.Road((z.From + z.To) * 0.5).Grade), Is.LessThan(0.012f), $"Shelf at {z.From:0} in {r.Name} is level.");
                }

                // Longest stretch steeper than 3 % on either side of the col.
                double run = 0.0, longest = 0.0;
                for (double s = r.Start - ElevationProfile.ClimbLead; s <= r.End + 300.0; s += 2.0)
                {
                    float grade = Math.Abs(g.Road(s).Grade);
                    Assert.That(grade, Is.LessThanOrEqualTo(g.Params.maxRoadGrade));
                    run = grade >= 0.03f ? run + 2.0 : 0.0;
                    longest = Math.Max(longest, run);
                }

                Assert.That(longest, Is.LessThan(1400.0), $"{r.Name}: the longest unbroken pitch.");
                Assert.That(g.Road(r.Col).Height, Is.InRange(r.ColAltitude - 12f, r.ColAltitude + 1f), $"{r.Name}: the col keeps its altitude.");
            }

            Assert.That(passes, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------------ villages

        [Test]
        public void Villages_AreFull_KeepTheRoadClear_AndDoNotOverlap([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Gen(seed);
            var wps = new List<Waypoint>();
            g.Waypoints(0.0, 30000.0, wps);
            int checkedPlaces = 0;
            foreach (Waypoint w in wps.Where(w => w.Kind == WaypointKind.Village || w.Kind == WaypointKind.Hamlet))
            {
                TileCoord c = TileCoord.Containing(w.X, w.Z, g.Params.tileSize);
                var props = new List<(PropPlacement prop, double x, double z)>();
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var k = new TileCoord(c.X + dx, c.Z + dz);
                        TileBlueprint bp = g.GenerateTile(k, TileLod.Near);
                        foreach (PropPlacement p in bp.Props.Where(p => p.settlementId == w.Id)) props.Add((p, k.OriginX(g.Params.tileSize) + p.position.x, k.OriginZ(g.Params.tileSize) + p.position.z));
                    }
                }

                // Since 2026-10-05 the houses are Fantasy Kingdom presets (BuildingTable): count the dwellings, compare their walls' middles.
                var cabins = props.Where(p => PropRole.IsDwelling(p.prop.role)).Select(p =>
                {
                    BuildingShape shape = BuildingTable.For(p.prop.role, p.prop.variant);
                    Vector3 c = shape != null ? BuildingTable.ToWorld(Vector3.zero, p.prop.yaw, shape.BodyCentre) : Vector3.zero;
                    return (p.prop, x: p.x + c.x, z: p.z + c.z);
                }).ToList();
                Assert.That(cabins.Count, Is.GreaterThanOrEqualTo(w.Kind == WaypointKind.Village ? 9 : 6), $"{w.Kind} '{w.Name}' has its houses.");
                Assert.That(props.Any(p => p.prop.role == PropRole.Well), Is.True, $"{w.Kind} '{w.Name}' has a well.");
                Assert.That(props.Count(p => p.prop.role == PropRole.StoneArch), Is.EqualTo(0), $"{w.Kind} '{w.Name}': no arch standing in the road.");
                for (int i = 0; i < cabins.Count; i++)
                {
                    for (int j = i + 1; j < cabins.Count; j++)
                    {
                        double d = Math.Sqrt((cabins[i].x - cabins[j].x) * (cabins[i].x - cabins[j].x) + (cabins[i].z - cabins[j].z) * (cabins[i].z - cabins[j].z));
                        Assert.That(d, Is.GreaterThan(5.0), $"Two houses of '{w.Name}' overlap.");
                    }
                }

                foreach ((PropPlacement prop, double x, double z) in props)
                {
                    if (prop.role == PropRole.StoneArch) continue;
                    RoadPoint rp = g.Road(g.NearestS(x, z, w.S));
                    double d = Math.Sqrt((rp.X - x) * (rp.X - x) + (rp.Z - z) * (rp.Z - z));
                    Assert.That(d, Is.GreaterThan(g.Params.roadWidth * 0.5 + 1.0), $"{prop.role} of '{w.Name}' stands {d:0.0} m from the road's centre.");
                }

                checkedPlaces++;
            }

            Assert.That(checkedPlaces, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------------ pass walls

        [Test]
        public void PassWalls_HaveSeatedBoulders_AndCleanRocks()
        {
            EndlessGenerator g = Gen(12345);
            Region pass = g.Regions.At(0.0);
            while (pass.Kind != RegionKind.HighlandPass) pass = g.Regions.Get(pass.Index + 1);
            EndlessParams p = g.Params;
            int rocks = 0;
            foreach (double s in new[] { pass.Start + 300.0, pass.Col - 400.0, pass.Col + 400.0 })
            {
                RoadPoint rp = g.Road(s);
                TileCoord c = TileCoord.Containing(rp.X, rp.Z, p.tileSize);
                TileBlueprint t = g.GenerateTile(c, TileLod.Near);
                double ox = c.OriginX(p.tileSize), oz = c.OriginZ(p.tileSize);
                GenWindow w = GenWindow.Create(p, g.Terrain, g.Curve, ox - 32.0, oz - 32.0, 160, 2f, -1f);
                var ctx = new RockPlacer.Context(w.Params, w.Map, g.Keys, null);
                foreach (PropPlacement prop in t.Props)
                {
                    if (!PropRole.IsRock(prop.role) || !g.Keys.TryGetMetrics(prop.role, prop.variant, out PropMetrics m)) continue;
                    // Rocks on a stream's drawn bank are seated on the bank, which lies above the ground there.
                    double ax = ox + prop.position.x, az = oz + prop.position.z;
                    StreamSample bank = StreamNetwork.Nearest(g.Terrain.RowAt(az).Streams, ax, az);
                    if (bank.Course != null && bank.Distance <= bank.Outer) continue;
                    PropPlacement local = prop;
                    local.position = new Vector3((float)(ox + prop.position.x - w.X0), prop.position.y, (float)(oz + prop.position.z - w.Z0));
                    RockAudit audit = RockPlacer.Inspect(ctx, m, local);
                    Assert.That(audit.gap, Is.LessThanOrEqualTo(0.02f), $"{prop.role} floats by {audit.gap:0.00} m.");
                    if (PropRole.IsCliff(prop.role)) Assert.That(audit.convex, Is.False, $"{prop.role} on convex ground.");
                    rocks++;
                }
            }

            Assert.That(rocks, Is.GreaterThan(10), "The pass walls have boulders and cliff blocks.");
        }

        // ------------------------------------------------------------------ cost

        [Test, Category("Slow")]
        public void NearTiles_StayWithinTheirTimeBudget()
        {
            var g = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            g.GenerateTile(TileCoord.Containing(g.Road(-3000.0).X, g.Road(-3000.0).Z, 256f), TileLod.Near);
            var times = new List<double>();
            for (double s = 500.0; s < 12000.0; s += 600.0)
            {
                RoadPoint rp = g.Road(s);
                var clock = Stopwatch.StartNew();
                g.GenerateTile(TileCoord.Containing(rp.X, rp.Z, 256f), TileLod.Near);
                times.Add(clock.Elapsed.TotalMilliseconds);
            }

            bool mono = Type.GetType("Mono.Runtime") != null;
            Assert.That(times.Average(), Is.LessThan(mono ? 400.0 : 120.0), $"Average near tile {times.Average():0} ms.");
        }
    }
}
