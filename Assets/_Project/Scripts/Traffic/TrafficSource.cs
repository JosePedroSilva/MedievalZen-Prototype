using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;

namespace MedievalZen.Traffic
{
    /// <summary>
    /// The endless road as the traffic plan reads it (pure): settlements and bridges from the generator's region waypoints, which it reads
    /// only once they are cached (<see cref="EndlessGenerator.WaypointsCached"/>: no lazy region work on the caller's thread), and the
    /// region's traffic factor.
    /// </summary>
    public sealed class TrafficSource : ITrafficRoad
    {
        private readonly EndlessGenerator generator;
        private readonly List<Waypoint> scratch = new List<Waypoint>();

        /// <summary>A source over <paramref name="g"/>.</summary>
        public TrafficSource(EndlessGenerator g)
        {
            generator = g;
        }

        /// <summary>When true, waypoints are built on demand (offline tools); otherwise only cached regions are read.</summary>
        public bool Warm { get; set; }

        /// <inheritdoc />
        public bool Places(double sFrom, double sTo, List<TrafficPlace> results)
        {
            if (generator == null) return false;
            if (!Warm && !generator.WaypointsCached(sFrom, sTo)) return false;
            scratch.Clear();
            generator.Waypoints(sFrom, sTo, scratch);
            foreach (Waypoint w in scratch)
            {
                if (!w.IsSettlement) continue;
                results.Add(new TrafficPlace { Id = w.Id, S = w.S, Radius = w.Radius, Village = w.IsVillageLike, Inn = w.Kind == WaypointKind.Inn });
            }

            return true;
        }

        /// <summary>Adds the bridges with road distance in [<paramref name="sFrom"/>, <paramref name="sTo"/>]; false when not cached.</summary>
        public bool Bridges(double sFrom, double sTo, List<TrafficBridge> results)
        {
            if (generator == null) return false;
            if (!Warm && !generator.WaypointsCached(sFrom, sTo)) return false;
            scratch.Clear();
            generator.Waypoints(sFrom, sTo, scratch);
            foreach (Waypoint w in scratch)
            {
                if (w.Kind == WaypointKind.Bridge) results.Add(new TrafficBridge { S = w.S, RoadHeight = w.Height });
            }

            return true;
        }

        /// <inheritdoc />
        public float Density(double s) => generator == null ? 0f : RoadTrafficPlan.RegionFactor(generator.Region(s).Kind);
    }
}
