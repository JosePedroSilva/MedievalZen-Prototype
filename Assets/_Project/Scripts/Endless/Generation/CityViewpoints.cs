using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// One street-level (or distant) view of a walled city for the places tour (2026-10-09: the user found every fault of the hill
    /// town on foot). Absolute x/z; heights are given above the ground: the planned surface (<see cref="Ground"/>) where the plan
    /// knows it, else the built ground the runtime reads.
    /// </summary>
    public struct CityViewpoint
    {
        /// <summary>Short name for the stop label and the sidecar ("ramp 1 foot", "terrace 2 edge", "castle gate"...).</summary>
        public string Name;
        /// <summary>Where the camera stands (absolute) and its height above the ground there.</summary>
        public double EyeX, EyeZ;
        public float EyeAbove;
        /// <summary>Where it looks (absolute) and the height of that point above the ground there, or above the eye's ground (<see cref="LookAboveEye"/>).</summary>
        public double LookX, LookZ;
        public float LookAbove;
        /// <summary>True: <see cref="LookAbove"/> is measured from the eye's ground (a level look along a terrace), not the look point's.</summary>
        public bool LookAboveEye;
        /// <summary>Vertical field of view, degrees.</summary>
        public float Fov;
        /// <summary>Road distance where the traveller stands for the shot (the city's centre, or before it for the distant views).</summary>
        public double StandS;
        /// <summary>
        /// Absolute height of the hill's planned surface under the eye (the ramps' grades, the flights' treads, the decks' tops), or NaN
        /// where the plan does not know it (outside the town wall, on the road): the runtime then reads the built ground
        /// (<c>EndlessWorld.GroundHeightAbsolute</c>, which reads the decks). The terrain under a deck or a ramp lies below its surface:
        /// an eye 1.6 m above that terrain stood inside the ramp (2026-10-09).
        /// </summary>
        public float Ground;
        /// <summary>The same for the look point.</summary>
        public float LookGround;
        /// <summary>How far the eye was moved from its planned spot to stand clear (metres; 0 when the planned spot was free).</summary>
        public float Moved;
    }

    /// <summary>
    /// The city's street-level capture stops (pure): each ramp's foot and top, each flight's foot (3 m before its bottom step) and top
    /// (2 m past it), each terrace's edge on its deck behind the parapet looking along it, the view square from its middle, the castle
    /// gate from before its bridge (and from inside its court looking out), the castle court, the castle's flanks and back from outside
    /// the town wall, and two views from the road (150 m and 1 km before the city, towards the castle).
    /// <para>
    /// Every eye stands on the planned surface with <see cref="CityClearance.Clear"/> metres free round it from every piece of the
    /// dressing (wall modules, battlements, pillars, towers, flights, castle pieces, houses' footprints, set pieces, trees and rocks:
    /// <see cref="CityClearance"/>). A blocked spot is moved along the look direction and across it, up to <see cref="Search"/> m, on
    /// the same level; a view with no free spot is dropped and named in the caller's list (2026-10-09: half the first street-level
    /// views stood inside a wall, a flight, a tree's crown or between two gables).
    /// </para>
    /// </summary>
    public static class CityViewpoints
    {
        /// <summary>Eye height of a standing traveller, metres.</summary>
        public const float Eye = 1.62f;
        /// <summary>The distant views: road distance before the city, metres.</summary>
        public const double NearRoad = 150.0, FarRoad = 1000.0;
        /// <summary>How far a blocked eye may move (along the look and across it), metres.</summary>
        public const float Search = 6f;
        /// <summary>A flight's foot view stands this far before its bottom step; its top view this far past its top step, metres.</summary>
        public const float StepsFoot = 3f, StepsTop = 2f, StepsTopMax = 9f;
        /// <summary>A terrace edge's eye stands this far behind the face's line (the parapet is about 0.6 m deep: 1.5 m clear behind it), metres.</summary>
        public const float EdgeBack = 2.4f;

        /// <summary>The viewpoints of the city whose road centre is at road distance <paramref name="cityS"/>; views without a free spot are named in <paramref name="dropped"/>.</summary>
        public static List<CityViewpoint> Plan(CityHill hill, Dressing d, double cityS, List<string> dropped = null) =>
            hill == null ? new List<CityViewpoint>() : Plan(hill, d?.City, d != null ? d.X0 : 0.0, d != null ? d.Z0 : 0.0, cityS, d != null ? new CityClearance(hill, d) : null, dropped);

        /// <summary>
        /// The planned viewpoints without the clearance search (no dressing yet: the composer asks where the castle's outside views will
        /// stand); <paramref name="stats"/> (local to the window at <paramref name="x0"/>, <paramref name="z0"/>) may be null.
        /// </summary>
        public static List<CityViewpoint> Plan(CityHill hill, CityStats stats, double x0, double z0, double cityS) => Plan(hill, stats, x0, z0, cityS, null, null);

        private static List<CityViewpoint> Plan(CityHill hill, CityStats stats, double x0, double z0, double cityS, CityClearance clear, List<string> dropped)
        {
            var list = new List<CityViewpoint>();
            if (hill == null) return list;
            double castleX, castleZ;
            if (hill.Castle != null) hill.World(0f, hill.CastleV, out castleX, out castleZ);
            else if (stats != null) { castleX = x0 + stats.CastleGate.x; castleZ = z0 + stats.CastleGate.z; }
            else hill.World(0f, CityHill.PlateauV + 10f, out castleX, out castleZ);

            // From the road, towards the castle (the traveller stands there: the runtime moves it, so the near tiles stream round the eye).
            list.Add(new CityViewpoint { Name = "road 1 km", StandS = cityS - FarRoad, EyeAbove = 2.4f, LookX = castleX, LookZ = castleZ, LookAbove = 12f, Fov = 30f, EyeX = double.NaN, Ground = float.NaN, LookGround = float.NaN });
            list.Add(new CityViewpoint { Name = "road 150 m", StandS = cityS - NearRoad, EyeAbove = 2.0f, LookX = castleX, LookZ = castleZ, LookAbove = 12f, Fov = 55f, EyeX = double.NaN, Ground = float.NaN, LookGround = float.NaN });

            // Each view as a list of candidate spots in order of preference: the first that settles free stands.
            var street = new List<List<CityViewpoint>>();
            int ramp = 0, flight = 0;
            foreach (CityHill.Way w in hill.Ways)
            {
                int n = w.U.Length;
                if (n < 2) continue;
                if (w.Stair)
                {
                    // The pack's flight: from U/V[0] (its bottom) to U/V[1] (its top). The foot view stands StepsFoot before the bottom
                    // step looking up the flight; the top view StepsTop past the top step looking down it.
                    flight++;
                    float cu = w.U[1] - w.U[0], cv = w.V[1] - w.V[0], cl = (float)Math.Sqrt(cu * cu + cv * cv);
                    if (cl < 1e-3f) continue;
                    cu /= cl;
                    cv /= cl;
                    street.Add(One(View(hill, $"steps {flight} foot", w.U[0] - cu * StepsFoot, w.V[0] - cv * StepsFoot, w.U[1], w.V[1], 1.0f, false, 60f, cityS)));
                    var tops = new List<CityViewpoint>();
                    // Past the top on the terrace's deck (up to StepsTopMax), then beside the flight's head along the terrace, always
                    // looking back down the flight (2026-10-09: the decks' lips and the houses along them leave little room there).
                    foreach (float side in new[] { 0f, 3f, -3f, 6f, -6f })
                    {
                        foreach (float past in new[] { StepsTop, StepsTop + 1.5f, StepsTop + 3f, StepsTop + 5f, StepsTopMax })
                        {
                            float eu = w.U[1] + cu * past + cv * side, ev = w.V[1] + cv * past - cu * side;
                            tops.Add(View(hill, $"steps {flight} top", eu, ev, w.U[0] - cu * StepsFoot, w.V[0] - cv * StepsFoot, 1.0f, false, 60f, cityS));
                        }
                    }
                    street.Add(tops);
                    continue;
                }

                ramp++;
                bool upward = w.H == null || w.H.Length < n || w.H[0] <= w.H[n - 1];
                int foot = upward ? 0 : n - 1, top = upward ? n - 1 : 0;
                Along(w, foot, top, 14f, out float fu, out float fv);
                street.Add(One(View(hill, $"ramp {ramp} foot", w.U[foot], w.V[foot], fu, fv, Eye, false, 60f, cityS)));
                // The top: on the ramp's last metres (its landing, between its walls) looking back down it; further down when that is taken.
                var rampTops = new List<CityViewpoint>();
                foreach (float back in new[] { 1.5f, 4f, 7f })
                {
                    Along(w, top, foot, back, out float eu, out float ev);
                    Along(w, top, foot, back + 16f, out float tu, out float tv);
                    rampTops.Add(View(hill, $"ramp {ramp} top", eu, ev, tu, tv, 1.0f, false, 60f, cityS));
                }

                street.Add(rampTops);
            }

            // Each terrace's edge: on its deck, EdgeBack behind the face's line (behind the parapet), looking along the face at eye
            // height; along the face wherever the first free spot is.
            for (int i = 0; i < hill.Faces; i++)
            {
                var edges = new List<CityViewpoint>();
                foreach (float phi in new[] { -0.45f, 0.45f, -0.25f, 0.25f, -0.65f, 0.65f, -0.12f, 0.12f, -0.35f, 0.35f, -0.55f, 0.55f })
                {
                    if (!EdgePoint(hill, i, phi, EdgeBack, out float u, out float v)) continue;
                    hill.World(u, v, out double x, out double z);
                    if (hill.Outside(x, z) > -4f || hill.OnWay(u, v, out _) || hill.OnCastle(u, v, 3f)) continue;
                    float ahead = phi - Math.Sign(phi) * 0.35f;   // along the face, towards its middle
                    if (!EdgePoint(hill, i, ahead, EdgeBack, out float lu, out float lv)) continue;
                    edges.Add(View(hill, $"terrace {i + 1} edge", u, v, lu, lv, Eye - 0.3f, true, 60f, cityS));
                    if (clear == null) break;
                }

                if (edges.Count > 0) street.Add(edges);
            }

            // The view square beside the castle, where the last ramp arrives (CityComposer's own rule: su * 0.55 + 4, PlateauV + 4.5):
            // from its middle, looking across it towards the town's axis.
            CityHill.Way last = null;
            foreach (CityHill.Way w in hill.Ways) if (!w.Stair) last = w;
            if (last != null)
            {
                float su = last.U[last.U.Length - 1];
                float sgn = su < 0f ? -1f : 1f;
                float squ = su * 0.55f + sgn * 4f, sqv = CityHill.PlateauV + (hill.Castle != null ? 4.5f : 6f);
                street.Add(One(View(hill, "view square", squ + sgn * 3f, sqv, squ - sgn * 20f, sqv, Eye, true, 60f, cityS)));
            }

            // The castle's gate (before its bridge's head, looking in through it), its court, and the gate from inside looking out.
            if (stats != null && (stats.CastleGateInner.x != 0f || stats.CastleGateInner.z != 0f))
            {
                double hx = x0 + stats.CastleGate.x, hz = z0 + stats.CastleGate.z;
                double ix = x0 + stats.CastleGateInner.x, iz = z0 + stats.CastleGateInner.z;
                double dx = ix - hx, dz = iz - hz, len = Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-3) { dx = castleX - hx; dz = castleZ - hz; len = Math.Max(1e-3, Math.Sqrt(dx * dx + dz * dz)); }
                dx /= len;
                dz /= len;
                street.Add(One(World(hill, "castle gate", hx - dx * 2.0, hz - dz * 2.0, ix, iz, 2.5f, false, 60f, cityS)));
                var court = new List<CityViewpoint>();
                foreach (double inward in new[] { 6.0, 10.0, 14.0 })
                    court.Add(World(hill, "castle court", ix + dx * inward, iz + dz * inward, ix + dx * (inward + 16.0), iz + dz * (inward + 16.0), Eye, true, 65f, cityS));
                street.Add(court);
                var inside = new List<CityViewpoint>();
                foreach (double inward in new[] { 9.0, 13.0, 6.0 })
                    inside.Add(World(hill, "castle gate inside", ix + dx * inward, iz + dz * inward, hx, hz, Eye, true, 60f, cityS));
                street.Add(inside);
            }

            // The castle's flanks and back, from outside the town wall (walking out until the wall is behind).
            if (hill.Castle != null)
            {
                float side = hill.RockHalfU + CityHill.CliffRun + 12f;
                street.Add(One(Outer(hill, "castle flank right", side, hill.CastleV, 1f, 0f, castleX, castleZ, cityS)));
                street.Add(One(Outer(hill, "castle flank left", -side, hill.CastleV, -1f, 0f, castleX, castleZ, cityS)));
                street.Add(One(Outer(hill, "castle back", 0f, hill.RockV1 + CityHill.CliffRun + 12f, 0f, 1f, castleX, castleZ, cityS)));
            }

            foreach (List<CityViewpoint> candidates in street)
            {
                if (clear == null) { list.Add(candidates[0]); continue; }
                string first = null;
                bool stood = false;
                foreach (CityViewpoint v in candidates)
                {
                    if (Settle(hill, clear, v, out CityViewpoint settled, out string why))
                    {
                        list.Add(settled);
                        stood = true;
                        break;
                    }

                    first = first ?? why;
                }

                if (!stood) dropped?.Add($"{candidates[0].Name}: {first}");
            }

            return list;
        }

        /// <summary>
        /// Moves <paramref name="v"/> to the nearest free spot within <see cref="Search"/> m along its look direction and across it (1 m
        /// steps, nearest first), on the same level (planned surface within 1 m of the planned spot's; the look point moves with the eye).
        /// False with the nearest blocker when none is free.
        /// </summary>
        public static bool Settle(CityHill hill, CityClearance clear, CityViewpoint v, out CityViewpoint settled, out string why)
        {
            settled = v;
            why = null;
            if (double.IsNaN(v.EyeX)) return true;
            double fx = v.LookX - v.EyeX, fz = v.LookZ - v.EyeZ, fl = Math.Sqrt(fx * fx + fz * fz);
            if (fl < 1e-6) { fx = 1.0; fz = 0.0; } else { fx /= fl; fz /= fl; }
            double sx = fz, sz = -fx;
            bool inside = hill.Outside(v.EyeX, v.EyeZ) < 0f;
            string nearest = null;
            float nearestDistance = float.NaN;
            foreach ((int a, int c) in Offsets)
            {
                double x = v.EyeX + fx * a + sx * c, z = v.EyeZ + fz * a + sz * c;
                bool inNow = hill.Outside(x, z) < 0f;
                if (inNow != inside) continue;
                if (inside && hill.Outside(x, z) > -1.5f) continue;   // never against the town wall's inner face
                float ground = GroundAt(hill, clear, x, z);
                float refGround = float.IsNaN(v.Ground) ? ground : v.Ground;
                if (!float.IsNaN(ground) && !float.IsNaN(refGround) && Math.Abs(ground - refGround) > 1f) continue;   // the same level
                if (!float.IsNaN(ground) && Math.Abs(BuiltAt(hill, clear, x, z) - ground) > 0.3f) continue;   // standing on what is built (a deck or the terrain), not over a bank
                if (!clear.Free(x, z, ground, v.EyeAbove, CityClearance.Clear, out string what, out float distance))
                {
                    if (a == 0 && c == 0) { nearest = what; nearestDistance = distance; }
                    continue;
                }

                settled.EyeX = x;
                settled.EyeZ = z;
                settled.LookX = v.LookX + fx * a + sx * c;
                settled.LookZ = v.LookZ + fz * a + sz * c;
                settled.Ground = ground;
                settled.LookGround = GroundAt(hill, clear, settled.LookX, settled.LookZ);
                settled.Moved = (float)Math.Sqrt(a * a + c * c);
                return true;
            }

            why = $"no free spot within {Search:0} m (at the planned spot: {nearest ?? "off its level"}, {nearestDistance:0.0} m)";
            return false;
        }

        /// <summary>The search's offsets (along the look, across it), metres, nearest first; ties in a fixed order.</summary>
        private static readonly List<(int a, int c)> Offsets = BuildOffsets();

        private static List<(int a, int c)> BuildOffsets()
        {
            var o = new List<(int a, int c)>();
            int r = (int)Search;
            for (int a = -r; a <= r; a++) for (int c = -r; c <= r; c++) if (a * a + c * c <= r * r) o.Add((a, c));
            o.Sort((p, q) =>
            {
                int dp = p.a * p.a + p.c * p.c, dq = q.a * q.a + q.c * q.c;
                if (dp != dq) return dp.CompareTo(dq);
                if (Math.Abs(p.c) != Math.Abs(q.c)) return Math.Abs(p.c).CompareTo(Math.Abs(q.c));   // along the look first
                if (p.c != q.c) return p.c.CompareTo(q.c);
                return q.a.CompareTo(p.a);   // forward before back
            });
            return o;
        }

        private static List<CityViewpoint> One(CityViewpoint v) => new List<CityViewpoint> { v };

        /// <summary>
        /// The ground an eye stands on at (x, z): on the castle's footprint its own court level (<see cref="CityClearance.CastleGround"/>:
        /// the castle stands on the rock's level, which need not be the plateau's), else <see cref="SurfaceAt"/>.
        /// </summary>
        public static float GroundAt(CityHill hill, CityClearance clear, double x, double z)
        {
            if (clear != null && !float.IsNaN(clear.CastleGround))
            {
                hill.Local(x, z, out float u, out float v);
                if (hill.OnCastle(u, v, 0f)) return clear.CastleGround;
            }

            return SurfaceAt(hill, x, z);
        }

        /// <summary>
        /// What is built at (x, z) inside the town wall (absolute): a terrace's or ramp's deck top where one covers it, else the terrain with the
        /// hill (the bank under a deck's free edge lies lower than the planned surface); the castle's ground on its footprint.
        /// </summary>
        public static float BuiltAt(CityHill hill, CityClearance clear, double x, double z)
        {
            if (clear != null && !float.IsNaN(clear.CastleGround))
            {
                hill.Local(x, z, out float u, out float v);
                if (hill.OnCastle(u, v, 0f)) return clear.CastleGround;
            }

            if (clear != null && clear.HasDecks) return clear.DeckHeight(x, z, out float built) ? built : hill.Level + hill.Offset(x, z);
            if (hill.DeckTop(x, z, out float top)) return hill.Level + top;
            return hill.Level + hill.Offset(x, z);
        }

        /// <summary>The hill's planned surface (absolute) at (x, z) inside the town wall, NaN outside it.</summary>
        public static float SurfaceAt(CityHill hill, double x, double z) => hill.Outside(x, z) < 0f ? hill.Level + hill.SurfaceOffset(x, z) : float.NaN;

        /// <summary>The point <paramref name="back"/> metres behind face <paramref name="face"/>'s line (on the terrace above it) at angle <paramref name="phi"/>.</summary>
        private static bool EdgePoint(CityHill hill, int face, float phi, float back, out float u, out float v)
        {
            hill.Contour(hill.Q[face], phi, out float lu, out float lv);
            hill.Inward(lu, lv, out float nu, out float nv);   // up the hill (towards the higher terrace)
            u = lu + nu * back;
            v = lv + nv * back;
            // The eye must stand on the terrace above the face (its surface one step higher than in front of the line).
            hill.World(lu - nu * 2f, lv - nv * 2f, out double ox, out double oz);
            hill.World(u, v, out double ix, out double iz);
            float below = hill.SurfaceOffset(ox, oz), above = hill.SurfaceOffset(ix, iz);
            return above > below + 1f;
        }

        private static CityViewpoint View(CityHill hill, string name, float u, float v, float lu, float lv, float lookAbove, bool lookAboveEye, float fov, double s)
        {
            hill.World(u, v, out double ex, out double ez);
            hill.World(lu, lv, out double lx, out double lz);
            return World(hill, name, ex, ez, lx, lz, lookAbove, lookAboveEye, fov, s);
        }

        private static CityViewpoint World(CityHill hill, string name, double ex, double ez, double lx, double lz, float lookAbove, bool lookAboveEye, float fov, double s) =>
            new CityViewpoint
            {
                Name = name, StandS = s, EyeX = ex, EyeZ = ez, EyeAbove = Eye, LookX = lx, LookZ = lz, LookAbove = lookAbove, LookAboveEye = lookAboveEye, Fov = fov,
                Ground = SurfaceAt(hill, ex, ez), LookGround = SurfaceAt(hill, lx, lz),
            };

        /// <summary>A view from outside the town wall: from (u, v) stepping out along (du, dv) until 6 m beyond the wall, looking at the castle.</summary>
        private static CityViewpoint Outer(CityHill hill, string name, float u, float v, float du, float dv, double castleX, double castleZ, double s)
        {
            double x = 0.0, z = 0.0;
            for (int k = 0; k < 30; k++, u += du * 5f, v += dv * 5f)
            {
                hill.World(u, v, out x, out z);
                if (hill.Outside(x, z) > 6f && hill.Rock(x, z) <= 0f) break;
            }

            return new CityViewpoint { Name = name, StandS = s, EyeX = x, EyeZ = z, EyeAbove = Eye, LookX = castleX, LookZ = castleZ, LookAbove = 10f, Fov = 60f, Ground = float.NaN, LookGround = float.NaN };
        }

        /// <summary>The point <paramref name="metres"/> along way <paramref name="w"/> from vertex <paramref name="from"/> towards vertex <paramref name="to"/>.</summary>
        private static void Along(CityHill.Way w, int from, int to, float metres, out float u, out float v)
        {
            int step = to > from ? 1 : -1;
            u = w.U[from];
            v = w.V[from];
            float left = metres;
            for (int i = from; i != to; i += step)
            {
                float au = w.U[i], av = w.V[i], bu = w.U[i + step], bv = w.V[i + step];
                float l = (float)Math.Sqrt((bu - au) * (bu - au) + (bv - av) * (bv - av));
                if (l >= left && l > 1e-4f)
                {
                    u = au + (bu - au) * left / l;
                    v = av + (bv - av) * left / l;
                    return;
                }

                left -= l;
                u = bu;
                v = bv;
            }
        }
    }

    /// <summary>
    /// What stands in a walled city's streets, for the review eyes (pure, absolute x/z): every piece of the city's dressing with its
    /// reach and its height span: retaining and town wall modules and their battlements as 5 m segments, pillars, towers, the stair
    /// flights, the castle's pieces (their mined reach and height), houses' footprints (<see cref="BuildingTable"/>, the composer's
    /// own cover circles), set pieces, rocks (measured) and trees (their crowns).
    /// </summary>
    public sealed class CityClearance
    {
        /// <summary>Free room round an eye, metres.</summary>
        public const float Clear = 1.5f;
        /// <summary>A tree's crown reach from its trunk, metres (the town's birches and fruit trees).</summary>
        public const float CrownReach = 2.8f;
        /// <summary>The eye's vertical window: from this far above its ground (kerbs, floors and treads are walked on) to this far above the eye.</summary>
        public const float Foot = 0.35f, Head = 1.2f;

        private struct Piece
        {
            public double X, Z;
            public float Dx, Dz, Half, Half2, R, Y0, Y1;
            public string What;
        }

        private readonly List<Piece> pieces = new List<Piece>();

        /// <summary>The castle's own ground (its plan's Y = 0, the court and the gate), absolute; NaN without a castle.</summary>
        public float CastleGround { get; } = float.NaN;

        private readonly List<(Vector3[] outer, Vector3[] inner)> decks = new List<(Vector3[], Vector3[])>();
        private double deckX0, deckZ0;

        /// <summary>True when the dressing's terrace decks (as the composer built them) are known.</summary>
        public bool HasDecks => decks.Count > 0;

        /// <summary>The top of the composer's terrace and ramp decks at absolute (x, z) (their strips between outer and inner edge), or false.</summary>
        public bool DeckHeight(double x, double z, out float height)
        {
            height = 0f;
            float px = (float)(x - deckX0), pz = (float)(z - deckZ0);
            foreach (var (outer, inner) in decks)
            {
                int n = Math.Min(outer.Length, inner.Length);
                for (int k = 0; k + 1 < n; k++)
                {
                    if (InTriangle(px, pz, outer[k], outer[k + 1], inner[k + 1], out height) || InTriangle(px, pz, outer[k], inner[k + 1], inner[k], out height)) return true;
                }
            }

            return false;
        }

        private static bool InTriangle(float x, float z, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0f;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Math.Abs(d) < 1e-6f) return false;
            float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d, l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d, l3 = 1f - l1 - l2;
            if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y;
            return true;
        }

        /// <summary>Pieces held.</summary>
        public int Count => pieces.Count;

        /// <summary>The pieces of <paramref name="d"/>, the dressing of the city on <paramref name="hill"/>.</summary>
        public CityClearance(CityHill hill, Dressing d)
        {
            if (d == null) return;
            double x0 = d.X0, z0 = d.Z0;
            int castleStart = hill?.Castle != null ? CastleLayout.Find(d.Props, hill.Castle) : -1;
            int castleCount = castleStart >= 0 ? hill.Castle.Parts.Length : 0;
            if (castleStart >= 0) CastleGround = d.Props[castleStart].position.y - hill.Castle.Parts[0].Y;
            deckX0 = x0;
            deckZ0 = z0;
            if (d.City != null) foreach (TerraceDeck deck in d.City.Decks) if (deck?.Outer != null && deck.Inner != null) decks.Add((deck.Outer, deck.Inner));
            for (int i = 0; i < d.Props.Count; i++)
            {
                PropPlacement p = d.Props[i];
                double x = x0 + p.position.x, z = z0 + p.position.z;
                float y = p.position.y, s = p.scale > 0f ? p.scale : 1f;
                float rad = p.yaw * (float)(Math.PI / 180.0), dx = (float)Math.Sin(rad), dz = (float)Math.Cos(rad);
                if (i >= castleStart && i < castleStart + castleCount)
                {
                    SetPart part = hill.Castle.Parts[i - castleStart];
                    float lo = part.Radius > 0f ? part.Low : 0f, hi = part.Radius > 0f ? part.High : 4f;
                    if (CastleLayout.Footprint(part.Prefab, out Vector4 f))
                    {
                        // The piece's measured footprint (prefab space, at its scale), turned by its yaw.
                        float cs = (float)Math.Cos(rad), sn = (float)Math.Sin(rad);
                        float cx = 0.5f * (f.x + f.z) * s, cz = 0.5f * (f.y + f.w) * s;
                        Add(x + cx * cs + cz * sn, z - cx * sn + cz * cs, cs, -sn, 0.5f * (f.z - f.x) * s, 0.05f, y + lo, y + hi, part.Prefab, 0.5f * (f.w - f.y) * s);
                    }
                    else Add(x, z, 0f, 0f, 0f, part.Radius > 0f ? part.Radius : 1f, y + lo, y + hi, part.Prefab ?? part.Role);
                    continue;
                }

                if (p.role == PropRole.CityWall || p.role == PropRole.CityBattlement)
                {
                    float h = p.role == PropRole.CityWall ? CityComposer.WallHeight : CityComposer.BattlementHeight;
                    float lean = p.pitch != 0f ? 1.2f : 0f;
                    Add(x, z, dx, dz, 0.5f * CityComposer.ModuleLength, 0.4f, y - lean, y + h + lean, p.role);
                }
                else if (p.role == PropRole.CityTower) Add(x, z, 0f, 0f, 0f, 0.5f * CityComposer.TowerDiameter * s + 0.1f, y, y + 9f * s, p.role);
                else if (p.role == PropRole.CityPillar) Add(x, z, 0f, 0f, 0f, 0.5f, y, y + CityComposer.PillarHeight, p.role);
                else if (p.role == PropRole.CityStairs) Add(x, z, dx, dz, 0.5f * CityHill.FlightRun + 0.2f, CityHill.StairHalf, y - 0.5f, y + CityHill.Rise + 0.5f, p.role);
                else if (PropRole.IsBuilding(p.role) && BuildingTable.For(p.role, p.variant) is BuildingShape shape)
                {
                    foreach (Vector3 c in SettlementKit.Cover(shape, p.position, p.yaw)) Add(x0 + c.x, z0 + c.z, 0f, 0f, 0f, c.y, y - 0.5f, y + shape.Height, p.role);
                }
                else if (PropRole.IsBuilding(p.role) || p.role == PropRole.Cabin) Add(x, z, 0f, 0f, 0f, 5f, y - 0.5f, y + 9f, p.role);
                else if (PropRole.IsRock(p.role))
                {
                    float r = 1.2f * s, h = 2f * s;
                    if (PaletteKeys.Default.TryGetMetrics(p.role, p.variant, out PropMetrics m) && m.IsValid)
                    {
                        r = Math.Max(Math.Max(-m.min.x, m.max.x), Math.Max(-m.min.z, m.max.z)) * s;
                        h = m.max.y * s;
                    }

                    Add(x, z, 0f, 0f, 0f, r, y - 1f, y + h, p.role);
                }
                else if (PropRole.IsTree(p.role)) Add(x, z, 0f, 0f, 0f, CrownReach, y - 1f, y + 20f, p.role);
                else Add(x, z, 0f, 0f, 0f, 0.6f, y - 0.3f, y + 2.6f, p.role);
            }

            if (d.City != null)
            {
                foreach (PlacedSetPart part in d.City.SetParts)
                {
                    float y = part.Position.y;
                    Add(x0 + part.Position.x, z0 + part.Position.z, 0f, 0f, 0f, Math.Max(0.3f, part.Radius), y - 0.3f, y + 2.6f, part.Prefab ?? part.Role);
                }
            }

            foreach (TreePlacement t in d.Trees)
            {
                if (!PropRole.IsTree(t.role)) continue;
                Add(x0 + t.position.x, z0 + t.position.z, 0f, 0f, 0f, CrownReach * (t.scale > 0f ? Math.Max(1f, t.scale) : 1f), float.NegativeInfinity, float.PositiveInfinity, t.role);
            }
        }

        /// <summary>A piece: a rectangle round (x, z) reaching <paramref name="half"/> along the unit axis (dx, dz) and <paramref name="half2"/> across it, rounded by <paramref name="r"/>, from y0 to y1.</summary>
        private void Add(double x, double z, float dx, float dz, float half, float r, float y0, float y1, string what, float half2 = 0f) =>
            pieces.Add(new Piece { X = x, Z = z, Dx = dx, Dz = dz, Half = half, Half2 = half2, R = r, Y0 = y0, Y1 = y1, What = what });

        /// <summary>
        /// Whether an eye <paramref name="eyeAbove"/> m above <paramref name="ground"/> at (x, z) has <paramref name="clear"/> metres free
        /// from every piece whose height span meets its window (NaN ground: every piece counts); the nearest piece and its distance.
        /// </summary>
        public bool Free(double x, double z, float ground, float eyeAbove, float clear, out string nearest, out float distance)
        {
            nearest = null;
            distance = float.MaxValue;
            float lo = ground + Foot, hi = ground + eyeAbove + Head;
            foreach (Piece p in pieces)
            {
                if (!float.IsNaN(ground) && (p.Y1 < lo || p.Y0 > hi)) continue;
                double px = x - p.X, pz = z - p.Z, reach = p.Half + p.Half2 + p.R + clear + 1.0;
                if (Math.Abs(px) > reach || Math.Abs(pz) > reach) continue;
                double a = Math.Max(0.0, Math.Abs(px * p.Dx + pz * p.Dz) - p.Half), b = Math.Max(0.0, Math.Abs(pz * p.Dx - px * p.Dz) - p.Half2);
                float dist = (float)Math.Sqrt(a * a + b * b) - p.R;
                if (dist < distance)
                {
                    distance = dist;
                    nearest = p.What;
                }
            }

            return distance >= clear;
        }
    }

    /// <summary>The castle's pieces in a city's dressing (pure): where the composer's run of the plan's parts starts.</summary>
    public static class CastleLayout
    {
        /// <summary>
        /// The footprint of a castle-kit piece in its prefab space at scale 1 (x0, z0, x1, z1 in a Vector4's x, y, z, w), metres; false for
        /// a piece not measured (or whose mesh stands far from its pivot).
        /// </summary>
        public static bool Footprint(string prefab, out Vector4 footprint)
        {
            footprint = default;
            return prefab != null && Footprints.TryGetValue(prefab, out footprint);
        }

        /// <summary>
        /// Footprints of the castle plans' pieces, measured offline (2026-10-09) from their FBX bounds (the mining cache of
        /// <c>CitySetPieces.Data.cs</c>, cm -> m, x negated as on import): prefab space, (x0, z0, x1, z1). The two cut walls' meshes
        /// stand far from their FBX origin and take <c>Wall_01</c>'s (their mined reach is a wall's, 4.5 m at x0.9).
        /// </summary>
        private static readonly Dictionary<string, Vector4> Footprints = new Dictionary<string, Vector4>
        {
            { "SM_Bld_Bridge_01", new Vector4(-5.31f, -0.21f, 0.31f, 5.28f) },
            { "SM_Bld_Bridge_Pillars_01", new Vector4(-5.22f, -0.22f, 0.22f, 0.22f) },
            { "SM_Bld_Bridge_Railing_01", new Vector4(-0.17f, -0.00f, 0.17f, 5.00f) },
            { "SM_Bld_Bridge_Roof_01", new Vector4(-6.04f, -0.01f, 1.04f, 5.01f) },
            { "SM_Bld_Castle_Balcony_01", new Vector4(-3.16f, -0.08f, 3.16f, 3.16f) },
            { "SM_Bld_Castle_Battlements_01", new Vector4(-5.00f, -0.25f, -0.00f, 0.25f) },
            { "SM_Bld_Castle_Battlements_01_Short", new Vector4(-3.75f, -0.25f, -0.00f, 0.25f) },
            { "SM_Bld_Castle_Battlements_02", new Vector4(-5.00f, -0.45f, -0.00f, 0.45f) },
            { "SM_Bld_Castle_Battlements_02_Half", new Vector4(-2.50f, -0.45f, -0.00f, 0.45f) },
            { "SM_Bld_Castle_Battlements_06", new Vector4(-5.00f, -0.33f, 0.00f, 0.33f) },
            { "SM_Bld_Castle_Battlements_L_Corner_02", new Vector4(-5.00f, -0.00f, 0.45f, 5.45f) },
            { "SM_Bld_Castle_Battlements_L_Corner_05", new Vector4(-5.00f, -0.00f, 0.49f, 5.49f) },
            { "SM_Bld_Castle_Battlements_Pillar_01", new Vector4(-0.36f, -0.36f, 0.36f, 0.36f) },
            { "SM_Bld_Castle_Battlements_Pillar_02", new Vector4(-0.50f, -0.50f, 0.50f, 0.50f) },
            { "SM_Bld_Castle_Drawbridge_01", new Vector4(-5.00f, -0.00f, -0.00f, 0.54f) },
            { "SM_Bld_Castle_Floor_Stone_01", new Vector4(-5.00f, -5.00f, 0.00f, 0.00f) },
            { "SM_Bld_Castle_Floor_Wood_01", new Vector4(-5.00f, -5.00f, 0.00f, 0.00f) },
            { "SM_Bld_Castle_Floor_Wood_03", new Vector4(-2.50f, -5.00f, -0.00f, 0.00f) },
            { "SM_Bld_Castle_Floor_Wood_Hatch_01", new Vector4(-0.01f, -2.51f, 2.51f, 0.01f) },
            { "SM_Bld_Castle_Floor_Wood_Round_L_01", new Vector4(0.00f, -5.00f, 5.00f, 0.00f) },
            { "SM_Bld_Castle_Floor_Wood_Round_S_01", new Vector4(-0.00f, -2.50f, 2.50f, -0.00f) },
            { "SM_Bld_Castle_Hoarding_01", new Vector4(-5.00f, -0.37f, 0.00f, 1.94f) },
            { "SM_Bld_Castle_Hoarding_Corner_L_01", new Vector4(-5.01f, -0.01f, 1.91f, 6.91f) },
            { "SM_Bld_Castle_Hoarding_Corner_S_01", new Vector4(-2.51f, -0.02f, 1.90f, 4.40f) },
            { "SM_Bld_Castle_Hoarding_End_01", new Vector4(-0.34f, -0.43f, 0.34f, 2.02f) },
            { "SM_Bld_Castle_Pillar_Stone_05", new Vector4(-0.33f, -0.33f, 0.33f, 0.33f) },
            { "SM_Bld_Castle_Pillar_Stone_Round_01", new Vector4(-0.82f, -0.82f, 0.82f, 0.82f) },
            { "SM_Bld_Castle_Pillar_Wood_Fancy_01", new Vector4(-0.36f, -0.11f, 0.36f, 0.82f) },
            { "SM_Bld_Castle_Pillar_Wood_Round_01", new Vector4(-0.82f, -0.82f, 0.82f, 0.82f) },
            { "SM_Bld_Castle_Pillar_Wood_Round_02", new Vector4(-0.41f, -0.41f, 0.41f, 0.41f) },
            { "SM_Bld_Castle_Roof_Cap_Edge_01", new Vector4(-5.00f, 0.00f, -0.00f, 1.26f) },
            { "SM_Bld_Castle_Roof_Cap_Round_03", new Vector4(-1.29f, -1.29f, 1.29f, 1.29f) },
            { "SM_Bld_Castle_Roof_Cap_Round_06", new Vector4(-1.82f, -1.82f, 1.82f, 1.82f) },
            { "SM_Bld_Castle_Roof_Cap_Round_07", new Vector4(-1.33f, -1.33f, 1.33f, 1.33f) },
            { "SM_Bld_Castle_Roof_Cap_Square_01", new Vector4(-1.22f, -1.22f, 1.22f, 1.22f) },
            { "SM_Bld_Castle_Roof_L_01", new Vector4(-0.00f, -5.00f, 5.00f, 0.00f) },
            { "SM_Bld_Castle_Roof_L_Corner_Inner_01", new Vector4(-5.00f, -5.00f, -0.00f, 0.00f) },
            { "SM_Bld_Castle_Roof_L_Edge_01", new Vector4(-5.00f, -0.25f, -0.00f, 1.22f) },
            { "SM_Bld_Castle_Roof_L_Edge_Corner_01", new Vector4(-5.00f, -0.00f, 1.22f, 6.22f) },
            { "SM_Bld_Castle_Roof_L_End_01", new Vector4(-0.31f, -5.00f, 0.31f, 0.05f) },
            { "SM_Bld_Castle_Roof_L_End_Beam_01", new Vector4(-0.31f, -5.00f, 0.31f, 0.05f) },
            { "SM_Bld_Castle_Roof_L_Window_01", new Vector4(-5.00f, -5.00f, -0.00f, 0.00f) },
            { "SM_Bld_Castle_Roof_S_Corner_01", new Vector4(-0.00f, -2.50f, 2.50f, 0.00f) },
            { "SM_Bld_Castle_Roof_S_Edge_Corner_01", new Vector4(-2.50f, 0.00f, 1.22f, 3.72f) },
            { "SM_Bld_Castle_Roof_Short_Half_01", new Vector4(-0.00f, -2.50f, 5.00f, 0.00f) },
            { "SM_Bld_Castle_Roof_Short_Half_End_01", new Vector4(-0.31f, -2.50f, 0.31f, 0.06f) },
            { "SM_Bld_Castle_Wall_01", new Vector4(-5.00f, -0.25f, 0.00f, 0.25f) },
            { "SM_Bld_Castle_Wall_Cut_01", new Vector4(-5.00f, -0.25f, 0.00f, 0.25f) },
            { "SM_Bld_Castle_Wall_Cut_02", new Vector4(-5.00f, -0.25f, 0.00f, 0.25f) },
            { "SM_Bld_Castle_Wall_02", new Vector4(-5.00f, -0.25f, 0.00f, 0.25f) },
            { "SM_Bld_Castle_Wall_03", new Vector4(-5.00f, -0.25f, 0.00f, 0.43f) },
            { "SM_Bld_Castle_Wall_04", new Vector4(-5.00f, -0.25f, 0.00f, 0.87f) },
            { "SM_Bld_Castle_Wall_05", new Vector4(-5.00f, -0.25f, 0.00f, 0.87f) },
            { "SM_Bld_Castle_Wall_Archway_01", new Vector4(-5.00f, -0.25f, 0.00f, 0.25f) },
            { "SM_Bld_Castle_Wall_Archway_02", new Vector4(-5.03f, -0.57f, 0.03f, 0.57f) },
            { "SM_Bld_Castle_Wall_Corbels_01", new Vector4(-5.00f, -0.25f, 0.00f, 0.44f) },
            { "SM_Bld_Castle_Wall_Corbels_Corner_01", new Vector4(-0.00f, 0.00f, 0.94f, 0.94f) },
            { "SM_Bld_Castle_Wall_Corner_L_01", new Vector4(-5.00f, 0.00f, 0.25f, 5.25f) },
            { "SM_Bld_Castle_Wall_Corner_L_Arrowslit_01", new Vector4(-5.00f, 0.00f, 0.25f, 5.25f) },
            { "SM_Bld_Castle_Wall_Corner_L_Base_01", new Vector4(-5.00f, -0.00f, 2.75f, 7.75f) },
            { "SM_Bld_Castle_Wall_Corner_L_Base_Slope_01", new Vector4(-5.00f, 0.00f, 2.75f, 7.75f) },
            { "SM_Bld_Castle_Wall_Corner_L_Door_01", new Vector4(-5.00f, 0.00f, 0.25f, 5.25f) },
            { "SM_Bld_Castle_Wall_Corner_L_Window_01", new Vector4(-5.00f, 0.00f, 0.25f, 5.25f) },
            { "SM_Bld_Castle_Wall_Corner_S_01", new Vector4(-2.50f, 0.00f, 0.25f, 2.75f) },
            { "SM_Bld_Castle_Wall_Corner_S_Door_01", new Vector4(-2.50f, -0.00f, 0.25f, 2.75f) },
            { "SM_Bld_Castle_Wall_Corner_S_Window_01", new Vector4(-2.50f, -0.00f, 0.25f, 2.75f) },
            { "SM_Bld_Castle_Wall_Corner_Slope_Straight_01", new Vector4(-5.00f, -0.25f, -0.00f, 2.75f) },
            { "SM_Bld_Castle_Wall_Door_01", new Vector4(-5.00f, -0.31f, 0.00f, 0.31f) },
            { "SM_Bld_Castle_Wall_Door_Big_01", new Vector4(-5.00f, -0.43f, -0.00f, 0.43f) },
            { "SM_Bld_Castle_Wall_Gate_01", new Vector4(-5.34f, -0.75f, 0.34f, 0.51f) },
            { "SM_Bld_Castle_Wall_Half_Corner_L_01", new Vector4(-5.00f, -0.00f, 0.25f, 5.25f) },
            { "SM_Bld_Castle_Wall_Half_Corner_S_01", new Vector4(-2.50f, 0.00f, 0.25f, 2.75f) },
            { "SM_Bld_Castle_Wall_Panel_Stone_01", new Vector4(-5.00f, 0.00f, 0.00f, 0.61f) },
            { "SM_Bld_Castle_Wall_Panel_Stone_02", new Vector4(-5.00f, 0.00f, 0.00f, 0.67f) },
            { "SM_Bld_Castle_Wall_Panel_Wood_01", new Vector4(-5.00f, 0.00f, -0.00f, 0.33f) },
            { "SM_Bld_Castle_Wall_Pillars_Half_01", new Vector4(-5.00f, -0.25f, 0.00f, 0.25f) },
            { "SM_Bld_Castle_Wall_Pillars_Half_Corner_L_01", new Vector4(-5.00f, 0.00f, 0.25f, 5.25f) },
            { "SM_Bld_Castle_Wall_Pillars_Half_Corner_S_01", new Vector4(-2.50f, 0.00f, 0.25f, 2.75f) },
            { "SM_Bld_Castle_Wall_Tower_M_01", new Vector4(-1.59f, -1.59f, 1.47f, 1.44f) },
            { "SM_Bld_Castle_Wall_Window_01", new Vector4(-5.00f, -0.32f, 0.00f, 0.32f) },
            { "SM_Bld_Castle_Wall_Window_03", new Vector4(-0.45f, -0.00f, 0.45f, 0.00f) },
            { "SM_Bld_Castle_Wall_Window_04", new Vector4(-5.00f, -0.25f, -0.00f, 0.25f) },
            { "SM_Bld_Castle_Wall_Window_05", new Vector4(-5.00f, -0.29f, 0.00f, 0.29f) },
            { "SM_Bld_Castle_Wall_Window_Big_01", new Vector4(-5.00f, -0.28f, 0.00f, 0.28f) },
            { "SM_Bld_Castle_Wall_Window_Fancy_01", new Vector4(-5.00f, -0.28f, -0.00f, 0.28f) },
            { "SM_Bld_Castle_Wall_Window_Fancy_Tall_01", new Vector4(-5.00f, -0.28f, -0.00f, 0.28f) },
            { "SM_Bld_Castle_Wood_Floor_Gap_01", new Vector4(-5.00f, -5.00f, -0.00f, 0.00f) },
            { "SM_Bld_Castle_Wood_Floor_Gap_02", new Vector4(-5.00f, -5.00f, -0.00f, 0.00f) },
            { "SM_Bld_House_Floor_Stone_01", new Vector4(-2.50f, -2.50f, -0.00f, 0.00f) },
            { "SM_Bld_House_Floor_Stone_02", new Vector4(-2.50f, -1.25f, -0.00f, 0.00f) },
            { "SM_Bld_Keep_Pillar_01", new Vector4(-0.50f, -0.62f, 0.50f, 0.62f) },
            { "SM_Bld_Keep_Pillar_02", new Vector4(-0.65f, -0.80f, 0.65f, 1.36f) },
            { "SM_Bld_Keep_Pillar_03", new Vector4(-0.65f, -0.80f, 0.65f, 4.09f) },
            { "SM_Bld_Keep_Pillar_04", new Vector4(-0.50f, -0.62f, 0.50f, 3.80f) },
            { "SM_Bld_Keep_Pillar_Cap_01", new Vector4(-0.68f, -0.84f, 0.68f, 0.84f) },
            { "SM_Bld_Stairs_Round_L_01", new Vector4(-4.83f, -0.00f, 4.83f, 4.80f) },
            { "SM_Bld_Stairs_Round_S_01", new Vector4(-2.34f, -0.00f, 2.34f, 2.32f) },
            { "SM_Bld_Stairs_Round_S_Roof_01", new Vector4(-2.34f, -2.32f, 2.34f, 0.00f) },
            { "SM_Bld_Stairs_Straight_Wide_Edges_01", new Vector4(-0.00f, -5.16f, 5.00f, 0.16f) },
            { "SM_Bld_Stairs_Straight_Wide_Edges_03", new Vector4(-0.00f, -5.00f, 7.52f, 0.00f) },
            { "SM_Bld_Stairs_Wood_Corner_01", new Vector4(-0.17f, -5.13f, 4.08f, 0.15f) },
            { "SM_Bld_Walkway_Stone_Railing_Pillar_01", new Vector4(-0.13f, -0.13f, 0.13f, 0.13f) },
            { "SM_Bld_Walkway_Wood_Railing_01", new Vector4(-2.50f, -0.07f, 0.00f, 0.06f) },
            { "SM_Bld_Walkway_Wood_Railing_Pillar_01", new Vector4(-0.08f, -0.09f, 0.08f, 0.11f) },
            { "SM_Bld_Wall_Wood_Beams_03", new Vector4(-5.00f, -0.34f, -0.00f, 0.34f) },
            { "SM_Bld_Wood_Beam_Curved_01", new Vector4(-4.81f, -0.33f, 4.81f, 0.33f) },
        };


        /// <summary>
        /// Index in <paramref name="props"/> of the first of <paramref name="plan"/>'s parts as the composer placed them (one placement
        /// per part, in the plan's order, consecutively), or -1.
        /// </summary>
        public static int Find(List<PropPlacement> props, CastlePlan plan)
        {
            if (props == null || plan?.Parts == null || plan.Parts.Length == 0) return -1;
            int n = plan.Parts.Length;
            SetPart first = plan.Parts[0];
            for (int i = 0; i + n <= props.Count; i++)
            {
                if (props[i].role != first.Role || props[i].variant != first.Variant) continue;
                bool all = true;
                for (int k = 1; k < n && all; k++) all = props[i + k].role == plan.Parts[k].Role && props[i + k].variant == plan.Parts[k].Variant;
                if (all) return i;
            }

            return -1;
        }

        /// <summary>
        /// The castle of a city's dressing as distant solids (<see cref="FarCastle"/>; window-local like the dressing's props): its parts
        /// rasterised on a <see cref="FarCastle.Cell"/> m grid of the plan (each part's measured footprint), each column as tall as its
        /// highest wall, tower or battlement (a gabled roof where a kit roof ends it higher), standing from the castle's ground (or its
        /// foundations' foot down the rock), merged along rows. <paramref name="start"/> and <paramref name="count"/> give the run of
        /// the dressing's props the proxies stand for (-1 and 0 when the dressing has no castle).
        /// </summary>
        public static void FarProxies(Dressing d, List<PropPlacement> proxies, out int start, out int count)
        {
            start = -1;
            count = 0;
            if (d?.City == null || string.IsNullOrEmpty(d.City.Castle)) return;
            CastlePlan plan = null;
            foreach (CastlePlan c in CitySetPieces.Castles) if (c.Name == d.City.Castle) plan = c;
            if (plan == null) return;
            start = Find(d.Props, plan);
            if (start < 0) return;
            count = plan.Parts.Length;

            PropPlacement p0 = d.Props[start];
            SetPart q0 = plan.Parts[0];
            float ground = p0.position.y - q0.Y;
            float yaw0 = p0.yaw - q0.Yaw, r0 = yaw0 * (float)(Math.PI / 180.0);
            var toTown = new Vector3((float)Math.Sin(r0), 0f, (float)Math.Cos(r0));
            var right = new Vector3(toTown.z, 0f, -toTown.x);
            Vector3 centre = p0.position - right * q0.X - toTown * q0.Z;

            // The parts' footprints in the plan frame (x along right, z along toTown).
            var rects = new List<(float x0, float z0, float x1, float z1, float lo, float hi, bool roof)>();
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (SetPart part in plan.Parts)
            {
                if (!FarSolid(part)) continue;
                float s = part.Scale > 0f ? part.Scale : 1f;
                Vector4 f;
                if (!Footprint(part.Prefab, out f))
                {
                    float h = Math.Max(0.5f, 0.7f * part.Radius / s);
                    f = new Vector4(-h, -h, h, h);
                }

                float a = part.Yaw * (float)(Math.PI / 180.0), cs = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                float rx0 = float.MaxValue, rz0 = float.MaxValue, rx1 = float.MinValue, rz1 = float.MinValue;
                foreach (var (fx, fz) in new[] { (f.x, f.y), (f.z, f.y), (f.z, f.w), (f.x, f.w) })
                {
                    float px = part.X + (fx * cs + fz * sn) * s, pz = part.Z + (-fx * sn + fz * cs) * s;
                    rx0 = Math.Min(rx0, px); rx1 = Math.Max(rx1, px);
                    rz0 = Math.Min(rz0, pz); rz1 = Math.Max(rz1, pz);
                }

                bool roof = part.Prefab != null && part.Prefab.IndexOf("Roof", StringComparison.Ordinal) >= 0;
                rects.Add((rx0, rz0, rx1, rz1, part.Y + Math.Min(0f, part.Low), part.Y + part.High, roof));
                minX = Math.Min(minX, rx0); maxX = Math.Max(maxX, rx1);
                minZ = Math.Min(minZ, rz0); maxZ = Math.Max(maxZ, rz1);
            }

            if (rects.Count == 0) return;
            const float cell = FarCastle.Cell;
            int nx = Math.Max(1, (int)Math.Ceiling((maxX - minX) / cell)), nz = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / cell));
            var wallTop = new float[nx, nz];
            var roofTop = new float[nx, nz];
            var roofBase = new float[nx, nz];
            var bottom = new float[nx, nz];
            for (int i = 0; i < nx; i++)
            {
                for (int k = 0; k < nz; k++)
                {
                    wallTop[i, k] = roofTop[i, k] = float.MinValue;
                    roofBase[i, k] = float.MaxValue;
                    bottom[i, k] = 0f;
                }
            }

            foreach (var r in rects)
            {
                // The cells whose centre the footprint covers (shrunk 0.3 m: a wall on a cell's edge marks one row), at least the one under its middle.
                int i0 = (int)Math.Ceiling((r.x0 + 0.3f - minX) / cell - 0.5f), i1 = (int)Math.Floor((r.x1 - 0.3f - minX) / cell - 0.5f);
                int k0 = (int)Math.Ceiling((r.z0 + 0.3f - minZ) / cell - 0.5f), k1 = (int)Math.Floor((r.z1 - 0.3f - minZ) / cell - 0.5f);
                if (i1 < i0) i0 = i1 = (int)Math.Floor((0.5f * (r.x0 + r.x1) - minX) / cell);
                if (k1 < k0) k0 = k1 = (int)Math.Floor((0.5f * (r.z0 + r.z1) - minZ) / cell);
                for (int i = Math.Max(0, i0); i <= Math.Min(nx - 1, i1); i++)
                {
                    for (int k = Math.Max(0, k0); k <= Math.Min(nz - 1, k1); k++)
                    {
                        bottom[i, k] = Math.Min(bottom[i, k], r.lo);
                        if (r.roof)
                        {
                            roofTop[i, k] = Math.Max(roofTop[i, k], r.hi);
                            roofBase[i, k] = Math.Min(roofBase[i, k], r.lo);
                        }
                        else wallTop[i, k] = Math.Max(wallTop[i, k], r.hi);
                    }
                }
            }

            // Each column as (role, base, eaves, top), merged along x.
            for (int k = 0; k < nz; k++)
            {
                int run = 0;
                string runRole = null;
                int runVariant = 0;
                float runBase = 0f;
                for (int i = 0; i <= nx; i++)
                {
                    string role = null;
                    int variant = 0;
                    float baseY = 0f;
                    if (i < nx)
                    {
                        float wt = wallTop[i, k], rt = roofTop[i, k];
                        float top = Math.Max(wt, rt);
                        if (top > 1f)
                        {
                            baseY = (float)Math.Round(Math.Max(-12f, bottom[i, k]) / FarCastle.Step) * FarCastle.Step;
                            bool roof = rt > wt + 0.5f;
                            float eaves = roof ? Math.Max(wt, roofBase[i, k]) : top;
                            if (eaves < baseY + 1f) eaves = baseY + 1f;
                            role = roof ? FarCastle.Roof : FarCastle.Block;
                            variant = FarCastle.Pack(1, eaves - baseY, top - baseY) & ~(31 << 14);
                        }
                    }

                    bool same = role != null && role == runRole && variant == runVariant && Math.Abs(baseY - runBase) < 1e-3f && run < FarCastle.MaxRun;
                    if (same)
                    {
                        run++;
                        continue;
                    }

                    if (run > 0)
                    {
                        float xc = minX + (i - 0.5f * run) * cell, zc = minZ + (k + 0.5f) * cell;
                        Vector3 at = centre + right * xc + toTown * zc;
                        proxies.Add(new PropPlacement
                        {
                            role = runRole, variant = runVariant | (run << 14), position = new Vector3(at.x, ground + runBase, at.z), yaw = yaw0, scale = 1f, settlementId = p0.settlementId,
                        });
                    }

                    run = role != null ? 1 : 0;
                    runRole = role;
                    runVariant = variant;
                    runBase = baseY;
                }
            }
        }

        /// <summary>Whether a castle part counts for the distant mass: walls, battlements, towers, pillars of stone, roofs; not floors, walkways, wood, stairs, bridges or props.</summary>
        private static bool FarSolid(SetPart part)
        {
            if (part.Role != PropRole.CastleModule && part.Role != PropRole.CityWall && part.Role != PropRole.CityBattlement && part.Role != PropRole.CityTower) return false;
            string n = part.Prefab ?? string.Empty;
            foreach (string k in NotSolid) if (n.IndexOf(k, StringComparison.Ordinal) >= 0) return false;
            return part.High - part.Low > 0.3f;
        }

        private static readonly string[] NotSolid = { "Floor", "Railing", "Walkway", "Wood", "Stairs", "Bridge", "Beam", "Hatch", "Corbels", "Panel" };
    }
}
