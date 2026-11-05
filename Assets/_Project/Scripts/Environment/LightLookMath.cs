using System;

namespace MedievalZen.Environment
{
    /// <summary>A plain RGB triple (no engine types, so the light maths runs in the offline test harness).</summary>
    public readonly struct Tone3
    {
        /// <summary>Red.</summary>
        public readonly float R;

        /// <summary>Green.</summary>
        public readonly float G;

        /// <summary>Blue.</summary>
        public readonly float B;

        /// <summary>A triple.</summary>
        public Tone3(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>(1, 1, 1): a multiplier that changes nothing.</summary>
        public static Tone3 One => new Tone3(1f, 1f, 1f);

        /// <summary>(0.5, 0.5, 0.5): the neutral split-toning colour (soft light with 0.5 is the identity).</summary>
        public static Tone3 Grey => new Tone3(0.5f, 0.5f, 0.5f);

        /// <summary>Component-wise linear blend, t clamped to 0..1.</summary>
        public static Tone3 Lerp(in Tone3 a, in Tone3 b, float t)
        {
            t = Math.Min(1f, Math.Max(0f, t));
            return new Tone3(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        /// <summary>Rec. 709 luminance weights.</summary>
        public float Luma => 0.2126f * R + 0.7152f * G + 0.0722f * B;
    }

    /// <summary>What the rich look does to the lights this frame (multipliers; all 1 when the look is off).</summary>
    public struct RichLighting
    {
        /// <summary>Multiplier on the sun's intensity (by day only; the moon is never changed).</summary>
        public float SunScale;

        /// <summary>Multiplier on the ambient sky colour (cooler and a little lower by day: deeper, bluer shade).</summary>
        public Tone3 AmbientSky;

        /// <summary>Multiplier on the ambient horizon colour.</summary>
        public Tone3 AmbientEquator;

        /// <summary>Multiplier on the ambient ground colour (warm bounce kept).</summary>
        public Tone3 AmbientGround;
    }

    /// <summary>What the rich look adds to the time-of-day grade this frame (deltas; zero / neutral when the look is off).</summary>
    public struct RichGrade
    {
        /// <summary>Added to ColorAdjustments.contrast.</summary>
        public float Contrast;

        /// <summary>Added to ColorAdjustments.saturation.</summary>
        public float Saturation;

        /// <summary>Added to WhiteBalance.temperature.</summary>
        public float Temperature;

        /// <summary>Added to Bloom.intensity.</summary>
        public float BloomIntensity;

        /// <summary>Multiplier on the profile's Bloom.threshold (below 1 at golden hours: the sun-lit haze glows).</summary>
        public float BloomThresholdScale;

        /// <summary>SplitToning.shadows (sRGB; grey = neutral).</summary>
        public Tone3 Shadows;

        /// <summary>SplitToning.highlights (sRGB; grey = neutral).</summary>
        public Tone3 Highlights;

        /// <summary>Added to ColorAdjustments.postExposure, EV (forest pass: a little darker under the canopy; 0 otherwise).</summary>
        public float Exposure;
    }

    /// <summary>
    /// The "Rich light" look as pure functions of the sun elevation and the weather, so it can be tested offline and toggled
    /// at runtime (Journey menu → Sky → Rich light). By day the sun is a little stronger and the ambient a little lower and
    /// cooler (more form, blue-ish shade), the grade gains contrast and saturation and a gentle cool-shadow / warm-highlight
    /// split; at the golden hours the sun, the warmth and the bloom on sun-lit haze rise further. At night the lights are
    /// exactly as before (the night stays as readable) and the grade adds only +2 contrast and a faint cool tone in the shade.
    /// Every value is continuous in the elevation, so dawn and dusk never jump.
    /// With <c>rich</c> = 0 every result is exactly neutral (the old look).
    /// </summary>
    public static class LightLookMath
    {
        /// <summary>
        /// Split-toning colour for the shadows by day (cool). Forest floor pass (2026-10-06): was (0.45, 0.49, 0.56); in gamma space the
        /// soft-light blend of a blue above 0.5 lifts the blue of near-black colours by about a fifth (the dark oak leaves in shade,
        /// linear ≈ 0.02) and turned shaded crowns blue-grey; (0.47, 0.49, 0.52) keeps a cool shade with a third of the blue push.
        /// </summary>
        public static readonly Tone3 CoolShadows = new Tone3(0.47f, 0.49f, 0.52f);

        /// <summary>Split-toning colour for the highlights by day (warm).</summary>
        public static readonly Tone3 WarmHighlights = new Tone3(0.555f, 0.525f, 0.47f);

        /// <summary>Split-toning colour for the highlights at the golden hours (warmer).</summary>
        public static readonly Tone3 GoldenHighlights = new Tone3(0.615f, 0.53f, 0.42f);

        /// <summary>Ambient sky multiplier at full day (≈ −10 % luminance, cooler).</summary>
        public static readonly Tone3 DaySky = new Tone3(0.86f, 0.9f, 0.98f);

        /// <summary>Ambient horizon multiplier at full day.</summary>
        public static readonly Tone3 DayEquator = new Tone3(0.9f, 0.9f, 0.93f);

        /// <summary>Ambient ground multiplier at full day (the warm bounce is kept).</summary>
        public static readonly Tone3 DayGround = new Tone3(0.95f, 0.92f, 0.87f);

        /// <summary>Sun gain at full day (+10 %).</summary>
        public const float DaySunGain = 0.10f;

        /// <summary>Extra sun gain at the golden hours (+15 %).</summary>
        public const float GoldenSunGain = 0.15f;

        /// <summary>The day cycle's sun ramp (degrees over which the sun fades in from the horizon; its default).</summary>
        public const float SunRampDegrees = 14f;

        /// <summary>0 at night, 1 once the sun is 8° up (smooth).</summary>
        public static float DayWeight(float sunElevation) => SmoothRange(-1f, 8f, sunElevation);

        /// <summary>
        /// How strong the sun itself is, 0..1: the day cycle's own ramp (smoothstep over <see cref="SunRampDegrees"/>). The
        /// ambient is lowered only as far as the sun makes up for it, so low-sun scenes do not get darker overall.
        /// </summary>
        public static float SunWeight(float sunElevation) => Smooth(sunElevation / SunRampDegrees);

        /// <summary>0 by day, 1 once the sun is 10° below the horizon (the same curve the grading has always used for night).</summary>
        public static float NightWeight(float sunElevation) => SmoothRange(-1f, -10f, sunElevation);

        /// <summary>
        /// Golden-hour weight: a bump around 6° of sun elevation (half-width 12°), reduced by overcast. The same curve as the
        /// time-of-day grading, so the rich look's golden extras line up with it.
        /// </summary>
        public static float GoldenWeight(float sunElevation, float overcast)
        {
            return Bump(sunElevation, 6f, 12f) * (1f - 0.7f * Clamp01(overcast));
        }

        /// <summary>The light multipliers for <paramref name="rich"/> (0 = old look, 1 = rich) at this sun elevation and overcast.</summary>
        public static RichLighting Lighting(float rich, float sunElevation, float overcast)
        {
            rich = Clamp01(rich);
            float day = DayWeight(sunElevation);
            float golden = GoldenWeight(sunElevation, overcast);
            float w = rich * SunWeight(sunElevation);
            return new RichLighting
            {
                SunScale = 1f + rich * (DaySunGain * day + GoldenSunGain * golden),
                AmbientSky = Tone3.Lerp(Tone3.One, DaySky, w),
                AmbientEquator = Tone3.Lerp(Tone3.One, DayEquator, w),
                AmbientGround = Tone3.Lerp(Tone3.One, DayGround, w),
            };
        }

        /// <summary>The grade deltas for <paramref name="rich"/> (0 = old look, 1 = rich) at this sun elevation and weather.</summary>
        public static RichGrade Grade(float rich, float sunElevation, float overcast, float mist)
        {
            rich = Clamp01(rich);
            overcast = Clamp01(overcast);
            float day = DayWeight(sunElevation);
            float night = NightWeight(sunElevation);
            float golden = GoldenWeight(sunElevation, overcast);
            float clear = 1f - 0.6f * overcast;
            Tone3 warm = Tone3.Lerp(WarmHighlights, GoldenHighlights, golden);
            return new RichGrade
            {
                Contrast = rich * (2f + 6f * day) * (1f - 0.4f * Clamp01(mist)),
                Saturation = rich * (6f * day + 4f * golden),
                Temperature = rich * 6f * golden,
                BloomIntensity = rich * (0.05f * day + 0.18f * golden),
                BloomThresholdScale = 1f - rich * 0.12f * golden,
                Shadows = Tone3.Lerp(Tone3.Grey, CoolShadows, rich * (0.85f * day + 0.5f * night) * (1f - 0.5f * overcast)),
                Highlights = Tone3.Lerp(Tone3.Grey, warm, rich * day * clear),
            };
        }

        /// <summary>White-balance warmth added under a full canopy by day (forest pass).</summary>
        public const float ForestWarmth = 5f;

        /// <summary>Exposure under a full canopy by day, EV (forest pass; −0.15 until the forest floor pass, whose darker floor does the rest).</summary>
        public const float ForestExposure = -0.08f;

        /// <summary>Contrast taken back under a full canopy by day (forest floor pass): the shade of the crowns opens a little.</summary>
        public const float ForestContrast = -4f;

        /// <summary>
        /// Split-toning colour of the shadows under a full canopy by day (forest floor pass): a green above 0.5 and a blue below it, so
        /// the soft-light blend lifts the shaded leaves' green (about +30 % linear on the darkest) and holds their blue back.
        /// </summary>
        public static readonly Tone3 ForestShadows = new Tone3(0.5f, 0.57f, 0.47f);

        /// <summary>Saturation added under a full canopy by day (forest pass).</summary>
        public const float ForestSaturation = 3f;

        /// <summary>
        /// Forest pass (2026-10-05): the rich look's term under the trees, added to <paramref name="grade"/>: by day, under a canopy of
        /// <paramref name="canopy"/> (0..1, <see cref="ForestMood.Canopy"/>), a warmer white balance, a little less exposure and a
        /// little more saturation; less in overcast, none at night. With <paramref name="rich"/> = 0 or no canopy nothing changes, so
        /// the toggle still returns the old look exactly.
        /// </summary>
        public static void AddForest(ref RichGrade grade, float rich, float canopy, float sunElevation, float overcast)
        {
            float w = Clamp01(rich) * Clamp01(canopy) * DayWeight(sunElevation) * (1f - 0.5f * Clamp01(overcast));
            if (w <= 0f) return;
            grade.Temperature += ForestWarmth * w;
            grade.Exposure += ForestExposure * w;
            grade.Saturation += ForestSaturation * w;
            grade.Contrast += ForestContrast * w;
            grade.Shadows = Tone3.Lerp(grade.Shadows, ForestShadows, w);
        }

        /// <summary>
        /// Forest floor pass (2026-10-06): luminance and colour (linear) of a dark oak leaf (albedo <paramref name="albedo"/>) after the
        /// light and the grade, lit (facing the sun at <paramref name="facing"/> = N·L) or in its crown's shade (ambient only, times
        /// <paramref name="occlusion"/>). The model: the day cycle's noon sun (1.35 ramped in) times the rich sun gain, the ambient sky
        /// colour (0.58, 0.66, 0.78) times the rich ambient multiplier, exposure, contrast as a power about mid-grey (0.18), then URP's
        /// split toning (soft light in gamma 2.2, shadows weighted by 1 − luminance, highlights by luminance). Fog and SSAO are left out.
        /// Pure; used by the tests and the docs.
        /// </summary>
        public static Tone3 LeafAfterGrade(Tone3 albedo, bool lit, float rich, float canopy, float sunElevation, float facing = 0.6f, float occlusion = 0.7f)
        {
            RichLighting l = Lighting(rich, sunElevation, 0f);
            RichGrade g = Grade(rich, sunElevation, 0f, 0f);
            AddForest(ref g, rich, canopy, sunElevation, 0f);
            float sun = lit ? 1.35f * SunWeight(sunElevation) * l.SunScale * facing : 0f;
            var amb = new Tone3(0.58f * l.AmbientSky.R * occlusion, 0.66f * l.AmbientSky.G * occlusion, 0.78f * l.AmbientSky.B * occlusion);
            float ev = (float)Math.Pow(2.0, g.Exposure);
            float k = 1f + g.Contrast / 100f;
            float Channel(float a, float light) => 0.18f * (float)Math.Pow(Math.Max(1e-6f, a * light * ev) / 0.18f, k);
            var c = new Tone3(Channel(albedo.R, sun + amb.R), Channel(albedo.G, sun + amb.G), Channel(albedo.B, sun + amb.B));
            var x = new Tone3((float)Math.Pow(c.R, 1 / 2.2), (float)Math.Pow(c.G, 1 / 2.2), (float)Math.Pow(c.B, 1 / 2.2));
            float t = Clamp01(0.2126f * x.R + 0.7152f * x.G + 0.0722f * x.B);
            Tone3 sh = Tone3.Lerp(Tone3.Grey, g.Shadows, 1f - t), hi = Tone3.Lerp(Tone3.Grey, g.Highlights, t);
            x = new Tone3(SoftLight(SoftLight(x.R, sh.R), hi.R), SoftLight(SoftLight(x.G, sh.G), hi.G), SoftLight(SoftLight(x.B, sh.B), hi.B));
            return new Tone3((float)Math.Pow(x.R, 2.2), (float)Math.Pow(x.G, 2.2), (float)Math.Pow(x.B, 2.2));
        }

        /// <summary>The Meadow oak leaf's mean albedo (linear; dark, about 0.05 green).</summary>
        public static readonly Tone3 OakLeaf = new Tone3(0.045f, 0.053f, 0.006f);

        /// <summary>Shaded / lit luminance of the oak leaf (see <see cref="LeafAfterGrade"/>).</summary>
        public static float ShadedLeafRatio(float rich, float canopy, float sunElevation) =>
            LeafAfterGrade(OakLeaf, false, rich, canopy, sunElevation).Luma / Math.Max(1e-6f, LeafAfterGrade(OakLeaf, true, rich, canopy, sunElevation).Luma);

        private static float SoftLight(float b, float s)
        {
            if (s <= 0.5f) return b - (1f - 2f * s) * b * (1f - b);
            float d = b <= 0.25f ? ((16f * b - 12f) * b + 4f) * b : (float)Math.Sqrt(b);
            return b + (2f * s - 1f) * (d - b);
        }

        /// <summary>
        /// Relative brightness of flat, sun-lit ground at a given sun elevation under the rich lighting vs the old (1 = unchanged):
        /// sun × ramp × sin(elevation) + ambient luminance, with the day cycle's noon sun (1.35, ramped in over 14°) and its daytime
        /// ambient sky colour (0.58, 0.66, 0.78). Used by the tests to keep the daytime exposure where it was.
        /// </summary>
        public static float LitGroundRatio(float sunElevation, float sunIntensity = 1.35f)
        {
            var ambient = new Tone3(0.58f, 0.66f, 0.78f);
            RichLighting rich = Lighting(1f, sunElevation, 0f);
            float sun = sunIntensity * SunWeight(sunElevation) * (float)Math.Max(0.0, Math.Sin(sunElevation * Math.PI / 180.0));
            var lit = new Tone3(ambient.R * rich.AmbientSky.R, ambient.G * rich.AmbientSky.G, ambient.B * rich.AmbientSky.B);
            float before = sun + ambient.Luma;
            float after = sun * rich.SunScale + lit.Luma;
            return after / Math.Max(1e-4f, before);
        }

        private static float Clamp01(float x) => float.IsNaN(x) ? 0f : Math.Min(1f, Math.Max(0f, x));

        private static float Smooth(float x)
        {
            x = Math.Min(1f, Math.Max(0f, x));
            return x * x * (3f - 2f * x);
        }

        private static float SmoothRange(float a, float b, float x)
        {
            if (Math.Abs(b - a) < 1e-6f) return x >= b ? 1f : 0f;
            return Smooth((x - a) / (b - a));
        }

        private static float Bump(float x, float centre, float width) => 1f - Smooth(Math.Abs(x - centre) / Math.Max(width, 1e-4f));
    }
}
