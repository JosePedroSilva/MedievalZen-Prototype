using System;
using System.Collections.Generic;
using MedievalZen.People;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>A building the kit placed: its prop index, pivot, yaw, measured shape, door and front direction (window-local).</summary>
    public sealed class PlacedBuilding
    {
        /// <summary>Family and variant.</summary>
        public string Role;

        /// <summary>Variant index.</summary>
        public int Variant;

        /// <summary>Index of the placement in the dressing's props.</summary>
        public int PropIndex;

        /// <summary>Pivot (y = the pivot's height) and yaw (degrees).</summary>
        public Vector3 Pivot;

        /// <summary>Yaw of the placement, degrees.</summary>
        public float Yaw;

        /// <summary>The measured preset.</summary>
        public BuildingShape Shape;

        /// <summary>The door's foot on the ground.</summary>
        public Vector3 Door;

        /// <summary>Unit direction out of the front.</summary>
        public Vector3 Facing;

        /// <summary>Side of the road (+1 right, -1 left).</summary>
        public int Side;

        /// <summary>A local point of the preset in the window.</summary>
        public Vector3 At(Vector3 local) => BuildingTable.ToWorld(Pivot, Yaw, local);

        /// <summary>Unit direction across the front (local +X for a +Z front), in the window.</summary>
        public Vector3 Across => new Vector3(Facing.z, 0f, -Facing.x);
    }

    /// <summary>
    /// The placing rules the endless road's inn and village composers share (endless only; pure; deterministic from the caller's
    /// random stream): the road's frame along a polyline 2 m apart, buildings placed by their measured footprint and door
    /// (<see cref="BuildingTable"/>) facing the road, never on the road or its verge (<see cref="Verge"/> beyond the road's edge for every
    /// footprint), seated on the ground by their stone base, props with a footprint and a clearance, rock-like pieces through
    /// <see cref="RockPlacer"/>, and the worn paths, yards and people's spots (<see cref="PlaceAnchor"/>) of the place.
    /// </summary>
    public sealed class SettlementKit
    {
        /// <summary>Kept clear beyond the road's edge by every footprint, metres.</summary>
        public const float Verge = 1.5f;

        /// <summary>Gap kept between a building's extent and any other footprint, metres.</summary>
        public const float BuildingGap = 0.9f;

        /// <summary>Steepest rise (highest minus lowest ground under the body) a stone-based building accepts, metres.</summary>
        public const float MaxRise = 1.9f;

        private readonly WorldGenParams p;
        private readonly float[,] map;
        private readonly IReadOnlyList<Vector3> road;
        private readonly int centre;

        /// <summary>Creates a kit over the window's map, roads and occupancy, writing props into <paramref name="props"/>.</summary>
        public SettlementKit(WorldGenParams p, float[,] map, RoadDistanceField roads, PaletteKeys keys, OccupancyMap occupancy, List<PropPlacement> props,
            IReadOnlyList<Vector3> road, int centreIndex, System.Random rng)
        {
            this.p = p;
            this.map = map;
            this.road = road;
            centre = centreIndex;
            Roads = roads;
            Keys = keys;
            Occupancy = occupancy;
            Props = props;
            Rng = rng;
            Half = p.roadWidth * 0.5f;
        }

        /// <summary>Distance field of the road (window-local).</summary>
        public RoadDistanceField Roads { get; }

        /// <summary>The palette's families.</summary>
        public PaletteKeys Keys { get; }

        /// <summary>Occupied circles.</summary>
        public OccupancyMap Occupancy { get; }

        /// <summary>Placed props (settlement id set by the caller).</summary>
        public List<PropPlacement> Props { get; }

        /// <summary>The place's random stream.</summary>
        public System.Random Rng { get; }

        /// <summary>Half the road's width.</summary>
        public float Half { get; }

        /// <summary>Worn paths (x0, z0, x1, z1).</summary>
        public readonly List<Vector4> Paths = new List<Vector4>();

        /// <summary>Trodden patches (x, radius, z).</summary>
        public readonly List<Vector3> Yards = new List<Vector3>();

        /// <summary>Spots for the place's people.</summary>
        public readonly List<PlaceAnchor> Anchors = new List<PlaceAnchor>();

        /// <summary>Buildings placed, in order.</summary>
        public readonly List<PlacedBuilding> Buildings = new List<PlacedBuilding>();

        /// <summary>Trees the place plants (fruit trees), local.</summary>
        public readonly List<TreePlacement> Trees = new List<TreePlacement>();

        /// <summary>Road point and unit tangent <paramref name="along"/> metres from the centre (2 m between points, clamped).</summary>
        public void Frame(float along, out Vector3 pos, out Vector3 tangent)
        {
            float f = centre + along / 2f;
            int i = Mathf.Clamp((int)Mathf.Floor(f), 0, road.Count - 2);
            float t = Mathf.Clamp01(f - i);
            Vector3 a = road[i], b = road[i + 1];
            pos = new Vector3(a.x + (b.x - a.x) * t, 0f, a.z + (b.z - a.z) * t);
            var d = new Vector3(b.x - a.x, 0f, b.z - a.z);
            tangent = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        /// <summary>Unit normal to the right of a road tangent.</summary>
        public static Vector3 Right(Vector3 tangent) => new Vector3(tangent.z, 0f, -tangent.x);

        /// <summary>A point <paramref name="offset"/> metres to side <paramref name="side"/> of the road, <paramref name="along"/> from the centre.</summary>
        public Vector3 Beside(float along, float side, float offset)
        {
            Frame(along, out Vector3 on, out Vector3 t);
            return on + Right(t) * side * offset;
        }

        /// <summary>
        /// Where a composer plans its own ground (a walled city's terraces and decks: <see cref="CityHill.SurfaceOffset"/>), the height things
        /// are seated on at a window point, or NaN where the terrain's built height is the ground. Null: the terrain everywhere.
        /// </summary>
        public Func<float, float, float> Surface;

        /// <summary>Ground height at a window point (the planned <see cref="Surface"/> where there is one, else the built terrain).</summary>
        public float Ground(Vector3 at)
        {
            if (Surface != null)
            {
                float s = Surface(at.x, at.z);
                if (!float.IsNaN(s)) return s;
            }

            return HeightmapGenerator.SampleHeight(map, p, at.x, at.z);
        }

        /// <summary>The point with its y on the ground.</summary>
        public Vector3 OnGround(Vector3 at) => new Vector3(at.x, Ground(at), at.z);

        private bool InWindow(Vector3 at, float margin) => at.x > margin && at.z > margin && at.x < p.worldSize - margin && at.z < p.worldSize - margin;

        /// <summary>
        /// True when a footprint of <paramref name="radius"/> at <paramref name="at"/> keeps off the road and its verge, by the exact distance
        /// to the road's polyline (the window's distance field is conservative by 1.5 m and would push roadside pieces back too far).
        /// </summary>
        public bool OffRoad(Vector3 at, float radius) => RoadDistance(at) >= Half + Verge + radius;

        /// <summary>Distance from a window point to the road's centre line (its 2 m polyline through the place), metres.</summary>
        public float RoadDistance(Vector3 at)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < road.Count; i++)
            {
                Vector3 a = road[i], b = road[i + 1];
                float ex = b.x - a.x, ez = b.z - a.z;
                float len = ex * ex + ez * ez;
                float t = len > 1e-9f ? Mathf.Clamp01(((at.x - a.x) * ex + (at.z - a.z) * ez) / len) : 0f;
                float dx = a.x + ex * t - at.x, dz = a.z + ez * t - at.z;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }

            return Mathf.Sqrt(best);
        }

        /// <summary>True when (x, z) is free of other footprints and off the road and verge, inside the window.</summary>
        public bool Clear(Vector3 at, float radius) => InWindow(at, radius + 1f) && OffRoad(at, radius) && !Occupancy.Overlaps(at.x, at.z, radius);

        // ------------------------------------------------------------------ buildings

        /// <summary>
        /// Places building <paramref name="role"/> / <paramref name="variant"/> with the middle of its front (the farthest its extent
        /// reaches out of the door side) at <paramref name="frontCentre"/>, its front facing <paramref name="facing"/>. Refused when any
        /// of its outline comes within <see cref="Verge"/> of the road's edge (plus <paramref name="extraClearance"/>), it overlaps another
        /// footprint, leaves the window, or the ground under it rises more than its base allows. Seated so the ground lies at the
        /// pivot on the highest corner less 0.55 m (the stone base shows all round, the door steps start at the ground).
        /// </summary>
        public PlacedBuilding TryBuilding(string role, int variant, Vector3 frontCentre, Vector3 facing, int side, float extraClearance = 0f)
        {
            if (!Keys.Has(role)) return null;
            variant = Math.Min(variant, Keys.Count(role) - 1);
            return TryShape(role, variant, BuildingTable.For(role, variant), frontCentre, facing, side, extraClearance);
        }

        /// <summary>
        /// <see cref="TryBuilding"/> with an explicit measured shape (a family whose presets are measured outside the name lookup, such as
        /// the village church, <see cref="BuildingTable.Church"/>); the same refusals and seating. With <paramref name="occupancy"/> false the
        /// occupancy test is left to the caller (the walled city checks its street fronts against the exact footprints, so neighbours can
        /// stand 0.5 m apart); the footprint is still added to the occupancy map.
        /// </summary>
        public PlacedBuilding TryShape(string role, int variant, BuildingShape shape, Vector3 frontCentre, Vector3 facing, int side, float extraClearance = 0f, bool occupancy = true)
        {
            if (shape == null || !Keys.Has(role)) return null;
            variant = Math.Min(variant, Keys.Count(role) - 1);
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) return null;
            facing.Normalize();
            float yaw = ScatterPlanner.Yaw(facing) - shape.FrontYaw;
            Vector3 c = shape.ExtentCentre;
            Vector3 localFront = shape.FrontYaw > 45f ? new Vector3(shape.ExtentX1, 0f, c.z) : new Vector3(c.x, 0f, shape.ExtentZ1);
            Vector3 pivot = frontCentre - BuildingTable.Direction(yaw, localFront);
            pivot.y = 0f;

            foreach (Vector3 corner in shape.ExtentOutline())
            {
                Vector3 w = BuildingTable.ToWorld(pivot, yaw, corner);
                if (!InWindow(w, 1f) || RoadDistance(w) < Half + Verge + extraClearance) return null;
            }

            List<Vector3> circles = Cover(shape, pivot, yaw);
            foreach (Vector3 circle in circles)
            {
                if (occupancy && Occupancy.Overlaps(circle.x, circle.z, circle.y + BuildingGap)) return null;
            }

            // Seat on the ground under the body (corners, edge middles, centre and the door).
            float lo = float.MaxValue, hi = float.MinValue;
            float bx0 = shape.BodyX0, bx1 = shape.BodyX1, bz0 = shape.BodyZ0, bz1 = shape.BodyZ1;
            for (int ix = 0; ix <= 2; ix++)
            {
                for (int iz = 0; iz <= 2; iz++)
                {
                    float h = Ground(BuildingTable.ToWorld(pivot, yaw, new Vector3(Mathf.Lerp(bx0, bx1, ix * 0.5f), 0f, Mathf.Lerp(bz0, bz1, iz * 0.5f))));
                    lo = Mathf.Min(lo, h);
                    hi = Mathf.Max(hi, h);
                }
            }

            float y;
            if (shape.BaseDepth > 0.5f)
            {
                if (hi - lo > Mathf.Min(MaxRise, shape.BaseDepth - 0.4f)) return null;
                y = hi - 0.55f;
            }
            else
            {
                // A building on its own feet (the privy): its floor on the lowest ground, the uphill side no more than 0.7 m higher.
                if (hi - lo > 0.7f) return null;
                y = lo + shape.BaseDepth - 0.05f;
            }

            pivot.y = y;
            foreach (Vector3 circle in circles) Occupancy.Add(new Vector3(circle.x, 0f, circle.z), circle.y);
            var placed = new PlacedBuilding
            {
                Role = role, Variant = variant, PropIndex = Props.Count, Pivot = pivot, Yaw = yaw, Shape = shape, Facing = facing, Side = side,
            };
            placed.Door = OnGround(placed.At(shape.Door));
            Props.Add(new PropPlacement { role = role, variant = variant, position = pivot, yaw = yaw, scale = 1f, settlementId = -1 });
            Buildings.Add(placed);
            return placed;
        }

        /// <summary>Circles (x, radius, z) covering a building's extent rectangle: a grid of cells at most 3.5 m on a side, each circle through its cell's corners.</summary>
        public static List<Vector3> Cover(BuildingShape shape, Vector3 pivot, float yaw)
        {
            float w = shape.ExtentX1 - shape.ExtentX0, d = shape.ExtentZ1 - shape.ExtentZ0;
            int nx = Math.Max(1, (int)Math.Ceiling(w / 3.5f)), nz = Math.Max(1, (int)Math.Ceiling(d / 3.5f));
            float cw = w / nx, cd = d / nz;
            float r = 0.5f * Mathf.Sqrt(cw * cw + cd * cd);
            var list = new List<Vector3>(nx * nz);
            for (int ix = 0; ix < nx; ix++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    var local = new Vector3(shape.ExtentX0 + (ix + 0.5f) * cw, 0f, shape.ExtentZ0 + (iz + 0.5f) * cd);
                    Vector3 at = BuildingTable.ToWorld(pivot, yaw, local);
                    list.Add(new Vector3(at.x, r, at.z));
                }
            }

            return list;
        }

        // ------------------------------------------------------------------ props

        /// <summary>Places a prop if its footprint is clear (off the road and verge, of other footprints, in the window); y on the ground.</summary>
        public bool TryProp(string role, int variant, Vector3 at, float yaw, float scale, float radius, float lift = 0f, float roadRadius = -1f)
        {
            if (!Keys.Has(role)) return false;
            if (roadRadius < 0f ? !Clear(at, radius) : !InWindow(at, radius + 1f) || !OffRoad(at, roadRadius) || Occupancy.Overlaps(at.x, at.z, radius)) return false;
            Add(role, Math.Min(Math.Max(0, variant), Keys.Count(role) - 1), at, yaw, scale, radius, lift);
            return true;
        }

        /// <summary>
        /// Places a piece the Unity side builds in code (no palette family, variant 0: <see cref="PropRole.InnSign"/>) if its footprint is
        /// clear (off the road and verge, of other footprints, in the window); y on the ground. Draws no randomness.
        /// </summary>
        public bool TryBuilt(string role, Vector3 at, float yaw, float radius)
        {
            if (!Clear(at, radius)) return false;
            Add(role, 0, at, yaw, 1f, radius, 0f);
            return true;
        }

        /// <summary>Places a prop with a random variant (see the full overload).</summary>
        public bool TryProp(string role, Vector3 at, float yaw, float radius) => TryProp(role, Keys.PickVariant(role, Rng), at, yaw, 1f, radius);

        /// <summary>Places a piece without the footprint check (pieces on or against another), still never on the road or verge.</summary>
        public bool Force(string role, Vector3 at, float yaw, float radius = 0f)
        {
            if (!Keys.Has(role) || !InWindow(at, 1f) || !OffRoad(at, 0.2f)) return false;
            Add(role, Keys.PickVariant(role, Rng), at, yaw, 1f, radius, 0f);
            return true;
        }

        /// <summary><see cref="Force"/> with a chosen variant (benches against a wall, pieces of a set).</summary>
        public bool ForceVariant(string role, int variant, Vector3 at, float yaw, float radius = 0f)
        {
            if (!Keys.Has(role) || !InWindow(at, 1f) || !OffRoad(at, 0.2f)) return false;
            Add(role, Math.Min(Math.Max(0, variant), Keys.Count(role) - 1), at, yaw, 1f, radius, 0f);
            return true;
        }

        /// <summary>A rock-like piece (cairn stones) seated by <see cref="RockPlacer"/> from its measured metrics.</summary>
        public bool TryRock(string role, Vector3 at, float yaw, float scale, float radius)
        {
            if (!Keys.Has(role) || !Clear(at, radius)) return false;
            return TryRock(role, Keys.PickVariant(role, Rng), at, yaw, scale, radius);
        }

        /// <summary>A given variant of a rock-like piece seated by <see cref="RockPlacer"/> (the walled city's rocks, sized by variant: 2026-10-09).</summary>
        public bool TryRock(string role, int variant, Vector3 at, float yaw, float scale, float radius)
        {
            if (!Keys.Has(role) || !Clear(at, radius)) return false;
            if (!Keys.TryGetMetrics(role, variant, out _)) return false;
            if (!RockPlacer.TrySeat(new RockPlacer.Context(p, map, Keys, null), role, variant, at.x, at.z, yaw, scale, out PropPlacement seated)) return false;
            Occupancy.Add(at, radius);
            seated.settlementId = -1;
            Props.Add(seated);
            return true;
        }

        private void Add(string role, int variant, Vector3 at, float yaw, float scale, float radius, float lift)
        {
            at.y = Ground(at) + lift;
            if (radius > 0f) Occupancy.Add(at, radius);
            Props.Add(new PropPlacement { role = role, variant = variant, position = at, yaw = yaw, scale = scale, settlementId = -1 });
        }

        /// <summary>A fruit tree (on the ground, a little sunk), if the family exists and the spot is in the window.</summary>
        public bool AddTree(string role, Vector3 at)
        {
            if (!Keys.Has(role) || !InWindow(at, 3f)) return false;
            float yaw = (float)(Rng.NextDouble() * 360.0), scale = 0.85f + 0.3f * (float)Rng.NextDouble();
            int variant = Keys.PickVariant(role, Rng);
            at.y = Ground(at) - 0.1f;
            Trees.Add(new TreePlacement { role = role, variant = variant, position = at, yaw = yaw, scale = scale });
            return true;
        }

        // ------------------------------------------------------------------ paths and people

        /// <summary>A worn path between two points.</summary>
        public void Path(Vector3 a, Vector3 b) => Paths.Add(new Vector4(a.x, a.z, b.x, b.z));

        /// <summary>A trodden patch.</summary>
        public void Yard(Vector3 at, float radius) => Yards.Add(new Vector3(at.x, radius, at.z));

        /// <summary>Records a spot for the place's people.</summary>
        public void Anchor(AnchorKind kind, Vector3 at, float facing, int side, int building = -1, float seatHeight = 0f)
        {
            Anchors.Add(new PlaceAnchor { Kind = kind, Position = OnGround(at), Facing = facing, Side = side, Building = building, SeatHeight = seatHeight });
        }

        /// <summary>
        /// The point where a path from <paramref name="from"/> straight toward the road meets the verge (road edge + <see cref="Verge"/>),
        /// on side <paramref name="side"/>: the garden gate.
        /// </summary>
        public Vector3 GateFor(Vector3 from, float along, int side)
        {
            Frame(along, out Vector3 on, out Vector3 t);
            return on + Right(t) * side * (Half + Verge + 0.4f);
        }

        /// <summary>Along-road coordinate of a window point near the place (nearest polyline vertex, metres from the centre).</summary>
        public float AlongOf(Vector3 at)
        {
            int best = centre;
            float bestD = float.MaxValue;
            for (int i = 0; i < road.Count; i++)
            {
                float dx = road[i].x - at.x, dz = road[i].z - at.z, d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return (best - centre) * 2f;
        }

        /// <summary>Yaw (degrees) that turns a prop's local +X along <paramref name="direction"/> (fences, benches, hitching rails).</summary>
        public static float YawAlongX(Vector3 direction) => ScatterPlanner.Yaw(direction) - 90f;
    }
}
