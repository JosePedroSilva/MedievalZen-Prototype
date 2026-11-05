using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using Unity.Profiling.LowLevel;
using UnityEngine;

namespace MedievalZen.Review
{
    /// <summary>
    /// The stall hunt's per-frame culprit (2026-10-06): named, main-thread-only timing scopes round each manager's per-frame work
    /// and the travel event handlers, plus engine profiler markers (garbage collection, shader programs, loading, Instantiate,
    /// physics baking, audio, the editor's own loop) read only for a slow frame.
    /// <para>
    /// Usage: <c>private static readonly int CostId = Review.FrameCost.Register("Wildlife");</c> once per class, then
    /// <c>using (Review.FrameCost.Measure(CostId)) { ... }</c>. While nobody records (<see cref="Recording"/> false, the normal game)
    /// a scope is one static bool test on entry and one long compare on exit; while recording it is two
    /// <see cref="Stopwatch.GetTimestamp"/> calls and an array add (tens of nanoseconds). Nothing allocates.
    /// </para>
    /// <para>
    /// Scopes are inclusive: a scope inside another counts in both (the outer one names the manager, the inner one the phase).
    /// The frame a delta time stands for is the previous frame (Unity samples the time at the start of a frame), so the readers
    /// (<see cref="TopOfLastFrame"/>, <see cref="EngineOfLastFrame"/>) return the last completed frame, which is what
    /// <c>Time.unscaledDeltaTime</c> and <see cref="ProfilerRecorder.LastValue"/> describe when read in this frame.
    /// </para>
    /// </summary>
    public static class FrameCost
    {
        /// <summary>The most scope names that can be registered (later registrations share the last slot, "other").</summary>
        public const int MaxScopes = 96;

        private static readonly string[] names = new string[MaxScopes];
        private static int registered;
        private static long[] current = new long[MaxScopes];
        private static long[] previous = new long[MaxScopes];
        private static int currentCalls, previousCalls;
        private static int frame = -1;
        private static bool recording;

        // Arrival stall hunt (2026-10-09): a scope that ends for the first time in the session is flagged in the frame it ran in
        // (first-use costs: an asset load, a shader, an Animator's first initialisation), and named first uses
        // (<see cref="FirstUse"/>: the first instantiate of a prefab, the first play of a clip) are listed per frame.
        private static readonly bool[] everRan = new bool[MaxScopes];
        private static bool[] firstCurrent = new bool[MaxScopes];
        private static bool[] firstPrevious = new bool[MaxScopes];
        private static readonly HashSet<string> firstUses = new HashSet<string>(StringComparer.Ordinal);
        private static List<string> firstsCurrent = new List<string>(4);
        private static List<string> firstsPrevious = new List<string>(4);
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>True while a review tool records (the walk benchmark, a tour): scopes measure, engine markers run.</summary>
        public static bool Recording
        {
            get => recording;
            set
            {
                if (recording == value) return;
                recording = value;
                Array.Clear(current, 0, current.Length);
                Array.Clear(previous, 0, previous.Length);
                Array.Clear(firstCurrent, 0, firstCurrent.Length);
                Array.Clear(firstPrevious, 0, firstPrevious.Length);
                firstsCurrent.Clear();
                firstsPrevious.Clear();
                currentCalls = previousCalls = 0;
                frame = -1;
                fontRebuildsCurrent = fontRebuildsPrevious = 0;
                Font.textureRebuilt -= OnFontRebuilt;
                if (value) Font.textureRebuilt += OnFontRebuilt;
                if (value) EngineMarkers.Start();
                else EngineMarkers.Stop();
            }
        }

        /// <summary>Registers a scope name (idempotent by name) and returns its id. Main thread or static constructors.</summary>
        public static int Register(string name)
        {
            lock (names)
            {
                for (int i = 0; i < registered; i++)
                {
                    if (names[i] == name) return i;
                }

                if (registered >= MaxScopes - 1)
                {
                    names[MaxScopes - 1] = "other";
                    return MaxScopes - 1;
                }

                names[registered] = name;
                return registered++;
            }
        }

        /// <summary>Times the work up to the end of the <c>using</c> block under scope <paramref name="id"/> (main thread only).</summary>
        public static Scope Measure(int id)
        {
            if (recording) return new Scope(id, Stopwatch.GetTimestamp());
            everRan[id] = true;   // not recording: the scope still counts as having run (its first run is not flagged later)
            return default;
        }

        /// <summary>
        /// Notes a named first use (e.g. <c>"Instantiate FX_Leaves_Meadows_01"</c>): true the first time <paramref name="what"/> is
        /// seen in the session (Play mode), when it is also listed for the current frame while recording
        /// (<see cref="FirstsOfLastFrame"/>). Call it at creation sites only (it looks up a set and the caller usually builds a string).
        /// </summary>
        public static bool FirstUse(string what)
        {
            if (string.IsNullOrEmpty(what) || !firstUses.Add(what)) return false;
            if (!recording) return true;
            Roll();
            firstsCurrent.Add(what);
            return true;
        }

        /// <summary>The named first uses of the last completed frame (empty while not recording or when none).</summary>
        public static string[] FirstsOfLastFrame()
        {
            if (!recording) return Array.Empty<string>();
            Roll();
            return firstsPrevious.Count == 0 ? Array.Empty<string>() : firstsPrevious.ToArray();
        }

        /// <summary>A running scope; ends at <see cref="Dispose"/>.</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly int id;
            private readonly long start;

            internal Scope(int id, long start)
            {
                this.id = id;
                this.start = start;
            }

            public void Dispose()
            {
                if (start == 0L || !recording) return;
                long end = Stopwatch.GetTimestamp();
                Roll();
                current[id] += end - start;
                currentCalls++;
                if (!everRan[id])
                {
                    everRan[id] = true;
                    firstCurrent[id] = true;
                }
            }
        }

        // A dynamic font's atlas was rebuilt (every glyph re-rasterised, every text and inn board re-laid): native work no scope sees.
        private static int fontRebuildsCurrent, fontRebuildsPrevious;

        private static void OnFontRebuilt(Font font)
        {
            Roll();
            fontRebuildsCurrent++;
        }

        private static void Roll()
        {
            int now = Time.frameCount;
            if (now == frame) return;
            fontRebuildsPrevious = now == frame + 1 ? fontRebuildsCurrent : 0;
            fontRebuildsCurrent = 0;
            long[] t = previous;
            previous = current;
            current = t;
            bool[] f = firstPrevious;
            firstPrevious = firstCurrent;
            firstCurrent = f;
            List<string> named = firstsPrevious;
            firstsPrevious = firstsCurrent;
            firstsCurrent = named;
            previousCalls = currentCalls;
            if (now != frame + 1)
            {
                // No scope ran in the frame before this one: it cost nothing measured.
                Array.Clear(previous, 0, previous.Length);
                Array.Clear(firstPrevious, 0, firstPrevious.Length);
                firstsPrevious.Clear();
                previousCalls = 0;
            }

            Array.Clear(current, 0, current.Length);
            Array.Clear(firstCurrent, 0, firstCurrent.Length);
            firstsCurrent.Clear();
            currentCalls = 0;
            frame = now;
        }

        /// <summary>Scopes that ended in the last completed frame (the per-frame count behind the cost estimate).</summary>
        public static int ScopesLastFrame
        {
            get
            {
                Roll();
                return previousCalls;
            }
        }

        /// <summary>The three largest scopes of the last completed frame, largest first (empty entries when fewer ran).</summary>
        public static void TopOfLastFrame(out CostEntry first, out CostEntry second, out CostEntry third)
        {
            first = second = third = default;
            if (!recording) return;
            Roll();
            for (int i = 0; i < MaxScopes; i++)
            {
                long t = previous[i];
                if (t <= 0L) continue;
                var e = new CostEntry(names[i] ?? "?", (float)(t * TicksToMs), firstPrevious[i]);
                if (e.Ms > first.Ms)
                {
                    third = second;
                    second = first;
                    first = e;
                }
                else if (e.Ms > second.Ms)
                {
                    third = second;
                    second = e;
                }
                else if (e.Ms > third.Ms)
                {
                    third = e;
                }
            }
        }

        /// <summary>
        /// The engine markers of the last completed frame at or above <paramref name="minMs"/>, largest first (allocates: call it
        /// for a slow frame only). Empty while not recording or when none reported.
        /// </summary>
        public static CostEntry[] EngineOfLastFrame(float minMs = 0.5f)
        {
            if (!recording) return Array.Empty<CostEntry>();
            Roll();
            CostEntry[] found = EngineMarkers.Read(minMs);
            if (fontRebuildsPrevious == 0) return found;
            // An event, not a time: listed after the markers with 0 ms.
            var withEvent = new CostEntry[found.Length + 1];
            Array.Copy(found, withEvent, found.Length);
            withEvent[found.Length] = new CostEntry($"event: Font.textureRebuilt x{fontRebuildsPrevious}", 0f);
            return withEvent;
        }

        /// <summary>Which engine markers were found (logged once when recording starts).</summary>
        public static string MarkerReport => EngineMarkers.Report;

        /// <summary>
        /// Engine markers that can stall a frame with no script on the stack. Names tried exactly, then every available timing
        /// marker whose name contains one of <see cref="Keywords"/> (so a name this list does not know is still recorded, by its own
        /// name). A missing name is reported, never fatal.
        /// </summary>
        private static class EngineMarkers
        {
            private static readonly string[] Exact =
            {
                "GC.Collect", "GC.CollectIncremental", "GarbageCollectAssetsProfile", "UnloadUnusedAssets",
                "Shader.CreateGPUProgram", "CreateGPUProgram", "Shader.Parse", "Shader.ParseMainThread", "Shader.ParseThreaded",
                "Shader.EditorLoadVariant", "Shader.CompileGPUProgram", "ShaderCompiler.CompileShaderVariant",
                "Resources.Load", "Loading.ReadObject", "Loading.AwakeFromLoad", "Loading.LoadFileHeaders", "Loading.UpdatePreloading",
                "AsyncUploadManager.AsyncResourceUpload", "AsyncUploadManager.ScheduleAsyncRead", "Gfx.UploadTexture",
                "Instantiate", "Instantiate.Copy", "Instantiate.Produce", "Instantiate.Awake",
                "Animator.Initialize", "Animator.Rebind", "AnimatorControllerPlayable.Initialize",
                "AudioClip.LoadAudioData", "AudioClip.Load", "PostLateUpdate.UpdateAudio",
                "Mesh.Bake PhysX CollisionData", "Mesh.Bake Scaled Mesh PhysX CollisionData", "Physics.BakeCollisionMeshes",
                "EditorLoop", "Font.CacheFontForText", "TextRendering.Cleanup", "Canvas.BuildBatch",
            };

            private static readonly string[] Keywords =
            {
                "GC.Collect", "Shader", "GPUProgram", "PipelineState", "PSO", "Resources.Load", "Loading.", "Instantiate",
                "LoadAudio", "AudioClip", "Bake", "EditorLoop", "AssetDatabase", "Animator.Initialize", "Rebind", "Font",
            };

            private const int MaxRecorders = 64;
            private static ProfilerRecorder[] recorders;
            private static string[] recorderNames;
            private static int count;
            private static readonly List<CostEntry> scratch = new List<CostEntry>(16);

            public static string Report { get; private set; } = "Frame cost markers: not started.";

            public static void Start()
            {
                if (recorders != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (recorders[i].Valid) recorders[i].Start();
                    }

                    return;
                }

                recorders = new ProfilerRecorder[MaxRecorders];
                recorderNames = new string[MaxRecorders];
                var byName = new Dictionary<string, ProfilerRecorderDescription>();
                var available = new List<ProfilerRecorderHandle>();
                try
                {
                    ProfilerRecorderHandle.GetAvailable(available);
                    foreach (ProfilerRecorderHandle h in available)
                    {
                        ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                        if (string.IsNullOrEmpty(d.Name) || byName.ContainsKey(d.Name)) continue;
                        byName[d.Name] = d;
                    }
                }
                catch (Exception)
                {
                    // No list: the exact names are tried blind.
                }

                var missing = new List<string>();
                foreach (string name in Exact.Concat(ReviewWalkBench.PhaseMarkers))
                {
                    if (byName.Count > 0 && !byName.ContainsKey(name))
                    {
                        missing.Add(name);
                        continue;
                    }

                    if (!TryAdd(name, byName)) missing.Add(name);
                }

                var extra = new List<string>();
                foreach (KeyValuePair<string, ProfilerRecorderDescription> pair in byName)
                {
                    if (count >= MaxRecorders) break;
                    if (pair.Value.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || Array.IndexOf(recorderNames, pair.Key, 0, count) >= 0) continue;
                    foreach (string k in Keywords)
                    {
                        if (pair.Key.IndexOf(k, StringComparison.Ordinal) < 0) continue;
                        if (TryAdd(pair.Key, byName)) extra.Add(pair.Key);
                        break;
                    }
                }

                var b = new StringBuilder("Frame cost markers: ");
                b.Append(count).Append(" recorded of ").Append(byName.Count).Append(" available. Exact: ");
                for (int i = 0; i < count; i++)
                {
                    if (extra.Contains(recorderNames[i])) continue;
                    b.Append(recorderNames[i]).Append("; ");
                }

                b.Append("by keyword: ").Append(string.Join("; ", extra)).Append(". Not found: ").Append(missing.Count > 0 ? string.Join("; ", missing) : "none").Append('.');
                Report = b.ToString();
                UnityEngine.Debug.Log(Report);
            }

            private static bool TryAdd(string name, Dictionary<string, ProfilerRecorderDescription> byName)
            {
                if (count >= MaxRecorders) return false;
                try
                {
                    ProfilerCategory category = byName.TryGetValue(name, out ProfilerRecorderDescription d) ? d.Category : ProfilerCategory.Internal;
                    ProfilerRecorder r = ProfilerRecorder.StartNew(category, name, 1, ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                    if (!r.Valid) return false;
                    recorders[count] = r;
                    recorderNames[count] = name;
                    count++;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            public static void Stop()
            {
                if (recorders == null) return;
                for (int i = 0; i < count; i++)
                {
                    if (recorders[i].Valid) recorders[i].Stop();
                }
            }

            public static CostEntry[] Read(float minMs)
            {
                if (recorders == null) return Array.Empty<CostEntry>();
                scratch.Clear();
                for (int i = 0; i < count; i++)
                {
                    if (!recorders[i].Valid || recorders[i].Count == 0) continue;
                    float ms = recorders[i].LastValue * 1e-6f;
                    if (ms >= minMs) scratch.Add(new CostEntry(recorderNames[i], ms));
                }

                scratch.Sort((a, b) => b.Ms.CompareTo(a.Ms));
                return scratch.ToArray();
            }

            public static void DisposeAll()
            {
                if (recorders != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (recorders[i].Valid) recorders[i].Dispose();
                    }
                }

                recorders = null;
                recorderNames = null;
                count = 0;
                Report = "Frame cost markers: not started.";
            }
        }

        // Play mode without a domain reload keeps statics: start clean (scope names stay registered, they are per class).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            EngineMarkers.DisposeAll();
            Font.textureRebuilt -= OnFontRebuilt;
            fontRebuildsCurrent = fontRebuildsPrevious = 0;
            recording = false;
            frame = -1;
            Array.Clear(current, 0, current.Length);
            Array.Clear(previous, 0, previous.Length);
            Array.Clear(everRan, 0, everRan.Length);
            Array.Clear(firstCurrent, 0, firstCurrent.Length);
            Array.Clear(firstPrevious, 0, firstPrevious.Length);
            firstUses.Clear();
            firstsCurrent.Clear();
            firstsPrevious.Clear();
            currentCalls = previousCalls = 0;
        }
    }
}
