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

        private static void FillField(WorldGenParams p, float[,] map, PaletteKeys keys, System.Random rng, FieldPatch field,
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

            for (int r = 0; r < rules.Count; r++)
            {
                ScatterRule rule = rules[r];
                if (!keys.Has(rule.family) || rule.density <= 0f) continue;
                bool tree = PropRole.IsTree(rule.family);
                float clumpOffset = 137.1f * (r + 1);

                for (int cz = 0; cz < chunks; cz++)
                {
                    for (int cx = 0; cx < chunks; cx++)
                    {
                        var rng = new System.Random(ChunkSeed(p.seed + 5, cx, cz, r));
                        float minX = cx * ChunkSize;
                        float minZ = cz * ChunkSize;
                        float width = Mathf.Min(ChunkSize, p.worldSize - minX);
                        float depth = Mathf.Min(ChunkSize, p.worldSize - minZ);
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
                                affinity = cover.Weights(i).Dot(rule.biomes);
                            }

                            if (rule.clump > 0f)
                            {
                                float n = 0.5f + 0.5f * clumpNoise.Fbm(x * 0.012f + clumpOffset, z * 0.012f - clumpOffset, 2);
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
                                float exact = HeightmapGenerator.SampleHeight(map, p, x, z);
                                if (rule.onWater ? exact > cover.WaterHeight - 0.2f : exact < cover.WaterHeight + 0.3f) continue;
                            }

                            occupancy.Add(new Vector3(x, 0f, z), rule.footprint);
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
            OccupancyMap occupancy, PaletteKeys keys, List<PropPlacement> props)
        {
            var rng = new System.Random(p.seed + 16);
            var ctx = new FeatureContext(p, map, roads, cover, sites, fields, occupancy, keys, props, rng);
            PlaceRoadside(ctx, network);
            PlaceCairns(ctx, landmarks);
            PlaceRunes(ctx);
            PlaceWaterWheel(ctx);
        }

        private static void PlaceRoadside(FeatureContext ctx, RoadNetwork network)
        {
            float offsetBase = ctx.Corridor + 5f;
            foreach (RoadSegment segment in network.Segments)
            {
                if (segment.Length < 350f) continue;
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

                    if (kind < 0.4)
                    {
                        // Camp: tent, fire with tripod, crate and small items, 10-14 m off the road.
                        Vector3 centre = on + side * (offsetBase + 4f + (float)ctx.Rng.NextDouble() * 3f);
                        if (!ctx.Clear(centre, 6.5f, 0.22f)) continue;
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
                    else if (kind < 0.7)
                    {
                        Vector3 pos = on + side * (offsetBase + (float)ctx.Rng.NextDouble() * 2f);
                        if (!ctx.Clear(pos, 2.5f, 0.25f)) continue;
                        ctx.Occupancy.Add(pos, 2.5f);
                        ctx.Add(PropRole.Cart, pos, Yaw(tangent) + (float)(ctx.Rng.NextDouble() * 50.0 - 25.0), 1f);
                    }
                    else
                    {
                        Vector3 pos = on + side * (offsetBase - 2f + (float)ctx.Rng.NextDouble() * 2f);
                        if (!ctx.Clear(pos, 1.5f, 0.35f)) continue;
                        ctx.Occupancy.Add(pos, 1.5f);
                        ctx.Add(PropRole.StonePile, pos, (float)(ctx.Rng.NextDouble() * 360.0), 1f);
                    }
                }
            }
        }

        private static void PlaceCairns(FeatureContext ctx, IReadOnlyList<Landmark> landmarks)
        {
            foreach (Landmark landmark in landmarks)
            {
                if (landmark.kind != LandmarkKind.Peak) continue;
                Vector3 pos = landmark.position;
                if (ctx.Clear(pos, 1.5f, 10f)) ctx.Add(PropRole.StoneStack, pos, (float)(ctx.Rng.NextDouble() * 360.0), 1.3f);
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
                    ctx.Add(family, best, (float)(ctx.Rng.NextDouble() * 360.0), 0.9f + (float)ctx.Rng.NextDouble() * 0.4f);
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

        private sealed class FeatureContext
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

            public void Add(string family, Vector3 pos, float yaw, float scale)
            {
                if (!Keys.Has(family)) return;
                if (!Roads.IsClear(pos.x, pos.z, Corridor)) return;
                pos.y = GroundHeight(P, Map, pos.x, pos.z, 0.5f);
                Props.Add(new PropPlacement { role = family, variant = Keys.PickVariant(family, Rng), position = pos, yaw = yaw, scale = scale, settlementId = -1 });
            }
        }

        // ------------------------------------------------------------------ helpers

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
