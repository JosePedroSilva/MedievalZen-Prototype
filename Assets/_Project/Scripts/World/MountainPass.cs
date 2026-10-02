using System;
using System.Collections.Generic;
using System.Text;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>What happened on one side of the range when the pass road was laid out.</summary>
    public sealed class PassSide
    {
        /// <summary>+1 or -1: which side of the ridge (along <see cref="PassRecord.RidgeNormal"/>).</summary>
        public int Side;
        /// <summary>True when a pass road reaches the lowland network on this side.</summary>
        public bool Routed;
        /// <summary>Where the valley below the col meets the lowland (steepest descent from the col).</summary>
        public Vector3 ValleyMouth;
        /// <summary>Where the pass road joins the lowland roads (junction or settlement).</summary>
        public Vector3 Join;
        /// <summary>Road length in metres (0 when not routed).</summary>
        public float Length;
        /// <summary>Search effort of the successful (or last) attempt.</summary>
        public int Expansions;
        /// <summary>Join candidates tried.</summary>
        public int Attempts;
    }

    /// <summary>The mountain inn and the pass road(s) over the col, as generated.</summary>
    public sealed class PassRecord
    {
        /// <summary>Index of the mountain inn in the settlement list (= its settlement id), or -1 when it was skipped.</summary>
        public int InnSite = -1;
        /// <summary>The col between the two central summits (y = terrain height when found).</summary>
        public Vector3 Col;
        /// <summary>Summits either side of the col.</summary>
        public Vector3 PeakBefore;
        /// <summary>Summits either side of the col.</summary>
        public Vector3 PeakAfter;
        /// <summary>Unit horizontal direction across the ridge at the col (side +1).</summary>
        public Vector3 RidgeNormal;
        /// <summary>Per-side routing outcome.</summary>
        public readonly List<PassSide> Sides = new List<PassSide>();
        /// <summary>Segment ids of the pass roads in the road network.</summary>
        public readonly List<int> SegmentIds = new List<int>();
        /// <summary>Why the inn was skipped, if it was.</summary>
        public string SkipReason;

        /// <summary>True when pass roads descend on both sides of the range (the pass closes a loop).</summary>
        public bool TwoSided
        {
            get
            {
                int routed = 0;
                foreach (PassSide side in Sides) if (side.Routed) routed++;
                return routed >= 2;
            }
        }

        /// <summary>One-line description for logs.</summary>
        public string Summary()
        {
            if (InnSite < 0) return $"Mountain pass: no mountain inn ({SkipReason ?? "disabled"}).";
            var sb = new StringBuilder();
            sb.Append($"Mountain pass: col at ({Col.x:0}, {Col.y:0.0}, {Col.z:0}); ").Append(TwoSided ? "two-sided pass road (closes a loop)" : "one-sided spur (there and back)");
            foreach (PassSide side in Sides)
            {
                sb.Append($"; side {side.Side:+0;-0}: ").Append(side.Routed ? $"{side.Length:0} m to ({side.Join.x:0}, {side.Join.z:0})" : "no road")
                  .Append($" [{side.Attempts} tries, {side.Expansions} states]");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Places the mountain inn on a shelf at the col of the <see cref="MountainRange"/> and lays out grade-limited pass roads
    /// (<see cref="PassRouter"/>) from it down both sides of the range to the lowland road network, splitting a lowland road
    /// at each join (a junction that gets a signpost). Skips gracefully when the terrain offers no site or no road.
    /// </summary>
    public static class MountainPassPlanner
    {
        /// <summary>The mountain inn stands at least this high (fraction of the total relief).</summary>
        public const float MinInnRelief = 0.55f;

        /// <summary>Largest height standard deviation (metres) within 45 m of an acceptable inn site.</summary>
        public const float MaxInnRoughness = 12f;

        /// <summary>A pass road is rejected when its profile would need a cut or fill deeper than this, metres.</summary>
        public const float MaxEarthwork = 10f;

        /// <summary>Search effort (expanded states) allowed for all pass roads of one world together.</summary>
        public const int TotalSearchBudget = 1300000;

        /// <summary>Join candidates tried per side of the range.</summary>
        public const int MaxAttemptsPerSide = 3;

        /// <summary>Preferred distance of the inn from the col: on the level shelf just below it, so the pass itself stays open.</summary>
        public const float PreferredInnDistance = 65f;

        private const float DrainageStep = 8f;

        /// <summary>Length of the final approach to the lowland road (blend plus straight), metres.</summary>
        private const float ApproachLength = 45f;

        /// <summary>Length of the blend from the searched road onto the straight approach, metres.</summary>
        private const float BlendLength = 24f;

        /// <summary>
        /// Finds the col and appends the mountain inn to <paramref name="sites"/> (named by <paramref name="names"/>).
        /// Returns the record (with <see cref="PassRecord.InnSite"/> = -1 and a reason when no site qualifies), or null when
        /// the mountain inn is disabled.
        /// </summary>
        public static PassRecord PlaceInn(WorldGenParams p, Relief relief, List<SettlementSite> sites, SettlementNames names)
        {
            if (!p.mountainInn) return null;
            var record = new PassRecord();
            FindCol(relief, record);
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);
            float minHeight = (MinInnRelief + 0.015f) * amplitude;
            float margin = 100f;

            Vector2 best = default;
            float bestScore = float.PositiveInfinity;
            float bestRough = 0f;
            for (float r = 0f; r <= 160f; r += 10f)
            {
                int around = r <= 0f ? 1 : 16;
                for (int k = 0; k < around; k++)
                {
                    float a = k * Mathf.PI * 2f / around;
                    var c = new Vector2(record.Col.x + Mathf.Cos(a) * r, record.Col.z + Mathf.Sin(a) * r);
                    if (c.x < margin || c.y < margin || c.x > p.worldSize - margin || c.y > p.worldSize - margin) continue;
                    float h = relief.Height(c.x, c.y);
                    if (h < minHeight || h > record.Col.y + 12f) continue;   // on the pass, not up on a summit
                    if (SettlementPlacer.WetWithin(relief, c, SettlementSite.InnRadius + 20f)) continue;
                    bool clear = true;
                    foreach (SettlementSite site in sites)
                    {
                        if (RoadPlanner.PlanarDistance(site.Position, new Vector3(c.x, 0f, c.y)) < Mathf.Max(250f, p.minSettlementSpacing * 0.7f)) clear = false;
                    }

                    if (!clear) continue;
                    float rough = SettlementPlacer.HeightStdDev(relief.Map, p, c, SettlementSite.InnRadius + 10f);
                    float score = rough + 0.03f * Mathf.Abs(r - PreferredInnDistance);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                        bestRough = rough;
                    }
                }
            }

            if (float.IsPositiveInfinity(bestScore))
            {
                record.SkipReason = "no dry, clear ground above " + (MinInnRelief * 100f).ToString("0") + " % of the relief near the col";
                return record;
            }

            if (bestRough > MaxInnRoughness)
            {
                record.SkipReason = $"the col is too rugged for a building (height spread {bestRough:0.0} m)";
                return record;
            }

            var position = new Vector3(best.x, relief.Height(best.x, best.y), best.y);
            sites.Add(new SettlementSite(names.NextMountainInn(), SettlementType.Inn, position));
            record.InnSite = sites.Count - 1;
            return record;
        }

        /// <summary>
        /// Routes the pass roads for <paramref name="pass"/> into <paramref name="plan"/> (after the lowland roads are planned
        /// and smoothed). Removes the inn again when neither side can be reached.
        /// </summary>
        public static void RouteRoads(WorldGenParams p, Relief relief, RoadPlan plan, PassRecord pass)
        {
            if (pass == null || pass.InnSite < 0) return;
            int innNode = plan.Nodes.FindIndex(n => n.SettlementIndex == pass.InnSite);
            if (innNode < 0)
            {
                pass.SkipReason = "the inn has no road node";
                pass.InnSite = -1;
                return;
            }

            SettlementSite inn = plan.Settlements[pass.InnSite];
            var router = new PassRouter(p, relief, p.maxRoadGrade);
            for (int s = 0; s < plan.Settlements.Count; s++)
            {
                if (s != pass.InnSite) router.AddKeepOut(plan.Settlements[s].Position, plan.Settlements[s].Radius + 12f);
            }

            foreach (PlannedSegment segment in plan.Segments) router.AddRoad(segment.Points);
            float amplitude = Mathf.Max(1e-3f, p.hillAmplitude);

            foreach (int side in new[] { 1, -1 })
            {
                var result = new PassSide { Side = side };
                pass.Sides.Add(result);
                Vector3 normal = pass.RidgeNormal * side;
                result.ValleyMouth = ValleyMouth(relief, router, pass, normal, 0.3f * amplitude);
                var start = new Vector3(inn.Position.x, 0f, inn.Position.z);
                int sideBudget = TotalSearchBudget / 2;
                int sideSpent = 0;

                foreach (JoinCandidate candidate in JoinCandidates(plan, router, pass, normal, result.ValleyMouth, amplitude, 8))
                {
                    if (result.Attempts >= MaxAttemptsPerSide || sideSpent >= sideBudget) break;

                    // The last stretch is a straight approach square to the lowland road (or into the widest gap at a node),
                    // so the two roads meet at a clear angle rather than running side by side into the junction. A join on a
                    // road below a steep bank, with no even line to approach it, is not worth a search.
                    Vector3 arrival = GentleArrival(router, candidate.Position, ArrivalDirection(plan, candidate, inn.Position), p.maxRoadGrade);
                    bool square = arrival.sqrMagnitude > 0.5f;
                    if (!square && candidate.Segment >= 0) continue;
                    result.Attempts++;
                    router.ExpansionLimit = Mathf.Min(PassRouter.MaxExpansions, sideBudget - sideSpent);
                    Vector3 approach = square ? candidate.Position - arrival * ApproachLength : candidate.Position;
                    List<Vector3> path = router.Route(start, approach, 260f, normal, 80f, square ? arrival : default, 40f,
                        square ? ApproachLength : 0f, candidate.Position);
                    if (path != null && square) AppendApproach(path, candidate.Position, arrival);
                    sideSpent += router.LastExpansions;
                    result.Expansions += router.LastExpansions;
                    bool clear = path != null && Clearance(path, plan, candidate.Position) &&
                                 EarthworkDepth(router, path, p.maxRoadGrade) <= MaxEarthwork;
                    if (!clear) continue;

                    int joinNode = Join(plan, candidate);
                    if (joinNode < 0) continue;
                    path[path.Count - 1] = plan.Nodes[joinNode].Position;
                    path[0] = plan.Nodes[innNode].Position;
                    plan.Segments.Add(new PlannedSegment { NodeA = innNode, NodeB = joinNode, Points = path, Pass = true });
                    router.AddRoad(path);
                    result.Routed = true;
                    result.Join = plan.Nodes[joinNode].Position;
                    for (int i = 1; i < path.Count; i++) result.Length += RoadPlanner.PlanarDistance(path[i - 1], path[i]);
                    break;
                }
            }

            bool any = false;
            foreach (PassSide side in pass.Sides) any |= side.Routed;
            if (!any)
            {
                pass.SkipReason = "no grade-limited road reaches the lowland from the col";
                RemoveSettlement(plan, pass.InnSite);
                pass.InnSite = -1;
            }
        }

        /// <summary>
        /// Lowland roads whose end nodes are too far apart in height for their length (they would exceed
        /// <see cref="WorldGenParams.LowlandRoadGrade"/> however they were profiled, e.g. up to a hill village) are laid out again
        /// with the grade-limited <see cref="PassRouter"/> between the same nodes. Returns how many were replaced.
        /// </summary>
        public static int RelayoutSteepRoads(WorldGenParams p, Relief relief, RoadPlan plan)
        {
            int replaced = 0;
            const float Grade = WorldGenParams.LowlandRoadGrade;
            for (int i = 0; i < plan.Segments.Count; i++)
            {
                PlannedSegment segment = plan.Segments[i];
                if (segment.Pass) continue;
                float length = 0f;
                for (int k = 1; k < segment.Points.Count; k++) length += RoadPlanner.PlanarDistance(segment.Points[k - 1], segment.Points[k]);
                Vector3 a = plan.Nodes[segment.NodeA].Position;
                Vector3 b = plan.Nodes[segment.NodeB].Position;
                if (Mathf.Abs(relief.Height(a.x, a.z) - relief.Height(b.x, b.z)) <= 0.9f * Grade * length) continue;

                var router = new PassRouter(p, relief, Grade);
                int siteA = plan.Nodes[segment.NodeA].SettlementIndex;
                int siteB = plan.Nodes[segment.NodeB].SettlementIndex;
                for (int s = 0; s < plan.Settlements.Count; s++)
                {
                    if (s != siteA && s != siteB) router.AddKeepOut(plan.Settlements[s].Position, plan.Settlements[s].Radius + 12f);
                }

                for (int j = 0; j < plan.Segments.Count; j++)
                {
                    if (j != i) router.AddRoad(plan.Segments[j].Points);
                }

                List<Vector3> path = router.Route(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
                if (path == null || path.Count < 2) continue;
                path[0] = new Vector3(a.x, 0f, a.z);
                path[path.Count - 1] = new Vector3(b.x, 0f, b.z);
                segment.Points = path;
                replaced++;
            }

            return replaced;
        }

        // ------------------------------------------------------------------ col

        /// <summary>
        /// The col between the two summits beside the nominal saddle: on a coarse copy of the heightmap, cells are added from
        /// the highest down (union-find) until the two summits' regions meet; the meeting cell is the col.
        /// </summary>
        public static void FindCol(Relief relief, PassRecord record)
        {
            WorldGenParams p = relief.Params;
            MountainRange range = relief.Mask.Range;
            float[,] map = relief.Map;
            int n = map.GetLength(0);
            float spacing = p.worldSize / (n - 1);
            int stride = Mathf.Max(1, Mathf.RoundToInt(DrainageStep / spacing));
            float cs = spacing * stride;
            int m = (n - 1) / stride + 1;

            int PeakCell(Vector2 nominal)
            {
                int best = -1;
                float bestH = float.NegativeInfinity;
                int r = Mathf.CeilToInt(200f / cs);
                int cx = Mathf.RoundToInt(nominal.x / cs);
                int cz = Mathf.RoundToInt(nominal.y / cs);
                for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(m - 1, cz + r); z++)
                {
                    for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(m - 1, cx + r); x++)
                    {
                        if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > r * r) continue;
                        float h = map[z * stride, x * stride];
                        if (h > bestH)
                        {
                            bestH = h;
                            best = z * m + x;
                        }
                    }
                }

                return best;
            }

            int a = PeakCell(range.PeakBeforeSaddle);
            int b = PeakCell(range.PeakAfterSaddle);
            Vector2 nominal = range.Saddle;
            record.Col = new Vector3(nominal.x, relief.Height(nominal.x, nominal.y), nominal.y);
            Vector3 CellPos(int c) => new Vector3(c % m * cs, map[c / m * stride, c % m * stride] * p.hillAmplitude, c / m * cs);
            record.PeakBefore = CellPos(a);
            record.PeakAfter = CellPos(b);
            Vector3 along = record.PeakAfter - record.PeakBefore;
            along.y = 0f;
            along = along.sqrMagnitude > 1f ? along.normalized : new Vector3(range.Direction.x, 0f, range.Direction.y);
            record.RidgeNormal = new Vector3(-along.z, 0f, along.x);
            if (a < 0 || b < 0 || RoadPlanner.PlanarDistance(record.PeakBefore, record.PeakAfter) < 120f)
            {
                // The summit search found the same top twice: use the spine's direction instead.
                along = new Vector3(range.Direction.x, 0f, range.Direction.y);
                record.RidgeNormal = new Vector3(-along.z, 0f, along.x);
            }

            // The col: along the ridge through the nominal saddle, the crest (highest point across the ridge) is lowest here.
            var tangent = new Vector3(along.x, 0f, along.z);
            var across = record.RidgeNormal;
            var saddleWorld = new Vector3(range.Saddle.x, 0f, range.Saddle.y);
            float lowestCrest = float.PositiveInfinity;
            for (float v = -140f; v <= 140f; v += 6f)
            {
                float crest = float.NegativeInfinity;
                Vector3 crestPoint = saddleWorld;
                for (float u = -150f; u <= 150f; u += 6f)
                {
                    Vector3 q = saddleWorld + tangent * v + across * u;
                    if (q.x < 0f || q.z < 0f || q.x > p.worldSize || q.z > p.worldSize) continue;
                    float hq = relief.Height(q.x, q.z);
                    if (hq <= crest) continue;
                    crest = hq;
                    crestPoint = q;
                }

                if (crest >= lowestCrest || float.IsNegativeInfinity(crest)) continue;
                lowestCrest = crest;
                record.Col = new Vector3(crestPoint.x, crest, crestPoint.z);
            }

            if (!float.IsPositiveInfinity(lowestCrest)) return;

            // Fallback: union-find inside a box around both summits.
            int x0 = Mathf.Max(0, Mathf.Min(a % m, b % m) - Mathf.CeilToInt(350f / cs));
            int x1 = Mathf.Min(m - 1, Mathf.Max(a % m, b % m) + Mathf.CeilToInt(350f / cs));
            int z0 = Mathf.Max(0, Mathf.Min(a / m, b / m) - Mathf.CeilToInt(350f / cs));
            int z1 = Mathf.Min(m - 1, Mathf.Max(a / m, b / m) + Mathf.CeilToInt(350f / cs));
            var cells = new List<int>();
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++) cells.Add(z * m + x);
            }

            cells.Sort((c1, c2) =>
            {
                int cmp = map[c2 / m * stride, c2 % m * stride].CompareTo(map[c1 / m * stride, c1 % m * stride]);
                return cmp != 0 ? cmp : c1.CompareTo(c2);
            });
            var parent = new Dictionary<int, int>(cells.Count);
            int Find(int c)
            {
                while (parent[c] != c)
                {
                    parent[c] = parent[parent[c]];
                    c = parent[c];
                }

                return c;
            }

            foreach (int c in cells)
            {
                parent[c] = c;
                int cx = c % m;
                int cz = c / m;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dz == 0) || cx + dx < x0 || cx + dx > x1 || cz + dz < z0 || cz + dz > z1) continue;
                        int k = (cz + dz) * m + cx + dx;
                        if (!parent.ContainsKey(k)) continue;
                        int ra = Find(c);
                        int rb = Find(k);
                        if (ra != rb) parent[rb] = ra;
                    }
                }

                if (parent.ContainsKey(a) && parent.ContainsKey(b) && Find(a) == Find(b))
                {
                    record.Col = CellPos(c);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ routing helpers

        /// <summary>
        /// Where the pass valley on the given side reaches the lowland: along that <see cref="MountainRange.Valley"/> (the one
        /// heading along <paramref name="normal"/>), the first point below <paramref name="stopHeight"/>; else a steepest descent.
        /// </summary>
        private static Vector3 ValleyMouth(Relief relief, PassRouter router, PassRecord pass, Vector3 normal, float stopHeight)
        {
            foreach (MountainRange.Valley valley in relief.Mask.Range.Valleys)
            {
                if (valley.Points.Length < 3) continue;
                Vector2 dir = valley.Points[2] - valley.Points[0];
                if (dir.x * normal.x + dir.y * normal.z <= 0f) continue;
                foreach (Vector2 point in valley.Points)
                {
                    if (router.Height(point.x, point.y) < stopHeight) return new Vector3(point.x, 0f, point.y);
                }

                Vector2 end = valley.Points[valley.Points.Length - 1];
                return new Vector3(end.x, 0f, end.y);
            }

            return Descend(router, pass.Col + normal * 40f, stopHeight, relief.Params.worldSize);
        }

        /// <summary>Follows the steepest descent from <paramref name="start"/> until the ground is below <paramref name="stopHeight"/>.</summary>
        private static Vector3 Descend(PassRouter router, Vector3 start, float stopHeight, float worldSize)
        {
            Vector3 pos = start;
            Vector3 heading = Vector3.zero;
            for (int i = 0; i < 300; i++)
            {
                float h = router.Height(pos.x, pos.z);
                if (h < stopHeight) break;
                float gx = router.Height(pos.x + 6f, pos.z) - router.Height(pos.x - 6f, pos.z);
                float gz = router.Height(pos.x, pos.z + 6f) - router.Height(pos.x, pos.z - 6f);
                var down = new Vector3(-gx, 0f, -gz);
                if (down.sqrMagnitude < 1e-6f) down = heading;
                if (down.sqrMagnitude < 1e-6f) break;
                heading = (heading * 0.5f + down.normalized).normalized;   // a little momentum carries it over small hollows
                Vector3 next = pos + heading * DrainageStep;
                if (next.x < 20f || next.z < 20f || next.x > worldSize - 20f || next.z > worldSize - 20f) break;
                pos = next;
            }

            pos.y = 0f;
            return pos;
        }

        private struct JoinCandidate
        {
            public Vector3 Position;
            public int Segment;     // -1 for a node
            public int PointIndex;
            public int Node;        // node index when Segment < 0
        }

        /// <summary>Points of the lowland network on the given side, nearest the valley mouth first, at most <c>maxCount</c>, 120 m apart.</summary>
        private static List<JoinCandidate> JoinCandidates(RoadPlan plan, PassRouter router, PassRecord pass, Vector3 normal, Vector3 mouth, float amplitude, int maxCount)
        {
            var all = new List<(float score, JoinCandidate candidate)>();
            bool OnSide(Vector3 pos)
            {
                Vector3 offset = pos - pass.Col;
                offset.y = 0f;
                return Vector3.Dot(offset, normal) > 80f && router.Height(pos.x, pos.z) < 0.45f * amplitude;
            }

            float Score(Vector3 pos) => RoadPlanner.PlanarDistance(pos, mouth) + 0.25f * RoadPlanner.PlanarDistance(pos, pass.Col);

            var connectedNodes = new List<Vector3>();
            for (int nIndex = 0; nIndex < plan.Nodes.Count; nIndex++)
            {
                foreach (PlannedSegment segment in plan.Segments)
                {
                    if (segment.NodeA != nIndex && segment.NodeB != nIndex) continue;
                    connectedNodes.Add(plan.Nodes[nIndex].Position);
                    break;
                }
            }

            for (int s = 0; s < plan.Segments.Count; s++)
            {
                PlannedSegment segment = plan.Segments[s];
                if (segment.Pass) continue;
                for (int i = 4; i < segment.Points.Count - 4; i += 5)
                {
                    Vector3 pos = segment.Points[i];
                    if (!OnSide(pos)) continue;
                    bool nearNode = false;
                    foreach (Vector3 node in connectedNodes) nearNode |= RoadPlanner.PlanarDistance(node, pos) < 35f;
                    if (nearNode) continue;   // the node itself is a candidate; a split this close would make a stub
                    if (i < 12 || i > segment.Points.Count - 13) continue;
                    Vector3 before = segment.Points[i] - segment.Points[i - 12];
                    Vector3 after = segment.Points[i + 12] - segment.Points[i];
                    before.y = after.y = 0f;
                    if (Vector3.Angle(before, after) > 35f) continue;   // join on a straight-ish stretch so the angle reads clearly
                    all.Add((Score(pos), new JoinCandidate { Position = pos, Segment = s, PointIndex = i, Node = -1 }));
                }
            }

            for (int nIndex = 0; nIndex < plan.Nodes.Count; nIndex++)
            {
                PlannedNode node = plan.Nodes[nIndex];
                if (node.SettlementIndex == pass.InnSite) continue;
                bool connected = false;
                foreach (PlannedSegment segment in plan.Segments) connected |= segment.NodeA == nIndex || segment.NodeB == nIndex;
                if (!connected || !OnSide(node.Position)) continue;
                all.Add((Score(node.Position), new JoinCandidate { Position = node.Position, Segment = -1, Node = nIndex }));
            }

            all.Sort((c1, c2) =>
            {
                int cmp = c1.score.CompareTo(c2.score);
                if (cmp != 0) return cmp;
                cmp = c1.candidate.Segment.CompareTo(c2.candidate.Segment);
                return cmp != 0 ? cmp : c1.candidate.PointIndex.CompareTo(c2.candidate.PointIndex);
            });
            var picked = new List<JoinCandidate>();
            foreach (var (_, candidate) in all)
            {
                bool far = true;
                foreach (JoinCandidate other in picked) far &= RoadPlanner.PlanarDistance(other.Position, candidate.Position) >= 120f;
                if (!far) continue;
                picked.Add(candidate);
                if (picked.Count >= maxCount) break;
            }

            return picked;
        }

        /// <summary>
        /// Across the lowland road at a mid-road join, pointing from the pass side towards the road (so the pass road meets it
        /// at a proper angle instead of running alongside); none for a join at a node.
        /// </summary>
        private static Vector3 ArrivalDirection(RoadPlan plan, JoinCandidate candidate, Vector3 from)
        {
            if (candidate.Segment < 0) return NodeArrival(plan, candidate.Node);
            List<Vector3> points = plan.Segments[candidate.Segment].Points;
            int a = Mathf.Max(0, candidate.PointIndex - 10);
            int b = Mathf.Min(points.Count - 1, candidate.PointIndex + 10);
            Vector3 tangent = points[b] - points[a];
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 1e-4f) return Vector3.zero;
            tangent.Normalize();
            var across = new Vector3(tangent.z, 0f, -tangent.x);
            Vector3 toRoad = candidate.Position - from;
            toRoad.y = 0f;
            return Vector3.Dot(across, toRoad) >= 0f ? across : -across;
        }

        /// <summary>
        /// <paramref name="arrival"/>, or it turned by up to 40° if needed, such that the straight approach of
        /// <see cref="ApproachLength"/> metres onto <paramref name="join"/> runs over even ground no steeper than about the
        /// design grade; zero when there is no such line (the road then comes in without a straight approach).
        /// </summary>
        private static Vector3 GentleArrival(PassRouter router, Vector3 join, Vector3 arrival, float grade)
        {
            if (arrival.sqrMagnitude < 0.5f) return Vector3.zero;
            foreach (float turn in new[] { 0f, 20f, -20f, 40f, -40f })
            {
                float c = Mathf.Cos(turn * Mathf.Deg2Rad);
                float sn = Mathf.Sin(turn * Mathf.Deg2Rad);
                var dir = new Vector3(arrival.x * c - arrival.z * sn, 0f, arrival.x * sn + arrival.z * c);   // managed maths only (pure stage)
                Vector3 start = join - dir * ApproachLength;
                float h0 = router.Height(start.x, start.z);
                float h1 = router.Height(join.x, join.z);
                if (Mathf.Abs(h0 - h1) > 1.2f * grade * ApproachLength) continue;
                bool even = true;
                for (float d = 5f; d < ApproachLength && even; d += 5f)
                {
                    Vector3 q = start + dir * d;
                    float line = Mathf.Lerp(h0, h1, d / ApproachLength);
                    even = Mathf.Abs(router.Height(q.x, q.z) - line) < 2.5f;
                }

                if (even) return dir;
            }

            return Vector3.zero;
        }

        /// <summary>Largest cut or fill (metres) a grade-limited profile along <paramref name="path"/> would need on the smoothed terrain.</summary>
        private static float EarthworkDepth(PassRouter router, List<Vector3> path, float grade)
        {
            int n = path.Count;
            var along = new float[n];
            var terrain = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i > 0) along[i] = along[i - 1] + RoadPlanner.PlanarDistance(path[i - 1], path[i]);
                terrain[i] = router.Height(path[i].x, path[i].z);
            }

            var profile = (float[])terrain.Clone();
            RoadPlanner.LimitGrade(profile, along, grade, terrain[0], terrain[n - 1]);
            float worst = 0f;
            for (int i = 0; i < n; i++) worst = Mathf.Max(worst, Mathf.Abs(profile[i] - terrain[i]));
            return worst;
        }

        /// <summary>
        /// True when <paramref name="path"/> keeps 14 m from its own earlier stretches (more than 70 m back along it) and 12 m
        /// from every other road, except within 60 m of <paramref name="join"/> and 45 m of its start (the inn).
        /// </summary>
        private static bool Clearance(List<Vector3> path, RoadPlan plan, Vector3 join)
        {
            var along = new float[path.Count];
            for (int i = 1; i < path.Count; i++) along[i] = along[i - 1] + RoadPlanner.PlanarDistance(path[i - 1], path[i]);
            for (int i = 0; i < path.Count; i += 2)
            {
                for (int j = i + 2; j < path.Count; j += 2)
                {
                    if (along[j] - along[i] < 70f) continue;
                    if (RoadPlanner.PlanarDistance(path[i], path[j]) < 14f) return false;
                }
            }

            foreach (PlannedSegment segment in plan.Segments)
            {
                for (int k = 0; k < segment.Points.Count; k += 2)
                {
                    Vector3 other = segment.Points[k];
                    if (RoadPlanner.PlanarDistance(other, join) < 60f || RoadPlanner.PlanarDistance(other, path[0]) < 45f) continue;
                    for (int i = 0; i < path.Count; i += 2)
                    {
                        if (RoadPlanner.PlanarDistance(other, path[i]) < 12f) return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Joins the end of a searched road (heading roughly along <paramref name="arrival"/>, within a few metres of the approach
        /// line) to <paramref name="join"/>: a Hermite blend onto the approach line about <see cref="BlendLength"/> metres long, then
        /// straight along <paramref name="arrival"/> to the join.
        /// </summary>
        private static void AppendApproach(List<Vector3> path, Vector3 join, Vector3 arrival)
        {
            Vector3 p0 = path[path.Count - 1];
            Vector3 heading = path.Count > 3 ? p0 - path[path.Count - 4] : arrival;
            heading.y = 0f;
            heading = heading.sqrMagnitude > 1e-4f ? heading.normalized : arrival;
            join.y = 0f;
            float along = Vector3.Dot(join - p0, arrival);   // distance left to the join along the approach line
            float blend = Mathf.Min(BlendLength, along * 0.6f);
            Vector3 q = join - arrival * (along - blend);
            float m = Vector3.Distance(p0, q);
            int steps = Mathf.Max(1, Mathf.CeilToInt(m));
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float t2 = t * t;
                float t3 = t2 * t;
                path.Add((2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m * heading + (-2f * t3 + 3f * t2) * q + (t3 - t2) * m * arrival);
            }

            float rest = Vector3.Distance(q, join);
            for (float d = 1f; d < rest; d += 1f) path.Add(q + arrival * d);
            path.Add(join);
            EaseCorners(path, PassRouter.MinRadius);
        }

        /// <summary>
        /// Rounds any corner of <paramref name="points"/> (about 1 m apart) tighter than <paramref name="radius"/> (measured over
        /// ±4 m) by repeatedly relaxing the points around it towards their neighbours' average; the ends stay put. Used where the
        /// searched road meets its straight final approach.
        /// </summary>
        public static void EaseCorners(List<Vector3> points, float radius)
        {
            const int Span = 4;
            int n = points.Count;
            for (int iteration = 0; iteration < 300; iteration++)
            {
                bool changed = false;
                for (int i = Span; i < n - Span; i++)
                {
                    if (Circumradius(points[i - Span], points[i], points[i + Span]) >= radius) continue;
                    for (int k = Mathf.Max(1, i - 2); k <= Mathf.Min(n - 2, i + 2); k++)
                    {
                        points[k] = Vector3.Lerp(points[k], (points[k - 1] + points[k + 1]) * 0.5f, 0.5f);
                    }

                    changed = true;
                }

                if (!changed) return;
            }
        }

        private static float Circumradius(Vector3 a, Vector3 b, Vector3 c)
        {
            float ab = RoadPlanner.PlanarDistance(a, b);
            float bc = RoadPlanner.PlanarDistance(b, c);
            float ca = RoadPlanner.PlanarDistance(c, a);
            float cross = Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x));
            return cross < 1e-6f ? float.PositiveInfinity : ab * bc * ca / (2f * cross);
        }

        /// <summary>Into the widest gap between the roads already at node <paramref name="node"/>, pointing at the node.</summary>
        private static Vector3 NodeArrival(RoadPlan plan, int node)
        {
            Vector3 centre = plan.Nodes[node].Position;
            var angles = new List<float>();
            foreach (PlannedSegment segment in plan.Segments)
            {
                if (segment.NodeA != node && segment.NodeB != node) continue;
                List<Vector3> points = segment.Points;
                int i = segment.NodeA == node ? Mathf.Min(points.Count - 1, 7) : Mathf.Max(0, points.Count - 8);
                Vector3 d = points[i] - centre;
                if (d.x * d.x + d.z * d.z > 1e-4f) angles.Add(Mathf.Atan2(d.z, d.x));
            }

            if (angles.Count == 0) return Vector3.zero;
            angles.Sort();
            float bestGap = -1f;
            float mid = 0f;
            for (int i = 0; i < angles.Count; i++)
            {
                float to = i + 1 < angles.Count ? angles[i + 1] : angles[0] + Mathf.PI * 2f;
                if (to - angles[i] <= bestGap) continue;
                bestGap = to - angles[i];
                mid = (angles[i] + to) * 0.5f;
            }

            return new Vector3(-Mathf.Cos(mid), 0f, -Mathf.Sin(mid));
        }

        /// <summary>Node index for a join: an existing node within 25 m, else the lowland segment is split at the point.</summary>
        private static int Join(RoadPlan plan, JoinCandidate candidate)
        {
            if (candidate.Segment < 0) return candidate.Node;
            for (int nIndex = 0; nIndex < plan.Nodes.Count; nIndex++)
            {
                if (RoadPlanner.PlanarDistance(plan.Nodes[nIndex].Position, candidate.Position) < 25f)
                {
                    bool connected = false;
                    foreach (PlannedSegment s in plan.Segments) connected |= s.NodeA == nIndex || s.NodeB == nIndex;
                    if (connected) return nIndex;
                }
            }

            PlannedSegment segment = plan.Segments[candidate.Segment];
            int k = candidate.PointIndex;
            if (k < 2 || k > segment.Points.Count - 3) return -1;
            int node = plan.Nodes.Count;
            Vector3 at = segment.Points[k];
            plan.Nodes.Add(new PlannedNode { Position = new Vector3(at.x, 0f, at.z) });
            var tail = segment.Points.GetRange(k, segment.Points.Count - k);
            var second = new PlannedSegment { NodeA = node, NodeB = segment.NodeB, Points = tail, Pass = segment.Pass };
            segment.Points = segment.Points.GetRange(0, k + 1);
            segment.NodeB = node;
            plan.Segments.Add(second);
            return node;
        }

        /// <summary>Removes settlement <paramref name="index"/> and its (unconnected) node from the plan, renumbering the rest.</summary>
        private static void RemoveSettlement(RoadPlan plan, int index)
        {
            int node = plan.Nodes.FindIndex(n => n.SettlementIndex == index);
            plan.Settlements.RemoveAt(index);
            foreach (PlannedNode n in plan.Nodes)
            {
                if (n.SettlementIndex > index) n.SettlementIndex--;
            }

            if (node < 0) return;
            plan.Segments.RemoveAll(s => s.NodeA == node || s.NodeB == node);
            plan.Nodes.RemoveAt(node);
            foreach (PlannedSegment s in plan.Segments)
            {
                if (s.NodeA > node) s.NodeA--;
                if (s.NodeB > node) s.NodeB--;
            }
        }
    }
}
