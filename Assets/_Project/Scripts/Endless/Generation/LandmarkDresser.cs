using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// Dresses one landmark in a window of its own (cached by <see cref="DressingCache.Landmark"/>, so every tile agrees): the
    /// structure on its planned floor, a few props that make it read as a place (rubble and a wood pile at a tower, a churchyard
    /// of gravestones with a cross and dark yews at a chapel, haystacks and sacks at a windmill, a ring of 9-13 seated rune
    /// stones, rubble and a lone arch at a ruin), bushes and a tree or two, a worn yard where people work, and a path stub toward
    /// the road (cut short before a field, a stream or water). Rubble goes through <see cref="RockPlacer"/>; randomness stream 1208.
    /// Pure.
    /// </summary>
    public static class LandmarkDresser
    {
        /// <summary>Randomness stream of the dressing.</summary>
        public const int Stream = 1208;

        /// <summary>Edge of the window a landmark is dressed in, metres (a multiple of the 2 m grid).</summary>
        public const float WindowSize = 128f;

        /// <summary>Longest path stub toward the road, metres.</summary>
        public const float PathStubLength = 70f;

        /// <summary>Dresses <paramref name="lm"/> into <paramref name="d"/> on window <paramref name="w"/>; returns the occupancy (window-local).</summary>
        public static OccupancyMap Dress(EndlessParams p, PaletteKeys keys, RoadCurve road, TerrainField field, FieldMosaic mosaic, GenWindow w, LandmarkSite lm, Dressing d)
        {
            WorldGenParams wp = w.Params.Clone();
            wp.seed = EndlessNoise.Seed(p.seed, Stream, lm.Id);
            var occupancy = new OccupancyMap(w.Size);
            var rng = new System.Random(EndlessNoise.Seed(p.seed, Stream, lm.Id, 1));
            var ctx = new ScatterPlanner.FeatureContext(wp, w.Map, w.Roads, w.Cover, new List<SettlementSite>(), new List<FieldPatch>(), occupancy, keys, d.Props, rng);
            var c = new Vector3(w.LocalX(lm.X), lm.Ground, w.LocalZ(lm.Z));
            float yawRad = lm.Yaw * Mathf.Deg2Rad;
            var fwd = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            float baseR = LandmarkPlan.BaseRadiusOf(lm.Type);

            string role = LandmarkPlan.RoleOf(lm.Type);
            if (role != null && keys.Has(role))
            {
                d.Props.Add(new PropPlacement { role = role, variant = lm.Variant % keys.Count(role), position = c, yaw = lm.Yaw, scale = 1f, settlementId = lm.Id });
            }

            if (lm.Type != LandmarkType.Stones) occupancy.Add(c, baseR + 1.5f);
            switch (lm.Type)
            {
                case LandmarkType.Tower:
                    Rubble(ctx, occupancy, c, 6.5f, 9.5f, 3 + rng.Next(3), 0.6f, 1.1f);
                    Near(ctx, occupancy, PropRole.WoodPile, c + fwd * (baseR + 1.6f) + right * (1.5f + (float)rng.NextDouble() * 1.5f), lm.Yaw + 90f, 1f, 1.4f);
                    for (int i = 0, n = 1 + rng.Next(2); i < n; i++) Near(ctx, occupancy, PropRole.Goods, c + fwd * (baseR + 1.2f) - right * (1.2f + 1.1f * i), (float)(rng.NextDouble() * 360.0), 1f, 0.8f);
                    Bushes(w, rng, keys, d, occupancy, c, 7f, 12f, 3 + rng.Next(3));
                    if (rng.NextDouble() < 0.6) Trees(w, rng, keys, d, occupancy, c, 12f, 16f, 1, PropRole.MeadowTree);
                    d.Yards.Add(new Vector3(c.x, baseR + 2.5f, c.z));
                    break;

                case LandmarkType.Chapel:
                {
                    // A churchyard beside the chapel: rows of stones facing the same way, a cross among them, a yew or two.
                    float sideSign = rng.NextDouble() < 0.5 ? 1f : -1f;
                    Vector3 yard = c + right * sideSign * (baseR + 4.5f);
                    int rows = 2 + rng.Next(2), cols = 3 + rng.Next(2);
                    int crossAt = rng.Next(rows * cols);
                    int gravestones = keys.Count(PropRole.Gravestone);
                    for (int r = 0; r < rows; r++)
                    {
                        for (int k = 0; k < cols; k++)
                        {
                            if (rng.NextDouble() < 0.15) continue;   // a gap in the row
                            Vector3 at = yard + right * sideSign * (r * 2.3f) + fwd * ((k - (cols - 1) * 0.5f) * 1.9f + ((float)rng.NextDouble() - 0.5f) * 0.4f);
                            bool cross = r * cols + k == crossAt && gravestones > 0;
                            int variant = cross ? gravestones - 1 : rng.Next(Math.Max(1, gravestones - 1));
                            float yaw = lm.Yaw + (cross ? 0f : ((float)rng.NextDouble() - 0.5f) * 10f);
                            if (!ctx.Clear(at, 0.6f, 0.45f)) continue;
                            if (ctx.AddVariant(PropRole.Gravestone, variant, at, yaw, 1f)) occupancy.Add(at, 0.6f);
                        }
                    }

                    Trees(w, rng, keys, d, occupancy, yard + right * sideSign * (rows * 2.3f + 2f), 0f, 3f, 1 + rng.Next(2), PropRole.PineTree);
                    Bushes(w, rng, keys, d, occupancy, c, 8f, 12f, 2 + rng.Next(3));
                    break;
                }

                case LandmarkType.Mill:
                    for (int i = 0, n = 2 + rng.Next(2); i < n; i++)
                    {
                        float a = (float)(rng.NextDouble() * Math.PI * 2.0);
                        Near(ctx, occupancy, PropRole.Haystack, c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (baseR + 4f + (float)rng.NextDouble() * 3f), (float)(rng.NextDouble() * 360.0), 0.9f + 0.2f * (float)rng.NextDouble(), 2.2f);
                    }

                    for (int i = 0; i < 2; i++) Near(ctx, occupancy, PropRole.Goods, c + fwd * (baseR + 1.3f) + right * (i * 1.1f - 0.5f), (float)(rng.NextDouble() * 360.0), 1f, 0.8f);
                    if (rng.NextDouble() < 0.5) Near(ctx, occupancy, PropRole.Cart, c + fwd * (baseR + 3.5f) - right * 3f, lm.Yaw + 70f, 1f, 2f);
                    Bushes(w, rng, keys, d, occupancy, c, 8f, 11f, 2);
                    d.Yards.Add(new Vector3(c.x, baseR + 3f, c.z));
                    break;

                case LandmarkType.Stones:
                {
                    int count = 9 + rng.Next(5);
                    float ring = 8.5f + 3f * (float)rng.NextDouble();
                    int missing = rng.Next(count), missing2 = rng.NextDouble() < 0.5 ? rng.Next(count) : -1;
                    for (int i = 0; i < count; i++)
                    {
                        if (i == missing || i == missing2) continue;
                        float a = (i + ((float)rng.NextDouble() - 0.5f) * 0.3f) * Mathf.PI * 2f / count;
                        var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        Vector3 at = c + dir * (ring + ((float)rng.NextDouble() - 0.5f) * 0.8f);
                        float scale = 1.15f + 0.45f * (float)rng.NextDouble();
                        float yaw = a * Mathf.Rad2Deg + 90f + ((float)rng.NextDouble() - 0.5f) * 24f;   // broad face toward the centre
                        if (!ctx.Clear(at, 0.9f, 0.4f)) continue;
                        int before = d.Props.Count;
                        if (!ctx.AddVariant(PropRole.StoneRunes, keys.PickVariant(PropRole.StoneRunes, rng), at, yaw, scale)) continue;
                        occupancy.Add(at, 1f);
                        // Weathered: some lean a little, every stone sunk a little deeper on its downhill side.
                        PropPlacement stone = d.Props[before];
                        stone.pitch = ((float)rng.NextDouble() - 0.5f) * 10f;
                        stone.roll = ((float)rng.NextDouble() - 0.5f) * 8f;
                        stone.position.y -= 0.12f;
                        d.Props[before] = stone;
                    }

                    occupancy.Add(c, ring - 1.5f);   // the ring's grass stays open
                    Bushes(w, rng, keys, d, occupancy, c, ring + 3f, ring + 7f, 2 + rng.Next(2));
                    break;
                }

                default:
                    if (rng.NextDouble() < 0.7 && keys.Has(PropRole.StoneArch))
                    {
                        Vector3 at = c + fwd * (baseR + 6f + (float)rng.NextDouble() * 2f) + right * (((float)rng.NextDouble() - 0.5f) * 6f);
                        if (ctx.Clear(at, 3.2f, 0.3f))
                        {
                            ctx.Add(PropRole.StoneArch, at, lm.Yaw + 90f + ((float)rng.NextDouble() - 0.5f) * 20f, 1f);
                            occupancy.Add(at, 3.2f);
                        }
                    }

                    Rubble(ctx, occupancy, c, 5.5f, 10f, 3 + rng.Next(2), 0.5f, 1f);
                    Bushes(w, rng, keys, d, occupancy, c, 6f, 11f, 4 + rng.Next(3));
                    Trees(w, rng, keys, d, occupancy, c, 11f, 16f, 1 + rng.Next(2), PropRole.ForestTree);
                    break;
            }

            PathStub(road, field, mosaic, w, lm, c, baseR, d);
            Planting.LandmarkIvy(p, keys, field, lm, c, d);   // forest pass: ivy on the walls (its own stream, the rest is unchanged)
            return occupancy;
        }

        /// <summary>Rubble round the structure: rock piles seated by the rock placer.</summary>
        private static void Rubble(ScatterPlanner.FeatureContext ctx, OccupancyMap occupancy, Vector3 c, float r0, float r1, int count, float h0, float h1)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (float)(ctx.Rng.NextDouble() * Math.PI * 2.0);
                float r = r0 + (r1 - r0) * (float)ctx.Rng.NextDouble();
                var at = new Vector3(c.x + Mathf.Cos(a) * r, 0f, c.z + Mathf.Sin(a) * r);
                if (occupancy.Overlaps(at.x, at.z, 0.8f)) continue;
                if (ctx.AddRock(PropRole.RockPile, at.x, at.z, h0 + (h1 - h0) * (float)ctx.Rng.NextDouble(), out float radius)) occupancy.Add(at, Mathf.Max(0.8f, 0.8f * radius));
            }
        }

        /// <summary>One prop at <paramref name="at"/> when the spot is clear (gentle, dry, unoccupied).</summary>
        private static void Near(ScatterPlanner.FeatureContext ctx, OccupancyMap occupancy, string family, Vector3 at, float yaw, float scale, float radius)
        {
            if (!ctx.Keys.Has(family) || !ctx.Clear(at, radius, 0.35f)) return;
            if (ctx.AddVariant(family, ctx.Keys.PickVariant(family, ctx.Rng), at, yaw, scale)) occupancy.Add(at, radius);
        }

        private static void Bushes(GenWindow w, System.Random rng, PaletteKeys keys, Dressing d, OccupancyMap occupancy, Vector3 c, float r0, float r1, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2.0);
                float r = r0 + (r1 - r0) * (float)rng.NextDouble();
                string role = rng.NextDouble() < 0.7 ? PropRole.Bush : PropRole.Shrub;
                Plant(w, rng, keys, d, occupancy, role, c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r, 0.9f + 0.4f * (float)rng.NextDouble(), 1.2f);
            }
        }

        private static void Trees(GenWindow w, System.Random rng, PaletteKeys keys, Dressing d, OccupancyMap occupancy, Vector3 c, float r0, float r1, int count, string role)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2.0);
                float r = r0 + (r1 - r0) * (float)rng.NextDouble();
                Plant(w, rng, keys, d, occupancy, role, c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r, 0.95f + 0.35f * (float)rng.NextDouble(), 2.6f);
            }
        }

        private static void Plant(GenWindow w, System.Random rng, PaletteKeys keys, Dressing d, OccupancyMap occupancy, string role, float x, float z, float scale, float radius)
        {
            int variant = keys.PickVariant(role, rng);
            float yaw = (float)(rng.NextDouble() * 360.0);
            if (!keys.Has(role) || x < radius + 2f || z < radius + 2f || x > w.Size - radius - 2f || z > w.Size - radius - 2f) return;
            if (occupancy.Overlaps(x, z, radius)) return;
            int i = w.Cover.Index(x, z);
            if (w.Cover.IsWater(i) || w.Cover.Slope[i] > 0.6f || w.Cover.Shore[i] > 0.4f) return;
            if (!w.Roads.IsClear(x, z, w.Params.ScatterCorridor + radius)) return;
            occupancy.Add(new Vector3(x, 0f, z), radius);
            float y = ScatterPlanner.GroundHeight(w.Params, w.Map, x, z, radius * 0.3f);
            d.Trees.Add(new TreePlacement { role = role, variant = variant, position = new Vector3(x, y, z), yaw = yaw, scale = scale });
        }

        /// <summary>A worn path from the structure toward the road, stopped before a field, a stream or water.</summary>
        private static void PathStub(RoadCurve road, TerrainField field, FieldMosaic mosaic, GenWindow w, LandmarkSite lm, Vector3 c, float baseR, Dressing d)
        {
            double zr = road.Nearest(lm.X, lm.Z, out double distance);
            double tx = road.X(zr) - lm.X, tz = zr - lm.Z;
            double len = Math.Sqrt(tx * tx + tz * tz);
            if (len < 1e-3) return;
            tx /= len;
            tz /= len;
            float start = lm.Type == LandmarkType.Stones ? 12f : baseR + 1.5f;
            float end = (float)Math.Min(PathStubLength, distance - 40.0);
            if (end < start + 8f) return;
            var fields = new List<MosaicField>();
            mosaic.Overlapping(lm.X - PathStubLength - 10.0, lm.Z - PathStubLength - 10.0, 2.0 * PathStubLength + 20.0, fields);
            float reached = start;
            for (float t = start; t <= end; t += 4f)
            {
                double x = lm.X + tx * t, z = lm.Z + tz * t;
                bool blocked = field.WaterHeight(x, z) > 0f || (field.Streams != null && field.Streams.Clearance(x, z, 8.0) < 3f);
                foreach (MosaicField f in fields)
                {
                    if (FieldMosaic.Distance(f, x, z) < 3.0) blocked = true;
                }

                float lx = w.LocalX(x), lz = w.LocalZ(z);
                if (lx < 2f || lz < 2f || lx > w.Size - 2f || lz > w.Size - 2f) blocked = true;
                if (blocked) break;
                reached = t;
            }

            if (reached < start + 8f) return;
            d.Paths.Add(new Vector4(c.x + (float)tx * start, c.z + (float)tz * start, c.x + (float)tx * reached, c.z + (float)tz * reached));
        }
    }
}
