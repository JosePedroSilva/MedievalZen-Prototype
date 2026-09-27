using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// A road between two nodes, stored as a polyline from <see cref="A"/> to <see cref="B"/>.
    /// All distances are arc lengths measured from <see cref="A"/>.
    /// Created only through <see cref="RoadNetworkBuilder"/>.
    /// </summary>
    public sealed class RoadSegment
    {
        internal RoadSegment(int id, RoadNode a, RoadNode b, Polyline polyline)
        {
            Id = id;
            A = a;
            B = b;
            Polyline = polyline;
        }

        /// <summary>Index of this segment in <see cref="RoadNetwork.Segments"/>.</summary>
        public int Id { get; }

        /// <summary>Start node (distance 0).</summary>
        public RoadNode A { get; }

        /// <summary>End node (distance <see cref="Length"/>).</summary>
        public RoadNode B { get; }

        /// <summary>Underlying polyline with its arc-length table.</summary>
        public Polyline Polyline { get; }

        /// <summary>Arc length of the road in metres.</summary>
        public float Length => Polyline.Length;

        /// <summary>Resampled polyline vertices from A to B (at most ~2 m apart).</summary>
        public IReadOnlyList<Vector3> Points => Polyline.Points;

        /// <summary>Position at <paramref name="distanceFromA"/> metres along the road (clamped).</summary>
        public Vector3 Evaluate(float distanceFromA) => Polyline.Evaluate(distanceFromA);

        /// <summary>Unit direction A→B at <paramref name="distanceFromA"/> metres along the road (clamped).</summary>
        public Vector3 Tangent(float distanceFromA) => Polyline.Tangent(distanceFromA);

        /// <summary>Closest point on this road to <paramref name="position"/>.</summary>
        public Vector3 ClosestPoint(Vector3 position, out float distanceFromA) => Polyline.ClosestPoint(position, out distanceFromA);

        /// <summary>The node at the other end from <paramref name="node"/>.</summary>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not an end of this segment.</exception>
        public RoadNode GetOther(RoadNode node)
        {
            if (node == A) return B;
            if (node == B) return A;
            throw new ArgumentException($"{node} is not an end of segment #{Id}.", nameof(node));
        }

        /// <inheritdoc />
        public override string ToString() => $"Segment #{Id} ({A.Id}->{B.Id}, {Length:0.0} m)";
    }
}
