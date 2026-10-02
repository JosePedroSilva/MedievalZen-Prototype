using System;
using MedievalZen.Environment;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    public sealed class AtmosphereTests
    {
        private static readonly WeatherState[] AllStates =
        {
            WeatherState.Clear, WeatherState.Hazy, WeatherState.Misty, WeatherState.Overcast, WeatherState.LightRain,
        };

        // ------------------------------------------------------------------ weather model

        [Test]
        public void WeatherModel_IsDeterministicForASeed()
        {
            var a = new WeatherModel(1234);
            var b = new WeatherModel(1234);
            for (int slot = 0; slot < 400; slot++) Assert.That(a.StateForSlot(slot), Is.EqualTo(b.StateForSlot(slot)), $"slot {slot}");

            // Evaluation order must not matter (the Markov chain is cached lazily).
            var c = new WeatherModel(1234);
            Assert.That(c.StateForSlot(399), Is.EqualTo(a.StateForSlot(399)));
            Assert.That(c.StateForSlot(17), Is.EqualTo(a.StateForSlot(17)));
        }

        [Test]
        public void WeatherModel_DifferentSeedsDiffer()
        {
            var a = new WeatherModel(1);
            var b = new WeatherModel(2);
            int differences = 0;
            for (int slot = 0; slot < 200; slot++)
            {
                if (a.StateForSlot(slot) != b.StateForSlot(slot)) differences++;
            }

            Assert.That(differences, Is.GreaterThan(10));
        }

        [Test]
        public void WeatherModel_TransitionsAreContinuous()
        {
            var model = new WeatherModel(99);
            const float crossFadeHours = 1.5f;
            const double step = 0.01;
            WeatherPreset previous = model.Evaluate(0, crossFadeHours, out _, out _, out _);
            float worst = 0f;
            for (double t = step; t < 24 * 12; t += step)
            {
                WeatherPreset now = model.Evaluate(t, crossFadeHours, out _, out _, out _);
                worst = Math.Max(worst, WeatherPreset.MaxDifference(previous, now));
                previous = now;
            }

            // Largest preset gap is ~2 (mist ×1 → ×3); smoothstep slope ≤ 1.5 / fade; step 0.01 h → ≤ 0.02 per step.
            Assert.That(worst, Is.LessThan(0.05f), "a weather multiplier jumped");
        }

        [Test]
        public void WeatherModel_SlotBoundariesMatch()
        {
            var model = new WeatherModel(7);
            for (int slot = 1; slot < 100; slot++)
            {
                double boundary = slot * (double)model.SlotHours;
                WeatherPreset before = model.Evaluate(boundary - 1e-4, 1.5f, out _, out _, out _);
                WeatherPreset after = model.Evaluate(boundary + 1e-4, 1.5f, out _, out _, out _);
                Assert.That(WeatherPreset.MaxDifference(before, after), Is.LessThan(1e-3f), $"slot {slot}");
            }
        }

        [Test]
        public void WeatherModel_SettlesOnThePresetAfterTheCrossFade()
        {
            var model = new WeatherModel(5);
            for (int slot = 0; slot < 50; slot++)
            {
                double inside = slot * (double)model.SlotHours + model.SlotHours * 0.99;
                WeatherPreset p = model.Evaluate(inside, 1.5f, out _, out WeatherState to, out float blend);
                Assert.That(blend, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(WeatherPreset.MaxDifference(p, model.Preset(to)), Is.LessThan(1e-4f));
            }
        }

        [Test]
        public void WeatherModel_MistLikesDawnAndRainIsRare()
        {
            Assert.That(WeatherModel.Weight(WeatherState.Misty, 6f), Is.GreaterThan(WeatherModel.Weight(WeatherState.Misty, 13f) * 5f));
            var model = new WeatherModel(2024);
            int rain = 0, dawnMist = 0, dawnSlots = 0, noonMist = 0, noonSlots = 0;
            const int slots = 4000;
            for (int slot = 0; slot < slots; slot++)
            {
                WeatherState s = model.StateForSlot(slot);
                if (s == WeatherState.LightRain) rain++;
                float hour = (float)((slot + 0.5) * model.SlotHours % 24.0);
                if (hour >= 3f && hour <= 9f)
                {
                    dawnSlots++;
                    if (s == WeatherState.Misty) dawnMist++;
                }
                else if (hour >= 10.5f && hour <= 16.5f)
                {
                    noonSlots++;
                    if (s == WeatherState.Misty) noonMist++;
                }
            }

            Assert.That(rain, Is.GreaterThan(0));
            Assert.That(rain / (float)slots, Is.LessThan(0.15f));
            Assert.That(dawnMist / (float)dawnSlots, Is.GreaterThan(noonMist / (float)noonSlots));
        }

        [Test]
        public void WeatherPresets_AreGentle()
        {
            foreach (WeatherState state in AllStates)
            {
                WeatherPreset p = WeatherPreset.Default(state);
                Assert.That(p.wind, Is.InRange(0f, 0.6f), $"{state} wind");
                Assert.That(p.gust, Is.InRange(0f, 0.3f), $"{state} gusts");
                Assert.That(p.rain, Is.InRange(0f, 1f), $"{state} rain");
                Assert.That(p.sunMultiplier, Is.InRange(0.3f, 1f), $"{state} sun");
            }
        }

        // ------------------------------------------------------------------ fog

        [Test]
        public void FogCurves_StayWithinClampsForAllHoursAndWeathers()
        {
            var profile = new AtmosphereProfile();
            float[] amounts = { 0f, 0.5f, 1f, 2f };
            float[] hazeDistances = { 0f, 1f, 2f };
            const float baseAltitude = 20f;
            foreach (WeatherState state in AllStates)
            {
                WeatherPreset weather = WeatherPreset.Default(state);
                foreach (float amount in amounts)
                {
                    foreach (float haze in hazeDistances)
                    {
                        for (float hour = 0f; hour < 24f; hour += 0.25f)
                        {
                            FogSettings fog = profile.Evaluate(hour, weather, amount, haze, baseAltitude, out float scatter, out float patches);
                            string at = $"{state} amount {amount} haze {haze} hour {hour}";
                            Assert.That(float.IsNaN(fog.hazeDensity) || float.IsNaN(fog.mistDensity), Is.False, at);
                            Assert.That(fog.hazeDensity, Is.GreaterThanOrEqualTo(0f), at);
                            Assert.That(fog.mistDensity, Is.GreaterThanOrEqualTo(0f), at);
                            Assert.That(fog.mistFalloff, Is.InRange(AtmosphereProfile.MinFalloff, AtmosphereProfile.MaxFalloff), at);
                            Assert.That(scatter, Is.InRange(0f, 1.5f), at);
                            Assert.That(patches, Is.InRange(0f, 1f), at);
                            float opacity = AtmosphereMath.Opacity(fog, profile.readableDistance, baseAltitude, baseAltitude);
                            Assert.That(opacity, Is.LessThanOrEqualTo(profile.maxOpacityAtReadable + 1e-3f), at + " — road not readable");
                        }
                    }
                }
            }
        }

        [Test]
        public void FogCurves_DawnIsMistyNoonIsClear()
        {
            var profile = new AtmosphereProfile();
            WeatherPreset clear = WeatherPreset.Default(WeatherState.Clear);
            FogSettings dawn = profile.Evaluate(5.8f, clear, 1f, 1f, 0f, out _, out float dawnPatches);
            FogSettings noon = profile.Evaluate(12.5f, clear, 1f, 1f, 0f, out _, out float noonPatches);
            Assert.That(dawn.mistDensity, Is.GreaterThan(noon.mistDensity * 5f));
            Assert.That(dawn.hazeDensity, Is.GreaterThan(noon.hazeDensity));
            Assert.That(dawnPatches, Is.GreaterThan(noonPatches));
        }

        [Test]
        public void ThickFog_IsClampedToTheReadableOpacity()
        {
            var fog = new FogSettings { hazeDensity = 0.02f, mistDensity = 0.05f, mistFalloff = 0.05f, baseAltitude = 0f };
            float scale = AtmosphereMath.ClampForReadability(ref fog, 60f, 0.5f);
            Assert.That(scale, Is.LessThan(1f));
            Assert.That(AtmosphereMath.Opacity(fog, 60f, 0f, 0f), Is.EqualTo(0.5f).Within(0.01f));

            var thin = new FogSettings { hazeDensity = 0.0003f, mistDensity = 0.001f, mistFalloff = 0.05f, baseAltitude = 0f };
            Assert.That(AtmosphereMath.ClampForReadability(ref thin, 60f, 0.5f), Is.EqualTo(1f));
            Assert.That(thin.mistDensity, Is.EqualTo(0.001f));
        }

        [Test]
        public void HeightFog_ThinsAboveTheBaseAltitude()
        {
            var fog = new FogSettings { hazeDensity = 0f, mistDensity = 0.01f, mistFalloff = 0.05f, baseAltitude = 10f };
            float low = AtmosphereMath.OpticalDepth(fog, 500f, 11f, 11f);
            float high = AtmosphereMath.OpticalDepth(fog, 500f, 80f, 80f);
            float below = AtmosphereMath.OpticalDepth(fog, 500f, 2f, 2f);
            Assert.That(high, Is.LessThan(low * 0.1f));
            Assert.That(below, Is.EqualTo(0.01f * 500f).Within(1e-3f), "density below the base is capped at the base density");

            // Sloped ray: symmetric and between the two horizontal cases.
            float up = AtmosphereMath.OpticalDepth(fog, 500f, 11f, 80f);
            float down = AtmosphereMath.OpticalDepth(fog, 500f, 80f, 11f);
            Assert.That(up, Is.EqualTo(down).Within(1e-4f));
            Assert.That(up, Is.InRange(high, low));
        }

        [Test]
        public void HazeHalfDistance_GivesHalfOpacity()
        {
            var fog = new FogSettings { hazeDensity = AtmosphereMath.HazeDensityForHalfDistance(800f), mistFalloff = 0.05f };
            Assert.That(AtmosphereMath.Opacity(fog, 800f, 0f, 0f), Is.EqualTo(0.5f).Within(1e-3f));
        }

        // ------------------------------------------------------------------ settlement lights

        [Test]
        public void LightFade_IsOffAtNoonOnAtMidnightAndContinuous()
        {
            const float on = 17.6f, off = 5.3f, fade = 0.5f;
            Assert.That(AtmosphereMath.LightFade(12f, on, fade, off, fade), Is.EqualTo(0f));
            Assert.That(AtmosphereMath.LightFade(0f, on, fade, off, fade), Is.EqualTo(1f));
            Assert.That(AtmosphereMath.LightFade(on + fade + 0.01f, on, fade, off, fade), Is.EqualTo(1f));
            Assert.That(AtmosphereMath.LightFade(off + fade + 0.01f, on, fade, off, fade), Is.EqualTo(0f));

            float previous = AtmosphereMath.LightFade(0f, on, fade, off, fade);
            float worst = 0f;
            for (float h = 0.001f; h <= 24f; h += 0.001f)
            {
                float v = AtmosphereMath.LightFade(h, on, fade, off, fade);
                Assert.That(v, Is.InRange(0f, 1f));
                worst = Math.Max(worst, Math.Abs(v - previous));
                previous = v;
            }

            Assert.That(worst, Is.LessThan(0.01f), "light fade jumped");
        }

        [Test]
        public void LightFade_RisesThroughDuskAndFallsThroughDawn()
        {
            const float on = 17.6f, off = 5.3f, fade = 0.5f;
            float last = -1f;
            for (float h = on; h <= on + fade; h += 0.01f)
            {
                float v = AtmosphereMath.LightFade(h, on, fade, off, fade);
                Assert.That(v, Is.GreaterThanOrEqualTo(last - 1e-6f));
                last = v;
            }

            last = 2f;
            for (float h = off; h <= off + fade; h += 0.01f)
            {
                float v = AtmosphereMath.LightFade(h, on, fade, off, fade);
                Assert.That(v, Is.LessThanOrEqualTo(last + 1e-6f));
                last = v;
            }
        }

        [Test]
        public void LightBudget_NeverExceedsTheCapAndServesTheNearestFirst()
        {
            int[] requests = { 5, 6, 4, 9 };
            var granted = new int[4];
            int total = AtmosphereMath.AllocateBudget(requests, 4, SettlementAmbience.GlobalLightCap, granted);
            Assert.That(total, Is.LessThanOrEqualTo(SettlementAmbience.GlobalLightCap));
            Assert.That(granted[0], Is.EqualTo(5));
            Assert.That(granted[1], Is.EqualTo(6));
            Assert.That(granted[0] + granted[1] + granted[2] + granted[3], Is.EqualTo(total));

            total = AtmosphereMath.AllocateBudget(requests, 4, 8, granted);
            Assert.That(total, Is.EqualTo(8));
            Assert.That(granted, Is.EqualTo(new[] { 5, 3, 0, 0 }));

            total = AtmosphereMath.AllocateBudget(new[] { 1, 2 }, 2, 16, granted);
            Assert.That(total, Is.EqualTo(3));
        }

        [Test]
        public void SmokeAmount_PeaksAtMealTimes()
        {
            Assert.That(SettlementAmbience.SmokeAmount(7.5f), Is.GreaterThan(SettlementAmbience.SmokeAmount(13f)));
            Assert.That(SettlementAmbience.SmokeAmount(19f), Is.GreaterThan(SettlementAmbience.SmokeAmount(2f)));
            for (float h = 0f; h < 24f; h += 0.5f) Assert.That(SettlementAmbience.SmokeAmount(h), Is.InRange(0.3f, 1f));
        }
    }
}
