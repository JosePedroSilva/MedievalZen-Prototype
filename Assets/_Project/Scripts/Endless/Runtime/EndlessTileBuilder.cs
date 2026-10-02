using System;
using System.Collections.Generic;
using MedievalZen.Environment;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Applies generated tile blueprints to the scene on the main thread, one small slice per step (each Unity call that can
    /// be heavy is its own step), and hands released tiles' objects back to pools. Near tiles are Unity Terrains (ground,
    /// paint, grass, collider; never terrain trees: their trees go to the <see cref="EndlessTreeRenderer"/>); far tiles are one
    /// MeshRenderer each with a runtime mesh prepared on a worker thread (<see cref="FarTileMeshData"/>). Owns the pools: near
    /// Terrain objects with their in-memory TerrainData, far mesh objects, prop instances per prefab, water planes.
    /// </summary>
    internal sealed class EndlessTileBuilder
    {
        /// <summary>Alphamap rows written per slice.</summary>
        private const int SplatBandRows = 64;

        /// <summary>Props instanced per group (a group is activated in one slice).</summary>
        private const int PropsPerGroup = 32;

        /// <summary>Props handed back to the pool per release slice.</summary>
        private const int ReleasePerSlice = 48;

        /// <summary>Far tiles sit this much lower than the true ground so their coarse edges never show above a near tile's (no cracks).</summary>
        public const float FarDrop = FarTileMeshData.DefaultDrop;

        /// <summary>
        /// Most idle near Terrain objects kept in the pool. Near needs about 16 tiles within 420 m, up to ~10 more inside the
        /// release margin and 3 builds in flight; idle ones beyond this are destroyed.
        /// </summary>
        public int NearPoolCap = 12;

        /// <summary>Most idle far mesh objects kept in the pool (cheap: a GameObject and a small mesh each).</summary>
        public int FarPoolCap = 320;

        /// <summary>Most pooled prop instances kept (all prefabs together).</summary>
        public int PropPoolCap = 4000;

        private static readonly VertexAttributeDescriptor[] FarLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        };

        private readonly EndlessWorld world;
        private readonly WorldPalette palette;
        private readonly Transform poolRoot;
        private readonly Material farMaterial;
        private readonly EndlessTreeRenderer trees;
        private readonly Stack<TerrainSlot> nearSlots = new Stack<TerrainSlot>();
        private readonly Stack<FarMeshSlot> farSlots = new Stack<FarMeshSlot>();
        private readonly List<TerrainData> terrainDatas = new List<TerrainData>();
        private readonly List<Mesh> farMeshes = new List<Mesh>();
        private readonly Dictionary<GameObject, Stack<GameObject>> propPool = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Stack<GameObject> waterPool = new Stack<GameObject>();
        private readonly Dictionary<(string, int), DetailPrototype> detailCache = new Dictionary<(string, int), DetailPrototype>();
        private readonly Dictionary<GameObject, Vector3> chimneys = new Dictionary<GameObject, Vector3>();
        private readonly HashSet<string> missing = new HashSet<string>();
        private readonly Dictionary<string, int[]> safety = new Dictionary<string, int[]>();
        private TerrainLayer[] layers;
        private int[] channelToLayer;
        private int[,] detailBuffer;
        private float[,,] bandBuffer;
        private int created;
        private int farCreated;
        private int propsTotal;
        private int pooledProps;
        private int farShown;
        private int farTrianglesShown;

        public EndlessTileBuilder(EndlessWorld world, WorldPalette palette, Transform poolRoot, Material farMaterial, EndlessTreeRenderer trees)
        {
            this.world = world;
            this.palette = palette;
            this.poolRoot = poolRoot;
            this.farMaterial = farMaterial;
            this.trees = trees;

            layers = TerrainDataWriter.ResolveLayers(palette.terrainLayers, TerrainShaper.LayerCount, out channelToLayer);
            foreach (EndlessWorld.CabinChimney c in world.CabinChimneys)
            {
                if (c.prefab != null) chimneys[c.prefab] = c.chimneyLocal;
            }
        }

        // ------------------------------------------------------------------ diagnostics

        public int PropsPooled => pooledProps;
        public int PropsInUse => propsTotal;
        public int WaterPooled => waterPool.Count;

        /// <summary>Near Terrain objects created so far (the only Unity Terrains of the endless road).</summary>
        public int TerrainsCreated => created;

        /// <summary>Far mesh objects created so far.</summary>
        public int FarMeshesCreated => farCreated;

        /// <summary>Idle near Terrain objects in the pool.</summary>
        public int PooledNear => nearSlots.Count;

        /// <summary>Idle far mesh objects in the pool.</summary>
        public int PooledFar => farSlots.Count;

        /// <summary>Far tiles currently shown.</summary>
        public int FarShown => farShown;

        /// <summary>Triangles of the far tiles currently shown.</summary>
        public int FarTrianglesShown => farTrianglesShown;

        /// <summary>
        /// One line with the rock safety net's counts per family (placed / sunk / shrunk / removed) accumulated since the last
        /// call, then clears them; null when nothing was seated.
        /// </summary>
        public string TakeSafetyReport()
        {
            if (safety.Count == 0) return null;
            var families = new List<string>(safety.Keys);
            families.Sort(StringComparer.Ordinal);
            var line = new System.Text.StringBuilder("rock safety net (placed / sunk / shrunk / removed):");
            foreach (string f in families)
            {
                int[] c = safety[f];
                line.Append($" {f} {c[0]}/{c[1]}/{c[2]}/{c[3]};");
            }

            safety.Clear();
            return line.ToString();
        }

        // ------------------------------------------------------------------ near terrain slots

        /// <summary>Creates one pooled near Terrain ahead of time (behind the start-up fade).</summary>
        public void PrewarmNear()
        {
            Park(CreateSlot());
        }

        private TerrainSlot TakeSlot()
        {
            while (nearSlots.Count > 0)
            {
                TerrainSlot slot = nearSlots.Pop();
                if (slot.Go != null && slot.Data != null) return slot;
            }

            return CreateSlot();
        }

        private TerrainSlot CreateSlot()
        {
            EndlessParams p = world.Params;
            var data = new TerrainData { name = "EndlessNear" };
            data.heightmapResolution = p.nearHeightResolution;
            data.size = new Vector3(p.tileSize, p.heightRange, p.tileSize);
            data.alphamapResolution = p.nearSplatResolution;
            data.baseMapResolution = Mathf.Clamp(p.nearSplatResolution, 16, TerrainDataWriter.MaxBaseMapResolution);
            data.SetDetailResolution(WorldBuilder.DetailResolutionFor(p.nearDetailResolution), WorldBuilder.DetailResolutionPerPatch);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            terrainDatas.Add(data);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.SetActive(false);
            go.name = "Terrain (near)";
            go.transform.SetParent(poolRoot, false);
            var terrain = go.GetComponent<Terrain>();
            var collider = go.GetComponent<TerrainCollider>();
            terrain.allowAutoConnect = false;
            terrain.groupingID = 1;
            WorldBuilder.ApplyTerrainLook(terrain);
            // The endless road has no terrain trees (EndlessTreeRenderer draws them): no tree distance, no billboards.
            terrain.treeDistance = 0f;
            terrain.treeBillboardDistance = 0f;
            terrain.treeMaximumFullLODCount = 0;
            if (collider != null) collider.enabled = false;
            created++;
            return new TerrainSlot { Go = go, Terrain = terrain, Collider = collider, Data = data, Lod = TileLod.Near };
        }

        private void Park(TerrainSlot slot)
        {
            if (slot == null || slot.Go == null) return;
            slot.Go.SetActive(false);
            if (slot.Collider != null) slot.Collider.enabled = false;
            slot.Terrain.SetNeighbors(null, null, null, null);
            slot.Go.transform.SetParent(poolRoot, false);
            if (nearSlots.Count < NearPoolCap)
            {
                nearSlots.Push(slot);
                return;
            }

            Object.Destroy(slot.Go);
            if (slot.Data != null)
            {
                terrainDatas.Remove(slot.Data);
                Object.Destroy(slot.Data);
            }
        }

        // ------------------------------------------------------------------ far mesh slots

        private FarMeshSlot TakeFarSlot()
        {
            while (farSlots.Count > 0)
            {
                FarMeshSlot slot = farSlots.Pop();
                if (slot.Go != null && slot.Mesh != null) return slot;
            }

            var go = new GameObject("Far tile");
            go.SetActive(false);
            go.transform.SetParent(poolRoot, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            var mesh = new Mesh { name = "Endless far tile" };
            farMeshes.Add(mesh);
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = farMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            farCreated++;
            return new FarMeshSlot { Go = go, Filter = filter, Renderer = renderer, Mesh = mesh };
        }

        private void ParkFar(FarMeshSlot slot)
        {
            if (slot == null || slot.Go == null) return;
            slot.Go.SetActive(false);
            slot.Go.transform.SetParent(poolRoot, false);
            if (farSlots.Count < FarPoolCap)
            {
                farSlots.Push(slot);
                return;
            }

            Object.Destroy(slot.Go);
            if (slot.Mesh != null)
            {
                farMeshes.Remove(slot.Mesh);
                Object.Destroy(slot.Mesh);
            }
        }

        /// <summary>Writes prepared far-tile data into a pooled mesh in one go (vertex + index buffers, bounds, upload).</summary>
        private static void Upload(Mesh mesh, FarTileMeshData d)
        {
            const MeshUpdateFlags flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;
            var bounds = new Bounds();
            bounds.SetMinMax(d.BoundsMin, d.BoundsMax);
            mesh.SetVertexBufferParams(d.VertexCount, FarLayout);
            mesh.SetVertexBufferData(d.Vertices, 0, 0, d.VertexCount, 0, flags);
            mesh.SetIndexBufferParams(d.IndexCount, IndexFormat.UInt16);
            mesh.SetIndexBufferData(d.Indices, 0, 0, d.IndexCount, flags);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, d.IndexCount, MeshTopology.Triangles) { bounds = bounds, firstVertex = 0, vertexCount = d.VertexCount }, flags);
            mesh.bounds = bounds;
            mesh.UploadMeshData(false);
        }

        // ------------------------------------------------------------------ props

        private GameObject TakeProp(GameObject prefab, Transform parent)
        {
            propsTotal++;
            if (propPool.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                while (stack.Count > 0)
                {
                    GameObject pooled = stack.Pop();
                    pooledProps--;
                    if (pooled == null) continue;
                    pooled.transform.SetParent(parent, false);
                    return pooled;
                }
            }

            return Object.Instantiate(prefab, parent, false);
        }

        private void ReturnProp(GameObject prefab, GameObject instance)
        {
            propsTotal--;
            if (instance == null) return;
            if (pooledProps >= PropPoolCap || prefab == null)
            {
                Object.Destroy(instance);
                return;
            }

            if (!propPool.TryGetValue(prefab, out Stack<GameObject> stack)) propPool[prefab] = stack = new Stack<GameObject>();
            instance.transform.SetParent(poolRoot, false);
            stack.Push(instance);
            pooledProps++;
        }

        // ------------------------------------------------------------------ build

        /// <summary>
        /// The slices that turn <paramref name="tile"/>'s blueprint into scene objects; each MoveNext does one bounded piece
        /// of main-thread work. The tile stays invisible until its last slices, and the caller keeps any old tile at the same
        /// coordinate visible until this one is complete.
        /// </summary>
        public IEnumerator<string> Build(EndlessTile tile)
        {
            TileBlueprint bp = tile.Blueprint;
            bool near = tile.Lod == TileLod.Near;
            float ts = bp.TileSize > 0f ? bp.TileSize : world.Params.tileSize;
            float range = bp.HeightRange > 0f ? bp.HeightRange : world.Params.heightRange;
            EndlessFeatures features = world.Features;

            // 1. Root (a far mesh carries its own drop below the true ground).
            tile.Root = new GameObject($"Tile {tile.Coord.X},{tile.Coord.Z} ({tile.Lod})");
            tile.Root.transform.SetParent(world.TilesRoot, false);
            tile.Root.transform.localPosition = world.TileScenePosition(tile.Coord);
            yield return "root";

            if (!near)
            {
                IEnumerator<string> far = BuildFar(tile, features, ts);
                while (far.MoveNext()) yield return far.Current;
                yield break;
            }

            tile.Slot = TakeSlot();
            TerrainSlot slot = tile.Slot;
            slot.Uses++;
            TerrainData data = slot.Data;
            yield return "terrain slot";

            // 2. Heights (LOD rebuild deferred to its own slice).
            int hRes = bp.Heights.GetLength(0);
            if (data.heightmapResolution != hRes)
            {
                data.heightmapResolution = hRes;
                data.size = new Vector3(ts, range, ts);
                yield return "heightmap resolution";
            }
            else if (data.size.x != ts || data.size.y != range)
            {
                data.size = new Vector3(ts, range, ts);
            }

            TerrainDataWriter.WriteHeightsDeferred(data, bp.Heights);
            yield return "heights";
            TerrainDataWriter.FinishHeights(data);
            yield return "heightmap LOD";

            // 3. Paint: layers once per pooled data, then the alphamaps in bands.
            if (layers != null && bp.Splat != null)
            {
                int sRes = bp.Splat.GetLength(0);
                if (data.alphamapResolution != sRes)
                {
                    data.alphamapResolution = sRes;
                    data.baseMapResolution = Mathf.Clamp(sRes, 16, TerrainDataWriter.MaxBaseMapResolution);
                    slot.LayersAssigned = false;
                    yield return "alphamap resolution";
                }

                if (!slot.LayersAssigned)
                {
                    data.terrainLayers = layers;
                    slot.LayersAssigned = true;
                    yield return "terrain layers";
                }

                int channels = bp.Splat.GetLength(2);
                for (int z0 = 0; z0 < sRes; z0 += SplatBandRows)
                {
                    int rows = Math.Min(SplatBandRows, sRes - z0);
                    float[,,] band = Band(rows, sRes, layers.Length);
                    FillBand(bp.Splat, z0, rows, channels, band);
                    TerrainDataWriter.WriteSplatBand(data, z0, band);
                    yield return "alphamap band";
                }

                data.SetBaseMapDirty();
            }

            // 4. Trees: none on the terrain (no tree prototypes, no instances). The instanced renderer draws the tile's trees
            //    from the cells prepared on the worker thread once the terrain shows (step 9).

            // 5. Grass and flowers: prototypes, then one layer per slice (an empty set when grass is switched off).
            {
                // Per-tile lists: several tiles may be building at once, interleaved slice by slice.
                var detailPrototypes = new List<DetailPrototype>();
                var detailSources = new List<DetailLayer>();
                if (features.grassDetails && bp.DetailResolution > 0)
                {
                    foreach (DetailLayer layer in bp.Details)
                    {
                        DetailPrototype prototype = DetailPrototypeFor(layer.family, layer.variant);
                        if (prototype == null) continue;
                        detailPrototypes.Add(prototype);
                        detailSources.Add(layer);
                    }
                }

                data.detailPrototypes = detailPrototypes.ToArray();
                yield return "detail prototypes";

                int res = data.detailResolution;
                if (detailBuffer == null || detailBuffer.GetLength(0) != res) detailBuffer = new int[res, res];
                for (int l = 0; l < detailSources.Count; l++)
                {
                    WorldBuilder.ResampleDetail(detailSources[l].density, bp.DetailResolution, detailBuffer);
                    data.SetDetailLayer(0, 0, l, detailBuffer);
                    yield return "detail layer";
                }
            }

            // 6. Props, a few per slice, in groups that are switched on together at the end.
            tile.Places = new GameObject("Places");
            tile.Places.SetActive(false);
            tile.Places.transform.SetParent(tile.Root.transform, false);
            if (features.props)
            {
                Transform group = null;
                int inGroup = 0;
                foreach (PropPlacement prop in bp.Props)
                {
                    GameObject prefab = palette.GetPrefab(prop.role, prop.variant);
                    if (prefab == null)
                    {
                        if (missing.Add(prop.role)) Debug.LogWarning($"Endless road: the WorldPalette has no prefabs for family '{prop.role}'; those placements are skipped.");
                        continue;
                    }

                    if (group == null || inGroup >= PropsPerGroup)
                    {
                        var g = new GameObject("Props");
                        g.SetActive(false);
                        g.transform.SetParent(tile.Root.transform, false);
                        tile.Groups.Add(g);
                        group = g.transform;
                        inGroup = 0;
                    }

                    PlaceProp(tile, prop, prefab, group, ts, range);
                    inGroup++;
                    yield return "prop";
                }
            }

            // 7. Water plane.
            if (features.water && bp.WaterHeight >= 0f && palette.waterPlane != null)
            {
                tile.Water = TakeWater();
                tile.Water.transform.SetParent(tile.Root.transform, false);
                WorldBuilder.FitWaterPlane(tile.Water, palette.waterPlane, ts * 0.5f, ts * 0.5f, ts, bp.WaterHeight);
                yield return "water";
            }

            // 8. Places: settlement markers (arrival radius, chimney smoke and window lights).
            foreach (Waypoint w in bp.Waypoints)
            {
                if (w == null || !w.IsSettlement) continue;
                PlacePlace(tile, w, ts);
                yield return "place";
            }

            // 9. Collider last, then show the terrain and its trees, and its neighbours' seams.
            if (slot.Collider != null)
            {
                // Re-assigning the data makes a reused collider rebuild its heightfield from the new heights.
                if (slot.Uses > 1) slot.Collider.terrainData = null;
                slot.Collider.terrainData = data;
                slot.Collider.enabled = true;
                yield return "collider";
            }

            slot.Go.transform.SetParent(tile.Root.transform, false);
            slot.Go.transform.localPosition = Vector3.zero;
            slot.Go.SetActive(true);
            if (tile.Water != null) tile.Water.SetActive(true);
            if (tile.Trees != null && trees != null) trees.Add(tile.Trees, tile.Root.transform.position);
            yield return "show terrain";

            // The caller swaps the tile in (releases the old one, sets neighbours); groups and places are switched on in the
            // following slices.
            world.OnTileVisible(tile);
            yield return "swap and neighbours";

            foreach (GameObject g in tile.Groups)
            {
                g.SetActive(true);
                yield return "show props";
            }

            tile.Places.SetActive(true);
            yield return "show places";
        }

        /// <summary>The slices of a far tile: a pooled mesh object filled from the worker's mesh data in one slice, water, show.</summary>
        private IEnumerator<string> BuildFar(EndlessTile tile, EndlessFeatures features, float ts)
        {
            TileBlueprint bp = tile.Blueprint;
            FarTileMeshData mesh = tile.FarMeshData;
            if (mesh == null)
            {
                // Normally prepared on the worker thread; only a blueprint that arrived without it is built here.
                mesh = FarTileMeshData.Build(bp, FarTreeStyles.Defaults, new FarMeshSettings { IncludeTrees = features.farTrees });
                yield return "far mesh data (main thread)";
            }

            tile.FarMesh = TakeFarSlot();
            FarMeshSlot slot = tile.FarMesh;
            Upload(slot.Mesh, mesh);
            slot.Triangles = mesh.Triangles;
            tile.FarMeshData = null;   // uploaded; the CPU arrays are no longer needed
            yield return "far mesh";

            tile.Places = new GameObject("Places");
            tile.Places.SetActive(false);
            tile.Places.transform.SetParent(tile.Root.transform, false);

            if (features.water && bp.WaterHeight >= 0f && palette.waterPlane != null)
            {
                tile.Water = TakeWater();
                tile.Water.transform.SetParent(tile.Root.transform, false);
                // Lowered with the far ground so its shoreline matches.
                WorldBuilder.FitWaterPlane(tile.Water, palette.waterPlane, ts * 0.5f, ts * 0.5f, ts, bp.WaterHeight - FarDrop);
                yield return "water";
            }

            slot.Go.transform.SetParent(tile.Root.transform, false);
            slot.Go.transform.localPosition = Vector3.zero;
            slot.Go.SetActive(true);
            if (tile.Water != null) tile.Water.SetActive(true);
            farShown++;
            farTrianglesShown += slot.Triangles;
            tile.FarCounted = true;
            yield return "show far mesh";

            world.OnTileVisible(tile);
            yield return "swap";
        }

        private void PlaceProp(EndlessTile tile, PropPlacement prop, GameObject prefab, Transform group, float ts, float range)
        {
            bool measured = WorldBuilder.PropPose(palette, prop, prefab, out Vector3 position, out Quaternion rotation);
            GameObject go = TakeProp(prefab, group);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = Vector3.one * prop.scale;
            if (measured)
            {
                Vector3 corner = tile.Root.transform.position;
                float[,] heights = tile.Blueprint.Heights;
                bool keep = WorldBuilder.SafetyNet(go, prop.role,
                    at => HeightmapGenerator.SampleHeight(heights, ts, range, Mathf.Clamp(at.x - corner.x, 0f, ts), Mathf.Clamp(at.z - corner.z, 0f, ts)),
                    (GameObject instance, out Bounds b) => PrefabWorldBounds(instance, prefab, out b), safety);
                if (!keep)
                {
                    ReturnProp(prefab, go);
                    return;
                }
            }

            tile.Props.Add((prefab, go));
            MedievalZen.Player.SeatIndex.Register(go, prefab);   // logs, stumps, crates, low rocks, fires: things to sit on
            if (prop.role == PropRole.Windmill) WorldBuilder.AnimateWindmill(go, palette);
            if (prop.settlementId < 0) return;

            if (prop.role == PropRole.Cabin)
            {
                tile.Cabins.Add((go, prop, prefab));
                Waypoint place = world.FindWaypoint(prop.settlementId, tile.Coord);
                if (place != null && place.Kind == WaypointKind.Inn)
                {
                    WorldBuilder.PlaceInnDoor(prefab, prop, palette.FindFamily(prop.role), position, rotation, tile.Places.transform, Vector3.zero, place.Name ?? "the inn");
                }
            }
            else if (prop.role == PropRole.Fireplace)
            {
                tile.Fires.Add((go, prop.settlementId));
            }
        }

        /// <summary>World AABB of <paramref name="prefab"/>'s mesh bounds under <paramref name="instance"/>'s transform (valid while inactive).</summary>
        private static bool PrefabWorldBounds(GameObject instance, GameObject prefab, out Bounds bounds)
        {
            Bounds local = WorldBuilder.LocalBounds(prefab);
            Matrix4x4 m = instance.transform.localToWorldMatrix;
            Vector3 min = local.min, max = local.max;
            bounds = new Bounds(m.MultiplyPoint3x4(min), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                bounds.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z)));
            }

            return true;
        }

        private void PlacePlace(EndlessTile tile, Waypoint w, float ts)
        {
            Vector3 local = new Vector3((float)(w.X - tile.Coord.OriginX(ts)), w.Height, (float)(w.Z - tile.Coord.OriginZ(ts)));
            SettlementType type = RoadWindow.TypeFor(w.Kind);
            SettlementMarker marker = WorldBuilder.PlaceSettlementMarker(tile.Places.transform, -1, w.Name ?? "Inn", type, RoadWindow.RadiusFor(w), local);

            var cabinAnchors = new List<SettlementAmbience.CabinAnchor>();
            foreach ((GameObject cabin, PropPlacement prop, GameObject prefab) in tile.Cabins)
            {
                if (prop.settlementId != w.Id || cabin == null) continue;
                cabinAnchors.Add(new SettlementAmbience.CabinAnchor
                {
                    cabin = cabin.transform,
                    chimneyLocal = ChimneyOf(prefab),
                    lightLocal = WindowLightOf(prefab, palette.FindFamily(prop.role), cabin.transform),
                    smoke = true,
                });
            }

            var fireAnchors = new List<SettlementAmbience.FireAnchor>();
            foreach ((GameObject fire, int settlementId) in tile.Fires)
            {
                if (settlementId == w.Id && fire != null) fireAnchors.Add(new SettlementAmbience.FireAnchor { fireplace = fire.transform, local = new Vector3(0f, 0.15f, 0f) });
            }

            if (cabinAnchors.Count == 0 && fireAnchors.Count == 0) return;
            var ambience = marker.gameObject.AddComponent<SettlementAmbience>();
            ambience.Configure(cabinAnchors.ToArray(), fireAnchors.ToArray(), world.SmokePrefab, world.FirePrefab);
        }

        /// <summary>Chimney top in the cabin's local space: measured by the scene menu, else the top centre of its bounds.</summary>
        private Vector3 ChimneyOf(GameObject prefab)
        {
            if (chimneys.TryGetValue(prefab, out Vector3 measured)) return measured;
            Bounds b = WorldBuilder.LocalBounds(prefab);
            return new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
        }

        /// <summary>Window light just outside the door wall (placement +Z), 1.4 m up, in the cabin's local space.</summary>
        private static Vector3 WindowLightOf(GameObject prefab, PrefabFamily family, Transform cabin)
        {
            Bounds b = WorldBuilder.LocalBounds(prefab);
            Vector3 dir = Quaternion.Euler(0f, -(family?.yawOffset ?? 0f), 0f) * Vector3.forward;
            float half = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
            float scale = Mathf.Max(0.01f, cabin.localScale.y);
            return new Vector3(b.center.x, b.min.y + 1.4f / scale, b.center.z) + dir * (half + 0.45f / Mathf.Max(0.01f, cabin.localScale.x));
        }

        private DetailPrototype DetailPrototypeFor(string family, int variant)
        {
            if (detailCache.TryGetValue((family, variant), out DetailPrototype cached)) return cached;
            WorldBuilder.TryDetailPrototype(palette, family, variant, out DetailPrototype prototype);
            detailCache[(family, variant)] = prototype;
            return prototype;
        }

        private float[,,] Band(int rows, int width, int layerCount)
        {
            if (bandBuffer == null || bandBuffer.GetLength(0) != rows || bandBuffer.GetLength(1) != width || bandBuffer.GetLength(2) != layerCount)
            {
                bandBuffer = new float[rows, width, layerCount];
            }

            return bandBuffer;
        }

        /// <summary>Copies rows [z0, z0 + rows) of the blueprint splat into <paramref name="band"/>, folding channels onto the palette's layers.</summary>
        private void FillBand(float[,,] splat, int z0, int rows, int channels, float[,,] band)
        {
            int width = band.GetLength(1);
            int layerCount = band.GetLength(2);
            Array.Clear(band, 0, band.Length);
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        int l = c < channelToLayer.Length && channelToLayer[c] >= 0 ? channelToLayer[c] : 0;
                        if (l < layerCount) band[z, x, l] += splat[z0 + z, x, c];
                    }
                }
            }
        }

        private GameObject TakeWater()
        {
            // Returned inactive: it is switched on with the terrain.
            while (waterPool.Count > 0)
            {
                GameObject pooled = waterPool.Pop();
                if (pooled != null) return pooled;
            }

            GameObject water = Object.Instantiate(palette.waterPlane);
            water.SetActive(false);
            water.name = "Lake";
            if (palette.waterMaterial != null)
            {
                foreach (Renderer renderer in water.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = palette.waterMaterial;
            }

            foreach (Collider collider in water.GetComponentsInChildren<Collider>()) collider.enabled = false;
            return water;
        }

        // ------------------------------------------------------------------ release

        /// <summary>Hides a tile at once (terrain or far mesh, its trees, props, places, water) so the next one can take its place this frame.</summary>
        public void Hide(EndlessTile tile)
        {
            if (tile.Slot != null && tile.Slot.Go != null)
            {
                tile.Slot.Go.SetActive(false);
                if (tile.Slot.Collider != null) tile.Slot.Collider.enabled = false;
            }

            if (tile.FarMesh != null && tile.FarMesh.Go != null) tile.FarMesh.Go.SetActive(false);
            if (tile.FarCounted)
            {
                tile.FarCounted = false;
                farShown--;
                farTrianglesShown -= tile.FarMesh != null ? tile.FarMesh.Triangles : 0;
            }

            if (tile.Trees != null && trees != null) trees.Remove(tile.Trees);

            foreach (GameObject g in tile.Groups)
            {
                if (g != null) g.SetActive(false);
            }

            if (tile.Places != null) tile.Places.SetActive(false);
            if (tile.Water != null) tile.Water.SetActive(false);
        }

        /// <summary>
        /// Hands a hidden tile's objects back to the pools, <see cref="ReleasePerSlice"/> props per call. Returns true when
        /// the tile is completely released.
        /// </summary>
        public bool ReleaseSlice(EndlessTile tile)
        {
            int end = Math.Min(tile.Props.Count, tile.ReleaseCursor + ReleasePerSlice);
            for (; tile.ReleaseCursor < end; tile.ReleaseCursor++)
            {
                (GameObject prefab, GameObject instance) = tile.Props[tile.ReleaseCursor];
                ReturnProp(prefab, instance);
            }

            if (tile.ReleaseCursor < tile.Props.Count) return false;
            tile.Props.Clear();
            tile.Cabins.Clear();
            tile.Fires.Clear();
            if (tile.Slot != null)
            {
                Park(tile.Slot);
                tile.Slot = null;
            }

            if (tile.FarMesh != null)
            {
                ParkFar(tile.FarMesh);
                tile.FarMesh = null;
            }

            tile.FarMeshData = null;
            tile.Trees = null;
            if (tile.Water != null)
            {
                tile.Water.SetActive(false);
                tile.Water.transform.SetParent(poolRoot, false);
                waterPool.Push(tile.Water);
                tile.Water = null;
            }

            if (tile.Root != null) Object.Destroy(tile.Root);   // groups, places (markers, inn doors, ambience) go with it
            tile.Root = null;
            tile.Groups.Clear();
            tile.Places = null;
            tile.Blueprint = null;
            tile.State = TileState.Released;
            return true;
        }

        /// <summary>Releases everything immediately (scene end, new journey).</summary>
        public void ReleaseNow(EndlessTile tile)
        {
            Hide(tile);
            while (!ReleaseSlice(tile))
            {
            }
        }

        /// <summary>
        /// Scene end: destroys every in-memory TerrainData and far mesh this builder created, pooled or in use (the GameObjects
        /// are children of the streamer and go with the scene).
        /// </summary>
        public void DestroyAll()
        {
            foreach (TerrainData data in terrainDatas)
            {
                if (data != null) Object.Destroy(data);
            }

            foreach (Mesh mesh in farMeshes)
            {
                if (mesh != null) Object.Destroy(mesh);
            }

            terrainDatas.Clear();
            farMeshes.Clear();
            nearSlots.Clear();
            farSlots.Clear();
            propPool.Clear();
            pooledProps = 0;
            waterPool.Clear();
        }
    }
}
