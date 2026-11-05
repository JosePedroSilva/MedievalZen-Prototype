using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>What a landmark is.</summary>
    public enum LandmarkType
    {
        /// <summary>A stone watchtower (Fantasy Kingdom tower, 20.7 m).</summary>
        Tower = 0,
        /// <summary>A chapel with a bell tower (Fantasy Kingdom church, 9.2 / 11.7 m) and a churchyard.</summary>
        Chapel = 1,
        /// <summary>A windmill (Fantasy Kingdom windmill house, sails to 17 m).</summary>
        Mill = 2,
        /// <summary>A ring of standing stones (Meadow rune stones scaled to 3.6-5.4 m).</summary>
        Stones = 3,
        /// <summary>A ruined house or abbey with a stone arch (Fantasy Kingdom ruin, 3-3.5 m walls).</summary>
        Ruin = 4,
    }

    /// <summary>A landmark off the road, in absolute coordinates. Immutable once planned.</summary>
    public sealed class LandmarkSite
    {
        /// <summary>Stable id (<see cref="JourneyPlan.MakeId"/> with <see cref="PlaceKind.Landmark"/>); its props carry it.</summary>
        public int Id;
        /// <summary>What it is.</summary>
        public LandmarkType Type;
        /// <summary>Region it was planned in, and its slot in the region.</summary>
        public long Region;
        /// <summary>Slot index within the region (0 or 1).</summary>
        public int Slot;
        /// <summary>Road distance it was planned from (its waypoint's S).</summary>
        public double S;
        /// <summary>Centre of the structure (absolute).</summary>
        public double X, Z;
        /// <summary>Height of the structure's floor (its pivot), metres.</summary>
        public float Ground;
        /// <summary>Lowest and highest ground under the structure's footprint, metres.</summary>
        public float GroundMin, GroundMax;
        /// <summary>Rotation about Y in degrees.</summary>
        public float Yaw;
        /// <summary>Footprint radius kept clear (trees, scatter), metres.</summary>
        public float Radius;
        /// <summary>Visible height of the structure above its floor, metres.</summary>
        public float Height;
        /// <summary>Generated name.</summary>
        public string Name;
        /// <summary>Distance from the nearest road point, metres.</summary>
        public float Offset;
        /// <summary>Road metres within 150-1600 m of it with a terrain line of sight to its upper part (planning estimate).</summary>
        public float VisibleRoad;
        /// <summary>Height of the site above the ground 45-100 m round it, metres.</summary>
        public float Prominence;
        /// <summary>Prefab variant of the main structure.</summary>
        public int Variant;
    }

    /// <summary>
    /// Where the landmarks stand (endless only): one or two slots per region (about one landmark every 1.6 km of road), each a
    /// kind chosen by region (towers and ruins in the woods, windmills and chapels on farmland, stones and towers on the downs, a
    /// watchtower or stones in a pass), placed on the best site within ±240 m of the slot along the road and 150-650 m to either
    /// side: level enough for the structure, dry, clear of streams, fields, settlements and the road, preferring sites that are
    /// seen from the most road (terrain line of sight from 1.7 m above the road to the structure's upper part, 150-1600 m away)
    /// and stand above the ground round them. Cached per region; a pure function of the seed (randomness streams 1201-1209), so
    /// every tile and the waypoints agree. Thread-safe.
    /// </summary>
    public sealed class LandmarkPlan
    {
        /// <summary>Randomness streams used here (1201-1209).</summary>
        private const int StreamSkip = 1201, StreamType = 1202, StreamTarget = 1203, StreamJitter = 1204, StreamTie = 1205, StreamYaw = 1206, StreamVariant = 1207;

        /// <summary>Nearest a landmark's centre stands to the road centre, metres.</summary>
        public const float MinOffset = 120f;

        /// <summary>Road points within this distance of a landmark count toward its visibility, metres (from <see cref="MinSight"/>).</summary>
        public const float MaxSight = 1600f;

        /// <summary>Road points nearer than this do not count toward visibility (the traveller is beside it, not walking toward it).</summary>
        public const float MinSight = 150f;

        /// <summary>Spacing of the road points the visibility is measured from, metres.</summary>
        public const double SightStep = 75.0;

        private static readonly double[] Offsets = { 150.0, 210.0, 290.0, 390.0, 510.0, 650.0 };
        private static readonly double[] Alongs = { -240.0, -120.0, 0.0, 120.0, 240.0 };

        private readonly EndlessParams p;
        private readonly RoadCurve road;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly ElevationProfile profile;
        private readonly TerrainField field;
        private readonly FieldMosaic mosaic;
        private readonly FeaturePlan features;
        private readonly Planting planting;
        private readonly ConcurrentDictionary<long, LandmarkSite[]> cache = new ConcurrentDictionary<long, LandmarkSite[]>();
        private readonly Func<long, LandmarkSite[]> compute;

        /// <summary>Creates the plan.</summary>
        public LandmarkPlan(EndlessParams p, RoadCurve road, RegionSequence regions, JourneyPlan plan, ElevationProfile profile, TerrainField field,
            FieldMosaic mosaic, FeaturePlan features, PaletteKeys keys)
        {
            this.p = p;
            this.road = road;
            this.regions = regions;
            this.plan = plan;
            this.profile = profile;
            this.field = field;
            this.mosaic = mosaic;
            this.features = features;
            planting = new Planting(p, keys ?? PaletteKeys.Default, road, regions, plan, profile, field, mosaic, features);
            compute = Compute;
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>Footprint radius kept clear round a landmark of <paramref name="type"/>, metres.</summary>
        public static float RadiusOf(LandmarkType type)
        {
            switch (type)
            {
                case LandmarkType.Tower: return 9f;
                case LandmarkType.Chapel: return 13f;
                case LandmarkType.Mill: return 11f;
                case LandmarkType.Stones: return 15f;
                default: return 13f;
            }
        }

        /// <summary>Half the structure's plan size: the ground under this radius must be nearly level, metres.</summary>
        public static float BaseRadiusOf(LandmarkType type)
        {
            switch (type)
            {
                case LandmarkType.Tower: return 5f;
                case LandmarkType.Chapel: return 6f;
                case LandmarkType.Mill: return 5.5f;
                case LandmarkType.Stones: return 11f;
                default: return 5f;
            }
        }

        /// <summary>
        /// Depth of the structure's foundation below its pivot (measured: the church and windmill presets reach 2.51 m below, the
        /// tower 0.75 m, the ruins 1.3 m; the stones are seated one by one).
        /// </summary>
        public static float FoundationOf(LandmarkType type)
        {
            switch (type)
            {
                case LandmarkType.Tower: return 0.75f;
                case LandmarkType.Chapel: return 2.51f;
                case LandmarkType.Mill: return 2.51f;
                case LandmarkType.Stones: return 3f;
                default: return 1.3f;
            }
        }

        /// <summary>
        /// Most rise of the ground under the base a site may have, metres. The floor sits at most <see cref="FoundationOf"/> − 0.25 m
        /// above the lowest ground, so on the uphill side a tower or a ruin is sunk into the slope by up to a metre (it reads as built
        /// into the hillside); the chapel and the windmill stand on their 2.5 m foundations.
        /// </summary>
        public static float FlatnessOf(LandmarkType type)
        {
            switch (type)
            {
                case LandmarkType.Stones: return 2.6f;
                case LandmarkType.Tower: return 1.5f;
                case LandmarkType.Ruin: return 1.5f;
                default: return FoundationOf(type) - 0.45f;
            }
        }

        /// <summary>Height of the structure above its floor (measured), metres.</summary>
        public static float HeightOf(LandmarkType type, int variant)
        {
            switch (type)
            {
                case LandmarkType.Tower: return 20.7f;
                case LandmarkType.Chapel: return variant % 2 == 1 ? 11.7f : 9.2f;
                case LandmarkType.Mill: return 17f;
                case LandmarkType.Stones: return 4.5f;
                default: return 3.4f;
            }
        }

        /// <summary>The prop family of a landmark's main structure (null for the stones, which are many rune stones).</summary>
        public static string RoleOf(LandmarkType type)
        {
            switch (type)
            {
                case LandmarkType.Tower: return PropRole.LandmarkTower;
                case LandmarkType.Chapel: return PropRole.LandmarkChapel;
                case LandmarkType.Mill: return PropRole.LandmarkMill;
                case LandmarkType.Ruin: return PropRole.LandmarkRuin;
                default: return null;
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Landmarks of region <paramref name="index"/>, by road distance.</summary>
        public LandmarkSite[] Of(long index) => cache.GetOrAdd(index, compute);

        /// <summary>Appends the landmarks planned from road distances in [s0, s1].</summary>
        public void Around(double s0, double s1, List<LandmarkSite> results)
        {
            for (Region r = regions.At(s0); r.Start <= s1; r = regions.Get(r.Index + 1))
            {
                foreach (LandmarkSite l in Of(r.Index))
                {
                    if (l.S >= s0 && l.S <= s1) results.Add(l);
                }
            }
        }

        /// <summary>Appends the landmarks whose footprint (grown by <paramref name="margin"/>) overlaps the absolute square [x0, x0 + size] × [z0, z0 + size].</summary>
        public void Overlapping(double x0, double z0, double size, double margin, List<LandmarkSite> results)
        {
            // A landmark stands at most 650 m (+ jitter) to the side of the road point it was planned from.
            double s0 = road.S(z0 - 900.0), s1 = road.S(z0 + size + 900.0);
            for (Region r = regions.At(s0 - 300.0); r.Start <= s1 + 300.0; r = regions.Get(r.Index + 1))
            {
                foreach (LandmarkSite l in Of(r.Index))
                {
                    double reach = l.Radius + margin;
                    if (l.X + reach < x0 || l.X - reach > x0 + size || l.Z + reach < z0 || l.Z - reach > z0 + size) continue;
                    results.Add(l);
                }
            }
        }

        // ------------------------------------------------------------------ planning

        private LandmarkSite[] Compute(long index)
        {
            Region r = regions.Get(index);
            var list = new List<LandmarkSite>();
            int slots = r.Kind == RegionKind.HighlandPass ? 1 : r.Length >= 2300.0 ? 2 : 1;
            var avoid = new List<Place>();
            plan.Inns(r.Start - 1500.0, r.End + 1500.0, avoid);
            plan.RegionPlaces(r.Start - 1500.0, r.End + 1500.0, avoid);
            var feats = new List<FeaturePlace>();
            features.Around(r.Start - 1500.0, r.End + 1500.0, feats);
            LandmarkType previous = (LandmarkType)(-1);
            for (int k = 0; k < slots; k++)
            {
                if (EndlessNoise.Unit(p.seed, StreamSkip, index, k) < SkipChance(r.Kind)) continue;
                LandmarkType type = PickType(r.Kind, EndlessNoise.Unit(p.seed, StreamType, index, k), previous);
                double span = r.Length / slots;
                double target = r.Start + span * (k + 0.5) + (EndlessNoise.Unit(p.seed, StreamTarget, index, k) - 0.5) * 0.2 * span;
                if (r.Kind == RegionKind.HighlandPass)
                {
                    // In a pass: on the foothills of the climb or the descent (the col itself has its refuge and cairn).
                    bool before = EndlessNoise.Unit(p.seed, StreamTarget, index, 7) < 0.5;
                    target = before ? r.Start + (r.Col - r.Start) * 0.3 : r.Col + (r.End - r.Col) * 0.7;
                }

                target = Math.Max(r.Start + 150.0, Math.Min(r.End - 150.0, target));
                LandmarkSite l = Search(r, k, type, target, avoid, feats);
                if (l == null)
                {
                    // No site suits that kind here (too steep for a tower, unseen): one more try with another kind of the region.
                    LandmarkType other = PickType(r.Kind, EndlessNoise.Unit(p.seed, StreamType, index, k + 16), type);
                    if (other != type) l = Search(r, k, type = other, target, avoid, feats);
                }

                if (l == null) continue;
                list.Add(l);
                previous = type;
            }

            list.Sort((a, b) => a.S.CompareTo(b.S));
            return list.ToArray();
        }

        private static double SkipChance(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.Downs: return 0.05;
                case RegionKind.HighlandPass: return 0.3;
                default: return 0.1;
            }
        }

        /// <summary>Weights of (tower, chapel, mill, stones, ruin) by region.</summary>
        private static double[] Weights(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.Meadow: return new[] { 0.2, 0.25, 0.2, 0.25, 0.1 };
                case RegionKind.Farmland: return new[] { 0.1, 0.4, 0.45, 0.0, 0.05 };
                case RegionKind.Forest: return new[] { 0.5, 0.2, 0.0, 0.15, 0.15 };
                case RegionKind.LakeCountry: return new[] { 0.3, 0.35, 0.05, 0.2, 0.1 };
                case RegionKind.Downs: return new[] { 0.3, 0.1, 0.25, 0.35, 0.0 };
                default: return new[] { 0.65, 0.0, 0.0, 0.35, 0.0 };
            }
        }

        /// <summary>The kind for a slot: by the region's weights from <paramref name="u"/>, never the region's previous kind.</summary>
        public static LandmarkType PickType(RegionKind kind, double u, LandmarkType previous)
        {
            double[] w = Weights(kind);
            if ((int)previous >= 0 && (int)previous < w.Length) w[(int)previous] = 0.0;
            double total = 0.0;
            foreach (double x in w) total += x;
            if (total <= 0.0) return LandmarkType.Tower;
            double t = u * total;
            for (int i = 0; i < w.Length; i++)
            {
                if (t < w[i]) return (LandmarkType)i;
                t -= w[i];
            }

            for (int i = w.Length - 1; i >= 0; i--)
            {
                if (w[i] > 0.0) return (LandmarkType)i;
            }

            return LandmarkType.Tower;
        }

        private struct Candidate
        {
            public double X, Z, S;
            public int Side;
            public float H, Min, Max, Prominence, Offset;
        }

        private LandmarkSite Search(Region r, int slot, LandmarkType type, double target, List<Place> avoid, List<FeaturePlace> feats)
        {
            float radius = RadiusOf(type), baseR = BaseRadiusOf(type), flat = FlatnessOf(type);
            var good = new List<Candidate>();
            var fields = new List<MosaicField>();
            int n = 0;
            foreach (double along in Alongs)
            {
                foreach (int side in new[] { 1, -1 })
                {
                    foreach (double offset in Offsets)
                    {
                        n++;
                        double ja = (EndlessNoise.Unit(p.seed, StreamJitter, r.Index * 4 + slot, n) - 0.5) * 60.0;
                        double jd = 1.0 + (EndlessNoise.Unit(p.seed, StreamJitter, r.Index * 4 + slot, n + 1000) - 0.5) * 0.2;
                        double s = target + along + ja;
                        if (s < r.Start + 80.0 || s > r.End - 80.0) continue;
                        double z = road.ZAt(s);
                        double x = road.X(z, out double slopeX, out _);
                        double len = Math.Sqrt(1.0 + slopeX * slopeX);
                        double nx = 1.0 / len, nz = -slopeX / len;
                        double d = offset * jd;
                        double cx = x + nx * side * d, cz = z + nz * side * d;
                        if (!Clear(cx, cz, radius, avoid, feats, fields, out float roadDistance)) continue;
                        float h = field.PlanHeight(cx, cz), lo = h, hi = h;
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4.0;
                            float g = field.PlanHeight(cx + Math.Cos(a) * baseR, cz + Math.Sin(a) * baseR);
                            lo = Math.Min(lo, g);
                            hi = Math.Max(hi, g);
                        }

                        if (hi - lo > flat) continue;
                        float ring = 0f;
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4.0 + 0.2;
                            ring += field.PlanHeight(cx + Math.Cos(a) * 45.0, cz + Math.Sin(a) * 45.0) + field.PlanHeight(cx + Math.Cos(a) * 100.0, cz + Math.Sin(a) * 100.0);
                        }

                        float prominence = h - ring / 16f;
                        good.Add(new Candidate { X = cx, Z = cz, S = s, Side = side, H = h, Min = lo, Max = hi, Prominence = prominence, Offset = roadDistance });
                    }
                }
            }

            if (good.Count == 0) return null;
            // The most prominent few are judged by how much road sees them.
            good.Sort((a, b) => b.Prominence != a.Prominence ? b.Prominence.CompareTo(a.Prominence) : a.S.CompareTo(b.S));
            int variant = (int)(EndlessNoise.Unit(p.seed, StreamVariant, r.Index * 4 + slot) * 2.0);
            float height = HeightOf(type, variant);
            double bestScore = double.NegativeInfinity;
            Candidate best = default;
            float bestVisible = 0f;
            for (int i = 0; i < good.Count && i < 8; i++)
            {
                Candidate c = good[i];
                float visible = VisibleRoad(c.X, c.Z, c.H + 0.65f * height, c.S);
                double above = c.H - profile.Height(c.S);   // a site above the road is seen over the trees beside it
                double score = visible + 40.0 * Math.Max(-10.0, Math.Min(15.0, c.Prominence)) + 12.0 * Math.Max(-10.0, Math.Min(40.0, above)) + 30.0 * EndlessNoise.Unit(p.seed, StreamTie, r.Index * 4 + slot, i);
                if (score <= bestScore) continue;
                bestScore = score;
                best = c;
                bestVisible = visible;
            }

            // A landmark nobody sees from the road is not worth its cost (in the woods a glimpse through a clearing is enough).
            if (bestVisible < (r.Kind == RegionKind.Forest || r.Kind == RegionKind.HighlandPass ? 100f : 200f)) return null;

            float foundation = FoundationOf(type);
            float ground = type == LandmarkType.Stones ? best.H : Math.Min(best.Max - 0.05f, best.Min + foundation - 0.25f);
            // Face the road (with a little wander): the doors of the presets face their local +Z or -Z; either reads.
            double zr = road.Nearest(best.X, best.Z, out _);
            double toX = road.X(zr) - best.X, toZ = zr - best.Z;
            float yaw = (float)(Math.Atan2(toX, toZ) * 180.0 / Math.PI + (EndlessNoise.Unit(p.seed, StreamYaw, r.Index * 4 + slot) - 0.5) * 50.0);
            long index = r.Index * 4 + slot;
            return new LandmarkSite
            {
                Id = JourneyPlan.MakeId(PlaceKind.Landmark, index),
                Type = type,
                Region = r.Index,
                Slot = slot,
                S = Math.Round(best.S, 1),
                X = best.X,
                Z = best.Z,
                Ground = ground,
                GroundMin = best.Min,
                GroundMax = best.Max,
                Yaw = yaw,
                Radius = RadiusOf(type),
                Height = height,
                Name = SettlementNames.LandmarkAt(p.seed, index, (int)type),
                Offset = best.Offset,
                VisibleRoad = bestVisible,
                Prominence = best.Prominence,
                Variant = variant,
            };
        }

        /// <summary>
        /// Off the road, out of the water and the lakes' margins, clear of streams, settlements (and the fields round villages),
        /// camps and viewpoints, and of the farmland fields.
        /// </summary>
        private bool Clear(double x, double z, float radius, List<Place> avoid, List<FeaturePlace> feats, List<MosaicField> fields, out float roadDistance)
        {
            road.Nearest(x, z, out double d);
            roadDistance = (float)d;
            if (d < MinOffset) return false;
            if (field.WaterHeight(x, z) > 0f) return false;
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4.0;
                if (field.WaterHeight(x + Math.Cos(a) * (radius + 25.0), z + Math.Sin(a) * (radius + 25.0)) > 0f) return false;
            }

            foreach (Place place in avoid)
            {
                if (place.Kind == PlaceKind.Lake || !place.IsSettlement) continue;
                SettlementDisc disc = field.Disc(place);
                double reach = place.Radius + radius + (place.Kind == PlaceKind.Inn ? 140.0 : 280.0);
                double dx = x - disc.X, dz = z - disc.Z;
                if (dx * dx + dz * dz < reach * reach) return false;
            }

            foreach (FeaturePlace f in feats)
            {
                double dx = x - f.X, dz = z - f.Z;
                if (dx * dx + dz * dz < (radius + 110.0) * (radius + 110.0)) return false;
            }

            if (field.Streams != null && field.Streams.Clearance(x, z, radius + 40.0) < radius + 30.0) return false;
            fields.Clear();
            mosaic.Overlapping(x - radius - 30.0, z - radius - 30.0, 2.0 * radius + 60.0, fields);
            foreach (MosaicField f in fields)
            {
                if (FieldMosaic.Distance(f, x, z) < radius + 12.0) return false;
            }

            return true;
        }

        /// <summary>
        /// Road metres between <see cref="MinSight"/> and <see cref="MaxSight"/> from (x, z) from which a point at height
        /// <paramref name="top"/> above it is in line of sight from 1.7 m above the road: over the terrain and the woods (land cover,
        /// a 9 m canopy where the forest weight passes 0.62), each road point weighted by how open the road is toward the landmark
        /// (the roadside planting's tree lines: open 1, a line on that side 0.35, an avenue 0.25, a tunnel 0).
        /// </summary>
        public float VisibleRoad(double x, double z, float top, double sNear)
        {
            float metres = 0f;
            for (double s = sNear - 1800.0; s <= sNear + 1800.0; s += SightStep)
            {
                double rz = road.ZAt(s);
                double rx = road.X(rz, out double slope, out _);
                double dx = x - rx, dz = z - rz;
                double dist = Math.Sqrt(dx * dx + dz * dz);
                if (dist < MinSight || dist > MaxSight) continue;
                float open = Openness(s, dx * 1.0 - dz * slope);
                if (open <= 0f) continue;
                if (InSight(rx, rz, profile.Height(s) + 1.7f, x, z, top, true)) metres += (float)SightStep * open;
            }

            return metres;
        }

        /// <summary>
        /// The road distance (within ±1800 m of <see cref="LandmarkSite.S"/>, every 25 m, at most <see cref="MaxSight"/> away) with the
        /// best view of <paramref name="l"/> by the same sight lines as <see cref="VisibleRoad"/>: its upper part (65 % of the height)
        /// in sight over the terrain and the woods from 1.7 m above the road, weighted by the road's openness toward it; among those
        /// the one where it looks largest (openness × height / distance). <see cref="LandmarkSite.S"/> when no road point sees it.
        /// Used by the review capture to stand where the landmark can be seen. Pure; not used by the planning itself.
        /// </summary>
        public double BestView(LandmarkSite l)
        {
            if (l == null) return double.NaN;
            float top = l.Ground + 0.65f * l.Height;
            double best = l.S, bestScore = 0.0;
            for (double s = l.S - 1800.0; s <= l.S + 1800.0; s += 25.0)
            {
                double rz = road.ZAt(s);
                double rx = road.X(rz, out double slope, out _);
                double dx = l.X - rx, dz = l.Z - rz;
                double dist = Math.Sqrt(dx * dx + dz * dz);
                if (dist > MaxSight) continue;
                float open = Openness(s, dx * 1.0 - dz * slope);
                if (open <= 0f) continue;
                double score = open * l.Height / Math.Max(dist, 100.0);
                if (score <= bestScore) continue;
                if (!InSight(rx, rz, profile.Height(s) + 1.7f, l.X, l.Z, top, true)) continue;
                bestScore = score;
                best = s;
            }

            return best;
        }

        /// <summary>How open the road at <paramref name="s"/> is toward a point on the side <paramref name="rightDot"/> (positive: right).</summary>
        public float Openness(double s, double rightDot)
        {
            int lining = planting.RoadsideLining(s, out bool tunnel);
            if (tunnel) return 0f;
            int side = rightDot >= 0.0 ? 1 : 2;
            if ((lining & side) == 0) return 1f;
            return lining == 3 ? 0.25f : 0.35f;
        }

        /// <summary>
        /// True when the terrain (and with <paramref name="woods"/> the woods' canopy) does not rise above the straight line from
        /// (ax, ay, az) to (bx, by, bz) (ends excluded).
        /// </summary>
        public bool InSight(double ax, double az, float ay, double bx, double bz, float by, bool woods)
        {
            double dx = bx - ax, dz = bz - az;
            double dist = Math.Sqrt(dx * dx + dz * dz);
            int samples = Math.Max(8, Math.Min(40, (int)(dist / 35.0)));
            for (int i = 1; i < samples; i++)
            {
                double t = (double)i / samples;
                double along = t * dist;
                if (along < 12.0 || dist - along < 14.0) continue;
                float line = ay + (by - ay) * (float)t;
                double px = ax + dx * t, pz = az + dz * t;
                TerrainField.Row row = field.RowAt(pz);
                float h = field.PlanHeight(row, px);
                if (h > line - 0.3f) return false;
                if (!woods || along < 30.0 || dist - along < 30.0 || line > h + 9f) continue;
                field.Cover(row, px, h, 0.1f, out float forest, out _, out _, out _);
                if (forest > 0.62f) return false;
            }

            return true;
        }

        /// <summary>True when the terrain does not rise above the straight line from (ax, ay, az) to (bx, by, bz) (ends excluded).</summary>
        public bool InSight(double ax, double az, float ay, double bx, double bz, float by) => InSight(ax, az, ay, bx, bz, by, false);
    }
}
