using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>How a stream crosses the road.</summary>
    public enum CrossingKind
    {
        /// <summary>The road dips into a shallow brook (water a few centimetres deep over the road), stepping stones beside it.</summary>
        Ford = 0,

        /// <summary>A footbridge carries the road over a larger stream; the channel passes under the deck.</summary>
        Bridge = 1,
    }

    /// <summary>
    /// A planned road crossing of a stream, chosen before any heights exist (a pure function of the seed, the regions and the
    /// settlements): where it is, how big the stream is and whether it is forded or bridged. Whether the terrain lets a stream
    /// run there is decided later by <see cref="StreamNetwork"/>. Immutable.
    /// </summary>
    public sealed class Crossing
    {
        /// <summary>Index of the schedule cell (<see cref="StreamPlan.CellLength"/> of road each).</summary>
        public long Cell;
        /// <summary>Waypoint id (<see cref="JourneyPlan.MakeId"/> with <see cref="PlaceKind.Ford"/> or <see cref="PlaceKind.Bridge"/>).</summary>
        public int Id;
        /// <summary>Road distance of the crossing.</summary>
        public double S;
        /// <summary>Ford or bridge.</summary>
        public CrossingKind Kind;
        /// <summary>Width of the flat channel bed away from the crossing, metres.</summary>
        public float BedWidth;
        /// <summary>Depth of the channel below its banks, metres.</summary>
        public float Depth;
        /// <summary>Depth of the water over the bed, metres.</summary>
        public float WaterDepth;
        /// <summary>How far the stream reaches upstream and downstream of the road (path length), metres.</summary>
        public float UpLength, DownLength;
        /// <summary>Meander wavelength (m), amplitude (radians) and phase.</summary>
        public float MeanderLength, MeanderAmplitude, MeanderPhase;
        /// <summary>Region the crossing lies in.</summary>
        public long Region;
        /// <summary>Region kind there.</summary>
        public RegionKind RegionKind;
        /// <summary>Generated name ("Hazel Ford", "Kelbeck Bridge").</summary>
        public string Name;
        /// <summary>Seed of the crossing's own random streams.</summary>
        public int Seed;

        /// <summary>The level stretch of road the crossing needs (bridge deck plus approaches, or the ford's dip).</summary>
        public FlatZone Flat => new FlatZone { From = S - FlatHalf, To = S + FlatHalf };

        /// <summary>Half length of the level stretch, metres.</summary>
        public double FlatHalf => Kind == CrossingKind.Bridge ? StreamPlan.BridgeFlatHalf : StreamPlan.FordFlatHalf;
    }

    /// <summary>
    /// The schedule of stream crossings along the road: one candidate per <see cref="CellLength"/> metres of road (at 200-500 m
    /// into its cell, so two crossings are always at least 400 m apart), taken with a probability set by the region kind,
    /// never within 300 m of the journey's start, inside or beside a settlement, or near a col. Each gets its stream's size
    /// (bed 2-6 m, 0.35-1.2 m deep), its kind (fords for brooks, bridges for larger streams), reach and meander. Pure function
    /// of the seed, regions and settlements; cached per cell. Thread-safe.
    /// </summary>
    public sealed class StreamPlan
    {
        /// <summary>Road length of one schedule cell, metres.</summary>
        public const double CellLength = 700.0;

        /// <summary>No crossing within this road distance of the journey's start.</summary>
        public const double StartClearance = 300.0;

        /// <summary>Half length of the level road at a bridge (the 20 m stone deck plus approaches).</summary>
        public const double BridgeFlatHalf = 17.0;

        /// <summary>Half length of the level road at a ford (the dip plus approaches).</summary>
        public const double FordFlatHalf = 16.0;

        /// <summary>Depth of the road's dip into a ford, metres.</summary>
        public const float FordDip = 0.3f;

        /// <summary>Half length of the road's dip into a ford, metres (steepest dip grade = π·dip / (2·half) ≈ 5 %).</summary>
        public const float FordDipHalf = 9f;

        /// <summary>Water over the road at the bottom of a ford, metres.</summary>
        public const float FordWater = 0.06f;

        private readonly EndlessParams p;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly ConcurrentDictionary<long, Crossing> cells = new ConcurrentDictionary<long, Crossing>();
        private static readonly Crossing None = new Crossing { Cell = long.MinValue };

        /// <summary>Creates the schedule.</summary>
        public StreamPlan(EndlessParams p, RegionSequence regions, JourneyPlan plan)
        {
            this.p = p;
            this.regions = regions;
            this.plan = plan;
        }

        /// <summary>The crossing planned in cell <paramref name="cell"/>, or null.</summary>
        public Crossing Cell(long cell)
        {
            Crossing c = cells.GetOrAdd(cell, Make);
            return ReferenceEquals(c, None) ? null : c;
        }

        /// <summary>Index of the cell containing road distance <paramref name="s"/>.</summary>
        public static long CellOf(double s) => (long)Math.Floor(s / CellLength);

        /// <summary>Appends the planned crossings with road distance in [s0, s1], in order.</summary>
        public void Crossings(double s0, double s1, List<Crossing> results)
        {
            for (long k = CellOf(s0); k <= CellOf(s1); k++)
            {
                Crossing c = Cell(k);
                if (c != null && c.S >= s0 && c.S <= s1) results.Add(c);
            }
        }

        /// <summary>The planned crossing whose level stretch contains <paramref name="s"/>, or null.</summary>
        public Crossing At(double s)
        {
            Crossing c = Cell(CellOf(s));
            return c != null && Math.Abs(s - c.S) <= c.FlatHalf ? c : null;
        }

        private static double Chance(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.Meadow: return 0.45;
                case RegionKind.Forest: return 0.5;
                case RegionKind.LakeCountry: return 0.55;
                case RegionKind.Downs: return 0.38;
                case RegionKind.HighlandPass: return 0.6;
                default: return 0.32;
            }
        }

        private static double BigChance(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.HighlandPass: return 0.6;
                case RegionKind.LakeCountry: return 0.5;
                case RegionKind.Downs: return 0.3;
                default: return 0.38;
            }
        }

        /// <summary>
        /// True when a schedule cell would put a crossing at least <paramref name="from"/> and less than <paramref name="reach"/> from road
        /// distance <paramref name="s"/> (closer ones the settlement already suppresses) before the
        /// settlement test (seed and region only, never the places): the walled city's slot rule refuses such a site, so making a village a
        /// city never removes a crossing that the village left standing (see <see cref="JourneyPlan"/>).
        /// </summary>
        public bool RawCrossingNear(double s, double from, double reach)
        {
            for (long k = CellOf(s - reach) - 1; k <= CellOf(s + reach) + 1; k++)
            {
                double cs = Math.Round(k * CellLength + 200.0 + 300.0 * EndlessNoise.Unit(p.seed, 1001, k), 1);
                if (Math.Abs(cs) < StartClearance || Math.Abs(cs - s) >= reach || Math.Abs(cs - s) < from) continue;
                if (EndlessNoise.Unit(p.seed, 1002, k) < Chance(regions.At(cs).Kind)) return true;
            }

            return false;
        }

        private Crossing Make(long k)
        {
            int seed = p.seed;
            double s = Math.Round(k * CellLength + 200.0 + 300.0 * EndlessNoise.Unit(seed, 1001, k), 1);
            if (Math.Abs(s) < StartClearance) return None;
            Region r = regions.At(s);
            if (EndlessNoise.Unit(seed, 1002, k) >= Chance(r.Kind)) return None;

            // Never inside or beside a settlement (its disc, the blend and the approach to it), never near a col.
            var places = new List<Place>();
            plan.Inns(s - 500.0, s + 500.0, places);
            plan.RegionPlaces(s - 500.0, s + 500.0, places);
            foreach (Place place in places)
            {
                if (!place.IsSettlement) continue;
                double half = place.Kind == PlaceKind.Inn ? JourneyPlan.InnFlat : place.Radius + 30.0;
                if (Math.Abs(place.S - s) < half + TerrainShaper.SettlementBlend + 45.0) return None;
            }

            if (r.Kind == RegionKind.HighlandPass && Math.Abs(s - r.Col) < JourneyPlan.InnFlat + 120.0) return None;

            bool big = EndlessNoise.Unit(seed, 1003, k) < BigChance(r.Kind);
            double a = EndlessNoise.Unit(seed, 1004, k), b = EndlessNoise.Unit(seed, 1005, k);
            var c = new Crossing
            {
                Cell = k,
                S = s,
                Kind = big ? CrossingKind.Bridge : CrossingKind.Ford,
                BedWidth = big ? (float)(3.0 + 3.0 * a) : (float)(2.0 + 0.8 * a),
                Depth = big ? (float)(0.75 + 0.45 * b) : (float)(0.35 + 0.25 * b),
                Region = r.Index,
                RegionKind = r.Kind,
                Seed = EndlessNoise.Seed(seed, 1006, k),
                MeanderLength = (float)(70.0 + 70.0 * EndlessNoise.Unit(seed, 1007, k)),
                MeanderAmplitude = (float)(0.22 + 0.2 * EndlessNoise.Unit(seed, 1008, k)),
                MeanderPhase = (float)(Math.PI * 2.0 * EndlessNoise.Unit(seed, 1009, k)),
            };
            c.WaterDepth = Math.Max(0.14f, Math.Min(0.45f, c.Depth * 0.36f));
            // Side valleys of passes and downs reach further up; brooks of flat country are shorter.
            bool high = r.Kind == RegionKind.HighlandPass || r.Kind == RegionKind.Downs;
            double reach = EndlessNoise.Unit(seed, 1010, k);
            c.UpLength = (float)((high ? 320.0 : 200.0) + (high ? 380.0 : 300.0) * reach);
            c.DownLength = (float)(220.0 + 380.0 * EndlessNoise.Unit(seed, 1011, k));
            c.Id = JourneyPlan.MakeId(big ? PlaceKind.Bridge : PlaceKind.Ford, k);
            c.Name = CrossingNames.For(seed, k, c.Kind);
            return c;
        }
    }

    /// <summary>Names of fords and bridges ("Hazel Ford", "Kelbeck Bridge", "Alderbrook Ford"). Pure.</summary>
    public static class CrossingNames
    {
        private static readonly string[] Roots =
        {
            "Hazel", "Alder", "Willow", "Kel", "Brock", "Mill", "Otter", "Stony", "Fern", "Cress", "Wren", "Ash", "Elder", "Rush",
            "Heron", "Thorn", "Holly", "Moss", "Pebble", "Linden", "Sedge", "Birch", "Bramble", "Clear", "Cold", "Swift", "Sallow",
            "Trout", "Kingfisher", "Oak", "Rowan", "Bell", "Larks", "Fox", "Hart", "Wether", "Gill", "Dun", "Whin", "Marigold",
        };

        private static readonly string[] Waters = { "beck", "brook", "burn", "water", "bourne", "wash", "rill", "ghyll", "sike" };

        /// <summary>The name of the crossing in schedule cell <paramref name="cell"/>.</summary>
        public static string For(int seed, long cell, CrossingKind kind)
        {
            string root = Roots[(int)(EndlessNoise.Unit(seed, 1020, cell) * Roots.Length)];
            bool compound = EndlessNoise.Unit(seed, 1021, cell) < 0.55 && root.Length <= 6;
            string water = Waters[(int)(EndlessNoise.Unit(seed, 1022, cell) * Waters.Length)];
            string stem = compound ? root + water : root;
            return kind == CrossingKind.Bridge ? stem + " Bridge" : stem + " Ford";
        }
    }
}
