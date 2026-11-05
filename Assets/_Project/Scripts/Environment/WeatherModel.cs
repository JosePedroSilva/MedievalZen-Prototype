using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>The gentle weather states of the world (never stormy).</summary>
    public enum WeatherState
    {
        /// <summary>Clear sky, light aerial haze.</summary>
        Clear = 0,
        /// <summary>Warm, milky haze; long views soften.</summary>
        Hazy = 1,
        /// <summary>Thick low mist in the valleys and over the lakes.</summary>
        Misty = 2,
        /// <summary>Grey sky, flat light, a bit of wind.</summary>
        Overcast = 3,
        /// <summary>Soft drizzle under a grey sky.</summary>
        LightRain = 4,
    }

    /// <summary>
    /// Everything a weather state changes, as multipliers/amounts. Presets are cross-faded with <see cref="Lerp"/>, so every
    /// field must be meaningful when interpolated linearly.
    /// </summary>
    [Serializable]
    public struct WeatherPreset
    {
        [Tooltip("Multiplier on the distance-haze density.")]
        public float hazeMultiplier;

        [Tooltip("Multiplier on the height-fog (valley mist) density.")]
        public float mistMultiplier;

        [Tooltip("0..1: how many low mist patches (MistField) appear, on top of the time-of-day amount.")]
        public float mistAmount;

        [Tooltip("Multiplier on the sun in-scattering strength of the fog.")]
        public float scatterMultiplier;

        [Tooltip("Multiplier on the sun/moon light intensity (DayCycle).")]
        public float sunMultiplier;

        [Tooltip("0..1: greys the sky, the horizon fog and the grading.")]
        public float overcast;

        [Tooltip("Multiplier on the cloud ring colours.")]
        public Color cloudTint;

        [Tooltip("0..1: Synty vegetation wind (_WindIntensity).")]
        public float wind;

        [Tooltip("0..1: Synty gusts (_GaleStrength).")]
        public float gust;

        [Tooltip("0..1: rain particle amount.")]
        public float rain;

        [Tooltip("0..1: how likely drifting wind streaks are.")]
        public float windStreaks;

        /// <summary>Component-wise linear blend (t clamped to 0..1).</summary>
        public static WeatherPreset Lerp(in WeatherPreset a, in WeatherPreset b, float t)
        {
            t = Mathf.Clamp01(t);
            return new WeatherPreset
            {
                hazeMultiplier = a.hazeMultiplier + (b.hazeMultiplier - a.hazeMultiplier) * t,
                mistMultiplier = a.mistMultiplier + (b.mistMultiplier - a.mistMultiplier) * t,
                mistAmount = a.mistAmount + (b.mistAmount - a.mistAmount) * t,
                scatterMultiplier = a.scatterMultiplier + (b.scatterMultiplier - a.scatterMultiplier) * t,
                sunMultiplier = a.sunMultiplier + (b.sunMultiplier - a.sunMultiplier) * t,
                overcast = a.overcast + (b.overcast - a.overcast) * t,
                cloudTint = Color.LerpUnclamped(a.cloudTint, b.cloudTint, t),
                wind = a.wind + (b.wind - a.wind) * t,
                gust = a.gust + (b.gust - a.gust) * t,
                rain = a.rain + (b.rain - a.rain) * t,
                windStreaks = a.windStreaks + (b.windStreaks - a.windStreaks) * t,
            };
        }

        /// <summary>Largest absolute difference over every field (colour channels included).</summary>
        public static float MaxDifference(in WeatherPreset a, in WeatherPreset b)
        {
            float d = Math.Abs(a.hazeMultiplier - b.hazeMultiplier);
            d = Math.Max(d, Math.Abs(a.mistMultiplier - b.mistMultiplier));
            d = Math.Max(d, Math.Abs(a.mistAmount - b.mistAmount));
            d = Math.Max(d, Math.Abs(a.scatterMultiplier - b.scatterMultiplier));
            d = Math.Max(d, Math.Abs(a.sunMultiplier - b.sunMultiplier));
            d = Math.Max(d, Math.Abs(a.overcast - b.overcast));
            d = Math.Max(d, Math.Abs(a.cloudTint.r - b.cloudTint.r));
            d = Math.Max(d, Math.Abs(a.cloudTint.g - b.cloudTint.g));
            d = Math.Max(d, Math.Abs(a.cloudTint.b - b.cloudTint.b));
            d = Math.Max(d, Math.Abs(a.wind - b.wind));
            d = Math.Max(d, Math.Abs(a.gust - b.gust));
            d = Math.Max(d, Math.Abs(a.rain - b.rain));
            d = Math.Max(d, Math.Abs(a.windStreaks - b.windStreaks));
            return d;
        }

        /// <summary>The default preset for <paramref name="state"/>.</summary>
        public static WeatherPreset Default(WeatherState state)
        {
            switch (state)
            {
                case WeatherState.Hazy:
                    return Make(2.1f, 1.3f, 0.35f, 1.3f, 0.85f, 0.15f, new Color(1f, 0.97f, 0.92f), 0.25f, 0.05f, 0f, 0.6f);
                case WeatherState.Misty:
                    return Make(1.5f, 3.0f, 1f, 1.1f, 0.7f, 0.3f, new Color(0.92f, 0.93f, 0.95f), 0.1f, 0.02f, 0f, 0f);
                case WeatherState.Overcast:
                    return Make(1.7f, 1.5f, 0.4f, 0.35f, 0.5f, 0.8f, new Color(0.74f, 0.76f, 0.8f), 0.45f, 0.15f, 0f, 0.8f);
                case WeatherState.LightRain:
                    return Make(2.3f, 2.0f, 0.6f, 0.25f, 0.4f, 1f, new Color(0.62f, 0.64f, 0.68f), 0.55f, 0.25f, 1f, 0.3f);
                default:
                    return Make(1f, 1f, 0.25f, 1f, 1f, 0f, Color.white, 0.3f, 0.05f, 0f, 0f);
            }
        }

        private static WeatherPreset Make(float haze, float mist, float mistAmount, float scatter, float sun, float overcast, Color clouds,
            float wind, float gust, float rain, float streaks)
        {
            return new WeatherPreset
            {
                hazeMultiplier = haze, mistMultiplier = mist, mistAmount = mistAmount, scatterMultiplier = scatter,
                sunMultiplier = sun, overcast = overcast, cloudTint = clouds, wind = wind, gust = gust, rain = rain, windStreaks = streaks,
            };
        }
    }

    /// <summary>
    /// Pure, deterministic weather schedule. Time is cut into slots of <see cref="SlotHours"/> in-game hours; each slot's
    /// state is a Markov step from the previous slot (stay with <see cref="StayProbability"/>, otherwise a draw weighted by
    /// the time of day — mist likes dawn, rain is rare), seeded per slot from the world seed, so the whole sequence is a
    /// function of the seed only. <see cref="Evaluate"/> cross-fades from the previous slot's preset over the first
    /// <c>crossFadeHours</c> of each slot with a smoothstep, so every multiplier is continuous in time.
    /// </summary>
    public sealed class WeatherModel
    {
        /// <summary>Number of weather states.</summary>
        public const int StateCount = 5;

        private readonly int seed;
        private readonly WeatherPreset[] presets;
        private readonly List<byte> states = new List<byte>(64);
        private readonly float[] weights = new float[StateCount];

        /// <summary>Creates a schedule for <paramref name="seed"/>. <paramref name="statePresets"/> may be null (defaults) or hold one preset per <see cref="WeatherState"/>.</summary>
        public WeatherModel(int seed, WeatherPreset[] statePresets = null, float slotHours = 3f, float stayProbability = 0.55f)
        {
            this.seed = seed;
            SlotHours = Mathf.Clamp(slotHours, 0.5f, 24f);
            StayProbability = Mathf.Clamp01(stayProbability);
            presets = new WeatherPreset[StateCount];
            for (int i = 0; i < StateCount; i++)
            {
                presets[i] = statePresets != null && i < statePresets.Length ? statePresets[i] : WeatherPreset.Default((WeatherState)i);
            }
        }

        /// <summary>Length of one weather slot in in-game hours.</summary>
        public float SlotHours { get; }

        /// <summary>Chance that a slot keeps the previous slot's state.</summary>
        public float StayProbability { get; }

        /// <summary>The preset used for <paramref name="state"/>.</summary>
        public WeatherPreset Preset(WeatherState state) => presets[(int)state];

        private SeasonChoice season = SeasonChoice.Autumn;

        /// <summary>
        /// The season choice the schedule follows (2026-10-06; fixed autumn by default, the schedule the game always had). Each
        /// slot draws with the weights of the year at that slot (<see cref="Season.YearFor"/> at the slot's middle), so under Auto
        /// the weather turns with the seasons. Changing it rebuilds the schedule: the same seed and choice always give the same
        /// sequence.
        /// </summary>
        public SeasonChoice Season
        {
            get => season;
            set
            {
                if (value == season) return;
                season = value;
                states.Clear();
            }
        }

        /// <summary>The year (<see cref="MedievalZen.Environment.Season"/>) at the middle of slot <paramref name="slot"/> under <see cref="Season"/>.</summary>
        public float YearForSlot(long slot)
        {
            double middle = (slot + 0.5) * SlotHours;
            int day = (int)Math.Floor(middle / DayClock.HoursPerDay) + 1;
            float hour = (float)(middle - (day - 1) * (double)DayClock.HoursPerDay);
            return MedievalZen.Environment.Season.YearFor(season, day, hour);
        }

        /// <summary>
        /// Season multipliers on <see cref="Weight(WeatherState, float)"/>, per state (spring, summer; autumn is 1). Spring is
        /// showery: much more light rain, little haze, fewer grey spells, so clear sky and showers alternate. Summer is clear or
        /// hazy, with little rain and hardly any mist.
        /// </summary>
        private static readonly float[] SpringWeight = { 1.0f, 0.45f, 1.0f, 0.7f, 4.0f };
        private static readonly float[] SummerWeight = { 1.35f, 1.9f, 0.12f, 0.45f, 0.35f };

        /// <summary>
        /// Relative likelihood of <paramref name="state"/> at clock <paramref name="hour"/> in a year of <see cref="MedievalZen.Environment.Season"/>:
        /// <see cref="Weight(WeatherState, float)"/> at the season's sky hour (dawn mist comes at the season's dawn) times the
        /// season's multiplier. At the identity (autumn) it is exactly <see cref="Weight(WeatherState, float)"/>.
        /// </summary>
        public static float Weight(WeatherState state, float hour, float year)
        {
            int i = (int)state;
            if (i < 0 || i >= StateCount) return 0f;
            float w = Weight(state, DayClock.SolarHour(hour, year));
            return w * MedievalZen.Environment.Season.Blend(year, SpringWeight[i], SummerWeight[i], 1f);
        }

        /// <summary>
        /// Chance that a slot keeps the previous state, in a year: the autumn figures (<see cref="StayProbability"/>, 0.6 of it
        /// after rain) at the identity; spring's showers pass sooner and its weather turns more often.
        /// </summary>
        public float StayFor(WeatherState previous, float year)
        {
            float scale = MedievalZen.Environment.Season.Blend(year, 0.85f, 1.1f, 1f);
            if (previous == WeatherState.LightRain)
            {
                float rainFactor = MedievalZen.Environment.Season.Blend(year, 0.35f, 0.5f, 0.6f);
                return Math.Min(1f, StayProbability * rainFactor * scale);
            }

            return Math.Min(1f, StayProbability * scale);
        }

        /// <summary>In-game hours since day 1, 00:00 (<paramref name="day"/> starts at 1).</summary>
        public static double AbsoluteHours(int day, float hour) => Math.Max(0, day - 1) * (double)DayClock.HoursPerDay + hour;

        /// <summary>Weather state of slot <paramref name="slot"/> (slot 0 starts at day 1, 00:00).</summary>
        public WeatherState StateForSlot(long slot)
        {
            if (slot < 0) slot = 0;
            if (slot > 10_000_000) slot = 10_000_000;
            while (states.Count <= slot)
            {
                int index = states.Count;
                WeatherState previous = index == 0 ? WeatherState.Clear : (WeatherState)states[index - 1];
                states.Add((byte)Step(previous, index));
            }

            return (WeatherState)states[(int)slot];
        }

        /// <summary>
        /// Blended preset at <paramref name="absoluteHours"/> (see <see cref="AbsoluteHours"/>). <paramref name="from"/> and
        /// <paramref name="to"/> are the states being blended and <paramref name="blend"/> the smoothstep weight of <paramref name="to"/>.
        /// </summary>
        public WeatherPreset Evaluate(double absoluteHours, float crossFadeHours, out WeatherState from, out WeatherState to, out float blend)
        {
            if (double.IsNaN(absoluteHours) || absoluteHours < 0) absoluteHours = 0;
            long slot = (long)Math.Floor(absoluteHours / SlotHours);
            to = StateForSlot(slot);
            from = slot > 0 ? StateForSlot(slot - 1) : to;
            float into = (float)(absoluteHours - slot * (double)SlotHours);
            float fade = Mathf.Clamp(crossFadeHours, 1e-4f, SlotHours * 0.95f);
            float t = Mathf.Clamp01(into / fade);
            blend = t * t * (3f - 2f * t);
            return WeatherPreset.Lerp(presets[(int)from], presets[(int)to], blend);
        }

        /// <summary>Relative likelihood of <paramref name="state"/> at <paramref name="hour"/> (0..24).</summary>
        public static float Weight(WeatherState state, float hour)
        {
            switch (state)
            {
                case WeatherState.Clear: return 1f;
                case WeatherState.Hazy: return 0.2f + 0.45f * Bump(hour, 14f, 3.5f);
                case WeatherState.Misty: return 0.04f + 1.4f * Bump(hour, 6f, 2.2f) + 0.35f * Bump(hour, 20.5f, 2f);
                case WeatherState.Overcast: return 0.3f;
                case WeatherState.LightRain: return 0.07f;
                default: return 0f;
            }
        }

        private WeatherState Step(WeatherState previous, int slot)
        {
            var rng = new System.Random(unchecked(seed * 73856093 ^ slot * 19349663 ^ 0x5bd1e995));
            float year = YearForSlot(slot);   // exactly the identity under fixed autumn: every figure below is then today's
            float stay = StayFor(previous, year);
            if (slot > 0 && rng.NextDouble() < stay) return previous;

            float hour = (float)((slot + 0.5) * SlotHours % DayClock.HoursPerDay);
            float total = 0f;
            for (int i = 0; i < StateCount; i++)
            {
                weights[i] = Weight((WeatherState)i, hour, year);
                total += weights[i];
            }

            double pick = rng.NextDouble() * total;
            for (int i = 0; i < StateCount; i++)
            {
                pick -= weights[i];
                if (pick <= 0) return (WeatherState)i;
            }

            return WeatherState.Clear;
        }

        private static float Bump(float hour, float centre, float width)
        {
            float d = Math.Abs(DayClock.Wrap(hour) - centre);
            d = Math.Min(d, DayClock.HoursPerDay - d);
            float x = d / width;
            return (float)Math.Exp(-x * x);
        }
    }
}
