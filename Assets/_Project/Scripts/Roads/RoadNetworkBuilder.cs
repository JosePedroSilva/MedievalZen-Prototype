using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// The only way to construct a <see cref="RoadNetwork"/>. Add settlements and nodes, connect them
    /// with polyline segments, then call <see cref="Build"/> once.
    /// </summary>
    public sealed class RoadNetworkBuilder
    {
        private readonly List<Settlement> settlements = new List<Settlement>();
        private readonly List<RoadNode> nodes = new List<RoadNode>();
        private readonly List<RoadSegment> segments = new List<RoadSegment>();
        private bool built;

        /// <summary>
        /// Maximum distance (metres) between a segment's first/last point and its node. Points within
        /// this distance are snapped onto the node; further away throws. Default 5 m.
        /// </summary>
        public float EndpointTolerance { get; set; } = 5f;

        /// <summary>
        /// Segment polylines are densified so no two consecutive points are further apart than this
        /// (existing points are kept). Zero or less disables densification. Default 2 m.
        /// </summary>
        public float MaxPointSpacing { get; set; } = 2f;

        /// <summary>Adds a settlement together with the road node at its centre.</summary>
        public Settlement AddSettlement(string name, SettlementType type, Vector3 position, float radius)
        {
            return AddSettlement(name, type, AddNode(position), radius);
        }

        /// <summary>Adds a settlement on an existing node (which must not already have one).</summary>
        public Settlement AddSettlement(string name, SettlementType type, RoadNode node, float radius)
        {
            EnsureNotBuilt();
            EnsureOwned(node, nameof(node));
            if (node.Settlement != null) throw new ArgumentException($"{node} already has settlement {node.Settlement}.", nameof(node));
            if (radius < 0f) throw new ArgumentOutOfRangeException(nameof(radius), "Radius must not be negative.");

            var settlement = new Settlement(settlements.Count, name, type, node, radius);
            node.Settlement = settlement;
            settlements.Add(settlement);
            return settlement;
        }

        /// <summary>Adds a plain node (junction or road end).</summary>
        public RoadNode AddNode(Vector3 position)
        {
            EnsureNotBuilt();
            var node = new RoadNode(nodes.Count, position);
            nodes.Add(node);
            return node;
        }

        /// <summary>
        /// Connects <paramref name="a"/> to <paramref name="b"/> along <paramref name="points"/>, which must
        /// start within <see cref="EndpointTolerance"/> of <paramref name="a"/> and end within it of
        /// <paramref name="b"/>. The end points are snapped onto the nodes.
        /// </summary>
        public RoadSegment AddSegment(RoadNode a, RoadNode b, IReadOnlyList<Vector3> points)
        {
            EnsureNotBuilt();
            EnsureOwned(a, nameof(a));
            EnsureOwned(b, nameof(b));
            if (a == b) throw new ArgumentException("A segment cannot start and end at the same node.", nameof(b));
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (points.Count < 2) throw new ArgumentException("A segment needs at least two points.", nameof(points));

            float tolerance = Mathf.Max(0f, EndpointTolerance);
            if (Vector3.Distance(points[0], a.Position) > tolerance)
            {
                throw new ArgumentException($"First point {points[0]} is more than {tolerance} m from {a} at {a.Position}.", nameof(points));
            }

            if (Vector3.Distance(points[points.Count - 1], b.Position) > tolerance)
            {
                throw new ArgumentException($"Last point {points[points.Count - 1]} is more than {tolerance} m from {b} at {b.Position}.", nameof(points));
            }

            var copy = new Vector3[points.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = points[i];
            copy[0] = a.Position;
            copy[copy.Length - 1] = b.Position;

            var polyline = new Polyline(copy);
            if (MaxPointSpacing > 0f) polyline = polyline.Densify(MaxPointSpacing);

            var segment = new RoadSegment(segments.Count, a, b, polyline);
            a.AddSegment(segment);
            b.AddSegment(segment);
            segments.Add(segment);
            return segment;
        }

        /// <summary>Convenience: connects two nodes with a straight road.</summary>
        public RoadSegment AddStraightSegment(RoadNode a, RoadNode b)
        {
            return AddSegment(a, b, new[] { a.Position, b.Position });
        }

        /// <summary>Finalises the network. The builder cannot be used afterwards.</summary>
        public RoadNetwork Build()
        {
            EnsureNotBuilt();
            built = true;
            return new RoadNetwork(settlements.ToArray(), nodes.ToArray(), segments.ToArray());
        }

        private void EnsureNotBuilt()
        {
            if (built) throw new InvalidOperationException("Build() has already been called on this builder.");
        }

        private void EnsureOwned(RoadNode node, string paramName)
        {
            if (node == null) throw new ArgumentNullException(paramName);
            if (node.Id < 0 || node.Id >= nodes.Count || nodes[node.Id] != node)
            {
                throw new ArgumentException($"{node} was not created by this builder.", paramName);
            }
        }
    }
}
