using System;
using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// What the fast-travel planner knows about the ground, as plain values: a height function (world x, z → ground height,
    /// or NaN when unknown; null = trust the road polyline heights), the lake level and the steepest slope a traveller may be
    /// set down on.
    /// </summary>
    public sealed class FastTravelGround
    {
        /// <summary>Ground height at (x, z), NaN when unknown. Null: every spot takes the road's own height.</summary>
        public Func<float, float, float> HeightAt;

        /// <summary>Lake level in metres; negative when the world has no lakes.</summary>
        public float WaterHeight = -1f;

        /// <summary>A spot must be at least this far above the lake level.</summary>
        public float WaterClearance = 0.3f;

        /// <summary>Steepest ground allowed, as rise over run (0.7 ≈ 35°).</summary>
        public float MaxSlope = 0.7f;

        /// <summary>Half-width of the finite differences that measure the slope, in metres.</summary>
        public float SlopeProbe = 1f;
    }

    /// <summary>Where fast travel sets the player down: feet position and body yaw (degrees, 0 = +Z).</summary>
    public readonly struct FastTravelSpot
    {
        /// <summary>Creates a spot.</summary>
        public FastTravelSpot(Vector3 position, float yaw, RoadLocation road)
        {
            Position = position;
            Yaw = yaw;
            Road = road;
        }

        /// <summary>Feet position (ground height at the road point).</summary>
        public Vector3 Position { get; }

        /// <summary>Body and camera yaw in degrees.</summary>
        public float Yaw { get; }

        /// <summary>The road point the spot lies on.</summary>
        public RoadLocation Road { get; }
    }

    /// <summary>
    /// Pure placement rules for "Travel there now":
    /// <list type="bullet">
    /// <item><b>Settlement</b>: on one of its own roads, <see cref="SettlementStandOff"/> metres past its radius (at most half
    /// the road), facing along the road towards the centre — the view of arriving on foot. Roads are tried longest first
    /// (the main road), and on each a few distances; the first spot on dry, gentle ground wins. With no usable road of its
    /// own, the rule for a position below is used on the centre.</item>
    /// <item><b>Position / landmark</b>: the nearest point of the nearest road; if that spot is under water or steep, the
    /// search walks along that road in <see cref="SearchStep"/> m steps both ways (nearest first, up to
    /// <see cref="SearchRange"/> m), then tries the other roads in order of distance. The traveller faces the target
    /// (a peak, a lake) when it is more than <see cref="FaceTargetMinDistance"/> m away, otherwise along the road.</item>
    /// </list>
    /// A spot is walkable when its ground is <see cref="FastTravelGround.WaterClearance"/> above the lake level and its slope
    /// (central differences) is at most <see cref="FastTravelGround.MaxSlope"/>. Yaw is in degrees, 0 = +Z, 90 = +X.
    /// </summary>
    public static class FastTravelPlanner
    {
        /// <summary>Metres beyond a settlement's radius at which the traveller is set down.</summary>
        public const float SettlementStandOff = 6f;

        /// <summary>Step of the search along a road, in metres.</summary>
        public const float SearchStep = 4f;

        /// <summary>How far along one road the search goes each way, in metres.</summary>
        public const float SearchRange = 240f;

        /// <summary>The traveller faces the target only when it is further than this.</summary>
        public const float FaceTargetMinDistance = 5f;

        private const int MaxOtherRoads = 8;

        /// <summary>Finds the arrival spot for <paramref name="settlement"/> (see the class summary).</summary>
        public static bool TryPlanSettlement(RoadNetwork roads, Settlement settlement, FastTravelGround ground, out FastTravelSpot spot)
        {
            spot = default;
            if (roads == null || settlement == null || settlement.Node == null) return false;

            var candidates = new List<RoadSegment>(settlement.Node.Segments);
            candidates.Sort((a, b) => a.Length != b.Length ? b.Length.CompareTo(a.Length) : a.Id.CompareTo(b.Id));
            float baseOffset = settlement.Radius + SettlementStandOff;
            float[] offsets = { baseOffset, baseOffset - 3f, baseOffset + 4f, baseOffset + 10f, Mathf.Max(3f, settlement.Radius * 0.5f) };

            foreach (RoadSegment road in candidates)
            {
                bool fromA = road.A == settlement.Node;
                float half = road.Length * 0.5f;
                for (int i = 0; i < offsets.Length; i++)
                {
                    float along = Mathf.Clamp(offsets[i], Mathf.Min(2f, half), half);
                    float distance = fromA ? along : road.Length - along;
                    Vector3 onRoad = road.Evaluate(distance);
                    if (!TryGround(onRoad, ground, out Vector3 feet)) continue;
                    Vector3 heading = road.Tangent(distance) * (fromA ? -1f : 1f);   // back towards the centre
                    spot = new FastTravelSpot(feet, YawOf(heading), new RoadLocation(road, distance));
                    return true;
                }
            }

            return TryPlanNear(roads, settlement.Position, ground, out spot);
        }

        /// <summary>Finds the walkable road spot nearest to <paramref name="target"/> (see the class summary).</summary>
        public static bool TryPlanNear(RoadNetwork roads, Vector3 target, FastTravelGround ground, out FastTravelSpot spot)
        {
            spot = default;
            if (roads == null || roads.Segments.Count == 0) return false;

            // Roads in order of distance from the target (the nearest first).
            int count = roads.Segments.Count;
            var order = new List<(float sqr, RoadSegment road, float distance)>(count);
            for (int i = 0; i < count; i++)
            {
                RoadSegment road = roads.Segments[i];
                Vector3 closest = road.ClosestPoint(target, out float distance);
                order.Add(((closest - target).sqrMagnitude, road, distance));
            }

            order.Sort((a, b) => a.sqr != b.sqr ? a.sqr.CompareTo(b.sqr) : a.road.Id.CompareTo(b.road.Id));
            int tries = Mathf.Min(order.Count, MaxOtherRoads + 1);
            for (int r = 0; r < tries; r++)
            {
                (float _, RoadSegment road, float start) = order[r];
                int steps = Mathf.CeilToInt(SearchRange / SearchStep);
                for (int k = 0; k <= steps; k++)
                {
                    for (int sign = 1; sign >= -1; sign -= 2)
                    {
                        if (k == 0 && sign < 0) continue;
                        float distance = start + sign * k * SearchStep;
                        if (distance < 0f || distance > road.Length) continue;
                        Vector3 onRoad = road.Evaluate(distance);
                        if (!TryGround(onRoad, ground, out Vector3 feet)) continue;
                        spot = new FastTravelSpot(feet, FacingFor(road, distance, feet, target), new RoadLocation(road, distance));
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>True when (x, z) of <paramref name="position"/> is dry and gentle enough to stand on.</summary>
        public static bool IsWalkable(Vector3 position, FastTravelGround ground) => TryGround(position, ground, out _);

        /// <summary>Yaw in degrees (0 = +Z, 90 = +X) of a planar direction; 0 for a zero vector.</summary>
        public static float YawOf(Vector3 direction)
        {
            if (direction.x * direction.x + direction.z * direction.z < 1e-10f) return 0f;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static float FacingFor(RoadSegment road, float distance, Vector3 feet, Vector3 target)
        {
            Vector3 toTarget = target - feet;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > FaceTargetMinDistance * FaceTargetMinDistance) return YawOf(toTarget);
            Vector3 tangent = road.Tangent(distance);
            return YawOf(tangent);
        }

        private static bool TryGround(Vector3 onRoad, FastTravelGround ground, out Vector3 feet)
        {
            feet = onRoad;
            if (ground == null) return true;
            float h = Height(ground, onRoad.x, onRoad.z, onRoad.y);
            if (float.IsNaN(h)) return false;
            feet = new Vector3(onRoad.x, h, onRoad.z);
            if (ground.WaterHeight >= 0f && h < ground.WaterHeight + ground.WaterClearance) return false;
            if (ground.HeightAt == null) return true;

            float p = Mathf.Max(0.1f, ground.SlopeProbe);
            float east = Height(ground, onRoad.x + p, onRoad.z, h);
            float west = Height(ground, onRoad.x - p, onRoad.z, h);
            float north = Height(ground, onRoad.x, onRoad.z + p, h);
            float south = Height(ground, onRoad.x, onRoad.z - p, h);
            float gx = (east - west) / (2f * p);
            float gz = (north - south) / (2f * p);
            return Mathf.Sqrt(gx * gx + gz * gz) <= ground.MaxSlope;
        }

        private static float Height(FastTravelGround ground, float x, float z, float fallback)
        {
            if (ground.HeightAt == null) return fallback;
            float h = ground.HeightAt(x, z);
            return float.IsInfinity(h) ? float.NaN : h;
        }
    }
}
