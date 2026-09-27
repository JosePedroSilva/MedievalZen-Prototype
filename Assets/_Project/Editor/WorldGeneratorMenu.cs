using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// <c>MedievalZen/Generate World</c>, <c>Regenerate With New Seed</c> and <c>Clear Generated World</c>: generate
    /// the world from the <see cref="WorldSettings"/> / <see cref="WorldPalette"/> assets into the open scene's
    /// <see cref="GameWorld"/> (created if missing), then make the scene playable: a PlayerRig on the road outside the
    /// first town and a warm sun if the scene has none.
    /// </summary>
    internal static class WorldGeneratorMenu
    {
        private const string ProgressTitle = "MedievalZen – Generate World";

        [MenuItem("MedievalZen/Generate World", priority = 0)]
        private static void GenerateWorldMenu() => Generate(false);

        [MenuItem("MedievalZen/Regenerate With New Seed", priority = 1)]
        private static void RegenerateWithNewSeedMenu() => Generate(true);

        [MenuItem("MedievalZen/Clear Generated World", priority = 2)]
        private static void ClearGeneratedWorldMenu()
        {
            var world = Object.FindFirstObjectByType<GameWorld>();
            if (world == null)
            {
                Debug.Log("No GameWorld in the open scene; nothing to clear.");
                return;
            }

            WorldBuilder.Clear(world);
            EditorUtility.SetDirty(world);
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            Debug.Log("Cleared the generated world.", world);
        }

        [MenuItem("MedievalZen/Generate World", true)]
        [MenuItem("MedievalZen/Regenerate With New Seed", true)]
        [MenuItem("MedievalZen/Clear Generated World", true)]
        private static bool ValidateNotPlaying() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Generates (optionally with a fresh seed) and builds the world into the open scene.</summary>
        public static void Generate(bool newSeed) => GenerateWorld(FindOrCreateWorld(), newSeed);

        /// <summary>
        /// Generates and builds the world into <paramref name="world"/> (settings/palette taken from it, or the default
        /// assets), then places the PlayerRig, a sun and fog if the scene has none. Returns true on success. Shared by the
        /// Generate World menu and <c>MedievalZen/Setup Demo Scene</c>.
        /// </summary>
        public static bool GenerateWorld(GameWorld world, bool newSeed = false)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            WorldSettings settings = world.Settings != null ? world.Settings : LoadOrCreateSettings();
            WorldPalette palette = world.Palette != null ? world.Palette : WorldPaletteMenu.LoadOrCreateDefault();
            if (settings == null || palette == null)
            {
                Debug.LogError("Generate World needs a WorldSettings and a WorldPalette asset.");
                return false;
            }

            // WorldSettings.asset created before the dramatic-relief redesign still holds the old 50 m relief,
            // which flattens the new mountains into a bland plain. Upgrade that exact legacy value once.
            if (Mathf.Approximately(settings.hillAmplitude, 50f))
            {
                Undo.RecordObject(settings, "Upgrade world relief");
                settings.hillAmplitude = 240f;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log("Upgraded legacy WorldSettings: total relief (hillAmplitude) 50 m → 240 m, as in Docs/DESIGN.md.", settings);
            }

            if (settings.MigrateReliefDefaults())
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log($"Upgraded {AssetDatabase.GetAssetPath(settings)} to the dramatic-relief defaults (Hill Amplitude = total relief = {settings.hillAmplitude:0} m).", settings);
            }

            if (newSeed)
            {
                Undo.RecordObject(settings, "New world seed");
                settings.seed = new System.Random(System.Environment.TickCount).Next(1, 1000000);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            var total = Stopwatch.StartNew();
            try
            {
                WorldBlueprint blueprint = WorldGenerator.Generate(settings, palette.GetKeys(),
                    (stage, t) => EditorUtility.DisplayProgressBar(ProgressTitle, $"Generating: {stage}", t * 0.45f));
                double generateMs = total.Elapsed.TotalMilliseconds;

                List<(string stage, double milliseconds)> build = WorldBuilder.Build(world, settings, palette, blueprint,
                    (stage, t) => EditorUtility.DisplayProgressBar(ProgressTitle, $"Building: {stage}", 0.45f + 0.5f * t));

                EditorUtility.DisplayProgressBar(ProgressTitle, "Player and light", 0.97f);
                EditorUtility.SetDirty(world);
                PlacePlayer(world, blueprint);
                EnsureSun();
                EnsureFog(settings.worldSize);
                EditorSceneManager.MarkSceneDirty(world.gameObject.scene);

                var log = new StringBuilder();
                log.Append($"Generated world seed {settings.seed} in {total.Elapsed.TotalSeconds:0.0} s ")
                   .Append($"(generation {generateMs / 1000.0:0.0} s, build {(total.Elapsed.TotalMilliseconds - generateMs) / 1000.0:0.0} s).\n")
                   .Append(blueprint.Summary());
                foreach (var (stage, ms) in build) log.Append($"\n  build / {stage}: {ms:0} ms");
                foreach (Landmark landmark in blueprint.Landmarks) log.Append($"\n  landmark: {landmark}");
                foreach (Settlement settlement in blueprint.Roads.Settlements) log.Append($"\n  {settlement} at {settlement.Position}");
                Debug.Log(log.ToString(), world);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static GameWorld FindOrCreateWorld()
        {
            var world = Object.FindFirstObjectByType<GameWorld>();
            if (world != null) return world;
            var go = new GameObject("GameWorld");
            Undo.RegisterCreatedObjectUndo(go, "Create GameWorld");
            return go.AddComponent<GameWorld>();
        }

        /// <summary>The default WorldSettings asset (created if missing).</summary>
        public static WorldSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldSettingsMenu.DefaultSettingsPath);
            if (settings != null) return settings;
            WorldPaletteMenu.EnsureFolder(WorldSettingsMenu.SettingsFolder);
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssetDatabase.CreateAsset(settings, WorldSettingsMenu.DefaultSettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created {WorldSettingsMenu.DefaultSettingsPath}", settings);
            return settings;
        }

        /// <summary>
        /// Puts the player on the road just outside the first town, facing the town. Instantiates the PlayerRig
        /// prefab when the scene has no <see cref="PlayerController"/>; otherwise moves the existing one.
        /// </summary>
        private static void PlacePlayer(GameWorld world, WorldBlueprint blueprint)
        {
            RoadNetwork network = blueprint.Roads;
            Settlement start = null;
            foreach (Settlement settlement in network.Settlements)
            {
                if (settlement.Node.Segments.Count == 0) continue;
                if (start == null || (settlement.Type == SettlementType.Town && start.Type != SettlementType.Town) ||
                    (settlement.Type == start.Type && settlement.Node.Segments.Count > start.Node.Segments.Count))
                {
                    start = settlement;
                }
            }

            if (start == null) return;
            RoadSegment road = start.Node.Segments[0];
            bool fromA = road.A == start.Node;
            float along = Mathf.Min(start.Radius + 15f, road.Length * 0.5f);
            float distance = fromA ? along : road.Length - along;
            Vector3 position = world.transform.TransformPoint(road.Evaluate(distance)) + Vector3.up * 0.1f;
            Vector3 heading = road.Tangent(distance) * (fromA ? -1f : 1f);   // back towards the town
            heading.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.forward);

            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                Undo.RecordObject(player.transform, "Move player");
                player.transform.SetPositionAndRotation(position, rotation);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSetupMenu.PrefabPath);
            if (prefab == null) prefab = PlayerSetupMenu.CreatePlayerRigPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("PlayerRig prefab not available; add a player manually.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, world.gameObject.scene);
            instance.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(instance, "Create player");

            // See the horizon ring from anywhere on the map.
            foreach (Camera camera in instance.GetComponentsInChildren<Camera>(true))
            {
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 4000f);
            }

            // A second camera/listener (e.g. the default "Main Camera") would fight the rig's.
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (camera.transform.IsChildOf(instance.transform) || !camera.gameObject.activeSelf) continue;
                Undo.RecordObject(camera.gameObject, "Disable scene camera");
                camera.gameObject.SetActive(false);
                Debug.Log($"Disabled scene camera '{camera.name}' so the PlayerRig camera is used.", camera);
            }

            Debug.Log($"Placed the PlayerRig on the road outside {start.Name}. Press Play (T = auto-travel, V = camera).", instance);
        }

        private static void EnsureSun()
        {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) return;
            }

            var sun = new GameObject("Sun");
            Undo.RegisterCreatedObjectUndo(sun, "Create sun");
            var directional = sun.AddComponent<Light>();
            directional.type = LightType.Directional;
            directional.color = new Color(1f, 0.92f, 0.78f);
            directional.intensity = 1.3f;
            directional.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            RenderSettings.sun = directional;
        }

        private static void EnsureFog(float worldSize)
        {
            if (RenderSettings.fog) return;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.86f);
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = Mathf.Max(1500f, worldSize * 1.4f);
        }
    }
}
