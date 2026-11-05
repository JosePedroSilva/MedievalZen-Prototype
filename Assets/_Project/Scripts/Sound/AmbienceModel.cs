using System;
using MedievalZen.Endless;

namespace MedievalZen.Sound
{
    /// <summary>What the ambience listens to at the traveller (all plain numbers; the director gathers them).</summary>
    public struct AmbienceInputs
    {
        /// <summary>The region the road is in.</summary>
        public RegionKind Region;

        /// <summary>Forest cover at the listener, 0 open .. 1 deep wood (the forest mood's canopy curve over the tree density).</summary>
        public float Cover;

        /// <summary>How exposed the listener is to the wind, 0 sheltered lowland .. 1 a high pass (see <see cref="AmbienceModel.Exposure"/>).</summary>
        public float Exposure;

        /// <summary>Sun elevation in degrees (negative below the horizon).</summary>
        public float SunElevation;

        /// <summary>True in the morning half of the day (the dawn chorus only rises with the sun).</summary>
        public bool Morning;

        /// <summary>The weather's wind, 0..1 (clear 0.3, misty 0.1, overcast 0.45, light rain 0.55).</summary>
        public float Wind;

        /// <summary>The weather's gust envelope, 0..1.</summary>
        public float Gust;

        /// <summary>The weather's rain, 0..1.</summary>
        public float Rain;

        /// <summary>The weather's mist amount, 0..1.</summary>
        public float Mist;
    }

    /// <summary>
    /// The ambience's rules, pure (2026-10-06): the user's direction is "like Death Stranding, where the music stops and there are long periods
    /// with just ambience", so the beds carry the game alone for tens of minutes. They must stay quiet and varied, never a wall of noise and
    /// never dead silent outdoors. <see cref="Targets"/> turns the inputs into one target weight (linear gain, 1 = the bed at
    /// <see cref="SoundMix.BedTargetDb"/>) per bed role of <see cref="AmbienceCatalog.Beds"/>; <see cref="AmbienceSmoother"/> eases the
    /// weights toward them.
    /// </summary>
    public static class AmbienceModel
    {
        /// <summary>The dawn chorus's lift on the day birds (dB), strongest from about −4° to +12° of a rising sun.</summary>
        public const float DawnChorusDb = 3f;

        /// <summary>Rain thins the birds by this much at full rain (dB).</summary>
        public const float RainThinDb = -6f;

        /// <summary>Mist thins the birds by this much at full mist (dB).</summary>
        public const float MistThinDb = -3f;

        /// <summary>The deep-forest layer's level (dB) once the cover is full.</summary>
        public const float DeepForestDb = -8f;

        /// <summary>Cover above which the deep-forest layer comes in.</summary>
        public const float DeepForestCover = 0.75f;

        /// <summary>The least cover the beds assume inside a Forest region.</summary>
        public const float ForestRegionCover = 0.45f;

        /// <summary>Least total bed power outdoors (sum of squared weights): about −8 dB below one bed. Never dead silent.</summary>
        public const float MinPower = 0.16f;

        /// <summary>Most total bed power (sum of squared weights): about +3.4 dB above one bed (a dawn chorus fits). Never a wall of noise.</summary>
        public const float MaxPower = 2.2f;

        /// <summary>Time constants of the weights (s): cover and birds, day and night, wind; and after a teleport or a streaming hold.</summary>
        public const float CoverSeconds = 5f, DaySeconds = 8f, WindSeconds = 4f, FastSeconds = 1.5f;

        /// <summary>Hermite smoothstep of x between a and b (0 below a, 1 above b; a &gt; b reverses).</summary>
        public static float SmoothStep(float a, float b, float x)
        {
            if (a == b) return x < a ? 0f : 1f;
            float t = (x - a) / (b - a);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        private static float Clamp01(float x) => float.IsNaN(x) ? 0f : x < 0f ? 0f : x > 1f ? 1f : x;

        /// <summary>Equal-power crossfade at t (0..1): <paramref name="a"/> fades out, <paramref name="b"/> fades in, a² + b² = 1.</summary>
        public static void EqualPower(float t, out float a, out float b)
        {
            t = Clamp01(t);
            a = (float)Math.Cos(t * Math.PI * 0.5);
            b = (float)Math.Sin(t * Math.PI * 0.5);
        }

        /// <summary>How much of the day birds are up, 0..1: a rising sun wakes them from about −8°, a setting one sends them to roost by about −6°.</summary>
        public static float Daylight(float sunElevation, bool morning) =>
            morning ? SmoothStep(-9f, -3f, sunElevation) : SmoothStep(-6f, 2f, sunElevation);

        /// <summary>How much of the night bed, 0..1: it comes in below about −2° and is full by −9°.</summary>
        public static float Nightness(float sunElevation) => SmoothStep(-2f, -9f, sunElevation);

        /// <summary>The dawn chorus, 0..1: strongest from −4° to +12° of a rising sun.</summary>
        public static float Chorus(float sunElevation, bool morning) =>
            morning ? SmoothStep(-8f, -4f, sunElevation) * SmoothStep(18f, 12f, sunElevation) : 0f;

        /// <summary>The birds' gain: daylight, the dawn chorus, thinned by rain and mist.</summary>
        public static float BirdGain(in AmbienceInputs i) =>
            Daylight(i.SunElevation, i.Morning)
            * SoundMix.DbToGain(DawnChorusDb * Chorus(i.SunElevation, i.Morning) + RainThinDb * Clamp01(i.Rain) + MistThinDb * Clamp01(i.Mist));

        /// <summary>
        /// Exposure to the wind, 0..1: a base by region (woods and farmland low, downs and passes high) raised by the height of the
        /// ground above <paramref name="baseHeight"/> (the road's height at the journey's start), lowered under trees.
        /// </summary>
        public static float Exposure(RegionKind region, float groundHeight, float baseHeight, float cover)
        {
            float b;
            switch (region)
            {
                case RegionKind.Forest: b = 0.15f; break;
                case RegionKind.Farmland: b = 0.25f; break;
                case RegionKind.LakeCountry: b = 0.35f; break;
                case RegionKind.Downs: b = 0.6f; break;
                case RegionKind.HighlandPass: b = 0.55f; break;
                default: b = 0.3f; break;
            }

            float lift = float.IsNaN(groundHeight) ? 0f : SmoothStep(40f, 220f, groundHeight - baseHeight);
            return Clamp01((b + 0.45f * lift) * (1f - 0.7f * Clamp01(cover)));
        }

        /// <summary>Fills <paramref name="weights"/> (length <see cref="AmbienceCatalog.BedCount"/>) with each bed's target weight.</summary>
        public static void Targets(in AmbienceInputs i, float[] weights)
        {
            if (weights == null || weights.Length < AmbienceCatalog.BedCount) throw new ArgumentException("weights");
            Array.Clear(weights, 0, AmbienceCatalog.BedCount);
            float cover = Clamp01(i.Cover), exposure = Clamp01(i.Exposure), rain = Clamp01(i.Rain), wind = Clamp01(i.Wind);
            bool pass = i.Region == RegionKind.HighlandPass;
            // In a Forest region the wood is never far even where the road runs through a clearing (seed 12345 measures 0–0.25 cover for
            // long stretches): its birds stay about half in.
            if (i.Region == RegionKind.Forest) cover = Math.Max(cover, ForestRegionCover);
            EqualPower(cover, out float open, out float wood);
            float birds = BirdGain(i);

            // Day: open country and woods crossfade with equal power on the cover.
            if (i.Region == RegionKind.Farmland)
            {
                // Farmland: its own farm bed and the open day share the open side with equal power.
                weights[(int)AmbienceRole.Farm] = open * 0.7071f * birds;
                weights[(int)AmbienceRole.OpenDay] = open * 0.7071f * birds;
            }
            else
            {
                // The top of a pass is wind and rock: the open day thins as the exposure climbs.
                weights[(int)AmbienceRole.OpenDay] = open * birds * (pass ? 1f - 0.6f * exposure : 1f);
            }

            if (pass)
            {
                // The pass's pines: their bed carries a little wind in the needles at any hour.
                weights[(int)AmbienceRole.Pines] = wood * Math.Max(birds, 0.4f);
            }
            else
            {
                weights[(int)AmbienceRole.ForestDay] = wood * birds;
            }

            float deep = SmoothStep(DeepForestCover, 0.95f, cover);
            float awake = Daylight(i.SunElevation, i.Morning);
            weights[(int)AmbienceRole.DeepForest] = pass ? 0f : deep * SoundMix.DbToGain(DeepForestDb) * (0.5f + 0.5f * awake);

            // Night: everywhere, a little softer in rain and up where the wind takes over.
            weights[(int)AmbienceRole.Night] = Nightness(i.SunElevation) * SoundMix.DbToGain(-4f * rain) * (1f - 0.45f * exposure);

            // Wind. A calm day leaves it nearly off: quiet matters.
            float gusty = Clamp01(wind + 0.2f * Clamp01(i.Gust) - 0.05f);
            float shelter = 1f - 0.6f * cover;
            weights[(int)AmbienceRole.WindLight] = SmoothStep(0.3f, 0.65f, gusty) * (1f - 0.6f * exposure) * shelter * 0.8f;
            weights[(int)AmbienceRole.WindHigh] = SmoothStep(0.35f, 0.85f, exposure) * (0.3f + 0.7f * SmoothStep(0.1f, 0.6f, gusty)) * 0.9f;
            weights[(int)AmbienceRole.WindStrong] = SmoothStep(0.55f, 0.8f, gusty) * (0.4f + 0.6f * exposure) * shelter * 0.7f;

            // Rain (synthesised).
            weights[(int)AmbienceRole.Rain] = rain * 0.9f;

            EnsureFloor(i, weights);
        }

        /// <summary>Sum of squared weights (the total bed power relative to one bed at full weight).</summary>
        public static float Power(float[] weights)
        {
            float p = 0f;
            for (int k = 0; k < AmbienceCatalog.BedCount; k++) p += weights[k] * weights[k];
            return p;
        }

        /// <summary>Keeps the total within [<see cref="MinPower"/>, <see cref="MaxPower"/>]: never silent, never a wall.</summary>
        private static void EnsureFloor(in AmbienceInputs i, float[] weights)
        {
            float p = Power(weights);
            if (p < MinPower)
            {
                // Dusk gaps and a calm sheltered spot: lift what is there, or fall back to the bed of the hour and the place.
                if (p > 1e-4f)
                {
                    float k = (float)Math.Sqrt(MinPower / p);
                    for (int n = 0; n < AmbienceCatalog.BedCount; n++) weights[n] *= k;
                }
                else
                {
                    int bed = i.SunElevation < -2f ? (int)AmbienceRole.Night
                        : i.Cover > 0.5f ? (i.Region == RegionKind.HighlandPass ? (int)AmbienceRole.Pines : (int)AmbienceRole.ForestDay)
                        : (int)AmbienceRole.OpenDay;
                    weights[bed] = (float)Math.Sqrt(MinPower);
                }
            }
            else if (p > MaxPower)
            {
                float k = (float)Math.Sqrt(MaxPower / p);
                for (int n = 0; n < AmbienceCatalog.BedCount; n++) weights[n] *= k;
            }
        }

        /// <summary>The time constant (s) a bed's weight follows its target with.</summary>
        public static float TimeConstant(int bed)
        {
            switch ((AmbienceRole)bed)
            {
                case AmbienceRole.WindLight:
                case AmbienceRole.WindHigh:
                case AmbienceRole.WindStrong:
                case AmbienceRole.Rain:
                    return WindSeconds;
                case AmbienceRole.Night:
                    return DaySeconds;
                default:
                    return CoverSeconds;
            }
        }

        // ------------------------------------------------------------------ positioned loops and spot sounds

        /// <summary>Daytime gain of the people's murmur (markets, inn yards): from mid-morning to dusk only, as the people's day.</summary>
        public static float PeopleDayGain(float hour) =>
            SmoothStep(7f, 9f, hour) * SmoothStep(20.5f, 19f, hour);

        /// <summary>Daytime gain of farm animals: up with the light, quiet at night.</summary>
        public static float FarmDayGain(float sunElevation) => 0.15f + 0.85f * SmoothStep(-6f, 4f, sunElevation);

        /// <summary>
        /// Mean gap between two spot sounds (s): 8–14 s in a wood by day, 15–30 s in the open; +∞ at night, in rain, or when the birds are thin.
        /// <paramref name="u"/> (0..1) picks within the range so it drifts over time.
        /// </summary>
        public static float SpotMeanGap(float cover, float birds, float rain, float u)
        {
            if (rain > 0.3f || birds < 0.25f) return float.PositiveInfinity;
            u = Clamp01(u);
            float wood = Clamp01(cover);
            float lo = 15f + (8f - 15f) * wood, hi = 30f + (14f - 30f) * wood;
            float gap = lo + (hi - lo) * u;
            return gap / Math.Min(1f, birds);
        }

        /// <summary>A Poisson process's next gap: exponential with the mean, from a uniform 0..1 draw (never under 2 s).</summary>
        public static float PoissonGap(float mean, double uniform)
        {
            if (float.IsInfinity(mean) || float.IsNaN(mean)) return float.PositiveInfinity;
            double u = Math.Min(Math.Max(uniform, 1e-6), 1.0 - 1e-9);
            return (float)Math.Max(2.0, -mean * Math.Log(1.0 - u));
        }

        /// <summary>A clip index in [0, count) never equal to <paramref name="last"/> when there is a choice.</summary>
        public static int PickOther(int count, int last, Random rng)
        {
            if (count <= 1) return 0;
            if (last < 0 || last >= count) return rng.Next(count);
            int k = rng.Next(count - 1);
            return k >= last ? k + 1 : k;
        }

        /// <summary>The spread (degrees) of a positioned loop at a distance: wide when the listener stands in it, point-like from 10 m.</summary>
        public static float SpreadAt(float distance) => 150f * SmoothStep(10f, 3f, distance);
    }

    /// <summary>The bed weights easing toward their targets (exponential approach; fast for a moment after a teleport or a hold).</summary>
    public sealed class AmbienceSmoother
    {
        private readonly float[] weights = new float[AmbienceCatalog.BedCount];
        private float fastFor;
        private bool primed;

        /// <summary>The current weights (do not modify).</summary>
        public float[] Weights => weights;

        /// <summary>The next <paramref name="seconds"/> follow with <see cref="AmbienceModel.FastSeconds"/> (a teleport, a streaming hold).</summary>
        public void Hurry(float seconds = 3f) => fastFor = Math.Max(fastFor, seconds);

        /// <summary>Jumps straight to the targets (the first frame).</summary>
        public void Snap(float[] targets)
        {
            Array.Copy(targets, weights, AmbienceCatalog.BedCount);
            primed = true;
        }

        /// <summary>One step of <paramref name="dt"/> seconds toward <paramref name="targets"/>.</summary>
        public void Step(float[] targets, float dt)
        {
            if (!primed)
            {
                Snap(targets);
                return;
            }

            if (!(dt > 0f)) return;
            dt = Math.Min(dt, 0.5f);
            bool fast = fastFor > 0f;
            fastFor = Math.Max(0f, fastFor - dt);
            for (int k = 0; k < AmbienceCatalog.BedCount; k++)
            {
                float tau = fast ? AmbienceModel.FastSeconds : AmbienceModel.TimeConstant(k);
                float a = 1f - (float)Math.Exp(-dt / tau);
                float t = float.IsNaN(targets[k]) ? 0f : targets[k];
                weights[k] += (t - weights[k]) * a;
                if (weights[k] < 1e-4f && t <= 0f) weights[k] = 0f;
            }
        }
    }
}
