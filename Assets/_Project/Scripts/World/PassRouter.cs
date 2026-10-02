using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Lays out a mountain road with a hybrid A* search over (position, heading): the road advances in 5 m arcs of fixed
    /// curvature (straight, gentle, medium and hairpin bends, radius never below <see cref="MinRadius"/>), so the result is
    /// a smooth drivable curve rather than a grid path. Each arc must climb no steeper than the design grade on the
    /// (lightly smoothed) terrain; only the tight hairpin arcs may cross steeper ground (up to <see cref="HairpinGrade"/>),
    /// which the road profile later absorbs with a little cut and fill. On a face steeper than the design grade the
    /// search therefore has to traverse and turn back: switchbacks. Costs favour gentle grades, little side slope (less
    /// benching), few bends; water, other settlements, the map border and existing roads (except near the goal) are
    /// off-limits. The heuristic is max(distance, height difference / design grade). Deterministic; pure C#.
    /// </summary>
    public sealed class PassRouter
    {
        /// <summary>Arc length of one step, metres.</summary>
        public const float Step = 5f;

        /// <summary>Tightest bend radius, metres (hairpins).</summary>
        public const float MinRadius = 8f;

        /// <summary>Grade the tight hairpin arcs may cross on the terrain (the profile evens it out).</summary>
        public const float HairpinGrade = 0.3f;

        /// <summary>Minimum distance between switchback legs (centre lines), metres.</summary>
        public const float LegSeparation = 20f;

        /// <summary>Steps of road just behind a pose that are exempt from <see cref="LegSeparation"/> (covers a hairpin).</summary>
        private const int SeparationSkip = 12;

        /// <summary>Steps of road checked for <see cref="LegSeparation"/> beyond the exempt ones.</summary>
        private const int SeparationSpan = 72;

        /// <summary>Weight on the heuristic (greater than 1 trades a little optimality for a much smaller search).</summary>
        private const float HeuristicWeight = 1.6f;

        /// <summary>Hard cap on expanded states per search.</summary>
        public const int MaxExpansions = 450000;

        private const int Headings = 16;
        private const float Cell = 3f;
        private const float HeightCell = 4f;
        private const float BorderMargin = 25f;

        private static readonly float[] Curvatures = { 0f, 1f / 40f, -1f / 40f, 1f / 16f, -1f / 16f, 1f / MinRadius, -1f / MinRadius };
        private static readonly float[] TurnSin = Array.ConvertAll(Curvatures, k => Mathf.Sin(k * Step));
        private static readonly float[] TurnCos = Array.ConvertAll(Curvatures, k => Mathf.Cos(k * Step));

        private readonly WorldGenParams p;
        private readonly float waterHeight;
        private readonly float designGrade;
        private readonly float[,] heights;   // smoothed terrain, HeightCell spacing
        private readonly float[] gradX;      // its gradient (rise over run), per HeightCell vertex
        private readonly float[] gradZ;
        private readonly int heightRes;
        private readonly List<(Vector2 centre, float radius)> keepOut = new List<(Vector2, float)>();
        private readonly List<Vector3> roadPoints = new List<Vector3>();

        /// <summary>Creates a router over <paramref name="relief"/> with the given design grade (rise over run).</summary>
        public PassRouter(WorldGenParams p, Relief relief, float designGrade)
        {
            this.p = p ?? throw new ArgumentNullException(nameof(p));
            if (relief == null) throw new ArgumentNullException(nameof(relief));
            this.designGrade = Mathf.Clamp(designGrade, 0.03f, 0.3f);
            waterHeight = relief.HasWater ? relief.WaterHeight : float.NegativeInfinity;

            // Terrain sampled every 4 m and box-blurred over ~12 m: the road follows the lie of the land, not every bump.
            heightRes = Mathf.Max(2, Mathf.CeilToInt(p.worldSize / HeightCell) + 1);
            var raw = new float[heightRes, heightRes];
            float spacing = p.worldSize / (heightRes - 1);
            for (int z = 0; z < heightRes; z++)
            {
                for (int x = 0; x < heightRes; x++) raw[z, x] = relief.Height(x * spacing, z * spacing);
            }

            heights = BoxBlur(raw, 1);
            gradX = new float[heightRes * heightRes];
            gradZ = new float[heightRes * heightRes];
            for (int z = 0; z < heightRes; z++)
            {
                int z0 = Mathf.Max(0, z - 1);
                int z1 = Mathf.Min(heightRes - 1, z + 1);
                for (int x = 0; x < heightRes; x++)
                {
                    int x0 = Mathf.Max(0, x - 1);
                    int x1 = Mathf.Min(heightRes - 1, x + 1);
                    gradX[z * heightRes + x] = (heights[z, x1] - heights[z, x0]) / ((x1 - x0) * spacing);
                    gradZ[z * heightRes + x] = (heights[z1, x] - heights[z0, x]) / ((z1 - z0) * spacing);
                }
            }
        }

        /// <summary>Side slope (rise over run across the heading with the given sine / cosine) at world (x, z), nearest vertex.</summary>
        private float SideSlope(float x, float z, float sin, float cos)
        {
            float s = (heightRes - 1) / p.worldSize;
            int gx = Mathf.Clamp(Mathf.RoundToInt(x * s), 0, heightRes - 1);
            int gz = Mathf.Clamp(Mathf.RoundToInt(z * s), 0, heightRes - 1);
            int i = gz * heightRes + gx;
            return Mathf.Abs(-sin * gradX[i] + cos * gradZ[i]);
        }

        /// <summary>Metres of height per step the search allows on straight and gentle arcs.</summary>
        public float DesignGrade => designGrade;

        /// <summary>Forbids the road within <paramref name="radius"/> of <paramref name="centre"/> (other settlements).</summary>
        public void AddKeepOut(Vector3 centre, float radius) => keepOut.Add((new Vector2(centre.x, centre.z), radius));

        /// <summary>Registers existing road centre-line points; the new road keeps away from them except near its goal.</summary>
        public void AddRoad(IReadOnlyList<Vector3> points)
        {
            for (int i = 0; i < points.Count; i++) roadPoints.Add(points[i]);
        }

        /// <summary>Number of registered road points (see <see cref="TruncateRoads"/>).</summary>
        public int RoadPointCount => roadPoints.Count;

        /// <summary>Forgets road points registered after the first <paramref name="count"/>.</summary>
        public void TruncateRoads(int count)
        {
            if (count < roadPoints.Count) roadPoints.RemoveRange(count, roadPoints.Count - count);
        }

        /// <summary>Smoothed terrain height at world (x, z).</summary>
        public float Height(float x, float z)
        {
            float s = (heightRes - 1) / p.worldSize;
            return HeightmapGenerator.SampleGrid(heights, x * s, z * s);
        }

        /// <summary>Expanded-state budget of the next <see cref="Route"/> call (capped at <see cref="MaxExpansions"/>).</summary>
        public int ExpansionLimit { get; set; } = MaxExpansions;

        /// <summary>Statistics of the last search.</summary>
        public int LastExpansions { get; private set; }

        /// <summary>
        /// Searches a road from <paramref name="from"/> to within 7 m of <paramref name="to"/>, inside the bounding box of both
        /// grown by <paramref name="margin"/> metres. With a <paramref name="startDirection"/> the road must set off within
        /// <paramref name="startCone"/> degrees of it (the two sides of a pass leave the inn in opposite directions); otherwise
        /// in any direction. With an <paramref name="arrivalDirection"/> the road must reach the goal heading within
        /// <paramref name="arrivalCone"/> degrees of it (meeting the lowland road at a proper angle). <paramref name="tail"/> is road
        /// that will follow the goal (a straight approach ending at <paramref name="tailEnd"/>); it counts towards the length
        /// needed for the climb, which is measured to the tail's end. Returns the centre
        /// line (y = 0, about 1 m spacing, ending exactly on <paramref name="to"/>, or with an arrival direction at the final pose
        /// within 7 m of it) or null when no road satisfies the constraints.
        /// </summary>
        public List<Vector3> Route(Vector3 from, Vector3 to, float margin = 260f, Vector3 startDirection = default, float startCone = 80f,
            Vector3 arrivalDirection = default, float arrivalCone = 55f, float tail = 0f, Vector3 tailEnd = default)
        {
            float minX = Mathf.Max(BorderMargin, Mathf.Min(from.x, to.x) - margin);
            float minZ = Mathf.Max(BorderMargin, Mathf.Min(from.z, to.z) - margin);
            float maxX = Mathf.Min(p.worldSize - BorderMargin, Mathf.Max(from.x, to.x) + margin);
            float maxZ = Mathf.Min(p.worldSize - BorderMargin, Mathf.Max(from.z, to.z) + margin);
            int w = Mathf.Max(2, Mathf.CeilToInt((maxX - minX) / Cell));
            int h = Mathf.Max(2, Mathf.CeilToInt((maxZ - minZ) / Cell));
            int stateCount = w * h * Headings;

            var g = new float[stateCount];
            var parent = new int[stateCount];
            var ancestor3 = new int[stateCount];   // three steps back along the road (for the leg-separation check)
            var poseX = new float[stateCount];
            var poseZ = new float[stateCount];
            var poseA = new float[stateCount];
            var arc = new sbyte[stateCount];
            var travelled = new float[stateCount];
            var state = new byte[stateCount];   // 0 unseen, 1 open, 2 closed
            var heap = new StateHeap(1 << 14);

            // Blocked cells: water, other settlements, existing roads (away from the goal).
            var blocked = new bool[w * h];
            const float RoadClearance = 18f;
            const float GoalFreedom = 22f;
            const float StartFreedom = 40f;
            for (int cz = 0; cz < h; cz++)
            {
                float wz = minZ + (cz + 0.5f) * Cell;
                for (int cx = 0; cx < w; cx++)
                {
                    float wx = minX + (cx + 0.5f) * Cell;
                    if (Height(wx, wz) < waterHeight + 0.8f) blocked[cz * w + cx] = true;
                    foreach (var (centre, radius) in keepOut)
                    {
                        float dx = wx - centre.x;
                        float dz = wz - centre.y;
                        if (dx * dx + dz * dz < radius * radius) blocked[cz * w + cx] = true;
                    }
                }
            }

            int stamp = Mathf.CeilToInt(RoadClearance / Cell);
            foreach (Vector3 pt in roadPoints)
            {
                if (Planar(pt, to) < GoalFreedom + RoadClearance || Planar(pt, from) < StartFreedom) continue;
                int px = Mathf.FloorToInt((pt.x - minX) / Cell);
                int pz = Mathf.FloorToInt((pt.z - minZ) / Cell);
                for (int dz = -stamp; dz <= stamp; dz++)
                {
                    for (int dx = -stamp; dx <= stamp; dx++)
                    {
                        int x = px + dx;
                        int z = pz + dz;
                        if (x < 0 || z < 0 || x >= w || z >= h) continue;
                        float ox = minX + (x + 0.5f) * Cell - pt.x;
                        float oz = minZ + (z + 0.5f) * Cell - pt.z;
                        if (ox * ox + oz * oz > RoadClearance * RoadClearance) continue;
                        if (Planar(new Vector3(minX + (x + 0.5f) * Cell, 0f, minZ + (z + 0.5f) * Cell), to) < GoalFreedom) continue;
                        blocked[z * w + x] = true;
                    }
                }
            }

            float goalHeight = Height(to.x, to.z);
            float endHeight = tail > 0f ? Height(tailEnd.x, tailEnd.z) : goalHeight;
            float climb = Mathf.Abs(Height(from.x, from.z) - endHeight);

            // Heuristic: road length still needed from each place to the goal, from a coarse Dijkstra run backwards from the
            // goal around blocked ground, where a step climbing steeper than the design grade counts as long as the switchbacks
            // it would need (length × grade / design grade). Much better informed than the straight-line distance.
            int hw = Mathf.Max(2, Mathf.CeilToInt((maxX - minX) / HeuristicCell) + 1);
            int hh = Mathf.Max(2, Mathf.CeilToInt((maxZ - minZ) / HeuristicCell) + 1);
            float[,] toGo = CostToGo(to, minX, minZ, hw, hh, cell => blocked[cell], w, h);
            float fromCost = HeuristicGrid(toGo, minX, minZ, from.x, from.z);
            if (float.IsPositiveInfinity(fromCost) || fromCost > 1e8f)
            {
                LastExpansions = 0;
                return null;   // the goal cannot be reached around the blocked ground at all
            }

            float Heuristic(float x, float z) => HeuristicWeight * HeuristicGrid(toGo, minX, minZ, x, z);

            // Switchback legs must not crowd each other: a new pose keeps LegSeparation from the road laid more than
            // SeparationSkip steps before it (the hairpin itself is exempt).
            bool Crowded(int s, float x, float z)
            {
                int a = s;
                for (int k = 0; k < SeparationSkip && a >= 0; k += 3) a = ancestor3[a];
                for (int k = 0; k < SeparationSpan && a >= 0; k += 3)
                {
                    float dx = poseX[a] - x;
                    float dz = poseZ[a] - z;
                    if (dx * dx + dz * dz < LegSeparation * LegSeparation) return true;
                    a = ancestor3[a];
                }

                return false;
            }

            int Index(float x, float z, float angle)
            {
                int cx = Mathf.FloorToInt((x - minX) / Cell);
                int cz = Mathf.FloorToInt((z - minZ) / Cell);
                if (cx < 0 || cz < 0 || cx >= w || cz >= h) return -1;
                int bin = HeadingBin(angle);
                return (cz * w + cx) * Headings + bin;
            }

            bool arrive = arrivalDirection.sqrMagnitude > 1e-6f;
            float arrivalAngle = Mathf.Atan2(arrivalDirection.z, arrivalDirection.x) * Mathf.Rad2Deg;
            bool cone = startDirection.sqrMagnitude > 1e-6f;
            float coneAngle = Mathf.Atan2(startDirection.z, startDirection.x);
            for (int b = 0; b < Headings; b++)
            {
                float angle = b * Mathf.PI * 2f / Headings;
                if (cone && Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, coneAngle * Mathf.Rad2Deg)) > startCone) continue;
                int s = Index(from.x, from.z, angle);
                if (s < 0) return null;
                g[s] = 0f;
                parent[s] = -1;
                ancestor3[s] = -1;
                poseX[s] = from.x;
                poseZ[s] = from.z;
                poseA[s] = angle;
                arc[s] = -1;
                travelled[s] = 0f;
                state[s] = 1;
                heap.Push(s, Heuristic(from.x, from.z));
            }

            int goal = -1;
            int expansions = 0;
            int limit = Mathf.Clamp(ExpansionLimit, 1, MaxExpansions);
            while (heap.Count > 0 && expansions < limit)
            {
                int s = heap.Pop();
                if (state[s] == 2) continue;
                state[s] = 2;
                expansions++;
                float x0 = poseX[s];
                float z0 = poseZ[s];
                float a0 = poseA[s];
                // Goal: close enough, facing the right way, and long enough that a profile at the design grade can span the
                // height difference (hairpin arcs may cross steeper ground, so this is not implied by the per-arc limits).
                if (Planar(new Vector3(x0, 0f, z0), to) <= 7f &&
                    (!arrive || Mathf.Abs(Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, arrivalAngle)) <= arrivalCone) &&
                    travelled[s] + Planar(new Vector3(x0, 0f, z0), to) + tail >= 1.1f * climb / designGrade + 15f)
                {
                    goal = s;
                    break;
                }

                float h0 = Height(x0, z0);
                float sin0 = Mathf.Sin(a0);
                float cos0 = Mathf.Cos(a0);
                for (int k = 0; k < Curvatures.Length; k++)
                {
                    float kappa = Curvatures[k];
                    float sin1 = sin0 * TurnCos[k] + cos0 * TurnSin[k];
                    float cos1 = cos0 * TurnCos[k] - sin0 * TurnSin[k];
                    float a1 = a0 + kappa * Step;
                    float x1 = kappa == 0f ? x0 + cos0 * Step : x0 + (sin1 - sin0) / kappa;
                    float z1 = kappa == 0f ? z0 + sin0 * Step : z0 - (cos1 - cos0) / kappa;
                    int next = Index(x1, z1, a1);
                    if (next < 0 || state[next] == 2) continue;
                    if (blocked[next / Headings]) continue;
                    if (Crowded(s, x1, z1)) continue;
                    float h1 = Height(x1, z1);
                    float grade = (h1 - h0) / Step;
                    bool hairpin = Mathf.Abs(kappa) > 0.1f;
                    if (Mathf.Abs(grade) > (hairpin ? HairpinGrade : designGrade + 0.005f)) continue;

                    // Side slope (benching effort) at the end of the arc.
                    float side = SideSlope(x1, z1, sin1, cos1);

                    float r = grade / designGrade;
                    float cost = Step * (1f + 1.5f * r * r + 3f * side * side);
                    if (hairpin && Mathf.Abs(grade) > designGrade) cost += Step * 12f * (Mathf.Abs(grade) - designGrade);   // cut and fill to come
                    if (kappa != 0f) cost += Step * (hairpin ? 0.9f : Mathf.Abs(kappa) > 0.05f ? 0.2f : 0.04f) + (hairpin ? 1.5f : 0f);
                    float candidate = g[s] + cost;
                    if (state[next] == 1 && candidate >= g[next]) continue;
                    g[next] = candidate;
                    parent[next] = s;
                    ancestor3[next] = parent[s] >= 0 ? parent[parent[s]] : -1;
                    poseX[next] = x1;
                    poseZ[next] = z1;
                    poseA[next] = a1;
                    arc[next] = (sbyte)k;
                    travelled[next] = travelled[s] + Step;
                    state[next] = 1;
                    heap.Push(next, candidate + Heuristic(x1, z1));
                }
            }

            LastExpansions = expansions;
            if (goal < 0) return null;

            var chain = new List<int>();
            for (int s = goal; s >= 0; s = parent[s]) chain.Add(s);
            chain.Reverse();
            var points = new List<Vector3> { new Vector3(poseX[chain[0]], 0f, poseZ[chain[0]]) };
            for (int i = 1; i < chain.Count; i++)
            {
                int prev = chain[i - 1];
                float kappa = Curvatures[arc[chain[i]]];
                for (int sub = 1; sub <= 5; sub++)
                {
                    Advance(poseX[prev], poseZ[prev], poseA[prev], kappa, Step * sub / 5f, out float x, out float z, out _);
                    points.Add(new Vector3(x, 0f, z));
                }
            }

            if (!arrive) points.Add(new Vector3(to.x, 0f, to.z));   // with an arrival direction the caller joins the end smoothly
            return points;
        }

        private const float HeuristicCell = 6f;

        private float[,] CostToGo(Vector3 goal, float minX, float minZ, int hw, int hh, Func<int, bool> blockedCell, int w, int h)
        {
            var cost = new float[hh, hw];
            for (int z = 0; z < hh; z++)
            {
                for (int x = 0; x < hw; x++) cost[z, x] = float.PositiveInfinity;
            }

            var heights = new float[hh * hw];
            var blockedCoarse = new bool[hh * hw];
            for (int z = 0; z < hh; z++)
            {
                for (int x = 0; x < hw; x++)
                {
                    float wx = minX + x * HeuristicCell;
                    float wz = minZ + z * HeuristicCell;
                    heights[z * hw + x] = Height(wx, wz);
                    int fx = Mathf.Clamp(Mathf.FloorToInt((wx - minX) / Cell), 0, w - 1);
                    int fz = Mathf.Clamp(Mathf.FloorToInt((wz - minZ) / Cell), 0, h - 1);
                    blockedCoarse[z * hw + x] = blockedCell(fz * w + fx);
                }
            }

            int gx = Mathf.Clamp(Mathf.RoundToInt((goal.x - minX) / HeuristicCell), 0, hw - 1);
            int gz = Mathf.Clamp(Mathf.RoundToInt((goal.z - minZ) / HeuristicCell), 0, hh - 1);
            var heap = new StateHeap(1024);
            cost[gz, gx] = 0f;
            heap.Push(gz * hw + gx, 0f);
            var done = new bool[hh * hw];
            while (heap.Count > 0)
            {
                int c = heap.Pop();
                if (done[c]) continue;
                done[c] = true;
                int cx = c % hw;
                int cz = c / hw;
                float cc = cost[cz, cx];
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx;
                        int z = cz + dz;
                        if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= hw || z >= hh) continue;
                        int k = z * hw + x;
                        if (done[k] || blockedCoarse[k]) continue;
                        float d = (dx != 0 && dz != 0 ? 1.41421356f : 1f) * HeuristicCell;
                        float grade = Mathf.Abs(heights[k] - heights[c]) / d;
                        float step = d * Mathf.Max(1f, grade / designGrade);
                        float candidate = cc + step;
                        if (candidate >= cost[z, x]) continue;
                        cost[z, x] = candidate;
                        heap.Push(k, candidate);
                    }
                }
            }

            return cost;
        }

        private static float HeuristicGrid(float[,] cost, float minX, float minZ, float x, float z)
        {
            int hh = cost.GetLength(0);
            int hw = cost.GetLength(1);
            int cx = Mathf.Clamp(Mathf.RoundToInt((x - minX) / HeuristicCell), 0, hw - 1);
            int cz = Mathf.Clamp(Mathf.RoundToInt((z - minZ) / HeuristicCell), 0, hh - 1);
            float v = cost[cz, cx];
            return float.IsPositiveInfinity(v) ? 1e9f : v;
        }

        /// <summary>End pose of an arc of <paramref name="length"/> with curvature <paramref name="kappa"/> (heading = angle from +X towards +Z).</summary>
        public static void Advance(float x, float z, float angle, float kappa, float length, out float x1, out float z1, out float a1)
        {
            if (Mathf.Abs(kappa) < 1e-6f)
            {
                x1 = x + Mathf.Cos(angle) * length;
                z1 = z + Mathf.Sin(angle) * length;
                a1 = angle;
                return;
            }

            a1 = angle + kappa * length;
            x1 = x + (Mathf.Sin(a1) - Mathf.Sin(angle)) / kappa;
            z1 = z - (Mathf.Cos(a1) - Mathf.Cos(angle)) / kappa;
        }

        private static int HeadingBin(float angle)
        {
            float turns = angle / (Mathf.PI * 2f);
            turns -= Mathf.Floor(turns);
            return Mathf.RoundToInt(turns * Headings) % Headings;
        }

        private static float Planar(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float[,] BoxBlur(float[,] src, int radius)
        {
            int n = src.GetLength(0);
            int m = src.GetLength(1);
            var tmp = new float[n, m];
            var dst = new float[n, m];
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < m; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int xx = x + k;
                        if (xx < 0 || xx >= m) continue;
                        sum += src[z, xx];
                        count++;
                    }

                    tmp[z, x] = sum / count;
                }
            }

            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < m; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int zz = z + k;
                        if (zz < 0 || zz >= n) continue;
                        sum += tmp[zz, x];
                        count++;
                    }

                    dst[z, x] = sum / count;
                }
            }

            return dst;
        }

        /// <summary>Binary min-heap of state indices (ties broken by index for determinism).</summary>
        private sealed class StateHeap
        {
            private int[] items;
            private float[] keys;

            public StateHeap(int capacity)
            {
                items = new int[capacity];
                keys = new float[capacity];
            }

            public int Count { get; private set; }

            public void Push(int item, float key)
            {
                if (Count == items.Length)
                {
                    Array.Resize(ref items, Count * 2);
                    Array.Resize(ref keys, Count * 2);
                }

                int i = Count++;
                while (i > 0)
                {
                    int up = (i - 1) >> 1;
                    if (keys[up] < key || (keys[up] == key && items[up] < item)) break;
                    items[i] = items[up];
                    keys[i] = keys[up];
                    i = up;
                }

                items[i] = item;
                keys[i] = key;
            }

            public int Pop()
            {
                int top = items[0];
                int lastItem = items[--Count];
                float lastKey = keys[Count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && (keys[child + 1] < keys[child] || (keys[child + 1] == keys[child] && items[child + 1] < items[child]))) child++;
                    if (keys[child] > lastKey || (keys[child] == lastKey && items[child] > lastItem)) break;
                    items[i] = items[child];
                    keys[i] = keys[child];
                    i = child;
                }

                if (Count > 0)
                {
                    items[i] = lastItem;
                    keys[i] = lastKey;
                }

                return top;
            }
        }
    }
}
