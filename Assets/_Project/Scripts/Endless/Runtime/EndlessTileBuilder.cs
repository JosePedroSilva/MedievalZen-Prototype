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

        /// <summary>Most idle far mesh objects kept in the pool (cheap: a GameObject each; their meshes are freed when parked).</summary>
        public int FarPoolCap = 320;

        /// <summary>Most pooled prop instances kept (all prefabs together).</summary>
        public int PropPoolCap = 4000;

        /// <summary>Far mesh vertex layout (<see cref="FarVertex"/>, 16 bytes): no normal, the shader shades each triangle flat.</summary>
        private static readonly VertexAttributeDescriptor[] FarLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
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
        private readonly List<FarMeshSlot> farAll = new List<FarMeshSlot>();
        private readonly Dictionary<GameObject, Stack<GameObject>> propPool = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Stack<GameObject> waterPool = new Stack<GameObject>();
        private readonly Stack<StreamMeshSlot> streamPool = new Stack<StreamMeshSlot>();
        private readonly List<Mesh> streamMeshes = new List<Mesh>();

        private static readonly VertexAttributeDescriptor[] StreamLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
            // Colour before the texture coordinate: the VertexAttribute enum's order, which is the order Unity lays the buffer
            // out in whatever is written here (StreamVertex has the same field order).
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };
        private readonly Dictionary<(string, int), DetailPrototype> detailCache = new Dictionary<(string, int), DetailPrototype>();
        private readonly Dictionary<GameObject, Vector3> chimneys = new Dictionary<GameObject, Vector3>();
        private readonly HashSet<string> missing = new HashSet<string>();
        private readonly Dictionary<string, int[]> safety = new Dictionary<string, int[]>();
        private TerrainLayer[] layers;
        /// <summary>The endless layer sets by kind (index: 1 = mountain channels, 2 = field channels, 3 = both; 0 the shared eight) and their channel maps.</summary>
        private TerrainLayer[][] layerSets;
        private int[][] layerMaps;
        private int[,] detailBuffer;
        private float[,,] bandBuffer;
        private int created;
        private int farCreated;
        private int propsTotal;
        private int pooledProps;
        private int farShown;
        private int farTrianglesShown;
        private long farBytesShown;

        private readonly Dictionary<GameObject, long> prefabTriangles = new Dictionary<GameObject, long>();

        /// <summary>Triangles of every mesh under a prefab (all LODs; a weight for grouping props, cached).</summary>
        private long PrefabTriangles(GameObject prefab)
        {
            if (prefabTriangles.TryGetValue(prefab, out long t)) return t;
            t = 0;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) t += (long)mesh.GetIndexCount(sub) / 3;
            }

            prefabTriangles[prefab] = t;
            return t;
        }

        public EndlessTileBuilder(EndlessWorld world, WorldPalette palette, Transform poolRoot, Material farMaterial, EndlessTreeRenderer trees)
        {
            this.world = world;
            this.palette = palette;
            this.poolRoot = poolRoot;
            this.farMaterial = farMaterial;
            trees?.SetImpostorMaterial(farMaterial);   // second performance pass: the near tiles' far trees as solid impostors
            this.trees = trees;

            // Seasons pass (2026-10-06): the seasonal channels' layers are run-time copies the season painter tints (the assets never change).
            Seasons = new SeasonPainter(poolRoot);
            Seasons.LayersChanged = RefreshTerrainLayers;
            layers = TerrainDataWriter.ResolveLayers(Seasons.Layers(palette.terrainLayers, 0), TerrainShaper.LayerCount, out int[] shared);
            // The endless road's mud is the matt copy: the Synty layer's gloss mirrored the sky wherever mud lies in the open.
            // Only when the palette really has a mud layer (a missing one folds onto another layer's slot).
            if (layers != null && palette.endlessMudLayer != null && palette.terrainLayers != null &&
                TerrainShaper.MudLayer < palette.terrainLayers.Length && palette.terrainLayers[TerrainShaper.MudLayer] != null &&
                TerrainShaper.MudLayer < shared.Length)
            {
                int mud = shared[TerrainShaper.MudLayer];
                if (mud >= 0 && mud < layers.Length) layers[mud] = palette.endlessMudLayer;
            }

            if (layers != null) BuildLayerSets(layers, shared, Seasons.Layers(palette.endlessTerrainLayers, TerrainShaper.LayerCount), out layerSets, out layerMaps);
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

        /// <summary>GPU memory of the far meshes currently shown (vertex and index buffers), bytes.</summary>
        public long FarMeshBytes => farBytesShown;

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
                if (slot.Go != null) return slot;
            }

            var go = new GameObject("Far tile");
            go.SetActive(false);
            go.transform.SetParent(poolRoot, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            // One material for every far tile (flat colours live in the vertices): the SRP Batcher batches them all.
            renderer.sharedMaterial = farMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            farCreated++;
            var created = new FarMeshSlot { Go = go, Filter = filter, Renderer = renderer };
            farAll.Add(created);
            return created;
        }

        private void ParkFar(FarMeshSlot slot)
        {
            if (slot == null || slot.Go == null) return;
            slot.Go.SetActive(false);
            slot.Go.transform.SetParent(poolRoot, false);
            DestroyFarMesh(slot);   // the next use uploads a fresh mesh; a parked one would only hold GPU memory
            if (farSlots.Count < FarPoolCap)
            {
                farSlots.Push(slot);
                return;
            }

            Object.Destroy(slot.Go);
            farAll.Remove(slot);
        }

        private void DestroyFarMesh(FarMeshSlot slot)
        {
            if (slot.Mesh == null) return;
            farMeshes.Remove(slot.Mesh);
            Object.Destroy(slot.Mesh);
            slot.Mesh = null;
            if (slot.Filter != null) slot.Filter.sharedMesh = null;
        }

        /// <summary>
        /// Writes prepared far-tile data into a fresh mesh in one go (vertex + index buffers, bounds, upload) and drops the slot's
        /// previous mesh. The mesh is uploaded non-readable, so its CPU copy is freed (nothing reads far meshes back).
        /// </summary>
        private void Upload(FarMeshSlot slot, FarTileMeshData d)
        {
            DestroyFarMesh(slot);
            var mesh = new Mesh { name = "Endless far tile" };
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
            mesh.UploadMeshData(true);
            farMeshes.Add(mesh);
            slot.Mesh = mesh;
            slot.Filter.sharedMesh = mesh;
            slot.Triangles = d.Triangles;
            slot.Bytes = (long)d.VertexCount * 16L + (long)d.IndexCount * 2L;
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

        // ------------------------------------------------------------------ heavy prop warm-up (third performance pass, 2026-10-06)

        private readonly List<GameObject> heavyPrefabs = new List<GameObject>();
        private readonly HashSet<GameObject> heavyTouched = new HashSet<GameObject>();
        private readonly List<(GameObject prefab, GameObject instance)> warmStaged = new List<(GameObject, GameObject)>();
        private readonly System.Diagnostics.Stopwatch warmClock = new System.Diagnostics.Stopwatch();
        private readonly List<Collider> colliderScratch = new List<Collider>();
        private bool heavyListed;
        private int warmCursor;
        private int stockCursor;
        private int stockMade;
        private Transform warmStage;

        /// <summary>Heavy prefabs (<see cref="PropWarmup"/>) the palette places as GameObjects at places.</summary>
        public int HeavyPrefabCount
        {
            get
            {
                ListHeavyPrefabs();
                return heavyPrefabs.Count;
            }
        }

        /// <summary>Heavy prefabs switched on once behind a black screen so far (their first use is paid).</summary>
        public int HeavyPrefabsWarmed => heavyTouched.Count;

        /// <summary>Instances made ahead of need by the warm-up and the stock (in the pool until a tile takes them).</summary>
        public int HeavyStockMade => stockMade;

        /// <summary>Idle pooled instances of heavy prefabs right now.</summary>
        public int HeavyStockIdle
        {
            get
            {
                int n = 0;
                foreach (GameObject prefab in heavyPrefabs) n += PoolCount(prefab);
                return n;
            }
        }

        /// <summary>True while a warm-up pass (started by <see cref="BeginWarmup"/>) has prefabs left or props on the stage.</summary>
        public bool WarmPending => warmCursor < heavyPrefabs.Count || warmStaged.Count > 0;

        /// <summary>Starts a warm-up pass over every heavy prefab (at each hold: start, Go now, outrun).</summary>
        public void BeginWarmup()
        {
            ListHeavyPrefabs();
            warmCursor = 0;
        }

        /// <summary>Ends the warm-up pass now: staged props go to the pool (inactive), the rest wait for the next hold or the stock.</summary>
        public void EndWarmup()
        {
            ParkStaged();
            warmCursor = heavyPrefabs.Count;
        }

        /// <summary>
        /// One frame of the warm-up behind a black screen: parks last frame's staged props in the pool, then for up to
        /// <see cref="PropWarmup.WarmPerFrame"/> heavy prefabs (within <see cref="PropWarmup.WarmBudgetMs"/>) either stages a never-used
        /// one active at <paramref name="stagePosition"/> (in front of the camera, so it is drawn and casts once: mesh upload, shader
        /// and shadow variants, renderer registration, collider cooking) or tops its pool up to one idle instance. Returns the
        /// milliseconds spent.
        /// </summary>
        public float WarmStep(Vector3 stagePosition, Quaternion stageRotation)
        {
            warmClock.Restart();
            ParkStaged();
            if (warmCursor >= heavyPrefabs.Count) return (float)warmClock.Elapsed.TotalMilliseconds;
            if (warmStage == null)
            {
                var stage = new GameObject("Prop warm-up stage");
                stage.transform.SetParent(world.transform, false);
                warmStage = stage.transform;
            }

            warmStage.SetPositionAndRotation(stagePosition, stageRotation);
            int done = 0;
            while (warmCursor < heavyPrefabs.Count && done < PropWarmup.WarmPerFrame && warmClock.Elapsed.TotalMilliseconds < PropWarmup.WarmBudgetMs)
            {
                GameObject prefab = heavyPrefabs[warmCursor++];
                if (prefab == null) continue;
                if (!heavyTouched.Contains(prefab))
                {
                    // Instantiated under the active stage: awake, registered and drawn this frame; parked next frame.
                    GameObject go = Object.Instantiate(prefab, warmStage, false);
                    warmStaged.Add((prefab, go));
                    heavyTouched.Add(prefab);
                    stockMade++;
                    done++;
                }
                else if (PoolCount(prefab) == 0 && StockInstance(prefab))
                {
                    done++;
                }
            }

            return (float)warmClock.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Makes one idle instance (inactive, in the pool) of the next heavy prefab whose pool is empty; false when every heavy
        /// prefab has one (the caller decides when: <see cref="PropWarmup.ShouldStock"/>). At most one instantiate per call.
        /// </summary>
        public bool StockOne()
        {
            ListHeavyPrefabs();
            for (int i = 0; i < heavyPrefabs.Count; i++)
            {
                GameObject prefab = heavyPrefabs[(stockCursor + i) % heavyPrefabs.Count];
                if (prefab == null || PoolCount(prefab) > 0) continue;
                stockCursor = (stockCursor + i + 1) % heavyPrefabs.Count;
                return StockInstance(prefab);
            }

            return false;
        }

        /// <summary>True when some heavy prefab has no idle instance in the pool (and the pool has room).</summary>
        public bool AnyHeavyPoolEmpty
        {
            get
            {
                if (pooledProps >= PropPoolCap) return false;
                foreach (GameObject prefab in heavyPrefabs)
                {
                    if (prefab != null && PoolCount(prefab) == 0) return true;
                }

                return false;
            }
        }

        private int PoolCount(GameObject prefab) => prefab != null && propPool.TryGetValue(prefab, out Stack<GameObject> s) ? s.Count : 0;

        private bool StockInstance(GameObject prefab)
        {
            if (pooledProps >= PropPoolCap) return false;
            GameObject go = Object.Instantiate(prefab, poolRoot, false);   // under the inactive pool root: nothing awakes
            if (!propPool.TryGetValue(prefab, out Stack<GameObject> stack)) propPool[prefab] = stack = new Stack<GameObject>();
            stack.Push(go);
            pooledProps++;
            stockMade++;
            return true;
        }

        private void ParkStaged()
        {
            foreach ((GameObject prefab, GameObject instance) in warmStaged)
            {
                if (instance == null) continue;
                if (pooledProps >= PropPoolCap)
                {
                    Object.Destroy(instance);
                    continue;
                }

                instance.transform.SetParent(poolRoot, false);
                if (!propPool.TryGetValue(prefab, out Stack<GameObject> stack)) propPool[prefab] = stack = new Stack<GameObject>();
                stack.Push(instance);
                pooledProps++;
            }

            warmStaged.Clear();
        }

        /// <summary>The palette's heavy GameObject prefabs at places, heaviest first (listed once).</summary>
        private void ListHeavyPrefabs()
        {
            if (heavyListed) return;
            heavyListed = true;
            var seen = new HashSet<GameObject>();
            var weighed = new List<(GameObject prefab, long triangles)>();
            foreach (PrefabFamily family in palette.families)
            {
                if (family == null || !PropWarmup.IsCandidateFamily(family.id) || family.prefabs == null) continue;
                foreach (GameObject prefab in family.prefabs)
                {
                    if (prefab == null || !seen.Add(prefab)) continue;
                    long t = PrefabTriangles(prefab);
                    if (PropWarmup.IsHeavy(t)) weighed.Add((prefab, t));
                }
            }

            weighed.Sort((a, b) => b.triangles.CompareTo(a.triangles));
            foreach ((GameObject prefab, long _) in weighed) heavyPrefabs.Add(prefab);
            long total = 0;
            foreach ((GameObject _, long t) in weighed) total += t;
            Debug.Log($"Endless road prop warm-up: {heavyPrefabs.Count} heavy prefabs (≥ {PropWarmup.HeavyTriangles} triangles, {total / 1000}k in all): " +
                      string.Join(", ", weighed.ConvertAll(w => $"{w.prefab.name} {w.triangles / 1000}k")) + ".");
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

                // The pass ranges' tiles add mountain rock, scree and heath to the shared eight, the farmland's tiles the field gold,
                // yellow and green (eleven layers, three passes), a tile with both all fourteen (four passes); a pooled TerrainData
                // switches sets only when a tile of another kind reuses it. A wood's tiles draw the leaf litter in the cobblestone's slot.
                int kind = EndlessLayerSets.LayerSetKind(bp.MountainPaint, bp.FieldPaint, bp.LitterPaint, bp.CobblePaint);
                TerrainLayer[] set = layerSets != null ? layerSets[kind] : layers;
                int[] map = layerMaps != null ? layerMaps[kind] : null;
                if (!slot.LayersAssigned || slot.LayerSet != set)
                {
                    data.terrainLayers = set;
                    slot.LayersAssigned = true;
                    slot.LayerSet = set;
                    yield return "terrain layers";
                }

                int channels = bp.Splat.GetLength(2);
                int bandRows = ApplySlicing.SplatRows(set.Length);   // second performance pass: 16 rows for 9-12 layers
                for (int z0 = 0; z0 < sRes; z0 += bandRows)
                {
                    int rows = Math.Min(bandRows, sRes - z0);
                    float[,,] band = Band(rows, sRes, set.Length);
                    FillBand(bp.Splat, z0, rows, channels, band, map);
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
            Dictionary<int, (string label, GameObject prefab, List<Collider> colliders)> heavyGroups = null;   // by group index
            if (features.props)
            {
                Transform group = null;
                int inGroup = 0;
                long groupTriangles = 0;
                foreach (PropPlacement prop in bp.Props)
                {
                    if (prop.role == PropRole.InnSign)
                    {
                        // Built in code, lettered with the inn's name (no palette family).
                        Waypoint inn = world.FindWaypoint(prop.settlementId, tile.Coord);
                        SettlementLife.PlaceNameBoard(prop, tile.Places.transform, inn != null ? inn.Name : null, palette);
                        continue;
                    }

                    GameObject prefab = palette.GetPrefab(prop.role, prop.variant);
                    if (prefab == null)
                    {
                        if (missing.Add(prop.role)) Debug.LogWarning($"Endless road: the WorldPalette has no prefabs for family '{prop.role}'; those placements are skipped.");
                        continue;
                    }

                    // Second performance pass: a group is switched on in one slice ("show props"), so it also closes at a triangle
                    // budget; a heavy building gets a group of its own.
                    long propTriangles = PrefabTriangles(prefab);
                    if (group == null || ApplySlicing.StartsNewGroup(inGroup, groupTriangles, propTriangles, PropsPerGroup))
                    {
                        groupTriangles = 0;
                        var g = new GameObject("Props");
                        g.SetActive(false);
                        g.transform.SetParent(tile.Root.transform, false);
                        tile.Groups.Add(g);
                        group = g.transform;
                        inGroup = 0;
                    }

                    int placedBefore = tile.Props.Count;
                    PlaceProp(tile, prop, prefab, group, ts, range);
                    if (PropWarmup.IsHeavy(propTriangles) && tile.Props.Count > placedBefore)
                    {
                        // Third performance pass: a heavy prop's colliders are switched on a slice after its renderers, and the
                        // group's slice is named after it (so the tour's worst slice says which building, and whether it was cold).
                        GameObject placed = tile.Props[tile.Props.Count - 1].instance;
                        heavyGroups ??= new Dictionary<int, (string label, GameObject prefab, List<Collider> colliders)>();
                        int index = tile.Groups.Count - 1;
                        heavyGroups.TryGetValue(index, out var entry);
                        entry.colliders ??= new List<Collider>();
                        placed.GetComponentsInChildren(true, colliderScratch);
                        foreach (Collider c in colliderScratch)
                        {
                            c.enabled = false;
                            entry.colliders.Add(c);
                        }

                        colliderScratch.Clear();
                        entry.prefab = prefab;
                        entry.label = heavyTouched.Contains(prefab) ? $"show {prefab.name}" : $"show {prefab.name} (first use)";
                        heavyGroups[index] = entry;
                    }

                    inGroup++;
                    groupTriangles += propTriangles;
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

            // 7b. Streams (prepared on the worker thread): water and banks in one mesh, the banks again as a collider.
            if (features.water && bp.StreamMesh != null && bp.StreamMesh.VertexCount > 0)
            {
                tile.Streams = TakeStreamSlot();
                if (tile.Streams != null)
                {
                    UploadStream(tile.Streams.Mesh, bp.StreamMesh);
                    tile.Streams.Go.transform.SetParent(tile.Root.transform, false);
                    tile.Streams.Go.transform.localPosition = Vector3.zero;
                    yield return "streams";
                    if (UploadStreamCollider(tile.Streams, bp.StreamMesh)) yield return "stream banks collider";
                }
            }

            // 7c. Rock skin (prepared on the worker thread): the steep mountain rock drawn triplanar over the terrain; one upload,
            // destroyed with the tile's root.
            if (RockSkin.Attach(tile.Root.transform, bp.RockSkin)) yield return "rock skin";

            // 7d. Bridge walls (prepared on the worker thread): parapets, end posts and approach walls in the bridge stone, with
            // their box colliders; one upload, destroyed with the tile's root.
            if (BridgeWallsView.Attach(tile.Root.transform, bp.BridgeWalls, palette.bridgeMaterial)) yield return "bridge walls";

            // 7e. Terrace decks (prepared on the worker thread): a walled city's cobbled decks over its terrace banks, with their box
            // colliders; one upload, destroyed with the tile's root.
            if (TerraceDeckView.Attach(tile.Root.transform, bp.TerraceDecks, palette.terraceDeckMaterial)) yield return "terrace decks";

            // 8. Places: settlement markers (arrival radius, chimney smoke and window lights).
            foreach (Waypoint w in bp.Waypoints)
            {
                if (w == null || !w.IsSettlement) continue;
                PlacePlace(tile, w, ts);
                yield return "place";
            }

            // 8b. The houses here of places whose waypoint lies in another tile: their own smoke and lights.
            foreach (int settlementId in SettlementLife.SettlementsWithoutWaypoint(tile.Cabins, bp.Waypoints))
            {
                PlaceSpreadAmbience(tile, bp.Waypoints, settlementId);
                yield return "place ambience";
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
            if (tile.Streams != null)
            {
                tile.Streams.Go.SetActive(true);
                if (tile.Streams.Collider != null) tile.Streams.Collider.enabled = tile.Streams.Collider.sharedMesh != null;
            }

            if (bp.Streams != null && bp.Streams.Count > 0) StreamWaterIndex.Register(tile.Coord, bp);
            if (tile.Trees != null && trees != null) trees.Add(tile.Trees, tile.Root.transform.position);
            yield return "show terrain";

            // The caller swaps the tile in (releases the old one, sets neighbours); groups and places are switched on in the
            // following slices.
            world.OnTileVisible(tile);
            yield return "swap and neighbours";

            for (int gi = 0; gi < tile.Groups.Count; gi++)
            {
                GameObject g = tile.Groups[gi];
                if (g == null) continue;
                if (heavyGroups == null || !heavyGroups.TryGetValue(gi, out var heavy))
                {
                    g.SetActive(true);
                    yield return "show props";
                    continue;
                }

                // A heavy building: renderers in this slice, its colliders in the next (PhysX shapes; cooked once per mesh).
                g.SetActive(true);
                if (heavy.prefab != null) heavyTouched.Add(heavy.prefab);
                yield return heavy.label;
                foreach (Collider c in heavy.colliders)
                {
                    if (c != null) c.enabled = true;
                }

                yield return "heavy prop colliders";
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
                mesh = FarTileMeshData.Build(bp, world.FarStyles, world.FarMeshSettingsFor(features));
                yield return "far mesh data (main thread)";
            }

            tile.FarMesh = TakeFarSlot();
            FarMeshSlot slot = tile.FarMesh;
            Upload(slot, mesh);
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
            farBytesShown += slot.Bytes;
            tile.FarCounted = true;
            yield return "show far mesh";

            world.OnTileVisible(tile);
            yield return "swap";
        }

        /// <summary>The bridge's lighter first-party stone (WorldPalette.bridgeMaterial) in place of the pack's palette material, on every slot.</summary>
        private void UseBridgeStone(GameObject go)
        {
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || materials[i] == palette.bridgeMaterial) continue;
                    materials[i] = palette.bridgeMaterial;
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private void PlaceProp(EndlessTile tile, PropPlacement prop, GameObject prefab, Transform group, float ts, float range)
        {
            bool measured = WorldBuilder.PropPose(palette, prop, prefab, out Vector3 position, out Quaternion rotation);
            GameObject go = TakeProp(prefab, group);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            // The stone bridge is stretched along the road (BridgeShape: a gentler hump, a wider arch); everything else scales uniformly.
            go.transform.localScale = prop.role == PropRole.Bridge
                ? new Vector3(Generation.BridgeShape.ScaleX, Generation.BridgeShape.ScaleY, Generation.BridgeShape.ScaleZ) * prop.scale
                : Vector3.one * prop.scale;
            if (prop.role == PropRole.Bridge && palette.bridgeMaterial != null) UseBridgeStone(go);
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
            if (prop.role == PropRole.Windmill || prop.role == PropRole.LandmarkMill) WorldBuilder.AnimateWindmill(go, palette);
            if (prop.settlementId < 0) return;

            if (SettlementLife.HasAmbience(prop.role))
            {
                // Dwellings smoke and light their windows, lamps light up; the inn's building gets its rest point at the door.
                tile.Cabins.Add((go, prop, prefab));
                if (SettlementLife.IsInnBuilding(prop.role))
                {
                    Waypoint place = world.FindWaypoint(prop.settlementId, tile.Coord);
                    if (place != null && place.Kind == WaypointKind.Inn)
                    {
                        SettlementLife.PlaceInnDoor(prefab, prop, palette.FindFamily(prop.role), position, rotation, tile.Places.transform, place.Name ?? "the inn");
                    }
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
                cabinAnchors.Add(CabinAnchorOf(cabin, prop, prefab));
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

        /// <summary>The ambience anchor of a dwelling or lamp: measured buildings and lamps by <see cref="SettlementLife"/>, the cabin as before.</summary>
        private SettlementAmbience.CabinAnchor CabinAnchorOf(GameObject cabin, PropPlacement prop, GameObject prefab)
        {
            if (SettlementLife.TryAnchor(cabin, prop, prefab, out SettlementAmbience.CabinAnchor anchor)) return anchor;
            return new SettlementAmbience.CabinAnchor
            {
                cabin = cabin.transform,
                chimneyLocal = ChimneyOf(prefab),
                lightLocal = WindowLightOf(prefab, palette.FindFamily(prop.role), cabin.transform),
                smoke = true,
            };
        }

        /// <summary>
        /// Smoke and lights for the part of a place that lies in this tile while its waypoint lies in another (a village spreads over
        /// several tiles): one ambience per such settlement, at the middle of its dwellings here.
        /// </summary>
        private void PlaceSpreadAmbience(EndlessTile tile, List<Waypoint> waypoints, int settlementId)
        {
            var anchors = new List<SettlementAmbience.CabinAnchor>();
            Vector3 sum = Vector3.zero;
            foreach ((GameObject cabin, PropPlacement prop, GameObject prefab) in tile.Cabins)
            {
                if (prop.settlementId != settlementId || cabin == null) continue;
                anchors.Add(CabinAnchorOf(cabin, prop, prefab));
                sum += cabin.transform.localPosition;
            }

            var fires = new List<SettlementAmbience.FireAnchor>();
            foreach ((GameObject fire, int id) in tile.Fires)
            {
                if (id == settlementId && fire != null) fires.Add(new SettlementAmbience.FireAnchor { fireplace = fire.transform, local = new Vector3(0f, 0.15f, 0f) });
            }

            if (anchors.Count == 0) return;
            var go = new GameObject($"Ambience {settlementId} {tile.Coord.X},{tile.Coord.Z}");
            go.transform.SetParent(tile.Places.transform, false);
            go.transform.localPosition = sum / anchors.Count;
            go.AddComponent<SettlementAmbience>().Configure(anchors.ToArray(), fires.ToArray(), world.SmokePrefab, world.FirePrefab);
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

        /// <summary>The seasons' painter of this builder's terrain layers and sward copies (ticked by the streamer once a frame).</summary>
        internal readonly SeasonPainter Seasons;

        /// <summary>Seasons pass: hands every live near terrain its layers again, so a season's new layer tints show at once.</summary>
        private void RefreshTerrainLayers()
        {
            foreach (TerrainData data in terrainDatas)
            {
                if (data != null && data.terrainLayers != null && data.terrainLayers.Length > 0) data.terrainLayers = data.terrainLayers;
            }
        }

        private DetailPrototype DetailPrototypeFor(string family, int variant)
        {
            if (detailCache.TryGetValue((family, variant), out DetailPrototype cached)) return cached;
            WorldBuilder.TryDetailPrototype(palette, family, variant, out DetailPrototype prototype);
            prototype = Seasons.Detail(prototype, family);   // seasons pass: grass clumps draw recolourable run-time copies
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

        /// <summary>
        /// The endless road's layer sets (<see cref="EndlessLayerSets.LayerSetKind"/>; the leaf litter in the cobblestone's slot on the woods' tiles) from the palette's shared layers (resolved, <paramref name="shared"/>
        /// mapping the eight shared channels onto them) and its endless layers <paramref name="extra"/> (channel 8 on: mountain rock, scree,
        /// heath, field gold, yellow, green): each set appends the groups its kind uses (<see cref="EndlessLayerSets.ChannelSlots"/>); a channel left out folds
        /// onto its shared stand-in (it is 0 on the tiles that use that set). Warns about missing layers.
        /// </summary>
        private static void BuildLayerSets(TerrainLayer[] layers, int[] shared, TerrainLayer[] extra, out TerrainLayer[][] sets, out int[][] maps)
        {
            int extras = TerrainShaper.EndlessLayerCount - TerrainShaper.LayerCount;
            var available = new bool[extras];
            var missing = new List<string>();
            for (int e = 0; e < extras; e++)
            {
                available[e] = extra != null && e < extra.Length && extra[e] != null;
                if (!available[e]) missing.Add(TerrainShaper.EndlessLayerNames[TerrainShaper.LayerCount + e]);
            }

            sets = new TerrainLayer[EndlessLayerSets.KindCount][];
            maps = new int[EndlessLayerSets.KindCount][];
            int cobbleIndex = TerrainShaper.CobbleLayer < shared.Length ? shared[TerrainShaper.CobbleLayer] : -1;
            for (int kind = 0; kind < EndlessLayerSets.KindCount; kind++)
            {
                // The woods' leaf litter takes the cobblestone's slot (no extra pass); with no cobblestone layer to replace it is appended.
                int k = cobbleIndex < 0 && (kind & EndlessLayerSets.LitterBit) != 0 ? kind | EndlessLayerSets.LitterAppendedBit : kind;
                int[] slots = EndlessLayerSets.ChannelSlots(k, available, out int[] appended);
                var set = new List<TerrainLayer>(layers);
                bool litterSlot = EndlessLayerSets.LitterInCobbleSlot(k, available);
                if (litterSlot) set[cobbleIndex] = extra[TerrainShaper.LeafLitterLayer - TerrainShaper.LayerCount];
                foreach (int c in appended) set.Add(extra[c - TerrainShaper.LayerCount]);
                // Shared channels resolve through the palette (a missing shared layer folds as TerrainDataWriter does); appended ones follow the set.
                var map = new int[TerrainShaper.EndlessLayerCount];
                for (int c = 0; c < map.Length; c++)
                    map[c] = slots[c] < TerrainShaper.LayerCount ? (slots[c] < shared.Length ? shared[slots[c]] : -1) : layers.Length + (slots[c] - TerrainShaper.LayerCount);
                sets[kind] = set.Count > layers.Length || litterSlot ? set.ToArray() : layers;
                maps[kind] = map;
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning($"WorldPalette has no endless layer for {string.Join(", ", missing)}; the pass ranges and fields paint them with shared stand-ins " +
                                 "(rock, dry grass, flower meadow, lush grass; mud for the leaf litter). Rebuild the palette (MedievalZen/Create Default World Palette).");
            }
        }


        /// <summary>Copies rows [z0, z0 + rows) of the blueprint splat into <paramref name="band"/>, folding channels onto the layers of the tile's set.</summary>
        private void FillBand(float[,,] splat, int z0, int rows, int channels, float[,,] band, int[] channelToLayer)
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

        // ------------------------------------------------------------------ streams

        /// <summary>
        /// A pooled stream mesh object (inactive) with the water and bank materials, or null when the palette has no water material
        /// at all. The renderer receives shadows (the banks are ground) and casts none.
        /// </summary>
        private StreamMeshSlot TakeStreamSlot()
        {
            Material water = palette.streamMaterial != null ? palette.streamMaterial : palette.waterMaterial;
            if (water == null) return null;
            Material bank = palette.streamBankMaterial != null ? palette.streamBankMaterial : water;
            if (palette.streamBankMaterial == null && missing.Add("streamBankMaterial"))
            {
                Debug.LogWarning("Endless road: the world palette has no stream bank material (MedievalZen/Create Default World Palette builds it); the banks are drawn with the water's.");
            }

            while (streamPool.Count > 0)
            {
                StreamMeshSlot pooled = streamPool.Pop();
                if (pooled.Go != null && pooled.Mesh != null)
                {
                    pooled.Renderer.sharedMaterials = new[] { water, bank };
                    return pooled;
                }
            }

            var go = new GameObject("Streams");
            go.SetActive(false);
            go.transform.SetParent(poolRoot, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            var mesh = new Mesh { name = "Endless streams" };
            streamMeshes.Add(mesh);
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { water, bank };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            var collider = go.AddComponent<MeshCollider>();
            collider.enabled = false;
            var colliderMesh = new Mesh { name = "Endless stream banks (collider)" };
            streamMeshes.Add(colliderMesh);
            return new StreamMeshSlot { Go = go, Renderer = renderer, Mesh = mesh, Collider = collider, ColliderMesh = colliderMesh };
        }

        private void ParkStream(StreamMeshSlot slot)
        {
            if (slot == null || slot.Go == null) return;
            slot.Go.SetActive(false);
            if (slot.Collider != null)
            {
                slot.Collider.enabled = false;
                slot.Collider.sharedMesh = null;
            }

            slot.Go.transform.SetParent(poolRoot, false);
            if (streamPool.Count < 64)
            {
                streamPool.Push(slot);
                return;
            }

            Object.Destroy(slot.Go);
            foreach (Mesh mesh in new[] { slot.Mesh, slot.ColliderMesh })
            {
                if (mesh == null) continue;
                streamMeshes.Remove(mesh);
                Object.Destroy(mesh);
            }
        }

        private static readonly SubMeshDescriptor[] StreamSubMeshes = new SubMeshDescriptor[2];

        /// <summary>Writes prepared stream mesh data into a pooled mesh in one go: the water (submesh 0) and the banks (submesh 1).</summary>
        private static void UploadStream(Mesh mesh, StreamRibbonMeshData d)
        {
            const MeshUpdateFlags flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;
            var bounds = new Bounds();
            bounds.SetMinMax(d.BoundsMin, d.BoundsMax);
            mesh.SetVertexBufferParams(d.VertexCount, StreamLayout);
            mesh.SetVertexBufferData(d.Vertices, 0, 0, d.VertexCount, 0, flags);
            mesh.SetIndexBufferParams(d.IndexCount, IndexFormat.UInt16);
            mesh.SetIndexBufferData(d.Indices, 0, 0, d.IndexCount, flags);
            // Both at once: a pooled mesh still holds its last stream's ranges, and setting the water alone first overlapped the
            // old banks' range (Unity warned "SetSubMesh #0 shares part of its index buffer with SubMesh #1" on every reuse).
            StreamSubMeshes[0] = new SubMeshDescriptor(0, d.WaterIndexCount, MeshTopology.Triangles) { bounds = bounds, firstVertex = 0, vertexCount = d.VertexCount };
            StreamSubMeshes[1] = new SubMeshDescriptor(d.WaterIndexCount, d.BankIndexCount, MeshTopology.Triangles) { bounds = bounds, firstVertex = 0, vertexCount = d.VertexCount };
            mesh.SetSubMeshes(StreamSubMeshes, flags);
            mesh.bounds = bounds;
            mesh.UploadMeshData(false);
        }

        /// <summary>Gives the stream's collider the bank triangles (the water has none); false when there are none.</summary>
        private static bool UploadStreamCollider(StreamMeshSlot slot, StreamRibbonMeshData d)
        {
            if (slot.Collider == null || slot.ColliderMesh == null || d.ColliderPositions == null || d.ColliderIndices == null || d.ColliderIndices.Length == 0)
            {
                if (slot.Collider != null) slot.Collider.sharedMesh = null;
                return false;
            }

            Mesh mesh = slot.ColliderMesh;
            mesh.Clear();
            mesh.indexFormat = d.ColliderPositions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(d.ColliderPositions);
            mesh.SetIndices(d.ColliderIndices, MeshTopology.Triangles, 0, false);
            mesh.RecalculateBounds();
            slot.Collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
            slot.Collider.sharedMesh = mesh;
            return true;
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
                farBytesShown -= tile.FarMesh != null ? tile.FarMesh.Bytes : 0;
            }

            if (tile.Trees != null && trees != null) trees.Remove(tile.Trees);

            foreach (GameObject g in tile.Groups)
            {
                if (g != null) g.SetActive(false);
            }

            if (tile.Places != null) tile.Places.SetActive(false);
            if (tile.Water != null) tile.Water.SetActive(false);
            if (tile.Streams != null && tile.Streams.Go != null) tile.Streams.Go.SetActive(false);
            if (tile.Blueprint != null) StreamWaterIndex.Unregister(tile.Coord, tile.Blueprint);
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

            if (tile.Streams != null)
            {
                ParkStream(tile.Streams);
                tile.Streams = null;
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
            Seasons.Dispose();
            foreach (TerrainData data in terrainDatas)
            {
                if (data != null) Object.Destroy(data);
            }

            foreach (Mesh mesh in farMeshes)
            {
                if (mesh != null) Object.Destroy(mesh);
            }

            farAll.Clear();

            foreach (Mesh mesh in streamMeshes)
            {
                if (mesh != null) Object.Destroy(mesh);
            }

            streamMeshes.Clear();
            streamPool.Clear();
            StreamWaterIndex.Clear();
            terrainDatas.Clear();
            farMeshes.Clear();
            nearSlots.Clear();
            farSlots.Clear();
            propPool.Clear();
            pooledProps = 0;
            waterPool.Clear();
            warmStaged.Clear();   // staged instances are children of the streamer and go with the scene
            warmCursor = heavyPrefabs.Count;
        }
    }
}
