using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>How one terrain-detail family (grass, flowers) is distributed.</summary>
    public sealed class DetailRule
    {
        /// <summary>Detail family id (<see cref="PropRole"/> detail constants).</summary>
        public string family;
        /// <summary>Affinity per biome (meadow, forest, highland, farm, shore).</summary>
        public BiomeWeights biomes;
        /// <summary>Instances per detail cell (≈ 2 m square on the default map) at affinity 1.</summary>
        public float perCell;
        /// <summary>Size of the patches in metres (noise wavelength).</summary>
        public float patchSize = 20f;
        /// <summary>Patch noise threshold: 0 ≈ half the ground covered, higher = rarer, sharper patches.</summary>
        public float patchThreshold;
        /// <summary>-1 prefers lush ground, +1 prefers dry ground, 0 indifferent.</summary>
        public float dryAffinity;
        /// <summary>Minimum distance from the road centre line, metres beyond the road half-width.</summary>
        public float roadMargin = 1f;

        /// <summary>
        /// Endless road, flower families: how much the flower patches follow the flower-meadow paint (0 = their own patch noise
        /// only, the finite world; 1 = only where the ground is painted as flower meadow), so the colour of a flowery stretch
        /// carries on in the ground where the flowers themselves are no longer drawn.
        /// </summary>
        public float followFlowerPaint;

        /// <summary>Creates a rule.</summary>
        public DetailRule(string family, BiomeWeights biomes, float perCell)
        {
            this.family = family;
            this.biomes = biomes;
            this.perCell = perCell;
        }
    }

    /// <summary>
    /// Per-prototype terrain detail density maps (instance counts per cell, as used by
    /// <c>DetailScatterMode.InstanceCountMode</c>) on the alphamap grid. Dense grass everywhere off-road, flower
    /// patches in meadows, ground cover in forests; nothing on roads, settlement centres, fields, rock or water.
    /// </summary>
    public static class DetailPlanner
    {
        /// <summary>Biome weights constructor shorthand: meadow, forest, highland, farm, shore.</summary>
        private static BiomeWeights B(float meadow, float forest, float highland, float farm, float shore = 0f) =>
            new BiomeWeights(meadow, forest, highland, farm, shore);

        /// <summary>Beyond a rule's road clearance, density ramps up from <see cref="RoadFadeMin"/> to full over this many metres.</summary>
        public const float RoadFadeWidth = 6f;

        /// <summary>Density multiplier right at the edge of a rule's road clearance (ground shows between clumps).</summary>
        public const float RoadFadeMin = 0.35f;

        /// <summary>Largest horizontal size (metres) a detail instance may reach; wider meshes are narrowed.</summary>
        public const float MaxDetailWidth = 3f;

        /// <summary>
        /// Visible height range (metres above the ground) each detail family is scaled into, whatever the native
        /// size of its meshes: knee-high grass at most, sunflowers taller than a person. Unknown families: 0.3-0.5 m.
        /// </summary>
        public static Vector2 TargetHeight(string family)
        {
            switch (family)
            {
                case PropRole.ShortGrass: return new Vector2(0.25f, 0.4f);
                case PropRole.MedGrass: return new Vector2(0.35f, 0.55f);
                case PropRole.TallGrass: return new Vector2(0.5f, 0.75f);
                case PropRole.Wildflowers: return new Vector2(0.3f, 0.5f);
                case PropRole.GenFlowers: return new Vector2(0.3f, 0.5f);
                case PropRole.FlatFlowers: return new Vector2(0.1f, 0.25f);
                case PropRole.GroundCover: return new Vector2(0.15f, 0.3f);
                case PropRole.SunflowerDetail: return new Vector2(1.2f, 1.6f);
                default: return new Vector2(0.3f, 0.5f);
            }
        }

        /// <summary>
        /// <c>DetailPrototype</c> scale ranges that put a mesh of <paramref name="nativeHeight"/> (visible height above its
        /// pivot) and <paramref name="nativeWidth"/> (largest horizontal extent) into the family's
        /// <see cref="TargetHeight"/>. Width scales with height (proportions kept) but is capped so an instance
        /// never exceeds <see cref="MaxDetailWidth"/>.
        /// </summary>
        public static (float minWidth, float maxWidth, float minHeight, float maxHeight) PrototypeScale(string family, float nativeHeight, float nativeWidth)
        {
            Vector2 target = TargetHeight(family);
            float h = FastMathf.Max(0.01f, nativeHeight);
            float minHeight = target.x / h;
            float maxHeight = target.y / h;
            float minWidth = minHeight;
            float maxWidth = maxHeight;
            if (nativeWidth > 0.01f)
            {
                float cap = MaxDetailWidth / nativeWidth;
                maxWidth = FastMathf.Min(maxWidth, cap);
                minWidth = FastMathf.Min(minWidth, maxWidth * 0.85f);
            }

            return (minWidth, maxWidth, minHeight, maxHeight);
        }

        /// <summary>The default detail table (one line per family).</summary>
        public static List<DetailRule> DefaultRules()
        {
            return new List<DetailRule>
            {
                new DetailRule(PropRole.TallGrass, B(0.6f, 0.12f, 0.35f, 0.35f, 0.9f), 1f) { patchSize = 26f, patchThreshold = -0.05f, dryAffinity = -0.3f, roadMargin = 2.5f },
                new DetailRule(PropRole.MedGrass, B(0.7f, 0.25f, 0.55f, 0.55f, 0.6f), 1.4f) { patchSize = 18f, patchThreshold = -0.15f, roadMargin = 1.5f },
                new DetailRule(PropRole.ShortGrass, B(0.55f, 0.3f, 0.75f, 0.6f, 0.3f), 1.6f) { patchSize = 12f, patchThreshold = -0.3f, dryAffinity = 0.4f, roadMargin = 0.3f },
                new DetailRule(PropRole.Wildflowers, B(0.45f, 0.05f, 0.2f, 0.25f, 0.2f), 0.75f) { patchSize = 35f, patchThreshold = 0.15f, dryAffinity = -0.2f, roadMargin = 1f },
                new DetailRule(PropRole.FlatFlowers, B(0.35f, 0.1f, 0.25f, 0.2f), 0.5f) { patchSize = 22f, patchThreshold = 0.2f, dryAffinity = 0.3f, roadMargin = 0.8f },
                new DetailRule(PropRole.GenFlowers, B(0.3f, 0.05f, 0.1f, 0.3f), 0.35f) { patchSize = 40f, patchThreshold = 0.3f, roadMargin = 1f },
                new DetailRule(PropRole.GroundCover, B(0.08f, 0.8f, 0.2f, 0f, 0.3f), 1f) { patchSize = 15f, patchThreshold = -0.1f, roadMargin = 1.5f },
                new DetailRule(PropRole.SunflowerDetail, B(0.03f, 0f, 0f, 0.3f), 0.3f) { patchSize = 60f, patchThreshold = 0.55f, dryAffinity = 0.3f, roadMargin = 3f },
            };
        }

        /// <summary>
        /// Builds one <see cref="DetailLayer"/> per (family, variant) that the palette provides. The variant of a
        /// cell is chosen by a hash so each prototype keeps its own sparse map.
        /// </summary>
        public static List<DetailLayer> Plan(WorldGenParams p, RoadDistanceField roads, LandCover cover, float[,,] splat,
            IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields, PaletteKeys keys, IReadOnlyList<DetailRule> rules)
        {
            return Plan(p, roads, cover, splat, sites, fields, keys, rules, 0, cover.Resolution);
        }

        /// <summary>
        /// <see cref="Plan(WorldGenParams, RoadDistanceField, LandCover, float[,,], IReadOnlyList{SettlementSite}, IReadOnlyList{FieldPatch}, PaletteKeys, IReadOnlyList{DetailRule})"/>
        /// for the square of <paramref name="count"/>² land-cover cells starting at cell (<paramref name="offset"/>,
        /// <paramref name="offset"/>) of <paramref name="cover"/> (whose grid <paramref name="roads"/> shares); <paramref name="splat"/>
        /// and the returned density maps cover only that square. In a window of an endless world (<see cref="WorldGenParams.window"/>)
        /// patch noise and the per-cell dither use absolute positions, so neighbouring tiles continue each other.
        /// </summary>
        public static List<DetailLayer> Plan(WorldGenParams p, RoadDistanceField roads, LandCover cover, float[,,] splat,
            IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields, PaletteKeys keys, IReadOnlyList<DetailRule> rules,
            int offset, int count)
        {
            int res = cover.Resolution;
            float texel = cover.TexelSize;
            float half = p.roadWidth * 0.5f;
            var layers = new List<DetailLayer>();
            TileWindow window = p.window;
            float ox = p.NoiseOriginX;
            float oz = p.NoiseOriginZ;
            long cellX0 = 0, cellZ0 = 0;
            if (window != null)
            {
                cellX0 = (long)Math.Round(window.originX / texel) + offset;
                cellZ0 = (long)Math.Round(window.originZ / texel) + offset;
            }

            // Endless road: only an inn's worn yard is thinned (its paint, TerrainShaper.InnYardCore), not the whole disc.
            float settlementMargin = window != null ? EndlessInnYardMargin : -8f;

            // Per-texel "can grow" mask shared by every rule: 0 on water, rock, dirt, settlement centres and tilled fields.
            var grow = new float[count * count];
            // The endless road's mountain channels (its splat has more than the shared eight): none on mountain rock, a little on
            // scree, thin tussocks on heath.
            bool mountainChannels = splat.GetLength(2) > TerrainShaper.HeathLayer;
            var weights = new BiomeWeights[count * count];
            var dryness = new float[count * count];
            for (int z = 0; z < count; z++)
            {
                float wz = (z + offset + 0.5f) * texel;
                for (int x = 0; x < count; x++)
                {
                    float wx = (x + offset + 0.5f) * texel;
                    int i = z * count + x;
                    int ci = (z + offset) * res + x + offset;
                    if (cover.IsWater(ci)) continue;
                    float g = (1f - splat[z, x, TerrainShaper.DirtLayer]) * (1f - splat[z, x, TerrainShaper.RockLayer])
                              * (1f - splat[z, x, TerrainShaper.CobbleLayer]) * (1f - 0.6f * splat[z, x, TerrainShaper.MudLayer]);
                    if (mountainChannels)
                    {
                        g *= (1f - splat[z, x, TerrainShaper.MountainRockLayer]) * (1f - 0.85f * splat[z, x, TerrainShaper.ScreeLayer])
                             * (1f - HeathThinning * splat[z, x, TerrainShaper.HeathLayer]);
                        // A pass range's turf is short and thin (the ground shows), its foothills more so as they rise into it.
                        if (cover.Mountain != null) g *= 1f - MountainThinning * cover.Mountain[ci];
                    }

                    if (g < 0.05f) continue;
                    if (InsideTilledField(fields, wx, wz, 0.5f)) continue;
                    if (ScatterPlanner.InsideSettlement(sites, wx, wz, settlementMargin)) g *= 0.25f;
                    grow[i] = g;
                    weights[i] = cover.Weights(ci);
                    dryness[i] = splat[z, x, TerrainShaper.DryGrassLayer] - splat[z, x, TerrainShaper.LushGrassLayer];
                }
            }

            for (int r = 0; r < rules.Count; r++)
            {
                DetailRule rule = rules[r];
                int variants = keys.DetailCount(rule.family);
                if (variants <= 0 || rule.perCell <= 0f) continue;
                var familyLayers = new DetailLayer[variants];
                for (int v = 0; v < variants; v++)
                {
                    familyLayers[v] = new DetailLayer { family = rule.family, variant = v, density = new byte[count * count] };
                    layers.Add(familyLayers[v]);
                }

                var patchNoise = new GradientNoise(p.seed + 200 + r);
                float frequency = 1f / FastMathf.Max(1f, rule.patchSize);
                float clearance = half + rule.roadMargin;
                uint salt = (uint)(r + 1) * 0x9E3779B9u;
                for (int z = 0; z < count; z++)
                {
                    float wz = (z + offset + 0.5f) * texel;
                    for (int x = 0; x < count; x++)
                    {
                        int i = z * count + x;
                        float g = grow[i];
                        if (g <= 0f) continue;
                        float roadDistance = roads[x + offset, z + offset];
                        if (roadDistance < clearance) continue;
                        int ci = (z + offset) * res + x + offset;
                        float affinity = weights[i].Dot(rule.biomes);
                        if (affinity <= 0.01f) continue;
                        float wx = (x + offset + 0.5f) * texel;
                        float n = patchNoise.Fbm((wx + ox) * frequency, (wz + oz) * frequency, 2);
                        float patch = FastMathf.Clamp01((n - rule.patchThreshold) * 3f + 0.5f);
                        if (rule.followFlowerPaint > 0f)
                        {
                            float painted = FastMathf.Clamp01(splat[z, x, TerrainShaper.FlowerLayer] * 2.2f - 0.15f);
                            patch = FastMathf.Lerp(patch, painted * (0.6f + 0.8f * patch), rule.followFlowerPaint);
                        }

                        if (patch <= 0f) continue;
                        float dry = 1f + rule.dryAffinity * dryness[i];
                        float roadFade = FastMathf.Lerp(RoadFadeMin, 1f, (roadDistance - clearance) / RoadFadeWidth);
                        float expected = rule.perCell * affinity * patch * g * FastMathf.Max(0f, dry) * roadFade;
                        uint h = window == null ? Hash((uint)ci ^ salt) : TileWindow.CellHash(cellX0 + x, cellZ0 + z, salt);
                        float dither = (h & 0xFFFF) / 65536f;
                        int count2 = (int)(expected + dither);
                        if (count2 <= 0) continue;
                        int variant = variants > 1 ? (int)((h >> 16) % (uint)variants) : 0;
                        familyLayers[variant].density[i] = (byte)FastMathf.Min(255, count2);
                    }
                }
            }

            return layers;
        }

        /// <summary>Endless road: share of the grass details heath paint holds back (thin tussocks between the heather).</summary>
        public const float HeathThinning = 0.6f;

        /// <summary>Endless road: share of the grass details a pass range holds back everywhere (its turf is short and thin).</summary>
        public const float MountainThinning = 0.3f;

        /// <summary>Endless road: margin on an inn's radius inside which grass is thinned (its worn yard).</summary>
        public const float EndlessInnYardMargin = -(SettlementSite.InnRadius - TerrainShaper.InnYardCore - 3f);

        /// <summary>Inside a field that is tilled earth (fields painted as grass – pasture, stubble, meadow strips – grow grass).</summary>
        private static bool InsideTilledField(IReadOnlyList<FieldPatch> fields, float x, float z, float margin)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Contains(x, z, margin) && !PropRole.IsGrassField(fields[i].crop)) return true;
            }

            return false;
        }

        /// <summary>
        /// The endless road's detail table: an even sward instead of patches. Medium grass is the base cover of lush ground and
        /// short grass of dry ground (each nearly everywhere: low patch thresholds), tall grass and the flowers are accents in
        /// patches. Same families and order as <see cref="DefaultRules"/> (so the per-tile layer cap is unchanged); the finite
        /// world keeps <see cref="DefaultRules"/> (its output is pinned by golden hashes).
        /// </summary>
        public static List<DetailRule> EndlessRules()
        {
            return new List<DetailRule>
            {
                new DetailRule(PropRole.TallGrass, B(0.6f, 0.3f, 0.35f, 0.35f, 0.9f), 0.8f) { patchSize = 26f, patchThreshold = 0f, dryAffinity = -0.5f, roadMargin = 2.5f },
                new DetailRule(PropRole.MedGrass, B(0.7f, 0.25f, 0.55f, 0.55f, 0.6f), 1.5f) { patchSize = 18f, patchThreshold = -0.45f, dryAffinity = -0.4f, roadMargin = 1.5f },
                new DetailRule(PropRole.ShortGrass, B(0.55f, 0.3f, 0.75f, 0.6f, 0.3f), 1.6f) { patchSize = 12f, patchThreshold = -0.55f, dryAffinity = 0.6f, roadMargin = 0.3f },
                new DetailRule(PropRole.Wildflowers, B(0.45f, 0.05f, 0.2f, 0.25f, 0.2f), 0.75f) { patchSize = 35f, patchThreshold = 0.15f, dryAffinity = -0.2f, roadMargin = 1f, followFlowerPaint = 0.5f },
                new DetailRule(PropRole.FlatFlowers, B(0.35f, 0.1f, 0.25f, 0.2f), 0.5f) { patchSize = 22f, patchThreshold = 0.2f, dryAffinity = 0.3f, roadMargin = 0.8f, followFlowerPaint = 0.5f },
                new DetailRule(PropRole.GenFlowers, B(0.3f, 0.05f, 0.1f, 0.3f), 0.35f) { patchSize = 40f, patchThreshold = 0.3f, roadMargin = 1f, followFlowerPaint = 0.5f },
                new DetailRule(PropRole.GroundCover, B(0.08f, 0.8f, 0.2f, 0f, 0.3f), 1.4f) { patchSize = 15f, patchThreshold = -0.35f, roadMargin = 1.5f },
                new DetailRule(PropRole.SunflowerDetail, B(0.03f, 0f, 0f, 0.3f), 0.3f) { patchSize = 60f, patchThreshold = 0.55f, dryAffinity = 0.3f, roadMargin = 3f },
            };
        }

        private static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352d;
                x ^= x >> 15;
                x *= 0x846ca68b;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
