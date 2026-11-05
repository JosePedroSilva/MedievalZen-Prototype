using System;

namespace MedievalZen.Environment
{
    /// <summary>
    /// The small life of the season (2026-10-06): how many of each ambient effect <see cref="AmbientFxSpawner"/> keeps at a
    /// year of <see cref="Season"/>. Pure. At the identity (autumn) every count is exactly the spawner's own maximum and there
    /// is no blossom, so the spawner behaves as it did before seasons. The weather, time and elevation gates are the spawner's
    /// own and do not change with the season.
    /// </summary>
    public static class SeasonFx
    {
        /// <summary>
        /// Falling-leaf effects under the birches: all of <paramref name="max"/> in autumn, fading out as the year leaves it
        /// (through winter's place to spring, and back in from late summer); none in spring or high summer.
        /// </summary>
        public static int FallingLeaves(int max, float year)
        {
            if (Season.Wrap(year) == Season.Identity) return max;
            Season.Weights(year, out _, out _, out float autumn);
            return Scaled(max, autumn);
        }

        /// <summary>Blossom drifting from the fruit trees: <paramref name="max"/> at the height of spring, fewer either side, none in autumn.</summary>
        public static int Blossom(int max, float year)
        {
            if (Season.Wrap(year) == Season.Identity) return 0;
            Season.Weights(year, out float spring, out _, out _);
            return Scaled(max, spring);
        }

        /// <summary>Pollen and seed drifts: one more in spring (at most 4), as now in summer and autumn.</summary>
        public static int Pollen(int max, float year)
        {
            if (Season.Wrap(year) == Season.Identity) return max;
            Season.Weights(year, out float spring, out _, out _);
            return Math.Min(Math.Max(max, 4), max + (int)Math.Round(spring));
        }

        /// <summary>Fireflies on warm nights: half in spring, a quarter more in summer, as now in autumn.</summary>
        public static int Fireflies(int max, float year)
        {
            if (Season.Wrap(year) == Season.Identity) return max;
            return Math.Max(0, (int)Math.Round(Season.Blend(year, max * 0.5f, max * 1.25f, max)));
        }

        private static int Scaled(int max, float share)
        {
            if (max <= 0 || share <= 0f) return 0;
            return Math.Min(max, (int)Math.Round(max * share));
        }
    }
}
