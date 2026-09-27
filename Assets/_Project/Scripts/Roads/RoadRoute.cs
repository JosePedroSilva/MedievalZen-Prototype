using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// A path along the road network from a <see cref="RoadLocation"/> to a <see cref="Settlement"/>,
    /// parameterised by distance travelled (0 … <see cref="TotalLength"/>).
    /// The first leg is always on the start location's segment and may be partial (it begins at
    /// <see cref="StartDistanceOnFirstLeg"/>); every later leg is a full segment.
    /// Obtain one from <see cref="RoadNetwork.TryFindRoute"/>.
    /// </summary>
    public sealed class RoadRoute
    {
        private readonly (RoadSegment segment, bool forward)[] legs;
        private readonly float[] legStart;   // route distance at which each leg begins
        private readonly float[] legLength;  // distance covered on each leg

        internal RoadRoute(Settlement destination, IReadOnlyList<(RoadSegment segment, bool forward)> legs, float startDistanceOnFirstLeg)
        {
            if (legs == null || legs.Count == 0) throw new ArgumentException("A route needs at least one leg.", nameof(legs));

            Destination = destination;
            this.legs = new (RoadSegment segment, bool forward)[legs.Count];
            legStart = new float[legs.Count];
            legLength = new float[legs.Count];

            RoadSegment first = legs[0].segment;
            StartDistanceOnFirstLeg = Mathf.Clamp(startDistanceOnFirstLeg, 0f, first.Length);

            float total = 0f;
            for (int i = 0; i < legs.Count; i++)
            {
                this.legs[i] = legs[i];
                float length = legs[i].segment.Length;
                if (i == 0)
                {
                    length = legs[0].forward ? first.Length - StartDistanceOnFirstLeg : StartDistanceOnFirstLeg;
                }

                legStart[i] = total;
                legLength[i] = length;
                total += length;
            }

            TotalLength = total;
        }

        /// <summary>
        /// The segments to travel, in order. <c>forward</c> = travel from the segment's A to B.
        /// </summary>
        public IReadOnlyList<(RoadSegment segment, bool forward)> Legs => legs;

        /// <summary>Total distance from the start location to the destination, in metres.</summary>
        public float TotalLength { get; }

        /// <summary>The settlement this route ends at.</summary>
        public Settlement Destination { get; }

        /// <summary>Where the route starts on the first leg's segment, as arc length from that segment's node A.</summary>
        public float StartDistanceOnFirstLeg { get; }

        /// <summary>World position of the route start.</summary>
        public Vector3 StartPosition => Evaluate(0f);

        /// <summary>Position after travelling <paramref name="distance"/> metres (clamped to [0, TotalLength]).</summary>
        public Vector3 Evaluate(float distance)
        {
            RoadSegment segment = Locate(distance, out float segmentDistance, out _);
            return segment.Evaluate(segmentDistance);
        }

        /// <summary>Unit direction of travel after <paramref name="distance"/> metres (clamped to [0, TotalLength]).</summary>
        public Vector3 Tangent(float distance)
        {
            RoadSegment segment = Locate(distance, out float segmentDistance, out bool forward);
            Vector3 tangent = segment.Tangent(segmentDistance);
            return forward ? tangent : -tangent;
        }

        /// <summary>Converts a route distance into a location on the network.</summary>
        public RoadLocation GetLocation(float distance)
        {
            RoadSegment segment = Locate(distance, out float segmentDistance, out _);
            return new RoadLocation(segment, segmentDistance);
        }

        /// <summary>Index into <see cref="Legs"/> of the leg containing <paramref name="distance"/>.</summary>
        public int LegIndexAt(float distance)
        {
            distance = Mathf.Clamp(distance, 0f, TotalLength);

            // Largest i with legStart[i] <= distance, skipping zero-length legs at the boundary.
            int lo = 0;
            int hi = legs.Length;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (legStart[mid] <= distance) lo = mid;
                else hi = mid;
            }

            return lo;
        }

        private RoadSegment Locate(float distance, out float segmentDistance, out bool forward)
        {
            distance = Mathf.Clamp(distance, 0f, TotalLength);
            int index = LegIndexAt(distance);
            (RoadSegment segment, bool isForward) = legs[index];
            float local = Mathf.Min(distance - legStart[index], legLength[index]);
            float entry = index == 0 ? StartDistanceOnFirstLeg : (isForward ? 0f : segment.Length);
            segmentDistance = isForward ? entry + local : entry - local;
            forward = isForward;
            return segment;
        }
    }
}
