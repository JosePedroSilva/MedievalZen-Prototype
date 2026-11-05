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
    /// The streams' look (2026-10-06): a ford's water edge drawn by the mesh across the road, irregular banks, the road surface on a
    /// bridge's deck. Pure generation only.
    /// </summary>
    public class EndlessStreamLookTests
    {
        private static readonly Dictionary<int, EndlessGenerator> Generators = new Dictionary<int, EndlessGenerator>();

        /// <summary>
        /// Unity lays a vertex buffer out in the order of the VertexAttribute enum (position, normal, tangent, colour, uv),
        /// whatever order the descriptors are given in: a vertex struct uploaded with SetVertexBufferData must have its fields in
        /// that order (the stream vertices had uv before colour, and the shaders read garbage: the NaN glow of 2026-10-06).
        /// </summary>
        [Test]
        public void StreamVertexFieldsAreInUnitysAttributeOrder()
        {
            int Offset(string field) => (int)System.Runtime.InteropServices.Marshal.OffsetOf(typeof(StreamVertex), field);
            Assert.That(Offset("position"), Is.EqualTo(0));
            Assert.That(Offset("normal"), Is.EqualTo(12));
            Assert.That(Offset("tangent"), Is.EqualTo(24));
            Assert.That(Offset("color"), Is.EqualTo(40), "colour comes before the texture coordinate");
            Assert.That(Offset("uv"), Is.EqualTo(44));
            Assert.That(System.Runtime.InteropServices.Marshal.SizeOf(typeof(StreamVertex)), Is.EqualTo(52));
        }

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

        private static List<Crossing> Crossings(EndlessGenerator g, CrossingKind kind, double from, double to)
        {
            var list = new List<Crossing>();
            g.Streams.Crossings(from, to, list);
            return list.Where(c => c.Kind == kind).ToList();
        }

        private static float RenderedGround(TileBlueprint bp, float x, float z)
        {
            int n = bp.Heights.GetLength(0);
            float sp = bp.TileSize / (n - 1);
            float gx = x / sp, gz = z / sp;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, n - 2), z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, n - 2);
            float fx = Mathf.Clamp01(gx - x0), fz = Mathf.Clamp01(gz - z0), r = bp.HeightRange;
            float a = bp.Heights[z0, x0] * r, b = bp.Heights[z0, x0 + 1] * r, c = bp.Heights[z0 + 1, x0] * r, d = bp.Heights[z0 + 1, x0 + 1] * r;
            float h1 = fx >= fz ? a + (b - a) * fx + (d - b) * fz : a + (c - a) * fz + (d - c) * fx;
            float h2 = fx + fz <= 1f ? a + (b - a) * fx + (c - a) * fz : d + (c - d) * (1f - fx) + (b - d) * (1f - fz);
            return Math.Max(h1, h2);
        }

        private static IEnumerable<TileBlueprint> TilesAround(EndlessGenerator g, double x, double z, float radius)
        {
            float ts = g.Params.tileSize;
            var coords = new HashSet<TileCoord>();
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++) coords.Add(TileCoord.Containing(x + i * radius, z + j * radius, ts));
            }

            return coords.Select(k => g.GenerateTile(k, TileLod.Near));
        }

        [Test]
        public void Fords_DrawTheirWaterEdgeAcrossTheRoad([Values(12345, 42, 777)] int seed)
        {
            // Across the road a ford's water is narrower than the channel and lies on (just above) the road bed: the ground the tile
            // renders never rises through it, so the water's edge is the mesh's, not the 2 m grid's.
            EndlessGenerator g = Gen(seed);
            int fords = 0, points = 0, above = 0;
            float worst = 0f;
            foreach (Crossing c in Crossings(g, CrossingKind.Ford, 0.0, 20000.0).Take(4))
            {
                StreamCourse course = g.Streams.Course(c);
                float halfWater = course.HalfWater[course.CrossingIndex];
                float drawn = TileStreams.VisibleHalf(course, course.Along[course.CrossingIndex], 0f, halfWater, course.HalfRibbon[course.CrossingIndex]);
                Assert.That(drawn, Is.LessThanOrEqualTo(Math.Max(TileStreams.FordMinHalf, halfWater * TileStreams.FordNarrowing) + 1e-4f), $"{c.Name}: the ford's water is narrowed on the road.");
                Assert.That(TileStreams.Fade(course, course.Along[course.CrossingIndex], 0f), Is.EqualTo(1f), $"{c.Name}: the ford's banks stay drawn on the road.");
                fords++;
                foreach (TileBlueprint bp in TilesAround(g, course.X[course.CrossingIndex], course.Z[course.CrossingIndex], 12f))
                {
                    StreamRibbonMeshData mesh = bp.StreamMesh;
                    if (mesh == null) continue;
                    for (int k = 0; k < mesh.WaterIndexCount; k += 3)
                    {
                        StreamVertex a = mesh.Vertices[mesh.Indices[k]], b = mesh.Vertices[mesh.Indices[k + 1]], d = mesh.Vertices[mesh.Indices[k + 2]];
                        float along = a.uv.y * StreamRibbonMeshData.TextureLength;
                        if (Math.Abs(along - course.Along[course.CrossingIndex]) > course.RoadZone + course.RoadFade) continue;
                        for (int i = 0; i <= 3; i++)
                        {
                            for (int j = 0; i + j <= 3; j++)
                            {
                                Vector3 p = a.position + (b.position - a.position) * (i / 3f) + (d.position - a.position) * (j / 3f);
                                if (p.x < 0f || p.z < 0f || p.x > bp.TileSize || p.z > bp.TileSize) continue;
                                float ground = RenderedGround(bp, p.x, p.z);
                                points++;
                                if (ground > p.y + 0.005f) above++;
                                worst = Math.Max(worst, ground - p.y);
                            }
                        }
                    }
                }
            }

            if (fords == 0) Assert.Ignore($"Seed {seed} has no ford in its first 20 km.");
            Console.WriteLine($"seed {seed}: {fords} fords, {points} water points on the road, {above} under the ground (worst {worst:0.000} m)");
            Assert.That(points, Is.GreaterThan(50));
            Assert.That(above, Is.LessThanOrEqualTo(points / 200), $"ground through a ford's water: {above}/{points}, worst {worst:0.000} m");
        }

        [Test]
        public void Banks_HaveAnIrregularProfile()
        {
            // Hummocks are upward only, zero at the lip and at the top (so the edge and the drape are unchanged), and vary along.
            const float top = 2.2f;
            var values = new List<float>();
            for (int i = 0; i < 400; i++)
            {
                float along = i * 0.5f;
                Assert.That(TileStreams.Hummock(77, 0, along, 0.1f, top), Is.EqualTo(0f));
                Assert.That(TileStreams.Hummock(77, 1, along, top, top), Is.EqualTo(0f));
                float h = TileStreams.Hummock(77, i % 2, along, top * 0.5f + 0.1f, top);
                Assert.That(h, Is.InRange(0f, TileStreams.HummockHeight));
                values.Add(h);
            }

            float mean = values.Average();
            float spread = (float)Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
            Assert.That(spread, Is.GreaterThan(0.015f), "the bank's height varies along the stream");
            Assert.That(TileStreams.Hummock(77, 0, 10f, 1f, top), Is.Not.EqualTo(TileStreams.Hummock(77, 1, 10f, 1f, top)), "the two banks differ");
        }

        [Test]
        public void Bridges_CarryTheRoadOverTheirDeck([Values(12345, 777)] int seed)
        {
            // One tile draws a road-coloured surface over each bridge's deck: just above the stone hump, meeting the road at its ends.
            EndlessGenerator g = Gen(seed);
            int bridges = 0;
            foreach (Crossing c in Crossings(g, CrossingKind.Bridge, 0.0, 20000.0).Take(3))
            {
                StreamCourse course = g.Streams.Course(c);
                int drawn = 0;
                foreach (TileBlueprint bp in TilesAround(g, course.X[course.CrossingIndex], course.Z[course.CrossingIndex], 16f))
                {
                    StreamRibbonMeshData mesh = bp.StreamMesh;
                    if (mesh == null || mesh.DeckVertexStart >= mesh.VertexCount) continue;
                    int deckVertices = mesh.VertexCount - mesh.DeckVertexStart;
                    Assert.That(deckVertices % 5, Is.EqualTo(0));
                    int sections = deckVertices / 5;
                    Assert.That(sections, Is.EqualTo((int)Math.Round(2f * BridgeShape.HalfLength / TileStreams.DeckStep) + 1));
                    Assert.That(mesh.IndexCount - mesh.DeckIndexStart, Is.EqualTo(6 * 4 * (sections - 1)));
                    StreamVertex first = mesh.Vertices[mesh.DeckVertexStart + 2], middle = mesh.Vertices[mesh.DeckVertexStart + (sections / 2) * 5 + 2];
                    StreamVertex last = mesh.Vertices[mesh.VertexCount - 3];
                    Assert.That(middle.position.y - first.position.y, Is.EqualTo(BridgeShape.DeckHeight(0f) - BridgeShape.Sink).Within(0.02f), "the hump");
                    Assert.That(last.position.y, Is.EqualTo(first.position.y).Within(0.01f), "both ends at the road");
                    Assert.That(first.color.b, Is.EqualTo(255), "road-coloured (the bank shader's dirt)");
                    drawn++;
                }

                Assert.That(drawn, Is.EqualTo(1), $"{c.Name}: exactly one tile draws the deck");
                bridges++;
            }

            if (bridges == 0) Assert.Ignore($"Seed {seed} has no bridge in its first 20 km.");
        }
    }
}
