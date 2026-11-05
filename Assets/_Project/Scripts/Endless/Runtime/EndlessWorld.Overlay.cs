using System.Text;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>The streaming overlay (F3): small, bottom-left, off by default.</summary>
    public sealed partial class EndlessWorld
    {
        private readonly StringBuilder overlayText = new StringBuilder(1024);
        private GUIStyle overlayStyle;
        private float nextOverlayText;

        /// <summary>Whether the overlay is shown (F3 toggles it; the review capture hides it while it takes shots).</summary>
        public bool ShowOverlay { get => showOverlay; set => showOverlay = value; }

        private void OnGUI()
        {
            if (!showOverlay || generator == null) return;
            if (overlayStyle == null)
            {
                overlayStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = Mathf.Max(11, Screen.height / 80),
                    richText = false,
                    wordWrap = false,
                    padding = new RectOffset(8, 8, 6, 6),
                };
                overlayStyle.normal.textColor = new Color(1f, 0.97f, 0.88f);
            }

            if (Time.unscaledTime >= nextOverlayText) BuildOverlayText();
            var content = new GUIContent(overlayText.ToString());
            Vector2 size = overlayStyle.CalcSize(content);
            GUI.Box(new Rect(10f, Screen.height - size.y - 10f, size.x, size.y), content, overlayStyle);
        }

        private void BuildOverlayText()
        {
            nextOverlayText = Time.unscaledTime + 0.25f;
            int near = 0, far = 0, queued = 0, waiting = 0;
            foreach (CoordState state in states.Values)
            {
                if (state.Active != null)
                {
                    if (state.Active.Lod == TileLod.Near) near++;
                    else far++;
                }

                if (state.Ready != null) queued++;
                if (state.Target != null && state.Job == null && state.Ready == null && state.Building == null &&
                    (state.Active == null || state.Active.Lod != state.Target)) waiting++;
            }

            (double x, double z) = TravellerAbsolute();
            Vector3 scene = traveller != null ? traveller.position : Vector3.zero;
            overlayText.Clear();
            overlayText.Append("Endless road  (F3 hide, F4 force origin shift, F6 animal event, F7 music now / end, F8 next track)\n");
            overlayText.Append($"s {travellerS:0.0} m   abs ({x:0.0}, {z:0.0})   scene ({scene.x:0.0}, {scene.y:0.0}, {scene.z:0.0})\n");
            overlayText.Append($"region {RegionName}   seed {Seed}\n");
            overlayText.Append($"tiles  near {near}  far {far}  building {building.Count}  generated {queued}  in flight {running.Count}  waiting {waiting}  releasing {releasing.Count}\n");
            overlayText.Append($"generation  last {lastGenerationMs:0.0} ms  worst {worstGenerationMs:0.0} ms   ({Mathf.Clamp(settings.workerCount, 1, 8)} workers)\n");
            overlayText.Append($"apply  this frame {lastApplyMs:0.00} ms  worst 10 s {WorstApplyMsLast10s:0.00} ms  (budget {settings.applyBudgetMs:0.0})\n");
            overlayText.Append($"worst slice 10 s {WorstSliceMsLast10s:0.00} ms ({WorstSliceNameLast10s})\n");
            overlayText.Append($"main thread  {EndlessPerf.MarkerLine(perf.MarkersRecent, 60)}\n");
            overlayText.Append($"far meshes {builder.FarShown}  ({builder.FarTrianglesShown / 1000}k triangles)   made {builder.FarMeshesCreated}, pooled {builder.PooledFar}   meshes {builder.FarMeshBytes / 1048576f:0.0} MB\n");
            if (trees != null)
            {
                overlayText.Append($"trees  {trees.RegisteredTrees} registered  {trees.InstancesThisFrame} drawn in {trees.DrawCallsThisFrame} draws this frame  ({trees.PrototypeCount} prototypes)  trunk colliders {trees.ActiveColliders}  boundary boxes {trees.ActiveBoxes} (+{trees.ActiveMeshColliders} mesh) of {trees.CandidateBoxes} pieces\n");
                TreeLodDistances d = trees.Distances;
                overlayText.Append($"tree LOD  full {d.Full:0} m  mid {d.Mid:0} m  (settings {trees.FullDetailDistance:0} / {trees.MidDetailDistance:0} x {trees.DetailScale:0.00})  shadows {trees.ShadowDistanceInEffect:0} m   " +
                                   $"LOD0 {trees.InstancesIn(TreeDrawBand.Detail)}  mid {trees.InstancesIn(TreeDrawBand.Mid)}  low {trees.InstancesIn(TreeDrawBand.Low)}  impostor {trees.InstancesIn(TreeDrawBand.Impostor)} (from {(float.IsInfinity(d.Impostor) ? 0f : d.Impostor):0} m)  shadow {trees.ShadowInstancesThisFrame}   " +
                                   $"{trees.TrianglesThisFrame / 1000}k + {trees.ShadowTrianglesThisFrame / 1000}k shadow triangles\n");
                TreeWalkStats w = trees.WalkStats;
                overlayText.Append($"triangles  LOD0 {trees.TrianglesIn(TreeDrawBand.Detail) / 1000}k  mid {trees.TrianglesIn(TreeDrawBand.Mid) / 1000}k  low {trees.TrianglesIn(TreeDrawBand.Low) / 1000}k  impostor {trees.TrianglesIn(TreeDrawBand.Impostor) / 1000}k   " +
                                   $"plants {trees.PlantsDrawn} (ferns {trees.FernsDrawn}) {trees.PlantTriangles / 1000}k   shadow near {trees.ShadowInstancesIn(0)} / {trees.ShadowTrianglesIn(0) / 1000}k  " +
                                   $"proxies {trees.ShadowInstancesIn(1)} / {trees.ShadowTrianglesIn(1) / 1000}k   cells {w.Cells} (view {w.CellsInView}, shadow {w.CellsShadowOnly}, culled {w.CellsShadowCulled})\n");
            }

            overlayText.Append($"perf  {perf.Line()}   terrains {(Terrain.activeTerrains != null ? Terrain.activeTerrains.Length : 0)}   sun {(MedievalZen.Environment.DayCycle.Current != null ? MedievalZen.Environment.DayCycle.Current.SunElevation : 0f):0.0}°\n");

            overlayText.Append($"pools  near terrains {builder.PooledNear} (made {builder.TerrainsCreated})  props {builder.PropsInUse} used / {builder.PropsPooled} pooled  water {builder.WaterPooled}  heavy warmed {builder.HeavyPrefabsWarmed}/{builder.HeavyPrefabCount} made ahead {builder.HeavyStockMade} idle {builder.HeavyStockIdle}\n");
            overlayText.Append($"safety level {safetyLevel} ({SafetyLadder.Describe(safetyLevel)})   {Features}\n");
            overlayText.Append($"origin {origin}   shifts {shiftCount}  last {lastShiftMs:0.0} ms\n");
            if (MedievalZen.Wildlife.WildlifeManager.Current != null) overlayText.Append(MedievalZen.Wildlife.WildlifeManager.Current.OverlayLine()).Append('\n');
            if (MedievalZen.Traffic.RoadTrafficManager.Current != null) overlayText.Append(MedievalZen.Traffic.RoadTrafficManager.Current.OverlayLine()).Append('\n');
            MedievalZen.Sound.SoundMix.AppendOverlay(overlayText);
            overlayText.Append($"road window {(window != null ? $"{window.SFrom:0}..{window.STo:0} m, {window.Network.Settlements.Count} places" : "none")}\n");
            overlayText.Append($"hold {(startup ? "start" : frozenByStream ? "outrun" : frozenByScript ? "travel" : "-")}   generator {generatorName}");
        }
    }
}
