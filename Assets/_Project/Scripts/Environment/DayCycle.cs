using System;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Drives the time of day: owns a <see cref="DayClock"/>, turns the directional light (sun by day, a faint blue moon by
    /// night), and colours the ambient light, the distance fog, the Synty skydome (<c>_Top_Color</c>/<c>_Bottom_Color</c>),
    /// the cloud ring and the horizon fog ring from gradients over the day. Colours are applied with
    /// <see cref="MaterialPropertyBlock"/>s so the Synty materials are never modified. Time keeps running while the map is open.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    public sealed class DayCycle : MonoBehaviour
    {
        private static readonly int TopColorId = Shader.PropertyToID("_Top_Color");
        private static readonly int BottomColorId = Shader.PropertyToID("_Bottom_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CloudBaseColorId = Shader.PropertyToID("_Base_Color");

        [Header("References")]
        [SerializeField, Tooltip("Directional light used as sun and moon.")]
        private Light sun;

        [SerializeField, Tooltip("Skydome renderers (Synty SkyDome shader: _Top_Color / _Bottom_Color).")]
        private Renderer[] skyRenderers = Array.Empty<Renderer>();

        [SerializeField, Tooltip("Cloud ring renderers (Synty Clouds shader: _Base_Color / _Top_Color are multiplied by the cloud tint).")]
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

        [SerializeField, Tooltip("Multiplier on the cloud ring colours over the day.")]
        private Gradient cloudTint = MakeGradient(
            (0.0f, new Color(0.14f, 0.16f, 0.24f)), (0.22f, new Color(0.16f, 0.18f, 0.26f)), (0.27f, new Color(1f, 0.75f, 0.65f)),
            (0.35f, new Color(1f, 1f, 1f)), (0.65f, new Color(1f, 1f, 1f)), (0.73f, new Color(1f, 0.7f, 0.55f)),
            (0.79f, new Color(0.16f, 0.18f, 0.26f)), (1f, new Color(0.14f, 0.16f, 0.24f)));

        private DayClock clock;
        private MaterialPropertyBlock block;
        private Color[] cloudBase = Array.Empty<Color>();
        private Color[] cloudTop = Array.Empty<Color>();
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

        /// <summary>Assigns the sky renderers (used by the demo-scene setup).</summary>
        public void SetSkyRenderers(Renderer[] sky, Renderer[] clouds, Renderer[] fogRings)
        {
            skyRenderers = sky ?? Array.Empty<Renderer>();
            cloudRenderers = clouds ?? Array.Empty<Renderer>();
            fogRingRenderers = fogRings ?? Array.Empty<Renderer>();
        }

        /// <summary>Jumps forward to <paramref name="hour"/> (the next day if it is earlier than now) and applies it at once.</summary>
        public void SetTime(float hour)
        {
            Clock.SetTime(hour);
            Apply();
            lastWholeHour = (int)Clock.Hour;
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
            cloudBase = new Color[cloudRenderers.Length];
            cloudTop = new Color[cloudRenderers.Length];
            for (int i = 0; i < cloudRenderers.Length; i++)
            {
                Material m = cloudRenderers[i] != null ? cloudRenderers[i].sharedMaterial : null;
                cloudBase[i] = m != null && m.HasProperty(CloudBaseColorId) ? m.GetColor(CloudBaseColorId) : Color.white;
                cloudTop[i] = m != null && m.HasProperty(TopColorId) ? m.GetColor(TopColorId) : Color.white;
            }

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
                sun.intensity = daytime ? sunIntensity * sunFactor : moonIntensity * moonFactor;
                sun.enabled = sun.intensity > 0.001f;
            }

            RenderSettings.ambientSkyColor = Floor(ambientSky.Evaluate(t));
            RenderSettings.ambientEquatorColor = Floor(ambientEquator.Evaluate(t));
            RenderSettings.ambientGroundColor = Floor(ambientGround.Evaluate(t));
            RenderSettings.ambientLight = RenderSettings.ambientSkyColor;

            Color fog = fogColor.Evaluate(t);
            RenderSettings.fogColor = fog;
            float start = fogStart.Evaluate(hour);
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = Mathf.Max(start + 10f, fogEnd.Evaluate(hour));

            if (block == null) return;
            Color top = skyTop.Evaluate(t);
            Color bottom = skyBottom.Evaluate(t);
            for (int i = 0; i < skyRenderers.Length; i++)
            {
                Renderer r = skyRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(TopColorId, top);
                block.SetColor(BottomColorId, bottom);
                r.SetPropertyBlock(block);
            }

            Color tint = cloudTint.Evaluate(t);
            for (int i = 0; i < cloudRenderers.Length && i < cloudBase.Length; i++)
            {
                Renderer r = cloudRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(CloudBaseColorId, cloudBase[i] * tint);
                block.SetColor(TopColorId, cloudTop[i] * tint);
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
