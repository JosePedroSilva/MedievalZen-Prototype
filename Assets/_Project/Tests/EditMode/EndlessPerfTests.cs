using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Review;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The performance pass of the endless road (2026-10-05): the instanced renderer's pass over its cells (whole-cell band decisions,
    /// the pure view frustum, shadow casters culled by the reach of their shadow, the reuse rule, the gather's slices), the settings
    /// upgrade, the frame statistics window and the review tour's measurement rule. Pure (no Unity objects).
    /// </summary>
    public sealed class EndlessPerfTests
    {
        private static readonly TreeLodDistances Lod = new TreeLodDistances(110f, 300f);

        // ------------------------------------------------------------------ whole-cell decisions

        [Test]
        public void UniformBandIsTheBandOfEveryTreeWhateverItsPast()
        {
            Assert.That(TreeDrawRules.UniformBand(0f, 100f, Lod), Is.EqualTo((byte)TreeDrawBand.Detail));
            Assert.That(TreeDrawRules.UniformBand(0f, 105f, Lod), Is.EqualTo(TreeDrawRules.Unknown), "within the 5.5 m margin of the full distance");
            Assert.That(TreeDrawRules.UniformBand(120f, 280f, Lod), Is.EqualTo((byte)TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.UniformBand(114f, 200f, Lod), Is.EqualTo(TreeDrawRules.Unknown));
            Assert.That(TreeDrawRules.UniformBand(120f, 290f, Lod), Is.EqualTo(TreeDrawRules.Unknown), "within the 15 m margin of the mid distance");
            Assert.That(TreeDrawRules.UniformBand(316f, 380f, Lod), Is.EqualTo((byte)TreeDrawBand.Low));
            Assert.That(TreeDrawRules.UniformBand(80f, 160f, Lod), Is.EqualTo(TreeDrawRules.Unknown), "straddles the full distance");

            // Property: whenever a band is returned, the hysteresis rule gives it for every distance in the cell and every past band.
            var rng = new System.Random(7);
            byte[] pasts = { TreeDrawRules.Unknown, (byte)TreeDrawBand.Detail, (byte)TreeDrawBand.Mid, (byte)TreeDrawBand.Low };
            for (int i = 0; i < 20000; i++)
            {
                float near = (float)(rng.NextDouble() * 500.0), far = near + (float)(rng.NextDouble() * 60.0);
                byte band = TreeDrawRules.UniformBand(near, far, Lod);
                if (band == TreeDrawRules.Unknown) continue;
                for (int k = 0; k <= 10; k++)
                {
                    float d = near + (far - near) * k / 10f;
                    foreach (byte past in pasts) Assert.That((byte)TreeDrawRules.BandFor(d, past, Lod), Is.EqualTo(band), $"near {near} far {far} d {d} past {past}");
                }
            }
        }

        [Test]
        public void BoxDistancesAreNearestAndFarthestCorner()
        {
            var min = new Vector3(10f, 0f, 0f);
            var max = new Vector3(20f, 5f, 10f);
            Assert.That(TreeDrawRules.NearestSq(Vector3.zero, min, max), Is.EqualTo(100f).Within(1e-3f));
            Assert.That(TreeDrawRules.NearestSq(new Vector3(15f, 2f, 5f), min, max), Is.EqualTo(0f));
            Assert.That(TreeDrawRules.FarthestSq(Vector3.zero, min, max), Is.EqualTo(400f + 25f + 100f).Within(1e-3f));
        }

        // ------------------------------------------------------------------ the pass over the cells

        private const string TreeRole = "WalkTree", PlantRole = "WalkPlant", FernRole = "WalkFern";

        /// <summary>Three prototypes: a tree with mesh LODs (proxy), a single-mesh plant, a fern.</summary>
        private static (TreePrototypeIndex index, TreeWalkPrototype[] protos) Prototypes()
        {
            var index = new TreePrototypeIndex();
            index.AddPrototype(12f, 4f);
            index.AddPrototype(2f, 1.5f);
            index.AddPrototype(1f, 0.8f);
            index.SetFamily(TreeRole, new[] { 0 });
            index.SetFamily(PlantRole, new[] { 1 });
            index.SetFamily(FernRole, new[] { 2 });
            var protos = new[]
            {
                new TreeWalkPrototype { Proxy = true },
                new TreeWalkPrototype { Plant = true, SingleMesh = true },
                new TreeWalkPrototype { Plant = true, Fern = true, SingleMesh = true },
            };
            return (index, protos);
        }

        /// <summary>A 256 m tile of scattered trees, plants and ferns (deterministic).</summary>
        private static TreeCellSet Tile(TreePrototypeIndex index, Vector3 corner, int seed)
        {
            var rng = new System.Random(seed);
            var trees = new List<TreePlacement>();
            string[] roles = { TreeRole, PlantRole, FernRole };
            for (int i = 0; i < 3000; i++)
            {
                trees.Add(new TreePlacement
                {
                    role = roles[i % 3], variant = 0, yaw = (float)(rng.NextDouble() * 360.0), scale = 0.8f + 0.6f * (float)rng.NextDouble(),
                    position = new Vector3((float)(rng.NextDouble() * 256.0), (float)(rng.NextDouble() * 6.0), (float)(rng.NextDouble() * 256.0)),
                });
            }

            TreeCellSet set = TreeCellSet.Build(trees, 256f, index);
            set.Offset(corner);
            return set;
        }

        private static TreeWalkFrame Frame(Vector3 eye, Vector3 forward, bool cull, Vector3 light)
        {
            return new TreeWalkFrame
            {
                Eye = eye, Frustum = TreeFrustum.FromView(eye, forward, Vector3.up, 60f, 1.6f, 3000f, 0f), Lod = Lod,
                ShadowDistance = 300f, ProxyDistance = 100f, PlantShadowDistance = 40f, FernDrawDistance = 160f, UndergrowthDrawDistance = 250f,
                Shadows = true, CullShadows = cull, LightForward = light, LowestReceiver = -25f, Pad = 2f,
            };
        }

        [Test]
        public void TheFirstPassDrawsEveryInstanceInViewInItsPlainBand()
        {
            var (index, protos) = Prototypes();
            var sets = new List<TreeCellSet>();
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++) sets.Add(Tile(index, new Vector3(x * 256f, 0f, z * 256f), 100 + 3 * x + z));
            }

            Vector3 eye = new Vector3(128f, 3f, 20f), forward = new Vector3(0.3f, -0.05f, 1f).normalized;
            var walker = new TreeCellWalker();
            walker.Walk(sets, Frame(eye, forward, false, new Vector3(0f, -1f, 0f)), protos);

            // Brute force: every instance in a cell the frustum keeps, its plain band; ferns to their own fade threshold.
            TreeFrustum frustum = TreeFrustum.FromView(eye, forward, Vector3.up, 60f, 1.6f, 3000f, 0f);
            var expected = new int[3, 3];
            var visibleTotal = new int[3];
            foreach (TreeCellSet set in sets)
            {
                foreach (TreeCell cell in set.Cells)
                {
                    if (!frustum.Intersects(cell.Min, cell.Max, 2f)) continue;
                    for (int p = 0; p < 3; p++)
                    {
                        Matrix4x4[] list = cell.ByPrototype[p];
                        if (list == null) continue;
                        foreach (Matrix4x4 m in list)
                        {
                            float d = (new Vector3(m.m03, m.m13, m.m23) - eye).magnitude;
                            if (p == 2 && d > TreeDrawRules.FadeDistance(160f, m.m03 - set.Applied.x, m.m23 - set.Applied.z)) continue;
                            expected[p, (int)TreeDrawRules.BandFor(d, Lod)]++;
                            visibleTotal[p]++;
                        }
                    }
                }
            }

            TreeGather g = walker.Gather;
            for (int band = 0; band < 3; band++) Assert.That(g.Count(g.Key(band, 0)), Is.EqualTo(expected[0, band]), $"trees in band {band}");
            for (int p = 1; p < 3; p++)
            {
                int total = 0;
                for (int band = 0; band < 3; band++) total += g.Count(g.Key(band, p));
                Assert.That(total, Is.EqualTo(visibleTotal[p]), $"single-mesh prototype {p}: same instances, any band");
            }

            Assert.That(walker.Stats.WholeLists, Is.GreaterThan(0), "most cells lie in one band");
            Assert.That(visibleTotal[0], Is.GreaterThan(100));
        }

        [Test]
        public void CastersWhoseShadowCannotReachTheViewAreCulled()
        {
            var (index, protos) = Prototypes();
            // One tile wholly behind the camera, which looks along +Z from z = 0.
            var sets = new List<TreeCellSet> { Tile(index, new Vector3(-128f, 0f, -300f), 5) };
            Vector3 eye = new Vector3(0f, 3f, 0f), forward = Vector3.forward;
            int Casters(Vector3 light, bool cull)
            {
                var w = new TreeCellWalker();
                w.Walk(sets, Frame(eye, forward, cull, light.normalized), protos);
                int n = 0;
                for (int ring = 0; ring < 2; ring++)
                {
                    for (int p = 0; p < 3; p++) n += w.Gather.Count(w.Gather.Key(TreeDrawRules.BandCount + ring, p));
                }

                return n;
            }

            int all = Casters(new Vector3(0f, -1f, 0.2f), false);
            Assert.That(all, Is.GreaterThan(0), "without culling every caster within 300 m casts");
            Assert.That(Casters(new Vector3(0f, -0.17f, -1f), true), Is.EqualTo(0), "low sun ahead: the shadows fall away from the view");
            Assert.That(Casters(new Vector3(0f, -1f, 0.1f), true), Is.EqualTo(0), "high sun: the shadows stay behind the camera");
            Assert.That(Casters(new Vector3(0f, -0.17f, 1f), true), Is.GreaterThan(0), "low sun behind the camera: long shadows reach into the view");
            Assert.That(Casters(new Vector3(0f, 0.2f, 1f), true), Is.EqualTo(all), "light from below the horizon: no culling");
        }

        [Test]
        public void ShadowSweepFollowsTheLightDownToTheLowestGround()
        {
            Vector3 sweep = TreeDrawRules.ShadowSweep(new Vector3(0f, -0.5f, 0.8660254f), 40f, 600f);
            Assert.That(sweep.y, Is.EqualTo(-40f).Within(1e-3f));
            Assert.That(sweep.z, Is.EqualTo(69.282f).Within(1e-2f));
            Assert.That(TreeDrawRules.ShadowSweep(new Vector3(0f, -0.04f, 0.9992f), 100f, 600f).magnitude, Is.EqualTo(600f).Within(1e-2f), "capped at twice the shadow distance");
            Assert.That(TreeDrawRules.ShadowSweep(new Vector3(0f, -0.01f, 1f), 100f, 600f), Is.EqualTo(Vector3.zero));
            Assert.That(TreeDrawRules.ShadowSweepPossible(new Vector3(0f, -0.01f, 1f)), Is.False);
            Assert.That(TreeDrawRules.ShadowSweepPossible(new Vector3(0f, -0.5f, 0.87f)), Is.True);
        }

        [Test]
        public void FrustumKeepsWhatIsAheadAndOpensByTheMargin()
        {
            TreeFrustum f = TreeFrustum.FromView(Vector3.zero, Vector3.forward, Vector3.up, 60f, 1f, 1000f, 0f);
            Assert.That(f.Intersects(new Vector3(-1f, -1f, 50f), new Vector3(1f, 1f, 52f)), Is.True);
            Assert.That(f.Intersects(new Vector3(-1f, -1f, -52f), new Vector3(1f, 1f, -50f)), Is.False, "behind");
            Assert.That(f.Intersects(new Vector3(-1f, -1f, 1100f), new Vector3(1f, 1f, 1102f)), Is.False, "beyond the far plane");
            // A box just outside the right edge (half angle 30°): 32° off the axis at 100 m.
            float x = 100f * Mathf.Tan(32f * Mathf.Deg2Rad);
            var min = new Vector3(x, -0.1f, 99.9f);
            var max = new Vector3(x + 0.2f, 0.1f, 100.1f);
            Assert.That(f.Intersects(min, max), Is.False);
            Assert.That(TreeFrustum.FromView(Vector3.zero, Vector3.forward, Vector3.up, 60f, 1f, 1000f, TreeDrawRules.ReuseTurn).Intersects(min, max), Is.True, "opened by 2°");
            Assert.That(f.Intersects(min, max, 4f), Is.True, "or padded");
            // Swept: a box behind the camera swept forward reaches the view.
            Assert.That(f.IntersectsSwept(new Vector3(-1f, -1f, -20f), new Vector3(1f, 1f, -18f), new Vector3(0f, 0f, 40f), 0f), Is.True);
            Assert.That(f.IntersectsSwept(new Vector3(-1f, -1f, -20f), new Vector3(1f, 1f, -18f), new Vector3(0f, 0f, -40f), 0f), Is.False);
        }

        // ------------------------------------------------------------------ reuse

        /// <summary>Turns <paramref name="v"/> about the vertical by <paramref name="degrees"/> (pure: +Z toward +X).</summary>
        private static Vector3 Yaw(Vector3 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
        }

        [Test]
        public void TheLastPassIsReusedOnlyWhileTheViewAndLightHardlyMove()
        {
            Vector3 eye = new Vector3(10f, 2f, 10f), f = Vector3.forward, up = Vector3.up, light = new Vector3(0.3f, -0.8f, 0.5f).normalized;
            bool Reuse(bool same, Vector3 e, Vector3 fw, Vector3 u, float fov, float aspect, Vector3 l) => TreeDrawRules.CanReuse(same, e, eye, fw, f, u, up, fov, 60f, aspect, 1.6f, l, light);
            Assert.That(Reuse(true, eye, f, up, 60f, 1.6f, light), Is.True);
            Assert.That(Reuse(false, eye, f, up, 60f, 1.6f, light), Is.False, "tiles, origin or settings changed");
            Assert.That(Reuse(true, eye + new Vector3(0.9f, 0f, 0f), f, up, 60f, 1.6f, light), Is.True);
            Assert.That(Reuse(true, eye + new Vector3(1.1f, 0f, 0f), f, up, 60f, 1.6f, light), Is.False, "moved over a metre");
            Assert.That(Reuse(true, eye, Yaw(f, 1.9f), up, 60f, 1.6f, light), Is.True);
            Assert.That(Reuse(true, eye, Yaw(f, 2.1f), up, 60f, 1.6f, light), Is.False, "turned over 2°");
            Assert.That(Reuse(true, eye, f, new Vector3(Mathf.Sin(3f * Mathf.Deg2Rad), Mathf.Cos(3f * Mathf.Deg2Rad), 0f), 60f, 1.6f, light), Is.False, "rolled");
            Assert.That(Reuse(true, eye, f, up, 50f, 1.6f, light), Is.False, "field of view changed");
            Assert.That(Reuse(true, eye, f, up, 60f, 1.7f, light), Is.False, "aspect changed");
            Assert.That(Reuse(true, eye, f, up, 60f, 1.6f, Yaw(light, 0.4f)), Is.True);
            Assert.That(Reuse(true, eye, f, up, 60f, 1.6f, Yaw(light, 1.5f)), Is.False, "the sun moved (0.8° of arc at this height)");
        }

        [Test]
        public void GatherSlicesKeepTheirOwnBounds()
        {
            var g = new TreeGather();
            g.Reset(TreeCellWalker.BucketCount, 2);
            int k = g.Key(1, 1);
            var block = new Matrix4x4[1000];
            for (int i = 0; i < block.Length; i++) block[i] = TreeCellSet.Pose(new Vector3(i, 0f, 0f), 0f, 1f);
            g.AddRange(k, block, new Vector3(0f, 0f, 0f), new Vector3(999f, 0f, 0f));
            Matrix4x4 m = TreeCellSet.Pose(new Vector3(5000f, 1f, 2f), 0f, 1f);
            for (int i = 0; i < 30; i++) g.Add(k, ref m);
            Assert.That(g.Count(k), Is.EqualTo(1030));
            Assert.That(g.Data(k)[1029].m03, Is.EqualTo(5000f));
            g.SliceBounds(k, 0, out Vector3 min0, out Vector3 max0);
            g.SliceBounds(k, 1, out Vector3 min1, out Vector3 max1);
            Assert.That(max0.x, Is.EqualTo(5000f), "the first slice (0..1022) holds 23 of the far instances");
            Assert.That(min1.x, Is.EqualTo(5000f), "the second slice holds only far instances");
            Assert.That(max1.x, Is.EqualTo(5000f));
            Assert.That(g.Count(g.Key(0, 0)), Is.EqualTo(0));
            g.Clear();
            Assert.That(g.Count(k), Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ settings upgrade

        [Test]
        public void SettingsUpgradeMovesOnlyOldDefaultsOnce()
        {
            var v = new EndlessSettingsMigration.Values { Version = 0, TreeShadowDistance = 90f, UndergrowthDrawDistance = 0f };
            var changes = new List<string>();
            Assert.That(EndlessSettingsMigration.Upgrade(ref v, changes), Is.True);
            Assert.That(v.Version, Is.EqualTo(EndlessSettingsMigration.Current));
            Assert.That(v.TreeShadowDistance, Is.EqualTo(300f));
            Assert.That(v.UndergrowthDrawDistance, Is.EqualTo(250f));
            Assert.That(v.ShadowCasterCulling && v.WalkReuse, Is.True);
            Assert.That(changes.Count, Is.EqualTo(7), "version 1's four, version 2's impostor distance and pixel rule, version 3's 420 → 320 m (2026-10-06)");

            var tuned = new EndlessSettingsMigration.Values { Version = 0, TreeShadowDistance = 200f, UndergrowthDrawDistance = 180f, ShadowCasterCulling = true, WalkReuse = true };
            Assert.That(EndlessSettingsMigration.Upgrade(ref tuned, null), Is.True, "the version still moves (the asset is saved with the new fields)");
            Assert.That(tuned.TreeShadowDistance, Is.EqualTo(200f), "a hand-tuned value survives");
            Assert.That(tuned.UndergrowthDrawDistance, Is.EqualTo(180f));

            Assert.That(EndlessSettingsMigration.Upgrade(ref v, changes), Is.False, "once only");
        }

        // ------------------------------------------------------------------ measuring

        [Test]
        public void FrameWindowAveragesWhatWasReported()
        {
            var w = new FrameStatsWindow(4);
            Assert.That(float.IsNaN(w.Mean(FrameStatsWindow.Gpu)), Is.True);
            w.Add(new FrameSample { FrameMs = 10f, GpuMs = float.NaN, CpuMainMs = 8f, TreeWalkReused = true });
            w.Add(new FrameSample { FrameMs = 20f, GpuMs = 12f, CpuMainMs = 9f });
            Assert.That(w.Mean(FrameStatsWindow.Frame), Is.EqualTo(15f));
            Assert.That(w.Mean(FrameStatsWindow.Gpu), Is.EqualTo(12f), "a frame without a GPU time is left out");
            Assert.That(w.ReusedShare(), Is.EqualTo(0.5f));
            Assert.That(w.Mean(FrameStatsWindow.Frame, 1), Is.EqualTo(20f), "the last frame");
            for (int i = 0; i < 4; i++) w.Add(new FrameSample { FrameMs = 30f });
            Assert.That(w.Count, Is.EqualTo(4));
            Assert.That(w.Mean(FrameStatsWindow.Frame), Is.EqualTo(30f), "the oldest dropped out");
            Assert.That(w.Max(FrameStatsWindow.Frame), Is.EqualTo(30f));
            w.Reset();
            Assert.That(w.Count, Is.EqualTo(0));
        }

        [Test]
        public void TourMeasuresEnoughFramesAndTime()
        {
            Assert.That(ReviewTourPlan.MeasureDone(29, 2f), Is.False);
            Assert.That(ReviewTourPlan.MeasureDone(30, 0.4f), Is.False);
            Assert.That(ReviewTourPlan.MeasureDone(30, 0.5f), Is.True);
            Assert.That(ReviewTourPlan.MeasureSkipFrames, Is.GreaterThanOrEqualTo(3), "frame timings arrive a few frames late");
        }
    }
}
