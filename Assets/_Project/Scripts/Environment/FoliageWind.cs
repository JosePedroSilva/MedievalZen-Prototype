using System;

namespace MedievalZen.Environment
{
    /// <summary>
    /// How the weather's wind reaches the Synty foliage shader (<c>PNB_Core/Shaders/Foliage</c>), as pure functions.
    /// <para>
    /// The shader reads three globals when a material has <c>_Use_Global_Weather_Controller</c> on (first-party copies only:
    /// the sward detail materials and the endless road's runtime tree copies): <c>_WindIntensity</c> (saturated, the lerp
    /// weight between the rest pose and the wind pose: a pure amplitude), <c>_GaleStrength</c> and <c>_WindDirection</c>
    /// (world, normalised in the shader after a world-to-object transform: a zero vector would give NaN positions). With the
    /// toggle off the shader uses constants: intensity 0.2, gale 0.2, direction object +X.
    /// </para>
    /// <para>
    /// The gale strength and the direction also set the <i>speed</i> of the sway: the strong-wind sine runs at
    /// <c>time × frequency × (gale + 2) × 1.5</c> and the breeze noise scrolls by <c>time × direction × f(gale)</c>, with the
    /// shader's time counting from scene start. Changing either while the game runs therefore changes the phase by
    /// <c>time × Δ</c>: after an hour of play a weather cross-fade would make the leaves race. So both stay constant (the
    /// shader's own values: <see cref="Gale"/> and a fixed direction) and only the intensity follows the weather and the
    /// gusts, which is always smooth.
    /// </para>
    /// </summary>
    public static class FoliageWind
    {
        /// <summary>The gale strength pushed to the shader: constant (see the class remarks), equal to the shader's built-in value.</summary>
        public const float Gale = 0.2f;

        /// <summary>The intensity the shader uses on its own (and what every material showed before the global wind).</summary>
        public const float BuiltInIntensity = 0.2f;

        /// <summary>Intensity in still air (weather wind 0).</summary>
        public const float CalmIntensity = 0.24f;

        /// <summary>Intensity at full weather wind (1), before gusts.</summary>
        public const float FullIntensity = 0.7f;

        /// <summary>Highest intensity ever pushed (a strong gust in the windiest weather).</summary>
        public const float MaxIntensity = 0.85f;

        /// <summary>
        /// Foliage wind intensity for the weather's <paramref name="weatherWind"/> (0..1; clear 0.3, overcast 0.45, rain 0.55)
        /// and a slow gust value <paramref name="gust"/> (0..1, the same envelope the wind sound uses): ×0.8 in a lull up to
        /// ×1.25 in a gust. Clear weather sways about 1.8× as much as the shader's fixed 0.2 on average.
        /// </summary>
        public static float Intensity(float weatherWind, float gust)
        {
            float w = Clamp01(weatherWind);
            float g = Clamp01(gust);
            float baseline = CalmIntensity + (FullIntensity - CalmIntensity) * w;
            return Math.Min(MaxIntensity, baseline * (0.8f + 0.45f * g));
        }

        /// <summary>
        /// The gust envelope shared by the foliage and the wind sound: smooth noise squared (lulls are long, gusts short).
        /// <paramref name="noise01"/> is a 0..1 smooth noise sample (Perlin).
        /// </summary>
        public static float GustFromNoise(float noise01)
        {
            float n = Clamp01(noise01);
            return n * n;
        }

        private static float Clamp01(float x) => float.IsNaN(x) ? 0f : Math.Min(1f, Math.Max(0f, x));
    }
}
