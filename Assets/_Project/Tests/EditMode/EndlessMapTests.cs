using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>The endless road's scroll map: layout, painting, reveal, cache, picking, persistence (all pure).</summary>
    public sealed class EndlessMapTests
    {
        private static readonly Dictionary<int, EndlessGenerator> Generators = new Dictionary<int, EndlessGenerator>();

        private static EndlessGenerator Real(int seed)
        {
            lock (Generators)
            {
                if (!Generators.TryGetValue(seed, out EndlessGenerator g)) Generators[seed] = g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
                return g;
            }
        }

        private static StubEndlessGenerator Stub(int seed = 7) => new StubEndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);

        // ------------------------------------------------------------------ projection

        [Test, Category("Slow")]
        public void StripIsMonotonicFoldFreeAndInvertibleOnRealRoads([Values(12345, 42, 777)] int seed)
        {
            EndlessGenerator g = Real(seed);
            var p = new StripProjection(g, 0.0);
            double previous = double.NegativeInfinity;
            double worstStretch = 0.0, widestRoad = 0.0;
            for (double s = -1500.0; s <= 30000.0; s += 4.0)
            {
                p.RoadToStrip(s, out double u, out double v);
                Assert.That(v, Is.GreaterThan(previous + 4.0 * 0.6), $"v must grow along the road (s = {s})");
                previous = v;
                widestRoad = Math.Max(widestRoad, Math.Abs(u));
                worstStretch = Math.Max(worstStretch, Math.Abs(p.CurvatureAt(v)) * StripMapLayout.HalfWidth);
            }

            // |u|·κ < 1 everywhere is fold-free; demand a wide margin (lengths at the edge change by at most this fraction).
            Assert.That(worstStretch, Is.LessThan(0.3), "distortion at the strip's edge");
            Assert.That(widestRoad, Is.LessThan(StripMapLayout.HalfWidth * 0.45), "the road stays well inside the strip");

            var rng = new System.Random(seed);
            double worstRoundTrip = 0.0;
            for (int i = 0; i < 4000; i++)
            {
                double v = rng.NextDouble() * 30000.0 - 1000.0;
                double u = (rng.NextDouble() * 2.0 - 1.0) * StripMapLayout.HalfWidth;
                p.ToWorld(u, v, out double x, out double z);
                p.ToStrip(x, z, double.NaN, out double u2, out double v2);
                worstRoundTrip = Math.Max(worstRoundTrip, Math.Max(Math.Abs(u2 - u), Math.Abs(v2 - v)));
            }

            Assert.That(worstRoundTrip, Is.LessThan(1e-6), "round trip (metres)");
        }

        [Test]
        public void StripLayoutDoesNotDependOnTheAnchorOrTheOrderOfQueries()
        {
            StubEndlessGenerator g = Stub();
            var a = new StripProjection(g, 0.0);
            var b = new StripProjection(g, 3072.0);
            // b is asked far ahead first, then back (its table grows the other way round).
            double b1 = b.VOfS(4800.0), b0 = b.VOfS(800.0);
            double a0 = a.VOfS(800.0), a1 = a.VOfS(4800.0);
            Assert.That(b1 - b0, Is.EqualTo(a1 - a0).Within(1e-6));
            Assert.That(a.SAtV(a.VOfS(1234.5)), Is.EqualTo(1234.5).Within(1e-4));
            Assert.That(a.SAtV(a.VOfS(-2222.0)), Is.EqualTo(-2222.0).Within(1e-4));
        }

        [Test]
        public void ProjectionIsThreadSafe()
        {
            StubEndlessGenerator g = Stub(3);
            var shared = new StripProjection(g, 0.0);
            var results = new double[64];
            Parallel.For(0, 64, i => results[i] = shared.VOfS(i * 250.0 - 3000.0));
            var fresh = new StripProjection(g, 0.0);
            for (int i = 0; i < 64; i++) Assert.That(results[i], Is.EqualTo(fresh.VOfS(i * 250.0 - 3000.0)), $"sample {i}");
        }

        // ------------------------------------------------------------------ painting

        [Test, Category("Slow")]
        public void SegmentsAreDeterministicAndThreadSafe()
        {
            var renderer = new StripMapRenderer(new StripProjection(Real(42), 0.0));
            int[] segments = { -2, 0, 3, 7 };
            var sequential = new ulong[segments.Length];
            for (int i = 0; i < segments.Length; i++) sequential[i] = Hash(renderer.RenderInked(segments[i]));
            Assert.That(Hash(renderer.RenderInked(3)), Is.EqualTo(sequential[2]), "same segment twice");

            var concurrent = new ulong[segments.Length * 2];
            var other = new StripMapRenderer(new StripProjection(Real(42), 0.0));
            Parallel.For(0, concurrent.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, j =>
                concurrent[j] = Hash((j % 2 == 0 ? renderer : other).RenderInked(segments[j / 2])));
            for (int j = 0; j < concurrent.Length; j++) Assert.That(concurrent[j], Is.EqualTo(sequential[j / 2]), $"segment {segments[j / 2]} on a worker");
        }

        [Test, Category("Slow")]
        public void NeighbouringSegmentsMeetWithoutASeam([Values(12345, 777)] int seed)
        {
            var renderer = new StripMapRenderer(new StripProjection(Real(seed), 0.0));
            const int rows = StripMapLayout.SegmentRows;
            const int w = StripMapLayout.Width;
            foreach (int k in new[] { 1, 16 })
            {
                foreach (bool inked in new[] { true, false })
                {
                    Color32[] lower = inked ? renderer.RenderInked(k) : renderer.RenderSketch(k);
                    Color32[] upper = inked ? renderer.RenderInked(k + 1) : renderer.RenderSketch(k + 1);
                    Color32[] both = renderer.RenderRows(k * rows, 2 * rows, inked);
                    int worst = 0;
                    for (int y = 0; y < 2 * rows; y++)
                    {
                        Color32[] part = y < rows ? lower : upper;
                        int py = y < rows ? y : y - rows;
                        for (int x = 0; x < w; x++)
                        {
                            Color32 a = part[py * w + x], b = both[y * w + x];
                            worst = Math.Max(worst, Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b))));
                        }
                    }

                    Assert.That(worst, Is.LessThanOrEqualTo(2), $"segments {k}/{k + 1} ({(inked ? "inked" : "sketch")}) differ from one painting of both");
                }
            }
        }

        [Test]
        public void StubGeneratorFallsBackToAPlainForestTone()
        {
            StubEndlessGenerator g = Stub();
            IStripGround ground = StripGround.For(g);
            Assert.That(ground, Is.InstanceOf<StripGround.PlainGround>());
            Assert.That(ground.TreeMarks, Is.False);
            Assert.That(StripGround.For(Real(12345)), Is.InstanceOf<StripGround.CoverGround>());
            var renderer = new StripMapRenderer(new StripProjection(g, 0.0));
            Color32[] px = renderer.RenderInked(2);
            Assert.That(px.Length, Is.EqualTo(StripMapLayout.Width * StripMapLayout.SegmentRows));
            Assert.That(Hash(renderer.RenderInked(2)), Is.EqualTo(Hash(px)));
        }

        [Test]
        public void UnknownWaypointKindsAreDrawnAndPickedGenerically()
        {
            var g = new ExtraPlaces(Stub(), new Waypoint { Id = 9001, Kind = (WaypointKind)42, Name = "The Odd Thing", S = 300.0 });
            g.Place(g.Extra);
            Assert.That(StripMapContent.ClassOf((WaypointKind)42), Is.EqualTo(StripFeatureClass.Other));
            var p = new StripProjection(g, 0.0);
            var renderer = new StripMapRenderer(p);
            Assert.DoesNotThrow(() => renderer.RenderInked(StripMapLayout.SegmentAt(p.VOfS(300.0))));
            StripMapContent content = StripMapContent.Build(p, 0.0, 1000.0);
            StripFeature odd = content.Features.Find(f => f.Waypoint.Id == 9001);
            Assert.That(odd, Is.Not.Null);
            Assert.That(odd.Class, Is.EqualTo(StripFeatureClass.Other));
            Assert.That(odd.Label, Is.EqualTo("The Odd Thing"));
            int picked = StripMapPicker.Pick(content.Features, odd.U + 3.0, odd.V - 2.0, 20.0);
            Assert.That(content.Features[picked].Waypoint.Id, Is.EqualTo(9001));
        }

        // ------------------------------------------------------------------ picking, reveal, cache, persistence, text

        [Test]
        public void PickingFindsTheNearestPlaceWithinToleranceAndPrefersSettlements()
        {
            var features = new List<StripFeature>
            {
                Feature(1, WaypointKind.Viewpoint, "Hob's Edge", 10.0, 100.0),
                Feature(2, WaypointKind.Inn, "The Drowsy Fox", 22.0, 100.0),
                Feature(3, WaypointKind.Milestone, "1 km", 0.0, 300.0),
                Feature(4, WaypointKind.Pass, "Kel Col", 0.0, 600.0),
            };
            // Viewpoint 6 m away, inn 6 m away: the inn wins the close call.
            Assert.That(StripMapPicker.Pick(features, 16.0, 100.0, 20.0), Is.EqualTo(1));
            Assert.That(StripMapPicker.Pick(features, 8.0, 101.0, 20.0), Is.EqualTo(0));
            Assert.That(StripMapPicker.Pick(features, 0.0, 310.0, 20.0), Is.EqualTo(2));
            Assert.That(StripMapPicker.Pick(features, 0.0, 450.0, 20.0), Is.EqualTo(-1), "nothing within tolerance");
            Assert.That(StripMapPicker.Pick(features, 0.0, 600.0, 20.0, f => f.Class == StripFeatureClass.Settlement), Is.EqualTo(-1), "filtered out");
        }

        [Test]
        public void RevealInksWalkedCountryAndFadesBeyondWhatWasSeen()
        {
            const double min = 0.0, max = 4000.0;
            Assert.That(StripReveal.Weight(2000.0, min, max), Is.EqualTo(1f));
            Assert.That(StripReveal.Weight(max + StripReveal.SeenAhead - 300.0, min, max), Is.EqualTo(1f));
            Assert.That(StripReveal.Weight(max + StripReveal.SeenAhead, min, max), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(StripReveal.Weight(max + StripReveal.SeenAhead + 300.0, min, max), Is.EqualTo(0f));
            Assert.That(StripReveal.Weight(min - StripReveal.SeenAhead - 300.0, min, max), Is.EqualTo(0f), "behind the start too");
            float previous = 1f;
            for (double s = max; s < max + 2500.0; s += 10.0)
            {
                float w = StripReveal.Weight(s, min, max);
                Assert.That(w, Is.LessThanOrEqualTo(previous + 1e-6f), "fades monotonically");
                previous = w;
            }

            Assert.That(StripReveal.Classify(1000.0, 1384.0, min, max), Is.EqualTo(StripReveal.State.Inked));
            Assert.That(StripReveal.Classify(5400.0, 5784.0, min, max), Is.EqualTo(StripReveal.State.Blend));
            Assert.That(StripReveal.Classify(6000.0, 6384.0, min, max), Is.EqualTo(StripReveal.State.Sketch));
            Assert.That(StripReveal.Key(min, max), Is.Not.EqualTo(StripReveal.Key(min, max + 10.0)));

            // Compose: rows well inside take the ink, rows far beyond the sketch.
            const int w2 = 4;
            var inked = new Color32[w2 * 3];
            var sketch = new Color32[w2 * 3];
            for (int i = 0; i < inked.Length; i++)
            {
                inked[i] = new Color32(10, 20, 30, 255);
                sketch[i] = new Color32(200, 190, 150, 255);
            }

            var output = new Color32[inked.Length];
            StripReveal.Compose(inked, sketch, new[] { 100.0, max + StripReveal.SeenAhead, 9000.0 }, w2, 0, min, max, output);
            Assert.That(output[0], Is.EqualTo(inked[0]));
            Assert.That(output[2 * w2], Is.EqualTo(sketch[0]));
            Assert.That(output[w2].r, Is.InRange(11, 199), "the fade row is a blend");
        }

        [Test]
        public void ReachPersistsPerRoadAndOnlyGrows()
        {
            EndlessMapReach r = EndlessMapReach.Start(12345, 500.0);
            Assert.That(r.Extend(450.0), Is.True);
            Assert.That(r.Extend(480.0), Is.False);
            Assert.That(r.Extend(2500.25), Is.True);
            Assert.That(EndlessMapReach.TryFromJson(r.ToJson(), out EndlessMapReach back), Is.True);
            Assert.That(back.Seed, Is.EqualTo(12345));
            Assert.That(back.MinS, Is.EqualTo(450.0));
            Assert.That(back.MaxS, Is.EqualTo(2500.25));

            EndlessMapReach same = EndlessMapReach.Resume(back, 12345, 3000.0);
            Assert.That(same.MinS, Is.EqualTo(450.0));
            Assert.That(same.MaxS, Is.EqualTo(3000.0));
            EndlessMapReach other = EndlessMapReach.Resume(back, 99, 10.0);
            Assert.That(other.MinS, Is.EqualTo(10.0), "a new road starts afresh");
            Assert.That(other.MaxS, Is.EqualTo(10.0));

            Assert.That(EndlessMapReach.TryFromJson("{\"version\":2,\"seed\":1,\"min\":0,\"max\":1}", out _), Is.False, "newer version");
            Assert.That(EndlessMapReach.TryFromJson("{\"seed\":1,\"min\":5,\"max\":1}", out _), Is.False, "inverted");
            Assert.That(EndlessMapReach.TryFromJson("not json", out _), Is.False);
        }

        [Test]
        public void CacheIsBoundedAndDropsTheLeastRecentlyUsed()
        {
            var cache = new StripSegmentCache(3);
            for (int k = 0; k < 3; k++) cache.Put(StripSegmentCache.Key(k, StripSegmentCache.Inked), new Color32[1]);
            Assert.That(cache.TryGet(StripSegmentCache.Key(0, StripSegmentCache.Inked), out _), Is.True);   // 0 is now the newest
            cache.Put(StripSegmentCache.Key(3, StripSegmentCache.Inked), new Color32[1]);
            Assert.That(cache.Count, Is.EqualTo(3));
            Assert.That(cache.Contains(StripSegmentCache.Key(1, StripSegmentCache.Inked)), Is.False, "the oldest went");
            Assert.That(cache.Contains(StripSegmentCache.Key(0, StripSegmentCache.Inked)), Is.True);
            Assert.That(StripSegmentCache.Key(5, StripSegmentCache.Inked), Is.Not.EqualTo(StripSegmentCache.Key(5, StripSegmentCache.Sketch)));
            Assert.That(StripSegmentCache.Key(-1, StripSegmentCache.Inked), Is.Not.EqualTo(StripSegmentCache.Key(0, StripSegmentCache.Inked)));

            Parallel.For(0, 200, i => cache.Put(StripSegmentCache.Key(i, StripSegmentCache.Sketch), new Color32[1]));
            Assert.That(cache.Count, Is.EqualTo(3));
            cache.Clear();
            Assert.That(cache.Count, Is.EqualTo(0));
        }

        [Test]
        public void LabelsAndDistancesReadNaturally()
        {
            Assert.That(StripMapText.Describe("The Drowsy Fox", 2400.0, 1000.0), Is.EqualTo("The Drowsy Fox — 1.4 km on"));
            Assert.That(StripMapText.Describe("Ashford", 400.0, 1000.0), Is.EqualTo("Ashford — 600 m back"));
            Assert.That(StripMapText.Describe("Ashford", 1010.0, 1000.0), Is.EqualTo("Ashford — here"));
            Assert.That(StripMapText.Kilometres(2500.0), Is.EqualTo("2.5 km"));
            Assert.That(StripMapText.Kilometres(3000.0), Is.EqualTo("3 km"));
            Assert.That(StripMapText.Spaced("The Wexmoor Downs"), Is.EqualTo("T H E   W E X M O O R   D O W N S"));
            StripLabelStyle style = StripMapText.StyleFor(Feature(1, WaypointKind.Inn, "X", 30.0, 0.0), 0.0);
            Assert.That(style.PivotX, Is.EqualTo(0f), "a place right of the road is lettered on its right");
            style = StripMapText.StyleFor(Feature(1, WaypointKind.Inn, "X", -30.0, 0.0), 0.0);
            Assert.That(style.PivotX, Is.EqualTo(1f));
        }

        // ------------------------------------------------------------------ the finite map's drawing stays identical

        /// <summary>
        /// The shared drawing code (<see cref="MapRaster"/> and <see cref="MapInk"/>) paints this synthetic scene — relief and
        /// contours, a lake with shore and ripples, woods and tree marks, roads, every settlement icon, the peak, pass and
        /// viewpoint signs, the frame and north arrow — exactly as the finite map's code did before it was shared (the golden hash
        /// was computed with the original inline code).
        /// </summary>
        [Test]
        public void FiniteMapDrawingIsUnchanged()
        {
            const int n = 256;
            var raster = new MapRaster(n, MapInk.Paper);
            float[] mask = raster.NewMask();
            Color[] px = raster.Pixels;
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++) h[y * n + x] = 40f + 30f * (float)Math.Sin(x * 0.05) + 25f * (float)Math.Cos(y * 0.037) + 0.02f * x * y / 4f;
            }

            const float mpp = 2.3f;
            for (int y = 1; y < n - 1; y++)
            {
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    float dx = (h[i + 1] - h[i - 1]) / (2f * mpp);
                    float dz = (h[i + n] - h[i - n]) / (2f * mpp);
                    float altitude = Mathf.Clamp01((h[i] - 0f) / Mathf.Max(1f, 120f));
                    px[i] = MapInk.ShadeRelief(px[i], dx, dz, h[i], altitude, mpp, 20f, 5f);
                }
            }

            var wet = new bool[n * n];
            for (int i = 0; i < wet.Length; i++) wet[i] = h[i] < 22f;
            for (int y = 1; y < n - 1; y++)
            {
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (!wet[i]) continue;
                    bool shore = !wet[i - 1] || !wet[i + 1] || !wet[i - n] || !wet[i + n];
                    px[i] = MapInk.ShadeWater(px[i], x, y);
                    if (shore) MapInk.MarkShore(mask, i, n);
                }
            }

            raster.Composite(mask, MapInk.WaterInk, 0.75f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = x > 150 && y > 40 && y < 200 ? 0.7f : 0f;
                    if (d > 0.05f) mask[y * n + x] = Mathf.Clamp01((d - 0.05f) / 1.2f);
                }
            }

            raster.CompositeMultiply(mask, MapInk.ForestWash, 0.8f);
            for (int y = 3; y < n; y += 6)
            {
                for (int x = 153; x < n; x += 6)
                {
                    if ((x * 7 + y * 13) % 5 == 0) continue;
                    MapInk.TreeMark(raster, mask, new Vector2(x + 0.3f, y - 0.4f));
                }
            }

            raster.Composite(mask, MapInk.ForestInk, 0.7f);
            raster.MaskSegment(mask, new Vector2(10, 10), new Vector2(120, 80), 1.35f);
            raster.MaskSegment(mask, new Vector2(120, 80), new Vector2(200, 240), 1.35f);
            raster.Composite(mask, MapInk.RoadInk, 0.92f);

            const float scale = 1.3f;
            Vector2[] at = { new Vector2(60, 40), new Vector2(120, 80), new Vector2(170, 170) };
            SettlementType[] types = { SettlementType.Town, SettlementType.Village, SettlementType.Inn };
            for (int k = 0; k < 3; k++) raster.MaskDisc(mask, at[k], MapTextureRenderer.IconRadius(types[k]) * scale + 3f * scale);
            raster.Composite(mask, MapInk.Paper * 0.97f, 0.9f);
            for (int k = 0; k < 3; k++) MapInk.SettlementIcon(raster, mask, at[k], types[k], scale);
            raster.Composite(mask, MapInk.InkBrown, 0.95f);
            MapInk.PeakSign(raster, mask, new Vector2(40, 200), scale);
            MapInk.PassSign(raster, mask, new Vector2(90, 150), scale);
            MapInk.ViewpointSign(raster, mask, new Vector2(200, 60), scale);
            raster.Composite(mask, MapInk.InkBrown, 0.95f);

            float inner = 0.07f * n;
            float outer = inner - 0.012f * n;
            Rect(raster, mask, inner, n - inner, 1.1f);
            Rect(raster, mask, outer, n - outer, 0.7f);
            raster.Composite(mask, MapInk.InkBrown, 0.8f);
            float s = n / 1024f;
            var c = new Vector2(n * 0.9f, n * 0.86f);
            raster.MaskSegment(mask, c + new Vector2(0f, -22f) * s, c + new Vector2(0f, 14f) * s, 1.2f * s);
            raster.MaskConvex(mask, c + new Vector2(-6f, 10f) * s, c + new Vector2(6f, 10f) * s, c + new Vector2(0f, 26f) * s);
            raster.MaskRing(mask, c + new Vector2(0f, -4f) * s, 11f * s, 1.2f * s);
            raster.Composite(mask, MapInk.InkBrown, 0.85f);

            // Per runtime, like the finite goldens: Unity's Mono evaluates float arithmetic in double where CoreCLR rounds every step.
            // The Mono value was computed 2026-10-06 in the editor's own Mono (Tools: SCRATCH mono/embedded.ps1, see Docs/DESIGN.md) and is
            // the same for the drawing code before and after the shared waypoint kinds of that day; CoreCLR still gives the original hash.
            bool mono = Type.GetType("Mono.Runtime") != null;
            Assert.That(HashColours(raster.Pixels).ToString("X16"), Is.EqualTo(mono ? "875F8C403A8576C1" : "4FD889D86939408E"));
        }

        // ------------------------------------------------------------------ helpers

        private static void Rect(MapRaster raster, float[] mask, float lo, float hi, float radius)
        {
            raster.MaskSegment(mask, new Vector2(lo, lo), new Vector2(hi, lo), radius);
            raster.MaskSegment(mask, new Vector2(hi, lo), new Vector2(hi, hi), radius);
            raster.MaskSegment(mask, new Vector2(hi, hi), new Vector2(lo, hi), radius);
            raster.MaskSegment(mask, new Vector2(lo, hi), new Vector2(lo, lo), radius);
        }

        private static StripFeature Feature(int id, WaypointKind kind, string label, double u, double v)
        {
            return new StripFeature { Waypoint = new Waypoint { Id = id, Kind = kind, Name = label, S = v }, U = u, V = v, Class = StripMapContent.ClassOf(kind), Label = label };
        }

        private static ulong Hash(Color32[] px)
        {
            ulong h = 1469598103934665603UL;
            foreach (Color32 c in px)
            {
                h = (h ^ c.r) * 1099511628211UL;
                h = (h ^ c.g) * 1099511628211UL;
                h = (h ^ c.b) * 1099511628211UL;
                h = (h ^ c.a) * 1099511628211UL;
            }

            return h;
        }

        private static ulong HashColours(Color[] px)
        {
            ulong h = 1469598103934665603UL;
            foreach (Color c in px)
            {
                Mix(ref h, c.r);
                Mix(ref h, c.g);
                Mix(ref h, c.b);
                Mix(ref h, c.a);
            }

            return h;
        }

        private static void Mix(ref ulong h, float f)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(f);
            for (int k = 0; k < 4; k++)
            {
                h ^= (bits >> (8 * k)) & 0xFF;
                h *= 1099511628211UL;
            }
        }

        /// <summary>A generator with one extra waypoint (an unknown kind) beside the road.</summary>
        private sealed class ExtraPlaces : IEndlessGenerator
        {
            private readonly IEndlessGenerator inner;

            public ExtraPlaces(IEndlessGenerator inner, Waypoint extra)
            {
                this.inner = inner;
                Extra = extra;
            }

            public Waypoint Extra { get; }

            public void Place(Waypoint w)
            {
                RoadPoint r = inner.Road(w.S);
                w.X = r.X + 12.0;
                w.Z = r.Z;
                w.Height = r.Height;
                w.RoadHeadingDeg = r.HeadingDeg;
            }

            public EndlessParams Params => inner.Params;
            public RoadPoint Road(double s) => inner.Road(s);
            public double NearestS(double x, double z, double hintS) => inner.NearestS(x, z, hintS);
            public RegionInfo Region(double s) => inner.Region(s);
            public float Height(double x, double z) => inner.Height(x, z);
            public float WaterHeight(double x, double z) => inner.WaterHeight(x, z);
            public TileBlueprint GenerateTile(TileCoord coord, TileLod lod) => inner.GenerateTile(coord, lod);

            public void Waypoints(double sFrom, double sTo, List<Waypoint> results)
            {
                inner.Waypoints(sFrom, sTo, results);
                if (Extra.S >= sFrom && Extra.S <= sTo)
                {
                    results.Add(Extra);
                    results.Sort((a, b) => a.S.CompareTo(b.S));
                }
            }
        }
    }
}
