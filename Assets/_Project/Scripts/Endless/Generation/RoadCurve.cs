using System;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// The endless road in plan: the graph of a function x = R(z). Its heading θ(z) (radians from +Z towards +X) is a slow
    /// corridor swing (≈6 km wavelength) plus a faster meander (≈420 m, amplitude itself varying over ≈2.5 km), squashed by a
    /// tanh so |θ| stays below <see cref="MaxHeading"/> (57°): the road winds but never turns back, so every point has one
    /// place along the journey. R and the arc length S are integrated (Simpson, 4 m nodes) in fixed steps outward from z = 0
    /// (x = 0, s = 0 there) and cached; between nodes R and S are cubic Hermite curves with the exact slopes at the nodes.
    /// Thread-safe; results never depend on query order.
    /// </summary>
    public sealed class RoadCurve
    {
        /// <summary>Spacing of the integration nodes along z, metres.</summary>
        public const double Step = 4.0;

        /// <summary>Nodes per cached chunk.</summary>
        public const int ChunkNodes = 1024;

        /// <summary>Largest heading away from +Z, radians (57°).</summary>
        public const double MaxHeading = 1.0;

        private readonly GradientNoise corridor;
        private readonly GradientNoise meander;
        private readonly GradientNoise meanderAmp;
        private readonly OutwardTable<Chunk> table;

        private sealed class Chunk
        {
            public long First;         // index of the first node
            public double[] X;         // R at each node
            public double[] S;         // arc length at each node
            public double[] Theta;     // heading at each node
            public double[] Slope;     // tan θ
            public double[] Sec;       // 1 / cos θ
        }

        /// <summary>Creates the road plan for <paramref name="seed"/>.</summary>
        public RoadCurve(int seed)
        {
            corridor = new GradientNoise(EndlessNoise.Seed(seed, 101, 0));
            meander = new GradientNoise(EndlessNoise.Seed(seed, 102, 0));
            meanderAmp = new GradientNoise(EndlessNoise.Seed(seed, 103, 0));
            table = new OutwardTable<Chunk>(ComputeChunk);
        }

        /// <summary>Heading θ(z) in radians (0 = +Z, positive towards +X), a pure function of z.</summary>
        public double HeadingAt(double z)
        {
            // Standard deviations (before the squash): corridor ≈ 17°, meander ≈ 4°..22° depending on the stretch.
            double slow = 0.30 * EndlessNoise.Fbm1N(corridor, z, 1.0 / 5200.0, 2);
            double amp = 0.21 + 0.13 * EndlessNoise.Fbm1N(meanderAmp, z, 1.0 / 2300.0, 1);
            double fast = Math.Max(0.07, amp) * EndlessNoise.Fbm1N(meander, z, 1.0 / 430.0, 2);
            return MaxHeading * Math.Tanh((slow + fast) / MaxHeading);
        }

        private Chunk ComputeChunk(long c, Chunk neighbour)
        {
            var chunk = new Chunk
            {
                First = c * ChunkNodes,
                X = new double[ChunkNodes],
                S = new double[ChunkNodes],
                Theta = new double[ChunkNodes],
                Slope = new double[ChunkNodes],
                Sec = new double[ChunkNodes],
            };

            for (int j = 0; j < ChunkNodes; j++)
            {
                double theta = HeadingAt((chunk.First + j) * Step);
                chunk.Theta[j] = theta;
                chunk.Slope[j] = Math.Tan(theta);
                chunk.Sec[j] = 1.0 / Math.Cos(theta);
            }

            if (c >= 0)
            {
                double x, s;
                int start;
                if (neighbour == null)
                {
                    x = 0.0;
                    s = 0.0;
                    chunk.X[0] = 0.0;
                    chunk.S[0] = 0.0;
                    start = 1;
                }
                else
                {
                    x = neighbour.X[ChunkNodes - 1];
                    s = neighbour.S[ChunkNodes - 1];
                    start = 0;
                }

                for (int j = start; j < ChunkNodes; j++)
                {
                    long i = chunk.First + j;   // integrate node i - 1 -> i
                    Integrate(i - 1, out double dx, out double ds);
                    x += dx;
                    s += ds;
                    chunk.X[j] = x;
                    chunk.S[j] = s;
                }
            }
            else
            {
                // Backwards from the first node of the next chunk (towards 0).
                double x = neighbour.X[0];
                double s = neighbour.S[0];
                for (int j = ChunkNodes - 1; j >= 0; j--)
                {
                    long i = chunk.First + j;   // integrate node i -> i + 1, subtract
                    Integrate(i, out double dx, out double ds);
                    x -= dx;
                    s -= ds;
                    chunk.X[j] = x;
                    chunk.S[j] = s;
                }
            }

            return chunk;
        }

        /// <summary>Simpson integral of tan θ and sec θ over [node i, node i + 1].</summary>
        private void Integrate(long i, out double dx, out double ds)
        {
            double z0 = i * Step;
            double t0 = HeadingAt(z0), tm = HeadingAt(z0 + Step * 0.5), t1 = HeadingAt(z0 + Step);
            dx = Step / 6.0 * (Math.Tan(t0) + 4.0 * Math.Tan(tm) + Math.Tan(t1));
            ds = Step / 6.0 * (1.0 / Math.Cos(t0) + 4.0 / Math.Cos(tm) + 1.0 / Math.Cos(t1));
        }

        private Chunk ChunkOf(long node, out int j)
        {
            long c = EndlessNoise.FloorDiv(node, ChunkNodes);
            j = (int)(node - c * ChunkNodes);
            return table.Get(c);
        }

        /// <summary>Node index and fraction for absolute z.</summary>
        private static long NodeOf(double z, out double t)
        {
            double u = z / Step;
            double f = Math.Floor(u);
            t = u - f;
            return (long)f;
        }

        private void Pair(long node, out Chunk a, out int ja, out Chunk b, out int jb)
        {
            a = ChunkOf(node, out ja);
            if (ja + 1 < ChunkNodes)
            {
                b = a;
                jb = ja + 1;
            }
            else
            {
                b = ChunkOf(node + 1, out jb);
            }
        }

        /// <summary>Road centre x at absolute <paramref name="z"/>, with its first and second derivatives dx/dz.</summary>
        public double X(double z, out double slope, out double curvature)
        {
            long i = NodeOf(z, out double t);
            Pair(i, out Chunk a, out int ja, out Chunk b, out int jb);
            double p0 = a.X[ja], p1 = b.X[jb], m0 = a.Slope[ja] * Step, m1 = b.Slope[jb] * Step;
            double t2 = t * t, t3 = t2 * t;
            double h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t, h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
            double d00 = 6 * t2 - 6 * t, d10 = 3 * t2 - 4 * t + 1, d01 = -6 * t2 + 6 * t, d11 = 3 * t2 - 2 * t;
            double e00 = 12 * t - 6, e10 = 6 * t - 4, e01 = -12 * t + 6, e11 = 6 * t - 2;
            slope = (d00 * p0 + d10 * m0 + d01 * p1 + d11 * m1) / Step;
            curvature = (e00 * p0 + e10 * m0 + e01 * p1 + e11 * m1) / (Step * Step);
            return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
        }

        /// <summary>Road centre x at absolute <paramref name="z"/>.</summary>
        public double X(double z) => X(z, out _, out _);

        /// <summary>Arc length (road distance) of the road point at absolute <paramref name="z"/>.</summary>
        public double S(double z)
        {
            long i = NodeOf(z, out double t);
            Pair(i, out Chunk a, out int ja, out Chunk b, out int jb);
            return Hermite(a.S[ja], b.S[jb], a.Sec[ja] * Step, b.Sec[jb] * Step, t);
        }

        /// <summary>Heading at z, radians, interpolated between nodes (matches the Hermite curve to about 1e-4 rad).</summary>
        public double Heading(double z)
        {
            X(z, out double slope, out _);
            return Math.Atan(slope);
        }

        /// <summary>The absolute z of the road point at road distance <paramref name="s"/>.</summary>
        public double ZAt(double s)
        {
            // Locate the chunk: s/z lies between 1 and sec(MaxHeading).
            double secMax = 1.0 / Math.Cos(MaxHeading);
            long c;
            if (s >= 0)
            {
                long lo = (long)Math.Floor(s / secMax / (Step * ChunkNodes)) - 1;
                long hi = (long)Math.Floor(s / (Step * ChunkNodes)) + 1;
                c = FindChunk(Math.Max(0, lo), hi, s);
            }
            else
            {
                long lo = (long)Math.Floor(s / (Step * ChunkNodes)) - 1;
                long hi = (long)Math.Floor(s / secMax / (Step * ChunkNodes)) + 1;
                c = FindChunk(lo, Math.Min(0, hi), s);
            }

            Chunk chunk = table.Get(c);
            // Binary search the last node with S <= s.
            int a = 0, b = ChunkNodes - 1;
            if (s < chunk.S[0]) return chunk.First * Step;   // cannot happen after FindChunk
            while (a < b)
            {
                int m = (a + b + 1) >> 1;
                if (chunk.S[m] <= s) a = m;
                else b = m - 1;
            }

            long node = chunk.First + a;
            Pair(node, out Chunk ca, out int ja, out Chunk cb, out int jb);
            double s0 = ca.S[ja], s1 = cb.S[jb], m0 = ca.Sec[ja] * Step, m1 = cb.Sec[jb] * Step;
            double t = (s1 > s0) ? (s - s0) / (s1 - s0) : 0.0;
            for (int it = 0; it < 4; it++)
            {
                double f = Hermite(s0, s1, m0, m1, t) - s;
                double d = HermiteDerivative(s0, s1, m0, m1, t);
                if (d <= 1e-9) break;
                t = Math.Max(0.0, Math.Min(1.0, t - f / d));
            }

            return (node + t) * Step;
        }

        /// <summary>The chunk in [lo, hi] whose S range contains s (largest chunk whose first S ≤ s).</summary>
        private long FindChunk(long lo, long hi, double s)
        {
            while (lo < hi)
            {
                long m = EndlessNoise.FloorDiv(lo + hi + 1, 2);
                if (table.Get(m).S[0] <= s) lo = m;
                else hi = m - 1;
            }

            return lo;
        }

        /// <summary>
        /// The road point nearest to absolute (x, z) by Newton iteration on the graph x = R(ζ) from ζ = z (a pure function of
        /// the point): its z, and the horizontal distance to it. Accurate where the point is closer to the road than the
        /// road's radius of curvature (always true within the road corridor).
        /// </summary>
        public double Nearest(double x, double z, out double distance) => Nearest(x, z, out distance, z);

        /// <summary><see cref="Nearest(double, double, out double)"/> starting the search at <paramref name="startZ"/>.</summary>
        public double Nearest(double x, double z, out double distance, double startZ)
        {
            double zeta = startZ;
            for (int it = 0; it < 6; it++)
            {
                double r = X(zeta, out double r1, out double r2);
                double g = (x - r) * r1 + (z - zeta);
                double dg = -r1 * r1 + (x - r) * r2 - 1.0;
                if (Math.Abs(dg) < 1e-6) break;
                double step = g / dg;
                if (step > 30.0) step = 30.0;
                if (step < -30.0) step = -30.0;
                zeta -= step;
                if (Math.Abs(step) < 1e-7) break;
            }

            double rx = X(zeta);
            double dx = x - rx, dz = z - zeta;
            distance = Math.Sqrt(dx * dx + dz * dz);
            return zeta;
        }

        private static double Hermite(double p0, double p1, double m0, double m1, double t)
        {
            double t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
        }

        private static double HermiteDerivative(double p0, double p1, double m0, double m1, double t)
        {
            double t2 = t * t;
            return (6 * t2 - 6 * t) * p0 + (3 * t2 - 4 * t + 1) * m0 + (-6 * t2 + 6 * t) * p1 + (3 * t2 - 2 * t) * m1;
        }
    }
}
