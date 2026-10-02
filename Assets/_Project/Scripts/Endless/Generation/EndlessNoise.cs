using System;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// Noise and hashing on absolute (double) coordinates for the endless world. <see cref="GradientNoise"/> repeats every
    /// 256 lattice cells (and its octaves keep that period at lacunarity 2), so a coordinate is reduced modulo 256 cells in
    /// double precision before it becomes a float: values stay exact to well under a millimetre at any distance from the
    /// journey's start. Pure and thread-safe (the noise objects are immutable).
    /// </summary>
    public static class EndlessNoise
    {
        /// <summary>Period of <see cref="GradientNoise"/> in lattice cells.</summary>
        public const double Period = 256.0;

        /// <summary><paramref name="v"/> (lattice units) reduced into [0, 256).</summary>
        public static float Wrap(double v) => (float)(v - Period * Math.Floor(v / Period));

        /// <summary>One octave at absolute (x, z) with <paramref name="frequency"/> cycles per metre, about [-0.7, 0.7].</summary>
        public static float Sample(GradientNoise noise, double x, double z, double frequency) =>
            noise.Sample(Wrap(x * frequency), Wrap(z * frequency));

        /// <summary>Fractal noise at absolute (x, z), about [-0.7, 0.7].</summary>
        public static float Fbm(GradientNoise noise, double x, double z, double frequency, int octaves) =>
            noise.Fbm(Wrap(x * frequency), Wrap(z * frequency), octaves);

        /// <summary>1D fractal noise along a line (z = constant row of the 2D field), about [-0.7, 0.7].</summary>
        public static float Fbm1(GradientNoise noise, double t, double frequency, int octaves, float row = 37.25f) =>
            noise.Fbm(Wrap(t * frequency), row, octaves);

        /// <summary>Standard deviation of <see cref="GradientNoise.Fbm"/> by octave count (measured; index = octaves).</summary>
        private static readonly float[] FbmStd = { 1f, 0.214f, 0.160f, 0.140f, 0.132f, 0.130f };

        /// <summary><see cref="Fbm"/> scaled to unit standard deviation (values mostly within ±3).</summary>
        public static float FbmN(GradientNoise noise, double x, double z, double frequency, int octaves) =>
            Fbm(noise, x, z, frequency, octaves) / FbmStd[Math.Min(octaves, FbmStd.Length - 1)];

        /// <summary><see cref="Fbm1"/> scaled to unit standard deviation (values mostly within ±3).</summary>
        public static float Fbm1N(GradientNoise noise, double t, double frequency, int octaves, float row = 37.25f) =>
            Fbm1(noise, t, frequency, octaves, row) / FbmStd[Math.Min(octaves, FbmStd.Length - 1)];

        /// <summary>
        /// Ridged multifractal 0..1 with lacunarity 2 (so it keeps the noise period): sharp crests, broad lows, each octave
        /// weighted by the previous one.
        /// </summary>
        public static float Ridged(GradientNoise noise, double x, double z, double frequency, int octaves, float sharpness)
        {
            float u = Wrap(x * frequency);
            float v = Wrap(z * frequency);
            float sum = 0f, amplitude = 1f, norm = 0f, weight = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float r = 1f - Mathf.Abs(noise.Sample(u, v) * 1.4f);
                r = Mathf.Clamp01(r);
                r = sharpness == 2f ? r * r : Mathf.Pow(r, sharpness);
                r *= weight;
                weight = Mathf.Clamp01(r * 1.5f);
                sum += r * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                u = u * 2f + 7.25f;
                v = v * 2f - 3.5f;
            }

            return sum / norm;
        }

        /// <summary>64-bit mix of a seed, a stream id and two integer coordinates.</summary>
        public static ulong Hash(int seed, int stream, long a, long b = 0)
        {
            unchecked
            {
                ulong h = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL;
                h ^= (ulong)(uint)stream * 0xD6E8FEB86659FD93UL;
                h = Mix(h);
                h ^= (ulong)a * 0xC2B2AE3D27D4EB4FUL;
                h = Mix(h);
                h ^= (ulong)b * 0x165667B19E3779F9UL;
                return Mix(h);
            }
        }

        /// <summary>Uniform double in [0, 1) from a hash.</summary>
        public static double Unit(ulong hash) => (hash >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform double in [0, 1) for (seed, stream, a, b).</summary>
        public static double Unit(int seed, int stream, long a, long b = 0) => Unit(Hash(seed, stream, a, b));

        /// <summary>Positive 31-bit seed for a <see cref="System.Random"/> stream.</summary>
        public static int Seed(int seed, int stream, long a, long b = 0) => (int)(Hash(seed, stream, a, b) & 0x7fffffff);

        private static ulong Mix(ulong h)
        {
            unchecked
            {
                h ^= h >> 30;
                h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 27;
                h *= 0x94D049BB133111EBUL;
                h ^= h >> 31;
                return h;
            }
        }

        /// <summary>Smoothstep of x from e0 (0) to e1 (1), in doubles.</summary>
        public static double Smooth(double e0, double e1, double x)
        {
            double t = (x - e0) / (e1 - e0);
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>Floor division for longs.</summary>
        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }
}
