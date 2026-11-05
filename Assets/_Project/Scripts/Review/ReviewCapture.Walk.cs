using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MedievalZen.Cinematic;
using MedievalZen.Endless;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.UI;
using UnityEngine;

namespace MedievalZen.Review
{
    /// <summary>
    /// The walking benchmark (menu <c>MedievalZen/Review/Walk Benchmark</c>): the traveller auto-travels along the endless road
    /// from inn to inn, through <see cref="PlayerTravel"/> and <see cref="AutoTraveller"/> like the T key (the only movement path),
    /// at <see cref="AutoTraveller.DebugSpeedScale"/> times the auto-travel pace, with the screen visible, until the set distance is
    /// covered. Every frame is recorded (no captures during the run); streaming holds are recorded with their duration. Writes
    /// <c>ReviewShots/walk_*.json</c> and <c>.csv</c> (<see cref="ReviewWalkBench"/>). Esc or F9 stops it and still writes the files;
    /// the speed scale, light, weather, HUD, overlay, camera mode and pitch, cinematic camera and destination are put back.
    /// </summary>
    public sealed partial class ReviewCapture
    {
        private const float WalkRetrySeconds = 0.5f;
        private const float WalkGiveUpSeconds = 30f;

        private sealed class WalkRun
        {
            public WalkBenchOptions Options;
            public bool StopRequested, Recording;
            public string StopReason;
            public readonly List<WalkFrame> Frames = new List<WalkFrame>(32768);
            public readonly List<WalkHold> Holds = new List<WalkHold>();
            public readonly List<WalkPlace> Places = new List<WalkPlace>();
            public readonly List<object> Legs = new List<object>();
            public double StartS, TargetS, MaxS, RegionAtS = double.NaN;
            public string Region;
            public float Began, HoldBegan = -1f;
            public int Blocked, ScopeCalls;
            public int LastGcCount = -1;
            public string Stamp;

            // put back at the end
            public float Hour, TimeScale, Pitch;
            public int CameraMode, ForcedWeather;
            public bool ShowHud, ShowOverlay, CinematicCamera, CinematicTravelling;
            public Settlement Destination;
            public float HourAtStart;
            public string WeatherAtStart;
        }

        private WalkRun walk;
        private PlayerTravel travel;
        private AutoTraveller auto;

        /// <summary>Starts the walking benchmark (false when a tour or walk runs, there is no endless road, or no traveller).</summary>
        public bool StartWalk(WalkBenchOptions options)
        {
            EndlessWorld world = EndlessWorld.Current;
            if (run != null || walk != null || world == null || world.IsBusy || world.Generator == null) return false;
            Resolve();
            travel = traveller != null ? traveller.GetComponent<PlayerTravel>() : null;
            auto = travel != null ? travel.Traveller : null;
            if (traveller == null || rig == null || travel == null || auto == null) return false;
            options = options ?? new WalkBenchOptions();
            DayCycle cycle = DayCycle.Current;
            HudController hud = HudController.Instance;
            WeatherSystem weather = WeatherSystem.Current;
            walk = new WalkRun
            {
                Options = options,
                Stamp = DateTime.Now.ToString("yyyyMMdd-HHmm"),
                Hour = cycle != null ? cycle.Hour : 12f,
                TimeScale = cycle != null ? cycle.TimeScale : 1f,
                CameraMode = (int)rig.Mode,
                Pitch = rig.Pitch,
                ShowHud = hud == null || hud.ShowHud,
                ShowOverlay = world.ShowOverlay,
                ForcedWeather = weather != null && weather.ForcedState.HasValue ? (int)weather.ForcedState.Value : -1,
                Destination = travel.Destination,
            };
            if (travel.IsTravelling) travel.Cancel();
            world.ShowOverlay = false;
            if (hud != null) hud.ShowHud = false;
            WeatherState? wanted = ReviewTourPlan.WeatherFor(options.Weather);
            if (weather != null && wanted.HasValue)
            {
                weather.Force(wanted);
                weather.SkipCrossFade();
            }

            if (cinematic != null)
            {
                walk.CinematicCamera = cinematic.CinematicCamera;
                walk.CinematicTravelling = cinematic.CinematicWhenTravelling;
                cinematic.CinematicCamera = false;
                cinematic.CinematicWhenTravelling = false;
                if (cinematic.IsRunning) cinematic.ToggleManual();
            }

            if (cycle != null) cycle.TimeScale = 0f;   // the light stays as it is: the same sun over the whole walk
            auto.Blocked += OnWalkBlocked;
            StartCoroutine(Walk(world));
            return true;
        }

        private void OnWalkBlocked(RoadRoute route)
        {
            if (walk != null) walk.Blocked++;
        }

        private IEnumerator Walk(EndlessWorld world)
        {
            WalkBenchOptions o = walk.Options;
            if (!double.IsNaN(o.StartS))
            {
                bool arrived = false;
                if (!world.TravelToRoadDistance(o.StartS, 1, true, () => arrived = true))
                {
                    FinishWalk($"could not move to {o.StartS:0} m");
                    yield break;
                }

                float began = Time.unscaledTime;
                while (walk != null && !walk.StopRequested && (!arrived || world.IsBusy) && Time.unscaledTime - began < ArrivalTimeout) yield return null;
            }

            if (walk == null) yield break;
            rig.SetMode(CameraMode.ThirdPerson, true);
            rig.SnapLook(world.Generator.Road(world.S).HeadingDeg, 5f);
            yield return new WaitForSecondsRealtime(1.5f);   // trees, fog and the camera settle before the clock starts
            if (walk == null) yield break;

            walk.StartS = walk.MaxS = world.S;
            walk.TargetS = walk.StartS + o.Distance;
            var found = new List<Waypoint>();
            world.Generator.Waypoints(walk.StartS - ReviewWalkBench.PlaceReach, walk.TargetS + ReviewWalkBench.PlaceReach, found);
            foreach (Waypoint w in found)
            {
                switch (w.Kind)
                {
                    case WaypointKind.Inn:
                    case WaypointKind.Village:
                    case WaypointKind.City:
                    case WaypointKind.Hamlet:
                    case WaypointKind.Landmark:
                    case WaypointKind.Bridge:
                    case WaypointKind.Ford:
                        walk.Places.Add(new WalkPlace { Kind = w.Kind.ToString(), Name = w.Name, S = w.S });
                        break;
                }
            }

            DayCycle cycle = DayCycle.Current;
            WeatherSystem weather = WeatherSystem.Current;
            walk.HourAtStart = cycle != null ? cycle.Hour : float.NaN;
            walk.WeatherAtStart = weather != null ? weather.State.ToString() : "unknown";
            auto.DebugSpeedScale = Mathf.Max(0.1f, o.SpeedScale);
            Debug.Log($"<b>Walk benchmark: {o.Distance:0} m from {walk.StartS:0} m at x{o.SpeedScale:0.##}</b> (auto-travel from inn to inn, about {1.4f * o.SpeedScale:0.0} m/s); Esc or F9 stops it.");
            walk.Began = Time.unscaledTime;
            walk.Recording = true;
            float lastTry = -10f, lastMoving = Time.unscaledTime;
            float timeout = (float)(o.Distance / Math.Max(0.2, 1.4 * o.SpeedScale * 0.25)) + 120f;
            string reason = null;
            while (walk != null && reason == null)
            {
                double s = world.S;
                walk.MaxS = Math.Max(walk.MaxS, s);
                if (walk.StopRequested) reason = "stopped (Esc/F9)";
                else if (s >= walk.TargetS) reason = "done";
                else if (s < walk.MaxS - 50.0) reason = $"went back along the road ({s:0} m after {walk.MaxS:0} m): the destination picked was behind";
                else if (Time.unscaledTime - walk.Began > timeout) reason = $"timed out after {timeout:0} s";
                else if (travel.IsTravelling) lastMoving = Time.unscaledTime;
                else if (!world.IsHolding && Time.unscaledTime - lastTry >= WalkRetrySeconds)
                {
                    lastTry = Time.unscaledTime;
                    StartWalkLeg(world);
                    if (!travel.IsTravelling && Time.unscaledTime - lastMoving > WalkGiveUpSeconds) reason = "auto-travel would not start for 30 s (no inn ahead mapped out?)";
                }

                if (reason != null) break;
                // The camera looks where the body walks (smoothed), so every run sees the road ahead the same way.
                float yaw = traveller.eulerAngles.y;
                rig.SnapYaw(Mathf.LerpAngle(rig.Yaw, yaw, 1f - Mathf.Exp(-2.5f * Time.unscaledDeltaTime)));
                yield return null;
            }

            if (walk != null) FinishWalk(reason ?? "stopped");
        }

        /// <summary>Starts the next leg: faces along +s (the picker chooses the inn on the side the view faces) and presses T.</summary>
        private void StartWalkLeg(EndlessWorld world)
        {
            rig.SnapYaw(world.Generator.Road(world.S).HeadingDeg);
            if (!travel.IsTravelling) travel.ToggleAutoTravel();
            if (!travel.IsTravelling) return;
            walk.Legs.Add(new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("fromDistance", world.S),
                new KeyValuePair<string, object>("atSecond", Time.unscaledTime - walk.Began),
                new KeyValuePair<string, object>("destination", travel.Destination != null ? travel.Destination.Name : null),
                new KeyValuePair<string, object>("routeMetres", auto.RemainingDistance),
            });
        }

        /// <summary>Records this frame of the walk (after the world, the trees and the frame statistics have updated).</summary>
        private void LateUpdate()
        {
            if (walk == null || !walk.Recording) return;
            EndlessWorld world = EndlessWorld.Current;
            if (world == null) return;
            double s = world.S;
            if (double.IsNaN(walk.RegionAtS) || Math.Abs(s - walk.RegionAtS) > 10.0)
            {
                walk.RegionAtS = s;
                walk.Region = world.Generator != null ? world.Generator.Region(s).Kind.ToString() : "unknown";
            }

            float t = Time.unscaledTime - walk.Began;
            bool held = world.IsHolding;
            if (held && walk.HoldBegan < 0f)
            {
                walk.HoldBegan = t;
                walk.Holds.Add(new WalkHold { S = s, Start = t, Region = walk.Region });
                Debug.LogWarning($"Walk benchmark: the streamer holds the traveller at {s:0} m ({(world.IsOutrunHold ? "outrun" : "hold")}).");
            }
            else if (!held && walk.HoldBegan >= 0f)
            {
                CloseHold(walk, t);
            }

            FrameSample perf = world.Perf.Last;
            float slice = world.FrameWorstSliceMs;
            // The frame time covers the previous frame's work: its scopes, and its engine markers when it was slow (allocates then only).
            float frameMs = Time.unscaledDeltaTime * 1000f;
            FrameCost.TopOfLastFrame(out CostEntry scope1, out CostEntry scope2, out CostEntry scope3);
            walk.ScopeCalls = Math.Max(walk.ScopeCalls, FrameCost.ScopesLastFrame);
            string[] firsts = FrameCost.FirstsOfLastFrame();
            int gcCount = GC.CollectionCount(0);
            int gc = walk.LastGcCount >= 0 ? Math.Max(0, gcCount - walk.LastGcCount) : 0;
            walk.LastGcCount = gcCount;
            walk.Frames.Add(new WalkFrame
            {
                T = t, S = s, Region = walk.Region,
                FrameMs = frameMs,   // what the traveller feels; the split below lags a few frames (frame timings)
                Scope1 = scope1, Scope2 = scope2, Scope3 = scope3,
                Engine = frameMs > ReviewWalkBench.CulpritMs ? FrameCost.EngineOfLastFrame() : null,
                Firsts = firsts.Length > 0 ? firsts : null,
                Gc = gc,
                CpuMainMs = perf.CpuMainMs, RenderMs = perf.CpuRenderMs, GpuMs = perf.GpuMs, PresentWaitMs = perf.PresentWaitMs,
                ApplyMs = world.LastApplyMs, SliceMs = slice,
                Slice = slice > ReviewWalkBench.SliceLabelMs ? world.FrameWorstSlice : null,
                Held = held,
            });
        }

        // Takes the run: FinishWalk has already cleared the field when it closes the last hold (reading the field there threw,
        // which left the travel speed scaled and wrote no report: the first benchmark ever run, 2026-10-06).
        private static void CloseHold(WalkRun w, float t)
        {
            if (w == null) return;
            int last = w.Holds.Count - 1;
            if (last < 0 || w.HoldBegan < 0f) return;
            WalkHold h = w.Holds[last];
            h.Seconds = t - w.HoldBegan;
            w.Holds[last] = h;
            w.HoldBegan = -1f;
        }

        /// <summary>Ends the walk: puts everything back, then writes the JSON report and the CSV and logs the summary.</summary>
        private void FinishWalk(string reason)
        {
            WalkRun w = walk;
            if (w == null) return;
            walk = null;
            w.StopReason = reason;
            if (w.Recording) CloseHold(w, Time.unscaledTime - w.Began);
            w.Recording = false;

            if (auto != null)
            {
                auto.DebugSpeedScale = 1f;
                auto.Blocked -= OnWalkBlocked;
            }

            if (travel != null)
            {
                if (travel.IsTravelling) travel.Cancel();
                travel.SetDestination(w.Destination);
            }

            DayCycle cycle = DayCycle.Current;
            if (cycle != null)
            {
                cycle.SetHourOfDay(w.Hour);
                cycle.TimeScale = w.TimeScale;
            }

            if (rig != null)
            {
                rig.SetMode((CameraMode)w.CameraMode, true);
                rig.SnapLook(rig.Yaw, w.Pitch);
            }

            HudController hud = HudController.Instance;
            if (hud != null) hud.ShowHud = w.ShowHud;
            EndlessWorld world = EndlessWorld.Current;
            if (world != null) world.ShowOverlay = w.ShowOverlay;
            WeatherSystem weather = WeatherSystem.Current;
            WeatherState? forced = w.ForcedWeather >= 0 ? (WeatherState)w.ForcedWeather : (WeatherState?)null;
            if (weather != null && ReviewTourPlan.WeatherFor(w.Options.Weather).HasValue && weather.ForcedState != forced)
            {
                weather.Force(forced);
                weather.SkipCrossFade();
            }

            if (cinematic != null)
            {
                cinematic.CinematicCamera = w.CinematicCamera;
                cinematic.CinematicWhenTravelling = w.CinematicTravelling;
            }

            WriteWalk(w, world);
        }

        private void WriteWalk(WalkRun w, EndlessWorld world)
        {
            if (w.Frames.Count == 0)
            {
                Debug.LogWarning($"Walk benchmark: nothing recorded ({w.StopReason}).");
                return;
            }

            try
            {
                Directory.CreateDirectory(Folder);
                int seed = world != null ? world.Seed : 0;
                double endS = w.Frames[w.Frames.Count - 1].S;
                string name = ReviewWalkBench.FileName(w.Stamp, seed, w.StartS, endS, w.Options.SpeedScale);
                string path = Path.Combine(Folder, name);
                var head = new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("kind", "walk benchmark"),
                    new KeyValuePair<string, object>("seed", seed),
                    new KeyValuePair<string, object>("startDistance", w.StartS),
                    new KeyValuePair<string, object>("endDistance", endS),
                    new KeyValuePair<string, object>("distanceRequested", w.Options.Distance),
                    new KeyValuePair<string, object>("speedScale", w.Options.SpeedScale),
                    new KeyValuePair<string, object>("nominalMetresPerSecond", 1.4f * w.Options.SpeedScale),
                    new KeyValuePair<string, object>("stopReason", w.StopReason),
                    new KeyValuePair<string, object>("weatherRequested", w.Options.Weather.ToString()),
                    new KeyValuePair<string, object>("weather", w.WeatherAtStart),
                    new KeyValuePair<string, object>("hour", w.HourAtStart),
                    new KeyValuePair<string, object>("screen", new[] { (double)Screen.width, Screen.height }),
                    new KeyValuePair<string, object>("frameTimingSource", world != null ? world.Perf.Source : "none"),
                    new KeyValuePair<string, object>("safetyLevelAtEnd", world != null ? world.SafetyLevel : -1),
                    new KeyValuePair<string, object>("blockedLegs", w.Blocked),
                    new KeyValuePair<string, object>("legs", w.Legs),
                    new KeyValuePair<string, object>("frameCostScopesPerFrameMax", w.ScopeCalls),
                    new KeyValuePair<string, object>("frameCostMarkers", FrameCost.MarkerReport),
                    new KeyValuePair<string, object>("written", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")),
                };
                List<KeyValuePair<string, object>> report = ReviewWalkBench.Report(head, w.Frames, w.Holds, w.Places);
                File.WriteAllText(path, ReviewJson.Write(report));
                File.WriteAllText(Path.ChangeExtension(path, ".csv"), ReviewWalkBench.Csv(w.Frames));
                foreach (string line in ReviewWalkBench.SummaryLines(report, path)) Debug.Log(line);
                Toast($"Walk benchmark {w.StopReason}: {name}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Walk benchmark: could not write the report ({e.Message}).");
            }
        }
    }
}
