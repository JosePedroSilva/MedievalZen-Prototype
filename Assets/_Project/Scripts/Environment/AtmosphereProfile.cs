using System;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// The fog "script" of a day, as curves over the hour (x = 0..24): thick low mist at dawn that burns off by mid-morning,
    /// a clear crisp noon with light aerial haze, golden haze at dusk and a thin cold mist at night. Pure data + evaluation
    /// (no scene access) so the clamps can be unit-tested for every hour and weather state.
    /// </summary>
    [Serializable]
    public sealed class AtmosphereProfile
    {
        [Tooltip("Distance at which the aerial haze reaches 50 % (metres) over the day. Larger = clearer.")]
        public AnimationCurve hazeHalfDistance = new AnimationCurve(
            new Keyframe(0f, 900f), new Keyframe(4f, 800f), new Keyframe(5.5f, 520f), new Keyframe(7f, 650f), new Keyframe(9f, 1300f),
            new Keyframe(12f, 2100f), new Keyframe(15f, 1800f), new Keyframe(17.5f, 1200f), new Keyframe(19f, 800f), new Keyframe(21f, 900f),
            new Keyframe(24f, 900f));

        [Tooltip("Height-fog (valley mist) density at the base altitude, per metre, over the day.")]
        public AnimationCurve mistDensity = new AnimationCurve(
            new Keyframe(0f, 0.004f), new Keyframe(4f, 0.006f), new Keyframe(5.5f, 0.011f), new Keyframe(7f, 0.009f), new Keyframe(8.5f, 0.004f),
            new Keyframe(10f, 0.0012f), new Keyframe(12f, 0.0006f), new Keyframe(16f, 0.0006f), new Keyframe(18f, 0.0015f),
            new Keyframe(19.5f, 0.0035f), new Keyframe(22f, 0.004f), new Keyframe(24f, 0.004f));

        [Tooltip("Height-fog falloff per metre over the day (larger = mist hugs the valley floor).")]
        public AnimationCurve mistFalloff = new AnimationCurve(
            new Keyframe(0f, 0.05f), new Keyframe(5.5f, 0.07f), new Keyframe(8f, 0.06f), new Keyframe(11f, 0.03f), new Keyframe(16f, 0.025f),
            new Keyframe(19f, 0.04f), new Keyframe(24f, 0.05f));

        [Tooltip("Sun (or moon) in-scattering strength over the day: the fog glows toward the light.")]
        public AnimationCurve scatterStrength = new AnimationCurve(
            new Keyframe(0f, 0.3f), new Keyframe(5f, 0.5f), new Keyframe(6.5f, 0.95f), new Keyframe(8f, 0.7f), new Keyframe(12f, 0.35f),
            new Keyframe(16f, 0.6f), new Keyframe(18f, 0.95f), new Keyframe(19.5f, 0.6f), new Keyframe(21f, 0.3f), new Keyframe(24f, 0.3f));

        [Tooltip("Base amount of low mist patches (0..1) over the day; the weather adds to it.")]
        public AnimationCurve patchAmount = new AnimationCurve(
            new Keyframe(0f, 0.35f), new Keyframe(4f, 0.55f), new Keyframe(6f, 1f), new Keyframe(8f, 0.7f), new Keyframe(10f, 0.15f),
            new Keyframe(16f, 0.1f), new Keyframe(18.5f, 0.5f), new Keyframe(20f, 0.6f), new Keyframe(24f, 0.35f));

        [Min(10f), Tooltip("The road must stay readable at least this far ahead (metres), even in the thickest mist.")]
        public float readableDistance = 60f;

        [Range(0.1f, 0.9f), Tooltip("Largest fog opacity allowed at the readable distance (worst case: a horizontal view at the base altitude).")]
        public float maxOpacityAtReadable = 0.5f;

        [Min(50f), Tooltip("The haze half-distance never drops below this (metres).")]
        public float minHazeHalfDistance = 150f;

        /// <summary>Largest height-fog falloff the evaluation returns (per metre).</summary>
        public const float MaxFalloff = 0.5f;

        /// <summary>Smallest height-fog falloff the evaluation returns (per metre).</summary>
        public const float MinFalloff = 0.005f;

        /// <summary>
        /// Fog for <paramref name="hour"/> under <paramref name="weather"/>, scaled by the two master sliders
        /// (<paramref name="fogAmount"/> multiplies densities, <paramref name="hazeDistance"/> multiplies the haze half-distance),
        /// clamped for readability. Also returns the scatter strength and the mist-patch amount (0..1).
        /// </summary>
        public FogSettings Evaluate(float hour, in WeatherPreset weather, float fogAmount, float hazeDistance, float baseAltitude,
            out float scatter, out float patches)
        {
            hour = DayClock.Wrap(hour);
            fogAmount = Mathf.Max(0f, fogAmount);
            float half = Mathf.Max(minHazeHalfDistance, Sample(hazeHalfDistance, hour, 1500f) * Mathf.Max(0.05f, hazeDistance));
            var fog = new FogSettings
            {
                hazeDensity = AtmosphereMath.HazeDensityForHalfDistance(half) * Mathf.Max(0f, weather.hazeMultiplier) * fogAmount,
                mistDensity = Mathf.Max(0f, Sample(mistDensity, hour, 0.002f)) * Mathf.Max(0f, weather.mistMultiplier) * fogAmount,
                mistFalloff = Mathf.Clamp(Sample(mistFalloff, hour, 0.04f), MinFalloff, MaxFalloff),
                baseAltitude = baseAltitude,
            };

            AtmosphereMath.ClampForReadability(ref fog, readableDistance, maxOpacityAtReadable);
            scatter = Mathf.Clamp(Sample(scatterStrength, hour, 0.5f) * Mathf.Max(0f, weather.scatterMultiplier), 0f, 1.5f);
            patches = Mathf.Clamp01(Mathf.Max(0f, Sample(patchAmount, hour, 0.3f)) * (0.5f + Mathf.Max(0f, weather.mistAmount)) * Mathf.Min(1.5f, fogAmount));
            return fog;
        }

        private static float Sample(AnimationCurve curve, float hour, float fallback)
        {
            if (curve == null || curve.length == 0) return fallback;
            float v = curve.Evaluate(hour);
            return float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;
        }
    }
}
