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

        /// <summary>Buckets <paramref name="trees"/> (tile-local positions) of a <paramref name="tileSize"/> tile.</summary>
        public static TreeCellSet Build(List<TreePlacement> trees, float tileSize, TreePrototypeIndex prototypes, float cellSize = DefaultCellSize)
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

                    float s = tree.scale > 0f ? tree.scale : 1f;
                    float r = prototypes.RadiusOf(proto) * s, h = prototypes.HeightOf(proto) * s;
                    var lo = new Vector3(tree.position.x - r, tree.position.y - 1f, tree.position.z - r);
                    var hi = new Vector3(tree.position.x + r, tree.position.y + h, tree.position.z + r);
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
            }

            var cells = new List<TreeCell>();
            for (int c = 0; c < lists.Length; c++)
            {
                if (counts[c] == 0) continue;
                var cell = new TreeCell { ByPrototype = new Matrix4x4[protoCount][], Min = mins[c], Max = maxs[c], Count = counts[c] };
                for (int p = 0; p < protoCount; p++)
                {
                    if (lists[c][p] != null) cell.ByPrototype[p] = lists[c][p].ToArray();
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

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>Which mesh LOD and shadow mode a tree cell is drawn with (in this order of priority).</summary>
    public enum TreeDrawBand
    {
        /// <summary>LOD0, nearest cells; casts shadows.</summary>
        Detail = 0,

        /// <summary>The LODGroup's middle LOD; casts shadows.</summary>
        Mid = 1,

        /// <summary>Lowest LOD inside the shadow distance (casts shadows).</summary>
        LowShadowed = 2,

        /// <summary>Lowest LOD beyond the shadow distance (no shadows).</summary>
        Low = 3,
    }

    /// <summary>Pure LOD / shadow / budget decisions of the instanced tree renderer (unit tested).</summary>
    public static class TreeDrawRules
    {
        /// <summary>RenderMeshInstanced takes at most this many instances per call.</summary>
        public const int MaxInstancesPerCall = 1023;

        /// <summary>Number of <see cref="TreeDrawBand"/> values.</summary>
        public const int BandCount = 4;

        /// <summary>
        /// The band a cell at <paramref name="distance"/> metres (camera to the cell's bounds) is drawn in: LOD0 within
        /// <paramref name="detailDistance"/>, the middle LOD within <paramref name="midDistance"/>, then the lowest LOD with
        /// shadows inside <paramref name="shadowDistance"/> and without beyond.
        /// </summary>
        public static TreeDrawBand BandFor(float distance, float detailDistance, float midDistance, float shadowDistance)
        {
            if (distance <= detailDistance) return TreeDrawBand.Detail;
            if (distance <= midDistance) return TreeDrawBand.Mid;
            return distance <= shadowDistance ? TreeDrawBand.LowShadowed : TreeDrawBand.Low;
        }

        /// <summary>Whether a band casts shadows (tree shadows on and the band inside the shadow distance).</summary>
        public static bool CastsShadows(TreeDrawBand band, bool shadowsEnabled) => shadowsEnabled && band != TreeDrawBand.Low;

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
}
