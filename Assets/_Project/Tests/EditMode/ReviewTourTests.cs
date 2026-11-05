using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Environment;
using MedievalZen.Review;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>The pure part of the review capture tour (<see cref="ReviewTourPlan"/>, <see cref="ReviewTourRun"/>).</summary>
    public sealed class ReviewTourTests
    {
        private static Waypoint Place(WaypointKind kind, double s, string name = null) => new Waypoint { Kind = kind, S = s, Name = name };

        [Test]
        public void StopsEverySpacingPlusThePlacesInRange()
        {
            var places = new List<Waypoint>
            {
                Place(WaypointKind.Inn, 1830.0, "The Grey Goose"),
                Place(WaypointKind.Milestone, 2500.0),
                Place(WaypointKind.Ford, 2410.0),
                Place(WaypointKind.Village, 4630.0, "Ashby"),
                Place(WaypointKind.Viewpoint, 3000.0),
                Place(WaypointKind.Inn, 7100.0, "Beyond the range"),
                Place(WaypointKind.Bridge, 900.0),   // behind the start
            };

            List<ReviewStop> stops = ReviewTourPlan.Stops(1000.0, +1, new ReviewTourOptions(), places);
            var expected = new List<(double s, string label)>
            {
                (1600.0, "600 m"), (1800.0, "inn the grey goose"), (2200.0, "1200 m"), (2380.0, "ford"), (2800.0, "1800 m"), (3400.0, "2400 m"),
                (4000.0, "3000 m"), (4600.0, "village ashby"), (5200.0, "4200 m"), (5800.0, "4800 m"), (6400.0, "5400 m"), (7000.0, "6000 m"),
            };

            Assert.That(stops.Count, Is.EqualTo(expected.Count), string.Join(", ", stops.ConvertAll(x => $"{x.S:0} {x.Label}")));
            for (int i = 0; i < stops.Count; i++)
            {
                Assert.That(stops[i].S, Is.EqualTo(expected[i].s).Within(1e-6), $"stop {i}");
                Assert.That(stops[i].Label.ToLowerInvariant(), Is.EqualTo(expected[i].label), $"stop {i}");
                Assert.That(stops[i].Direction, Is.EqualTo(1));
            }

            // The village replaced the regular stop 30 m from it; a place 30 m before the stop keeps the stop's distance as its own.
            Assert.That(stops.Exists(x => x.Kind == WaypointKind.Village), Is.True);

            // Facing back: the stops run the other way, places stop before them in that direction.
            List<ReviewStop> back = ReviewTourPlan.Stops(5000.0, -1, new ReviewTourOptions { Length = 1300.0, Places = true }, places);
            Assert.That(back.ConvertAll(x => x.S), Is.EqualTo(new List<double> { 4660.0, 4400.0, 3800.0 }));
            Assert.That(back[0].Kind, Is.EqualTo(WaypointKind.Village));
            Assert.That(back.TrueForAll(x => x.Direction == -1), Is.True);

            // Without places: only the regular stops.
            Assert.That(ReviewTourPlan.Stops(0.0, 1, new ReviewTourOptions { Places = false, Length = 1800.0 }, places).Count, Is.EqualTo(3));
        }

        [Test]
        public void LandmarkStopsCarryTheLandmarkAsTheWideShotTarget()
        {
            var places = new List<Waypoint>
            {
                new Waypoint { Kind = WaypointKind.Landmark, S = 1400.0, Name = "Ey Keep", X = 310.0, Z = 1520.0, Height = 87.5f, Radius = 9f, ViewS = 1655.0, StructureHeight = 20.7f },
                new Waypoint { Kind = WaypointKind.Landmark, S = 3300.0, Name = "No View", X = 500.0, Z = 3300.0, Height = 60f },   // ViewS unknown (NaN)
                Place(WaypointKind.Inn, 2600.0, "The Old Wheel"),
            };

            List<ReviewStop> stops = ReviewTourPlan.Stops(1000.0, +1, new ReviewTourOptions { Length = 3000.0 }, places);
            ReviewStop landmark = stops.Find(x => x.Kind == WaypointKind.Landmark);
            Assert.That(landmark, Is.Not.Null);
            Assert.That(landmark.HasTarget, Is.True);
            Assert.That(landmark.TargetX, Is.EqualTo(310.0));
            Assert.That(landmark.TargetZ, Is.EqualTo(1520.0));
            Assert.That(landmark.TargetHeight, Is.EqualTo(87.5f), "the floor of the structure: the framing aims above it");
            Assert.That(landmark.TargetStructureHeight, Is.EqualTo(20.7f));
            Assert.That(landmark.S, Is.EqualTo(1655.0), "the stop is the planner's best view, not 30 m before the planning point");
            Assert.That(landmark.PlannedViewS, Is.EqualTo(1655.0));
            Assert.That(landmark.PlaceName, Is.EqualTo("Ey Keep"));
            Assert.That(landmark.TargetVisible.HasValue, Is.False, "the runtime's sight check has not run yet");
            ReviewStop noView = stops.Find(x => x.PlaceName == "No View");
            Assert.That(noView.S, Is.EqualTo(3270.0), "without a planned view: 30 m before the planning point");
            Assert.That(noView.TargetStructureHeight, Is.GreaterThan(0f), "a fallback height to frame");
            Assert.That(stops.Find(x => x.Kind == WaypointKind.Inn).HasTarget, Is.False, "only landmarks are framed off the road");
            Assert.That(stops.TrueForAll(x => !x.HasTarget || x.Kind == WaypointKind.Landmark), Is.True);
        }

        [Test]
        public void ThePlacesTourVisitsSettlementsAndLandmarksOnlyWithAnOverviewOfEachSettlement()
        {
            var places = new List<Waypoint>
            {
                new Waypoint { Kind = WaypointKind.Inn, S = 1830.0, Name = "The Grey Goose", X = 10.0, Z = 1830.0, Height = 50f, Radius = 35f, RoadHeadingDeg = 5f },
                Place(WaypointKind.Ford, 2410.0),
                Place(WaypointKind.Pass, 5000.0, "Col"),
                new Waypoint { Kind = WaypointKind.Village, S = 11865.0, Name = "Hartdenfield", X = 0.0, Z = 11865.0, Height = 120f, Radius = 90f },
                new Waypoint { Kind = WaypointKind.Hamlet, S = 11900.0, Name = "Close By", Radius = 45f },   // two places near each other both stay
                new Waypoint { Kind = WaypointKind.Landmark, S = 4375.0, Name = "St Aldo's", X = 343.0, Z = 4155.0, Height = 209.7f, ViewS = 4100.0, StructureHeight = 9.2f },
                Place(WaypointKind.Village, 21500.0, "Too far"),
            };

            ReviewTourOptions options = ReviewTourOptions.PlacesTour();
            Assert.That(options.Kind, Is.EqualTo(ReviewTourKind.Places));
            Assert.That(options.Length, Is.EqualTo(20000.0));
            Assert.That(options.Weather, Is.EqualTo(ReviewWeather.Clear), "tours run in clear weather unless told otherwise");
            List<ReviewStop> stops = ReviewTourPlan.Stops(1000.0, +1, options, places);
            Assert.That(stops.ConvertAll(x => x.PlaceName), Is.EqualTo(new List<string> { "The Grey Goose", "St Aldo's", "Hartdenfield", "Close By" }));
            Assert.That(stops.TrueForAll(x => x.Kind.HasValue), Is.True, "no regular 600 m stops");
            Assert.That(stops[0].Shots, Is.EqualTo(ReviewTourPlan.SettlementShots));
            Assert.That(stops[0].Shots[0], Is.EqualTo(ReviewShot.Road), "the road shot comes first: the traveller moves before it");
            Assert.That(stops[1].Shots, Is.EqualTo(ReviewTourPlan.Shots), "no overview at a landmark");
            Assert.That(stops[0].PlaceRadius, Is.EqualTo(35f));
            Assert.That(stops[0].PlaceHeight, Is.EqualTo(50f));

            List<ReviewJob> jobs = ReviewTourPlan.Jobs(stops, true);
            Assert.That(jobs.Count, Is.EqualTo(2 * (4 + 3 + 4 + 4)));
            var run = new ReviewTourRun(stops, false, default);
            int moves = 0, overviews = 0;
            while (run.Next(out ReviewJob job))
            {
                if (run.StartsStop(job)) moves++;
                if (job.Shot == ReviewShot.Overview) overviews++;
            }

            Assert.That(moves, Is.EqualTo(4));
            Assert.That(overviews, Is.EqualTo(3));

            // The regular tour keeps its three shots per stop (no overview) and starts from a given road distance.
            List<ReviewStop> tour = ReviewTourPlan.Stops(11000.0, +1, new ReviewTourOptions { Length = 1200.0 }, places);
            Assert.That(tour.TrueForAll(x => x.Shots.Length == 3), Is.True);
            Assert.That(tour.Exists(x => x.PlaceName == "Hartdenfield"), Is.True);
            Assert.That(double.IsNaN(new ReviewTourOptions().StartS), Is.True, "default: from where the traveller stands");
        }

        [Test]
        public void TheLandmarkShotFramesTheStructureLargeEnough()
        {
            // St Aldo's (seed 12345): a 9.2 m chapel 270 m away, its floor 46 m above the eye.
            ReviewTourPlan.LandmarkFrame f = ReviewTourPlan.FrameLandmark(71.3, 163.5, 4138.6, 343.0, 209.7f, 4155.0, 9.2f);
            Assert.That(f.Distance, Is.EqualTo(272.2).Within(1.0));
            Assert.That(f.Heading, Is.EqualTo(86.5f).Within(0.5f));
            Assert.That(f.PitchUp, Is.EqualTo(Math.Atan2(209.7 + 4.6 - 163.5, f.Distance) * 180.0 / Math.PI).Within(0.01), "aimed at mid-height");
            Assert.That(f.Pixels, Is.GreaterThanOrEqualTo(60f), "at least 60 px tall at 1080p");
            Assert.That(f.Fov, Is.InRange(ReviewTourPlan.LandmarkMinFov, ReviewTourPlan.LandmarkMaxFov));

            // A tall tower close by: the widest field of view, still in frame (under the full frame height).
            ReviewTourPlan.LandmarkFrame near = ReviewTourPlan.FrameLandmark(0.0, 102.4, 0.0, 0.0, 100f, 130.0, 20.7f);
            Assert.That(near.Fov, Is.EqualTo(ReviewTourPlan.LandmarkMaxFov));
            Assert.That(near.Pixels, Is.InRange(60f, 1080f));
            Assert.That(near.Heading, Is.EqualTo(0f).Within(1e-3f));

            // Far stones: the narrowest field of view.
            ReviewTourPlan.LandmarkFrame far = ReviewTourPlan.FrameLandmark(0.0, 50.0, 0.0, 1200.0, 50f, 0.0, 4.5f);
            Assert.That(far.Fov, Is.EqualTo(ReviewTourPlan.LandmarkMinFov));
            Assert.That(far.Heading, Is.EqualTo(90f).Within(1e-3f));
            Assert.That(ReviewTourPlan.Bearing(0.0, 0.0, -1.0, 0.0), Is.EqualTo(270.0).Within(1e-9));
        }

        [Test]
        public void TheLandmarkStopIsWhereTheStructureCanBeSeen()
        {
            // A straight road along +Z on flat ground at 0; a 10 m tower 300 m to the east of s = 1000.
            RoadPoint Road(double s) => new RoadPoint { S = s, X = 0.0, Z = s, Height = 0f };
            float Flat(double x, double z) => 0f;
            // A wood (crowns 3-15 m) east of the road between z = 800 and 1150: the planned point (1000) sees nothing.
            bool Wood(double x, double z, float y) => x > 20.0 && x < 120.0 && z > 800.0 && z < 1150.0 && y > 3f && y < 15f;

            ReviewTourPlan.LandmarkView open = ReviewTourPlan.BestLandmarkView(Road, Flat, null, 300.0, 1000.0, 0f, 10f, 9f, 1000.0);
            Assert.That(open.Visible, Is.True);
            Assert.That(open.S, Is.EqualTo(1000.0).Within(1e-6), "with nothing in the way: the nearest point (the largest view)");
            Assert.That(open.Distance, Is.EqualTo(300.0).Within(1e-6));

            ReviewTourPlan.LandmarkView wood = ReviewTourPlan.BestLandmarkView(Road, Flat, Wood, 300.0, 1000.0, 0f, 10f, 9f, 1000.0);
            Assert.That(wood.Visible, Is.True);
            Assert.That(wood.S < 800.0 || wood.S > 1150.0, Is.True, $"the stop must leave the wood behind ({wood.S:0} m)");
            Assert.That(ReviewTourPlan.Clear(Flat, Wood, 0.0, wood.S, ReviewTourPlan.LandmarkEyeHeight, 300.0, 1000.0, 5f, 9f), Is.True);

            // A ridge 10 m high between the road and the tower everywhere: hidden; the planned point is kept and flagged.
            float Ridge(double x, double z) => x > 100.0 && x < 140.0 ? 10f : 0f;
            ReviewTourPlan.LandmarkView hidden = ReviewTourPlan.BestLandmarkView(Road, Ridge, null, 300.0, 1000.0, 0f, 10f, 9f, 1000.0);
            Assert.That(hidden.Visible, Is.False);
            Assert.That(hidden.S, Is.EqualTo(1000.0));

            // The canopy grid: a crown blocks only within its band and radius.
            var canopy = new ReviewCanopy();
            canopy.Add(100.0, 100.0, 4f, 3f, 12f);
            Assert.That(canopy.Blocks(101.0, 99.0, 6f), Is.True);
            Assert.That(canopy.Blocks(101.0, 99.0, 2f), Is.False, "under the crown");
            Assert.That(canopy.Blocks(101.0, 99.0, 13f), Is.False, "over the crown");
            Assert.That(canopy.Blocks(120.0, 100.0, 6f), Is.False, "beside it");
        }

        [Test]
        public void TheSettlementOverviewIsARaisedThreeQuarterViewFromTheLowerSide()
        {
            // A village of radius 80 at the origin, the road heading +Z, the ground rising to the east.
            float East(double x, double z) => (float)(x * 0.2);
            ReviewTourPlan.SettlementOverview(East, 0.0, 0.0, 0f, 80f, 0f, 1, out double ex, out double ey, out double ez, out double lx, out double ly, out double lz);
            Assert.That(Math.Sqrt(ex * ex + ez * ez), Is.EqualTo(96.0).Within(1e-6), "1.2 radii from the centre");
            Assert.That(ez, Is.LessThan(0.0), "behind the centre, the way the traveller comes from");
            Assert.That(ex, Is.LessThan(0.0), "on the lower (west) side");
            Assert.That(ey, Is.InRange(25.0, 40.0));
            Assert.That(ey - East(ex, ez), Is.GreaterThanOrEqualTo(10.0));
            Assert.That((lx, ly, lz), Is.EqualTo((0.0, 2.0, 0.0)), "looking at the centre");

            // A small inn: at least 45 m away and 25 m up.
            ReviewTourPlan.SettlementOverview(null, 0.0, 0.0, 100f, 20f, 90f, 1, out ex, out ey, out ez, out _, out _, out _);
            Assert.That(Math.Sqrt(ex * ex + ez * ez), Is.EqualTo(45.0).Within(1e-6));
            Assert.That(ey, Is.EqualTo(125.0).Within(1e-6));
        }

        [Test]
        public void TheCrossingViewStandsBesideTheStreamOnTheLowerSideAndLooksAtTheCrossing()
        {
            // A road running north (+z) over ground that falls toward +x: the stream runs east, the view stands east of the road.
            Func<double, double, float> ground = (x, z) => 100f - 0.05f * (float)x;
            ReviewTourPlan.CrossingView(ground, 0.0, 500.0, 100f, 0.0, +1, out double ex, out float ey, out double ez, out double tx, out float ty, out double tz);
            Assert.That(ex, Is.EqualTo(ReviewTourPlan.CrossingViewOut).Within(1e-6), "on the lower side, along the stream");
            Assert.That(ez, Is.EqualTo(493.0).Within(1e-6), "a little before the crossing, the way the tour comes");
            Assert.That(ey, Is.GreaterThanOrEqualTo(ground(ex, ez) + 2.5f - 1e-4f));
            Assert.That(ey, Is.GreaterThanOrEqualTo(101.5f - 1e-4f), "above the road, so the deck does not hide the far bank");
            Assert.That(tx, Is.EqualTo(0.0));
            Assert.That(tz, Is.EqualTo(500.0));
            Assert.That(ty, Is.LessThan(100f), "aimed just below road level: at the arch and the water");

            // Travelling the other way the eye stands on the other side of the crossing along the road; the lower side is the same.
            ReviewTourPlan.CrossingView(ground, 0.0, 500.0, 100f, 0.0, -1, out ex, out ey, out ez, out tx, out ty, out tz);
            Assert.That(ex, Is.EqualTo(ReviewTourPlan.CrossingViewOut).Within(1e-6));
            Assert.That(ez, Is.EqualTo(507.0).Within(1e-6));
        }

        [Test]
        public void JobsAreThreeShotsPerStopThenTheLowSunRepeat()
        {
            List<ReviewJob> jobs = ReviewTourPlan.Jobs(2, true);
            Assert.That(jobs.Count, Is.EqualTo(12));
            Assert.That(jobs[0].Shot, Is.EqualTo(ReviewShot.Road));
            Assert.That(jobs[1].Shot, Is.EqualTo(ReviewShot.Prominent));
            Assert.That(jobs[2].Shot, Is.EqualTo(ReviewShot.Side));
            Assert.That(jobs[3].LowSun && jobs[3].Shot == ReviewShot.Road && jobs[3].Stop == 0, Is.True);
            Assert.That(jobs[6].Stop, Is.EqualTo(1));
            Assert.That(ReviewTourPlan.Jobs(3, false).Count, Is.EqualTo(9));

            float low = ReviewTourPlan.LowSunHour();
            Assert.That(DayClock.SunElevation(low), Is.EqualTo(10f).Within(0.05f));
            Assert.That(low, Is.InRange(12f, DayClock.Sunset));
        }

        [Test]
        public void ARunHandsOutItsJobsInOrderAndStopsWhenAborted()
        {
            var stops = new List<ReviewStop> { new ReviewStop { S = 100, Label = "a" }, new ReviewStop { S = 700, Label = "b" } };
            var snapshot = new ReviewSnapshot { S = 42.0, Direction = -1, Hour = 9.5f, TimeScale = 1f, CameraMode = 1, Yaw = 30f, Pitch = 5f, ShowHud = true };
            var run = new ReviewTourRun(stops, false, snapshot);
            Assert.That(run.JobCount, Is.EqualTo(6));
            var moves = 0;
            var seen = new List<ReviewJob>();
            while (run.Next(out ReviewJob job))
            {
                if (run.StartsStop(job)) moves++;
                seen.Add(job);
                if (seen.Count == 4) run.Abort();
            }

            Assert.That(seen.Count, Is.EqualTo(4), "nothing after the abort");
            Assert.That(moves, Is.EqualTo(2), "the traveller moves once per stop");
            Assert.That(run.Finished && run.Aborted, Is.True);
            Assert.That(run.Next(out _), Is.False);
            Assert.That(run.Snapshot.S, Is.EqualTo(42.0), "the snapshot to put back is the one taken at the start");
            Assert.That(run.Snapshot.Hour, Is.EqualTo(9.5f));
            Assert.That(run.Snapshot.Direction, Is.EqualTo(-1));

            var full = new ReviewTourRun(stops, true, snapshot);
            int n = 0;
            while (full.Next(out _)) n++;
            Assert.That(n, Is.EqualTo(12));
            Assert.That(full.Finished && !full.Aborted, Is.True);
        }

        [Test]
        public void TargetsAndNamesAreWhatTheShotsNeed()
        {
            // A cone of height 200 at (300, 900) on flat ground at 50: the prominent target is its top; the side view looks toward it.
            float Height(double x, double z)
            {
                double d = Math.Sqrt((x - 300.0) * (x - 300.0) + (z - 900.0) * (z - 900.0));
                return 50f + (float)Math.Max(0.0, 200.0 - d * 0.4);
            }

            Assert.That(ReviewTourPlan.ProminentTarget(Height, 0.0, 0.0, 0.0, out double tx, out double tz, out float ty), Is.True);
            Assert.That(Math.Sqrt((tx - 300) * (tx - 300) + (tz - 900) * (tz - 900)), Is.LessThan(60.0));
            Assert.That(ty, Is.GreaterThan(220f));
            Assert.That(ReviewTourPlan.SideHeading(Height, 0.0, 900.0, 0.0), Is.EqualTo(90.0), "the higher side (east)");
            Assert.That(ReviewTourPlan.SideHeading(Height, 600.0, 900.0, 0.0), Is.EqualTo(-90.0), "the higher side (west)");

            var stop = new ReviewStop { S = 4200.4, Label = "Inn The Grey Goose!" };
            Assert.That(ReviewTourPlan.FileName("20261002-1432", 12345, 7, stop, ReviewShot.Prominent, true), Is.EqualTo("tour_20261002-1432_s12345_0007_s4200m_inn-the-grey-goose_prominent_low.png"));
            Assert.That(ReviewTourPlan.Slug("  Über   Pass / Col  "), Is.EqualTo("ber-pass-col"));
            Assert.That(ReviewTourPlan.FileName("20261002-1432", 12345, 7, stop, ReviewShot.Prominent, true, ReviewTourKind.Tour), Is.EqualTo("tour_20261002-1432_s12345_0007_s4200m_inn-the-grey-goose_prominent_low.png"));
            Assert.That(ReviewTourPlan.FileName("20261002-1432", 12345, 12, stop, ReviewShot.Overview, false, ReviewTourKind.Places), Is.EqualTo("tour_20261002-1432_places_s12345_0012_s4200m_inn-the-grey-goose_overview.png"));
            var names = new List<string>
            {
                ReviewTourPlan.FileName("20261002-1432", 1, 10, stop, ReviewShot.Road, false, ReviewTourKind.Places),
                ReviewTourPlan.FileName("20261002-1432", 1, 2, stop, ReviewShot.Road, false, ReviewTourKind.Places),
                ReviewTourPlan.FileName("20261001-0900", 1, 9, stop, ReviewShot.Road, false, ReviewTourKind.Tour),
            };
            names.Sort(StringComparer.Ordinal);
            Assert.That(names[0], Does.Contain("20261001-0900"), "by run first");
            Assert.That(names[1], Does.Contain("_0002_"), "then by capture");

            Assert.That(ReviewTourPlan.WeatherFor(ReviewWeather.Keep), Is.Null);
            Assert.That(ReviewTourPlan.WeatherFor(ReviewWeather.Clear), Is.EqualTo(WeatherState.Clear));
            Assert.That(ReviewTourPlan.WeatherFor(ReviewWeather.Hazy), Is.EqualTo(WeatherState.Hazy));
            Assert.That(ReviewTourPlan.WeatherFor(ReviewWeather.Overcast), Is.EqualTo(WeatherState.Overcast));
            Assert.That(ReviewTourPlan.WeatherFor(ReviewWeather.Rain), Is.EqualTo(WeatherState.LightRain));

            var mark = new ReviewStop { S = 4100.0, Label = "landmark St Aldo's", Kind = WaypointKind.Landmark, PlaceName = "St Aldo's", HasTarget = true, TargetX = 300.0, TargetZ = 4400.0, TargetStructureHeight = 9.2f, TargetKind = "Chapel", TargetVisible = false, TargetPixels = 216f };
            var markFields = new List<KeyValuePair<string, object>>();
            ReviewTourPlan.StopFields(markFields, mark, 0.0, 4000.0, ReviewShot.Prominent);
            string markJson = ReviewTourPlan.Sidecar(markFields);
            Assert.That(markJson, Does.Contain("\"landmarkName\": \"St Aldo's\""));
            Assert.That(markJson, Does.Contain("\"landmarkKind\": \"Chapel\""));
            Assert.That(markJson, Does.Contain("\"landmarkDistance\": 500"));
            Assert.That(markJson, Does.Contain("\"landmarkBearing\": 36.87"));
            Assert.That(markJson, Does.Contain("\"landmarkVisible\": false"));
            Assert.That(markJson, Does.Contain("\"landmarkPixels1080\": 216"));
            var village = new ReviewStop { Kind = WaypointKind.Village, PlaceName = "Hartdenfield", PlaceRadius = 90f };
            var villageFields = new List<KeyValuePair<string, object>>();
            ReviewTourPlan.StopFields(villageFields, village, 0.0, 0.0, ReviewShot.Overview);
            Assert.That(ReviewTourPlan.Sidecar(villageFields), Is.EqualTo("{\n  \"placeName\": \"Hartdenfield\",\n  \"placeKind\": \"Village\",\n  \"placeRadius\": 90\n}\n"));

            string json = ReviewTourPlan.Sidecar(new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("seed", 7),
                new KeyValuePair<string, object>("hour", 14.25f),
                new KeyValuePair<string, object>("region", "The \"Wexmoor\" Downs"),
                new KeyValuePair<string, object>("camera", new[] { 1.5, 2.0, -3.25 }),
                new KeyValuePair<string, object>("lowSun", false),
                new KeyValuePair<string, object>("missing", null),
            });
            Assert.That(json, Is.EqualTo("{\n  \"seed\": 7,\n  \"hour\": 14.25,\n  \"region\": \"The \\\"Wexmoor\\\" Downs\",\n  \"camera\": [1.5, 2, -3.25],\n  \"lowSun\": false,\n  \"missing\": null\n}\n"));
        }

        // ------------------------------------------------------------------ walking benchmark

        private static List<WalkFrame> Walk(params (double s, string region, float ms, bool held)[] rows)
        {
            var frames = new List<WalkFrame>();
            float t = 0f;
            foreach (var r in rows)
            {
                t += r.ms / 1000f;
                frames.Add(new WalkFrame { T = t, S = r.s, Region = r.region, FrameMs = r.ms, GpuMs = r.ms * 0.5f, Held = r.held });
            }

            return frames;
        }

        [Test]
        public void WalkPercentilesInterpolateAndIgnoreEmptyLists()
        {
            var sorted = new List<float> { 10f, 20f, 30f, 40f, 50f };
            Assert.That(ReviewWalkBench.Percentile(sorted, 50.0), Is.EqualTo(30f));
            Assert.That(ReviewWalkBench.Percentile(sorted, 95.0), Is.EqualTo(48f).Within(1e-4));
            Assert.That(ReviewWalkBench.Percentile(sorted, 0.0), Is.EqualTo(10f));
            Assert.That(ReviewWalkBench.Percentile(sorted, 100.0), Is.EqualTo(50f));
            Assert.That(float.IsNaN(ReviewWalkBench.Percentile(new List<float>(), 50.0)), Is.True);
            Assert.That(ReviewWalkBench.Mean(new[] { 1f, float.NaN, 3f }), Is.EqualTo(2f));
        }

        [Test]
        public void WalkHitchesCountPerKilometreAndLeaveHeldFramesOut()
        {
            List<WalkFrame> frames = Walk((0.0, "Meadow", 10f, false), (250.0, "Meadow", 20f, false), (500.0, "Forest", 40f, false),
                (500.0, "Forest", 400f, true), (750.0, "Forest", 60f, false), (1000.0, "Forest", 12f, false));
            Assert.That(ReviewWalkBench.CountOver(frames, 16.7f), Is.EqualTo(3));
            Assert.That(ReviewWalkBench.CountOver(frames, 50f), Is.EqualTo(1));
            Assert.That(ReviewWalkBench.PerKm(3, 500.0), Is.EqualTo(6f));
            Assert.That(ReviewWalkBench.PerKm(3, 0.0), Is.EqualTo(0f));

            List<ReviewWalkBench.RegionRow> rows = ReviewWalkBench.Regions(frames);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].Region, Is.EqualTo("Meadow"));
            Assert.That(rows[0].Metres, Is.EqualTo(250.0));
            Assert.That(rows[1].Metres, Is.EqualTo(750.0));
            Assert.That(rows[1].Frames, Is.EqualTo(3), "the held frame is not a frame the traveller saw");
            Assert.That(rows[1].Hitches, Is.EqualTo(2));
            Assert.That(rows[1].HitchesPerKm, Is.EqualTo(2f * 1000f / 750f).Within(1e-4));
            Assert.That(rows[1].MeanFrame, Is.EqualTo(112f / 3f).Within(1e-4));
        }

        [Test]
        public void WalkWorstFramesNameTheNearestPlace()
        {
            List<WalkFrame> frames = Walk((100.0, "Meadow", 30f, false), (200.0, "Meadow", 90f, true), (900.0, "Forest", 45f, false), (1500.0, "Forest", 30f, false));
            List<int> worst = ReviewWalkBench.Worst(frames, 2);
            Assert.That(worst, Is.EqualTo(new List<int> { 2, 0 }), "longest first, held frames out, earlier first on ties");
            var places = new List<WalkPlace>
            {
                new WalkPlace { Kind = "Inn", Name = "The Grey Goose", S = 1150.0 },
                new WalkPlace { Kind = "Bridge", S = 880.0 },
            };
            Assert.That(ReviewWalkBench.NearestPlace(places, 900.0), Is.EqualTo(1));
            Assert.That(ReviewWalkBench.NearestPlace(places, 1300.0), Is.EqualTo(0));
            Assert.That(ReviewWalkBench.NearestPlace(places, 100.0), Is.EqualTo(-1));
        }

        [Test]
        public void WalkReportFileNameJsonAndCsv()
        {
            Assert.That(ReviewWalkBench.FileName("20261006-1500", 12345, 14012.4, 16013.6, 6f), Is.EqualTo("walk_20261006-1500_s12345_14012-16014m_x6.json"));
            Assert.That(ReviewWalkBench.FileName("20261006-1500", 1, 0.0, 2000.0, 0.5f), Is.EqualTo("walk_20261006-1500_s1_0-2000m_x0.5.json"));
            List<WalkFrame> frames = Walk((0.0, "Meadow", 10f, false), (500.0, "Meadow", 40f, false), (1000.0, "Forest", 20f, false));
            frames[1] = new WalkFrame { T = frames[1].T, S = 500.0, Region = "Meadow", FrameMs = 40f, Slice = "place, inn", SliceMs = 11.2f };
            var head = new List<KeyValuePair<string, object>> { new KeyValuePair<string, object>("speedScale", 6f) };
            var holds = new List<WalkHold> { new WalkHold { S = 700.0, Start = 3f, Seconds = 1.5f, Region = "Forest" } };
            List<KeyValuePair<string, object>> report = ReviewWalkBench.Report(head, frames, holds, new List<WalkPlace> { new WalkPlace { Kind = "Inn", Name = "Goose", S = 600.0 } });
            Assert.That(ReviewWalkBench.Number(report, "metresCovered"), Is.EqualTo(1000.0));
            Assert.That(ReviewWalkBench.Number(report, "frameMsMax"), Is.EqualTo(40.0));
            Assert.That(ReviewWalkBench.Number(report, "holdSeconds"), Is.EqualTo(1.5).Within(1e-6));
            string json = ReviewJson.Write(report);
            Assert.That(json, Does.StartWith("{\n  \"speedScale\": 6,\n"));
            Assert.That(json, Does.Contain("\"framesOverMs\": {\n    \"16.7\": {\n      \"frames\": 2,\n      \"perKm\": 2\n    },"));
            Assert.That(json, Does.Contain("\"nearestPlace\": \"Inn Goose\""));
            Assert.That(json, Does.Contain("\"slice\": \"place, inn\""));
            Assert.That(json, Does.Contain("\"holds\": [\n    {\n      \"roadDistance\": 700,"));
            string[] lines = ReviewWalkBench.SummaryLines(report, "walk.json");
            Assert.That(lines.Length, Is.EqualTo(3));
            Assert.That(lines[0], Does.Contain("1000 m"));
            string csv = ReviewWalkBench.Csv(frames);
            Assert.That(csv.Split('\n')[0], Does.EndWith(",held,culprit"));
            Assert.That(csv.Split('\n')[2], Does.EndWith(",11.2,place  inn,0,"));   // slow, but nothing named
        }

        [Test]
        public void WalkWorstFramesNameTheirCulprit()
        {
            List<WalkFrame> frames = Walk((0.0, "Meadow", 8f, false), (100.0, "Meadow", 277f, false), (200.0, "Forest", 12f, false), (300.0, "Forest", 95f, false));
            WalkFrame stall = frames[1];
            stall.Scope1 = new CostEntry("PeopleManager", 3.5f);
            stall.Scope2 = new CostEntry("EndlessWorld", 1.25f);
            stall.Engine = new[] { new CostEntry("PlayerLoop", 270f), new CostEntry("Shader.CreateGPUProgram", 240f), new CostEntry("GC.Collect", 0.6f) };
            frames[1] = stall;
            WalkFrame scripted = frames[3];
            scripted.Scope1 = new CostEntry("EndlessWorld.ApplyWindow", 80f);
            scripted.Engine = new[] { new CostEntry("Update.ScriptRunBehaviourUpdate", 82f) };
            frames[3] = scripted;
            WalkFrame quiet = frames[2];
            quiet.Scope1 = new CostEntry("HudController", 0.2f);
            frames[2] = quiet;

            // A phase marker (the whole loop, the script update) is context, never the culprit; the largest cause wins over a scope.
            Assert.That(ReviewWalkBench.IsPhase("PlayerLoop"), Is.True);
            Assert.That(ReviewWalkBench.Culprit(frames[1]).Name, Is.EqualTo("Shader.CreateGPUProgram"));
            Assert.That(ReviewWalkBench.Culprit(frames[3]).Name, Is.EqualTo("EndlessWorld.ApplyWindow"));
            Assert.That(ReviewWalkBench.Culprit(frames[0]).IsEmpty, Is.True);

            string json = ReviewJson.Write(ReviewWalkBench.Report(null, frames, null, new List<WalkPlace> { new WalkPlace { Kind = "Inn", Name = "The Rusty Lantern", S = 117.0 } }));
            Assert.That(json, Does.Contain("\"culprit\": \"Shader.CreateGPUProgram 240.0 ms\""));
            Assert.That(json, Does.Contain("\"topScopes\": [\n        {\n          \"name\": \"PeopleManager\",\n          \"ms\": 3.5\n        },\n        {\n          \"name\": \"EndlessWorld\",\n          \"ms\": 1.25\n        }\n      ],"));
            Assert.That(json, Does.Contain("\"name\": \"GC.Collect\",\n          \"ms\": 0.6"));
            Assert.That(json, Does.Contain("\"culprit\": \"EndlessWorld.ApplyWindow 80.0 ms\""));

            string[] csv = ReviewWalkBench.Csv(frames).Split('\n');
            Assert.That(csv[2], Does.EndWith(",0,Shader.CreateGPUProgram 240.0 ms"));
            Assert.That(csv[3], Does.EndWith(",0,"));   // 12 ms: under the culprit threshold, not named
            Assert.That(csv[4], Does.EndWith(",0,EndlessWorld.ApplyWindow 80.0 ms"));

            var fields = new List<KeyValuePair<string, object>>();
            ReviewWalkBench.CulpritFields(fields, "stopWorst", new CostEntry("WildlifeManager", 2f), default, default, null);
            Assert.That(fields.Count, Is.EqualTo(3));
            Assert.That(fields[0].Key, Is.EqualTo("stopWorstCulprit"));
            Assert.That(fields[0].Value, Is.EqualTo("WildlifeManager 2.0 ms"));
            Assert.That(((List<object>)fields[1].Value).Count, Is.EqualTo(1));
            Assert.That(((List<object>)fields[2].Value).Count, Is.EqualTo(0));
        }

        [Test]
        public void TourSummaryGroupsByRegionStopAndLandmark()
        {
            var shots = new List<TourShotRecord>
            {
                new TourShotRecord { Stop = 0, S = 600.0, Label = "600 m", Region = "Forest", Shot = "Road", FrameMsStop = 16f, GpuMs = 14f, PresentWaitMs = 2f, TreeTriangles = 1e6f },
                new TourShotRecord { Stop = 0, S = 600.0, Label = "600 m", Region = "Forest", Shot = "Road", LowSun = true, FrameMsStop = 30f, GpuMs = 28f, PresentWaitMs = 4f, TreeTriangles = 2e6f },
                new TourShotRecord { Stop = 1, S = 1200.0, Label = "landmark crow abbey", Region = "Meadow", Shot = "Prominent", FrameMsStop = 9f, GpuMs = 7f, Landmark = "Crow Abbey", LandmarkVisible = true, EyeRaise = 3f },
            };
            string json = ReviewJson.Write(ReviewTourSummary.Build(null, shots));
            Assert.That(json, Does.Contain("\"region\": \"Forest\",\n      \"captures\": 2,\n      \"frameMsStopMean\": 23,\n      \"frameMsStopMax\": 30,"));
            Assert.That(json, Does.Contain("\"frameMsNoon\": 16,\n      \"frameMsLowSun\": 30"));
            Assert.That(json, Does.Contain("\"name\": \"Crow Abbey\",\n      \"roadDistance\": 1200,\n      \"visible\": true,\n      \"eyeRaise\": 3,"));
            Assert.That(ReviewTourSummary.FileName("20261006-0812", ReviewTourKind.Places), Is.EqualTo("tour_20261006-0812_places_summary.json"));
            Assert.That(ReviewTourPlan.StartLine("tour", 14012.0, double.NaN, null), Does.StartWith("<b>Review tour starts at 14012 m</b> (from where the traveller stands"));
        }

        // ------------------------------------------------------------------ trees near the landmark eye

        [Test]
        public void NearTreesBlockTrunkAndCrownButNotBeyondReach()
        {
            // Eye at the origin 2.4 m up, target 400 m north at 30 m: the line rises 0.069 m per metre.
            var trunk = new ReviewSight.Tree { X = 0.2, Z = 6.0, Ground = 0f, CrownBottom = 4f, Top = 12f, TrunkRadius = 0.5f, CrownRadius = 3f };
            Assert.That(ReviewSight.Clear(new[] { trunk }, 0.0, 0.0, 2.4f, 0.0, 400.0, 30f), Is.False, "a trunk 6 m ahead fills the frame");
            var aside = trunk;
            aside.X = 4.0;   // 4 m to the side: misses the trunk (0.5 m) and the crown (3 m)
            Assert.That(ReviewSight.Clear(new[] { aside }, 0.0, 0.0, 2.4f, 0.0, 400.0, 30f), Is.True);
            var crown = new ReviewSight.Tree { X = 1.0, Z = 40.0, Ground = 0f, CrownBottom = 3f, Top = 14f, TrunkRadius = 0.4f, CrownRadius = 4f };
            Assert.That(ReviewSight.Clear(new[] { crown }, 0.0, 0.0, 2.4f, 0.0, 400.0, 30f), Is.False, "the line at 40 m is 5.2 m up, inside the crown");
            var far = crown;
            far.Z = 120.0;   // beyond the 80 m reach: the crown bands of the distant tiles cover it
            Assert.That(ReviewSight.Clear(new[] { far }, 0.0, 0.0, 2.4f, 0.0, 400.0, 30f), Is.True);
            Assert.That(ReviewSight.Clear(new[] { crown }, 0.0, 0.0, 16f, 0.0, 400.0, 30f), Is.True, "a raised eye looks over the crown");
            // Crow Abbey (seed 12345, 21,187 m): a big oak 66 m out passes 1.6 m beside the centre line, and the 26 m wide ruin
            // 411 m away is a wedge, not a line: with half its radius kept clear the trunk blocks it.
            var oak = new ReviewSight.Tree { X = -1.6, Z = 66.0, Ground = 0f, CrownBottom = 3.7f, Top = 11f, TrunkRadius = 1.5f, CrownRadius = 6f };
            Assert.That(ReviewSight.Clear(new[] { oak }, 0.0, 0.0, 2.4f, 0.0, 411.0, 4f), Is.True);
            Assert.That(ReviewSight.Clear(new[] { oak }, 0.0, 0.0, 2.4f, 0.0, 411.0, 4f, ReviewSight.Reach, ReviewSight.LandmarkWiden * 13f), Is.False);
        }

        [Test]
        public void LandmarkEyeTriesRoadPositionsBeforeRaising()
        {
            RoadPoint Road(double s) => new RoadPoint { S = s, X = 0.0, Z = s, Height = 10f, HeadingDeg = 0f };
            // Blocked across the whole road at the stop: the first other position is 3 m along the road.
            ReviewSight.Eye moved = ReviewSight.Choose(Road, 100.0, (x, z, y) => Math.Abs(z - 100.0) > 0.01);
            Assert.That(moved.Found, Is.True);
            Assert.That(moved.Shift, Is.EqualTo(3f));
            Assert.That(moved.Raise, Is.EqualTo(0f));
            Assert.That(moved.Y, Is.EqualTo(10f + ReviewTourPlan.LandmarkEyeHeight));
            // Only an eye 4 m or more above the usual one clears: every road position at the lower heights is tried first.
            ReviewSight.Eye raised = ReviewSight.Choose(Road, 100.0, (x, z, y) => y >= 10f + ReviewTourPlan.LandmarkEyeHeight + 4f);
            Assert.That(raised.Found, Is.True);
            Assert.That(raised.Raise, Is.EqualTo(4.5f));
            Assert.That(raised.Shift, Is.EqualTo(0f));
            // Across the road: heading 0 (north), right is +x.
            ReviewSight.Eye across = ReviewSight.Choose(Road, 100.0, (x, z, y) => x > 1f);
            Assert.That(across.Lateral, Is.EqualTo(1.5f));
            Assert.That(across.X, Is.EqualTo(1.5).Within(1e-9));
            ReviewSight.Eye none = ReviewSight.Choose(Road, 100.0, (x, z, y) => false);
            Assert.That(none.Found, Is.False);
            Assert.That(none.Z, Is.EqualTo(100.0));
        }
    }
}
