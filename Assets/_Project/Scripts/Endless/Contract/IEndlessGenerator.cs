using System.Collections.Generic;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The endless world as a pure function of the seed: one road, the landscape around it and the places along it.
    /// Every member must be deterministic (the same inputs give the same outputs whatever the order of calls), free of
    /// native Unity calls, and safe to call from several threads at once. All coordinates are absolute (doubles); the
    /// runtime subtracts its floating origin before anything reaches the scene.
    /// </summary>
    public interface IEndlessGenerator
    {
        /// <summary>The inputs this world was created from.</summary>
        EndlessParams Params { get; }

        /// <summary>The road's centre line at a distance from the journey's start (defined for every real <paramref name="s"/>).</summary>
        RoadPoint Road(double s);

        /// <summary>
        /// Road distance of the point on the road nearest to an absolute position, searching around
        /// <paramref name="hintS"/> (the last known distance).
        /// </summary>
        double NearestS(double x, double z, double hintS);

        /// <summary>The region the road is in at a distance.</summary>
        RegionInfo Region(double s);

        /// <summary>Appends the waypoints with road distance in [<paramref name="sFrom"/>, <paramref name="sTo"/>], ordered by distance.</summary>
        void Waypoints(double sFrom, double sTo, List<Waypoint> results);

        /// <summary>Final ground height in metres at an absolute position (road bed and lake beds included).</summary>
        float Height(double x, double z);

        /// <summary>Lake level in metres at an absolute position, or -1 where there is no lake.</summary>
        float WaterHeight(double x, double z);

        /// <summary>Generates one tile. Typically called on a worker thread; may take tens of milliseconds.</summary>
        TileBlueprint GenerateTile(TileCoord coord, TileLod lod);
    }
}
