using System;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Large-scale 0..1 mask of the highland region: a noisy disc around a (seeded or configured) centre that
    /// sits towards one side or corner of the map, so there is a lowland valley region and a highland region.
    /// </summary>
    public sealed class MountainMask
    {
        private readonly GradientNoise noise;
        private readonly float worldSize;

        private MountainMask(Vector2 centre, float radius, float worldSize, int seed)
        {
            Centre = centre;
            Radius = Mathf.Max(0.05f, radius);
            this.worldSize = worldSize;
            noise = new GradientNoise(seed);
        }

        /// <summary>Normalised centre of the highlands.</summary>
        public Vector2 Centre { get; }

        /// <summary>Normalised radius of the highlands.</summary>
        public float Radius { get; }

        /// <summary>Mask for <paramref name="p"/> (stage seed + 11).</summary>
        public static MountainMask Create(WorldGenParams p)
        {
            Vector2 centre = p.mountainMaskCentre;
            if (p.randomiseMountainSide)
            {
                var rng = new System.Random(p.seed + 11);
                Vector2[] spots =
                {
                    new Vector2(0.85f, 0.85f), new Vector2(0.15f, 0.85f), new Vector2(0.85f, 0.15f), new Vector2(0.15f, 0.15f),
                    new Vector2(0.5f, 0.92f), new Vector2(0.5f, 0.08f), new Vector2(0.92f, 0.5f), new Vector2(0.08f, 0.5f),
                };
                centre = spots[rng.Next(spots.Length)] + new Vector2((float)(rng.NextDouble() - 0.5) * 0.1f, (float)(rng.NextDouble() - 0.5) * 0.1f);
            }

            return new MountainMask(centre, p.mountainMaskRadius, p.worldSize, p.seed + 12);
        }

        /// <summary>Highland weight at world (x, z): 1 in the core, 0 in the lowlands.</summary>
        public float Evaluate(float x, float z)
        {
            float u = x / worldSize - Centre.x;
            float v = z / worldSize - Centre.y;
            float d = Mathf.Sqrt(u * u + v * v) / Radius;
            d += 0.3f * noise.Fbm(x * 0.0012f, z * 0.0012f, 2);
            float t = Mathf.Clamp01((d - 0.3f) / 0.7f);
            return 1f - t * t * (3f - 2f * t);
        }
    }

    /// <summary>
    /// The heightmap plus what later stages need to interpret it: water level and highland mask.
    /// <see cref="Map"/> is modified in place by <see cref="TerrainShaper"/>.
    /// </summary>
    public sealed class Relief
    {
        /// <summary>Creates a relief.</summary>
        public Relief(WorldGenParams p, float[,] map, float waterHeight, MountainMask mask)
        {
            Params = p ?? throw new ArgumentNullException(nameof(p));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            WaterHeight = waterHeight;
            Mask = mask ?? MountainMask.Create(p);
        }

        /// <summary>Generation inputs.</summary>
        public WorldGenParams Params { get; }

        /// <summary>Normalised heightmap <c>[z, x]</c>.</summary>
        public float[,] Map { get; }

        /// <summary>Lake water level in metres, or a negative value when there are no lakes.</summary>
        public float WaterHeight { get; }

        /// <summary>True when the world has lakes.</summary>
        public bool HasWater => WaterHeight > 0f;

        /// <summary>Highland mask.</summary>
        public MountainMask Mask { get; }

        /// <summary>Relief with no water for an arbitrary map (tests, tools).</summary>
        public static Relief Dry(WorldGenParams p, float[,] map) => new Relief(p, map, -1f, MountainMask.Create(p));

        /// <summary>Terrain height in metres at world (x, z).</summary>
        public float Height(float x, float z) => HeightmapGenerator.SampleHeight(Map, Params, x, z);

        /// <summary>Normalised height (0..1) at world (x, z).</summary>
        public float Normalized(float x, float z) => HeightmapGenerator.SampleNormalized(Map, Params.worldSize, x, z);

        /// <summary>Slope (rise over run) at world (x, z).</summary>
        public float Slope(float x, float z) => HeightmapGenerator.SampleSlope(Map, Params.worldSize, Params.hillAmplitude, x, z);

        /// <summary>True when (x, z) is under water, or within <paramref name="margin"/> metres (height) of the water line.</summary>
        public bool IsWater(float x, float z, float margin = 0f) => HasWater && Height(x, z) < WaterHeight + margin;
    }

    /// <summary>
    /// Builds the normalised (0..1) heightmap. Layout matches <c>TerrainData.SetHeights</c>: <c>map[z, x]</c>;
    /// vertex (x, z) sits at world position (x * spacing, z * spacing) with spacing = worldSize / (res - 1).
    /// World height in metres = value * <see cref="WorldGenParams.hillAmplitude"/> (the total relief).
    /// </summary>
    public static class HeightmapGenerator
    {
        /// <summary>Lake beds are carved up to this many metres below the lowest land.</summary>
        public const float LakeDepth = 5f;

        /// <summary>Generates the heightmap for <paramref name="settings"/>.</summary>
        public static float[,] Generate(WorldSettings settings) => Generate(WorldGenParams.From(settings));

        /// <summary>Generates the heightmap (with lake beds carved) for <paramref name="p"/>.</summary>
        public static float[,] Generate(WorldGenParams p) => GenerateRelief(p).Map;

        /// <summary>
        /// Composes (a) warped fBm rolling lowland hills with flattened valley floors, (b) a ridged-multifractal
        /// mountain mass masked to one side/corner, (c) occasional escarpments; normalises to 0..1 leaving room
        /// for lake beds; smooths gentle slopes (<see cref="SmoothLowSlopes"/>); then floods the lowest <see cref="WorldGenParams.lakeCoverage"/> of the map and carves
        /// the lake floors. Deterministic (stage seeds + 1, + 101..106, + 11/12).
        /// </summary>
        public static Relief GenerateRelief(WorldGenParams p)
        {
            int n = Mathf.Max(2, p.heightmapResolution);
            float spacing = p.worldSize / (n - 1);
            var baseNoise = new GradientNoise(p.seed + 1);
            var warpX = new GradientNoise(p.seed + 101);
            var warpZ = new GradientNoise(p.seed + 102);
            var ridgeNoise = new GradientNoise(p.seed + 104);
            var escarpNoise = new GradientNoise(p.seed + 105);
            var escarpGate = new GradientNoise(p.seed + 106);
            MountainMask mask = MountainMask.Create(p);

            float f = Mathf.Max(1e-5f, p.hillFrequency);
            float warpF = f * 0.5f;
            float ridgeF = f * 1.4f;
            float escarpF = f * 0.55f;
            float flattenExponent = 1f + 2f * Mathf.Clamp01(p.valleyFlatten);
            float sharpness = Mathf.Max(1f, p.ridgeSharpness);

            var map = new float[n, n];
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int z = 0; z < n; z++)
            {
                float wz = z * spacing;
                for (int x = 0; x < n; x++)
                {
                    float wx = x * spacing;
                    float dx = warpX.Fbm(wx * warpF, wz * warpF, 2) * p.domainWarp;
                    float dz = warpZ.Fbm(wx * warpF, wz * warpF, 2) * p.domainWarp;
                    float sx = wx + dx;
                    float sz = wz + dz;

                    // (a) Rolling base; the power curve flattens valley floors into plains. Four octaves (smallest
                    // wavelength ≈ 60 m on the default map): a fifth only added ~30 m micro-bumps that the terrain
                    // LOD could not follow, which left grass floating at mid distance.
                    float rolling = Mathf.Clamp01(0.5f + 0.75f * baseNoise.Fbm(sx * f, sz * f, 4));
                    rolling = Mathf.Pow(rolling, flattenExponent);

                    // (b) Ridged multifractal, masked; the mask also lifts the whole highland plateau.
                    float m = mask.Evaluate(wx, wz);
                    float mountains = 0f;
                    if (m > 0.001f)
                    {
                        float ridged = Ridged(ridgeNoise, sx * ridgeF, sz * ridgeF, 5, sharpness);
                        mountains = m * (0.4f * m + 0.6f * ridged);
                    }

                    // (c) Escarpments: a steep step where a low-frequency noise crosses a threshold.
                    float e = escarpNoise.Sample(wx * escarpF, wz * escarpF);
                    float step = Mathf.Clamp01((e - 0.12f) / 0.05f);
                    // A second noise gates the step so escarpments are broken ridges of a few hundred metres, not
                    // endless contour lines; where the gate fades the step becomes a gentle ramp.
                    float gate = Mathf.Clamp01((escarpGate.Fbm(wx * escarpF * 1.7f, wz * escarpF * 1.7f, 2) + 0.05f) / 0.3f);
                    gate = gate * gate * (3f - 2f * gate);
                    float escarpment = step * step * (3f - 2f * step) * gate * (1f - 0.6f * m);

                    float h = rolling * p.rollingAmplitude + mountains * p.mountainAmplitude + escarpment * p.escarpmentHeight;
                    map[z, x] = h;
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }

            // Normalise land to [reserve, 1] so lake beds can be carved below the lowest land.
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);
            bool lakes = p.lakeCoverage > 0f;
            float reserve = lakes ? Mathf.Min(LakeDepth / amplitude, 0.1f) : 0f;
            float range = max - min;
            float inv = range > 1e-6f ? (1f - reserve) / range : 0f;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++) map[z, x] = reserve + (map[z, x] - min) * inv;
            }

            // Smooth the gentle ground (ridges, cliffs and escarpments keep their edges).
            SmoothLowSlopes(map, spacing, amplitude);

            float waterHeight = -1f;
            if (lakes)
            {
                float level = Percentile(map, Mathf.Clamp(p.lakeCoverage, 0f, 0.5f));
                float shelf = 3f / amplitude;
                for (int z = 0; z < n; z++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float v = map[z, x];
                        if (v >= level) continue;
                        float t = Mathf.Clamp01((level - v) / shelf);
                        map[z, x] = Mathf.Max(0f, v - reserve * t * t * (3f - 2f * t));
                    }
                }

                waterHeight = level * amplitude;
            }

            return new Relief(p, map, waterHeight, mask);
        }

        /// <summary>Slope (rise over run) below which <see cref="SmoothLowSlopes"/> smooths fully.</summary>
        public const float SmoothSlopeStart = 0.3f;

        /// <summary>Slope (rise over run) above which <see cref="SmoothLowSlopes"/> leaves the ground untouched.</summary>
        public const float SmoothSlopeEnd = 0.6f;

        /// <summary>
        /// Removes high-frequency bumps from gentle ground: <paramref name="passes"/> passes of a 3×3 binomial blur,
        /// blended in by slope (full below <see cref="SmoothSlopeStart"/>, none above <see cref="SmoothSlopeEnd"/>,
        /// slope measured on the unsmoothed map) so ridges, cliffs and escarpments stay sharp. Smooth ground keeps
        /// the terrain LOD close to the true surface, so detail meshes (grass) do not float at mid distance.
        /// Pure and deterministic; modifies <paramref name="map"/> in place.
        /// </summary>
        public static void SmoothLowSlopes(float[,] map, float spacing, float heightScale, int passes = 2)
        {
            int n = map.GetLength(0);
            int m = map.GetLength(1);
            if (n < 3 || m < 3 || passes <= 0) return;

            // Blend weight from the original slope.
            var weight = new float[n, m];
            float inv = heightScale / (2f * Mathf.Max(1e-4f, spacing));
            for (int z = 0; z < n; z++)
            {
                int z0 = Mathf.Max(0, z - 1);
                int z1 = Mathf.Min(n - 1, z + 1);
                for (int x = 0; x < m; x++)
                {
                    int x0 = Mathf.Max(0, x - 1);
                    int x1 = Mathf.Min(m - 1, x + 1);
                    float gx = (map[z, x1] - map[z, x0]) * inv * 2f / (x1 - x0);
                    float gz = (map[z1, x] - map[z0, x]) * inv * 2f / (z1 - z0);
                    float slope = Mathf.Sqrt(gx * gx + gz * gz);
                    float t = Mathf.Clamp01((slope - SmoothSlopeStart) / (SmoothSlopeEnd - SmoothSlopeStart));
                    weight[z, x] = 1f - t * t * (3f - 2f * t);
                }
            }

            var src = (float[,])map.Clone();
            var dst = new float[n, m];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int z = 0; z < n; z++)
                {
                    int z0 = Mathf.Max(0, z - 1);
                    int z1 = Mathf.Min(n - 1, z + 1);
                    for (int x = 0; x < m; x++)
                    {
                        int x0 = Mathf.Max(0, x - 1);
                        int x1 = Mathf.Min(m - 1, x + 1);
                        float row0 = src[z0, x0] + 2f * src[z0, x] + src[z0, x1];
                        float row1 = src[z, x0] + 2f * src[z, x] + src[z, x1];
                        float row2 = src[z1, x0] + 2f * src[z1, x] + src[z1, x1];
                        dst[z, x] = (row0 + 2f * row1 + row2) * (1f / 16f);
                    }
                }

                var swap = src;
                src = dst;
                dst = swap;
            }

            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < m; x++)
                {
                    float w = weight[z, x];
                    if (w > 0f) map[z, x] += (src[z, x] - map[z, x]) * w;
                }
            }
        }

        /// <summary>Ridged multifractal (Musgrave): sharp crests, each octave weighted by the previous one. 0..1.</summary>
        public static float Ridged(GradientNoise noise, float x, float y, int octaves, float sharpness)
        {
            float sum = 0f;
            float amplitude = 1f;
            float norm = 0f;
            float weight = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float r = 1f - Mathf.Abs(noise.Sample(x, y) * 1.4f);
                r = Mathf.Pow(Mathf.Clamp01(r), sharpness);
                r *= weight;
                weight = Mathf.Clamp01(r * 1.5f);
                sum += r * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                x = x * 2.03f + 7.1f;
                y = y * 2.03f - 3.3f;
            }

            return sum / norm;
        }

        private static float Percentile(float[,] map, float fraction)
        {
            const int Bins = 4096;
            var histogram = new int[Bins];
            int n = map.GetLength(0);
            int total = 0;
            for (int z = 0; z < n; z += 2)
            {
                for (int x = 0; x < n; x += 2)
                {
                    histogram[Mathf.Clamp((int)(map[z, x] * (Bins - 1)), 0, Bins - 1)]++;
                    total++;
                }
            }

            int target = Mathf.RoundToInt(total * fraction);
            int cumulative = 0;
            for (int b = 0; b < Bins; b++)
            {
                cumulative += histogram[b];
                if (cumulative >= target) return (b + 0.5f) / (Bins - 1);
            }

            return 1f;
        }

        /// <summary>Bilinear sample in grid units (clamped to the map).</summary>
        public static float SampleGrid(float[,] map, float gx, float gz)
        {
            int n = map.GetLength(0);
            gx = Mathf.Clamp(gx, 0f, n - 1);
            gz = Mathf.Clamp(gz, 0f, n - 1);
            int x0 = Mathf.Min((int)gx, n - 2);
            int z0 = Mathf.Min((int)gz, n - 2);
            float tx = gx - x0;
            float tz = gz - z0;
            float a = map[z0, x0] + (map[z0, x0 + 1] - map[z0, x0]) * tx;
            float b = map[z0 + 1, x0] + (map[z0 + 1, x0 + 1] - map[z0 + 1, x0]) * tx;
            return a + (b - a) * tz;
        }

        /// <summary>Bilinear normalised height (0..1) at world position (x, z).</summary>
        public static float SampleNormalized(float[,] map, float worldSize, float x, float z)
        {
            float s = (map.GetLength(0) - 1) / worldSize;
            return SampleGrid(map, x * s, z * s);
        }

        /// <summary>Bilinear height in metres at world position (x, z).</summary>
        public static float SampleHeight(float[,] map, float worldSize, float heightScale, float x, float z)
        {
            return SampleNormalized(map, worldSize, x, z) * heightScale;
        }

        /// <summary>Bilinear height in metres at world position (x, z) for the world described by <paramref name="p"/>.</summary>
        public static float SampleHeight(float[,] map, WorldGenParams p, float x, float z)
        {
            return SampleNormalized(map, p.worldSize, x, z) * p.hillAmplitude;
        }

        /// <summary>Terrain gradient magnitude (rise over run, 1 = 45 degrees) at world (x, z), central differences.</summary>
        public static float SampleSlope(float[,] map, float worldSize, float heightScale, float x, float z)
        {
            float step = worldSize / (map.GetLength(0) - 1);
            float hx = SampleNormalized(map, worldSize, x + step, z) - SampleNormalized(map, worldSize, x - step, z);
            float hz = SampleNormalized(map, worldSize, x, z + step) - SampleNormalized(map, worldSize, x, z - step);
            float inv = heightScale / (2f * step);
            return Mathf.Sqrt(hx * hx + hz * hz) * inv;
        }
    }
}
