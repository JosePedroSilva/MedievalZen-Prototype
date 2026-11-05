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

        [Header("Trees (read every frame: tune live in Play mode)")]
        [Range(20f, 300f), Tooltip("Near trees are drawn at full detail (LOD0) out to this distance, metres (times the detail scale).")]
        public float treeFullDetailDistance = 110f;

        [Range(40f, 420f), Tooltip("Near trees use their intermediate mesh LOD out to this distance and their simplest mesh LOD beyond (never a flat card), metres (times the detail scale).")]
        public float treeMidDetailDistance = 300f;

        [Range(0.5f, 2f), Tooltip("Multiplies both tree detail distances (lower = faster, higher = finer).")]
        public float treeDetailScale = 1f;

        [Range(0f, 400f), Tooltip("Near trees cast shadows (from their simplest mesh) out to this distance, metres; never beyond the pipeline's shadow distance (300 m). Was 90 m: trees on the next hill stood on unshaded ground and looked afloat.")]
        public float treeShadowDistance = 300f;

        [Range(0f, 300f), Tooltip("Forest pass: beyond this distance trees cast their shadow from a 26-triangle solid of their crown (walls and hedges from their box) instead of their simplest mesh LOD (700-6,600 triangles), metres.")]
        public float treeShadowProxyDistance = 100f;

        [Range(0f, 120f), Tooltip("Forest pass: bushes, ferns, undergrowth and ivy cast shadows only within this distance, metres.")]
        public float plantShadowDistance = 40f;

        [Range(60f, 420f), Tooltip("Forest pass: the woods' ferns are drawn out to this distance (thinning out over its last fifth), metres.")]
        public float fernDrawDistance = 160f;

        [Range(60f, 420f), Tooltip("Performance pass: the woods' undergrowth bushes are drawn out to this distance (thinning out over its last fifth), metres. They were drawn to the edge of the near tiles (up to ~750 m).")]
        public float undergrowthDrawDistance = 250f;

        [Tooltip("Performance pass: trees, plants and boundary pieces out of view cast no shadow when their shadow cannot reach the view (along the sun, down to the lowest ground nearby). No visible change; off = every caster within the shadow distance, as before.")]
        public bool treeShadowCasterCulling = true;

        [Tooltip("Performance pass: the instanced renderer submits its last pass over the cells again while the camera has moved under 1 m, turned under 2° and the sun under 0.5° (the pass is made with a view opened by 2°). Off = a fresh pass every frame.")]
        public bool treeWalkReuse = true;

        [Range(0f, 900f), Tooltip("Second performance pass: beyond this distance (times the detail scale) a near tile's tree is drawn as its solid low-poly impostor (the distant tiles' shape and colours, never a card) instead of its simplest mesh LOD, metres; 0 = never. Trees whose impostor outline differs more than treeImpostorMaxPixels there swap farther out.")]
        public float treeImpostorDistance = 320f;

        [Range(0f, 8f), Tooltip("Second performance pass: a tree swaps to its impostor only where the measured mean outline difference (impostor against the simplest mesh LOD) spans at most this many 1080p pixels (60° view); 0 = every tree at treeImpostorDistance. Lobed impostors (2026-10-06) match within 3 px from about 230-365 m.")]
        public float treeImpostorMaxPixels = 3f;

        [HideInInspector, Tooltip("Version of these settings' one-time upgrades (EndlessSettingsMigration); set by the editor.")]
        public int settingsVersion;

        [Header("Distant tiles")]
        [Min(256f), Tooltip("Distant tiles within this distance use the finer 4 m mesh (TileLod.Mid); beyond it the 8 m mesh, out to the far distance.")]
        public float midDistance = 1000f;

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
