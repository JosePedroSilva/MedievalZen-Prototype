using System;
using System.Collections.Generic;
using System.IO;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The breadcrumb file of the safety ladder (<c>endless_run.json</c> in <see cref="Application.persistentDataPath"/>):
    /// what level a run used, how far it got, and the last load milestone it logged. Read and written by
    /// <see cref="EndlessWorld"/>; reset by the menu MedievalZen/Endless Road/Reset Safety Level.
    /// </summary>
    public static class EndlessRunFile
    {
        /// <summary>File name inside <see cref="Application.persistentDataPath"/>.</summary>
        public const string FileName = "endless_run.json";

        /// <summary>Full path of the breadcrumb.</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>The file's contents.</summary>
        [Serializable]
        public sealed class Breadcrumb
        {
            public int level;
            public string phase = "started";
            public string started = string.Empty;
            public string updated = string.Empty;
            public string features = string.Empty;
            public string unity = string.Empty;
            public string gpu = string.Empty;
            public string lastLoad = string.Empty;
            public string note = string.Empty;

            /// <summary>The record the ladder works on.</summary>
            public SafetyRecord Record => new SafetyRecord { Level = level, Phase = SafetyLadder.ParsePhase(phase) };
        }

        /// <summary>Reads the breadcrumb; false when there is none or it is unreadable.</summary>
        public static bool TryRead(out Breadcrumb breadcrumb)
        {
            breadcrumb = null;
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return false;
                breadcrumb = JsonUtility.FromJson<Breadcrumb>(File.ReadAllText(path));
                return breadcrumb != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Endless road: could not read {FileName} ({e.Message}); treating this as a first run.");
                breadcrumb = null;
                return false;
            }
        }

        /// <summary>Writes the breadcrumb (time-stamped); failures are logged once per call site and ignored.</summary>
        public static void Write(Breadcrumb breadcrumb)
        {
            try
            {
                breadcrumb.updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(FilePath, JsonUtility.ToJson(breadcrumb, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Endless road: could not write {FilePath} ({e.Message}).");
            }
        }

        /// <summary>Sets the ladder back to level 0 (menu MedievalZen/Endless Road/Reset Safety Level).</summary>
        public static void Reset()
        {
            SafetyRecord r = SafetyLadder.Reset();
            Write(new Breadcrumb { level = r.Level, phase = SafetyLadder.PhaseName(r.Phase), note = "reset by MedievalZen/Endless Road/Reset Safety Level" });
        }
    }

    /// <summary>Feature toggles, the safety ladder, load milestones, the late tree pass and the far-tile resources.</summary>
    public sealed partial class EndlessWorld
    {
        private int safetyLevel;
        private string pendingSafetyToast;
        private EndlessRunFile.Breadcrumb breadcrumb;
        private bool safetyEnded;
        private bool runningOkWritten;
        private float readyAt = -1f;
        private readonly LoadMilestones milestones = new LoadMilestones();
        private float nextMilestoneCheck;
        private Camera drawCamera;

        /// <summary>Safety level of this run (0 = everything; see <see cref="SafetyLadder"/>).</summary>
        public int SafetyLevel => safetyLevel;

        /// <summary>What is on in this run: the inspector toggles and the safety level together.</summary>
        public EndlessFeatures Features => EndlessFeatures.And(new EndlessFeatures
        {
            farTiles = farTiles, farTrees = farTrees, nearTrees = nearTrees, grassDetails = grassDetails, props = props, water = water,
            treeShadows = treeShadows, animals = animals, forestExtras = forestExtras,
        }, SafetyLadder.FeaturesFor(safetyLevel));

        /// <summary>The GPU load right now (overlay and milestones).</summary>
        public EndlessLoad Load => new EndlessLoad
        {
            NearTiles = CountActive(TileLod.Near),
            FarTiles = builder != null ? builder.FarShown : 0,
            FarTriangles = builder != null ? builder.FarTrianglesShown : 0,
            TreeInstances = trees != null ? trees.InstancesThisFrame : 0,
            TreeDrawCalls = trees != null ? trees.DrawCallsThisFrame : 0,
        };

        private int CountActive(TileLod lod)
        {
            int n = 0;
            foreach (CoordState state in states.Values)
            {
                if (state.Active != null && state.Active.Lod == lod) n++;
            }

            return n;
        }

        // ------------------------------------------------------------------ safety ladder

        /// <summary>Reads the previous run's breadcrumb, picks this run's level and records the start.</summary>
        private void BeginSafetyRun()
        {
            bool hasPrevious = EndlessRunFile.TryRead(out EndlessRunFile.Breadcrumb previous);
            SafetyDecision decision = SafetyLadder.Decide(hasPrevious, hasPrevious ? previous.Record : default);
            safetyLevel = decision.Level;
            if (decision.SteppedDown)
            {
                string last = hasPrevious && !string.IsNullOrEmpty(previous.lastLoad) ? $" Its last logged load: {previous.lastLoad}." : string.Empty;
                Debug.LogError($"Endless road: {decision.Message}. The previous run (level {previous.level}, started {previous.started}) never reached " +
                               $"{SafetyLadder.RunningOkSeconds:0} s of rendering nor a clean exit.{last} Switched off now: {Describe(SafetyLadder.FeaturesFor(safetyLevel))}. " +
                               $"Breadcrumb: {EndlessRunFile.FilePath}. MedievalZen/Endless Road/Reset Safety Level returns to everything.", this);
                pendingSafetyToast = decision.Message;
            }

            SafetyRecord start = SafetyLadder.OnStart(safetyLevel);
            breadcrumb = new EndlessRunFile.Breadcrumb
            {
                level = start.Level,
                phase = SafetyLadder.PhaseName(start.Phase),
                started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                features = Features.ToString(),
                unity = Application.unityVersion,
                gpu = $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize} MB)",
                note = decision.SteppedDown ? decision.Message : string.Empty,
            };
            EndlessRunFile.Write(breadcrumb);
        }

        private static string Describe(EndlessFeatures f)
        {
            var off = new List<string>();
            if (!f.farTrees) off.Add("distant trees");
            if (!f.farTiles) off.Add("distant tiles");
            if (!f.nearTrees) off.Add("near trees");
            if (!f.grassDetails) off.Add("grass");
            if (!f.props) off.Add("props");
            if (!f.water) off.Add("water");
            if (!f.animals) off.Add("animals");
            if (!f.forestExtras) off.Add("forest extras");
            return off.Count == 0 ? "nothing" : string.Join(", ", off);
        }

        private void LogStartLine()
        {
            Debug.Log($"Endless road: start — safety level {safetyLevel} ({SafetyLadder.Describe(safetyLevel)}); features {Features}; " +
                      $"near {settings.nearDistance:0} m, far {(Features.farTiles ? settings.farDistance : settings.nearDistance):0} m; " +
                      $"{(trees != null ? trees.PrototypeCount : 0)} instanced tree prototypes; far material {(builder != null && farMaterialInUse != null ? farMaterialInUse.name : "none")}; " +
                      $"breadcrumb {EndlessRunFile.FilePath}.", this);
        }

        /// <summary>The start hold is over: the HUD gets the safety toast (if any) and the "running OK" clock starts.</summary>
        private void NoteReady()
        {
            if (readyAt < 0f) readyAt = Time.unscaledTime;
            if (pendingSafetyToast == null) return;
            HudController hud = HudController.Instance;
            if (hud != null) hud.ShowToast(pendingSafetyToast);
            pendingSafetyToast = null;
        }

        private void UpdateSafetyRun()
        {
            if (runningOkWritten || safetyEnded || breadcrumb == null || readyAt < 0f) return;
            if (Time.unscaledTime - readyAt < SafetyLadder.RunningOkSeconds) return;
            runningOkWritten = true;
            SafetyRecord r = SafetyLadder.OnRunningOk(breadcrumb.Record);
            breadcrumb.phase = SafetyLadder.PhaseName(r.Phase);
            breadcrumb.lastLoad = Load.ToString();
            EndlessRunFile.Write(breadcrumb);
            Debug.Log($"Endless road: running OK for {SafetyLadder.RunningOkSeconds:0} s at safety level {safetyLevel} — {breadcrumb.lastLoad}.{ShadowReport()} Performance: {PerfSummary(perf.Recent, 300)}", this);
            Debug.Log(perf.MarkerReport + (trees != null ? " " + trees.ImpostorSummary : string.Empty), this);
        }

        /// <summary>
        /// Tree shadows as they are being drawn right now (for the log, so a "no shadows" report can be told apart from
        /// "shadows hidden behind the trees"): distance in effect, shadow-only instances, and where the sun stands against the view.
        /// </summary>
        private string ShadowReport()
        {
            if (trees == null) return string.Empty;
            string sun = string.Empty;
            Light light = RenderSettings.sun;
            Camera view = Camera.main;
            if (light != null && view != null)
            {
                Vector3 toSun = -light.transform.forward;
                float elevation = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
                Vector3 flatSun = new Vector3(toSun.x, 0f, toSun.z), flatView = new Vector3(view.transform.forward.x, 0f, view.transform.forward.z);
                float bearing = flatSun.sqrMagnitude > 1e-6f && flatView.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(flatView, flatSun, Vector3.up) : 0f;
                sun = $"; sun {elevation:0}° high, {bearing:0}° from the view direction (180 = behind the camera), light shadows {light.shadows} strength {light.shadowStrength:0.00}";
            }

            return $" Tree shadows: {trees.ShadowDistanceInEffect:0} m in effect (setting {trees.ShadowDistance:0} m), {trees.ShadowInstancesThisFrame} shadow instances, " +
                   $"{trees.ShadowTrianglesThisFrame / 1000}k shadow triangles{sun}.";
        }

        private void EndSafetyRun()
        {
            if (safetyEnded || breadcrumb == null) return;
            safetyEnded = true;
            SafetyRecord r = SafetyLadder.OnCleanExit(breadcrumb.Record);
            breadcrumb.phase = SafetyLadder.PhaseName(r.Phase);
            EndlessRunFile.Write(breadcrumb);
        }

        // ------------------------------------------------------------------ late pass (EndlessLateDraw)

        /// <summary>Submits the instanced trees for this frame's view, keeps the trunk colliders, the run clock and the milestones.</summary>
        internal void LateDraw()
        {
            if (generator == null || trees == null) return;
            EndlessFeatures f = Features;
            trees.Enabled = f.nearTrees;
            trees.Shadows = f.treeShadows;
            MedievalZen.Environment.ForestMood.Allowed = f.forestExtras;
            if (settings != null)
            {
                // Read every frame so the distances can be tuned live from the settings asset.
                trees.FullDetailDistance = settings.treeFullDetailDistance;
                trees.MidDetailDistance = settings.treeMidDetailDistance;
                trees.DetailScale = settings.treeDetailScale;
                trees.ShadowDistance = settings.treeShadowDistance;
                trees.ShadowProxyDistance = settings.treeShadowProxyDistance;
                trees.PlantShadowDistance = settings.plantShadowDistance;
                trees.FernDrawDistance = settings.fernDrawDistance;
                trees.UndergrowthDrawDistance = settings.undergrowthDrawDistance;
                trees.CullShadowCasters = settings.treeShadowCasterCulling;
                trees.ReuseWalk = settings.treeWalkReuse;
                trees.ImpostorDistance = settings.treeImpostorDistance;
                trees.ImpostorMaxPixels = settings.treeImpostorMaxPixels;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                if (drawCamera == null && traveller != null) drawCamera = traveller.GetComponentInChildren<Camera>();
                camera = drawCamera;
            }

            trees.Draw(camera);
            perf.Sample((float)trees.WalkMs, (float)trees.SubmitMs, trees.WalkReused);
            if (traveller != null) trees.UpdateColliders(traveller.position);
            UpdateSafetyRun();

            if (Time.unscaledTime < nextMilestoneCheck) return;
            nextMilestoneCheck = Time.unscaledTime + 0.5f;
            EndlessLoad load = Load;
            if (!milestones.Update(load, out string line)) return;
            Debug.Log(line, this);
            if (breadcrumb != null && !safetyEnded)
            {
                breadcrumb.lastLoad = load.ToString();
                EndlessRunFile.Write(breadcrumb);
            }
        }

        // ------------------------------------------------------------------ far tile resources

        private Material farMaterialInUse;

        /// <summary>The far-tile material: the scene's asset, else a runtime one from the shader (logged), else none (logged).</summary>
        private Material ResolveFarMaterial()
        {
            if (farMaterial != null)
            {
                farMaterialInUse = farMaterial;
                return farMaterial;
            }

            Shader shader = Shader.Find("MedievalZen/FarTerrain");
            if (shader != null)
            {
                Debug.LogWarning("EndlessWorld has no far-tile material; made one from MedievalZen/FarTerrain at runtime (a build would not include the shader). Run MedievalZen/Endless Road/Setup Scene.", this);
                farMaterialInUse = new Material(shader) { name = "FarTerrain (runtime)" };
                return farMaterialInUse;
            }

            Debug.LogError("EndlessWorld: no far-tile material and the MedievalZen/FarTerrain shader was not found; distant tiles will render pink. Run MedievalZen/Endless Road/Setup Scene.", this);
            return null;
        }

        /// <summary>
        /// Far-prop styles of the palette: rock families from their prefabs' local bounds, houses as measured
        /// (<see cref="FarPropStyles"/>); main thread only.
        /// </summary>
        private static FarPropStyles BuildFarProps(WorldPalette palette)
        {
            var styles = new FarPropStyles();
            if (palette == null) return styles;
            foreach (PrefabFamily family in palette.families)
            {
                if (family == null || !PropRole.IsRock(family.id)) continue;
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(family);
                if (prefabs.Count == 0) continue;
                var variants = new FarPropStyle[prefabs.Count];
                for (int v = 0; v < prefabs.Count; v++)
                {
                    Bounds b = WorldBuilder.LocalBounds(prefabs[v]);
                    variants[v] = FarPropStyles.RockFromBounds(family.id, b.min, b.max);
                }

                styles.Set(family.id, variants);
            }

            return styles;
        }

        /// <summary>Far-tree styles for every tree family of the palette (shape, measured size and colour per prefab).</summary>
        private static FarTreeStyles BuildFarStyles(WorldPalette palette)
        {
            var styles = new FarTreeStyles();
            if (palette == null) return styles;
            foreach (PrefabFamily family in palette.families)
            {
                if (family == null || !PropRole.IsTree(family.id)) continue;
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(family);
                if (prefabs.Count == 0) continue;
                var variants = new FarTreeStyle[prefabs.Count];
                for (int v = 0; v < prefabs.Count; v++)
                {
                    Bounds b = WorldBuilder.LocalBounds(prefabs[v]);
                    variants[v] = FarTreeStyles.ForPrefab(prefabs[v].name, b.max.y, Mathf.Max(b.extents.x, b.extents.z));
                }

                styles.Set(family.id, variants);
            }

            return styles;
        }
    }
}
