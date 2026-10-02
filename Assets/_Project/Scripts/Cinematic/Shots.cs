using System;

namespace MedievalZen.Cinematic
{
    /// <summary>The cinematic camera's shots: seven for walking (auto-travel) and six for resting (seated).</summary>
    public enum ShotType
    {
        /// <summary>Behind and above on the road, long lens, slow drift.</summary>
        TrailingWide = 0,
        /// <summary>Parallel to the road 8–20 m off, the traveller on a third, the landscape behind.</summary>
        SideTracking = 1,
        /// <summary>Ahead on the road looking back while the traveller approaches; the camera retreats slowly.</summary>
        Leading = 2,
        /// <summary>A fixed low point beside the road ahead: pans as the traveller passes, holds as they walk away.</summary>
        RoadsideTripod = 3,
        /// <summary>Rises from low behind to high and wide (or the reverse).</summary>
        Crane = 4,
        /// <summary>The best distant subject (a landmark, the road winding on, a lake, the pass) with the traveller small in front.</summary>
        Vista = 5,
        /// <summary>Near the ground beside the road, shallow angle; the traveller walks through the frame.</summary>
        LowGrass = 6,
        /// <summary>Seated: a slow push-in from behind towards what the traveller faces.</summary>
        RestPushIn = 7,
        /// <summary>Seated: a wide profile.</summary>
        RestProfile = 8,
        /// <summary>Seated: a high slow orbit.</summary>
        RestOrbit = 9,
        /// <summary>Seated: over the shoulder at the view.</summary>
        RestOverShoulder = 10,
        /// <summary>Seated, dawn or dusk: the sky and the horizon, the traveller a silhouette.</summary>
        RestSky = 11,
        /// <summary>Seated by a fire at night: low, across the flames.</summary>
        RestFire = 12,
    }

    /// <summary>Data that describes one kind of shot: how long it runs, its lens and how stiffly the camera follows.</summary>
    public readonly struct ShotDefinition
    {
        /// <summary>The shot.</summary>
        public readonly ShotType Type;
        /// <summary>Shortest and longest run in seconds (within 9–22).</summary>
        public readonly float MinSeconds, MaxSeconds;
        /// <summary>Narrowest and widest vertical field of view in degrees (within 28–60).</summary>
        public readonly float FovMin, FovMax;
        /// <summary>Smoothing time (s) of the camera position and of the look target.</summary>
        public readonly float PositionSmooth, LookSmooth;
        /// <summary>Lowest the camera may go above the ground (m).</summary>
        public readonly float GroundMargin;
        /// <summary>True for the seated (rest) shots.</summary>
        public readonly bool IsRest;

        /// <summary>Creates a definition.</summary>
        public ShotDefinition(ShotType type, float minSeconds, float maxSeconds, float fovMin, float fovMax, float positionSmooth, float lookSmooth,
            float groundMargin, bool isRest)
        {
            Type = type;
            MinSeconds = minSeconds;
            MaxSeconds = maxSeconds;
            FovMin = fovMin;
            FovMax = fovMax;
            PositionSmooth = positionSmooth;
            LookSmooth = lookSmooth;
            GroundMargin = groundMargin;
            IsRest = isRest;
        }
    }

    /// <summary>The shot table (one line per shot; tune here).</summary>
    public static class ShotLibrary
    {
        private static readonly ShotDefinition[] Table =
        {
            //                 type                         min   max  fovMin fovMax posSm lookSm ground rest
            new ShotDefinition(ShotType.TrailingWide,       12f, 20f,  30f,  40f,  1.1f, 0.6f,  0.8f, false),
            new ShotDefinition(ShotType.SideTracking,       10f, 18f,  32f,  44f,  1.0f, 0.6f,  0.8f, false),
            new ShotDefinition(ShotType.Leading,            10f, 16f,  34f,  46f,  0.9f, 0.5f,  0.8f, false),
            new ShotDefinition(ShotType.RoadsideTripod,      9f, 16f,  28f,  50f,  0.5f, 0.9f,  0.6f, false),
            new ShotDefinition(ShotType.Crane,              10f, 16f,  40f,  54f,  1.0f, 0.6f,  0.8f, false),
            new ShotDefinition(ShotType.Vista,              12f, 22f,  40f,  56f,  1.2f, 0.8f,  0.8f, false),
            new ShotDefinition(ShotType.LowGrass,            9f, 12f,  48f,  58f,  0.5f, 1.4f,  0.3f, false),
            new ShotDefinition(ShotType.RestPushIn,         14f, 20f,  38f,  46f,  1.4f, 0.9f,  0.6f, true),
            new ShotDefinition(ShotType.RestProfile,        12f, 20f,  32f,  40f,  1.4f, 0.9f,  0.6f, true),
            new ShotDefinition(ShotType.RestOrbit,          16f, 22f,  38f,  44f,  1.6f, 1.0f,  1.0f, true),
            new ShotDefinition(ShotType.RestOverShoulder,   12f, 20f,  42f,  50f,  1.2f, 0.9f,  0.5f, true),
            new ShotDefinition(ShotType.RestSky,            14f, 22f,  48f,  56f,  1.6f, 1.0f,  0.45f, true),
            new ShotDefinition(ShotType.RestFire,           12f, 18f,  40f,  46f,  1.2f, 0.8f,  0.5f, true),
        };

        /// <summary>The walking (auto-travel) shots.</summary>
        public static readonly ShotType[] TravelShots =
        {
            ShotType.TrailingWide, ShotType.SideTracking, ShotType.Leading, ShotType.RoadsideTripod, ShotType.Crane, ShotType.Vista, ShotType.LowGrass,
        };

        /// <summary>The seated (rest) shots.</summary>
        public static readonly ShotType[] RestShots =
        {
            ShotType.RestPushIn, ShotType.RestProfile, ShotType.RestOrbit, ShotType.RestOverShoulder, ShotType.RestSky, ShotType.RestFire,
        };

        /// <summary>Number of shot types.</summary>
        public const int Count = 13;

        /// <summary>The definition of <paramref name="type"/>.</summary>
        public static ShotDefinition Get(ShotType type)
        {
            int i = (int)type;
            if (i < 0 || i >= Table.Length) throw new ArgumentOutOfRangeException(nameof(type));
            return Table[i];
        }
    }

    /// <summary>What kind of country the traveller is in (weights the shot choice).</summary>
    public enum Scenery
    {
        /// <summary>Open meadow, farmland, downs.</summary>
        Open = 0,
        /// <summary>Woodland around the road.</summary>
        Forest = 1,
        /// <summary>A mountain pass or steep relief.</summary>
        Pass = 2,
        /// <summary>A lake beside the road.</summary>
        Lakeside = 3,
        /// <summary>Near a settlement.</summary>
        Settlement = 4,
    }

    /// <summary>The situation the shot scheduler weighs: country, time of day, weather and what is around.</summary>
    public struct ShotContext
    {
        /// <summary>The kind of country.</summary>
        public Scenery Scenery;
        /// <summary>Hour of day, 0..24.</summary>
        public float Hour;
        /// <summary>0 = clear sky, 1 = overcast or raining.</summary>
        public float Overcast;
        /// <summary>The traveller is seated (rest shots).</summary>
        public bool Seated;
        /// <summary>A camp fire is within a few metres.</summary>
        public bool NearFire;
        /// <summary>A landmark or long view exists for the vista shot.</summary>
        public bool HasVistaSubject;

        /// <summary>Dawn or dusk (golden hours).</summary>
        public bool GoldenHour => (Hour >= 5.5f && Hour <= 8.5f) || (Hour >= 17f && Hour <= 20f);

        /// <summary>Night.</summary>
        public bool Night => Hour >= 20.5f || Hour < 5.25f;

        /// <summary>
        /// Classifies the country from plain measurements: tree density around the traveller (trees per 100 m², averaged over a
        /// ring ~20 m out, since the road corridor itself is kept clear), whether a pass landmark is near or the ground around
        /// rises steeply (<paramref name="relief"/> m within 110 m), water within ~100 m, and a settlement within its radius + 60 m.
        /// A settlement wins, then a pass, then water, then forest. Thresholds from the endless generator, seed 12345: forest
        /// 0.3–0.6 trees/100 m² around the road, open country under 0.1; forest banks reach 80 m of relief, passes 90–100+.
        /// </summary>
        public static Scenery Classify(float treesPer100m2, bool nearPass, float relief, bool nearWater, bool nearSettlement)
        {
            if (nearSettlement) return Scenery.Settlement;
            if (nearPass || relief > 95f) return Scenery.Pass;
            if (nearWater) return Scenery.Lakeside;
            if (treesPer100m2 >= 0.3f) return Scenery.Forest;
            return Scenery.Open;
        }
    }

    /// <summary>
    /// Picks the next shot: deterministic for a seed (same contexts in the same order give the same shots), never the same
    /// type twice in a row, and weighted by the context (country, golden hours, night, weather, fire, vista subject).
    /// </summary>
    public sealed class ShotScheduler
    {
        private readonly Random random;
        private readonly float[] weights = new float[ShotLibrary.Count];

        /// <summary>A scheduler with its own seeded random stream.</summary>
        public ShotScheduler(int seed)
        {
            random = new Random(seed);
        }

        /// <summary>The shot chosen last (null before the first).</summary>
        public ShotType? Previous { get; private set; }

        /// <summary>The scheduler's random stream (shot plans draw their parameters from it too, keeping one deterministic stream).</summary>
        public Random Random => random;

        /// <summary>
        /// Relative weight of <paramref name="type"/> in <paramref name="context"/> (0 = never). Travel shots only when not seated,
        /// rest shots only when seated.
        /// </summary>
        public static float Weight(ShotType type, in ShotContext context)
        {
            bool rest = ShotLibrary.Get(type).IsRest;
            if (rest != context.Seated) return 0f;
            float w;
            if (!rest)
            {
                // Rows: Open, Forest, Pass, Lakeside, Settlement.
                switch (type)
                {
                    case ShotType.TrailingWide: w = Pick(context.Scenery, 1.0f, 1.2f, 1.0f, 0.9f, 0.9f); break;
                    case ShotType.SideTracking: w = Pick(context.Scenery, 1.0f, 0.5f, 1.1f, 1.4f, 0.8f); break;
                    case ShotType.Leading: w = Pick(context.Scenery, 0.8f, 1.0f, 0.8f, 0.8f, 1.1f); break;
                    case ShotType.RoadsideTripod: w = Pick(context.Scenery, 0.8f, 0.7f, 0.9f, 0.8f, 1.3f); break;
                    case ShotType.Crane: w = Pick(context.Scenery, 0.8f, 0.3f, 1.2f, 1.0f, 0.9f); break;
                    case ShotType.Vista: w = Pick(context.Scenery, 0.7f, 0.2f, 1.5f, 1.3f, 0.6f); break;
                    case ShotType.LowGrass: w = Pick(context.Scenery, 0.8f, 0.4f, 0.5f, 0.7f, 0.6f); break;
                    default: w = 0f; break;
                }

                if (context.GoldenHour)
                {
                    if (type == ShotType.Vista) w *= 1.5f;
                    else if (type == ShotType.Crane) w *= 1.3f;
                    else if (type == ShotType.SideTracking) w *= 1.2f;
                }
                else if (context.Night)
                {
                    if (type == ShotType.Vista) w *= 0.3f;
                    else if (type == ShotType.Crane || type == ShotType.LowGrass) w *= 0.5f;
                    else if (type == ShotType.TrailingWide) w *= 1.3f;
                    else if (type == ShotType.Leading) w *= 1.2f;
                }

                if (context.Overcast > 0.6f)
                {
                    if (type == ShotType.Vista) w *= 0.6f;
                    else if (type == ShotType.Crane) w *= 0.7f;
                    else if (type == ShotType.TrailingWide) w *= 1.2f;
                    else if (type == ShotType.Leading) w *= 1.1f;
                }

                if (type == ShotType.Vista && !context.HasVistaSubject) w *= 0.4f;
                return w;
            }

            switch (type)
            {
                case ShotType.RestPushIn: w = 1.0f; break;
                case ShotType.RestProfile: w = 1.0f; break;
                case ShotType.RestOrbit: w = context.Night ? 0.5f : 0.8f; break;
                case ShotType.RestOverShoulder: w = 1.2f; break;
                case ShotType.RestSky: w = context.GoldenHour ? 1.6f : 0f; break;
                case ShotType.RestFire: w = context.NearFire ? (context.Night ? 2.0f : 0.6f) : 0f; break;
                default: w = 0f; break;
            }

            return w;
        }

        private static float Pick(Scenery s, float open, float forest, float pass, float lake, float settlement)
        {
            switch (s)
            {
                case Scenery.Forest: return forest;
                case Scenery.Pass: return pass;
                case Scenery.Lakeside: return lake;
                case Scenery.Settlement: return settlement;
                default: return open;
            }
        }

        /// <summary>
        /// The next shot for <paramref name="context"/>: weighted random, excluding the previous type and
        /// <paramref name="alsoExclude"/> (a type that just failed). Falls back to any allowed type, then to the previous one.
        /// Call <see cref="Commit"/> with the shot actually used.
        /// </summary>
        public ShotType Next(in ShotContext context, ShotType? alsoExclude = null)
        {
            ShotType[] pool = context.Seated ? ShotLibrary.RestShots : ShotLibrary.TravelShots;
            float total = 0f;
            for (int i = 0; i < pool.Length; i++)
            {
                ShotType t = pool[i];
                float w = Weight(t, context);
                if ((Previous.HasValue && Previous.Value == t) || (alsoExclude.HasValue && alsoExclude.Value == t)) w = 0f;
                weights[i] = w;
                total += w;
            }

            if (total <= 0f)
            {
                for (int i = 0; i < pool.Length; i++)
                {
                    if (Previous.HasValue && Previous.Value == pool[i]) continue;
                    if (Weight(pool[i], context) > 0f) return pool[i];
                }

                return Previous ?? pool[0];
            }

            double pick = random.NextDouble() * total;
            for (int i = 0; i < pool.Length; i++)
            {
                pick -= weights[i];
                if (pick <= 0.0 && weights[i] > 0f) return pool[i];
            }

            for (int i = pool.Length - 1; i >= 0; i--)
            {
                if (weights[i] > 0f) return pool[i];
            }

            return pool[0];
        }

        /// <summary>Records <paramref name="type"/> as the shot in use (it will not be picked next).</summary>
        public void Commit(ShotType type)
        {
            Previous = type;
        }

        /// <summary>Clears the memory of the previous shot (e.g. after switching between travel and rest shots).</summary>
        public void Forget()
        {
            Previous = null;
        }

        /// <summary>A run length in seconds for <paramref name="type"/>, uniform in its range.</summary>
        public float Duration(ShotType type)
        {
            ShotDefinition d = ShotLibrary.Get(type);
            return d.MinSeconds + (float)random.NextDouble() * (d.MaxSeconds - d.MinSeconds);
        }
    }
}
