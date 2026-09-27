using System;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Progress along a <see cref="RoadRoute"/>: a distance in metres from the route start, clamped to
    /// [0, <see cref="TotalLength"/>]. Pure C#; queries do not allocate.
    /// </summary>
    public sealed class RouteFollower
    {
        private const float FinishEpsilon = 1e-4f;

        private readonly float[] legRouteStart;   // route distance at which each leg begins

        /// <summary>Starts following <paramref name="route"/> at <paramref name="startDistance"/> metres (default: the start).</summary>
        public RouteFollower(RoadRoute route, float startDistance = 0f)
        {
            Route = route ?? throw new ArgumentNullException(nameof(route));

            var legs = route.Legs;
            legRouteStart = new float[legs.Count];
            float total = 0f;
            for (int i = 0; i < legs.Count; i++)
            {
                legRouteStart[i] = total;
                total += CoveredLength(i);
            }

            Distance = Mathf.Clamp(startDistance, 0f, route.TotalLength);
        }

        /// <summary>The route being followed.</summary>
        public RoadRoute Route { get; }

        /// <summary>Distance travelled along the route, in metres.</summary>
        public float Distance { get; private set; }

        /// <summary>Length of the route, in metres.</summary>
        public float TotalLength => Route.TotalLength;

        /// <summary>Metres left to the destination.</summary>
        public float Remaining => Mathf.Max(0f, Route.TotalLength - Distance);

        /// <summary>True once <see cref="Distance"/> has reached the end of the route.</summary>
        public bool IsFinished => Distance >= Route.TotalLength - FinishEpsilon;

        /// <summary>World position at <see cref="Distance"/>.</summary>
        public Vector3 Position => Route.Evaluate(Distance);

        /// <summary>Unit direction of travel at <see cref="Distance"/>.</summary>
        public Vector3 Tangent => Route.Tangent(Distance);

        /// <summary>
        /// Moves <paramref name="metres"/> along the route (negative moves back), clamped to the route.
        /// Returns the distance actually moved.
        /// </summary>
        public float Advance(float metres)
        {
            float previous = Distance;
            Distance = Mathf.Clamp(Distance + metres, 0f, Route.TotalLength);
            return Distance - previous;
        }

        /// <summary>Sets <see cref="Distance"/> directly (clamped to the route).</summary>
        public void SetDistance(float distance)
        {
            Distance = Mathf.Clamp(distance, 0f, Route.TotalLength);
        }

        /// <summary>Position <paramref name="metres"/> ahead of <see cref="Distance"/> (clamped to the route end).</summary>
        public Vector3 LookAhead(float metres)
        {
            return Route.Evaluate(Mathf.Min(Distance + Mathf.Max(0f, metres), Route.TotalLength));
        }

        /// <summary>
        /// Resynchronises <see cref="Distance"/> to the point of the route closest to <paramref name="worldPosition"/>
        /// (e.g. after the traveller was pushed off the road). Returns the straight-line distance from
        /// <paramref name="worldPosition"/> to that point.
        /// </summary>
        public float Reproject(Vector3 worldPosition)
        {
            Distance = ClosestDistance(worldPosition, out float separation);
            return separation;
        }

        /// <summary>
        /// Route distance of the point on the route closest to <paramref name="worldPosition"/>, without
        /// changing <see cref="Distance"/>. <paramref name="separation"/> is the straight-line gap.
        /// </summary>
        public float ClosestDistance(Vector3 worldPosition, out float separation)
        {
            var legs = Route.Legs;
            float bestDistance = 0f;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < legs.Count; i++)
            {
                (RoadSegment segment, bool forward) = legs[i];
                segment.ClosestPoint(worldPosition, out float onSegment);

                // Restrict to the part of the segment this leg actually covers (matters for the partial first leg).
                float entry = EntryOnSegment(i);
                float exit = forward ? entry + CoveredLength(i) : entry - CoveredLength(i);
                onSegment = Mathf.Clamp(onSegment, Mathf.Min(entry, exit), Mathf.Max(entry, exit));

                float sqr = (segment.Evaluate(onSegment) - worldPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestDistance = legRouteStart[i] + Mathf.Abs(onSegment - entry);
                }
            }

            separation = Mathf.Sqrt(bestSqr);
            return Mathf.Clamp(bestDistance, 0f, Route.TotalLength);
        }

        private float EntryOnSegment(int legIndex)
        {
            (RoadSegment segment, bool forward) = Route.Legs[legIndex];
            if (legIndex == 0) return Route.StartDistanceOnFirstLeg;
            return forward ? 0f : segment.Length;
        }

        private float CoveredLength(int legIndex)
        {
            (RoadSegment segment, bool forward) = Route.Legs[legIndex];
            if (legIndex > 0) return segment.Length;
            return forward ? segment.Length - Route.StartDistanceOnFirstLeg : Route.StartDistanceOnFirstLeg;
        }
    }
}
