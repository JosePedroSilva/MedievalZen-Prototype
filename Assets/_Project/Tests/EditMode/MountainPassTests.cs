using System;
using System.Collections.Generic;
using MedievalZen.Roads;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// Phase 1c, the mountain pass: on the default 2 km world for three seeds, the range has peaks rather than a plateau,
    /// a mountain inn stands high on the col, the pass road climbs to it within the grade limit with proper hairpins, the
    /// network stays connected (with a loop over the pass when both sides are reached), trees respect the tree line, roads
    /// stay dry, and everything is deterministic. Pure (no Unity objects); each world is generated once.
    /// </summary>
    public sealed class MountainPassTests
    {
        private static readonly Dictionary<int, WorldBlueprint> Worlds = new Dictionary<int, WorldBlueprint>();

        private static WorldBlueprint World(int seed)
        {
            if (!Worlds.TryGetValue(seed, out WorldBlueprint bp))
            {
                bp = WorldGenerator.Generate(new WorldGenParams { seed = seed }, PaletteKeys.Default);
                Worlds[seed] = bp;
            }

            return bp;
        }

        [Test]
        public void MountainInn_StandsHighOnTheCol([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            Assert.That(bp.Pass, Is.Not.Null, "The mountain inn is on by default.");
            if (bp.Pass.InnSite < 0)
            {
                // Graceful skip: allowed, but it must say why and leave no trace in the network.
                Assert.That(bp.Pass.SkipReason, Is.Not.Null.And.Not.Empty);
                Assert.That(bp.Pass.SegmentIds, Is.Empty);
                Assert.Inconclusive($"Seed {seed}: mountain inn skipped ({bp.Pass.SkipReason}).");
            }

            Settlement inn = bp.Roads.Settlements[bp.Pass.InnSite];
            Assert.That(inn.Type, Is.EqualTo(SettlementType.Inn));
            Assert.That(inn.Position.y, Is.GreaterThanOrEqualTo(MountainPassPlanner.MinInnRelief * bp.Params.hillAmplitude), $"{inn} is too low.");
            Assert.That(inn.Node.Segments.Count, Is.GreaterThanOrEqualTo(1), "The inn is on the road network.");
            Assert.That(bp.Pass.SegmentIds.Count, Is.EqualTo(inn.Node.Segments.Count), "Every road at the inn is a pass road.");
            Assert.That(bp.Landmarks.Exists(l => l.kind == LandmarkKind.Pass), Is.True, "The pass is a named landmark.");
        }

        [Test]
        public void Network_IsConnected_AndSegmentsEndOnTheirNodes([Values(12345, 42, 777)] int seed)
        {
            RoadNetwork network = World(seed).Roads;
            foreach (RoadSegment segment in network.Segments)
            {
                Assert.That(Vector3.Distance(segment.Points[0], segment.A.Position), Is.LessThan(1e-3f));
                Assert.That(Vector3.Distance(segment.Points[segment.Points.Count - 1], segment.B.Position), Is.LessThan(1e-3f));
            }

            foreach (Settlement from in network.Settlements)
            {
                RoadLocation start = network.Project(from.Position);
                foreach (Settlement to in network.Settlements)
                {
                    if (to == from) continue;
                    Assert.That(network.TryFindRoute(start, to, out _), Is.True, $"{from} -> {to}");
                }
            }
        }

        [Test]
        public void Roads_NeverExceedTheHardGradeLimit_AfterCarving([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            foreach (RoadSegment segment in bp.Roads.Segments)
            {
                float grade = MaxGrade(bp, segment.Points, 10f, out Vector3 where);
                Assert.That(grade, Is.LessThanOrEqualTo(WorldGenParams.HardRoadGrade),
                    $"Segment {segment.Id} climbs {grade * 100f:0.0} % over 10 m near ({where.x:0}, {where.z:0}).");
            }

            foreach (int id in bp.Pass?.SegmentIds ?? new List<int>())
            {
                float grade = MaxGrade(bp, bp.Roads.Segments[id].Points, 10f, out Vector3 where);
                Assert.That(grade, Is.LessThanOrEqualTo(bp.Params.maxRoadGrade + 0.02f), $"Pass road {id} at ({where.x:0}, {where.z:0}).");
            }
        }

        [Test]
        public void PassRoad_HairpinsKeepTheMinimumRadius_AndLegsKeepApart([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            if (bp.Pass == null || bp.Pass.InnSite < 0) Assert.Inconclusive("No pass road.");
            foreach (int id in bp.Pass.SegmentIds)
            {
                IReadOnlyList<Vector3> points = bp.Roads.Segments[id].Points;
                float[] along = Along(points);
                float total = along[along.Length - 1];
                for (int i = 0, a = 0, b = 0; i < points.Count; i++)
                {
                    if (along[i] < 12f || along[i] > total - 12f) continue;
                    while (a < i && along[i] - along[a + 1] >= 3f) a++;
                    while (b < points.Count - 1 && along[b] - along[i] < 3f) b++;
                    float r = Circumradius(points[a], points[i], points[b]);
                    Assert.That(r, Is.GreaterThanOrEqualTo(PassRouter.MinRadius - 0.6f), $"Pass road {id}: bend of radius {r:0.0} m at ({points[i].x:0}, {points[i].z:0}).");
                }

                for (int i = 0; i < points.Count; i += 3)
                {
                    for (int j = i + 3; j < points.Count; j += 3)
                    {
                        if (along[j] - along[i] < 70f) continue;
                        Assert.That(Flat(points[j] - points[i]).magnitude, Is.GreaterThanOrEqualTo(14f), $"Pass road {id} doubles back on itself at ({points[i].x:0}, {points[i].z:0}).");
                    }
                }
            }
        }

        [Test]
        public void TwoSidedPass_ClosesALoop([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            if (bp.Pass == null || bp.Pass.InnSite < 0) Assert.Inconclusive("No pass road.");
            if (!bp.Pass.TwoSided) Assert.Inconclusive($"Seed {seed}: one-sided pass (spur): {bp.Pass.Summary()}");
            RoadNetwork network = bp.Roads;
            var pass = new HashSet<int>(bp.Pass.SegmentIds);
            int with = IndependentCycles(network, new HashSet<int>());
            int without = IndependentCycles(network, pass);
            Assert.That(with, Is.GreaterThanOrEqualTo(1));
            Assert.That(with, Is.GreaterThanOrEqualTo(without + 1), "The pass over the range must add a loop to the network.");

            // Take away either pass leg: everything must still be connected (over the other leg and the lowland roads).
            foreach (int leg in bp.Pass.SegmentIds)
            {
                Assert.That(Components(network, new HashSet<int> { leg }), Is.EqualTo(1), $"Without pass road {leg} the network falls apart.");
            }
        }

        [Test]
        public void Range_HasPeaks_NotAPlateau([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            float[,] map = bp.Heightmap;
            float amplitude = bp.Params.hillAmplitude;
            int n = map.GetLength(0);
            float min = float.MaxValue, max = float.MinValue;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    min = Mathf.Min(min, map[z, x]);
                    max = Mathf.Max(max, map[z, x]);
                }
            }

            int massif = 0, top = 0;
            for (int z = 0; z < n; z += 2)
            {
                for (int x = 0; x < n; x += 2)
                {
                    if (map[z, x] - min >= 0.4f * (max - min)) massif++;
                    if (map[z, x] >= max - 0.1f * (max - min)) top++;
                }
            }

            Assert.That(massif, Is.GreaterThan(0));
            Assert.That(top / (float)massif, Is.LessThan(0.1f), "Plateau metric: the highest 10 % of the relief must be summits, not a table-land.");
            Assert.That(ProminentPeaks(map, amplitude, 25f), Is.GreaterThanOrEqualTo(3), "At least three summits with 25 m of prominence.");
        }

        [Test]
        public void Trees_RespectTheTreeLine([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            float amplitude = bp.Params.hillAmplitude;
            foreach (TreePlacement tree in bp.Trees)
            {
                float hn = HeightmapGenerator.SampleHeight(bp.Heightmap, bp.Params, tree.position.x, tree.position.z) / amplitude;
                bool conifer = tree.role == PropRole.PineTree || tree.role == PropRole.DeadTree;
                Assert.That(hn, Is.LessThan(bp.Params.treeLine + (conifer ? 0.16f : 0f)), $"{tree.role} at {hn * 100f:0} % of the relief ({tree.position}).");
            }
        }

        [Test]
        public void Roads_StayOutOfTheLakes([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            if (bp.WaterHeight <= 0f) return;
            var pass = new HashSet<int>(bp.Pass?.SegmentIds ?? new List<int>());
            int points = 0, wet = 0;
            foreach (RoadSegment segment in bp.Roads.Segments)
            {
                foreach (Vector3 point in segment.Points)
                {
                    points++;
                    bool under = HeightmapGenerator.SampleHeight(bp.Heightmap, bp.Params, point.x, point.z) < bp.WaterHeight - 0.25f;
                    if (!under) continue;
                    wet++;
                    Assert.That(pass.Contains(segment.Id), Is.False, $"Pass road under water at {point}.");
                }
            }

            Assert.That(wet, Is.LessThanOrEqualTo(points / 100), $"{wet} of {points} road points are under water.");
        }

        [Test]
        public void Generation_IsDeterministic_WithThePass()
        {
            WorldBlueprint a = World(42);
            WorldBlueprint b = WorldGenerator.Generate(new WorldGenParams { seed = 42 }, PaletteKeys.Default);
            Assert.That(b.Heightmap, Is.EqualTo(a.Heightmap));
            Assert.That(b.Settlements.Count, Is.EqualTo(a.Settlements.Count));
            for (int i = 0; i < a.Settlements.Count; i++)
            {
                Assert.That(b.Settlements[i].Name, Is.EqualTo(a.Settlements[i].Name));
                Assert.That(b.Settlements[i].Position, Is.EqualTo(a.Settlements[i].Position));
            }

            Assert.That(b.Roads.Segments.Count, Is.EqualTo(a.Roads.Segments.Count));
            for (int s = 0; s < a.Roads.Segments.Count; s++)
            {
                Assert.That(b.Roads.Segments[s].Points, Is.EqualTo(a.Roads.Segments[s].Points), $"Segment {s}");
            }

            Assert.That(b.Pass.Summary(), Is.EqualTo(a.Pass.Summary()));
            Assert.That(b.Props.Count, Is.EqualTo(a.Props.Count));
            Assert.That(b.Trees.Count, Is.EqualTo(a.Trees.Count));
            for (int i = 0; i < a.Props.Count; i++)
            {
                if (!PropRole.IsRock(a.Props[i].role)) continue;
                Assert.That(b.Props[i].role, Is.EqualTo(a.Props[i].role));
                Assert.That(b.Props[i].position, Is.EqualTo(a.Props[i].position), $"{a.Props[i].role} {i}");
                Assert.That(b.Props[i].scale, Is.EqualTo(a.Props[i].scale));
                Assert.That(b.Props[i].pitch, Is.EqualTo(a.Props[i].pitch));
            }
        }

        // ------------------------------------------------------------------ rocks, cliffs and the shape of the summits

        /// <summary>Rock-like props with measured metrics, with their audit against the final heightmap.</summary>
        private static List<(PropPlacement prop, PropMetrics metrics, RockAudit audit)> Rocks(WorldBlueprint bp)
        {
            var keys = PaletteKeys.Default;
            var ctx = new RockPlacer.Context(bp.Params, bp.Heightmap, keys, null);
            var result = new List<(PropPlacement, PropMetrics, RockAudit)>();
            foreach (PropPlacement prop in bp.Props)
            {
                bool rockLike = PropRole.IsRock(prop.role) || prop.role == PropRole.StoneStack || prop.role == PropRole.StonePile;
                if (!rockLike || !keys.TryGetMetrics(prop.role, prop.variant, out PropMetrics m)) continue;
                result.Add((prop, m, RockPlacer.Inspect(ctx, m, prop)));
            }

            return result;
        }

        [Test]
        public void Rocks_AreEmbedded_WithNoAirGap([Values(12345, 42, 777)] int seed)
        {
            var rocks = Rocks(World(seed));
            Assert.That(rocks.Count, Is.GreaterThan(200), "Rock-like props expected.");
            foreach (var (prop, _, audit) in rocks)
            {
                Assert.That(audit.gap, Is.LessThanOrEqualTo(0.05f), $"{prop.role} at {prop.position}: air gap {audit.gap:0.00} m at the edge of its footprint.");
                Assert.That(audit.aabbGap, Is.LessThanOrEqualTo(0.05f), $"{prop.role} at {prop.position}: lowest point {audit.aabbGap:0.00} m above the lowest ground under it.");
            }
        }

        [Test]
        public void Cliffs_AreBacked_DoNotLoom_AndAvoidConvexGround([Values(12345, 42, 777)] int seed)
        {
            int cliffs = 0;
            foreach (var (prop, _, audit) in Rocks(World(seed)))
            {
                if (!PropRole.IsCliff(prop.role)) continue;
                cliffs++;
                Assert.That(audit.backing, Is.GreaterThanOrEqualTo(RockPlacer.MinBacking - 0.02f), $"{prop.role} at {prop.position}: backed by only {audit.backing:0.00} of its height.");
                Assert.That(audit.loom, Is.LessThanOrEqualTo(0.35f * audit.height + 0.5f), $"{prop.role} at {prop.position}: top {audit.loom:0.0} m above the highest ground under it.");
                Assert.That(audit.convex, Is.False, $"{prop.role} at {prop.position} stands on a crest or knoll.");
            }

            Assert.That(cliffs, Is.GreaterThan(10), "Cliff faces expected on the steep ground of the range.");
        }

        [Test]
        public void Rocks_AreSizedInMetres_UnderTheCap([Values(12345, 42, 777)] int seed)
        {
            foreach (var (prop, _, audit) in Rocks(World(seed)))
            {
                Assert.That(audit.height, Is.LessThanOrEqualTo(RockPlacer.MaxHeight), $"{prop.role} at {prop.position} is {audit.height:0.0} m tall.");
                if (prop.role == PropRole.Rock || prop.role == PropRole.LowlandRock) Assert.That(audit.height, Is.LessThanOrEqualTo(2.6f), $"{prop.role} boulder {audit.height:0.0} m.");
                if (PropRole.IsCliff(prop.role)) Assert.That(audit.height, Is.InRange(2.9f, 8.1f), $"{prop.role} {audit.height:0.0} m.");
            }
        }

        [Test]
        public void Highlands_UseOneRockFamily_MeadowOnly([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            var paths = new Dictionary<string, string[]>();
            foreach (var (family, list) in SyntyCatalog.Families) paths[family] = list;
            int high = 0;
            foreach (PropPlacement prop in bp.Props)
            {
                if (!PropRole.IsRock(prop.role)) continue;
                Assert.That(prop.role, Is.Not.EqualTo(PropRole.GroundCliff), "The Ground_Cliff_Large mesas are not scattered.");
                float hn = HeightmapGenerator.SampleHeight(bp.Heightmap, bp.Params, prop.position.x, prop.position.z) / bp.Params.hillAmplitude;
                if (hn < 0.45f) continue;
                high++;
                string[] list = paths[prop.role];
                string path = list[((prop.variant % list.Length) + list.Length) % list.Length];
                Assert.That(path.Contains("PNB_Meadow_Forest"), Is.True, $"{prop.role} ({path}) at {hn * 100f:0} % of the relief: only Meadow rock pieces in the range.");
            }

            Assert.That(high, Is.GreaterThan(20), "Rocks and cliffs expected in the range.");
        }

        [Test]
        public void Summits_HaveCliffBands_SharpCrests_AndKeepTheRelief([Values(12345, 42, 777)] int seed)
        {
            WorldBlueprint bp = World(seed);
            WorldGenParams p = bp.Params;
            float amplitude = p.hillAmplitude;
            float min = float.MaxValue, max = float.MinValue;
            foreach (float v in bp.Heightmap)
            {
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }

            Assert.That((max - min) * amplitude, Is.InRange(230f, 241f), "Total relief stays about 240 m.");

            // Cliff bands: on the upper range both steep risers (> 45 degrees) and ledges (< 17 degrees) are common.
            int upper = 0, steep = 0, ledge = 0;
            double sharp = 0.0;
            int convex = 0;
            for (float z = 6f; z < p.worldSize - 6f; z += 4f)
            {
                for (float x = 6f; x < p.worldSize - 6f; x += 4f)
                {
                    float h = HeightmapGenerator.SampleHeight(bp.Heightmap, p, x, z);
                    if (h < 0.45f * amplitude) continue;
                    upper++;
                    float slope = HeightmapGenerator.SampleSlope(bp.Heightmap, p.worldSize, amplitude, x, z);
                    if (slope > 1f) steep++;
                    if (slope < 0.3f) ledge++;
                    if (h < 0.7f * amplitude) continue;
                    float lap = (HeightmapGenerator.SampleHeight(bp.Heightmap, p, x + 6f, z) + HeightmapGenerator.SampleHeight(bp.Heightmap, p, x - 6f, z) +
                                 HeightmapGenerator.SampleHeight(bp.Heightmap, p, x, z + 6f) + HeightmapGenerator.SampleHeight(bp.Heightmap, p, x, z - 6f) - 4f * h) / 36f;
                    if (lap >= 0f) continue;
                    sharp -= lap;
                    convex++;
                }
            }

            Assert.That(steep / (float)upper, Is.GreaterThan(0.08f), "Cliff bands: faces steeper than 45 degrees.");
            Assert.That(ledge / (float)upper, Is.GreaterThan(0.12f), "Ledges and shoulders between the bands.");
            // Mean convex curvature above 70 % of the relief: about 0.05 on the old smooth cones, about 0.1 with aretes and horns.
            Assert.That(sharp / Mathf.Max(1, convex), Is.GreaterThan(0.075f), "Crests and summits are sharp, not rounded cones.");
        }

        [Test]
        public void RockPlacer_Rotate_MatchesUnityEulerConvention()
        {
            // Quaternion.Euler(pitch, yaw, roll): roll about Z, then pitch about X, then yaw about Y.
            AssertClose(RockPlacer.Rotate(Vector3.forward, 90f, 0f, 0f), Vector3.right);
            AssertClose(RockPlacer.Rotate(Vector3.forward, 0f, 90f, 0f), Vector3.down);
            AssertClose(RockPlacer.Rotate(Vector3.right, 0f, 0f, 90f), Vector3.up);
            AssertClose(RockPlacer.Rotate(Vector3.up, 90f, 90f, 0f), Vector3.right);
            Vector3 up = new Vector3(0.3f, 0.9f, -0.2f).normalized;
            RockPlacer.AnglesFor(up, 37f, out float pitch, out float roll);
            AssertClose(RockPlacer.Rotate(Vector3.up, 37f, pitch, roll), up);

            void AssertClose(Vector3 a, Vector3 b) => Assert.That(Vector3.Distance(a, b), Is.LessThan(1e-4f), $"{a} vs {b}");
        }

        [Test]
        public void RockPlacer_SinksBoulders_AndRejectsCliffsOnASummitCone()
        {
            var p = new WorldGenParams { seed = 1, worldSize = 400f, heightmapResolution = 201, hillAmplitude = 100f };
            int n = p.heightmapResolution;
            float spacing = p.worldSize / (n - 1);
            var cone = new float[n, n];
            var slope = new float[n, n];
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x * spacing - 200f) * (x * spacing - 200f) + (z * spacing - 200f) * (z * spacing - 200f));
                    cone[z, x] = Mathf.Clamp01(1f - d / 110f);                      // a 100 m cone, slope 0.9
                    slope[z, x] = Mathf.Clamp01((x * spacing) * 0.9f / 100f * 0.4f);  // a plane rising 0.36 m per m towards +x
                }
            }

            PaletteKeys keys = PaletteKeys.Default;
            var onSlope = new RockPlacer.Context(p, slope, keys, null);
            Assert.That(RockPlacer.TryPlace(onSlope, PropRole.Rock, 3, 150f, 200f, 2f, 40f, 0f, out PropPlacement boulder, out _), Is.True);
            keys.TryGetMetrics(PropRole.Rock, 3, out PropMetrics bm);
            RockAudit a = RockPlacer.Inspect(onSlope, bm, boulder);
            Assert.That(a.gap, Is.LessThanOrEqualTo(0f));
            Assert.That(a.aabbGap, Is.LessThanOrEqualTo(0f));
            Assert.That(Mathf.Abs(boulder.pitch) + Mathf.Abs(boulder.roll), Is.GreaterThan(1f), "Boulders lean with the slope.");

            var onCone = new RockPlacer.Context(p, cone, keys, null);
            for (int v = 0; v < keys.Count(PropRole.Cliff); v++)
            {
                Assert.That(RockPlacer.TryPlace(onCone, PropRole.Cliff, v, 200f, 204f, 6f, 0f, 0f, out _, out _), Is.False, "No cliff block on a summit.");
            }
        }

        [Test]
        public void LimitGrade_KeepsTheGrade_AndPinsTheEnds()
        {
            var along = new float[101];
            var heights = new float[101];
            for (int i = 0; i <= 100; i++)
            {
                along[i] = i * 2f;
                heights[i] = i < 50 ? 0f : 20f;   // a 20 m cliff half way along a 200 m road (0.12 × 200 m = 24 m is enough)
            }

            RoadPlanner.LimitGrade(heights, along, 0.12f, 0f, 20f);
            Assert.That(heights[0], Is.EqualTo(0f).Within(1e-4f));
            Assert.That(heights[100], Is.EqualTo(20f).Within(1e-4f));
            for (int i = 1; i <= 100; i++) Assert.That(Mathf.Abs(heights[i] - heights[i - 1]) / 2f, Is.LessThanOrEqualTo(0.12f + 1e-4f));
            Assert.That(heights[25], Is.LessThan(heights[75]), "The ramp still climbs from the low end to the high end.");

            // Ends too far apart for the grade: a straight ramp between them.
            for (int i = 0; i <= 100; i++) heights[i] = 0f;
            RoadPlanner.LimitGrade(heights, along, 0.12f, 0f, 40f);
            Assert.That(heights[50], Is.EqualTo(20f).Within(1e-3f));
        }

        // ------------------------------------------------------------------ helpers

        private static float MaxGrade(WorldBlueprint bp, IReadOnlyList<Vector3> points, float window, out Vector3 where)
        {
            float[] along = Along(points);
            var h = new float[points.Count];
            for (int i = 0; i < points.Count; i++) h[i] = HeightmapGenerator.SampleHeight(bp.Heightmap, bp.Params, points[i].x, points[i].z);
            float worst = 0f;
            where = points[0];
            for (int i = 0, j = 0; i < points.Count; i++)
            {
                while (j < points.Count - 1 && along[j] - along[i] < window) j++;
                if (along[j] - along[i] < window * 0.95f) break;
                float g = Mathf.Abs(h[j] - h[i]) / (along[j] - along[i]);
                if (g <= worst) continue;
                worst = g;
                where = points[i];
            }

            return worst;
        }

        private static float[] Along(IReadOnlyList<Vector3> points)
        {
            var along = new float[points.Count];
            for (int i = 1; i < points.Count; i++) along[i] = along[i - 1] + Flat(points[i] - points[i - 1]).magnitude;
            return along;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static float Circumradius(Vector3 a, Vector3 b, Vector3 c)
        {
            a = Flat(a);
            b = Flat(b);
            c = Flat(c);
            float cross = Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x));
            return cross < 1e-6f ? float.PositiveInfinity : (b - a).magnitude * (c - b).magnitude * (a - c).magnitude / (2f * cross);
        }

        /// <summary>Cyclomatic number (independent loops) of the network without <paramref name="removedSegments"/>.</summary>
        private static int IndependentCycles(RoadNetwork network, HashSet<int> removedSegments) =>
            network.Segments.Count - removedSegments.Count - network.Nodes.Count + Components(network, removedSegments);

        private static int Components(RoadNetwork network, HashSet<int> removedSegments)
        {
            var parent = new int[network.Nodes.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            foreach (RoadSegment segment in network.Segments)
            {
                if (removedSegments.Contains(segment.Id)) continue;
                parent[Find(segment.A.Id)] = Find(segment.B.Id);
            }

            int components = 0;
            for (int i = 0; i < parent.Length; i++)
            {
                if (Find(i) == i) components++;
            }

            return components;
        }

        /// <summary>Summits with at least <paramref name="minimum"/> metres of topographic prominence (8 m grid, union-find).</summary>
        private static int ProminentPeaks(float[,] map, float amplitude, float minimum)
        {
            int n = map.GetLength(0);
            int stride = Math.Max(1, (n - 1) / 256);
            int m = (n - 1) / stride + 1;
            var h = new float[m * m];
            var order = new int[m * m];
            for (int z = 0; z < m; z++)
            {
                for (int x = 0; x < m; x++)
                {
                    h[z * m + x] = map[z * stride, x * stride] * amplitude;
                    order[z * m + x] = z * m + x;
                }
            }

            Array.Sort(order, (a, b) => h[b] != h[a] ? h[b].CompareTo(h[a]) : a.CompareTo(b));
            var parent = new int[m * m];
            var peak = new int[m * m];
            for (int i = 0; i < parent.Length; i++) parent[i] = -1;
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            int count = 0;
            float lowest = h[order[order.Length - 1]];
            foreach (int c in order)
            {
                parent[c] = c;
                peak[c] = c;
                var roots = new List<int>(8);
                int cx = c % m, cz = c / m;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= m || z >= m || parent[z * m + x] < 0) continue;
                        int r = Find(z * m + x);
                        if (!roots.Contains(r)) roots.Add(r);
                    }
                }

                if (roots.Count == 0) continue;
                roots.Sort((a, b) => h[peak[b]].CompareTo(h[peak[a]]));
                for (int i = 1; i < roots.Count; i++)
                {
                    if (h[peak[roots[i]]] - h[c] >= minimum) count++;   // that summit's region merges here: its key col
                    parent[roots[i]] = roots[0];
                }

                parent[c] = roots[0];
            }

            if (h[order[0]] - lowest >= minimum) count++;   // the highest summit
            return count;
        }
    }
}
