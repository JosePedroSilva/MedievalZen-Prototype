using System;
using System.Collections.Generic;
using System.Linq;
using MedievalZen.People;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>The layout a hamlet or village grew into (endless only; see <see cref="VillageComposer"/>).</summary>
    public enum VillagePlan
    {
        /// <summary>Two staggered rows along the road (a minority): varied setbacks, gaps, a gable or two to the road, a back house.</summary>
        Street = 0,
        /// <summary>An irregular green beside or astride the road, houses facing it round its edge, the well, benches and stalls on it.</summary>
        Green = 1,
        /// <summary>A lane leaves the road, runs behind the roadside plots and rejoins it; houses on both; the smithy at a junction.</summary>
        BackLane = 2,
        /// <summary>A church on the highest ground behind the road in a walled yard with gravestones, houses gathered along its lane below.</summary>
        Church = 3,
        /// <summary>Hamlet: three or four farmsteads (house and barn) round a shared yard up a lane.</summary>
        FarmYard = 4,
        /// <summary>Hamlet: a lane that forks behind the road, farmsteads at the fork and along the branches.</summary>
        LaneFork = 5,
        /// <summary>Hamlet: a small green by the road with farmsteads round it.</summary>
        HamletGreen = 6,
    }

    /// <summary>
    /// Dresses the endless road's hamlets and villages (endless only; the finite world keeps <see cref="SettlementDresser"/>) with the
    /// Fantasy Kingdom building presets (<see cref="BuildingTable"/>) in one of several plans (<see cref="VillagePlan"/>, chosen by
    /// <see cref="PlanFor"/> from the seed, the region and the settlement before it, never the same twice running): houses on the road
    /// with setbacks from the verge to 25 m back behind a garden, houses facing a green, a back lane, a fork or a farmyard, a church on the
    /// highest ground in its walled yard; small random turns and the odd gable to the road; a tight core loosening toward the edges; per house
    /// a plot (yard, garden, fence, privy or outbuilding behind); worn lanes from the road and a path from every door to the nearest lane,
    /// green rim or garden gate; the arch house astride a lane to the fields; a few trees among the houses; lamps, benches, the well, the
    /// smithy, stalls; the orchard and the windmills. The people's spots (<see cref="PlaceAnchor"/>) are recorded. Nothing stands on the
    /// road or its verge (<see cref="SettlementKit"/>). Pure; deterministic from the place's random stream (1090) and, for the plan, streams
    /// 1303 / 1304.
    /// </summary>
    public static class VillageComposer
    {
        /// <summary>Random stream of a place's preferred plan.</summary>
        public const int PlanStream = 1303;

        /// <summary>Random stream of a place's second choice (when the preferred plan repeats the settlement before it).</summary>
        public const int AltStream = 1304;

        /// <summary>Settlements before a place whose plans are replayed to keep neighbours different.</summary>
        private const int History = 5;

        /// <summary>What the composer produced besides props.</summary>
        public sealed class Result
        {
            /// <summary>Houses placed (dwellings: houses, smithy, stables, huts, barns, the arch house, the church).</summary>
            public int Houses;

            /// <summary>The green's (or yard's) centre (window-local) and radius.</summary>
            public Vector3 Green;

            /// <summary>Radius of the green.</summary>
            public float GreenRadius;

            /// <summary>The plan composed.</summary>
            public VillagePlan Plan;
        }

        // ------------------------------------------------------------------ the plan

        /// <summary>
        /// The plan of hamlet or village <paramref name="place"/>: a weighted pick by seed and region (stream 1303), replaced by a second
        /// pick (1304) when it equals the plan of the settlement before it on the road (replayed over the previous few settlements).
        /// </summary>
        public static VillagePlan PlanFor(int seed, JourneyPlan journey, Place place)
        {
            var before = new List<Place>();
            for (double back = 20000.0; journey != null && back <= 160000.0; back *= 2.0)
            {
                before.Clear();
                journey.RegionPlaces(place.S - back, place.S - 1.0, before);
                before.RemoveAll(q => (q.Kind != PlaceKind.Village && q.Kind != PlaceKind.Hamlet) || q.S >= place.S || q.S < place.S - back);
                if (before.Count >= History) break;
            }

            before.Sort((a, b) => a.S.CompareTo(b.S));
            int last = -1;
            for (int i = Math.Max(0, before.Count - History); i < before.Count; i++) last = (int)Choose(seed, journey, before[i], last);
            return Choose(seed, journey, place, last);
        }

        private static VillagePlan Choose(int seed, JourneyPlan journey, Place place, int avoid)
        {
            RegionKind kind = journey != null ? journey.Regions.Get(place.Region).Kind : RegionKind.Meadow;
            bool village = place.Kind == PlaceKind.Village;
            VillagePlan first = Pick(EndlessNoise.Unit(seed, PlanStream, place.Id), village, kind, -1);
            return (int)first != avoid ? first : Pick(EndlessNoise.Unit(seed, AltStream, place.Id), village, kind, avoid);
        }

        private static VillagePlan Pick(double u, bool village, RegionKind kind, int exclude)
        {
            var plans = village
                ? new[] { VillagePlan.Street, VillagePlan.Green, VillagePlan.BackLane, VillagePlan.Church }
                : new[] { VillagePlan.FarmYard, VillagePlan.LaneFork, VillagePlan.HamletGreen };
            var w = new double[plans.Length];
            for (int i = 0; i < plans.Length; i++) w[i] = Weight(plans[i], kind);
            double total = 0.0;
            for (int i = 0; i < plans.Length; i++)
            {
                if ((int)plans[i] == exclude) w[i] = 0.0;
                total += w[i];
            }

            double x = u * total;
            for (int i = 0; i < plans.Length; i++)
            {
                if (x < w[i]) return plans[i];
                x -= w[i];
            }

            for (int i = plans.Length - 1; i >= 0; i--)
            {
                if (w[i] > 0.0) return plans[i];
            }

            return plans[0];
        }

        private static double Weight(VillagePlan plan, RegionKind kind)
        {
            switch (plan)
            {
                // The street village belongs where the site hems it in (a forest clearing, a pass valley); elsewhere it is rare.
                case VillagePlan.Street: return kind == RegionKind.Forest || kind == RegionKind.HighlandPass ? 0.3 : 0.04;
                case VillagePlan.Green: return kind == RegionKind.Meadow || kind == RegionKind.Downs ? 0.42 : 0.3;
                case VillagePlan.BackLane: return kind == RegionKind.Farmland ? 0.42 : 0.3;
                case VillagePlan.Church: return kind == RegionKind.HighlandPass || kind == RegionKind.Downs ? 0.36 : 0.26;
                case VillagePlan.FarmYard: return kind == RegionKind.Farmland ? 0.45 : 0.36;
                case VillagePlan.LaneFork: return kind == RegionKind.Forest ? 0.42 : 0.34;
                default: return kind == RegionKind.Meadow ? 0.36 : 0.26;
            }
        }

        // ------------------------------------------------------------------ composing

        /// <summary>Composes the place with the green plan of a village or the farmyard of a hamlet (callers without the journey's plan).</summary>
        public static Result Compose(SettlementKit kit, bool village, float radius) => Compose(kit, village, radius, village ? VillagePlan.Green : VillagePlan.FarmYard);

        /// <summary>
        /// Composes the place round the kit's road (2 m polyline, the place's centre at the kit's centre index) with <paramref name="plan"/>;
        /// <paramref name="village"/> for the larger place, <paramref name="radius"/> the place's radius (its ground is level to 0.85 of it).
        /// </summary>
        public static Result Compose(SettlementKit kit, bool village, float radius, VillagePlan plan)
        {
            var c = new Composer(kit, village, radius);
            c.Result.Plan = plan;
            switch (plan)
            {
                case VillagePlan.Street: c.Street(); break;
                case VillagePlan.Green: c.GreenVillage(); break;
                case VillagePlan.BackLane: c.BackLane(); break;
                case VillagePlan.Church: c.ChurchVillage(); break;
                case VillagePlan.FarmYard: c.FarmYard(); break;
                case VillagePlan.LaneFork: c.LaneFork(); break;
                default: c.HamletGreen(); break;
            }

            c.Finish();
            return c.Result;
        }

        /// <summary>How a house reaches the rest of the place: its garden gate on the road, or the nearest lane or green.</summary>
        private enum Reach
        {
            Road,
            Lane,
        }

        private sealed class Composer
        {
            public readonly Result Result = new Result();
            private readonly SettlementKit kit;
            private readonly System.Random rng;
            private readonly PaletteKeys keys;
            private readonly bool village;
            private readonly float radius, half, flat;
            private readonly string houseRole;
            private readonly int houseCount;
            private readonly List<PlacedBuilding> dwellings = new List<PlacedBuilding>();
            private readonly List<Reach> reach = new List<Reach>();
            private readonly List<(Vector3 a, Vector3 b)> lanes = new List<(Vector3, Vector3)>();
            private readonly List<(Vector3 c, float r, int side)> hubs = new List<(Vector3, float, int)>();
            private int lastVariant = -1, lastVariant2 = -1;
            private int greenSide;

            public Composer(SettlementKit kit, bool village, float radius)
            {
                this.kit = kit;
                this.village = village;
                this.radius = radius;
                rng = kit.Rng;
                keys = kit.Keys;
                half = kit.Half;
                flat = radius * 0.85f;
                houseRole = keys.Has(PropRole.House) ? PropRole.House : PropRole.Cabin;
                houseCount = keys.Count(houseRole);
                greenSide = rng.NextDouble() < 0.5 ? -1 : 1;
            }

            private float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();

            private int Houses
            {
                get
                {
                    int n = 0;
                    foreach (PlacedBuilding b in dwellings)
                    {
                        if (b.Role == houseRole) n++;
                    }

                    return n;
                }
            }

            // -------------------------------------------------------------- frame

            /// <summary>A point <paramref name="v"/> metres beyond the verge on side <paramref name="s"/>, <paramref name="u"/> along the road.</summary>
            private Vector3 At(float u, int s, float v) => kit.Beside(u, s, half + SettlementKit.Verge + v);

            /// <summary>Unit direction away from the road on side <paramref name="s"/> at <paramref name="u"/>.</summary>
            private Vector3 Away(float u, int s)
            {
                kit.Frame(u, out _, out Vector3 t);
                return SettlementKit.Right(t) * s;
            }

            private Vector3 Along(float u)
            {
                kit.Frame(u, out _, out Vector3 t);
                return t;
            }

            private static Vector3 Turn(Vector3 d, float degrees)
            {
                float r = degrees * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
                return new Vector3(d.x * cs + d.z * sn, 0f, -d.x * sn + d.z * cs);
            }

            private bool OnLevel(Vector3 p, float margin) => (p - Centre).magnitude < flat - margin;

            private Vector3 Centre
            {
                get
                {
                    kit.Frame(0f, out Vector3 c, out _);
                    return c;
                }
            }

            // -------------------------------------------------------------- lanes, hubs, buildings

            /// <summary>A worn lane through the points, kept clear of footprints (1.4 m either side of its line).</summary>
            private void Lane(params Vector3[] pts)
            {
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    kit.Path(a, b);
                    lanes.Add((a, b));
                    float len = (b - a).magnitude;
                    int n = Math.Max(1, (int)Math.Ceiling(len / 2.5f));
                    for (int k = 0; k <= n; k++) kit.Occupancy.Add(Vector3.Lerp(a, b, (float)k / n), 1.4f);
                }
            }

            /// <summary>A green or yard people gather on: kept clear, doors connect to its rim.</summary>
            private void Hub(Vector3 c, float r, int side)
            {
                hubs.Add((c, r, side));
                kit.Occupancy.Add(c, Mathf.Max(1f, r - 2.5f));
            }

            private int NextVariant()
            {
                if (houseCount <= 1) return 0;
                int v = rng.Next(houseCount);
                for (int k = 0; k < 8 && (v == lastVariant || (houseCount > 3 && v == lastVariant2)); k++) v = rng.Next(houseCount);
                return v;
            }

            /// <summary>A house with its front centre at <paramref name="front"/> facing <paramref name="facing"/> (a few tries a little back or aside).</summary>
            private PlacedBuilding House(Vector3 front, Vector3 facing, int side, Reach how, float maxTurn = 7f)
            {
                int variant = NextVariant();
                Vector3 f = Turn(facing.normalized, R(-maxTurn, maxTurn));
                Vector3 across = new Vector3(f.z, 0f, -f.x);
                foreach (Vector3 shift in new[] { Vector3.zero, -f * 2.5f, across * 3f, -across * 3f, -f * 5f })
                {
                    if (!OnLevel(front + shift, 4f)) continue;
                    PlacedBuilding h = kit.TryBuilding(houseRole, variant, front + shift, f, side);
                    if (h == null) continue;
                    lastVariant2 = lastVariant;
                    lastVariant = variant;
                    dwellings.Add(h);
                    reach.Add(how);
                    return h;
                }

                return null;
            }

            /// <summary>Any building by role (smithy, stables, huts, barns): placed and connected like a house.</summary>
            private PlacedBuilding Other(string role, int variant, Vector3 front, Vector3 facing, int side, Reach how)
            {
                if (!OnLevel(front, 4f)) return null;
                PlacedBuilding b = kit.TryBuilding(role, variant, front, facing, side);
                if (b == null) return null;
                dwellings.Add(b);
                reach.Add(how);
                return b;
            }

            /// <summary>Setback from the verge: tight in the core, looser toward the edges, now and then a house well back behind its garden.</summary>
            private float Setback(float u)
            {
                float t = Mathf.Clamp01(Mathf.Abs(u) / (radius * 0.8f));
                if (rng.NextDouble() < 0.18 + 0.2 * t) return R(10f, 22f);
                return R(0.4f, 3f) + t * R(0f, 10f);
            }

            /// <summary>
            /// Houses along the road on side <paramref name="s"/> between <paramref name="u0"/> and <paramref name="u1"/>: staggered setbacks,
            /// gaps, small turns, now and then a gable to the road; at most <paramref name="max"/> and only where <paramref name="depthLimit"/> allows.
            /// </summary>
            private int RoadRow(int s, float u0, float u1, int max, float depthLimit = 99f)
            {
                int placed = 0, failures = 0;
                float cursor = u0 + R(0f, 5f);
                while (cursor < u1 && placed < max && failures < 30)
                {
                    if (rng.NextDouble() < 0.15)
                    {
                        cursor += R(8f, 15f);   // a yard, a field gate
                        continue;
                    }

                    float width = 9f;
                    float centre = cursor + width * 0.5f;
                    float set = Mathf.Min(Setback(centre), Mathf.Max(0.4f, depthLimit - 12f));
                    Vector3 away = Away(centre, s);
                    Vector3 facing = -away;
                    bool gable = rng.NextDouble() < 0.2;
                    if (gable) facing = Turn(-away, rng.NextDouble() < 0.5 ? 90f : -90f);
                    float extra = gable ? 2.5f : 0f;
                    PlacedBuilding h = House(At(centre, s, set + extra) + (gable ? facing * 4f : Vector3.zero), facing, s, Reach.Road);
                    if (h == null)
                    {
                        cursor += 3f;
                        failures++;
                        continue;
                    }

                    placed++;
                    cursor += h.Shape.Width + R(3f, 9f);
                }

                return placed;
            }

            /// <summary>Houses along a lane segment on the side <paramref name="outward"/> points to, facing the lane.</summary>
            private int LaneRow(Vector3 a, Vector3 b, Vector3 outward, int side, int max, float setMin, float setMax)
            {
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 6f) return 0;
                d /= len;
                int placed = 0;
                float cursor = R(2f, 6f);
                while (cursor < len - 4f && placed < max)
                {
                    float at = cursor + 4.5f;
                    if (rng.NextDouble() < 0.12)
                    {
                        cursor += R(6f, 12f);
                        continue;
                    }

                    Vector3 facing = rng.NextDouble() < 0.15 ? Turn(-outward, rng.NextDouble() < 0.5 ? 90f : -90f) : -outward;
                    PlacedBuilding h = House(a + d * at + outward * (2.4f + R(setMin, setMax)), facing, side, Reach.Lane, 9f);
                    if (h == null)
                    {
                        cursor += 3f;
                        continue;
                    }

                    placed++;
                    cursor += h.Shape.Width + R(3f, 8f);
                }

                return placed;
            }

            /// <summary>The smithy with its anvil, grindstone, woodpile and goods, at a front point facing <paramref name="facing"/>.</summary>
            private bool Smithy(Vector3 front, Vector3 facing, int side, Reach how)
            {
                if (!village || !keys.Has(PropRole.Smithy)) return false;
                Vector3 across = new Vector3(facing.z, 0f, -facing.x).normalized;
                PlacedBuilding smithy = null;
                foreach (float shift in new[] { 0f, 8f, -8f, 16f, -16f, 24f })
                {
                    smithy = Other(PropRole.Smithy, 0, front + across * shift, facing, side, how);
                    if (smithy != null) break;
                }

                if (smithy == null) return false;
                Vector3 work = smithy.At(smithy.Shape.Work);
                kit.Anchor(AnchorKind.Work, work, ScatterPlanner.Yaw(smithy.Facing), smithy.Side, smithy.PropIndex);
                kit.ForceVariant(PropRole.SmithyTool, rng.Next(2), work + smithy.Across * 1.4f + smithy.Facing * 0.6f, ScatterPlanner.Yaw(smithy.Across));
                kit.ForceVariant(PropRole.SmithyTool, 2, work + smithy.Facing * 2.6f - smithy.Across * 1.2f, ScatterPlanner.Yaw(smithy.Facing));
                kit.Yard(work + smithy.Facing * 2.5f, 5f);
                kit.TryProp(PropRole.WoodPile, smithy.At(new Vector3(smithy.Shape.ExtentX0 - 1.1f, 0f, 0f)), ScatterPlanner.Yaw(smithy.Facing), 1.0f);
                kit.TryProp(PropRole.YardGoods, keys.PickVariant(PropRole.YardGoods, rng), smithy.At(new Vector3(smithy.Shape.ExtentX1 + 1.0f, 0f, 1f)), R(0f, 360f), 1f, 0.8f);
                return true;
            }

            /// <summary>Stables with a haystack and a cart.</summary>
            private bool Stables(Vector3 front, Vector3 facing, int side, Reach how)
            {
                if (!keys.Has(PropRole.Stables)) return false;
                PlacedBuilding stables = Other(PropRole.Stables, 0, front, facing, side, how);
                if (stables == null) return false;
                kit.Anchor(AnchorKind.Work, stables.At(stables.Shape.Work), ScatterPlanner.Yaw(stables.Facing), side, stables.PropIndex);
                kit.TryProp(PropRole.Haystack, stables.At(new Vector3(stables.Shape.ExtentX1 + 2.6f, 0f, stables.Shape.ExtentZ0 + 3f)), R(0f, 360f), 2.2f);
                kit.TryProp(PropRole.FarmCart, keys.PickVariant(PropRole.FarmCart, rng), stables.At(new Vector3(stables.Shape.ExtentCentre.x, 0f, stables.Shape.ExtentZ1 + 3.5f)), R(0f, 360f), 1f, 2.2f);
                return true;
            }

            /// <summary>A farmstead: the house facing the hub and a barn (open shelter, stables or hut) turned to close an L with it.</summary>
            private void Farmstead(Vector3 hub, Vector3 dir, float dist, int side)
            {
                PlacedBuilding h = null;
                foreach (float k in new[] { 0f, -3f, 4f })
                {
                    Vector3 d2 = Turn(dir, k * 4f);
                    h = House(hub + d2 * (dist + k), -d2, side, Reach.Lane, 12f);
                    if (h != null)
                    {
                        dir = d2;
                        dist += k;
                        break;
                    }
                }

                if (h == null) return;
                float turn = (rng.NextDouble() < 0.5 ? 1f : -1f) * R(38f, 52f);
                Vector3 bdir = Turn(dir, turn);
                double pick = rng.NextDouble();
                string role = pick < 0.5 && keys.Has(PropRole.Shelter) ? PropRole.Shelter : pick < 0.72 && keys.Has(PropRole.Stables) ? PropRole.Stables : PropRole.Hut;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 front = hub + bdir * (dist + 1f + 2f * k);
                    PlacedBuilding barn = role == PropRole.Stables
                        ? (Stables(front, -bdir, side, Reach.Lane) ? dwellings[dwellings.Count - 1] : null)
                        : Other(role, keys.PickVariant(role, rng), front, Turn(-bdir, R(-10f, 10f)), side, Reach.Lane);
                    if (barn == null) continue;
                    if (role == PropRole.Shelter) kit.TryProp(PropRole.Haystack, barn.At(new Vector3(barn.Shape.ExtentCentre.x, 0f, barn.Shape.ExtentZ0 - 2.5f)), R(0f, 360f), 2.2f);
                    break;
                }
            }

            /// <summary>The well (with the people's spot), a fire and stones (hamlets), stalls (villages), benches round a hub.</summary>
            private void Gathering(Vector3 c, float r, int side, Vector3 toRoad, int benches, int stalls, bool fire)
            {
                Vector3 across = new Vector3(toRoad.z, 0f, -toRoad.x);
                Vector3 well = c + across * (r * 0.25f) - toRoad * (r * 0.15f);
                if (kit.TryProp(PropRole.Well, well, R(0f, 360f), 1.8f))
                {
                    kit.Yard(well, 3.5f);
                    kit.Anchor(AnchorKind.Well, well + toRoad * 1.8f, ScatterPlanner.Yaw(-toRoad), side);
                }

                if (fire)
                {
                    Vector3 at = c - across * (r * 0.35f) - toRoad * 2f;
                    if (kit.TryProp(PropRole.Fireplace, at, 0f, 1.5f))
                    {
                        kit.Force(PropRole.FireTripod, at, R(0f, 360f));
                        if (keys.Count(PropRole.Stump) >= 5) kit.TryProp(PropRole.Stump, 3 + rng.Next(2), at + across * 2.2f, ScatterPlanner.Yaw(-toRoad), 1f, 0.6f);
                    }
                }

                for (int k = 0; k < stalls; k++)
                {
                    Vector3 at = c + toRoad * (r - 1.5f) + across * ((k == 0 ? -1f : 1f) * 5f);
                    if (kit.TryProp(PropRole.MarketStall, keys.PickVariant(PropRole.MarketStall, rng), at, ScatterPlanner.Yaw(toRoad), 1f, 2.4f))
                    {
                        kit.Yard(at - toRoad * 1.5f, 3f);
                        kit.Anchor(AnchorKind.Stall, at - toRoad * 2.4f, ScatterPlanner.Yaw(-toRoad), side);
                    }
                }

                Hub(c, r, side);
                kit.Anchor(AnchorKind.Green, c, ScatterPlanner.Yaw(toRoad), side);
                // A worn track from the middle to the nearest lane (the green is open ground; the track joins it to the place's paths).
                Vector3 join = kit.GateFor(c, kit.AlongOf(c), side);
                float joinD = Flat(join - c).magnitude;
                foreach (var lane in lanes)
                {
                    Vector3 q = Nearest(lane.a, lane.b, c);
                    float dq = Flat(q - c).magnitude;
                    if (dq < joinD) { join = q; joinD = dq; }
                }

                kit.Path(c, join);
                for (int k = 0; k < benches; k++)
                {
                    float a = (k - (benches - 1) * 0.5f) * 1.1f + (float)Math.PI + R(-0.15f, 0.15f);
                    Vector3 dir = toRoad * Mathf.Cos(a) + across * Mathf.Sin(a);
                    Vector3 at = c + dir * (r - 1.0f);
                    if (kit.TryProp(PropRole.Bench, rng.Next(Math.Max(1, keys.Count(PropRole.Bench))), at, SettlementKit.YawAlongX(new Vector3(dir.z, 0f, -dir.x)), 1f, 1.0f))
                    {
                        kit.Anchor(AnchorKind.Bench, at, ScatterPlanner.Yaw(-dir), side, -1, BenchSeat);
                    }
                }

                Result.Green = c;
                Result.GreenRadius = r;
            }

            /// <summary>The arch house astride a lane to the fields: <paramref name="from"/> on a lane, the arch along <paramref name="outward"/> from it.</summary>
            private void ArchOnLane(Vector3 from, Vector3 outward, int side)
            {
                if (!village || !keys.Has(PropRole.Gatehouse) || rng.NextDouble() > 0.85) return;
                for (int k = 0; k < 9; k++)
                {
                    Vector3 dir = Turn(outward, (k % 3 - 1) * 20f);
                    Vector3 front = from + dir * (3f + 3f * (k / 3));
                    if (!OnLevel(front + dir * 9f, 0f)) continue;
                    PlacedBuilding g = kit.TryBuilding(PropRole.Gatehouse, 0, front, -dir, side);
                    if (g == null) continue;
                    dwellings.Add(g);
                    reach.Add(Reach.Lane);
                    float laneZ = (BuildingTable.ArchOpeningZ0 + BuildingTable.ArchOpeningZ1) * 0.5f;
                    Vector3 laneStart = g.At(new Vector3(g.Shape.ExtentX1 + 0.5f, 0f, laneZ));
                    Vector3 laneEnd = g.At(new Vector3(g.Shape.ExtentX0 - 10f, 0f, laneZ));
                    // Joined to the nearest lane, so the arch's own lane is part of the place's paths.
                    Vector3 on = from;
                    float onD = float.MaxValue;
                    foreach (var lane in lanes)
                    {
                        Vector3 q = Nearest(lane.a, lane.b, laneStart);
                        float dq = Flat(q - laneStart).magnitude;
                        if (dq < onD) { on = q; onD = dq; }
                    }

                    kit.Path(on, laneStart);
                    kit.Path(laneStart, laneEnd);
                    lanes.Add((on, laneStart));
                    lanes.Add((laneStart, laneEnd));
                    return;
                }
            }

            // -------------------------------------------------------------- the plans

            public void Street()
            {
                float reachU = radius * 0.78f;
                float gu = R(-15f, 15f);
                Lane(At(gu, greenSide, 4f), At(gu, greenSide, 0.4f));
                Gathering(At(gu, greenSide, 4f + 10f), 10f, greenSide, -Away(gu, greenSide), 3, 1 + rng.Next(2), rng.NextDouble() < 0.4);
                Smithy(At(R(-25f, 25f), -greenSide, 3f), -Away(0f, -greenSide), -greenSide, Reach.Road);
                int wanted = 11 + rng.Next(4);
                RoadRow(-1, -reachU, reachU, wanted / 2 + 1);
                RoadRow(1, -reachU, reachU, wanted - Houses);
                // A house or two well back, up a short lane between the plots.
                for (int k = 0; k < 2; k++)
                {
                    int s = k == 0 ? greenSide : -greenSide;
                    float u = R(-reachU * 0.7f, reachU * 0.7f);
                    Vector3 end = At(u, s, R(26f, 34f));
                    Vector3 a = At(u, s, 0.4f);
                    if (kit.Occupancy.Overlaps(end.x, end.z, 5f)) continue;
                    Lane(a, end);
                    House(end + Away(u, s) * 2.6f + Along(u) * R(-2f, 2f), Turn(-Away(u, s), R(-30f, 30f)), s, Reach.Lane);
                }

                Ends(reachU);
            }

            public void GreenVillage()
            {
                bool astride = rng.NextDouble() < 0.3;
                float gu = R(-10f, 10f);
                float gr = astride ? R(15f, 19f) : R(13f, 17f);
                Vector3 toRoad = -Away(gu, greenSide);
                Vector3 green = astride ? At(gu, greenSide, gr * 0.35f) : At(gu, greenSide, gr + 2f);
                Vector3 along = Along(gu);
                // An irregular green: lobes kept clear around the main disc.
                if (!astride) Lane(green + toRoad * (gr - 1f), At(gu, greenSide, 0.4f));
                if (!astride) Gathering(green, gr, greenSide, toRoad, 3, 1 + rng.Next(2), rng.NextDouble() < 0.3);
                else Gathering(At(gu, greenSide, 4f + 9f), 9f, greenSide, toRoad, 3, 1 + rng.Next(2), false);
                Vector3 lobe = green + along * R(-0.7f, 0.7f) * gr - toRoad * gr * 0.4f;
                if (!astride) kit.Occupancy.Add(lobe, gr * 0.55f);
                for (int k = 0; k < 2; k++) PlantTree(green + Turn(-toRoad, R(-60f, 60f)) * gr * R(0.45f, 0.8f), PropRole.MeadowTree, 2f);

                // A lane to the fields from the green's back, the arch house on it.
                Vector3 back = green - toRoad * (gr + 1.5f);
                Vector3 far = back - toRoad * R(14f, 20f) + along * R(-6f, 6f);
                if (!astride && OnLevel(far, 6f))
                {
                    Lane(back, far);
                    ArchOnLane(far, (far - back).normalized, greenSide);
                }

                // Houses round the green's edge facing it (on both sides of the road when it is astride).
                foreach (int s in astride ? new[] { greenSide, -greenSide } : new[] { greenSide })
                {
                    Vector3 c = astride ? At(gu, s, gr * 0.35f) : green;
                    Vector3 out0 = Away(gu, s);
                    float rim = gr + R(2f, 4f);
                    for (float a = -105f; a <= 105f; a += R(28f, 40f))
                    {
                        Vector3 dir = Turn(out0, a);
                        float r = rim + R(0f, 5f) + (Mathf.Abs(a) > 70f ? 3f : 0f);
                        House(c + dir * r, -dir, s, astride && Vector3.Dot(dir, out0) < 0.3f ? Reach.Road : Reach.Lane, 10f);
                    }
                }

                // The smithy across the road near the green, roadside houses loosening toward the ends.
                int other = -greenSide;
                Smithy(At(gu + R(-14f, 14f), other, R(2f, 6f)), -Away(gu, other), other, Reach.Road);
                int wanted = 12 + rng.Next(4);
                RoadRow(other, -radius * 0.75f, radius * 0.75f, Math.Max(2, (wanted - Houses) / 2 + 1));
                RoadRow(greenSide, -radius * 0.75f, radius * 0.75f, Math.Max(1, wanted - Houses));
                Ends(radius * 0.75f);
            }

            public void BackLane()
            {
                int s = greenSide;
                float l = R(40f, 52f);
                float depth = R(30f, 38f);
                float u0 = -l + R(-4f, 4f), u1 = l + R(-4f, 4f);
                Vector3 a = At(u0, s, 0.4f);
                Vector3 b = At(u0 + R(8f, 14f), s, depth * 0.75f);
                Vector3 m = At(R(-6f, 6f), s, depth + R(-3f, 4f));
                Vector3 c = At(u1 - R(8f, 14f), s, depth * 0.8f);
                Vector3 d = At(u1, s, 0.4f);
                // The smithy at the first junction, facing it.
                Vector3 j = At(u0 - 11f, s, 3f);
                Smithy(j, Turn(-Away(u0, s), -35f), s, Reach.Road);
                Lane(a, b, m, c, d);
                kit.Yard(m, 6f);
                // A well and a bench where the lane widens.
                // (Its 'road' side is the lane: the benches sit on the far side, away from the lane.)
                Gathering(m - Away(0f, s) * 6.5f, 5.5f, s, Away(0f, s), 2, 0, false);
                ArchOnLane(m + Away(0f, s) * 0.5f, Away(0f, s), s);

                // Roadside plots between the road and the lane, the lane's own houses behind, houses across the road.
                RoadRow(s, u0 + 6f, u1 - 6f, village ? 5 : 3, depth - 4f);
                LaneRow(b, m, Turn(Away(0f, s), 0f), s, 3, 0.5f, 6f);
                LaneRow(m, c, Away(0f, s), s, 3, 0.5f, 6f);
                Vector3 ab = Turn((b - a).normalized, 90f);
                LaneRow(a, b, Vector3.Dot(ab, Along(0f)) < 0f ? ab : -ab, s, 1, 1f, 4f);
                int wanted = 12 + rng.Next(4);
                RoadRow(-s, -radius * 0.75f, radius * 0.75f, Math.Max(2, wanted - Houses));
                Ends(radius * 0.78f);
            }

            public void ChurchVillage()
            {
                int s = greenSide;
                PlacedBuilding church = null;
                if (keys.Has(PropRole.LandmarkChapel))
                {
                    // The highest ground of a few spots behind the road (the place's level disc fades out at its edge).
                    var spots = new List<(float h, Vector3 front, float u)>();
                    foreach (float u in new[] { -24f, -12f, 0f, 12f, 24f })
                    {
                        foreach (float v in new[] { 40f, 48f, 56f })
                        {
                            Vector3 front = At(u + R(-3f, 3f), s, v);
                            spots.Add((kit.Ground(front - Away(u, s) * 8f), front, u));
                        }
                    }

                    spots.Sort((x, y) => y.h.CompareTo(x.h));
                    foreach (var spot in spots)
                    {
                        if (!OnLevel(spot.front - Away(spot.u, s) * 11f, 1f)) continue;
                        church = kit.TryShape(PropRole.LandmarkChapel, rng.Next(2), BuildingTable.Church, spot.front, Turn(-Away(spot.u, s), R(-12f, 12f)), s);
                        if (church != null) break;
                    }
                }

                Vector3 gateAt;
                float cu;
                if (church != null)
                {
                    dwellings.Add(church);
                    reach.Add(Reach.Lane);
                    gateAt = Churchyard(church);
                    cu = kit.AlongOf(church.Door);
                }
                else
                {
                    cu = R(-10f, 10f);
                    gateAt = At(cu, s, 34f);
                }

                // The church lane: from the road up to the yard gate, with a bend; a small green with the well at its foot.
                Vector3 foot = At(cu + R(-12f, 12f), s, 0.4f);
                Vector3 bend = Vector3.Lerp(foot, gateAt, 0.5f) + Along(cu) * R(-6f, 6f);
                Lane(foot, bend, gateAt);
                Vector3 toRoad = -Away(cu, s);
                Vector3 greenAt = Vector3.Lerp(foot, bend, 0.5f) + Along(cu) * (rng.NextDouble() < 0.5 ? 9f : -9f);
                Gathering(greenAt, 6.5f, s, toRoad, 2, village ? 1 : 0, false);
                Vector3 laneDir = (gateAt - bend).normalized;
                Vector3 laneAcross = new Vector3(laneDir.z, 0f, -laneDir.x);
                // Houses gathered below the church: along its lane on both sides and round the little green.
                LaneRow(bend, gateAt, laneAcross, s, 3, 0.5f, 5f);
                LaneRow(bend, gateAt, -laneAcross, s, 3, 0.5f, 5f);
                Vector3 d0 = (bend - foot).normalized;
                Vector3 a0 = new Vector3(d0.z, 0f, -d0.x);
                LaneRow(foot, bend, Vector3.Dot(a0, greenAt - foot) > 0f ? -a0 : a0, s, 2, 1f, 5f);
                Vector3 fromLane = Flat(greenAt - Nearest(foot, bend, greenAt)).normalized;
                for (float a = -70f; a <= 70f; a += R(45f, 60f)) House(greenAt + Turn(fromLane, a) * (6.5f + R(3f, 6f)), -Turn(fromLane, a), s, Reach.Lane, 10f);
                Smithy(At(cu + (rng.NextDouble() < 0.5 ? 26f : -26f), -s, R(2f, 6f)), -Away(cu, -s), -s, Reach.Road);
                int wanted = 12 + rng.Next(4);
                RoadRow(s, -radius * 0.75f, radius * 0.75f, Math.Min(3, Math.Max(1, (wanted - Houses) / 2)), 22f);
                RoadRow(-s, -radius * 0.75f, radius * 0.75f, Math.Max(2, wanted - Houses));
                Ends(radius * 0.78f);
            }

            /// <summary>The churchyard: rows of gravestones beside the church, a dry-stone wall round both with a gate on the lane; returns the gate.</summary>
            private Vector3 Churchyard(PlacedBuilding church)
            {
                Vector3 f = church.Facing, x = church.Across;
                float sideSign = rng.NextDouble() < 0.5 ? 1f : -1f;
                // Yard rectangle in the church's frame: across from the far flank to 8 m past the near one, along 3 m behind to 4 m in front.
                float xa = -3.22f - 2.2f, xb = 3.22f + 2.2f;
                if (sideSign > 0f) xb += 7.5f; else xa -= 7.5f;
                float za = -5.46f - 3f, zb = 5.46f + 4f;
                Vector3 P(float lx, float lz) => church.At(new Vector3(lx, 0f, lz));
                int rows = 3, cols = 4 + rng.Next(2);
                int stones = keys.Count(PropRole.Gravestone);
                float gx = sideSign > 0f ? 3.22f + 4.2f : -3.22f - 4.2f;
                for (int r = 0; r < rows; r++)
                {
                    for (int k = 0; k < cols; k++)
                    {
                        if (stones == 0 || rng.NextDouble() < 0.15) continue;
                        Vector3 at = P(gx + sideSign * r * 2.2f, -4f + k * 2.1f + R(-0.2f, 0.2f));
                        int variant = r * cols + k == 5 ? stones - 1 : rng.Next(Math.Max(1, stones - 1));
                        kit.TryProp(PropRole.Gravestone, variant, at, ScatterPlanner.Yaw(f) + R(-5f, 5f), 1f, 0.6f);
                    }
                }

                if (rng.NextDouble() < 0.7) PlantTree(P(-sideSign * 5.6f, -6f), PropRole.MeadowTree, 1.5f);
                if (keys.Has(PropRole.StoneWall))
                {
                    float length = Mathf.Max(0.5f, keys.wallLength);
                    void Run(Vector3 p0, Vector3 p1, bool gate)
                    {
                        Vector3 d = p1 - p0;
                        int n = Math.Max(1, Mathf.RoundToInt(d.magnitude / length));
                        float yaw = ScatterPlanner.Yaw(d);
                        for (int i = 0; i < n; i++)
                        {
                            if (gate && (i == n / 2 || i == (n - 1) / 2)) continue;
                            kit.Force(PropRole.StoneWall, p0 + d * ((i + 0.5f) / n), yaw);
                        }

                        kit.Force(PropRole.StoneWallPillar, p0, yaw);
                    }

                    Run(P(xa, zb), P(xb, zb), true);
                    Run(P(xb, zb), P(xb, za), false);
                    Run(P(xb, za), P(xa, za), false);
                    Run(P(xa, za), P(xa, zb), false);
                }

                // The whole yard is kept clear of later footprints.
                for (float lx = xa; lx <= xb; lx += 3f)
                {
                    for (float lz = za; lz <= zb; lz += 3f) kit.Occupancy.Add(P(lx, lz), 2.2f);
                }

                Vector3 gateAt = P((xa + xb) * 0.5f, zb + 1.6f);
                kit.Anchor(AnchorKind.Chapel, church.Door, ScatterPlanner.Yaw(f), church.Side, church.PropIndex);
                return gateAt;
            }

            public void FarmYard()
            {
                int s = greenSide;
                float gu = R(-12f, 12f);
                Vector3 yard = At(gu, s, R(18f, 26f));
                Vector3 toRoad = -Away(gu, s);
                Lane(At(gu + R(-8f, 8f), s, 0.4f), yard + toRoad * 6f);
                kit.Yard(yard, 7f);
                Gathering(yard, 6f, s, toRoad, 2, 0, rng.NextDouble() < 0.6);
                int n = 3 + rng.Next(2);
                float span = 230f;
                for (int k = 0; k < n; k++)
                {
                    float a = -span * 0.5f + span * (k + 0.5f) / n + R(-10f, 10f);
                    Farmstead(yard, Turn(-toRoad, a), R(11f, 15f), s);
                }

                RoadRow(-s, -radius * 0.6f, radius * 0.6f, 2 + rng.Next(2));
                Ends(radius * 0.7f);
            }

            public void LaneFork()
            {
                int s = greenSide;
                float gu = R(-10f, 10f);
                Vector3 away = Away(gu, s), along = Along(gu);
                Vector3 start = At(gu, s, 0.4f);
                Vector3 fork = At(gu + R(-4f, 4f), s, R(10f, 14f));
                float spread = R(14f, 20f);
                Vector3 endA = fork + away * R(9f, 13f) + along * spread;
                Vector3 endB = fork + away * R(9f, 13f) - along * spread;
                Lane(start, fork);
                Lane(fork, endA);
                Lane(fork, endB);
                kit.Yard(fork, 4.5f);
                Gathering(fork - along * 0f + away * 0.5f - along * 7f, 4.5f, s, -away, 1, 0, false);
                foreach (Vector3 end in new[] { endA, endB })
                {
                    Vector3 dir = (end - fork).normalized;
                    Farmstead(end, Turn(dir, R(-30f, 30f)), R(7f, 10f), s);
                }

                Farmstead(fork, Turn(along, rng.NextDouble() < 0.5 ? 0f : 180f), R(11f, 14f), s);
                RoadRow(-s, -radius * 0.6f, radius * 0.6f, 2 + rng.Next(2));
                Ends(radius * 0.7f);
            }

            public void HamletGreen()
            {
                int s = greenSide;
                float gu = R(-8f, 8f);
                float gr = R(9f, 11f);
                Vector3 toRoad = -Away(gu, s);
                Vector3 green = At(gu, s, gr + 2f);
                Lane(green + toRoad * (gr - 1f), At(gu, s, 0.4f));
                Gathering(green, gr, s, toRoad, 2, 0, true);
                int n = 3;
                for (int k = 0; k < n; k++)
                {
                    float a = -80f + 160f * k / (n - 1) + R(-12f, 12f);
                    Farmstead(green, Turn(-toRoad, a), gr + R(3f, 6f), s);
                }

                RoadRow(-s, -radius * 0.6f, radius * 0.6f, 2 + rng.Next(2));
                Ends(radius * 0.7f);
            }

            /// <summary>Stables near one end, huts at the ends, set back and turned.</summary>
            private void Ends(float reachU)
            {
                int endSide = rng.NextDouble() < 0.5 ? -1 : 1;
                if (village && rng.NextDouble() < 0.75)
                {
                    foreach (float u in new[] { reachU * 0.8f * endSide, -reachU * 0.8f * endSide, reachU * 0.6f * endSide })
                    {
                        int side = rng.NextDouble() < 0.5 ? -1 : 1;
                        if (Stables(At(u, side, R(4f, 12f)), Turn(-Away(u, side), R(-20f, 20f)), side, Reach.Road)) break;
                    }
                }

                int huts = keys.Has(PropRole.Hut) ? (village ? 2 : rng.Next(2)) : 0;
                for (int k = 0; k < huts; k++)
                {
                    int end = k % 2 == 0 ? -endSide : endSide;
                    foreach (float extra in new[] { 0f, 8f, -8f })
                    {
                        int side = rng.NextDouble() < 0.5 ? -1 : 1;
                        float u = end * (reachU + extra);
                        if (Other(PropRole.Hut, keys.PickVariant(PropRole.Hut, rng), At(u, side, R(6f, 16f)), Turn(-Away(u, side), R(-35f, 35f)), side, Reach.Road) != null) break;
                    }
                }
            }

            private void PlantTree(Vector3 at, string role, float keep)
            {
                if (!keys.Has(role)) role = PropRole.FruitTree;
                if (kit.Occupancy.Overlaps(at.x, at.z, keep) || !kit.OffRoad(at, 4f)) return;
                if (kit.AddTree(role, at)) kit.Occupancy.Add(at, 1.2f);
            }

            // -------------------------------------------------------------- finishing: plots, paths, walls, lamps, trees, orchard, mills

            public void Finish()
            {
                // A site that refused part of its plan (a stream, a steep edge, an inn nearby) still gets its people's homes: houses on the road.
                int least = village ? 12 : 6;
                for (int k = 0; k < 4 && dwellings.Count(b => PropRole.IsDwelling(b.Role)) < least; k++)
                {
                    int side = k % 2 == 0 ? -greenSide : greenSide;
                    RoadRow(side, -radius * 0.8f, radius * 0.8f, least - dwellings.Count(b => PropRole.IsDwelling(b.Role)));
                }

                Result.Houses = dwellings.Count;
                for (int i = 0; i < dwellings.Count; i++) Plot(dwellings[i], reach[i]);
                RoadWalls();
                Lamps();

                // A few trees among the houses.
                int trees = village ? 3 + rng.Next(3) : 2 + rng.Next(2);
                for (int k = 0, tries = 0; k < trees && tries < 30; tries++)
                {
                    if (dwellings.Count == 0) break;
                    PlacedBuilding h = dwellings[rng.Next(dwellings.Count)];
                    Vector3 at = h.At(new Vector3(rng.NextDouble() < 0.5 ? h.Shape.ExtentX0 - 4f : h.Shape.ExtentX1 + 4f, 0f, h.Shape.ExtentZ0 - R(2f, 6f)));
                    int before = kit.Trees.Count;
                    PlantTree(at, rng.NextDouble() < 0.5 ? PropRole.MeadowTree : PropRole.FruitTree, 2.5f);
                    if (kit.Trees.Count > before) k++;
                }

                Orchard();
                Mills();
            }

            private void Plot(PlacedBuilding house, Reach how)
            {
                BuildingShape s = house.Shape;
                Vector3 f = house.Facing, across = house.Across;
                float facing = ScatterPlanner.Yaw(f);
                if (PropRole.IsDwelling(house.Role)) kit.Anchor(AnchorKind.Door, house.Door, facing, house.Side, house.PropIndex);
                if (house.Role == PropRole.Gatehouse) return;
                Connect(house, how);
                if (house.Role != houseRole) return;   // the smithy, stables, barns, huts and the church have their own yards

                float s0 = rng.NextDouble() < 0.5 ? 1f : -1f;
                float sideX = s0 > 0 ? s.ExtentX1 : s.ExtentX0;
                // Behind the house: a privy or a hut, else a fence closing the back yard.
                double back = rng.NextDouble();
                bool outbuilding = false;
                if (back < 0.3) outbuilding = kit.TryBuilding(PropRole.Outhouse, 0, house.At(new Vector3(s.ExtentCentre.x + s0 * 2f, 0f, s.ExtentZ0 - 2.4f)), f, house.Side) != null;
                else if (back < 0.42 && keys.Has(PropRole.Hut))
                {
                    PlacedBuilding hut = kit.TryBuilding(PropRole.Hut, 1, house.At(new Vector3(s.ExtentCentre.x - s0 * 1.5f, 0f, s.ExtentZ0 - 4.5f)), -across * s0, house.Side);
                    outbuilding = hut != null;
                    if (hut != null)
                    {
                        // A cottar's hut at the back of the plot: a dwelling of its own, with its door and path.
                        kit.Anchor(AnchorKind.Door, hut.Door, ScatterPlanner.Yaw(hut.Facing), hut.Side, hut.PropIndex);
                        Connect(hut, Reach.Lane);
                    }
                }

                if (!outbuilding && rng.NextDouble() < 0.6) Fence(house.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ0 - R(2.6f, 5f))), across, 3 + rng.Next(2));
                if (rng.NextDouble() < 0.45) kit.TryProp(PropRole.WoodPile, house.At(new Vector3(sideX + s0 * 1.9f, 0f, s.BodyCentre.z)), ScatterPlanner.Yaw(f), 0.8f);
                double extra = rng.NextDouble();
                if (extra < 0.4) Garden(house.At(new Vector3(-sideX - s0 * 3.2f, 0f, s.BodyCentre.z)), across, f, 3, 2);
                else if (extra < 0.55)
                {
                    kit.TryProp(PropRole.FarmCart, keys.PickVariant(PropRole.FarmCart, rng), house.At(new Vector3(-sideX - s0 * 3f, 0f, s.BodyCentre.z)), facing + 90f + R(-20f, 20f), 1f, 2.2f);
                }

                if (rng.NextDouble() < 0.25) kit.TryProp(PropRole.YardGoods, keys.PickVariant(PropRole.YardGoods, rng), house.At(new Vector3(sideX + s0 * 1.8f, 0f, s.BodyZ1 - 1.2f)), R(0f, 360f), 1f, 0.7f);
                if (rng.NextDouble() < 0.25)
                {
                    Vector3 bench = house.At(new Vector3(s.Door.x + (s.Door.x < s.BodyCentre.x ? 2.2f : -2.2f), 0f, s.FrontReach + 0.9f));
                    if (kit.TryProp(PropRole.Bench, rng.Next(Math.Max(1, keys.Count(PropRole.Bench))), bench, SettlementKit.YawAlongX(across), 1f, 0.9f))
                    {
                        kit.Anchor(AnchorKind.Bench, bench, facing, house.Side, house.PropIndex, BenchSeat);
                    }
                }

                if (rng.NextDouble() < 0.2)
                {
                    string family = rng.Next(2) == 0 ? PropRole.Birdhouse : PropRole.WindChime;
                    kit.TryProp(family, house.At(new Vector3(-sideX - s0 * 1.4f, 0f, s.BodyZ1)), R(0f, 360f), 0.4f);
                }
            }

            /// <summary>
            /// A worn path from the door to the nearest reachable lane, green rim or (for a house on the road) its garden gate at the verge,
            /// choosing the nearest whose straight line keeps out of every other building; the gate (or the lane point) is the people's gate.
            /// </summary>
            private void Connect(PlacedBuilding house, Reach how)
            {
                Vector3 door = house.Door;
                Vector3 best = Vector3.zero;
                float bestD = float.MaxValue;
                bool bestIsHub = false;
                void Consider(Vector3 p, bool hub)
                {
                    float d = Flat(p - door).magnitude;
                    if (d >= bestD || !Clear(door, p, house)) return;
                    best = p;
                    bestD = d;
                    bestIsHub = hub;
                }

                foreach (var lane in lanes) Consider(Nearest(lane.a, lane.b, door), false);
                foreach (var hub in hubs)
                {
                    Vector3 to = Flat(door - hub.c);
                    if (to.sqrMagnitude > 1e-4f) Consider(hub.c + to.normalized * (hub.r - 0.5f), true);
                }

                Vector3 gate = kit.GateFor(door, kit.AlongOf(door), house.Side);
                if (how == Reach.Road || bestD == float.MaxValue || Flat(gate - door).magnitude + 4f < bestD) Consider(gate, false);
                if (bestD == float.MaxValue) best = gate;   // nothing reachable in a straight line: the gate (the path may brush a neighbour)
                kit.Path(door, best);
                if (bestIsHub)
                {
                    foreach (var hub in hubs)
                    {
                        if (Mathf.Abs(Flat(best - hub.c).magnitude - (hub.r - 0.5f)) < 0.05f) { kit.Path(best, hub.c); break; }
                    }
                }
                else
                {
                    kit.Anchor(AnchorKind.Gate, best, ScatterPlanner.Yaw(Flat(door - best)), house.Side, house.PropIndex);
                    if (kit.RoadDistance(best) < half + SettlementKit.Verge + 1f) kit.Occupancy.Add(best, 1.3f);   // the gate stays open
                }
            }

            private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

            private static Vector3 Nearest(Vector3 a, Vector3 b, Vector3 p)
            {
                Vector3 d = Flat(b - a);
                float len = d.sqrMagnitude;
                float t = len > 1e-6f ? Mathf.Clamp01(Vector3.Dot(Flat(p - a), d) / len) : 0f;
                return new Vector3(a.x + d.x * t, 0f, a.z + d.z * t);
            }

            /// <summary>True when a straight path from <paramref name="a"/> to <paramref name="b"/> keeps out of every building but its own.</summary>
            private bool Clear(Vector3 a, Vector3 b, PlacedBuilding own)
            {
                float len = Flat(b - a).magnitude;
                int n = Math.Max(1, (int)Math.Ceiling(len / 1f));
                foreach (PlacedBuilding other in kit.Buildings)
                {
                    if (other == own) continue;
                    BuildingShape s = other.Shape;
                    for (int k = 1; k <= n; k++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, (float)k / n) - other.Pivot;
                        Vector3 local = BuildingTable.ToWorld(Vector3.zero, -other.Yaw, new Vector3(p.x, 0f, p.z));
                        float lx = local.x, lz = local.z;
                        if (lx > s.ExtentX0 - 0.3f && lx < s.ExtentX1 + 0.3f && lz > s.ExtentZ0 - 0.3f && lz < s.ExtentZ1 + 0.3f) return false;
                    }
                }

                return true;
            }

            private void RoadWalls()
            {
                if (!keys.Has(PropRole.StoneWall)) return;
                float length = Mathf.Max(0.5f, keys.wallLength);
                float reachU = radius * 0.78f;
                foreach (int side in new[] { -1, 1 })
                {
                    if (rng.NextDouble() < 0.45) continue;
                    float along = -reachU + 4f + R(0f, reachU);
                    float stop = along + length * 6f;
                    bool inRun = false;
                    Vector3 lastEnd = Vector3.zero;
                    float yaw = 0f;
                    while (along < Mathf.Min(reachU - 2f, stop))
                    {
                        kit.Frame(along + length * 0.5f, out Vector3 on, out Vector3 t);
                        yaw = ScatterPlanner.Yaw(t);
                        Vector3 pos = on + SettlementKit.Right(t) * side * (half + SettlementKit.Verge + 0.8f);
                        bool ok = kit.TryProp(PropRole.StoneWall, 0, pos, yaw, 1f, length * 0.42f, 0f, 0.4f);
                        if (ok && !inRun) kit.Force(PropRole.StoneWallPillar, pos - t * (length * 0.5f), yaw);
                        if (!ok && inRun)
                        {
                            kit.Force(PropRole.StoneWallPillar, lastEnd, yaw);
                            inRun = false;
                            break;
                        }

                        if (ok) lastEnd = pos + t * (length * 0.5f);
                        inRun = ok;
                        along += length;
                    }

                    if (inRun) kit.Force(PropRole.StoneWallPillar, lastEnd, yaw);
                }
            }

            private void Lamps()
            {
                int lamps = village ? 3 : 1;
                // By the road near the middle, then at the lanes' feet.
                for (int k = 0, attempt = 0; k < lamps && attempt < lamps * 4; attempt++)
                {
                    float along = (attempt % 2 == 0 ? 1f : -1f) * (6f + 12f * (attempt / 2));
                    int side = attempt % 3 == 2 ? -greenSide : greenSide;
                    if (kit.TryProp(PropRole.Lamp, keys.PickVariant(PropRole.Lamp, rng), kit.Beside(along, side, half + SettlementKit.Verge + 0.8f), ScatterPlanner.Yaw(Away(along, side)), 1f, 0.5f)) k++;
                }

                if (village && hubs.Count > 0)
                {
                    var hub = hubs[0];
                    Vector3 at = hub.c + Turn(Away(0f, hub.side), R(-50f, 50f)) * (hub.r - 0.4f);
                    kit.TryProp(PropRole.Lamp, keys.PickVariant(PropRole.Lamp, rng), at, R(0f, 360f), 1f, 0.5f);
                }
            }

            private void Orchard()
            {
                int side = -greenSide;
                float baseAlong = R(-0.5f, 0.5f) * radius * 0.8f;
                int rows = 2 + rng.Next(2), cols = 3 + rng.Next(3);
                float offset = 30f + R(0f, 12f);
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        Vector3 pos = kit.Beside(baseAlong + (c - (cols - 1) * 0.5f) * 6.5f, side, half + offset + r * 6.5f);
                        pos += new Vector3(R(-0.6f, 0.6f), 0f, R(-0.6f, 0.6f));
                        if (kit.Occupancy.Overlaps(pos.x, pos.z, 2.5f) || !kit.Roads.IsClear(pos.x, pos.z, half + 8f)) continue;
                        kit.AddTree(PropRole.FruitTree, pos);
                    }
                }
            }

            private void Mills()
            {
                kit.Frame(0f, out Vector3 centre, out Vector3 tangent);
                Vector3 normal = SettlementKit.Right(tangent);
                int mills = village ? 1 + (rng.NextDouble() < 0.4 ? 1 : 0) : rng.NextDouble() < 0.5 ? 1 : 0;
                for (int m = 0; m < mills; m++)
                {
                    for (int attempt = 0; attempt < 16; attempt++)
                    {
                        float a = R(-0.5f, 0.5f) * Mathf.PI * 0.9f;
                        float side = (m + attempt) % 2 == 0 ? greenSide : -greenSide;
                        Vector3 dir = normal * side * Mathf.Cos(a) + tangent * Mathf.Sin(a);
                        Vector3 pos = centre + dir * (radius + 10f + R(0f, 18f));
                        if (kit.TryProp(PropRole.Windmill, 0, pos, R(0f, 360f), 1f, 7f, 0f, 4f)) break;
                    }
                }
            }

            private void Garden(Vector3 centre, Vector3 across, Vector3 forward, int cols, int rows)
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        Vector3 at = centre + across * ((c - (cols - 1) * 0.5f) * 1.3f) + forward * ((r - (rows - 1) * 0.5f) * 1.4f);
                        at += new Vector3(R(-0.125f, 0.125f), 0f, R(-0.125f, 0.125f));
                        kit.TryProp(PropRole.GardenPlant, keys.PickVariant(PropRole.GardenPlant, rng), at, R(0f, 360f), 0.9f + 0.2f * (float)rng.NextDouble(), 0.55f);
                    }
                }
            }

            private void Fence(Vector3 centre, Vector3 runs, int pieces)
            {
                if (!keys.Has(PropRole.Fence)) return;
                float length = Mathf.Max(0.5f, keys.fenceLength);
                Vector3 start = centre - runs * (length * pieces * 0.5f);
                int variant = keys.PickVariant(PropRole.Fence, rng);
                int placed = 0;
                float yaw = ScatterPlanner.Yaw(runs);
                for (int i = 0; i < pieces; i++)
                {
                    if (kit.TryProp(PropRole.Fence, variant, start + runs * ((i + 0.5f) * length), yaw, 1f, 0.7f)) placed++;
                }

                if (placed > 0 && keys.Has(PropRole.FencePost))
                {
                    kit.Force(PropRole.FencePost, start, yaw);
                    kit.Force(PropRole.FencePost, start + runs * (length * pieces), yaw);
                }
            }
        }

        /// <summary>Seat height of a bench (the inn composer's value).</summary>
        private const float BenchSeat = 0.45f;
    }
}
