using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Seeded 2D gradient (Perlin) noise. Pure managed code (unlike <c>Mathf.PerlinNoise</c>) so it is
    /// deterministic everywhere and usable from plain unit tests. <see cref="Sample"/> returns roughly [-1, 1].
    /// </summary>
    public sealed class GradientNoise
    {
        private static readonly float[] GradX = { 1f, -1f, 1f, -1f, 1.4142135f, -1.4142135f, 0f, 0f };
        private static readonly float[] GradY = { 1f, 1f, -1f, -1f, 0f, 0f, 1.4142135f, -1.4142135f };

        private readonly int[] perm = new int[512];
        private readonly float offsetX;
        private readonly float offsetY;

        /// <summary>Creates a noise field whose permutation and offset derive from <paramref name="seed"/>.</summary>
        public GradientNoise(int seed)
        {
            var rng = new System.Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = p[i];
                p[i] = p[j];
                p[j] = t;
            }

            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
            offsetX = (float)(rng.NextDouble() * 512.0);
            offsetY = (float)(rng.NextDouble() * 512.0);
        }

        /// <summary>Single octave of noise at (x, y) in noise units (1 unit = one lattice cell).</summary>
        public float Sample(float x, float y)
        {
            x += offsetX;
            y += offsetY;
            // Mathf.FloorToInt is (int)Math.Floor(f); written out because the call is not inlined (hot: every texel of every pass).
            int xi = (int)System.Math.Floor(x);
            int yi = (int)System.Math.Floor(y);
            float xf = x - xi;
            float yf = y - yi;
            int X = xi & 255;
            int Y = yi & 255;

            float u = xf * xf * xf * (xf * (xf * 6f - 15f) + 10f);
            float v = yf * yf * yf * (yf * (yf * 6f - 15f) + 10f);

            int aa = perm[perm[X] + Y] & 7;
            int ab = perm[perm[X] + Y + 1] & 7;
            int ba = perm[perm[X + 1] + Y] & 7;
            int bb = perm[perm[X + 1] + Y + 1] & 7;

            float n00 = GradX[aa] * xf + GradY[aa] * yf;
            float n10 = GradX[ba] * (xf - 1f) + GradY[ba] * yf;
            float n01 = GradX[ab] * xf + GradY[ab] * (yf - 1f);
            float n11 = GradX[bb] * (xf - 1f) + GradY[bb] * (yf - 1f);

            float x1 = n00 + (n10 - n00) * u;
            float x2 = n01 + (n11 - n01) * u;
            return (x1 + (x2 - x1) * v) * 0.7071f;
        }

        /// <summary>Fractal Brownian motion: <paramref name="octaves"/> octaves, normalised to roughly [-1, 1].</summary>
        public float Fbm(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f;
            float amplitude = 1f;
            float norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Sample(x, y) * amplitude;
                norm += amplitude;
                amplitude *= gain;
                x = x * lacunarity + 17.31f;
                y = y * lacunarity - 9.73f;
            }

            return sum / norm;
        }
    }
}
