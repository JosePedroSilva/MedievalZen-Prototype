using System;
using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>One destination offered by a signpost: a settlement, the road it lies along and its road distance.</summary>
    public readonly struct SignpostChoice
    {
        /// <summary>Creates a choice.</summary>
        public SignpostChoice(Settlement settlement, float distance, RoadSegment arm, Vector3 direction)
        {
            Settlement = settlement;
            Distance = distance;
            Arm = arm;
            Direction = direction;
        }

        /// <summary>Where the road leads.</summary>
        public Settlement Settlement { get; }

        /// <summary>Road distance from the junction, in metres.</summary>
        public float Distance { get; }

        /// <summary>The road leaving the junction that the shortest route starts on.</summary>
        public RoadSegment Arm { get; }

        /// <summary>Horizontal unit direction in which that road leaves the junction.</summary>
        public Vector3 Direction { get; }
    }

    /// <summary>
    /// Works out what a junction signpost says: for every settlement reachable from the junction, the road it starts along
    /// and the road distance (via <see cref="RoadNetwork.TryFindRoute"/>), sorted nearest first. Pure C#.
    /// </summary>
    public static class SignpostChoices
    {
        private static readonly string[] CompassNames = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        /// <summary>All reachable settlements from <paramref name="junction"/> (except one sitting on the junction), nearest first.</summary>
        public static List<SignpostChoice> Build(RoadNetwork network, RoadNode junction)
        {
            var result = new List<SignpostChoice>();
            if (network == null || junction == null || junction.Segments.Count == 0) return result;

            RoadSegment first = junction.Segments[0];
            var start = new RoadLocation(first, first.A == junction ? 0f : first.Length);
            var settlements = network.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (settlement.Node == junction) continue;
                if (!network.TryFindRoute(start, settlement, out RoadRoute route)) continue;
                RoadSegment arm = FirstArm(route);
                if (arm == null) continue;
                result.Add(new SignpostChoice(settlement, route.TotalLength, arm, ArmDirection(arm, junction)));
            }

            result.Sort(CompareByDistance);
            return result;
        }

        /// <summary>Orders choices by road distance, then by name.</summary>
        public static int CompareByDistance(SignpostChoice a, SignpostChoice b)
        {
            int byDistance = a.Distance.CompareTo(b.Distance);
            return byDistance != 0 ? byDistance : string.CompareOrdinal(a.Settlement.Name, b.Settlement.Name);
        }

        /// <summary>The first leg of <paramref name="route"/> that actually covers road (the start leg may be empty).</summary>
        public static RoadSegment FirstArm(RoadRoute route)
        {
            var legs = route.Legs;
            if (legs.Count == 0) return null;
            (RoadSegment segment, bool forward) = legs[0];
            float covered = forward ? segment.Length - route.StartDistanceOnFirstLeg : route.StartDistanceOnFirstLeg;
            if (covered > 0.01f) return segment;
            return legs.Count > 1 ? legs[1].segment : null;
        }

        /// <summary>Horizontal direction in which <paramref name="arm"/> leaves <paramref name="junction"/> (sampled ~12 m out).</summary>
        public static Vector3 ArmDirection(RoadSegment arm, RoadNode junction)
        {
            float along = Mathf.Min(12f, arm.Length);
            Vector3 point = arm.A == junction ? arm.Evaluate(along) : arm.Evaluate(arm.Length - along);
            Vector3 d = point - junction.Position;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>Eight-point compass name for a horizontal direction (+Z = north, +X = east).</summary>
        public static string Compass(Vector3 direction)
        {
            float bearing = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (bearing < 0f) bearing += 360f;
            int index = (int)Math.Floor((bearing + 22.5f) / 45f) % 8;
            return CompassNames[index];
        }
    }
}
