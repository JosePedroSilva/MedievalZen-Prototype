using System.Text;
using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MedievalZen.EditorTools
{
    /// <summary>Menu items that build the player rig prefab and the player test scene.</summary>
    internal static class PlayerSetupMenu
    {
        /// <summary>Where the player rig prefab is saved.</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/PlayerRig.prefab";

        /// <summary>Where the player test scene is saved.</summary>
        public const string ScenePath = "Assets/_Project/Scenes/PlayerTest.unity";

        private const string MaterialsFolder = "Assets/_Project/Materials/PlayerTest";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string CharacterPrefabPath = "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Peasent_Male_01.prefab";
        private const string DirtMaterialPath = "Assets/Synty/PolygonGeneric/Materials/Generic_Dirt.mat";

        private const float CapsuleHeight = 1.8f;
        private const float CapsuleRadius = 0.35f;
        private const float PivotHeight = 1.4f;
        private const float EyeHeight = 1.62f;
        private const float GroundSize = 600f;

        [MenuItem("MedievalZen/Player/Create Player Rig Prefab")]
        private static void CreatePlayerRigPrefabMenu()
        {
            GameObject prefab = CreatePlayerRigPrefab();
            if (prefab == null) return;
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        [MenuItem("MedievalZen/Player/Create Player Test Scene")]
        private static void CreatePlayerTestSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = CreatePlayerRigPrefab();
            if (prefab == null) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RoadNetwork network = TestRoadNetworks.Triangle();

            CreateSun();
            CreateGround();
            CreateGameWorld(network);
            CreateRoadRibbon(network);
            CreateSettlementMarkers(network);
            CreatePlayer(prefab, network, scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EnsureFolder(ScenePath.Substring(0, ScenePath.LastIndexOf('/')));
            if (EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.Log($"Created {ScenePath}. Press Play, then T to auto-travel, V to switch camera, Esc to free the cursor.");
            }
        }

        /// <summary>Builds the PlayerRig hierarchy and saves it as <see cref="PrefabPath"/> (overwriting).</summary>
        public static GameObject CreatePlayerRigPrefab()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            if (character == null)
            {
                Debug.LogError($"Character prefab not found at {CharacterPrefabPath}.");
                return null;
            }

            Object inputActions = AssetDatabase.LoadMainAssetAtPath(InputActionsPath);
            if (inputActions == null)
            {
                Debug.LogError($"Input actions not found at {InputActionsPath}.");
                return null;
            }

            var root = new GameObject("PlayerRig");
            try
            {
                var controller = root.AddComponent<CharacterController>();
                controller.height = CapsuleHeight;
                controller.radius = CapsuleRadius;
                controller.center = new Vector3(0f, CapsuleHeight * 0.5f, 0f);
                controller.slopeLimit = 45f;
                controller.stepOffset = 0.3f;
                controller.skinWidth = 0.04f;
                controller.minMoveDistance = 0f;

                root.AddComponent<PlayerMotor>();
                var cameraRig = root.AddComponent<PlayerCameraRig>();
                root.AddComponent<AutoTraveller>();
                root.AddComponent<PlayerTravel>();
                var playerController = root.AddComponent<PlayerController>();
                AddWatchingComponents(root);

                // Visual: the Synty peasant as a nested prefab, feet on the rig origin.
                var visualRoot = new GameObject("Visual").transform;
                visualRoot.SetParent(root.transform, false);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(character, visualRoot);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                if (TryGetRendererBounds(visual, out Bounds bounds))
                {
                    visual.transform.localPosition = new Vector3(0f, -(bounds.min.y - root.transform.position.y), 0f);
                }

                var pivot = new GameObject("CameraPivot").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(0f, PivotHeight, 0f);

                var cameraObject = new GameObject("Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(pivot, false);
                cameraObject.transform.localPosition = new Vector3(0.35f, 0.3f, -3.5f);
                var camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 1500f;
                camera.fieldOfView = 60f;
                cameraObject.AddComponent<AudioListener>();

                var eyeAnchor = new GameObject("EyeAnchor").transform;
                eyeAnchor.SetParent(root.transform, false);
                eyeAnchor.localPosition = new Vector3(0f, EyeHeight, 0.08f);

                var rigObject = new SerializedObject(cameraRig);
                rigObject.FindProperty("viewCamera").objectReferenceValue = camera;
                rigObject.FindProperty("pivot").objectReferenceValue = pivot;
                rigObject.FindProperty("eyeAnchor").objectReferenceValue = eyeAnchor;
                rigObject.FindProperty("visualRoot").objectReferenceValue = visualRoot;
                rigObject.ApplyModifiedPropertiesWithoutUndo();

                var controllerObject = new SerializedObject(playerController);
                controllerObject.FindProperty("actions").objectReferenceValue = inputActions;
                controllerObject.ApplyModifiedPropertiesWithoutUndo();
                if (controllerObject.FindProperty("actions").objectReferenceValue == null)
                {
                    Debug.LogError($"Could not assign {InputActionsPath} to PlayerController.actions.");
                }

                if (!AddAnimation(root))
                {
                    Debug.LogWarning("PlayerRig saved without animation (see the errors above); run MedievalZen/Player/Upgrade Player Rig (Add Animation) once fixed.");
                }

                EnsureFolder(PrefabPath.Substring(0, PrefabPath.LastIndexOf('/')));
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError($"Failed to save {PrefabPath}.");
                    return null;
                }

                Debug.Log($"Saved {PrefabPath}.", saved);
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("MedievalZen/Player/Upgrade Player Rig (Add Animation)")]
        private static void UpgradePlayerRigMenu()
        {
            if (UpgradePlayerRigPrefab())
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
        }

        [MenuItem("MedievalZen/Player/Upgrade Player Rig (Add Animation)", true)]
        private static bool ValidateUpgrade() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>
        /// Adds (or refreshes) the animation setup on the existing <see cref="PrefabPath"/> in place, so scene instances
        /// (Demo, PlayerTest) pick it up without regenerating anything. Creates the prefab if it does not exist. Idempotent.
        /// </summary>
        public static bool UpgradePlayerRigPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                return CreatePlayerRigPrefab() != null;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (!AddAnimation(contents)) return false;
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError($"Failed to save {PrefabPath}.");
                    return false;
                }

                Debug.Log($"Upgraded {PrefabPath}: Animator (PlayerLocomotion.controller rebuilt, no root motion, always animate) + PlayerAnimator + measured grounding (see the grounding log) + sitting and the cinematic camera.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Configures the Animator on the character under <c>Visual</c> (avatar, locomotion controller rebuilt in place, no
        /// root motion), adds a <see cref="PlayerAnimator"/> to the rig root, and grounds the character: the soles'
        /// height in the animated idle pose is measured (<see cref="PlayerAnimationMenu.MeasureGrounding"/>) and the
        /// character is placed so they sit on the rig origin. The capsule's skin width (the gap the CharacterController
        /// keeps above the ground) is compensated at runtime by <see cref="PlayerAnimator"/>, which lowers the Visual by it
        /// (plus its inspector nudge <c>groundOffset</c>). Idempotent.
        /// </summary>
        private static bool AddAnimation(GameObject root)
        {
            if (!PlayerAnimationMenu.EnsureAssets(true, out var controller, out Avatar avatar)) return false;

            Transform visual = root.transform.Find("Visual");
            if (visual == null || visual.childCount == 0)
            {
                Debug.LogError($"'{root.name}' has no Visual/<character> child to animate.");
                return false;
            }

            Animator animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = visual.GetChild(0).gameObject.AddComponent<Animator>();
            PlayerAnimationMenu.ConfigureAnimator(animator, controller, avatar);

            var playerAnimator = root.GetComponent<PlayerAnimator>();
            if (playerAnimator == null) playerAnimator = root.AddComponent<PlayerAnimator>();
            var animatorObject = new SerializedObject(playerAnimator);
            animatorObject.FindProperty("animator").objectReferenceValue = animator;
            animatorObject.FindProperty("cameraRig").objectReferenceValue = root.GetComponent<PlayerCameraRig>();
            animatorObject.FindProperty("visual").objectReferenceValue = visual;
            animatorObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(playerAnimator);

            GroundCharacter(root, visual, animator, controller, avatar);
            AddWatchingComponents(root);
            return true;
        }

        /// <summary>
        /// Sitting down (<see cref="PlayerSeat"/>, <see cref="SeatedPose"/>) and the cinematic camera
        /// (<see cref="MedievalZen.Cinematic.CinematicDirector"/>). The PlayerController also adds them at runtime when a rig lacks
        /// them, so older scenes work without this. Idempotent.
        /// </summary>
        private static void AddWatchingComponents(GameObject root)
        {
            if (root.GetComponent<PlayerSeat>() == null) root.AddComponent<PlayerSeat>();
            if (root.GetComponent<SeatedPose>() == null) root.AddComponent<SeatedPose>();
            if (root.GetComponent<MedievalZen.Cinematic.CinematicDirector>() == null) root.AddComponent<MedievalZen.Cinematic.CinematicDirector>();
        }

        /// <summary>
        /// Measures the soles in the animated idle pose and sets the character's local height under <c>Visual</c> so they
        /// sit exactly on the rig origin (the bottom of the capsule). Keeps the current height if the measurement fails.
        /// Logs every number, including the same measurement with Synty's own avatar for comparison.
        /// </summary>
        private static void GroundCharacter(GameObject root, Transform visual, Animator animator, RuntimeAnimatorController controller, Avatar avatar)
        {
            var body = root.GetComponent<CharacterController>();
            var motor = root.GetComponent<PlayerMotor>();
            var playerAnimator = root.GetComponent<PlayerAnimator>();
            float skinWidth = body != null ? body.skinWidth : 0f;
            float walkSpeed = motor != null ? motor.WalkSpeed : 1.6f;
            Transform character = animator.transform;
            float before = character.localPosition.y;
            float visualY = visual.localPosition.y;
            float nudge = playerAnimator != null ? playerAnimator.GroundOffset : 0f;

            var log = new StringBuilder("Player grounding — heights in metres, + = up, relative to the character's origin unless noted:\n");
            if (!PlayerAnimationMenu.MeasureGrounding(controller, avatar, walkSpeed, log, out PlayerAnimationMenu.GroundingResult measured))
            {
                log.AppendLine($"  NOT APPLIED: character height stays {before:0.000}. Use PlayerAnimator.groundOffset to correct by eye.");
                Debug.LogWarning(log.ToString(), root);
                return;
            }

            float after = -measured.IdleSole;
            character.localPosition = new Vector3(character.localPosition.x, after, character.localPosition.z);
            EditorUtility.SetDirty(character);

            // Runtime: the rig origin rests skinWidth above the ground; PlayerAnimator lowers the Visual by skinWidth and adds the nudge.
            float oldGap = before + measured.IdleSole + visualY + skinWidth;
            float newGap = after + measured.IdleSole + nudge;
            log.AppendLine($"  character local height: {before:0.000} (previous; the old renderer-bounds guess) → {after:0.000}; Visual edit-mode height {visualY:0.000}");
            log.AppendLine($"  CharacterController: skinWidth {skinWidth:0.000} (kept), stepOffset {(body != null ? body.stepOffset : 0f):0.00}, centre {(body != null ? body.center.y : 0f):0.00} (kept); " +
                           $"PlayerAnimator lowers the Visual by the skin width at runtime, groundOffset nudge {nudge:0.000}");
            log.AppendLine($"  idle soles above the ground at runtime: before {oldGap:0.000} → after {newGap:0.000}; " +
                           $"walk: planted foot {measured.WalkLowest - measured.IdleSole:+0.000;-0.000} vs idle, lower foot never above {measured.WalkHighestLowFoot - measured.IdleSole:+0.000;-0.000}");

            Avatar synty = PlayerAnimationMenu.CharacterAvatar();
            if (synty != null && synty != avatar)
            {
                log.AppendLine("  comparison with Synty's own avatar (not applied):");
                if (PlayerAnimationMenu.MeasureGrounding(controller, synty, walkSpeed, log, out PlayerAnimationMenu.GroundingResult original))
                {
                    log.AppendLine($"  → Synty avatar idle soles {original.IdleSole:0.000} vs rebuilt {measured.IdleSole:0.000} (difference {(original.IdleSole - measured.IdleSole) * 100f:0.0} cm); " +
                                   $"pelvis {original.PelvisHeight:0.000} vs {measured.PelvisHeight:0.000}; humanScale {original.HumanScale:0.000} vs {measured.HumanScale:0.000}");
                }
            }

            Debug.Log(log.ToString(), root);
        }

        private static bool TryGetRendererBounds(GameObject gameObject, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;
                if (any) bounds.Encapsulate(renderer.bounds);
                else bounds = renderer.bounds;
                any = true;
            }

            return any;
        }

        private static void CreateSun()
        {
            var sun = new GameObject("Directional Light");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = light;
        }

        private static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10 x 10 m with a MeshCollider
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(GroundSize / 10f, 1f, GroundSize / 10f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateLitMaterial("Ground_Grass", new Color(0.36f, 0.52f, 0.24f));
        }

        private static void CreateGameWorld(RoadNetwork network)
        {
            var worldObject = new GameObject("GameWorld");
            var world = worldObject.AddComponent<GameWorld>();
            world.SetRoads(network);
            EditorUtility.SetDirty(world);
        }

        private static void CreateRoadRibbon(RoadNetwork network)
        {
            var ribbonObject = new GameObject("RoadRibbon");
            var ribbon = ribbonObject.AddComponent<RoadRibbonRenderer>();
            var renderer = ribbonObject.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var dirt = AssetDatabase.LoadAssetAtPath<Material>(DirtMaterialPath);
            renderer.sharedMaterial = dirt != null ? dirt : GetOrCreateLitMaterial("Road_Dirt", new Color(0.55f, 0.43f, 0.3f));
            ribbon.Rebuild(network);
            EditorUtility.SetDirty(ribbon);
        }

        private static void CreateSettlementMarkers(RoadNetwork network)
        {
            var parent = new GameObject("SettlementMarkers").transform;

            Vector3 centroid = Vector3.zero;
            foreach (Settlement settlement in network.Settlements) centroid += settlement.Position;
            if (network.Settlements.Count > 0) centroid /= network.Settlements.Count;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (Settlement settlement in network.Settlements)
            {
                // Offset outwards, away from where the roads arrive, so the marker never blocks a road end.
                Vector3 outward = settlement.Position - centroid;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
                Vector3 basePosition = settlement.Position + outward * 8f;

                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = $"Marker {settlement.Name}";
                marker.transform.SetParent(parent, false);
                marker.transform.position = basePosition + Vector3.up * 1.5f;
                marker.transform.localScale = new Vector3(1.5f, 3f, 1.5f);
                marker.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateLitMaterial($"Marker_{settlement.Type}", MarkerColor(settlement.Type));

                var label = new GameObject("Label");
                label.transform.SetParent(parent, false);
                label.name = $"Label {settlement.Name}";
                label.transform.position = basePosition + Vector3.up * 4.5f;
                label.transform.rotation = Quaternion.LookRotation(outward);   // readable when looking outwards from the centre
                var text = label.AddComponent<TextMesh>();
                text.text = $"{settlement.Name}\n({settlement.Type})";
                text.anchor = TextAnchor.LowerCenter;
                text.alignment = TextAlignment.Center;
                text.fontSize = 64;
                text.characterSize = 0.08f;
                text.color = Color.white;
                if (font != null)
                {
                    text.font = font;
                    label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                }
            }
        }

        private static void CreatePlayer(GameObject prefab, RoadNetwork network, Scene scene)
        {
            Settlement start = network.Settlements[0];
            RoadSegment road = start.Node.Segments[0];
            bool fromA = road.A == start.Node;
            float along = Mathf.Min(start.Radius + 8f, road.Length * 0.5f);
            float distance = fromA ? along : road.Length - along;
            Vector3 position = road.Evaluate(distance) + Vector3.up * 0.05f;
            Vector3 heading = road.Tangent(distance) * (fromA ? 1f : -1f);
            heading.y = 0f;

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading.normalized));
        }

        private static Color MarkerColor(SettlementType type)
        {
            switch (type)
            {
                case SettlementType.Town: return new Color(0.85f, 0.3f, 0.25f);
                case SettlementType.Village: return new Color(0.95f, 0.8f, 0.3f);
                case SettlementType.Inn: return new Color(0.3f, 0.5f, 0.9f);
                default: return Color.gray;
            }
        }

        private static Material GetOrCreateLitMaterial(string name, Color color)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);

            EnsureFolder(MaterialsFolder);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
