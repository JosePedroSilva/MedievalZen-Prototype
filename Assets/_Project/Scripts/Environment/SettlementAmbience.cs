using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Makes a settlement feel lived in: chimney smoke on some cabins (more at breakfast and supper time), warm window/door
    /// lights that fade in one by one at dusk and out at dawn with a slight flicker, and a campfire with a flickering light
    /// at fireplaces in the evening and night. Anchors (cabins, fireplaces, chimney and light offsets) are baked by the
    /// editor menu <c>MedievalZen/Atmosphere/Apply To Open Scene</c> from the generated hierarchy. Only settlements within
    /// <see cref="activeRadius"/> of the player build/run their effects, and all settlements share one light budget of
    /// <see cref="GlobalLightCap"/> point lights (nearest settlements first).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettlementAmbience : MonoBehaviour
    {
        /// <summary>A cabin: where its chimney top and its window/door light are, in the cabin's local space.</summary>
        [Serializable]
        public struct CabinAnchor
        {
            /// <summary>The cabin instance.</summary>
            public Transform cabin;
            /// <summary>Chimney top (smoke origin), cabin-local.</summary>
            public Vector3 chimneyLocal;
            /// <summary>Window/door light position, cabin-local.</summary>
            public Vector3 lightLocal;
            /// <summary>Whether this cabin's chimney smokes.</summary>
            public bool smoke;
        }

        /// <summary>A fireplace: where the flames sit, fireplace-local.</summary>
        [Serializable]
        public struct FireAnchor
        {
            /// <summary>The fireplace instance.</summary>
            public Transform fireplace;
            /// <summary>Flame origin, fireplace-local.</summary>
            public Vector3 local;
        }

        /// <summary>Most settlement point lights lit at once, over all settlements (Forward+ has no per-object limit, this keeps the cost flat).</summary>
        public const int GlobalLightCap = 16;

        private static readonly List<SettlementAmbience> Registry = new List<SettlementAmbience>(16);
        private static int lastBudgetFrame = -1;
        private static int[] budgetOrder = new int[16];
        private static float[] budgetDistance = new float[16];
        private static int[] budgetRequests = new int[16];
        private static int[] budgetGranted = new int[16];

        [Header("Anchors (baked by the menu)")]
        [SerializeField] private CabinAnchor[] cabins = Array.Empty<CabinAnchor>();
        [SerializeField] private FireAnchor[] fires = Array.Empty<FireAnchor>();

        [Header("Effects")]
        [SerializeField, Tooltip("Chimney smoke effect (PolygonGeneric FX_Smoke_01).")]
        private GameObject smokePrefab;

        [SerializeField, Tooltip("Campfire flames (Meadow FX_Fire_01).")]
        private GameObject firePrefab;

        [SerializeField, Range(0.1f, 3f), Tooltip("Scale of the chimney smoke effect.")]
        private float smokeScale = 0.8f;

        [Header("Culling")]
        [SerializeField, Min(50f), Tooltip("Effects run only while the player is within this distance (metres).")]
        private float activeRadius = 250f;

        [Header("Window lights")]
        [SerializeField] private Color windowColor = new Color(1f, 0.63f, 0.3f);
        [SerializeField, Min(0f)] private float windowIntensity = 2.2f;
        [SerializeField, Min(0.5f)] private float windowRange = 6f;
        [SerializeField, Tooltip("Lights start coming on at this hour (+ a per-light stagger).")] private float lightsOnHour = 17.6f;
        [SerializeField, Tooltip("Lights start going off at this hour (+ a per-light stagger).")] private float lightsOffHour = 5.3f;
        [SerializeField, Min(0f), Tooltip("Largest per-light delay in hours, so windows light up one by one.")] private float stagger = 1.2f;
        [SerializeField, Min(0.05f), Tooltip("Hours each light takes to fade in or out.")] private float fadeHours = 0.5f;
        [SerializeField, Range(0f, 0.5f), Tooltip("Candle flicker amount.")] private float windowFlicker = 0.07f;

        [Header("Campfire")]
        [SerializeField] private Color fireColor = new Color(1f, 0.52f, 0.2f);
        [SerializeField, Min(0f)] private float fireIntensity = 3.2f;
        [SerializeField, Min(0.5f)] private float fireRange = 9f;
        [SerializeField] private float fireOnHour = 17f;
        [SerializeField] private float fireOffHour = 6.5f;
        [SerializeField, Range(0f, 1f)] private float fireFlicker = 0.35f;

        private bool active;
        private bool built;
        private Light[] lights = Array.Empty<Light>();       // fires first, then windows
        private float[] lightLevel = Array.Empty<float>();
        private float[] lightSeed = Array.Empty<float>();
        private ParticleSystem[][] smoke = Array.Empty<ParticleSystem[]>();
        private float[][] smokeRates = Array.Empty<float[]>();
        private GameObject[] fireFx = Array.Empty<GameObject>();
        private bool[] fireFxOn = Array.Empty<bool>();
        private int granted;
        private int requested;
        private float distanceToPlayer = float.PositiveInfinity;

        /// <summary>Number of cabins with anchors.</summary>
        public int CabinCount => cabins.Length;

        /// <summary>Number of fireplaces with anchors.</summary>
        public int FireCount => fires.Length;

        /// <summary>Editor: sets the baked anchors and effect prefabs.</summary>
        public void Configure(CabinAnchor[] cabinAnchors, FireAnchor[] fireAnchors, GameObject smoke, GameObject fire)
        {
            cabins = cabinAnchors ?? Array.Empty<CabinAnchor>();
            fires = fireAnchors ?? Array.Empty<FireAnchor>();
            smokePrefab = smoke;
            firePrefab = fire;
        }

        /// <summary>Smoke amount (0..1) over the day: a little all day, most at breakfast and supper time.</summary>
        public static float SmokeAmount(float hour)
        {
            hour = DayClock.Wrap(hour);
            float meals = Mathf.Max(AtmosphereMath.Bump(hour, 7.5f, 2.5f), AtmosphereMath.Bump(hour, 19f, 3f));
            return 0.35f + 0.65f * meals;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
            lastBudgetFrame = -1;
            cachedPlayer = null;
        }

        private void OnEnable()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
        }

        private void OnDisable()
        {
            Registry.Remove(this);
            SetActive(false);
        }

        private void Update()
        {
            Transform player = PlayerTransform();
            if (player == null) return;
            Vector3 d = transform.position - player.position;
            d.y = 0f;
            distanceToPlayer = d.magnitude;

            bool shouldBeActive = active ? distanceToPlayer < activeRadius + 20f : distanceToPlayer < activeRadius;
            if (shouldBeActive != active) SetActive(shouldBeActive);
            if (!active) return;

            float hour = DayCycle.Current != null ? DayCycle.Current.Hour : 12f;
            requested = CountWantedLights(hour);
            if (lastBudgetFrame != Time.frameCount) RunBudget();
            UpdateLights(hour);
            UpdateSmoke(hour);
            UpdateFires(hour);
        }

        private static Transform cachedPlayer;

        private static Transform PlayerTransform()
        {
            if (cachedPlayer != null) return cachedPlayer;
            Camera main = Camera.main;
            if (main != null) cachedPlayer = main.transform;
            return cachedPlayer;
        }

        private void SetActive(bool value)
        {
            active = value;
            if (value && !built) Build();
            if (!built) return;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                lights[i].enabled = false;
                lightLevel[i] = 0f;
            }

            for (int i = 0; i < smoke.Length; i++)
            {
                ParticleSystem[] systems = smoke[i];
                for (int k = 0; k < systems.Length; k++)
                {
                    if (systems[k] == null) continue;
                    if (value) systems[k].Play(false);
                    else systems[k].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            for (int i = 0; i < fireFx.Length; i++)
            {
                if (fireFx[i] != null && !value) fireFx[i].SetActive(false);
                fireFxOn[i] = false;
            }

            granted = 0;
            requested = 0;
        }

        private void Build()
        {
            built = true;
            int seed = Mathf.Abs(gameObject.name.GetHashCode());
            var rng = new System.Random(seed);
            lights = new Light[fires.Length + cabins.Length];
            lightLevel = new float[lights.Length];
            lightSeed = new float[lights.Length];
            for (int i = 0; i < fires.Length; i++)
            {
                FireAnchor f = fires[i];
                if (f.fireplace == null) continue;
                lights[i] = CreateLight("FireLight", f.fireplace.TransformPoint(f.local) + Vector3.up * 0.6f, fireColor, fireRange);
                lightSeed[i] = (float)rng.NextDouble() * 100f;
            }

            for (int i = 0; i < cabins.Length; i++)
            {
                CabinAnchor c = cabins[i];
                if (c.cabin == null) continue;
                int li = fires.Length + i;
                lights[li] = CreateLight("WindowLight", c.cabin.TransformPoint(c.lightLocal), windowColor, windowRange);
                lightSeed[li] = (float)rng.NextDouble() * 100f;
            }

            smoke = new ParticleSystem[cabins.Length][];
            smokeRates = new float[cabins.Length][];
            for (int i = 0; i < cabins.Length; i++)
            {
                CabinAnchor c = cabins[i];
                if (!c.smoke || c.cabin == null || smokePrefab == null)
                {
                    smoke[i] = Array.Empty<ParticleSystem>();
                    smokeRates[i] = Array.Empty<float>();
                    continue;
                }

                GameObject go = Instantiate(smokePrefab, transform);
                go.name = "ChimneySmoke";
                go.transform.SetPositionAndRotation(c.cabin.TransformPoint(c.chimneyLocal), Quaternion.identity);
                go.transform.localScale = Vector3.one * smokeScale;
                smoke[i] = go.GetComponentsInChildren<ParticleSystem>(true);
                smokeRates[i] = new float[smoke[i].Length];
                for (int k = 0; k < smoke[i].Length; k++) smokeRates[i][k] = smoke[i][k].emission.rateOverTimeMultiplier;
            }

            fireFx = new GameObject[fires.Length];
            fireFxOn = new bool[fires.Length];
            for (int i = 0; i < fires.Length; i++)
            {
                FireAnchor f = fires[i];
                if (f.fireplace == null || firePrefab == null) continue;
                GameObject go = Instantiate(firePrefab, transform);
                go.name = "Campfire";
                go.transform.SetPositionAndRotation(f.fireplace.TransformPoint(f.local), Quaternion.identity);
                go.SetActive(false);
                fireFx[i] = go;
            }
        }

        private Light CreateLight(string lightName, Vector3 position, Color color, float range)
        {
            var go = new GameObject(lightName);
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        private float WindowFade(int cabinIndex, float hour)
        {
            float s = lightSeed.Length > fires.Length + cabinIndex ? lightSeed[fires.Length + cabinIndex] : 0f;
            float onDelay = (s * 0.37f % 1f) * stagger;
            float offDelay = (s * 0.61f % 1f) * stagger;
            return AtmosphereMath.LightFade(hour, lightsOnHour + onDelay, fadeHours, lightsOffHour + offDelay, fadeHours);
        }

        private float FireFade(float hour) => AtmosphereMath.LightFade(hour, fireOnHour, 0.8f, fireOffHour, 0.8f);

        private int CountWantedLights(float hour)
        {
            int count = 0;
            float fire = FireFade(hour);
            for (int i = 0; i < fires.Length; i++)
            {
                if (fire > 0.01f && lights[i] != null) count++;
            }

            for (int i = 0; i < cabins.Length; i++)
            {
                if (lights[fires.Length + i] != null && WindowFade(i, hour) > 0.01f) count++;
            }

            return count;
        }

        /// <summary>Shares <see cref="GlobalLightCap"/> between the active settlements, nearest first.</summary>
        private static void RunBudget()
        {
            lastBudgetFrame = Time.frameCount;
            int n = Registry.Count;
            if (budgetOrder.Length < n)
            {
                int size = Mathf.NextPowerOfTwo(n);
                budgetOrder = new int[size];
                budgetDistance = new float[size];
                budgetRequests = new int[size];
                budgetGranted = new int[size];
            }

            int m = 0;
            for (int i = 0; i < n; i++)
            {
                SettlementAmbience s = Registry[i];
                if (s == null || !s.active) continue;
                // Insertion sort by distance (a handful of settlements at most).
                int j = m++;
                while (j > 0 && budgetDistance[j - 1] > s.distanceToPlayer)
                {
                    budgetDistance[j] = budgetDistance[j - 1];
                    budgetOrder[j] = budgetOrder[j - 1];
                    j--;
                }

                budgetDistance[j] = s.distanceToPlayer;
                budgetOrder[j] = i;
            }

            for (int k = 0; k < m; k++) budgetRequests[k] = Registry[budgetOrder[k]].requested;
            AtmosphereMath.AllocateBudget(budgetRequests, m, GlobalLightCap, budgetGranted);
            for (int k = 0; k < m; k++) Registry[budgetOrder[k]].granted = budgetGranted[k];
        }

        private void UpdateLights(float hour)
        {
            float t = Time.time;
            float step = Time.deltaTime;
            float distanceFade = 1f - AtmosphereMath.SmoothRange(activeRadius * 0.75f, activeRadius, distanceToPlayer);
            float fire = FireFade(hour);
            int left = granted;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;
                bool isFire = i < fires.Length;
                float fade = isFire ? fire : WindowFade(i - fires.Length, hour);
                bool allowed = fade > 0.01f && left > 0;
                if (fade > 0.01f && left > 0) left--;
                lightLevel[i] = Mathf.MoveTowards(lightLevel[i], allowed ? fade * distanceFade : 0f, step);
                if (lightLevel[i] <= 0.001f)
                {
                    if (light.enabled) light.enabled = false;
                    continue;
                }

                float noise = Mathf.PerlinNoise(t * (isFire ? 5.5f : 1.7f), lightSeed[i]);
                float flicker = isFire ? 1f - fireFlicker + 2f * fireFlicker * noise : 1f + windowFlicker * (noise * 2f - 1f);
                light.intensity = (isFire ? fireIntensity : windowIntensity) * lightLevel[i] * flicker;
                if (!light.enabled) light.enabled = true;
            }
        }

        private void UpdateSmoke(float hour)
        {
            float amount = SmokeAmount(hour);
            float rain = WeatherSystem.Current != null ? WeatherSystem.Current.Weather.rain : 0f;
            amount *= 1f - 0.4f * rain;
            for (int i = 0; i < smoke.Length; i++)
            {
                ParticleSystem[] systems = smoke[i];
                for (int k = 0; k < systems.Length; k++)
                {
                    if (systems[k] == null) continue;
                    ParticleSystem.EmissionModule emission = systems[k].emission;
                    emission.rateOverTimeMultiplier = smokeRates[i][k] * amount;
                }
            }
        }

        private void UpdateFires(float hour)
        {
            bool on = FireFade(hour) > 0.05f;
            for (int i = 0; i < fireFx.Length; i++)
            {
                if (fireFx[i] == null || fireFxOn[i] == on) continue;
                fireFxOn[i] = on;
                fireFx[i].SetActive(on);
            }
        }
    }
}
