using System;
using System.Reflection;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>Inspector override for the weather.</summary>
    public enum WeatherOverride
    {
        /// <summary>Follow the seeded schedule.</summary>
        Auto = -1,
        /// <summary>Force <see cref="WeatherState.Clear"/>.</summary>
        Clear = WeatherState.Clear,
        /// <summary>Force <see cref="WeatherState.Hazy"/>.</summary>
        Hazy = WeatherState.Hazy,
        /// <summary>Force <see cref="WeatherState.Misty"/>.</summary>
        Misty = WeatherState.Misty,
        /// <summary>Force <see cref="WeatherState.Overcast"/>.</summary>
        Overcast = WeatherState.Overcast,
        /// <summary>Force <see cref="WeatherState.LightRain"/>.</summary>
        LightRain = WeatherState.LightRain,
    }

    /// <summary>
    /// Slow, seeded weather (clear, hazy, misty, overcast, light rain) from a <see cref="WeatherModel"/> keyed on
    /// <c>WorldSettings.seed</c> and the <see cref="DayCycle"/> day and hour. Every frame it publishes the blended
    /// <see cref="Current"/> preset and drives: the DayCycle sun/cloud/overcast hooks, the Synty vegetation wind globals
    /// (<c>_WindDirection</c>, <c>_WindIntensity</c>, <c>_GaleStrength</c> — also written into the scene's Synty
    /// <c>WeatherControl</c> so its own Update agrees), and the rain particle emission. The AtmosphereController reads
    /// <see cref="Current"/> for the fog. Force a state with <see cref="forcedState"/> or the context menu.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-58)]
    public sealed class WeatherSystem : MonoBehaviour
    {
        private static readonly int WindDirectionId = Shader.PropertyToID("_WindDirection");
        private static readonly int WindIntensityId = Shader.PropertyToID("_WindIntensity");
        private static readonly int GaleStrengthId = Shader.PropertyToID("_GaleStrength");

        [Header("Schedule")]
        [SerializeField, Tooltip("Force a weather state (cross-fades like a natural change). Auto follows the seeded schedule.")]
        private WeatherOverride forcedState = WeatherOverride.Auto;

        [SerializeField, Min(0.5f), Tooltip("Length of one weather slot in in-game hours; a state usually lasts one to three slots.")]
        private float slotHours = 3f;

        [SerializeField, Range(0f, 0.95f), Tooltip("Chance that the next slot keeps the current state.")]
        private float stayProbability = 0.55f;

        [SerializeField, Range(60f, 120f), Tooltip("Real seconds a change of weather takes to cross-fade.")]
        private float crossFadeSeconds = 90f;

        [SerializeField, Tooltip("Added to WorldSettings.seed so the weather can be re-rolled without changing the world.")]
        private int seedOffset = 7;

        [Header("Presets (Clear, Hazy, Misty, Overcast, LightRain)")]
        [SerializeField, Tooltip("One preset per WeatherState, in enum order.")]
        private WeatherPreset[] presets = DefaultPresets();

        [Header("Wind")]
        [SerializeField, Range(-180f, 180f), Tooltip("Wind direction (yaw, degrees) used when the scene has no Synty WeatherControl.")]
        private float windYaw = 40f;

        [SerializeField, Range(0f, 30f), Tooltip("The wind direction wanders this many degrees either way, very slowly.")]
        private float windWander = 12f;

        [Header("Rain")]
        [SerializeField, Tooltip("Root of the rain effects (FX_Rain_01, FX_Rain_Splash_01) — a child of the player-following sky root.")]
        private GameObject rainRoot = null;

        [SerializeField, Range(0f, 1f), Tooltip("Rain amount below which the rain effects are stopped.")]
        private float rainThreshold = 0.03f;

        private WeatherModel model;
        private ParticleSystem[] rainSystems = Array.Empty<ParticleSystem>();
        private float[] rainBaseRates = Array.Empty<float>();
        private bool rainPlaying;
        private Behaviour syntyWeather;
        private FieldInfo syntyWind;
        private FieldInfo syntyGale;
        private float syntyPushTime;
        private Vector3 baseWindDirection = new Vector3(0.64f, 0f, 0.77f);
        private bool initialised;
        private bool forcing;
        private bool releasing;
        private bool snapNextFrame;
        private float forceWeight;
        private float releaseWeight;
        private WeatherState forcedTarget;
        private WeatherPreset forcedFrom;
        private WeatherPreset releaseFrom;
        private float transitionSeconds = -1f;
        private float pendingFadeSeconds = -1f;

        /// <summary>The active weather system (set in Awake).</summary>
        public static WeatherSystem Current { get; private set; }

        /// <summary>The blended weather this frame.</summary>
        public WeatherPreset Weather { get; private set; } = WeatherPreset.Default(WeatherState.Clear);

        /// <summary>The state being faded toward (or held).</summary>
        public WeatherState State { get; private set; }

        /// <summary>The state being faded from.</summary>
        public WeatherState PreviousState { get; private set; }

        /// <summary>0..1 progress of the current cross-fade (1 = settled).</summary>
        public float Blend { get; private set; } = 1f;

        /// <summary>Current wind direction (horizontal, normalised).</summary>
        public Vector3 WindDirection { get; private set; } = new Vector3(0.64f, 0f, 0.77f);

        /// <summary>The weather now, or the clear preset when no weather system is in the scene.</summary>
        public static WeatherPreset CurrentWeather => Current != null ? Current.Weather : WeatherPreset.Default(WeatherState.Clear);

        /// <summary>Forces a weather state (null = back to the schedule). Cross-fades over the usual time.</summary>
        public void Force(WeatherState? state)
        {
            forcedState = state.HasValue ? (WeatherOverride)(int)state.Value : WeatherOverride.Auto;
            pendingFadeSeconds = -1f;
        }

        /// <summary>
        /// Forces a weather state (null = back to the schedule) with a cross-fade of <paramref name="fadeSeconds"/> real
        /// seconds instead of the usual 60–120 s (the Journey menu uses ~5 s so a choice shows quickly). Only the change this
        /// call starts uses the short fade; natural changes of the schedule keep <see cref="crossFadeSeconds"/>.
        /// </summary>
        public void Force(WeatherState? state, float fadeSeconds)
        {
            WeatherOverride wanted = state.HasValue ? (WeatherOverride)(int)state.Value : WeatherOverride.Auto;
            if (wanted == forcedState) return;
            forcedState = wanted;
            pendingFadeSeconds = Mathf.Max(0.5f, fadeSeconds);
        }

        /// <summary>True while the sky is changing: a forced change, a fade back to the schedule, or a scheduled cross-fade.</summary>
        public bool IsChanging => releasing || (forcing && forceWeight < 1f) || Blend < 0.995f;

        /// <summary>The forced state, or null while following the schedule.</summary>
        public WeatherState? ForcedState => forcedState == WeatherOverride.Auto ? (WeatherState?)null : (WeatherState)(int)forcedState;

        /// <summary>
        /// Call after the clock jumps (e.g. the time-of-day slider): while following the schedule, the weather eases from
        /// what is on screen to the schedule of the new time over <paramref name="fadeSeconds"/> instead of snapping. Does
        /// nothing while a state is forced (a forced state does not depend on the time).
        /// </summary>
        public void EaseTimeJump(float fadeSeconds)
        {
            if (!initialised || forcedState != WeatherOverride.Auto || forcing) return;
            releasing = true;
            releaseFrom = Weather;
            releaseWeight = 0f;
            transitionSeconds = Mathf.Max(0.5f, fadeSeconds);
        }

        /// <summary>One default preset per <see cref="WeatherState"/>.</summary>
        public static WeatherPreset[] DefaultPresets()
        {
            var result = new WeatherPreset[WeatherModel.StateCount];
            for (int i = 0; i < result.Length; i++) result[i] = WeatherPreset.Default((WeatherState)i);
            return result;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }

        private void Awake()
        {
            Current = this;
            CacheRain();
        }

        private void Start()
        {
            WorldSettings settings = GameWorld.Current != null ? GameWorld.Current.Settings : null;
            int seed = (settings != null ? settings.seed : MedievalZen.Endless.EndlessWorld.Current != null ? MedievalZen.Endless.EndlessWorld.Current.Seed : 1) + seedOffset;
            model = new WeatherModel(seed, presets, slotHours, stayProbability);
            FindSyntyWeather();
            Update();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Update()
        {
            if (model == null) return;
            DayCycle cycle = DayCycle.Current;
            int day = cycle != null ? cycle.Day : 1;
            float hour = cycle != null ? cycle.Hour : 12f;
            float hoursPerSecond = cycle != null ? cycle.Clock.HoursPerRealSecond : 1f / 60f;
            float crossFadeHours = crossFadeSeconds * hoursPerSecond;

            WeatherPreset scheduled = model.Evaluate(WeatherModel.AbsoluteHours(day, hour), crossFadeHours,
                out WeatherState from, out WeatherState to, out float blend);

            if (!initialised)
            {
                Weather = scheduled;
                initialised = true;
            }

            // A forced state fades in (and a release fades back to the schedule) over the same real time as a natural
            // change, always starting from what is on screen, so nothing ever jumps.
            // A transition started by Force(state, seconds) uses that fade; any other uses crossFadeSeconds.
            bool snap = snapNextFrame;
            snapNextFrame = false;
            WeatherPreset result = scheduled;
            if (forcedState != WeatherOverride.Auto)
            {
                var wanted = (WeatherState)(int)forcedState;
                if (!forcing || wanted != forcedTarget)
                {
                    forcedFrom = Weather;
                    forcedTarget = wanted;
                    forceWeight = 0f;
                    forcing = true;
                    releasing = false;
                    BeginTransition();
                }

                forceWeight = Mathf.Min(1f, forceWeight + Step(snap));
                blend = AtmosphereMath.Smooth(forceWeight);
                result = WeatherPreset.Lerp(forcedFrom, model.Preset(forcedTarget), blend);
                from = PreviousState;
                to = forcedTarget;
            }
            else if (forcing)
            {
                forcing = false;
                releasing = true;
                releaseFrom = Weather;
                releaseWeight = 0f;
                BeginTransition();
            }

            if (releasing)
            {
                releaseWeight = Mathf.Min(1f, releaseWeight + Step(snap));
                result = WeatherPreset.Lerp(releaseFrom, scheduled, AtmosphereMath.Smooth(releaseWeight));
                if (releaseWeight >= 1f) releasing = false;
            }

            Weather = result;
            PreviousState = from;
            State = to;
            Blend = blend;

            ApplyDayCycle(cycle, result);
            ApplyWind(result);
            ApplyRain(result.rain);
        }

        private void BeginTransition()
        {
            transitionSeconds = pendingFadeSeconds;   // <= 0: the usual crossFadeSeconds (read live, as before)
            pendingFadeSeconds = -1f;
        }

        private float Step(bool snap)
        {
            if (snap) return 1f;
            return transitionSeconds > 0f ? Time.deltaTime / Mathf.Max(0.5f, transitionSeconds) : Time.deltaTime / Mathf.Max(1f, crossFadeSeconds);
        }

        private static void ApplyDayCycle(DayCycle cycle, in WeatherPreset weather)
        {
            if (cycle == null) return;
            cycle.SunIntensityMultiplier = weather.sunMultiplier;
            cycle.CloudTintMultiplier = weather.cloudTint;
            cycle.Overcast = weather.overcast;
        }

        private void ApplyWind(in WeatherPreset weather)
        {
            float wander = (Mathf.PerlinNoise(Time.time * 0.004f, 3.1f) * 2f - 1f) * windWander;
            Vector3 direction = Quaternion.Euler(0f, wander, 0f) * baseWindDirection;
            WindDirection = direction;
            float gustNoise = Mathf.PerlinNoise(Time.time * 0.05f, 7.7f);
            float wind = Mathf.Clamp01(weather.wind * (0.85f + 0.3f * gustNoise));
            float gale = Mathf.Clamp01(weather.gust);
            Shader.SetGlobalVector(WindDirectionId, direction);
            Shader.SetGlobalFloat(WindIntensityId, wind);
            Shader.SetGlobalFloat(GaleStrengthId, gale);

            // The Synty WeatherControl pushes its own fields every Update (after us); keep them in step, twice a second
            // (reflection boxes the float, so not every frame).
            if (syntyWeather == null || Time.unscaledTime < syntyPushTime) return;
            syntyPushTime = Time.unscaledTime + 0.5f;
            syntyWind?.SetValue(syntyWeather, wind);
            syntyGale?.SetValue(syntyWeather, gale);
            syntyWeather.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private void ApplyRain(float rain)
        {
            if (rainSystems.Length == 0) return;
            bool wanted = rain > rainThreshold;
            for (int i = 0; i < rainSystems.Length; i++)
            {
                ParticleSystem ps = rainSystems[i];
                if (ps == null) continue;
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.rateOverTimeMultiplier = rainBaseRates[i] * Mathf.Clamp01(rain);
            }

            if (wanted == rainPlaying) return;
            rainPlaying = wanted;
            if (rainRoot != null && wanted && !rainRoot.activeSelf) rainRoot.SetActive(true);
            for (int i = 0; i < rainSystems.Length; i++)
            {
                ParticleSystem ps = rainSystems[i];
                if (ps == null) continue;
                if (wanted) ps.Play(false);
                else ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void CacheRain()
        {
            if (rainRoot == null) return;
            rainSystems = rainRoot.GetComponentsInChildren<ParticleSystem>(true);
            rainBaseRates = new float[rainSystems.Length];
            for (int i = 0; i < rainSystems.Length; i++)
            {
                rainBaseRates[i] = rainSystems[i].emission.rateOverTimeMultiplier;
                rainSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            rainPlaying = false;
        }

        private void FindSyntyWeather()
        {
            baseWindDirection = Quaternion.Euler(0f, windYaw, 0f) * Vector3.forward;
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null || behaviour.GetType().Name != "WeatherControl") continue;
                syntyWeather = behaviour;
                Type type = behaviour.GetType();
                syntyWind = type.GetField("windIntensity", BindingFlags.Public | BindingFlags.Instance);
                syntyGale = type.GetField("weatherIntensity", BindingFlags.Public | BindingFlags.Instance);
                if (syntyWind != null && syntyWind.FieldType != typeof(float)) syntyWind = null;
                if (syntyGale != null && syntyGale.FieldType != typeof(float)) syntyGale = null;
                Vector3 forward = behaviour.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 1e-4f) baseWindDirection = forward.normalized;
                break;
            }
        }

        [ContextMenu("Weather/Auto (schedule)")]
        private void ForceAuto() => forcedState = WeatherOverride.Auto;

        [ContextMenu("Weather/Clear")]
        private void ForceClear() => forcedState = WeatherOverride.Clear;

        [ContextMenu("Weather/Hazy")]
        private void ForceHazy() => forcedState = WeatherOverride.Hazy;

        [ContextMenu("Weather/Misty")]
        private void ForceMisty() => forcedState = WeatherOverride.Misty;

        [ContextMenu("Weather/Overcast")]
        private void ForceOvercast() => forcedState = WeatherOverride.Overcast;

        [ContextMenu("Weather/Light Rain")]
        private void ForceRain() => forcedState = WeatherOverride.LightRain;

        [ContextMenu("Weather/Skip The Cross-Fade")]
        private void SnapContext() => SkipCrossFade();

        /// <summary>Finishes a forced change on the next frame instead of cross-fading (for previewing).</summary>
        public void SkipCrossFade() => snapNextFrame = true;

        [ContextMenu("Weather/Log Schedule (next 3 days)")]
        private void LogSchedule()
        {
            WorldSettings settings = GameWorld.Current != null ? GameWorld.Current.Settings : FindFirstObjectByType<GameWorld>()?.Settings;
            var preview = new WeatherModel((settings != null ? settings.seed : 1) + seedOffset, presets, slotHours, stayProbability);
            var log = new System.Text.StringBuilder("Weather schedule:");
            int slots = Mathf.CeilToInt(72f / preview.SlotHours);
            for (int i = 0; i < slots; i++)
            {
                double start = i * (double)preview.SlotHours;
                log.Append($"\n  day {(int)(start / 24) + 1} {start % 24:00}:00  {preview.StateForSlot(i)}");
            }

            Debug.Log(log.ToString(), this);
        }
    }
}
