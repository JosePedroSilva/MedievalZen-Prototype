using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>One straight stretch of field boundary along a line of a super-cell's grid, in absolute coordinates.</summary>
    public struct BoundaryEdge
    {
        /// <summary>Ends (absolute).</summary>
        public double X0, Z0, X1, Z1;
        /// <summary>What it is made of.</summary>
        public FieldEdgeKind Kind;
        /// <summary>Stable key (seeds its gaps).</summary>
        public long Key;
        /// <summary>Gate centres along the edge from (X0, Z0), metres (NaN: none).</summary>
        public float Gate0, Gate1;
        /// <summary>The super-cell it belongs to (its pieces stay inside it).</summary>
        public long CellI, CellJ;

        /// <summary>Length, metres.</summary>
        public double Length => Math.Sqrt((X1 - X0) * (X1 - X0) + (Z1 - Z0) * (Z1 - Z0));
    }

    /// <summary>
    /// Field boundaries of the farmland patchwork (endless only): every field of <see cref="FieldMosaic"/> is enclosed along the
    /// lines of its super-cell's grid, so neighbouring fields share one boundary (a strip of grass, the headland, lies between a
    /// field's crop and its boundary) and the network joins at the grid's corners. Each stretch between two cells is a dry-stone
    /// wall, a hedge or a fence, by an "estate" pattern of slow noise, the altitude and the nearness of downs or a pass (walls up
    /// high, hedges and fences in the lowland), varied per stretch. Gates sit where the field's gateway faces the road
    /// (<see cref="TileGenerator.Gateway"/>): an open gate between posts in a fence or a hedge, a gap between pillars in a wall.
    /// Old walls have gaps and tumbled stretches, fences lean, hedges vary in size. Pure; randomness streams 1210-1216; cached
    /// per super-cell, so every tile agrees.
    /// </summary>
    public sealed class FieldBoundaries
    {
        private const int StreamEstate = 1210, StreamKind = 1211, StreamPiece = 1212, StreamBreak = 1213, StreamLean = 1214, StreamGate = 1215;

        /// <summary>Hedge pieces are the 5.03 m Fantasy Kingdom hedge at about this scale (1.8 m tall), laid this far apart.</summary>
        public const float HedgeScale = 1.3f, HedgeSpacing = 5.6f;

        /// <summary>Width of a gate's opening, metres.</summary>
        public const float GateWidth = 3.6f;

        /// <summary>How far the largest hedge piece reaches beyond its share of the run (5.03 m × 1.3 × 1.12 against 5.6 m), metres.</summary>
        public const float HedgeOverhang = 0.9f;

        /// <summary>Length of the open fence-gate leaf (Meadow <c>Fence_Gate_01</c>, measured 2.28 m).</summary>
        public const float GateLeaf = 2.28f;

        /// <summary>Pieces are sunk this far into the ground (walls, hedges, fences).</summary>
        public const float WallSink = 0.1f, HedgeSink = 0.15f, FenceSink = 0.05f;

        /// <summary>Distant tiles draw the boundaries in stretches of this length, metres.</summary>
        public const float FarChunk = 8f;

        private readonly EndlessParams p;
        private readonly PaletteKeys keys;
        private readonly RoadCurve road;
        private readonly RegionSequence regions;
        private readonly TerrainField field;
        private readonly FieldMosaic mosaic;
        private readonly GradientNoise estate;
        private readonly ConcurrentDictionary<(long, long), BoundaryEdge[]> cache = new ConcurrentDictionary<(long, long), BoundaryEdge[]>();

        /// <summary>Creates the boundaries.</summary>
        public FieldBoundaries(EndlessParams p, PaletteKeys keys, RoadCurve road, RegionSequence regions, TerrainField field, FieldMosaic mosaic)
        {
            this.p = p;
            this.keys = keys;
            this.road = road;
            this.regions = regions;
            this.field = field;
            this.mosaic = mosaic;
            estate = new GradientNoise(EndlessNoise.Seed(p.seed, StreamEstate, 0));
        }

        /// <summary>Length of one piece of a boundary of <paramref name="kind"/> (the palette's measured fence and wall pieces), metres.</summary>
        public float PieceLength(FieldEdgeKind kind) => kind == FieldEdgeKind.Wall ? Math.Max(1f, keys.wallLength) : kind == FieldEdgeKind.Fence ? Math.Max(0.5f, keys.fenceLength) : HedgeSpacing;

        // ------------------------------------------------------------------ edges

        /// <summary>The edges of super-cell (<paramref name="ci"/>, <paramref name="cj"/>) (cached; do not modify).</summary>
        public BoundaryEdge[] Cell(long ci, long cj) => cache.GetOrAdd((ci, cj), Compute);

        /// <summary>Appends the edges whose bounds (grown by <paramref name="margin"/>) overlap [x0, x0 + size] × [z0, z0 + size].</summary>
        public void Overlapping(double x0, double z0, double size, double margin, List<BoundaryEdge> results)
        {
            long i0 = (long)Math.Floor((x0 - margin) / FieldMosaic.SuperCell), i1 = (long)Math.Floor((x0 + size + margin) / FieldMosaic.SuperCell);
            long j0 = (long)Math.Floor((z0 - margin) / FieldMosaic.SuperCell), j1 = (long)Math.Floor((z0 + size + margin) / FieldMosaic.SuperCell);
            for (long j = j0; j <= j1; j++)
            {
                for (long i = i0; i <= i1; i++)
                {
                    foreach (BoundaryEdge e in Cell(i, j))
                    {
                        if (Math.Max(e.X0, e.X1) + margin < x0 || Math.Min(e.X0, e.X1) - margin > x0 + size) continue;
                        if (Math.Max(e.Z0, e.Z1) + margin < z0 || Math.Min(e.Z0, e.Z1) - margin > z0 + size) continue;
                        results.Add(e);
                    }
                }
            }
        }

        private BoundaryEdge[] Compute((long, long) key)
        {
            (long ci, long cj) = key;
            MosaicField[] fields = mosaic.Cell(ci, cj);
            if (fields.Length == 0) return Array.Empty<BoundaryEdge>();
            MosaicGrid g = mosaic.GridOf(ci, cj);
            var cells = new Dictionary<(int, int), int>();
            for (int i = 0; i < fields.Length; i++) cells[(fields[i].A, fields[i].B)] = i;

            // Edge keys: (a, b, 0) is the line between columns a and a + 1 along row b; (a, b, 1) the line between rows b and b + 1
            // along column a. Each is drawn once when a field lies on either side.
            var keysSeen = new SortedSet<(int a, int b, int dir)>();
            var normal = new HashSet<(int, int)>();
            var cut = new HashSet<(int, int)>();   // cells cut into closes (the line between two of them is drawn once, by the lower cell)
            foreach (MosaicField f in fields)
            {
                if (!f.Close) normal.Add((f.A, f.B));
                else cut.Add((f.A, f.B));
            }

            foreach (MosaicField f in fields)
            {
                if (f.Close) continue;   // a cut cell's closes draw their own sides (below), never across the houses
                keysSeen.Add((f.A, f.B, 0));
                keysSeen.Add((f.A - 1, f.B, 0));
                keysSeen.Add((f.A, f.B, 1));
                keysSeen.Add((f.A, f.B - 1, 1));
            }

            var gates = new Dictionary<(int, int, int), List<float>>();
            foreach (MosaicField f in fields) AddGate(f, g, gates);

            var list = new List<BoundaryEdge>(keysSeen.Count);
            foreach ((int a, int b, int dir) in keysSeen)
            {
                double u0, v0, u1, v1;
                if (dir == 0)
                {
                    u0 = u1 = (a + 0.5) * g.CellU;
                    v0 = (b - 0.5) * g.CellV;
                    v1 = (b + 0.5) * g.CellV;
                }
                else
                {
                    v0 = v1 = (b + 0.5) * g.CellV;
                    u0 = (a - 0.5) * g.CellU;
                    u1 = (a + 0.5) * g.CellU;
                }

                g.ToWorld(u0, v0, out double x0, out double z0);
                g.ToWorld(u1, v1, out double x1, out double z1);
                long edgeKey = ((ci * 73856093L) ^ (cj * 19349663L)) * 8192 + ((long)(a + 64) * 128 + (b + 64)) * 2 + dir;
                var e = new BoundaryEdge { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Key = edgeKey, Gate0 = float.NaN, Gate1 = float.NaN, CellI = ci, CellJ = cj };
                e.Kind = KindAt((x0 + x1) * 0.5, (z0 + z1) * 0.5, edgeKey);
                if (gates.TryGetValue((a, b, dir), out List<float> at))
                {
                    at.Sort();
                    e.Gate0 = at[0];
                    if (at.Count > 1 && at[at.Count - 1] - at[0] > GateWidth + 6f) e.Gate1 = at[at.Count - 1];
                }

                list.Add(e);
            }

            // The village's closes (2026-10-06): the lines between the closes of a cut cell, each drawn once (on the side of the close
            // before it), so the inner ring reads as small enclosures; gateways where the lanes cross come from ClearOfPaths.
            var closeLines = new SortedSet<(int a, int b, int dir, int k, int along)>();
            foreach (MosaicField f in fields)
            {
                if (!f.Close) continue;
                // Each side of the close: line k + 1 of its cell's subdivision (k = -1 and n - 1 are the cell's own lines, drawn by the
                // close only where no field of the patchwork lies across them, which draws that line itself).
                if (f.Iu + 1 < f.Nu || !normal.Contains((f.A + 1, f.B))) closeLines.Add((f.A, f.B, 2, f.Iu, f.Iv));
                if (f.Iu > 0 || !normal.Contains((f.A - 1, f.B)) && !cut.Contains((f.A - 1, f.B))) closeLines.Add((f.A, f.B, 2, f.Iu - 1, f.Iv));
                if (f.Iv + 1 < f.Nv || !normal.Contains((f.A, f.B + 1))) closeLines.Add((f.A, f.B, 3, f.Iv, f.Iu));
                if (f.Iv > 0 || !normal.Contains((f.A, f.B - 1)) && !cut.Contains((f.A, f.B - 1))) closeLines.Add((f.A, f.B, 3, f.Iv - 1, f.Iu));
            }

            var closeSizes = new Dictionary<(int, int), (int nu, int nv)>();
            foreach (MosaicField f in fields)
            {
                if (f.Close) closeSizes[(f.A, f.B)] = (f.Nu, f.Nv);
            }

            foreach ((int a, int b, int dir, int k, int along) in closeLines)
            {
                (int nu, int nv) = closeSizes[(a, b)];
                double su = g.CellU / nu, sv = g.CellV / nv, u0, v0, u1, v1;
                if (dir == 2)
                {
                    u0 = u1 = (a - 0.5) * g.CellU + (k + 1) * su;
                    v0 = (b - 0.5) * g.CellV + along * sv;
                    v1 = v0 + sv;
                }
                else
                {
                    v0 = v1 = (b - 0.5) * g.CellV + (k + 1) * sv;
                    u0 = (a - 0.5) * g.CellU + along * su;
                    u1 = u0 + su;
                }

                g.ToWorld(u0, v0, out double x0, out double z0);
                g.ToWorld(u1, v1, out double x1, out double z1);
                long edgeKey = unchecked((((ci * 73856093L) ^ (cj * 19349663L)) * 8192 + ((long)(a + 64) * 128 + (b + 64)) * 2) * 256 + dir * 64 + (k + 1) * 8 + along) ^ 0x7C105EL;
                var e = new BoundaryEdge { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Key = edgeKey, Gate0 = float.NaN, Gate1 = float.NaN, CellI = ci, CellJ = cj };
                e.Kind = KindAt((x0 + x1) * 0.5, (z0 + z1) * 0.5, edgeKey);
                list.Add(e);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Records the gate of field <paramref name="f"/>: where its gateway (the ray from its centre toward the road point it faces)
        /// leaves the field, projected onto the grid line on that side, as metres along that edge.
        /// </summary>
        private static void AddGate(MosaicField f, MosaicGrid g, Dictionary<(int, int, int), List<float>> gates)
        {
            double gx = f.GateX - f.X, gz = f.GateZ - f.Z;
            if (gx * gx + gz * gz < 1e-6) return;
            // Field axes (as the grid): right = (cos, −sin), forward = (sin, cos).
            double u = gx * g.Cos - gz * g.Sin, v = gx * g.Sin + gz * g.Cos;
            double t = Math.Min(f.HalfX / Math.Max(1e-6, Math.Abs(u)), f.HalfZ / Math.Max(1e-6, Math.Abs(v)));
            // Exit point in grid coordinates (relative to the super-cell centre).
            double cu = (f.X - g.CentreX) * g.Cos - (f.Z - g.CentreZ) * g.Sin, cv = (f.X - g.CentreX) * g.Sin + (f.Z - g.CentreZ) * g.Cos;
            double eu = cu + u * t, ev = cv + v * t;
            bool alongU = Math.Abs(u) * f.HalfZ >= Math.Abs(v) * f.HalfX;   // leaves through a side facing ±right
            (int, int, int) key;
            float along;
            if (alongU)
            {
                key = u > 0 ? (f.A, f.B, 0) : (f.A - 1, f.B, 0);
                along = (float)(ev - (f.B - 0.5) * g.CellV);
                along = Mathf.Clamp(along, GateWidth, (float)g.CellV - GateWidth);
            }
            else
            {
                key = v > 0 ? (f.A, f.B, 1) : (f.A, f.B - 1, 1);
                along = (float)(eu - (f.A - 0.5) * g.CellU);
                along = Mathf.Clamp(along, GateWidth, (float)g.CellU - GateWidth);
            }

            if (!gates.TryGetValue(key, out List<float> list)) gates[key] = list = new List<float>(2);
            list.Add(along);
        }

        /// <summary>
        /// The boundary kind at absolute (x, z): walls high up and near downs or a pass, hedges and fences in the lowland, by a slow
        /// "estate" noise (whole groups of fields alike) varied per stretch.
        /// </summary>
        public FieldEdgeKind KindAt(double x, double z, long edgeKey)
        {
            double s = road.S(z);
            Region r = regions.Weights(s, out _, out Region other, out double ow);
            // Upland: the farmland next to downs or a pass (within 1.2 km of them along the road), and high farmland generally.
            double upland = 0.5 * Mathf.Clamp01((field.Height(x, z) - 110f) / 80f);
            foreach (Region n in new[] { regions.Get(r.Index - 1), regions.Get(r.Index + 1) })
            {
                double gap = Math.Min(Math.Abs(s - n.Start), Math.Abs(s - n.End));
                if ((n.Kind == RegionKind.Downs || n.Kind == RegionKind.HighlandPass) && gap < 1200.0) upland += 0.7 * (1.0 - gap / 1200.0);
            }

            if (other != null && (other.Kind == RegionKind.Downs || other.Kind == RegionKind.HighlandPass)) upland += 0.5 * ow;
            upland = Math.Min(1.2, upland);
            double e = EndlessNoise.Fbm(estate, x, z, 1.0 / 700.0, 2) / 0.16;   // about ±1
            double jitter = EndlessNoise.Unit(p.seed, StreamKind, edgeKey) - 0.5;
            double wall = 0.1 + 0.9 * upland + 0.3 * e + 0.5 * jitter;
            double hedge = 0.55 - 0.4 * upland - 0.25 * e + 0.5 * (EndlessNoise.Unit(p.seed, StreamKind, edgeKey, 1) - 0.5);
            double fence = 0.42 - 0.15 * upland + 0.1 * e + 0.5 * (EndlessNoise.Unit(p.seed, StreamKind, edgeKey, 2) - 0.5);
            if (wall >= hedge && wall >= fence && keys.Has(PropRole.StoneWall)) return FieldEdgeKind.Wall;
            if (hedge >= fence && keys.Has(PropRole.Hedge)) return FieldEdgeKind.Hedge;
            return keys.Has(PropRole.Fence) ? FieldEdgeKind.Fence : keys.Has(PropRole.Hedge) ? FieldEdgeKind.Hedge : FieldEdgeKind.Wall;
        }

        // ------------------------------------------------------------------ runs and pieces

        /// <summary>A straight run of one edge between gates (metres along the edge).</summary>
        private struct Run
        {
            public float From, To;
            public bool GateAtStart, GateAtEnd;
        }

        private static List<Run> Runs(BoundaryEdge e, float length)
        {
            // Hedge pieces are scaled a little larger than their spacing (they overlap): their runs stop short of a gate by the overlap.
            float halfGap = GateWidth * 0.5f + (e.Kind == FieldEdgeKind.Hedge ? HedgeOverhang : 0f);
            var gates = new List<float>(2);
            if (!float.IsNaN(e.Gate0)) gates.Add(e.Gate0);
            if (!float.IsNaN(e.Gate1)) gates.Add(e.Gate1);
            var runs = new List<Run>(3);
            float from = 0f;
            bool gateBefore = false;
            foreach (float g in gates)
            {
                runs.Add(new Run { From = from, To = g - halfGap, GateAtStart = gateBefore, GateAtEnd = true });
                from = g + halfGap;
                gateBefore = true;
            }

            runs.Add(new Run { From = from, To = length, GateAtStart = gateBefore, GateAtEnd = false });
            return runs;
        }

        /// <summary>
        /// Adds a circle to <paramref name="occupancy"/> (window-local) over every piece position of every edge near the window, so the
        /// scatter keeps its trees and rocks off the boundaries. Pure geometry of the edges (no checks), so near and distant tiles agree.
        /// </summary>
        public void Occupy(GenWindow w, OccupancyMap occupancy)
        {
            var edges = new List<BoundaryEdge>();
            Overlapping(w.X0, w.Z0, w.Size, 4.0, edges);
            foreach (BoundaryEdge e in edges)
            {
                float len = (float)e.Length;
                float piece = PieceLength(e.Kind);
                int n = Math.Max(1, (int)Math.Ceiling(len / piece));
                float step = len / n;
                double dx = (e.X1 - e.X0) / len, dz = (e.Z1 - e.Z0) / len;
                for (int i = 0; i < n; i++)
                {
                    double t = (i + 0.5) * step;
                    float lx = w.LocalX(e.X0 + dx * t), lz = w.LocalZ(e.Z0 + dz * t);
                    if (lx < -step || lz < -step || lx > w.Size + step || lz > w.Size + step) continue;
                    occupancy.Add(new Vector3(lx, 0f, lz), step * 0.5f + 0.4f);
                }
            }
        }

        /// <summary>What a window needs to check pieces against.</summary>
        public sealed class Context
        {
            /// <summary>The window.</summary>
            public GenWindow W;
            /// <summary>Absolute corner and edge of the tile (pieces whose centre is in it are kept).</summary>
            public double Tx, Tz;
            /// <summary>Tile edge, metres.</summary>
            public float Tile;
            /// <summary>Settlement discs in the window (local).</summary>
            public IReadOnlyList<SettlementSite> Sites;
            /// <summary>Fields in the window (local): mosaic and settlement fields.</summary>
            public IReadOnlyList<FieldPatch> Fields;
            /// <summary>Planted tree trunks in the window (local x, z): walls and fences keep 1 m off them; hedges may hold them.</summary>
            public IReadOnlyList<Vector2> Trunks;
            /// <summary>Streams near the window.</summary>
            public IReadOnlyList<StreamCourse> Streams;
            /// <summary>
            /// Worn paths in the window (absolute segment and width): a settlement's lanes, pasture gateways, ford tracks. Where one
            /// crosses a boundary the pieces leave a gateway (2026-10-06).
            /// </summary>
            public IReadOnlyList<(double x0, double z0, double x1, double z1, float width)> Paths;
        }

        /// <summary>
        /// The near tile's boundary pieces (window-local, appended to <paramref name="pieces"/>): every piece of every edge whose
        /// centre lies in the tile and passes the checks (off the road corridor, dry, not on a stream bank or in a settlement, not
        /// cutting into a field, gentle ground, walls and fences off the planted trunks), laid on the ground and tilted with it.
        /// </summary>
        public void Pieces(Context ctx, List<PropPlacement> pieces)
        {
            GenWindow w = ctx.W;
            var edges = new List<BoundaryEdge>();
            Overlapping(ctx.Tx, ctx.Tz, ctx.Tile, 8.0, edges);
            foreach (BoundaryEdge e in edges)
            {
                float len = (float)e.Length;
                double dx = (e.X1 - e.X0) / len, dz = (e.Z1 - e.Z0) / len;
                float yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                float piece = PieceLength(e.Kind);
                // An old wall sometimes has a broken stretch of a few pieces.
                double breakRoll = EndlessNoise.Unit(p.seed, StreamBreak, e.Key);
                int breakStart = breakRoll < 0.12 ? (int)(EndlessNoise.Unit(p.seed, StreamBreak, e.Key, 1) * 12.0) : -100;
                int breakLength = 2 + (int)(EndlessNoise.Unit(p.seed, StreamBreak, e.Key, 2) * 3.0);
                int counter = 0;
                foreach (Run run in Runs(e, len))
                {
                    float runLength = run.To - run.From;
                    if (runLength > piece * 0.45f)
                    {
                        int n = Math.Max(1, (int)Math.Ceiling(runLength / piece - 0.15f));
                        float step = runLength / n;
                        for (int i = 0; i < n; i++, counter++)
                        {
                            double t = run.From + (i + 0.5) * step;
                            if (e.Kind == FieldEdgeKind.Wall && counter >= breakStart && counter < breakStart + breakLength) continue;
                            AddPiece(ctx, e, t, step, dx, dz, yaw, counter, pieces);
                        }

                        // Fence posts at the ends of every fence run (corners and gate sides).
                        if (e.Kind == FieldEdgeKind.Fence)
                        {
                            AddFurniture(ctx, PropRole.FencePost, e, run.From, dx, dz, yaw, 0f, pieces);
                            AddFurniture(ctx, PropRole.FencePost, e, run.To, dx, dz, yaw, 0f, pieces);
                        }
                    }

                    if (run.GateAtEnd) AddGate(ctx, e, run.To + (e.Kind == FieldEdgeKind.Hedge ? HedgeOverhang : 0f), dx, dz, yaw, pieces);
                }
            }
        }

        private void AddPiece(Context ctx, BoundaryEdge e, double t, float step, double dx, double dz, float yaw, int index, List<PropPlacement> pieces)
        {
            GenWindow w = ctx.W;
            double ax = e.X0 + dx * t, az = e.Z0 + dz * t;
            if (!InTile(ctx, ax, az)) return;
            ulong h = EndlessNoise.Hash(p.seed, StreamPiece, e.Key, index);
            double u = EndlessNoise.Unit(h);
            string role;
            float scale = 1f, roll = 0f, sink, yawJitter = 0f, half = step * 0.5f;
            switch (e.Kind)
            {
                case FieldEdgeKind.Wall:
                    if (u < 0.035) return;   // a gap
                    role = u < 0.095 && keys.Has(PropRole.StoneWallSmall) ? PropRole.StoneWallSmall : PropRole.StoneWall;
                    sink = role == PropRole.StoneWall ? WallSink : 0.3f;   // tumbled stretches lie lower
                    if (role == PropRole.StoneWallSmall)
                    {
                        yawJitter = (float)(EndlessNoise.Unit(h ^ 0x51ED27UL) - 0.5) * 12f;
                        roll = (float)(EndlessNoise.Unit(h ^ 0x2545F491UL) - 0.5) * 8f;
                    }

                    break;
                case FieldEdgeKind.Fence:
                    if (u < 0.04) return;
                    role = PropRole.Fence;
                    sink = FenceSink;
                    if (u < 0.075) roll = (EndlessNoise.Unit(p.seed, StreamLean, e.Key, index) < 0.5 ? -1f : 1f) * (8f + 6f * (float)EndlessNoise.Unit(h ^ 0x68E31DA4UL));
                    break;
                default:
                    if (u < 0.05) return;
                    role = PropRole.Hedge;
                    sink = HedgeSink;
                    scale = HedgeScale * (0.88f + 0.24f * (float)EndlessNoise.Unit(h ^ 0x1B873593UL));
                    yawJitter = (float)(EndlessNoise.Unit(h ^ 0x9E3779B9UL) - 0.5) * 6f;
                    break;
            }

            if (!keys.Has(role)) return;
            double x0 = ax - dx * half, z0 = az - dz * half, x1 = ax + dx * half, z1 = az + dz * half;
            if (!PieceFits(ctx, e.Kind, ax, az, x0, z0, x1, z1)) return;
            float lx = w.LocalX(ax), lz = w.LocalZ(az);
            float h0 = HeightmapGenerator.SampleHeight(w.Map, w.Params, w.LocalX(x0), w.LocalZ(z0));
            float h1 = HeightmapGenerator.SampleHeight(w.Map, w.Params, w.LocalX(x1), w.LocalZ(z1));
            float hc = HeightmapGenerator.SampleHeight(w.Map, w.Params, lx, lz);
            float mid = (h0 + h1) * 0.5f;
            float y = Math.Min(mid, hc) - sink - 0.5f * Math.Max(0f, Math.Abs(hc - mid) - 0.1f);
            float pitch = -(float)(Math.Atan2(h1 - h0, Math.Max(0.5f, step)) * 180.0 / Math.PI);
            int variant = keys.PickVariant(role, new System.Random((int)(h & 0x7fffffff)));
            pieces.Add(new PropPlacement { role = role, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw + yawJitter, pitch = pitch, roll = roll, scale = scale, settlementId = -1 });
        }

        /// <summary>A post or pillar standing upright at metres <paramref name="t"/> along the edge.</summary>
        private void AddFurniture(Context ctx, string role, BoundaryEdge e, double t, double dx, double dz, float yaw, float side, List<PropPlacement> pieces)
        {
            if (!keys.Has(role)) return;
            GenWindow w = ctx.W;
            double ax = e.X0 + dx * t + dz * side, az = e.Z0 + dz * t - dx * side;
            if (!InTile(ctx, ax, az) || !PieceFits(ctx, e.Kind, ax, az, ax, az, ax, az)) return;
            float lx = w.LocalX(ax), lz = w.LocalZ(az);
            float y = ScatterPlanner.GroundHeight(w.Params, w.Map, lx, lz, 0.4f) - FenceSink;
            int variant = keys.PickVariant(role, new System.Random(EndlessNoise.Seed(p.seed, StreamGate, e.Key, (long)Math.Round(t * 10.0))));
            pieces.Add(new PropPlacement { role = role, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = 1f, settlementId = -1 });
        }

        /// <summary>
        /// A gate whose opening starts at metres <paramref name="t"/>: pillars either side in a wall; in a fence (and in half the
        /// hedges) posts and an open wooden gate swung back against the boundary, so the way is always open.
        /// </summary>
        private void AddGate(Context ctx, BoundaryEdge e, double t, double dx, double dz, float yaw, List<PropPlacement> pieces)
        {
            double far = t + GateWidth;
            if (e.Kind == FieldEdgeKind.Wall)
            {
                AddFurniture(ctx, PropRole.StoneWallPillar, e, t - 0.2, dx, dz, yaw, 0f, pieces);
                AddFurniture(ctx, PropRole.StoneWallPillar, e, far + 0.2, dx, dz, yaw, 0f, pieces);
                return;
            }

            bool leaf = e.Kind == FieldEdgeKind.Fence || EndlessNoise.Unit(p.seed, StreamGate, e.Key, 7) < 0.5;
            if (!leaf) return;
            AddFurniture(ctx, PropRole.FencePost, e, t, dx, dz, yaw, 0f, pieces);
            AddFurniture(ctx, PropRole.FencePost, e, far, dx, dz, yaw, 0f, pieces);
            if (!keys.Has(PropRole.FenceGate)) return;
            // The leaf hangs on the post at t and is swung 75-105° to one side of the boundary.
            double turn = (EndlessNoise.Unit(p.seed, StreamGate, e.Key, 8) < 0.5 ? -1.0 : 1.0) * (75.0 + 30.0 * EndlessNoise.Unit(p.seed, StreamGate, e.Key, 9));
            double rad = turn * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            // Rotate the run direction about Y by `turn` (positive yaw turns +Z toward +X).
            double ox = dx * cs + dz * sn, oz = -dx * sn + dz * cs;
            double hx = e.X0 + dx * t, hz = e.Z0 + dz * t;
            double cx = hx + ox * GateLeaf * 0.5, cz = hz + oz * GateLeaf * 0.5;
            GenWindow w = ctx.W;
            if (!InTile(ctx, cx, cz)) return;
            float lx = w.LocalX(cx), lz = w.LocalZ(cz);
            int i = w.Cover.Index(lx, lz);
            if (w.Cover.IsWater(i) || !w.Roads.IsClear(lx, lz, w.Params.ScatterCorridor + 0.5f)) return;
            float y = HeightmapGenerator.SampleHeight(w.Map, w.Params, lx, lz) - FenceSink;
            pieces.Add(new PropPlacement { role = PropRole.FenceGate, variant = 0, position = new Vector3(lx, y, lz), yaw = yaw + (float)turn, scale = 1f, settlementId = -1 });
        }

        private static bool InTile(Context ctx, double x, double z) => x >= ctx.Tx && x < ctx.Tx + ctx.Tile && z >= ctx.Tz && z < ctx.Tz + ctx.Tile;

        /// <summary>The checks of a piece from (x0, z0) to (x1, z1) centred on (x, z) (absolute).</summary>
        private bool PieceFits(Context ctx, FieldEdgeKind kind, double x, double z, double x0, double z0, double x1, double z1)
        {
            GenWindow w = ctx.W;
            float corridor = w.Params.ScatterCorridor + 1.5f;
            foreach ((double px, double pz) in new[] { (x, z), (x0, z0), (x1, z1) })
            {
                float lx = w.LocalX(px), lz = w.LocalZ(pz);
                if (lx < 0.5f || lz < 0.5f || lx > w.Size - 0.5f || lz > w.Size - 0.5f) return false;
                if (!w.Roads.IsClear(lx, lz, corridor)) return false;
            }

            float cx = w.LocalX(x), cz = w.LocalZ(z);
            int i = w.Cover.Index(cx, cz);
            if (w.Cover.IsWater(i) || w.Cover.Shore[i] > 0.35f || w.Cover.Slope[i] > 0.5f) return false;
            if (w.Cover.WaterHeight > 0f && HeightmapGenerator.SampleHeight(w.Map, w.Params, cx, cz) < w.Cover.WaterHeight + 0.4f) return false;
            if (ScatterPlanner.InsideSettlement(ctx.Sites, cx, cz, 3f)) return false;
            if (ScatterPlanner.InsideField(ctx.Fields, cx, cz, -0.45f)) return false;
            if (!ClearOfStreams(ctx.Streams, x, z, 4.5f)) return false;
            if (!ClearOfPaths(ctx.Paths, x, z, GateWidth * 0.5f)) return false;
            if (kind != FieldEdgeKind.Hedge && ctx.Trunks != null)
            {
                float ax = w.LocalX(x0), az = w.LocalZ(z0), bx = w.LocalX(x1), bz = w.LocalZ(z1);
                foreach (Vector2 trunk in ctx.Trunks)
                {
                    if (Math.Abs(trunk.x - cx) > 4f || Math.Abs(trunk.y - cz) > 4f) continue;
                    if (SegmentDistance(trunk.x, trunk.y, ax, az, bx, bz) < 1.1f) return false;
                }
            }

            return true;
        }

        /// <summary>True when (x, z) is at least <paramref name="margin"/> outside every stream's ribbon.</summary>
        /// <summary>True when (x, z) (absolute) keeps <paramref name="margin"/> beyond the half width of every path.</summary>
        public static bool ClearOfPaths(IReadOnlyList<(double x0, double z0, double x1, double z1, float width)> paths, double x, double z, float margin)
        {
            if (paths == null) return true;
            foreach (var (x0, z0, x1, z1, width) in paths)
            {
                double ex = x1 - x0, ez = z1 - z0, len2 = ex * ex + ez * ez;
                double t = len2 > 1e-9 ? Math.Max(0.0, Math.Min(1.0, ((x - x0) * ex + (z - z0) * ez) / len2)) : 0.0;
                double dx = x - (x0 + ex * t), dz = z - (z0 + ez * t), r = width * 0.5 + margin;
                if (dx * dx + dz * dz < r * r) return false;
            }

            return true;
        }

        public static bool ClearOfStreams(IReadOnlyList<StreamCourse> streams, double x, double z, float margin)
        {
            if (streams == null) return true;
            foreach (StreamCourse c in streams)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    double dx = x - c.X[i], dz = z - c.Z[i];
                    double r = c.HalfRibbon[i] + margin;
                    if (Math.Abs(dx) < r && Math.Abs(dz) < r && dx * dx + dz * dz < r * r) return false;
                }
            }

            return true;
        }

        private static float SegmentDistance(float px, float pz, float ax, float az, float bx, float bz)
        {
            float ex = bx - ax, ez = bz - az, len2 = ex * ex + ez * ez;
            float t = len2 > 1e-6f ? Mathf.Clamp01(((px - ax) * ex + (pz - az) * ez) / len2) : 0f;
            float dx = px - (ax + ex * t), dz = pz - (az + ez * t);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ distant tiles

        /// <summary>
        /// The boundaries a distant tile draws (tile-local, appended to <paramref name="results"/>): each edge cut into
        /// <see cref="FarChunk"/> stretches without the gate openings, the stretches whose middle is in the tile and passes the
        /// coarse checks (road corridor, water, settlements, streams).
        /// </summary>
        public void FarEdges(GenWindow w, double tx, double tz, float tile, IReadOnlyList<SettlementSite> sites, IReadOnlyList<StreamCourse> streams, List<FieldEdge> results)
        {
            var edges = new List<BoundaryEdge>();
            Overlapping(tx, tz, tile, FarChunk, edges);
            float corridor = w.Params.ScatterCorridor + 1.5f;
            foreach (BoundaryEdge e in edges)
            {
                float len = (float)e.Length;
                double dx = (e.X1 - e.X0) / len, dz = (e.Z1 - e.Z0) / len;
                foreach (Run run in Runs(e, len))
                {
                    float runLength = run.To - run.From;
                    if (runLength < 1f) continue;
                    int n = Math.Max(1, (int)Math.Ceiling(runLength / FarChunk));
                    float step = runLength / n;
                    for (int i = 0; i < n; i++)
                    {
                        double a = run.From + i * step, b = a + step, m = (a + b) * 0.5;
                        double mx = e.X0 + dx * m, mz = e.Z0 + dz * m;
                        if (mx < tx || mx >= tx + tile || mz < tz || mz >= tz + tile) continue;
                        float lx = w.LocalX(mx), lz = w.LocalZ(mz);
                        if (lx < 0f || lz < 0f || lx > w.Size || lz > w.Size) continue;
                        if (!w.Roads.IsClear(lx, lz, corridor)) continue;
                        int c = w.Cover.Index(lx, lz);
                        if (w.Cover.IsWater(c) || w.Cover.Shore[c] > 0.35f) continue;
                        if (ScatterPlanner.InsideSettlement(sites, lx, lz, 3f)) continue;
                        if (!ClearOfStreams(streams, mx, mz, 4.5f)) continue;
                        results.Add(new FieldEdge
                        {
                            X0 = (float)(e.X0 + dx * a - tx), Z0 = (float)(e.Z0 + dz * a - tz),
                            X1 = (float)(e.X0 + dx * b - tx), Z1 = (float)(e.Z0 + dz * b - tz),
                            Kind = e.Kind,
                        });
                    }
                }
            }
        }
    }
}
