using System;
using System.Globalization;

namespace MedievalZen.Player
{
    /// <summary>
    /// The player's walking pace: a multiplier on the walking speed, 0.5 to 1.5 in steps of 0.1 (1 = the validated speeds:
    /// walk 1.6 m/s, auto-travel 1.4 m/s with its slow variation). The mouse wheel (one step per notch) and the gamepad
    /// bumpers change it; the Journey menu shows it and <see cref="MedievalZen.UI.JourneySettings"/> keeps it. Sprint is not
    /// scaled: it stays the fixed fast speed. Pure: steps, clamping, names, labels and the wheel-notch logic are unit tested
    /// (<c>WalkPaceTests</c>); <see cref="PlayerController"/> and <see cref="MedievalZen.Travel.AutoTraveller"/> apply it.
    /// The pace is held as whole tenths, so every value is exactly one of the eleven steps and 1.0 is exactly 1f.
    /// </summary>
    public static class WalkPace
    {
        /// <summary>Slowest, default and fastest pace in tenths.</summary>
        public const int MinTenths = 5, DefaultTenths = 10, MaxTenths = 15;

        /// <summary>Slowest, default and fastest pace.</summary>
        public const float Min = 0.5f, Default = 1f, Max = 1.5f;

        /// <summary>One step of the wheel, the bumpers or the menu.</summary>
        public const float Step = 0.1f;

        /// <summary>Number of paces (0.5, 0.6 … 1.5).</summary>
        public const int Count = MaxTenths - MinTenths + 1;

        /// <summary>The pace in whole tenths, clamped to 5..15 (NaN → the default, ±infinity → the nearer end).</summary>
        public static int ToTenths(float pace)
        {
            if (float.IsNaN(pace)) return DefaultTenths;
            double tenths = Math.Round(Math.Max(MinTenths, Math.Min(MaxTenths, pace * 10.0)), MidpointRounding.AwayFromZero);
            return (int)tenths;
        }

        /// <summary>The pace for <paramref name="tenths"/> (clamped to 5..15).</summary>
        public static float FromTenths(int tenths) => Math.Max(MinTenths, Math.Min(MaxTenths, tenths)) / 10f;

        /// <summary><paramref name="pace"/> snapped to the nearest step and clamped to 0.5..1.5 (NaN → 1).</summary>
        public static float Clamp(float pace) => FromTenths(ToTenths(pace));

        /// <summary><paramref name="pace"/> moved by <paramref name="steps"/> steps (clamped at both ends).</summary>
        public static float Stepped(float pace, int steps) => FromTenths(ToTenths(pace) + steps);

        /// <summary>Index of <paramref name="pace"/> among the <see cref="Count"/> paces (0 = 0.5, 5 = 1.0, 10 = 1.5).</summary>
        public static int Index(float pace) => ToTenths(pace) - MinTenths;

        /// <summary>The pace at <paramref name="index"/> (clamped).</summary>
        public static float FromIndex(int index) => FromTenths(index + MinTenths);

        /// <summary>Amble (0.5–0.6), Stroll (0.7–0.9), Walk (1.0), Brisk (1.1–1.3), Stride (1.4–1.5).</summary>
        public static string Name(float pace)
        {
            int tenths = ToTenths(pace);
            if (tenths <= 6) return "Amble";
            if (tenths <= 9) return "Stroll";
            if (tenths == 10) return "Walk";
            if (tenths <= 13) return "Brisk";
            return "Stride";
        }

        /// <summary>The name and a speed, e.g. "Stroll · 1.1 m/s" (invariant culture: one decimal).</summary>
        public static string Label(float pace, float metresPerSecond)
        {
            if (float.IsNaN(metresPerSecond) || float.IsInfinity(metresPerSecond)) metresPerSecond = 0f;
            return Name(pace) + " · " + metresPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " m/s";
        }

        /// <summary>The menu's options: <see cref="Label"/> of every pace at <paramref name="walkSpeed"/> × pace.</summary>
        public static string[] Labels(float walkSpeed)
        {
            var labels = new string[Count];
            for (int i = 0; i < Count; i++)
            {
                float pace = FromIndex(i);
                labels[i] = Label(pace, walkSpeed * pace);
            }

            return labels;
        }

        /// <summary>
        /// The multiplier auto-travel applies: the walking benchmark's own speed scale when it is not 1 (the pace is then ignored
        /// so benchmarks stay comparable), else the clamped pace. Exactly 1f for pace 1 and benchmark scale 1.
        /// </summary>
        public static float TravelScale(float pace, float benchmarkScale)
        {
            if (benchmarkScale != 1f) return float.IsNaN(benchmarkScale) ? 1f : Math.Max(0.01f, benchmarkScale);
            return Clamp(pace);
        }

        /// <summary>
        /// The speed scale manual walking asks of the motor: the motor's sprint scale while sprinting (sprint stays the fixed
        /// fast speed), else the pace. Exactly 1f for pace 1.
        /// </summary>
        public static float ManualScale(float pace, bool sprinting, float sprintScale) => sprinting ? sprintScale : Clamp(pace);
    }

    /// <summary>
    /// Turns the mouse wheel into pace steps: one step per notch whatever the size of the delta (120 per notch on some
    /// platforms, 1 on others), and at most one step per <see cref="RepeatGap"/> seconds so a free-spinning wheel or a
    /// smooth-scrolling touchpad cannot race through the whole range in a few frames.
    /// </summary>
    public sealed class WalkPaceWheel
    {
        /// <summary>Scroll deltas smaller than this (either sign) are noise.</summary>
        public const float DeadZone = 0.01f;

        /// <summary>Shortest time between two steps from the wheel, seconds.</summary>
        public const float RepeatGap = 0.06f;

        private float sinceStep = float.PositiveInfinity;

        /// <summary>
        /// Feeds one frame's vertical scroll delta (up = positive) and the frame's unscaled time; returns +1 (one step faster),
        /// -1 (one step slower) or 0.
        /// </summary>
        public int Update(float scrollY, float deltaTime)
        {
            if (deltaTime > 0f && !float.IsNaN(deltaTime)) sinceStep += deltaTime;
            if (float.IsNaN(scrollY) || Math.Abs(scrollY) < DeadZone) return 0;
            if (sinceStep < RepeatGap) return 0;
            sinceStep = 0f;
            return scrollY > 0f ? 1 : -1;
        }

        /// <summary>Forgets the last step (the next notch steps at once).</summary>
        public void Reset() => sinceStep = float.PositiveInfinity;
    }
}
