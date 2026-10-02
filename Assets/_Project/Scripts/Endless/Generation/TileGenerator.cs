using System;
using System.Collections.Generic;
using System.Diagnostics;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// Generates one tile by running the finite world's stages on a padded window around it: heights from the global terrain
    /// function, land cover from the region functions, dressings of nearby places from the <see cref="DressingCache"/>, then
    /// per-slot roadside features, <c>ScatterPlanner.Scatter</c> over absolute 128 m chunks (only the tile's own chunks
    /// are kept), <c>TerrainShaper.ComputeSplat</c> and <c>DetailPlanner.Plan</c> on the tile's own square.
    /// Everything that could reach across the edge (rock footprints, occupancy, slopes, scree) sees the padding, so neighbours
    /// agree. A prop or tree belongs to the tile containing its footprint centre. Pure; thread-safe.
    /// </summary>
    public sealed class TileGenerator
    {
        /// <summary>Padding around a near tile, metres (multiple of 2).</summary>
        public const float NearPad = 24f;

        /// <summary>Padding around a far tile, metres (multiple of 8).</summary>
        public const float FarPad = 8f;

        /// <summary>Road distance between roadside feature slots (carts, stone piles).</summary>
        public const double RoadsideSpacing = 290.0;

        private readonly EndlessParams p;
        private readonly PaletteKeys keys;
        private readonly RoadCurve road;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly ElevationProfile profile;
        private readonly TerrainField field;
        private readonly FeaturePlan features;
        private readonly DressingCache dressings;
        private readonly FieldMosaic mosaic;
        private readonly EndlessGenerator owner;
        private readonly List<ScatterRule> rules;
        private readonly List<ScatterRule> farRules;
        private readonly List<DetailRule> detailRules;

        /// <summary>Creates the tile generator.</summary>
        public TileGenerator(EndlessParams p, PaletteKeys keys, RoadCurve road, RegionSequence regions, JourneyPlan plan,
            ElevationProfile profile, TerrainField field, FeaturePlan features, DressingCache dressings, FieldMosaic mosaic, EndlessGenerator owner)
        {
            this.mosaic = mosaic;
            this.p = p;
            this.keys = keys;
            this.road = road;
            this.regions = regions;
            this.plan = plan;
            this.profile = profile;
            this.field = field;
            this.features = features;
            this.dressings = dressings;
            this.owner = owner;
            rules = ScatterRules.Default(EndlessGenerator.TreeLine);
            // Far tiles: the same table with everything but the big trees switched off, so each rule keeps its index and with it
            // its chunk streams (far trees are the near trees minus what near-only props crowd out).
            farRules = new List<ScatterRule>();
            foreach (ScatterRule rule in rules)
            {
                bool keep = PropRole.IsTree(rule.family) && rule.family != PropRole.DeadTree;
                farRules.Add(keep ? rule : new ScatterRule(rule.family, rule.biomes, 0f, rule.footprint));
            }

            detailRules = DetailPlanner.DefaultRules();
        }

        /// <summary>Generates <paramref name="coord"/> at <paramref name="lod"/>.</summary>
        public TileBlueprint Generate(TileCoord coord, TileLod lod)
        {
            var clock = Stopwatch.StartNew();
            TileBlueprint bp = lod == TileLod.Near ? Near(coord) : Far(coord);
            bp.GenerationMs = clock.Elapsed.TotalMilliseconds;
            return bp;
        }

        // ------------------------------------------------------------------ near

        private TileBlueprint Near(TileCoord coord)
        {
            float tile = p.tileSize;
            double tx = coord.OriginX(tile), tz = coord.OriginZ(tile);
            int heightRes = Math.Max(3, p.nearHeightResolution);
            float spacing = tile / (heightRes - 1);
            float pad = NearPad;
            int padCells = (int)Math.Round(pad / spacing);
            pad = padCells * spacing;
            int cells = (heightRes - 1) + 2 * padCells;
            var clock = Stopwatch.StartNew();
            float water = field.WaterLevelForSquare(tx, tz, tile, out _);
            GenWindow w = GenWindow.Create(p, field, road, tx - pad, tz - pad, cells, spacing, water);
            TileWindow window = w.Params.window;
            window.chunkSize = ChunkSizeFor(tile);
            window.keepMinX = tx;
            window.keepMaxX = tx + tile;
            window.keepMinZ = tz;
            window.keepMaxZ = tz + tile;
            Lap(clock, "heights");

            // Land cover and the road field on a 2 m grid (the finite world's cover/scatter/detail resolution).
            float coverTexel = tile / Math.Max(1, p.nearDetailResolution);
            int coverRes = (int)Math.Round(w.Size / coverTexel);
            float maxDistance = Mathf.Max(30f, w.Params.ScatterCorridor + 12f);
            w.Roads = w.DistanceField(coverRes, maxDistance);
            w.BuildCover(p, field, coverRes);
            Lap(clock, "cover");

            var bp = new TileBlueprint { Coord = coord, Lod = TileLod.Near, TileSize = tile, HeightRange = p.heightRange, WaterHeight = water };
            bp.Heights = new float[heightRes, heightRes];
            for (int j = 0; j < heightRes; j++)
            {
                for (int i = 0; i < heightRes; i++) bp.Heights[j, i] = w.Map[j + padCells, i + padCells];
            }

            // Places, their dressings and the settlement discs / fields they bring.
            var occupancy = new OccupancyMap(w.Size);
            var sites = new List<SettlementSite>();
            var fields = new List<FieldPatch>();
            var props = new List<PropPlacement>();
            var trees = new List<TreePlacement>();
            double sLo = road.S(w.Z0) - 600.0, sHi = road.S(w.Z0 + w.Size) + 600.0;
            var places = new List<Place>();
            plan.Inns(sLo, sHi, places);
            plan.RegionPlaces(sLo, sHi, places);
            var feats = new List<FeaturePlace>();
            features.Around(sLo, sHi, feats);
            places.Sort((a, b) => a.S != b.S ? a.S.CompareTo(b.S) : a.Id.CompareTo(b.Id));
            feats.Sort((a, b) => a.Place.S != b.Place.S ? a.Place.S.CompareTo(b.Place.S) : a.Place.Id.CompareTo(b.Place.Id));

            foreach (Place place in places)
            {
                if (!place.IsSettlement) continue;
                SettlementDisc disc = field.Disc(place);
                if (!Overlaps(w, disc.X, disc.Z, 300.0)) continue;
                Dressing d = dressings.Settlement(place);
                if (!Overlaps(w, d.CentreX, d.CentreZ, d.Reach)) continue;
                Import(d, w, tx, tz, occupancy, sites, fields, props);
            }

            foreach (FeaturePlace f in feats)
            {
                if (!Overlaps(w, f.X, f.Z, 80.0)) continue;
                Dressing d = dressings.Feature(f);
                if (!Overlaps(w, d.CentreX, d.CentreZ, d.Reach)) continue;
                Import(d, w, tx, tz, occupancy, sites, fields, props);
            }

            // The farmland patchwork: fields (painted, kept clear) and the crops, fences and scarecrows of the planted ones.
            var patchwork = new List<MosaicField>();
            mosaic.Overlapping(w.X0, w.Z0, w.Size, patchwork);
            foreach (MosaicField f in patchwork)
            {
                FieldPatch patch = FieldMosaic.ToPatch(f, w.X0, w.Z0, field.Height(f.X, f.Z));
                if (f.Fallow) patch.crop = PropRole.CropField;
                fields.Add(patch);
                occupancy.Add(patch.center, patch.halfExtents.magnitude + 1.5f);
                if (f.Fallow || !f.Planted) continue;
                var rng = new System.Random(EndlessNoise.Seed(p.seed, 810, f.Id));
                var crops = new List<PropPlacement>();
                var gate = new SettlementSite("road", MedievalZen.Roads.SettlementType.Village, new Vector3(w.LocalX(f.GateX), 0f, w.LocalZ(f.GateZ)));
                ScatterPlanner.FillField(w.Params, w.Map, keys, rng, patch, false, gate, crops);
                foreach (PropPlacement crop in crops)
                {
                    if (InTile(w.X0 + crop.position.x, w.Z0 + crop.position.z, tx, tz, tile)) props.Add(crop);
                }
            }

            Lap(clock, "dressings");

            // Per-slot roadside features in road order.
            SlotFeatures(w, tx, tz, places, feats, sites, fields, occupancy, props);
            Lap(clock, "slots");

            // Countryside scatter (absolute chunks; only this tile's chunks are kept).
            var scatterProps = new List<PropPlacement>();
            ScatterPlanner.Scatter(w.Params, w.Map, w.Roads, w.Cover, sites, fields, occupancy, keys, rules, trees, scatterProps);
            props.AddRange(scatterProps);
            Lap(clock, "scatter");

            // Ground paint on the tile's own square (1 m texels by default) and grass / flower density maps.
            int splatRes = Math.Max(1, p.nearSplatResolution);
            RoadDistanceField splatRoads = w.DistanceField(pad, tile, splatRes, maxDistance);
            bp.Splat = TerrainShaper.ComputeSplat(w.Params, splatRoads, w.Cover, sites, fields, pad, pad);
            Lap(clock, "splat");
            int detailRes = Math.Max(1, p.nearDetailResolution);
            int detailOffset = (int)Math.Round(pad / coverTexel);
            float[,,] coverSplat = TerrainShaper.Resample(bp.Splat, detailRes);
            bp.DetailResolution = detailRes;
            bp.Details = CompactDetails(DetailPlanner.Plan(w.Params, w.Roads, w.Cover, coverSplat, sites, fields, keys, detailRules, detailOffset, detailRes));
            Lap(clock, "details");

            // To tile-local coordinates.
            foreach (TreePlacement t in trees)
            {
                TreePlacement local = t;
                local.position = new Vector3(t.position.x - pad, t.position.y, t.position.z - pad);
                bp.Trees.Add(local);
            }

            foreach (PropPlacement prop in props)
            {
                PropPlacement local = prop;
                local.position = new Vector3(prop.position.x - pad, prop.position.y, prop.position.z - pad);
                bp.Props.Add(local);
            }

            AddWaypoints(bp, tx, tz, tile);
            return bp;
        }

        /// <summary>
        /// Most variants a detail family keeps in a tile. Variants beyond it are folded into a neighbour of the same family
        /// (cells keep their density, only the prototype changes) with a fixed mapping, so neighbouring tiles always agree.
        /// Grasses look alike between variants; flower families keep more variants (colours).
        /// </summary>
        public static int MaxVariants(string family)
        {
            switch (family)
            {
                case PropRole.Wildflowers: return 2;
                case PropRole.GenFlowers: return 3;
                case PropRole.SunflowerDetail: return 1;
                default: return 2;
            }
        }

        /// <summary>The variant a detail variant is folded into (see <see cref="MaxVariants"/>).</summary>
        public static int FoldVariant(string family, int variant, int count)
        {
            int keep = Math.Min(count, MaxVariants(family));
            if (count <= keep) return variant;
            int group = variant * keep / count;
            return group * count / keep;
        }

        /// <summary>Folds variants (<see cref="FoldVariant"/>) and drops layers with no instances in the tile.</summary>
        private List<DetailLayer> CompactDetails(List<DetailLayer> layers)
        {
            var byKey = new Dictionary<(string, int), DetailLayer>();
            var order = new List<DetailLayer>();
            foreach (DetailLayer layer in layers)
            {
                int target = FoldVariant(layer.family, layer.variant, keys.DetailCount(layer.family));
                if (!byKey.TryGetValue((layer.family, target), out DetailLayer into))
                {
                    into = new DetailLayer { family = layer.family, variant = target, density = new byte[layer.density.Length] };
                    byKey[(layer.family, target)] = into;
                    order.Add(into);
                }

                byte[] src = layer.density, dst = into.density;
                for (int i = 0; i < src.Length; i++)
                {
                    if (src[i] != 0) dst[i] = (byte)Math.Min(255, dst[i] + src[i]);
                }
            }

            var result = new List<DetailLayer>(order.Count);
            foreach (DetailLayer layer in order)
            {
                byte[] d = layer.density;
                for (int i = 0; i < d.Length; i++)
                {
                    if (d[i] == 0) continue;
                    result.Add(layer);
                    break;
                }
            }

            return result;
        }

        private void Lap(Stopwatch clock, string stage)
        {
            Action<string, double> timer = owner.StageTimer;
            if (timer != null) timer(stage, clock.Elapsed.TotalMilliseconds);
            clock.Restart();
        }

        /// <summary>Scatter chunk edge: the largest of 128, 64, 32 m that divides the tile size (or the tile itself).</summary>
        private static float ChunkSizeFor(float tile)
        {
            foreach (float c in new[] { 128f, 64f, 32f })
            {
                if (Math.Abs(tile / c - Math.Round(tile / c)) < 1e-4) return c;
            }

            return tile;
        }

        private static bool Overlaps(GenWindow w, double cx, double cz, double radius)
        {
            double nx = Math.Max(w.X0, Math.Min(cx, w.X0 + w.Size));
            double nz = Math.Max(w.Z0, Math.Min(cz, w.Z0 + w.Size));
            double dx = cx - nx, dz = cz - nz;
            return dx * dx + dz * dz <= radius * radius;
        }

        private static bool InTile(double x, double z, double tx, double tz, float tile) => x >= tx && x < tx + tile && z >= tz && z < tz + tile;

        /// <summary>Brings a dressing into the window: occupancy, settlement disc, fields, and the props whose centre is in the tile.</summary>
        private void Import(Dressing d, GenWindow w, double tx, double tz, OccupancyMap occupancy, List<SettlementSite> sites, List<FieldPatch> fields,
            List<PropPlacement> props)
        {
            float ox = (float)(d.X0 - w.X0), oz = (float)(d.Z0 - w.Z0);
            foreach (Vector3 c in d.Circles) occupancy.Add(new Vector3(c.x + ox, 0f, c.z + oz), c.y);
            if (d.Site != null)
            {
                Vector3 c = d.Site.Position;
                sites.Add(new SettlementSite(d.Site.Name, d.Site.Type, new Vector3(c.x + ox, c.y, c.z + oz)));
            }

            foreach (FieldPatch f in d.Fields)
            {
                FieldPatch moved = f;
                moved.center = new Vector3(f.center.x + ox, f.center.y, f.center.z + oz);
                fields.Add(moved);
            }

            foreach (PropPlacement prop in d.Props)
            {
                double ax = d.X0 + prop.position.x, az = d.Z0 + prop.position.z;
                if (!InTile(ax, az, tx, tz, p.tileSize)) continue;
                PropPlacement moved = prop;
                moved.position = new Vector3((float)(ax - w.X0), prop.position.y, (float)(az - w.Z0));
                props.Add(moved);
            }
        }

        private enum SlotKind
        {
            Milestone,
            Roadside,
            CutFace,
            Cairn,
        }

        /// <summary>
        /// Small features on fixed road-distance slots (each with its own random stream, so a slot comes out the same in every
        /// window that sees it): milestones every 500 m, a cart or stone pile on about half of the 290 m slots outside the
        /// passes, rock faces above the deeper road cuttings and cairns along the upper part of each pass.
        /// </summary>
        private void SlotFeatures(GenWindow w, double tx, double tz, List<Place> places, List<FeaturePlace> feats, List<SettlementSite> sites,
            List<FieldPatch> fields, OccupancyMap occupancy, List<PropPlacement> props)
        {
            double s0 = road.S(w.Z0 - 20.0), s1 = road.S(w.Z0 + w.Size + 20.0);
            var slots = new List<(double s, SlotKind kind, long index)>();
            for (long k = (long)Math.Ceiling(s0 / JourneyPlan.MilestoneSpacing); k * JourneyPlan.MilestoneSpacing <= s1; k++)
            {
                if (k != 0) slots.Add((k * JourneyPlan.MilestoneSpacing, SlotKind.Milestone, k));
            }

            for (long k = (long)Math.Floor(s0 / RoadsideSpacing) - 1; k * RoadsideSpacing <= s1 + RoadsideSpacing; k++)
            {
                if (EndlessNoise.Unit(p.seed, 720, k) > 0.5) continue;
                double s = k * RoadsideSpacing + 60.0 * EndlessNoise.Unit(p.seed, 721, k);
                if (s >= s0 && s <= s1) slots.Add((s, SlotKind.Roadside, k));
            }

            for (long k = (long)Math.Floor(s0 / 40.0); k * 40.0 <= s1; k++) slots.Add((k * 40.0, SlotKind.CutFace, k));
            for (long k = (long)Math.Floor(s0 / 100.0); k * 100.0 <= s1; k++) slots.Add((k * 100.0 + 50.0, SlotKind.Cairn, k));
            slots.Sort((a, b) => a.s != b.s ? a.s.CompareTo(b.s) : a.kind.CompareTo(b.kind));

            foreach (var (s, kind, index) in slots)
            {
                Region region = regions.At(s);
                bool pass = region.Kind == RegionKind.HighlandPass;
                if ((kind == SlotKind.CutFace || kind == SlotKind.Cairn) && !pass) continue;
                if (kind == SlotKind.Roadside && pass) continue;
                if (kind == SlotKind.Cairn && (profile.Height(s) < region.ColAltitude - 75f || Math.Abs(s - region.Col) < 90.0)) continue;
                if (kind != SlotKind.CutFace && NearPlace(s, places, feats, kind == SlotKind.Milestone ? 20.0 : 80.0)) continue;

                double z = road.ZAt(s);
                double x = road.X(z, out double slope, out _);
                double len = Math.Sqrt(1.0 + slope * slope);
                var tangent = new Vector3((float)(slope / len), 0f, (float)(1.0 / len));
                var normal = new Vector3(tangent.z, 0f, -tangent.x);
                float roadHeight = profile.Height(s);
                var on = new Vector3(w.LocalX(x), roadHeight, w.LocalZ(z));
                if (on.x < -30f || on.z < -30f || on.x > w.Size + 30f || on.z > w.Size + 30f) continue;

                var rng = new System.Random(EndlessNoise.Seed(p.seed, 730 + (int)kind, index));
                var slotProps = new List<PropPlacement>();
                var ctx = new ScatterPlanner.FeatureContext(w.Params, w.Map, w.Roads, w.Cover, sites, fields, occupancy, keys, slotProps, rng);
                switch (kind)
                {
                    case SlotKind.Milestone:
                    {
                        Vector3 pos = on + normal * (ctx.Corridor + 0.9f);
                        if (ctx.Clear(pos, 0.7f, 0.7f))
                        {
                            occupancy.Add(pos, 0.7f);
                            ctx.Add(PropRole.StoneRunes, pos, ScatterPlanner.Yaw(-normal), 0.42f);
                            for (int i = 0; i < slotProps.Count; i++)
                            {
                                PropPlacement milestone = slotProps[i];
                                milestone.settlementId = JourneyPlan.MakeId(PlaceKind.Milestone, index);
                                slotProps[i] = milestone;
                            }
                        }

                        break;
                    }

                    case SlotKind.Roadside:
                    {
                        Vector3 side = rng.Next(2) == 0 ? normal : -normal;
                        double roll = 0.4 + 0.6 * rng.NextDouble();
                        ScatterPlanner.PlaceRoadsideItem(ctx, on, side, tangent, roll);
                        break;
                    }

                    case SlotKind.CutFace:
                        ScatterPlanner.TryCutFace(ctx, on, normal, roadHeight);
                        break;

                    case SlotKind.Cairn:
                    {
                        float sideSign = index % 2 == 0 ? 1f : -1f;
                        for (int attempt = 0; attempt < 2; attempt++, sideSign = -sideSign)
                        {
                            Vector3 pos = on + normal * sideSign * (ctx.Corridor + 3f + (float)rng.NextDouble() * 1.5f);
                            if (!ctx.Clear(pos, 1.2f, 1f)) continue;
                            occupancy.Add(pos, 1.2f);
                            string family = index % 2 == 0 ? PropRole.StoneStack : PropRole.StonePile;
                            int variant = keys.PickVariant(family, rng);
                            float r = (float)rng.NextDouble();
                            float height = family == PropRole.StoneStack ? 2.2f + 0.6f * r : 1.1f + 0.4f * r;
                            ctx.AddVariant(family, variant, pos, (float)(rng.NextDouble() * 360.0), ScatterPlanner.ScaleFor(ctx, family, variant, height, 0.9f + 0.3f * r));
                            break;
                        }

                        break;
                    }
                }

                foreach (PropPlacement prop in slotProps)
                {
                    double ax = w.X0 + prop.position.x, az = w.Z0 + prop.position.z;
                    if (InTile(ax, az, tx, tz, p.tileSize)) props.Add(prop);
                }
            }
        }

        private static bool NearPlace(double s, List<Place> places, List<FeaturePlace> feats, double margin)
        {
            foreach (Place place in places)
            {
                if (place.Kind == PlaceKind.Lake) continue;
                if (Math.Abs(place.S - s) < place.Radius + margin) return true;
            }

            foreach (FeaturePlace f in feats)
            {
                if (Math.Abs(f.Place.S - s) < 30.0 + margin) return true;
            }

            return false;
        }

        private void AddWaypoints(TileBlueprint bp, double tx, double tz, float tile)
        {
            double s0 = road.S(tz - 400.0), s1 = road.S(tz + tile + 400.0);
            var list = new List<Waypoint>();
            owner.Waypoints(s0, s1, list);
            foreach (Waypoint wp in list)
            {
                if (InTile(wp.X, wp.Z, tx, tz, tile)) bp.Waypoints.Add(wp);
            }
        }

        // ------------------------------------------------------------------ far

        private TileBlueprint Far(TileCoord coord)
        {
            float tile = p.tileSize;
            double tx = coord.OriginX(tile), tz = coord.OriginZ(tile);
            int heightRes = Math.Max(3, p.farHeightResolution);
            float spacing = tile / (heightRes - 1);
            int padCells = Math.Max(1, (int)Math.Round(FarPad / spacing));
            float pad = padCells * spacing;
            int cells = (heightRes - 1) + 2 * padCells;
            float water = field.WaterLevelForSquare(tx, tz, tile, out _);
            GenWindow w = GenWindow.Create(p, field, road, tx - pad, tz - pad, cells, spacing, water);
            TileWindow window = w.Params.window;
            window.chunkSize = ChunkSizeFor(tile);
            window.keepMinX = tx;
            window.keepMaxX = tx + tile;
            window.keepMinZ = tz;
            window.keepMaxZ = tz + tile;

            int splatRes = Math.Max(1, p.farSplatResolution);
            float texel = tile / splatRes;
            int coverRes = (int)Math.Round(w.Size / texel);
            float maxDistance = Mathf.Max(30f, w.Params.ScatterCorridor + 12f);
            w.Roads = w.DistanceField(coverRes, maxDistance);
            w.BuildCover(p, field, coverRes);

            var bp = new TileBlueprint { Coord = coord, Lod = TileLod.Far, TileSize = tile, HeightRange = p.heightRange, WaterHeight = water };
            bp.Heights = new float[heightRes, heightRes];
            for (int j = 0; j < heightRes; j++)
            {
                for (int i = 0; i < heightRes; i++) bp.Heights[j, i] = w.Map[j + padCells, i + padCells];
            }

            // Settlement discs keep the trees off them (dressings themselves are not needed for a silhouette).
            var sites = new List<SettlementSite>();
            double sLo = road.S(w.Z0) - 300.0, sHi = road.S(w.Z0 + w.Size) + 300.0;
            var places = new List<Place>();
            plan.Inns(sLo, sHi, places);
            plan.RegionPlaces(sLo, sHi, places);
            foreach (Place place in places)
            {
                if (!place.IsSettlement) continue;
                SettlementDisc disc = field.Disc(place);
                if (!Overlaps(w, disc.X, disc.Z, place.Radius + 40.0)) continue;
                sites.Add(new SettlementSite(place.Name ?? "Inn", place.DressAs, new Vector3(w.LocalX(disc.X), disc.Level, w.LocalZ(disc.Z))));
            }

            var fields = new List<FieldPatch>();
            var patchwork = new List<MosaicField>();
            mosaic.Overlapping(w.X0, w.Z0, w.Size, patchwork);
            foreach (MosaicField f in patchwork)
            {
                FieldPatch patch = FieldMosaic.ToPatch(f, w.X0, w.Z0, 0f);
                if (f.Fallow) patch.crop = PropRole.CropField;
                fields.Add(patch);
            }

            RoadDistanceField splatRoads = w.DistanceField(pad, tile, splatRes, maxDistance);
            bp.Splat = TerrainShaper.ComputeSplat(w.Params, splatRoads, w.Cover, sites, fields, pad, pad);

            var trees = new List<TreePlacement>();
            var unused = new List<PropPlacement>();
            ScatterPlanner.Scatter(w.Params, w.Map, w.Roads, w.Cover, sites, fields, new OccupancyMap(w.Size), keys, farRules, trees, unused);
            foreach (TreePlacement t in trees)
            {
                if (t.scale < 0.95f) continue;
                TreePlacement local = t;
                local.position = new Vector3(t.position.x - pad, t.position.y, t.position.z - pad);
                bp.Trees.Add(local);
            }

            bp.DetailResolution = 0;
            AddWaypoints(bp, tx, tz, tile);
            return bp;
        }
    }
}
