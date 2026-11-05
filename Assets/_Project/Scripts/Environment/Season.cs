using System;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>What the Journey menu offers for the season.</summary>
    public enum SeasonChoice
    {
        /// <summary>The season follows the journey's days.</summary>
        Auto = 0,

        /// <summary>Always spring.</summary>
        Spring = 1,

        /// <summary>Always summer.</summary>
        Summer = 2,

        /// <summary>Always early autumn: the look the game had before seasons, and the default.</summary>
        Autumn = 3,
    }

    /// <summary>
    /// The year, as far as the game has one (2026-10-06): three seasons on a loop, spring, summer and autumn. Winter comes later
    /// (the packs have no snow for ground, trees or roofs; the user agreed to "winter later").
    /// <para>
    /// The one place the seasonal systems meet. <see cref="Year"/> runs 0..3 and wraps: 0 is the middle of spring, 1 of summer,
    /// 2 of autumn. <b>Autumn is the identity</b>: the world as it looked before seasons existed (green oaks, orange birches,
    /// gold fields, the validated sky and weather) is exactly <see cref="Identity"/>, and every seasonal table, tint, weight
    /// and curve must return today's value there, to the bit where a test can check it. The default choice is fixed autumn, so
    /// nothing changes until the user picks another season or Auto.
    /// </para>
    /// <para>
    /// One system drives it (the season cycle: choice, day and hour in, <see cref="Set"/> out, once a frame); the others only
    /// read. Colour systems that are costly to update watch <see cref="Revision"/>, which moves only when the year has moved by
    /// <see cref="Step"/>.
    /// </para>
    /// </summary>
    public static class Season
    {
        /// <summary>The year's positions of the three seasons' middles.</summary>
        public const float Spring = 0f, Summer = 1f, Autumn = 2f;

        /// <summary>Length of the loop.</summary>
        public const float Length = 3f;

        /// <summary>Where every seasonal table is the identity: the look before seasons.</summary>
        public const float Identity = Autumn;

        /// <summary>Days of the journey one season lasts under <see cref="SeasonChoice.Auto"/>.</summary>
        public const float DaysPerSeason = 5f;

        /// <summary>The smallest move of the year that makes colour systems update (150 steps a year).</summary>
        public const float Step = 0.02f;

        /// <summary>The user's choice (Journey settings). Fixed autumn until they choose otherwise.</summary>
        public static SeasonChoice Choice = SeasonChoice.Autumn;

        /// <summary>Where the year stands, 0..3.</summary>
        public static float Year { get; private set; } = Identity;

        /// <summary>Moves each time <see cref="Year"/> has changed by at least <see cref="Step"/> since the last move.</summary>
        public static int Revision { get; private set; }

        private static float revisedAt = Identity;

        /// <summary>Sets the year (wrapped into 0..3). Called once a frame by the season cycle, and by tests.</summary>
        public static void Set(float year)
        {
            year = Wrap(year);
            Year = year;
            if (Distance(year, revisedAt) >= Step)
            {
                revisedAt = year;
                Revision++;
            }
        }

        /// <summary>
        /// The year for a choice at a day (1 = the journey's first) and hour: a fixed season's middle, or under Auto a year that
        /// starts at the identity on day 1 and moves on by one season every <see cref="DaysPerSeason"/> days (autumn, then spring).
        /// </summary>
        public static float YearFor(SeasonChoice choice, int day, float hour)
        {
            switch (choice)
            {
                case SeasonChoice.Spring: return Spring;
                case SeasonChoice.Summer: return Summer;
                case SeasonChoice.Autumn: return Autumn;
                default:
                    float days = Math.Max(0, day - 1) + Math.Min(Math.Max(hour, 0f), 24f) / 24f;
                    return Wrap(Identity + days / DaysPerSeason);
            }
        }

        /// <summary>The three seasons' weights at a year: triangular, each 1 at its middle, summing to 1.</summary>
        public static void Weights(float year, out float spring, out float summer, out float autumn)
        {
            year = Wrap(year);
            spring = Math.Max(0f, 1f - Distance(year, Spring));
            summer = Math.Max(0f, 1f - Distance(year, Summer));
            autumn = Math.Max(0f, 1f - Distance(year, Autumn));
            float sum = spring + summer + autumn;
            if (sum <= 0f)
            {
                autumn = 1f;
                return;
            }

            spring /= sum;
            summer /= sum;
            autumn /= sum;
        }

        /// <summary>A value that has one figure per season, at a year. At <see cref="Identity"/> it is exactly the autumn figure.</summary>
        public static float Blend(float year, float spring, float summer, float autumn)
        {
            if (Wrap(year) == Identity) return autumn;
            Weights(year, out float s, out float u, out float a);
            return spring * s + summer * u + autumn * a;
        }

        /// <summary>A colour or vector that has one figure per season, at a year. At <see cref="Identity"/> it is exactly the autumn figure.</summary>
        public static Vector3 Blend(float year, Vector3 spring, Vector3 summer, Vector3 autumn)
        {
            if (Wrap(year) == Identity) return autumn;
            Weights(year, out float s, out float u, out float a);
            return spring * s + summer * u + autumn * a;
        }

        /// <summary>The season in words, for the HUD and the journal: "Spring", "Early summer", "Late autumn" ...</summary>
        public static string Name(float year)
        {
            year = Wrap(year);
            int nearest = (int)Math.Floor(year + 0.5f) % 3;
            float offset = year - (float)Math.Floor(year + 0.5f);
            string name = nearest == 0 ? "spring" : nearest == 1 ? "summer" : "autumn";
            if (offset < -0.2f) return "Early " + name;
            if (offset > 0.2f) return "Late " + name;
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>A year wrapped into 0..3.</summary>
        public static float Wrap(float year)
        {
            if (float.IsNaN(year) || float.IsInfinity(year)) return Identity;
            year %= Length;
            return year < 0f ? year + Length : year;
        }

        /// <summary>The shorter way round the loop between two years, 0..1.5.</summary>
        public static float Distance(float a, float b)
        {
            float d = Math.Abs(Wrap(a) - Wrap(b));
            return d > Length * 0.5f ? Length - d : d;
        }

        /// <summary>Every Play starts at the identity until the cycle sets the year (the choice is the settings' and stays).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime()
        {
            Year = Identity;
            revisedAt = Identity;
            Revision = 0;
        }
    }
}
