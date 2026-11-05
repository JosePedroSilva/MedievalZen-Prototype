using System.Collections.Generic;
using System;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>One frame's timings (milliseconds; NaN where the source reports nothing).</summary>
    public struct FrameSample
    {
        /// <summary>Frame time from <c>Time.unscaledDeltaTime</c>.</summary>
        public float FrameMs;
        public float CpuMainMs;
        public float CpuRenderMs;

        /// <summary>
        /// Milliseconds of <see cref="CpuMainMs"/> the main thread spent waiting for the frame to be presented (NaN when not
        /// reported). When this is most of the main-thread time, the frame is bound by the GPU, not by scripts: the tour of
        /// 2026-10-06 showed 14.5 ms of main thread in forest with only 1.2 ms in script, culling and animation markers.
        /// </summary>
        public float PresentWaitMs;
        public float GpuMs;

        /// <summary>The instanced renderer's pass over the cells (0 on a reused frame) and its submission.</summary>
        public float TreeWalkMs;
        public float TreeSubmitMs;
        public bool TreeWalkReused;
    }

    /// <summary>
    /// Averages of frame samples (performance pass, 2026-10-05; pure): a resettable window (a review stop: <see cref="Reset"/> once the
    /// view has settled, read after a few dozen frames) and the same maths for a rolling window of the last frames (overlay, log line).
    /// NaN samples are left out of their field's mean (a source that reports nothing gives NaN, not 0).
    /// </summary>
    public sealed class FrameStatsWindow
    {
        private readonly FrameSample[] ring;
        private int next, count;

        /// <summary>A window keeping the last <paramref name="capacity"/> samples.</summary>
        public FrameStatsWindow(int capacity = 600)
        {
            ring = new FrameSample[Math.Max(1, capacity)];
        }

        /// <summary>Samples held (at most the capacity).</summary>
        public int Count => count;

        /// <summary>Forgets every sample.</summary>
        public void Reset()
        {
            next = 0;
            count = 0;
        }

        /// <summary>Adds a sample (the oldest drops out when full).</summary>
        public void Add(in FrameSample s)
        {
            ring[next] = s;
            next = (next + 1) % ring.Length;
            if (count < ring.Length) count++;
        }

        /// <summary>Mean of a field over the last <paramref name="last"/> samples (all when 0), NaN samples left out; NaN when none.</summary>
        public float Mean(Func<FrameSample, float> field, int last = 0)
        {
            int n = last > 0 ? Math.Min(last, count) : count;
            double sum = 0;
            int used = 0;
            for (int i = 0; i < n; i++)
            {
                float v = field(ring[(next - 1 - i + ring.Length * 2) % ring.Length]);
                if (float.IsNaN(v)) continue;
                sum += v;
                used++;
            }

            return used > 0 ? (float)(sum / used) : float.NaN;
        }

        /// <summary>Largest value of a field over the last <paramref name="last"/> samples (all when 0); NaN when none.</summary>
        public float Max(Func<FrameSample, float> field, int last = 0)
        {
            int n = last > 0 ? Math.Min(last, count) : count;
            float max = float.NaN;
            for (int i = 0; i < n; i++)
            {
                float v = field(ring[(next - 1 - i + ring.Length * 2) % ring.Length]);
                if (!float.IsNaN(v) && (float.IsNaN(max) || v > max)) max = v;
            }

            return max;
        }

        /// <summary>Share (0..1) of the last <paramref name="last"/> samples (all when 0) whose tree pass was reused.</summary>
        public float ReusedShare(int last = 0)
        {
            int n = last > 0 ? Math.Min(last, count) : count, reused = 0;
            for (int i = 0; i < n; i++)
            {
                if (ring[(next - 1 - i + ring.Length * 2) % ring.Length].TreeWalkReused) reused++;
            }

            return n > 0 ? (float)reused / n : float.NaN;
        }

        public static readonly Func<FrameSample, float> Frame = s => s.FrameMs;
        public static readonly Func<FrameSample, float> CpuMain = s => s.CpuMainMs;
        public static readonly Func<FrameSample, float> CpuRender = s => s.CpuRenderMs;
        public static readonly Func<FrameSample, float> PresentWait = s => s.PresentWaitMs;
        public static readonly Func<FrameSample, float> Gpu = s => s.GpuMs;
        public static readonly Func<FrameSample, float> Walk = s => s.TreeWalkMs;
        public static readonly Func<FrameSample, float> Submit = s => s.TreeSubmitMs;
    }

    /// <summary>
    /// Per-frame timings for the endless road (performance pass, 2026-10-05): the CPU main-thread, render-thread and GPU times of
    /// <see cref="FrameTimingManager"/> (captured every frame; its numbers are a few frames old), falling back to the profiler's
    /// "Main Thread" counter for the main thread when the manager reports nothing (then the render thread and the GPU are NaN, written
    /// as null, and <see cref="Source"/> says so); the instanced renderer's own pass and submission; a rolling window (the last 600
    /// frames) and a stop window the review tour resets.
    /// </summary>
    public sealed class EndlessPerf : IDisposable
    {
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private ProfilerRecorder mainThread;
        private bool recorderStarted;

        /// <summary>The last 600 frames.</summary>
        public FrameStatsWindow Recent { get; } = new FrameStatsWindow(600);

        /// <summary>Frames since <see cref="BeginStop"/> (the review tour's measurement at a shot).</summary>
        public FrameStatsWindow Stop { get; } = new FrameStatsWindow(600);

        /// <summary>Where the CPU and GPU times come from: "FrameTimingManager", "FrameTimingManager (no GPU time)", "profiler (main thread only)" or "none".</summary>
        public string Source { get; private set; } = "none";

        /// <summary>The last sample.</summary>
        public FrameSample Last { get; private set; }

        /// <summary>Starts a stop window (the review tour, once the shot has settled).</summary>
        public void BeginStop()
        {
            Stop.Reset();
            MarkersStop.Reset();
        }

        /// <summary>Records this frame with the instanced renderer's pass and submission times (call once per frame, after the trees were submitted).</summary>
        public void Sample(float treeWalkMs, float treeSubmitMs, bool treeWalkReused)
        {
            var s = new FrameSample
            {
                FrameMs = Time.unscaledDeltaTime * 1000f, CpuMainMs = float.NaN, CpuRenderMs = float.NaN, PresentWaitMs = float.NaN, GpuMs = float.NaN,
                TreeWalkMs = treeWalkMs, TreeSubmitMs = treeSubmitMs, TreeWalkReused = treeWalkReused,
            };

            uint got = 0;
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                got = FrameTimingManager.GetLatestTimings(1, timings);
            }
            catch (Exception)
            {
                got = 0;
            }

            if (got > 0 && timings[0].cpuMainThreadFrameTime > 0.0)
            {
                s.CpuMainMs = (float)timings[0].cpuMainThreadFrameTime;
                s.CpuRenderMs = timings[0].cpuRenderThreadFrameTime > 0.0 ? (float)timings[0].cpuRenderThreadFrameTime : float.NaN;
                s.GpuMs = timings[0].gpuFrameTime > 0.0 ? (float)timings[0].gpuFrameTime : float.NaN;
                s.PresentWaitMs = (float)timings[0].cpuMainThreadPresentWaitTime;
                Source = float.IsNaN(s.GpuMs) ? "FrameTimingManager (no GPU time)" : "FrameTimingManager";
            }
            else
            {
                if (!recorderStarted)
                {
                    recorderStarted = true;
                    try
                    {
                        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
                    }
                    catch (Exception)
                    {
                        // No recorder: the main thread stays NaN.
                    }
                }

                if (mainThread.Valid && mainThread.Count > 0)
                {
                    s.CpuMainMs = mainThread.LastValue * 1e-6f;
                    Source = "profiler (main thread only)";
                }
                else
                {
                    Source = "none";
                }
            }

            Last = s;
            Recent.Add(s);
            Stop.Add(s);
            SampleMarkers();
        }

        /// <summary>One line: frame, CPU main/render, GPU (means over the last <paramref name="frames"/>), the tree pass.</summary>
        public string Line(int frames = 120)
        {
            var b = new StringBuilder();
            b.Append($"frame {Recent.Mean(FrameStatsWindow.Frame, frames):0.0} ms (max {Recent.Max(FrameStatsWindow.Frame, frames):0.0}), cpu main {Ms(Recent.Mean(FrameStatsWindow.CpuMain, frames))}, ");
            b.Append($"render {Ms(Recent.Mean(FrameStatsWindow.CpuRender, frames))}, gpu {Ms(Recent.Mean(FrameStatsWindow.Gpu, frames))} [{Source}]; ");
            b.Append($"tree pass {Recent.Mean(FrameStatsWindow.Walk, frames):0.00} ms + submit {Recent.Mean(FrameStatsWindow.Submit, frames):0.00} ms, reused {100f * Recent.ReusedShare(frames):0}% of frames");
            return b.ToString();
        }

        private static string Ms(float v) => float.IsNaN(v) ? "n/a" : v.ToString("0.0") + " ms";

        // ------------------------------------------------------------------ main-thread markers (second performance pass, 2026-10-06)

        /// <summary>
        /// Main-thread profiler markers that can explain <c>cpuMainMs</c>: a label and the marker names tried in order. The names are
        /// not guessed blind: on the first sample every available marker is listed (<see cref="ProfilerRecorderHandle.GetAvailable"/>),
        /// the first candidate that exists is recorded, and the log says which slots found none (<see cref="MarkerReport"/>).
        /// </summary>
        public static readonly (string label, string[] names)[] MarkerSlots =
        {
            ("update", new[] { "Update.ScriptRunBehaviourUpdate", "BehaviourUpdate" }),
            ("lateUpdate", new[] { "PreLateUpdate.ScriptRunBehaviourLateUpdate", "LateBehaviourUpdate" }),
            ("fixed", new[] { "FixedUpdate.PhysicsFixedUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate" }),
            ("animators", new[] { "PreLateUpdate.DirectorUpdateAnimationBegin", "Animators.Update", "Director.ProcessFrame" }),
            ("particles", new[] { "PreLateUpdate.ParticleSystemBeginUpdateAll", "ParticleSystem.Update", "ParticleSystem.ScheduleJobs" }),
            ("ui", new[] { "PostLateUpdate.PlayerUpdateCanvases", "Canvas.SendWillRenderCanvases", "UGUI.Rendering.UpdateBatches" }),
            ("render", new[] { "Inl_UniversalRenderPipeline.RenderSingleCameraInternal", "UniversalRenderPipeline.RenderSingleCameraInternal", "Camera.Render" }),
            ("cull", new[] { "CullScriptable", "Culling", "SceneCulling" }),
            ("shadowCull", new[] { "CullShadowCasters", "Shadows.CullShadowCasters", "ShadowCulling" }),
            ("terrain", new[] { "Terrain.CullAllTerrains", "TerrainRenderer.RenderStep", "Terrain.Render" }),
            ("details", new[] { "Terrain.Details.Render", "TerrainDetails.Render", "Details.Render", "DetailRenderer.Render" }),
            ("gfxWait", new[] { "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.PresentFrame" }),
        };

        private ProfilerRecorder[] markers;
        private string[] markerNames;

        /// <summary>Which marker each slot records (null: none found) and the available names that look relevant (logged once).</summary>
        public string MarkerReport { get; private set; } = string.Empty;

        /// <summary>Per-slot milliseconds, the window of the last frames and of the current stop.</summary>
        public MarkerWindow MarkersRecent { get; } = new MarkerWindow(MarkerSlots.Length, 600);
        public MarkerWindow MarkersStop { get; } = new MarkerWindow(MarkerSlots.Length, 600);

        /// <summary>Records this frame's marker times (call once per frame, from <see cref="Sample"/>'s caller).</summary>
        public void SampleMarkers()
        {
            if (markers == null) StartMarkers();
            var ms = MarkersRecent.Scratch;
            for (int i = 0; i < markers.Length; i++) ms[i] = markers[i].Valid && markers[i].Count > 0 ? markers[i].LastValue * 1e-6f : float.NaN;
            MarkersRecent.Add(ms);
            MarkersStop.Add(ms);
        }

        private void StartMarkers()
        {
            markers = new ProfilerRecorder[MarkerSlots.Length];
            markerNames = new string[MarkerSlots.Length];
            var available = new List<ProfilerRecorderHandle>();
            var byName = new Dictionary<string, ProfilerRecorderDescription>();
            var relevant = new List<string>();
            try
            {
                ProfilerRecorderHandle.GetAvailable(available);
                foreach (ProfilerRecorderHandle h in available)
                {
                    ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                    if (string.IsNullOrEmpty(d.Name) || byName.ContainsKey(d.Name)) continue;
                    byName[d.Name] = d;
                    if (relevant.Count < 120 && (d.Name.Contains("Cull") || d.Name.Contains("Terrain") || d.Name.Contains("Detail") || d.Name.Contains("Particle") ||
                                                 d.Name.Contains("Animat") || d.Name.Contains("Canvas") || d.Name.Contains("ScriptRun"))) relevant.Add(d.Name);
                }
            }
            catch (Exception)
            {
                // No list: every slot is tried by its first name.
            }

            var b = new StringBuilder("Endless perf markers:");
            for (int i = 0; i < MarkerSlots.Length; i++)
            {
                foreach (string name in MarkerSlots[i].names)
                {
                    if (byName.Count > 0 && !byName.ContainsKey(name)) continue;
                    try
                    {
                        ProfilerCategory category = byName.TryGetValue(name, out ProfilerRecorderDescription d) ? d.Category : ProfilerCategory.Internal;
                        markers[i] = ProfilerRecorder.StartNew(category, name, 1, ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                        markerNames[i] = name;
                        break;
                    }
                    catch (Exception)
                    {
                        // try the next name
                    }
                }

                b.Append($" {MarkerSlots[i].label}={markerNames[i] ?? "NONE"};");
            }

            b.Append($" ({byName.Count} markers available; relevant names: {string.Join(", ", relevant)})");
            MarkerReport = b.ToString();
        }

        /// <summary>The marker times as "label ms" pairs (means over <paramref name="window"/>), slots that reported nothing as "n/a".</summary>
        public static string MarkerLine(MarkerWindow window, int frames = 0)
        {
            var b = new StringBuilder();
            for (int i = 0; i < MarkerSlots.Length; i++) b.Append($"{MarkerSlots[i].label} {Ms(window.Mean(i, frames))}{(i < MarkerSlots.Length - 1 ? ", " : string.Empty)}");
            return b.ToString();
        }

        public void Dispose()
        {
            if (mainThread.Valid) mainThread.Dispose();
            if (markers != null)
            {
                foreach (ProfilerRecorder r in markers)
                {
                    if (r.Valid) r.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Per-slot frame values (milliseconds) over the last <c>capacity</c> frames, ignoring NaN (a slot that reported nothing). Pure;
    /// the main-thread marker window of <see cref="EndlessPerf"/>.
    /// </summary>
    public sealed class MarkerWindow
    {
        private readonly float[,] ring;
        private readonly int capacity;
        private int head, count;

        public MarkerWindow(int slots, int capacity)
        {
            this.capacity = Math.Max(1, capacity);
            ring = new float[Math.Max(1, slots), this.capacity];
            Scratch = new float[Math.Max(1, slots)];
        }

        /// <summary>A buffer of one value per slot for the caller to fill and pass to <see cref="Add"/> (no allocation per frame).</summary>
        public float[] Scratch { get; }

        public int Count => count;

        public void Reset()
        {
            head = 0;
            count = 0;
        }

        public void Add(float[] values)
        {
            for (int s = 0; s < ring.GetLength(0); s++) ring[s, head] = s < values.Length ? values[s] : float.NaN;
            head = (head + 1) % capacity;
            if (count < capacity) count++;
        }

        /// <summary>Mean of slot <paramref name="slot"/> over the last <paramref name="last"/> frames (0: all), NaN values skipped; NaN when none.</summary>
        public float Mean(int slot, int last = 0)
        {
            int n = last <= 0 || last > count ? count : last;
            double sum = 0;
            int used = 0;
            for (int k = 1; k <= n; k++)
            {
                float v = ring[slot, (head - k + capacity) % capacity];
                if (float.IsNaN(v)) continue;
                sum += v;
                used++;
            }

            return used > 0 ? (float)(sum / used) : float.NaN;
        }
    }
}
