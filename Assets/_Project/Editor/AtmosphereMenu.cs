using System.Collections.Generic;
using System.Text;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// <c>MedievalZen/Atmosphere/Apply To Open Scene</c> (also run by Setup Demo Scene). Idempotent — finds or creates, then
    /// updates:
    /// <list type="bullet">
    /// <item>materials <c>Assets/_Project/Materials/Atmosphere/AtmosphericFog.mat</c> and <c>FireflyGlow.mat</c>;</item>
    /// <item>a <c>FullScreenPassRendererFeature</c> "AtmosphericFog" on <c>Assets/Settings/PC_Renderer.asset</c>
    /// (Before Rendering Post Processing, Depth input, no colour copy — the shader alpha-blends);</item>
    /// <item>main-light shadow settings on <c>Assets/Settings/PC_RPAsset.asset</c>;</item>
    /// <item>the time-of-day grading profile <c>Assets/_Project/Settings/Atmosphere/TimeOfDay.asset</c>;</item>
    /// <item>in the open scene: an <c>Atmosphere</c> object (AtmosphereController, WeatherSystem, MistField, grading Volume,
    /// procedural wind audio — off), rain effects under the player-following sky root, SettlementAmbience on every
    /// settlement, the ambient FX spawner's new effects, and exp² built-in fog as the saved fallback.</item>
    /// </list>
    /// Re-running it on a scene set up by an earlier version upgrades it in place (changed defaults are rewritten only
    /// while they still hold the old value). Nothing under <c>Assets/Synty</c> is modified.
    /// </summary>
    internal static class AtmosphereMenu
    {
        private const string MaterialFolder = "Assets/_Project/Materials/Atmosphere";
        private const string SettingsFolder = "Assets/_Project/Settings/Atmosphere";
        private const string FogMaterialPath = MaterialFolder + "/AtmosphericFog.mat";
        private const string FireflyMaterialPath = MaterialFolder + "/FireflyGlow.mat";
        private const string GradingProfilePath = SettingsFolder + "/TimeOfDay.asset";
        private const string PipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";
        private const string RendererDataPath = "Assets/Settings/PC_Renderer.asset";
        private const string FogFeatureName = "AtmosphericFog";
        private const string FireflyShaderName = "MedievalZen/FireflyGlow";

        private const string Meadow = "Assets/Synty/PolygonNatureBiomes/PNB_Meadow_Forest/FX/FX_Prefabs/";
        private const string Generic = "Assets/Synty/PolygonGeneric/Prefabs/FX/";
        private const string MistPrefabPath = Generic + "FX_Fog_01.prefab";
        internal const string SmokePrefabPath = Generic + "FX_Smoke_01.prefab";
        internal const string FirePrefabPath = Meadow + "FX_Fire_01.prefab";
        private const string RainPrefabPath = "Assets/Synty/PNB_Core/Prefabs/FX_Rain_01.prefab";
        private const string RainSplashPrefabPath = "Assets/Synty/PNB_Core/Prefabs/FX_Rain_Splash_01.prefab";
        private const string BirdPrefabPath = "Assets/Synty/PolygonNatureBiomes/PNB_Arid_Desert/FX/FX_Prefabs/FX_Vulture_01.prefab";

        private static readonly string[] MeadowFx =
        {
            "FX_Butterflies_Blue_01", "FX_Butterflies_Cabbage_01", "FX_Butterflies_Monarch_01", "FX_Butterflies_Lunar_01",
            "FX_Petals_Red_01", "FX_Petals_Yellow_01",
        };

        private static readonly string[] ForestFx = { "FX_Leaves_Meadows_01", "FX_Leaves_Blowing_Meadows_01" };

        // Main-light shadows for a walking-speed game: 180 m, 4 cascades at 12 / 36 / 85 / 180 m.
        private const float ShadowDistance = 180f;
        private static readonly Vector3 Cascade4Split = new Vector3(0.067f, 0.2f, 0.47f);
        private const float CascadeBorder = 0.12f;
        private const float ShadowDepthBias = 1f;
        private const float ShadowNormalBias = 0.75f;
        private const int ShadowResolution = 4096;

        [MenuItem("MedievalZen/Atmosphere/Apply To Open Scene", priority = 40)]
        private static void ApplyMenu()
        {
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            string report = ApplyToScene(player != null ? player.transform : null);
            Debug.Log(report);
        }

        [MenuItem("MedievalZen/Atmosphere/Apply To Open Scene", true)]
        private static bool ValidateApply() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Applies the whole atmosphere setup to the active scene; returns a human-readable report.</summary>
        public static string ApplyToScene(Transform player)
        {
            var log = new StringBuilder("Atmosphere applied:");
            WorldPaletteMenu.EnsureFolder(MaterialFolder);
            WorldPaletteMenu.EnsureFolder(SettingsFolder);

            Material fogMaterial = EnsureMaterial(FogMaterialPath, AtmosphereController.FogShaderName, log);
            Material fireflyMaterial = EnsureMaterial(FireflyMaterialPath, FireflyShaderName, log);
            if (fireflyMaterial != null)
            {
                fireflyMaterial.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(fireflyMaterial);
            }

            EnsureFogFeature(fogMaterial, log);
            EnsureShadows(log);
            VolumeProfile grading = EnsureGradingProfile(log);

            GameObject atmosphere = FindOrCreateRoot("Atmosphere");
            Volume volume = EnsureGradingVolume(atmosphere, grading);
            GameObject rain = EnsureRain(player, log);

            var controller = GetOrAdd<AtmosphereController>(atmosphere);
            var so = new SerializedObject(controller);
            so.FindProperty("fogMaterial").objectReferenceValue = fogMaterial;
            so.FindProperty("gradingVolume").objectReferenceValue = volume;
            UpgradeController(so, log);
            so.ApplyModifiedPropertiesWithoutUndo();
            CheckClouds(log);

            var weather = GetOrAdd<WeatherSystem>(atmosphere);
            so = new SerializedObject(weather);
            so.FindProperty("rainRoot").objectReferenceValue = rain;
            so.ApplyModifiedPropertiesWithoutUndo();

            var mist = GetOrAdd<MistField>(atmosphere);
            so = new SerializedObject(mist);
            so.FindProperty("mistPrefab").objectReferenceValue = Load(MistPrefabPath, log);
            if (player != null) so.FindProperty("target").objectReferenceValue = player;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureWindAudio(atmosphere);
            EnsureAmbientFx(player, fireflyMaterial, log);
            int settlements = EnsureSettlements(log);

            // Saved built-in fog = the fallback look (exp², also keeps the exp² shader variants in builds). At runtime the
            // AtmosphereController turns it off while the fog pass is live.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0009f;

            EditorUtility.SetDirty(atmosphere);
            EditorSceneManager.MarkSceneDirty(atmosphere.scene);
            AssetDatabase.SaveAssets();
            log.Append($"\n  scene: Atmosphere (controller, weather, mist, grading volume, wind audio off), {settlements} settlements with ambience.");
            log.Append("\n  Save the scene to keep it. Tune: Atmosphere → AtmosphereController → Fog Amount / Haze Distance / Cloud Softness; force weather on WeatherSystem.");
            return log.ToString();
        }

        // Values shipped by earlier versions of this menu / the controller, upgraded in place only while untouched.
        private const float OldGoldenSaturation = 10f;
        private const float NewGoldenSaturation = 6f;
        private const float OldBaseSaturation = 8f;
        private const float NewBaseSaturation = 6f;

        /// <summary>
        /// Brings an AtmosphereController saved by an earlier version up to date. New fields (cloud softness, saturation,
        /// contrast, sky haze tail) already get their defaults when the old component deserialises; changed defaults are
        /// rewritten only if they still hold the old default, so user tweaks survive.
        /// </summary>
        private static void UpgradeController(SerializedObject so, StringBuilder log)
        {
            SerializedProperty saturation = so.FindProperty("saturationGoldenNightOvercast");
            if (saturation != null && Mathf.Approximately(saturation.vector3Value.x, OldGoldenSaturation))
            {
                Vector3 v = saturation.vector3Value;
                v.x = NewGoldenSaturation;
                saturation.vector3Value = v;
                log.Append($"\n  upgraded grading: golden-hour saturation +{OldGoldenSaturation} → +{NewGoldenSaturation}");
            }

            SerializedProperty softness = so.FindProperty("cloudSoftness");
            log.Append(softness != null
                ? $"\n  clouds: colours from the atmosphere (Cloud Softness {softness.floatValue:0.##}, saturation cap {so.FindProperty("cloudSaturation").floatValue:0.##}, contrast {so.FindProperty("cloudContrast").floatValue:0.##})"
                : "\n  WARNING: AtmosphereController has no cloudSoftness field (scripts not recompiled yet?) — re-run after compiling.");
        }

        private static void CheckClouds(StringBuilder log)
        {
            DayCycle cycle = Object.FindFirstObjectByType<DayCycle>();
            if (cycle == null)
            {
                log.Append("\n  WARNING: no DayCycle in the scene; clouds are not coloured.");
                return;
            }

            int count = 0;
            foreach (Renderer r in cycle.CloudRenderers)
            {
                if (r != null) count++;
            }

            if (count == 0) log.Append("\n  WARNING: DayCycle has no cloud renderers (run Setup Demo Scene to create the sky).");
        }

        // ------------------------------------------------------------------ assets

        private static Material EnsureMaterial(string path, string shaderName, StringBuilder log)
        {
            Shader shader = Shader.Find(shaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (shader == null)
            {
                log.Append($"\n  WARNING: shader '{shaderName}' not found (not imported yet?) — {path} not created. Re-run once the editor has imported Assets/_Project/Shaders.");
                return material;
            }

            if (material == null)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
                log.Append($"\n  created {path}");
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        private static void EnsureFogFeature(Material material, StringBuilder log)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererDataPath);
            if (data == null)
            {
                log.Append($"\n  WARNING: {RendererDataPath} not found; no fog pass (built-in fog fallback will be used).");
                return;
            }

            if (material == null)
            {
                log.Append("\n  WARNING: no fog material; fog feature not added.");
                return;
            }

            FullScreenPassRendererFeature feature = null;
            foreach (ScriptableRendererFeature f in data.rendererFeatures)
            {
                if (f is FullScreenPassRendererFeature fs && (f.name == FogFeatureName || (fs.passMaterial != null && fs.passMaterial.shader == material.shader)))
                {
                    feature = fs;
                    break;
                }
            }

            bool created = feature == null;
            if (created)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = FogFeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                var so = new SerializedObject(data);
                SerializedProperty list = so.FindProperty("m_RendererFeatures");
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feature.fetchColorBuffer = false;
            feature.requirements = ScriptableRenderPassInput.Depth;
            feature.passMaterial = material;
            feature.passIndex = 0;
            feature.bindDepthStencilAttachment = false;
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            data.SetDirty();
            EditorUtility.SetDirty(data);
            log.Append(created
                ? $"\n  added renderer feature '{FogFeatureName}' to {RendererDataPath}"
                : $"\n  updated renderer feature '{FogFeatureName}' on {RendererDataPath}");
        }

        private static void EnsureShadows(StringBuilder log)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (asset == null)
            {
                log.Append($"\n  WARNING: {PipelineAssetPath} not found; shadows unchanged.");
                return;
            }

            var so = new SerializedObject(asset);
            SetFloat(so, "m_ShadowDistance", ShadowDistance);
            SetInt(so, "m_ShadowCascadeCount", 4);
            SerializedProperty split = so.FindProperty("m_Cascade4Split");
            if (split != null) split.vector3Value = Cascade4Split;
            SetFloat(so, "m_CascadeBorder", CascadeBorder);
            SetFloat(so, "m_ShadowDepthBias", ShadowDepthBias);
            SetFloat(so, "m_ShadowNormalBias", ShadowNormalBias);
            SetInt(so, "m_MainLightShadowmapResolution", ShadowResolution);
            SerializedProperty soft = so.FindProperty("m_SoftShadowsSupported");
            if (soft != null) soft.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            log.Append($"\n  {PipelineAssetPath}: shadow distance {ShadowDistance} m, 4 cascades ({Cascade4Split.x}, {Cascade4Split.y}, {Cascade4Split.z}), " +
                       $"border {CascadeBorder}, depth bias {ShadowDepthBias}, normal bias {ShadowNormalBias}, main light {ShadowResolution}², soft shadows on");
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        private static VolumeProfile EnsureGradingProfile(StringBuilder log)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradingProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, GradingProfilePath);
                log.Append($"\n  created {GradingProfilePath}");
            }

            // Values are authored only when a component is first added, so user tweaks survive re-runs. The Meadow
            // post-processing asset is a Post Processing Stack v2 profile (not a URP VolumeProfile), so these baselines
            // carry its intent: gentle saturation/contrast, warm light bloom, a soft vignette.
            if (AddComponent(profile, out Tonemapping tonemapping))
            {
                tonemapping.mode.Override(TonemappingMode.Neutral);
            }

            if (AddComponent(profile, out ColorAdjustments color))
            {
                color.postExposure.Override(0f);
                color.contrast.Override(5f);
                color.saturation.Override(NewBaseSaturation);
                color.colorFilter.Override(Color.white);
            }
            else if (color.saturation.overrideState && Mathf.Approximately(color.saturation.value, OldBaseSaturation))
            {
                // Upgrade the earlier baseline (+8) in place; a user-edited value is left alone.
                color.saturation.Override(NewBaseSaturation);
                log.Append($"\n  upgraded {GradingProfilePath}: saturation +{OldBaseSaturation} → +{NewBaseSaturation}");
            }

            if (AddComponent(profile, out WhiteBalance balance))
            {
                balance.temperature.Override(0f);
                balance.tint.Override(0f);
            }

            if (AddComponent(profile, out Bloom bloom))
            {
                bloom.intensity.Override(0.2f);
                bloom.threshold.Override(1f);
                bloom.scatter.Override(0.65f);
                bloom.tint.Override(new Color(1f, 0.9f, 0.8f));
            }

            if (AddComponent(profile, out Vignette vignette))
            {
                vignette.intensity.Override(0.18f);
                vignette.smoothness.Override(0.45f);
            }

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static bool AddComponent<T>(VolumeProfile profile, out T component) where T : VolumeComponent
        {
            if (profile.TryGet(out component)) return false;
            component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return true;
        }

        // ------------------------------------------------------------------ scene

        private static GameObject FindOrCreateRoot(string rootName)
        {
            foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == rootName) return go;
            }

            return new GameObject(rootName);
        }

        private static Volume EnsureGradingVolume(GameObject atmosphere, VolumeProfile profile)
        {
            foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (v.sharedProfile == profile)
                {
                    v.isGlobal = true;
                    v.priority = Mathf.Max(v.priority, 10f);
                    return v;
                }
            }

            var go = new GameObject("TimeOfDayGrading");
            go.transform.SetParent(atmosphere.transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;   // above the Meadow/default volumes
            volume.sharedProfile = profile;
            return volume;
        }

        private static GameObject EnsureRain(Transform player, StringBuilder log)
        {
            SkyFollower sky = Object.FindFirstObjectByType<SkyFollower>();
            Transform parent;
            if (sky != null)
            {
                parent = sky.transform;
            }
            else
            {
                var follower = new GameObject("WeatherFx").AddComponent<SkyFollower>();
                follower.Target = player;
                parent = follower.transform;
                log.Append("\n  no Sky root found: created WeatherFx (player-following) for the rain.");
            }

            Transform rain = parent.Find("Rain");
            if (rain == null)
            {
                rain = new GameObject("Rain").transform;
                rain.SetParent(parent, false);
            }

            rain.localPosition = Vector3.zero;
            rain.localRotation = Quaternion.identity;
            rain.localScale = Vector3.one;
            EnsureChildPrefab(rain, RainPrefabPath, Vector3.zero, log);
            EnsureChildPrefab(rain, RainSplashPrefabPath, Vector3.zero, log);
            return rain.gameObject;
        }

        private static void EnsureChildPrefab(Transform parent, string path, Vector3 localPosition, StringBuilder log)
        {
            string childName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (parent.Find(childName) != null) return;
            GameObject prefab = Load(path, log);
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = childName;
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
        }

        private static void EnsureWindAudio(GameObject atmosphere)
        {
            Transform child = atmosphere.transform.Find("WindAudio");
            if (child == null)
            {
                child = new GameObject("WindAudio", typeof(AudioSource)).transform;
                child.SetParent(atmosphere.transform, false);
            }

            GetOrAdd<ProceduralWindAudio>(child.gameObject);
        }

        private static void EnsureAmbientFx(Transform player, Material fireflyMaterial, StringBuilder log)
        {
            AmbientFxSpawner spawner = Object.FindFirstObjectByType<AmbientFxSpawner>();
            if (spawner == null) spawner = new GameObject("AmbientFx").AddComponent<AmbientFxSpawner>();
            var so = new SerializedObject(spawner);
            if (player != null && so.FindProperty("target").objectReferenceValue == null) so.FindProperty("target").objectReferenceValue = player;
            if (so.FindProperty("meadowFx").arraySize == 0) FillPrefabs(so.FindProperty("meadowFx"), MeadowFx, log);
            if (so.FindProperty("forestFx").arraySize == 0) FillPrefabs(so.FindProperty("forestFx"), ForestFx, log);
            if (so.FindProperty("sunBeamFx").objectReferenceValue == null) so.FindProperty("sunBeamFx").objectReferenceValue = Load(Meadow + "FX_SunBeam_01.prefab", log);
            so.FindProperty("dustFx").objectReferenceValue = Load(Meadow + "FX_Dust_Spots_01.prefab", log);
            so.FindProperty("windStreakFx").objectReferenceValue = Load(Meadow + "FX_Wind_Streaks_01.prefab", log);
            so.FindProperty("rippleFx").objectReferenceValue = Load(Meadow + "FX_Water_Movement_01.prefab", log);
            so.FindProperty("birdFx").objectReferenceValue = Load(BirdPrefabPath, log);
            so.FindProperty("fireflyMaterial").objectReferenceValue = fireflyMaterial;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillPrefabs(SerializedProperty array, string[] names, StringBuilder log)
        {
            array.ClearArray();
            foreach (string fxName in names)
            {
                GameObject prefab = Load(Meadow + fxName + ".prefab", log);
                if (prefab == null) continue;
                array.InsertArrayElementAtIndex(array.arraySize);
                array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = prefab;
            }
        }

        private static int EnsureSettlements(StringBuilder log)
        {
            GameObject smoke = Load(SmokePrefabPath, log);
            GameObject fire = Load(FirePrefabPath, log);
            int count = 0;
            int cabinsTotal = 0;
            int firesTotal = 0;
            foreach (SettlementMarker marker in Object.FindObjectsByType<SettlementMarker>(FindObjectsSortMode.None))
            {
                var cabins = new List<SettlementAmbience.CabinAnchor>();
                var fires = new List<SettlementAmbience.FireAnchor>();
                Transform door = null;
                var cabinRoots = new List<Transform>();
                foreach (Transform t in marker.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "InnDoor") door = t;
                    if (IsRootMatch(t, "Stone_Cabin")) cabinRoots.Add(t);
                    if (IsRootMatch(t, "Camp_Fireplace")) fires.Add(new SettlementAmbience.FireAnchor { fireplace = t, local = new Vector3(0f, 0.15f, 0f) });
                }

                var rng = new System.Random(marker.SettlementName.GetHashCode());
                bool inn = marker.SettlementType == MedievalZen.Roads.SettlementType.Inn;
                foreach (Transform cabin in cabinRoots)
                {
                    if (!CabinAnchors(cabin, marker.transform.position, door, out Vector3 chimney, out Vector3 light)) continue;
                    cabins.Add(new SettlementAmbience.CabinAnchor
                    {
                        cabin = cabin,
                        chimneyLocal = chimney,
                        lightLocal = light,
                        smoke = inn || rng.NextDouble() < 0.65,
                    });
                }

                var ambience = GetOrAdd<SettlementAmbience>(marker.gameObject);
                ambience.Configure(cabins.ToArray(), fires.ToArray(), smoke, fire);
                EditorUtility.SetDirty(ambience);
                count++;
                cabinsTotal += cabins.Count;
                firesTotal += fires.Count;
            }

            log.Append($"\n  settlements: {count} with ambience ({cabinsTotal} cabins, {firesTotal} fireplaces)");
            return count;
        }

        /// <summary>True for the outermost transform whose name contains <paramref name="key"/> (not its children).</summary>
        private static bool IsRootMatch(Transform t, string key)
        {
            if (t.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
            return t.parent == null || t.parent.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// Chimney top = centre of the highest vertices (the chimney pokes above the ridge); window light = just outside the
        /// wall facing the settlement centre (or the inn door), 1.4 m up. Both in the cabin's local space.
        /// </summary>
        internal static bool CabinAnchors(Transform cabin, Vector3 settlementCentre, Transform door, out Vector3 chimney, out Vector3 light)
        {
            chimney = light = Vector3.zero;
            MeshFilter[] filters = cabin.GetComponentsInChildren<MeshFilter>(true);
            var local = new Bounds();
            bool any = false;
            float top = float.NegativeInfinity;
            var points = new List<Vector3>(4096);
            foreach (MeshFilter mf in filters)
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || mf.name.IndexOf("Collision", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = cabin.InverseTransformPoint(mf.transform.TransformPoint(vertices[i]));
                    points.Add(p);
                    if (!any)
                    {
                        local = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        local.Encapsulate(p);
                    }

                    top = Mathf.Max(top, p.y);
                }
            }

            if (!any) return false;
            float scale = Mathf.Max(0.01f, cabin.lossyScale.y);
            float band = 0.3f / scale;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (Vector3 p in points)
            {
                if (p.y < top - band) continue;
                sum += p;
                n++;
            }

            Vector3 peak = n > 0 ? sum / n : new Vector3(local.center.x, top, local.center.z);
            chimney = new Vector3(peak.x, top + 0.15f / scale, peak.z);

            Vector3 centreWorld = cabin.TransformPoint(local.center);
            Vector3 toward = (door != null && (door.position - centreWorld).sqrMagnitude < 15f * 15f ? door.position : settlementCentre) - centreWorld;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.01f) toward = cabin.forward;
            Vector3 dir = cabin.InverseTransformDirection(toward.normalized);
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            float half = Mathf.Abs(dir.x) * local.extents.x + Mathf.Abs(dir.z) * local.extents.z;
            float horizontalScale = Mathf.Max(0.01f, cabin.lossyScale.x);
            Vector3 centre = local.center;
            light = new Vector3(centre.x, local.min.y + 1.4f / scale, centre.z) + dir * (half + 0.45f / horizontalScale);
            return true;
        }

        // ------------------------------------------------------------------ helpers

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static GameObject Load(string path, StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) log.Append($"\n  WARNING: {path} not found.");
            return prefab;
        }
    }
}
