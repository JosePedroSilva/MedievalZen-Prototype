using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Biome weights of one texel. The four land biomes sum to 1 (farmland first, then forest, then highland vs
    /// meadow); <see cref="shore"/> is an overlay (0..1) near lake edges.
    /// </summary>
    public struct BiomeWeights
    {
        /// <summary>Open lowland grass.</summary>
        public float meadow;
        /// <summary>Woodland.</summary>
        public float forest;
        /// <summary>Open highland (upper slopes, crests).</summary>
        public float highland;
        /// <summary>Farmland around villages and towns.</summary>
        public float farm;
        /// <summary>Lake shore overlay.</summary>
        public float shore;

        /// <summary>Creates weights (used by rule tables: the rule's affinity per biome).</summary>
        public BiomeWeights(float meadow, float forest, float highland, float farm, float shore = 0f)
        {
            this.meadow = meadow;
            this.forest = forest;
            this.highland = highland;
            this.farm = farm;
            this.shore = shore;
        }

        /// <summary>Affinity of a rule (<paramref name="rule"/> = per-biome weights) at a texel with these weights, 0..1.</summary>
        public float Dot(BiomeWeights rule)
        {
            float land = meadow * rule.meadow + forest * rule.forest + highland * rule.highland + farm * rule.farm;
            return Mathf.Clamp01(Mathf.Max(land, shore * rule.shore));
        }
    }

    /// <summary>
    /// Per-texel land-cover fields on the alphamap grid (texel centres as in <see cref="RoadDistanceField"/>):
    /// height, slope, forest/highland/farm/moisture/shore weights. Built once after terrain shaping and shared by
    /// splat painting, detail density maps and prop scatter so they agree with each other.
    /// </summary>
    public sealed class LandCover
    {
        /// <summary>Distance beyond a village/town edge that counts as farmland.</summary>
        public const float FarmZone = 150f;

        /// <summary>Shore band above the water line, metres of height.</summary>
        public const float ShoreHeight = 2.5f;

        private LandCover(int resolution, float worldSize, float waterHeight)
        {
            Resolution = resolution;
            WorldSize = worldSize;
            TexelSize = worldSize / resolution;
            WaterHeight = waterHeight;
            int count = resolution * resolution;
            Height = new float[count];
            Slope = new float[count];
            Forest = new float[count];
            Highland = new float[count];
            Farm = new float[count];
            Moisture = new float[count];
            Shore = new float[count];
        }

        /// <summary>Texels per side.</summary>
        public int Resolution { get; }

        /// <summary>World side length in metres.</summary>
        public float WorldSize { get; }

        /// <summary>Texel size in metres.</summary>
        public float TexelSize { get; }

        /// <summary>Lake level in metres (negative = no lakes).</summary>
        public float WaterHeight { get; }

        /// <summary>Terrain height (m).</summary>
        public float[] Height { get; }

        /// <summary>Slope, rise over run.</summary>
        public float[] Slope { get; }

        /// <summary>Forest weight 0..1 (before farmland is subtracted).</summary>
        public float[] Forest { get; }

        /// <summary>Highland weight 0..1.</summary>
        public float[] Highland { get; }

        /// <summary>Farmland weight 0..1 (near villages and towns).</summary>
        public float[] Farm { get; }

        /// <summary>Moisture 0 (dry) .. 1 (lush).</summary>
        public float[] Moisture { get; }

        /// <summary>Shore weight 0..1 just above the water line (0 under water and inland).</summary>
        public float[] Shore { get; }

        /// <summary>Builds the fields for the (already shaped) <paramref name="relief"/>.</summary>
        public static LandCover Build(WorldGenParams p, Relief relief, IReadOnlyList<SettlementSite> sites, int resolution)
        {
            var cover = new LandCover(resolution, p.worldSize, relief.HasWater ? relief.WaterHeight : -1f);
            float[,] map = relief.Map;
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);
            var forestNoise = new GradientNoise(p.seed + 4);
            var moistureNoise = new GradientNoise(p.seed + 13);
            float threshold = Mathf.Lerp(0.32f, -0.32f, Mathf.Clamp01(p.forestDensity));
            float texel = cover.TexelSize;

            // Farmland only around villages and towns.
            var farmSites = new List<SettlementSite>();
            foreach (SettlementSite site in sites)
            {
                if (site.Type != SettlementType.Inn) farmSites.Add(site);
            }

            for (int z = 0; z < resolution; z++)
            {
                float wz = (z + 0.5f) * texel;
                for (int x = 0; x < resolution; x++)
                {
                    float wx = (x + 0.5f) * texel;
                    int i = z * resolution + x;
                    float h = HeightmapGenerator.SampleHeight(map, p, wx, wz);
                    float hn = h / amplitude;
                    float slope = HeightmapGenerator.SampleSlope(map, p.worldSize, p.hillAmplitude, wx, wz);
                    cover.Height[i] = h;
                    cover.Slope[i] = slope;

                    float mask = relief.Mask.Evaluate(wx, wz);
                    float highland = Mathf.Max(Smooth(0.25f, 0.65f, mask) * Smooth(0.3f, 0.5f, hn), Smooth(0.55f, 0.75f, hn));
                    cover.Highland[i] = highland;

                    float moisture = 0.47f + 0.9f * moistureNoise.Fbm(wx * 0.0011f, wz * 0.0011f, 3) - 0.6f * (hn - 0.3f);
                    moisture = Mathf.Clamp01(moisture);
                    cover.Moisture[i] = moisture;

                    float n = forestNoise.Fbm(wx * 0.0032f, wz * 0.0032f, 4) + 0.2f * (moisture - 0.5f);
                    float forest = Smooth(threshold - 0.04f, threshold + 0.1f, n);
                    forest *= 1f - Smooth(0.72f, 0.88f, hn);        // tree line
                    forest *= 1f - Smooth(0.55f, 0.85f, slope);     // no forest on cliffs
                    forest *= 1f - 0.45f * highland;
                    cover.Forest[i] = forest;

                    float farm = 0f;
                    for (int s = 0; s < farmSites.Count; s++)
                    {
                        Vector3 c = farmSites[s].Position;
                        float dx = wx - c.x;
                        float dz = wz - c.z;
                        float reach = farmSites[s].Radius + FarmZone + 40f;
                        float dSqr = dx * dx + dz * dz;
                        if (dSqr >= reach * reach) continue;
                        float d = Mathf.Sqrt(dSqr) - farmSites[s].Radius + 35f * forestNoise.Sample(wx * 0.012f + 50f, wz * 0.012f);
                        farm = Mathf.Max(farm, 1f - Smooth(FarmZone * 0.55f, FarmZone, d));
                    }

                    cover.Farm[i] = farm;
                    if (cover.WaterHeight > 0f && h >= cover.WaterHeight)
                    {
                        cover.Shore[i] = 1f - Smooth(0f, ShoreHeight, h - cover.WaterHeight);
                    }
                }
            }

            return cover;
        }

        /// <summary>Nearest texel index for world (x, z) (clamped).</summary>
        public int Index(float x, float z)
        {
            int tx = Mathf.Clamp((int)(x / TexelSize), 0, Resolution - 1);
            int tz = Mathf.Clamp((int)(z / TexelSize), 0, Resolution - 1);
            return tz * Resolution + tx;
        }

        /// <summary>True when texel <paramref name="i"/> is under water.</summary>
        public bool IsWater(int i) => WaterHeight > 0f && Height[i] < WaterHeight;

        /// <summary>Water depth at texel <paramref name="i"/> (0 on land).</summary>
        public float Depth(int i) => WaterHeight > 0f ? Mathf.Max(0f, WaterHeight - Height[i]) : 0f;

        /// <summary>Biome weights of texel <paramref name="i"/>.</summary>
        public BiomeWeights Weights(int i)
        {
            float farm = Farm[i];
            float rest = 1f - farm;
            float forest = rest * Forest[i];
            float open = rest - forest;
            float highland = open * Highland[i];
            return new BiomeWeights(open - highland, forest, highland, farm, Shore[i]);
        }

        internal static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
