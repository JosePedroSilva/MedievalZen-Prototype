using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>A point on the road network: a segment plus a distance from its node A.</summary>
    public readonly struct RoadLocation
    {
        /// <summary>Creates a location <paramref name="distance"/> metres from <paramref name="segment"/>'s node A.</summary>
        public RoadLocation(RoadSegment segment, float distance)
        {
            Segment = segment;
            Distance = distance;
        }

        /// <summary>The segment, or <c>null</c> for an invalid location (e.g. projecting onto an empty network).</summary>
        public RoadSegment Segment { get; }

        /// <summary>Arc length from <see cref="RoadSegment.A"/>.</summary>
        public float Distance { get; }

        /// <summary>True when <see cref="Segment"/> is set.</summary>
        public bool IsValid => Segment != null;

        /// <summary>World position of this location.</summary>
        public Vector3 Position => Segment.Evaluate(Distance);

        /// <inheritdoc />
        public override string ToString() => IsValid ? $"{Segment} @ {Distance:0.0} m" : "Invalid RoadLocation";
    }
}
