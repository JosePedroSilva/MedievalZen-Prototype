using System;
using System.Collections.Generic;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// The walled city's hill (2026-10-06, the hill town; crisp terraces 2026-10-09): one side of the road rises in
    /// <see cref="FaceCount"/> terraces of one wall module (<see cref="Rise"/>, the demo's <c>CitySetPieces.TerraceStep</c>) to a plateau
    /// where the castle stands; the road and the lower town keep the level disc. Pure and deterministic (<see cref="EndlessNoise"/> stream
    /// <see cref="Stream"/>, the place id): the terrain (<see cref="TerrainField"/>) adds <see cref="Offset"/> on top of the city's level
    /// disc, and <see cref="CityComposer"/> stands the retaining walls on the <see cref="Runs"/>, draws the <see cref="Decks"/> and seats
    /// everything on the <see cref="SurfaceAt"/> the plan describes.
    /// <para>Frame: u along the road's tangent at the centre, v across towards the hill. Terrace faces follow the contours of
    /// q = sqrt((u/K)^2 + (DTop - v)^2) (ellipses round a point DTop out, stretched K times along the road); n is the distance behind a
    /// face's line (the wall's low face), first order (Q - q) / |grad q|.</para>
    /// <para>Two grounds (the user's walk of 2026-10-06: an 8 m riser sampled on the near tiles' 2 m heightmap was a bank several metres
    /// wide that spilled out of the wall's foot and bulged past its top). The <b>surface</b> is what the eye and the composer take for
    /// ground: true steps, the low terrace level right up to each wall's face and the high level from it. The <b>terrain</b> is the same
    /// except behind every wall, where the riser's bank lies wholly on the high side, from <see cref="Toe"/> behind the face over
    /// <see cref="BankRun"/>, under a first-party cobbled deck (<see cref="Decks"/>, drawn at the high level from the wall's top to firm
    /// ground) so the slope is never seen. Each wall module (5 m) is one riser: it stands on the low level and its top is the high level.</para>
    /// <para>The ways up: a ramp (at most <see cref="Grade"/>) in front of each face on the terrace below it, the face's wall its inner
    /// side, its own wall on the outer side (tilted with the ramp), a level landing where the face's wall opens onto the terrace above and
    /// an end wall beyond the landing; and the pack's stair flights (<see cref="FlightRun"/> x <see cref="Rise"/>, 45 degrees) standing in
    /// front of a face, which opens for them.</para>
    /// <para>The wall's outline (it moved here from the composer's random stream so the terrain knows it): R(θ) = R0 (1 + 0.04 sin(2θ+φ2)
    /// + 0.03 sin(3θ+φ3)), widened up to 10 % towards the hill, at most <see cref="MaxWallRadius"/>; the hill falls away outside it over
    /// <see cref="Flank"/> m (the outer flank, where rocks are seated).</para>
    /// </summary>
    public sealed class CityHill
    {
        /// <summary>The hill's stream (the city's 1401-1429).</summary>
        public const int Stream = 1401;
        /// <summary>One terrace's rise: one castle wall module (<c>SM_Bld_Castle_Wall_01</c>, 5 m; the demo's terraces are 5 m too).</summary>
        public const float Rise = 5f;
        /// <summary>Terrace faces (4 x 5 m: the plateau 20 m above the road, its benches about 15 m deep).</summary>
        public const int FaceCount = 4;
        /// <summary>The wall module's thickness, measured from the prefab (FBX bounds 0.50 m, centred on its line).</summary>
        public const float WallThick = 0.5f;
        /// <summary>The near tiles' heightmap texel (256 m / 128 cells): the built terrain is this grid's interpolation of the plan.</summary>
        public const float Cell = 2f;
        /// <summary>
        /// The riser's bank starts this far behind the wall's low face (the module is 0.5 m thick: the terrain stays at the low level under
        /// it and 0.5 m on), so the 2 m grid's interpolation does not lift the ground in front of the wall's foot.
        /// </summary>
        public const float Toe = 1f;
        /// <summary>
        /// Width of the riser's bank behind the toe, metres: one heightmap cell plus margin. The bank must be at least a cell wide or the
        /// 2 m grid turns it into a cliff that leans out of the wall's foot; wider than needed it only widens the deck.
        /// </summary>
        public const float BankRun = 3f;
        /// <summary>How far past the bank's top the built terrain still lags the plan: one cell across a diagonal (2 x 1.41) plus margin.</summary>
        public const float FirmLag = 3f;
        /// <summary>The deck's reach behind the wall's low face: toe, bank, the grid's lag, then 0.3 m onto firm ground.</summary>
        public const float DeckReach = Toe + BankRun + FirmLag + 0.3f;
        /// <summary>The deck's outer edge lies this far behind the wall's low face: on the module's top (5 m chords sag at most 0.1 m inside a curved face).</summary>
        public const float DeckInset = 0.1f;
        /// <summary>The deck's top above the plan's high level (a hair above the module's top and any terrain under it).</summary>
        public const float DeckLift = 0.03f;
        /// <summary>The steepest slope of a bank (the terrain under a deck).</summary>
        public const float BankSlope = Rise / BankRun;
        /// <summary>Width of a terrace face behind its wall line used by the capture stops (a point Band + 1.5 m behind a face stands on its deck).</summary>
        public const float Band = 2.5f;
        /// <summary>Steepest grade of a ramp; a flight of steps' run per metre of rise (the pack's flight: 5 m over 5 m).</summary>
        public const float Grade = 0.11f, StairRun = 1f;
        /// <summary>Half widths of a ramp and of a flight of steps, metres (<c>SM_Bld_Stairs_Straight_Wide_Edges_01</c> is 5.32 m wide with its edges).</summary>
        public const float RampHalf = 2.3f, StairHalf = 2.66f;
        /// <summary>A ramp's level landing at its top, where the face's wall opens onto the terrace above, metres along the face.</summary>
        public const float LandLen = 5f;
        /// <summary>A ramp's sloping length along its face (one rise at <see cref="Grade"/>).</summary>
        public const float SlopeLen = Rise / Grade;
        /// <summary>The terrain under a ramp's deck lies this far below the ramp (the deck's skirt hides the step at its foot).</summary>
        public const float RampSink = 0.15f;
        /// <summary>A ramp's outer wall begins where the ramp stands this far above the terrace in front of it.</summary>
        public const float MinWall = 0.25f;
        /// <summary>
        /// The pack's flight (<c>SM_Bld_Stairs_Straight_Wide_Edges_01</c>, measured from the FBX): its run along the climb is -5.00 .. +0.42 m
        /// with the top riser at 0, so its bounds centre (where a centre-pivot placement puts it) lies 2.29 m below the top riser.
        /// </summary>
        public const float FlightRun = 5f, FlightTopFromCentre = 2.29f;
        /// <summary>Faces keep their ramps and flights this far from where they meet the town wall, metres along the face.</summary>
        public const float FaceMargin = 8f;
        /// <summary>The wall's farthest reach, and the width of the hill's outer flank outside it, metres.</summary>
        public const float MaxWallRadius = 102.5f, Flank = 24f;
        /// <summary>Distance across the road of the lowest face's foot (at u = 0) and of the plateau's front, metres.</summary>
        public const float FootV = 19f, PlateauV = 65f;
        /// <summary>
        /// The castle's rock (2026-10-06): the castle's front edge stands this far behind the plateau's front (room for the last ramp's
        /// arrival and the street along the top); its rock is the castle's footprint plus <see cref="RockMargin"/> at its flanks and back,
        /// level at the plateau's height, then falling as a cliff over <see cref="CliffRun"/> m (it reaches beyond the town wall).
        /// </summary>
        public const float CastleFrontGap = 9f, RockMargin = 3f, CliffRun = 16f;
        /// <summary>The plateau's contour radius with a castle (its rock is wide), and without one (the keep in its ring).</summary>
        public const float TopQCastle = 30f, TopQKeep = 18f;

        /// <summary>Centre (the road at the place; absolute in the terrain, window-local in the composer).</summary>
        public double CX, CZ;
        /// <summary>The city's level (its disc's height), set by the composer's caller; the plan's heights are above it.</summary>
        public float Level;
        /// <summary>Unit road tangent at the centre and unit normal towards the hill (x, z).</summary>
        public float TX, TZ, NX, NZ;
        /// <summary>The hill's side of the road (+1 right of the tangent).</summary>
        public int Side;
        /// <summary>Wall outline parameters.</summary>
        public float R0, P2, P3;
        /// <summary>Contour stretch along the road, and the contours' centre across the road.</summary>
        public float K = 2.4f, DTop;
        /// <summary>Per face (outermost first): contour value, ground below it (above the road's level), its rise.</summary>
        public float[] Q, Base, Step;
        /// <summary>Each face's line inside the town wall (its wall's low face), dense, outermost first.</summary>
        public FaceLine[] Lines;
        /// <summary>The ways up (ramps first, face by face, then the flights of steps).</summary>
        public readonly List<Way> Ways = new List<Way>();
        /// <summary>The retaining walls: faces (opened at landings and flights), ramps' outer walls and end walls.</summary>
        public readonly List<Run> Runs = new List<Run>();
        /// <summary>The decks over the banks: one strip along each face, one over each ramp.</summary>
        public readonly List<DeckPlan> Decks = new List<DeckPlan>();
        /// <summary>The castle on the rock (a mined <see cref="CastlePlan"/>), or null (the keep in a ring on the plateau).</summary>
        public CastlePlan Castle;
        /// <summary>The castle's centre across the road (its +Z faces the town), and its rock's level top: |u| within <see cref="RockHalfU"/>, v from <see cref="RockV0"/> to <see cref="RockV1"/>.</summary>
        public float CastleV, RockHalfU, RockV0, RockV1;
        /// <summary>Farthest reach of the castle's rock and its cliff from the centre, metres (0 without a castle).</summary>
        public float RockReach => Castle == null ? 0f : (float)Math.Sqrt((RockHalfU + CliffRun) * (RockHalfU + CliffRun) + (RockV1 + CliffRun) * (RockV1 + CliffRun)) + 1f;

        /// <summary>Number of terrace faces (the terraces above the lower town, the plateau included).</summary>
        public int Faces => Q.Length;
        /// <summary>The plateau's height above the road.</summary>
        public float Top => Base[Q.Length - 1] + Step[Q.Length - 1];

        /// <summary>A way up: a polyline (u, v) with the ground's height (above the level) at each point.</summary>
        public sealed class Way
        {
            public bool Stair;
            public int Face;
            public float Half;
            public float[] U, V, H;
            public float U0, U1, V0, V1;   // bounds with the half width
            /// <summary>Ramps: the face's arc length at each vertex, the climbing direction along the face (+1: arc length grows), the arc length of the foot and of the landing's end.</summary>
            public float[] FS, NU, NV;
            public int Dir;
            /// <summary>Ramps: the u range behind their low end (from 3 m before the foot to <see cref="FootLen"/> m up), where the face's bank starts later.</summary>
            public float FootU0 = float.MaxValue, FootU1 = float.MinValue;
            public float SA, SB;
            public float Length { get { float l = 0f; for (int i = 1; i < U.Length; i++) l += (float)Math.Sqrt((U[i] - U[i - 1]) * (U[i] - U[i - 1]) + (V[i] - V[i - 1]) * (V[i] - V[i - 1])); return l; } }
        }

        /// <summary>A polyline in the hill's frame, dense, with a unit normal per point and its arc length.</summary>
        public class Line
        {
            public float[] U, V, NU, NV, S;
            public float Length => S[S.Length - 1];

            /// <summary>The point and normal at arc length <paramref name="s"/> (linear beyond the ends).</summary>
            public void At(float s, out float u, out float v, out float nu, out float nv)
            {
                int n = S.Length;
                int i = 1;
                if (s >= S[n - 1]) i = n - 1;
                else if (s > S[0])
                {
                    int lo = 0, hi = n - 1;
                    while (hi - lo > 1) { int m = (lo + hi) >> 1; if (S[m] <= s) lo = m; else hi = m; }
                    i = hi;
                }

                float l = S[i] - S[i - 1];
                float t = l > 1e-6f ? (s - S[i - 1]) / l : 0f;
                u = U[i - 1] + (U[i] - U[i - 1]) * t;
                v = V[i - 1] + (V[i] - V[i - 1]) * t;
                float a = t < 0f ? 0f : t > 1f ? 1f : t;
                nu = NU[i - 1] + (NU[i] - NU[i - 1]) * a;
                nv = NV[i - 1] + (NV[i] - NV[i - 1]) * a;
                float nl = (float)Math.Sqrt(nu * nu + nv * nv);
                if (nl > 1e-6f) { nu /= nl; nv /= nl; }
            }

            /// <summary>Unit tangent (direction of growing arc length) at <paramref name="s"/>.</summary>
            public void Tangent(float s, out float tu, out float tv)
            {
                At(s - 0.25f, out float u0, out float v0, out _, out _);
                At(s + 0.25f, out float u1, out float v1, out _, out _);
                tu = u1 - u0;
                tv = v1 - v0;
                float l = (float)Math.Sqrt(tu * tu + tv * tv);
                if (l > 1e-6f) { tu /= l; tv /= l; } else { tu = 1f; tv = 0f; }
            }

            protected static float[] Lengths(float[] u, float[] v)
            {
                var s = new float[u.Length];
                for (int i = 1; i < u.Length; i++) s[i] = s[i - 1] + (float)Math.Sqrt((u[i] - u[i - 1]) * (u[i] - u[i - 1]) + (v[i] - v[i - 1]) * (v[i] - v[i - 1]));
                return s;
            }
        }

        /// <summary>A face's line (its wall's low face), its normal pointing up the hill, from where it enters the town wall to where it leaves.</summary>
        public sealed class FaceLine : Line
        {
            public int Face;

            public FaceLine(int face, List<float> u, List<float> v, CityHill h)
            {
                Face = face;
                U = u.ToArray();
                V = v.ToArray();
                NU = new float[U.Length];
                NV = new float[U.Length];
                for (int i = 0; i < U.Length; i++) h.Inward(U[i], V[i], out NU[i], out NV[i]);
                S = Lengths(U, V);
            }

            /// <summary>Arc length where the line's u is <paramref name="u"/> (u grows along the line), clamped to its ends.</summary>
            public float SAtU(float u)
            {
                if (u <= U[0]) return 0f;
                for (int i = 1; i < U.Length; i++)
                {
                    if (U[i] < u) continue;
                    float t = U[i] - U[i - 1] > 1e-6f ? (u - U[i - 1]) / (U[i] - U[i - 1]) : 0f;
                    return S[i - 1] + (S[i] - S[i - 1]) * t;
                }

                return Length;
            }
        }

        /// <summary>What a retaining wall holds.</summary>
        public enum RunKind { Face, RampSide, RampEnd }

        /// <summary>
        /// A run of retaining wall: its low face as a dense polyline, the normal into the high side, the low ground (<see cref="Lo"/>) and the
        /// high level (<see cref="Hi"/>, the module's top) at each point, above the city's level. <see cref="Tilt"/>: the modules lean with a
        /// ramp (their top follows <see cref="Hi"/>), else level.
        /// </summary>
        public sealed class Run : Line
        {
            public RunKind Kind;
            public int Face;
            public float[] Lo, Hi;
            public bool Tilt;

            public Run(RunKind kind, int face, List<float> u, List<float> v, List<float> nu, List<float> nv, List<float> lo, List<float> hi, bool tilt)
            {
                Kind = kind;
                Face = face;
                U = u.ToArray();
                V = v.ToArray();
                NU = nu.ToArray();
                NV = nv.ToArray();
                Lo = lo.ToArray();
                Hi = hi.ToArray();
                Tilt = tilt;
                S = Lengths(U, V);
            }

            /// <summary>The high level at arc length <paramref name="s"/> (clamped to the run).</summary>
            public float HiAt(float s) => Interp(Hi, s);

            /// <summary>The low ground at arc length <paramref name="s"/> (clamped to the run).</summary>
            public float LoAt(float s) => Interp(Lo, s);

            private float Interp(float[] a, float s)
            {
                if (s <= 0f) return a[0];
                for (int i = 1; i < S.Length; i++)
                {
                    if (S[i] < s) continue;
                    float l = S[i] - S[i - 1];
                    return l > 1e-6f ? a[i - 1] + (a[i] - a[i - 1]) * (s - S[i - 1]) / l : a[i];
                }

                return a[a.Length - 1];
            }
        }

        /// <summary>A deck strip: its outer edge (on the wall's top) and inner edge (on firm ground) per sample, and the top's height above the level (before <see cref="DeckLift"/>).</summary>
        public sealed class DeckPlan
        {
            public RunKind Kind;
            public int Face;
            public float[] OU, OV, IU, IV, H;
            /// <summary>
            /// The parapet's lip along the outer edge (2026-10-09, the demo's low terrace parapets): per segment (sample k to k + 1) the
            /// fraction of it the lip covers, from <see cref="LipA"/> to <see cref="LipB"/> (none where <c>LipB &lt;= LipA</c>): wherever a
            /// retaining wall stands under the edge, never across a landing's or a flight's opening. <see cref="EndLip"/>: a lip across the
            /// strip's last edge too (a ramp's end wall).
            /// </summary>
            public float[] LipA, LipB;
            public bool EndLip;
        }

        /// <summary>The terrace parapet's lip (the demo's <c>CitySetPieces.ParapetHeight</c>): its height above the deck and its thickness, metres.</summary>
        public const float LipHeight = 0.44f, LipThick = 0.4f;

        /// <summary>Arc lengths along each face's line where its retaining wall stands (the face runs), for the decks' lips.</summary>
        private readonly List<(int face, float a, float b)> faceSpans = new List<(int, float, float)>();

        /// <summary>The hill of the city <paramref name="placeId"/> centred at (cx, cz) where the road's heading (radians from +z) is <paramref name="heading"/>.</summary>
        public static CityHill For(int seed, long placeId, double cx, double cz, double heading)
        {
            var h = new CityHill { CX = cx, CZ = cz, TX = (float)Math.Sin(heading), TZ = (float)Math.Cos(heading) };
            Func<int, double> u = k => EndlessNoise.Unit(seed, Stream, placeId, k);
            h.Side = u(0) < 0.5 ? -1 : 1;
            h.NX = h.TZ * h.Side;
            h.NZ = -h.TX * h.Side;
            h.R0 = 87f + 6f * (float)u(1);
            h.P2 = (float)(u(2) * Math.PI * 2.0);
            h.P3 = (float)(u(3) * Math.PI * 2.0);
            const int n = FaceCount;
            // The castle: the demo's whole castle (the screenshot's), which the rock is shaped for; its keep only when the tables lack it.
            foreach (CastlePlan c in CitySetPieces.Castles)
            {
                if (c == null || c.Parts == null || c.Parts.Length == 0) continue;
                if (h.Castle == null || c.HalfWidth * c.HalfDepth > h.Castle.HalfWidth * h.Castle.HalfDepth) h.Castle = c;
            }

            float qTop = h.Castle != null ? TopQCastle : TopQKeep;
            if (h.Castle != null)
            {
                h.CastleV = PlateauV + CastleFrontGap + h.Castle.HalfDepth;
                h.RockHalfU = h.Castle.HalfWidth + RockMargin;
                h.RockV0 = PlateauV + CastleFrontGap;
                h.RockV1 = h.CastleV + h.Castle.HalfDepth + RockMargin;
            }

            h.DTop = PlateauV + qTop;
            h.Q = new float[n];
            h.Base = new float[n];
            h.Step = new float[n];
            float pitch = (PlateauV - FootV) / (n - 1);
            for (int i = 0; i < n; i++)
            {
                h.Q[i] = h.DTop - (FootV + pitch * i);
                h.Step[i] = Rise;
                h.Base[i] = Rise * i;
            }

            h.Lines = new FaceLine[n];
            for (int i = 0; i < n; i++) h.Lines[i] = h.BuildFace(i);

            // Ramps: one in front of each face, alternating direction; each starts where the one below ended (the hairpin across the bench).
            int dir = u(5) < 0.5 ? -1 : 1;
            float us = -dir * 36f;
            for (int i = 0; i < n; i++, dir = -dir)
            {
                Way w = h.Ramp(i, us, dir);
                h.Ways.Add(w);
                h.Lines[i].At(w.SB, out us, out _, out _, out _);
            }

            // Steps: a flight up the lowest face beyond the first ramp's end, and one up the second face beyond its ramp's end.
            var flights = new List<Way>();
            for (int i = 0; i < Math.Min(2, n - 1); i++)
            {
                Way r = h.Ways[i];
                Way f = h.Flight(i, r.SB + r.Dir * (WallThick + 9f + StairHalf)) ?? h.Flight(i, r.SA - r.Dir * (9f + StairHalf));
                if (f != null) flights.Add(f);
            }

            h.Ways.AddRange(flights);
            h.PlanWalls();
            h.PlanDecks();
            return h;
        }

        // ------------------------------------------------------------------ frame

        public void Local(double x, double z, out float u, out float v)
        {
            float dx = (float)(x - CX), dz = (float)(z - CZ);
            u = dx * TX + dz * TZ;
            v = dx * NX + dz * NZ;
        }

        /// <summary>Absolute (or window-local) x and z of frame point (u, v).</summary>
        public void World(float u, float v, out double x, out double z)
        {
            x = CX + u * TX + v * NX;
            z = CZ + u * TZ + v * NZ;
        }

        /// <summary>The contour value at (u, v).</summary>
        public float QAt(float u, float v)
        {
            float a = u / K, b = DTop - v;
            return (float)Math.Sqrt(a * a + b * b);
        }

        /// <summary>|grad q| at (u, v).</summary>
        private float Grad(float u, float v)
        {
            float a = u / K, b = DTop - v;
            float q = (float)Math.Sqrt(a * a + b * b);
            return q > 1e-3f ? (float)Math.Sqrt(a * a / (K * K) + b * b) / q : 1f;
        }

        /// <summary>Distance of (u, v) behind face <paramref name="face"/>'s line (negative in front of it), first order.</summary>
        public float Behind(int face, float u, float v) => (Q[face] - QAt(u, v)) / Grad(u, v);

        /// <summary>Unit normal at (u, v) pointing up the hill (towards smaller q).</summary>
        public void Inward(float u, float v, out float nu, out float nv)
        {
            nu = -u / (K * K);
            nv = DTop - v;
            float l = (float)Math.Sqrt(nu * nu + nv * nv);
            if (l > 1e-6f) { nu /= l; nv /= l; } else { nu = 0f; nv = 1f; }
        }

        /// <summary>The point of contour <paramref name="q"/> at angle <paramref name="phi"/> (0 = facing the road).</summary>
        public void Contour(float q, float phi, out float u, out float v)
        {
            u = K * q * (float)Math.Sin(phi);
            v = DTop - q * (float)Math.Cos(phi);
        }

        /// <summary>The angle of contour <paramref name="q"/> at <paramref name="u"/> (clamped to its ends).</summary>
        public float PhiAt(float q, float u) => (float)Math.Asin(Math.Max(-0.97, Math.Min(0.97, u / (K * q))));

        /// <summary>Radial distance outside the town wall of frame point (u, v).</summary>
        public float OutsideUV(float u, float v)
        {
            World(u, v, out double x, out double z);
            return Outside(x, z);
        }

        /// <summary>The point <paramref name="n"/> m behind face <paramref name="face"/>'s line (negative: in front) from its line point (u0, v0), along its normal.</summary>
        public void OffsetPoint(int face, float u0, float v0, float nu, float nv, float n, out float u, out float v)
        {
            float t = n;
            for (int k = 0; k < 3; k++) t += n - Behind(face, u0 + nu * t, v0 + nv * t);
            u = u0 + nu * t;
            v = v0 + nv * t;
        }

        // ------------------------------------------------------------------ the wall

        public float WallRadius(float theta)
        {
            float r = R0 * (1f + 0.04f * (float)Math.Sin(2f * theta + P2) + 0.03f * (float)Math.Sin(3f * theta + P3));
            float c = (float)Math.Cos(theta - Math.Atan2(NZ, NX));
            if (c > 0f) r *= 1f + 0.1f * c * c;
            return Math.Min(r, MaxWallRadius);
        }

        /// <summary>Signed distance (radially) outside the wall's outline, metres.</summary>
        public float Outside(double x, double z)
        {
            double dx = x - CX, dz = z - CZ;
            return (float)Math.Sqrt(dx * dx + dz * dz) - WallRadius((float)Math.Atan2(dz, dx));
        }

        // ------------------------------------------------------------------ the ground

        /// <summary>Height of the hill's terrain above the city's level at (x, z) (0 off the hill): the surface with the banks under the decks.</summary>
        public float Offset(double x, double z)
        {
            Local(x, z, out float u, out float v);
            if (v < FootV - 12f || Math.Abs(u) > 180f) return 0f;
            float o = Outside(x, z);
            if (o > Flank + 1f) return 0f;
            float h = TerrainAt(u, v);
            if (o > 1f) { float t = (o - 1f) / Flank; h *= 1f - t * t * (3f - 2f * t); }
            return h;
        }

        /// <summary>
        /// Height of the hill's surface above the city's level at (x, z): the planned ground as the eye takes it (true steps, the ramps'
        /// grades, the flights' treads, the plateau), what the composer seats things on. Equal to <see cref="Offset"/> away from the banks.
        /// </summary>
        public float SurfaceOffset(double x, double z)
        {
            Local(x, z, out float u, out float v);
            if (v < FootV - 12f || Math.Abs(u) > 180f) return 0f;
            float o = Outside(x, z);
            if (o > 0f) return Offset(x, z);
            return SurfaceAt(u, v);
        }

        /// <summary>The deck's top above the city's level at (x, z) where a deck covers it (with <see cref="DeckLift"/>); false elsewhere.</summary>
        public bool DeckTop(double x, double z, out float top)
        {
            top = 0f;
            Local(x, z, out float u, out float v);
            if (v < FootV - 12f || Math.Abs(u) > 180f || Outside(x, z) > 0.05f) return false;
            return DeckAt(u, v, out top);
        }

        /// <summary>
        /// The castle rock's weight at (x, z): 1 on its level top, falling smoothly to 0 over <see cref="CliffRun"/> m beyond it (0
        /// without a castle). The terrain sets the ground to the city's level plus <see cref="Top"/> by this weight, so the castle's
        /// ground is level wherever the rock reaches, inside the levelled disc or not. On the town's side of the rock and inside the town
        /// wall the terraces' plateau and the top face's wall are the ground, so the rock does not fall towards the town there.
        /// </summary>
        public float Rock(double x, double z)
        {
            if (Castle == null) return 0f;
            Local(x, z, out float u, out float v);
            float du = Math.Max(0f, Math.Abs(u) - RockHalfU), dv = v > RockV1 ? v - RockV1 : v < RockV0 ? RockV0 - v : 0f;
            if (du >= CliffRun || dv >= CliffRun) return 0f;
            bool inside = Outside(x, z) < 0f;
            if (v < RockV0 && inside) return 0f;
            float d = (float)Math.Sqrt(du * du + dv * dv);
            if (d >= CliffRun) return 0f;
            float t = d / CliffRun;
            float w = 1f - t * t * (3f - 2f * t);
            if (!inside) return w;
            // Inside the town wall the rock never reaches over the terraces or the top face's bank: the plateau beyond the bank is level
            // at the rock's height anyway, and the bank must stay under its deck (else the ground leans out of the top wall's foot).
            float behind = Behind(Q.Length - 1, u, v) - (Toe + BankRun);
            return behind <= 0f ? 0f : behind >= 1f ? w : w * behind;
        }

        /// <summary>Whether (u, v) lies on the castle's footprint grown by <paramref name="margin"/> metres.</summary>
        public bool OnCastle(float u, float v, float margin)
        {
            if (Castle == null) return false;
            return Math.Abs(u) <= Castle.HalfWidth + margin && Math.Abs(v - CastleV) <= Castle.HalfDepth + margin;
        }

        /// <summary>The stepped terraces' terrain alone (no ways, no flank): each face's bank behind its line.</summary>
        public float Terraces(float u, float v)
        {
            float q = QAt(u, v), g = Grad(u, v);
            float h = 0f;
            for (int i = 0; i < Q.Length; i++)
            {
                float toe = Toe, run = BankRun;
                // Behind the low end of the ramp in front of this face, the bank starts 1.5 m later and rises over 1.5 m (its top where
                // it always is): the grid's interpolation would otherwise lift the ground through the ramp's deck beside the wall's foot.
                foreach (Way w in Ways)
                {
                    if (w.Stair || w.Face != i || u < w.FootU0 || u > w.FootU1) continue;
                    toe = Toe + FootToe;
                    run = BankRun - FootToe;
                    break;
                }

                float t = ((Q[i] - q) / g - toe) / run;
                h += Step[i] * (t <= 0f ? 0f : t >= 1f ? 1f : t);
            }

            return h;
        }

        /// <summary>How much later the bank starts behind a ramp's low end (<see cref="FootLen"/> m of it from its foot), metres.</summary>
        public const float FootToe = 1.5f, FootLen = 14f;

        /// <summary>The terrain above the level at frame point (u, v), inside the town (no flank).</summary>
        public float TerrainAt(float u, float v)
        {
            foreach (Way w in Ways)
            {
                if (w.Stair || !Corridor(w, u, v, 0f, out float l, out float fs)) continue;
                float n = l - w.Half;
                float hr = RampHeight(w, fs), b = Base[w.Face];
                float dOuter = n + 2f * w.Half + WallThick, dEnd = w.Dir * (w.SB + w.Dir * WallThick - fs);
                // Sunk a little under the ramp's deck: the grid smooths the ramp's own kinks (its foot, its landing) into the deck otherwise.
                return Math.Min(hr - RampSink, Math.Min(b + BankSlope * Math.Max(0f, dOuter - Toe), b + BankSlope * Math.Max(0f, dEnd - Toe)));
            }

            return Terraces(u, v);
        }

        /// <summary>The surface above the level at frame point (u, v), inside the town (no flank).</summary>
        public float SurfaceAt(float u, float v)
        {
            foreach (Way w in Ways)
            {
                if (w.Stair)
                {
                    if (FlightAt(w, u, v, out float hf)) return hf;
                    continue;
                }

                if (Corridor(w, u, v, 0f, out _, out float fs)) return RampHeight(w, fs);
            }

            float q = QAt(u, v), h = 0f;
            for (int i = 0; i < Q.Length; i++) if (q <= Q[i]) h += Step[i];
            return h;
        }

        /// <summary>The deck's top (with <see cref="DeckLift"/>) at frame point (u, v) inside the town, or false where no deck covers it.</summary>
        public bool DeckAt(float u, float v, out float top)
        {
            top = 0f;
            foreach (Way w in Ways)
            {
                if (w.Stair || !Corridor(w, u, v, DeckInset, out float l, out float fs)) continue;
                if (l - w.Half < -(2f * w.Half + WallThick) + DeckInset) continue;
                if (w.Dir * (w.SB + w.Dir * (WallThick - DeckInset) - fs) < 0f) continue;
                top = RampHeight(w, fs) + DeckLift;
                return true;
            }

            float q = QAt(u, v), g = Grad(u, v);
            for (int i = 0; i < Q.Length; i++)
            {
                float n = (Q[i] - q) / g;
                if (n < DeckInset || n > DeckReach) continue;
                top = Base[i] + Step[i] + DeckLift;
                return true;
            }

            return false;
        }

        /// <summary>A ramp's ground at face arc length <paramref name="fs"/>.</summary>
        public float RampHeight(Way w, float fs)
        {
            float t = w.Dir * (fs - w.SA) / SlopeLen;
            return Base[w.Face] + Step[w.Face] * (t <= 0f ? 0f : t >= 1f ? 1f : t);
        }

        /// <summary>
        /// Whether (u, v) lies in ramp <paramref name="w"/>'s corridor (from its outer wall's low face to the face's line plus
        /// <paramref name="grow"/>, from its foot to its end wall's low face): the lateral offset from its centre line (positive towards the
        /// face) and the face arc length there.
        /// </summary>
        public bool Corridor(Way w, float u, float v, float grow, out float lateral, out float fs)
        {
            lateral = fs = 0f;
            float m = w.Half + WallThick + 1f;
            if (u < w.U0 - m || u > w.U1 + m || v < w.V0 - m || v > w.V1 + m) return false;
            float best = float.MaxValue;
            int count = w.U.Length;
            for (int i = 1; i < count; i++)
            {
                float ex = w.U[i] - w.U[i - 1], ez = w.V[i] - w.V[i - 1], l2 = ex * ex + ez * ez;
                if (l2 < 1e-9f) continue;
                float t = ((u - w.U[i - 1]) * ex + (v - w.V[i - 1]) * ez) / l2;
                float tMax = i == count - 1 ? 1f + (WallThick + 0.05f) / (float)Math.Sqrt(l2) : 1f;
                if (i == 1 && t < 0f) continue;
                if (t > tMax) continue;
                t = t < 0f ? 0f : t;
                float tc = t > 1f ? 1f : t;
                float pu = w.U[i - 1] + ex * t, pv = w.V[i - 1] + ez * t;
                float du = u - pu, dv = v - pv, d = du * du + dv * dv;
                if (d >= best) continue;
                float nu = w.NU[i - 1] + (w.NU[i] - w.NU[i - 1]) * tc, nv = w.NV[i - 1] + (w.NV[i] - w.NV[i - 1]) * tc;
                float l = du * nu + dv * nv;
                if (l < -(w.Half + WallThick) - 0.01f || l > w.Half + grow) continue;
                if (d > (Math.Abs(l) + 0.25f) * (Math.Abs(l) + 0.25f)) continue;   // beyond a joint, not across the corridor
                best = d;
                lateral = l;
                fs = w.FS[i - 1] + (w.FS[i] - w.FS[i - 1]) * t;
            }

            return best < float.MaxValue;
        }

        /// <summary>Whether (u, v) lies on flight <paramref name="w"/>, and its tread's height there.</summary>
        private static bool FlightAt(Way w, float u, float v, out float height)
        {
            height = 0f;
            float ex = w.U[1] - w.U[0], ez = w.V[1] - w.V[0], l2 = ex * ex + ez * ez;
            if (l2 < 1e-9f) return false;
            float t = ((u - w.U[0]) * ex + (v - w.V[0]) * ez) / l2;
            if (t < 0f || t > 1f) return false;
            float l = (float)Math.Sqrt(l2);
            float lat = Math.Abs(((u - w.U[0]) * ez - (v - w.V[0]) * ex) / l);
            if (lat > w.Half) return false;
            height = w.H[0] + (w.H[1] - w.H[0]) * t;
            return true;
        }

        /// <summary>Whether (u, v) lies on a way up, and the way's ground there.</summary>
        public bool OnWay(float u, float v, out float height)
        {
            height = 0f;
            foreach (Way w in Ways)
            {
                if (u < w.U0 || u > w.U1 || v < w.V0 || v > w.V1) continue;
                float best = float.MaxValue;
                for (int i = 1; i < w.U.Length; i++)
                {
                    float ex = w.U[i] - w.U[i - 1], ez = w.V[i] - w.V[i - 1], l2 = ex * ex + ez * ez;
                    float t = l2 > 1e-9f ? ((u - w.U[i - 1]) * ex + (v - w.V[i - 1]) * ez) / l2 : 0f;
                    t = t < 0f ? 0f : t > 1f ? 1f : t;
                    float dx = w.U[i - 1] + ex * t - u, dz = w.V[i - 1] + ez * t - v, d = dx * dx + dz * dz;
                    if (d < best && d <= w.Half * w.Half)
                    {
                        best = d;
                        height = w.H[i - 1] + (w.H[i] - w.H[i - 1]) * t;
                    }
                }

                if (best < float.MaxValue) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ the faces

        /// <summary>Face <paramref name="i"/>'s line inside the town wall: its contour from the middle out both ways to where it meets the wall's outline.</summary>
        private FaceLine BuildFace(int i)
        {
            float q = Q[i], dphi = 0.4f / (q * K);
            var pos = new List<float>();
            var neg = new List<float>();
            for (int side = -1; side <= 1; side += 2)
            {
                List<float> list = side > 0 ? pos : neg;
                float phi = 0f, last = 0f;
                for (; Math.Abs(phi) < 1.55f; phi += side * dphi)
                {
                    Contour(q, phi, out float cu, out float cv);
                    if (OutsideUV(cu, cv) > 0f) break;
                    list.Add(phi);
                    last = phi;
                }

                // The exact end on the outline.
                float a = last, b = phi;
                for (int k = 0; k < 20; k++)
                {
                    float m = 0.5f * (a + b);
                    Contour(q, m, out float mu, out float mv);
                    if (OutsideUV(mu, mv) > 0f) b = m; else a = m;
                }

                if (Math.Abs(b - last) > 1e-5f) list.Add(a);
            }

            var us = new List<float>();
            var vs = new List<float>();
            for (int k = neg.Count - 1; k >= 1; k--) { Contour(q, neg[k], out float cu, out float cv); us.Add(cu); vs.Add(cv); }
            foreach (float phi in pos) { Contour(q, phi, out float cu, out float cv); us.Add(cu); vs.Add(cv); }
            return new FaceLine(i, us, vs, this);
        }

        // ------------------------------------------------------------------ the ways up

        /// <summary>The ramp in front of face <paramref name="face"/>, starting near u = <paramref name="uStart"/> and climbing towards <paramref name="dir"/>.</summary>
        private Way Ramp(int face, float uStart, int dir)
        {
            FaceLine f = Lines[face];
            float total = SlopeLen + LandLen;
            float sA = f.SAtU(uStart);
            float lo = FaceMargin + (dir > 0 ? 0f : total + WallThick), hi = f.Length - FaceMargin - (dir > 0 ? total + WallThick : 0f);
            sA = hi < lo ? 0.5f * (lo + hi) : Math.Max(lo, Math.Min(hi, sA));
            float sB = sA + dir * total;
            int count = Math.Max(2, (int)Math.Ceiling(total / 1f) + 1);
            var w = new Way
            {
                Face = face, Half = RampHalf, Dir = dir, SA = sA, SB = sB,
                U = new float[count], V = new float[count], H = new float[count], FS = new float[count], NU = new float[count], NV = new float[count],
            };
            for (int k = 0; k < count; k++)
            {
                float s = sA + dir * total * k / (count - 1);
                f.At(s, out float fu, out float fv, out float nu, out float nv);
                OffsetPoint(face, fu, fv, nu, nv, -RampHalf, out w.U[k], out w.V[k]);
                w.FS[k] = s;
                w.NU[k] = nu;
                w.NV[k] = nv;
                w.H[k] = RampHeight(w, s);
            }

            w.H[count - 1] = Base[face] + Step[face];
            f.At(sA - dir * 3f, out float fu0, out _, out _, out _);
            f.At(sA + dir * FootLen, out float fu1, out _, out _, out _);
            w.FootU0 = Math.Min(fu0, fu1);
            w.FootU1 = Math.Max(fu0, fu1);
            Bounds(w);
            return w;
        }

        /// <summary>A flight of the pack's stairs standing in front of face <paramref name="face"/> at arc length <paramref name="s"/>, or null when it does not fit there.</summary>
        private Way Flight(int face, float s)
        {
            FaceLine f = Lines[face];
            if (s < FaceMargin + StairHalf || s > f.Length - FaceMargin - StairHalf) return null;
            foreach (Way r in Ways)
            {
                if (r.Face != face) continue;
                float a = Math.Min(r.SA, r.SB + r.Dir * WallThick) - StairHalf - 2f, b = Math.Max(r.SA, r.SB + r.Dir * WallThick) + StairHalf + 2f;
                if (s > a && s < b) return null;
            }

            f.At(s, out float fu, out float fv, out float nu, out float nv);
            var w = new Way
            {
                Stair = true, Face = face, Half = StairHalf, Dir = 1, SA = s, SB = s,
                U = new[] { fu - nu * FlightRun, fu }, V = new[] { fv - nv * FlightRun, fv }, H = new[] { Base[face], Base[face] + Step[face] },
                FS = new[] { s, s }, NU = new[] { nu, nu }, NV = new[] { nv, nv },
            };
            Bounds(w);
            return w;
        }

        private static void Bounds(Way w)
        {
            w.U0 = w.V0 = float.MaxValue;
            w.U1 = w.V1 = float.MinValue;
            for (int i = 0; i < w.U.Length; i++)
            {
                w.U0 = Math.Min(w.U0, w.U[i] - w.Half); w.U1 = Math.Max(w.U1, w.U[i] + w.Half);
                w.V0 = Math.Min(w.V0, w.V[i] - w.Half); w.V1 = Math.Max(w.V1, w.V[i] + w.Half);
            }
        }

        // ------------------------------------------------------------------ walls and decks

        /// <summary>The retaining walls: each face's line (inside the town wall's inner face, opened at its ramp's landing and its flight), each ramp's outer wall and end wall.</summary>
        private void PlanWalls()
        {
            for (int i = 0; i < Faces; i++)
            {
                FaceLine f = Lines[i];
                if (f.U.Length < 2) continue;
                // From where the line is 0.3 m inside the town wall's outline (its modules meet the town wall's inner face).
                float s0 = InsideFrom(f, 0f, 1f), s1 = InsideFrom(f, f.Length, -1f);
                var gaps = new List<(float a, float b)>();
                foreach (Way w in Ways)
                {
                    if (w.Face != i) continue;
                    if (w.Stair) gaps.Add((w.SA - StairHalf, w.SA + StairHalf));
                    else gaps.Add((Math.Min(w.SB - w.Dir * LandLen, w.SB), Math.Max(w.SB - w.Dir * LandLen, w.SB)));
                }

                gaps.Sort((x, y) => x.a.CompareTo(y.a));
                float from = s0;
                foreach (var (a, b) in gaps)
                {
                    if (a - from > 1f) FaceRun(f, from, Math.Min(a, s1));
                    from = Math.Max(from, b);
                }

                if (s1 - from > 1f) FaceRun(f, from, s1);
            }

            foreach (Way w in Ways)
            {
                if (w.Stair) continue;
                FaceLine f = Lines[w.Face];
                float b = Base[w.Face], hi = b + Step[w.Face], outer = -(2f * w.Half + WallThick);
                // The outer wall: from where the ramp stands MinWall above the terrace to the landing, tilted with the ramp; then level along
                // the landing to the end wall's low face (two runs, a pillar where the slope meets the landing).
                float sStart = w.SA + w.Dir * SlopeLen * MinWall / Step[w.Face], sKink = w.SA + w.Dir * SlopeLen, sEnd = w.SB + w.Dir * WallThick;
                OuterRun(w, f, sStart, sKink, outer, true);
                OuterRun(w, f, sKink, sEnd, outer, false);

                // The end wall beyond the landing, across the corridor from the face's line to the outer wall's low face.
                f.At(sEnd, out float eu, out float ev, out float enu, out float env);
                OffsetPoint(w.Face, eu, ev, enu, env, outer, out float ou2, out float ov2);
                f.Tangent(sEnd, out float tu, out float tv);
                float inu = -w.Dir * tu, inv = -w.Dir * tv;
                Runs.Add(new Run(RunKind.RampEnd, w.Face, new List<float> { eu, ou2 }, new List<float> { ev, ov2 }, new List<float> { inu, inu }, new List<float> { inv, inv },
                    new List<float> { b, b }, new List<float> { hi, hi }, false));
            }
        }

        private void OuterRun(Way w, FaceLine f, float s0, float s1, float outer, bool tilt)
        {
            var us = new List<float>(); var vs = new List<float>(); var nus = new List<float>(); var nvs = new List<float>();
            var los = new List<float>(); var his = new List<float>();
            float len = Math.Abs(s1 - s0);
            int count = Math.Max(2, (int)Math.Ceiling(len / 0.5f) + 1);
            for (int k = 0; k < count; k++)
            {
                float s = s0 + (s1 - s0) * k / (count - 1);
                f.At(s, out float fu, out float fv, out float nu, out float nv);
                OffsetPoint(w.Face, fu, fv, nu, nv, outer, out float ou, out float ov);
                us.Add(ou); vs.Add(ov); nus.Add(nu); nvs.Add(nv); los.Add(Base[w.Face]); his.Add(RampHeight(w, s));
            }

            Runs.Add(new Run(RunKind.RampSide, w.Face, us, vs, nus, nvs, los, his, tilt));
        }

        /// <summary>Arc length on <paramref name="f"/> from <paramref name="s"/> towards <paramref name="step"/> where it is 0.3 m inside the town wall's outline.</summary>
        private float InsideFrom(FaceLine f, float s, float step)
        {
            float a = s, b = s;
            for (int k = 0; k < 200; k++, b += step * 0.25f)
            {
                f.At(b, out float u, out float v, out _, out _);
                if (OutsideUV(u, v) <= -0.3f) break;
                a = b;
            }

            for (int k = 0; k < 16; k++)
            {
                float m = 0.5f * (a + b);
                f.At(m, out float u, out float v, out _, out _);
                if (OutsideUV(u, v) <= -0.3f) b = m; else a = m;
            }

            return b;
        }

        private void FaceRun(FaceLine f, float s0, float s1)
        {
            if (s1 - s0 < 1f) return;
            var us = new List<float>(); var vs = new List<float>(); var nus = new List<float>(); var nvs = new List<float>();
            var los = new List<float>(); var his = new List<float>();
            float b = Base[f.Face], hi = b + Step[f.Face];
            void Add(float s)
            {
                f.At(s, out float u, out float v, out float nu, out float nv);
                us.Add(u); vs.Add(v); nus.Add(nu); nvs.Add(nv); los.Add(b); his.Add(hi);
            }

            Add(s0);
            for (int k = 0; k < f.S.Length; k++) if (f.S[k] > s0 + 0.05f && f.S[k] < s1 - 0.05f) Add(f.S[k]);
            Add(s1);
            Runs.Add(new Run(RunKind.Face, f.Face, us, vs, nus, nvs, los, his, false));
            faceSpans.Add((f.Face, s0, s1));
        }

        /// <summary>Per segment of <paramref name="d"/> (face arc lengths <paramref name="s"/> per sample) the fraction covered by <paramref name="spans"/>.</summary>
        private static void Lips(DeckPlan d, float[] s, List<(float a, float b)> spans)
        {
            int n = s.Length - 1;
            d.LipA = new float[Math.Max(0, n)];
            d.LipB = new float[Math.Max(0, n)];
            for (int k = 0; k < n; k++)
            {
                float s0 = s[k], s1 = s[k + 1], len = s1 - s0;
                if (Math.Abs(len) < 1e-6f) continue;
                float best = 0f;
                foreach (var (a, b) in spans)
                {
                    // The span in the segment's fraction (the segment may run either way along the face).
                    float ta = (a - s0) / len, tb = (b - s0) / len;
                    float lo = Math.Max(0f, Math.Min(ta, tb)), hi = Math.Min(1f, Math.Max(ta, tb));
                    if (hi - lo <= best) continue;
                    best = hi - lo;
                    d.LipA[k] = lo;
                    d.LipB[k] = hi;
                }
            }
        }

        /// <summary>The decks: along each face's whole line inside the town (from the wall's top to firm ground), and over each ramp's corridor.</summary>
        private void PlanDecks()
        {
            for (int i = 0; i < Faces; i++)
            {
                FaceLine f = Lines[i];
                if (f.U.Length < 2) continue;
                var ou = new List<float>(); var ov = new List<float>(); var iu = new List<float>(); var iv = new List<float>(); var hs = new List<float>();
                var ss = new List<float>();
                float top = Base[i] + Step[i];
                int count = Math.Max(2, (int)Math.Ceiling(f.Length / 1f) + 1);
                for (int k = 0; k < count; k++)
                {
                    float s = f.Length * k / (count - 1);
                    f.At(s, out float fu, out float fv, out float nu, out float nv);
                    OffsetPoint(i, fu, fv, nu, nv, DeckInset, out float au, out float av);
                    OffsetPoint(i, fu, fv, nu, nv, DeckReach, out float bu, out float bv);
                    if (OutsideUV(au, av) > 0.05f)
                    {
                        Flush();
                        continue;
                    }

                    if (OutsideUV(bu, bv) > 0.05f)
                    {
                        // Clip the inner edge to the town wall's line.
                        float a = 0f, c = 1f;
                        for (int it = 0; it < 16; it++)
                        {
                            float m = 0.5f * (a + c);
                            if (OutsideUV(au + (bu - au) * m, av + (bv - av) * m) > 0.05f) c = m; else a = m;
                        }

                        bu = au + (bu - au) * a;
                        bv = av + (bv - av) * a;
                    }

                    ou.Add(au); ov.Add(av); iu.Add(bu); iv.Add(bv); hs.Add(top); ss.Add(s);
                }

                Flush();

                void Flush()
                {
                    if (ou.Count >= 2)
                    {
                        var d = new DeckPlan { Kind = RunKind.Face, Face = i, OU = ou.ToArray(), OV = ov.ToArray(), IU = iu.ToArray(), IV = iv.ToArray(), H = hs.ToArray() };
                        var spans = new List<(float, float)>();
                        foreach (var (face, a, b) in faceSpans) if (face == i) spans.Add((a, b));
                        Lips(d, ss.ToArray(), spans);
                        Decks.Add(d);
                    }

                    ou.Clear(); ov.Clear(); iu.Clear(); iv.Clear(); hs.Clear(); ss.Clear();
                }
            }

            foreach (Way w in Ways)
            {
                if (w.Stair) continue;
                FaceLine f = Lines[w.Face];
                float sEnd = w.SB + w.Dir * (WallThick - DeckInset), len = Math.Abs(sEnd - w.SA);
                int count = Math.Max(2, (int)Math.Ceiling(len / 1f) + 1);
                var d = new DeckPlan { Kind = RunKind.RampSide, Face = w.Face, OU = new float[count], OV = new float[count], IU = new float[count], IV = new float[count], H = new float[count], EndLip = true };
                var ss = new float[count];
                for (int k = 0; k < count; k++)
                {
                    float s = w.SA + (sEnd - w.SA) * k / (count - 1);
                    ss[k] = s;
                    f.At(s, out float fu, out float fv, out float nu, out float nv);
                    OffsetPoint(w.Face, fu, fv, nu, nv, -(2f * w.Half + WallThick) + DeckInset, out d.OU[k], out d.OV[k]);
                    OffsetPoint(w.Face, fu, fv, nu, nv, DeckInset, out d.IU[k], out d.IV[k]);
                    d.H[k] = RampHeight(w, s);
                }

                // The lip along the ramp's outer wall: from where the wall begins (the ramp MinWall above the terrace) to the end wall.
                float sStart = w.SA + w.Dir * SlopeLen * MinWall / Step[w.Face];
                Lips(d, ss, new List<(float, float)> { (Math.Min(sStart, sEnd), Math.Max(sStart, sEnd)) });
                Decks.Add(d);
            }
        }
    }
}
