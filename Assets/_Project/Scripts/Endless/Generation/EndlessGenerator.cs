using System;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// The endless road as a pure function of the seed (<see cref="IEndlessGenerator"/>). Construction is cheap (no tables are
    /// built until asked for); the road, region, place and elevation tables grow in fixed steps outward from the start and
    /// are cached, so every answer is independent of call order and thread. See <c>Docs/ENDLESS_ROAD.md</c>.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public sealed class EndlessGenerator : IEndlessGenerator
    {
        /// <summary>Fraction of the height range above which no broadleaf trees grow (pines thin out above it).</summary>
        public const float TreeLine = 0.55f;

        private readonly TileGenerator tiles;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, Waypoint[]> regionWaypoints =
            new System.Collections.Concurrent.ConcurrentDictionary<long, Waypoint[]>();
        private readonly Func<long, Waypoint[]> buildRegionWaypoints;

        /// <summary>
        /// Optional diagnostics: called with (stage, milliseconds) for every stage of every tile (heights, cover, dressings,
        /// slots, scatter, splat, details). Null by default; must be thread-safe when set.
        /// </summary>
        public Action<string, double> StageTimer { get; set; }

        /// <summary>Creates the world for <paramref name="p"/> with the palette described by <paramref name="keys"/>.</summary>
        [UnityEngine.Scripting.Preserve]
        public EndlessGenerator(EndlessParams p, PaletteKeys keys)
        {
            Params = p ?? throw new ArgumentNullException(nameof(p));
            Keys = keys ?? PaletteKeys.Default;
            Curve = new RoadCurve(p.seed);
            Regions = new RegionSequence(p);
            Plan = new JourneyPlan(p, Regions, Curve);
            Profile = new ElevationProfile(p, Regions, Plan);
            Terrain = new TerrainField(p, Curve, Regions, Plan, Profile);
            Features = new FeaturePlan(p, Curve, Regions, Plan, Profile, Terrain);
            Dressings = new DressingCache(p, Keys, Curve, Profile, Terrain);
            Fields = new FieldMosaic(p, Curve, Regions, Plan, Terrain);
            buildRegionWaypoints = BuildRegionWaypoints;
            tiles = new TileGenerator(p, Keys, Curve, Regions, Plan, Profile, Terrain, Features, Dressings, Fields, this);
        }

        /// <inheritdoc />
        public EndlessParams Params { get; }

        /// <summary>Palette keys the props are planned for.</summary>
        public PaletteKeys Keys { get; }

        /// <summary>The road in plan.</summary>
        public RoadCurve Curve { get; }

        /// <summary>The region sequence.</summary>
        public RegionSequence Regions { get; }

        /// <summary>Inns, villages, lakes and level stretches.</summary>
        public JourneyPlan Plan { get; }

        /// <summary>The road's height profile.</summary>
        public ElevationProfile Profile { get; }

        /// <summary>The global terrain function.</summary>
        public TerrainField Terrain { get; }

        /// <summary>Viewpoints, camps and cols.</summary>
        public FeaturePlan Features { get; }

        /// <summary>Cached dressings of places.</summary>
        public DressingCache Dressings { get; }

        /// <summary>The farmland patchwork.</summary>
        public FieldMosaic Fields { get; }

        /// <inheritdoc />
        public RoadPoint Road(double s)
        {
            double z = Curve.ZAt(s);
            double x = Curve.X(z, out double slope, out _);
            float h = Profile.Height(s, out float grade);
            return new RoadPoint
            {
                S = s,
                X = x,
                Z = z,
                Height = h,
                HeadingDeg = (float)(Math.Atan(slope) * 180.0 / Math.PI),
                Grade = grade,
            };
        }

        /// <inheritdoc />
        public double NearestS(double x, double z, double hintS)
        {
            // The road is the graph of a function of z: the nearest point is found from the point itself; the hint only
            // breaks ties far from the road (where two stretches can be about equally near).
            double zeta = Curve.Nearest(x, z, out double d);
            double best = Curve.S(zeta);
            if (!double.IsNaN(hintS) && !double.IsInfinity(hintS))
            {
                double hz = Curve.Nearest(x, z, out double dh, Curve.ZAt(hintS));
                if (dh < d - 1e-6) best = Curve.S(hz);
            }

            return best;
        }

        /// <inheritdoc />
        public RegionInfo Region(double s) => Regions.At(s).Info;

        /// <inheritdoc />
        public void Waypoints(double sFrom, double sTo, List<Waypoint> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            // Region by region from cached, immutable arrays (sorted by road distance): no allocation once a region is cached.
            Region r = Regions.At(sFrom);
            while (r.Start <= sTo)
            {
                Waypoint[] list = regionWaypoints.GetOrAdd(r.Index, buildRegionWaypoints);
                for (int i = 0; i < list.Length; i++)
                {
                    Waypoint w = list[i];
                    if (w.S >= sFrom && w.S <= sTo) results.Add(w);
                }

                r = Regions.Get(r.Index + 1);
            }
        }

        /// <summary>Every waypoint whose road distance lies in region <paramref name="index"/>, sorted.</summary>
        private Waypoint[] BuildRegionWaypoints(long index)
        {
            Region region = Regions.Get(index);
            double sFrom = region.Start, sTo = region.End;
            var list = new List<Waypoint>();
            var places = new List<Place>();
            Plan.Inns(sFrom, sTo, places);
            Plan.RegionPlaces(sFrom, sTo, places);
            foreach (Place place in places)
            {
                if (place.S < sFrom || place.S >= sTo) continue;
                if (place.Kind == PlaceKind.Lake)
                {
                    LakeInfo lake = Terrain.Lake(place);
                    list.Add(new Waypoint
                    {
                        Id = place.Id, Kind = WaypointKind.Lake, Name = place.Name, S = place.S, X = lake.CentreX, Z = lake.CentreZ,
                        Height = lake.Level, Radius = (float)lake.Radius, RoadHeadingDeg = Heading(place.S),
                    });
                    continue;
                }

                list.Add(Settlement(place));
            }

            var features = new List<FeaturePlace>();
            Features.Around(sFrom, sTo, features);
            foreach (FeaturePlace f in features)
            {
                Place place = f.Place;
                if (place.S < sFrom || place.S >= sTo) continue;
                RoadPoint at = Road(place.S);
                bool onRoad = place.Kind == PlaceKind.Pass;
                double x = onRoad ? at.X : f.X, z = onRoad ? at.Z : f.Z;
                list.Add(new Waypoint
                {
                    Id = place.Id, Kind = (WaypointKind)(int)place.Kind, Name = place.Name, S = place.S, X = x, Z = z,
                    Height = onRoad ? at.Height : Terrain.Height(x, z), Radius = place.Kind == PlaceKind.Camp ? 6.5f : place.Kind == PlaceKind.Viewpoint ? 12f : 0f,
                    RoadHeadingDeg = at.HeadingDeg,
                });
            }

            // Milestones every 500 m (none inside a settlement), on the right of the road.
            var near = new List<Place>();
            Plan.Inns(sFrom - 200.0, sTo + 200.0, near);
            Plan.RegionPlaces(sFrom - 200.0, sTo + 200.0, near);
            for (long k = (long)Math.Ceiling(sFrom / JourneyPlan.MilestoneSpacing); k * JourneyPlan.MilestoneSpacing < sTo; k++)
            {
                if (k == 0) continue;
                double s = k * JourneyPlan.MilestoneSpacing;
                bool inside = false;
                foreach (Place place in near)
                {
                    if (place.IsSettlement && Math.Abs(place.S - s) < place.Radius + 20.0) inside = true;
                }

                if (inside) continue;
                RoadPoint at = Road(s);
                double rad = at.HeadingDeg * Math.PI / 180.0;
                double off = Params.roadWidth * 0.5 + 8.0 + 0.9;
                double x = at.X + Math.Cos(rad) * off, z = at.Z - Math.Sin(rad) * off;
                list.Add(new Waypoint
                {
                    Id = JourneyPlan.MakeId(PlaceKind.Milestone, k), Kind = WaypointKind.Milestone, S = s, X = x, Z = z,
                    Height = Terrain.Height(x, z), RoadHeadingDeg = at.HeadingDeg,
                });
            }

            list.Sort((a, b) => a.S != b.S ? a.S.CompareTo(b.S) : a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.Id.CompareTo(b.Id));
            return list.ToArray();
        }

        private Waypoint Settlement(Place place)
        {
            RoadPoint at = Road(place.S);
            return new Waypoint
            {
                Id = place.Id,
                Kind = (WaypointKind)(int)place.Kind,
                Name = place.Name,
                S = place.S,
                X = at.X,
                Z = at.Z,
                Height = at.Height,
                Radius = place.Radius,
                RoadHeadingDeg = at.HeadingDeg,
            };
        }

        private float Heading(double s)
        {
            double z = Curve.ZAt(s);
            Curve.X(z, out double slope, out _);
            return (float)(Math.Atan(slope) * 180.0 / Math.PI);
        }

        /// <inheritdoc />
        public float Height(double x, double z) => Terrain.Height(x, z);

        /// <inheritdoc />
        public float WaterHeight(double x, double z) => Terrain.WaterHeight(x, z);

        /// <inheritdoc />
        public TileBlueprint GenerateTile(TileCoord coord, TileLod lod) => tiles.Generate(coord, lod);
    }
}
