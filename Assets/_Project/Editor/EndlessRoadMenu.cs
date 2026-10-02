using System.Collections.Generic;
using System.Text;
using MedievalZen.Endless;
using MedievalZen.Player;
using MedievalZen.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// <c>MedievalZen/Endless Road/Setup Scene</c>: builds <see cref="ScenePath"/> from scratch — the
    /// <see cref="EndlessWorld"/> streamer (with the <see cref="EndlessSettings"/> asset, created at
    /// <see cref="SettingsPath"/> if missing, the default WorldPalette, built if missing, and the far-tile material at
    /// <see cref="FarMaterialPath"/>), a GameWorld for travel, the PlayerRig, sun + day cycle + the player-following sky,
    /// post-processing, weather, the atmosphere (<see cref="AtmosphereMenu.ApplyToScene"/>), the HUD and ambient effects — the
    /// finite demo scene's building blocks, without a terrain (tiles are generated while walking). Run on an existing scene it
    /// offers to rebuild it or only repair its references. Either way every reference is verified after saving by reopening
    /// the scene. <c>Open Scene</c> opens it; <c>Forget Saved Journey</c> drops the saved journey; <c>Reset Safety Level</c>
    /// returns the self-diagnosing safety ladder to everything on. Demo.unity is not touched.
    /// </summary>
    /// <remarks>
    /// Why earlier scenes had no settings: the assets were loaded BEFORE <c>EditorSceneManager.NewScene(…, Single)</c>, which
    /// unloads unused assets. The C# references survived as unloaded wrappers; assigning them through
    /// <c>SerializedProperty.objectReferenceValue</c> stored null (the GameWorld's palette, set as a plain C# field, survived).
    /// Assets are now loaded after the new scene exists, and the result is checked.
    /// </remarks>
    internal static class EndlessRoadMenu
    {
        /// <summary>Where the endless-road scene is saved.</summary>
        public const string ScenePath = "Assets/_Project/Scenes/EndlessRoad.unity";

        /// <summary>The endless-road settings asset.</summary>
        public const string SettingsPath = "Assets/_Project/Settings/EndlessSettings.asset";

        /// <summary>The far-tile material (MedievalZen/FarTerrain shader).</summary>
        public const string FarMaterialPath = "Assets/_Project/Materials/Endless/FarTerrain.mat";

        /// <summary>Shader of the far-tile material.</summary>
        public const string FarShaderName = "MedievalZen/FarTerrain";

        [MenuItem("MedievalZen/Endless Road/Setup Scene", priority = 30)]
        private static void SetupSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                int choice = EditorUtility.DisplayDialogComplex("Endless Road",
                    $"{ScenePath} exists.\n\nRebuild it from scratch, or only repair its references (settings, palette, far-tile material, prefabs)?",
                    "Rebuild", "Cancel", "Repair references");
                if (choice == 1) return;
                if (choice == 2)
                {
                    RepairScene();
                    return;
                }
            }

            SetupScene();
        }

        [MenuItem("MedievalZen/Endless Road/Open Scene", priority = 31)]
        private static void OpenSceneMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                if (EditorUtility.DisplayDialog("Endless Road", $"{ScenePath} does not exist yet. Build it now?", "Setup Scene", "Cancel"))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) SetupScene();
                }

                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("MedievalZen/Endless Road/Forget Saved Journey", priority = 32)]
        private static void ForgetJourneyMenu()
        {
            PlayerPrefs.DeleteKey(EndlessJourney.PrefsKey);
            PlayerPrefs.Save();
            Debug.Log($"Endless road: the saved journey ({EndlessJourney.PrefsKey}) was deleted; the next Play starts at the beginning of the settings' seed.");
        }

        [MenuItem("MedievalZen/Endless Road/Reset Safety Level", priority = 33)]
        private static void ResetSafetyLevelMenu()
        {
            EndlessRunFile.Reset();
            Debug.Log($"Endless road: safety level reset to 0 (everything on) in {EndlessRunFile.FilePath}.");
        }

        [MenuItem("MedievalZen/Endless Road/Setup Scene", true)]
        [MenuItem("MedievalZen/Endless Road/Open Scene", true)]
        [MenuItem("MedievalZen/Endless Road/Reset Safety Level", true)]
        private static bool ValidateNotPlaying() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Builds and saves the scene (no prompts), then verifies it.</summary>
        public static bool SetupScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSetupMenu.PrefabPath);
            if (prefab == null) prefab = PlayerSetupMenu.CreatePlayerRigPrefab();
            if (prefab == null)
            {
                Debug.LogError("Endless Road: the PlayerRig prefab is not available; the scene was not built.");
                return false;
            }

            // NewScene(Single) unloads every unused asset: load or create the assets only after it (see the class remarks).
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!LoadReferences(out EndlessSettings settings, out WorldPalette palette, out Material farMaterial)) return false;
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSetupMenu.PrefabPath);

            var world = new GameObject("GameWorld").AddComponent<GameWorld>();
            world.Palette = palette;

            var endless = new GameObject("EndlessWorld").AddComponent<EndlessWorld>();
            AssignReferences(endless, settings, palette, farMaterial);

            // The streamer puts the player on the road at Play; until then the rig waits above the scene origin.
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.transform.SetPositionAndRotation(new Vector3(0f, 120f, 0f), Quaternion.identity);
            DemoSceneMenu.SetupPlayer(player);

            Light sun = DemoSceneMenu.FindSun();
            (Renderer[] sky, Renderer[] clouds, Renderer[] fog) = DemoSceneMenu.CreateSky(player.transform);
            DemoSceneMenu.CreateDayCycle(sun, sky, clouds, fog);
            DemoSceneMenu.CreatePostProcessing();
            DemoSceneMenu.CreateWeather();
            DemoSceneMenu.CreateUi();
            DemoSceneMenu.CreateAmbientFx(player.transform);
            Debug.Log(AtmosphereMenu.ApplyToScene(player.transform));

            EditorSceneManager.MarkSceneDirty(scene);
            WorldPaletteMenu.EnsureFolder(ScenePath.Substring(0, ScenePath.LastIndexOf('/')));
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) return false;
            bool ok = Verify();
            Debug.Log($"Created {ScenePath} (generator: {(EndlessGeneratorFactory.RealGeneratorExists ? "the real one" : "the stub until the real one exists")}){(ok ? ", references verified" : " — SEE THE ERRORS ABOVE")}. " +
                      "Press Play: WASD walk, T walk to the next inn, Esc journey menu (The road: Go now / New journey), F3 streaming overlay, F4 force an origin shift.");
            return ok;
        }

        /// <summary>Opens the existing scene, (re)assigns every reference of the streamer and the GameWorld, saves and verifies.</summary>
        public static bool RepairScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!LoadReferences(out EndlessSettings settings, out WorldPalette palette, out Material farMaterial)) return false;

            var endless = Object.FindFirstObjectByType<EndlessWorld>();
            if (endless == null)
            {
                endless = new GameObject("EndlessWorld").AddComponent<EndlessWorld>();
                Debug.LogWarning($"Endless Road: {ScenePath} had no EndlessWorld; added one.");
            }

            var world = Object.FindFirstObjectByType<GameWorld>();
            if (world == null) world = new GameObject("GameWorld").AddComponent<GameWorld>();
            var worldObject = new SerializedObject(world);
            SerializedProperty worldPalette = worldObject.FindProperty("palette");
            if (worldPalette != null) worldPalette.objectReferenceValue = palette;
            worldObject.ApplyModifiedPropertiesWithoutUndo();

            AssignReferences(endless, settings, palette, farMaterial);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) return false;
            bool ok = Verify();
            Debug.Log($"Repaired {ScenePath}{(ok ? "; references verified" : " — SEE THE ERRORS ABOVE")}.");
            return ok;
        }

        /// <summary>Loads (or creates) the settings asset, the default palette and the far-tile material.</summary>
        private static bool LoadReferences(out EndlessSettings settings, out WorldPalette palette, out Material farMaterial)
        {
            settings = LoadOrCreateSettings();
            palette = WorldPaletteMenu.LoadOrCreateDefault();
            farMaterial = LoadOrCreateFarMaterial();
            if (palette == null)
            {
                Debug.LogError("Endless Road: no WorldPalette; the scene was not built.");
                return false;
            }

            return true;
        }

        private static void AssignReferences(EndlessWorld endless, EndlessSettings settings, WorldPalette palette, Material farMaterial)
        {
            var so = new SerializedObject(endless);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("palette").objectReferenceValue = palette;
            so.FindProperty("farMaterial").objectReferenceValue = farMaterial;
            so.FindProperty("smokePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(AtmosphereMenu.SmokePrefabPath);
            so.FindProperty("firePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(AtmosphereMenu.FirePrefabPath);
            FillChimneys(so.FindProperty("cabinChimneys"), palette);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(endless);
        }

        /// <summary>
        /// Reopens the saved scene and checks every reference the streamer needs; logs an error naming each missing one.
        /// True when all are set.
        /// </summary>
        public static bool Verify()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var missing = new List<string>();
            var endless = Object.FindFirstObjectByType<EndlessWorld>();
            if (endless == null)
            {
                missing.Add("the EndlessWorld component itself");
            }
            else
            {
                var so = new SerializedObject(endless);
                Check(so, "settings", SettingsPath, missing);
                Check(so, "palette", WorldPaletteMenu.DefaultPalettePath, missing);
                Check(so, "farMaterial", FarMaterialPath, missing);
                Check(so, "smokePrefab", AtmosphereMenu.SmokePrefabPath, missing);
                Check(so, "firePrefab", AtmosphereMenu.FirePrefabPath, missing);
                var material = so.FindProperty("farMaterial").objectReferenceValue as Material;
                if (material != null && (material.shader == null || material.shader.name != FarShaderName))
                {
                    missing.Add($"farMaterial's shader ({(material.shader != null ? material.shader.name : "none")} instead of {FarShaderName})");
                }
            }

            var world = Object.FindFirstObjectByType<GameWorld>();
            if (world == null) missing.Add("the GameWorld");
            else if (world.Palette == null) missing.Add("GameWorld.palette");
            if (Object.FindFirstObjectByType<PlayerController>() == null) missing.Add("the PlayerRig (PlayerController)");

            if (missing.Count == 0) return true;
            var text = new StringBuilder($"Endless Road: {ScenePath} was saved but these references are MISSING after reloading it:");
            foreach (string m in missing) text.Append("\n  - ").Append(m);
            text.Append("\nRe-run MedievalZen/Endless Road/Setup Scene (Repair references); if it persists, check that the assets above exist and compile errors are cleared.");
            Debug.LogError(text.ToString());
            return false;
        }

        private static void Check(SerializedObject so, string property, string expectedPath, List<string> missing)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                missing.Add($"EndlessWorld.{property} (no such field)");
                return;
            }

            if (p.objectReferenceValue == null)
            {
                missing.Add($"EndlessWorld.{property} (expected {expectedPath})");
            }
        }

        /// <summary>The endless-road settings asset (created with the defaults if missing).</summary>
        public static EndlessSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<EndlessSettings>(SettingsPath);
            if (settings != null) return settings;
            WorldPaletteMenu.EnsureFolder(SettingsPath.Substring(0, SettingsPath.LastIndexOf('/')));
            settings = ScriptableObject.CreateInstance<EndlessSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created {SettingsPath}", settings);
            return AssetDatabase.LoadAssetAtPath<EndlessSettings>(SettingsPath);
        }

        /// <summary>The far-tile material asset (created if missing; its shader is fixed if it is not MedievalZen/FarTerrain).</summary>
        public static Material LoadOrCreateFarMaterial()
        {
            Shader shader = Shader.Find(FarShaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(FarMaterialPath);
            if (shader == null)
            {
                Debug.LogError($"Endless Road: shader '{FarShaderName}' was not found (Assets/_Project/Shaders/FarTerrain.shader not imported, or it failed to compile — see the shader in the Project window); {FarMaterialPath} {(material != null ? "kept as is" : "not created")}.");
                return material;
            }

            if (material == null)
            {
                WorldPaletteMenu.EnsureFolder(FarMaterialPath.Substring(0, FarMaterialPath.LastIndexOf('/')));
                material = new Material(shader) { name = "FarTerrain" };
                AssetDatabase.CreateAsset(material, FarMaterialPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Created {FarMaterialPath} ({FarShaderName}).");
                material = AssetDatabase.LoadAssetAtPath<Material>(FarMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }

            return material;
        }

        /// <summary>Measures the chimney top of every cabin prefab in the palette (for the inns' chimney smoke at runtime).</summary>
        private static void FillChimneys(SerializedProperty array, WorldPalette palette)
        {
            array.ClearArray();
            var seen = new HashSet<GameObject>();
            foreach (GameObject cabin in WorldPalette.ValidPrefabs(palette.FindFamily(PropRole.Cabin)))
            {
                if (!seen.Add(cabin)) continue;
                if (!AtmosphereMenu.CabinAnchors(cabin.transform, Vector3.zero, null, out Vector3 chimney, out _)) continue;
                array.InsertArrayElementAtIndex(array.arraySize);
                SerializedProperty entry = array.GetArrayElementAtIndex(array.arraySize - 1);
                entry.FindPropertyRelative("prefab").objectReferenceValue = cabin;
                entry.FindPropertyRelative("chimneyLocal").vector3Value = chimney;
            }
        }
    }
}
