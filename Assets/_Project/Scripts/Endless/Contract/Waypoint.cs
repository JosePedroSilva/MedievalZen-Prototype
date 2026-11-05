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

        /// <summary>A shallow ford where a brook crosses the road (the road dips into the water; stepping stones beside it).</summary>
        Ford = 8,

        /// <summary>A footbridge where a larger stream crosses the road.</summary>
        Bridge = 9,

        /// <summary>
        /// A landmark seen from the road: a watchtower, chapel, windmill, ring of standing stones or ruin off the road on a crest
        /// or across a valley (<see cref="Waypoint.X"/>/<see cref="Waypoint.Z"/> are the structure, <see cref="Waypoint.S"/> the
        /// road point it was planned from).
        /// </summary>
        Landmark = 10,

        /// <summary>
        /// A walled city the road passes through (gatehouses, a main street, a market square), in the slot of a village; where nothing
        /// better exists, treat it as a <see cref="Village"/>.
        /// </summary>
        City = 11,
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

        /// <summary>Ground height at the centre (metres); for a landmark, the floor (pivot) of the structure.</summary>
        public float Height;

        /// <summary>Footprint radius in metres (0 for point features).</summary>
        public float Radius;

        /// <summary>Heading of the road where it passes the place, in degrees about Y (0 = +Z).</summary>
        public float RoadHeadingDeg;

        /// <summary>
        /// Landmarks only (NaN elsewhere): the road distance with the best view of the structure by the landmark planner's sight
        /// lines (terrain, the woods' cover and the roadside tree lines; nearest open view wins). See <c>LandmarkPlan.BestView</c>.
        /// </summary>
        public double ViewS = double.NaN;

        /// <summary>Landmarks only (0 elsewhere): visible height of the structure above <see cref="Height"/> (its floor), metres.</summary>
        public float StructureHeight;

        /// <summary>True for places a traveller can stop at (inns, hamlets, villages).</summary>
        public bool IsSettlement => Kind == WaypointKind.Inn || Kind == WaypointKind.Hamlet || Kind == WaypointKind.Village || Kind == WaypointKind.City;

        /// <summary>True for villages and walled cities (a city is a village wherever nothing more specific exists).</summary>
        public bool IsVillageLike => Kind == WaypointKind.Village || Kind == WaypointKind.City;
    }
}
