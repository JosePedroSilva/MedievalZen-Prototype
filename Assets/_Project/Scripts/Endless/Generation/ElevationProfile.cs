using System;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// The road's height E(s): integrated in 4 m steps outward from the start (both ways) as a grade-limited, smoothed
    /// response to the regions. State: altitude u and grade q (along the direction of integration). Each step the wanted
    /// grade is (a) zero on level stretches (settlements, lake shores, stream crossings and the shelves of a climb), approached
    /// so the grade is already zero on arrival; (b) on the way up to a col (from <see cref="ClimbLead"/> before the pass region),
    /// the grade that arrives at the col's planned altitude just before its level stretch while leaving room for the level
    /// stretches still ahead: the climb becomes pitches of 4-7 % separated by shelves 40-120 m long every 250-450 m (a plan made
    /// when the climb starts, dropping shelves the climb cannot afford); (c) elsewhere a saturated pull towards the blended
    /// target altitude of the regions plus a gentle undulation, with fixed shelves on the far side of a pass so the way down
    /// (a climb when walked back) rests too. The grade itself changes by at most 0.08 per 100 m (smooth vertical curves) and
    /// never exceeds 97 % of maxRoadGrade. Because every region pulls towards its own target altitude, E cannot drift: it stays
    /// within about 30..260 m for any s. <see cref="Height(double)"/> is that profile minus the dip of the road into each ford
    /// (<see cref="BaseHeight(double)"/> is without). Thread-safe, order-independent.
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

        /// <summary>The climb to a col starts this far before the pass region (in the foothills), metres.</summary>
        public const double ClimbLead = 1300.0;

        /// <summary>Steepest pitch a climb is planned with (rise over run); shelves are dropped until the pitches fit.</summary>
        public const double PlannedPitch = 0.068;

        private readonly EndlessParams p;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly GradientNoise undulation;
        private readonly OutwardTable<Chunk> table;
        private StreamNetwork streams;

        private sealed class Chunk
        {
            public long First;
            public float[] U;   // altitude at node
            public float[] G;   // grade along +s at node
            public ClimbPlan Active;   // the climb under way at the chunk's last node (same integration direction), or null
            public readonly List<FlatZone> Shelves = new List<FlatZone>();   // shelves realised in this chunk
        }

        /// <summary>A climb to a col planned when the integration reaches its foot.</summary>
        private sealed class ClimbPlan
        {
            public long Pass;
            public double Dir;
            public double Edge;   // road distance of the col's level stretch edge
            public float ColAltitude;
            public readonly List<FlatZone> Shelves = new List<FlatZone>();
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

        /// <summary>Connects the stream network (fords dip the road). Called once by the generator before any use.</summary>
        internal void SetStreams(StreamNetwork network) => streams = network;

        private double MaxGrade => Math.Max(0.01, p.maxRoadGrade);

        /// <summary>Road height at road distance <paramref name="s"/>, metres (fords included).</summary>
        public float Height(double s) => Height(s, out _);

        /// <summary>Road height and grade (rise over run along increasing s) at road distance <paramref name="s"/> (fords included).</summary>
        public float Height(double s, out float grade)
        {
            float h = BaseHeight(s, out grade);
            if (streams != null)
            {
                float dip = streams.FordDip(s, out float slope);
                if (dip > 0f)
                {
                    h -= dip;
                    grade -= slope;
                }
            }

            return h;
        }

        /// <summary>The integrated profile without the fords' dips (what the terrain's baseline and the streams are planned on).</summary>
        public float BaseHeight(double s) => BaseHeight(s, out _);

        /// <summary><see cref="BaseHeight(double)"/> and its grade.</summary>
        public float BaseHeight(double s, out float grade)
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

        /// <summary>
        /// Appends the shelves (level rests on the climbs and descents of passes, 40-120 m long) whose middle lies in [s0, s1],
        /// ordered by road distance. Level stretches of inns and crossings are not included.
        /// </summary>
        public void Shelves(double s0, double s1, List<FlatZone> results)
        {
            long c0 = EndlessNoise.FloorDiv((long)Math.Floor(s0 / Step), ChunkSteps) - 1;
            long c1 = EndlessNoise.FloorDiv((long)Math.Floor(s1 / Step), ChunkSteps) + 1;
            var seen = new HashSet<double>();
            var found = new List<FlatZone>();
            for (long c = c0; c <= c1; c++)
            {
                foreach (FlatZone z in table.Get(c).Shelves)
                {
                    double mid = (z.From + z.To) * 0.5;
                    if (mid < s0 || mid > s1 || !seen.Add(Math.Round(z.From, 3))) continue;
                    found.Add(z);
                }
            }

            found.Sort((a, b) => a.From.CompareTo(b.From));
            results.AddRange(found);
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

        /// <summary>
        /// The pass whose climb (in the direction of integration <paramref name="dir"/>) contains <paramref name="s"/>: from
        /// <see cref="ClimbLead"/> before the pass region up to its col's level stretch. Null when none.
        /// </summary>
        private Region ClimbOf(double s, double dir, out double edge)
        {
            edge = 0.0;
            Region r = regions.At(s);
            Region pass = null;
            if (r.Kind == RegionKind.HighlandPass) pass = r;
            else
            {
                Region next = regions.Get(r.Index + (dir > 0 ? 1 : -1));
                if (next.Kind == RegionKind.HighlandPass && (dir > 0 ? next.Start - s : s - next.End) <= ClimbLead) pass = next;
            }

            if (pass == null) return null;
            edge = dir > 0 ? pass.Col - JourneyPlan.InnFlat : pass.Col + JourneyPlan.InnFlat;
            return (edge - s) * dir > 0.0 ? pass : null;
        }

        /// <summary>Plans the shelves of the climb to <paramref name="pass"/> starting at <paramref name="s"/> with altitude <paramref name="u"/>.</summary>
        private ClimbPlan MakeClimb(Region pass, double s, double u, double dir, double edge)
        {
            var climb = new ClimbPlan { Pass = pass.Index, Dir = dir, Edge = edge, ColAltitude = pass.ColAltitude };
            double length = Math.Abs(edge - s);
            double gain = pass.ColAltitude - u;
            double lo = Math.Min(s, edge), hi = Math.Max(s, edge);
            var fixedZones = new List<FlatZone>();
            plan.FlatZones(lo, hi, fixedZones);

            // Candidate shelves every 250-450 m from the foot, kept clear of the level stretches already there (which rest the
            // traveller anyway) and of the last 220 m below the col. When the climb cannot afford them all, the spacing grows
            // (so the shelves that remain are spread over the whole climb) until the pitches between all level stretches fit.
            var candidates = new List<FlatZone>();
            foreach (double spread in new[] { 1.0, 1.25, 1.55, 1.9, 2.4, 3.2, 4.5 })
            {
                candidates.Clear();
                double at = 160.0 + 120.0 * EndlessNoise.Unit(p.seed, 1030, pass.Index, dir > 0 ? 1 : 0);
                for (int k = 0; k < 24 && at < length - 220.0; k++)
                {
                    double len = 40.0 + 60.0 * EndlessNoise.Unit(p.seed, 1031, pass.Index * 64 + k, dir > 0 ? 1 : 0);
                    double a = s + dir * at, b = s + dir * (at + len);
                    double from = Math.Min(a, b), to = Math.Max(a, b);
                    bool clear = true;
                    foreach (FlatZone z in fixedZones)
                    {
                        if (z.To + 130.0 > from && z.From - 130.0 < to) clear = false;
                    }

                    if (clear) candidates.Add(new FlatZone { From = from, To = to });
                    at += len + spread * (250.0 + 200.0 * EndlessNoise.Unit(p.seed, 1032, pass.Index * 64 + k, dir > 0 ? 1 : 0));
                }

                double level = 0.0;
                int rests = candidates.Count + 1;
                foreach (FlatZone z in candidates) level += z.To - z.From;
                foreach (FlatZone z in fixedZones)
                {
                    double overlap = Math.Min(hi, z.To) - Math.Max(lo, z.From);
                    if (overlap <= 0.0) continue;
                    level += overlap;
                    rests++;
                }

                double g = PlannedPitch;
                for (int it = 0; it < 4; it++)
                {
                    double effective = length - level - rests * g / GradeRate;
                    g = effective > 40.0 ? Math.Max(0.0, gain) / effective : 1.0;
                }

                if (g <= PlannedPitch) break;
                candidates.Clear();
            }

            climb.Shelves.AddRange(candidates);
            return climb;
        }

        /// <summary>Fixed shelves on the far side of a pass (in the direction of integration), every 280-420 m from the col down.</summary>
        private static void DescentShelves(EndlessParams p, Region pass, double dir, List<FlatZone> results)
        {
            double start = pass.Col + dir * (JourneyPlan.InnFlat + 230.0);
            double end = dir > 0 ? pass.End + 250.0 : pass.Start - 250.0;
            double at = 0.0;
            for (int k = 0; k < 16; k++)
            {
                double len = 40.0 + 70.0 * EndlessNoise.Unit(p.seed, 1033, pass.Index * 64 + k, dir > 0 ? 1 : 0);
                double a = start + dir * at, b = start + dir * (at + len);
                if ((end - b) * dir < 0.0) break;
                results.Add(new FlatZone { From = Math.Min(a, b), To = Math.Max(a, b) });
                at += len + 280.0 + 140.0 * EndlessNoise.Unit(p.seed, 1034, pass.Index * 64 + k, dir > 0 ? 1 : 0);
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

            // Shelves on the far side of the passes this chunk touches (the side the integration goes down).
            var shelves = new List<FlatZone>();
            for (Region r = regions.At(sFrom - 2000.0); r.Start <= sTo + 2000.0; r = regions.Get(r.Index + 1))
            {
                if (r.Kind != RegionKind.HighlandPass) continue;
                var down = new List<FlatZone>();
                DescentShelves(p, r, dir, down);
                foreach (FlatZone z in down)
                {
                    if (z.To >= sFrom && z.From <= sTo) shelves.Add(z);
                }
            }

            ClimbPlan climb = neighbour != null && neighbour.Active != null && neighbour.Active.Dir == dir ? neighbour.Active : null;
            double chunkLo = Math.Min(chunk.First, chunk.First + ChunkSteps - 1) * Step, chunkHi = Math.Max(chunk.First, chunk.First + ChunkSteps - 1) * Step;
            if (climb != null)
            {
                foreach (FlatZone z in climb.Shelves) RecordShelf(chunk, z, chunkLo, chunkHi);
            }

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
            double climbGrade = 0.92 * maxGrade;
            double rate = GradeRate * Step;
            for (int j = jStart; j != jEnd; j += jStep)
            {
                double sPrev = (chunk.First + j - jStep) * Step;   // the node we step from

                // (c) target pull plus undulation.
                Targets(sPrev, out double target, out double wiggle);
                Region r = regions.At(sPrev);
                double gPull = r.Kind == RegionKind.HighlandPass ? climbGrade : r.Kind == RegionKind.Downs ? 0.06 : 0.05;
                gPull = Math.Min(gPull, climbGrade);
                double want = gPull * Math.Tanh((target - u) / (gPull * 380.0));
                want += wiggle * 0.6 * EndlessNoise.Fbm1N(undulation, sPrev, 1.0 / 650.0, 2);

                // (b) climbing to a col: pitches between the level stretches still ahead.
                Region pass = ClimbOf(sPrev, dir, out double edge);
                if (pass == null)
                {
                    climb = null;
                }
                else
                {
                    if (climb == null || climb.Pass != pass.Index)
                    {
                        climb = MakeClimb(pass, sPrev, u, dir, edge);
                        foreach (FlatZone z in climb.Shelves) RecordShelf(chunk, z, chunkLo, chunkHi);
                    }

                    double remaining = (edge - sPrev) * dir;
                    double level = 0.0;
                    int rests = 1;
                    foreach (FlatZone z in climb.Shelves) Ahead(z, sPrev, edge, dir, ref level, ref rests);
                    foreach (FlatZone z in zones) Ahead(z, sPrev, edge, dir, ref level, ref rests);
                    double effective = remaining - level - rests * Math.Abs(q) * 0.5 / GradeRate;
                    want = Math.Max(-climbGrade, Math.Min(climbGrade, (pass.ColAltitude - u) / Math.Max(40.0, effective)));
                }

                // Safety rails (never needed by the region targets, kept for any parameters).
                if (u < 30.0) want = Math.Max(want, 0.02);
                if (u > p.heightRange * 0.66) want = Math.Min(want, -0.02);

                // (a) level stretches: zero inside, and a grade that can still reach zero before the next one.
                Level(zones, sPrev, forward, ref want);
                if (climb != null) Level(climb.Shelves, sPrev, forward, ref want);
                else Level(shelves, sPrev, forward, ref want);

                want = Math.Max(-maxGrade * 0.97, Math.Min(maxGrade * 0.97, want));
                q += Math.Max(-rate, Math.Min(rate, want - q));
                q = Math.Max(-maxGrade * 0.97, Math.Min(maxGrade * 0.97, q));
                u += q * Step;
                chunk.U[j] = (float)u;
                chunk.G[j] = (float)(q * dir);
            }

            foreach (FlatZone z in shelves) RecordShelf(chunk, z, chunkLo, chunkHi);
            chunk.Active = climb;
            return chunk;
        }

        private static void RecordShelf(Chunk chunk, FlatZone z, double lo, double hi)
        {
            if (z.To < lo || z.From > hi) return;
            foreach (FlatZone have in chunk.Shelves)
            {
                if (have.From == z.From && have.To == z.To) return;
            }

            chunk.Shelves.Add(z);
        }

        /// <summary>Adds the part of <paramref name="z"/> between <paramref name="s"/> and <paramref name="edge"/> to the level distance ahead.</summary>
        private static void Ahead(FlatZone z, double s, double edge, double dir, ref double level, ref int rests)
        {
            double a = dir > 0 ? Math.Max(s, z.From) : Math.Max(edge, z.From);
            double b = dir > 0 ? Math.Min(edge, z.To) : Math.Min(s, z.To);
            if (b <= a) return;
            level += b - a;
            rests++;
        }

        private static void Level(List<FlatZone> zones, double sPrev, bool forward, ref double want)
        {
            foreach (FlatZone zone in zones)
            {
                double enter = forward ? zone.From - sPrev : sPrev - zone.To;
                double inside = forward ? zone.To - sPrev : sPrev - zone.From;
                if (enter <= 0.0 && inside >= 0.0)
                {
                    want = 0.0;
                    return;
                }

                if (enter > 0.0 && enter < Lookahead)
                {
                    double cap = GradeRate * Math.Max(0.0, enter - Step);
                    want = Math.Max(-cap, Math.Min(cap, want));
                }
            }
        }
    }
}
