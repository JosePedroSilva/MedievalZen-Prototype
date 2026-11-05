using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Countryside scatter: crop fields (wheat, rapeseed, sunflowers with fences and scarecrows) around villages
    /// and towns; the data-driven <see cref="ScatterRules"/> table sampled per 100 m chunk; and hand-composed
    /// features (roadside camps and carts, cairns on crests, rune stones, a water wheel). Nothing is placed within
    /// <see cref="WorldGenParams.ScatterCorridor"/> of a road, inside settlement radius + 10 m, on crop fields or
    /// (except lilies) on water.
    /// </summary>
    public static class ScatterPlanner
    {
        /// <summary>Scatter chunk size in metres (each chunk and rule has its own seeded random stream).</summary>
        public const float ChunkSize = 100f;

        /// <summary>Extra keep-out around settlement discs.</summary>
        public const float SettlementMargin = 10f;

        private const float CropSpacing = 3f;

        // ------------------------------------------------------------------ fields

        /// <summary>
        /// Lays out 2–4 fields per village/town (away from roads, water and other settlements), planted with wheat,
        /// rapeseed or sunflowers in rows; fences most of them (posts at the corners, a gate facing the settlement)
        /// and adds scarecrows.
        /// </summary>
        public static List<FieldPatch> PlanFields(WorldGenParams p, float[,] map, RoadDistanceField roads, LandCover cover,
            IReadOnlyList<SettlementSite> sites, OccupancyMap occupancy, PaletteKeys keys, List<PropPlacement> props)
        {
            var fields = new List<FieldPatch>();
            var rng = new System.Random(p.seed + 5);
            float corridor = p.ScatterCorridor;
            for (int si = 0; si < sites.Count; si++)
            {
                SettlementSite site = sites[si];
                if (site.Type == SettlementType.Inn) continue;
                bool town = site.Type == SettlementType.Town;
                int count = town ? 3 + rng.Next(2) : 2 + rng.Next(2);
                for (int k = 0; k < count; k++)
                {
                    string crop = PickCrop(keys, rng, k);
                    if (crop == null) break;
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                        float dist = site.Radius + 30f + (float)rng.NextDouble() * 90f;
                        var center = new Vector3(site.Position.x + Mathf.Cos(angle) * dist, 0f, site.Position.z + Mathf.Sin(angle) * dist);
                        var half = new Vector2(10f + (float)rng.NextDouble() * 10f, 7f + (float)rng.NextDouble() * 7f);
                        float yaw = angle * Mathf.Rad2Deg + (float)(rng.NextDouble() * 40.0 - 20.0);
                        var field = new FieldPatch { center = center, halfExtents = half, yaw = yaw, settlementId = si, crop = crop };
                        if (!FieldFits(p, map, roads, cover, sites, fields, occupancy, field, corridor)) continue;

                        center.y = HeightmapGenerator.SampleHeight(map, p, center.x, center.z);
                        field.center = center;
                        fields.Add(field);
                        occupancy.Add(center, half.magnitude + 1.5f);
                        FillField(p, map, keys, rng, field, town, site, props);
                        break;
                    }
                }
            }

            return fields;
        }

        private static string PickCrop(PaletteKeys keys, System.Random rng, int index)
        {
            // The first field of every settlement is wheat; the rest mix in yellow rapeseed and sunflowers.
            double roll = index == 0 ? 0.0 : rng.NextDouble();
            string crop = roll < 0.45 ? PropRole.CropField : roll < 0.8 ? PropRole.Rapeseed : PropRole.Sunflower;
            if (keys.Has(crop)) return crop;
            if (keys.Has(PropRole.CropField)) return PropRole.CropField;
            if (keys.Has(PropRole.Rapeseed)) return PropRole.Rapeseed;
            return keys.Has(PropRole.Sunflower) ? PropRole.Sunflower : null;
        }

        private static bool FieldFits(WorldGenParams p, float[,] map, RoadDistanceField roads, LandCover cover, IReadOnlyList<SettlementSite> sites,
            List<FieldPatch> fields, OccupancyMap occupancy, FieldPatch field, float corridor)
        {
            float diag = field.halfExtents.magnitude + 1.5f;
            Vector3 c = field.center;
            if (c.x < diag + 20f || c.z < diag + 20f || c.x > p.worldSize - diag - 20f || c.z > p.worldSize - diag - 20f) return false;

            for (int i = 0; i < sites.Count; i++)
            {
                float d = RoadPlanner.PlanarDistance(sites[i].Position, c);
                if (d < sites[i].Radius + SettlementMargin + diag) return false;
            }

            for (int i = 0; i < fields.Count; i++)
            {
                float d = RoadPlanner.PlanarDistance(fields[i].center, c);
                if (d < fields[i].halfExtents.magnitude + diag + 4f) return false;
            }

            if (occupancy.Overlaps(c.x, c.z, diag)) return false;
            if (HeightmapGenerator.SampleSlope(map, p.worldSize, p.hillAmplitude, c.x, c.z) > 0.2f) return false;

            // Road clearance and dry ground over a grid covering the field plus its fence line.
            Axes(field.yaw, out Vector3 right, out Vector3 forward);
            for (int iz = -3; iz <= 3; iz++)
            {
                for (int ix = -3; ix <= 3; ix++)
                {
                    Vector3 pt = c + right * ((field.halfExtents.x + 1.5f) * ix / 3f) + forward * ((field.halfExtents.y + 1.5f) * iz / 3f);
                    if (!roads.IsClear(pt.x, pt.z, corridor + 1f)) return false;
                    int t = cover.Index(pt.x, pt.z);
                    if (cover.IsWater(t) || cover.Shore[t] > 0.3f) return false;
                }
            }

            return true;
        }

        internal static void FillField(WorldGenParams p, float[,] map, PaletteKeys keys, System.Random rng, FieldPatch field,
            bool town, SettlementSite site, List<PropPlacement> props)
        {
            Axes(field.yaw, out Vector3 right, out Vector3 forward);
            Vector3 c = field.center;
            float spacing = field.crop == PropRole.Sunflower ? 1.6f : CropSpacing;
            float rowSpacing = field.crop == PropRole.Sunflower ? 2.2f : CropSpacing;

            for (float lz = -field.halfExtents.y + rowSpacing * 0.5f; lz <= field.halfExtents.y; lz += rowSpacing)
            {
                for (float lx = -field.halfExtents.x + spacing * 0.5f; lx <= field.halfExtents.x; lx += spacing)
                {
                    float jx = (float)(rng.NextDouble() - 0.5) * 0.5f;
                    float jz = (float)(rng.NextDouble() - 0.5) * 0.5f;
                    Vector3 pos = c + right * (lx + jx) + forward * (lz + jz);
                    float yaw = field.crop == PropRole.Sunflower ? field.yaw + (float)(rng.NextDouble() * 40.0 - 20.0) : field.yaw + (rng.Next(2) == 0 ? 0f : 180f);
                    Add(field.crop, pos, yaw, 0.9f + (float)rng.NextDouble() * 0.2f);
                }
            }

            if (field.crop == PropRole.CropField && (town || rng.NextDouble() < 0.6))
            {
                Vector3 pos = c + right * ((float)(rng.NextDouble() - 0.5) * field.halfExtents.x) + forward * ((float)(rng.NextDouble() - 0.5) * field.halfExtents.y);
                Add(PropRole.Scarecrow, pos, (float)(rng.NextDouble() * 360.0), 1f);
            }

            if (!keys.Has(PropRole.Fence) || rng.NextDouble() > 0.75) return;

            // Fence around the field; the side facing the settlement gets a gate in the middle, corners get posts.
            float length = Mathf.Max(0.5f, keys.fenceLength);
            Vector3 toSite = site.Position - c;
            toSite.y = 0f;
            float ex = field.halfExtents.x + 0.8f;
            float ez = field.halfExtents.y + 0.8f;
            var sides = new[]
            {
                (from: c - right * ex - forward * ez, dir: right, len: 2f * ex, normal: -forward),
                (from: c + right * ex - forward * ez, dir: forward, len: 2f * ez, normal: right),
                (from: c + right * ex + forward * ez, dir: -right, len: 2f * ex, normal: forward),
                (from: c - right * ex + forward * ez, dir: -forward, len: 2f * ez, normal: -right),
            };

            int gateSide = 0;
            float bestDot = float.NegativeInfinity;
            for (int s = 0; s < 4; s++)
            {
                float dot = Vector3.Dot(sides[s].normal, toSite);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    gateSide = s;
                }
            }

            int fenceVariant = keys.PickVariant(PropRole.Fence, rng);
            for (int s = 0; s < 4; s++)
            {
                int pieces = Mathf.Max(1, Mathf.FloorToInt(sides[s].len / length));
                float start = (sides[s].len - pieces * length) * 0.5f;
                float yaw = Yaw(sides[s].dir);
                for (int i = 0; i < pieces; i++)
                {
                    Vector3 pos = sides[s].from + sides[s].dir * (start + (i + 0.5f) * length);
                    bool gate = s == gateSide && i == pieces / 2 && keys.Has(PropRole.FenceGate);
                    if (gate) Add(PropRole.FenceGate, pos, yaw, 1f);
                    else AddVariant(PropRole.Fence, fenceVariant, pos, yaw, 1f);
                }

                if (keys.Has(PropRole.FencePost)) Add(PropRole.FencePost, sides[s].from, yaw, 1f);
            }

            void Add(string role, Vector3 pos, float yaw, float scale) => AddVariant(role, keys.PickVariant(role, rng), pos, yaw, scale);

            void AddVariant(string role, int variant, Vector3 pos, float yaw, float scale)
            {
                if (!keys.Has(role)) return;
                pos.y = HeightmapGenerator.SampleHeight(map, p, pos.x, pos.z);
                props.Add(new PropPlacement { role = role, variant = variant, position = pos, yaw = yaw, scale = scale, settlementId = -1 });
            }
        }

        // ------------------------------------------------------------------ rule scatter

        /// <summary>Samples every rule of <paramref name="rules"/> chunk by chunk (trees become terrain instances).</summary>
        public static void Scatter(WorldGenParams p, float[,] map, RoadDistanceField roads, LandCover cover, IReadOnlyList<SettlementSite> sites,
            IReadOnlyList<FieldPatch> fields, OccupancyMap occupancy, PaletteKeys keys, IReadOnlyList<ScatterRule> rules,
            List<TreePlacement> trees, List<PropPlacement> props)
        {
            var clumpNoise = new GradientNoise(p.seed + 15);
            float corridor = p.ScatterCorridor;
            int chunks = Mathf.CeilToInt(p.worldSize / ChunkSize);
            RockPlacer.Context rocks = RockContext(p, map, roads, cover, sites, fields, keys);
            TileWindow window = p.window;
            float ox = p.NoiseOriginX;
            float oz = p.NoiseOriginZ;

            // Chunks: the finite world's 100 m grid from its corner; in a window of an endless world, the absolute chunk grid
            // over the window (chunks sticking out are sampled inside the window only), each chunk seeded by its absolute index.
            long chunkX0 = 0, chunkZ0 = 0, chunkX1 = chunks - 1, chunkZ1 = chunks - 1;
            if (window != null)
            {
                chunkX0 = window.ChunkIndex(window.originX);
                chunkZ0 = window.ChunkIndex(window.originZ);
                chunkX1 = window.ChunkIndex(window.originX + p.worldSize);
                chunkZ1 = window.ChunkIndex(window.originZ + p.worldSize);
            }

            for (int r = 0; r < rules.Count; r++)
            {
                ScatterRule rule = rules[r];
                if (!keys.Has(rule.family) || rule.density <= 0f) continue;
                bool tree = PropRole.IsTree(rule.family);
                // Rock-like families with measured prefabs are seated by RockPlacer (sized in metres, embedded, cliffs backed).
                bool rock = !tree && PropRole.IsRock(rule.family) && keys.HasMetrics(rule.family);
                RockSpec spec = rock ? RockPlacer.SpecFor(rule.family) : null;
                float sizeMin = rock ? (rule.heightMin > 0f ? rule.heightMin : spec.heightMin) : 0f;
                float sizeMax = rock ? (rule.heightMax > 0f ? rule.heightMax : spec.heightMax) : 0f;
                float clumpOffset = 137.1f * (r + 1);

                for (long cz = chunkZ0; cz <= chunkZ1; cz++)
                {
                    for (long cx = chunkX0; cx <= chunkX1; cx++)
                    {
                        System.Random rng;
                        float minX, minZ, width, depth;
                        bool keep = true;
                        if (window == null)
                        {
                            rng = new System.Random(ChunkSeed(p.seed + 5, (int)cx, (int)cz, r));
                            minX = cx * ChunkSize;
                            minZ = cz * ChunkSize;
                            width = Mathf.Min(ChunkSize, p.worldSize - minX);
                            depth = Mathf.Min(ChunkSize, p.worldSize - minZ);
                        }
                        else
                        {
                            rng = new System.Random(ChunkSeed(p.seed + 5, unchecked((int)cx), unchecked((int)cz), r));
                            minX = (float)(cx * (double)window.chunkSize - window.originX);
                            minZ = (float)(cz * (double)window.chunkSize - window.originZ);
                            width = depth = window.chunkSize;
                            keep = window.Keeps(cx, cz);
                        }

                        double expected = rule.density * width * depth / 10000.0;
                        int attempts = (int)expected + (rng.NextDouble() < expected - (int)expected ? 1 : 0);

                        for (int a = 0; a < attempts; a++)
                        {
                            float x = minX + (float)rng.NextDouble() * width;
                            float z = minZ + (float)rng.NextDouble() * depth;
                            double roll = rng.NextDouble();
                            float yaw = (float)(rng.NextDouble() * 360.0);
                            float scale = rule.scaleMin + (float)rng.NextDouble() * (rule.scaleMax - rule.scaleMin);
                            if (x < 3f || z < 3f || x > p.worldSize - 3f || z > p.worldSize - 3f) continue;

                            int i = cover.Index(x, z);
                            float affinity;
                            float exact = float.NaN;
                            float heightWeight = 1f;
                            if (rule.maxHeight < 1.5f)
                            {
                                exact = HeightmapGenerator.SampleHeight(map, p, x, z);
                                float hn = exact / Mathf.Max(1e-3f, p.hillAmplitude);
                                if (hn >= rule.maxHeight) continue;
                                heightWeight = 1f - LandCover.Smooth(rule.maxHeight - rule.heightFade, rule.maxHeight, hn);
                            }

                            if (rule.onWater)
                            {
                                if (!cover.IsWater(i) || cover.Depth(i) > rule.maxDepth) continue;
                                affinity = 1f;
                            }
                            else
                            {
                                if (cover.IsWater(i)) continue;
                                float slope = cover.Slope[i];
                                if (slope < rule.minSlope || slope > rule.maxSlope) continue;
                                affinity = cover.Weights(i).Dot(rule.biomes) * heightWeight;
                            }

                            if (rule.clump > 0f)
                            {
                                float n = 0.5f + 0.5f * clumpNoise.Fbm((x + ox) * 0.012f + clumpOffset, (z + oz) * 0.012f - clumpOffset, 2);
                                affinity *= Mathf.Lerp(1f, Mathf.Clamp01(LandCover.Smooth(0.42f, 0.68f, n) * 1.4f), rule.clump);
                            }

                            if (roll >= affinity) continue;
                            if (rule.nearSettlement > 0f && !NearSettlement(sites, x, z, rule.nearSettlement)) continue;
                            if (!roads.IsClear(x, z, corridor)) continue;
                            if (InsideSettlement(sites, x, z, SettlementMargin + rule.footprint)) continue;
                            if (InsideField(fields, x, z, 1.5f + rule.footprint)) continue;
                            if (occupancy.Overlaps(x, z, rule.footprint)) continue;
                            if (cover.WaterHeight > 0f)
                            {
                                // Exact (bilinear) height check: the texel lookup above can miss the shoreline by a texel.
                                if (float.IsNaN(exact)) exact = HeightmapGenerator.SampleHeight(map, p, x, z);
                                if (rule.onWater ? exact > cover.WaterHeight - 0.2f : exact < cover.WaterHeight + 0.3f) continue;
                            }

                            if (rock)
                            {
                                // A window may swap the family here (the endless road's pass ranges: their own rock look), sized by its spec.
                                string family = rule.family;
                                float lo = sizeMin, hi = sizeMax;
                                string swapped = window?.RockFamily?.Invoke(rule.family, x, z);
                                if (swapped != null && swapped != family && keys.Has(swapped) && keys.HasMetrics(swapped))
                                {
                                    family = swapped;
                                    RockSpec swappedSpec = RockPlacer.SpecFor(family);
                                    lo = swappedSpec.heightMin;
                                    hi = swappedSpec.heightMax;
                                }

                                float size = lo + (hi - lo) * Mathf.Pow((float)rng.NextDouble(), Mathf.Max(0.1f, rule.sizeBias));
                                float jitter = (float)(rng.NextDouble() - 0.5) * 24f;
                                int rockVariant = keys.PickVariant(family, rng);
                                if (!RockPlacer.TryPlace(rocks, family, rockVariant, x, z, size, yaw, jitter, out PropPlacement seated, out float radius)) continue;
                                float keepRadius = Mathf.Max(rule.footprint, 0.8f * radius);
                                if (keepRadius > rule.footprint && occupancy.Overlaps(x, z, keepRadius)) continue;
                                occupancy.Add(new Vector3(x, 0f, z), keepRadius);
                                if (keep) props.Add(seated);
                                continue;
                            }

                            occupancy.Add(new Vector3(x, 0f, z), rule.footprint);
                            if (!keep)
                            {
                                keys.PickVariant(rule.family, rng);   // same draws as a kept chunk, so the chunk's stream is window-independent
                                continue;
                            }

                            float y = rule.onWater ? cover.WaterHeight : GroundHeight(p, map, x, z, rule.footprint * (tree ? 0.3f : 0.5f));
                            if (tree)
                            {
                                trees.Add(new TreePlacement
                                {
                                    role = rule.family,
                                    variant = keys.PickVariant(rule.family, rng),
                                    position = new Vector3(x, y, z),
                                    yaw = yaw,
                                    scale = scale,
                                });
                            }
                            else
                            {
                                props.Add(new PropPlacement
                                {
                                    role = rule.family,
                                    variant = keys.PickVariant(rule.family, rng),
                                    position = new Vector3(x, y, z),
                                    yaw = yaw,
                                    scale = scale,
                                    settlementId = -1,
                                });
                            }
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ features

        /// <summary>
        /// Hand-composed countryside features: roadside camps / carts / stone piles on long legs, cairns on highland
        /// crests (and on the tallest peak), a rune-stone circle, and a water wheel on the shore nearest a village.
        /// </summary>
        public static void PlaceFeatures(WorldGenParams p, float[,] map, RoadNetwork network, RoadDistanceField roads, LandCover cover,
            IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields, IReadOnlyList<Landmark> landmarks,
            OccupancyMap occupancy, PaletteKeys keys, List<PropPlacement> props, PassRecord pass = null)
        {
            var rng = new System.Random(p.seed + 16);
            var ctx = new FeatureContext(p, map, roads, cover, sites, fields, occupancy, keys, props, rng);
            PlaceRoadside(ctx, network, pass);
            PlaceCairns(ctx, landmarks);
            PlaceSummitRocks(ctx);
            PlaceRunes(ctx);
            PlaceWaterWheel(ctx);
        }

        private static void PlaceRoadside(FeatureContext ctx, RoadNetwork network, PassRecord pass)
        {
            foreach (RoadSegment segment in network.Segments)
            {
                if (segment.Length < 350f) continue;
                if (pass != null && pass.SegmentIds.Contains(segment.Id)) continue;   // the climb has its own, sparser features
                for (float d = 150f; d < segment.Length - 150f; d += 230f + (float)ctx.Rng.NextDouble() * 80f)
                {
                    if (ctx.Rng.NextDouble() > 0.5) continue;
                    Vector3 on = segment.Evaluate(d);
                    Vector3 tangent = segment.Tangent(d);
                    tangent.y = 0f;
                    if (tangent.sqrMagnitude < 1e-4f) continue;
                    tangent.Normalize();
                    var side = new Vector3(tangent.z, 0f, -tangent.x);
                    if (ctx.Rng.Next(2) == 0) side = -side;
                    double kind = ctx.Rng.NextDouble();
                    PlaceRoadsideItem(ctx, on, side, tangent, kind);
                }
            }
        }

        /// <summary>
        /// One roadside feature beside the road point <paramref name="on"/>: a camp (<paramref name="kind"/> &lt; 0.4), a cart
        /// (&lt; 0.7) or a stone pile, on <paramref name="side"/> (unit, horizontal) just outside the scatter-free corridor;
        /// nothing when the spot is not clear. False when nothing was placed.
        /// </summary>
        internal static bool PlaceRoadsideItem(FeatureContext ctx, Vector3 on, Vector3 side, Vector3 tangent, double kind)
        {
            float offsetBase = ctx.Corridor + 5f;
            if (kind < 0.4)
            {
                // Camp: tent, fire with tripod, crate and small items, 10-14 m off the road.
                Vector3 centre = on + side * (offsetBase + 4f + (float)ctx.Rng.NextDouble() * 3f);
                if (!ctx.Clear(centre, 6.5f, 0.22f)) return false;
                PlaceCamp(ctx, centre, side, tangent);
            }
            else if (kind < 0.7)
            {
                Vector3 pos = on + side * (offsetBase + (float)ctx.Rng.NextDouble() * 2f);
                if (!ctx.Clear(pos, 2.5f, 0.25f)) return false;
                ctx.Occupancy.Add(pos, 2.5f);
                ctx.Add(PropRole.Cart, pos, Yaw(tangent) + (float)(ctx.Rng.NextDouble() * 50.0 - 25.0), 1f);
            }
            else
            {
                Vector3 pos = on + side * (offsetBase - 2f + (float)ctx.Rng.NextDouble() * 2f);
                if (!ctx.Clear(pos, 1.5f, 0.35f)) return false;
                ctx.Occupancy.Add(pos, 1.5f);
                ctx.Add(PropRole.StonePile, pos, (float)(ctx.Rng.NextDouble() * 360.0), 1f);
            }

            return true;
        }

        /// <summary>Camp around <paramref name="centre"/> (already checked clear): tent away from the road, fire with tripod, crate, small items.</summary>
        internal static void PlaceCamp(FeatureContext ctx, Vector3 centre, Vector3 side, Vector3 tangent)
        {
            ctx.Occupancy.Add(centre, 6.5f);
            Vector3 fire = centre - side * 2.2f + tangent * 1.5f;
            ctx.Add(PropRole.Tent, centre + side * 2.5f, Yaw(-side), 1f);
            ctx.Add(PropRole.Fireplace, fire, (float)(ctx.Rng.NextDouble() * 360.0), 1f);
            ctx.Add(PropRole.FireTripod, fire, (float)(ctx.Rng.NextDouble() * 360.0), 1f);
            ctx.Add(PropRole.Crate, centre + tangent * 3.5f, Yaw(tangent), 1f);
            ctx.Add(PropRole.CampItem, centre - tangent * 2.5f + side * 0.5f, (float)(ctx.Rng.NextDouble() * 360.0), 1f);
            ctx.Add(PropRole.CampItem, fire + tangent * 1.6f, (float)(ctx.Rng.NextDouble() * 360.0), 1f);
            if (ctx.Rng.NextDouble() < 0.5) ctx.Add(PropRole.TanningRack, centre - tangent * 4f + side * 2f, Yaw(side), 1f);
        }

        /// <summary>Scale that makes <paramref name="variant"/> of <paramref name="family"/> about <paramref name="height"/> m tall (<paramref name="fallback"/> without metrics).</summary>
        internal static float ScaleFor(FeatureContext ctx, string family, int variant, float height, float fallback) =>
            ctx.Keys.TryGetMetrics(family, variant, out PropMetrics m) ? Mathf.Clamp(height / m.Height, 0.2f, 2f) : fallback;

        /// <summary>
        /// A cairn (about 2.5 m) on the highest walkable point (slope under 0.6) within 30 m of the named peak, and smaller
        /// cairns (1.6-2.6 m) on some local maxima of the highland crests. All seated on their measured footprint.
        /// </summary>
        private static void PlaceCairns(FeatureContext ctx, IReadOnlyList<Landmark> landmarks)
        {
            foreach (Landmark landmark in landmarks)
            {
                if (landmark.kind != LandmarkKind.Peak) continue;
                Vector3 top = landmark.position;
                Vector3 best = Vector3.zero;
                float bestH = float.NegativeInfinity;
                for (float r = 0f; r <= 30f; r += 3f)
                {
                    int around = r <= 0f ? 1 : 12;
                    for (int k = 0; k < around; k++)
                    {
                        float a = k * Mathf.PI * 2f / around;
                        var pos = new Vector3(top.x + Mathf.Cos(a) * r, 0f, top.z + Mathf.Sin(a) * r);
                        if (!ctx.Clear(pos, 1.5f, 0.6f)) continue;
                        float h = HeightmapGenerator.SampleHeight(ctx.Map, ctx.P, pos.x, pos.z);
                        if (h <= bestH) continue;
                        bestH = h;
                        best = pos;
                    }
                }

                if (float.IsNegativeInfinity(bestH)) continue;   // no walkable ground on the summit: no cairn
                ctx.Occupancy.Add(best, 1.5f);
                int variant = ctx.Keys.PickVariant(PropRole.StoneStack, ctx.Rng);
                ctx.AddVariant(PropRole.StoneStack, variant, best, (float)(ctx.Rng.NextDouble() * 360.0), ScaleFor(ctx, PropRole.StoneStack, variant, 2.5f, 0.5f));
            }

            // Local maxima on a 60 m grid in the highlands.
            const float Cell = 60f;
            int cells = Mathf.FloorToInt(ctx.P.worldSize / Cell);
            for (int cz = 1; cz < cells - 1; cz++)
            {
                for (int cx = 1; cx < cells - 1; cx++)
                {
                    float bestH = float.NegativeInfinity;
                    Vector3 best = Vector3.zero;
                    for (int k = 0; k < 9; k++)
                    {
                        float x = (cx + 0.1f + 0.4f * (k % 3)) * Cell;
                        float z = (cz + 0.1f + 0.4f * (k / 3)) * Cell;
                        float h = ctx.Cover.Height[ctx.Cover.Index(x, z)];
                        if (h > bestH)
                        {
                            bestH = h;
                            best = new Vector3(x, 0f, z);
                        }
                    }

                    int i = ctx.Cover.Index(best.x, best.z);
                    if (ctx.Cover.Highland[i] < 0.5f) continue;
                    bool peak = true;
                    for (int k = 0; k < 8 && peak; k++)
                    {
                        float a = k * Mathf.PI * 0.25f;
                        int j = ctx.Cover.Index(best.x + Mathf.Cos(a) * 45f, best.z + Mathf.Sin(a) * 45f);
                        if (ctx.Cover.Height[j] > bestH - 2f) peak = false;
                    }

                    if (!peak || ctx.Rng.NextDouble() > 0.45) continue;
                    if (!ctx.Clear(best, 1.5f, 0.6f)) continue;
                    ctx.Occupancy.Add(best, 1.5f);
                    string family = ctx.Rng.NextDouble() < 0.6 ? PropRole.StoneStack : PropRole.StonePile;
                    int variant = ctx.Keys.PickVariant(family, ctx.Rng);
                    float height = family == PropRole.StoneStack ? 1.6f + (float)ctx.Rng.NextDouble() : 0.9f + (float)ctx.Rng.NextDouble() * 0.6f;
                    ctx.AddVariant(family, variant, best, (float)(ctx.Rng.NextDouble() * 360.0), ScaleFor(ctx, family, variant, height, 0.9f + (float)ctx.Rng.NextDouble() * 0.4f));
                }
            }
        }

        /// <summary>
        /// Summit dressing: a few small Meadow boulders and rock piles (0.6-2 m, embedded) just below each summit of the range,
        /// and sparse pairs along the aretes (convex crests above 65 % of the relief). Nothing on the summit point itself (the
        /// cairn has it); everything keeps off roads and other props.
        /// </summary>
        private static void PlaceSummitRocks(FeatureContext ctx)
        {
            if (!ctx.Keys.Has(PropRole.Rock)) return;
            WorldGenParams p = ctx.P;
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);
            int placed = 0;

            // Summits: the highest point of a 30 m cell that nothing within 70 m exceeds.
            const float Cell = 30f;
            int cells = Mathf.FloorToInt(p.worldSize / Cell);
            var summits = new List<Vector3>();
            for (int cz = 1; cz < cells - 1; cz++)
            {
                for (int cx = 1; cx < cells - 1; cx++)
                {
                    float bestH = float.NegativeInfinity;
                    Vector3 best = Vector3.zero;
                    for (int k = 0; k < 16; k++)
                    {
                        float x = (cx + 0.125f + 0.25f * (k % 4)) * Cell;
                        float z = (cz + 0.125f + 0.25f * (k / 4)) * Cell;
                        float h = HeightmapGenerator.SampleHeight(ctx.Map, p, x, z);
                        if (h <= bestH) continue;
                        bestH = h;
                        best = new Vector3(x, h, z);
                    }

                    if (bestH < 0.72f * amplitude || ctx.Cover.Highland[ctx.Cover.Index(best.x, best.z)] < 0.5f) continue;
                    bool top = true;
                    for (int k = 0; k < 16 && top; k++)
                    {
                        float a = k * Mathf.PI / 8f;
                        float r = k % 2 == 0 ? 35f : 70f;
                        if (HeightmapGenerator.SampleHeight(ctx.Map, p, best.x + Mathf.Cos(a) * r, best.z + Mathf.Sin(a) * r) > bestH - 3f) top = false;
                    }

                    if (top) summits.Add(best);
                }
            }

            foreach (Vector3 summit in summits)
            {
                int want = 3 + ctx.Rng.Next(3);
                for (int attempt = 0, got = 0; attempt < 24 && got < want; attempt++)
                {
                    float a = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
                    float r = 6f + (float)ctx.Rng.NextDouble() * 18f;
                    float x = summit.x + Mathf.Cos(a) * r, z = summit.z + Mathf.Sin(a) * r;
                    bool pile = ctx.Rng.NextDouble() < 0.3;
                    float height = pile ? 1.2f + (float)ctx.Rng.NextDouble() * 1f : 0.6f + (float)ctx.Rng.NextDouble() * 1.3f;
                    if (ctx.Occupancy.Overlaps(x, z, 1.5f)) continue;
                    if (!ctx.AddRock(pile ? PropRole.RockPile : PropRole.Rock, x, z, height, out float radius)) continue;
                    ctx.Occupancy.Add(new Vector3(x, 0f, z), Mathf.Max(1.2f, 0.8f * radius));
                    got++;
                    placed++;
                }
            }

            // Aretes: convex crest points on a 45 m grid above 65 % of the relief, some of them get a pair of boulders.
            const float Crest = 45f;
            int crestCells = Mathf.FloorToInt(p.worldSize / Crest);
            var rocks = ctx.Seating;
            for (int cz = 1; cz < crestCells - 1; cz++)
            {
                for (int cx = 1; cx < crestCells - 1; cx++)
                {
                    float bestH = float.NegativeInfinity;
                    Vector3 best = Vector3.zero;
                    for (int k = 0; k < 9; k++)
                    {
                        float x = (cx + 0.17f + 0.33f * (k % 3)) * Crest;
                        float z = (cz + 0.17f + 0.33f * (k / 3)) * Crest;
                        float h = HeightmapGenerator.SampleHeight(ctx.Map, p, x, z);
                        if (h <= bestH) continue;
                        bestH = h;
                        best = new Vector3(x, h, z);
                    }

                    if (bestH < 0.65f * amplitude || ctx.Cover.Highland[ctx.Cover.Index(best.x, best.z)] < 0.5f) continue;
                    if (!RockPlacer.IsConvex(rocks, best.x, best.z, 10f) || ctx.Rng.NextDouble() > 0.3) continue;
                    int pair = 1 + ctx.Rng.Next(2);
                    for (int attempt = 0, got = 0; attempt < 8 && got < pair; attempt++)
                    {
                        float a = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
                        float r = 2f + (float)ctx.Rng.NextDouble() * 8f;
                        float x = best.x + Mathf.Cos(a) * r, z = best.z + Mathf.Sin(a) * r;
                        if (ctx.Occupancy.Overlaps(x, z, 1.5f)) continue;
                        if (!ctx.AddRock(PropRole.Rock, x, z, 0.7f + (float)ctx.Rng.NextDouble() * 1.2f, out float radius)) continue;
                        ctx.Occupancy.Add(new Vector3(x, 0f, z), Mathf.Max(1.2f, 0.8f * radius));
                        got++;
                        placed++;
                    }
                }
            }
        }

        private static void PlaceRunes(FeatureContext ctx)
        {
            if (!ctx.Keys.Has(PropRole.StoneRunes)) return;
            int circles = 1 + (ctx.Rng.NextDouble() < 0.5 ? 1 : 0);
            for (int c = 0, attempt = 0; c < circles && attempt < 400; attempt++)
            {
                var centre = new Vector3(150f + (float)ctx.Rng.NextDouble() * (ctx.P.worldSize - 300f), 0f, 150f + (float)ctx.Rng.NextDouble() * (ctx.P.worldSize - 300f));
                int i = ctx.Cover.Index(centre.x, centre.z);
                BiomeWeights w = ctx.Cover.Weights(i);
                if (w.forest > 0.3f || w.farm > 0.1f) continue;
                if (!ctx.Clear(centre, 7f, 0.25f)) continue;
                if (!ctx.Roads.IsClear(centre.x, centre.z, 40f)) continue;
                ctx.Occupancy.Add(centre, 7f);
                int stones = 3 + ctx.Rng.Next(3);
                float start = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
                for (int s = 0; s < stones; s++)
                {
                    float a = start + s * Mathf.PI * 2f / stones;
                    Vector3 pos = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4.5f;
                    ctx.Add(PropRole.StoneRunes, pos, Yaw(centre - pos), 1f);
                }

                c++;
            }
        }

        private static void PlaceWaterWheel(FeatureContext ctx)
        {
            if (!ctx.Keys.Has(PropRole.WaterWheel) || ctx.Cover.WaterHeight <= 0f) return;
            foreach (SettlementSite site in ctx.Sites)
            {
                if (site.Type == SettlementType.Inn) continue;
                for (float r = site.Radius + 20f; r < site.Radius + 220f; r += 10f)
                {
                    for (int k = 0; k < 36; k++)
                    {
                        float a = k * Mathf.PI / 18f;
                        var pos = new Vector3(site.Position.x + Mathf.Cos(a) * r, 0f, site.Position.z + Mathf.Sin(a) * r);
                        int i = ctx.Cover.Index(pos.x, pos.z);
                        if (ctx.Cover.IsWater(i) || ctx.Cover.Shore[i] < 0.6f) continue;

                        // Face the water: find the wettest direction.
                        Vector3 toWater = Vector3.zero;
                        for (int d = 0; d < 8; d++)
                        {
                            float b = d * Mathf.PI * 0.25f;
                            var dir = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                            if (ctx.Cover.IsWater(ctx.Cover.Index(pos.x + dir.x * 5f, pos.z + dir.z * 5f))) toWater += dir;
                        }

                        if (toWater.sqrMagnitude < 0.5f) continue;
                        if (!ctx.Roads.IsClear(pos.x, pos.z, ctx.Corridor + 3f) || ctx.Occupancy.Overlaps(pos.x, pos.z, 3f)) continue;
                        ctx.Occupancy.Add(pos, 3f);
                        ctx.Add(PropRole.WaterWheel, pos + toWater.normalized * 1.5f, Yaw(Vector3.Cross(toWater, Vector3.up)), 1f);
                        return;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the pass road

        /// <summary>
        /// Things to see on the climb, placed before the general features so they get their spots: the pass itself as a named
        /// landmark with a big cairn beside the col; cairns every ~100 m along the upper road; a viewpoint clearing with a
        /// stone arch and standing stones where the view over the lowland and its lakes is widest (a named landmark); rock
        /// faces above the deepest road cuts; a camp about two-thirds of the way up; a broken wagon on the lowest hairpin; a
        /// sign where a pass road leaves a settlement. Everything stays outside the scatter-free road corridor. Appends the
        /// pass and viewpoint landmarks to <paramref name="landmarks"/>. Stage seed + 19.
        /// </summary>
        public static void PlacePassFeatures(WorldGenParams p, float[,] map, RoadNetwork network, RoadDistanceField roads, LandCover cover,
            IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields, OccupancyMap occupancy, PaletteKeys keys,
            List<PropPlacement> props, PassRecord pass, SettlementNames names, List<Landmark> landmarks)
        {
            if (pass == null || pass.InnSite < 0 || pass.SegmentIds.Count == 0) return;
            var rng = new System.Random(p.seed + 19);
            var ctx = new FeatureContext(p, map, roads, cover, sites, fields, occupancy, keys, props, rng);
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);
            Settlement inn = network.Settlements[pass.InnSite];

            // Legs from the inn down to the lowland, as point lists with arc lengths.
            var legs = new List<List<Vector3>>();
            foreach (int id in pass.SegmentIds)
            {
                RoadSegment segment = network.Segments[id];
                var points = new List<Vector3>(segment.Points);
                if (segment.B == inn.Node) points.Reverse();
                legs.Add(points);
            }

            // The pass: named after the tallest summit when it stands beside the col.
            string peakName = null;
            foreach (Landmark landmark in landmarks)
            {
                if (landmark.kind == LandmarkKind.Peak && RoadPlanner.PlanarDistance(landmark.position, pass.Col) < 700f) peakName = landmark.name;
            }

            // The landmark sits where the road crosses the col (at the col itself when the road only climbs to the inn).
            Vector3 col = pass.Col;
            float nearest = float.PositiveInfinity;
            foreach (List<Vector3> leg in legs)
            {
                foreach (Vector3 point in leg)
                {
                    float d = RoadPlanner.PlanarDistance(point, pass.Col);
                    if (d >= nearest) continue;
                    nearest = d;
                    col = point;
                }
            }

            if (!pass.TwoSided || nearest > 30f) col = pass.Col;

            col.y = HeightmapGenerator.SampleHeight(map, p, col.x, col.z);
            landmarks.Add(new Landmark { name = names.NextPass(peakName), kind = LandmarkKind.Pass, position = col });
            PlaceCairnNear(ctx, col, 3.5f);

            foreach (List<Vector3> leg in legs) PlaceUpperCairns(ctx, leg, amplitude);
            PlaceViewpoint(ctx, legs, amplitude, names, landmarks, inn.Position, col);
            foreach (List<Vector3> leg in legs) PlaceCutFaces(ctx, leg);
            PlacePassCamp(ctx, legs);
            PlaceBrokenWagon(ctx, legs);

            // A plain sign where a pass road leaves a settlement (junctions get an interactive signpost anyway).
            foreach (int id in pass.SegmentIds)
            {
                RoadSegment segment = network.Segments[id];
                RoadNode bottom = segment.A == inn.Node ? segment.B : segment.A;
                if (bottom.Settlement == null) continue;
                float d = Mathf.Min(segment.Length * 0.5f, bottom.Settlement.Radius + 12f);
                float at = segment.B == bottom ? segment.Length - d : d;
                Vector3 on = segment.Evaluate(at);
                Vector3 tangent = Flat(segment.Tangent(at));
                var side = new Vector3(tangent.z, 0f, -tangent.x);
                foreach (float s in new[] { 1f, -1f })
                {
                    Vector3 pos = on + side * s * (ctx.Corridor + 2.5f);
                    if (!ctx.Clear(pos, 0.8f, 0.5f)) continue;
                    ctx.Occupancy.Add(pos, 0.8f);
                    ctx.Add(PropRole.Sign, pos, Yaw(-side * s), 1f);
                    break;
                }
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// <summary>Arc length at every point of <paramref name="points"/>.</summary>
        private static float[] Along(List<Vector3> points)
        {
            var along = new float[points.Count];
            for (int i = 1; i < points.Count; i++) along[i] = along[i - 1] + RoadPlanner.PlanarDistance(points[i - 1], points[i]);
            return along;
        }

        private static int IndexAt(float[] along, float distance)
        {
            int i = System.Array.BinarySearch(along, distance);
            return Mathf.Clamp(i >= 0 ? i : ~i, 0, along.Length - 1);
        }

        private static Vector3 TangentAt(List<Vector3> points, int i)
        {
            int a = Mathf.Max(0, i - 2);
            int b = Mathf.Min(points.Count - 1, i + 2);
            return Flat(points[b] - points[a]);
        }

        /// <summary>The big cairn at the pass: a stone stack about <paramref name="height"/> m tall, seated beside the road.</summary>
        internal static void PlaceCairnNear(FeatureContext ctx, Vector3 centre, float height)
        {
            for (float r = ctx.Corridor + 2f; r < ctx.Corridor + 30f; r += 4f)
            {
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    var pos = new Vector3(centre.x + Mathf.Cos(a) * r, 0f, centre.z + Mathf.Sin(a) * r);
                    if (!ctx.Clear(pos, 1.5f, 0.7f)) continue;
                    ctx.Occupancy.Add(pos, 1.5f);
                    int variant = ctx.Keys.PickVariant(PropRole.StoneStack, ctx.Rng);
                    ctx.AddVariant(PropRole.StoneStack, variant, pos, (float)(ctx.Rng.NextDouble() * 360.0), ScaleFor(ctx, PropRole.StoneStack, variant, height, 1.5f));
                    return;
                }
            }
        }

        /// <summary>Cairns every ~100 m, alternating sides, while the road is above 58 % of the relief.</summary>
        private static void PlaceUpperCairns(FeatureContext ctx, List<Vector3> leg, float amplitude)
        {
            float[] along = Along(leg);
            float total = along[along.Length - 1];
            int placed = 0;
            float side = ctx.Rng.Next(2) == 0 ? 1f : -1f;
            for (float d = 70f; d < total - 40f && placed < 5; d += 95f + (float)ctx.Rng.NextDouble() * 30f)
            {
                int i = IndexAt(along, d);
                Vector3 on = leg[i];
                if (HeightmapGenerator.SampleHeight(ctx.Map, ctx.P, on.x, on.z) < 0.58f * amplitude) break;
                Vector3 tangent = TangentAt(leg, i);
                var normal = new Vector3(tangent.z, 0f, -tangent.x);
                for (int attempt = 0; attempt < 2; attempt++, side = -side)
                {
                    Vector3 pos = on + normal * side * (ctx.Corridor + 3f + (float)ctx.Rng.NextDouble() * 1.5f);
                    if (!ctx.Clear(pos, 1.2f, 1f)) continue;
                    ctx.Occupancy.Add(pos, 1.2f);
                    string family = placed % 2 == 0 ? PropRole.StoneStack : PropRole.StonePile;
                    int variant = ctx.Keys.PickVariant(family, ctx.Rng);
                    float roll = (float)ctx.Rng.NextDouble();
                    float height = family == PropRole.StoneStack ? 2.2f + 0.6f * roll : 1.1f + 0.4f * roll;
                    ctx.AddVariant(family, variant, pos, (float)(ctx.Rng.NextDouble() * 360.0), ScaleFor(ctx, family, variant, height, 0.9f + 0.3f * roll));
                    placed++;
                    break;
                }

                side = -side;
            }
        }

        /// <summary>
        /// The viewpoint: of the clear, gentle spots 18-34 m off the pass road (above 45 % of the relief, at least 150 m from the
        /// inn and 120 m from the pass), the one that sees the most
        /// lowland (lakes count triple) over the terrain in between. A stone arch frames the view, three standing stones
        /// stand behind, and the clearing is kept free of trees.
        /// </summary>
        private static void PlaceViewpoint(FeatureContext ctx, List<List<Vector3>> legs, float amplitude, SettlementNames names, List<Landmark> landmarks,
            Vector3 inn, Vector3 pass)
        {
            WorldGenParams p = ctx.P;
            var targets = new List<(Vector3 pos, float weight)>();
            const float TargetStep = 70f;
            for (float z = TargetStep * 0.5f; z < p.worldSize; z += TargetStep)
            {
                for (float x = TargetStep * 0.5f; x < p.worldSize; x += TargetStep)
                {
                    int t = ctx.Cover.Index(x, z);
                    if (ctx.Cover.IsWater(t)) targets.Add((new Vector3(x, ctx.Cover.WaterHeight, z), 3f));
                    else if (ctx.Cover.Height[t] < 0.22f * amplitude) targets.Add((new Vector3(x, ctx.Cover.Height[t] + 2f, z), 1f));
                }
            }

            if (targets.Count == 0) return;
            float bestScore = 0.12f;
            Vector3 best = Vector3.zero;
            Vector3 bestView = Vector3.zero;
            foreach (List<Vector3> leg in legs)
            {
                float[] along = Along(leg);
                for (float d = 40f; d < along[along.Length - 1] - 40f; d += 40f)
                {
                    int i = IndexAt(along, d);
                    Vector3 on = leg[i];
                    float hn = HeightmapGenerator.SampleHeight(ctx.Map, p, on.x, on.z) / amplitude;
                    if (hn < 0.45f || hn > 0.85f) continue;
                    if (RoadPlanner.PlanarDistance(on, inn) < 150f || RoadPlanner.PlanarDistance(on, pass) < 120f) continue;   // a stop on the way, not at the top
                    Vector3 tangent = TangentAt(leg, i);
                    var normal = new Vector3(tangent.z, 0f, -tangent.x);
                    foreach (float side in new[] { 1f, -1f })
                    {
                        foreach (float offset in new[] { 18f, 26f, 34f })
                        {
                            Vector3 c = on + normal * side * Mathf.Max(offset, ctx.Corridor + 7f);
                            if (!ctx.Clear(c, 6f, 0.3f)) continue;
                            float score = ViewScore(ctx, c, targets, out Vector3 view);
                            if (score <= bestScore) continue;
                            bestScore = score;
                            best = c;
                            bestView = view;
                        }
                    }
                }
            }

            if (bestScore <= 0.12f) return;
            DressViewpoint(ctx, best, bestView);
            Vector3 mark = best;
            mark.y = HeightmapGenerator.SampleHeight(ctx.Map, p, best.x, best.z);
            landmarks.Add(new Landmark { name = names.NextViewpoint(), kind = LandmarkKind.Viewpoint, position = mark });
        }

        /// <summary>
        /// A viewpoint clearing at <paramref name="centre"/> (already checked clear) looking along <paramref name="view"/>: a stone
        /// arch frames the view, three standing stones stand behind, and 12 m around is kept free of trees and rocks.
        /// </summary>
        internal static void DressViewpoint(FeatureContext ctx, Vector3 centre, Vector3 view)
        {
            ctx.Occupancy.Add(centre, 12f);   // the clearing: no trees or rocks
            Vector3 view3 = Flat(view);
            var across = new Vector3(view3.z, 0f, -view3.x);
            ctx.Add(PropRole.StoneArch, centre + view3 * 4.5f, Yaw(view3), 1f);
            ctx.Add(PropRole.StoneRunes, centre - view3 * 3.5f + across * 3.5f, Yaw(view3 + across * 0.3f), 1f);
            ctx.Add(PropRole.StoneRunes, centre - view3 * 4.5f, Yaw(view3), 1f);
            ctx.Add(PropRole.StoneRunes, centre - view3 * 3.5f - across * 3.5f, Yaw(view3 - across * 0.3f), 1f);
        }

        /// <summary>Weighted share of <paramref name="targets"/> within 1.8 km visible from eye height above <paramref name="c"/>.</summary>
        private static float ViewScore(FeatureContext ctx, Vector3 c, List<(Vector3 pos, float weight)> targets, out Vector3 view)
        {
            WorldGenParams p = ctx.P;
            float eye = HeightmapGenerator.SampleHeight(ctx.Map, p, c.x, c.z) + 1.7f;
            float seen = 0f;
            float total = 0f;
            view = Vector3.zero;
            foreach (var (pos, weight) in targets)
            {
                float dx = pos.x - c.x;
                float dz = pos.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > 1800f || dist < 60f) continue;
                total += weight;
                bool visible = true;
                int steps = Mathf.CeilToInt(dist / 12f);
                for (int s = 1; s < steps && visible; s++)
                {
                    float t = s / (float)steps;
                    float line = eye + (pos.y - eye) * t;
                    if (HeightmapGenerator.SampleHeight(ctx.Map, p, c.x + dx * t, c.z + dz * t) > line + 0.5f) visible = false;
                }

                if (!visible) continue;
                seen += weight;
                view += new Vector3(dx, 0f, dz) / dist * weight;
            }

            return total > 0f ? seen / total : 0f;
        }

        /// <summary>
        /// Rock faces on the steep ground above the road's cuttings (up to six per leg, 40 m apart): Meadow cliff blocks 3-6 m
        /// tall, seated by <see cref="RockPlacer"/> so their whole footprint stays outside the scatter-free corridor (nothing
        /// overhangs the road), they are sunk into the bank and higher ground backs them.
        /// </summary>
        internal static void PlaceCutFaces(FeatureContext ctx, List<Vector3> leg)
        {
            float[] along = Along(leg);
            int placed = 0;
            float last = -100f;
            for (float d = 30f; d < along[along.Length - 1] - 30f && placed < 6; d += 12f)
            {
                if (d - last < 40f) continue;
                int i = IndexAt(along, d);
                Vector3 on = leg[i];
                float road = HeightmapGenerator.SampleHeight(ctx.Map, ctx.P, on.x, on.z);
                Vector3 tangent = TangentAt(leg, i);
                var normal = new Vector3(tangent.z, 0f, -tangent.x);
                if (TryCutFace(ctx, on, normal, road))
                {
                    placed++;
                    last = d;
                }
            }
        }

        /// <summary>
        /// One rock face (Meadow cliff block 3-6 m, seated by <see cref="RockPlacer"/>) on the steep uphill side of a road cutting
        /// beside road point <paramref name="on"/> (road height <paramref name="road"/>), 4.5-10 m beyond the corridor on either
        /// side (horizontal unit <paramref name="normal"/>). False when neither side has a cutting there.
        /// </summary>
        internal static bool TryCutFace(FeatureContext ctx, Vector3 on, Vector3 normal, float road)
        {
            foreach (float side in new[] { 1f, -1f })
            {
                foreach (float offset in new[] { 4.5f, 7f, 10f })
                {
                    Vector3 pos = on + normal * side * (ctx.Corridor + offset);
                    float h = HeightmapGenerator.SampleHeight(ctx.Map, ctx.P, pos.x, pos.z);
                    float slope = HeightmapGenerator.SampleSlope(ctx.Map, ctx.P.worldSize, ctx.P.hillAmplitude, pos.x, pos.z);
                    if (h < road + 4f || slope < 0.75f) continue;   // uphill side, steep: above a cutting
                    if (ctx.Occupancy.Overlaps(pos.x, pos.z, 2.5f)) continue;
                    float height = 3f + (float)ctx.Rng.NextDouble() * 3f;
                    if (!ctx.AddRock(PropRole.Cliff, pos.x, pos.z, height, out float radius)) continue;
                    ctx.Occupancy.Add(pos, Mathf.Max(2.5f, 0.8f * radius));
                    return true;
                }
            }

            return false;
        }

        /// <summary>One camp beside the longest leg, about two-thirds of the way up.</summary>
        private static void PlacePassCamp(FeatureContext ctx, List<List<Vector3>> legs)
        {
            List<Vector3> longest = null;
            float longestLength = 0f;
            foreach (List<Vector3> leg in legs)
            {
                float[] a = Along(leg);
                if (a[a.Length - 1] > longestLength)
                {
                    longestLength = a[a.Length - 1];
                    longest = leg;
                }
            }

            if (longest == null) return;
            float top = HeightmapGenerator.SampleHeight(ctx.Map, ctx.P, longest[0].x, longest[0].z);
            Vector3 end = longest[longest.Count - 1];
            float bottom = HeightmapGenerator.SampleHeight(ctx.Map, ctx.P, end.x, end.z);
            float[] along = Along(longest);
            var order = new List<(float miss, int index)>();
            for (int i = 0; i < longest.Count; i += 4)
            {
                float h = HeightmapGenerator.SampleHeight(ctx.Map, ctx.P, longest[i].x, longest[i].z);
                float frac = top > bottom + 1f ? (h - bottom) / (top - bottom) : 0.67f;
                if (Mathf.Abs(frac - 0.67f) < 0.25f && along[i] > 60f) order.Add((Mathf.Abs(frac - 0.67f), i));
            }

            order.Sort((a, b) => a.miss != b.miss ? a.miss.CompareTo(b.miss) : a.index.CompareTo(b.index));
            foreach (var (_, i) in order)
            {
                Vector3 tangent = TangentAt(longest, i);
                var normal = new Vector3(tangent.z, 0f, -tangent.x);
                foreach (float side in new[] { 1f, -1f })
                {
                    Vector3 s = normal * side;
                    foreach (float offset in new[] { 8.5f, 11f })
                    {
                        Vector3 centre = longest[i] + s * (ctx.Corridor + offset);
                        if (!ctx.Clear(centre, 6.5f, 0.3f)) continue;
                        PlaceCamp(ctx, centre, s, tangent);
                        return;
                    }
                }
            }
        }

        /// <summary>A broken wagon beside the lowest hairpin (heading turns 120° within 45 m), else low on the longest leg.</summary>
        private static void PlaceBrokenWagon(FeatureContext ctx, List<List<Vector3>> legs)
        {
            var spots = new List<(float height, Vector3 pos, Vector3 tangent)>();
            foreach (List<Vector3> leg in legs)
            {
                float[] along = Along(leg);
                int k = 0;
                for (int i = 1; i < leg.Count - 1; i++)
                {
                    while (k < leg.Count - 2 && along[k + 1] - along[i] < 45f) k++;
                    if (k <= i + 1) continue;
                    Vector3 t0 = Flat(leg[i] - leg[i - 1]);
                    Vector3 t1 = Flat(leg[k + 1] - leg[k]);
                    if (Vector3.Angle(t0, t1) < 120f) continue;
                    int apex = (i + k) / 2;
                    spots.Add((HeightmapGenerator.SampleHeight(ctx.Map, ctx.P, leg[apex].x, leg[apex].z), leg[apex], TangentAt(leg, apex)));
                    i = k;
                }

                if (spots.Count == 0 && leg.Count > 8)
                {
                    int low = IndexAt(along, along[along.Length - 1] * 0.75f);
                    spots.Add((float.MaxValue, leg[low], TangentAt(leg, low)));
                }
            }

            spots.Sort((a, b) => a.height.CompareTo(b.height));
            foreach (var (_, pos, tangent) in spots)
            {
                var normal = new Vector3(tangent.z, 0f, -tangent.x);
                foreach (float side in new[] { 1f, -1f })
                {
                    Vector3 at = pos + normal * side * (ctx.Corridor + 4.5f);
                    if (!ctx.Clear(at, 2.5f, 0.45f)) continue;
                    ctx.Occupancy.Add(at, 2.5f);
                    if (ctx.AddVariant(PropRole.Cart, BrokenWagonVariant, at, Yaw(tangent) + 20f, 1f)) return;
                }
            }
        }

        /// <summary>Cart-family variant of the broken wagon (SyntyCatalog order: hand cart, broken wagon).</summary>
        private const int BrokenWagonVariant = 1;

        internal sealed class FeatureContext
        {
            public FeatureContext(WorldGenParams p, float[,] map, RoadDistanceField roads, LandCover cover, IReadOnlyList<SettlementSite> sites,
                IReadOnlyList<FieldPatch> fields, OccupancyMap occupancy, PaletteKeys keys, List<PropPlacement> props, System.Random rng)
            {
                P = p;
                Map = map;
                Roads = roads;
                Cover = cover;
                Sites = sites;
                Fields = fields;
                Occupancy = occupancy;
                Keys = keys;
                Props = props;
                Rng = rng;
                Corridor = p.ScatterCorridor;
                Rocks = RockContext(p, map, roads, cover, sites, fields, keys);
                Seating = new RockPlacer.Context(p, map, keys, null);
            }

            /// <summary>Rock placement with footprint clearance (roads, settlements, fields, water).</summary>
            public RockPlacer.Context Rocks { get; }

            /// <summary>Rock seating without clearance (the caller has checked the spot).</summary>
            public RockPlacer.Context Seating { get; }

            /// <summary>
            /// Places a rock-like piece of <paramref name="family"/> about <paramref name="height"/> m tall centred on (x, z), embedded
            /// and (for cliffs) backed; false when the spot does not suit it.
            /// </summary>
            public bool AddRock(string family, float x, float z, float height, out float radius)
            {
                radius = 0f;
                if (!Keys.Has(family)) return false;
                int variant = Keys.PickVariant(family, Rng);
                float yaw = (float)(Rng.NextDouble() * 360.0);
                float jitter = (float)(Rng.NextDouble() - 0.5) * 20f;
                if (!RockPlacer.TryPlace(Rocks, family, variant, x, z, height, yaw, jitter, out PropPlacement placement, out radius)) return false;
                Props.Add(placement);
                return true;
            }

            public WorldGenParams P { get; }
            public float[,] Map { get; }
            public RoadDistanceField Roads { get; }
            public LandCover Cover { get; }
            public IReadOnlyList<SettlementSite> Sites { get; }
            public IReadOnlyList<FieldPatch> Fields { get; }
            public OccupancyMap Occupancy { get; }
            public PaletteKeys Keys { get; }
            public List<PropPlacement> Props { get; }
            public System.Random Rng { get; }
            public float Corridor { get; }

            /// <summary>Dry, gentle, off-road, outside settlements/fields and unoccupied within <paramref name="radius"/>.</summary>
            public bool Clear(Vector3 pos, float radius, float maxSlope)
            {
                if (pos.x < radius + 5f || pos.z < radius + 5f || pos.x > P.worldSize - radius - 5f || pos.z > P.worldSize - radius - 5f) return false;
                int i = Cover.Index(pos.x, pos.z);
                if (Cover.IsWater(i) || Cover.Slope[i] > maxSlope) return false;
                if (!Roads.IsClear(pos.x, pos.z, Corridor + radius)) return false;
                if (InsideSettlement(Sites, pos.x, pos.z, SettlementMargin + radius)) return false;
                if (InsideField(Fields, pos.x, pos.z, 2f + radius)) return false;
                return !Occupancy.Overlaps(pos.x, pos.z, radius);
            }

            public void Add(string family, Vector3 pos, float yaw, float scale) => AddVariant(family, Keys.PickVariant(family, Rng), pos, yaw, scale);

            /// <summary>Adds a specific variant (e.g. the broken wagon of the cart family); off-road only, like <see cref="Add"/>.</summary>
            public bool AddVariant(string family, int variant, Vector3 pos, float yaw, float scale)
            {
                if (!Keys.Has(family)) return false;
                if (!Roads.IsClear(pos.x, pos.z, Corridor)) return false;
                variant = Mathf.Min(variant, Keys.Count(family) - 1);
                if (PropRole.IsRock(family) || family == PropRole.StoneStack || family == PropRole.StonePile)
                {
                    // Measured pieces (cairns): seated on the ground under their whole footprint, upright.
                    if (Keys.TryGetMetrics(family, variant, out _))
                    {
                        // Seated or not placed at all: never fall back to a pivot-on-the-ground piece that could float.
                        if (!RockPlacer.TrySeat(Seating, family, variant, pos.x, pos.z, yaw, scale, out PropPlacement seated)) return false;
                        Props.Add(seated);
                        return true;
                    }
                }

                pos.y = GroundHeight(P, Map, pos.x, pos.z, 0.5f);
                Props.Add(new PropPlacement { role = family, variant = variant, position = pos, yaw = yaw, scale = scale, settlementId = -1 });
                return true;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Rock placement context: a footprint point is clear when it is outside the road corridor, settlements (+ margin),
        /// fields and water.
        /// </summary>
        internal static RockPlacer.Context RockContext(WorldGenParams p, float[,] map, RoadDistanceField roads, LandCover cover,
            IReadOnlyList<SettlementSite> sites, IReadOnlyList<FieldPatch> fields, PaletteKeys keys)
        {
            float corridor = p.ScatterCorridor;
            float water = cover != null ? cover.WaterHeight : -1f;
            return new RockPlacer.Context(p, map, keys, (x, z) =>
            {
                if (roads != null && !roads.IsClear(x, z, corridor)) return false;
                if (InsideSettlement(sites, x, z, SettlementMargin)) return false;
                if (fields != null && InsideField(fields, x, z, 1.5f)) return false;
                return !(water > 0f && HeightmapGenerator.SampleHeight(map, p, x, z) < water + 0.3f);
            });
        }

        /// <summary>Terrain height at (x, z), sunk by up to slope * <paramref name="sink"/> so props on slopes do not float.</summary>
        internal static float GroundHeight(WorldGenParams p, float[,] map, float x, float z, float sink)
        {
            float h = HeightmapGenerator.SampleHeight(map, p, x, z);
            float slope = HeightmapGenerator.SampleSlope(map, p.worldSize, p.hillAmplitude, x, z);
            return h - Mathf.Min(1.5f, slope * sink);
        }

        private static bool NearSettlement(IReadOnlyList<SettlementSite> sites, float x, float z, float distance)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                if (sites[i].Type == SettlementType.Inn) continue;
                float dx = x - sites[i].Position.x;
                float dz = z - sites[i].Position.z;
                float r = sites[i].Radius + distance;
                if (dx * dx + dz * dz < r * r) return true;
            }

            return false;
        }

        internal static bool InsideSettlement(IReadOnlyList<SettlementSite> sites, float x, float z, float margin)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                float dx = x - sites[i].Position.x;
                float dz = z - sites[i].Position.z;
                float r = sites[i].Radius + margin;
                if (dx * dx + dz * dz < r * r) return true;
            }

            return false;
        }

        internal static bool InsideField(IReadOnlyList<FieldPatch> fields, float x, float z, float margin)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Contains(x, z, margin)) return true;
            }

            return false;
        }

        internal static void Axes(float yawDegrees, out Vector3 right, out Vector3 forward)
        {
            float rad = yawDegrees * Mathf.Deg2Rad;
            float s = Mathf.Sin(rad);
            float c = Mathf.Cos(rad);
            right = new Vector3(c, 0f, -s);
            forward = new Vector3(s, 0f, c);
        }

        /// <summary>Unity yaw (degrees about +Y) that turns +Z towards <paramref name="direction"/>.</summary>
        public static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        private static int ChunkSeed(int seed, int cx, int cz, int rule)
        {
            unchecked
            {
                int h = seed * 73856093;
                h ^= cx * 19349663;
                h ^= cz * 83492791;
                h ^= (rule + 1) * 49979687;
                h *= 16777619;
                return h & 0x7fffffff;
            }
        }
    }
}
