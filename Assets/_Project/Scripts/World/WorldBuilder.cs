using System;
using System.Collections.Generic;
using System.Diagnostics;
using MedievalZen.Roads;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MedievalZen.World
{
    /// <summary>
    /// Turns a <see cref="WorldBlueprint"/> into Unity objects under the <see cref="GameWorld"/>: a Terrain (heights,
    /// splat layers, tree instances, detail density maps), the lake water plane, props in per-chunk parents,
    /// settlement markers, signposts and the horizon ring. In the editor (outside Play mode) prefabs are
    /// instantiated as prefab instances and the TerrainData is created as an asset BEFORE it is painted (see
    /// <see cref="TerrainDataWriter"/>), then saved and self-checked.
    /// </summary>
    public static class WorldBuilder
    {
        /// <summary>Name of the child of the GameWorld that holds everything generated.</summary>
        public const string GeneratedRootName = "Generated";

        /// <summary>Folder the TerrainData assets are saved to in the editor.</summary>
        public const string TerrainDataFolder = TerrainDataWriter.AssetFolder;

        /// <summary>Size of the prop chunk parents in metres.</summary>
        public const float PropChunkSize = 250f;

        /// <summary>Detail cells per detail patch (the detail resolution must be a multiple of this).</summary>
        public const int DetailResolutionPerPatch = 32;

        /// <summary>Terrain <c>heightmapPixelError</c>: low so distant terrain LOD stays under the grass.</summary>
        public const float TerrainPixelError = 1f;

        private static readonly Dictionary<GameObject, Bounds> BoundsCache = new Dictionary<GameObject, Bounds>();

        /// <summary>
        /// Builds the world (replacing any previous generated content). Returns per-stage timings in milliseconds.
        /// <paramref name="progress"/> receives a stage name and 0..1 progress within the build.
        /// </summary>
        public static List<(string stage, double milliseconds)> Build(GameWorld world, WorldSettings settings, WorldPalette palette,
            WorldBlueprint blueprint, Action<string, float> progress = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));

            var timings = new List<(string, double)>();
            var clock = new Stopwatch();
            BoundsCache.Clear();
            WorldGenParams p = blueprint.Params;

            void Stage(string name, float t, Action action)
            {
                progress?.Invoke(name, t);
                clock.Restart();
                action();
                timings.Add((name, clock.Elapsed.TotalMilliseconds));
            }

            Clear(world);
            Transform root = new GameObject(GeneratedRootName).transform;
            root.SetParent(world.transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;

            // Persist-then-paint: in the editor the TerrainData asset is created on disk before anything is written into it
            // (see TerrainDataWriter); heights, alphamaps, trees and details all go into the persistent object.
            Terrain terrain = null;
            TerrainData data = null;
            float[,,] painted = null;
            Stage("Terrain heights and layers", 0f, () =>
            {
                data = TerrainDataWriter.Create($"Terrain_{p.seed}", p.heightmapResolution,
                    new Vector3(p.worldSize, p.hillAmplitude, p.worldSize), TerrainAssetPath(p.seed));
                TerrainDataWriter.WriteHeights(data, blueprint.Heightmap);
                painted = TerrainDataWriter.WriteSplat(data, palette.terrainLayers, blueprint.Splat);
                terrain = CreateTerrainObject(data, root);
            });
            Stage("Terrain trees", 0.25f, () => BuildTrees(data, p, palette, blueprint));
            Stage("Terrain details", 0.35f, () => BuildDetails(data, palette, blueprint));
            Stage("Water and horizon", 0.5f, () =>
            {
                BuildWater(p, palette, blueprint, root);
                BuildHorizon(p, palette, blueprint, root);
            });
            Stage("Settlements and signposts", 0.6f, () =>
            {
                BuildSettlementMarkers(blueprint, root);
                BuildSignposts(palette, blueprint, root);
            });
            Stage("Props", 0.65f, () => BuildProps(palette, blueprint, root, terrain));
            Stage("Save and check terrain", 0.95f, () =>
            {
                terrain.Flush();
                TerrainDataWriter.SaveAndCheck(data, painted, "Generate World");
            });

            world.Terrain = terrain;
            if (settings != null) world.Settings = settings;
            world.Palette = palette;
            world.SetRoads(blueprint.Roads);
            world.SetNature(blueprint.Landmarks, blueprint.WaterHeight);
            progress?.Invoke("Done", 1f);
            return timings;
        }

        /// <summary>Destroys everything generated under <paramref name="world"/> and clears its roads.</summary>
        public static void Clear(GameWorld world)
        {
            if (world == null) return;
#if UNITY_EDITOR
            // Drop the previous generated TerrainData asset so regenerating does not pile up files.
            if (!Application.isPlaying && world.Terrain != null && world.Terrain.terrainData != null)
            {
                string previous = UnityEditor.AssetDatabase.GetAssetPath(world.Terrain.terrainData);
                if (!string.IsNullOrEmpty(previous) && previous.StartsWith(TerrainDataFolder + "/")) UnityEditor.AssetDatabase.DeleteAsset(previous);
            }
#endif
            for (int i = world.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = world.transform.GetChild(i);
                if (child.name == GeneratedRootName) DestroyObject(child.gameObject);
            }

            if (world.Terrain != null && world.Terrain.gameObject != null) DestroyObject(world.Terrain.gameObject);
            world.Terrain = null;
            world.SetRoads(null);
            world.SetNature(null, -1f);
        }

        // ------------------------------------------------------------------ terrain

        /// <summary>Where the editor saves the TerrainData for <paramref name="seed"/>; null in Play mode (kept in memory).</summary>
        private static string TerrainAssetPath(int seed) => Application.isPlaying ? null : TerrainDataWriter.AssetPathFor(seed);

        private static Terrain CreateTerrainObject(TerrainData data, Transform root)
        {
            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            var terrain = go.GetComponent<Terrain>();
            ApplyTerrainLook(terrain);
            return terrain;
        }

        /// <summary>
        /// The finite world's terrain look (draw instanced, pixel error, base map, detail and tree distances). Shared with the
        /// endless road's near tiles so both modes look the same.
        /// </summary>
        public static void ApplyTerrainLook(Terrain terrain)
        {
            terrain.drawInstanced = true;
            // Pixel error 1: coarser LOD patches otherwise render tens of centimetres below the true surface at mid
            // distance, while grass (placed on the true surface) floats above them.
            terrain.heightmapPixelError = TerrainPixelError;
            terrain.basemapDistance = 400f;
            terrain.detailObjectDistance = 110f;
            terrain.detailObjectDensity = 1f;
            terrain.treeDistance = 2000f;
            terrain.treeBillboardDistance = 300f;
            terrain.treeCrossFadeLength = 40f;
            terrain.treeMaximumFullLODCount = 300;
        }

        private static void BuildTrees(TerrainData data, WorldGenParams p, WorldPalette palette, WorldBlueprint bp)
        {
            var roles = new List<string>(bp.Trees.Count);
            foreach (TreePlacement tree in bp.Trees) roles.Add(tree.role);
            var index = new Dictionary<string, int[]>();
            data.treePrototypes = TreePrototypes(palette, roles, index);
            var instances = new List<TreeInstance>(bp.Trees.Count);
            float height = Mathf.Max(1e-3f, p.hillAmplitude);
            foreach (TreePlacement tree in bp.Trees)
            {
                if (TryTreeInstance(tree, index, p.worldSize, height, out TreeInstance instance)) instances.Add(instance);
            }

            data.SetTreeInstances(instances.ToArray(), true);
        }

        /// <summary>
        /// Tree prototypes for <paramref name="roles"/> (in order of first appearance, each family's valid prefabs in palette
        /// order); fills <paramref name="index"/> with family to prototype index per variant.
        /// </summary>
        public static TreePrototype[] TreePrototypes(WorldPalette palette, IEnumerable<string> roles, Dictionary<string, int[]> index)
        {
            var prototypes = new List<TreePrototype>();
            foreach (string role in roles)
            {
                if (role == null || index.ContainsKey(role)) continue;
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(palette.FindFamily(role));
                var map = new int[prefabs.Count];
                for (int v = 0; v < prefabs.Count; v++)
                {
                    map[v] = prototypes.Count;
                    prototypes.Add(new TreePrototype { prefab = prefabs[v], bendFactor = 0f });
                }

                index[role] = map;
            }

            return prototypes.ToArray();
        }

        /// <summary>
        /// The terrain tree instance for <paramref name="tree"/> (position relative to the terrain's corner) on a terrain
        /// <paramref name="size"/> metres square and <paramref name="height"/> metres high; false when its family has no prefab.
        /// </summary>
        public static bool TryTreeInstance(TreePlacement tree, Dictionary<string, int[]> index, float size, float height, out TreeInstance instance)
        {
            instance = default;
            if (tree.role == null || !index.TryGetValue(tree.role, out int[] map) || map.Length == 0) return false;
            instance = new TreeInstance
            {
                prototypeIndex = map[((tree.variant % map.Length) + map.Length) % map.Length],
                position = new Vector3(tree.position.x / size, tree.position.y / height, tree.position.z / size),
                rotation = tree.yaw * Mathf.Deg2Rad,
                widthScale = tree.scale,
                heightScale = tree.scale,
                color = Color.white,
                lightmapColor = Color.white,
            };
            return true;
        }

        /// <summary>
        /// Writes the detail (grass, flowers) prototypes and density maps of <paramref name="bp"/> into <paramref name="data"/>,
        /// replacing any previous ones. Also used by MedievalZen/Repaint Terrain so grass follows the new paint.
        /// </summary>
        public static void BuildDetails(TerrainData data, WorldPalette palette, WorldBlueprint bp)
        {
            int res = bp.DetailResolution;
            if (res <= 0 || bp.DetailLayers.Count == 0) return;
            res = Mathf.Max(DetailResolutionPerPatch, res / DetailResolutionPerPatch * DetailResolutionPerPatch);

            var prototypes = new List<DetailPrototype>();
            var sources = new List<DetailLayer>();
            foreach (DetailLayer layer in bp.DetailLayers)
            {
                if (!TryDetailPrototype(palette, layer.family, layer.variant, out DetailPrototype prototype)) continue;
                prototypes.Add(prototype);
                sources.Add(layer);
            }

            data.SetDetailResolution(res, DetailResolutionPerPatch);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.detailPrototypes = prototypes.ToArray();

            int src = bp.DetailResolution;
            var buffer = new int[res, res];
            for (int l = 0; l < sources.Count; l++)
            {
                ResampleDetail(sources[l].density, src, buffer);
                data.SetDetailLayer(0, 0, l, buffer);
            }
        }

        /// <summary>The detail resolution actually used for a blueprint resolution (a multiple of <see cref="DetailResolutionPerPatch"/>).</summary>
        public static int DetailResolutionFor(int blueprintResolution) =>
            Mathf.Max(DetailResolutionPerPatch, blueprintResolution / DetailResolutionPerPatch * DetailResolutionPerPatch);

        /// <summary>
        /// The detail prototype for one variant of a detail family, scaled into its target height from the mesh's native size;
        /// false (with a warning) when the family has no prefab or the prototype is invalid.
        /// </summary>
        public static bool TryDetailPrototype(WorldPalette palette, string familyId, int variant, out DetailPrototype prototype)
        {
            prototype = null;
            PrefabFamily family = palette.FindDetail(familyId);
            List<GameObject> prefabs = WorldPalette.ValidPrefabs(family);
            if (prefabs.Count == 0) return false;
            GameObject prefab = prefabs[variant % prefabs.Count];

            // Scale every family into its target height (DetailPlanner.TargetHeight) from the mesh's native
            // size: recorded by the palette menu, else measured here (older palettes).
            Vector2 native = family.NativeSize(prefab);
            if (native.x <= 0f)
            {
                Bounds b = LocalBounds(prefab);
                native = new Vector2(Mathf.Max(0.01f, b.max.y), Mathf.Max(b.size.x, b.size.z));
            }

            var (minWidth, maxWidth, minHeight, maxHeight) = DetailPlanner.PrototypeScale(familyId, native.x, native.y);
            var candidate = new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                renderMode = DetailRenderMode.VertexLit,
                useInstancing = true,
                minWidth = minWidth,
                maxWidth = maxWidth,
                minHeight = minHeight,
                maxHeight = maxHeight,
                noiseSpread = 0.4f,
                healthyColor = Color.white,
                dryColor = new Color(0.92f, 0.9f, 0.78f),
                alignToGround = 0.6f,
                positionJitter = 1f,
            };

            if (!candidate.Validate(out string error))
            {
                Debug.LogWarning($"Detail prototype {prefab.name} ({familyId}) is invalid: {error}. Run MedievalZen/Create Default World Palette to rebuild the single-mesh detail prefabs.", prefab);
                return false;
            }

            prototype = candidate;
            return true;
        }

        /// <summary>Nearest-neighbour resample of a <paramref name="src"/>-square density map into <paramref name="buffer"/> (<c>[z, x]</c>, square).</summary>
        public static void ResampleDetail(byte[] density, int src, int[,] buffer)
        {
            int res = buffer.GetLength(0);
            for (int z = 0; z < res; z++)
            {
                int sz = Mathf.Min(src - 1, z * src / res);
                for (int x = 0; x < res; x++)
                {
                    buffer[z, x] = density[sz * src + Mathf.Min(src - 1, x * src / res)];
                }
            }
        }

        // ------------------------------------------------------------------ water and horizon

        private static void BuildWater(WorldGenParams p, WorldPalette palette, WorldBlueprint bp, Transform root)
        {
            if (bp.WaterHeight <= 0f) return;
            if (palette.waterPlane == null)
            {
                Debug.LogWarning("WorldPalette has no water plane; lakes will be empty hollows.");
                return;
            }

            GameObject water = Spawn(palette.waterPlane, root);
            water.name = "Lakes";
            if (palette.waterMaterial != null)
            {
                foreach (Renderer renderer in water.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = palette.waterMaterial;
            }

            FitWaterPlane(water, palette.waterPlane, p.worldSize * 0.5f, p.worldSize * 0.5f, p.worldSize * 1.02f, bp.WaterHeight);
            foreach (Collider collider in water.GetComponentsInChildren<Collider>()) collider.enabled = false;
        }

        /// <summary>
        /// Scales and places a water plane instance of <paramref name="prefab"/> so it covers a <paramref name="cover"/> metre
        /// square centred on local (<paramref name="centreX"/>, <paramref name="centreZ"/>) at <paramref name="height"/>.
        /// </summary>
        public static void FitWaterPlane(GameObject water, GameObject prefab, float centreX, float centreZ, float cover, float height)
        {
            Bounds local = LocalBounds(prefab);
            Vector3 scale = new Vector3(cover / Mathf.Max(0.01f, local.size.x), 1f, cover / Mathf.Max(0.01f, local.size.z));
            water.transform.localScale = scale;
            water.transform.localRotation = Quaternion.identity;
            water.transform.localPosition = new Vector3(centreX - local.center.x * scale.x, height, centreZ - local.center.z * scale.z);
        }

        private static void BuildHorizon(WorldGenParams p, WorldPalette palette, WorldBlueprint bp, Transform root)
        {
            Transform parent = new GameObject("Horizon").transform;
            parent.SetParent(root, false);
            BuildSkirt(p, palette, bp, parent);

            foreach (HorizonPlacement placement in bp.Horizon)
            {
                GameObject prefab = palette.GetPrefab(placement.family, placement.variant);
                if (prefab == null) continue;
                GameObject go = Spawn(prefab, parent);
                Bounds local = LocalBounds(prefab);
                var scale = new Vector3(
                    placement.width / Mathf.Max(0.01f, local.size.x),
                    placement.height / Mathf.Max(0.01f, local.size.y),
                    placement.width / Mathf.Max(0.01f, local.size.z));
                go.transform.localScale = scale;
                go.transform.localRotation = Quaternion.Euler(0f, placement.yaw, 0f);
                go.transform.localPosition = placement.position - Vector3.up * (local.min.y * scale.y);
                FixMissingMaterials(go, palette.backgroundHillMaterial);
                foreach (Collider collider in go.GetComponentsInChildren<Collider>()) collider.enabled = false;
            }
        }

        /// <summary>
        /// A ring mesh around the terrain that continues its edge outwards and rises into a low bowl of hills, so
        /// there is never a void between the terrain edge and the background hills. Has a collider so the player
        /// cannot fall off the world.
        /// </summary>
        private static void BuildSkirt(WorldGenParams p, WorldPalette palette, WorldBlueprint bp, Transform parent)
        {
            float size = p.worldSize;
            int perSide = Mathf.Max(8, Mathf.RoundToInt(size / 25f));
            int ringCount = perSide * 4;
            float[] ringDistance = { 0f, 40f, 160f, 450f, 1200f };
            float[] ringRise = { -0.3f, 1f, 12f, 45f, 110f };
            var noise = new GradientNoise(p.seed + 18);
            var vertices = new Vector3[ringCount * ringDistance.Length];
            for (int k = 0; k < ringCount; k++)
            {
                int side = k / perSide;
                float t = (k % perSide) / (float)perSide * size;
                Vector2 edge;
                switch (side)
                {
                    case 0: edge = new Vector2(t, 0f); break;
                    case 1: edge = new Vector2(size, t); break;
                    case 2: edge = new Vector2(size - t, size); break;
                    default: edge = new Vector2(0f, size - t); break;
                }

                var centre = new Vector2(size * 0.5f, size * 0.5f);
                Vector2 dir = (edge - centre).normalized;
                float edgeHeight = HeightmapGenerator.SampleHeight(bp.Heightmap, p, edge.x, edge.y);
                for (int r = 0; r < ringDistance.Length; r++)
                {
                    Vector2 pos = edge + dir * ringDistance[r];
                    float n = r >= 2 ? 0.6f + 0.8f * (0.5f + 0.5f * noise.Fbm(pos.x * 0.004f, pos.y * 0.004f, 3)) : 1f;
                    vertices[r * ringCount + k] = new Vector3(pos.x, edgeHeight + ringRise[r] * n, pos.y);
                }
            }

            var triangles = new List<int>(ringCount * (ringDistance.Length - 1) * 6);
            for (int r = 0; r < ringDistance.Length - 1; r++)
            {
                for (int k = 0; k < ringCount; k++)
                {
                    int a = r * ringCount + k;
                    int b = r * ringCount + (k + 1) % ringCount;
                    int c = (r + 1) * ringCount + k;
                    int d = (r + 1) * ringCount + (k + 1) % ringCount;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) uvs[i] = new Vector2(vertices[i].x, vertices[i].z) / 8f;

            var mesh = new Mesh { name = "HorizonSkirt" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Make sure the winding faces up (the perimeter walk direction decides it).
            Vector3[] normals = mesh.normals;
            if (normals.Length > 0 && normals[ringCount + perSide / 2].y < 0f)
            {
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int tmp = triangles[i + 1];
                    triangles[i + 1] = triangles[i + 2];
                    triangles[i + 2] = tmp;
                }

                mesh.triangles = triangles.ToArray();
                mesh.RecalculateNormals();
            }

            var go = new GameObject("Skirt");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            Material material = CreateSkirtMaterial(palette);
            if (material != null) renderer.sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        /// <summary>URP Lit material with the lush-grass terrain texture (the skirt continues the terrain's look).</summary>
        private static Material CreateSkirtMaterial(WorldPalette palette)
        {
            TerrainLayer grass = palette.terrainLayers != null && palette.terrainLayers.Length > 0 ? palette.terrainLayers[0] : null;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (grass == null || grass.diffuseTexture == null || shader == null) return palette.backgroundHillMaterial;
            var material = new Material(shader) { name = "HorizonSkirt" };
            material.SetTexture("_BaseMap", grass.diffuseTexture);
            material.SetColor("_BaseColor", new Color(0.82f, 0.86f, 0.78f));
            material.SetFloat("_Smoothness", 0.05f);
            return material;
        }

        private static void FixMissingMaterials(GameObject go, Material fallback)
        {
            if (fallback == null) return;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material m = materials[i];
                    if (m == null || m.name.StartsWith("Default") || m.shader == null || m.shader.name == "Standard" || m.shader.name.StartsWith("Hidden/InternalErrorShader"))
                    {
                        materials[i] = fallback;
                        changed = true;
                    }
                }

                if (changed) renderer.sharedMaterials = materials;
            }
        }

        // ------------------------------------------------------------------ props

        private static void BuildProps(WorldPalette palette, WorldBlueprint bp, Transform root, Terrain terrain)
        {
            Transform propsRoot = new GameObject("Props").transform;
            propsRoot.SetParent(root, false);
            Transform settlementsRoot = GetOrCreate(root, "Settlements");
            var chunks = new Dictionary<long, Transform>();
            var missing = new HashSet<string>();
            var safety = new Dictionary<string, int[]>();
            int placed = 0;

            foreach (PropPlacement prop in bp.Props)
            {
                GameObject prefab = palette.GetPrefab(prop.role, prop.variant);
                if (prefab == null)
                {
                    missing.Add(prop.role);
                    continue;
                }

                Transform parent;
                Vector3 origin = Vector3.zero;
                if (prop.settlementId >= 0 && prop.settlementId < bp.Settlements.Count)
                {
                    Transform settlement = GetOrCreate(settlementsRoot, bp.Settlements[prop.settlementId].Name);
                    parent = GetOrCreate(settlement, "Props");
                    origin = settlement.localPosition;
                }
                else
                {
                    int cx = Mathf.FloorToInt(prop.position.x / PropChunkSize);
                    int cz = Mathf.FloorToInt(prop.position.z / PropChunkSize);
                    long key = ((long)cx << 32) ^ (uint)cz;
                    if (!chunks.TryGetValue(key, out parent))
                    {
                        parent = new GameObject($"Chunk_{cx}_{cz}").transform;
                        parent.SetParent(propsRoot, false);
                        chunks[key] = parent;
                    }
                }

                GameObject go = Spawn(prefab, parent);
                PrefabFamily family = palette.FindFamily(prop.role);
                bool measured = PropPose(palette, prop, prefab, out Vector3 position, out Quaternion rotation);

                go.transform.localPosition = position - origin;
                go.transform.localRotation = rotation;
                go.transform.localScale = Vector3.one * prop.scale;
                if (measured && terrain != null && !SafetyNet(go, prop.role, at => terrain.SampleHeight(at) + terrain.GetPosition().y, WorldBounds, safety))
                {
                    DestroyObject(go);
                    continue;
                }

                if (prop.role == PropRole.Windmill) AnimateWindmill(go, palette);
                if (prop.role == PropRole.Cabin && prop.settlementId >= 0 && prop.settlementId < bp.Settlements.Count &&
                    bp.Settlements[prop.settlementId].Type == SettlementType.Inn)
                {
                    PlaceInnDoor(prefab, prop, family, position, rotation, parent, origin, bp.Settlements[prop.settlementId].Name);
                }

                placed++;
            }

            foreach (string role in missing) Debug.LogWarning($"WorldPalette has no prefabs for family '{role}'; those placements were skipped.");
            if (safety.Count > 0)
            {
                var line = new System.Text.StringBuilder("Rock safety net (placed / sunk / shrunk / removed):");
                var families = new List<string>(safety.Keys);
                families.Sort(StringComparer.Ordinal);
                foreach (string f in families)
                {
                    int[] c = safety[f];
                    line.Append($" {f} {c[0]}/{c[1]}/{c[2]}/{c[3]};");
                }

                Debug.Log(line.ToString());
            }
        }

        /// <summary>
        /// Where and how a prop placement is instanced (position in the placement's own space, before any parent offset):
        /// rotation <c>Quaternion.Euler(pitch, yaw + yawOffset, roll)</c>; families with a centre pivot are shifted so their
        /// mesh centre lands on the placement point. Rock-like pieces (returns true) were seated by <see cref="RockPlacer"/> on
        /// the pivot with their own yaw / pitch / roll and get no offsets.
        /// </summary>
        public static bool PropPose(WorldPalette palette, PropPlacement prop, GameObject prefab, out Vector3 position, out Quaternion rotation)
        {
            PrefabFamily family = palette.FindFamily(prop.role);
            bool measured = IsMeasured(prop.role);
            float yawOffset = measured ? 0f : family?.yawOffset ?? 0f;
            rotation = Quaternion.Euler(prop.pitch, prop.yaw + yawOffset, prop.roll);
            position = prop.position;
            if (!measured && family != null && family.centerPivot)
            {
                Bounds local = LocalBounds(prefab);
                position -= rotation * new Vector3(local.center.x, 0f, local.center.z) * prop.scale;
            }

            return measured;
        }

        /// <summary>Families seated by <see cref="RockPlacer"/> and checked by the safety net.</summary>
        public static bool IsMeasured(string role) => PropRole.IsRock(role) || role == PropRole.StoneStack || role == PropRole.StonePile;

        /// <summary>World-space bounds of an instance (see <see cref="SafetyNet"/>); false when there are none.</summary>
        public delegate bool BoundsOf(GameObject go, out Bounds bounds);

        /// <summary>
        /// Build-time guarantee for a rock-like piece, from its real renderer bounds: if its lowest point is not below the lowest
        /// terrain under its footprint (5 x 5 samples) it is sunk until it is (0.3 m margin); a cliff whose top stands more than
        /// 45 % of its height + 1 m above the highest ground under it is shrunk (x0.8, twice at most) and removed if it still looms;
        /// anything taller than <see cref="RockPlacer.MaxHeight"/> + 0.5 m is removed. Counts per family into
        /// <paramref name="counts"/> (placed, sunk, shrunk, removed). Returns false when the piece must be removed.
        /// </summary>
        public static bool SafetyNet(GameObject go, string role, Func<Vector3, float> groundAt, BoundsOf boundsOf, Dictionary<string, int[]> counts)
        {
            if (!counts.TryGetValue(role, out int[] c)) counts[role] = c = new int[4];
            bool cliff = PropRole.IsCliff(role);
            bool sunk = false, shrunk = false;
            for (int pass = 0; pass < 4; pass++)
            {
                if (!boundsOf(go, out Bounds b)) break;
                if (b.size.y > RockPlacer.MaxHeight + 0.5f)
                {
                    c[3]++;
                    return false;
                }

                float gMin = float.PositiveInfinity, gMax = float.NegativeInfinity;
                for (int j = 0; j < 5; j++)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var at = new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / 4f), 0f, Mathf.Lerp(b.min.z, b.max.z, j / 4f));
                        float g = groundAt(at);
                        gMin = Mathf.Min(gMin, g);
                        gMax = Mathf.Max(gMax, g);
                    }
                }

                if (b.min.y > gMin - 0.05f)
                {
                    go.transform.position -= Vector3.up * (b.min.y - gMin + 0.3f);
                    sunk = true;
                    continue;
                }

                if (cliff && b.max.y - gMax > 0.45f * b.size.y + 1f)
                {
                    if (pass >= 3)
                    {
                        c[3]++;
                        return false;
                    }

                    // Shrink about the bottom centre (keeps it seated), then check again.
                    Vector3 pivotToBase = new Vector3(b.center.x, b.min.y, b.center.z) - go.transform.position;
                    go.transform.localScale *= 0.8f;
                    go.transform.position += pivotToBase * 0.2f;
                    shrunk = true;
                    continue;
                }

                break;
            }

            c[0]++;
            if (sunk) c[1]++;
            if (shrunk) c[2]++;
            return true;
        }

        /// <summary>World bounds of every renderer under <paramref name="go"/>.</summary>
        public static bool WorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            {
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any;
        }

        /// <summary>Spins the blades of a windmill instance with the palette's rotate controller.</summary>
        public static void AnimateWindmill(GameObject windmill, WorldPalette palette)
        {
            foreach (Transform child in windmill.GetComponentsInChildren<Transform>(true))
            {
                if (child == windmill.transform || child.name.IndexOf("Blades", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Vector3 offset = child.localPosition;
                RuntimeAnimatorController controller = Mathf.Abs(offset.z) >= Mathf.Abs(offset.x) ? palette.rotateZController : palette.rotateXController;
                if (controller == null) controller = palette.rotateZController != null ? palette.rotateZController : palette.rotateXController;
                if (controller == null) return;
                Animator animator = child.GetComponent<Animator>();
                if (animator == null) animator = child.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                return;
            }
        }

        private static void BuildSettlementMarkers(WorldBlueprint bp, Transform root)
        {
            Transform settlementsRoot = GetOrCreate(root, "Settlements");
            foreach (Settlement settlement in bp.Roads.Settlements)
            {
                PlaceSettlementMarker(settlementsRoot, settlement.Id, settlement.Name, settlement.Type, settlement.Radius, settlement.Position);
            }
        }

        /// <summary>
        /// The child of <paramref name="parent"/> named <paramref name="displayName"/> (created if missing) at local
        /// <paramref name="localPosition"/>, with an initialised <see cref="SettlementMarker"/> (trigger sphere).
        /// </summary>
        public static SettlementMarker PlaceSettlementMarker(Transform parent, int id, string displayName, SettlementType type, float radius, Vector3 localPosition)
        {
            Transform t = GetOrCreate(parent, displayName);
            t.localPosition = localPosition;
            var marker = t.gameObject.GetComponent<SettlementMarker>();
            if (marker == null) marker = t.gameObject.AddComponent<SettlementMarker>();
            marker.Initialize(id, displayName, type, radius);
            return marker;
        }

        private static void BuildSignposts(WorldPalette palette, WorldBlueprint bp, Transform root)
        {
            if (bp.Signposts.Count == 0) return;
            Transform parent = GetOrCreate(root, "Signposts");
            IReadOnlyList<Settlement> settlements = bp.Roads.Settlements;
            foreach (SignpostRecord record in bp.Signposts)
            {
                GameObject prefab = palette.GetPrefab(PropRole.Sign, record.variant);
                GameObject go = prefab != null ? Spawn(prefab, parent) : new GameObject();
                go.name = $"Signpost_{record.nodeId}";
                go.transform.localPosition = record.position;
                go.transform.localRotation = Quaternion.Euler(0f, record.yaw + palette.GetYawOffset(PropRole.Sign), 0f);

                var arms = new List<SignpostDirection>(record.arms.Count);
                foreach (SignpostArmRecord arm in record.arms)
                {
                    var direction = new SignpostDirection
                    {
                        segmentId = arm.segmentId,
                        towardNodeId = arm.towardNodeId,
                        direction = arm.direction,
                        settlementIds = arm.settlementIds.ToArray(),
                        distances = arm.distances.ToArray(),
                        settlementNames = new string[arm.settlementIds.Count],
                    };

                    for (int i = 0; i < arm.settlementIds.Count; i++) direction.settlementNames[i] = settlements[arm.settlementIds[i]].Name;
                    if (arm.settlementIds.Count > 0)
                    {
                        direction.nearestSettlement = direction.settlementNames[0];
                        direction.nearestDistance = arm.distances[0];
                    }

                    arms.Add(direction);
                }

                go.AddComponent<Signpost>().Initialize(record.nodeId, arms);
                go.AddComponent<MedievalZen.Travel.SignpostInteractable>();
            }
        }

        /// <summary>Puts an <see cref="MedievalZen.Travel.InnRest"/> point just outside the door side (+Z of the placement) of an inn cabin.</summary>
        public static GameObject PlaceInnDoor(GameObject prefab, PropPlacement prop, PrefabFamily family, Vector3 position, Quaternion rotation,
            Transform parent, Vector3 origin, string innName)
        {
            Bounds local = LocalBounds(prefab);
            Vector3 axis = Quaternion.Euler(0f, -(family?.yawOffset ?? 0f), 0f) * Vector3.forward;   // placement +Z in mesh space
            float half = (Mathf.Abs(axis.x) * local.extents.x + Mathf.Abs(axis.z) * local.extents.z) * prop.scale;
            Vector3 centre = position + rotation * new Vector3(local.center.x, 0f, local.center.z) * prop.scale;
            Vector3 door = centre + Quaternion.Euler(0f, prop.yaw, 0f) * Vector3.forward * (half + 1.2f);
            door.y = prop.position.y;

            var go = new GameObject("InnDoor");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = door - origin;
            go.AddComponent<MedievalZen.Travel.InnRest>().Initialize(prop.settlementId, innName);
            return go;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Combined mesh bounds of <paramref name="prefab"/> in its root's local space (cached per build).</summary>
        public static Bounds LocalBounds(GameObject prefab)
        {
            if (prefab == null) return new Bounds(Vector3.zero, Vector3.one);
            if (BoundsCache.TryGetValue(prefab, out Bounds cached)) return cached;
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            bool any = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                Bounds b = filter.sharedMesh.bounds;
                Vector3 min = b.min;
                Vector3 max = b.max;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                    Vector3 point = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            if (!any) bounds = new Bounds(Vector3.zero, Vector3.one);
            BoundsCache[prefab] = bounds;
            return bounds;
        }

        private static Transform GetOrCreate(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        /// <summary>Instantiates <paramref name="prefab"/> under <paramref name="parent"/> (as a prefab instance in the editor outside Play mode).</summary>
        public static GameObject Spawn(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
                if (instance != null) return instance;
            }
#endif
            return Object.Instantiate(prefab, parent, false);
        }

        private static void DestroyObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
