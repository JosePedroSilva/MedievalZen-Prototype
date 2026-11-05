using System;
using MedievalZen.Endless;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// The endless road for events (pure): nearest road point and points along it from the generator, converted to scene
    /// coordinates with the floating origin the caller keeps up to date (<see cref="OriginX"/>, <see cref="OriginZ"/>).
    /// </summary>
    public sealed class GeneratorRoad : IWildlifeRoad
    {
        private readonly IEndlessGenerator generator;

        public GeneratorRoad(IEndlessGenerator generator)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        /// <summary>Absolute position of the scene origin.</summary>
        public double OriginX, OriginZ;

        /// <summary>Road distance near the traveller (speeds up and disambiguates the nearest-point search).</summary>
        public double HintS = double.NaN;

        /// <inheritdoc />
        public bool Nearest(float x, float z, out float roadX, out float roadZ, out float dirX, out float dirZ, out float distance)
        {
            double ax = x + OriginX, az = z + OriginZ;
            double s = generator.NearestS(ax, az, HintS);
            RoadPoint p = generator.Road(s);
            roadX = (float)(p.X - OriginX);
            roadZ = (float)(p.Z - OriginZ);
            double h = p.HeadingDeg * Math.PI / 180.0;
            dirX = (float)Math.Sin(h);
            dirZ = (float)Math.Cos(h);
            distance = (float)Math.Sqrt((p.X - ax) * (p.X - ax) + (p.Z - az) * (p.Z - az));
            return true;
        }

        /// <inheritdoc />
        public bool Along(float x, float z, float dirX, float dirZ, float ahead, out float roadX, out float roadZ)
        {
            double ax = x + OriginX, az = z + OriginZ;
            double s = generator.NearestS(ax, az, HintS);
            RoadPoint p = generator.Road(s);
            double h = p.HeadingDeg * Math.PI / 180.0;
            int sign = Math.Sin(h) * dirX + Math.Cos(h) * dirZ >= 0.0 ? 1 : -1;
            RoadPoint q = generator.Road(s + sign * ahead);
            roadX = (float)(q.X - OriginX);
            roadZ = (float)(q.Z - OriginZ);
            return true;
        }
    }

    /// <summary>The generator's ground for the offline simulation and tests (pure): heights and water; nothing blocks.</summary>
    public sealed class GeneratorGround : IWildlifeGround
    {
        private readonly IEndlessGenerator generator;

        public GeneratorGround(IEndlessGenerator generator)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        public double OriginX, OriginZ;

        public float Height(float x, float z) => generator.Height(x + OriginX, z + OriginZ);

        public bool IsWater(float x, float z) => generator.WaterHeight(x + OriginX, z + OriginZ) > 0f;

        public bool IsBlocked(float x, float z, float radius) => false;
    }
}
