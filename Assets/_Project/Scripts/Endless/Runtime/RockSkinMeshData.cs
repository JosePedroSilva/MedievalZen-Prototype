using System;
using System.Collections.Generic;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The faceted rock skin of a near tile in a pass range (second version, 2026-10-06, endless only; it replaces the thin triplanar copy made earlier that day)
    /// as plain mesh data, built on the generating thread. Pure: no Unity objects.
    /// <para>The pass slopes read as smooth sand dunes because the ground is a smooth 2 m height grid: paint cannot give it form. The skin
    /// gives the rock geometry in the low-poly Synty style: an irregular triangulation (a lattice of <see cref="LatticeCells"/> cells per
    /// tile, every point jittered, the diagonal of each quad the shorter one) over the range's rock, from moderately steep ground (about
    /// 27°) to vertical, displaced outward only along the smoothed ground normal by blocky, strata-aligned relief: tilted strata
    /// <see cref="StrataHeight"/> high with a ledge or an overhang at each bed, blocks along each bed, a little noise per point. Up to
    /// <see cref="CliffRelief"/> per lattice step (up to <see cref="MaxCliffRelief"/>) on faces too steep to walk, at most <see cref="WalkableRelief"/> (plus <see cref="Lift"/>) where the
    /// player can stand, so feet do not sink. The relief fades to zero at the skin's edge (a vertex touching ground without skin has
    /// none), so the skin meets the terrain by geometry, not by alpha. It never lies below the terrain: every vertex sits on the
    /// terrain's own triangle surface plus an outward offset, and faces the terrain bulges through are lifted (interior vertices only).</para>
    /// <para>Each triangle has its own vertices, its flat normal and one colour (linear → sRGB): the mountain rock's distant colour
    /// (<see cref="GroundLook"/>, as the far tiles' rock zone) with strata tone bands (<see cref="StrataTone"/>, shared with the far tiles),
    /// darker in recesses and under overhangs, lighter on outstanding blocks and upward ledges, leaning to heath on ledges with heath paint
    /// and to the splat's own mix where the paint is not rock. Seams: the lattice is global (its points on a tile's edge move only along
    /// the edge; the rock field and the heights come from the generator's padded window), so neighbouring tiles build identical vertices
    /// along their shared edge. Randomness streams 1346-1352.</para>
    /// </summary>
    public sealed class RockSkinMeshData
    {
        /// <summary>Lattice cells per tile side (256 m tiles: 1.78 m, about the facet size of a Synty rock face).</summary>
        public const int LatticeCells = 144;

        /// <summary>The player's slope limit (degrees): steeper ground cannot be walked, so it may carry the full relief.</summary>
        public const float WalkableSlope = 45f;

        /// <summary>
        /// Relief (metres, outward) on walkable rock, and on unwalkable faces per metre of lattice step along the face: the lattice is
        /// horizontal, so on a 60° face its rows lie 3.6 m apart up the face and the relief grows with that stretch (up to
        /// <see cref="MaxCliffRelief"/>), else the facets would tilt too little to read. It ramps in between 45° and 57°.
        /// </summary>
        public const float WalkableRelief = 0.14f, CliffRelief = 1.7f, MaxCliffRelief = 3.0f;

        /// <summary>Base lift of the skin along the ground's normal, metres (with the shader's depth offset, against z-fighting).</summary>
        public const float Lift = 0.06f;

        /// <summary>Height of one stratum (bed) of the rock, metres.</summary>
        public const float StrataHeight = 3.0f;

        /// <summary>Most triangles of skin per tile; above it the coverage threshold rises until the skin fits.</summary>
        public const int MaxTriangles = 25000;

        /// <summary>Rockiness (0..1) above which a lattice triangle carries skin (dithered ±0.08 per triangle).</summary>
        public const float CoverThreshold = 0.32f;

        /// <summary>Randomness streams: point jitter, coverage dither, blocks, strata, point relief, face colour, strata tone.</summary>
        public const int JitterStream = 1346, CoverStream = 1347, BlockStream = 1348, StrataStream = 1349, PointStream = 1350, ColorStream = 1351, ToneStream = 1352;

        /// <summary>Vertex layout: position, flat face normal, colour (sRGB, alpha 255).</summary>
        public struct Vertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Color32 color;
        }

        public Vertex[] Vertices;
        public int[] Indices;
        public int VertexCount, IndexCount;
        public Vector3 BoundsMin, BoundsMax;

        /// <summary>Statistics: triangles, horizontal area covered (m²), largest relief on walkable ground and anywhere (metres).</summary>
        public int Triangles;
        public float CoveredArea, MaxWalkableRelief, MaxRelief;

        /// <summary>Statistics: mean angle (degrees) between a face and the smoothed ground under it, on unwalkable and on walkable rock.</summary>
        public float SteepFacetTilt, WalkableFacetTilt;

        /// <summary>Statistics: mean relief fade (0..1) of the skin's points on unwalkable ground inside the tile.</summary>
        public float SteepFade;
        private double steepTiltSum, steepTiltCount, flatTiltSum, flatTiltCount;

        /// <summary>The skin's vertices on the tile's edge lines, by global lattice point (gx, gz): tests check neighbours agree.</summary>
        public Dictionary<(long, long), Vector3> EdgePoints = new Dictionary<(long, long), Vector3>();

        /// <summary>What the skin is built from: the generator's padded window and the tile's paint.</summary>
        public sealed class Input
        {
            /// <summary>Normalised heights of the padded window ([z, x], <see cref="Spacing"/> apart, ×<see cref="HeightRange"/> metres).</summary>
            public float[,] Map;
            /// <summary>Rockiness (0..1) at every node of <see cref="Map"/>, row-major (z × width + x).</summary>
            public float[] Rock;
            /// <summary>Grid cells of padding on each side of the tile in <see cref="Map"/>.</summary>
            public int PadCells;
            public float Spacing, HeightRange, TileSize;
            /// <summary>The tile's coordinate (the lattice is global: tile X × <see cref="LatticeCells"/> + i).</summary>
            public int TileX, TileZ;
            public int Seed;
            /// <summary>The tile's paint ([z, x, channel]) and the distant colour of each channel (linear).</summary>
            public float[,,] Splat;
            public Vector3[] ChannelColors;
            /// <summary>Absolute x, z of the tile's corner (strata dip and tone; the lattice does not need them).</summary>
            public double OriginX, OriginZ;
            /// <summary>The paint channel whose colour and share make the skin's rock (the mountain rock; the shared rock on a castle rock, 2026-10-09).</summary>
            public int RockLayer = TerrainShaper.MountainRockLayer;
            /// <summary>Most a point on the skin's rim may be raised where the terrain bulges through a face (metres; unlimited on the passes). The castle
            /// rock's level top meets its cliff in a sharp convex crease, where unlimited lifts left the rim up to 0.7 m off the ground (2026-10-09).</summary>
            public float MaxRimLift = float.PositiveInfinity;
        }

        /// <summary>Strata tone (about 0.88..1.12, mean 1) of rock at absolute (x, y, z); the far tiles' rock faces take the same bands.</summary>
        public static float StrataTone(double x, float y, double z)
        {
            long k = StratumIndex(x, y, z, out _);
            return 1f + 0.12f * (float)(2.0 * EndlessNoise.Unit(0, ToneStream, k) - 1.0);
        }

        /// <summary>The stratum (bed) at absolute (x, y, z) and the fraction (0 at its foot, 1 at its top): beds dip gently and bend.</summary>
        public static long StratumIndex(double x, float y, double z, out float fraction)
        {
            double yd = y + 0.06 * x - 0.035 * z + 1.3 * Math.Sin(x * 0.019) * Math.Cos(z * 0.023);
            double t = yd / StrataHeight;
            double k = Math.Floor(t);
            fraction = (float)(t - k);
            return (long)k;
        }

        /// <summary>Relief amplitude (metres) on ground of this slope (degrees): <see cref="WalkableRelief"/> up to 45°, rising to <see cref="CliffRelief"/> at 57°.</summary>
        public static float Amplitude(float slopeDegrees)
        {
            double cos = Math.Cos(Math.Min(89.0, Math.Max(0.0, slopeDegrees)) * (Math.PI / 180.0));
            float cliff = (float)Math.Min(MaxCliffRelief, CliffRelief / Math.Max(0.05, cos));
            return WalkableRelief + (cliff - WalkableRelief) * TerrainShaper.SmoothStep01((slopeDegrees - WalkableSlope) / 12f);
        }

        /// <summary>Builds the skin; null when the tile has no rock to clad.</summary>
        public static RockSkinMeshData Build(Input input)
        {
            if (input == null || input.Map == null || input.Rock == null || input.Splat == null) return null;
            if (input.Splat.GetLength(2) <= TerrainShaper.MountainRockLayer) return null;
            var f = new Field(input);
            if (!f.AnyRock) return null;
            return new Builder(input, f).Run();
        }

        // ------------------------------------------------------------------ the padded window: terrain surface, normals, rockiness

        private sealed class Field
        {
            private readonly float[,] map;
            private readonly float[] rock;
            private readonly int width, pad;
            private readonly float sp, range;
            public readonly bool AnyRock;

            public Field(Input input)
            {
                map = input.Map;
                rock = input.Rock;
                width = map.GetLength(1);
                pad = input.PadCells;
                sp = Math.Max(1e-3f, input.Spacing);
                range = input.HeightRange;
                for (int k = 0; k < rock.Length && !AnyRock; k++) AnyRock = rock[k] > CoverThreshold - 0.1f;
            }

            private float H(int i, int j)
            {
                i = Math.Max(0, Math.Min(width - 1, i));
                j = Math.Max(0, Math.Min(map.GetLength(0) - 1, j));
                return map[j, i] * range;
            }

            /// <summary>Height (metres) of the terrain's own triangle surface at tile-local (x, z): cells split bottom-left to top-right.</summary>
            public float Height(float x, float z)
            {
                float gx = x / sp + pad, gz = z / sp + pad;
                int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
                float fx = gx - i, fz = gz - j;
                float ha = H(i, j), hb = H(i + 1, j), hc = H(i, j + 1), hd = H(i + 1, j + 1);
                return fx >= fz ? ha + (hb - ha) * fx + (hd - hb) * fz : ha + (hd - hc) * fx + (hc - ha) * fz;
            }

            private Vector3 NodeNormal(int i, int j)
            {
                // Over ±4 m: the smoothed ground, so the relief pushes out of the face, not out of every 2 m crease.
                float dx = (H(i + 2, j) - H(i - 2, j)) / (4f * sp);
                float dz = (H(i, j + 2) - H(i, j - 2)) / (4f * sp);
                float len = (float)Math.Sqrt(dx * dx + 1f + dz * dz);
                return new Vector3(-dx / len, 1f / len, -dz / len);
            }

            private float NodeRock(int i, int j)
            {
                i = Math.Max(0, Math.Min(width - 1, i));
                j = Math.Max(0, Math.Min(map.GetLength(0) - 1, j));
                return rock[j * width + i];
            }

            /// <summary>Smoothed unit normal and rockiness at tile-local (x, z), bilinear between window nodes.</summary>
            public void Sample(float x, float z, out Vector3 normal, out float rockiness)
            {
                float gx = x / sp + pad, gz = z / sp + pad;
                int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
                float fx = gx - i, fz = gz - j;
                Vector3 n = Lerp(Lerp(NodeNormal(i, j), NodeNormal(i + 1, j), fx), Lerp(NodeNormal(i, j + 1), NodeNormal(i + 1, j + 1), fx), fz);
                float len = n.magnitude;
                normal = len > 1e-6f ? n / len : new Vector3(0f, 1f, 0f);
                float r0 = NodeRock(i, j) + (NodeRock(i + 1, j) - NodeRock(i, j)) * fx;
                float r1 = NodeRock(i, j + 1) + (NodeRock(i + 1, j + 1) - NodeRock(i, j + 1)) * fx;
                rockiness = r0 + (r1 - r0) * fz;
            }

            private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        // ------------------------------------------------------------------ the lattice and its faces

        private sealed class Builder
        {
            private const int Ext = 3;   // lattice points beyond the tile on each side (coverage and fades of the neighbours' faces)
            private readonly Input input;
            private readonly Field field;
            private readonly int n, side;
            private readonly float cell;
            private readonly float[] bx, bz, ground, rockiness, slope, amp, relief;
            private readonly Vector3[] normal, pos;
            private readonly bool[] present;   // two triangles per quad
            private readonly bool[] adDiagonal;
            private readonly float[] threshold;

            public Builder(Input input, Field field)
            {
                this.input = input;
                this.field = field;
                n = LatticeCells;
                side = n + 1 + 2 * Ext;
                cell = input.TileSize / n;
                int count = side * side;
                bx = new float[count]; bz = new float[count]; ground = new float[count]; rockiness = new float[count];
                slope = new float[count]; amp = new float[count]; relief = new float[count];
                normal = new Vector3[count]; pos = new Vector3[count];
                int quads = (side - 1) * (side - 1);
                present = new bool[quads * 2];
                adDiagonal = new bool[quads];
                threshold = new float[quads * 2];
            }

            private int P(int i, int j) => (j + Ext) * side + (i + Ext);   // lattice point (i, j), i and j from -Ext to n + Ext
            private int Q(int i, int j) => (j + Ext) * (side - 1) + (i + Ext);   // quad (i, j), from -Ext to n + Ext - 1
            private long GX(int i) => (long)input.TileX * n + i;
            private long GZ(int j) => (long)input.TileZ * n + j;

            public RockSkinMeshData Run()
            {
                Points();
                Quads();
                float raise = 0f;
                int tris;
                while ((tris = Cover(raise, 1f)) > MaxTriangles && raise < 0.6f) raise += 0.04f;
                // Still too many (rock everywhere): thin by a hash of each global triangle, alike in every tile.
                if (tris > MaxTriangles) tris = Cover(raise, 0.98f * MaxTriangles / tris);
                if (tris == 0) return null;
                Relief();
                Unpierce();
                Unpierce();
                RockSkinMeshData data = Emit(tris);
                data.SteepFade = (float)(fadeSum / Math.Max(1.0, fadeCount));
                return data;
            }

            private void Points()
            {
                int seed = input.Seed;
                const double jitter = 0.36;
                for (int j = -Ext; j <= n + Ext; j++)
                {
                    for (int i = -Ext; i <= n + Ext; i++)
                    {
                        long gx = GX(i), gz = GZ(j);
                        // A point on a tile's edge line moves only along that line, so both tiles place it alike.
                        double ux = gx % n == 0 ? 0.0 : EndlessNoise.Unit(seed, JitterStream, gx, gz) - 0.5;
                        double uz = gz % n == 0 ? 0.0 : EndlessNoise.Unit(seed, JitterStream, gx + 1000003L, gz - 7919L) - 0.5;
                        int k = P(i, j);
                        bx[k] = (float)((i + 2.0 * jitter * ux) * cell);
                        bz[k] = (float)((j + 2.0 * jitter * uz) * cell);
                        ground[k] = field.Height(bx[k], bz[k]);
                        field.Sample(bx[k], bz[k], out normal[k], out rockiness[k]);
                        slope[k] = (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, normal[k].y))) * (180.0 / Math.PI));
                        amp[k] = Amplitude(slope[k]);
                    }
                }
            }

            private void Quads()
            {
                int seed = input.Seed;
                for (int j = -Ext; j < n + Ext; j++)
                {
                    for (int i = -Ext; i < n + Ext; i++)
                    {
                        int a = P(i, j), b = P(i + 1, j), c = P(i, j + 1), d = P(i + 1, j + 1);
                        float ad = Sq(bx[d] - bx[a]) + Sq(bz[d] - bz[a]), bc = Sq(bx[c] - bx[b]) + Sq(bz[c] - bz[b]);
                        int q = Q(i, j);
                        adDiagonal[q] = Math.Abs(ad - bc) > 1e-4f ? ad < bc : EndlessNoise.Unit(seed, CoverStream, GX(i) + 31L, GZ(j)) < 0.5;
                        for (int t = 0; t < 2; t++)
                            threshold[q * 2 + t] = CoverThreshold + 0.16f * (float)(EndlessNoise.Unit(seed, CoverStream, GX(i) * 2 + t, GZ(j)) - 0.5);
                    }
                }
            }

            /// <summary>The corners of triangle <paramref name="t"/> of quad (i, j), wound clockwise seen from above.</summary>
            private void Corners(int i, int j, int t, out int p0, out int p1, out int p2)
            {
                int a = P(i, j), b = P(i + 1, j), c = P(i, j + 1), d = P(i + 1, j + 1);
                if (adDiagonal[Q(i, j)])
                {
                    if (t == 0) { p0 = a; p1 = c; p2 = d; } else { p0 = a; p1 = d; p2 = b; }
                }
                else
                {
                    if (t == 0) { p0 = a; p1 = c; p2 = b; } else { p0 = b; p1 = c; p2 = d; }
                }
            }

            /// <summary>Marks the triangles with skin (threshold raised by <paramref name="raise"/>); returns the count inside the tile.</summary>
            private int Cover(float raise, float keep)
            {
                int inside = 0;
                for (int j = -Ext; j < n + Ext; j++)
                {
                    for (int i = -Ext; i < n + Ext; i++)
                    {
                        int q = Q(i, j);
                        for (int t = 0; t < 2; t++)
                        {
                            Corners(i, j, t, out int p0, out int p1, out int p2);
                            float mean = (rockiness[p0] + rockiness[p1] + rockiness[p2]) / 3f;
                            bool on = mean >= threshold[q * 2 + t] + raise && Math.Min(rockiness[p0], Math.Min(rockiness[p1], rockiness[p2])) > 0.05f;
                            if (on && keep < 1f) on = EndlessNoise.Unit(input.Seed, CoverStream, GX(i) * 2 + t + 77777L, GZ(j)) < keep;
                            present[q * 2 + t] = on;
                            if (on && i >= 0 && j >= 0 && i < n && j < n) inside++;
                        }
                    }
                }

                return inside;
            }

            /// <summary>True when every lattice triangle round point (i, j) carries skin.</summary>
            private bool Surrounded(int i, int j)
            {
                for (int qj = j - 1; qj <= j; qj++)
                {
                    for (int qi = i - 1; qi <= i; qi++)
                    {
                        if (qi < -Ext || qj < -Ext || qi >= n + Ext || qj >= n + Ext) return false;
                        int q = Q(qi, qj), me = P(i, j);
                        for (int t = 0; t < 2; t++)
                        {
                            Corners(qi, qj, t, out int p0, out int p1, out int p2);
                            if ((p0 == me || p1 == me || p2 == me) && !present[q * 2 + t]) return false;
                        }
                    }
                }

                return true;
            }

            public double fadeSum, fadeCount;

            private void Relief()
            {
                int seed = input.Seed;
                int count = side * side;
                var inner = innerPoints = new bool[count];
                for (int j = -Ext + 1; j <= n + Ext - 1; j++)
                    for (int i = -Ext + 1; i <= n + Ext - 1; i++) inner[P(i, j)] = Surrounded(i, j);

                for (int j = -Ext + 2; j <= n + Ext - 2; j++)
                {
                    for (int i = -Ext + 2; i <= n + Ext - 2; i++)
                    {
                        int k = P(i, j);
                        float fade = 0f;
                        if (inner[k])
                        {
                            // Half relief next to the skin's edge, full one ring further in; and less where the ground is barely rock.
                            bool ring = true;
                            for (int dj = -1; dj <= 1 && ring; dj++)
                                for (int di = -1; di <= 1 && ring; di++) ring = inner[P(i + di, j + dj)];
                            fade = (ring ? 1f : 0.5f) * TerrainShaper.SmoothStep01((rockiness[k] - 0.25f) / 0.3f);
                        }

                        double ax = input.OriginX + bx[k], az = input.OriginZ + bz[k];
                        long stratum = StratumIndex(ax, ground[k], az, out float frac);
                        // Each bed is a ledge (its top stands out: a step facing up) or an overhang (its foot stands out).
                        bool ledge = EndlessNoise.Unit(seed, StrataStream, stratum) < 0.6;
                        float strength = 0.4f + 0.6f * (float)EndlessNoise.Unit(seed, StrataStream, stratum, 1);
                        float profile = ledge ? (float)Math.Pow(frac, 2.2) : (float)Math.Pow(1f - frac, 2.2);
                        // Blocks along each bed: 2.2 m cells, shifted per bed so the joints do not line up.
                        long cx = (long)Math.Floor((ax + 3.3 * stratum) / 2.2), cz = (long)Math.Floor((az - 2.1 * stratum) / 2.2);
                        float block = (float)EndlessNoise.Unit(seed, BlockStream, cx * 7919L + stratum, cz);
                        // Three levels per block (sunk, mid, standing out), so neighbouring blocks step by up to the full relief.
                        float level = block < 0.4f ? 0f : block < 0.6f ? 0.5f : 1f;
                        float point = (float)EndlessNoise.Unit(seed, PointStream, GX(i), GZ(j));
                        float shape = Math.Min(1f, 0.05f + 0.6f * level + 0.2f * strength * profile + 0.15f * point);
                        relief[k] = amp[k] * shape * fade;
                        if (i >= 0 && j >= 0 && i <= n && j <= n && slope[k] > WalkableSlope && rockiness[k] > 0.05f) { fadeSum += fade; fadeCount++; }
                        pos[k] = new Vector3(bx[k], ground[k], bz[k]) + normal[k] * (Lift + relief[k]);
                    }
                }
            }

            private bool[] innerPoints;   // lattice points with skin all round (the rest are the skin's rim)
            private float[] rimLift;      // how far each rim point has been raised (only with a cap on it)

            private bool RimCapped => !float.IsPositiveInfinity(input.MaxRimLift);

            /// <summary>The weight point <paramref name="k"/> takes in a lift: none on the tile's edge or on a rim point at its cap.</summary>
            private float Movable(int k, float w) => Edge(k) || RimCapped && !innerPoints[k] && rimLift[k] >= input.MaxRimLift ? 0f : w;

            private void Raise(int k, float up)
            {
                if (Edge(k)) return;
                if (RimCapped && !innerPoints[k])
                {
                    up = Math.Min(up, input.MaxRimLift - rimLift[k]);
                    if (up <= 0f) return;
                    rimLift[k] += up;
                }

                pos[k].y += up;
            }

            /// <summary>Lifts faces the terrain bulges through (between their corners) by raising their corners inside the tile.</summary>
            private void Unpierce()
            {
                if (RimCapped && rimLift == null) rimLift = new float[side * side];
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int q = Q(i, j);
                        for (int t = 0; t < 2; t++)
                        {
                            if (!present[q * 2 + t]) continue;
                            Corners(i, j, t, out int p0, out int p1, out int p2);
                            for (int s = 0; s < Samples.Length; s += 3)
                            {
                                float w0 = Samples[s], w1 = Samples[s + 1], w2 = Samples[s + 2];
                                Vector3 p = pos[p0] * w0 + pos[p1] * w1 + pos[p2] * w2;
                                float deficit = field.Height(p.x, p.z) + 0.03f - p.y;
                                if (deficit <= 0f) continue;
                                float movable = Movable(p0, w0) + Movable(p1, w1) + Movable(p2, w2);
                                if (movable < 0.2f) continue;
                                float up = Math.Min(1f, deficit / movable);
                                Raise(p0, up);
                                Raise(p1, up);
                                Raise(p2, up);
                            }
                        }
                    }
                }
            }

            private static readonly float[] Samples =
            {
                1f / 3f, 1f / 3f, 1f / 3f, 0.5f, 0.5f, 0f, 0f, 0.5f, 0.5f, 0.5f, 0f, 0.5f,
                2f / 3f, 1f / 6f, 1f / 6f, 1f / 6f, 2f / 3f, 1f / 6f, 1f / 6f, 1f / 6f, 2f / 3f,
            };

            private bool Edge(int k)
            {
                int i = k % side - Ext, j = k / side - Ext;
                return i <= 0 || j <= 0 || i >= n || j >= n;
            }

            private RockSkinMeshData Emit(int tris)
            {
                int seed = input.Seed;
                var data = new RockSkinMeshData
                {
                    Vertices = new Vertex[tris * 3], Indices = new int[tris * 3], Triangles = tris,
                    BoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), BoundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue),
                };
                Vector3[] colors = input.ChannelColors;
                int rockLayer = input.RockLayer >= 0 && input.RockLayer < colors.Length ? input.RockLayer : TerrainShaper.MountainRockLayer;
                Vector3 rockColor = colors[rockLayer];
                Vector3 heathColor = colors.Length > TerrainShaper.HeathLayer ? colors[TerrainShaper.HeathLayer] : rockColor;
                float[,,] splat = input.Splat;
                int sres = splat.GetLength(0), channels = Math.Min(splat.GetLength(2), colors.Length);
                int v = 0;
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int q = Q(i, j);
                        for (int t = 0; t < 2; t++)
                        {
                            if (!present[q * 2 + t]) continue;
                            Corners(i, j, t, out int p0, out int p1, out int p2);
                            Vector3 a = pos[p0], b = pos[p1], c = pos[p2];
                            Vector3 smooth = normal[p0] + normal[p1] + normal[p2];
                            Vector3 nrm = Vector3.Cross(b - a, c - a);
                            if (Vector3.Dot(nrm, smooth) < 0f)
                            {
                                (b, c) = (c, b);
                                nrm = -nrm;
                            }

                            float len = nrm.magnitude;
                            float slen = smooth.magnitude;
                            if (len > 1e-8f && slen > 1e-6f)
                            {
                                double tilt = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vector3.Dot(nrm, smooth) / (len * slen)))) * (180.0 / Math.PI);
                                if ((slope[p0] + slope[p1] + slope[p2]) / 3f > WalkableSlope) { data.steepTiltSum += tilt; data.steepTiltCount++; }
                                else { data.flatTiltSum += tilt; data.flatTiltCount++; }
                            }

                            nrm = len > 1e-8f ? nrm / len : (smooth.magnitude > 1e-6f ? smooth.normalized : new Vector3(0f, 1f, 0f));
                            data.CoveredArea += 0.5f * Math.Abs((bx[p1] - bx[p0]) * (bz[p2] - bz[p0]) - (bx[p2] - bx[p0]) * (bz[p1] - bz[p0]));

                            // The paint under the face's centre.
                            float cx = (a.x + b.x + c.x) / 3f, cz = (a.z + b.z + c.z) / 3f, cy = (a.y + b.y + c.y) / 3f;
                            int si = Clamp((int)(cx / input.TileSize * sres), 0, sres - 1), sj = Clamp((int)(cz / input.TileSize * sres), 0, sres - 1);
                            float total = 0f;
                            Vector3 mix = Vector3.zero;
                            for (int ch = 0; ch < channels; ch++)
                            {
                                float w = splat[sj, si, ch];
                                total += w;
                                mix += colors[ch] * w;
                            }

                            float rockShare = total > 1e-5f ? splat[sj, si, rockLayer] / total : 0f;
                            float heathShare = total > 1e-5f && splat.GetLength(2) > TerrainShaper.HeathLayer ? splat[sj, si, TerrainShaper.HeathLayer] / total : 0f;
                            mix = total > 1e-5f ? mix / total : rockColor;

                            // Rock: strata bands, relief (recesses dark, outstanding blocks light), ledges up, overhangs down, jitter.
                            float meanRelief = (relief[p0] + relief[p1] + relief[p2]) / 3f;
                            float meanAmp = Math.Max(0.05f, (amp[p0] + amp[p1] + amp[p2]) / 3f);
                            float relTone = 0.86f + 0.4f * Math.Min(1f, meanRelief / meanAmp);
                            float steep = (slope[p0] + slope[p1] + slope[p2]) / 3f;
                            float orient = 1f;
                            if (nrm.y < 0.05f) orient = 0.72f;                                          // overhang: faces down
                            else if (steep > WalkableSlope && nrm.y > 0.75f) orient = 1.1f;              // a ledge on a cliff
                            double fx = input.OriginX + cx, fz = input.OriginZ + cz;
                            float jitter = 1f + 0.12f * (float)(2.0 * EndlessNoise.Unit(seed, ColorStream, GX(i) * 2 + t, GZ(j)) - 1.0);
                            Vector3 rock = rockColor * (StrataTone(fx, cy, fz) * relTone * orient * jitter);
                            if (steep > WalkableSlope && nrm.y > 0.75f) rock = Vector3.Lerp(rock, heathColor * jitter, Math.Min(0.6f, 0.25f + heathShare));
                            Vector3 color = Vector3.Lerp(mix * (jitter * (orient < 1f ? orient : 1f)), rock, Math.Min(1f, rockShare * 1.25f));
                            Color32 c32 = FarTileMeshData.EncodeSrgbFast(color);

                            AddVertex(data, v++, a, nrm, c32);
                            AddVertex(data, v++, b, nrm, c32);
                            AddVertex(data, v++, c, nrm, c32);
                            for (int s = 0; s < 3; s++)
                            {
                                int k = s == 0 ? p0 : s == 1 ? p1 : p2;
                                if (Edge(k)) data.EdgePoints[(GX(k % side - Ext), GZ(k / side - Ext))] = pos[k];
                                if (slope[k] <= WalkableSlope) data.MaxWalkableRelief = Math.Max(data.MaxWalkableRelief, relief[k] + Lift);
                                data.MaxRelief = Math.Max(data.MaxRelief, relief[k] + Lift);
                            }
                        }
                    }
                }

                data.SteepFacetTilt = (float)(data.steepTiltSum / Math.Max(1.0, data.steepTiltCount));
                data.WalkableFacetTilt = (float)(data.flatTiltSum / Math.Max(1.0, data.flatTiltCount));
                for (int k = 0; k < v; k++) data.Indices[k] = k;
                data.VertexCount = v;
                data.IndexCount = v;
                return data;
            }

            private static void AddVertex(RockSkinMeshData d, int k, Vector3 p, Vector3 nrm, Color32 c)
            {
                d.Vertices[k] = new Vertex { position = p, normal = nrm, color = c };
                d.BoundsMin = Vector3.Min(d.BoundsMin, p);
                d.BoundsMax = Vector3.Max(d.BoundsMax, p);
            }

            private static float Sq(float x) => x * x;
            private static int Clamp(int x, int lo, int hi) => x < lo ? lo : x > hi ? hi : x;
        }

        /// <summary>Unit ground normal at grid point (i, j) of a height grid from central differences (one-sided at the edges).</summary>
        public static Vector3 Normal(float[,] h, int n, int i, int j, float sp, float range)
        {
            int i0 = Math.Max(0, i - 1), i1 = Math.Min(n - 1, i + 1), j0 = Math.Max(0, j - 1), j1 = Math.Min(n - 1, j + 1);
            float dx = (h[j, i1] - h[j, i0]) * range / Math.Max(1e-4f, (i1 - i0) * sp);
            float dz = (h[j1, i] - h[j0, i]) * range / Math.Max(1e-4f, (j1 - j0) * sp);
            float len = Mathf.Sqrt(dx * dx + 1f + dz * dz);
            return new Vector3(-dx / len, 1f / len, -dz / len);
        }
    }
}
