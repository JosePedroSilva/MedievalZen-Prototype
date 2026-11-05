using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.People
{
    /// <summary>What a person does at a stop.</summary>
    public enum StopKind
    {
        /// <summary>Stands (and turns to look at the traveller who comes close).</summary>
        Stand = 0,

        /// <summary>Sits on a bench.</summary>
        Sit = 1,
    }

    /// <summary>One stop of a person's round: an absolute point on the ground, the way they face there, and how long they stay.</summary>
    public struct RouteStop
    {
        /// <summary>Absolute position.</summary>
        public double X, Z;

        /// <summary>Yaw faced while there, degrees.</summary>
        public float Facing;

        /// <summary>Standing or sitting.</summary>
        public StopKind Kind;

        /// <summary>Shortest and longest stay, seconds.</summary>
        public float MinSeconds, MaxSeconds;

        /// <summary>Seat height above the ground (sitting), metres.</summary>
        public float SeatHeight;
    }

    /// <summary>One person of a place: who they are, their round (walked out along the stops and back again) and their hours.</summary>
    public sealed class PersonPlan
    {
        /// <summary>Stable id (the place's id and the person's index).</summary>
        public long Id;

        /// <summary>Character prefab name.</summary>
        public string Character;

        /// <summary>Feminine clips.</summary>
        public bool Feminine;

        /// <summary>Role.</summary>
        public PersonRole Role;

        /// <summary>The round: stop 0 is home (the door, the forge, the stall); walked 0 → last → 0.</summary>
        public RouteStop[] Stops;

        /// <summary>Hours out of doors (from, to), with the person's jitter.</summary>
        public float FromHour, ToHour;

        /// <summary>Seed of the person's own random stream.</summary>
        public int Seed;
    }

    /// <summary>The people of one place.</summary>
    public sealed class PlaceCast
    {
        /// <summary>Id of the place (its waypoint id).</summary>
        public int PlaceId;

        /// <summary>Centre of the place, absolute.</summary>
        public double X, Z;

        /// <summary>Everyone.</summary>
        public readonly List<PersonPlan> People = new List<PersonPlan>();
    }

    /// <summary>A building's ground-floor walls as the planner keeps people out of them: pivot (absolute), yaw and the body rectangle.</summary>
    public struct BuildingBody
    {
        /// <summary>Pivot, absolute.</summary>
        public double X, Z;

        /// <summary>Yaw, degrees.</summary>
        public float Yaw;

        /// <summary>The measured preset.</summary>
        public BuildingShape Shape;

        /// <summary>Index of the building's placement in the dressing's props (matches <see cref="PlaceAnchor.Building"/>).</summary>
        public int PropIndex;

        /// <summary>True when (x, z) lies inside the walls (<see cref="BuildingShape.Walls"/>) grown by <paramref name="margin"/>.</summary>
        public bool Contains(double x, double z, float margin)
        {
            if (Shape == null) return false;
            float r = Yaw * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            float dx = (float)(x - X), dz = (float)(z - Z);
            // Inverse of BuildingTable.ToWorld: local = R(-yaw) * d.
            float lx = dx * cs - dz * sn, lz = dx * sn + dz * cs;
            (float x0, float x1, float z0, float z1) = Shape.Walls;
            return lx > x0 - margin && lx < x1 + margin && lz > z0 - margin && lz < z1 + margin;
        }
    }

    /// <summary>Everything the planner needs to know about a place (absolute coordinates; a pure description).</summary>
    public sealed class PlaceInput
    {
        /// <summary>World seed.</summary>
        public int Seed;

        /// <summary>The place's id.</summary>
        public int PlaceId;

        /// <summary>An inn (a roadside inn or the refuge), a hamlet or a village.</summary>
        public bool Inn, Village;

        /// <summary>Centre, absolute.</summary>
        public double CentreX, CentreZ;

        /// <summary>Origin of the anchors' local coordinates (the dressing window's), absolute.</summary>
        public double X0, Z0;

        /// <summary>The place's spots.</summary>
        public readonly List<PlaceAnchor> Anchors = new List<PlaceAnchor>();

        /// <summary>The walls people must walk round.</summary>
        public readonly List<BuildingBody> Bodies = new List<BuildingBody>();

        /// <summary>Distance from an absolute point to the road's centre line.</summary>
        public Func<double, double, double> RoadDistance;

        /// <summary>Half the road's width.</summary>
        public float HalfRoad = 2.5f;

        /// <summary>A chapel near the place: its centre (absolute), yaw and base radius; <see cref="HasChapel"/> false when none.</summary>
        public bool HasChapel;

        /// <summary>Chapel centre, absolute.</summary>
        public double ChapelX, ChapelZ;

        /// <summary>Chapel yaw (degrees) and the radius of its base.</summary>
        public float ChapelYaw, ChapelRadius;
    }

    /// <summary>
    /// Gives each inn and village its people (2026-10-05): at an inn the innkeeper between the porch, the door, the gate and a bench, a
    /// groom at the stables and now and then a guest on a bench; in a hamlet or village two to five peasants each walking a slow round from
    /// their door to their garden gate and on to the well, a bench or the green on their side of the road and back, the smith at the forge
    /// and the merchant at a stall; a monk or a nun at a chapel landmark within <see cref="ChapelReach"/>. Every leg of every round keeps
    /// <see cref="RoadMargin"/> beyond the road's edge (people never step on the road) and out of every building's walls; a stop that
    /// cannot be reached so is dropped. Pure; deterministic from the seed (stream <see cref="PeopleCatalog.CastStream"/>).
    /// </summary>
    public static class PeoplePlanner
    {
        /// <summary>Closest a person's path comes to the road's edge, metres (a body's half width and a margin).</summary>
        public const float RoadMargin = 0.6f;

        /// <summary>Kept clear of building walls by a person's path, metres.</summary>
        public const float WallMargin = 0.35f;

        /// <summary>Longest leg of a round, metres.</summary>
        public const float MaxLeg = 70f;

        /// <summary>A chapel landmark this near the place's centre gets a monk or a nun, metres.</summary>
        public const double ChapelReach = 750.0;

        /// <summary>The cast of a place.</summary>
        public static PlaceCast Plan(PlaceInput input)
        {
            var cast = new PlaceCast { PlaceId = input.PlaceId, X = input.CentreX, Z = input.CentreZ };
            var rng = new System.Random(Hash(input.Seed, PeopleCatalog.CastStream, input.PlaceId));
            var used = new HashSet<int>();
            if (input.Inn) PlanInn(input, cast, rng, used);
            else PlanVillage(input, cast, rng, used);
            if (input.HasChapel && cast.People.Count < PeopleCatalog.MaxPerPlace) PlanChapel(input, cast, rng);
            return cast;
        }

        private static void PlanInn(PlaceInput input, PlaceCast cast, System.Random rng, HashSet<int> used)
        {
            int door = First(input, AnchorKind.InnDoor);
            List<int> works = All(input, AnchorKind.Work);
            // A forge inn's smith keeps to the forge (across the road from the inn): his anchor is nobody else's.
            for (int i = works.Count - 1; i >= 0; i--)
            {
                BuildingShape forge = BuildingOf(input, input.Anchors[works[i]]);
                if (forge == null || forge.Name.IndexOf("Blacksmith", StringComparison.Ordinal) < 0) continue;
                Add(input, cast, rng, PersonRole.Smith, new List<RouteStop> { Stop(input, works[i], StopKind.Stand, 60f, 140f), Offset(input, works[i], 2.6f, StopKind.Stand, 15f, 35f) });
                works.RemoveAt(i);
            }

            List<int> benches = All(input, AnchorKind.Bench);
            int gate = First(input, AnchorKind.Gate);
            int well = First(input, AnchorKind.Well);

            // The innkeeper: the porch, the door, the gate, a bench under the balcony, and back.
            var keeper = new List<RouteStop>();
            if (works.Count > 0) keeper.Add(Stop(input, works[0], StopKind.Stand, 40f, 90f));
            if (door >= 0) keeper.Add(Stop(input, door, StopKind.Stand, 15f, 35f));
            if (gate >= 0) keeper.Add(Stop(input, gate, StopKind.Stand, 8f, 18f));
            if (benches.Count > 0 && rng.NextDouble() < 0.7) keeper.Add(Stop(input, benches[0], StopKind.Sit, 30f, 70f));
            Add(input, cast, rng, PersonRole.Innkeeper, keeper);

            // A groom at the stables (the second work spot), to the well and back.
            if (works.Count > 1 && rng.NextDouble() < 0.75)
            {
                var groom = new List<RouteStop> { Stop(input, works[1], StopKind.Stand, 50f, 110f) };
                if (well >= 0) groom.Add(Stop(input, well, StopKind.Stand, 15f, 30f));
                Add(input, cast, rng, PersonRole.Groom, groom);
            }

            // A guest or two on the benches, now and then at the gate watching the road.
            int guests = rng.NextDouble() < 0.75 ? 1 + (rng.NextDouble() < 0.35 ? 1 : 0) : 0;
            for (int g = 0; g < guests && benches.Count > 1; g++)
            {
                int bench = benches[1 + (g % (benches.Count - 1))];
                if (!used.Add(bench)) continue;
                var guest = new List<RouteStop> { Stop(input, bench, StopKind.Sit, 90f, 220f) };
                if (gate >= 0 && rng.NextDouble() < 0.6) guest.Add(Stop(input, gate, StopKind.Stand, 20f, 45f));
                Add(input, cast, rng, PersonRole.Guest, guest);
            }
        }

        private static void PlanVillage(PlaceInput input, PlaceCast cast, System.Random rng, HashSet<int> used)
        {
            // The smith and the merchant first: their places are fixed.
            foreach (int work in All(input, AnchorKind.Work))
            {
                PlaceAnchor a = input.Anchors[work];
                BuildingShape shape = BuildingOf(input, a);
                bool smithy = shape != null && shape.Name.IndexOf("Blacksmith", StringComparison.Ordinal) >= 0;
                bool stables = shape != null && shape.Name.IndexOf("Stables", StringComparison.Ordinal) >= 0;
                if (smithy)
                {
                    var smith = new List<RouteStop> { Stop(input, work, StopKind.Stand, 60f, 140f), Offset(input, work, 2.6f, StopKind.Stand, 15f, 35f) };
                    Add(input, cast, rng, PersonRole.Smith, smith);
                }
                else if (stables && rng.NextDouble() < 0.6)
                {
                    var groom = new List<RouteStop> { Stop(input, work, StopKind.Stand, 50f, 120f) };
                    int well = Nearest(input, a, AnchorKind.Well, a.Side);
                    if (well >= 0) groom.Add(Stop(input, well, StopKind.Stand, 15f, 30f));
                    Add(input, cast, rng, PersonRole.Groom, groom);
                }
            }

            List<int> stalls = All(input, AnchorKind.Stall);
            for (int k = 0; k < stalls.Count && k < 2; k++)
            {
                if (k > 0 && rng.NextDouble() < 0.5) continue;
                var merchant = new List<RouteStop> { Stop(input, stalls[k], StopKind.Stand, 90f, 200f), Offset(input, stalls[k], -3f, StopKind.Stand, 15f, 30f) };
                Add(input, cast, rng, PersonRole.Merchant, merchant);
            }

            // Peasants from their doors: the garden gate, then the well, a bench or the green on their side of the road.
            List<int> doors = All(input, AnchorKind.Door);
            int wanted = input.Village ? 3 + rng.Next(3) : 2 + rng.Next(2);
            for (int tries = 0; tries < doors.Count * 2 && wanted > 0 && cast.People.Count < PeopleCatalog.MaxPerPlace - (input.HasChapel ? 1 : 0); tries++)
            {
                int door = doors[rng.Next(doors.Count)];
                if (!used.Add(door)) continue;
                PlaceAnchor d = input.Anchors[door];
                var round = new List<RouteStop> { Stop(input, door, StopKind.Stand, 20f, 60f) };
                int gate = GateOf(input, d);
                if (gate >= 0) round.Add(Stop(input, gate, StopKind.Stand, 6f, 14f));
                PlaceAnchor from = gate >= 0 ? input.Anchors[gate] : d;
                int target = PickTarget(input, from, d.Side, rng, used);
                if (target >= 0)
                {
                    PlaceAnchor t = input.Anchors[target];
                    round.Add(Stop(input, target, t.Kind == AnchorKind.Bench ? StopKind.Sit : StopKind.Stand, t.Kind == AnchorKind.Bench ? 45f : 15f, t.Kind == AnchorKind.Bench ? 120f : 40f));
                    if (t.Kind == AnchorKind.Bench) used.Add(target);
                }

                if (Add(input, cast, rng, PersonRole.Peasant, round)) wanted--;
            }
        }

        private static void PlanChapel(PlaceInput input, PlaceCast cast, System.Random rng)
        {
            float r = input.ChapelYaw * Mathf.Deg2Rad;
            double fx = Math.Sin(r), fz = Math.Cos(r);
            double front = input.ChapelRadius + 2.5;
            var stops = new List<RouteStop>
            {
                new RouteStop { X = input.ChapelX + fx * front, Z = input.ChapelZ + fz * front, Facing = input.ChapelYaw, Kind = StopKind.Stand, MinSeconds = 60f, MaxSeconds = 140f },
                new RouteStop { X = input.ChapelX + fx * front + fz * 8.0, Z = input.ChapelZ + fz * front - fx * 8.0, Facing = input.ChapelYaw + 90f, Kind = StopKind.Stand, MinSeconds = 30f, MaxSeconds = 70f },
            };
            Add(input, cast, rng, PersonRole.Religious, stops);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Adds a person if their round keeps off the road and out of the walls (failing stops after the first are dropped).</summary>
        private static bool Add(PlaceInput input, PlaceCast cast, System.Random rng, PersonRole role, List<RouteStop> stops)
        {
            if (stops.Count == 0 || cast.People.Count >= PeopleCatalog.MaxPerPlace) return false;
            if (!PointClear(input, stops[0].X, stops[0].Z)) return false;
            var kept = new List<RouteStop> { stops[0] };
            for (int i = 1; i < stops.Count; i++)
            {
                RouteStop last = kept[kept.Count - 1];
                if (!LegClear(input, last.X, last.Z, stops[i].X, stops[i].Z)) break;
                kept.Add(stops[i]);
            }

            PersonCharacter character = PeopleCatalog.Pick(role, rng);
            (float from, float to) = PeopleCatalog.Hours(role);
            int index = cast.People.Count;
            cast.People.Add(new PersonPlan
            {
                Id = ((long)input.PlaceId << 8) | (long)index,
                Character = character.Name,
                Feminine = character.Feminine,
                Role = role,
                Stops = kept.ToArray(),
                FromHour = from + (float)(rng.NextDouble() - 0.5) * 1.2f,
                ToHour = to + (float)(rng.NextDouble() - 0.5) * 1.2f,
                Seed = Hash(input.Seed, PeopleCatalog.PersonStream, input.PlaceId * 16 + index),
            });
            return true;
        }

        /// <summary>True when an absolute point keeps <see cref="RoadMargin"/> beyond the road's edge and out of every building's walls.</summary>
        public static bool PointClear(PlaceInput input, double x, double z)
        {
            if (input.RoadDistance != null && input.RoadDistance(x, z) < input.HalfRoad + RoadMargin) return false;
            foreach (BuildingBody b in input.Bodies)
            {
                if (b.Contains(x, z, WallMargin)) return false;
            }

            return true;
        }

        /// <summary>True when a straight leg (sampled every 0.75 m) keeps off the road and out of the walls and is not too long.</summary>
        public static bool LegClear(PlaceInput input, double x0, double z0, double x1, double z1)
        {
            double dx = x1 - x0, dz = z1 - z0, len = Math.Sqrt(dx * dx + dz * dz);
            if (len > MaxLeg) return false;
            int n = Math.Max(1, (int)Math.Ceiling(len / 0.75));
            for (int k = 0; k <= n; k++)
            {
                double t = (double)k / n;
                if (!PointClear(input, x0 + dx * t, z0 + dz * t)) return false;
            }

            return true;
        }

        private static RouteStop Stop(PlaceInput input, int anchor, StopKind kind, float min, float max)
        {
            PlaceAnchor a = input.Anchors[anchor];
            return new RouteStop
            {
                X = input.X0 + a.Position.x, Z = input.Z0 + a.Position.z, Facing = a.Facing, Kind = kind, MinSeconds = min, MaxSeconds = max,
                SeatHeight = kind == StopKind.Sit ? Math.Max(0.3f, a.SeatHeight) : 0f,
            };
        }

        /// <summary>A stop <paramref name="distance"/> metres in front of an anchor (along its facing), standing.</summary>
        private static RouteStop Offset(PlaceInput input, int anchor, float distance, StopKind kind, float min, float max)
        {
            RouteStop s = Stop(input, anchor, kind, min, max);
            float r = s.Facing * Mathf.Deg2Rad;
            s.X += Math.Sin(r) * distance;
            s.Z += Math.Cos(r) * distance;
            return s;
        }

        private static int First(PlaceInput input, AnchorKind kind)
        {
            for (int i = 0; i < input.Anchors.Count; i++)
            {
                if (input.Anchors[i].Kind == kind) return i;
            }

            return -1;
        }

        private static List<int> All(PlaceInput input, AnchorKind kind)
        {
            var list = new List<int>();
            for (int i = 0; i < input.Anchors.Count; i++)
            {
                if (input.Anchors[i].Kind == kind) list.Add(i);
            }

            return list;
        }

        private static int GateOf(PlaceInput input, PlaceAnchor door)
        {
            if (door.Building < 0) return -1;
            for (int i = 0; i < input.Anchors.Count; i++)
            {
                PlaceAnchor a = input.Anchors[i];
                if (a.Kind == AnchorKind.Gate && a.Building == door.Building) return i;
            }

            return -1;
        }

        private static int Nearest(PlaceInput input, PlaceAnchor from, AnchorKind kind, int side)
        {
            int best = -1;
            float bestD = MaxLeg;
            for (int i = 0; i < input.Anchors.Count; i++)
            {
                PlaceAnchor a = input.Anchors[i];
                if (a.Kind != kind || (side != 0 && a.Side != 0 && a.Side != side)) continue;
                float d = Vector3.Distance(new Vector3(a.Position.x, 0f, a.Position.z), new Vector3(from.Position.x, 0f, from.Position.z));
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>The well, a free bench, a stall or the green on the same side of the road within a leg's reach of <paramref name="from"/>.</summary>
        private static int PickTarget(PlaceInput input, PlaceAnchor from, int side, System.Random rng, HashSet<int> used)
        {
            var options = new List<int>();
            for (int i = 0; i < input.Anchors.Count; i++)
            {
                PlaceAnchor a = input.Anchors[i];
                if (a.Kind != AnchorKind.Well && a.Kind != AnchorKind.Bench && a.Kind != AnchorKind.Green && a.Kind != AnchorKind.Stall) continue;
                if (a.Side != 0 && side != 0 && a.Side != side) continue;
                if (a.Kind == AnchorKind.Bench && used.Contains(i)) continue;
                float dx = a.Position.x - from.Position.x, dz = a.Position.z - from.Position.z;
                if (dx * dx + dz * dz > MaxLeg * MaxLeg) continue;
                options.Add(i);
            }

            return options.Count == 0 ? -1 : options[rng.Next(options.Count)];
        }

        private static BuildingShape BuildingOf(PlaceInput input, PlaceAnchor a)
        {
            if (a.Building < 0) return null;
            foreach (BuildingBody b in input.Bodies)
            {
                if (b.PropIndex == a.Building) return b.Shape;
            }

            return null;
        }

        /// <summary>A seed for a random stream: <see cref="MedievalZen.Endless.Generation.EndlessNoise.Seed"/> of the world seed, the stream and an index.</summary>
        public static int Hash(int seed, int stream, long index) => MedievalZen.Endless.Generation.EndlessNoise.Seed(seed, stream, index);
    }
}
