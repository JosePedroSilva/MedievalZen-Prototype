using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>What the per-frame walk needs to know about an instanced prototype (pure).</summary>
    public struct TreeWalkPrototype
    {
        /// <summary>A bush, shrub, fern, undergrowth or ivy: casts only within the plant shadow distance.</summary>
        public bool Plant;

        /// <summary>A fern: drawn only to the fern draw distance, each instance fading at its own threshold.</summary>
        public bool Fern;

        /// <summary>Woodland undergrowth (performance pass): drawn only to the undergrowth draw distance, fading like the ferns.</summary>
        public bool Undergrowth;

        /// <summary>Has a shadow proxy (casts beyond the proxy distance).</summary>
        public bool Proxy;

        /// <summary>Draws the same mesh in every band (no LODGroup, or one mesh LOD): its band is never decided per instance.</summary>
        public bool SingleMesh;

        /// <summary>
        /// Second performance pass (2026-10-06): the prototype has distances of its own (<see cref="Lod"/>: the frame's, with the
        /// impostor distance pushed out to where its impostor's silhouette matches its mesh, <see cref="TreeImpostor.SwapDistance"/>).
        /// </summary>
        public bool OwnLod;

        /// <summary>The prototype's own distances when <see cref="OwnLod"/>.</summary>
        public TreeLodDistances Lod;
    }

    /// <summary>
    /// A view frustum as inward-facing planes (n·p + d ≥ 0 inside), built in pure maths from the camera's pose so it can be widened by
    /// an angle (performance pass, 2026-10-05: a walk can then be reused for a few frames while the camera turns less than that angle).
    /// Only the four side planes and the far plane: the near plane culls nothing the side planes keep.
    /// </summary>
    public struct TreeFrustum
    {
        private Vector3 n0, n1, n2, n3, n4;
        private float d0, d1, d2, d3, d4;

        /// <summary>
        /// The frustum of a camera at <paramref name="eye"/> looking along <paramref name="forward"/> (up <paramref name="up"/>) with a
        /// vertical field of view of <paramref name="verticalFov"/> degrees and <paramref name="aspect"/> (width / height), each side
        /// opened by <paramref name="marginDegrees"/>, out to <paramref name="far"/>.
        /// </summary>
        public static TreeFrustum FromView(Vector3 eye, Vector3 forward, Vector3 up, float verticalFov, float aspect, float far, float marginDegrees)
        {
            Vector3 f = Normalised(forward, new Vector3(0f, 0f, 1f));
            Vector3 right = Normalised(Cross(up, f), new Vector3(1f, 0f, 0f));
            Vector3 u = Cross(f, right);
            double halfV = Math.Max(0.1, Math.Min(89.0, verticalFov * 0.5)) * Math.PI / 180.0;
            double halfH = Math.Atan(Math.Tan(halfV) * Math.Max(0.05f, aspect));
            double margin = Math.Max(0.0, marginDegrees) * Math.PI / 180.0;
            halfV = Math.Min(Math.PI * 0.5 - 1e-3, halfV + margin);
            halfH = Math.Min(Math.PI * 0.5 - 1e-3, halfH + margin);
            float cv = (float)Math.Cos(halfV), sv = (float)Math.Sin(halfV), ch = (float)Math.Cos(halfH), sh = (float)Math.Sin(halfH);
            var frustum = new TreeFrustum();
            // A side plane's inward normal: the forward axis tilted away from that side by the half angle's complement.
            frustum.n0 = right * ch + f * sh;      // left plane (normal points right/forward)
            frustum.n1 = -right * ch + f * sh;     // right plane
            frustum.n2 = u * cv + f * sv;          // bottom plane
            frustum.n3 = -u * cv + f * sv;         // top plane
            frustum.n4 = -f;                       // far plane
            frustum.d0 = -Dot(frustum.n0, eye);
            frustum.d1 = -Dot(frustum.n1, eye);
            frustum.d2 = -Dot(frustum.n2, eye);
            frustum.d3 = -Dot(frustum.n3, eye);
            frustum.d4 = Dot(f, eye) + Math.Max(1f, far);
            return frustum;
        }

        /// <summary>True when the box <paramref name="min"/>..<paramref name="max"/> grown by <paramref name="pad"/> on every side is not wholly outside a plane.</summary>
        public bool Intersects(Vector3 min, Vector3 max, float pad = 0f)
        {
            return Reach(n0, d0, min, max) >= -pad * L1(n0) && Reach(n1, d1, min, max) >= -pad * L1(n1) && Reach(n2, d2, min, max) >= -pad * L1(n2) &&
                   Reach(n3, d3, min, max) >= -pad * L1(n3) && Reach(n4, d4, min, max) >= -pad * L1(n4);
        }

        private static float L1(Vector3 n) => Math.Abs(n.x) + Math.Abs(n.y) + Math.Abs(n.z);

        /// <summary>
        /// True when the box swept along <paramref name="sweep"/> (every point of it moved by 0..1 × the sweep) and grown by
        /// <paramref name="pad"/> on every side is not wholly outside a plane: a shadow cast along the sweep can land in the frustum.
        /// </summary>
        public bool IntersectsSwept(Vector3 min, Vector3 max, Vector3 sweep, float pad)
        {
            return Swept(n0, d0, min, max, sweep) >= -pad * L1(n0) && Swept(n1, d1, min, max, sweep) >= -pad * L1(n1) && Swept(n2, d2, min, max, sweep) >= -pad * L1(n2) &&
                   Swept(n3, d3, min, max, sweep) >= -pad * L1(n3) && Swept(n4, d4, min, max, sweep) >= -pad * L1(n4);
        }

        private static float Swept(Vector3 n, float d, Vector3 min, Vector3 max, Vector3 sweep)
        {
            float reach = Reach(n, d, min, max);
            float moved = n.x * sweep.x + n.y * sweep.y + n.z * sweep.z;
            return moved > 0f ? reach + moved : reach;
        }

        /// <summary>The largest n·p + d over the box (its corner furthest along the normal).</summary>
        private static float Reach(Vector3 n, float d, Vector3 min, Vector3 max)
        {
            return n.x * (n.x >= 0f ? max.x : min.x) + n.y * (n.y >= 0f ? max.y : min.y) + n.z * (n.z >= 0f ? max.z : min.z) + d;
        }

        private static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        private static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        private static Vector3 Normalised(Vector3 v, Vector3 fallback)
        {
            float m = (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return m > 1e-6f ? new Vector3(v.x / m, v.y / m, v.z / m) : fallback;
        }
    }

    /// <summary>The inputs of one walk over the registered cells (pure).</summary>
    public struct TreeWalkFrame
    {
        public Vector3 Eye;
        public TreeFrustum Frustum;
        public TreeLodDistances Lod;

        /// <summary>Tree shadow distance in effect (0: none).</summary>
        public float ShadowDistance;
        public float ProxyDistance;
        public float PlantShadowDistance;
        public float FernDrawDistance;

        /// <summary>Woodland undergrowth is drawn to this distance (0: no limit), each instance fading at its own threshold.</summary>
        public float UndergrowthDrawDistance;
        public bool Shadows;

        /// <summary>Cull shadow casters whose shadow cannot reach the view (<see cref="TreeDrawRules.ShadowSweep"/>).</summary>
        public bool CullShadows;

        /// <summary>Direction the shadow-casting light travels (normalised; y &lt; 0 with the sun up).</summary>
        public Vector3 LightForward;

        /// <summary>Lowest height a shadow may land on (below every registered cell), scene metres.</summary>
        public float LowestReceiver;

        /// <summary>Extra metres on every cull test (the reuse tolerance: the eye may move this much before the next walk).</summary>
        public float Pad;

        /// <summary>Extra fraction of a sweep's length on the shadow test (the light may turn a little before the next walk).</summary>
        public float SweepPad;
    }

    /// <summary>What one walk did (pure counters, for the overlay and the benchmarks).</summary>
    public struct TreeWalkStats
    {
        public int Cells;
        public int CellsInView;
        public int CellsShadowOnly;

        /// <summary>Cells within the shadow distance, out of view, whose shadow cannot reach the view (not walked).</summary>
        public int CellsShadowCulled;

        /// <summary>Visible prototype lists added whole (one band for the whole cell).</summary>
        public int WholeLists;

        /// <summary>Instances decided one by one (distance, fade, hysteresis).</summary>
        public int PerInstance;

        /// <summary>Instances gathered (visible and shadow).</summary>
        public int Gathered;
    }

    /// <summary>
    /// The instance buffers of one frame, per bucket (a visible band or a shadow ring) and prototype: a growing array (no allocation
    /// once it has reached its working size) with the bounds of every slice of <see cref="TreeDrawRules.MaxInstancesPerCall"/>
    /// instances (one draw call each). Pure.
    /// </summary>
    public sealed class TreeGather
    {
        private Matrix4x4[][] data = Array.Empty<Matrix4x4[]>();
        private int[] counts = Array.Empty<int>();
        private Vector3[][] sliceMin = Array.Empty<Vector3[]>();
        private Vector3[][] sliceMax = Array.Empty<Vector3[]>();

        /// <summary>Buckets per prototype.</summary>
        public int Buckets { get; private set; }

        /// <summary>Prototypes.</summary>
        public int Prototypes { get; private set; }

        /// <summary>Makes room for <paramref name="buckets"/> × <paramref name="prototypes"/> lists (kept when unchanged) and empties them.</summary>
        public void Reset(int buckets, int prototypes)
        {
            int needed = buckets * prototypes;
            if (needed != counts.Length)
            {
                data = new Matrix4x4[needed][];
                counts = new int[needed];
                sliceMin = new Vector3[needed][];
                sliceMax = new Vector3[needed][];
                for (int k = 0; k < needed; k++)
                {
                    data[k] = new Matrix4x4[64];
                    sliceMin[k] = new Vector3[1];
                    sliceMax[k] = new Vector3[1];
                }
            }

            Buckets = buckets;
            Prototypes = prototypes;
            Clear();
        }

        /// <summary>Empties every list.</summary>
        public void Clear()
        {
            for (int k = 0; k < counts.Length; k++) counts[k] = 0;
        }

        /// <summary>The list index of a bucket and prototype.</summary>
        public int Key(int bucket, int prototype) => bucket * Prototypes + prototype;

        /// <summary>Instances in list <paramref name="k"/>.</summary>
        public int Count(int k) => counts[k];

        /// <summary>The instances of list <paramref name="k"/> (the first <see cref="Count"/> are valid).</summary>
        public Matrix4x4[] Data(int k) => data[k];

        /// <summary>Bounds of the instance positions of slice <paramref name="slice"/> of list <paramref name="k"/>.</summary>
        public void SliceBounds(int k, int slice, out Vector3 min, out Vector3 max)
        {
            min = sliceMin[k][slice];
            max = sliceMax[k][slice];
        }

        /// <summary>Adds one instance.</summary>
        public void Add(int k, ref Matrix4x4 m)
        {
            int n = counts[k];
            Ensure(k, n + 1);
            data[k][n] = m;
            var p = new Vector3(m.m03, m.m13, m.m23);
            Grow(k, n / TreeDrawRules.MaxInstancesPerCall, n % TreeDrawRules.MaxInstancesPerCall == 0, p, p);
            counts[k] = n + 1;
        }

        /// <summary>Adds every instance of <paramref name="source"/> (whose positions lie within <paramref name="min"/>..<paramref name="max"/>) in one copy.</summary>
        public void AddRange(int k, Matrix4x4[] source, Vector3 min, Vector3 max)
        {
            int count = source.Length;
            if (count == 0) return;
            int n = counts[k];
            Ensure(k, n + count);
            Array.Copy(source, 0, data[k], n, count);
            int first = n / TreeDrawRules.MaxInstancesPerCall, last = (n + count - 1) / TreeDrawRules.MaxInstancesPerCall;
            for (int s = first; s <= last; s++) Grow(k, s, s * TreeDrawRules.MaxInstancesPerCall >= n, min, max);
            counts[k] = n + count;
        }

        private void Ensure(int k, int needed)
        {
            if (needed > data[k].Length)
            {
                int size = data[k].Length;
                while (size < needed) size *= 2;
                Array.Resize(ref data[k], size);
            }

            int slices = (needed + TreeDrawRules.MaxInstancesPerCall - 1) / TreeDrawRules.MaxInstancesPerCall;
            if (slices > sliceMin[k].Length)
            {
                Array.Resize(ref sliceMin[k], Math.Max(slices, sliceMin[k].Length * 2));
                Array.Resize(ref sliceMax[k], sliceMin[k].Length);
            }
        }

        private void Grow(int k, int slice, bool first, Vector3 min, Vector3 max)
        {
            if (first)
            {
                sliceMin[k][slice] = min;
                sliceMax[k][slice] = max;
                return;
            }

            Vector3 a = sliceMin[k][slice], b = sliceMax[k][slice];
            sliceMin[k][slice] = new Vector3(min.x < a.x ? min.x : a.x, min.y < a.y ? min.y : a.y, min.z < a.z ? min.z : a.z);
            sliceMax[k][slice] = new Vector3(max.x > b.x ? max.x : b.x, max.y > b.y ? max.y : b.y, max.z > b.z ? max.z : b.z);
        }
    }

    /// <summary>
    /// The instanced renderer's per-frame pass over the registered cells (performance pass, 2026-10-05; pure, benchmarked offline).
    /// Per cell: the nearest and farthest distance to its bounds, the shadow ring, the frustum test, and for cells out of view the
    /// shadow sweep test (can a shadow cast from the cell land in the view?). A cell wholly inside one LOD band (beyond the hysteresis
    /// margins, <see cref="TreeDrawRules.UniformBand"/>) adds each prototype's list in one copy, as do single-mesh prototypes and
    /// shadow casters; only cells straddling a band edge and ferns near their fade decide per instance, with the same hysteresis as
    /// before. Fills a <see cref="TreeGather"/>; the renderer submits it.
    /// </summary>
    public sealed class TreeCellWalker
    {
        /// <summary>The buffers filled by <see cref="Walk"/>.</summary>
        public TreeGather Gather { get; } = new TreeGather();

        /// <summary>Counters of the last walk.</summary>
        public TreeWalkStats Stats;

        /// <summary>Gather buckets: one per visible band, then one per shadow ring.</summary>
        public const int BucketCount = TreeDrawRules.BandCount + TreeDrawRules.ShadowRingCount;

        /// <summary>Walks <paramref name="sets"/> for frame <paramref name="f"/>, filling <see cref="Gather"/>.</summary>
        public void Walk(IReadOnlyList<TreeCellSet> sets, in TreeWalkFrame f, TreeWalkPrototype[] prototypes)
        {
            int protoCount = prototypes.Length;
            if (Gather.Prototypes != protoCount || Gather.Buckets != BucketCount) Gather.Reset(BucketCount, protoCount);
            else Gather.Clear();
            Stats = default;
            Vector3 eye = f.Eye;
            bool canSweep = f.Shadows && f.CullShadows && TreeDrawRules.ShadowSweepPossible(f.LightForward);
            float maxSweep = 2f * f.ShadowDistance;
            for (int s = 0; s < sets.Count; s++)
            {
                TreeCellSet set = sets[s];
                float offsetX = set.Applied.x, offsetZ = set.Applied.z;
                TreeCell[] cells = set.Cells;
                for (int c = 0; c < cells.Length; c++)
                {
                    TreeCell cell = cells[c];
                    Stats.Cells++;
                    float near = (float)Math.Sqrt(TreeDrawRules.NearestSq(eye, cell.Min, cell.Max));
                    int ring = TreeDrawRules.ShadowRing(near, f.ShadowDistance, f.ProxyDistance, f.Shadows);
                    bool inView = f.Frustum.Intersects(cell.Min, cell.Max, f.Pad);
                    if (!inView)
                    {
                        cell.Stale = true;
                        if (ring < 0) continue;
                        if (canSweep)
                        {
                            Vector3 sweep = TreeDrawRules.ShadowSweep(f.LightForward, cell.Max.y - f.LowestReceiver, maxSweep);
                            float pad = f.Pad + f.SweepPad * sweep.magnitude;
                            if (!f.Frustum.IntersectsSwept(cell.Min, cell.Max, sweep, pad))
                            {
                                Stats.CellsShadowCulled++;
                                continue;
                            }
                        }

                        Stats.CellsShadowOnly++;
                    }
                    else
                    {
                        Stats.CellsInView++;
                    }

                    float far = inView ? (float)Math.Sqrt(TreeDrawRules.FarthestSq(eye, cell.Min, cell.Max)) : 0f;
                    byte uniform = inView ? TreeDrawRules.UniformBand(near, far, f.Lod) : TreeDrawRules.Unknown;
                    Matrix4x4[][] lists = cell.ByPrototype;
                    if (cell.UniformOf == null || cell.UniformOf.Length != protoCount)
                    {
                        cell.UniformOf = new byte[protoCount];
                        for (int p = 0; p < protoCount; p++) cell.UniformOf[p] = TreeDrawRules.Unknown;
                        cell.Stale = true;
                    }

                    byte[] uniformOf = cell.UniformOf;
                    bool stale = cell.Stale;
                    for (int p = 0; p < protoCount; p++)
                    {
                        Matrix4x4[] list = lists[p];
                        if (list == null) continue;
                        TreeWalkPrototype proto = prototypes[p];
                        bool casts = ring == 0 ? !proto.Plant || near <= f.PlantShadowDistance : ring == 1 && proto.Proxy;
                        if (casts)
                        {
                            Gather.AddRange(Gather.Key(TreeDrawRules.BandCount + ring, p), list, cell.Min, cell.Max);
                            Stats.Gathered += list.Length;
                        }

                        if (!inView) continue;
                        bool fade = false;
                        float maxDistance = proto.Fern ? f.FernDrawDistance : proto.Undergrowth ? f.UndergrowthDrawDistance : 0f;
                        if (maxDistance > 0f)
                        {
                            if (near > maxDistance) continue;
                            fade = far > maxDistance * TreeDrawRules.FernFadeStart;
                        }

                        TreeLodDistances lod = proto.OwnLod ? proto.Lod : f.Lod;
                        byte uni = !proto.OwnLod ? uniform : inView ? TreeDrawRules.UniformBand(near, far, lod) : TreeDrawRules.Unknown;
                        byte previousUniform = stale ? TreeDrawRules.Unknown : uniformOf[p];
                        uniformOf[p] = fade ? TreeDrawRules.Unknown : uni;
                        if (!fade && (uni != TreeDrawRules.Unknown || proto.SingleMesh))
                        {
                            byte band = uni != TreeDrawRules.Unknown ? uni : (byte)TreeDrawRules.BandFor(near, lod);
                            Gather.AddRange(Gather.Key(band, p), list, cell.Min, cell.Max);
                            Stats.WholeLists++;
                            Stats.Gathered += list.Length;
                            continue;
                        }

                        byte[] last = cell.LastBand[p];
                        for (int i = 0; i < list.Length; i++)
                        {
                            float dx = list[i].m03 - eye.x, dy = list[i].m13 - eye.y, dz = list[i].m23 - eye.z;
                            float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            Stats.PerInstance++;
                            if (fade && d > TreeDrawRules.FadeDistance(maxDistance, list[i].m03 - offsetX, list[i].m23 - offsetZ)) continue;
                            byte previous = previousUniform != TreeDrawRules.Unknown ? previousUniform : stale ? TreeDrawRules.Unknown : last[i];
                            TreeDrawBand band = uni != TreeDrawRules.Unknown ? (TreeDrawBand)uni : TreeDrawRules.BandFor(d, previous, lod);
                            last[i] = (byte)band;
                            Gather.Add(Gather.Key((int)band, p), ref list[i]);
                            Stats.Gathered++;
                        }
                    }

                    if (inView)
                    {
                        cell.Uniform = uniform;
                        cell.Stale = false;
                    }
                }
            }
        }
    }
}
