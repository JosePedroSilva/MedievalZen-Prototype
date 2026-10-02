using System;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Drives the time of day: owns a <see cref="DayClock"/>, turns the directional light (sun by day, a faint blue moon by
    /// night), and colours the ambient light, the distance fog, the Synty skydome (<c>_Top_Color</c>/<c>_Bottom_Color</c>)
    /// and the horizon fog ring from gradients over the day. The cloud ring colours come from <see cref="CloudColorModel"/>
    /// (sky, sun and fog colours, weather overcast and the atmosphere's haze), replacing the Synty defaults. Colours are
    /// applied with <see cref="MaterialPropertyBlock"/>s so the Synty materials are never modified. Time keeps running while
    /// the map is open.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    public sealed class DayCycle : MonoBehaviour
    {
        private static readonly int TopColorId = Shader.PropertyToID("_Top_Color");
        private static readonly int BottomColorId = Shader.PropertyToID("_Bottom_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CloudBaseColorId = Shader.PropertyToID("_Base_Color");
        private static readonly int CloudFresnelColorId = Shader.PropertyToID("_Fresnel_Color");
        private static readonly int CloudScatteringColorId = Shader.PropertyToID("_Scattering_Color");
        private static readonly int CloudEnableFogId = Shader.PropertyToID("_Enable_Fog");
        private static readonly int CloudEnableScatteringId = Shader.PropertyToID("_Enable_Scattering");

        [Header("References")]
        [SerializeField, Tooltip("Directional light used as sun and moon.")]
        private Light sun;

        [SerializeField, Tooltip("Skydome renderers (Synty SkyDome shader: _Top_Color / _Bottom_Color).")]
        private Renderer[] skyRenderers = Array.Empty<Renderer>();

        [SerializeField, Tooltip("Cloud ring renderers (Synty Clouds shader: _Base_Color / _Top_Color / _Fresnel_Color come from the cloud colour model).")]
        private Renderer[] cloudRenderers = Array.Empty<Renderer>();

        [SerializeField, Tooltip("Horizon fog ring renderers (_BaseColor follows the fog colour, keeping the material's alpha).")]
        private Renderer[] fogRingRenderers = Array.Empty<Renderer>();

        [Header("Time")]
        [SerializeField, Range(0f, 24f), Tooltip("Hour at which play starts.")]
        private float startHour = 8f;

        [SerializeField, Min(0.1f), Tooltip("Real minutes per in-game day when the scene's GameWorld has no WorldSettings.")]
        private float fallbackDayLengthMinutes = 24f;

        [SerializeField, Min(0f), Tooltip("Multiplier on the passage of time (1 = WorldSettings.dayLengthMinutes).")]
        private float timeScale = 1f;

        [Header("Sun path")]
        [SerializeField, Range(10f, 89f), Tooltip("Sun elevation at noon in degrees.")]
        private float maxSunElevation = 58f;

        [SerializeField, Range(-180f, 180f), Tooltip("Rotates the whole sun path about the vertical axis (0 = rises in +X/east, noon towards -Z).")]
        private float azimuthOffset = -20f;

        [SerializeField, Tooltip("Sun colour over the day (0 = midnight, 0.5 = noon).")]
        private Gradient sunColor = MakeGradient(
            (0.24f, new Color(1f, 0.45f, 0.2f)), (0.3f, new Color(1f, 0.72f, 0.45f)), (0.38f, new Color(1f, 0.93f, 0.82f)),
            (0.5f, new Color(1f, 0.98f, 0.94f)), (0.62f, new Color(1f, 0.93f, 0.8f)), (0.7f, new Color(1f, 0.68f, 0.4f)),
            (0.76f, new Color(1f, 0.42f, 0.2f)));

        [SerializeField, Min(0f), Tooltip("Sun intensity at noon.")]
        private float sunIntensity = 1.35f;

        [SerializeField, Range(1f, 30f), Tooltip("Sun intensity ramps from 0 at the horizon to full over this many degrees of elevation.")]
        private float sunRampDegrees = 14f;

        [SerializeField, Tooltip("Moonlight colour.")]
        private Color moonColor = new Color(0.55f, 0.66f, 0.95f);

        [SerializeField, Min(0f), Tooltip("Moonlight intensity at full night.")]
        private float moonIntensity = 0.14f;

        [Header("Ambient")]
        [SerializeField, Tooltip("Ambient sky colour (trilight) over the day.")]
        private Gradient ambientSky = MakeGradient(
            (0.0f, new Color(0.1f, 0.13f, 0.22f)), (0.22f, new Color(0.12f, 0.15f, 0.25f)), (0.27f, new Color(0.45f, 0.4f, 0.42f)),
            (0.35f, new Color(0.58f, 0.66f, 0.78f)), (0.65f, new Color(0.58f, 0.66f, 0.78f)), (0.73f, new Color(0.5f, 0.4f, 0.42f)),
            (0.79f, new Color(0.12f, 0.15f, 0.25f)), (1f, new Color(0.1f, 0.13f, 0.22f)));

        [SerializeField, Tooltip("Ambient horizon colour (trilight) over the day.")]
        private Gradient ambientEquator = MakeGradient(
            (0.0f, new Color(0.08f, 0.1f, 0.16f)), (0.22f, new Color(0.09f, 0.11f, 0.18f)), (0.27f, new Color(0.55f, 0.4f, 0.32f)),
            (0.35f, new Color(0.55f, 0.56f, 0.52f)), (0.65f, new Color(0.55f, 0.56f, 0.52f)), (0.73f, new Color(0.58f, 0.4f, 0.3f)),
            (0.79f, new Color(0.09f, 0.11f, 0.18f)), (1f, new Color(0.08f, 0.1f, 0.16f)));

        [SerializeField, Tooltip("Ambient ground colour (trilight) over the day.")]
        private Gradient ambientGround = MakeGradient(
            (0.0f, new Color(0.08f, 0.08f, 0.1f)), (0.25f, new Color(0.14f, 0.12f, 0.1f)), (0.35f, new Color(0.3f, 0.28f, 0.22f)),
            (0.65f, new Color(0.3f, 0.28f, 0.22f)), (0.75f, new Color(0.14f, 0.12f, 0.1f)), (1f, new Color(0.08f, 0.08f, 0.1f)));

        [SerializeField, Range(0f, 0.3f), Tooltip("No ambient channel ever drops below this (moonlit blue, never pitch black).")]
        private float minAmbient = 0.08f;

        [Header("Fog")]
        [SerializeField, Tooltip("Distance fog colour over the day (also the horizon fog ring colour).")]
        private Gradient fogColor = MakeGradient(
            (0.0f, new Color(0.1f, 0.12f, 0.19f)), (0.22f, new Color(0.12f, 0.14f, 0.22f)), (0.26f, new Color(0.78f, 0.62f, 0.55f)),
            (0.34f, new Color(0.75f, 0.8f, 0.85f)), (0.66f, new Color(0.75f, 0.81f, 0.86f)), (0.74f, new Color(0.85f, 0.6f, 0.48f)),
            (0.79f, new Color(0.12f, 0.14f, 0.22f)), (1f, new Color(0.1f, 0.12f, 0.19f)));

        [SerializeField, Tooltip("Linear fog start distance in metres over the day (x = hour).")]
        private AnimationCurve fogStart = new AnimationCurve(
            new Keyframe(0f, 90f), new Keyframe(5.5f, 60f), new Keyframe(8f, 160f), new Keyframe(12f, 280f),
            new Keyframe(18f, 220f), new Keyframe(21f, 110f), new Keyframe(24f, 90f));

        [SerializeField, Tooltip("Linear fog end distance in metres over the day (x = hour). Dawn is misty.")]
        private AnimationCurve fogEnd = new AnimationCurve(
            new Keyframe(0f, 1100f), new Keyframe(5.5f, 900f), new Keyframe(8f, 1700f), new Keyframe(12f, 2600f),
            new Keyframe(18f, 2200f), new Keyframe(21f, 1300f), new Keyframe(24f, 1100f));

        [Header("Sky")]
        [SerializeField, Tooltip("Skydome zenith colour over the day.")]
        private Gradient skyTop = MakeGradient(
            (0.0f, new Color(0.01f, 0.02f, 0.06f)), (0.22f, new Color(0.02f, 0.04f, 0.1f)), (0.27f, new Color(0.14f, 0.2f, 0.38f)),
            (0.36f, new Color(0.05f, 0.28f, 0.58f)), (0.64f, new Color(0.05f, 0.28f, 0.58f)), (0.73f, new Color(0.15f, 0.18f, 0.38f)),
            (0.79f, new Color(0.02f, 0.04f, 0.1f)), (1f, new Color(0.01f, 0.02f, 0.06f)));

        [SerializeField, Tooltip("Skydome horizon colour over the day.")]
        private Gradient skyBottom = MakeGradient(
            (0.0f, new Color(0.06f, 0.08f, 0.14f)), (0.22f, new Color(0.08f, 0.1f, 0.18f)), (0.26f, new Color(1f, 0.6f, 0.42f)),
            (0.34f, new Color(1f, 0.92f, 0.84f)), (0.66f, new Color(1f, 0.94f, 0.88f)), (0.74f, new Color(1f, 0.55f, 0.35f)),
            (0.79f, new Color(0.08f, 0.1f, 0.18f)), (1f, new Color(0.06f, 0.08f, 0.14f)));

        private DayClock clock;
        private MaterialPropertyBlock block;
        private float[] fogRingAlpha = Array.Empty<float>();
        private int lastWholeHour = -1;

        /// <summary>The active day cycle (set in Awake).</summary>
        public static DayCycle Current { get; private set; }

        /// <summary>Raised when the whole hour changes and after <see cref="SetTime"/> (argument: the hour, 0..24).</summary>
        public event Action<float> HourChanged;

        /// <summary>Hour of the day, 0..24.</summary>
        public float Hour => Clock.Hour;

        /// <summary>Day counter, starting at 1.</summary>
        public int Day => Clock.Day;

        /// <summary>True while the sun is up.</summary>
        public bool IsDay => Clock.IsDay;

        /// <summary>The underlying clock.</summary>
        public DayClock Clock => clock ??= new DayClock(fallbackDayLengthMinutes, startHour);

        /// <summary>The directional light driven by this cycle.</summary>
        public Light Sun
        {
            get => sun;
            set => sun = value;
        }

        /// <summary>Weather hook: multiplier on the sun and moon intensity (1 = unchanged). Set every frame by the weather.</summary>
        public float SunIntensityMultiplier { get; set; } = 1f;

        /// <summary>Weather hook: multiplier on the cloud ring colours (white = unchanged).</summary>
        public Color CloudTintMultiplier { get; set; } = Color.white;

        /// <summary>Atmosphere hook, 0..1: haze around the clouds (pulls them toward the fog colour). Set every frame by the atmosphere.</summary>
        public float CloudHaze { get; set; }

        /// <summary>Atmosphere hook: the cloud look (saturation cap, contrast, softness). Set every frame by the atmosphere.</summary>
        public CloudLookSettings CloudLook { get; set; } = CloudLookSettings.Default;

        /// <summary>
        /// Atmosphere hook: while true the cycle leaves <c>RenderSettings.fogStartDistance</c> / <c>fogEndDistance</c> alone
        /// (the atmosphere owns the built-in fog).
        /// </summary>
        public bool ExternalFogDistances { get; set; }

        /// <summary>Cloud colours applied this frame.</summary>
        public CloudColors CurrentCloudColors { get; private set; }

        /// <summary>Weather hook, 0..1: greys the skydome and the fog colour toward an overcast sky (0 = unchanged).</summary>
        public float Overcast { get; set; }

        /// <summary>Fog colour applied this frame (after the overcast hook); the horizon fog ring uses it too.</summary>
        public Color CurrentFogColor { get; private set; } = new Color(0.75f, 0.8f, 0.85f);

        /// <summary>Sun elevation in degrees this frame (negative at night).</summary>
        public float SunElevation { get; private set; }

        /// <summary>World direction toward the light (the sun by day, the moon by night).</summary>
        public Vector3 LightDirection { get; private set; } = Vector3.up;

        /// <summary>Skydome renderers (read-only view, used by the atmosphere to find the sky's size).</summary>
        public Renderer[] SkyRenderers => skyRenderers;

        /// <summary>Cloud ring renderers (read-only view, used by the atmosphere to find the cloud layer).</summary>
        public Renderer[] CloudRenderers => cloudRenderers;

        /// <summary>Assigns the sky renderers (used by the demo-scene setup).</summary>
        public void SetSkyRenderers(Renderer[] sky, Renderer[] clouds, Renderer[] fogRings)
        {
            skyRenderers = sky ?? Array.Empty<Renderer>();
            cloudRenderers = clouds ?? Array.Empty<Renderer>();
            fogRingRenderers = fogRings ?? Array.Empty<Renderer>();
        }

        /// <summary>
        /// Multiplier on the passage of time (1 = one day per <c>WorldSettings.dayLengthMinutes</c>, 0 = time stands still).
        /// Starts at the inspector value; the Journey menu's "Flow of time" sets it.
        /// </summary>
        public float TimeScale
        {
            get => timeScale;
            set => timeScale = Mathf.Max(0f, value);
        }

        /// <summary>True while time stands still (<see cref="TimeScale"/> is 0).</summary>
        public bool IsPaused => timeScale <= 0f;

        /// <summary>Jumps forward to <paramref name="hour"/> (the next day if it is earlier than now) and applies it at once.</summary>
        public void SetTime(float hour)
        {
            Clock.SetTime(hour);
            Apply();
            lastWholeHour = (int)Clock.Hour;
            HourChanged?.Invoke(Clock.Hour);
        }

        /// <summary>
        /// Sets the hour on the current day (the day counter never changes, whether <paramref name="hour"/> is earlier or
        /// later than now) and applies it at once. Used by the Journey menu's time slider; <see cref="HourChanged"/> is raised
        /// only when the whole hour changes, so dragging does not flood listeners.
        /// </summary>
        public void SetHourOfDay(float hour)
        {
            Clock.SetTime(hour, false);
            Apply();
            int whole = (int)Clock.Hour;
            if (whole == lastWholeHour) return;
            lastWholeHour = whole;
            HourChanged?.Invoke(Clock.Hour);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }

        private void Awake()
        {
            Current = this;
            block = new MaterialPropertyBlock();
            clock = new DayClock(fallbackDayLengthMinutes, startHour);
            CaptureMaterialColours();
        }

        private void Start()
        {
            WorldSettings settings = GameWorld.Current != null ? GameWorld.Current.Settings : null;
            if (settings != null) clock.DayLengthMinutes = settings.dayLengthMinutes;
            else if (MedievalZen.Endless.EndlessWorld.Current != null && MedievalZen.Endless.EndlessWorld.Current.Settings != null)
            {
                clock.DayLengthMinutes = MedievalZen.Endless.EndlessWorld.Current.Settings.dayLengthMinutes;   // the endless road has no WorldSettings
            }

            if (sun == null) sun = RenderSettings.sun;
            if (sun != null) RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            Apply();
            lastWholeHour = (int)clock.Hour;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Update()
        {
            clock.Advance(Time.deltaTime * timeScale);
            Apply();
            int whole = (int)clock.Hour;
            if (whole != lastWholeHour)
            {
                lastWholeHour = whole;
                HourChanged?.Invoke(clock.Hour);
            }
        }

        private void CaptureMaterialColours()
        {
            fogRingAlpha = new float[fogRingRenderers.Length];
            for (int i = 0; i < fogRingRenderers.Length; i++)
            {
                Material m = fogRingRenderers[i] != null ? fogRingRenderers[i].sharedMaterial : null;
                fogRingAlpha[i] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId).a : 0.6f;
            }
        }

        private void Apply()
        {
            float hour = Clock.Hour;
            float t = hour / DayClock.HoursPerDay;
            float elevation = DayClock.SunElevation(hour, maxSunElevation);
            float azimuth = DayClock.SunAzimuth(hour) + azimuthOffset;
            SunElevation = elevation;
            {
                bool up = elevation >= 0f;
                Quaternion toLight = Quaternion.Euler(up ? elevation : -elevation, (up ? azimuth : azimuth + 180f) + 180f, 0f);
                LightDirection = -(toLight * Vector3.forward);
            }

            if (sun != null)
            {
                float sunFactor = Mathf.Clamp01(elevation / sunRampDegrees);
                sunFactor = sunFactor * sunFactor * (3f - 2f * sunFactor);
                float moonFactor = Mathf.Clamp01(-elevation / sunRampDegrees);
                moonFactor = moonFactor * moonFactor * (3f - 2f * moonFactor);

                // By day the light comes from the sun; by night from the moon opposite it. Both are 0 at the horizon swap.
                bool daytime = elevation >= 0f;
                float lightElevation = daytime ? elevation : -elevation;
                float lightAzimuth = daytime ? azimuth : azimuth + 180f;
                sun.transform.rotation = Quaternion.Euler(lightElevation, lightAzimuth + 180f, 0f);
                sun.color = daytime ? sunColor.Evaluate(t) : moonColor;
                sun.intensity = (daytime ? sunIntensity * sunFactor : moonIntensity * moonFactor) * Mathf.Max(0f, SunIntensityMultiplier);
                sun.enabled = sun.intensity > 0.001f;
            }

            RenderSettings.ambientSkyColor = Floor(ambientSky.Evaluate(t));
            RenderSettings.ambientEquatorColor = Floor(ambientEquator.Evaluate(t));
            RenderSettings.ambientGroundColor = Floor(ambientGround.Evaluate(t));
            RenderSettings.ambientLight = RenderSettings.ambientSkyColor;

            float overcast = Mathf.Clamp01(Overcast);
            Color fog = OvercastBlend(fogColor.Evaluate(t), overcast, 0.95f);
            CurrentFogColor = fog;
            RenderSettings.fogColor = fog;
            if (!ExternalFogDistances)
            {
                float start = fogStart.Evaluate(hour);
                RenderSettings.fogStartDistance = start;
                RenderSettings.fogEndDistance = Mathf.Max(start + 10f, fogEnd.Evaluate(hour));
            }

            if (block == null) return;
            Color top = OvercastBlend(skyTop.Evaluate(t), overcast, 1.25f);
            Color bottom = OvercastBlend(skyBottom.Evaluate(t), overcast, 0.85f);
            for (int i = 0; i < skyRenderers.Length; i++)
            {
                Renderer r = skyRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(TopColorId, top);
                block.SetColor(BottomColorId, bottom);
                r.SetPropertyBlock(block);
            }

            // Clouds: derived from the atmosphere instead of multiplying Synty's saturated defaults. The shader's own fog
            // branch is switched off (the colours already carry the haze and the fog pass adds the aerial perspective, so
            // the clouds no longer depend on Unity's built-in fog) and its per-channel scattering is off unless the look
            // asks for it.
            CloudLookSettings look = CloudLook;
            var input = new CloudLightInput
            {
                skyHorizon = bottom,
                skyZenith = top,
                fogColor = fog,
                sunColor = sunColor.Evaluate(t),
                moonColor = moonColor,
                sunElevation = elevation,
                overcast = overcast,
                mist = CloudHaze,
                tint = CloudTintMultiplier,
            };
            CloudColors clouds = CloudColorModel.Evaluate(input, look);
            CurrentCloudColors = clouds;
            for (int i = 0; i < cloudRenderers.Length; i++)
            {
                Renderer r = cloudRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(CloudBaseColorId, clouds.baseColor);
                block.SetColor(TopColorId, clouds.topColor);
                block.SetColor(CloudFresnelColorId, clouds.fresnelColor);
                block.SetColor(CloudScatteringColorId, clouds.scatteringColor);
                block.SetFloat(CloudEnableFogId, 0f);
                block.SetFloat(CloudEnableScatteringId, look.scattering ? 1f : 0f);
                r.SetPropertyBlock(block);
            }

            for (int i = 0; i < fogRingRenderers.Length && i < fogRingAlpha.Length; i++)
            {
                Renderer r = fogRingRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(fog.r, fog.g, fog.b, fogRingAlpha[i]));
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// Moves <paramref name="c"/> toward a slightly cool grey of similar brightness (× <paramref name="brightness"/>) by
        /// <paramref name="amount"/> × 0.85 — an overcast sky keeps a hint of the time-of-day colour.
        /// </summary>
        private static Color OvercastBlend(Color c, float amount, float brightness)
        {
            if (amount <= 0f) return c;
            float lum = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * brightness;
            var grey = new Color(lum * 0.96f, lum * 0.98f, lum * 1.03f, c.a);
            return Color.Lerp(c, grey, amount * 0.85f);
        }

        private Color Floor(Color c)
        {
            return new Color(Mathf.Max(c.r, minAmbient * 0.8f), Mathf.Max(c.g, minAmbient * 0.9f), Mathf.Max(c.b, minAmbient * 1.4f), 1f);
        }

        private static Gradient MakeGradient(params (float time, Color color)[] keys)
        {
            var colorKeys = new GradientColorKey[Mathf.Min(8, keys.Length)];
            for (int i = 0; i < colorKeys.Length; i++) colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].time);
            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
