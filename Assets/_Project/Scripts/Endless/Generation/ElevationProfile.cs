using System;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// The road's height E(s): integrated in 4 m steps outward from the start (both ways) as a grade-limited, smoothed
    /// response to the regions. State: altitude u and grade q (along the direction of integration). Each step the wanted
    /// grade is (a) zero on level stretches (settlements, lake shores), approached so the grade is already zero on arrival;
    /// (b) on the way up to a col, the constant grade that arrives at the col's planned altitude just before its level
    /// stretch (a long even climb, at most 92 % of maxRoadGrade); (c) elsewhere a saturated pull towards the blended target
    /// altitude of the regions plus a gentle undulation. The grade itself changes by at most 0.08 per 100 m (smooth vertical
    /// curves) and never exceeds 97 % of maxRoadGrade. Because every region pulls towards its own target altitude, E cannot
    /// drift: it stays within about 30..260 m for any s. Thread-safe, order-independent.
    /// </summary>
    public sealed class ElevationProfile
    {
        /// <summary>Integration step, metres.</summary>
        public const double Step = 4.0;

        /// <summary>Steps per cached chunk.</summary>
        public const int ChunkSteps = 1024;

        /// <summary>Largest change of grade per metre of road.</summary>
        public const double GradeRate = 0.0008;

        /// <summary>How far ahead level stretches are anticipated, metres.</summary>
        public const double Lookahead = 160.0;

        private readonly EndlessParams p;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly GradientNoise undulation;
        private readonly OutwardTable<Chunk> table;

        private sealed class Chunk
        {
            public long First;
            public float[] U;   // altitude at node
            public float[] G;   // grade along +s at node
        }

        /// <summary>Creates the profile.</summary>
        public ElevationProfile(EndlessParams p, RegionSequence regions, JourneyPlan plan)
        {
            this.p = p;
            this.regions = regions;
            this.plan = plan;
            undulation = new GradientNoise(EndlessNoise.Seed(p.seed, 401, 0));
            table = new OutwardTable<Chunk>(ComputeChunk);
        }

        private double MaxGrade => Math.Max(0.01, p.maxRoadGrade);

        /// <summary>Road height at road distance <paramref name="s"/>, metres.</summary>
        public float Height(double s) => Height(s, out _);

        /// <summary>Road height and grade (rise over run along increasing s) at road distance <paramref name="s"/>.</summary>
        public float Height(double s, out float grade)
        {
            double u = s / Step;
            double f = Math.Floor(u);
            long n = (long)f;
            float t = (float)(u - f);
            Node(n, out float u0, out float g0);
            Node(n + 1, out float u1, out float g1);
            grade = g0 + (g1 - g0) * t;
            return u0 + (u1 - u0) * t;
        }

        private void Node(long n, out float u, out float g)
        {
            long c = EndlessNoise.FloorDiv(n, ChunkSteps);
            Chunk chunk = table.Get(c);
            int j = (int)(n - chunk.First);
            u = chunk.U[j];
            g = chunk.G[j];
        }

        /// <summary>Target altitude and undulation amplitude blended over the regions at s.</summary>
        private void Targets(double s, out double target, out double wiggle)
        {
            Region r = regions.Weights(s, out double w, out Region other, out double ow);
            target = Target(r) * w;
            wiggle = Wiggle(r.Kind) * w;
            if (other != null)
            {
                target += Target(other) * ow;
                wiggle += Wiggle(other.Kind) * ow;
            }
        }

        /// <summary>A region's target altitude, lifted by <see cref="FoothillLift"/> next to a pass (its foothills).</summary>
        public float Target(Region r)
        {
            if (r.Kind == RegionKind.HighlandPass) return r.TargetAltitude;
            bool foothill = regions.Get(r.Index - 1).Kind == RegionKind.HighlandPass || regions.Get(r.Index + 1).Kind == RegionKind.HighlandPass;
            return foothill ? r.TargetAltitude + FoothillLift : r.TargetAltitude;
        }

        /// <summary>Extra target altitude of the regions either side of a pass, metres.</summary>
        public const float FoothillLift = 50f;

        private static double Wiggle(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.Meadow: return 0.03;
                case RegionKind.Forest: return 0.035;
                case RegionKind.LakeCountry: return 0.012;
                case RegionKind.Downs: return 0.04;
                case RegionKind.Farmland: return 0.018;
                default: return 0.0;
            }
        }

        private Chunk ComputeChunk(long c, Chunk neighbour)
        {
            var chunk = new Chunk { First = c * ChunkSteps, U = new float[ChunkSteps], G = new float[ChunkSteps] };
            bool forward = c >= 0;
            double dir = forward ? 1.0 : -1.0;

            // Level stretches that matter for this chunk (with the lookahead).
            double sFrom = Math.Min(chunk.First, chunk.First + ChunkSteps) * Step - Lookahead - 10.0;
            double sTo = Math.Max(chunk.First, chunk.First + ChunkSteps) * Step + Lookahead + 10.0;
            var zones = new List<FlatZone>();
            plan.FlatZones(sFrom, sTo, zones);

            double u, q;   // altitude, grade along the direction of integration
            int jStart, jEnd, jStep;
            if (forward)
            {
                jStart = 0;
                jEnd = ChunkSteps;
                jStep = 1;
                if (neighbour == null)
                {
                    Targets(0.0, out double t0, out _);
                    u = t0;
                    q = 0.0;
                    chunk.U[0] = (float)u;
                    chunk.G[0] = 0f;
                    jStart = 1;
                }
                else
                {
                    u = neighbour.U[ChunkSteps - 1];
                    q = neighbour.G[ChunkSteps - 1];
                }
            }
            else
            {
                jStart = ChunkSteps - 1;
                jEnd = -1;
                jStep = -1;
                u = neighbour.U[0];
                q = -neighbour.G[0];
            }

            double maxGrade = MaxGrade;
            double climb = 0.92 * maxGrade;
            double rate = GradeRate * Step;
            for (int j = jStart; j != jEnd; j += jStep)
            {
                double sPrev = (chunk.First + j - jStep) * Step;   // the node we step from
                double s = (chunk.First + j) * Step;

                // (c) target pull plus undulation.
                Targets(sPrev, out double target, out double wiggle);
                Region r = regions.At(sPrev);
                double gPull = r.Kind == RegionKind.HighlandPass ? climb : r.Kind == RegionKind.Downs ? 0.06 : 0.05;
                gPull = Math.Min(gPull, climb);
                double want = gPull * Math.Tanh((target - u) / (gPull * 380.0));
                want += wiggle * 0.6 * EndlessNoise.Fbm1N(undulation, sPrev, 1.0 / 650.0, 2);

                // (b) climbing to a col.
                if (r.Kind == RegionKind.HighlandPass)
                {
                    double flatEdge = forward ? r.Col - JourneyPlan.InnFlat : r.Col + JourneyPlan.InnFlat;
                    double remaining = (flatEdge - sPrev) * dir;
                    if (remaining > 0.0)
                    {
                        want = Math.Max(-climb, Math.Min(climb, (r.ColAltitude - u) / Math.Max(40.0, remaining)));
                    }
                }

                // Safety rails (never needed by the region targets, kept for any parameters).
                if (u < 30.0) want = Math.Max(want, 0.02);
                if (u > p.heightRange * 0.66) want = Math.Min(want, -0.02);

                // (a) level stretches: zero inside, and a grade that can still reach zero before the next one.
                foreach (FlatZone zone in zones)
                {
                    double enter = forward ? zone.From - sPrev : sPrev - zone.To;
                    double inside = forward ? zone.To - sPrev : sPrev - zone.From;
                    if (enter <= 0.0 && inside >= 0.0)
                    {
                        want = 0.0;
                        break;
                    }

                    if (enter > 0.0 && enter < Lookahead)
                    {
                        double cap = GradeRate * Math.Max(0.0, enter - Step);
                        want = Math.Max(-cap, Math.Min(cap, want));
                    }
                }

                want = Math.Max(-maxGrade * 0.97, Math.Min(maxGrade * 0.97, want));
                q += Math.Max(-rate, Math.Min(rate, want - q));
                q = Math.Max(-maxGrade * 0.97, Math.Min(maxGrade * 0.97, q));
                u += q * Step;
                chunk.U[j] = (float)u;
                chunk.G[j] = (float)(q * dir);
                _ = s;
            }

            return chunk;
        }
    }
}
