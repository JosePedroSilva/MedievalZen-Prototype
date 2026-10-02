using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Composes each settlement from props on its flattened disc (inn, village, town layouts), keeping the road
    /// corridor clear. Every placement is registered in the <see cref="OccupancyMap"/> so later scatter avoids it.
    /// </summary>
    public static class SettlementDresser
    {
        private const float CabinFootprint = 5.5f;

        /// <summary>Stump-family variants that are logs (SyntyCatalog order: Stump 1-3, Log 1-2, Root 1-2).</summary>
        private static readonly int[] LogVariants = { 3, 4 };

        /// <summary>
        /// Dresses every settlement of <paramref name="network"/>; settlement <paramref name="mountainInnId"/> (if any) is
        /// dressed as a high refuge instead of a roadside inn.
        /// </summary>
        public static void Dress(WorldGenParams p, float[,] map, RoadNetwork network, RoadDistanceField roads, PaletteKeys keys,
            OccupancyMap occupancy, List<PropPlacement> props, int mountainInnId = -1)
        {
            foreach (Settlement settlement in network.Settlements)
            {
                var ctx = new Context(p, map, roads, keys, occupancy, props, settlement);
                if (settlement.Id == mountainInnId)
                {
                    DressMountainInn(ctx);
                    continue;
                }

                switch (settlement.Type)
                {
                    case SettlementType.Inn: DressInn(ctx); break;
                    case SettlementType.Village: DressVillage(ctx); break;
                    default: DressTown(ctx); break;
                }
            }
        }

        private static void DressInn(Context ctx)
        {
            Vector3 c = ctx.Center;
            Vector3 road = ctx.RoadDirections.Count > 0 ? ctx.RoadDirections[0] : Vector3.right;
            var side = new Vector3(road.z, 0f, -road.x);
            if (ctx.Rng.Next(2) == 0) side = -side;
            float half = ctx.HalfRoad;

            Vector3 cabin = Vector3.zero;
            bool placed = false;
            float[] alongOffsets = { 0f, 6f, -6f, 12f, -12f };
            for (int s = 0; s < 2 && !placed; s++, side = -side)
            {
                foreach (float along in alongOffsets)
                {
                    Vector3 pos = c + side * 15f + road * along;
                    if (ctx.TryPlace(PropRole.Cabin, pos, ScatterPlanner.Yaw(-side), CabinFootprint, half + 1.5f))
                    {
                        cabin = pos;
                        placed = true;
                        break;
                    }
                }
            }

            if (!placed) return;
            Vector3 sideDir = (cabin - c) - road * Vector3.Dot(cabin - c, road);
            sideDir = sideDir.sqrMagnitude > 0.01f ? sideDir.normalized : side;
            float along0 = Vector3.Dot(cabin - c, road);

            ctx.TryPlace(PropRole.Sign, c + sideDir * (half + 3.5f) + road * (along0 + 7f), ScatterPlanner.Yaw(-sideDir), 0.6f, half + 0.5f);
            foreach (float offset in new[] { -6f, 12f, -12f, 16f })
            {
                if (ctx.TryPlace(PropRole.HorseHitch, c + sideDir * (half + 5f) + road * (along0 + offset), ScatterPlanner.Yaw(road), 1.4f, half + 0.5f)) break;
            }
            if (!ctx.TryPlace(PropRole.Well, cabin + road * 11f, 0f, 1.8f, half + 2f))
            {
                ctx.TryPlace(PropRole.Well, cabin - road * 11f, 0f, 1.8f, half + 2f);
            }

            bool fire = ctx.TryPlace(PropRole.Fireplace, cabin + sideDir * 8f - road * 6f, 0f, 1.5f, half + 2f);
            ctx.TryPlace(PropRole.Crate, cabin + road * 5f + sideDir * 3.5f, (float)(ctx.Rng.NextDouble() * 360.0), 0.8f, half + 1.5f);
            Vector3 fireSpot = cabin + sideDir * 8f - road * 6f;
            if (fire) ctx.ForcePlaceNoFootprint(PropRole.FireTripod, fireSpot, (float)(ctx.Rng.NextDouble() * 360.0));
            ScatterGoods(ctx, cabin + road * 5f - sideDir * 1f, 2 + ctx.Rng.Next(3), 2.5f);
            if (ctx.TryPlace(PropRole.Table, cabin - road * 5f - sideDir * 4f, ScatterPlanner.Yaw(road), 1.2f, half + 1.5f))
            {
                Vector3 table = cabin - road * 5f - sideDir * 4f;
                ctx.ForcePlaceNoFootprint(PropRole.Chair, table + road * 1.1f, ScatterPlanner.Yaw(-road));
                ctx.ForcePlaceNoFootprint(PropRole.Chair, table - road * 1.1f, ScatterPlanner.Yaw(road));
            }


            // A short fence run behind the cabin.
            float length = Mathf.Max(0.5f, ctx.Keys.fenceLength);
            int pieces = 3 + ctx.Rng.Next(2);
            Vector3 start = cabin + sideDir * 9f - road * (length * pieces * 0.5f);
            for (int i = 0; i < pieces; i++)
            {
                ctx.TryPlace(PropRole.Fence, start + road * ((i + 0.5f) * length), ScatterPlanner.Yaw(road), 0.8f, half + 1.5f);
            }
        }

        /// <summary>
        /// A refuge at the pass: the stone cabin in the widest gap between the roads, a curved dry-stone wall behind it as a
        /// windbreak, a fire with a cooking tripod, a hitch and a sign by the road, stacked firewood against the cabin, a few
        /// barrels and crates, and a cairn across the road. No well, no fences, no fields.
        /// </summary>
        private static void DressMountainInn(Context ctx)
        {
            Vector3 c = ctx.Center;
            Vector3 gap = ctx.LargestGap();
            float gapAngle = Mathf.Atan2(gap.z, gap.x);
            float half = ctx.HalfRoad;
            Vector3 cabin = Vector3.zero;
            bool placed = false;
            float[] turns = { 0f, 0.35f, -0.35f, 0.7f, -0.7f, 1.05f, -1.05f };
            foreach (float radius in new[] { 14f, 18f, 11f })
            {
                foreach (float turn in turns)
                {
                    Vector3 pos = c + Dir(gapAngle + turn) * radius;
                    if (ctx.TryPlace(PropRole.Cabin, pos, ScatterPlanner.Yaw(c - pos), CabinFootprint, half + 1.5f))
                    {
                        cabin = pos;
                        placed = true;
                        break;
                    }
                }

                if (placed) break;
            }

            if (!placed) return;
            Vector3 outward = cabin - c;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : gap;
            var tangent = new Vector3(-outward.z, 0f, outward.x);

            // Windbreak: a dry-stone wall arc behind the cabin.
            if (ctx.Keys.Has(PropRole.StoneWall))
            {
                float length = Mathf.Max(0.5f, ctx.Keys.wallLength);
                const float WallRadius = 8.5f;
                float step = length / WallRadius;
                float centreAngle = Mathf.Atan2(outward.z, outward.x);
                const int Pieces = 5;
                for (int k = 0; k < Pieces; k++)
                {
                    float a = centreAngle + (k - (Pieces - 1) * 0.5f) * step;
                    Vector3 pos = cabin + Dir(a) * WallRadius;
                    var along = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    bool end = k == 0 || k == Pieces - 1;
                    string piece = end && ctx.Keys.Has(PropRole.StoneWallSmall) ? PropRole.StoneWallSmall : PropRole.StoneWall;
                    if (ctx.TryPlace(piece, pos, ScatterPlanner.Yaw(along), length * 0.4f, half + 2f) && end)
                    {
                        float sign = k == 0 ? -1f : 1f;
                        ctx.ForcePlaceNoFootprint(PropRole.StoneWallPillar, pos + along * (sign * length * 0.5f), ScatterPlanner.Yaw(along));
                    }
                }
            }

            // Fire with a tripod in front of the cabin, off to one side.
            Vector3 fire = cabin - outward * 7f + tangent * 5f;
            if (ctx.TryPlace(PropRole.Fireplace, fire, 0f, 1.5f, half + 2f)) ctx.ForcePlaceNoFootprint(PropRole.FireTripod, fire, (float)(ctx.Rng.NextDouble() * 360.0));

            // Firewood stacked against the cabin, a few goods by the door.
            for (int i = 0; i < 3; i++)
            {
                Vector3 log = cabin - tangent * (4.2f + 0.2f * i) + outward * (1.2f * i - 1.2f);
                ctx.TryPlaceVariant(PropRole.Stump, LogVariants[i % LogVariants.Length], log, ScatterPlanner.Yaw(outward) + 90f, 0.5f, half + 1.5f);
            }

            ScatterGoods(ctx, cabin + tangent * 4.5f - outward * 2f, 3, 2.2f);
            ctx.TryPlace(PropRole.Crate, cabin + tangent * 3.5f - outward * 4.5f, (float)(ctx.Rng.NextDouble() * 360.0), 0.8f, half + 1.5f);

            // Hitch and sign facing the road; a cairn across the road.
            Vector3 road = ctx.RoadDirections.Count > 0 ? ctx.RoadDirections[0] : tangent;
            var roadSide = new Vector3(road.z, 0f, -road.x);
            if (Vector3.Dot(roadSide, outward) < 0f) roadSide = -roadSide;
            foreach (float offset in new[] { 9f, -9f, 14f, -14f })
            {
                if (ctx.TryPlace(PropRole.HorseHitch, c + roadSide * (half + 4.5f) + road * offset, ScatterPlanner.Yaw(road), 1.4f, half + 0.5f)) break;
            }

            foreach (float offset in new[] { 6f, -6f, 11f })
            {
                if (ctx.TryPlace(PropRole.Sign, c + roadSide * (half + 3f) + road * offset, ScatterPlanner.Yaw(-roadSide), 0.6f, half + 0.5f)) break;
            }

            foreach (float offset in new[] { 4f, -4f, 10f, -10f })
            {
                if (ctx.TryPlaceScaled(PropRole.StoneStack, c - roadSide * (half + 4f) + road * offset, (float)(ctx.Rng.NextDouble() * 360.0), 0.6f, 1.3f, half + 1f)) break;
            }
        }

        private static void DressVillage(Context ctx)
        {
            Vector3 c = ctx.Center;
            Vector3 gap = ctx.LargestGap();
            if (!PlaceNear(ctx, PropRole.Well, c, gap, 12f, 1.8f, 0f, out Vector3 well)) well = c + gap * 12f;

            int cabins = 3 + ctx.Rng.Next(3);
            float baseAngle = Mathf.Atan2(gap.z, gap.x);
            for (int k = 0; k < cabins; k++)
            {
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    float angle = baseAngle + (k - (cabins - 1) * 0.5f) * 1.2f + (float)(ctx.Rng.NextDouble() - 0.5) * 0.6f + (attempt / 4) * 0.5f;
                    float r = 14f + (float)ctx.Rng.NextDouble() * 7f;
                    Vector3 pos = well + Dir(angle) * r;
                    if (RoadPlanner.PlanarDistance(pos, c) > ctx.Settlement.Radius - 4f) continue;
                    if (ctx.TryPlace(PropRole.Cabin, pos, ScatterPlanner.Yaw(well - pos), CabinFootprint, ctx.HalfRoad + 1.5f)) break;
                }
            }

            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = baseAngle + (float)(ctx.Rng.NextDouble() - 0.5) * Mathf.PI;
                float r = ctx.Settlement.Radius + 12f + (float)ctx.Rng.NextDouble() * 20f;
                if (ctx.TryPlace(PropRole.Windmill, c + Dir(angle) * r, (float)(ctx.Rng.NextDouble() * 360.0), 7f, ctx.HalfRoad + 4f)) break;
            }

            PlaceNear(ctx, PropRole.Cart, well, gap, 7f, 1.5f, (float)(ctx.Rng.NextDouble() * 360.0), out _);
            ScatterGoods(ctx, well, 2 + ctx.Rng.Next(2), 5f);
            DecorateCabins(ctx, 0.5);
            if (ctx.Rng.NextDouble() < 0.2) PlaceNear(ctx, PropRole.Kite, c, gap, ctx.Settlement.Radius * 0.6f, 1f, (float)(ctx.Rng.NextDouble() * 360.0), out _);
        }

        private static void DressTown(Context ctx)
        {
            Vector3 c = ctx.Center;
            float radius = ctx.Settlement.Radius;
            Vector3 gap = ctx.LargestGap();
            if (!PlaceNear(ctx, PropRole.Well, c, gap, 10f, 1.8f, 0f, out Vector3 well)) well = c;

            // Stone arches where the first two roads enter the town.
            RoadNode node = ctx.Settlement.Node;
            for (int i = 0; i < node.Segments.Count && i < 2; i++)
            {
                RoadSegment segment = node.Segments[i];
                float d = Mathf.Min(radius - 6f, segment.Length * 0.45f);
                bool fromA = segment.A == node;
                Vector3 pos = segment.Evaluate(fromA ? d : segment.Length - d);
                Vector3 tangent = segment.Tangent(fromA ? d : segment.Length - d);
                ctx.ForcePlace(PropRole.StoneArch, pos, ScatterPlanner.Yaw(tangent), 3f);
            }

            int cabins = 6 + ctx.Rng.Next(4);
            int placed = 0;
            for (int attempt = 0; attempt < cabins * 12 && placed < cabins; attempt++)
            {
                float angle = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
                float r = 20f + (float)ctx.Rng.NextDouble() * (radius - 30f);
                Vector3 pos = c + Dir(angle) * r;
                if (ctx.TryPlace(PropRole.Cabin, pos, ScatterPlanner.Yaw(c - pos), CabinFootprint, ctx.HalfRoad + 1.5f)) placed++;
            }

            // Stone wall along the perimeter arc facing away from the roads, with pillars at the run ends, the odd
            // short piece for variety, and a small gate in the middle of the arc.
            if (ctx.Keys.Has(PropRole.StoneWall))
            {
                float wallRadius = radius * 0.93f;
                float length = Mathf.Max(0.5f, ctx.Keys.wallLength);
                float step = length / wallRadius;
                float center = Mathf.Atan2(gap.z, gap.x);
                float span = Mathf.PI * (0.35f + (float)ctx.Rng.NextDouble() * 0.2f);
                int pieces = Mathf.Max(1, Mathf.FloorToInt(2f * span / step));
                int gateIndex = ctx.Keys.Has(PropRole.Gate) ? pieces / 2 : -1;
                bool inRun = false;
                Vector3 lastEnd = Vector3.zero;
                float lastYaw = 0f;
                for (int k = 0; k < pieces; k++)
                {
                    float a = center - span + (k + 0.5f) * step;
                    Vector3 pos = c + Dir(a) * wallRadius;
                    Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    float yaw = ScatterPlanner.Yaw(tangent);
                    string piece = k == gateIndex ? PropRole.Gate
                        : ctx.Keys.Has(PropRole.StoneWallSmall) && ctx.Rng.NextDouble() < 0.15 ? PropRole.StoneWallSmall : PropRole.StoneWall;
                    bool ok = ctx.TryPlace(piece, pos, yaw, length * 0.45f, ctx.HalfRoad + 2f + length * 0.5f);
                    if (ok && (!inRun || k == gateIndex || k == gateIndex + 1)) ctx.ForcePlaceNoFootprint(PropRole.StoneWallPillar, pos - tangent * (length * 0.5f), yaw);
                    if (!ok && inRun) ctx.ForcePlaceNoFootprint(PropRole.StoneWallEnd, lastEnd, lastYaw);
                    if (ok)
                    {
                        lastEnd = pos + tangent * (length * 0.5f);
                        lastYaw = yaw;
                    }

                    inRun = ok;
                }

                if (inRun) ctx.ForcePlaceNoFootprint(PropRole.StoneWallPillar, lastEnd, lastYaw);
            }

            PlaceNear(ctx, PropRole.Cart, well, gap, 8f, 1.5f, (float)(ctx.Rng.NextDouble() * 360.0), out _);
            PlaceNear(ctx, PropRole.Crate, well, -gap, 6f, 0.8f, (float)(ctx.Rng.NextDouble() * 360.0), out _);
            ScatterGoods(ctx, well, 3 + ctx.Rng.Next(3), 6f);
            DecorateCabins(ctx, 0.35);
            if (PlaceNear(ctx, PropRole.Table, well, -gap, 9f, 1.2f, (float)(ctx.Rng.NextDouble() * 360.0), out Vector3 tablePos))
            {
                ctx.ForcePlaceNoFootprint(PropRole.Chair, tablePos + Vector3.right * 1.1f, -90f);
                ctx.ForcePlaceNoFootprint(PropRole.Chair, tablePos - Vector3.right * 1.1f, 90f);
            }

            if (ctx.Rng.NextDouble() < 0.3) PlaceNear(ctx, PropRole.Kite, c, gap, radius * 0.5f, 1f, (float)(ctx.Rng.NextDouble() * 360.0), out _);
        }

        /// <summary>A little cluster of barrels, crates, sacks and pots around <paramref name="origin"/>.</summary>
        private static void ScatterGoods(Context ctx, Vector3 origin, int count, float radius)
        {
            for (int k = 0, attempt = 0; k < count && attempt < count * 6; attempt++)
            {
                float a = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
                float r = radius * (0.5f + 0.5f * (float)ctx.Rng.NextDouble());
                if (ctx.TryPlace(PropRole.Goods, origin + Dir(a) * r, (float)(ctx.Rng.NextDouble() * 360.0), 0.6f, ctx.HalfRoad + 1.5f)) k++;
            }
        }

        /// <summary>Birdhouses and wind chimes beside some of the settlement's cabins.</summary>
        private static void DecorateCabins(Context ctx, double chance)
        {
            foreach (Vector3 cabin in ctx.Cabins.ToArray())
            {
                if (ctx.Rng.NextDouble() > chance) continue;
                string family = ctx.Rng.Next(2) == 0 ? PropRole.Birdhouse : PropRole.WindChime;
                float a = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
                ctx.TryPlace(family, cabin + Dir(a) * 5.5f, (float)(ctx.Rng.NextDouble() * 360.0), 0.5f, ctx.HalfRoad + 1.5f);
            }
        }

        private static bool PlaceNear(Context ctx, string role, Vector3 origin, Vector3 direction, float distance, float footprint, float yaw, out Vector3 placed)
        {
            float baseAngle = Mathf.Atan2(direction.z, direction.x);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = baseAngle + (attempt == 0 ? 0f : (float)(ctx.Rng.NextDouble() - 0.5) * 2.4f);
                float r = distance * (attempt == 0 ? 1f : 0.8f + (float)ctx.Rng.NextDouble() * 0.6f);
                Vector3 pos = origin + Dir(angle) * r;
                if (ctx.TryPlace(role, pos, yaw, footprint, ctx.HalfRoad + 1.5f))
                {
                    placed = pos;
                    return true;
                }
            }

            placed = origin;
            return false;
        }

        private static Vector3 Dir(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

        private sealed class Context
        {
            private readonly WorldGenParams p;
            private readonly float[,] map;
            private readonly RoadDistanceField roads;
            private readonly OccupancyMap occupancy;
            private readonly List<PropPlacement> props;

            public Context(WorldGenParams p, float[,] map, RoadDistanceField roads, PaletteKeys keys, OccupancyMap occupancy,
                List<PropPlacement> props, Settlement settlement)
            {
                this.p = p;
                this.map = map;
                this.roads = roads;
                this.occupancy = occupancy;
                this.props = props;
                Keys = keys;
                Settlement = settlement;
                Center = settlement.Position;
                HalfRoad = p.roadWidth * 0.5f;
                Rng = new System.Random(p.seed + 6 + 7919 * (settlement.Id + 1));
                RoadDirections = new List<Vector3>();
                foreach (RoadSegment segment in settlement.Node.Segments)
                {
                    float d = Mathf.Min(20f, segment.Length);
                    Vector3 point = segment.A == settlement.Node ? segment.Evaluate(d) : segment.Evaluate(segment.Length - d);
                    Vector3 dir = point - Center;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-4f) RoadDirections.Add(dir.normalized);
                }
            }

            public PaletteKeys Keys { get; }
            public Settlement Settlement { get; }
            public Vector3 Center { get; }
            public float HalfRoad { get; }
            public System.Random Rng { get; }
            public List<Vector3> RoadDirections { get; }
            public List<Vector3> Cabins { get; } = new List<Vector3>();

            /// <summary>Bisector of the widest angular gap between roads (or a seeded direction without roads).</summary>
            public Vector3 LargestGap()
            {
                if (RoadDirections.Count == 0)
                {
                    float a = (float)(Rng.NextDouble() * Mathf.PI * 2.0);
                    return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                }

                if (RoadDirections.Count == 1) return -RoadDirections[0];

                var angles = new List<float>(RoadDirections.Count);
                foreach (Vector3 d in RoadDirections) angles.Add(Mathf.Atan2(d.z, d.x));
                angles.Sort();
                float bestGap = -1f;
                float bestMid = 0f;
                for (int i = 0; i < angles.Count; i++)
                {
                    float from = angles[i];
                    float to = i + 1 < angles.Count ? angles[i + 1] : angles[0] + Mathf.PI * 2f;
                    if (to - from > bestGap)
                    {
                        bestGap = to - from;
                        bestMid = (from + to) * 0.5f;
                    }
                }

                return new Vector3(Mathf.Cos(bestMid), 0f, Mathf.Sin(bestMid));
            }

            /// <summary>Places if clear of roads (centre-line distance ≥ roadClearance + footprint) and other props.</summary>
            public bool TryPlace(string role, Vector3 pos, float yaw, float footprint, float roadClearance)
            {
                if (!Keys.Has(role)) return false;
                if (pos.x < footprint || pos.z < footprint || pos.x > p.worldSize - footprint || pos.z > p.worldSize - footprint) return false;
                if (!roads.IsClear(pos.x, pos.z, roadClearance + footprint)) return false;
                if (occupancy.Overlaps(pos.x, pos.z, footprint)) return false;
                ForcePlace(role, pos, yaw, footprint);
                return true;
            }

            /// <summary><see cref="TryPlace"/> with an explicit variant (e.g. logs out of the stump family).</summary>
            public bool TryPlaceVariant(string role, int variant, Vector3 pos, float yaw, float footprint, float roadClearance)
            {
                if (!Keys.Has(role) || variant >= Keys.Count(role)) return TryPlace(role, pos, yaw, footprint, roadClearance);
                if (!Clear(pos, footprint, roadClearance)) return false;
                Add(role, variant, pos, yaw, 1f, footprint);
                return true;
            }

            /// <summary><see cref="TryPlace"/> with a scale.</summary>
            public bool TryPlaceScaled(string role, Vector3 pos, float yaw, float scale, float footprint, float roadClearance)
            {
                if (!Keys.Has(role) || !Clear(pos, footprint, roadClearance)) return false;
                Add(role, Keys.PickVariant(role, Rng), pos, yaw, scale, footprint);
                return true;
            }

            private bool Clear(Vector3 pos, float footprint, float roadClearance)
            {
                if (pos.x < footprint || pos.z < footprint || pos.x > p.worldSize - footprint || pos.z > p.worldSize - footprint) return false;
                return roads.IsClear(pos.x, pos.z, roadClearance + footprint) && !occupancy.Overlaps(pos.x, pos.z, footprint);
            }

            private void Add(string role, int variant, Vector3 pos, float yaw, float scale, float footprint)
            {
                pos.y = HeightmapGenerator.SampleHeight(map, p, pos.x, pos.z);
                occupancy.Add(pos, footprint);
                if (role == PropRole.StoneStack || role == PropRole.StonePile || PropRole.IsRock(role))
                {
                    // Measured stone pieces are seated on the ground under their whole footprint (no gap at a disc edge).
                    if (Keys.TryGetMetrics(role, variant, out _))
                    {
                        if (!RockPlacer.TrySeat(new RockPlacer.Context(p, map, Keys, null), role, variant, pos.x, pos.z, yaw, scale, out PropPlacement seated)) return;
                        seated.settlementId = Settlement.Id;
                        props.Add(seated);
                        return;
                    }
                }

                props.Add(new PropPlacement { role = role, variant = variant, position = pos, yaw = yaw, scale = scale, settlementId = Settlement.Id });
            }

            /// <summary>Places without clearance checks or a footprint (pieces that sit on/against another prop).</summary>
            public void ForcePlaceNoFootprint(string role, Vector3 pos, float yaw)
            {
                if (!Keys.Has(role)) return;
                pos.y = HeightmapGenerator.SampleHeight(map, p, pos.x, pos.z);
                props.Add(new PropPlacement
                {
                    role = role,
                    variant = Keys.PickVariant(role, Rng),
                    position = pos,
                    yaw = yaw,
                    scale = 1f,
                    settlementId = Settlement.Id,
                });
            }

            /// <summary>Places without clearance checks (arches over the road).</summary>
            public void ForcePlace(string role, Vector3 pos, float yaw, float footprint)
            {
                if (!Keys.Has(role)) return;
                pos.y = HeightmapGenerator.SampleHeight(map, p, pos.x, pos.z);
                occupancy.Add(pos, footprint);
                if (role == PropRole.Cabin) Cabins.Add(pos);
                props.Add(new PropPlacement
                {
                    role = role,
                    variant = Keys.PickVariant(role, Rng),
                    position = pos,
                    yaw = yaw,
                    scale = 1f,
                    settlementId = Settlement.Id,
                });
            }
        }
    }
}
