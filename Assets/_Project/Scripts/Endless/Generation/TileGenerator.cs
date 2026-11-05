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

        /// <summary>Randomness stream of the roadside slots (whether a slot is used; the next stream gives its offset).</summary>
        public const int RoadsideStream = 720;

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
        private readonly GradientNoise streamNoise;
        private readonly Planting planting;

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
            rules = ScatterRules.Endless(EndlessGenerator.TreeLine);
            // Far tiles: the same table with everything but the trees and the rocks switched off, so each rule keeps its index and
            // with it its chunk streams. Every tree is kept (far trees are the near trees minus what near-only props crowd out), and
            // the rocks are scattered too: they take the same room as on the near tiles, and the large ones are drawn far away.
            farRules = new List<ScatterRule>();
            foreach (ScatterRule rule in rules)
            {
                bool keep = PropRole.IsTree(rule.family) || FarRock(rule.family);
                farRules.Add(keep ? rule : new ScatterRule(rule.family, rule.biomes, 0f, rule.footprint));
            }

            detailRules = DetailPlanner.EndlessRules();
            streamNoise = new GradientNoise(EndlessNoise.Seed(p.seed, 1050, 0));
            planting = new Planting(p, keys, road, regions, plan, profile, field, mosaic, features);
        }

        /// <summary>Generates <paramref name="coord"/> at <paramref name="lod"/>.</summary>
        public TileBlueprint Generate(TileCoord coord, TileLod lod)
        {
            var clock = Stopwatch.StartNew();
            TileBlueprint bp = lod == TileLod.Near ? Near(coord) : Far(coord, lod == TileLod.Mid ? TileLod.Mid : TileLod.Far);
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

            // Places, their dressings and the settlement discs / fields they bring; the stream channels first.
            var occupancy = new OccupancyMap(w.Size);
            var streams = new List<StreamCourse>();
            field.Streams?.Overlapping(w.X0, w.Z0, w.Size, 30.0, streams);
            TileStreams.Occupy(w, streams, occupancy);
            TileStreams.Ground streamGround = TileStreams.Prepare(w, field, streams, p.heightRange);
            var waterRocks = new List<TileStreams.WaterRock>();
            var sites = new List<SettlementSite>();
            var fields = new List<FieldPatch>();
            var props = new List<PropPlacement>();
            var trees = new List<TreePlacement>();
            var paintSites = new List<SettlementSite>();
            var paths = new List<(double x0, double z0, double x1, double z1, float width)>();
            var cobbles = new List<(double x0, double z0, double x1, double z1, float width)>();   // a walled city's streets
            var castleRocks = new List<CityHill>();   // the castle rocks reaching the window (the terrain's own hills: CityPaving)
            var deckDressings = new List<Dressing>();   // walled cities whose terrace decks may reach the tile (TerraceDecks)
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
                if (disc.Hill != null && disc.Hill.Castle != null && Overlaps(w, disc.Hill.CX, disc.Hill.CZ, disc.Hill.RockReach)) castleRocks.Add(disc.Hill);
                if (!Overlaps(w, disc.X, disc.Z, 300.0 + FieldPatchwork.TrackReachMargin)) continue;
                Dressing d = dressings.Settlement(place);
                // A composed village's outer lanes run on as field tracks (2026-10-06); every tile within a boundary edge's length of a
                // track sees it, so the gateways it opens agree between tiles.
                if (!d.PaintSite && Overlaps(w, d.CentreX, d.CentreZ, d.Reach + FieldPatchwork.TrackReachMargin))
                    FieldPatchwork.Tracks(d, (x, z) => { road.Nearest(x, z, out double rd); return rd; }, paths);
                if (!Overlaps(w, disc.X, disc.Z, 300.0) || !Overlaps(w, d.CentreX, d.CentreZ, d.Reach)) continue;
                if (d.City != null && d.City.Decks.Count > 0) deckDressings.Add(d);
                Import(d, w, tx, tz, occupancy, sites, fields, props, trees, paintSites, paths, cobbles);
            }

            foreach (FeaturePlace f in feats)
            {
                if (!Overlaps(w, f.X, f.Z, 80.0)) continue;
                Dressing d = dressings.Feature(f);
                if (!Overlaps(w, d.CentreX, d.CentreZ, d.Reach)) continue;
                Import(d, w, tx, tz, occupancy, sites, fields, props, trees, paintSites, paths, cobbles);
            }

            // Landmarks (endless only): the structure, its props and its kept-clear ground, before the planting and the scatter.
            var landmarkList = new List<LandmarkSite>();
            owner.Landmarks.Overlapping(w.X0, w.Z0, w.Size, LandmarkDresser.WindowSize * 0.5, landmarkList);
            foreach (LandmarkSite l in landmarkList)
            {
                Dressing d = dressings.Landmark(l, mosaic);
                if (Overlaps(w, d.CentreX, d.CentreZ, d.Reach)) Import(d, w, tx, tz, occupancy, sites, fields, props, trees, paintSites, paths, cobbles);
            }

            // Structured planting (roadside lines, hedgerow trees, copses, crest trees) before the fields and the scatter fill in.
            Planting.Context planted = planting.Prepare(w, tx, tz, tile, occupancy, sites, fields);
            planted.Trunks = new List<Vector2>();
            planting.PlantTrees(planted, trees, props);

            // The farmland patchwork: fields (painted, kept clear) and the crops, fences and scarecrows of the planted ones.
            var patchwork = new List<MosaicField>();
            var pastures = new List<FieldPatch>();
            mosaic.Overlapping(w.X0, w.Z0, w.Size, patchwork);
            foreach (MosaicField f in patchwork)
            {
                FieldPatch patch = FieldMosaic.ToPatch(f, w.X0, w.Z0, field.Height(f.X, f.Z));
                patch.crop = FieldLook(f, p.seed);
                fields.Add(patch);
                occupancy.Add(patch.center, patch.halfExtents.magnitude + 1.5f);
                if (f.Fallow)
                {
                    // Pasture: grazed grass with a worn gateway on the side facing the road.
                    pastures.Add(patch);
                    paths.Add(Gateway(f));
                }

                if (patch.crop == PropRole.StubbleField) Haystacks(w, f, patch, tx, tz, tile, props);
                if (f.Close) CloseItems(w, f, tx, tz, tile, trees, props);   // orchard trees, garden plants, scarecrows, haystacks (lived-in closes)
                if (f.Fallow || !f.Planted) continue;
                var rng = new System.Random(EndlessNoise.Seed(p.seed, 810, f.Id));
                var crops = new List<PropPlacement>();
                var gate = new SettlementSite("road", MedievalZen.Roads.SettlementType.Village, new Vector3(w.LocalX(f.GateX), 0f, w.LocalZ(f.GateZ)));
                ScatterPlanner.FillField(w.Params, w.Map, keys, rng, patch, false, gate, crops);
                foreach (PropPlacement crop in crops)
                {
                    // The field's own fence is left out: the patchwork's boundaries (FieldBoundaries) enclose every field.
                    if (crop.role == PropRole.Fence || crop.role == PropRole.FenceGate || crop.role == PropRole.FencePost) continue;
                    if (InTile(w.X0 + crop.position.x, w.Z0 + crop.position.z, tx, tz, tile)) props.Add(crop);
                }
            }

            Lap(clock, "dressings");

            // The streams' rocks, stepping stones and bridges.
            TileStreams.Props(w, streamGround, p, keys, road, profile, occupancy, tx, tz, tile, props, waterRocks, paths);
            Lap(clock, "streams");

            // Per-slot roadside features in road order.
            SlotFeatures(w, tx, tz, places, feats, sites, fields, occupancy, props);
            PassScree(w, tx, tz, sites, fields, occupancy, props);
            PassOutcrops(w, tx, tz, sites, fields, occupancy, props);
            Lap(clock, "slots");

            // Field boundaries (endless only): the scatter keeps off every boundary line; the tile's pieces are kept for the instanced renderer.
            owner.Boundaries.Occupy(w, occupancy);
            var pieces = new List<PropPlacement>();
            owner.Boundaries.Pieces(new FieldBoundaries.Context { W = w, Tx = tx, Tz = tz, Tile = tile, Sites = sites, Fields = fields, Trunks = planted.Trunks, Streams = streams, Paths = paths }, pieces);
            planting.WallIvy(w, pieces);   // the woods' stone walls carry ivy (forest pass)
            Lap(clock, "boundaries");

            // Countryside scatter (absolute chunks; only this tile's chunks are kept).
            var scatterProps = new List<PropPlacement>();
            ScatterPlanner.Scatter(w.Params, w.Map, w.Roads, w.Cover, sites, fields, occupancy, keys, rules, trees, scatterProps);
            props.AddRange(scatterProps);
            planting.PlantBushes(planted, props);
            Lap(clock, "scatter");

            // Ground paint on the tile's own square (1 m texels by default) and grass / flower density maps.
            int splatRes = Math.Max(1, p.nearSplatResolution);
            RoadDistanceField splatRoads = w.DistanceField(pad, tile, splatRes, maxDistance);
            Lap(clock, "splat: road field");
            bp.Splat = TerrainShaper.ComputeSplat(w.Params, splatRoads, w.Cover, paintSites, fields, pad, pad);
            Lap(clock, "splat: painter");
            planting.PaintWoodFloor(bp.Splat, w, pad, tile / splatRes, planted.Glades, trees, planted.Trunks);   // the wood floor under the canopy
            Lap(clock, "splat: wood floor");
            Planting.PaintGlades(bp.Splat, w, pad, tile / splatRes, planted.Glades);
            Lap(clock, "splat: glades");
            FieldPatchwork.Paint(bp.Splat, w, pad, tile / splatRes, patchwork, p.seed);   // the patchwork's strips (endless only)
            Lap(clock, "splat: fields");
            TileStreams.Paint(bp.Splat, w, pad, streams, streamNoise);
            Lap(clock, "splat: streams");
            TileStreams.PaintPaths(bp.Splat, w, pad, paths, streamNoise);
            if (cobbles.Count > 0) TileStreams.PaintPaths(bp.Splat, w, pad, cobbles, streamNoise, TerrainShaper.CobbleLayer);
            CityPaving.PaintCastleRock(bp.Splat, w, pad, castleRocks, p.heightRange);   // the castle rock: cobbled top, rock cliff
            Lap(clock, "splat: paths");
            bp.MountainPaint = UsesMountainChannels(bp.Splat);
            bp.FieldPaint = UsesFieldChannels(bp.Splat);   // the patchwork's gold, yellow and green: the three field layers
            bp.LitterPaint = UsesChannels(bp.Splat, TerrainShaper.LeafLitterLayer, TerrainShaper.LeafLitterLayer + 1);   // the woods' leaf litter (cobblestone's slot)
            bp.CobblePaint = UsesChannels(bp.Splat, TerrainShaper.CobbleLayer, TerrainShaper.CobbleLayer + 1);
            Lap(clock, "splat");
            int detailRes = Math.Max(1, p.nearDetailResolution);
            int detailOffset = (int)Math.Round(pad / coverTexel);
            float[,,] coverSplat = TerrainShaper.Resample(bp.Splat, detailRes);
            bp.DetailResolution = detailRes;
            Lap(clock, "details: resample");
            List<DetailLayer> planned = DetailPlanner.Plan(w.Params, w.Roads, w.Cover, coverSplat, paintSites, fields, keys, detailRules, detailOffset, detailRes);
            Lap(clock, "details: planner");
            if (pastures.Count > 0) Graze(planned, pastures, detailOffset, detailRes, coverTexel);
            FieldPatchwork.ThinDetails(planned, w, detailOffset, detailRes, coverTexel, patchwork, p.seed);   // bare ploughed strips, thin stubble
            Lap(clock, "details: fields");
            planting.ShadeDetails(planned, w, detailOffset, detailRes, coverTexel, planted.Glades, trees, planted.Trunks);   // a low, sparse sward under the canopy, almost none under a crown
            Lap(clock, "details: shade");
            planting.GladeDetails(planned, w, detailOffset, detailRes, coverTexel, planted.Glades);
            TileStreams.KeepDetailsOut(planned, w, streams, detailOffset, detailRes, coverTexel);
            if (cobbles.Count > 0 || castleRocks.Count > 0) CityPaving.KeepDetailsOffPaving(planned, coverSplat, w, castleRocks, detailOffset, detailRes, coverTexel);   // no meadow on a city's paving
            bp.Details = CompactDetails(planned);
            Lap(clock, "details");

            // The Generic broadleaves near the road are drawn as the detailed trees (forest pass), then to tile-local coordinates.
            planting.Restyle(w, trees);
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
                // Countryside bushes, shrubs and ferns are drawn by the instanced tree renderer (no GameObject per plant); a
                // settlement's garden planting stays with its dressing.
                if (PropRole.IsInstancedPlant(prop.role) && prop.settlementId < 0)
                {
                    int variant = planting.RestyleBush(prop.role, prop.variant, w.X0 + prop.position.x, w.Z0 + prop.position.z);
                    bp.Trees.Add(new TreePlacement { role = prop.role, variant = variant, position = local.position, yaw = prop.yaw, scale = prop.scale });
                }
                else if (PropRole.IsOvergrowth(prop.role))
                {
                    bp.Pieces.Add(local);   // ivy on a landmark's walls: drawn instanced like the boundary pieces (no collider)
                }
                else
                {
                    bp.Props.Add(local);
                }
            }

            foreach (PropPlacement piece in pieces)
            {
                PropPlacement local = piece;
                local.position = new Vector3(piece.position.x - pad, piece.position.y, piece.position.z - pad);
                bp.Pieces.Add(local);
            }

            bp.StreamMesh = TileStreams.Mesh(streamGround, tx, tz, tile, waterRocks, out List<StreamRibbon> ribbons);
            bp.BridgeWalls = BridgeWalls.Build(streamGround.Decks, streamGround.LowestGround, p.seed, tx, tz, tx + tile, tz + tile);
            bp.TerraceDecks = TerraceDecks.Build(deckDressings, tx, tz, tile);
            Lap(clock, "props and streams");
            // The pass ranges' steep rock, and a walled city's castle rock wherever it reaches the tile (2026-10-09; nothing on other tiles).
            if (bp.MountainPaint || castleRocks.Count > 0) bp.RockSkin = RockSkinField.Build(w, coord, tx, tz, tile, padCells, bp.Splat, p.seed, p.roadWidth, bp.HeightRange, castleRocks);
            Lap(clock, "rock skin");
            bp.Streams = ribbons;
            AddWaypoints(bp, tx, tz, tile);
            return bp;
        }

        /// <summary>Strength of a pass range (<see cref="CoverSample.mountain"/>) from which its own rocks replace the Meadow ones.</summary>
        public const float PassRockThreshold = 0.35f;

        /// <summary>
        /// The rock family placed instead of <paramref name="family"/> where the pass range's strength is <paramref name="mountain"/>:
        /// cliffs become <see cref="PropRole.PassCliff"/>, boulders, piles and flat outcrops <see cref="PropRole.PassBoulder"/> (the
        /// small pebble piles and the grassy mounds stay); null to keep the family (outside the ranges and their outer foothills).
        /// </summary>
        public static string PassRockFamily(string family, float mountain)
        {
            if (mountain < PassRockThreshold) return null;
            switch (family)
            {
                case PropRole.Cliff:
                case PropRole.DirtCliff:
                case PropRole.GenericCliff: return PropRole.PassCliff;
                case PropRole.Rock:
                case PropRole.LowlandRock:
                case PropRole.RockPile:
                case PropRole.RockGround: return PropRole.PassBoulder;
                default: return null;
            }
        }

        /// <summary><see cref="PassRockFamily"/> at window-local <paramref name="pos"/>, or <paramref name="family"/> itself.</summary>
        private static string PassRock(GenWindow w, string family, Vector3 pos)
        {
            if (pos.x < 0f || pos.z < 0f || pos.x > w.Size || pos.z > w.Size) return family;
            return PassRockFamily(family, w.Cover.Sample(pos.x, pos.z).mountain) ?? family;
        }

        /// <summary>True when any texel of <paramref name="splat"/> has weight in the endless road's mountain channels.</summary>
        public static bool UsesMountainChannels(float[,,] splat) => UsesChannels(splat, TerrainShaper.MountainRockLayer, TerrainShaper.HeathLayer + 1);

        /// <summary>True when any texel of <paramref name="splat"/> has weight in the endless road's field channels (2026-10-06).</summary>
        public static bool UsesFieldChannels(float[,,] splat) => UsesChannels(splat, TerrainShaper.FieldGoldLayer, TerrainShaper.FieldGreenLayer + 1);

        /// <summary>True when any texel has weight over <see cref="MountainPaintThreshold"/> in channels [<paramref name="first"/>, <paramref name="end"/>).</summary>
        public static bool UsesChannels(float[,,] splat, int first, int end)
        {
            if (splat == null || splat.GetLength(2) <= first) return false;
            int n = splat.GetLength(0), m = splat.GetLength(1), channels = Math.Min(end, splat.GetLength(2));
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < m; x++)
                {
                    for (int c = first; c < channels; c++)
                    {
                        if (splat[z, x, c] > MountainPaintThreshold) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Weight under which a mountain channel does not count (the tile then keeps the eight shared layers; the weight folds onto its stand-in).</summary>
        public const float MountainPaintThreshold = 0.002f;

        /// <summary>
        /// Far tiles paint with their slopes scaled by this, so slope-driven rock and scree match the near tiles on average
        /// (measured over 180 tile pairs on seeds 12345, 42 and 777; see the far/near paint test).
        /// </summary>
        public const float FarPaintSlopeScale = 1.05f;

        /// <summary>
        /// <see cref="TileLod.Mid"/> tiles paint with their slopes (measured on the 4 m grid) scaled by this, for the same reason as
        /// <see cref="FarPaintSlopeScale"/> (calibrated the same way, see the far/near paint test).
        /// </summary>
        public const float MidPaintSlopeScale = 1.02f;

        /// <summary>Rock families a far tile scatters (all but the small piles): they take room as on the near tiles.</summary>
        public static bool FarRock(string family) => PropRole.IsRock(family) && family != PropRole.RockSmallPile;

        /// <summary>A far tile keeps (and draws) a scattered rock whose measured height is at least this, metres.</summary>
        public const float FarRockMinHeight = 1.4f;

        /// <summary>Families of a settlement's dressing a far tile keeps, drawn as simple house solids.</summary>
        public static bool FarHouse(string family) => family == PropRole.Cabin || family == PropRole.Windmill || PropRole.IsBuilding(family) ||
            family == PropRole.CityWall || family == PropRole.CityTower ||   // a walled city's wall and towers (its crenellations are near-only)
            family == PropRole.CastleModule || family == PropRole.CastlePiece;   // its castle: the mass that stands over the town from afar

        /// <summary>Families of a landmark's dressing a far tile keeps: the structure and the standing stones (drawn as solids).</summary>
        public static bool FarLandmark(string family) => PropRole.IsLandmark(family) || family == PropRole.StoneRunes;

        /// <summary>Haystacks stand in the stubble fields whose centre is within this distance of the road, metres.</summary>
        public const double HaystackReach = 320.0;

        /// <summary>
        /// Two to five haystacks in a stubble field near the road (most fields; randomness stream 1216), in rows across the field like
        /// stooks left to dry, kept off its edges; those in the tile are appended to <paramref name="props"/> (window coordinates).
        /// </summary>
        private void Haystacks(GenWindow w, MosaicField f, FieldPatch patch, double tx, double tz, float tile, List<PropPlacement> props)
        {
            if (!keys.Has(PropRole.Haystack)) return;
            road.Nearest(f.X, f.Z, out double distance);
            if (distance > HaystackReach) return;
            var rng = new System.Random(EndlessNoise.Seed(p.seed, 1216, f.Id));
            if (rng.NextDouble() < 0.3) return;
            int count = 2 + rng.Next(4);
            double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            for (int i = 0; i < count; i++)
            {
                double u = (rng.NextDouble() * 2.0 - 1.0) * Math.Max(0.0, f.HalfX - 7.0);
                double v = (rng.NextDouble() * 2.0 - 1.0) * Math.Max(0.0, f.HalfZ - 7.0);
                double ax = f.X + u * cs + v * sn, az = f.Z - u * sn + v * cs;
                int variant = keys.PickVariant(PropRole.Haystack, rng);
                float yaw = (float)(rng.NextDouble() * 360.0), scale = 0.85f + 0.3f * (float)rng.NextDouble();
                if (!InTile(ax, az, tx, tz, tile)) continue;
                float lx = w.LocalX(ax), lz = w.LocalZ(az);
                if (!w.Roads.IsClear(lx, lz, w.Params.ScatterCorridor + 3f)) continue;
                float y = ScatterPlanner.GroundHeight(w.Params, w.Map, lx, lz, 1f) - 0.05f;
                props.Add(new PropPlacement { role = PropRole.Haystack, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale, settlementId = -1 });
            }
        }

        /// <summary>Fraction of the distant (unplanted) wheat fields that still show as ploughed earth.</summary>
        public const double PloughedShare = 0.25;

        /// <summary>
        /// How a farmland field is painted: fallow fields are grazed pasture; planted fields keep their crop (tilled earth,
        /// crops, fences); the paint-only fields further out read as a patchwork of stubble (wheat), flowering meadow strips
        /// (rapeseed, sunflowers) and, for a quarter of the wheat fields, ploughed earth, instead of bands of bare earth.
        /// </summary>
        public static string FieldLook(MosaicField f, int seed)
        {
            if (f.Fallow) return PropRole.PastureField;
            if (f.Planted) return f.Crop;
            if (f.Crop != PropRole.CropField) return PropRole.MeadowStrip;
            return EndlessNoise.Unit(seed, 811, f.Id) < PloughedShare ? PropRole.CropField : PropRole.StubbleField;
        }

        /// <summary>A short worn strip (absolute segment and width) from the edge of a pasture facing its gate into the field.</summary>
        public static (double x0, double z0, double x1, double z1, float width) Gateway(MosaicField f)
        {
            double gx = f.GateX - f.X, gz = f.GateZ - f.Z;
            double len = Math.Sqrt(gx * gx + gz * gz);
            if (len < 1e-6) return (f.X, f.Z, f.X, f.Z, 0f);
            double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            // Field axes (as FieldPatch.Contains): right = (cos, -sin), forward = (sin, cos).
            double u = gx * cs - gz * sn, v = gx * sn + gz * cs;
            double t = Math.Min(f.HalfX / Math.Max(1e-6, Math.Abs(u)), f.HalfZ / Math.Max(1e-6, Math.Abs(v)));
            double ex = f.X + gx * t, ez = f.Z + gz * t;
            double nx = gx / len, nz = gz / len;
            return (ex + nx * 0.5, ez + nz * 0.5, ex - nx * 6.0, ez - nz * 6.0, 3.5f);
        }

        /// <summary>
        /// What stands in a village close (2026-10-06, <see cref="CloseContents"/>): its fruit trees are appended to <paramref name="trees"/>
        /// (instanced, near and distant tiles alike, under the trunk rules of the region), its garden plants, scarecrow and haystacks to
        /// <paramref name="props"/> (near tiles only; null on distant tiles). Only the items inside this tile are kept; window coordinates.
        /// </summary>
        private void CloseItems(GenWindow w, MosaicField f, double tx, double tz, float tile, List<TreePlacement> trees, List<PropPlacement> props)
        {
            var items = new List<CloseItem>();
            CloseContents.Plan(f, p.seed, mosaic.SiteOf(f.CellI, f.CellJ), (x, z) => { road.Nearest(x, z, out double d); return d; }, p.roadWidth * 0.5, items);
            foreach (CloseItem it in items)
            {
                if (!InTile(it.X, it.Z, tx, tz, tile)) continue;
                bool tree = it.Role == PropRole.FruitTree;
                if (!tree && props == null) continue;
                int count = keys.Count(it.Role);
                if (count == 0) continue;
                int variant = (int)(it.Pick % (uint)count);
                if (tree)
                {
                    // The trunk rules (Planting.MinTrunkDistance under the region's corridor); a close is never this near the road, but keep the rule explicit.
                    road.Nearest(it.X, it.Z, out double roadDistance);
                    float corridor = Planting.TrunkCorridor(regions.At(road.S(it.Z)).Kind, w.Params.ScatterCorridor);
                    if (roadDistance < Planting.MinTrunkDistance(Planting.TrunkRadiusOf(it.Role, variant), it.Scale, corridor, p.roadWidth * 0.5f)) continue;
                }

                float lx = w.LocalX(it.X), lz = w.LocalZ(it.Z);
                float y = ScatterPlanner.GroundHeight(w.Params, w.Map, lx, lz, tree ? 0.6f : 0.4f) - (tree ? 0f : 0.03f);
                if (tree) trees.Add(new TreePlacement { role = it.Role, variant = variant, position = new Vector3(lx, y, lz), yaw = it.Yaw, scale = it.Scale });
                else props.Add(new PropPlacement { role = it.Role, variant = variant, position = new Vector3(lx, y, lz), yaw = it.Yaw, scale = it.Scale, settlementId = -1 });
            }
        }

        /// <summary>Pastures are grazed: no tall grass or sunflowers inside them; a cell left without grass gets a short tuft.</summary>
        private static void Graze(List<DetailLayer> layers, List<FieldPatch> pastures, int offset, int res, float texel)
        {
            DetailLayer shortGrass = null;
            foreach (DetailLayer layer in layers)
            {
                if (layer.family == PropRole.ShortGrass && shortGrass == null) shortGrass = layer;
            }

            for (int z = 0; z < res; z++)
            {
                float wz = (z + offset + 0.5f) * texel;
                for (int x = 0; x < res; x++)
                {
                    float wx = (x + offset + 0.5f) * texel;
                    bool inside = false;
                    foreach (FieldPatch f in pastures)
                    {
                        if (!f.Contains(wx, wz, 0f)) continue;
                        inside = true;
                        break;
                    }

                    if (!inside) continue;
                    int i = z * res + x, grass = 0;
                    foreach (DetailLayer layer in layers)
                    {
                        if (layer.family == PropRole.TallGrass || layer.family == PropRole.SunflowerDetail) layer.density[i] = 0;
                        else if (layer.family == PropRole.MedGrass || layer.family == PropRole.ShortGrass) grass += layer.density[i];
                    }

                    if (grass == 0 && shortGrass != null) shortGrass.density[i] = 1;
                }
            }
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
            List<PropPlacement> props, List<TreePlacement> trees, List<SettlementSite> paintSites, List<(double, double, double, double, float)> paths,
            List<(double, double, double, double, float)> cobbles)
        {
            float ox = (float)(d.X0 - w.X0), oz = (float)(d.Z0 - w.Z0);
            foreach (Vector3 c in d.Circles) occupancy.Add(new Vector3(c.x + ox, 0f, c.z + oz), c.y);
            if (d.Site != null)
            {
                Vector3 c = d.Site.Position;
                var site = new SettlementSite(d.Site.Name, d.Site.Type, new Vector3(c.x + ox, c.y, c.z + oz));
                sites.Add(site);
                if (d.PaintSite) paintSites.Add(site);
            }

            foreach (var c in d.Cobbles) cobbles.Add((d.X0 + c.x0, d.Z0 + c.z0, d.X0 + c.x1, d.Z0 + c.z1, c.width));
            foreach (Vector4 path in d.Paths) paths.Add((d.X0 + path.x, d.Z0 + path.y, d.X0 + path.z, d.Z0 + path.w, 1.5f));            foreach (Vector3 yard in d.Yards) paths.Add((d.X0 + yard.x, d.Z0 + yard.z, d.X0 + yard.x, d.Z0 + yard.z, 2f * yard.y));
            foreach (TreePlacement tree in d.Trees)
            {
                double ax = d.X0 + tree.position.x, az = d.Z0 + tree.position.z;
                if (!InTile(ax, az, tx, tz, p.tileSize)) continue;
                TreePlacement moved = tree;
                moved.position = new Vector3((float)(ax - w.X0), tree.position.y, (float)(az - w.Z0));
                trees.Add(moved);
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
            Shelf,
            WallFoot,
            Outcrop,
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
                if (EndlessNoise.Unit(p.seed, RoadsideStream, k) > 0.5) continue;
                double s = k * RoadsideSpacing + 60.0 * EndlessNoise.Unit(p.seed, RoadsideStream + 1, k);
                if (s >= s0 && s <= s1) slots.Add((s, SlotKind.Roadside, k));
            }

            for (long k = (long)Math.Floor(s0 / 40.0); k * 40.0 <= s1; k++) slots.Add((k * 40.0, SlotKind.CutFace, k));
            for (long k = (long)Math.Floor(s0 / 100.0); k * 100.0 <= s1; k++) slots.Add((k * 100.0 + 50.0, SlotKind.Cairn, k));
            // Pass walls: boulders on the scree at the wall foot every 30 m, an outcrop on the wall every ~270 m (their search runs
            // up to 120 m from the road, so road points beyond the window still count).
            double w0 = road.S(w.Z0 - 130.0), w1 = road.S(w.Z0 + w.Size + 130.0);
            for (long k = (long)Math.Floor(w0 / 30.0); k * 30.0 <= w1; k++) slots.Add((k * 30.0 + 15.0, SlotKind.WallFoot, k));
            for (long k = (long)Math.Floor(w0 / 270.0); k * 270.0 <= w1; k++) slots.Add((k * 270.0 + 135.0, SlotKind.Outcrop, k));
            var shelves = new List<FlatZone>();
            profile.Shelves(s0, s1, shelves);
            foreach (FlatZone z in shelves) slots.Add((Math.Round((z.From + z.To) * 0.5, 1), SlotKind.Shelf, (long)Math.Round(z.From)));
            slots.Sort((a, b) => a.s != b.s ? a.s.CompareTo(b.s) : a.kind.CompareTo(b.kind));

            foreach (var (s, kind, index) in slots)
            {
                Region region = regions.At(s);
                bool pass = region.Kind == RegionKind.HighlandPass;
                if ((kind == SlotKind.CutFace || kind == SlotKind.Cairn || kind == SlotKind.WallFoot || kind == SlotKind.Outcrop) && !pass) continue;
                if (kind == SlotKind.Shelf && NearPlace(s, places, feats, 40.0)) continue;
                if (kind == SlotKind.Roadside && pass) continue;
                if (kind == SlotKind.Cairn && (profile.Height(s) < region.ColAltitude - 75f || Math.Abs(s - region.Col) < 90.0)) continue;
                if (kind != SlotKind.CutFace && NearPlace(s, places, feats, kind == SlotKind.Milestone ? 20.0 : 80.0)) continue;
                if (kind != SlotKind.CutFace && NearCrossing(s, kind == SlotKind.Milestone ? 45.0 : 60.0)) continue;

                double z = road.ZAt(s);
                double x = road.X(z, out double slope, out _);
                double len = Math.Sqrt(1.0 + slope * slope);
                var tangent = new Vector3((float)(slope / len), 0f, (float)(1.0 / len));
                var normal = new Vector3(tangent.z, 0f, -tangent.x);
                float roadHeight = profile.Height(s);
                var on = new Vector3(w.LocalX(x), roadHeight, w.LocalZ(z));
                float slotReach = kind == SlotKind.WallFoot || kind == SlotKind.Outcrop ? 130f : 30f;
                if (on.x < -slotReach || on.z < -slotReach || on.x > w.Size + slotReach || on.z > w.Size + slotReach) continue;

                var rng = new System.Random(EndlessNoise.Seed(p.seed, kind == SlotKind.Shelf ? 1070 : kind == SlotKind.WallFoot ? 1071 : kind == SlotKind.Outcrop ? 1072 : 730 + (int)kind, index));
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

                    case SlotKind.WallFoot:
                    {
                        // Where the floor meets a wall (the ground first steeper than 1 in 2.2 going out), 1-3 boulders or piles on
                        // the scree just below it, sometimes a cliff block against the wall itself.
                        foreach (float side in new[] { 1f, -1f })
                        {
                            float foot = WallFoot(x, z, normal, side, ctx.Corridor + 3f, 95f);
                            if (foot < 0f) continue;
                            int count = 2 + rng.Next(4);
                            for (int i = 0; i < count; i++)
                            {
                                float d = Math.Max(ctx.Corridor + 2.5f, foot - 1f - (float)rng.NextDouble() * 9f);
                                Vector3 pos = on + normal * side * d + tangent * ((float)rng.NextDouble() - 0.5f) * 26f;
                                double roll = rng.NextDouble();
                                string family = roll < 0.5 ? PropRole.Rock : roll < 0.82 ? PropRole.RockPile : PropRole.RockGround;
                                float height = family == PropRole.Rock ? 0.8f + 1.6f * (float)rng.NextDouble() : 1.3f + 1.6f * (float)rng.NextDouble();
                                if (!Inside(w, pos, 2f) || ctx.Occupancy.Overlaps(pos.x, pos.z, 1.2f)) continue;
                                family = PassRock(w, family, pos);
                                if (ctx.AddRock(family, pos.x, pos.z, height, out float radius)) occupancy.Add(pos, Mathf.Max(1.2f, 0.8f * radius));
                            }

                            if (rng.NextDouble() < 0.3)
                            {
                                Vector3 pos = on + normal * side * (foot + 2f + (float)rng.NextDouble() * 4f);
                                string family = PassRock(w, PropRole.Cliff, pos);
                                float height = family == PropRole.PassCliff ? 5f + 3.5f * (float)rng.NextDouble() : 4f + 3f * (float)rng.NextDouble();
                                if (Inside(w, pos, 4f) && !ctx.Occupancy.Overlaps(pos.x, pos.z, 3f) &&
                                    ctx.AddRock(family, pos.x, pos.z, height, out float radius)) occupancy.Add(pos, Mathf.Max(3f, 0.8f * radius));
                            }
                        }

                        break;
                    }

                    case SlotKind.Outcrop:
                    {
                        // An outcrop standing out of the wall 40-120 m from the road, overlooking it.
                        float side = rng.NextDouble() < 0.5 ? 1f : -1f;
                        float foot = WallFoot(x, z, normal, side, ctx.Corridor + 3f, 95f);
                        if (foot < 0f) break;
                        Vector3 pos = on + normal * side * (Mathf.Max(40f, foot + 12f) + (float)rng.NextDouble() * 50f) + tangent * ((float)rng.NextDouble() - 0.5f) * 40f;
                        float height = 6.5f + 2f * (float)rng.NextDouble();
                        if (Inside(w, pos, 8f) && !ctx.Occupancy.Overlaps(pos.x, pos.z, 6f) &&
                            ctx.AddRock(PassRock(w, PropRole.Cliff, pos), pos.x, pos.z, height, out float radius)) occupancy.Add(pos, Mathf.Max(5f, 0.8f * radius));
                        break;
                    }

                    case SlotKind.Shelf:
                    {
                        // A rest on the shelf: a log to sit on beside the road on its open side, and a small cairn by it.
                        float hr = HeightmapGenerator.SampleHeight(w.Map, w.Params, on.x + normal.x * 14f, on.z + normal.z * 14f);
                        float hl = HeightmapGenerator.SampleHeight(w.Map, w.Params, on.x - normal.x * 14f, on.z - normal.z * 14f);
                        Vector3 open = hr <= hl ? normal : -normal;
                        Vector3 seat = on + open * (ctx.Corridor + 1.2f);
                        if (ctx.Clear(seat, 1.4f, 0.3f))
                        {
                            occupancy.Add(seat, 1.4f);
                            int log = Math.Min(3 + rng.Next(2), keys.Count(PropRole.Stump) - 1);
                            if (log >= 0) ctx.AddVariant(PropRole.Stump, log, seat, ScatterPlanner.Yaw(tangent) + (float)(rng.NextDouble() * 16.0 - 8.0), 1f);
                            Vector3 cairn = seat + tangent * (2.6f + (float)rng.NextDouble()) + open * 0.6f;
                            if (ctx.Clear(cairn, 1f, 0.35f))
                            {
                                occupancy.Add(cairn, 1f);
                                int variant = keys.PickVariant(PropRole.StonePile, rng);
                                float r = (float)rng.NextDouble();
                                ctx.AddVariant(PropRole.StonePile, variant, cairn, (float)(rng.NextDouble() * 360.0), ScatterPlanner.ScaleFor(ctx, PropRole.StonePile, variant, 1.0f + 0.4f * r, 0.9f));
                            }
                        }

                        break;
                    }

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

        /// <summary>Edge of the absolute grid the pass ranges' scree boulders are strewn on, metres.</summary>
        public const double ScreeBoulderGrid = 9.0;

        /// <summary>
        /// Near tiles: boulders (<see cref="PropRole.PassBoulder"/>, 0.6-1.8 m, seated by <see cref="RockPlacer"/>) strewn over the
        /// talus fans and aprons of the pass ranges, up to two in five points of an absolute 9 m grid where the talus is thick
        /// (randomness streams 1272-1273), kept off the road's corridor like every rock.
        /// </summary>
        private void PassScree(GenWindow w, double tx, double tz, List<SettlementSite> sites, List<FieldPatch> fields, OccupancyMap occupancy, List<PropPlacement> props)
        {
            if (!keys.Has(PropRole.PassBoulder) || !keys.HasMetrics(PropRole.PassBoulder)) return;
            long gx0 = (long)Math.Floor((w.X0 + 3.0) / ScreeBoulderGrid), gx1 = (long)Math.Floor((w.X0 + w.Size - 3.0) / ScreeBoulderGrid);
            long gz0 = (long)Math.Floor((w.Z0 + 3.0) / ScreeBoulderGrid), gz1 = (long)Math.Floor((w.Z0 + w.Size - 3.0) / ScreeBoulderGrid);
            for (long gz = gz0; gz <= gz1; gz++)
            {
                for (long gx = gx0; gx <= gx1; gx++)
                {
                    double u = EndlessNoise.Unit(p.seed, 1272, gx, gz);
                    if (u >= 0.4) continue;
                    double ax = (gx + 0.15 + 0.7 * EndlessNoise.Unit(p.seed, 1272, gx, gz + 7919)) * ScreeBoulderGrid;
                    double az = (gz + 0.15 + 0.7 * EndlessNoise.Unit(p.seed, 1272, gx + 7919, gz)) * ScreeBoulderGrid;
                    var pos = new Vector3(w.LocalX(ax), 0f, w.LocalZ(az));
                    if (!Inside(w, pos, 3f)) continue;
                    CoverSample cv = w.Cover.Sample(pos.x, pos.z);
                    if (cv.mountain < PassRockThreshold || cv.talus < 0.5f || u >= 0.4 * cv.talus) continue;
                    if (occupancy.Overlaps(pos.x, pos.z, 1.5f)) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, 1273, gx, gz));
                    var placed = new List<PropPlacement>();
                    var ctx = new ScatterPlanner.FeatureContext(w.Params, w.Map, w.Roads, w.Cover, sites, fields, occupancy, keys, placed, rng);
                    float height = 0.6f + 1.2f * Mathf.Pow((float)rng.NextDouble(), 1.6f);
                    if (!ctx.AddRock(PropRole.PassBoulder, pos.x, pos.z, height, out float radius)) continue;
                    occupancy.Add(pos, Mathf.Max(1.2f, 0.8f * radius));
                    if (InTile(ax, az, tx, tz, p.tileSize)) props.AddRange(placed);
                }
            }
        }

        /// <summary>Edge of the absolute grid the crags of the pass ranges' moderate-slope outcrops are seated on, metres.</summary>
        public const double OutcropCragGrid = 20.0;

        /// <summary>
        /// Near and distant tiles (2026-10-06): a crag (<see cref="PropRole.PassCliff"/>, 2.5-5.5 m, seated by <see cref="RockPlacer"/>) with
        /// one to three boulders round it at the cores of the mountain-rock outcrops the paint lays on the moderate slopes of a range
        /// (<see cref="TerrainShaper.MountainOutcrop"/>, the same noise), on an absolute 20 m grid (randomness streams 1344-1345), kept off
        /// the road's corridor like every rock. Adds the placements whose anchor lies in the tile to <paramref name="props"/>.
        /// </summary>
        private void PassOutcrops(GenWindow w, double tx, double tz, List<SettlementSite> sites, List<FieldPatch> fields, OccupancyMap occupancy, List<PropPlacement> props)
        {
            if (!keys.Has(PropRole.PassCliff) || !keys.HasMetrics(PropRole.PassCliff)) return;
            var noise = new GradientNoise(w.Params.seed + 16);   // the paint's outcrop noise (TerrainShaper.ComputeSplat)
            long gx0 = (long)Math.Floor((w.X0 + 6.0) / OutcropCragGrid), gx1 = (long)Math.Floor((w.X0 + w.Size - 6.0) / OutcropCragGrid);
            long gz0 = (long)Math.Floor((w.Z0 + 6.0) / OutcropCragGrid), gz1 = (long)Math.Floor((w.Z0 + w.Size - 6.0) / OutcropCragGrid);
            for (long gz = gz0; gz <= gz1; gz++)
            {
                for (long gx = gx0; gx <= gx1; gx++)
                {
                    double u = EndlessNoise.Unit(p.seed, 1344, gx, gz);
                    if (u >= 0.85) continue;
                    double ax = (gx + 0.2 + 0.6 * EndlessNoise.Unit(p.seed, 1344, gx, gz + 7919)) * OutcropCragGrid;
                    double az = (gz + 0.2 + 0.6 * EndlessNoise.Unit(p.seed, 1344, gx + 7919, gz)) * OutcropCragGrid;
                    var pos = new Vector3(w.LocalX(ax), 0f, w.LocalZ(az));
                    if (!Inside(w, pos, 6f)) continue;
                    CoverSample cv = w.Cover.Sample(pos.x, pos.z);
                    if (cv.mountain < PassRockThreshold || cv.isWater || cv.talus > 0.3f || TerrainShaper.ModerateSlope(cv.slope) < 0.5f) continue;
                    float o = TerrainShaper.MountainOutcrop(noise, pos.x + w.Params.NoiseOriginX, pos.z + w.Params.NoiseOriginZ);
                    if (o < TerrainShaper.MountainOutcropStart + 0.1f) continue;
                    if (occupancy.Overlaps(pos.x, pos.z, 3f)) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, 1345, gx, gz));
                    var placed = new List<PropPlacement>();
                    var ctx = new ScatterPlanner.FeatureContext(w.Params, w.Map, w.Roads, w.Cover, sites, fields, occupancy, keys, placed, rng);
                    float height = 2.5f + 3f * (float)rng.NextDouble();
                    if (!ctx.AddRock(PropRole.PassCliff, pos.x, pos.z, height, out float radius)) continue;
                    occupancy.Add(pos, Mathf.Max(3f, 0.8f * radius));
                    int boulders = 1 + rng.Next(3);
                    if (keys.Has(PropRole.PassBoulder) && keys.HasMetrics(PropRole.PassBoulder))
                    {
                        for (int b = 0; b < boulders; b++)
                        {
                            double ang = rng.NextDouble() * Math.PI * 2.0, d = radius + 2.0 + 6.0 * rng.NextDouble();
                            var bp = new Vector3(pos.x + (float)(Math.Cos(ang) * d), 0f, pos.z + (float)(Math.Sin(ang) * d));
                            float bh = 0.6f + 1.0f * (float)rng.NextDouble();
                            if (!Inside(w, bp, 3f) || occupancy.Overlaps(bp.x, bp.z, 1.2f)) continue;
                            if (ctx.AddRock(PropRole.PassBoulder, bp.x, bp.z, bh, out float br)) occupancy.Add(bp, Mathf.Max(1.2f, 0.8f * br));
                        }
                    }

                    if (InTile(ax, az, tx, tz, p.tileSize)) props.AddRange(placed);
                }
            }
        }

        private static bool Inside(GenWindow w, Vector3 pos, float margin) => pos.x > margin && pos.z > margin && pos.x < w.Size - margin && pos.z < w.Size - margin;

        /// <summary>
        /// Distance from the road centre (absolute <paramref name="x"/>, <paramref name="z"/>) along <paramref name="normal"/> ×
        /// <paramref name="side"/> to the foot of a wall: the first point from <paramref name="from"/> out where the ground (the global
        /// field, so every window agrees) rises steeper than 1 in 2.2 over 3 m; -1 when there is none before <paramref name="to"/>.
        /// </summary>
        private float WallFoot(double x, double z, Vector3 normal, float side, float from, float to)
        {
            double nx = normal.x * side, nz = normal.z * side;
            float prev = field.Height(x + nx * from, z + nz * from);
            for (float d = from + 3f; d <= to; d += 3f)
            {
                float h = field.Height(x + nx * d, z + nz * d);
                if ((h - prev) / 3f > 0.45f) return d - 3f;
                prev = h;
            }

            return -1f;
        }

        private bool NearCrossing(double s, double margin)
        {
            StreamNetwork net = field.Streams;
            if (net == null) return false;
            for (long k = StreamPlan.CellOf(s) - 1; k <= StreamPlan.CellOf(s) + 1; k++)
            {
                Crossing c = net.Plan.Cell(k);
                if (c != null && Math.Abs(c.S - s) < margin && net.Accepted(c)) return true;
            }

            return false;
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

        private TileBlueprint Far(TileCoord coord, TileLod lod)
        {
            float tile = p.tileSize;
            double tx = coord.OriginX(tile), tz = coord.OriginZ(tile);
            int heightRes = Math.Max(3, lod == TileLod.Mid ? p.midHeightResolution : p.farHeightResolution);
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

            var bp = new TileBlueprint { Coord = coord, Lod = lod, TileSize = tile, HeightRange = p.heightRange, WaterHeight = water };
            bp.Heights = new float[heightRes, heightRes];
            for (int j = 0; j < heightRes; j++)
            {
                for (int i = 0; i < heightRes; i++) bp.Heights[j, i] = w.Map[j + padCells, i + padCells];
            }

            // Settlement discs keep the trees off them; only inns paint their ground (a worn yard), as on the near tiles (villages
            // paint only their roads and paths). The settlement's dressing (cached) brings its room, its trees (orchards, gardens)
            // and its houses, which a far tile draws as simple solids so a place can be seen from afar.
            var sites = new List<SettlementSite>();
            var paintSites = new List<SettlementSite>();
            var occupancy = new OccupancyMap(w.Size);
            var trees = new List<TreePlacement>();
            double sLo = road.S(w.Z0) - 300.0, sHi = road.S(w.Z0 + w.Size) + 300.0;
            var places = new List<Place>();
            plan.Inns(sLo, sHi, places);
            plan.RegionPlaces(sLo, sHi, places);
            places.Sort((a, b) => a.S != b.S ? a.S.CompareTo(b.S) : a.Id.CompareTo(b.Id));
            foreach (Place place in places)
            {
                if (!place.IsSettlement) continue;
                SettlementDisc disc = field.Disc(place);
                if (!Overlaps(w, disc.X, disc.Z, place.Radius + 40.0)) continue;
                var site = new SettlementSite(place.Name ?? "Inn", place.DressAs, new Vector3(w.LocalX(disc.X), disc.Level, w.LocalZ(disc.Z)));
                sites.Add(site);
                if (place.Kind == PlaceKind.Inn) paintSites.Add(site);
                Dressing d = dressings.Settlement(place);
                if (!Overlaps(w, d.CentreX, d.CentreZ, d.Reach)) continue;
                ImportFar(d, w, tx, tz, occupancy, trees, bp.Props);
            }

            // Landmarks: their kept-clear ground and trees as on the near tiles, their structures (and standing stones) as solids.
            var landmarkList = new List<LandmarkSite>();
            owner.Landmarks.Overlapping(w.X0, w.Z0, w.Size, LandmarkDresser.WindowSize * 0.5, landmarkList);
            foreach (LandmarkSite l in landmarkList)
            {
                Dressing d = dressings.Landmark(l, mosaic);
                if (Overlaps(w, d.CentreX, d.CentreZ, d.Reach)) ImportFar(d, w, tx, tz, occupancy, trees, bp.Props);
            }

            var fields = new List<FieldPatch>();
            var patchwork = new List<MosaicField>();
            mosaic.Overlapping(w.X0, w.Z0, w.Size, patchwork);
            foreach (MosaicField f in patchwork)
            {
                FieldPatch patch = FieldMosaic.ToPatch(f, w.X0, w.Z0, 0f);
                patch.crop = FieldLook(f, p.seed);
                fields.Add(patch);
                if (f.Close) CloseItems(w, f, tx, tz, tile, trees, null);   // the orchards' trees, as on the near tiles
            }

            // Paint sampled on the grid points (splatRes + 1 per edge, texel k at k * texel from the tile's corner), so the edge
            // samples of neighbouring far tiles are the same points.
            float gridOffset = pad - texel * 0.5f;
            RoadDistanceField splatRoads = w.DistanceField(gridOffset, tile + texel, splatRes + 1, maxDistance);
            // Slopes measured on the coarse grid are shallower than on the near tiles' 2 m grid, so rock and scree would be
            // under-painted at a distance: the paint sees slopes scaled up (the trees below still see the true ones).
            float slopeScale = lod == TileLod.Mid ? MidPaintSlopeScale : FarPaintSlopeScale;
            float[] slopes = (float[])w.Cover.Slope.Clone(), scree = (float[])w.Cover.Scree.Clone();
            for (int k = 0; k < slopes.Length; k++) w.Cover.Slope[k] = slopes[k] * slopeScale;
            w.Cover.ComputeScree(p.heightRange);
            bp.Splat = TerrainShaper.ComputeSplat(w.Params, splatRoads, w.Cover, paintSites, fields, gridOffset, gridOffset);
            Planting.Context farPlanting = planting.Prepare(w, tx, tz, tile, occupancy, sites, fields);
            planting.PaintWoodFloor(bp.Splat, w, gridOffset, texel, farPlanting.Glades);   // the same wood floor as the near tiles
            Planting.PaintGlades(bp.Splat, w, gridOffset, texel, farPlanting.Glades);
            FieldPatchwork.Paint(bp.Splat, w, gridOffset, texel, patchwork, p.seed);   // the same strips as the near tiles
            Array.Copy(slopes, w.Cover.Slope, slopes.Length);
            Array.Copy(scree, w.Cover.Scree, scree.Length);
            var streams = new List<StreamCourse>();
            field.Streams?.Overlapping(w.X0, w.Z0, w.Size, 20.0, streams);
            bp.Streams = TileStreams.Ribbons(streams, tx, tz, tile);
            TileStreams.Occupy(w, streams, occupancy);
            planting.PlantTrees(farPlanting, trees, null);
            owner.Boundaries.Occupy(w, occupancy);
            if (lod == TileLod.Mid) owner.Boundaries.FarEdges(w, tx, tz, tile, sites, streams, bp.FieldEdges);

            var rocks = new List<PropPlacement>();
            ScatterPlanner.Scatter(w.Params, w.Map, w.Roads, w.Cover, sites, fields, occupancy, keys, farRules, trees, rocks);
            PassOutcrops(w, tx, tz, sites, fields, occupancy, rocks);
            planting.Restyle(w, trees);
            foreach (TreePlacement t in trees)
            {
                TreePlacement local = t;
                local.position = new Vector3(t.position.x - pad, t.position.y, t.position.z - pad);
                bp.Trees.Add(local);
            }

            // The large rocks (cliff blocks, outcrops, boulders) the near tile would seat here, drawn far away as faceted solids.
            foreach (PropPlacement rock in rocks)
            {
                if (!FarRock(rock.role) || !keys.TryGetMetrics(rock.role, rock.variant, out PropMetrics m)) continue;
                if (m.Height * (rock.scale > 0f ? rock.scale : 1f) < FarRockMinHeight) continue;
                PropPlacement local = rock;
                local.position = new Vector3(rock.position.x - pad, rock.position.y, rock.position.z - pad);
                bp.Props.Add(local);
            }

            bp.DetailResolution = 0;
            AddWaypoints(bp, tx, tz, tile);
            return bp;
        }

        /// <summary>
        /// A settlement's dressing on a far tile: its room (so the scatter keeps clear of it as on the near tiles), its trees (window
        /// coordinates, like the scatter's) and its houses (<see cref="FarHouse"/>, tile-local) whose centre is in the tile.
        /// </summary>
        private void ImportFar(Dressing d, GenWindow w, double tx, double tz, OccupancyMap occupancy, List<TreePlacement> trees, List<PropPlacement> houses)
        {
            float ox = (float)(d.X0 - w.X0), oz = (float)(d.Z0 - w.Z0);
            foreach (Vector3 c in d.Circles) occupancy.Add(new Vector3(c.x + ox, 0f, c.z + oz), c.y);
            foreach (TreePlacement tree in d.Trees)
            {
                if (!PropRole.IsTree(tree.role)) continue;   // a landmark's bushes are near-only
                double ax = d.X0 + tree.position.x, az = d.Z0 + tree.position.z;
                if (!InTile(ax, az, tx, tz, p.tileSize)) continue;
                TreePlacement moved = tree;
                moved.position = new Vector3((float)(ax - w.X0), tree.position.y, (float)(az - w.Z0));
                trees.Add(moved);
            }

            // A walled city's castle: its mass as a few dozen blocks (CastleLayout.FarProxies) in place of its pieces one by one.
            var castle = new List<PropPlacement>();
            CastleLayout.FarProxies(d, castle, out int castleStart, out int castleCount);
            for (int i = 0; i < d.Props.Count + castle.Count; i++)
            {
                if (i >= castleStart && i < castleStart + castleCount) continue;
                PropPlacement prop = i < d.Props.Count ? d.Props[i] : castle[i - d.Props.Count];
                if (!FarHouse(prop.role) && !FarLandmark(prop.role) && !FarCastle.IsProxy(prop.role)) continue;
                double ax = d.X0 + prop.position.x, az = d.Z0 + prop.position.z;
                if (!InTile(ax, az, tx, tz, p.tileSize)) continue;
                PropPlacement moved = prop;
                moved.position = new Vector3((float)(ax - tx), prop.position.y, (float)(az - tz));
                houses.Add(moved);
            }
        }
    }
}
