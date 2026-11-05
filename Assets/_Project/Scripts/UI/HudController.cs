using System.Collections;
using System.Collections.Generic;
using System.Text;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedievalZen.UI
{
    /// <summary>
    /// The small, parchment-tinted screen HUD, built in code on its Canvas at Awake: time of day and day count (top left),
    /// destination + remaining road distance and mode chips (top right), a contextual prompt (bottom centre), queued toasts,
    /// a choice list (signposts), the <see cref="JourneyMenu"/> side panel (Esc / Start) and a full-screen fade layer. While the
    /// map is open (<see cref="MapMode"/>) or the player hides it (<see cref="ShowHud"/>) the always-on part fades out. Access
    /// it through <see cref="Instance"/>. A scene with a player but no HUD gets one at runtime.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class HudController : MonoBehaviour
    {
        [Header("References (found automatically when empty)")]
        [SerializeField, Tooltip("The player's travel state (destination, auto-travel).")]
        private PlayerTravel travel;

        [SerializeField, Tooltip("The player's camera rig (first/third person chip).")]
        private PlayerCameraRig cameraRig;

        [Header("Timing")]
        [SerializeField, Min(0.5f), Tooltip("Seconds a toast stays fully visible.")]
        private float toastSeconds = 3.2f;

        [SerializeField, Min(0.05f), Tooltip("Seconds a toast takes to fade in or out.")]
        private float toastFadeSeconds = 0.6f;

        [SerializeField, Min(0.3f), Tooltip("Seconds the walking-pace notice (a toast replaced in place while the pace is stepped) stays fully visible.")]
        private float paceSeconds = 1.6f;

        [SerializeField, Min(0.05f), Tooltip("Seconds the HUD and prompt take to fade in or out.")]
        private float panelFadeSeconds = 0.35f;

        [SerializeField, Min(0.1f), Tooltip("Seconds between refreshes of the destination distance.")]
        private float distanceRefreshSeconds = 0.5f;

        private readonly Queue<string> toasts = new Queue<string>();
        private readonly StringBuilder builder = new StringBuilder(256);

        private CanvasGroup hudGroup;
        private CanvasGroup clockGroup;
        private RectTransform letterboxTop;
        private RectTransform letterboxBottom;
        private float letterboxAmount;
        private Text timeText;
        private Text dayText;
        private CanvasGroup destinationGroup;
        private Text destinationText;
        private Text chipsText;
        private CanvasGroup roadGroup;
        private Text roadRegion;
        private Text roadDetail;
        private float nextRoadRefresh;
        private string lastRoadLine;
        private CanvasGroup promptGroup;
        private Text promptText;
        private CanvasGroup toastGroup;
        private Text toastText;
        private CanvasGroup menuGroup;
        private Text menuTitle;
        private Text menuBody;
        private Text menuFooter;
        private Image fadeImage;

        private string currentToast;
        private float toastTimer;
        private int toastPhase;          // 0 idle, 1 fade in, 2 hold, 3 fade out
        private bool paceShowing;        // the toast on screen is the walking-pace notice (updated in place)
        private string pendingPace;      // a pace notice waiting for the toast slot
        private PlayerController paceSource;
        private bool promptVisible;
        private bool menuVisible;
        private string[] menuLines;
        private int lastMinute = -1;
        private int lastDay = -1;
        private int lastSeasonKey = -1;
        private string lastSeasonName;
        private float lastSeasonYear = float.NaN;
        private bool lastTravelling;
        private CameraMode lastMode;
        private bool chipsDirty = true;
        private float nextDistanceRefresh;
        private Settlement routedDestination;
        private float routedDistance = -1f;
        private float nextRouteRefresh;
        private string lastDestinationLine;
        private Coroutine fadeRoutine;

        /// <summary>The HUD of the loaded scene.</summary>
        public static HudController Instance { get; private set; }

        /// <summary>True while the held map is open: the HUD hides except for the prompt.</summary>
        public bool MapMode { get; set; }

        /// <summary>
        /// Player preference (Journey menu → View): false fades out the always-on HUD (clock, destination, mode chips).
        /// Contextual prompts, toasts, the choice list and fades still show.
        /// </summary>
        public bool ShowHud { get; set; } = true;

        /// <summary>
        /// True while the cinematic camera has the view: the always-on HUD fades out (toasts, prompts and fades still show)
        /// unless <see cref="HudAlways"/>.
        /// </summary>
        public bool CinematicMode { get; set; }

        /// <summary>True while the traveller sits: the always-on HUD fades out like <see cref="CinematicMode"/>.</summary>
        public bool SeatedMode { get; set; }

        /// <summary>Player preference (Journey menu → View, "Always"): keep the HUD even during cinematic shots and while seated.</summary>
        public bool HudAlways { get; set; }

        /// <summary>Shows the clock even while the rest of the HUD is hidden (time running fast while seated).</summary>
        public bool ForceClock { get; set; }

        /// <summary>Thin black bars top and bottom (the cinematic camera's letterbox), eased in and out.</summary>
        public bool Letterbox { get; set; }

        /// <summary>Share of the screen height each letterbox bar takes when fully in.</summary>
        public const float LetterboxHeight = 0.085f;

        /// <summary>True when the always-on HUD (clock, destination, chips) is meant to be visible now.</summary>
        public bool HudVisible => !MapMode && ShowHud && (HudAlways || (!CinematicMode && !SeatedMode));

        /// <summary>The Journey menu built with this HUD.</summary>
        public JourneyMenu Journey { get; private set; }

        /// <summary>Current alpha of the full-screen fade (0 = clear, 1 = black).</summary>
        public float FadeAlpha => fadeImage != null ? fadeImage.color.a : 0f;

        /// <summary>True while the choice list is shown.</summary>
        public bool IsMenuVisible => menuVisible;

        /// <summary>Queues a toast message (duplicates of the one on screen are ignored).</summary>
        public void ShowToast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (toastPhase != 0 && message == currentToast) return;
            if (toasts.Count > 0 && message == LastQueued()) return;
            toasts.Enqueue(message);
        }

        /// <summary>
        /// Shows the walking pace (e.g. "Stroll · 1.1 m/s") in the toast for about two seconds. Stepping again while it shows
        /// replaces the text in place and restarts its time instead of queueing; it goes ahead of queued toasts, and a toast
        /// already on screen is shortened as for any queued message.
        /// </summary>
        public void ShowPace(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (toastPhase != 0 && paceShowing)
            {
                currentToast = message;
                if (toastText != null) toastText.text = message;
                if (toastPhase == 2) toastTimer = 0f;
                else if (toastPhase == 3) toastPhase = 1;
                return;
            }

            pendingPace = message;
        }

        private void OnPaceChanged(float pace, float metresPerSecond)
        {
            ShowPace(WalkPace.Label(pace, metresPerSecond));
        }

        /// <summary>Shows the contextual prompt at the bottom of the screen (e.g. "E — Read signpost").</summary>
        public void ShowPrompt(string message)
        {
            if (promptText == null) return;
            if (!ReferenceEquals(promptText.text, message) && promptText.text != message) promptText.text = message;
            promptVisible = true;
        }

        /// <summary>Hides the contextual prompt.</summary>
        public void HidePrompt()
        {
            promptVisible = false;
        }

        /// <summary>Fades the full-screen overlay to <paramref name="alpha"/> (1 = black) over <paramref name="seconds"/>;
        /// yield on the returned coroutine to wait for it.</summary>
        public Coroutine Fade(float alpha, float seconds)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadeRoutine(Mathf.Clamp01(alpha), Mathf.Max(0f, seconds)));
            return fadeRoutine;
        }

        /// <summary>Shows a choice list with <paramref name="lines"/>, highlighting <paramref name="selected"/>.</summary>
        public void ShowMenu(string title, string[] lines, int selected, string footer)
        {
            if (menuGroup == null) return;
            menuLines = lines;
            menuTitle.text = title;
            menuFooter.text = footer;
            menuVisible = true;
            SetMenuSelection(selected);
        }

        /// <summary>Moves the highlight of the choice list.</summary>
        public void SetMenuSelection(int selected)
        {
            if (menuBody == null || menuLines == null) return;
            builder.Clear();
            for (int i = 0; i < menuLines.Length; i++)
            {
                if (i > 0) builder.Append('\n');
                if (i == selected) builder.Append("<color=#8C2914><b>›  ").Append(menuLines[i]).Append("</b></color>");
                else builder.Append("    ").Append(menuLines[i]);
            }

            menuBody.text = builder.ToString();
        }

        /// <summary>Hides the choice list.</summary>
        public void HideMenu()
        {
            menuVisible = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            Instance = this;
            var canvas = GetComponent<Canvas>();
            if (canvas.renderMode == RenderMode.WorldSpace) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 10);
            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();   // the Journey menu is clickable
            Build();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallSceneHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureForScene();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureForScene();

        /// <summary>
        /// Scenes with a player but no HUD (e.g. PlayerTest.unity) get one at runtime, so the Journey menu is always there;
        /// a scene with UI but no EventSystem gets an EventSystem with the Input System UI module (default UI actions).
        /// Nothing is saved into the scene.
        /// </summary>
        private static void EnsureForScene()
        {
            if (Instance == null && FindFirstObjectByType<PlayerController>() != null)
            {
                var ui = new GameObject("UI (runtime)", typeof(RectTransform));
                int layer = LayerMask.NameToLayer("UI");
                ui.layer = layer >= 0 ? layer : 0;
                var canvas = ui.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                ui.AddComponent<HudController>();
            }

            if (Instance != null) JourneyMenu.EnsureEventSystem();
        }

        private void Start()
        {
            if (travel == null) travel = FindFirstObjectByType<PlayerTravel>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<PlayerCameraRig>();
            if (travel != null)
            {
                travel.Notice += ShowToast;
                travel.DestinationChanged += OnDestinationChanged;
            }

            paceSource = travel != null ? travel.GetComponent<PlayerController>() : null;
            if (paceSource == null) paceSource = FindFirstObjectByType<PlayerController>();
            if (paceSource != null) paceSource.PaceChanged += OnPaceChanged;
        }

        private void OnDestroy()
        {
            if (travel != null)
            {
                travel.Notice -= ShowToast;
                travel.DestinationChanged -= OnDestinationChanged;
            }

            if (paceSource != null) paceSource.PaceChanged -= OnPaceChanged;
            if (Instance == this) Instance = null;
        }

        private void OnDestinationChanged(Settlement settlement)
        {
            routedDestination = null;
            routedDistance = -1f;
            nextRouteRefresh = 0f;
            nextDistanceRefresh = 0f;
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("HudController");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            float dt = Time.unscaledDeltaTime;
            float step = dt / panelFadeSeconds;
            bool hudVisible = HudVisible;
            hudGroup.alpha = Mathf.MoveTowards(hudGroup.alpha, hudVisible ? 1f : 0f, step);
            clockGroup.alpha = Mathf.MoveTowards(clockGroup.alpha, hudVisible || (ForceClock && !MapMode) ? 1f : 0f, step);
            UpdateLetterbox(dt);
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, promptVisible ? 1f : 0f, step);
            menuGroup.alpha = Mathf.MoveTowards(menuGroup.alpha, menuVisible ? 1f : 0f, step);

            UpdateClock();
            UpdateChips();
            UpdateDestination();
            UpdateRoad();
            UpdateToast(dt);
        }

        private void UpdateLetterbox(float dt)
        {
            float target = Letterbox ? 1f : 0f;
            if (Mathf.Approximately(letterboxAmount, target) && (target > 0f || !letterboxTop.gameObject.activeSelf)) return;
            letterboxAmount = Mathf.MoveTowards(letterboxAmount, target, dt / 0.9f);
            float eased = Mathf.SmoothStep(0f, 1f, letterboxAmount);
            bool visible = eased > 0.001f;
            if (letterboxTop.gameObject.activeSelf != visible)
            {
                letterboxTop.gameObject.SetActive(visible);
                letterboxBottom.gameObject.SetActive(visible);
            }

            letterboxTop.anchorMin = new Vector2(0f, 1f - LetterboxHeight * eased);
            letterboxBottom.anchorMax = new Vector2(1f, LetterboxHeight * eased);
        }

        private void UpdateClock()
        {
            DayCycle cycle = DayCycle.Current;
            if (cycle == null)
            {
                if (lastMinute != -2)
                {
                    lastMinute = -2;
                    timeText.text = "--:--";
                    dayText.text = string.Empty;
                }

                return;
            }

            int minute = DayClock.MinuteOfDay(cycle.Hour);
            if (minute != lastMinute)
            {
                lastMinute = minute;
                timeText.text = $"{minute / 60:00}:{minute % 60:00}";
            }

            // The season in words beside the day, unless it is the fixed autumn the game always had; a one-off notice when its
            // name moves on under Auto ("Early spring"), but not when the year jumps (a new choice, a journey resumed).
            int seasonKey = Season.Revision * 4 + (int)Season.Choice;
            if (cycle.Day != lastDay || seasonKey != lastSeasonKey)
            {
                lastDay = cycle.Day;
                lastSeasonKey = seasonKey;
                float year = Season.Year;
                string season = Season.Choice == SeasonChoice.Autumn ? null : Season.Name(year);
                if (season != null && lastSeasonName != null && season != lastSeasonName && Season.Choice == SeasonChoice.Auto &&
                    !float.IsNaN(lastSeasonYear) && Season.Distance(year, lastSeasonYear) < 0.1f)
                {
                    ShowToast(season);
                }

                lastSeasonName = season;
                lastSeasonYear = year;
                dayText.text = season == null ? $"Day {cycle.Day}" : $"Day {cycle.Day}   ·   {season}";
            }
        }

        private void UpdateChips()
        {
            bool travelling = travel != null && travel.IsTravelling;
            CameraMode mode = cameraRig != null ? cameraRig.Mode : CameraMode.ThirdPerson;
            if (!chipsDirty && travelling == lastTravelling && mode == lastMode) return;
            chipsDirty = false;
            lastTravelling = travelling;
            lastMode = mode;
            chipsText.text = (travelling ? "Auto-travel" : "Walk") + "   ·   " + (mode == CameraMode.FirstPerson ? "1st person" : "3rd person");
        }

        private void UpdateDestination()
        {
            if (Time.unscaledTime < nextDistanceRefresh) return;
            nextDistanceRefresh = Time.unscaledTime + distanceRefreshSeconds;

            Settlement destination = travel != null ? travel.Destination : null;
            string line = null;
            if (destination != null)
            {
                float remaining = travel.IsTravelling ? travel.Traveller.RemainingDistance : RouteDistance(destination);
                line = remaining >= 0f ? $"{destination.Name}   ·   {UiKit.FormatDistance(remaining)}" : destination.Name;
            }

            if (line == lastDestinationLine) return;
            lastDestinationLine = line;
            destinationGroup.alpha = line != null ? 1f : 0f;
            destinationText.text = line ?? string.Empty;
        }

        /// <summary>
        /// Endless road only: the region, the distance walked along the road and the next inn in the direction the traveller
        /// faces. Hidden in the finite world.
        /// </summary>
        private void UpdateRoad()
        {
            if (Time.unscaledTime < nextRoadRefresh) return;
            nextRoadRefresh = Time.unscaledTime + distanceRefreshSeconds;
            MedievalZen.Endless.EndlessWorld endless = MedievalZen.Endless.EndlessWorld.Current;
            if (endless == null || endless.Generator == null)
            {
                if (roadGroup.alpha > 0f) roadGroup.alpha = 0f;
                lastRoadLine = null;
                return;
            }

            string region = endless.RegionName;
            MedievalZen.Endless.Waypoint inn = endless.InnAheadOfTraveller;
            string walked = endless.S >= 0.0 ? $"{endless.S / 1000.0:0.0} km walked" : $"{-endless.S / 1000.0:0.0} km behind the start";
            string detail = inn != null ? $"{walked}   ·   {inn.Name} in {UiKit.FormatDistance((float)endless.RoadDistanceTo(inn))}" : walked;
            string line = region + " | " + detail;
            roadGroup.alpha = string.IsNullOrEmpty(region) ? 0f : 1f;
            if (line == lastRoadLine) return;
            lastRoadLine = line;
            roadRegion.text = region;
            roadDetail.text = detail;
        }

        private float RouteDistance(Settlement destination)
        {
            if (destination == routedDestination && Time.unscaledTime < nextRouteRefresh) return routedDistance;
            routedDestination = destination;
            nextRouteRefresh = Time.unscaledTime + 2f;
            routedDistance = -1f;
            RoadNetwork roads = GameWorld.Current != null ? GameWorld.Current.Roads : null;
            if (roads == null || roads.Segments.Count == 0 || travel == null) return routedDistance;
            Vector3 position = travel.transform.position;
            RoadLocation start = roads.Project(position);
            if (roads.TryFindRoute(start, destination, out RoadRoute route))
            {
                Vector3 onRoad = start.Position;
                routedDistance = route.TotalLength + new Vector2(onRoad.x - position.x, onRoad.z - position.z).magnitude;
            }

            return routedDistance;
        }

        private void UpdateToast(float dt)
        {
            switch (toastPhase)
            {
                case 0:
                    if (pendingPace != null)
                    {
                        // The walking pace answers the player's own wheel: it goes ahead of queued toasts.
                        currentToast = pendingPace;
                        pendingPace = null;
                        paceShowing = true;
                    }
                    else
                    {
                        if (toasts.Count == 0) return;
                        currentToast = toasts.Dequeue();
                        paceShowing = false;
                    }

                    toastText.text = currentToast;
                    toastPhase = 1;
                    toastTimer = 0f;
                    break;
                case 1:
                    // The pace notice fades in quickly so the wheel feels answered.
                    float fadeIn = paceShowing ? Mathf.Min(toastFadeSeconds, 0.2f) : toastFadeSeconds;
                    toastGroup.alpha = Mathf.MoveTowards(toastGroup.alpha, 1f, dt / fadeIn);
                    if (toastGroup.alpha >= 1f)
                    {
                        toastPhase = 2;
                        toastTimer = 0f;
                    }

                    break;
                case 2:
                    toastTimer += dt;
                    float hold = paceShowing ? paceSeconds : toastSeconds;
                    bool waiting = toasts.Count > 0 || pendingPace != null;
                    if (toastTimer >= hold || (waiting && toastTimer >= hold * 0.5f)) toastPhase = 3;
                    break;
                default:
                    toastGroup.alpha = Mathf.MoveTowards(toastGroup.alpha, 0f, dt / toastFadeSeconds);
                    if (toastGroup.alpha <= 0f)
                    {
                        toastPhase = 0;
                        currentToast = null;
                    }

                    break;
            }
        }

        private string LastQueued()
        {
            string last = null;
            foreach (string queued in toasts) last = queued;
            return last;
        }

        private IEnumerator FadeRoutine(float target, float seconds)
        {
            float start = fadeImage.color.a;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
                SetFade(Mathf.Lerp(start, target, k));
                yield return null;
            }

            SetFade(target);
            fadeRoutine = null;
        }

        private void SetFade(float alpha)
        {
            Color c = fadeImage.color;
            c.a = alpha;
            fadeImage.color = c;
            fadeImage.enabled = alpha > 0.001f;
        }

        // ------------------------------------------------------------------ layout

        private static RectTransform CreateBar(string name, Transform parent, bool top)
        {
            RectTransform bar = UiKit.CreateRect(name, parent);
            bar.anchorMin = top ? new Vector2(0f, 1f) : Vector2.zero;
            bar.anchorMax = top ? Vector2.one : new Vector2(1f, 0f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            Image image = bar.gameObject.AddComponent<Image>();
            image.color = new Color(0.01f, 0.01f, 0.012f, 1f);
            image.raycastTarget = false;
            bar.gameObject.SetActive(false);
            return bar;
        }

        private void Build()
        {
            Transform root = transform;

            // Top-left: time + day.
            RectTransform hud = UiKit.CreateRect("Hud", root);
            UiKit.Stretch(hud);
            hudGroup = hud.gameObject.AddComponent<CanvasGroup>();
            hudGroup.blocksRaycasts = false;
            hudGroup.interactable = false;

            // The clock has its own group (outside the HUD's) so it can stay up while time runs fast with the HUD hidden.
            Image clock = UiKit.CreatePanel("Clock", root, UiKit.Parchment);
            UiKit.Place(clock.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(170f, 62f));
            clockGroup = clock.gameObject.AddComponent<CanvasGroup>();
            clockGroup.blocksRaycasts = false;
            clockGroup.interactable = false;
            clock.transform.SetSiblingIndex(hud.GetSiblingIndex() + 1);
            timeText = UiKit.CreateText("Time", clock.transform, 28, TextAnchor.MiddleLeft, UiKit.Ink, FontStyle.Bold);
            UiKit.Place(timeText.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(90f, 40f));
            dayText = UiKit.CreateText("Day", clock.transform, 18, TextAnchor.MiddleRight, UiKit.FadedInk, FontStyle.Italic);
            UiKit.Place(dayText.rectTransform, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(70f, 40f));

            // Under the clock: the endless road's region, distance walked and next inn (hidden in the finite world).
            Image road = UiKit.CreatePanel("Road", hud, UiKit.Parchment);
            UiKit.Place(road.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -94f), new Vector2(420f, 64f));
            roadGroup = road.gameObject.AddComponent<CanvasGroup>();
            roadGroup.alpha = 0f;
            roadRegion = UiKit.CreateText("Region", road.transform, 20, TextAnchor.UpperLeft, UiKit.Ink, FontStyle.Bold);
            UiKit.Place(roadRegion.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -6f), new Vector2(392f, 28f));
            roadDetail = UiKit.CreateText("Detail", road.transform, 17, TextAnchor.UpperLeft, UiKit.FadedInk, FontStyle.Italic);
            UiKit.Place(roadDetail.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -34f), new Vector2(392f, 24f));

            // Top-right: destination + chips.
            Image destination = UiKit.CreatePanel("Destination", hud, UiKit.Parchment);
            UiKit.Place(destination.rectTransform, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(380f, 44f));
            destinationGroup = destination.gameObject.AddComponent<CanvasGroup>();
            destinationGroup.alpha = 0f;
            destinationText = UiKit.CreateText("Text", destination.transform, 20, TextAnchor.MiddleCenter, UiKit.Ink, FontStyle.Italic);
            UiKit.Stretch(destinationText.rectTransform);

            Image chips = UiKit.CreatePanel("Chips", hud, new Color(UiKit.Parchment.r, UiKit.Parchment.g, UiKit.Parchment.b, 0.6f));
            UiKit.Place(chips.rectTransform, new Vector2(1f, 1f), new Vector2(-24f, -76f), new Vector2(260f, 32f));
            chipsText = UiKit.CreateText("Text", chips.transform, 16, TextAnchor.MiddleCenter, UiKit.FadedInk);
            UiKit.Stretch(chipsText.rectTransform);

            // Letterbox bars (cinematic camera): over the world and the HUD, under the prompt, menus, fade and toasts.
            letterboxTop = CreateBar("LetterboxTop", root, true);
            letterboxBottom = CreateBar("LetterboxBottom", root, false);

            // Bottom-centre prompt.
            Image prompt = UiKit.CreatePanel("Prompt", root, UiKit.Parchment);
            UiKit.Place(prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(560f, 46f));
            promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;
            promptText = UiKit.CreateText("Text", prompt.transform, 21, TextAnchor.MiddleCenter, UiKit.Ink);
            UiKit.Stretch(promptText.rectTransform);

            // Choice list (signposts), right of centre.
            Image menu = UiKit.CreatePanel("Choices", root, new Color(UiKit.Parchment.r, UiKit.Parchment.g, UiKit.Parchment.b, 0.92f));
            UiKit.Place(menu.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(260f, 20f), new Vector2(520f, 360f));
            menuGroup = menu.gameObject.AddComponent<CanvasGroup>();
            menuGroup.alpha = 0f;
            menuTitle = UiKit.CreateText("Title", menu.transform, 24, TextAnchor.UpperCenter, UiKit.Ink, FontStyle.Bold);
            UiKit.Place(menuTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(480f, 34f));
            menuBody = UiKit.CreateText("Body", menu.transform, 20, TextAnchor.UpperLeft, UiKit.Ink);
            UiKit.Place(menuBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(470f, 240f));
            menuBody.lineSpacing = 1.2f;
            menuFooter = UiKit.CreateText("Footer", menu.transform, 16, TextAnchor.LowerCenter, UiKit.FadedInk, FontStyle.Italic);
            UiKit.Place(menuFooter.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(480f, 26f));

            // Journey menu (Esc / Start): a side panel over the HUD, under the fade and the toasts.
            Journey = gameObject.AddComponent<JourneyMenu>();
            Journey.Build(this, root);

            // Full-screen fade.
            RectTransform fade = UiKit.CreateRect("Fade", root);
            UiKit.Stretch(fade);
            fadeImage = fade.gameObject.AddComponent<Image>();
            fadeImage.color = new Color(0.02f, 0.02f, 0.03f, 0f);
            fadeImage.raycastTarget = false;
            fadeImage.enabled = false;

            // Toasts sit above the fade so "You rest until dawn" reads on black.
            Image toast = UiKit.CreatePanel("Toast", root, UiKit.Parchment);
            UiKit.Place(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(620f, 56f));
            toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastText = UiKit.CreateText("Text", toast.transform, 26, TextAnchor.MiddleCenter, UiKit.Ink, FontStyle.Italic);
            UiKit.Stretch(toastText.rectTransform);
        }
    }
}
