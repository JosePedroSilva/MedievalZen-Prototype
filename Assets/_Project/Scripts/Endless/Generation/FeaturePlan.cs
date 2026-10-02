using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MedievalZen.Endless.Generation
{
    /// <summary>A feature place with its centre in absolute coordinates.</summary>
    public sealed class FeaturePlace
    {
        /// <summary>The place (kind, id, road distance, side, name).</summary>
        public Place Place;
        /// <summary>Centre of the feature (absolute).</summary>
        public double X, Z;
        /// <summary>Direction the feature faces (viewpoints: the view), horizontal unit vector.</summary>
        public double DirX, DirZ;
    }

    /// <summary>
    /// Places chosen with the heights known: the col of each pass, a viewpoint per downs region (at the road's highest point,
    /// on the side that falls away) and per pass (on the way down, looking back down the valley), a camp in most meadow,
    /// forest and lake-country regions and two-thirds of the way up each pass.
    /// Cached per region; a pure function of the seed. Thread-safe.
    /// </summary>
    public sealed class FeaturePlan
    {
        private readonly EndlessParams p;
        private readonly RoadCurve road;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly ElevationProfile profile;
        private readonly TerrainField field;
        private readonly ConcurrentDictionary<long, FeaturePlace[]> cache = new ConcurrentDictionary<long, FeaturePlace[]>();

        /// <summary>Creates the plan.</summary>
        public FeaturePlan(EndlessParams p, RoadCurve road, RegionSequence regions, JourneyPlan plan, ElevationProfile profile, TerrainField field)
        {
            this.p = p;
            this.road = road;
            this.regions = regions;
            this.plan = plan;
            this.profile = profile;
            this.field = field;
        }

        /// <summary>Features of region <paramref name="index"/>, ordered by road distance.</summary>
        public FeaturePlace[] Of(long index) => cache.GetOrAdd(index, Compute);

        /// <summary>Appends features of the regions overlapping [s0, s1] whose road distance lies within it.</summary>
        public void Around(double s0, double s1, List<FeaturePlace> results)
        {
            Region r = regions.At(s0);
            for (; r.Start <= s1; r = regions.Get(r.Index + 1))
            {
                foreach (FeaturePlace f in Of(r.Index))
                {
                    if (f.Place.S >= s0 && f.Place.S <= s1) results.Add(f);
                }
            }
        }

        private FeaturePlace[] Compute(long index)
        {
            Region r = regions.Get(index);
            var list = new List<FeaturePlace>();
            var avoid = new List<Place>();
            plan.Inns(r.Start - 400.0, r.End + 400.0, avoid);
            plan.RegionPlaces(r.Start - 400.0, r.End + 400.0, avoid);

            switch (r.Kind)
            {
                case RegionKind.HighlandPass:
                {
                    list.Add(Make(PlaceKind.Pass, index, r.Col, 0, 0.0, SettlementNamesPass(index)));
                    // Camp two-thirds of the way up the near side.
                    double camp = r.Start + (r.Col - r.Start) * 0.66;
                    int campSide = Side(index, 601);
                    if (Clear(camp, avoid, 150.0)) list.Add(Make(PlaceKind.Camp, index * 4 + 1, camp, campSide, 18.5, null));
                    // Viewpoint on the far side, 40 % of the way down, looking back down the valley.
                    double view = r.Col + (r.End - r.Col) * 0.4;
                    if (Clear(view, avoid, 200.0))
                    {
                        FeaturePlace vp = Make(PlaceKind.Viewpoint, index * 4 + 2, view, OpenSide(view), 22.0, MedievalZen.World.SettlementNames.ViewpointAt(p.seed, index));
                        Facing(vp, 1.0, 0.6);
                        list.Add(vp);
                    }

                    break;
                }

                case RegionKind.Downs:
                {
                    double best = r.Start + r.Length * 0.5, bestH = double.NegativeInfinity;
                    for (double s = r.Start + r.Length * 0.25; s <= r.End - r.Length * 0.25; s += 50.0)
                    {
                        double h = profile.Height(s) + 0.5 * EndlessNoise.Unit(p.seed, 602, index, (long)s);
                        if (h > bestH && Clear(s, avoid, 250.0))
                        {
                            bestH = h;
                            best = s;
                        }
                    }

                    if (!double.IsNegativeInfinity(bestH))
                    {
                        FeaturePlace vp = Make(PlaceKind.Viewpoint, index * 4 + 2, best, OpenSide(best), 22.0, MedievalZen.World.SettlementNames.ViewpointAt(p.seed, index));
                        Facing(vp, 0.0, 1.0);
                        list.Add(vp);
                    }

                    break;
                }

                case RegionKind.Meadow:
                case RegionKind.Forest:
                case RegionKind.LakeCountry:
                {
                    if (EndlessNoise.Unit(p.seed, 603, index) < 0.8)
                    {
                        for (int attempt = 0; attempt < 8; attempt++)
                        {
                            double s = r.Start + r.Length * (0.2 + 0.6 * EndlessNoise.Unit(p.seed, 604, index, attempt));
                            if (!Clear(s, avoid, 250.0)) continue;
                            int side = Side(index * 8 + attempt, 605);
                            foreach (Place lake in avoid)
                            {
                                if (lake.Kind == PlaceKind.Lake && s > lake.S0 - 150.0 && s < lake.S1 + 150.0) side = -lake.Side;
                            }

                            list.Add(Make(PlaceKind.Camp, index * 4 + 1, s, side, 18.5, null));
                            break;
                        }
                    }

                    break;
                }
            }

            list.Sort((a, b) => a.Place.S.CompareTo(b.Place.S));
            return list.ToArray();
        }

        private string SettlementNamesPass(long index) => MedievalZen.World.SettlementNames.PassAt(p.seed, index);

        private int Side(long key, int stream) => EndlessNoise.Unit(p.seed, stream, key) < 0.5 ? -1 : 1;

        private static bool Clear(double s, List<Place> avoid, double distance)
        {
            foreach (Place place in avoid)
            {
                if (place.Kind == PlaceKind.Lake) continue;
                if (Math.Abs(place.S - s) < distance + place.Radius) return false;
            }

            return true;
        }

        /// <summary>The side of the road at s whose ground 60-90 m out is lower (the open side).</summary>
        private int OpenSide(double s)
        {
            double z = road.ZAt(s);
            double x = road.X(z, out double slope, out _);
            double len = Math.Sqrt(1.0 + slope * slope);
            double nx = 1.0 / len, nz = -slope / len;
            double right = 0.0, left = 0.0;
            foreach (double d in new[] { 60.0, 90.0 })
            {
                right += field.Height(x + nx * d, z + nz * d);
                left += field.Height(x - nx * d, z - nz * d);
            }

            return right <= left ? 1 : -1;
        }

        private FeaturePlace Make(PlaceKind kind, long index, double s, int side, double offset, string name)
        {
            double z = road.ZAt(s);
            double x = road.X(z, out double slope, out _);
            double len = Math.Sqrt(1.0 + slope * slope);
            double nx = 1.0 / len, nz = -slope / len;
            var place = new Place
            {
                Kind = kind,
                Id = JourneyPlan.MakeId(kind, index),
                S = Math.Round(s, 1),
                Side = side,
                Name = name,
                Region = regions.At(s).Index,
            };
            return new FeaturePlace
            {
                Place = place,
                X = x + nx * side * offset,
                Z = z + nz * side * offset,
                DirX = slope / len,
                DirZ = 1.0 / len,
            };
        }

        /// <summary>Faces <paramref name="f"/> into a mix of back along the road (<paramref name="back"/>) and out from it (<paramref name="outward"/>).</summary>
        private static void Facing(FeaturePlace f, double back, double outward)
        {
            double tx = f.DirX, tz = f.DirZ;
            double ox = tz * f.Place.Side, oz = -tx * f.Place.Side;   // right-hand normal times side
            double dx = -tx * back + ox * outward, dz = -tz * back + oz * outward;
            double len = Math.Sqrt(dx * dx + dz * dz);
            f.DirX = dx / len;
            f.DirZ = dz / len;
        }
    }
}
