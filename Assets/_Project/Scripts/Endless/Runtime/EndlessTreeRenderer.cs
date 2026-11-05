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
    /// palette is taken apart into mesh parts per LOD with instancing-enabled runtime copies of its materials (the Synty assets
    /// are untouched): LOD0, an intermediate mesh LOD and the simplest LOD that is still a 3D mesh (<see cref="TreeLodPlan"/>:
    /// cards are never drawn); a prefab without a LODGroup uses its meshes for all three. Each frame one pass over the
    /// registered 32 m cells frustum-culls them; cells beyond the intermediate distance draw whole in the lowest band, nearer
    /// ones choose a band per tree with hysteresis (<see cref="TreeDrawRules.BandFor(float, byte, TreeLodDistances)"/>).
    /// Visible draws cast no shadow; shadows come from separate shadow-only draws of the simplest mesh for the trees within
    /// <see cref="ShadowDistance"/> (in view or not), gathered in two distance rings. Instances are gathered per band and
    /// prototype in chunks of up to 1023, within hard caps on instances and draw calls. No per-frame allocations. Also keeps a
    /// small pool of capsule colliders on the trunks near the traveller.
    /// <para>
    /// Forest pass (2026-10-05): shadows are split at <see cref="ShadowProxyDistance"/>: nearer, the simplest mesh LOD casts as
    /// before; beyond it a tree casts from a 26-triangle solid of its measured crown and a wall or hedge from its box
    /// (<see cref="ShadowProxy"/>), drawn with <see cref="WorldPalette.shadowProxyMaterial"/>. Bushes, ferns and ivy cast only within
    /// <see cref="PlantShadowDistance"/>; ferns are drawn only to <see cref="FernDrawDistance"/>, thinning out over its last fifth.
    /// </para>
    /// </summary>
    internal sealed class EndlessTreeRenderer
    {
        /// <summary>LOD0 out to this distance (times <see cref="DetailScale"/>), metres. Set every frame from <see cref="EndlessSettings"/>.</summary>
        public float FullDetailDistance = 110f;

        /// <summary>The intermediate mesh LOD out to this distance (times <see cref="DetailScale"/>); the simplest mesh LOD beyond.</summary>
        public float MidDetailDistance = 300f;

        /// <summary>Multiplier on both detail distances.</summary>
        public float DetailScale = 1f;

        /// <summary>Trees cast shadows out to this distance (never beyond the pipeline's shadow distance).</summary>
        public float ShadowDistance = 300f;

        /// <summary>Beyond this distance trees and boundary pieces cast their shadow from a low-poly proxy, metres (forest pass).</summary>
        public float ShadowProxyDistance = 100f;

        /// <summary>
        /// Second performance pass (2026-10-06): beyond this distance (times <see cref="DetailScale"/>) a tree is drawn as its solid
        /// impostor (<see cref="TreeImpostor"/>) instead of its lowest mesh LOD; 0: never. Set every frame from <see cref="EndlessSettings"/>.
        /// </summary>
        public float ImpostorDistance = 0f;

        /// <summary>
        /// A tree whose impostor silhouette (<see cref="TreeImpostor.SilhouetteError"/>) would differ from its mesh by more than this many
        /// 1080p pixels at <see cref="ImpostorDistance"/> swaps farther out, where it differs by this many (0: every tree at the distance).
        /// </summary>
        public float ImpostorMaxPixels = 2f;

        /// <summary>Bushes, ferns, undergrowth and ivy cast shadows only within this distance, metres.</summary>
        public float PlantShadowDistance = 40f;

        /// <summary>Ferns are drawn out to this distance (each fades out between 80 and 100 % of it), metres.</summary>
        public float FernDrawDistance = 160f;

        /// <summary>Woodland undergrowth is drawn out to this distance (each fades out between 80 and 100 % of it), metres; 0: no limit.</summary>
        public float UndergrowthDrawDistance = 250f;

        /// <summary>Most instances submitted per frame (visible and shadow-only draws together).</summary>
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
            public int Triangles;
        }

        /// <summary>One of a prefab's own colliders: a mesh (convex or not) or a box, posed in the piece's frame.</summary>
        private struct OwnShape
        {
            public Mesh Mesh;
            public bool Convex;
            public Vector3 BoxCenter, BoxSize;
            public Matrix4x4 Local;
        }

        private sealed class Prototype
        {
            public string Name;
            public Part[] Detail;
            public Part[] Mid;
            public Part[] Low;
            public int[] Triangles = new int[TreeDrawRules.BandCount];
            public int ShadowTriangles;
            public bool HasCollider;
            public Vector3 ColliderCenter;
            public float ColliderRadius;
            public float ColliderHeight;

            /// <summary>A field-boundary piece (laid along +Z, centred): no trunk, not a sight blocker, maybe a box collider.</summary>
            public bool IsPiece;
            public bool HasBox;
            /// <summary>A walled city's or castle's piece (<see cref="PropRole.IsCityPiece"/>): near one, the box budget is <see cref="CityMaxBoxes"/>.</summary>
            public bool IsCityPiece;
            /// <summary>The prefab's own colliders in the piece's frame (<see cref="MedievalZen.Endless.Generation.PieceCollider.Own"/>: archways, gates, doors, stairs).</summary>
            public OwnShape[] OwnShapes = Array.Empty<OwnShape>();
            public Vector3 BoxCenter;
            public Vector3 BoxSize;

            /// <summary>Shadow-only parts beyond the proxy distance (a crown solid or a box); empty: no shadow there.</summary>
            public Part[] Proxy = Array.Empty<Part>();

            /// <summary>A bush, shrub, fern or climber: shadows only near the traveller.</summary>
            public bool IsPlant;

            /// <summary>A fern: drawn only to <see cref="FernDrawDistance"/>.</summary>
            public bool IsFern;

            /// <summary>Woodland undergrowth: drawn only to <see cref="UndergrowthDrawDistance"/>.</summary>
            public bool IsUndergrowth;

            /// <summary>Second performance pass: the solid impostor drawn beyond <see cref="ImpostorDistance"/> (empty: the lowest mesh LOD there).</summary>
            public Part[] Impostor = Array.Empty<Part>();

            /// <summary>The measured style the shadow proxy and the impostor are built from (trees only).</summary>
            public FarTreeStyle Style;
            public bool HasStyle;

            /// <summary>Its impostor's silhouette difference against its lowest mesh LOD, metres (<see cref="TreeImpostor.SilhouetteError"/>).</summary>
            public float SilhouetteMetres;

            public Part[] For(TreeDrawBand band) => band == TreeDrawBand.Detail ? Detail : band == TreeDrawBand.Mid ? Mid : band == TreeDrawBand.Low || Impostor.Length == 0 ? Low : Impostor;
        }

        private readonly List<Prototype> prototypes = new List<Prototype>();
        private readonly Dictionary<Material, Material> instancedMaterials = new Dictionary<Material, Material>();
        private readonly List<TreeCellSet> sets = new List<TreeCellSet>();
        private readonly Matrix4x4[] partBuffer = new Matrix4x4[TreeDrawRules.MaxInstancesPerCall];
        private readonly int[] bandInstances = new int[TreeDrawRules.BandCount];
        private bool stopped;
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

        /// <summary>Visible instances drawn last frame in a band.</summary>
        public int InstancesIn(TreeDrawBand band) => bandInstances[(int)band];

        /// <summary>Shadow-only instances submitted last frame.</summary>
        public int ShadowInstancesThisFrame { get; private set; }

        /// <summary>Triangles submitted for the visible draws last frame.</summary>
        public long TrianglesThisFrame { get; private set; }

        /// <summary>Triangles submitted for the shadow-only draws last frame (per cascade they fall in).</summary>
        public long ShadowTrianglesThisFrame { get; private set; }

        /// <summary>The LOD distances used last frame.</summary>
        public TreeLodDistances Distances { get; private set; } = new TreeLodDistances(110f, 300f);

        /// <summary>The tree shadow distance used last frame.</summary>
        public float ShadowDistanceInEffect { get; private set; }

        /// <summary>One line per prototype: the LODs it draws and their triangles (logged at start).</summary>
        public string LodSummary { get; private set; } = string.Empty;

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
            proxyMaterial = ProxyMaterial(palette);
            BuildPrototypes(palette);
        }

        // ------------------------------------------------------------------ prototypes

        private void BuildPrototypes(WorldPalette palette)
        {
            // Performance pass: the ferns and the woodland undergrowth get prototypes of their own even where their prefabs are also the
            // countryside's shrubs and bushes (the Generic ferns are Shrub variants too, the Generic bushes Bush variants), so their draw
            // distance applies to them alone. Before, the prototype a Shrub variant had made first was reused without the fern flag and
            // three of the five fern variants were drawn to the edge of the near tiles.
            var byPrefab = new Dictionary<(GameObject, int), int>();
            if (palette == null) return;
            var summary = new System.Text.StringBuilder("Endless road trees (LOD used per band, triangles; cards are never drawn):");
            foreach (PrefabFamily family in palette.families)
            {
                // Trees, and the bushes, shrubs and ferns of the endless road (drawn here instead of as GameObjects), and the pieces of
                // its field boundaries (walls, hedges, fences: laid along +Z and centred, with box colliders near the traveller).
                bool plant = family != null && PropRole.IsInstancedPlant(family.id);
                bool piece = family != null && (PropRole.IsBoundaryPiece(family.id) || PropRole.IsOvergrowth(family.id));
                if (family == null || !(PropRole.IsTree(family.id) || plant || piece)) continue;
                List<GameObject> prefabs = WorldPalette.ValidPrefabs(family);
                if (prefabs.Count == 0) continue;
                var map = new int[prefabs.Count];
                for (int v = 0; v < prefabs.Count; v++)
                {
                    GameObject prefab = prefabs[v];
                    // A piece family shares prefabs with nothing else, but keep its prototypes apart from any tree use of the prefab.
                    var cacheKey = (prefab, family.id == PropRole.Fern ? 1 : family.id == PropRole.Undergrowth ? 2 : 0);
                    if (!piece && byPrefab.TryGetValue(cacheKey, out int index))
                    {
                        map[v] = index;
                        continue;
                    }

                    index = piece ? AddPiecePrototype(prefab, family, summary) : AddPrototype(prefab, summary, !plant);
                    if (!piece) byPrefab[cacheKey] = index;
                    Prototype added = prototypes[index];
                    added.IsPlant |= plant || PropRole.IsOvergrowth(family.id);
                    added.IsFern |= family.id == PropRole.Fern;
                    added.IsUndergrowth |= family.id == PropRole.Undergrowth;
                    if (!added.IsPlant && added.Proxy.Length == 0) added.Proxy = BuildProxy(added, prefab);

                    map[v] = index;
                }

                Index.SetFamily(family.id, map);
            }

            LodSummary = summary.ToString();
            Debug.Log(LodSummary);
        }

        private int AddPrototype(GameObject prefab, System.Text.StringBuilder summary, bool trunk = true)
        {
            Bounds b = WorldBuilder.LocalBounds(prefab);
            var proto = new Prototype { Name = prefab.name };
            LODGroup group = prefab.GetComponentInChildren<LODGroup>(true);
            LOD[] lods = group != null ? group.GetLODs() : null;
            string plan = "no LODGroup";
            if (lods != null && lods.Length > 0)
            {
                // Measure every LOD (triangles, thinness) and never draw the cards.
                var lodParts = new Part[lods.Length][];
                var tris = new int[lods.Length];
                var thin = new float[lods.Length];
                for (int i = 0; i < lods.Length; i++)
                {
                    lodParts[i] = Parts(prefab.transform, lods[i].renderers);
                    tris[i] = TrianglesOf(lodParts[i]);
                    thin[i] = Thinness(lodParts[i]);
                }

                (int detail, int mid, int low) = TreeLodPlan.Choose(tris, thin);
                proto.Detail = lodParts[detail];
                proto.Mid = lodParts[mid];
                proto.Low = lodParts[low];
                var all = new System.Text.StringBuilder();
                for (int i = 0; i < lods.Length; i++)
                {
                    bool card = i > 0 && TreeLodPlan.IsCard(tris[i], tris[0], thin[i]);
                    all.Append(i == 0 ? string.Empty : ", ").Append($"LOD{i} {tris[i]}{(card ? " card" : string.Empty)}");
                }

                plan = $"detail LOD{detail}, mid LOD{mid}, low LOD{low} (of {all})";
            }

            if (proto.Detail == null || proto.Detail.Length == 0) proto.Detail = Parts(prefab.transform, prefab.GetComponentsInChildren<MeshRenderer>(true));
            if (proto.Mid == null || proto.Mid.Length == 0) proto.Mid = proto.Detail;
            if (proto.Low == null || proto.Low.Length == 0) proto.Low = proto.Mid;
            proto.Triangles[(int)TreeDrawBand.Detail] = TrianglesOf(proto.Detail);
            proto.Triangles[(int)TreeDrawBand.Mid] = TrianglesOf(proto.Mid);
            proto.Triangles[(int)TreeDrawBand.Low] = TrianglesOf(proto.Low);
            foreach (Part part in proto.Low)
            {
                if (part.CastsShadows) proto.ShadowTriangles += part.Triangles;
            }

            summary.Append($"\n  {prefab.name}: {proto.Triangles[0]} / {proto.Triangles[1]} / {proto.Triangles[2]} triangles; {plan}");

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

            proto.HasCollider &= trunk;   // bushes and ferns are walked through, as their prefabs have no collider
            prototypes.Add(proto);
            Index.AddPrototype(Mathf.Max(0.5f, b.max.y), Mathf.Max(b.extents.x, b.extents.z));
            return prototypes.Count - 1;
        }

        /// <summary>
        /// A field-boundary piece: its LODs as for a tree, every part turned by the family's yaw offset and shifted so the mesh's
        /// bounds centre sits on the pivot (as <see cref="WorldBuilder.PropPose"/> lays line pieces), so the piece runs along local +Z
        /// of its pose (<see cref="TreeCellSet.PiecePose"/>). Walls, pillars, fences, posts and hedges get a box collider (from those
        /// bounds) while the traveller is near; gate leaves stand open and have none.
        /// </summary>
        private int AddPiecePrototype(GameObject prefab, PrefabFamily family, System.Text.StringBuilder summary)
        {
            int index = AddPrototype(prefab, summary, false);
            Prototype proto = prototypes[index];
            Bounds b = WorldBuilder.LocalBounds(prefab);
            Vector3 centre = family.centerPivot ? new Vector3(b.center.x, 0f, b.center.z) : Vector3.zero;
            Matrix4x4 offset = Matrix4x4.Rotate(Quaternion.Euler(0f, family.yawOffset, 0f)) * Matrix4x4.Translate(-centre);
            var done = new HashSet<Part>();
            foreach (Part[] parts in new[] { proto.Detail, proto.Mid, proto.Low })
            {
                foreach (Part part in parts)
                {
                    if (!done.Add(part)) continue;
                    part.Local = offset * part.Local;
                    part.Identity = false;
                }
            }

            // Bounds in the piece's own frame.
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = -min;
            for (int c = 0; c < 8; c++)
            {
                Vector3 p = offset.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            proto.IsPiece = true;
            // Ivy and gate leaves have none; a city's archways, gates, doors, stairs, bridges and overhead pieces have none either (PieceColliders).
            var kind = MedievalZen.Endless.Generation.PieceColliders.Kind(family.id, prefab.name);
            proto.HasBox = kind == MedievalZen.Endless.Generation.PieceCollider.Box;
            proto.IsCityPiece = PropRole.IsCityPiece(family.id);
            if (kind == MedievalZen.Endless.Generation.PieceCollider.Own) proto.OwnShapes = OwnShapesOf(prefab, offset);
            proto.BoxCenter = (min + max) * 0.5f;
            proto.BoxSize = Vector3.Max(max - min, new Vector3(0.2f, 0.2f, 0.2f));
            Index.SetShape(index, Mathf.Max(0.5f, max.y), Mathf.Max(Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)), Mathf.Max(Mathf.Abs(min.z), Mathf.Abs(max.z))));
            return index;
        }

        /// <summary>The prefab's own mesh and box colliders, posed in the piece's frame (<paramref name="offset"/> x the child's pose in the prefab).</summary>
        private static OwnShape[] OwnShapesOf(GameObject prefab, Matrix4x4 offset)
        {
            var shapes = new List<OwnShape>();
            Matrix4x4 root = prefab.transform.worldToLocalMatrix;
            foreach (Collider c in prefab.GetComponentsInChildren<Collider>(true))
            {
                Matrix4x4 local = offset * root * c.transform.localToWorldMatrix;
                if (c is MeshCollider mc && mc.sharedMesh != null) shapes.Add(new OwnShape { Mesh = mc.sharedMesh, Convex = mc.convex, Local = local });
                else if (c is BoxCollider bc) shapes.Add(new OwnShape { BoxCenter = bc.center, BoxSize = bc.size, Local = local });
            }

            return shapes.ToArray();
        }

        private readonly Material proxyMaterial;
        private readonly List<Mesh> proxyMeshes = new List<Mesh>();
        private Material impostorMaterial;

        /// <summary>Triangles of each prototype's impostor (0: none), for the overlay and the log.</summary>
        public string ImpostorSummary { get; private set; } = string.Empty;

        /// <summary>
        /// Second performance pass (2026-10-06): gives every tree prototype its solid impostor (<see cref="TreeImpostor"/>), drawn with an
        /// instancing-enabled runtime copy of <paramref name="farMaterial"/> (the far tiles' flat-shaded vertex-colour material, so a
        /// near tile's distant tree and a far tile's tree look alike). Without a material no impostor band is drawn.
        /// </summary>
        public void SetImpostorMaterial(Material farMaterial)
        {
            if (farMaterial == null || impostorMaterial != null) return;
            impostorMaterial = new Material(farMaterial) { name = "Tree impostor (endless instanced)", enableInstancing = true };
            var summary = new System.Text.StringBuilder("Endless road tree impostors (triangles):");
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();
            foreach (Prototype proto in prototypes)
            {
                if (!proto.HasStyle || proto.IsPlant || proto.IsPiece) continue;
                vertices.Clear();
                triangles.Clear();
                colors.Clear();
                TreeImpostor.Build(proto.Style, vertices, triangles, colors);
                var mesh = new Mesh { name = proto.Name + " (impostor)" };
                mesh.SetVertices(vertices);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                proxyMeshes.Add(mesh);
                int tris = triangles.Count / 3;
                proto.Impostor = new[]
                {
                    new Part { Mesh = mesh, Submesh = 0, Material = impostorMaterial, Local = Matrix4x4.identity, Identity = true, CastsShadows = false, Triangles = tris },
                };
                proto.Triangles[(int)TreeDrawBand.Impostor] = tris;
                proto.SilhouetteMetres = TreeImpostor.SilhouetteError(proto.Name, proto.Style.Height);
                summary.Append(' ').Append(proto.Name).Append(' ').Append(tris).Append($" ({proto.SilhouetteMetres:0.00} m);");
            }

            ImpostorSummary = summary.ToString();
            walkPrototypes = Array.Empty<TreeWalkPrototype>();
            hasWalk = false;
        }

        /// <summary>
        /// The shadow proxy of a prototype: for a boundary piece with a box, its box; for a tree, the solid of its measured crown
        /// (<see cref="FarTreeStyles.ForPrefab"/>, the same styles the distant impostors use). Empty when there is no proxy material.
        /// </summary>
        private Part[] BuildProxy(Prototype proto, GameObject prefab)
        {
            if (proxyMaterial == null) return Array.Empty<Part>();
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            if (proto.IsPiece)
            {
                if (!proto.HasBox) return Array.Empty<Part>();
                ShadowProxy.Box(proto.BoxCenter, proto.BoxSize, vertices, triangles);
            }
            else
            {
                Bounds b = WorldBuilder.LocalBounds(prefab);
                FarTreeStyle style = FarTreeStyles.ForPrefab(prefab.name, Mathf.Max(0.5f, b.max.y), Mathf.Max(b.extents.x, b.extents.z));
                proto.Style = style;
                proto.HasStyle = true;
                ShadowProxy.Tree(style, vertices, triangles);
            }

            var mesh = new Mesh { name = proto.Name + " (shadow proxy)" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            proxyMeshes.Add(mesh);
            return new[]
            {
                new Part { Mesh = mesh, Submesh = 0, Material = proxyMaterial, Local = Matrix4x4.identity, Identity = true, CastsShadows = true, Triangles = triangles.Count / 3 },
            };
        }

        /// <summary>
        /// The shadow proxies' material: the palette's (<see cref="WorldPalette.shadowProxyMaterial"/>, URP Lit, built by the palette
        /// menu so the shader is in builds), else a runtime URP Lit copy; instanced, double-sided, opaque (no alpha clip).
        /// </summary>
        private static Material ProxyMaterial(WorldPalette palette)
        {
            Material source = palette != null ? palette.shadowProxyMaterial : null;
            Material material = source != null ? new Material(source) : null;
            if (material == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null)
                {
                    Debug.LogWarning("Endless road: no shadow proxy material (palette) and no URP Lit shader; distant trees cast no shadow.");
                    return null;
                }

                material = new Material(lit);
            }

            material.name = "Shadow proxy (endless instanced)";
            material.enableInstancing = true;
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            return material;
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
                        Triangles = (int)(mesh.GetIndexCount(s) / 3),
                    });
                }
            }

            return parts.ToArray();
        }

        private static int TrianglesOf(Part[] parts)
        {
            int sum = 0;
            if (parts == null) return 0;
            foreach (Part part in parts) sum += part.Triangles;
            return sum;
        }

        /// <summary>Smallest over largest extent of the parts' bounds in prefab space (≈ 0 for a flat card).</summary>
        private static float Thinness(Part[] parts)
        {
            if (parts == null || parts.Length == 0) return 0f;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = -min;
            foreach (Part part in parts)
            {
                Bounds mb = part.Mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? mb.min.x : mb.max.x, (c & 2) == 0 ? mb.min.y : mb.max.y, (c & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 p = part.Local.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }

            Vector3 size = max - min;
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            return largest > 1e-4f ? Mathf.Min(size.x, Mathf.Min(size.y, size.z)) / largest : 0f;
        }

        private Material Instanced(Material source)
        {
            if (instancedMaterials.TryGetValue(source, out Material copy)) return copy;
            copy = new Material(source) { name = source.name + " (endless instanced)", enableInstancing = true };
            // Light and motion pass (2026-10-02): the copy sways with the weather's wind (the Synty foliage shader reads the
            // _WindIntensity/_GaleStrength/_WindDirection globals pushed by WeatherSystem instead of its fixed 0.2), and its
            // double-sided leaf cards can light their back faces (see ApplyLightLook).
            if (copy.HasProperty(GlobalWindId)) copy.SetFloat(GlobalWindId, 1f);
            if (copy.HasProperty(BackFaceLightingId)) backFaceMaterials.Add((copy, copy.GetFloat(BackFaceLightingId)));
            instancedMaterials[source] = copy;
            return copy;
        }

        /// <summary>The run-time material copies by their Synty source (read-only; the season painter recolours the leaf copies).</summary>
        public IReadOnlyDictionary<Material, Material> InstancedMaterials => instancedMaterials;

        private static readonly int GlobalWindId = Shader.PropertyToID("_Use_Global_Weather_Controller");
        private static readonly int BackFaceLightingId = Shader.PropertyToID("_Enable_Back_Face_Lighting");
        private readonly List<(Material material, float original)> backFaceMaterials = new List<(Material, float)>();
        private int appliedLookVersion = -1;
        private bool richLight;

        /// <summary>
        /// Follows the Journey menu's "Rich light" (<see cref="MedievalZen.Environment.LightLook"/>). Rich: the Synty foliage
        /// shader's <c>_Enable_Back_Face_Lighting</c> is on, so a leaf card seen from behind is lit with its normal flipped
        /// toward the viewer (URP Shader Graph does not flip normals on back faces by itself, and every Synty tree ships with
        /// it off: about half of a canopy's cards were lit as if facing away from the sun, which made the dark-textured oak
        /// crowns look nearly black); and the instances take the scene's ambient probe directly (light probes off: the
        /// procedural world bakes none). Old look: the materials' own values and blended probes, as before.
        /// </summary>
        private void ApplyLightLook()
        {
            appliedLookVersion = MedievalZen.Environment.LightLook.Version;
            richLight = MedievalZen.Environment.LightLook.Rich;
            foreach ((Material material, float original) in backFaceMaterials)
            {
                if (material != null) material.SetFloat(BackFaceLightingId, richLight ? 1f : original);
            }
        }

        // ------------------------------------------------------------------ registration

        /// <summary>Starts drawing a tile's trees (its matrices are moved from tile-local to scene space by <paramref name="tileScene"/>).</summary>
        public void Add(TreeCellSet set, Vector3 tileScene)
        {
            if (set == null || sets.Contains(set)) return;
            set.Offset(tileScene - set.Applied);
            sets.Add(set);
            setsVersion++;
            RegisteredTrees += set.Total;
            nextColliderRefresh = 0f;
        }

        /// <summary>Stops drawing a tile's trees.</summary>
        public void Remove(TreeCellSet set)
        {
            if (set == null || !sets.Remove(set)) return;
            setsVersion++;
            RegisteredTrees -= set.Total;
            nextColliderRefresh = 0f;
        }

        /// <summary>Forgets every tile (new journey).</summary>
        public void Clear()
        {
            sets.Clear();
            setsVersion++;
            RegisteredTrees = 0;
            nextColliderRefresh = 0f;
        }

        /// <summary>Floating-origin shift: every registered matrix moves by <paramref name="delta"/>.</summary>
        public void ShiftOrigin(Vector3 delta)
        {
            foreach (TreeCellSet set in sets) set.Offset(delta);
            setsVersion++;
            nextColliderRefresh = 0f;
        }

        // ------------------------------------------------------------------ drawing

        private readonly TreeCellWalker walker = new TreeCellWalker();
        private TreeWalkPrototype[] walkPrototypes = Array.Empty<TreeWalkPrototype>();
        private readonly System.Diagnostics.Stopwatch walkClock = new System.Diagnostics.Stopwatch();
        private readonly int[] ringInstances = new int[TreeDrawRules.ShadowRingCount];
        private readonly long[] ringTriangles = new long[TreeDrawRules.ShadowRingCount];
        private readonly long[] bandTriangles = new long[TreeDrawRules.BandCount];
        private int setsVersion;
        private bool hasWalk;
        private int walkVersion;
        private Vector3 walkEye, walkForward, walkUp, walkLight;
        private float walkFov, walkAspect;
        private TreeWalkFrame walkFrame;

        /// <summary>Cull shadow casters whose shadow cannot land in the view (performance pass; on by default).</summary>
        public bool CullShadowCasters = true;

        /// <summary>Submit the last walk again while the camera and the light have hardly moved (performance pass; on by default).</summary>
        public bool ReuseWalk = true;

        /// <summary>Milliseconds the last frame's walk over the cells took (0 when the last walk was reused).</summary>
        public double WalkMs { get; private set; }

        /// <summary>Milliseconds the last frame's submission (the instanced draw calls) took.</summary>
        public double SubmitMs { get; private set; }

        /// <summary>True when the last frame submitted the previous walk again.</summary>
        public bool WalkReused { get; private set; }

        /// <summary>Counters of the last walk.</summary>
        public TreeWalkStats WalkStats => walker.Stats;

        /// <summary>Triangles drawn last frame in a visible band.</summary>
        public long TrianglesIn(TreeDrawBand band) => bandTriangles[(int)band];

        /// <summary>Shadow-only instances submitted last frame in a ring (0: simplest mesh LOD near, 1: proxies).</summary>
        public int ShadowInstancesIn(int ring) => ringInstances[ring];

        /// <summary>Shadow triangles submitted last frame in a ring.</summary>
        public long ShadowTrianglesIn(int ring) => ringTriangles[ring];

        /// <summary>Bushes, shrubs, ferns, undergrowth and ivy drawn last frame (visible).</summary>
        public int PlantsDrawn { get; private set; }

        /// <summary>Ferns drawn last frame (visible; included in <see cref="PlantsDrawn"/>).</summary>
        public int FernsDrawn { get; private set; }

        /// <summary>Triangles of the visible plants last frame.</summary>
        public long PlantTriangles { get; private set; }

        /// <summary>Submits this frame's trees for <paramref name="camera"/> (call once per frame, after the camera moved).</summary>
        public void Draw(Camera camera)
        {
            InstancesThisFrame = 0;
            DrawCallsThisFrame = 0;
            ShadowInstancesThisFrame = 0;
            TrianglesThisFrame = 0;
            ShadowTrianglesThisFrame = 0;
            PlantsDrawn = 0;
            FernsDrawn = 0;
            PlantTriangles = 0;
            WalkMs = 0;
            SubmitMs = 0;
            WalkReused = false;
            for (int b = 0; b < bandInstances.Length; b++)
            {
                bandInstances[b] = 0;
                bandTriangles[b] = 0;
            }

            for (int r = 0; r < ringInstances.Length; r++)
            {
                ringInstances[r] = 0;
                ringTriangles[r] = 0;
            }

            if (!Enabled || camera == null || sets.Count == 0 || prototypes.Count == 0)
            {
                hasWalk = false;
                return;
            }

            if (appliedLookVersion != MedievalZen.Environment.LightLook.Version) ApplyLightLook();
            Transform view = camera.transform;
            Vector3 eye = view.position, forward = view.forward, up = view.up;
            TreeLodDistances lod = TreeLodDistances.From(FullDetailDistance, MidDetailDistance, DetailScale, impostorMaterial != null ? ImpostorDistance : 0f);
            float shadowDistance = TreeDrawRules.ShadowDistance(ShadowDistance, PipelineShadowDistance());
            Distances = lod;
            EnsureWalkPrototypes(lod);
            ShadowDistanceInEffect = Shadows ? shadowDistance : 0f;
            Light sun = RenderSettings.sun;
            Vector3 light = sun != null ? sun.transform.forward : Vector3.down;
            bool cull = CullShadowCasters && sun != null && sun.shadows != LightShadows.None;

            // Everything but the camera and the light: when any of it changes the walk is redone.
            var frame = new TreeWalkFrame
            {
                Lod = lod, ShadowDistance = Shadows ? shadowDistance : 0f, ProxyDistance = ShadowProxyDistance, PlantShadowDistance = PlantShadowDistance,
                FernDrawDistance = FernDrawDistance, UndergrowthDrawDistance = UndergrowthDrawDistance, Shadows = Shadows, CullShadows = cull,
                Pad = 2f + (ReuseWalk ? TreeDrawRules.ReuseMove : 0f), SweepPad = ReuseWalk ? (float)Math.Sin(TreeDrawRules.ReuseLightTurn * Math.PI / 180.0) : 0f,
            };

            bool sameInputs = hasWalk && walkVersion == setsVersion && SameSettings(frame, walkFrame);
            if (ReuseWalk && TreeDrawRules.CanReuse(sameInputs, eye, walkEye, forward, walkForward, up, walkUp, camera.fieldOfView, walkFov, camera.aspect, walkAspect, light, walkLight))
            {
                WalkReused = true;
            }
            else
            {
                walkClock.Restart();
                frame.Eye = eye;
                frame.Frustum = TreeFrustum.FromView(eye, forward, up, camera.fieldOfView, camera.aspect, camera.farClipPlane, ReuseWalk ? TreeDrawRules.ReuseTurn : 0f);
                frame.LightForward = light;
                frame.LowestReceiver = LowestCell() - TreeDrawRules.ReceiverDrop;
                walker.Walk(sets, frame, walkPrototypes);
                walkClock.Stop();
                WalkMs = walkClock.Elapsed.TotalMilliseconds;
                hasWalk = true;
                walkVersion = setsVersion;
                walkFrame = frame;
                walkEye = eye;
                walkForward = forward;
                walkUp = up;
                walkLight = light;
                walkFov = camera.fieldOfView;
                walkAspect = camera.aspect;
            }

            // Nearest band first, shadows last: when a cap is hit, what is dropped is the farthest band or a shadow.
            walkClock.Restart();
            stopped = false;
            TreeGather gather = walker.Gather;
            int protoCount = prototypes.Count;
            for (int bucket = 0; bucket < TreeCellWalker.BucketCount && !stopped; bucket++)
            {
                for (int p = 0; p < protoCount && !stopped; p++) Submit(gather, bucket, p);
            }

            walkClock.Stop();
            SubmitMs = walkClock.Elapsed.TotalMilliseconds;
        }

        private static bool SameSettings(in TreeWalkFrame a, in TreeWalkFrame b)
        {
            return a.Lod.Full == b.Lod.Full && a.Lod.Mid == b.Lod.Mid && a.Lod.Impostor == b.Lod.Impostor && a.ShadowDistance == b.ShadowDistance && a.ProxyDistance == b.ProxyDistance &&
                   a.PlantShadowDistance == b.PlantShadowDistance && a.FernDrawDistance == b.FernDrawDistance &&
                   a.UndergrowthDrawDistance == b.UndergrowthDrawDistance && a.Shadows == b.Shadows &&
                   a.CullShadows == b.CullShadows && a.Pad == b.Pad && a.SweepPad == b.SweepPad;
        }

        /// <summary>The lowest point of any registered cell (scene metres).</summary>
        private float LowestCell()
        {
            float lowest = float.MaxValue;
            foreach (TreeCellSet set in sets)
            {
                foreach (TreeCell cell in set.Cells)
                {
                    if (cell.Min.y < lowest) lowest = cell.Min.y;
                }
            }

            return lowest == float.MaxValue ? 0f : lowest;
        }

        private float walkProtoImpostor = -1f, walkProtoPixels = -1f;

        private void EnsureWalkPrototypes(TreeLodDistances lod)
        {
            if (walkPrototypes.Length == prototypes.Count && walkProtoImpostor == lod.Impostor && walkProtoPixels == ImpostorMaxPixels) return;
            walkProtoImpostor = lod.Impostor;
            walkProtoPixels = ImpostorMaxPixels;
            hasWalk = false;
            walkPrototypes = new TreeWalkPrototype[prototypes.Count];
            for (int p = 0; p < prototypes.Count; p++)
            {
                Prototype proto = prototypes[p];
                walkPrototypes[p] = new TreeWalkPrototype
                {
                    Plant = proto.IsPlant, Fern = proto.IsFern, Undergrowth = proto.IsUndergrowth, Proxy = proto.Proxy.Length > 0,
                    // A prototype with an impostor changes its look at the impostor distance: decided per tree there (hysteresis).
                    SingleMesh = SameParts(proto.Detail, proto.Mid) && SameParts(proto.Mid, proto.Low) && proto.Impostor.Length == 0,
                };
                if (proto.Impostor.Length > 0)
                {
                    float own = TreeImpostor.DistanceFor(lod.Impostor, proto.SilhouetteMetres, ImpostorMaxPixels);
                    if (own != lod.Impostor)
                    {
                        walkPrototypes[p].OwnLod = true;
                        walkPrototypes[p].Lod = new TreeLodDistances(lod.Full, lod.Mid, own);
                    }
                }
            }
        }

        private static bool SameParts(Part[] a, Part[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Mesh != b[i].Mesh || a[i].Submesh != b[i].Submesh || a[i].Material != b[i].Material || a[i].Local != b[i].Local) return false;
            }

            return true;
        }

        /// <summary>
        /// Submits a bucket's gathered instances of prototype <paramref name="p"/> in calls of up to 1023: a visible band (no shadows) or
        /// a shadow ring (shadow-only: the simplest mesh LOD near, the proxy beyond). Stops the frame when a cap is hit.
        /// </summary>
        private void Submit(TreeGather gather, int bucket, int p)
        {
            int k = gather.Key(bucket, p);
            int total = gather.Count(k);
            if (total <= 0) return;
            Prototype proto = prototypes[p];
            bool shadow = bucket >= TreeDrawRules.BandCount;
            int ring = bucket - TreeDrawRules.BandCount;
            Part[] parts = !shadow ? proto.For((TreeDrawBand)bucket) : ring == 0 ? proto.Low : proto.Proxy;
            if (parts.Length == 0) return;
            int partCount = 0, partTriangles = 0;
            foreach (Part part in parts)
            {
                if (part.Broken || (shadow && !part.CastsShadows)) continue;
                partCount++;
                partTriangles += part.Triangles;
            }

            if (partCount == 0) return;
            Matrix4x4[] instances = gather.Data(k);
            float r = Index.RadiusOf(p) * 2f + 2f, h = Index.HeightOf(p) * 2f + 2f;
            for (int start = 0, slice = 0; start < total && !stopped; start += TreeDrawRules.MaxInstancesPerCall, slice++)
            {
                int count = Math.Min(TreeDrawRules.MaxInstancesPerCall, total - start);
                int allowed = TreeDrawRules.Allowed(count, partCount, InstancesThisFrame, DrawCallsThisFrame, MaxInstancesPerFrame, MaxDrawCallsPerFrame);
                if (allowed < count)
                {
                    if (!warnedCap)
                    {
                        warnedCap = true;
                        Debug.LogWarning($"Endless road: the instanced tree renderer hit its cap ({MaxInstancesPerFrame} instances / {MaxDrawCallsPerFrame} draws per frame); the farther bands and shadows are skipped while it lasts. Load: {RegisteredTrees} instances registered.");
                    }

                    stopped = true;
                    if (allowed <= 0) return;
                    count = allowed;
                }

                // Bounds of the slice's instance positions grown by the prototype's crown and height.
                gather.SliceBounds(k, slice, out Vector3 min, out Vector3 max);
                var bounds = new Bounds();
                bounds.SetMinMax(min - new Vector3(r, 2f, r), max + new Vector3(r, h, r));
                foreach (Part part in parts)
                {
                    if (part.Broken || (shadow && !part.CastsShadows)) continue;
                    var rp = new RenderParams(part.Material)
                    {
                        layer = layer,
                        worldBounds = bounds,
                        shadowCastingMode = shadow ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off,
                        receiveShadows = true,
                        lightProbeUsage = richLight ? LightProbeUsage.Off : LightProbeUsage.BlendProbes,   // Off = the scene's ambient probe
                        reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
                        motionVectorMode = MotionVectorGenerationMode.Camera,
                    };

                    try
                    {
                        if (part.Identity)
                        {
                            Graphics.RenderMeshInstanced(rp, part.Mesh, part.Submesh, instances, count, start);
                        }
                        else
                        {
                            for (int i = 0; i < count; i++) partBuffer[i] = instances[start + i] * part.Local;
                            Graphics.RenderMeshInstanced(rp, part.Mesh, part.Submesh, partBuffer, count);
                        }

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

                long triangles = (long)partTriangles * count;
                InstancesThisFrame += count;
                if (shadow)
                {
                    ShadowInstancesThisFrame += count;
                    ShadowTrianglesThisFrame += triangles;
                    ringInstances[ring] += count;
                    ringTriangles[ring] += triangles;
                }
                else
                {
                    bandInstances[bucket] += count;
                    bandTriangles[bucket] += triangles;
                    TrianglesThisFrame += triangles;
                    if (proto.IsPlant)
                    {
                        PlantsDrawn += count;
                        PlantTriangles += triangles;
                        if (proto.IsFern) FernsDrawn += count;
                    }
                }
            }
        }

        /// <summary>The URP shadow distance (0 when shadows are off or the pipeline is not URP).</summary>
        private static float PipelineShadowDistance()
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
            UpdateBoxes(traveller);
        }

        /// <summary>Most box colliders on field-boundary pieces near the traveller.</summary>
        public int MaxBoxes = 160;

        /// <summary>
        /// Most box colliders while a walled city's or castle's piece is within reach (2026-10-09: Durncaster's castle alone has about a
        /// thousand pieces and the traveller walked through its walls at 160). Over the budget the nearest pieces win.
        /// </summary>
        public int CityMaxBoxes = 1200;

        private readonly List<BoxCollider> boxes = new List<BoxCollider>();

        private struct BoxCandidate
        {
            public float D;
            public int P;
            public Matrix4x4 M;
        }

        private readonly List<BoxCandidate> boxCandidates = new List<BoxCandidate>();
        private static readonly Comparison<BoxCandidate> ByDistance = (a, b) => a.D.CompareTo(b.D);

        /// <summary>Box colliders in use on field-boundary pieces.</summary>
        public int ActiveBoxes { get; private set; }

        /// <summary>Pieces with a box within reach at the last refresh (more than <see cref="ActiveBoxes"/> when over the budget).</summary>
        public int CandidateBoxes { get; private set; }

        /// <summary>
        /// Boxes on the walls, hedges, fences and a city's solid pieces within <see cref="ColliderRadius"/> of the traveller (posed like
        /// the pieces). The candidates are gathered in a stable order (so a box keeps its piece from one refresh to the next and is not
        /// moved) and sorted by distance only when there are more than the budget.
        /// </summary>
        private void UpdateBoxes(Vector3 traveller)
        {
            int used = 0, usedMeshes = 0;
            boxCandidates.Clear();
            if (Enabled)
            {
                float r2 = (ColliderRadius + 4f) * (ColliderRadius + 4f);
                bool city = false;
                foreach (TreeCellSet set in sets)
                {
                    if (set.Pieces == 0) continue;
                    foreach (TreeCell cell in set.Cells)
                    {
                        if (cell.SqrDistanceXZ(traveller.x, traveller.z) > r2) continue;
                        for (int p = 0; p < prototypes.Count; p++)
                        {
                            Matrix4x4[] list = cell.ByPrototype[p];
                            Prototype proto = prototypes[p];
                            if (list == null || (!proto.HasBox && proto.OwnShapes.Length == 0)) continue;
                            for (int i = 0; i < list.Length; i++)
                            {
                                float dx = list[i].m03 - traveller.x, dz = list[i].m23 - traveller.z;
                                float d2 = dx * dx + dz * dz;
                                if (d2 > r2) continue;
                                boxCandidates.Add(new BoxCandidate { D = d2, P = p, M = list[i] });
                                city |= proto.IsCityPiece;
                            }
                        }
                    }
                }

                int budget = city ? Math.Max(MaxBoxes, CityMaxBoxes) : MaxBoxes;
                if (boxCandidates.Count > budget) boxCandidates.Sort(ByDistance);
                int take = Math.Min(budget, boxCandidates.Count);
                for (int k = 0; k < take; k++)
                {
                    Matrix4x4 m = boxCandidates[k].M;
                    Prototype proto = prototypes[boxCandidates[k].P];
                    var position = new Vector3(m.m03, m.m13, m.m23);
                    var column0 = new Vector3(m.m00, m.m10, m.m20);
                    var up = new Vector3(m.m01, m.m11, m.m21);
                    var forward = new Vector3(m.m02, m.m12, m.m22);
                    float scale = column0.magnitude;
                    if (scale < 1e-4f) continue;
                    Quaternion rotation = Quaternion.LookRotation(forward / scale, up / scale);
                    if (proto.HasBox)
                    {
                        PoseBox(TakeBox(used++), position, rotation, scale, proto.BoxCenter, proto.BoxSize);
                        continue;
                    }

                    // An opening (archway, gate, door, stairs): the prefab's own colliders.
                    foreach (OwnShape shape in proto.OwnShapes)
                    {
                        Matrix4x4 world = m * shape.Local;
                        var at = new Vector3(world.m03, world.m13, world.m23);
                        var c0 = new Vector3(world.m00, world.m10, world.m20);
                        float s = c0.magnitude;
                        if (s < 1e-4f) continue;
                        Quaternion turn = Quaternion.LookRotation(new Vector3(world.m02, world.m12, world.m22) / s, new Vector3(world.m01, world.m11, world.m21) / s);
                        if (shape.Mesh == null)
                        {
                            PoseBox(TakeBox(used++), at, turn, s, shape.BoxCenter, shape.BoxSize);
                        }
                        else if (usedMeshes < MaxMeshColliders)
                        {
                            MeshCollider mc = TakeMeshCollider(usedMeshes++);
                            Transform t = mc.transform;
                            if (t.position != at || t.rotation != turn) t.SetPositionAndRotation(at, turn);
                            var size = Vector3.one * s;
                            if (t.localScale != size) t.localScale = size;
                            if (mc.sharedMesh != shape.Mesh || mc.convex != shape.Convex)
                            {
                                mc.sharedMesh = null;
                                mc.convex = shape.Convex;
                                mc.sharedMesh = shape.Mesh;
                            }

                            if (!mc.enabled) mc.enabled = true;
                        }
                    }
                }
            }

            for (int i = usedMeshes; i < meshColliders.Count; i++)
            {
                if (meshColliders[i].enabled) meshColliders[i].enabled = false;
            }

            ActiveMeshColliders = usedMeshes;

            for (int i = used; i < boxes.Count; i++)
            {
                if (boxes[i].enabled) boxes[i].enabled = false;
            }

            ActiveBoxes = used;
            CandidateBoxes = boxCandidates.Count;
        }

        /// <summary>Most mesh colliders on openings (the prefabs' own: archways, gates, doors, stairs) near the traveller.</summary>
        public int MaxMeshColliders = 300;

        private readonly List<MeshCollider> meshColliders = new List<MeshCollider>();

        /// <summary>Mesh colliders in use on openings.</summary>
        public int ActiveMeshColliders { get; private set; }

        /// <summary>Poses a box (a box that keeps its piece is not moved: moving a static collider re-inserts it in the physics scene).</summary>
        private static void PoseBox(BoxCollider box, Vector3 position, Quaternion rotation, float scale, Vector3 center, Vector3 size)
        {
            Transform t = box.transform;
            if (t.position != position || t.rotation != rotation) t.SetPositionAndRotation(position, rotation);
            var scaled = Vector3.one * scale;
            if (t.localScale != scaled) t.localScale = scaled;
            if (box.center != center) box.center = center;
            if (box.size != size) box.size = size;
            if (!box.enabled) box.enabled = true;
        }

        private MeshCollider TakeMeshCollider(int i)
        {
            if (i < meshColliders.Count) return meshColliders[i];
            var go = new GameObject("Piece opening");
            go.layer = layer;
            go.transform.SetParent(colliderRoot, false);
            var mc = go.AddComponent<MeshCollider>();
            meshColliders.Add(mc);
            return mc;
        }

        private BoxCollider TakeBox(int i)
        {
            if (i < boxes.Count) return boxes[i];
            var go = new GameObject("Boundary piece");
            go.layer = layer;
            go.transform.SetParent(colliderRoot, false);
            var box = go.AddComponent<BoxCollider>();
            boxes.Add(box);
            return box;
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

        // ------------------------------------------------------------------ sight queries (cinematic camera)

        /// <summary>
        /// True when the sight line <paramref name="a"/>–<paramref name="b"/> passes through a near tree's trunk or the dense core
        /// of its crown (<see cref="MedievalZen.Cinematic.CinematicMath.TreeBlocks"/>). Only cells whose bounds the segment's
        /// box touches are visited; allocation-free.
        /// </summary>
        public bool SegmentBlocked(Vector3 a, Vector3 b)
        {
            if (!Enabled) return false;
            Vector3 lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
            foreach (TreeCellSet set in sets)
            {
                foreach (TreeCell cell in set.Cells)
                {
                    if (cell.Max.x < lo.x || cell.Min.x > hi.x || cell.Max.z < lo.z || cell.Min.z > hi.z || cell.Max.y < lo.y || cell.Min.y > hi.y) continue;
                    for (int p = 0; p < prototypes.Count; p++)
                    {
                        Matrix4x4[] list = cell.ByPrototype[p];
                        if (list == null || prototypes[p].IsPiece) continue;   // walls and hedges are low: they hide nothing from the camera
                        float height = Index.HeightOf(p), crown = Index.RadiusOf(p), trunk = prototypes[p].ColliderRadius;
                        for (int i = 0; i < list.Length; i++)
                        {
                            var position = new Vector3(list[i].m03, list[i].m13, list[i].m23);
                            float scale = new Vector3(list[i].m00, list[i].m10, list[i].m20).magnitude;
                            float reach = crown * scale;
                            if (position.x + reach < lo.x || position.x - reach > hi.x || position.z + reach < lo.z || position.z - reach > hi.z) continue;
                            if (MedievalZen.Cinematic.CinematicMath.TreeBlocks(a, b, position, height * scale, crown * scale, Mathf.Max(0.15f, trunk * scale))) return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>True when <paramref name="point"/> is within <paramref name="pad"/> metres of a near tree's trunk or inside its crown core.</summary>
        public bool PointInside(Vector3 point, float pad)
        {
            if (!Enabled) return false;
            foreach (TreeCellSet set in sets)
            {
                foreach (TreeCell cell in set.Cells)
                {
                    if (cell.SqrDistanceXZ(point.x, point.z) > pad * pad + 1f || point.y < cell.Min.y - pad || point.y > cell.Max.y + pad) continue;
                    for (int p = 0; p < prototypes.Count; p++)
                    {
                        Matrix4x4[] list = cell.ByPrototype[p];
                        if (list == null || prototypes[p].IsPiece) continue;   // walls and hedges are low: they hide nothing from the camera
                        float height = Index.HeightOf(p), crown = Index.RadiusOf(p), trunk = prototypes[p].ColliderRadius;
                        for (int i = 0; i < list.Length; i++)
                        {
                            var position = new Vector3(list[i].m03, list[i].m13, list[i].m23);
                            float scale = new Vector3(list[i].m00, list[i].m10, list[i].m20).magnitude;
                            if (MedievalZen.Cinematic.CinematicMath.InsideTree(point, pad, position, height * scale, crown * scale, Mathf.Max(0.15f, trunk * scale))) return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The nearest near tree whose prototype name contains <paramref name="nameKey"/> with its base between
        /// <paramref name="minDistance"/> and <paramref name="maxDistance"/> (horizontal) of <paramref name="point"/>: base, height
        /// and crown radius in metres (ambient effects: falling leaves under the birches). Allocation-free; a few times a minute.
        /// </summary>
        public bool NearestTree(Vector3 point, float minDistance, float maxDistance, string nameKey, out Vector3 position, out float height, out float crown)
        {
            position = default;
            height = crown = 0f;
            if (!Enabled || string.IsNullOrEmpty(nameKey)) return false;
            float best = float.MaxValue, min2 = minDistance * minDistance, max2 = maxDistance * maxDistance;
            bool found = false;
            foreach (TreeCellSet set in sets)
            {
                foreach (TreeCell cell in set.Cells)
                {
                    if (cell.SqrDistanceXZ(point.x, point.z) > max2) continue;
                    for (int p = 0; p < prototypes.Count; p++)
                    {
                        Matrix4x4[] list = cell.ByPrototype[p];
                        if (list == null || prototypes[p].Name.IndexOf(nameKey, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        for (int i = 0; i < list.Length; i++)
                        {
                            float dx = list[i].m03 - point.x, dz = list[i].m23 - point.z, d2 = dx * dx + dz * dz;
                            if (d2 < min2 || d2 > max2 || d2 >= best) continue;
                            best = d2;
                            float scale = new Vector3(list[i].m00, list[i].m10, list[i].m20).magnitude;
                            position = new Vector3(list[i].m03, list[i].m13, list[i].m23);
                            height = Index.HeightOf(p) * scale;
                            crown = Index.RadiusOf(p) * scale;
                            found = true;
                        }
                    }
                }
            }

            return found;
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
            foreach (Mesh mesh in proxyMeshes)
            {
                if (mesh != null) Object.Destroy(mesh);
            }

            proxyMeshes.Clear();
            if (proxyMaterial != null) Object.Destroy(proxyMaterial);
            if (impostorMaterial != null) Object.Destroy(impostorMaterial);
            sets.Clear();
        }
    }
}
