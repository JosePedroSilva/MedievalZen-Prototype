using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Which instanced tree prototype each tree family + variant draws as (built on the main thread from the palette, then
    /// read-only, so worker threads may use it), with the size of each prototype for culling bounds.
    /// </summary>
    public sealed class TreePrototypeIndex
    {
        private readonly Dictionary<string, int[]> map = new Dictionary<string, int[]>();
        private readonly List<float> heights = new List<float>();
        private readonly List<float> radii = new List<float>();

        /// <summary>Number of prototypes.</summary>
        public int Count => heights.Count;

        /// <summary>Adds a prototype (height above its pivot and largest horizontal half-extent at scale 1) and returns its index.</summary>
        public int AddPrototype(float height, float radius)
        {
            heights.Add(Math.Max(0.5f, height));
            radii.Add(Math.Max(0.25f, radius));
            return heights.Count - 1;
        }

        /// <summary>Replaces a prototype's height and horizontal reach (a field-boundary piece measured in its own laid-out frame).</summary>
        public void SetShape(int prototype, float height, float radius)
        {
            heights[prototype] = Math.Max(0.5f, height);
            radii[prototype] = Math.Max(0.25f, radius);
        }

        /// <summary>Sets which prototype each variant of <paramref name="role"/> draws as (palette order).</summary>
        public void SetFamily(string role, int[] prototypes)
        {
            if (!string.IsNullOrEmpty(role) && prototypes != null && prototypes.Length > 0) map[role] = prototypes;
        }

        /// <summary>The prototype of a placement (variant wrapped like the palette), or −1 when its family has none.</summary>
        public int PrototypeOf(string role, int variant)
        {
            if (role == null || !map.TryGetValue(role, out int[] list) || list.Length == 0) return -1;
            return list[((variant % list.Length) + list.Length) % list.Length];
        }

        /// <summary>Height of a prototype at scale 1.</summary>
        public float HeightOf(int prototype) => heights[prototype];

        /// <summary>Largest horizontal half-extent of a prototype at scale 1.</summary>
        public float RadiusOf(int prototype) => radii[prototype];
    }

    /// <summary>One square cell of a near tile's trees: per prototype, the instance matrices, plus the cell's bounds.</summary>
    public sealed class TreeCell
    {
        /// <summary>Instance matrices per prototype index (null where the cell has none of that prototype).</summary>
        public Matrix4x4[][] ByPrototype;

        /// <summary>Bounds of every tree in the cell (positions, crowns and heights), in the same space as the matrices.</summary>
        public Vector3 Min, Max;

        /// <summary>Trees in the cell.</summary>
        public int Count;

        /// <summary>
        /// The band each tree was last drawn in, per prototype (parallel to <see cref="ByPrototype"/>; <see cref="TreeDrawRules.Unknown"/>
        /// before the first frame). Read and written by the renderer for LOD hysteresis.
        /// </summary>
        public byte[][] LastBand;

        /// <summary>
        /// The band every tree of the cell was last drawn in when the whole cell lay in one band (no per-tree bookkeeping, see
        /// <see cref="TreeDrawRules.UniformBand"/>), else <see cref="TreeDrawRules.Unknown"/> (the per-tree <see cref="LastBand"/> holds).
        /// </summary>
        public byte Uniform = TreeDrawRules.Unknown;

        /// <summary>
        /// Second performance pass: <see cref="Uniform"/> per prototype (prototypes with distances of their own may lie in one band
        /// where the frame's distances do not); allocated by the walk.
        /// </summary>
        public byte[] UniformOf;

        /// <summary>The cell was out of view last frame (its <see cref="LastBand"/> is stale: start afresh).</summary>
        public bool Stale = true;

        /// <summary>Squared horizontal distance from (x, z) to the cell's bounds (0 inside).</summary>
        public float SqrDistanceXZ(float x, float z)
        {
            float dx = x < Min.x ? Min.x - x : x > Max.x ? x - Max.x : 0f;
            float dz = z < Min.z ? Min.z - z : z > Max.z ? z - Max.z : 0f;
            return dx * dx + dz * dz;
        }
    }

    /// <summary>
    /// A near tile's trees bucketed into square cells by prototype (pure, built on a worker thread from the blueprint).
    /// Matrices start tile-local (tile corner at the origin) and are moved into scene space with <see cref="Offset"/> when the
    /// tile is shown, and again on every floating-origin shift. Each matrix is translate(position) · rotateY(yaw) ·
    /// scale(scale), exactly the pose the terrain trees used to get.
    /// </summary>
    public sealed class TreeCellSet
    {
        /// <summary>Default cell edge in metres.</summary>
        public const float DefaultCellSize = 32f;

        /// <summary>Cells (row-major by z, then x); never null entries.</summary>
        public TreeCell[] Cells = Array.Empty<TreeCell>();

        /// <summary>Cells per edge.</summary>
        public int CellsPerSide;

        /// <summary>Cell edge in metres.</summary>
        public float CellSize;

        /// <summary>Trees bucketed.</summary>
        public int Total;

        /// <summary>Trees whose family had no prototype (not drawn).</summary>
        public int Skipped;

        /// <summary>The translation applied so far (tile-local → scene).</summary>
        public Vector3 Applied;

        /// <summary>Field-boundary pieces bucketed (included in <see cref="Total"/>).</summary>
        public int Pieces;

        /// <summary>
        /// Buckets <paramref name="trees"/> (tile-local positions) of a <paramref name="tileSize"/> tile, and the tile's field-boundary
        /// <paramref name="pieces"/> (optional) with their full pose (<see cref="PiecePose"/>).
        /// </summary>
        public static TreeCellSet Build(List<TreePlacement> trees, float tileSize, TreePrototypeIndex prototypes, float cellSize = DefaultCellSize, List<PropPlacement> pieces = null,
            bool forestExtras = true)
        {
            if (prototypes == null) throw new ArgumentNullException(nameof(prototypes));
            if (!(tileSize > 0f)) throw new ArgumentOutOfRangeException(nameof(tileSize));
            int side = Math.Max(1, (int)Math.Ceiling(tileSize / Math.Max(1f, cellSize)));
            float size = tileSize / side;
            var set = new TreeCellSet { CellsPerSide = side, CellSize = size };
            int protoCount = prototypes.Count;
            var lists = new List<Matrix4x4>[side * side][];
            var mins = new Vector3[side * side];
            var maxs = new Vector3[side * side];
            var counts = new int[side * side];

            if (trees != null)
            {
                foreach (TreePlacement tree in trees)
                {
                    if (!forestExtras && PropRole.IsForestExtra(tree.role)) continue;   // shed by the safety ladder (level 1)
                    int proto = prototypes.PrototypeOf(tree.role, tree.variant);
                    if (proto < 0 || proto >= protoCount)
                    {
                        set.Skipped++;
                        continue;
                    }

                    int cx = Clamp((int)Math.Floor(tree.position.x / size), 0, side - 1);
                    int cz = Clamp((int)Math.Floor(tree.position.z / size), 0, side - 1);
                    int cell = cz * side + cx;
                    lists[cell] ??= new List<Matrix4x4>[protoCount];
                    lists[cell][proto] ??= new List<Matrix4x4>();
                    lists[cell][proto].Add(Pose(tree.position, tree.yaw, tree.scale));
                    Grow(cell, proto, tree.position, tree.scale);
                }
            }

            if (pieces != null)
            {
                foreach (PropPlacement piece in pieces)
                {
                    if (!forestExtras && PropRole.IsForestExtra(piece.role)) continue;
                    int proto = prototypes.PrototypeOf(piece.role, piece.variant);
                    if (proto < 0 || proto >= protoCount)
                    {
                        set.Skipped++;
                        continue;
                    }

                    int cx = Clamp((int)Math.Floor(piece.position.x / size), 0, side - 1);
                    int cz = Clamp((int)Math.Floor(piece.position.z / size), 0, side - 1);
                    int cell = cz * side + cx;
                    lists[cell] ??= new List<Matrix4x4>[protoCount];
                    lists[cell][proto] ??= new List<Matrix4x4>();
                    lists[cell][proto].Add(PiecePose(piece.position, piece.yaw, piece.pitch, piece.roll, piece.scale));
                    Grow(cell, proto, piece.position, piece.scale);
                    set.Pieces++;
                }
            }

            void Grow(int cell, int proto, Vector3 position, float scale)
            {
                float s = scale > 0f ? scale : 1f;
                float r = prototypes.RadiusOf(proto) * s, h = prototypes.HeightOf(proto) * s;
                var lo = new Vector3(position.x - r, position.y - 1f, position.z - r);
                var hi = new Vector3(position.x + r, position.y + h, position.z + r);
                if (counts[cell] == 0)
                {
                    mins[cell] = lo;
                    maxs[cell] = hi;
                }
                else
                {
                    mins[cell] = Vector3.Min(mins[cell], lo);
                    maxs[cell] = Vector3.Max(maxs[cell], hi);
                }

                counts[cell]++;
                set.Total++;
            }

            var cells = new List<TreeCell>();
            for (int c = 0; c < lists.Length; c++)
            {
                if (counts[c] == 0) continue;
                var cell = new TreeCell { ByPrototype = new Matrix4x4[protoCount][], LastBand = new byte[protoCount][], Min = mins[c], Max = maxs[c], Count = counts[c] };
                for (int p = 0; p < protoCount; p++)
                {
                    if (lists[c][p] == null) continue;
                    cell.ByPrototype[p] = lists[c][p].ToArray();
                    cell.LastBand[p] = new byte[cell.ByPrototype[p].Length];
                    for (int i = 0; i < cell.LastBand[p].Length; i++) cell.LastBand[p][i] = TreeDrawRules.Unknown;
                }

                cells.Add(cell);
            }

            set.Cells = cells.ToArray();
            return set;
        }

        /// <summary>Moves every matrix and bound by <paramref name="delta"/> (tile shown at a scene position, or an origin shift).</summary>
        public void Offset(Vector3 delta)
        {
            if (delta == Vector3.zero) return;
            foreach (TreeCell cell in Cells)
            {
                cell.Min += delta;
                cell.Max += delta;
                foreach (Matrix4x4[] list in cell.ByPrototype)
                {
                    if (list == null) continue;
                    for (int i = 0; i < list.Length; i++)
                    {
                        list[i].m03 += delta.x;
                        list[i].m13 += delta.y;
                        list[i].m23 += delta.z;
                    }
                }
            }

            Applied += delta;
        }

        /// <summary>
        /// translate(position) · rotateY(yaw degrees) · scale(uniform), written out (no native calls). Rotation follows
        /// <c>Quaternion.Euler(0, yaw, 0)</c>: positive yaw turns +Z toward +X.
        /// </summary>
        public static Matrix4x4 Pose(Vector3 position, float yawDegrees, float scale)
        {
            float s = scale > 0f ? scale : 1f;
            double a = yawDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            var m = new Matrix4x4();
            m.m00 = cos * s;
            m.m01 = 0f;
            m.m02 = sin * s;
            m.m03 = position.x;
            m.m10 = 0f;
            m.m11 = s;
            m.m12 = 0f;
            m.m13 = position.y;
            m.m20 = -sin * s;
            m.m21 = 0f;
            m.m22 = cos * s;
            m.m23 = position.z;
            m.m30 = 0f;
            m.m31 = 0f;
            m.m32 = 0f;
            m.m33 = 1f;
            return m;
        }

        /// <summary>
        /// The pose of a field-boundary piece: translate(position) · rotateY(yaw) · rotateX(pitch) · rotateZ(roll) · scale(uniform),
        /// i.e. <c>Quaternion.Euler(pitch, yaw, roll)</c>, written out. The piece's prototype is laid along local +Z and centred
        /// (see <c>EndlessTreeRenderer</c>), so yaw is the direction the boundary runs, a positive pitch tilts its far end down and
        /// roll leans it sideways.
        /// </summary>
        public static Matrix4x4 PiecePose(Vector3 position, float yawDegrees, float pitchDegrees, float rollDegrees, float scale)
        {
            float s = scale > 0f ? scale : 1f;
            double a = yawDegrees * Math.PI / 180.0, b = pitchDegrees * Math.PI / 180.0, g = rollDegrees * Math.PI / 180.0;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a), cb = (float)Math.Cos(b), sb = (float)Math.Sin(b), cg = (float)Math.Cos(g), sg = (float)Math.Sin(g);
            // R = Ry(a) · Rx(b) · Rz(g) with Ry = [c 0 s; 0 1 0; -s 0 c], Rx = [1 0 0; 0 c -s; 0 s c], Rz = [c -s 0; s c 0; 0 0 1].
            float r00 = ca * cg + sa * sb * sg, r01 = -ca * sg + sa * sb * cg, r02 = sa * cb;
            float r10 = cb * sg, r11 = cb * cg, r12 = -sb;
            float r20 = -sa * cg + ca * sb * sg, r21 = sa * sg + ca * sb * cg, r22 = ca * cb;
            var m = new Matrix4x4();
            m.m00 = r00 * s;
            m.m01 = r01 * s;
            m.m02 = r02 * s;
            m.m03 = position.x;
            m.m10 = r10 * s;
            m.m11 = r11 * s;
            m.m12 = r12 * s;
            m.m13 = position.y;
            m.m20 = r20 * s;
            m.m21 = r21 * s;
            m.m22 = r22 * s;
            m.m23 = position.z;
            m.m30 = 0f;
            m.m31 = 0f;
            m.m32 = 0f;
            m.m33 = 1f;
            return m;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>Which mesh LOD a tree is drawn with (shadows are drawn separately, see <c>TreeDrawRules.ShadowRing</c>).</summary>
    public enum TreeDrawBand
    {
        /// <summary>LOD0, nearest trees.</summary>
        Detail = 0,

        /// <summary>An intermediate mesh LOD (LOD1 of the Meadow, birch and fruit trees).</summary>
        Mid = 1,

        /// <summary>The lowest LOD that is still a 3D mesh (never a card), out to the near/far boundary.</summary>
        Low = 2,

        /// <summary>
        /// Second performance pass (2026-10-06): beyond the impostor distance a tree is drawn as its solid low-poly impostor
        /// (<see cref="TreeImpostor"/>, the same shape and colours as the distant tiles' trees; never a card). Prototypes without one
        /// (plants, boundary pieces) keep drawing their lowest mesh LOD in this band.
        /// </summary>
        Impostor = 3,
    }

    /// <summary>The tree LOD distances in effect (already multiplied by the detail scale) and their hysteresis margins.</summary>
    public readonly struct TreeLodDistances
    {
        /// <summary>LOD0 out to this distance, metres.</summary>
        public readonly float Full;

        /// <summary>The intermediate LOD out to this distance; the lowest mesh LOD beyond.</summary>
        public readonly float Mid;

        /// <summary>Hysteresis around <see cref="Full"/>, metres.</summary>
        public readonly float FullMargin;

        /// <summary>Hysteresis around <see cref="Mid"/>, metres.</summary>
        public readonly float MidMargin;

        /// <summary>The lowest mesh LOD out to this distance; the solid impostor beyond (positive infinity: no impostor band).</summary>
        public readonly float Impostor;

        /// <summary>Hysteresis around <see cref="Impostor"/>, metres (0 when there is no impostor band).</summary>
        public readonly float ImpostorMargin;

        /// <summary>Distances in effect (non-negative, mid ≥ full), no impostor band.</summary>
        public TreeLodDistances(float full, float mid) : this(full, mid, 0f)
        {
        }

        /// <summary>
        /// Distances in effect (non-negative, mid ≥ full); <paramref name="impostor"/> ≤ 0 or not finite: no impostor band, otherwise
        /// at least the mid distance plus both margins (the Low band never vanishes, so the hysteresis stays one band at a time).
        /// </summary>
        public TreeLodDistances(float full, float mid, float impostor)
        {
            Full = Math.Max(0f, full);
            Mid = Math.Max(Full, mid);
            FullMargin = TreeDrawRules.Hysteresis(Full);
            MidMargin = TreeDrawRules.Hysteresis(Mid);
            if (impostor > 0f && !float.IsInfinity(impostor) && !float.IsNaN(impostor))
            {
                // Impostor − its margin stays at least a metre past Mid + its margin (margin: 5 %, at least 4 m).
                float floor = Math.Max((Mid + MidMargin + 1f) / 0.95f, Mid + MidMargin + 5f);
                Impostor = Math.Max(floor, impostor);
                ImpostorMargin = TreeDrawRules.Hysteresis(Impostor);
            }
            else
            {
                Impostor = float.PositiveInfinity;
                ImpostorMargin = 0f;
            }
        }

        /// <summary>
        /// The distances for the settings: <paramref name="full"/>, <paramref name="mid"/> and <paramref name="impostor"/> (0: none)
        /// times <paramref name="scale"/> (clamped to <see cref="TreeDrawRules.MinScale"/>..<see cref="TreeDrawRules.MaxScale"/>).
        /// </summary>
        public static TreeLodDistances From(float full, float mid, float scale, float impostor = 0f)
        {
            float s = float.IsNaN(scale) ? 1f : Math.Max(TreeDrawRules.MinScale, Math.Min(TreeDrawRules.MaxScale, scale));
            return new TreeLodDistances(full * s, mid * s, impostor * s);
        }
    }

    /// <summary>
    /// Which LOD of a tree prefab's LODGroup each band draws (pure: from per-LOD triangle counts and thinness). Cards (a few
    /// crossed or flat quads standing in for the whole tree) are never used: the user found them "really noticeable"; the
    /// far-tile impostors take over at the near/far boundary instead.
    /// </summary>
    public static class TreeLodPlan
    {
        /// <summary>A LOD with at most this many triangles (and at most 1/8 of LOD0's) is a card.</summary>
        public const int CardTriangles = 64;

        /// <summary>A LOD whose smallest bounds extent is below this fraction of its largest is a flat card.</summary>
        public const float CardThinness = 0.02f;

        /// <summary>True when a LOD of <paramref name="triangles"/> triangles (LOD0: <paramref name="lod0Triangles"/>) with the given thinness is a card.</summary>
        public static bool IsCard(int triangles, int lod0Triangles, float thinness)
        {
            if (triangles <= 0) return true;
            if (triangles <= CardTriangles && lod0Triangles >= 8 * triangles) return true;
            return thinness < CardThinness;
        }

        /// <summary>
        /// The LOD indices drawn as <see cref="TreeDrawBand.Detail"/>, <see cref="TreeDrawBand.Mid"/> and <see cref="TreeDrawBand.Low"/>:
        /// LOD0, the middle one of the mesh (non-card) LODs, and the last mesh LOD. <paramref name="thinness"/> (smallest over
        /// largest bounds extent per LOD) may be null. With a single LOD (no LODGroup) all three are 0; LOD0 is always kept.
        /// </summary>
        public static (int detail, int mid, int low) Choose(int[] lodTriangles, float[] thinness)
        {
            if (lodTriangles == null || lodTriangles.Length == 0) return (0, 0, 0);
            var mesh = new List<int> { 0 };
            for (int i = 1; i < lodTriangles.Length; i++)
            {
                float thin = thinness != null && i < thinness.Length ? thinness[i] : 1f;
                if (!IsCard(lodTriangles[i], lodTriangles[0], thin)) mesh.Add(i);
            }

            return (mesh[0], mesh[mesh.Count / 2], mesh[mesh.Count - 1]);
        }
    }

    /// <summary>Pure LOD / shadow / budget decisions of the instanced tree renderer (unit tested).</summary>
    public static class TreeDrawRules
    {
        /// <summary>RenderMeshInstanced takes at most this many instances per call.</summary>
        public const int MaxInstancesPerCall = 1023;

        /// <summary>Number of <see cref="TreeDrawBand"/> values.</summary>
        public const int BandCount = 4;

        /// <summary>Shadow rings (shadow-only draws gathered by distance, so each batch's bounds stay tight for cascade culling).</summary>
        public const int ShadowRingCount = 2;

        /// <summary>The inner shadow ring ends at this fraction of the tree shadow distance.</summary>
        public const float InnerShadowRing = 0.4f;

        /// <summary>Smallest detail scale.</summary>
        public const float MinScale = 0.5f;

        /// <summary>Largest detail scale.</summary>
        public const float MaxScale = 2f;

        /// <summary>"No band remembered yet".</summary>
        public const byte Unknown = 255;

        /// <summary>Hysteresis around a LOD threshold: 5 % of it, at least 4 m.</summary>
        public static float Hysteresis(float threshold) => Math.Max(4f, 0.05f * threshold);

        /// <summary>The band at <paramref name="distance"/> without memory: LOD0 within Full, the intermediate LOD within Mid, else the lowest mesh LOD.</summary>
        public static TreeDrawBand BandFor(float distance, TreeLodDistances d)
        {
            if (distance <= d.Full) return TreeDrawBand.Detail;
            if (distance <= d.Mid) return TreeDrawBand.Mid;
            return distance <= d.Impostor ? TreeDrawBand.Low : TreeDrawBand.Impostor;
        }

        /// <summary>
        /// The band with hysteresis: a tree keeps its <paramref name="previous"/> band (a <see cref="TreeDrawBand"/> value, or
        /// <see cref="Unknown"/>) until it is more than the margin past that band's threshold, so walking to and fro across a
        /// boundary does not make it flicker between LODs.
        /// </summary>
        public static TreeDrawBand BandFor(float distance, byte previous, TreeLodDistances d)
        {
            switch (previous)
            {
                case (byte)TreeDrawBand.Detail:
                    if (distance <= d.Full + d.FullMargin) return TreeDrawBand.Detail;
                    break;
                case (byte)TreeDrawBand.Mid:
                    if (distance > d.Full - d.FullMargin && distance <= d.Mid + d.MidMargin) return TreeDrawBand.Mid;
                    break;
                case (byte)TreeDrawBand.Low:
                    if (distance > d.Mid - d.MidMargin && distance <= d.Impostor + d.ImpostorMargin) return TreeDrawBand.Low;
                    break;
                case (byte)TreeDrawBand.Impostor:
                    if (distance > d.Impostor - d.ImpostorMargin) return TreeDrawBand.Impostor;
                    break;
            }

            return BandFor(distance, d);
        }

        /// <summary>
        /// True when every tree of a cell whose nearest point is <paramref name="cellDistance"/> away is in the lowest band
        /// whatever it was before (no per-tree work needed).
        /// </summary>
        public static bool WholeCellLow(float cellDistance, TreeLodDistances d) => cellDistance > d.Mid + d.MidMargin;

        /// <summary>
        /// Performance pass: the band every tree of a cell is in whatever band it was in before, when the cell's nearest point is
        /// <paramref name="near"/> and its farthest <paramref name="far"/> away (the trees' pivots lie between), or <see cref="Unknown"/>
        /// when the cell straddles a band edge or its hysteresis margin and must be decided per tree. Detail when the farthest point is
        /// inside the full distance less its margin; Mid when the cell lies between the full distance plus its margin and the mid distance
        /// less its margin; Low beyond the mid distance plus its margin. In each case <see cref="BandFor(float, byte, TreeLodDistances)"/>
        /// gives that band for every previous band, so the hysteresis is unchanged.
        /// </summary>
        public static byte UniformBand(float near, float far, TreeLodDistances d)
        {
            if (far <= d.Full - d.FullMargin) return (byte)TreeDrawBand.Detail;
            if (near > d.Impostor + d.ImpostorMargin) return (byte)TreeDrawBand.Impostor;
            if (near > d.Mid + d.MidMargin && far <= d.Impostor - d.ImpostorMargin) return (byte)TreeDrawBand.Low;
            if (near > d.Full + d.FullMargin && far <= d.Mid - d.MidMargin) return (byte)TreeDrawBand.Mid;
            return Unknown;
        }

        /// <summary>Squared distance from <paramref name="p"/> to the nearest point of the box (0 inside).</summary>
        public static float NearestSq(Vector3 p, Vector3 min, Vector3 max)
        {
            float dx = p.x < min.x ? min.x - p.x : p.x > max.x ? p.x - max.x : 0f;
            float dy = p.y < min.y ? min.y - p.y : p.y > max.y ? p.y - max.y : 0f;
            float dz = p.z < min.z ? min.z - p.z : p.z > max.z ? p.z - max.z : 0f;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>Squared distance from <paramref name="p"/> to the farthest corner of the box.</summary>
        public static float FarthestSq(Vector3 p, Vector3 min, Vector3 max)
        {
            float dx = Math.Max(Math.Abs(p.x - min.x), Math.Abs(max.x - p.x));
            float dy = Math.Max(Math.Abs(p.y - min.y), Math.Abs(max.y - p.y));
            float dz = Math.Max(Math.Abs(p.z - min.z), Math.Abs(max.z - p.z));
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>A fern is always drawn within this fraction of the fern draw distance (<see cref="FadeDistance"/> starts here).</summary>
        public const float FernFadeStart = 0.8f;

        /// <summary>Below this height of the sun (sine of its elevation: about 1.7°) shadows are too long to cull casters by their reach.</summary>
        public const float MinSweepSine = 0.03f;

        /// <summary>
        /// Metres below the lowest registered cell that a shadow may still land on (ground between the trees: a stream channel, a hollow,
        /// a shelf's foot); the shadow sweep reaches down to there.
        /// </summary>
        public const float ReceiverDrop = 25f;

        /// <summary>True when the light travelling along <paramref name="lightForward"/> comes down steeply enough to cull casters by their shadow's reach.</summary>
        public static bool ShadowSweepPossible(Vector3 lightForward) => -lightForward.y >= MinSweepSine;

        /// <summary>
        /// Performance pass: the longest path a shadow can take from a caster, as a vector along the light (<paramref name="lightForward"/>,
        /// normalised, pointing down): from the caster's top down to <paramref name="drop"/> metres below it (the lowest ground a shadow may
        /// land on), at most <paramref name="maxLength"/> long (twice the shadow distance: a caster and the ground it shades both lie
        /// within the shadow distance of the camera). A caster whose box swept along this vector misses the view frustum shades nothing
        /// that is seen. Zero when the light does not come down (<see cref="ShadowSweepPossible"/>).
        /// </summary>
        public static Vector3 ShadowSweep(Vector3 lightForward, float drop, float maxLength)
        {
            float down = -lightForward.y;
            if (down < MinSweepSine) return Vector3.zero;
            float length = Math.Min(Math.Max(0f, maxLength), Math.Max(0f, drop) / down);
            return new Vector3(lightForward.x * length, lightForward.y * length, lightForward.z * length);
        }

        /// <summary>Metres the eye may move before the last walk is redone (<see cref="CanReuse"/>).</summary>
        public const float ReuseMove = 1f;

        /// <summary>Degrees the view may turn before the last walk is redone; the walk's frustum is opened by this much on every side.</summary>
        public const float ReuseTurn = 2f;

        /// <summary>Degrees the light may turn before the last walk is redone (the sun moves about 0.25° a second at the default day length).</summary>
        public const float ReuseLightTurn = 0.5f;

        /// <summary>
        /// Performance pass: true when the last walk's result can be submitted again: nothing registered changed (<paramref name="sameInputs"/>:
        /// tiles, origin, settings, toggles), the eye moved at most <see cref="ReuseMove"/>, the view's forward and up turned at most
        /// <see cref="ReuseTurn"/>, the field of view and aspect did not change, and the light turned at most <see cref="ReuseLightTurn"/>.
        /// The walk is done with a frustum opened by <see cref="ReuseTurn"/> and padded by <see cref="ReuseMove"/>, so what it gathered still
        /// covers the view; LOD bands lag by at most a metre (the hysteresis margins are 4 m and more).
        /// </summary>
        public static bool CanReuse(bool sameInputs, Vector3 eye, Vector3 lastEye, Vector3 forward, Vector3 lastForward, Vector3 up, Vector3 lastUp,
            float fov, float lastFov, float aspect, float lastAspect, Vector3 light, Vector3 lastLight)
        {
            if (!sameInputs) return false;
            if (Math.Abs(fov - lastFov) > 1e-3f || Math.Abs(aspect - lastAspect) > 1e-4f) return false;
            float mx = eye.x - lastEye.x, my = eye.y - lastEye.y, mz = eye.z - lastEye.z;
            if (mx * mx + my * my + mz * mz > ReuseMove * ReuseMove) return false;
            return Within(forward, lastForward, ReuseTurn) && Within(up, lastUp, ReuseTurn) && Within(light, lastLight, ReuseLightTurn);
        }

        private static bool Within(Vector3 a, Vector3 b, float degrees)
        {
            float ma = (float)Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z), mb = (float)Math.Sqrt(b.x * b.x + b.y * b.y + b.z * b.z);
            if (ma < 1e-6f || mb < 1e-6f) return ma < 1e-6f && mb < 1e-6f;
            float cos = (a.x * b.x + a.y * b.y + a.z * b.z) / (ma * mb);
            return cos >= (float)Math.Cos(degrees * Math.PI / 180.0) - 1e-6f;
        }

        /// <summary>
        /// The shadow ring of a cell <paramref name="cellDistance"/> away (0 inner, 1 outer), or −1 when its trees cast no
        /// shadow: shadows off, or beyond <paramref name="shadowDistance"/> (the tree shadow distance, see <see cref="ShadowDistance"/>).
        /// </summary>
        public static int ShadowRing(float cellDistance, float shadowDistance, bool shadowsEnabled)
        {
            if (!shadowsEnabled || !(shadowDistance > 0f) || cellDistance > shadowDistance) return -1;
            return cellDistance <= shadowDistance * InnerShadowRing ? 0 : 1;
        }

        /// <summary>
        /// Forest pass: the shadow ring of a cell <paramref name="cellDistance"/> away when shadows are split at
        /// <paramref name="proxyDistance"/>: 0 within it (the simplest mesh LOD casts), 1 beyond it out to
        /// <paramref name="shadowDistance"/> (a low-poly shadow proxy casts instead), −1 beyond or with shadows off.
        /// </summary>
        public static int ShadowRing(float cellDistance, float shadowDistance, float proxyDistance, bool shadowsEnabled)
        {
            if (!shadowsEnabled || !(shadowDistance > 0f) || cellDistance > shadowDistance) return -1;
            return cellDistance <= Math.Max(0f, proxyDistance) ? 0 : 1;
        }

        /// <summary>
        /// Distance at which an instance of a prototype drawn only to <paramref name="maxDistance"/> (ferns) stops being drawn: each
        /// instance has its own threshold between 80 and 100 % of it (from its position), so the edge is a gradual thinning, not a line.
        /// </summary>
        public static float FadeDistance(float maxDistance, float x, float z)
        {
            uint h = (uint)((int)Math.Floor(x * 4f) * 73856093) ^ (uint)((int)Math.Floor(z * 4f) * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return maxDistance * (FernFadeStart + (1f - FernFadeStart) * (h & 0xFFFF) / 65535f);
        }

        /// <summary>The tree shadow distance in effect: the setting, never beyond the pipeline's shadow distance (0 = no shadows).</summary>
        public static float ShadowDistance(float setting, float pipeline) => Math.Max(0f, Math.Min(setting, pipeline));

        /// <summary>Draw calls needed to submit <paramref name="instances"/> instances of a prototype whose LOD has <paramref name="parts"/> mesh parts.</summary>
        public static int CallsFor(int instances, int parts) => instances <= 0 || parts <= 0 ? 0 : ((instances + MaxInstancesPerCall - 1) / MaxInstancesPerCall) * parts;

        /// <summary>
        /// How many of <paramref name="wanted"/> instances may still be drawn given what was already submitted this frame and
        /// the caps (instances and draw calls; each chunk of up to 1023 instances costs <paramref name="parts"/> calls).
        /// </summary>
        public static int Allowed(int wanted, int parts, int instancesSoFar, int callsSoFar, int maxInstances, int maxCalls)
        {
            if (wanted <= 0 || parts <= 0) return 0;
            int byInstances = Math.Max(0, maxInstances - instancesSoFar);
            int chunks = Math.Max(0, (maxCalls - callsSoFar) / parts);
            long byCalls = (long)chunks * MaxInstancesPerCall;
            return (int)Math.Min(wanted, Math.Min(byInstances, byCalls));
        }
    }

    /// <summary>
    /// Low-poly shadow casters (forest pass, 2026-10-05): beyond the shadow proxy distance a tree's shadow comes from a closed solid
    /// of its measured crown (<see cref="FarTreeStyle"/>: a lathe of five sides through the crown's base, widest ring, upper ring and
    /// top, shrunk to 85 % so it stays inside the leaves it stands for) on a three-sided trunk, 26 triangles instead of the
    /// 700-6,600 of its simplest mesh LOD; a field-boundary piece's from its collider box (12 triangles). Pure (vertices and indices;
    /// the renderer uploads them).
    /// </summary>
    public static class ShadowProxy
    {
        /// <summary>Fraction of the measured crown radius the proxy keeps (inside the leaves: no self-shadow on the outer crown).</summary>
        public const float CrownShrink = 0.85f;

        /// <summary>Triangles of a tree proxy of <paramref name="shape"/>.</summary>
        public static int TrianglesOf(FarTreeShape shape) => shape == FarTreeShape.Dead ? 6 : 26;

        /// <summary>Appends the proxy of a tree of <paramref name="style"/> at scale 1 (pivot at the trunk base) to the lists.</summary>
        public static void Tree(FarTreeStyle style, List<Vector3> vertices, List<int> triangles)
        {
            float h = Math.Max(1f, style.Height);
            float trunk = Math.Max(0.12f, style.TrunkRadius * 0.6f);
            if (style.Shape == FarTreeShape.Dead)
            {
                Prism(vertices, triangles, trunk * 1.4f, h);
                return;
            }

            float cb = Math.Max(0.3f, Math.Min(style.CrownBase, h * 0.8f));
            float wy = Math.Max(cb, Math.Min(style.WidestAt, h * 0.95f));
            float r = Math.Max(0.3f, style.Radius * CrownShrink);
            float tr = Math.Max(0.15f, Math.Min(r, style.TopRadius * CrownShrink));
            float uy = cb + 0.8125f * (h - cb);
            if (style.Shape == FarTreeShape.Conifer)
            {
                wy = cb + 0.1f * (h - cb);
                tr = Math.Max(0.12f, r * 0.35f);
            }

            int first = vertices.Count;
            vertices.Add(new Vector3(0f, cb, 0f));                       // bottom point
            for (int k = 0; k < 5; k++) vertices.Add(Ring(k, 0f, r, wy));   // widest ring
            for (int k = 0; k < 5; k++) vertices.Add(Ring(k, 0.5f, tr, uy));   // upper ring, half a side round
            vertices.Add(new Vector3(0f, h * 0.98f, 0f));                // top
            int bottom = first, ring1 = first + 1, ring2 = first + 6, top = first + 11;
            for (int k = 0; k < 5; k++)
            {
                int a = ring1 + k, b = ring1 + (k + 1) % 5, c = ring2 + k, d = ring2 + (k + 1) % 5;
                triangles.Add(bottom); triangles.Add(b); triangles.Add(a);   // underside, facing down
                triangles.Add(a); triangles.Add(b); triangles.Add(c);        // sides
                triangles.Add(c); triangles.Add(b); triangles.Add(d);
                triangles.Add(c); triangles.Add(d); triangles.Add(top);      // top fan
            }

            Prism(vertices, triangles, trunk, Math.Max(cb + 0.3f, wy));
        }

        /// <summary>Appends a box (the collider box of a field-boundary piece, in its posed frame) to the lists.</summary>
        public static void Box(Vector3 centre, Vector3 size, List<Vector3> vertices, List<int> triangles)
        {
            int first = vertices.Count;
            Vector3 e = size * 0.5f;
            for (int i = 0; i < 8; i++) vertices.Add(centre + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
            int[] faces = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5 };
            for (int f = 0; f < 6; f++)
            {
                int a = first + faces[f * 4], b = first + faces[f * 4 + 1], c = first + faces[f * 4 + 2], d = first + faces[f * 4 + 3];
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }

        private static Vector3 Ring(int k, float shift, float radius, float y)
        {
            double a = (k + shift) * 2.0 * Math.PI / 5.0;
            return new Vector3((float)Math.Sin(a) * radius, y, (float)Math.Cos(a) * radius);
        }

        /// <summary>A three-sided trunk from the ground to <paramref name="height"/> (6 triangles, open ends: it is always inside the crown or the ground).</summary>
        private static void Prism(List<Vector3> vertices, List<int> triangles, float radius, float height)
        {
            int first = vertices.Count;
            for (int k = 0; k < 3; k++)
            {
                double a = k * 2.0 * Math.PI / 3.0;
                vertices.Add(new Vector3((float)Math.Sin(a) * radius, -0.2f, (float)Math.Cos(a) * radius));
                vertices.Add(new Vector3((float)Math.Sin(a) * radius * 0.7f, height, (float)Math.Cos(a) * radius * 0.7f));
            }

            for (int k = 0; k < 3; k++)
            {
                int a0 = first + 2 * k, a1 = a0 + 1, b0 = first + 2 * ((k + 1) % 3), b1 = b0 + 1;
                triangles.Add(a0); triangles.Add(a1); triangles.Add(b1);
                triangles.Add(a0); triangles.Add(b1); triangles.Add(b0);
            }
        }
    }

    /// <summary>
    /// Second performance pass (2026-10-06): the solid low-poly impostor a near tile's tree is drawn as beyond the impostor distance
    /// (<see cref="TreeDrawBand.Impostor"/>) — the same shape, proportions and colours as the detailed impostors of the distant tiles
    /// (<c>FarTileMeshData.AddTree</c>, detailed: broadleaf 23, pine 18, dead 5 triangles), at scale 1 with the pivot at the trunk base,
    /// drawn instanced with the far terrain's flat-shaded vertex-colour material. Its crown is the measured one (not shrunk like the
    /// shadow proxy), so its silhouette matches the mesh's; its colour is the middle of the style's two foliage colours (one mesh per
    /// prototype: the per-tree colour noise of the distant tiles is not reproduced). Never a card. Pure; vertices are not shared, so
    /// each face keeps one flat colour.
    /// </summary>
    public static class TreeImpostor
    {
        /// <summary>
        /// Radial scale on the measured crown radii: the five-sided rings are inscribed in the measured radius, so their flat sides
        /// fall short of it (cos 36° = 0.81); fitted on the silhouettes (see ENDLESS_ROAD.md, 2026-10-06).
        /// </summary>
        public const float CrownFit = 1.2f;

        /// <summary>Triangles of the impostor of <paramref name="shape"/>.</summary>
        public static int TrianglesOf(FarTreeShape shape) => shape == FarTreeShape.Conifer ? 18 : shape == FarTreeShape.Dead ? 5 : 23;

        /// <summary>Triangles of the impostor of <paramref name="style"/>: a lobed crown by its lobes (<see cref="TreeLobes"/>), else by shape.</summary>
        public static int TrianglesOf(FarTreeStyle style) => style.HasLobes ? TreeLobes.NearTriangles(style) : TrianglesOf(style.Shape);

        /// <summary>Metres one pixel spans at <paramref name="distance"/> for a vertical field of view and screen height in pixels.</summary>
        public static double PixelMetres(double distance, double verticalFov, int screenHeight) =>
            2.0 * distance * Math.Tan(verticalFov * 0.5 * Math.PI / 180.0) / Math.Max(1, screenHeight);

        /// <summary>
        /// The silhouette match rule: the nearest distance at which a silhouette difference of <paramref name="hausdorffMetres"/>
        /// (impostor against the lowest mesh LOD, seen level) spans at most <paramref name="maxPixels"/> pixels.
        /// </summary>
        public static double SwapDistance(double hausdorffMetres, double maxPixels, double verticalFov = 60.0, int screenHeight = 1080) =>
            hausdorffMetres <= 0.0 ? 0.0 : hausdorffMetres / Math.Max(1e-6, maxPixels * PixelMetres(1.0, verticalFov, screenHeight));

        /// <summary>
        /// The silhouette difference of each measured tree's impostor (at <see cref="CrownFit"/>) against its lowest mesh LOD, seen level
        /// from eight bearings at scale 1: the mean over the bearings of the Hausdorff distance between the two outlines, metres
        /// (offline harness, 2 cm raster, 2026-10-06). Lumpy crowns (the Meadow oak) stray furthest.
        /// </summary>
        private static readonly Dictionary<string, float> SilhouetteMetres = new Dictionary<string, float>
        {
            ["SM_Env_Tree_Birch_01"] = 1.23f, ["SM_Env_Tree_Birch_02"] = 1.21f, ["SM_Env_Tree_Birch_03"] = 0.62f,
            ["SM_Env_Tree_Meadow_01"] = 2.78f, ["SM_Env_Tree_Meadow_02"] = 1.63f,
            ["SM_Env_Tree_Fruit_01"] = 1.03f, ["SM_Env_Tree_Fruit_02"] = 1.01f, ["SM_Env_Tree_Fruit_03"] = 0.68f,
            ["SM_Gen_Env_Tree_01"] = 0.80f, ["SM_Gen_Env_Tree_02"] = 0.79f, ["SM_Gen_Env_Tree_03"] = 0.52f,
            ["SM_Gen_Env_Tree_Pine_01"] = 0.49f, ["SM_Gen_Env_Tree_Pine_02"] = 0.47f, ["SM_Gen_Env_Tree_Pine_03"] = 0.37f,
            ["SM_Gen_Env_Tree_Dead_01"] = 0.99f, ["SM_Gen_Env_Tree_Dead_02"] = 1.47f, ["SM_Gen_Env_Tree_Dead_03"] = 2.43f,
        };

        /// <summary>Silhouette difference assumed for a tree that was not measured, as a fraction of its height.</summary>
        public const float UnmeasuredSilhouetteShare = 0.2f;

        /// <summary>
        /// The measured silhouette difference of <paramref name="prefabName"/>'s impostor, metres (a palette copy whose name contains the
        /// Synty name counts); unmeasured trees: <see cref="UnmeasuredSilhouetteShare"/> of <paramref name="height"/>.
        /// </summary>
        public static float SilhouetteError(string prefabName, float height)
        {
            // Lobed crowns (impostor pass, 2026-10-06) carry their own measured difference (mesh clipped at the ground).
            if (FarTreeShapes.TryGetLobedSilhouette(prefabName, out float lobed, out _)) return lobed;
            if (!string.IsNullOrEmpty(prefabName))
            {
                foreach (KeyValuePair<string, float> entry in SilhouetteMetres)
                {
                    if (prefabName.Contains(entry.Key)) return entry.Value;
                }
            }

            return Math.Max(0.5f, UnmeasuredSilhouetteShare * height);
        }

        /// <summary>
        /// The impostor distance of one prototype: the frame's (<paramref name="frameImpostor"/>, already scaled; infinite: none) or,
        /// when its silhouette error spans more than <paramref name="maxPixels"/> 1080p pixels there, the distance where it spans that
        /// many (60° view). Infinite when there is no impostor band or <paramref name="maxPixels"/> ≤ 0 means "no swap rule"
        /// (then the frame's distance holds).
        /// </summary>
        public static float DistanceFor(float frameImpostor, float silhouetteMetres, float maxPixels)
        {
            if (float.IsInfinity(frameImpostor) || float.IsNaN(frameImpostor)) return float.PositiveInfinity;
            if (maxPixels <= 0f) return frameImpostor;
            return Math.Max(frameImpostor, (float)SwapDistance(silhouetteMetres, maxPixels));
        }

        /// <summary>The impostor's foliage colour (linear): halfway between the style's two colours.</summary>
        public static Vector3 LeafColor(FarTreeStyle style) => (style.Color + style.Color2) * 0.5f;

        /// <summary>Appends the impostor of a tree of <paramref name="style"/> (scale 1, pivot at the trunk base) to the lists.</summary>
        public static void Build(FarTreeStyle style, List<Vector3> vertices, List<int> triangles, List<Color32> colors, float crownScale = CrownFit)
        {
            float H = Math.Max(1f, style.Height), R = Math.Max(0.3f, style.Radius * crownScale);
            float cb = Math.Max(0f, Math.Min(style.CrownBase, H * 0.9f)), wy = Math.Max(cb, Math.Min(style.WidestAt, H * 0.98f));
            float tr = Math.Max(0.12f, style.TopRadius * crownScale), trunkR = Math.Max(0.12f, style.TrunkRadius);
            Vector3 leaf = LeafColor(style);
            if (style.HasLobes)
            {
                // Impostor pass (2026-10-06): domed lobes fitted on the mesh (five-sided rings), trunk and main fork.
                TreeLobes.Build(style.Lobes, style.ForkY, style.TrunkRadius, leaf, style.TrunkColor, TreeLobes.NearSides, true, vertices, colors, triangles, style.LeafCrown, style.SeasonClass);
                return;
            }

            Color32 mid = FarTileMeshData.EncodeSrgbFast(leaf), lit = FarTileMeshData.EncodeSrgbFast(leaf * 1.07f), dark = FarTileMeshData.EncodeSrgbFast(leaf * 0.8f);
            mid.a = lit.a = dark.a = TreeLobes.LeafAlpha(false, style.SeasonClass);
            if (style.LeafCrown)
            {
                // A leaf crown without lobes: the same lit-top ladder and foliage light as the lobes.
                byte alpha = TreeLobes.LeafAlpha(true, style.SeasonClass);
                mid = TreeLobes.LeafFace(leaf, TreeLobes.LeafMid, alpha);
                lit = TreeLobes.LeafFace(leaf, TreeLobes.LeafTop, alpha);
                dark = TreeLobes.LeafFace(leaf, TreeLobes.LeafUnder, alpha);
            }

            Color32 bark = FarTileMeshData.EncodeSrgbFast(style.TrunkColor);
            var b = new Faces(vertices, triangles, colors);
            switch (style.Shape)
            {
                case FarTreeShape.Conifer:
                {
                    float span = H - cb;
                    Vector3[] ring1 = Ring(5, 0f, cb + 0.04f * span, R), ring2 = Ring(5, 0.63f, cb + 0.42f * span, 0.62f * R);
                    b.Fan(new Vector3(0f, cb + 0.62f * span, 0f), ring1, mid, dark, true);
                    b.Fan(new Vector3(0f, cb - 0.06f * span, 0f), ring1, dark, dark, false);
                    b.Fan(new Vector3(0f, H, 0f), ring2, lit, mid, true);
                    b.Fan(new Vector3(0f, cb + 0.2f * span, 0f), Ring(3, 0.5f, 0f, trunkR), bark, bark, true);
                    return;
                }

                case FarTreeShape.Dead:
                {
                    b.Fan(new Vector3(0f, H, 0f), Ring(3, 0f, 0f, Math.Max(0.2f, trunkR * 1.4f)), bark, bark, true);
                    var dir = new Vector3((float)Math.Sin(1.1), 0f, (float)Math.Cos(1.1));
                    Vector3 f0 = new Vector3(0f, 0.45f * H, 0f), f1 = dir * (0.8f * R) + new Vector3(0f, 0.8f * H, 0f);
                    Vector3 f2 = -dir * (0.6f * R) + new Vector3(0f, 0.7f * H, 0f), f3 = new Vector3(0f, 0.62f * H, 0f);
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    b.Facing(f0, f1, f2, side, bark);
                    b.Facing(f3, f2, f1, -side, bark);
                    return;
                }

                default:
                {
                    Vector3[] ring1 = Ring(5, 0f, wy, R), ring2 = Ring(5, 0.5f, cb + 0.8125f * (H - cb), Math.Min(R, tr));
                    b.Fan(new Vector3(0f, cb, 0f), ring1, dark, dark, false);
                    for (int k = 0; k < 5; k++)
                    {
                        Vector3 r0 = ring1[k], r1 = ring1[(k + 1) % 5], u0 = ring2[k], u1 = ring2[(k + 1) % 5];
                        b.Outward(r0, r1, u0, mid);
                        b.Outward(u0, r1, u1, lit);
                    }

                    b.Fan(new Vector3(0f, H, 0f), ring2, lit, lit, true);
                    b.Fan(new Vector3(0f, cb + 0.3f * (wy - cb), 0f), Ring(3, 0.5f, 0f, trunkR), bark, bark, true);
                    return;
                }
            }
        }

        private static Vector3[] Ring(int sides, float shift, float y, float radius)
        {
            var ring = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                double a = (k + shift) * 2.0 * Math.PI / sides;
                ring[k] = new Vector3((float)Math.Sin(a) * radius, y, (float)Math.Cos(a) * radius);
            }

            return ring;
        }

        /// <summary>Unshared-vertex triangles, each facing away from the trunk axis (or up / down), one colour per face.</summary>
        private readonly struct Faces
        {
            private readonly List<Vector3> v;
            private readonly List<int> t;
            private readonly List<Color32> c;

            public Faces(List<Vector3> v, List<int> t, List<Color32> c)
            {
                this.v = v;
                this.t = t;
                this.c = c;
            }

            public void Fan(Vector3 point, Vector3[] ring, Color32 color, Color32 unused, bool above)
            {
                for (int k = 0; k < ring.Length; k++)
                {
                    Vector3 r0 = ring[k], r1 = ring[(k + 1) % ring.Length];
                    if (above) Outward(r0, r1, point, color);
                    else Outward(r0, point, r1, color);
                }
            }

            public void Outward(Vector3 a, Vector3 b, Vector3 cc, Color32 color)
            {
                Vector3 centroid = (a + b + cc) / 3f;
                var outward = new Vector3(centroid.x, 0f, centroid.z);
                Vector3 n = Vector3.Cross(b - a, cc - a);
                if (outward.sqrMagnitude < 1e-6f) outward = new Vector3(0f, n.y >= 0f ? 1f : -1f, 0f);
                Facing(a, b, cc, outward, color);
            }

            public void Facing(Vector3 a, Vector3 b, Vector3 cc, Vector3 outward, Color32 color)
            {
                Vector3 n = Vector3.Cross(b - a, cc - a);
                bool flip = n.x * outward.x + n.y * outward.y + n.z * outward.z < 0f;
                int first = v.Count;
                v.Add(a);
                v.Add(flip ? cc : b);
                v.Add(flip ? b : cc);
                for (int i = 0; i < 3; i++)
                {
                    c.Add(color);
                    t.Add(first + i);
                }
            }
        }
    }
}
