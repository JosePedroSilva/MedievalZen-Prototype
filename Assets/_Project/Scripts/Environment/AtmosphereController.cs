using MedievalZen.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MedievalZen.Environment
{
    /// <summary>
    /// One per scene. Owns the fog over the day (<see cref="AtmosphereProfile"/> curves × the <see cref="WeatherSystem"/>
    /// multipliers × the two master sliders), reads the <see cref="DayCycle"/> (hour, light direction, fog colour) and pushes
    /// the <c>_Atmo*</c> globals the full-screen fog pass (<c>MedievalZen/AtmosphericFog</c>) reads. While that pass is live
    /// Unity's built-in fog stays enabled but linear from <see cref="AtmosphereMath.LiveFogStart"/> (far beyond the far
    /// clip), so it adds nothing to any geometry (no double fogging) while fog keywords and <c>unity_FogColor</c> stay
    /// defined; if the renderer feature is missing, disabled or the shader is unsupported it falls back to built-in
    /// exponential-squared fog with the same curves. Feeds the <see cref="DayCycle"/> cloud hooks (haze, look) so the
    /// clouds belong to the same atmosphere, and treats the cloud ring as part of the sky layer in the pass (elevation fog
    /// with a haze tail). Also animates the time-of-day grading volume (warm golden hours, neutral noon, cool night, a
    /// little bloom in mist), plus the "Rich light" deltas of <see cref="LightLookMath.Grade"/> when <see cref="LightLook"/> is on
    /// (contrast, saturation, cool-shadow / warm-highlight split toning added to the runtime profile, golden-hour bloom).
    /// Forest pass (2026-10-05, endless road only, while <see cref="ForestMood.Allowed"/>): keeps <see cref="ForestMood.Canopy"/> from
    /// the tree density around the camera; under the canopy the haze is deeper (× 1 + <see cref="ForestMood.HazeGain"/> · canopy) and
    /// a little warmer by day, and the rich grade gains its forest term (<see cref="LightLookMath.AddForest"/>). In the open the canopy
    /// is 0 and every value is exactly as before; the clouds' haze is computed before the forest term.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-55)]
    public sealed class AtmosphereController : MonoBehaviour, IOriginShiftable
    {
        /// <summary>Name of the fog pass shader (the renderer feature is found through its material).</summary>
        public const string FogShaderName = "MedievalZen/AtmosphericFog";

        private const float NoisePeriodCells = 1024f;

        private static readonly int FogColorId = Shader.PropertyToID("_AtmoFogColor");
        private static readonly int SunColorId = Shader.PropertyToID("_AtmoSunColor");
        private static readonly int AntiSunColorId = Shader.PropertyToID("_AtmoAntiSunColor");
        private static readonly int SunDirId = Shader.PropertyToID("_AtmoSunDir");
        private static readonly int DensityId = Shader.PropertyToID("_AtmoDensity");
        private static readonly int ShapeId = Shader.PropertyToID("_AtmoShape");
        private static readonly int SkyId = Shader.PropertyToID("_AtmoSky");
        private static readonly int SkyCenterId = Shader.PropertyToID("_AtmoSkyCenter");
        private static readonly int SkyTailId = Shader.PropertyToID("_AtmoSkyTail");
        private static readonly int NoiseId = Shader.PropertyToID("_AtmoNoise");

        [Header("Master")]
        [SerializeField, Range(0f, 2f), Tooltip("Master fog amount: multiplies every fog density (1 = as designed, 0 = no fog). Readability clamps still apply.")]
        private float fogAmount = 1f;

        [SerializeField, Range(0f, 2f), Tooltip("How far you can see through the aerial haze (multiplies the haze half-distance; 2 = twice as clear).")]
        private float hazeDistance = 1f;

        [SerializeField, Range(0f, 2f), Tooltip("How much the clouds melt into the haze (0 = crisp cut-outs, 1 = as designed, 2 = very soft). Also scales the haze tail on the sky above the horizon.")]
        private float cloudSoftness = 1f;

        [Header("References")]
        [SerializeField, Tooltip("Material of the fog renderer feature (MedievalZen/AtmosphericFog). Used to detect whether the pass is live.")]
        private Material fogMaterial = null;

        [SerializeField, Tooltip("Global volume with the TimeOfDay grading profile (ColorAdjustments, WhiteBalance, Bloom, Vignette).")]
        private Volume gradingVolume = null;

        [Header("Fog over the day")]
        [SerializeField]
        private AtmosphereProfile profile = new AtmosphereProfile();

        [SerializeField, Tooltip("Height fog is densest this many metres above the lake level (or the low ground when there are no lakes).")]
        private float baseAltitudeOffset = 3f;

        [SerializeField, Tooltip("Colour the fog takes toward the sun (0 = midnight, 0.5 = noon; the night keys are the moon glow).")]
        private Gradient scatterColor = MakeGradient(
            (0f, new Color(0.16f, 0.2f, 0.34f)), (0.23f, new Color(0.3f, 0.26f, 0.32f)), (0.26f, new Color(1f, 0.56f, 0.3f)),
            (0.32f, new Color(1f, 0.8f, 0.58f)), (0.5f, new Color(1f, 0.95f, 0.84f)), (0.69f, new Color(1f, 0.74f, 0.46f)),
            (0.76f, new Color(1f, 0.5f, 0.27f)), (0.8f, new Color(0.18f, 0.2f, 0.34f)));

        [SerializeField, Min(0f), Tooltip("Brightness of the in-scattered sun colour.")]
        private float scatterIntensity = 1.05f;

        [SerializeField, Range(1f, 64f), Tooltip("Tightness of the glow around the sun (higher = smaller).")]
        private float scatterExponent = 7f;

        [SerializeField, Tooltip("Multiplier on the fog colour on the side away from the sun (cooler shadow side).")]
        private Color antiSunTint = new Color(0.82f, 0.89f, 1f);

        [SerializeField, Range(0f, 1f), Tooltip("Strength of the anti-sun tint.")]
        private float antiSunStrength = 0.55f;

        [SerializeField, Tooltip("Lowest fog colour (moonlit blue — the night never fogs to black).")]
        private Color nightFloor = new Color(0.06f, 0.08f, 0.14f);

        [Header("Sky")]
        [SerializeField, Range(0f, 1f), Tooltip("Fog on the skydome at the horizon (it fades out with elevation; the zenith stays clear).")]
        private float skyHorizonFog = 0.8f;

        [SerializeField, Range(1f, 45f), Tooltip("Elevation in degrees over which the horizon fog fades out on the sky.")]
        private float skyFadeDegrees = 14f;

        [SerializeField, Range(0.5f, 1f), Tooltip("Pixels at this fraction of the skydome radius or further count as sky.")]
        private float skyTestFactor = 0.9f;

        [SerializeField, Range(0f, 1f), Tooltip("Haze tail on the sky layer in clear air (× Cloud Softness): low clouds dissolve into the horizon haze.")]
        private float skyTailClear = 0.15f;

        [SerializeField, Range(0f, 1f), Tooltip("Extra haze tail at full haze / mist (× Cloud Softness).")]
        private float skyTailHaze = 0.6f;

        [SerializeField, Tooltip("Elevation (degrees) where the haze tail ends, in clear air / at full haze. Kept below 60 so the zenith stays clear.")]
        private Vector2 skyTailDegrees = new Vector2(22f, 50f);

        [Header("Clouds")]
        [SerializeField, Range(0f, 1f), Tooltip("Largest saturation any cloud colour may have (0 = grey clouds).")]
        private float cloudSaturation = 0.3f;

        [SerializeField, Range(0f, 1f), Tooltip("How much darker the shaded side of the clouds is than the lit side in clear weather.")]
        private float cloudContrast = 0.6f;

        [SerializeField, Tooltip("Keep the Synty cloud shader's view-dependent scattering (tints each colour channel differently; off by default).")]
        private bool cloudScattering = false;

        [SerializeField, Range(0.3f, 1f), Tooltip("Pixels this fraction of the cloud ring's lowest point above the sky centre (and far enough away) count as clouds (sky layer) in the fog pass.")]
        private float cloudFloorFactor = 0.85f;

        [SerializeField, Range(0.1f, 0.9f), Tooltip("Cloud pixels must also be at least this fraction of the skydome radius away.")]
        private float cloudMinDistanceFactor = 0.35f;

        [Header("Shape")]
        [SerializeField, Range(0.5f, 1f), Tooltip("Largest fog opacity anywhere (distant hills never vanish completely).")]
        private float maxOpacity = 0.985f;

        [SerializeField, Min(0f), Tooltip("Fog starts this many metres from the camera.")]
        private float startDistance = 2f;

        [SerializeField, Range(0f, 1f), Tooltip("Strength of the slow world-space noise in the mist.")]
        private float noiseStrength = 0.45f;

        [SerializeField, Min(10f), Tooltip("Size of the mist noise features in metres.")]
        private float noiseSize = 170f;

        [SerializeField, Min(0f), Tooltip("Mist drift speed in m/s at full wind.")]
        private float noiseDrift = 2.5f;

        [Header("Fallback (no fog pass)")]
        [SerializeField, Min(50f), Tooltip("Distance at which the built-in exp² fallback fog matches the pass (metres).")]
        private float fallbackMatchDistance = 400f;

        [SerializeField, Min(0.5f), Tooltip("Seconds between checks that the fog renderer feature is present and active.")]
        private float passCheckInterval = 2f;

        [Header("Grading (deltas on the TimeOfDay profile)")]
        [SerializeField, Tooltip("White balance temperature at golden hours / at night.")]
        private Vector2 temperatureGoldenNight = new Vector2(12f, -16f);

        [SerializeField, Tooltip("Saturation change at golden hours / at night / fully overcast.")]
        private Vector3 saturationGoldenNightOvercast = new Vector3(6f, -22f, -14f);

        [SerializeField, Tooltip("Contrast change at golden hours / at night / in thick mist.")]
        private Vector3 contrastGoldenNightMist = new Vector3(6f, -4f, -8f);

        [SerializeField, Tooltip("Exposure lift at night (EV) so the road stays visible.")]
        private float nightExposureLift = 0.3f;

        [SerializeField, Tooltip("Colour filter at golden hours.")]
        private Color goldenFilter = new Color(1f, 0.95f, 0.87f);

        [SerializeField, Tooltip("Colour filter at night.")]
        private Color nightFilter = new Color(0.86f, 0.92f, 1.06f);

        [SerializeField, Tooltip("Bloom intensity added in mist / at golden hours.")]
        private Vector2 bloomMistGolden = new Vector2(0.3f, 0.12f);

        [SerializeField, Tooltip("Vignette intensity added at night.")]
        private float nightVignette = 0.08f;

        [Header("Preview")]
        [SerializeField, Range(0f, 24f), Tooltip("Hour used by the 'Preview hour' context menu.")]
        private float previewHour = 6f;

        private float baseAltitude;
        private bool baseAltitudeKnown;
        private float targetBaseAltitude;
        private float nextBaseAltitudeCheck;
        private bool passActive;
        private float nextPassCheck;
        private Vector2 windOffset;
        private Transform skyRoot;
        private Renderer skyRenderer;
        private float skyRadius;
        private float skyHeight;
        private Renderer[] cloudRenderers = System.Array.Empty<Renderer>();
        private float cloudFloor;

        private ColorAdjustments colorAdjustments;
        private WhiteBalance whiteBalance;
        private Bloom bloom;
        private Vignette vignette;
        private SplitToning splitToning;
        private float baseBloomThreshold = 1f;
        private float baseExposure;
        private float baseContrast;
        private float baseSaturation;
        private Color baseFilter = Color.white;
        private float baseTemperature;
        private float baseBloom;
        private float baseVignette;
        private ITreeDensity canopyDensity;
        private float nextCanopySample;
        private float canopyTarget;

        /// <summary>The active controller (set in Awake).</summary>
        public static AtmosphereController Current { get; private set; }

        /// <summary>Master fog amount, 0..2.</summary>
        public float FogAmount
        {
            get => fogAmount;
            set => fogAmount = Mathf.Clamp(value, 0f, 2f);
        }

        /// <summary>Haze distance multiplier, 0..2.</summary>
        public float HazeDistance
        {
            get => hazeDistance;
            set => hazeDistance = Mathf.Clamp(value, 0f, 2f);
        }

        /// <summary>Cloud softness, 0..2 (0 = crisp, 1 = as designed, 2 = very soft).</summary>
        public float CloudSoftness
        {
            get => cloudSoftness;
            set => cloudSoftness = Mathf.Clamp(value, 0f, 2f);
        }

        /// <summary>0..1 haze around the clouds this frame (aerial haze + mist patches; see <see cref="AtmosphereMath.CloudHaze"/>).</summary>
        public float CloudHaze { get; private set; }

        /// <summary>True while the full-screen fog pass is live (built-in fog adds nothing).</summary>
        public bool PassActive => passActive;

        /// <summary>Altitude at which the height fog is densest.</summary>
        public float BaseAltitude => baseAltitude;

        /// <summary>0..1 amount of low mist patches wanted now (time of day × weather).</summary>
        public float MistAmount { get; private set; }

        /// <summary>The fog densities pushed this frame.</summary>
        public FogSettings Fog { get; private set; }

        /// <summary>The designer's day curves.</summary>
        public AtmosphereProfile Profile => profile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }

        private void Awake()
        {
            Current = this;
        }

        private void Start()
        {
            ResolveBaseAltitude();
            ResolveSky();
            ResolveGrading();
            nextPassCheck = 0f;
            Apply(DayCycle.Current != null ? DayCycle.Current.SolarHour : 12f, true, Time.deltaTime);   // the sky's hour (= Hour in spring and autumn)
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void OnDisable()
        {
            // Leave a sane state behind: no pass fog, the day cycle's built-in linear fog back on.
            Shader.SetGlobalVector(ShapeId, Vector4.zero);
            Shader.SetGlobalVector(SkyTailId, Vector4.zero);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            DayCycle cycle = DayCycle.Current;
            if (cycle != null)
            {
                cycle.ExternalFogDistances = false;
                cycle.CloudHaze = 0f;
                cycle.CloudLook = CloudLookSettings.Default;
            }
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("AtmosphereController");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            Apply(DayCycle.Current != null ? DayCycle.Current.SolarHour : 12f, true, Time.deltaTime);   // the sky's hour (= Hour in spring and autumn)
        }

        /// <summary>
        /// Evaluates and pushes everything for <paramref name="hour"/>. <paramref name="touchRenderSettings"/> false leaves the
        /// scene's built-in fog alone (edit-mode preview).
        /// </summary>
        private void Apply(float hour, bool touchRenderSettings, float deltaTime)
        {
            if (!baseAltitudeKnown) ResolveBaseAltitude();
            else if (Application.isPlaying) FollowBaseAltitude(deltaTime);
            if (Time.unscaledTime >= nextPassCheck || !Application.isPlaying)
            {
                nextPassCheck = Time.unscaledTime + passCheckInterval;
                passActive = IsFogPassLive(fogMaterial);
            }

            WeatherPreset weather = WeatherSystem.CurrentWeather;
            FogSettings fog = profile.Evaluate(hour, weather, fogAmount, hazeDistance, baseAltitude, out float scatter, out float patches);
            FogSettings cloudFog = fog;   // the clouds' haze ignores the forest term
            float canopy = UpdateCanopy(deltaTime);
            if (canopy > 0f) fog.hazeDensity *= 1f + ForestMood.HazeGain * canopy;
            Fog = fog;
            MistAmount = patches;

            DayCycle cycle = DayCycle.Current;
            float t = DayClock.Wrap(hour) / DayClock.HoursPerDay;
            Color fogColor = cycle != null ? cycle.CurrentFogColor : new Color(0.75f, 0.8f, 0.85f);
            fogColor = new Color(Mathf.Max(fogColor.r, nightFloor.r), Mathf.Max(fogColor.g, nightFloor.g), Mathf.Max(fogColor.b, nightFloor.b), 1f);
            Vector3 lightDir = cycle != null ? cycle.LightDirection : Quaternion.Euler(-40f, 160f, 0f) * Vector3.back;
            float elevation = cycle != null ? cycle.SunElevation : 40f;
            if (canopy > 0f)
            {
                // Warm haze among the trunks by day (sunlight scattered in the green-gold air under the leaves).
                float warm = 0.5f * canopy * LightLookMath.DayWeight(elevation) * (1f - 0.6f * Mathf.Clamp01(weather.overcast));
                fogColor = Color.Lerp(fogColor, new Color(fogColor.r * 1.06f, fogColor.g * 1.02f, fogColor.b * 0.86f, 1f), warm);
            }


            // In-scattering: the sun colour, dimmed by weather; toward the grey of the sky when overcast.
            Color sunScatter = scatterColor.Evaluate(t) * scatterIntensity;
            sunScatter = Color.Lerp(sunScatter, fogColor * 1.08f, Mathf.Clamp01(weather.overcast) * 0.7f);

            Shader.SetGlobalColor(FogColorId, fogColor);
            Shader.SetGlobalColor(SunColorId, sunScatter);
            Shader.SetGlobalVector(AntiSunColorId, new Vector4(antiSunTint.r, antiSunTint.g, antiSunTint.b, antiSunStrength));
            Shader.SetGlobalVector(SunDirId, new Vector4(lightDir.x, lightDir.y, lightDir.z, scatter));
            Shader.SetGlobalVector(DensityId, new Vector4(fog.hazeDensity, fog.mistDensity, fog.mistFalloff, fog.baseAltitude));
            Shader.SetGlobalVector(ShapeId, new Vector4(passActive ? maxOpacity : 0f, startDistance, scatterExponent, skyTestFactor));

            UpdateSky();
            float skyFog = Mathf.Clamp01(skyHorizonFog * Mathf.Lerp(1f, 1.15f, weather.overcast));
            Shader.SetGlobalVector(SkyId, new Vector4(skyFog, Mathf.Sin(skyFadeDegrees * Mathf.Deg2Rad), skyRadius, skyHeight));
            if (skyRoot != null) Shader.SetGlobalVector(SkyCenterId, skyRoot.position);

            // Clouds and the sky layer: haze from the aerial haze + mist patches; a longer haze tail above the horizon so low
            // clouds dissolve into it (never past 60°, the zenith stays clear); cloud pixels are found by height and distance.
            float haze = AtmosphereMath.CloudHaze(cloudFog, patches);
            CloudHaze = haze;
            float softness = Mathf.Clamp(cloudSoftness, 0f, 2f);
            float tail = Mathf.Clamp01(skyHorizonFog * softness * (skyTailClear + skyTailHaze * haze));
            float tailDegrees = Mathf.Clamp(Mathf.Lerp(skyTailDegrees.x, skyTailDegrees.y, haze), 1f, 60f);
            bool cloudsKnown = cloudFloor > 1f && skyRadius > 1f;
            Shader.SetGlobalVector(SkyTailId, new Vector4(tail, Mathf.Sin(tailDegrees * Mathf.Deg2Rad),
                cloudsKnown ? cloudFloor * cloudFloorFactor : 0f, cloudsKnown ? skyRadius * cloudMinDistanceFactor : 0f));
            if (cycle != null)
            {
                cycle.CloudHaze = haze;
                cycle.CloudLook = new CloudLookSettings
                {
                    saturation = cloudSaturation,
                    contrast = cloudContrast,
                    softness = softness,
                    scattering = cloudScattering,
                };
            }

            float frequency = 1f / Mathf.Max(10f, noiseSize);
            float period = NoisePeriodCells / frequency;
            Vector3 wind = WeatherSystem.Current != null ? WeatherSystem.Current.WindDirection : new Vector3(0.64f, 0f, 0.77f);
            float driftSpeed = noiseDrift * (0.25f + weather.wind);
            windOffset.x = Mathf.Repeat(windOffset.x - wind.x * driftSpeed * deltaTime, period);
            windOffset.y = Mathf.Repeat(windOffset.y - wind.z * driftSpeed * deltaTime, period);
            Shader.SetGlobalVector(NoiseId, new Vector4(noiseStrength, frequency, windOffset.x, windOffset.y));

            if (touchRenderSettings) ApplyBuiltInFog(fog, fogColor, cycle);
            if (Application.isPlaying) ApplyGrading(elevation, weather, patches);
        }

        private void ApplyBuiltInFog(in FogSettings fog, Color fogColor, DayCycle cycle)
        {
            if (cycle != null) cycle.ExternalFogDistances = true;
            if (passActive)
            {
                // Enabled but linear from far beyond the far clip: exactly zero on every pixel (the pass does the fog), while
                // fog keywords and unity_FogColor stay defined for any shader that reads them.
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = AtmosphereMath.LiveFogStart;
                RenderSettings.fogEndDistance = AtmosphereMath.LiveFogEnd;
                RenderSettings.fogColor = fogColor;
                return;
            }

            // Fallback: exp² fog matched to the pass at the match distance for a horizontal view at the camera's height.
            Camera main = Camera.main;
            float cameraY = main != null ? main.transform.position.y : fog.baseAltitude;
            float tau = AtmosphereMath.OpticalDepth(fog, fallbackMatchDistance, cameraY, cameraY);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Sqrt(Mathf.Max(0f, tau)) / fallbackMatchDistance;
            RenderSettings.fogColor = fogColor;
        }

        private void ApplyGrading(float elevation, in WeatherPreset weather, float mist)
        {
            if (colorAdjustments == null && whiteBalance == null && bloom == null && vignette == null) return;
            float golden = AtmosphereMath.Bump(elevation, 6f, 12f);           // sun low above the horizon
            float night = AtmosphereMath.SmoothRange(-1f, -10f, elevation);    // sun well below the horizon
            golden *= 1f - 0.7f * Mathf.Clamp01(weather.overcast);
            float overcast = Mathf.Clamp01(weather.overcast);

            // "Rich light" (LightLook, Journey menu): deltas on top of the time-of-day grade; all zero / neutral with it off.
            RichGrade rich = LightLookMath.Grade(LightLook.Weight, elevation, overcast, mist);
            LightLookMath.AddForest(ref rich, LightLook.Weight, ForestMood.Canopy, elevation, overcast);

            if (colorAdjustments != null)
            {
                colorAdjustments.postExposure.value = baseExposure + night * nightExposureLift + rich.Exposure;
                colorAdjustments.contrast.value = baseContrast + golden * contrastGoldenNightMist.x + night * contrastGoldenNightMist.y + mist * contrastGoldenNightMist.z + rich.Contrast;
                colorAdjustments.saturation.value = baseSaturation + golden * saturationGoldenNightOvercast.x + night * saturationGoldenNightOvercast.y + overcast * saturationGoldenNightOvercast.z + rich.Saturation;
                Color filter = Color.Lerp(Color.white, goldenFilter, golden);
                filter = Color.Lerp(filter, nightFilter, night);
                colorAdjustments.colorFilter.value = baseFilter * filter;
            }

            if (whiteBalance != null)
            {
                whiteBalance.temperature.value = baseTemperature + golden * temperatureGoldenNight.x + night * temperatureGoldenNight.y + rich.Temperature;
            }

            if (bloom != null)
            {
                bloom.intensity.value = baseBloom + mist * bloomMistGolden.x + golden * bloomMistGolden.y + rich.BloomIntensity;
                bloom.threshold.value = baseBloomThreshold * rich.BloomThresholdScale;
            }

            if (vignette != null) vignette.intensity.value = baseVignette + night * nightVignette;
            if (splitToning != null)
            {
                splitToning.shadows.value = new Color(rich.Shadows.R, rich.Shadows.G, rich.Shadows.B, 1f);
                splitToning.highlights.value = new Color(rich.Highlights.R, rich.Highlights.G, rich.Highlights.B, 1f);
            }
        }

        private void ResolveBaseAltitude()
        {
            GameWorld world = GameWorld.Current != null ? GameWorld.Current : FindFirstObjectByType<GameWorld>();
            if (world == null) return;
            if (!world.Surface.TryGetFogBase(ViewPosition(), out float ground)) return;
            baseAltitude = ground + baseAltitudeOffset;
            targetBaseAltitude = baseAltitude;
            baseAltitudeKnown = true;
        }

        /// <summary>
        /// Endless road only (a dynamic surface): every few seconds asks the surface for the fog base near the traveller and
        /// eases toward it at <see cref="BaseAltitudeFollowSpeed"/>, so the fog follows the land up a pass without jumping. A
        /// fixed world keeps the altitude resolved at start (unchanged behaviour).
        /// </summary>
        private void FollowBaseAltitude(float deltaTime)
        {
            GameWorld world = GameWorld.Current;
            if (world == null || !world.Surface.IsDynamic) return;
            if (Time.time >= nextBaseAltitudeCheck)
            {
                nextBaseAltitudeCheck = Time.time + 3f;
                if (world.Surface.TryGetFogBase(ViewPosition(), out float ground)) targetBaseAltitude = ground + baseAltitudeOffset;
            }

            baseAltitude = Mathf.MoveTowards(baseAltitude, targetBaseAltitude, BaseAltitudeFollowSpeed * Mathf.Max(0f, deltaTime));
        }

        /// <summary>
        /// Forest pass: samples the tree density around the camera twice a second (endless road, forest extras allowed) and eases
        /// <see cref="ForestMood.Canopy"/> toward it. Returns the canopy (0 when not allowed: the old look).
        /// </summary>
        private float UpdateCanopy(float deltaTime)
        {
            GameWorld world = GameWorld.Current;
            if (!Application.isPlaying || !ForestMood.Allowed || world == null || !world.Surface.IsDynamic)
            {
                ForestMood.Canopy = 0f;
                canopyTarget = 0f;
                return 0f;
            }

            if (canopyDensity == null) canopyDensity = world.Surface.CreateTreeDensity(16f);
            if (Time.time >= nextCanopySample)
            {
                nextCanopySample = Time.time + 0.5f;
                Vector3 eye = ViewPosition();
                canopyTarget = ForestMood.CanopyFor(ForestMood.MeanDensity(canopyDensity.Sample, eye.x, eye.z));
            }

            ForestMood.Follow(canopyTarget, deltaTime);
            return ForestMood.Canopy;
        }

        /// <summary>Metres per second the fog base follows the land on the endless road.</summary>
        private const float BaseAltitudeFollowSpeed = 1.5f;

        private static Vector3 ViewPosition()
        {
            Camera main = Camera.main;
            return main != null ? main.transform.position : Vector3.zero;
        }

        /// <summary>
        /// Floating-origin hook (endless road): every scene position moved by <paramref name="delta"/>; shifts the mist
        /// noise's wind offset by the same amount so the world-space noise stays fixed to the land (no visible jump).
        /// </summary>
        public void ShiftOrigin(Vector3 delta)
        {
            float frequency = 1f / Mathf.Max(10f, noiseSize);
            float period = NoisePeriodCells / frequency;
            windOffset.x = Mathf.Repeat(windOffset.x - delta.x, period);
            windOffset.y = Mathf.Repeat(windOffset.y - delta.z, period);
            Shader.SetGlobalVector(NoiseId, new Vector4(noiseStrength, frequency, windOffset.x, windOffset.y));
        }

        private void ResolveSky()
        {
            SkyFollower follower = FindFirstObjectByType<SkyFollower>();
            skyRoot = follower != null ? follower.transform : null;
            DayCycle cycle = DayCycle.Current != null ? DayCycle.Current : FindFirstObjectByType<DayCycle>();
            Renderer[] domes = cycle != null ? cycle.SkyRenderers : null;
            skyRenderer = domes != null && domes.Length > 0 ? domes[0] : null;
            if (skyRoot == null && skyRenderer != null) skyRoot = skyRenderer.transform;
            cloudRenderers = cycle != null && cycle.CloudRenderers != null ? cycle.CloudRenderers : System.Array.Empty<Renderer>();
            UpdateSky();
        }

        private void UpdateSky()
        {
            if (skyRenderer == null || skyRoot == null)
            {
                skyRadius = 0f;
                skyHeight = 0f;
                cloudFloor = 0f;
                return;
            }

            Bounds b = skyRenderer.bounds;
            skyRadius = Mathf.Max(b.extents.x, b.extents.z);
            skyHeight = Mathf.Max(1f, b.max.y - skyRoot.position.y);

            // Lowest point of the cloud ring above the sky centre (the ring follows the player with the sky root).
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < cloudRenderers.Length; i++)
            {
                Renderer r = cloudRenderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                lowest = Mathf.Min(lowest, r.bounds.min.y - skyRoot.position.y);
            }

            cloudFloor = float.IsInfinity(lowest) ? 0f : Mathf.Max(0f, lowest);
        }

        private void ResolveGrading()
        {
            if (gradingVolume == null || !Application.isPlaying) return;
            VolumeProfile runtime = gradingVolume.profile;   // runtime copy: the asset is never modified
            if (runtime == null) return;
            if (runtime.TryGet(out colorAdjustments))
            {
                baseExposure = colorAdjustments.postExposure.value;
                baseContrast = colorAdjustments.contrast.value;
                baseSaturation = colorAdjustments.saturation.value;
                baseFilter = colorAdjustments.colorFilter.value;
                colorAdjustments.postExposure.overrideState = true;
                colorAdjustments.contrast.overrideState = true;
                colorAdjustments.saturation.overrideState = true;
                colorAdjustments.colorFilter.overrideState = true;
            }

            if (runtime.TryGet(out whiteBalance))
            {
                baseTemperature = whiteBalance.temperature.value;
                whiteBalance.temperature.overrideState = true;
            }

            if (runtime.TryGet(out bloom))
            {
                baseBloom = bloom.intensity.value;
                baseBloomThreshold = bloom.threshold.value;
                bloom.intensity.overrideState = true;
                bloom.threshold.overrideState = true;
            }

            if (runtime.TryGet(out vignette))
            {
                baseVignette = vignette.intensity.value;
                vignette.intensity.overrideState = true;
            }

            // Split toning for the rich look's cool shade / warm light, added to the runtime copy only (the profile asset is
            // not touched). Grey is neutral (soft light with 0.5 is the identity), so the old look is unchanged.
            if (!runtime.TryGet(out splitToning)) splitToning = runtime.Add<SplitToning>(false);
            splitToning.shadows.Override(Color.grey);
            splitToning.highlights.Override(Color.grey);
            splitToning.balance.Override(0f);
        }

        /// <summary>
        /// True when the active URP renderer has an active full-screen feature whose material uses the fog shader, and the
        /// shader is supported on this GPU.
        /// </summary>
        public static bool IsFogPassLive(Material material)
        {
            if (material == null || material.shader == null || !material.shader.isSupported) return false;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return false;
            foreach (ScriptableRendererData data in asset.rendererDataList)
            {
                if (data == null) continue;
                var features = data.rendererFeatures;
                for (int i = 0; i < features.Count; i++)
                {
                    if (features[i] is FullScreenPassRendererFeature pass && pass.isActive && pass.passMaterial != null &&
                        pass.passMaterial.shader == material.shader)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [ContextMenu("Preview hour")]
        private void PreviewHour()
        {
            if (Application.isPlaying && DayCycle.Current != null)
            {
                DayCycle.Current.SetTime(previewHour);
                Apply(previewHour, true, 0f);
                return;
            }

            // Edit mode: push the fog globals for the preview hour so the Scene/Game views show the pass (the scene's
            // saved built-in fog is left alone).
            ResolveBaseAltitude();
            ResolveSky();
            DayCycle cycle = FindFirstObjectByType<DayCycle>();
            if (cycle != null) cycle.SetTime(previewHour);
            Apply(previewHour, false, 0f);
        }

        [ContextMenu("Log Fog Now")]
        private void LogFog()
        {
            FogSettings f = Fog;
            Debug.Log($"Atmosphere: pass {(passActive ? "live" : "OFF (built-in fallback)")}, haze {f.hazeDensity:0.00000}/m, mist {f.mistDensity:0.00000}/m, " +
                      $"falloff {f.mistFalloff:0.000}/m, base {f.baseAltitude:0.0} m, opacity at 60 m (base level) " +
                      $"{AtmosphereMath.Opacity(f, 60f, f.baseAltitude, f.baseAltitude):0.00}, mist patches {MistAmount:0.00}, " +
                      $"cloud haze {CloudHaze:0.00}, cloud floor {cloudFloor:0} m (× {cloudFloorFactor:0.00}), clouds {(DayCycle.Current != null ? FormatClouds(DayCycle.Current.CurrentCloudColors) : "-")}", this);
        }

        private static string FormatClouds(in CloudColors c)
        {
            return $"base #{ColorUtility.ToHtmlStringRGB(c.baseColor)} top #{ColorUtility.ToHtmlStringRGB(c.topColor)} " +
                   $"fresnel #{ColorUtility.ToHtmlStringRGB(c.fresnelColor)} fog pull {c.fogWeight:0.00}";
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
