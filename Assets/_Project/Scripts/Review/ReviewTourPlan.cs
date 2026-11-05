using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MedievalZen.Endless;
using MedievalZen.Environment;
using MedievalZen.World;

namespace MedievalZen.Review
{
    /// <summary>The three shots the review tour takes at every stop.</summary>
    public enum ReviewShot
    {
        /// <summary>The traveller's third-person view along the road.</summary>
        Road = 0,

        /// <summary>A wide shot at the most prominent distant terrain (the highest ground 450–1500 m away in the forward half-plane).</summary>
        Prominent = 1,

        /// <summary>A view across the valley: square to the road, toward the higher side.</summary>
        Side = 2,

        /// <summary>Settlements on the places tour: a raised three-quarter view of the whole place (<see cref="ReviewTourPlan.SettlementOverview"/>).</summary>
        Overview = 3,

        /// <summary>
        /// A walled city on the places tour: one street-level (or distant) view of <see cref="ReviewStop.View"/> (ramps, terrace edges,
        /// the view square, the castle's gate, court, flanks and back, the road at 150 m and 1 km).
        /// </summary>
        Street = 4,
    }

    /// <summary>Which capture produced a shot (written to the sidecar as <c>tourKind</c>).</summary>
    public enum ReviewTourKind
    {
        /// <summary><c>MedievalZen/Review/Capture Tour</c>: every 600 m plus the places.</summary>
        Tour = 0,

        /// <summary><c>MedievalZen/Review/Capture Places</c>: each inn, hamlet, village and landmark of the next 20 km.</summary>
        Places = 1,

        /// <summary>F9: one shot of the current view.</summary>
        Single = 2,
    }

    /// <summary>The weather a tour runs in (<c>MedievalZen/Review/Weather</c>); the values match <see cref="WeatherState"/>.</summary>
    public enum ReviewWeather
    {
        /// <summary>Leave the weather as it is.</summary>
        Keep = -1,

        /// <summary>Force clear.</summary>
        Clear = 0,

        /// <summary>Force hazy.</summary>
        Hazy = 1,

        /// <summary>Force overcast.</summary>
        Overcast = 3,

        /// <summary>Force light rain.</summary>
        Rain = 4,
    }

    /// <summary>One stop of the review tour: where the traveller stands (road distance, facing) and why.</summary>
    public sealed class ReviewStop
    {
        /// <summary>The shots taken here, in order (the first is always <see cref="ReviewShot.Road"/>).</summary>
        public ReviewShot[] Shots = ReviewTourPlan.Shots;

        /// <summary>Name of the place or landmark (null for regular stops).</summary>
        public string PlaceName;

        /// <summary>Settlements: absolute centre, ground height there, radius and road heading (for <see cref="ReviewShot.Overview"/>).</summary>
        public double PlaceX, PlaceZ;

        /// <summary>See <see cref="PlaceX"/>.</summary>
        public float PlaceHeight, PlaceRadius, PlaceRoadHeadingDeg;

        /// <summary>Landmarks: visible height of the structure above <see cref="TargetHeight"/>, metres.</summary>
        public float TargetStructureHeight;

        /// <summary>Landmarks: footprint radius, metres.</summary>
        public float TargetRadius;

        /// <summary>Landmarks: the planner's best-view road distance (<see cref="Waypoint.ViewS"/>), NaN when unknown.</summary>
        public double PlannedViewS = double.NaN;

        /// <summary>Landmarks: what it is (Tower, Chapel, ...), filled in by the runtime; null when unknown.</summary>
        public string TargetKind;

        /// <summary>Landmarks: whether the runtime's sight check (terrain and the trees' crowns) found it in view from the stop; null before the check.</summary>
        public bool? TargetVisible;

        /// <summary>Landmarks: the structure's height on a 1080 px frame in the framed shot (NaN before it is framed).</summary>
        public float TargetPixels = float.NaN;

        /// <summary>Landmarks: the eye chosen against the trees near it (<see cref="ReviewSight.Choose"/>); null before the shot.</summary>
        public ReviewSight.Eye? LandmarkEye;

        /// <summary>Landmarks: trees within reach of the eye that the near-tree test used (−1 before the shot).</summary>
        public int LandmarkNearTrees = -1;

        /// <summary>Road distance of the stop, metres.</summary>
        public double S;

        /// <summary>+1 facing the way the road runs, −1 facing back.</summary>
        public int Direction = 1;

        /// <summary>Short reason ("600 m", "inn The Grey Goose", "ford").</summary>
        public string Label;

        /// <summary>The waypoint the stop is for, if any.</summary>
        public WaypointKind? Kind;

        /// <summary>True when the wide shot should frame a thing off the road (a landmark) rather than the most prominent ground.</summary>
        public bool HasTarget;

        /// <summary>Absolute position and ground (for a landmark: floor) height of that thing (see <see cref="HasTarget"/>).</summary>
        public double TargetX, TargetZ;

        /// <summary>See <see cref="TargetX"/>.</summary>
        public float TargetHeight;

        /// <summary>A city's street-level stop: the view (<see cref="ReviewShot.Street"/>), null for every other stop.</summary>
        public MedievalZen.Endless.Generation.CityViewpoint? View;
    }

    /// <summary>Options of the review tour (defaults: every 600 m over the next 6 km, plus the places in that range, and a low sun).</summary>
    public sealed class ReviewTourOptions
    {
        /// <summary>Road distance between regular stops, metres.</summary>
        public double Spacing = 600.0;

        /// <summary>How far ahead the tour runs, metres.</summary>
        public double Length = 6000.0;

        /// <summary>Also stop at each inn, pass, ford, bridge, hamlet and village in the range.</summary>
        public bool Places = true;

        /// <summary>Repeat every stop's shots with a low sun (<see cref="ReviewTourPlan.LowSunHour"/>).</summary>
        public bool LowSun = true;

        /// <summary>Which tour: <see cref="ReviewTourKind.Places"/> has no regular stops and visits inns, hamlets, villages and landmarks only.</summary>
        public ReviewTourKind Kind = ReviewTourKind.Tour;

        /// <summary>Road distance the tour starts from; NaN = where the traveller stands.</summary>
        public double StartS = double.NaN;

        /// <summary>Why the tour starts where it does (printed in the first console line); null: from <see cref="StartS"/> or where the traveller stands.</summary>
        public string StartReason;

        /// <summary>The weather for the tour (put back afterwards).</summary>
        public ReviewWeather Weather = ReviewWeather.Clear;

        /// <summary>Add the raised <see cref="ReviewShot.Overview"/> at each inn, hamlet and village.</summary>
        public bool Overview;

        /// <summary>The places tour's options: the next 20 km, settlements and landmarks only, with the overview shot.</summary>
        public static ReviewTourOptions PlacesTour() => new ReviewTourOptions { Kind = ReviewTourKind.Places, Length = 20000.0, Overview = true };
    }

    /// <summary>One capture of the tour: a stop, a shot and whether it is the low-sun repeat.</summary>
    public struct ReviewJob
    {
        /// <summary>Index into the stops.</summary>
        public int Stop;

        /// <summary>Which shot.</summary>
        public ReviewShot Shot;

        /// <summary>True for the low-sun repeat.</summary>
        public bool LowSun;
    }

    /// <summary>
    /// Everything the tour restores when it ends, finishes or is interrupted: where the traveller was, the time and whether it
    /// ran, the camera mode and look, and whether the HUD was shown.
    /// </summary>
    public struct ReviewSnapshot
    {
        public double S;
        public int Direction;
        public float Hour;
        public float TimeScale;
        public int CameraMode;
        public float Yaw, Pitch;
        public bool ShowHud;

        /// <summary>The endless road's F3 overlay was on.</summary>
        public bool ShowOverlay;

        /// <summary>The weather's forced state before the tour (−1: following the schedule).</summary>
        public int ForcedWeather;
    }

    /// <summary>
    /// The pure part of the review capture tour (unit tested): which stops for a stretch of road, which shots per stop, the order
    /// of the captures, the camera targets, the file names and the JSON sidecar. No Unity calls.
    /// </summary>
    public static class ReviewTourPlan
    {
        /// <summary>Two stops closer than this (metres) are merged; a place's stop wins over a regular one.</summary>
        public const double MergeDistance = 120.0;

        /// <summary>A place's stop is this far before it (the traveller arrives as "Go now" does), metres.</summary>
        public const double PlaceLeadIn = 30.0;

        /// <summary>The waypoint kinds the tour stops at.</summary>
        public static bool IsTourPlace(WaypointKind kind) =>
            kind == WaypointKind.Inn || kind == WaypointKind.Pass || kind == WaypointKind.Ford || kind == WaypointKind.Bridge ||
            kind == WaypointKind.Hamlet || kind == WaypointKind.Village || kind == WaypointKind.City || kind == WaypointKind.Landmark;

        /// <summary>The waypoint kinds the places tour stops at: inns, hamlets, villages and landmarks.</summary>
        public static bool IsPlacesTourPlace(WaypointKind kind) =>
            kind == WaypointKind.Inn || kind == WaypointKind.Hamlet || kind == WaypointKind.Village || kind == WaypointKind.City || kind == WaypointKind.Landmark;

        /// <summary>The shots at a settlement on the places tour: the usual three, then the raised overview.</summary>
        public static readonly ReviewShot[] SettlementShots = { ReviewShot.Road, ReviewShot.Prominent, ReviewShot.Side, ReviewShot.Overview };

        /// <summary>
        /// The stops from road distance <paramref name="fromS"/> going <paramref name="direction"/>: every
        /// <see cref="ReviewTourOptions.Spacing"/> metres up to <see cref="ReviewTourOptions.Length"/>, plus (with
        /// <see cref="ReviewTourOptions.Places"/>) a stop <see cref="PlaceLeadIn"/> before each tour place in that range; stops within
        /// <see cref="MergeDistance"/> of each other are merged (the place keeps its label). In travelling order.
        /// </summary>
        public static List<ReviewStop> Stops(double fromS, int direction, ReviewTourOptions options, IReadOnlyList<Waypoint> waypoints)
        {
            options = options ?? new ReviewTourOptions();
            direction = direction >= 0 ? 1 : -1;
            var stops = new List<ReviewStop>();
            double spacing = Math.Max(50.0, options.Spacing), length = Math.Max(0.0, options.Length);
            bool placesTour = options.Kind == ReviewTourKind.Places;
            for (double d = spacing; !placesTour && d <= length + 1e-6; d += spacing)
            {
                stops.Add(new ReviewStop { S = fromS + direction * d, Direction = direction, Label = $"{d:0} m" });
            }

            if ((options.Places || placesTour) && waypoints != null)
            {
                foreach (Waypoint w in waypoints)
                {
                    if (w == null || !(placesTour ? IsPlacesTourPlace(w.Kind) : IsTourPlace(w.Kind))) continue;
                    double along = (w.S - fromS) * direction;
                    if (along <= PlaceLeadIn || along > length) continue;
                    var stop = new ReviewStop { S = w.S - direction * PlaceLeadIn, Direction = direction, Label = PlaceLabel(w), Kind = w.Kind, PlaceName = w.Name };
                    if (w.Kind == WaypointKind.Landmark)
                    {
                        // Landmarks stand 120-750 m off the road: the road shot never shows them, so the wide shot frames them, from
                        // the road point the landmark planner sees them best from (30 m before the planning point is often behind
                        // a wood or a crest). The runtime refines it with the trees' crowns before travelling there.
                        stop.HasTarget = true;
                        stop.TargetX = w.X;
                        stop.TargetZ = w.Z;
                        stop.TargetHeight = w.Height;
                        stop.TargetStructureHeight = w.StructureHeight > 0f ? w.StructureHeight : 8f;
                        stop.TargetRadius = w.Radius;
                        stop.PlannedViewS = w.ViewS;
                        if (!double.IsNaN(w.ViewS)) stop.S = w.ViewS;
                    }
                    else if (w.IsSettlement)
                    {
                        stop.PlaceX = w.X;
                        stop.PlaceZ = w.Z;
                        stop.PlaceHeight = w.Height;
                        stop.PlaceRadius = w.Radius;
                        stop.PlaceRoadHeadingDeg = w.RoadHeadingDeg;
                        if (options.Overview) stop.Shots = SettlementShots;
                    }

                    // A place replaces a regular stop near it; two places near each other both keep their stops.
                    int near = stops.FindIndex(x => x.Kind == null && Math.Abs(x.S - stop.S) < MergeDistance);
                    if (near >= 0) stops[near] = stop;
                    else stops.Add(stop);
                }
            }

            stops.Sort((a, b) => ((a.S - fromS) * direction).CompareTo((b.S - fromS) * direction));
            return stops;
        }

        private static string PlaceLabel(Waypoint w)
        {
            string kind = w.Kind.ToString().ToLowerInvariant();
            return string.IsNullOrEmpty(w.Name) ? kind : $"{kind} {w.Name}";
        }

        /// <summary>The shots taken at every stop, in order.</summary>
        public static readonly ReviewShot[] Shots = { ReviewShot.Road, ReviewShot.Prominent, ReviewShot.Side };

        /// <summary>Every capture of a tour of <paramref name="stopCount"/> stops: per stop the three shots, then (with a low sun) the three again.</summary>
        public static List<ReviewJob> Jobs(int stopCount, bool lowSun)
        {
            var jobs = new List<ReviewJob>();
            for (int s = 0; s < stopCount; s++)
            {
                foreach (ReviewShot shot in Shots) jobs.Add(new ReviewJob { Stop = s, Shot = shot });
                if (!lowSun) continue;
                foreach (ReviewShot shot in Shots) jobs.Add(new ReviewJob { Stop = s, Shot = shot, LowSun = true });
            }

            return jobs;
        }

        /// <summary>Every capture of a tour over <paramref name="stops"/>: per stop its own <see cref="ReviewStop.Shots"/>, then (with a low sun) those again.</summary>
        public static List<ReviewJob> Jobs(IReadOnlyList<ReviewStop> stops, bool lowSun)
        {
            var jobs = new List<ReviewJob>();
            for (int s = 0; s < (stops?.Count ?? 0); s++)
            {
                ReviewShot[] shots = stops[s]?.Shots ?? Shots;
                foreach (ReviewShot shot in shots) jobs.Add(new ReviewJob { Stop = s, Shot = shot });
                if (!lowSun) continue;
                foreach (ReviewShot shot in shots) jobs.Add(new ReviewJob { Stop = s, Shot = shot, LowSun = true });
            }

            return jobs;
        }

        // ------------------------------------------------------------------ landmark and settlement framing

        /// <summary>The camera's height above the road at a landmark stop, metres.</summary>
        public const float LandmarkEyeHeight = 2.4f;

        /// <summary>How far along the road either side of the planner's best view the runtime looks for a clear view, metres.</summary>
        public const double LandmarkSearchReach = 900.0;

        /// <summary>Spacing of the road points tried, metres.</summary>
        public const double LandmarkSearchStep = 15.0;

        /// <summary>Farthest a landmark is framed from, metres.</summary>
        public const double LandmarkMaxView = 1300.0;

        /// <summary>Share of the frame height the structure should fill (the field of view follows from it within its limits).</summary>
        public const double LandmarkFrameShare = 0.2;

        /// <summary>Narrowest and widest field of view of the landmark shot, degrees (vertical).</summary>
        public const float LandmarkMinFov = 5f, LandmarkMaxFov = 45f;

        /// <summary>The frame height the pixel estimate assumes (1080p).</summary>
        public const int ReferenceHeight = 1080;

        /// <summary>The landmark shot from one eye: where to look and with what field of view.</summary>
        public struct LandmarkFrame
        {
            /// <summary>View heading in degrees about Y (0 = +Z) and pitch in degrees (positive looks up).</summary>
            public float Heading, PitchUp;

            /// <summary>Vertical field of view, degrees.</summary>
            public float Fov;

            /// <summary>Horizontal distance from the eye to the structure's centre, metres.</summary>
            public double Distance;

            /// <summary>Height of the structure on a <see cref="ReferenceHeight"/> px frame, pixels.</summary>
            public float Pixels;
        }

        /// <summary>
        /// Frames a structure of height <paramref name="height"/> standing on a floor at (<paramref name="tx"/>, <paramref name="floor"/>,
        /// <paramref name="tz"/>) from the eye (<paramref name="ex"/>, <paramref name="ey"/>, <paramref name="ez"/>): aimed at its mid-height,
        /// with the field of view that makes it <see cref="LandmarkFrameShare"/> of the frame (clamped to
        /// <see cref="LandmarkMinFov"/>..<see cref="LandmarkMaxFov"/>), and its height on a 1080 px frame.
        /// </summary>
        public static LandmarkFrame FrameLandmark(double ex, double ey, double ez, double tx, float floor, double tz, float height)
        {
            double dx = tx - ex, dz = tz - ez;
            double d = Math.Max(1.0, Math.Sqrt(dx * dx + dz * dz));
            height = Math.Max(0.5f, height);
            double aim = Math.Atan2(floor + 0.5 * height - ey, d);
            double bottom = Math.Atan2(floor - ey, d), top = Math.Atan2(floor + height - ey, d);
            double angular = (top - bottom) * 180.0 / Math.PI;
            float fov = (float)Math.Max(LandmarkMinFov, Math.Min(LandmarkMaxFov, angular / LandmarkFrameShare));
            double half = Math.Tan(fov * 0.5 * Math.PI / 180.0);
            double pixels = ReferenceHeight * (Math.Tan(top - aim) - Math.Tan(bottom - aim)) / (2.0 * half);
            return new LandmarkFrame
            {
                Heading = (float)Bearing(ex, ez, tx, tz),
                PitchUp = (float)(aim * 180.0 / Math.PI),
                Fov = fov,
                Distance = d,
                Pixels = (float)pixels,
            };
        }

        /// <summary>Bearing from (ax, az) to (bx, bz), degrees in [0, 360) about Y (0 = +Z, 90 = +X).</summary>
        public static double Bearing(double ax, double az, double bx, double bz)
        {
            double deg = Math.Atan2(bx - ax, bz - az) * 180.0 / Math.PI;
            return deg < 0.0 ? deg + 360.0 : deg;
        }

        /// <summary>The road point the landmark shot is taken from, and whether the structure is in sight there.</summary>
        public struct LandmarkView
        {
            /// <summary>Road distance of the stop.</summary>
            public double S;

            /// <summary>True when the lines to its middle and its top clear the terrain and every crown.</summary>
            public bool Visible;

            /// <summary>Horizontal distance from the stop to the structure, metres.</summary>
            public double Distance;
        }

        /// <summary>
        /// The road point within <see cref="LandmarkSearchReach"/> of <paramref name="plannedS"/> (every <see cref="LandmarkSearchStep"/> m,
        /// at most <see cref="LandmarkMaxView"/> away) from which the structure looks largest with both lines of sight — from
        /// <see cref="LandmarkEyeHeight"/> above the road to its middle and to 90 % of its height — clear of the ground
        /// (<paramref name="height"/>, every 6 m) and of the crowns (<paramref name="canopyBlocks"/>; null: terrain only). Nearer
        /// the planned point wins ties. With no clear line anywhere: the planned point (or the waypoint's own) and Visible false.
        /// </summary>
        public static LandmarkView BestLandmarkView(Func<double, RoadPoint> road, Func<double, double, float> height, Func<double, double, float, bool> canopyBlocks,
            double tx, double tz, float floor, float structureHeight, float radius, double plannedS)
        {
            var fallback = new LandmarkView { S = plannedS, Visible = false };
            RoadPoint at = road(plannedS);
            fallback.Distance = Math.Sqrt((tx - at.X) * (tx - at.X) + (tz - at.Z) * (tz - at.Z));
            LandmarkView best = fallback;
            double bestScore = double.NegativeInfinity;
            for (double s = plannedS - LandmarkSearchReach; s <= plannedS + LandmarkSearchReach + 1e-6; s += LandmarkSearchStep)
            {
                RoadPoint r = road(s);
                double dx = tx - r.X, dz = tz - r.Z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d > LandmarkMaxView || d < radius + 20.0) continue;
                double score = structureHeight / Math.Max(d, 80.0) - Math.Abs(s - plannedS) * 1e-6;
                if (score <= bestScore) continue;
                float eye = r.Height + LandmarkEyeHeight;
                if (!Clear(height, canopyBlocks, r.X, r.Z, eye, tx, tz, floor + 0.5f * structureHeight, radius)) continue;
                if (!Clear(height, canopyBlocks, r.X, r.Z, eye, tx, tz, floor + 0.9f * structureHeight, radius)) continue;
                bestScore = score;
                best = new LandmarkView { S = s, Visible = true, Distance = d };
            }

            return best;
        }

        /// <summary>True when nothing (ground, or a crown) rises into the line from the eye to the target, 3 m from the eye to the target's footprint.</summary>
        public static bool Clear(Func<double, double, float> height, Func<double, double, float, bool> canopyBlocks, double ax, double az, float ay,
            double bx, double bz, float by, float radius)
        {
            double dx = bx - ax, dz = bz - az, d = Math.Sqrt(dx * dx + dz * dz);
            int n = Math.Max(4, (int)(d / 6.0));
            for (int i = 1; i < n; i++)
            {
                double t = (double)i / n, along = t * d;
                if (along < 3.0 || d - along < radius + 1.0) continue;
                double px = ax + dx * t, pz = az + dz * t;
                float line = ay + (by - ay) * (float)t;
                float h = height(px, pz);
                if (!float.IsNaN(h) && h > line - 0.3f) return false;
                if (canopyBlocks != null && canopyBlocks(px, pz, line)) return false;
            }

            return true;
        }

        /// <summary>
        /// The raised three-quarter view of a settlement: <see cref="OverviewDistance"/> radii from its centre, 45° back from the road's
        /// direction on the side whose ground is lower (so the eye is clear of the hillside), 25–40 m above the centre and at least
        /// 10 m above the ground under the eye; looking at the centre 2 m above its ground. <paramref name="height"/> may be null.
        /// </summary>
        public static void SettlementOverview(Func<double, double, float> height, double cx, double cz, float ground, float radius, float roadHeadingDeg,
            int direction, out double ex, out double ey, out double ez, out double lx, out double ly, out double lz)
        {
            double dist = Math.Max(45.0, OverviewDistance * Math.Max(1f, radius));
            double up = Math.Max(25.0, Math.Min(40.0, 0.4 * radius));
            double back = roadHeadingDeg + (direction < 0 ? 0.0 : 180.0);
            double bestX = cx, bestZ = cz;
            float bestGround = float.MaxValue;
            foreach (double turn in new[] { 45.0, -45.0 })
            {
                double rad = (back + turn) * Math.PI / 180.0;
                double x = cx + Math.Sin(rad) * dist, z = cz + Math.Cos(rad) * dist;
                float g = height != null ? height(x, z) : ground;
                if (float.IsNaN(g)) g = ground;
                if (g >= bestGround) continue;
                bestGround = g;
                bestX = x;
                bestZ = z;
            }

            ex = bestX;
            ez = bestZ;
            ey = Math.Max(ground + up, bestGround + 10.0);
            lx = cx;
            lz = cz;
            ly = ground + 2.0;
        }

        /// <summary>The overview's distance from the settlement's centre, in radii.</summary>
        public const double OverviewDistance = 1.2;

        /// <summary>
        /// The street-level stops of a city (one <see cref="ReviewShot.Street"/> shot each), after its own stop: labelled
        /// "city &lt;name&gt; &lt;view&gt;", standing at each view's road distance.
        /// </summary>
        public static List<ReviewStop> CityStops(Waypoint city, IReadOnlyList<MedievalZen.Endless.Generation.CityViewpoint> views)
        {
            var stops = new List<ReviewStop>();
            if (city == null || views == null) return stops;
            foreach (MedievalZen.Endless.Generation.CityViewpoint v in views)
            {
                stops.Add(new ReviewStop
                {
                    S = v.StandS, Direction = 1, Kind = WaypointKind.City, PlaceName = city.Name, PlaceX = city.X, PlaceZ = city.Z,
                    PlaceHeight = city.Height, PlaceRadius = city.Radius, PlaceRoadHeadingDeg = city.RoadHeadingDeg,
                    Label = $"city {city.Name} {v.Name}", Shots = StreetShots, View = v,
                });
            }

            return stops;
        }

        /// <summary>How far either way of its planned road distance a view from the road may move to see the castle past other places, metres.</summary>
        public const double RoadViewShift = 300.0;

        /// <summary>
        /// Where a view from the road stands (pure): the planned road distance <paramref name="want"/>, or the nearest one within
        /// <see cref="RoadViewShift"/> m (60 m steps) from which no other settlement (its disc grown by 40 m) lies within the first 80 % of
        /// the sight line from the road to (<paramref name="lookX"/>, <paramref name="lookZ"/>) (2026-10-09: the 1 km view of Durncaster
        /// framed a hamlet's two houses); <paramref name="want"/> when none is clear.
        /// </summary>
        public static double ClearRoadStand(Func<double, (double x, double z)> road, double want, double lookX, double lookZ, IReadOnlyList<Waypoint> places, Waypoint city)
        {
            if (road == null || places == null) return want;
            for (int k = 0; k <= (int)(RoadViewShift / 60.0) * 2; k++)
            {
                double s = want + ((k & 1) == 1 ? -1.0 : 1.0) * 60.0 * ((k + 1) / 2);
                (double ex, double ez) = road(s);
                double dx = lookX - ex, dz = lookZ - ez, l2 = dx * dx + dz * dz;
                bool blocked = false;
                foreach (Waypoint w in places)
                {
                    if (w == null || w == city || !w.IsSettlement || (city != null && w.Id == city.Id && w.Kind == city.Kind)) continue;
                    double t = l2 > 1e-9 ? ((w.X - ex) * dx + (w.Z - ez) * dz) / l2 : 0.0;
                    t = t < 0.0 ? 0.0 : t > 0.8 ? 0.8 : t;
                    double px = ex + dx * t - w.X, pz = ez + dz * t - w.Z;
                    if (px * px + pz * pz < (w.Radius + 40.0) * (w.Radius + 40.0)) { blocked = true; break; }
                }

                if (!blocked) return s;
            }

            return want;
        }

        /// <summary>
        /// True when the traveller (at road distance <paramref name="travellerS"/>) need not move for <paramref name="stop"/>: a city's
        /// street-level stop (not a view from the road) whose road distance is within 40 m (the near tiles then cover the whole town).
        /// </summary>
        public static bool AlreadyThere(ReviewStop stop, double travellerS) =>
            stop != null && stop.View.HasValue && !double.IsNaN(stop.View.Value.EyeX) && Math.Abs(travellerS - stop.S) < 40.0;

        /// <summary>The one shot of a city's street-level stop.</summary>
        public static readonly ReviewShot[] StreetShots = { ReviewShot.Street };

        /// <summary>The sidecar fields of a stop's place or landmark (appended to <paramref name="fields"/>; nothing for a regular stop).</summary>
        public static void StopFields(List<KeyValuePair<string, object>> fields, ReviewStop stop, double cameraX, double cameraZ, ReviewShot shot)
        {
            if (stop == null || fields == null) return;
            if (stop.View.HasValue)
            {
                fields.Add(new KeyValuePair<string, object>("cityShot", stop.View.Value.Name));
                fields.Add(new KeyValuePair<string, object>("cityLookAt", new[] { stop.View.Value.LookX, stop.View.Value.LookZ }));
            }

            if (stop.Kind.HasValue && !stop.HasTarget)
            {
                fields.Add(new KeyValuePair<string, object>("placeName", stop.PlaceName));
                fields.Add(new KeyValuePair<string, object>("placeKind", stop.Kind.Value.ToString()));
                if (stop.PlaceRadius > 0f) fields.Add(new KeyValuePair<string, object>("placeRadius", stop.PlaceRadius));
            }

            if (!stop.HasTarget) return;
            fields.Add(new KeyValuePair<string, object>("landmarkName", stop.PlaceName));
            fields.Add(new KeyValuePair<string, object>("landmarkKind", stop.TargetKind ?? "unknown"));
            double dx = stop.TargetX - cameraX, dz = stop.TargetZ - cameraZ;
            fields.Add(new KeyValuePair<string, object>("landmarkDistance", Math.Sqrt(dx * dx + dz * dz)));
            fields.Add(new KeyValuePair<string, object>("landmarkBearing", Bearing(cameraX, cameraZ, stop.TargetX, stop.TargetZ)));
            fields.Add(new KeyValuePair<string, object>("landmarkHeight", stop.TargetStructureHeight));
            fields.Add(new KeyValuePair<string, object>("landmarkVisible", stop.TargetVisible));
            fields.Add(new KeyValuePair<string, object>("landmarkPlannedViewS", stop.PlannedViewS));
            fields.Add(new KeyValuePair<string, object>("landmarkFramed", shot == ReviewShot.Prominent));
            if (shot == ReviewShot.Prominent) fields.Add(new KeyValuePair<string, object>("landmarkPixels1080", stop.TargetPixels));
            if (!stop.LandmarkEye.HasValue) return;
            ReviewSight.Eye eye = stop.LandmarkEye.Value;
            fields.Add(new KeyValuePair<string, object>("landmarkEyeRaise", eye.Raise));
            fields.Add(new KeyValuePair<string, object>("landmarkEyeShift", eye.Shift));
            fields.Add(new KeyValuePair<string, object>("landmarkEyeLateral", eye.Lateral));
            fields.Add(new KeyValuePair<string, object>("landmarkNearTrees", stop.LandmarkNearTrees));
        }

        /// <summary>The first console line of a tour: the start distance first and in bold, and why it starts there.</summary>
        public static string StartLine(string what, double fromS, double optionStartS, string reason) =>
            $"<b>Review {what} starts at {fromS:0} m</b> ({reason ?? (double.IsNaN(optionStartS) ? "from where the traveller stands" : "from 'Tour Start Distance...'")}).";

        /// <summary>The forced weather state for a tour option, or null to leave the weather as it is.</summary>
        public static WeatherState? WeatherFor(ReviewWeather weather) => weather == ReviewWeather.Keep ? (WeatherState?)null : (WeatherState)(int)weather;

        /// <summary>
        /// The afternoon hour at which the sun stands <paramref name="elevationDegrees"/> above the horizon on the day clock (bisection
        /// on <see cref="DayClock.SunElevation"/> between noon and sunset).
        /// </summary>
        /// <summary>
        /// Hour of the day shots (late morning). Tours used to take them at whatever hour the game happened to be, and two tours
        /// in a row were shot at dusk, where colours cannot be judged.
        /// </summary>
        public const float DayHour = 11.5f;

        public static float LowSunHour(float elevationDegrees = 10f)
        {
            float lo = 12f, hi = DayClock.Sunset;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (DayClock.SunElevation(mid) > elevationDegrees) lo = mid;
                else hi = mid;
            }

            return DayClock.ClockHour((lo + hi) * 0.5f, Season.Year);   // the sky's hour back to the clock (unchanged in spring and autumn)
        }

        /// <summary>
        /// The most prominent distant ground seen from (<paramref name="x"/>, <paramref name="z"/>) looking along
        /// <paramref name="headingDeg"/>: the highest point 450–1500 m away in the forward half-plane (±80°), sampled every 50 m and
        /// 4°. False when <paramref name="height"/> gives nothing.
        /// </summary>
        public static bool ProminentTarget(Func<double, double, float> height, double x, double z, double headingDeg, out double tx, out double tz, out float ty)
        {
            tx = tz = 0.0;
            ty = float.MinValue;
            bool found = false;
            for (double a = -80.0; a <= 80.0; a += 4.0)
            {
                double rad = (headingDeg + a) * Math.PI / 180.0, sx = Math.Sin(rad), sz = Math.Cos(rad);
                for (double d = 450.0; d <= 1500.0; d += 50.0)
                {
                    double px = x + sx * d, pz = z + sz * d;
                    float h = height(px, pz);
                    if (float.IsNaN(h) || h <= ty) continue;
                    ty = h;
                    tx = px;
                    tz = pz;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>How far from the road, along the stream, the crossing view stands, metres.</summary>
        public const double CrossingViewOut = 16.0;

        /// <summary>
        /// The view of a bridge or ford from beside the stream: the eye stands <see cref="CrossingViewOut"/> metres from the road
        /// on the lower (downstream) side and 7 m back along the road, at least 2.5 m above the ground there and 1.5 m above the
        /// road, and looks at the crossing just below road level, so an arch and the water under it are in the frame. The road shot
        /// 30 m before a bridge only shows its deck. (<paramref name="crossX"/>, <paramref name="crossZ"/>) is the road point at
        /// the crossing, <paramref name="direction"/> the way the tour travels (+1 or −1).
        /// </summary>
        public static void CrossingView(Func<double, double, float> height, double crossX, double crossZ, float crossHeight, double roadHeadingDeg,
            int direction, out double eyeX, out float eyeY, out double eyeZ, out double targetX, out float targetY, out double targetZ)
        {
            double along = roadHeadingDeg * Math.PI / 180.0;
            double ax = Math.Sin(along), az = Math.Cos(along);
            // The lower side at 20-60 m: where the stream runs to.
            float Mean(double sign)
            {
                float sum = 0f;
                int n = 0;
                for (double d = 20.0; d <= 60.0; d += 10.0)
                {
                    float h = height(crossX + az * sign * d, crossZ - ax * sign * d);
                    if (float.IsNaN(h)) continue;
                    sum += h;
                    n++;
                }

                return n > 0 ? sum / n : float.MaxValue;
            }

            double side = Mean(1.0) <= Mean(-1.0) ? 1.0 : -1.0;
            double back = direction >= 0 ? -7.0 : 7.0;
            eyeX = crossX + az * side * CrossingViewOut + ax * back;
            eyeZ = crossZ - ax * side * CrossingViewOut + az * back;
            float ground = height(eyeX, eyeZ);
            if (float.IsNaN(ground)) ground = crossHeight;
            eyeY = Math.Max(ground + 2.5f, crossHeight + 1.5f);
            targetX = crossX;
            targetZ = crossZ;
            targetY = crossHeight - 0.5f;
        }

        /// <summary>
        /// The side to look across the valley from: −90° or +90° from the road heading, toward the higher ground 200–1000 m away
        /// (mean of samples every 100 m). Returns the view heading in degrees.
        /// </summary>
        public static double SideHeading(Func<double, double, float> height, double x, double z, double headingDeg)
        {
            float Mean(double side)
            {
                double rad = (headingDeg + side) * Math.PI / 180.0;
                float sum = 0f;
                int n = 0;
                for (double d = 200.0; d <= 1000.0; d += 100.0)
                {
                    float h = height(x + Math.Sin(rad) * d, z + Math.Cos(rad) * d);
                    if (float.IsNaN(h)) continue;
                    sum += h;
                    n++;
                }

                return n > 0 ? sum / n : float.MinValue;
            }

            return Mean(90.0) >= Mean(-90.0) ? headingDeg + 90.0 : headingDeg - 90.0;
        }

        /// <summary>
        /// The file name (no folder) of a capture, e.g. <c>tour_20261002-1432_s12345_0007_s4200m_inn-the-grey-goose_prominent_low.png</c>:
        /// the run's stamp, the seed, the capture number, the road distance, the stop label, the shot and the light.
        /// </summary>
        public static string FileName(string stamp, int seed, int index, ReviewStop stop, ReviewShot shot, bool lowSun) =>
            FileName(stamp, seed, index, stop, shot, lowSun, ReviewTourKind.Tour);

        /// <summary>
        /// As above, with the tour kind after the stamp for the places tour (<c>tour_20261006-0930_places_s12345_0004_...</c>), so the
        /// names of every tour sort by run (the stamp) and then by capture (stops in travelling order).
        /// </summary>
        public static string FileName(string stamp, int seed, int index, ReviewStop stop, ReviewShot shot, bool lowSun, ReviewTourKind kind)
        {
            string label = Slug(stop?.Label ?? "shot");
            double s = stop?.S ?? 0.0;
            string tag = kind == ReviewTourKind.Places ? "_places" : string.Empty;
            return $"tour_{stamp}{tag}_s{seed}_{index:0000}_s{Math.Round(s).ToString(CultureInfo.InvariantCulture)}m_{label}_{shot.ToString().ToLowerInvariant()}{(lowSun ? "_low" : string.Empty)}.png";
        }

        /// <summary>A lower-case file-name-safe version of <paramref name="text"/> (letters, digits and dashes, at most 40 characters).</summary>
        public static string Slug(string text)
        {
            var b = new StringBuilder();
            bool dash = false;
            foreach (char c in text ?? string.Empty)
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    b.Append(char.ToLowerInvariant(c));
                    dash = false;
                }
                else if (!dash && b.Length > 0)
                {
                    b.Append('-');
                    dash = true;
                }

                if (b.Length >= 40) break;
            }

            return b.ToString().TrimEnd('-');
        }

        /// <summary>Performance pass: frames measured at each settled shot (and at least <see cref="MeasureSeconds"/>).</summary>
        public const int MeasureFrames = 30;

        /// <summary>Shortest measurement at a shot, seconds.</summary>
        public const float MeasureSeconds = 0.5f;

        /// <summary>Frames skipped before measuring (frame timings arrive a few frames late; the previous capture's hitch stays out).</summary>
        public const int MeasureSkipFrames = 4;

        /// <summary>True when a measurement of <paramref name="frames"/> frames over <paramref name="seconds"/> is long enough.</summary>
        public static bool MeasureDone(int frames, float seconds) => frames >= MeasureFrames && seconds >= MeasureSeconds;

        /// <summary>The JSON sidecar of a capture (hand-written, invariant culture; values as given).</summary>
        public static string Sidecar(IReadOnlyList<KeyValuePair<string, object>> fields)
        {
            var b = new StringBuilder("{\n");
            for (int i = 0; i < fields.Count; i++)
            {
                b.Append("  \"").Append(Escape(fields[i].Key)).Append("\": ").Append(Value(fields[i].Value));
                b.Append(i + 1 < fields.Count ? ",\n" : "\n");
            }

            return b.Append("}\n").ToString();
        }

        private static string Value(object v)
        {
            switch (v)
            {
                case null: return "null";
                case bool flag: return flag ? "true" : "false";
                case int n: return n.ToString(CultureInfo.InvariantCulture);
                case long n: return n.ToString(CultureInfo.InvariantCulture);
                case float f: return float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("0.###", CultureInfo.InvariantCulture);
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("0.###", CultureInfo.InvariantCulture);
                case float[] a:
                {
                    var parts = new string[a.Length];
                    for (int i = 0; i < a.Length; i++) parts[i] = a[i].ToString("0.###", CultureInfo.InvariantCulture);
                    return "[" + string.Join(", ", parts) + "]";
                }

                case double[] a:
                {
                    var parts = new string[a.Length];
                    for (int i = 0; i < a.Length; i++) parts[i] = a[i].ToString("0.###", CultureInfo.InvariantCulture);
                    return "[" + string.Join(", ", parts) + "]";
                }

                case System.Collections.IList list: return ReviewJson.ValueText(list);   // the culprit rows ({name, ms}); were written as the list's type name
                case IReadOnlyList<KeyValuePair<string, object>> obj: return ReviewJson.ValueText(obj);
                default: return "\"" + Escape(v.ToString()) + "\"";
            }
        }

        private static string Escape(string s)
        {
            var b = new StringBuilder();
            foreach (char c in s ?? string.Empty)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < 32) b.Append(' ');
                else b.Append(c);
            }

            return b.ToString();
        }
    }

    /// <summary>
    /// The tour as it runs (pure): the stops, the job list, a cursor, and the snapshot to put back. <see cref="Next"/> hands out the
    /// jobs in order; <see cref="Abort"/> stops it (no further jobs) and the caller restores <see cref="Snapshot"/> in both cases.
    /// </summary>
    public sealed class ReviewTourRun
    {
        private readonly List<ReviewJob> jobs;
        private int cursor;

        /// <summary>Starts a run over <paramref name="stops"/> that will restore <paramref name="snapshot"/>.</summary>
        public ReviewTourRun(List<ReviewStop> stops, bool lowSun, ReviewSnapshot snapshot)
        {
            Stops = stops ?? new List<ReviewStop>();
            jobs = ReviewTourPlan.Jobs(Stops, lowSun);
            Snapshot = snapshot;
        }

        /// <summary>The stops in travelling order.</summary>
        public List<ReviewStop> Stops { get; }

        /// <summary>What to put back at the end.</summary>
        public ReviewSnapshot Snapshot { get; }

        /// <summary>All jobs.</summary>
        public int JobCount => jobs.Count;

        /// <summary>Jobs handed out so far.</summary>
        public int Done => cursor;

        /// <summary>True once aborted.</summary>
        public bool Aborted { get; private set; }

        /// <summary>True when there is nothing more to do (all done or aborted).</summary>
        public bool Finished => Aborted || cursor >= jobs.Count;

        /// <summary>The next job, or false when finished.</summary>
        public bool Next(out ReviewJob job)
        {
            job = default;
            if (Finished) return false;
            job = jobs[cursor++];
            return true;
        }

        /// <summary>
        /// True when <paramref name="job"/> (the one <see cref="Next"/> just handed out) is the first job at its stop (the traveller has to be
        /// moved there first). By position, not by shot: a city's street-level stops have no road shot (2026-10-09: they were never
        /// travelled to, and the 1 km view was taken with the traveller still in the city).
        /// </summary>
        public bool StartsStop(ReviewJob job)
        {
            int at = cursor - 1;
            if (at < 0 || at >= jobs.Count || jobs[at].Stop != job.Stop || jobs[at].Shot != job.Shot || jobs[at].LowSun != job.LowSun)
            {
                at = jobs.FindIndex(j => j.Stop == job.Stop && j.Shot == job.Shot && j.LowSun == job.LowSun);
            }

            return at >= 0 && (at == 0 || jobs[at - 1].Stop != job.Stop);
        }

        /// <summary>Stops the run: no further jobs.</summary>
        public void Abort() => Aborted = true;
    }
    /// <summary>
    /// The trees' crowns round a landmark for the review tour's sight check (pure): from the generator's distant tiles (every tree,
    /// the same placements the renderer draws), each crown's solid band (from its crown base to 85 % of its height) over its
    /// radius on a 4 m grid, as the landmark harness measured them.
    /// </summary>
    public sealed class ReviewCanopy
    {
        private const double Cell = 4.0;
        private readonly Dictionary<(long, long), (float bottom, float top)> crowns = new Dictionary<(long, long), (float, float)>();

        /// <summary>Crown cells stored.</summary>
        public int Count => crowns.Count;

        /// <summary>Adds a crown at absolute (x, z) filling heights [bottom, top] over <paramref name="radius"/> metres.</summary>
        public void Add(double x, double z, float radius, float bottom, float top)
        {
            for (double dz = -radius; dz <= radius; dz += Cell)
            {
                for (double dx = -radius; dx <= radius; dx += Cell)
                {
                    if (dx * dx + dz * dz > radius * radius + Cell) continue;
                    var key = ((long)Math.Floor((x + dx) / Cell), (long)Math.Floor((z + dz) / Cell));
                    crowns[key] = crowns.TryGetValue(key, out var old) ? (Math.Min(old.bottom, bottom), Math.Max(old.top, top)) : (bottom, top);
                }
            }
        }

        /// <summary>True when a crown fills height <paramref name="y"/> at (x, z).</summary>
        public bool Blocks(double x, double z, float y) =>
            crowns.TryGetValue(((long)Math.Floor(x / Cell), (long)Math.Floor(z / Cell)), out var c) && y > c.bottom && y < c.top;

        /// <summary>
        /// The tiles crossed by the lines from the road (every 50 m within <see cref="ReviewTourPlan.LandmarkSearchReach"/> of
        /// <paramref name="plannedS"/>) to the landmark at (tx, tz).
        /// </summary>
        public static HashSet<TileCoord> TilesFor(Func<double, RoadPoint> road, double tx, double tz, double plannedS, float tileSize)
        {
            var tiles = new HashSet<TileCoord>();
            for (double s = plannedS - ReviewTourPlan.LandmarkSearchReach; s <= plannedS + ReviewTourPlan.LandmarkSearchReach + 1e-6; s += 50.0)
            {
                RoadPoint r = road(s);
                double dx = tx - r.X, dz = tz - r.Z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d > ReviewTourPlan.LandmarkMaxView) continue;
                int n = Math.Max(2, (int)(d / 40.0));
                for (int k = 0; k <= n; k++)
                {
                    tiles.Add(TileCoord.Containing(r.X + dx * k / n, r.Z + dz * k / n, tileSize));
                }
            }

            return tiles;
        }

        /// <summary>Builds the crowns of <paramref name="tiles"/> from the generator's distant tiles (slow: run it off the main thread).</summary>
        public static ReviewCanopy Build(IEndlessGenerator generator, IEnumerable<TileCoord> tiles, Func<bool> cancelled = null)
        {
            var canopy = new ReviewCanopy();
            float size = generator.Params.tileSize;
            foreach (TileCoord t in tiles)
            {
                if (cancelled != null && cancelled()) break;
                TileBlueprint bp = generator.GenerateTile(t, TileLod.Far);
                if (bp?.Trees == null) continue;
                double ox = t.OriginX(size), oz = t.OriginZ(size);
                foreach (TreePlacement tree in bp.Trees)
                {
                    FarTreeStyle st = FarTreeStyles.Defaults.Resolve(tree.role, tree.variant);
                    float k = tree.scale > 0f ? tree.scale : 1f;
                    float top = tree.position.y + st.Height * k * 0.85f;
                    float bottom = tree.position.y + Math.Max(1.2f, st.CrownBase * k);
                    canopy.Add(ox + tree.position.x, oz + tree.position.z, Math.Max(1.5f, st.Radius * k * 0.8f), bottom, top);
                }
            }

            return canopy;
        }
    }
}
