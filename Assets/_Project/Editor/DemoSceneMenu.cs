using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Travel;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// <c>MedievalZen/Setup Demo Scene</c>: builds <see cref="ScenePath"/> from scratch — a generated world (same code path as
    /// Generate World), the PlayerRig with the Phase-3 travel components (held map, interactor, arrival toasts), a day cycle
    /// owning the sun, a player-following sky (Synty skydome, cloud ring, horizon fog ring), the Meadow post-processing
    /// volume, Synty WeatherControl with a gentle breeze, the HUD canvas + EventSystem and the ambient FX spawner.
    /// <c>MedievalZen/Open Demo Scene</c> opens it. The scene building blocks (sun, sky, day cycle, post-processing, weather,
    /// UI, ambient effects) are shared with the endless road's scene menu (<see cref="EndlessRoadMenu"/>).
    /// </summary>
    internal static class DemoSceneMenu
    {
        /// <summary>Where the demo scene is saved.</summary>
        public const string ScenePath = "Assets/_Project/Scenes/Demo.unity";

        private const string SkydomePath = "Assets/Synty/PNB_Core/Prefabs/SM_Env_Skydome_01.prefab";
        private const string CloudRingPath = "Assets/Synty/PNB_Core/Prefabs/SM_Env_Cloud_Ring_01.prefab";
        private const string FogRingPath = "Assets/Synty/PNB_Core/Prefabs/SM_Env_Fog_Ring_01.prefab";
        private const string WeatherControlPath = "Assets/Synty/PNB_Core/Prefabs/WeatherControl.prefab";
        private const string Meadow = "Assets/Synty/PolygonNatureBiomes/PNB_Meadow_Forest/";
        private const string SkyMaterialPath = Meadow + "Materials/Skybox_Meadows_Mat_01.mat";
        private const string CloudMaterialPath = Meadow + "Materials/Synty_Clouds_Meadows.mat";
        private const string FogMaterialPath = Meadow + "FX/FX_Materials/Fog_Meadows_01.mat";
        private const string PostProcessingPath = Meadow + "Meadows_Post_Processing_01.asset";
        private const string FxFolder = Meadow + "FX/FX_Prefabs/";

        private static readonly string[] MeadowFx =
        {
            "FX_Butterflies_Blue_01", "FX_Butterflies_Cabbage_01", "FX_Butterflies_Monarch_01", "FX_Butterflies_Lunar_01",
            "FX_Petals_Red_01", "FX_Petals_Yellow_01",
        };

        private static readonly string[] ForestFx = { "FX_Leaves_Meadows_01", "FX_Leaves_Blowing_Meadows_01" };

        private const float SkyRadius = 3200f;
        private const float CloudRadius = 2700f;
        private const float FogRingRadius = 2300f;
        private const float FogRingHeight = 260f;
        private const float MinFarClip = 4500f;

        [MenuItem("MedievalZen/Setup Demo Scene", priority = 20)]
        private static void SetupDemoSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("Setup Demo Scene", $"{ScenePath} exists. Rebuild it from scratch?", "Rebuild", "Cancel"))
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var worldObject = new GameObject("GameWorld");
            var world = worldObject.AddComponent<GameWorld>();
            world.Settings = WorldGeneratorMenu.LoadOrCreateSettings();
            world.Palette = WorldPaletteMenu.LoadOrCreateDefault();
            if (!WorldGeneratorMenu.GenerateWorld(world))
            {
                Debug.LogError("Setup Demo Scene: world generation failed; the scene was not saved.");
                return;
            }

            GameObject player = Object.FindFirstObjectByType<PlayerController>()?.gameObject;
            if (player == null)
            {
                Debug.LogError("Setup Demo Scene: no PlayerRig was placed; the scene was not saved.");
                return;
            }

            SetupPlayer(player);
            Light sun = FindSun();
            (Renderer[] sky, Renderer[] clouds, Renderer[] fog) = CreateSky(player.transform);
            CreateDayCycle(sun, sky, clouds, fog);
            CreatePostProcessing();
            CreateWeather();
            CreateUi();
            CreateAmbientFx(player.transform);
            Debug.Log(AtmosphereMenu.ApplyToScene(player.transform));

            EditorSceneManager.MarkSceneDirty(scene);
            WorldPaletteMenu.EnsureFolder(ScenePath.Substring(0, ScenePath.LastIndexOf('/')));
            if (EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.Log($"Created {ScenePath}. Press Play: WASD walk, mouse look, V camera, M map, E interact, T auto-travel, Esc journey menu (time, weather, view, travel).");
            }
        }

        [MenuItem("MedievalZen/Open Demo Scene", priority = 21)]
        private static void OpenDemoSceneMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                if (EditorUtility.DisplayDialog("Open Demo Scene", $"{ScenePath} does not exist yet. Build it now?", "Setup Demo Scene", "Cancel"))
                {
                    SetupDemoSceneMenu();
                }

                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("MedievalZen/Setup Demo Scene", true)]
        [MenuItem("MedievalZen/Open Demo Scene", true)]
        private static bool ValidateNotPlaying() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Adds the Phase-3 player components (held map, interactor, signpost list, arrival toasts) and camera settings.</summary>
        public static void SetupPlayer(GameObject player)
        {
            var map = GetOrAdd<HeldMap>(player);
            var mapObject = new SerializedObject(map);
            SerializedProperty shader = mapObject.FindProperty("sheetShader");
            if (shader != null) shader.objectReferenceValue = Shader.Find("Universal Render Pipeline/Unlit");
            mapObject.ApplyModifiedPropertiesWithoutUndo();

            GetOrAdd<PlayerInteractor>(player);
            GetOrAdd<SignpostMenu>(player);
            GetOrAdd<ArrivalAnnouncer>(player);

            foreach (Camera camera in player.GetComponentsInChildren<Camera>(true))
            {
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, MinFarClip);
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(camera);
            }

            EditorUtility.SetDirty(player);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        internal static Light FindSun()
        {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) return light;
            }

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            return sun;
        }

        internal static (Renderer[] sky, Renderer[] clouds, Renderer[] fog) CreateSky(Transform player)
        {
            var root = new GameObject("Sky");
            root.transform.position = player.position;
            root.AddComponent<SkyFollower>().Target = player;

            GameObject dome = SpawnScaled(SkydomePath, root.transform, SkyRadius, 0.55f, 0f, SkyMaterialPath);
            GameObject clouds = SpawnScaled(CloudRingPath, root.transform, CloudRadius, 1f, 90f, CloudMaterialPath);
            GameObject fog = SpawnScaled(FogRingPath, root.transform, FogRingRadius, -1f, -70f, FogMaterialPath);

            foreach (GameObject go in new[] { dome, clouds, fog })
            {
                if (go == null) continue;
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }

                foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }

            return (Renderers(dome), Renderers(clouds), Renderers(fog));
        }

        /// <summary>
        /// Instantiates <paramref name="prefabPath"/> under <paramref name="parent"/> scaled so its horizontal radius is
        /// <paramref name="radius"/>; the vertical scale is <paramref name="verticalRatio"/> × the horizontal one, or (negative
        /// ratio) fits the mesh to <see cref="FogRingHeight"/>. Optionally overrides the material (a scene override, the
        /// Synty assets are untouched).
        /// </summary>
        private static GameObject SpawnScaled(string prefabPath, Transform parent, float radius, float verticalRatio, float height, string materialPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"Setup Demo Scene: {prefabPath} not found; skipped.");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Bounds bounds = WorldBuilder.LocalBounds(prefab);
            float horizontal = radius / Mathf.Max(0.01f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            float vertical = verticalRatio > 0f ? horizontal * verticalRatio : FogRingHeight / Mathf.Max(0.01f, bounds.size.y);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(horizontal, vertical, horizontal);

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material != null)
            {
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = material;
            }

            return go;
        }

        private static Renderer[] Renderers(GameObject go) => go != null ? go.GetComponentsInChildren<Renderer>(true) : new Renderer[0];

        internal static void CreateDayCycle(Light sun, Renderer[] sky, Renderer[] clouds, Renderer[] fog)
        {
            var cycle = new GameObject("DayCycle").AddComponent<DayCycle>();
            if (sun != null)
            {
                sun.name = "Sun";
                sun.transform.SetParent(cycle.transform, true);
                sun.shadows = LightShadows.Soft;
                RenderSettings.sun = sun;
            }

            cycle.Sun = sun;
            cycle.SetSkyRenderers(sky, clouds, fog);
            EditorUtility.SetDirty(cycle);
        }

        internal static void CreatePostProcessing()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProcessingPath);
            if (profile == null)
            {
                Debug.LogWarning($"Setup Demo Scene: {PostProcessingPath} is not a VolumeProfile; no post-processing.");
                return;
            }

            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;   // referenced, not copied
        }

        internal static void CreateWeather()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeatherControlPath);
            if (prefab == null)
            {
                Debug.LogWarning($"Setup Demo Scene: {WeatherControlPath} not found; no wind.");
                return;
            }

            var weather = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            weather.transform.rotation = Quaternion.Euler(0f, 40f, 0f);   // wind blows towards the north-east
            Component control = weather.GetComponent("WeatherControl");
            if (control == null) return;
            var so = new SerializedObject(control);
            SerializedProperty wind = so.FindProperty("windIntensity");
            SerializedProperty gale = so.FindProperty("weatherIntensity");
            if (wind != null) wind.floatValue = 0.3f;
            if (gale != null) gale.floatValue = 0.05f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void CreateUi()
        {
            var ui = new GameObject("UI", typeof(RectTransform));
            ui.layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 0;
            var canvas = ui.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = ui.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            ui.AddComponent<GraphicRaycaster>();
            ui.AddComponent<HudController>();   // builds the HUD, prompt, toast, choice and fade layers at runtime

            // The module's editor Reset assigns the package's DefaultInputActions asset; if it ends up without actions it
            // falls back to DefaultInputActions at runtime (OnEnable), so nothing non-persistent is stored in the scene.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        internal static void CreateAmbientFx(Transform player)
        {
            var spawner = new GameObject("AmbientFx").AddComponent<AmbientFxSpawner>();
            var so = new SerializedObject(spawner);
            so.FindProperty("target").objectReferenceValue = player;
            FillPrefabs(so.FindProperty("meadowFx"), MeadowFx);
            FillPrefabs(so.FindProperty("forestFx"), ForestFx);
            so.FindProperty("sunBeamFx").objectReferenceValue = LoadFx("FX_SunBeam_01");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillPrefabs(SerializedProperty array, string[] names)
        {
            array.ClearArray();
            foreach (string fxName in names)
            {
                GameObject prefab = LoadFx(fxName);
                if (prefab == null) continue;
                array.InsertArrayElementAtIndex(array.arraySize);
                array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = prefab;
            }
        }

        private static GameObject LoadFx(string fxName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FxFolder + fxName + ".prefab");
            if (prefab == null) Debug.LogWarning($"Setup Demo Scene: ambient FX {fxName} not found in {FxFolder}.");
            return prefab;
        }
    }
}
