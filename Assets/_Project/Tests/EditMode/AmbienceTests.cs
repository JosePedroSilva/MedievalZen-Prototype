using System;
using System.Collections.Generic;
using System.IO;
using MedievalZen.Endless;
using MedievalZen.Sound;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>The ambience's pure rules (<see cref="AmbienceModel"/>, <see cref="AmbienceSmoother"/>) and its catalog.</summary>
    public sealed class AmbienceTests
    {
        private static AmbienceInputs Day(RegionKind region = RegionKind.Meadow, float cover = 0f, float sun = 35f, bool morning = false) => new AmbienceInputs
        {
            Region = region, Cover = cover, Exposure = AmbienceModel.Exposure(region, 0f, 0f, cover), SunElevation = sun, Morning = morning, Wind = 0.3f, Gust = 0.3f,
        };

        private static float[] Targets(AmbienceInputs i)
        {
            var w = new float[AmbienceCatalog.BedCount];
            AmbienceModel.Targets(i, w);
            return w;
        }

        private static float W(float[] w, AmbienceRole r) => w[(int)r];

        private static float Db(float gain) => SoundMix.GainToDb(gain);

        [Test]
        public void BedIndicesMatchTheRoleOrder()
        {
            Assert.AreEqual(AmbienceCatalog.BedCount, AmbienceCatalog.Beds.Length);
            for (int k = 0; k < AmbienceCatalog.Beds.Length; k++) Assert.AreEqual(k, AmbienceCatalog.BedIndex(AmbienceCatalog.Beds[k]));
            Assert.AreEqual(-1, AmbienceCatalog.BedIndex(AmbienceRole.Stream));
        }

        [Test]
        public void EqualPowerKeepsThePowerConstant()
        {
            for (float t = 0f; t <= 1.0001f; t += 0.05f)
            {
                AmbienceModel.EqualPower(t, out float a, out float b);
                Assert.AreEqual(1f, a * a + b * b, 1e-5f, $"t {t}");
            }

            AmbienceModel.EqualPower(0f, out float a0, out float b0);
            Assert.AreEqual(1f, a0, 1e-6f);
            Assert.AreEqual(0f, b0, 1e-6f);
            AmbienceModel.EqualPower(0.5f, out float am, out float bm);
            Assert.AreEqual(Db(am), -3.01f, 0.05f);
            Assert.AreEqual(am, bm, 1e-6f);
        }

        [Test]
        public void CoverCrossfadesOpenAndForestWithEqualPower()
        {
            for (float c = 0f; c <= 1.0001f; c += 0.1f)
            {
                float[] w = Targets(Day(RegionKind.Meadow, c));
                float open = W(w, AmbienceRole.OpenDay), wood = W(w, AmbienceRole.ForestDay);
                Assert.AreEqual(1f, open * open + wood * wood, 0.02f, $"cover {c}");
            }

            Assert.Greater(W(Targets(Day(cover: 0f)), AmbienceRole.OpenDay), 0.99f);
            Assert.Greater(W(Targets(Day(cover: 1f)), AmbienceRole.ForestDay), 0.99f);
        }

        [Test]
        public void DeepForestOnlyAboveThreeQuartersCoverAtAboutMinusEight()
        {
            Assert.AreEqual(0f, W(Targets(Day(RegionKind.Forest, 0.7f)), AmbienceRole.DeepForest));
            Assert.AreEqual(0f, W(Targets(Day(RegionKind.Forest, 0.75f)), AmbienceRole.DeepForest));
            Assert.Greater(W(Targets(Day(RegionKind.Forest, 0.85f)), AmbienceRole.DeepForest), 0f);
            Assert.AreEqual(-8f, Db(W(Targets(Day(RegionKind.Forest, 1f)), AmbienceRole.DeepForest)), 0.5f);
        }

        [Test]
        public void DawnChorusLiftsTheBirdsThreeDecibels()
        {
            float noon = W(Targets(Day(sun: 40f, morning: false)), AmbienceRole.OpenDay);
            float dawn = W(Targets(Day(sun: 5f, morning: true)), AmbienceRole.OpenDay);
            float dusk = W(Targets(Day(sun: 5f, morning: false)), AmbienceRole.OpenDay);
            Assert.AreEqual(3f, Db(dawn) - Db(noon), 0.3f);
            Assert.AreEqual(0f, Db(dusk) - Db(noon), 0.3f, "no chorus at dusk");
            Assert.AreEqual(1f, AmbienceModel.Chorus(-4f, true), 1e-4f);
            Assert.AreEqual(1f, AmbienceModel.Chorus(12f, true), 1e-4f);
            Assert.AreEqual(0f, AmbienceModel.Chorus(25f, true), 1e-4f);
        }

        [Test]
        public void BirdsFadeAtDuskAndTheNightBedTakesOver()
        {
            float[] evening = Targets(Day(sun: 1f));
            float[] night = Targets(Day(sun: -12f));
            Assert.Greater(W(evening, AmbienceRole.OpenDay), 0.5f);
            Assert.AreEqual(0f, W(evening, AmbienceRole.Night), 1e-4f, "no night bed above −2°");
            Assert.AreEqual(0f, W(night, AmbienceRole.OpenDay), 1e-4f);
            Assert.Greater(W(night, AmbienceRole.Night), 0.8f, "full night bed, a little under it where the wind takes over");
            AmbienceInputs sheltered = Day(sun: -12f);
            sheltered.Exposure = 0f;
            Assert.Greater(W(Targets(sheltered), AmbienceRole.Night), 0.99f);
            Assert.Greater(W(Targets(Day(sun: -5f)), AmbienceRole.Night), 0.2f);
        }

        [Test]
        public void RainAndMistThinTheBirds()
        {
            AmbienceInputs dry = Day(), wet = Day(), misty = Day();
            wet.Rain = 1f;
            misty.Mist = 1f;
            Assert.AreEqual(-6f, Db(AmbienceModel.BirdGain(wet)) - Db(AmbienceModel.BirdGain(dry)), 0.05f);
            Assert.AreEqual(-3f, Db(AmbienceModel.BirdGain(misty)) - Db(AmbienceModel.BirdGain(dry)), 0.05f);
            Assert.Greater(W(Targets(wet), AmbienceRole.Rain), 0.5f);
            Assert.AreEqual(0f, W(Targets(dry), AmbienceRole.Rain));
        }

        [Test]
        public void ACalmDayLeavesTheWindNearlyOff()
        {
            AmbienceInputs calm = Day();
            calm.Wind = 0.3f;
            calm.Gust = 0f;
            float[] w = Targets(calm);
            float wind = W(w, AmbienceRole.WindLight) + W(w, AmbienceRole.WindHigh) + W(w, AmbienceRole.WindStrong);
            Assert.Less(Db(wind), -18f, "a calm lowland day: wind at least 18 dB under a bed");

            AmbienceInputs rough = Day();
            rough.Wind = 0.55f;
            rough.Gust = 1f;
            rough.Exposure = 0.9f;
            float[] r = Targets(rough);
            Assert.Greater(W(r, AmbienceRole.WindHigh), 0.5f);
            Assert.Greater(W(r, AmbienceRole.WindStrong), 0.2f);
        }

        [Test]
        public void ExposureClimbsWithRegionAndHeightAndDropsUnderTrees()
        {
            Assert.Less(AmbienceModel.Exposure(RegionKind.Forest, 0f, 0f, 0f), AmbienceModel.Exposure(RegionKind.Downs, 0f, 0f, 0f));
            Assert.Greater(AmbienceModel.Exposure(RegionKind.HighlandPass, 300f, 0f, 0f), 0.9f);
            Assert.Less(AmbienceModel.Exposure(RegionKind.HighlandPass, 300f, 0f, 1f), 0.35f);
        }

        [Test]
        public void NeverSilentAndNeverAWallOfNoise()
        {
            var regions = (RegionKind[])Enum.GetValues(typeof(RegionKind));
            foreach (RegionKind region in regions)
            {
                for (float sun = -20f; sun <= 60f; sun += 1f)
                {
                    foreach (bool morning in new[] { true, false })
                    {
                        for (float cover = 0f; cover <= 1.0001f; cover += 0.25f)
                        {
                            foreach (float rain in new[] { 0f, 1f })
                            {
                                foreach (float wind in new[] { 0.1f, 0.3f, 0.55f })
                                {
                                    var i = new AmbienceInputs
                                    {
                                        Region = region, Cover = cover, SunElevation = sun, Morning = morning, Rain = rain, Wind = wind, Gust = 1f, Mist = 1f,
                                        Exposure = AmbienceModel.Exposure(region, 250f, 0f, cover),
                                    };
                                    float p = AmbienceModel.Power(Targets(i));
                                    Assert.GreaterOrEqual(p, AmbienceModel.MinPower - 1e-4f, $"{region} sun {sun} cover {cover} rain {rain} wind {wind}");
                                    Assert.LessOrEqual(p, AmbienceModel.MaxPower + 1e-4f, $"{region} sun {sun} cover {cover} rain {rain} wind {wind}");
                                }
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void SmootherApproachesWithItsTimeConstantAndHurries()
        {
            var s = new AmbienceSmoother();
            var a = new float[AmbienceCatalog.BedCount];
            var b = new float[AmbienceCatalog.BedCount];
            b[(int)AmbienceRole.OpenDay] = 1f;
            s.Step(a, 0.02f);   // primes on the first step
            Assert.AreEqual(0f, s.Weights[(int)AmbienceRole.OpenDay]);
            float tau = AmbienceModel.TimeConstant((int)AmbienceRole.OpenDay);
            Assert.That(tau, Is.InRange(4f, 8f));
            for (float t = 0f; t < tau - 1e-3f; t += 0.02f) s.Step(b, 0.02f);
            Assert.AreEqual(1f - Math.Exp(-1.0), s.Weights[(int)AmbienceRole.OpenDay], 0.02f, "63 % after one time constant");

            var fast = new AmbienceSmoother();
            fast.Step(a, 0.02f);
            fast.Hurry();
            for (float t = 0f; t < AmbienceModel.FastSeconds - 1e-3f; t += 0.02f) fast.Step(b, 0.02f);
            Assert.AreEqual(1f - Math.Exp(-1.0), fast.Weights[(int)AmbienceRole.OpenDay], 0.02f, "1.5 s after a teleport");

            for (int k = 0; k < AmbienceCatalog.BedCount; k++) Assert.That(AmbienceModel.TimeConstant(k), Is.InRange(4f, 8f));
        }

        [Test]
        public void SpotSoundsFollowTheirGapsAndNeverRepeat()
        {
            Assert.AreEqual(8f, AmbienceModel.SpotMeanGap(1f, 1f, 0f, 0f), 1e-3f);
            Assert.AreEqual(14f, AmbienceModel.SpotMeanGap(1f, 1f, 0f, 1f), 1e-3f);
            Assert.AreEqual(15f, AmbienceModel.SpotMeanGap(0f, 1f, 0f, 0f), 1e-3f);
            Assert.AreEqual(30f, AmbienceModel.SpotMeanGap(0f, 1f, 0f, 1f), 1e-3f);
            Assert.IsTrue(float.IsPositiveInfinity(AmbienceModel.SpotMeanGap(0f, 0f, 0f, 0.5f)), "none at night");
            Assert.IsTrue(float.IsPositiveInfinity(AmbienceModel.SpotMeanGap(0f, 1f, 0.8f, 0.5f)), "none in rain");

            var rng = new Random(7);
            double sum = 0;
            const int n = 20000;
            for (int k = 0; k < n; k++) sum += AmbienceModel.PoissonGap(12f, rng.NextDouble());
            Assert.AreEqual(12.0 + 0.0, sum / n, 0.6, "mean of the exponential gaps (2 s floor adds a little)");

            int last = -1;
            for (int k = 0; k < 500; k++)
            {
                int pick = AmbienceModel.PickOther(8, last, rng);
                Assert.AreNotEqual(last, pick);
                Assert.That(pick, Is.InRange(0, 7));
                last = pick;
            }
        }

        [Test]
        public void RainSynthSitsAtTheBedTargetWithoutNaN()
        {
            foreach (float amount in new[] { 0.3f, 1f })
            {
                var synth = new RainSynth { SampleRate = 48000f };
                var all = new float[48000 * 2 * 20];
                var block = new float[1024 * 2];
                for (int o = 0; o < all.Length; o += block.Length)
                {
                    Array.Clear(block, 0, block.Length);
                    synth.Render(block, 2, 1f, amount);
                    Array.Copy(block, 0, all, o, Math.Min(block.Length, all.Length - o));
                }

                foreach (float v in all) Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
                WavFile.Level(all, 2, 48000 * 10, 48000 * 20, out float rms, out float peak);
                Assert.AreEqual(SoundMix.BedTargetDb, rms, 1.5f, $"rain {amount}");
                Assert.Less(peak, -6f, $"rain {amount}");
            }

            var silent = new RainSynth();
            var quiet = new float[2048];
            silent.Render(quiet, 2, 0f, 1f);
            foreach (float v in quiet) Assert.AreEqual(0f, v);
        }

        [Test]
        public void PeopleMurmurIsDaytimeOnly()
        {
            Assert.AreEqual(0f, AmbienceModel.PeopleDayGain(3f));
            Assert.AreEqual(0f, AmbienceModel.PeopleDayGain(22f));
            Assert.AreEqual(1f, AmbienceModel.PeopleDayGain(13f), 1e-4f);
        }

        [Test]
        public void CatalogIsConsistent()
        {
            var paths = new HashSet<string>();
            foreach (AmbienceClipSpec c in AmbienceCatalog.All)
            {
                Assert.IsTrue(paths.Add(c.Path), $"duplicate {c.Path}");
                Assert.IsTrue(c.Path.EndsWith(".wav"), c.Path);
                Assert.IsFalse(c.Path.Contains("__MACOSX"), c.Path);
                Assert.AreEqual(c.Category == AmbienceCategory.Bed, AmbienceCatalog.IsBed(c.Role), c.Path);
                Assert.LessOrEqual(c.LevelDb, 0f, c.Path);
            }

            foreach (AmbienceRole bed in AmbienceCatalog.Beds)
            {
                if (AmbienceCatalog.HasClips(bed)) Assert.Greater(AmbienceCatalog.Of(bed).Count, 0, $"no clip for {bed}");
            }

            foreach (AmbienceRole role in new[] { AmbienceRole.Stream, AmbienceRole.Lake, AmbienceRole.FarmAnimals, AmbienceRole.Market, AmbienceRole.InnYard, AmbienceRole.Windmill, AmbienceRole.Church, AmbienceRole.Stones, AmbienceRole.SpotBirds, AmbienceRole.SpotPigeon, AmbienceRole.SpotGull })
            {
                Assert.Greater(AmbienceCatalog.Of(role).Count, 0, $"no clip for {role}");
            }
        }

        [Test]
        public void CatalogFilesExistAndMeasure()
        {
            string root = ProjectRoot();
            if (root == null)
            {
                Assert.Ignore("Assets/Sounds is not present");
                return;
            }

            foreach (AmbienceClipSpec c in AmbienceCatalog.All)
            {
                string file = Path.Combine(root, c.AssetPath.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(file), $"missing {c.AssetPath}");
                Assert.IsTrue(AmbienceLevels.Measure(File.ReadAllBytes(file), out AmbienceLevel l), $"unreadable {c.AssetPath}");
                Assert.That(l.RmsDb, Is.InRange(-70f, -3f), c.Name);
                if (c.Category != AmbienceCategory.Spot) Assert.Greater(l.Seconds, 20.0, c.Name);
            }
        }

        private static string ProjectRoot()
        {
            foreach (string start in new[] { System.Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Sounds"))) return dir.FullName;
                    dir = dir.Parent;
                }
            }

            return null;
        }
    }
}
