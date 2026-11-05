using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MedievalZen.Endless;

namespace MedievalZen.Review
{
    /// <summary>A named cost of one frame: a first-party timing scope (<c>FrameCost</c>) or an engine profiler marker.</summary>
    public readonly struct CostEntry
    {
        public readonly string Name;
        public readonly float Ms;

        /// <summary>True when this scope ran for the first time in the session in that frame (a first-use cost).</summary>
        public readonly bool First;

        public CostEntry(string name, float ms, bool first = false)
        {
            Name = name;
            Ms = ms;
            First = first;
        }

        public bool IsEmpty => Name == null;

        public override string ToString() => IsEmpty ? string.Empty : $"{Name} {Ms.ToString("0.0", CultureInfo.InvariantCulture)} ms{(First ? " (first)" : string.Empty)}";
    }

    /// <summary>One frame of the walking benchmark (times in milliseconds; <see cref="T"/> in seconds since the walk began).</summary>
    public struct WalkFrame
    {
        public float T;
        public double S;
        public string Region;
        public float FrameMs, CpuMainMs, RenderMs, GpuMs, PresentWaitMs, ApplyMs, SliceMs;

        /// <summary>The longest streaming slice of the frame when it took more than <see cref="ReviewWalkBench.SliceLabelMs"/>, else null.</summary>
        public string Slice;

        /// <summary>The three largest first-party scopes of the work this frame time covers (the previous frame's), largest first.</summary>
        public CostEntry Scope1, Scope2, Scope3;

        /// <summary>Engine markers of that work (garbage collection, shader programs, loading...), largest first; only for a frame
        /// over <see cref="ReviewWalkBench.CulpritMs"/> (null otherwise).</summary>
        public CostEntry[] Engine;

        /// <summary>Named first uses in that work (<c>FrameCost.FirstUse</c>: the first instantiate of a prefab...), null when none.</summary>
        public string[] Firsts;

        /// <summary>Garbage collections that ended in that work (the change of the collection count; 0 when none).</summary>
        public int Gc;

        /// <summary>True while the streamer held the traveller on black (outrun): kept in the CSV, left out of the frame statistics.</summary>
        public bool Held;
    }

    /// <summary>One streaming hold during the walk: where, when (seconds since the walk began) and for how long.</summary>
    public struct WalkHold
    {
        public double S;
        public float Start, Seconds;
        public string Region;
    }

    /// <summary>A place along the walked road (inn, village, hamlet, landmark, bridge, ford).</summary>
    public struct WalkPlace
    {
        public string Kind, Name;
        public double S;
    }

    /// <summary>Settings of a walk: road metres to cover, the speed multiple of the auto-travel pace and where to start (NaN: here).</summary>
    public sealed class WalkBenchOptions
    {
        public double Distance = 2000.0;
        public float SpeedScale = 6f;
        public double StartS = double.NaN;
        public ReviewWeather Weather = ReviewWeather.Clear;
    }

    /// <summary>
    /// The pure part of the walking benchmark (unit tested): percentiles, hitch counts, the per-region table, the worst frames
    /// with the nearest place, the file names and the JSON / CSV text. No Unity calls.
    /// </summary>
    public static class ReviewWalkBench
    {
        /// <summary>Frame-time thresholds counted, ms (60, 40, 30 and 20 fps).</summary>
        public static readonly float[] Thresholds = { 16.7f, 25f, 33.3f, 50f };

        /// <summary>A frame over this many ms is a hitch in the per-region table.</summary>
        public const float HitchMs = 33.3f;

        /// <summary>A streaming slice over this many ms is named in the frame record.</summary>
        public const float SliceLabelMs = 3f;

        /// <summary>How many worst frames the report lists.</summary>
        public const int WorstCount = 25;

        /// <summary>A frame over this many ms keeps its engine markers and names its culprit in the CSV.</summary>
        public const float CulpritMs = 16.7f;

        /// <summary>
        /// Engine markers recorded for context only (where in the frame the time went: the whole player loop, scripts, coroutines,
        /// animation, physics, UI, rendering, waiting on the GPU). Listed with a slow frame's markers, never named its culprit.
        /// </summary>
        public static readonly string[] PhaseMarkers =
        {
            "PlayerLoop", "Update.ScriptRunBehaviourUpdate", "Update.ScriptRunDelayedDynamicFrameRate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "FixedUpdate.PhysicsFixedUpdate", "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.DirectorUpdateAnimationEnd",
            "PostLateUpdate.PlayerUpdateCanvases", "Camera.Render", "Inl_UniversalRenderPipeline.RenderSingleCameraInternal",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.WaitForRenderThread", "Gfx.PresentFrame",
        };

        /// <summary>True for a context marker (<see cref="PhaseMarkers"/>).</summary>
        public static bool IsPhase(string name) => name != null && Array.IndexOf(PhaseMarkers, name) >= 0;

        /// <summary>
        /// The largest named cost of a frame: its largest scope or cause marker (phase markers left out), whichever took longer;
        /// empty when none.
        /// </summary>
        public static CostEntry Culprit(in WalkFrame f)
        {
            CostEntry best = f.Scope1;
            if (f.Engine != null)
            {
                foreach (CostEntry e in f.Engine)
                {
                    if (!e.IsEmpty && !IsPhase(e.Name) && (best.IsEmpty || e.Ms > best.Ms)) best = e;
                }
            }

            return best;
        }

        /// <summary>
        /// The culprit fields of one frame under <paramref name="prefix"/> (the tour sidecar's worst frame): <c>Culprit</c> (the
        /// largest scope or marker, "name 12.3 ms", null when none), <c>Scopes</c> and <c>EngineMarkers</c> ({name, ms} rows).
        /// </summary>
        public static void CulpritFields(List<KeyValuePair<string, object>> fields, string prefix, CostEntry scope1, CostEntry scope2, CostEntry scope3,
            CostEntry[] engine)
        {
            CostEntry culprit = Culprit(new WalkFrame { Scope1 = scope1, Engine = engine });
            fields.Add(new KeyValuePair<string, object>(prefix + "Culprit", culprit.IsEmpty ? null : culprit.ToString()));
            fields.Add(new KeyValuePair<string, object>(prefix + "Scopes", CostRows(scope1, scope2, scope3)));
            fields.Add(new KeyValuePair<string, object>(prefix + "EngineMarkers", CostRows(engine)));
        }

        /// <summary>
        /// The CSV culprit of a slow frame: the culprit, then its named first uses and garbage collections when there were any
        /// ("PeopleManager.Instantiate 40.1 ms (first) [first: Instantiate Villager_Farmer; gc x1]").
        /// </summary>
        public static string CulpritText(in WalkFrame f)
        {
            string text = Culprit(f).ToString();
            bool firsts = f.Firsts != null && f.Firsts.Length > 0;
            if (!firsts && f.Gc <= 0) return text;
            var b = new StringBuilder(text);
            if (b.Length > 0) b.Append(' ');
            b.Append('[');
            if (firsts) b.Append("first: ").Append(string.Join("; ", f.Firsts));
            if (f.Gc > 0) b.Append(firsts ? "; " : string.Empty).Append("gc x").Append(f.Gc.ToString(CultureInfo.InvariantCulture));
            return b.Append(']').ToString();
        }

        /// <summary>The entries as JSON rows ({name, ms}, and "first": true for a first run), empty entries left out.</summary>
        public static List<object> CostRows(params CostEntry[] entries)
        {
            var rows = new List<object>();
            if (entries == null) return rows;
            foreach (CostEntry e in entries)
            {
                if (e.IsEmpty) continue;
                var row = new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("name", e.Name),
                    new KeyValuePair<string, object>("ms", e.Ms),
                };
                if (e.First) row.Add(new KeyValuePair<string, object>("first", true));
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>A worst frame names the nearest place within this many road metres.</summary>
        public const double PlaceReach = 300.0;

        /// <summary>Linear-interpolated percentile (<paramref name="p"/> in 0..100) of an ascending list; NaN when empty.</summary>
        public static float Percentile(IReadOnlyList<float> sorted, double p)
        {
            if (sorted == null || sorted.Count == 0) return float.NaN;
            double rank = Math.Max(0.0, Math.Min(100.0, p)) / 100.0 * (sorted.Count - 1);
            int lo = (int)Math.Floor(rank), hi = Math.Min(sorted.Count - 1, lo + 1);
            return (float)(sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo));
        }

        /// <summary>Mean of the values (NaN ignored; NaN when none).</summary>
        public static float Mean(IEnumerable<float> values)
        {
            double sum = 0.0;
            int n = 0;
            foreach (float v in values)
            {
                if (float.IsNaN(v)) continue;
                sum += v;
                n++;
            }

            return n > 0 ? (float)(sum / n) : float.NaN;
        }

        /// <summary>Frames above <paramref name="ms"/> (held frames left out).</summary>
        public static int CountOver(IReadOnlyList<WalkFrame> frames, float ms)
        {
            int n = 0;
            foreach (WalkFrame f in frames)
            {
                if (!f.Held && f.FrameMs > ms) n++;
            }

            return n;
        }

        /// <summary>A count per kilometre of <paramref name="metres"/> (0 when no distance).</summary>
        public static float PerKm(int count, double metres) => metres > 1.0 ? (float)(count * 1000.0 / metres) : 0f;

        /// <summary>Road metres each frame stands for (the distance since the previous frame; 0 for the first).</summary>
        public static double MetresOf(IReadOnlyList<WalkFrame> frames, int i) => i > 0 ? Math.Abs(frames[i].S - frames[i - 1].S) : 0.0;

        /// <summary>Indices of the <paramref name="count"/> longest frames (held frames left out), longest first, earlier first on ties.</summary>
        public static List<int> Worst(IReadOnlyList<WalkFrame> frames, int count)
        {
            var idx = new List<int>();
            for (int i = 0; i < frames.Count; i++)
            {
                if (!frames[i].Held) idx.Add(i);
            }

            idx.Sort((a, b) =>
            {
                int c = frames[b].FrameMs.CompareTo(frames[a].FrameMs);
                return c != 0 ? c : a.CompareTo(b);
            });
            if (idx.Count > count) idx.RemoveRange(count, idx.Count - count);
            return idx;
        }

        /// <summary>Index of the place nearest to road distance <paramref name="s"/> within <paramref name="reach"/> metres, or −1.</summary>
        public static int NearestPlace(IReadOnlyList<WalkPlace> places, double s, double reach = PlaceReach)
        {
            int best = -1;
            double bestGap = reach;
            for (int i = 0; places != null && i < places.Count; i++)
            {
                double gap = Math.Abs(places[i].S - s);
                if (gap > bestGap) continue;
                bestGap = gap;
                best = i;
            }

            return best;
        }

        /// <summary>One row of the per-region table.</summary>
        public struct RegionRow
        {
            public string Region;
            public int Frames, Hitches;
            public double Metres;
            public float MeanFrame, P95Frame, MeanGpu, HitchesPerKm;
        }

        /// <summary>Per region kind (in order of first appearance): metres, frames, mean and p95 frame, mean GPU, hitches per km.</summary>
        public static List<RegionRow> Regions(IReadOnlyList<WalkFrame> frames)
        {
            var order = new List<string>();
            var times = new Dictionary<string, List<float>>();
            var gpus = new Dictionary<string, List<float>>();
            var metres = new Dictionary<string, double>();
            for (int i = 0; i < frames.Count; i++)
            {
                string r = frames[i].Region ?? "unknown";
                if (!times.ContainsKey(r))
                {
                    order.Add(r);
                    times[r] = new List<float>();
                    gpus[r] = new List<float>();
                    metres[r] = 0.0;
                }

                metres[r] += MetresOf(frames, i);
                if (frames[i].Held) continue;
                times[r].Add(frames[i].FrameMs);
                gpus[r].Add(frames[i].GpuMs);
            }

            var rows = new List<RegionRow>();
            foreach (string r in order)
            {
                List<float> t = times[r];
                int hitches = 0;
                foreach (float v in t)
                {
                    if (v > HitchMs) hitches++;
                }

                var sorted = new List<float>(t);
                sorted.Sort();
                rows.Add(new RegionRow
                {
                    Region = r, Frames = t.Count, Metres = metres[r], MeanFrame = Mean(t), P95Frame = Percentile(sorted, 95.0),
                    MeanGpu = Mean(gpus[r]), Hitches = hitches, HitchesPerKm = PerKm(hitches, metres[r]),
                });
            }

            return rows;
        }

        /// <summary><c>walk_&lt;stamp&gt;_s&lt;seed&gt;_&lt;from&gt;-&lt;to&gt;m_x&lt;speed&gt;.json</c> (road metres rounded, speed as 6, 12 or 0.5).</summary>
        public static string FileName(string stamp, int seed, double fromS, double toS, float speedScale)
        {
            string from = Math.Round(fromS).ToString(CultureInfo.InvariantCulture);
            string to = Math.Round(toS).ToString(CultureInfo.InvariantCulture);
            return $"walk_{stamp}_s{seed}_{from}-{to}m_x{speedScale.ToString("0.##", CultureInfo.InvariantCulture)}.json";
        }

        /// <summary>
        /// The report's fields after the caller's <paramref name="head"/> (settings, weather, hour): distance and time, frame
        /// statistics, threshold counts (total and per km), the per-region table, the holds and the worst frames.
        /// </summary>
        public static List<KeyValuePair<string, object>> Report(List<KeyValuePair<string, object>> head, IReadOnlyList<WalkFrame> frames,
            IReadOnlyList<WalkHold> holds, IReadOnlyList<WalkPlace> places)
        {
            var f = head ?? new List<KeyValuePair<string, object>>();
            void Add(string k, object v) => f.Add(new KeyValuePair<string, object>(k, v));
            double metres = 0.0;
            for (int i = 1; i < frames.Count; i++) metres += MetresOf(frames, i);
            var times = new List<float>();
            var cpu = new List<float>();
            var render = new List<float>();
            var gpu = new List<float>();
            var wait = new List<float>();
            var apply = new List<float>();
            int held = 0;
            foreach (WalkFrame w in frames)
            {
                if (w.Held)
                {
                    held++;
                    continue;
                }

                times.Add(w.FrameMs);
                cpu.Add(w.CpuMainMs);
                render.Add(w.RenderMs);
                gpu.Add(w.GpuMs);
                wait.Add(w.PresentWaitMs);
                apply.Add(w.ApplyMs);
            }

            var sorted = new List<float>(times);
            sorted.Sort();
            float seconds = frames.Count > 0 ? frames[frames.Count - 1].T - frames[0].T : 0f;
            Add("metresCovered", metres);
            Add("secondsTaken", seconds);
            Add("metresPerSecond", seconds > 0f ? metres / seconds : 0.0);
            Add("frames", frames.Count);
            Add("framesHeld", held);
            Add("frameMsMean", Mean(times));
            Add("frameMsP50", Percentile(sorted, 50.0));
            Add("frameMsP95", Percentile(sorted, 95.0));
            Add("frameMsP99", Percentile(sorted, 99.0));
            Add("frameMsMax", sorted.Count > 0 ? sorted[sorted.Count - 1] : float.NaN);
            Add("cpuMainMsMean", Mean(cpu));
            Add("renderThreadMsMean", Mean(render));
            Add("gpuMsMean", Mean(gpu));
            Add("presentWaitMsMean", Mean(wait));
            Add("applyMsMean", Mean(apply));
            var over = new List<KeyValuePair<string, object>>();
            foreach (float t in Thresholds)
            {
                int n = CountOver(frames, t);
                string key = t.ToString("0.#", CultureInfo.InvariantCulture);
                over.Add(new KeyValuePair<string, object>(key, new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("frames", n),
                    new KeyValuePair<string, object>("perKm", PerKm(n, metres)),
                }));
            }

            Add("framesOverMs", over);
            var regionRows = new List<object>();
            foreach (RegionRow r in Regions(frames))
            {
                regionRows.Add(new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("region", r.Region),
                    new KeyValuePair<string, object>("metres", r.Metres),
                    new KeyValuePair<string, object>("frames", r.Frames),
                    new KeyValuePair<string, object>("frameMsMean", r.MeanFrame),
                    new KeyValuePair<string, object>("frameMsP95", r.P95Frame),
                    new KeyValuePair<string, object>("gpuMsMean", r.MeanGpu),
                    new KeyValuePair<string, object>("hitchesOver33ms", r.Hitches),
                    new KeyValuePair<string, object>("hitchesPerKm", r.HitchesPerKm),
                });
            }

            Add("regions", regionRows);
            var holdRows = new List<object>();
            float holdSeconds = 0f;
            foreach (WalkHold h in holds ?? Array.Empty<WalkHold>())
            {
                holdSeconds += h.Seconds;
                holdRows.Add(new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("roadDistance", h.S),
                    new KeyValuePair<string, object>("atSecond", h.Start),
                    new KeyValuePair<string, object>("seconds", h.Seconds),
                    new KeyValuePair<string, object>("region", h.Region),
                });
            }

            Add("holdCount", holdRows.Count);
            Add("holdSeconds", holdSeconds);
            Add("holds", holdRows);
            var worstRows = new List<object>();
            foreach (int i in Worst(frames, WorstCount))
            {
                WalkFrame w = frames[i];
                int p = NearestPlace(places, w.S);
                worstRows.Add(new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("frameMs", w.FrameMs),
                    new KeyValuePair<string, object>("atSecond", w.T),
                    new KeyValuePair<string, object>("roadDistance", w.S),
                    new KeyValuePair<string, object>("region", w.Region),
                    new KeyValuePair<string, object>("cpuMainMs", w.CpuMainMs),
                    new KeyValuePair<string, object>("renderThreadMs", w.RenderMs),
                    new KeyValuePair<string, object>("gpuMs", w.GpuMs),
                    new KeyValuePair<string, object>("presentWaitMs", w.PresentWaitMs),
                    new KeyValuePair<string, object>("applyMs", w.ApplyMs),
                    new KeyValuePair<string, object>("slice", w.Slice),
                    new KeyValuePair<string, object>("sliceMs", w.SliceMs),
                    new KeyValuePair<string, object>("nearestPlace", p >= 0 ? $"{places[p].Kind} {places[p].Name}".Trim() : null),
                    new KeyValuePair<string, object>("nearestPlaceGap", p >= 0 ? places[p].S - w.S : double.NaN),
                    new KeyValuePair<string, object>("culprit", Culprit(w).IsEmpty ? null : Culprit(w).ToString()),
                    new KeyValuePair<string, object>("topScopes", CostRows(w.Scope1, w.Scope2, w.Scope3)),
                    new KeyValuePair<string, object>("engineMarkers", CostRows(w.Engine)),
                    new KeyValuePair<string, object>("firsts", w.Firsts != null ? new List<object>(w.Firsts) : new List<object>()),
                    new KeyValuePair<string, object>("gcCollections", w.Gc),
                });
            }

            Add("worstFrames", worstRows);
            return f;
        }

        /// <summary>Every frame as CSV (invariant culture, one header line).</summary>
        public static string Csv(IReadOnlyList<WalkFrame> frames)
        {
            var b = new StringBuilder("t,roadDistance,region,frameMs,cpuMainMs,renderThreadMs,gpuMs,presentWaitMs,applyMs,sliceMs,slice,held,culprit\n");
            CultureInfo c = CultureInfo.InvariantCulture;
            foreach (WalkFrame f in frames)
            {
                b.Append(f.T.ToString("0.###", c)).Append(',').Append(f.S.ToString("0.##", c)).Append(',').Append(CsvText(f.Region)).Append(',')
                    .Append(Num(f.FrameMs)).Append(',').Append(Num(f.CpuMainMs)).Append(',').Append(Num(f.RenderMs)).Append(',')
                    .Append(Num(f.GpuMs)).Append(',').Append(Num(f.PresentWaitMs)).Append(',').Append(Num(f.ApplyMs)).Append(',')
                    .Append(Num(f.SliceMs)).Append(',').Append(CsvText(f.Slice)).Append(',').Append(f.Held ? '1' : '0').Append(',')
                    .Append(f.FrameMs > CulpritMs ? CsvText(CulpritText(f)) : string.Empty).Append('\n');
            }

            return b.ToString();
        }

        private static string Num(float v) => float.IsNaN(v) || float.IsInfinity(v) ? string.Empty : v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string CsvText(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace(',', ' ').Replace('"', ' ').Replace('\n', ' ');

        /// <summary>The three console lines of a finished walk.</summary>
        public static string[] SummaryLines(IReadOnlyList<KeyValuePair<string, object>> report, string file)
        {
            string V(string key)
            {
                foreach (KeyValuePair<string, object> kv in report)
                {
                    if (kv.Key != key) continue;
                    return kv.Value is IFormattable fm ? fm.ToString("0.#", CultureInfo.InvariantCulture) : kv.Value?.ToString() ?? "-";
                }

                return "-";
            }

            float km = (float)(Number(report, "metresCovered") / 1000.0);
            string per(int i)
            {
                foreach (KeyValuePair<string, object> kv in report)
                {
                    if (kv.Key != "framesOverMs" || !(kv.Value is List<KeyValuePair<string, object>> list) || i >= list.Count) continue;
                    var row = (List<KeyValuePair<string, object>>)list[i].Value;
                    return $"{row[0].Value} (>{list[i].Key} ms)";
                }

                return "-";
            }

            return new[]
            {
                $"Walk benchmark: {V("metresCovered")} m in {V("secondsTaken")} s ({V("metresPerSecond")} m/s, x{V("speedScale")}), {V("frames")} frames, {V("holdCount")} streaming holds ({V("holdSeconds")} s).",
                $"  frame ms mean {V("frameMsMean")}, p50 {V("frameMsP50")}, p95 {V("frameMsP95")}, p99 {V("frameMsP99")}, max {V("frameMsMax")}; GPU {V("gpuMsMean")}, CPU main {V("cpuMainMsMean")}, present wait {V("presentWaitMsMean")}.",
                $"  frames over: {per(0)}, {per(1)}, {per(2)}, {per(3)} over {km:0.00} km. File: {file}",
            };
        }

        /// <summary>A numeric field of a sidecar or report (float, double, int or long), NaN when missing.</summary>
        public static double Number(IReadOnlyList<KeyValuePair<string, object>> fields, string key)
        {
            if (fields == null) return double.NaN;
            foreach (KeyValuePair<string, object> kv in fields)
            {
                if (kv.Key != key) continue;
                switch (kv.Value)
                {
                    case float v: return v;
                    case double v: return v;
                    case int v: return v;
                    case long v: return v;
                }
            }

            return double.NaN;
        }
    }

    /// <summary>One capture of a tour, as the end-of-tour summary needs it.</summary>
    public struct TourShotRecord
    {
        public int Stop;
        public double S;
        public string Label, Region, Shot;
        public bool LowSun;
        public float FrameMsStop, GpuMs, PresentWaitMs, TreeTriangles;

        /// <summary>Landmark stops: its name (null elsewhere), whether it was in sight and how the eye was moved.</summary>
        public string Landmark;

        public bool? LandmarkVisible;
        public float EyeRaise, EyeShift, EyeLateral;
    }

    /// <summary>The end-of-tour summary (pure): per region kind, per stop and per landmark.</summary>
    public static class ReviewTourSummary
    {
        /// <summary>The summary's file name: <c>tour_&lt;stamp&gt;[_places]_summary.json</c>.</summary>
        public static string FileName(string stamp, ReviewTourKind kind) => $"tour_{stamp}{(kind == ReviewTourKind.Places ? "_places" : string.Empty)}_summary.json";

        /// <summary>The summary's fields after <paramref name="head"/>.</summary>
        public static List<KeyValuePair<string, object>> Build(List<KeyValuePair<string, object>> head, IReadOnlyList<TourShotRecord> shots)
        {
            var f = head ?? new List<KeyValuePair<string, object>>();
            f.Add(new KeyValuePair<string, object>("captures", shots.Count));
            var order = new List<string>();
            var byRegion = new Dictionary<string, List<TourShotRecord>>();
            foreach (TourShotRecord r in shots)
            {
                string key = r.Region ?? "unknown";
                if (!byRegion.TryGetValue(key, out List<TourShotRecord> list))
                {
                    byRegion[key] = list = new List<TourShotRecord>();
                    order.Add(key);
                }

                list.Add(r);
            }

            var regions = new List<object>();
            foreach (string key in order)
            {
                List<TourShotRecord> list = byRegion[key];
                regions.Add(new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("region", key),
                    new KeyValuePair<string, object>("captures", list.Count),
                    new KeyValuePair<string, object>("frameMsStopMean", Mean(list, r => r.FrameMsStop)),
                    new KeyValuePair<string, object>("frameMsStopMax", Max(list, r => r.FrameMsStop)),
                    new KeyValuePair<string, object>("gpuMsMean", Mean(list, r => r.GpuMs)),
                    new KeyValuePair<string, object>("gpuMsMax", Max(list, r => r.GpuMs)),
                    new KeyValuePair<string, object>("presentWaitMsMean", Mean(list, r => r.PresentWaitMs)),
                    new KeyValuePair<string, object>("presentWaitMsMax", Max(list, r => r.PresentWaitMs)),
                    new KeyValuePair<string, object>("treeTrianglesMean", Mean(list, r => r.TreeTriangles)),
                    new KeyValuePair<string, object>("treeTrianglesMax", Max(list, r => r.TreeTriangles)),
                });
            }

            f.Add(new KeyValuePair<string, object>("regions", regions));
            var stops = new List<object>();
            var landmarks = new List<object>();
            var seen = new HashSet<int>();
            foreach (TourShotRecord r in shots)
            {
                if (!seen.Add(r.Stop)) continue;
                var all = new List<TourShotRecord>();
                foreach (TourShotRecord o in shots)
                {
                    if (o.Stop == r.Stop) all.Add(o);
                }

                stops.Add(new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("stop", r.Stop),
                    new KeyValuePair<string, object>("roadDistance", r.S),
                    new KeyValuePair<string, object>("label", r.Label),
                    new KeyValuePair<string, object>("region", r.Region),
                    new KeyValuePair<string, object>("frameMsNoon", Mean(all.FindAll(o => !o.LowSun), o => o.FrameMsStop)),
                    new KeyValuePair<string, object>("frameMsLowSun", Mean(all.FindAll(o => o.LowSun), o => o.FrameMsStop)),
                });
                if (r.Landmark == null) continue;
                landmarks.Add(new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("name", r.Landmark),
                    new KeyValuePair<string, object>("roadDistance", r.S),
                    new KeyValuePair<string, object>("visible", r.LandmarkVisible),
                    new KeyValuePair<string, object>("eyeRaise", r.EyeRaise),
                    new KeyValuePair<string, object>("eyeShift", r.EyeShift),
                    new KeyValuePair<string, object>("eyeLateral", r.EyeLateral),
                });
            }

            f.Add(new KeyValuePair<string, object>("stops", stops));
            f.Add(new KeyValuePair<string, object>("landmarks", landmarks));
            return f;
        }

        private static float Mean(List<TourShotRecord> list, Func<TourShotRecord, float> field)
        {
            var values = new List<float>();
            foreach (TourShotRecord r in list) values.Add(field(r));
            return ReviewWalkBench.Mean(values);
        }

        private static float Max(List<TourShotRecord> list, Func<TourShotRecord, float> field)
        {
            float max = float.NaN;
            foreach (TourShotRecord r in list)
            {
                float v = field(r);
                if (!float.IsNaN(v) && (float.IsNaN(max) || v > max)) max = v;
            }

            return max;
        }
    }

    /// <summary>
    /// The trees near the eye of a landmark shot (pure): trunks as cylinders from the ground to the crown base, crowns as
    /// cylinders from the crown base to the top, tested against the first <see cref="Reach"/> metres of each line of sight; and
    /// the choice of the eye among the positions the road offers and raised eyes.
    /// </summary>
    public static class ReviewSight
    {
        /// <summary>One tree near the eye (absolute x, z; heights absolute).</summary>
        public struct Tree
        {
            public double X, Z;
            public float Ground, CrownBottom, Top, TrunkRadius, CrownRadius;
        }

        /// <summary>Share of the landmark's footprint radius kept clear of near trees on either side of the line (0.5: the middle half of the structure, not only its centre line; 1 found no eye at all for Crow Abbey, seed 12345, in a wood).</summary>
        public const float LandmarkWiden = 0.5f;

        /// <summary>Metres of each line tested against the real trees.</summary>
        public const double Reach = 80.0;

        /// <summary>Eye raises tried, metres above <see cref="ReviewTourPlan.LandmarkEyeHeight"/> (positions first, then higher).</summary>
        public static readonly float[] Raises = { 0f, 1.5f, 3f, 4.5f, 6f, 8f };

        /// <summary>Shifts along the road tried, metres (nearest first).</summary>
        public static readonly float[] Shifts = { 0f, 3f, -3f, 6f, -6f, 10f, -10f };

        /// <summary>Offsets across the road tried, metres (right positive).</summary>
        public static readonly float[] Laterals = { 0f, 1.5f, -1.5f };

        /// <summary>
        /// Adds the trees of a tile blueprint (origin <paramref name="ox"/>, <paramref name="oz"/>) within <paramref name="reach"/> of
        /// (cx, cz): bushes, ferns and undergrowth left out; trunks from 1 m below the pivot to the crown base (at least 1.2 m) with
        /// 15 cm to spare, crowns from there to 90 % of the height over the whole measured radius (as the shapes of
        /// <see cref="FarTreeStyles.Defaults"/>).
        /// </summary>
        public static void AddTrees(List<Tree> into, TileBlueprint bp, double ox, double oz, double cx, double cz, double reach)
        {
            if (bp?.Trees == null) return;
            foreach (MedievalZen.World.TreePlacement tree in bp.Trees)
            {
                if (MedievalZen.World.PropRole.IsInstancedPlant(tree.role) || MedievalZen.World.PropRole.IsForestExtra(tree.role)) continue;
                double x = ox + tree.position.x, z = oz + tree.position.z;
                if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > reach * reach) continue;
                FarTreeStyle st = FarTreeStyles.Defaults.Resolve(tree.role, tree.variant);
                float k = tree.scale > 0f ? tree.scale : 1f;
                float ground = tree.position.y;
                into.Add(new Tree
                {
                    X = x, Z = z, Ground = ground - 1f, CrownBottom = ground + Math.Max(1.2f, st.CrownBase * k), Top = ground + st.Height * k * 0.9f,
                    // The whole measured radius (not 85 %): leaning trunks carry the crown off the pivot.
                    TrunkRadius = Math.Max(0.3f, st.TrunkRadius * k) + 0.15f, CrownRadius = Math.Max(1.5f, st.Radius * k),
                });
            }
        }

        /// <summary>True when no trunk or crown in <paramref name="trees"/> cuts the first <paramref name="reach"/> metres of the line a→b.</summary>
        public static bool Clear(IReadOnlyList<Tree> trees, double ax, double az, float ay, double bx, double bz, float by, double reach = Reach, float widen = 0f,
            float frameSlopeAcross = 0f, float frameSlopeUp = 0f)
        {
            if (trees == null || trees.Count == 0) return true;
            double dx = bx - ax, dz = bz - az, d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) return true;
            double ux = dx / d, uz = dz / d, len = Math.Min(reach, d);
            foreach (Tree t in trees)
            {
                double px = t.X - ax, pz = t.Z - az;
                double along = px * ux + pz * uz;
                double off = Math.Abs(px * uz - pz * ux);
                double a = Math.Max(0.0, Math.Min(len, along));
                // The structure is seen as a wedge, not a line: an obstacle counts within widen x (its distance / the target's) of the line.
                // The frame's central part is a pyramid round the line: frameSlope x the distance across and up (2026-10-06: the line alone
                // let a crown 3 m beside it at 60 m fill the 15° frame of Wick Windmill).
                float grow = (widen > 0f ? (float)(widen * a / d) : 0f) + (float)(frameSlopeAcross * a);
                float rise = (float)(frameSlopeUp * a);
                if (Cuts(along, off, t.TrunkRadius + grow, len, d, ay, by, t.Ground - rise, t.CrownBottom + rise)) return false;
                if (Cuts(along, off, t.CrownRadius + grow, len, d, ay, by, t.CrownBottom - rise, t.Top + rise)) return false;
            }

            return true;
        }

        /// <summary>Share of the frame (each way from its centre) kept clear of near trees: the central third.</summary>
        public const float FrameClearShare = 1f / 3f;

        /// <summary>
        /// The slopes of the frame's clear central part for a vertical field of view <paramref name="fovDeg"/> and
        /// <paramref name="aspect"/> (width / height): metres across and up per metre along the line.
        /// </summary>
        public static void FrameSlopes(float fovDeg, float aspect, out float across, out float up)
        {
            double half = Math.Tan(Math.Max(0.1, fovDeg) * 0.5 * Math.PI / 180.0);
            up = (float)(half * FrameClearShare);
            across = (float)(half * Math.Max(0.5, aspect) * FrameClearShare);
        }

        private static bool Cuts(double along, double off, float radius, double len, double d, float ay, float by, float bottom, float top)
        {
            if (radius <= 0f || off >= radius || top <= bottom) return false;
            double half = Math.Sqrt(radius * radius - off * off);
            double u0 = Math.Max(0.0, along - half), u1 = Math.Min(len, along + half);
            if (u1 < u0) return false;
            double y0 = ay + (by - ay) * u0 / d, y1 = ay + (by - ay) * u1 / d;
            return Math.Max(y0, y1) > bottom && Math.Min(y0, y1) < top;
        }

        /// <summary>The eye chosen for a landmark shot.</summary>
        public struct Eye
        {
            public bool Found;
            public double X, Z;
            public float Y, Raise, Shift, Lateral;
        }

        /// <summary>
        /// The first eye from which <paramref name="clear"/> (x, z, eye height) holds: at <paramref name="s"/> and the other road
        /// positions (<see cref="Shifts"/> × <see cref="Laterals"/>) at the usual height, then the same raised by <see cref="Raises"/>.
        /// Not found: the road centre at <paramref name="s"/>, usual height.
        /// </summary>
        public static Eye Choose(Func<double, RoadPoint> road, double s, Func<double, double, float, bool> clear)
        {
            foreach (float raise in Raises)
            {
                foreach (float shift in Shifts)
                {
                    RoadPoint r = road(s + shift);
                    double h = r.HeadingDeg * Math.PI / 180.0;
                    foreach (float lateral in Laterals)
                    {
                        double x = r.X + Math.Cos(h) * lateral, z = r.Z - Math.Sin(h) * lateral;
                        float y = r.Height + ReviewTourPlan.LandmarkEyeHeight + raise;
                        if (clear(x, z, y)) return new Eye { Found = true, X = x, Z = z, Y = y, Raise = raise, Shift = shift, Lateral = lateral };
                    }
                }
            }

            RoadPoint at = road(s);
            return new Eye { Found = false, X = at.X, Z = at.Z, Y = at.Height + ReviewTourPlan.LandmarkEyeHeight };
        }

        /// <summary>
        /// Both landmark lines (to the middle and to 90 % of the structure) clear of the ground and the crowns
        /// (<see cref="ReviewTourPlan.Clear"/>) and of the trees near the eye (<see cref="Clear"/>).
        /// </summary>
        public static bool LandmarkClear(Func<double, double, float> height, Func<double, double, float, bool> canopyBlocks, IReadOnlyList<Tree> near,
            double ex, double ez, float ey, double tx, double tz, float floor, float structureHeight, float radius, float aspect = 16f / 9f)
        {
            // The near trees are tested against the central third of the frame the shot will use from this eye.
            ReviewTourPlan.LandmarkFrame frame = ReviewTourPlan.FrameLandmark(ex, ey, ez, tx, floor, tz, structureHeight);
            FrameSlopes(frame.Fov, aspect, out float across, out float up);
            foreach (float share in new[] { 0.5f, 0.9f })
            {
                float ty = floor + share * structureHeight;
                if (!Clear(near, ex, ez, ey, tx, tz, ty, Reach, LandmarkWiden * radius, across, up)) return false;
                if (!ReviewTourPlan.Clear(height, canopyBlocks, ex, ez, ey, tx, tz, ty, radius)) return false;
            }

            return true;
        }
    }

    /// <summary>A small indented JSON writer (invariant culture): field lists are objects, other lists are arrays.</summary>
    public static class ReviewJson
    {
        /// <summary>The JSON text of one value (a list, an object given as its fields, a number...) at the given indent depth.</summary>
        public static string ValueText(object value, int depth = 1)
        {
            var b = new StringBuilder();
            Value(b, value, depth);
            return b.ToString();
        }

        /// <summary>The JSON text of an object given as its fields.</summary>
        public static string Write(IReadOnlyList<KeyValuePair<string, object>> fields)
        {
            var b = new StringBuilder();
            Value(b, fields, 0);
            return b.Append('\n').ToString();
        }

        private static void Value(StringBuilder b, object v, int depth)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            switch (v)
            {
                case null: b.Append("null"); return;
                case bool flag: b.Append(flag ? "true" : "false"); return;
                case int n: b.Append(n.ToString(c)); return;
                case long n: b.Append(n.ToString(c)); return;
                case float x: b.Append(float.IsNaN(x) || float.IsInfinity(x) ? "null" : x.ToString("0.###", c)); return;
                case double x: b.Append(double.IsNaN(x) || double.IsInfinity(x) ? "null" : x.ToString("0.###", c)); return;
                case string s: b.Append('"').Append(Escape(s)).Append('"'); return;
                case IReadOnlyList<KeyValuePair<string, object>> obj:
                {
                    if (obj.Count == 0)
                    {
                        b.Append("{}");
                        return;
                    }

                    b.Append("{\n");
                    for (int i = 0; i < obj.Count; i++)
                    {
                        b.Append(' ', 2 * depth + 2).Append('"').Append(Escape(obj[i].Key)).Append("\": ");
                        Value(b, obj[i].Value, depth + 1);
                        b.Append(i + 1 < obj.Count ? ",\n" : "\n");
                    }

                    b.Append(' ', 2 * depth).Append('}');
                    return;
                }

                case IList list:
                {
                    if (list.Count == 0)
                    {
                        b.Append("[]");
                        return;
                    }

                    bool flat = !(list[0] is IList);
                    b.Append(flat && !(list[0] is IReadOnlyList<KeyValuePair<string, object>>) ? "[" : "[\n");
                    bool inline = flat && !(list[0] is IReadOnlyList<KeyValuePair<string, object>>);
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!inline) b.Append(' ', 2 * depth + 2);
                        Value(b, list[i], depth + 1);
                        if (i + 1 < list.Count) b.Append(inline ? ", " : ",\n");
                    }

                    if (!inline) b.Append('\n').Append(' ', 2 * depth);
                    b.Append(']');
                    return;
                }

                default: b.Append('"').Append(Escape(Convert.ToString(v, c))).Append('"'); return;
            }
        }

        private static string Escape(string s)
        {
            var b = new StringBuilder();
            foreach (char ch in s ?? string.Empty)
            {
                if (ch == '"' || ch == '\\') b.Append('\\').Append(ch);
                else if (ch < 32) b.Append(' ');
                else b.Append(ch);
            }

            return b.ToString();
        }
    }
}
