using System.Globalization;
using System.IO;
using MedievalZen.Review;
using UnityEditor;
using UnityEngine;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// Review captures (<see cref="ReviewCapture"/>): <c>MedievalZen/Review/Capture Tour</c> runs the screenshot tour in Play mode in
    /// the EndlessRoad scene (from where the traveller stands, or from the distance set in <c>Tour Start Distance…</c>);
    /// <c>Capture Places</c> visits each inn, hamlet, village and landmark of the next 20 km; <c>Low Sun Shots</c> toggles the
    /// low-sun repeat; <c>Weather/…</c> picks the weather the tours run in (default Clear); <c>Open Shots Folder</c> shows the
    /// folder. F9 in Play mode saves a single capture. <c>Capture Tour From Road Start</c> runs the tour from 0 m without touching the
    /// saved start. <c>Walk Benchmark</c> auto-travels the set distance at the set multiple of the pace (<c>Walk Benchmark Options/</c>)
    /// and writes <c>walk_*.json</c> + <c>.csv</c>.
    /// </summary>
    internal static class ReviewMenu
    {
        private const string LowSunKey = "MedievalZen.Review.LowSun";
        private const string LowSunMenu = "MedievalZen/Review/Low Sun Shots";
        private const string WeatherKey = "MedievalZen.Review.Weather";
        private const string WeatherMenu = "MedievalZen/Review/Weather/";
        internal const string StartKey = "MedievalZen.Review.TourStartS";

        private const string Usage =
            "Review capture: open Assets/_Project/Scenes/EndlessRoad.unity, press Play, wait until the road is shown (Game view visible, " +
            "at the resolution you want), then MedievalZen/Review/Capture Tour (every 600 m of the next 6 km plus each inn, pass, ford, " +
            "bridge, village and landmark; from where you stand or from 'Tour Start Distance…') or Capture Places (each inn, hamlet, " +
            "village and landmark of the next 20 km, with a raised overview of each settlement). 3 shots per stop, again with a low sun " +
            "when 'Low Sun Shots' is ticked, in the weather ticked under 'Weather' (default Clear); then the traveller, time, weather, " +
            "camera and overlay are put back. Esc or F9 stops it. F9 alone saves one capture. Files: <project>/ReviewShots/ (PNG + JSON).";

        /// <summary>Adds the capture component whenever Play mode starts (F9 works in any scene with the player rig).</summary>
        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode) ReviewCapture.Ensure();
        }

        [MenuItem("MedievalZen/Review/Capture Tour", false, 1)]
        private static void CaptureTour()
        {
            var options = new ReviewTourOptions { LowSun = EditorPrefs.GetBool(LowSunKey, true), Weather = Weather, StartS = StartDistance };
            Start(options, "tour");
        }

        [MenuItem("MedievalZen/Review/Capture Tour From Road Start", false, 1)]
        private static void CaptureTourFromRoadStart()
        {
            // Road distance 0 whatever the saved 'Tour Start Distance...' says (the preference is left as it is).
            var options = new ReviewTourOptions { LowSun = EditorPrefs.GetBool(LowSunKey, true), Weather = Weather, StartS = 0.0, StartReason = "from the road start (Capture Tour From Road Start)" };
            Start(options, "tour");
        }

        [MenuItem("MedievalZen/Review/Capture Places", false, 2)]
        private static void CapturePlaces()
        {
            ReviewTourOptions options = ReviewTourOptions.PlacesTour();
            options.LowSun = EditorPrefs.GetBool(LowSunKey, true);
            options.Weather = Weather;
            options.StartS = StartDistance;   // 'Tour Start Distance...' applies here too (2026-10-09): a city behind the traveller got no stop
            Start(options, "places tour");
        }

        private static void Start(ReviewTourOptions options, string what)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.Log(Usage);
                return;
            }

            if (Endless.EndlessWorld.Current == null)
            {
                Debug.LogWarning($"Review capture: the {what} needs the endless road (EndlessRoad scene). F9 still saves single captures here. " + Usage);
                return;
            }

            ReviewCapture capture = ReviewCapture.Ensure();
            if (capture.IsRunning)
            {
                Debug.Log("Review capture: a tour is already running (Esc or F9 stops it).");
                return;
            }

            if (!capture.StartTour(options)) Debug.LogWarning($"Review capture: could not start the {what} now (the road is still loading, a Go now is running, or there is no traveller). Try again in a moment.");
        }

        // ------------------------------------------------------------------ walking benchmark

        private const string WalkSpeedKey = "MedievalZen.Review.WalkSpeed";
        private const string WalkDistanceKey = "MedievalZen.Review.WalkDistance";
        private const string WalkMenu = "MedievalZen/Review/Walk Benchmark Options/";

        private static float WalkSpeed => EditorPrefs.GetFloat(WalkSpeedKey, 6f);
        private static float WalkDistance => EditorPrefs.GetFloat(WalkDistanceKey, 2000f);

        [MenuItem("MedievalZen/Review/Walk Benchmark", false, 5)]
        private static void WalkBenchmark()
        {
            if (!EditorApplication.isPlaying || Endless.EndlessWorld.Current == null)
            {
                Debug.Log("Walk benchmark: open Assets/_Project/Scenes/EndlessRoad.unity, press Play, wait until the road is shown (Game view visible), " +
                          $"then MedievalZen/Review/Walk Benchmark. It auto-travels {WalkDistance:0} m from where you stand at x{WalkSpeed:0.##} the auto-travel pace " +
                          "(Walk Benchmark Options), from inn to inn, and writes ReviewShots/walk_*.json and .csv. Esc or F9 stops it.");
                return;
            }

            ReviewCapture capture = ReviewCapture.Ensure();
            if (capture.IsRunning)
            {
                Debug.Log("Review capture: a tour or walk is already running (Esc or F9 stops it).");
                return;
            }

            var options = new WalkBenchOptions { Distance = WalkDistance, SpeedScale = WalkSpeed, Weather = Weather };
            if (!capture.StartWalk(options)) Debug.LogWarning("Walk benchmark: could not start now (the road is still loading, a Go now is running, or there is no traveller). Try again in a moment.");
        }

        private static bool CheckWalk(string item, bool on)
        {
            Menu.SetChecked(WalkMenu + item, on);
            return true;
        }

        [MenuItem(WalkMenu + "Speed x1 (true walk)", false, 6)] private static void WalkSpeed1() => EditorPrefs.SetFloat(WalkSpeedKey, 1f);
        [MenuItem(WalkMenu + "Speed x1 (true walk)", true)] private static bool WalkSpeed1Check() => CheckWalk("Speed x1 (true walk)", Mathf.Approximately(WalkSpeed, 1f));
        [MenuItem(WalkMenu + "Speed x6", false, 7)] private static void WalkSpeed6() => EditorPrefs.SetFloat(WalkSpeedKey, 6f);
        [MenuItem(WalkMenu + "Speed x6", true)] private static bool WalkSpeed6Check() => CheckWalk("Speed x6", Mathf.Approximately(WalkSpeed, 6f));
        [MenuItem(WalkMenu + "Speed x12 (stress)", false, 8)] private static void WalkSpeed12() => EditorPrefs.SetFloat(WalkSpeedKey, 12f);
        [MenuItem(WalkMenu + "Speed x12 (stress)", true)] private static bool WalkSpeed12Check() => CheckWalk("Speed x12 (stress)", Mathf.Approximately(WalkSpeed, 12f));
        [MenuItem(WalkMenu + "Distance 1 km", false, 20)] private static void WalkDistance1() => EditorPrefs.SetFloat(WalkDistanceKey, 1000f);
        [MenuItem(WalkMenu + "Distance 1 km", true)] private static bool WalkDistance1Check() => CheckWalk("Distance 1 km", Mathf.Approximately(WalkDistance, 1000f));
        [MenuItem(WalkMenu + "Distance 2 km", false, 21)] private static void WalkDistance2() => EditorPrefs.SetFloat(WalkDistanceKey, 2000f);
        [MenuItem(WalkMenu + "Distance 2 km", true)] private static bool WalkDistance2Check() => CheckWalk("Distance 2 km", Mathf.Approximately(WalkDistance, 2000f));
        [MenuItem(WalkMenu + "Distance 5 km", false, 22)] private static void WalkDistance5() => EditorPrefs.SetFloat(WalkDistanceKey, 5000f);
        [MenuItem(WalkMenu + "Distance 5 km", true)] private static bool WalkDistance5Check() => CheckWalk("Distance 5 km", Mathf.Approximately(WalkDistance, 5000f));

        [MenuItem("MedievalZen/Review/Tour Start Distance...", false, 3)]
        private static void OpenStartWindow() => ReviewStartWindow.Open();

        [MenuItem(LowSunMenu, false, 20)]
        private static void ToggleLowSun()
        {
            EditorPrefs.SetBool(LowSunKey, !EditorPrefs.GetBool(LowSunKey, true));
        }

        [MenuItem(LowSunMenu, true)]
        private static bool ToggleLowSunValidate()
        {
            Menu.SetChecked(LowSunMenu, EditorPrefs.GetBool(LowSunKey, true));
            return true;
        }

        // ------------------------------------------------------------------ weather (a checked submenu, default Clear)

        private static ReviewWeather Weather => (ReviewWeather)EditorPrefs.GetInt(WeatherKey, (int)ReviewWeather.Clear);

        private static bool SetWeather(ReviewWeather weather)
        {
            EditorPrefs.SetInt(WeatherKey, (int)weather);
            return true;
        }

        private static bool CheckWeather(string item, ReviewWeather weather)
        {
            Menu.SetChecked(WeatherMenu + item, Weather == weather);
            return true;
        }

        [MenuItem(WeatherMenu + "Keep current", false, 21)] private static void WeatherKeep() => SetWeather(ReviewWeather.Keep);
        [MenuItem(WeatherMenu + "Keep current", true)] private static bool WeatherKeepCheck() => CheckWeather("Keep current", ReviewWeather.Keep);
        [MenuItem(WeatherMenu + "Clear", false, 22)] private static void WeatherClear() => SetWeather(ReviewWeather.Clear);
        [MenuItem(WeatherMenu + "Clear", true)] private static bool WeatherClearCheck() => CheckWeather("Clear", ReviewWeather.Clear);
        [MenuItem(WeatherMenu + "Hazy", false, 23)] private static void WeatherHazy() => SetWeather(ReviewWeather.Hazy);
        [MenuItem(WeatherMenu + "Hazy", true)] private static bool WeatherHazyCheck() => CheckWeather("Hazy", ReviewWeather.Hazy);
        [MenuItem(WeatherMenu + "Overcast", false, 24)] private static void WeatherOvercast() => SetWeather(ReviewWeather.Overcast);
        [MenuItem(WeatherMenu + "Overcast", true)] private static bool WeatherOvercastCheck() => CheckWeather("Overcast", ReviewWeather.Overcast);
        [MenuItem(WeatherMenu + "Rain", false, 25)] private static void WeatherRain() => SetWeather(ReviewWeather.Rain);
        [MenuItem(WeatherMenu + "Rain", true)] private static bool WeatherRainCheck() => CheckWeather("Rain", ReviewWeather.Rain);

        // ------------------------------------------------------------------ start distance

        /// <summary>The saved tour start distance (road metres), or NaN for "from where I stand".</summary>
        internal static double StartDistance
        {
            get
            {
                string text = EditorPrefs.GetString(StartKey, string.Empty);
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? s : double.NaN;
            }
            set => EditorPrefs.SetString(StartKey, double.IsNaN(value) ? string.Empty : value.ToString("R", CultureInfo.InvariantCulture));
        }

        [MenuItem("MedievalZen/Review/Open Shots Folder", false, 40)]
        private static void OpenFolder()
        {
            Directory.CreateDirectory(ReviewCapture.Folder);
            EditorUtility.RevealInFinder(ReviewCapture.Folder);
        }
    }

    /// <summary>Sets where <c>Capture Tour</c> and <c>Capture Places</c> start: from where the traveller stands (the default) or from a road distance.</summary>
    internal sealed class ReviewStartWindow : EditorWindow
    {
        private bool fromHere;
        private double distance;

        public static void Open()
        {
            var window = GetWindow<ReviewStartWindow>(true, "Review Tour Start", true);
            window.minSize = new Vector2(340f, 120f);
            double s = ReviewMenu.StartDistance;
            window.fromHere = double.IsNaN(s);
            window.distance = double.IsNaN(s) ? 0.0 : s;
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Where MedievalZen/Review/Capture Tour and Capture Places start. A tour then runs 6 km along the road from there, " +
                                    "a places tour visits the places of the next 20 km.", MessageType.None);
            fromHere = EditorGUILayout.ToggleLeft("From where I stand", fromHere);
            using (new EditorGUI.DisabledScope(fromHere))
            {
                distance = EditorGUILayout.DoubleField("Road distance (m)", distance);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Now:", double.IsNaN(ReviewMenu.StartDistance) ? "from where I stand" : $"from {ReviewMenu.StartDistance:0} m");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Save", GUILayout.Width(90f)))
                {
                    ReviewMenu.StartDistance = fromHere ? double.NaN : distance;
                    Close();
                }
            }
        }
    }
}
