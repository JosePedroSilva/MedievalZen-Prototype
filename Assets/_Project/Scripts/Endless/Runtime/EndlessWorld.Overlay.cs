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
            overlayText.Append("Endless road  (F3 hide, F4 force origin shift)\n");
            overlayText.Append($"s {travellerS:0.0} m   abs ({x:0.0}, {z:0.0})   scene ({scene.x:0.0}, {scene.y:0.0}, {scene.z:0.0})\n");
            overlayText.Append($"region {RegionName}   seed {Seed}\n");
            overlayText.Append($"tiles  near {near}  far {far}  building {building.Count}  generated {queued}  in flight {running.Count}  waiting {waiting}  releasing {releasing.Count}\n");
            overlayText.Append($"generation  last {lastGenerationMs:0.0} ms  worst {worstGenerationMs:0.0} ms   ({Mathf.Clamp(settings.workerCount, 1, 8)} workers)\n");
            overlayText.Append($"apply  this frame {lastApplyMs:0.00} ms  worst 10 s {WorstApplyMsLast10s:0.00} ms  (budget {settings.applyBudgetMs:0.0})\n");
            overlayText.Append($"worst slice 10 s {WorstSliceMsLast10s:0.00} ms ({WorstSliceNameLast10s})\n");
            overlayText.Append($"far meshes {builder.FarShown}  ({builder.FarTrianglesShown / 1000}k triangles)   made {builder.FarMeshesCreated}, pooled {builder.PooledFar}\n");
            if (trees != null)
            {
                overlayText.Append($"trees  {trees.RegisteredTrees} registered  {trees.InstancesThisFrame} drawn in {trees.DrawCallsThisFrame} draws this frame  ({trees.PrototypeCount} prototypes)  trunk colliders {trees.ActiveColliders}\n");
            }

            overlayText.Append($"pools  near terrains {builder.PooledNear} (made {builder.TerrainsCreated})  props {builder.PropsInUse} used / {builder.PropsPooled} pooled  water {builder.WaterPooled}\n");
            overlayText.Append($"safety level {safetyLevel} ({SafetyLadder.Describe(safetyLevel)})   {Features}\n");
            overlayText.Append($"origin {origin}   shifts {shiftCount}  last {lastShiftMs:0.0} ms\n");
            overlayText.Append($"road window {(window != null ? $"{window.SFrom:0}..{window.STo:0} m, {window.Network.Settlements.Count} places" : "none")}\n");
            overlayText.Append($"hold {(startup ? "start" : frozenByStream ? "outrun" : frozenByScript ? "travel" : "-")}   generator {generatorName}");
        }
    }
}
