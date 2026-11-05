using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MedievalZen.Roads;
using MedievalZen.Travel;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Fixed geometry of the endless road's scroll map. The strip is <see cref="Width"/> pixels across
    /// (<see cref="HalfWidth"/> metres each side of the trend line) at <see cref="MetresPerPixel"/>; it is painted in segments of
    /// <see cref="SegmentRows"/> rows (<see cref="SegmentLength"/> metres of strip), each identified by its index
    /// <c>k = floor(v / SegmentLength)</c>. Global pixel row <c>Y</c> covers v ∈ [Y, Y + 1) × MetresPerPixel.
    /// </summary>
    public static class StripMapLayout
    {
        /// <summary>Metres of ground per pixel of the scroll.</summary>
        public const double MetresPerPixel = 1.5;

        /// <summary>Pixels across the strip.</summary>
        public const int Width = 512;

        /// <summary>Metres from the trend line to either edge of the paper.</summary>
        public const double HalfWidth = Width * MetresPerPixel * 0.5;

        /// <summary>Rows per segment.</summary>
        public const int SegmentRows = 256;

        /// <summary>Metres of strip per segment.</summary>
        public const double SegmentLength = SegmentRows * MetresPerPixel;

        /// <summary>Extra rows painted above and below a segment (then cropped) so symbols crossing a seam match exactly.</summary>
        public const int Pad = 24;

        /// <summary>Height lattice spacing in pixels (heights, water and slopes are sampled there and interpolated).</summary>
        public const int LatticeStep = 2;

        /// <summary>Land-cover lattice spacing in pixels (a multiple of <see cref="LatticeStep"/>).</summary>
        public const int CoverStep = 4;

        /// <summary>Tree-mark cell size in pixels.</summary>
        public const int TreeCell = 8;

        /// <summary>Contour interval in metres (every fifth is an index contour).</summary>
        public const float ContourStep = 10f;

        /// <summary>Hillshade strength (the finite map uses 1.25 at 2.3 m per pixel; the scroll's ground is steeper close up).</summary>
        public const float ReliefStrength = 0.62f;

        /// <summary>Darkest hillshade (the finite map's 0.5 turns the pass walls into ink blots at this scale).</summary>
        public const float ReliefDarkest = 0.74f;

        /// <summary>Scale of the map symbols relative to the finite map's 1024-pixel sheet.</summary>
        public const float SymbolScale = 1.15f;

        /// <summary>Segment index containing strip height <paramref name="v"/>.</summary>
        public static int SegmentAt(double v) => (int)Math.Floor(v / SegmentLength);

        /// <summary>Strip height of the bottom edge of segment <paramref name="k"/>.</summary>
        public static double SegmentBottom(int k) => k * SegmentLength;
    }

    /// <summary>What the scroll map reads about the ground. Implementations must be thread-safe and pure.</summary>
    public interface IStripGround
    {
        /// <summary>Ground height in metres at absolute (x, z).</summary>
        float Height(double x, double z);

        /// <summary>Water level at absolute (x, z) where there is water, else a negative value.</summary>
        float Water(double x, double z);

        /// <summary>Wooded-ness 0..1 at absolute (x, z) for ground of <paramref name="height"/> and <paramref name="slope"/> (rise over run).</summary>
        float Forest(double x, double z, float height, float slope);

        /// <summary>True when <see cref="Forest"/> varies with the actual woods (tree marks are drawn); false for a plain tone.</summary>
        bool TreeMarks { get; }
    }

    /// <summary>Ground samplers for the scroll map.</summary>
    public static class StripGround
    {
        /// <summary>
        /// The best sampler for <paramref name="generator"/>: the real generator's land-cover field when it is
        /// <see cref="MedievalZen.Endless.Generation.EndlessGenerator"/>, else heights and water from the interface with a plain
        /// forest tone by region.
        /// </summary>
        public static IStripGround For(IEndlessGenerator generator)
        {
            if (generator is MedievalZen.Endless.Generation.EndlessGenerator real && real.Terrain != null) return new CoverGround(real);
            return new PlainGround(generator);
        }

        /// <summary>Heights and water from the interface; woods as a plain tone over forest regions (the stub generator).</summary>
        public sealed class PlainGround : IStripGround
        {
            private readonly IEndlessGenerator generator;

            /// <summary>Creates the sampler.</summary>
            public PlainGround(IEndlessGenerator generator)
            {
                this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            }

            /// <inheritdoc />
            public float Height(double x, double z) => generator.Height(x, z);

            /// <inheritdoc />
            public float Water(double x, double z) => generator.WaterHeight(x, z);

            /// <inheritdoc />
            public bool TreeMarks => false;

            /// <inheritdoc />
            public float Forest(double x, double z, float height, float slope)
            {
                double s = generator.NearestS(x, z, double.NaN);
                if (generator.Region(s).Kind != RegionKind.Forest) return 0f;
                RoadPoint r = generator.Road(s);
                double dx = x - r.X, dz = z - r.Z;
                return dx * dx + dz * dz > 25.0 * 25.0 ? 0.55f : 0f;
            }
        }

        /// <summary>The real generator: forest weight from its land-cover field (forest less farmland), heights and water as built.</summary>
        public sealed class CoverGround : IStripGround
        {
            private readonly MedievalZen.Endless.Generation.EndlessGenerator generator;

            /// <summary>Creates the sampler.</summary>
            public CoverGround(MedievalZen.Endless.Generation.EndlessGenerator generator)
            {
                this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            }

            /// <inheritdoc />
            public float Height(double x, double z) => generator.Height(x, z);

            /// <inheritdoc />
            public float Water(double x, double z) => generator.WaterHeight(x, z);

            /// <inheritdoc />
            public bool TreeMarks => true;

            /// <inheritdoc />
            public float Forest(double x, double z, float height, float slope)
            {
                var terrain = generator.Terrain;
                terrain.Cover(terrain.RowAt(z), x, height, slope, out float forest, out _, out _, out float farm);
                return Mathf.Clamp01(forest * (1f - farm));
            }
        }
    }

    /// <summary>Deterministic hashed value noise for the paper and the hand-made wobbles (pure, thread-safe).</summary>
    public static class StripNoise
    {
        /// <summary>Hash of two integers and a salt to [0, 1).</summary>
        public static double Hash(long a, long b, int salt)
        {
            unchecked
            {
                ulong h = (ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)salt * 0x165667B19E3779F9UL;
                h ^= h >> 31;
                h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 29;
                h *= 0x94D049BB133111EBUL;
                h ^= h >> 32;
                return (h >> 11) * (1.0 / 9007199254740992.0);
            }
        }

        /// <summary>Smooth value noise in [0, 1] at (<paramref name="x"/>, <paramref name="y"/>) (lattice spacing 1).</summary>
        public static float Value(double x, double y, int salt)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            long ix = (long)fx, iy = (long)fy;
            double tx = x - fx, ty = y - fy;
            tx = tx * tx * (3.0 - 2.0 * tx);
            ty = ty * ty * (3.0 - 2.0 * ty);
            double a = Hash(ix, iy, salt), b = Hash(ix + 1, iy, salt), c = Hash(ix, iy + 1, salt), d = Hash(ix + 1, iy + 1, salt);
            double top = a + (b - a) * tx, bottom = c + (d - c) * tx;
            return (float)(top + (bottom - top) * ty);
        }
    }

    /// <summary>Working buffers of one segment render (pooled, one per concurrent render).</summary>
    public sealed class StripScratch
    {
        internal Color[] Pixels;
        internal float[] Mask;
        internal bool[] Wet;
        internal float[] Heights, GradU, GradV, Levels, LevelsWide, Cover;
        internal readonly List<Waypoint> Waypoints = new List<Waypoint>();
        internal readonly List<Vector2> Road = new List<Vector2>(512);
        internal readonly List<double> RoadS = new List<double>(512);
    }

    /// <summary>
    /// Paints segments of the endless road's scroll map in the finite map's hand (<see cref="MapInk"/>): parchment, hillshaded
    /// relief with contours (from <see cref="IEndlessGenerator.Height"/>), water with shorelines and ripples, woods as a green
    /// wash with stippled tree marks, the road, settlement and feature symbols, milestone ticks, dashed region boundaries and the
    /// side frame. A second, "sketch" layer is the same paper with only a pencilled road and faint settlements, for country the
    /// traveller has not seen yet (<see cref="StripReveal"/> blends the two). Names are not rasterised: the overlay letters them.
    /// <para>
    /// Every pixel depends only on its absolute strip position, so neighbouring segments meet without a seam (each is painted
    /// with <see cref="StripMapLayout.Pad"/> extra rows and cropped). Pure managed code: call from worker threads; renders of
    /// different segments may run concurrently.
    /// </para>
    /// </summary>
    public sealed class StripMapRenderer
    {
        private const int W = StripMapLayout.Width;
        private const double Mpp = StripMapLayout.MetresPerPixel;
        private const float Scale = StripMapLayout.SymbolScale;

        private static readonly Color FadedInk = new Color(0.36f, 0.27f, 0.17f);

        private readonly IEndlessGenerator generator;
        private readonly StripProjection projection;
        private readonly IStripGround ground;
        private readonly float heightRange;
        private readonly ConcurrentBag<StripScratch> scratchPool = new ConcurrentBag<StripScratch>();

        /// <summary>Creates a renderer for <paramref name="projection"/>'s road; <paramref name="ground"/> defaults to <see cref="StripGround.For"/>.</summary>
        public StripMapRenderer(StripProjection projection, IStripGround ground = null)
        {
            this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
            generator = projection.Generator;
            this.ground = ground ?? StripGround.For(generator);
            heightRange = generator.Params != null ? Math.Max(1f, generator.Params.heightRange) : 420f;
        }

        /// <summary>The projection painted.</summary>
        public StripProjection Projection => projection;

        /// <summary>The ground sampler.</summary>
        public IStripGround Ground => ground;

        /// <summary>The fully inked segment <paramref name="k"/>: <see cref="StripMapLayout.Width"/> × <see cref="StripMapLayout.SegmentRows"/> pixels, row 0 at the bottom.</summary>
        public Color32[] RenderInked(int k) => RenderRows(k * StripMapLayout.SegmentRows, StripMapLayout.SegmentRows, true);

        /// <summary>The sketch layer of segment <paramref name="k"/> (paper, pencilled road, faint settlements).</summary>
        public Color32[] RenderSketch(int k) => RenderRows(k * StripMapLayout.SegmentRows, StripMapLayout.SegmentRows, false);

        /// <summary>Global rows [<paramref name="firstRow"/>, + <paramref name="rows"/>) of the inked or sketch layer.</summary>
        public Color32[] RenderRows(int firstRow, int rows, bool inked)
        {
            if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
            StripScratch scratch = scratchPool.TryTake(out StripScratch taken) ? taken : new StripScratch();
            try
            {
                return Paint(firstRow, rows, inked, scratch);
            }
            finally
            {
                scratchPool.Add(scratch);
            }
        }

        // ------------------------------------------------------------------ painting

        private Color32[] Paint(int firstRow, int rows, bool inked, StripScratch scratch)
        {
            const int pad = StripMapLayout.Pad;
            int h = rows + 2 * pad;
            int baseRow = firstRow - pad;
            int count = W * h;
            if (scratch.Pixels == null || scratch.Pixels.Length < count) scratch.Pixels = new Color[count];
            if (scratch.Mask == null || scratch.Mask.Length < count) scratch.Mask = new float[count];
            if (scratch.Wet == null || scratch.Wet.Length < count) scratch.Wet = new bool[count];
            var raster = new MapRaster(W, h, MapInk.Paper, scratch.Pixels);
            float[] mask = scratch.Mask;
            Array.Clear(mask, 0, count);

            PaintPaper(raster, baseRow);

            // The stretch of road this raster can show (with a margin for symbols and the road's wander).
            double vLow = (baseRow - 40) * Mpp, vHigh = (baseRow + h + 40) * Mpp;
            double sLow = projection.SAtV(vLow) - 40.0, sHigh = projection.SAtV(vHigh) + 40.0;
            scratch.Waypoints.Clear();
            generator.Waypoints(sLow, sHigh, scratch.Waypoints);
            CollectRoad(scratch, baseRow, sLow, sHigh);

            if (inked)
            {
                PaintGround(raster, mask, baseRow, scratch);
                PaintFrame(raster, mask);
                PaintRegionBoundaries(raster, mask, baseRow, sLow, sHigh, scratch);
                PaintRoad(raster, mask, scratch, false);
                PaintPlaces(raster, mask, baseRow, scratch, true);
            }
            else
            {
                PaintFrame(raster, mask);
                PaintRoad(raster, mask, scratch, true);
                PaintPlaces(raster, mask, baseRow, scratch, false);
            }

            var result = new Color32[W * rows];
            Color[] px = raster.Pixels;
            for (int y = 0; y < rows; y++)
            {
                int src = (y + pad) * W;
                int dst = y * W;
                for (int x = 0; x < W; x++) result[dst + x] = px[src + x];
            }

            return result;
        }

        private static void PaintPaper(MapRaster raster, int baseRow)
        {
            Color[] px = raster.Pixels;
            int h = raster.Height;
            for (int y = 0; y < h; y++)
            {
                double vm = (baseRow + y + 0.5) * Mpp;
                for (int x = 0; x < W; x++)
                {
                    px[y * W + x] = PaperAt(x, vm);
                }
            }
        }

        /// <summary>The bare parchment at column <paramref name="x"/> and strip height <paramref name="vm"/> metres.</summary>
        public static Color PaperAt(int x, double vm)
        {
            double um = (x + 0.5) * Mpp;
            float blotch = StripNoise.Value(um / 260.0, vm / 260.0, 11);
            float grain = StripNoise.Value(um / 21.0, vm / 21.0, 12);
            float fibre = StripNoise.Value(um / 4.5, vm / 70.0, 13);
            float shade = 0.9f + 0.1f * blotch + 0.05f * (grain - 0.5f) + 0.03f * (fibre - 0.5f);

            // Darker, slightly ragged long edges (the scroll's sides).
            float u = (x + 0.5f) / W;
            float edge = Mathf.Min(u, 1f - u);
            float ragged = 0.02f * StripNoise.Value(um / 30.0, vm / 30.0, 14);
            float border = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edge - ragged) / 0.06f));
            shade *= Mathf.Lerp(0.62f, 1f, border);

            // A few faint tea stains.
            float stain = StripNoise.Value(um / 190.0, vm / 190.0, 15);
            if (stain > 0.74f) shade *= 1f - (stain - 0.74f) * 0.35f;

            Color c = MapInk.Paper * shade;
            c.a = 1f;
            return c;
        }

        // ------------------------------------------------------------------ ground: relief, water, woods

        private void PaintGround(MapRaster raster, float[] mask, int baseRow, StripScratch scratch)
        {
            const int step = StripMapLayout.LatticeStep;
            const double lm = step * Mpp;                         // metres between lattice points
            int h = raster.Height;
            // Lattice columns i = -1 .. W/step + 1 at u = -HalfWidth + i·lm; rows j at v = j·lm (global).
            // Cover rows c at v = c·CoverStep pixels; every one used below (pixels and tree marks reach 8 rows past the raster)
            // lies on a height-lattice row with central differences, so its value never depends on which segment is painted.
            const int cs = StripMapLayout.CoverStep / step;
            int cjFirst = FloorDiv(baseRow - 8, StripMapLayout.CoverStep) - 1;
            int cjLast = FloorDiv(baseRow + h + 8, StripMapLayout.CoverStep) + 2;
            int ni = W / step + 3;
            int j0 = Math.Min((int)Math.Floor((baseRow + 0.5) / step) - 1, cjFirst * cs - 1);
            int j1 = Math.Max((int)Math.Floor((baseRow + h - 0.5) / step) + 2, cjLast * cs + 1);
            int nj = j1 - j0 + 1;
            int lattice = ni * nj;
            if (scratch.Heights == null || scratch.Heights.Length < lattice)
            {
                scratch.Heights = new float[lattice];
                scratch.GradU = new float[lattice];
                scratch.GradV = new float[lattice];
                scratch.Levels = new float[lattice];
            }

            float[] hs = scratch.Heights, gu = scratch.GradU, gv = scratch.GradV, levels = scratch.Levels;
            for (int j = 0; j < nj; j++)
            {
                double v = (j0 + j) * lm;
                projection.Frame(v, out double cx, out double cz, out double nx, out double nz);
                for (int i = 0; i < ni; i++)
                {
                    double u = -StripMapLayout.HalfWidth + (i - 1) * lm;
                    double x = cx + u * nx, z = cz + u * nz;
                    int k = j * ni + i;
                    hs[k] = ground.Height(x, z);
                    levels[k] = ground.Water(x, z);
                }
            }

            for (int j = 0; j < nj; j++)
            {
                for (int i = 0; i < ni; i++)
                {
                    int k = j * ni + i;
                    int il = i > 0 ? k - 1 : k, ir = i < ni - 1 ? k + 1 : k;
                    int jd = j > 0 ? k - ni : k, ju = j < nj - 1 ? k + ni : k;
                    gu[k] = (hs[ir] - hs[il]) / (float)((ir - il) * lm);
                    gv[k] = (hs[ju] - hs[jd]) / (float)((ju - jd) / ni * lm);
                }
            }

            // Spread each lake level to the neighbouring lattice points (the generator reports a level only where the bed is
            // under it), so the shoreline is decided per pixel by the interpolated ground, not by the lattice.
            if (scratch.LevelsWide == null || scratch.LevelsWide.Length < lattice) scratch.LevelsWide = new float[lattice];
            float[] wide = scratch.LevelsWide;
            for (int j = 0; j < nj; j++)
            {
                for (int i = 0; i < ni; i++)
                {
                    float best = -1f;
                    for (int b = Math.Max(0, j - 1); b <= Math.Min(nj - 1, j + 1); b++)
                    {
                        for (int a = Math.Max(0, i - 1); a <= Math.Min(ni - 1, i + 1); a++) best = Math.Max(best, levels[b * ni + a]);
                    }

                    wide[j * ni + i] = best;
                }
            }

            // Land cover on the coarser lattice (every second height-lattice point), clear of water.
            int ci = (ni - 1 + cs - 1) / cs + 1;
            int cj = cjLast - cjFirst + 1;
            if (scratch.Cover == null || scratch.Cover.Length < ci * cj) scratch.Cover = new float[ci * cj];
            float[] cover = scratch.Cover;
            for (int j = 0; j < cj; j++)
            {
                int gj = (cjFirst + j) * cs;                       // global height-lattice row
                double v = gj * lm;
                projection.Frame(v, out double cx, out double cz, out double nx, out double nz);
                for (int i = 0; i < ci; i++)
                {
                    int li = i * cs;                               // height-lattice column (index into hs, offset by the -1 column)
                    double u = -StripMapLayout.HalfWidth + (li - 1) * lm;
                    double x = cx + u * nx, z = cz + u * nz;
                    int k = (gj - j0) * ni + Math.Min(li, ni - 1);
                    float height = hs[k];
                    float slope = Mathf.Sqrt(gu[k] * gu[k] + gv[k] * gv[k]);
                    cover[j * ci + i] = levels[k] >= 0f && height < levels[k] ? 0f : ground.Forest(x, z, height, slope);
                }
            }

            // Per pixel: relief, then water.
            Color[] px = raster.Pixels;
            bool[] wet = scratch.Wet;
            for (int y = 0; y < h; y++)
            {
                int gy = baseRow + y;
                double fy = (gy + 0.5) / step - j0;
                int ly = (int)Math.Floor(fy);
                float ty = (float)(fy - ly);
                for (int x = 0; x < W; x++)
                {
                    double fx = (x + 0.5) / step + 1.0;
                    int lx = (int)fx;
                    float tx = (float)(fx - lx);
                    int k = ly * ni + lx;
                    float hh = Bilinear(hs, k, ni, tx, ty);
                    float du = Bilinear(gu, k, ni, tx, ty);
                    float dv = Bilinear(gv, k, ni, tx, ty);
                    int i = y * W + x;
                    px[i] = MapInk.ShadeRelief(px[i], du, dv, hh, Mathf.Clamp01(hh / heightRange), (float)Mpp, StripMapLayout.ContourStep, 5f, StripMapLayout.ReliefStrength, StripMapLayout.ReliefDarkest);
                    float level = Math.Max(Math.Max(wide[k], wide[k + 1]), Math.Max(wide[k + ni], wide[k + ni + 1]));
                    wet[i] = level >= 0f && hh < level;
                }
            }

            for (int y = 1; y < h - 1; y++)
            {
                int ripple = Mod(baseRow + y, 7);
                for (int x = 1; x < W - 1; x++)
                {
                    int i = y * W + x;
                    if (!wet[i]) continue;
                    px[i] = MapInk.ShadeWater(px[i], x, ripple);
                    if (!wet[i - 1] || !wet[i + 1] || !wet[i - W] || !wet[i + W]) MapInk.MarkShore(mask, i, W);
                }
            }

            raster.Composite(mask, MapInk.WaterInk, 0.75f);

            // Woods: a soft green wash, then stippled tree marks on an absolute jittered lattice.
            for (int y = 0; y < h; y++)
            {
                double fy = (baseRow + y + 0.5) / StripMapLayout.CoverStep - cjFirst;
                int ly = (int)Math.Floor(fy);
                float ty = (float)(fy - ly);
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (wet[i]) continue;
                    double fx = (x + 0.5 + step) / StripMapLayout.CoverStep;
                    int lx = (int)fx;
                    float tx = (float)(fx - lx);
                    float f = Bilinear(cover, ly * ci + lx, ci, tx, ty);
                    if (f > 0.1f) mask[i] = Mathf.SmoothStep(0f, 0.85f, (f - 0.1f) / 0.45f);
                }
            }

            raster.CompositeMultiply(mask, MapInk.ForestWash, 0.8f);
            if (!ground.TreeMarks) return;

            const int cell = StripMapLayout.TreeCell;
            int cy0 = FloorDiv(baseRow - 6, cell), cy1 = FloorDiv(baseRow + h + 6, cell);
            for (int cyIndex = cy0; cyIndex <= cy1; cyIndex++)
            {
                for (int cxIndex = 0; cxIndex * cell < W; cxIndex++)
                {
                    double jx = StripNoise.Hash(cxIndex, cyIndex, 21) * 4.0 - 2.0;
                    double jy = StripNoise.Hash(cxIndex, cyIndex, 22) * 4.0 - 2.0;
                    double roll = StripNoise.Hash(cxIndex, cyIndex, 23);
                    double mx = cxIndex * cell + cell * 0.5 + jx;                  // columns are absolute
                    double myGlobal = cyIndex * (double)cell + cell * 0.5 + jy;    // global rows
                    double fy = myGlobal / StripMapLayout.CoverStep - cjFirst;
                    double fx = (mx + step) / StripMapLayout.CoverStep;
                    int ly = (int)Math.Floor(fy), lx = (int)Math.Floor(fx);
                    if (ly < 0 || ly >= cj - 1 || lx < 0 || lx >= ci - 1) continue;
                    float f = Bilinear(cover, ly * ci + lx, ci, (float)(fx - lx), (float)(fy - ly));
                    float p = Mathf.Clamp((f - 0.25f) / 0.75f, 0f, 0.7f);
                    if (roll >= p) continue;
                    var c = new Vector2((float)mx, (float)(myGlobal - baseRow));
                    int py = (int)Math.Floor(c.y), pxIndex = (int)Math.Floor(c.x);
                    if (py >= 0 && py < h && pxIndex >= 0 && pxIndex < W && wet[py * W + pxIndex]) continue;
                    MapInk.TreeMark(raster, mask, c);
                }
            }

            raster.Composite(mask, MapInk.ForestInk, 0.7f);
        }

        private static float Bilinear(float[] a, int k, int stride, float tx, float ty)
        {
            float top = a[k] + (a[k + 1] - a[k]) * tx;
            float bottom = a[k + stride] + (a[k + stride + 1] - a[k + stride]) * tx;
            return top + (bottom - top) * ty;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        private static int Mod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }

        // ------------------------------------------------------------------ frame, boundaries, road, places

        private static void PaintFrame(MapRaster raster, float[] mask)
        {
            int h = raster.Height;
            float inner = 9f, outer = 4.5f;
            foreach (float x in new[] { inner, W - inner, outer, W - outer })
            {
                float r = x == inner || x == W - inner ? 1.1f : 0.7f;
                raster.MaskSegment(mask, new Vector2(x, -4f), new Vector2(x, h + 4f), r);
            }

            raster.Composite(mask, MapInk.InkBrown, 0.8f);
        }

        private void PaintRegionBoundaries(MapRaster raster, float[] mask, int baseRow, double sLow, double sHigh, StripScratch scratch)
        {
            RegionInfo r = generator.Region(sLow);
            int guard = 0;
            while (r.SStart <= sHigh && guard++ < 64)
            {
                if (r.SStart > sLow)
                {
                    projection.RoadToStrip(r.SStart, out double ru, out double rv);
                    float y = (float)(rv / Mpp - baseRow);
                    float roadX = (float)((ru + StripMapLayout.HalfWidth) / Mpp);
                    // Dashes on an absolute pattern, kept clear of the road and the frame.
                    for (int x0 = 14; x0 < W - 14; x0 += 9)
                    {
                        if (Math.Abs(x0 + 2.5f - roadX) < 12f) continue;
                        raster.MaskSegment(mask, new Vector2(x0, y), new Vector2(x0 + 4.5f, y), 0.6f);
                    }
                }

                double next = r.SEnd + 0.01;
                if (!(next > r.SStart)) break;
                r = generator.Region(next);
            }

            raster.Composite(mask, FadedInk, 0.55f);
        }

        private void CollectRoad(StripScratch scratch, int baseRow, double sLow, double sHigh)
        {
            scratch.Road.Clear();
            scratch.RoadS.Clear();
            const double spacing = 3.0;
            for (double s = Math.Floor(sLow / spacing) * spacing; s <= sHigh; s += spacing)
            {
                projection.RoadToStrip(s, out double u, out double v);
                scratch.Road.Add(new Vector2((float)((u + StripMapLayout.HalfWidth) / Mpp), (float)(v / Mpp - baseRow)));
                scratch.RoadS.Add(s);
            }
        }

        private static void PaintRoad(MapRaster raster, float[] mask, StripScratch scratch, bool sketch)
        {
            List<Vector2> road = scratch.Road;
            if (road.Count < 2) return;
            if (!sketch)
            {
                Vector2 previous = road[0];
                for (int i = 1; i < road.Count; i++)
                {
                    Vector2 next = road[i];
                    if ((next - previous).sqrMagnitude < 1f && i < road.Count - 1) continue;
                    raster.MaskSegment(mask, previous, next, 1.45f);
                    previous = next;
                }

                raster.Composite(mask, MapInk.RoadInk, 0.92f);
                return;
            }

            // A pencilled guess: thin, a little wavering, broken into strokes.
            for (int i = 1; i < road.Count; i++)
            {
                double s = scratch.RoadS[i];
                if (Mod((int)Math.Floor(s / 21.0), 5) == 4) continue;     // a lift of the pencil every ~100 m
                Vector2 a = Wobble(road[i - 1], road, i - 1, scratch.RoadS[i - 1]);
                Vector2 b = Wobble(road[i], road, i, s);
                raster.MaskSegment(mask, a, b, 0.75f);
            }

            raster.Composite(mask, MapInk.RoadInk, 0.6f);
        }

        private static Vector2 Wobble(Vector2 p, List<Vector2> road, int i, double s)
        {
            Vector2 a = road[Math.Max(0, i - 1)], b = road[Math.Min(road.Count - 1, i + 1)];
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return p;
            var normal = new Vector2(d.y / len, -d.x / len);
            float off = (StripNoise.Value(s / 55.0, 0.5, 31) - 0.5f) * 3.2f;
            return p + normal * off;
        }

        private void PaintPlaces(MapRaster raster, float[] mask, int baseRow, StripScratch scratch, bool inked)
        {
            List<Waypoint> places = scratch.Waypoints;
            if (places.Count == 0) return;
            if (inked)
            {
                // Clear a little paper halo so icons read over the road, woods and stipple.
                foreach (Waypoint w in places)
                {
                    if (!w.IsSettlement) continue;
                    Vector2 c = Pixel(w.X, w.Z, baseRow, w.S);
                    raster.MaskDisc(mask, c, MapInk.IconRadius(RoadWindow.TypeFor(w.Kind)) * IconScale(w.Kind) + 3f * Scale);
                }

                raster.Composite(mask, MapInk.Paper * 0.97f, 0.9f);
            }

            foreach (Waypoint w in places)
            {
                if (!inked && !w.IsSettlement) continue;
                Vector2 c = Pixel(w.X, w.Z, baseRow, w.S);
                switch (w.Kind)
                {
                    case WaypointKind.Inn:
                        MapInk.SettlementIcon(raster, mask, c, SettlementType.Inn, IconScale(w.Kind));
                        break;
                    case WaypointKind.Hamlet:
                    case WaypointKind.Village:
                    case WaypointKind.City:   // a city's own symbol is follow-up work: the town icon until then
                        MapInk.SettlementIcon(raster, mask, c, w.IsVillageLike ? SettlementType.Town : SettlementType.Village, IconScale(w.Kind));
                        break;
                    case WaypointKind.Pass:
                        MapInk.PassSign(raster, mask, c, Scale * 1.6f);   // wide enough for a refuge inn to sit between the brackets
                        break;
                    case WaypointKind.Viewpoint:
                        MapInk.ViewpointSign(raster, mask, c, Scale);
                        break;
                    case WaypointKind.Lake:
                        break;   // the water itself is drawn; the overlay letters its name
                    case WaypointKind.Camp:
                        CampSign(raster, mask, c);
                        break;
                    case WaypointKind.Milestone:
                        MilestoneTick(raster, mask, c, baseRow, w);
                        break;
                    case WaypointKind.Landmark:
                        LandmarkSign(raster, mask, c);
                        break;
                    default:
                        OtherSign(raster, mask, c, baseRow, w);
                        break;
                }
            }

            raster.Composite(mask, MapInk.InkBrown, inked ? 0.95f : 0.42f);
        }

        /// <summary>Symbol scale of a settlement kind (a village reads as the finite map's town, a hamlet as its village).</summary>
        private static float IconScale(WaypointKind kind) => kind == WaypointKind.Hamlet ? Scale * 1.1f : Scale;

        private Vector2 Pixel(double x, double z, int baseRow, double s)
        {
            projection.ToStrip(x, z, projection.VOfS(s), out double u, out double v);
            return new Vector2((float)((u + StripMapLayout.HalfWidth) / Mpp), (float)(v / Mpp - baseRow));
        }

        private static void CampSign(MapRaster raster, float[] mask, Vector2 c)
        {
            float s = Scale;
            raster.MaskSegment(mask, c + new Vector2(-4.5f, -3.5f) * s, c + new Vector2(0f, 4f) * s, 0.9f * s);
            raster.MaskSegment(mask, c + new Vector2(0f, 4f) * s, c + new Vector2(4.5f, -3.5f) * s, 0.9f * s);
            raster.MaskSegment(mask, c + new Vector2(-5.5f, -3.5f) * s, c + new Vector2(5.5f, -3.5f) * s, 0.7f * s);
            raster.MaskSegment(mask, c + new Vector2(0f, 4f) * s, c + new Vector2(0f, -1.5f) * s, 0.5f * s);
        }

        /// <summary>A landmark: the cartographer's small tower, a narrow block on a base line with a pointed top.</summary>
        private static void LandmarkSign(MapRaster raster, float[] mask, Vector2 c)
        {
            float s = Scale;
            raster.MaskSegment(mask, c + new Vector2(-3.2f, -3.5f) * s, c + new Vector2(3.2f, -3.5f) * s, 0.75f * s);
            raster.MaskSegment(mask, c + new Vector2(-1.8f, -3.5f) * s, c + new Vector2(-1.8f, 2.5f) * s, 0.7f * s);
            raster.MaskSegment(mask, c + new Vector2(1.8f, -3.5f) * s, c + new Vector2(1.8f, 2.5f) * s, 0.7f * s);
            raster.MaskSegment(mask, c + new Vector2(-2.4f, 2.5f) * s, c + new Vector2(0f, 5.5f) * s, 0.7f * s);
            raster.MaskSegment(mask, c + new Vector2(0f, 5.5f) * s, c + new Vector2(2.4f, 2.5f) * s, 0.7f * s);
        }

        private void MilestoneTick(MapRaster raster, float[] mask, Vector2 c, int baseRow, Waypoint w)
        {
            // A short tick from the road's edge out towards the stone, and the stone as a dot.
            projection.RoadToStrip(w.S, out double ru, out double rv);
            var road = new Vector2((float)((ru + StripMapLayout.HalfWidth) / Mpp), (float)(rv / Mpp - baseRow));
            Vector2 d = c - road;
            float len = d.magnitude;
            Vector2 dir = len > 1e-3f ? d / len : Vector2.right;
            raster.MaskSegment(mask, road + dir * 2.4f, road + dir * 7.5f, 0.6f);
            raster.MaskDisc(mask, road + dir * 8.6f, 1.1f);
        }

        private void OtherSign(MapRaster raster, float[] mask, Vector2 c, int baseRow, Waypoint w)
        {
            string kind = w.Kind.ToString();
            if (kind == "Bridge" || kind == "Ford")
            {
                // Across the road: a bridge as two parapet brackets, a ford as three short wavy dashes.
                projection.RoadToStrip(w.S - 3.0, out double au, out double av);
                projection.RoadToStrip(w.S + 3.0, out double bu, out double bv);
                var along = new Vector2((float)(bu - au), (float)(bv - av));
                along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector2.up;
                var across = new Vector2(along.y, -along.x);
                if (kind == "Bridge")
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 o = c + across * (side * 4f);
                        raster.MaskSegment(mask, o - along * 4.5f, o + along * 4.5f, 0.75f);
                        raster.MaskSegment(mask, o - along * 4.5f, o - along * 4.5f + across * (side * 2f) - along * 1.5f, 0.6f);
                        raster.MaskSegment(mask, o + along * 4.5f, o + along * 4.5f + across * (side * 2f) + along * 1.5f, 0.6f);
                    }
                }
                else
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        Vector2 o = c + along * (k * 2.6f);
                        raster.MaskSegment(mask, o - across * 4f, o - across * 1.3f + along * 0.8f, 0.55f);
                        raster.MaskSegment(mask, o + across * 1.3f + along * 0.8f, o + across * 4f, 0.55f);
                    }
                }

                return;
            }

            // Anything else the generator may add: a small ring with a dot.
            raster.MaskRing(mask, c, 3.2f * Scale, 0.9f * Scale);
            raster.MaskDisc(mask, c, 0.9f * Scale);
        }
    }
}
