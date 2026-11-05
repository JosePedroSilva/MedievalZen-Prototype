using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>A lake with its level and footprint in absolute coordinates (immutable).</summary>
    public sealed class LakeInfo
    {
        /// <summary>The planned lake.</summary>
        public Place Place;
        /// <summary>Water level, metres (1.8 m below the level road beside it).</summary>
        public float Level;
        /// <summary>Deepest point below the level, metres.</summary>
        public float Depth;
        /// <summary>Centre of the footprint circle (absolute).</summary>
        public double CentreX, CentreZ;
        /// <summary>Radius of a circle that contains the whole basin.</summary>
        public double Radius;
        /// <summary>Radius within which no other ground may dip below the level (every tile the lake touches lies inside).</summary>
        public double ZoneRadius => Radius + 380.0;
    }

    /// <summary>A settlement disc to flatten (absolute centre, level = the road there).</summary>
    public sealed class SettlementDisc
    {
        /// <summary>The planned settlement.</summary>
        public Place Place;
        /// <summary>Centre (on the road).</summary>
        public double X, Z;
        /// <summary>Level of the disc (road height).</summary>
        public float Level;
        /// <summary>A walled city's hill on top of the level disc (<see cref="CityHill"/>), else null.</summary>
        public CityHill Hill;
    }

    /// <summary>
    /// The endless world's terrain as a pure function of absolute position: H(x, z) = road height at that z, plus a lateral
    /// profile chosen by the regions (blended, with boundaries wandering away from the road), plus lakes and settlement discs,
    /// with the road bed benched in exactly like <c>TerrainShaper.CarveRoads</c> (level bed, 45° cut banks, 35° fills).
    /// Everything z-dependent is gathered once per row (<see cref="Row"/>), so a whole heightmap row costs little more than its
    /// noise. Also supplies the land-cover fields (forest, highland, moisture, farm) the shared stages need. Thread-safe.
    /// </summary>
    public sealed class TerrainField
    {
        /// <summary>Height above which peaks are compressed (soft ceiling), as a fraction of the height range.</summary>
        private const float CeilingStart = 0.86f;

        private readonly EndlessParams p;
        private readonly RoadCurve road;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly ElevationProfile profile;
        private readonly ConcurrentDictionary<long, Bucket> buckets = new ConcurrentDictionary<long, Bucket>();
        private readonly ConcurrentDictionary<int, LakeInfo> lakes = new ConcurrentDictionary<int, LakeInfo>();
        private StreamNetwork streams;

        private readonly GradientNoise warpX, warpZ, swell, detail, distant, tilt, scarp, hollow, blend;
        private readonly GradientNoise ridge, spur, floorNoise, bankNoise, moraine, lakeWobble, island;
        private readonly GradientNoise forestNoise, clearing, moisture, farmNoise, crag;
        private readonly GradientNoise buttress, wallCrag, apronNoise;
        private readonly GradientNoise ledgeNoise, strataWander, gullyMeander, shelfNoise, shelfDepth;

        /// <summary>What a point of a pass wall sheds besides its height (see <see cref="Height(Row, double, out float, out float)"/>).</summary>
        private struct Shed
        {
            /// <summary>Talus 0..1: scree fans, aprons at the wall foot.</summary>
            public float Talus;
            /// <summary>Gully 0..1: inside a gully cut into a wall.</summary>
            public float Gully;
        }

        /// <summary>Places that matter for a 256 m stretch of road (immutable once built).</summary>
        private sealed class Bucket
        {
            public LakeInfo[] Lakes;
            public SettlementDisc[] Discs;
        }

        /// <summary>Everything about one terrain row (constant z) that does not depend on x.</summary>
        public sealed class Row
        {
            /// <summary>Absolute z of the row.</summary>
            public double Z;
            /// <summary>Road centre x at this z and its slope dx/dz.</summary>
            public double RoadX, Slope;
            /// <summary>Cosine of the heading (lateral distance = (x - RoadX) · Cos).</summary>
            public double Cos;
            /// <summary>Road distance of the road point at this z.</summary>
            public double S;
            /// <summary>Road height at this z.</summary>
            public float Road;
            /// <summary>Region weights at S (unwarped; per-sample weights warp them away from the road).</summary>
            public Region A, B;
            /// <summary>Weights of <see cref="A"/> and <see cref="B"/>.</summary>
            public double WA, WB;
            /// <summary>Cross-slope of the ground per region (rise per metre to the right).</summary>
            public float TiltA, TiltB;
            /// <summary>Nearby lakes and settlement discs.</summary>
            public LakeInfo[] Lakes;
            /// <summary>Nearby settlement discs.</summary>
            public SettlementDisc[] Discs;
            /// <summary>Stream pieces whose reach overlaps this row (empty on rows built for planning the streams).</summary>
            public StreamPiece[] Streams = Array.Empty<StreamPiece>();
            /// <summary>Pass ranges: strength 0..1 of the road's shelf at this row (the ground beyond it drops away) and its side (+1 right).</summary>
            public float Shelf;
            /// <summary>Side of <see cref="Shelf"/>'s drop (+1: right of the road looking toward +s, -1 left).</summary>
            public int ShelfSide;
        }

        /// <summary>Creates the field.</summary>
        public TerrainField(EndlessParams p, RoadCurve road, RegionSequence regions, JourneyPlan plan, ElevationProfile profile)
        {
            this.p = p;
            this.road = road;
            this.regions = regions;
            this.plan = plan;
            this.profile = profile;
            int s = p.seed;
            GradientNoise N(int stream) => new GradientNoise(EndlessNoise.Seed(s, stream, 0));
            warpX = N(501); warpZ = N(502); swell = N(503); detail = N(504); distant = N(505); tilt = N(506); scarp = N(507);
            hollow = N(508); blend = N(509); ridge = N(510); spur = N(511); floorNoise = N(512); bankNoise = N(513); moraine = N(514);
            lakeWobble = N(517); island = N(518); forestNoise = N(519); clearing = N(520);
            moisture = N(521); farmNoise = N(522); crag = N(523);
            buttress = N(1080); wallCrag = N(1081); apronNoise = N(1082);
            ledgeNoise = N(1263); strataWander = N(1264); gullyMeander = N(1265); shelfNoise = N(1270); shelfDepth = N(1271);
        }

        /// <summary>The journey plan the field was made from (the inns' archetypes ask it for their neighbours).</summary>
        public JourneyPlan Plan => plan;

        /// <summary>Connects the stream network (carving, water, banks). Called once by the generator before any use.</summary>
        internal void SetStreams(StreamNetwork network) => streams = network;

        /// <summary>The stream network (null until connected).</summary>
        public StreamNetwork Streams => streams;

        /// <summary>Height of the tree line (broadleaf stops there), metres.</summary>
        public float TreeLineAltitude => EndlessGenerator.TreeLine * p.heightRange;

        // ------------------------------------------------------------------ rows and places

        /// <summary>Gathers everything about row <paramref name="z"/>, the streams near it included.</summary>
        public Row RowAt(double z)
        {
            Row row = BaseRow(z);
            if (streams != null) row.Streams = streams.PiecesNear(z);
            return row;
        }

        /// <summary>Gathers everything about row <paramref name="z"/> except the streams (what the streams are planned on).</summary>
        public Row BaseRow(double z)
        {
            var row = new Row { Z = z };
            row.RoadX = road.X(z, out double slope, out _);
            row.Slope = slope;
            row.Cos = 1.0 / Math.Sqrt(1.0 + slope * slope);
            row.S = road.S(z);
            row.Road = profile.BaseHeight(row.S);
            row.A = regions.Weights(row.S, out row.WA, out row.B, out row.WB);
            row.TiltA = TiltOf(row.A, row.S);
            row.TiltB = row.B != null ? TiltOf(row.B, row.S) : 0f;
            Bucket bucket = BucketAt(row.S);
            row.Lakes = bucket.Lakes;
            row.Discs = bucket.Discs;
            row.Shelf = ShelfAt(row.S, row.Discs, out row.ShelfSide);
            return row;
        }

        /// <summary>Width of a pass road's shelf: the ground beyond this distance from the road's centre drops away, metres.</summary>
        public const float ShelfWidth = 24f;

        /// <summary>Slope (rise over run) of the bank below a shelf's edge, on average (42 degrees; steepest in its middle).</summary>
        public const float ShelfBank = 0.9f;

        /// <summary>No shelf within this road distance of a stream crossing (its stream leaves the road over level ground), metres.</summary>
        public const double ShelfCrossingClearance = 200.0;

        /// <summary>No shelf within this road distance of the col (it keeps its walls and its divide), metres.</summary>
        public const double ShelfColClearance = 250.0;

        /// <summary>
        /// The road's shelf in a pass range at road distance <paramref name="s"/> (2026-10-05): over about half the range the road runs on
        /// a shelf along one wall of the valley and the floor beyond <see cref="ShelfWidth"/> drops 6-20 m into a lower floor, the side
        /// changing every kilometre or so (slow noise along the road). Not in the foothills, near the col, a stream crossing or a
        /// settlement (their discs would fill the drop). Returns the strength 0..1; <paramref name="side"/> receives the drop's side.
        /// </summary>
        public float ShelfAt(double s, SettlementDisc[] discs, out int side)
        {
            side = 1;
            Region r = regions.Weights(s, out double wa, out Region other, out double wb);
            if (r.Kind != RegionKind.HighlandPass && (other == null || other.Kind != RegionKind.HighlandPass)) return 0f;
            Region pass = r.Kind == RegionKind.HighlandPass ? r : other;
            float w = (float)(pass == r ? wa : wb);
            float n = EndlessNoise.Fbm1N(shelfNoise, s, 1.0 / 650.0, 2);
            side = n >= 0f ? 1 : -1;
            float strength = w * (float)EndlessNoise.Smooth(0.15, 0.5, Math.Abs(n)) * (float)EndlessNoise.Smooth(0.35, 0.7, PassEnvelope(pass, s));
            strength *= (float)EndlessNoise.Smooth(ShelfColClearance - 120.0, ShelfColClearance, Math.Abs(s - pass.Col));
            if (strength <= 0f) return 0f;
            if (discs != null)
            {
                foreach (SettlementDisc d in discs)
                {
                    double reach = d.Place.Radius + TerrainShaper.SettlementBlend;
                    strength *= (float)EndlessNoise.Smooth(reach + 40.0, reach + 140.0, Math.Abs(s - d.Place.S));
                }
            }

            if (strength > 0f)
            {
                var near = new List<Crossing>();
                plan.Crossings.Crossings(s - ShelfCrossingClearance, s + ShelfCrossingClearance, near);
                foreach (Crossing c in near) strength *= (float)EndlessNoise.Smooth(ShelfCrossingClearance - 80.0, ShelfCrossingClearance, Math.Abs(s - c.S));
            }

            return strength;
        }

        private float TiltOf(Region r, double s)
        {
            float amp;
            switch (r.Kind)
            {
                case RegionKind.Meadow: amp = 0.07f; break;
                case RegionKind.Farmland: amp = 0.035f; break;
                case RegionKind.Forest: amp = 0.11f; break;
                case RegionKind.Downs: amp = 0.1f; break;
                case RegionKind.LakeCountry: amp = 0.04f; break;
                default: amp = 0.05f; break;
            }

            return amp * EndlessNoise.Fbm1N(tilt, s + r.Index * 7919.0, 1.0 / 900.0, 2);
        }

        private Bucket BucketAt(double s)
        {
            long key = (long)Math.Floor(s / 256.0);
            return buckets.GetOrAdd(key, BuildBucket);
        }

        private Bucket BuildBucket(long key)
        {
            double s0 = key * 256.0, s1 = s0 + 256.0;
            var places = new List<Place>();
            plan.RegionPlaces(s0 - 1500.0, s1 + 1500.0, places);
            var lakeList = new List<LakeInfo>();
            var discList = new List<SettlementDisc>();
            foreach (Place place in places)
            {
                if (place.Kind == PlaceKind.Lake)
                {
                    LakeInfo lake = Lake(place);
                    // Rows whose road point lies within the lake's zone (generously, in road distance).
                    if (s1 >= place.S0 - lake.ZoneRadius - 200.0 && s0 <= place.S1 + lake.ZoneRadius + 200.0) lakeList.Add(lake);
                }
                else if (place.IsSettlement && Math.Abs(place.S - (s0 + s1) * 0.5) < 128.0 + place.Radius + SettlementBlendReach + 300.0)
                {
                    discList.Add(Disc(place));
                }
            }

            var inns = new List<Place>();
            plan.Inns(s0 - 400.0, s1 + 400.0, inns);
            foreach (Place inn in inns) discList.Add(Disc(inn));
            return new Bucket { Lakes = lakeList.ToArray(), Discs = discList.ToArray() };
        }

        private const float SettlementBlendReach = TerrainShaper.SettlementBlend;

        /// <summary>Settlement disc of a planned settlement.</summary>
        public SettlementDisc Disc(Place place)
        {
            double z = road.ZAt(place.S);
            double x = road.X(z);
            CityHill hill = place.City ? CityHill.For(p.seed, place.Id, x, z, road.Heading(z)) : null;
            float level = profile.BaseHeight(place.S);
            if (hill != null) hill.Level = level;
            return new SettlementDisc { Place = place, X = x, Z = z, Level = level, Hill = hill };
        }

        /// <summary>Level and footprint of a planned lake (cached).</summary>
        public LakeInfo Lake(Place place) => lakes.GetOrAdd(place.Id, _ => BuildLake(place));

        private LakeInfo BuildLake(Place place)
        {
            double z = road.ZAt(place.S);
            double x = road.X(z, out double slope, out _);
            double len = Math.Sqrt(1.0 + slope * slope);
            double nx = 1.0 / len, nz = -slope / len;
            double off = place.ShoreOffset + place.HalfWidth;
            var lake = new LakeInfo
            {
                Place = place,
                Depth = 3.5f + 0.012f * place.HalfWidth,
                CentreX = x + nx * place.Side * off,
                CentreZ = z + nz * place.Side * off,
                Radius = Math.Max(place.HalfLength, place.HalfWidth) * 1.35 + 40.0,
            };

            // The level: 1.8 m below the road, which is level wherever it passes through the lake's zone (JourneyPlan); the
            // lowest road point of that stretch is used so float noise in the level road can never put it under water.
            float lowest = profile.BaseHeight(place.S);
            for (double s = place.FlatFrom; s <= place.FlatTo; s += 8.0) lowest = Math.Min(lowest, profile.BaseHeight(s));
            lake.Level = lowest - 1.8f;
            return lake;
        }

        // ------------------------------------------------------------------ height

        /// <summary>Final ground height at absolute (x, z).</summary>
        public float Height(double x, double z) => Height(RowAt(z), x);

        /// <summary>
        /// Final ground height at absolute x on <paramref name="row"/>: relief, lakes, settlement discs, the stream channels, the
        /// road bed (with the dips of the fords), then the channels again under the bridges (the deck carries the road there).
        /// </summary>
        public float Height(Row row, double x) => Height(row, x, out _, out _);

        /// <summary>
        /// <see cref="Height(Row, double)"/> and what the pass walls shed there (0 elsewhere): <paramref name="talus"/> (scree fans,
        /// aprons) and <paramref name="gullyWeight"/> (inside a gully), both faded out where lakes, settlements, streams or the road bed
        /// changed the ground.
        /// </summary>
        public float Height(Row row, double x, out float talus, out float gullyWeight) => Height(row, x, true, out talus, out gullyWeight);

        /// <summary>
        /// The ground every plan that decides where other things go reads (landmark sites and sight lines, feature sides): the final
        /// ground without a city's hill (<see cref="CityHill"/>), so a slot becoming a city moves nothing else. Only the built terrain
        /// and the things inside the city see the hill.
        /// </summary>
        public float PlanHeight(double x, double z) => PlanHeight(RowAt(z), x);

        /// <summary><see cref="PlanHeight(double, double)"/> on a row.</summary>
        public float PlanHeight(Row row, double x) => Height(row, x, false, out _, out _);

        private float Height(Row row, double x, bool hill, out float talus, out float gullyWeight)
        {
            double z = row.Z;
            double v = (x - row.RoadX) * row.Cos;
            double a = Math.Abs(v);
            float e0 = row.Road;
            var shed = new Shed();
            float relief = e0 + Relief(row, x, z, v, a, out _, ref shed);
            float h = relief;
            h = ApplyLakes(row, x, z, v, h, out _);
            h = ApplyDiscs(row, x, z, h, hill);
            float bridge = float.PositiveInfinity;
            if (row.Streams.Length > 0) h = StreamNetwork.Carve(row.Streams, x, z, h, out bridge);
            h = ApplyRoad(x, z, a, h, true);
            if (bridge < h) h = bridge;
            float kept = 1f - FastMathf.Clamp01((FastMathf.Abs(h - relief) - 0.5f) / 1.5f);
            talus = shed.Talus * kept;
            gullyWeight = shed.Gully * kept;
            return Finish(h);
        }

        /// <summary>
        /// <see cref="Height(Row, double)"/> without the stream channels (the road bed with the dips of the fords is kept): the ground the
        /// drawn streams' banks are draped over (<see cref="TileStreams"/>).
        /// </summary>
        public float HeightWithoutStreams(Row row, double x)
        {
            double z = row.Z;
            double v = (x - row.RoadX) * row.Cos;
            double a = Math.Abs(v);
            float h = row.Road + Relief(row, x, z, v, a, out _);
            h = ApplyLakes(row, x, z, v, h, out _);
            h = ApplyDiscs(row, x, z, h);
            h = ApplyRoad(x, z, a, h, true);
            return Finish(h);
        }

        /// <summary>Ground height at absolute (x, z) before the streams (no channels, no fords) and without a city's hill: what the streams are traced on.</summary>
        public float BaseHeight(double x, double z) => BaseHeight(BaseRow(z), x);

        /// <summary><see cref="BaseHeight(double, double)"/> on a row (from <see cref="BaseRow"/> or <see cref="RowAt"/>).</summary>
        public float BaseHeight(Row row, double x)
        {
            double z = row.Z;
            double v = (x - row.RoadX) * row.Cos;
            double a = Math.Abs(v);
            float h = row.Road + Relief(row, x, z, v, a, out _);
            h = ApplyLakes(row, x, z, v, h, out _);
            h = ApplyDiscs(row, x, z, h, false);   // the streams are traced (and crossings decided) on the land without a city's hill
            h = ApplyRoad(x, z, a, h, false);
            return Finish(h);
        }

        /// <summary>Diagnostics: the ground at (x, z) before the road bed is benched in (lakes and settlement discs included).</summary>
        public float HeightBeforeRoad(double x, double z)
        {
            Row row = BaseRow(z);
            double v = (x - row.RoadX) * row.Cos;
            float h = row.Road + Relief(row, x, z, v, Math.Abs(v), out _);
            h = ApplyLakes(row, x, z, v, h, out _);
            h = ApplyDiscs(row, x, z, h);
            return Finish(h);
        }

        private float Finish(float h)
        {
            float top = p.heightRange;
            float start = CeilingStart * top;
            if (h > start)
            {
                float room = top - 3f - start;
                h = start + room * (float)Math.Tanh((h - start) / room);
            }

            return FastMathf.Clamp(h, 1f, top - 1f);
        }

        /// <summary>
        /// Height of the regions' relief above the road altitude at (x, z) (blended over the regions, boundaries warped by up to
        /// 180 m away from the road so ranges do not end along straight lines). <paramref name="aboveRoad"/> receives the same.
        /// </summary>
        private float Relief(Row row, double x, double z, double v, double a, out float aboveRoad)
        {
            var shed = new Shed();
            return Relief(row, x, z, v, a, out aboveRoad, ref shed);
        }

        private float Relief(Row row, double x, double z, double v, double a, out float aboveRoad, ref Shed shed)
        {
            // Domain warp for every recipe.
            double xw = x + 55.0 * EndlessNoise.Fbm(warpX, x, z, 1.0 / 650.0, 2);
            double zw = z + 55.0 * EndlessNoise.Fbm(warpZ, x, z, 1.0 / 650.0, 2);

            Region ra = row.A, rb = row.B;
            double wa = row.WA, wb = row.WB;
            double sr = row.S;
            if (a > 40.0)
            {
                double shift = 180.0 * EndlessNoise.Fbm(blend, x, z, 1.0 / 900.0, 2) * EndlessNoise.Smooth(40.0, 300.0, a);
                sr = row.S + shift;
                ra = regions.Weights(sr, out wa, out rb, out wb);
            }

            var sa = new Shed();
            float h = (float)wa * Recipe(ra, row, sr, x, z, xw, zw, v, a, ra == row.A ? row.TiltA : ra == row.B ? row.TiltB : TiltOf(ra, row.S), ref sa);
            shed.Talus = (float)wa * sa.Talus;
            shed.Gully = (float)wa * sa.Gully;
            if (rb != null && wb > 1e-4)
            {
                var sb = new Shed();
                h += (float)wb * Recipe(rb, row, sr, x, z, xw, zw, v, a, rb == row.A ? row.TiltA : rb == row.B ? row.TiltB : TiltOf(rb, row.S), ref sb);
                shed.Talus += (float)wb * sb.Talus;
                shed.Gully += (float)wb * sb.Gully;
            }

            aboveRoad = h;
            return h;
        }

        private static float Ramp(double a, double a0, double a1) => (float)EndlessNoise.Smooth(a0, a1, a);

        /// <summary>Unit-variance fractal noise at the (warped) position.</summary>
        private static float N(GradientNoise noise, double x, double z, double wavelength, int octaves) =>
            EndlessNoise.FbmN(noise, x, z, 1.0 / wavelength, octaves);

        /// <summary>One region's relief (metres above the road altitude of the row). Amplitudes are standard deviations.</summary>
        private float Recipe(Region r, Row row, double sr, double x, double z, double xw, double zw, double v, double a, float tiltNow, ref Shed shed)
        {
            float e0 = row.Road;
            float cross = tiltNow * (float)Math.Max(-150.0, Math.Min(150.0, v));
            switch (r.Kind)
            {
                case RegionKind.Meadow:
                {
                    // Gentle swells growing away from the road, small undulations near it, the odd knoll, hills far off.
                    float h = cross;
                    h += 10f * N(swell, xw, zw, 430.0, 4) * Ramp(a, 14, 190);
                    h += 1.6f * N(detail, xw, zw, 110.0, 3) * Ramp(a, 7, 45);
                    h += Knolls(xw, zw, a, 26f, 330.0);
                    h += Distant(x, z, a, 60f);
                    return h;
                }

                case RegionKind.Farmland:
                {
                    float h = cross;
                    h += 4f * N(swell, xw, zw, 400.0, 3) * Ramp(a, 14, 150);
                    h += 0.9f * N(detail, xw, zw, 140.0, 2) * Ramp(a, 8, 40);
                    h += Distant(x, z, a, 50f) * Ramp(a, 380, 900);
                    return h;
                }

                case RegionKind.Forest:
                {
                    float h = cross;
                    h += 12f * N(swell, xw, zw, 320.0, 4) * Ramp(a, 10, 120);
                    h += 3.5f * Ramp(a, 7, 24);   // wooded banks either side of the road
                    float hol = FastMathf.Max(0f, N(hollow, xw, zw, 170.0, 2) - 0.8f);
                    h -= 6f * hol * Ramp(a, 26, 70);
                    h += 1.4f * N(detail, xw, zw, 90.0, 2) * Ramp(a, 8, 40);
                    h += Distant(x, z, a, 70f);
                    return h;
                }

                case RegionKind.Downs:
                {
                    // Big bare swells, short chalk scarps, long views to higher ground.
                    float h = cross;
                    h += 24f * N(swell, xw, zw, 760.0, 4) * Ramp(a, 18, 300);
                    h += 6f * N(detail, xw, zw, 240.0, 3) * Ramp(a, 8, 80);
                    float edge = N(scarp, xw, zw, 900.0, 2) + 0.4f * N(scarp, xw, zw, 260.0, 2);
                    h += 15f * (float)EndlessNoise.Smooth(-0.25, 0.25, edge) * Ramp(a, 70, 170);
                    h += Distant(x, z, a, 90f);
                    return h;
                }

                case RegionKind.LakeCountry:
                {
                    float h = cross;
                    float fellSide = (float)EndlessNoise.Smooth(-40.0, 40.0, -v * r.Side);   // 1 away from the lakes
                    float fells = (55f + 20f * N(swell, xw, zw, 560.0, 4)) * Ramp(a, 35, 420) + Knolls(xw, zw, a, 30f, 300.0);
                    float low = -2.5f * Ramp(a, 20, 70) + 5f * N(swell, xw, zw, 500.0, 3) * Ramp(a, 260, 700);
                    h += fellSide * fells + (1f - fellSide) * low;
                    h += 1.1f * N(detail, xw, zw, 110.0, 2) * Ramp(a, 8, 40);
                    h += Distant(x, z, a, 80f) * Ramp(a, 500, 1100);
                    return h;
                }

                default:
                    return Pass(r, row, sr, x, z, xw, zw, v, a, ref shed) - e0;
            }
        }

        /// <summary>Far hills common to the lowland regions: rising beyond ~300 m to about <paramref name="amp"/> metres by ~1.2 km.</summary>
        private float Distant(double x, double z, double a, float amp)
        {
            if (a < 220.0) return 0f;
            float n = N(distant, x, z, 1100.0, 3);
            return amp * FastMathf.Max(0f, 0.55f + 0.45f * n) * Ramp(a, 250, 1200);
        }

        /// <summary>Isolated rounded knolls (about half the cells of a seeded grid) away from the road, to break open ground.</summary>
        private float Knolls(double x, double z, double a, float height, double cell)
        {
            if (a < 45.0) return 0f;
            long cx = (long)Math.Floor(x / cell), cz = (long)Math.Floor(z / cell);
            float best = 0f;
            for (long j = cz - 1; j <= cz + 1; j++)
            {
                for (long i = cx - 1; i <= cx + 1; i++)
                {
                    if (BoxDistance(x, z, (i + 0.2) * cell, (j + 0.2) * cell, (i + 0.8) * cell, (j + 0.8) * cell) > 0.8 * cell) continue;
                    ulong hsh = EndlessNoise.Hash(p.seed, 540, i, j);
                    if (EndlessNoise.Unit(hsh) > 0.55) continue;
                    double px = (i + 0.2 + 0.6 * EndlessNoise.Unit(EndlessNoise.Hash(p.seed, 541, i, j))) * cell;
                    double pz = (j + 0.2 + 0.6 * EndlessNoise.Unit(EndlessNoise.Hash(p.seed, 542, i, j))) * cell;
                    double radius = cell * (0.22 + 0.18 * EndlessNoise.Unit(EndlessNoise.Hash(p.seed, 543, i, j)));
                    double dx = x - px, dz = z - pz;
                    double d2 = (dx * dx + dz * dz) / (radius * radius);
                    if (d2 >= 4.0) continue;
                    float hgt = height * (0.5f + 0.5f * (float)EndlessNoise.Unit(EndlessNoise.Hash(p.seed, 544, i, j)));
                    best = FastMathf.Max(best, hgt * (float)Math.Exp(-1.6 * d2));
                }
            }

            return best * Ramp(a, 45, 110);
        }

        /// <summary>
        /// The pass: a valley floor 16-60 m wide along the road (rising gently to the walls), and walls whose height comes from
        /// absolute ridged noise: wall = floor + (crest altitude - road) * (1 - exp(-t / L)) * shape, where t is the distance
        /// beyond the floor and shape (0.3..1) follows ridges in absolute coordinates, so spurs run steeply down to the floor and
        /// side valleys open between them; horns on top (taller and the crests higher beyond the near walls), a divide across the
        /// road at the col. The walls and the range's steep flanks are cut into cliff bands with ledges, the walls also into gullies
        /// with talus fans below them and scree aprons at their foot (<see cref="Shed"/> reports the talus and gullies for the
        /// paint); over part of the range the road runs on a shelf with the floor dropping away on one side (<see cref="ShelfAt"/>).
        /// The foot of the walls wanders (spurs push towards the road). Returns absolute metres.
        /// Towards either end of the region the range sinks into foothills (<see cref="PassEnvelope"/>): lower, rounder, wider
        /// apart, with no walls, crags or cliff bands, so a pass is approached through foothills and never rises as a wall out
        /// of the neighbouring meadow or wood.
        /// </summary>
        private float Pass(Region r, Row row, double sr, double x, double z, double xw, double zw, double v, double a, ref Shed shed)
        {
            float e0 = row.Road;
            float range = PassEnvelope(r, sr);
            float ridges = EndlessNoise.Ridged(ridge, xw, zw, 1.0 / 620.0, 4, 2f);
            // The foot of the walls wanders (35-90 m out) and pushes in where a ridge comes down to the valley (a spur).
            float floorHalf = 58f + 20f * N(floorNoise, x, z, 520.0, 2) - 34f * LandCover.Smooth(0.45f, 0.85f, ridges);
            // Buttresses: the wall's foot steps towards the road every hundred metres or so (never closer than the floor allows).
            float butt = EndlessNoise.Ridged(buttress, xw, zw, 1.0 / 110.0, 2, 2f);
            floorHalf -= 16f * LandCover.Smooth(0.55f, 0.9f, butt) * range;
            floorHalf = FastMathf.Max(17f, floorHalf) + FoothillFloorWiden * (1f - range);
            float floorAlt = e0 + 0.07f * (float)Math.Min(a, floorHalf) + 1.0f * N(moraine, x, z, 55.0, 2) * Ramp(a, 9, 22);

            // The mountains: ridged noise relative to the valley floor (sharp crests, broad side valleys, concave flanks),
            // reaching the crest altitude on the ridges, with horns where ridges are highest and crags on their flanks.
            double colDistance = row.S - r.Col;
            float divide = 30f * (float)Math.Exp(-(colDistance * colDistance) / (450.0 * 450.0));
            float broad = EndlessNoise.Ridged(spur, xw, zw, 1.0 / 1500.0, 2, 1.5f);
            // Beyond the near walls the range climbs higher (up to 50 m more by 1.1 km out), so the peaks behind stand above the walls
            // in layers that the haze sets apart.
            float beyond = Ramp(a, 280, 1100) * range;
            float crestAlt = r.ColAltitude + 55f + 60f * broad + divide + BackRangeRise * beyond;
            float rise = FastMathf.Max(30f, crestAlt - floorAlt) * FastMathf.Lerp(FoothillRise, 1f, range);
            // Foothills are rounded (the ridged noise flattened into broad backs); the range keeps its sharp crests.
            float lift = FastMathf.Lerp(LandCover.Smooth(0.05f, 0.95f, ridges), Mathf.Pow(ridges, 1.25f), range);
            float crags = 16f * (EndlessNoise.Ridged(crag, xw, zw, 1.0 / 160.0, 3, 2.2f) - 0.3f) * LandCover.Smooth(0.25f, 0.6f, ridges) * range;
            // The range's steep flanks (over ~20 degrees, slope from the ridged noise 3 m east and north) get the cliff bands of the
            // trench walls: risers with ledges that pinch out. (Gullies need the fall line's direction; turned by its small-scale
            // wobble, a frame anchored at the world origin scrambles them, so they stay on the trench walls.)
            float relief = rise * lift;
            float faced = 0f;
            if (range > 0.05f && ridges > 0.12f)
            {
                float rx = EndlessNoise.Ridged(ridge, xw + 3.0, zw, 1.0 / 620.0, 4, 2f), rz = EndlessNoise.Ridged(ridge, xw, zw + 3.0, 1.0 / 620.0, 4, 2f);
                float gx = rise * (FastMathf.Lerp(LandCover.Smooth(0.05f, 0.95f, rx), Mathf.Pow(rx, 1.25f), range) - lift) / 3f;
                float gz = rise * (FastMathf.Lerp(LandCover.Smooth(0.05f, 0.95f, rz), Mathf.Pow(rz, 1.25f), range) - lift) / 3f;
                float massSlope = FastMathf.Sqrt(gx * gx + gz * gz);
                // From about 29 degrees, full by 37 (2026-10-06: from 17 degrees the bands' weak terraces drew contour stripes on the
                // moderate slopes).
                float faces = LandCover.Smooth(FlankBandSlopeStart, FlankBandSlopeFull, massSlope) * range * range;
                faced = faces;
                if (faces > 0.001f)
                {
                    float wander = 4f * N(strataWander, x, z, 420.0, 2);
                    float ledged = Ledges(relief, massSlope, wander, x, z, 0, false);
                    relief += (ledged - relief) * faces * LandCover.Smooth(3f, 12f, relief);
                }
            }

            // The crags stay rough off the faces and mostly give way to the bands on them (a lumpy surface cut into bands rings
            // every lump with little terraces).
            float mass = floorAlt + relief + crags * (1f - 0.75f * faced) + Horns(xw, zw, r) * (1f + 0.6f * beyond) * LandCover.Smooth(0.45f, 0.8f, ridges) * range * range;
            mass = FastMathf.Max(mass, floorAlt);

            // The valley: a trench along the road with steep banks; where a ridge reaches the valley its end is a rock wall.
            float bankSlope = FastMathf.Lerp(FoothillBank, FastMathf.Max(0.7f, 1.2f + 0.45f * N(bankNoise, x, z, 380.0, 2)), range);
            float t = (float)(a - floorHalf);
            const float Fillet = 10f;
            float tt = t <= -Fillet ? 0f : t >= Fillet ? t : (t + Fillet) * (t + Fillet) / (4f * Fillet);
            float wall = floorAlt + bankSlope * tt;
            // Smooth minimum, its blend narrowing to nothing near the floor (no dip where both meet the floor).
            float am = mass - floorAlt, aw = wall - floorAlt;
            float k = FastMathf.Min(12f, 2f * FastMathf.Min(am, aw));
            float h = floorAlt + FastMathf.Min(am, aw);
            if (k > 1e-3f)
            {
                float hk = FastMathf.Max(k - FastMathf.Abs(am - aw), 0f) / k;
                h -= hk * hk * k * 0.25f;
            }

            // The rock architecture of the trench walls (2026-10-05; replaces the faint 13 m strata bands that read as stripes on
            // smooth brown dunes): on the wall, in coordinates along the valley (u) and out from the wall's foot (t), cliff bands of
            // 14-30 m with a ledge at the foot of each riser that runs for a while and pinches out, gullies cut down the face at
            // irregular intervals (the ribs between them read as buttresses), then crags at eye level, the scree apron at the foot
            // and a talus fan below each gully.
            int side = v >= 0 ? 1 : -1;
            double u = row.S + (x - row.RoadX) * row.Slope * row.Cos;
            // Crags on the walls near the road (5-200 m beyond the floor): rough rock at eye level (cut into bands below).
            float nearWall = Ramp(t, 4, 22) * (1f - Ramp(t, 150, 230)) * range;
            if (nearWall > 0f)
            {
                float faceUp = FastMathf.Clamp01((h - floorAlt - 4f) / 14f);
                float rough = EndlessNoise.Ridged(wallCrag, xw, zw, 1.0 / 48.0, 2, 2.4f) - 0.35f;
                h = FastMathf.Max(floorAlt, h + 6f * rough * nearWall * faceUp);
            }

            float onWall = FastMathf.Clamp01((mass - wall) / 15f) * range * range;
            float above = h - floorAlt;
            if (onWall > 0.001f && above > 2f)
            {
                float wander = 4f * EndlessNoise.Fbm1N(strataWander, u, 1.0 / 420.0, 2, 13.7f * side + 5.3f);
                float ledged = Ledges(above, bankSlope, wander, u, 0.0, side, true);
                h = floorAlt + above + (ledged - above) * onWall * LandCover.Smooth(3f, 12f, above);
            }

            if (onWall > 0.001f && t > 0f)
            {
                float gullyCut = GullyDepth(u, t, side, above, out float inside) * onWall;
                h -= gullyCut;
                shed.Gully = FastMathf.Max(shed.Gully, inside * onWall * LandCover.Smooth(0.5f, 2.5f, gullyCut));
            }

            // Scree aprons: where a wall rises from the floor, a talus slope (1 in 2.4) fills its foot, 8-14 m high.
            float walled = FastMathf.Clamp01((mass - wall) / 10f) * Ramp(t, -6, 2) * range;
            if (walled > 0f)
            {
                float apron = floorAlt + (0.42f + 0.08f * N(apronNoise, x, z, 90.0, 2)) * FastMathf.Max(0f, t + 3f);
                apron = FastMathf.Min(apron, floorAlt + 11f + 3f * N(apronNoise, x + 517.0, z, 140.0, 2));
                float aproned = floorAlt + (apron - floorAlt) * walled;
                if (aproned > h - 0.3f) shed.Talus = FastMathf.Max(shed.Talus, walled * LandCover.Smooth(0.6f, 2.5f, aproned - floorAlt));
                h = FastMathf.Max(h, aproned);
            }

            // The road's shelf: on the drop side the floor beyond ShelfWidth falls 6-20 m to a lower floor that runs to the far wall,
            // where the valley allows (a floor at least 40-50 m wide on that side).
            if (row.Shelf > 0f && side == row.ShelfSide)
            {
                float room = (float)EndlessNoise.Smooth(36.0, 52.0, floorHalf);
                float depth = FastMathf.Clamp(13f + 3.5f * N(shelfDepth, x, z, 300.0, 2), 6f, 20f) * row.Shelf * room;
                if (depth > 0.2f)
                {
                    float bankWidth = depth / ShelfBank;
                    float drop = depth * LandCover.Smooth(ShelfWidth, ShelfWidth + bankWidth, (float)a) * (1f - LandCover.Smooth(floorHalf - 6f, floorHalf + 8f, (float)a));
                    h -= drop;
                    floorAlt -= drop;
                }
            }

            // Talus fans below the gullies, spreading onto the floor (never within FanRoadClearance of the road's centre).
            if (onWall > 0.001f)
            {
                float fan = Fans(u, t, side, bankSlope, floorHalf) * onWall;
                if (fan > 0f)
                {
                    float fanned = floorAlt + fan;
                    if (fanned > h - 0.3f) shed.Talus = FastMathf.Max(shed.Talus, onWall * LandCover.Smooth(0.3f, 1.8f, fan));
                    h = FastMathf.Max(h, fanned);
                }
            }

            return h;
        }

        // ------------------------------------------------------------------ pass walls: ledges, gullies, fans

        /// <summary>How much higher the crests of a pass range rise 1.1 km and more from the road than by it, metres.</summary>
        public const float BackRangeRise = 50f;

        /// <summary>Slope of the range's mass (rise over run) where its flanks start to be cut into cliff bands, and where fully.</summary>
        public const float FlankBandSlopeStart = 0.55f, FlankBandSlopeFull = 0.75f;

        /// <summary>Cliff bands of the pass walls: shortest and tallest band (riser and ledge together), metres of height.</summary>
        public const float BandStepMin = 10f, BandStepMax = 24f;

        /// <summary>Gullies: one possible gully per this much road distance on each wall, metres.</summary>
        public const double GullyCell = 80.0;

        /// <summary>Share of the <see cref="GullyCell"/> cells with a gully.</summary>
        public const double GullyChance = 0.75;

        /// <summary>Slope (rise over run) of a talus fan's surface: 32 degrees, scree at rest.</summary>
        public const float FanSlope = 0.62f;

        /// <summary>A fan's toe stays at least this far from the road's centre, metres.</summary>
        public const float FanRoadClearance = 14f;

        /// <summary>Randomness streams of the pass walls (bands, gullies, fans).</summary>
        private const int StreamBands = 1266, StreamGullies = 1267, StreamFans = 1268;

        /// <summary>
        /// Ground <paramref name="above"/> metres over the valley floor, rising at <paramref name="slope"/>, recut into cliff bands: in
        /// each band a ledge (under half the band's width at no more than 0.3 of the slope, so under 17 degrees) then a riser (55-73
        /// degrees on the walls), the band edges wandering by <paramref name="wander"/> metres, each ledge present over stretches of
        /// 50-250 m and pinching out between them (the face is then the plain slope). Presence follows the valley (1D noise along
        /// <paramref name="px"/> = road distance, per <paramref name="side"/>) on the trench walls, or the ground (2D noise at
        /// (<paramref name="px"/>, <paramref name="pz"/>)) on the range's flanks. A pure height mapping, monotonic in the input.
        /// </summary>
        private float Ledges(float above, float slope, float wander, double px, double pz, int side, bool alongValley)
        {
            float hh = above + wander + 40f;
            if (hh <= 0f) return above;
            float bottom = 0f, step = BandStep(side, 0);
            int k = 0;
            while (bottom + step <= hh && k < 64)
            {
                bottom += step;
                k++;
                step = BandStep(side, k);
            }

            float f = (hh - bottom) / step;
            float n = alongValley
                ? EndlessNoise.Fbm1N(ledgeNoise, px, 1.0 / 120.0, 2, 7.31f * k + 19.7f * side)
                : N(ledgeNoise, px + 173.1 * k, pz - 91.7 * k, 120.0, 2);
            float presence = (float)EndlessNoise.Smooth(-0.8, -0.5, n);
            if (presence <= 0f) return above;
            float ledgeSlope = FastMathf.Min(0.25f, 0.2f / FastMathf.Max(0.3f, slope));
            float f0 = BandFoot(side, k);
            // In metres from the band's foot (x in, y out): the ledge line, the riser line (meeting it at the ledge's back), the next
            // ledge's line beyond the band's top and the band below's riser line below its foot. The ledge's back and both lips are
            // rounded over LedgeSoftening metres (2026-10-06: the hard kinks drew lines on the faces; about one 2 m sample).
            float x = f * step, x0 = f0 * step;
            float riser = (1f - ledgeSlope * f0) / (1f - f0), riserBelow = (1f - ledgeSlope * BandFoot(side, k - 1)) / (1f - BandFoot(side, k - 1));
            float y = SoftMax(ledgeSlope * x, ledgeSlope * x0 + riser * (x - x0), LedgeSoftening);
            y = SoftMin(y, step + ledgeSlope * (x - step), LedgeSoftening);
            y = SoftMin(y, riserBelow * x, LedgeSoftening);
            return above + presence * (y - x);
        }

        /// <summary>Rounding of the ledges' kinks (the ledge's back, the lips), metres of height difference between the lines.</summary>
        public const float LedgeSoftening = 2f;

        /// <summary>Where band <paramref name="k"/>'s ledge meets its riser, as a share of the band (0.42-0.58).</summary>
        private float BandFoot(int side, int k) => 0.42f + 0.16f * (float)EndlessNoise.Unit(p.seed, StreamBands, k, side + 3);

        private static float SoftMax(float a, float b, float k)
        {
            float h = FastMathf.Max(k - FastMathf.Abs(a - b), 0f) / k;
            return FastMathf.Max(a, b) + h * h * k * 0.25f;
        }

        private static float SoftMin(float a, float b, float k)
        {
            float h = FastMathf.Max(k - FastMathf.Abs(a - b), 0f) / k;
            return FastMathf.Min(a, b) - h * h * k * 0.25f;
        }

        private float BandStep(int side, int k) => BandStepMin + (BandStepMax - BandStepMin) * (float)EndlessNoise.Unit(p.seed, StreamBands, k, side);

        /// <summary>
        /// Depth of the gully cut at (<paramref name="u"/> along the valley, <paramref name="t"/> out from the wall's foot) on wall
        /// <paramref name="side"/>: a rounded V 12-30 m wide and 6-16 m deep (at most 45 % of the wall's height there), straight down
        /// the face with a slow meander; <paramref name="inside"/> is the channel profile 0..1 (1 on its axis).
        /// </summary>
        private float GullyDepth(double u, float t, int side, float above, out float inside)
        {
            inside = 0f;
            if (t <= 1f) return 0f;
            double meander = 6.0 * EndlessNoise.Fbm1N(gullyMeander, t, 1.0 / 60.0, 2, 3.7f * side);
            long c0 = (long)Math.Floor(u / GullyCell);
            float best = 0f;
            for (long c = c0 - 1; c <= c0 + 1; c++)
            {
                if (EndlessNoise.Unit(p.seed, StreamGullies, c, side) >= GullyChance) continue;
                double uc = (c + 0.2 + 0.6 * EndlessNoise.Unit(p.seed, StreamGullies, c, side + 10)) * GullyCell + meander;
                float half = 6f + 9f * (float)EndlessNoise.Unit(p.seed, StreamGullies, c, side + 20);
                float q = (float)(Math.Abs(u - uc) / half);
                if (q >= 1f) continue;
                float profile = (1f - q * q) * (1f - q * q);
                float depth = 6f + 10f * (float)EndlessNoise.Unit(p.seed, StreamGullies, c, side + 30);
                float cut = FastMathf.Min(depth, 0.45f * FastMathf.Max(0f, above)) * LandCover.Smooth(2f, 18f, t) * profile;
                if (cut > best)
                {
                    best = cut;
                    inside = profile;
                }
            }

            return best;
        }

        /// <summary>
        /// Height above the floor of the talus fans at (<paramref name="u"/>, <paramref name="t"/>): below four in five gullies a cone of
        /// <see cref="FanSlope"/> whose apex lies 10-24 m up the gully at about the gully floor's height, its radius cut so the toe stays
        /// <see cref="FanRoadClearance"/> from the road's centre (<paramref name="floorHalf"/>: the floor's half width). 0 where none.
        /// </summary>
        private float Fans(double u, float t, int side, float bank, float floorHalf)
        {
            long c0 = (long)Math.Floor(u / GullyCell);
            float best = 0f;
            for (long c = c0 - 1; c <= c0 + 1; c++)
            {
                if (EndlessNoise.Unit(p.seed, StreamGullies, c, side) >= GullyChance) continue;
                if (EndlessNoise.Unit(p.seed, StreamFans, c, side) >= 0.8) continue;   // a few gullies end on bare floor
                double uc = (c + 0.2 + 0.6 * EndlessNoise.Unit(p.seed, StreamGullies, c, side + 10)) * GullyCell;
                float apexT = 10f + 14f * (float)EndlessNoise.Unit(p.seed, StreamFans, c, side + 10);
                float depth = 6f + 10f * (float)EndlessNoise.Unit(p.seed, StreamGullies, c, side + 30);
                float apexH = bank * apexT - FastMathf.Min(depth, 0.45f * bank * apexT) * LandCover.Smooth(2f, 18f, apexT);
                apexH = FastMathf.Min(apexH, 22f);
                float radius = apexH / FanSlope;
                float reach = floorHalf + apexT - FanRoadClearance;
                if (radius > reach)
                {
                    radius = FastMathf.Max(0f, reach);
                    apexH = radius * FanSlope;
                }

                if (radius < 6f) continue;
                double du = u - uc, dt = t - apexT;
                if (Math.Abs(du) >= radius || Math.Abs(dt) >= radius) continue;
                float d = (float)Math.Sqrt(du * du + dt * dt);
                best = FastMathf.Max(best, apexH - FanSlope * d);
            }

            return best;
        }

        /// <summary>Distance from either end of a pass region over which its range rises out of foothills, metres.</summary>
        public const double FoothillReach = 1000.0;

        /// <summary>Height of the foothills at a pass region's ends, as a share of the range's height.</summary>
        public const float FoothillRise = 0.3f;

        /// <summary>Slope (rise over run) of the valley sides among the foothills (the range's banks are 0.7-1.65).</summary>
        public const float FoothillBank = 0.38f;

        /// <summary>Highland weight of the foothills at a pass region's ends, as a share of the range's (below the open-hill rock rule's threshold).</summary>
        public const float FoothillHighland = 0.4f;

        /// <summary>How much wider the valley floor is at a pass region's ends, metres each side.</summary>
        public const float FoothillFloorWiden = 60f;

        /// <summary>
        /// The pass's range strength at road distance <paramref name="s"/>: 0 at either end of region <paramref name="r"/> (and
        /// beyond), rising smoothly to 1 at <see cref="FoothillReach"/> inside. Below 1 the range sinks into foothills.
        /// </summary>
        public static float PassEnvelope(Region r, double s)
        {
            double inside = Math.Min(s - r.Start, r.End - s);
            return (float)EndlessNoise.Smooth(0.0, FoothillReach, inside);
        }

        /// <summary>Sharp horns: one per ~380 m cell (seeded position and height), a steep cone, maximum over neighbours.</summary>
        private float Horns(double x, double z, Region r)
        {
            const double Cell = 380.0;
            long cx = (long)Math.Floor(x / Cell), cz = (long)Math.Floor(z / Cell);
            float best = 0f;
            for (long j = cz - 1; j <= cz + 1; j++)
            {
                for (long i = cx - 1; i <= cx + 1; i++)
                {
                    if (BoxDistance(x, z, (i + 0.2) * Cell, (j + 0.2) * Cell, (i + 0.8) * Cell, (j + 0.8) * Cell) >= 130.0) continue;
                    ulong hsh = EndlessNoise.Hash(p.seed, 530, i, j);
                    double px = (i + 0.2 + 0.6 * EndlessNoise.Unit(hsh)) * Cell;
                    double pz = (j + 0.2 + 0.6 * EndlessNoise.Unit(EndlessNoise.Hash(p.seed, 531, i, j))) * Cell;
                    double dx = x - px, dz = z - pz;
                    double d = Math.Sqrt(dx * dx + dz * dz);
                    if (d >= 130.0) continue;
                    float height = 28f + 34f * (float)EndlessNoise.Unit(EndlessNoise.Hash(p.seed, 532, i, j));
                    float f = 1f - (float)(d / 130.0);
                    best = FastMathf.Max(best, height * f * f);
                }
            }

            return best;
        }

        private static double BoxDistance(double x, double z, double x0, double z0, double x1, double z1)
        {
            double dx = x < x0 ? x0 - x : x > x1 ? x - x1 : 0.0;
            double dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0.0;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private float ApplyLakes(Row row, double x, double z, double v, float h, out float level)
        {
            level = -1f;
            LakeInfo[] list = row.Lakes;
            for (int i = 0; i < list.Length; i++)
            {
                LakeInfo lake = list[i];
                double dxc = x - lake.CentreX, dzc = z - lake.CentreZ;
                double dc = Math.Sqrt(dxc * dxc + dzc * dzc);
                if (dc > lake.ZoneRadius + 100.0) continue;
                Place pl = lake.Place;
                double along = (row.S - pl.S) / pl.HalfLength;
                double across = (v * pl.Side - (pl.ShoreOffset + pl.HalfWidth)) / pl.HalfWidth;
                double r = Math.Sqrt(along * along + across * across);
                r *= 1.0 + 0.12 * EndlessNoise.Fbm(lakeWobble, x, z, 1.0 / 90.0, 2) * EndlessNoise.Smooth(0.0, 0.25, -across - 0.0 + 1.2);
                float target;
                if (r < 1.0)
                {
                    float bed = lake.Level - lake.Depth * (float)(1.0 - r * r);
                    // Islands in big lakes: bumps of the bed that break the surface.
                    if (pl.HalfWidth > 110f)
                    {
                        float bump = FastMathf.Max(0f, EndlessNoise.Fbm(island, x, z, 1.0 / 140.0, 2) - 0.32f) * 26f;
                        bed += bump * (float)EndlessNoise.Smooth(0.95, 0.6, r);
                    }

                    target = bed;
                    level = lake.Level;
                }
                else
                {
                    target = lake.Level + (float)((r - 1.0) * pl.HalfWidth * 0.12);
                }

                if (target < h) h = target;
                // The bed is the designed bowl: lower natural ground (the low side of lake country) is filled up to it, so the
                // basin has no sheer underwater cliff where the shore clamp below meets ground far below the bed.
                if (r < 1.0 && h < target - LakeBedSlack) h = target - LakeBedSlack;
                if (r >= 1.0)
                {
                    float fade = 1f - (float)EndlessNoise.Smooth(lake.ZoneRadius, lake.ZoneRadius + 100.0, dc);
                    float floor = lake.Level + 0.4f * (float)EndlessNoise.Smooth(1.0, 1.1, r) * fade - (1f - fade) * 50f;
                    if (h < floor) h = floor;
                }
            }

            return h;
        }

        /// <summary>How far natural ground may lie below a lake's designed bed before it is filled up to it, metres.</summary>
        public const float LakeBedSlack = 1.5f;

        private static float ApplyDiscs(Row row, double x, double z, float h, bool hill = true)
        {
            SettlementDisc[] discs = row.Discs;
            for (int i = 0; i < discs.Length; i++)
            {
                SettlementDisc disc = discs[i];
                double dx = x - disc.X, dz = z - disc.Z;
                float radius = disc.Place.Radius;
                float outer = radius + TerrainShaper.SettlementBlend;
                double dsq = dx * dx + dz * dz;
                if (dsq >= outer * outer)
                {
                    // Beyond the disc only a castle's rock (it reaches past the levelled ground behind the town).
                    if (!hill || disc.Hill == null || disc.Hill.Castle == null || dsq >= disc.Hill.RockReach * disc.Hill.RockReach) continue;
                    float far = disc.Hill.Rock(x, z);
                    if (far > 0f) h = FastMathf.Lerp(h, disc.Level + disc.Hill.Top, far);
                    continue;
                }
                float d = (float)Math.Sqrt(dsq);
                float inner = radius * 0.85f;
                float w = d <= inner ? 1f : 1f - TerrainShaper.SmoothStep01((d - inner) / (outer - inner));
                h = FastMathf.Lerp(h, disc.Level, w);
                if (hill && disc.Hill != null)
                {
                    h += disc.Hill.Offset(x, z);
                    // The castle's rock: level at the plateau's height wherever it reaches (also beyond the levelled disc), a cliff round it.
                    float rock = disc.Hill.Rock(x, z);
                    if (rock > 0f) h = FastMathf.Lerp(h, disc.Level + disc.Hill.Top, rock);
                }
            }

            return h;
        }

        /// <summary>
        /// The road bed: within reach of the centre line the ground is clamped between the fill and cut banks of the road's
        /// profile (<paramref name="fords"/>: with the dips into the fords, else the profile the streams are planned on).
        /// </summary>
        private float ApplyRoad(double x, double z, double a, float h, bool fords)
        {
            float half = p.roadWidth * 0.5f;
            float flat = half + 0.5f;
            float outer = flat + TerrainShaper.MaxBankReach;
            if (a > outer * 1.6 + 6.0) return h;
            double zeta = road.Nearest(x, z, out double d);
            if (d >= outer) return h;
            double sRoad = road.S(zeta);
            float y = fords ? profile.Height(sRoad) : profile.BaseHeight(sRoad);
            float beyond = FastMathf.Max(0f, (float)d - flat);
            float lo = y - TerrainShaper.Bank(beyond, TerrainShaper.FillSlope);
            float hi = y + TerrainShaper.Bank(beyond, TerrainShaper.CutSlope);
            return FastMathf.Clamp(h, lo, hi);
        }

        // ------------------------------------------------------------------ water

        /// <summary>
        /// The water level at absolute (x, z): a lake's level where the ground there is lake bed, else a stream's surface inside its
        /// channel (where the ground is below the water), else -1.
        /// </summary>
        public float WaterHeight(double x, double z)
        {
            Row row = RowAt(z);
            float h = Height(row, x);
            LakeInfo[] list = row.Lakes;
            for (int i = 0; i < list.Length; i++)
            {
                LakeInfo lake = list[i];
                double dx = x - lake.CentreX, dz = z - lake.CentreZ;
                if (dx * dx + dz * dz <= lake.Radius * lake.Radius && h < lake.Level) return lake.Level;
            }

            if (row.Streams.Length > 0)
            {
                StreamSample sample = StreamNetwork.Nearest(row.Streams, x, z);
                if (sample.Course != null && sample.Distance <= sample.HalfWater + 0.3f && h < sample.Surface) return sample.Surface;
            }

            return -1f;
        }

        /// <summary>The level of the lake whose basin reaches into the square [x0, x0 + size] × [z0, z0 + size], or -1.</summary>
        public float WaterLevelForSquare(double x0, double z0, double size, out LakeInfo lake)
        {
            lake = null;
            double cz = z0 + size * 0.5;
            var seen = new HashSet<int>();
            for (double z = z0 - 600.0; z <= z0 + size + 600.0; z += 128.0)
            {
                Bucket b = BucketAt(road.S(z));
                foreach (LakeInfo l in b.Lakes)
                {
                    if (!seen.Add(l.Place.Id)) continue;
                    double nx = Math.Max(x0, Math.Min(l.CentreX, x0 + size));
                    double nz = Math.Max(z0, Math.Min(l.CentreZ, z0 + size));
                    double dx = l.CentreX - nx, dz = l.CentreZ - nz;
                    if (dx * dx + dz * dz <= l.Radius * l.Radius)
                    {
                        lake = l;
                        return l.Level;
                    }
                }
            }

            _ = cz;
            return -1f;
        }

        /// <summary>
        /// Shore weight 0..1 at absolute x on <paramref name="row"/> for ground at <paramref name="h"/>: high just above the water
        /// line at the edge of a lake basin, from the lake geometry (so it is the same in every tile, with or without water).
        /// </summary>
        public float Shore(Row row, double x, float h)
        {
            float shore = 0f;
            double z = row.Z;
            double v = (x - row.RoadX) * row.Cos;
            LakeInfo[] list = row.Lakes;
            for (int i = 0; i < list.Length; i++)
            {
                LakeInfo lake = list[i];
                if (h < lake.Level) continue;
                double dxc = x - lake.CentreX, dzc = z - lake.CentreZ;
                if (dxc * dxc + dzc * dzc > (lake.Radius + 60.0) * (lake.Radius + 60.0)) continue;
                Place pl = lake.Place;
                double along = (row.S - pl.S) / pl.HalfLength;
                double across = (v * pl.Side - (pl.ShoreOffset + pl.HalfWidth)) / pl.HalfWidth;
                double r = Math.Sqrt(along * along + across * across);
                float near = 1f - (float)EndlessNoise.Smooth(1.0, 1.0 + 30.0 / pl.HalfWidth, r);
                shore = FastMathf.Max(shore, near * (1f - LandCover.Smooth(0f, LandCover.ShoreHeight, h - lake.Level)));
            }

            // Stream banks: muddy just above the water, then lush reedy grass a few metres up the bank.
            if (row.Streams.Length > 0)
            {
                StreamSample sample = StreamNetwork.Nearest(row.Streams, x, z);
                if (sample.Course != null)
                {
                    float near = 1f - (float)EndlessNoise.Smooth(sample.HalfWater, sample.HalfWater + 6.0, sample.Distance);
                    float low = 1f - LandCover.Smooth(0f, 1.8f, h - sample.Surface);
                    shore = FastMathf.Max(shore, 0.8f * near * low);
                }
            }

            return shore;
        }

        // ------------------------------------------------------------------ land cover

        /// <summary>
        /// Land-cover fields at absolute (x, z) on <paramref name="row"/> for ground at height <paramref name="h"/> with slope
        /// <paramref name="slope"/>: forest (before farmland), highland, moisture and farm weights, blended over the regions
        /// with the same rules as the finite world (tree line, no woods on cliffs, fewer in the highland).
        /// </summary>
        public void Cover(Row row, double x, float h, float slope, out float forest, out float highland, out float moist, out float farm) =>
            Cover(row, x, h, slope, out forest, out highland, out moist, out farm, out _);

        /// <summary>
        /// <see cref="Cover(Row, double, float, float, out float, out float, out float, out float)"/> and the strength of a pass range
        /// there, <paramref name="mountain"/> 0..1 (its foothills less, every other region 0), blended over the regions like the rest.
        /// </summary>
        public void Cover(Row row, double x, float h, float slope, out float forest, out float highland, out float moist, out float farm, out float mountain)
        {
            double z = row.Z;
            double v = (x - row.RoadX) * row.Cos;
            double a = Math.Abs(v);
            double xw = x + 55.0 * EndlessNoise.Fbm(warpX, x, z, 1.0 / 650.0, 2);
            double zw = z + 55.0 * EndlessNoise.Fbm(warpZ, x, z, 1.0 / 650.0, 2);
            Region ra = row.A, rb = row.B;
            double wa = row.WA, wb = row.WB;
            double sr = row.S;
            if (a > 40.0)
            {
                double shift = 180.0 * EndlessNoise.Fbm(blend, x, z, 1.0 / 900.0, 2) * EndlessNoise.Smooth(40.0, 300.0, a);
                sr = row.S + shift;
                ra = regions.Weights(sr, out wa, out rb, out wb);
            }

            float above = h - row.Road;
            CoverRecipe(ra, row, sr, x, z, xw, zw, v, a, h, above, out float fa, out float ha, out float ma, out float pa);
            forest = (float)wa * fa;
            highland = (float)wa * ha;
            moist = (float)wa * ma;
            farm = (float)wa * pa;
            mountain = ra.Kind == RegionKind.HighlandPass ? (float)wa * PassEnvelope(ra, sr) : 0f;
            if (rb != null && wb > 1e-4)
            {
                CoverRecipe(rb, row, sr, x, z, xw, zw, v, a, h, above, out float fb, out float hb, out float mb, out float pb);
                forest += (float)wb * fb;
                highland += (float)wb * hb;
                moist += (float)wb * mb;
                farm += (float)wb * pb;
                if (rb.Kind == RegionKind.HighlandPass) mountain += (float)wb * PassEnvelope(rb, sr);
            }

            // Fields around villages and hamlets, as in the finite world.
            foreach (SettlementDisc disc in row.Discs)
            {
                if (disc.Place.Kind == PlaceKind.Inn) continue;
                double dx = x - disc.X, dz = z - disc.Z;
                float reach = disc.Place.Radius + LandCover.FarmZone + 40f;
                double dsq = dx * dx + dz * dz;
                if (dsq >= reach * reach) continue;
                float d = (float)Math.Sqrt(dsq) - disc.Place.Radius + 35f * EndlessNoise.Fbm(farmNoise, x, z, 1.0 / 83.0, 1);
                farm = FastMathf.Max(farm, 1f - LandCover.Smooth(LandCover.FarmZone * 0.55f, LandCover.FarmZone, d));
            }

            float hn = h / FastMathf.Max(1e-3f, p.heightRange);
            float line = EndlessGenerator.TreeLine;
            moist = FastMathf.Clamp01(moist + 0.1f * EndlessNoise.Fbm(moisture, x, z, 1.0 / 700.0, 2));
            if (row.Streams.Length > 0)
            {
                // Lusher ground along the streams.
                StreamSample sample = StreamNetwork.Nearest(row.Streams, x, z);
                if (sample.Course != null) moist = FastMathf.Clamp01(moist + 0.35f * (1f - (float)EndlessNoise.Smooth(1.5, 8.5, sample.Distance - sample.HalfWater)));
            }
            forest *= 1f - LandCover.Smooth(line - 0.14f, line - 0.02f, hn);
            forest *= 1f - LandCover.Smooth(0.55f, 0.85f, slope);
            forest *= 1f - 0.45f * highland;
            forest = FastMathf.Clamp01(forest);
            highland = FastMathf.Clamp01(highland);
            farm = FastMathf.Clamp01(farm);
        }

        /// <summary>
        /// True when <see cref="Cover(Row, double, float, float, out float, out float, out float, out float)"/> gives a forest weight of
        /// exactly 0 at <paramref name="x"/> on <paramref name="row"/>, whatever the height and slope: every recipe ramps its woods in
        /// from a lateral distance (<see cref="WoodsFrom"/>), and within 40 m of the road (no region shift) x lies inside the ramp of
        /// both of the row's regions. Lets callers skip the height samples a cover call needs (2026-10-06 generation time pass).
        /// </summary>
        private long woodSamples;

        /// <summary>
        /// Full forest-weight evaluations made through <see cref="Planting.ForestAt(TerrainField, double, double)"/> on this field
        /// (three height samples and a cover call each) since it was created: the work counter the tests hold the wood floor's
        /// roadside pass to (a time-free budget). Thread-safe.
        /// </summary>
        public long WoodSamples => System.Threading.Interlocked.Read(ref woodSamples);

        internal void CountWoodSample() => System.Threading.Interlocked.Increment(ref woodSamples);

        public static bool NoWoodsAt(Row row, double x) => NoWoods(Math.Abs((x - row.RoadX) * row.Cos), row.A, row.B, row.WB);

        /// <summary>
        /// <see cref="NoWoodsAt(Row, double)"/> at absolute (x, z) from only the parts of the row it reads (the road's x, heading and
        /// distance, the regions there; the same calls <see cref="BaseRow"/> makes), so a caller builds the full row only when needed.
        /// </summary>
        public bool NoWoodsAt(double x, double z)
        {
            double roadX = road.X(z, out double slope, out _);
            double cos = 1.0 / Math.Sqrt(1.0 + slope * slope);
            double a = Math.Abs((x - roadX) * cos);
            if (a > 40.0) return false;
            Region ra = regions.Weights(road.S(z), out _, out Region rb, out double wb);
            return NoWoods(a, ra, rb, wb);
        }

        private static bool NoWoods(double a, Region ra, Region rb, double wb)
        {
            if (a > 40.0 || a > WoodsFrom(ra.Kind)) return false;
            return rb == null || !(wb > 1e-4) || a <= WoodsFrom(rb.Kind);
        }

        /// <summary>Lateral distance from the road within which a region's cover recipe has no wood at all (its forest ramp's start), metres.</summary>
        private static double WoodsFrom(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.Meadow: return MeadowWoodsFrom;
                case RegionKind.Farmland: return FarmlandWoodsFrom;
                case RegionKind.Forest: return ForestWoodsFrom;
                case RegionKind.Downs: return DownsWoodsFrom;
                case RegionKind.LakeCountry: return LakeWoodsFrom;
                default: return PassWoodsFrom;
            }
        }

        private const double MeadowWoodsFrom = 22, FarmlandWoodsFrom = 30, ForestWoodsFrom = 9, DownsWoodsFrom = 40, LakeWoodsFrom = 28, PassWoodsFrom = 20;

        private void CoverRecipe(Region r, Row row, double sr, double x, double z, double xw, double zw, double v, double a, float h, float above,
            out float forest, out float highland, out float moist, out float farm)
        {
            forest = 0f;
            highland = 0f;
            farm = 0f;
            switch (r.Kind)
            {
                case RegionKind.Meadow:
                {
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 240.0, 3) + 0.12f * EndlessNoise.Fbm(clearing, x, z, 1.0 / 70.0, 2);
                    // Woods and spinneys over about a fifth of the meadows (2026-10-02: a tenth read as bare).
                    forest = LandCover.Smooth(0.1f, 0.24f, n) * Ramp(a, MeadowWoodsFrom, 50);
                    moist = 0.58f;
                    break;
                }

                case RegionKind.Farmland:
                {
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 190.0, 3);
                    forest = LandCover.Smooth(0.24f, 0.36f, n) * Ramp(a, FarmlandWoodsFrom, 60) + 0.6f * LandCover.Smooth(0.15f, 0.3f, n) * Ramp(a, 450, 700);
                    float fields = 1f - LandCover.Smooth(0.18f, 0.32f, EndlessNoise.Fbm(farmNoise, xw, zw, 1.0 / 330.0, 2));
                    farm = 0.8f * fields * Ramp(a, 12, 30) * (1f - Ramp(a, 450, 750));
                    moist = 0.5f;
                    break;
                }

                case RegionKind.Forest:
                {
                    float open = LandCover.Smooth(0.28f, 0.44f, EndlessNoise.Fbm(clearing, xw, zw, 1.0 / 330.0, 2));
                    forest = (0.97f - 0.9f * open) * Ramp(a, ForestWoodsFrom, 15);
                    moist = 0.74f;
                    break;
                }

                case RegionKind.Downs:
                {
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 300.0, 3);
                    // Hanging woods in the folds and on the lower slopes; the high tops stay open.
                    forest = 0.8f * LandCover.Smooth(0.16f, 0.3f, n) * (1f - 0.7f * LandCover.Smooth(12f, 45f, above)) * Ramp(a, DownsWoodsFrom, 80);
                    highland = 0.08f + 0.32f * LandCover.Smooth(25f, 70f, above);
                    moist = 0.22f;
                    break;
                }

                case RegionKind.LakeCountry:
                {
                    float fellSide = (float)EndlessNoise.Smooth(-40.0, 40.0, -v * r.Side);
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 260.0, 3);
                    forest = LandCover.Smooth(0.05f, 0.25f, n) * Ramp(a, LakeWoodsFrom, 70) * (0.45f + 0.55f * fellSide);
                    highland = 0.25f * LandCover.Smooth(30f, 70f, above) * fellSide;
                    moist = 0.85f;
                    break;
                }

                default:
                {
                    // The foothills at either end are wooded, grassy hills (less highland, more wood); the range is highland.
                    float range = PassEnvelope(r, sr);
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 220.0, 3);
                    float lineAlt = TreeLineAltitude;
                    forest = 0.9f * LandCover.Smooth(-0.05f - 0.25f * (1f - range), 0.18f, n) * (1f - LandCover.Smooth(lineAlt - 70f, lineAlt - 10f, h)) * Ramp(a, PassWoodsFrom, 45);
                    highland = (0.55f + 0.45f * LandCover.Smooth(8f, 55f, above)) * FastMathf.Lerp(FoothillHighland, 1f, range);
                    moist = 0.42f;
                    break;
                }
            }
        }
    }
}
