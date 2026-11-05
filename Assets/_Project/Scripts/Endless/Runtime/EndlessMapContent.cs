using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MedievalZen.UI;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// How much of the scroll is inked. Country within <see cref="SeenAhead"/> metres of the stretch of road the traveller has
    /// walked on this journey (between the nearest and farthest road distances reached) is fully drawn; beyond it the map fades
    /// over <see cref="FadeHalf"/>·2 metres, with a ragged edge, into bare parchment with a pencilled road — the map "drawn as you
    /// go". Pure.
    /// </summary>
    public static class StripReveal
    {
        /// <summary>The map is inked this far beyond the farthest (and nearest) road distance reached, metres.</summary>
        public const double SeenAhead = 1500.0;

        /// <summary>Half the length of the fade from ink to sketch, metres of road.</summary>
        public const double FadeHalf = 200.0;

        /// <summary>Amplitude of the ragged edge of the fade, metres.</summary>
        public const double Ragged = 70.0;

        /// <summary>Kind of content a segment needs.</summary>
        public enum State
        {
            /// <summary>Fully inked.</summary>
            Inked = 0,

            /// <summary>Only the sketch layer.</summary>
            Sketch = 1,

            /// <summary>Partly faded: both layers, blended.</summary>
            Blend = 2,
        }

        /// <summary>
        /// Ink weight 0..1 at road distance <paramref name="s"/> for a journey that has reached [<paramref name="minS"/>,
        /// <paramref name="maxS"/>]; <paramref name="ragged01"/> (0..1, 0.5 neutral) moves the edge by up to ±<see cref="Ragged"/>.
        /// </summary>
        public static float Weight(double s, double minS, double maxS, float ragged01 = 0.5f)
        {
            double jitter = (ragged01 - 0.5) * 2.0 * Ragged;
            double ahead = (s + jitter - (maxS + SeenAhead)) / FadeHalf;
            double behind = ((minS - SeenAhead) - (s - jitter)) / FadeHalf;
            double t = Math.Max(ahead, behind);
            if (t <= -1.0) return 1f;
            if (t >= 1.0) return 0f;
            double x = (t + 1.0) * 0.5;
            return (float)(1.0 - x * x * (3.0 - 2.0 * x));
        }

        /// <summary>What a stretch of road [<paramref name="sLow"/>, <paramref name="sHigh"/>] needs for a reach of [<paramref name="minS"/>, <paramref name="maxS"/>].</summary>
        public static State Classify(double sLow, double sHigh, double minS, double maxS)
        {
            double margin = FadeHalf + Ragged;
            double inkTo = maxS + SeenAhead - margin, inkFrom = minS - SeenAhead + margin;
            if (sHigh <= inkTo && sLow >= inkFrom) return State.Inked;
            if (sLow >= maxS + SeenAhead + margin || sHigh <= minS - SeenAhead - margin) return State.Sketch;
            return State.Blend;
        }

        /// <summary>Reach quantised to 6 m steps (a blended segment is redrawn when this changes).</summary>
        public static long Key(double minS, double maxS)
        {
            long a = (long)Math.Floor(minS / 6.0), b = (long)Math.Floor(maxS / 6.0);
            return unchecked(a * 1000003L ^ b);
        }

        /// <summary>
        /// Blends <paramref name="inked"/> and <paramref name="sketch"/> (both <paramref name="width"/> × rows, row 0 at global
        /// row <paramref name="firstRow"/>) into <paramref name="output"/> by the ink weight of each row's road distance
        /// <paramref name="rowS"/>, with a ragged, slightly uneven edge.
        /// </summary>
        public static void Compose(Color32[] inked, Color32[] sketch, double[] rowS, int width, int firstRow, double minS, double maxS, Color32[] output)
        {
            int rows = rowS.Length;
            for (int y = 0; y < rows; y++)
            {
                int o = y * width;
                int gy = firstRow + y;
                for (int x = 0; x < width; x++)
                {
                    float ragged = StripNoise.Value(x / 23.0, gy / 23.0, 41) * 0.75f + StripNoise.Value(x / 5.0, gy / 5.0, 42) * 0.25f;
                    float w = Weight(rowS[y], minS, maxS, ragged);
                    Color32 a = inked[o + x], b = sketch[o + x];
                    if (w >= 1f) output[o + x] = a;
                    else if (w <= 0f) output[o + x] = b;
                    else output[o + x] = Color32.Lerp(b, a, w);
                }
            }
        }
    }

    /// <summary>The stretch of road reached on a journey (persisted beside the journey; reset for a new road). Pure data.</summary>
    public sealed class EndlessMapReach
    {
        /// <summary>PlayerPrefs key (separate from <see cref="EndlessJourney.PrefsKey"/>, which belongs to the world).</summary>
        public const string PrefsKey = "MedievalZen.EndlessMapReach";

        /// <summary>Version written by <see cref="ToJson"/>.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Seed of the road.</summary>
        public int Seed;

        /// <summary>Nearest road distance reached (the road also runs back from the start).</summary>
        public double MinS;

        /// <summary>Farthest road distance reached.</summary>
        public double MaxS;

        /// <summary>A reach that starts at <paramref name="s"/> on road <paramref name="seed"/>.</summary>
        public static EndlessMapReach Start(int seed, double s) => new EndlessMapReach { Seed = seed, MinS = s, MaxS = s };

        /// <summary>
        /// The reach to use for road <paramref name="seed"/> with the traveller at <paramref name="s"/>: <paramref name="saved"/>
        /// extended to <paramref name="s"/> when it is the same road, else a fresh one.
        /// </summary>
        public static EndlessMapReach Resume(EndlessMapReach saved, int seed, double s)
        {
            if (saved == null || saved.Seed != seed) return Start(seed, s);
            var r = new EndlessMapReach { Seed = seed, MinS = saved.MinS, MaxS = saved.MaxS };
            r.Extend(s);
            return r;
        }

        /// <summary>Widens the reach to include <paramref name="s"/>. True when it changed.</summary>
        public bool Extend(double s)
        {
            if (double.IsNaN(s) || double.IsInfinity(s)) return false;
            bool changed = false;
            if (s > MaxS)
            {
                MaxS = s;
                changed = true;
            }

            if (s < MinS)
            {
                MinS = s;
                changed = true;
            }

            return changed;
        }

        /// <summary>One flat JSON object, e.g. <c>{"version":1,"seed":12345,"min":-20,"max":5321.5}</c>.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder(80);
            sb.Append("{\"version\":").Append(CurrentVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"seed\":").Append(Seed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"min\":").Append(Math.Round(MinS, 2).ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"max\":").Append(Math.Round(MaxS, 2).ToString("R", CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Reads a saved reach; false (and null) for anything malformed, newer, or inconsistent.</summary>
        public static bool TryFromJson(string json, out EndlessMapReach result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || !FlatJson.TryParse(json, out Dictionary<string, object> values)) return false;
            int version = values.TryGetValue("version", out object v) && v is double dv ? (int)dv : 0;
            if (version > CurrentVersion) return false;
            if (!values.TryGetValue("seed", out object seedValue) || !(seedValue is double seed) || seed < int.MinValue || seed > int.MaxValue) return false;
            if (!values.TryGetValue("min", out object minValue) || !(minValue is double min)) return false;
            if (!values.TryGetValue("max", out object maxValue) || !(maxValue is double max)) return false;
            if (double.IsNaN(min) || double.IsNaN(max) || min > max || Math.Abs(min) > 1e7 || Math.Abs(max) > 1e7) return false;
            result = new EndlessMapReach { Seed = (int)seed, MinS = min, MaxS = max };
            return true;
        }
    }

    /// <summary>A bounded, thread-safe least-recently-used cache of painted segments (keyed by segment and layer).</summary>
    public sealed class StripSegmentCache
    {
        private readonly object gate = new object();
        private readonly Dictionary<long, LinkedListNode<KeyValuePair<long, Color32[]>>> map = new Dictionary<long, LinkedListNode<KeyValuePair<long, Color32[]>>>();
        private readonly LinkedList<KeyValuePair<long, Color32[]>> order = new LinkedList<KeyValuePair<long, Color32[]>>();

        /// <summary>A cache holding at most <paramref name="capacity"/> entries.</summary>
        public StripSegmentCache(int capacity)
        {
            Capacity = Math.Max(1, capacity);
        }

        /// <summary>Most entries kept.</summary>
        public int Capacity { get; }

        /// <summary>Entries held.</summary>
        public int Count
        {
            get
            {
                lock (gate) return map.Count;
            }
        }

        /// <summary>Layer ids for <see cref="Key"/>.</summary>
        public const int Inked = 0, Sketch = 1, Blended = 2;

        /// <summary>Cache key of <paramref name="layer"/> of segment <paramref name="segment"/>.</summary>
        public static long Key(int segment, int layer) => ((long)segment << 2) | (uint)(layer & 3);

        /// <summary>The entry for <paramref name="key"/> (marked as recently used), if present.</summary>
        public bool TryGet(long key, out Color32[] pixels)
        {
            lock (gate)
            {
                if (map.TryGetValue(key, out LinkedListNode<KeyValuePair<long, Color32[]>> node))
                {
                    order.Remove(node);
                    order.AddFirst(node);
                    pixels = node.Value.Value;
                    return true;
                }
            }

            pixels = null;
            return false;
        }

        /// <summary>True when <paramref name="key"/> is cached (does not refresh its age).</summary>
        public bool Contains(long key)
        {
            lock (gate) return map.ContainsKey(key);
        }

        /// <summary>Stores <paramref name="pixels"/> under <paramref name="key"/>, dropping the least recently used entries beyond the capacity.</summary>
        public void Put(long key, Color32[] pixels)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            lock (gate)
            {
                if (map.TryGetValue(key, out LinkedListNode<KeyValuePair<long, Color32[]>> old)) order.Remove(old);
                var node = new LinkedListNode<KeyValuePair<long, Color32[]>>(new KeyValuePair<long, Color32[]>(key, pixels));
                order.AddFirst(node);
                map[key] = node;
                while (map.Count > Capacity)
                {
                    LinkedListNode<KeyValuePair<long, Color32[]>> last = order.Last;
                    order.RemoveLast();
                    map.Remove(last.Value.Key);
                }
            }
        }

        /// <summary>Empties the cache.</summary>
        public void Clear()
        {
            lock (gate)
            {
                map.Clear();
                order.Clear();
            }
        }
    }

    /// <summary>How a feature is drawn, lettered and picked.</summary>
    public enum StripFeatureClass
    {
        /// <summary>Inn, hamlet or village: can be chosen as a destination.</summary>
        Settlement,

        /// <summary>The top of a pass.</summary>
        Pass,

        /// <summary>A viewpoint.</summary>
        Viewpoint,

        /// <summary>A lake (its centre).</summary>
        Lake,

        /// <summary>A roadside camp.</summary>
        Camp,

        /// <summary>A milestone (lettered with its distance).</summary>
        Milestone,

        /// <summary>Anything else (bridges, fords, kinds added later).</summary>
        Other,

        /// <summary>A landmark off the road (tower, chapel, windmill, standing stones, ruin): a small tower sign and its name.</summary>
        Landmark,
    }

    /// <summary>A waypoint placed on the scroll.</summary>
    public sealed class StripFeature
    {
        /// <summary>The place.</summary>
        public Waypoint Waypoint;

        /// <summary>Strip position, metres.</summary>
        public double U, V;

        /// <summary>Drawing and picking class.</summary>
        public StripFeatureClass Class;

        /// <summary>The text lettered beside it (name, or "2.5 km" for a milestone), or null.</summary>
        public string Label;
    }

    /// <summary>The start of a region on the scroll (lettered across the strip).</summary>
    public sealed class StripRegionMark
    {
        /// <summary>The region.</summary>
        public RegionInfo Region;

        /// <summary>Strip height of the road at the region's start.</summary>
        public double V;

        /// <summary>Strip offset of the road there.</summary>
        public double RoadU;

        /// <summary>The name in letter-spaced capitals.</summary>
        public string Lettering;
    }

    /// <summary>
    /// The places and region starts along a stretch of road, laid out on the scroll (built on a worker thread for the overlay
    /// and the pointer). Pure.
    /// </summary>
    public sealed class StripMapContent
    {
        /// <summary>Road distance range covered.</summary>
        public double SFrom, STo;

        /// <summary>The places, by road distance.</summary>
        public readonly List<StripFeature> Features = new List<StripFeature>();

        /// <summary>Region starts inside the range.</summary>
        public readonly List<StripRegionMark> Regions = new List<StripRegionMark>();

        /// <summary>Class of a waypoint kind (unknown kinds are <see cref="StripFeatureClass.Other"/>).</summary>
        public static StripFeatureClass ClassOf(WaypointKind kind)
        {
            switch (kind)
            {
                case WaypointKind.Inn:
                case WaypointKind.Hamlet:
                case WaypointKind.Village:
                case WaypointKind.City:
                    return StripFeatureClass.Settlement;
                case WaypointKind.Pass: return StripFeatureClass.Pass;
                case WaypointKind.Viewpoint: return StripFeatureClass.Viewpoint;
                case WaypointKind.Lake: return StripFeatureClass.Lake;
                case WaypointKind.Camp: return StripFeatureClass.Camp;
                case WaypointKind.Milestone: return StripFeatureClass.Milestone;
                case WaypointKind.Landmark: return StripFeatureClass.Landmark;
                default: return StripFeatureClass.Other;
            }
        }

        /// <summary>Lays out the waypoints and region starts of [<paramref name="sFrom"/>, <paramref name="sTo"/>].</summary>
        public static StripMapContent Build(StripProjection projection, double sFrom, double sTo)
        {
            var content = new StripMapContent { SFrom = sFrom, STo = sTo };
            IEndlessGenerator generator = projection.Generator;
            var found = new List<Waypoint>();
            generator.Waypoints(sFrom, sTo, found);
            foreach (Waypoint w in found)
            {
                if (w == null) continue;
                projection.ToStrip(w.X, w.Z, projection.VOfS(w.S), out double u, out double v);
                StripFeatureClass cls = ClassOf(w.Kind);
                string label = cls == StripFeatureClass.Milestone ? StripMapText.Kilometres(w.S) : w.Name;
                content.Features.Add(new StripFeature { Waypoint = w, U = u, V = v, Class = cls, Label = label });
            }

            RegionInfo r = generator.Region(sFrom);
            int guard = 0;
            while (r.SStart <= sTo && guard++ < 64)
            {
                if (r.SStart >= sFrom)
                {
                    projection.RoadToStrip(r.SStart, out double ru, out double rv);
                    content.Regions.Add(new StripRegionMark { Region = r, V = rv, RoadU = ru, Lettering = StripMapText.Spaced(r.Name) });
                }

                double next = r.SEnd + 0.01;
                if (!(next > r.SStart)) break;
                r = generator.Region(next);
            }

            return content;
        }
    }

    /// <summary>Finding the feature under the pointer. Pure.</summary>
    public static class StripMapPicker
    {
        /// <summary>
        /// Index of the feature of <paramref name="features"/> nearest strip point (<paramref name="u"/>, <paramref name="v"/>)
        /// within <paramref name="tolerance"/> metres, or −1. Settlements win close calls (they are what one picks), milestones
        /// lose them, a lake counts from anywhere near its middle. Features failing <paramref name="visible"/> are skipped.
        /// </summary>
        public static int Pick(IReadOnlyList<StripFeature> features, double u, double v, double tolerance, Func<StripFeature, bool> visible = null)
        {
            int best = -1;
            double bestScore = double.PositiveInfinity;
            if (features == null) return best;
            for (int i = 0; i < features.Count; i++)
            {
                StripFeature f = features[i];
                if (f == null || visible != null && !visible(f)) continue;
                if (f.Class != StripFeatureClass.Settlement && string.IsNullOrEmpty(f.Label)) continue;
                double du = f.U - u, dv = f.V - v;
                double d = Math.Sqrt(du * du + dv * dv);
                double reach = tolerance;
                switch (f.Class)
                {
                    case StripFeatureClass.Settlement:
                        d *= 0.8;
                        break;
                    case StripFeatureClass.Milestone:
                        d *= 1.3;
                        break;
                    case StripFeatureClass.Lake:
                        d = Math.Max(0.0, d - Math.Min(f.Waypoint != null ? f.Waypoint.Radius * 0.45 : 0.0, 70.0));
                        break;
                }

                if (d > reach || d >= bestScore) continue;
                bestScore = d;
                best = i;
            }

            return best;
        }
    }

    /// <summary>Ink of a label.</summary>
    public enum StripInk
    {
        /// <summary>Dark brown (settlements).</summary>
        Ink,

        /// <summary>Faded brown (features, milestones, regions).</summary>
        Faded,

        /// <summary>Blue-grey (water).</summary>
        Water,
    }

    /// <summary>Where and how a label is lettered, in sheet pixels (1 px = <see cref="StripMapLayout.MetresPerPixel"/> m).</summary>
    public struct StripLabelStyle
    {
        /// <summary>Offset of the label's pivot from the feature, pixels (x right, y up).</summary>
        public float OffsetX, OffsetY;

        /// <summary>Pivot of the label (0..1; 0 = left / bottom).</summary>
        public float PivotX, PivotY;

        /// <summary>Letter height, pixels.</summary>
        public float Size;

        /// <summary>Bold capitals (villages).</summary>
        public bool Bold;

        /// <summary>Ink.</summary>
        public StripInk Ink;

        /// <summary>Slight hand-lettered tilt, degrees.</summary>
        public float Tilt;

        /// <summary>Lowest opacity in unseen country (settlements are hearsay: still faintly lettered; others vanish).</summary>
        public float MinAlpha;
    }

    /// <summary>Text on the scroll (invariant culture: the same everywhere). Pure.</summary>
    public static class StripMapText
    {
        /// <summary>The lettering rule for <paramref name="f"/> (labels sit on the side of the symbol away from the road's middle).</summary>
        public static StripLabelStyle StyleFor(StripFeature f, double roadU)
        {
            bool rightSide = f.U >= roadU ? f.U < StripMapLayout.HalfWidth - 150.0 : f.U < -StripMapLayout.HalfWidth + 150.0;
            float side = rightSide ? 1f : -1f;
            float pivot = rightSide ? 0f : 1f;
            int id = f.Waypoint != null ? f.Waypoint.Id : 0;
            float tilt = ((id * 37 % 7 + 7) % 7 - 3) * 0.7f;
            switch (f.Class)
            {
                case StripFeatureClass.Settlement:
                    bool village = f.Waypoint != null && f.Waypoint.IsVillageLike;
                    return new StripLabelStyle { OffsetX = side * (village ? 15f : 13f), OffsetY = 0f, PivotX = pivot, PivotY = 0.5f, Size = village ? 18f : 16f, Bold = village, Ink = StripInk.Ink, Tilt = tilt, MinAlpha = 0.5f };
                case StripFeatureClass.Pass:
                    return new StripLabelStyle { OffsetX = 0f, OffsetY = 22f, PivotX = 0.5f, PivotY = 0f, Size = 14f, Ink = StripInk.Faded, MinAlpha = 0f };
                case StripFeatureClass.Lake:
                    return new StripLabelStyle { OffsetX = 0f, OffsetY = 0f, PivotX = 0.5f, PivotY = 0.5f, Size = 15f, Ink = StripInk.Water, Tilt = -4f, MinAlpha = 0f };
                case StripFeatureClass.Milestone:
                    return new StripLabelStyle { OffsetX = side * 12f, OffsetY = 0f, PivotX = pivot, PivotY = 0.5f, Size = 11f, Ink = StripInk.Faded, MinAlpha = 0f };
                case StripFeatureClass.Landmark:
                    return new StripLabelStyle { OffsetX = side * 11f, OffsetY = 0f, PivotX = pivot, PivotY = 0.5f, Size = 14f, Ink = StripInk.Ink, Tilt = tilt * 0.5f, MinAlpha = 0f };
                default:
                    return new StripLabelStyle { OffsetX = side * 10f, OffsetY = 0f, PivotX = pivot, PivotY = 0.5f, Size = 13f, Ink = StripInk.Faded, Tilt = tilt * 0.5f, MinAlpha = 0f };
            }
        }

        /// <summary>The region lettering's position: across the strip on the side with more room, just above the boundary (strip metres).</summary>
        public static void RegionLabelAt(StripRegionMark mark, out double u, out double v)
        {
            u = mark.RoadU > 0.0 ? -StripMapLayout.HalfWidth * 0.48 : StripMapLayout.HalfWidth * 0.48;
            v = mark.V + 14.0;
        }

        /// <summary>"850 m" below a kilometre (to 10 m), "1.4 km" above.</summary>
        public static string Distance(double metres)
        {
            metres = Math.Abs(metres);
            if (metres < 995.0) return Math.Max(10.0, Math.Round(metres / 10.0) * 10.0).ToString("0", CultureInfo.InvariantCulture) + " m";
            return (metres / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>"The Drowsy Fox — 1.4 km on", "… — 600 m back", "… — here" (within 30 m).</summary>
        public static string Describe(string name, double featureS, double travellerS)
        {
            double d = featureS - travellerS;
            string where = Math.Abs(d) < 30.0 ? "here" : Distance(d) + (d > 0 ? " on" : " back");
            return string.IsNullOrEmpty(name) ? where : name + " — " + where;
        }

        /// <summary>A milestone's lettering: "3 km", "2.5 km", "−1 km".</summary>
        public static string Kilometres(double s)
        {
            double km = Math.Round(s / 100.0) / 10.0;
            string text = Math.Abs(km).ToString("0.#", CultureInfo.InvariantCulture) + " km";
            return km < 0 ? "−" + text : text;
        }

        /// <summary>"The Wexmoor Downs" → "T H E   W E X M O O R   D O W N S".</summary>
        public static string Spaced(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string upper = name.ToUpperInvariant();
            var sb = new StringBuilder(upper.Length * 2);
            for (int i = 0; i < upper.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(upper[i]);
            }

            return sb.ToString();
        }
    }
}
