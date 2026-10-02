using System;
using MedievalZen.Environment;

namespace MedievalZen.UI
{
    /// <summary>
    /// Pure time rules of the Journey menu.
    /// <para><b>Set-hour rule.</b> Everything the menu does to the clock (the slider, the Dawn/Noon/Dusk/Night buttons)
    /// moves the hour <i>within the current day</i>: the day counter never changes, whether the new hour is earlier or later
    /// than now (<see cref="DayClock.SetTime"/> with <c>forwardOnly = false</c>, exposed as <c>DayCycle.SetHourOfDay</c>).
    /// The slider is a picture of today, 00:00 on the left to 24:00 on the right; dragging it left goes back to earlier
    /// today, never to yesterday or tomorrow. Only the natural flow of time (or resting at an inn, which is a forward jump)
    /// crosses midnight and counts a new day.</para>
    /// <para><b>Flow of time.</b> A multiplier on <c>DayCycle.TimeScale</c>: Paused 0, Slow ½, Normal 1, Fast 2, Very fast 4;
    /// the real length of a day is <c>WorldSettings.dayLengthMinutes</c> ÷ that multiplier.</para>
    /// </summary>
    public static class JourneyTime
    {
        /// <summary>"Dawn" button: just after sunrise, while the light is still golden.</summary>
        public const float DawnHour = 6.25f;

        /// <summary>"Noon" button.</summary>
        public const float NoonHour = 12f;

        /// <summary>"Dusk" button: shortly before sunset.</summary>
        public const float DuskHour = 17.75f;

        /// <summary>"Night" button: moonlit, well after dusk.</summary>
        public const float NightHour = 22.5f;

        /// <summary>Seconds a Dawn/Noon/Dusk/Night button takes to glide the sun to its hour.</summary>
        public const float GlideSeconds = 2.5f;

        /// <summary>Number of <see cref="FlowOfTime"/> choices.</summary>
        public const int FlowCount = 5;

        /// <summary>The <c>DayCycle.TimeScale</c> for <paramref name="flow"/>.</summary>
        public static float FlowScale(FlowOfTime flow)
        {
            switch (flow)
            {
                case FlowOfTime.Paused: return 0f;
                case FlowOfTime.Slow: return 0.5f;
                case FlowOfTime.Fast: return 2f;
                case FlowOfTime.VeryFast: return 4f;
                default: return 1f;
            }
        }

        /// <summary>The choice whose scale is closest to <paramref name="timeScale"/> (ties go to the slower one).</summary>
        public static FlowOfTime NearestFlow(float timeScale)
        {
            if (float.IsNaN(timeScale) || timeScale <= 0f) return FlowOfTime.Paused;
            FlowOfTime best = FlowOfTime.Paused;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < FlowCount; i++)
            {
                var flow = (FlowOfTime)i;
                float d = Math.Abs(FlowScale(flow) - timeScale);
                if (d < bestDistance - 1e-6f)
                {
                    bestDistance = d;
                    best = flow;
                }
            }

            return best;
        }

        /// <summary>Menu label of <paramref name="flow"/>.</summary>
        public static string FlowLabel(FlowOfTime flow)
        {
            switch (flow)
            {
                case FlowOfTime.Paused: return "Paused";
                case FlowOfTime.Slow: return "Slow  (×½)";
                case FlowOfTime.Fast: return "Fast  (×2)";
                case FlowOfTime.VeryFast: return "Very fast  (×4)";
                default: return "Normal";
            }
        }

        /// <summary>
        /// Real minutes one in-game day lasts at <paramref name="timeScale"/> for a world day of
        /// <paramref name="dayLengthMinutes"/>; infinity when time is paused.
        /// </summary>
        public static float RealDayMinutes(float dayLengthMinutes, float timeScale)
        {
            if (timeScale <= 0f) return float.PositiveInfinity;
            return Math.Max(0.01f, dayLengthMinutes) / timeScale;
        }

        /// <summary>"A day lasts 24 minutes" / "about 1.5 hours" / "Time stands still".</summary>
        public static string DayLengthText(float dayLengthMinutes, float timeScale)
        {
            float minutes = RealDayMinutes(dayLengthMinutes, timeScale);
            if (float.IsInfinity(minutes)) return "Time stands still";
            if (minutes < 90f) return $"A day lasts {Math.Max(1, (int)Math.Round(minutes))} minutes";
            return $"A day lasts about {minutes / 60f:0.#} hours";
        }

        /// <summary>
        /// The hour applied when the slider (or a button) asks for <paramref name="requestedHour"/>: wrapped into [0, 24) with
        /// the slider's right end (24:00) meaning 23:59 of the same day, never the next midnight. The day is unchanged.
        /// </summary>
        public static float SliderHour(float requestedHour)
        {
            if (float.IsNaN(requestedHour)) return 0f;
            if (requestedHour >= DayClock.HoursPerDay) return DayClock.HoursPerDay - 1f / 60f;
            if (requestedHour <= 0f) return 0f;
            return requestedHour;
        }

        /// <summary>
        /// Applies the set-hour rule to <paramref name="clock"/>: moves it to <see cref="SliderHour"/>(<paramref name="hour"/>)
        /// on the same day. Returns the day (unchanged).
        /// </summary>
        public static int SetHourSameDay(DayClock clock, float hour)
        {
            if (clock == null) return 0;
            clock.SetTime(SliderHour(hour), false);
            return clock.Day;
        }

        /// <summary>
        /// Hour on a glide from <paramref name="from"/> to <paramref name="to"/> at progress <paramref name="t"/> (0..1, eased
        /// with a smoothstep). The glide stays inside the current day (straight across the day, as the slider would), so it
        /// never crosses midnight and never changes the day.
        /// </summary>
        public static float GlideHour(float from, float to, float t)
        {
            t = t <= 0f ? 0f : t >= 1f ? 1f : t;
            float k = t * t * (3f - 2f * t);
            return SliderHour(from + (to - from) * k);
        }

        /// <summary>"07:05" for <paramref name="hour"/> (24:00 is shown for the slider's right end).</summary>
        public static string FormatHour(float hour)
        {
            if (hour >= DayClock.HoursPerDay) return "24:00";
            int minute = DayClock.MinuteOfDay(hour);
            return $"{minute / 60:00}:{minute % 60:00}";
        }

        /// <summary>Menu label of a weather choice.</summary>
        public static string WeatherLabel(WeatherOverride choice)
        {
            switch (choice)
            {
                case WeatherOverride.Auto: return "As it comes";
                case WeatherOverride.LightRain: return "Light rain";
                default: return choice.ToString();
            }
        }

        /// <summary>Plain name of a weather state.</summary>
        public static string WeatherName(WeatherState state) => state == WeatherState.LightRain ? "Light rain" : state.ToString();
    }
}
