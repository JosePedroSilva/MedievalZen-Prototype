using System;
using System.Collections.Generic;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// A stretch of the endless road turned into an ordinary <see cref="RoadNetwork"/> in scene coordinates, so the finite
    /// world's travel code (auto-travel, routing, arrival, the HUD's distances) works on it unchanged: one chain of nodes along
    /// the road (at the window ends, every few hundred metres, and at every inn, hamlet and village, which become
    /// <see cref="Settlement"/>s). Pure (no Unity objects): built on a worker thread, applied on the main thread.
    /// </summary>
    public sealed class RoadWindow
    {
        /// <summary>Spacing of polyline samples along the road, metres.</summary>
        public const double SampleSpacing = 2.0;

        /// <summary>Plain nodes are not placed closer than this to a settlement node or a window end, metres.</summary>
        public const double NodeClearance = 40.0;

        private readonly Dictionary<int, Settlement> byWaypoint = new Dictionary<int, Settlement>();
        private readonly Dictionary<Settlement, Waypoint> waypoints = new Dictionary<Settlement, Waypoint>();

        private RoadWindow(RoadNetwork network, double sFrom, double sTo, double originX, double originZ)
        {
            Network = network;
            SFrom = sFrom;
            STo = sTo;
            OriginX = originX;
            OriginZ = originZ;
        }

        /// <summary>The road stretch as a network (scene coordinates for the origin it was built with).</summary>
        public RoadNetwork Network { get; }

        /// <summary>Road distance of the window's first point.</summary>
        public double SFrom { get; }

        /// <summary>Road distance of the window's last point.</summary>
        public double STo { get; }

        /// <summary>Absolute X of the floating origin the scene coordinates were computed with.</summary>
        public double OriginX { get; }

        /// <summary>Absolute Z of the floating origin the scene coordinates were computed with.</summary>
        public double OriginZ { get; }

        /// <summary>The settlement standing for waypoint <paramref name="waypointId"/>, or null when it is not in this window.</summary>
        public Settlement SettlementFor(int waypointId) => byWaypoint.TryGetValue(waypointId, out Settlement s) ? s : null;

        /// <summary>The waypoint a settlement of this window stands for, or null.</summary>
        public Waypoint WaypointOf(Settlement settlement) => settlement != null && waypoints.TryGetValue(settlement, out Waypoint w) ? w : null;

        /// <summary>True when road distance <paramref name="s"/> lies inside the window.</summary>
        public bool Contains(double s) => s >= SFrom && s <= STo;

        /// <summary>The settlement type a waypoint becomes in the network (inns stay inns; hamlets and villages are villages).</summary>
        public static SettlementType TypeFor(WaypointKind kind) => kind == WaypointKind.Inn ? SettlementType.Inn : SettlementType.Village;

        /// <summary>Footprint radius of a waypoint as a settlement (its own radius, else the finite world's default for the type).</summary>
        public static float RadiusFor(Waypoint w) => w.Radius > 1f ? w.Radius : SettlementSite.RadiusFor(TypeFor(w.Kind));

        /// <summary>
        /// The window [<paramref name="sFrom"/>, <paramref name="sTo"/>] of <paramref name="generator"/>'s road, in scene
        /// coordinates for the origin (<paramref name="originX"/>, <paramref name="originZ"/>), with plain nodes about every
        /// <paramref name="nodeSpacing"/> metres. Settlement nodes sit on the road at the waypoint's road distance.
        /// </summary>
        public static RoadWindow Build(IEndlessGenerator generator, double sFrom, double sTo, double originX, double originZ, double nodeSpacing = 400.0)
        {
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            if (!(sTo > sFrom + 1.0)) throw new ArgumentException("The window must be longer than a metre.", nameof(sTo));
            nodeSpacing = Math.Max(50.0, nodeSpacing);

            var found = new List<Waypoint>();
            generator.Waypoints(sFrom, sTo, found);
            var places = new List<Waypoint>();
            foreach (Waypoint w in found)
            {
                if (w != null && w.IsSettlement && w.S > sFrom + 1.0 && w.S < sTo - 1.0) places.Add(w);
            }

            places.Sort((a, b) => a.S.CompareTo(b.S));

            // Node distances: the ends, every settlement, and plain nodes on a fixed absolute grid (so consecutive windows
            // share node positions) that keep clear of the others.
            var stops = new List<(double s, Waypoint place)> { (sFrom, null), (sTo, null) };
            foreach (Waypoint w in places)
            {
                if (stops.Exists(t => t.place != null && Math.Abs(t.s - w.S) < 1.0)) continue;
                stops.Add((w.S, w));
            }

            for (double s = Math.Ceiling(sFrom / nodeSpacing) * nodeSpacing; s < sTo; s += nodeSpacing)
            {
                bool clear = true;
                foreach ((double s, Waypoint place) t in stops)
                {
                    if (Math.Abs(t.s - s) < NodeClearance)
                    {
                        clear = false;
                        break;
                    }
                }

                if (clear) stops.Add((s, null));
            }

            stops.Sort((a, b) => a.s.CompareTo(b.s));

            var builder = new RoadNetworkBuilder { EndpointTolerance = 0.5f, MaxPointSpacing = (float)SampleSpacing };
            var nodes = new RoadNode[stops.Count];
            var settlements = new List<(Settlement settlement, Waypoint place)>();
            for (int i = 0; i < stops.Count; i++)
            {
                nodes[i] = builder.AddNode(Scene(generator.Road(stops[i].s), originX, originZ));
                Waypoint place = stops[i].place;
                if (place == null) continue;
                Settlement settlement = builder.AddSettlement(place.Name ?? "Inn", TypeFor(place.Kind), nodes[i], RadiusFor(place));
                settlements.Add((settlement, place));
            }

            var points = new List<Vector3>(256);
            for (int i = 0; i + 1 < stops.Count; i++)
            {
                double a = stops[i].s, b = stops[i + 1].s;
                points.Clear();
                points.Add(nodes[i].Position);
                int steps = Math.Max(1, (int)Math.Ceiling((b - a) / SampleSpacing));
                for (int k = 1; k < steps; k++) points.Add(Scene(generator.Road(a + (b - a) * k / steps), originX, originZ));
                points.Add(nodes[i + 1].Position);
                builder.AddSegment(nodes[i], nodes[i + 1], points);
            }

            var result = new RoadWindow(builder.Build(), sFrom, sTo, originX, originZ);
            foreach ((Settlement settlement, Waypoint place) in settlements)
            {
                result.byWaypoint[place.Id] = settlement;
                result.waypoints[settlement] = place;
            }

            return result;
        }

        /// <summary>
        /// The same window after a floating-origin shift: every scene position moved by <paramref name="delta"/> (the new origin
        /// is (<paramref name="originX"/>, <paramref name="originZ"/>)). Copies the network without asking the generator, so it is
        /// cheap enough to do in the frame of the shift; settlement ids and node order are kept.
        /// </summary>
        public RoadWindow Translate(Vector3 delta, double originX, double originZ)
        {
            var builder = new RoadNetworkBuilder { EndpointTolerance = 0.5f, MaxPointSpacing = 0f };
            IReadOnlyList<RoadNode> oldNodes = Network.Nodes;
            var nodes = new RoadNode[oldNodes.Count];
            for (int i = 0; i < oldNodes.Count; i++) nodes[i] = builder.AddNode(oldNodes[i].Position + delta);
            foreach (Settlement settlement in Network.Settlements)
            {
                builder.AddSettlement(settlement.Name, settlement.Type, nodes[settlement.Node.Id], settlement.Radius);
            }

            var points = new List<Vector3>(512);
            foreach (RoadSegment segment in Network.Segments)
            {
                points.Clear();
                IReadOnlyList<Vector3> old = segment.Points;
                for (int i = 0; i < old.Count; i++) points.Add(old[i] + delta);
                builder.AddSegment(nodes[segment.A.Id], nodes[segment.B.Id], points);
            }

            var result = new RoadWindow(builder.Build(), SFrom, STo, originX, originZ);
            foreach (KeyValuePair<int, Settlement> pair in byWaypoint)
            {
                Settlement moved = result.Network.Settlements[pair.Value.Id];
                result.byWaypoint[pair.Key] = moved;
                result.waypoints[moved] = waypoints[pair.Value];
            }

            return result;
        }

        /// <summary>Scene position of a road point for an origin.</summary>
        public static Vector3 Scene(RoadPoint p, double originX, double originZ) => new Vector3((float)(p.X - originX), p.Height, (float)(p.Z - originZ));
    }

    /// <summary>Finding the next and previous inn along the endless road. Pure (unit tested).</summary>
    public static class InnFinder
    {
        /// <summary>Inns closer than this (metres along the road) count as "the one you are at", not the next one.</summary>
        public const double DefaultSkip = 30.0;

        /// <summary>
        /// Among <paramref name="inns"/> (any order), the nearest one strictly beyond <paramref name="skip"/> metres from
        /// <paramref name="s"/> in <paramref name="direction"/> (+1 = increasing road distance, −1 = back); null when none.
        /// </summary>
        public static Waypoint Select(IReadOnlyList<Waypoint> inns, double s, int direction, double skip = DefaultSkip)
        {
            return Select(inns, s, direction, skip, false);
        }

        private static Waypoint Select(IReadOnlyList<Waypoint> places, double s, int direction, double skip, bool villages)
        {
            Waypoint best = null;
            double bestGap = double.PositiveInfinity;
            for (int i = 0; i < places.Count; i++)
            {
                Waypoint w = places[i];
                if (w == null) continue;
                bool wanted = villages ? w.Kind == WaypointKind.Hamlet || w.IsVillageLike : w.Kind == WaypointKind.Inn;
                if (!wanted) continue;
                double gap = (w.S - s) * (direction >= 0 ? 1.0 : -1.0);
                if (gap <= skip || gap >= bestGap) continue;
                bestGap = gap;
                best = w;
            }

            return best;
        }

        /// <summary>
        /// Among <paramref name="places"/> (any order), the nearest hamlet or village strictly beyond <paramref name="skip"/> metres
        /// from <paramref name="s"/> in <paramref name="direction"/>; null when none. Villages are rare (one per farmland region), and
        /// hopping from inn to inn passes them unseen, so the Journey menu offers them by name.
        /// </summary>
        public static Waypoint SelectVillage(IReadOnlyList<Waypoint> places, double s, int direction, double skip = DefaultSkip) =>
            Select(places, s, direction, skip, true);

        /// <summary>
        /// The next inn along <paramref name="generator"/>'s road from <paramref name="s"/> in <paramref name="direction"/>,
        /// searching up to <paramref name="searchLimit"/> metres in 1.5 km steps; null when there is none that close.
        /// </summary>
        public static Waypoint Find(IEndlessGenerator generator, double s, int direction, double skip = DefaultSkip, double searchLimit = 8000.0) =>
            Find(generator, s, direction, skip, searchLimit, false);

        /// <summary>
        /// The next hamlet or village along the road of <paramref name="generator"/> from <paramref name="s"/> in
        /// <paramref name="direction"/>, searching up to <paramref name="searchLimit"/> metres; null when there is none that close.
        /// </summary>
        public static Waypoint FindVillage(IEndlessGenerator generator, double s, int direction, double skip = DefaultSkip, double searchLimit = 12000.0) =>
            Find(generator, s, direction, skip, searchLimit, true);

        /// <summary>
        /// True when a search of <paramref name="searchLimit"/> metres each way from <paramref name="s"/> (<c>Find</c>,
        /// <see cref="FindVillage"/>) only reads cached waypoints: no lazy generation, a fraction of a millisecond. Always true for
        /// a generator without that cache. Main-thread callers check it and keep their last answer until the warmer has built the
        /// regions (<see cref="Generation.EndlessGenerator.WarmWaypoints"/> on a worker).
        /// </summary>
        public static bool Ready(IEndlessGenerator generator, double s, double searchLimit) =>
            !(generator is Generation.EndlessGenerator g) || g.WaypointsCached(s - searchLimit - SearchStep, s + searchLimit + SearchStep);

        /// <summary>The searches ask the generator for this much road at a time.</summary>
        public const double SearchStep = 1500.0;

        /// <summary>
        /// The endless world warms the region waypoints this far each way round the traveller on a worker (again after each
        /// 1 km): the 12 km village search plus one step stays inside it.
        /// </summary>
        public const double WarmReach = 16000.0;

        private static Waypoint Find(IEndlessGenerator generator, double s, int direction, double skip, double searchLimit, bool villages)
        {
            var buffer = new List<Waypoint>();
            const double step = SearchStep;
            for (double covered = 0.0; covered < searchLimit; covered += step)
            {
                buffer.Clear();
                double a = direction >= 0 ? s + covered : s - covered - step;
                double b = direction >= 0 ? s + covered + step : s - covered;
                generator.Waypoints(a, b, buffer);
                Waypoint place = Select(buffer, s, direction, skip, villages);
                if (place != null) return place;
            }

            return null;
        }

        /// <summary>+1 when <paramref name="facing"/> (a horizontal direction) points along increasing road distance at a road heading of <paramref name="roadHeadingDeg"/>, else −1.</summary>
        public static int DirectionFor(Vector3 facing, float roadHeadingDeg)
        {
            float rad = roadHeadingDeg * Mathf.Deg2Rad;
            float dot = facing.x * Mathf.Sin(rad) + facing.z * Mathf.Cos(rad);
            return dot >= 0f ? 1 : -1;
        }
    }
}
