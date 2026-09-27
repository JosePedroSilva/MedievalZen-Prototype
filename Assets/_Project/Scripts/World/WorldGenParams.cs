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

        /// <summary>Alphamap (splat) resolution; 0 = derive from the world size (about worldSize / 2, power of two).</summary>
        public int alphamapResolutionOverride;

        /// <summary>Alphamap resolution actually used: a power of two in 16..4096 close to worldSize / 2 (or the override).</summary>
        public int AlphamapResolution
        {
            get
            {
                // Managed code only (no native Mathf helpers) so the pure stages run outside Unity too.
                int target = alphamapResolutionOverride > 0 ? alphamapResolutionOverride : Mathf.Max(16, Mathf.RoundToInt(worldSize * 0.5f));
                int pow = 16;
                while (pow < target && pow < 4096) pow <<= 1;
                if (alphamapResolutionOverride > 0 && pow > 16 && pow - target > target - pow / 2) pow >>= 1;
                return pow;
            }
        }

        /// <summary>Half the road width plus the scatter-free margin (8 m) on each side.</summary>
        public float ScatterCorridor => roadWidth * 0.5f + 8f;

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
