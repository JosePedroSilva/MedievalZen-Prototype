using System.Collections;
using System.Collections.Generic;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MedievalZen.UI
{
    /// <summary>
    /// The Journey menu (<c>Esc</c> / gamepad <c>Start</c>): a parchment side panel on the right of the screen, built in code
    /// by the <see cref="HudController"/>, with four pages — Time, Sky, View and "Where to?" — and Resume / Reset / Quit.
    /// <para>Input: it opens when <see cref="PlayerController.PauseToggled"/> reports the cursor was freed and then becomes
    /// <see cref="PlayerController.InputOverride"/>, so move/look/interact/map/auto-travel are swallowed while it is open and
    /// Pause closes it (the cursor locks again). Auto-travel already under way keeps walking. The panel itself is ordinary
    /// uGUI driven by the EventSystem (mouse, keys, gamepad), plus Tab / shoulder buttons to turn pages and gamepad East to
    /// go back.</para>
    /// <para>Every control applies live (the world keeps rendering and running behind the panel). Preferences persist in
    /// PlayerPrefs as one versioned <see cref="JourneySettings"/> JSON blob; the time of day is not persisted. "Reset to
    /// defaults" restores the values the scene started with.</para>
    /// Sections whose systems are missing from the scene (DayCycle, WeatherSystem, AtmosphereController, wind audio,
    /// GameWorld roads) are hidden.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-44)]
    public sealed class JourneyMenu : MonoBehaviour, IPlayerInputOverride
    {
        /// <summary>PlayerPrefs key of the settings blob.</summary>
        public const string PrefsKey = "MedievalZen.JourneySettings";

        private const float WeatherFadeSeconds = 5f;
        private const float PanelWidth = 580f;
        private const float Margin = 24f;
        private const float OpenSeconds = 0.25f;
        private const float RowHeight = 54f;
        private const float FastTravelFadeOut = 0.9f;
        private const float FastTravelHold = 0.6f;
        private const float FastTravelFadeIn = 1.3f;

        private static readonly WeatherOverride[] WeatherChoices =
        {
            WeatherOverride.Auto, WeatherOverride.Clear, WeatherOverride.Hazy, WeatherOverride.Misty, WeatherOverride.Overcast,
            WeatherOverride.LightRain,
        };

        private static readonly string[] PageNames = { "Time", "Sky", "View", "Where to?" };

        private static readonly Color ControlNormal = new Color(0.84f, 0.76f, 0.6f, 1f);
        private static readonly Color ControlHover = new Color(0.92f, 0.85f, 0.68f, 1f);
        private static readonly Color ControlPressed = new Color(0.72f, 0.62f, 0.46f, 1f);
        private static readonly Color ControlSelected = new Color(0.96f, 0.9f, 0.74f, 1f);
        private static readonly Color Rule = new Color(0.36f, 0.27f, 0.17f, 0.35f);

        // ------------------------------------------------------------------ scene references
        private HudController hud;
        private PlayerController controller;
        private PlayerCameraRig cameraRig;
        private PlayerTravel travel;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private DayCycle day;
        private WeatherSystem weather;
        private AtmosphereController atmosphere;
        private ProceduralWindAudio wind;
        private GameWorld world;
        private MedievalZen.Endless.EndlessWorld endless;
        private MedievalZen.Cinematic.CinematicDirector director;

        // ------------------------------------------------------------------ state
        private JourneySettings settings = new JourneySettings();
        private JourneySettings defaults = new JourneySettings();
        private float baselineTimeScale = 1f;
        private bool ready;
        private bool busy;
        private float panelAlpha;
        private int page;
        private bool gliding;
        private float glideFrom;
        private float glideTo;
        private float glideT;
        private bool quitArmed;
        private float quitArmedUntil;
        private GameObject lastSelected;
        private int lastMinute = -1;
        private int lastDay = -1;
        private string lastWeatherLine;
        private float nextSlowRefresh;

        // ------------------------------------------------------------------ UI
        private RectTransform panel;
        private CanvasGroup panelGroup;
        private Text statusText;
        private readonly Button[] tabButtons = new Button[4];
        private readonly Text[] tabLabels = new Text[4];
        private readonly Image[] tabUnderlines = new Image[4];
        private readonly RectTransform[] pages = new RectTransform[4];
        private readonly bool[] pageAvailable = { true, true, true, true };
        private readonly List<List<Selectable>> pageRows = new List<List<Selectable>>
        {
            new List<Selectable>(), new List<Selectable>(), new List<Selectable>(), new List<Selectable>(),
        };

        private Button resumeButton;
        private Button resetButton;
        private Button quitButton;
        private Text quitLabel;
        private Image focusFrame;

        private JourneySlider timeSlider;
        private Text timeValue;
        private Text dayInfo;
        private Text dayLengthInfo;
        private JourneyStepper flowStepper;
        private readonly Button[] quickTimeButtons = new Button[4];

        private GameObject weatherHeader;
        private JourneyStepper weatherStepper;
        private Text weatherNow;
        private GameObject atmosphereHeader;
        private JourneySlider fogSlider;
        private JourneySlider hazeSlider;
        private JourneySlider cloudSlider;
        private Text fogValue;
        private Text hazeValue;
        private Text cloudValue;
        private GameObject soundHeader;
        private JourneyStepper windStepper;
        private JourneySlider windSlider;
        private Text windValue;

        private JourneyStepper cameraStepper;
        private JourneySlider lookSlider;
        private Text lookValue;
        private JourneyStepper invertStepper;
        private JourneyStepper hudStepper;
        private GameObject cinematicHeader;
        private JourneyStepper cinematicTravelStepper;
        private JourneyStepper letterboxStepper;

        private ScrollRect travelScroll;
        private RectTransform travelContent;
        private Text travelInfo;
        private readonly List<TravelEntry> travelEntries = new List<TravelEntry>();
        private readonly List<RectTransform> travelHeaders = new List<RectTransform>();

        // "The road" page (endless road only; replaces "Where to?").
        private RectTransform roadPage;
        private Text roadRegionText;
        private Text roadWalkedText;
        private Text roadSeedText;
        private Text nextInnName;
        private Text nextInnDetail;
        private Text previousInnName;
        private Text previousInnDetail;
        private Button nextInnGo;
        private Button previousInnGo;
        private Button newJourneyButton;
        private Text newJourneyLabel;
        private bool newJourneyArmed;
        private float newJourneyArmedUntil;
        private float nextRoadRefresh;
        private MedievalZen.Endless.Waypoint shownNextInn;
        private MedievalZen.Endless.Waypoint shownPreviousInn;

        /// <summary>True while the panel is open (it also fades out for a moment after closing).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The settings currently applied.</summary>
        public JourneySettings Settings => settings;

        /// <summary>The values the scene started with ("Reset to defaults").</summary>
        public JourneySettings Defaults => defaults;

        /// <inheritdoc />
        public float MoveSpeedScale => 0f;

        // ------------------------------------------------------------------ public API

        /// <summary>Opens the panel (no-op when another overlay holds the input). Returns true if it is open afterwards.</summary>
        public bool Open()
        {
            if (IsOpen) return true;
            if (!ready || busy || controller == null || !controller.isActiveAndEnabled) return false;
            if (controller.InputOverride != null && !ReferenceEquals(controller.InputOverride, this)) return false;

            IsOpen = true;
            controller.InputOverride = this;
            if (cameraRig != null && cameraRig.CursorLocked) cameraRig.SetCursorLocked(false);
            if (hud != null) hud.HidePrompt();
            panel.gameObject.SetActive(true);
            panelGroup.interactable = true;
            panelGroup.blocksRaycasts = true;
            EnsureEventSystem();
            SyncControls();
            RefreshTravel();
            RefreshRoad(true);
            ShowPage(pageAvailable[page] ? page : FirstAvailablePage(), true);
            return true;
        }

        /// <summary>Closes the panel, gives input back (cursor locked) and saves the settings.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            DisarmQuit();
            if (controller != null && ReferenceEquals(controller.InputOverride, this)) controller.InputOverride = null;
            if (cameraRig != null && !busy) cameraRig.SetCursorLocked(true);
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
            EventSystem events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null && events.currentSelectedGameObject.transform.IsChildOf(panel))
            {
                events.SetSelectedGameObject(null);
            }

            lastSelected = null;
            Save();
        }

        /// <summary>Creates an EventSystem with the Input System UI module if the scene has none.</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem (runtime)", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Debug.Log("JourneyMenu: the scene had no EventSystem; created one with the Input System UI module.");
        }

        // ------------------------------------------------------------------ input override

        /// <inheritdoc />
        public bool OnLook(Vector2 input, bool isPointerDelta) => true;

        /// <inheritdoc />
        public bool OnMove(Vector2 move) => true;

        /// <inheritdoc />
        public bool OnInteract() => true;

        /// <inheritdoc />
        public bool OnMap() => true;

        /// <inheritdoc />
        public bool OnAutoTravel() => true;

        /// <inheritdoc />
        public bool OnPause()
        {
            Close();
            return true;
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Builds the (hidden) panel under <paramref name="root"/>; called by the HUD while it builds itself.</summary>
        public void Build(HudController owner, Transform root)
        {
            hud = owner;
            if (panel != null) return;
            BuildPanel(root);
            panel.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (panel == null)
            {
                enabled = false;
                return;
            }

            controller = FindFirstObjectByType<PlayerController>();
            if (controller == null)
            {
                enabled = false;   // no player: no menu
                return;
            }

            cameraRig = controller.GetComponent<PlayerCameraRig>();
            travel = controller.GetComponent<PlayerTravel>();
            motor = controller.GetComponent<PlayerMotor>();
            interactor = controller.GetComponent<PlayerInteractor>();
            director = controller.GetComponent<MedievalZen.Cinematic.CinematicDirector>();
            day = DayCycle.Current;
            weather = WeatherSystem.Current;
            atmosphere = AtmosphereController.Current;
            wind = FindFirstObjectByType<ProceduralWindAudio>();
            world = GameWorld.Current;
            endless = MedievalZen.Endless.EndlessWorld.Current;

            CaptureDefaults();
            ConfigureSections();
            BuildTravelList();
            Load();
            controller.PauseToggled += OnPauseToggled;
            ready = true;
        }

        private void OnDestroy()
        {
            if (controller != null) controller.PauseToggled -= OnPauseToggled;
        }

        private void OnDisable()
        {
            if (IsOpen) Close();
        }

        private void OnPauseToggled(bool paused)
        {
            if (paused) Open();
        }

        private void Update()
        {
            if (gliding) StepGlide();
            if (!IsOpen && panelAlpha <= 0f) return;   // closed: nothing to do, nothing allocated

            float step = Time.unscaledDeltaTime / OpenSeconds;
            panelAlpha = Mathf.MoveTowards(panelAlpha, IsOpen ? 1f : 0f, step);
            float eased = Mathf.SmoothStep(0f, 1f, panelAlpha);
            panelGroup.alpha = eased;
            panel.anchoredPosition = new Vector2(-Margin + (1f - eased) * 40f, 0f);
            if (!IsOpen)
            {
                if (panelAlpha <= 0f) panel.gameObject.SetActive(false);
                return;
            }

            HandlePageKeys();
            UpdateFocus();
            RefreshLive();
            if (page == 3) RefreshRoad(false);
            if (quitArmed && Time.unscaledTime > quitArmedUntil) DisarmQuit();
            if (newJourneyArmed && Time.unscaledTime > newJourneyArmedUntil) DisarmNewJourney();
        }

        // ------------------------------------------------------------------ defaults, persistence, applying

        private void CaptureDefaults()
        {
            baselineTimeScale = day != null ? day.TimeScale : 1f;
            defaults = new JourneySettings
            {
                Flow = JourneyTime.NearestFlow(baselineTimeScale),
                Weather = weather != null && weather.ForcedState.HasValue ? (WeatherOverride)(int)weather.ForcedState.Value : WeatherOverride.Auto,
                FogAmount = atmosphere != null ? atmosphere.FogAmount : 1f,
                HazeDistance = atmosphere != null ? atmosphere.HazeDistance : 1f,
                CloudSoftness = atmosphere != null ? atmosphere.CloudSoftness : 1f,
                WindSound = wind != null && wind.PlayWind,
                WindVolume = wind != null ? wind.Volume : 0.06f,
                Camera = cameraRig != null ? cameraRig.Mode : CameraMode.ThirdPerson,
                LookSpeed = cameraRig != null ? cameraRig.LookSensitivityScale : 1f,
                InvertY = cameraRig != null && cameraRig.InvertY,
                ShowHud = hud == null || hud.ShowHud,
                CinematicCamera = director != null && director.CinematicCamera,
                CinematicWhenTravelling = director == null || director.CinematicWhenTravelling,
                Letterbox = director == null || director.Letterbox,
                HudAlways = hud != null && hud.HudAlways,
            }.Clamp();
            settings = defaults.Clone();
        }

        private void Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            if (!JourneySettings.TryFromJson(json, defaults, out JourneySettings loaded))
            {
                Debug.Log("JourneyMenu: saved settings were unreadable or from a newer version; using the scene's defaults.");
                return;
            }

            settings = loaded;
            ApplyAll(true);
        }

        private void Save()
        {
            if (!ready) return;
            if (cameraRig != null) settings.Camera = cameraRig.Mode;   // V may have changed it outside the menu
            if (settings.SameAs(defaults)) PlayerPrefs.DeleteKey(PrefsKey);
            else PlayerPrefs.SetString(PrefsKey, settings.ToJson());
            PlayerPrefs.Save();
        }

        /// <summary>Pushes every setting into the scene. <paramref name="atStart"/> snaps the weather and the camera.</summary>
        private void ApplyAll(bool atStart)
        {
            if (day != null) day.TimeScale = ScaleFor(settings.Flow);
            ApplyWeather(atStart);
            if (atmosphere != null)
            {
                atmosphere.FogAmount = settings.FogAmount;
                atmosphere.HazeDistance = settings.HazeDistance;
                atmosphere.CloudSoftness = settings.CloudSoftness;
            }

            if (wind != null)
            {
                wind.PlayWind = settings.WindSound;
                wind.Volume = settings.WindVolume;
            }

            if (cameraRig != null)
            {
                cameraRig.SetMode(settings.Camera, atStart);
                cameraRig.LookSensitivityScale = settings.LookSpeed;
                cameraRig.InvertY = settings.InvertY;
            }

            if (hud != null)
            {
                hud.ShowHud = settings.ShowHud;
                hud.HudAlways = settings.HudAlways;
            }

            if (director != null)
            {
                director.CinematicCamera = settings.CinematicCamera;
                director.CinematicWhenTravelling = settings.CinematicWhenTravelling;
                director.Letterbox = settings.Letterbox;
            }
        }

        /// <summary>Index of the camera choice: through your eyes, over the shoulder, cinematic.</summary>
        private int CameraIndex() => settings.CinematicCamera ? 2 : settings.Camera == CameraMode.FirstPerson ? 0 : 1;

        /// <summary>Index of the HUD choice: shown, hidden, always.</summary>
        private int HudIndex() => !settings.ShowHud ? 1 : settings.HudAlways ? 2 : 0;

        /// <summary>The scene's own time scale for its default choice (it may not be one of the five), else the choice's.</summary>
        private float ScaleFor(FlowOfTime flow) => flow == defaults.Flow ? baselineTimeScale : JourneyTime.FlowScale(flow);

        private void ApplyWeather(bool snap)
        {
            if (weather == null) return;
            WeatherState? state = settings.Weather == WeatherOverride.Auto ? (WeatherState?)null : (WeatherState)(int)settings.Weather;
            if (weather.ForcedState == state) return;
            if (snap)
            {
                weather.Force(state);
                weather.SkipCrossFade();
            }
            else
            {
                weather.Force(state, WeatherFadeSeconds);
            }
        }

        private void ResetToDefaults()
        {
            settings = defaults.Clone();
            ApplyAll(false);
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            SyncControls();
            if (hud != null) hud.ShowToast("Settings restored");
        }

        // ------------------------------------------------------------------ live refresh

        /// <summary>Puts every control on the current value without raising its change event.</summary>
        private void SyncControls()
        {
            if (day != null)
            {
                timeSlider.SetValueWithoutNotify(day.Hour);
                flowStepper.SetIndexWithoutNotify((int)settings.Flow);
                lastMinute = -1;
                lastDay = -1;
            }

            weatherStepper.SetIndexWithoutNotify(System.Array.IndexOf(WeatherChoices, settings.Weather));
            fogSlider.SetValueWithoutNotify(settings.FogAmount);
            hazeSlider.SetValueWithoutNotify(settings.HazeDistance);
            cloudSlider.SetValueWithoutNotify(settings.CloudSoftness);
            windStepper.SetIndexWithoutNotify(settings.WindSound ? 1 : 0);
            windSlider.SetValueWithoutNotify(settings.WindVolume / JourneySettings.WindVolumeMax);
            if (cameraRig != null) settings.Camera = cameraRig.Mode;
            cameraStepper.SetIndexWithoutNotify(CameraIndex());
            lookSlider.SetValueWithoutNotify(settings.LookSpeed);
            invertStepper.SetIndexWithoutNotify(settings.InvertY ? 1 : 0);
            hudStepper.SetIndexWithoutNotify(HudIndex());
            cinematicTravelStepper.SetIndexWithoutNotify(settings.CinematicWhenTravelling ? 0 : 1);
            letterboxStepper.SetIndexWithoutNotify(settings.Letterbox ? 0 : 1);
            UpdateValueTexts();
            UpdateDayLengthText();
            if (day == null) statusText.text = "The road is quiet";
            lastWeatherLine = null;
            RefreshLive();
        }

        private void UpdateValueTexts()
        {
            fogValue.text = Percent(settings.FogAmount);
            hazeValue.text = Percent(settings.HazeDistance);
            cloudValue.text = Percent(settings.CloudSoftness);
            windValue.text = Percent(settings.WindVolume / JourneySettings.WindVolumeMax);
            lookValue.text = $"×{settings.LookSpeed:0.00}";
            windSlider.interactable = settings.WindSound;
        }

        private void UpdateDayLengthText()
        {
            if (day == null) return;
            WorldSettings ws = world != null ? world.Settings : null;
            float length = ws != null ? ws.dayLengthMinutes : day.Clock.DayLengthMinutes;
            dayLengthInfo.text = JourneyTime.DayLengthText(length, day.TimeScale);
        }

        /// <summary>Values that change by themselves while the panel is open (clock, weather, camera toggled with V).</summary>
        private void RefreshLive()
        {
            if (day != null)
            {
                bool handsOff = !timeSlider.IsDragging && Time.unscaledTime - timeSlider.LastTouched > 0.4f;
                if (handsOff && Mathf.Abs(timeSlider.value - day.Hour) > 1e-3f) timeSlider.SetValueWithoutNotify(day.Hour);
                int minute = DayClock.MinuteOfDay(day.Hour);
                if (minute != lastMinute || day.Day != lastDay)
                {
                    lastMinute = minute;
                    lastDay = day.Day;
                    string clock = JourneyTime.FormatHour(day.Hour);
                    timeValue.text = clock;
                    dayInfo.text = $"Day {day.Day}";
                    statusText.text = weather != null
                        ? $"Day {day.Day}   ·   {clock}   ·   {JourneyTime.WeatherName(weather.State)}"
                        : $"Day {day.Day}   ·   {clock}";
                }
            }

            if (Time.unscaledTime < nextSlowRefresh) return;
            nextSlowRefresh = Time.unscaledTime + 0.25f;

            if (weather != null)
            {
                string line = weather.IsChanging
                    ? $"The sky is turning {JourneyTime.WeatherName(weather.State).ToLowerInvariant()}…"
                    : $"The sky now: {JourneyTime.WeatherName(weather.State).ToLowerInvariant()}";
                if (line != lastWeatherLine)
                {
                    lastWeatherLine = line;
                    weatherNow.text = line;
                    lastMinute = -1;   // refresh the status line's weather word too
                }
            }

            if (cameraRig != null && cameraRig.Mode != settings.Camera)
            {
                settings.Camera = cameraRig.Mode;
                cameraStepper.SetIndexWithoutNotify(CameraIndex());
            }
        }

        // ------------------------------------------------------------------ control callbacks

        private void OnTimeSlider(float value)
        {
            if (day == null) return;
            gliding = false;
            day.SetHourOfDay(JourneyTime.SliderHour(value));
            if (weather != null) weather.EaseTimeJump(WeatherFadeSeconds);
        }

        private void GlideTo(float hour)
        {
            if (day == null) return;
            glideFrom = day.Hour;
            glideTo = hour;
            glideT = 0f;
            gliding = true;
        }

        private void StepGlide()
        {
            if (day == null)
            {
                gliding = false;
                return;
            }

            glideT += Time.unscaledDeltaTime / JourneyTime.GlideSeconds;
            day.SetHourOfDay(JourneyTime.GlideHour(glideFrom, glideTo, glideT));
            if (weather != null) weather.EaseTimeJump(WeatherFadeSeconds);
            if (glideT >= 1f) gliding = false;
        }

        private void OnFlow(int index)
        {
            settings.Flow = (FlowOfTime)index;
            if (day != null) day.TimeScale = ScaleFor(settings.Flow);
            UpdateDayLengthText();
        }

        private void OnWeather(int index)
        {
            settings.Weather = WeatherChoices[Mathf.Clamp(index, 0, WeatherChoices.Length - 1)];
            ApplyWeather(false);
            nextSlowRefresh = 0f;
        }

        private void OnFog(float value)
        {
            settings.FogAmount = value;
            if (atmosphere != null) atmosphere.FogAmount = value;
            UpdateValueTexts();
        }

        private void OnHaze(float value)
        {
            settings.HazeDistance = value;
            if (atmosphere != null) atmosphere.HazeDistance = value;
            UpdateValueTexts();
        }

        private void OnClouds(float value)
        {
            settings.CloudSoftness = value;
            if (atmosphere != null) atmosphere.CloudSoftness = value;
            UpdateValueTexts();
        }

        private void OnWindSound(int index)
        {
            settings.WindSound = index == 1;
            if (wind != null) wind.PlayWind = settings.WindSound;
            UpdateValueTexts();
        }

        private void OnWindVolume(float value)
        {
            settings.WindVolume = value * JourneySettings.WindVolumeMax;
            if (wind != null) wind.Volume = settings.WindVolume;
            UpdateValueTexts();
        }

        private void OnCamera(int index)
        {
            settings.CinematicCamera = index == 2;
            if (director != null) director.CinematicCamera = settings.CinematicCamera;
            if (index == 2) return;   // the base mode stays what it was; the cinematic camera hands back to it
            settings.Camera = index == 0 ? CameraMode.FirstPerson : CameraMode.ThirdPerson;
            if (cameraRig != null) cameraRig.SetMode(settings.Camera);
        }

        private void OnCinematicTravel(int index)
        {
            settings.CinematicWhenTravelling = index == 0;
            if (director != null) director.CinematicWhenTravelling = settings.CinematicWhenTravelling;
        }

        private void OnLetterbox(int index)
        {
            settings.Letterbox = index == 0;
            if (director != null) director.Letterbox = settings.Letterbox;
        }

        private void OnLookSpeed(float value)
        {
            settings.LookSpeed = Mathf.Round(value * 20f) / 20f;
            if (cameraRig != null) cameraRig.LookSensitivityScale = settings.LookSpeed;
            UpdateValueTexts();
        }

        private void OnInvert(int index)
        {
            settings.InvertY = index == 1;
            if (cameraRig != null) cameraRig.InvertY = settings.InvertY;
        }

        private void OnHud(int index)
        {
            settings.ShowHud = index != 1;
            settings.HudAlways = index == 2;
            if (hud != null)
            {
                hud.ShowHud = settings.ShowHud;
                hud.HudAlways = settings.HudAlways;
            }
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            if (hud != null) hud.ShowToast("Quit only leaves the game in a build");
            Close();
#else
            if (!quitArmed)
            {
                quitArmed = true;
                quitArmedUntil = Time.unscaledTime + 4f;
                quitLabel.text = "Press again to leave";
                return;
            }

            Save();
            Application.Quit();
#endif
        }

        private void DisarmQuit()
        {
            quitArmed = false;
            if (quitLabel != null) quitLabel.text = "Quit";
        }

        // ------------------------------------------------------------------ pages and navigation

        private int FirstAvailablePage()
        {
            for (int i = 0; i < pageAvailable.Length; i++)
            {
                if (pageAvailable[i]) return i;
            }

            return 0;
        }

        private void ShowPage(int index, bool selectFirst)
        {
            page = Mathf.Clamp(index, 0, pages.Length - 1);
            for (int i = 0; i < pages.Length; i++)
            {
                bool active = i == page;
                pages[i].gameObject.SetActive(active);
                tabLabels[i].color = active ? UiKit.Accent : UiKit.FadedInk;
                tabLabels[i].fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                tabUnderlines[i].enabled = active;
            }

            if (page == 3)
            {
                RefreshTravel();
                RefreshRoad(true);
            }

            RebuildNavigation();
            if (!selectFirst) return;
            List<Selectable> rows = pageRows[page];
            Selectable first = null;
            for (int i = 0; i < rows.Count && first == null; i++)
            {
                if (rows[i] != null && rows[i].gameObject.activeInHierarchy) first = rows[i];
            }

            Select(first != null ? first : resumeButton);
        }

        private void TurnPage(int direction)
        {
            int next = page;
            for (int k = 0; k < pages.Length; k++)
            {
                next = (next + direction + pages.Length) % pages.Length;
                if (pageAvailable[next]) break;
            }

            if (next != page) ShowPage(next, true);
        }

        private void HandlePageKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                TurnPage(keyboard.shiftKey.isPressed ? -1 : 1);
            }

            Gamepad pad = Gamepad.current;
            if (pad == null) return;
            if (pad.rightShoulder.wasPressedThisFrame) TurnPage(1);
            if (pad.leftShoulder.wasPressedThisFrame) TurnPage(-1);
            if (pad.buttonEast.wasPressedThisFrame)
            {
                if (quitArmed) DisarmQuit();
                else Close();
            }
        }

        private static void Select(Selectable selectable)
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;
            events.SetSelectedGameObject(selectable != null ? selectable.gameObject : null);
        }

        /// <summary>
        /// Explicit navigation: tabs, then the page's rows top to bottom, then the footer. Up/down keeps the column (the
        /// rightmost stays rightmost); left/right moves within a row, and single controls (sliders, choices) keep left/right
        /// for themselves.
        /// </summary>
        private void RebuildNavigation()
        {
            var rows = new List<Selectable[]>();
            var tabs = new List<Selectable>();
            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (pageAvailable[i]) tabs.Add(tabButtons[i]);
            }

            rows.Add(tabs.ToArray());
            if (page == 3 && endless != null)
            {
                if (nextInnGo.gameObject.activeInHierarchy) rows.Add(new Selectable[] { nextInnGo });
                if (previousInnGo.gameObject.activeInHierarchy) rows.Add(new Selectable[] { previousInnGo });
                rows.Add(new Selectable[] { newJourneyButton });
            }
            else if (page == 3)
            {
                foreach (TravelEntry entry in SortedTravelEntries())
                {
                    rows.Add(entry.SetButton != null ? new Selectable[] { entry.SetButton, entry.GoButton } : new Selectable[] { entry.GoButton });
                }
            }
            else
            {
                List<Selectable> list = pageRows[page];
                int i = 0;
                while (i < list.Count)
                {
                    // Quick-time buttons share one row.
                    if (list[i] == quickTimeButtons[0])
                    {
                        rows.Add(new Selectable[] { quickTimeButtons[0], quickTimeButtons[1], quickTimeButtons[2], quickTimeButtons[3] });
                        i += 4;
                        continue;
                    }

                    if (list[i] != null && list[i].gameObject.activeInHierarchy) rows.Add(new[] { list[i] });
                    i++;
                }
            }

            rows.Add(new Selectable[] { resumeButton, resetButton, quitButton });

            for (int r = 0; r < rows.Count; r++)
            {
                Selectable[] row = rows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    var nav = new Navigation { mode = Navigation.Mode.Explicit };
                    nav.selectOnUp = r > 0 ? Pick(rows[r - 1], c, row.Length) : null;
                    nav.selectOnDown = r < rows.Count - 1 ? Pick(rows[r + 1], c, row.Length) : null;
                    nav.selectOnLeft = c > 0 ? row[c - 1] : null;
                    nav.selectOnRight = c < row.Length - 1 ? row[c + 1] : null;
                    row[c].navigation = nav;
                }
            }
        }

        private static Selectable Pick(Selectable[] row, int column, int fromLength)
        {
            if (row.Length == 0) return null;
            if (column == fromLength - 1 && fromLength > 1) return row[row.Length - 1];
            return row[Mathf.Min(column, row.Length - 1)];
        }

        private void UpdateFocus()
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && !selected.transform.IsChildOf(panel)) selected = null;
            if (selected == lastSelected) return;
            lastSelected = selected;
            if (selected == null)
            {
                focusFrame.gameObject.SetActive(false);
                return;
            }

            var rect = focusFrame.rectTransform;
            rect.SetParent(selected.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-5f, -5f);
            rect.offsetMax = new Vector2(5f, 5f);
            rect.SetAsLastSibling();
            focusFrame.gameObject.SetActive(true);
            if (page == 3 && selected.transform.IsChildOf(travelContent)) KeepVisible((RectTransform)selected.transform.parent);
        }

        private void KeepVisible(RectTransform item)
        {
            if (travelScroll == null || item == null) return;
            Canvas.ForceUpdateCanvases();
            RectTransform viewport = travelScroll.viewport;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
            Rect view = viewport.rect;
            const float pad = 8f;
            Vector2 position = travelContent.anchoredPosition;
            if (bounds.max.y > view.yMax) position.y -= bounds.max.y - view.yMax + pad;
            else if (bounds.min.y < view.yMin) position.y += view.yMin - bounds.min.y + pad;
            else return;
            float maxY = Mathf.Max(0f, travelContent.rect.height - view.height);
            position.y = Mathf.Clamp(position.y, 0f, maxY);
            travelContent.anchoredPosition = position;
            travelScroll.velocity = Vector2.zero;
        }

        // ------------------------------------------------------------------ sections

        private void ConfigureSections()
        {
            bool hasDay = day != null;
            bool hasWeather = weather != null;
            bool hasAtmosphere = atmosphere != null;
            bool hasWind = wind != null;
            bool hasCamera = cameraRig != null;

            pageAvailable[0] = hasDay;

            weatherHeader.SetActive(hasWeather);
            weatherStepper.transform.parent.gameObject.SetActive(hasWeather);
            weatherNow.gameObject.SetActive(hasWeather);
            atmosphereHeader.SetActive(hasAtmosphere);
            fogSlider.transform.parent.gameObject.SetActive(hasAtmosphere);
            hazeSlider.transform.parent.gameObject.SetActive(hasAtmosphere);
            cloudSlider.transform.parent.gameObject.SetActive(hasAtmosphere);
            soundHeader.SetActive(hasWind);
            windStepper.transform.parent.gameObject.SetActive(hasWind);
            windSlider.transform.parent.gameObject.SetActive(hasWind);
            pageAvailable[1] = hasWeather || hasAtmosphere || hasWind;

            cameraStepper.transform.parent.gameObject.SetActive(hasCamera);
            lookSlider.transform.parent.gameObject.SetActive(hasCamera);
            invertStepper.transform.parent.gameObject.SetActive(hasCamera);
            bool hasCinematic = hasCamera && director != null;
            cinematicHeader.SetActive(hasCinematic);
            cinematicTravelStepper.transform.parent.gameObject.SetActive(hasCinematic);
            letterboxStepper.transform.parent.gameObject.SetActive(hasCinematic);
            pageAvailable[2] = true;   // the HUD toggle is always there

            RoadNetwork roads = world != null ? world.Roads : null;
            pageAvailable[3] = roads != null && roads.Segments.Count > 0 && motor != null;
            if (endless != null)
            {
                // The endless road: "Where to?" becomes "The road" (the inns either way, a new journey, the seed).
                pageAvailable[3] = motor != null;
                tabLabels[3].text = "The road";
                travelInfo.gameObject.SetActive(false);
                travelScroll.gameObject.SetActive(false);
                BuildRoadPage(pages[3]);
            }

            for (int i = 0; i < tabButtons.Length; i++) tabButtons[i].gameObject.SetActive(pageAvailable[i]);
            page = FirstAvailablePage();
        }

        // ------------------------------------------------------------------ travel

        private sealed class TravelEntry
        {
            public string Name;
            public Settlement Settlement;
            public Vector3 Position;
            public int Group;
            public string Kind;
            public float Distance;
            public RectTransform Row;
            public Text NameText;
            public Text DetailText;
            public Button SetButton;
            public Button GoButton;
        }

        private void BuildTravelList()
        {
            if (!pageAvailable[3] || endless != null) return;
            RoadNetwork roads = world.Roads;
            string[] groupNames = { "Towns", "Villages", "Inns", "Landmarks" };
            var groups = new List<TravelEntry>[4];
            for (int g = 0; g < 4; g++) groups[g] = new List<TravelEntry>();

            foreach (Settlement s in roads.Settlements)
            {
                int g = s.Type == SettlementType.Town ? 0 : s.Type == SettlementType.Village ? 1 : 2;
                groups[g].Add(new TravelEntry
                {
                    Name = s.Name, Settlement = s, Position = s.Position, Group = g,
                    Kind = s.Type == SettlementType.Town ? "town" : s.Type == SettlementType.Village ? "village" : "inn",
                });
            }

            IReadOnlyList<Landmark> landmarks = world.Landmarks;
            if (landmarks != null)
            {
                for (int i = 0; i < landmarks.Count; i++)
                {
                    Landmark l = landmarks[i];
                    if (string.IsNullOrEmpty(l.name)) continue;
                    groups[3].Add(new TravelEntry { Name = l.name, Position = l.position, Group = 3, Kind = KindLabel(l.kind.ToString()) });
                }
            }

            for (int g = 0; g < 4; g++)
            {
                if (groups[g].Count == 0) continue;
                RectTransform header = Header(travelContent, groupNames[g]).rectTransform;
                travelHeaders.Add(header);
                header.name = "Group" + g;
                foreach (TravelEntry entry in groups[g])
                {
                    BuildTravelRow(entry);
                    travelEntries.Add(entry);
                }
            }
        }

        /// <summary>"MountainPass" → "mountain pass" (landmark kinds are shown as the world names them).</summary>
        private static string KindLabel(string kind)
        {
            var sb = new System.Text.StringBuilder(kind.Length + 4);
            for (int i = 0; i < kind.Length; i++)
            {
                char c = kind[i];
                if (i > 0 && char.IsUpper(c)) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        private void BuildTravelRow(TravelEntry entry)
        {
            RectTransform row = Row(travelContent, 62f);
            entry.Row = row;
            entry.NameText = UiKit.CreateText("Name", row, 20, TextAnchor.LowerLeft, UiKit.Ink, FontStyle.Bold);
            Anchor(entry.NameText.rectTransform, 0f, 0.5f, 0.6f, 1f, 10f, 0f, 0f, -4f);
            entry.NameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            entry.DetailText = UiKit.CreateText("Detail", row, 16, TextAnchor.UpperLeft, UiKit.FadedInk, FontStyle.Italic);
            Anchor(entry.DetailText.rectTransform, 0f, 0f, 0.6f, 0.5f, 10f, 4f, 0f, 0f);

            entry.GoButton = MakeButton(row, "Go now", 16);
            Anchor((RectTransform)entry.GoButton.transform, 0.81f, 0.18f, 1f, 0.82f, 0f, 0f, -4f, 0f);
            TravelEntry captured = entry;
            entry.GoButton.onClick.AddListener(() => StartCoroutine(FastTravel(captured)));

            if (entry.Settlement != null)
            {
                entry.SetButton = MakeButton(row, "Set destination", 16);
                Anchor((RectTransform)entry.SetButton.transform, 0.6f, 0.18f, 0.795f, 0.82f, 0f, 0f, 0f, 0f);
                entry.SetButton.onClick.AddListener(() => SetDestination(captured));
            }
        }

        private IEnumerable<TravelEntry> SortedTravelEntries()
        {
            // Rows are kept in sibling order (grouped, nearest first) by RefreshTravel.
            var sorted = new List<TravelEntry>(travelEntries);
            sorted.Sort((a, b) => a.Row.GetSiblingIndex().CompareTo(b.Row.GetSiblingIndex()));
            return sorted;
        }

        /// <summary>Distances from the player, nearest first within each group, destination marked.</summary>
        private void RefreshTravel()
        {
            if (!pageAvailable[3] || travelEntries.Count == 0) return;
            Vector3 here = controller.transform.position;
            Settlement destination = travel != null ? travel.Destination : null;
            foreach (TravelEntry e in travelEntries)
            {
                e.Distance = new Vector2(e.Position.x - here.x, e.Position.z - here.z).magnitude;
                bool marked = e.Settlement != null && e.Settlement == destination;
                e.NameText.text = marked ? $"{e.Name}  <color=#8C2914>· your destination</color>" : e.Name;
                e.DetailText.text = $"{e.Kind}, {UiKit.FormatDistance(e.Distance)} away";
            }

            var ordered = new List<TravelEntry>(travelEntries);
            ordered.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : a.Distance.CompareTo(b.Distance));
            int sibling = 0;
            int group = -1;
            int header = -1;
            foreach (TravelEntry e in ordered)
            {
                if (e.Group != group)
                {
                    group = e.Group;
                    header++;
                    if (header < travelHeaders.Count) travelHeaders[header].SetSiblingIndex(sibling++);
                }

                e.Row.SetSiblingIndex(sibling++);
            }

            if (IsOpen && page == 3) RebuildNavigation();
        }

        private void SetDestination(TravelEntry entry)
        {
            if (travel == null || entry.Settlement == null) return;
            Settlement s = entry.Settlement;
            if (travel.Destination != s)
            {
                travel.SetDestination(s);
                if (hud != null) hud.ShowToast(travel.IsTravelling ? $"New destination: {s.Name}" : $"Destination: {s.Name}. Press T to set off.");
            }
            else if (hud != null)
            {
                hud.ShowToast(travel.IsTravelling ? $"On the way to {s.Name}" : $"{s.Name} is marked. Press T to set off.");
            }

            RefreshTravel();
        }

        private FastTravelGround GroundProbe()
        {
            var ground = new FastTravelGround { WaterHeight = world != null ? world.WaterHeight : -1f };
            Terrain terrain = world != null ? world.Terrain : null;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                ground.HeightAt = (x, z) =>
                {
                    if (x < origin.x || z < origin.z || x > origin.x + size.x || z > origin.z + size.z) return float.NaN;
                    return terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y;
                };
            }

            return ground;
        }

        private IEnumerator FastTravel(TravelEntry entry)
        {
            if (busy || motor == null || world == null) yield break;
            RoadNetwork roads = world.Roads;
            FastTravelGround ground = GroundProbe();
            bool found = entry.Settlement != null
                ? FastTravelPlanner.TryPlanSettlement(roads, entry.Settlement, ground, out FastTravelSpot spot)
                : FastTravelPlanner.TryPlanNear(roads, entry.Position, ground, out spot);
            if (!found)
            {
                if (hud != null) hud.ShowToast($"No road leads to {entry.Name}");
                yield break;
            }

            busy = true;
            Close();
            if (travel != null) travel.Cancel();
            bool controllerWasEnabled = controller.enabled;
            controller.enabled = false;
            if (interactor != null) interactor.Busy = true;
            if (cameraRig != null) cameraRig.SetCursorLocked(true);

            if (hud != null)
            {
                hud.HidePrompt();
                yield return hud.Fade(1f, FastTravelFadeOut);
            }

            motor.Teleport(spot.Position + Vector3.up * 0.1f, spot.Yaw);
            if (cameraRig != null) cameraRig.SnapYaw(spot.Yaw);
            yield return new WaitForSecondsRealtime(FastTravelHold);
            if (hud != null)
            {
                hud.ShowToast(entry.Settlement != null ? $"The road brings you to {entry.Name}" : $"You come to the road near {entry.Name}");
                yield return hud.Fade(0f, FastTravelFadeIn);
            }

            if (controllerWasEnabled && controller != null) controller.enabled = true;
            if (interactor != null) interactor.Busy = false;
            busy = false;
        }

        // ------------------------------------------------------------------ the road (endless road)

        private void BuildRoadPage(RectTransform area)
        {
            if (roadPage != null) return;
            roadPage = Stack(area);
            roadPage.name = "Road";
            Header(roadPage, "On the road");
            roadRegionText = Info(roadPage, string.Empty);
            roadRegionText.fontStyle = FontStyle.Bold;
            roadRegionText.color = UiKit.Ink;
            roadRegionText.fontSize = 19;
            roadWalkedText = Info(roadPage, string.Empty);

            Header(roadPage, "Inns");
            nextInnGo = InnRow(roadPage, out nextInnName, out nextInnDetail, () => GoToInn(shownNextInn));
            previousInnGo = InnRow(roadPage, out previousInnName, out previousInnDetail, () => GoToInn(shownPreviousInn));

            Header(roadPage, "A new road");
            RectTransform row = Row(roadPage, RowHeight);
            newJourneyButton = MakeButton(row, "New journey", 18);
            newJourneyLabel = newJourneyButton.GetComponentInChildren<Text>();
            Anchor((RectTransform)newJourneyButton.transform, 0f, 0.12f, 0.5f, 0.88f, 6f, 0f, 0f, 0f);
            newJourneyButton.onClick.AddListener(OnNewJourney);
            roadSeedText = UiKit.CreateText("Seed", row, 16, TextAnchor.MiddleLeft, UiKit.FadedInk, FontStyle.Italic);
            Anchor(roadSeedText.rectTransform, 0.52f, 0f, 1f, 1f, 6f, 0f, 0f, 0f);
            Info(roadPage, "A new journey starts a different road from its first inn.");
        }

        private Button InnRow(RectTransform parent, out Text name, out Text detail, UnityEngine.Events.UnityAction go)
        {
            RectTransform row = Row(parent, 62f);
            name = UiKit.CreateText("Name", row, 20, TextAnchor.LowerLeft, UiKit.Ink, FontStyle.Bold);
            Anchor(name.rectTransform, 0f, 0.5f, 0.78f, 1f, 10f, 0f, 0f, -4f);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            detail = UiKit.CreateText("Detail", row, 16, TextAnchor.UpperLeft, UiKit.FadedInk, FontStyle.Italic);
            Anchor(detail.rectTransform, 0f, 0f, 0.78f, 0.5f, 10f, 4f, 0f, 0f);
            Button button = MakeButton(row, "Go now", 16);
            Anchor((RectTransform)button.transform, 0.8f, 0.18f, 1f, 0.82f, 0f, 0f, -4f, 0f);
            button.onClick.AddListener(go);
            return button;
        }

        /// <summary>Region, distance, the inns either way and the seed; every half second while the page is open.</summary>
        private void RefreshRoad(bool force)
        {
            if (endless == null || roadPage == null) return;
            if (!force && Time.unscaledTime < nextRoadRefresh) return;
            nextRoadRefresh = Time.unscaledTime + 0.5f;
            roadRegionText.text = string.IsNullOrEmpty(endless.RegionName) ? "Setting out" : endless.RegionName;
            double s = endless.S;
            roadWalkedText.text = s >= 0.0 ? $"{s / 1000.0:0.0} km walked from where the journey began" : $"{-s / 1000.0:0.0} km behind where the journey began";
            roadSeedText.text = $"Seed {endless.Seed}";

            bool layoutChanged = SetInn(endless.NextInn, ref shownNextInn, nextInnName, nextInnDetail, nextInnGo, "ahead");
            layoutChanged |= SetInn(endless.PreviousInn, ref shownPreviousInn, previousInnName, previousInnDetail, previousInnGo, "back the way you came");
            if (layoutChanged && IsOpen && page == 3) RebuildNavigation();
        }

        private bool SetInn(MedievalZen.Endless.Waypoint inn, ref MedievalZen.Endless.Waypoint shown, Text name, Text detail, Button go, string where)
        {
            bool visible = inn != null;
            bool changed = go.transform.parent.gameObject.activeSelf != visible;
            go.transform.parent.gameObject.SetActive(visible);
            shown = inn;
            if (!visible) return changed;
            name.text = inn.Name;
            detail.text = $"inn, {UiKit.FormatDistance((float)endless.RoadDistanceTo(inn))} {where} by road";
            return changed;
        }

        private void GoToInn(MedievalZen.Endless.Waypoint inn)
        {
            if (busy || endless == null || inn == null || endless.IsBusy) return;
            Close();
            busy = true;
            if (interactor != null) interactor.Busy = true;
            if (cameraRig != null) cameraRig.SetCursorLocked(true);
            if (!endless.TravelTo(inn, EndRoadAction)) EndRoadAction();
        }

        private void OnNewJourney()
        {
            if (busy || endless == null || endless.IsBusy) return;
            if (!newJourneyArmed)
            {
                newJourneyArmed = true;
                newJourneyArmedUntil = Time.unscaledTime + 4f;
                newJourneyLabel.text = "Press again to begin";
                return;
            }

            DisarmNewJourney();
            Close();
            busy = true;
            if (interactor != null) interactor.Busy = true;
            if (cameraRig != null) cameraRig.SetCursorLocked(true);
            if (!endless.BeginNewJourney(EndRoadAction)) EndRoadAction();
        }

        private void DisarmNewJourney()
        {
            newJourneyArmed = false;
            if (newJourneyLabel != null) newJourneyLabel.text = "New journey";
        }

        private void EndRoadAction()
        {
            if (interactor != null) interactor.Busy = false;
            busy = false;
        }

        // ------------------------------------------------------------------ building

        private void BuildPanel(Transform root)
        {
            panel = UiKit.CreatePanel("JourneyMenu", root, new Color(UiKit.Parchment.r, UiKit.Parchment.g, UiKit.Parchment.b, 0.93f)).rectTransform;
            panel.anchorMin = new Vector2(1f, 0f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, -2f * Margin);
            panel.anchoredPosition = new Vector2(-Margin, 0f);
            panel.GetComponent<Image>().raycastTarget = true;   // clicks on the panel never fall through to the world
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            Text title = UiKit.CreateText("Title", panel, 32, TextAnchor.UpperLeft, UiKit.Ink, FontStyle.Bold);
            title.text = "Journey";
            Anchor(title.rectTransform, 0f, 1f, 1f, 1f, 28f, -60f, -28f, -18f);
            statusText = UiKit.CreateText("Status", panel, 18, TextAnchor.UpperLeft, UiKit.FadedInk, FontStyle.Italic);
            Anchor(statusText.rectTransform, 0f, 1f, 1f, 1f, 30f, -90f, -28f, -62f);

            // Tabs.
            RectTransform tabs = UiKit.CreateRect("Tabs", panel);
            Anchor(tabs, 0f, 1f, 1f, 1f, 20f, -146f, -20f, -102f);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                Button tab = MakeButton(tabs, PageNames[i], 20);
                tab.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.35f);
                Anchor((RectTransform)tab.transform, i * 0.25f, 0f, (i + 1) * 0.25f, 1f, 4f, 0f, -4f, 0f);
                tab.onClick.AddListener(() => ShowPage(index, false));
                tabButtons[i] = tab;
                tabLabels[i] = tab.GetComponentInChildren<Text>();
                Image underline = UiKit.CreateImage("Underline", tab.transform, null, UiKit.Accent, Vector2.zero);
                Anchor(underline.rectTransform, 0.2f, 0f, 0.8f, 0f, 0f, 3f, 0f, 6f);
                tabUnderlines[i] = underline;
            }

            Image rule = UiKit.CreateImage("Rule", panel, null, Rule, Vector2.zero);
            Anchor(rule.rectTransform, 0f, 1f, 1f, 1f, 24f, -156f, -24f, -154f);

            // Page area.
            for (int i = 0; i < 4; i++)
            {
                RectTransform area = UiKit.CreateRect("Page" + PageNames[i], panel);
                Anchor(area, 0f, 0f, 1f, 1f, 24f, 112f, -24f, -166f);
                pages[i] = area;
            }

            BuildTimePage(pages[0], pageRows[0]);
            BuildSkyPage(pages[1], pageRows[1]);
            BuildViewPage(pages[2], pageRows[2]);
            BuildTravelPage(pages[3]);

            // Footer.
            Image footerRule = UiKit.CreateImage("FooterRule", panel, null, Rule, Vector2.zero);
            Anchor(footerRule.rectTransform, 0f, 0f, 1f, 0f, 24f, 104f, -24f, 106f);
            RectTransform footer = UiKit.CreateRect("Footer", panel);
            Anchor(footer, 0f, 0f, 1f, 0f, 20f, 44f, -20f, 94f);
            resumeButton = MakeButton(footer, "Resume", 20);
            Anchor((RectTransform)resumeButton.transform, 0f, 0f, 0.34f, 1f, 4f, 0f, -4f, 0f);
            resumeButton.onClick.AddListener(Close);
            resetButton = MakeButton(footer, "Reset to defaults", 18);
            Anchor((RectTransform)resetButton.transform, 0.34f, 0f, 0.74f, 1f, 4f, 0f, -4f, 0f);
            resetButton.onClick.AddListener(ResetToDefaults);
            quitButton = MakeButton(footer, "Quit", 18);
            quitLabel = quitButton.GetComponentInChildren<Text>();
            Anchor((RectTransform)quitButton.transform, 0.74f, 0f, 1f, 1f, 4f, 0f, -4f, 0f);
            quitButton.onClick.AddListener(OnQuit);

            Text hint = UiKit.CreateText("Hint", panel, 15, TextAnchor.MiddleCenter, UiKit.FadedInk, FontStyle.Italic);
            hint.text = "Esc or Start to return   ·   Tab or bumpers to turn the page";
            Anchor(hint.rectTransform, 0f, 0f, 1f, 0f, 20f, 12f, -20f, 38f);

            focusFrame = UiKit.CreateImage("Focus", panel, UiKit.SoftFrame, UiKit.Accent, Vector2.zero);
            focusFrame.type = Image.Type.Sliced;
            focusFrame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            focusFrame.gameObject.SetActive(false);
        }

        private void BuildTimePage(RectTransform area, List<Selectable> rows)
        {
            RectTransform stack = Stack(area);
            Header(stack, "Time of day");

            RectTransform timeRow = Row(stack, RowHeight + 6f);
            timeValue = UiKit.CreateText("Clock", timeRow, 30, TextAnchor.MiddleLeft, UiKit.Ink, FontStyle.Bold);
            Anchor(timeValue.rectTransform, 0f, 0f, 0.22f, 1f, 6f, 0f, 0f, 0f);
            timeSlider = MakeSlider(timeRow, 0f, DayClock.HoursPerDay, 0.25f, 0.24f, 1f);
            timeSlider.onValueChanged.AddListener(OnTimeSlider);
            rows.Add(timeSlider);

            // Hour marks under the slider track (same horizontal span as the slider).
            RectTransform scale = Row(stack, 16f);
            RectTransform marks = UiKit.CreateRect("Marks", scale);
            Anchor(marks, 0.24f, 0f, 1f, 1f, 10f, 0f, -14f, 0f);
            for (int i = 0; i <= 4; i++)
            {
                Text mark = UiKit.CreateText("Mark", marks, 14, TextAnchor.MiddleCenter, UiKit.FadedInk);
                mark.text = JourneyTime.FormatHour(i * 6f);
                UiKit.Place(mark.rectTransform, new Vector2(i * 0.25f, 0.5f), Vector2.zero, new Vector2(50f, 16f));
                mark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }

            RectTransform quick = Row(stack, RowHeight);
            string[] quickNames = { "Dawn", "Noon", "Dusk", "Night" };
            float[] quickHours = { JourneyTime.DawnHour, JourneyTime.NoonHour, JourneyTime.DuskHour, JourneyTime.NightHour };
            for (int i = 0; i < 4; i++)
            {
                float hour = quickHours[i];
                Button b = MakeButton(quick, quickNames[i], 19);
                Anchor((RectTransform)b.transform, i * 0.25f, 0.1f, (i + 1) * 0.25f, 0.9f, 4f, 0f, -4f, 0f);
                b.onClick.AddListener(() => GlideTo(hour));
                quickTimeButtons[i] = b;
                rows.Add(b);
            }

            dayInfo = Info(stack, string.Empty);
            Header(stack, "Flow of time");
            flowStepper = StepperRow(stack, "Pace", FlowLabels(), false, rows);
            flowStepper.Changed += OnFlow;
            dayLengthInfo = Info(stack, string.Empty);
        }

        private void BuildSkyPage(RectTransform area, List<Selectable> rows)
        {
            RectTransform stack = Stack(area);
            weatherHeader = Header(stack, "Weather").gameObject;
            var weatherLabels = new string[WeatherChoices.Length];
            for (int i = 0; i < weatherLabels.Length; i++) weatherLabels[i] = JourneyTime.WeatherLabel(WeatherChoices[i]);
            weatherStepper = StepperRow(stack, "The sky", weatherLabels, false, rows);
            weatherStepper.Changed += OnWeather;
            weatherNow = Info(stack, string.Empty);

            atmosphereHeader = Header(stack, "Mist and haze").gameObject;
            fogSlider = SliderRow(stack, "Fog", 0f, JourneySettings.AtmosphereMax, 0.1f, out fogValue, rows);
            fogSlider.onValueChanged.AddListener(OnFog);
            hazeSlider = SliderRow(stack, "Seeing far", 0f, JourneySettings.AtmosphereMax, 0.1f, out hazeValue, rows);
            hazeSlider.onValueChanged.AddListener(OnHaze);
            cloudSlider = SliderRow(stack, "Soft clouds", 0f, JourneySettings.AtmosphereMax, 0.1f, out cloudValue, rows);
            cloudSlider.onValueChanged.AddListener(OnClouds);

            soundHeader = Header(stack, "Sound").gameObject;
            windStepper = StepperRow(stack, "Wind", new[] { "Silent", "Blowing" }, true, rows);
            windStepper.Changed += OnWindSound;
            windSlider = SliderRow(stack, "Wind volume", 0f, 1f, 0.05f, out windValue, rows);
            windSlider.onValueChanged.AddListener(OnWindVolume);
        }

        private void BuildViewPage(RectTransform area, List<Selectable> rows)
        {
            RectTransform stack = Stack(area);
            Header(stack, "Your eyes");
            cameraStepper = StepperRow(stack, "Camera", new[] { "Through your eyes", "Over the shoulder", "Cinematic" }, true, rows);
            cameraStepper.Changed += OnCamera;
            lookSlider = SliderRow(stack, "Look speed", JourneySettings.LookSpeedMin, JourneySettings.LookSpeedMax, 0.05f, out lookValue, rows);
            lookSlider.onValueChanged.AddListener(OnLookSpeed);
            invertStepper = StepperRow(stack, "Look up/down", new[] { "Normal", "Inverted" }, true, rows);
            invertStepper.Changed += OnInvert;
            cinematicHeader = Header(stack, "Cinematic camera").gameObject;
            cinematicTravelStepper = StepperRow(stack, "Cinematic when travelling", new[] { "On", "Off" }, true, rows);
            cinematicTravelStepper.Changed += OnCinematicTravel;
            letterboxStepper = StepperRow(stack, "Letterbox", new[] { "On", "Off" }, true, rows);
            letterboxStepper.Changed += OnLetterbox;
            Header(stack, "On screen");
            hudStepper = StepperRow(stack, "Clock and destination", new[] { "Shown", "Hidden", "Always" }, true, rows);
            hudStepper.Changed += OnHud;
            Info(stack, "Prompts and messages always show. \"Always\" keeps the clock in cinematic shots too.");
        }

        private void BuildTravelPage(RectTransform area)
        {
            travelInfo = UiKit.CreateText("Info", area, 16, TextAnchor.UpperLeft, UiKit.FadedInk, FontStyle.Italic);
            travelInfo.text = "Where to? Distances are as the crow flies.";
            Anchor(travelInfo.rectTransform, 0f, 1f, 1f, 1f, 6f, -26f, 0f, 0f);

            var scrollObject = UiKit.CreateRect("Scroll", area);
            Anchor(scrollObject, 0f, 0f, 1f, 1f, 0f, 0f, 0f, -32f);
            travelScroll = scrollObject.gameObject.AddComponent<ScrollRect>();
            travelScroll.horizontal = false;
            travelScroll.vertical = true;
            travelScroll.movementType = ScrollRect.MovementType.Clamped;
            travelScroll.scrollSensitivity = 32f;
            travelScroll.inertia = true;
            travelScroll.decelerationRate = 0.12f;

            RectTransform viewport = UiKit.CreateRect("Viewport", scrollObject);
            Anchor(viewport, 0f, 0f, 1f, 1f, 0f, 0f, -16f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();   // wheel/drag target over the gaps between rows
            hit.color = new Color(1f, 1f, 1f, 0f);
            travelScroll.viewport = viewport;

            travelContent = UiKit.CreateRect("Content", viewport);
            travelContent.anchorMin = new Vector2(0f, 1f);
            travelContent.anchorMax = new Vector2(1f, 1f);
            travelContent.pivot = new Vector2(0.5f, 1f);
            travelContent.sizeDelta = Vector2.zero;
            var layout = travelContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(2, 8, 4, 12);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            travelContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            travelScroll.content = travelContent;

            RectTransform barRect = UiKit.CreateRect("Scrollbar", scrollObject);
            Anchor(barRect, 1f, 0f, 1f, 1f, -8f, 4f, 0f, -4f);
            Image track = barRect.gameObject.AddComponent<Image>();
            track.sprite = UiKit.SoftRect;
            track.type = Image.Type.Sliced;
            track.color = new Color(UiKit.FadedInk.r, UiKit.FadedInk.g, UiKit.FadedInk.b, 0.18f);
            RectTransform slide = UiKit.CreateRect("Sliding", barRect);
            UiKit.Stretch(slide);
            Image handle = UiKit.CreateImage("Handle", slide, UiKit.SoftRect, new Color(UiKit.FadedInk.r, UiKit.FadedInk.g, UiKit.FadedInk.b, 0.6f), Vector2.zero);
            handle.type = Image.Type.Sliced;
            handle.raycastTarget = true;
            UiKit.Stretch(handle.rectTransform);
            var bar = barRect.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };
            travelScroll.verticalScrollbar = bar;
            travelScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private static string[] FlowLabels()
        {
            var labels = new string[JourneyTime.FlowCount];
            for (int i = 0; i < labels.Length; i++) labels[i] = JourneyTime.FlowLabel((FlowOfTime)i);
            return labels;
        }

        // ------------------------------------------------------------------ widget factory

        private static RectTransform Stack(RectTransform area)
        {
            RectTransform stack = UiKit.CreateRect("Stack", area);
            stack.anchorMin = new Vector2(0f, 1f);
            stack.anchorMax = new Vector2(1f, 1f);
            stack.pivot = new Vector2(0.5f, 1f);
            stack.sizeDelta = Vector2.zero;
            var layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            stack.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return stack;
        }

        private static RectTransform Row(Transform parent, float height)
        {
            RectTransform row = UiKit.CreateRect("Row", parent);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            return row;
        }

        private static Text Header(Transform parent, string text)
        {
            Text header = UiKit.CreateText("Header", parent, 21, TextAnchor.LowerLeft, UiKit.Accent, FontStyle.Bold);
            header.text = text;
            var element = header.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 38f;
            element.minHeight = 38f;
            return header;
        }

        private static Text Info(Transform parent, string text)
        {
            Text info = UiKit.CreateText("Info", parent, 16, TextAnchor.MiddleLeft, UiKit.FadedInk, FontStyle.Italic);
            info.text = text;
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            var element = info.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 26f;
            element.minHeight = 26f;
            return info;
        }

        private static Text RowLabel(RectTransform row, string text)
        {
            Text label = UiKit.CreateText("Label", row, 19, TextAnchor.MiddleLeft, UiKit.Ink);
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;   // long labels take two lines instead of running under the control
            label.lineSpacing = 0.9f;
            Anchor(label.rectTransform, 0f, 0f, 0.34f, 1f, 6f, 0f, 0f, 0f);
            return label;
        }

        private JourneyStepper StepperRow(Transform parent, string label, string[] options, bool wrap, List<Selectable> rows)
        {
            RectTransform row = Row(parent, RowHeight);
            RowLabel(row, label);
            RectTransform box = UiKit.CreateRect("Choice", row);
            Anchor(box, 0.34f, 0.12f, 1f, 0.88f, 0f, 0f, -4f, 0f);
            Image background = box.gameObject.AddComponent<Image>();
            background.sprite = UiKit.SoftRect;
            background.type = Image.Type.Sliced;
            var stepper = box.gameObject.AddComponent<JourneyStepper>();
            Style(stepper, background);
            Text text = UiKit.CreateText("Value", box, 19, TextAnchor.MiddleCenter, UiKit.Ink);
            UiKit.Stretch(text.rectTransform);
            stepper.Label = text;
            stepper.Options = options;
            stepper.Wrap = wrap;
            stepper.SetIndexWithoutNotify(0);
            rows.Add(stepper);
            return stepper;
        }

        private JourneySlider SliderRow(Transform parent, string label, float min, float max, float keyStep, out Text value, List<Selectable> rows)
        {
            RectTransform row = Row(parent, RowHeight);
            RowLabel(row, label);
            JourneySlider slider = MakeSlider(row, min, max, keyStep, 0.34f, 0.82f);
            value = UiKit.CreateText("Value", row, 18, TextAnchor.MiddleRight, UiKit.FadedInk);
            Anchor(value.rectTransform, 0.82f, 0f, 1f, 1f, 0f, 0f, -6f, 0f);
            rows.Add(slider);
            return slider;
        }

        private static JourneySlider MakeSlider(RectTransform row, float min, float max, float keyStep, float x0, float x1)
        {
            RectTransform root = UiKit.CreateRect("Slider", row);
            Anchor(root, x0, 0.15f, x1, 0.85f, 10f, 0f, -14f, 0f);
            Image hit = root.gameObject.AddComponent<Image>();   // whole row height is grabbable
            hit.color = new Color(1f, 1f, 1f, 0f);

            Image track = UiKit.CreateImage("Track", root, UiKit.SoftRect, new Color(UiKit.FadedInk.r, UiKit.FadedInk.g, UiKit.FadedInk.b, 0.3f), Vector2.zero);
            track.type = Image.Type.Sliced;
            Anchor(track.rectTransform, 0f, 0.5f, 1f, 0.5f, 0f, -4f, 0f, 4f);

            RectTransform fillArea = UiKit.CreateRect("Fill Area", root);
            Anchor(fillArea, 0f, 0.5f, 1f, 0.5f, 0f, -4f, 0f, 4f);
            Image fill = UiKit.CreateImage("Fill", fillArea, UiKit.SoftRect, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.75f), Vector2.zero);
            fill.type = Image.Type.Sliced;
            fill.rectTransform.sizeDelta = Vector2.zero;

            RectTransform handleArea = UiKit.CreateRect("Handle Slide Area", root);
            UiKit.Stretch(handleArea);
            Image handle = UiKit.CreateImage("Handle", handleArea, UiKit.Circle, Color.white, new Vector2(26f, 26f));
            handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            handle.raycastTarget = true;

            var slider = root.gameObject.AddComponent<JourneySlider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.KeyStep = keyStep;
            Style(slider, handle);
            var colors = slider.colors;
            colors.normalColor = new Color(0.55f, 0.16f, 0.08f, 1f);
            colors.highlightedColor = new Color(0.68f, 0.24f, 0.12f, 1f);
            colors.selectedColor = new Color(0.72f, 0.28f, 0.14f, 1f);
            colors.pressedColor = new Color(0.42f, 0.12f, 0.06f, 1f);
            slider.colors = colors;
            return slider;
        }

        private static Button MakeButton(Transform parent, string text, int size)
        {
            RectTransform rect = UiKit.CreateRect(text, parent);
            Image background = rect.gameObject.AddComponent<Image>();
            background.sprite = UiKit.SoftRect;
            background.type = Image.Type.Sliced;
            var button = rect.gameObject.AddComponent<Button>();
            Style(button, background);
            Text label = UiKit.CreateText("Label", rect, size, TextAnchor.MiddleCenter, UiKit.Ink);
            label.text = text;
            UiKit.Stretch(label.rectTransform);
            return button;
        }

        private static void Style(Selectable selectable, Graphic target)
        {
            target.color = Color.white;
            target.raycastTarget = true;
            selectable.targetGraphic = target;
            selectable.transition = Selectable.Transition.ColorTint;
            selectable.colors = new ColorBlock
            {
                normalColor = ControlNormal,
                highlightedColor = ControlHover,
                pressedColor = ControlPressed,
                selectedColor = ControlSelected,
                disabledColor = new Color(0.8f, 0.78f, 0.74f, 0.45f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };
        }

        /// <summary>Anchors (x0, y0)–(x1, y1) of the parent with pixel offsets for the min and max corners.</summary>
        private static void Anchor(RectTransform rect, float x0, float y0, float x1, float y1, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";
    }
}
