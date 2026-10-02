using System;
using MedievalZen.Environment;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// Pure tests of the cloud colour model, the sky-layer fog and the built-in fog split. No Gradient / AnimationCurve /
    /// MonoBehaviour is touched, so they also run outside the editor. <see cref="CloudTestSky"/> mirrors the DayCycle
    /// default gradients and overcast blend.
    /// </summary>
    public sealed class CloudColorModelTests
    {
        private static readonly WeatherState[] AllStates =
        {
            WeatherState.Clear, WeatherState.Hazy, WeatherState.Misty, WeatherState.Overcast, WeatherState.LightRain,
        };

        private static readonly float[] Hazes = { 0f, 0.35f, 0.7f, 1f };

        private const float Tolerance = 1e-4f;

        // Overcast also changes the sky and fog colours themselves (DayCycle greys them), so the fog target moves a little
        // while the clouds are pulled toward it: allow a step of 0.005 (under one 8-bit level per channel).
        private const float DistanceTolerance = 0.005f;

        [Test]
        public void Saturation_NeverExceedsTheCap_ForAllHoursAndWeathers()
        {
            foreach (float cap in new[] { 0f, 0.15f, 0.3f, 0.6f })
            {
                var look = CloudLookSettings.Default;
                look.saturation = cap;
                for (float hour = 0f; hour < 24f; hour += 0.25f)
                {
                    foreach (WeatherState state in AllStates)
                    {
                        foreach (float haze in Hazes)
                        {
                            CloudColors c = CloudColorModel.Evaluate(CloudTestSky.Input(hour, state, haze), look);
                            string at = $"cap {cap} hour {hour} {state} haze {haze}";
                            Assert.That(CloudColorModel.Saturation(c.baseColor), Is.LessThanOrEqualTo(cap + Tolerance), "base " + at);
                            Assert.That(CloudColorModel.Saturation(c.topColor), Is.LessThanOrEqualTo(cap + Tolerance), "top " + at);
                            Assert.That(CloudColorModel.Saturation(c.fresnelColor), Is.LessThanOrEqualTo(cap + Tolerance), "fresnel " + at);
                            Assert.That(CloudColorModel.Saturation(c.scatteringColor), Is.LessThanOrEqualTo(cap + Tolerance), "scattering " + at);
                        }
                    }
                }
            }
        }

        [Test]
        public void Saturation_IsCappedEvenForGaudyInputs()
        {
            var rng = new System.Random(4321);
            for (int i = 0; i < 2000; i++)
            {
                var input = new CloudLightInput
                {
                    skyHorizon = RandomColor(rng),
                    skyZenith = RandomColor(rng),
                    fogColor = RandomColor(rng),
                    sunColor = RandomColor(rng),
                    moonColor = RandomColor(rng),
                    sunElevation = (float)(rng.NextDouble() * 140.0 - 70.0),
                    overcast = (float)rng.NextDouble(),
                    mist = (float)rng.NextDouble(),
                    tint = RandomColor(rng),
                };
                CloudColors c = CloudColorModel.Evaluate(input, CloudLookSettings.Default);
                Assert.That(CloudColorModel.Saturation(c.baseColor), Is.LessThanOrEqualTo(0.3f + Tolerance), $"base, sample {i}");
                Assert.That(CloudColorModel.Saturation(c.topColor), Is.LessThanOrEqualTo(0.3f + Tolerance), $"top, sample {i}");
                AssertInRange(c.baseColor, $"base, sample {i}");
                AssertInRange(c.topColor, $"top, sample {i}");
            }
        }

        [Test]
        public void LitSide_IsNeverDarkerThanTheShadedSide()
        {
            for (float hour = 0f; hour < 24f; hour += 0.25f)
            {
                foreach (WeatherState state in AllStates)
                {
                    foreach (float haze in Hazes)
                    {
                        CloudColors c = CloudColorModel.Evaluate(CloudTestSky.Input(hour, state, haze), CloudLookSettings.Default);
                        Assert.That(CloudColorModel.Luminance(c.topColor), Is.GreaterThanOrEqualTo(CloudColorModel.Luminance(c.baseColor) - Tolerance),
                            $"hour {hour} {state} haze {haze}");
                    }
                }
            }
        }

        [Test]
        public void Overcast_MonotonicallyLowersContrast_AndPullsTowardTheFog()
        {
            for (float hour = 0f; hour < 24f; hour += 0.5f)
            {
                float lastContrast = float.MaxValue;
                float lastTop = float.MaxValue;
                float lastBase = float.MaxValue;
                for (float overcast = 0f; overcast <= 1.0001f; overcast += 0.05f)
                {
                    CloudLightInput input = CloudTestSky.Input(hour, overcast, 0.3f, Color.white);
                    CloudColors c = CloudColorModel.Evaluate(input, CloudLookSettings.Default);
                    float contrast = Contrast(c);
                    float top = Distance(c.topColor, Attractor(c, input.tint));
                    float bottom = Distance(c.baseColor, Attractor(c, input.tint));
                    string at = $"hour {hour} overcast {overcast:0.00}";
                    Assert.That(contrast, Is.LessThanOrEqualTo(lastContrast + Tolerance), "contrast " + at);
                    Assert.That(top, Is.LessThanOrEqualTo(lastTop + DistanceTolerance), "top to fog " + at);
                    Assert.That(bottom, Is.LessThanOrEqualTo(lastBase + DistanceTolerance), "base to fog " + at);
                    lastContrast = contrast;
                    lastTop = top;
                    lastBase = bottom;
                }
            }
        }

        [Test]
        public void Mist_MonotonicallyLowersContrast_AndPullsTowardTheFog()
        {
            for (float hour = 0f; hour < 24f; hour += 0.5f)
            {
                foreach (WeatherState state in AllStates)
                {
                    float lastContrast = float.MaxValue;
                    float lastTop = float.MaxValue;
                    float lastBase = float.MaxValue;
                    for (float mist = 0f; mist <= 1.0001f; mist += 0.05f)
                    {
                        CloudLightInput input = CloudTestSky.Input(hour, state, mist);
                        CloudColors c = CloudColorModel.Evaluate(input, CloudLookSettings.Default);
                        Color attractor = Attractor(c, input.tint);
                        string at = $"hour {hour} {state} mist {mist:0.00}";
                        Assert.That(Contrast(c), Is.LessThanOrEqualTo(lastContrast + Tolerance), "contrast " + at);
                        Assert.That(Distance(c.topColor, attractor), Is.LessThanOrEqualTo(lastTop + Tolerance), "top to fog " + at);
                        Assert.That(Distance(c.baseColor, attractor), Is.LessThanOrEqualTo(lastBase + Tolerance), "base to fog " + at);
                        lastContrast = Contrast(c);
                        lastTop = Distance(c.topColor, attractor);
                        lastBase = Distance(c.baseColor, attractor);
                    }
                }
            }
        }

        [Test]
        public void Softness_ZeroIsCrispAndTwoIsSofter()
        {
            CloudLightInput input = CloudTestSky.Input(8f, WeatherState.Misty, 0.8f);
            var crisp = CloudLookSettings.Default;
            crisp.softness = 0f;
            var soft = CloudLookSettings.Default;
            soft.softness = 2f;
            float c0 = Contrast(CloudColorModel.Evaluate(input, crisp));
            float c1 = Contrast(CloudColorModel.Evaluate(input, CloudLookSettings.Default));
            float c2 = Contrast(CloudColorModel.Evaluate(input, soft));
            Assert.That(CloudColorModel.Evaluate(input, crisp).fogWeight, Is.EqualTo(0f));
            Assert.That(c0, Is.GreaterThan(c1));
            Assert.That(c1, Is.GreaterThan(c2));
        }

        [Test]
        public void Night_StaysDimButVisible()
        {
            foreach (float hour in new[] { 21.5f, 22f, 23f, 0f, 1f, 2f, 3f, 4f })
            {
                foreach (WeatherState state in AllStates)
                {
                    foreach (float haze in Hazes)
                    {
                        CloudColors c = CloudColorModel.Evaluate(CloudTestSky.Input(hour, state, haze), CloudLookSettings.Default);
                        string at = $"hour {hour} {state} haze {haze}";
                        Assert.That(c.topColor.maxColorComponent, Is.InRange(0.04f, 0.3f), "top " + at);
                        Assert.That(c.baseColor.maxColorComponent, Is.InRange(0.03f, 0.3f), "base " + at);
                        Assert.That(c.topColor.b, Is.GreaterThanOrEqualTo(c.topColor.r - Tolerance), "night top is blue-grey " + at);
                    }
                }
            }
        }

        [Test]
        public void ClearNoon_IsBrightNearWhiteWithSofterShading()
        {
            CloudColors c = CloudColorModel.Evaluate(CloudTestSky.Input(12f, WeatherState.Clear, 0.1f), CloudLookSettings.Default);
            Assert.That(CloudColorModel.Luminance(c.topColor), Is.GreaterThan(0.8f));
            Assert.That(CloudColorModel.Saturation(c.topColor), Is.LessThan(0.08f), "lit side near white");
            Assert.That(c.baseColor.b, Is.GreaterThan(c.baseColor.r), "shaded side blue-grey");
            Assert.That(Contrast(c), Is.InRange(0.1f, 0.4f));
        }

        [Test]
        public void GoldenHour_WarmsTheLitSideOnly()
        {
            CloudColors dawn = CloudColorModel.Evaluate(CloudTestSky.Input(6.4f, WeatherState.Clear, 0.3f), CloudLookSettings.Default);
            CloudColors noon = CloudColorModel.Evaluate(CloudTestSky.Input(12f, WeatherState.Clear, 0.3f), CloudLookSettings.Default);
            float warmDawn = dawn.topColor.r - dawn.topColor.b;
            float warmNoon = noon.topColor.r - noon.topColor.b;
            Assert.That(warmDawn, Is.GreaterThan(warmNoon + 0.08f));
            Assert.That(warmNoon, Is.LessThan(0.05f));
        }

        // ------------------------------------------------------------------ sky layer fog and the built-in fog split

        [Test]
        public void SkyFog_ZenithStaysClear_AndFadesWithElevation()
        {
            foreach (float haze in Hazes)
            {
                float tail = 0.8f * 2f * (0.15f + 0.6f * haze);
                float tailSin = (float)Math.Sin(Math.Min(60f, 22f + 28f * haze) * Math.PI / 180.0);
                Assert.That(AtmosphereMath.SkyFog(1f, 0.92f, 0.24f, tail, tailSin), Is.EqualTo(0f).Within(1e-6f), "zenith");
                Assert.That(AtmosphereMath.SkyFog(0.87f, 0.92f, 0.24f, tail, tailSin), Is.EqualTo(0f).Within(1e-6f), "60 degrees");
                float last = float.MaxValue;
                for (float y = -0.2f; y <= 1f; y += 0.01f)
                {
                    float f = AtmosphereMath.SkyFog(y, 0.92f, 0.24f, tail, tailSin);
                    Assert.That(f, Is.InRange(0f, 1f));
                    Assert.That(f, Is.LessThanOrEqualTo(last + 1e-6f), $"elevation sine {y}");
                    last = f;
                }
            }
        }

        [Test]
        public void SkyFog_HazeTailReachesHigherThanTheOldBand()
        {
            float sin20 = (float)Math.Sin(20.0 * Math.PI / 180.0);
            float sin14 = (float)Math.Sin(14.0 * Math.PI / 180.0);
            float clearAir = AtmosphereMath.SkyFog(sin20, 0.8f, sin14, 0.8f * 0.15f, (float)Math.Sin(22.0 * Math.PI / 180.0));
            float mist = AtmosphereMath.SkyFog(sin20, 0.8f, sin14, 0.8f * 0.75f, (float)Math.Sin(50.0 * Math.PI / 180.0));
            Assert.That(clearAir, Is.LessThan(0.01f), "clear air: 20 degrees almost untouched");
            Assert.That(mist, Is.GreaterThan(0.15f), "mist: low clouds at 20 degrees dissolve into the haze");
        }

        [Test]
        public void CloudHaze_GrowsWithTheAerialHaze()
        {
            float last = -1f;
            foreach (float half in new[] { 5000f, 2100f, 1300f, 800f, 520f, 350f, 150f })
            {
                var fog = new FogSettings { hazeDensity = AtmosphereMath.HazeDensityForHalfDistance(half) };
                float haze = AtmosphereMath.CloudHaze(fog, 0f);
                Assert.That(haze, Is.GreaterThan(last), $"half distance {half}");
                Assert.That(haze, Is.InRange(0f, 1f));
                last = haze;
            }

            Assert.That(AtmosphereMath.CloudHaze(new FogSettings(), 0f), Is.EqualTo(0f), "no fog, no haze");
            Assert.That(AtmosphereMath.CloudHaze(new FogSettings(), 1f), Is.EqualTo(0.25f).Within(1e-6f), "mist patches");
        }

        [Test]
        public void BuiltInFog_AddsNothingWhileThePassIsLive()
        {
            foreach (float distance in new[] { 0f, 100f, 500f, 2000f, 4500f, 10000f })
            {
                float amount = AtmosphereMath.BuiltInFogAmount(true, FogMode.Linear, 0f, AtmosphereMath.LiveFogStart, AtmosphereMath.LiveFogEnd, distance);
                Assert.That(amount, Is.LessThan(1e-6f), $"{distance} m");
            }
        }

        [Test]
        public void BuiltInFog_FormulasMatchUrp()
        {
            Assert.That(AtmosphereMath.BuiltInFogAmount(true, FogMode.Linear, 0f, 100f, 300f, 200f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(AtmosphereMath.BuiltInFogAmount(true, FogMode.Linear, 0f, 100f, 300f, 50f), Is.EqualTo(0f));
            Assert.That(AtmosphereMath.BuiltInFogAmount(true, FogMode.Linear, 0f, 100f, 300f, 400f), Is.EqualTo(1f));
            Assert.That(AtmosphereMath.BuiltInFogAmount(true, FogMode.ExponentialSquared, 0.001f, 0f, 0f, 1000f), Is.EqualTo(1f - (float)Math.Exp(-1.0)).Within(1e-5f));
            Assert.That(AtmosphereMath.BuiltInFogAmount(false, FogMode.ExponentialSquared, 0.001f, 0f, 0f, 1000f), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------ helpers

        private static float Contrast(in CloudColors c) => CloudColorModel.Luminance(c.topColor) - CloudColorModel.Luminance(c.baseColor);

        /// <summary>What weather pulls the clouds toward (the weather tint is applied to the cloud body before the pull).</summary>
        private static Color Attractor(in CloudColors c, Color tint) => c.fogTarget;

        private static float Distance(Color a, Color b)
        {
            float r = a.r - b.r, g = a.g - b.g, bl = a.b - b.b;
            return (float)Math.Sqrt(r * r + g * g + bl * bl);
        }

        private static Color RandomColor(System.Random rng)
        {
            return new Color((float)rng.NextDouble() * 1.5f, (float)rng.NextDouble() * 1.5f, (float)rng.NextDouble() * 1.5f, 1f);
        }

        private static void AssertInRange(Color c, string message)
        {
            Assert.That(c.r, Is.InRange(0f, 1f), message);
            Assert.That(c.g, Is.InRange(0f, 1f), message);
            Assert.That(c.b, Is.InRange(0f, 1f), message);
        }
    }

    /// <summary>
    /// Pure stand-in for the DayCycle inputs of the cloud model: its default gradients (linear between keys, clamped at the
    /// ends, like <see cref="Gradient"/> in Blend mode) and its overcast blend. Keep in step with DayCycle's defaults.
    /// </summary>
    public static class CloudTestSky
    {
        private static readonly (float t, Color c)[] SunColor =
        {
            (0.24f, new Color(1f, 0.45f, 0.2f)), (0.3f, new Color(1f, 0.72f, 0.45f)), (0.38f, new Color(1f, 0.93f, 0.82f)),
            (0.5f, new Color(1f, 0.98f, 0.94f)), (0.62f, new Color(1f, 0.93f, 0.8f)), (0.7f, new Color(1f, 0.68f, 0.4f)),
            (0.76f, new Color(1f, 0.42f, 0.2f)),
        };

        private static readonly (float t, Color c)[] FogColor =
        {
            (0.0f, new Color(0.1f, 0.12f, 0.19f)), (0.22f, new Color(0.12f, 0.14f, 0.22f)), (0.26f, new Color(0.78f, 0.62f, 0.55f)),
            (0.34f, new Color(0.75f, 0.8f, 0.85f)), (0.66f, new Color(0.75f, 0.81f, 0.86f)), (0.74f, new Color(0.85f, 0.6f, 0.48f)),
            (0.79f, new Color(0.12f, 0.14f, 0.22f)), (1f, new Color(0.1f, 0.12f, 0.19f)),
        };

        private static readonly (float t, Color c)[] SkyTop =
        {
            (0.0f, new Color(0.01f, 0.02f, 0.06f)), (0.22f, new Color(0.02f, 0.04f, 0.1f)), (0.27f, new Color(0.14f, 0.2f, 0.38f)),
            (0.36f, new Color(0.05f, 0.28f, 0.58f)), (0.64f, new Color(0.05f, 0.28f, 0.58f)), (0.73f, new Color(0.15f, 0.18f, 0.38f)),
            (0.79f, new Color(0.02f, 0.04f, 0.1f)), (1f, new Color(0.01f, 0.02f, 0.06f)),
        };

        private static readonly (float t, Color c)[] SkyBottom =
        {
            (0.0f, new Color(0.06f, 0.08f, 0.14f)), (0.22f, new Color(0.08f, 0.1f, 0.18f)), (0.26f, new Color(1f, 0.6f, 0.42f)),
            (0.34f, new Color(1f, 0.92f, 0.84f)), (0.66f, new Color(1f, 0.94f, 0.88f)), (0.74f, new Color(1f, 0.55f, 0.35f)),
            (0.79f, new Color(0.08f, 0.1f, 0.18f)), (1f, new Color(0.06f, 0.08f, 0.14f)),
        };

        /// <summary>DayCycle's default moonlight colour.</summary>
        public static readonly Color Moon = new Color(0.55f, 0.66f, 0.95f);

        /// <summary>DayCycle's default noon sun elevation.</summary>
        public const float MaxSunElevation = 58f;

        /// <summary>Model input for <paramref name="hour"/> under the default preset of <paramref name="state"/>.</summary>
        public static CloudLightInput Input(float hour, WeatherState state, float haze)
        {
            WeatherPreset w = WeatherPreset.Default(state);
            return Input(hour, w.overcast, haze, w.cloudTint);
        }

        /// <summary>Model input for <paramref name="hour"/> with explicit weather values.</summary>
        public static CloudLightInput Input(float hour, float overcast, float haze, Color tint)
        {
            float t = DayClock.Wrap(hour) / DayClock.HoursPerDay;
            return new CloudLightInput
            {
                skyHorizon = OvercastBlend(Sample(SkyBottom, t), overcast, 0.85f),
                skyZenith = OvercastBlend(Sample(SkyTop, t), overcast, 1.25f),
                fogColor = OvercastBlend(Sample(FogColor, t), overcast, 0.95f),
                sunColor = Sample(SunColor, t),
                moonColor = Moon,
                sunElevation = DayClock.SunElevation(hour, MaxSunElevation),
                overcast = overcast,
                mist = haze,
                tint = tint,
            };
        }

        /// <summary>Sky horizon colour (after overcast) — for drawing.</summary>
        public static Color Horizon(float hour, float overcast) => OvercastBlend(Sample(SkyBottom, DayClock.Wrap(hour) / DayClock.HoursPerDay), overcast, 0.85f);

        /// <summary>Sky zenith colour (after overcast) — for drawing.</summary>
        public static Color Zenith(float hour, float overcast) => OvercastBlend(Sample(SkyTop, DayClock.Wrap(hour) / DayClock.HoursPerDay), overcast, 1.25f);

        /// <summary>Fog colour (after overcast) — for drawing.</summary>
        public static Color Fog(float hour, float overcast) => OvercastBlend(Sample(FogColor, DayClock.Wrap(hour) / DayClock.HoursPerDay), overcast, 0.95f);

        private static Color Sample((float t, Color c)[] keys, float t)
        {
            if (t <= keys[0].t) return keys[0].c;
            for (int i = 1; i < keys.Length; i++)
            {
                if (t <= keys[i].t)
                {
                    float u = (t - keys[i - 1].t) / Math.Max(1e-6f, keys[i].t - keys[i - 1].t);
                    return Color.LerpUnclamped(keys[i - 1].c, keys[i].c, u);
                }
            }

            return keys[keys.Length - 1].c;
        }

        /// <summary>Same as DayCycle.OvercastBlend.</summary>
        private static Color OvercastBlend(Color c, float amount, float brightness)
        {
            if (amount <= 0f) return c;
            float lum = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * brightness;
            var grey = new Color(lum * 0.96f, lum * 0.98f, lum * 1.03f, c.a);
            return Color.Lerp(c, grey, amount * 0.85f);
        }
    }
}
