using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MedievalZen.Cinematic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedievalZen.Review
{
    /// <summary>
    /// Review captures for visual work (a component cannot live in an Editor folder, so it is in the runtime assembly; only the
    /// editor's <c>ReviewMenu</c> ever creates it, when Play mode starts, so a build never has one). F9 saves the current view; the review tour (menu
    /// <c>MedievalZen/Review/Capture Tour</c>) takes the traveller along the next 6 km of the endless road with the same routine as the
    /// Journey menu's "Go now" (waiting for the near tiles and the distant ones within 1.1 km at every stop) and saves, at each stop,
    /// the third-person view along the road, a wide shot at the most prominent distant ground (at a landmark: the landmark, from the
    /// road point where it is in sight) and a view across the valley, optionally again with a low sun. <c>Capture Places</c> visits
    /// the inns, hamlets, villages and landmarks of the next 20 km instead, with a raised overview of each settlement. Tours run in
    /// the weather chosen in the menu. Every capture is the final frame (post-processing and the atmosphere's fog included) at the
    /// Game view's resolution with the UI and the F3 overlay hidden, saved as PNG in <c>&lt;project&gt;/ReviewShots/</c> with a JSON
    /// sidecar. Esc or F9 stops the tour; everything (place, time, weather, camera, HUD, overlay, cinematic camera) is put back at
    /// the end, when stopped, and when play ends.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed partial class ReviewCapture : MonoBehaviour, ICameraPoseProvider
    {
        /// <summary>The capture component of this play session (created on demand).</summary>
        public static ReviewCapture Instance { get; private set; }

        /// <summary>Where captures go: <c>&lt;project&gt;/ReviewShots</c>.</summary>
        public static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ReviewShots");

        private const float ArrivalTimeout = 90f;

        private ReviewTourRun run;
        private string stamp;
        private int captured;
        private bool hasPose;
        private Vector3 posePosition;
        private Quaternion poseRotation = Quaternion.identity;
        private float poseFov = 50f;
        private PlayerCameraRig rig;
        private Transform traveller;
        private CinematicDirector cinematic;
        private bool cinematicCamera, cinematicTravelling;
        private readonly float[] frameMs = new float[60];
        private int frameCursor;
        private bool restoring;
        private ReviewTourKind kind = ReviewTourKind.Tour;
        private ReviewWeather weatherOption = ReviewWeather.Keep;
        private readonly List<TourShotRecord> shotRecords = new List<TourShotRecord>();
        private readonly Dictionary<ReviewStop, ReviewCanopy> landmarkCanopies = new Dictionary<ReviewStop, ReviewCanopy>();
        private double tourStartS;
        private string tourStartReason;

        /// <summary>True while a tour or the walking benchmark runs.</summary>
        public bool IsRunning => run != null || walk != null;

        /// <summary>The capture component, created if needed (play mode only).</summary>
        public static ReviewCapture Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Review capture") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            return go.AddComponent<ReviewCapture>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (run != null)
            {
                WriteTourSummary(true);
                RestoreNow(false);
            }

            if (walk != null) FinishWalk("Play mode ended");
            FrameCost.Recording = false;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            frameMs[frameCursor++ % frameMs.Length] = Time.unscaledDeltaTime * 1000f;
            // A tour teleports and a benchmark scales the travel speed: no piece of music starts meanwhile.
            MedievalZen.Sound.SoundMix.CaptureRunning = IsRunning;
            // Stall hunt: the per-frame culprit records while a tour or walk runs, or a shot is measured.
            FrameCost.Recording = IsRunning || measuring;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (walk != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.f9Key.wasPressedThisFrame) walk.StopRequested = true;
                return;
            }

            if (run != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.f9Key.wasPressedThisFrame)
                {
                    run.Abort();
                    Debug.Log("Review tour: stopping (Esc/F9); putting everything back.");
                }

                return;
            }

            if (keyboard.f9Key.wasPressedThisFrame) StartCoroutine(SingleShot());
        }

        // ------------------------------------------------------------------ single shot

        private IEnumerator SingleShot()
        {
            stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Resolve();
            var stop = new ReviewStop { S = EndlessWorld.Current != null ? EndlessWorld.Current.S : 0.0, Label = "f9" };
            yield return Measure(null);
            kind = ReviewTourKind.Single;
            weatherOption = ReviewWeather.Keep;
            yield return Capture(stop, ReviewShot.Road, false, "single");
            Debug.Log($"Review capture saved to {Folder}");
        }

        // ------------------------------------------------------------------ tour

        /// <summary>Starts the tour (false when one runs, there is no endless road, or the streamer is busy).</summary>
        public bool StartTour(ReviewTourOptions options)
        {
            EndlessWorld world = EndlessWorld.Current;
            if (run != null || walk != null || world == null || world.IsBusy || world.Generator == null) return false;
            Resolve();
            if (traveller == null || rig == null) return false;
            options = options ?? new ReviewTourOptions();
            // The tour always runs the way the road runs (+s, "ahead" everywhere else in the game). It used to follow the
            // traveller's facing, and a tour started facing back visited the previous regions instead of the ones ahead.
            const int direction = 1;
            var waypoints = new List<Waypoint>();
            double from = double.IsNaN(options.StartS) ? world.S : options.StartS;
            double to = from + direction * options.Length;
            world.Generator.Waypoints(Math.Min(from, to), Math.Max(from, to), waypoints);
            List<ReviewStop> stops = ReviewTourPlan.Stops(from, direction, options, waypoints);
            if (options.Kind == ReviewTourKind.Places) AddCityStops(world, stops);
            DayCycle cycle = DayCycle.Current;
            HudController hud = HudController.Instance;
            WeatherSystem weather = WeatherSystem.Current;
            var snapshot = new ReviewSnapshot
            {
                S = world.S,
                Direction = DirectionOf(world),   // only to put the traveller back facing the same way
                Hour = cycle != null ? cycle.Hour : 12f,
                TimeScale = cycle != null ? cycle.TimeScale : 1f,
                CameraMode = (int)rig.Mode,
                Yaw = rig.Yaw,
                Pitch = rig.Pitch,
                ShowHud = hud == null || hud.ShowHud,
                ShowOverlay = world.ShowOverlay,
                ForcedWeather = weather != null && weather.ForcedState.HasValue ? (int)weather.ForcedState.Value : -1,
            };
            run = new ReviewTourRun(stops, options.LowSun, snapshot);
            stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            captured = 0;
            kind = options.Kind;
            weatherOption = options.Weather;
            world.ShowOverlay = false;   // the F3 overlay must not be burned into the shots
            WeatherState? wanted = ReviewTourPlan.WeatherFor(options.Weather);
            if (weather != null && wanted.HasValue)
            {
                weather.Force(wanted);
                weather.SkipCrossFade();
            }

            if (cinematic != null)
            {
                cinematicCamera = cinematic.CinematicCamera;
                cinematicTravelling = cinematic.CinematicWhenTravelling;
                cinematic.CinematicCamera = false;
                cinematic.CinematicWhenTravelling = false;
                if (cinematic.IsRunning) cinematic.ToggleManual();
            }

            if (cycle != null) cycle.TimeScale = 0f;   // the light stays as it is while the tour runs
            shotRecords.Clear();
            landmarkCanopies.Clear();
            tourStartS = from;
            tourStartReason = options.StartReason ?? (double.IsNaN(options.StartS) ? "from where the traveller stands" : "from 'Tour Start Distance...'");
            string what = kind == ReviewTourKind.Places ? "places tour" : "tour";
            Debug.Log(ReviewTourPlan.StartLine(what, from, options.StartS, tourStartReason));
            Debug.Log($"Review {what}: {stops.Count} stops from {from:0} m over {options.Length:0} m, {run.JobCount} captures, low sun {(options.LowSun ? "on" : "off")}, weather {options.Weather}; Esc or F9 stops it. Folder: {Folder}");
            StartCoroutine(Tour());
            return true;
        }

        /// <summary>
        /// After each walled city's own stop, its street-level stops (<see cref="CityViewpoints"/>: ramps, terrace edges, the view
        /// square, the castle's gate, court, flanks and back, the road at 150 m and 1 km), from the city's cached dressing and the
        /// terrain's hill.
        /// </summary>
        private static void AddCityStops(EndlessWorld world, List<ReviewStop> stops)
        {
            if (!(world.Generator is EndlessGenerator g)) return;   // the generator's own caches (dressings, terrain) are needed
            var waypoints = new List<Waypoint>();
            for (int i = stops.Count - 1; i >= 0; i--)
            {
                ReviewStop stop = stops[i];
                if (stop.Kind != WaypointKind.City || stop.View.HasValue) continue;
                try
                {
                    waypoints.Clear();
                    g.Waypoints(stop.S - 10.0, stop.S + 2.0 * ReviewTourPlan.PlaceLeadIn + 10.0, waypoints);
                    Waypoint city = waypoints.Find(w => w != null && w.Kind == WaypointKind.City && w.Name == stop.PlaceName);
                    if (city == null) continue;
                    Place place = MedievalZen.People.PeopleSource.FindPlace(g, city.Id, city.S);
                    if (place == null) continue;
                    Dressing d = g.Dressings.Settlement(place);
                    SettlementDisc disc = g.Terrain.Disc(place);
                    var dropped = new List<string>();
                    List<CityViewpoint> views = CityViewpoints.Plan(disc?.Hill, d, city.S, dropped);
                    foreach (string drop in dropped) Debug.Log($"Review places tour: {city.Name}: view dropped, {drop}.");
                    // The two views from the road stand where no other place's houses come between the eye and the castle.
                    waypoints.Clear();
                    g.Waypoints(city.S - CityViewpoints.FarRoad - ReviewTourPlan.RoadViewShift - 400.0, city.S, waypoints);
                    for (int k = 0; k < views.Count; k++)
                    {
                        CityViewpoint v = views[k];
                        if (!double.IsNaN(v.EyeX)) continue;
                        double want = v.StandS;
                        v.StandS = ReviewTourPlan.ClearRoadStand(s => { RoadPoint r = g.Road(s); return (r.X, r.Z); }, want, v.LookX, v.LookZ, waypoints, city);
                        if (Math.Abs(v.StandS - want) > 0.5) Debug.Log($"Review places tour: {city.Name} {v.Name} stands at {v.StandS:0} m, not {want:0} m (another place stood between it and the castle).");
                        views[k] = v;
                    }

                    stops.InsertRange(i + 1, ReviewTourPlan.CityStops(city, views));
                    int movedViews = views.FindAll(v => v.Moved > 0f).Count;
                    Debug.Log($"Review places tour: {views.Count} street-level stops at {city.Name} ({movedViews} moved up to {CityViewpoints.Search:0} m to stand clear, {dropped.Count} dropped).");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Review places tour: no street-level stops at {stop.PlaceName} ({e.Message}).");
                }
            }
        }

        private IEnumerator Tour()
        {
            EndlessWorld world = EndlessWorld.Current;
            DayCycle cycle = DayCycle.Current;
            float lowHour = ReviewTourPlan.LowSunHour();
            bool lowApplied = false;
            // Day shots in daylight whatever the hour was when the tour began (the snapshot puts the hour back at the end).
            if (cycle != null) cycle.SetHourOfDay(ReviewTourPlan.DayHour);
            while (run != null && run.Next(out ReviewJob job))
            {
                ReviewStop stop = run.Stops[job.Stop];
                if (run.StartsStop(job))
                {
                    if (lowApplied && cycle != null)
                    {
                        cycle.SetHourOfDay(ReviewTourPlan.DayHour);
                        lowApplied = false;
                    }

                    ReleasePose();
                    if (stop.HasTarget && !stop.TargetVisible.HasValue) yield return FindLandmarkView(world, stop);
                    if (run == null || run.Aborted) break;
                    // A city's street-level stops share the city's road distance: the traveller moves once (its near tiles cover the town),
                    // and again for each view from the road (2026-10-09: they used to be taken with the traveller still at the city).
                    bool arrived = world != null && ReviewTourPlan.AlreadyThere(stop, world.S);
                    float began = Time.unscaledTime;
                    while (!arrived && world != null && world.IsBusy && Time.unscaledTime - began < ArrivalTimeout) yield return null;
                    if (world == null || !arrived && !world.TravelToRoadDistance(stop.S, stop.Direction, true, () => arrived = true))
                    {
                        Debug.LogWarning($"Review tour: could not move to {stop.S:0} m; stopping.");
                        run.Abort();
                        break;
                    }

                    while (!arrived && !run.Aborted && Time.unscaledTime - began < ArrivalTimeout) yield return null;
                    if (run.Aborted) break;
                    // A view from the road looks a kilometre ahead: the distant tiles round the city finish streaming first.
                    if (stop.View.HasValue && double.IsNaN(stop.View.Value.EyeX))
                    {
                        float held = Time.unscaledTime;
                        while (world.IsBusy && !run.Aborted && Time.unscaledTime - held < ArrivalTimeout) yield return null;
                    }
                    // No drift of the previous stop (pollen, dust, petals) floats into this one's shots.
                    foreach (AmbientFxSpawner fx in FindObjectsByType<AmbientFxSpawner>(FindObjectsSortMode.None)) fx.ClearAll();
                    Toast($"Review tour {run.Done}/{run.JobCount} — {stop.Label}");
                    yield return new WaitForSecondsRealtime(1.2f);   // trees, fog and the camera settle
                }

                if (job.LowSun && !lowApplied && cycle != null)
                {
                    cycle.SetHourOfDay(lowHour);
                    lowApplied = true;
                    yield return new WaitForSecondsRealtime(0.6f);
                }

                var setShotWatch = System.Diagnostics.Stopwatch.StartNew();
                bool shotSet = SetShot(world, stop, job.Shot);
                setShotMs = (float)setShotWatch.Elapsed.TotalMilliseconds;
                if (!shotSet) continue;
                for (int i = 0; i < 4; i++) yield return null;   // the rig's blend and the trees' LOD catch up
                if (run.Aborted) break;
                yield return Measure(run);
                if (run.Aborted) break;
                yield return Capture(stop, job.Shot, job.LowSun, null);
                // At a crossing, the same view again through the first-party shaders' debug views (which term is non-finite).
                if (!job.LowSun && job.Shot == ReviewShot.Side && (stop.Kind == WaypointKind.Bridge || stop.Kind == WaypointKind.Ford))
                {
                    yield return DebugViews(stop);
                }
            }

            if (run != null) yield return RestoreRoutine();
        }

        /// <summary>
        /// Saves the current view through the first-party shaders' debug views (global <c>_MZDebugView</c> 1 and 2: flags for
        /// non-finite terms over a map of the mesh's parts, and the unlit albedo) as <c>debug_&lt;stamp&gt;_s&lt;m&gt;m_&lt;stop&gt;_view&lt;n&gt;.png</c>.
        /// </summary>
        private IEnumerator DebugViews(ReviewStop stop)
        {
            Directory.CreateDirectory(Folder);
            for (int view = 1; view <= 2; view++)
            {
                Shader.SetGlobalFloat("_MZDebugView", view);
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                Texture2D shot = null;
                try
                {
                    shot = ScreenCapture.CaptureScreenshotAsTexture();
                    if (shot != null)
                    {
                        string name = $"debug_{stamp}_s{Math.Round(stop.S)}m_{ReviewTourPlan.Slug(stop.Label)}_view{view}.png";
                        File.WriteAllBytes(Path.Combine(Folder, name), shot.EncodeToPNG());
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Review tour: debug view {view} was not saved: {e.Message}");
                }
                finally
                {
                    if (shot != null) Destroy(shot);
                    Shader.SetGlobalFloat("_MZDebugView", 0f);
                }
            }

            yield return null;
        }

        /// <summary>Points the camera for a shot: the rig's own third-person view for the road shot, a held pose for the others.</summary>
        private bool SetShot(EndlessWorld world, ReviewStop stop, ReviewShot shot)
        {
            try
            {
                RoadPoint road = world.Generator.Road(stop.S);
                float heading = road.HeadingDeg + (stop.Direction < 0 ? 180f : 0f);
                if (shot == ReviewShot.Road)
                {
                    ReleasePose();
                    rig.SetMode(CameraMode.ThirdPerson, true);
                    rig.SnapLook(heading, 2f);   // slightly down (positive pitch looks down), the horizon in the upper half
                    return true;
                }

                (double ax, double az) = world.ToAbsolute(traveller.position);
                Vector3 eye;
                Vector3 look;
                float fov;
                if (shot == ReviewShot.Prominent && stop.HasTarget)
                {
                    // A landmark stop: the wide shot frames the structure from the road point the sight check chose, 2.4 m above
                    // the road centre (on the road, never inside a roadside trunk), aimed at its mid-height with a field of view
                    // that makes it a fifth of the frame.
                    // The trees near the eye (trunks, low crowns) are tested against the built tiles' real placements; when they
                    // block, the eye moves along and across the road, then up to 8 m higher (ChooseLandmarkEye), until no tree is in the central third of the frame.
                    if (!stop.LandmarkEye.HasValue) ChooseLandmarkEye(world, stop);
                    ReviewSight.Eye chosen = stop.LandmarkEye.Value;
                    ReviewTourPlan.LandmarkFrame frame = ReviewTourPlan.FrameLandmark(chosen.X, chosen.Y, chosen.Z, stop.TargetX, stop.TargetHeight, stop.TargetZ, stop.TargetStructureHeight);
                    eye = world.ToScene(chosen.X, chosen.Y, chosen.Z);
                    look = Quaternion.Euler(-frame.PitchUp, frame.Heading, 0f) * Vector3.forward;
                    fov = frame.Fov;
                    stop.TargetPixels = frame.Pixels;
                }
                else if (shot == ReviewShot.Street)
                {
                    if (!stop.View.HasValue) return false;
                    CityViewpoint v = stop.View.Value;
                    double ex = v.EyeX, ez = v.EyeZ;
                    if (double.IsNaN(ex))
                    {
                        // A view from the road: where the traveller stands.
                        RoadPoint at = world.Generator.Road(v.StandS);
                        ex = at.X;
                        ez = at.Z;
                    }

                    // The hill's planned surface where the plan knows it (a ramp's grade, a flight's tread, a deck's top), else the built
                    // ground with its decks (the terrain under a deck or a ramp lies below its surface: the eye stood inside the ramp).
                    float eyeGround = !float.IsNaN(v.Ground) ? v.Ground : world.GroundHeightAbsolute(ex, ez);
                    float lookGround = v.LookAboveEye ? eyeGround : !float.IsNaN(v.LookGround) ? v.LookGround : world.GroundHeightAbsolute(v.LookX, v.LookZ);
                    eye = world.ToScene(ex, eyeGround + v.EyeAbove, ez);
                    look = world.ToScene(v.LookX, lookGround + v.LookAbove, v.LookZ) - eye;
                    fov = v.Fov;
                }
                else if (shot == ReviewShot.Overview)
                {
                    if (stop.PlaceRadius <= 0f) return false;
                    ReviewTourPlan.SettlementOverview(world.Generator.Height, stop.PlaceX, stop.PlaceZ, stop.PlaceHeight, stop.PlaceRadius, stop.PlaceRoadHeadingDeg,
                        stop.Direction, out double ex, out double oy, out double ez, out double lx, out double ly, out double lz);
                    eye = world.ToScene(ex, oy, ez);
                    look = world.ToScene(lx, ly, lz) - eye;
                    fov = 55f;
                }
                else if (shot == ReviewShot.Prominent)
                {
                    if (!ReviewTourPlan.ProminentTarget(world.Generator.Height, ax, az, heading, out double tx, out double tz, out float ty)) return false;
                    Vector3 forward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                    eye = traveller.position + Vector3.up * 2.6f - forward * 3.5f;
                    // Aim a quarter of the way below the top, so the hill fills the frame rather than the sky.
                    Vector3 top = world.ToScene(tx, ty, tz);
                    look = new Vector3(top.x, eye.y + (top.y - eye.y) * 0.75f, top.z) - eye;
                    fov = 40f;
                }
                else if (stop.Kind == WaypointKind.Bridge || stop.Kind == WaypointKind.Ford)
                {
                    // A crossing: the side shot looks at it from beside the stream (the arch and the water are not seen from the road).
                    RoadPoint cross = world.Generator.Road(stop.S + stop.Direction * ReviewTourPlan.PlaceLeadIn);
                    ReviewTourPlan.CrossingView(world.Generator.Height, cross.X, cross.Z, cross.Height, cross.HeadingDeg, stop.Direction,
                        out double ex, out float ey, out double ez, out double tx, out float ty, out double tz);
                    eye = world.ToScene(ex, ey, ez);
                    look = world.ToScene(tx, ty, tz) - eye;
                    fov = 50f;
                }
                else
                {
                    double side = ReviewTourPlan.SideHeading(world.Generator.Height, ax, az, heading);
                    eye = traveller.position + Vector3.up * 6f;
                    look = Quaternion.Euler(4f, (float)side, 0f) * Vector3.forward;
                    fov = 60f;
                }

                posePosition = eye;
                poseRotation = Quaternion.LookRotation(look.normalized, Vector3.up);
                poseFov = fov;
                hasPose = true;
                rig.SetPoseProvider(this, 0.01f);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Review tour: could not set up the {shot} shot at {stop.S:0} m: {e.Message}");
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryGetCameraPose(float deltaTime, out Vector3 position, out Quaternion rotation, out float fieldOfView)
        {
            position = posePosition;
            rotation = poseRotation;
            fieldOfView = poseFov;
            return hasPose;
        }

        private void ReleasePose()
        {
            if (rig != null && hasPose) rig.ReleasePoseProvider(this, 0.01f);
            hasPose = false;
        }

        // ------------------------------------------------------------------ capture

        /// <summary>
        /// Performance pass: averages the frame timings over <see cref="ReviewTourPlan.MeasureFrames"/> frames and at least
        /// <see cref="ReviewTourPlan.MeasureSeconds"/> at the settled shot, after <see cref="ReviewTourPlan.MeasureSkipFrames"/> more frames
        /// (the frame timings lag a few frames behind, and the previous capture's hitch must stay out). Written to the sidecar.
        /// </summary>
        private IEnumerator Measure(ReviewTourRun tour)
        {
            EndlessWorld world = EndlessWorld.Current;
            for (int i = 0; i < ReviewTourPlan.MeasureSkipFrames; i++) yield return null;
            if (world == null) yield break;
            world.Perf.BeginStop();
            measuring = true;
            FrameCost.Recording = true;
            measureWorstScopes = default;
            measureWorstEngine = null;
            float began = Time.unscaledTime;
            int frames = 0;
            int gcBefore = GC.CollectionCount(0);
            long heapBefore = GC.GetTotalMemory(false);
            measureWorstMs = 0f;
            measureWorstIndex = -1;
            measureWorldBusy = false;
            while (!ReviewTourPlan.MeasureDone(frames, Time.unscaledTime - began) && (tour == null || !tour.Aborted))
            {
                frames++;
                yield return null;
                float ms = Time.unscaledDeltaTime * 1000f;
                if (ms > measureWorstMs)
                {
                    measureWorstMs = ms;
                    measureWorstIndex = frames;
                    // The scopes and markers of the frame this delta covers (the previous one): the culprit of the stop's worst frame.
                    FrameCost.TopOfLastFrame(out measureWorstScopes.a, out measureWorstScopes.b, out measureWorstScopes.c);
                    measureWorstEngine = ms > ReviewWalkBench.CulpritMs ? FrameCost.EngineOfLastFrame() : null;
                }

                measureWorldBusy |= world != null && world.IsBusy;
            }

            measuring = false;
            measureGcCollections = GC.CollectionCount(0) - gcBefore;
            measureHeapMbChange = (GC.GetTotalMemory(false) - heapBefore) / (1024f * 1024f);
        }

        private float measureWorstMs, measureHeapMbChange, setShotMs;
        private int measureWorstIndex, measureGcCollections;
        private bool measureWorldBusy, measuring;
        private (CostEntry a, CostEntry b, CostEntry c) measureWorstScopes;
        private CostEntry[] measureWorstEngine;

        private IEnumerator Capture(ReviewStop stop, ReviewShot shot, bool lowSun, string fileName)
        {
            Directory.CreateDirectory(Folder);
            var hidden = new List<Canvas>();
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas == null || !canvas.enabled || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
                canvas.enabled = false;
                hidden.Add(canvas);
            }

            EndlessWorld overlayWorld = EndlessWorld.Current;
            bool overlay = overlayWorld != null && overlayWorld.ShowOverlay;
            if (overlay) overlayWorld.ShowOverlay = false;   // the F3 overlay is drawn in OnGUI, after this point of the frame
            yield return new WaitForEndOfFrame();
            Texture2D shotTexture = null;
            try
            {
                shotTexture = ScreenCapture.CaptureScreenshotAsTexture();
            }
            finally
            {
                foreach (Canvas canvas in hidden)
                {
                    if (canvas != null) canvas.enabled = true;
                }

                if (overlay && overlayWorld != null) overlayWorld.ShowOverlay = true;
            }

            if (shotTexture == null) yield break;
            try
            {
                EndlessWorld world = EndlessWorld.Current;
                int seed = world != null ? world.Seed : 0;
                string name = fileName != null
                    ? $"single_{stamp}_s{seed}_s{Math.Round(stop.S)}m.png"
                    : ReviewTourPlan.FileName(stamp, seed, ++captured, stop, shot, lowSun, kind);
                string path = Path.Combine(Folder, name);
                File.WriteAllBytes(path, shotTexture.EncodeToPNG());
                File.WriteAllText(Path.ChangeExtension(path, ".json"), Sidecar(world, stop, shot, lowSun, shotTexture.width, shotTexture.height));
            }
            catch (Exception e)
            {
                Debug.LogError($"Review capture: could not save ({e.Message}).");
            }
            finally
            {
                Destroy(shotTexture);
            }
        }

        private string Sidecar(EndlessWorld world, ReviewStop stop, ReviewShot shot, bool lowSun, int width, int height)
        {
            Camera camera = rig != null ? rig.Camera : Camera.main;
            Vector3 cp = camera != null ? camera.transform.position : Vector3.zero;
            double cx = cp.x, cz = cp.z;
            if (world != null) (cx, cz) = world.ToAbsolute(cp);
            float mean = 0f;
            int n = Math.Min(frameCursor, frameMs.Length);
            for (int i = 0; i < n; i++) mean += frameMs[i] / Math.Max(1, n);
            RegionInfo region = world != null && world.Generator != null ? world.Generator.Region(stop.S) : default;
            WeatherSystem weather = WeatherSystem.Current;
            var fields = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("seed", world != null ? world.Seed : 0),
                new KeyValuePair<string, object>("roadDistance", world != null ? world.S : stop.S),
                new KeyValuePair<string, object>("tourKind", kind.ToString()),
                new KeyValuePair<string, object>("stop", stop.Label),
                new KeyValuePair<string, object>("shot", shot.ToString()),
                new KeyValuePair<string, object>("lowSun", lowSun),
                new KeyValuePair<string, object>("region", region.Name),
                new KeyValuePair<string, object>("regionKind", region.Kind.ToString()),
                new KeyValuePair<string, object>("hour", DayCycle.Current != null ? DayCycle.Current.Hour : float.NaN),
                new KeyValuePair<string, object>("weather", weather != null ? weather.State.ToString() : "unknown"),
                new KeyValuePair<string, object>("weatherRequested", weatherOption.ToString()),
                new KeyValuePair<string, object>("cameraAbsolute", new[] { cx, cp.y, cz }),
                new KeyValuePair<string, object>("cameraHeading", camera != null ? camera.transform.eulerAngles.y : 0f),
                new KeyValuePair<string, object>("cameraPitch", camera != null ? -Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x) : 0f),
                new KeyValuePair<string, object>("fieldOfView", camera != null ? camera.fieldOfView : 0f),
                new KeyValuePair<string, object>("safetyLevel", world != null ? world.SafetyLevel : -1),
                new KeyValuePair<string, object>("frameMs", mean),
                new KeyValuePair<string, object>("width", width),
                new KeyValuePair<string, object>("height", height),
                new KeyValuePair<string, object>("captured", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")),
            };
            ReviewTourPlan.StopFields(fields, stop, cx, cz, shot);
            // Performance pass: the stop's averages (frames measured at the settled shot, not the rolling "frameMs" above, which
            // also holds the travel and the previous captures' hitches) and the renderer's load.
            if (world != null) world.PerfFields(fields, world.Perf.Stop);
            // Stall hunt (2026-10-06): which measured frame was the worst, whether a garbage collection ran in the window, how the
            // managed heap moved, and how long setting up the shot took on the main thread (outside the window).
            fields.Add(new KeyValuePair<string, object>("stopWorstFrameMs", measureWorstMs));
            fields.Add(new KeyValuePair<string, object>("stopWorstFrameIndex", measureWorstIndex));
            ReviewWalkBench.CulpritFields(fields, "stopWorst", measureWorstScopes.a, measureWorstScopes.b, measureWorstScopes.c, measureWorstEngine);
            fields.Add(new KeyValuePair<string, object>("stopGcCollections", measureGcCollections));
            fields.Add(new KeyValuePair<string, object>("stopHeapMbChange", measureHeapMbChange));
            fields.Add(new KeyValuePair<string, object>("stopWorldBusy", measureWorldBusy));
            fields.Add(new KeyValuePair<string, object>("setShotMs", setShotMs));
            if (run != null && kind != ReviewTourKind.Single)
            {
                ReviewSight.Eye eye = stop.LandmarkEye ?? default;
                shotRecords.Add(new TourShotRecord
                {
                    Stop = run.Stops.IndexOf(stop), S = stop.S, Label = stop.Label, Region = region.Kind.ToString(), Shot = shot.ToString(), LowSun = lowSun,
                    FrameMsStop = (float)ReviewWalkBench.Number(fields, "frameMsStop"), GpuMs = (float)ReviewWalkBench.Number(fields, "gpuMs"),
                    PresentWaitMs = (float)ReviewWalkBench.Number(fields, "cpuPresentWaitMs"), TreeTriangles = (float)ReviewWalkBench.Number(fields, "treeVisibleTriangles"),
                    Landmark = stop.HasTarget ? stop.PlaceName ?? stop.Label : null, LandmarkVisible = stop.TargetVisible,
                    EyeRaise = eye.Raise, EyeShift = eye.Shift, EyeLateral = eye.Lateral,
                });
            }

            return ReviewTourPlan.Sidecar(fields);
        }

        // ------------------------------------------------------------------ restore

        private IEnumerator RestoreRoutine()
        {
            if (run == null || restoring) yield break;
            restoring = true;
            ReviewSnapshot snapshot = run.Snapshot;
            bool aborted = run.Aborted;
            WriteTourSummary(aborted);
            RestoreNow(true);
            EndlessWorld world = EndlessWorld.Current;
            if (world != null)
            {
                float began = Time.unscaledTime;
                while (world.IsBusy && Time.unscaledTime - began < ArrivalTimeout) yield return null;
                world.TravelToRoadDistance(snapshot.S, snapshot.Direction, true);
            }

            Toast(aborted ? "Review tour stopped" : $"Review tour done: {captured} captures");
            Debug.Log($"Review tour {(aborted ? "stopped" : "finished")}: {captured} captures in {Folder}");
            restoring = false;
        }

        /// <summary>Puts back time, camera, HUD and the cinematic camera (the place is restored by the routine when it can run).</summary>
        private void RestoreNow(bool keepRunForTravel)
        {
            if (run == null) return;
            ReviewSnapshot snapshot = run.Snapshot;
            run = null;
            ReleasePose();
            DayCycle cycle = DayCycle.Current;
            if (cycle != null)
            {
                cycle.SetHourOfDay(snapshot.Hour);
                cycle.TimeScale = snapshot.TimeScale;
            }

            if (rig != null)
            {
                rig.SetMode((CameraMode)snapshot.CameraMode, true);
                rig.SnapLook(snapshot.Yaw, snapshot.Pitch);
            }

            HudController hud = HudController.Instance;
            if (hud != null) hud.ShowHud = snapshot.ShowHud;
            EndlessWorld world = EndlessWorld.Current;
            if (world != null) world.ShowOverlay = snapshot.ShowOverlay;
            WeatherSystem weather = WeatherSystem.Current;
            WeatherState? forced = snapshot.ForcedWeather >= 0 ? (WeatherState)snapshot.ForcedWeather : (WeatherState?)null;
            if (weather != null && ReviewTourPlan.WeatherFor(weatherOption).HasValue && weather.ForcedState != forced)
            {
                weather.Force(forced);
                weather.SkipCrossFade();
            }

            if (cinematic != null)
            {
                cinematic.CinematicCamera = cinematicCamera;
                cinematic.CinematicWhenTravelling = cinematicTravelling;
            }
        }

        // ------------------------------------------------------------------ landmark view

        /// <summary>
        /// Chooses the landmark stop's road point: from the planner's best view (<see cref="Waypoint.ViewS"/>), the road point within
        /// reach from which the structure's middle and top clear the ground and the trees' crowns (built off the main thread from the
        /// generator's distant tiles). Records whether it is in sight at all (<c>landmarkVisible</c> in the sidecar).
        /// </summary>
        private IEnumerator FindLandmarkView(EndlessWorld world, ReviewStop stop)
        {
            IEndlessGenerator g = world.Generator;
            double from = double.IsNaN(stop.PlannedViewS) ? stop.S : stop.PlannedViewS;
            if (g is EndlessGenerator endless)
            {
                var sites = new List<LandmarkSite>();
                endless.Landmarks.Around(from - 2500.0, from + 2500.0, sites);
                foreach (LandmarkSite l in sites)
                {
                    if (Math.Abs(l.X - stop.TargetX) < 0.01 && Math.Abs(l.Z - stop.TargetZ) < 0.01) stop.TargetKind = l.Type.ToString();
                }
            }

            ReviewTourRun tour = run;
            Task<(ReviewTourPlan.LandmarkView view, ReviewCanopy canopy)> job = Task.Run(() =>
            {
                HashSet<TileCoord> tiles = ReviewCanopy.TilesFor(g.Road, stop.TargetX, stop.TargetZ, from, g.Params.tileSize);
                ReviewCanopy canopy = ReviewCanopy.Build(g, tiles, () => tour == null || tour.Aborted);
                return (ReviewTourPlan.BestLandmarkView(g.Road, g.Height, canopy.Blocks, stop.TargetX, stop.TargetZ, stop.TargetHeight,
                    stop.TargetStructureHeight, stop.TargetRadius, from), canopy);
            });
            Toast($"Review tour: looking for a view of {stop.PlaceName ?? "the landmark"}");
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted || job.IsCanceled)
            {
                Debug.LogWarning($"Review tour: sight check for {stop.Label} failed ({job.Exception?.GetBaseException().Message}); using the planner's view at {from:0} m.");
                stop.S = from;
                yield break;
            }

            ReviewTourPlan.LandmarkView view = job.Result.view;
            landmarkCanopies[stop] = job.Result.canopy;
            stop.S = view.S;
            stop.TargetVisible = view.Visible;
            if (view.Visible) Debug.Log($"Review tour: {stop.Label} ({stop.TargetKind}) in sight from {view.S:0} m, {view.Distance:0} m away (planner's view {from:0} m).");
            else Debug.LogWarning($"Review tour: {stop.Label} ({stop.TargetKind}) is hidden by the ground or the trees from every road point within {ReviewTourPlan.LandmarkSearchReach:0} m of {from:0} m; its shots say landmarkVisible false.");
        }

        /// <summary>
        /// The landmark shot's eye: the trees within <see cref="ReviewSight.Reach"/> (+ 20 m) of the stop, taken from the built
        /// tiles' blueprints (the placements the renderer draws), tested with the ground and the crown bands along both lines; the
        /// first clear eye of <see cref="ReviewSight.Choose"/>. None clear: the road centre and <c>landmarkVisible</c> false.
        /// </summary>
        private void ChooseLandmarkEye(EndlessWorld world, ReviewStop stop)
        {
            IEndlessGenerator g = world.Generator;
            RoadPoint at = g.Road(stop.S);
            float ts = g.Params.tileSize;
            double reach = ReviewSight.Reach + 20.0;
            var near = new List<ReviewSight.Tree>();
            var seen = new HashSet<TileCoord>();
            for (double dz = -reach; dz <= reach + 1e-6; dz += reach / 3.0)
            {
                for (double dx = -reach; dx <= reach + 1e-6; dx += reach / 3.0)
                {
                    TileCoord c = TileCoord.Containing(at.X + dx, at.Z + dz, ts);
                    if (!seen.Add(c)) continue;
                    ReviewSight.AddTrees(near, world.ActiveBlueprint(c), c.OriginX(ts), c.OriginZ(ts), at.X, at.Z, reach);
                }
            }

            landmarkCanopies.TryGetValue(stop, out ReviewCanopy canopy);
            Func<double, double, float, bool> blocks = canopy != null ? canopy.Blocks : (Func<double, double, float, bool>)null;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            ReviewSight.Eye eye = ReviewSight.Choose(g.Road, stop.S, (x, z, y) => ReviewSight.LandmarkClear(g.Height, blocks, near, x, z, y,
                stop.TargetX, stop.TargetZ, stop.TargetHeight, stop.TargetStructureHeight, stop.TargetRadius, aspect));
            stop.LandmarkEye = eye;
            stop.LandmarkNearTrees = near.Count;
            if (!eye.Found)
            {
                stop.TargetVisible = false;
                Debug.LogWarning($"Review tour: {stop.Label} is blocked from every eye at {stop.S:0} m (±10 m along, ±1.5 m across, up to 8 m higher; {near.Count} trees near the eye); landmarkVisible false.");
            }
            else if (eye.Raise > 0f || eye.Shift != 0f || eye.Lateral != 0f)
            {
                Debug.Log($"Review tour: {stop.Label}: a tree near the eye blocked the view; eye moved {eye.Shift:0} m along, {eye.Lateral:0.0} m across, raised {eye.Raise:0.0} m.");
            }
        }

        /// <summary>Writes <c>tour_&lt;stamp&gt;[_places]_summary.json</c> from the captures so far (also when stopped).</summary>
        private void WriteTourSummary(bool aborted)
        {
            if (shotRecords.Count == 0 || kind == ReviewTourKind.Single) return;
            try
            {
                EndlessWorld world = EndlessWorld.Current;
                WeatherSystem weather = WeatherSystem.Current;
                var head = new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("seed", world != null ? world.Seed : 0),
                    new KeyValuePair<string, object>("tourKind", kind.ToString()),
                    new KeyValuePair<string, object>("startDistance", tourStartS),
                    new KeyValuePair<string, object>("startReason", tourStartReason),
                    new KeyValuePair<string, object>("weatherRequested", weatherOption.ToString()),
                    new KeyValuePair<string, object>("weather", weather != null ? weather.State.ToString() : "unknown"),
                    new KeyValuePair<string, object>("stopped", aborted),
                    new KeyValuePair<string, object>("written", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")),
                };
                string path = Path.Combine(Folder, ReviewTourSummary.FileName(stamp, kind));
                File.WriteAllText(path, ReviewJson.Write(ReviewTourSummary.Build(head, shotRecords)));
                Debug.Log($"Review tour summary: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Review tour: could not write the summary ({e.Message}).");
            }

            shotRecords.Clear();
        }

        // ------------------------------------------------------------------ helpers

        private void Resolve()
        {
            PlayerController controller = FindFirstObjectByType<PlayerController>();
            traveller = controller != null ? controller.transform : null;
            rig = controller != null ? controller.GetComponent<PlayerCameraRig>() : FindFirstObjectByType<PlayerCameraRig>();
            cinematic = FindFirstObjectByType<CinematicDirector>();
        }

        private static int DirectionOf(EndlessWorld world)
        {
            Vector3 facing = world.Facing();
            RoadPoint road = world.Generator.Road(world.S);
            Vector3 along = Quaternion.Euler(0f, road.HeadingDeg, 0f) * Vector3.forward;
            return Vector3.Dot(facing, along) >= 0f ? 1 : -1;
        }

        private static void Toast(string message)
        {
            HudController hud = HudController.Instance;
            if (hud != null) hud.ShowToast(message);
        }
    }
}
