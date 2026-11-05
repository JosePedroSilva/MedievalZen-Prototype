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

        /// <summary>First-party copies of the Synty foliage materials used by the sward details (no shadow pass, sward colours).</summary>
        private const string DetailMaterialFolder = "Assets/_Project/Materials/Details";

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
            // Refresh a stale default palette once after scripts load (not in Play mode), so new detail and ground looks
            // appear without running the menu by hand.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var palette = AssetDatabase.LoadAssetAtPath<WorldPalette>(DefaultPalettePath);
                if (palette != null && palette.detailVersion < WorldPalette.CurrentDetailVersion) UpgradeStaleDetails(palette);
            };
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
            MeasureRockMetrics(palette);
            LogLandmarkFamilies(palette);
            LogForestFamilies(palette);
            LogPassFamilies(palette);
            LogVillageFamilies(palette);

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
                    GameObject detail = CreateDetailPrefab(source, family, sizeLog, out float height, out float width);
                    if (detail == null) continue;
                    prefabs.Add(detail);
                    heights.Add(height);
                    widths.Add(width);
                }

                palette.details.Add(new PrefabFamily(family, prefabs.ToArray()) { nativeHeights = heights.ToArray(), nativeWidths = widths.ToArray() });
            }

            palette.detailVersion = WorldPalette.CurrentDetailVersion;
            Debug.Log(sizeLog.ToString(), palette);

            // Terrain layers (the rock channel is the first-party RockWall layer, created here from the Meadow textures; the grass
            // channels are first-party tinted copies of the Meadow grass layers, see GroundLook).
            EnsureRockWallLayer(missing);
            EnsureTintedGrassLayers(missing);
            var layers = new List<TerrainLayer>();
            var byChannel = new TerrainLayer[SyntyCatalog.TerrainLayers.Length];
            for (int channel = 0; channel < SyntyCatalog.TerrainLayers.Length; channel++)
            {
                string path = SyntyCatalog.TerrainLayers[channel];
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null && path == SyntyCatalog.RockWallLayer)
                {
                    Debug.LogWarning($"{SyntyCatalog.RockWallLayer} could not be created; using {SyntyCatalog.RockFallbackLayer} for rock.");
                    layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(SyntyCatalog.RockFallbackLayer);
                }

                if (layer == null && SyntyCatalog.TintedLayerSources.TryGetValue(path, out string source))
                {
                    Debug.LogWarning($"{path} could not be created; using the untinted {source}.");
                    layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(source);
                }

                if (layer == null) missing.Add(path);
                else layers.Add(layer);
                byChannel[channel] = layer;
            }

            palette.terrainLayers = layers.ToArray();
            palette.endlessTerrainLayers = EnsureMountainLayers(missing);
            palette.endlessMudLayer = EnsureMattMudLayer(missing);

            // Water, horizon, animation.
            palette.waterPlane = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCatalog.WaterPlane);
            if (palette.waterPlane == null) palette.waterPlane = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCatalog.WaterPlaneFallback);
            if (palette.waterPlane == null) missing.Add(SyntyCatalog.WaterPlane);
            palette.waterMaterial = Load<Material>(SyntyCatalog.WaterMaterial, missing);
            // The endless road's streams: first-party water and bank materials (the banks use the terrain's own grass, mud and dirt).
            palette.streamMaterial = EnsureStreamWaterMaterial(missing);
            palette.streamBankMaterial = EnsureStreamBankMaterial(byChannel, missing);
            palette.bridgeMaterial = EnsureBridgeMaterial(missing);
            palette.terraceDeckMaterial = EnsureTerraceDeckMaterial(byChannel[TerrainShaper.CobbleLayer], missing);
            palette.shadowProxyMaterial = EnsureShadowProxyMaterial(missing);
            EnsureSignMaterials(palette, missing);
            palette.backgroundHill = Load<GameObject>(SyntyCatalog.BackgroundHill, missing);
            palette.backgroundHillMaterial = Load<Material>(SyntyCatalog.BackgroundHillMaterial, missing);
            palette.rotateXController = Load<RuntimeAnimatorController>(SyntyCatalog.RotateXController, missing);
            palette.rotateZController = Load<RuntimeAnimatorController>(SyntyCatalog.RotateZController, missing);

            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            VerifyStreamMaterials();

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

        // ------------------------------------------------------------------ inn name boards

        private const string SignMaterialFolder = "Assets/_Project/Materials/Signs";
        private const string SignWoodPath = SignMaterialFolder + "/SignWood.mat";
        private const string SignLetterPath = SignMaterialFolder + "/SignLetters.mat";
        private const string SimpleLitShader = "Universal Render Pipeline/Simple Lit";

        /// <summary>
        /// The endless inns' name boards (see <c>SettlementLife.PlaceNameBoard</c>): weathered light wood, and dark painted lettering
        /// (alpha-clipped; the runtime puts the built-in font's atlas in its base map). Lit, not emissive: dark at night but for the lamps.
        /// </summary>
        private static void EnsureSignMaterials(WorldPalette palette, List<string> missing)
        {
            Shader shader = Shader.Find(SimpleLitShader);
            if (shader == null)
            {
                missing.Add($"shader {SimpleLitShader}");
                return;
            }

            if (!AssetDatabase.IsValidFolder(SignMaterialFolder)) AssetDatabase.CreateFolder("Assets/_Project/Materials", "Signs");
            palette.signWoodMaterial = EnsureSignMaterial(SignWoodPath, shader, "SignWood", new Color(0.62f, 0.48f, 0.31f), false);
            palette.signLetterMaterial = EnsureSignMaterial(SignLetterPath, shader, "SignLetters", new Color(0.10f, 0.07f, 0.05f), true);
            Debug.Log($"Inn name boards: wood {(palette.signWoodMaterial != null ? SignWoodPath : "MISSING")}, lettering {(palette.signLetterMaterial != null ? SignLetterPath : "MISSING")} ({SimpleLitShader}).", palette);
        }

        private static Material EnsureSignMaterial(string path, Shader shader, string name, Color color, bool clip)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.05f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", clip ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_Cull", clip ? 0f : 2f);
            if (clip) material.EnableKeyword("_ALPHATEST_ON");
            else material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = clip ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ stream materials

        /// <summary>Folder of the first-party stream materials.</summary>
        private const string StreamMaterialFolder = "Assets/_Project/Materials/Streams";

        private const string StreamWaterPath = StreamMaterialFolder + "/StreamWater.mat";
        private const string StreamBankPath = StreamMaterialFolder + "/StreamBank.mat";
        private const string StreamWaterShader = "MedievalZen/StreamWater";
        private const string StreamBankShader = "MedievalZen/StreamBank";

        /// <summary>Loads or creates the material at <paramref name="path"/> with <paramref name="shaderName"/>; null (logged) when the shader is missing.</summary>
        private static Material EnsureMaterial(string path, string shaderName, string name, List<string> missing)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                missing.Add($"shader {shaderName}");
                return null;
            }

            if (ShaderUtil.ShaderHasError(shader)) Debug.LogError($"{shaderName} has compile errors; the streams will not draw correctly.", shader);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EnsureFolder(StreamMaterialFolder);
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
                Debug.Log($"Created {path} ({shaderName}).", material);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        /// <summary>The streams' water (<see cref="StreamWaterShader"/>, the shader's own colours).</summary>
        private static Material EnsureStreamWaterMaterial(List<string> missing)
        {
            Material material = EnsureMaterial(StreamWaterPath, StreamWaterShader, "StreamWater", missing);
            if (material == null) return null;
            material.renderQueue = -1;   // the shader's (Transparent-10)
            // The look of 2026-10-06 (an existing material keeps old values when only the shader's defaults change, so write them).
            material.SetColor("_ShallowColor", new Color(0.27f, 0.40f, 0.38f, 1f));   // sRGB values (a Color property)
            material.SetColor("_DeepColor", new Color(0.09f, 0.20f, 0.23f, 1f));
            material.SetColor("_StreakColor", new Color(0.45f, 0.60f, 0.60f, 1f));
            material.SetColor("_FoamColor", new Color(0.72f, 0.75f, 0.72f, 1f));
            material.SetFloat("_EdgeAlpha", 0.55f);
            material.SetFloat("_CentreAlpha", 0.93f);
            material.SetFloat("_SkyTint", 0.32f);
            material.SetFloat("_Glint", 0.6f);
            material.SetFloat("_FlowSpeed", 0.9f);
            material.SetFloat("_RippleScale", 0.9f);
            material.SetFloat("_RippleStrength", 0.55f);
            material.SetFloat("_StreakStrength", 0.55f);
            material.SetFloat("_EdgeFoam", 0.45f);
            Debug.Log($"{StreamWaterPath}: shallow {material.GetColor("_ShallowColor")}, deep {material.GetColor("_DeepColor")}, alpha {material.GetFloat("_EdgeAlpha")}–{material.GetFloat("_CentreAlpha")}, sky tint {material.GetFloat("_SkyTint")}.", material);
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The streams' banks (<see cref="StreamBankShader"/>): the terrain's lush grass (first-party tinted layer), mud and dirt
        /// layers, each with its texture, normal map, tile size and offset and its diffuse tint, so the drape over the ground matches
        /// the terrain it meets. Never touches the Synty assets.
        /// </summary>
        private static Material EnsureStreamBankMaterial(TerrainLayer[] byChannel, List<string> missing)
        {
            Material material = EnsureMaterial(StreamBankPath, StreamBankShader, "StreamBank", missing);
            if (material == null) return null;
            SetLayer(material, "_Grass", byChannel[TerrainShaper.LushGrassLayer], true, missing);
            SetLayer(material, "_Mud", byChannel[TerrainShaper.MudLayer], true, missing);
            SetLayer(material, "_Dirt", byChannel[TerrainShaper.DirtLayer], false, missing);
            material.SetVector("_EarthColor", new Vector4(2.4f, 1.9f, 1.35f, 1f));   // linear: earth brown from the mud layer
            material.SetFloat("_Broken", 0.8f);
            material.renderQueue = -1;
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        private const string BridgeSourceMaterial = "Assets/Synty/PolygonFantasyKingdom/Materials/Alts/PolygonFantasyKingdom_Mat_01_A.mat";
        private const string BridgeMaterialPath = StreamMaterialFolder + "/BridgeStone.mat";

        /// <summary>
        /// The endless road's stone bridges: a first-party copy of the Fantasy Kingdom palette material (the bridge's atlas cells average
        /// sRGB (90, 88, 83), 35 % darker than the houses' walls, and the side seen from the stream is usually in shade), with a lighter
        /// warm stone base colour (_BaseColor multiplies the atlas: sRGB about (113, 108, 99), as the tower's walls). Never edits Synty.
        /// </summary>
        private static Material EnsureBridgeMaterial(List<string> missing)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(BridgeSourceMaterial);
            if (source == null)
            {
                missing.Add(BridgeSourceMaterial);
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(BridgeMaterialPath);
            if (material == null)
            {
                EnsureFolder(StreamMaterialFolder);
                material = new Material(source) { name = "BridgeStone" };
                AssetDatabase.CreateAsset(material, BridgeMaterialPath);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            material.SetColor("_BaseColor", new Color(1.6f, 1.52f, 1.4f, 1f));
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(material);
            Debug.Log($"Bridge stone: {BridgeMaterialPath} ({material.shader.name}), base colour {material.GetColor("_BaseColor")} over {BridgeSourceMaterial}.", material);
            return material;
        }

        private const string TerraceDeckPath = "Assets/_Project/Materials/City/TerraceDeck.mat";

        /// <summary>
        /// The walled city's terrace decks (palette v24): URP/Lit with the first-party cobblestone terrain layer's texture, normal map, tint
        /// and tiling, so a deck (its UVs are tile-local metres, like the terrain's) matches the painted street it meets. Matt, as the
        /// terrain layer. Never touches the Synty assets.
        /// </summary>
        private static Material EnsureTerraceDeckMaterial(TerrainLayer cobble, List<string> missing)
        {
            if (cobble == null || cobble.diffuseTexture == null)
            {
                missing.Add("terrain layer for the terrace decks' cobblestone");
                return null;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                missing.Add("shader Universal Render Pipeline/Lit");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(TerraceDeckPath);
            if (material == null)
            {
                EnsureFolder("Assets/_Project/Materials/City");
                material = new Material(shader) { name = "TerraceDeck" };
                AssetDatabase.CreateAsset(material, TerraceDeckPath);
            }
            else if (material.shader != shader) material.shader = shader;

            Vector2 size = new Vector2(Mathf.Max(0.01f, cobble.tileSize.x), Mathf.Max(0.01f, cobble.tileSize.y));
            material.SetTexture("_BaseMap", cobble.diffuseTexture);
            material.SetTextureScale("_BaseMap", new Vector2(1f / size.x, 1f / size.y));
            material.SetTextureOffset("_BaseMap", new Vector2(cobble.tileOffset.x / size.x, cobble.tileOffset.y / size.y));
            Vector4 remap = cobble.diffuseRemapMax;
            // The remap is a linear multiplier and Unity linearises Color properties: hand it over gamma-encoded (as the stream banks note).
            material.SetColor("_BaseColor", new Color(remap.x, remap.y, remap.z, 1f).gamma);
            if (cobble.normalMapTexture != null)
            {
                material.SetTexture("_BumpMap", cobble.normalMapTexture);
                material.SetFloat("_BumpScale", cobble.normalScale);
                material.EnableKeyword("_NORMALMAP");
            }
            else material.DisableKeyword("_NORMALMAP");

            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", Mathf.Min(0.2f, cobble.smoothness));
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            Debug.Log($"Terrace decks: {TerraceDeckPath} (URP/Lit) over {AssetDatabase.GetAssetPath(cobble)}: tile {size.x:0.##} x {size.y:0.##} m, tint {remap}, normal map {(cobble.normalMapTexture != null ? "yes" : "no")}.", material);
            return material;
        }

        private static void SetLayer(Material material, string prefix, TerrainLayer layer, bool normals, List<string> missing)
        {
            if (layer == null || layer.diffuseTexture == null)
            {
                missing.Add($"terrain layer for the stream banks' {prefix.TrimStart('_').ToLowerInvariant()}");
                return;
            }

            material.SetTexture(prefix + "Map", layer.diffuseTexture);
            Vector4 remap = layer.diffuseRemapMax;
            // A vector, not a colour: the remap is a linear multiplier, and Unity gamma-converts Color properties (the bank's grass
            // came out more saturated than the terrain's: the lime line along the streams).
            material.SetVector(prefix + "Tint", new Vector4(remap.x, remap.y, remap.z, 1f));
            material.SetVector(prefix + "Tile", new Vector4(layer.tileSize.x, layer.tileSize.y, layer.tileOffset.x, layer.tileOffset.y));
            if (!normals) return;
            material.SetTexture(prefix + "Normal", layer.normalMapTexture);   // null: the shader's flat default
            material.SetFloat(prefix + "NormalScale", layer.normalMapTexture != null ? layer.normalScale : 0f);
        }

        /// <summary>Re-reads the saved palette and checks its stream materials and their textures (logs an error for each problem).</summary>
        private static void VerifyStreamMaterials()
        {
            var saved = AssetDatabase.LoadAssetAtPath<WorldPalette>(DefaultPalettePath);
            if (saved == null) return;
            var problems = new List<string>();
            if (saved.streamMaterial == null) problems.Add("streamMaterial is not set");
            else if (saved.streamMaterial.shader == null || saved.streamMaterial.shader.name != StreamWaterShader) problems.Add($"streamMaterial does not use {StreamWaterShader}");
            if (saved.streamBankMaterial == null) problems.Add("streamBankMaterial is not set");
            else
            {
                if (saved.streamBankMaterial.shader == null || saved.streamBankMaterial.shader.name != StreamBankShader) problems.Add($"streamBankMaterial does not use {StreamBankShader}");
                foreach (string texture in new[] { "_GrassMap", "_MudMap", "_DirtMap" })
                {
                    if (saved.streamBankMaterial.GetTexture(texture) == null) problems.Add($"streamBankMaterial has no {texture}");
                }
            }

            if (problems.Count == 0) Debug.Log($"Stream materials verified: {AssetDatabase.GetAssetPath(saved.streamMaterial)}, {AssetDatabase.GetAssetPath(saved.streamBankMaterial)}.", saved);
            else Debug.LogError("World palette stream materials: " + string.Join("; ", problems), saved);
        }

        // ------------------------------------------------------------------ tinted grass terrain layers

        /// <summary>
        /// Creates (or refreshes) the first-party grass terrain layers: copies of the Meadow grass / flower-meadow layers (same
        /// textures, normals and tiling) whose <c>diffuseRemapMax</c> multiplies the texture toward its sward colour
        /// (<see cref="GroundLook.LayerTints"/>; URP Terrain Lit multiplies the sampled albedo by it), so the ground between and
        /// beyond the grass clumps has the clumps' colour. Never touches the Synty assets.
        /// </summary>
        private static void EnsureTintedGrassLayers(List<string> missing)
        {
            for (int channel = 0; channel < SyntyCatalog.TerrainLayers.Length; channel++)
            {
                string path = SyntyCatalog.TerrainLayers[channel];
                if (!SyntyCatalog.TintedLayerSources.TryGetValue(path, out string sourcePath)) continue;
                var source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(sourcePath);
                if (source == null)
                {
                    missing.Add(sourcePath);
                    continue;
                }

                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                bool create = layer == null;
                if (create) layer = new TerrainLayer();
                layer.diffuseTexture = source.diffuseTexture;
                layer.normalMapTexture = source.normalMapTexture;
                layer.maskMapTexture = source.maskMapTexture;
                layer.normalScale = source.normalScale;
                layer.tileSize = source.tileSize;
                layer.tileOffset = source.tileOffset;
                layer.specular = source.specular;
                layer.metallic = source.metallic;
                layer.smoothness = source.smoothness;
                layer.maskMapRemapMin = source.maskMapRemapMin;
                layer.maskMapRemapMax = source.maskMapRemapMax;
                Vector3 tint = GroundLook.LayerTints[channel];
                layer.diffuseRemapMin = Vector4.zero;
                layer.diffuseRemapMax = new Vector4(tint.x, tint.y, tint.z, 1f);
                if (create)
                {
                    EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
                    AssetDatabase.CreateAsset(layer, path);
                    Debug.Log($"Created {path} ({System.IO.Path.GetFileName(sourcePath)} tinted by {tint}).", layer);
                }
                else
                {
                    EditorUtility.SetDirty(layer);
                }
            }
        }

        // ------------------------------------------------------------------ the endless road's mountain layers

        /// <summary>
        /// Creates (or refreshes) the endless road's mountain layers (<see cref="SyntyCatalog.MountainLayers"/>): copies of the Meadow rock
        /// rough, gravel and grass-debris layers (textures and normals) at their own tile sizes, tinted by <see cref="GroundLook.LayerTints"/>
        /// to the grey mountain rock, the pale scree and the olive-brown heath, in channel order. Logs what it built and whether every layer
        /// resolved. Never touches the Synty assets.
        /// </summary>
        /// <summary>
        /// The endless road's mud layer (palette v21): the Synty mud layer's textures with metallic and smoothness 0, so mud in the
        /// open no longer mirrors the sky (<see cref="SyntyCatalog.EndlessMudLayer"/>). Null when the Synty layer is missing.
        /// </summary>
        private static TerrainLayer EnsureMattMudLayer(List<string> missing)
        {
            var source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(SyntyCatalog.MudSourceLayer);
            if (source == null || source.diffuseTexture == null)
            {
                missing.Add(SyntyCatalog.MudSourceLayer);
                return null;
            }

            string path = SyntyCatalog.EndlessMudLayer;
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            bool create = layer == null;
            if (create) layer = new TerrainLayer();
            layer.diffuseTexture = source.diffuseTexture;
            layer.normalMapTexture = source.normalMapTexture;
            layer.maskMapTexture = null;
            layer.normalScale = source.normalScale;
            layer.tileSize = source.tileSize;
            layer.tileOffset = source.tileOffset;
            layer.specular = source.specular;
            layer.metallic = 0f;
            layer.smoothness = 0f;
            layer.diffuseRemapMin = source.diffuseRemapMin;
            layer.diffuseRemapMax = source.diffuseRemapMax;
            if (create)
            {
                EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
                AssetDatabase.CreateAsset(layer, path);
            }
            else
            {
                EditorUtility.SetDirty(layer);
            }

            Debug.Log($"Endless road mud layer: {(create ? "created" : "updated")} {path} from {System.IO.Path.GetFileName(SyntyCatalog.MudSourceLayer)} " +
                      $"(metallic {source.metallic:0.##} to 0, smoothness {source.smoothness:0.##} to 0; the finite world keeps the Synty layer).");
            return layer;
        }

        private static TerrainLayer[] EnsureMountainLayers(List<string> missing)
        {
            var result = new TerrainLayer[SyntyCatalog.MountainLayers.Count];
            var log = new System.Text.StringBuilder("Endless road terrain layers (8-10 the pass ranges, 11-13 the farmland's field colours, 14 the woods' leaf litter; channel, asset, source, tile, tint, distant colour sRGB):");
            for (int i = 0; i < result.Length; i++)
            {
                var (path, sourcePath, tileSize) = SyntyCatalog.MountainLayers[i];
                int channel = TerrainShaper.LayerCount + i;
                var source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(sourcePath);
                if (source == null || source.diffuseTexture == null)
                {
                    missing.Add(sourcePath);
                    log.Append($"\n  {TerrainShaper.EndlessLayerNames[channel]}: NOT BUILT, {sourcePath} not found");
                    continue;
                }

                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                bool create = layer == null;
                if (create) layer = new TerrainLayer();
                layer.diffuseTexture = source.diffuseTexture;
                layer.normalMapTexture = source.normalMapTexture;
                layer.maskMapTexture = source.maskMapTexture;
                layer.normalScale = source.normalScale;
                layer.tileSize = new Vector2(tileSize, tileSize);
                layer.tileOffset = Vector2.zero;
                layer.specular = source.specular;
                layer.metallic = 0f;
                layer.smoothness = 0f;
                layer.maskMapRemapMin = source.maskMapRemapMin;
                layer.maskMapRemapMax = source.maskMapRemapMax;
                Vector3 tint = GroundLook.LayerTints[channel];
                layer.diffuseRemapMin = Vector4.zero;
                layer.diffuseRemapMax = new Vector4(tint.x, tint.y, tint.z, 1f);
                if (create)
                {
                    EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
                    AssetDatabase.CreateAsset(layer, path);
                }
                else
                {
                    EditorUtility.SetDirty(layer);
                }

                result[i] = layer;
                Vector3 c = GroundLook.LayerColor(channel);
                log.Append($"\n  {channel} {TerrainShaper.EndlessLayerNames[channel]}: {(create ? "created" : "updated")} {path} from {System.IO.Path.GetFileName(sourcePath)}, " +
                           $"{tileSize} m tiles, tint {tint}, colour ({Mathf.RoundToInt(Mathf.LinearToGammaSpace(c.x) * 255)}, {Mathf.RoundToInt(Mathf.LinearToGammaSpace(c.y) * 255)}, {Mathf.RoundToInt(Mathf.LinearToGammaSpace(c.z) * 255)})" +
                           (source.normalMapTexture == null ? " (no normal map)" : string.Empty));
            }

            int built = 0;
            foreach (TerrainLayer l in result) built += l != null ? 1 : 0;
            log.Append(built == result.Length ? "\n  all resolved" : $"\n  {result.Length - built} NOT RESOLVED: the pass ranges paint them with the shared rock / dry grass");
            if (built == result.Length) Debug.Log(log.ToString());
            else Debug.LogWarning(log.ToString());
            EnsureRockSkinMaterial(result.Length > 0 ? result[0] : null);
            return result;
        }

        /// <summary>Resources path of the endless road's rock skin material (<see cref="MedievalZen.Endless.RockSkin"/>).</summary>
        private const string RockSkinMaterialPath = "Assets/_Project/Resources/RockSkin.mat";

        /// <summary>
        /// Creates (or refreshes) the rock skin material (shader MedievalZen/RockSkin, 2026-10-06: opaque, faceted, flat colour per face from
        /// the mesh, no texture): white tint, smoothness 0 as the terrain layers, the shader's own queue. Needs the mountain rock layer only
        /// as proof the endless channels were built. Logs what it built.
        /// </summary>
        private static void EnsureRockSkinMaterial(TerrainLayer rock)
        {
            var shader = Shader.Find("MedievalZen/RockSkin");
            if (shader == null || rock == null)
            {
                Debug.LogWarning($"Endless road rock skin: NOT BUILT ({(shader == null ? "shader MedievalZen/RockSkin not found" : "no mountain rock layer")}); steep pass faces keep the terrain's own rock.");
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(RockSkinMaterialPath);
            bool create = mat == null;
            if (create) mat = new Material(shader) { name = "RockSkin" };
            mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f);
            mat.renderQueue = -1;   // the shader's (Geometry+10); the old skin was transparent
            mat.enableInstancing = true;
            if (create)
            {
                EnsureFolder(RockSkinMaterialPath.Substring(0, RockSkinMaterialPath.LastIndexOf('/')));
                AssetDatabase.CreateAsset(mat, RockSkinMaterialPath);
            }
            else
            {
                EditorUtility.SetDirty(mat);
            }

            Debug.Log($"Endless road rock skin: {(create ? "created" : "updated")} {RockSkinMaterialPath} (shader MedievalZen/RockSkin, faceted, opaque, " +
                      $"queue {mat.renderQueue}, casts shadows).");
        }

        /// <summary>Logs the endless road's pass rock families (prefabs found, triangles and bounds) and warns about any that resolved to nothing.</summary>
        private static void LogPassFamilies(WorldPalette palette)
        {
            var log = new System.Text.StringBuilder("Endless road mountain pass families (prefabs found / catalogued; triangles, bounds and measured metrics per prefab):");
            var empty = new List<string>();
            foreach (var (id, paths) in SyntyCatalog.Families)
            {
                if (!PropRole.IsPassRock(id)) continue;
                PrefabFamily family = palette.FindFamily(id);
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(family);
                log.Append($"\n  {id}: {prefabs.Count} / {paths.Length}{(prefabs.Count == paths.Length ? " all resolved" : prefabs.Count == 0 ? "  NOT FOUND" : "  SOME MISSING")}");
                if (prefabs.Count == 0) empty.Add(id);
                foreach (GameObject prefab in prefabs)
                {
                    int triangles = 0;
                    foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh != null) triangles += (int)(filter.sharedMesh.triangles.LongLength / 3);
                    }

                    PropMetrics m = family.MetricsOf(prefab);
                    Bounds b = WorldBuilder.LocalBounds(prefab);
                    log.Append($"\n    {prefab.name}: {triangles} triangles, {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m, metrics {(m.IsValid ? $"height {m.Height:0.00} m" : "MISSING")}");
                }
            }

            Debug.Log(log.ToString(), palette);
            if (empty.Count > 0) Debug.LogWarning("World palette: these endless-road pass rock families have no prefabs; the passes keep the Meadow rocks: " + string.Join(", ", empty), palette);
        }

        // ------------------------------------------------------------------ rock metrics and the rock terrain layer

        /// <summary>
        /// Creates (or refreshes) <see cref="SyntyCatalog.RockWallLayer"/>: the Meadow RockWall texture and normals that the Meadow
        /// rock and cliff meshes show on their sides, tiled every <see cref="SyntyCatalog.RockWallTileSize"/> m like their triplanar
        /// material, so painted rock matches the meshes. Never touches the Synty assets.
        /// </summary>
        private static void EnsureRockWallLayer(List<string> missing)
        {
            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(SyntyCatalog.RockWallTexture);
            var normals = AssetDatabase.LoadAssetAtPath<Texture2D>(SyntyCatalog.RockWallNormals);
            if (diffuse == null)
            {
                missing.Add(SyntyCatalog.RockWallTexture);
                return;
            }

            if (normals == null) missing.Add(SyntyCatalog.RockWallNormals);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(SyntyCatalog.RockWallLayer);
            bool create = layer == null;
            if (create) layer = new TerrainLayer();
            layer.diffuseTexture = diffuse;
            layer.normalMapTexture = normals;
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(SyntyCatalog.RockWallTileSize, SyntyCatalog.RockWallTileSize);
            layer.tileOffset = Vector2.zero;
            layer.smoothness = 0f;
            layer.metallic = 0f;
            if (create)
            {
                EnsureFolder(SyntyCatalog.RockWallLayer.Substring(0, SyntyCatalog.RockWallLayer.LastIndexOf('/')));
                AssetDatabase.CreateAsset(layer, SyntyCatalog.RockWallLayer);
                Debug.Log($"Created {SyntyCatalog.RockWallLayer} (RockWall_Texture_01 + RockWall_Normals_01, {SyntyCatalog.RockWallTileSize} m tiles).", layer);
            }
            else
            {
                EditorUtility.SetDirty(layer);
            }
        }

        /// <summary>
        /// Measures every rock, cliff, mound and cairn prefab (LOD0 meshes in prefab space) into
        /// <see cref="PrefabFamily.metrics"/> with <see cref="PropMetrics.FromTriangles"/>, and logs each prefab's size and any
        /// difference from the offline table (<see cref="PropMetricsTable"/>) larger than 5 cm.
        /// </summary>
        private static void MeasureRockMetrics(WorldPalette palette)
        {
            var log = new System.Text.StringBuilder("Rock metrics (prefab space, m):");
            int measured = 0, differing = 0;
            foreach (PrefabFamily family in palette.families)
            {
                if (family == null || !(PropRole.IsRock(family.id) || family.id == PropRole.StoneStack || family.id == PropRole.StonePile)) continue;
                family.metrics = new PropMetrics[family.prefabs.Length];
                for (int i = 0; i < family.prefabs.Length; i++)
                {
                    GameObject prefab = family.prefabs[i];
                    if (prefab == null) continue;
                    PropMetrics m = MeasurePrefab(prefab);
                    family.metrics[i] = m;
                    if (!m.IsValid)
                    {
                        log.Append($"\n  {family.id}/{prefab.name}: no readable mesh (the offline table will be used)");
                        continue;
                    }

                    measured++;
                    string note = string.Empty;
                    if (PropMetricsTable.TryGetForPath(AssetDatabase.GetAssetPath(prefab), out PropMetrics offline))
                    {
                        float diff = PropMetrics.BoundsDifference(m, offline);
                        if (diff > 0.05f)
                        {
                            differing++;
                            note = $"  <-- differs from offline by {diff:0.00} m (offline {offline})";
                        }
                    }
                    else
                    {
                        note = "  (not in the offline table)";
                    }

                    log.Append($"\n  {family.id}/{prefab.name}: {m}{note}");
                }
            }

            log.Insert(0, $"Measured {measured} rock-like prefabs; {differing} differ from the offline measurements by more than 5 cm. ");
            Debug.Log(log.ToString(), palette);
        }

        /// <summary>LOD0 (or all) mesh triangles of <paramref name="prefab"/> in its root space, measured.</summary>
        private static PropMetrics MeasurePrefab(GameObject prefab)
        {
            var renderers = new List<Renderer>();
            var lodGroup = prefab.GetComponentInChildren<LODGroup>();
            if (lodGroup != null && lodGroup.GetLODs().Length > 0)
            {
                foreach (Renderer r in lodGroup.GetLODs()[0].renderers)
                {
                    if (r != null) renderers.Add(r);
                }
            }

            if (renderers.Count == 0) renderers.AddRange(prefab.GetComponentsInChildren<MeshRenderer>(true));
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Mesh mesh = filter.sharedMesh;
                Vector3[] v = mesh.vertices;
                if (v == null || v.Length == 0) continue;
                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                int start = vertices.Count;
                foreach (Vector3 p in v) vertices.Add(m.MultiplyPoint3x4(p));
                foreach (int t in mesh.triangles) triangles.Add(start + t);
            }

            return vertices.Count > 0 ? PropMetrics.FromTriangles(vertices, triangles) : default;
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

            foreach (string id in new[] { PropRole.Fence, PropRole.FenceGate, PropRole.StoneWall, PropRole.StoneWallSmall, PropRole.Gate, PropRole.Hedge, PropRole.CityWall, PropRole.CityBattlement })
            {
                PrefabFamily family = palette.FindFamily(id);
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(family);
                if (prefabs.Count == 0) continue;
                LongAxis(prefabs[0], out bool alongX);
                family.yawOffset = alongX ? -90f : 0f;
                family.centerPivot = true;
            }

            // The hill town's pillars (palette v24): centred on their placement, instanced like the wall.
            PrefabFamily pillar = palette.FindFamily(PropRole.CityPillar);
            if (pillar != null)
            {
                pillar.yawOffset = 0f;
                pillar.centerPivot = true;
            }

            // The stair flight (palette v24): centred on its placement, turned so it climbs along the placement's yaw (+Z of the pose).
            // Which way the imported mesh climbs is measured from its vertices, not guessed: the end of its longer level axis whose
            // vertices stand higher is the top.
            PrefabFamily stairs = palette.FindFamily(PropRole.CityStairs);
            List<GameObject> flights = WorldPalette.ValidPrefabs(stairs);
            if (flights.Count > 0)
            {
                stairs.centerPivot = true;
                stairs.yawOffset = ClimbYawOffset(flights[0]);
                Debug.Log($"City stair flight {flights[0].name}: climbs along the placement's yaw with yaw offset {stairs.yawOffset:0}.", flights[0]);
            }

            // Endless road landmarks (palette v9): the structure is centred on its placement (the Fantasy Kingdom presets' pivots sit
            // off their footprint), as are the hay piles (one of whose meshes is 4 m off its pivot).
            foreach (string id in new[] { PropRole.LandmarkTower, PropRole.LandmarkChapel, PropRole.LandmarkMill, PropRole.LandmarkRuin, PropRole.Haystack, PropRole.CityTower })
            {
                PrefabFamily family = palette.FindFamily(id);
                if (family == null) continue;
                family.yawOffset = 0f;
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

        /// <summary>
        /// Logs how the endless road's landmark and field-boundary families resolved (prefabs found of those catalogued, measured
        /// bounds of the first), and warns about any that resolved to nothing (their placements are then skipped).
        /// </summary>
        private static void LogLandmarkFamilies(WorldPalette palette)
        {
            var log = new System.Text.StringBuilder("Endless road landmark and boundary families (prefabs found / catalogued; first prefab's bounds):");
            var empty = new List<string>();
            foreach (var (id, paths) in SyntyCatalog.Families)
            {
                if (!PropRole.IsLandmark(id) && !PropRole.IsBoundaryPiece(id) && id != PropRole.Gravestone && id != PropRole.Haystack && id != PropRole.WoodPile && id != PropRole.StoneRunes) continue;
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(palette.FindFamily(id));
                if (prefabs.Count == 0)
                {
                    empty.Add(id);
                    log.Append($"\n  {id}: 0 / {paths.Length}  NOT FOUND");
                    continue;
                }

                Bounds b = WorldBuilder.LocalBounds(prefabs[0]);
                log.Append($"\n  {id}: {prefabs.Count} / {paths.Length}; {prefabs[0].name} {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m (min y {b.min.y:0.00})");
            }

            Debug.Log(log.ToString(), palette);
            if (empty.Count > 0) Debug.LogWarning("World palette: these endless-road families have no prefabs and are skipped: " + string.Join(", ", empty), palette);
        }

        /// <summary>
        /// Forest pass (palette v10): logs how the endless road's woods families resolved (ferns, undergrowth, ivy: prefabs found of
        /// those catalogued, triangles and bounds of each), and warns about any that resolved to nothing (their placements are then
        /// not drawn). Also checks that the ivy and fern prefabs carry no LODGroup surprises (the instanced renderer draws LOD0).
        /// </summary>
        private static void LogForestFamilies(WorldPalette palette)
        {
            var log = new System.Text.StringBuilder("Endless road woods families (forest pass; prefabs found / catalogued; triangles and bounds per prefab):");
            var empty = new List<string>();
            foreach (var (id, paths) in SyntyCatalog.Families)
            {
                if (!PropRole.IsForestExtra(id)) continue;
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(palette.FindFamily(id));
                log.Append($"\n  {id}: {prefabs.Count} / {paths.Length}{(prefabs.Count == paths.Length ? " all resolved" : prefabs.Count == 0 ? "  NOT FOUND" : "  SOME MISSING")}");
                if (prefabs.Count == 0) empty.Add(id);
                foreach (GameObject prefab in prefabs)
                {
                    int triangles = 0;
                    foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh != null) triangles += (int)(filter.sharedMesh.triangles.LongLength / 3);
                    }

                    Bounds b = WorldBuilder.LocalBounds(prefab);
                    log.Append($"\n    {prefab.name}: {triangles} triangles, {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m (min {b.min.x:0.00}, {b.min.y:0.00}, {b.min.z:0.00})");
                }
            }

            Debug.Log(log.ToString(), palette);
            if (empty.Count > 0) Debug.LogWarning("World palette: these endless-road woods families have no prefabs and are not drawn: " + string.Join(", ", empty), palette);
        }

        /// <summary>
        /// Palette v12: logs how the endless road's inn and village families resolved (prefabs found of those catalogued, triangles and bounds
        /// of each, and for the building presets the offline measurement in BuildingTable beside the editor's bounds), and warns about any
        /// family that resolved to nothing (an inn then falls back to the shared dresser, a village to the stone cabin).
        /// </summary>
        private static void LogVillageFamilies(WorldPalette palette)
        {
            var log = new System.Text.StringBuilder("Endless road inn and village families (palette v12; prefabs found / catalogued; triangles and bounds per prefab):");
            var empty = new List<string>();
            string[] ids =
            {
                PropRole.Tavern, PropRole.House, PropRole.Hut, PropRole.Outhouse, PropRole.Shelter, PropRole.Smithy, PropRole.Stables, PropRole.Gatehouse,
                PropRole.Bench, PropRole.Lamp, PropRole.MarketStall, PropRole.YardGoods, PropRole.FarmCart, PropRole.GardenPlant, PropRole.SmithyTool, PropRole.Fingerpost,
            };
            foreach (string id in ids)
            {
                int catalogued = BuildingTable.CatalogCount(id);
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(palette.FindFamily(id));
                log.Append($"\n  {id}: {prefabs.Count} / {catalogued}{(prefabs.Count == catalogued ? " all resolved" : prefabs.Count == 0 ? "  NOT FOUND" : "  SOME MISSING (variants shift: check BuildingTable)")}");
                if (prefabs.Count == 0) empty.Add(id);
                foreach (GameObject prefab in prefabs)
                {
                    int triangles = 0;
                    foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh != null) triangles += (int)(filter.sharedMesh.triangles.LongLength / 3);
                    }

                    Bounds b = WorldBuilder.LocalBounds(prefab);
                    log.Append($"\n    {prefab.name}: {triangles} triangles, {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m (min {b.min.x:0.00}, {b.min.y:0.00}, {b.min.z:0.00})");
                    BuildingShape shape = BuildingTable.ForPrefab(prefab.name);
                    if (shape != null)
                    {
                        log.Append($"; measured offline: extent x {shape.ExtentX0:0.00}..{shape.ExtentX1:0.00} z {shape.ExtentZ0:0.00}..{shape.ExtentZ1:0.00}, {shape.Height:0.00} m, {shape.Triangles} triangles");
                    }
                }
            }

            Debug.Log(log.ToString(), palette);
            if (empty.Count > 0) Debug.LogWarning("World palette: these endless-road inn and village families have no prefabs: " + string.Join(", ", empty), palette);
        }

        /// <summary>Where the shadow proxy material is saved.</summary>
        private const string ShadowProxyPath = "Assets/_Project/Materials/Endless/ShadowProxy.mat";

        /// <summary>
        /// Forest pass: the shadow-only material of the instanced trees' low-poly proxies beyond 100 m: URP Lit, opaque (no alpha clip),
        /// double-sided, instanced. Referenced by the palette so the shader's shadow pass is in builds.
        /// </summary>
        private static Material EnsureShadowProxyMaterial(List<string> missing)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                missing.Add("shader Universal Render Pipeline/Lit (shadow proxies)");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(ShadowProxyPath);
            if (material == null)
            {
                EnsureFolder(ShadowProxyPath.Substring(0, ShadowProxyPath.LastIndexOf('/')));
                material = new Material(shader) { name = "ShadowProxy" };
                AssetDatabase.CreateAsset(material, ShadowProxyPath);
                Debug.Log($"Created {ShadowProxyPath} (URP Lit, shadow proxies of the endless road's trees).", material);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.enableInstancing = true;
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetColor("_BaseColor", new Color(0.2f, 0.25f, 0.15f, 1f));
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Length of the longer horizontal axis of the prefab's mesh bounds.</summary>
        /// <summary>
        /// The yaw offset that turns a stair flight prefab so it climbs along local +Z: compares the mean height of its mesh vertices
        /// (in the prefab's space) at the two ends of each level axis; the axis with the larger difference is the climb, its higher end the top.
        /// </summary>
        private static float ClimbYawOffset(GameObject prefab)
        {
            var pts = new List<Vector3>();
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (Vector3 v in mf.sharedMesh.vertices) pts.Add(m.MultiplyPoint3x4(v));
            }

            if (pts.Count == 0) return 0f;
            Vector3 mn = pts[0], mx = pts[0];
            foreach (Vector3 p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            float EndRise(int axis)
            {
                float lo = mn[axis], hi = mx[axis], band = 0.2f * (hi - lo);
                double a = 0, b = 0;
                int na = 0, nb = 0;
                foreach (Vector3 p in pts)
                {
                    if (p[axis] <= lo + band) { a += p.y; na++; }
                    else if (p[axis] >= hi - band) { b += p.y; nb++; }
                }

                return na == 0 || nb == 0 ? 0f : (float)(b / nb - a / na);   // > 0: climbs towards +axis
            }

            float rx = EndRise(0), rz = EndRise(2);
            // Ry(a) maps local (1, 0, 0) to (cos a, 0, -sin a) and (0, 0, 1) to (sin a, 0, cos a).
            if (Mathf.Abs(rx) >= Mathf.Abs(rz)) return rx > 0f ? -90f : 90f;
            return rz > 0f ? 0f : 180f;
        }

        private static float LongAxis(GameObject prefab, out bool alongX)
        {
            Bounds b = WorldBuilder.LocalBounds(prefab);
            alongX = b.size.x >= b.size.z;
            return Mathf.Max(b.size.x, b.size.z);
        }

        // ------------------------------------------------------------------ generated prefabs

        /// <summary>First-party copy of the crop field texture with yellow rapeseed heads, and the rapeseed material using it (v15, v20).</summary>
        private const string RapeseedTexturePath = DetailMaterialFolder + "/Rapeseed_Flowers.png", RapeseedMaterialPath = DetailMaterialFolder + "/Rapeseed_Field.mat";

        /// <summary>
        /// The rapeseed material (palette v20): the Synty crop field material with its texture swapped, in the leaf AND the trunk
        /// slot, for a first-party copy whose flower heads (the amber texels: red at least green; stems and leaves are green) are
        /// recoloured to <see cref="GroundLook.RapeseedFlower"/>. Falls back to the Synty material when the texture cannot be read.
        /// History: v15 to v19 derived it from the flower field material, whose texture has another layout and whose trunk slot is
        /// empty; the mesh is drawn from the trunk slot, so every rapeseed field was opaque white cards (the "white fields" of the
        /// tours of 2026-10-06, found by asking the generator which crop the white field was and diffing the two materials).
        /// </summary>
        private static Material RapeseedMaterial(Material source)
        {
            if (source == null || !source.HasProperty("_Leaf_Texture") || !(source.GetTexture("_Leaf_Texture") is Texture2D leaf)) return source;
            EnsureFolder(DetailMaterialFolder);
            var rt = RenderTexture.GetTemporary(leaf.width, leaf.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            var copy = new Texture2D(leaf.width, leaf.height, TextureFormat.RGBA32, false, false);
            int recoloured = 0;
            try
            {
                Graphics.Blit(leaf, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, leaf.width, leaf.height), 0, 0);
                Color[] px = copy.GetPixels();
                Color flower = GroundLook.RapeseedFlower;
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = px[i];
                    // Only the flower heads: the texture's one amber tone (red at least green); stems and leaves are green. The
                    // alpha is the cut-out and is kept as it is; the transparent background is left alone.
                    if (c.a < 0.1f || c.r < c.g) continue;
                    float shade = Mathf.Clamp01(Mathf.Max(c.r, c.g) / 0.72f);
                    px[i] = new Color(flower.r * shade, flower.g * shade, flower.b * shade, c.a);
                    recoloured++;
                }

                copy.SetPixels(px);
                File.WriteAllBytes(RapeseedTexturePath, copy.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(copy);
            }

            AssetDatabase.ImportAsset(RapeseedTexturePath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(RapeseedTexturePath) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(RapeseedTexturePath);
            if (texture == null) return source;
            var fresh = new Material(source) { name = Path.GetFileNameWithoutExtension(RapeseedMaterialPath) };
            fresh.SetTexture("_Leaf_Texture", texture);
            // The clump is drawn from the trunk slot: an empty one is an opaque white card.
            if (fresh.HasProperty("_Trunk_Texture")) fresh.SetTexture("_Trunk_Texture", texture);
            fresh.enableInstancing = true;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(RapeseedMaterialPath);
            if (existing == null) AssetDatabase.CreateAsset(fresh, RapeseedMaterialPath);
            else
            {
                EditorUtility.CopySerialized(fresh, existing);
                existing.name = fresh.name;
                EditorUtility.SetDirty(existing);
                fresh = existing;
            }

            Debug.Log($"Rapeseed material: {RapeseedMaterialPath} from {AssetDatabase.GetAssetPath(source)}, leaf texture {AssetDatabase.GetAssetPath(leaf)} ({leaf.width}x{leaf.height}) " +
                      $"with {recoloured} flower-head texels recoloured to sRGB {GroundLook.RapeseedFlower}; leaf and trunk slots both set.");
            return fresh;
        }

        private static void EnsureRapeseedPrefabs(List<string> missing)
        {
            var synty = AssetDatabase.LoadAssetAtPath<Material>(SyntyCatalog.RapeseedMaterial);
            if (synty == null) missing.Add(SyntyCatalog.RapeseedMaterial);
            Material material = RapeseedMaterial(synty);
            EnsureFolder(SyntyCatalog.GeneratedPrefabFolder);
            foreach (string modelPath in SyntyCatalog.RapeseedModels)
            {
                string target = $"{SyntyCatalog.GeneratedPrefabFolder}/{Path.GetFileNameWithoutExtension(modelPath)}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(target) != null)
                {
                    // v15: an existing prefab takes the first-party material.
                    if (material == null) continue;
                    GameObject contents = PrefabUtility.LoadPrefabContents(target);
                    try
                    {
                        foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
                        {
                            var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                            for (int i = 0; i < materials.Length; i++) materials[i] = material;
                            renderer.sharedMaterials = materials;
                        }

                        PrefabUtility.SaveAsPrefabAsset(contents, target);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }

                    Debug.Log($"Rapeseed prefab {target}: material {AssetDatabase.GetAssetPath(material)}.");
                    continue;
                }

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
        private static GameObject CreateDetailPrefab(GameObject source, string family, System.Text.StringBuilder log, out float height, out float width)
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

            // Sward details (grass, ground cover, small flowers): normals pointing up so a clump shades like the ground it stands
            // on, no shadow of their own (renderer and material), sward colours (GroundLook). Sunflowers keep their own look.
            Material[] materials = renderer.sharedMaterials;
            bool sward = GroundLook.IsSward(family);
            if (sward)
            {
                Mesh up = SaveUpNormalMesh(mesh, $"{DetailFolder}/{source.name}_Sward.asset");
                if (up != null) mesh = up;
                else Debug.LogWarning($"Could not copy the mesh of {source.name} with upward normals (mesh not readable?); the clump keeps its own normals.", source);
                materials = (Material[])materials.Clone();
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null) materials[i] = SwardMaterial(materials[i], family);
                }
            }

            var go = new GameObject(source.name + "_Detail");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = go.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterials = materials;
                if (sward)
                {
                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = true;
                }

                return PrefabUtility.SaveAsPrefabAsset(go, target);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Saves (or overwrites) a copy of <paramref name="mesh"/> whose normals point up (+Y, <see cref="GroundLook.SwardNormalUp"/>), so the clump is lit
        /// like flat ground (the detail's partial alignment to the terrain then tilts it with the slope). Null when the mesh
        /// data cannot be read.
        /// </summary>
        private static Mesh SaveUpNormalMesh(Mesh mesh, string path)
        {
            Mesh copy = Object.Instantiate(mesh);
            int count = copy.vertexCount;
            if (count == 0 || copy.vertices.Length != count)
            {
                Object.DestroyImmediate(copy);
                return null;
            }

            copy.name = Path.GetFileNameWithoutExtension(path);
            Vector3[] own = copy.normals;
            var normals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 n = own != null && own.Length == count ? own[i] : Vector3.up;
                normals[i] = Vector3.Lerp(n, Vector3.up, GroundLook.SwardNormalUp).normalized;
                if (normals[i].sqrMagnitude < 0.5f) normals[i] = Vector3.up;
            }

            copy.normals = normals;
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

        /// <summary>
        /// The first-party copy of a Synty foliage material for a sward detail family (created or refreshed in place, so its GUID
        /// is kept): instancing on, no shadow caster pass (<c>_CastShadows</c> 0 and the ShadowCaster pass disabled, as URP's own
        /// material inspector does), no leaf normal map (the clump's upward normals are used as they are), matte (every smoothness
        /// and metallic property 0, <see cref="GroundLook.SwardSmoothnessProperties"/>), and the leaf colours
        /// (base, noise, large noise) multiplied in linear space by <see cref="GroundLook.GrassTint"/> so the clumps average
        /// their sward colour.
        /// </summary>
        private static Material SwardMaterial(Material source, string family)
        {
            EnsureFolder(DetailMaterialFolder);
            string path = $"{DetailMaterialFolder}/{source.name}_{family}.mat";
            var fresh = new Material(source) { name = Path.GetFileNameWithoutExtension(path) };
            fresh.enableInstancing = true;
            if (fresh.HasProperty("_CastShadows")) fresh.SetFloat("_CastShadows", 0f);
            fresh.SetShaderPassEnabled("ShadowCaster", false);
            if (fresh.HasProperty("_Enable_Leaf_Normal")) fresh.SetFloat("_Enable_Leaf_Normal", 0f);
            // Sway with the weather (WeatherSystem pushes the wind globals; FoliageWind keeps gale and direction fixed).
            if (fresh.HasProperty("_Use_Global_Weather_Controller")) fresh.SetFloat("_Use_Global_Weather_Controller", 1f);
            // Matte: no sky sheen on the up-facing blades (they looked frosted at dusk).
            foreach (string name in GroundLook.SwardSmoothnessProperties)
            {
                if (fresh.HasProperty(name)) fresh.SetFloat(name, GroundLook.SwardSmoothness);
            }

            foreach (string name in GroundLook.SwardMetallicProperties)
            {
                if (fresh.HasProperty(name)) fresh.SetFloat(name, GroundLook.SwardMetallic);
            }

            Vector3 tint = GroundLook.GrassTint(family);
            if (tint != Vector3.one)
            {
                foreach (string name in new[] { "_Leaf_Base_Color", "_Leaf_Noise_Color", "_Leaf_Noise_Large_Color" })
                {
                    if (!fresh.HasProperty(name)) continue;
                    Color linear = fresh.GetColor(name).linear;
                    var tinted = new Color(linear.r * tint.x, linear.g * tint.y, linear.b * tint.z, linear.a);
                    fresh.SetColor(name, tinted.gamma);
                }
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }

            EditorUtility.CopySerialized(fresh, existing);
            existing.name = fresh.name;
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return existing;
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
