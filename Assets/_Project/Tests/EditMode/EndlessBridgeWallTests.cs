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
    /// The first-party walls of the endless road's stone bridges (2026-10-06): a coping of stones and a lining for the full span, end
    /// posts, approach walls splayed outwards into the ground; nothing in front of the bridge's side below its coping. Pure generation only.
    /// </summary>
    public class EndlessBridgeWallTests
    {
        private const double Tx = 960.0, Tz = -540.0;

        private static TileStreams.BridgeDeck Deck(float roadHeight = 100f)
        {
            double yaw = 30.0 * Math.PI / 180.0;
            return new TileStreams.BridgeDeck { X = 1000.3, Z = -500.7, DirX = (float)Math.Sin(yaw), DirZ = (float)Math.Cos(yaw), PivotY = BridgeShape.PivotY(roadHeight) };
        }

        // (along, across) of a tile-local point in the bridge's frame.
        private static (double a, double u) Frame(TileStreams.BridgeDeck d, Vector3 p)
        {
            double x = Tx + p.x - d.X, z = Tz + p.z - d.Z;
            return (x * d.DirX + z * d.DirZ, x * d.DirZ - z * d.DirX);
        }

        private static float Slope(double x, double z) => 100f + 0.16f * (float)(x - 1000.0) - 0.11f * (float)(z + 500.0) + 0.35f * (float)Math.Sin(x * 0.9) * (float)Math.Cos(z * 0.7);

        [Test]
        public void VertexFieldsAreInUnitysAttributeOrder()
        {
            int Offset(string field) => (int)System.Runtime.InteropServices.Marshal.OffsetOf(typeof(BridgeWallVertex), field);
            Assert.That(Offset("position"), Is.EqualTo(0));
            Assert.That(Offset("normal"), Is.EqualTo(12));
            Assert.That(Offset("tangent"), Is.EqualTo(24));
            Assert.That(Offset("uv"), Is.EqualTo(40));
            Assert.That(System.Runtime.InteropServices.Marshal.SizeOf(typeof(BridgeWallVertex)), Is.EqualTo(48));
        }

        [Test]
        public void TheParapetEnclosesTheMeshsOwnAndKeepsTheRoadClear()
        {
            Assert.That(BridgeWalls.InnerFace, Is.GreaterThanOrEqualTo(2.5f + 0.3f - 1e-4f), "the 5 m road and 0.3 m a side");
            Assert.That(BridgeWalls.InnerFace, Is.LessThan(BridgeShape.MeshParapetInner * BridgeShape.ScaleX), "the mesh's parapet shows through the wall's road side");
            Assert.That(BridgeWalls.OuterFace, Is.GreaterThan(BridgeShape.HalfWidth), "the mesh's side shows through the wall's outer face");
            Assert.That(BridgeShape.MeshParapetOuter * BridgeShape.ScaleX, Is.EqualTo(BridgeShape.HalfWidth).Within(0.005f), "the bridge's side is the mesh parapet's outer face");
            Assert.That(BridgeWalls.InnerFace + BridgeWalls.LiningLowerSet, Is.LessThan(BridgeShape.MeshParapetInner * BridgeShape.ScaleX), "the lining stands in front of the mesh parapet's inner face");
            Assert.That(BridgeWalls.CopingOverhangMin, Is.GreaterThan(0f), "the coping overhangs the bridge's side");
            for (float a = 0f; a <= BridgeWalls.CopingEnd; a += 0.05f)
            {
                Assert.That(BridgeShape.ParapetTopLow(a), Is.LessThanOrEqualTo(BridgeShape.ParapetTopGuide(a) + 1e-4f));
                Assert.That(BridgeShape.ParapetTopGuide(a), Is.LessThanOrEqualTo(BridgeShape.ParapetTopHigh(a) + 1e-4f));
                Assert.That(BridgeShape.ParapetTopHigh(a) - BridgeShape.DeckHeight(a), Is.InRange(0.9f, 1.3f), $"the mesh parapet's measured top at {a:0.0} m");
            }
            Assert.That(TileStreams.DeckHalf, Is.LessThan(BridgeWalls.InnerFace));
        }

        [Test]
        public void Stations_FollowTheHump()
        {
            List<float> st = BridgeWalls.Stations();
            Assert.That(st.Count, Is.InRange(8, 40));
            for (int i = 0; i < st.Count; i++)
            {
                Assert.That(st[i], Is.EqualTo(-st[st.Count - 1 - i]).Within(1e-5f), "symmetric");
                if (i > 0) Assert.That(st[i], Is.GreaterThan(st[i - 1] + 0.2f));
            }

            for (int k = 0; k + 1 < st.Count; k++)
            {
                for (int j = 1; j < 20; j++)
                {
                    float a = st[k] + (st[k + 1] - st[k]) * j / 20f;
                    float chord = BridgeShape.DeckHeight(st[k]) + (BridgeShape.DeckHeight(st[k + 1]) - BridgeShape.DeckHeight(st[k])) * j / 20f;
                    Assert.That(chord - BridgeShape.DeckHeight(a), Is.InRange(-BridgeWalls.ChordBelow - 1e-3f, BridgeWalls.ChordAbove + 1e-3f), $"at {a:0.00} m");
                }
            }
        }

        [Test]
        public void Walls_AreCleanFacetedGeometry()
        {
            TileStreams.BridgeDeck d = Deck();
            BridgeWallMeshData m = BridgeWalls.Build(d, (x, z) => 100f, 12345, Tx, Tz);
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Triangles, Is.InRange(600, 1500), "the budget: about 1,500 triangles per bridge");
            Assert.That(m.VertexCount, Is.EqualTo(m.Vertices.Length));
            Assert.That(m.IndexCount, Is.EqualTo(m.Indices.Length));
            foreach (BridgeWallVertex v in m.Vertices)
            {
                Assert.That(Finite(v.position) && Finite(v.normal) && Finite(v.tangent) && Finite(v.uv), Is.True);
                Assert.That(v.normal.magnitude, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(new Vector3(v.tangent.x, v.tangent.y, v.tangent.z).magnitude, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(Vector3.Dot(v.normal, new Vector3(v.tangent.x, v.tangent.y, v.tangent.z)), Is.EqualTo(0f).Within(1e-3f));
                Assert.That(v.uv.x, Is.InRange(0.456f, 0.465f), "inside the atlas's stone cells");
                Assert.That(v.uv.y, Is.InRange(0.148f, 0.218f));
                for (int c = 0; c < 3; c++)
                {
                    Assert.That(v.position[c], Is.InRange(m.BoundsMin[c] - 1e-4f, m.BoundsMax[c] + 1e-4f));
                }
            }

            for (int i = 0; i < m.IndexCount; i += 3)
            {
                BridgeWallVertex a = m.Vertices[m.Indices[i]], b = m.Vertices[m.Indices[i + 1]], c = m.Vertices[m.Indices[i + 2]];
                Vector3 cross = Vector3.Cross(b.position - a.position, c.position - a.position);
                Assert.That(cross.magnitude * 0.5f, Is.GreaterThan(1e-4f), "degenerate triangle");
                Assert.That(Vector3.Dot(cross.normalized, a.normal), Is.GreaterThan(0.99f), "winding and normal agree (flat, front side out)");
            }

            Assert.That(m.Boxes.Count, Is.GreaterThanOrEqualTo(2 * (BridgeWalls.Stations().Count - 1) + 8));
            foreach (BridgeWallBox box in m.Boxes)
            {
                Assert.That(Finite(box.Centre) && Finite(box.Size) && !float.IsNaN(box.Pitch) && !float.IsNaN(box.Yaw), Is.True);
                Assert.That(Math.Min(box.Size.x, Math.Min(box.Size.y, box.Size.z)), Is.GreaterThan(0.2f));
            }
        }

        [Test]
        public void Walls_KeepTheRoadClearAndStandAboveTheDeck()
        {
            TileStreams.BridgeDeck d = Deck();
            BridgeWallMeshData m = BridgeWalls.Build(d, (x, z) => 100f, 12345, Tx, Tz);
            foreach (BridgeWallVertex v in m.Vertices)
            {
                (double a, double u) = Frame(d, v.position);
                Assert.That(Math.Abs(u), Is.GreaterThanOrEqualTo(BridgeWalls.InnerFace - 1e-3), $"a vertex {Math.Abs(u):0.000} m from the axis at {a:0.0} m");
            }

            // Every box collider stays off the road too: its road-side face.
            foreach (BridgeWallBox box in m.Boxes)
            {
                (double a, double u) = Frame(d, box.Centre);
                Assert.That(Math.Abs(u) - 0.5 * box.Size.x, Is.GreaterThanOrEqualTo(BridgeWalls.InnerFace - 0.02), $"a collider at {a:0.0} m reaches over the road");
            }

            // Slices across the bridge: the walls' top above the walking surface (the deck's road surface) all along the hump.
            float lo = float.MaxValue, hi = float.MinValue;
            for (float a = -BridgeShape.HalfLength + 0.7f; a <= BridgeShape.HalfLength - 0.7f; a += 0.1f)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float top = SliceTop(m, d, a, side);
                    float walk = d.PivotY + BridgeShape.DeckHeight(a) + TileStreams.DeckLift;
                    lo = Math.Min(lo, top - walk);
                    hi = Math.Max(hi, top - walk);
                    float own = d.PivotY + BridgeShape.ParapetTopHigh(a);
                    Assert.That(top, Is.GreaterThan(own + 0.02f), $"the mesh's own parapet pokes through at {a:0.0} m");
                }
            }

            Assert.That(lo, Is.GreaterThan(0.95f), "parapet top above the walking surface (the coping lies on the mesh's own parapet: 0.96 m above the deck at the ends)");
            Assert.That(hi, Is.LessThan(1.35f));
            float post = SliceTop(m, d, BridgeShape.HalfLength, 1);
            Assert.That(post - (d.PivotY + BridgeShape.DeckHeight(BridgeShape.HalfLength)), Is.GreaterThan(BridgeWalls.CopingTop + 0.2f), "the end posts rise above the parapet");
        }

        [Test]
        public void ApproachWalls_MeetTheGroundOnASlope()
        {
            TileStreams.BridgeDeck d = Deck(Slope(1000.3, -500.7));
            BridgeWallMeshData m = BridgeWalls.Build(d, Slope, 777, Tx, Tz);
            Assert.That(m.Feet.Count, Is.EqualTo(4 * (4 + 2 * BridgeWalls.WallStations)));
            float worst = float.MaxValue;
            foreach (Vector3 f in m.Feet)
            {
                worst = Math.Min(worst, Slope(Tx + f.x, Tz + f.z) - f.y);
            }

            Assert.That(worst, Is.GreaterThan(0.1f), "a foot shows above the ground");
            for (int g = 0; g < 4; g++)
            {
                int first = g * (4 + 2 * BridgeWalls.WallStations) + 4;
                for (int face = 0; face < 2; face++)
                {
                    for (int i = 0; i + 1 < BridgeWalls.WallStations; i++)
                    {
                        Vector3 p = m.Feet[first + 2 * i + face], q = m.Feet[first + 2 * (i + 1) + face];
                        for (int j = 1; j < 8; j++)
                        {
                            Vector3 r = Vector3.Lerp(p, q, j / 8f);
                            Assert.That(Slope(Tx + r.x, Tz + r.z) - r.y, Is.GreaterThan(0.05f), "the wall's foot leaves a gap above the ground");
                        }
                    }
                }
            }

            // Each wall's far end stands a low wall above the ground there.
            foreach (BridgeWallVertex v in m.Vertices)
            {
                (double a, double u) = Frame(d, v.position);
                if (Math.Abs(a) < BridgeShape.HalfLength + 0.5) continue;
                Assert.That(Math.Abs(u), Is.GreaterThanOrEqualTo(BridgeWalls.InnerFace));
            }
        }

        [Test]
        public void Walls_AreDeterministic()
        {
            TileStreams.BridgeDeck d = Deck();
            BridgeWallMeshData a = BridgeWalls.Build(d, Slope, 12345, Tx, Tz), b = BridgeWalls.Build(d, Slope, 12345, Tx, Tz), c = BridgeWalls.Build(d, Slope, 42, Tx, Tz);
            Assert.That(a.VertexCount, Is.EqualTo(b.VertexCount));
            Assert.That(a.Indices, Is.EqualTo(b.Indices));
            bool tonesDiffer = false, stonesDiffer = false;
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.That(a.Vertices[i].position, Is.EqualTo(b.Vertices[i].position));
                Assert.That(a.Vertices[i].uv, Is.EqualTo(b.Vertices[i].uv));
                if (i < c.VertexCount)
                {
                    tonesDiffer |= a.Vertices[i].uv != c.Vertices[i].uv;
                    stonesDiffer |= a.Vertices[i].position != c.Vertices[i].position;
                }
            }

            Assert.That(tonesDiffer && stonesDiffer, Is.True, "another seed lays other stones");
            Assert.That(a.Boxes.Select(x => x.Centre), Is.EqualTo(c.Boxes.Select(x => x.Centre)), "the colliders do not depend on the stones");
            Assert.That(a.Vertices.Select(v => v.uv.y).Distinct().Count(), Is.EqualTo(3), "three stone tones");
        }

        [Test]
        public void Bridges_GetTheirWallsOnceAndKeepTheRoadClear([Values(12345, 777)] int seed)
        {
            var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            var list = new List<Crossing>();
            g.Streams.Crossings(0.0, 20000.0, list);
            int bridges = 0;
            float ts = g.Params.tileSize;
            foreach (Crossing c in list.Where(c => c.Kind == CrossingKind.Bridge).Take(3))
            {
                StreamCourse course = g.Streams.Course(c);
                double cx = course.X[course.CrossingIndex], cz = course.Z[course.CrossingIndex];
                var coords = new HashSet<TileCoord>();
                for (int i = -1; i <= 1; i++)
                {
                    for (int j = -1; j <= 1; j++) coords.Add(TileCoord.Containing(cx + i * 20.0, cz + j * 20.0, ts));
                }

                int drawn = 0;
                float roadHeight = g.Road(c.S).Height;
                foreach (TileCoord k in coords)
                {
                    TileBlueprint bp = g.GenerateTile(k, TileLod.Near);
                    BridgeWallMeshData m = bp.BridgeWalls;
                    if (m == null) continue;
                    drawn++;
                    double ox = k.OriginX(ts), oz = k.OriginZ(ts);
                    foreach (BridgeWallVertex v in m.Vertices)
                    {
                        if (v.position.y < roadHeight + 0.05f) continue;
                        g.Curve.Nearest(ox + v.position.x, oz + v.position.z, out double toRoad);
                        // The bridge is straight along the road's tangent at the crossing, the road itself a very gentle curve: at the
                        // bridge's ends its centre line may drift up to about 0.2 m from the bridge's axis (Oakbrook Bridge, seed 12345:
                        // 0.17 m, the end posts 2.63 m from the road's centre), while the walls keep 0.3 m beyond the edge on the axis.
                        Assert.That(toRoad, Is.GreaterThan(0.5 * g.Params.roadWidth + 0.05), $"{c.Name}: a wall stands on the road");
                    }
                }

                Assert.That(drawn, Is.EqualTo(1), $"{c.Name}: exactly one tile builds the walls");
                bridges++;
            }

            if (bridges == 0) Assert.Ignore($"Seed {seed} has no bridge in its first 20 km.");
        }

        [Test]
        public void Walls_LeaveTheBridgesSideAndArchInSight()
        {
            TileStreams.BridgeDeck d = Deck();
            BridgeWallMeshData m = BridgeWalls.Build(d, (x, z) => 100f, 12345, Tx, Tz);
            float lowestOutside = float.MaxValue;
            foreach (BridgeWallVertex v in m.Vertices)
            {
                (double a, double u) = Frame(d, v.position);
                if (Math.Abs(a) > BridgeWalls.CopingEnd - BridgeWalls.CapOverhang - 1e-3) continue; // the posts and their caps
                float deck = d.PivotY + BridgeShape.DeckHeight((float)a);
                Assert.That(v.position.y, Is.GreaterThan(deck - 0.05f), $"a wall reaches below the deck at {a:0.0} m");
                Assert.That(Math.Abs(u), Is.LessThanOrEqualTo(BridgeShape.HalfWidth + BridgeWalls.CopingOverhangMax + 1e-3), "only the coping overhangs the bridge's side");
                if (Math.Abs(u) > BridgeShape.HalfWidth - 1e-3) lowestOutside = Math.Min(lowestOutside, v.position.y - deck);
            }

            // In front of the bridge's side only the coping, at the height of the mesh's own coping band (0.77 m and up above the deck).
            Assert.That(lowestOutside, Is.GreaterThan(0.7f), "a wall hangs in front of the bridge's side");
        }

        [Test]
        public void Walls_AreStonesNotSlabs()
        {
            TileStreams.BridgeDeck d = Deck();
            BridgeWallMeshData m = BridgeWalls.Build(d, Slope, 12345, Tx, Tz);
            // No flat panel longer than 1.2 m: the two shorter edges of every triangle (the sides of its quad), measured level.
            for (int i = 0; i < m.IndexCount; i += 3)
            {
                var edges = new float[3];
                for (int e = 0; e < 3; e++)
                {
                    Vector3 p = m.Vertices[m.Indices[i + e]].position, q = m.Vertices[m.Indices[i + (e + 1) % 3]].position;
                    edges[e] = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                }

                Array.Sort(edges);
                Assert.That(edges[1], Is.LessThanOrEqualTo(1.2f), "a flat panel longer than 1.2 m");
            }

            // The coping: one unbroken run of stones a side from post to post, each 0.62-1.08 m long and overhanging 4-8 cm.
            foreach (int side in new[] { -1, 1 })
            {
                List<Vector4> run = m.CopingStones.Where(s => (int)s.z == side).OrderBy(s => s.x).ToList();
                Assert.That(run.Count, Is.InRange(24, 42));
                Assert.That(run[0].x, Is.EqualTo(-BridgeWalls.CopingEnd).Within(1e-4f));
                Assert.That(run[run.Count - 1].y, Is.EqualTo(BridgeWalls.CopingEnd).Within(1e-3f));
                for (int k = 0; k < run.Count; k++)
                {
                    Assert.That(run[k].y - run[k].x, Is.InRange(0.6f, 1.1f), "a coping stone's length");
                    Assert.That(run[k].w, Is.InRange(BridgeWalls.CopingOverhangMin, BridgeWalls.CopingOverhangMax));
                    if (k > 0) Assert.That(run[k].x, Is.EqualTo(run[k - 1].y).Within(1e-4f), "the stones abut");
                }

                Assert.That(run.Select(s => s.w).Distinct().Count(), Is.GreaterThan(run.Count / 2), "each stone its own overhang");
            }

            Assert.That(m.Triangles, Is.LessThanOrEqualTo(1500), "the triangle budget");
        }

        [Test]
        public void Joints_StayInRange([Values(1, 2, 3, 4, 5)] int seed)
        {
            var rng = new System.Random(seed);
            foreach (float span in new[] { 2.5f, 9.3f, 25.96f })
            {
                List<float> j = BridgeWalls.Joints(-0.5f * span, 0.5f * span, BridgeWalls.StoneMean, BridgeWalls.StoneMin, BridgeWalls.StoneMax, rng);
                Assert.That(j[0], Is.EqualTo(-0.5f * span));
                Assert.That(j[j.Count - 1], Is.EqualTo(0.5f * span));
                for (int k = 1; k < j.Count; k++)
                {
                    Assert.That(j[k] - j[k - 1], Is.InRange(BridgeWalls.StoneMin - 1e-3f, BridgeWalls.StoneMax + 1e-3f), $"span {span}");
                }
            }
        }

        // Highest point of the mesh on the cross-section at (along) on one side of the road.
        private static float SliceTop(BridgeWallMeshData m, TileStreams.BridgeDeck d, float along, int side)
        {
            float top = float.NegativeInfinity;
            for (int i = 0; i < m.IndexCount; i += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    Vector3 p = m.Vertices[m.Indices[i + e]].position, q = m.Vertices[m.Indices[i + (e + 1) % 3]].position;
                    (double ap, double up) = Frame(d, p);
                    (double aq, double uq) = Frame(d, q);
                    if (up * side < 2.0 || uq * side < 2.0) continue;
                    if ((ap - along) * (aq - along) > 0 || Math.Abs(aq - ap) < 1e-6) continue;
                    float t = (float)((along - ap) / (aq - ap));
                    top = Math.Max(top, p.y + (q.y - p.y) * t);
                }
            }

            return top;
        }

        private static bool Finite(Vector2 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y);

        private static bool Finite(Vector3 v) => Finite(new Vector2(v.x, v.y)) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);

        private static bool Finite(Vector4 v) => Finite(new Vector3(v.x, v.y, v.z)) && !float.IsNaN(v.w) && !float.IsInfinity(v.w);
    }
}
