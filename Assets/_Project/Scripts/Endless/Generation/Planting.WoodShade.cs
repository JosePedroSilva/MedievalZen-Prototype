using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// The floor of the woods (forest floor pass, 2026-10-06; endless only). Under the canopy the ground is not a meadow: the paint
    /// turns to leaf litter and moss (<see cref="GroundLook.WoodFloorShares"/>) with bare earth round the trunks, the sward thins to
    /// short clumps and ground cover, and the understorey grows in clusters and drifts of ferns and undergrowth, densest at the glade
    /// edges and along the road's margin, thinner under the densest canopy, with fallen logs ringed by ferns and mushrooms. The glades
    /// and the wood edges keep the bright grass and flowers so the contrast reads. The shade measure (<see cref="WoodShade(GenWindow, IReadOnlyList{Glade}, float, float)"/>) is a
    /// function of the global land cover, the road and the glades, so near and distant tiles paint the same floor. Randomness
    /// streams 1361-1366. Pure and deterministic like the rest of <see cref="Planting"/>. Canopy floor pass (2026-10-06, afternoon): under
    /// each crown (<see cref="CanopyMap"/>) the floor is leaf litter (<see cref="GroundLook.LeafLitterShares"/>) and the grass almost gone,
    /// across the road's clearing the floor follows the wood beyond it (<see cref="RoadsideReach"/>), distant tiles use the expected canopy.
    /// </summary>
    public sealed partial class Planting
    {
        /// <summary>Randomness streams (EndlessNoise) of the wood floor: 1361-1366 (1367-1380 free).</summary>
        private const int StreamLitter = 1361, StreamCluster = 1362, StreamClusterPick = 1363, StreamShadeDetail = 1364, StreamWoodLog = 1365,
            StreamWoodMushroom = 1366;

        /// <summary>Land-cover forest weight where the shade of a wood starts, and where it is full.</summary>
        public const float ShadeStart = 0.4f, ShadeFull = 0.75f;

        /// <summary>Largest share of the grass paint the wood floor mix replaces (full shade).</summary>
        public const float MaxFloorPaint = 0.9f;

        /// <summary>Largest share of the grass paint the leaf litter replaces under a full crown.</summary>
        public const float CrownFloorPaint = 0.97f;

        /// <summary>Shade kept on the road's margin (within 3.5 m of the centre), rising to full by 8.5 m (was 4-10 m): the road edge stays lighter.</summary>
        public const float VergeShade = 0.45f;

        /// <summary>The shade is 0 from 1 m inside a glade's rim and full this many metres outside it.</summary>
        public const float GladeShadeRamp = 6f;

        /// <summary>Bare earth round a trunk: radius (metres at scale 1) and the share of the paint it takes at the trunk.</summary>
        public const float BareEarthRadius = 2.2f, BareEarthShare = 0.55f;

        /// <summary>The bare earth round a trunk: mud and leaf-litter shares (dirt takes the rest).</summary>
        public const float BareMud = 0.55f, BareLeaves = 0.25f;

        /// <summary>Edge of the cells understorey clusters are planned on, metres.</summary>
        public const double ClusterCell = 6.0;

        /// <summary>Plants per understorey cluster (a drift of ferns, a clump of undergrowth or both).</summary>
        public const int ClusterMin = 5, ClusterMax = 15;

        /// <summary>
        /// Understorey plants keep this far from the centre of any small occupied disc (a trunk's 3 m footprint, a bush, a rock):
        /// they may stand inside a crown's footprint, never on a trunk.
        /// </summary>
        public const float FloorTrunkKeep = 1.1f;

        /// <summary>Occupied discs up to this radius are trunks, bushes and small props (their keep-out shrinks to <see cref="FloorTrunkKeep"/>).</summary>
        public const float FloorSmallDisc = 3.6f;

        /// <summary>Edge of the cells fallen logs are planned on inside the wood, and how far from the road they lie at most.</summary>
        public const double WoodLogCell = 30.0, WoodLogReach = 70.0;

        /// <summary>Understorey plants keep apart by this share of their reach (each disc), so a cluster spreads instead of piling up.</summary>
        public const float FloorSpacing = 0.85f;

        /// <summary>
        /// Undergrowth away from the road: the Generic bushes 1-4 (266-400 triangles) and the large bushes 2 and 4 (696 and 348); not
        /// the large bushes 1 and 3 (1,190 and 1,058), which cover no more ground.
        /// </summary>
        private static readonly int[] LightUndergrowth = { 0, 1, 2, 3, 5, 7 };

        private GradientNoise litterNoise;

        private void InitWoodFloor() => litterNoise = new GradientNoise(EndlessNoise.Seed(p.seed, StreamLitter, 0));

        private static float Smooth01(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// How shaded the floor is at window-local (lx, lz), 0..1: the land cover's forest weight (0 below <see cref="ShadeStart"/>,
        /// 1 from <see cref="ShadeFull"/>), lighter on the road's margin, 0 inside the glades. Pure; near and far windows agree.
        /// </summary>
        public static float WoodShade(GenWindow w, IReadOnlyList<Glade> glades, float lx, float lz) => WoodShade(w, glades, lx, lz, 0f, out _);

        /// <summary>
        /// <see cref="WoodShade(GenWindow, IReadOnlyList{Glade}, float, float)"/> and, in <paramref name="crown"/>, the leaf litter of
        /// the crowns: <paramref name="canopy"/> (0..1, how much of the point a crown covers; <see cref="CanopyMap"/>) where the land
        /// cover's forest weight is at least <see cref="CrownLitterStart"/> (full from <see cref="CrownLitterFull"/>, so the scattered
        /// oaks of a wood's open parts get their patch and meadow trees none), 0 in the glades, thinner on the road's verge.
        /// </summary>
        public static float WoodShade(GenWindow w, IReadOnlyList<Glade> glades, float lx, float lz, float canopy, out float crown) =>
            WoodShade(w, glades, lx, lz, canopy, out crown, null);

        /// <summary>
        /// The land cover clears the forest for <see cref="RoadsideReach"/> m either side of the road (weight 0 within 10 m), so the
        /// strip seen from the road was painted as lawn under the roadside oaks. Within that reach the wood floor takes the forest
        /// weight of the point straight out from the road at <see cref="RoadsideReach"/> m (global field: every window agrees).
        /// </summary>
        public const float RoadsideReach = 16f;

        private static float WoodShade(GenWindow w, IReadOnlyList<Glade> glades, float lx, float lz, float canopy, out float crown, TerrainField field)
        {
            crown = 0f;
            float f = WoodForest(w, lx, lz, field);
            float s = Smooth01((f - ShadeStart) / (ShadeFull - ShadeStart));
            float c = canopy > 0f ? canopy * Smooth01((f - CrownLitterStart) / (CrownLitterFull - CrownLitterStart)) : 0f;
            if (s <= 0f && c <= 0f) return 0f;
            float rd = w.Roads.Sample(lx, lz);
            s *= VergeShade + (1f - VergeShade) * Smooth01((rd - 3.5f) / 5f);
            c *= CrownVerge + (1f - CrownVerge) * Smooth01((rd - 3.5f) / 3.5f);
            if (glades != null)
            {
                double ax = w.X0 + lx, az = w.Z0 + lz;
                foreach (Glade g in glades)
                {
                    double dx = ax - g.X, dz = az - g.Z;
                    double reach = g.Radius + GladeShadeRamp;
                    if (dx * dx + dz * dz >= reach * reach) continue;
                    double d = Math.Sqrt(dx * dx + dz * dz) - g.Radius;
                    float k = Smooth01((float)(d + 1.0) / (GladeShadeRamp + 1f));
                    s *= k;
                    c *= k;
                }
            }

            crown = c;
            return s;
        }

        /// <summary>The land cover's forest weight for the wood floor: across the road's clearing, the wood's beyond it (<see cref="RoadsideReach"/>).</summary>
        private static float WoodForest(GenWindow w, float lx, float lz, TerrainField field)
        {
            float f = w.Cover.SampleForest(lx, lz);
            if (field != null && f < 0.9f)
            {
                float rd0 = w.Roads.Sample(lx, lz);
                if (rd0 < RoadsideReach)
                {
                    float gx = w.Roads.Sample(lx + 1f, lz) - w.Roads.Sample(lx - 1f, lz), gz = w.Roads.Sample(lx, lz + 1f) - w.Roads.Sample(lx, lz - 1f);
                    float gl = (float)Math.Sqrt(gx * gx + gz * gz);
                    if (gl > 1e-3f)
                    {
                        float push = RoadsideReach - rd0;
                        f = Math.Max(f, ForestAt(field, w.X0 + lx + gx / gl * push, w.Z0 + lz + gz / gl * push));
                    }
                }
            }

            return f;
        }

        /// <summary>Land-cover forest weight where the crowns start to drop leaf litter, and where it is full.</summary>
        public const float CrownLitterStart = 0.3f, CrownLitterFull = 0.5f;

        /// <summary>Crown litter kept on the road's margin (within 3.5 m of the centre), full by 7 m: the verge keeps a little grass.</summary>
        public const float CrownVerge = 0.3f;

        /// <summary>A crown covers the ground fully to this share of its radius and fades out by <see cref="CrownOuter"/>.</summary>
        public const float CrownInner = 0.8f, CrownOuter = 1.2f;

        /// <summary>
        /// Distant tiles have no crowns: their expected canopy at a forest weight (<see cref="GroundLook.WoodCanopyMean"/> in a deep
        /// wood), so a wood's floor averages to the same colour near and far.
        /// </summary>
        public static float ExpectedCanopy(float forest) => GroundLook.WoodCanopyMean * Smooth01((forest - CrownLitterStart) / (ShadeFull - CrownLitterStart));

        /// <summary>Width of the band along a near tile's edge where its crowns blend into the expected canopy, metres.</summary>
        public const float SeamBand = 6f;

        /// <summary>
        /// A near tile knows only its own crowns (the scatter keeps the tile's trees), so a crown on the seam would be cut: within
        /// <see cref="SeamBand"/> of the tile's edge (window-local square [lo, hi)) the crown cover blends into
        /// <see cref="ExpectedCanopy"/>, which both neighbours (and the distant tiles) compute the same.
        /// </summary>
        private static float SeamCanopy(GenWindow w, TerrainField field, float k, float lx, float lz, float lo, float hi)
        {
            float edge = Math.Min(Math.Min(lx - lo, hi - lx), Math.Min(lz - lo, hi - lz));
            if (edge >= SeamBand) return k;
            float e = ExpectedCanopy(WoodForest(w, lx, lz, field));
            float t = Smooth01(edge / SeamBand);
            return e + (k - e) * t;
        }

        /// <summary>
        /// How much of the ground a tile's tree crowns cover, at window-local points (canopy floor pass): each crown (radius from the
        /// measured far-tree shapes times the tree's scale) covers fully to <see cref="CrownInner"/> of its radius and fades out by
        /// <see cref="CrownOuter"/>; overlapping crowns take the larger cover. Pure.
        /// </summary>
        public sealed class CanopyMap
        {
            private const float Cell = 8f;
            private readonly Dictionary<long, List<Vector3>> cells = new Dictionary<long, List<Vector3>>();
            private float maxReach;

            public CanopyMap(IReadOnlyList<TreePlacement> trees, IReadOnlyList<Vector2> trunks = null)
            {
                if (trees != null)
                {
                    foreach (TreePlacement t in trees)
                    {
                        if (PropRole.IsTree(t.role)) Add(t.position.x, t.position.z, CrownRadius(t));
                    }
                }

                if (trunks == null) return;
                // The window's pad: trunks the planting placed for the neighbouring tiles (no placement kept), at a mean crown, so
                // the litter of a crown on the seam reaches both tiles.
                foreach (Vector2 t in trunks)
                {
                    if (!Near(t.x, t.y)) Add(t.x, t.y, PadCrownRadius);
                }
            }

            /// <summary>Crown radius of the trunks in the window's pad (the mean crown of a wood, metres).</summary>
            public const float PadCrownRadius = 3.3f;

            private void Add(float x, float z, float r)
            {
                if (r <= 0.3f) return;
                float reach = r * CrownOuter;
                if (reach > maxReach) maxReach = reach;
                long key = Key((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell));
                if (!cells.TryGetValue(key, out List<Vector3> l)) cells[key] = l = new List<Vector3>();
                l.Add(new Vector3(x, r, z));
            }

            private bool Near(float x, float z)
            {
                if (!cells.TryGetValue(Key((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell)), out List<Vector3> l)) return false;
                foreach (Vector3 c in l)
                {
                    if ((c.x - x) * (c.x - x) + (c.z - z) * (c.z - z) < 0.25f) return true;
                }

                return false;
            }

            /// <summary>The number of crowns in the map.</summary>
            public int Count
            {
                get
                {
                    int n = 0;
                    foreach (List<Vector3> l in cells.Values) n += l.Count;
                    return n;
                }
            }

            /// <summary>Crown radius of a tree placement, metres.</summary>
            public static float CrownRadius(TreePlacement t) => FarTreeStyles.Defaults.Resolve(t.role, t.variant).Radius * (t.scale > 0f ? t.scale : 1f);

            /// <summary>Crown cover 0..1 at window-local (lx, lz).</summary>
            public float At(float lx, float lz)
            {
                if (cells.Count == 0) return 0f;
                if (raster == null) Rasterise();
                float gx = (lx - rx0) / RasterTexel - 0.5f, gz = (lz - rz0) / RasterTexel - 0.5f;
                int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz);
                if (ix < 0 || iz < 0 || ix >= rw - 1 || iz >= rh - 1) return Exact(lx, lz);
                float fx = gx - ix, fz = gz - iz;
                int i = iz * rw + ix;
                float a = raster[i] + (raster[i + 1] - raster[i]) * fx, b = raster[i + rw] + (raster[i + rw + 1] - raster[i + rw]) * fx;
                return a + (b - a) * fz;
            }

            /// <summary>Texel of the cover raster <see cref="At"/> samples (bilinear), metres.</summary>
            private const float RasterTexel = 0.5f;
            private float[] raster;
            private float rx0, rz0;
            private int rw, rh;

            /// <summary>Stamps every crown into a raster over the crowns' extent (one pass, instead of a search per sample).</summary>
            private void Rasterise()
            {
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                foreach (List<Vector3> l in cells.Values)
                {
                    foreach (Vector3 c in l)
                    {
                        float o = c.y * CrownOuter;
                        x0 = Math.Min(x0, c.x - o); z0 = Math.Min(z0, c.z - o); x1 = Math.Max(x1, c.x + o); z1 = Math.Max(z1, c.z + o);
                    }
                }

                rx0 = (float)Math.Floor(x0) - 1f;
                rz0 = (float)Math.Floor(z0) - 1f;
                rw = (int)Math.Ceiling((x1 + 1f - rx0) / RasterTexel) + 1;
                rh = (int)Math.Ceiling((z1 + 1f - rz0) / RasterTexel) + 1;
                raster = new float[rw * rh];
                foreach (List<Vector3> l in cells.Values)
                {
                    foreach (Vector3 c in l)
                    {
                        float o = c.y * CrownOuter;
                        int ax = Math.Max(0, (int)((c.x - o - rx0) / RasterTexel)), bx = Math.Min(rw - 1, (int)((c.x + o - rx0) / RasterTexel) + 1);
                        int az = Math.Max(0, (int)((c.z - o - rz0) / RasterTexel)), bz = Math.Min(rh - 1, (int)((c.z + o - rz0) / RasterTexel) + 1);
                        for (int z = az; z <= bz; z++)
                        {
                            float dz = rz0 + (z + 0.5f) * RasterTexel - c.z;
                            for (int x = ax; x <= bx; x++)
                            {
                                float dx = rx0 + (x + 0.5f) * RasterTexel - c.x;
                                float d2 = dx * dx + dz * dz;
                                if (d2 >= o * o) continue;
                                float v = 1f - Smooth01(((float)Math.Sqrt(d2) - c.y * CrownInner) / (c.y * (CrownOuter - CrownInner)));
                                int i = z * rw + x;
                                if (v > raster[i]) raster[i] = v;
                            }
                        }
                    }
                }
            }

            /// <summary>Crown cover at a point by searching the crowns (outside the raster).</summary>
            private float Exact(float lx, float lz)
            {
                int r = (int)Math.Ceiling(maxReach / Cell);
                int cx = (int)Math.Floor(lx / Cell), cz = (int)Math.Floor(lz / Cell);
                float best = 0f;
                for (int z = cz - r; z <= cz + r; z++)
                {
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (!cells.TryGetValue(Key(x, z), out List<Vector3> l)) continue;
                        foreach (Vector3 c in l)
                        {
                            float dx = lx - c.x, dz = lz - c.z, outer = c.y * CrownOuter;
                            float d2 = dx * dx + dz * dz;
                            if (d2 >= outer * outer) continue;
                            float v = 1f - Smooth01(((float)Math.Sqrt(d2) - c.y * CrownInner) / (c.y * (CrownOuter - CrownInner)));
                            if (v > best) best = v;
                        }
                    }
                }

                return best;
            }

            private static long Key(int gx, int gz) => ((long)gx << 32) ^ (uint)gz;
        }

        /// <summary>Litter share 0..1 (0 mossy, 1 leaf litter) at an absolute point: slow drifts about 22 m across.</summary>
        public float Litter(double ax, double az) => FastMathf.Clamp01(0.5f + 0.35f * EndlessNoise.FbmN(litterNoise, ax, az, 1.0 / 22.0, 2));

        /// <summary>
        /// The wood floor on a splat grid (sample i at <paramref name="offset"/> + (i + 0.5) · <paramref name="texel"/>, window-local),
        /// before the glades are painted: under the canopy the grass channels give up to <see cref="MaxFloorPaint"/> of their weight to
        /// the wood floor mix (<see cref="GroundLook.WoodFloorBlend"/>: under the crowns mostly the leaf-litter layer); road dirt and cobbles are kept. With <paramref name="trees"/> or <paramref name="trunks"/> (near tiles)
        /// bare earth rings the trunks.
        /// </summary>
        public void PaintWoodFloor(float[,,] splat, GenWindow w, float offset, float texel, IReadOnlyList<Glade> glades,
            IReadOnlyList<TreePlacement> trees = null, IReadOnlyList<Vector2> trunks = null)
        {
            if (splat == null) return;
            int rz = splat.GetLength(0), rx = splat.GetLength(1), layers = splat.GetLength(2);
            if (layers <= TerrainShaper.CobbleLayer) return;
            bool litterChannel = layers > TerrainShaper.LeafLitterLayer;   // the endless road's tiles: the litter layer (else folded into mud)
            float[] bare = null;
            if ((trees != null && trees.Count > 0) || (trunks != null && trunks.Count > 0))
            {
                bare = new float[rz * rx];
                if (trees != null)
                {
                    foreach (TreePlacement t in trees) StampBare(bare, rz, rx, offset, texel, t.position.x, t.position.z, BareEarthRadius * FastMathf.Clamp(t.scale > 0f ? t.scale : 1f, 0.7f, 1.6f));
                }

                if (trunks != null)
                {
                    foreach (Vector2 t in trunks) StampBare(bare, rz, rx, offset, texel, t.x, t.y, BareEarthRadius);
                }
            }

            // Canopy floor pass: near tiles darken the floor under each crown (leaf litter), distant ones by the expected canopy.
            CanopyMap canopy = trees != null ? new CanopyMap(trees, trunks) : null;
            for (int z = 0; z < rz; z++)
            {
                float lz = offset + (z + 0.5f) * texel;
                for (int x = 0; x < rx; x++)
                {
                    float lx = offset + (x + 0.5f) * texel;
                    float k0 = canopy != null ? canopy.At(lx, lz) : -1f;
                    if (k0 < 0f) k0 = ExpectedCanopy(WoodForest(w, lx, lz, field));
                    else k0 = SeamCanopy(w, field, k0, lx, lz, offset, offset + rx * texel);
                    float s = WoodShade(w, glades, lx, lz, k0, out float crown, field);
                    float b = bare != null && s > 0f ? bare[z * rx + x] * Smooth01(s * 2f) : 0f;
                    float floor = 1f - (1f - s) * (1f - crown);
                    if (floor <= 0f) continue;
                    float protect = 1f - FastMathf.Clamp01(2f * (splat[z, x, TerrainShaper.DirtLayer] + splat[z, x, TerrainShaper.CobbleLayer]));
                    if (protect <= 0f) continue;
                    float k = (MaxFloorPaint + (CrownFloorPaint - MaxFloorPaint) * (crown / floor)) * floor * protect;
                    float moved = Take(splat, z, x, TerrainShaper.LushGrassLayer, k) + Take(splat, z, x, TerrainShaper.DryGrassLayer, k)
                                  + Take(splat, z, x, TerrainShaper.FlowerLayer, k) + Take(splat, z, x, TerrainShaper.ForestFloorLayer, k);
                    Vector4 shares = GroundLook.WoodFloorBlend(Litter(w.X0 + lx, w.Z0 + lz), crown / floor);
                    splat[z, x, TerrainShaper.ForestFloorLayer] += moved * shares.x;
                    splat[z, x, TerrainShaper.MudLayer] += moved * (shares.y + (litterChannel ? 0f : shares.w));
                    splat[z, x, TerrainShaper.DirtLayer] += moved * shares.z;
                    if (litterChannel) splat[z, x, TerrainShaper.LeafLitterLayer] += moved * shares.w;
                    if (b <= 0f) continue;
                    float kb = b * protect;
                    float bareMoved = Take(splat, z, x, TerrainShaper.ForestFloorLayer, kb) + Take(splat, z, x, TerrainShaper.LushGrassLayer, kb)
                                      + Take(splat, z, x, TerrainShaper.DryGrassLayer, kb);
                    if (litterChannel) bareMoved += Take(splat, z, x, TerrainShaper.LeafLitterLayer, kb);
                    // Leaf-litter pass: the trunk's ring is dark earth with leaves blown in (dirt 0.40 -> 0.20; its pale texture read as sand).
                    splat[z, x, TerrainShaper.MudLayer] += bareMoved * (litterChannel ? BareMud : BareMud + BareLeaves);
                    splat[z, x, TerrainShaper.DirtLayer] += bareMoved * (1f - BareMud - BareLeaves);
                    if (litterChannel) splat[z, x, TerrainShaper.LeafLitterLayer] += bareMoved * BareLeaves;
                }
            }
        }

        private static float Take(float[,,] splat, int z, int x, int channel, float k)
        {
            float t = splat[z, x, channel] * k;
            splat[z, x, channel] -= t;
            return t;
        }

        private static void StampBare(float[] bare, int rz, int rx, float offset, float texel, float cx, float cz, float r)
        {
            int x0 = Math.Max(0, (int)Math.Floor((cx - r - offset) / texel - 0.5f)), x1 = Math.Min(rx - 1, (int)Math.Ceiling((cx + r - offset) / texel - 0.5f));
            int z0 = Math.Max(0, (int)Math.Floor((cz - r - offset) / texel - 0.5f)), z1 = Math.Min(rz - 1, (int)Math.Ceiling((cz + r - offset) / texel - 0.5f));
            for (int z = z0; z <= z1; z++)
            {
                float dz = offset + (z + 0.5f) * texel - cz;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = offset + (x + 0.5f) * texel - cx;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d >= r) continue;
                    float v = BareEarthShare * (1f - d / r) * (1f - d / r);
                    int i = z * rx + x;
                    if (v > bare[i]) bare[i] = v;
                }
            }
        }

        /// <summary>
        /// The sward under the canopy (a detail grid like <see cref="GladeDetails"/>, before it): tall grass and flowers give way,
        /// medium clumps thin by up to three quarters and short ones by up to 45 %, and ground cover fills in (up to 60 % of the
        /// cells that grow anything), so the floor reads as low, dark growth. Glades and wood edges keep their grass (shade 0).
        /// </summary>
        public void ShadeDetails(List<DetailLayer> layers, GenWindow w, int offset, int res, float texel, IReadOnlyList<Glade> glades,
            IReadOnlyList<TreePlacement> trees = null, IReadOnlyList<Vector2> trunks = null)
        {
            // Canopy floor pass: under a crown (its leaf litter, WoodShade's crown term) the grass goes almost entirely.
            CanopyMap canopy = trees != null ? new CanopyMap(trees, trunks) : null;
            var gone = new List<DetailLayer>();
            var med = new List<DetailLayer>();
            var shortGrass = new List<DetailLayer>();
            var cover = new List<DetailLayer>();
            foreach (DetailLayer l in layers)
            {
                if (l.family == PropRole.TallGrass || l.family == PropRole.Wildflowers || l.family == PropRole.GenFlowers || l.family == PropRole.FlatFlowers ||
                    l.family == PropRole.SunflowerDetail) gone.Add(l);
                else if (l.family == PropRole.MedGrass) med.Add(l);
                else if (l.family == PropRole.ShortGrass) shortGrass.Add(l);
                else if (l.family == PropRole.GroundCover) cover.Add(l);
            }

            for (int z = 0; z < res; z++)
            {
                float lz = (z + offset + 0.5f) * texel;
                for (int x = 0; x < res; x++)
                {
                    float lx = (x + offset + 0.5f) * texel;
                    int i = z * res + x;
                    bool grows = false;
                    foreach (DetailLayer l in layers)
                    {
                        if (l.density[i] > 0) { grows = true; break; }
                    }

                    if (!grows) continue;   // road, path, water, rock: left alone
                    float k0 = canopy != null ? SeamCanopy(w, field, canopy.At(lx, lz), lx, lz, offset * texel, (offset + res) * texel) : 0f;
                    float s = WoodShade(w, glades, lx, lz, k0, out float crown, field);
                    if (s <= 0.02f && crown <= 0.02f) continue;
                    ulong h = EndlessNoise.Hash(p.seed, StreamShadeDetail, (long)Math.Floor((w.X0 + lx) / texel), (long)Math.Floor((w.Z0 + lz) / texel));
                    double u1 = (h & 0xFFFF) / 65536.0, u2 = ((h >> 16) & 0xFFFF) / 65536.0, u3 = ((h >> 32) & 0xFFFF) / 65536.0, u4 = ((h >> 48) & 0xFFFF) / 65536.0;
                    if (u1 < Math.Max(s, crown)) Clear(gone, i);
                    if (u2 < Math.Max(0.75 * s, CrownMedGone * crown)) Clear(med, i);
                    if (u3 < Math.Max(0.45 * s, CrownShortGone * crown)) Clear(shortGrass, i);
                    if (cover.Count > 0 && u4 < 0.6 * Math.Max(s, crown))
                    {
                        bool has = false;
                        foreach (DetailLayer l in cover) has |= l.density[i] > 0;
                        if (!has) cover[(int)((h >> 8) % (ulong)cover.Count)].density[i] = 1;
                    }
                }
            }
        }

        /// <summary>Under a full crown: the share of medium and of short grass clumps that go (tall grass and flowers all go).</summary>
        public const float CrownMedGone = 0.95f, CrownShortGone = 0.85f;

        private static void Clear(List<DetailLayer> list, int i)
        {
            foreach (DetailLayer l in list) l.density[i] = 0;
        }

        // ------------------------------------------------------------------ the understorey (near tiles)

        /// <summary>Discs the understorey keeps off (window-local): small occupied discs shrunk to <see cref="FloorTrunkKeep"/>, glades left out.</summary>
        private sealed class FloorSpace
        {
            private const float Cell = 8f;
            private readonly Dictionary<long, List<Vector3>> cells = new Dictionary<long, List<Vector3>>();
            private float largest;

            public FloorSpace(OccupancyMap occupancy, IReadOnlyList<Glade> glades, GenWindow w)
            {
                for (int i = 0; i < occupancy.Count; i++)
                {
                    Vector3 c = occupancy.Circle(i);   // x, radius, z
                    if (IsGlade(c, glades, w)) continue;
                    Add(c.x, c.z, c.y <= FloorSmallDisc ? Math.Min(c.y, FloorTrunkKeep) : c.y);
                }
            }

            private static bool IsGlade(Vector3 c, IReadOnlyList<Glade> glades, GenWindow w)
            {
                if (glades == null) return false;
                foreach (Glade g in glades)
                {
                    if (Math.Abs(c.y - g.Radius) < 1e-3f && Math.Abs(c.x - w.LocalX(g.X)) < 1e-2f && Math.Abs(c.z - w.LocalZ(g.Z)) < 1e-2f) return true;
                }

                return false;
            }

            private static long Key(int gx, int gz) => ((long)gx << 32) ^ (uint)gz;

            public void Add(float x, float z, float r)
            {
                if (r > largest) largest = r;
                int gx0 = (int)Math.Floor((x - r) / Cell), gx1 = (int)Math.Floor((x + r) / Cell);
                int gz0 = (int)Math.Floor((z - r) / Cell), gz1 = (int)Math.Floor((z + r) / Cell);
                for (int gz = gz0; gz <= gz1; gz++)
                {
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        long k = Key(gx, gz);
                        if (!cells.TryGetValue(k, out List<Vector3> l)) cells[k] = l = new List<Vector3>(4);
                        l.Add(new Vector3(x, r, z));
                    }
                }
            }

            public bool Overlaps(float x, float z, float r)
            {
                if (cells.TryGetValue(Key((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell)), out List<Vector3> l) && Hit(l, x, z, r)) return true;
                int gx0 = (int)Math.Floor((x - r) / Cell), gx1 = (int)Math.Floor((x + r) / Cell);
                int gz0 = (int)Math.Floor((z - r) / Cell), gz1 = (int)Math.Floor((z + r) / Cell);
                for (int gz = gz0; gz <= gz1; gz++)
                {
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        if (cells.TryGetValue(Key(gx, gz), out l) && Hit(l, x, z, r)) return true;
                    }
                }

                return false;
            }

            private static bool Hit(List<Vector3> l, float x, float z, float r)
            {
                foreach (Vector3 c in l)
                {
                    float dx = c.x - x, dz = c.z - z, rr = c.y + r;
                    if (dx * dx + dz * dz < rr * rr) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// The understorey of a near tile, after the scatter: logs lying in the wood (each ringed by ferns, some with mushrooms), then
        /// clusters of 5-15 ferns and undergrowth bushes of different sizes on a <see cref="ClusterCell"/> m grid, most likely at the
        /// glade edges and on the road's margin (8-9 m from the centre), less likely under the densest canopy; none on the verge.
        /// </summary>
        private void WoodUnderstorey(Context ctx, List<PropPlacement> props)
        {
            GenWindow w = ctx.W;
            bool ferns = keys.Has(PropRole.Fern), undergrowth = keys.Has(PropRole.Undergrowth);
            if (!ferns && !undergrowth) return;
            var space = new FloorSpace(ctx.Occupancy, ctx.Glades, w);
            WoodLogs(ctx, space, props, ferns);
            double margin = 6.0;
            double x0 = Math.Max(ctx.Tx - margin, w.X0 + 2.0), z0 = Math.Max(ctx.Tz - margin, w.Z0 + 2.0);
            double x1 = Math.Min(ctx.Tx + ctx.Tile + margin, w.X0 + w.Size - 2.0), z1 = Math.Min(ctx.Tz + ctx.Tile + margin, w.Z0 + w.Size - 2.0);
            for (long gz = (long)Math.Floor(z0 / ClusterCell); gz * ClusterCell < z1; gz++)
            {
                for (long gx = (long)Math.Floor(x0 / ClusterCell); gx * ClusterCell < x1; gx++)
                {
                    double cx = (gx + 0.15 + 0.7 * EndlessNoise.Unit(p.seed, StreamCluster, gx, gz + 7919)) * ClusterCell;
                    double cz = (gz + 0.15 + 0.7 * EndlessNoise.Unit(p.seed, StreamCluster, gx + 7919, gz)) * ClusterCell;
                    float lx = w.LocalX(cx), lz = w.LocalZ(cz);
                    if (lx < 2f || lz < 2f || lx > w.Size - 2f || lz > w.Size - 2f) continue;
                    double chance = ClusterChance(ctx, lx, lz, cx, cz, out bool inGlade);
                    if (chance <= 0.0 || EndlessNoise.Unit(p.seed, StreamCluster, gx, gz) >= chance) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamClusterPick, gx, gz));
                    double roll = rng.NextDouble();
                    // A drift of ferns (most), undergrowth with ferns round it, or a clump of undergrowth; in a glade small fern drifts only.
                    int kind = inGlade || !undergrowth ? 0 : !ferns ? 2 : roll < 0.45 ? 0 : roll < 0.8 ? 1 : 2;
                    int n = inGlade ? ClusterMin : ClusterMin + rng.Next(ClusterMax - ClusterMin + 1);
                    float radius = 1.4f + 0.24f * n;
                    // Away from the road (seen from 25 m and more) only the light fern meshes (catalogue 1-3: 176-252 triangles).
                    bool light = w.Roads.Sample(lx, lz) > 25f && keys.Count(PropRole.Fern) >= 4;
                    for (int m = 0; m < n; m++)
                    {
                        double a = rng.NextDouble() * Math.PI * 2.0, rr = radius * Math.Sqrt(rng.NextDouble());
                        float px = lx + (float)(Math.Cos(a) * rr), pz = lz + (float)(Math.Sin(a) * rr);
                        bool bush = kind == 2 || (kind == 1 && rr < radius * 0.55);
                        double size = rng.NextDouble();
                        float yaw = (float)(rng.NextDouble() * 360.0);
                        if (bush) FloorPlant(ctx, space, props, PropRole.Undergrowth, light ? LightUndergrowth[rng.Next(LightUndergrowth.Length)] % keys.Count(PropRole.Undergrowth) : keys.PickVariant(PropRole.Undergrowth, rng), px, pz, 0.85f + 0.85f * (float)size, yaw, 0.8f, PlantVerge + 1.4f);
                        else FloorPlant(ctx, space, props, PropRole.Fern, light ? 1 + rng.Next(3) : keys.PickVariant(PropRole.Fern, rng), px, pz, 0.65f + 0.95f * (float)(size * size), yaw, 0.42f, PlantVerge + 0.4f);
                    }
                }
            }
        }

        /// <summary>Chance of an understorey cluster centred at window-local (lx, lz) (absolute ax, az).</summary>
        private double ClusterChance(Context ctx, float lx, float lz, double ax, double az, out bool inGlade)
        {
            GenWindow w = ctx.W;
            inGlade = false;
            float f = w.Cover.SampleForest(lx, lz);
            if (f < 0.3f) return 0.0;
            float wood = FastMathf.Clamp01((f - 0.3f) / 0.3f);
            float edge = 0f;
            foreach (Glade g in ctx.Glades)
            {
                double dx = ax - g.X, dz = az - g.Z, d = Math.Sqrt(dx * dx + dz * dz) - g.Radius;
                if (d < -3.0) inGlade = true;
                edge = Math.Max(edge, 1f - FastMathf.Clamp01((float)Math.Abs(d - 2.0) / 7f));
            }

            float rd = w.Roads.Sample(lx, lz);
            float roadMargin = 1f - FastMathf.Clamp01(Math.Abs(rd - 8.5f) / 4.5f);
            float rim = Math.Max(edge, roadMargin);
            float deep = Smooth01((f - 0.65f) / 0.25f) * (1f - rim);
            float drift = FastMathf.Clamp(1f + 0.6f * EndlessNoise.FbmN(floorNoise, ax, az, 1.0 / 45.0, 2), 0.25f, 1.9f);
            double chance = wood * drift * (0.6f + 0.4f * rim) * (1f - 0.45f * deep);
            return inGlade ? chance * 0.2 : chance;
        }

        /// <summary>Plants one understorey plant if the ground takes it (no trunk, road verge, water, steep slope, field or yard).</summary>
        private void FloorPlant(Context ctx, FloorSpace space, List<PropPlacement> list, string role, int variant, float lx, float lz, float scale, float yaw, float reach, float roadClearance)
        {
            float r = reach * scale;
            if (!FloorFits(ctx, lx, lz, r, roadClearance, 0.85f) || space.Overlaps(lx, lz, r * FloorSpacing)) return;
            if (TooHigh(ctx, PropRole.PineTree, lx, lz)) return;
            space.Add(lx, lz, r * FloorSpacing);
            ctx.Occupancy.Add(new Vector3(lx, 0f, lz), r);
            if (!InTile(ctx, lx, lz)) return;
            float y = ScatterPlanner.GroundHeight(ctx.W.Params, ctx.W.Map, lx, lz, r * 0.5f);
            list.Add(new PropPlacement { role = role, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale, settlementId = -1 });
        }

        /// <summary><see cref="Fits"/> without the occupancy test (the understorey has its own, <see cref="FloorSpace"/>).</summary>
        private bool FloorFits(Context ctx, float lx, float lz, float radius, float roadClearance, float maxSlope)
        {
            GenWindow w = ctx.W;
            if (lx < 2f || lz < 2f || lx > w.Size - 2f || lz > w.Size - 2f) return false;
            if (!RoadClear(w, lx, lz, roadClearance)) return false;
            int i = w.Cover.Index(lx, lz);
            if (w.Cover.IsWater(i) || w.Cover.Slope[i] > maxSlope || w.Cover.Shore[i] > 0.6f) return false;
            if (w.Cover.WaterHeight > 0f && HeightmapGenerator.SampleHeight(w.Map, w.Params, lx, lz) < w.Cover.WaterHeight + 0.4f) return false;
            if (ScatterPlanner.InsideSettlement(ctx.Sites, lx, lz, 12f + radius)) return false;
            if (ScatterPlanner.InsideField(ctx.Fields, lx, lz, 0.6f + 0.4f * radius)) return false;
            return !ScatterPlanner.InsideField(ctx.Patchwork, lx, lz, 0.6f + 0.4f * radius);
        }

        /// <summary>
        /// Fallen logs inside the wood (one cell in three of <see cref="WoodLogCell"/> m, within <see cref="WoodLogReach"/> m of the
        /// road, out of the glades), each ringed by 6-10 ferns, half of them with a group of mushrooms at one end.
        /// </summary>
        private void WoodLogs(Context ctx, FloorSpace space, List<PropPlacement> props, bool ferns)
        {
            GenWindow w = ctx.W;
            int logs = keys.Count(PropRole.Stump);
            if (logs < 5) return;   // catalogue order: stumps 0-2, logs 3-4, roots 5-6
            double x0 = Math.Max(ctx.Tx - 8.0, w.X0 + 2.0), z0 = Math.Max(ctx.Tz - 8.0, w.Z0 + 2.0);
            double x1 = Math.Min(ctx.Tx + ctx.Tile + 8.0, w.X0 + w.Size - 2.0), z1 = Math.Min(ctx.Tz + ctx.Tile + 8.0, w.Z0 + w.Size - 2.0);
            for (long gz = (long)Math.Floor(z0 / WoodLogCell); gz * WoodLogCell < z1; gz++)
            {
                for (long gx = (long)Math.Floor(x0 / WoodLogCell); gx * WoodLogCell < x1; gx++)
                {
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamWoodLog, gx, gz));
                    if (rng.NextDouble() > 0.34) continue;
                    double ax = (gx + 0.15 + 0.7 * rng.NextDouble()) * WoodLogCell, az = (gz + 0.15 + 0.7 * rng.NextDouble()) * WoodLogCell;
                    float lx = w.LocalX(ax), lz = w.LocalZ(az);
                    if (lx < 3f || lz < 3f || lx > w.Size - 3f || lz > w.Size - 3f) continue;
                    if (w.Cover.Sample(lx, lz).weights.forest < 0.55f || InGlade(ctx, ax, az, 3.0)) continue;
                    float rd = w.Roads.Sample(lx, lz);
                    if (rd < 9f || rd > WoodLogReach) continue;
                    int variant = 3 + rng.Next(2);
                    float yaw = (float)(rng.NextDouble() * 360.0), scale = 0.85f + 0.4f * (float)rng.NextDouble();
                    if (!Fits(ctx, lx, lz, 1.6f, 0f, 0.4f) || TooHigh(ctx, PropRole.PineTree, lx, lz)) continue;
                    ctx.Occupancy.Add(new Vector3(lx, 0f, lz), 1.6f);
                    space.Add(lx, lz, 1.0f);
                    if (InTile(ctx, lx, lz))
                    {
                        float y = ScatterPlanner.GroundHeight(w.Params, w.Map, lx, lz, 0.8f);
                        props.Add(new PropPlacement { role = PropRole.Stump, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale, settlementId = -1 });
                    }

                    var pick = new System.Random(EndlessNoise.Seed(p.seed, StreamWoodMushroom, gx, gz));
                    if (keys.Has(PropRole.MushroomGroup) && pick.NextDouble() < 0.5)
                    {
                        double a = yaw * Math.PI / 180.0, end = (pick.NextDouble() < 0.5 ? 1.0 : -1.0) * (1.4 + 0.4 * pick.NextDouble());
                        float mx = lx + (float)(Math.Sin(a) * end), mz = lz + (float)(Math.Cos(a) * end);
                        if (FloorFits(ctx, mx, mz, 0.4f, PlantVerge + 1f, 0.6f) && !space.Overlaps(mx, mz, 0.3f))
                        {
                            space.Add(mx, mz, 0.4f);
                            if (InTile(ctx, mx, mz))
                            {
                                props.Add(new PropPlacement { role = PropRole.MushroomGroup, variant = keys.PickVariant(PropRole.MushroomGroup, pick), position = new Vector3(mx, ScatterPlanner.GroundHeight(w.Params, w.Map, mx, mz, 0.3f), mz), yaw = (float)(pick.NextDouble() * 360.0), scale = 0.8f + 0.5f * (float)pick.NextDouble(), settlementId = -1 });
                            }
                        }
                    }

                    if (!ferns) continue;
                    int n = 6 + pick.Next(5);
                    for (int m = 0; m < n; m++)
                    {
                        double a = pick.NextDouble() * Math.PI * 2.0, rr = 1.7 + 1.8 * pick.NextDouble();
                        float px = lx + (float)(Math.Cos(a) * rr), pz = lz + (float)(Math.Sin(a) * rr);
                        double size = pick.NextDouble();
                        FloorPlant(ctx, space, props, PropRole.Fern, keys.PickVariant(PropRole.Fern, pick), px, pz, 0.7f + 0.8f * (float)size, (float)(pick.NextDouble() * 360.0), 0.42f, PlantVerge + 0.4f);
                    }
                }
            }
        }
    }
}
