using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MedievalZen.Wildlife;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// Builds the <see cref="WildlifePalette"/> (menu MedievalZen/Wildlife/Build Wildlife Palette, and by itself when the editor
    /// loads with the polyperfect animals imported and the palette missing or out of date). For each catalog prefab it saves
    /// a first-party prefab variant under <see cref="PrefabFolder"/> with the pack's scripts (matched by namespace: this
    /// assembly does not reference the pack's), NavMeshAgent, CharacterController, AudioSource and any collider removed —
    /// scripts first, because the wander script requires the CharacterController. The pack itself is never edited. It then
    /// measures each animal in its idle pose (clip sampled at t = 0, skinned meshes baked), checks the catalog's bools against
    /// the controller's YAML (<see cref="ControllerGraph"/>), collects the sound clips, and logs a table.
    /// </summary>
    [InitializeOnLoad]
    internal static class WildlifeMenu
    {
        public const string PackFolder = "Assets/polyperfect/Low Poly Animated Animals";
        public const string PackPrefabs = PackFolder + "/Prefabs/Animals";
        public const string PackSounds = PackFolder + "/Sounds";
        public const string PrefabFolder = "Assets/_Project/Prefabs/Animals";
        public const string ResourcesFolder = "Assets/_Project/Resources";
        public const string PalettePath = ResourcesFolder + "/" + WildlifePalette.ResourcePath + ".asset";
        private const string SessionKey = "MedievalZen.Wildlife.AutoBuildTried";

        private static bool building;
        private static int waitFrames;

        static WildlifeMenu()
        {
            EditorApplication.delayCall += ScheduleAutoBuild;
        }

        [MenuItem("MedievalZen/Wildlife/Build Wildlife Palette", priority = 50)]
        private static void BuildMenu()
        {
            WildlifePalette palette = Build(false);
            if (palette == null) return;
            Selection.activeObject = palette;
            EditorGUIUtility.PingObject(palette);
        }

        [MenuItem("MedievalZen/Wildlife/Build Wildlife Palette", true)]
        private static bool BuildMenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        // ------------------------------------------------------------------ automatic build

        private static void ScheduleAutoBuild()
        {
            waitFrames = 0;
            EditorApplication.update -= AutoBuildWhenIdle;
            EditorApplication.update += AutoBuildWhenIdle;
        }

        private static void AutoBuildWhenIdle()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.update -= AutoBuildWhenIdle;   // never during play; the next domain reload tries again
                    return;
                }

                if (EditorApplication.isCompiling || EditorApplication.isUpdating || building)
                {
                    if (++waitFrames > 20000) EditorApplication.update -= AutoBuildWhenIdle;
                    return;
                }

                EditorApplication.update -= AutoBuildWhenIdle;
                if (!AssetDatabase.IsValidFolder(PackPrefabs)) return;   // the pack is not imported: nothing to do
                var palette = AssetDatabase.LoadAssetAtPath<WildlifePalette>(PalettePath);
                if (palette != null && palette.IsCurrent) return;
                // Once per editor session and catalog: a build that cannot complete must not repeat on every reload.
                string tried = SessionState.GetString(SessionKey, string.Empty);
                string mark = SpeciesCatalog.Signature.GetHashCode() + "/" + WildlifePalette.CurrentVersion;
                if (tried == mark) return;
                SessionState.SetString(SessionKey, mark);
                Debug.Log($"Wildlife: {(palette == null ? "no palette yet" : "the palette is out of date")}; building {PalettePath} (MedievalZen/Wildlife/Build Wildlife Palette).");
                Build(true);
            }
            catch (Exception e)
            {
                EditorApplication.update -= AutoBuildWhenIdle;
                Debug.LogError($"Wildlife: building the palette automatically failed: {e}");
            }
        }

        // ------------------------------------------------------------------ build

        /// <summary>Builds or refreshes the palette; returns it, or null when the pack is missing. Never throws.</summary>
        public static WildlifePalette Build(bool automatic)
        {
            if (building) return null;
            if (!AssetDatabase.IsValidFolder(PackPrefabs))
            {
                Debug.LogError($"Wildlife: the polyperfect animals are not imported ({PackPrefabs} is missing); no palette built.");
                return null;
            }

            building = true;
            Scene preview = default;
            try
            {
                EnsureFolder(PrefabFolder);
                EnsureFolder(ResourcesFolder);
                var palette = AssetDatabase.LoadAssetAtPath<WildlifePalette>(PalettePath);
                bool created = palette == null;
                if (created) palette = ScriptableObject.CreateInstance<WildlifePalette>();

                preview = EditorSceneManager.NewPreviewScene();
                var problems = new List<string>();
                var entries = new List<WildlifePalette.Entry>();
                var graphs = new Dictionary<string, ControllerGraph>(StringComparer.Ordinal);
                var table = new StringBuilder(4096);
                int index = 0;
                List<string> names = SpeciesCatalog.PrefabNames();
                foreach (string name in names)
                {
                    index++;
                    if (!automatic) EditorUtility.DisplayProgressBar("Wildlife palette", name, index / (float)names.Count);
                    try
                    {
                        WildlifePalette.Entry entry = BuildEntry(name, preview, graphs, problems);
                        if (entry != null) entries.Add(entry);
                    }
                    catch (Exception e)
                    {
                        problems.Add($"{name}: {e.GetType().Name}: {e.Message}");
                    }
                }

                // Check each catalog row against its controller graph.
                foreach (SpeciesInfo info in SpeciesCatalog.All)
                {
                    ControllerGraph graph = GraphFor(info.Controller, graphs);
                    if (graph == null)
                    {
                        problems.Add($"{info.Name}: controller {info.Controller} not found");
                        continue;
                    }

                    problems.AddRange(graph.Check(info));
                }

                // Sounds.
                var clips = new List<AudioClip>();
                foreach (string clipName in SpeciesCatalog.ClipNames())
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{PackSounds}/{clipName}.ogg");
                    if (clip != null) clips.Add(clip);
                    else problems.Add($"sound {clipName}.ogg not found");
                }

                // Keep the tuning the user set; add rows for new species.
                var tuning = new List<WildlifePalette.Tuning>();
                foreach (SpeciesInfo info in SpeciesCatalog.All)
                {
                    float scale = 1f;
                    foreach (WildlifePalette.Tuning t in palette.TuningRows)
                    {
                        if (t != null && t.species == info.Id) scale = t.scale;
                    }

                    tuning.Add(new WildlifePalette.Tuning { species = info.Id, scale = scale });
                }

                palette.Entries.Clear();
                palette.Entries.AddRange(entries);
                palette.TuningRows.Clear();
                palette.TuningRows.AddRange(tuning);
                palette.Clips.Clear();
                palette.Clips.AddRange(clips);
                palette.Version = WildlifePalette.CurrentVersion;
                palette.CatalogSignature = SpeciesCatalog.Signature;
                if (created) AssetDatabase.CreateAsset(palette, PalettePath);
                EditorUtility.SetDirty(palette);
                AssetDatabase.SaveAssets();

                // The table.
                table.Append("Wildlife palette built: ").Append(entries.Count).Append('/').Append(names.Count).Append(" prefabs, ")
                    .Append(clips.Count).Append(" sounds, ").Append(problems.Count).Append(" problem(s) → ").Append(PalettePath).Append('\n');
                table.Append("species            prefab                 measured h   length   scale   shown h   idle state\n");
                foreach (SpeciesInfo info in SpeciesCatalog.All)
                {
                    foreach (string p in info.Prefabs)
                    {
                        WildlifePalette.Entry e = palette.Find(p);
                        if (e == null)
                        {
                            table.Append($"{info.Name,-18} {p,-22} MISSING\n");
                            continue;
                        }

                        float scale = palette.ScaleFor(info, e);
                        table.Append($"{info.Name,-18} {p,-22} {e.height,8:0.00} m {e.length,6:0.00} m {scale,7:0.000} {e.height * scale,7:0.00} m   {e.idleState}\n");
                    }
                }

                if (problems.Count > 0)
                {
                    table.Append("Problems:\n");
                    foreach (string p in problems) table.Append("  - ").Append(p).Append('\n');
                }

                if (problems.Count > 0) Debug.LogWarning(table.ToString(), palette);
                else Debug.Log(table.ToString(), palette);
                return palette;
            }
            catch (Exception e)
            {
                Debug.LogError($"Wildlife: building the palette failed: {e}");
                return null;
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (!automatic) EditorUtility.ClearProgressBar();
                building = false;
            }
        }

        /// <summary>Saves the stripped variant of one pack prefab and measures it.</summary>
        private static WildlifePalette.Entry BuildEntry(string name, Scene preview, Dictionary<string, ControllerGraph> graphs, List<string> problems)
        {
            string source = $"{PackPrefabs}/{name}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (prefab == null)
            {
                problems.Add($"{name}: {source} not found");
                return null;
            }

            string target = $"{PrefabFolder}/{name}.prefab";
            // A variant of the pack prefab with the components removed (removed components are variant overrides). Should the
            // editor refuse that, fall back to an unpacked plain copy: still first-party, the pack untouched.
            GameObject variant = SaveStripped(prefab, target, preview, false, out string failure);
            string left = variant != null ? Leftovers(variant) : null;
            if (variant == null || left != null)
            {
                problems.Add($"{name}: {(variant == null ? failure : "the variant kept " + left)}; saved an unpacked copy instead");
                variant = SaveStripped(prefab, target, preview, true, out failure);
                left = variant != null ? Leftovers(variant) : null;
                if (variant == null)
                {
                    problems.Add($"{name}: {failure}");
                    return null;
                }

                if (left != null) problems.Add($"{name}: the saved copy still has {left}");
            }

            var entry = new WildlifePalette.Entry { name = name, prefab = variant };
            Measure(variant, preview, entry, problems);

            Animator animator = variant.GetComponentInChildren<Animator>(true);
            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
            ControllerGraph graph = controller != null ? GraphOf(controller, graphs) : null;
            if (graph != null)
            {
                entry.idleState = graph.DefaultState;
                var bools = new List<string>();
                foreach (KeyValuePair<string, string> p in graph.Parameters)
                {
                    if (p.Value == "bool") bools.Add(p.Key);
                }

                entry.bools = bools.ToArray();
            }
            else
            {
                problems.Add($"{name}: no Animator controller");
            }

            return entry;
        }

        private static GameObject SaveStripped(GameObject prefab, string target, Scene preview, bool unpack, out string failure)
        {
            failure = null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            try
            {
                if (unpack) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Strip(instance);
                if (unpack && AssetDatabase.LoadAssetAtPath<GameObject>(target) != null) AssetDatabase.DeleteAsset(target);
                PrefabUtility.SaveAsPrefabAsset(instance, target, out bool saved);
                if (!saved)
                {
                    failure = $"could not save {target}";
                    return null;
                }
            }
            catch (Exception e)
            {
                failure = $"{e.GetType().Name}: {e.Message}";
                return null;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(target);
            if (variant == null) failure = $"{target} did not load after saving";
            return variant;
        }

        /// <summary>Removes the pack's scripts (first: they require the CharacterController), then the agent, controller, sound and colliders.</summary>
        private static void Strip(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                try
                {
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                }
                catch (Exception)
                {
                    // A prefab instance may refuse; the leftover check reports it.
                }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (MonoBehaviour b in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (b != null && IsPackScript(b)) Object.DestroyImmediate(b);
                }
            }

            foreach (UnityEngine.AI.NavMeshAgent c in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) Object.DestroyImmediate(c);
            foreach (CharacterController c in root.GetComponentsInChildren<CharacterController>(true)) Object.DestroyImmediate(c);
            foreach (AudioSource c in root.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(c);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Rigidbody c in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(c);
        }

        private static bool IsPackScript(MonoBehaviour b)
        {
            Type t = b.GetType();
            string ns = t.Namespace ?? string.Empty;
            string full = t.FullName ?? t.Name;
            return ns.StartsWith("Polyperfect", StringComparison.Ordinal) || full.IndexOf("olyperfect", StringComparison.Ordinal) >= 0 ||
                   t.Name == "Animal_WanderScript" || t.Name == "Common_WanderScript" || t.Name == "AnimalPlaySound" || t.Name == "Common_PlaySound";
        }

        private static string Leftovers(GameObject root)
        {
            var found = new List<string>();
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null)
                {
                    found.Add("a missing script");
                    continue;
                }

                if (c is MonoBehaviour b && IsPackScript(b)) found.Add(c.GetType().Name);
                else if (c is UnityEngine.AI.NavMeshAgent || c is CharacterController || c is AudioSource || c is Collider || c is Rigidbody) found.Add(c.GetType().Name);
            }

            return found.Count > 0 ? string.Join(", ", found) : null;
        }

        // ------------------------------------------------------------------ measuring

        /// <summary>Idle pose at t = 0, skinned meshes baked: height, length along +Z and the lowest point, in the root's space at scale 1.</summary>
        private static void Measure(GameObject variant, Scene preview, WildlifePalette.Entry entry, List<string> problems)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(variant, preview);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                AnimationClip idle = animator != null ? IdleClip(animator.runtimeAnimatorController) : null;
                if (idle != null) idle.SampleAnimation(animator.gameObject, 0f);
                else problems.Add($"{entry.name}: no idle clip found; measured the bind pose");

                // BakeMesh's space depends on the renderer's scale handling (some rigs carry a ×100 scale on the renderer or the
                // armature), so both readings are kept and the one that agrees with the skeleton's span is used.
                var rigid = new Box();
                var full = new Box();
                var skeleton = new Box();
                var mesh = new Mesh();
                var vertices = new List<Vector3>();
                foreach (SkinnedMeshRenderer smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    smr.BakeMesh(mesh, true);
                    mesh.GetVertices(vertices);
                    Transform t = smr.transform;
                    Matrix4x4 toWorld = t.localToWorldMatrix;
                    foreach (Vector3 v in vertices)
                    {
                        rigid.Add(t.position + t.rotation * v);
                        full.Add(toWorld.MultiplyPoint3x4(v));
                    }

                    foreach (Transform bone in smr.bones)
                    {
                        if (bone != null) skeleton.Add(bone.position);
                    }
                }

                foreach (MeshRenderer mr in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mf.sharedMesh.GetVertices(vertices);
                    foreach (Vector3 v in vertices)
                    {
                        Vector3 w = mr.transform.TransformPoint(v);
                        rigid.Add(w);
                        full.Add(w);
                    }
                }

                Object.DestroyImmediate(mesh);
                if (!rigid.Any)
                {
                    problems.Add($"{entry.name}: no meshes to measure");
                    return;
                }

                Bounds bounds = rigid.Bounds;
                if (skeleton.Any && skeleton.Bounds.size.y > 0.01f)
                {
                    // The mesh wraps the bones: its height is about 1–2 times the skeleton's. Take the reading nearest 1.3 times.
                    float bone = skeleton.Bounds.size.y;
                    float er = Mathf.Abs(Mathf.Log(rigid.Bounds.size.y / bone / 1.3f + 1e-6f)), ef = Mathf.Abs(Mathf.Log(full.Bounds.size.y / bone / 1.3f + 1e-6f));
                    bounds = ef < er ? full.Bounds : rigid.Bounds;
                    if (bounds.size.y < bone * 0.5f || bounds.size.y > bone * 4f)
                    {
                        problems.Add($"{entry.name}: baked mesh {bounds.size.y:0.000} m tall but its bones span {bone:0.000} m; using the bones (+20 %)");
                        Bounds k = skeleton.Bounds;
                        bounds = new Bounds(k.center, k.size * 1.2f);
                    }
                }

                entry.height = bounds.size.y;
                entry.length = bounds.size.z;
                entry.footOffset = bounds.min.y;
                if (bounds.size.x > bounds.size.z * 1.35f && entry.height < bounds.size.x)
                {
                    problems.Add($"{entry.name}: wider ({bounds.size.x:0.00} m) than long ({bounds.size.z:0.00} m) — it may not face +Z");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>A bounding box grown point by point.</summary>
        private struct Box
        {
            public bool Any;
            public Bounds Bounds;

            public void Add(Vector3 p)
            {
                if (!Any)
                {
                    Bounds = new Bounds(p, Vector3.zero);
                    Any = true;
                }
                else
                {
                    Bounds.Encapsulate(p);
                }
            }
        }

        /// <summary>The motion of the controller's default state (through an override controller's replacement).</summary>
        private static AnimationClip IdleClip(RuntimeAnimatorController runtime)
        {
            if (runtime == null) return null;
            AnimatorOverrideController overrides = runtime as AnimatorOverrideController;
            var controller = (overrides != null ? overrides.runtimeAnimatorController : runtime) as AnimatorController;
            if (controller == null || controller.layers.Length == 0) return null;
            AnimatorState state = controller.layers[0].stateMachine.defaultState;
            Motion motion = state != null ? state.motion : null;
            var clip = motion as AnimationClip;
            if (clip == null && motion is BlendTree tree && tree.children.Length > 0) clip = tree.children[0].motion as AnimationClip;
            if (clip != null && overrides != null && overrides[clip] != null) clip = overrides[clip];
            return clip;
        }

        // ------------------------------------------------------------------ controller graphs

        private static ControllerGraph GraphOf(RuntimeAnimatorController runtime, Dictionary<string, ControllerGraph> graphs)
        {
            AnimatorOverrideController overrides = runtime as AnimatorOverrideController;
            RuntimeAnimatorController baseController = overrides != null ? overrides.runtimeAnimatorController : runtime;
            string path = AssetDatabase.GetAssetPath(baseController);
            if (string.IsNullOrEmpty(path)) return null;
            return GraphFor(Path.GetFileNameWithoutExtension(path), graphs, path);
        }

        private static ControllerGraph GraphFor(string controllerName, Dictionary<string, ControllerGraph> graphs, string path = null)
        {
            if (graphs.TryGetValue(controllerName, out ControllerGraph g)) return g;
            path ??= $"{PackFolder}/Animator Controllers/{controllerName}.controller";
            if (!File.Exists(path)) return null;
            g = ControllerGraph.Parse(File.ReadAllText(path));
            graphs[controllerName] = g;
            return g;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
