using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// Structured planting of the endless countryside (endless only; the finite world is untouched): trees along the road in
    /// stretches that alternate with open ground (a line on one side, an avenue, now and then a tunnel of large trees), hedgerow
    /// trees and bushes along the field edges of the farmland patchwork, copses in open country, a lone large tree on a crest,
    /// and bushes and ferns softening the edges of the woods. Pure and deterministic: every decision is hashed from absolute
    /// positions (randomness streams 1101-1112) and checked against the window's road field, fields, settlements, water,
    /// slope and occupancy, so neighbouring tiles agree. Trees are planted before the rule scatter (which then fills around
    /// them) on near and far tiles alike; bushes only on near tiles, after the scatter, so the far tiles' trees stay those of
    /// the near tiles.
    /// </summary>
    public sealed partial class Planting
    {
        /// <summary>Randomness streams (EndlessNoise) used here: 1101-1112.</summary>
        private const int StreamEnclosure = 1101, StreamSide = 1102, StreamRoadside = 1103, StreamHedge = 1104, StreamCrest = 1105,
            StreamCopse = 1106, StreamEdge = 1107, StreamCopseTree = 1108, StreamUnderstorey = 1109, StreamCopseBush = 1110, StreamRoadsideBush = 1111;

        /// <summary>Road distance between roadside planting slots, metres.</summary>
        public const double RoadsideSlot = 7.0;

        /// <summary>Enclosure (along-road noise plus the region's bias) above which a stretch of road has a line of trees.</summary>
        public const float LineThreshold = 0.35f;

        /// <summary>Enclosure above which the trees stand on both sides as an avenue.</summary>
        public const float AvenueThreshold = 0.75f;

        /// <summary>Enclosure above which the avenue closes into a tunnel of large trees.</summary>
        public const float TunnelThreshold = 1.25f;

        /// <summary>Edge of the absolute cells copses are planned on, metres.</summary>
        public const double CopseCell = 125.0;

        /// <summary>Edge of the absolute cells lone crest trees are planned on, metres.</summary>
        public const double CrestCell = 110.0;

        /// <summary>Spacing of the hedgerow slots along a field edge, metres.</summary>
        public const float HedgeSpacing = 6.5f;

        /// <summary>Footprint kept clear round a planted tree, metres.</summary>
        public const float TreeFootprint = 3f;

        private readonly EndlessParams p;
        private readonly PaletteKeys keys;
        private readonly RoadCurve road;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly ElevationProfile profile;
        private readonly TerrainField field;
        private readonly FieldMosaic mosaic;
        private readonly FeaturePlan features;
        private readonly GradientNoise enclosure, sideNoise;

        /// <summary>Creates the planting stage.</summary>
        public Planting(EndlessParams p, PaletteKeys keys, RoadCurve road, RegionSequence regions, JourneyPlan plan, ElevationProfile profile,
            TerrainField field, FieldMosaic mosaic, FeaturePlan features)
        {
            this.p = p;
            this.keys = keys;
            this.road = road;
            this.regions = regions;
            this.plan = plan;
            this.profile = profile;
            this.field = field;
            this.mosaic = mosaic;
            this.features = features;
            enclosure = new GradientNoise(EndlessNoise.Seed(p.seed, StreamEnclosure, 0));
            sideNoise = new GradientNoise(EndlessNoise.Seed(p.seed, StreamSide, 0));
            InitWoodland();
            InitMountain();
        }

        /// <summary>What a window needs to plant: the window, its occupancy, the sites and fields it keeps clear of.</summary>
        public sealed class Context
        {
            /// <summary>The window.</summary>
            public GenWindow W;
            /// <summary>Absolute corner of the tile being generated and its edge.</summary>
            public double Tx, Tz;
            /// <summary>Tile edge, metres.</summary>
            public float Tile;
            /// <summary>Occupancy of the window (planted trees and bushes are added to it).</summary>
            public OccupancyMap Occupancy;
            /// <summary>Settlement sites in the window (local coordinates).</summary>
            public IReadOnlyList<SettlementSite> Sites;
            /// <summary>Fields in the window (local coordinates).</summary>
            public IReadOnlyList<FieldPatch> Fields;
            /// <summary>
            /// Optional: receives the trunk position (local x, z) of every structured tree planted anywhere in the window, in the tile
            /// or not (the field boundaries keep their walls and fences off them). Does not change what is planted.
            /// </summary>
            public List<Vector2> Trunks;
            internal List<FieldPatch> Patchwork;
            internal List<MosaicField> Mosaic;
            internal List<Place> Places;
            internal List<FeaturePlace> Feats;
            internal List<FlatZone> Shelves;
            internal List<Glade> Glades;
        }

        /// <summary>Prepares the context of a window (the places and shelves along its stretch of road).</summary>
        public Context Prepare(GenWindow w, double tx, double tz, float tile, OccupancyMap occupancy, IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields)
        {
            var ctx = new Context { W = w, Tx = tx, Tz = tz, Tile = tile, Occupancy = occupancy, Sites = sites, Fields = fields };
            double s0 = road.S(w.Z0 - 200.0), s1 = road.S(w.Z0 + w.Size + 200.0);
            ctx.Places = new List<Place>();
            plan.Inns(s0 - 400.0, s1 + 400.0, ctx.Places);
            plan.RegionPlaces(s0 - 600.0, s1 + 600.0, ctx.Places);
            ctx.Feats = new List<FeaturePlace>();
            features.Around(s0 - 200.0, s1 + 200.0, ctx.Feats);
            ctx.Shelves = new List<FlatZone>();
            profile.Shelves(s0 - 100.0, s1 + 100.0, ctx.Shelves);
            ctx.Glades = new List<Glade>();
            Glades(s0 - 80.0, s1 + 80.0, ctx.Glades);
            // The farmland patchwork (planting may run before the near tile has added its fields).
            ctx.Mosaic = new List<MosaicField>();
            mosaic.Overlapping(w.X0 - 10.0, w.Z0 - 10.0, w.Size + 20.0, ctx.Mosaic);
            ctx.Patchwork = new List<FieldPatch>(ctx.Mosaic.Count);
            foreach (MosaicField f in ctx.Mosaic) ctx.Patchwork.Add(FieldMosaic.ToPatch(f, w.X0, w.Z0, 0f));
            return ctx;
        }

        // ------------------------------------------------------------------ trees (near and far)

        /// <summary>
        /// Plants the structured trees over the whole window (all are added to the occupancy, so the scatter of every tile keeps
        /// clear of them); those whose trunk is in the tile are appended to <paramref name="trees"/> (window coordinates). With
        /// <paramref name="bushes"/> (near tiles) the hedgerow bushes and the bushes round the copses and under the roadside trees
        /// come too, each from its own random stream so the trees are the same with or without them.
        /// </summary>
        public void PlantTrees(Context ctx, List<TreePlacement> trees, List<PropPlacement> bushes)
        {
            Roadside(ctx, trees, bushes);
            Hedgerows(ctx, trees, bushes);
            Copses(ctx, trees, bushes);
            Crests(ctx, trees);
            Woodland(ctx, trees);
            MountainPines(ctx, trees, bushes);
        }

        /// <summary>
        /// Near tiles only, after the scatter: bushes and ferns along the edges of the woods and scattered thinly through open
        /// country (appended to <paramref name="bushes"/> when in the tile).
        /// </summary>
        public void PlantBushes(Context ctx, List<PropPlacement> bushes)
        {
            WoodEdges(ctx, bushes);
            WoodFloor(ctx, bushes);
        }

        /// <summary>
        /// How enclosed the road is at <paramref name="s"/>: slow noise along the road plus the region's bias (woods enclose,
        /// downs and lake shores open up). Above <see cref="LineThreshold"/> trees line the road.
        /// </summary>
        public float Enclosure(double s)
        {
            Region r = regions.Weights(s, out double wa, out Region other, out double wb);
            float bias = (float)(wa * Bias(r, s) + (other != null ? wb * Bias(other, s) : 0.0));
            return EndlessNoise.Fbm1N(enclosure, s, 1.0 / 520.0, 2) * 0.75f + bias;
        }

        /// <summary>
        /// Which sides of the road at <paramref name="s"/> the roadside planting lines with trees (bit 1 right, bit 2 left; 0 open),
        /// and whether it closes into a tunnel: what a traveller looks out through. Ignores the places' gaps. Pure; does not plant.
        /// </summary>
        public int RoadsideLining(double s, out bool tunnel)
        {
            float e = Enclosure(s);
            tunnel = e >= TunnelThreshold;
            if (e < LineThreshold) return 0;
            if (e >= AvenueThreshold) return 3;
            float sidePref = EndlessNoise.Fbm1N(sideNoise, s, 1.0 / 380.0, 1);
            return sidePref >= 0f ? 1 : 2;
        }

        private static float Bias(Region r, double s)
        {
            switch (r.Kind)
            {
                case RegionKind.Meadow: return 0.05f;
                case RegionKind.Farmland: return 0.15f;
                case RegionKind.Forest: return 0.1f;
                case RegionKind.Downs: return -0.35f;
                case RegionKind.LakeCountry: return -0.1f;
                default: return -1.6f + 1.5f * (1f - TerrainField.PassEnvelope(r, s));   // only among the foothills
            }
        }

        private void Roadside(Context ctx, List<TreePlacement> trees, List<PropPlacement> bushes)
        {
            GenWindow w = ctx.W;
            float corridor = w.Params.ScatterCorridor;
            double s0 = road.S(w.Z0 - 20.0), s1 = road.S(w.Z0 + w.Size + 20.0);
            for (long j = (long)Math.Floor(s0 / RoadsideSlot); j * RoadsideSlot <= s1; j++)
            {
                double sBase = j * RoadsideSlot;
                float e = Enclosure(sBase);
                if (e < LineThreshold) continue;
                if (NearPlace(ctx, sBase)) continue;
                Region region = regions.At(sBase);
                bool tunnel = e >= TunnelThreshold;
                bool avenue = e >= AvenueThreshold;
                float sidePref = EndlessNoise.Fbm1N(sideNoise, sBase, 1.0 / 380.0, 1);
                for (int k = 0; k < 2; k++)
                {
                    int side = k == 0 ? 1 : -1;
                    // A line keeps to one side (which side wanders); an avenue and a tunnel use both.
                    if (!avenue && side * sidePref < 0f) continue;
                    ulong h = EndlessNoise.Hash(p.seed, StreamRoadside, j, k);
                    double u = EndlessNoise.Unit(h);
                    // Spacing: about 20 m in a line, 13 m in an avenue, 8 m in a tunnel (gaps included).
                    double keep = tunnel ? 0.85 : avenue ? 0.52 : 0.36;
                    if (u >= keep) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamRoadside, j, k + 2));
                    double s = sBase + (rng.NextDouble() - 0.5) * RoadsideSlot * 0.6;
                    double z = road.ZAt(s);
                    double x = road.X(z, out double slope, out _);
                    double len = Math.Sqrt(1.0 + slope * slope);
                    double nx = 1.0 / len, nz = -slope / len;
                    float offset = corridor + (tunnel ? 0.6f + 1.2f * (float)rng.NextDouble() : 1.0f + 3.5f * (float)rng.NextDouble());
                    float lx = w.LocalX(x + nx * side * offset), lz = w.LocalZ(z + nz * side * offset);
                    string role = RoadsideSpecies(region, sBase, rng);
                    float scale = tunnel ? 1.25f + 0.35f * (float)rng.NextDouble() : 0.95f + 0.4f * (float)rng.NextDouble();
                    bool planted = TryTree(ctx, trees, role, lx, lz, scale, rng, corridor, 0.45f);
                    if (!planted || bushes == null) continue;
                    // Understorey under the roadside trees: a bush or fern behind or beside some of them.
                    var under = new System.Random(EndlessNoise.Seed(p.seed, StreamRoadsideBush, j, k));
                    if (under.NextDouble() > 0.55) continue;
                    double along = (under.NextDouble() - 0.5) * 6.0, back = 2.5 + 2.5 * under.NextDouble();
                    double bz = road.ZAt(s + along), bx = road.X(bz);
                    TryBush(ctx, bushes, under.NextDouble() < 0.65 ? PropRole.Bush : PropRole.Shrub, w.LocalX(bx + nx * side * (offset + back)), w.LocalZ(bz + nz * side * (offset + back)),
                        0.9f + 0.4f * (float)under.NextDouble(), under, corridor + 1f, 0.5f);
                }
            }
        }

        private string RoadsideSpecies(Region region, double s, System.Random rng)
        {
            double roll = rng.NextDouble();
            switch (region.Kind)
            {
                case RegionKind.Forest: return roll < 0.75 ? PropRole.ForestTree : PropRole.MeadowTree;
                case RegionKind.HighlandPass: return roll < 0.5 ? PropRole.PineTree : PropRole.ForestTree;
                case RegionKind.LakeCountry: return roll < 0.55 ? PropRole.ForestTree : PropRole.MeadowTree;
                default: return roll < 0.72 ? PropRole.MeadowTree : PropRole.ForestTree;
            }
        }

        /// <summary>True near a settlement, camp, viewpoint, stream crossing, rest shelf or lake (kept open for the view).</summary>
        private bool NearPlace(Context ctx, double s)
        {
            foreach (Place place in ctx.Places)
            {
                if (place.Kind == PlaceKind.Lake)
                {
                    if (s > place.S0 - 80.0 && s < place.S1 + 80.0) return true;
                    continue;
                }

                if (Math.Abs(place.S - s) < place.Radius + 45.0) return true;
            }

            foreach (FeaturePlace f in ctx.Feats)
            {
                double margin = f.Place.Kind == PlaceKind.Viewpoint ? 140.0 : 45.0;
                if (Math.Abs(f.Place.S - s) < margin) return true;
            }

            foreach (FlatZone z in ctx.Shelves)
            {
                if (s > z.From - 20.0 && s < z.To + 20.0) return true;
            }

            // The roadside slots of the tile generator (milestones, carts and stone piles) keep their spots.
            double m = s - Math.Round(s / JourneyPlan.MilestoneSpacing) * JourneyPlan.MilestoneSpacing;
            if (Math.Abs(m) < 14.0) return true;
            for (long k = (long)Math.Floor(s / TileGenerator.RoadsideSpacing) - 1; k <= (long)Math.Floor(s / TileGenerator.RoadsideSpacing) + 1; k++)
            {
                if (EndlessNoise.Unit(p.seed, TileGenerator.RoadsideStream, k) > 0.5) continue;
                double at = k * TileGenerator.RoadsideSpacing + 60.0 * EndlessNoise.Unit(p.seed, TileGenerator.RoadsideStream + 1, k);
                if (Math.Abs(at - s) < 16.0) return true;
            }

            StreamNetwork net = field.Streams;
            if (net != null)
            {
                for (long k = StreamPlan.CellOf(s) - 1; k <= StreamPlan.CellOf(s) + 1; k++)
                {
                    Crossing c = net.Plan.Cell(k);
                    if (c != null && Math.Abs(c.S - s) < 45.0 && net.Accepted(c)) return true;
                }
            }

            return false;
        }

        private void Hedgerows(Context ctx, List<TreePlacement> trees, List<PropPlacement> bushes)
        {
            GenWindow w = ctx.W;
            float corridor = w.Params.ScatterCorridor;
            foreach (MosaicField f in ctx.Mosaic)
            {
                double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
                // Field axes (as FieldPatch.Contains): right = (cos, -sin), forward = (sin, cos).
                double rx = cs, rz = -sn, fx = sn, fz = cs;
                for (int edge = 0; edge < 4; edge++)
                {
                    // Edge: from corner a along direction d, outward normal n.
                    double ax, az, dx, dz, nx, nz, length;
                    double hx = f.HalfX + 2.4, hz = f.HalfZ + 2.4;
                    switch (edge)
                    {
                        case 0: ax = f.X - rx * hx + fx * hz; az = f.Z - rz * hx + fz * hz; dx = rx; dz = rz; nx = fx; nz = fz; length = 2.0 * hx; break;
                        case 1: ax = f.X - rx * hx - fx * hz; az = f.Z - rz * hx - fz * hz; dx = rx; dz = rz; nx = -fx; nz = -fz; length = 2.0 * hx; break;
                        case 2: ax = f.X + rx * hx - fx * hz; az = f.Z + rz * hx - fz * hz; dx = fx; dz = fz; nx = rx; nz = rz; length = 2.0 * hz; break;
                        default: ax = f.X - rx * hx - fx * hz; az = f.Z - rz * hx - fz * hz; dx = fx; dz = fz; nx = -rx; nz = -rz; length = 2.0 * hz; break;
                    }

                    int steps = (int)(length / HedgeSpacing);
                    for (int k = 1; k < steps; k++)
                    {
                        ulong h = EndlessNoise.Hash(p.seed, StreamHedge, f.Id * 4 + edge, k);
                        double u = EndlessNoise.Unit(h);
                        bool tree = u < 0.2;
                        bool bush = !tree && u < 0.68;
                        if (tree ? trees == null : (!bush || bushes == null)) continue;
                        var rng = new System.Random((int)(h & 0x7fffffff));
                        double t = k * HedgeSpacing + (rng.NextDouble() - 0.5) * 2.0;
                        double jitter = (rng.NextDouble() - 0.5) * 1.2;
                        double x = ax + dx * t + nx * jitter, z = az + dz * t + nz * jitter;
                        // Keep the gate open.
                        double gx = x - f.GateX, gz = z - f.GateZ;
                        if (gx * gx + gz * gz < 64.0) continue;
                        float lx = w.LocalX(x), lz = w.LocalZ(z);
                        if (tree) TryTree(ctx, trees, rng.NextDouble() < 0.7 ? PropRole.MeadowTree : PropRole.ForestTree, lx, lz, 0.85f + 0.4f * (float)rng.NextDouble(), rng, corridor, 0.4f);
                        else TryBush(ctx, bushes, rng.NextDouble() < 0.8 ? PropRole.Bush : PropRole.Shrub, lx, lz, 1.0f + 0.35f * (float)rng.NextDouble(), rng, corridor + 1f, 0.5f);
                    }
                }
            }
        }

        private void Copses(Context ctx, List<TreePlacement> trees, List<PropPlacement> bushes)
        {
            GenWindow w = ctx.W;
            float corridor = w.Params.ScatterCorridor;
            long c0x = (long)Math.Floor((w.X0 - 40.0) / CopseCell), c1x = (long)Math.Floor((w.X0 + w.Size + 40.0) / CopseCell);
            long c0z = (long)Math.Floor((w.Z0 - 40.0) / CopseCell), c1z = (long)Math.Floor((w.Z0 + w.Size + 40.0) / CopseCell);
            for (long cz = c0z; cz <= c1z; cz++)
            {
                for (long cx = c0x; cx <= c1x; cx++)
                {
                    ulong h = EndlessNoise.Hash(p.seed, StreamCopse, cx, cz);
                    double px = (cx + 0.15 + 0.7 * EndlessNoise.Unit(EndlessNoise.Hash(p.seed, StreamCopse, cx, cz + 7919))) * CopseCell;
                    double pz = (cz + 0.15 + 0.7 * EndlessNoise.Unit(EndlessNoise.Hash(p.seed, StreamCopse, cx + 7919, cz))) * CopseCell;
                    Region region = DominantRegion(pz, out double sRoad);
                    if (EndlessNoise.Unit(h) >= CopseChance(region, sRoad)) continue;
                    float lx = w.LocalX(px), lz = w.LocalZ(pz);
                    if (lx < -30f || lz < -30f || lx > w.Size + 30f || lz > w.Size + 30f) continue;
                    // Copses stand in open country (not inside the woods, which are dense already).
                    if (lx > 0f && lz > 0f && lx < w.Size && lz < w.Size)
                    {
                        CoverSample cv = w.Cover.Sample(lx, lz);
                        if (cv.weights.forest > 0.35f || cv.weights.highland > 0.6f) continue;
                    }

                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamCopseTree, cx, cz));
                    int count = 7 + rng.Next(14);
                    float radius = 11f + 14f * (float)rng.NextDouble();
                    bool pines = region.Kind == RegionKind.Downs && rng.NextDouble() < 0.35;
                    for (int i = 0; i < count; i++)
                    {
                        double ang = rng.NextDouble() * Math.PI * 2.0;
                        double d = radius * Math.Sqrt(rng.NextDouble());
                        string role = pines ? PropRole.PineTree : rng.NextDouble() < 0.55 ? PropRole.MeadowTree : PropRole.ForestTree;
                        float scale = 0.85f + 0.5f * (float)rng.NextDouble();
                        TryTree(ctx, trees, role, lx + (float)(Math.Cos(ang) * d), lz + (float)(Math.Sin(ang) * d), scale, rng, corridor + 2f, 0.5f, 2.6f);
                    }

                    if (bushes == null) continue;
                    // A skirt of bushes and ferns round the copse.
                    var skirt = new System.Random(EndlessNoise.Seed(p.seed, StreamCopseBush, cx, cz));
                    int ring = 4 + skirt.Next(8);
                    for (int i = 0; i < ring; i++)
                    {
                        double ang = skirt.NextDouble() * Math.PI * 2.0;
                        double d = radius + 1.5 + 4.0 * skirt.NextDouble();
                        TryBush(ctx, bushes, skirt.NextDouble() < 0.7 ? PropRole.Bush : PropRole.Shrub, lx + (float)(Math.Cos(ang) * d), lz + (float)(Math.Sin(ang) * d),
                            0.9f + 0.45f * (float)skirt.NextDouble(), skirt, corridor + 1f, 0.55f);
                    }
                }
            }
        }

        private static double CopseChance(Region r, double s)
        {
            switch (r.Kind)
            {
                case RegionKind.Meadow: return 0.7;
                case RegionKind.Farmland: return 0.4;
                case RegionKind.Downs: return 0.55;
                case RegionKind.LakeCountry: return 0.55;
                case RegionKind.Forest: return 0.3;
                default: return 0.45 * (1.0 - TerrainField.PassEnvelope(r, s));
            }
        }

        private void Crests(Context ctx, List<TreePlacement> trees)
        {
            GenWindow w = ctx.W;
            float corridor = w.Params.ScatterCorridor;
            long c0x = (long)Math.Floor(w.X0 / CrestCell), c1x = (long)Math.Floor((w.X0 + w.Size) / CrestCell);
            long c0z = (long)Math.Floor(w.Z0 / CrestCell), c1z = (long)Math.Floor((w.Z0 + w.Size) / CrestCell);
            for (long cz = c0z; cz <= c1z; cz++)
            {
                for (long cx = c0x; cx <= c1x; cx++)
                {
                    ulong h = EndlessNoise.Hash(p.seed, StreamCrest, cx, cz);
                    double px = (cx + 0.15 + 0.7 * EndlessNoise.Unit(EndlessNoise.Hash(p.seed, StreamCrest, cx, cz + 7919))) * CrestCell;
                    double pz = (cz + 0.15 + 0.7 * EndlessNoise.Unit(EndlessNoise.Hash(p.seed, StreamCrest, cx + 7919, cz))) * CrestCell;
                    float lx = w.LocalX(px), lz = w.LocalZ(pz);
                    if (lx < 4f || lz < 4f || lx > w.Size - 4f || lz > w.Size - 4f) continue;
                    Region region = DominantRegion(pz, out double sRoad);
                    if (EndlessNoise.Unit(h) >= CrestChance(region, sRoad)) continue;
                    // A crest: higher than the ground 22 m round it and nearly level on top (the global field, so every window agrees).
                    float h0 = field.Height(px, pz);
                    float e = field.Height(px + 22.0, pz), wv = field.Height(px - 22.0, pz), n = field.Height(px, pz + 22.0), sv = field.Height(px, pz - 22.0);
                    float rise = h0 - 0.25f * (e + wv + n + sv);
                    float tilt = Math.Max(Math.Abs(e - wv), Math.Abs(n - sv)) / 44f;
                    if (rise < 1.2f || tilt > 0.28f) continue;
                    CoverSample cv = w.Cover.Sample(lx, lz);
                    if (cv.weights.forest > 0.3f) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamCrest, cx, cz + 104729));
                    string role = region.Kind == RegionKind.HighlandPass || (region.Kind == RegionKind.Downs && rng.NextDouble() < 0.25) ? PropRole.PineTree : PropRole.MeadowTree;
                    TryTree(ctx, trees, role, lx, lz, 1.45f + 0.3f * (float)rng.NextDouble(), rng, corridor + 6f, 0.35f, 5f);
                }
            }
        }

        private static double CrestChance(Region r, double s)
        {
            switch (r.Kind)
            {
                case RegionKind.Meadow: return 0.5;
                case RegionKind.Downs: return 0.6;
                case RegionKind.Farmland: return 0.35;
                case RegionKind.LakeCountry: return 0.4;
                case RegionKind.Forest: return 0.25;
                default: return 0.3 * (1.0 - TerrainField.PassEnvelope(r, s));
            }
        }

        private void WoodEdges(Context ctx, List<PropPlacement> bushes)
        {
            GenWindow w = ctx.W;
            float corridor = w.Params.ScatterCorridor;
            const double Grid = 5.0;
            double x0 = Math.Max(ctx.Tx, w.X0 + 2.0), z0 = Math.Max(ctx.Tz, w.Z0 + 2.0);
            double x1 = Math.Min(ctx.Tx + ctx.Tile, w.X0 + w.Size - 2.0), z1 = Math.Min(ctx.Tz + ctx.Tile, w.Z0 + w.Size - 2.0);
            for (long gz = (long)Math.Floor(z0 / Grid); gz * Grid < z1; gz++)
            {
                for (long gx = (long)Math.Floor(x0 / Grid); gx * Grid < x1; gx++)
                {
                    ulong h = EndlessNoise.Hash(p.seed, StreamEdge, gx, gz);
                    double u = EndlessNoise.Unit(h);
                    if (u > 0.5) continue;   // at most half the grid points
                    double x = (gx + EndlessNoise.Unit(EndlessNoise.Hash(p.seed, StreamEdge, gx, gz + 7919))) * Grid;
                    double z = (gz + EndlessNoise.Unit(EndlessNoise.Hash(p.seed, StreamEdge, gx + 7919, gz))) * Grid;
                    float lx = w.LocalX(x), lz = w.LocalZ(z);
                    CoverSample cv = w.Cover.Sample(lx, lz);
                    // Wood edges: peaks where the wood is half closed; open country: a light understorey.
                    float f = cv.weights.forest;
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(f - 0.45f) / 0.35f);
                    float open = (cv.weights.meadow + 0.6f * cv.weights.shore) * (1f - Mathf.Clamp01(f * 3f));
                    float chance = 0.36f * edge + Understorey * open;
                    if (u >= chance * 0.5) continue;
                    var rng = new System.Random((int)(EndlessNoise.Hash(p.seed, StreamUnderstorey, gx, gz) & 0x7fffffff));
                    string role = rng.NextDouble() < (edge > 0.3f ? 0.6 : 0.7) ? PropRole.Bush : PropRole.Shrub;
                    TryBush(ctx, bushes, role, lx, lz, 0.85f + 0.45f * (float)rng.NextDouble(), rng, corridor + 1f, 0.6f);
                }
            }
        }

        /// <summary>Share of the 5 m grid points in open meadow that get a bush or fern (light understorey in open country).</summary>
        public const float Understorey = 0.035f;

        private Region DominantRegion(double z, out double s)
        {
            s = road.S(z);
            Region a = regions.Weights(s, out double wa, out Region b, out double wb);
            return b != null && wb > wa ? b : a;
        }

        // ------------------------------------------------------------------ checks

        private bool Fits(Context ctx, float lx, float lz, float radius, float clearance, float maxSlope)
        {
            GenWindow w = ctx.W;
            if (lx < 2f || lz < 2f || lx > w.Size - 2f || lz > w.Size - 2f) return false;
            if (!w.Roads.IsClear(lx, lz, clearance)) return false;
            int i = w.Cover.Index(lx, lz);
            if (w.Cover.IsWater(i) || w.Cover.Slope[i] > maxSlope) return false;
            if (w.Cover.WaterHeight > 0f && HeightmapGenerator.SampleHeight(w.Map, w.Params, lx, lz) < w.Cover.WaterHeight + 0.4f) return false;
            if (ScatterPlanner.InsideSettlement(ctx.Sites, lx, lz, 12f + radius)) return false;
            if (ScatterPlanner.InsideField(ctx.Fields, lx, lz, 0.6f + 0.4f * radius)) return false;
            if (ScatterPlanner.InsideField(ctx.Patchwork, lx, lz, 0.6f + 0.4f * radius)) return false;
            if (w.Cover.Shore[i] > 0.6f) return false;   // stream banks and lake margins: the scatter's shore trees take them
            return !ctx.Occupancy.Overlaps(lx, lz, radius);
        }

        private bool InTile(Context ctx, float lx, float lz)
        {
            double ax = ctx.W.X0 + lx, az = ctx.W.Z0 + lz;
            return ax >= ctx.Tx && ax < ctx.Tx + ctx.Tile && az >= ctx.Tz && az < ctx.Tz + ctx.Tile;
        }

        private bool TryTree(Context ctx, List<TreePlacement> trees, string role, float lx, float lz, float scale, System.Random rng, float clearance, float maxSlope,
            float footprint = TreeFootprint)
        {
            int variant = keys.PickVariant(role, rng);
            float yaw = (float)(rng.NextDouble() * 360.0);
            if (!keys.Has(role) || !Fits(ctx, lx, lz, footprint, clearance, maxSlope)) return false;
            if (TooHigh(ctx, role, lx, lz)) return false;
            ctx.Occupancy.Add(new Vector3(lx, 0f, lz), footprint);
            ctx.Trunks?.Add(new Vector2(lx, lz));
            if (!InTile(ctx, lx, lz)) return true;
            float y = ScatterPlanner.GroundHeight(ctx.W.Params, ctx.W.Map, lx, lz, footprint * 0.3f);
            trees.Add(new TreePlacement { role = role, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale });
            return true;
        }

        /// <summary>Broadleaf trees stop at the tree line, pines a little higher (as the scatter rules).</summary>
        private bool TooHigh(Context ctx, string role, float lx, float lz)
        {
            float hn = HeightmapGenerator.SampleHeight(ctx.W.Map, ctx.W.Params, lx, lz) / Mathf.Max(1e-3f, p.heightRange);
            float line = EndlessGenerator.TreeLine;
            return role == PropRole.PineTree ? hn > line + 0.12f : hn > line - 0.04f;
        }

        private void TryBush(Context ctx, List<PropPlacement> bushes, string role, float lx, float lz, float scale, System.Random rng, float clearance, float maxSlope)
        {
            int variant = keys.PickVariant(role, rng);
            float yaw = (float)(rng.NextDouble() * 360.0);
            const float Footprint = 1.3f;
            if (!keys.Has(role) || !Fits(ctx, lx, lz, Footprint, clearance, maxSlope)) return;
            if (TooHigh(ctx, PropRole.PineTree, lx, lz)) return;
            ctx.Occupancy.Add(new Vector3(lx, 0f, lz), Footprint);
            if (!InTile(ctx, lx, lz)) return;
            float y = ScatterPlanner.GroundHeight(ctx.W.Params, ctx.W.Map, lx, lz, Footprint * 0.5f);
            bushes.Add(new PropPlacement { role = role, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale, settlementId = -1 });
        }
    }
}
