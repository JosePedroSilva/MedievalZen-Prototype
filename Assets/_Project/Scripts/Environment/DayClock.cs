using System;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Pure in-game clock: hour of day (0 ≤ <see cref="Hour"/> &lt; 24) plus a day counter, advanced by real
    /// seconds at <see cref="DayLengthMinutes"/> real minutes per in-game day. Also hosts the sun-path maths.
    /// </summary>
    public sealed class DayClock
    {
        /// <summary>Hours per day.</summary>
        public const float HoursPerDay = 24f;

        /// <summary>Hour of sunrise (sun on the horizon, rising in the east).</summary>
        public const float Sunrise = 6f;

        /// <summary>Hour of sunset (sun on the horizon, setting in the west).</summary>
        public const float Sunset = 18f;

        private float dayLengthMinutes;

        /// <summary>Creates a clock at <paramref name="startHour"/> on day <paramref name="startDay"/>.</summary>
        public DayClock(float dayLengthMinutes, float startHour = 8f, int startDay = 1)
        {
            DayLengthMinutes = dayLengthMinutes;
            Day = startDay;
            Hour = Wrap(startHour);
        }

        /// <summary>Hour of the day, 0 (inclusive) to 24 (exclusive).</summary>
        public float Hour { get; private set; }

        /// <summary>Day counter (starts at 1 by default; increments at midnight).</summary>
        public int Day { get; private set; }

        /// <summary>Fraction of the day elapsed, 0..1.</summary>
        public float Progress => Hour / HoursPerDay;

        /// <summary>True while the sun is above the horizon.</summary>
        public bool IsDay => SunElevation(Hour) > 0f;

        /// <summary>Real-time minutes per in-game day (clamped to at least 0.01).</summary>
        public float DayLengthMinutes
        {
            get => dayLengthMinutes;
            set => dayLengthMinutes = Math.Max(0.01f, value);
        }

        /// <summary>In-game hours that pass per real second.</summary>
        public float HoursPerRealSecond => HoursPerDay / (dayLengthMinutes * 60f);

        /// <summary>Advances the clock by <paramref name="realSeconds"/> of real time (negative values are ignored).
        /// Returns the number of midnights crossed.</summary>
        public int Advance(float realSeconds)
        {
            if (realSeconds <= 0f) return 0;
            return AddHours(realSeconds * HoursPerRealSecond);
        }

        /// <summary>
        /// Moves the clock forward to <paramref name="hour"/>. If that hour is earlier than (or equal to) the current one and
        /// <paramref name="forwardOnly"/> is true, it is taken to be the next day (resting overnight advances the day count).
        /// </summary>
        public void SetTime(float hour, bool forwardOnly = true)
        {
            float target = Wrap(hour);
            if (forwardOnly && target <= Hour) Day++;
            Hour = target;
        }

        /// <summary>Adds in-game hours (may cross several midnights). Returns the number of midnights crossed.</summary>
        public int AddHours(float hours)
        {
            if (hours <= 0f) return 0;
            float total = Hour + hours;
            int days = (int)Math.Floor(total / HoursPerDay);
            Hour = Wrap(total);
            Day += days;
            return days;
        }

        /// <summary>
        /// Sun elevation in degrees for <paramref name="hour"/>: 0 at 06:00 and 18:00, <paramref name="maxElevation"/> at noon,
        /// −<paramref name="maxElevation"/> at midnight (a cosine over the day).
        /// </summary>
        public static float SunElevation(float hour, float maxElevation = 60f)
        {
            double angle = 2.0 * Math.PI * Wrap(hour) / HoursPerDay;
            return (float)(-Math.Cos(angle) * maxElevation);
        }

        /// <summary>Sun azimuth in degrees clockwise from north (+Z): 90 (east) at 06:00, 180 (south) at noon, 270 (west) at 18:00.</summary>
        public static float SunAzimuth(float hour)
        {
            return Wrap(hour) * (360f / HoursPerDay);
        }

        /// <summary>Wraps any hour into [0, 24).</summary>
        public static float Wrap(float hour)
        {
            float h = hour % HoursPerDay;
            if (h < 0f) h += HoursPerDay;
            if (h >= HoursPerDay) h = 0f;
            return h;
        }

        /// <summary>Whole minutes since midnight for <paramref name="hour"/> (0..1439).</summary>
        public static int MinuteOfDay(float hour)
        {
            return Math.Min(1439, (int)(Wrap(hour) * 60f));
        }
    }
}
