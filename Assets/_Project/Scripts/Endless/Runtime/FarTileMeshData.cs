using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// One vertex of a far tile's mesh, laid out exactly as the GPU vertex buffer (16 bytes): position (float3) and colour (UNorm8 x4,
    /// sRGB-encoded; the FarTerrain shader decodes it to linear). There is no normal: the shader shades every triangle flat from the
    /// screen-space derivatives of its position, and takes its colour from the triangle's provoking vertex (no interpolation).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FarVertex
    {
        /// <summary>Tile-local position (x, z in 0..tile size from the tile's minimum corner; y in absolute metres, drop included).</summary>
        public Vector3 position;

        /// <summary>Albedo, sRGB-encoded.</summary>
        public Color32 color;
    }

    /// <summary>Inputs of <see cref="FarTileMeshData.Build"/> besides the blueprint.</summary>
    public sealed class FarMeshSettings
    {
        /// <summary>
        /// Most triangles one far tile may have (ground + skirt + rocks + houses + trees); trees are thinned deterministically to fit.
        /// 0 = by the tile's grid: <see cref="FarTileMeshData.MidMaxTriangles"/> for a 4 m grid, <see cref="FarTileMeshData.FarMaxTriangles"/> else.
        /// </summary>
        public int MaxTriangles;

        /// <summary>How far the far ground sits below the true ground (its coarse edges stay under the near tiles').</summary>
        public float Drop = FarTileMeshData.DefaultDrop;

        /// <summary>Depth of the downward skirt around the tile's edge, metres.</summary>
        public float SkirtDepth = 6f;

        /// <summary>Build tree impostors (off: ground only; the ground still takes the canopy shade under woods).</summary>
        public bool IncludeTrees = true;

        /// <summary>Build rock solids for the blueprint's large rocks.</summary>
        public bool IncludeRocks = true;

        /// <summary>Build house solids for the blueprint's houses (and the landmarks' structures and standing stones).</summary>
        public bool IncludeHouses = true;

        /// <summary>Build low ridges along the blueprint's field boundaries (<see cref="TileBlueprint.FieldEdges"/>).</summary>
        public bool IncludeFieldEdges = true;

        /// <summary>Detailed impostors (null = by the grid: detailed on a 4 m grid, simple on an 8 m grid).</summary>
        public bool? DetailedImpostors;

        /// <summary>Trees per 100 m² (averaged over ±6 m) at which a ground face is fully under canopy.</summary>
        public float FullCoverDensity = 1.2f;

        /// <summary>Canopy share above which a ground face takes the canopy zone (dithered ±0.15).</summary>
        public float CanopyThreshold = 0.5f;

        /// <summary>Distant colour of each splat channel (linear), in <see cref="TerrainShaper"/> channel order (<see cref="GroundLook.LayerColors"/>).</summary>
        public Vector3[] ChannelColors = FarTileMeshData.AppearanceChannelColors;

        /// <summary>Colour the canopy zone leans toward (linear): the shade between crowns.</summary>
        public Vector3 CanopyColor = new Vector3(0.040f, 0.062f, 0.018f);

        /// <summary>How much of <see cref="CanopyColor"/> the forest floor takes in the canopy zone (0..1).</summary>
        public float CoverTint = 0.45f;

        /// <summary>Colour of the water zone along streams (linear: dark water with its muddy banks).</summary>
        public Vector3 StreamColor = new Vector3(0.035f, 0.055f, 0.050f);

        /// <summary>
        /// How much of the true splat mix a face's colour keeps besides its zone colour (0 = pure zone palette, 1 = the old smooth
        /// mix): keeps near and far in agreement where two zones blend, while each face stays one flat colour.
        /// </summary>
        public float BlendPull = 0.3f;

        /// <summary>Weight noise added to each zone's weight when a face picks its zone, so zone edges are ragged facets, not lines.</summary>
        public float ZoneDither = 0.2f;

        /// <summary>Per-face brightness jitter (± fraction): slopes shimmer like the low-poly ground near by.</summary>
        public float Jitter = 0.07f;

        /// <summary>Far-tree styles (null = <see cref="FarTreeStyles.Defaults"/>).</summary>
        public FarPropStyles Props;
    }

    /// <summary>
    /// A far tile as plain mesh data, built on a worker thread from its <see cref="TileBlueprint"/> (pure, no Unity objects), in the
    /// same faceted low-poly style as the near world:
    /// <list type="bullet">
    /// <item>Ground: the height grid (4 m on <see cref="TileLod.Mid"/> tiles, 8 m on <see cref="TileLod.Far"/> tiles) lowered by
    /// <see cref="FarMeshSettings.Drop"/>, every quad with its own four vertices so each of its two triangles has one flat colour, its
    /// diagonal along the terrain (or by hash on even ground) so facets do not form a grid. Each triangle takes a ground zone (a
    /// splat channel, the canopy shade under woods, or water) at its centre, dithered where zones blend, coloured from
    /// <see cref="GroundLook"/> with a little per-face brightness jitter. The tile's edge vertices lie on the 8 m grid's edge
    /// (odd ones interpolated), so neighbours of either grid meet exactly; a 6 m skirt hides any remaining T-junction gap.</item>
    /// <item>Trees: every blueprint tree as a low-poly solid of its species (<see cref="FarTreeStyle"/>), sized as measured.</item>
    /// <item>Rocks and houses: the blueprint's large rocks and the houses of places, as faceted solids.</item>
    /// </list>
    /// One 16-bit-indexed triangle list; the main thread only uploads it. Every triangle's colour sits on both its first and its last
    /// vertex, so it is flat under either provoking-vertex convention.
    /// </summary>
    public sealed class FarTileMeshData
    {
        /// <summary>Triangle cap of a <see cref="TileLod.Mid"/> tile (4 m grid, detailed impostors).</summary>
        public const int MidMaxTriangles = 36864;

        /// <summary>Triangle cap of a <see cref="TileLod.Far"/> tile (8 m grid, simple impostors).</summary>
        public const int FarMaxTriangles = 12288;

        /// <summary>Default triangle cap of an 8 m tile (kept for callers of the old name).</summary>
        public const int DefaultMaxTriangles = FarMaxTriangles;

        /// <summary>Default drop of the far ground (matches the old far terrains).</summary>
        public const float DefaultDrop = 1.5f;

        /// <summary>Trees are sunk this far into the ground so their base never floats on a slope.</summary>
        public const float TreeSink = 0.3f;

        /// <summary>Tile edges follow a grid of this spacing (metres) whatever the tile's own grid, so every pair of neighbours agrees.</summary>
        public const float EdgeSpacing = 8f;

        /// <summary>Most rock solids per tile.</summary>
        public const int MaxRocks = 160;

        /// <summary>Kept trees' crowns grow by at most this factor (horizontally) when a tile is thinned, so a wood stays closed.</summary>
        public const float MaxThinningGrowth = 1.3f;

        /// <summary>Ground zones: the splat channels (0..10, the endless road's channels in <see cref="TerrainShaper"/> order), then these.</summary>
        public const int ZoneCanopy = TerrainShaper.EndlessLayerCount;

        /// <summary>Water zone (streams).</summary>
        public const int ZoneWater = TerrainShaper.EndlessLayerCount + 1;

        /// <summary>Number of ground zones.</summary>
        public const int ZoneCount = TerrainShaper.EndlessLayerCount + 2;

        /// <summary>Short zone names (logs, statistics).</summary>
        public static readonly string[] ZoneNames = { "lush", "dry", "dirt", "rock", "forest floor", "flowers", "mud", "cobble", "mountain rock", "scree", "heath", "field gold", "field yellow", "field green", "leaf litter", "canopy", "water" };

        /// <summary>Distant colour of each channel as the near terrain shows it (texture mean × layer tint, <see cref="GroundLook.LayerColors"/>).</summary>
        public static readonly Vector3[] AppearanceChannelColors = GroundLook.LayerColors();

        /// <summary>Distant colour (linear) of a wood's floor under its canopy (<see cref="GroundLook.WoodFloorColor"/> at mean litter).</summary>
        public static readonly Vector3 WoodFloorColor = GroundLook.WoodFloorColor();

        /// <summary>Vertices (exactly <see cref="VertexCount"/> long).</summary>
        public FarVertex[] Vertices;

        /// <summary>Triangle list indices (exactly <see cref="IndexCount"/> long).</summary>
        public ushort[] Indices;

        /// <summary>Number of vertices.</summary>
        public int VertexCount;

        /// <summary>Number of indices (3 per triangle).</summary>
        public int IndexCount;

        /// <summary>Triangles of the ground grid (the first ones).</summary>
        public int GroundTriangles;

        /// <summary>Triangles of the edge skirt (after the ground).</summary>
        public int SkirtTriangles;

        /// <summary>Triangles of the rock solids (after the skirt).</summary>
        public int RockTriangles;

        /// <summary>Triangles of the house solids (after the rocks).</summary>
        public int HouseTriangles;

        /// <summary>Triangles of the tree impostors (the last ones).</summary>
        public int TreeTriangles;

        /// <summary>Trees drawn.</summary>
        public int TreesDrawn;

        /// <summary>Trees dropped to keep within the triangle cap.</summary>
        public int TreesThinned;

        /// <summary>Rocks drawn.</summary>
        public int RocksDrawn;

        /// <summary>Houses drawn.</summary>
        public int HousesDrawn;

        /// <summary>Triangles of the field-boundary ridges (after the houses).</summary>
        public int EdgeTriangles;

        /// <summary>Field-boundary stretches drawn.</summary>
        public int EdgesDrawn;

        /// <summary>Horizontal growth of the kept trees' crowns (1 when nothing was thinned).</summary>
        public float CrownGrowth = 1f;

        /// <summary>The zone of every ground triangle, in index order.</summary>
        public byte[] FaceZones;

        /// <summary>Horizontal area of the ground in each zone, m².</summary>
        public float[] ZoneArea;

        /// <summary>Height samples per edge of the ground grid.</summary>
        public int GridResolution;

        /// <summary>Tile-local bounds of every vertex.</summary>
        public Vector3 BoundsMin, BoundsMax;

        /// <summary>Edge of the tile in metres.</summary>
        public float TileSize;

        /// <summary>Total triangles.</summary>
        public int Triangles => IndexCount / 3;

        /// <summary>Triangles one tree impostor of <paramref name="shape"/> costs (detailed on 4 m tiles, simple on 8 m tiles).</summary>
        public static int TrianglesOf(FarTreeShape shape, bool detailed)
        {
            if (detailed) return shape == FarTreeShape.Conifer ? 18 : shape == FarTreeShape.Dead ? 5 : 23;
            // Broadleaf on the 8 m tiles: the low dome of rounded lobes (2026-10-06), 12 triangles.
            return shape == FarTreeShape.Conifer ? 8 : shape == FarTreeShape.Dead ? 3 : 12;
        }

        /// <summary>Triangles one tree impostor of <paramref name="style"/> costs: lobed crowns (<see cref="TreeLobes"/>) by their lobes, else by shape.</summary>
        public static int TrianglesOf(FarTreeStyle style, bool detailed) =>
            style.HasLobes && detailed ? TreeLobes.MidTriangles(style) : TrianglesOf(style.Shape, detailed);

        /// <summary>Vertices one tree impostor of <paramref name="style"/> uses (lobed crowns: three rings per lobe, shared within the lobe).</summary>
        public static int VerticesOf(FarTreeStyle style, bool detailed) =>
            style.HasLobes && detailed ? TreeLobes.MidVertices(style) : VerticesOf(style.Shape, detailed);

        /// <summary>Vertices one tree impostor of <paramref name="shape"/> uses.</summary>
        public static int VerticesOf(FarTreeShape shape, bool detailed)
        {
            if (detailed) return shape == FarTreeShape.Conifer ? 17 : shape == FarTreeShape.Dead ? 8 : 16;
            return shape == FarTreeShape.Conifer ? 6 : shape == FarTreeShape.Dead ? 4 : 8;
        }

        /// <summary>Triangles of one rock solid.</summary>
        public const int RockTris = 15;

        /// <summary>Vertices of one rock solid.</summary>
        public const int RockVerts = 11;

        /// <summary>Triangles of one house or landmark solid.</summary>
        public static int HouseTrianglesOf(FarPropKind kind)
        {
            switch (kind)
            {
                case FarPropKind.Windmill: return 20;
                case FarPropKind.Tower: return 14;
                case FarPropKind.Chapel: return 26;
                case FarPropKind.Mill: return 22;
                case FarPropKind.Stone: return 10;
                case FarPropKind.Ruin: return 24;
                default: return 14;
            }
        }

        /// <summary>Vertices of one house or landmark solid.</summary>
        public static int HouseVerticesOf(FarPropKind kind)
        {
            switch (kind)
            {
                case FarPropKind.Windmill: return 17;
                case FarPropKind.Tower: return 13;
                case FarPropKind.Chapel: return 27;
                case FarPropKind.Mill: return 22;
                case FarPropKind.Stone: return 8;
                case FarPropKind.Ruin: return 16;
                default: return 14;
            }
        }

        /// <summary>Families a distant tile draws as solids: cabins, windmills, the inns' and villages' building presets, landmark structures and standing stones.</summary>
        public static bool IsSolid(string role) => role == PropRole.Cabin || role == PropRole.Windmill || PropRole.IsLandmark(role) || role == PropRole.StoneRunes || PropRole.IsBuilding(role) ||
            role == PropRole.CityWall || role == PropRole.CityTower || role == PropRole.CastleModule || role == PropRole.CastlePiece || FarCastle.IsProxy(role);

        /// <summary>Triangles of one field-boundary stretch (a ridge of two sloped faces).</summary>
        public const int EdgeTris = 4;

        /// <summary>Vertices of one field-boundary stretch.</summary>
        public const int EdgeVerts = 6;

        /// <summary>Most field-boundary stretches drawn per tile.</summary>
        public const int MaxFieldEdges = 1200;

        /// <summary>Width at the foot and height of the distant ridge of a hedge, wall and fence, metres (a little wider than life so a hedge line holds a pixel at 500 m).</summary>
        public static readonly Vector2[] EdgeShape = { new Vector2(2.0f, 1.9f), new Vector2(1.1f, 1.25f), new Vector2(0.7f, 1.1f) };

        /// <summary>
        /// Colour (linear) of a hedge, wall and fence ridge: the Fantasy Kingdom hedge's atlas colour (101, 103, 66) in the shade of its
        /// own leaves, the Meadow dry-stone wall (104, 100, 92) and fence wood (106, 93, 77), measured at their triangles' UVs.
        /// </summary>
        public static readonly Vector3[] EdgeColor = { new Vector3(0.1041f, 0.1085f, 0.0436f), new Vector3(0.1398f, 0.1267f, 0.1079f), new Vector3(0.1437f, 0.1095f, 0.0746f) };

        /// <summary>Ground + skirt triangles of a tile with <paramref name="res"/> height samples per edge.</summary>
        public static int GroundAndSkirtTriangles(int res) => 2 * (res - 1) * (res - 1) + 4 * 2 * (res - 1);

        /// <summary>Ground + skirt vertices of a tile with <paramref name="res"/> height samples per edge (four per quad).</summary>
        public static int GroundAndSkirtVertices(int res) => 4 * (res - 1) * (res - 1) + 4 * 4 * (res - 1);

        /// <summary>The triangle cap for a tile with <paramref name="res"/> height samples per edge and a tile size of <paramref name="tileSize"/>.</summary>
        public static int CapFor(int res, float tileSize, int requested)
        {
            if (requested > 0) return requested;
            return tileSize / Math.Max(1, res - 1) < 6f ? MidMaxTriangles : FarMaxTriangles;
        }

        /// <summary>
        /// Builds the mesh data of a far tile. <paramref name="outsideHeight"/> is accepted for compatibility and not needed any more
        /// (faces are shaded flat from their own vertices, so there are no edge normals to agree on).
        /// </summary>
        public static FarTileMeshData Build(TileBlueprint bp, FarTreeStyles styles, FarMeshSettings settings, Func<double, double, float> outsideHeight = null)
        {
            if (bp == null) throw new ArgumentNullException(nameof(bp));
            if (bp.Heights == null) throw new ArgumentException("The blueprint has no heights.", nameof(bp));
            settings = settings ?? new FarMeshSettings();
            styles = styles ?? FarTreeStyles.Defaults;
            FarPropStyles propStyles = settings.Props ?? FarPropStyles.Defaults;

            int res = bp.Heights.GetLength(0);
            if (res < 2 || bp.Heights.GetLength(1) != res) throw new ArgumentException("Heights must be square with at least 2 samples per edge.", nameof(bp));
            float ts = bp.TileSize;
            float spacing = ts / (res - 1);
            bool detailed = settings.DetailedImpostors ?? spacing < 6f;
            int cap = CapFor(res, ts, settings.MaxTriangles);
            float drop = settings.Drop;

            // ---- what goes in: rocks and houses always (small), trees thinned to the rest of the budget
            var rocks = new List<PropPlacement>();
            var houses = new List<PropPlacement>();
            if (bp.Props != null)
            {
                foreach (PropPlacement prop in bp.Props)
                {
                    if (settings.IncludeHouses && IsSolid(prop.role)) houses.Add(prop);
                    else if (settings.IncludeRocks && PropRole.IsRock(prop.role) && rocks.Count < MaxRocks) rocks.Add(prop);
                }
            }

            int groundTris = GroundAndSkirtTriangles(res);
            int groundVerts = GroundAndSkirtVertices(res);
            int houseTris = 0, houseVerts = 0;
            foreach (PropPlacement h in houses)
            {
                FarPropKind kind = propStyles.Resolve(h.role, h.variant).Kind;
                houseTris += HouseTrianglesOf(kind);
                houseVerts += HouseVerticesOf(kind);
            }

            int rockTris = rocks.Count * RockTris;
            int edgeCount = settings.IncludeFieldEdges && bp.FieldEdges != null ? Math.Min(bp.FieldEdges.Count, MaxFieldEdges) : 0;
            int fixedTris = groundTris + rockTris + houseTris + edgeCount * EdgeTris;
            int fixedVerts = groundVerts + rocks.Count * RockVerts + houseVerts + edgeCount * EdgeVerts;

            List<TreePlacement> trees = bp.Trees ?? new List<TreePlacement>();
            var keep = new bool[trees.Count];
            int wantedTris = 0;
            if (settings.IncludeTrees)
            {
                foreach (TreePlacement t in trees) wantedTris += TrianglesOf(styles.Resolve(t.role, t.variant), detailed);
            }

            int treeBudget = Math.Max(0, cap - fixedTris);
            int vertexRoom = ushort.MaxValue - fixedVerts;
            double keepRatio = wantedTris <= treeBudget || wantedTris == 0 ? 1.0 : (double)treeBudget / wantedTris;
            int keptTris = 0, keptVerts = 0, kept = 0, thinned = 0;
            if (settings.IncludeTrees)
            {
                for (int i = 0; i < trees.Count; i++)
                {
                    FarTreeStyle treeStyle = styles.Resolve(trees[i].role, trees[i].variant);
                    int tt = TrianglesOf(treeStyle, detailed), tv = VerticesOf(treeStyle, detailed);
                    bool take = keepRatio >= 1.0 || Math.Floor((i + 1) * keepRatio) > Math.Floor(i * keepRatio);
                    if (take && keptTris + tt <= treeBudget && keptVerts + tv <= vertexRoom)
                    {
                        keep[i] = true;
                        keptTris += tt;
                        keptVerts += tv;
                        kept++;
                    }
                    else
                    {
                        thinned++;
                    }
                }
            }

            int vertexCount = fixedVerts + keptVerts;
            var data = new FarTileMeshData
            {
                TileSize = ts,
                GridResolution = res,
                Vertices = new FarVertex[vertexCount],
                Indices = new ushort[(fixedTris + keptTris) * 3],
                TreesDrawn = kept,
                TreesThinned = thinned,
                FaceZones = new byte[2 * (res - 1) * (res - 1)],
                ZoneArea = new float[ZoneCount],
                CrownGrowth = kept > 0 && thinned > 0 ? Math.Min(MaxThinningGrowth, (float)Math.Sqrt((kept + thinned) / (double)kept)) : 1f,
            };

            var b = new Builder(data);
            var ground = new GroundSampler(bp, settings, trees, spacing);
            BuildGround(b, ground, bp, settings, res, spacing, drop);
            data.GroundTriangles = b.Triangle;
            BuildSkirt(b, ground, bp, settings, res, spacing, drop);
            data.SkirtTriangles = b.Triangle - data.GroundTriangles;

            foreach (PropPlacement rock in rocks)
            {
                AddRock(b, ground, rock, propStyles.Resolve(rock.role, rock.variant), drop);
                data.RocksDrawn++;
            }

            data.RockTriangles = b.Triangle - data.GroundTriangles - data.SkirtTriangles;
            foreach (PropPlacement house in houses)
            {
                AddHouse(b, house, propStyles.Resolve(house.role, house.variant), drop);
                data.HousesDrawn++;
            }

            data.HouseTriangles = b.Triangle - data.GroundTriangles - data.SkirtTriangles - data.RockTriangles;
            if (edgeCount > 0)
            {
                float[] heights = EdgeStitchedHeights(bp.Heights, bp.HeightRange, bp.TileSize);
                for (int e = 0; e < edgeCount; e++) AddFieldEdge(b, bp.FieldEdges[e], heights, res, spacing, drop);
                data.EdgesDrawn = edgeCount;
            }

            data.EdgeTriangles = b.Triangle - data.GroundTriangles - data.SkirtTriangles - data.RockTriangles - data.HouseTriangles;
            for (int t = 0; t < trees.Count; t++)
            {
                if (!keep[t]) continue;
                AddTree(b, trees[t], styles.Resolve(trees[t].role, trees[t].variant), drop, detailed, data.CrownGrowth);
            }

            data.TreeTriangles = b.Triangle - data.GroundTriangles - data.SkirtTriangles - data.RockTriangles - data.HouseTriangles - data.EdgeTriangles;
            data.VertexCount = b.Vertex;
            data.IndexCount = b.Index;
            if (b.Vertex != data.Vertices.Length || b.Index != data.Indices.Length)
            {
                throw new InvalidOperationException($"Far tile mesh: {b.Vertex}/{data.Vertices.Length} vertices, {b.Index}/{data.Indices.Length} indices.");
            }

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < b.Vertex; i++)
            {
                Vector3 p = data.Vertices[i].position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            data.BoundsMin = min;
            data.BoundsMax = max;
            return data;
        }

        // ------------------------------------------------------------------ ground

        /// <summary>
        /// The ground's heights with the tile edges on the <see cref="EdgeSpacing"/> grid: edge samples between two 8 m samples are
        /// the linear interpolation of those, so a 4 m tile's edge is exactly an 8 m tile's edge (bit-identical where shared).
        /// </summary>
        public static float[] EdgeStitchedHeights(float[,] heights, float range, float tileSize)
        {
            int res = heights.GetLength(0);
            var h = new float[res * res];
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++) h[j * res + i] = heights[j, i] * range;
            }

            float spacing = tileSize / (res - 1);
            int k = Math.Max(1, (int)Math.Round(EdgeSpacing / spacing));
            if (k <= 1 || (res - 1) % k != 0) return h;
            for (int e = 0; e < res; e++)
            {
                int r = e % k;
                if (r == 0) continue;
                int e0 = e - r, e1 = e0 + k;
                float t = (float)r / k;
                // Bottom (z = 0), top (z = max), left (x = 0), right (x = max).
                h[e] = Mix(h[e0], h[e1], t);
                h[(res - 1) * res + e] = Mix(h[(res - 1) * res + e0], h[(res - 1) * res + e1], t);
                h[e * res] = Mix(h[e0 * res], h[e1 * res], t);
                h[e * res + res - 1] = Mix(h[e0 * res + res - 1], h[e1 * res + res - 1], t);
            }

            return h;
        }

        /// <summary>Linear interpolation written so that the same two values and t give the same result in any tile.</summary>
        private static float Mix(float a, float b, float t) => t == 0.5f ? (a + b) * 0.5f : a + (b - a) * t;

        /// <summary>
        /// True when a quad's diagonal runs from its (i, j) corner to its (i + 1, j + 1) corner: the diagonal whose ends differ less in
        /// height (it follows ridges and valleys), or by hash of the quad's absolute position where the two are within 0.2 m.
        /// </summary>
        public static bool DiagonalAD(float ha, float hb, float hc, float hd, long qx, long qz)
        {
            float dAD = Math.Abs(ha - hd), dBC = Math.Abs(hb - hc);
            if (Math.Abs(dAD - dBC) > 0.2f) return dAD < dBC;
            return (Hash(qx * 73856093L ^ qz * 19349663L ^ 0x5bd1e995L) & 1u) == 0u;
        }

        private static void BuildGround(Builder b, GroundSampler g, TileBlueprint bp, FarMeshSettings s, int res, float spacing, float drop)
        {
            float[] h = EdgeStitchedHeights(bp.Heights, bp.HeightRange, bp.TileSize);
            long baseX = (long)bp.Coord.X * (res - 1), baseZ = (long)bp.Coord.Z * (res - 1);
            float halfArea = spacing * spacing * 0.5f;
            int face = 0;
            for (int j = 0; j < res - 1; j++)
            {
                for (int i = 0; i < res - 1; i++)
                {
                    float ha = h[j * res + i], hb = h[j * res + i + 1], hc = h[(j + 1) * res + i], hd = h[(j + 1) * res + i + 1];
                    var a = new Vector3(i * spacing, ha - drop, j * spacing);
                    var bb = new Vector3((i + 1) * spacing, hb - drop, j * spacing);
                    var c = new Vector3(i * spacing, hc - drop, (j + 1) * spacing);
                    var d = new Vector3((i + 1) * spacing, hd - drop, (j + 1) * spacing);
                    long qx = baseX + i, qz = baseZ + j;
                    bool ad = DiagonalAD(ha, hb, hc, hd, qx, qz);
                    // Each triangle's zone at its centre (x, z = mean of its corners).
                    Vector3 t1 = ad ? (a + bb + d) / 3f : (a + bb + c) / 3f;
                    Vector3 t2 = ad ? (a + c + d) / 3f : (bb + c + d) / 3f;
                    Color32 c1 = g.FaceColor(t1.x, t1.z, (uint)Hash(qx * 2 * 92837111L ^ qz * 689287499L), out int z1);
                    Color32 c2 = g.FaceColor(t2.x, t2.z, (uint)Hash((qx * 2 + 1) * 92837111L ^ qz * 689287499L), out int z2);
                    // Pass rock takes the near rock skin's strata bands (RockSkinMeshData.StrataTone), so near and far agree.
                    if (z1 == TerrainShaper.MountainRockLayer) c1 = StrataToned(c1, bp, t1, drop);
                    if (z2 == TerrainShaper.MountainRockLayer) c2 = StrataToned(c2, bp, t2, drop);
                    b.Data.FaceZones[face++] = (byte)z1;
                    b.Data.FaceZones[face++] = (byte)z2;
                    b.Data.ZoneArea[z1] += halfArea;
                    b.Data.ZoneArea[z2] += halfArea;
                    int v = b.Vertex;
                    if (ad)
                    {
                        // Triangles (b, a, d) and (c, d, a): first and last vertex of each carry its colour.
                        b.Add(a, c2);
                        b.Add(bb, c1);
                        b.Add(c, c2);
                        b.Add(d, c1);
                        b.Tri(v + 1, v, v + 3);
                        b.Tri(v + 2, v + 3, v);
                    }
                    else
                    {
                        // Triangles (a, c, b) and (d, b, c).
                        b.Add(a, c1);
                        b.Add(bb, c1);
                        b.Add(c, c2);
                        b.Add(d, c2);
                        b.Tri(v, v + 2, v + 1);
                        b.Tri(v + 3, v + 1, v + 2);
                    }
                }
            }
        }

        /// <summary>A mountain-rock face's colour (sRGB) × the strata tone at its centre (tile-local, lowered by <paramref name="drop"/>).</summary>
        private static Color32 StrataToned(Color32 c, TileBlueprint bp, Vector3 centre, float drop)
        {
            float tone = RockSkinMeshData.StrataTone((double)bp.Coord.X * bp.TileSize + centre.x, centre.y + drop, (double)bp.Coord.Z * bp.TileSize + centre.z);
            float f = (float)Math.Pow(Math.Max(0.01f, tone), 1.0 / 2.2);   // a linear factor applied to sRGB values
            return new Color32((byte)Math.Min(255f, c.r * f + 0.5f), (byte)Math.Min(255f, c.g * f + 0.5f), (byte)Math.Min(255f, c.b * f + 0.5f), c.a);
        }

        private static void BuildSkirt(Builder b, GroundSampler g, TileBlueprint bp, FarMeshSettings s, int res, float spacing, float drop)
        {
            float[] h = EdgeStitchedHeights(bp.Heights, bp.HeightRange, bp.TileSize);
            float ts = bp.TileSize;
            for (int side = 0; side < 4; side++)
            {
                Vector3 outward = side == 0 ? new Vector3(0f, 0f, -1f) : side == 1 ? new Vector3(1f, 0f, 0f) : side == 2 ? new Vector3(0f, 0f, 1f) : new Vector3(-1f, 0f, 0f);
                for (int k = 0; k < res - 1; k++)
                {
                    Vector3 p0 = EdgePoint(side, k, res, spacing, ts, h, drop), p1 = EdgePoint(side, k + 1, res, spacing, ts, h, drop);
                    Vector3 mid = (p0 + p1) * 0.5f;
                    Color32 col = g.FaceColor(Mathf.Clamp(mid.x, 0f, ts), Mathf.Clamp(mid.z, 0f, ts), (uint)(side * 7919 + k), out _, 0.75f);
                    int v = b.Vertex;
                    b.Add(p0, col);
                    b.Add(p1, col);
                    b.Add(p0 - new Vector3(0f, s.SkirtDepth, 0f), col);
                    b.Add(p1 - new Vector3(0f, s.SkirtDepth, 0f), col);
                    b.Facing(v, v + 1, v + 2, outward);
                    b.Facing(v + 2, v + 1, v + 3, outward);
                }
            }
        }

        /// <summary>The k-th edge point of a side: 0 = z min (x rising), 1 = x max (z rising), 2 = z max (x rising), 3 = x min (z rising).</summary>
        private static Vector3 EdgePoint(int side, int k, int res, float spacing, float ts, float[] h, float drop)
        {
            switch (side)
            {
                case 0: return new Vector3(k * spacing, h[k] - drop, 0f);
                case 1: return new Vector3(ts, h[k * res + res - 1] - drop, k * spacing);
                case 2: return new Vector3(k * spacing, h[(res - 1) * res + k] - drop, ts);
                default: return new Vector3(0f, h[k * res] - drop, k * spacing);
            }
        }

        /// <summary>
        /// The zone of a ground face from its splat weights (normalised), canopy share and wetness: water where the stream is, the
        /// canopy shade where the woods are dense, else the splat channel with the largest weight after a dither of up to
        /// <paramref name="dither"/> per channel (from <paramref name="hash"/>), so blends come out as ragged mixes of whole faces.
        /// </summary>
        public static int ChooseZone(float[] weights, float canopy, float wet, uint hash, float dither, float canopyThreshold = 0.5f)
        {
            if (wet > 0f && wet + 0.3f * (Hash01(hash ^ 0x2545F491u) - 0.5f) > 0.5f) return ZoneWater;
            if (canopy > 0f && canopy + 0.3f * (Hash01(hash ^ 0x68E31DA4u) - 0.5f) > canopyThreshold) return ZoneCanopy;
            // Canopy floor pass (2026-10-06): the wood floor's mix (forest floor with mud as leaf litter, GroundLook.WoodFloorBlend)
            // is one zone whichever of the two leads, so a wood's distant floor is its mean colour, not faces of bare mud.
            // Leaf-litter pass: the litter layer is part of that mix (and leads it under the crowns).
            float leaves = weights.Length > TerrainShaper.LeafLitterLayer ? weights[TerrainShaper.LeafLitterLayer] : 0f;
            if (weights.Length > TerrainShaper.MudLayer && weights[TerrainShaper.ForestFloorLayer] + leaves >= 0.3f &&
                weights[TerrainShaper.ForestFloorLayer] + weights[TerrainShaper.MudLayer] + leaves >= 0.6f) return TerrainShaper.ForestFloorLayer;
            int best = 0;
            float bestScore = float.MinValue;
            for (int c = 0; c < weights.Length; c++)
            {
                if (weights[c] <= 0f) continue;
                float score = weights[c] + dither * Hash01(hash + (uint)c * 0x9E3779B9u);
                if (score <= bestScore) continue;
                bestScore = score;
                best = c;
            }

            return best;
        }

        /// <summary>The flat colour (linear) of a zone: its channel's distant colour, the canopy shade, or the water colour.</summary>
        public static Vector3 ZoneColor(int zone, FarMeshSettings s)
        {
            Vector3[] ch = s.ChannelColors;
            if (zone == ZoneWater) return s.StreamColor;
            // Forest floor pass (2026-10-06): the near tiles paint a wood's floor as leaf litter and moss (forest floor, mud and dirt,
            // GroundLook.WoodFloorShares), and that is what an FF-led face is; the canopy zone darkens the same floor.
            if (zone == ZoneCanopy) return Lerp(WoodFloorColor, s.CanopyColor, s.CoverTint);
            if (zone == TerrainShaper.ForestFloorLayer) return WoodFloorColor;
            return zone < ch.Length ? ch[zone] : ch[0];
        }

        /// <summary>The flat colour (linear) of a field look on a distant face: its mix of the channel colours <paramref name="ch"/>.</summary>
        public static Vector3 FieldZoneColor(int look, Vector3[] ch)
        {
            float[] mix = GroundLook.FieldShares[look];
            Vector3 c = Vector3.zero;
            for (int i = 0; i < mix.Length && i < ch.Length; i++) c += ch[i] * mix[i];
            return c;
        }

        /// <summary>Per-face brightness factor: 1 ± <paramref name="jitter"/>, from the face's hash.</summary>
        public static float JitterOf(uint hash, float jitter) => 1f + jitter * (2f * Hash01(hash ^ 0x1B873593u) - 1f);

        /// <summary>Samples the far blueprint's paint, canopy and streams at tile-local points and turns them into face colours.</summary>
        private sealed class GroundSampler
        {
            private readonly TileBlueprint bp;
            private readonly FarMeshSettings s;
            private readonly float[] cover;
            private readonly int coverRes;
            private readonly float coverSpacing;
            private readonly float[] weights;
            private readonly int channels;
            private readonly StreamBox box;
            private readonly float faceSpacing;

            public GroundSampler(TileBlueprint bp, FarMeshSettings s, List<TreePlacement> trees, float faceSpacing)
            {
                this.bp = bp;
                this.s = s;
                this.faceSpacing = faceSpacing;
                channels = bp.Splat != null ? bp.Splat.GetLength(2) : 0;
                weights = new float[Math.Max(1, channels)];
                coverRes = 65;
                coverSpacing = bp.TileSize / (coverRes - 1);
                cover = CoverDensity(trees, coverRes, coverSpacing, 1);
                box = StreamBox.Of(bp.Streams, faceSpacing);
            }

            /// <summary>Zone and colour (sRGB) of the face whose centre is tile-local (x, z); <paramref name="shade"/> darkens (skirts).</summary>
            public Color32 FaceColor(float x, float z, uint hash, out int zone, float shade = 1f)
            {
                float ts = bp.TileSize;
                float u = Clamp01(x / ts), v = Clamp01(z / ts);
                Vector3 mix = GroundColor(bp.Splat, channels, weights, u, v, s.ChannelColors);
                float total = 0f;
                for (int c = 0; c < channels; c++) total += weights[c];
                if (total > 1e-6f)
                {
                    for (int c = 0; c < channels; c++) weights[c] /= total;
                }

                float canopy = s.FullCoverDensity > 0f ? Math.Min(1f, Sample(cover, coverRes, x / coverSpacing, z / coverSpacing) / s.FullCoverDensity) : 0f;
                float wet = 0f;
                if (box.Contains(x, z))
                {
                    float d = StreamDistance(bp.Streams, x, z, out float half);
                    wet = Clamp01((half + 2f + faceSpacing * 0.5f - d) / Math.Max(1f, faceSpacing));
                }

                zone = channels > 0 ? ChooseZone(weights, canopy, wet, hash, s.ZoneDither, s.CanopyThreshold) : 0;
                // The smooth colour this point had before (splat mix, darkened under cover, toward the stream colour), for the pull.
                Vector3 blend = Lerp(mix, s.CanopyColor, Math.Min(1f, canopy) * s.CoverTint);
                if (wet > 0f) blend = Lerp(blend, s.StreamColor, 0.75f * wet);
                // Farmland (2026-10-06): a face inside a field takes the field look's own colour (the near terrain's mix), not its leading
                // channel's, so stubble, ploughed earth and the village's closes read the same at every distance.
                Vector3 zoneColor = ZoneColor(zone, s);
                int look = -1;
                if (zone < TerrainShaper.EndlessLayerCount)
                {
                    look = GroundLook.MatchFieldLook(weights, channels);
                    if (look >= 0) zoneColor = FieldZoneColor(look, s.ChannelColors);
                }

                Vector3 color = Lerp(zoneColor, blend, s.BlendPull) * (JitterOf(hash, s.Jitter) * shade);
                Color32 encoded = EncodeSrgbFast(color);
                // Seasons pass (2026-10-06): the face's season class in its alpha (grass, heath, wood floor, pasture), so the far shader
                // tints it as the near layers are tinted; water, rock, dirt and the crops stay 255 (untouched).
                encoded.a = MedievalZen.Environment.SeasonLook.Alpha(false,
                    zone == ZoneWater ? 0 : MedievalZen.Environment.SeasonLook.GroundClass(zone, look, zone == ZoneCanopy));
                return encoded;
            }
        }

        /// <summary>Trees per 100 m² around each grid point: trees counted to their nearest point, then a box average of (2r + 1)² points.</summary>
        private static float[] CoverDensity(List<TreePlacement> trees, int res, float spacing, int radius)
        {
            var counts = new float[res * res];
            if (trees != null)
            {
                foreach (TreePlacement t in trees)
                {
                    int i = (int)Math.Round(t.position.x / spacing), j = (int)Math.Round(t.position.z / spacing);
                    if (i < 0 || j < 0 || i >= res || j >= res) continue;
                    counts[j * res + i] += 1f;
                }
            }

            int stride = res + 1;
            var table = new float[stride * stride];
            for (int j = 0; j < res; j++)
            {
                float row = 0f;
                for (int i = 0; i < res; i++)
                {
                    row += counts[j * res + i];
                    table[(j + 1) * stride + i + 1] = table[j * stride + i + 1] + row;
                }
            }

            var density = new float[res * res];
            float cellArea = spacing * spacing;
            for (int j = 0; j < res; j++)
            {
                int z0 = Math.Max(0, j - radius), z1 = Math.Min(res - 1, j + radius);
                for (int i = 0; i < res; i++)
                {
                    int x0 = Math.Max(0, i - radius), x1 = Math.Min(res - 1, i + radius);
                    float sum = table[(z1 + 1) * stride + x1 + 1] - table[z0 * stride + x1 + 1] - table[(z1 + 1) * stride + x0] + table[z0 * stride + x0];
                    int n = (x1 - x0 + 1) * (z1 - z0 + 1);
                    density[j * res + i] = sum / (n * cellArea) * 100f;
                }
            }

            return density;
        }

        private static float Sample(float[] grid, int res, float gx, float gz)
        {
            gx = Math.Max(0f, Math.Min(res - 1.001f, gx));
            gz = Math.Max(0f, Math.Min(res - 1.001f, gz));
            int i = (int)gx, j = (int)gz;
            float tx = gx - i, tz = gz - j;
            float a = grid[j * res + i] + (grid[j * res + i + 1] - grid[j * res + i]) * tx;
            float c = grid[(j + 1) * res + i] + (grid[(j + 1) * res + i + 1] - grid[(j + 1) * res + i]) * tx;
            return a + (c - a) * tz;
        }

        /// <summary>
        /// Splat-weighted ground colour (linear) at normalised tile position (u, v), sampling the splat the way the terrain shader
        /// does (texel k sits at k / (res − 1) of the tile), bilinearly; <paramref name="sample"/> receives the channel weights.
        /// </summary>
        public static Vector3 GroundColor(float[,,] splat, int channels, float[] sample, float u, float v, Vector3[] channelColors)
        {
            if (splat == null || channels == 0) return channelColors != null && channelColors.Length > 0 ? channelColors[0] : new Vector3(0.1f, 0.15f, 0.03f);
            int sres = splat.GetLength(0);
            float fx = Clamp01(u) * (sres - 1), fz = Clamp01(v) * (sres - 1);
            int x0 = Math.Min(sres - 1, (int)Math.Floor(fx)), z0 = Math.Min(sres - 1, (int)Math.Floor(fz));
            int x1 = Math.Min(sres - 1, x0 + 1), z1 = Math.Min(sres - 1, z0 + 1);
            float tx = fx - x0, tz = fz - z0;
            float total = 0f;
            for (int c = 0; c < channels; c++)
            {
                float a = splat[z0, x0, c] + (splat[z0, x1, c] - splat[z0, x0, c]) * tx;
                float b = splat[z1, x0, c] + (splat[z1, x1, c] - splat[z1, x0, c]) * tx;
                sample[c] = Math.Max(0f, a + (b - a) * tz);
                total += sample[c];
            }

            if (total <= 1e-6f) return channelColors[0];
            Vector3 color = Vector3.zero;
            for (int c = 0; c < channels; c++)
            {
                Vector3 cc = c < channelColors.Length ? channelColors[c] : channelColors[0];
                color += cc * (sample[c] / total);
            }

            return color;
        }

        /// <summary>Bounds of a tile's stream ribbons grown by their reach (samples outside skip the per-segment search).</summary>
        private readonly struct StreamBox
        {
            private readonly float minX, minZ, maxX, maxZ;
            private readonly bool any;

            private StreamBox(float minX, float minZ, float maxX, float maxZ)
            {
                this.minX = minX;
                this.minZ = minZ;
                this.maxX = maxX;
                this.maxZ = maxZ;
                any = true;
            }

            public bool Contains(float x, float z) => any && x >= minX && x <= maxX && z >= minZ && z <= maxZ;

            public static StreamBox Of(List<StreamRibbon> ribbons, float spacing)
            {
                if (ribbons == null || ribbons.Count == 0) return default;
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue, half = 0f;
                foreach (StreamRibbon r in ribbons)
                {
                    if (r.Vertices == null) continue;
                    foreach (StreamRibbonVertex v in r.Vertices)
                    {
                        minX = Math.Min(minX, v.X);
                        maxX = Math.Max(maxX, v.X);
                        minZ = Math.Min(minZ, v.Z);
                        maxZ = Math.Max(maxZ, v.Z);
                        half = Math.Max(half, v.HalfWidth);
                    }
                }

                if (minX > maxX) return default;
                float reach = half + 2f + spacing * 0.5f + 1f;
                return new StreamBox(minX - reach, minZ - reach, maxX + reach, maxZ + reach);
            }
        }

        /// <summary>Distance from tile-local (x, z) to the nearest ribbon centre line, and the ribbon's half width there.</summary>
        private static float StreamDistance(List<StreamRibbon> ribbons, float x, float z, out float half)
        {
            float best = float.MaxValue;
            half = 0f;
            foreach (StreamRibbon r in ribbons)
            {
                StreamRibbonVertex[] v = r.Vertices;
                if (v == null) continue;
                for (int i = 0; i + 1 < v.Length; i++)
                {
                    float ex = v[i + 1].X - v[i].X, ez = v[i + 1].Z - v[i].Z;
                    float len2 = ex * ex + ez * ez;
                    float t = len2 > 1e-6f ? ((x - v[i].X) * ex + (z - v[i].Z) * ez) / len2 : 0f;
                    t = t < 0f ? 0f : t > 1f ? 1f : t;
                    float dx = x - (v[i].X + ex * t), dz = z - (v[i].Z + ez * t);
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d >= best) continue;
                    best = d;
                    half = v[i].HalfWidth + (v[i + 1].HalfWidth - v[i].HalfWidth) * t;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ trees

        /// <summary>The colour (linear) of one tree's foliage: between its style's two colours by its own amount, ±7 % brightness.</summary>
        public static Vector3 TreeLeafColor(FarTreeStyle style, TreePlacement tree)
        {
            uint h = TreeHash(tree);
            return Lerp(style.Color, style.Color2, Hash01(h)) * (0.93f + 0.14f * Hash01(h ^ 0xA5A5A5A5u));
        }

        private static uint TreeHash(TreePlacement tree) => Hash((long)Math.Round(tree.position.x * 16f) * 73856093L ^ (long)Math.Round(tree.position.z * 16f) * 19349663L);

        private static void AddTree(Builder b, TreePlacement tree, FarTreeStyle style, float drop, bool detailed, float growth)
        {
            float s = tree.scale > 0f ? tree.scale : 1f;
            var basePoint = new Vector3(tree.position.x, tree.position.y - drop - TreeSink, tree.position.z);
            float yaw = tree.yaw * (float)(Math.PI / 180.0);
            uint h = TreeHash(tree);
            Vector3 leaf = TreeLeafColor(style, tree);
            Color32 mid = EncodeSrgbFast(leaf), lit = EncodeSrgbFast(leaf * 1.07f), dark = EncodeSrgbFast(leaf * 0.8f);
            mid.a = lit.a = dark.a = TreeLobes.LeafAlpha(false, style.SeasonClass);   // seasons pass: the leaves' season class
            byte crownAlpha = TreeLobes.LeafAlpha(true, style.SeasonClass);
            if (style.LeafCrown)
            {
                // Distant-tree pass (2026-10-06 evening): a leaf crown's lit-top ladder and foliage light, as the near band's impostors.
                mid = TreeLobes.LeafFace(leaf, TreeLobes.LeafMid, crownAlpha);
                lit = TreeLobes.LeafFace(leaf, TreeLobes.LeafTop, crownAlpha);
                dark = TreeLobes.LeafFace(leaf, TreeLobes.LeafUnder, crownAlpha);
            }

            Color32 bark = EncodeSrgbFast(style.TrunkColor * (0.9f + 0.2f * Hash01(h ^ 0x3C6EF372u)));
            float H = style.Height * s, R = style.Radius * s * growth;
            float cb = Math.Max(0f, style.CrownBase * s), wy = Math.Max(cb, style.WidestAt * s), tr = Math.Max(0.12f, style.TopRadius * s * growth);
            float trunkR = Math.Max(0.12f, style.TrunkRadius * s);

            switch (style.Shape)
            {
                case FarTreeShape.Conifer:
                {
                    if (!detailed)
                    {
                        // A cone on a shallow inverted cone (4 sides): 8 triangles.
                        int apex = b.Add(basePoint + new Vector3(0f, H, 0f), lit);
                        int ring = Ring(b, basePoint, yaw, 4, cb, R, mid, h, 0.1f);
                        int bottom = b.Add(basePoint + new Vector3(0f, cb * 0.75f, 0f), dark);
                        Fan(b, apex, ring, 4, basePoint, true);
                        Fan(b, bottom, ring, 4, basePoint, false);
                        return;
                    }

                    // Lower tier (cone with a shallow cap), upper tier, trunk: 18 triangles.
                    float span = H - cb;
                    int apex1 = b.Add(basePoint + new Vector3(0f, cb + 0.62f * span, 0f), mid);
                    int ring1 = Ring(b, basePoint, yaw, 5, cb + 0.04f * span, R, dark, h, 0.1f);
                    int cap = b.Add(basePoint + new Vector3(0f, cb - 0.06f * span, 0f), dark);
                    int top = b.Add(basePoint + new Vector3(0f, H, 0f), lit);
                    int ring2 = Ring(b, basePoint, yaw + 0.63f, 5, cb + 0.42f * span, 0.62f * R, mid, h ^ 0x51ED27u, 0.1f);
                    Fan(b, apex1, ring1, 5, basePoint, true);
                    Fan(b, cap, ring1, 5, basePoint, false);
                    Fan(b, top, ring2, 5, basePoint, true);
                    Trunk(b, basePoint, yaw, trunkR, cb + 0.2f * span, bark);
                    return;
                }

                case FarTreeShape.Dead:
                {
                    // A bare spike (3 sides); detailed: one branch fin (two faces).
                    int tip = b.Add(basePoint + new Vector3(0f, H, 0f), bark);
                    int ring = Ring(b, basePoint, yaw, 3, 0f, Math.Max(0.2f, trunkR * 1.4f), bark, h, 0f);
                    Fan(b, tip, ring, 3, basePoint, true);
                    if (!detailed) return;
                    var dir = new Vector3((float)Math.Sin(yaw + 1.1f), 0f, (float)Math.Cos(yaw + 1.1f));
                    int f0 = b.Add(basePoint + new Vector3(0f, 0.45f * H, 0f), bark);
                    int f1 = b.Add(basePoint + dir * (0.8f * R) + new Vector3(0f, 0.8f * H, 0f), bark);
                    int f2 = b.Add(basePoint - dir * (0.6f * R) + new Vector3(0f, 0.7f * H, 0f), bark);
                    int f3 = b.Add(basePoint + new Vector3(0f, 0.62f * H, 0f), bark);
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    b.Facing(f0, f1, f2, side);
                    b.Facing(f3, f2, f1, -side);
                    return;
                }

                default:
                {
                    if (!detailed)
                    {
                        // Rounded lobes (2026-10-06): a low dome, not a double pyramid. A flat underside of 4 at 0.9 R halfway up to the
                        // widest ring, a flat cap of 4 at 0.6 R at the top, twisted 45° so the band zig-zags: 12 triangles, 8 vertices,
                        // one colour (light and shade come from the shader's flat normals).
                        // A leaf crown: the low ring between the ladder's mid and shade, the cap ring its lit top; each band triangle
                        // starts and ends on the ring whose colour it takes.
                        float under = 0.5f * (Math.Max(cb * 0.8f, 0.3f * wy) + wy);
                        Color32 lowC = style.LeafCrown ? TreeLobes.LeafFace(leaf, (TreeLobes.LeafMid + TreeLobes.LeafUnder) * 0.5f, crownAlpha) : mid;
                        Color32 highC = style.LeafCrown ? lit : mid;
                        int low = Ring(b, basePoint, yaw, 4, under, 0.9f * R, lowC, h, 0.1f);
                        int high = Ring(b, basePoint, yaw + (float)(Math.PI / 4.0), 4, H, 0.6f * R, highC, h ^ 0x51ED27u, 0.1f);
                        for (int k = 0; k < 4; k++)
                        {
                            int k1 = (k + 1) % 4;
                            RingFace(b, low + k, low + k1, high + k, basePoint);
                            RingFace(b, high + k, high + k1, low + k1, basePoint);
                        }

                        b.Facing(low, low + 1, low + 2, Vector3.down);
                        b.Facing(low, low + 2, low + 3, Vector3.down);
                        b.Facing(high, high + 1, high + 2, Vector3.up);
                        b.Facing(high, high + 2, high + 3, Vector3.up);
                        return;
                    }

                    if (style.HasLobes)
                    {
                        // Lobed crown (impostor pass 2026-10-06): the near impostor band's lobes, simple form (10 per lobe, trunk 3).
                        AddLobes(b, style, basePoint, yaw, s, growth, leaf, style.TrunkColor * (0.9f + 0.2f * Hash01(h ^ 0x3C6EF372u)));
                        return;
                    }

                    // Crown: bottom point, the widest ring, an upper ring (twisted half a side), the top; trunk below: 23 triangles.
                    int bottom2 = b.Add(basePoint + new Vector3(0f, cb, 0f), dark);
                    int ring1 = Ring(b, basePoint, yaw, 5, wy, R, mid, h, 0.14f);
                    int ring2 = Ring(b, basePoint, yaw + 0.628f, 5, cb + 0.8125f * (H - cb), tr, lit, h ^ 0x7F4A7C15u, 0.14f);
                    int top2 = b.Add(basePoint + new Vector3(0f, H, 0f), lit);
                    Fan(b, bottom2, ring1, 5, basePoint, false);
                    for (int k = 0; k < 5; k++)
                    {
                        int r0 = ring1 + k, r1 = ring1 + (k + 1) % 5, u0 = ring2 + k, u1 = ring2 + (k + 1) % 5;
                        b.Outward(r0, r1, u0, basePoint);
                        b.Outward(u0, r1, u1, basePoint);
                    }

                    Fan(b, top2, ring2, 5, basePoint, true);
                    Trunk(b, basePoint, yaw, trunkR, cb + 0.3f * (wy - cb), bark);
                    return;
                }
            }
        }

        /// <summary>
        /// Triangle with edge <paramref name="p"/>–<paramref name="q"/> on one ring and apex <paramref name="apex"/> on the other, facing
        /// away from the vertical axis through <paramref name="axis"/>, written so its first and last index lie on the edge's ring (the
        /// flat colour is the provoking vertex's: first or last by API).
        /// </summary>
        private static void RingFace(Builder b, int p, int q, int apex, Vector3 axis)
        {
            Vector3 pp = b.Data.Vertices[p].position, pq = b.Data.Vertices[q].position, pa = b.Data.Vertices[apex].position;
            Vector3 centroid = (pp + pq + pa) / 3f;
            var outward = new Vector3(centroid.x - axis.x, 0f, centroid.z - axis.z);
            Vector3 n = Vector3.Cross(pa - pp, pq - pp);
            if (Vector3.Dot(n, outward) >= 0f) b.Tri(p, apex, q);
            else b.Tri(q, apex, p);
        }

        [ThreadStatic] private static List<Vector3> lobeVertices;
        [ThreadStatic] private static List<Color32> lobeColors;
        [ThreadStatic] private static List<int> lobeTriangles;

        /// <summary>A lobed crown (<see cref="TreeLobes.Build"/>, simple form) scaled by <paramref name="s"/>, crowns grown by <paramref name="growth"/>, turned by <paramref name="yaw"/>.</summary>
        private static void AddLobes(Builder b, FarTreeStyle style, Vector3 basePoint, float yaw, float s, float growth, Vector3 leaf, Vector3 bark)
        {
            List<Vector3> v = lobeVertices ??= new List<Vector3>();
            List<Color32> c = lobeColors ??= new List<Color32>();
            List<int> t = lobeTriangles ??= new List<int>();
            v.Clear();
            c.Clear();
            t.Clear();
            // Rounded lobes (2026-10-06): four-sided domed lobes, vertices shared within a lobe (one colour per lobe).
            TreeLobes.Build(TreeLobes.MidOf(style), style.ForkY, style.TrunkRadius, leaf, bark, TreeLobes.MidSides, false, v, c, t, style.LeafCrown, style.SeasonClass);
            float cs = (float)Math.Cos(yaw), sn = (float)Math.Sin(yaw);
            int first = b.Vertex;
            for (int i = 0; i < v.Count; i++)
            {
                Vector3 p = v[i];
                float x = p.x * s * growth, z = p.z * s * growth;
                b.Add(basePoint + new Vector3(x * cs + z * sn, p.y * s, -x * sn + z * cs), c[i]);
            }

            for (int i = 0; i < t.Count; i += 3) b.Tri(first + t[i], first + t[i + 1], first + t[i + 2]);
        }

        /// <summary>A ring of <paramref name="sides"/> vertices at <paramref name="y"/> above the base, radius jittered by ±<paramref name="jitter"/>.</summary>
        private static int Ring(Builder b, Vector3 basePoint, float yaw, int sides, float y, float radius, Color32 color, uint hash, float jitter)
        {
            int first = b.Vertex;
            for (int k = 0; k < sides; k++)
            {
                float a = yaw + k * (float)(2.0 * Math.PI) / sides;
                float r = radius * (1f + jitter * (2f * Hash01(hash + (uint)k * 0x632BE5ABu) - 1f));
                b.Add(basePoint + new Vector3((float)Math.Sin(a) * r, y, (float)Math.Cos(a) * r), color);
            }

            return first;
        }

        /// <summary>Triangles from a point to every edge of a ring, facing out (up for a point above, down for one below).</summary>
        private static void Fan(Builder b, int point, int ring, int sides, Vector3 axis, bool above)
        {
            for (int k = 0; k < sides; k++)
            {
                int r0 = ring + k, r1 = ring + (k + 1) % sides;
                // The ring vertex goes first: it carries the face's colour (the point's colour is shared by every face).
                if (above) b.Outward(r0, r1, point, axis);
                else b.Outward(r0, point, r1, axis);
            }
        }

        /// <summary>A three-sided trunk (three triangles from the ground to a point inside the crown).</summary>
        private static void Trunk(Builder b, Vector3 basePoint, float yaw, float radius, float top, Color32 bark)
        {
            int ring = Ring(b, basePoint, yaw + 0.5f, 3, 0f, radius, bark, 0u, 0f);
            int tip = b.Add(basePoint + new Vector3(0f, top, 0f), bark);
            Fan(b, tip, ring, 3, basePoint, true);
        }

        // ------------------------------------------------------------------ rocks and houses

        private static void AddRock(Builder b, GroundSampler g, PropPlacement rock, FarPropStyle style, float drop)
        {
            float s = rock.scale > 0f ? rock.scale : 1f;
            float yaw = rock.yaw * (float)(Math.PI / 180.0);
            float cs = (float)Math.Cos(yaw), sn = (float)Math.Sin(yaw);
            Vector3 c = new Vector3((style.Min.x + style.Max.x) * 0.5f * s, 0f, (style.Min.z + style.Max.z) * 0.5f * s);
            var centre = new Vector3(rock.position.x + c.x * cs + c.z * sn, 0f, rock.position.z - c.x * sn + c.z * cs);
            float rx = Math.Max(0.5f, (style.Max.x - style.Min.x) * 0.5f * s), rz = Math.Max(0.5f, (style.Max.z - style.Min.z) * 0.5f * s);
            float top = rock.position.y + style.Max.y * s - drop;
            float bottom = Math.Min(rock.position.y + style.Min.y * s, rock.position.y) - drop - 0.4f;
            float height = Math.Max(0.8f, top - bottom);
            uint h = Hash((long)Math.Round(rock.position.x * 8f) * 83492791L ^ (long)Math.Round(rock.position.z * 8f) * 2654435761L);
            Vector3 col = style.Wall * (0.9f + 0.16f * Hash01(h));
            Color32 low = EncodeSrgbFast(col * 0.9f), side = EncodeSrgbFast(col), up = EncodeSrgbFast(col * 1.08f);
            var axis = new Vector3(centre.x, bottom, centre.z);

            // Base ring (5), upper ring (5, smaller, twisted), a top point: 15 triangles.
            int ring0 = b.Vertex;
            for (int k = 0; k < 5; k++)
            {
                float a = k * 1.2566371f + 0.3f * Hash01(h + (uint)k);
                float lx = (float)Math.Sin(a) * rx, lz = (float)Math.Cos(a) * rz;
                b.Add(new Vector3(centre.x + lx * cs + lz * sn, bottom, centre.z - lx * sn + lz * cs), low);
            }

            int ring1 = b.Vertex;
            for (int k = 0; k < 5; k++)
            {
                float a = k * 1.2566371f + 0.63f + 0.3f * Hash01(h + 17u + (uint)k);
                float f = 0.55f + 0.2f * Hash01(h + 31u + (uint)k);
                float lx = (float)Math.Sin(a) * rx * f, lz = (float)Math.Cos(a) * rz * f;
                float y = bottom + height * (0.55f + 0.2f * Hash01(h + 47u + (uint)k));
                b.Add(new Vector3(centre.x + lx * cs + lz * sn, y, centre.z - lx * sn + lz * cs), side);
            }

            int apex = b.Add(new Vector3(centre.x + 0.2f * rx * (Hash01(h + 61u) - 0.5f), bottom + height, centre.z + 0.2f * rz * (Hash01(h + 67u) - 0.5f)), up);
            for (int k = 0; k < 5; k++)
            {
                int a0 = ring0 + k, a1 = ring0 + (k + 1) % 5, u0 = ring1 + k, u1 = ring1 + (k + 1) % 5, um = ring1 + (k + 4) % 5;
                b.Outward(u0, a0, a1, axis);
                b.Outward(u0, um, a0, axis);
                b.Outward(u0, u1, apex, axis);
            }
        }

        private static void AddHouse(Builder b, PropPlacement house, FarPropStyle style, float drop)
        {
            if (style.Kind >= FarPropKind.Tower)
            {
                AddLandmark(b, house, style, drop);
                return;
            }

            float s = house.scale > 0f ? house.scale : 1f;
            float yaw = house.yaw * (float)(Math.PI / 180.0);
            float cs = (float)Math.Cos(yaw), sn = (float)Math.Sin(yaw);
            Vector3 p = house.position;
            Vector3 L(float x, float y, float z) => new Vector3(p.x + (x * cs + z * sn) * s, p.y - drop + y * s, p.z + (-x * sn + z * cs) * s);
            Color32 wall = EncodeSrgbFast(style.Wall), roof = EncodeSrgbFast(style.Roof), wallDark = EncodeSrgbFast(style.Wall * 0.85f);
            var axis = new Vector3(p.x, 0f, p.z);
            float bottom = -1.2f;

            if (style.Kind == FarPropKind.Windmill)
            {
                // A square tower to the eaves, a pyramid roof, two crossed sails on the front: 8 + 4 + 8 = 20 triangles.
                float hw = 2.2f, eaves = style.Eaves, top = style.Eaves + 2.6f;
                int w0 = b.Vertex;
                foreach (var (x, z) in new[] { (-hw, -hw), (hw, -hw), (hw, hw), (-hw, hw) })
                {
                    b.Add(L(x, bottom, z), wall);
                    b.Add(L(x, eaves, z), wall);
                }

                Vector3 centre = L(0f, 0f, 0f);
                for (int k = 0; k < 4; k++)
                {
                    int lo0 = w0 + 2 * k, hi0 = lo0 + 1, lo1 = w0 + 2 * ((k + 1) % 4), hi1 = lo1 + 1;
                    b.Outward(lo0, lo1, hi0, centre);
                    b.Outward(hi1, hi0, lo1, centre);
                }

                int peak = b.Add(L(0f, top, 0f), roof);
                for (int k = 0; k < 4; k++) b.Outward(peak, w0 + 2 * k + 1, w0 + 2 * ((k + 1) % 4) + 1, centre);
                float hub = style.Eaves + 1.2f, arm = Math.Max(3f, (style.Max.x - style.Min.x) * 0.5f), front = hw + 0.3f, width = 0.6f;
                foreach (float angle in new[] { 0.785f, 2.356f })
                {
                    float ux = (float)Math.Cos(angle) * arm, uy = (float)Math.Sin(angle) * arm;
                    float wx = -(float)Math.Sin(angle) * width, wy = (float)Math.Cos(angle) * width;
                    int s0 = b.Add(L(-ux - wx, hub - uy - wy, front), wallDark);
                    int s1 = b.Add(L(ux - wx, hub + uy - wy, front), wallDark);
                    int s2 = b.Add(L(-ux + wx, hub - uy + wy, front), wallDark);
                    int s3 = b.Add(L(ux + wx, hub + uy + wy, front), wallDark);
                    Vector3 n = L(0f, 0f, 1f) - L(0f, 0f, 0f);
                    b.Facing(s0, s1, s2, n);
                    b.Facing(s3, s2, s1, n);
                    b.Facing(s0, s2, s1, -n);
                    b.Facing(s3, s1, s2, -n);
                }

                return;
            }

            // A house: four walls to the eaves and a gabled roof along the longer side: 8 + 4 + 2 = 14 triangles.
            float x0 = style.Min.x, x1 = style.Max.x, z0 = style.Min.z, z1 = style.Max.z;
            float eave = style.Eaves, ridge = style.Max.y;
            bool alongZ = (z1 - z0) >= (x1 - x0);
            int v = b.Vertex;
            var corners = new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1) };
            foreach (var (x, z) in corners)
            {
                b.Add(L(x, bottom, z), wall);
                b.Add(L(x, eave, z), wall);
            }

            Vector3 mid = L((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
            for (int k = 0; k < 4; k++)
            {
                int lo0 = v + 2 * k, hi0 = lo0 + 1, lo1 = v + 2 * ((k + 1) % 4), hi1 = lo1 + 1;
                b.Outward(lo0, lo1, hi0, mid);
                b.Outward(hi1, hi0, lo1, mid);
            }

            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            int r0 = b.Add(alongZ ? L(cx, ridge, z0) : L(x0, ridge, cz), roof);
            int r1 = b.Add(alongZ ? L(cx, ridge, z1) : L(x1, ridge, cz), roof);
            int e00 = b.Add(L(x0, eave, z0), roof), e10 = b.Add(L(x1, eave, z0), roof), e11 = b.Add(L(x1, eave, z1), roof), e01 = b.Add(L(x0, eave, z1), roof);
            if (alongZ)
            {
                // Slopes toward −x and +x; gables at z0 and z1 (wall coloured).
                b.Outward(e00, e01, r0, mid);
                b.Outward(r1, r0, e01, mid);
                b.Outward(e10, r0, e11, mid);
                b.Outward(r1, e11, r0, mid);
                b.Outward(v + 1, v + 3, r0, mid);
                b.Outward(v + 5, v + 7, r1, mid);
            }
            else
            {
                b.Outward(e00, r0, e10, mid);
                b.Outward(r1, e10, r0, mid);
                b.Outward(e01, e11, r0, mid);
                b.Outward(r1, r0, e11, mid);
                b.Outward(v + 1, v + 7, r0, mid);
                b.Outward(v + 3, v + 5, r1, mid);
            }
        }

        // ------------------------------------------------------------------ landmarks and field boundaries

        /// <summary>
        /// A landmark's structure (or one standing stone) as a solid whose silhouette, size and colours match the prefab: a tower's
        /// shaft and cap, a chapel's nave, bell tower and spire, a windmill's house, steep roof and sails, a stone's tapering slab, a
        /// ruin's broken walls. Triangle and vertex counts are <see cref="HouseTrianglesOf"/> / <see cref="HouseVerticesOf"/>.
        /// </summary>
        private static void AddLandmark(Builder b, PropPlacement prop, FarPropStyle st, float drop)
        {
            float s = prop.scale > 0f ? prop.scale : 1f;
            float yaw = prop.yaw * (float)(Math.PI / 180.0);
            float cs = (float)Math.Cos(yaw), sn = (float)Math.Sin(yaw);
            Vector3 p = prop.position;
            Vector3 L(float x, float y, float z) => new Vector3(p.x + (x * cs + z * sn) * s, p.y - drop + y * s, p.z + (-x * sn + z * cs) * s);
            Color32 wall = EncodeSrgbFast(st.Wall), roof = EncodeSrgbFast(st.Roof), trim = EncodeSrgbFast(st.Trim);
            const float bottom = -1.2f;
            uint h = Hash((long)Math.Round(p.x * 8f) * 83492791L ^ (long)Math.Round(p.z * 8f) * 2654435761L);
            switch (st.Kind)
            {
                case FarPropKind.Tower:
                    Box(b, L, st.Min.x, st.Max.x, st.Min.z, st.Max.z, bottom, st.FeatureTop, st.FeatureTop, wall, true);
                    Pyramid(b, L, st.Feature.x, st.Feature.z, st.Feature.y, st.FeatureTop, st.Peak, roof);
                    return;
                case FarPropKind.Chapel:
                    Gabled(b, L, st.Min.x, st.Max.x, st.Min.z, st.Max.z, bottom, st.Eaves, st.Ridge, wall, roof);
                    Box(b, L, st.Feature.x - st.Feature.y, st.Feature.x + st.Feature.y, st.Feature.z - st.Feature.y, st.Feature.z + st.Feature.y, st.Eaves - 0.5f, st.FeatureTop, st.FeatureTop, wall, false);
                    Pyramid(b, L, st.Feature.x, st.Feature.z, st.Feature.y * 1.15f, st.FeatureTop, st.Peak, roof);
                    return;
                case FarPropKind.Mill:
                {
                    Box(b, L, st.Min.x, st.Max.x, st.Min.z, st.Max.z, bottom, st.Eaves, st.Eaves, wall, false);
                    // A steep roof with its ridge across the house (along x) over the mill's works: two slopes and two hips.
                    int e0 = b.Add(L(st.Min.x, st.Eaves, st.Min.z), roof), e1 = b.Add(L(st.Max.x, st.Eaves, st.Min.z), roof);
                    int e2 = b.Add(L(st.Max.x, st.Eaves, st.Max.z), roof), e3 = b.Add(L(st.Min.x, st.Eaves, st.Max.z), roof);
                    int r0 = b.Add(L(st.Feature.x - st.Feature.y, st.Ridge, st.Feature.z), roof), r1 = b.Add(L(st.Feature.x + st.Feature.y, st.Ridge, st.Feature.z), roof);
                    Vector3 axis = L(st.Feature.x, 0f, st.Feature.z);
                    b.Outward(e0, e1, r0, axis);
                    b.Outward(r1, r0, e1, axis);
                    b.Outward(e3, e2, r0, axis);
                    b.Outward(r1, r0, e2, axis);
                    b.Outward(e0, r0, e3, axis);
                    b.Outward(e1, r1, e2, axis);
                    // Crossed sails on the front at the hub.
                    Vector3 n = L(0f, 0f, 1f) - L(0f, 0f, 0f);
                    foreach (float angle in new[] { 0.785f, 2.356f })
                    {
                        float ux = (float)Math.Cos(angle) * st.Arm, uy = (float)Math.Sin(angle) * st.Arm;
                        float wx = -(float)Math.Sin(angle) * 0.7f, wy = (float)Math.Cos(angle) * 0.7f;
                        float hz = st.Hub.z + 0.3f;
                        int s0 = b.Add(L(st.Hub.x - ux - wx, st.Hub.y - uy - wy, hz), trim);
                        int s1 = b.Add(L(st.Hub.x + ux - wx, st.Hub.y + uy - wy, hz), trim);
                        int s2 = b.Add(L(st.Hub.x - ux + wx, st.Hub.y - uy + wy, hz), trim);
                        int s3 = b.Add(L(st.Hub.x + ux + wx, st.Hub.y + uy + wy, hz), trim);
                        b.Facing(s0, s1, s2, n);
                        b.Facing(s3, s2, s1, n);
                        b.Facing(s0, s2, s1, -n);
                        b.Facing(s3, s1, s2, -n);
                    }

                    return;
                }

                case FarPropKind.Stone:
                {
                    // A slab tapering to 80 % at the top, leaning as placed (pitch and roll are small: drawn upright).
                    float top = st.Max.y, low = Math.Min(st.Min.y, 0f) - 0.2f;
                    Box(b, L, st.Min.x, st.Max.x, st.Min.z, st.Max.z, low, top, top, wall, true, 0.8f);
                    return;
                }

                default:
                {
                    // Four broken walls: outer and inner faces (0.55 m thick) and a ragged top.
                    float x0 = st.Min.x, x1 = st.Max.x, z0 = st.Min.z, z1 = st.Max.z, t = 0.55f;
                    var tops = new float[4];
                    for (int k = 0; k < 4; k++) tops[k] = st.Max.y * (0.45f + 0.55f * Hash01(h + (uint)k * 0x9E3779B9u));
                    var outer = new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1) };
                    var inner = new[] { (x0 + t, z0 + t), (x1 - t, z0 + t), (x1 - t, z1 - t), (x0 + t, z1 - t) };
                    int o = b.Vertex;
                    for (int k = 0; k < 4; k++)
                    {
                        b.Add(L(outer[k].Item1, bottom, outer[k].Item2), wall);
                        b.Add(L(outer[k].Item1, tops[k], outer[k].Item2), wall);
                    }

                    int i = b.Vertex;
                    for (int k = 0; k < 4; k++)
                    {
                        b.Add(L(inner[k].Item1, bottom, inner[k].Item2), wall);
                        b.Add(L(inner[k].Item1, tops[k] - 0.1f, inner[k].Item2), wall);
                    }

                    Vector3 mid = L((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
                    for (int k = 0; k < 4; k++)
                    {
                        int k1 = (k + 1) % 4;
                        int lo0 = o + 2 * k, hi0 = lo0 + 1, lo1 = o + 2 * k1, hi1 = lo1 + 1;
                        b.Outward(lo0, lo1, hi0, mid);
                        b.Outward(hi1, hi0, lo1, mid);
                        // Inner faces look inward: toward the middle.
                        int il0 = i + 2 * k, ih0 = il0 + 1, il1 = i + 2 * k1, ih1 = il1 + 1;
                        Vector3 inward = mid - (b.Data.Vertices[il0].position + b.Data.Vertices[il1].position) * 0.5f;
                        inward.y = 0f;
                        b.Facing(il0, il1, ih0, inward);
                        b.Facing(ih1, ih0, il1, inward);
                        // The rim between the outer and inner tops faces up.
                        b.Facing(hi0, hi1, ih0, Vector3.up);
                        b.Facing(ih1, ih0, hi1, Vector3.up);
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// A four-sided block from <paramref name="y0"/> to <paramref name="y1"/> (walls: 8 triangles, 8 vertices) with a flat top at
        /// <paramref name="yTop"/> (2 more triangles) when <paramref name="top"/>; the top ring shrinks to <paramref name="taper"/>.
        /// </summary>
        private static void Box(Builder b, Func<float, float, float, Vector3> L, float x0, float x1, float z0, float z1, float y0, float y1, float yTop, Color32 color, bool top, float taper = 1f)
        {
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            int v = b.Vertex;
            foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1) })
            {
                b.Add(L(x, y0, z), color);
                b.Add(L(cx + (x - cx) * taper, y1, cz + (z - cz) * taper), color);
            }

            Vector3 centre = L(cx, 0f, cz);
            for (int k = 0; k < 4; k++)
            {
                int lo0 = v + 2 * k, hi0 = lo0 + 1, lo1 = v + 2 * ((k + 1) % 4), hi1 = lo1 + 1;
                b.Outward(lo0, lo1, hi0, centre);
                b.Outward(hi1, hi0, lo1, centre);
            }

            if (!top) return;
            b.Facing(v + 1, v + 3, v + 5, Vector3.up);
            b.Facing(v + 7, v + 1, v + 5, Vector3.up);
        }

        /// <summary>A square pyramid of half width <paramref name="half"/> centred on (cx, cz) from <paramref name="y0"/> to its tip at <paramref name="y1"/> (4 triangles, 5 vertices).</summary>
        private static void Pyramid(Builder b, Func<float, float, float, Vector3> L, float cx, float cz, float half, float y0, float y1, Color32 color)
        {
            int v = b.Vertex;
            foreach (var (x, z) in new[] { (cx - half, cz - half), (cx + half, cz - half), (cx + half, cz + half), (cx - half, cz + half) }) b.Add(L(x, y0, z), color);
            int apex = b.Add(L(cx, y1, cz), color);
            Vector3 axis = L(cx, 0f, cz);
            for (int k = 0; k < 4; k++) b.Outward(v + k, v + (k + 1) % 4, apex, axis);
        }

        /// <summary>Walls to the eaves and a gabled roof along the longer side (14 triangles, 14 vertices), as <see cref="AddHouse"/>.</summary>
        private static void Gabled(Builder b, Func<float, float, float, Vector3> L, float x0, float x1, float z0, float z1, float bottom, float eave, float ridge, Color32 wall, Color32 roof)
        {
            bool alongZ = (z1 - z0) >= (x1 - x0);
            int v = b.Vertex;
            foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1) })
            {
                b.Add(L(x, bottom, z), wall);
                b.Add(L(x, eave, z), wall);
            }

            Vector3 mid = L((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
            for (int k = 0; k < 4; k++)
            {
                int lo0 = v + 2 * k, hi0 = lo0 + 1, lo1 = v + 2 * ((k + 1) % 4), hi1 = lo1 + 1;
                b.Outward(lo0, lo1, hi0, mid);
                b.Outward(hi1, hi0, lo1, mid);
            }

            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            int r0 = b.Add(alongZ ? L(cx, ridge, z0) : L(x0, ridge, cz), roof);
            int r1 = b.Add(alongZ ? L(cx, ridge, z1) : L(x1, ridge, cz), roof);
            int e00 = b.Add(L(x0, eave, z0), roof), e10 = b.Add(L(x1, eave, z0), roof), e11 = b.Add(L(x1, eave, z1), roof), e01 = b.Add(L(x0, eave, z1), roof);
            if (alongZ)
            {
                b.Outward(e00, e01, r0, mid);
                b.Outward(r1, r0, e01, mid);
                b.Outward(e10, r0, e11, mid);
                b.Outward(r1, e11, r0, mid);
                b.Outward(v + 1, v + 3, r0, mid);
                b.Outward(v + 5, v + 7, r1, mid);
            }
            else
            {
                b.Outward(e00, r0, e10, mid);
                b.Outward(r1, e10, r0, mid);
                b.Outward(e01, e11, r0, mid);
                b.Outward(r1, r0, e11, mid);
                b.Outward(v + 1, v + 7, r0, mid);
                b.Outward(v + 3, v + 5, r1, mid);
            }
        }

        /// <summary>
        /// One stretch of field boundary as a low ridge on the ground (two sloped faces: 4 triangles, 6 vertices): its foot a little under
        /// the (dropped) ground at both ends, its shape and colour by kind (<see cref="EdgeShape"/>, <see cref="EdgeColor"/>).
        /// </summary>
        private static void AddFieldEdge(Builder b, FieldEdge e, float[] heights, int res, float spacing, float drop)
        {
            int kind = Math.Min((int)e.Kind, EdgeShape.Length - 1);
            Vector2 shape = EdgeShape[kind];
            float dx = e.X1 - e.X0, dz = e.Z1 - e.Z0, len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-3f) len = 1e-3f;
            var n = new Vector3(dz / len, 0f, -dx / len) * (shape.x * 0.5f);
            float h0 = GroundAt(heights, res, spacing, e.X0, e.Z0) - drop, h1 = GroundAt(heights, res, spacing, e.X1, e.Z1) - drop;
            uint hash = Hash((long)Math.Round(e.X0 * 4f) * 73856093L ^ (long)Math.Round(e.Z0 * 4f) * 19349663L);
            Color32 color = EncodeSrgbFast(EdgeColor[kind] * (0.94f + 0.12f * Hash01(hash)));
            var a = new Vector3(e.X0, h0, e.Z0);
            var c = new Vector3(e.X1, h1, e.Z1);
            int a0 = b.Add(a + n - new Vector3(0f, 0.3f, 0f), color), a1 = b.Add(a - n - new Vector3(0f, 0.3f, 0f), color), at = b.Add(a + new Vector3(0f, shape.y, 0f), color);
            int c0 = b.Add(c + n - new Vector3(0f, 0.3f, 0f), color), c1 = b.Add(c - n - new Vector3(0f, 0.3f, 0f), color), ct = b.Add(c + new Vector3(0f, shape.y, 0f), color);
            Vector3 up = Vector3.up * 0.5f;
            b.Facing(a0, c0, at, n + up);
            b.Facing(ct, at, c0, n + up);
            b.Facing(a1, at, c1, -n + up);
            b.Facing(ct, c1, at, -n + up);
        }

        /// <summary>Ground height (before the drop) at tile-local (x, z) from the edge-stitched grid, bilinear.</summary>
        private static float GroundAt(float[] h, int res, float spacing, float x, float z)
        {
            float gx = Clamp01(x / (spacing * (res - 1))) * (res - 1), gz = Clamp01(z / (spacing * (res - 1))) * (res - 1);
            int i = Math.Min(res - 2, (int)gx), j = Math.Min(res - 2, (int)gz);
            float tx = gx - i, tz = gz - j;
            float a = h[j * res + i] + (h[j * res + i + 1] - h[j * res + i]) * tx;
            float c = h[(j + 1) * res + i] + (h[(j + 1) * res + i + 1] - h[(j + 1) * res + i]) * tx;
            return a + (c - a) * tz;
        }

        // ------------------------------------------------------------------ writing

        /// <summary>Appends vertices and triangles; every triangle's first index is the vertex whose colour it shows.</summary>
        private sealed class Builder
        {
            public readonly FarTileMeshData Data;
            public int Vertex, Index;

            public Builder(FarTileMeshData data)
            {
                Data = data;
            }

            public int Triangle => Index / 3;

            public int Add(Vector3 position, Color32 color)
            {
                Data.Vertices[Vertex] = new FarVertex { position = position, color = color };
                return Vertex++;
            }

            public void Tri(int a, int b, int c)
            {
                Data.Indices[Index++] = (ushort)a;
                Data.Indices[Index++] = (ushort)b;
                Data.Indices[Index++] = (ushort)c;
            }

            /// <summary>Triangle (a, b, c) with b and c swapped if needed so its front face (cross(b − a, c − a)) points along <paramref name="outward"/>.</summary>
            public void Facing(int a, int b, int c, Vector3 outward)
            {
                Vector3 pa = Data.Vertices[a].position, pb = Data.Vertices[b].position, pc = Data.Vertices[c].position;
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), outward) < 0f) Tri(a, c, b);
                else Tri(a, b, c);
            }

            /// <summary>Triangle (a, b, c) facing away from the vertical axis through <paramref name="axis"/>.</summary>
            public void Outward(int a, int b, int c, Vector3 axis)
            {
                Vector3 centroid = (Data.Vertices[a].position + Data.Vertices[b].position + Data.Vertices[c].position) / 3f;
                Vector3 outward = new Vector3(centroid.x - axis.x, 0f, centroid.z - axis.z);
                Vector3 n = Vector3.Cross(Data.Vertices[b].position - Data.Vertices[a].position, Data.Vertices[c].position - Data.Vertices[a].position);
                // Nearly flat faces (roof caps, fans seen from the axis) face up or down by their own height.
                if (outward.sqrMagnitude < 1e-6f) outward = new Vector3(0f, n.y >= 0f ? 1f : -1f, 0f);
                Facing(a, b, c, outward);
            }
        }

        // ------------------------------------------------------------------ colour and maths

        /// <summary>sRGB 8-bit channel to linear 0..1 (exact curve).</summary>
        public static float SrgbToLinear(byte c)
        {
            double s = c / 255.0;
            return (float)(s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4));
        }

        /// <summary>Linear 0..1 to an sRGB 8-bit channel (exact curve, rounded).</summary>
        public static byte LinearToSrgb(float l)
        {
            double x = Math.Max(0.0, Math.Min(1.0, l));
            double s = x <= 0.0031308 ? x * 12.92 : 1.055 * Math.Pow(x, 1.0 / 2.4) - 0.055;
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(s * 255.0)));
        }

        private static readonly byte[] SrgbTable = BuildSrgbTable();

        private static byte[] BuildSrgbTable()
        {
            var table = new byte[4097];
            for (int i = 0; i < table.Length; i++) table[i] = LinearToSrgb(i / 4096f);
            return table;
        }

        /// <summary>Linear RGB to sRGB bytes through a 4096-step table (within one level of <see cref="EncodeSrgb"/>; alpha 255).</summary>
        public static Color32 EncodeSrgbFast(Vector3 linear)
        {
            int r = (int)(Clamp01(linear.x) * 4096f + 0.5f), g = (int)(Clamp01(linear.y) * 4096f + 0.5f), b = (int)(Clamp01(linear.z) * 4096f + 0.5f);
            return new Color32(SrgbTable[r], SrgbTable[g], SrgbTable[b], 255);
        }

        /// <summary>Linear RGB to an sRGB-encoded vertex colour (alpha 255).</summary>
        public static Color32 EncodeSrgb(Vector3 linear) => new Color32(LinearToSrgb(linear.x), LinearToSrgb(linear.y), LinearToSrgb(linear.z), 255);

        /// <summary>sRGB-encoded vertex colour to linear RGB.</summary>
        public static Vector3 DecodeSrgb(Color32 c) => new Vector3(SrgbToLinear(c.r), SrgbToLinear(c.g), SrgbToLinear(c.b));

        private static uint Hash(long n)
        {
            unchecked
            {
                ulong x = (ulong)n * 0x9E3779B97F4A7C15UL;
                x ^= x >> 31;
                x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 29;
                return (uint)(x ^ (x >> 32));
            }
        }

        /// <summary>A value in [0, 1) from a hash (deterministic on every platform).</summary>
        public static float Hash01(uint n)
        {
            unchecked
            {
                uint x = n * 0x9E3779B1u;
                x ^= x >> 15;
                x *= 0x85EBCA77u;
                x ^= x >> 13;
                return (x & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
