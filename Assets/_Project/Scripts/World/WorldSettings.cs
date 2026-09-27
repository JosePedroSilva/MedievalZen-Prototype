using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// All inputs to world generation. Generation must be fully deterministic from these values
    /// (in particular <see cref="seed"/>); stages derive their own <c>System.Random</c> from it.
    /// </summary>
    [CreateAssetMenu(menuName = "MedievalZen/World Settings", fileName = "WorldSettings")]
    public sealed class WorldSettings : ScriptableObject
    {
        /// <summary>Smallest heightmap resolution Unity accepts (2^5 + 1).</summary>
        public const int MinHeightmapResolution = 33;

        /// <summary>Largest heightmap resolution Unity accepts (2^12 + 1).</summary>
        public const int MaxHeightmapResolution = 4097;

        [Header("Seed")]
        [Tooltip("Master seed. The same seed and settings always produce the same world.")]
        public int seed = 12345;

        [Header("Terrain")]
        [Tooltip("Side length of the square world, in metres.")]
        [Min(100f)]
        public float worldSize = 2000f;

        [Tooltip("Terrain heightmap resolution. Must be 2^n + 1 (33 … 4097); rounded on validate.")]
        [Range(MinHeightmapResolution, MaxHeightmapResolution)]
        public int heightmapResolution = 1025;

        [Tooltip("Total relief in metres: lowest lake bed to highest peak (terrain height range).")]
        [Range(0f, 600f)]
        public float hillAmplitude = 240f;

        [Tooltip("Base noise frequency in cycles per metre (0.002 ≈ one hill every 500 m).")]
        [Range(0.0001f, 0.02f)]
        public float hillFrequency = 0.002f;

        [Tooltip("Domain-warp strength in metres; bends the noise so hills look less regular.")]
        [Range(0f, 500f)]
        public float domainWarp = 80f;

        [Header("Relief shape")]
        [Tooltip("Relative height of the broad rolling lowland hills (metres before scaling to the total relief).")]
        [Range(0f, 200f)]
        public float rollingAmplitude = 60f;

        [Tooltip("Relative height of the ridged mountain mass (metres before scaling to the total relief).")]
        [Range(0f, 400f)]
        public float mountainAmplitude = 190f;

        [Tooltip("If on, the seed picks which side or corner of the map the highlands occupy; otherwise Mountain Mask Centre is used.")]
        public bool randomiseMountainSide = true;

        [Tooltip("Centre of the highland region in normalised map coordinates (0..1), used when Randomise Mountain Side is off.")]
        public Vector2 mountainMaskCentre = new Vector2(0.8f, 0.8f);

        [Tooltip("Radius of the highland region as a fraction of the map size.")]
        [Range(0.05f, 1.5f)]
        public float mountainMaskRadius = 0.6f;

        [Tooltip("Exponent applied to the ridge noise: higher = sharper ridgelines.")]
        [Range(1f, 4f)]
        public float ridgeSharpness = 2f;

        [Tooltip("0 = raw noise, 1 = broad flat valley floors (plains for roads and settlements).")]
        [Range(0f, 1f)]
        public float valleyFlatten = 0.6f;

        [Tooltip("Height of the occasional steep escarpments, in metres before scaling (0 = none).")]
        [Range(0f, 80f)]
        public float escarpmentHeight = 25f;

        [Header("Water")]
        [Tooltip("Fraction of the map below the lake water level (0 = no lakes).")]
        [Range(0f, 0.2f)]
        public float lakeCoverage = 0.035f;

        [Header("Settlements")]
        [Tooltip("Number of towns (largest settlements).")]
        [Range(0, 8)]
        public int townCount = 2;

        [Tooltip("Number of villages.")]
        [Range(0, 16)]
        public int villageCount = 3;

        [Tooltip("Number of roadside inns (placed along long road legs after routing).")]
        [Range(0, 16)]
        public int innCount = 2;

        [Tooltip("Minimum distance between any two settlements, in metres.")]
        [Min(10f)]
        public float minSettlementSpacing = 350f;

        [Header("Vegetation")]
        [Tooltip("Overall forest coverage: 0 = open meadow, 1 = dense woodland.")]
        [Range(0f, 1f)]
        public float forestDensity = 0.5f;

        [Header("Roads")]
        [Tooltip("Width of the flattened, dirt-painted road surface, in metres.")]
        [Range(1f, 20f)]
        public float roadWidth = 5f;

        [Header("Time")]
        [Tooltip("Real-time minutes for one full in-game day.")]
        [Range(1f, 240f)]
        public float dayLengthMinutes = 24f;

        /// <summary>Relief defaults this asset has been migrated to (0 = created before the dramatic-relief defaults).</summary>
        [SerializeField, HideInInspector]
        private int reliefVersion;

        /// <summary>Current relief-defaults version.</summary>
        public const int CurrentReliefVersion = 1;

        /// <summary>
        /// One-time upgrade of assets created before the dramatic-relief defaults (they carry hillAmplitude 50, which
        /// squashes the mountains flat). Returns true when a value changed; the caller saves the asset.
        /// </summary>
        public bool MigrateReliefDefaults()
        {
            if (reliefVersion >= CurrentReliefVersion) return false;
            reliefVersion = CurrentReliefVersion;
            if (hillAmplitude < 150f) hillAmplitude = 240f;
            return true;
        }

        /// <summary>Rounds <paramref name="resolution"/> to the nearest valid Unity heightmap resolution (2^n + 1).</summary>
        public static int ToValidHeightmapResolution(int resolution)
        {
            int best = MinHeightmapResolution;
            for (int size = MinHeightmapResolution; size <= MaxHeightmapResolution; size = (size - 1) * 2 + 1)
            {
                if (Mathf.Abs(size - resolution) < Mathf.Abs(best - resolution))
                {
                    best = size;
                }
            }

            return best;
        }

        private void OnValidate()
        {
            heightmapResolution = ToValidHeightmapResolution(heightmapResolution);
        }
    }
}
