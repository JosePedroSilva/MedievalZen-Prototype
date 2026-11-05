using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.People;
using MedievalZen.World;

namespace MedievalZen.Sound
{
    /// <summary>One positioned ambience source found near the listener, in absolute coordinates.</summary>
    public struct AmbienceSpot
    {
        /// <summary>What it is.</summary>
        public AmbienceRole Role;

        /// <summary>Stable key: the same stream, lake or place keeps its source (and its clip) while the listener moves.</summary>
        public long Key;

        /// <summary>Absolute position of the sound (height in metres).</summary>
        public double X, Z;
        public float Y;

        /// <summary>Horizontal distance from the listener, metres (to the water's edge for water).</summary>
        public float Distance;

        /// <summary>Distance at which the source plays at its full target level, and the search radius it fades out toward.</summary>
        public float Reference, Reach;
    }

    /// <summary>
    /// Finds the positioned ambience around an absolute point from the endless generator, pure and thread-safe like the generator (2026-10-06).
    /// The director runs it on a worker about once a second, so a cold region never stalls a frame: streams (the nearest point of the two
    /// nearest built courses within <see cref="StreamReach"/>), lakes (the nearest shore within <see cref="LakeReach"/>), settlements and
    /// landmarks by kind within <see cref="PlaceReach"/>.
    /// </summary>
    public static class AmbienceScan
    {
        /// <summary>Search radii, metres.</summary>
        public const float StreamReach = 70f, LakeReach = 90f, PlaceReach = 160f;

        /// <summary>At most this many streams at once.</summary>
        public const int MaxStreams = 2;

        /// <summary>Key bases (the low bits hold the generator's id).</summary>
        private const long StreamKey = 1L << 40, LakeKey = 2L << 40, PlaceKey = 3L << 40, LandmarkKey = 4L << 40;

        /// <summary>Appends the spots around (<paramref name="x"/>, <paramref name="z"/>) at road distance <paramref name="s"/>, nearest first.</summary>
        public static void Around(EndlessGenerator g, double x, double z, double s, List<AmbienceSpot> results)
        {
            if (g == null || results == null) return;
            int first = results.Count;
            Streams(g, x, z, results);
            var waypoints = new List<Waypoint>();
            g.Waypoints(s - 900.0, s + 900.0, waypoints);
            foreach (Waypoint w in waypoints)
            {
                if (w == null) continue;
                double dx = w.X - x, dz = w.Z - z;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (w.Kind == WaypointKind.Lake)
                {
                    if (d - w.Radius > LakeReach + 40.0) continue;
                    Lake(g, w, x, z, results);
                }
                else if (w.IsSettlement)
                {
                    if (d > PlaceReach + w.Radius) continue;
                    Settlement(g, w, x, z, Math.Max(0.0, d - 0.5 * w.Radius), results);
                }
            }

            var sites = new List<LandmarkSite>();
            g.Landmarks.Around(s - 900.0, s + 900.0, sites);
            foreach (LandmarkSite l in sites)
            {
                double dx = l.X - x, dz = l.Z - z;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d > PlaceReach) continue;
                AmbienceRole role = l.Type == LandmarkType.Mill ? AmbienceRole.Windmill : l.Type == LandmarkType.Stones ? AmbienceRole.Stones : AmbienceRole.Church;
                float lift = l.Type == LandmarkType.Mill ? 7f : l.Type == LandmarkType.Stones ? 1.5f : Math.Min(10f, 0.5f * l.Height);
                results.Add(new AmbienceSpot { Role = role, Key = LandmarkKey | (uint)l.Id, X = l.X, Z = l.Z, Y = l.Ground + lift, Distance = d, Reference = 18f, Reach = PlaceReach });
            }

            results.Sort(first, results.Count - first, ByDistance);
        }

        private static readonly IComparer<AmbienceSpot> ByDistance = Comparer<AmbienceSpot>.Create((a, b) => a.Distance.CompareTo(b.Distance));

        private static void Streams(EndlessGenerator g, double x, double z, List<AmbienceSpot> results)
        {
            if (g.Streams == null) return;
            var courses = new List<StreamCourse>();
            g.Streams.Overlapping(x - StreamReach, z - StreamReach, 2.0 * StreamReach, 5.0, courses);
            var found = new List<AmbienceSpot>();
            foreach (StreamCourse c in courses)
            {
                if (c == null || !c.Accepted || c.Count < 2) continue;
                int best = -1;
                double bestD = double.MaxValue;
                for (int i = 0; i < c.Count; i++)
                {
                    double dx = c.X[i] - x, dz = c.Z[i] - z;
                    double d = dx * dx + dz * dz;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }

                // Refine on the two segments round the nearest point so the sound slides along the water instead of stepping 3 m at a time.
                double px = c.X[best], pz = c.Z[best];
                float py = c.Surface[best], half = c.HalfWater[best];
                for (int k = Math.Max(0, best - 1); k < Math.Min(c.Count - 1, best + 1); k++)
                {
                    double ax = c.X[k], az = c.Z[k], bx = c.X[k + 1] - ax, bz = c.Z[k + 1] - az;
                    double len = bx * bx + bz * bz;
                    if (len < 1e-9) continue;
                    double t = Math.Max(0.0, Math.Min(1.0, ((x - ax) * bx + (z - az) * bz) / len));
                    double qx = ax + bx * t, qz = az + bz * t;
                    double d = (qx - x) * (qx - x) + (qz - z) * (qz - z);
                    if (d < bestD)
                    {
                        bestD = d;
                        px = qx;
                        pz = qz;
                        py = c.Surface[k] + (c.Surface[k + 1] - c.Surface[k]) * (float)t;
                        half = c.HalfWater[k] + (c.HalfWater[k + 1] - c.HalfWater[k]) * (float)t;
                    }
                }

                float edge = Math.Max(0f, (float)Math.Sqrt(bestD) - half);
                if (edge > StreamReach) continue;
                found.Add(new AmbienceSpot { Role = AmbienceRole.Stream, Key = StreamKey | (uint)c.Crossing.Id, X = px, Z = pz, Y = py + 0.2f, Distance = edge, Reference = 5f, Reach = StreamReach });
            }

            found.Sort(ByDistance);
            for (int k = 0; k < found.Count && k < MaxStreams; k++) results.Add(found[k]);
        }

        private static void Lake(EndlessGenerator g, Waypoint w, double x, double z, List<AmbienceSpot> results)
        {
            // The nearest water toward the lake's centre: march from the listener in 2 m steps.
            double dx = w.X - x, dz = w.Z - z;
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) d = 1e-6;
            double ux = dx / d, uz = dz / d;
            for (double t = 0.0; t <= Math.Min(d, LakeReach); t += 2.0)
            {
                double px = x + ux * t, pz = z + uz * t;
                float level = g.WaterHeight(px, pz);
                if (level < 0f || g.Height(px, pz) >= level) continue;
                results.Add(new AmbienceSpot { Role = AmbienceRole.Lake, Key = LakeKey | (uint)w.Id, X = px, Z = pz, Y = level + 0.3f, Distance = (float)t, Reference = 10f, Reach = LakeReach });
                return;
            }
        }

        private static void Settlement(EndlessGenerator g, Waypoint w, double x, double z, double d, List<AmbienceSpot> results)
        {
            AmbienceRole role;
            if (w.Kind == WaypointKind.Hamlet)
            {
                role = AmbienceRole.FarmAnimals;
            }
            else
            {
                Place place = PeopleSource.FindPlace(g, w.Id, w.S);
                Dressing dressing = place != null ? g.Dressings.Settlement(place) : null;
                if (dressing == null) return;
                if (w.IsVillageLike)   // a walled city has its market square: the market murmur
                {
                    bool market = false;
                    foreach (PropPlacement p in dressing.Props)
                    {
                        if (p.role == PropRole.MarketStall)
                        {
                            market = true;
                            break;
                        }
                    }

                    role = market ? AmbienceRole.Market : AmbienceRole.FarmAnimals;
                }
                else
                {
                    switch ((InnArchetype)dressing.InnStyle)
                    {
                        case InnArchetype.Farm: role = AmbienceRole.FarmAnimals; break;
                        case InnArchetype.Coaching:
                        case InnArchetype.Alehouse:
                        case InnArchetype.ForgeInn:
                        case InnArchetype.Waterside: role = AmbienceRole.InnYard; break;
                        default: return;   // a forest lodge, a refuge: quiet
                    }
                }
            }

            results.Add(new AmbienceSpot { Role = role, Key = PlaceKey | (uint)w.Id, X = w.X, Z = w.Z, Y = w.Height + 2f, Distance = (float)d, Reference = 15f, Reach = PlaceReach });
        }
    }
}
