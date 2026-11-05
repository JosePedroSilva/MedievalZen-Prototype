using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// One stream: the centre line of its channel from the source to the mouth (about 3 m between points, absolute
    /// coordinates) with the bed, water surface, widths and flow direction at each point. Immutable once built.
    /// </summary>
    public sealed class StreamCourse
    {
        /// <summary>The road crossing the stream belongs to.</summary>
        public Crossing Crossing;
        /// <summary>True when the terrain lets the stream run there (else the crossing is dropped and the arrays are empty).</summary>
        public bool Accepted;
        /// <summary>Why a crossing was dropped (diagnostics), or null.</summary>
        public string Reason;
        /// <summary>Number of points.</summary>
        public int Count;
        /// <summary>Centre line, absolute metres, source first.</summary>
        public double[] X, Z;
        /// <summary>Distance along the stream from its source, metres.</summary>
        public float[] Along;
        /// <summary>Height of the channel bed (never rises downstream).</summary>
        public float[] Bed;
        /// <summary>Height of the water surface (never rises downstream).</summary>
        public float[] Surface;
        /// <summary>Width of the flat bed, metres.</summary>
        public float[] Width;
        /// <summary>Half width of the water surface (where the carved banks meet it), metres.</summary>
        public float[] HalfWater;
        /// <summary>Half width of the drawn water ribbon (tucked under the banks), metres.</summary>
        public float[] HalfRibbon;
        /// <summary>Unit flow direction.</summary>
        public float[] DirX, DirZ;
        /// <summary>Base ground (before carving) on the centre line.</summary>
        public float[] Ground;
        /// <summary>
        /// How far beyond the water's edge the drawn bank rises before it meets the ground (estimated from the ground on both
        /// sides; the larger side), metres.
        /// </summary>
        public float[] BankTop;
        /// <summary>Half width of the whole drawn stream (water, bank and the strip draped over the ground), metres.</summary>
        public float[] Outer;
        /// <summary>0..1: how much of a riffle the stream is here (narrow, quick water: more foam).</summary>
        public float[] Riffle;
        /// <summary>
        /// Within this distance of the road crossing (along the stream) the road shapes the ground: no drawn bank there (a ford: the
        /// road bed and half a metre; a bridge: the span and its approaches).
        /// </summary>
        public float RoadZone;

        /// <summary>Beyond <see cref="RoadZone"/> the drawn bank fades in over this length, metres.</summary>
        public float RoadFade => Crossing != null && Crossing.Kind == CrossingKind.Ford ? 1.2f : 2f;
        /// <summary>Index of the point on the road's centre line.</summary>
        public int CrossingIndex;
        /// <summary>True when the stream runs into a lake.</summary>
        public bool EndsInLake;
        /// <summary>Groups of consecutive segments with their bounds (grown by the carving reach), for lookups.</summary>
        public StreamPiece[] Pieces = Array.Empty<StreamPiece>();
        /// <summary>Bounds of every point (no reach).</summary>
        public double MinX, MaxX, MinZ, MaxZ;

        /// <summary>Total length, metres.</summary>
        public float Length => Count > 0 ? Along[Count - 1] : 0f;

        private StreamLine line;

        /// <summary>The centre line smoothed (Catmull-Rom through the points) and resampled every metre, computed once.</summary>
        public StreamLine Line => line ?? System.Threading.Interlocked.CompareExchange(ref line, StreamLine.Of(this), null) ?? line;
    }

    /// <summary>
    /// A stream's centre line smoothed and resampled every <see cref="Spacing"/> metres of <see cref="StreamCourse.Along"/> (a
    /// uniform Catmull-Rom through the 3 m points, so it passes through them and strays a few centimetres from the polyline,
    /// covered by <see cref="StreamNetwork.ReachSlack"/>), with the course's values interpolated at each point. What the drawn
    /// stream follows. Immutable.
    /// </summary>
    public sealed class StreamLine
    {
        /// <summary>Spacing of the points along the stream, metres.</summary>
        public const float Spacing = 1f;

        /// <summary>Number of points.</summary>
        public int Count;
        /// <summary>Absolute position.</summary>
        public double[] X, Z;
        /// <summary>Unit flow direction (from the curve's tangent).</summary>
        public float[] DirX, DirZ;
        /// <summary>Distance along the stream (the course's measure: k × <see cref="Spacing"/>, the last point at its length).</summary>
        public float[] Along;
        /// <summary>Course values interpolated at the point.</summary>
        public float[] Bed, Surface, HalfWater, HalfRibbon, BankTop, Outer, Riffle;

        /// <summary>Builds the line of <paramref name="c"/>.</summary>
        public static StreamLine Of(StreamCourse c)
        {
            var l = new StreamLine();
            if (c.Count < 2)
            {
                l.X = l.Z = Array.Empty<double>();
                l.DirX = l.DirZ = l.Along = l.Bed = l.Surface = l.HalfWater = l.HalfRibbon = l.BankTop = l.Outer = l.Riffle = Array.Empty<float>();
                return l;
            }

            float length = c.Length;
            int n = (int)Math.Floor(length / Spacing) + 1;
            if (length - (n - 1) * Spacing > 0.05f) n++;
            l.Count = n;
            l.X = new double[n];
            l.Z = new double[n];
            l.DirX = new float[n];
            l.DirZ = new float[n];
            l.Along = new float[n];
            l.Bed = new float[n];
            l.Surface = new float[n];
            l.HalfWater = new float[n];
            l.HalfRibbon = new float[n];
            l.BankTop = new float[n];
            l.Outer = new float[n];
            l.Riffle = new float[n];
            int seg = 0;
            for (int k = 0; k < n; k++)
            {
                float a = Math.Min(length, k * Spacing);
                while (seg + 2 < c.Count && c.Along[seg + 1] <= a) seg++;
                float span = Math.Max(1e-4f, c.Along[seg + 1] - c.Along[seg]);
                double t = Math.Max(0.0, Math.Min(1.0, (a - c.Along[seg]) / span));
                int i0 = Math.Max(0, seg - 1), i1 = seg, i2 = seg + 1, i3 = Math.Min(c.Count - 1, seg + 2);
                double t2 = t * t, t3 = t2 * t;
                double b0 = -0.5 * t3 + t2 - 0.5 * t, b1 = 1.5 * t3 - 2.5 * t2 + 1.0, b2 = -1.5 * t3 + 2.0 * t2 + 0.5 * t, b3 = 0.5 * t3 - 0.5 * t2;
                double d0 = -1.5 * t2 + 2.0 * t - 0.5, d1 = 4.5 * t2 - 5.0 * t, d2 = -4.5 * t2 + 4.0 * t + 0.5, d3 = 1.5 * t2 - t;
                // Ends: reflect the missing neighbour so the curve keeps the end segment's direction.
                double x0 = i0 == i1 ? 2.0 * c.X[i1] - c.X[i2] : c.X[i0], z0 = i0 == i1 ? 2.0 * c.Z[i1] - c.Z[i2] : c.Z[i0];
                double x3 = i3 == i2 ? 2.0 * c.X[i2] - c.X[i1] : c.X[i3], z3 = i3 == i2 ? 2.0 * c.Z[i2] - c.Z[i1] : c.Z[i3];
                l.X[k] = b0 * x0 + b1 * c.X[i1] + b2 * c.X[i2] + b3 * x3;
                l.Z[k] = b0 * z0 + b1 * c.Z[i1] + b2 * c.Z[i2] + b3 * z3;
                double dx = d0 * x0 + d1 * c.X[i1] + d2 * c.X[i2] + d3 * x3, dz = d0 * z0 + d1 * c.Z[i1] + d2 * c.Z[i2] + d3 * z3;
                double dl = Math.Sqrt(dx * dx + dz * dz);
                l.DirX[k] = dl > 1e-9 ? (float)(dx / dl) : c.DirX[i1];
                l.DirZ[k] = dl > 1e-9 ? (float)(dz / dl) : c.DirZ[i1];
                float f = (float)t;
                l.Along[k] = a;
                l.Bed[k] = Lerp(c.Bed, seg, f);
                l.Surface[k] = Lerp(c.Surface, seg, f);
                l.HalfWater[k] = Lerp(c.HalfWater, seg, f);
                l.HalfRibbon[k] = Lerp(c.HalfRibbon, seg, f);
                l.BankTop[k] = Lerp(c.BankTop, seg, f);
                l.Outer[k] = Lerp(c.Outer, seg, f);
                l.Riffle[k] = Lerp(c.Riffle, seg, f);
            }

            return l;
        }

        private static float Lerp(float[] v, int i, float f) => v[i] + (v[i + 1] - v[i]) * f;
    }

    /// <summary>A run of consecutive segments of a <see cref="StreamCourse"/> (points First..Last) and its bounds grown by the reach of the carving.</summary>
    public sealed class StreamPiece
    {
        /// <summary>The stream.</summary>
        public StreamCourse Course;
        /// <summary>First and last point.</summary>
        public int First, Last;
        /// <summary>Bounds of the points grown by the reach.</summary>
        public double MinX, MaxX, MinZ, MaxZ;
    }

    /// <summary>The nearest point of a stream to a query position, with the stream's values there.</summary>
    public struct StreamSample
    {
        /// <summary>The stream (null when none is near).</summary>
        public StreamCourse Course;
        /// <summary>Horizontal distance from the centre line, metres.</summary>
        public float Distance;
        /// <summary>Bed height, water surface, bed width, half widths of the water and the ribbon there.</summary>
        public float Bed, Surface, Width, HalfWater, HalfRibbon;
        /// <summary>Distance along the stream from its source, and from the road crossing (negative upstream).</summary>
        public float Along, FromCrossing;
        /// <summary>Half width of the whole drawn stream there (<see cref="StreamCourse.Outer"/>).</summary>
        public float Outer;
        /// <summary>Where the drawn bank meets the ground there, metres beyond the water's edge (<see cref="StreamCourse.BankTop"/>).</summary>
        public float BankTop;
        /// <summary>
        /// How much nearer to the centre line than the query point a point can be that the height-map sample at the query point
        /// shapes (2 m grid: half the 4 × 4 m square of the cells around a sample, measured across the nearest segment), plus slack
        /// for the smoothed centre line the mesh follows. See <see cref="StreamNetwork.Carve"/>.
        /// </summary>
        public float Reach;
    }

    /// <summary>
    /// The streams of the endless world, one per accepted <see cref="Crossing"/>, as pure functions of the seed. A course is
    /// traced on the terrain before streams (<see cref="TerrainField.BaseHeight(double, double)"/>) from the road crossing both
    /// ways: perpendicular to the road for its first metres, then following the fall line (up it towards the source, down it
    /// towards the mouth) with a gentle meander, never back towards the road, stopping at settlements, lakes (the stream runs
    /// into the lake), cliffs (the source) or after its reach. Its bed is a running minimum of the ground minus the channel
    /// depth from the source down, so it never rises downstream; at a ford the bed meets the road's dip, at a bridge it passes
    /// at least 0.45 m of clearance below the deck. Where the water would stand above the ground (a hollow upstream, a bank
    /// falling away) the course is shortened; where it would have to cut too deep the stream ends. Courses of neighbouring
    /// crossings that meet are cut where they meet (the later one joins the earlier). Carving: a flat bed, a gentle first metre
    /// of bank (1 in 4) then 1 in 1.67 up to the ground, and a small levee where the ground beside the water is lower than the
    /// water. Cached (lazily, one computation per stream); thread-safe; no global lock.
    /// </summary>
    public sealed class StreamNetwork
    {
        /// <summary>Spacing of the centre-line points, metres.</summary>
        public const double PointSpacing = 3.0;

        /// <summary>The gentle first part of a bank: its width (m) and slope (rise over run).</summary>
        public const float GentleBank = 1f, GentleSlope = 0.25f;

        /// <summary>Slope of the bank above its gentle first metre (rise over run, about 31°).</summary>
        public const float BankSlope = 0.6f;

        /// <summary>Along the stream, this far either side of a bridge crossing the channel passes under the road (the deck spans it).</summary>
        public const float BridgeSpanHalf = 6.5f;

        /// <summary>Bed width under a bridge, at most (the channel narrows to pass through the arch, <see cref="BridgeShape.ArchHalf"/>).</summary>
        public const float BridgeBedWidth = 4.2f;

        /// <summary>Least clearance between the water and the road (the deck's ends) at a bridge, metres.</summary>
        public const float BridgeClearance = 0.45f;

        /// <summary>Deepest channel under a bridge below the road, metres (the arch's piers reach 3.8 m below the deck's ends).</summary>
        public const float BridgeMaxDepth = 1.6f;

        /// <summary>Shallowest channel under a bridge below the road, metres: enough for the arch to open well above the water.</summary>
        public const float BridgeMinDepth = 1.25f;

        /// <summary>Largest extra cut beyond the channel depth before a stream must end, metres.</summary>
        public const float MaxExtraCut = 2.4f;

        /// <summary>How far the carving reaches beyond half the bed width, metres.</summary>
        public const float CarveReach = 11f;

        /// <summary>Points per <see cref="StreamPiece"/>.</summary>
        private const int PiecePoints = 12;

        /// <summary>Height of the z buckets of the lookup, metres.</summary>
        private const double BucketSize = 64.0;

        // ------------------------------------------------------------------ the drawn stream (2026-10-02)
        // A near tile's height map has a sample every 2 m, too coarse for a brook 3-5 m wide: where the ground crossed a flat water
        // plane it drew a jagged, stair-stepped edge and poked through the water. The visible stream is now a mesh (water, a wet
        // lip, a bank that rises to the ground and a strip draped over the ground; TileStreams.Mesh), and the ground is carved
        // just below that mesh over its whole footprint: a sample is lowered to what the mesh is at the nearest point the sample
        // can shape (eroded by the 2 m grid, see StreamSample.Reach), so linear interpolation between samples can never lift the
        // ground through the water or the bank.

        /// <summary>The ground lies at least this far below the drawn stream (water, bank, drape) everywhere under it, metres.</summary>
        public const float LookMargin = 0.05f;

        /// <summary>Largest <see cref="StreamSample.Reach"/>: half the diagonal of the 4 m square around a sample plus slack, metres.</summary>
        public const float MaxReach = 2.83f + ReachSlack;

        /// <summary>Slack added to the grid reach for the smoothed centre line the mesh follows (its largest offset from the 3 m polyline is about 8 cm).</summary>
        public const float ReachSlack = 0.15f;

        /// <summary>Width of the wet lip of mud and gravel at the water's edge, metres.</summary>
        public const float LipWidth = 0.6f;

        /// <summary>The drawn bank starts this far under the water (beyond its edge it rises; under it it sinks 12 cm per metre).</summary>
        public const float BankUnderWater = 0.25f;

        /// <summary>The drape over the ground reaches this far beyond the top of the bank (where the eroded carving still lowers the ground).</summary>
        public const float DrapeWidth = 2f * MaxReach + 0.2f;

        /// <summary>
        /// Height of the drawn bank above the water surface at <paramref name="x"/> metres beyond the water's edge (negative: under
        /// the water): 12 cm per metre under the water, then a concave rise, 0.18 at the edge steepening to 0.8 (39°).
        /// </summary>
        public static float BankRise(float x)
        {
            if (x <= 0f) return 0.12f * x;
            const float a = 0.18f, b = 0.16f, cap = 0.8f;
            const float xc = (cap - a) / (2f * b);
            if (x <= xc) return a * x + b * x * x;
            return a * xc + b * xc * xc + cap * (x - xc);
        }

        /// <summary>Inverse of <see cref="BankRise"/> for rises of 0 and more.</summary>
        public static float BankRiseInverse(float rise)
        {
            if (rise <= 0f) return 0f;
            const float a = 0.18f, b = 0.16f, cap = 0.8f;
            const float xc = (cap - a) / (2f * b);
            float top = a * xc + b * xc * xc;
            if (rise >= top) return xc + (rise - top) / cap;
            return (-a + (float)Math.Sqrt(a * a + 4f * b * rise)) / (2f * b);
        }

        /// <summary>
        /// The low lip that holds the water where the ground beside it is lower than the water, above the water surface at
        /// <paramref name="x"/> metres beyond its edge: never above the bank, 5 cm on the lip, then falling 0.4 per metre.
        /// </summary>
        public static float LeveeRise(float x) => Math.Min(BankRise(x), 0.05f - 0.4f * Math.Max(0f, x - LipWidth));

        /// <summary>How steeply the drawn bank rises beyond its top (<see cref="StreamCourse.BankTop"/>) to meet the ground at once.</summary>
        public const float BeyondTopRise = 10f;

        /// <summary>
        /// The drawn bank's rise above the water <paramref name="x"/> metres beyond its edge when it does not meet the ground first:
        /// <see cref="BankRise"/>, steepening to <see cref="BeyondTopRise"/> beyond <paramref name="top"/>.
        /// </summary>
        public static float DrawnRise(float x, float top) => BankRise(x) + BeyondTopRise * Math.Max(0f, x - top);

        /// <summary>
        /// The lowest the drawn stream is at <paramref name="u"/> metres from the centre line (water <paramref name="halfWater"/> wide at
        /// <paramref name="surface"/>, bank top <paramref name="top"/>): the bank under the water's edge, then the rising bank. The
        /// ground carving keeps below this (eroded by the grid) and below the channelled ground minus <see cref="LookMargin"/>.
        /// </summary>
        public static float LowestDrawn(float u, float halfWater, float surface, float top) =>
            u <= halfWater ? surface + BankRise(-BankUnderWater) : surface + DrawnRise(u - halfWater, top);

        private readonly EndlessParams p;
        private readonly RoadCurve road;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly ElevationProfile profile;
        private readonly TerrainField field;
        private readonly ConcurrentDictionary<long, Lazy<StreamCourse>> raw = new ConcurrentDictionary<long, Lazy<StreamCourse>>();
        private readonly ConcurrentDictionary<long, Lazy<StreamCourse>> final = new ConcurrentDictionary<long, Lazy<StreamCourse>>();
        private readonly ConcurrentDictionary<long, StreamPiece[]> buckets = new ConcurrentDictionary<long, StreamPiece[]>();
        private readonly Func<long, Lazy<StreamCourse>> makeRaw, makeFinal;

        /// <summary>Creates the network (nothing is computed until asked for).</summary>
        public StreamNetwork(EndlessParams p, RoadCurve road, RegionSequence regions, JourneyPlan plan, ElevationProfile profile, TerrainField field)
        {
            this.p = p;
            this.road = road;
            this.regions = regions;
            this.plan = plan;
            this.profile = profile;
            this.field = field;
            makeRaw = k => new Lazy<StreamCourse>(() => BuildRaw(plan.Crossings.Cell(k)), LazyThreadSafetyMode.ExecutionAndPublication);
            makeFinal = k => new Lazy<StreamCourse>(() => BuildFinal(k), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>The schedule of crossings.</summary>
        public StreamPlan Plan => plan.Crossings;

        /// <summary>True when the planned crossing <paramref name="c"/> has a stream (the terrain allows one).</summary>
        public bool Accepted(Crossing c) => c != null && Raw(c.Cell).Accepted;

        /// <summary>The stream of crossing <paramref name="c"/> (shortened where it meets a neighbour); not accepted when there is none.</summary>
        public StreamCourse Course(Crossing c) => final.GetOrAdd(c.Cell, makeFinal).Value;

        private StreamCourse Raw(long cell) => raw.GetOrAdd(cell, makeRaw).Value;

        /// <summary>Appends the accepted crossings with road distance in [s0, s1], in order.</summary>
        public void Crossings(double s0, double s1, List<Crossing> results)
        {
            var all = new List<Crossing>();
            plan.Crossings.Crossings(s0, s1, all);
            foreach (Crossing c in all)
            {
                if (Accepted(c)) results.Add(c);
            }
        }

        /// <summary>The accepted crossing whose level stretch contains <paramref name="s"/>, or null.</summary>
        public Crossing CrossingAt(double s)
        {
            Crossing c = plan.Crossings.At(s);
            return c != null && Accepted(c) ? c : null;
        }

        /// <summary>
        /// The dip of the road into a ford at road distance <paramref name="s"/> (metres, 0 elsewhere) and its slope d(dip)/ds.
        /// A raised cosine <see cref="StreamPlan.FordDip"/> deep and 2 × <see cref="StreamPlan.FordDipHalf"/> long.
        /// </summary>
        public float FordDip(double s, out float slope)
        {
            slope = 0f;
            Crossing c = plan.Crossings.At(s);
            if (c == null || c.Kind != CrossingKind.Ford) return 0f;
            double d = s - c.S;
            if (Math.Abs(d) >= StreamPlan.FordDipHalf || !Accepted(c)) return 0f;
            double a = Math.PI * d / StreamPlan.FordDipHalf;
            slope = (float)(-StreamPlan.FordDip * 0.5 * Math.Sin(a) * Math.PI / StreamPlan.FordDipHalf);
            return (float)(StreamPlan.FordDip * 0.5 * (1.0 + Math.Cos(a)));
        }

        /// <summary>Half width (m) of the water over the road at a ford: where the dip is less than <see cref="StreamPlan.FordWater"/> deep.</summary>
        public static float FordWaterHalf => (float)(StreamPlan.FordDipHalf / Math.PI * Math.Acos(1.0 - 2.0 * StreamPlan.FordWater / StreamPlan.FordDip));

        // ------------------------------------------------------------------ lookups

        /// <summary>The stream pieces whose reach overlaps the 64 m band of z containing <paramref name="z"/> (shared array; do not modify).</summary>
        public StreamPiece[] PiecesNear(double z) => buckets.GetOrAdd((long)Math.Floor(z / BucketSize), BuildBucket);

        private StreamPiece[] BuildBucket(long key)
        {
            double z0 = key * BucketSize, z1 = z0 + BucketSize;
            double sA = road.S(z0 - 800.0), sB = road.S(z1 + 800.0);
            var list = new List<StreamPiece>();
            var all = new List<Crossing>();
            plan.Crossings.Crossings(sA - 60.0, sB + 60.0, all);
            foreach (Crossing c in all)
            {
                if (!Accepted(c)) continue;
                StreamCourse course = Course(c);
                if (!course.Accepted) continue;
                foreach (StreamPiece piece in course.Pieces)
                {
                    if (piece.MaxZ >= z0 && piece.MinZ <= z1) list.Add(piece);
                }
            }

            return list.Count == 0 ? Array.Empty<StreamPiece>() : list.ToArray();
        }

        /// <summary>Appends the accepted streams whose points come within <paramref name="margin"/> of the square [x0, x0 + size] × [z0, z0 + size], each once.</summary>
        public void Overlapping(double x0, double z0, double size, double margin, List<StreamCourse> results)
        {
            var seen = new HashSet<int>();
            for (long k = (long)Math.Floor((z0 - margin) / BucketSize); k <= (long)Math.Floor((z0 + size + margin) / BucketSize); k++)
            {
                foreach (StreamPiece piece in PiecesNear(k * BucketSize + 1.0))
                {
                    StreamCourse c = piece.Course;
                    if (seen.Contains(c.Crossing.Id)) continue;
                    if (c.MaxX < x0 - margin || c.MinX > x0 + size + margin || c.MaxZ < z0 - margin || c.MinZ > z0 + size + margin) continue;
                    seen.Add(c.Crossing.Id);
                    results.Add(c);
                }
            }

            results.Sort((a, b) => a.Crossing.S.CompareTo(b.Crossing.S));
        }

        /// <summary>
        /// The least distance from (x, z) to the water's edge of any stream within <paramref name="radius"/> (negative inside the
        /// water), or +∞ when none is that close.
        /// </summary>
        public float Clearance(double x, double z, double radius)
        {
            var near = new List<StreamCourse>();
            Overlapping(x - radius, z - radius, 2.0 * radius, 12.0, near);
            float best = float.PositiveInfinity;
            foreach (StreamCourse c in near)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    double dx = x - c.X[i], dz = z - c.Z[i];
                    float d = (float)Math.Sqrt(dx * dx + dz * dz) - c.HalfRibbon[i];
                    if (d < best) best = d;
                }
            }

            return best;
        }

        /// <summary>The nearest point of any stream of <paramref name="pieces"/> to (x, z) within its pieces' reach (Course null when none).</summary>
        public static StreamSample Nearest(StreamPiece[] pieces, double x, double z)
        {
            var best = new StreamSample { Distance = float.MaxValue };
            for (int k = 0; k < pieces.Length; k++)
            {
                StreamPiece piece = pieces[k];
                if (x < piece.MinX || x > piece.MaxX || z < piece.MinZ || z > piece.MaxZ) continue;
                Sample(piece, x, z, ref best);
            }

            return best;
        }

        /// <summary>Updates <paramref name="best"/> with the nearest point of <paramref name="piece"/>'s segments if nearer.</summary>
        private static void Sample(StreamPiece piece, double x, double z, ref StreamSample best)
        {
            StreamCourse c = piece.Course;
            for (int i = piece.First; i < piece.Last; i++)
            {
                double ax = c.X[i], az = c.Z[i], bx = c.X[i + 1], bz = c.Z[i + 1];
                double ex = bx - ax, ez = bz - az;
                double len2 = ex * ex + ez * ez;
                double t = len2 > 1e-9 ? ((x - ax) * ex + (z - az) * ez) / len2 : 0.0;
                if (t < 0.0) t = 0.0;
                else if (t > 1.0) t = 1.0;
                double dx = x - (ax + ex * t), dz = z - (az + ez * t);
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d >= best.Distance) continue;
                float f = (float)t;
                best.Course = c;
                best.Distance = (float)d;
                best.Bed = c.Bed[i] + (c.Bed[i + 1] - c.Bed[i]) * f;
                best.Surface = c.Surface[i] + (c.Surface[i + 1] - c.Surface[i]) * f;
                best.Width = c.Width[i] + (c.Width[i + 1] - c.Width[i]) * f;
                best.HalfWater = c.HalfWater[i] + (c.HalfWater[i + 1] - c.HalfWater[i]) * f;
                best.HalfRibbon = c.HalfRibbon[i] + (c.HalfRibbon[i + 1] - c.HalfRibbon[i]) * f;
                best.Along = c.Along[i] + (c.Along[i + 1] - c.Along[i]) * f;
                best.FromCrossing = best.Along - c.Along[c.CrossingIndex];
                best.Outer = c.Outer == null ? 0f : c.Outer[i] + (c.Outer[i + 1] - c.Outer[i]) * f;
                best.BankTop = c.BankTop == null ? 0f : c.BankTop[i] + (c.BankTop[i + 1] - c.BankTop[i]) * f;
                double len = Math.Sqrt(len2);
                best.Reach = len > 1e-9 ? (float)(2.0 * (Math.Abs(ex) + Math.Abs(ez)) / len) + ReachSlack : MaxReach;
            }
        }

        /// <summary>Height of a bank <paramref name="beyond"/> metres out from the edge of the flat bed, above the bed.</summary>
        public static float Bank(float beyond)
        {
            if (beyond <= 0f) return 0f;
            if (beyond <= GentleBank) return GentleSlope * beyond;
            return GentleSlope * GentleBank + BankSlope * (beyond - GentleBank);
        }

        /// <summary>Distance beyond the edge of the bed at which the bank reaches <paramref name="rise"/> above the bed.</summary>
        public static float BankInverse(float rise)
        {
            if (rise <= 0f) return 0f;
            float gentle = GentleSlope * GentleBank;
            if (rise <= gentle) return rise / GentleSlope;
            return GentleBank + (rise - gentle) / BankSlope;
        }

        /// <summary>
        /// Carves the streams of <paramref name="pieces"/> into ground height <paramref name="h"/> at (x, z), which is taken to be a
        /// sample of a 2 m height map: the channel (flat bed and banks) where it is lower, and, away from the road crossing, the
        /// ground kept below the drawn stream: a sample is lowered to <see cref="LowestDrawn"/> at the nearest point it can shape
        /// (<see cref="StreamSample.Reach"/> nearer the centre line) minus <see cref="LookMargin"/>, and to its own height minus
        /// <see cref="LookMargin"/> anywhere it can shape the drawn stream (so the drape over the ground floats just above it).
        /// Near the road crossing (where the road shapes the ground and no bank is drawn) a levee 8 cm above the water holds it
        /// where the ground beside it is lower. Returns the new height; <paramref name="bridge"/> receives the height to cut the ground to again after the road bed is levelled: the channel where
        /// a bridge's span lies over it, and the carving under the drawn stream outside the road's part (the road's fill slopes must not lift the ground back through it); else +∞.
        /// </summary>
        public static float Carve(StreamPiece[] pieces, double x, double z, float h, out float bridge)
        {
            bridge = float.PositiveInfinity;
            float levee = float.NegativeInfinity, cut = float.PositiveInfinity;
            StreamCourse last = null;
            var best = new StreamSample();
            for (int k = 0; k < pieces.Length; k++)
            {
                StreamPiece piece = pieces[k];
                if (x < piece.MinX || x > piece.MaxX || z < piece.MinZ || z > piece.MaxZ) continue;
                if (piece.Course != last)
                {
                    if (last != null) Apply(ref best, h, ref levee, ref cut, ref bridge);
                    best = new StreamSample { Distance = float.MaxValue };
                    last = piece.Course;
                }

                Sample(piece, x, z, ref best);
            }

            if (last != null) Apply(ref best, h, ref levee, ref cut, ref bridge);
            if (levee > h) h = levee;
            if (cut < h) h = cut;
            return h;
        }

        /// <summary>
        /// The channels alone at (x, z): the lowest flat bed and banks (<see cref="Bank"/>) of the streams of <paramref name="pieces"/>
        /// there, +∞ where none reaches. The drawn banks are draped over the ground cut by this (and the ground is kept just below).
        /// </summary>
        public static float ChannelCut(StreamPiece[] pieces, double x, double z)
        {
            float cut = float.PositiveInfinity;
            StreamCourse last = null;
            var best = new StreamSample();
            for (int k = 0; k <= pieces.Length; k++)
            {
                StreamPiece piece = k < pieces.Length ? pieces[k] : null;
                if (piece != null && (x < piece.MinX || x > piece.MaxX || z < piece.MinZ || z > piece.MaxZ)) continue;
                if (piece == null || piece.Course != last)
                {
                    if (last != null && best.Course != null) cut = Math.Min(cut, best.Bed + Bank(best.Distance - best.Width * 0.5f));
                    if (piece == null) break;
                    best = new StreamSample { Distance = float.MaxValue };
                    last = piece.Course;
                }

                Sample(piece, x, z, ref best);
            }

            return cut;
        }

        /// <summary>True when the sample lies in the road's part of the stream (no bank is drawn there; the road shapes the ground).</summary>
        public static bool InRoadZone(StreamCourse c, float fromCrossing) => Math.Abs(fromCrossing) <= c.RoadZone;

        private static void Apply(ref StreamSample s, float h, ref float levee, ref float cut, ref float bridge)
        {
            if (s.Course == null) return;
            float beyond = s.Distance - s.Width * 0.5f;
            float carve = s.Bed + Bank(beyond);
            if (InRoadZone(s.Course, s.FromCrossing))
            {
                if (s.Distance >= s.HalfWater && s.Distance <= s.HalfRibbon + 3.5f)
                {
                    float l = s.Surface + 0.08f - 0.5f * Math.Max(0f, s.Distance - s.HalfRibbon - 1.5f);
                    if (l > levee) levee = l;
                }
            }
            else if (s.Distance <= s.Outer + s.Reach)
            {
                // Below the drawn stream: below the channelled ground itself (ChannelCut) where the mesh is draped over it, and, where
                // this sample shapes the water or the rising bank, below what they are at the nearest point it shapes.
                float nearest = Math.Max(0f, s.Distance - s.Reach);
                carve = Math.Min(Math.Min(carve, h), LowestDrawn(nearest, s.HalfWater, s.Surface, s.BankTop)) - LookMargin;
                // Cut again after the road bed is benched in: the road's fill slopes beside a ford must not lift the ground back
                // through the drawn stream (the road bed itself lies in the road's part of the stream, which is never cut again).
                if (carve < bridge) bridge = carve;
            }

            if (carve < cut) cut = carve;
            if (s.Course.Crossing.Kind == CrossingKind.Bridge && Math.Abs(s.FromCrossing) <= BridgeSpanHalf && carve < bridge) bridge = carve;
        }

        // ------------------------------------------------------------------ building a course

        private sealed class Walked
        {
            public readonly List<double> X = new List<double>(), Z = new List<double>();
            public readonly List<float> G = new List<float>();
            public bool Lake;
        }

        private StreamCourse Reject(Crossing c, string reason) => new StreamCourse { Crossing = c, Accepted = false, Reason = reason };

        private StreamCourse BuildRaw(Crossing c)
        {
            if (c == null) return new StreamCourse { Accepted = false, Reason = "no crossing" };
            double zc = road.ZAt(c.S);
            double xc = road.X(zc, out double slope, out _);
            double len = Math.Sqrt(1.0 + slope * slope);
            double nx = 1.0 / len, nz = -slope / len;   // right-hand normal of the road
            float hRoad = profile.BaseHeight(c.S);

            // The downstream side is the one whose ground lies lower.
            double right = 0.0, left = 0.0;
            foreach (double d in new[] { 35.0, 70.0, 110.0, 160.0 })
            {
                right += field.BaseHeight(xc + nx * d, zc + nz * d);
                left += field.BaseHeight(xc - nx * d, zc - nz * d);
            }

            right *= 0.25;
            left *= 0.25;
            int down = right < left ? 1 : -1;
            if (Math.Abs(right - left) < 0.4) down = EndlessNoise.Unit(c.Seed, 1040, 0) < 0.5 ? 1 : -1;
            double low = Math.Min(right, left);
            // In a valley (both sides rise from the road) the stream comes down one side, crosses and turns down the valley
            // along the foot of the other side: it may run beside the road there instead of moving away from it.
            bool valley = low > hRoad + 1.2;
            if (valley && c.RegionKind != RegionKind.HighlandPass && c.RegionKind != RegionKind.Downs && c.RegionKind != RegionKind.Forest)
            {
                return Reject(c, "the road lies in a hollow");
            }

            Walked up = Walk(c, xc, zc, -nx * down, -nz * down, false, valley);
            Walked dn = Walk(c, xc, zc, nx * down, nz * down, true, valley);

            // Source first: the upstream walk reversed, the crossing, the downstream walk.
            var xs = new List<double>();
            var zs = new List<double>();
            var gs = new List<float>();
            for (int i = up.X.Count - 1; i >= 0; i--)
            {
                xs.Add(up.X[i]);
                zs.Add(up.Z[i]);
                gs.Add(up.G[i]);
            }

            int ic = xs.Count;
            xs.Add(xc);
            zs.Add(zc);
            gs.Add(hRoad);
            for (int i = 0; i < dn.X.Count; i++)
            {
                xs.Add(dn.X[i]);
                zs.Add(dn.Z[i]);
                gs.Add(dn.G[i]);
            }

            StreamCourse course = Shape(c, xs, zs, gs, ic, dn.Lake, hRoad, out string reason);
            return course ?? Reject(c, reason);
        }

        /// <summary>Walks from the crossing away from the road: down the fall line (downstream) or up it (towards the source).</summary>
        private Walked Walk(Crossing c, double x0, double z0, double dx, double dz, bool downstream, bool valley)
        {
            var w = new Walked();
            double maxLength = downstream ? c.DownLength : c.UpLength;
            double straight = p.roadWidth * 0.5 + 9.0;
            double px = x0, pz = z0, along = 0.0;
            double heading = Math.Atan2(dx, dz);
            double phase = c.MeanderPhase + (downstream ? 0.0 : 1.7);
            while (along < maxLength)
            {
                if (along >= straight)
                {
                    TerrainField.Row row = field.BaseRow(pz);
                    float g0 = field.BaseHeight(row, px);
                    float gxp = field.BaseHeight(row, px + 2.5);
                    float gzp = field.BaseHeight(px, pz + 2.5);
                    double gx = (gxp - g0) / 2.5, gz = (gzp - g0) / 2.5;
                    double fx = downstream ? -gx : gx, fz = downstream ? -gz : gz;
                    double fl = Math.Sqrt(fx * fx + fz * fz);
                    double wx = Math.Sin(heading), wz = Math.Cos(heading);
                    if (fl > 0.004)
                    {
                        double weight = Math.Min(0.75, fl * 6.0);
                        wx += fx / fl * weight;
                        wz += fz / fl * weight;
                    }

                    double fall = Math.Atan2(wx, wz);
                    double ramp = EndlessNoise.Smooth(straight, straight + 40.0, along);
                    double desired = fall + ramp * c.MeanderAmplitude * Math.Sin(2.0 * Math.PI * along / c.MeanderLength + phase);

                    // Never back towards the road: within 40 m keep moving away from it, beyond that at least not towards it.
                    double zeta = road.Nearest(px, pz, out double dist);
                    if (dist > 1e-3)
                    {
                        double ax = (px - road.X(zeta)) / dist, az = (pz - zeta) / dist;
                        double minDot = valley && downstream ? (dist < 16.0 ? 0.3 : -0.05) : dist < 40.0 ? 0.4 : -0.05;
                        double ox = Math.Sin(desired), oz = Math.Cos(desired);
                        if (ox * ax + oz * az < minDot)
                        {
                            double away = Math.Atan2(ax, az);
                            double offset = Wrap(desired - away);
                            double limit = Math.Acos(minDot);
                            desired = away + Math.Sign(offset) * limit;
                        }
                    }

                    double turn = Wrap(desired - heading);
                    double maxTurn = valley && downstream ? 0.2 : 0.13;
                    heading += Math.Max(-maxTurn, Math.Min(maxTurn, turn));
                }

                px += Math.Sin(heading) * PointSpacing;
                pz += Math.Cos(heading) * PointSpacing;
                along += PointSpacing;

                TerrainField.Row at = field.BaseRow(pz);
                float g = field.BaseHeight(at, px);
                if (along > straight + 3.0)
                {
                    road.Nearest(px, pz, out double back);
                    if (back < 12.0) break;
                }

                bool stop = false;
                foreach (SettlementDisc disc in at.Discs)
                {
                    double ddx = px - disc.X, ddz = pz - disc.Z;
                    double reach = disc.Place.Radius + TerrainShaper.SettlementBlend + 12.0;
                    if (ddx * ddx + ddz * ddz < reach * reach) stop = true;
                }

                if (stop) break;
                bool lake = false;
                foreach (LakeInfo l in at.Lakes)
                {
                    double ddx = px - l.CentreX, ddz = pz - l.CentreZ;
                    if (ddx * ddx + ddz * ddz < (l.Radius + 30.0) * (l.Radius + 30.0) && g < l.Level + 0.25f) lake = true;
                }

                if (lake)
                {
                    if (downstream)
                    {
                        w.X.Add(px);
                        w.Z.Add(pz);
                        w.G.Add(g);
                        w.Lake = true;
                    }

                    break;
                }

                if (!downstream && w.X.Count > 3)
                {
                    float rise = (g - w.G[w.G.Count - 1]) / (float)PointSpacing;
                    if (rise > 1.0f) break;   // a crag: the stream springs below it
                }

                w.X.Add(px);
                w.Z.Add(pz);
                w.G.Add(g);
            }

            return w;
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2.0 * Math.PI;
            while (a < -Math.PI) a += 2.0 * Math.PI;
            return a;
        }

        /// <summary>
        /// Builds the channel along the points (source first, the road crossing at <paramref name="ic"/>): widths, depths, bed,
        /// water, ribbon, then shortens it where the water would stand above the ground and checks it; null (with a reason) when
        /// the crossing cannot have a stream.
        /// </summary>
        private StreamCourse Shape(Crossing c, List<double> xs, List<double> zs, List<float> gs, int ic, bool lake, float hRoad, out string reason)
        {
            reason = null;
            int first = 0, last = xs.Count - 1;
            float roadHalf = p.roadWidth * 0.5f;
            string why = "";
            for (int attempt = 0; attempt < 7; attempt++)
            {
                StreamCourse course = Channel(c, xs, zs, gs, first, last, ic, lake && last == xs.Count - 1, hRoad);
                int n = course.Count;
                int cIndex = course.CrossingIndex;
                // Upstream: the nearest point to the road where the water would stand at or above the ground (a hollow) or a
                // bank falls away below it: the source moves just below it.
                int newFirst = first;
                for (int i = cIndex - 1; i >= 0; i--)
                {
                    float fromRoad = course.Along[cIndex] - course.Along[i];
                    if (fromRoad <= roadHalf + 3f) continue;
                    // Beside the road the ground may dip below a ford's bed (a ditch, the foot of a fill): the water simply stands
                    // deeper there; further up such a hollow ends the stream's course (it springs below it).
                    bool hollow = fromRoad > 25f && course.Surface[i] > course.Ground[i] - 0.05f;
                    if (hollow || BankFalls(course, i))
                    {
                        newFirst = first + i + 1;
                        why = $"up i{i} ic{cIndex} W{course.Surface[i]:0.00} G{course.Ground[i]:0.00} bank{BankFalls(course, i)}";
                        break;
                    }
                }

                // Downstream: the stream ends before it would have to cut too deep or its bank falls away.
                int newLast = last;
                for (int i = cIndex + 1; i < n; i++)
                {
                    float fromRoad = course.Along[i] - course.Along[cIndex];
                    if (fromRoad <= roadHalf + 3f) continue;
                    bool inLake = course.EndsInLake && i >= n - 2;
                    float depth = course.Ground[i] - course.Bed[i];
                    if (!inLake && (depth > c.Depth + MaxExtraCut || BankFalls(course, i)))
                    {
                        newLast = first + i - 1;
                        why = $"down i{i} ic{cIndex} cut{depth:0.00} bank{BankFalls(course, i)}";
                        break;
                    }
                }

                if (newFirst == first && newLast == last)
                {
                    float upLength = course.Along[cIndex] - course.Along[0];
                    float downLength = course.Along[n - 1] - course.Along[cIndex];
                    if (upLength < roadHalf + 30f) reason = "too short upstream";
                    else if (downLength < roadHalf + 30f) reason = "too short downstream";
                    else if (c.Kind == CrossingKind.Bridge && hRoad - course.Surface[cIndex] < BridgeClearance - 0.01f) reason = "no clearance under the bridge";
                    else if (c.Kind == CrossingKind.Bridge && hRoad - course.Bed[cIndex] > BridgeMaxDepth + 0.25f) reason = "too deep under the bridge";
                    if (reason != null) return null;
                    course.Accepted = true;
                    Look(course);
                    return course;
                }

                first = newFirst;
                last = newLast;
                if (ic - first < 4 || last - ic < 4)
                {
                    reason = "the terrain does not hold a stream " + why;
                    return null;
                }
            }

            reason = "did not settle";
            return null;
        }

        /// <summary>True when the ground beside the water at point <paramref name="i"/> lies more than 0.7 m below the water (a levee would have to hold it).</summary>
        private bool BankFalls(StreamCourse course, int i)
        {
            double rx = course.DirZ[i], rz = -course.DirX[i];
            double r = course.HalfRibbon[i] + 1.2;
            float a = field.BaseHeight(course.X[i] + rx * r, course.Z[i] + rz * r);
            float b = field.BaseHeight(course.X[i] - rx * r, course.Z[i] - rz * r);
            return Math.Min(a, b) < course.Surface[i] - 0.7f;
        }

        /// <summary>Widths, depths, bed (running minimum), water, ribbon and directions for points [first, last].</summary>
        private StreamCourse Channel(Crossing c, List<double> xs, List<double> zs, List<float> gs, int first, int last, int icAll, bool lake, float hRoad)
        {
            int n = last - first + 1;
            int ic = icAll - first;
            var course = new StreamCourse
            {
                Crossing = c,
                Count = n,
                CrossingIndex = ic,
                EndsInLake = lake,
                X = new double[n],
                Z = new double[n],
                Along = new float[n],
                Bed = new float[n],
                Surface = new float[n],
                Width = new float[n],
                HalfWater = new float[n],
                HalfRibbon = new float[n],
                DirX = new float[n],
                DirZ = new float[n],
                Ground = new float[n],
                BankTop = new float[n],
                Outer = new float[n],
                Riffle = new float[n],
                // A bridge: only under its own width (the drawn stream and its banks run up to the faces of the arch).
                RoadZone = c.Kind == CrossingKind.Ford ? p.roadWidth * 0.5f + 0.6f : BridgeShape.HalfWidth + 1.4f,
            };

            for (int i = 0; i < n; i++)
            {
                course.X[i] = xs[first + i];
                course.Z[i] = zs[first + i];
                course.Ground[i] = gs[first + i];
                if (i > 0)
                {
                    double dx = course.X[i] - course.X[i - 1], dz = course.Z[i] - course.Z[i - 1];
                    course.Along[i] = course.Along[i - 1] + (float)Math.Sqrt(dx * dx + dz * dz);
                }
            }

            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                double dx = course.X[b] - course.X[a], dz = course.Z[b] - course.Z[a];
                double l = Math.Sqrt(dx * dx + dz * dz);
                course.DirX[i] = l > 1e-9 ? (float)(dx / l) : 0f;
                course.DirZ[i] = l > 1e-9 ? (float)(dz / l) : 1f;
            }

            float total = course.Along[n - 1];
            float roadHalf = p.roadWidth * 0.5f;
            float atCrossing = course.Along[ic];
            bool ford = c.Kind == CrossingKind.Ford;
            float hFord = hRoad - StreamPlan.FordDip;
            float bridgeDepth = Math.Min(BridgeMaxDepth, Math.Max(c.WaterDepth + BridgeClearance + 0.15f, Math.Max(BridgeMinDepth, c.Depth + 0.2f)));
            // Below the arch the water spreads into a pool, wider for a larger stream.
            float pool = 0.35f + 0.35f * Math.Min(1f, Math.Max(0f, (c.BedWidth - 2f) / 3f));
            var depth = new float[n];
            var water = new float[n];
            float run = float.PositiveInfinity;
            // Pools and riffles: the width swells and narrows along the stream (two slow waves of the crossing's own), never near the road.
            double poolLength = 26.0 + 18.0 * EndlessNoise.Unit(c.Seed, 1151, 0), rippleLength = 9.0 + 6.0 * EndlessNoise.Unit(c.Seed, 1152, 0);
            double poolPhase = 2.0 * Math.PI * EndlessNoise.Unit(c.Seed, 1153, 0), ripplePhase = 2.0 * Math.PI * EndlessNoise.Unit(c.Seed, 1154, 0);
            for (int i = 0; i < n; i++)
            {
                float a = course.Along[i];
                float fromCrossing = Math.Abs(a - atCrossing);
                float source = (float)EndlessNoise.Smooth(0.0, 75.0, a);
                float mouth = lake ? 1f : 1f - (float)EndlessNoise.Smooth(total - 50.0, total, a);
                float swell = (float)(0.13 * Math.Sin(2.0 * Math.PI * a / poolLength + poolPhase) + 0.06 * Math.Sin(2.0 * Math.PI * a / rippleLength + ripplePhase));
                swell *= (float)EndlessNoise.Smooth(roadHalf + 2.0, roadHalf + 14.0, fromCrossing);
                course.Riffle[i] = (float)EndlessNoise.Smooth(-0.04, -0.15, swell);
                float w = c.BedWidth * (1f + swell) * (0.55f + 0.45f * source) * (0.5f + 0.5f * mouth);
                float d = c.Depth * (0.35f + 0.65f * source) * (0.4f + 0.6f * mouth);
                float wd = c.WaterDepth * (0.6f + 0.4f * source) * (0.6f + 0.4f * mouth);
                if (ford)
                {
                    d *= (float)EndlessNoise.Smooth(roadHalf + 0.5, roadHalf + 12.0, fromCrossing);
                    wd = StreamPlan.FordWater + (wd - StreamPlan.FordWater) * (float)EndlessNoise.Smooth(roadHalf, roadHalf + 14.0, fromCrossing);
                }
                else
                {
                    // Narrow through the arch, widening again from the bridge's faces; a pool just below it.
                    float pinch = (float)EndlessNoise.Smooth(BridgeShape.HalfWidth + 0.5, BridgeShape.HalfWidth + 9.0, fromCrossing);
                    w = Math.Min(w, BridgeBedWidth + (w - BridgeBedWidth) * pinch);
                    if (i > ic)
                    {
                        double u = (fromCrossing - BridgeShape.HalfWidth) / 14.0;
                        if (u > 0.0 && u < 1.0) w *= 1f + pool * (float)Math.Pow(Math.Sin(Math.PI * u), 2.0);
                    }
                    d = d + (bridgeDepth - d) * (1f - (float)EndlessNoise.Smooth(BridgeSpanHalf, BridgeSpanHalf + 10.0, fromCrossing));
                }

                course.Width[i] = Math.Min(6f, Math.Max(0.8f, w));
                depth[i] = d;
                water[i] = wd;

                float target = course.Ground[i] - d;
                if (ford && fromCrossing <= roadHalf + 0.5f) target = hFord;
                if (!ford && fromCrossing <= BridgeSpanHalf) target = Math.Min(target, hRoad - bridgeDepth);
                run = Math.Min(run, target);
                float bed = run;
                if (ford)
                {
                    if (i < ic) bed = Math.Max(run, hFord);
                    else if (i == ic) run = bed = hFord;
                }

                course.Bed[i] = bed;
            }

            float surface = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                surface = Math.Min(surface, course.Bed[i] + water[i]);
                surface = Math.Max(surface, course.Bed[i] + 0.03f);
                course.Surface[i] = surface;
                float halfWater = course.Width[i] * 0.5f + BankInverse(surface - course.Bed[i]);
                course.HalfWater[i] = halfWater;
                float ribbon = halfWater + 0.55f;
                if (ford && Math.Abs(course.Along[i] - atCrossing) <= roadHalf + 1f) ribbon = Math.Max(ribbon, FordWaterHalf + 0.3f);
                course.HalfRibbon[i] = ribbon;
            }

            Bounds(course);
            return course;
        }

        /// <summary>Largest <see cref="StreamCourse.BankTop"/>, metres.</summary>
        public const float MaxBankTop = 4.5f;

        /// <summary>
        /// The drawn stream's extent along an accepted course: on each side, where the rising bank (<see cref="BankRise"/>) first
        /// reaches the ground as the channel leaves it (the ground before the streams, cut by this channel's banks), scanning
        /// outward every half metre; the larger side, at least 0.4 m and at most <see cref="MaxBankTop"/>. Then the bounds again.
        /// </summary>
        private void Look(StreamCourse course)
        {
            for (int i = 0; i < course.Count; i++)
            {
                double rx = course.DirZ[i], rz = -course.DirX[i];
                float half = course.HalfWater[i], surface = course.Surface[i];
                float top = 0.4f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = 0.5f;
                    for (; x < MaxBankTop; x += 0.5f)
                    {
                        double u = (half + x) * side;
                        float ground = field.BaseHeight(course.X[i] + rx * u, course.Z[i] + rz * u);
                        float channel = course.Bed[i] + Bank(half + x - course.Width[i] * 0.5f);
                        if (surface + BankRise(x) >= Math.Min(ground, channel) - 0.02f) break;
                    }

                    top = Math.Max(top, Math.Min(MaxBankTop, x));
                }

                course.BankTop[i] = top;
                course.Outer[i] = half + top + DrapeWidth;
            }

            Bounds(course);
        }

        private static void Bounds(StreamCourse course)
        {
            int n = course.Count;
            course.MinX = course.MinZ = double.PositiveInfinity;
            course.MaxX = course.MaxZ = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                course.MinX = Math.Min(course.MinX, course.X[i]);
                course.MaxX = Math.Max(course.MaxX, course.X[i]);
                course.MinZ = Math.Min(course.MinZ, course.Z[i]);
                course.MaxZ = Math.Max(course.MaxZ, course.Z[i]);
            }

            var pieces = new List<StreamPiece>();
            for (int a = 0; a < n - 1; a += PiecePoints)
            {
                int b = Math.Min(n - 1, a + PiecePoints);
                var piece = new StreamPiece { Course = course, First = a, Last = b, MinX = double.PositiveInfinity, MinZ = double.PositiveInfinity, MaxX = double.NegativeInfinity, MaxZ = double.NegativeInfinity };
                float reach = 0f;
                for (int i = a; i <= b; i++)
                {
                    piece.MinX = Math.Min(piece.MinX, course.X[i]);
                    piece.MaxX = Math.Max(piece.MaxX, course.X[i]);
                    piece.MinZ = Math.Min(piece.MinZ, course.Z[i]);
                    piece.MaxZ = Math.Max(piece.MaxZ, course.Z[i]);
                    reach = Math.Max(reach, Math.Max(course.Width[i] * 0.5f + CarveReach, course.HalfRibbon[i] + 4f));
                    if (course.Outer != null) reach = Math.Max(reach, course.Outer[i] + MaxReach + 1f);
                }

                piece.MinX -= reach;
                piece.MinZ -= reach;
                piece.MaxX += reach;
                piece.MaxZ += reach;
                pieces.Add(piece);
            }

            course.Pieces = pieces.ToArray();
        }

        /// <summary>
        /// The final course of cell <paramref name="cell"/>: the raw course cut where it comes near another stream's raw course
        /// (never within 40 m of its own road crossing), then shaped again from its new ends.
        /// </summary>
        private StreamCourse BuildFinal(long cell)
        {
            StreamCourse own = Raw(cell);
            if (!own.Accepted) return own;
            int n = own.Count;
            int first = 0, last = n - 1;
            float atCrossing = own.Along[own.CrossingIndex];
            for (long k = cell - 2; k <= cell + 2; k++)
            {
                if (k == cell) continue;
                StreamCourse other = Raw(k);
                if (!other.Accepted) continue;
                if (other.MaxX < own.MinX - 30.0 || other.MinX > own.MaxX + 30.0 || other.MaxZ < own.MinZ - 30.0 || other.MinZ > own.MaxZ + 30.0) continue;
                bool laterJoins = k < cell;   // the later stream joins the earlier one where they meet
                for (int i = 0; i < n; i++)
                {
                    if (Math.Abs(own.Along[i] - atCrossing) < 40f) continue;
                    bool meets = false;
                    for (int j = 0; j < other.Count && !meets; j++)
                    {
                        double dx = own.X[i] - other.X[j], dz = own.Z[i] - other.Z[j];
                        double reach = own.HalfRibbon[i] + other.HalfRibbon[j] + 6.0;
                        if (dx * dx + dz * dz < reach * reach) meets = true;
                    }

                    if (!meets) continue;
                    if (i < own.CrossingIndex) first = Math.Max(first, i + 1);
                    else if (i > own.CrossingIndex) last = Math.Min(last, laterJoins ? i : i - 1);
                }
            }

            if (first == 0 && last == n - 1) return own;
            var xs = new List<double>();
            var zs = new List<double>();
            var gs = new List<float>();
            for (int i = 0; i < n; i++)
            {
                xs.Add(own.X[i]);
                zs.Add(own.Z[i]);
                gs.Add(own.Ground[i]);
            }

            StreamCourse cut = Channel(own.Crossing, xs, zs, gs, first, last, own.CrossingIndex, own.EndsInLake && last == n - 1, profile.BaseHeight(own.Crossing.S));
            cut.Accepted = true;
            Look(cut);
            return cut;
        }
    }

    /// <summary>
    /// The Fantasy Kingdom stone bridge (<c>SM_Env_Bridge_Stone_01</c>) as the endless road places it, measured from its FBX
    /// (2026-10-06): 20 m long along its z (the road), 5.68 m wide (a 4.6 m deck between 0.54 m parapets), from 3.80 m below
    /// its pivot (the piers' feet) to 3.15 m above (the parapets' crown). The deck is a hump: 0.24 m above the pivot at its ends
    /// (|z| ≥ 8 m), 1.92 m at the crown, 47 % at its steepest (|z| 2.2-3.2 m). One arch, 4.8 m wide (|z| &lt; 2.4 m): its soffit is
    /// 1.24 m above the pivot at the crown and its sides drop straight to the piers' feet. The palette material is a flat colour
    /// atlas, so a stretch along the road reads as a longer, gentler bridge with a wider arch (no texture distortion). Pure.
    /// </summary>
    public static class BridgeShape
    {
        /// <summary>
        /// Scale of the placed bridge across the road, up, and along the road. Across (2026-10-06, was 1.12): wide enough that the
        /// mesh's own parapet (inner face at least 2.163 m from the axis, unscaled) lies outside <see cref="BridgeWalls.InnerFace"/>,
        /// so the first-party parapet encloses it and the full 5 m road plus 0.3 m a side stays clear between the walls.
        /// </summary>
        public const float ScaleX = 1.31f, ScaleY = 0.85f, ScaleZ = 1.35f;

        /// <summary>Least distance of the mesh's own parapet (its inner face) from the bridge's axis, unscaled, measured from the FBX.</summary>
        public const float MeshParapetInner = 2.163f;

        /// <summary>Highest point of the mesh's own parapet above <see cref="DeckHeight"/> (unscaled), measured from the FBX.</summary>
        public const float MeshParapetRise = 1.332f;

        /// <summary>Number of measured stations of the deck's hump (from the middle outwards).</summary>
        public static int DeckKnots => DeckZ.Length;

        /// <summary>Distance from the middle (placed, metres) of the deck's measured station <paramref name="i"/>.</summary>
        public static float DeckKnot(int i) => DeckZ[i] * ScaleZ;

        /// <summary>Measured mesh (unscaled): half length, half width, deck end and crown, soffit at the crown, half width of the arch.</summary>
        public const float MeshHalfLength = 10f, MeshHalfWidth = 2.84f, MeshDeckEnd = 0.24f, MeshDeckCrown = 1.92f, MeshSoffitCrown = 1.24f, MeshArchHalf = 2.4f;

        /// <summary>Placed half length along the road, half width across it, half width of the arch (along the road), metres.</summary>
        public const float HalfLength = MeshHalfLength * ScaleZ, HalfWidth = MeshHalfWidth * ScaleX, ArchHalf = MeshArchHalf * ScaleZ;

        /// <summary>The pivot sits this far below the road so the deck's flat ends meet the road flush, metres.</summary>
        public const float Sink = MeshDeckEnd * ScaleY;

        /// <summary>Height of the pivot for a road at <paramref name="roadHeight"/>.</summary>
        public static float PivotY(float roadHeight) => roadHeight - Sink;

        // Deck top on the centre line (unscaled), every half metre or so from the middle.
        private static readonly float[] DeckZ = { 0f, 0.7f, 1.2f, 1.7f, 2.2f, 2.7f, 3.2f, 3.7f, 4.2f, 4.7f, 5.2f, 5.7f, 6.2f, 6.7f, 7.2f, 7.7f, 8.2f, 10f };
        private static readonly float[] DeckY = { 1.92f, 1.92f, 1.83f, 1.72f, 1.61f, 1.38f, 1.14f, 0.92f, 0.76f, 0.60f, 0.46f, 0.41f, 0.37f, 0.31f, 0.26f, 0.26f, 0.25f, 0.24f };
        // Arch soffit (unscaled) from the crown to the spring.
        private static readonly float[] SoffitZ = { 0f, 1f, 1.5f, 1.8f, 2f, 2.3f, 2.4f };
        private static readonly float[] SoffitY = { 1.24f, 1.14f, 0.96f, 0.68f, 0.40f, -0.18f, -0.6f };

        // The mesh's own parapet top (unscaled, above the pivot), sliced from the FBX every 0.25 m from the middle (2026-10-06, both
        // sides and both halves, each knot the extreme over ±0.13 m): its highest point (|x| 2.1-2.9), and the lowest of its coping's
        // top (|x| 2.3-2.8) once the narrow joints between its own coping stones (±0.12 m) are skipped. Its coping is stepped, so the
        // two differ by up to 0.29 m on the steep flanks. Its outer face is at |x| 2.839 (the bridge's side) from 0.68 below the deck
        // to the top, a recessed band at 2.706 from 0.25 to 0.9 above it; the spandrels below at 2.5; its inner face 2.163-2.272.
        private static readonly float[] ParapetHi =
        {
            3.154f, 3.154f, 3.154f, 3.154f, 3.128f, 3.070f, 3.007f, 2.949f, 2.886f, 2.828f, 2.765f, 2.693f, 2.588f, 2.490f, 2.384f, 2.287f, 2.181f, 2.093f, 2.019f, 1.950f, 1.875f,
            1.806f, 1.731f, 1.688f, 1.651f, 1.617f, 1.580f, 1.548f, 1.489f, 1.454f, 1.455f, 1.442f, 1.426f, 1.411f, 1.395f, 1.380f, 1.373f, 1.373f, 1.373f, 1.373f, 1.373f,
        };

        private static readonly float[] ParapetLo =
        {
            3.154f, 3.154f, 3.154f, 3.135f, 3.094f, 3.030f, 2.960f, 2.897f, 2.837f, 2.784f, 2.679f, 2.610f, 2.461f, 2.344f, 2.217f, 2.080f, 1.894f, 1.859f, 1.774f, 1.682f, 1.597f,
            1.505f, 1.434f, 1.418f, 1.404f, 1.383f, 1.372f, 1.349f, 1.328f, 1.325f, 1.333f, 1.341f, 1.349f, 1.357f, 1.366f, 1.370f, 1.373f, 1.373f, 1.373f, 1.373f, 1.373f,
        };

        /// <summary>Spacing of the mesh's parapet-top knots (unscaled), metres.</summary>
        public const float ParapetKnotStep = 0.25f;

        /// <summary>The mesh's own outer face (the bridge's side) from the axis, unscaled, measured from the FBX.</summary>
        public const float MeshParapetOuter = 2.839f;

        /// <summary>
        /// Highest point of the mesh's own parapet above the pivot (placed) near <paramref name="along"/>: the larger of the two
        /// measured knots around it, so a straight stone laid over it can be checked against it anywhere.
        /// </summary>
        public static float ParapetTopHigh(float along)
        {
            Knots(along, out int i, out _);
            return Math.Max(ParapetHi[i], ParapetHi[i + 1]) * ScaleY;
        }

        /// <summary>Lowest of the mesh's own parapet's coping top above the pivot (placed) near <paramref name="along"/> (the smaller knot).</summary>
        public static float ParapetTopLow(float along)
        {
            Knots(along, out int i, out _);
            return Math.Min(ParapetLo[i], ParapetLo[i + 1]) * ScaleY;
        }

        /// <summary>The mesh's own parapet's highest point above the pivot (placed) at <paramref name="along"/>, interpolated (a smooth guide).</summary>
        public static float ParapetTopGuide(float along)
        {
            Knots(along, out int i, out float t);
            return (ParapetHi[i] + (ParapetHi[i + 1] - ParapetHi[i]) * t) * ScaleY;
        }

        private static void Knots(float along, out int i, out float t)
        {
            float k = Math.Min(Math.Abs(along) / ScaleZ / ParapetKnotStep, ParapetHi.Length - 1.001f);
            i = (int)k;
            t = k - i;
        }

        /// <summary>Height of the deck above the pivot (placed, scaled) <paramref name="along"/> metres from the middle; NaN beyond its ends.</summary>
        public static float DeckHeight(float along)
        {
            float a = Math.Abs(along) / ScaleZ;
            return a > MeshHalfLength ? float.NaN : Interp(DeckZ, DeckY, a) * ScaleY;
        }

        /// <summary>Height of the arch's soffit above the pivot (placed) <paramref name="along"/> metres from the middle; NaN outside the arch.</summary>
        public static float SoffitHeight(float along)
        {
            float a = Math.Abs(along) / ScaleZ;
            return a > MeshArchHalf ? float.NaN : Interp(SoffitZ, SoffitY, a) * ScaleY;
        }

        /// <summary>Steepest grade of the placed deck (rise over run).</summary>
        public static float SteepestGrade
        {
            get
            {
                float worst = 0f;
                for (int i = 1; i < DeckZ.Length; i++) worst = Math.Max(worst, Math.Abs(DeckY[i] - DeckY[i - 1]) / (DeckZ[i] - DeckZ[i - 1]));
                return worst * ScaleY / ScaleZ;
            }
        }

        private static float Interp(float[] z, float[] y, float a)
        {
            for (int i = 1; i < z.Length; i++)
            {
                if (a <= z[i]) return y[i - 1] + (y[i] - y[i - 1]) * (a - z[i - 1]) / (z[i] - z[i - 1]);
            }

            return y[y.Length - 1];
        }
    }
}
