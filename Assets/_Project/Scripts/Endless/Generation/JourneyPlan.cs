using System;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>Internal kinds of places along the road (the first eight match <see cref="WaypointKind"/>).</summary>
    public enum PlaceKind
    {
        /// <summary>Roadside inn (also the start and the refuge at a col).</summary>
        Inn = 0,
        /// <summary>A few houses (dressed like a finite-world village).</summary>
        Hamlet = 1,
        /// <summary>A village (dressed like a finite-world town).</summary>
        Village = 2,
        /// <summary>The col of a pass.</summary>
        Pass = 3,
        /// <summary>Viewpoint with an arch and standing stones.</summary>
        Viewpoint = 4,
        /// <summary>A lake beside the road.</summary>
        Lake = 5,
        /// <summary>Roadside camp.</summary>
        Camp = 6,
        /// <summary>Milestone.</summary>
        Milestone = 7,
    }

    /// <summary>A place along the road (settlement, lake, feature). Immutable after creation.</summary>
    public sealed class Place
    {
        /// <summary>Kind.</summary>
        public PlaceKind Kind;
        /// <summary>Stable id (<see cref="JourneyPlan.MakeId"/>).</summary>
        public int Id;
        /// <summary>Road distance of the place (lakes: of their middle).</summary>
        public double S;
        /// <summary>Footprint radius (settlements) or 0.</summary>
        public float Radius;
        /// <summary>Generated name or null.</summary>
        public string Name;
        /// <summary>Region the place belongs to.</summary>
        public long Region;
        /// <summary>Side of the road (+1 right, -1 left) for off-road places.</summary>
        public int Side;
        /// <summary>True for the refuge inn at a col.</summary>
        public bool Refuge;
        /// <summary>Lakes: first and last road distance along the shore.</summary>
        public double S0, S1;
        /// <summary>Lakes: distance from the road centre to the near shore, and the half width / half length of the basin.</summary>
        public float ShoreOffset, HalfWidth, HalfLength;
        /// <summary>
        /// Lakes: the road-distance range over which the road passes through the lake's zone (every terrain tile the lake can
        /// touch); the road is level over it, so no stretch of road in a tile with this lake's water plane can be flooded.
        /// </summary>
        public double FlatFrom, FlatTo;

        /// <summary>True for inns, hamlets and villages.</summary>
        public bool IsSettlement => Kind == PlaceKind.Inn || Kind == PlaceKind.Hamlet || Kind == PlaceKind.Village;

        /// <summary>Settlement type used to dress it with the finite world's dresser (hamlet = village layout, village = town layout).</summary>
        public MedievalZen.Roads.SettlementType DressAs =>
            Kind == PlaceKind.Village ? MedievalZen.Roads.SettlementType.Town : Kind == PlaceKind.Hamlet ? MedievalZen.Roads.SettlementType.Village : MedievalZen.Roads.SettlementType.Inn;
    }

    /// <summary>A stretch of road that must be level (settlements, lake shores).</summary>
    public struct FlatZone
    {
        /// <summary>Road distance range.</summary>
        public double From, To;
    }

    /// <summary>
    /// What lies along the road before any heights exist: inns (the start, every col, and evenly spread between them so that
    /// consecutive inns are innSpacingMin..innSpacingMax apart), the village or hamlet of each farmland region (as far from the
    /// inns as its middle allows), the lakes of each lake-country region (one or two, never sharing a terrain tile with
    /// another lake) and the level stretches they need. Every answer is a pure function of the seed and the region sequence,
    /// so it is the same for any query window. Thread-safe (immutable results; per-region caches are idempotent).
    /// </summary>
    public sealed class JourneyPlan
    {
        /// <summary>Half-length of the level road at an inn (radius 35 m + blend + approach).</summary>
        public const double InnFlat = 70.0;

        /// <summary>Road distance between milestones.</summary>
        public const double MilestoneSpacing = 500.0;

        private readonly EndlessParams p;
        private readonly RoadCurve road;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, Place[]> regionPlaces =
            new System.Collections.Concurrent.ConcurrentDictionary<long, Place[]>();

        /// <summary>Creates the plan.</summary>
        public JourneyPlan(EndlessParams p, RegionSequence regions, RoadCurve road)
        {
            this.p = p;
            Regions = regions;
            this.road = road;
        }

        /// <summary>The region sequence.</summary>
        public RegionSequence Regions { get; }

        /// <summary>Stable waypoint id from a kind and a kind-specific index (never -1).</summary>
        public static int MakeId(PlaceKind kind, long index) => unchecked((int)(index * 16 + (int)kind));

        // ------------------------------------------------------------------ inns

        /// <summary>The mandatory inn site at or before <paramref name="s"/> (the start or a col) and its ordinal.</summary>
        public double MandatoryBefore(double s, out long ordinal)
        {
            Region r = Regions.At(s);
            while (true)
            {
                if (r.Kind == RegionKind.HighlandPass && r.Col <= s)
                {
                    if (s >= 0 && r.Col < 0)
                    {
                        ordinal = 0;
                        return 0.0;
                    }

                    ordinal = r.PassOrdinal;
                    return r.Col;
                }

                if (s >= 0 && r.Index <= 0)
                {
                    ordinal = 0;
                    return 0.0;
                }

                r = Regions.Get(r.Index - 1);
            }
        }

        /// <summary>The mandatory inn site after <paramref name="s"/> (strictly) and its ordinal.</summary>
        public double MandatoryAfter(double s, out long ordinal)
        {
            Region r = Regions.At(s);
            while (true)
            {
                if (r.Kind == RegionKind.HighlandPass && r.Col > s)
                {
                    if (s < 0 && r.Col > 0)
                    {
                        ordinal = 0;
                        return 0.0;
                    }

                    ordinal = r.PassOrdinal;
                    return r.Col;
                }

                if (s < 0 && r.Index >= 0)
                {
                    ordinal = 0;
                    return 0.0;
                }

                r = Regions.Get(r.Index + 1);
            }
        }

        /// <summary>Inns of the interval that starts at mandatory site <paramref name="a"/> (ordinal <paramref name="ordinal"/>) and ends before <paramref name="b"/>.</summary>
        public List<Place> InnsOfInterval(double a, double b, long ordinal)
        {
            var result = new List<Place>();
            double gap = b - a;
            float min = Math.Max(300f, Math.Min(p.innSpacingMin, p.innSpacingMax));
            float max = Math.Max(min, Math.Max(p.innSpacingMin, p.innSpacingMax));
            int k = 1;
            if (gap >= min)
            {
                int kmin = (int)Math.Ceiling(gap / max);
                int kmax = Math.Max(kmin, (int)Math.Floor(gap / min));
                k = kmin + (kmax > kmin && EndlessNoise.Unit(p.seed, 301, ordinal) < 0.35 ? 1 : 0);
            }

            k = Math.Min(k, 15);
            var lengths = new double[k];
            if (k > 1 || gap >= min)
            {
                // Spread the slack over the k legs with random weights, capping each leg at max.
                double slack = gap - k * (double)min;
                var weights = new double[k];
                for (int j = 0; j < k; j++) weights[j] = 0.35 + 0.65 * EndlessNoise.Unit(p.seed, 302, ordinal, j);
                var capped = new bool[k];
                for (int pass = 0; pass < k + 1; pass++)
                {
                    double wsum = 0.0, fixedSum = 0.0;
                    for (int j = 0; j < k; j++)
                    {
                        if (capped[j]) fixedSum += max - min;
                        else wsum += weights[j];
                    }

                    double rest = slack - fixedSum;
                    bool changed = false;
                    for (int j = 0; j < k; j++)
                    {
                        if (capped[j])
                        {
                            lengths[j] = max;
                            continue;
                        }

                        lengths[j] = min + (wsum > 0 ? rest * weights[j] / wsum : 0.0);
                        if (lengths[j] > max)
                        {
                            capped[j] = true;
                            changed = true;
                        }
                    }

                    if (!changed) break;
                }
            }

            double at = a;
            for (int j = 0; j < k; j++)
            {
                bool refuge = j == 0 && ordinal != 0;
                var inn = new Place
                {
                    Kind = PlaceKind.Inn,
                    Id = MakeId(PlaceKind.Inn, ordinal * 16 + j),
                    S = Math.Round(at, 1),
                    Radius = SettlementSite.InnRadius,
                    Refuge = refuge,
                    Name = refuge ? SettlementNames.MountainInnAt(p.seed, ordinal) : SettlementNames.InnAt(p.seed, ordinal * 8 + j),
                    Region = Regions.At(at).Index,
                };
                inn.Side = EndlessNoise.Unit(p.seed, 303, inn.Id) < 0.5 ? -1 : 1;
                result.Add(inn);
                at += lengths[j];
            }

            return result;
        }

        /// <summary>Appends the inns with road distance in [s0, s1], in order.</summary>
        public void Inns(double s0, double s1, List<Place> results)
        {
            double a = MandatoryBefore(s0, out long ordinal);
            while (a <= s1)
            {
                double b = MandatoryAfter(a, out long nextOrdinal);
                foreach (Place inn in InnsOfInterval(a, b, ordinal))
                {
                    if (inn.S >= s0 && inn.S <= s1) results.Add(inn);
                }

                a = b;
                ordinal = nextOrdinal;
            }
        }

        // ------------------------------------------------------------------ region places

        /// <summary>Villages, hamlets and lakes of region <paramref name="index"/> (cached; same answer every time).</summary>
        public Place[] PlacesOf(long index) => regionPlaces.GetOrAdd(index, ComputePlaces);

        private Place[] ComputePlaces(long index)
        {
            Region r = Regions.Get(index);
            var list = new List<Place>();
            if (r.Kind == RegionKind.Farmland) AddVillage(r, list);
            if (r.Kind == RegionKind.LakeCountry) AddLakes(r, list);
            return list.ToArray();
        }

        private void AddVillage(Region r, List<Place> list)
        {
            var inns = new List<Place>();
            Inns(r.Start - 3000.0, r.End + 3000.0, inns);
            double bestS = r.Start + r.Length * 0.5, bestScore = double.NegativeInfinity;
            for (int k = 0; k < 9; k++)
            {
                double s = r.Start + r.Length * (0.3 + 0.4 * k / 8.0);
                double nearest = double.PositiveInfinity;
                foreach (Place inn in inns) nearest = Math.Min(nearest, Math.Abs(inn.S - s));
                double score = Math.Min(nearest, 900.0) + 40.0 * EndlessNoise.Unit(p.seed, 311, r.Index, k);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestS = s;
                }
            }

            bool village = EndlessNoise.Unit(p.seed, 312, r.Index) < 0.55;
            var place = new Place
            {
                Kind = village ? PlaceKind.Village : PlaceKind.Hamlet,
                Id = MakeId(village ? PlaceKind.Village : PlaceKind.Hamlet, r.Index),
                S = Math.Round(bestS, 1),
                Radius = village ? SettlementSite.TownRadius : SettlementSite.VillageRadius,
                Name = SettlementNames.PlaceAt(p.seed, r.Index * 2 + 1),
                Region = r.Index,
            };
            list.Add(place);
        }

        private void AddLakes(Region r, List<Place> list)
        {
            var inns = new List<Place>();
            Inns(r.Start - 500.0, r.End + 500.0, inns);
            int want = 1 + (r.Length > 2200.0 && EndlessNoise.Unit(p.seed, 321, r.Index) < 0.6 ? 1 : 0);
            var accepted = new List<(Place lake, double x, double z, double radius)>();
            for (int attempt = 0; attempt < 24 && accepted.Count < want; attempt++)
            {
                double halfLength = 160.0 + 220.0 * EndlessNoise.Unit(p.seed, 322, r.Index, attempt);
                double margin = 320.0 + halfLength;
                if (r.Length < 2.0 * margin) halfLength = Math.Max(120.0, r.Length * 0.5 - 330.0);
                margin = 320.0 + halfLength;
                if (r.Length < 2.0 * margin) break;
                double centre = r.Start + margin + (r.Length - 2.0 * margin) * EndlessNoise.Unit(p.seed, 323, r.Index, attempt);
                bool clear = true;
                foreach (Place inn in inns)
                {
                    if (Math.Abs(inn.S - centre) < halfLength + 150.0) clear = false;
                }

                if (!clear) continue;
                int side = EndlessNoise.Unit(p.seed, 324, r.Index, attempt) < 0.7 ? r.Side : -r.Side;
                float halfWidth = (float)(70.0 + 110.0 * EndlessNoise.Unit(p.seed, 325, r.Index, attempt));
                float offset = (float)(16.0 + 6.0 * EndlessNoise.Unit(p.seed, 326, r.Index, attempt));
                // Footprint circle in absolute coordinates.
                double z = road.ZAt(centre);
                double x = road.X(z, out double slope, out _);
                double len = Math.Sqrt(1.0 + slope * slope);
                double nx = 1.0 / len, nz = -slope / len;   // right-hand normal of the heading (dx, dz) = (slope, 1)/len
                double cx = x + nx * side * (offset + halfWidth), cz = z + nz * side * (offset + halfWidth);
                double radius = Math.Max(halfLength, halfWidth) * 1.35 + 40.0;
                foreach (var other in accepted)
                {
                    double dx = cx - other.x, dz = cz - other.z;
                    if (Math.Sqrt(dx * dx + dz * dz) < radius + other.radius + 420.0) clear = false;
                    if (Math.Abs(centre - other.lake.S) < halfLength + other.lake.HalfLength + 600.0) clear = false;
                }

                if (!clear) continue;

                // Where the road runs within reach of every tile the lake touches (footprint + a tile diagonal + margin).
                double zone = radius + 380.0 + 40.0;
                double flatFrom = centre, flatTo = centre;
                for (double s = centre - 3000.0; s <= centre + 3000.0; s += 8.0)
                {
                    double rz = road.ZAt(s);
                    double rx = road.X(rz);
                    double dx = rx - cx, dz = rz - cz;
                    if (dx * dx + dz * dz > zone * zone) continue;
                    flatFrom = Math.Min(flatFrom, s - 8.0);
                    flatTo = Math.Max(flatTo, s + 8.0);
                }

                var lake = new Place
                {
                    FlatFrom = Math.Min(flatFrom, centre - halfLength - 40.0),
                    FlatTo = Math.Max(flatTo, centre + halfLength + 40.0),
                    Kind = PlaceKind.Lake,
                    Id = MakeId(PlaceKind.Lake, r.Index * 4 + accepted.Count),
                    S = Math.Round(centre, 1),
                    S0 = centre - halfLength,
                    S1 = centre + halfLength,
                    Side = side,
                    HalfLength = (float)halfLength,
                    HalfWidth = halfWidth,
                    ShoreOffset = offset,
                    Region = r.Index,
                    Name = SettlementNames.LakeAt(p.seed, r.Index * 4 + accepted.Count),
                };
                accepted.Add((lake, cx, cz, radius));
            }

            accepted.Sort((a, b) => a.lake.S.CompareTo(b.lake.S));
            foreach (var a in accepted) list.Add(a.lake);
        }

        /// <summary>Appends villages, hamlets and lakes of the regions overlapping [s0, s1] (lakes by their shore range).</summary>
        public void RegionPlaces(double s0, double s1, List<Place> results)
        {
            Region r = Regions.At(s0);
            if (r.Index > long.MinValue) r = Regions.Get(r.Index - 1);
            for (; r.Start <= s1 + 1000.0; r = Regions.Get(r.Index + 1))
            {
                foreach (Place place in PlacesOf(r.Index))
                {
                    double from = place.Kind == PlaceKind.Lake ? place.S0 : place.S;
                    double to = place.Kind == PlaceKind.Lake ? place.S1 : place.S;
                    if (to >= s0 && from <= s1) results.Add(place);
                }
            }
        }

        /// <summary>Level stretches of road overlapping [s0, s1] (settlements and lake shores), unordered.</summary>
        public void FlatZones(double s0, double s1, List<FlatZone> results)
        {
            var places = new List<Place>();
            Inns(s0 - 300.0, s1 + 300.0, places);
            RegionPlaces(s0 - 3300.0, s1 + 3300.0, places);
            foreach (Place place in places)
            {
                double from, to;
                if (place.Kind == PlaceKind.Lake)
                {
                    from = place.FlatFrom;
                    to = place.FlatTo;
                }
                else
                {
                    double half = place.Kind == PlaceKind.Inn ? InnFlat : place.Radius + 30.0;
                    from = place.S - half;
                    to = place.S + half;
                }

                if (to >= s0 && from <= s1) results.Add(new FlatZone { From = from, To = to });
            }
        }
    }
}
