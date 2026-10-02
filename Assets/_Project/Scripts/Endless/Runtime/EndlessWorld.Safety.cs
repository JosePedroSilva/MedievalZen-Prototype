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
            treeShadows = treeShadows,
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
            Debug.Log($"Endless road: running OK for {SafetyLadder.RunningOkSeconds:0} s at safety level {safetyLevel} — {breadcrumb.lastLoad}.", this);
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
            Camera camera = Camera.main;
            if (camera == null)
            {
                if (drawCamera == null && traveller != null) drawCamera = traveller.GetComponentInChildren<Camera>();
                camera = drawCamera;
            }

            trees.Draw(camera);
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
