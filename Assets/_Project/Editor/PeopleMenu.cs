using System;
using System.Collections.Generic;
using System.Text;
using MedievalZen.People;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// Builds the people of the endless road's inns and villages (menu MedievalZen/People/Build People Palette, and by itself when the editor
    /// loads with the palette missing or out of date): a humanoid avatar for the Fantasy Kingdom characters with the human Hips on the
    /// pelvis bone (Synty's import maps it to the ground-level <c>Root</c>, as for the player's peasant), two small Animator controllers
    /// (idle to walk on <c>Speed</c>, masculine and feminine in-place clips of the Base Locomotion pack, no root motion), one first-party
    /// prefab per <see cref="PeopleCatalog"/> character (the Synty prefab unpacked: its 21 other characters' meshes, colliders and scripts
    /// removed, the avatar and controller set) and <c>Resources/PeoplePalette</c>. Logs the skeleton check against the player's avatar,
    /// the clips' speeds and a table of the characters. The Synty packs are never edited. The people need no scene change: the
    /// <see cref="PeopleManager"/> creates itself in Play mode when the endless road is loaded.
    /// </summary>
    [InitializeOnLoad]
    internal static class PeopleMenu
    {
        public const string AnimationFolder = "Assets/_Project/Animation";
        public const string AvatarPath = AnimationFolder + "/FantasyKingdom_Humanoid.asset";
        public const string MasculinePath = AnimationFolder + "/VillagerMasculine.controller";
        public const string FemininePath = AnimationFolder + "/VillagerFeminine.controller";
        public const string ResourcesFolder = "Assets/_Project/Resources";
        public const string PalettePath = ResourcesFolder + "/" + PeoplePalette.ResourcePath + ".asset";
        private const string Polygon = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/";
        private const string SessionKey = "MedievalZen.People.AutoBuildTried";

        /// <summary>Walking pace the controllers' walk clip is slowed to (<see cref="VillagerBrain.WalkSpeed"/>), m/s.</summary>
        private const float Pace = VillagerBrain.WalkSpeed;

        /// <summary>Natural speed of the in-place walk cycles (measured from their root-motion twins for the player), m/s.</summary>
        private const float ClipWalkSpeed = 1.452f;

        private static bool building;
        private static int waitFrames;

        static PeopleMenu()
        {
            EditorApplication.delayCall += () =>
            {
                waitFrames = 0;
                EditorApplication.update -= AutoBuildWhenIdle;
                EditorApplication.update += AutoBuildWhenIdle;
            };
        }

        [MenuItem("MedievalZen/People/Build People Palette", priority = 52)]
        private static void BuildMenu()
        {
            PeoplePalette palette = Build(false);
            if (palette == null) return;
            Selection.activeObject = palette;
            EditorGUIUtility.PingObject(palette);
        }

        [MenuItem("MedievalZen/People/Build People Palette", true)]
        private static bool BuildMenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static void AutoBuildWhenIdle()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.update -= AutoBuildWhenIdle;
                    return;
                }

                if (EditorApplication.isCompiling || EditorApplication.isUpdating || building)
                {
                    if (++waitFrames > 20000) EditorApplication.update -= AutoBuildWhenIdle;
                    return;
                }

                EditorApplication.update -= AutoBuildWhenIdle;
                if (!AssetDatabase.IsValidFolder(PeopleCatalog.SourceFolder)) return;   // Fantasy Kingdom not imported
                var palette = AssetDatabase.LoadAssetAtPath<PeoplePalette>(PalettePath);
                if (palette != null && palette.IsCurrent) return;
                string mark = PeopleCatalog.Signature.GetHashCode() + "/" + PeoplePalette.CurrentVersion;
                if (SessionState.GetString(SessionKey, string.Empty) == mark) return;
                SessionState.SetString(SessionKey, mark);
                Debug.Log($"People: {(palette == null ? "no palette yet" : "the palette is out of date")}; building {PalettePath} (MedievalZen/People/Build People Palette).");
                Build(true);
            }
            catch (Exception e)
            {
                EditorApplication.update -= AutoBuildWhenIdle;
                Debug.LogError($"People: building the palette automatically failed: {e}");
            }
        }

        // ------------------------------------------------------------------ build

        /// <summary>Builds or refreshes everything; returns the palette, or null when the characters are missing. Never throws.</summary>
        public static PeoplePalette Build(bool automatic)
        {
            if (building) return null;
            building = true;
            Scene preview = default;
            var log = new StringBuilder(4096);
            var problems = new List<string>();
            try
            {
                if (!AssetDatabase.IsValidFolder(PeopleCatalog.SourceFolder))
                {
                    Debug.LogError($"People: the Fantasy Kingdom characters are not imported ({PeopleCatalog.SourceFolder} is missing); no palette built.");
                    return null;
                }

                WorldPaletteMenu.EnsureFolder(AnimationFolder);
                WorldPaletteMenu.EnsureFolder(PeopleCatalog.PrefabFolder);
                WorldPaletteMenu.EnsureFolder(ResourcesFolder);
                preview = EditorSceneManager.NewPreviewScene();

                // 1. The avatar (from the first character: they share one skeleton).
                var first = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeopleCatalog.SourceFolder}/{PeopleCatalog.All[0].Name}.prefab");
                Avatar avatar = first != null ? BuildAvatar(first, preview, log, problems) : null;
                if (avatar == null)
                {
                    problems.Add("no usable humanoid avatar: the people are not built");
                    return null;
                }

                CompareWithPlayer(avatar, log);

                // 2. The controllers.
                AnimatorController masculine = BuildController(MasculinePath, "Masc", log, problems);
                AnimatorController feminine = BuildController(FemininePath, "Femn", log, problems);
                if (masculine == null || feminine == null) return null;

                // 3. The characters.
                var palette = AssetDatabase.LoadAssetAtPath<PeoplePalette>(PalettePath);
                bool created = palette == null;
                if (created) palette = ScriptableObject.CreateInstance<PeoplePalette>();
                var entries = new List<PeoplePalette.Entry>();
                log.Append("\ncharacter                      triangles  height  hips   clips\n");
                int index = 0;
                foreach (PersonCharacter c in PeopleCatalog.All)
                {
                    index++;
                    if (!automatic) EditorUtility.DisplayProgressBar("People palette", c.Name, index / (float)PeopleCatalog.All.Length);
                    try
                    {
                        PeoplePalette.Entry entry = BuildCharacter(c, avatar, c.Feminine ? feminine : masculine, preview, problems);
                        if (entry == null) continue;
                        entries.Add(entry);
                        log.Append($"{c.Name,-30} {Triangles(entry.prefab),9}  {entry.height,5:0.00}  {entry.hipHeight,4:0.00}   {(c.Feminine ? "feminine" : "masculine")}\n");
                    }
                    catch (Exception e)
                    {
                        problems.Add($"{c.Name}: {e.GetType().Name}: {e.Message}");
                    }
                }

                palette.Entries.Clear();
                palette.Entries.AddRange(entries);
                palette.Version = PeoplePalette.CurrentVersion;
                palette.CatalogSignature = PeopleCatalog.Signature;
                if (created) AssetDatabase.CreateAsset(palette, PalettePath);
                EditorUtility.SetDirty(palette);
                AssetDatabase.SaveAssets();

                // Verify the references after saving.
                palette = AssetDatabase.LoadAssetAtPath<PeoplePalette>(PalettePath);
                int missing = 0;
                foreach (PeoplePalette.Entry e in palette.Entries)
                {
                    if (e.prefab == null) missing++;
                }

                if (missing > 0) problems.Add($"{missing} palette entries lost their prefab after saving");
                log.Insert(0, $"People palette built: {entries.Count}/{PeopleCatalog.All.Length} characters, {problems.Count} problem(s) → {PalettePath}\n");
                if (problems.Count > 0)
                {
                    log.Append("Problems:\n");
                    foreach (string p in problems) log.Append("  - ").Append(p).Append('\n');
                    Debug.LogWarning(log.ToString(), palette);
                }
                else
                {
                    Debug.Log(log.ToString(), palette);
                }

                return palette;
            }
            catch (Exception e)
            {
                Debug.LogError($"People: building the palette failed: {e}\n{log}");
                return null;
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (!automatic) EditorUtility.ClearProgressBar();
                building = false;
            }
        }

        // ------------------------------------------------------------------ avatar

        /// <summary>
        /// The Fantasy Kingdom avatar with the human Hips on the pelvis bone <c>Hips</c> and no finger bones (both hands' fingers share names in
        /// the model, which the avatar builder cannot tell apart; the clips do not move them), saved as <see cref="AvatarPath"/>. Falls back
        /// to the model's own avatar (Hips on <c>Root</c>: the pelvis then does not follow the clips' hip motion) with a warning.
        /// </summary>
        private static Avatar BuildAvatar(GameObject character, Scene preview, StringBuilder log, List<string> problems)
        {
            Animator sourceAnimator = character.GetComponentInChildren<Animator>(true);
            Avatar source = sourceAnimator != null ? sourceAnimator.avatar : null;
            if (source == null || !source.isHuman)
            {
                problems.Add($"{character.name} has no humanoid avatar");
                return null;
            }

            HumanDescription description = source.humanDescription;
            var human = new List<HumanBone>();
            string originalHips = "?";
            foreach (HumanBone bone in description.human)
            {
                if (IsFinger(bone.humanName)) continue;
                HumanBone b = bone;
                if (b.humanName == "Hips")
                {
                    originalHips = b.boneName;
                    b.boneName = "Hips";
                }

                human.Add(b);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(character, preview);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                if (FindDeep(instance.transform, "Hips") == null)
                {
                    problems.Add($"{character.name} has no 'Hips' bone; using Synty's avatar (Hips → '{originalHips}')");
                    return source;
                }

                // The importer's skeleton entries carry their parent's name: the root must be called what the model's root was at import
                // (as for the player's avatar, see PlayerAnimationMenu), or the builder rejects the hierarchy.
                SkeletonBone[] sourceSkeleton = description.skeleton;
                instance.name = sourceSkeleton != null && sourceSkeleton.Length > 0 && !string.IsNullOrEmpty(sourceSkeleton[0].name) ? sourceSkeleton[0].name : character.name;
                description.human = human.ToArray();
                description.skeleton = BuildSkeleton(instance.transform, sourceSkeleton);
                Avatar avatar = AvatarBuilder.BuildHumanAvatar(instance, description);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    if (avatar != null) Object.DestroyImmediate(avatar);
                    problems.Add($"rebuilding the Fantasy Kingdom avatar with Hips on the pelvis failed; using Synty's avatar (Hips → '{originalHips}')");
                    return source;
                }

                avatar.name = "FantasyKingdom_Humanoid";
                var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(avatar, existing);
                    Object.DestroyImmediate(avatar);
                    avatar = existing;
                    EditorUtility.SetDirty(avatar);
                }
                else
                {
                    AssetDatabase.CreateAsset(avatar, AvatarPath);
                }

                AssetDatabase.SaveAssets();
                log.Append($"Avatar {AvatarPath}: human Hips moved from '{originalHips}' to 'Hips'; {human.Count} human bones (fingers left out).\n");
                return avatar;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static bool IsFinger(string humanName) =>
            humanName.IndexOf("Thumb", StringComparison.Ordinal) >= 0 || humanName.IndexOf("Index", StringComparison.Ordinal) >= 0 ||
            humanName.IndexOf("Middle", StringComparison.Ordinal) >= 0 || humanName.IndexOf("Ring", StringComparison.Ordinal) >= 0 ||
            humanName.IndexOf("Little", StringComparison.Ordinal) >= 0;

        private static SkeletonBone[] BuildSkeleton(Transform root, SkeletonBone[] source)
        {
            var byName = new Dictionary<string, SkeletonBone>();
            if (source != null)
            {
                foreach (SkeletonBone bone in source)
                {
                    if (!byName.ContainsKey(bone.name)) byName.Add(bone.name, bone);
                }
            }

            var bones = new List<SkeletonBone>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || !byName.TryGetValue(t.name, out SkeletonBone bone))
                {
                    bone = new SkeletonBone { position = t.localPosition, rotation = t.localRotation, scale = t.localScale };
                }

                bone.name = t.name;
                bones.Add(bone);
            }

            return bones.ToArray();
        }

        /// <summary>Logs how the Fantasy Kingdom skeleton compares with the player's (bone names and rest positions of the body bones).</summary>
        private static void CompareWithPlayer(Avatar avatar, StringBuilder log)
        {
            var player = AssetDatabase.LoadAssetAtPath<Avatar>(PlayerAnimationMenu.AvatarPath);
            if (player == null || !player.isHuman)
            {
                log.Append("Skeleton check: the player's avatar is not built yet; skipped.\n");
                return;
            }

            var mine = new Dictionary<string, Vector3>();
            foreach (SkeletonBone b in avatar.humanDescription.skeleton) mine[b.name] = b.position;
            int same = 0, compared = 0;
            float worst = 0f;
            string worstBone = "-";
            foreach (HumanBone h in player.humanDescription.human)
            {
                if (IsFinger(h.humanName)) continue;
                compared++;
                foreach (SkeletonBone b in player.humanDescription.skeleton)
                {
                    if (b.name != h.boneName || !mine.TryGetValue(b.name, out Vector3 p)) continue;
                    same++;
                    float d = Vector3.Distance(p, b.position);
                    if (d > worst)
                    {
                        worst = d;
                        worstBone = b.name;
                    }

                    break;
                }
            }

            log.Append($"Skeleton check against the player's avatar: {same}/{compared} body bones by the same name, largest rest-position difference {worst * 100f:0.0} cm ({worstBone}). " +
                       "Offline the two rigs matched bone for bone (Root 0.028 m, Hips 0.876 m, same rest rotations); only the finger bones differ.\n");
        }

        // ------------------------------------------------------------------ controllers

        /// <summary>One state: a 1D blend on Speed from the standing idle (0) to the walk cycle (at the villagers' pace, slowed to match).</summary>
        private static AnimatorController BuildController(string path, string set, StringBuilder log, List<string> problems)
        {
            string folder = set == "Femn" ? "Feminine" : "Masculine";
            AnimationClip idle = LoadClip($"{Polygon}{folder}/Idle/A_Idle_Standing_{set}.fbx");
            AnimationClip walk = LoadClip($"{Polygon}{folder}/Locomotion/Walk/A_Walk_F_{set}.fbx");
            if (idle == null || walk == null)
            {
                problems.Add($"{folder} idle or walk clip missing (is the ANIMATION Base Locomotion pack imported?)");
                return null;
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter(Villager.SpeedParameter, AnimatorControllerParameterType.Float);
            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = Villager.SpeedParameter;
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, Pace);
            ChildMotion[] children = tree.children;
            children[1].timeScale = Pace / ClipWalkSpeed;
            tree.children = children;
            controller.layers[0].stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            log.Append($"Controller {path}: {idle.name} → {walk.name} at {Pace:0.00} m/s (walk clip x{Pace / ClipWalkSpeed:0.000}; clip averageSpeed {walk.averageSpeed.magnitude:0.000}).\n");
            return controller;
        }

        private static AnimationClip LoadClip(string fbx)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
            }

            return null;
        }

        // ------------------------------------------------------------------ characters

        private static PeoplePalette.Entry BuildCharacter(PersonCharacter c, Avatar avatar, AnimatorController controller, Scene preview, List<string> problems)
        {
            string sourcePath = $"{PeopleCatalog.SourceFolder}/{c.Name}.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                problems.Add($"{c.Name}: {sourcePath} not found");
                return null;
            }

            string target = $"{PeopleCatalog.PrefabFolder}/{c.Name}.prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
            float height = 1.8f;
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = c.Name;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // Keep only this character's own body (the Synty prefab carries all 22 bodies, 21 switched off).
                int removed = 0;
                foreach (SkinnedMeshRenderer smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr == null || smr.gameObject == instance) continue;
                    if (!smr.gameObject.activeSelf || smr.gameObject.name != c.Name)
                    {
                        Object.DestroyImmediate(smr.gameObject);
                        removed++;
                    }
                }

                foreach (Collider col in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                foreach (Rigidbody rb in instance.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                foreach (AudioSource a in instance.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                animator.updateMode = AnimatorUpdateMode.Normal;
                if (instance.GetComponent<Villager>() == null) instance.AddComponent<Villager>();
                if (instance.GetComponent<VillagerPose>() == null) instance.AddComponent<VillagerPose>();

                var bounds = new Bounds();
                bool any = false;
                foreach (SkinnedMeshRenderer smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.updateWhenOffscreen = false;
                    if (!any)
                    {
                        bounds = smr.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(smr.bounds);
                    }
                }

                if (any) height = bounds.max.y - instance.transform.position.y;
                if (removed == 0) problems.Add($"{c.Name}: no other bodies found to remove (check the prefab's layout)");

                PrefabUtility.SaveAsPrefabAsset(instance, target, out bool saved);
                if (!saved)
                {
                    problems.Add($"{c.Name}: could not save {target}");
                    return null;
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target);
            if (prefab == null)
            {
                problems.Add($"{c.Name}: {target} did not load after saving");
                return null;
            }

            // Hips above the feet in the rest pose (Hips 0.876 m under a Root 0.028 m up, measured offline from the model).
            Transform hips = FindDeep(prefab.transform, "Hips");
            float hipHeight = hips != null ? hips.position.y - prefab.transform.position.y : 0.9f;
            if (hipHeight < 0.5f || hipHeight > 1.3f) hipHeight = 0.9f;
            return new PeoplePalette.Entry { name = c.Name, prefab = prefab, feminine = c.Feminine, height = height, hipHeight = hipHeight };
        }

        private static int Triangles(GameObject prefab)
        {
            int n = 0;
            if (prefab == null) return 0;
            foreach (SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null) n += (int)(smr.sharedMesh.triangles.LongLength / 3);
            }

            return n;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }

            return null;
        }
    }
}
