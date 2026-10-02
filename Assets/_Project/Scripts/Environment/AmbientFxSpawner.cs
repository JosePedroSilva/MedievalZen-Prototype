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
    /// material); and lake ripples near the shore the player is looking at. Forest vs meadow comes from a coarse
    /// <see cref="TreeDensityGrid"/> built once from the Terrain's tree instances. Effects that fall out of season stop
    /// emitting and are recycled once their particles have died, so nothing pops. Cosmetic only, so it uses
    /// <c>UnityEngine.Random</c>.
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
        private float nextCheck;
        private int wanted;
        private int wantedBirds;

        private void Start()
        {
            // An inspector-assigned terrain wins; otherwise the scene's world surface (the finite world's terrain, or the
            // endless road's streamed tiles).
            surface = terrain != null ? new TerrainSurface(terrain) : GameWorld.Current != null ? GameWorld.Current.Surface : null;
            grid = surface != null ? surface.CreateTreeDensity(gridCellSize) : TreeDensityGrid.FromTerrain(null, gridCellSize);
            wanted = Random.Range(minActive, Mathf.Max(minActive, maxActive) + 1);
            wantedBirds = Random.Range(birdCount.x, Mathf.Max(birdCount.x, birdCount.y) + 1);
            if (fireflyMaterial != null) fireflyTemplate = BuildFireflyTemplate(fireflyMaterial, transform);
        }

        private void Update()
        {
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

            int ground = 0, birds = 0, fireflies = 0, ripples = 0;
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
                    default: ground++; break;
                }
            }

            if (ground < wanted) TrySpawnGround(centre, elevation, weather);
            if (birds < wantedBirds && Allowed(Kind.Bird, elevation, weather)) TrySpawnBird(centre);
            if (fireflies < fireflyMax && Allowed(Kind.Firefly, elevation, weather)) TrySpawnFireflies(centre);
            if (ripples < rippleMax) TrySpawnRipple(centre);
            if (Random.value < 0.05f) wanted = Random.Range(minActive, Mathf.Max(minActive, maxActive) + 1);
        }

        private bool Allowed(Kind kind, float elevation, in WeatherPreset weather)
        {
            switch (kind)
            {
                case Kind.Meadow: return elevation > 6f && weather.rain < 0.15f;
                case Kind.Forest: return weather.rain < 0.6f;
                case Kind.Dust: return elevation > 4f && weather.rain < 0.1f && weather.overcast < 0.6f;
                case Kind.SunBeam: return elevation >= sunBeamElevation.x && elevation <= sunBeamElevation.y && weather.sunMultiplier > 0.75f && weather.rain < 0.05f;
                case Kind.Streak: return weather.windStreaks > 0.3f && weather.rain < 0.5f;
                case Kind.Bird: return elevation > 8f && weather.rain < 0.1f;
                case Kind.Firefly: return elevation < -3f && weather.rain < 0.2f;
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
                if (sunBeamFx != null && Allowed(Kind.SunBeam, elevation, weather) && !Has(Kind.SunBeam) && Random.value < 0.45f)
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
                    prefab = Pick(meadowFx);
                    kind = Kind.Meadow;
                    lift = 0.8f;
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

        private void Spawn(GameObject prefab, Kind kind, Vector3 position, Quaternion rotation)
        {
            GameObject instance = Take(prefab);
            if (kind == Kind.Streak) SoftenStreak(instance);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            ParticleSystem[] systems = SystemsOf(instance);
            for (int i = 0; i < systems.Length; i++) systems[i].Play(false);
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

            GameObject instance = Instantiate(prefab, transform);
            instance.SetActive(false);
            return instance;
        }

        private void Recycle(Slot slot)
        {
            if (slot.Instance == null) return;
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
    }
}
