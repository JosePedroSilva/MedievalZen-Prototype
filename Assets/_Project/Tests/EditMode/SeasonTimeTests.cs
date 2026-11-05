using System;
using System.Collections.Generic;
using MedievalZen.Environment;
using MedievalZen.UI;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>
    /// Seasons, time-and-sky half (2026-10-06): the year from the choice, the day and the hour; the summer day's remap onto the
    /// equinox gradients; the seasonal weather; the season's ambient effects; the setting. Autumn must be today, bit for bit.
    /// </summary>
    public sealed class SeasonTimeTests
    {
        private static void Reset()
        {
            Season.Choice = SeasonChoice.Autumn;
            Season.Set(Season.Identity);
        }

        [Test]
        public void YearFollowsTheChoiceTheDayAndTheHour()
        {
            Assert.That(Season.YearFor(SeasonChoice.Autumn, 37, 5f), Is.EqualTo(Season.Autumn));
            Assert.That(Season.YearFor(SeasonChoice.Spring, 2, 13f), Is.EqualTo(Season.Spring));
            Assert.That(Season.YearFor(SeasonChoice.Summer, 9, 0f), Is.EqualTo(Season.Summer));
            Assert.That(Season.YearFor(SeasonChoice.Auto, 1, 0f), Is.EqualTo(Season.Identity), "Auto starts at autumn on day 1");
            Assert.That(Season.YearFor(SeasonChoice.Auto, 6, 0f), Is.EqualTo(Season.Spring).Within(1e-5f), "five days on: spring");
            Assert.That(Season.YearFor(SeasonChoice.Auto, 11, 0f), Is.EqualTo(Season.Summer).Within(1e-5f), "ten days on: summer");
            Assert.That(Season.YearFor(SeasonChoice.Auto, 16, 0f), Is.EqualTo(Season.Autumn).Within(1e-5f), "fifteen days on: autumn again");
            Assert.That(Season.YearFor(SeasonChoice.Auto, 3, 12f), Is.EqualTo(Season.Wrap(2f + 2.5f / 5f)).Within(1e-5f), "the hour counts");
            Assert.That(Season.Name(Season.YearFor(SeasonChoice.Auto, 5, 0f)), Is.EqualTo("Early spring"));
        }

        [Test]
        public void AutumnAndSpringKeepTodaysSunBitForBit()
        {
            foreach (float year in new[] { Season.Autumn, Season.Spring })
            {
                Assert.That(DayClock.SunriseAt(year), Is.EqualTo(DayClock.Sunrise));
                Assert.That(DayClock.SunsetAt(year), Is.EqualTo(DayClock.Sunset));
                Assert.That(DayClock.NoonElevationAt(year, 58f), Is.EqualTo(58f));
                for (int i = 0; i <= 2400; i++)
                {
                    float h = i * 0.01f;
                    float solar = DayClock.SolarHour(h, year);
                    Assert.That(BitConverter.SingleToInt32Bits(solar), Is.EqualTo(BitConverter.SingleToInt32Bits(h)), $"sky hour at {h}");
                    Assert.That(BitConverter.SingleToInt32Bits(DayClock.ClockHour(h, year)), Is.EqualTo(BitConverter.SingleToInt32Bits(h)));
                    float before = DayClock.SunElevation(h, 58f);   // DayCycle before seasons: elevation and t = hour / 24
                    float now = DayClock.SunElevation(solar, DayClock.NoonElevationAt(year, 58f));
                    Assert.That(BitConverter.SingleToInt32Bits(now), Is.EqualTo(BitConverter.SingleToInt32Bits(before)), $"elevation at {h}");
                    Assert.That(solar / DayClock.HoursPerDay, Is.EqualTo(h / DayClock.HoursPerDay), "day fraction");
                }
            }

            try
            {
                Season.Set(Season.Identity);
                Assert.That(new DayClock(24f, 5.5f).IsDay, Is.False);
                Assert.That(new DayClock(24f, 6.5f).IsDay, Is.True);
                Assert.That(new DayClock(24f, 18.5f).IsDay, Is.False);
            }
            finally
            {
                Reset();
            }
        }

        [Test]
        public void SummerSunriseAndSunsetLandOnTheDawnAndDuskKeys()
        {
            float y = Season.Summer;
            Assert.That(DayClock.SunriseAt(y), Is.EqualTo(4.75f));
            Assert.That(DayClock.SunsetAt(y), Is.EqualTo(19.25f));
            Assert.That(DayClock.SolarHour(4.75f, y), Is.EqualTo(DayClock.Sunrise).Within(1e-4f), "sunrise on the 06:00 keys");
            Assert.That(DayClock.SolarHour(19.25f, y), Is.EqualTo(DayClock.Sunset).Within(1e-4f), "sunset on the 18:00 keys");
            Assert.That(DayClock.SolarHour(12f, y), Is.EqualTo(12f).Within(1e-4f), "noon stays noon");
            float midnight = DayClock.SolarHour(0f, y);
            Assert.That(Math.Min(midnight, 24f - midnight), Is.LessThan(1e-3f), "midnight stays midnight");
            float noonSun = DayClock.NoonElevationAt(y, 58f);
            Assert.That(noonSun, Is.EqualTo(64f));
            Assert.That(DayClock.SunElevation(DayClock.SolarHour(12f, y), noonSun), Is.EqualTo(64f).Within(1e-3f));
            Assert.That(DayClock.SunElevation(DayClock.SolarHour(4.75f, y), noonSun), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(DayClock.SunElevation(DayClock.SolarHour(19.25f, y), noonSun), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(DayClock.SunElevation(DayClock.SolarHour(5f, y), noonSun), Is.GreaterThan(0f), "light at five in summer");
            Assert.That(DayClock.SunElevation(DayClock.SolarHour(19f, y), noonSun), Is.GreaterThan(0f));
            Assert.That(DayClock.SunElevation(DayClock.SolarHour(20f, y), noonSun), Is.LessThan(0f));

            float last = -1f;
            for (int i = 0; i < 2400; i++)
            {
                float h = i * 0.01f;
                float s = DayClock.SolarHour(h, y);
                if (i > 0) Assert.That(s, Is.GreaterThan(last), $"the sky hour only moves forward ({h})");
                last = s;
                Assert.That(DayClock.ClockHour(s, y), Is.EqualTo(h).Within(2e-3f), $"inverse at {h}");
            }

            try
            {
                Season.Set(y);
                Assert.That(new DayClock(24f, 5f).IsDay, Is.True);
                Assert.That(new DayClock(24f, 19.5f).IsDay, Is.False);
            }
            finally
            {
                Reset();
            }
        }

        /// <summary>The weather chain exactly as it was before seasons (copied from WeatherModel.Step on 2026-10-06).</summary>
        private static List<WeatherState> TodaysChain(int seed, int count, float slotHours = 3f, float stayProbability = 0.55f)
        {
            var list = new List<WeatherState>(count);
            var weights = new float[WeatherModel.StateCount];
            WeatherState previous = WeatherState.Clear;
            for (int slot = 0; slot < count; slot++)
            {
                var rng = new System.Random(unchecked(seed * 73856093 ^ slot * 19349663 ^ 0x5bd1e995));
                float stay = previous == WeatherState.LightRain ? stayProbability * 0.6f : stayProbability;
                WeatherState next = WeatherState.Clear;
                if (slot > 0 && rng.NextDouble() < stay) next = previous;
                else
                {
                    float hour = (float)((slot + 0.5) * slotHours % DayClock.HoursPerDay);
                    float total = 0f;
                    for (int i = 0; i < WeatherModel.StateCount; i++)
                    {
                        weights[i] = WeatherModel.Weight((WeatherState)i, hour);
                        total += weights[i];
                    }

                    double pick = rng.NextDouble() * total;
                    for (int i = 0; i < WeatherModel.StateCount; i++)
                    {
                        pick -= weights[i];
                        if (pick <= 0) { next = (WeatherState)i; break; }
                    }
                }

                list.Add(next);
                previous = next;
            }

            return list;
        }

        [Test]
        public void AutumnWeatherIsTodaysSequence()
        {
            const int slots = 20 * 8;   // 20 days of 3-hour slots
            foreach (int seed in new[] { 1, 12345, -987654 })
            {
                List<WeatherState> today = TodaysChain(seed, slots);
                var model = new WeatherModel(seed);
                Assert.That(model.Season, Is.EqualTo(SeasonChoice.Autumn), "fixed autumn by default");
                for (int s = 0; s < slots; s++) Assert.That(model.StateForSlot(s), Is.EqualTo(today[s]), $"seed {seed} slot {s}");
                for (int i = 0; i < WeatherModel.StateCount; i++)
                {
                    for (float h = 0f; h < 24f; h += 0.25f)
                    {
                        Assert.That(WeatherModel.Weight((WeatherState)i, h, Season.Identity), Is.EqualTo(WeatherModel.Weight((WeatherState)i, h)));
                    }
                }
            }
        }

        [Test]
        public void SeasonalWeatherIsDeterministicPerSeedAndSeason()
        {
            foreach (SeasonChoice choice in new[] { SeasonChoice.Auto, SeasonChoice.Spring, SeasonChoice.Summer })
            {
                var a = new WeatherModel(12345) { Season = choice };
                var b = new WeatherModel(12345) { Season = choice };
                bool differs = false;
                var autumn = new WeatherModel(12345);
                for (int s = 0; s < 480; s++)
                {
                    Assert.That(a.StateForSlot(s), Is.EqualTo(b.StateForSlot(s)), $"{choice} slot {s}");
                    differs |= a.StateForSlot(s) != autumn.StateForSlot(s);
                }

                Assert.That(differs, Is.True, $"{choice} weather differs from autumn's somewhere in 60 days");
            }

            var flip = new WeatherModel(777);
            WeatherState first = flip.StateForSlot(100);
            flip.Season = SeasonChoice.Summer;
            flip.StateForSlot(100);
            flip.Season = SeasonChoice.Autumn;
            Assert.That(flip.StateForSlot(100), Is.EqualTo(first), "back to autumn, back to the same schedule");
            Assert.That(new WeatherModel(5) { Season = SeasonChoice.Auto }.YearForSlot(0), Is.EqualTo(Season.Identity + 1.5f / 24f / Season.DaysPerSeason).Within(1e-5f), "the first slot's middle, 01:30 on day 1");
        }

        [Test]
        public void SummerIsDrierAndSpringWetterThanAutumn()
        {
            float Share(SeasonChoice choice, WeatherState state)
            {
                int n = 0, total = 0;
                foreach (int seed in new[] { 1, 2, 3, 4, 5, 12345 })
                {
                    var m = new WeatherModel(seed) { Season = choice };
                    for (int s = 0; s < 480; s++, total++) if (m.StateForSlot(s) == state) n++;
                }

                return n / (float)total;
            }

            Assert.That(Share(SeasonChoice.Spring, WeatherState.LightRain), Is.GreaterThan(Share(SeasonChoice.Autumn, WeatherState.LightRain)));
            Assert.That(Share(SeasonChoice.Summer, WeatherState.LightRain), Is.LessThan(Share(SeasonChoice.Autumn, WeatherState.LightRain)));
            Assert.That(Share(SeasonChoice.Summer, WeatherState.Misty), Is.LessThan(Share(SeasonChoice.Autumn, WeatherState.Misty) * 0.5f));
            Assert.That(Share(SeasonChoice.Summer, WeatherState.Clear) + Share(SeasonChoice.Summer, WeatherState.Hazy), Is.GreaterThan(0.7f));
        }

        [Test]
        public void SeasonEffectsAreTodaysAtAutumn()
        {
            for (int max = 0; max <= 4; max++)
            {
                Assert.That(SeasonFx.FallingLeaves(max, Season.Autumn), Is.EqualTo(max));
                Assert.That(SeasonFx.Pollen(max, Season.Autumn), Is.EqualTo(max));
                Assert.That(SeasonFx.Fireflies(max, Season.Autumn), Is.EqualTo(max));
                Assert.That(SeasonFx.Blossom(max, Season.Autumn), Is.EqualTo(0));
            }

            Assert.That(SeasonFx.FallingLeaves(2, Season.Spring), Is.EqualTo(0), "no falling leaves in spring");
            Assert.That(SeasonFx.FallingLeaves(2, Season.Summer), Is.EqualTo(0), "nor in summer");
            Assert.That(SeasonFx.FallingLeaves(2, 1.75f), Is.GreaterThan(0), "they come back as autumn approaches");
            Assert.That(SeasonFx.Blossom(2, Season.Spring), Is.EqualTo(2));
            Assert.That(SeasonFx.Blossom(2, Season.Summer), Is.EqualTo(0));
            Assert.That(SeasonFx.Pollen(2, Season.Spring), Is.EqualTo(3), "more pollen in spring");
            Assert.That(SeasonFx.Pollen(2, Season.Summer), Is.EqualTo(2), "as now in summer");
        }

        [Test]
        public void SeasonSettingMigratesAndRoundTrips()
        {
            Assert.That(new JourneySettings().Season, Is.EqualTo(SeasonChoice.Autumn));
            Assert.That(JourneySettings.CurrentVersion, Is.GreaterThanOrEqualTo(7));
            Assert.That(JourneySettings.TryFromJson("{\"version\":6,\"season\":\"Summer\",\"walkPace\":0.8}", new JourneySettings(), out JourneySettings old), Is.True);
            Assert.That(old.Season, Is.EqualTo(SeasonChoice.Autumn), "a version-6 blob could not have chosen a season");
            Assert.That(old.WalkingPace, Is.EqualTo(0.8f).Within(1e-5f));

            var a = new JourneySettings { Season = SeasonChoice.Auto };
            string json = a.ToJson();
            StringAssert.Contains("\"season\":\"Auto\"", json);
            Assert.That(JourneySettings.TryFromJson(json, new JourneySettings(), out JourneySettings read), Is.True);
            Assert.That(read.Season, Is.EqualTo(SeasonChoice.Auto));
            Assert.That(read.SameAs(a), Is.True);
            Assert.That(new JourneySettings().SameAs(a), Is.False, "the season counts for SameAs");

            string v = "{\"version\":" + JourneySettings.CurrentVersion + ",\"season\":";
            Assert.That(JourneySettings.TryFromJson(v + "\"Winter\"}", new JourneySettings(), out JourneySettings winter), Is.True);
            Assert.That(winter.Season, Is.EqualTo(SeasonChoice.Autumn), "an unknown name keeps the default");
            Assert.That(JourneySettings.TryFromJson(v + "\"2\"}", new JourneySettings(), out JourneySettings numeric), Is.True);
            Assert.That(numeric.Season, Is.EqualTo(SeasonChoice.Autumn), "names only");
            Assert.That(new JourneySettings { Season = (SeasonChoice)9 }.Clamp().Season, Is.EqualTo(SeasonChoice.Autumn));
        }
    }
}
