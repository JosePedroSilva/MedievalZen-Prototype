using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Keeps pooled Synty ambient particle prefabs alive around the player, gated by time of day and weather:
    /// by day butterflies and petals over open meadow, dust motes, drifting leaves under forest, sun shafts in dense forest
    /// when the sun is low, wind streaks in hazy/overcast weather and a few high circling birds over open country; at night
    /// fireflies near water and forest edges (a code-built particle system with the additive <c>MedievalZen/FireflyGlow</c>
    /// material); and lake ripples near the shore the player is looking at. Since 2026-10-02 also: leaves falling from the
    /// birches near the traveller (autumn-coloured), pollen and seed glints drifting on the wind over sunlit open ground,
    /// butterflies that seek out flower patches (<see cref="FlowerSampler"/>), and small flocks crossing the sky by day
    /// (<see cref="SkyBirds"/>, ticked every frame). Forest vs meadow comes from a coarse
    /// <see cref="TreeDensityGrid"/> built once from the Terrain's tree instances. Effects that fall out of season stop
    /// emitting and are recycled once their particles have died, so nothing pops. Cosmetic only, so it uses
    /// <c>UnityEngine.Random</c>.
    /// <para>
    /// Forest pass (2026-10-05, endless road while <see cref="ForestMood.Allowed"/>): under a canopy, soft shafts of sunlight slant
    /// between the trunks along the sun's direction (<see cref="DayCycle.LightDirection"/>), strongest with a low sun and none in rain
    /// or thick overcast (<see cref="ForestMood.ShaftStrength"/>), with warm dust motes drifting in them. Both are code-built
    /// particle systems with copies of the first-party additive glow material (the Synty sun-beam and dust effects use built-in
    /// particle shaders). They replace the Synty sun beam in the endless woods; the finite world is unchanged.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientFxSpawner : MonoBehaviour, IOriginShiftable
    {
        private enum Kind
        {
            Meadow,
            Forest,
            SunBeam,
            Dust,
            Streak,
            Bird,
            Firefly,
            Ripple,
            AutumnLeaves,
            Pollen,
            ForestShaft,
            ForestDust,
            Blossom,
        }

        private sealed class Slot
        {
            public GameObject Prefab;
            public GameObject Instance;
            public ParticleSystem[] Systems;
            public Kind Kind;
            public bool Retiring;
            public float RetireDeadline;
        }

        [Header("References")]
        [SerializeField, Tooltip("Transform the effects gather around (the player rig). Falls back to the main camera.")]
        private Transform target;

        [SerializeField, Tooltip("Terrain to read tree density and heights from. Defaults to the scene world's surface (GameWorld.Current.Surface: the baked terrain, or the endless road's tiles).")]
        private Terrain terrain = null;

        [Header("Prefabs")]
        [SerializeField, Tooltip("Effects for open meadow by day (butterflies, petals).")]
        private GameObject[] meadowFx = Array.Empty<GameObject>();

        [SerializeField, Tooltip("Effects under forest (falling / blowing leaves).")]
        private GameObject[] forestFx = Array.Empty<GameObject>();

        [SerializeField, Tooltip("Sun beam effect placed in dense forest when the sun is low.")]
        private GameObject sunBeamFx = null;

        [SerializeField, Tooltip("Dust motes drifting in the light (Meadow FX_Dust_Spots_01).")]
        private GameObject dustFx = null;

        [SerializeField, Tooltip("Wind streaks, shown in hazy/overcast weather (Meadow FX_Wind_Streaks_01).")]
        private GameObject windStreakFx = null;

        [SerializeField, Tooltip("High circling birds (Arid FX_Vulture_01).")]
        private GameObject birdFx = null;

        [SerializeField, Tooltip("Lake ripples near the shore (Meadow FX_Water_Movement_01).")]
        private GameObject rippleFx = null;

        [SerializeField, Tooltip("Additive glow material for the code-built fireflies (MedievalZen/FireflyGlow). No fireflies without it.")]
        private Material fireflyMaterial = null;

        [Header("Placement")]
        [SerializeField, Range(0, 12), Tooltip("Fewest ground effects to keep alive.")]
        private int minActive = 3;

        [SerializeField, Range(1, 12), Tooltip("Most ground effects alive at once (birds, fireflies and ripples have their own caps).")]
        private int maxActive = 6;

        [SerializeField, Min(1f), Tooltip("Closest spawn distance from the player, in metres.")]
        private float minSpawnDistance = 8f;

        [SerializeField, Min(2f), Tooltip("Furthest spawn distance from the player, in metres.")]
        private float maxSpawnDistance = 28f;

        [SerializeField, Min(5f), Tooltip("Effects further than this from the player are recycled.")]
        private float despawnDistance = 40f;

        [SerializeField, Min(0.1f), Tooltip("Seconds between spawn/despawn checks (one spawn per check keeps it gentle).")]
        private float checkInterval = 1.2f;

        [SerializeField, Min(0f), Tooltip("Trees per 100 m² at or below which the ground counts as meadow.")]
        private float meadowMaxDensity = 0.15f;

        [SerializeField, Min(0f), Tooltip("Trees per 100 m² at or above which the ground counts as forest.")]
        private float forestMinDensity = 0.8f;

        [SerializeField, Tooltip("Sun elevation range (degrees) in which sun shafts appear in dense forest (low sun).")]
        private Vector2 sunBeamElevation = new Vector2(4f, 28f);

        [SerializeField, Min(1f), Tooltip("Tree density grid cell size in metres.")]
        private float gridCellSize = 16f;

        [Header("Birds")]
        [SerializeField, Tooltip("How many circling flocks over open country by day (min, max).")]
        private Vector2Int birdCount = new Vector2Int(2, 3);

        [SerializeField, Tooltip("Height above the ground of the circling birds (metres).")]
        private Vector2 birdAltitude = new Vector2(40f, 70f);

        [SerializeField, Tooltip("Horizontal distance of the birds from the player (metres).")]
        private Vector2 birdDistance = new Vector2(90f, 220f);

        [SerializeField, Min(50f), Tooltip("Birds further than this are recycled.")]
        private float birdDespawnDistance = 420f;

        [Header("Night and water")]
        [SerializeField, Range(0, 8), Tooltip("Most firefly swarms at once.")]
        private int fireflyMax = 4;

        [SerializeField, Range(0, 4), Tooltip("Most lake-ripple effects at once.")]
        private int rippleMax = 2;

        [Header("Light and motion")]
        [SerializeField, Tooltip("Material of the small birds crossing the sky (MedievalZen/Atmosphere/BirdSilhouette.mat, URP Unlit, dark). A dark URP Unlit material is made at runtime when empty.")]
        private Material birdMaterial = null;

        [SerializeField, Tooltip("Small flocks crossing the sky by day (in addition to the circling birds).")]
        private bool crossingBirds = true;

        [SerializeField, Range(0, 4), Tooltip("Most falling-leaf effects under autumn-coloured trees (the birches) at once.")]
        private int autumnLeavesMax = 2;

        [SerializeField, Tooltip("Distance from the traveller of the birches that drop leaves (min, max), metres.")]
        private Vector2 autumnLeavesDistance = new Vector2(6f, 36f);

        [SerializeField, Tooltip("Colours of the falling birch leaves (each leaf picks between the two).")]
        private Color autumnLeafA = new Color(0.96f, 0.55f, 0.16f);

        [SerializeField, Tooltip("Colours of the falling birch leaves (each leaf picks between the two).")]
        private Color autumnLeafB = new Color(0.98f, 0.78f, 0.26f);

        [SerializeField, Tooltip("Colours of the spring blossom drifting from the fruit trees (the falling-leaf effect recoloured; each petal picks between the two).")]
        private Color blossomA = new Color(1f, 0.86f, 0.9f);

        [SerializeField, Tooltip("Colours of the spring blossom drifting from the fruit trees (the falling-leaf effect recoloured; each petal picks between the two).")]
        private Color blossomB = new Color(1f, 0.97f, 0.95f);

        [SerializeField, Range(0, 4), Tooltip("Most blossom drifts under fruit trees at once at the height of spring (SeasonFx.Blossom).")]
        private int blossomMax = 2;

        [SerializeField, Range(0, 4), Tooltip("Most pollen/seed drifts at once (sunlit open ground by day, never in rain).")]
        private int pollenMax = 2;

        [SerializeField, Min(0f), Tooltip("Flower details per detail cell (around a spawn point) above which butterflies come to the flowers.")]
        private float butterflyFlowerDensity = 0.6f;

        /// <summary>Wind streaks spawn at least this far from the camera (they fly ~14 m/s along the wind).</summary>
        private const float StreakMinDistance = 28f;

        /// <summary>Wind streaks spawn at most this far from the camera.</summary>
        private const float StreakMaxDistance = 60f;

        /// <summary>Wind streaks are recycled beyond this distance from the player.</summary>
        private const float StreakDespawnDistance = 90f;

        /// <summary>A streak's flight path must keep this far from the camera and from the camera-to-player line.</summary>
        private const float StreakClearance = 22f;

        private readonly List<Slot> active = new List<Slot>(24);
        private readonly HashSet<GameObject> softened = new HashSet<GameObject>();
        private readonly Dictionary<GameObject, Stack<GameObject>> pool = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Dictionary<GameObject, ParticleSystem[]> systemsCache = new Dictionary<GameObject, ParticleSystem[]>();
        private ITreeDensity grid;
        private IWorldSurface surface;
        private GameObject fireflyTemplate;
        private GameObject autumnTemplate;
        private GameObject blossomTemplate;
        private GameObject pollenTemplate;
        private GameObject shaftTemplate;
        private GameObject forestDustTemplate;

        /// <summary>Most forest sun-shaft emitters (and as many dust drifts) at once.</summary>
        private const int ShaftMax = 3;
        private SkyBirds skyBirds;
        private readonly AutumnTreeFinder autumnTrees = new AutumnTreeFinder();
        private readonly AutumnTreeFinder fruitTrees = new AutumnTreeFinder(AutumnTreeFinder.FruitKey);
        private float nextCheck;
        private int wanted;
        private int wantedBirds;
        private bool started;

        // Prewarm without world collision (2026-10-09): every Synty effect here prewarms (a whole 5 s loop simulated in the frame it
        // starts), and the petals and falling leaves (also the autumn and blossom copies) collide with the world at high quality, so
        // that prewarm raycast every particle at every step: the ~12 ms AmbientFxSpawner frame at nearly every tour stop. Their
        // collision is switched off for the frame they start and back on in the next one; the look is the same (a prewarmed leaf
        // that would have come to rest on the ground falls through it instead, out of sight), the cost is gone.
        private readonly List<ParticleSystem> collisionRestore = new List<ParticleSystem>(8);
        private int collisionFrame;

        // The arrival warm-up (2026-10-09): one instance of every effect is made behind the hold's black screen, shown for one
        // frame in front of the camera (its shader and particle renderer meet the GPU) and parked in the pool.
        private List<GameObject> warmQueue;
        private int warmIndex;
        private GameObject warmPrefab, warmInstance;
        private static readonly int CostInstantiate = Review.FrameCost.Register("AmbientFxSpawner.Instantiate");
        private static readonly int CostPlay = Review.FrameCost.Register("AmbientFxSpawner.Play (prewarm)");

        private void Start()
        {
            // An inspector-assigned terrain wins; otherwise the scene's world surface (the finite world's terrain, or the
            // endless road's streamed tiles).
            surface = terrain != null ? new TerrainSurface(terrain) : GameWorld.Current != null ? GameWorld.Current.Surface : null;
            grid = surface != null ? surface.CreateTreeDensity(gridCellSize) : TreeDensityGrid.FromTerrain(null, gridCellSize);
            wanted = Random.Range(minActive, Mathf.Max(minActive, maxActive) + 1);
            wantedBirds = Random.Range(birdCount.x, Mathf.Max(birdCount.x, birdCount.y) + 1);
            if (fireflyMaterial != null) fireflyTemplate = BuildFireflyTemplate(fireflyMaterial, transform);
            if (fireflyMaterial != null) pollenTemplate = BuildPollenTemplate(fireflyMaterial, transform);
            if (fireflyMaterial != null) shaftTemplate = BuildShaftTemplate(fireflyMaterial, transform);
            if (fireflyMaterial != null) forestDustTemplate = BuildForestDustTemplate(fireflyMaterial, transform);
            autumnTemplate = BuildAutumnTemplate(FindNamed(forestFx, "Leaves_Meadows"), transform, autumnLeafA, autumnLeafB);
            blossomTemplate = BuildAutumnTemplate(FindNamed(forestFx, "Leaves_Meadows"), transform, blossomA, blossomB);
            if (blossomTemplate != null) blossomTemplate.name = "FX_Blossom_Spring";
            if (crossingBirds) skyBirds = new SkyBirds(birdMaterial);
            started = true;
        }

        /// <summary>True once the effect templates exist (after Start): the arrival warm-up waits for it.</summary>
        public bool WarmReady => started;

        /// <summary>True until every effect has been warmed once (and the last one parked).</summary>
        public bool WarmPending => !started || warmQueue == null || warmIndex < warmQueue.Count || warmInstance != null;

        /// <summary>Effects the warm-up covers (0 before Start).</summary>
        public int WarmCount => warmQueue != null ? warmQueue.Count : 0;

        /// <summary>
        /// One step of the arrival warm-up: parks the effect shown last frame, then makes (or takes from the pool) the next effect,
        /// switches it on at <paramref name="at"/> and plays it for this frame. False when there was nothing left to show.
        /// </summary>
        public bool WarmStep(Vector3 at)
        {
            ParkWarm();
            if (!started) return false;
            if (warmQueue == null)
            {
                warmQueue = new List<GameObject>(24);
                void Add(GameObject g)
                {
                    if (g != null && !warmQueue.Contains(g)) warmQueue.Add(g);
                }

                foreach (GameObject g in meadowFx) Add(g);
                foreach (GameObject g in forestFx) Add(g);
                Add(sunBeamFx);
                Add(dustFx);
                Add(windStreakFx);
                Add(birdFx);
                Add(rippleFx);
                Add(fireflyTemplate);
                Add(autumnTemplate);
                Add(blossomTemplate);
                Add(pollenTemplate);
                Add(shaftTemplate);
                Add(forestDustTemplate);
            }

            if (warmIndex >= warmQueue.Count) return false;
            GameObject prefab = warmQueue[warmIndex++];
            GameObject instance = Take(prefab);
            instance.transform.SetPositionAndRotation(at, Quaternion.identity);
            StartAll(instance, SystemsOf(instance));
            warmPrefab = prefab;
            warmInstance = instance;
            return true;
        }

        /// <summary>Parks the effect the warm-up showed (the end of a hold).</summary>
        public void EndWarm() => ParkWarm();

        private void ParkWarm()
        {
            if (warmInstance == null)
            {
                warmPrefab = null;
                return;
            }

            Recycle(new Slot { Prefab = warmPrefab, Instance = warmInstance, Systems = SystemsOf(warmInstance) });
            warmPrefab = null;
            warmInstance = null;
        }

        /// <summary>
        /// Switches an inactive (pooled) instance on and plays its systems; a prewarming system with world collision starts without
        /// its collision, switched off before the instance wakes (play on awake prewarms there) and back on next frame
        /// (see <see cref="collisionRestore"/>).
        /// </summary>
        private void StartAll(GameObject instance, ParticleSystem[] systems)
        {
            using Review.FrameCost.Scope cost = Review.FrameCost.Measure(CostPlay);
            collisionFrame = Time.frameCount;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                if (system == null) continue;
                ParticleSystem.CollisionModule collision = system.collision;
                if (!collision.enabled || !system.main.prewarm) continue;
                collision.enabled = false;
                collisionRestore.Add(system);
            }

            instance.SetActive(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null) systems[i].Play(false);
            }
        }

        /// <summary>Switches back on the collision a prewarm went without (the frame after it started).</summary>
        private void RestoreCollision()
        {
            for (int i = 0; i < collisionRestore.Count; i++)
            {
                ParticleSystem system = collisionRestore[i];
                if (system == null) continue;
                ParticleSystem.CollisionModule collision = system.collision;
                collision.enabled = true;
            }

            collisionRestore.Clear();
        }

        private void OnDestroy()
        {
            skyBirds?.Dispose();
            skyBirds = null;
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("AmbientFxSpawner");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (collisionRestore.Count > 0 && Time.frameCount > collisionFrame) RestoreCollision();
            if (skyBirds != null)
            {
                // Every frame: the crossing flock moves and is drawn (spawning is gated like the circling birds).
                Transform follow = target != null ? target : Camera.main != null ? Camera.main.transform : null;
                if (follow != null)
                {
                    float sun = DayCycle.Current != null ? DayCycle.Current.SunElevation : 40f;
                    skyBirds.Tick(Time.deltaTime, follow.position, Allowed(Kind.Bird, sun, WeatherSystem.CurrentWeather), surface);
                }
            }

            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            if (target == null)
            {
                Camera main = Camera.main;
                if (main == null) return;
                target = main.transform;
            }

            Vector3 centre = target.position;
            WeatherPreset weather = WeatherSystem.CurrentWeather;
            float elevation = DayCycle.Current != null ? DayCycle.Current.SunElevation : 40f;

            int ground = 0, birds = 0, fireflies = 0, ripples = 0, autumn = 0, pollen = 0, shafts = 0, dust = 0, blossom = 0;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Slot slot = active[i];
                if (slot.Instance == null)
                {
                    active.RemoveAt(i);
                    continue;
                }

                Vector3 offset = slot.Instance.transform.position - centre;
                offset.y = 0f;
                float limit = slot.Kind == Kind.Bird ? birdDespawnDistance : slot.Kind == Kind.Streak ? StreakDespawnDistance : despawnDistance;
                if (offset.sqrMagnitude > limit * limit)
                {
                    Recycle(slot);
                    active.RemoveAt(i);
                    continue;
                }

                if ((slot.Kind == Kind.Pollen || slot.Kind == Kind.Dust || slot.Kind == Kind.ForestDust) && InPassRange())
                {
                    Recycle(slot);   // gone at once, particles and all: a fading drift still reads as snow
                    active.RemoveAt(i);
                    continue;
                }

                if (!slot.Retiring && !Allowed(slot.Kind, elevation, weather)) Retire(slot);
                if (slot.Retiring && (!AnyAlive(slot) || Time.time > slot.RetireDeadline))
                {
                    Recycle(slot);
                    active.RemoveAt(i);
                    continue;
                }

                switch (slot.Kind)
                {
                    case Kind.Bird: birds++; break;
                    case Kind.Firefly: fireflies++; break;
                    case Kind.Ripple: ripples++; break;
                    case Kind.AutumnLeaves: autumn++; break;
                    case Kind.Pollen: pollen++; break;
                    case Kind.ForestShaft: shafts++; break;
                    case Kind.ForestDust: dust++; break;
                    case Kind.Blossom: blossom++; break;
                    default: ground++; break;
                }
            }

            // The season sets how many of the seasonal effects may live (SeasonFx: exactly the maxima at autumn, no blossom).
            float year = Season.Year;
            if (ground < wanted) TrySpawnGround(centre, elevation, weather);
            if (birds < wantedBirds && Allowed(Kind.Bird, elevation, weather)) TrySpawnBird(centre);
            if (fireflies < SeasonFx.Fireflies(fireflyMax, year) && Allowed(Kind.Firefly, elevation, weather)) TrySpawnFireflies(centre);
            if (ripples < rippleMax) TrySpawnRipple(centre);
            if (autumn < SeasonFx.FallingLeaves(autumnLeavesMax, year) && Allowed(Kind.AutumnLeaves, elevation, weather)) TrySpawnAutumnLeaves(centre);
            if (blossom < SeasonFx.Blossom(blossomMax, year) && Allowed(Kind.Blossom, elevation, weather)) TrySpawnBlossom(centre);
            if (pollen < SeasonFx.Pollen(pollenMax, year) && Allowed(Kind.Pollen, elevation, weather)) TrySpawnPollen(centre);
            if (shafts < ShaftMax && Allowed(Kind.ForestShaft, elevation, weather)) TrySpawnShaft(centre, elevation, weather, dust < ShaftMax);
            if (Random.value < 0.05f) wanted = Random.Range(minActive, Mathf.Max(minActive, maxActive) + 1);
        }

        private bool Allowed(Kind kind, float elevation, in WeatherPreset weather)
        {
            switch (kind)
            {
                case Kind.Meadow: return elevation > 6f && weather.rain < 0.15f;
                case Kind.Forest: return weather.rain < 0.6f;
                case Kind.Dust: return elevation > 4f && weather.rain < 0.1f && weather.overcast < 0.6f && !InPassRange();
                case Kind.SunBeam: return elevation >= sunBeamElevation.x && elevation <= sunBeamElevation.y && weather.sunMultiplier > 0.75f && weather.rain < 0.05f;
                case Kind.Streak: return weather.windStreaks > 0.3f && weather.rain < 0.5f;
                case Kind.Bird: return elevation > 8f && weather.rain < 0.1f;
                case Kind.Firefly: return elevation < -3f && weather.rain < 0.2f;
                case Kind.AutumnLeaves: return elevation > -4f && weather.rain < 0.5f;
                case Kind.Blossom: return elevation > -4f && weather.rain < 0.5f;   // the falling leaves' gate (spring only by count: SeasonFx.Blossom)
                case Kind.Pollen: return elevation > 10f && weather.rain < 0.05f && weather.overcast < 0.5f && weather.sunMultiplier > 0.7f && !InPassRange();
                case Kind.ForestShaft:
                case Kind.ForestDust:
                    if (kind == Kind.ForestDust && InPassRange()) return false;   // the motes read as snow among the pass pines too
                    return ForestMood.Allowed && ForestMood.ShaftStrength(elevation, weather.rain, weather.overcast, weather.sunMultiplier) > 0.05f;
                default: return true;
            }
        }

        private void TrySpawnGround(Vector3 centre, float elevation, in WeatherPreset weather)
        {
            if (!HasGround || grid == null) return;
            if (!RandomGroundPoint(centre, minSpawnDistance, maxSpawnDistance, out Vector3 position, out float groundY)) return;

            float density = grid.Sample(position.x, position.z);
            GameObject prefab = null;
            Kind kind = Kind.Meadow;
            float lift = 0f;
            float roll = Random.value;

            if (windStreakFx != null && Allowed(Kind.Streak, elevation, weather) && roll < 0.25f * weather.windStreaks)
            {
                // Subtle: one streak effect at a time, far from the camera and never across the view of the character.
                if (Has(Kind.Streak) || !StreakPoint(centre, out position, out groundY)) return;
                prefab = windStreakFx;
                kind = Kind.Streak;
                lift = 1.5f;
            }
            else if (density >= forestMinDensity)
            {
                if (sunBeamFx != null && !ForestMood.Allowed && Allowed(Kind.SunBeam, elevation, weather) && !Has(Kind.SunBeam) && Random.value < 0.45f)
                {
                    prefab = sunBeamFx;
                    kind = Kind.SunBeam;
                }
                else if (dustFx != null && Allowed(Kind.Dust, elevation, weather) && Random.value < 0.25f)
                {
                    prefab = dustFx;
                    kind = Kind.Dust;
                    lift = 1.2f;
                }
                else if (Allowed(Kind.Forest, elevation, weather))
                {
                    prefab = Pick(forestFx);
                    kind = Kind.Forest;
                    lift = 2.5f;
                }
            }
            else if (density <= meadowMaxDensity)
            {
                if (dustFx != null && Allowed(Kind.Dust, elevation, weather) && Random.value < 0.2f)
                {
                    prefab = dustFx;
                    kind = Kind.Dust;
                    lift = 1f;
                }
                else if (Allowed(Kind.Meadow, elevation, weather))
                {
                    prefab = PickMeadow(centre, ref position, ref groundY, out lift);
                    kind = Kind.Meadow;
                }
            }

            if (prefab == null) return;
            position.y = groundY + lift;
            Quaternion rotation = kind == Kind.Streak && WeatherSystem.Current != null
                ? Quaternion.LookRotation(WeatherSystem.Current.WindDirection, Vector3.up)
                : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Spawn(prefab, kind, position, rotation);
        }

        /// <summary>
        /// A spawn point for wind streaks <see cref="StreakMinDistance"/>-<see cref="StreakMaxDistance"/> m from the camera whose
        /// flight path (about 30 m downwind) stays <see cref="StreakClearance"/> m from the camera and from the line between the
        /// camera and the player, so streaks never cross close in front of the view.
        /// </summary>
        private bool StreakPoint(Vector3 player, out Vector3 position, out float groundY)
        {
            position = default;
            groundY = 0f;
            Camera main = Camera.main;
            Vector3 eye = main != null ? main.transform.position : player;
            Vector3 wind = WeatherSystem.Current != null ? WeatherSystem.Current.WindDirection : Vector3.forward;
            wind.y = 0f;
            wind = wind.sqrMagnitude > 1e-4f ? wind.normalized : Vector3.forward;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!RandomGroundPoint(eye, StreakMinDistance, StreakMaxDistance, out position, out groundY)) continue;
                Vector3 start = new Vector3(position.x, groundY + 1.5f, position.z);
                Vector3 end = start + wind * 30f;
                if (SegmentDistance(eye, start, end) < StreakClearance) continue;
                if (SegmentDistance(player, start, end) < StreakClearance) continue;
                if (SegmentDistance(Vector3.Lerp(eye, player, 0.5f), start, end) < StreakClearance) continue;
                return true;
            }

            return false;
        }

        private static float SegmentDistance(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector3.Distance(point, a + ab * t);
        }

        /// <summary>Wind streak instances: shorter lives (about 30 m of flight instead of 56), smaller and fewer particles.</summary>
        private void SoftenStreak(GameObject instance)
        {
            if (!softened.Add(instance)) return;
            foreach (ParticleSystem system in SystemsOf(instance))
            {
                ParticleSystem.MainModule main = system.main;
                main.startLifetimeMultiplier *= 0.55f;
                main.startSizeMultiplier *= 0.6f;
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTimeMultiplier *= 0.5f;
            }
        }

        /// <summary>
        /// Butterflies go to the flowers: up to five points are tried for the most flower details (<see cref="FlowerSampler"/>);
        /// at a flower patch a butterfly effect is chosen, otherwise one of the other meadow effects (petals). Where the ground
        /// has no detail data at all (grass switched off) it picks among all of them as before.
        /// </summary>
        private GameObject PickMeadow(Vector3 centre, ref Vector3 position, ref float groundY, out float lift)
        {
            lift = 0.8f;
            GameObject butterflies = PickNamed(meadowFx, "Butterfl", true);
            if (butterflies == null) return Pick(meadowFx);
            float flowers = FlowerSampler.Density(position);
            if (flowers < 0f) return Pick(meadowFx);
            for (int attempt = 0; attempt < 4 && flowers < butterflyFlowerDensity; attempt++)
            {
                if (!RandomGroundPoint(centre, minSpawnDistance, maxSpawnDistance, out Vector3 p, out float y)) continue;
                if (grid.Sample(p.x, p.z) > meadowMaxDensity) continue;
                float f = FlowerSampler.Density(p);
                if (f <= flowers) continue;
                flowers = f;
                position = p;
                groundY = y;
            }

            if (flowers >= butterflyFlowerDensity)
            {
                lift = 0.6f;
                return butterflies;
            }

            return PickNamed(meadowFx, "Butterfl", false);
        }

        /// <summary>A random prefab whose name contains <paramref name="key"/> (<paramref name="matching"/>) or does not; null if none.</summary>
        private static GameObject PickNamed(GameObject[] prefabs, string key, bool matching)
        {
            if (prefabs == null) return null;
            int count = 0;
            foreach (GameObject p in prefabs)
            {
                if (p != null && (p.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) == matching) count++;
            }

            if (count == 0) return null;
            int pick = Random.Range(0, count);
            foreach (GameObject p in prefabs)
            {
                if (p == null || (p.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) != matching) continue;
                if (pick-- == 0) return p;
            }

            return null;
        }

        private static GameObject FindNamed(GameObject[] prefabs, string key)
        {
            if (prefabs == null) return null;
            foreach (GameObject p in prefabs)
            {
                if (p != null && p.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 && p.name.IndexOf("Blowing", StringComparison.OrdinalIgnoreCase) < 0) return p;
            }

            return null;
        }

        /// <summary>
        /// Leaves drifting down from the crown of a birch near the traveller (one effect per tree, at most
        /// <see cref="autumnLeavesMax"/>): the Meadow falling-leaf effect recoloured orange/yellow, a few leaves a second,
        /// emitted across the crown.
        /// </summary>
        private void TrySpawnAutumnLeaves(Vector3 centre) => TrySpawnFromCrown(centre, autumnTrees, autumnTemplate, Kind.AutumnLeaves);

        /// <summary>Spring: pale pink-white blossom drifting from a fruit tree's crown, the same effect as the autumn leaves.</summary>
        private void TrySpawnBlossom(Vector3 centre) => TrySpawnFromCrown(centre, fruitTrees, blossomTemplate, Kind.Blossom);

        private void TrySpawnFromCrown(Vector3 centre, AutumnTreeFinder finder, GameObject template, Kind kind)
        {
            if (template == null) return;
            if (!finder.TryFind(centre, autumnLeavesDistance.x, autumnLeavesDistance.y, out Vector3 tree, out float height, out float crown)) return;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Kind != kind || active[i].Instance == null) continue;
                Vector3 d = active[i].Instance.transform.position - tree;
                d.y = 0f;
                if (d.sqrMagnitude < 36f) return;   // that tree already drops leaves
            }

            float radius = Mathf.Clamp(crown, 1f, 5f);
            Vector3 position = tree + Vector3.up * Mathf.Clamp(height * 0.62f, 2f, 14f);
            GameObject instance = Take(template);
            foreach (ParticleSystem system in SystemsOf(instance))
            {
                ParticleSystem.ShapeModule shape = system.shape;
                shape.scale = new Vector3(radius * 1.4f, Mathf.Clamp(height * 0.2f, 0.5f, 3f), radius * 1.4f);
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTimeMultiplier = Random.Range(1.2f, 2.6f);
            }

            Spawn(template, kind, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), instance);
        }

        /// <summary>
        /// Forest pass: a sun-shaft emitter under the canopy 8–40 m from the traveller, where the trees open a little (a gap, a glade's
        /// edge), its beams slanting along the light; with a drift of warm dust motes in it. The beams' brightness follows
        /// <see cref="ForestMood.ShaftStrength"/> and the canopy.
        /// </summary>
        private void TrySpawnShaft(Vector3 centre, float elevation, in WeatherPreset weather, bool withDust)
        {
            if (shaftTemplate == null || !HasGround || grid == null || ForestMood.Canopy < 0.35f) return;
            float strength = ForestMood.ShaftStrength(elevation, weather.rain, weather.overcast, weather.sunMultiplier) * ForestMood.Canopy;
            if (strength <= 0.05f) return;
            Vector3 light = DayCycle.Current != null ? DayCycle.Current.LightDirection : new Vector3(0.4f, 0.6f, 0.7f);
            if (light.y < 0.05f) return;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (!RandomGroundPoint(centre, 8f, 40f, out Vector3 position, out float groundY)) continue;
                float density = grid.Sample(position.x, position.z);
                if (density < 0.25f || density > 1.6f) continue;   // where the trees open a little, not the open field or the thickest wood
                bool near = false;
                for (int i = 0; i < active.Count && !near; i++)
                {
                    if (active[i].Kind != Kind.ForestShaft || active[i].Instance == null) continue;
                    Vector3 d = active[i].Instance.transform.position - position;
                    d.y = 0f;
                    near = d.sqrMagnitude < 15f * 15f;
                }

                if (near) continue;
                position.y = groundY + 0.4f;
                GameObject instance = Take(shaftTemplate);
                Vector3 down = -light.normalized;
                foreach (ParticleSystem system in SystemsOf(instance))
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.87f, 0.62f, 0.07f * strength), new Color(1f, 0.8f, 0.55f, 0.16f * strength));
                    ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                    velocity.enabled = true;
                    velocity.space = ParticleSystemSimulationSpace.World;
                    velocity.x = new ParticleSystem.MinMaxCurve(down.x * 0.12f);
                    velocity.y = new ParticleSystem.MinMaxCurve(down.y * 0.12f);
                    velocity.z = new ParticleSystem.MinMaxCurve(down.z * 0.12f);
                }

                Spawn(shaftTemplate, Kind.ForestShaft, position, Quaternion.identity, instance);
                if (withDust && forestDustTemplate != null && !InPassRange()) Spawn(forestDustTemplate, Kind.ForestDust, position + Vector3.up * 1.6f, Quaternion.identity);
                return;
            }
        }

        private static int passFrame = -1;
        private static bool passCached;

        /// <summary>
        /// True while the endless road's traveller stands in a mountain pass region (no pollen glints and no dust specks there:
        /// both read as snow over the pass meadows). Asks the generator for the region at the traveller's road distance, not the
        /// announced region, which lags 20 m behind a boundary and is not refreshed by every teleport (review 2026-10-06, T0073).
        /// </summary>
        private static bool InPassRange()
        {
            if (passFrame == Time.frameCount) return passCached;
            passFrame = Time.frameCount;
            MedievalZen.Endless.EndlessWorld world = MedievalZen.Endless.EndlessWorld.Current;
            if (world == null)
            {
                passCached = false;
            }
            else
            {
                MedievalZen.Endless.RegionKind kind = world.Generator != null ? world.Generator.Region(world.S).Kind : world.CurrentRegion.Kind;
                passCached = kind == MedievalZen.Endless.RegionKind.HighlandPass;
            }

            return passCached;
        }

        /// <summary>
        /// Puts every ambient effect away at once, its particles cleared (the review tour after each teleport, so no drift of the
        /// previous stop floats into the next shot). The spawner starts afresh round the new position.
        /// </summary>
        public void ClearAll()
        {
            for (int i = active.Count - 1; i >= 0; i--) Recycle(active[i]);
            active.Clear();
        }

        /// <summary>Pollen and seed fluff drifting with the wind over sunlit open ground, 5–18 m from the traveller.</summary>
        private void TrySpawnPollen(Vector3 centre)
        {
            if (pollenTemplate == null || !HasGround || grid == null) return;
            if (InPassRange()) return;   // over the pass meadows at noon the white specks read as snow (review 2026-10-05, 0073)
            if (!RandomGroundPoint(centre, 5f, 18f, out Vector3 position, out float groundY)) return;
            if (grid.Sample(position.x, position.z) >= forestMinDensity) return;   // open ground and woodland edges only
            position.y = groundY + 1.3f;
            GameObject instance = Take(pollenTemplate);
            Vector3 wind = WeatherSystem.Current != null ? WeatherSystem.Current.WindDirection : new Vector3(0.64f, 0f, 0.77f);
            float strength = 0.35f + 0.9f * (WeatherSystem.Current != null ? WeatherSystem.Current.Weather.wind : 0.3f);
            foreach (ParticleSystem system in SystemsOf(instance))
            {
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(wind.x * strength * 0.6f, wind.x * strength);
                velocity.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.08f);
                velocity.z = new ParticleSystem.MinMaxCurve(wind.z * strength * 0.6f, wind.z * strength);
            }

            Spawn(pollenTemplate, Kind.Pollen, position, Quaternion.identity, instance);
        }

        private void TrySpawnBird(Vector3 centre)
        {
            if (birdFx == null || !HasGround || grid == null) return;
            if (!RandomGroundPoint(centre, birdDistance.x, birdDistance.y, out Vector3 position, out float groundY)) return;
            if (grid.Sample(position.x, position.z) > meadowMaxDensity * 3f) return;   // open country only
            position.y = groundY + Random.Range(birdAltitude.x, birdAltitude.y);
            Spawn(birdFx, Kind.Bird, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        }

        private void TrySpawnFireflies(Vector3 centre)
        {
            if (fireflyTemplate == null || !HasGround || grid == null) return;
            float water = WaterAt(centre);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!RandomGroundPoint(centre, 10f, 45f, out Vector3 position, out float groundY)) continue;
                float density = grid.Sample(position.x, position.z);
                bool shore = water > 0f && groundY > water + 0.1f && groundY < water + 2.5f;
                bool forestEdge = density > meadowMaxDensity && density < forestMinDensity * 1.5f;
                if (!shore && !forestEdge) continue;
                position.y = groundY + 0.6f;
                Spawn(fireflyTemplate, Kind.Firefly, position, Quaternion.identity);
                return;
            }
        }

        private void TrySpawnRipple(Vector3 centre)
        {
            if (rippleFx == null || !HasGround) return;
            float water = WaterAt(centre);
            if (water <= 0f) return;
            Camera main = Camera.main;
            Vector3 forward = main != null ? main.transform.forward : target.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return;
            forward.Normalize();
            var right = new Vector3(forward.z, 0f, -forward.x);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                float distance = Random.Range(12f, 60f);
                Vector3 p = centre + forward * distance + right * Random.Range(-15f, 15f);
                float groundY = GroundHeight(p);
                if (groundY > water - 0.3f) continue;                                  // must be over water
                Vector3 nearer = p - forward * 8f;
                if (GroundHeight(nearer) < water - 0.3f && GroundHeight(p + right * 8f) < water - 0.3f &&
                    GroundHeight(p - right * 8f) < water - 0.3f) continue;                 // near a shore only
                p.y = water + 0.02f;
                Spawn(rippleFx, Kind.Ripple, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                return;
            }
        }

        private bool RandomGroundPoint(Vector3 centre, float minDistance, float maxDistance, out Vector3 position, out float groundY)
        {
            Vector2 dir = Random.insideUnitCircle;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.up;
            dir.Normalize();
            float distance = Random.Range(minDistance, Mathf.Max(minDistance, maxDistance));
            position = new Vector3(centre.x + dir.x * distance, 0f, centre.z + dir.y * distance);
            groundY = GroundHeight(position);
            float water = WaterAt(position);
            return !(water > 0f && groundY < water + 0.2f);
        }

        private bool HasGround => surface != null && surface.HasGround;

        private float GroundHeight(Vector3 p) => surface.GroundHeight(p);

        /// <summary>Lake level that applies at <paramref name="p"/> (the finite world has one level everywhere).</summary>
        private float WaterAt(Vector3 p) => surface != null ? surface.WaterHeight(p) : GameWorld.Current != null ? GameWorld.Current.WaterHeight : -1f;

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Instance != null) active[i].Instance.transform.position += delta;
            }

            skyBirds?.ShiftOrigin(delta);
        }

        private bool Has(Kind kind)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Kind == kind) return true;
            }

            return false;
        }

        private static GameObject Pick(GameObject[] prefabs)
        {
            if (prefabs == null || prefabs.Length == 0) return null;
            return prefabs[Random.Range(0, prefabs.Length)];
        }

        private void Spawn(GameObject prefab, Kind kind, Vector3 position, Quaternion rotation, GameObject taken = null)
        {
            GameObject instance = taken != null ? taken : Take(prefab);
            if (kind == Kind.Streak) SoftenStreak(instance);
            instance.transform.SetPositionAndRotation(position, rotation);
            ParticleSystem[] systems = SystemsOf(instance);
            StartAll(instance, systems);
            active.Add(new Slot { Prefab = prefab, Instance = instance, Systems = systems, Kind = kind });
        }

        private ParticleSystem[] SystemsOf(GameObject instance)
        {
            if (!systemsCache.TryGetValue(instance, out ParticleSystem[] systems))
            {
                systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                systemsCache[instance] = systems;
            }

            return systems;
        }

        private static void Retire(Slot slot)
        {
            slot.Retiring = true;
            slot.RetireDeadline = Time.time + 20f;
            for (int i = 0; i < slot.Systems.Length; i++)
            {
                if (slot.Systems[i] != null) slot.Systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private static bool AnyAlive(Slot slot)
        {
            for (int i = 0; i < slot.Systems.Length; i++)
            {
                if (slot.Systems[i] != null && slot.Systems[i].particleCount > 0) return true;
            }

            return false;
        }

        private GameObject Take(GameObject prefab)
        {
            if (pool.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                while (stack.Count > 0)
                {
                    GameObject pooled = stack.Pop();
                    if (pooled != null) return pooled;
                }
            }

            using Review.FrameCost.Scope cost = Review.FrameCost.Measure(CostInstantiate);
            Review.FrameCost.FirstUse("Instantiate " + prefab.name);
            GameObject instance = Instantiate(prefab, transform);
            instance.SetActive(false);
            return instance;
        }

        private void Recycle(Slot slot)
        {
            if (slot.Instance == null) return;
            // No particle of this run survives into the instance's next use (a pooled pollen drift reused after a teleport).
            for (int i = 0; slot.Systems != null && i < slot.Systems.Length; i++)
            {
                if (slot.Systems[i] != null) slot.Systems[i].Clear(false);
            }

            slot.Instance.SetActive(false);
            if (!pool.TryGetValue(slot.Prefab, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>(2);
                pool[slot.Prefab] = stack;
            }

            stack.Push(slot.Instance);
        }

        /// <summary>
        /// Builds an inactive firefly swarm template: ~20 slow, softly blinking warm-green specks drifting in a 14 × 2 × 14 m
        /// box, rendered with <paramref name="material"/> (additive glow).
        /// </summary>
        private static GameObject BuildFireflyTemplate(Material material, Transform parent)
        {
            var go = new GameObject("FX_Fireflies");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 1f, 0.35f), new Color(1f, 0.85f, 0.35f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 3f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14f, 2f, 14f);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.15f;
            noise.damping = true;

            // Blink: alpha pulses a few times over each life.
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var blink = new Gradient();
            blink.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.1f, 0.3f), new GradientAlphaKey(0.9f, 0.45f),
                    new GradientAlphaKey(0.05f, 0.62f), new GradientAlphaKey(1f, 0.78f), new GradientAlphaKey(0f, 1f),
                });
            colour.color = new ParticleSystem.MinMaxGradient(blink);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// Builds an inactive pollen/seed drift template: up to 24 tiny warm-white specks (3.5–6 cm) in a 12 × 3 × 12 m box,
        /// living 7–11 s, fading in and out, carried by the wind (the velocity is set at each spawn) with a little turbulence.
        /// Same additive glow material as the fireflies but faint (alpha 0.12–0.22), so they read as glints in the sun against
        /// shade and grass, never as lights.
        /// </summary>
        private static GameObject BuildPollenTemplate(Material material, Transform parent)
        {
            var go = new GameObject("FX_Pollen");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 6f;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.97f, 0.85f, 0.12f), new Color(1f, 0.9f, 0.7f, 0.22f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 2.4f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(12f, 3f, 12f);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.1f;
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(fade);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// Forest pass: an inactive sun-shaft emitter. Stretched billboards (each turns about its own axis to face the camera) whose
        /// axis is their velocity, set to the light's direction at spawn, so every beam slants along the sunlight; 1.6–2.8 m wide and
        /// seven times as long, rising from the ground toward the canopy; a few at a time in an 8 × 8 m patch, fading in and out over
        /// their 9–14 s life. A copy of the additive glow material with a broad, faint falloff (no hot core).
        /// </summary>
        private static GameObject BuildShaftTemplate(Material glow, Transform parent)
        {
            var go = new GameObject("FX_ForestSunShafts");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 10f;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 8;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0.45f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(8f, 0.5f, 8f);

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(fade);

            var material = new Material(glow) { name = "ForestSunShaft (runtime)" };
            if (material.HasProperty("_Intensity")) material.SetFloat("_Intensity", 1.2f);
            if (material.HasProperty("_Core")) material.SetFloat("_Core", 1f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 7f;
            renderer.velocityScale = 0f;
            renderer.cameraVelocityScale = 0f;
            renderer.maxParticleSize = 4f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>Forest pass: warm dust motes turning slowly in a sun shaft (like the pollen glints, slower and denser).</summary>
        private static GameObject BuildForestDustTemplate(Material glow, Transform parent)
        {
            GameObject go = BuildPollenTemplate(glow, parent);
            go.name = "FX_ForestDust";
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 13f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.7f, 0.2f), new Color(1f, 0.85f, 0.6f, 0.35f));
            main.maxParticles = 40;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 4f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.scale = new Vector3(6f, 3f, 6f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.strength = 0.12f;
            noise.frequency = 0.25f;
            return go;
        }

        /// <summary>
        /// An inactive copy of the Meadow falling-leaf effect for the birches: leaves coloured between
        /// <paramref name="a"/> and <paramref name="b"/> (the particle start colour; the Synty prefab and material are untouched)
        /// and at most 40 leaves alive. Null when the prefab is missing.
        /// </summary>
        private static GameObject BuildAutumnTemplate(GameObject leaves, Transform parent, Color a, Color b)
        {
            if (leaves == null) return null;
            GameObject go = Instantiate(leaves, parent);
            go.name = "FX_Leaves_Autumn";
            go.SetActive(false);
            foreach (ParticleSystem system in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.startColor = new ParticleSystem.MinMaxGradient(a, b);
                main.maxParticles = Mathf.Min(main.maxParticles, 40);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }

            return go;
        }
    }
}
