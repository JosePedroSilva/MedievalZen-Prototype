using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// One vertex of a bridge's walls, laid out exactly as the GPU vertex buffer (48 bytes) in the order of Unity's
    /// <c>VertexAttribute</c> enum: position (float3), normal (float3), tangent (float4), texture coordinate (float2). No colour:
    /// the bridge stone material (a copy of the Fantasy Kingdom palette material) reads only its atlas, so each stone's tone is a
    /// texture coordinate on one of the atlas's three grey stone cells.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BridgeWallVertex
    {
        /// <summary>Tile-local position (x, z from the tile's minimum corner; y absolute).</summary>
        public Vector3 position;

        /// <summary>Unit face normal (flat shaded: every face has its own vertices).</summary>
        public Vector3 normal;

        /// <summary>Unit tangent in the face's plane (w = 1), for the material's normal map.</summary>
        public Vector4 tangent;

        /// <summary>A point inside one of the atlas's stone cells (the same for the face's every vertex).</summary>
        public Vector2 uv;
    }

    /// <summary>A box collider of a bridge's walls: tile-local centre (y absolute), size (x across, y up, z along), Euler angles in degrees.</summary>
    public struct BridgeWallBox
    {
        public Vector3 Centre, Size;
        public float Pitch, Yaw;
    }

    /// <summary>
    /// The walls of a near tile's bridges as one plain mesh with its box colliders, built on the generating thread (pure, no Unity
    /// objects; <see cref="BridgeWalls.Build(List{TileStreams.BridgeDeck}, Func{double, double, float}, int, double, double, double, double)"/>),
    /// about 1,300 triangles per bridge.
    /// 16-bit indices. The main thread only uploads it, drawn with the bridge stone material.
    /// </summary>
    public sealed class BridgeWallMeshData
    {
        public BridgeWallVertex[] Vertices;
        public ushort[] Indices;
        public int VertexCount, IndexCount;
        /// <summary>Tile-local bounds of every vertex.</summary>
        public Vector3 BoundsMin, BoundsMax;
        /// <summary>Thin boxes along the parapets, one per end post and one per approach wall.</summary>
        public List<BridgeWallBox> Boxes = new List<BridgeWallBox>();
        /// <summary>Tile-local foot vertices of the end posts and approach walls (where they enter the ground), for checks.</summary>
        public List<Vector3> Feet = new List<Vector3>();
        /// <summary>The parapets' coping stones, for checks: x and y the stone's ends along the bridge (from its middle), z the side (±1), w its overhang past the bridge's side, metres.</summary>
        public List<Vector4> CopingStones = new List<Vector4>();
        /// <summary>Total triangles.</summary>
        public int Triangles => IndexCount / 3;
    }

    /// <summary>
    /// The first-party walls that make the Fantasy Kingdom stone bridge read as a bridge from the road and stay stone from the side
    /// (2026-10-06): on each side a coping of individual stones laid on the mesh's own parapet for the full span (0.62–1.08 m long,
    /// each its own height, overhang and atlas grey, flush with the road side and overhanging the bridge's side a few centimetres)
    /// over a thin lining of two courses of blocks on the road side of the mesh's parapet; an end post in three courses with a
    /// hipped cap at each of the four ends; an approach wall beyond each post, splayed outwards and sloping down into the ground
    /// under its own coping stones. Nothing hangs in front of the bridge's side below the coping, so the mesh's own stonework (its
    /// side, string course and arch ring) shows as it did; the coping hides the mesh parapet's top (<see cref="BridgeShape.ParapetTopHigh"/>)
    /// and the lining its inner face. Nothing comes nearer the bridge's axis than <see cref="InnerFace"/>, so the full road stays
    /// clear. Faceted and flat shaded. Deterministic (streams 1386 tones, 1387 coping, 1388 lining, 1389 posts and approach walls); pure.
    /// </summary>
    public static class BridgeWalls
    {
        /// <summary>Randomness stream of the stones' tones.</summary>
        public const int ToneStream = 1386;

        /// <summary>Randomness streams of the coping stones, the lining's blocks, the posts' and approach walls' stones.</summary>
        public const int CopingStream = 1387, LiningStream = 1388, PostStream = 1389;

        /// <summary>Distance of the parapets' (and posts') inner face from the bridge's axis: the 2.5 m half road + 0.3 m, metres.</summary>
        public const float InnerFace = 2.8f;

        /// <summary>The lining's two courses stand back from <see cref="InnerFace"/> by this much (upper, lower), so the coping above reads proud, metres.</summary>
        public const float LiningUpperSet = 0.02f, LiningLowerSet = 0.025f;

        /// <summary>Height of the lining's course joint above the stone deck, metres.</summary>
        public const float LiningCourse = 0.46f;

        /// <summary>The posts' outer face line: just outside the bridge mesh's own side (<see cref="BridgeShape.HalfWidth"/>), metres.</summary>
        public const float OuterFace = BridgeShape.HalfWidth + 0.04f;

        /// <summary>Coping stones: overhang past the bridge's side (least, most), metres.</summary>
        public const float CopingOverhangMin = 0.04f, CopingOverhangMax = 0.08f;

        /// <summary>Coping stones: length along the bridge (mean, least, most), metres.</summary>
        public const float StoneMean = 0.92f, StoneMin = 0.62f, StoneMax = 1.08f;

        /// <summary>Coping stones: thickness (least, most), how far the top clears the mesh parapet's highest point (guide, least), metres.</summary>
        public const float CopingThickMin = 0.13f, CopingThickMax = 0.17f, CopingClear = 0.05f, CopingClearMin = 0.03f;

        /// <summary>Coping stones: the largest random step in height between neighbours (±), metres.</summary>
        public const float CopingJitter = 0.012f;

        /// <summary>
        /// Where the mesh parapet's stepped top dips below a coping stone, a bed face this far inside the bridge's side closes the gap
        /// under the stone, metres.
        /// </summary>
        public const float BedInset = 0.1f;

        /// <summary>Nominal height of the parapet's top above the stone deck (for its colliders), metres.</summary>
        public const float CopingTop = 1.19f;

        /// <summary>End post: length along the road (its outer end just past the bridge's end), how far it stands proud outwards, metres.</summary>
        public const float PostLength = 0.62f, PostBeyondEnd = 0.1f, PostProud = 0.1f;

        /// <summary>End post: body top and cap thickness and overhang, metres (the body top is above the deck at the bridge's end).</summary>
        public const float PostTop = 1.36f, CapThick = 0.13f, CapOverhang = 0.06f;

        /// <summary>End post: course joints above the deck at the bridge's end, the middle course's recess, the cap's hipped rise, metres.</summary>
        public const float PostCourse1 = 0.36f, PostCourse2 = 0.86f, PostRecess = 0.012f, CapRise = 0.07f;

        /// <summary>Approach wall: length, splay outwards from the road's direction (degrees), thickness, metres.</summary>
        public const float WallLength = 3.1f, WallSplay = 20f, WallThick = 0.5f;

        /// <summary>Approach wall: gap between its inner face and the parapet's line where it leaves the post, metres.</summary>
        public const float WallSetBack = 0.08f;

        /// <summary>Approach wall: top above the road where it leaves the post, and above the ground at its far end, metres.</summary>
        public const float WallStartTop = 1.08f, WallEndTop = 0.42f;

        /// <summary>Approach wall: its coping stones' overhang either side and thickness, the course joint below its top, metres.</summary>
        public const float WallCopingOverhang = 0.04f, WallCopingThick = 0.13f, WallCourse = 0.5f;

        /// <summary>Stations along each approach wall (segments + 1).</summary>
        public const int WallStations = 4;

        /// <summary>The posts' and approach walls' feet reach at least this far below the deck at the bridge's end, metres.</summary>
        public const float FootDrop = 0.45f;

        /// <summary>Feet are buried this far below the lowest ground under them, metres.</summary>
        public const float Bury = 0.3f;

        /// <summary>
        /// Simplifying the hump for the parapet's colliders: the chord of two kept stations may pass at most this far below / above a
        /// dropped one, metres.
        /// </summary>
        public const float ChordBelow = 0.012f, ChordAbove = 0.04f;

        /// <summary>The coping between the posts runs from the middle to this far along either way, metres.</summary>
        public const float CopingEnd = BridgeShape.HalfLength - PostLength + PostBeyondEnd;

        // Three grey stone cells of the Fantasy Kingdom atlas (PolygonFantasyKingdom_01_A, measured 2026-10-06; the bridge mesh uses
        // all three): sRGB (80, 76, 73), (86, 84, 79), (99, 96, 92) before the bridge material's warm base colour.
        private const float AtlasU = 0.46f;
        private static readonly float[] AtlasV = { 0.1565f, 0.181f, 0.207f };

        /// <summary>Highest point of the parapet's colliders above the stone deck at <paramref name="along"/> (from the middle).</summary>
        public static float ParapetTop(float along) => BridgeShape.DeckHeight(along) + CopingTop;

        /// <summary>
        /// Stations along the bridge (metres from its middle, increasing) where the parapet's colliders bend: the measured hump's
        /// stations, dropping those that lie nearly on the chord of their neighbours, from one post to the other.
        /// </summary>
        public static List<float> Stations()
        {
            float end = CopingEnd;
            var half = new List<float>();
            for (int i = 1; i < BridgeShape.DeckKnots; i++)
            {
                float a = BridgeShape.DeckKnot(i);
                if (a < end - 0.3f) half.Add(a);
            }

            half.Add(end);
            // Greedy: from each kept station, reach as far as every skipped knot stays near the chord.
            var kept = new List<float> { half[0] };
            int from = 0;
            while (from < half.Count - 1)
            {
                int to = from + 1;
                for (int j = from + 2; j < half.Count; j++)
                {
                    bool ok = true;
                    for (int m = from + 1; m < j && ok; m++)
                    {
                        float t = (half[m] - half[from]) / (half[j] - half[from]);
                        float chord = BridgeShape.DeckHeight(half[from]) + (BridgeShape.DeckHeight(half[j]) - BridgeShape.DeckHeight(half[from])) * t;
                        float off = chord - BridgeShape.DeckHeight(half[m]);
                        ok = off <= ChordAbove && off >= -ChordBelow;
                    }

                    if (!ok) break;
                    to = j;
                }

                kept.Add(half[to]);
                from = to;
            }

            var result = new List<float>();
            for (int i = kept.Count - 1; i >= 0; i--) result.Add(-kept[i]);
            result.AddRange(kept);
            return result;
        }

        /// <summary>
        /// Joints of a course of stones from <paramref name="from"/> to <paramref name="to"/> (increasing, both ends included): lengths
        /// around <paramref name="mean"/>, each within [<paramref name="min"/>, <paramref name="max"/>]. Pure.
        /// </summary>
        public static List<float> Joints(float from, float to, float mean, float min, float max, System.Random rng)
        {
            float span = to - from;
            int n = Math.Max(1, (int)Math.Round(span / mean));
            n = Math.Max(n, (int)Math.Ceiling(span / max - 1e-4f));
            n = Math.Min(n, Math.Max(1, (int)Math.Floor(span / min + 1e-4f)));
            var len = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                len[i] = 1f + ((float)rng.NextDouble() - 0.5f) * 0.44f;
                sum += len[i];
            }

            for (int i = 0; i < n; i++) len[i] *= span / sum;
            // Clamp into range and spread what that changed over the stones still inside it.
            for (int iter = 0; iter < 6; iter++)
            {
                float excess = 0f;
                int free = 0;
                for (int i = 0; i < n; i++)
                {
                    float c = Math.Max(min, Math.Min(max, len[i]));
                    excess += len[i] - c;
                    len[i] = c;
                    if (c > min + 1e-4f && c < max - 1e-4f) free++;
                }

                if (Math.Abs(excess) < 1e-5f || free == 0) break;
                for (int i = 0; i < n; i++)
                {
                    if (len[i] > min + 1e-4f && len[i] < max - 1e-4f) len[i] += excess / free;
                }
            }

            var joints = new List<float>(n + 1) { from };
            float at = from;
            for (int i = 0; i + 1 < n; i++)
            {
                at += len[i];
                joints.Add(at);
            }

            joints.Add(to);
            return joints;
        }

        /// <summary>
        /// The walls of every bridge whose centre lies in the tile [<paramref name="tx"/>, <paramref name="x1"/>) ×
        /// [<paramref name="tz"/>, <paramref name="z1"/>) (drawn once, by that tile, like its deck surface), or null. <paramref name="ground"/>
        /// gives the rendered ground's lowest height at an absolute point.
        /// </summary>
        public static BridgeWallMeshData Build(List<TileStreams.BridgeDeck> decks, Func<double, double, float> ground, int seed, double tx, double tz, double x1, double z1)
        {
            if (decks == null || decks.Count == 0) return null;
            var b = new Builder(ground, tx, tz);
            foreach (TileStreams.BridgeDeck d in decks)
            {
                if (d.X < tx || d.X >= x1 || d.Z < tz || d.Z >= z1) continue;
                b.Bridge(d, seed);
            }

            return b.Finish();
        }

        /// <summary>The walls of one bridge, in the frame of the tile whose minimum corner is (<paramref name="tx"/>, <paramref name="tz"/>).</summary>
        public static BridgeWallMeshData Build(TileStreams.BridgeDeck d, Func<double, double, float> ground, int seed, double tx, double tz)
        {
            var b = new Builder(ground, tx, tz);
            b.Bridge(d, seed);
            return b.Finish();
        }

        private struct Stone
        {
            public float A0, A1, Bottom0, Bottom1;
        }

        private sealed class Builder
        {
            private readonly Func<double, double, float> ground;
            private readonly double tx, tz;
            private readonly List<BridgeWallVertex> vertices = new List<BridgeWallVertex>();
            private readonly List<ushort> indices = new List<ushort>();
            private readonly BridgeWallMeshData data = new BridgeWallMeshData();
            private readonly List<Stone> stones = new List<Stone>();
            private TileStreams.BridgeDeck d;
            private double rx, rz;
            private System.Random tones, coping, lining, posts;
            private int lastTone = -1;

            public Builder(Func<double, double, float> ground, double tx, double tz)
            {
                this.ground = ground;
                this.tx = tx;
                this.tz = tz;
            }

            // Absolute ground at (along, across) of the current bridge.
            private float Ground(float a, float u) => ground(d.X + d.DirX * a + rx * u, d.Z + d.DirZ * a + rz * u);

            // Tile-local point at (along, across, height) of the current bridge.
            private Vector3 P(float a, float u, float y) => new Vector3((float)(d.X + d.DirX * a + rx * u - tx), y, (float)(d.Z + d.DirZ * a + rz * u - tz));

            // World direction of (along, across, up).
            private Vector3 Dir(float a, float u, float y) => new Vector3((float)(d.DirX * a + rx * u), y, (float)(d.DirZ * a + rz * u));

            private float Deck(float a) => d.PivotY + BridgeShape.DeckHeight(Math.Max(-BridgeShape.HalfLength, Math.Min(BridgeShape.HalfLength, a)));

            private float Range(System.Random r, float lo, float hi) => lo + (hi - lo) * (float)r.NextDouble();

            // 0 dark, 1 middle, 2 light grey; a stone of a wall's face or of a coping (mostly light), rarely the same as the last one drawn.
            private int Tone(bool cap)
            {
                int t = 0;
                for (int tries = 0; tries < 2; tries++)
                {
                    double r = tones.NextDouble();
                    t = cap ? (r < 0.6 ? 2 : r < 0.92 ? 1 : 0) : (r < 0.2 ? 0 : r < 0.6 ? 1 : 2);
                    if (t != lastTone) break;
                }

                lastTone = t;
                return t;
            }

            public void Bridge(TileStreams.BridgeDeck deck, int seed)
            {
                d = deck;
                rx = d.DirZ;
                rz = -d.DirX;
                long kx = (long)Math.Floor(d.X * 8.0), kz = (long)Math.Floor(d.Z * 8.0);
                tones = new System.Random(EndlessNoise.Seed(seed, ToneStream, kx, kz));
                coping = new System.Random(EndlessNoise.Seed(seed, CopingStream, kx, kz));
                lining = new System.Random(EndlessNoise.Seed(seed, LiningStream, kx, kz));
                posts = new System.Random(EndlessNoise.Seed(seed, PostStream, kx, kz));
                List<float> st = Stations();
                for (int side = -1; side <= 1; side += 2)
                {
                    Coping(side);
                    Lining(side);
                    ParapetColliders(st, side);
                }

                for (int end = -1; end <= 1; end += 2)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Post(end, side);
                        ApproachWall(end, side);
                    }
                }
            }

            // Largest of f(a) - (v0 + (v1 - v0) t) over the stone [a0, a1], sampled every 5 cm.
            private static float Worst(float a0, float a1, float v0, float v1, Func<float, float> f)
            {
                int n = Math.Max(2, (int)Math.Ceiling((a1 - a0) / 0.05f));
                float worst = float.MinValue;
                for (int j = 0; j <= n; j++)
                {
                    float t = j / (float)n, a = a0 + (a1 - a0) * t;
                    worst = Math.Max(worst, f(a) - (v0 + (v1 - v0) * t));
                }

                return worst;
            }

            // The coping: individual stones on the mesh's own parapet, their top clear of its highest point, flush with the road side.
            private void Coping(int s)
            {
                stones.Clear();
                List<float> joints = Joints(-CopingEnd, CopingEnd, StoneMean, StoneMin, StoneMax, coping);
                float ui = s * InnerFace, ub = s * (BridgeShape.HalfWidth - BedInset);
                Vector3 toRoad = Dir(0f, -s, 0f), away = Dir(0f, s, 0f), fwd = Dir(1f, 0f, 0f), back = Dir(-1f, 0f, 0f);
                float pv = d.PivotY;
                for (int k = 0; k + 1 < joints.Count; k++)
                {
                    float a0 = joints[k], a1 = joints[k + 1];
                    float over = Range(coping, CopingOverhangMin, CopingOverhangMax);
                    float thick = Range(coping, CopingThickMin, CopingThickMax);
                    float jit = Range(coping, -CopingJitter, CopingJitter);
                    float uo = s * (BridgeShape.HalfWidth + over);
                    // Top: a straight stone on the guide, raised until it clears the mesh parapet's highest point everywhere under it.
                    float t0 = pv + BridgeShape.ParapetTopGuide(a0) + CopingClear + jit, t1 = pv + BridgeShape.ParapetTopGuide(a1) + CopingClear + jit;
                    float lift = Worst(a0, a1, t0, t1, a => pv + BridgeShape.ParapetTopHigh(a) + CopingClearMin);
                    if (lift > 0f)
                    {
                        t0 += lift;
                        t1 += lift;
                    }

                    float b0 = t0 - thick, b1 = t1 - thick;
                    stones.Add(new Stone { A0 = a0, A1 = a1, Bottom0 = b0, Bottom1 = b1 });
                    data.CopingStones.Add(new Vector4(a0, a1, s, over));
                    int tone = Tone(true);
                    Quad(P(a0, ui, b0), P(a1, ui, b1), P(a1, ui, t1), P(a0, ui, t0), toRoad, tone);
                    Quad(P(a0, ui, t0), P(a1, ui, t1), P(a1, uo, t1), P(a0, uo, t0), Vector3.up, tone);
                    Quad(P(a0, uo, b0), P(a1, uo, b1), P(a1, uo, t1), P(a0, uo, t0), away, tone);
                    Quad(P(a0, ub, b0), P(a1, ub, b1), P(a1, uo, b1), P(a0, uo, b0), Vector3.down, tone);
                    // The joints: each stone's own end faces (the posts hide the two at the ends of the run).
                    if (k > 0) Quad(P(a0, ui, b0), P(a0, uo, b0), P(a0, uo, t0), P(a0, ui, t0), back, tone);
                    if (k + 2 < joints.Count) Quad(P(a1, ui, b1), P(a1, uo, b1), P(a1, uo, t1), P(a1, ui, t1), fwd, tone);

                    // Where the mesh parapet's stepped top lies below the stone, a bed face just inside the bridge's side closes the gap.
                    // Worst: the largest height of the stone's bottom above the mesh parapet's low top under it.
                    float gap = Worst(a0, a1, -b0, -b1, a => -(pv + BridgeShape.ParapetTopLow(a)));
                    if (gap > 0f)
                    {
                        float drop = gap + 0.03f;
                        Quad(P(a0, ub, b0 - drop), P(a1, ub, b1 - drop), P(a1, ub, b1), P(a0, ub, b0), away, Tone(false));
                    }
                }
            }

            // Bottom of the coping above a point along the parapet (the stone over it).
            private float CopingBottom(float a)
            {
                foreach (Stone st in stones)
                {
                    if (a <= st.A1 + 1e-4f) return st.Bottom0 + (st.Bottom1 - st.Bottom0) * Math.Max(0f, Math.Min(1f, (a - st.A0) / (st.A1 - st.A0)));
                }

                Stone last = stones[stones.Count - 1];
                return last.Bottom1;
            }

            // The lining: two courses of blocks on the road side of the mesh's parapet, from just under the deck into the coping.
            private void Lining(int s)
            {
                Vector3 toRoad = Dir(0f, -s, 0f);
                float ul = s * (InnerFace + LiningLowerSet), uu = s * (InnerFace + LiningUpperSet);
                List<float> lower = Joints(-CopingEnd, CopingEnd, StoneMean, StoneMin, StoneMax, lining);
                for (int k = 0; k + 1 < lower.Count; k++)
                {
                    float a0 = lower[k], a1 = lower[k + 1];
                    float c0 = Deck(a0) + LiningCourse, c1 = Deck(a1) + LiningCourse;
                    Quad(P(a0, ul, Deck(a0) - 0.03f), P(a1, ul, Deck(a1) - 0.03f), P(a1, ul, c1), P(a0, ul, c0), toRoad, Tone(false));
                }

                // The upper course has joints of its own (rarely over the lower ones); it laps 2 cm over the lower, 5 mm proud of it.
                List<float> upper = Joints(-CopingEnd, CopingEnd, StoneMean, StoneMin, StoneMax, lining);
                for (int k = 0; k + 1 < upper.Count; k++)
                {
                    float a0 = upper[k], a1 = upper[k + 1];
                    float c0 = Deck(a0) + LiningCourse - 0.02f, c1 = Deck(a1) + LiningCourse - 0.02f;
                    float t0 = CopingBottom(a0) + 0.06f, t1 = CopingBottom(a1) + 0.06f;
                    float lift = Worst(a0, a1, t0, t1, a => CopingBottom(a) + 0.02f);
                    if (lift > 0f)
                    {
                        t0 += lift;
                        t1 += lift;
                    }

                    Quad(P(a0, uu, c0), P(a1, uu, c1), P(a1, uu, t1), P(a0, uu, t0), toRoad, Tone(false));
                }
            }

            // A thin box on the road side of each parapet segment between the hump's stations, its top along the nominal coping.
            private void ParapetColliders(List<float> st, int s)
            {
                for (int k = 0; k + 1 < st.Count; k++)
                {
                    float a0 = st[k], a1 = st[k + 1];
                    float d0 = Deck(a0), d1 = Deck(a1), t0 = d0 + CopingTop, t1 = d1 + CopingTop;
                    float am = 0.5f * (a0 + a1), run = a1 - a0, rise = t1 - t0;
                    float len = (float)Math.Sqrt(run * run + rise * rise);
                    float height = CopingTop + 0.1f;
                    data.Boxes.Add(new BridgeWallBox
                    {
                        Centre = P(am, s * (InnerFace + 0.17f), 0.5f * (d0 + d1) - 0.1f + 0.5f * height),
                        Size = new Vector3(0.34f, height, len + 0.06f),
                        Pitch = -(float)(Math.Atan2(rise, run) * 180.0 / Math.PI),
                        Yaw = Yaw(Dir(1f, 0f, 0f)),
                    });
                }
            }

            private void Post(int e, int s)
            {
                float end = BridgeShape.HalfLength;
                float aIn = e * (end - PostLength + PostBeyondEnd), aOut = e * (end + PostBeyondEnd);
                float ui = s * InnerFace, uo = s * (OuterFace + PostProud);
                float deck = Deck(e * end);
                float foot = Math.Min(deck - FootDrop, Math.Min(Math.Min(Ground(aIn, ui), Ground(aOut, ui)), Math.Min(Ground(aIn, uo), Ground(aOut, uo)))) - Bury;
                float top = deck + PostTop;
                // Three courses, each face a stone of its own; the middle course a little recessed (never towards the road).
                float[] lines = { foot, deck + PostCourse1 + Range(posts, -0.03f, 0.03f), deck + PostCourse2 + Range(posts, -0.03f, 0.03f), top };
                for (int c = 0; c < 3; c++)
                {
                    float r = c == 1 ? PostRecess : 0f;
                    float a0 = aIn + e * r, a1 = aOut - e * r, u1 = uo - s * r;
                    CourseRing(a0, a1, ui, u1, lines[c], lines[c + 1]);
                }

                // The cap: overhanging on every side but the road's, its top hipped to a low point so it reads from the side.
                float cIn = aIn - e * CapOverhang, cOut = aOut + e * CapOverhang, cu = uo + s * CapOverhang;
                int tone = Tone(true);
                float y0 = top, y1 = top + CapThick;
                float aLo = Math.Min(cIn, cOut), aHi = Math.Max(cIn, cOut), uLo = Math.Min(ui, cu), uHi = Math.Max(ui, cu);
                Quad(P(aLo, uLo, y0), P(aHi, uLo, y0), P(aHi, uLo, y1), P(aLo, uLo, y1), Dir(0f, -1f, 0f), tone);
                Quad(P(aLo, uHi, y0), P(aHi, uHi, y0), P(aHi, uHi, y1), P(aLo, uHi, y1), Dir(0f, 1f, 0f), tone);
                Quad(P(aLo, uLo, y0), P(aLo, uHi, y0), P(aLo, uHi, y1), P(aLo, uLo, y1), Dir(-1f, 0f, 0f), tone);
                Quad(P(aHi, uLo, y0), P(aHi, uHi, y0), P(aHi, uHi, y1), P(aHi, uLo, y1), Dir(1f, 0f, 0f), tone);
                Quad(P(aLo, uLo, y0), P(aHi, uLo, y0), P(aHi, uHi, y0), P(aLo, uHi, y0), Vector3.down, tone);
                Vector3 apex = P(0.5f * (aLo + aHi), 0.5f * (uLo + uHi), y1 + CapRise);
                Vector3 p00 = P(aLo, uLo, y1), p10 = P(aHi, uLo, y1), p11 = P(aHi, uHi, y1), p01 = P(aLo, uHi, y1);
                Tri(p00, p10, apex, Dir(0f, -1f, 0f) + Vector3.up, tone);
                Tri(p10, p11, apex, Dir(1f, 0f, 0f) + Vector3.up, tone);
                Tri(p11, p01, apex, Dir(0f, 1f, 0f) + Vector3.up, tone);
                Tri(p01, p00, apex, Dir(-1f, 0f, 0f) + Vector3.up, tone);
                foreach (float a in new[] { aIn, aOut })
                {
                    foreach (float u in new[] { ui, uo }) data.Feet.Add(P(a, u, foot));
                }

                data.Boxes.Add(new BridgeWallBox
                {
                    Centre = P(0.5f * (aIn + aOut), 0.5f * (ui + uo), 0.5f * (foot + top + CapThick)),
                    Size = new Vector3(Math.Abs(uo - ui), top + CapThick - foot, PostLength),
                    Yaw = Yaw(Dir(1f, 0f, 0f)),
                });
            }

            // The four sides of one course of a post, each its own stone.
            private void CourseRing(float a0, float a1, float u0, float u1, float y0, float y1)
            {
                float aLo = Math.Min(a0, a1), aHi = Math.Max(a0, a1), uLo = Math.Min(u0, u1), uHi = Math.Max(u0, u1);
                Quad(P(aLo, uLo, y0), P(aHi, uLo, y0), P(aHi, uLo, y1), P(aLo, uLo, y1), Dir(0f, -1f, 0f), Tone(false));
                Quad(P(aLo, uHi, y0), P(aHi, uHi, y0), P(aHi, uHi, y1), P(aLo, uHi, y1), Dir(0f, 1f, 0f), Tone(false));
                Quad(P(aLo, uLo, y0), P(aLo, uHi, y0), P(aLo, uHi, y1), P(aLo, uLo, y1), Dir(-1f, 0f, 0f), Tone(false));
                Quad(P(aHi, uLo, y0), P(aHi, uHi, y0), P(aHi, uHi, y1), P(aHi, uLo, y1), Dir(1f, 0f, 0f), Tone(false));
            }

            private void ApproachWall(int e, int s)
            {
                float a0 = e * (BridgeShape.HalfLength + PostBeyondEnd);
                float splay = WallSplay * (float)Math.PI / 180f;
                // Along the wall (outwards from the bridge, splayed away from the road) and across it (towards the road -> away).
                float da = e * (float)Math.Cos(splay), du = s * (float)Math.Sin(splay);
                // Perpendicular to the wall in the (along, across) plane, pointing away from the road.
                float pa = -du, pu = da;
                if (pu * s < 0f)
                {
                    pa = -pa;
                    pu = -pu;
                }

                float u0 = s * (InnerFace + WallSetBack + 0.5f * WallThick);
                float deck = Deck(e * BridgeShape.HalfLength);
                var centre = new (float a, float u)[WallStations];
                var inner = new (float a, float u)[WallStations];
                var outer = new (float a, float u)[WallStations];
                var foot = new float[WallStations];
                var top = new float[WallStations];
                float endGround = 0f;
                for (int i = 0; i < WallStations; i++)
                {
                    float t = i / (float)(WallStations - 1), along = WallLength * t;
                    float ca = a0 + da * along, cu = u0 + du * along;
                    centre[i] = (ca, cu);
                    inner[i] = (ca - pa * 0.5f * WallThick, cu - pu * 0.5f * WallThick);
                    outer[i] = (ca + pa * 0.5f * WallThick, cu + pu * 0.5f * WallThick);
                    // The lowest ground under the wall here (both faces and halfway to the next station).
                    float lo = Math.Min(Ground(inner[i].a, inner[i].u), Ground(outer[i].a, outer[i].u));
                    float hi = Math.Max(Ground(inner[i].a, inner[i].u), Ground(outer[i].a, outer[i].u));
                    if (i + 1 < WallStations)
                    {
                        float half = WallLength / (WallStations - 1) * 0.5f;
                        float ma = ca + da * half, mu = cu + du * half;
                        lo = Math.Min(lo, Math.Min(Ground(ma - pa * 0.5f * WallThick, mu - pu * 0.5f * WallThick), Ground(ma + pa * 0.5f * WallThick, mu + pu * 0.5f * WallThick)));
                    }

                    if (i > 0)
                    {
                        float half = WallLength / (WallStations - 1) * 0.5f;
                        float ma = ca - da * half, mu = cu - du * half;
                        lo = Math.Min(lo, Math.Min(Ground(ma - pa * 0.5f * WallThick, mu - pu * 0.5f * WallThick), Ground(ma + pa * 0.5f * WallThick, mu + pu * 0.5f * WallThick)));
                    }

                    foot[i] = lo - Bury;
                    if (i == 0) foot[i] = Math.Min(foot[i], deck - FootDrop);
                    if (i == WallStations - 1) endGround = hi;
                    top[i] = hi;
                }

                // Sloping down from near the parapet's height at the post to a low wall at the far end, never under the ground.
                for (int i = 0; i < WallStations; i++)
                {
                    float t = i / (float)(WallStations - 1);
                    float slope = deck + WallStartTop + (endGround + WallEndTop - deck - WallStartTop) * t;
                    top[i] = Math.Max(slope, top[i] + 0.3f);
                }

                // The body, in two courses per segment, runs up into its coping stones.
                Vector3 toRoad = Dir(-pa, -pu, 0f), away = Dir(pa, pu, 0f), outward = Dir(da, du, 0f), inward = Dir(-da, -du, 0f);
                var body = new float[WallStations];
                var course = new float[WallStations];
                for (int i = 0; i < WallStations; i++)
                {
                    body[i] = top[i] - WallCopingThick + 0.02f;
                    course[i] = Math.Max(foot[i] + 0.15f, top[i] - WallCourse);
                }

                for (int i = 0; i + 1 < WallStations; i++)
                {
                    foreach ((float a, float u)[] face in new[] { inner, outer })
                    {
                        Vector3 want = face == inner ? toRoad : away;
                        Quad(P(face[i].a, face[i].u, foot[i]), P(face[i + 1].a, face[i + 1].u, foot[i + 1]), P(face[i + 1].a, face[i + 1].u, course[i + 1]), P(face[i].a, face[i].u, course[i]), want, Tone(false));
                        Quad(P(face[i].a, face[i].u, course[i]), P(face[i + 1].a, face[i + 1].u, course[i + 1]), P(face[i + 1].a, face[i + 1].u, body[i + 1]), P(face[i].a, face[i].u, body[i]), want, Tone(false));
                    }
                }

                int last = WallStations - 1;
                Quad(P(inner[last].a, inner[last].u, foot[last]), P(outer[last].a, outer[last].u, foot[last]), P(outer[last].a, outer[last].u, body[last]), P(inner[last].a, inner[last].u, body[last]), outward, Tone(false));

                // One coping stone per segment, overhanging both faces, each a little higher or lower than the next.
                float half2 = 0.5f * WallThick + WallCopingOverhang;
                for (int i = 0; i + 1 < WallStations; i++)
                {
                    float jit = Range(posts, -CopingJitter, CopingJitter);
                    float t0 = top[i] + jit, t1 = top[i + 1] + jit, b0 = t0 - WallCopingThick, b1 = t1 - WallCopingThick;
                    (float a, float u) ci = centre[i], cj = centre[i + 1];
                    Vector3 i0 = P(ci.a - pa * half2, ci.u - pu * half2, 0f), i1 = P(cj.a - pa * half2, cj.u - pu * half2, 0f);
                    Vector3 o0 = P(ci.a + pa * half2, ci.u + pu * half2, 0f), o1 = P(cj.a + pa * half2, cj.u + pu * half2, 0f);
                    int tone = Tone(true);
                    Quad(AtY(i0, t0), AtY(i1, t1), AtY(o1, t1), AtY(o0, t0), Vector3.up, tone);
                    Quad(AtY(i0, b0), AtY(i1, b1), AtY(i1, t1), AtY(i0, t0), toRoad, tone);
                    Quad(AtY(o0, b0), AtY(o1, b1), AtY(o1, t1), AtY(o0, t0), away, tone);
                    Quad(AtY(i0, b0), AtY(i1, b1), AtY(o1, b1), AtY(o0, b0), Vector3.down, tone);
                    Quad(AtY(i1, b1), AtY(o1, b1), AtY(o1, t1), AtY(i1, t1), outward, tone);
                    if (i > 0) Quad(AtY(i0, b0), AtY(o0, b0), AtY(o0, t0), AtY(i0, t0), inward, tone);
                }

                for (int i = 0; i < WallStations; i++)
                {
                    data.Feet.Add(P(inner[i].a, inner[i].u, foot[i]));
                    data.Feet.Add(P(outer[i].a, outer[i].u, foot[i]));
                }

                float lowFoot = float.MaxValue, highTop = float.MinValue;
                for (int i = 0; i < WallStations; i++)
                {
                    lowFoot = Math.Min(lowFoot, foot[i]);
                    highTop = Math.Max(highTop, top[i]);
                }

                data.Boxes.Add(new BridgeWallBox
                {
                    Centre = P(a0 + da * WallLength * 0.5f, u0 + du * WallLength * 0.5f, 0.5f * (lowFoot + highTop)),
                    Size = new Vector3(WallThick, highTop - lowFoot, WallLength),
                    Yaw = Yaw(Dir(da, du, 0f)),
                });
            }

            private static Vector3 AtY(Vector3 p, float y) => new Vector3(p.x, y, p.z);

            private static float Yaw(Vector3 forward) => (float)(Math.Atan2(forward.x, forward.z) * 180.0 / Math.PI);

            // A flat quad p0-p1-p2-p3 (around its edge), facing towards want; tone picks the atlas cell.
            private void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 want, int tone)
            {
                Vector3 n = Vector3.Cross(p2 - p0, p3 - p1);
                if (Vector3.Dot(n, want) < 0f) n = -n;
                float len = n.magnitude;
                if (!(len > 1e-8f)) return;
                n /= len;
                Vector3 edge = p1 - p0;
                if (edge.sqrMagnitude < 1e-8f) edge = p2 - p3;
                if (vertices.Count + 4 > ushort.MaxValue) return;
                int first = vertices.Count;
                Add(p0, n, edge, tone);
                Add(p1, n, edge, tone);
                Add(p2, n, edge, tone);
                Add(p3, n, edge, tone);
                Triangle(first, first + 1, first + 2, n);
                Triangle(first, first + 2, first + 3, n);
            }

            // A flat triangle facing towards want.
            private void Tri(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 want, int tone)
            {
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(n, want) < 0f) n = -n;
                float len = n.magnitude;
                if (!(len > 1e-8f)) return;
                n /= len;
                if (vertices.Count + 3 > ushort.MaxValue) return;
                int first = vertices.Count;
                Add(p0, n, p1 - p0, tone);
                Add(p1, n, p1 - p0, tone);
                Add(p2, n, p1 - p0, tone);
                Triangle(first, first + 1, first + 2, n);
            }

            private void Add(Vector3 p, Vector3 n, Vector3 edge, int tone)
            {
                Vector3 t = edge - n * Vector3.Dot(edge, n);
                float tl = t.magnitude;
                t = tl > 1e-6f ? t / tl : (Math.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right);
                var uv = new Vector2(AtlasU, AtlasV[Math.Max(0, Math.Min(2, tone))]);
                vertices.Add(new BridgeWallVertex { position = p, normal = n, tangent = new Vector4(t.x, t.y, t.z, 1f), uv = uv });
            }

            // Clockwise seen from the front (Unity's winding): the cross product of its edges points to where it is seen from.
            private void Triangle(int a, int b, int c, Vector3 n)
            {
                Vector3 pa = vertices[a].position, pb = vertices[b].position, pc = vertices[c].position;
                Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
                if (cross.sqrMagnitude < 1e-10f) return;
                if (Vector3.Dot(cross, n) < 0f)
                {
                    int swap = b;
                    b = c;
                    c = swap;
                }

                indices.Add((ushort)a);
                indices.Add((ushort)b);
                indices.Add((ushort)c);
            }

            public BridgeWallMeshData Finish()
            {
                if (indices.Count == 0) return null;
                data.Vertices = vertices.ToArray();
                data.VertexCount = vertices.Count;
                data.Indices = indices.ToArray();
                data.IndexCount = indices.Count;
                var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                foreach (BridgeWallVertex v in vertices)
                {
                    mn = Vector3.Min(mn, v.position);
                    mx = Vector3.Max(mx, v.position);
                }

                data.BoundsMin = mn;
                data.BoundsMax = mx;
                return data;
            }
        }
    }
}
