using System;
using System.Collections.Generic;
using MedievalZen.People;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>What a composed walled city holds (counts for tests and the log).</summary>
    public sealed class CityStats
    {
        /// <summary>Buildings by family (House, Tavern, Smithy, LandmarkChapel...).</summary>
        public readonly Dictionary<string, int> Buildings = new Dictionary<string, int>();
        /// <summary>Instanced wall modules, crenellations and towers (gatehouse towers included).</summary>
        public int WallModules, Battlements, Towers;
        /// <summary>Cobbled area, square metres (sum of strip areas; overlaps at junctions counted twice).</summary>
        public float CobbledArea;
        /// <summary>Triangles of the city's buildings and wall pieces at LOD0.</summary>
        public long Triangles;
        /// <summary>The two gates (window-local road points) and the road distances (from the centre) at which the road crosses the wall.</summary>
        public Vector3 GateIn, GateOut;
        /// <summary>Along-road distance of the gates from the centre (negative behind).</summary>
        public float GateInAlong, GateOutAlong;
        /// <summary>Total buildings.</summary>
        public int BuildingCount { get { int n = 0; foreach (int c in Buildings.Values) n += c; return n; } }
        /// <summary>Least and greatest distance of the wall's outline from the centre, metres.</summary>
        public float WallRadiusMin, WallRadiusMax;
        /// <summary>The wall's chord vertices (window-local), in order round the centre.</summary>
        public readonly List<Vector3> Outline = new List<Vector3>();
        /// <summary>The gatehouse towers' foot centres (window-local, y on the ground).</summary>
        public readonly List<Vector3> GateTowers = new List<Vector3>();
        /// <summary>Neighbours in a closed street front (prop indices): the main street, the square and the lanes.</summary>
        public readonly List<(int a, int b)> FrontPairs = new List<(int, int)>();
        /// <summary>Ground given a use inside the wall (Garden, Orchard, KeepYard): window-local quads, for the numbers and tests.</summary>
        public readonly List<(string kind, Vector3[] quad)> Grounds = new List<(string, Vector3[])>();
        /// <summary>The hill: its height above the road, its terrace faces, the ways up (ramps, flights of steps) and the steepest ramp's grade.</summary>
        public float HillHeight, RampGradeMax;
        public int Terraces, Ramps, Stairs;
        /// <summary>Retaining-wall modules and their battlement rails (instanced), rocks seated on the outer flank, the castle's pieces, gardens.</summary>
        public int RetainingModules, RetainingBattlements, Rocks, CastlePieces, Gardens;
        /// <summary>The castle (a mined plan's name, or "Keep"), its gate, the first ramp's foot and the last ramp's top (window-local).</summary>
        public string Castle;
        public Vector3 CastleGate, RampFoot, RampTop;
        /// <summary>The castle's footprint across, height and depth (zero for the keep), its own gate (the bridge's inner end; <see cref="CastleGate"/> is the bridge's head).</summary>
        public Vector3 CastleSize, CastleGateInner;
        /// <summary>Cliff rocks round the castle's rock (also in <see cref="Rocks"/>), rocks at the retaining walls' feet (also in <see cref="Rocks"/>), flags, trees in the squares and gardens.</summary>
        public int CastleRocks, WallFootRocks, Banners, Trees, WidenedPaving;
        /// <summary>The hill's capped pillars (run ends, corners, relief), its stair flights, and its decks (window frame) with their triangles.</summary>
        public int Pillars, Flights;
        /// <summary>Garden trees where a terrace's riser row had no room for a house.</summary>
        public int TerraceGardens;
        public readonly List<TerraceDeck> Decks = new List<TerraceDeck>();
        public long DeckTriangles;
        /// <summary>Set pieces placed in part (a vignette deeper than its slot: the parts nearest the wall), by kind (also counted in <see cref="SetPieces"/>).</summary>
        public readonly Dictionary<VignetteKind, int> Partial = new Dictionary<VignetteKind, int>();
        /// <summary>Paved open ground (square metres, beyond the streets) and the share of the ground inside the wall left as grass.</summary>
        public float PavedArea, OpenGrassShare;
        /// <summary>Set-piece slots the layout offered, and the slots a vignette from <see cref="CitySetPieces"/> filled, by kind.</summary>
        public readonly Dictionary<VignetteKind, int> Slots = new Dictionary<VignetteKind, int>(), SetPieces = new Dictionary<VignetteKind, int>();
        /// <summary>Every vignette part placed, as placed (2026-10-09: how it is held, the ground under it, the face it is fixed to), for the tests.</summary>
        public readonly List<PlacedSetPart> SetParts = new List<PlacedSetPart>();
    }

    /// <summary>
    /// The walled city (2026-10-06): a rare high point of the journey in the slot of a village (<see cref="JourneyPlan.IsCitySlot"/>).
    /// Composed on a <see cref="SettlementKit"/> whose ground the city's terrain disc has levelled (radius 125 m, level within 106 m).
    /// Rebuilt the same day as a town built up inside a tighter wall (the first version was a village with a wall round it):
    /// <list type="bullet">
    /// <item>A wall on a rounded outline round the centre, R(θ) = R0 (1 + 0.04 sin(2θ + φ) + 0.03 sin(3θ + ψ)), R0 87..93 m, so 81..100 m
    /// out; the castle kit's 5 m <see cref="PropRole.CityWall"/> modules laid on 4.9 m chords (no gaps), crenellations on each, a round
    /// <see cref="PropRole.CityTower"/> (x1.6: 4.9 m across, 8 m tall) every eighth chord. All instanced (boundary pieces).</item>
    /// <item>A gatehouse where the road crosses the outline: two towers (x2.0: 6.1 m across, 10 m tall) 7.5 m either side of the road's
    /// centre (8.9 m clear between them: the 5 m road and its 1.5 m verges), joined over the road by a two-faced span of wall modules from
    /// 4.8 m up (at least <see cref="MinGateClearance"/> above the road) with crenellations on top. The pack has no gate this wide.</item>
    /// <item>The main street is the road, cobbled gate to gate, a closed front of the tall presets each side (neighbours 0.5-2 m apart by
    /// the measured extents, eaves included; a gap only at a lane's mouth); at the centre on one side the market square (44 x 34 m: well,
    /// stalls, benches), tall houses closing its two sides and the church at its back; the tavern across the road from it.</item>
    /// <item>Blocks: a back lane runs parallel at <see cref="BackLane"/> m each side and lanes join it to the main street at both ends (and
    /// once more on the side without the square), lined both sides; behind the street fronts long fenced gardens run back to a low wall
    /// along the back lane. Across the back lane a looser row faces it, its gardens reaching the town wall.</item>
    /// <item>The open ground given a use: a small square where the middle lane meets the back lane, the smithy at its back; the keep
    /// (<see cref="PropRole.LandmarkTower"/>) in a yard of its own against the town wall, a low stone wall on its three town sides with
    /// its gate towards the back lane; an orchard behind the church. Outside each gate, a house or two along the road.</item>
    /// </list>
    /// The castle on its rock (2026-10-06, see <see cref="CityHill.Castle"/>): the mined demo castle whole behind the plateau, its gate
    /// bridge's head at the end of the ramp street, the town wall running up to its flanks, cliff rocks round the rock, flags; slots take
    /// vignettes by the room they really have (cut to the parts nearest the wall when deeper), birches in the squares and gardens.
    /// Houses never repeat the preset of the house placed before them. Every building is checked against the exact extents placed so far
    /// and the reserved ground (wall strip, streets, the keep's yard, the orchard), then seated by <see cref="SettlementKit.TryShape"/>
    /// (off the road and verge, in the window, on level enough ground). Pure: no native Unity calls; one random stream (the kit's).
    /// </summary>
    public static class CityComposer
    {
        /// <summary>The city's random stream (<see cref="EndlessNoise"/> 1400; 1401-1429 free for the city's later work).</summary>
        public const int Stream = 1400;


        /// <summary>Wall module chord, metres (the 5 m module overlaps its neighbour by 0.1 m).</summary>
        public const float Chord = 4.9f;
        /// <summary>Height of a wall module, of the crenellations on it, and the castle kit's measured triangles per piece.</summary>
        public const float WallHeight = 5f, BattlementHeight = 1.38f;
        public const int WallTris = 20, BattlementTris = 146, TowerTris = 608;
        /// <summary>Wall tower scale and the gatehouse towers' scale (Tower_M is 3.05 m across, 5.02 m tall, 2.5 m of foundation).</summary>
        public const float TowerScale = 1.6f, GateTowerScale = 2.0f, TowerDiameter = 3.05f;
        /// <summary>Gatehouse towers' centres either side of the road's centre, and the span's underside above the road.</summary>
        public const float GateTowerOffset = 7.5f, GateClearHeight = 4.8f;
        /// <summary>Least clearance of the gate's span above the road (the span is laid <see cref="GateClearHeight"/> above the ground at the gate).</summary>
        public const float MinGateClearance = 4.5f;

        /// <summary>Clear width between the gatehouse towers, metres.</summary>
        public static float GateClearWidth => 2f * (GateTowerOffset - 0.5f * TowerDiameter * GateTowerScale);

        private static readonly int[] TallHouses = { 0, 3, 5, 8 };     // House_01_A, House_05, House_08, House_09_C (8.4-11.9 m)
        private static readonly int[] BackHouses = { 1, 2, 5 };        // House_02_A, House_03, House_08 (9.8-12.5k): the back lanes and outside the gates
        private static readonly int[] HillHouses = { 1, 2, 5, 4 };     // House_02_A, House_03, House_08, House_07: the shallow ones, for the terraces

        /// <summary>Composes the city on <paramref name="kit"/> (road centre index = the place) on <paramref name="hill"/> (window-local). Returns its numbers; cobbles go to <paramref name="cobbles"/>.</summary>
        public static CityStats Compose(SettlementKit kit, CityHill hill, List<(Vector3 a, Vector3 b, float width)> cobbles)
        {
            var stats = new CityStats { HillHeight = hill.Top, Terraces = hill.Faces };
            System.Random rng = kit.Rng;
            kit.Frame(0f, out Vector3 centre, out Vector3 t0);
            var town = new Town { Kit = kit, Rng = rng, Stats = stats, Cobbles = cobbles, Hill = hill, RockFamily = TileGenerator.PassRockFamily(PropRole.Cliff, 1f) ?? PropRole.Cliff };
            Func<Vector3, float> outside = q => hill.Outside(q.x, q.z);
            Func<float, float> radius = hill.WallRadius;
            // Everything inside the town wall on the hill sits on the planned surface (true steps, decks over the banks), never on a bank.
            float level = hill.Level;
            kit.Surface = (x, z) =>
            {
                hill.Local(x, z, out float su, out float sv);
                if (sv < CityHill.FootV - 6f || hill.Outside(x, z) > -0.5f) return float.NaN;
                return level + (hill.DeckAt(su, sv, out float top) ? top : hill.SurfaceAt(su, sv));
            };

            stats.GateOutAlong = GateAlong(kit, outside, 1f);
            stats.GateInAlong = GateAlong(kit, outside, -1f);
            kit.Frame(stats.GateOutAlong, out stats.GateOut, out Vector3 tOut);
            kit.Frame(stats.GateInAlong, out stats.GateIn, out Vector3 tIn);

            // ---------------------------------------------------------------- the wall
            var outline = new List<Vector3>();
            const int samples = 1440;
            for (int i = 0; i < samples; i++)
            {
                float th = i * Mathf.PI * 2f / samples;
                float rr = radius(th);
                outline.Add(new Vector3(centre.x + rr * Mathf.Cos(th), 0f, centre.z + rr * Mathf.Sin(th)));
                stats.WallRadiusMin = i == 0 ? rr : Mathf.Min(stats.WallRadiusMin, rr);
                stats.WallRadiusMax = Mathf.Max(stats.WallRadiusMax, rr);
            }

            float perimeter = 0f;
            for (int i = 0; i < samples; i++) perimeter += Vector3.Distance(outline[i], outline[(i + 1) % samples]);
            float chord = perimeter / Mathf.Ceil(perimeter / Chord);
            var vertices = new List<Vector3> { outline[0] };
            float acc = 0f;
            for (int i = 1; i <= samples; i++)
            {
                Vector3 a = outline[i - 1], b = outline[i % samples];
                float seg = Vector3.Distance(a, b);
                while (acc + seg >= chord)
                {
                    a = Vector3.Lerp(a, b, (chord - acc) / seg);
                    vertices.Add(a);
                    seg = Vector3.Distance(a, b);
                    acc = 0f;
                }

                acc += seg;
            }

            if (Vector3.Distance(vertices[vertices.Count - 1], vertices[0]) < chord * 0.5f) vertices.RemoveAt(vertices.Count - 1);
            stats.Outline.AddRange(vertices);
            Vector3 wallIn = WallDirection(centre, radius, stats.GateIn), wallOut = WallDirection(centre, radius, stats.GateOut);
            float oIn = TowerSpacing(wallIn, tIn), oOut = TowerSpacing(wallOut, tOut);
            string rock = TileGenerator.PassRockFamily(PropRole.Cliff, 1f) ?? PropRole.Cliff;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 a = vertices[i], b = vertices[(i + 1) % vertices.Count];
                Vector3 mid = (a + b) * 0.5f;
                if (FlatDistance(mid, stats.GateIn) < oIn + 1.5f || FlatDistance(mid, stats.GateOut) < oOut + 1.5f) continue;
                if (kit.RoadDistance(mid) < kit.Half + SettlementKit.Verge + 3f) continue;
                // The castle's rock: the town wall runs up to the castle's flanks (a tower at each joint) instead of round it.
                hill.Local(mid.x, mid.z, out float wu, out float wv);
                if (hill.OnCastle(wu, wv, 0.5f))
                {
                    hill.Local(a.x, a.z, out float au, out float av);
                    hill.Local(b.x, b.z, out float bu, out float bv);
                    Vector3 joint = !hill.OnCastle(au, av, 0.5f) ? a : !hill.OnCastle(bu, bv, 0.5f) ? b : Vector3.zero;
                    if (joint != Vector3.zero)
                    {
                        Piece(kit, PropRole.CityTower, new Vector3(joint.x, kit.Ground(joint), joint.z), ScatterPlanner.Yaw(b - a), TowerScale * 1.3f);
                        town.Reserve(joint, 0.5f * TowerDiameter * TowerScale * 1.3f + 1f);
                        stats.Towers++;
                    }

                    continue;
                }

                float yaw = ScatterPlanner.Yaw(b - a);
                // Level steps up the hill's flanks: the module sits on the lowest ground under its chord and is stacked to the highest.
                float lo = Mathf.Min(kit.Ground(mid), Mathf.Min(kit.Ground(a), kit.Ground(b))) - 0.3f;
                float hi = Mathf.Max(kit.Ground(mid), Mathf.Max(kit.Ground(a), kit.Ground(b))) - 0.3f;
                float y = lo;
                int stacked = 0;
                for (; ; y += WallHeight)
                {
                    Piece(kit, PropRole.CityWall, new Vector3(mid.x, y, mid.z), yaw, 1f);
                    stats.WallModules++;
                    stacked++;
                    if (y >= hi - 0.01f) break;
                }

                // Relief where the wall stands taller than one module (up the hill's flanks): a pillar at every third joint.
                if (stacked > 1 && i % 3 == 0) Pillar(town, a, yaw, lo, y + WallHeight);

                Piece(kit, PropRole.CityBattlement, new Vector3(mid.x, y + WallHeight, mid.z), yaw, 1f);
                town.Reserve(mid, 3.2f);
                stats.Battlements++;
                bool nearGate = FlatDistance(a, stats.GateIn) < 22f || FlatDistance(a, stats.GateOut) < 22f;
                if (i % 8 == 0 && !nearGate)
                {
                    Piece(kit, PropRole.CityTower, new Vector3(a.x, kit.Ground(a), a.z), yaw, TowerScale);
                    town.Reserve(a, 0.5f * TowerDiameter * TowerScale + 1f);
                    stats.Towers++;
                }

                // The hill's outer flank: rock outcrops below the wall where the ground falls away (seated after the town, off its streets).
                Vector3 outward = new Vector3(mid.x - centre.x, 0f, mid.z - centre.z).normalized;
                for (int row = 0; row < 2; row++)
                {
                    // Two staggered rows of outcrops down the flank (11 m and 18 m out) wherever it falls more than 2.5 m.
                    if ((i + row) % 2 != 0) continue;
                    Vector3 below = mid + outward * (row == 0 ? 11f : 18f);
                    if (kit.Ground(mid) - kit.Ground(below) <= 2.5f) continue;
                    float sc = row == 0 ? 0.9f + 0.6f * (float)rng.NextDouble() : 0.7f + 0.5f * (float)rng.NextDouble();
                    town.PendingRocks.Add((rock, below, (float)(rng.NextDouble() * 360.0), sc, 2.5f, false));
                }

                // Flags along the wall's inner face, every sixth module.
                if (town.WallCount++ % 6 == 3 && !nearGate) Flag(town, mid - outward * 2.4f, yaw, 0.66f, town.WallCount / 6);
            }

            Gatehouse(town, stats.GateIn, wallIn, oIn);
            Gatehouse(town, stats.GateOut, wallOut, oOut);

            Hill(town, centre);
            Build(town, centre, outside);
            Pave(town, outside);
            Greenery(town);
            Rocks(town);
            stats.Triangles += (long)(stats.WallModules + stats.RetainingModules) * WallTris + (long)(stats.Battlements + stats.RetainingBattlements) * BattlementTris + (long)stats.Towers * TowerTris
                + (long)stats.Pillars * PillarTris + stats.DeckTriangles;
            return stats;
        }

        // ------------------------------------------------------------------ pieces

        private static float GateAlong(SettlementKit kit, Func<Vector3, float> outside, float dir)
        {
            for (float a = 0f; a < 178f; a += 0.5f)
            {
                if (outside(kit.Beside(dir * a, 0f, 0f)) >= 0f) return dir * a;
            }

            return dir * 178f;
        }

        /// <summary>Unit direction of the wall's outline where it passes <paramref name="at"/>.</summary>
        private static Vector3 WallDirection(Vector3 centre, Func<float, float> radius, Vector3 at)
        {
            float th = Mathf.Atan2(at.z - centre.z, at.x - centre.x), e = 0.01f;
            Func<float, Vector3> o = a => new Vector3(radius(a) * Mathf.Cos(a), 0f, radius(a) * Mathf.Sin(a));
            Vector3 d = o(th + e) - o(th - e);
            return d.sqrMagnitude > 1e-9f ? d.normalized : Vector3.right;
        }

        /// <summary>
        /// Distance along the wall from the gate to each gatehouse tower: the towers stand on the wall's line, each
        /// <see cref="GateTowerOffset"/> square off the road's centre line however obliquely the wall crosses the road.
        /// </summary>
        private static float TowerSpacing(Vector3 wallDir, Vector3 roadTangent)
        {
            float sin = Mathf.Abs(wallDir.x * roadTangent.z - wallDir.z * roadTangent.x);
            return GateTowerOffset / Mathf.Max(0.6f, sin);
        }

        private static void Gatehouse(Town town, Vector3 gate, Vector3 wallDir, float spacing)
        {
            SettlementKit kit = town.Kit;
            CityStats stats = town.Stats;
            float g = kit.Ground(gate);
            float yaw = ScatterPlanner.Yaw(wallDir);
            Vector3 normal = new Vector3(-wallDir.z, 0f, wallDir.x);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 at = gate + wallDir * s * spacing;
                Piece(kit, PropRole.CityTower, new Vector3(at.x, g, at.z), yaw, GateTowerScale);
                town.Reserve(at, 0.5f * TowerDiameter * GateTowerScale + 1f);
                stats.GateTowers.Add(new Vector3(at.x, g, at.z));
                stats.Towers++;
            }

            // The span over the road along the wall's line, tower centre to tower centre, two faces 1.2 m either side of it.
            int modules = Mathf.CeilToInt(2f * spacing / 5f);
            float step = 2f * spacing / modules;
            for (int face = -1; face <= 1; face += 2)
            {
                for (int k = 0; k < modules; k++)
                {
                    Vector3 at = gate + normal * (1.2f * face) + wallDir * (-spacing + step * (k + 0.5f));
                    Piece(kit, PropRole.CityWall, new Vector3(at.x, g + GateClearHeight, at.z), yaw, 1f);
                    Piece(kit, PropRole.CityBattlement, new Vector3(at.x, g + GateClearHeight + WallHeight, at.z), yaw, 1f);
                    stats.WallModules++;
                    stats.Battlements++;
                }
            }

            Vector3 side = kit.RoadDistance(gate + normal) >= 0f ? new Vector3(wallDir.x, 0f, wallDir.z) : normal;
            kit.Anchor(AnchorKind.Gate, gate + side * (kit.Half + SettlementKit.Verge + 0.6f), ScatterPlanner.Yaw(-side), 1);
        }

        private static void Piece(SettlementKit kit, string role, Vector3 at, float yaw, float scale)
        {
            kit.Props.Add(new PropPlacement { role = role, variant = 0, position = at, yaw = yaw, scale = scale, settlementId = -1 });
        }

        private static Vector3 Tangent(SettlementKit kit, float along)
        {
            kit.Frame(along, out _, out Vector3 t);
            return t;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ the hill

        private static Vector3 At(CityHill h, float u, float v)
        {
            h.World(u, v, out double x, out double z);
            return new Vector3((float)x, 0f, (float)z);
        }

        /// <summary>Distance from (u, v) to the nearest way's centre line less its half width (negative on a way), the ways up face <paramref name="skipFace"/> left out.</summary>
        private static float WayDistance(CityHill h, float u, float v, int skipFace = -1)
        {
            float best = float.MaxValue;
            foreach (CityHill.Way w in h.Ways)
            {
                if (w.Face == skipFace) continue;
                for (int i = 1; i < w.U.Length; i++)
                {
                    float ex = w.U[i] - w.U[i - 1], ez = w.V[i] - w.V[i - 1], l2 = ex * ex + ez * ez;
                    float f = l2 > 1e-9f ? Mathf.Clamp01(((u - w.U[i - 1]) * ex + (v - w.V[i - 1]) * ez) / l2) : 0f;
                    float dx = w.U[i - 1] + ex * f - u, dz = w.V[i - 1] + ez * f - v;
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz) - w.Half);
                }
            }

            return best;
        }

        /// <summary>Length of a wall module and of its battlement rail (<c>Castle_Wall_01</c>, <c>Castle_Battlements_01</c>: 5.00 m).</summary>
        public const float ModuleLength = 5f;
        /// <summary>The stone pillar (<c>Castle_Pillar_Stone_05</c>: 0.66 m square, 5.03 m tall from its pivot) and where its cap (<c>Castle_Battlements_Pillar_01</c>) starts above its own pivot (4.93 m: it is modelled to sit on a 5 m pillar).</summary>
        public const float PillarHeight = 5f, PillarCapBase = 4.93f;
        /// <summary>What a retaining wall keeps clear on either side of its thickness, metres (round each module's middle).</summary>
        public const float WallClear = 0.75f;
        /// <summary>A pillar every this many modules along a terrace's wall, for relief.</summary>
        public const int ReliefEvery = 4;
        /// <summary>Measured triangles of a capped pillar and of the stair flight (<c>Stairs_Straight_Wide_Edges_01</c>).</summary>
        public const int PillarTris = 16 + 36, StairTris = 276;

        /// <summary>
        /// The retaining walls of the hill's plan (<see cref="CityHill.Runs"/>): along each run 5 m modules, each one riser, standing on the
        /// low ground with its top at the high level (a ramp's outer wall leans with the ramp), the parapet the deck's low lip on the
        /// module's top (<see cref="CityHill.LipHeight"/>, drawn by <see cref="TerraceDecks"/>; 2026-10-09, no battlements), a capped pillar at both ends of every run and every <see cref="ReliefEvery"/> modules along a face.
        /// </summary>
        private static void Walls(Town t)
        {
            SettlementKit kit = t.Kit;
            CityHill h = t.Hill;
            CityStats st = t.Stats;
            float level = h.Level;
            Vector3 o = At(h, 0f, 0f);
            foreach (CityHill.Run run in h.Runs)
            {
                float len = run.Length;
                int count = len < 1f ? 0 : Math.Max(1, Mathf.CeilToInt((len - 0.1f) / Chord));
                float yawRun = 0f;
                for (int k = 0; k < count; k++)
                {
                    float s0 = count == 1 ? 0.5f * len - 0.5f * ModuleLength : k * (len - ModuleLength) / (count - 1), s1 = s0 + ModuleLength;
                    run.At(s0, out float au, out float av, out _, out _);
                    run.At(s1, out float bu, out float bv, out _, out _);
                    run.At(0.5f * (s0 + s1), out _, out _, out float nu, out float nv);
                    Vector3 a = At(h, au, av), b = At(h, bu, bv), n = At(h, nu, nv) - o;
                    Vector3 d = b - a;
                    d.y = 0f;
                    float dl = d.magnitude;
                    if (dl < 1e-3f) continue;
                    d /= dl;
                    Vector3 line = (a + b) * 0.5f, mid = line + n * (0.5f * CityHill.WallThick);
                    float yaw = ScatterPlanner.Yaw(d), pitch = 0f;
                    yawRun = yaw;
                    float ha = run.HiAt(Mathf.Clamp(s0, 0f, len)), hb = run.HiAt(Mathf.Clamp(s1, 0f, len));
                    float top = level + (run.Tilt ? 0.5f * (ha + hb) : run.HiAt(0.5f * (s0 + s1)));
                    Vector3 basePos = new Vector3(mid.x, top - WallHeight, mid.z);
                    if (run.Tilt)
                    {
                        // Lean with the ramp (+Z of the piece rises): the top's centre over the chord's middle, the base set along the slope.
                        float p = -Mathf.Atan2(hb - ha, dl);
                        pitch = p * Mathf.Rad2Deg;
                        float sp = Mathf.Sin(p), cp = Mathf.Cos(p);
                        basePos = new Vector3(mid.x - WallHeight * sp * d.x, top - WallHeight * cp, mid.z - WallHeight * sp * d.z);
                    }

                    // No battlement: the terraces' parapet is the decks' low stone lip (TerraceDecks), as on the demo's terraces;
                    // battlements belong to the town wall, its gatehouses and the castle.
                    kit.Props.Add(new PropPlacement { role = PropRole.CityWall, variant = 0, position = basePos, yaw = yaw, pitch = pitch, scale = 1f, settlementId = -1 });
                    st.RetainingModules++;
                    // The module's footprint and 0.75 m either side (the terrace houses stand with their backs to it).
                    t.Reserve(mid, 0.5f * CityHill.WallThick + WallClear, false);
                    if (run.Kind != CityHill.RunKind.Face) continue;

                    // Relief along a long face.
                    if (k > 0 && k % ReliefEvery == 0) Pillar(t, a + n * (0.5f * CityHill.WallThick), yaw, level + run.LoAt(s0), level + run.HiAt(s0));
                    // The wall's foot on the terrace below (where no way runs in front of it): a slot for the set pieces.
                    Vector3 foot = line - n * 1.9f;
                    h.Local(foot.x, foot.z, out float fu, out float fv);
                    if (WayDistance(h, fu, fv) > 1.5f) t.WallFeet.Add((foot, -n));
                    // Flags on the terrace's edge behind the parapet, every third module (the demo's terrace edges carry them).
                    if (t.RetainCount++ % 3 == 1) Flag(t, line + n * 1.3f, yaw, 0.66f, t.RetainCount / 3);
                }

                if (count == 0) continue;
                // A capped pillar at both ends of the run (its corners and the openings at landings and flights).
                for (int e = 0; e < 2; e++)
                {
                    float s = e == 0 ? 0f : len;
                    run.At(s, out float pu, out float pv, out float nu, out float nv);
                    Vector3 n = At(h, nu, nv) - o;
                    Pillar(t, At(h, pu, pv) + n * (0.5f * CityHill.WallThick), yawRun, level + run.LoAt(s), level + run.HiAt(s));
                }
            }
        }

        /// <summary>A stone pillar from the ground <paramref name="lo"/> to a wall's top <paramref name="top"/> (stacked where taller than one), its cap standing above the parapet; one per spot.</summary>
        private static void Pillar(Town t, Vector3 at, float yaw, float lo, float top)
        {
            SettlementKit kit = t.Kit;
            if (!kit.Keys.Has(PropRole.CityPillar)) return;
            foreach (Vector3 p in t.PillarSpots) if (FlatDistance(p, at) < 0.8f) return;
            t.PillarSpots.Add(at);
            for (float y = top - PillarHeight; ; y -= PillarHeight)
            {
                kit.Props.Add(new PropPlacement { role = PropRole.CityPillar, variant = 0, position = new Vector3(at.x, y, at.z), yaw = yaw, scale = 1f, settlementId = -1 });
                if (y <= lo + 0.05f) break;
            }

            if (kit.Keys.Count(PropRole.CityPillar) > 1)
                kit.Props.Add(new PropPlacement { role = PropRole.CityPillar, variant = 1, position = new Vector3(at.x, top - PillarCapBase, at.z), yaw = yaw, scale = 1f, settlementId = -1 });
            t.Reserve(at, 0.7f, false);
            t.Stats.Pillars++;
        }

        /// <summary>The pack's stair flight standing in front of its face (its top riser on the face's line, climbing up the hill), its foot and its top kept clear.</summary>
        private static void Flight(Town t, CityHill.Way w)
        {
            SettlementKit kit = t.Kit;
            CityHill h = t.Hill;
            Vector3 bottom = At(h, w.U[0], w.V[0]), top = At(h, w.U[1], w.V[1]);
            Vector3 climb = top - bottom;
            climb.y = 0f;
            climb.Normalize();
            Vector3 centre = top - climb * CityHill.FlightTopFromCentre;
            if (kit.Keys.Has(PropRole.CityStairs))
            {
                kit.Props.Add(new PropPlacement { role = PropRole.CityStairs, variant = 0, position = new Vector3(centre.x, h.Level + w.H[0], centre.z), yaw = ScatterPlanner.Yaw(climb), scale = 1f, settlementId = -1 });
                t.Stats.Flights++;
                t.Stats.Triangles += StairTris;
            }

            for (float a = 0.5f; a < CityHill.FlightRun; a += 1.5f) t.Reserve(bottom + climb * a, w.Half + 0.3f);
            t.Strip(bottom - climb * 3f, bottom, 2f * w.Half);
            t.Strip(top, top + climb * 3f, 2f * w.Half);
        }

        /// <summary>The hill's decks (<see cref="CityHill.Decks"/>) in the window's frame, their tops at the city's level plus the plan's.</summary>
        private static void Decks(Town t)
        {
            CityHill h = t.Hill;
            foreach (CityHill.DeckPlan p in h.Decks)
            {
                int n = p.OU.Length;
                var deck = new TerraceDeck { Ramp = p.Kind != CityHill.RunKind.Face, Outer = new Vector3[n], Inner = new Vector3[n], LipA = p.LipA, LipB = p.LipB, EndLip = p.EndLip };
                for (int k = 0; k < n; k++)
                {
                    float y = h.Level + p.H[k] + CityHill.DeckLift;
                    Vector3 a = At(h, p.OU[k], p.OV[k]), b = At(h, p.IU[k], p.IV[k]);
                    deck.Outer[k] = new Vector3(a.x, y, a.z);
                    deck.Inner[k] = new Vector3(b.x, y, b.z);
                }

                t.Stats.Decks.Add(deck);
                t.Stats.DeckTriangles += TerraceDecks.TrianglesOf(deck);
            }
        }

        /// <summary>The hill: the ways (ramps cobbled, the pack's flights), the retaining walls and decks, the castle on the plateau, the view square.</summary>
        private static void Hill(Town t, Vector3 centre)
        {
            SettlementKit kit = t.Kit;
            CityHill h = t.Hill;
            CityStats st = t.Stats;

            // The ways: ramps cobbled and kept free, flights placed.
            foreach (CityHill.Way w in h.Ways)
            {
                if (w.Stair)
                {
                    Flight(t, w);
                    st.Stairs++;
                    continue;
                }

                for (int i = 0; i + 1 < w.U.Length; i++) t.Strip(At(h, w.U[i], w.V[i]), At(h, w.U[i + 1], w.V[i + 1]), 2f * w.Half + 0.4f);
                st.Ramps++;
                float rise = w.H[w.H.Length - 1] - w.H[0];
                st.RampGradeMax = Mathf.Max(st.RampGradeMax, rise / Mathf.Max(1f, w.Length));
            }

            Walls(t);
            Decks(t);
            // The castle on the plateau, its gate towards the town; the street from the last ramp to its gate.
            Vector3 toTown = At(h, 0f, -1f) - At(h, 0f, 0f);
            Vector3 gate;
            if (h.Castle != null) gate = CastleOnRock(t, toTown);
            else
            {
                float vWall = h.WallRadius(Mathf.Atan2(h.NZ, h.NX));
                float depth = vWall - 3.5f - (CityHill.PlateauV + 2f);
                float vc = CityHill.PlateauV + 2f + 0.5f * depth;
                gate = Castle(t, At(h, 0f, vc), toTown, 0.5f * depth);
            }

            st.CastleGate = gate;
            CityHill.Way last = null;
            foreach (CityHill.Way w in h.Ways) if (!w.Stair) last = w;
            Vector3 rampTop = At(h, last.U[last.U.Length - 1], last.V[last.V.Length - 1]);
            st.RampTop = rampTop;
            st.RampFoot = At(h, h.Ways[0].U[0], h.Ways[0].V[0]);
            t.Hill.Local(gate.x, gate.z, out float gu, out float gv);
            Vector3 bend = At(h, last.U[last.U.Length - 1], gv);
            t.Strip(rampTop, bend, 5f);
            t.Strip(bend, gate, 5f);

            // Hairpins: from each ramp's top across the bench to the next ramp's foot, kept free and cobbled.
            for (int i = 0; i + 1 < h.Ways.Count; i++)
            {
                CityHill.Way a = h.Ways[i], b = h.Ways[i + 1];
                if (a.Stair || b.Stair) continue;
                t.Strip(At(h, a.U[a.U.Length - 1], a.V[a.V.Length - 1]), At(h, b.U[0], b.V[0]), 4.6f);
            }

            // The landings' openings onto the terrace above, kept free (the terrace houses stand clear of them).
            foreach (CityHill.Way w in h.Ways)
            {
                if (w.Stair) continue;
                CityHill.FaceLine fl = h.Lines[w.Face];
                fl.At(w.SB - w.Dir * 0.5f * CityHill.LandLen, out float lu, out float lv, out float lnu, out float lnv);
                h.OffsetPoint(w.Face, lu, lv, lnu, lnv, 0.6f, out float au, out float av);
                h.OffsetPoint(w.Face, lu, lv, lnu, lnv, 6f, out float bu, out float bv);
                t.Strip(At(h, au, av), At(h, bu, bv), CityHill.LandLen, false);
            }

            // The main street to the first ramp's foot.
            CityHill.Way first = h.Ways[0];
            t.Strip(At(h, first.U[0], 4f), At(h, first.U[0], first.V[0]), 4.6f);
            t.SideStreets.Add(first.U[0]);

            // The view square on the plateau beside the castle, where the last ramp arrives: a tree, a well, benches, the parapet below.
            float su = last.U[last.U.Length - 1];
            float sv = CityHill.PlateauV + (h.Castle != null ? 4.5f : 6f);
            Vector3 sq = At(h, su * 0.55f + Mathf.Sign(su) * 4f, sv);
            for (float a = -6f; a <= 6f; a += 4f) t.Strip(sq + (At(h, a, 0f) - At(h, 0f, 0f)) - toTown * 4f, sq + (At(h, a, 0f) - At(h, 0f, 0f)) + toTown * 4f, 4.4f);
            t.Plazas.Add((sq, toTown));
            if (!Vignette(t, VignetteKind.Plaza, sq, -toTown, 12f)) kit.AddTree(PropRole.FruitTree, sq - toTown * 3.5f);
            // The town's well up here whether or not a set piece took the square (on level ground now the square always offers one):
            // beside it, the first free spot.
            if (!kit.Props.Exists(p => p.role == PropRole.Well))
            {
                int wellVariant = kit.Keys.PickVariant(PropRole.Well, t.Rng);
                foreach (Vector2 o in new[] { new Vector2(3.5f, 1.5f), new Vector2(-3.5f, 1.5f), new Vector2(7.5f, 0f), new Vector2(-7.5f, 0f), new Vector2(0f, -5f), new Vector2(10.5f, 0f), new Vector2(-10.5f, 0f), new Vector2(5f, -5f), new Vector2(-5f, -5f) })
                {
                    Vector3 spot = sq + (At(h, o.x, 0f) - At(h, 0f, 0f)) + toTown * o.y;
                    if (Free(t, spot, 1.8f, false) && kit.TryProp(PropRole.Well, wellVariant, spot, 0f, 1f, 1.8f))
                    {
                        kit.Anchor(AnchorKind.Well, spot + toTown * 1.8f, ScatterPlanner.Yaw(-toTown), h.Side);
                        break;
                    }
                }
            }

            for (int k = -1; k <= 1; k += 2) kit.TryProp(PropRole.Bench, sq + toTown * 3.4f + (At(h, 2.6f * k, 0f) - At(h, 0f, 0f)), ScatterPlanner.Yaw(toTown), 1.1f);
        }

        /// <summary>
        /// The castle on its rock (<see cref="CityHill.Castle"/>): every piece of the mined plan at the rock's level (Y = 0 the courtyard and
        /// gate, foundations below it running down the rock's cliff), its gate towards the town; the head of its gate bridge is where the
        /// street from the last ramp arrives (returned). Cliff rocks are seated round the rock's foot and down its faces so no stretched
        /// terrain shows, flags stand on its front, and the rock is kept clear of everything else.
        /// </summary>
        private static Vector3 CastleOnRock(Town t, Vector3 toTown)
        {
            SettlementKit kit = t.Kit;
            CityStats st = t.Stats;
            CityHill h = t.Hill;
            CastlePlan p = h.Castle;
            Vector3 right = new Vector3(toTown.z, 0f, -toTown.x);
            Vector3 c = At(h, 0f, h.CastleV);
            float yaw0 = ScatterPlanner.Yaw(toTown), g = kit.Ground(c);
            foreach (SetPart part in p.Parts)
            {
                Vector3 at = c + right * part.X + toTown * part.Z;
                kit.Props.Add(new PropPlacement { role = part.Role, variant = part.Variant, position = new Vector3(at.x, g + part.Y, at.z), yaw = yaw0 + part.Yaw, scale = part.Scale, settlementId = -1 });
                if (PropRole.IsCityPiece(part.Role) || part.Role == PropRole.CastlePiece) st.CastlePieces++;
            }

            st.Castle = p.Name;
            st.CastleSize = new Vector3(2f * p.HalfWidth, p.Height, 2f * p.HalfDepth);
            Add(st, PropRole.LandmarkTower, 0);
            t.Rects.Add(new[] { c - right * p.HalfWidth - toTown * p.HalfDepth, c + right * p.HalfWidth - toTown * p.HalfDepth, c + right * p.HalfWidth + toTown * p.HalfDepth, c - right * p.HalfWidth + toTown * p.HalfDepth });

            // Cliff rocks (2026-10-09, after the review's single giant boulder): the rock's faces carry the faceted skin (RockSkinField),
            // so the pieces only break its line, at a believable size. Along the outline of the rock's level top on its three outer faces
            // (the flanks, the back and the rounded corners between): a foot row of cliff pieces at x0.9-1.4 every 8 m, staggered in and
            // out, a sparser row half-way up the face between them, and small pieces (x0.5-0.9) between the foot row's. Each where the
            // rock falls at least 2 m from its top's edge; sized by variant and seated off the town and its wall in Rocks.
            string cliff = kit.Keys.Has(PropRole.PassCliff) ? PropRole.PassCliff : TileGenerator.PassRockFamily(PropRole.Cliff, 1f) ?? PropRole.Cliff;
            foreach (var (row, off, step, lo, hi) in new[]
            {
                (0, 0.82f * CityHill.CliffRun, 8f, 0.9f, 1.4f),     // the foot row
                (1, 0.5f * CityHill.CliffRun, 12f, 0.9f, 1.15f),    // half-way up the face
                (2, 0.95f * CityHill.CliffRun, 8f, 0.5f, 0.9f),     // small pieces between the foot row's
            })
            {
                float phase = row == 0 ? 0f : 0.5f;
                foreach (var (ru, rv, ou, ov) in RockOutline(h, off, step, phase))
                {
                    // The foot row steps in and out by 1.5 m (its pieces alternate), the small ones sit a little to the side.
                    float d = off + (row == 0 ? 1.5f * (float)(t.Rng.NextDouble() * 2.0 - 1.0) : 0f);
                    float side = row == 2 ? 1.5f * (float)(t.Rng.NextDouble() * 2.0 - 1.0) : 0f;
                    Vector3 at = At(h, ru + ou * (d - off) - ov * side, rv + ov * (d - off) + ou * side);
                    Vector3 up = At(h, ru - ou * off, rv - ov * off);
                    float sc = lo + (hi - lo) * (float)t.Rng.NextDouble();
                    float yaw = (float)(t.Rng.NextDouble() * 360.0);
                    if (kit.Ground(up) - kit.Ground(at) < 2f) continue;
                    // Only on the rock's outer faces: never inside the town wall (seated after the town, off its streets: Rocks).
                    t.PendingRocks.Add((cliff, at, yaw, sc, 0.5f, true));
                }
            }

            // The rock is the castle's alone: occupied (no house, tree, field or prop of the countryside on it or its cliff).
            for (float a = -h.RockHalfU - 6f; a <= h.RockHalfU + 6f; a += 9f)
            {
                for (float v = h.RockV0 + 4f; v <= h.RockV1 + 8f; v += 9f) kit.Occupancy.Add(At(h, a, v), 6.5f);
            }

            // Flags either side of the gate bridge's head, on the rock's front.
            Vector3 head = c + right * p.GateX + toTown * (p.HalfDepth + 1.5f);
            for (int k = -1; k <= 1; k += 2)
            {
                Vector3 f = head + right * (4.5f * k);
                Flag(t, f, yaw0 + 90f, 1f, k);
            }

            st.CastleGateInner = c + right * p.GateX + toTown * p.GateZ;
            return head;
        }

        /// <summary>
        /// A flag on its pole (the demo's <c>SM_Prop_Flag_10</c> / <c>_12</c>, which its artists stood on terrace edges and squares at
        /// x0.47-0.66) standing on the ground at <paramref name="at"/>, off the road, its verge and the ways up; alternates by <paramref name="k"/>.
        /// </summary>
        private static bool Flag(Town t, Vector3 at, float yaw, float scale, int k)
        {
            SettlementKit kit = t.Kit;
            if (!kit.Keys.Has(PropRole.CityBanner) || !kit.OffRoad(at, 0.5f) || !Free(t, at, 0.4f, false) || kit.Occupancy.Overlaps(at.x, at.z, 0.4f)) return false;
            t.Hill.Local(at.x, at.z, out float u, out float v);
            if (WayDistance(t.Hill, u, v) < 0.6f) return false;
            int variant = Math.Min(kit.Keys.Count(PropRole.CityBanner) - 1, 2 + (k & 1));
            kit.Props.Add(new PropPlacement { role = PropRole.CityBanner, variant = variant, position = new Vector3(at.x, kit.Ground(at), at.z), yaw = yaw, scale = scale, settlementId = -1 });
            kit.Occupancy.Add(new Vector3(at.x, 0f, at.z), 0.5f);
            t.Stats.Banners++;
            return true;
        }

        /// <summary>Rocks keep this far from a street, a way, a door and a house, metres (plus half the rock's own reach).</summary>
        public const float RockClear = 6f;

        /// <summary>
        /// The rock outcrops planned on the hill's outer flank and on the castle rock's outer faces, seated once the town stands: only
        /// outside the town wall (by their whole reach) and at least <see cref="RockClear"/> m plus half their reach from any street,
        /// way, door or house. With the terraces stepped crisply there is no stretched terrain inside the town left for rocks to hide.
        /// </summary>
        private static void Rocks(Town t)
        {
            SettlementKit kit = t.Kit;
            CityHill h = t.Hill;
            List<Vector3> views = OuterViews(h);
            foreach (var (role, at, yaw, planned, radius, castle) in t.PendingRocks)
            {
                float scale = planned;
                if (!PickRock(kit, h, views, role, at, ref scale, out int variant, out float reach, out float size)) continue;
                if (h.Outside(at.x, at.z) < Mathf.Max(0.6f * reach, 0.5f * size) + 1.5f) continue;
                float clear = RockClear + 0.5f * reach;
                if (kit.RoadDistance(at) < kit.Half + SettlementKit.Verge + clear) continue;
                h.Local(at.x, at.z, out float u, out float v);
                if (WayDistance(h, u, v) < clear || NearCobbles(t, at, clear)) continue;
                bool house = false;
                foreach (Vector3[] rect in t.Rects) if (PolygonDistance(rect, new Vector3(at.x, 0f, at.z)) < clear) { house = true; break; }
                if (house) continue;
                if (!kit.TryRock(role, variant, at, yaw, scale, radius)) continue;
                t.Stats.Rocks++;
                if (castle) t.Stats.CastleRocks++;
            }
        }

        /// <summary>Largest piece (metres across, its longest footprint side) within <see cref="NearRockReach"/> m of the town wall or a castle view, and anywhere round the city.</summary>
        public const float NearRockMax = 8f, RockMax = 12f;

        /// <summary>How near (metres, from a piece's edge) to the town wall's outline or a castle view counts as near.</summary>
        public const float NearRockReach = 10f;

        /// <summary>Where the capture tour's castle views stand (<see cref="CityViewpoints"/>: the castle's flanks and back, outside the wall), in the hill's frame.</summary>
        private static List<Vector3> OuterViews(CityHill h)
        {
            var list = new List<Vector3>();
            if (h.Castle == null) return list;
            foreach (CityViewpoint vp in CityViewpoints.Plan(h, null, 0.0))
            {
                if (double.IsNaN(vp.EyeX) || !vp.Name.StartsWith("castle flank", StringComparison.Ordinal) && vp.Name != "castle back") continue;
                list.Add(new Vector3((float)vp.EyeX, 0f, (float)vp.EyeZ));
            }

            return list;
        }

        /// <summary>Whether a piece <paramref name="size"/> m across at <paramref name="at"/> comes within <see cref="NearRockReach"/> m of the town wall's outline or a view.</summary>
        public static bool NearWallOrView(CityHill h, List<Vector3> views, Vector3 at, float size)
        {
            float half = 0.5f * size;
            if (Mathf.Abs(h.Outside(at.x, at.z)) - half < NearRockReach) return true;
            foreach (Vector3 vp in views) if (FlatDistance(vp, at) - half < NearRockReach + CityViewpoints.Search) return true;   // (the tour may move a view by up to Search m)
            return false;
        }

        /// <summary>
        /// The variant of a rock family for a planned piece and its scale (2026-10-09): a random variant whose size at the planned scale
        /// fits (<see cref="NearRockMax"/> near the town wall or a castle view, <see cref="RockMax"/> elsewhere), else the
        /// smallest variant scaled down to fit; false when it would have to shrink below 0.3. Returns its footprint radius and its size.
        /// </summary>
        private static bool PickRock(SettlementKit kit, CityHill h, List<Vector3> views, string role, Vector3 at, ref float scale, out int variant, out float reach, out float size)
        {
            variant = -1;
            reach = size = 0f;
            int n = kit.Keys.Count(role);
            if (n <= 0) return false;
            int start = kit.Keys.PickVariant(role, kit.Rng), smallest = -1;
            float smallestLength = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                int vi = (start + k) % n;
                if (!kit.Keys.TryGetMetrics(role, vi, out PropMetrics m)) continue;
                if (m.Length < smallestLength) { smallestLength = m.Length; smallest = vi; }
                float s = m.Length * scale;
                float limit = NearWallOrView(h, views, at, s) ? NearRockMax : RockMax;
                if (s > limit) continue;
                variant = vi;
                reach = m.FootprintRadius * scale;
                size = s;
                return true;
            }

            if (smallest < 0 || !kit.Keys.TryGetMetrics(role, smallest, out PropMetrics sm)) return false;
            float fit = Mathf.Min(scale, NearRockMax / Mathf.Max(0.1f, sm.Length));
            if (fit < 0.3f) return false;
            scale = fit;
            variant = smallest;
            reach = sm.FootprintRadius * scale;
            size = sm.Length * scale;
            return true;
        }

        /// <summary>
        /// Points every <paramref name="step"/> m (starting <paramref name="phase"/> of a step in) along the castle rock's outline grown by
        /// <paramref name="off"/> m on its three outer faces: up the right flank from the rock's front line, round its back corner, along
        /// the back, round the other corner and down the left flank; each with its outward direction (hill frame u, v).
        /// </summary>
        private static List<(float u, float v, float ou, float ov)> RockOutline(CityHill h, float off, float step, float phase)
        {
            var list = new List<(float, float, float, float)>();
            float side = h.RockV1 - h.RockV0, arc = 0.5f * Mathf.PI * off, back = 2f * h.RockHalfU;
            float total = 2f * side + 2f * arc + back;
            for (float s = phase * step; s <= total + 1e-3f; s += step)
            {
                float r = s;
                if (r <= side) { list.Add((h.RockHalfU + off, h.RockV0 + r, 1f, 0f)); continue; }
                r -= side;
                if (r <= arc)
                {
                    float a = off > 1e-3f ? r / off : 0f;
                    float ou = Mathf.Cos(a), ov = Mathf.Sin(a);
                    list.Add((h.RockHalfU + ou * off, h.RockV1 + ov * off, ou, ov));
                    continue;
                }

                r -= arc;
                if (r <= back) { list.Add((h.RockHalfU - r, h.RockV1 + off, 0f, 1f)); continue; }
                r -= back;
                if (r <= arc)
                {
                    float a = 0.5f * Mathf.PI + (off > 1e-3f ? r / off : 0f);
                    float ou = Mathf.Cos(a), ov = Mathf.Sin(a);
                    list.Add((-h.RockHalfU + ou * off, h.RockV1 + ov * off, ou, ov));
                    continue;
                }

                r -= arc;
                list.Add((-h.RockHalfU - off, h.RockV1 - Mathf.Min(r, side), -1f, 0f));
            }

            return list;
        }

        /// <summary>The largest footprint radius among a rock family's pieces at scale 1 (2 m when unmeasured).</summary>
        public static float RockReach(SettlementKit kit, string role)
        {
            float r = 0f;
            int n = kit.Keys.Count(role);
            for (int i = 0; i < n; i++) if (kit.Keys.TryGetMetrics(role, i, out PropMetrics m)) r = Mathf.Max(r, m.FootprintRadius);
            return r > 0f ? r : 2f;
        }

        /// <summary>Whether a cobbled strip passes within <paramref name="clear"/> m of <paramref name="p"/>'s edge of paving.</summary>
        private static bool NearCobbles(Town t, Vector3 p, float clear)
        {
            foreach (var (a, b, w) in t.Cobbles)
            {
                float ex = b.x - a.x, ez = b.z - a.z, l2 = ex * ex + ez * ez;
                float f = l2 > 1e-9f ? Mathf.Clamp01(((p.x - a.x) * ex + (p.z - a.z) * ez) / l2) : 0f;
                float dx = a.x + ex * f - p.x, dz = a.z + ez * f - p.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) < 0.5f * w + clear) return true;
            }

            return false;
        }

        /// <summary>
        /// Trees in the squares and the gardens (the demo's town is full of them): a birch where the families have one (orange in this
        /// game's autumn: <see cref="PropRole.MeadowTree"/> variant 2, <c>Tree_Birch_02</c>), else a fruit tree; each off the road by the
        /// trunk rule (no trunk within 4 m of its centre, never on the verge: 3 m beyond the verge), off the ways up, the buildings and props.
        /// </summary>
        private static void Greenery(Town t)
        {
            SettlementKit kit = t.Kit;
            var spots = new List<(Vector3 at, int group)>();
            int g = 0;
            foreach (var (at, toTown) in t.Plazas)
            {
                Vector3 r = new Vector3(toTown.z, 0f, -toTown.x);
                for (int k = -1; k <= 1; k += 2, g++)
                {
                    foreach (float a in new[] { 7.5f, 10f, 5.5f }) foreach (float b in new[] { -3f, 3f, 0f }) spots.Add((at + r * (a * k) + toTown * b, g));
                }
            }

            // The market square's back corners and the ends of its far side.
            for (int k = -1; k <= 1; k += 2, g++)
            {
                foreach (float a in new[] { SqHalf - 3.5f, SqHalf - 7f, SqHalf - 11f }) foreach (float b in new[] { SqFar - 3f, SqFar - 7f }) spots.Add((kit.Beside(k * a, t.SquareSide, b), g));
            }


            int done = -1;
            foreach (var (at, group) in spots)
            {
                if (group == done) continue;
                if (kit.RoadDistance(at) < kit.Half + SettlementKit.Verge + 3f) continue;   // 7 m: no trunk collider within 4 m of the road's centre
                t.Hill.Local(at.x, at.z, out float u, out float v);
                if (WayDistance(t.Hill, u, v) < 3f || t.Hill.OnCastle(u, v, 2f) || !Free(t, at, 2f, false) || kit.Occupancy.Overlaps(at.x, at.z, 0.9f)) continue;
                if (!AutumnTree(t, at)) continue;
                kit.Occupancy.Add(new Vector3(at.x, 0f, at.z), 1.2f);
                done = group;
            }
        }

        /// <summary>A town tree at <paramref name="at"/>: two in three a birch (orange in autumn), else a fruit tree. Counted in <see cref="CityStats.Trees"/>.</summary>
        private static bool AutumnTree(Town t, Vector3 at)
        {
            SettlementKit kit = t.Kit;
            bool birch = kit.Keys.Has(PropRole.MeadowTree) && kit.Keys.Count(PropRole.MeadowTree) > 2 && t.Stats.Trees % 3 != 2;
            string role = birch ? PropRole.MeadowTree : PropRole.FruitTree;
            if (!kit.Keys.Has(role)) return false;
            kit.Trees.Add(new TreePlacement { role = role, variant = birch ? 2 : kit.Keys.PickVariant(role, t.Rng), position = new Vector3(at.x, kit.Ground(at) - 0.1f, at.z), yaw = (float)(t.Rng.NextDouble() * 360.0), scale = 0.8f + 0.25f * (float)t.Rng.NextDouble() });
            t.Stats.Trees++;
            return true;
        }

        /// <summary>The castle: a mined <see cref="CastlePlan"/> that fits the plateau, else the keep in a ring of wall and towers. Returns its gate.</summary>
        private static Vector3 Castle(Town t, Vector3 c, Vector3 toTown, float halfDepth)
        {
            SettlementKit kit = t.Kit;
            CityStats st = t.Stats;
            Vector3 right = new Vector3(toTown.z, 0f, -toTown.x);
            float yaw0 = ScatterPlanner.Yaw(toTown), g = kit.Ground(c);

            // The keep (x1.25) in a ring of wall modules with four towers, the gap of its gate towards the town.
            st.Castle = "Keep";
            Piece(kit, PropRole.LandmarkTower, new Vector3(c.x, g - 0.2f, c.z), yaw0, 1.25f);
            Add(st, PropRole.LandmarkTower, 33000);
            float r = Mathf.Clamp(halfDepth - 1.5f, 9f, 14f);
            int n = Mathf.CeilToInt(2f * Mathf.PI * r / Chord);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * 2f * Mathf.PI / n, a1 = (i + 1) * 2f * Mathf.PI / n, am = 0.5f * (a0 + a1);
                Vector3 p0 = c + right * (r * Mathf.Sin(a0)) + toTown * (r * Mathf.Cos(a0)), p1 = c + right * (r * Mathf.Sin(a1)) + toTown * (r * Mathf.Cos(a1));
                Vector3 m = (p0 + p1) * 0.5f;
                bool gateGap = Mathf.Abs(Mathf.DeltaAngle(am * Mathf.Rad2Deg, 0f)) < 18f;
                if (!gateGap)
                {
                    float y = kit.Ground(m) - 0.3f;
                    Piece(kit, PropRole.CityWall, new Vector3(m.x, y, m.z), ScatterPlanner.Yaw(p1 - p0), 1f);
                    Piece(kit, PropRole.CityBattlement, new Vector3(m.x, y + WallHeight, m.z), ScatterPlanner.Yaw(p1 - p0), 1f);
                    st.WallModules++;
                    st.Battlements++;
                    st.CastlePieces += 2;
                }
            }

            for (int k = 0; k < 6; k++)
            {
                float a = k < 4 ? (45f + 90f * k) * Mathf.Deg2Rad : (k == 4 ? 22f : -22f) * Mathf.Deg2Rad;
                Vector3 p = c + right * (r * Mathf.Sin(a)) + toTown * (r * Mathf.Cos(a));
                Piece(kit, PropRole.CityTower, new Vector3(p.x, kit.Ground(p), p.z), yaw0, k < 4 ? TowerScale * 1.3f : TowerScale);
                st.Towers++;
                st.CastlePieces++;
            }

            t.Reserve(c, r + 2.5f);
            return c + toTown * (r + 1.5f);
        }

        // ------------------------------------------------------------------ set pieces

        /// <summary>
        /// Offers a slot of <paramref name="kind"/> at <paramref name="origin"/> (on the edge it stands against, <paramref name="outward"/> into
        /// the street) with <paramref name="room"/> m along the edge, and fills it from <see cref="CitySetPieces"/> by a seeded pick: never the
        /// same vignette within 60 m of itself, never on a door, the road, its verge or a way up. False when nothing was placed.
        /// Parts are seated as they were in the demo (2026-10-09, <see cref="SetMount"/>): on the ground under their own footprint (none
        /// where the ground under it varies more than 0.25 m), on the part they stood on, hung from the part they hung from, or on the
        /// house front or wall face the slot stands against (none without one); none overlaps a rock, a building, a wall or a piece
        /// placed before, and the context rules hold (<see cref="Suits"/>).
        /// </summary>
        private static bool Vignette(Town t, VignetteKind kind, Vector3 origin, Vector3 outward, float room, float deep = 99f)
        {
            CityStats st = t.Stats;
            st.Slots.TryGetValue(kind, out int offered);
            st.Slots[kind] = offered + 1;
            t.Pool.Clear();
            CitySetPieces.Of(kind, t.Pool);
            int own = t.Pool.Count;
            // Clutter against a front is much the same beside a door, at a corner or at a wall's foot: a slot may borrow the other's.
            if (kind == VignetteKind.DoorSide || kind == VignetteKind.WallFoot) CitySetPieces.Of(VignetteKind.Corner, t.Pool);
            else if (kind == VignetteKind.Corner) CitySetPieces.Of(VignetteKind.DoorSide, t.Pool);
            if (t.Pool.Count == 0) return false;
            outward.y = 0f;
            outward.Normalize();
            Vector3 right = new Vector3(outward.z, 0f, -outward.x);
            // The room the slot really has out from its edge (to the road's verge, a way up, a door).
            float depth = Mathf.Min(deep, FreeDepth(t, origin, outward, right, room));
            if (depth < 0.8f) return false;
            SlotFace face = FindFace(t, origin, outward, right);
            if (face.Found && face.Back < 0f)
            {
                // The slot's edge lies inside the wall (a wall foot offered from the wall's middle line): the vignette starts at the face.
                origin = new Vector3(face.Point.x, origin.y, face.Point.z);
                face.Back = 0f;
                depth = Mathf.Min(deep, FreeDepth(t, origin, outward, right, room));
                if (depth < 0.8f) return false;
            }
            List<Vector3> rocks = RockCircles(t, origin, 15f);

            // By the room: the first vignette (seeded order, its own kind first) that fits and seats whole; else the one seating most of its
            // parts when cut to the slot (the parts nearest the wall), at least two (one for a vignette of two) and a third of them.
            int start = t.Rng.Next(own > 0 ? own : t.Pool.Count);
            CityVignette best = null;
            SeatPlan bestPlan = null;
            bool whole = false;
            for (int k = 0; k < t.Pool.Count; k++)
            {
                CityVignette v = t.Pool[own > 0 && k < own ? (start + k) % own : k];
                if (v.Parts == null || v.Parts.Length == 0) continue;
                bool repeat = false;
                foreach (var (name, at) in t.Placed) if (name == v.Name && FlatDistance(at, origin) < 60f) repeat = true;
                if (repeat) continue;
                bool fits = v.Width <= room && v.Depth <= depth;
                SeatPlan plan = Seat(t, v, kind, origin, outward, right, room, depth, fits, face, rocks);
                if (fits && plan.Count == v.Parts.Length)
                {
                    best = v;
                    bestPlan = plan;
                    whole = true;
                    break;
                }

                if (plan.Count >= Mathf.Min(2, v.Parts.Length) && plan.Count * 3 >= v.Parts.Length && (bestPlan == null || plan.Count > bestPlan.Count))
                {
                    best = v;
                    bestPlan = plan;
                }
            }

            if (best == null) return false;
            float yaw0 = ScatterPlanner.Yaw(outward);
            int first = st.SetParts.Count;
            float w = 0f, d = 0f;
            for (int i = 0; i < best.Parts.Length; i++)
            {
                if (!bestPlan.Ok[i]) continue;
                SetPart part = best.Parts[i];
                Vector3 at = bestPlan.At[i];
                float yaw = yaw0 + bestPlan.Yaw[i];
                t.Kit.Props.Add(new PropPlacement { role = part.Role, variant = part.Variant, position = at, yaw = yaw, scale = part.Scale, settlementId = -1 });
                if (part.Mount == SetMount.Ground) t.Kit.Occupancy.Add(new Vector3(at.x, 0f, at.z), 0.7f * part.Radius);
                w = Mathf.Max(w, 2f * Mathf.Abs(part.X) + 1.2f);
                d = Mathf.Max(d, part.Z + 0.6f);
                bool onFace = part.Mount == SetMount.Wall || (part.Mount == SetMount.Hung && bestPlan.OnFace[i]);
                st.SetParts.Add(new PlacedSetPart
                {
                    Vignette = best.Name, Placement = t.Placed.Count, Slot = kind, Part = i, Role = part.Role, Prefab = part.Prefab, Mount = part.Mount,
                    Support = part.Support >= 0 ? first + bestPlan.Index[part.Support] : -1, Position = at, Yaw = yaw, Radius = part.Radius,
                    Contact = at.y + part.Contact, GroundLow = bestPlan.GroundLow[i], GroundHigh = bestPlan.GroundHigh[i], OnFace = onFace,
                    HasFace = face.Found, FacePoint = face.Point, FaceBack = face.Back, FaceNormal = outward, FaceTop = face.Top, Street = face.Street,
                });
                bestPlan.Index[i] = st.SetParts.Count - 1 - first;
            }

            t.Placed.Add((best.Name, origin));
            st.SetPieces.TryGetValue(kind, out int filled);
            st.SetPieces[kind] = filled + 1;
            if (!whole)
            {
                st.Partial.TryGetValue(kind, out int part0);
                st.Partial[kind] = part0 + 1;
            }

            // The paving widened under it where a door's or a corner's clutter would otherwise stand on grass.
            d = Mathf.Min(d, depth);
            w = Mathf.Min(w, room);
            if ((kind == VignetteKind.DoorSide || kind == VignetteKind.Corner) && !Cobbled(t, origin + outward * (0.5f * d)))
            {
                Vector3 m = origin + outward * (0.5f * d);
                t.Strip(m - right * (0.5f * w + 0.3f), m + right * (0.5f * w + 0.3f), d + 0.4f);
                st.WidenedPaving++;
            }

            return true;
        }

        /// <summary>Where each part of a vignette would go in a slot (window-local), and which parts the slot keeps.</summary>
        private sealed class SeatPlan
        {
            public bool[] Ok, OnFace;
            public Vector3[] At;
            public float[] Yaw, GroundLow, GroundHigh;
            public int[] Index;
            public int Count;
        }

        /// <summary>
        /// Seats <paramref name="v"/> in a slot (see <see cref="Vignette"/>) without placing anything. A part is kept when the slot keeps it
        /// (whole, or within the cut), what it stands on or hangs from is kept, and: on the ground, the ground under its footprint varies by
        /// 0.25 m at most and the footprint is clear (<see cref="Blocked"/>); on the wall, the slot has a face that spans it and is tall
        /// enough to hold it at its lowest mount; and its context holds (<see cref="Suits"/>).
        /// </summary>
        private static SeatPlan Seat(Town t, CityVignette v, VignetteKind kind, Vector3 origin, Vector3 outward, Vector3 right, float room, float depth, bool whole,
            SlotFace face, List<Vector3> rocks)
        {
            int n = v.Parts.Length;
            var plan = new SeatPlan { Ok = new bool[n], OnFace = new bool[n], At = new Vector3[n], Yaw = new float[n], GroundLow = new float[n], GroundHigh = new float[n], Index = new int[n] };
            // Each part's root (the ground or wall part it rests on through its supports) and each wall root's reach below and above its pivot.
            var root = new int[n];
            var low = new float[n];
            var high = new float[n];
            for (int i = 0; i < n; i++)
            {
                SetPart p = v.Parts[i];
                root[i] = (p.Mount == SetMount.OnPart || p.Mount == SetMount.Hung) && p.Support >= 0 && p.Support < i ? root[p.Support] : i;
                low[i] = float.MaxValue;
                high[i] = float.MinValue;
                SetPart r = v.Parts[root[i]];
                low[root[i]] = Mathf.Min(low[root[i]], p.Y - r.Y + p.Low);
                high[root[i]] = Mathf.Max(high[root[i]], p.Y - r.Y + p.High);
            }

            bool planterContext = kind == VignetteKind.DoorSide;
            for (int i = 0; i < n; i++)
            {
                SetPart p = v.Parts[i];
                if (!whole && !Keeps(p, room, depth)) continue;
                Vector3 at = origin + right * p.X + outward * p.Z;
                float yaw = p.Yaw;
                switch (p.Mount)
                {
                    case SetMount.Ground:
                    {
                        if (!Suits(t, kind, p, ref yaw, ref at, origin, outward, face, rocks)) continue;
                        if (!SeatGround(t, at, p.Radius, out float g, out plan.GroundLow[i], out plan.GroundHigh[i])) continue;
                        if (Blocked(t, at, p.Radius, rocks)) continue;
                        bool clash = false;
                        for (int j = 0; j < i && !clash; j++)
                        {
                            if (plan.Ok[j] && v.Parts[j].Mount == SetMount.Ground && FlatDistance(plan.At[j], at) < 0.45f * (p.Radius + v.Parts[j].Radius) && yaw != p.Yaw) clash = true;
                        }

                        if (clash) continue;
                        plan.At[i] = new Vector3(at.x, g - p.Contact, at.z);
                        break;
                    }
                    case SetMount.Wall:
                    {
                        if (!face.Found) continue;
                        float s = p.X;
                        if (s - 0.5f * p.Radius < face.Left - 0.05f || s + 0.5f * p.Radius > face.Right + 0.05f) continue;
                        float y = Mathf.Min(p.Y, face.Top - face.Street - high[i]);
                        if (y < p.MountMin) continue;
                        Vector3 f = face.Point + right * s + outward * p.Z;
                        plan.At[i] = new Vector3(f.x, face.Street + y, f.z);
                        plan.OnFace[i] = true;
                        break;
                    }
                    default:
                    {
                        int sp = p.Support;
                        if (sp < 0 || sp >= i || !plan.Ok[sp]) continue;
                        SetPart s = v.Parts[sp];
                        plan.At[i] = plan.At[sp] + right * (p.X - s.X) + outward * (p.Z - s.Z) + Vector3.up * (p.Y - s.Y);
                        plan.OnFace[i] = plan.OnFace[sp];
                        plan.GroundLow[i] = plan.GroundLow[sp];
                        plan.GroundHigh[i] = plan.GroundHigh[sp];
                        break;
                    }
                }

                plan.Ok[i] = true;
                plan.Yaw[i] = yaw;
                if (p.Role == PropRole.Bench || p.Role == PropRole.CityMonument || IsDoorSlot(kind)) planterContext = true;
            }

            // A planter beside a door or a bench (or a monument it frames): never alone against a wall.
            if (!planterContext)
            {
                for (int i = 0; i < n; i++) if (plan.Ok[i] && v.Parts[i].Role == PropRole.CityPlanter) plan.Ok[i] = false;
                for (int i = 0; i < n; i++)
                {
                    int sp = v.Parts[i].Support;
                    if (plan.Ok[i] && sp >= 0 && !plan.Ok[sp]) plan.Ok[i] = false;
                }
            }

            for (int i = 0; i < n; i++) if (plan.Ok[i]) plan.Count++;
            return plan;
        }

        private static bool IsDoorSlot(VignetteKind kind) => kind == VignetteKind.DoorSide;

        /// <summary>
        /// The context rules for a part on the ground (2026-10-09): a lamp only at a corner, against a wall or beside a door, and within 1.6 m
        /// of the edge; a bench with its back to the house front or wall the slot stands against (turned to it, within 1.5 m of it), or round
        /// a monument on a square, with 2 m clear in front of it; a cart only in a yard. May turn the part (<paramref name="yaw"/>).
        /// </summary>
        private static bool Suits(Town t, VignetteKind kind, SetPart p, ref float yaw, ref Vector3 at, Vector3 origin, Vector3 outward, SlotFace face, List<Vector3> rocks)
        {
            if (p.Role == PropRole.Lamp || p.Prefab.StartsWith("SM_Prop_Street_Lamp", StringComparison.Ordinal))
                return (kind == VignetteKind.Corner || kind == VignetteKind.DoorSide || kind == VignetteKind.WallFoot && face.Found) && p.Z <= 1.6f;
            if (IsCart(p.Prefab)) return kind == VignetteKind.Yard;
            if (p.Role != PropRole.Bench) return true;
            float front;
            if (kind == VignetteKind.Plaza)
            {
                front = yaw;   // the designed seating round a monument
            }
            else
            {
                // Within 1.5 m of the slot's edge (a house front, its porch or steps, or a wall face).
                if (!face.Found || p.Z > 1.5f) return false;
                // A bench with a back turns its back to the face; one without lies along it. Either way it looks out into the street.
                if (p.High > 0.7f) yaw = 0f;
                else yaw = Mathf.Abs(Mathf.Repeat(yaw + 180f, 360f) - 180f) <= 90f ? 0f : 180f;
                front = 0f;
            }

            Vector3 r2 = new Vector3(outward.z, 0f, -outward.x);
            float fr = front * Mathf.Deg2Rad;
            Vector3 dir = outward * Mathf.Cos(fr) + r2 * Mathf.Sin(fr);
            return ClearAhead(t, at, dir, rocks);
        }

        private static bool IsCart(string prefab) => prefab.Contains("Cart") || prefab.Contains("Wheelbarrow");

        /// <summary>Nothing in the 2 m in front of a bench: no rock, building, wall or piece placed before, no road.</summary>
        private static bool ClearAhead(Town t, Vector3 at, Vector3 dir, List<Vector3> rocks)
        {
            for (float s = 0.7f; s <= 2.01f; s += 0.65f)
            {
                Vector3 q = at + dir * s;
                if (!t.Kit.OffRoad(q, 0f) || !Free(t, q, 0.25f, false) || t.Kit.Occupancy.Overlaps(q.x, q.z, 0.2f)) return false;
                foreach (Vector3 c in rocks) if (Sq(c.x - q.x) + Sq(c.z - q.z) < Sq(c.y + 0.2f)) return false;
                if (NearWall(t, q, 0.4f)) return false;
            }

            return true;
        }

        private static float Sq(float x) => x * x;

        /// <summary>Whether a footprint of <paramref name="radius"/> at <paramref name="at"/> touches a rock, a building, a wall, a piece placed before or the road.</summary>
        private static bool Blocked(Town t, Vector3 at, float radius, List<Vector3> rocks)
        {
            float r = 0.7f * radius;
            if (!t.Kit.OffRoad(at, 0.2f) || !Free(t, at, 0.5f * radius, false) || t.Kit.Occupancy.Overlaps(at.x, at.z, 0.4f * radius)) return true;
            foreach (Vector3 c in rocks) if (Sq(c.x - at.x) + Sq(c.z - at.z) < Sq(c.y + r)) return true;
            return NearWall(t, at, r - 0.2f);
        }

        /// <summary>Whether a circle at <paramref name="at"/> reaches into a wall module (town wall or retaining wall; centred on its pivot, 5 m along its yaw, 0.5 m thick).</summary>
        private static bool NearWall(Town t, Vector3 at, float radius)
        {
            foreach (PropPlacement p in t.Kit.Props)
            {
                if (p.role != PropRole.CityWall && p.role != PropRole.CityTower) continue;
                float dx = at.x - p.position.x, dz = at.z - p.position.z;
                if (dx * dx + dz * dz > 49f) continue;
                if (p.role == PropRole.CityTower)
                {
                    if (dx * dx + dz * dz < Sq(radius + 0.5f * TowerDiameter * p.scale)) return true;
                    continue;
                }

                float yr = p.yaw * Mathf.Deg2Rad, ax = Mathf.Sin(yr), az = Mathf.Cos(yr);
                float along = dx * ax + dz * az, across = dx * az - dz * ax;
                if (Mathf.Abs(along) < 2.5f * p.scale + radius && Mathf.Abs(across) < 0.25f * p.scale + radius) return true;
            }

            return false;
        }

        /// <summary>
        /// A rock as a circle: the measured footprint's half-diagonal at the rock's scale round the
        /// footprint's centre (2 m per unit of scale without metrics). Also used by the tests.
        /// </summary>
        public static void RockCircle(PaletteKeys keys, PropPlacement p, out Vector3 centre, out float radius)
        {
            centre = p.position;
            radius = 2f * p.scale;
            if (keys == null || !keys.TryGetMetrics(p.role, p.variant, out PropMetrics m)) return;
            Vector3 c = 0.5f * (m.min + m.max) * p.scale;
            float yr = p.yaw * Mathf.Deg2Rad, cs = Mathf.Cos(yr), sn = Mathf.Sin(yr);
            centre = new Vector3(p.position.x + c.x * cs + c.z * sn, p.position.y, p.position.z - c.x * sn + c.z * cs);
            radius = m.FootprintRadius * p.scale;
        }

        private static List<Vector3> RockCircles(Town t, Vector3 at, float reach)
        {
            var list = new List<Vector3>();
            foreach (PropPlacement p in t.Kit.Props)
            {
                if (!PropRole.IsRock(p.role)) continue;
                RockCircle(t.Kit.Keys, p, out Vector3 c, out float r);
                if (FlatDistance(c, at) < reach + r) list.Add(new Vector3(c.x, r, c.z));
            }

            return list;
        }

        /// <summary>
        /// The ground under a footprint of <paramref name="radius"/> (centre and four points at 0.7 of it): false when it varies by more than
        /// 0.25 m; else the seat (the centre's height, raised or lowered so no point is more than 0.1 m above it or 0.15 m below it).
        /// </summary>
        private static bool SeatGround(Town t, Vector3 at, float radius, out float seat, out float lo, out float hi)
        {
            float r = 0.7f * Mathf.Max(0.2f, radius);
            float c = SetGround(t, at);
            lo = hi = c;
            for (int k = 0; k < 4; k++)
            {
                float h = SetGround(t, at + new Vector3(k == 0 ? r : k == 1 ? -r : 0f, 0f, k == 2 ? r : k == 3 ? -r : 0f));
                lo = Mathf.Min(lo, h);
                hi = Mathf.Max(hi, h);
            }

            seat = Mathf.Clamp(c, hi - 0.1f, lo + 0.15f);
            return hi - lo <= 0.25f;
        }

        /// <summary>The ground a set piece stands on (the composer's ground; one place to change if the hill gets its own surface).</summary>
        private static float SetGround(Town t, Vector3 at) => t.Kit.Ground(at);

        /// <summary>The house front or wall face a slot stands against: a point on it level with the slot's origin, the street's height at its foot, its top, its reach to each side of the origin (along +right).</summary>
        private struct SlotFace
        {
            public bool Found;
            public Vector3 Point;
            public float Back, Street, Top, Left, Right;
        }

        /// <summary>
        /// The face behind a slot's origin (at most 3 m behind it, facing the same way within 18 degrees, spanning the origin): a house's front
        /// wall (<see cref="BuildingShape.FrontWall"/>, up to its eaves) or a run of wall modules (up to the highest module's top).
        /// </summary>
        private static SlotFace FindFace(Town t, Vector3 origin, Vector3 outward, Vector3 right)
        {
            var face = new SlotFace();
            float bestBack = float.MaxValue;
            float street = SetGround(t, origin + outward * 0.3f);
            foreach (PlacedBuilding b in t.Kit.Buildings)
            {
                if (Vector3.Dot(b.Facing, outward) < 0.95f || FlatDistance(b.Pivot, origin) > 30f) continue;
                BuildingShape sh = b.Shape;
                bool side = sh.FrontYaw > 45f;
                Vector3 a = b.At(side ? new Vector3(sh.FrontWall, 0f, sh.BodyZ0) : new Vector3(sh.BodyX0, 0f, sh.FrontWall));
                Vector3 c = b.At(side ? new Vector3(sh.FrontWall, 0f, sh.BodyZ1) : new Vector3(sh.BodyX1, 0f, sh.FrontWall));
                float back = (origin.x - a.x) * outward.x + (origin.z - a.z) * outward.z;
                float sa = (a.x - origin.x) * right.x + (a.z - origin.z) * right.z, sc = (c.x - origin.x) * right.x + (c.z - origin.z) * right.z;
                if (back < -0.3f || back > 3f || back >= bestBack || Mathf.Min(sa, sc) > -0.3f || Mathf.Max(sa, sc) < 0.3f) continue;
                bestBack = back;
                face = new SlotFace
                {
                    Found = true, Point = new Vector3(origin.x - outward.x * back, street, origin.z - outward.z * back), Back = back, Street = street,
                    Top = b.Pivot.y + sh.Eaves, Left = Mathf.Min(sa, sc), Right = Mathf.Max(sa, sc),
                };
            }

            // Wall modules facing the slot: the nearest plane that spans the origin, widened by the modules beside it on the same plane.
            var mods = new List<(float back, float lo, float hi, float top)>();
            foreach (PropPlacement p in t.Kit.Props)
            {
                if (p.role != PropRole.CityWall) continue;
                float dx = p.position.x - origin.x, dz = p.position.z - origin.z;
                if (dx * dx + dz * dz > 400f) continue;
                float yr = p.yaw * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(yr), 0f, -Mathf.Sin(yr));
                float dn = Vector3.Dot(n, outward);
                if (Mathf.Abs(dn) < 0.95f) continue;
                float back = -(dx * outward.x + dz * outward.z) - 0.25f * p.scale;
                if (back < -1.5f || back > 3f) continue;
                float s = dx * right.x + dz * right.z;
                mods.Add((back, s - 2.5f * p.scale, s + 2.5f * p.scale, p.position.y + WallHeight * p.scale));
            }

            float wb = float.MaxValue;
            foreach (var m in mods) if (m.lo <= 0.3f && m.hi >= -0.3f && m.back < wb) wb = m.back;
            if (wb < bestBack && wb < float.MaxValue)
            {
                float lo = 0f, hi = 0f, top = float.MinValue;
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var m in mods)
                    {
                        if (Mathf.Abs(m.back - wb) > 0.3f || m.hi < lo - 0.3f || m.lo > hi + 0.3f) continue;
                        if (m.lo < lo - 1e-3f || m.hi > hi + 1e-3f) grew = true;
                        lo = Mathf.Min(lo, m.lo);
                        hi = Mathf.Max(hi, m.hi);
                        top = Mathf.Max(top, m.top);
                    }
                }

                face = new SlotFace
                {
                    Found = true, Point = new Vector3(origin.x - outward.x * wb, street, origin.z - outward.z * wb), Back = wb, Street = street, Top = top,
                    Left = lo, Right = hi,
                };
            }

            return face;
        }

        /// <summary>Whether a vignette's part stays when the vignette is cut to a slot <paramref name="room"/> wide and <paramref name="depth"/> deep.</summary>
        private static bool Keeps(SetPart part, float room, float depth) => part.Z <= depth - 0.3f && Mathf.Abs(part.X) <= 0.5f * room + 0.4f;

        /// <summary>How far out from <paramref name="origin"/> a slot <paramref name="room"/> wide stays off the road and its verge, the ways up and the doors (at most 9 m).</summary>
        private static float FreeDepth(Town t, Vector3 origin, Vector3 outward, Vector3 right, float room)
        {
            for (float s = 0.3f; s <= 9f; s += 0.3f)
            {
                for (int i = 0; i <= 2; i++)
                {
                    Vector3 p = origin + right * (room * (i / 2f - 0.5f)) + outward * s;
                    t.Hill.Local(p.x, p.z, out float u, out float vv);
                    if (!t.Kit.OffRoad(p, 0.3f) || WayDistance(t.Hill, u, vv) < 0.6f) return s - 0.3f;
                    foreach (PlaceAnchor door in t.Kit.Anchors) if (door.Kind == AnchorKind.Door && FlatDistance(door.Position, p) < 1.1f) return s - 0.3f;
                }
            }

            return 9f;
        }
        /// <summary>A slot's fallback when the tables have nothing for it: the props the city used before the set pieces.</summary>
        private static void Fallback(Town t, VignetteKind kind, Vector3 origin, Vector3 outward)
        {
            SettlementKit kit = t.Kit;
            Vector3 at = origin + outward * 1.2f;
            float yaw = ScatterPlanner.Yaw(outward);
            switch (kind)
            {
                case VignetteKind.DoorSide: kit.TryProp(PropRole.Goods, at, yaw, 0.7f); break;
                case VignetteKind.Corner: kit.TryProp(PropRole.Lamp, at, yaw, 0.5f); break;
                case VignetteKind.WallFoot: if (t.Rng.NextDouble() < 0.5) kit.TryProp(PropRole.YardGoods, at, yaw, 1f); else kit.TryProp(PropRole.Bench, at, yaw, 1f); break;
                case VignetteKind.Yard: kit.TryProp(PropRole.FarmCart, origin + outward * 2.2f, yaw, 1.6f); break;
            }
        }

        private static void Slot(Town t, VignetteKind kind, Vector3 origin, Vector3 outward, float room, float deep = 99f)
        {
            if (!Vignette(t, kind, origin, outward, room, deep)) Fallback(t, kind, origin, outward);
        }

        // ------------------------------------------------------------------ the town

        /// <summary>The composer's working state: placed footprints (exact), reserved ground (wall, streets, ways), the slots' bookkeeping.</summary>
        private sealed class Town
        {
            public SettlementKit Kit;
            public System.Random Rng;
            public CityStats Stats;
            public CityHill Hill;
            public List<(Vector3 a, Vector3 b, float width)> Cobbles;
            public readonly List<Vector3[]> Rects = new List<Vector3[]>();
            public readonly List<Vector3> Reserved = new List<Vector3>();   // (x, radius, z)
            public readonly List<(Vector3 at, Vector3 outward)> WallFeet = new List<(Vector3, Vector3)>();
            public readonly List<(Vector3 at, Vector3 toTown)> Plazas = new List<(Vector3, Vector3)>();
            public readonly List<float> SideStreets = new List<float>();
            public readonly List<CityVignette> Pool = new List<CityVignette>();
            public readonly List<(string name, Vector3 at)> Placed = new List<(string, Vector3)>();
            public int LastHouse = -1;
            public int RetainCount, WallCount;
            public string RockFamily;
            /// <summary>Rock outcrops planned on the hill's outer flank and the castle rock's faces, seated last (<see cref="Rocks"/>).</summary>
            public readonly List<(string role, Vector3 at, float yaw, float scale, float radius, bool castle)> PendingRocks = new List<(string, Vector3, float, float, float, bool)>();
            public readonly List<Vector3> PillarSpots = new List<Vector3>();
            public int SquareSide;
            /// <summary>The terrace houses (prop index) and the face below each one's street (-1: a house facing up the hill).</summary>
            public readonly Dictionary<int, int> TerraceHouses = new Dictionary<int, int>();

            public void Reserve(Vector3 at, float r, bool occupy = true)
            {
                Reserved.Add(new Vector3(at.x, r, at.z));
                if (occupy) Kit.Occupancy.Add(new Vector3(at.x, 0f, at.z), r);
            }

            public void Strip(Vector3 a, Vector3 b, float width, bool cobble = true)
            {
                float len = FlatDistance(a, b);
                int n = Math.Max(1, Mathf.CeilToInt(len / 1.5f));
                for (int i = 0; i <= n; i++) Reserve(Vector3.Lerp(a, b, i / (float)n), 0.5f * width + 0.4f, false);
                if (!cobble) return;
                Cobbles.Add((a, b, width));
                Stats.CobbledArea += len * width;
            }
        }

        /// <summary>Front of a house on the main street (road half-width, verge, 0.3 m), and of a house on a lane (lane half-width and 0.5 m).</summary>
        private const float StreetFront = 5.6f, LaneFront = 2.5f;
        /// <summary>The market square's half-length along the road and its depth from the road's centre (on the flat side).</summary>
        private const float SqHalf = 22f, SqNear = 4f, SqFar = 27f;
        /// <summary>The flat side's streets parallel to the road: the first lane's offset and the spacing between lanes.</summary>
        public const float BackLane = 29f, LaneSpacing = 27f;
        /// <summary>Gaps between neighbours in a street front by the measured extents, metres.</summary>
        public const float GapMin = 0.2f, GapMax = 1f;
        /// <summary>
        /// The most buildings a city holds (the frame budget: about 1.8 M triangles and 250 GameObjects at full detail). 110 until the
        /// terraces were filled (2026-10-09); five cities on three seeds now hold 126-142 (Durncaster 127, its count 1.67 M triangles).
        /// </summary>
        public const int MaxBuildings = 175;

        private static void Build(Town t, Vector3 centre, Func<Vector3, float> outside)
        {
            SettlementKit kit = t.Kit;
            CityStats stats = t.Stats;
            CityHill h = t.Hill;
            System.Random rng = t.Rng;
            int sq = -h.Side, hs = h.Side;
            t.SquareSide = sq;
            float inner = stats.GateInAlong + 10f, outer = stats.GateOutAlong - 10f;

            // ------------------------------------------------ the main street: cobbled to the house fronts, gate to gate
            for (float a = stats.GateInAlong; a < stats.GateOutAlong; a += 10f)
            {
                float b = Mathf.Min(stats.GateOutAlong, a + 10f), w = 2f * StreetFront;
                t.Cobbles.Add((kit.Beside(a, 0f, 0f), kit.Beside(b, 0f, 0f), w));
                stats.CobbledArea += (b - a) * w;
            }

            // ------------------------------------------------ the flat side: the market square, lanes parallel to the road, cross lanes
            for (float a = -SqHalf; a < SqHalf; a += 4f) t.Strip(kit.Beside(a + 2f, sq, SqNear), kit.Beside(a + 2f, sq, SqFar), 4.4f);
            var parallels = new List<(float off, float a0, float a1)>();
            for (float off = BackLane; off < 100f; off += LaneSpacing)
            {
                float a0 = -110f, a1 = 110f;
                while (a0 < 0f && outside(kit.Beside(a0, sq, off + 3f)) > -7f) a0 += 1f;
                while (a1 > 0f && outside(kit.Beside(a1, sq, off + 3f)) > -7f) a1 -= 1f;
                if (a1 - a0 < 30f) break;
                parallels.Add((off, a0, a1));
                for (float a = a0; a < a1; a += 6f)
                {
                    float b = Mathf.Min(a1, a + 6f);
                    if (off < SqFar + 25f && b > -SqHalf && a < SqHalf) continue;   // the church stands behind the square
                    t.Strip(kit.Beside(a, sq, off), kit.Beside(b, sq, off), 3.6f);
                }
            }

            var lanes = new List<(float along, int side)>();
            if (parallels.Count > 0)
            {
                var (_, pa0, pa1) = parallels[0];
                float mid = (rng.NextDouble() < 0.5 ? -1f : 1f) * (SqHalf + 12f + 6f * (float)rng.NextDouble());
                foreach (float along in new[] { pa0, pa1, mid })
                {
                    lanes.Add((along, sq));
                    float far = 0f;
                    foreach (var (off, a0, a1) in parallels) if (along >= a0 - 1f && along <= a1 + 1f) far = off;
                    t.Strip(kit.Beside(along, sq, kit.Half + 1f), kit.Beside(along, sq, Mathf.Max(BackLane, far)), 3.6f);
                }
            }

            foreach (float u in t.SideStreets) lanes.Add((u, hs));

            // ------------------------------------------------ buildings: the church behind the square, the tavern across from it
            Vector3 toRoadSq = -SettlementKit.Right(Tangent(kit, 0f)) * sq;
            PlacedBuilding church = null;
            for (int k = 0; k < 6 && church == null; k++) church = Place(t, PropRole.LandmarkChapel, rng.Next(2), BuildingTable.Church, kit.Beside(0f, sq, SqFar + 2.8f + k), toRoadSq, sq, 0.5f);
            Count(stats, church);
            PlacedBuilding tavern = null;
            // On the hill side's main street if it fits below the first terrace, else on the flat side beyond the square.
            foreach (float shift in new[] { 0f, 8f, -8f, 16f, -16f, SqHalf + 12f, -SqHalf - 12f, SqHalf + 22f, -SqHalf - 22f })
            {
                int ts = Mathf.Abs(shift) > SqHalf ? sq : hs;
                Vector3 face = -SettlementKit.Right(Tangent(kit, shift)) * ts;
                tavern = Place(t, PropRole.Tavern, 0, BuildingTable.For(PropRole.Tavern, 0), kit.Beside(shift, ts, StreetFront + 0.2f), face, ts, GapMin);
                if (tavern != null) break;
            }

            Count(stats, tavern);
            if (tavern != null) Door(t, tavern);

            // ------------------------------------------------ the smithy's yard on the flat side's middle lane, the market's stalls and well
            PlacedBuilding smithy = null;
            if (lanes.Count > 2 && parallels.Count > 0)
            {
                float mid = lanes[2].along;
                Vector3 toRoadNs = -SettlementKit.Right(Tangent(kit, mid)) * sq;
                foreach (float shift in new[] { 6f, -6f, 9f, -9f, 12f, -12f })
                {
                    smithy = Place(t, PropRole.Smithy, 0, BuildingTable.For(PropRole.Smithy, 0), kit.Beside(mid + shift, sq, BackLane + 2.4f), toRoadNs, sq, GapMin);
                    if (smithy != null) break;
                }
            }

            Count(stats, smithy);
            if (smithy != null)
            {
                Door(t, smithy);
                Vector3 work = smithy.At(smithy.Shape.Work);
                kit.Anchor(AnchorKind.Work, work, ScatterPlanner.Yaw(smithy.Facing), smithy.Side, smithy.PropIndex);
                kit.ForceVariant(PropRole.SmithyTool, rng.Next(2), work + smithy.Across * 1.4f + smithy.Facing * 0.6f, ScatterPlanner.Yaw(smithy.Across));
                Slot(t, VignetteKind.Yard, smithy.Door + smithy.Facing * 0.5f - smithy.Across * 3.5f, smithy.Facing, 5f);
            }

            for (int e = -1; e <= 1; e += 2)
            {
                float edge = e * (SqHalf + 2.6f);
                Vector3 inward = Tangent(kit, edge) * -e;
                Row(t, u => kit.Beside(edge, sq, u), u => inward, StreetFront, SqFar + 1f, sq, TallHouses, GapMin, GapMax, true, 12f);
            }

            // The main street's fronts, both sides, broken only at the lanes' mouths (a corner slot each side of a mouth).
            for (int side = -1; side <= 1; side += 2)
            {
                var cuts = new List<float>();
                foreach (var (along, s) in lanes) if (s == side) cuts.Add(along);
                cuts.Sort();
                float from = inner;
                var stretches = new List<(float, float)>();
                foreach (float c in cuts)
                {
                    stretches.Add((from, c - 2.6f));
                    from = c + 2.6f;
                    for (int k = -1; k <= 1; k += 2) Slot(t, VignetteKind.Corner, kit.Beside(c + k * 2.6f, side, StreetFront + 1.5f), Tangent(kit, c) * -k, 2.8f, 2.3f);
                }

                stretches.Add((from, outer));
                foreach (var (f0, f1) in stretches)
                {
                    if (side == sq && f0 < SqHalf && f1 > -SqHalf)
                    {
                        MainRow(t, f0, -SqHalf - 2f, side);
                        MainRow(t, SqHalf + 2f, f1, side);
                    }
                    else MainRow(t, f0, f1, side);
                }
            }

            // ------------------------------------------------ the terraces (2026-10-09, full as the demo's): on every bench a row of houses
            // shoulder to shoulder with their backs to the next riser (in front of a ramp's outer wall where the ramp runs at its foot) and
            // their fronts to the deck below, their street; where the bench is wide enough (its flanks) a second row with its backs to the
            // lower parapet, facing the first across a street. Gardens where no house fits.
            for (int b = 1; b < h.Faces; b++) TerraceRow(t, b, true);
            for (int b = 1; b <= h.Faces; b++) TerraceRow(t, b, false);
            // Corners at the landings: on the terrace above, either side of the opening, against the parapet and facing up the terrace.
            foreach (CityHill.Way w in h.Ways)
            {
                if (w.Stair) continue;
                CityHill.FaceLine fl = h.Lines[w.Face];
                float la = w.SB - w.Dir * CityHill.LandLen, lb = w.SB + w.Dir * CityHill.WallThick;
                foreach (float sc in new[] { Mathf.Min(la, lb) - 2.2f, Mathf.Max(la, lb) + 2.2f })
                {
                    if (sc < 1f || sc > fl.Length - 1f) continue;
                    fl.At(sc, out float lu, out float lv, out float lnu, out float lnv);
                    h.OffsetPoint(w.Face, lu, lv, lnu, lnv, CityHill.LipThick + 0.2f, out float ou, out float ov);
                    if (h.OutsideUV(ou, ov) > -3.5f) continue;
                    Slot(t, VignetteKind.Corner, At(h, ou, ov), At(h, lnu, lnv) - At(h, 0f, 0f), 2.8f, 2.3f);
                }
            }

            // The flat side's lanes: houses facing each parallel lane from both sides, back to back with the next row; the cross lanes lined.
            foreach (var (off, a0, a1) in parallels)
            {
                Func<float, Vector3> toRoad = u => -SettlementKit.Right(Tangent(kit, u)) * sq;
                Row(t, u => kit.Beside(u, sq, off - 2.4f), u => -toRoad(u), a0, a1, sq, BackHouses, GapMin, GapMax, true, 10.5f);
                Row(t, u => kit.Beside(u, sq, off + 2.4f), toRoad, a0, a1, sq, BackHouses, GapMin, GapMax, true, 10.5f);
            }

            foreach (var (along, side) in lanes)
            {
                if (side != sq) continue;
                Vector3 tan = Tangent(kit, along);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 across = tan * s;
                    Row(t, u => kit.Beside(along, side, u) + across * LaneFront, u => -across, StreetFront + 12f, 100f, side, BackHouses, GapMin, GapMax, true, 10f);
                }
            }

            // The terrace streets' wall feet: a slot every 15 m or so where nothing stands.
            Vector3 lastFoot = new Vector3(1e6f, 0f, 1e6f);
            foreach (var (at, outward) in t.WallFeet)
            {
                if (FlatDistance(at, lastFoot) < 15f || !Free(t, at, 1.2f, false)) continue;
                lastFoot = at;
                Slot(t, VignetteKind.WallFoot, at - outward * 1.9f, outward, 4f);
            }

            Vector3 well = kit.Beside(0f, sq, 16f);
            if (!Vignette(t, VignetteKind.Plaza, well - toRoadSq * 2f, toRoadSq, 10f) &&
                kit.TryProp(PropRole.Well, kit.Keys.PickVariant(PropRole.Well, rng), well, 0f, 1f, 2.2f))
                kit.Anchor(AnchorKind.Well, well + toRoadSq * 2f, ScatterPlanner.Yaw(-toRoadSq), sq);
            for (int k = 0; k < 6; k++)
            {
                float along = -15f + 15f * (k % 3);
                Vector3 at = kit.Beside(along, sq, k < 3 ? 9f : 23f);
                Vector3 face = k < 3 ? -toRoadSq : toRoadSq;
                if (Vignette(t, VignetteKind.Stall, at - face * 1.2f, face, 6f)) continue;
                float yaw = ScatterPlanner.Yaw(face);
                if (kit.TryProp(PropRole.MarketStall, kit.Keys.PickVariant(PropRole.MarketStall, rng), at, yaw, 1f, 2.6f))
                    kit.Anchor(AnchorKind.Stall, at + face * 2.2f, yaw + 180f, sq);
            }

            for (int k = 0; k < 4; k++) kit.TryProp(PropRole.Bench, kit.Beside(k < 2 ? -SqHalf + 2.2f : SqHalf - 2.2f, sq, k % 2 == 0 ? 10f : 22f), ScatterPlanner.Yaw(Tangent(kit, 0f)) + (k < 2 ? 90f : -90f), 1.2f);

            // Beside the doors: a slot at every other house where the front has room beside its door, at every terrace house (kept off the
            // lower parapet's lip where the house faces the deck).
            int n = 0;
            foreach (PlacedBuilding b in new List<PlacedBuilding>(kit.Buildings))
            {
                if (b.Role != PropRole.House) continue;
                bool terrace = t.TerraceHouses.TryGetValue(b.PropIndex, out int below);
                if (!terrace && n++ % 2 == 1) continue;
                float side = b.Shape.Door.x - b.Shape.ExtentCentre.x >= 0f ? -1f : 1f;
                Vector3 origin = b.Door + b.Across * (side * 2.7f) + b.Facing * 0.2f;
                float deep = 99f;
                if (terrace && below >= 0)
                {
                    h.Local(origin.x, origin.z, out float ou, out float ov);
                    deep = Mathf.Max(0f, h.Behind(below, ou, ov) - CityHill.LipThick - 0.5f);
                }

                Slot(t, VignetteKind.DoorSide, origin, b.Facing, 3f, deep);
            }

            // ------------------------------------------------ outside the gates
            for (int side = -1; side <= 1; side += 2)
            {
                Func<float, Vector3> facing = u => -SettlementKit.Right(Tangent(kit, u)) * side;
                Row(t, u => kit.Beside(u, side, StreetFront + 0.4f), facing, stats.GateOutAlong + 14f, stats.GateOutAlong + 24f, side, BackHouses, 2.5f, 6f, false, 10f);
                Row(t, u => kit.Beside(u, side, StreetFront + 0.4f), facing, stats.GateInAlong - 24f, stats.GateInAlong - 14f, side, BackHouses, 2.5f, 6f, false, 10f);
            }
        }

        /// <summary>
        /// Stone underfoot: the open ground inside the wall left after the streets and houses is paved (3 m rows of cobbles), except the
        /// terrace faces behind their walls and a few small gardens round a tree where the ground is widest. Measures the open-grass share.
        /// </summary>
        private static void Pave(Town t, Func<Vector3, float> outside)
        {
            SettlementKit kit = t.Kit;
            CityHill h = t.Hill;
            const float cell = 3f;
            int inside = 0, grass = 0;
            var trees = new List<Vector3>();
            float cx = (float)h.CX, cz = (float)h.CZ;
            for (float z = cz - 105f; z <= cz + 105f; z += cell)
            {
                float runStart = float.NaN, runEnd = 0f;
                for (float x = cx - 105f; x <= cx + 105f + cell; x += cell)
                {
                    var p = new Vector3(x, 0f, z);
                    bool pave = false;
                    if (x <= cx + 105f && outside(p) < -3.5f)
                    {
                        inside++;
                        bool built = !Free(t, p, 0.2f, false) || kit.RoadDistance(p) < kit.Half + SettlementKit.Verge + 1f || Cobbled(t, p);
                        bool garden = false;
                        foreach (Vector3 tr in trees) if (FlatDistance(tr, p) < 4.5f) garden = true;
                        if (!built && !garden && t.Rng.NextDouble() < 0.03 && Free(t, p, 4f, true) && AutumnTree(t, p))
                        {
                            trees.Add(p);
                            garden = true;
                        }

                        if (garden) grass++;
                        else if (!built) pave = true;
                    }

                    if (pave)
                    {
                        if (float.IsNaN(runStart)) runStart = x;
                        runEnd = x;
                    }
                    else if (!float.IsNaN(runStart))
                    {
                        Vector3 a = new Vector3(runStart - 0.5f * cell, 0f, z), b = new Vector3(runEnd + 0.5f * cell, 0f, z);
                        t.Cobbles.Add((a, b, cell + 0.4f));
                        t.Stats.PavedArea += (runEnd - runStart + cell) * cell;
                        runStart = float.NaN;
                    }
                }
            }

            t.Stats.OpenGrassShare = inside > 0 ? grass / (float)inside : 0f;
            t.Stats.Gardens = trees.Count;
        }

        private static bool Cobbled(Town t, Vector3 p)
        {
            foreach (var (a, b, w) in t.Cobbles)
            {
                float ex = b.x - a.x, ez = b.z - a.z, l2 = ex * ex + ez * ez;
                float f = l2 > 1e-9f ? Mathf.Clamp01(((p.x - a.x) * ex + (p.z - a.z) * ez) / l2) : 0f;
                float dx = a.x + ex * f - p.x, dz = a.z + ez * f - p.z;
                if (dx * dx + dz * dz < 0.25f * w * w) return true;
            }

            return false;
        }

        private static void MainRow(Town t, float from, float to, int side)
        {
            SettlementKit kit = t.Kit;
            if (to - from < 5f) return;
            Row(t, u => kit.Beside(u, side, StreetFront), u => -SettlementKit.Right(Tangent(kit, u)) * side, from, to, side, TallHouses, GapMin, GapMax, true, 12f);
        }

        /// <summary>
        /// A row of houses along a line (<paramref name="at"/>(u) the middle of a front, <paramref name="facing"/>(u) its facing) from
        /// <paramref name="from"/> to <paramref name="to"/>: neighbours <paramref name="gap0"/>..<paramref name="gap1"/> apart by their measured
        /// extents (eaves included), never the same preset as the house placed before (<see cref="Pick"/>).
        /// </summary>
        private static void Row(Town t, Func<float, Vector3> at, Func<float, Vector3> facing, float from, float to, int side, int[] pool, float gap0, float gap1, bool front, float maxDepth)
        {
            float u = from;
            int previous = -1;
            while (u < to - 5.5f)
            {
                PlacedBuilding b = null;
                float w = 0f;
                foreach (int v in Pick(t, pool))
                {
                    BuildingShape sh = BuildingTable.For(PropRole.House, v);
                    if (sh == null || u + sh.Width > to || sh.Depth > maxDepth) continue;
                    b = Place(t, PropRole.House, v, sh, at(u + 0.5f * sh.Width), facing(u + 0.5f * sh.Width), side, gap0);
                    if (b == null) continue;
                    w = sh.Width;
                    break;
                }

                if (b == null)
                {
                    u += 0.5f;
                    previous = -1;
                    continue;
                }

                Count(t.Stats, b);
                Door(t, b, true);
                if (front && previous >= 0) t.Stats.FrontPairs.Add((previous, b.PropIndex));
                previous = b.PropIndex;
                u += w + gap0 + (gap1 - gap0) * (float)t.Rng.NextDouble();
            }
        }

        /// <summary>A terrace house's back off its wall's low face, metres (the wall's reservation, <see cref="WallClear"/>, holds it there).</summary>
        public const float TerraceBack = WallClear + 0.12f, ParapetBack = CityHill.WallThick + WallClear + 0.12f;
        /// <summary>The walk a riser row leaves along the deck in front of it, to the lower parapet's lip; the street between two rows.</summary>
        public const float TerraceWalk = 3.2f, TerraceStreet = 5f;
        /// <summary>
        /// The walk a row in front of a ramp's outer wall leaves to the lower parapet's lip, metres: the bench is a ramp's width shorter
        /// there (about 10 m before the house's back gap), and two presets must fit for the row never to repeat one (House_08 and
        /// House_03, 6.3 and 7.7 m deep), so the houses stand over a narrow alley along the parapet, as on the demo's terraces.
        /// </summary>
        public const float RampWalk = 1f;
        /// <summary>The widest gap between two terrace houses (shoulder to shoulder: <see cref="GapMin"/> to this), metres.</summary>
        public const float TerraceGap = 0.6f;
        /// <summary>A house keeps this far from a way up (its verge), metres.</summary>
        public const float WayVerge = 1.5f;

        /// <summary>
        /// One row of terrace houses on bench <paramref name="b"/> (between face b - 1 below and face b above), walked along a face's line:
        /// <paramref name="riser"/> the houses stand in front of face b with their backs to it (or to the outer wall of the ramp at its foot)
        /// and face down the hill, their fronts leaving <see cref="TerraceWalk"/> m of deck to the lower parapet's lip; otherwise they
        /// stand behind face b - 1 with their backs to its parapet and face up the hill, only where the bench leaves room for the riser row,
        /// a <see cref="TerraceStreet"/> m street and themselves. Neighbours <see cref="GapMin"/>..<see cref="GapMax"/> apart; where no
        /// house fits over 6 m of a riser row, a garden tree.
        /// </summary>
        private static void TerraceRow(Town t, int b, bool riser)
        {
            CityHill h = t.Hill;
            CityHill.FaceLine f = h.Lines[riser ? b : b - 1];
            if (f.U.Length < 2) return;
            int face = f.Face;
            float sign = riser ? -1f : 1f;
            float deepest = 0f;
            foreach (int v in HillHouses) { BuildingShape sh = BuildingTable.For(PropRole.House, v); if (sh != null) deepest = Mathf.Max(deepest, sh.Depth); }
            float s = 0f, gapFrom = 0f;
            int previous = -1;
            while (s < f.Length - 4f)
            {
                PlacedBuilding placed = null;
                float width = 0f;
                foreach (int v in Pick(t, HillHouses))
                {
                    BuildingShape sh = BuildingTable.For(PropRole.House, v);
                    if (sh == null || s + sh.Width > f.Length) continue;
                    f.At(s + 0.5f * sh.Width, out float lu, out float lv, out float nu, out float nv);
                    if (h.OutsideUV(lu, lv) > -3f) continue;
                    // The bench's depth here, from this face's line to the other face's.
                    float bench = riser ? h.Behind(b - 1, lu, lv) : b < h.Faces ? -h.Behind(b, lu, lv) : float.MaxValue;
                    for (int k = 0; k < (riser ? 2 : 1); k++)
                    {
                        // Against the riser, or (where a ramp runs at its foot) against the ramp's outer wall with a narrower walk in front.
                        float back = !riser ? ParapetBack : k == 0 ? TerraceBack : 2f * CityHill.RampHalf + WayVerge + 0.05f;
                        float reach = back + sh.Depth;
                        if (riser && reach > bench - CityHill.LipThick - (k == 0 ? TerraceWalk : RampWalk)) continue;
                        if (!riser && b < h.Faces && reach + TerraceStreet + TerraceBack + deepest > bench) continue;
                        h.OffsetPoint(face, lu, lv, nu, nv, sign * reach, out float fu, out float fv);
                        h.Inward(fu, fv, out float iu, out float iv);
                        Vector3 front = At(h, fu, fv), facing = (At(h, iu, iv) - At(h, 0f, 0f)) * sign;
                        if (!TerraceFits(t, sh, front, facing, riser ? -1 : b - 1)) continue;
                        placed = Place(t, PropRole.House, v, sh, front, facing, h.Side, GapMin);
                        if (placed != null) break;
                    }

                    if (placed == null) continue;
                    width = sh.Width;
                    break;
                }

                if (placed == null)
                {
                    s += 0.5f;
                    previous = -1;
                    continue;
                }

                if (riser) TerraceGarden(t, f, gapFrom, s, sign);
                Count(t.Stats, placed);
                Door(t, placed);
                t.TerraceHouses[placed.PropIndex] = riser ? b - 1 : -1;
                if (previous >= 0) t.Stats.FrontPairs.Add((previous, placed.PropIndex));
                previous = placed.PropIndex;
                s += width + GapMin + (TerraceGap - GapMin) * (float)t.Rng.NextDouble();
                gapFrom = s;
            }

            if (riser) TerraceGarden(t, f, gapFrom, f.Length, sign);
        }

        /// <summary>
        /// Whether a terrace house at <paramref name="front"/> keeps <see cref="WayVerge"/> off every way up (but those up face
        /// <paramref name="below"/>, the wall the house backs onto from above: its ramp runs below the wall, its landing and flight top are
        /// kept free by their own reservations) and stands clear of the flags on the parapets.
        /// </summary>
        private static bool TerraceFits(Town t, BuildingShape shape, Vector3 front, Vector3 facing, int below)
        {
            CityHill h = t.Hill;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) return false;
            facing.Normalize();
            float yaw = ScatterPlanner.Yaw(facing) - shape.FrontYaw;
            Vector3 c = shape.ExtentCentre;
            Vector3 localFront = shape.FrontYaw > 45f ? new Vector3(shape.ExtentX1, 0f, c.z) : new Vector3(c.x, 0f, shape.ExtentZ1);
            Vector3 pivot = front - BuildingTable.Direction(yaw, localFront);
            pivot.y = 0f;
            Vector3[] corners = shape.ExtentOutline();
            var rect = new Vector3[corners.Length];
            for (int i = 0; i < corners.Length; i++) rect[i] = BuildingTable.ToWorld(pivot, yaw, corners[i]);
            // The outline's edges every metre or so, and its middle.
            for (int i = 0; i < rect.Length; i++)
            {
                Vector3 a = rect[i], e = rect[(i + 1) % rect.Length];
                int steps = Math.Max(1, Mathf.CeilToInt(FlatDistance(a, e)));
                for (int k = 0; k < steps; k++)
                {
                    Vector3 p = Vector3.Lerp(a, e, k / (float)steps);
                    h.Local(p.x, p.z, out float u, out float v);
                    if (WayDistance(h, u, v, below) < WayVerge) return false;
                }
            }

            foreach (PropPlacement p in t.Kit.Props) if (p.role == PropRole.CityBanner && PolygonDistance(rect, p.position) < 0.8f) return false;
            return true;
        }

        /// <summary>A garden tree where a riser row left more than 6 m between arc lengths <paramref name="s0"/> and <paramref name="s1"/> of its face's line.</summary>
        private static void TerraceGarden(Town t, CityHill.FaceLine f, float s0, float s1, float sign)
        {
            if (s1 - s0 < 6f) return;
            CityHill h = t.Hill;
            f.At(0.5f * (s0 + s1), out float lu, out float lv, out float nu, out float nv);
            h.OffsetPoint(f.Face, lu, lv, nu, nv, sign * (TerraceBack + 2.6f), out float gu, out float gv);
            Vector3 at = At(h, gu, gv);
            if (h.OutsideUV(gu, gv) > -4f || WayDistance(h, gu, gv) < 3f || h.OnCastle(gu, gv, 2f) || !Free(t, at, 2f, false) || t.Kit.Occupancy.Overlaps(at.x, at.z, 1.2f)) return;
            if (!t.Kit.OffRoad(at, 3f) || !AutumnTree(t, at)) return;
            t.Kit.Occupancy.Add(new Vector3(at.x, 0f, at.z), 1.2f);
            t.Stats.TerraceGardens++;
        }

        /// <summary>The pool's presets in a random order, the last placed house's preset left out (never the same twice in a row).</summary>
        private static List<int> Pick(Town t, int[] pool)
        {
            var list = new List<int>(pool.Length);
            foreach (int v in pool) if (v != t.LastHouse) list.Add(v);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = t.Rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }

            return list;
        }

        /// <summary>
        /// Places a building if its measured extent keeps <paramref name="gap"/> from every footprint placed so far (exact rectangles, so street
        /// fronts close up) and off the reserved ground; the kit still refuses the road, its verge, the window's edge and steep ground.
        /// </summary>
        private static PlacedBuilding Place(Town t, string role, int variant, BuildingShape shape, Vector3 front, Vector3 facing, int side, float gap)
        {
            SettlementKit kit = t.Kit;
            if (shape == null || !kit.Keys.Has(role)) return null;
            if (role == PropRole.House && kit.Buildings.Count >= MaxBuildings) return null;
            if (role != PropRole.LandmarkChapel && Math.Min(variant, kit.Keys.Count(role) - 1) != variant) return null;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) return null;
            facing.Normalize();
            float yaw = ScatterPlanner.Yaw(facing) - shape.FrontYaw;
            Vector3 c = shape.ExtentCentre;
            Vector3 localFront = shape.FrontYaw > 45f ? new Vector3(shape.ExtentX1, 0f, c.z) : new Vector3(c.x, 0f, shape.ExtentZ1);
            Vector3 pivot = front - BuildingTable.Direction(yaw, localFront);
            Vector3[] corners = shape.ExtentOutline();
            var rect = new Vector3[corners.Length];
            for (int i = 0; i < corners.Length; i++) rect[i] = BuildingTable.ToWorld(new Vector3(pivot.x, 0f, pivot.z), yaw, corners[i]);
            foreach (Vector3 corner in rect) if (t.Hill.Outside(corner.x, corner.z) > -3.6f) return null;   // inside the wall, off its walk
            Vector3 mid = Centre(rect, out float reach);
            foreach (Vector3[] other in t.Rects)
            {
                Vector3 oc = Centre(other, out float or);
                if (FlatDistance(oc, mid) < reach + or + gap + 0.1f && !Apart(rect, other, gap)) return null;
            }

            foreach (Vector3 q in t.Reserved)
            {
                if (FlatDistance(q, mid) < reach + q.y && PolygonDistance(rect, q) < q.y) return null;
            }
            PlacedBuilding b = kit.TryShape(role, variant, shape, front, facing, side, 0f, false);
            if (b == null) return null;
            t.Rects.Add(rect);
            if (role == PropRole.House) t.LastHouse = variant;
            return b;
        }

        private static void Door(Town t, PlacedBuilding b, bool yard = false)
        {
            t.Kit.Anchor(AnchorKind.Door, b.Door, ScatterPlanner.Yaw(b.Facing), b.Side, b.PropIndex);
            t.Strip(b.Door, b.Door + b.Facing * 3f, 2f);   // the door step's cobbles, kept free in front of the door
            if (yard) t.Kit.Yard(b.At(new Vector3(0.5f * (b.Shape.BodyX0 + b.Shape.BodyX1), 0f, b.Shape.BodyZ0 - 2.4f)), 2.2f);
        }

        /// <summary>Whether a piece of radius <paramref name="r"/> at <paramref name="at"/> keeps off every footprint (and the reserved ground).</summary>
        private static bool Free(Town t, Vector3 at, float r, bool reservedToo)
        {
            foreach (Vector3[] rect in t.Rects)
            {
                if (FlatDistance(Centre(rect, out float reach), at) < reach + r + 0.3f && PolygonDistance(rect, new Vector3(at.x, 0f, at.z)) < r + 0.2f) return false;
            }
            if (!reservedToo) return true;
            foreach (Vector3 q in t.Reserved) if (FlatDistance(q, at) < q.y + r - 0.6f && q.y < 3f) return false;
            return t.Kit.OffRoad(at, r);
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>Whether two convex outlines lie at least <paramref name="gap"/> apart along one of their edge normals.</summary>
        private static bool Apart(Vector3[] a, Vector3[] b, float gap) => Gap(a, b, gap) || Gap(b, a, gap);

        private static bool Gap(Vector3[] a, Vector3[] b, float gap)
        {
            for (int i = 0; i < a.Length; i++)
            {
                Vector3 p = a[i], q = a[(i + 1) % a.Length];
                float nx = -(q.z - p.z), nz = q.x - p.x, len = Mathf.Sqrt(nx * nx + nz * nz);
                if (len < 1e-6f) continue;
                nx /= len;
                nz /= len;
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (Vector3 v in a) { float d = v.x * nx + v.z * nz; amin = Math.Min(amin, d); amax = Math.Max(amax, d); }
                foreach (Vector3 v in b) { float d = v.x * nx + v.z * nz; bmin = Math.Min(bmin, d); bmax = Math.Max(bmax, d); }
                if (amax + gap <= bmin || bmax + gap <= amin) return true;
            }

            return false;
        }

        /// <summary>An outline's centre and the radius that holds it.</summary>
        private static Vector3 Centre(Vector3[] poly, out float reach)
        {
            Vector3 c = Vector3.zero;
            foreach (Vector3 v in poly) c += v;
            c /= poly.Length;
            reach = 0f;
            foreach (Vector3 v in poly) reach = Mathf.Max(reach, FlatDistance(v, c));
            return c;
        }

        /// <summary>Distance from a point to a convex outline (0 inside).</summary>
        private static float PolygonDistance(Vector3[] poly, Vector3 p)
        {
            bool inside = true;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Length];
                float ex = b.x - a.x, ez = b.z - a.z, l2 = ex * ex + ez * ez;
                if (ex * (p.z - a.z) - ez * (p.x - a.x) < 0f) inside = false;
                float f = l2 > 1e-9f ? Mathf.Clamp01(((p.x - a.x) * ex + (p.z - a.z) * ez) / l2) : 0f;
                float dx = a.x + ex * f - p.x, dz = a.z + ez * f - p.z;
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }

            // The outline's winding is not fixed: inside when on the same side of every edge.
            if (inside) return 0f;
            bool right = true;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Length];
                if ((b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x) > 0f) right = false;
            }

            return right ? 0f : best;
        }

        private static void Count(CityStats stats, PlacedBuilding b)
        {
            if (b != null) Add(stats, b.Role, b.Shape.Triangles > 0 ? b.Shape.Triangles : 20000);
        }

        private static void Add(CityStats stats, string role, int tris)
        {
            stats.Buildings.TryGetValue(role, out int n);
            stats.Buildings[role] = n + 1;
            stats.Triangles += tris;
        }
    }
}
