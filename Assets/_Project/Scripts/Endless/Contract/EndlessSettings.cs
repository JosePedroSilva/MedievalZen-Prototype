using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Settings of the endless-road mode: what world to generate (<see cref="ToParams"/>) and how to stream it around
    /// the traveller.
    /// </summary>
    [CreateAssetMenu(menuName = "MedievalZen/Endless Road Settings", fileName = "EndlessSettings")]
    public sealed class EndlessSettings : ScriptableObject
    {
        [Header("World")]
        [Tooltip("World seed. The same seed always gives the same road.")]
        public int seed = 12345;

        [Min(0.1f), Tooltip("Real minutes per in-game day.")]
        public float dayLengthMinutes = 24f;

        [Range(3f, 8f), Tooltip("Road width in metres.")]
        public float roadWidth = 5f;

        [Range(0.03f, 0.12f), Tooltip("Steepest grade of the road (rise over run).")]
        public float maxRoadGrade = 0.08f;

        [Min(300f), Tooltip("Shortest stretch of road between two inns, in metres.")]
        public float innSpacingMin = 1200f;

        [Min(300f), Tooltip("Longest stretch of road between two inns, in metres.")]
        public float innSpacingMax = 2400f;

        [Min(500f), Tooltip("Shortest length of one kind of landscape along the road, in metres.")]
        public float regionLengthMin = 1500f;

        [Min(500f), Tooltip("Longest length of one kind of landscape along the road, in metres.")]
        public float regionLengthMax = 3500f;

        [Header("Tiles")]
        [Min(64f), Tooltip("Edge length of a terrain tile in metres.")]
        public float tileSize = 256f;

        [Min(100f), Tooltip("Vertical extent of the terrain in metres (all heights lie within it).")]
        public float heightRange = 420f;

        [Header("Streaming")]
        [Min(128f), Tooltip("Tiles within this distance of the traveller are built at full detail (walkable).")]
        public float nearDistance = 420f;

        [Min(256f), Tooltip("Tiles out to this distance are built as coarse silhouettes.")]
        public float farDistance = 2200f;

        [Min(0f), Tooltip("Extra distance a tile may fall behind before it is released (avoids thrashing when turning round).")]
        public float releaseMargin = 160f;

        [Range(0.5f, 8f), Tooltip("Main-thread milliseconds per frame the streamer may spend applying finished tiles.")]
        public float applyBudgetMs = 2.5f;

        [Range(1, 8), Tooltip("Tiles generated in parallel on worker threads.")]
        public int workerCount = 3;

        [Min(500f), Tooltip("When the traveller is this far from the scene origin, everything is shifted back to it (keeps positions precise on a long walk).")]
        public float originShiftDistance = 4000f;

        /// <summary>The plain generator inputs for these settings.</summary>
        public EndlessParams ToParams()
        {
            return new EndlessParams
            {
                seed = seed,
                tileSize = tileSize,
                heightRange = heightRange,
                roadWidth = roadWidth,
                maxRoadGrade = maxRoadGrade,
                innSpacingMin = Mathf.Min(innSpacingMin, innSpacingMax),
                innSpacingMax = Mathf.Max(innSpacingMin, innSpacingMax),
                regionLengthMin = Mathf.Min(regionLengthMin, regionLengthMax),
                regionLengthMax = Mathf.Max(regionLengthMin, regionLengthMax),
            };
        }
    }
}
