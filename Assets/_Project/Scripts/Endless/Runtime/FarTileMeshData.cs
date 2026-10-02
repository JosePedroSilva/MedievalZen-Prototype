using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// One vertex of a far tile's mesh, laid out exactly as the GPU vertex buffer (28 bytes): position (float3), normal
    /// (float3), colour (UNorm8 x4, sRGB-encoded; the FarTerrain shader decodes it to linear).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FarVertex
    {
        /// <summary>Tile-local position (x, z in 0..tile size from the tile's minimum corner; y in absolute metres, drop included).</summary>
        public Vector3 position;

        /// <summary>Unit normal.</summary>
        public Vector3 normal;

        /// <summary>Albedo, sRGB-encoded.</summary>
        public Color32 color;
    }

    /// <summary>The low-poly solid a distant tree is drawn as.</summary>
    public enum FarTreeShape
    {
        /// <summary>A squat double pyramid (5 sides, 10 triangles): broadleaf trees.</summary>
        Broadleaf = 0,

        /// <summary>A cone (6 sides, 6 triangles): pines.</summary>
        Conifer = 1,

        /// <summary>A thin cone (4 sides, 4 triangles): dead trees.</summary>
        Dead = 2,
    }

    /// <summary>How one tree prefab is drawn far away: shape, size at scale 1, and average foliage colour (linear).</summary>
    public struct FarTreeStyle
    {
        /// <summary>Impostor shape.</summary>
        public FarTreeShape Shape;

        /// <summary>Height of the prefab above its pivot at scale 1, metres.</summary>
        public float Height;

        /// <summary>Crown radius at scale 1, metres.</summary>
        public float Radius;

        /// <summary>Average foliage colour, linear RGB.</summary>
        public Vector3 Color;

        /// <summary>Triangles one impostor of this shape costs.</summary>
        public int Triangles => FarTileMeshData.TrianglesOf(Shape);
    }

    /// <summary>
    /// Far-tree styles per tree family and variant (resolved like the palette: variant wrapped). Built on the main thread
    /// from the palette's prefabs (<see cref="ForPrefab"/>), read-only afterwards (safe to share with worker threads).
    /// Families or variants it does not know get <see cref="DefaultFor"/>.
    /// </summary>
    public sealed class FarTreeStyles
    {
        private readonly Dictionary<string, FarTreeStyle[]> styles = new Dictionary<string, FarTreeStyle[]>();

        /// <summary>Styles of every tree family by role defaults only (no palette).</summary>
        public static readonly FarTreeStyles Defaults = new FarTreeStyles();

        /// <summary>Sets the styles of a family (variants in palette order).</summary>
        public void Set(string role, FarTreeStyle[] variants)
        {
            if (string.IsNullOrEmpty(role) || variants == null || variants.Length == 0) return;
            styles[role] = variants;
        }

        /// <summary>The style for a placement's family and variant.</summary>
        public FarTreeStyle Resolve(string role, int variant)
        {
            if (role != null && styles.TryGetValue(role, out FarTreeStyle[] list) && list.Length > 0)
            {
                return list[((variant % list.Length) + list.Length) % list.Length];
            }

            return DefaultFor(role);
        }

        /// <summary>A sensible style for a family when the palette is not known (pines tall cones, dead trees thin, the rest broadleaf).</summary>
        public static FarTreeStyle DefaultFor(string role)
        {
            if (role == PropRole.PineTree) return ForPrefab("Pine", 14f, 3.2f);
            if (role == PropRole.DeadTree) return ForPrefab("Dead", 9f, 3f);
            if (role == PropRole.FruitTree) return ForPrefab("Fruit", 6f, 3f);
            return ForPrefab(role == PropRole.MeadowTree ? "Meadow" : "Tree", 11f, 4.5f);
        }

        /// <summary>
        /// The style of a tree prefab from its name and measured size (height above the pivot, largest horizontal half-extent).
        /// Colours (sRGB, converted to linear here): the PNB Meadow trees from their own LOD card textures, averaged offline over
        /// opaque texels (Meadow 62,65,31 nudged greener; Birch 121,134,101 and Fruit 111,125,85 darkened toward their leaf
        /// tints); the PolygonGeneric trees use an atlas, so their colours are chosen constants (broadleaf 70,96,40; pine
        /// 40,62,40; dead 92,80,66).
        /// </summary>
        public static FarTreeStyle ForPrefab(string name, float height, float halfExtent)
        {
            name = name ?? string.Empty;
            FarTreeShape shape = Contains(name, "Pine") ? FarTreeShape.Conifer : Contains(name, "Dead") ? FarTreeShape.Dead : FarTreeShape.Broadleaf;
            Color32 srgb;
            if (shape == FarTreeShape.Conifer) srgb = new Color32(40, 62, 40, 255);
            else if (shape == FarTreeShape.Dead) srgb = new Color32(92, 80, 66, 255);
            else if (Contains(name, "Birch")) srgb = new Color32(104, 112, 52, 255);
            else if (Contains(name, "Fruit")) srgb = new Color32(82, 104, 48, 255);
            else if (Contains(name, "Meadow")) srgb = new Color32(62, 72, 30, 255);
            else srgb = new Color32(70, 96, 40, 255);

            return new FarTreeStyle
            {
                Shape = shape,
                Height = Math.Max(1f, height),
                Radius = Math.Max(0.5f, halfExtent * (shape == FarTreeShape.Dead ? 0.5f : 0.85f)),
                Color = new Vector3(FarTileMeshData.SrgbToLinear(srgb.r), FarTileMeshData.SrgbToLinear(srgb.g), FarTileMeshData.SrgbToLinear(srgb.b)),
            };
        }

        private static bool Contains(string s, string part) => s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Inputs of <see cref="FarTileMeshData.Build"/> besides the blueprint.</summary>
    public sealed class FarMeshSettings
    {
        /// <summary>Most triangles one far tile may have (ground + skirt + trees); trees are thinned deterministically to fit.</summary>
        public int MaxTriangles = FarTileMeshData.DefaultMaxTriangles;

        /// <summary>How far the far ground sits below the true ground (its coarse edges stay under the near tiles').</summary>
        public float Drop = FarTileMeshData.DefaultDrop;

        /// <summary>Depth of the downward skirt around the tile's edge, metres.</summary>
        public float SkirtDepth = 6f;

        /// <summary>Build tree impostors (off: ground only, the ground is still tinted by tree cover).</summary>
        public bool IncludeTrees = true;

        /// <summary>Trees per 100 m² at which the ground takes the full canopy tint.</summary>
        public float FullCoverDensity = 1.5f;

        /// <summary>How much of the canopy colour the ground takes under full cover (0..1).</summary>
        public float CoverTint = 0.45f;

        /// <summary>Average ground colour per splat channel (linear RGB), in <see cref="TerrainShaper"/> channel order.</summary>
        public Vector3[] ChannelColors = FarTileMeshData.MeasuredChannelColors;

        /// <summary>Canopy colour the ground leans toward under tree cover (linear RGB).</summary>
        public Vector3 CanopyColor = new Vector3(0.040f, 0.062f, 0.018f);
    }

    /// <summary>
    /// A far tile as plain mesh data, built on a worker thread from its <see cref="TileBlueprint"/> (pure, no Unity objects):
    /// the height grid as triangles with vertex colours from the splat (per-channel average ground colours, darkened toward
    /// the canopy colour under tree cover), lowered by <see cref="FarMeshSettings.Drop"/>, with a downward skirt on all four
    /// edges, plus one low-poly solid per far tree, all in one 16-bit-indexed triangle list. The main thread only uploads it.
    /// </summary>
    public sealed class FarTileMeshData
    {
        /// <summary>Default triangle cap per far tile.</summary>
        public const int DefaultMaxTriangles = 8192;

        /// <summary>Default drop of the far ground (matches the old far terrains).</summary>
        public const float DefaultDrop = 1.5f;

        /// <summary>Trees are sunk this far into the ground so their base never floats on a slope.</summary>
        public const float TreeSink = 0.3f;

        /// <summary>
        /// Average colour of each terrain layer's diffuse texture (linear RGB, mean of the sRGB-decoded texels of the full
        /// 2048² PNG, measured offline on 2026-10-02), in channel order: lush grass (Grass_Texture_01), dry grass
        /// (Grass_Texture_02), dirt (Dirt_Texture_01), rock (RockWall_Texture_01), forest floor (Grass_Leaves_Texture_01),
        /// flower meadow (Grass_Flowers_Texture_01), mud (Mud_Texture_01), cobblestone (Cobblestone_Texture_01).
        /// </summary>
        public static readonly Vector3[] MeasuredChannelColors =
        {
            new Vector3(0.1357f, 0.1851f, 0.0176f),
            new Vector3(0.0722f, 0.0971f, 0.0221f),
            new Vector3(0.3208f, 0.2368f, 0.1619f),
            new Vector3(0.2973f, 0.2167f, 0.1472f),
            new Vector3(0.1378f, 0.1746f, 0.0180f),
            new Vector3(0.2152f, 0.1977f, 0.0153f),
            new Vector3(0.0560f, 0.0399f, 0.0255f),
            new Vector3(0.2319f, 0.1990f, 0.1548f),
        };

        /// <summary>Vertices (exactly <see cref="VertexCount"/> long).</summary>
        public FarVertex[] Vertices;

        /// <summary>Triangle list indices (exactly <see cref="IndexCount"/> long).</summary>
        public ushort[] Indices;

        /// <summary>Number of vertices.</summary>
        public int VertexCount;

        /// <summary>Number of indices (3 per triangle).</summary>
        public int IndexCount;

        /// <summary>Triangles of the ground grid.</summary>
        public int GroundTriangles;

        /// <summary>Triangles of the edge skirt.</summary>
        public int SkirtTriangles;

        /// <summary>Triangles of the tree impostors.</summary>
        public int TreeTriangles;

        /// <summary>Trees drawn.</summary>
        public int TreesDrawn;

        /// <summary>Trees dropped to keep within the triangle cap.</summary>
        public int TreesThinned;

        /// <summary>Tile-local bounds of every vertex.</summary>
        public Vector3 BoundsMin, BoundsMax;

        /// <summary>Total triangles.</summary>
        public int Triangles => IndexCount / 3;

        /// <summary>Triangles one impostor of <paramref name="shape"/> costs.</summary>
        public static int TrianglesOf(FarTreeShape shape) => shape == FarTreeShape.Conifer ? 6 : shape == FarTreeShape.Dead ? 4 : 10;

        private static int VerticesOf(FarTreeShape shape) => shape == FarTreeShape.Conifer ? 7 : shape == FarTreeShape.Dead ? 5 : 7;

        /// <summary>Ground + skirt triangles of a tile with <paramref name="res"/> height samples per edge.</summary>
        public static int GroundAndSkirtTriangles(int res) => 2 * (res - 1) * (res - 1) + 4 * 2 * (res - 1);

        /// <summary>
        /// Builds the mesh data of a far tile. <paramref name="outsideHeight"/> (absolute x, z → metres, may be null) gives the
        /// ground just beyond the tile's edge so edge normals are central differences, the same on both sides of a seam;
        /// without it the edges use one-sided differences.
        /// </summary>
        public static FarTileMeshData Build(TileBlueprint bp, FarTreeStyles styles, FarMeshSettings settings, Func<double, double, float> outsideHeight = null)
        {
            if (bp == null) throw new ArgumentNullException(nameof(bp));
            if (bp.Heights == null) throw new ArgumentException("The blueprint has no heights.", nameof(bp));
            settings = settings ?? new FarMeshSettings();
            styles = styles ?? FarTreeStyles.Defaults;

            int res = bp.Heights.GetLength(0);
            if (res < 2 || bp.Heights.GetLength(1) != res) throw new ArgumentException("Heights must be square with at least 2 samples per edge.", nameof(bp));
            float ts = bp.TileSize;
            float range = bp.HeightRange;
            float spacing = ts / (res - 1);
            float drop = settings.Drop;

            // ---- tree selection (deterministic thinning to the cap)
            int groundTris = GroundAndSkirtTriangles(res);
            int treeBudget = Math.Max(0, settings.MaxTriangles - groundTris);
            List<TreePlacement> trees = bp.Trees ?? new List<TreePlacement>();
            int wantedTris = 0, wantedVerts = 0;
            if (settings.IncludeTrees)
            {
                foreach (TreePlacement t in trees) wantedTris += styles.Resolve(t.role, t.variant).Triangles;
            }

            double keepRatio = wantedTris <= treeBudget || wantedTris == 0 ? 1.0 : (double)treeBudget / wantedTris;
            var keep = new bool[trees.Count];
            int keptTris = 0, kept = 0, thinned = 0;
            if (settings.IncludeTrees)
            {
                for (int i = 0; i < trees.Count; i++)
                {
                    FarTreeStyle style = styles.Resolve(trees[i].role, trees[i].variant);
                    bool take = keepRatio >= 1.0 || Math.Floor((i + 1) * keepRatio) > Math.Floor(i * keepRatio);
                    if (take && keptTris + style.Triangles <= treeBudget)
                    {
                        keep[i] = true;
                        keptTris += style.Triangles;
                        wantedVerts += VerticesOf(style.Shape);
                        kept++;
                    }
                    else
                    {
                        thinned++;
                    }
                }
            }

            int groundVerts = res * res;
            int skirtVerts = 4 * res;
            int vertexCount = groundVerts + skirtVerts + wantedVerts;
            if (vertexCount > ushort.MaxValue) throw new InvalidOperationException($"Far tile mesh would need {vertexCount} vertices (16-bit indices); lower the triangle cap.");

            var data = new FarTileMeshData
            {
                Vertices = new FarVertex[vertexCount],
                Indices = new ushort[(groundTris + keptTris) * 3],
                TreesDrawn = kept,
                TreesThinned = thinned,
            };

            // ---- ground: heights, normals, colours
            var heights = new float[res * res];
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++) heights[j * res + i] = bp.Heights[j, i] * range;
            }

            float[] cover = CoverDensity(trees, res, spacing);
            double ox = bp.Coord.OriginX(ts), oz = bp.Coord.OriginZ(ts);
            int channels = bp.Splat != null ? bp.Splat.GetLength(2) : 0;
            var sample = new float[Math.Max(1, channels)];
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float h = heights[j * res + i];
                    float hl = i > 0 ? heights[j * res + i - 1] : Outside(outsideHeight, ox + (i - 1) * spacing, oz + j * spacing, float.NaN);
                    float hr = i < res - 1 ? heights[j * res + i + 1] : Outside(outsideHeight, ox + (i + 1) * spacing, oz + j * spacing, float.NaN);
                    float hd = j > 0 ? heights[(j - 1) * res + i] : Outside(outsideHeight, ox + i * spacing, oz + (j - 1) * spacing, float.NaN);
                    float hu = j < res - 1 ? heights[(j + 1) * res + i] : Outside(outsideHeight, ox + i * spacing, oz + (j + 1) * spacing, float.NaN);
                    float dx = Slope(hl, h, hr, spacing);
                    float dz = Slope(hd, h, hu, spacing);
                    Vector3 n = Normalize(new Vector3(-dx, 1f, -dz));

                    Vector3 albedo = GroundColor(bp.Splat, channels, sample, i * spacing / ts, j * spacing / ts, settings.ChannelColors);
                    float c = settings.FullCoverDensity > 0f ? Math.Min(1f, cover[j * res + i] / settings.FullCoverDensity) : 0f;
                    albedo = Lerp(albedo, settings.CanopyColor, c * settings.CoverTint);

                    data.Vertices[j * res + i] = new FarVertex
                    {
                        position = new Vector3(i * spacing, h - drop, j * spacing),
                        normal = n,
                        color = EncodeSrgb(albedo),
                    };
                }
            }

            int index = 0;
            for (int j = 0; j < res - 1; j++)
            {
                for (int i = 0; i < res - 1; i++)
                {
                    int a = j * res + i, b = a + 1, c = a + res, d = c + 1;
                    // Clockwise seen from above (Unity's front face): normal = cross(c - a, b - a) points up.
                    data.Indices[index++] = (ushort)a;
                    data.Indices[index++] = (ushort)c;
                    data.Indices[index++] = (ushort)b;
                    data.Indices[index++] = (ushort)b;
                    data.Indices[index++] = (ushort)c;
                    data.Indices[index++] = (ushort)d;
                }
            }

            data.GroundTriangles = index / 3;

            // ---- skirt: a copy of every edge vertex lowered by SkirtDepth, quads facing outward
            int v = groundVerts;
            for (int side = 0; side < 4; side++)
            {
                int start = v;
                for (int k = 0; k < res; k++)
                {
                    FarVertex top = data.Vertices[EdgeVertex(side, k, res)];
                    top.position.y -= settings.SkirtDepth;
                    data.Vertices[v++] = top;
                }

                Vector3 outward = side == 0 ? new Vector3(0f, 0f, -1f) : side == 1 ? new Vector3(1f, 0f, 0f) : side == 2 ? new Vector3(0f, 0f, 1f) : new Vector3(-1f, 0f, 0f);
                for (int k = 0; k < res - 1; k++)
                {
                    int e0 = EdgeVertex(side, k, res), e1 = EdgeVertex(side, k + 1, res);
                    int s0 = start + k, s1 = start + k + 1;
                    AddFacing(data, ref index, e0, e1, s0, outward);
                    AddFacing(data, ref index, s0, e1, s1, outward);
                }
            }

            data.SkirtTriangles = index / 3 - data.GroundTriangles;

            // ---- trees
            for (int t = 0; t < trees.Count; t++)
            {
                if (!keep[t]) continue;
                TreePlacement tree = trees[t];
                FarTreeStyle style = styles.Resolve(tree.role, tree.variant);
                AddTree(data, ref v, ref index, tree, style, drop, t);
            }

            data.TreeTriangles = index / 3 - data.GroundTriangles - data.SkirtTriangles;
            data.VertexCount = v;
            data.IndexCount = index;
            if (v != vertexCount || index != data.Indices.Length) throw new InvalidOperationException("Far tile mesh: vertex or index count mismatch.");

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < v; i++)
            {
                Vector3 p = data.Vertices[i].position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            data.BoundsMin = min;
            data.BoundsMax = max;
            return data;
        }

        // ------------------------------------------------------------------ ground helpers

        private static float Outside(Func<double, double, float> sampler, double x, double z, float fallback)
        {
            if (sampler == null) return fallback;
            float h = sampler(x, z);
            return float.IsNaN(h) || float.IsInfinity(h) ? fallback : h;
        }

        /// <summary>Central difference where both neighbours are known, else one-sided.</summary>
        private static float Slope(float before, float here, float after, float spacing)
        {
            bool hasBefore = !float.IsNaN(before), hasAfter = !float.IsNaN(after);
            if (hasBefore && hasAfter) return (after - before) / (2f * spacing);
            if (hasAfter) return (after - here) / spacing;
            if (hasBefore) return (here - before) / spacing;
            return 0f;
        }

        /// <summary>
        /// Trees per 100 m² around each grid vertex: trees counted to their nearest vertex, then a 3 x 3 box average.
        /// </summary>
        private static float[] CoverDensity(List<TreePlacement> trees, int res, float spacing)
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

            var density = new float[res * res];
            float cellArea = spacing * spacing;
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        for (int di = -1; di <= 1; di++)
                        {
                            int x = i + di, z = j + dj;
                            if (x < 0 || z < 0 || x >= res || z >= res) continue;
                            sum += counts[z * res + x];
                            n++;
                        }
                    }

                    density[j * res + i] = sum / (n * cellArea) * 100f;
                }
            }

            return density;
        }

        /// <summary>
        /// Splat-weighted ground colour (linear) at normalised tile position (u, v), sampling the splat the way the terrain
        /// shader does (texel k sits at k / (res − 1) of the tile), bilinearly; channel weights are renormalised.
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

        /// <summary>Index of the k-th vertex along an edge: 0 = z min (x rising), 1 = x max (z rising), 2 = z max (x rising), 3 = x min (z rising).</summary>
        private static int EdgeVertex(int side, int k, int res)
        {
            switch (side)
            {
                case 0: return k;
                case 1: return k * res + (res - 1);
                case 2: return (res - 1) * res + k;
                default: return k * res;
            }
        }

        // ------------------------------------------------------------------ trees

        private static void AddTree(FarTileMeshData data, ref int v, ref int index, TreePlacement tree, FarTreeStyle style, float drop, int ordinal)
        {
            float s = tree.scale > 0f ? tree.scale : 1f;
            float h = style.Height * s;
            float r = style.Radius * s;
            var basePoint = new Vector3(tree.position.x, tree.position.y - drop - TreeSink, tree.position.z);
            float yaw = tree.yaw * (float)(Math.PI / 180.0);

            // Small deterministic brightness variation per tree, lighter crown top, darker bottom.
            float vary = 0.92f + 0.16f * Hash01(ordinal * 7919 + (int)(tree.position.x * 13f) + (int)(tree.position.z * 31f));
            Vector3 body = style.Color * vary;
            Color32 mid = EncodeSrgb(body);
            Color32 top = EncodeSrgb(body * 1.18f);
            Color32 low = EncodeSrgb(body * 0.7f);

            int sides;
            float ringY, ringR, topY, bottomY;
            bool bottomApex;
            switch (style.Shape)
            {
                case FarTreeShape.Conifer:
                    sides = 6;
                    ringY = 0.12f * h;
                    ringR = r;
                    topY = h;
                    bottomY = 0f;
                    bottomApex = false;
                    break;
                case FarTreeShape.Dead:
                    sides = 4;
                    ringY = 0f;
                    ringR = Math.Max(0.4f, r * 0.45f);
                    topY = 0.9f * h;
                    bottomY = 0f;
                    bottomApex = false;
                    break;
                default:
                    sides = 5;
                    ringY = 0.55f * h;
                    ringR = r;
                    topY = h;
                    bottomY = 0f;
                    bottomApex = true;
                    break;
            }

            int apex = v;
            data.Vertices[v++] = new FarVertex { position = basePoint + new Vector3(0f, topY, 0f), normal = new Vector3(0f, 1f, 0f), color = top };
            int ring = v;
            for (int k = 0; k < sides; k++)
            {
                float a = yaw + k * (float)(2.0 * Math.PI) / sides;
                var radial = new Vector3((float)Math.Sin(a), 0f, (float)Math.Cos(a));
                Vector3 n = Normalize(radial + new Vector3(0f, bottomApex ? 0.15f : 0.45f, 0f));
                data.Vertices[v++] = new FarVertex { position = basePoint + radial * ringR + new Vector3(0f, ringY, 0f), normal = n, color = style.Shape == FarTreeShape.Broadleaf ? mid : low };
            }

            int bottom = -1;
            if (bottomApex)
            {
                bottom = v;
                data.Vertices[v++] = new FarVertex { position = basePoint + new Vector3(0f, bottomY, 0f), normal = new Vector3(0f, -1f, 0f), color = low };
            }

            Vector3 axis = basePoint;
            for (int k = 0; k < sides; k++)
            {
                int r0 = ring + k, r1 = ring + (k + 1) % sides;
                AddFacingTree(data, ref index, apex, r0, r1, axis);
                if (bottom >= 0) AddFacingTree(data, ref index, bottom, r1, r0, axis);
            }
        }

        /// <summary>Adds triangle (a, b, c) wound so its front face looks away from the vertical axis through <paramref name="axis"/>.</summary>
        private static void AddFacingTree(FarTileMeshData data, ref int index, int a, int b, int c, Vector3 axis)
        {
            Vector3 pa = data.Vertices[a].position, pb = data.Vertices[b].position, pc = data.Vertices[c].position;
            Vector3 centroid = (pa + pb + pc) / 3f;
            var outward = new Vector3(centroid.x - axis.x, 0f, centroid.z - axis.z);
            AddFacing(data, ref index, a, b, c, outward);
        }

        /// <summary>Adds triangle (a, b, c), swapping b and c if needed so its front-face normal (cross(b − a, c − a)) points along <paramref name="outward"/>.</summary>
        private static void AddFacing(FarTileMeshData data, ref int index, int a, int b, int c, Vector3 outward)
        {
            Vector3 pa = data.Vertices[a].position, pb = data.Vertices[b].position, pc = data.Vertices[c].position;
            Vector3 n = Vector3.Cross(pb - pa, pc - pa);
            if (Vector3.Dot(n, outward) < 0f)
            {
                int tmp = b;
                b = c;
                c = tmp;
            }

            data.Indices[index++] = (ushort)a;
            data.Indices[index++] = (ushort)b;
            data.Indices[index++] = (ushort)c;
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

        /// <summary>Linear RGB to an sRGB-encoded vertex colour (alpha 255).</summary>
        public static Color32 EncodeSrgb(Vector3 linear) => new Color32(LinearToSrgb(linear.x), LinearToSrgb(linear.y), LinearToSrgb(linear.z), 255);

        private static float Hash01(int n)
        {
            unchecked
            {
                uint x = (uint)n * 0x9E3779B1u;
                x ^= x >> 15;
                x *= 0x85EBCA77u;
                x ^= x >> 13;
                return (x & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        private static Vector3 Normalize(Vector3 v)
        {
            float m = (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return m > 1e-12f ? v / m : new Vector3(0f, 1f, 0f);
        }
    }
}
