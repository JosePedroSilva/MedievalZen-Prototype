using System;
using MedievalZen.Environment;
using MedievalZen.UI;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>The "Rich light" look (LightLookMath): neutral when off, unchanged lights at night, exposure kept by day.</summary>
    public sealed class LightLookTests
    {
        private static float[] Elevations()
        {
            var e = new float[201];
            for (int i = 0; i < e.Length; i++) e[i] = -30f + i * 0.5f;   // −30° … 70°
            return e;
        }

        [Test]
        public void OffIsExactlyTheOldLook()
        {
            foreach (float elevation in Elevations())
            {
                foreach (float overcast in new[] { 0f, 0.5f, 1f })
                {
                    RichLighting l = LightLookMath.Lighting(0f, elevation, overcast);
                    Assert.That(l.SunScale, Is.EqualTo(1f));
                    AssertTone(l.AmbientSky, 1f, 1f, 1f);
                    AssertTone(l.AmbientEquator, 1f, 1f, 1f);
                    AssertTone(l.AmbientGround, 1f, 1f, 1f);

                    RichGrade g = LightLookMath.Grade(0f, elevation, overcast, 0.7f);
                    Assert.That(g.Contrast, Is.EqualTo(0f));
                    Assert.That(g.Saturation, Is.EqualTo(0f));
                    Assert.That(g.Temperature, Is.EqualTo(0f));
                    Assert.That(g.BloomIntensity, Is.EqualTo(0f));
                    Assert.That(g.BloomThresholdScale, Is.EqualTo(1f));
                    AssertTone(g.Shadows, 0.5f, 0.5f, 0.5f);
                    AssertTone(g.Highlights, 0.5f, 0.5f, 0.5f);
                }
            }
        }

        [Test]
        public void NightLightsAreUnchangedAndTheGradeBarelyMoves()
        {
            foreach (float elevation in new[] { -12f, -20f, -45f })
            {
                RichLighting l = LightLookMath.Lighting(1f, elevation, 0f);
                Assert.That(l.SunScale, Is.EqualTo(1f).Within(1e-6f));
                AssertTone(l.AmbientSky, 1f, 1f, 1f);
                AssertTone(l.AmbientEquator, 1f, 1f, 1f);
                AssertTone(l.AmbientGround, 1f, 1f, 1f);
                RichGrade g = LightLookMath.Grade(1f, elevation, 0f, 0f);
                Assert.That(g.Contrast, Is.LessThanOrEqualTo(2f));
                Assert.That(g.Saturation, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(g.BloomIntensity, Is.EqualTo(0f).Within(1e-6f));
                AssertTone(g.Highlights, 0.5f, 0.5f, 0.5f);
            }
        }

        [Test]
        public void DaytimeExposureOfSunlitGroundStaysWithinAFewPercent()
        {
            // From the low golden-hour sun to noon: the deeper shade is paid for by the stronger sun, so flat sunlit ground
            // (what fills most of the view) stays within a few percent of the old exposure.
            for (float elevation = 1f; elevation <= 58f; elevation += 1f)
            {
                float ratio = LightLookMath.LitGroundRatio(elevation);
                Assert.That(ratio, Is.InRange(0.97f, 1.04f), $"at {elevation}°");
            }
        }

        [Test]
        public void ShadeIsDeeperAndCoolerByDay()
        {
            RichLighting noon = LightLookMath.Lighting(1f, 58f, 0f);
            Assert.That(noon.SunScale, Is.EqualTo(1f + LightLookMath.DaySunGain).Within(1e-4f));
            Assert.That(noon.AmbientSky.Luma, Is.InRange(0.85f, 0.93f), "about 10 % less sky light in the shade");
            Assert.That(noon.AmbientSky.B, Is.GreaterThan(noon.AmbientSky.R), "the shade turns cooler");
            Assert.That(noon.AmbientGround.R, Is.GreaterThan(noon.AmbientGround.B), "the ground bounce stays warm");

            RichGrade g = LightLookMath.Grade(1f, 58f, 0f, 0f);
            Assert.That(g.Contrast, Is.InRange(6f, 9f));
            Assert.That(g.Saturation, Is.InRange(5f, 8f));
            Assert.That(g.Shadows.B, Is.GreaterThan(g.Shadows.R), "cool shadows");
            Assert.That(g.Highlights.R, Is.GreaterThan(g.Highlights.B), "warm highlights");
            Assert.That(Math.Abs(g.Shadows.G - 0.5f), Is.LessThan(0.03f), "subtle: within a few percent of neutral");
        }

        [Test]
        public void GoldenHoursGlowMoreThanNoon()
        {
            RichGrade golden = LightLookMath.Grade(1f, 6f, 0f, 0f);
            RichGrade noon = LightLookMath.Grade(1f, 58f, 0f, 0f);
            Assert.That(golden.Temperature, Is.GreaterThan(noon.Temperature));
            Assert.That(golden.BloomIntensity, Is.GreaterThan(noon.BloomIntensity));
            Assert.That(golden.BloomThresholdScale, Is.LessThan(1f));
            Assert.That(golden.Highlights.R - golden.Highlights.B, Is.GreaterThan(noon.Highlights.R - noon.Highlights.B));
            Assert.That(LightLookMath.Lighting(1f, 6f, 0f).SunScale, Is.GreaterThan(LightLookMath.Lighting(1f, 58f, 0f).SunScale));

            RichGrade grey = LightLookMath.Grade(1f, 6f, 1f, 0f);
            Assert.That(grey.Temperature, Is.LessThan(golden.Temperature), "overcast mutes the golden extras");
        }

        [Test]
        public void EverythingIsContinuousThroughDawnAndDusk()
        {
            RichLighting previous = LightLookMath.Lighting(1f, -30f, 0.3f);
            RichGrade previousGrade = LightLookMath.Grade(1f, -30f, 0.3f, 0.2f);
            for (float e = -30f; e <= 70f; e += 0.1f)
            {
                RichLighting l = LightLookMath.Lighting(1f, e, 0.3f);
                RichGrade g = LightLookMath.Grade(1f, e, 0.3f, 0.2f);
                Assert.That(Math.Abs(l.SunScale - previous.SunScale), Is.LessThan(0.01f), $"sun at {e}°");
                Assert.That(Math.Abs(l.AmbientSky.Luma - previous.AmbientSky.Luma), Is.LessThan(0.01f), $"ambient at {e}°");
                Assert.That(Math.Abs(g.Contrast - previousGrade.Contrast), Is.LessThan(0.2f), $"contrast at {e}°");
                Assert.That(Math.Abs(g.Saturation - previousGrade.Saturation), Is.LessThan(0.2f), $"saturation at {e}°");
                Assert.That(Math.Abs(g.Highlights.R - previousGrade.Highlights.R), Is.LessThan(0.01f), $"highlights at {e}°");
                previous = l;
                previousGrade = g;
            }
        }

        private static void AssertTone(in Tone3 t, float r, float g, float b)
        {
            Assert.That(t.R, Is.EqualTo(r).Within(1e-6f));
            Assert.That(t.G, Is.EqualTo(g).Within(1e-6f));
            Assert.That(t.B, Is.EqualTo(b).Within(1e-6f));
        }
    }

    /// <summary>Foliage wind: only the intensity follows the weather; gentle, bounded, monotonic.</summary>
    public sealed class FoliageWindTests
    {
        [Test]
        public void IntensityIsBoundedAndRisesWithWindAndGusts()
        {
            for (float wind = 0f; wind <= 1f; wind += 0.05f)
            {
                float previous = -1f;
                for (float gust = 0f; gust <= 1f; gust += 0.05f)
                {
                    float i = FoliageWind.Intensity(wind, gust);
                    Assert.That(i, Is.InRange(0.19f, FoliageWind.MaxIntensity));
                    Assert.That(i, Is.GreaterThanOrEqualTo(previous));
                    previous = i;
                }
            }

            for (float gust = 0f; gust <= 1f; gust += 0.25f)
            {
                Assert.That(FoliageWind.Intensity(0.55f, gust), Is.GreaterThan(FoliageWind.Intensity(0.1f, gust)), "rain sways more than mist");
            }
        }

        [Test]
        public void ClearWeatherSwaysMoreThanTheShadersFixedValueButStaysGentle()
        {
            // The average gust of squared Perlin noise is about 0.27.
            float clear = FoliageWind.Intensity(0.3f, 0.27f);
            Assert.That(clear, Is.GreaterThan(FoliageWind.BuiltInIntensity * 1.4f));
            Assert.That(clear, Is.LessThan(0.5f));
            Assert.That(FoliageWind.Gale, Is.EqualTo(0.2f), "gale stays at the shader's own value (it sets the sway speed)");
        }

        [Test]
        public void NaNAndOutOfRangeInputsAreSafe()
        {
            Assert.That(FoliageWind.Intensity(float.NaN, float.NaN), Is.EqualTo(FoliageWind.Intensity(0f, 0f)));
            Assert.That(FoliageWind.Intensity(7f, 7f), Is.EqualTo(FoliageWind.Intensity(1f, 1f)));
            Assert.That(FoliageWind.GustFromNoise(0.5f), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(FoliageWind.GustFromNoise(-1f), Is.EqualTo(0f));
        }
    }

    /// <summary>Birds crossing the sky: wing beats with glides, formations and paths.</summary>
    public sealed class BirdMathTests
    {
        [Test]
        public void WingAngleStaysInThePoseRangeAndGlidesBetweenBeats()
        {
            bool sawGlide = false, sawBeat = false;
            for (double t = 0; t < 30; t += 0.01)
            {
                float a = BirdMath.WingAngle(t, 3.6f, 4f, 0.45f);
                Assert.That(a, Is.InRange(BirdMath.MinAngle - 1e-3f, BirdMath.MaxAngle + 1e-3f));
                float envelope = BirdMath.FlapEnvelope(t, 4f, 0.45f);
                if (envelope == 0f)
                {
                    sawGlide = true;
                    Assert.That(a, Is.EqualTo(BirdMath.GlideAngle).Within(1e-4f));
                }

                if (envelope > 0.99f) sawBeat = true;
            }

            Assert.That(sawGlide && sawBeat, Is.True);
        }

        [Test]
        public void FlapEnvelopeIsContinuous()
        {
            float previous = BirdMath.FlapEnvelope(0, 4f, 0.5f);
            for (double t = 0.005; t < 20; t += 0.005)
            {
                float e = BirdMath.FlapEnvelope(t, 4f, 0.5f);
                Assert.That(Math.Abs(e - previous), Is.LessThan(0.05f), $"at {t:0.000} s");
                previous = e;
            }
        }

        [Test]
        public void PosesCoverTheRangeAndRoundTrip()
        {
            const int poses = 8;
            Assert.That(BirdMath.PoseIndex(BirdMath.MinAngle - 10f, poses), Is.EqualTo(0));
            Assert.That(BirdMath.PoseIndex(BirdMath.MaxAngle + 10f, poses), Is.EqualTo(poses - 1));
            for (int i = 0; i < poses; i++) Assert.That(BirdMath.PoseIndex(BirdMath.PoseAngle(i, poses), poses), Is.EqualTo(i));
        }

        [Test]
        public void VeeHasTheLeaderInFrontAndBothSidesBehind()
        {
            const int count = 7;
            (float lx, float lz) = BirdMath.FormationOffset(0, count, true, 3f, 0f, 0f);
            Assert.That(lx, Is.EqualTo(0f).Within(1e-5f));
            for (int i = 1; i < count; i++)
            {
                (float x, float z) = BirdMath.FormationOffset(i, count, true, 3f, 0f, 0f);
                Assert.That(z, Is.LessThan(lz), "behind the leader");
                Assert.That(Math.Sign(x), Is.EqualTo(i % 2 == 1 ? -1 : 1), "alternating sides");
            }
        }

        [Test]
        public void ClusterIsCompactAndBirdsDoNotOverlap()
        {
            const int count = 9;
            const float spacing = 2.5f;
            var points = new (float x, float z)[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = BirdMath.FormationOffset(i, count, false, spacing, 0f, 0f);
                Assert.That(Math.Sqrt(points[i].x * points[i].x + points[i].z * points[i].z), Is.LessThan(spacing * 3.2f));
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    double d = Math.Sqrt(Math.Pow(points[i].x - points[j].x, 2) + Math.Pow(points[i].z - points[j].z, 2));
                    Assert.That(d, Is.GreaterThan(1.2), $"birds {i} and {j}");
                }
            }
        }

        [Test]
        public void CrossingPathPassesBesideTheTravellerAtTheGivenOffset()
        {
            var (sx, sz, ex, ez) = BirdMath.CrossingPath(10f, -4f, 3f, 4f, 60f, 300f);
            double length = Math.Sqrt((ex - sx) * (ex - sx) + (ez - sz) * (ez - sz));
            Assert.That(length, Is.EqualTo(600.0).Within(1e-3));
            // Distance from the traveller to the infinite line.
            double dx = ex - sx, dz = ez - sz;
            double cross = Math.Abs(dx * (-4f - sz) - dz * (10f - sx)) / length;
            Assert.That(cross, Is.EqualTo(60.0).Within(1e-3));
        }
    }

    /// <summary>Journey settings version 4: the "Rich light" choice.</summary>
    public sealed class JourneySettingsRichLightTests
    {
        [Test]
        public void RichLightIsOnByDefaultRoundTripsAndCountsForSameAs()
        {
            var defaults = new JourneySettings();
            Assert.That(defaults.RichLight, Is.True);
            Assert.That(JourneySettings.CurrentVersion, Is.GreaterThanOrEqualTo(4));   // later versions add sound, pace and season
            var off = new JourneySettings { RichLight = false };
            string json = off.ToJson();
            StringAssert.StartsWith("{\"version\":" + JourneySettings.CurrentVersion + ",", json);
            StringAssert.Contains("\"richLight\":false", json);
            Assert.That(JourneySettings.TryFromJson(json, new JourneySettings(), out JourneySettings read), Is.True);
            Assert.That(read.RichLight, Is.False);
            Assert.That(read.SameAs(off), Is.True);
            Assert.That(defaults.SameAs(off), Is.False);
        }

        [Test]
        public void VersionThreeBlobLoadsWithRichLightOn()
        {
            // Saved before the choice existed: every old field is read, the new one defaults (a stray key is ignored).
            string v3 = "{\"version\":3,\"flow\":\"Slow\",\"weather\":\"Hazy\",\"fog\":1.2,\"haze\":1,\"clouds\":1,\"wind\":true," +
                        "\"windVolume\":0.1,\"camera\":\"ThirdPerson\",\"lookSpeed\":1,\"invertY\":false,\"showHud\":true,\"cinematic\":false," +
                        "\"cinematicTravel\":true,\"letterbox\":true,\"hudAlways\":false,\"animals\":false,\"animalVolume\":0.3,\"richLight\":false}";
            Assert.That(JourneySettings.TryFromJson(v3, new JourneySettings(), out JourneySettings read), Is.True);
            Assert.That(read.Flow, Is.EqualTo(FlowOfTime.Slow));
            Assert.That(read.Weather, Is.EqualTo(WeatherOverride.Hazy));
            Assert.That(read.Animals, Is.False);
            Assert.That(read.AnimalVolume, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(read.RichLight, Is.True, "keys a version-3 blob could not have written are ignored");
            StringAssert.StartsWith("{\"version\":" + JourneySettings.CurrentVersion + ",", read.ToJson(), "re-stamped as the current version on the next save");
        }

        [Test]
        public void NewerVersionIsStillRejected()
        {
            Assert.That(JourneySettings.TryFromJson("{\"version\":" + (JourneySettings.CurrentVersion + 1) + ",\"richLight\":false}", new JourneySettings(), out JourneySettings read), Is.False);
            Assert.That(read.RichLight, Is.True);
        }
    }
}
