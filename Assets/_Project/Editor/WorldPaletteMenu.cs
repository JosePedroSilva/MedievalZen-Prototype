using System.Collections.Generic;
using System.IO;
using MedievalZen.World;
using UnityEditor;
using UnityEngine;

namespace MedievalZen.EditorTools
{
    /// <summary>
    /// Creates / refreshes the default <see cref="WorldPalette"/> from <see cref="SyntyCatalog"/>: loads every family's
    /// prefabs, wraps the grass/flower LOD prefabs into single-mesh detail prefabs and the rapeseed models into
    /// prefabs (under <see cref="SyntyCatalog.GeneratedPrefabFolder"/>), measures fence/wall pieces, and assigns the
    /// Synty terrain layers, water plane, background hills and windmill blade controllers. Missing assets are logged,
    /// never thrown.
    /// </summary>
    internal static class WorldPaletteMenu
    {
        /// <summary>Path of the default palette asset.</summary>
        public const string DefaultPalettePath = WorldSettingsMenu.SettingsFolder + "/WorldPalette.asset";

        private const string DetailFolder = SyntyCatalog.GeneratedPrefabFolder + "/Details";

        [MenuItem("MedievalZen/Create Default World Palette")]
        private static void CreateDefaultWorldPaletteMenu()
        {
            WorldPalette palette = CreateOrUpdateDefault();
            if (palette == null) return;
            Selection.activeObject = palette;
            EditorGUIUtility.PingObject(palette);
        }

        /// <summary>
        /// Loads the default palette, creating it if needed. An existing palette is only refreshed when its detail
        /// prefabs predate <see cref="WorldPalette.CurrentDetailVersion"/>.
        /// </summary>
        public static WorldPalette LoadOrCreateDefault()
        {
            var palette = AssetDatabase.LoadAssetAtPath<WorldPalette>(DefaultPalettePath);
            return palette != null && palette.detailVersion >= WorldPalette.CurrentDetailVersion ? palette : CreateOrUpdateDefault();
        }

        [InitializeOnLoadMethod]
        private static void RegisterStaleDetailUpgrader()
        {
            WorldPalette.StaleDetailUpgrader = UpgradeStaleDetails;
        }

        /// <summary>
        /// Called (via <see cref="WorldPalette.GetKeys"/>) before generating with a palette whose detail prefabs are
        /// stale: refreshes the default palette in place; other palettes get a warning.
        /// </summary>
        private static void UpgradeStaleDetails(WorldPalette palette)
        {
            if (palette == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string path = AssetDatabase.GetAssetPath(palette);
            if (path == DefaultPalettePath)
            {
                Debug.Log($"{DefaultPalettePath} predates detail pipeline v{WorldPalette.CurrentDetailVersion} (has v{palette.detailVersion}); refreshing it (rebuilding the grass/flower detail prefabs).", palette);
                CreateOrUpdateDefault();
            }
            else
            {
                Debug.LogWarning($"World palette {path} has stale detail prefabs (v{palette.detailVersion} < v{WorldPalette.CurrentDetailVersion}): grass may be mis-sized. " +
                                 "Rebuild it from the default (MedievalZen/Create Default World Palette) or re-measure its details.", palette);
            }
        }

        /// <summary>Creates the default palette or refills the existing one from the Synty catalog.</summary>
        public static WorldPalette CreateOrUpdateDefault()
        {
            var missing = new List<string>();
            var palette = AssetDatabase.LoadAssetAtPath<WorldPalette>(DefaultPalettePath);
            bool created = palette == null;
            if (created)
            {
                EnsureFolder(WorldSettingsMenu.SettingsFolder);
                palette = ScriptableObject.CreateInstance<WorldPalette>();
                AssetDatabase.CreateAsset(palette, DefaultPalettePath);
            }

            EnsureRapeseedPrefabs(missing);

            // Prefab families.
            palette.families = new List<PrefabFamily>();
            foreach (var (family, paths) in SyntyCatalog.Families)
            {
                palette.families.Add(new PrefabFamily(family, LoadAll(paths, missing)));
            }

            ConfigureLinePieces(palette);

            // Detail families: single-mesh copies of the LOD0 of each prefab (rebuilt every time), with their
            // native sizes so the builder can scale every family into its target height range.
            palette.details = new List<PrefabFamily>();
            var sizeLog = new System.Text.StringBuilder("Detail meshes (LOD0 bounds in metres):");
            foreach (var (family, paths) in SyntyCatalog.Details)
            {
                var prefabs = new List<GameObject>();
                var heights = new List<float>();
                var widths = new List<float>();
                foreach (GameObject source in LoadAll(paths, missing))
                {
                    GameObject detail = CreateDetailPrefab(source, sizeLog, out float height, out float width);
                    if (detail == null) continue;
                    prefabs.Add(detail);
                    heights.Add(height);
                    widths.Add(width);
                }

                palette.details.Add(new PrefabFamily(family, prefabs.ToArray()) { nativeHeights = heights.ToArray(), nativeWidths = widths.ToArray() });
            }

            palette.detailVersion = WorldPalette.CurrentDetailVersion;
            Debug.Log(sizeLog.ToString(), palette);

            // Terrain layers.
            var layers = new List<TerrainLayer>();
            foreach (string path in SyntyCatalog.TerrainLayers)
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null) missing.Add(path);
                else layers.Add(layer);
            }

            palette.terrainLayers = layers.ToArray();

            // Water, horizon, animation.
            palette.waterPlane = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCatalog.WaterPlane);
            if (palette.waterPlane == null) palette.waterPlane = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCatalog.WaterPlaneFallback);
            if (palette.waterPlane == null) missing.Add(SyntyCatalog.WaterPlane);
            palette.waterMaterial = Load<Material>(SyntyCatalog.WaterMaterial, missing);
            palette.backgroundHill = Load<GameObject>(SyntyCatalog.BackgroundHill, missing);
            palette.backgroundHillMaterial = Load<Material>(SyntyCatalog.BackgroundHillMaterial, missing);
            palette.rotateXController = Load<RuntimeAnimatorController>(SyntyCatalog.RotateXController, missing);
            palette.rotateZController = Load<RuntimeAnimatorController>(SyntyCatalog.RotateZController, missing);

            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();

            int prefabCount = 0;
            foreach (PrefabFamily family in palette.families) prefabCount += WorldPalette.ValidPrefabs(family).Count;
            int detailCount = 0;
            foreach (PrefabFamily family in palette.details) detailCount += WorldPalette.ValidPrefabs(family).Count;
            Debug.Log($"{(created ? "Created" : "Updated")} {DefaultPalettePath}: {palette.families.Count} families ({prefabCount} prefabs), " +
                      $"{palette.details.Count} detail families ({detailCount} prototypes), {palette.terrainLayers.Length} terrain layers. " +
                      $"Fence piece {palette.fencePieceLength:0.00} m, wall piece {palette.stoneWallLength:0.00} m.", palette);
            if (missing.Count > 0)
            {
                Debug.LogWarning($"World palette: {missing.Count} asset(s) not found (skipped):\n  " + string.Join("\n  ", missing), palette);
            }

            return palette;
        }

        // ------------------------------------------------------------------ line pieces

        /// <summary>
        /// Measures fence, wall, gate and arch meshes: the generator lays line pieces along +Z of their yaw and
        /// centres them on the placement point, so each family gets a yaw offset (−90° when the mesh's long axis is X)
        /// and a centred pivot. Fence variants whose length differs from the first by more than 20 % are dropped so
        /// runs stay seamless.
        /// </summary>
        private static void ConfigureLinePieces(WorldPalette palette)
        {
            PrefabFamily fence = palette.FindFamily(PropRole.Fence);
            if (fence != null)
            {
                var kept = new List<GameObject>();
                float reference = -1f;
                foreach (GameObject prefab in WorldPalette.ValidPrefabs(fence))
                {
                    float length = LongAxis(prefab, out _);
                    if (reference < 0f) reference = length;
                    if (Mathf.Abs(length - reference) <= reference * 0.2f) kept.Add(prefab);
                }

                if (kept.Count > 0)
                {
                    fence.prefabs = kept.ToArray();
                    palette.fencePieceLength = Mathf.Max(0.5f, reference * 0.98f);
                }
            }

            PrefabFamily wall = palette.FindFamily(PropRole.StoneWall);
            List<GameObject> walls = WorldPalette.ValidPrefabs(wall);
            if (walls.Count > 0) palette.stoneWallLength = Mathf.Max(0.5f, LongAxis(walls[0], out _) * 0.97f);

            foreach (string id in new[] { PropRole.Fence, PropRole.FenceGate, PropRole.StoneWall, PropRole.StoneWallSmall, PropRole.Gate })
            {
                PrefabFamily family = palette.FindFamily(id);
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(family);
                if (prefabs.Count == 0) continue;
                LongAxis(prefabs[0], out bool alongX);
                family.yawOffset = alongX ? -90f : 0f;
                family.centerPivot = true;
            }

            // Arches: the placement yaw points along the road; the arch's wide axis must be across it (local X).
            PrefabFamily arch = palette.FindFamily(PropRole.StoneArch);
            List<GameObject> arches = WorldPalette.ValidPrefabs(arch);
            if (arches.Count > 0)
            {
                LongAxis(arches[0], out bool alongX);
                arch.yawOffset = alongX ? 0f : 90f;
                arch.centerPivot = true;
            }
        }

        /// <summary>Length of the longer horizontal axis of the prefab's mesh bounds.</summary>
        private static float LongAxis(GameObject prefab, out bool alongX)
        {
            Bounds b = WorldBuilder.LocalBounds(prefab);
            alongX = b.size.x >= b.size.z;
            return Mathf.Max(b.size.x, b.size.z);
        }

        // ------------------------------------------------------------------ generated prefabs

        private static void EnsureRapeseedPrefabs(List<string> missing)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SyntyCatalog.RapeseedMaterial);
            if (material == null) missing.Add(SyntyCatalog.RapeseedMaterial);
            EnsureFolder(SyntyCatalog.GeneratedPrefabFolder);
            foreach (string modelPath in SyntyCatalog.RapeseedModels)
            {
                string target = $"{SyntyCatalog.GeneratedPrefabFolder}/{Path.GetFileNameWithoutExtension(modelPath)}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(target) != null) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null)
                {
                    missing.Add(modelPath);
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                try
                {
                    if (material != null)
                    {
                        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                        {
                            var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                            for (int i = 0; i < materials.Length; i++) materials[i] = material;
                            renderer.sharedMaterials = materials;
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(instance, target);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        /// <summary>Base offset (m) beyond which a detail mesh is re-based so it does not hover above its pivot.</summary>
        private const float BaseTolerance = 0.01f;

        /// <summary>Detail meshes wider than this (m) are rigid patches that cannot follow the terrain; warned about.</summary>
        private const float MaxSensibleDetailWidth = 5f;

        /// <summary>
        /// Terrain details cannot use LOD groups: saves a copy of the prefab's LOD0 mesh + materials as a
        /// single-renderer prefab under <see cref="DetailFolder"/> (overwritten each time, so its GUID is kept). If
        /// the mesh's lowest vertex sits more than <see cref="BaseTolerance"/> above the pivot (it would hover over
        /// the ground), a re-based copy of the mesh is saved next to it (<c>&lt;name&gt;_Mesh.asset</c>) and used
        /// instead. Meshes that dip slightly below their pivot (all the Synty clumps: 3-12 cm) are kept as they are;
        /// the sunken base hides the clump's edge on uneven ground. Appends the bounds to <paramref name="log"/> and
        /// returns the visible height above the pivot and the largest horizontal extent.
        /// </summary>
        private static GameObject CreateDetailPrefab(GameObject source, System.Text.StringBuilder log, out float height, out float width)
        {
            height = 0f;
            width = 0f;
            EnsureFolder(DetailFolder);
            string target = $"{DetailFolder}/{source.name}_Detail.prefab";

            Renderer renderer = null;
            var lodGroup = source.GetComponentInChildren<LODGroup>();
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length > 0)
                {
                    foreach (Renderer r in lods[0].renderers)
                    {
                        if (r is MeshRenderer)
                        {
                            renderer = r;
                            break;
                        }
                    }
                }
            }

            if (renderer == null) renderer = source.GetComponentInChildren<MeshRenderer>(true);
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogWarning($"No mesh found in {source.name}; it cannot be a terrain detail.", source);
                return null;
            }

            Mesh mesh = filter.sharedMesh;
            Bounds original = mesh.bounds;
            string note = string.Empty;
            if (original.min.y > BaseTolerance)
            {
                mesh = SaveRebasedMesh(mesh, original.min.y, $"{DetailFolder}/{source.name}_Mesh.asset");
                note = $" -> re-based down by {original.min.y:0.00} m";
            }

            Bounds final = mesh.bounds;
            height = Mathf.Max(0.01f, final.max.y);
            width = Mathf.Max(final.size.x, final.size.z);
            log.Append($"\n  {source.name}: min.y {original.min.y:0.00}, max.y {original.max.y:0.00}, size {original.size.x:0.00} x {original.size.y:0.00} x {original.size.z:0.00}{note}");
            if (width > MaxSensibleDetailWidth)
            {
                Debug.LogWarning($"Detail mesh {source.name} is {width:0.0} m wide: a rigid patch that cannot follow the terrain (it will float/sink). " +
                                 "Remove it from SyntyCatalog.Details or scatter it as a prefab.", source);
            }

            var go = new GameObject(source.name + "_Detail");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                return PrefabUtility.SaveAsPrefabAsset(go, target);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Saves (or overwrites) a copy of <paramref name="mesh"/> shifted down by <paramref name="baseY"/>.</summary>
        private static Mesh SaveRebasedMesh(Mesh mesh, float baseY, string path)
        {
            Mesh copy = Object.Instantiate(mesh);
            copy.name = Path.GetFileNameWithoutExtension(path);
            Vector3[] vertices = copy.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].y -= baseY;
            copy.vertices = vertices;
            copy.RecalculateBounds();

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(copy, path);
                return copy;
            }

            EditorUtility.CopySerialized(copy, existing);
            existing.name = copy.name;
            Object.DestroyImmediate(copy);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ------------------------------------------------------------------ helpers

        private static GameObject[] LoadAll(string[] paths, List<string> missing)
        {
            var result = new List<GameObject>(paths.Length);
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) missing.Add(path);
                else result.Add(prefab);
            }

            return result.ToArray();
        }

        private static T Load<T>(string path, List<string> missing) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) missing.Add(path);
            return asset;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
