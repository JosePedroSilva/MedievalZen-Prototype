using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>The traveller, the sliding road window, inns, regions and the world's landmarks.</summary>
    public sealed partial class EndlessWorld
    {
        /// <summary>The road window reaches at least this far behind the traveller (metres along the road).</summary>
        private const double WindowBehind = 600.0;

        /// <summary>The road window reaches at least this far ahead of the traveller.</summary>
        private const double WindowAhead = 2600.0;

        /// <summary>The window is rebuilt when the traveller comes this close to either end.</summary>
        private const double WindowMargin = 300.0;

        /// <summary>A new region is announced once the traveller is this far inside it.</summary>
        private const double RegionHysteresis = 20.0;

        private double travellerS;
        private float nextTrack;
        private RoadWindow window;
        private Task<RoadWindow> windowJob;
        private double windowJobOriginX, windowJobOriginZ;
        private float nextWindowCheck;
        private Waypoint innAhead;
        private Waypoint innBehind;
        private double innCacheS = double.NaN;
        private float nextInnCheck;
        private Waypoint hudInn;
        private RegionInfo region;
        private bool regionKnown;
        private float nextNature;
        private float lastWater = float.NaN;
        private int lastLandmarkCount = -1;

        /// <summary>Raised when the traveller enters a new region (after the start).</summary>
        public event Action<RegionInfo> RegionEntered;

        /// <summary>Distance of the traveller along the road from the journey's start, in metres.</summary>
        public double S => travellerS;

        /// <summary>The region the traveller is in.</summary>
        public RegionInfo CurrentRegion => region;

        /// <summary>The name of the region the traveller is in, or empty before the first update.</summary>
        public string RegionName => regionKnown ? region.Name ?? string.Empty : string.Empty;

        /// <summary>The next inn along the road (increasing distance), or null.</summary>
        public Waypoint NextInn => innAhead;

        /// <summary>The previous inn along the road (decreasing distance), or null.</summary>
        public Waypoint PreviousInn => innBehind;

        /// <summary>The next inn in the direction the traveller faces (what T walks to), or null.</summary>
        public Waypoint InnAheadOfTraveller => hudInn;

        /// <summary>Metres along the road from the traveller to <paramref name="w"/> (always positive).</summary>
        public double RoadDistanceTo(Waypoint w) => w != null ? Math.Abs(w.S - travellerS) : double.NaN;

        /// <summary>The current road window (scene coordinates), or null before the first one is built.</summary>
        public RoadWindow Window => window;

        // ------------------------------------------------------------------ traveller

        private (double x, double z) TravellerAbsolute()
        {
            Vector3 p = traveller != null ? traveller.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            return origin.ToAbsolute(p);
        }

        /// <summary>Horizontal unit direction the traveller looks in (the camera's, else the body's).</summary>
        public Vector3 Facing()
        {
            Vector3 f = cameraRig != null ? cameraRig.PlanarForward : traveller != null ? traveller.forward : Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }

        private Vector3 TravelDirection()
        {
            if (motor != null)
            {
                Vector3 v = motor.Velocity;
                v.y = 0f;
                if (v.sqrMagnitude > 0.09f) return v.normalized;
            }

            return Facing();
        }

        private void TrackTraveller()
        {
            if (Time.unscaledTime < nextTrack) return;
            nextTrack = Time.unscaledTime + 0.1f;
            (double x, double z) = TravellerAbsolute();
            travellerS = generator.NearestS(x, z, travellerS);
        }

        /// <summary>Puts the traveller on the road at road distance <paramref name="s"/>, facing along it (<paramref name="direction"/> −1 = back).</summary>
        private void PlaceTravellerAt(double s, int direction)
        {
            RoadPoint r = generator.Road(s);
            Vector3 scene = origin.ToScene(r.X, generator.Height(r.X, r.Z) + 0.05, r.Z);
            float yaw = r.HeadingDeg + (direction < 0 ? 180f : 0f);
            travellerS = s;
            if (motor != null) motor.Teleport(scene, yaw);
            else if (traveller != null) traveller.SetPositionAndRotation(scene, Quaternion.Euler(0f, yaw, 0f));
            if (cameraRig != null) cameraRig.SnapYaw(yaw);
        }

        /// <summary>Moves the traveller to road distance <paramref name="s"/> anywhere on the road: the origin follows, the window and plan restart there.</summary>
        private void MoveTravellerTo(double s, int direction)
        {
            RoadPoint r = generator.Road(s);
            Vector3 delta = origin.DeltaToward(origin.ToScene(r.X, r.Z));
            if (delta.sqrMagnitude > 0f) ShiftOrigin(delta);
            PlaceTravellerAt(s, direction);
            innCacheS = double.NaN;
            UpdateInns(true);
            RebuildWindowNow();
            planX = double.NaN;
            PlanNow();
            UpdateNature(true);
        }

        // ------------------------------------------------------------------ inns and regions

        private void UpdateInns(bool force)
        {
            if (!force && Time.unscaledTime < nextInnCheck) return;
            nextInnCheck = Time.unscaledTime + 1f;
            bool crossed = (innAhead != null && travellerS >= innAhead.S - InnFinder.DefaultSkip) ||
                           (innBehind != null && travellerS <= innBehind.S + InnFinder.DefaultSkip);
            if (force || crossed || double.IsNaN(innCacheS) || Math.Abs(travellerS - innCacheS) > 400.0)
            {
                innCacheS = travellerS;
                innAhead = InnFinder.Find(generator, travellerS, +1);
                innBehind = InnFinder.Find(generator, travellerS, -1);
                Remember(innAhead);
                Remember(innBehind);
            }

            RoadPoint here = generator.Road(travellerS);
            hudInn = InnFinder.DirectionFor(Facing(), here.HeadingDeg) >= 0 ? innAhead : innBehind;
        }

        private void UpdateRegion()
        {
            RegionInfo r = generator.Region(travellerS);
            if (!regionKnown)
            {
                region = r;
                regionKnown = true;
                return;
            }

            if (r.Index == region.Index) return;
            if (travellerS - r.SStart < RegionHysteresis || r.SEnd - travellerS < RegionHysteresis) return;
            region = r;
            RegionEntered?.Invoke(r);
            HudController hud = HudController.Instance;
            if (hud != null && !startup) hud.ShowToast(EnterToast(r.Name));
        }

        /// <summary>"The Wexmoor Downs" → "You enter the Wexmoor Downs".</summary>
        public static string EnterToast(string regionName)
        {
            if (string.IsNullOrEmpty(regionName)) return "You enter new country";
            return regionName.StartsWith("The ", StringComparison.Ordinal) ? "You enter the " + regionName.Substring(4) : "You enter " + regionName;
        }

        // ------------------------------------------------------------------ road window

        private void WindowRange(out double a, out double b, double include = double.NaN)
        {
            a = travellerS - WindowBehind;
            b = travellerS + WindowAhead;
            if (innBehind != null) a = Math.Min(a, innBehind.S - 100.0);
            if (innAhead != null) b = Math.Max(b, innAhead.S + 100.0);
            if (!double.IsNaN(include))
            {
                a = Math.Min(a, include - 100.0);
                b = Math.Max(b, include + 100.0);
            }
        }

        private bool WindowStale()
        {
            if (window == null) return true;
            if (travellerS - window.SFrom < WindowMargin || window.STo - travellerS < WindowMargin) return true;
            if (innAhead != null && !window.Contains(innAhead.S + 50.0)) return true;
            if (innBehind != null && !window.Contains(innBehind.S - 50.0)) return true;
            return false;
        }

        private void UpdateRoadWindow()
        {
            if (startup && window != null) return;
            if (windowJob != null)
            {
                if (!windowJob.IsCompleted) return;
                Task<RoadWindow> job = windowJob;
                windowJob = null;
                if (job.Status != TaskStatus.RanToCompletion)
                {
                    Debug.LogError($"Endless road: building the road window failed: {job.Exception?.GetBaseException()}", this);
                    return;
                }

                if (windowJobOriginX != origin.X || windowJobOriginZ != origin.Z) return;   // origin moved meanwhile: rebuild
                ApplyWindow(window, job.Result, Vector3.zero);
                return;
            }

            UpdateInns(false);
            if (Time.unscaledTime < nextWindowCheck) return;
            nextWindowCheck = Time.unscaledTime + 0.5f;
            if (!WindowStale()) return;
            WindowRange(out double a, out double b);
            IEndlessGenerator gen = generator;
            double ox = origin.X, oz = origin.Z;
            windowJobOriginX = ox;
            windowJobOriginZ = oz;
            windowJob = Task.Run(() => RoadWindow.Build(gen, a, b, ox, oz));
        }

        /// <summary>Builds the window on the main thread right away (start, Go now, an inn outside the window).</summary>
        private void RebuildWindowNow(double include = double.NaN)
        {
            UpdateInns(true);
            WindowRange(out double a, out double b, include);
            windowJob = null;
            ApplyWindow(window, RoadWindow.Build(generator, a, b, origin.X, origin.Z), Vector3.zero);
        }

        /// <summary>
        /// Gives the GameWorld the new window and keeps an ongoing trip going to the same inn in it (re-routed in place, no
        /// stop, no announcement). <paramref name="delta"/> is non-zero when the window was moved by an origin shift.
        /// </summary>
        private void ApplyWindow(RoadWindow previous, RoadWindow next, Vector3 delta)
        {
            Waypoint destination = previous != null && travel != null ? previous.WaypointOf(travel.Destination) : null;
            window = next;
            world.SetRoads(next.Network);
            foreach (Settlement settlement in next.Network.Settlements) Remember(next.WaypointOf(settlement));
            if (travel == null) return;
            Settlement same = destination != null ? next.SettlementFor(destination.Id) : null;
            if (same == null && travel.IsTravelling) travel.Cancel();
            travel.ContinueTravel(same, delta);
        }

        // ------------------------------------------------------------------ travel (T)

        /// <inheritdoc />
        public Settlement PickDestination(PlayerTravel player, out string notice)
        {
            notice = null;
            if (startup || scripted || generator == null) return null;
            RoadPoint here = generator.Road(travellerS);
            int direction = InnFinder.DirectionFor(Facing(), here.HeadingDeg);
            Waypoint inn = InnFinder.Find(generator, travellerS, direction);
            if (inn == null)
            {
                notice = "No inn lies along the road this way.";
                return null;
            }

            Remember(inn);
            if (window == null || !window.Contains(inn.S)) RebuildWindowNow(inn.S);
            Settlement settlement = window.SettlementFor(inn.Id);
            if (settlement == null) notice = $"The road to {inn.Name} is not mapped out yet.";
            return settlement;
        }

        // ------------------------------------------------------------------ landmarks for the shared systems

        private void UpdateNature(bool force)
        {
            if (!force && Time.unscaledTime < nextNature) return;
            nextNature = Time.unscaledTime + 2f;
            var found = new List<Waypoint>();
            generator.Waypoints(travellerS - 1500.0, travellerS + 1500.0, found);
            var landmarks = new List<Landmark>();
            (double tx, double tz) = TravellerAbsolute();
            float water = WaterHeightAbsolute(tx, tz);
            double nearestLake = double.PositiveInfinity;
            foreach (Waypoint w in found)
            {
                LandmarkKind kind;
                switch (w.Kind)
                {
                    case WaypointKind.Lake: kind = LandmarkKind.Lake; break;
                    case WaypointKind.Pass: kind = LandmarkKind.Pass; break;
                    case WaypointKind.Viewpoint: kind = LandmarkKind.Viewpoint; break;
                    default: continue;
                }

                float level = kind == LandmarkKind.Lake ? generator.WaterHeight(w.X, w.Z) : -1f;
                float y = kind == LandmarkKind.Lake && level >= 0f ? level : w.Height;
                landmarks.Add(new Landmark { name = w.Name, kind = kind, position = origin.ToScene(w.X, y, w.Z), radius = w.Radius });
                double d = Math.Sqrt((w.X - tx) * (w.X - tx) + (w.Z - tz) * (w.Z - tz));
                if (kind == LandmarkKind.Lake && level >= 0f && water < 0f && d < 600.0 && d < nearestLake)
                {
                    nearestLake = d;
                    water = level;
                }
            }

            if (!force && landmarks.Count == lastLandmarkCount && Mathf.Approximately(water, lastWater) && landmarks.Count == 0) return;
            lastLandmarkCount = landmarks.Count;
            lastWater = water;
            world.SetNature(landmarks, water);
        }
    }
}
