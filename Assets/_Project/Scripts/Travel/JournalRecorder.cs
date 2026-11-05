using System;
using System.Collections.Generic;
using MedievalZen.Endless;

namespace MedievalZen.Travel
{
    /// <summary>One look at the traveller, a few times a second (absolute coordinates).</summary>
    public struct JournalTick
    {
        /// <summary>Road distance of the traveller.</summary>
        public double S;
        /// <summary>Absolute position of the traveller.</summary>
        public double X, Z;
        /// <summary>Day of the day cycle and hour.</summary>
        public int Day;
        public float Hour;
        /// <summary>Weather state as int (−1 unknown).</summary>
        public int Weather;
        /// <summary>Region the traveller is in (the world's, with its hysteresis), and its name.</summary>
        public int RegionIndex;
        public string RegionName;
        /// <summary>Nothing may be recorded now (a streaming hold, a scripted move, a review tour).</summary>
        public bool Suppressed;
        /// <summary>A review tour or benchmark runs: whatever happens until it ends is not the traveller's.</summary>
        public bool Capture;
    }

    /// <summary>
    /// Decides what goes into the journal from where the traveller is: each place once per visit (entering its footprint, or
    /// its enter distance for crossings, passes, viewpoints and landmarks), again only after really leaving it; regions as the
    /// world announces them. A jump along the road (a "Go now", or a hold that moved the traveller) records only the settlement
    /// landed at; a review tour records nothing, and the places round where it leaves the traveller are taken as already
    /// visited. Pure; unit tested.
    /// </summary>
    public sealed class JournalRecorder
    {
        /// <summary>A move of more than this along the road between two ticks is a teleport, not a walk.</summary>
        public const double TeleportJump = 200.0;

        /// <summary>After a teleport or a tour, this many ticks only mark places as visited.</summary>
        public const int SettleTicks = 4;

        /// <summary>A teleport records the settlement within its radius plus this many metres of the landing point.</summary>
        public const double ArrivalSlack = 60.0;

        /// <summary>Extra metres beyond the enter distance before a visit is over.</summary>
        public const double LeaveMargin = 150.0;

        /// <summary>A landmark is seen from close by within this planar distance of the structure ...</summary>
        public const double LandmarkNear = 250.0;

        /// <summary>... or within this many road metres of its best view (and this close: <see cref="LandmarkViewReach"/>).</summary>
        public const double LandmarkViewSpan = 30.0;

        /// <summary>Farthest a landmark's best view counts from.</summary>
        public const double LandmarkViewReach = 1600.0;

        private readonly HashSet<long> present = new HashSet<long>();
        private bool hasLast;
        private double lastS;
        private int lastRegion = int.MinValue;
        private bool silentResync;
        private int settle;

        /// <summary>The journal written to.</summary>
        public Journal Journal { get; }

        /// <summary>Kind word of a landmark ("windmill", "chapel" ...), or null when unknown ("landmark").</summary>
        public Func<Waypoint, string> LandmarkKind;

        /// <summary>Raised for each entry added.</summary>
        public event Action<JournalEntry> Added;

        public JournalRecorder(Journal journal)
        {
            Journal = journal ?? throw new ArgumentNullException(nameof(journal));
        }

        private enum Category { None, Inn, Village, Crossing, Pass, Viewpoint, Landmark }

        private static Category CategoryOf(Waypoint w)
        {
            switch (w.Kind)
            {
                case WaypointKind.Inn: return Category.Inn;
                case WaypointKind.Hamlet:
                case WaypointKind.Village: return Category.Village;
                case WaypointKind.Bridge:
                case WaypointKind.Ford: return string.IsNullOrEmpty(w.Name) ? Category.None : Category.Crossing;
                case WaypointKind.Pass: return Category.Pass;
                case WaypointKind.Viewpoint: return Category.Viewpoint;
                case WaypointKind.Landmark: return Category.Landmark;
                case WaypointKind.Lake:
                case WaypointKind.Camp:
                case WaypointKind.Milestone: return Category.None;
                default:
                    // A kind added later (a city ...): a named place with a footprint is a settlement.
                    return !string.IsNullOrEmpty(w.Name) && w.Radius >= 10f ? Category.Village : Category.None;
            }
        }

        /// <summary>The kind word of a settlement: "inn", "hamlet", "village", or the kind's own name for a later kind ("city").</summary>
        public static string SettlementWord(WaypointKind kind)
        {
            switch (kind)
            {
                case WaypointKind.Inn: return "inn";
                case WaypointKind.Hamlet: return "hamlet";
                case WaypointKind.Village: return "village";
            }

            string name = kind.ToString();
            if (name.Length == 0 || char.IsDigit(name[0]) || name[0] == '-') return "town";
            return name.ToLowerInvariant();
        }

        private static long Key(Waypoint w) => ((long)(int)w.Kind << 32) | (uint)w.Id;

        private static char SeenCategory(Category c)
        {
            switch (c)
            {
                case Category.Inn: return Journal.InnKey;
                case Category.Village: return Journal.VillageKey;
                case Category.Crossing: return Journal.CrossingKey;
                case Category.Pass: return Journal.PassKey;
                case Category.Viewpoint: return Journal.ViewKey;
                default: return Journal.LandmarkKey;
            }
        }

        private static double Planar(Waypoint w, in JournalTick t)
        {
            double dx = w.X - t.X, dz = w.Z - t.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static double EnterDistance(Category c, Waypoint w)
        {
            switch (c)
            {
                case Category.Inn:
                case Category.Village: return Math.Max(12.0, w.Radius);
                case Category.Crossing: return Math.Max(12.0, w.Radius + 6.0);
                case Category.Pass: return 30.0;
                case Category.Viewpoint: return Math.Max(12.0, w.Radius) + 30.0;
                default: return LandmarkNear;
            }
        }

        private static bool Inside(Category c, Waypoint w, in JournalTick t, double extra)
        {
            double d = Planar(w, t);
            if (c == Category.Landmark)
            {
                if (d <= LandmarkNear + extra) return true;
                return !double.IsNaN(w.ViewS) && Math.Abs(t.S - w.ViewS) <= LandmarkViewSpan + extra && d <= LandmarkViewReach + extra;
            }

            return d <= EnterDistance(c, w) + extra;
        }

        /// <summary>Looks at the traveller once; <paramref name="near"/> are the waypoints round them (any order).</summary>
        public void Tick(in JournalTick t, IReadOnlyList<Waypoint> near)
        {
            if (t.Suppressed || t.Capture)
            {
                if (t.Capture) silentResync = true;
                return;
            }

            if (near == null) near = Array.Empty<Waypoint>();
            double jump = hasLast ? Math.Abs(t.S - lastS) : 0.0;
            bool first = !hasLast;
            bool teleport = hasLast && jump > TeleportJump;
            bool silent = silentResync;
            silentResync = false;
            hasLast = true;
            lastS = t.S;

            if (first)
            {
                MarkPresent(t, near);
                if (Journal.Entries.Count == 0) SetOut(t, near);
                else lastRegion = t.RegionIndex;
                settle = SettleTicks;
                return;
            }

            if (teleport || silent)
            {
                present.Clear();
                Waypoint landed = teleport && !silent ? LandedAt(t, near) : null;
                MarkPresent(t, near);
                if (!silent) MarkSeen(Journal.RegionKey, t.RegionIndex);
                lastRegion = t.RegionIndex;
                settle = SettleTicks;
                if (landed != null) Record(t, landed, CategoryOf(landed));
                return;
            }

            Journal.Walked += jump;
            if (settle > 0)
            {
                settle--;
                MarkPresent(t, near);
                Leave(t, near);
                lastRegion = t.RegionIndex;
                return;
            }

            if (t.RegionIndex != lastRegion)
            {
                lastRegion = t.RegionIndex;
                if (!string.IsNullOrEmpty(t.RegionName))
                {
                    string key = Journal.SeenKey(Journal.RegionKey, t.RegionIndex);
                    bool again = Journal.Seen.Contains(key);
                    Journal.Seen.Add(key);
                    Emit(new JournalEntry { Kind = JournalKind.Region, Name = t.RegionName, Again = again }, t);
                }
            }

            for (int i = 0; i < near.Count; i++)
            {
                Waypoint w = near[i];
                if (w == null) continue;
                Category c = CategoryOf(w);
                if (c == Category.None || present.Contains(Key(w))) continue;
                if (Inside(c, w, t, 0.0)) Record(t, w, c);
            }

            Leave(t, near);
        }

        /// <summary>Auto-travel reached <paramref name="w"/> (counted once per visit, like walking in).</summary>
        public bool Arrive(Waypoint w, in JournalTick t)
        {
            if (w == null || t.Suppressed || t.Capture || !hasLast) return false;
            Category c = CategoryOf(w);
            if (c != Category.Inn && c != Category.Village) return false;
            if (present.Contains(Key(w))) return false;
            Record(t, w, c);
            return true;
        }

        /// <summary>Sat a while: <paramref name="seatPhrase"/> "on a log", <paramref name="placeName"/> the settlement sat in, or null.</summary>
        public bool Rest(string seatPhrase, string placeName, in JournalTick t)
        {
            if (t.Suppressed || t.Capture) return false;
            Emit(new JournalEntry { Kind = JournalKind.Rest, Detail = seatPhrase, Name = placeName }, t);
            return true;
        }

        /// <summary>A night at the inn <paramref name="innName"/> (the tick's time is the waking hour).</summary>
        public bool Night(string innName, in JournalTick t)
        {
            if (t.Capture) return false;
            Emit(new JournalEntry { Kind = JournalKind.Night, Name = innName }, t);
            return true;
        }

        private void SetOut(in JournalTick t, IReadOnlyList<Waypoint> near)
        {
            Waypoint from = LandedAt(t, near);
            MarkSeen(Journal.RegionKey, t.RegionIndex);
            lastRegion = t.RegionIndex;
            if (from != null) MarkSeen(SeenCategory(CategoryOf(from)), from.Id);
            Emit(new JournalEntry { Kind = JournalKind.SetOut, Name = from?.Name, Detail = t.RegionName }, t);
        }

        private Waypoint LandedAt(in JournalTick t, IReadOnlyList<Waypoint> near)
        {
            Waypoint best = null;
            double bestD = double.MaxValue;
            for (int i = 0; i < near.Count; i++)
            {
                Waypoint w = near[i];
                if (w == null) continue;
                Category c = CategoryOf(w);
                if (c != Category.Inn && c != Category.Village) continue;
                double d = Planar(w, t);
                if (d <= EnterDistance(c, w) + ArrivalSlack && d < bestD)
                {
                    best = w;
                    bestD = d;
                }
            }

            return best;
        }

        private void MarkPresent(in JournalTick t, IReadOnlyList<Waypoint> near)
        {
            for (int i = 0; i < near.Count; i++)
            {
                Waypoint w = near[i];
                if (w == null) continue;
                Category c = CategoryOf(w);
                if (c == Category.None) continue;
                double extra = c == Category.Inn || c == Category.Village ? ArrivalSlack : 0.0;
                if (Inside(c, w, t, extra)) present.Add(Key(w));
            }
        }

        private readonly HashSet<long> keep = new HashSet<long>();

        private void Leave(in JournalTick t, IReadOnlyList<Waypoint> near)
        {
            if (present.Count == 0) return;
            // Kept: places still in the list round the traveller and not yet well outside; the rest are over (a place gone
            // from the list is kilometres behind).
            keep.Clear();
            for (int i = 0; i < near.Count; i++)
            {
                Waypoint w = near[i];
                if (w == null) continue;
                long key = Key(w);
                if (present.Contains(key) && Inside(CategoryOf(w), w, t, LeaveMargin)) keep.Add(key);
            }

            present.Clear();
            present.UnionWith(keep);
        }

        private void MarkSeen(char category, long id) => Journal.Seen.Add(Journal.SeenKey(category, id));

        private void Record(in JournalTick t, Waypoint w, Category c)
        {
            present.Add(Key(w));
            string key = Journal.SeenKey(SeenCategory(c), w.Id);
            bool again = Journal.Seen.Contains(key);
            Journal.Seen.Add(key);
            var e = new JournalEntry { Name = w.Name, Again = again };
            switch (c)
            {
                case Category.Inn:
                case Category.Village: e.Kind = JournalKind.Arrive; e.Detail = SettlementWord(w.Kind); break;
                case Category.Crossing: e.Kind = JournalKind.Crossing; e.Detail = w.Kind == WaypointKind.Ford ? "ford" : "bridge"; break;
                case Category.Pass: e.Kind = JournalKind.Pass; break;
                case Category.Viewpoint: e.Kind = JournalKind.Viewpoint; break;
                default: e.Kind = JournalKind.Landmark; e.Detail = LandmarkKind?.Invoke(w) ?? "landmark"; break;
            }

            Emit(e, t);
        }

        private void Emit(JournalEntry e, in JournalTick t)
        {
            e.Day = Math.Max(1, t.Day);
            e.Hour = float.IsNaN(t.Hour) ? 12f : ((t.Hour % 24f) + 24f) % 24f;
            e.Weather = t.Weather;
            e.S = t.S;
            Journal.Add(e);
            Added?.Invoke(e);
        }
    }
}
