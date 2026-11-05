using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>The finite world's dressing of one place, in the local coordinates of its own window.</summary>
    public sealed class Dressing
    {
        /// <summary>Absolute position of the window's local origin.</summary>
        public double X0, Z0;
        /// <summary>Props (local positions, absolute y).</summary>
        public readonly List<PropPlacement> Props = new List<PropPlacement>();
        /// <summary>Occupied circles (x, radius, z), local.</summary>
        public readonly List<Vector3> Circles = new List<Vector3>();
        /// <summary>Crop fields, local.</summary>
        public readonly List<FieldPatch> Fields = new List<FieldPatch>();
        /// <summary>The settlement disc (local), or null.</summary>
        public SettlementSite Site;
        /// <summary>
        /// True when the ground paint and grass treat <see cref="Site"/> as a dirt square (inns); composed villages paint only
        /// their <see cref="Paths"/> and <see cref="Yards"/> and keep the grass.
        /// </summary>
        public bool PaintSite = true;
        /// <summary>Trees the dressing plants (orchards, the green), local.</summary>
        public readonly List<TreePlacement> Trees = new List<TreePlacement>();
        /// <summary>Worn paths (x0, z0, x1, z1), local.</summary>
        public readonly List<Vector4> Paths = new List<Vector4>();
        /// <summary>Trodden patches (x, radius, z), local.</summary>
        public readonly List<Vector3> Yards = new List<Vector3>();
        /// <summary>Centre of the place (absolute) and a radius containing everything the dressing placed.</summary>
        public double CentreX, CentreZ, Reach;
        /// <summary>The place's spots for its people (doors, the well, benches, gates, work places), local; empty for the finite dresser's places.</summary>
        public readonly List<MedievalZen.People.PlaceAnchor> Anchors = new List<MedievalZen.People.PlaceAnchor>();
        /// <summary>Inns: the archetype that stands (<see cref="InnArchetype"/>), or -1 (not an inn, or the shared dresser's).</summary>
        public int InnStyle = -1;
        /// <summary>Cobbled strips (local segment and width): a walled city's main street, square and lanes, painted on the cobblestone channel.</summary>
        public readonly List<(float x0, float z0, float x1, float z1, float width)> Cobbles = new List<(float, float, float, float, float)>();
        /// <summary>A walled city's numbers (see <see cref="CityComposer"/>), or null.</summary>
        public CityStats City;
    }

    /// <summary>
    /// Dresses places with the finite world's composers, each in a window of its own centred on it (so the result does not
    /// depend on which tile asked first): inns, hamlets and villages with <see cref="SettlementDresser"/> (the refuge at a
    /// col with its mountain-inn layout) and their crop fields with <c>ScatterPlanner.PlanFields</c>; camps, viewpoints
    /// and the big cairn at a col with the countryside feature composers. Results are cached (lazily, one computation per
    /// place even under concurrency) and evicted when far from recent requests; recomputation gives the same result.
    /// </summary>
    public sealed class DressingCache
    {
        private const int Capacity = 384;

        private readonly EndlessParams p;
        private readonly PaletteKeys keys;
        private readonly RoadCurve road;
        private readonly ElevationProfile profile;
        private readonly TerrainField field;
        private readonly ConcurrentDictionary<int, Lazy<Dressing>> cache = new ConcurrentDictionary<int, Lazy<Dressing>>();
        private readonly ConcurrentDictionary<int, double> lastS = new ConcurrentDictionary<int, double>();
        private long recentS;

        /// <summary>Creates the cache.</summary>
        public DressingCache(EndlessParams p, PaletteKeys keys, RoadCurve road, ElevationProfile profile, TerrainField field)
        {
            this.p = p;
            this.keys = keys;
            this.road = road;
            this.profile = profile;
            this.field = field;
        }

        /// <summary>Dressing of a settlement place (inn, hamlet, village).</summary>
        public Dressing Settlement(Place place) => Get(place.Id, place.S, () => DressSettlement(place));

        /// <summary>Dressing of a feature place (camp, viewpoint, the big cairn at a col).</summary>
        public Dressing Feature(FeaturePlace feature) => Get(feature.Place.Id, feature.Place.S, () => DressFeature(feature));

        /// <summary>Dressing of a landmark (its structure, props, kept-clear circles and path stub; see <see cref="LandmarkDresser"/>).</summary>
        public Dressing Landmark(LandmarkSite landmark, FieldMosaic mosaic) => Get(landmark.Id, landmark.S, () => DressLandmark(landmark, mosaic));

        private Dressing DressLandmark(LandmarkSite landmark, FieldMosaic mosaic)
        {
            GenWindow w = Window(landmark.X, landmark.Z, LandmarkDresser.WindowSize, true);
            var d = new Dressing { X0 = w.X0, Z0 = w.Z0, CentreX = landmark.X, CentreZ = landmark.Z };
            OccupancyMap occupancy = LandmarkDresser.Dress(p, keys, road, field, mosaic, w, landmark, d);
            Finish(d, occupancy, landmark.Id);
            return d;
        }

        private Dressing Get(int id, double s, Func<Dressing> make)
        {
            Interlocked.Exchange(ref recentS, BitConverter.DoubleToInt64Bits(s));
            Lazy<Dressing> lazy = cache.GetOrAdd(id, _ => new Lazy<Dressing>(make, LazyThreadSafetyMode.ExecutionAndPublication));
            lastS[id] = s;
            Dressing result = lazy.Value;
            if (cache.Count > Capacity) Evict();
            return result;
        }

        private void Evict()
        {
            double centre = BitConverter.Int64BitsToDouble(Interlocked.Read(ref recentS));
            var far = new List<(double distance, int id)>();
            foreach (var pair in lastS) far.Add((Math.Abs(pair.Value - centre), pair.Key));
            far.Sort((a, b) => b.distance.CompareTo(a.distance));
            for (int i = 0; i < far.Count && cache.Count > Capacity * 3 / 4; i++)
            {
                cache.TryRemove(far[i].id, out _);
                lastS.TryRemove(far[i].id, out _);
            }
        }

        private GenWindow Window(double cx, double cz, float size, bool cover, float spacing = 2f)
        {
            double x0 = Math.Floor((cx - size * 0.5) / spacing) * spacing;
            double z0 = Math.Floor((cz - size * 0.5) / spacing) * spacing;
            int cells = (int)(size / spacing);
            float water = field.WaterLevelForSquare(x0, z0, size, out _);
            GenWindow w = GenWindow.Create(p, field, road, x0, z0, cells, spacing, water);
            w.Roads = w.DistanceField(cells, 30f);
            if (cover) w.BuildCover(p, field, cells);
            return w;
        }

        private Dressing DressSettlement(Place place)
        {
            double z = road.ZAt(place.S);
            double x = road.X(z);
            // A walled city plans no crop fields of its own: the farmland patchwork round it keeps out of the wall (FieldPatchwork).
            bool fields = place.Kind != PlaceKind.Inn && !place.City;
            // The dressing itself on a 2 m grid just big enough for it (seated stone pieces need the true ground).
            float size = place.City ? 400f : place.Kind == PlaceKind.Village ? 304f : place.Kind == PlaceKind.Hamlet ? 240f : 176f;
            GenWindow w = Window(x, z, size, false);
            WorldGenParams wp = w.Params.Clone();
            wp.seed = EndlessNoise.Seed(p.seed, 701, place.Id);
            float level = profile.Height(place.S);
            var centre = new Vector3(w.LocalX(x), level, w.LocalZ(z));

            // A small network: the settlement node and the road 200 m either way.
            var builder = new RoadNetworkBuilder();
            RoadNode node = builder.AddNode(centre);
            Settlement settlement = builder.AddSettlement(place.Name ?? "Inn", place.DressAs, node, SettlementSite.RadiusFor(place.DressAs));
            AddLeg(builder, w, node, place.S, -1.0);
            AddLeg(builder, w, node, place.S, 1.0);
            RoadNetwork network = builder.Build();

            var d = new Dressing { X0 = w.X0, Z0 = w.Z0, CentreX = x, CentreZ = z };
            var occupancy = new OccupancyMap(w.Size);
            // The stream channels near the place stay clear.
            var streams = new List<StreamCourse>();
            field.Streams?.Overlapping(w.X0, w.Z0, w.Size, 20.0, streams);
            TileStreams.Occupy(w, streams, occupancy);
            // The road through the place as a 2 m polyline, the place's centre at index 90.
            var line = new List<Vector3>();
            for (int k = -90; k <= 90; k++)
            {
                double sz = road.ZAt(place.S + 2.0 * k);
                line.Add(new Vector3(w.LocalX(road.X(sz)), 0f, w.LocalZ(sz)));
            }

            if (place.Kind == PlaceKind.Inn)
            {
                // The endless road's own inn, by archetype (see InnStyles, InnComposer); the shared dresser only when the palette has no tavern or hut.
                int stream = place.Refuge ? InnComposer.RefugeStream : InnComposer.Stream;
                var inn = new SettlementKit(wp, w.Map, w.Roads, keys, occupancy, d.Props, line, 90, new System.Random(EndlessNoise.Seed(p.seed, stream, place.Id)));
                InnSite innSite = field.Plan != null ? InnStyles.For(p, field.Plan, road, field, place)
                    : new InnSite { Archetype = place.Refuge ? InnArchetype.Refuge : InnArchetype.Coaching };
                if (InnComposer.Compose(inn, innSite, out InnArchetype built))
                {
                    TakeKit(d, inn);
                    d.PaintSite = false;
                    d.InnStyle = (int)built;
                }
                else
                {
                    d.Props.Clear();
                    SettlementDresser.Dress(wp, w.Map, network, w.Roads, keys, occupancy, d.Props, place.Refuge ? settlement.Id : -1);
                }
            }
            else if (place.City)
            {
                // A walled city in a village's slot (see CityComposer, JourneyPlan.IsCitySlot).
                var kit = new SettlementKit(wp, w.Map, w.Roads, keys, occupancy, d.Props, line, 90, new System.Random(EndlessNoise.Seed(p.seed, CityComposer.Stream, place.Id)));
                var cobbles = new List<(Vector3 a, Vector3 b, float width)>();
                // The hill the terrain raised (TerrainField's disc), in the window's frame.
                CityHill hill = CityHill.For(p.seed, place.Id, w.LocalX(x), w.LocalZ(z), road.Heading(z));
                hill.Level = field.Disc(place).Level;   // the composer seats things on the hill's planned surface above the disc's level
                d.City = CityComposer.Compose(kit, hill, cobbles);
                foreach (var (a, b, width) in cobbles) d.Cobbles.Add((a.x, a.z, b.x, b.z, width));
                TakeKit(d, kit);
                d.PaintSite = false;
            }
            else
            {
                // Hamlets and villages: the endless road's own composition along the road (see VillageComposer).
                var kit = new SettlementKit(wp, w.Map, w.Roads, keys, occupancy, d.Props, line, 90, new System.Random(EndlessNoise.Seed(p.seed, 1090, place.Id)));
                VillageComposer.Compose(kit, place.Kind == PlaceKind.Village, place.Radius, VillageComposer.PlanFor(p.seed, field.Plan, place));
                TakeKit(d, kit);
                d.PaintSite = false;
            }
            var site = new SettlementSite(place.Name ?? "Inn", place.DressAs, centre);
            d.Site = site;
            if (fields)
            {
                // Crop fields reach ~250 m out: plan them on a coarser 4 m window around the same centre, seeing the dressing.
                GenWindow fw = Window(x, z, 512f, true, 4f);
                WorldGenParams fp = fw.Params.Clone();
                fp.seed = wp.seed;
                float ox = (float)(w.X0 - fw.X0), oz = (float)(w.Z0 - fw.Z0);
                var fieldOccupancy = new OccupancyMap(fw.Size);
                for (int i = 0; i < occupancy.Count; i++)
                {
                    Vector3 c = occupancy.Circle(i);
                    fieldOccupancy.Add(new Vector3(c.x + ox, 0f, c.z + oz), c.y);
                }

                var fieldSite = new SettlementSite(site.Name, site.Type, new Vector3(centre.x + ox, level, centre.z + oz));
                var fieldProps = new List<PropPlacement>();
                List<FieldPatch> patches = ScatterPlanner.PlanFields(fp, fw.Map, fw.Roads, fw.Cover, new List<SettlementSite> { fieldSite }, fieldOccupancy, keys, fieldProps);
                foreach (FieldPatch patch in patches)
                {
                    FieldPatch moved = patch;
                    moved.center = new Vector3(patch.center.x - ox, patch.center.y, patch.center.z - oz);
                    d.Fields.Add(moved);
                }

                foreach (PropPlacement prop in fieldProps)
                {
                    PropPlacement moved = prop;
                    moved.position = new Vector3(prop.position.x - ox, prop.position.y, prop.position.z - oz);
                    d.Props.Add(moved);
                }

                for (int i = occupancy.Count; i < fieldOccupancy.Count; i++)
                {
                    Vector3 c = fieldOccupancy.Circle(i);
                    occupancy.Add(new Vector3(c.x - ox, 0f, c.z - oz), c.y);
                }
            }

            Finish(d, occupancy, place.Id);
            return d;
        }

        private static void TakeKit(Dressing d, SettlementKit kit)
        {
            d.Trees.AddRange(kit.Trees);
            d.Paths.AddRange(kit.Paths);
            d.Yards.AddRange(kit.Yards);
            d.Anchors.AddRange(kit.Anchors);
        }

        private void AddLeg(RoadNetworkBuilder builder, GenWindow w, RoadNode node, double s, double dir)
        {
            var points = new List<Vector3> { node.Position };
            for (double t = 2.0; t <= 200.0; t += 2.0)
            {
                double sz = road.ZAt(s + dir * t);
                double sx = road.X(sz);
                points.Add(new Vector3(w.LocalX(sx), profile.Height(s + dir * t), w.LocalZ(sz)));
            }

            RoadNode end = builder.AddNode(points[points.Count - 1]);
            builder.AddSegment(node, end, points);
        }

        private Dressing DressFeature(FeaturePlace f)
        {
            Place place = f.Place;
            double z = road.ZAt(place.S);
            double rx = road.X(z, out double slope, out _);
            bool onRoad = place.Kind == PlaceKind.Pass;
            double cx = onRoad ? rx : f.X;
            double cz = onRoad ? z : f.Z;
            GenWindow w = Window(cx, cz, 112f, true);
            WorldGenParams wp = w.Params.Clone();
            wp.seed = EndlessNoise.Seed(p.seed, 702, place.Id);
            var d = new Dressing { X0 = w.X0, Z0 = w.Z0, CentreX = cx, CentreZ = cz };
            var occupancy = new OccupancyMap(w.Size);
            var rng = new System.Random(EndlessNoise.Seed(p.seed, 703, place.Id));
            var ctx = new ScatterPlanner.FeatureContext(wp, w.Map, w.Roads, w.Cover, new List<SettlementSite>(), new List<FieldPatch>(), occupancy, keys, d.Props, rng);

            double len = Math.Sqrt(1.0 + slope * slope);
            var tangent = new Vector3((float)(slope / len), 0f, (float)(1.0 / len));
            var right = new Vector3(tangent.z, 0f, -tangent.x);
            var local = new Vector3(w.LocalX(cx), 0f, w.LocalZ(cz));
            Vector3 side = right * place.Side;
            switch (place.Kind)
            {
                case PlaceKind.Camp:
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 c = local + side * (2.5f * k);
                        if (!ctx.Clear(c, 6.5f, 0.3f)) continue;
                        ScatterPlanner.PlaceCamp(ctx, c, side, tangent);
                        break;
                    }

                    break;
                case PlaceKind.Viewpoint:
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 c = local + side * (3f * k);
                        if (!ctx.Clear(c, 6f, 0.4f)) continue;
                        ScatterPlanner.DressViewpoint(ctx, c, new Vector3((float)f.DirX, 0f, (float)f.DirZ));
                        break;
                    }

                    break;
                case PlaceKind.Pass:
                    ScatterPlanner.PlaceCairnNear(ctx, local, 3.5f);
                    break;
            }

            Finish(d, occupancy, place.Id);
            return d;
        }

        private static void Finish(Dressing d, OccupancyMap occupancy, int owner)
        {
            for (int i = 0; i < d.Props.Count; i++)
            {
                PropPlacement prop = d.Props[i];
                prop.settlementId = owner;
                d.Props[i] = prop;
            }

            double reach = 0.0;
            for (int i = 0; i < occupancy.Count; i++)
            {
                Vector3 c = occupancy.Circle(i);
                d.Circles.Add(c);
                double dx = d.X0 + c.x - d.CentreX, dz = d.Z0 + c.z - d.CentreZ;
                reach = Math.Max(reach, Math.Sqrt(dx * dx + dz * dz) + c.y);
            }

            foreach (PropPlacement prop in d.Props)
            {
                double dx = d.X0 + prop.position.x - d.CentreX, dz = d.Z0 + prop.position.z - d.CentreZ;
                reach = Math.Max(reach, Math.Sqrt(dx * dx + dz * dz) + 4.0);
            }

            foreach (TreePlacement tree in d.Trees)
            {
                double dx = d.X0 + tree.position.x - d.CentreX, dz = d.Z0 + tree.position.z - d.CentreZ;
                reach = Math.Max(reach, Math.Sqrt(dx * dx + dz * dz) + 4.0);
            }

            foreach (FieldPatch fieldPatch in d.Fields)
            {
                double dx = d.X0 + fieldPatch.center.x - d.CentreX, dz = d.Z0 + fieldPatch.center.z - d.CentreZ;
                reach = Math.Max(reach, Math.Sqrt(dx * dx + dz * dz) + fieldPatch.halfExtents.magnitude + 2.0);
            }

            if (d.Site != null) reach = Math.Max(reach, d.Site.Radius + TerrainShaper.SettlementBlend);
            d.Reach = reach;
        }
    }
}
