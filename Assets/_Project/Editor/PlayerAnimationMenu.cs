using System.Collections.Generic;
using System.Text;
using MedievalZen.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using P =MedievalZen.Player.PlayerAnimator.Parameters;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// Builds the player's animation assets from the Synty ANIMATION Base Locomotion pack (never edited):
    /// <list type="bullet">
    /// <item><c>PeasantMale_Humanoid.asset</c> — a copy of the peasant's Humanoid avatar with the human Hips mapped to the
    /// real pelvis bone <c>Hips</c> (Synty's import maps it to the ground-level <c>Root</c> bone). Falls back to Synty's own
    /// avatar if the rebuilt one is not valid.</item>
    /// <item><c>PlayerLocomotion.controller</c> — base layer (a 2D freeform-directional blend on the body-space velocity
    /// <c>VelocityX</c>/<c>VelocityZ</c>: idle at the origin, 8-way walk and run rings from the pack's in-place
    /// strafe/backward clips, forward children as incline trees, sprint forward only; turn in place, fall, land) plus
    /// additive BodyLook / HeadLook / Lean layers, all in-place clips (no root motion). Clip velocities and look extents
    /// are measured from the clips and baked into the blend trees.</item>
    /// </list>
    /// Re-running overwrites both assets in place (GUIDs kept), so prefab references survive. <see cref="MeasureGrounding"/>
    /// measures where the soles are in the animated pose (used by the player rig menus).
    /// </summary>
    internal static class PlayerAnimationMenu
    {
        public const string AnimationFolder = "Assets/_Project/Animation";
        public const string ControllerPath = AnimationFolder + "/PlayerLocomotion.controller";
        public const string AvatarPath = AnimationFolder + "/PeasantMale_Humanoid.asset";
        public const string CharacterPrefabPath = "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Peasent_Male_01.prefab";

        private const string Masculine = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/";
        private const string Additive = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Neutral/Additive/";
        private const string HeadLookFbx = Additive + "Look/A_HeadLook_Additive_Neut.fbx";
        private const string BodyLookFbx = Additive + "Look/A_BodyLook_Additive_Neut.fbx";
        private const string LeanFbx = Additive + "Lean/A_Lean_Additive_Neut.fbx";
        private const string TPoseFbx = Additive + "TPose/A_TPose_Neut.fbx";

        // Measured offline from the root-motion twins (Root bone Z over one cycle, source rig, m/s):
        // Walk_F 1.50 m / 1.033 s, Run_F 1.81 m / 0.700 s, Sprint_F 4.35 m / 0.600 s. Used to sanity-check (and, if needed,
        // replace) AnimationClip.averageSpeed, whose units depend on the humanoid scale.
        private const float SourceWalkSpeed = 1.452f;
        private const float SourceRunSpeed = 2.585f;
        private const float SourceSprintSpeed = 7.243f;
        private const float SourceHipsHeight = 0.89f;

        private const float InclineRange = 25f;

        // Walk and run cycle lengths (frames 1..32 and 1..22 at 30 fps).
        private const float WalkCycle = 31f / 30f;
        private const float RunCycle = 21f / 30f;

        /// <summary>
        /// The directional clips of one gait ring (index = <see cref="LocomotionMath"/> Dir*): clip name (in-place; the
        /// root-motion twin adds <c>_RootMotion</c>) and its Root bone displacement over one cycle in the FBX (cm; FBX +X =
        /// the character's left), measured offline from the root-motion twins. The forward entry is the gait's plain
        /// forward clip, whose incline tree takes the forward slot.
        /// </summary>
        private static readonly (string Clip, float FbxX, float FbxZ)[] WalkRing =
        {
            ("A_Walk_F", 0f, 150f),                  // DirF   1.452 m/s
            ("A_Walk_FwdStrafeFL", 106f, 106f),      // DirFL  1.451 m/s
            ("A_Walk_FwdStrafeFR", -106f, 106f),     // DirFR  1.451 m/s
            ("A_Walk_FwdStrafeL", 150f, 0f),         // DirL   1.452 m/s
            ("A_Walk_FwdStrafeR", -150f, 0f),        // DirR   1.452 m/s
            ("A_Walk_BckStrafeBL", 106f, -106f),     // DirBL  1.451 m/s
            ("A_Walk_BckStrafeBR", -106f, -106f),    // DirBR  1.451 m/s
            ("A_Walk_BckStrafeB", 0f, -150f),        // DirB   1.452 m/s
        };

        private static readonly (string Clip, float FbxX, float FbxZ)[] RunRing =
        {
            ("A_Run_F", 0f, 180.966f),                    // DirF   2.585 m/s
            ("A_Run_FwdStrafeFL", 128.225f, 128.138f),    // DirFL  2.589 m/s
            ("A_Run_FwdStrafeFR", -128.173f, 128.173f),   // DirFR  2.589 m/s
            ("A_Run_FwdStrafeL", 181.100f, 0f),           // DirL   2.587 m/s
            ("A_Run_FwdStrafeR", -181.000f, 0f),          // DirR   2.586 m/s
            ("A_Run_BckStrafeBL", 128.172f, -128.172f),   // DirBL  2.589 m/s
            ("A_Run_BckStrafeBR", -128.172f, -128.172f),  // DirBR  2.589 m/s
            ("A_Run_BckStrafeB", 0f, -181.000f),          // DirB   2.586 m/s
        };

        [MenuItem("MedievalZen/Player/Build Locomotion Controller", priority = 40)]
        private static void BuildMenu()
        {
            AnimatorController controller = BuildController();
            if (controller == null) return;
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
        }

        [MenuItem("MedievalZen/Player/Build Locomotion Controller", true)]
        private static bool ValidateNotPlaying() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>
        /// Loads the controller and avatar, building whichever is missing; with <paramref name="rebuildController"/> the
        /// controller is always rebuilt in place (GUID kept) so a rig upgrade picks up the current layout. False if the
        /// clips or the character are missing.
        /// </summary>
        public static bool EnsureAssets(bool rebuildController, out AnimatorController controller, out Avatar avatar)
        {
            controller = rebuildController ? null : AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = BuildController();
            avatar = ResolveAvatar();
            return controller != null && avatar != null;
        }

        /// <summary>Synty's own Humanoid avatar of the peasant (human Hips on the ground-level <c>Root</c> bone), or null.</summary>
        public static Avatar CharacterAvatar()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            Animator animator = character != null ? character.GetComponent<Animator>() : null;
            return animator != null ? animator.avatar : null;
        }

        /// <summary>Which bone an avatar maps the human Hips to (for logs), or "?".</summary>
        public static string HipsBoneOf(Avatar avatar)
        {
            if (avatar == null || !avatar.isHuman) return "?";
            foreach (HumanBone bone in avatar.humanDescription.human)
            {
                if (bone.humanName == "Hips") return bone.boneName;
            }

            return "?";
        }

        /// <summary>Points <paramref name="animator"/> at the locomotion controller: no root motion, always animate (the first-person shadow walks too).</summary>
        public static void ConfigureAnimator(Animator animator, RuntimeAnimatorController controller, Avatar avatar)
        {
            animator.runtimeAnimatorController = controller;
            if (avatar != null) animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            EditorUtility.SetDirty(animator);
        }

        // ------------------------------------------------------------------ avatar

        /// <summary>The corrected avatar if it exists (or can be built), else the character's own Synty avatar.</summary>
        private static Avatar ResolveAvatar()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
            if (existing != null && existing.isValid && existing.isHuman) return existing;
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            Avatar avatar = character != null ? BuildAvatar(character) : null;
            if (avatar != null && AssetDatabase.GetAssetPath(avatar) != AvatarPath)
            {
                Debug.LogWarning($"The player uses Synty's own avatar '{avatar.name}' (human Hips → '{HipsBoneOf(avatar)}'), not {AvatarPath}: " +
                                 "the pelvis does not follow the clips' hip motion. See the messages above for why the rebuild was skipped.", avatar);
            }

            return avatar;
        }

        /// <summary>
        /// Rebuilds the character's Humanoid avatar with the human Hips on the <c>Hips</c> bone and saves it as
        /// <see cref="AvatarPath"/>. Returns the character's own avatar when that mapping is already right or the rebuild fails.
        /// </summary>
        private static Avatar BuildAvatar(GameObject character)
        {
            var sourceAnimator = character.GetComponent<Animator>();
            Avatar source = sourceAnimator != null ? sourceAnimator.avatar : null;
            if (source == null || !source.isHuman)
            {
                Debug.LogError($"{CharacterPrefabPath} has no Humanoid avatar; the player cannot be animated with Humanoid clips.");
                return null;
            }

            HumanDescription description = source.humanDescription;
            HumanBone[] human = description.human;
            int hipsIndex = System.Array.FindIndex(human, h => h.humanName == "Hips");
            if (hipsIndex < 0 || human[hipsIndex].boneName == "Hips") return source;

            GameObject instance = Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            // The importer's skeleton entries carry their parent's name: the root must be called what the model's
            // root was at import ("Generic_Characters(Clone)"), not what the prefab is called, or AvatarBuilder
            // rejects the hierarchy ("Parent for 'Root' differs from one found in HumanDescription").
            SkeletonBone[] sourceSkeleton = description.skeleton;
            instance.name = sourceSkeleton != null && sourceSkeleton.Length > 0 && !string.IsNullOrEmpty(sourceSkeleton[0].name)
                ? sourceSkeleton[0].name
                : character.name;
            try
            {
                if (FindDeep(instance.transform, "Hips") == null)
                {
                    Debug.LogWarning($"{CharacterPrefabPath} has no 'Hips' bone; keeping Synty's avatar (Hips → '{human[hipsIndex].boneName}').");
                    return source;
                }

                string original = human[hipsIndex].boneName;
                human[hipsIndex].boneName = "Hips";
                description.human = human;
                description.skeleton = BuildSkeleton(instance.transform, description.skeleton);

                Avatar avatar = AvatarBuilder.BuildHumanAvatar(instance, description);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    Debug.LogWarning($"Rebuilding the peasant avatar with Hips on the pelvis failed; using Synty's avatar (Hips → '{original}').");
                    if (avatar != null) Object.DestroyImmediate(avatar);
                    return source;
                }

                avatar.name = "PeasantMale_Humanoid";
                WorldPaletteMenu.EnsureFolder(AnimationFolder);
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
                Debug.Log($"Saved {AvatarPath}: Synty's Humanoid avatar with the human Hips moved from '{original}' to the pelvis bone 'Hips'.", avatar);
                return avatar;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>One skeleton entry per transform of the instance; rest poses from the importer's skeleton where names match.</summary>
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
                SkeletonBone bone;
                if (t == root || !byName.TryGetValue(t.name, out bone))
                {
                    bone = new SkeletonBone { position = t.localPosition, rotation = t.localRotation, scale = t.localScale };
                }

                bone.name = t.name;
                bones.Add(bone);
            }

            return bones.ToArray();
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }

            return null;
        }

        // ------------------------------------------------------------------ controller

        /// <summary>Builds (or rebuilds in place) <see cref="ControllerPath"/>. Returns null (with an error) if clips are missing.</summary>
        public static AnimatorController BuildController()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            if (character == null)
            {
                Debug.LogError($"Character prefab not found at {CharacterPrefabPath}.");
                return null;
            }

            var missing = new List<string>();
            AnimationClip idle = Clip(Masculine + "Idle/A_Idle_Standing_Masc.fbx", missing);
            AnimationClip walk = Clip(Masculine + "Locomotion/Walk/A_Walk_F_Masc.fbx", missing);
            AnimationClip walkUp = Clip(Masculine + "Locomotion/Walk/A_Walk_Up25F_Masc.fbx", missing);
            AnimationClip walkDown = Clip(Masculine + "Locomotion/Walk/A_Walk_Down25F_Masc.fbx", missing);
            AnimationClip run = Clip(Masculine + "Locomotion/Run/A_Run_F_Masc.fbx", missing);
            AnimationClip runUp = Clip(Masculine + "Locomotion/Run/A_Run_Up25F_Masc.fbx", missing);
            AnimationClip runDown = Clip(Masculine + "Locomotion/Run/A_Run_Down25F_Masc.fbx", missing);
            AnimationClip sprint = Clip(Masculine + "Locomotion/Sprint/A_Sprint_F_Masc.fbx", missing);
            AnimationClip sprintUp = Clip(Masculine + "Locomotion/Sprint/A_Sprint_Up25F_Masc.fbx", missing);
            AnimationClip sprintDown = Clip(Masculine + "Locomotion/Sprint/A_Sprint_Down25F_Masc.fbx", missing);
            AnimationClip turn90L = Clip(Masculine + "Locomotion/Turn/A_Turn_Standing_90L_Masc.fbx", missing);
            AnimationClip turn90R = Clip(Masculine + "Locomotion/Turn/A_Turn_Standing_90R_Masc.fbx", missing);
            AnimationClip turn180L = Clip(Masculine + "Locomotion/Turn/A_Turn_Standing_180L_Masc.fbx", missing);
            AnimationClip turn180R = Clip(Masculine + "Locomotion/Turn/A_Turn_Standing_180R_Masc.fbx", missing);
            AnimationClip fall = Clip(Masculine + "InAir/A_InAir_FallShort_Masc.fbx", missing);
            AnimationClip land = Clip(Masculine + "InAir/A_Land_IdleSoft_Masc.fbx", missing);
            AnimationClip headL = Clip(HeadLookFbx, "A_HeadLook_L_Neut", missing);
            AnimationClip headR = Clip(HeadLookFbx, "A_HeadLook_R_Neut", missing);
            AnimationClip headU = Clip(HeadLookFbx, "A_HeadLook_U_Neut", missing);
            AnimationClip headD = Clip(HeadLookFbx, "A_HeadLook_D_Neut", missing);
            AnimationClip bodyL = Clip(BodyLookFbx, "A_BodyLook_L_Neut", missing);
            AnimationClip bodyR = Clip(BodyLookFbx, "A_BodyLook_R_Neut", missing);
            AnimationClip bodyU = Clip(BodyLookFbx, "A_BodyLook_U_Neut", missing);
            AnimationClip bodyD = Clip(BodyLookFbx, "A_BodyLook_D_Neut", missing);
            AnimationClip leanL = Clip(LeanFbx, "A_Lean_L_Neut", missing);
            AnimationClip leanR = Clip(LeanFbx, "A_Lean_R_Neut", missing);
            AnimationClip tpose = Clip(TPoseFbx, "A_TPose_Neut", missing);
            AnimationClip[] walkRing = RingClips(Masculine + "Locomotion/Walk/", WalkRing, missing);
            AnimationClip[] runRing = RingClips(Masculine + "Locomotion/Run/", RunRing, missing);
            if (missing.Count > 0)
            {
                Debug.LogError("Build Locomotion Controller: missing clips (is the Synty ANIMATION Base Locomotion pack imported?):\n  " + string.Join("\n  ", missing));
                return null;
            }

            Avatar avatar = ResolveAvatar();
            if (avatar == null) return null;

            var log = new StringBuilder("Built " + ControllerPath + " — measurements:\n");

            // Measure on a hidden instance of the character: natural velocities, look extents, lean direction.
            float walkSpeed, runSpeed, sprintSpeed;
            Vector2[] walkVelocities, runVelocities;
            Vector2[] headPositions, bodyPositions;
            float leanRSign;
            GameObject instance = Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.Rebind();

                Transform hips = FindDeep(instance.transform, "Hips");
                float hipsHeight = hips != null ? hips.position.y - instance.transform.position.y : SourceHipsHeight;
                float heightRatio = Mathf.Clamp(hipsHeight / SourceHipsHeight, 0.7f, 1.4f);
                float humanScale = animator.humanScale;
                log.AppendLine($"  target hips height {hipsHeight:0.000} m (ratio {heightRatio:0.000}), Animator.humanScale {humanScale:0.000}, avatar '{avatar.name}'");

                walkVelocities = RingVelocities(Masculine + "Locomotion/Walk/", WalkRing, WalkCycle, heightRatio, humanScale, log);
                runVelocities = RingVelocities(Masculine + "Locomotion/Run/", RunRing, RunCycle, heightRatio, humanScale, log);
                walkSpeed = walkVelocities[LocomotionMath.DirF].y;
                runSpeed = runVelocities[LocomotionMath.DirF].y;
                sprintSpeed = NaturalVelocity(Masculine + "Locomotion/Sprint/A_Sprint_F_RootMotion_Masc.fbx", new Vector2(0f, SourceSprintSpeed), heightRatio, humanScale, log).y;
                if (Mathf.Abs(walkSpeed - SourceWalkSpeed * heightRatio) > 0.3f * SourceWalkSpeed || Mathf.Abs(runSpeed - SourceRunSpeed * heightRatio) > 0.3f * SourceRunSpeed)
                {
                    log.AppendLine("  WARNING: forward walk/run speeds disagree with the offline measurements; check the blend tree positions.");
                }

                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                headPositions = LookExtents(instance, head, new[] { headL, headR, headU, headD }, 50f, 35f, log);
                bodyPositions = LookExtents(instance, head, new[] { bodyL, bodyR, bodyU, bodyD }, 30f, 20f, log);
                leanRSign = LeanDirection(instance, head, leanR, log);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AnimatorController controller = CreateOrClearController();
            AddParameters(controller);

            // ---- Base layer
            var baseMachine = NewStateMachine(controller, "Base Layer");
            controller.AddLayer(new AnimatorControllerLayer { name = "Base Layer", stateMachine = baseMachine, defaultWeight = 1f });

            // Forward axis exactly as the former 1D speed tree (idle, slow/natural/brisk walk, run, sprint at the
            // thresholds of LocomotionMath.SpeedThresholds); every walk and run child is now a ring of 8 directions, each
            // clip placed at its measured natural velocity × playback rate so sidesteps and back steps never slide.
            float[] thresholds = LocomotionMath.SpeedThresholds(walkSpeed, runSpeed, sprintSpeed);
            float[] timeScales = LocomotionMath.SpeedTimeScales();
            BlendTree locomotionTree = NewTree(controller, "Locomotion", BlendTreeType.FreeformDirectional2D, P.VelocityX, P.VelocityZ);
            var locomotionChildren = new List<ChildMotion>
            {
                new ChildMotion { motion = idle, position = Vector2.zero, timeScale = 1f },
            };
            var ringNames = new Dictionary<int, string>
            {
                { LocomotionMath.SlowWalkChild, "Walk_Slow" }, { LocomotionMath.WalkChild, "Walk" },
                { LocomotionMath.BriskWalkChild, "Walk_Brisk" }, { LocomotionMath.RunChild, "Run" },
            };
            for (int speedChild = 0; speedChild < LocomotionMath.SpeedChildCount; speedChild++)
            {
                if (!LocomotionMath.IsDirectionalRing(speedChild)) continue;
                bool isRun = speedChild == LocomotionMath.RunChild;
                AnimationClip[] clips = isRun ? runRing : walkRing;
                Vector2[] velocities = isRun ? runVelocities : walkVelocities;
                float rate = timeScales[speedChild];
                for (int direction = 0; direction < LocomotionMath.DirectionCount; direction++)
                {
                    Motion motion = direction == LocomotionMath.DirF
                        ? isRun
                            ? InclineTree(controller, "Run_Incline", runDown, run, runUp)
                            : InclineTree(controller, ringNames[speedChild] + "_Incline", walkDown, walk, walkUp)
                        : clips[direction];
                    Vector2 position = LocomotionMath.ChildPosition(velocities[direction], rate);
                    locomotionChildren.Add(new ChildMotion { motion = motion, position = position, timeScale = rate });
                }
            }

            locomotionChildren.Add(new ChildMotion
            {
                motion = InclineTree(controller, "Sprint_Incline", sprintDown, sprint, sprintUp),
                position = new Vector2(0f, thresholds[LocomotionMath.SprintChild]),
                timeScale = timeScales[LocomotionMath.SprintChild],
            });
            locomotionTree.children = locomotionChildren.ToArray();
            log.AppendLine($"  forward axis (m/s): {string.Join(", ", System.Array.ConvertAll(thresholds, t => t.ToString("0.00")))}; walk playback x{string.Join("/x", timeScales[1].ToString("0.00"), timeScales[2].ToString("0.00"), timeScales[3].ToString("0.00"))}");
            log.AppendLine($"  locomotion tree: {locomotionChildren.Count} children (idle, 4 rings × {LocomotionMath.DirectionCount} directions, sprint forward)");

            float rate90 = 90f / Mathf.Max(0.1f, 0.5f * (turn90L.length + turn90R.length));
            float rate180 = Mathf.Max(rate90 + 5f, 180f / Mathf.Max(0.1f, 0.5f * (turn180L.length + turn180R.length)));
            BlendTree turnTree = NewTree(controller, "TurnInPlace", BlendTreeType.Simple1D, P.TurnInPlace);
            turnTree.children = new[]
            {
                Child1D(turn180L, -rate180, 1f), Child1D(turn90L, -rate90, 1f), Child1D(idle, 0f, 1f),
                Child1D(turn90R, rate90, 1f), Child1D(turn180R, rate180, 1f),
            };
            log.AppendLine($"  turn in place: 90° clips at ±{rate90:0} °/s, 180° clips at ±{rate180:0} °/s");

            AnimatorState locomotion = baseMachine.AddState("Locomotion", new Vector3(300f, 120f, 0f));
            locomotion.motion = locomotionTree;
            AnimatorState turning = baseMachine.AddState("TurnInPlace", new Vector3(300f, 260f, 0f));
            turning.motion = turnTree;
            AnimatorState falling = baseMachine.AddState("Fall", new Vector3(600f, 120f, 0f));
            falling.motion = fall;
            AnimatorState landing = baseMachine.AddState("Land", new Vector3(600f, 260f, 0f));
            landing.motion = land;
            baseMachine.defaultState = locomotion;

            AnimatorStateTransition turnRight = Transition(locomotion, turning, 0.2f);
            turnRight.AddCondition(AnimatorConditionMode.Less, 0.15f, P.Speed);
            turnRight.AddCondition(AnimatorConditionMode.Greater, 30f, P.TurnInPlace);
            AnimatorStateTransition turnLeft = Transition(locomotion, turning, 0.2f);
            turnLeft.AddCondition(AnimatorConditionMode.Less, 0.15f, P.Speed);
            turnLeft.AddCondition(AnimatorConditionMode.Less, -30f, P.TurnInPlace);
            Transition(turning, locomotion, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.35f, P.Speed);
            AnimatorStateTransition turnDone = Transition(turning, locomotion, 0.3f);
            turnDone.AddCondition(AnimatorConditionMode.Less, 12f, P.TurnInPlace);
            turnDone.AddCondition(AnimatorConditionMode.Greater, -12f, P.TurnInPlace);

            Transition(locomotion, falling, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, P.Grounded);
            Transition(turning, falling, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, P.Grounded);
            AnimatorStateTransition fallToWalk = Transition(falling, locomotion, 0.2f);
            fallToWalk.AddCondition(AnimatorConditionMode.If, 0f, P.Grounded);
            fallToWalk.AddCondition(AnimatorConditionMode.Greater, 0.5f, P.Speed);
            Transition(falling, landing, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, P.Grounded);
            Transition(landing, falling, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, P.Grounded);
            Transition(landing, locomotion, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.5f, P.Speed);
            AnimatorStateTransition landDone = Transition(landing, locomotion, 0.25f);
            landDone.hasExitTime = true;
            landDone.exitTime = 0.75f;

            // ---- Additive look and lean layers (as in Synty's master controller: 2D freeform directional around a
            // T-pose "zero" child; here positioned in degrees measured from the clips, so parameters are plain angles).
            AddAdditiveLayer(controller, "BodyLook", DirectionalTree(controller, "BodyLook", P.BodyYaw, P.BodyPitch, tpose,
                new[] { bodyL, bodyR, bodyU, bodyD }, bodyPositions));
            AddAdditiveLayer(controller, "HeadLook", DirectionalTree(controller, "HeadLook", P.HeadYaw, P.HeadPitch, tpose,
                new[] { headL, headR, headU, headD }, headPositions));

            BlendTree leanTree = NewTree(controller, "Lean", BlendTreeType.Simple1D, P.Lean);
            leanTree.children = leanRSign >= 0f
                ? new[] { Child1D(leanL, -1f, 1f), Child1D(tpose, 0f, 1f), Child1D(leanR, 1f, 1f) }
                : new[] { Child1D(leanR, -1f, 1f), Child1D(tpose, 0f, 1f), Child1D(leanL, 1f, 1f) };
            AddAdditiveLayer(controller, "Lean", leanTree);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString(), controller);
            return controller;
        }

        private static AnimatorController CreateOrClearController()
        {
            WorldPaletteMenu.EnsureFolder(AnimationFolder);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = new AnimatorController { name = "PlayerLocomotion" };
                AssetDatabase.CreateAsset(controller, ControllerPath);
                return controller;
            }

            // Rebuild in place so the GUID (and every reference to the controller) survives.
            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            {
                if (sub != null && sub != controller) Object.DestroyImmediate(sub, true);
            }

            return controller;
        }

        private static void AddParameters(AnimatorController controller)
        {
            controller.AddParameter(P.Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.VelocityX, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.VelocityZ, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.Incline, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = P.Grounded, type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddParameter(P.TurnInPlace, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.LookYaw, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.LookPitch, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.BodyYaw, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.BodyPitch, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.HeadYaw, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.HeadPitch, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.Lean, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.MapOpen, AnimatorControllerParameterType.Bool);
        }

        private static AnimatorStateMachine NewStateMachine(AnimatorController controller, string name)
        {
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            return machine;
        }

        private static void AddAdditiveLayer(AnimatorController controller, string name, BlendTree tree)
        {
            AnimatorStateMachine machine = NewStateMachine(controller, name);
            AnimatorState state = machine.AddState(name, new Vector3(300f, 120f, 0f));
            state.motion = tree;
            machine.defaultState = state;
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = name,
                stateMachine = machine,
                blendingMode = AnimatorLayerBlendingMode.Additive,
                defaultWeight = 1f,
            });
        }

        private static BlendTree NewTree(AnimatorController controller, string name, BlendTreeType type, string parameter, string parameterY = null)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = type,
                blendParameter = parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            if (parameterY != null) tree.blendParameterY = parameterY;
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        /// <summary>Down25 / flat / Up25 blended by Incline (degrees, + uphill).</summary>
        private static BlendTree InclineTree(AnimatorController controller, string name, AnimationClip down, AnimationClip flat, AnimationClip up)
        {
            BlendTree tree = NewTree(controller, name, BlendTreeType.Simple1D, P.Incline);
            tree.children = new[] { Child1D(down, -InclineRange, 1f), Child1D(flat, 0f, 1f), Child1D(up, InclineRange, 1f) };
            return tree;
        }

        private static BlendTree DirectionalTree(AnimatorController controller, string name, string x, string y, AnimationClip zero,
            AnimationClip[] clips, Vector2[] positions)
        {
            BlendTree tree = NewTree(controller, name, BlendTreeType.FreeformDirectional2D, x, y);
            var children = new ChildMotion[clips.Length + 1];
            children[0] = new ChildMotion { motion = zero, position = Vector2.zero, timeScale = 1f };
            for (int i = 0; i < clips.Length; i++)
            {
                children[i + 1] = new ChildMotion { motion = clips[i], position = positions[i], timeScale = 1f };
            }

            tree.children = children;
            return tree;
        }

        private static ChildMotion Child1D(Motion motion, float threshold, float timeScale)
        {
            return new ChildMotion { motion = motion, threshold = threshold, timeScale = timeScale };
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float seconds)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = seconds;
            transition.offset = 0f;
            transition.interruptionSource = TransitionInterruptionSource.None;
            return transition;
        }

        // ------------------------------------------------------------------ clips and measurements

        private static AnimationClip Clip(string fbxPath, List<string> missing)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(fbxPath);
            return Clip(fbxPath, name, missing);
        }

        private static AnimationClip Clip(string fbxPath, string clipName, List<string> missing)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip && clip.name == clipName) return clip;
            }

            missing.Add($"{fbxPath} :: {clipName}");
            return null;
        }

        /// <summary>The in-place clips of a gait ring (index = direction); the forward slot holds the plain forward clip.</summary>
        private static AnimationClip[] RingClips(string folder, (string Clip, float FbxX, float FbxZ)[] ring, List<string> missing)
        {
            var clips = new AnimationClip[ring.Length];
            for (int i = 0; i < ring.Length; i++) clips[i] = Clip(folder + ring[i].Clip + "_Masc.fbx", missing);
            return clips;
        }

        /// <summary>Natural body-space velocities (m/s, x = right, y = forward) of a gait ring, measured from the root-motion twins.</summary>
        private static Vector2[] RingVelocities(string folder, (string Clip, float FbxX, float FbxZ)[] ring, float cycleSeconds,
            float heightRatio, float humanScale, StringBuilder log)
        {
            var velocities = new Vector2[ring.Length];
            for (int i = 0; i < ring.Length; i++)
            {
                // Offline: FBX Root displacement in cm → m.
                Vector2 source = LocomotionMath.NaturalVelocityFromFbx(ring[i].FbxX * 0.01f, ring[i].FbxZ * 0.01f, cycleSeconds);
                velocities[i] = NaturalVelocity(folder + ring[i].Clip + "_RootMotion_Masc.fbx", source, heightRatio, humanScale, log);
            }

            return velocities;
        }

        /// <summary>
        /// Natural body-space ground velocity (m/s on the target character; x = right, y = forward) of a clip, from its
        /// root-motion twin's <see cref="Motion.averageSpeed"/> (raw or × humanScale, whichever matches the offline
        /// measurement <paramref name="sourceVelocity"/>), falling back to the offline measurement scaled by hips height.
        /// </summary>
        private static Vector2 NaturalVelocity(string rootMotionFbx, Vector2 sourceVelocity, float heightRatio, float humanScale, StringBuilder log)
        {
            Vector2 expected = sourceVelocity * heightRatio;
            var none = new List<string>();
            AnimationClip clip = Clip(rootMotionFbx, none);
            string name = System.IO.Path.GetFileNameWithoutExtension(rootMotionFbx);
            if (clip == null)
            {
                log.AppendLine($"  {name}: clip not found, using offline {Format(expected)}");
                return expected;
            }

            Vector3 average = clip.averageSpeed;
            var raw = new Vector2(average.x, average.z);
            Vector2 scaled = raw * (humanScale > 0.01f ? humanScale : 1f);
            Vector2 best = (raw - expected).sqrMagnitude <= (scaled - expected).sqrMagnitude ? raw : scaled;
            bool plausible = best.magnitude > 0.05f && (best - expected).magnitude <= 0.3f * expected.magnitude;
            Vector2 chosen = plausible ? best : expected;
            string note = plausible ? string.Empty
                : Vector2.Dot(best, expected) < 0f || Vector2.Dot(new Vector2(-best.x, best.y), expected) > Vector2.Dot(best, expected) + 0.1f
                    ? " (averageSpeed points another way — mirrored axis? — used offline value)"
                    : " (averageSpeed implausible, used offline value)";
            log.AppendLine($"  {name}: averageSpeed {Format(raw)} (×humanScale {Format(scaled)}), offline {Format(expected)} → {Format(chosen)} = {chosen.magnitude:0.000} m/s{note}");
            return chosen;
        }

        private static string Format(Vector2 v) => $"({v.x:0.000}, {v.y:0.000})";

        // ------------------------------------------------------------------ grounding

        /// <summary>Where the character's soles are in the animated pose (metres, relative to the character's origin; + = up).</summary>
        public struct GroundingResult
        {
            /// <summary>How the pose was produced ("animator graph" = the real controller, or "clip sampling").</summary>
            public string Method;

            /// <summary>Lowest vertex in the un-animated (prefab) pose.</summary>
            public float BindPoseSole;

            /// <summary>Lowest vertex in the idle pose, averaged over the idle cycle: the reference the rig is grounded on.</summary>
            public float IdleSole;

            /// <summary>Range of the idle's lowest vertex over its cycle.</summary>
            public float IdleSoleMin, IdleSoleMax;

            /// <summary>Lowest vertex over a walk cycle at the motor's walk speed (the planted foot at contact).</summary>
            public float WalkLowest;

            /// <summary>Highest per-frame lowest vertex over the walk cycle (how far the lower foot ever lifts; ~0 = always a foot down).</summary>
            public float WalkHighestLowFoot;

            /// <summary>Animator.humanScale with this avatar.</summary>
            public float HumanScale;

            /// <summary>Height of the pelvis bone ('Hips') above the origin in the idle pose.</summary>
            public float PelvisHeight;
        }

        /// <summary>
        /// Measures where the soles are once animated: instantiates the peasant hidden, gives it <paramref name="avatar"/>,
        /// drives <paramref name="controller"/> through a manual <see cref="PlayableGraph"/> exactly as at runtime (no root
        /// motion), bakes every visible skinned mesh and takes the lowest vertex relative to the character's origin — over
        /// the idle cycle (the reference) and over a walk cycle at <paramref name="walkSpeed"/> (a check that the planted
        /// foot touches). Falls back to sampling the idle/walk clips directly if the graph does not animate. Logs every number.
        /// </summary>
        public static bool MeasureGrounding(RuntimeAnimatorController controller, Avatar avatar, float walkSpeed, StringBuilder log, out GroundingResult result)
        {
            result = default;
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            if (character == null || controller == null || avatar == null)
            {
                log.AppendLine("  grounding: character, controller or avatar missing — not measured");
                return false;
            }

            var missing = new List<string>();
            AnimationClip idleClip = Clip(Masculine + "Idle/A_Idle_Standing_Masc.fbx", missing);
            AnimationClip walkClip = Clip(Masculine + "Locomotion/Walk/A_Walk_F_Masc.fbx", missing);

            GameObject instance = Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var scratch = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            PlayableGraph graph = default;
            try
            {
                Transform root = instance.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.localScale = Vector3.one;
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();

                List<Renderer> renderers = VisibleRenderers(instance);
                Transform pelvis = FindDeep(root, "Hips");
                Transform leftFoot = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
                result.HumanScale = animator.humanScale;
                result.BindPoseSole = LowestPoint(root, renderers, scratch, out string bakeNote);
                log.AppendLine($"  avatar '{avatar.name}' (human Hips → '{HipsBoneOf(avatar)}'): Animator.humanScale {result.HumanScale:0.000}; " +
                               $"prefab pose: lowest vertex {result.BindPoseSole:0.000}, pelvis 'Hips' at {(pelvis != null ? pelvis.position.y : float.NaN):0.000}{bakeNote}");

                // 1) The real controller through a manual graph (the runtime path: same controller, same avatar, no root motion).
                graph = PlayableGraph.Create("MedievalZen Grounding");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(graph, controller);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                output.SetSourcePlayable(playable);
                playable.SetBool(P.Grounded, true);
                graph.Play();
                graph.Evaluate(0f);
                for (int i = 0; i < 15; i++) graph.Evaluate(1f / 30f);   // settle in the idle

                float idleLength = idleClip != null ? idleClip.length : 1.8f;
                SampleRange(() => graph.Evaluate(idleLength / 36f), 36, root, renderers, scratch, out result.IdleSoleMin, out result.IdleSoleMax, out float idleAverage, null, out _);
                result.IdleSole = idleAverage;
                result.PelvisHeight = pelvis != null ? root.InverseTransformPoint(pelvis.position).y : float.NaN;

                playable.SetFloat(P.Speed, walkSpeed);
                playable.SetFloat(P.VelocityX, 0f);
                playable.SetFloat(P.VelocityZ, walkSpeed);
                playable.SetFloat(P.Incline, 0f);
                for (int i = 0; i < 20; i++) graph.Evaluate(1f / 30f);
                SampleRange(() => graph.Evaluate(1.2f / 36f), 36, root, renderers, scratch, out result.WalkLowest, out result.WalkHighestLowFoot, out _, leftFoot, out float footTravel);
                result.Method = "animator graph";

                if (footTravel < 0.05f && idleClip != null && walkClip != null)
                {
                    // The graph did not animate in edit mode: sample the clips directly instead (same avatar).
                    log.AppendLine($"  grounding: the animator graph did not move the feet in edit mode (left foot travel {footTravel:0.000} m); falling back to clip sampling");
                    graph.Destroy();
                    graph = default;
                    animator.Rebind();
                    float t = 0f;
                    SampleRange(() => { idleClip.SampleAnimation(instance, t); t += idleClip.length / 36f; root.SetPositionAndRotation(Vector3.zero, Quaternion.identity); },
                        36, root, renderers, scratch, out result.IdleSoleMin, out result.IdleSoleMax, out idleAverage, null, out _);
                    result.IdleSole = idleAverage;
                    result.PelvisHeight = pelvis != null ? root.InverseTransformPoint(pelvis.position).y : float.NaN;
                    t = 0f;
                    SampleRange(() => { walkClip.SampleAnimation(instance, t); t += walkClip.length / 36f; root.SetPositionAndRotation(Vector3.zero, Quaternion.identity); },
                        36, root, renderers, scratch, out result.WalkLowest, out result.WalkHighestLowFoot, out _, leftFoot, out footTravel);
                    result.Method = "clip sampling";
                }

                log.AppendLine($"  [{result.Method}] idle: lowest vertex {result.IdleSole:0.000} (cycle range {result.IdleSoleMin:0.000} … {result.IdleSoleMax:0.000}), pelvis at {result.PelvisHeight:0.000}");
                log.AppendLine($"  [{result.Method}] walk at {walkSpeed:0.00} m/s: lowest vertex {result.WalkLowest:0.000}, lower foot lifts to at most {result.WalkHighestLowFoot:0.000} (left foot travelled {footTravel:0.00} m)");
                bool plausible = footTravel >= 0.05f && Mathf.Abs(result.IdleSole) < 0.35f;
                if (!plausible) log.AppendLine("  grounding: measurement implausible (no foot motion or soles more than 35 cm off the origin) — not applied");
                return plausible;
            }
            catch (System.Exception exception)
            {
                log.AppendLine($"  grounding: measurement failed ({exception.GetType().Name}: {exception.Message})");
                return false;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(scratch);
            }
        }

        /// <summary>Calls <paramref name="step"/> <paramref name="count"/> times, recording the lowest vertex after each: min, max and mean of those.</summary>
        private static void SampleRange(System.Action step, int count, Transform root, List<Renderer> renderers, Mesh scratch,
            out float min, out float max, out float mean, Transform foot, out float footTravel)
        {
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;
            float sum = 0f;
            Vector3 footMin = Vector3.positiveInfinity, footMax = Vector3.negativeInfinity;
            for (int i = 0; i < count; i++)
            {
                step();
                float lowest = LowestPoint(root, renderers, scratch, out _);
                min = Mathf.Min(min, lowest);
                max = Mathf.Max(max, lowest);
                sum += lowest;
                if (foot != null)
                {
                    Vector3 p = root.InverseTransformPoint(foot.position);
                    footMin = Vector3.Min(footMin, p);
                    footMax = Vector3.Max(footMax, p);
                }
            }

            mean = sum / Mathf.Max(1, count);
            footTravel = foot != null ? (footMax - footMin).magnitude : 0f;
        }

        private static List<Renderer> VisibleRenderers(GameObject instance)
        {
            var renderers = new List<Renderer>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(false))
            {
                if (renderer.enabled && (renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) renderers.Add(renderer);
            }

            return renderers;
        }

        /// <summary>
        /// Lowest vertex (y, relative to <paramref name="root"/>) of the renderers in their current pose: skinned meshes
        /// baked, static meshes as they are. If a mesh cannot be read, falls back to the foot bones (noted in <paramref name="note"/>).
        /// </summary>
        private static float LowestPoint(Transform root, List<Renderer> renderers, Mesh scratch, out string note)
        {
            note = string.Empty;
            float lowest = float.PositiveInfinity;
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                Vector3[] vertices = null;
                Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                try
                {
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.BakeMesh(scratch, true);   // vertices include the transform's scale, not its rotation/position
                        vertices = scratch.vertices;
                        toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                    }
                    else if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    {
                        vertices = filter.sharedMesh.vertices;
                    }
                }
                catch (System.Exception exception)
                {
                    note = $" (could not read '{renderer.name}': {exception.Message})";
                }

                if (vertices == null) continue;
                Matrix4x4 matrix = toRoot * toWorld;
                foreach (Vector3 vertex in vertices) lowest = Mathf.Min(lowest, matrix.MultiplyPoint3x4(vertex).y);
            }

            if (float.IsPositiveInfinity(lowest))
            {
                // No readable mesh: estimate from the lowest foot bone (sits a few cm above the sole — noted in the log).
                foreach (string bone in new[] { "Toes_L", "Toes_R", "Ball_L", "Ball_R" })
                {
                    Transform t = FindDeep(root, bone);
                    if (t != null) lowest = Mathf.Min(lowest, root.InverseTransformPoint(t.position).y);
                }

                note += " (NO READABLE MESH: foot-bone estimate, sole is a few cm lower)";
            }

            return lowest;
        }

        /// <summary>
        /// Blend-tree positions (yaw, pitch in degrees; + = right / up) of the L, R, U, D look clips: where each clip turns
        /// the head, measured as its last frame relative to its first (its additive reference). If the four measurements
        /// do not form two opposite pairs (e.g. sampling did nothing), canonical guesses ±<paramref name="yawGuess"/> /
        /// ±<paramref name="pitchGuess"/> are used instead.
        /// </summary>
        private static Vector2[] LookExtents(GameObject instance, Transform head, AnimationClip[] clips, float yawGuess, float pitchGuess, StringBuilder log)
        {
            var guesses = new[] { new Vector2(-yawGuess, 0f), new Vector2(yawGuess, 0f), new Vector2(0f, pitchGuess), new Vector2(0f, -pitchGuess) };
            var measured = new Vector2[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                measured[i] = head != null ? ViewChange(instance, head, clips[i]) : Vector2.zero;
            }

            bool usable = true;
            foreach (Vector2 m in measured) usable &= m.magnitude >= 5f && m.magnitude <= 120f;
            usable &= Vector2.Dot(measured[0].normalized, measured[1].normalized) < -0.5f;   // L and R opposite
            usable &= Vector2.Dot(measured[2].normalized, measured[3].normalized) < -0.5f;   // U and D opposite
            usable &= Mathf.Abs(Vector2.Dot(measured[0].normalized, measured[2].normalized)) < 0.7f;   // yaw pair ⟂ pitch pair

            Vector2[] result = usable ? measured : guesses;
            for (int i = 0; i < clips.Length; i++)
            {
                string note = usable
                    ? (Vector2.Dot(measured[i].normalized, guesses[i].normalized) > 0.5f ? "measured" : "measured — direction disagrees with the clip name, check in the editor")
                    : $"GUESS (measured {measured[i].x:0}°/{measured[i].y:0}° was unusable)";
                log.AppendLine($"  {clips[i].name}: yaw {result[i].x:0.0}°, pitch {result[i].y:0.0}° — {note}");
            }

            return result;
        }

        /// <summary>Yaw/pitch (degrees, + = right / up) by which a clip's last frame turns the head relative to its first.</summary>
        private static Vector2 ViewChange(GameObject instance, Transform head, AnimationClip clip)
        {
            Transform root = instance.transform;
            clip.SampleAnimation(instance, 0f);
            Quaternion start = Quaternion.Inverse(root.rotation) * head.rotation;
            clip.SampleAnimation(instance, clip.length);
            Quaternion end = Quaternion.Inverse(root.rotation) * head.rotation;
            Vector3 view = end * Quaternion.Inverse(start) * Vector3.forward;
            float yaw = Mathf.Atan2(view.x, view.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(view.y, -1f, 1f)) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
        }

        /// <summary>+1 if the "R" lean clip moves the head to the character's right (+X), -1 if to the left.</summary>
        private static float LeanDirection(GameObject instance, Transform head, AnimationClip leanR, StringBuilder log)
        {
            if (head == null) return 1f;
            Transform root = instance.transform;
            leanR.SampleAnimation(instance, 0f);
            Vector3 start = root.InverseTransformPoint(head.position);
            leanR.SampleAnimation(instance, leanR.length);
            Vector3 end = root.InverseTransformPoint(head.position);
            float dx = end.x - start.x;
            float sign = Mathf.Abs(dx) < 0.01f ? 1f : Mathf.Sign(dx);
            log.AppendLine($"  {leanR.name}: head moves {dx * 100f:0.0} cm sideways → Lean +1 = {(sign > 0f ? leanR.name : "A_Lean_L_Neut")}");
            return sign;
        }
    }
}
