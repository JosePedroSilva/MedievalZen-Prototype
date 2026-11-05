using System.Collections.Generic;
using MedievalZen.Endless;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The second performance pass of the endless road (2026-10-06): the impostor band and its hysteresis, the silhouette match rule,
    /// the impostor solids, the apply slicing, the settings upgrade to version 2 and the main-thread marker window. Pure.
    /// </summary>
    public sealed class EndlessPerf2Tests
    {
        private static readonly TreeLodDistances Lod = new TreeLodDistances(110f, 300f, 420f);

        [Test]
        public void ImpostorBandBeyondItsDistance()
        {
            Assert.That(TreeDrawRules.BandFor(301f, Lod), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.BandFor(420f, Lod), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.BandFor(421f, Lod), Is.EqualTo(TreeDrawBand.Impostor));
            Assert.That(TreeDrawRules.BandFor(5000f, new TreeLodDistances(110f, 300f)), Is.EqualTo(TreeDrawBand.Low), "no impostor distance: never");
            Assert.That(TreeDrawRules.BandFor(5000f, new TreeLodDistances(110f, 300f, 0f)), Is.EqualTo(TreeDrawBand.Low));
        }

        [Test]
        public void ImpostorHysteresis()
        {
            float m = Lod.ImpostorMargin;
            Assert.That(m, Is.EqualTo(21f).Within(1e-3f));
            Assert.That(TreeDrawRules.BandFor(420f + m - 1f, (byte)TreeDrawBand.Low, Lod), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.BandFor(420f + m + 1f, (byte)TreeDrawBand.Low, Lod), Is.EqualTo(TreeDrawBand.Impostor));
            Assert.That(TreeDrawRules.BandFor(420f - m + 1f, (byte)TreeDrawBand.Impostor, Lod), Is.EqualTo(TreeDrawBand.Impostor));
            Assert.That(TreeDrawRules.BandFor(420f - m - 1f, (byte)TreeDrawBand.Impostor, Lod), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.BandFor(50f, (byte)TreeDrawBand.Impostor, Lod), Is.EqualTo(TreeDrawBand.Detail), "a jump resets the band");
        }

        [Test]
        public void ImpostorDistanceNeverSwallowsTheLowBand()
        {
            var d = new TreeLodDistances(110f, 300f, 305f);
            Assert.That(d.Impostor - d.ImpostorMargin, Is.GreaterThan(d.Mid + d.MidMargin));
        }

        [Test]
        public void UniformBandMatchesThePerTreeRuleForEveryPast()
        {
            byte[] pasts = { TreeDrawRules.Unknown, 0, 1, 2, 3 };
            for (float near = 0f; near < 900f; near += 7f)
            {
                for (float far = near; far < near + 60f; far += 9f)
                {
                    byte uniform = TreeDrawRules.UniformBand(near, far, Lod);
                    if (uniform == TreeDrawRules.Unknown) continue;
                    foreach (byte past in pasts)
                    {
                        for (float d = near; d <= far; d += 3f) Assert.That((byte)TreeDrawRules.BandFor(d, past, Lod), Is.EqualTo(uniform), $"near {near} far {far} past {past} d {d}");
                    }
                }
            }
        }

        [Test]
        public void SilhouetteRule()
        {
            // One 1080p pixel at a 60 degree view spans about 1.07 mm per metre of distance.
            Assert.That(TreeImpostor.PixelMetres(420, 60, 1080), Is.EqualTo(0.449).Within(0.001));
            Assert.That(TreeImpostor.SwapDistance(0.449, 1), Is.EqualTo(420).Within(1));
            Assert.That(TreeImpostor.DistanceFor(420f, 0.4f, 3f), Is.EqualTo(420f), "a close match swaps at the setting");
            Assert.That(TreeImpostor.DistanceFor(420f, 2.78f, 3f), Is.GreaterThan(800f), "the Meadow oak stays on its mesh");
            Assert.That(TreeImpostor.DistanceFor(420f, 2.78f, 0f), Is.EqualTo(420f), "0 px: no rule");
            Assert.That(float.IsPositiveInfinity(TreeImpostor.DistanceFor(float.PositiveInfinity, 0.4f, 3f)), Is.True);
            Assert.That(TreeImpostor.SilhouetteError("SM_Env_Tree_Meadow_01 (palette)", 12f), Is.LessThan(1.35f), "lobed since 2026-10-06 (was 2.78 m)");
            Assert.That(TreeImpostor.SilhouetteError("Unknown", 10f), Is.EqualTo(2f));
        }

        [Test]
        public void ImpostorSolids()
        {
            foreach (FarTreeShape shape in new[] { FarTreeShape.Broadleaf, FarTreeShape.Conifer, FarTreeShape.Dead })
            {
                FarTreeStyle style = FarTreeStyle.Proportional(shape, 10f, 3f, new Vector3(0.1f, 0.3f, 0.1f), new Vector3(0.2f, 0.15f, 0.1f));
                var v = new List<Vector3>();
                var t = new List<int>();
                var c = new List<Color32>();
                TreeImpostor.Build(style, v, t, c);
                Assert.That(t.Count / 3, Is.EqualTo(TreeImpostor.TrianglesOf(shape)), shape.ToString());
                Assert.That(c.Count, Is.EqualTo(v.Count));
                float top = 0f, minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
                foreach (Vector3 p in v)
                {
                    top = Mathf.Max(top, p.y);
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }

                Assert.That(top, Is.EqualTo(style.Height).Within(0.01f), "the silhouette height is the measured one");
                if (shape != FarTreeShape.Dead) Assert.That(Mathf.Min(maxX - minX, maxZ - minZ), Is.GreaterThan(0.2f * style.Height), "a solid, never a flat card");
            }
        }

        [Test]
        public void ApplySlices()
        {
            Assert.That(ApplySlicing.SplatRows(4), Is.EqualTo(64));
            Assert.That(ApplySlicing.SplatRows(8), Is.EqualTo(32));
            Assert.That(ApplySlicing.SplatRows(11), Is.EqualTo(16));
            Assert.That(ApplySlicing.SplatRows(16), Is.EqualTo(16));
            Assert.That(ApplySlicing.StartsNewGroup(0, 0, 30000, 32), Is.False, "an empty group takes a heavy building");
            Assert.That(ApplySlicing.StartsNewGroup(1, 30000, 200, 32), Is.True, "nothing joins a heavy building");
            Assert.That(ApplySlicing.StartsNewGroup(5, 2000, 30000, 32), Is.True, "a heavy building starts its own group");
            Assert.That(ApplySlicing.StartsNewGroup(31, 3000, 100, 32), Is.False);
            Assert.That(ApplySlicing.StartsNewGroup(32, 3000, 100, 32), Is.True);
        }

        [Test]
        public void SettingsUpgradeToVersion3()
        {
            var v = new EndlessSettingsMigration.Values { Version = 1, TreeShadowDistance = 300f, UndergrowthDrawDistance = 250f, ShadowCasterCulling = true, WalkReuse = true };
            var changes = new List<string>();
            Assert.That(EndlessSettingsMigration.Upgrade(ref v, changes), Is.True);
            Assert.That(v.Version, Is.EqualTo(3));
            Assert.That(v.ImpostorDistance, Is.EqualTo(320f), "version 2 fills 420 m, version 3 moves the old default to 320 m");
            Assert.That(v.ImpostorMaxPixels, Is.EqualTo(3f));
            Assert.That(changes.Count, Is.EqualTo(3));

            var v2 = new EndlessSettingsMigration.Values { Version = 2, ImpostorDistance = 420f, ImpostorMaxPixels = 3f };
            Assert.That(EndlessSettingsMigration.Upgrade(ref v2, null), Is.True);
            Assert.That(v2.ImpostorDistance, Is.EqualTo(320f));
            var v2Tuned = new EndlessSettingsMigration.Values { Version = 2, ImpostorDistance = 380f, ImpostorMaxPixels = 3f };
            EndlessSettingsMigration.Upgrade(ref v2Tuned, null);
            Assert.That(v2Tuned.ImpostorDistance, Is.EqualTo(380f), "a hand-tuned distance survives version 3");

            var tuned = new EndlessSettingsMigration.Values { Version = 1, TreeShadowDistance = 300f, UndergrowthDrawDistance = 250f, ImpostorDistance = 500f, ImpostorMaxPixels = 1.5f };
            EndlessSettingsMigration.Upgrade(ref tuned, null);
            Assert.That(tuned.ImpostorDistance, Is.EqualTo(500f), "a hand-tuned value survives");
            Assert.That(tuned.ImpostorMaxPixels, Is.EqualTo(1.5f));
            Assert.That(EndlessSettingsMigration.Upgrade(ref tuned, null), Is.False, "once only");
        }

        [Test]
        public void MarkerWindowSkipsSilentSlots()
        {
            var w = new MarkerWindow(2, 4);
            for (int i = 1; i <= 6; i++)
            {
                w.Scratch[0] = i;
                w.Scratch[1] = float.NaN;
                w.Add(w.Scratch);
            }

            Assert.That(w.Count, Is.EqualTo(4));
            Assert.That(w.Mean(0), Is.EqualTo(4.5f).Within(1e-4f), "the last four: 3..6");
            Assert.That(w.Mean(0, 2), Is.EqualTo(5.5f).Within(1e-4f));
            Assert.That(float.IsNaN(w.Mean(1)), Is.True, "a marker that reported nothing stays n/a");
            w.Reset();
            Assert.That(float.IsNaN(w.Mean(0)), Is.True);
        }
    }
}
