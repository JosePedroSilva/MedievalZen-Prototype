using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Draws the near tiles' trees with <see cref="Graphics.RenderMeshInstanced{T}(in RenderParams, Mesh, int, T[], int, int)"/>
    /// instead of Unity terrain trees (no legacy billboards, no per-terrain tree work). At start every tree prefab of the
    /// palette is taken apart into mesh parts per LOD (LODGroup: LOD0, the middle LOD and the last LOD; a prefab without one
    /// uses its meshes for all three) with instancing-enabled runtime copies of its materials (the Synty assets are
    /// untouched). Each frame one pass over the registered 32 m cells sorts the cells in the camera's frustum into bands (LOD0
    /// within <see cref="DetailDistance"/>, the middle LOD within <see cref="MidDistance"/>, the lowest LOD beyond, shadows only
    /// inside the URP shadow distance); the bands are then submitted nearest first, gathered per prototype in chunks of up to
    /// 1023 instances, within hard caps on instances and draw calls. No per-frame allocations. Also keeps a small pool of
    /// capsule colliders on the trunks near the traveller.
    /// </summary>
    internal sealed class EndlessTreeRenderer
    {
        /// <summary>Cells nearer than this (camera to cell bounds) use LOD0 (the Synty Meadow trees are ~25k triangles at LOD0).</summary>
        public float DetailDistance = 40f;

        /// <summary>Cells nearer than this use the LODGroup's middle LOD; farther ones the lowest (a card for the Meadow trees).</summary>
        public float MidDistance = 140f;

        /// <summary>Most instances submitted per frame.</summary>
        public int MaxInstancesPerFrame = 40000;

        /// <summary>Most draw calls submitted per frame.</summary>
        public int MaxDrawCallsPerFrame = 600;

        /// <summary>Trunk colliders are kept on trees within this distance of the traveller.</summary>
        public float ColliderRadius = 25f;

        /// <summary>Most trunk colliders.</summary>
        public int MaxColliders = 192;

        /// <summary>Seconds between collider refreshes.</summary>
        public float ColliderInterval = 0.25f;

        private sealed class Part
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;
            public Matrix4x4 Local;
            public bool Identity;
            public bool CastsShadows;
            public bool Broken;
        }

        private sealed class Prototype
        {
            public string Name;
            public Part[] Detail;
            public Part[] Mid;
            public Part[] Low;
            public bool HasCollider;
            public Vector3 ColliderCenter;
            public float ColliderRadius;
            public float ColliderHeight;

            public Part[] For(TreeDrawBand band) => band == TreeDrawBand.Detail ? Detail : band == TreeDrawBand.Mid ? Mid : Low;
        }

        private readonly List<Prototype> prototypes = new List<Prototype>();
        private readonly Dictionary<Material, Material> instancedMaterials = new Dictionary<Material, Material>();
        private readonly List<TreeCellSet> sets = new List<TreeCellSet>();
        private readonly Plane[] planes = new Plane[6];
        private readonly Matrix4x4[] partBuffer = new Matrix4x4[TreeDrawRules.MaxInstancesPerCall];
        private readonly List<TreeCell>[] bandCells = { new List<TreeCell>(256), new List<TreeCell>(512), new List<TreeCell>(256), new List<TreeCell>(1024) };
        private Matrix4x4[][] gather = Array.Empty<Matrix4x4[]>();
        private int[] gatherCount = Array.Empty<int>();
        private Vector3[] gatherMin = Array.Empty<Vector3>();
        private Vector3[] gatherMax = Array.Empty<Vector3>();
        private readonly List<CapsuleCollider> colliders = new List<CapsuleCollider>();
        private readonly Transform colliderRoot;
        private readonly int layer;
        private float nextColliderRefresh;
        private bool warnedCap;
        private bool warnedPart;
        private int activeColliders;

        /// <summary>Which prototype each tree family + variant uses (shared with the worker threads, read-only).</summary>
        public TreePrototypeIndex Index { get; } = new TreePrototypeIndex();

        /// <summary>Instances submitted in the last frame.</summary>
        public int InstancesThisFrame { get; private set; }

        /// <summary>Draw calls submitted in the last frame.</summary>
        public int DrawCallsThisFrame { get; private set; }

        /// <summary>Trees registered (on shown near tiles).</summary>
        public int RegisteredTrees { get; private set; }

        /// <summary>Trunk colliders in use.</summary>
        public int ActiveColliders => activeColliders;

        /// <summary>Number of prototypes.</summary>
        public int PrototypeCount => prototypes.Count;

        /// <summary>Draw shadows from instanced trees (inside the URP shadow distance only).</summary>
        public bool Shadows { get; set; } = true;

        /// <summary>Draw at all (off: nothing drawn, colliders cleared).</summary>
        public bool Enabled { get; set; } = true;

        public EndlessTreeRenderer(WorldPalette palette, Transform colliderParent, int layer)
        {
            this.layer = layer;
            var root = new GameObject("Tree trunks");
            root.transform.SetParent(colliderParent, false);
            colliderRoot = root.transform;
            BuildPrototypes(palette);
        }

        // ------------------------------------------------------------------ prototypes

        private void BuildPrototypes(WorldPalette palette)
        {
            var byPrefab = new Dictionary<GameObject, int>();
            if (palette == null) return;
            foreach (PrefabFamily family in palette.families)
            {
                if (family == null || !PropRole.IsTree(family.id)) continue;
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(family);
                if (prefabs.Count == 0) continue;
                var map = new int[prefabs.Count];
                for (int v = 0; v < prefabs.Count; v++)
                {
                    GameObject prefab = prefabs[v];
                    if (!byPrefab.TryGetValue(prefab, out int index))
                    {
                        index = AddPrototype(prefab);
                        byPrefab[prefab] = index;
                    }

                    map[v] = index;
                }

                Index.SetFamily(family.id, map);
            }
        }

        private int AddPrototype(GameObject prefab)
        {
            Bounds b = WorldBuilder.LocalBounds(prefab);
            var proto = new Prototype { Name = prefab.name };
            LODGroup group = prefab.GetComponentInChildren<LODGroup>(true);
            LOD[] lods = group != null ? group.GetLODs() : null;
            if (lods != null && lods.Length > 0)
            {
                proto.Detail = Parts(prefab.transform, lods[0].renderers);
                proto.Mid = Parts(prefab.transform, lods[lods.Length / 2].renderers);
                proto.Low = Parts(prefab.transform, lods[lods.Length - 1].renderers);
            }

            if (proto.Detail == null || proto.Detail.Length == 0) proto.Detail = Parts(prefab.transform, prefab.GetComponentsInChildren<MeshRenderer>(true));
            if (proto.Mid == null || proto.Mid.Length == 0) proto.Mid = proto.Detail;
            if (proto.Low == null || proto.Low.Length == 0) proto.Low = proto.Mid;

            // Trunk collider: the prefab's own capsule if it has one, else one sized from its collider mesh (trunk) or bounds.
            var capsule = prefab.GetComponentInChildren<CapsuleCollider>(true);
            if (capsule != null)
            {
                proto.HasCollider = true;
                proto.ColliderRadius = Mathf.Clamp(capsule.radius, 0.1f, 1f);
                proto.ColliderHeight = Mathf.Max(capsule.height, proto.ColliderRadius * 2f + 0.1f);
                proto.ColliderCenter = capsule.center;
            }
            else
            {
                var meshCollider = prefab.GetComponentInChildren<MeshCollider>(true);
                Bounds cb = meshCollider != null && meshCollider.sharedMesh != null ? meshCollider.sharedMesh.bounds : b;
                float radius = Mathf.Clamp(Mathf.Min(cb.extents.x, cb.extents.z), 0.15f, 0.6f);
                if (meshCollider == null) radius = Mathf.Clamp(b.size.y * 0.025f, 0.15f, 0.45f);
                float height = Mathf.Clamp(cb.max.y, 1.5f, 6f);
                proto.HasCollider = true;
                proto.ColliderRadius = radius;
                proto.ColliderHeight = Mathf.Max(height, radius * 2f + 0.1f);
                proto.ColliderCenter = new Vector3(0f, proto.ColliderHeight * 0.5f, 0f);
            }

            prototypes.Add(proto);
            Index.AddPrototype(Mathf.Max(0.5f, b.max.y), Mathf.Max(b.extents.x, b.extents.z));
            return prototypes.Count - 1;
        }

        private Part[] Parts(Transform root, Renderer[] renderers)
        {
            var parts = new List<Part>();
            if (renderers == null) return parts.ToArray();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is MeshRenderer)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Material[] materials = renderer.sharedMaterials;
                Matrix4x4 local = toRoot * renderer.transform.localToWorldMatrix;
                bool identity = local == Matrix4x4.identity;
                for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
                {
                    if (materials[s] == null) continue;
                    parts.Add(new Part
                    {
                        Mesh = mesh, Submesh = s, Material = Instanced(materials[s]), Local = local, Identity = identity,
                        CastsShadows = renderer.shadowCastingMode != ShadowCastingMode.Off,
                    });
                }
            }

            return parts.ToArray();
        }

        private Material Instanced(Material source)
        {
            if (instancedMaterials.TryGetValue(source, out Material copy)) return copy;
            copy = new Material(source) { name = source.name + " (endless instanced)", enableInstancing = true };
            instancedMaterials[source] = copy;
            return copy;
        }

        // ------------------------------------------------------------------ registration

        /// <summary>Starts drawing a tile's trees (its matrices are moved from tile-local to scene space by <paramref name="tileScene"/>).</summary>
        public void Add(TreeCellSet set, Vector3 tileScene)
        {
            if (set == null || sets.Contains(set)) return;
            set.Offset(tileScene - set.Applied);
            sets.Add(set);
            RegisteredTrees += set.Total;
            nextColliderRefresh = 0f;
        }

        /// <summary>Stops drawing a tile's trees.</summary>
        public void Remove(TreeCellSet set)
        {
            if (set == null || !sets.Remove(set)) return;
            RegisteredTrees -= set.Total;
            nextColliderRefresh = 0f;
        }

        /// <summary>Forgets every tile (new journey).</summary>
        public void Clear()
        {
            sets.Clear();
            RegisteredTrees = 0;
            nextColliderRefresh = 0f;
        }

        /// <summary>Floating-origin shift: every registered matrix moves by <paramref name="delta"/>.</summary>
        public void ShiftOrigin(Vector3 delta)
        {
            foreach (TreeCellSet set in sets) set.Offset(delta);
            nextColliderRefresh = 0f;
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Submits this frame's trees for <paramref name="camera"/> (call once per frame, after the camera moved).</summary>
        public void Draw(Camera camera)
        {
            InstancesThisFrame = 0;
            DrawCallsThisFrame = 0;
            if (!Enabled || camera == null || sets.Count == 0 || prototypes.Count == 0) return;
            EnsureGatherBuffers();
            ClearGather();
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            Vector3 eye = camera.transform.position;
            float shadowDistance = ShadowDistance();

            // One pass over the cells: frustum test and band per cell.
            foreach (List<TreeCell> list in bandCells) list.Clear();
            foreach (TreeCellSet set in sets)
            {
                foreach (TreeCell cell in set.Cells)
                {
                    var bounds = new Bounds();
                    bounds.SetMinMax(cell.Min - Vector3.one * 2f, cell.Max + Vector3.one * 2f);
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                    float distance = Mathf.Sqrt(DistanceSq(eye, cell));
                    bandCells[(int)TreeDrawRules.BandFor(distance, DetailDistance, MidDistance, shadowDistance)].Add(cell);
                }
            }

            // Nearest band first (a cap cuts the farthest trees), gathered per prototype.
            for (int band = 0; band < TreeDrawRules.BandCount; band++)
            {
                var drawBand = (TreeDrawBand)band;
                bool shadows = TreeDrawRules.CastsShadows(drawBand, Shadows);
                foreach (TreeCell cell in bandCells[band])
                {
                    for (int p = 0; p < prototypes.Count; p++)
                    {
                        Matrix4x4[] list = cell.ByPrototype[p];
                        if (list == null) continue;
                        for (int i = 0; i < list.Length; i++)
                        {
                            int n = gatherCount[p];
                            if (n == 0)
                            {
                                gatherMin[p] = cell.Min;
                                gatherMax[p] = cell.Max;
                            }

                            gather[p][n] = list[i];
                            gatherCount[p] = ++n;
                            if (n == TreeDrawRules.MaxInstancesPerCall && !Flush(p, drawBand, shadows)) return;
                        }

                        if (gatherCount[p] > 0)
                        {
                            gatherMin[p] = Vector3.Min(gatherMin[p], cell.Min);
                            gatherMax[p] = Vector3.Max(gatherMax[p], cell.Max);
                        }
                    }
                }

                for (int p = 0; p < prototypes.Count; p++)
                {
                    if (gatherCount[p] > 0 && !Flush(p, drawBand, shadows)) return;
                }
            }
        }

        private void EnsureGatherBuffers()
        {
            if (gather.Length == prototypes.Count) return;
            gather = new Matrix4x4[prototypes.Count][];
            for (int p = 0; p < gather.Length; p++) gather[p] = new Matrix4x4[TreeDrawRules.MaxInstancesPerCall];
            gatherCount = new int[prototypes.Count];
            gatherMin = new Vector3[prototypes.Count];
            gatherMax = new Vector3[prototypes.Count];
        }

        /// <summary>Submits prototype <paramref name="p"/>'s gathered instances for every part of its LOD; false when a cap stops the frame.</summary>
        private bool Flush(int p, TreeDrawBand band, bool shadows)
        {
            int count = gatherCount[p];
            gatherCount[p] = 0;
            Part[] parts = prototypes[p].For(band);
            if (count <= 0 || parts.Length == 0) return true;
            int allowed = TreeDrawRules.Allowed(count, parts.Length, InstancesThisFrame, DrawCallsThisFrame, MaxInstancesPerFrame, MaxDrawCallsPerFrame);
            bool capped = allowed < count;
            if (capped)
            {
                if (!warnedCap)
                {
                    warnedCap = true;
                    Debug.LogWarning($"Endless road: the instanced tree renderer hit its cap ({MaxInstancesPerFrame} instances / {MaxDrawCallsPerFrame} draws per frame); farther trees are skipped while it lasts. Load: {RegisteredTrees} trees registered.");
                }

                if (allowed <= 0)
                {
                    ClearGather();
                    return false;
                }

                count = allowed;
            }

            var bounds = new Bounds();
            bounds.SetMinMax(gatherMin[p] - Vector3.one * 2f, gatherMax[p] + Vector3.one * 2f);
            Matrix4x4[] instances = gather[p];
            foreach (Part part in parts)
            {
                if (part.Broken) continue;
                var rp = new RenderParams(part.Material)
                {
                    layer = layer,
                    worldBounds = bounds,
                    shadowCastingMode = shadows && part.CastsShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.BlendProbes,
                    reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
                    motionVectorMode = MotionVectorGenerationMode.Camera,
                };

                Matrix4x4[] data = instances;
                if (!part.Identity)
                {
                    for (int i = 0; i < count; i++) partBuffer[i] = instances[i] * part.Local;
                    data = partBuffer;
                }

                try
                {
                    Graphics.RenderMeshInstanced(rp, part.Mesh, part.Submesh, data, count);
                    DrawCallsThisFrame++;
                }
                catch (Exception e)
                {
                    part.Broken = true;
                    if (!warnedPart)
                    {
                        warnedPart = true;
                        Debug.LogError($"Endless road: instanced drawing of {part.Mesh.name} ({part.Material.name}) failed and that part is switched off: {e.Message}");
                    }
                }
            }

            InstancesThisFrame += count;
            if (capped) ClearGather();
            return !capped;
        }

        private void ClearGather()
        {
            for (int i = 0; i < gatherCount.Length; i++) gatherCount[i] = 0;
        }

        private static float DistanceSq(Vector3 eye, TreeCell cell)
        {
            float dx = eye.x < cell.Min.x ? cell.Min.x - eye.x : eye.x > cell.Max.x ? eye.x - cell.Max.x : 0f;
            float dy = eye.y < cell.Min.y ? cell.Min.y - eye.y : eye.y > cell.Max.y ? eye.y - cell.Max.y : 0f;
            float dz = eye.z < cell.Min.z ? cell.Min.z - eye.z : eye.z > cell.Max.z ? eye.z - cell.Max.z : 0f;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>The URP shadow distance (0 when shadows are off or the pipeline is not URP).</summary>
        private static float ShadowDistance()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return 0f;
            return asset.supportsMainLightShadows ? asset.shadowDistance : 0f;
        }

        // ------------------------------------------------------------------ trunk colliders

        /// <summary>Keeps capsule colliders on the trunks within <see cref="ColliderRadius"/> of <paramref name="traveller"/> (a few times a second).</summary>
        public void UpdateColliders(Vector3 traveller, bool force = false)
        {
            if (!force && Time.unscaledTime < nextColliderRefresh) return;
            nextColliderRefresh = Time.unscaledTime + ColliderInterval;
            int used = 0;
            if (Enabled)
            {
                float r2 = ColliderRadius * ColliderRadius;
                foreach (TreeCellSet set in sets)
                {
                    foreach (TreeCell cell in set.Cells)
                    {
                        if (cell.SqrDistanceXZ(traveller.x, traveller.z) > r2) continue;
                        for (int p = 0; p < prototypes.Count && used < MaxColliders; p++)
                        {
                            Matrix4x4[] list = cell.ByPrototype[p];
                            Prototype proto = prototypes[p];
                            if (list == null || !proto.HasCollider) continue;
                            for (int i = 0; i < list.Length && used < MaxColliders; i++)
                            {
                                var position = new Vector3(list[i].m03, list[i].m13, list[i].m23);
                                float dx = position.x - traveller.x, dz = position.z - traveller.z;
                                if (dx * dx + dz * dz > r2) continue;
                                float scale = new Vector3(list[i].m00, list[i].m10, list[i].m20).magnitude;
                                CapsuleCollider c = TakeCollider(used++);
                                Transform t = c.transform;
                                t.SetPositionAndRotation(position, Quaternion.identity);
                                t.localScale = Vector3.one;
                                c.radius = proto.ColliderRadius * scale;
                                c.height = proto.ColliderHeight * scale;
                                c.center = new Vector3(0f, proto.ColliderCenter.y * scale, 0f);
                                if (!c.enabled) c.enabled = true;
                            }
                        }
                    }
                }
            }

            for (int i = used; i < colliders.Count; i++)
            {
                if (colliders[i].enabled) colliders[i].enabled = false;
            }

            activeColliders = used;
        }

        private CapsuleCollider TakeCollider(int i)
        {
            if (i < colliders.Count) return colliders[i];
            var go = new GameObject("Trunk");
            go.layer = layer;
            go.transform.SetParent(colliderRoot, false);
            var c = go.AddComponent<CapsuleCollider>();
            c.direction = 1;
            colliders.Add(c);
            return c;
        }

        // ------------------------------------------------------------------ teardown

        /// <summary>Destroys the runtime material copies (scene end).</summary>
        public void Dispose()
        {
            foreach (Material m in instancedMaterials.Values)
            {
                if (m != null) Object.Destroy(m);
            }

            instancedMaterials.Clear();
            sets.Clear();
        }
    }
}
