using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>How a rock-like family sits in the ground.</summary>
    public enum RockShape
    {
        /// <summary>Single boulder: random yaw, leans a little with the ground.</summary>
        Boulder,
        /// <summary>Pile of rocks: random yaw, leans a little.</summary>
        Pile,
        /// <summary>Flat outcrop slab: leans with the ground, mostly buried.</summary>
        Slab,
        /// <summary>Cliff block: upright, long side along the contour, face downhill, backed by higher ground.</summary>
        Cliff,
        /// <summary>Wide grassy mound: level ground only.</summary>
        Mound,
        /// <summary>Stone stack / pile (cairn): upright, level-ish ground.</summary>
        Cairn,
    }

    /// <summary>Size (in metres, not scale) and seating of one rock-like family.</summary>
    public sealed class RockSpec
    {
        /// <summary>Seating behaviour.</summary>
        public RockShape shape;
        /// <summary>Target total mesh height range, metres.</summary>
        public float heightMin;
        /// <summary>Target total mesh height range, metres.</summary>
        public float heightMax;
        /// <summary>Fraction of the ground's tilt the piece follows (0 = upright).</summary>
        public float tiltFollow;
        /// <summary>Largest tilt from vertical, degrees.</summary>
        public float maxTilt;
        /// <summary>Scale limits (low-poly meshes look wrong far from their modelled size).</summary>
        public float minScale = 0.35f;
        /// <summary>Scale limits.</summary>
        public float maxScale = 1.8f;

        /// <summary>Creates a spec.</summary>
        public RockSpec(RockShape shape, float heightMin, float heightMax, float tiltFollow, float maxTilt)
        {
            this.shape = shape;
            this.heightMin = heightMin;
            this.heightMax = heightMax;
            this.tiltFollow = tiltFollow;
            this.maxTilt = maxTilt;
        }
    }

    /// <summary>Why a candidate was rejected (counted for logs and tests).</summary>
    public enum RockReject
    {
        /// <summary>Accepted.</summary>
        None,
        /// <summary>No measured metrics for the variant.</summary>
        NoMetrics,
        /// <summary>Footprint touches a road corridor, settlement, field, water or the map edge.</summary>
        Blocked,
        /// <summary>Cliff without enough higher ground behind it.</summary>
        Unbacked,
        /// <summary>Cliff on a crest, knoll or summit (ground falls away on both sides).</summary>
        Convex,
        /// <summary>Sinking it far enough would bury most of it.</summary>
        Buried,
        /// <summary>Cannot be sized inside the family's height range and the hard cap.</summary>
        Size,
    }

    /// <summary>Numbers describing one placed rock-like piece against the ground (see <see cref="RockPlacer.Inspect"/>).</summary>
    public struct RockAudit
    {
        /// <summary>Height of the piece (m).</summary>
        public float height;
        /// <summary>World y of its top.</summary>
        public float top;
        /// <summary>Worst air gap (m) between the lowest mesh point of a footprint edge cell and the ground under that cell (&gt; 0 = a gap visible from outside).</summary>
        public float gap;
        /// <summary>Air gap of the whole piece by the brief's rule: lowest mesh point minus lowest ground under the footprint (&gt; 0 = gap).</summary>
        public float aabbGap;
        /// <summary>How far the top stands above the highest ground under the footprint (m).</summary>
        public float loom;
        /// <summary>Ground at the uphill edge of the footprint minus the piece's base, as a fraction of its height (cliffs).</summary>
        public float backing;
        /// <summary>True when the ground falls away on both sides along some axis (crest, knoll, summit).</summary>
        public bool convex;
        /// <summary>Largest horizontal extent (m).</summary>
        public float length;
    }

    /// <summary>
    /// Places rock-like props (boulders, piles, slabs, cliff blocks, mounds, cairns) from their measured shape
    /// (<see cref="PropMetrics"/>) so they cannot float or loom: sized in metres, seated so the lowest mesh point of every
    /// footprint cell is at least <see cref="EmbedFor"/> below the lowest ground under that cell, rocks tilted part-way to the
    /// ground, cliff blocks upright with their long side along the contour and their face downhill, only where the ground behind
    /// them rises to at least <see cref="MinBacking"/> of their height and never on convex ground. Pure; deterministic given the
    /// caller's random stream.
    /// </summary>
    public static class RockPlacer
    {
        /// <summary>Nothing rock-like is taller than this (m).</summary>
        public const float MaxHeight = 10f;

        /// <summary>A cliff block needs ground at its uphill edge at least this fraction of its height above its base.</summary>
        public const float MinBacking = 0.7f;

        /// <summary>Radius (fraction of the piece's footprint radius, at least 6 m) of the convexity test.</summary>
        private const float ConvexProbe = 0.6f;

        /// <summary>Default seating per family (heights in metres).</summary>
        public static RockSpec SpecFor(string family)
        {
            switch (family)
            {
                case PropRole.Rock: return new RockSpec(RockShape.Boulder, 0.6f, 2.5f, 0.5f, 18f) { minScale = 0.5f, maxScale = 1.6f };
                case PropRole.LowlandRock: return new RockSpec(RockShape.Boulder, 0.6f, 2.0f, 0.5f, 18f) { minScale = 0.5f, maxScale = 1.4f };
                case PropRole.RockSmallPile: return new RockSpec(RockShape.Pile, 0.15f, 0.4f, 0.8f, 20f) { minScale = 0.7f, maxScale = 1.6f };
                case PropRole.RockPile: return new RockSpec(RockShape.Pile, 1.2f, 3.5f, 0.4f, 12f) { minScale = 0.45f, maxScale = 1.3f };
                case PropRole.RockGround: return new RockSpec(RockShape.Slab, 1.2f, 3.2f, 0.8f, 22f) { minScale = 0.4f, maxScale = 1.1f };
                case PropRole.Cliff:
                case PropRole.GenericCliff:
                case PropRole.DirtCliff: return new RockSpec(RockShape.Cliff, 3f, 8f, 0f, 0f) { minScale = 0.3f, maxScale = 1f };
                case PropRole.GroundCliff: return new RockSpec(RockShape.Mound, 3f, 6f, 0f, 0f) { minScale = 0.5f, maxScale = 1f };
                case PropRole.Mound: return new RockSpec(RockShape.Mound, 1.5f, 3.2f, 0.6f, 6f) { minScale = 0.6f, maxScale = 1.1f };
                case PropRole.StoneStack:
                case PropRole.StonePile: return new RockSpec(RockShape.Cairn, 1.2f, 3f, 0.2f, 5f) { minScale = 0.35f, maxScale = 1.3f };
                default: return new RockSpec(RockShape.Boulder, 0.5f, 2.5f, 0.4f, 15f);
            }
        }

        /// <summary>
        /// Embed margin (m) for a piece of height <paramref name="height"/>: 0.3 m or 7-8 % of the height, whichever is more;
        /// pieces under 1.2 m sink a quarter of their height instead.
        /// </summary>
        public static float EmbedFor(float height, RockShape shape)
        {
            if (shape == RockShape.Cliff) return Mathf.Max(0.4f, 0.08f * height);
            if (shape == RockShape.Mound) return 0.3f;
            // Pebbles and small boulders: a quarter of their height (0.3 m would bury a 0.3 m pebble pile).
            return height < 1.2f ? 0.25f * height : Mathf.Max(0.3f, 0.07f * height);
        }

        /// <summary>Where pieces may go: the terrain plus a clearance test for footprint points.</summary>
        public sealed class Context
        {
            /// <summary>Creates a context over <paramref name="map"/>; <paramref name="clear"/> (world x, z) → false where nothing may stand.</summary>
            public Context(WorldGenParams p, float[,] map, PaletteKeys keys, Func<float, float, bool> clear)
            {
                P = p;
                Map = map;
                Keys = keys;
                Clear = clear;
            }

            /// <summary>Generation inputs.</summary>
            public WorldGenParams P { get; }
            /// <summary>Heightmap.</summary>
            public float[,] Map { get; }
            /// <summary>Palette keys (metrics).</summary>
            public PaletteKeys Keys { get; }
            /// <summary>Footprint clearance test (roads, settlements, fields, water); null = everywhere.</summary>
            public Func<float, float, bool> Clear { get; }
            /// <summary>Rejections by reason (for logs and tests).</summary>
            public readonly Dictionary<RockReject, int> Rejected = new Dictionary<RockReject, int>();

            /// <summary>Ground height (m).</summary>
            public float Ground(float x, float z) => HeightmapGenerator.SampleHeight(Map, P, x, z);

            internal void Count(RockReject reason)
            {
                Rejected.TryGetValue(reason, out int n);
                Rejected[reason] = n + 1;
            }
        }

        /// <summary>
        /// Tries to place <paramref name="variant"/> of <paramref name="family"/> with its footprint centred on (x, z), about
        /// <paramref name="targetHeight"/> metres tall (clamped to the family's range; cliffs shrink to fit their backing, down to
        /// the range minimum). <paramref name="yaw"/> is used for non-cliff shapes (cliffs face downhill, turned by
        /// <paramref name="yawJitter"/> degrees). Returns false with the reason counted in the context.
        /// </summary>
        public static bool TryPlace(Context ctx, string family, int variant, float x, float z, float targetHeight, float yaw, float yawJitter,
            out PropPlacement placement, out float radius)
        {
            placement = default;
            radius = 0f;
            if (!ctx.Keys.TryGetMetrics(family, variant, out PropMetrics m))
            {
                ctx.Count(RockReject.NoMetrics);
                return false;
            }

            RockSpec spec = SpecFor(family);
            float cap = Mathf.Min(MaxHeight, spec.heightMax * 1.15f);
            float h = Mathf.Clamp(targetHeight, spec.heightMin, spec.heightMax);
            float scale = Mathf.Clamp(h / Mathf.Max(0.01f, m.Height), spec.minScale, spec.maxScale);
            if (m.Height * scale > cap + 1e-3f || m.Height * scale < spec.heightMin * 0.8f)
            {
                ctx.Count(RockReject.Size);
                return false;
            }

            bool cliff = spec.shape == RockShape.Cliff;
            if (cliff)
            {
                float r = Mathf.Max(6f, m.FootprintRadius * scale);
                Vector2 down = Downhill(ctx, x, z, r);
                if (down.sqrMagnitude < 1e-6f)
                {
                    ctx.Count(RockReject.Unbacked);
                    return false;
                }

                yaw = Mathf.Atan2(down.x, down.y) * Mathf.Rad2Deg - m.faceYaw + yawJitter;
            }

            RockReject last = RockReject.Size;
            for (int attempt = 0; attempt < (cliff ? 4 : 1); attempt++)
            {
                if (attempt > 0)
                {
                    scale *= 0.82f;
                    if (m.Height * scale < spec.heightMin || scale < spec.minScale) break;
                }

                last = Seat(ctx, m, spec, x, z, yaw, scale, out placement);
                if (last != RockReject.None) continue;
                placement.role = family;
                placement.variant = variant;
                placement.settlementId = -1;
                radius = m.FootprintRadius * scale;
                return true;
            }

            ctx.Count(last);
            return false;
        }

        /// <summary>
        /// Seats <paramref name="variant"/> of <paramref name="family"/> at a fixed <paramref name="scale"/> and yaw with its footprint
        /// centred on (x, z) (for hand-composed features such as cairns); false when the family has no metrics or the spot
        /// would bury it.
        /// </summary>
        public static bool TrySeat(Context ctx, string family, int variant, float x, float z, float yaw, float scale, out PropPlacement placement)
        {
            placement = default;
            if (!ctx.Keys.TryGetMetrics(family, variant, out PropMetrics m)) return false;
            RockSpec spec = SpecFor(family);
            if (spec.shape == RockShape.Cliff) spec = new RockSpec(RockShape.Boulder, 0f, MaxHeight, 0f, 0f);
            if (Seat(ctx, m, spec, x, z, yaw, scale, out placement) != RockReject.None) return false;
            placement.role = family;
            placement.variant = variant;
            placement.settlementId = -1;
            return true;
        }

        /// <summary>Seats one piece at a given yaw and scale; returns the rejection reason or <see cref="RockReject.None"/>.</summary>
        private static RockReject Seat(Context ctx, PropMetrics m, RockSpec spec, float x, float z, float yaw, float scale, out PropPlacement placement)
        {
            placement = default;
            float height = m.Height * scale;
            Vector3 centre = new Vector3((m.min.x + m.max.x) * 0.5f, 0f, (m.min.z + m.max.z) * 0.5f) * scale;

            // Ground under the footprint (upright): plane fit for the tilt, clearance of every sample point.
            float pitch = 0f, roll = 0f;
            Vector3 up = Vector3.up;
            if (spec.tiltFollow > 0f && spec.maxTilt > 0f)
            {
                Vector3 normal = GroundNormal(ctx, m, x, z, scale);
                up = TiltTowards(normal, spec.tiltFollow, spec.maxTilt);
                AnglesFor(up, yaw, out pitch, out roll);
            }

            // Pivot so that the bounds centre lands on (x, z).
            Vector3 c = Rotate(centre, yaw, pitch, roll);
            float px = x - c.x;
            float pz = z - c.z;

            // Height: at every contact cell on the footprint's edge the lowest mesh point sits at least `embed` below the lowest
            // ground under that cell (no gap anyone could see under), and the pivot never above the mean ground (the modelled
            // seating; shells such as slabs and mounds would otherwise ride up on their skirts).
            float embed = EmbedFor(height, spec.shape);
            float y = float.PositiveInfinity;
            float groundMin = float.PositiveInfinity, groundMax = float.NegativeInfinity, groundSum = 0f;
            int groundCount = 0;
            float hx = (m.max.x - m.min.x) * scale / PropMetrics.Grid * 0.5f;
            float hz = (m.max.z - m.min.z) * scale / PropMetrics.Grid * 0.5f;
            for (int j = 0; j < PropMetrics.Grid; j++)
            {
                for (int i = 0; i < PropMetrics.Grid; i++)
                {
                    float lx = m.CellX(i) * scale, lz = m.CellZ(j) * scale;
                    float g = float.PositiveInfinity;
                    for (int k = 0; k < 5; k++)
                    {
                        float ox = k == 0 ? 0f : (k & 1) == 0 ? hx : -hx;
                        float oz = k == 0 ? 0f : k < 3 ? hz : -hz;
                        Vector3 w = Rotate(new Vector3(lx + ox, 0f, lz + oz), yaw, pitch, roll);
                        float wx = px + w.x, wz = pz + w.z;
                        if (ctx.Clear != null && !ctx.Clear(wx, wz)) return RockReject.Blocked;
                        if (wx < 1f || wz < 1f || wx > ctx.P.worldSize - 1f || wz > ctx.P.worldSize - 1f) return RockReject.Blocked;
                        float gk = ctx.Ground(wx, wz);
                        g = Mathf.Min(g, gk);
                        groundMax = Mathf.Max(groundMax, gk);
                        if (k == 0)
                        {
                            groundSum += gk;
                            groundCount++;
                        }
                    }

                    groundMin = Mathf.Min(groundMin, g);
                    if (!m.IsContact(i, j)) continue;
                    float oy = Rotate(new Vector3(lx, m.Underside(i, j) * scale, lz), yaw, pitch, roll).y;
                    y = Mathf.Min(y, g - embed - oy);
                }
            }

            if (float.IsPositiveInfinity(y)) return RockReject.NoMetrics;
            float groundMean = groundSum / Mathf.Max(1, groundCount);
            y = Mathf.Min(y, groundMean);
            // And the brief's whole-piece rule: its lowest point below the lowest ground anywhere under the footprint (covers
            // dips inside the footprint; normally already implied by the edge cells).
            y = Mathf.Min(y, groundMin - 0.05f - BottomOffset(m, scale, yaw, pitch, roll));
            float top = y + TopOffset(m, scale, yaw, pitch, roll);
            float bottom = y + BottomOffset(m, scale, yaw, pitch, roll);
            float visible = (m.max.y - Mathf.Max(m.min.y, Mathf.Min(0f, m.max.y * 0.5f))) * scale;

            switch (spec.shape)
            {
                case RockShape.Mound:
                    if (groundMax - groundMin > 0.45f * height) return RockReject.Buried;
                    break;
                case RockShape.Cliff:
                {
                    RockReject backing = CheckBacking(ctx, m, x, z, yaw, scale, bottom, height, top);
                    if (backing != RockReject.None) return backing;
                    break;
                }
                default:
                    // A good part of it shows above the ground it stands on.
                    if (top - groundMean < 0.4f * visible) return RockReject.Buried;
                    break;
            }

            placement = new PropPlacement { position = new Vector3(px, y, pz), yaw = yaw, pitch = pitch, roll = roll, scale = scale };
            return RockReject.None;
        }

        /// <summary>
        /// Backing and convexity of a cliff block: the ground along its uphill edge must reach <see cref="MinBacking"/> of its
        /// height above its base, must keep rising behind it (not a crest), and must not fall away on both sides along any
        /// axis (knoll, summit, ridge).
        /// </summary>
        private static RockReject CheckBacking(Context ctx, PropMetrics m, float x, float z, float yaw, float scale, float bottom, float height, float top)
        {
            // Face direction (downhill) and lateral axis in world space.
            float fy = (yaw + m.faceYaw) * Mathf.Deg2Rad;
            var face = new Vector2(Mathf.Sin(fy), Mathf.Cos(fy));
            var lateral = new Vector2(face.y, -face.x);
            Vector3 size = m.Size * scale;
            bool faceAlongZ = Mathf.Abs(Mathf.Cos(m.faceYaw * Mathf.Deg2Rad)) >= Mathf.Abs(Mathf.Sin(m.faceYaw * Mathf.Deg2Rad));
            float depth = faceAlongZ ? size.z : size.x;
            float width = faceAlongZ ? size.x : size.z;

            float minBack = float.PositiveInfinity;
            float ends = 0f, mid = 0f;
            for (int k = 0; k < 5; k++)
            {
                float t = (k / 4f - 0.5f) * width;
                float bx = x - face.x * depth * 0.5f + lateral.x * t;
                float bz = z - face.y * depth * 0.5f + lateral.y * t;
                float g = ctx.Ground(bx, bz);
                minBack = Mathf.Min(minBack, g);
                if (k == 0 || k == 4) ends += 0.5f * g;
                if (k == 2) mid = g;
            }

            if (minBack - bottom < MinBacking * height || top - minBack > (1f - MinBacking) * height + 0.05f) return RockReject.Unbacked;

            // Keeps rising behind the back edge (not just below a crest).
            float r = Mathf.Max(4f, depth);
            float behind = ctx.Ground(x - face.x * (depth * 0.5f + r), z - face.y * (depth * 0.5f + r));
            if (behind < mid + 0.25f * r) return RockReject.Convex;

            // The back edge must not wrap round a nose (ends much lower than the middle).
            if (mid - ends > 0.12f * width) return RockReject.Convex;
            return IsConvex(ctx, x, z, Mathf.Max(6f, ConvexProbe * m.FootprintRadius * scale)) ? RockReject.Convex : RockReject.None;
        }

        /// <summary>True when, along one of four axes through (x, z), the ground at ±<paramref name="r"/> is lower than at the centre on both sides by more than 0.1 r.</summary>
        public static bool IsConvex(Context ctx, float x, float z, float r)
        {
            float c = ctx.Ground(x, z);
            float drop = 0.1f * r;
            for (int a = 0; a < 4; a++)
            {
                float ang = a * Mathf.PI * 0.25f;
                float dx = Mathf.Cos(ang) * r, dz = Mathf.Sin(ang) * r;
                if (ctx.Ground(x + dx, z + dz) < c - drop && ctx.Ground(x - dx, z - dz) < c - drop) return true;
            }

            return false;
        }

        /// <summary>Unit downhill direction (x, z) of the ground around (x, z) over ±<paramref name="r"/> metres.</summary>
        public static Vector2 Downhill(Context ctx, float x, float z, float r)
        {
            float gx = ctx.Ground(x + r, z) - ctx.Ground(x - r, z);
            float gz = ctx.Ground(x, z + r) - ctx.Ground(x, z - r);
            var d = new Vector2(-gx, -gz);
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.zero;
        }

        /// <summary>
        /// Up vector tilted from vertical towards <paramref name="normal"/> by <paramref name="follow"/> of the angle between them,
        /// at most <paramref name="maxTilt"/> degrees (managed math only).
        /// </summary>
        public static Vector3 TiltTowards(Vector3 normal, float follow, float maxTilt)
        {
            normal = normal.normalized;
            float angle = Mathf.Acos(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg * Mathf.Clamp01(follow);
            angle = Mathf.Min(angle, maxTilt);
            var horizontal = new Vector3(normal.x, 0f, normal.z);
            if (horizontal.sqrMagnitude < 1e-10f || angle <= 0f) return Vector3.up;
            horizontal.Normalize();
            float a = angle * Mathf.Deg2Rad;
            return new Vector3(horizontal.x * Mathf.Sin(a), Mathf.Cos(a), horizontal.z * Mathf.Sin(a));
        }

        private static Vector3 GroundNormal(Context ctx, PropMetrics m, float x, float z, float scale)
        {
            float r = Mathf.Max(0.5f, 0.45f * Mathf.Max(m.max.x - m.min.x, m.max.z - m.min.z) * scale);
            float gx = ctx.Ground(x + r, z) - ctx.Ground(x - r, z);
            float gz = ctx.Ground(x, z + r) - ctx.Ground(x, z - r);
            return new Vector3(-gx / (2f * r), 1f, -gz / (2f * r)).normalized;
        }

        /// <summary>Pitch and roll (degrees) that turn the local up axis to <paramref name="up"/> under yaw <paramref name="yaw"/>.</summary>
        public static void AnglesFor(Vector3 up, float yaw, out float pitch, out float roll)
        {
            // R = Ry(yaw) Rx(pitch) Rz(roll): R*(0,1,0) in the yaw frame is (-sin r, cos r cos p, cos r sin p).
            Vector3 local = Rotate(up, -yaw, 0f, 0f);
            roll = -Mathf.Asin(Mathf.Clamp(local.x, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Atan2(local.z, local.y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// <paramref name="v"/> rotated like Unity's <c>Quaternion.Euler(pitch, yaw, roll) * v</c> (roll about Z, then pitch
        /// about X, then yaw about Y), in managed code so pure stages can use it.
        /// </summary>
        public static Vector3 Rotate(Vector3 v, float yaw, float pitch, float roll)
        {
            if (roll != 0f)
            {
                float r = roll * Mathf.Deg2Rad, cr = Mathf.Cos(r), sr = Mathf.Sin(r);
                v = new Vector3(v.x * cr - v.y * sr, v.x * sr + v.y * cr, v.z);
            }

            if (pitch != 0f)
            {
                float p = pitch * Mathf.Deg2Rad, cp = Mathf.Cos(p), sp = Mathf.Sin(p);
                v = new Vector3(v.x, v.y * cp - v.z * sp, v.y * sp + v.z * cp);
            }

            float a = yaw * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            return new Vector3(v.x * ca + v.z * sa, v.y, -v.x * sa + v.z * ca);
        }

        private static float TopOffset(PropMetrics m, float scale, float yaw, float pitch, float roll)
        {
            if (pitch == 0f && roll == 0f) return m.max.y * scale;
            float top = float.NegativeInfinity;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) == 0 ? m.min.x : m.max.x, (k & 2) == 0 ? m.min.y : m.max.y, (k & 4) == 0 ? m.min.z : m.max.z) * scale;
                top = Mathf.Max(top, Rotate(corner, yaw, pitch, roll).y);
            }

            return top;   // rotated box: slightly above the true top of a tilted mesh
        }

        private static float BottomOffset(PropMetrics m, float scale, float yaw, float pitch, float roll)
        {
            float bottom = float.PositiveInfinity;
            for (int j = 0; j < PropMetrics.Grid; j++)
            {
                for (int i = 0; i < PropMetrics.Grid; i++)
                {
                    float u = m.Underside(i, j);
                    if (u >= PropMetrics.Empty) continue;
                    bottom = Mathf.Min(bottom, Rotate(new Vector3(m.CellX(i), u, m.CellZ(j)) * scale, yaw, pitch, roll).y);
                }
            }

            return float.IsPositiveInfinity(bottom) ? m.min.y * scale : bottom;
        }

        /// <summary>
        /// Measures a placed piece against the ground: per-cell air gap, whole-piece gap (lowest point vs lowest ground under
        /// the footprint), how far it stands above the highest ground under it, backing at its uphill edge and whether it sits on
        /// convex ground. Pure; used by tests and the offline audit.
        /// </summary>
        public static RockAudit Inspect(Context ctx, PropMetrics m, PropPlacement placement)
        {
            float s = placement.scale;
            float yaw = placement.yaw, pitch = placement.pitch, roll = placement.roll;
            Vector3 pos = placement.position;
            float hx = (m.max.x - m.min.x) * s / PropMetrics.Grid * 0.5f;
            float hz = (m.max.z - m.min.z) * s / PropMetrics.Grid * 0.5f;
            float worstGap = float.NegativeInfinity;
            float lowest = float.PositiveInfinity, groundMin = float.PositiveInfinity, groundMax = float.NegativeInfinity;
            for (int j = 0; j < PropMetrics.Grid; j++)
            {
                for (int i = 0; i < PropMetrics.Grid; i++)
                {
                    float lx = m.CellX(i) * s, lz = m.CellZ(j) * s;
                    float g = float.PositiveInfinity;
                    for (int k = 0; k < 5; k++)
                    {
                        float ox = k == 0 ? 0f : (k & 1) == 0 ? hx : -hx;
                        float oz = k == 0 ? 0f : k < 3 ? hz : -hz;
                        Vector3 w = Rotate(new Vector3(lx + ox, 0f, lz + oz), yaw, pitch, roll);
                        float gk = ctx.Ground(pos.x + w.x, pos.z + w.z);
                        g = Mathf.Min(g, gk);
                        groundMax = Mathf.Max(groundMax, gk);
                    }

                    groundMin = Mathf.Min(groundMin, g);
                    float u = m.Underside(i, j);
                    if (u >= PropMetrics.Empty) continue;
                    float cellLow = pos.y + Rotate(new Vector3(lx, u * s, lz), yaw, pitch, roll).y;
                    lowest = Mathf.Min(lowest, cellLow);
                    if (m.IsContact(i, j)) worstGap = Mathf.Max(worstGap, cellLow - g);
                }
            }

            var audit = new RockAudit
            {
                height = m.Height * s,
                top = pos.y + TopOffset(m, s, yaw, pitch, roll),
                gap = worstGap,
                aabbGap = lowest - groundMin,
                length = m.Length * s,
            };
            audit.loom = audit.top - groundMax;
            float bottom = pos.y + BottomOffset(m, s, yaw, pitch, roll);
            audit.backing = audit.height > 0f ? (groundMax - bottom) / audit.height : 0f;
            Vector3 c = Rotate(new Vector3((m.min.x + m.max.x) * 0.5f, 0f, (m.min.z + m.max.z) * 0.5f) * s, yaw, pitch, roll);
            audit.convex = IsConvex(ctx, pos.x + c.x, pos.z + c.z, Mathf.Max(6f, ConvexProbe * m.FootprintRadius * s));
            return audit;
        }
    }
}
