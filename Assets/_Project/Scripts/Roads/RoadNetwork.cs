using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// Immutable road graph: settlements, nodes and polyline segments, with projection and routing.
    /// Build one with <see cref="RoadNetworkBuilder"/> or <see cref="RoadNetworkData.ToNetwork"/>.
    /// Ids of settlements, nodes and segments equal their index in the corresponding list.
    /// </summary>
    public sealed class RoadNetwork
    {
        private readonly Settlement[] settlements;
        private readonly RoadNode[] nodes;
        private readonly RoadSegment[] segments;

        internal RoadNetwork(Settlement[] settlements, RoadNode[] nodes, RoadSegment[] segments)
        {
            this.settlements = settlements;
            this.nodes = nodes;
            this.segments = segments;
        }

        /// <summary>All settlements, indexed by <see cref="Settlement.Id"/>.</summary>
        public IReadOnlyList<Settlement> Settlements => settlements;

        /// <summary>All nodes, indexed by <see cref="RoadNode.Id"/>.</summary>
        public IReadOnlyList<RoadNode> Nodes => nodes;

        /// <summary>All segments, indexed by <see cref="RoadSegment.Id"/>.</summary>
        public IReadOnlyList<RoadSegment> Segments => segments;

        /// <summary>
        /// Nearest point on any road to <paramref name="worldPosition"/> (brute force over all segments).
        /// Returns an invalid location (<see cref="RoadLocation.IsValid"/> false) if there are no segments.
        /// </summary>
        public RoadLocation Project(Vector3 worldPosition)
        {
            RoadSegment best = null;
            float bestDistance = 0f;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < segments.Length; i++)
            {
                Vector3 point = segments[i].ClosestPoint(worldPosition, out float distance);
                float sqr = (point - worldPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = segments[i];
                    bestDistance = distance;
                }
            }

            return best != null ? new RoadLocation(best, bestDistance) : default;
        }

        /// <summary>
        /// Shortest route along the roads from <paramref name="from"/> to settlement <paramref name="to"/>
        /// (Dijkstra over nodes, seeded from both ends of the start segment).
        /// </summary>
        /// <returns>False if <paramref name="from"/> is invalid, <paramref name="to"/> is not part of this
        /// network, or the destination is unreachable.</returns>
        public bool TryFindRoute(RoadLocation from, Settlement to, out RoadRoute route)
        {
            route = null;
            if (!from.IsValid || to == null) return false;
            if (to.Id < 0 || to.Id >= settlements.Length || settlements[to.Id] != to) return false;
            RoadSegment start = from.Segment;
            if (start.Id < 0 || start.Id >= segments.Length || segments[start.Id] != start) return false;

            int count = nodes.Length;
            var cost = new float[count];
            var viaSegment = new int[count];   // segment used to reach the node; -1 = reached from the start leg
            var done = new bool[count];
            for (int i = 0; i < count; i++)
            {
                cost[i] = float.PositiveInfinity;
                viaSegment[i] = -1;
            }

            float s0 = Mathf.Clamp(from.Distance, 0f, start.Length);
            cost[start.A.Id] = s0;
            cost[start.B.Id] = Mathf.Min(cost[start.B.Id], start.Length - s0);

            int target = to.Node.Id;
            while (true)
            {
                // O(V^2) selection is plenty for a few hundred nodes and keeps ordering deterministic.
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (!done[i] && cost[i] < best)
                    {
                        best = cost[i];
                        u = i;
                    }
                }

                if (u < 0) return false;
                if (u == target) break;
                done[u] = true;

                RoadNode node = nodes[u];
                IReadOnlyList<RoadSegment> adjacent = node.Segments;
                for (int k = 0; k < adjacent.Count; k++)
                {
                    RoadSegment segment = adjacent[k];
                    int v = segment.GetOther(node).Id;
                    float candidate = cost[u] + segment.Length;
                    if (!done[v] && candidate < cost[v])
                    {
                        cost[v] = candidate;
                        viaSegment[v] = segment.Id;
                    }
                }
            }

            // Walk back from the destination to the endpoint of the start segment.
            var reversed = new List<(RoadSegment segment, bool forward)>();
            RoadNode current = to.Node;
            while (viaSegment[current.Id] >= 0)
            {
                RoadSegment segment = segments[viaSegment[current.Id]];
                RoadNode previous = segment.GetOther(current);
                reversed.Add((segment, segment.A == previous));
                current = previous;
            }

            var legs = new List<(RoadSegment segment, bool forward)>(reversed.Count + 1) { (start, current == start.B) };
            for (int i = reversed.Count - 1; i >= 0; i--)
            {
                legs.Add(reversed[i]);
            }

            route = new RoadRoute(to, legs, s0);
            return true;
        }

        /// <summary>Settlement closest to <paramref name="worldPosition"/> (straight-line), or <c>null</c> if there are none.</summary>
        public Settlement Nearest(Vector3 worldPosition)
        {
            Settlement best = null;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < settlements.Length; i++)
            {
                float sqr = (settlements[i].Position - worldPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = settlements[i];
                }
            }

            return best;
        }
    }
}
