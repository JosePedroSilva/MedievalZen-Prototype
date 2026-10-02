using System;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Large-scale 0..1 weight of the highland: 1 on and near the ridges of the <see cref="MountainRange"/>, fading through
    /// the valleys between its spurs to 0 in the lowland. The range rises at a (seeded or configured) spot on one side or
    /// corner of the map and runs towards the centre, so the lowland wraps around it.
    /// </summary>
    public sealed class MountainMask
    {
        /// <summary>Cells per side of the cached weight grid.</summary>
        private const int GridResolution = 129;

        private readonly float worldSize;
        private float[] grid;

        private MountainMask(Vector2 centre, float radius, float worldSize, MountainRange range)
        {
            Centre = centre;
            Radius = Mathf.Max(0.05f, radius);
            this.worldSize = worldSize;
            Range = range;
        }

        /// <summary>Normalised position where the range rises (its anchor).</summary>
        public Vector2 Centre { get; }

        /// <summary>Normalised size of the range.</summary>
        public float Radius { get; }

        /// <summary>The ridge skeleton of the highland.</summary>
        public MountainRange Range { get; }

        /// <summary>Mask for <paramref name="p"/> (stage seeds + 11, + 120).</summary>
        public static MountainMask Create(WorldGenParams p)
        {
            Vector2 centre = p.mountainMaskCentre;
            if (p.randomiseMountainSide)
            {
                var rng = new System.Random(p.seed + 11);
                Vector2[] spots =
                {
                    new Vector2(0.85f, 0.85f), new Vector2(0.15f, 0.85f), new Vector2(0.85f, 0.15f), new Vector2(0.15f, 0.15f),
                    new Vector2(0.5f, 0.92f), new Vector2(0.5f, 0.08f), new Vector2(0.92f, 0.5f), new Vector2(0.08f, 0.5f),
                };
                centre = spots[rng.Next(spots.Length)] + new Vector2((float)(rng.NextDouble() - 0.5) * 0.1f, (float)(rng.NextDouble() - 0.5) * 0.1f);
            }

            return new MountainMask(centre, p.mountainMaskRadius, p.worldSize, MountainRange.Create(p, centre));
        }

        /// <summary>Highland weight of a skeleton height (fraction of the mountain amplitude).</summary>
        public static float WeightOf(float skeleton) => LandCover.Smooth(0.08f, 0.45f, skeleton);

        /// <summary>
        /// Uses <paramref name="values"/> (<see cref="GridResolution"/>² weights at world positions i * size / (res - 1), as
        /// produced by the heightmap with its domain warp) instead of evaluating the unwarped skeleton.
        /// </summary>
        internal void SetGrid(float[] values)
        {
            if (values != null && values.Length == GridResolution * GridResolution) grid = values;
        }

        /// <summary>World position of grid vertex <paramref name="i"/> along one axis (for <see cref="SetGrid"/>).</summary>
        internal float GridCoordinate(int i) => i * worldSize / (GridResolution - 1);

        /// <summary>Grid vertices per side (for <see cref="SetGrid"/>).</summary>
        internal static int GridSize => GridResolution;

        /// <summary>Highland weight at world (x, z): 1 on the ridges, 0 in the lowlands.</summary>
        public float Evaluate(float x, float z)
        {
            if (grid == null)
            {
                var values = new float[GridResolution * GridResolution];
                for (int j = 0; j < GridResolution; j++)
                {
                    for (int i = 0; i < GridResolution; i++) values[j * GridResolution + i] = WeightOf(Range.Evaluate(GridCoordinate(i), GridCoordinate(j)));
                }

                grid = values;
            }

            float s = (GridResolution - 1) / worldSize;
            float gx = Mathf.Clamp(x * s, 0f, GridResolution - 1);
            float gz = Mathf.Clamp(z * s, 0f, GridResolution - 1);
            int x0 = Mathf.Min((int)gx, GridResolution - 2);
            int z0 = Mathf.Min((int)gz, GridResolution - 2);
            float tx = gx - x0;
            float tz = gz - z0;
            int k = z0 * GridResolution + x0;
            float a = grid[k] + (grid[k + 1] - grid[k]) * tx;
            float b = grid[k + GridResolution] + (grid[k + GridResolution + 1] - grid[k + GridResolution]) * tx;
            return a + (b - a) * tz;
        }
    }

    /// <summary>
    /// The heightmap plus what later stages need to interpret it: water level and highland mask.
    /// <see cref="Map"/> is modified in place by <see cref="TerrainShaper"/>.
    /// </summary>
    public sealed class Relief
    {
        /// <summary>Creates a relief.</summary>
        public Relief(WorldGenParams p, float[,] map, float waterHeight, MountainMask mask)
        {
            Params = p ?? throw new ArgumentNullException(nameof(p));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            WaterHeight = waterHeight;
            Mask = mask ?? MountainMask.Create(p);
        }

        /// <summary>Generation inputs.</summary>
        public WorldGenParams Params { get; }

        /// <summary>Normalised heightmap <c>[z, x]</c>.</summary>
        public float[,] Map { get; }

        /// <summary>Lake water level in metres, or a negative value when there are no lakes.</summary>
        public float WaterHeight { get; }

        /// <summary>True when the world has lakes.</summary>
        public bool HasWater => WaterHeight > 0f;

        /// <summary>Highland mask.</summary>
        public MountainMask Mask { get; }

        /// <summary>Relief with no water for an arbitrary map (tests, tools).</summary>
        public static Relief Dry(WorldGenParams p, float[,] map) => new Relief(p, map, -1f, MountainMask.Create(p));

        /// <summary>Terrain height in metres at world (x, z).</summary>
        public float Height(float x, float z) => HeightmapGenerator.SampleHeight(Map, Params, x, z);

        /// <summary>Normalised height (0..1) at world (x, z).</summary>
        public float Normalized(float x, float z) => HeightmapGenerator.SampleNormalized(Map, Params.worldSize, x, z);

        /// <summary>Slope (rise over run) at world (x, z).</summary>
        public float Slope(float x, float z) => HeightmapGenerator.SampleSlope(Map, Params.worldSize, Params.hillAmplitude, x, z);

        /// <summary>True when (x, z) is under water, or within <paramref name="margin"/> metres (height) of the water line.</summary>
        public bool IsWater(float x, float z, float margin = 0f) => HasWater && Height(x, z) < WaterHeight + margin;
    }

    /// <summary>
    /// Builds the normalised (0..1) heightmap. Layout matches <c>TerrainData.SetHeights</c>: <c>map[z, x]</c>;
    /// vertex (x, z) sits at world position (x * spacing, z * spacing) with spacing = worldSize / (res - 1).
    /// World height in metres = value * <see cref="WorldGenParams.hillAmplitude"/> (the total relief).
    /// </summary>
    public static class HeightmapGenerator
    {
        /// <summary>Lake beds are carved up to this many metres below the lowest land.</summary>
        public const float LakeDepth = 5f;

        /// <summary>Generates the heightmap for <paramref name="settings"/>.</summary>
        public static float[,] Generate(WorldSettings settings) => Generate(WorldGenParams.From(settings));

        /// <summary>Generates the heightmap (with lake beds carved) for <paramref name="p"/>.</summary>
        public static float[,] Generate(WorldGenParams p) => GenerateRelief(p).Map;

        /// <summary>
        /// Composes (a) warped fBm rolling lowland hills with flattened valley floors, (b) the <see cref="MountainRange"/>:
        /// a ridge skeleton of summits, a col and spurs, roughened by ridged-multifractal detail that grows with altitude,
        /// (c) occasional escarpments (rock bands) outside the range; carves drainage valleys into the highland
        /// (<see cref="CarveDrainage"/>); normalises to 0..1 leaving room for lake beds; smooths gentle slopes
        /// (<see cref="SmoothLowSlopes"/>); then floods the lowest <see cref="WorldGenParams.lakeCoverage"/> of the map and carves
        /// the lake floors. Deterministic (stage seeds + 1, + 101..106, + 11, + 120).
        /// </summary>
        public static Relief GenerateRelief(WorldGenParams p)
        {
            int n = Mathf.Max(2, p.heightmapResolution);
            float spacing = p.worldSize / (n - 1);
            var baseNoise = new GradientNoise(p.seed + 1);
            var warpX = new GradientNoise(p.seed + 101);
            var warpZ = new GradientNoise(p.seed + 102);
            var ridgeNoise = new GradientNoise(p.seed + 104);
            var escarpNoise = new GradientNoise(p.seed + 105);
            var escarpGate = new GradientNoise(p.seed + 106);
            var fineWarpX = new GradientNoise(p.seed + 107);
            var fineWarpZ = new GradientNoise(p.seed + 108);
            var strataNoise = new GradientNoise(p.seed + 109);
            var gullyNoise = new GradientNoise(p.seed + 110);
            MountainMask mask = MountainMask.Create(p);
            SkeletonGrid skeleton = SkeletonGrid.Build(mask.Range, p.worldSize, p.domainWarp * 1.5f + FineWarp + 20f);

            float f = Mathf.Max(1e-5f, p.hillFrequency);
            float warpF = f * 0.5f;
            float ridgeF = f * 1.4f;
            float escarpF = f * 0.55f;
            float flattenExponent = 1f + 2f * Mathf.Clamp01(p.valleyFlatten);
            float sharpness = Mathf.Max(1f, p.ridgeSharpness);
            float amplitudeM = Mathf.Max(1f, p.mountainAmplitude);
            Vector2 saddle = mask.Range.Saddle;
            // Strata dip: the cliff bands tilt gently across the range (a seeded direction, ~2 m per 100 m).
            var dipRng = new System.Random(p.seed + 111);
            float dipAngle = (float)(dipRng.NextDouble() * Math.PI * 2.0);
            float dipX = Mathf.Cos(dipAngle) * StrataDip, dipZ = Mathf.Sin(dipAngle) * StrataDip;

            // The fine skeleton warp is smooth (240 m wavelength): evaluate it every 4th vertex and interpolate.
            const int WarpStride = 4;
            int wn = (n - 1) / WarpStride + 2;
            var fineX = new float[wn, wn];
            var fineZ = new float[wn, wn];
            for (int j = 0; j < wn; j++)
            {
                float wz = j * WarpStride * spacing;
                for (int i = 0; i < wn; i++)
                {
                    float wx = i * WarpStride * spacing;
                    fineX[j, i] = fineWarpX.Fbm(wx * FineWarpFrequency, wz * FineWarpFrequency, 2) * FineWarp;
                    fineZ[j, i] = fineWarpZ.Fbm(wx * FineWarpFrequency, wz * FineWarpFrequency, 2) * FineWarp;
                }
            }

            var map = new float[n, n];
            var lowland = new float[n, n];
            var weight = new float[n, n];
            var valleyFloor = new float[n, n];
            for (int z = 0; z < n; z++)
            {
                float wz = z * spacing;
                for (int x = 0; x < n; x++)
                {
                    float wx = x * spacing;
                    float dx = warpX.Fbm(wx * warpF, wz * warpF, 2) * p.domainWarp;
                    float dz = warpZ.Fbm(wx * warpF, wz * warpF, 2) * p.domainWarp;
                    float sx = wx + dx;
                    float sz = wz + dz;

                    // (a) Rolling base; the power curve flattens valley floors into plains. Four octaves (smallest
                    // wavelength ≈ 60 m on the default map): a fifth only added ~30 m micro-bumps that the terrain
                    // LOD could not follow, which left grass floating at mid distance.
                    float rolling = Mathf.Clamp01(0.5f + 0.75f * baseNoise.Fbm(sx * f, sz * f, 4));
                    rolling = Mathf.Pow(rolling, flattenExponent);

                    // (b) The range: skeleton height (summits, col, spurs, knolls) plus ridged detail that is strongest
                    // high up (crags, gullies) and absent in the lowland.
                    // A finer warp bends the ridge lines themselves so spurs wander instead of running dead straight.
                    float fx = SampleGrid(fineX, x / (float)WarpStride, z / (float)WarpStride);
                    float fz = SampleGrid(fineZ, x / (float)WarpStride, z / (float)WarpStride);
                    float s = skeleton.Sample(sx + fx, sz + fz, out float valley);
                    float m = MountainMask.WeightOf(s);
                    float mountains = s;
                    if (s > 0.01f)
                    {
                        // Crags and gullies, but calm on the pass valley floors.
                        float ridged = Ridged(ridgeNoise, sx * ridgeF, sz * ridgeF, 5, sharpness);
                        mountains += (ridged - 0.42f) * 0.3f * LandCover.Smooth(0.02f, 0.55f, s) * (1f - 0.85f * valley);
                    }

                    valleyFloor[z, x] = valley;

                    // (b2) Rock architecture on the upper range: cliff bands (soft terraces on the steep faces) and gullies
                    // down them, away from the pass valleys and the col (the pass road, the inn shelf stay as they were).
                    if (s > SculptStart)
                    {
                        float kx = sx + fx, kz = sz + fz;
                        float protect = skeleton.SampleNear(kx, kz);
                        float colDistance = Mathf.Sqrt((kx - saddle.x) * (kx - saddle.x) + (kz - saddle.y) * (kz - saddle.y));
                        float weightS = LandCover.Smooth(SculptStart, SculptFull, s) * (1f - protect) * LandCover.Smooth(ColKeepOut, ColKeepOut + 80f, colDistance);
                        if (weightS > 0.001f)
                        {
                            float gxs = (skeleton.Sample(kx + 4f, kz, out _) - skeleton.Sample(kx - 4f, kz, out _)) * (amplitudeM / 8f);
                            float gzs = (skeleton.Sample(kx, kz + 4f, out _) - skeleton.Sample(kx, kz - 4f, out _)) * (amplitudeM / 8f);
                            float face = Mathf.Sqrt(gxs * gxs + gzs * gzs);
                            float metres = Mathf.Max(0f, mountains) * amplitudeM;
                            metres = Sculpt(metres, wx, wz, gxs, gzs, face, weightS, strataNoise, gullyNoise, dipX, dipZ);
                            mountains = metres / amplitudeM;
                        }
                    }

                    // (c) Escarpments: a steep step where a low-frequency noise crosses a threshold.
                    float e = escarpNoise.Sample(wx * escarpF, wz * escarpF);
                    float step = Mathf.Clamp01((e - 0.12f) / 0.05f);
                    // A second noise gates the step so escarpments are broken ridges of a few hundred metres, not
                    // endless contour lines; where the gate fades the step becomes a gentle ramp.
                    float gate = Mathf.Clamp01((escarpGate.Fbm(wx * escarpF * 1.7f, wz * escarpF * 1.7f, 2) + 0.05f) / 0.3f);
                    gate = gate * gate * (3f - 2f * gate);
                    float escarpment = step * step * (3f - 2f * step) * gate * (1f - 0.8f * m);

                    float low = rolling * p.rollingAmplitude + escarpment * p.escarpmentHeight;
                    lowland[z, x] = low;
                    weight[z, x] = m;
                    map[z, x] = low + Mathf.Max(0f, mountains) * p.mountainAmplitude;
                }
            }

            // Highland weight grid for later stages (the same warped skeleton the heights were built from).
            int g = MountainMask.GridSize;
            var maskGrid = new float[g * g];
            for (int j = 0; j < g; j++)
            {
                for (int i = 0; i < g; i++)
                {
                    maskGrid[j * g + i] = SampleGrid(weight, mask.GridCoordinate(i) / spacing, mask.GridCoordinate(j) / spacing);
                }
            }

            mask.SetGrid(maskGrid);

            // Drainage valleys cut into the range from the lowland (dendritic, deepening downstream).
            CarveDrainage(map, lowland, weight, valleyFloor, spacing, p.valleyCarving);

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float h = map[z, x];
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }

            // Normalise land to [reserve, 1] so lake beds can be carved below the lowest land.
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);
            bool lakes = p.lakeCoverage > 0f;
            float reserve = lakes ? Mathf.Min(LakeDepth / amplitude, 0.1f) : 0f;
            float range = max - min;
            float inv = range > 1e-6f ? (1f - reserve) / range : 0f;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++) map[z, x] = reserve + (map[z, x] - min) * inv;
            }

            // Smooth the gentle ground (ridges, cliffs and escarpments keep their edges).
            SmoothLowSlopes(map, spacing, amplitude);

            float waterHeight = -1f;
            if (lakes)
            {
                float level = Percentile(map, Mathf.Clamp(p.lakeCoverage, 0f, 0.5f));
                float shelf = 3f / amplitude;
                for (int z = 0; z < n; z++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float v = map[z, x];
                        if (v >= level) continue;
                        float t = Mathf.Clamp01((level - v) / shelf);
                        map[z, x] = Mathf.Max(0f, v - reserve * t * t * (3f - 2f * t));
                    }
                }

                waterHeight = level * amplitude;
            }

            return new Relief(p, map, waterHeight, mask);
        }

        /// <summary>Amplitude (metres) of the fine warp applied to the range skeleton lookup.</summary>
        public const float FineWarp = 30f;

        /// <summary>Frequency (cycles per metre) of the fine skeleton warp.</summary>
        public const float FineWarpFrequency = 1f / 240f;

        /// <summary>Grid spacing (metres) of the drainage analysis in <see cref="CarveDrainage"/>.</summary>
        public const float DrainageCell = 8f;

        /// <summary>
        /// Cuts drainage valleys into the highland: on a coarse (~<see cref="DrainageCell"/> m) copy of <paramref name="map"/>
        /// (metres), fills depressions (priority flood), routes flow downhill (D8) and accumulates the drained area; each cell
        /// is lowered by k·√area (k grows with <paramref name="strength"/>), limited to 45 % of its height above the
        /// <paramref name="lowland"/> surface and faded out where the highland <paramref name="weight"/> is low, so channels
        /// deepen downstream but never dig below the plain; the cut is blurred a little (valley floors, not slots) and
        /// subtracted at full resolution; nothing is cut where <paramref name="keepFloor"/> is 1 (pass valley floors stay smooth
        /// for the road). Deterministic; modifies <paramref name="map"/> in place.
        /// </summary>
        public static void CarveDrainage(float[,] map, float[,] lowland, float[,] weight, float[,] keepFloor, float spacing, float strength)
        {
            if (strength <= 0f) return;
            int n = map.GetLength(0);
            int stride = Mathf.Max(1, Mathf.RoundToInt(DrainageCell / Mathf.Max(0.01f, spacing)));
            int m = (n - 1) / stride + 1;
            if (m < 4) return;
            float cs = spacing * stride;
            int count = m * m;
            var h = new float[count];
            var filled = new float[count];
            var closed = new bool[count];
            for (int z = 0; z < m; z++)
            {
                for (int x = 0; x < m; x++) h[z * m + x] = map[z * stride, x * stride];
            }

            // Priority flood from the border: every cell ends up draining to the edge of the map.
            var heap = new MinHeap(count);
            for (int z = 0; z < m; z++)
            {
                for (int x = 0; x < m; x++)
                {
                    if (x != 0 && z != 0 && x != m - 1 && z != m - 1) continue;
                    int c = z * m + x;
                    filled[c] = h[c];
                    closed[c] = true;
                    heap.Push(c, h[c]);
                }
            }

            const float Epsilon = 1e-3f;
            var order = new int[count];
            int ordered = 0;
            while (heap.Count > 0)
            {
                int c = heap.Pop();
                order[ordered++] = c;
                int cx = c % m;
                int cz = c / m;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx;
                        int z = cz + dz;
                        if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= m || z >= m) continue;
                        int k = z * m + x;
                        if (closed[k]) continue;
                        closed[k] = true;
                        filled[k] = Mathf.Max(h[k], filled[c] + Epsilon);
                        heap.Push(k, filled[k]);
                    }
                }
            }

            // Accumulate drained area from the highest filled cell down (reverse flood order is a valid topological order).
            var area = new float[count];
            for (int i = 0; i < count; i++) area[i] = cs * cs;
            for (int o = ordered - 1; o >= 0; o--)
            {
                int c = order[o];
                int cx = c % m;
                int cz = c / m;
                int receiver = -1;
                float steepest = 0f;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx;
                        int z = cz + dz;
                        if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= m || z >= m) continue;
                        int k = z * m + x;
                        float drop = (filled[c] - filled[k]) / (dx != 0 && dz != 0 ? 1.41421356f : 1f);
                        if (drop > steepest)
                        {
                            steepest = drop;
                            receiver = k;
                        }
                    }
                }

                if (receiver >= 0) area[receiver] += area[c];
            }

            // Channel depth, limited to part of the height above the plain, only in the highland.
            float k0 = 0.13f * Mathf.Clamp01(strength);
            var depth = new float[count];
            for (int z = 0; z < m; z++)
            {
                for (int x = 0; x < m; x++)
                {
                    int c = z * m + x;
                    float above = h[c] - lowland[z * stride, x * stride];
                    if (above <= 0f) continue;
                    float w = LandCover.Smooth(0.05f, 0.5f, weight[z * stride, x * stride]) * (1f - keepFloor[z * stride, x * stride]);
                    depth[c] = Mathf.Min(k0 * Mathf.Sqrt(area[c]), 0.45f * above) * w;
                }
            }

            // Two binomial passes: valley floors a few cells wide rather than slots.
            var tmp = new float[count];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int z = 0; z < m; z++)
                {
                    for (int x = 0; x < m; x++)
                    {
                        float sum = 0f;
                        float wsum = 0f;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                int zz = z + dz;
                                if (xx < 0 || zz < 0 || xx >= m || zz >= m) continue;
                                float wk = (dx == 0 ? 2f : 1f) * (dz == 0 ? 2f : 1f);
                                sum += depth[zz * m + xx] * wk;
                                wsum += wk;
                            }
                        }

                        tmp[z * m + x] = sum / wsum;
                    }
                }

                var swap = depth;
                depth = tmp;
                tmp = swap;
            }

            var coarse = new float[m, m];
            for (int z = 0; z < m; z++)
            {
                for (int x = 0; x < m; x++) coarse[z, x] = depth[z * m + x];
            }

            float inv = 1f / stride;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++) map[z, x] -= SampleGrid(coarse, x * inv, z * inv);
            }
        }

        /// <summary>Binary min-heap of cell indices keyed by height (ties by index, so the order is deterministic).</summary>
        private sealed class MinHeap
        {
            private readonly int[] items;
            private readonly float[] keys;

            public MinHeap(int capacity)
            {
                items = new int[capacity];
                keys = new float[capacity];
            }

            public int Count { get; private set; }

            public void Push(int item, float key)
            {
                int i = Count++;
                while (i > 0)
                {
                    int up = (i - 1) >> 1;
                    if (Less(keys[up], items[up], key, item)) break;
                    items[i] = items[up];
                    keys[i] = keys[up];
                    i = up;
                }

                items[i] = item;
                keys[i] = key;
            }

            public int Pop()
            {
                int top = items[0];
                int lastItem = items[--Count];
                float lastKey = keys[Count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && Less(keys[child + 1], items[child + 1], keys[child], items[child])) child++;
                    if (!Less(keys[child], items[child], lastKey, lastItem)) break;
                    items[i] = items[child];
                    keys[i] = keys[child];
                    i = child;
                }

                if (Count > 0)
                {
                    items[i] = lastItem;
                    keys[i] = lastKey;
                }

                return top;
            }

            private static bool Less(float ka, int ia, float kb, int ib) => ka < kb || (ka == kb && ia < ib);
        }

        /// <summary>
        /// The <see cref="MountainRange"/> skeleton sampled on a ~8 m grid covering the map plus <c>pad</c> metres on every
        /// side (domain-warped coordinates reach beyond the map), bilinearly interpolated.
        /// </summary>
        private sealed class SkeletonGrid
        {
            private float[,] values;
            private float[,] valleys;
            private float[,] near;
            private float origin;
            private float cell;

            public static SkeletonGrid Build(MountainRange range, float worldSize, float pad)
            {
                float extent = worldSize + 2f * pad;
                int res = Mathf.Max(8, Mathf.CeilToInt(extent / 8f) + 1);
                var grid = new SkeletonGrid { values = new float[res, res], valleys = new float[res, res], near = new float[res, res], origin = -pad, cell = extent / (res - 1) };
                for (int z = 0; z < res; z++)
                {
                    float wz = grid.origin + z * grid.cell;
                    for (int x = 0; x < res; x++)
                    {
                        float wx = grid.origin + x * grid.cell;
                        grid.values[z, x] = range.Evaluate(wx, wz, out float valley);
                        grid.valleys[z, x] = valley;
                        grid.near[z, x] = grid.values[z, x] > SculptStart
                            ? 1f - LandCover.Smooth(ValleyKeepOut, ValleyKeepOut + 60f, range.ValleyDistance(wx, wz, out _, out _))
                            : 0f;
                    }
                }

                return grid;
            }

            /// <summary>1 on and beside the pass valleys (no sculpting), fading to 0 beyond <see cref="ValleyKeepOut"/> + 60 m.</summary>
            public float SampleNear(float x, float z) => SampleGrid(near, (x - origin) / cell, (z - origin) / cell);

            public float Sample(float x, float z, out float valley)
            {
                float gx = (x - origin) / cell;
                float gz = (z - origin) / cell;
                valley = SampleGrid(valleys, gx, gz);
                return SampleGrid(values, gx, gz);
            }
        }

        /// <summary>Skeleton height (fraction of the mountain amplitude) where the rock architecture starts.</summary>
        public const float SculptStart = 0.28f;

        /// <summary>Skeleton height where the rock architecture is at full strength.</summary>
        public const float SculptFull = 0.5f;

        /// <summary>No sculpting within this many metres of a pass valley centre line (fades in over the next 60 m).</summary>
        public const float ValleyKeepOut = 65f;

        /// <summary>No sculpting within this many metres of the col (fades in over the next 80 m): the inn shelf and the pass.</summary>
        public const float ColKeepOut = 110f;

        /// <summary>Height of one cliff band (strata step), metres.</summary>
        public const float StrataStep = 13f;

        /// <summary>Tilt of the strata (metres of height per metre across the range).</summary>
        public const float StrataDip = 0.02f;

        /// <summary>Fraction of a strata step taken by its riser (the cliff); the rest is a ledge.</summary>
        public const float RiserFraction = 0.36f;

        /// <summary>Ledge slope as a fraction of the original face slope.</summary>
        public const float LedgeSlope = 0.3f;

        /// <summary>Deepest gully cut into a face, metres.</summary>
        public const float GullyDepth = 6f;

        /// <summary>
        /// Rock architecture for one point of the upper range: <paramref name="metres"/> is the mountain height there, (gx, gz) the
        /// gradient of the smooth skeleton (rise over run) and <paramref name="face"/> its magnitude. Steep faces become soft
        /// terraces (<see cref="StrataStep"/> bands: a ledge at <see cref="LedgeSlope"/> of the slope, then a riser about three
        /// times as steep), the band heights wandering with noise and a seeded dip so they do not look machined; gullies (narrow
        /// channels running straight down the face, ~30 m apart) are cut up to <see cref="GullyDepth"/> deep. Weighted by
        /// <paramref name="weight"/> (upper range, away from the pass). Pure.
        /// </summary>
        public static float Sculpt(float metres, float x, float z, float gx, float gz, float face, float weight,
            GradientNoise strata, GradientNoise gullies, float dipX, float dipZ)
        {
            return Sculpt(metres, x, z, gx, gz, face, weight, strata, gullies, dipX, dipZ, 1f);
        }

        /// <summary>
        /// <see cref="Sculpt(float, float, float, float, float, float, float, GradientNoise, GradientNoise, float, float)"/> with the
        /// gullies scaled by <paramref name="gullyScale"/> (0 = cliff bands only).
        /// </summary>
        public static float Sculpt(float metres, float x, float z, float gx, float gz, float face, float weight,
            GradientNoise strata, GradientNoise gullies, float dipX, float dipZ, float gullyScale)
        {
            // Cliff bands on faces steeper than ~20 degrees.
            float bandWeight = weight * LandCover.Smooth(0.3f, 0.6f, face);
            if (bandWeight > 0f)
            {
                float offset = 8f * strata.Fbm(x * 0.008f, z * 0.008f, 2) + dipX * x + dipZ * z;
                float h = (metres + offset) / StrataStep;
                float band = Mathf.Floor(h);
                float f = h - band;
                float f0 = 1f - RiserFraction;
                float t = f < f0 ? LedgeSlope * f : LedgeSlope * f0 + (1f - LedgeSlope * f0) * SmoothStep((f - f0) / RiserFraction);
                // Some bands are weaker (irregular strata), set by a second octave of the noise.
                float strength = 0.4f + 0.6f * Mathf.Clamp01(0.5f + 1.5f * strata.Sample(x * 0.012f + 31f, z * 0.012f - 17f));
                metres += bandWeight * strength * ((band + t) * StrataStep - (metres + offset));
            }

            // Gullies: channels straight down the fall line, on faces steeper than ~25 degrees.
            float gullyWeight = weight * LandCover.Smooth(0.45f, 0.8f, face) * gullyScale;
            if (gullyWeight > 0f && face > 1e-4f)
            {
                float ux = gx / face, uz = gz / face;           // uphill
                float across = (x * -uz + z * ux) / 30f;         // along the contour: channel spacing
                float down = (x * ux + z * uz) / 150f;           // down the fall line: channels stay straight
                float n = gullies.Sample(across, down);
                float channel = 1f - Mathf.Clamp01(Mathf.Abs(n) / 0.22f);
                metres -= GullyDepth * gullyWeight * channel * channel;
            }

            return metres;
        }

        private static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Slope (rise over run) below which <see cref="SmoothLowSlopes"/> smooths fully.</summary>
        public const float SmoothSlopeStart = 0.3f;

        /// <summary>Slope (rise over run) above which <see cref="SmoothLowSlopes"/> leaves the ground untouched.</summary>
        public const float SmoothSlopeEnd = 0.6f;

        /// <summary>
        /// Removes high-frequency bumps from gentle ground: <paramref name="passes"/> passes of a 3×3 binomial blur,
        /// blended in by slope (full below <see cref="SmoothSlopeStart"/>, none above <see cref="SmoothSlopeEnd"/>,
        /// slope measured on the unsmoothed map) so ridges, cliffs and escarpments stay sharp. Smooth ground keeps
        /// the terrain LOD close to the true surface, so detail meshes (grass) do not float at mid distance.
        /// Pure and deterministic; modifies <paramref name="map"/> in place.
        /// </summary>
        public static void SmoothLowSlopes(float[,] map, float spacing, float heightScale, int passes = 2)
        {
            int n = map.GetLength(0);
            int m = map.GetLength(1);
            if (n < 3 || m < 3 || passes <= 0) return;

            // Blend weight from the original slope.
            var weight = new float[n, m];
            float inv = heightScale / (2f * Mathf.Max(1e-4f, spacing));
            for (int z = 0; z < n; z++)
            {
                int z0 = Mathf.Max(0, z - 1);
                int z1 = Mathf.Min(n - 1, z + 1);
                for (int x = 0; x < m; x++)
                {
                    int x0 = Mathf.Max(0, x - 1);
                    int x1 = Mathf.Min(m - 1, x + 1);
                    float gx = (map[z, x1] - map[z, x0]) * inv * 2f / (x1 - x0);
                    float gz = (map[z1, x] - map[z0, x]) * inv * 2f / (z1 - z0);
                    float slope = Mathf.Sqrt(gx * gx + gz * gz);
                    float t = Mathf.Clamp01((slope - SmoothSlopeStart) / (SmoothSlopeEnd - SmoothSlopeStart));
                    weight[z, x] = 1f - t * t * (3f - 2f * t);
                }
            }

            var src = (float[,])map.Clone();
            var dst = new float[n, m];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int z = 0; z < n; z++)
                {
                    int z0 = Mathf.Max(0, z - 1);
                    int z1 = Mathf.Min(n - 1, z + 1);
                    for (int x = 0; x < m; x++)
                    {
                        int x0 = Mathf.Max(0, x - 1);
                        int x1 = Mathf.Min(m - 1, x + 1);
                        float row0 = src[z0, x0] + 2f * src[z0, x] + src[z0, x1];
                        float row1 = src[z, x0] + 2f * src[z, x] + src[z, x1];
                        float row2 = src[z1, x0] + 2f * src[z1, x] + src[z1, x1];
                        dst[z, x] = (row0 + 2f * row1 + row2) * (1f / 16f);
                    }
                }

                var swap = src;
                src = dst;
                dst = swap;
            }

            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < m; x++)
                {
                    float w = weight[z, x];
                    if (w > 0f) map[z, x] += (src[z, x] - map[z, x]) * w;
                }
            }
        }

        /// <summary>Ridged multifractal (Musgrave): sharp crests, each octave weighted by the previous one. 0..1.</summary>
        public static float Ridged(GradientNoise noise, float x, float y, int octaves, float sharpness)
        {
            float sum = 0f;
            float amplitude = 1f;
            float norm = 0f;
            float weight = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float r = 1f - Mathf.Abs(noise.Sample(x, y) * 1.4f);
                r = Mathf.Pow(Mathf.Clamp01(r), sharpness);
                r *= weight;
                weight = Mathf.Clamp01(r * 1.5f);
                sum += r * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                x = x * 2.03f + 7.1f;
                y = y * 2.03f - 3.3f;
            }

            return sum / norm;
        }

        private static float Percentile(float[,] map, float fraction)
        {
            const int Bins = 4096;
            var histogram = new int[Bins];
            int n = map.GetLength(0);
            int total = 0;
            for (int z = 0; z < n; z += 2)
            {
                for (int x = 0; x < n; x += 2)
                {
                    histogram[Mathf.Clamp((int)(map[z, x] * (Bins - 1)), 0, Bins - 1)]++;
                    total++;
                }
            }

            int target = Mathf.RoundToInt(total * fraction);
            int cumulative = 0;
            for (int b = 0; b < Bins; b++)
            {
                cumulative += histogram[b];
                if (cumulative >= target) return (b + 0.5f) / (Bins - 1);
            }

            return 1f;
        }

        /// <summary>Bilinear sample in grid units (clamped to the map).</summary>
        public static float SampleGrid(float[,] map, float gx, float gz)
        {
            int n = map.GetLength(0);
            gx = Mathf.Clamp(gx, 0f, n - 1);
            gz = Mathf.Clamp(gz, 0f, n - 1);
            int x0 = Mathf.Min((int)gx, n - 2);
            int z0 = Mathf.Min((int)gz, n - 2);
            float tx = gx - x0;
            float tz = gz - z0;
            float a = map[z0, x0] + (map[z0, x0 + 1] - map[z0, x0]) * tx;
            float b = map[z0 + 1, x0] + (map[z0 + 1, x0 + 1] - map[z0 + 1, x0]) * tx;
            return a + (b - a) * tz;
        }

        /// <summary>Bilinear normalised height (0..1) at world position (x, z).</summary>
        public static float SampleNormalized(float[,] map, float worldSize, float x, float z)
        {
            float s = (map.GetLength(0) - 1) / worldSize;
            return SampleGrid(map, x * s, z * s);
        }

        /// <summary>Bilinear height in metres at world position (x, z).</summary>
        public static float SampleHeight(float[,] map, float worldSize, float heightScale, float x, float z)
        {
            return SampleNormalized(map, worldSize, x, z) * heightScale;
        }

        /// <summary>Bilinear height in metres at world position (x, z) for the world described by <paramref name="p"/>.</summary>
        public static float SampleHeight(float[,] map, WorldGenParams p, float x, float z)
        {
            return SampleNormalized(map, p.worldSize, x, z) * p.hillAmplitude;
        }

        /// <summary>Terrain gradient magnitude (rise over run, 1 = 45 degrees) at world (x, z), central differences.</summary>
        public static float SampleSlope(float[,] map, float worldSize, float heightScale, float x, float z)
        {
            float step = worldSize / (map.GetLength(0) - 1);
            float hx = SampleNormalized(map, worldSize, x + step, z) - SampleNormalized(map, worldSize, x - step, z);
            float hz = SampleNormalized(map, worldSize, x, z + step) - SampleNormalized(map, worldSize, x, z - step);
            float inv = heightScale / (2f * step);
            return Mathf.Sqrt(hx * hx + hz * hz) * inv;
        }
    }
}
