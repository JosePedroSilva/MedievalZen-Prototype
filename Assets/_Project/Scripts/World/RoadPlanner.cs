using System;
using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>A node of a <see cref="RoadPlan"/> (settlement centre or junction).</summary>
    public sealed class PlannedNode
    {
        /// <summary>Horizontal position (y is 0 until the network is built).</summary>
        public Vector3 Position;

        /// <summary>Index into <see cref="RoadPlan.Settlements"/>, or -1 for a plain junction.</summary>
        public int SettlementIndex = -1;
    }

    /// <summary>A road between two planned nodes: a smoothed horizontal polyline (y = 0) from A to B.</summary>
    public sealed class PlannedSegment
    {
        /// <summary>Start node index.</summary>
        public int NodeA;

        /// <summary>End node index.</summary>
        public int NodeB;

        /// <summary>Smoothed centre line, about 2 m spacing, starting exactly on node A and ending on node B.</summary>
        public List<Vector3> Points = new List<Vector3>();
    }

    /// <summary>Output of <see cref="RoadPlanner.Plan"/>: routed road topology before heights are applied.</summary>
    public sealed class RoadPlan
    {
        /// <summary>Cost-grid cells per side.</summary>
        public int GridWidth;

        /// <summary>Cost-grid cell size in metres.</summary>
        public float CellSize;

        /// <summary>Grid cells crossed by a routed road (index = z * GridWidth + x).</summary>
        public bool[] RoadCells;

        /// <summary>All settlements: the input towns/villages followed by the inns the planner added.</summary>
        public List<SettlementSite> Settlements = new List<SettlementSite>();

        /// <summary>Road nodes; each settlement has exactly one.</summary>
        public List<PlannedNode> Nodes = new List<PlannedNode>();

        /// <summary>Road segments between nodes.</summary>
        public List<PlannedSegment> Segments = new List<PlannedSegment>();
    }

    /// <summary>
    /// Plans the road network: Gabriel-graph candidates → MST plus useful shortcuts → A* routing on a 10 m
    /// cost grid (slope-averse, cheap on existing roads so routes merge into trunks) → cell graph with nodes at
    /// settlements, junctions and inserted inns → Douglas-Peucker + centripetal Catmull-Rom curves.
    /// Heights are applied later (<see cref="ComputeProfiles"/>) once the terrain has been flattened.
    /// </summary>
    public static class RoadPlanner
    {
        /// <summary>Target cost-grid cell size in metres.</summary>
        public const float TargetCellSize = 10f;

        /// <summary>Cost multiplier per squared slope of a grid move (slope 0.1 => 1 + 4.5); makes roads wind through passes.</summary>
        public const float SlopePenalty = 450f;

        /// <summary>Cost factor for cells already used by a road.</summary>
        public const float ReuseFactor = 0.3f;

        /// <summary>An extra (non-MST) edge is kept when the existing path is at least this many times longer.</summary>
        public const float LoopDetourRatio = 1.6f;

        /// <summary>Douglas-Peucker tolerance before curve fitting, in metres.</summary>
        public const float SimplifyTolerance = 6f;

        /// <summary>Output curve spacing in metres.</summary>
        public const float CurveSpacing = 2f;

        /// <summary>Settlement-to-settlement roads longer than this get an inn at their midpoint.</summary>
        public const float MinInnLegLength = 500f;

        /// <summary>Moving-average window of the road elevation profile, in metres.</summary>
        public const float ProfileWindow = 40f;

        private const float JunctionMergeDistance = 30f;
        private const float SettlementMergeDistance = 40f;
        private const float NodeTrimDistance = 15f;
        private const float StubLength = 80f;

        private static readonly int[] DirX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DirZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] DirLength = { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f };

        /// <summary>
        /// Convenience for tests and tools: plans roads between <paramref name="sites"/> on an unmodified
        /// heightmap and builds the network directly (no flattening or carving).
        /// </summary>
        public static RoadNetwork PlanNetwork(WorldGenParams p, float[,] heightmap, List<SettlementSite> sites, out RoadPlan plan)
        {
            plan = Plan(p, Relief.Dry(p, heightmap), sites, new SettlementNames(p.seed + 8));
            List<Vector3[]> profiles = ComputeProfiles(plan, p, heightmap, out float[] nodeHeights);
            return BuildNetwork(plan, profiles, nodeHeights);
        }

        /// <summary>
        /// Routes roads between <paramref name="sites"/> (which are copied into the plan; inns are appended,
        /// named by <paramref name="names"/>). Deterministic; uses no randomness beyond the names.
        /// </summary>
        public static RoadPlan Plan(WorldGenParams p, Relief relief, List<SettlementSite> sites, SettlementNames names)
        {
            if (sites == null) throw new ArgumentNullException(nameof(sites));
            if (relief == null) throw new ArgumentNullException(nameof(relief));
            float[,] heightmap = relief.Map;
            var plan = new RoadPlan();
            plan.Settlements.AddRange(sites);

            int gw = Mathf.Max(2, Mathf.CeilToInt(p.worldSize / TargetCellSize));
            float cs = p.worldSize / gw;
            int cellCount = gw * gw;
            plan.GridWidth = gw;
            plan.CellSize = cs;

            var grid = new CostGrid(gw, cs);
            for (int z = 0; z < gw; z++)
            {
                for (int x = 0; x < gw; x++)
                {
                    int c = z * gw + x;
                    grid.Height[c] = HeightmapGenerator.SampleHeight(heightmap, p, (x + 0.5f) * cs, (z + 0.5f) * cs);
                    grid.Water[c] = relief.HasWater && grid.Height[c] < relief.WaterHeight + 0.5f;
                    int edge = Mathf.Min(Mathf.Min(x, z), Mathf.Min(gw - 1 - x, gw - 1 - z));
                    if (edge < 5) grid.Penalty[c] = (5 - edge) * 1.5f;
                }
            }

            // Settlement discs (+20 m) are expensive for roads that do not start or end there.
            var siteCells = new List<int>(sites.Count);
            for (int s = 0; s < sites.Count; s++)
            {
                siteCells.Add(grid.CellOf(sites[s].Position));
                grid.StampOwner(sites[s].Position, sites[s].Radius + 20f, s + 1);
            }

            // 1. Candidate edges: Gabriel graph, MST + detour-saving extras.
            List<(int a, int b)> edges = ChooseEdges(sites);
            edges.Sort((e1, e2) =>
            {
                bool t1 = sites[e1.a].Type == SettlementType.Town && sites[e1.b].Type == SettlementType.Town;
                bool t2 = sites[e2.a].Type == SettlementType.Town && sites[e2.b].Type == SettlementType.Town;
                if (t1 != t2) return t1 ? -1 : 1;
                int cmp = PlanarDistance(sites[e2.a].Position, sites[e2.b].Position).CompareTo(PlanarDistance(sites[e1.a].Position, sites[e1.b].Position));
                if (cmp != 0) return cmp;
                return e1.a != e2.a ? e1.a.CompareTo(e2.a) : e1.b.CompareTo(e2.b);
            });

            // 2. Route each edge with A*; later routes reuse earlier ones.
            var paths = new List<(int a, int b, List<int> cells)>();
            foreach ((int a, int b) in edges)
            {
                // Water is impassable; only if that disconnects the pair, wade through at a heavy cost.
                List<int> path = grid.FindPath(siteCells[a], siteCells[b], a, b, false) ?? grid.FindPath(siteCells[a], siteCells[b], a, b, true);
                if (path == null || path.Count < 2) continue;
                for (int i = 0; i < path.Count; i++) grid.Used[path[i]] = true;
                paths.Add((a, b, path));
            }

            plan.RoadCells = grid.Used;

            // 3. Cell graph from consecutive path cells.
            var adjacency = new Dictionary<int, List<int>>();
            foreach (var path in paths)
            {
                for (int i = 1; i < path.cells.Count; i++) Link(adjacency, path.cells[i - 1], path.cells[i]);
            }

            // 4. Inns at the middle of long settlement-to-settlement roads.
            AddInns(p, heightmap, grid, paths, plan.Settlements, siteCells, names);

            // 5. Nodes: settlement cells + branch cells, clustered.
            var candidates = new List<int>();
            var candidateSite = new List<int>();
            for (int s = 0; s < plan.Settlements.Count; s++)
            {
                candidates.Add(siteCells[s]);
                candidateSite.Add(s);
            }

            var siteCellSet = new HashSet<int>(siteCells);
            var junctionCells = new List<int>();
            foreach (var pair in adjacency)
            {
                if (pair.Value.Count >= 3 && !siteCellSet.Contains(pair.Key)) junctionCells.Add(pair.Key);
            }

            junctionCells.Sort();
            foreach (int cell in junctionCells)
            {
                candidates.Add(cell);
                candidateSite.Add(-1);
            }

            var cellNode = new Dictionary<int, int>();
            BuildNodes(plan, grid, candidates, candidateSite, cellNode);

            // 6. Trace cell chains between nodes.
            var chains = TraceChains(grid, adjacency, candidates, cellNode, cellCount);

            // 7. Turn chains into raw segments, clean up the topology, then smooth.
            BuildSegments(plan, grid, chains);
            CleanTopology(plan);
            for (int i = 0; i < plan.Segments.Count; i++)
            {
                PlannedSegment segment = plan.Segments[i];
                List<Vector3> simplified = Polyline.Simplify(segment.Points, SimplifyTolerance);
                Polyline curve = Polyline.CatmullRom(simplified, CurveSpacing);
                segment.Points = new List<Vector3>(curve.Points);
                segment.Points[0] = plan.Nodes[segment.NodeA].Position;
                segment.Points[segment.Points.Count - 1] = plan.Nodes[segment.NodeB].Position;
            }

            return plan;
        }

        /// <summary>
        /// Samples <paramref name="heightmap"/> (already flattened under settlements) along every segment and
        /// smooths the elevation with a <see cref="ProfileWindow"/> moving average, pinned to the node heights at
        /// both ends so all roads meeting at a node agree. Returns one point array per segment (y = road height).
        /// </summary>
        public static List<Vector3[]> ComputeProfiles(RoadPlan plan, WorldGenParams p, float[,] heightmap, out float[] nodeHeights)
        {
            nodeHeights = new float[plan.Nodes.Count];
            for (int i = 0; i < plan.Nodes.Count; i++)
            {
                Vector3 pos = plan.Nodes[i].Position;
                nodeHeights[i] = HeightmapGenerator.SampleHeight(heightmap, p, pos.x, pos.z);
            }

            var profiles = new List<Vector3[]>(plan.Segments.Count);
            foreach (PlannedSegment segment in plan.Segments)
            {
                int n = segment.Points.Count;
                var raw = new float[n];
                var along = new float[n];
                var prefix = new double[n + 1];
                for (int i = 0; i < n; i++)
                {
                    Vector3 pt = segment.Points[i];
                    raw[i] = HeightmapGenerator.SampleHeight(heightmap, p, pt.x, pt.z);
                    prefix[i + 1] = prefix[i] + raw[i];
                    if (i > 0) along[i] = along[i - 1] + PlanarDistance(segment.Points[i - 1], pt);
                }

                float total = along[n - 1];
                float spacing = n > 1 ? Mathf.Max(0.5f, total / (n - 1)) : 1f;
                int half = Mathf.Max(1, Mathf.RoundToInt(ProfileWindow * 0.5f / spacing));
                float hA = nodeHeights[segment.NodeA];
                float hB = nodeHeights[segment.NodeB];
                const float PinDistance = 30f;

                var result = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    int lo = Mathf.Max(0, i - half);
                    int hi = Mathf.Min(n - 1, i + half);
                    float h = (float)((prefix[hi + 1] - prefix[lo]) / (hi - lo + 1));
                    float fromStart = along[i];
                    float fromEnd = total - along[i];
                    h = Mathf.Lerp(hA, h, SmoothStep01(fromStart / PinDistance));
                    h = Mathf.Lerp(hB, h, SmoothStep01(fromEnd / PinDistance));
                    Vector3 pt = segment.Points[i];
                    result[i] = new Vector3(pt.x, h, pt.z);
                }

                result[0].y = hA;
                result[n - 1].y = hB;
                profiles.Add(result);
            }

            return profiles;
        }

        /// <summary>
        /// Builds the runtime network. Settlement ids equal indices into <see cref="RoadPlan.Settlements"/>;
        /// segment ids equal indices into <see cref="RoadPlan.Segments"/>.
        /// </summary>
        public static RoadNetwork BuildNetwork(RoadPlan plan, IReadOnlyList<Vector3[]> profiles, float[] nodeHeights)
        {
            var builder = new RoadNetworkBuilder();
            var built = new RoadNode[plan.Nodes.Count];
            var siteNode = new int[plan.Settlements.Count];
            for (int i = 0; i < siteNode.Length; i++) siteNode[i] = -1;
            for (int n = 0; n < plan.Nodes.Count; n++)
            {
                if (plan.Nodes[n].SettlementIndex >= 0) siteNode[plan.Nodes[n].SettlementIndex] = n;
            }

            for (int s = 0; s < plan.Settlements.Count; s++)
            {
                SettlementSite site = plan.Settlements[s];
                int n = siteNode[s];
                if (n < 0) throw new InvalidOperationException($"Settlement {site} has no road node.");
                Vector3 pos = plan.Nodes[n].Position;
                built[n] = builder.AddSettlement(site.Name, site.Type, new Vector3(pos.x, nodeHeights[n], pos.z), site.Radius).Node;
            }

            for (int n = 0; n < plan.Nodes.Count; n++)
            {
                if (built[n] != null) continue;
                Vector3 pos = plan.Nodes[n].Position;
                built[n] = builder.AddNode(new Vector3(pos.x, nodeHeights[n], pos.z));
            }

            for (int i = 0; i < plan.Segments.Count; i++)
            {
                PlannedSegment segment = plan.Segments[i];
                builder.AddSegment(built[segment.NodeA], built[segment.NodeB], profiles[i]);
            }

            return builder.Build();
        }

        // ---------------------------------------------------------------- edges

        private static List<(int a, int b)> ChooseEdges(List<SettlementSite> sites)
        {
            int n = sites.Count;
            var gabriel = new List<(int a, int b, float length)>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    Vector3 pi = sites[i].Position;
                    Vector3 pj = sites[j].Position;
                    float mx = (pi.x + pj.x) * 0.5f;
                    float mz = (pi.z + pj.z) * 0.5f;
                    float r2 = ((pi.x - pj.x) * (pi.x - pj.x) + (pi.z - pj.z) * (pi.z - pj.z)) * 0.25f;
                    bool empty = true;
                    for (int k = 0; k < n && empty; k++)
                    {
                        if (k == i || k == j) continue;
                        Vector3 pk = sites[k].Position;
                        float dx = pk.x - mx;
                        float dz = pk.z - mz;
                        if (dx * dx + dz * dz < r2) empty = false;
                    }

                    if (empty) gabriel.Add((i, j, PlanarDistance(pi, pj)));
                }
            }

            gabriel.Sort((e1, e2) =>
            {
                int cmp = e1.length.CompareTo(e2.length);
                if (cmp != 0) return cmp;
                return e1.a != e2.a ? e1.a.CompareTo(e2.a) : e1.b.CompareTo(e2.b);
            });

            // Kruskal MST.
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            var kept = new List<(int a, int b)>();
            var inMst = new bool[gabriel.Count];
            for (int e = 0; e < gabriel.Count; e++)
            {
                int ra = Find(gabriel[e].a);
                int rb = Find(gabriel[e].b);
                if (ra == rb) continue;
                parent[ra] = rb;
                inMst[e] = true;
                kept.Add((gabriel[e].a, gabriel[e].b));
            }

            // Extra edges: only where the kept graph forces a long detour (gives a few loops, no near-duplicates).
            for (int e = 0; e < gabriel.Count; e++)
            {
                if (inMst[e]) continue;
                float existing = ShortestPath(sites, kept, gabriel[e].a, gabriel[e].b);
                if (existing >= LoopDetourRatio * gabriel[e].length) kept.Add((gabriel[e].a, gabriel[e].b));
            }

            return kept;
        }

        private static float ShortestPath(List<SettlementSite> sites, List<(int a, int b)> edges, int from, int to)
        {
            int n = sites.Count;
            var dist = new float[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
            dist[from] = 0f;
            while (true)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (!done[i] && dist[i] < best)
                    {
                        best = dist[i];
                        u = i;
                    }
                }

                if (u < 0 || u == to) break;
                done[u] = true;
                foreach ((int a, int b) in edges)
                {
                    int v = a == u ? b : b == u ? a : -1;
                    if (v < 0) continue;
                    float candidate = dist[u] + PlanarDistance(sites[u].Position, sites[v].Position);
                    if (candidate < dist[v]) dist[v] = candidate;
                }
            }

            return dist[to];
        }

        // ---------------------------------------------------------------- inns

        private static void AddInns(WorldGenParams p, float[,] heightmap, CostGrid grid, List<(int a, int b, List<int> cells)> paths,
            List<SettlementSite> settlements, List<int> siteCells, SettlementNames names)
        {
            if (p.innCount <= 0) return;

            var candidates = new List<(int path, float length)>();
            for (int k = 0; k < paths.Count; k++)
            {
                var path = paths[k];
                bool clean = true;
                for (int i = 0; i < path.cells.Count && clean; i++)
                {
                    int owner = grid.Owner[path.cells[i]] - 1;
                    if (owner >= 0 && owner != path.a && owner != path.b) clean = false;
                }

                float length = grid.PathLength(path.cells);
                if (clean && length > MinInnLegLength) candidates.Add((k, length));
            }

            candidates.Sort((c1, c2) => c1.length != c2.length ? c2.length.CompareTo(c1.length) : c1.path.CompareTo(c2.path));

            float innSpacing = Mathf.Max(250f, p.minSettlementSpacing * 0.7f);
            float edgeMargin = 60f;
            int placed = 0;
            foreach (var candidate in candidates)
            {
                if (placed >= p.innCount) break;
                List<int> cells = paths[candidate.path].cells;

                // Search the middle 40 % of the road for the flattest valid spot (cheap to flatten, no craters).
                int mid = grid.IndexAtDistance(cells, candidate.length * 0.5f);
                int maxShift = Mathf.Max(1, cells.Count / 5);
                int bestIndex = -1;
                float bestScore = float.PositiveInfinity;
                for (int index = Mathf.Max(1, mid - maxShift); index <= Mathf.Min(cells.Count - 2, mid + maxShift); index++)
                {
                    Vector3 pos = grid.CenterOf(cells[index]);
                    if (grid.Water[cells[index]]) continue;
                    if (pos.x < edgeMargin || pos.z < edgeMargin || pos.x > p.worldSize - edgeMargin || pos.z > p.worldSize - edgeMargin) continue;
                    bool ok = true;
                    for (int s = 0; s < settlements.Count && ok; s++)
                    {
                        if (PlanarDistance(settlements[s].Position, pos) < innSpacing) ok = false;
                    }

                    if (!ok) continue;
                    float score = SettlementPlacer.HeightStdDev(heightmap, p, new Vector2(pos.x, pos.z), SettlementSite.InnRadius + 15f)
                                  + 0.01f * Mathf.Abs(index - mid) * grid.Cell;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIndex = index;
                    }
                }

                if (bestIndex < 0) continue;
                Vector3 innPos = grid.CenterOf(cells[bestIndex]);
                innPos.y = HeightmapGenerator.SampleHeight(heightmap, p, innPos.x, innPos.z);
                settlements.Add(new SettlementSite(names.NextInn(), SettlementType.Inn, innPos));
                siteCells.Add(cells[bestIndex]);
                placed++;
            }
        }

        // ---------------------------------------------------------------- topology

        private static void Link(Dictionary<int, List<int>> adjacency, int a, int b)
        {
            if (a == b) return;
            if (!adjacency.TryGetValue(a, out List<int> la)) adjacency[a] = la = new List<int>(2);
            if (!adjacency.TryGetValue(b, out List<int> lb)) adjacency[b] = lb = new List<int>(2);
            if (!la.Contains(b)) la.Add(b);
            if (!lb.Contains(a)) lb.Add(a);
        }

        private static void BuildNodes(RoadPlan plan, CostGrid grid, List<int> candidates, List<int> candidateSite, Dictionary<int, int> cellNode)
        {
            int count = candidates.Count;
            var parent = new int[count];
            var rootSite = new int[count];
            for (int i = 0; i < count; i++)
            {
                parent[i] = i;
                rootSite[i] = candidateSite[i];
            }

            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    bool anySite = candidateSite[i] >= 0 || candidateSite[j] >= 0;
                    if (candidateSite[i] >= 0 && candidateSite[j] >= 0) continue;
                    float limit = anySite ? SettlementMergeDistance : JunctionMergeDistance;
                    Vector3 pi = candidateSite[i] >= 0 ? plan.Settlements[candidateSite[i]].Position : grid.CenterOf(candidates[i]);
                    Vector3 pj = candidateSite[j] >= 0 ? plan.Settlements[candidateSite[j]].Position : grid.CenterOf(candidates[j]);
                    if (PlanarDistance(pi, pj) > limit) continue;
                    int ri = Find(i);
                    int rj = Find(j);
                    if (ri == rj || (rootSite[ri] >= 0 && rootSite[rj] >= 0)) continue;
                    if (ri < rj)
                    {
                        parent[rj] = ri;
                        if (rootSite[ri] < 0) rootSite[ri] = rootSite[rj];
                    }
                    else
                    {
                        parent[ri] = rj;
                        if (rootSite[rj] < 0) rootSite[rj] = rootSite[ri];
                    }
                }
            }

            var rootNode = new Dictionary<int, int>();
            var sums = new List<Vector3>();
            var counts = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int root = Find(i);
                if (!rootNode.TryGetValue(root, out int node))
                {
                    node = plan.Nodes.Count;
                    rootNode[root] = node;
                    plan.Nodes.Add(new PlannedNode { SettlementIndex = rootSite[root] });
                    sums.Add(Vector3.zero);
                    counts.Add(0);
                }

                cellNode[candidates[i]] = node;
                sums[node] += grid.CenterOf(candidates[i]);
                counts[node]++;
            }

            for (int n = 0; n < plan.Nodes.Count; n++)
            {
                PlannedNode node = plan.Nodes[n];
                Vector3 pos = node.SettlementIndex >= 0 ? plan.Settlements[node.SettlementIndex].Position : sums[n] / counts[n];
                node.Position = new Vector3(pos.x, 0f, pos.z);
            }
        }

        private readonly struct Chain
        {
            public Chain(int nodeA, int nodeB, List<int> cells, float length)
            {
                NodeA = nodeA;
                NodeB = nodeB;
                Cells = cells;
                Length = length;
            }

            public int NodeA { get; }
            public int NodeB { get; }
            public List<int> Cells { get; }
            public float Length { get; }
        }

        private static List<Chain> TraceChains(CostGrid grid, Dictionary<int, List<int>> adjacency, List<int> candidates,
            Dictionary<int, int> cellNode, int cellCount)
        {
            var chains = new List<Chain>();
            var visited = new HashSet<long>();
            foreach (int start in candidates)
            {
                if (!adjacency.TryGetValue(start, out List<int> neighbours)) continue;
                foreach (int first in neighbours)
                {
                    if (!visited.Add((long)start * cellCount + first)) continue;
                    var cells = new List<int> { start, first };
                    int prev = start;
                    int cur = first;
                    bool dead = false;
                    while (!cellNode.ContainsKey(cur))
                    {
                        List<int> next = adjacency[cur];
                        if (next.Count != 2 || cells.Count > cellCount)
                        {
                            dead = true;
                            break;
                        }

                        int step = next[0] == prev ? next[1] : next[0];
                        prev = cur;
                        cur = step;
                        cells.Add(cur);
                    }

                    if (dead) continue;
                    visited.Add((long)cur * cellCount + prev);
                    int a = cellNode[start];
                    int b = cellNode[cur];
                    if (a == b) continue;
                    chains.Add(new Chain(a, b, cells, grid.PathLength(cells)));
                }
            }

            // Near-duplicate chains between the same two nodes: keep the shortest, keep others only if much longer.
            chains.Sort((c1, c2) => c1.Length.CompareTo(c2.Length));
            var shortest = new Dictionary<long, float>();
            var result = new List<Chain>(chains.Count);
            foreach (Chain chain in chains)
            {
                long key = (long)Mathf.Min(chain.NodeA, chain.NodeB) * 1000003L + Mathf.Max(chain.NodeA, chain.NodeB);
                if (shortest.TryGetValue(key, out float best) && chain.Length < best * 1.5f) continue;
                if (!shortest.ContainsKey(key)) shortest[key] = chain.Length;
                result.Add(chain);
            }

            return result;
        }

        private static void BuildSegments(RoadPlan plan, CostGrid grid, List<Chain> chains)
        {
            foreach (Chain chain in chains)
            {
                Vector3 a = plan.Nodes[chain.NodeA].Position;
                Vector3 b = plan.Nodes[chain.NodeB].Position;
                var points = new List<Vector3>(chain.Cells.Count) { a };
                for (int i = 1; i < chain.Cells.Count - 1; i++)
                {
                    Vector3 c = grid.CenterOf(chain.Cells[i]);
                    if (PlanarDistance(c, a) < NodeTrimDistance || PlanarDistance(c, b) < NodeTrimDistance) continue;
                    points.Add(c);
                }

                points.Add(b);
                plan.Segments.Add(new PlannedSegment { NodeA = chain.NodeA, NodeB = chain.NodeB, Points = points });
            }
        }

        private static void CleanTopology(RoadPlan plan)
        {
            var segments = plan.Segments;
            var removed = new bool[segments.Count];
            var degree = new List<int>[plan.Nodes.Count];
            for (int n = 0; n < degree.Length; n++) degree[n] = new List<int>(4);
            for (int s = 0; s < segments.Count; s++)
            {
                degree[segments[s].NodeA].Add(s);
                degree[segments[s].NodeB].Add(s);
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int n = 0; n < plan.Nodes.Count; n++)
                {
                    if (plan.Nodes[n].SettlementIndex >= 0) continue;
                    List<int> incident = degree[n];
                    if (incident.Count == 1 && SegmentLength(segments[incident[0]].Points) < StubLength)
                    {
                        int s = incident[0];
                        removed[s] = true;
                        degree[segments[s].NodeA].Remove(s);
                        degree[segments[s].NodeB].Remove(s);
                        changed = true;
                    }
                    else if (incident.Count == 2)
                    {
                        int s1 = incident[0];
                        int s2 = incident[1];
                        int other1 = segments[s1].NodeA == n ? segments[s1].NodeB : segments[s1].NodeA;
                        int other2 = segments[s2].NodeA == n ? segments[s2].NodeB : segments[s2].NodeA;
                        if (other1 == other2 || other1 == n || other2 == n) continue;

                        // other1 -> n -> other2
                        var points = new List<Vector3>(segments[s1].Points);
                        if (segments[s1].NodeB != n) points.Reverse();
                        var tail = new List<Vector3>(segments[s2].Points);
                        if (segments[s2].NodeA != n) tail.Reverse();
                        for (int i = 1; i < tail.Count; i++) points.Add(tail[i]);

                        removed[s1] = true;
                        removed[s2] = true;
                        degree[other1].Remove(s1);
                        degree[other2].Remove(s2);
                        incident.Clear();
                        int merged = segments.Count;
                        segments.Add(new PlannedSegment { NodeA = other1, NodeB = other2, Points = points });
                        Array.Resize(ref removed, segments.Count);
                        degree[other1].Add(merged);
                        degree[other2].Add(merged);
                        changed = true;
                    }
                }
            }

            // Compact nodes (drop orphaned junctions) and segments.
            var nodeMap = new int[plan.Nodes.Count];
            var nodes = new List<PlannedNode>(plan.Nodes.Count);
            for (int n = 0; n < plan.Nodes.Count; n++)
            {
                bool keep = plan.Nodes[n].SettlementIndex >= 0 || degree[n].Count > 0;
                nodeMap[n] = keep ? nodes.Count : -1;
                if (keep) nodes.Add(plan.Nodes[n]);
            }

            var kept = new List<PlannedSegment>(segments.Count);
            for (int s = 0; s < segments.Count; s++)
            {
                if (removed[s]) continue;
                PlannedSegment segment = segments[s];
                segment.NodeA = nodeMap[segment.NodeA];
                segment.NodeB = nodeMap[segment.NodeB];
                kept.Add(segment);
            }

            plan.Nodes = nodes;
            plan.Segments = kept;
        }

        // ---------------------------------------------------------------- helpers

        internal static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float SegmentLength(List<Vector3> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++) length += PlanarDistance(points[i - 1], points[i]);
            return length;
        }

        private static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Flat-array cost grid with an allocation-free A* (binary heap, search stamps).</summary>
        private sealed class CostGrid
        {
            public readonly int Width;
            public readonly float Cell;
            public readonly float[] Height;
            public readonly float[] Penalty;
            public readonly int[] Owner;
            public readonly bool[] Used;
            public readonly bool[] Water;

            private readonly float[] g;
            private readonly int[] parent;
            private readonly int[] openStamp;
            private readonly int[] closedStamp;
            private int[] heapItems = new int[1024];
            private float[] heapKeys = new float[1024];
            private int heapCount;
            private int search;

            public CostGrid(int width, float cell)
            {
                Width = width;
                Cell = cell;
                int count = width * width;
                Height = new float[count];
                Penalty = new float[count];
                Owner = new int[count];
                Used = new bool[count];
                Water = new bool[count];
                g = new float[count];
                parent = new int[count];
                openStamp = new int[count];
                closedStamp = new int[count];
            }

            public int CellOf(Vector3 position)
            {
                int x = Mathf.Clamp((int)(position.x / Cell), 0, Width - 1);
                int z = Mathf.Clamp((int)(position.z / Cell), 0, Width - 1);
                return z * Width + x;
            }

            public Vector3 CenterOf(int cell) => new Vector3((cell % Width + 0.5f) * Cell, 0f, (cell / Width + 0.5f) * Cell);

            public void StampOwner(Vector3 center, float radius, int owner)
            {
                int r = Mathf.CeilToInt(radius / Cell);
                int cx = (int)(center.x / Cell);
                int cz = (int)(center.z / Cell);
                for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(Width - 1, cz + r); z++)
                {
                    for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(Width - 1, cx + r); x++)
                    {
                        float dx = (x + 0.5f) * Cell - center.x;
                        float dz = (z + 0.5f) * Cell - center.z;
                        if (dx * dx + dz * dz <= radius * radius) Owner[z * Width + x] = owner;
                    }
                }
            }

            public float PathLength(List<int> cells)
            {
                float length = 0f;
                for (int i = 1; i < cells.Count; i++) length += PlanarDistance(CenterOf(cells[i - 1]), CenterOf(cells[i]));
                return length;
            }

            public int IndexAtDistance(List<int> cells, float distance)
            {
                float length = 0f;
                for (int i = 1; i < cells.Count; i++)
                {
                    length += PlanarDistance(CenterOf(cells[i - 1]), CenterOf(cells[i]));
                    if (length >= distance) return i;
                }

                return cells.Count - 1;
            }

            public List<int> FindPath(int start, int goal, int siteA, int siteB, bool allowWater)
            {
                search++;
                heapCount = 0;
                g[start] = 0f;
                parent[start] = -1;
                openStamp[start] = search;
                Push(start, Heuristic(start, goal));

                bool found = false;
                while (heapCount > 0)
                {
                    int current = Pop();
                    if (closedStamp[current] == search) continue;
                    closedStamp[current] = search;
                    if (current == goal)
                    {
                        found = true;
                        break;
                    }

                    int cx = current % Width;
                    int cz = current / Width;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cx + DirX[d];
                        int nz = cz + DirZ[d];
                        if (nx < 0 || nz < 0 || nx >= Width || nz >= Width) continue;
                        int next = nz * Width + nx;
                        if (closedStamp[next] == search) continue;
                        if (Water[next] && !allowWater) continue;

                        // Never cut diagonally across an existing road: that would cross it without a junction.
                        if (d >= 4 && Used[cz * Width + nx] && Used[nz * Width + cx]) continue;

                        float dist = DirLength[d] * Cell;
                        float slope = (Height[next] - Height[current]) / dist;
                        float multiplier = 1f + SlopePenalty * slope * slope + Penalty[next];
                        int owner = Owner[next] - 1;
                        if (owner >= 0 && owner != siteA && owner != siteB) multiplier += 6f;
                        if (Water[next]) multiplier += 40f;
                        if (Used[next]) multiplier *= ReuseFactor;
                        float candidate = g[current] + dist * multiplier;
                        if (openStamp[next] != search || candidate < g[next])
                        {
                            openStamp[next] = search;
                            g[next] = candidate;
                            parent[next] = current;
                            Push(next, candidate + Heuristic(next, goal));
                        }
                    }
                }

                if (!found) return null;
                var path = new List<int>();
                for (int c = goal; c >= 0; c = parent[c]) path.Add(c);
                path.Reverse();
                return path;
            }

            private float Heuristic(int cell, int goal)
            {
                int dx = Mathf.Abs(cell % Width - goal % Width);
                int dz = Mathf.Abs(cell / Width - goal / Width);
                int diag = Mathf.Min(dx, dz);
                int straight = Mathf.Max(dx, dz) - diag;
                return (straight + 1.41421356f * diag) * Cell * ReuseFactor;
            }

            private void Push(int item, float key)
            {
                if (heapCount == heapItems.Length)
                {
                    Array.Resize(ref heapItems, heapCount * 2);
                    Array.Resize(ref heapKeys, heapCount * 2);
                }

                int i = heapCount++;
                while (i > 0)
                {
                    int up = (i - 1) >> 1;
                    if (heapKeys[up] <= key) break;
                    heapItems[i] = heapItems[up];
                    heapKeys[i] = heapKeys[up];
                    i = up;
                }

                heapItems[i] = item;
                heapKeys[i] = key;
            }

            private int Pop()
            {
                int top = heapItems[0];
                int lastItem = heapItems[--heapCount];
                float lastKey = heapKeys[heapCount];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= heapCount) break;
                    if (child + 1 < heapCount && heapKeys[child + 1] < heapKeys[child]) child++;
                    if (heapKeys[child] >= lastKey) break;
                    heapItems[i] = heapItems[child];
                    heapKeys[i] = heapKeys[child];
                    i = child;
                }

                if (heapCount > 0)
                {
                    heapItems[i] = lastItem;
                    heapKeys[i] = lastKey;
                }

                return top;
            }
        }
    }
}
