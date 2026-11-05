using System;
using System.Runtime.CompilerServices;

namespace MedievalZen.World
{
    /// <summary>
    /// The <c>UnityEngine.Mathf</c> helpers the per-texel generation loops call, with Unity's own bodies (the same comparisons and
    /// casts, so every result is bit-identical) but inlinable: calls into UnityEngine.CoreModule are not inlined, and in the splat,
    /// cover, distance-field and detail passes they cost more than the arithmetic (2026-10-06 generation time pass). Pure.
    /// </summary>
    internal static class FastMathf
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float a, float b) => a > b ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float a, float b) => a < b ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Max(int a, int b) => a > b ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Min(int a, int b) => a < b ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sqrt(float f) => (float)Math.Sqrt(f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float f) => Math.Abs(f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt(float f) => (int)Math.Floor(f);
    }
}
