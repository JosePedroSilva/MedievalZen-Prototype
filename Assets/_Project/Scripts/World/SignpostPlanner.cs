using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// One signpost per plain junction with three or more roads. Each arm lists the settlements whose shortest
    /// route from the junction starts along that road (nearest first).
    /// </summary>
    public static class SignpostPlanner
    {
        /// <summary>Plans signposts and registers their footprints in <paramref name="occupancy"/>.</summary>
        public static List<SignpostRecord> Plan(WorldGenParams p, float[,] map, RoadNetwork network, PaletteKeys keys, OccupancyMap occupancy)
        {
            var result = new List<SignpostRecord>();
            var rng = new System.Random(p.seed + 3);
            int count = network.Nodes.Count;
            var cost = new float[count];
            var firstSegment = new int[count];
            var done = new bool[count];

            foreach (RoadNode node in network.Nodes)
            {
                if (node.Segments.Count < 3 || node.Settlement != null) continue;

                var record = new SignpostRecord { nodeId = node.Id, variant = keys.PickVariant(PropRole.Sign, rng) };
                var angles = new List<float>(node.Segments.Count);
                foreach (RoadSegment segment in node.Segments)
                {
                    bool fromA = segment.A == node;
                    float d = Mathf.Min(15f, segment.Length);
                    Vector3 dir = segment.Evaluate(fromA ? d : segment.Length - d) - node.Position;
                    dir.y = 0f;
                    dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
                    record.arms.Add(new SignpostArmRecord { segmentId = segment.Id, towardNodeId = segment.GetOther(node).Id, direction = dir });
                    angles.Add(Mathf.Atan2(dir.z, dir.x));
                }

                // Dijkstra from the junction, remembering which arm each node was first reached through.
                for (int i = 0; i < count; i++)
                {
                    cost[i] = float.PositiveInfinity;
                    firstSegment[i] = -1;
                    done[i] = false;
                }

                cost[node.Id] = 0f;
                while (true)
                {
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

                    if (u < 0) break;
                    done[u] = true;
                    foreach (RoadSegment segment in network.Nodes[u].Segments)
                    {
                        int v = segment.GetOther(network.Nodes[u]).Id;
                        float candidate = cost[u] + segment.Length;
                        if (done[v] || candidate >= cost[v]) continue;
                        cost[v] = candidate;
                        firstSegment[v] = u == node.Id ? segment.Id : firstSegment[u];
                    }
                }

                var settlements = new List<Settlement>(network.Settlements);
                settlements.Sort((a, b) => cost[a.Node.Id].CompareTo(cost[b.Node.Id]));
                foreach (Settlement settlement in settlements)
                {
                    int first = firstSegment[settlement.Node.Id];
                    if (first < 0) continue;
                    foreach (SignpostArmRecord arm in record.arms)
                    {
                        if (arm.segmentId != first) continue;
                        arm.settlementIds.Add(settlement.Id);
                        arm.distances.Add(cost[settlement.Node.Id]);
                    }
                }

                // Stand the post in the widest gap between the roads, just off the road surface.
                angles.Sort();
                float bestGap = -1f;
                float mid = 0f;
                for (int i = 0; i < angles.Count; i++)
                {
                    float to = i + 1 < angles.Count ? angles[i + 1] : angles[0] + Mathf.PI * 2f;
                    if (to - angles[i] > bestGap)
                    {
                        bestGap = to - angles[i];
                        mid = (angles[i] + to) * 0.5f;
                    }
                }

                var gap = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                float offset = p.roadWidth * 0.5f + 2.5f + (bestGap < Mathf.PI * 0.6f ? 3f : 0f);
                Vector3 pos = node.Position + gap * offset;
                pos.y = HeightmapGenerator.SampleHeight(map, p, pos.x, pos.z);
                record.position = pos;
                record.yaw = ScatterPlanner.Yaw(-gap);
                occupancy.Add(pos, 1f);
                result.Add(record);
            }

            return result;
        }
    }
}
