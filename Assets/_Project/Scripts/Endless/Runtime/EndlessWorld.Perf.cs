using System.Collections.Generic;
using System.Text;
using MedievalZen.Environment;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Performance numbers (performance pass, 2026-10-05) for the F3 overlay, the "running OK" log line and the review tour's sidecars:
    /// CPU main-thread, render-thread and GPU times (<see cref="EndlessPerf"/>), the instanced renderer's own pass, instances and
    /// triangles per band and shadow ring, plants and ferns, registered instances, terrains and the sun's elevation.
    /// </summary>
    public sealed partial class EndlessWorld
    {
        private readonly EndlessPerf perf = new EndlessPerf();

        /// <summary>Frame timings of this run (sampled once per frame after the trees are submitted).</summary>
        public EndlessPerf Perf => perf;

        private static float SunElevation() => DayCycle.Current != null ? DayCycle.Current.SunElevation : float.NaN;

        /// <summary>The scene's active Unity terrains (the near tiles, plus any other terrain).</summary>
        private static int TerrainCount() => Terrain.activeTerrains != null ? Terrain.activeTerrains.Length : 0;

        /// <summary>One paragraph: the window's timings and this frame's renderer load (log line, overlay).</summary>
        public string PerfSummary(FrameStatsWindow window, int frames)
        {
            var b = new StringBuilder();
            b.Append($"over {(frames > 0 ? Mathf.Min(frames, window.Count) : window.Count)} frames: frame {Ms(window.Mean(FrameStatsWindow.Frame, frames))} (max {Ms(window.Max(FrameStatsWindow.Frame, frames))}), ");
            b.Append($"CPU main {Ms(window.Mean(FrameStatsWindow.CpuMain, frames))}, render {Ms(window.Mean(FrameStatsWindow.CpuRender, frames))}, GPU {Ms(window.Mean(FrameStatsWindow.Gpu, frames))} [{perf.Source}]; ");
            b.Append($"tree pass {window.Mean(FrameStatsWindow.Walk, frames):0.000} ms + submit {window.Mean(FrameStatsWindow.Submit, frames):0.000} ms, pass reused on {100f * window.ReusedShare(frames):0}% of frames. ");
            if (trees != null)
            {
                TreeWalkStats w = trees.WalkStats;
                b.Append($"Instanced: {trees.RegisteredTrees} registered, {trees.InstancesThisFrame} drawn in {trees.DrawCallsThisFrame} draws; ");
                b.Append($"LOD0 {trees.InstancesIn(TreeDrawBand.Detail)} / {K(trees.TrianglesIn(TreeDrawBand.Detail))}, mid {trees.InstancesIn(TreeDrawBand.Mid)} / {K(trees.TrianglesIn(TreeDrawBand.Mid))}, ");
                b.Append($"low {trees.InstancesIn(TreeDrawBand.Low)} / {K(trees.TrianglesIn(TreeDrawBand.Low))}, impostor {trees.InstancesIn(TreeDrawBand.Impostor)} / {K(trees.TrianglesIn(TreeDrawBand.Impostor))} triangles; plants {trees.PlantsDrawn} (ferns {trees.FernsDrawn}) / {K(trees.PlantTriangles)}; ");
                b.Append($"shadows near {trees.ShadowInstancesIn(0)} / {K(trees.ShadowTrianglesIn(0))}, proxies {trees.ShadowInstancesIn(1)} / {K(trees.ShadowTrianglesIn(1))}; ");
                b.Append($"cells {w.Cells} (in view {w.CellsInView}, shadow only {w.CellsShadowOnly}, shadow culled {w.CellsShadowCulled}). ");
            }

            b.Append($"Terrains {TerrainCount()} (near tiles {CountActive(TileLod.Near)}), sun {SunElevation():0.0}° high. ");
            b.Append($"Main-thread markers: {EndlessPerf.MarkerLine(window == perf.Stop ? perf.MarkersStop : perf.MarkersRecent, frames)}. ");
            b.Append($"Apply worst 10 s {WorstApplyMsLast10s:0.00} ms, worst slice {WorstSliceMsLast10s:0.00} ms ({WorstSliceNameLast10s}).");
            return b.ToString();
        }

        /// <summary>The review sidecar's performance fields: the window's means, then this frame's renderer load.</summary>
        public void PerfFields(List<KeyValuePair<string, object>> fields, FrameStatsWindow window)
        {
            void Add(string key, object value) => fields.Add(new KeyValuePair<string, object>(key, value));
            Add("perfFrames", window.Count);
            Add("frameMsStop", window.Mean(FrameStatsWindow.Frame));
            Add("frameMsStopMax", window.Max(FrameStatsWindow.Frame));
            Add("cpuMainMs", window.Mean(FrameStatsWindow.CpuMain));
            Add("cpuRenderMs", window.Mean(FrameStatsWindow.CpuRender));
            Add("cpuPresentWaitMs", window.Mean(FrameStatsWindow.PresentWait));
            Add("gpuMs", window.Mean(FrameStatsWindow.Gpu));
            Add("frameTimingSource", perf.Source);
            Add("treeWalkMs", window.Mean(FrameStatsWindow.Walk));
            Add("treeSubmitMs", window.Mean(FrameStatsWindow.Submit));
            Add("treeWalkReusedShare", window.ReusedShare());
            Add("sunElevation", SunElevation());
            Add("terrains", TerrainCount());
            Add("nearTiles", CountActive(TileLod.Near));
            Add("farTiles", builder != null ? builder.FarShown : 0);
            // Second performance pass: where the main thread goes (profiler markers; NaN: the marker reported nothing) and the loading.
            MarkerWindow mw = window == perf.Stop ? perf.MarkersStop : perf.MarkersRecent;
            for (int i = 0; i < EndlessPerf.MarkerSlots.Length; i++) Add("mainMs_" + EndlessPerf.MarkerSlots[i].label, mw.Mean(i));
            Add("worstApplyMs10s", WorstApplyMsLast10s);
            Add("worstSliceMs10s", WorstSliceMsLast10s);
            Add("worstSlice10s", WorstSliceNameLast10s);
            // Third performance pass: heavy props warmed behind a hold, made ahead of need, idle in the pool now.
            if (builder != null) Add("heavyProps", new[] { (double)builder.HeavyPrefabsWarmed, builder.HeavyPrefabCount, builder.HeavyStockMade, builder.HeavyStockIdle });
            if (trees == null) return;
            TreeWalkStats w = trees.WalkStats;
            Add("treeRegistered", trees.RegisteredTrees);
            Add("treeInstances", trees.InstancesThisFrame);
            Add("treeDraws", trees.DrawCallsThisFrame);
            Add("treeLod0", new[] { (double)trees.InstancesIn(TreeDrawBand.Detail), trees.TrianglesIn(TreeDrawBand.Detail) });
            Add("treeMid", new[] { (double)trees.InstancesIn(TreeDrawBand.Mid), trees.TrianglesIn(TreeDrawBand.Mid) });
            Add("treeLow", new[] { (double)trees.InstancesIn(TreeDrawBand.Low), trees.TrianglesIn(TreeDrawBand.Low) });
            Add("treeImpostor", new[] { (double)trees.InstancesIn(TreeDrawBand.Impostor), trees.TrianglesIn(TreeDrawBand.Impostor) });
            Add("treeImpostorDistance", float.IsInfinity(trees.Distances.Impostor) ? 0f : trees.Distances.Impostor);
            Add("treeVisibleTriangles", trees.TrianglesThisFrame);
            Add("shadowNear", new[] { (double)trees.ShadowInstancesIn(0), trees.ShadowTrianglesIn(0) });
            Add("shadowProxies", new[] { (double)trees.ShadowInstancesIn(1), trees.ShadowTrianglesIn(1) });
            Add("shadowTriangles", trees.ShadowTrianglesThisFrame);
            Add("plantsDrawn", trees.PlantsDrawn);
            Add("fernsDrawn", trees.FernsDrawn);
            Add("plantTriangles", trees.PlantTriangles);
            Add("cells", new[] { (double)w.Cells, w.CellsInView, w.CellsShadowOnly, w.CellsShadowCulled });
        }

        private static string Ms(float v) => float.IsNaN(v) ? "n/a" : v.ToString("0.0") + " ms";

        private static string K(long triangles) => triangles >= 1000000 ? (triangles / 1e6).ToString("0.00") + " M" : (triangles / 1000) + "k";
    }
}
