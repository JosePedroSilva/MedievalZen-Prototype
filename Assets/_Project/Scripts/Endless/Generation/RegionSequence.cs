using System;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>One region of the endless road with everything later stages derive from it.</summary>
    public sealed class Region
    {
        /// <summary>Position in the sequence (0 contains the journey's start).</summary>
        public long Index;
        /// <summary>Kind of landscape.</summary>
        public RegionKind Kind;
        /// <summary>Road distance where the region begins.</summary>
        public double Start;
        /// <summary>Length along the road.</summary>
        public double Length;
        /// <summary>Width of the blend with the previous region, centred on <see cref="Start"/> (300-500 m).</summary>
        public double Blend;
        /// <summary>Road distance of the col (passes only).</summary>
        public double Col;
        /// <summary>Signed ordinal of the pass (1, 2, ... ahead of the start, -1, -2, ... behind; 0 for other kinds).</summary>
        public long PassOrdinal;
        /// <summary>Ordinal of the last pass at or before this region going away from the start (0 when none).</summary>
        public long LastPassOrdinal;
        /// <summary>Road altitude the region pulls the road towards (metres).</summary>
        public float TargetAltitude;
        /// <summary>Planned altitude of the col (passes only).</summary>
        public float ColAltitude;
        /// <summary>Lake side (+1 right of the road, -1 left) for lake country; hillside side for others.</summary>
        public int Side;
        /// <summary>Generated name.</summary>
        public string Name;

        /// <summary>Road distance where the region ends.</summary>
        public double End => Start + Length;

        /// <summary>Contract view of the region.</summary>
        public RegionInfo Info => new RegionInfo { Index = (int)Index, Kind = Kind, Name = Name, SStart = Start, SEnd = End };
    }

    /// <summary>
    /// The seeded sequence of regions along the road, computed outward from region 0 (which contains s = 0 and is a Meadow or
    /// Farmland). Each kind is chosen from the regions before it (going away from the start): never the same kind twice in a
    /// row; a pass only after Downs or Forest, never within four regions of another pass, and always followed by Downs or
    /// Forest; lake country at most every fourth region; a kind not seen for six regions is forced as soon as the rules allow,
    /// so every kind recurs within about seven regions (≈ 15 km). Lengths are drawn per kind inside [regionLengthMin,
    /// regionLengthMax] (passes long, farmland short). Thread-safe, order-independent.
    /// </summary>
    public sealed class RegionSequence
    {
        /// <summary>Regions per cached chunk.</summary>
        public const int ChunkRegions = 64;

        private const int KindCount = 6;
        private const int Overdue = 7;
        private readonly EndlessParams p;
        private readonly OutwardTable<Chunk> table;

        private sealed class Chunk
        {
            public long First;
            public Region[] Regions;
        }

        /// <summary>Creates the sequence for <paramref name="p"/>.</summary>
        public RegionSequence(EndlessParams p)
        {
            this.p = p ?? throw new ArgumentNullException(nameof(p));
            table = new OutwardTable<Chunk>(ComputeChunk);
        }

        /// <summary>Region with sequence index <paramref name="index"/>.</summary>
        public Region Get(long index)
        {
            long c = EndlessNoise.FloorDiv(index, ChunkRegions);
            return table.Get(c).Regions[index - c * ChunkRegions];
        }

        /// <summary>The region containing road distance <paramref name="s"/>.</summary>
        public Region At(double s)
        {
            double meanLength = 0.5 * (MinLength + MaxLength);
            long guess = (long)Math.Floor(s / meanLength);
            Region r = Get(guess);
            while (s < r.Start) r = Get(r.Index - 1);
            while (s >= r.End) r = Get(r.Index + 1);
            return r;
        }

        private float MinLength => Math.Max(300f, Math.Min(p.regionLengthMin, p.regionLengthMax));

        private float MaxLength => Math.Max(MinLength, Math.Max(p.regionLengthMin, p.regionLengthMax));

        /// <summary>
        /// Blend weights at road distance <paramref name="s"/>: the region containing it and, within half a blend width of a
        /// boundary, its neighbour across that boundary (<paramref name="other"/> null otherwise). Weights sum to 1.
        /// </summary>
        public Region Weights(double s, out double weight, out Region other, out double otherWeight)
        {
            Region r = At(s);
            other = null;
            otherWeight = 0.0;
            weight = 1.0;
            double fromStart = s - r.Start;
            if (fromStart < r.Blend * 0.5)
            {
                double w = EndlessNoise.Smooth(-r.Blend * 0.5, r.Blend * 0.5, fromStart);
                other = Get(r.Index - 1);
                weight = w;
                otherWeight = 1.0 - w;
                return r;
            }

            Region next = Get(r.Index + 1);
            double toEnd = r.End - s;
            if (toEnd < next.Blend * 0.5)
            {
                double w = EndlessNoise.Smooth(-next.Blend * 0.5, next.Blend * 0.5, -toEnd);
                other = next;
                otherWeight = w;
                weight = 1.0 - w;
            }

            return r;
        }

        private Chunk ComputeChunk(long c, Chunk neighbour)
        {
            var chunk = new Chunk { First = c * ChunkRegions, Regions = new Region[ChunkRegions] };
            if (c >= 0)
            {
                for (int j = 0; j < ChunkRegions; j++)
                {
                    long index = chunk.First + j;
                    Region prev = index == 0 ? null : j > 0 ? chunk.Regions[j - 1] : neighbour.Regions[ChunkRegions - 1];
                    chunk.Regions[j] = Make(index, prev, k => History(chunk, neighbour, j, k, true));
                }
            }
            else
            {
                for (int j = ChunkRegions - 1; j >= 0; j--)
                {
                    long index = chunk.First + j;
                    Region next = j < ChunkRegions - 1 ? chunk.Regions[j + 1] : neighbour.Regions[0];
                    chunk.Regions[j] = Make(index, next, k => History(chunk, neighbour, j, k, false));
                }
            }

            return chunk;
        }

        /// <summary>The region k steps back towards the start from slot j (k ≥ 1), or null beyond region 0.</summary>
        private static Region History(Chunk chunk, Chunk neighbour, int j, int k, bool forward)
        {
            int at = forward ? j - k : j + k;
            if (at >= 0 && at < ChunkRegions) return chunk.Regions[at];
            if (neighbour == null) return null;
            int nAt = forward ? at + ChunkRegions : at - ChunkRegions;
            if (nAt < 0 || nAt >= ChunkRegions) return null;
            // The forward chain (regions 1, 2, ...) never sees the regions behind the start (they are computed from it);
            // the backward chain (-1, -2, ...) does see the regions ahead of the start, so the rules hold across region 0.
            return neighbour.Regions[nAt];
        }

        /// <summary>Builds region <paramref name="index"/> from its predecessor (towards the start) and history.</summary>
        private Region Make(long index, Region towardStart, Func<int, Region> history)
        {
            int seed = p.seed;
            var r = new Region { Index = index };
            r.Kind = index == 0 ? (EndlessNoise.Unit(seed, 201, 0) < 0.5 ? RegionKind.Meadow : RegionKind.Farmland) : ChooseKind(index, history);

            double u = EndlessNoise.Unit(seed, 202, index);
            float min = MinLength, max = MaxLength;
            double lo, hi;
            switch (r.Kind)
            {
                case RegionKind.HighlandPass: lo = Lerp(min, max, 0.72); hi = max; break;
                case RegionKind.Forest:
                case RegionKind.Downs: lo = Lerp(min, max, 0.2); hi = Lerp(min, max, 0.8); break;
                case RegionKind.Farmland: lo = min; hi = Lerp(min, max, 0.6); break;
                default: lo = min; hi = Lerp(min, max, 0.7); break;
            }

            r.Length = Math.Round(lo + (hi - lo) * u);
            r.Blend = 300.0 + 200.0 * EndlessNoise.Unit(seed, 203, index);
            if (index == 0) r.Start = -Math.Round(r.Length * (0.2 + 0.15 * EndlessNoise.Unit(seed, 204, 0)));
            else if (index > 0) r.Start = towardStart.End;
            else r.Start = towardStart.Start - r.Length;

            double v = EndlessNoise.Unit(seed, 205, index);
            switch (r.Kind)
            {
                case RegionKind.Meadow: r.TargetAltitude = (float)(62 + 26 * v); break;
                case RegionKind.Farmland: r.TargetAltitude = (float)(48 + 18 * v); break;
                case RegionKind.LakeCountry: r.TargetAltitude = (float)(44 + 14 * v); break;
                case RegionKind.Forest: r.TargetAltitude = (float)(82 + 34 * v); break;
                case RegionKind.Downs: r.TargetAltitude = (float)(118 + 34 * v); break;
                default: r.TargetAltitude = (float)(165 + 20 * v); break;
            }

            r.Side = EndlessNoise.Unit(seed, 206, index) < 0.5 ? -1 : 1;
            if (r.Kind == RegionKind.HighlandPass)
            {
                r.Col = r.Start + r.Length * (0.45 + 0.1 * EndlessNoise.Unit(seed, 207, index));
                r.ColAltitude = (float)(232 + 26 * EndlessNoise.Unit(seed, 208, index));
            }

            long lastPass = towardStart?.LastPassOrdinal ?? 0;
            if (r.Kind == RegionKind.HighlandPass)
            {
                r.PassOrdinal = index > 0 ? lastPass + 1 : lastPass - 1;
                r.LastPassOrdinal = r.PassOrdinal;
            }
            else
            {
                r.LastPassOrdinal = lastPass;
            }

            r.Name = NameFor(seed, r);
            return r;
        }

        private RegionKind ChooseKind(long index, Func<int, Region> history)
        {
            var lastSeen = new int[KindCount];
            for (int k = 0; k < KindCount; k++) lastSeen[k] = 99;
            int known = 0;
            for (int k = 1; k <= 12; k++)
            {
                Region h = history(k);
                if (h == null) break;
                known = k;
                int kind = (int)h.Kind;
                if (lastSeen[kind] == 99) lastSeen[kind] = k;
            }

            RegionKind last = history(1).Kind;
            bool lastIsPassApproach = last == RegionKind.Forest || last == RegionKind.Downs;
            var allowed = new bool[KindCount];
            for (int k = 0; k < KindCount; k++)
            {
                var kind = (RegionKind)k;
                bool ok = kind != last;
                if (kind == RegionKind.HighlandPass) ok &= lastIsPassApproach && lastSeen[k] > 4;
                if (kind == RegionKind.LakeCountry) ok &= lastSeen[k] > 3;
                allowed[k] = ok;
            }

            // After a pass: always Downs or Forest.
            if (last == RegionKind.HighlandPass)
            {
                for (int k = 0; k < KindCount; k++) allowed[k] = (RegionKind)k == RegionKind.Forest || (RegionKind)k == RegionKind.Downs;
            }

            // Overdue kinds first (most overdue wins; ties by kind order). A pass that is overdue but cannot come yet makes
            // its approach (Downs or Forest) come now.
            int best = -1, bestAge = -1;
            for (int k = 0; k < KindCount; k++)
            {
                int age = lastSeen[k] == 99 ? (known >= Overdue ? 99 : 0) : lastSeen[k];
                if (age < Overdue) continue;
                int candidate = k;
                if (!allowed[k])
                {
                    if ((RegionKind)k != RegionKind.HighlandPass || last == RegionKind.HighlandPass) continue;
                    int f = (int)RegionKind.Forest, d = (int)RegionKind.Downs;
                    candidate = allowed[f] && (!allowed[d] || lastSeen[f] >= lastSeen[d]) ? f : allowed[d] ? d : -1;
                    if (candidate < 0) continue;
                }

                if (age > bestAge)
                {
                    bestAge = age;
                    best = candidate;
                }
            }

            if (best >= 0) return (RegionKind)best;

            double[] baseWeight = { 1.0, 1.0, 0.8, 0.9, 0.85, 0.9 };
            double total = 0.0;
            var weights = new double[KindCount];
            for (int k = 0; k < KindCount; k++)
            {
                if (!allowed[k]) continue;
                int age = Math.Min(9, lastSeen[k]);
                // Kinds not seen for a while are likelier, but recent ones (other than the last) stay possible.
                weights[k] = baseWeight[k] * (age <= 2 ? 0.35 : age <= 4 ? 1.0 : 1.0 + 0.9 * (age - 4));
                total += weights[k];
            }

            double roll = EndlessNoise.Unit(p.seed, 209, index) * total;
            for (int k = 0; k < KindCount; k++)
            {
                if (weights[k] <= 0.0) continue;
                roll -= weights[k];
                if (roll < 0.0) return (RegionKind)k;
            }

            for (int k = KindCount - 1; k >= 0; k--)
            {
                if (allowed[k]) return (RegionKind)k;
            }

            return RegionKind.Meadow;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static readonly string[][] Forms =
        {
            new[] { "{0} Meadows", "The {0} Leas", "{0} Green" },             // Meadow
            new[] { "{0} Wood", "The {0} Forest", "{0} Chase" },               // Forest
            new[] { "The {0} Lakes", "{0} Waters", "The {0} Meres" },          // LakeCountry
            new[] { "The {0} Downs", "The {0} Wolds", "{0} Down" },            // Downs
            new[] { "The {0} Fells", "The {0} Crags", "{0} Heights" },         // HighlandPass
            new[] { "The Vale of {0}", "{0} Fields", "The {0} Farmlands" },    // Farmland
        };

        private static string NameFor(int seed, Region r)
        {
            string[] forms = Forms[(int)r.Kind];
            string form = forms[(int)(EndlessNoise.Unit(seed, 210, r.Index) * forms.Length)];
            string root = r.Kind == RegionKind.HighlandPass ? SettlementNames.PeakAt(seed, r.Index) : SettlementNames.PlaceAt(seed, r.Index * 2);
            return string.Format(form, root);
        }
    }
}
