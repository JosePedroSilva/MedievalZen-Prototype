using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;

namespace MedievalZen.Sound
{
    /// <summary>What the moments read each tick: the road round the traveller, the places, the sky, the seat.</summary>
    public struct MusicMomentContext
    {
        /// <summary>Seconds on any steady clock (the director's).</summary>
        public double Time;
        /// <summary>Speed along the road, m/s.</summary>
        public float Speed;
        /// <summary>Road height now minus the lowest road point of the last 250 m (metres climbed to get here).</summary>
        public float RiseBehind;
        /// <summary>Road height now minus the highest road point of the next 300 m (positive: the road ahead falls away, a long view).</summary>
        public float DropAhead;
        /// <summary>Road height now minus the road height 300 m ahead (positive: the road goes down into the view).</summary>
        public float FallAhead;
        /// <summary>How open the roadside is here, 0 (a tunnel of trees) .. 1 (open both sides).</summary>
        public float Openness;
        /// <summary>Metres since the road last ran under a canopy (+inf: none within 150 m).</summary>
        public float SinceCanopy;
        /// <summary>The road here runs under a canopy.</summary>
        public bool Canopy;
        /// <summary>The traveller is in a forest region.</summary>
        public bool ForestRegion;
        /// <summary>Road metres to the next inn, hamlet or village centre ahead (+inf none known), and its kind.</summary>
        public double PlaceAhead;
        public WaypointKind PlaceAheadKind;
        /// <summary>Road metres since the last inn, hamlet or village centre behind (+inf none known).</summary>
        public double PlaceBehind;
        /// <summary>Road metres past the point a landmark ahead first shows well (NaN: none near), and its height in metres.</summary>
        public double PastLandmarkView;
        public float LandmarkHeight;
        /// <summary>Road metres past the top of a pass (NaN: none near).</summary>
        public double PastPass;
        /// <summary>Sun elevation in degrees.</summary>
        public float SunElevation;
        /// <summary>Rain or mist now.</summary>
        public bool Wet;
        public bool Seated;
        public float SeatedSeconds;
    }

    /// <summary>
    /// Rates the moments a piece of music may start on (<see cref="MusicMoment"/>), 0..1, from a per-tick context. Pure. Road moments
    /// (a crest, leaving the woods, a landmark, a pass top, leaving or nearing a place) are read from where the traveller is; sky
    /// moments (daybreak, the golden hour, weather clearing) are crossings remembered for a while.
    /// </summary>
    public sealed class MusicMoments
    {
        /// <summary>Sun elevations crossed: rising at daybreak, sinking into the golden hour (degrees).</summary>
        public const float DaybreakElevation = 3f, GoldenElevation = 10f;
        /// <summary>How long a sky moment stays eligible, seconds.</summary>
        public const double SkyWindow = 90.0, ClearingWindow = 120.0;

        private float lastSun = float.NaN;
        private double daybreakAt = double.NegativeInfinity, goldenAt = double.NegativeInfinity, clearedAt = double.NegativeInfinity;
        private bool lastWet;
        private double wetSince = double.NaN;

        /// <summary>The best moment now and its strength.</summary>
        public MusicMoment Best { get; private set; }
        public float Strength { get; private set; }

        /// <summary>The mood the track choice weighs.</summary>
        public static MusicMood MoodOf(in MusicMomentContext c)
        {
            if (c.Seated) return MusicMood.Rest;
            if (c.SunElevation < 4f) return MusicMood.Night;
            if (c.Canopy || c.ForestRegion && c.SinceCanopy < 400f) return MusicMood.Forest;
            return MusicMood.OpenDay;
        }

        /// <summary>Updates the remembered crossings and finds the best moment.</summary>
        public void Update(in MusicMomentContext c)
        {
            bool open = !c.Canopy && c.Openness >= 0.5f;
            if (!float.IsNaN(lastSun))
            {
                if (lastSun < DaybreakElevation && c.SunElevation >= DaybreakElevation) daybreakAt = c.Time;
                if (lastSun > GoldenElevation && c.SunElevation <= GoldenElevation) goldenAt = c.Time;
            }

            lastSun = c.SunElevation;
            if (c.Wet && !lastWet) wetSince = c.Time;
            if (!c.Wet && lastWet && c.Time - wetSince >= 120.0) clearedAt = c.Time;   // a passing shower is not a clearing
            lastWet = c.Wet;

            Best = MusicMoment.None;
            Strength = 0f;
            if (c.Seated)
            {
                if (c.SeatedSeconds >= MusicDirector.RestDelay) Offer(MusicMoment.Rest, 0.7f);
                return;   // sitting: only the rest moment
            }

            bool moving = c.Speed >= 0.5f;
            if (!moving) return;

            // Crest: climbed at least 8 m over the last 250 m, the road ahead falls away, the view is open.
            if (c.RiseBehind >= 8f && c.DropAhead >= -0.5f && c.FallAhead >= 3f && open)
                Offer(MusicMoment.Crest, 0.45f + 0.35f * Clamp01((c.RiseBehind - 8f) / 30f) + 0.15f * Clamp01((c.FallAhead - 3f) / 25f));

            // Out of the woods: the canopy closed within the last 100 m and the roadside is open now.
            if (!c.Canopy && c.SinceCanopy >= 15f && c.SinceCanopy <= 100f && c.Openness >= 0.35f)
                Offer(MusicMoment.LeavingWoods, c.ForestRegion ? 0.7f : 0.6f);

            if (!double.IsNaN(c.PastLandmarkView) && c.PastLandmarkView >= 0.0 && c.PastLandmarkView <= 150.0)
                Offer(MusicMoment.LandmarkSight, 0.55f + 0.35f * Clamp01(c.LandmarkHeight / 30f));

            if (!double.IsNaN(c.PastPass) && c.PastPass >= 0.0 && c.PastPass <= 150.0) Offer(MusicMoment.PassSummit, 0.9f);

            if (c.PlaceBehind >= 150.0 && c.PlaceBehind <= 300.0) Offer(MusicMoment.LeavingPlace, 0.55f);

            if ((c.PlaceAheadKind == WaypointKind.Village || c.PlaceAheadKind == WaypointKind.City) && c.PlaceAhead >= 250.0 && c.PlaceAhead <= 450.0) Offer(MusicMoment.Arrival, 0.5f);

            if (open && c.Time - daybreakAt <= SkyWindow) Offer(MusicMoment.Daybreak, 0.75f);
            if (open && c.Time - goldenAt <= SkyWindow) Offer(MusicMoment.GoldenHour, 0.65f);
            if (!c.Wet && c.Time - clearedAt <= ClearingWindow) Offer(MusicMoment.WeatherClearing, 0.6f);

            // The calm baseline: open road, well clear of the last place. Enough once the gap is long.
            // Under trees it is weaker (0.25: only once the later time is reached), so a long wood cannot hold the music off for good.
            if (c.PlaceBehind >= 300.0) Offer(MusicMoment.Wander, open ? 0.3f : 0.25f);
        }

        private void Offer(MusicMoment moment, float strength)
        {
            if (strength > Strength)
            {
                Best = moment;
                Strength = strength;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>
    /// Reads the road part of <see cref="MusicMomentContext"/> from the endless generator: road heights and roadside openness round the
    /// traveller (pure noise and curve reads, a fraction of a millisecond), and the places, landmarks and passes from the region
    /// waypoints, but only when they are already cached (<see cref="EndlessGenerator.WaypointsCached"/>): it never builds a region on
    /// the calling thread, and keeps its last answer until the warmer has run.
    /// </summary>
    public sealed class MusicRoadProbe
    {
        /// <summary>The waypoint search reaches this far behind and ahead (metres).</summary>
        public const double Behind = 400.0, Ahead = 4000.0;

        private readonly EndlessGenerator generator;
        private readonly List<Waypoint> waypoints = new List<Waypoint>();
        private double waypointsAt = double.NaN;
        private bool waypointsKnown;

        public MusicRoadProbe(EndlessGenerator generator)
        {
            this.generator = generator;
        }

        /// <summary>The places are known (the waypoints round the traveller were cached at least once).</summary>
        public bool PlacesKnown => waypointsKnown;

        /// <summary>Fills the road fields of <paramref name="c"/> at road distance <paramref name="s"/>.</summary>
        public void Fill(double s, ref MusicMomentContext c)
        {
            float here = generator.Road(s).Height;
            float low = here, high = float.NegativeInfinity;
            for (int i = 1; i <= 10; i++) low = Math.Min(low, generator.Road(s - 25.0 * i).Height);
            float far = here;
            for (int i = 1; i <= 12; i++)
            {
                far = generator.Road(s + 25.0 * i).Height;
                high = Math.Max(high, far);
            }

            c.RiseBehind = here - low;
            c.DropAhead = here - high;
            c.FallAhead = here - far;

            LandmarkPlan landmarks = generator.Landmarks;
            float right = landmarks.Openness(s, 1.0), left = landmarks.Openness(s, -1.0);
            c.Canopy = right <= 0f && left <= 0f;
            c.Openness = Math.Min(right, left);
            c.SinceCanopy = float.PositiveInfinity;
            if (c.Canopy) c.SinceCanopy = 0f;
            else
            {
                for (int i = 1; i <= 10; i++)
                {
                    if (landmarks.Openness(s - 15.0 * i, 1.0) > 0f) continue;
                    c.SinceCanopy = 15f * i;
                    break;
                }
            }

            c.ForestRegion = generator.Region(s).Kind == RegionKind.Forest;

            if (double.IsNaN(waypointsAt) || Math.Abs(s - waypointsAt) >= 20.0 || !waypointsKnown)
            {
                if (generator.WaypointsCached(s - Behind, s + Ahead))
                {
                    waypoints.Clear();
                    generator.Waypoints(s - Behind, s + Ahead, waypoints);
                    waypointsAt = s;
                    waypointsKnown = true;
                }
            }

            c.PlaceAhead = double.PositiveInfinity;
            c.PlaceBehind = double.PositiveInfinity;
            c.PlaceAheadKind = WaypointKind.Inn;
            c.PastLandmarkView = double.NaN;
            c.LandmarkHeight = 0f;
            c.PastPass = double.NaN;
            if (!waypointsKnown)
            {
                c.PlaceAhead = double.NaN;
                return;
            }

            for (int i = 0; i < waypoints.Count; i++)
            {
                Waypoint w = waypoints[i];
                double d = w.S - s;
                if (w.IsSettlement)
                {
                    if (d > 0.0 && d < c.PlaceAhead)
                    {
                        c.PlaceAhead = d;
                        c.PlaceAheadKind = w.Kind;
                    }
                    else if (d <= 0.0 && -d < c.PlaceBehind) c.PlaceBehind = -d;
                }
                else if (w.Kind == WaypointKind.Landmark)
                {
                    double view = double.IsNaN(w.ViewS) ? w.S - 600.0 : Math.Min(w.ViewS, w.S - 150.0);
                    double past = s - view;
                    if (past >= 0.0 && past <= 300.0 && (double.IsNaN(c.PastLandmarkView) || past < c.PastLandmarkView))
                    {
                        c.PastLandmarkView = past;
                        c.LandmarkHeight = w.StructureHeight;
                    }
                }
                else if (w.Kind == WaypointKind.Pass && d <= 0.0 && -d <= 300.0) c.PastPass = -d;
            }

            // The search reaches 4 km: beyond it the runway is "long enough" (no piece is longer than about 7.5 min, 650 m at 1.4 m/s).
        }
    }
}
