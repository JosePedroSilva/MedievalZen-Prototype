using System.Collections.Generic;
using MedievalZen.Endless;

namespace MedievalZen.Wildlife
{
    /// <summary>What the land is like at one absolute position (see <see cref="IWildlifeHabitat.Sample"/>).</summary>
    public struct HabitatSample
    {
        /// <summary>False when nothing is known there (no ground).</summary>
        public bool Valid;

        /// <summary>Ground height in metres.</summary>
        public float Height;

        /// <summary>Under a lake.</summary>
        public bool Water;

        /// <summary>Ground slope (rise over run).</summary>
        public float Slope;

        /// <summary>Horizontal distance to the road's centre line, metres.</summary>
        public float RoadDistance;

        /// <summary>Land cover weights 0..1 (forest before clearings, highland, farm, moisture) and nearness to a lake shore.</summary>
        public float Forest, Highland, Farm, Moist, Shore;

        /// <summary>The region the land belongs to.</summary>
        public RegionKind Region;
    }

    /// <summary>Settlement kinds as the planner sees them.</summary>
    public enum HabitatSettlementKind
    {
        Inn = 0,
        Hamlet = 1,
        Village = 2,
        Town = 3,
    }

    /// <summary>A settlement in absolute coordinates.</summary>
    public struct HabitatSettlement
    {
        public int Id;
        public HabitatSettlementKind Kind;
        public double X, Z;

        /// <summary>Dressed radius in metres (buildings and fences stay inside it).</summary>
        public float Radius;

        /// <summary>Heading of the road through it, degrees about Y (0 = +Z).</summary>
        public float RoadHeadingDeg;
    }

    /// <summary>An empty (fallow) field that can hold a herd, in absolute coordinates.</summary>
    public struct HabitatPasture
    {
        public long Id;
        public double X, Z;
        public float HalfX, HalfZ;

        /// <summary>Rotation about Y in degrees (local right = (cos, −sin), forward = (sin, cos)).</summary>
        public float Yaw;
    }

    /// <summary>
    /// What the wildlife planner may ask about the land, in absolute coordinates. Implemented by <see cref="EndlessHabitat"/>
    /// (the endless road's generator) and by <c>FiniteHabitat</c> (the finite world's terrain and roads). Answers must be
    /// pure functions of the position, so planning is deterministic and order-independent.
    /// </summary>
    public interface IWildlifeHabitat
    {
        /// <summary>World seed (planner seeds derive from it).</summary>
        int Seed { get; }

        /// <summary>True when the habitat may be asked from a worker thread.</summary>
        bool ThreadSafe { get; }

        /// <summary>Half width of the strip along the road kept clear of animals (road plus verge), metres.</summary>
        float Corridor { get; }

        /// <summary>The land at (x, z).</summary>
        HabitatSample Sample(double x, double z);

        /// <summary>True where (x, z) is under water (a lake or a stream): cheaper than <see cref="Sample"/>.</summary>
        bool IsWater(double x, double z);

        /// <summary>Appends the settlements whose disc overlaps the square [x0, x0 + size] × [z0, z0 + size].</summary>
        void Settlements(double x0, double z0, double size, List<HabitatSettlement> results);

        /// <summary>Appends the fallow fields whose centre lies in the square.</summary>
        void Pastures(double x0, double z0, double size, List<HabitatPasture> results);
    }
}
