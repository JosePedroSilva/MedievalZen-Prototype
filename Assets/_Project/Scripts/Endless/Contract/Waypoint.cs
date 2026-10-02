namespace MedievalZen.Endless
{
    /// <summary>What a <see cref="Waypoint"/> is.</summary>
    public enum WaypointKind
    {
        /// <summary>A roadside inn: the traveller can rest here.</summary>
        Inn = 0,

        /// <summary>A few houses on the road.</summary>
        Hamlet = 1,

        /// <summary>A village the road passes through.</summary>
        Village = 2,

        /// <summary>The top of a mountain pass.</summary>
        Pass = 3,

        /// <summary>A place with a view, marked by standing stones or an arch.</summary>
        Viewpoint = 4,

        /// <summary>A lake beside the road.</summary>
        Lake = 5,

        /// <summary>A roadside camp.</summary>
        Camp = 6,

        /// <summary>A distance marker beside the road.</summary>
        Milestone = 7,
    }

    /// <summary>A named or notable place along the endless road. Immutable once returned by the generator.</summary>
    public sealed class Waypoint
    {
        /// <summary>Stable id: the same place always has the same id for a given seed. Prop placements refer to it.</summary>
        public int Id;

        /// <summary>What the place is.</summary>
        public WaypointKind Kind;

        /// <summary>Generated name ("The Drowsy Fox", "Ashford"), or null for unnamed places such as milestones.</summary>
        public string Name;

        /// <summary>Road distance of the place (metres from the journey's start).</summary>
        public double S;

        /// <summary>Absolute X of the place's centre.</summary>
        public double X;

        /// <summary>Absolute Z of the place's centre.</summary>
        public double Z;

        /// <summary>Ground height at the centre (metres).</summary>
        public float Height;

        /// <summary>Footprint radius in metres (0 for point features).</summary>
        public float Radius;

        /// <summary>Heading of the road where it passes the place, in degrees about Y (0 = +Z).</summary>
        public float RoadHeadingDeg;

        /// <summary>True for places a traveller can stop at (inns, hamlets, villages).</summary>
        public bool IsSettlement => Kind == WaypointKind.Inn || Kind == WaypointKind.Hamlet || Kind == WaypointKind.Village;
    }
}
