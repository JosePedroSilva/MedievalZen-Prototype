using System;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Fog densities pushed to the atmospheric fog pass (see <c>Shaders/AtmosphericFog.shader</c>): exponential-squared
    /// distance haze plus exponential height fog that is densest at <see cref="baseAltitude"/> and thins above it.
    /// </summary>
    public struct FogSettings
    {
        /// <summary>Distance haze: optical depth is (hazeDensity · distance)², per metre.</summary>
        public float hazeDensity;

        /// <summary>Height fog density at (and below) the base altitude, per metre.</summary>
        public float mistDensity;

        /// <summary>Height fog density falls by e every 1/mistFalloff metres above the base altitude.</summary>
        public float mistFalloff;

        /// <summary>Altitude (world Y) at which the height fog is densest.</summary>
        public float baseAltitude;
    }

    /// <summary>Pure maths shared by the atmosphere components and their tests (mirrors the fog shader).</summary>
    public static class AtmosphereMath
    {
        private static readonly float SqrtLn2 = (float)Math.Sqrt(Math.Log(2.0));

        /// <summary>Exp² haze density at which the haze reaches 50 % opacity after <paramref name="halfDistance"/> metres.</summary>
        public static float HazeDensityForHalfDistance(float halfDistance) => SqrtLn2 / Math.Max(1f, halfDistance);

        /// <summary>
        /// Optical depth along a straight ray of <paramref name="distance"/> metres from a camera at <paramref name="cameraY"/>
        /// to a point at <paramref name="targetY"/> (heights below the base altitude count as the base altitude).
        /// Same formula as the shader.
        /// </summary>
        public static float OpticalDepth(in FogSettings fog, float distance, float cameraY, float targetY)
        {
            if (distance <= 0f) return 0f;
            float haze = fog.hazeDensity * distance;
            float k = Math.Max(fog.mistFalloff, 1e-5f);
            float h0 = Math.Max(cameraY - fog.baseAltitude, 0f);
            float h1 = Math.Max(targetY - fog.baseAltitude, 0f);
            float dh = h1 - h0;
            float e0 = (float)Math.Exp(-k * h0);
            float ratio = Math.Abs(k * dh) > 1e-3f ? (e0 - (float)Math.Exp(-k * h1)) / (k * dh) : e0;
            return haze * haze + fog.mistDensity * distance * ratio;
        }

        /// <summary>Fog opacity (0 = clear, 1 = solid) for <see cref="OpticalDepth"/>.</summary>
        public static float Opacity(in FogSettings fog, float distance, float cameraY, float targetY)
        {
            return 1f - (float)Math.Exp(-OpticalDepth(fog, distance, cameraY, targetY));
        }

        /// <summary>
        /// Scales both densities down (never up) so that a horizontal ray at the base altitude — the thickest case — is at most
        /// <paramref name="maxOpacity"/> opaque after <paramref name="readableDistance"/> metres, keeping the road readable.
        /// Returns the scale applied (1 = unchanged).
        /// </summary>
        public static float ClampForReadability(ref FogSettings fog, float readableDistance, float maxOpacity)
        {
            fog.hazeDensity = Math.Max(0f, fog.hazeDensity);
            fog.mistDensity = Math.Max(0f, fog.mistDensity);
            if (readableDistance <= 0f) return 1f;
            maxOpacity = Math.Min(Math.Max(maxOpacity, 0.01f), 0.999f);
            float tauMax = -(float)Math.Log(1.0 - maxOpacity);
            float a = fog.hazeDensity * readableDistance;
            a *= a;
            float b = fog.mistDensity * readableDistance;
            if (a + b <= tauMax) return 1f;

            // Solve a·s² + b·s = tauMax for the scale s in (0, 1).
            float s = a > 1e-9f ? (-b + (float)Math.Sqrt(b * b + 4f * a * tauMax)) / (2f * a) : tauMax / b;
            s = Math.Min(1f, Math.Max(0f, s)) * 0.999f;
            fog.hazeDensity *= s;
            fog.mistDensity *= s;
            return s;
        }

        /// <summary>
        /// A light that switches on at <paramref name="onStart"/> (ramping to full over <paramref name="onDuration"/> hours)
        /// and off at <paramref name="offStart"/> (ramping to zero over <paramref name="offDuration"/> hours). Hours wrap at 24,
        /// the result is continuous in <paramref name="hour"/> and in 0..1.
        /// </summary>
        public static float LightFade(float hour, float onStart, float onDuration, float offStart, float offDuration)
        {
            onDuration = Math.Max(onDuration, 1e-3f);
            offDuration = Math.Max(offDuration, 1e-3f);
            float sinceOn = DayClock.Wrap(hour - onStart);
            float onSpan = DayClock.Wrap(offStart - onStart);
            if (sinceOn < onSpan) return Smooth(sinceOn / onDuration);
            float level = Smooth(onSpan / onDuration);
            return level * (1f - Smooth((sinceOn - onSpan) / offDuration));
        }

        /// <summary>
        /// Greedy light budget: <paramref name="requests"/>[i] lights are wanted by requester i, requesters already sorted by
        /// priority (nearest first). Fills <paramref name="granted"/> so the total never exceeds <paramref name="cap"/>.
        /// Returns the total granted.
        /// </summary>
        public static int AllocateBudget(int[] requests, int count, int cap, int[] granted)
        {
            if (requests == null || granted == null) return 0;
            int left = Math.Max(0, cap);
            int total = 0;
            count = Math.Min(count, Math.Min(requests.Length, granted.Length));
            for (int i = 0; i < count; i++)
            {
                int give = Math.Min(Math.Max(0, requests[i]), left);
                granted[i] = give;
                left -= give;
                total += give;
            }

            return total;
        }

        /// <summary>
        /// Built-in linear fog start (metres) while the fog pass is live: far beyond the far clip, so Unity's fog adds exactly
        /// nothing to any geometry but stays enabled (fog keywords and <c>unity_FogColor</c> keep feeding the Synty shaders).
        /// </summary>
        public const float LiveFogStart = 20000f;

        /// <summary>Built-in linear fog end (metres) while the fog pass is live.</summary>
        public const float LiveFogEnd = 40000f;

        /// <summary>Haze half-distance (metres) at which <see cref="CloudHaze"/> is 1/e from the aerial haze alone.</summary>
        public const float CloudHazeScale = 900f;

        /// <summary>
        /// Fog amount (0 = none, 1 = full) Unity's built-in fog gives at <paramref name="distance"/> (view depth, metres) —
        /// the URP formulas (<c>ComputeFogFactorZ0ToFar</c> + <c>ComputeFogIntensity</c>). 0 when <paramref name="enabled"/> is false.
        /// </summary>
        public static float BuiltInFogAmount(bool enabled, UnityEngine.FogMode mode, float density, float start, float end, float distance)
        {
            if (!enabled) return 0f;
            distance = Math.Max(0f, distance);
            switch (mode)
            {
                case UnityEngine.FogMode.Linear:
                    if (end - start <= 1e-4f) return distance >= end ? 1f : 0f;
                    return 1f - Math.Min(1f, Math.Max(0f, (end - distance) / (end - start)));
                case UnityEngine.FogMode.Exponential:
                    return 1f - (float)Math.Exp(-Math.Max(0f, density) * distance);
                default:
                    float x = Math.Max(0f, density) * distance;
                    return 1f - (float)Math.Exp(-x * x);
            }
        }

        /// <summary>
        /// 0..1 haze around the clouds: the aerial haze (from its half-distance, 1/e at <see cref="CloudHazeScale"/>) plus a
        /// quarter of the low-mist patch amount. Weather overcast is not included (the cloud model adds it separately).
        /// </summary>
        public static float CloudHaze(in FogSettings fog, float mistPatches)
        {
            float haze = 0f;
            if (fog.hazeDensity > 1e-7f)
            {
                float half = SqrtLn2 / fog.hazeDensity;
                haze = (float)Math.Exp(-half / CloudHazeScale);
            }

            return Math.Min(1f, Math.Max(0f, haze + 0.25f * Math.Max(0f, mistPatches)));
        }

        /// <summary>
        /// Fog on the sky layer (skydome and clouds) for a view ray with vertical component <paramref name="dirY"/> (sine of the
        /// elevation): a horizon band (<paramref name="core"/>, fading out by <paramref name="coreSin"/> with a smoothstep)
        /// combined with a longer, convex haze tail (<paramref name="tail"/>, fading out by <paramref name="tailSin"/>).
        /// Zero at and above max(coreSin, tailSin). Same formula as <c>AtmosphericFog.shader</c>.
        /// </summary>
        public static float SkyFog(float dirY, float core, float coreSin, float tail, float tailSin)
        {
            float c = Math.Min(1f, Math.Max(0f, core)) * (1f - SmoothRange(0f, Math.Max(coreSin, 1e-3f), dirY));
            float f = 1f - Math.Min(1f, Math.Max(0f, dirY / Math.Max(tailSin, 1e-3f)));
            float t = Math.Min(1f, Math.Max(0f, tail)) * f * f;
            return 1f - (1f - c) * (1f - t);
        }

        /// <summary>Smoothstep of x clamped to 0..1.</summary>
        public static float Smooth(float x)
        {
            x = Math.Min(1f, Math.Max(0f, x));
            return x * x * (3f - 2f * x);
        }

        /// <summary>0 at or below <paramref name="a"/>, 1 at or above <paramref name="b"/>, smooth in between (b may be below a).</summary>
        public static float SmoothRange(float a, float b, float x)
        {
            if (Math.Abs(b - a) < 1e-6f) return x >= b ? 1f : 0f;
            return Smooth((x - a) / (b - a));
        }

        /// <summary>A 0..1 bump centred on <paramref name="centre"/> with half-width <paramref name="width"/> (smoothstep falloff).</summary>
        public static float Bump(float x, float centre, float width)
        {
            return 1f - Smooth(Math.Abs(x - centre) / Math.Max(width, 1e-4f));
        }
    }
}
