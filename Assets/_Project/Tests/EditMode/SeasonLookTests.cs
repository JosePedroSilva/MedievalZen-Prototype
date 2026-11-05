using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Environment;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>Seasons pass, colours half (2026-10-06): the seasonal palette, its identity in autumn, near/far agreement, the alpha classes.</summary>
    public class SeasonLookTests
    {
        private static readonly int[] LeafClasses = { SeasonLook.ClassOak, SeasonLook.ClassBirch, SeasonLook.ClassFruit };
        private static readonly string[] Families = { PropRole.TallGrass, PropRole.MedGrass, PropRole.ShortGrass, PropRole.SunflowerDetail };

        private static IEnumerable<float> Years()
        {
            for (int i = 0; i <= 300; i++) yield return i * 0.01f;
        }

        private static float SrgbByte(float linear)
        {
            float c = Mathf.Clamp01(linear);
            float s = c <= 0.0031308f ? 12.92f * c : 1.055f * (float)Math.Pow(c, 1.0 / 2.4) - 0.055f;
            return s * 255f;
        }

        private static float Saturation(Vector3 linear)
        {
            float r = SrgbByte(linear.x), g = SrgbByte(linear.y), b = SrgbByte(linear.z);
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            return max > 0f ? (max - min) / max : 0f;
        }

        private static Vector3 FarLeafMean(string prefab)
        {
            Assert.That(FarTreeShapes.TryGet(prefab, out FarTreeStyle style), Is.True, prefab);
            return (style.Color + style.Color2) * 0.5f;
        }

        private static string PrefabOf(int cls) => cls == SeasonLook.ClassBirch ? "SM_Env_Tree_Birch_01" : cls == SeasonLook.ClassOak ? "SM_Env_Tree_Meadow_01" : "SM_Env_Tree_Fruit_01";

        [Test]
        public void AutumnIsExactlyTodayForEveryFigure([Values(2f, 5f, -1f)] float year)
        {
            for (int ch = 0; ch < TerrainShaper.EndlessLayerCount; ch++)
            {
                Assert.That(SeasonLook.LayerFactor(ch, year) == Vector3.one && SeasonLook.LayerFactor(ch, year).Equals(Vector3.one), $"layer {ch}");
                Assert.That(SeasonLook.LayerColor(ch, year).Equals(GroundLook.LayerColor(ch)), $"layer colour {ch}");
            }

            foreach (string f in Families) Assert.That(SeasonLook.SwardFactor(f, year).Equals(Vector3.one), f);
            for (int cls = 0; cls < SeasonLook.ClassCount; cls++)
            {
                Assert.That(SeasonLook.ClassTint(cls, year).Equals(Vector3.one), $"far tint {cls}");
                Assert.That(SeasonLook.LeafFactor(cls, year).Equals(Vector3.one), $"leaf factor {cls}");
            }

            var tints = new Vector4[SeasonLook.ClassCount];
            SeasonLook.ClassTints(year, tints);
            foreach (Vector4 t in tints) Assert.That(t.Equals(new Vector4(1f, 1f, 1f, 1f)));
            // Today's material colours (linear) come back bit for bit.
            var birchBase = new Vector3(0.5299f, 0.3880f, 0f);
            foreach (int cls in LeafClasses) Assert.That(SeasonLook.LeafColour(cls, birchBase, year).Equals(birchBase), $"leaf colour {cls}");
        }

        [Test]
        public void EveryFactorStaysInASaneRangeAllYear()
        {
            foreach (float y in Years())
            {
                for (int cls = 0; cls < SeasonLook.ClassCount; cls++)
                {
                    Vector3 t = SeasonLook.ClassTint(cls, y);
                    for (int k = 0; k < 3; k++) Assert.That(t[k], Is.InRange(0.25f, 2.5f), $"class {cls} at {y}");
                }

                for (int ch = 0; ch < TerrainShaper.EndlessLayerCount; ch++)
                {
                    Vector3 c = SeasonLook.LayerColor(ch, y);
                    for (int k = 0; k < 3; k++) Assert.That(c[k], Is.InRange(0f, 1f), $"layer {ch} at {y}");
                    // Believable: no channel much more saturated than today's (CLAUDE: no neon), no jump in lightness.
                    Vector3 today = GroundLook.LayerColor(ch);
                    Assert.That(Saturation(c), Is.LessThanOrEqualTo(Math.Max(0.86f, Saturation(today) + 0.03f)), $"layer {ch} at {y}");
                    Assert.That(Math.Abs(GroundLook.Lab(c).x - GroundLook.Lab(today).x), Is.LessThan(12f), $"layer {ch} at {y}");
                }

                foreach (int cls in LeafClasses)
                {
                    Vector3 near = SeasonLook.NearLeaf(cls, y), today = SeasonLook.NearLeafMean(cls);
                    for (int k = 0; k < 3; k++) Assert.That(near[k], Is.InRange(0f, 1f));
                    Assert.That(Saturation(near), Is.LessThanOrEqualTo(Math.Max(0.86f, Saturation(today) + 0.03f)), $"leaves {cls} at {y}");
                }
            }
        }

        [Test]
        public void NearAndDistantLeavesStayAsCloseAsTodayInEverySeason()
        {
            foreach (int cls in LeafClasses)
            {
                Vector3 far = FarLeafMean(PrefabOf(cls));
                float today = GroundLook.DeltaE(SeasonLook.NearLeafMean(cls), far);
                foreach (float y in Years())
                {
                    float now = GroundLook.DeltaE(SeasonLook.NearLeaf(cls, y), Vector3.Scale(far, SeasonLook.ClassTint(cls, y)));
                    Assert.That(now, Is.LessThanOrEqualTo(today + 8f), $"class {cls} at {y}: ΔE near/far {now:0.0} (today {today:0.0})");
                }
            }
        }

        [Test]
        public void TheGroundsFarTintIsTheNearLayersFactor()
        {
            foreach (float y in Years())
            {
                Assert.That(SeasonLook.ClassTint(SeasonLook.ClassLush, y), Is.EqualTo(SeasonLook.LayerFactor(TerrainShaper.LushGrassLayer, y)));
                Assert.That(SeasonLook.ClassTint(SeasonLook.ClassDry, y), Is.EqualTo(SeasonLook.LayerFactor(TerrainShaper.DryGrassLayer, y)));
                Assert.That(SeasonLook.ClassTint(SeasonLook.ClassFlower, y), Is.EqualTo(SeasonLook.LayerFactor(TerrainShaper.FlowerLayer, y)));
                Assert.That(SeasonLook.ClassTint(SeasonLook.ClassHeath, y), Is.EqualTo(SeasonLook.LayerFactor(TerrainShaper.HeathLayer, y)));
                Assert.That(SeasonLook.SwardFactor(PropRole.TallGrass, y), Is.EqualTo(SeasonLook.LayerFactor(TerrainShaper.LushGrassLayer, y)));
                Assert.That(SeasonLook.SwardFactor(PropRole.ShortGrass, y), Is.EqualTo(SeasonLook.LayerFactor(TerrainShaper.DryGrassLayer, y)));
            }

            foreach (int ch in new[] { TerrainShaper.RockLayer, TerrainShaper.DirtLayer, TerrainShaper.MudLayer, TerrainShaper.CobbleLayer, TerrainShaper.ScreeLayer, TerrainShaper.MountainRockLayer })
            {
                Assert.That(SeasonLook.LayerFactor(ch, Season.Spring).Equals(Vector3.one) && SeasonLook.LayerFactor(ch, Season.Summer).Equals(Vector3.one), $"channel {ch} never changes");
            }
        }

        [Test]
        public void TheSeasonsLookTheWayTheyShould()
        {
            // Spring: young grass greener and lighter than autumn; summer dry grass strawier; the litter mossier (less red) in spring and summer.
            Vector3 lushA = GroundLook.LayerColor(TerrainShaper.LushGrassLayer), lushS = SeasonLook.LayerColor(TerrainShaper.LushGrassLayer, Season.Spring);
            Assert.That(GroundLook.Lab(lushS).x, Is.GreaterThan(GroundLook.Lab(lushA).x + 2f));
            Vector3 dryA = GroundLook.LayerColor(TerrainShaper.DryGrassLayer), dryU = SeasonLook.LayerColor(TerrainShaper.DryGrassLayer, Season.Summer);
            Assert.That(dryU.x / dryU.y, Is.GreaterThan(dryA.x / dryA.y));
            Vector3 litA = GroundLook.LayerColor(TerrainShaper.LeafLitterLayer);
            foreach (float y in new[] { Season.Spring, Season.Summer })
            {
                Vector3 lit = SeasonLook.LayerColor(TerrainShaper.LeafLitterLayer, y);
                Assert.That(lit.x / lit.y, Is.LessThan(litA.x / litA.y * 0.75f), "litter greener");
            }

            // Birch green in spring and summer (green over red), orange today.
            Assert.That(SeasonLook.NearLeafMean(SeasonLook.ClassBirch).x, Is.GreaterThan(SeasonLook.NearLeafMean(SeasonLook.ClassBirch).y));
            foreach (float y in new[] { Season.Spring, Season.Summer })
            {
                Vector3 b = SeasonLook.NearLeaf(SeasonLook.ClassBirch, y);
                Assert.That(b.y, Is.GreaterThan(b.x * 1.3f), $"birch green at {y}");
            }

            // Oak lighter in spring, deeper (not lighter) in summer.
            float oakL = GroundLook.Lab(SeasonLook.NearLeafMean(SeasonLook.ClassOak)).x;
            Assert.That(GroundLook.Lab(SeasonLook.NearLeaf(SeasonLook.ClassOak, Season.Spring)).x, Is.GreaterThan(oakL + 5f));
            Assert.That(GroundLook.Lab(SeasonLook.NearLeaf(SeasonLook.ClassOak, Season.Summer)).x, Is.LessThan(oakL + 3f));
        }

        [Test]
        public void FlatLeafRecolourKeepsTheMeanOnTheSeasonsColour()
        {
            foreach (int cls in new[] { SeasonLook.ClassBirch, SeasonLook.ClassFruit })
            {
                Vector3 mean = SeasonLook.NearLeafMean(cls);
                foreach (float y in Years())
                {
                    Vector3 got = SeasonLook.LeafColour(cls, mean, y), want = SeasonLook.NearLeaf(cls, y);
                    Assert.That((got - want).magnitude, Is.LessThan(2e-4f), $"class {cls} at {y}");
                    // Darker and lighter colours of the noise keep their luminance share.
                    Vector3 dark = mean * 0.5f;
                    Assert.That(SeasonLook.Luminance(SeasonLook.LeafColour(cls, dark, y)), Is.EqualTo(SeasonLook.Luminance(got) * 0.5f).Within(1e-4f));
                }
            }
        }

        [Test]
        public void AlphaClassesRoundTripAndKeepTheFoliageMeaning()
        {
            Assert.That(SeasonLook.Alpha(true, 0), Is.EqualTo(TreeLobes.LeafCrownAlpha), "an unclassed crown is the old alpha 0");
            Assert.That(SeasonLook.Alpha(false, 0), Is.EqualTo(255), "an unclassed face is the old alpha 255");
            for (int cls = 0; cls < SeasonLook.ClassCount; cls++)
            {
                foreach (bool foliage in new[] { true, false })
                {
                    byte a = SeasonLook.Alpha(foliage, cls);
                    Assert.That(SeasonLook.IsFoliage(a), Is.EqualTo(foliage));
                    Assert.That(SeasonLook.ClassOf(a), Is.EqualTo(cls));
                    // The shader decodes alpha / 255 in half precision: ±0.5 of a level still lands in the class.
                    foreach (float wobble in new[] { -0.5f, 0.5f })
                    {
                        float k = foliage ? a + wobble : 255f - (a + wobble);
                        Assert.That((int)Math.Floor(k / 8f + 0.5f), Is.EqualTo(cls));
                        Assert.That(a + wobble < 127.5f, Is.EqualTo(foliage));
                    }
                }
            }
        }

        [Test]
        public void ImpostorLeavesCarryTheirSpeciesClassUnderEitherProvokingConvention()
        {
            foreach (string prefab in new[] { "SM_Env_Tree_Birch_01", "SM_Env_Tree_Meadow_01", "SM_Env_Tree_Fruit_02", "SM_Gen_Env_Tree_01", "SM_Gen_Env_Tree_Pine_01" })
            {
                Assert.That(FarTreeShapes.TryGet(prefab, out FarTreeStyle style), Is.True);
                int expected = SeasonLook.LeafClassOfTree(prefab);
                var v = new List<Vector3>();
                var t = new List<int>();
                var c = new List<Color32>();
                TreeImpostor.Build(style, v, t, c);
                int leaf = 0;
                for (int i = 0; i < t.Count; i += 3)
                {
                    Assert.That(c[t[i]], Is.EqualTo(c[t[i + 2]]), $"{prefab}: triangle {i / 3} starts and ends on one colour (alpha included)");
                    byte a = c[t[i]].a;
                    if (a == 255) continue;
                    leaf++;
                    Assert.That(SeasonLook.ClassOf(a), Is.EqualTo(expected), prefab);
                    Assert.That(SeasonLook.IsFoliage(a), Is.EqualTo(style.LeafCrown), prefab);
                }

                if (expected != 0) Assert.That(leaf, Is.GreaterThan(0), $"{prefab}: leaves carry class {expected}");
                else Assert.That(leaf, Is.EqualTo(0), $"{prefab}: Generic broadleaves and pines are unchanged");
            }
        }

        [Test]
        public void MaterialsAndTreesMapToTheirSpecies()
        {
            Assert.That(SeasonLook.LeafClassOfMaterial("Tree_Birch_Mat_01"), Is.EqualTo(SeasonLook.ClassBirch));
            Assert.That(SeasonLook.LeafClassOfMaterial("Tree_Mat_03"), Is.EqualTo(SeasonLook.ClassOak));
            Assert.That(SeasonLook.LeafClassOfMaterial("Tree_Mat_01"), Is.EqualTo(SeasonLook.ClassFruit));
            Assert.That(SeasonLook.LeafClassOfMaterial("Tree_Mat_01_Small"), Is.EqualTo(SeasonLook.ClassNone));
            Assert.That(SeasonLook.LeafClassOfMaterial("Branches_01"), Is.EqualTo(SeasonLook.ClassNone));
            Assert.That(SeasonLook.LeafClassOfMaterial("Generic_01_A"), Is.EqualTo(SeasonLook.ClassNone));
            Assert.That(SeasonLook.LeafClassOfTree("SM_Env_Tree_Birch_02 (palette)"), Is.EqualTo(SeasonLook.ClassBirch));
            Assert.That(SeasonLook.LeafClassOfTree("SM_Gen_Env_Tree_Dead_01"), Is.EqualTo(SeasonLook.ClassNone));
        }

        [Test]
        public void ThePainterRepaintsOnlyWhenTheRevisionMoves()
        {
            var gate = new SeasonGate();
            Assert.That(gate.Due(0, 0), Is.True, "first frame paints");
            Assert.That(gate.Due(0, 0), Is.False);
            Assert.That(gate.Due(0, 0), Is.False);
            Assert.That(gate.Due(1, 0), Is.True, "the revision moved");
            Assert.That(gate.Due(1, 0), Is.False);
            Assert.That(gate.Due(1, 3), Is.True, "new copies registered");
            Assert.That(gate.Due(1, 3), Is.False);
            // The year moving by less than a step does not move the revision.
            Season.Set(Season.Identity);
            int r = Season.Revision;
            Season.Set(Season.Identity + Season.Step * 0.4f);
            Assert.That(Season.Revision, Is.EqualTo(r));
            Season.Set(Season.Identity + Season.Step * 1.5f);
            Assert.That(Season.Revision, Is.EqualTo(r + 1), "a full step moves it");
            Season.Set(Season.Identity);
        }
    }
}
