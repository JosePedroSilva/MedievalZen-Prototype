using System;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Plain-data copy of <see cref="WorldSettings"/> consumed by the pure generation stages, so they
    /// (and their tests) never need a ScriptableObject instance. Create one with <see cref="From"/>.
    /// </summary>
    [Serializable]
    public sealed class WorldGenParams
    {
        /// <summary>Master seed; every stage derives its own <c>System.Random</c> from it.</summary>
        public int seed = 12345;

        /// <summary>Side length of the square world in metres.</summary>
        public float worldSize = 2000f;

        /// <summary>Heightmap resolution (2^n + 1).</summary>
        public int heightmapResolution = 1025;

        /// <summary>Total relief in metres (normalised height 1 = this many metres).</summary>
        public float hillAmplitude = 240f;

        /// <summary>Base noise frequency in cycles per metre.</summary>
        public float hillFrequency = 0.002f;

        /// <summary>Domain-warp strength in metres.</summary>
        public float domainWarp = 80f;

        /// <summary>Relative height of the rolling lowland hills.</summary>
        public float rollingAmplitude = 60f;

        /// <summary>Relative height of the ridged mountain mass.</summary>
        public float mountainAmplitude = 190f;

        /// <summary>Seed picks the highland side/corner (otherwise <see cref="mountainMaskCentre"/>).</summary>
        public bool randomiseMountainSide = true;

        /// <summary>Normalised centre of the highland region.</summary>
        public Vector2 mountainMaskCentre = new Vector2(0.8f, 0.8f);

        /// <summary>Highland radius as a fraction of the map size.</summary>
        public float mountainMaskRadius = 0.6f;

        /// <summary>Ridge sharpness exponent.</summary>
        public float ridgeSharpness = 2f;

        /// <summary>Valley-floor flattening 0..1.</summary>
        public float valleyFlatten = 0.6f;

        /// <summary>Escarpment height (relative metres).</summary>
        public float escarpmentHeight = 25f;

        /// <summary>Crest height of the col between the central summits (the pass), fraction of <see cref="mountainAmplitude"/>.</summary>
        public float passHeight = 0.76f;

        /// <summary>Strength of the drainage-valley carving in the highlands, 0 (none) .. 1 (deep).</summary>
        public float valleyCarving = 0.5f;

        /// <summary>Fraction of the total relief above which no broadleaf trees grow (pines thin out above it).</summary>
        public float treeLine = 0.7f;

        /// <summary>Place a mountain inn near the pass and connect it with grade-limited pass roads.</summary>
        public bool mountainInn = true;

        /// <summary>Design grade (rise over run) of the pass road; lowland roads use <see cref="LowlandRoadGrade"/>.</summary>
        public float maxRoadGrade = 0.12f;

        /// <summary>Design grade of the lowland roads.</summary>
        public const float LowlandRoadGrade = 0.15f;

        /// <summary>Hard limit for any road: steepest 10 m stretch after carving (rise over run).</summary>
        public const float HardRoadGrade = 0.18f;

        /// <summary>Fraction of the map under water (0 = no lakes).</summary>
        public float lakeCoverage = 0.035f;

        /// <summary>Number of towns.</summary>
        public int townCount = 2;

        /// <summary>Number of villages.</summary>
        public int villageCount = 3;

        /// <summary>Maximum number of roadside inns.</summary>
        public int innCount = 2;

        /// <summary>Minimum distance between towns/villages in metres.</summary>
        public float minSettlementSpacing = 350f;

        /// <summary>Forest coverage 0..1.</summary>
        public float forestDensity = 0.5f;

        /// <summary>Road surface width in metres.</summary>
        public float roadWidth = 5f;

        /// <summary>Alphamap (splat) resolution; 0 = derive from the world size (about 1 texel per metre, power of two).</summary>
        public int alphamapResolutionOverride;

        /// <summary>
        /// Alphamap (splat) resolution actually used: a power of two in 16..4096 close to worldSize (1 m texels, so a 5 m
        /// road has a crisp edge), or the override. 2048 on the default 2 km map.
        /// </summary>
        public int AlphamapResolution
        {
            get
            {
                int target = alphamapResolutionOverride > 0 ? alphamapResolutionOverride : Mathf.Max(16, Mathf.RoundToInt(worldSize));
                return PowerOfTwoNear(target, alphamapResolutionOverride > 0);
            }
        }

        /// <summary>
        /// Resolution of the land-cover grid shared by the road distance field, biome fields, scatter and the terrain
        /// detail (grass) maps: about 2 m cells (worldSize / 2, power of two), never finer than <see cref="AlphamapResolution"/>.
        /// 1024 on the default 2 km map. Detail densities are "instances per cell", so this must not follow the finer splat.
        /// </summary>
        public int CoverResolution => Math.Min(PowerOfTwoNear(Mathf.Max(16, Mathf.RoundToInt(worldSize * 0.5f)), false), AlphamapResolution);

        private static int PowerOfTwoNear(int target, bool nearest)
        {
            // Managed code only (no native Mathf helpers) so the pure stages run outside Unity too.
            int pow = 16;
            while (pow < target && pow < 4096) pow <<= 1;
            if (nearest && pow > 16 && pow - target > target - pow / 2) pow >>= 1;
            return pow;
        }

        /// <summary>Half the road width plus the scatter-free margin (8 m) on each side.</summary>
        public float ScatterCorridor => roadWidth * 0.5f + 8f;

        /// <summary>
        /// Set when the stages run on a window of an endless world (see <see cref="TileWindow"/>): local coordinates
        /// 0..<see cref="worldSize"/> then start at an absolute origin, and noise, patch patterns, scatter chunks and detail
        /// dithering continue across window edges. Null for the finite world (which is unchanged by it).
        /// </summary>
        [NonSerialized]
        public TileWindow window;

        /// <summary>X offset added to local coordinates before noise lookups (0 for the finite world).</summary>
        public float NoiseOriginX => window != null ? window.noiseOriginX : 0f;

        /// <summary>Z offset added to local coordinates before noise lookups (0 for the finite world).</summary>
        public float NoiseOriginZ => window != null ? window.noiseOriginZ : 0f;

        /// <summary>Copies the generation inputs from <paramref name="settings"/>.</summary>
        public static WorldGenParams From(WorldSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return new WorldGenParams
            {
                seed = settings.seed,
                worldSize = settings.worldSize,
                heightmapResolution = WorldSettings.ToValidHeightmapResolution(settings.heightmapResolution),
                hillAmplitude = settings.hillAmplitude,
                hillFrequency = settings.hillFrequency,
                domainWarp = settings.domainWarp,
                rollingAmplitude = settings.rollingAmplitude,
                mountainAmplitude = settings.mountainAmplitude,
                randomiseMountainSide = settings.randomiseMountainSide,
                mountainMaskCentre = settings.mountainMaskCentre,
                mountainMaskRadius = settings.mountainMaskRadius,
                ridgeSharpness = settings.ridgeSharpness,
                valleyFlatten = settings.valleyFlatten,
                escarpmentHeight = settings.escarpmentHeight,
                passHeight = settings.passHeight,
                valleyCarving = settings.valleyCarving,
                treeLine = settings.treeLine,
                mountainInn = settings.mountainInn,
                maxRoadGrade = settings.maxRoadGrade,
                lakeCoverage = settings.lakeCoverage,
                townCount = settings.townCount,
                villageCount = settings.villageCount,
                innCount = settings.innCount,
                minSettlementSpacing = settings.minSettlementSpacing,
                forestDensity = settings.forestDensity,
                roadWidth = settings.roadWidth,
            };
        }

        /// <summary>Shallow copy (for tests that tweak one value).</summary>
        public WorldGenParams Clone() => (WorldGenParams)MemberwiseClone();
    }
}
