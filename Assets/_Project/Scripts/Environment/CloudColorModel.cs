using System;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>What the cloud colours are derived from (all colours gamma-space, like the <see cref="DayCycle"/> gradients).</summary>
    public struct CloudLightInput
    {
        /// <summary>Skydome horizon colour this frame (after the overcast greying).</summary>
        public Color skyHorizon;

        /// <summary>Skydome zenith colour this frame (after the overcast greying).</summary>
        public Color skyZenith;

        /// <summary>Atmosphere fog colour this frame (<see cref="DayCycle.CurrentFogColor"/>).</summary>
        public Color fogColor;

        /// <summary>Sun colour for the hour (before intensity).</summary>
        public Color sunColor;

        /// <summary>Moonlight colour.</summary>
        public Color moonColor;

        /// <summary>Sun elevation in degrees (negative at night).</summary>
        public float sunElevation;

        /// <summary>0..1 weather overcast.</summary>
        public float overcast;

        /// <summary>0..1 haze / mist around the clouds (<see cref="AtmosphereController.CloudHaze"/>, via <see cref="DayCycle.CloudHaze"/>).</summary>
        public float mist;

        /// <summary>Weather multiplier on the cloud body, applied before the haze pull (white = unchanged; <see cref="DayCycle.CloudTintMultiplier"/>).</summary>
        public Color tint;
    }

    /// <summary>The artistic controls of the cloud look.</summary>
    public struct CloudLookSettings
    {
        /// <summary>Largest HSV saturation any cloud colour may have (0 = grey clouds, 1 = uncapped).</summary>
        public float saturation;

        /// <summary>0..1: how much darker the shaded side is than the lit side in clear weather.</summary>
        public float contrast;

        /// <summary>0..2: how strongly the clouds melt into the atmosphere (1 = as designed).</summary>
        public float softness;

        /// <summary>
        /// Keep the Synty shader's view-dependent scattering term. Off by default: it lerps each colour channel with a
        /// different weight (main light direction × fresnel), which tints the clouds whatever colours are pushed.
        /// </summary>
        public bool scattering;

        /// <summary>The designed defaults.</summary>
        public static CloudLookSettings Default => new CloudLookSettings { saturation = 0.3f, contrast = 0.6f, softness = 1f, scattering = false };
    }

    /// <summary>The Synty cloud shader colours for one frame.</summary>
    public struct CloudColors
    {
        /// <summary><c>_Base_Color</c>: the side facing away from the light.</summary>
        public Color baseColor;

        /// <summary><c>_Top_Color</c>: the side facing the light.</summary>
        public Color topColor;

        /// <summary><c>_Fresnel_Color</c>: the rim of the shaded side.</summary>
        public Color fresnelColor;

        /// <summary><c>_Scattering_Color</c> (only used when the shader's scattering is enabled).</summary>
        public Color scatteringColor;

        /// <summary>The colour overcast and mist pull the clouds toward (the fog colour, capped to the cloud saturation).</summary>
        public Color fogTarget;

        /// <summary>0..0.9: how far the clouds were pulled toward <see cref="fogTarget"/>.</summary>
        public float fogWeight;
    }

    /// <summary>
    /// Pure cloud colour model (no UnityEngine calls besides the <see cref="Color"/> struct, so it runs outside the editor).
    /// The Synty cloud shader (<c>PNB_Core/Shaders/Clouds.shadergraph</c>, URP Unlit, opaque) picks <c>_Top_Color</c> where a
    /// face points at the main light, <c>_Base_Color</c> elsewhere and <c>_Fresnel_Color</c> on the rim of the shaded side,
    /// then (<c>_Enable_Fog</c>) lerps toward <c>unity_FogColor</c> by <c>(2 · _Fog_Density − 0.5) + built-in fog density</c>
    /// — unclamped, so it can overshoot — and finally (<c>_Enable_Scattering</c>) toward <c>_Scattering_Color</c>.
    /// <see cref="DayCycle"/> switches both branches off per renderer, so those three colours are the whole output and
    /// capping them here caps what reaches the screen (the full-screen fog pass then adds the aerial perspective on top).
    /// <para>Lit side: near-white tinted by the sun (warm only around sunrise/sunset), dim moonlit blue-grey at night.
    /// Shaded side: a desaturated mix of the sky horizon, fog and zenith colours, darker than the lit side by
    /// <see cref="CloudLookSettings.contrast"/>. Overcast and mist (× softness) pull everything toward the fog colour, which
    /// lowers the contrast; every colour is finally capped to <see cref="CloudLookSettings.saturation"/>.</para>
    /// </summary>
    public static class CloudColorModel
    {
        /// <summary>Largest weight with which weather pulls the clouds into the fog (clouds never vanish completely).</summary>
        public const float MaxFogWeight = 0.9f;

        /// <summary>Luminance (Rec. 709 weights) of a colour.</summary>
        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>HSV saturation, (max − min) / max (0 for black).</summary>
        public static float Saturation(Color c)
        {
            float max = Math.Max(c.r, Math.Max(c.g, c.b));
            float min = Math.Min(c.r, Math.Min(c.g, c.b));
            return max > 1e-5f ? (max - min) / max : 0f;
        }

        /// <summary>
        /// Lowers the HSV saturation of <paramref name="c"/> to at most <paramref name="cap"/>, keeping its hue and its
        /// brightest channel. Colours already under the cap are returned unchanged.
        /// </summary>
        public static Color CapSaturation(Color c, float cap)
        {
            cap = Clamp01(cap);
            float max = Math.Max(c.r, Math.Max(c.g, c.b));
            float s = Saturation(c);
            if (s <= cap || max <= 1e-5f) return c;
            float k = cap / s;
            return new Color(max - (max - c.r) * k, max - (max - c.g) * k, max - (max - c.b) * k, c.a);
        }

        /// <summary>Same hue and saturation, brightest channel set to <paramref name="value"/>.</summary>
        public static Color WithValue(Color c, float value)
        {
            float max = Math.Max(c.r, Math.Max(c.g, c.b));
            if (max <= 1e-5f) return new Color(value, value, value, 1f);
            float k = value / max;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        /// <summary>Evaluates the cloud colours for <paramref name="input"/> with the look <paramref name="look"/>.</summary>
        public static CloudColors Evaluate(in CloudLightInput input, in CloudLookSettings look)
        {
            float cap = Clamp01(look.saturation);
            float contrast = Clamp01(look.contrast);
            float softness = Math.Max(0f, Math.Min(2f, look.softness));
            float overcast = Clamp01(input.overcast);
            float mist = Clamp01(input.mist);
            float elevation = input.sunElevation;

            // 0 at night → 1 by day (clouds keep catching light a little after sunset); golden = warm light near the horizon.
            float day = AtmosphereMath.SmoothRange(-9f, 6f, elevation);
            float golden = AtmosphereMath.Bump(elevation, 2f, 12f) * AtmosphereMath.SmoothRange(-9f, -2f, elevation);
            float shade = 1f - 0.5f * contrast;   // shaded side value relative to the lit side

            // Lit side. By day a near-white warmed by the sun (a lot more at golden hours); at night the moon on the fog.
            Color white = Color.white;
            Color sunTint = Color.Lerp(white, input.sunColor, 0.2f + 0.6f * golden);
            float dayValue = 0.95f - 0.07f * golden - 0.2f * AtmosphereMath.SmoothRange(4f, -9f, elevation);
            Color dayTop = WithValue(CapSaturation(sunTint, cap), dayValue);
            Color moonlit = Color.Lerp(Grey(input.fogColor), input.moonColor, 0.3f);
            Color nightTop = WithValue(CapSaturation(moonlit, cap * 0.8f), 0.24f);
            Color top = Color.Lerp(nightTop, dayTop, day);

            // Shaded side: the sky light it receives — horizon and fog, a little zenith blue — desaturated and darker.
            Color skyLight = Color.Lerp(Color.Lerp(input.skyHorizon, input.fogColor, 0.5f), input.skyZenith, 0.3f);
            skyLight = Color.Lerp(skyLight, Grey(skyLight), 0.4f);
            Color dayBase = WithValue(CapSaturation(skyLight, cap), dayValue * shade);
            Color nightBase = WithValue(nightTop, 0.24f * shade);
            Color baseColor = Color.Lerp(nightBase, dayBase, day);
            // The weather tint darkens / cools the cloud body before the haze pull, so overcast clouds end up a pale grey
            // close to the fog rather than darker than the sky around them.
            Color tint = input.tint.maxColorComponent > 0f ? input.tint : white;
            tint.a = 1f;
            top = Finish(top * tint, cap);
            baseColor = Finish(baseColor * tint, cap);

            // Weather: overcast and mist (and a little haze always, scaled by softness) pull both sides into the fog.
            // The fog target is the fog colour made paler; the same weight on both sides shrinks the contrast. Both ends
            // of the lerp are capped, so the result is too (the cap is preserved by convex combinations).
            Color fogTarget = CapSaturation(Color.Lerp(input.fogColor, Grey(input.fogColor), 0.3f), cap * 0.7f);
            fogTarget = new Color(Clamp01(fogTarget.r), Clamp01(fogTarget.g), Clamp01(fogTarget.b), 1f);
            float weight = Math.Min(MaxFogWeight, softness * (0.12f + 0.4f * overcast + 0.4f * mist));
            top = Finish(Color.Lerp(top, fogTarget, weight), cap);
            baseColor = Finish(Color.Lerp(baseColor, fogTarget, weight), cap);

            // Rim of the shaded side: between the two, a touch closer to the lit side at golden hours.
            Color fresnel = Finish(Color.Lerp(baseColor, top, 0.35f + 0.25f * golden), cap);
            Color scattering = Finish(Color.Lerp(top, fogTarget, 0.5f), cap);

            return new CloudColors
            {
                baseColor = baseColor,
                topColor = top,
                fresnelColor = fresnel,
                scatteringColor = scattering,
                fogTarget = fogTarget,
                fogWeight = weight,
            };
        }

        private static Color Finish(Color c, float cap)
        {
            c = CapSaturation(c, cap);
            return new Color(Clamp01(c.r), Clamp01(c.g), Clamp01(c.b), 1f);
        }

        private static Color Grey(Color c)
        {
            float l = Luminance(c);
            return new Color(l, l, l, c.a);
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
