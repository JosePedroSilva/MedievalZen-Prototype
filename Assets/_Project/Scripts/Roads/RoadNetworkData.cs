using System;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// Plain serialisable snapshot of a <see cref="RoadNetwork"/> so it can live on a MonoBehaviour or
    /// asset and be rebuilt at runtime. Ids are array indices and survive a round trip.
    /// </summary>
    [Serializable]
    public sealed class RoadNetworkData
    {
        /// <summary>Settlements in id order.</summary>
        public SettlementData[] settlements = Array.Empty<SettlementData>();

        /// <summary>Nodes in id order.</summary>
        public RoadNodeData[] nodes = Array.Empty<RoadNodeData>();

        /// <summary>Segments in id order.</summary>
        public RoadSegmentData[] segments = Array.Empty<RoadSegmentData>();

        /// <summary>True when there is nothing to build.</summary>
        public bool IsEmpty => (nodes == null || nodes.Length == 0) && (segments == null || segments.Length == 0);

        /// <summary>Captures <paramref name="network"/> as plain data.</summary>
        public static RoadNetworkData From(RoadNetwork network)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));

            var data = new RoadNetworkData
            {
                settlements = new SettlementData[network.Settlements.Count],
                nodes = new RoadNodeData[network.Nodes.Count],
                segments = new RoadSegmentData[network.Segments.Count],
            };

            for (int i = 0; i < data.nodes.Length; i++)
            {
                data.nodes[i] = new RoadNodeData { position = network.Nodes[i].Position };
            }

            for (int i = 0; i < data.settlements.Length; i++)
            {
                Settlement settlement = network.Settlements[i];
                data.settlements[i] = new SettlementData
                {
                    name = settlement.Name,
                    type = settlement.Type,
                    node = settlement.Node.Id,
                    radius = settlement.Radius,
                };
            }

            for (int i = 0; i < data.segments.Length; i++)
            {
                RoadSegment segment = network.Segments[i];
                var points = new Vector3[segment.Points.Count];
                for (int p = 0; p < points.Length; p++) points[p] = segment.Points[p];
                data.segments[i] = new RoadSegmentData { nodeA = segment.A.Id, nodeB = segment.B.Id, points = points };
            }

            return data;
        }

        /// <summary>Rebuilds the network. Points are used as stored (no further densification).</summary>
        /// <exception cref="InvalidOperationException">The data references a node index that does not exist.</exception>
        public RoadNetwork ToNetwork()
        {
            var builder = new RoadNetworkBuilder { MaxPointSpacing = 0f, EndpointTolerance = float.PositiveInfinity };
            RoadNodeData[] nodeData = nodes ?? Array.Empty<RoadNodeData>();
            var built = new RoadNode[nodeData.Length];
            for (int i = 0; i < nodeData.Length; i++)
            {
                built[i] = builder.AddNode(nodeData[i].position);
            }

            foreach (SettlementData settlement in settlements ?? Array.Empty<SettlementData>())
            {
                builder.AddSettlement(settlement.name, settlement.type, NodeAt(built, settlement.node), settlement.radius);
            }

            foreach (RoadSegmentData segment in segments ?? Array.Empty<RoadSegmentData>())
            {
                RoadNode a = NodeAt(built, segment.nodeA);
                RoadNode b = NodeAt(built, segment.nodeB);
                Vector3[] points = segment.points != null && segment.points.Length >= 2
                    ? segment.points
                    : new[] { a.Position, b.Position };
                builder.AddSegment(a, b, points);
            }

            return builder.Build();
        }

        private static RoadNode NodeAt(RoadNode[] built, int index)
        {
            if (index < 0 || index >= built.Length)
            {
                throw new InvalidOperationException($"Road data references node {index}, but only {built.Length} nodes exist.");
            }

            return built[index];
        }
    }
}
