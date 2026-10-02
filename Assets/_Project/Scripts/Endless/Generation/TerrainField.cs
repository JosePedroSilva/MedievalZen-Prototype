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

        private readonly GradientNoise warpX, warpZ, swell, detail, distant, tilt, scarp, hollow, blend;
        private readonly GradientNoise ridge, spur, floorNoise, bankNoise, moraine, strata, gully, lakeWobble, island;
        private readonly GradientNoise forestNoise, clearing, moisture, farmNoise, crag;

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
            strata = N(515); gully = N(516); lakeWobble = N(517); island = N(518); forestNoise = N(519); clearing = N(520);
            moisture = N(521); farmNoise = N(522); crag = N(523);
        }

        /// <summary>Height of the tree line (broadleaf stops there), metres.</summary>
        public float TreeLineAltitude => EndlessGenerator.TreeLine * p.heightRange;

        // ------------------------------------------------------------------ rows and places

        /// <summary>Gathers everything about row <paramref name="z"/>.</summary>
        public Row RowAt(double z)
        {
            var row = new Row { Z = z };
            row.RoadX = road.X(z, out double slope, out _);
            row.Slope = slope;
            row.Cos = 1.0 / Math.Sqrt(1.0 + slope * slope);
            row.S = road.S(z);
            row.Road = profile.Height(row.S);
            row.A = regions.Weights(row.S, out row.WA, out row.B, out row.WB);
            row.TiltA = TiltOf(row.A, row.S);
            row.TiltB = row.B != null ? TiltOf(row.B, row.S) : 0f;
            Bucket bucket = BucketAt(row.S);
            row.Lakes = bucket.Lakes;
            row.Discs = bucket.Discs;
            return row;
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
            return new SettlementDisc { Place = place, X = road.X(z), Z = z, Level = profile.Height(place.S) };
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
            float lowest = profile.Height(place.S);
            for (double s = place.FlatFrom; s <= place.FlatTo; s += 8.0) lowest = Math.Min(lowest, profile.Height(s));
            lake.Level = lowest - 1.8f;
            return lake;
        }

        // ------------------------------------------------------------------ height

        /// <summary>Final ground height at absolute (x, z).</summary>
        public float Height(double x, double z) => Height(RowAt(z), x);

        /// <summary>Final ground height at absolute x on <paramref name="row"/>.</summary>
        public float Height(Row row, double x)
        {
            double z = row.Z;
            double v = (x - row.RoadX) * row.Cos;
            double a = Math.Abs(v);
            float e0 = row.Road;
            float h = e0 + Relief(row, x, z, v, a, out _);
            h = ApplyLakes(row, x, z, v, h, out _);
            h = ApplyDiscs(row, x, z, h);
            h = ApplyRoad(x, z, a, h);
            return Finish(h);
        }

        /// <summary>Diagnostics: the ground at (x, z) before the road bed is benched in (lakes and settlement discs included).</summary>
        public float HeightBeforeRoad(double x, double z)
        {
            Row row = RowAt(z);
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

            return Mathf.Clamp(h, 1f, top - 1f);
        }

        /// <summary>
        /// Height of the regions' relief above the road altitude at (x, z) (blended over the regions, boundaries warped by up to
        /// 180 m away from the road so ranges do not end along straight lines). <paramref name="aboveRoad"/> receives the same.
        /// </summary>
        private float Relief(Row row, double x, double z, double v, double a, out float aboveRoad)
        {
            // Domain warp for every recipe.
            double xw = x + 55.0 * EndlessNoise.Fbm(warpX, x, z, 1.0 / 650.0, 2);
            double zw = z + 55.0 * EndlessNoise.Fbm(warpZ, x, z, 1.0 / 650.0, 2);

            Region ra = row.A, rb = row.B;
            double wa = row.WA, wb = row.WB;
            if (a > 40.0)
            {
                double shift = 180.0 * EndlessNoise.Fbm(blend, x, z, 1.0 / 900.0, 2) * EndlessNoise.Smooth(40.0, 300.0, a);
                ra = regions.Weights(row.S + shift, out wa, out rb, out wb);
            }

            float h = (float)wa * Recipe(ra, row, x, z, xw, zw, v, a, ra == row.A ? row.TiltA : ra == row.B ? row.TiltB : TiltOf(ra, row.S));
            if (rb != null && wb > 1e-4)
            {
                h += (float)wb * Recipe(rb, row, x, z, xw, zw, v, a, rb == row.A ? row.TiltA : rb == row.B ? row.TiltB : TiltOf(rb, row.S));
            }

            aboveRoad = h;
            return h;
        }

        private static float Ramp(double a, double a0, double a1) => (float)EndlessNoise.Smooth(a0, a1, a);

        /// <summary>Unit-variance fractal noise at the (warped) position.</summary>
        private static float N(GradientNoise noise, double x, double z, double wavelength, int octaves) =>
            EndlessNoise.FbmN(noise, x, z, 1.0 / wavelength, octaves);

        /// <summary>One region's relief (metres above the road altitude of the row). Amplitudes are standard deviations.</summary>
        private float Recipe(Region r, Row row, double x, double z, double xw, double zw, double v, double a, float tiltNow)
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
                    float hol = Mathf.Max(0f, N(hollow, xw, zw, 170.0, 2) - 0.8f);
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
                    return Pass(r, row, x, z, xw, zw, v, a) - e0;
            }
        }

        /// <summary>Far hills common to the lowland regions: rising beyond ~300 m to about <paramref name="amp"/> metres by ~1.2 km.</summary>
        private float Distant(double x, double z, double a, float amp)
        {
            if (a < 220.0) return 0f;
            float n = N(distant, x, z, 1100.0, 3);
            return amp * Mathf.Max(0f, 0.55f + 0.45f * n) * Ramp(a, 250, 1200);
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
                    best = Mathf.Max(best, hgt * (float)Math.Exp(-1.6 * d2));
                }
            }

            return best * Ramp(a, 45, 110);
        }

        /// <summary>
        /// The pass: a valley floor 16-60 m wide along the road (rising gently to the walls), and walls whose height comes from
        /// absolute ridged noise: wall = floor + (crest altitude - road) * (1 - exp(-t / L)) * shape, where t is the distance
        /// beyond the floor and shape (0.3..1) follows ridges in absolute coordinates, so spurs run steeply down to the floor and
        /// side valleys open between them; horns on top, a divide across the road at the col, the finite world's cliff bands on
        /// the steep spurs. The foot of the walls wanders (spurs push towards the road). Returns absolute metres.
        /// </summary>
        private float Pass(Region r, Row row, double x, double z, double xw, double zw, double v, double a)
        {
            float e0 = row.Road;
            float ridges = EndlessNoise.Ridged(ridge, xw, zw, 1.0 / 620.0, 4, 2f);
            // The foot of the walls wanders (35-90 m out) and pushes in where a ridge comes down to the valley (a spur).
            float floorHalf = 58f + 20f * N(floorNoise, x, z, 520.0, 2) - 34f * LandCover.Smooth(0.45f, 0.85f, ridges);
            floorHalf = Mathf.Max(17f, floorHalf);
            float floorAlt = e0 + 0.07f * (float)Math.Min(a, floorHalf) + 1.0f * N(moraine, x, z, 55.0, 2) * Ramp(a, 9, 22);

            // The mountains: ridged noise relative to the valley floor (sharp crests, broad side valleys, concave flanks),
            // reaching the crest altitude on the ridges, with horns where ridges are highest and crags on their flanks.
            double colDistance = row.S - r.Col;
            float divide = 30f * (float)Math.Exp(-(colDistance * colDistance) / (450.0 * 450.0));
            float broad = EndlessNoise.Ridged(spur, xw, zw, 1.0 / 1500.0, 2, 1.5f);
            float crestAlt = r.ColAltitude + 55f + 60f * broad + divide;
            float rise = Mathf.Max(30f, crestAlt - floorAlt);
            float lift = Mathf.Pow(ridges, 1.25f);
            float crags = 16f * (EndlessNoise.Ridged(crag, xw, zw, 1.0 / 160.0, 3, 2.2f) - 0.3f) * LandCover.Smooth(0.25f, 0.6f, ridges);
            float mass = floorAlt + rise * lift + crags + Horns(xw, zw, r) * LandCover.Smooth(0.45f, 0.8f, ridges);
            mass = Mathf.Max(mass, floorAlt);

            // The valley: a trench along the road with steep banks; where a ridge reaches the valley its end is a rock wall.
            float bankSlope = Mathf.Max(0.7f, 1.2f + 0.45f * N(bankNoise, x, z, 380.0, 2));
            float t = (float)(a - floorHalf);
            const float Fillet = 10f;
            float tt = t <= -Fillet ? 0f : t >= Fillet ? t : (t + Fillet) * (t + Fillet) / (4f * Fillet);
            float wall = floorAlt + bankSlope * tt;
            // Smooth minimum, its blend narrowing to nothing near the floor (no dip where both meet the floor).
            float am = mass - floorAlt, aw = wall - floorAlt;
            float k = Mathf.Min(12f, 2f * Mathf.Min(am, aw));
            float h = floorAlt + Mathf.Min(am, aw);
            if (k > 1e-3f)
            {
                float hk = Mathf.Max(k - Mathf.Abs(am - aw), 0f) / k;
                h -= hk * hk * k * 0.25f;
            }

            // Light cliff bands where the trench wall shows (no gullies: on a valley wall they line up into a comb).
            float sculpt = 0.45f * Mathf.Clamp01((h - floorAlt - 10f) / 25f) * Mathf.Clamp01((mass - wall) / 15f);
            if (sculpt > 0.001f)
            {
                double side = v >= 0 ? 1.0 : -1.0;
                float ux = (float)(row.Cos * side), uz = (float)(-row.Slope * row.Cos * side);
                float wx = (float)TileWindow.Wrap(x), wz = (float)TileWindow.Wrap(z);
                h = HeightmapGenerator.Sculpt(h, wx, wz, ux * bankSlope, uz * bankSlope, bankSlope, sculpt, strata, gully, 0f, 0f, 0f);
            }

            return h;
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
                    best = Mathf.Max(best, height * f * f);
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
                        float bump = Mathf.Max(0f, EndlessNoise.Fbm(island, x, z, 1.0 / 140.0, 2) - 0.32f) * 26f;
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
                if (r >= 1.0)
                {
                    float fade = 1f - (float)EndlessNoise.Smooth(lake.ZoneRadius, lake.ZoneRadius + 100.0, dc);
                    float floor = lake.Level + 0.4f * (float)EndlessNoise.Smooth(1.0, 1.1, r) * fade - (1f - fade) * 50f;
                    if (h < floor) h = floor;
                }
            }

            return h;
        }

        private static float ApplyDiscs(Row row, double x, double z, float h)
        {
            SettlementDisc[] discs = row.Discs;
            for (int i = 0; i < discs.Length; i++)
            {
                SettlementDisc disc = discs[i];
                double dx = x - disc.X, dz = z - disc.Z;
                float radius = disc.Place.Radius;
                float outer = radius + TerrainShaper.SettlementBlend;
                double dsq = dx * dx + dz * dz;
                if (dsq >= outer * outer) continue;
                float d = (float)Math.Sqrt(dsq);
                float inner = radius * 0.85f;
                float w = d <= inner ? 1f : 1f - TerrainShaper.SmoothStep01((d - inner) / (outer - inner));
                h = Mathf.Lerp(h, disc.Level, w);
            }

            return h;
        }

        /// <summary>The road bed: within reach of the centre line the ground is clamped between the fill and cut banks.</summary>
        private float ApplyRoad(double x, double z, double a, float h)
        {
            float half = p.roadWidth * 0.5f;
            float flat = half + 0.5f;
            float outer = flat + TerrainShaper.MaxBankReach;
            if (a > outer * 1.6 + 6.0) return h;
            double zeta = road.Nearest(x, z, out double d);
            if (d >= outer) return h;
            float y = profile.Height(road.S(zeta));
            float beyond = Mathf.Max(0f, (float)d - flat);
            float lo = y - TerrainShaper.Bank(beyond, TerrainShaper.FillSlope);
            float hi = y + TerrainShaper.Bank(beyond, TerrainShaper.CutSlope);
            return Mathf.Clamp(h, lo, hi);
        }

        // ------------------------------------------------------------------ water

        /// <summary>Lake level at absolute (x, z) where the ground there is lake bed, else -1.</summary>
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
                shore = Mathf.Max(shore, near * (1f - LandCover.Smooth(0f, LandCover.ShoreHeight, h - lake.Level)));
            }

            return shore;
        }

        // ------------------------------------------------------------------ land cover

        /// <summary>
        /// Land-cover fields at absolute (x, z) on <paramref name="row"/> for ground at height <paramref name="h"/> with slope
        /// <paramref name="slope"/>: forest (before farmland), highland, moisture and farm weights, blended over the regions
        /// with the same rules as the finite world (tree line, no woods on cliffs, fewer in the highland).
        /// </summary>
        public void Cover(Row row, double x, float h, float slope, out float forest, out float highland, out float moist, out float farm)
        {
            double z = row.Z;
            double v = (x - row.RoadX) * row.Cos;
            double a = Math.Abs(v);
            double xw = x + 55.0 * EndlessNoise.Fbm(warpX, x, z, 1.0 / 650.0, 2);
            double zw = z + 55.0 * EndlessNoise.Fbm(warpZ, x, z, 1.0 / 650.0, 2);
            Region ra = row.A, rb = row.B;
            double wa = row.WA, wb = row.WB;
            if (a > 40.0)
            {
                double shift = 180.0 * EndlessNoise.Fbm(blend, x, z, 1.0 / 900.0, 2) * EndlessNoise.Smooth(40.0, 300.0, a);
                ra = regions.Weights(row.S + shift, out wa, out rb, out wb);
            }

            float above = h - row.Road;
            CoverRecipe(ra, row, x, z, xw, zw, v, a, h, above, out float fa, out float ha, out float ma, out float pa);
            forest = (float)wa * fa;
            highland = (float)wa * ha;
            moist = (float)wa * ma;
            farm = (float)wa * pa;
            if (rb != null && wb > 1e-4)
            {
                CoverRecipe(rb, row, x, z, xw, zw, v, a, h, above, out float fb, out float hb, out float mb, out float pb);
                forest += (float)wb * fb;
                highland += (float)wb * hb;
                moist += (float)wb * mb;
                farm += (float)wb * pb;
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
                farm = Mathf.Max(farm, 1f - LandCover.Smooth(LandCover.FarmZone * 0.55f, LandCover.FarmZone, d));
            }

            float hn = h / Mathf.Max(1e-3f, p.heightRange);
            float line = EndlessGenerator.TreeLine;
            moist = Mathf.Clamp01(moist + 0.1f * EndlessNoise.Fbm(moisture, x, z, 1.0 / 700.0, 2));
            forest *= 1f - LandCover.Smooth(line - 0.14f, line - 0.02f, hn);
            forest *= 1f - LandCover.Smooth(0.55f, 0.85f, slope);
            forest *= 1f - 0.45f * highland;
            forest = Mathf.Clamp01(forest);
            highland = Mathf.Clamp01(highland);
            farm = Mathf.Clamp01(farm);
        }

        private void CoverRecipe(Region r, Row row, double x, double z, double xw, double zw, double v, double a, float h, float above,
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
                    forest = LandCover.Smooth(0.2f, 0.34f, n) * Ramp(a, 22, 50);
                    moist = 0.58f;
                    break;
                }

                case RegionKind.Farmland:
                {
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 190.0, 3);
                    forest = LandCover.Smooth(0.3f, 0.42f, n) * Ramp(a, 30, 60) + 0.6f * LandCover.Smooth(0.15f, 0.3f, n) * Ramp(a, 450, 700);
                    float fields = 1f - LandCover.Smooth(0.18f, 0.32f, EndlessNoise.Fbm(farmNoise, xw, zw, 1.0 / 330.0, 2));
                    farm = 0.8f * fields * Ramp(a, 12, 30) * (1f - Ramp(a, 450, 750));
                    moist = 0.5f;
                    break;
                }

                case RegionKind.Forest:
                {
                    float open = LandCover.Smooth(0.28f, 0.44f, EndlessNoise.Fbm(clearing, xw, zw, 1.0 / 330.0, 2));
                    forest = (0.97f - 0.9f * open) * Ramp(a, 9, 15);
                    moist = 0.74f;
                    break;
                }

                case RegionKind.Downs:
                {
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 300.0, 3);
                    forest = 0.7f * LandCover.Smooth(0.36f, 0.5f, n) * (1f - LandCover.Smooth(0f, 14f, above)) * Ramp(a, 40, 80);
                    highland = 0.08f + 0.32f * LandCover.Smooth(25f, 70f, above);
                    moist = 0.22f;
                    break;
                }

                case RegionKind.LakeCountry:
                {
                    float fellSide = (float)EndlessNoise.Smooth(-40.0, 40.0, -v * r.Side);
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 260.0, 3);
                    forest = LandCover.Smooth(0.05f, 0.25f, n) * Ramp(a, 28, 70) * (0.45f + 0.55f * fellSide);
                    highland = 0.25f * LandCover.Smooth(30f, 70f, above) * fellSide;
                    moist = 0.85f;
                    break;
                }

                default:
                {
                    float n = EndlessNoise.Fbm(forestNoise, xw, zw, 1.0 / 220.0, 3);
                    float lineAlt = TreeLineAltitude;
                    forest = 0.9f * LandCover.Smooth(-0.05f, 0.18f, n) * (1f - LandCover.Smooth(lineAlt - 70f, lineAlt - 10f, h)) * Ramp(a, 20, 45);
                    highland = 0.55f + 0.45f * LandCover.Smooth(8f, 55f, above);
                    moist = 0.42f;
                    break;
                }
            }
        }
    }
}
