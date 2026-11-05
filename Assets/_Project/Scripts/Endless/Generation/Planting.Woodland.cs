using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>A clearing beside the road in a wood (absolute centre, radius) and the stretch of road it opens onto.</summary>
    public struct Glade
    {
        /// <summary>Absolute centre.</summary>
        public double X, Z;

        /// <summary>Road distance of the clearing's middle, and the stretch (S0..S1) where it meets the road.</summary>
        public double S, S0, S1;

        /// <summary>Radius of the clearing, metres.</summary>
        public float Radius;

        /// <summary>Side of the road (+1 right, −1 left, looking toward +s).</summary>
        public int Side;

        /// <summary>True when (x, z) is inside the clearing (with <paramref name="margin"/>).</summary>
        public bool Contains(double x, double z, double margin = 0.0)
        {
            double dx = x - X, dz = z - Z, r = Radius + margin;
            return dx * dx + dz * dz <= r * r;
        }
    }

    /// <summary>
    /// The woods (forest pass, 2026-10-05; endless only): where the land cover is wooded the road runs through the wood instead of
    /// past it. Trunks stand from <see cref="WoodTrunkCorridor"/> m of the road centre (the per-region trunk corridor,
    /// <see cref="TrunkCorridor"/>; the open-country rules keep the scatter's 12 m), in stretches where large oaks close over the
    /// road (a tunnel), with sparse ancient trees whose ring of shade keeps the understorey back, and glades every few hundred
    /// metres where the light comes in; under the trees a floor of ferns and undergrowth bushes (instanced plants, near tiles),
    /// logs and roots along the road edge; the PolygonGeneric broadleaves near the road and the dark Meadow bush are swapped for
    /// the detailed trees and the Generic bushes. Randomness streams 1251-1262. Pure and deterministic like the rest of
    /// <see cref="Planting"/>.
    /// </summary>
    public sealed partial class Planting
    {
        /// <summary>Randomness streams (EndlessNoise) of the woods: 1251-1262.</summary>
        private const int StreamWoodSlot = 1251, StreamTunnel = 1252, StreamGlade = 1253, StreamAncient = 1254, StreamFloor = 1255, StreamFloorPick = 1256,
            StreamLog = 1257, StreamRestyle = 1258, StreamFloorNoise = 1259, StreamAncientTree = 1260, StreamLandmarkIvy = 1261, StreamWallIvy = 1262;

        /// <summary>In a wood, trunks stand at least this far from the road centre (and as far as their collider and base need).</summary>
        public const float WoodTrunkCorridor = 5.5f;

        /// <summary>
        /// The walkable verge: no trunk collider within this distance of the road centre (the 5 m road plus 1.5 m), and no plant
        /// within <see cref="PlantVerge"/>.
        /// </summary>
        public const float Verge = 4f;

        /// <summary>No fern or bush stands within this distance of the road centre.</summary>
        public const float PlantVerge = 4.6f;

        /// <summary>Largest trunk collider radius at scale 1 the instanced renderer gives a tree (its prefab's, clamped), metres.</summary>
        public const float MaxTrunkCollider = 0.6f;

        /// <summary>The visible trunk base keeps at least this far off the road's edge, metres.</summary>
        public const float TrunkBaseMargin = 0.5f;

        /// <summary>Road distance between the wood-edge planting slots, metres.</summary>
        public const double WoodSlot = 4.0;

        /// <summary>One glade is planned per this much road (most cells have one).</summary>
        public const double GladeCell = 380.0;

        /// <summary>Land-cover forest weight from which the ground counts as a wood.</summary>
        public const float WoodedCover = 0.45f;

        /// <summary>Generic broadleaves within this distance of the road are drawn as the detailed trees instead.</summary>
        public const double RestyleReach = 150.0;

        /// <summary>Edge of the cells ancient trees are planned on, metres.</summary>
        public const double AncientCell = 75.0;

        /// <summary>Instanced-plant family: forest ferns (endless only).</summary>
        public const string FernRole = PropRole.Fern;

        private GradientNoise tunnelNoise, floorNoise;
        private int[] forestVariants = Array.Empty<int>();
        private int[] restyleVariants = Array.Empty<int>();
        private bool[] genericForest = Array.Empty<bool>();
        private int[] genericBushes = Array.Empty<int>();
        private bool[] darkBush = Array.Empty<bool>();

        private void InitWoodland()
        {
            tunnelNoise = new GradientNoise(EndlessNoise.Seed(p.seed, StreamTunnel, 0));
            floorNoise = new GradientNoise(EndlessNoise.Seed(p.seed, StreamFloorNoise, 0));
            InitWoodFloor();
            int n = keys.Count(PropRole.ForestTree);
            genericForest = new bool[n];
            var detailed = new List<int>();
            for (int v = 0; v < n; v++)
            {
                genericForest[v] = IsGenericBroadleaf(FarTreeShapes.CatalogName(PropRole.ForestTree, v));
                if (!genericForest[v]) detailed.Add(v);
            }

            forestVariants = detailed.Count > 0 ? detailed.ToArray() : new[] { 0 };
            // What a Generic broadleaf near the road becomes, weighted toward the lighter meshes (triangles LOD0 / mid / low):
            // the young birch (2,945 / 1,408 / 684) 4 in 10, the small oak (11,872 / 6,464 / 3,500) 3 in 10, the tall birches
            // (about 11,400 / 5,800 / 2,860) 3 in 10; never the large oak (25,278 / 12,700 / 6,562).
            var weighted = new List<int>();
            for (int v = 0; v < n; v++)
            {
                string name = FarTreeShapes.CatalogName(PropRole.ForestTree, v);
                int weight = name == "SM_Env_Tree_Birch_03" ? 8 : name == "SM_Env_Tree_Meadow_02" ? 6 : name == "SM_Env_Tree_Birch_01" || name == "SM_Env_Tree_Birch_02" ? 3 : 0;
                for (int k = 0; k < weight; k++) weighted.Add(v);
            }

            restyleVariants = weighted.Count > 0 ? weighted.ToArray() : forestVariants;
            int b = keys.Count(PropRole.Bush);
            darkBush = new bool[b];
            var generic = new List<int>();
            for (int v = 0; v < b; v++)
            {
                string name = FarTreeShapes.CatalogName(PropRole.Bush, v) ?? string.Empty;
                darkBush[v] = name == "SM_Env_Bush_03";
                if (name.StartsWith("SM_Gen_Env_Bush_", StringComparison.Ordinal)) generic.Add(v);
            }

            genericBushes = generic.ToArray();
        }

        /// <summary>True for the PolygonGeneric broadleaf trees (faceted blobs among the detailed Meadow trees).</summary>
        public static bool IsGenericBroadleaf(string prefabName) =>
            prefabName != null && prefabName.StartsWith("SM_Gen_Env_Tree_", StringComparison.Ordinal) && prefabName.IndexOf("Pine", StringComparison.Ordinal) < 0 &&
            prefabName.IndexOf("Dead", StringComparison.Ordinal) < 0;

        /// <summary>
        /// The trunk corridor of a region: trunks of the woods stand from this distance of the road centre. Forest 5.5 m; the woods of
        /// lake country, meadows and downs a little further back; farmland and passes keep the scatter's corridor (no wood edge there).
        /// </summary>
        public static float TrunkCorridor(RegionKind kind, float scatterCorridor)
        {
            switch (kind)
            {
                case RegionKind.Forest: return WoodTrunkCorridor;
                case RegionKind.LakeCountry: return 6.5f;
                case RegionKind.Meadow:
                case RegionKind.Downs: return 7f;
                default: return scatterCorridor;
            }
        }

        /// <summary>
        /// Least distance from the road centre of a trunk of base radius <paramref name="trunkRadius"/> (at scale 1) at
        /// <paramref name="scale"/>: the corridor, the verge plus the largest collider, and the road's edge plus the base and a margin.
        /// </summary>
        public static float MinTrunkDistance(float trunkRadius, float scale, float corridor, float roadHalfWidth = 2.5f)
        {
            float collider = Verge + MaxTrunkCollider * scale;
            float visual = roadHalfWidth + TrunkBaseMargin + trunkRadius * scale;
            return Math.Max(corridor, Math.Max(collider, visual));
        }

        /// <summary>Trunk base radius (scale 1) of a family's variant, from the measured far-tree styles.</summary>
        public static float TrunkRadiusOf(string role, int variant) =>
            FarTreeShapes.TryGet(FarTreeShapes.CatalogName(role, variant), out FarTreeStyle st) ? st.TrunkRadius : 0.5f;

        /// <summary>True on the stretches of road where large trees close over it (slow noise along the road).</summary>
        public bool Tunnel(double s) => EndlessNoise.Fbm1N(tunnelNoise, s, 1.0 / 260.0, 2) > 0.45f;

        /// <summary>Land-cover forest weight at an absolute point (the global field, so every window agrees).</summary>
        public float ForestAt(double x, double z) => ForestAt(field, x, z);

        /// <summary>Land-cover forest weight of <paramref name="field"/> at an absolute point.</summary>
        public static float ForestAt(TerrainField field, double x, double z)
        {
            // No height samples where the cover has no woods at all (exact: TerrainField.NoWoodsAt), and one row for every sample on z
            // (rows are immutable). 2026-10-06: this took half of a near tile's splat stage.
            if (field.NoWoodsAt(x, z)) return 0f;
            field.CountWoodSample();
            TerrainField.Row row = field.RowAt(z);
            float h = field.Height(row, x);
            float hx = field.Height(row, x + 2.0), hz = field.Height(x, z + 2.0);
            float slope = (float)Math.Sqrt((hx - h) * (hx - h) + (hz - h) * (hz - h)) / 2f;
            field.Cover(row, x, h, slope, out float forest, out _, out _, out _);
            return forest;
        }

        /// <summary>
        /// The glades along the road between <paramref name="s0"/> and <paramref name="s1"/>: about one per <see cref="GladeCell"/>
        /// m of wooded road, 16-28 m across, opening onto the road on one side. Pure: a function of the seed and the road distance.
        /// </summary>
        public void Glades(double s0, double s1, List<Glade> list)
        {
            for (long k = (long)Math.Floor(s0 / GladeCell) - 1; k * GladeCell <= s1 + GladeCell; k++)
            {
                if (EndlessNoise.Unit(p.seed, StreamGlade, k) > 0.85) continue;
                double s = k * GladeCell + 60.0 + (GladeCell - 120.0) * EndlessNoise.Unit(p.seed, StreamGlade, k, 1);
                if (s < s0 - 40.0 || s > s1 + 40.0) continue;
                Region region = regions.At(s);
                if (TrunkCorridor(region.Kind, 12f) >= 12f) continue;
                int side = EndlessNoise.Unit(p.seed, StreamGlade, k, 2) < 0.5 ? 1 : -1;
                float radius = 16f + 12f * (float)EndlessNoise.Unit(p.seed, StreamGlade, k, 3);
                double z = road.ZAt(s);
                double x = road.X(z, out double slope, out _);
                double len = Math.Sqrt(1.0 + slope * slope);
                double nx = 1.0 / len, nz = -slope / len;
                double off = radius + 6.0;
                double cx = x + nx * side * off, cz = z + nz * side * off;
                if (ForestAt(cx, cz) < WoodedCover) continue;
                if (field.WaterHeight(cx, cz) > 0f) continue;
                list.Add(new Glade { X = cx, Z = cz, S = s, S0 = s - radius * 0.9, S1 = s + radius * 0.9, Radius = radius, Side = side });
            }
        }

        private static bool InGladeSpan(Context ctx, double s, int side)
        {
            foreach (Glade g in ctx.Glades)
            {
                if (g.Side == side && s > g.S0 - 4.0 && s < g.S1 + 4.0) return true;
            }

            return false;
        }

        private static bool InGlade(Context ctx, double ax, double az, double margin = 0.0)
        {
            foreach (Glade g in ctx.Glades)
            {
                if (g.Contains(ax, az, margin)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ trees (near and far)

        private void Woodland(Context ctx, List<TreePlacement> trees)
        {
            GenWindow w = ctx.W;
            // The glades are kept clear of every later tree (the scatter's included).
            foreach (Glade g in ctx.Glades)
            {
                float lx = w.LocalX(g.X), lz = w.LocalZ(g.Z);
                if (lx < -g.Radius || lz < -g.Radius || lx > w.Size + g.Radius || lz > w.Size + g.Radius) continue;
                ctx.Occupancy.Add(new Vector3(lx, 0f, lz), g.Radius);
            }

            WoodEdge(ctx, trees);
            Ancient(ctx, trees);
        }

        /// <summary>Trees from 5.5 m of the road wherever it runs through a wood: in a tunnel, large oaks whose crowns meet over it.</summary>
        private void WoodEdge(Context ctx, List<TreePlacement> trees)
        {
            GenWindow w = ctx.W;
            float half = w.Params.roadWidth * 0.5f;
            double s0 = road.S(w.Z0 - 20.0), s1 = road.S(w.Z0 + w.Size + 20.0);
            for (long j = (long)Math.Floor(s0 / WoodSlot); j * WoodSlot <= s1; j++)
            {
                double sBase = j * WoodSlot;
                Region region = regions.At(sBase);
                float corridor = TrunkCorridor(region.Kind, w.Params.ScatterCorridor);
                if (corridor >= w.Params.ScatterCorridor) continue;
                if (NearPlace(ctx, sBase)) continue;
                bool tunnel = region.Kind == RegionKind.Forest && Tunnel(sBase);
                for (int k = 0; k < 2; k++)
                {
                    int side = k == 0 ? 1 : -1;
                    if (InGladeSpan(ctx, sBase, side)) continue;
                    double u = EndlessNoise.Unit(EndlessNoise.Hash(p.seed, StreamWoodSlot, j, k));
                    if (u >= (tunnel ? 0.46 : 0.3)) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamWoodSlot, j, k + 2));
                    double s = sBase + (rng.NextDouble() - 0.5) * WoodSlot * 0.8;
                    double z = road.ZAt(s);
                    double x = road.X(z, out double slope, out _);
                    double len = Math.Sqrt(1.0 + slope * slope);
                    double nx = 1.0 / len, nz = -slope / len;
                    if (ForestAt(x + nx * side * 14.0, z + nz * side * 14.0) < WoodedCover) continue;   // the wood reaches the road here

                    string role;
                    int variant;
                    float scale;
                    double roll = rng.NextDouble();
                    if (tunnel)
                    {
                        role = PropRole.MeadowTree;
                        variant = roll < 0.4 ? 0 : 1;   // Meadow oaks (catalogue order): the crowns that can meet over the road
                        scale = 1.25f + 0.35f * (float)rng.NextDouble();
                    }
                    else if (roll < 0.22)
                    {
                        role = PropRole.MeadowTree;
                        variant = rng.Next(2);
                        scale = 1f + 0.35f * (float)rng.NextDouble();
                    }
                    else
                    {
                        role = PropRole.ForestTree;
                        variant = forestVariants[rng.Next(forestVariants.Length)];
                        scale = 0.9f + 0.4f * (float)rng.NextDouble();
                    }

                    if (!keys.Has(role)) continue;
                    variant %= keys.Count(role);
                    float min = MinTrunkDistance(TrunkRadiusOf(role, variant), scale, corridor, half);
                    double offset = min + (tunnel ? 1.8 : 5.5) * rng.NextDouble();
                    double ax = x + nx * side * offset, az = z + nz * side * offset;
                    road.Nearest(ax, az, out double distance);
                    if (distance < min) continue;
                    float yaw = (float)(rng.NextDouble() * 360.0);
                    TryTreeAt(ctx, trees, role, variant, w.LocalX(ax), w.LocalZ(az), scale, yaw, min - 2f, 0.55f, 2.4f);
                }
            }
        }

        /// <summary>Sparse ancient oaks deep in the woods (scale 1.45-1.8) with a ring of shade the understorey keeps out of.</summary>
        private void Ancient(Context ctx, List<TreePlacement> trees)
        {
            GenWindow w = ctx.W;
            if (!keys.Has(PropRole.MeadowTree)) return;
            float half = w.Params.roadWidth * 0.5f;
            long c0x = (long)Math.Floor((w.X0 - 20.0) / AncientCell), c1x = (long)Math.Floor((w.X0 + w.Size + 20.0) / AncientCell);
            long c0z = (long)Math.Floor((w.Z0 - 20.0) / AncientCell), c1z = (long)Math.Floor((w.Z0 + w.Size + 20.0) / AncientCell);
            for (long cz = c0z; cz <= c1z; cz++)
            {
                for (long cx = c0x; cx <= c1x; cx++)
                {
                    if (EndlessNoise.Unit(p.seed, StreamAncient, cx, cz) >= 0.32) continue;
                    double ax = (cx + 0.15 + 0.7 * EndlessNoise.Unit(p.seed, StreamAncient, cx, cz + 7919)) * AncientCell;
                    double az = (cz + 0.15 + 0.7 * EndlessNoise.Unit(p.seed, StreamAncient, cx + 7919, cz)) * AncientCell;
                    float lx = w.LocalX(ax), lz = w.LocalZ(az);
                    if (lx < -10f || lz < -10f || lx > w.Size + 10f || lz > w.Size + 10f) continue;
                    double sRoad = road.Nearest(ax, az, out double distance);
                    Region region = regions.At(sRoad);
                    float corridor = TrunkCorridor(region.Kind, w.Params.ScatterCorridor);
                    if (corridor >= w.Params.ScatterCorridor) continue;
                    if (ForestAt(ax, az) < 0.6f || InGlade(ctx, ax, az, 8.0)) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamAncientTree, cx, cz));
                    int variant = (rng.NextDouble() < 0.6 ? 0 : 1) % keys.Count(PropRole.MeadowTree);
                    float scale = 1.45f + 0.35f * (float)rng.NextDouble();
                    float min = MinTrunkDistance(TrunkRadiusOf(PropRole.MeadowTree, variant), scale, corridor, half) + 2f;
                    if (distance < min) continue;
                    TryTreeAt(ctx, trees, PropRole.MeadowTree, variant, lx, lz, scale, (float)(rng.NextDouble() * 360.0), min - 2f, 0.45f, 6.5f);
                }
            }
        }

        private bool TryTreeAt(Context ctx, List<TreePlacement> trees, string role, int variant, float lx, float lz, float scale, float yaw, float clearance, float maxSlope, float footprint)
        {
            if (!keys.Has(role) || !Fits(ctx, lx, lz, footprint, clearance, maxSlope)) return false;
            if (TooHigh(ctx, role, lx, lz)) return false;
            ctx.Occupancy.Add(new Vector3(lx, 0f, lz), footprint);
            ctx.Trunks?.Add(new Vector2(lx, lz));
            if (!InTile(ctx, lx, lz)) return true;
            float y = ScatterPlanner.GroundHeight(ctx.W.Params, ctx.W.Map, lx, lz, Math.Min(footprint, 3f) * 0.3f);
            trees.Add(new TreePlacement { role = role, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale });
            return true;
        }

        // ------------------------------------------------------------------ the floor (near tiles)

        /// <summary>
        /// Near tiles, after the scatter: logs and roots along the road edge in the woods, then the understorey (clusters of ferns and
        /// undergrowth, logs in the wood ringed by ferns; see <see cref="WoodUnderstorey"/>).
        /// </summary>
        private void WoodFloor(Context ctx, List<PropPlacement> bushes)
        {
            // Forest floor pass (2026-10-06): the understorey grows in clusters and drifts (Planting.WoodShade.cs), not one plant
            // per 3 m cell; the logs along the road edge first, so the understorey grows round them.
            RoadEdgeLogs(ctx, bushes);
            WoodUnderstorey(ctx, bushes);
        }

        /// <summary>Logs, roots and stumps lying along the road edge in the woods (GameObject props: logs keep their colliders).</summary>
        private void RoadEdgeLogs(Context ctx, List<PropPlacement> props)
        {
            GenWindow w = ctx.W;
            int count = keys.Count(PropRole.Stump);
            if (count == 0) return;
            const double Slot = 23.0;
            double s0 = road.S(w.Z0 - 10.0), s1 = road.S(w.Z0 + w.Size + 10.0);
            for (long j = (long)Math.Floor(s0 / Slot); j * Slot <= s1; j++)
            {
                double sBase = j * Slot;
                Region region = regions.At(sBase);
                if (TrunkCorridor(region.Kind, w.Params.ScatterCorridor) >= w.Params.ScatterCorridor || NearPlace(ctx, sBase)) continue;
                var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamLog, j));
                if (rng.NextDouble() > 0.4) continue;
                int side = rng.NextDouble() < 0.5 ? 1 : -1;
                double s = sBase + (rng.NextDouble() - 0.5) * Slot * 0.6;
                double z = road.ZAt(s);
                double x = road.X(z, out double slope, out _);
                double len = Math.Sqrt(1.0 + slope * slope);
                double nx = 1.0 / len, nz = -slope / len;
                double offset = 6.5 + 4.5 * rng.NextDouble();
                double ax = x + nx * side * offset, az = z + nz * side * offset;
                if (ForestAt(ax, az) < WoodedCover || InGlade(ctx, ax, az)) continue;
                // Catalogue order: stumps 0-2, logs 3-4, roots 5-6.
                double roll = rng.NextDouble();
                int variant = (roll < 0.5 ? 3 + rng.Next(2) : roll < 0.8 ? 5 + rng.Next(2) : rng.Next(3)) % count;
                float heading = (float)(Math.Atan2(slope, 1.0) * 180.0 / Math.PI);
                float yaw = heading + ((float)rng.NextDouble() - 0.5f) * 50f + (rng.NextDouble() < 0.5 ? 180f : 0f);
                float lx = w.LocalX(ax), lz = w.LocalZ(az);
                road.Nearest(ax, az, out double distance);
                if (distance < 6.0) continue;
                TryPlant(ctx, props, PropRole.Stump, variant, lx, lz, 0.9f + 0.35f * (float)rng.NextDouble(), yaw, 1.6f, 0.4f);
            }
        }

        /// <summary>True when (lx, lz) is at least <paramref name="clearance"/> from the road centre (exactly, near the road).</summary>
        private bool RoadClear(GenWindow w, float lx, float lz, float clearance)
        {
            float sample = w.Roads.Sample(lx, lz);
            if (sample - w.Roads.TexelSize * 0.75f >= clearance) return true;
            if (sample + w.Roads.TexelSize * 0.75f < clearance) return false;
            road.Nearest(w.X0 + lx, w.Z0 + lz, out double distance);
            return distance >= clearance;
        }

        private void TryPlant(Context ctx, List<PropPlacement> list, string role, int variant, float lx, float lz, float scale, float yaw, float footprint, float maxSlope)
        {
            if (!Fits(ctx, lx, lz, footprint, 0f, maxSlope)) return;
            if (TooHigh(ctx, PropRole.PineTree, lx, lz)) return;
            ctx.Occupancy.Add(new Vector3(lx, 0f, lz), footprint);
            if (!InTile(ctx, lx, lz)) return;
            float y = ScatterPlanner.GroundHeight(ctx.W.Params, ctx.W.Map, lx, lz, footprint * 0.5f);
            list.Add(new PropPlacement { role = role, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale, settlementId = -1 });
        }

        // ------------------------------------------------------------------ restyling (near and far alike)

        /// <summary>
        /// Swaps the PolygonGeneric broadleaves within <see cref="RestyleReach"/> of the road for the detailed birches and oaks (the
        /// faceted Generic crowns read as placeholders beside them) and the dark Meadow bush (its oak-leaf texture is near black in
        /// shade) for the Generic bushes. Positions are window-local; the pick is hashed from the position, so near and distant
        /// tiles agree. Pines and dead trees are kept.
        /// </summary>
        public void Restyle(GenWindow w, List<TreePlacement> trees)
        {
            for (int i = 0; i < trees.Count; i++)
            {
                TreePlacement t = trees[i];
                if (t.role == PropRole.ForestTree && t.variant >= 0 && t.variant < genericForest.Length && genericForest[t.variant])
                {
                    double ax = w.X0 + t.position.x, az = w.Z0 + t.position.z;
                    road.Nearest(ax, az, out double distance);
                    if (distance > RestyleReach) continue;
                    t.variant = restyleVariants[(int)(PositionHash(ax, az) % (ulong)restyleVariants.Length)];
                    trees[i] = t;
                }
            }
        }

        /// <summary>The dark Meadow bush (instanced plants of the countryside) swapped for a Generic bush (see <see cref="Restyle"/>).</summary>
        public int RestyleBush(string role, int variant, double ax, double az)
        {
            if (role != PropRole.Bush || genericBushes.Length == 0 || variant < 0 || variant >= darkBush.Length || !darkBush[variant]) return variant;
            return genericBushes[(int)(PositionHash(ax, az) % (ulong)genericBushes.Length)];
        }

        private ulong PositionHash(double ax, double az) => EndlessNoise.Hash(p.seed, StreamRestyle, (long)Math.Round(ax * 8.0), (long)Math.Round(az * 8.0));
        // ------------------------------------------------------------------ glades: grass, flowers and light paint

        /// <summary>
        /// Glade floors on a splat grid (sample i at <paramref name="offset"/> + (i + 0.5) · <paramref name="texel"/>, window-local):
        /// lush grass and flower-meadow paint inside the clearing, fading over its last 5 m; the road and paths keep their dirt.
        /// </summary>
        public static void PaintGlades(float[,,] splat, GenWindow w, float offset, float texel, IReadOnlyList<Glade> glades)
        {
            if (splat == null || glades == null || glades.Count == 0) return;
            int rz = splat.GetLength(0), rx = splat.GetLength(1), layers = splat.GetLength(2);
            if (layers <= TerrainShaper.FlowerLayer) return;
            foreach (Glade g in glades)
            {
                for (int z = 0; z < rz; z++)
                {
                    double az = w.Z0 + offset + (z + 0.5) * texel;
                    if (Math.Abs(az - g.Z) > g.Radius) continue;
                    for (int x = 0; x < rx; x++)
                    {
                        double ax = w.X0 + offset + (x + 0.5) * texel;
                        double dx = ax - g.X, dz = az - g.Z, d = Math.Sqrt(dx * dx + dz * dz);
                        if (d >= g.Radius) continue;
                        float k = 0.75f * Mathf.Clamp01((float)(g.Radius - d) / 5f) * (1f - Mathf.Clamp01(splat[z, x, TerrainShaper.DirtLayer] * 2f));
                        if (k <= 0f) continue;
                        for (int l = 0; l < layers; l++) splat[z, x, l] *= 1f - k;
                        splat[z, x, TerrainShaper.FlowerLayer] += 0.55f * k;
                        splat[z, x, TerrainShaper.LushGrassLayer] += 0.45f * k;
                    }
                }
            }
        }

        /// <summary>
        /// Glade grass on a detail grid (cell i centred at (<paramref name="offset"/> + i + 0.5) · <paramref name="texel"/>,
        /// window-local): tall grass nearly everywhere in the clearing and drifts of wildflowers (endless only).
        /// </summary>
        public void GladeDetails(List<DetailLayer> layers, GenWindow w, int offset, int res, float texel, IReadOnlyList<Glade> glades)
        {
            if (glades == null || glades.Count == 0) return;
            var tall = new List<DetailLayer>();
            var flowers = new List<DetailLayer>();
            foreach (DetailLayer l in layers)
            {
                if (l.family == PropRole.TallGrass) tall.Add(l);
                else if (l.family == PropRole.Wildflowers || l.family == PropRole.GenFlowers) flowers.Add(l);
            }

            if (tall.Count == 0 && flowers.Count == 0) return;
            foreach (Glade g in glades)
            {
                for (int z = 0; z < res; z++)
                {
                    double az = w.Z0 + (z + offset + 0.5) * texel;
                    if (Math.Abs(az - g.Z) > g.Radius) continue;
                    for (int x = 0; x < res; x++)
                    {
                        double ax = w.X0 + (x + offset + 0.5) * texel;
                        double dx = ax - g.X, dz = az - g.Z;
                        if (dx * dx + dz * dz > g.Radius * g.Radius) continue;
                        int i = z * res + x;
                        bool grows = false;
                        foreach (DetailLayer l in layers)
                        {
                            if (l.density[i] > 0) grows = true;
                        }

                        if (!grows) continue;   // road, path, water or stream bank: left alone
                        ulong h = EndlessNoise.Hash(p.seed, StreamFloorPick, 7919 + (long)Math.Floor(ax / texel), (long)Math.Floor(az / texel));
                        double u = EndlessNoise.Unit(h);
                        if (tall.Count > 0 && u < 0.7)
                        {
                            DetailLayer t = tall[(int)((h >> 8) % (ulong)tall.Count)];
                            if (t.density[i] == 0) t.density[i] = 1;
                        }

                        float drift = EndlessNoise.FbmN(floorNoise, ax, az, 1.0 / 14.0, 2);
                        if (flowers.Count > 0 && drift > 0.2f && u < 0.55) flowers[(int)((h >> 20) % (ulong)flowers.Count)].density[i] = 1;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ climbers

        /// <summary>
        /// Ivy draped over the stone walls of the field boundaries in and near the woods (about two in five wall pieces in a wood,
        /// one in eight at its edge): an <see cref="PropRole.IvyDrape"/> piece hung from the top edge of one face, appended to
        /// <paramref name="pieces"/> (window-local, like them).
        /// </summary>
        public void WallIvy(GenWindow w, List<PropPlacement> pieces)
        {
            if (!keys.Has(PropRole.IvyDrape)) return;
            int count = pieces.Count;
            for (int i = 0; i < count; i++)
            {
                PropPlacement wall = pieces[i];
                bool longWall = wall.role == PropRole.StoneWall;
                if (!longWall && wall.role != PropRole.StoneWallSmall) continue;
                double ax = w.X0 + wall.position.x, az = w.Z0 + wall.position.z;
                ulong h = EndlessNoise.Hash(p.seed, StreamWallIvy, (long)Math.Round(ax * 4.0), (long)Math.Round(az * 4.0));
                double u = EndlessNoise.Unit(h);
                if (u >= 0.42) continue;
                float forest = ForestAt(ax, az);
                double chance = forest >= 0.3f ? 0.42 : forest >= 0.12f ? 0.12 : 0.0;
                if (u >= chance) continue;
                var rng = new System.Random((int)(h & 0x7fffffff));
                int side = rng.NextDouble() < 0.5 ? 1 : -1;
                float s = wall.scale > 0f ? wall.scale : 1f;
                float along = longWall ? ((float)rng.NextDouble() - 0.5f) * 2.4f : 0f;
                // Wall piece frame (laid along +Z, centred): faces at x = ±0.36 m, top at 1.17 m (Meadow Stonewall_Long_01 / Small).
                float yaw = wall.yaw * Mathf.Deg2Rad;
                float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw);
                float lx = side * 0.37f * s, lz = along * s;
                var offset = new Vector3(lx * cos + lz * sin, 1.12f * s - along * s * (float)Math.Sin(wall.pitch * Mathf.Deg2Rad), -lx * sin + lz * cos);
                pieces.Add(new PropPlacement
                {
                    role = PropRole.IvyDrape, variant = keys.PickVariant(PropRole.IvyDrape, rng), position = wall.position + offset,
                    yaw = wall.yaw + 90f * side, scale = (0.7f + 0.2f * (float)rng.NextDouble()) * s, settlementId = -1,
                });
            }
        }

        /// <summary>
        /// Ivy panels on a landmark's walls (ruins always, towers and chapels in or near a wood; randomness stream 1261, so the rest
        /// of the dressing is unchanged): 2-5 panels on the faces of the structure's measured footprint, rooted at the ground.
        /// </summary>
        public static void LandmarkIvy(EndlessParams p, PaletteKeys keys, TerrainField field, LandmarkSite lm, Vector3 c, Dressing d)
        {
            if (!keys.Has(PropRole.Ivy)) return;
            float hx, hz, top;
            switch (lm.Type)
            {
                case LandmarkType.Ruin:
                    hx = 4.19f;
                    hz = lm.Variant % 2 == 0 ? 4.18f : 2.93f;
                    top = 2.9f;
                    break;
                case LandmarkType.Tower:
                    hx = 4.5f;
                    hz = 4.69f;
                    top = 14.5f;
                    break;
                case LandmarkType.Chapel:
                    hx = 3.22f;
                    hz = 5.46f;
                    top = 3.55f;
                    break;
                default:
                    return;
            }

            if (lm.Type != LandmarkType.Ruin && ForestAt(field, lm.X, lm.Z) < 0.2f) return;
            var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamLandmarkIvy, lm.Id));
            int panels = 2 + rng.Next(lm.Type == LandmarkType.Ruin ? 4 : 3);
            float yaw = lm.Yaw * Mathf.Deg2Rad;
            var fwd = new Vector3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw));
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            for (int i = 0; i < panels; i++)
            {
                int face = rng.Next(4);
                Vector3 normal = face == 0 ? right : face == 1 ? -right : face == 2 ? fwd : -fwd;
                Vector3 along = face < 2 ? fwd : right;
                float halfLength = face < 2 ? hz : hx, depth = face < 2 ? hx : hz;
                float t = ((float)rng.NextDouble() * 2f - 1f) * Math.Max(0f, halfLength - 1.2f);
                int variant = keys.PickVariant(PropRole.Ivy, rng);
                // The panels are 2.1-4.2 m tall, centred on their pivot (catalogue order: three short ones, then two tall); a tall one
                // only where the wall is tall.
                float panelHalf = variant >= 3 ? 2.05f : 1.15f;
                float scale = Math.Min(0.8f + 0.35f * (float)rng.NextDouble(), (top + 0.3f) / (2f * panelHalf));
                Vector3 at = c + normal * (depth + 0.02f) + along * t;
                at.y = lm.Ground + panelHalf * scale - 0.15f;
                d.Props.Add(new PropPlacement
                {
                    role = PropRole.Ivy, variant = variant, position = at, yaw = (float)(Math.Atan2(normal.x, normal.z) * 180.0 / Math.PI), scale = scale,
                    settlementId = -1,
                });
            }
        }
    }
}
