namespace MedievalZen.Endless
{
    /// <summary>A point on the endless road's centre line, in absolute coordinates.</summary>
    public struct RoadPoint
    {
        /// <summary>Distance along the road from the journey's start, in metres (may be negative: the road also runs back).</summary>
        public double S;

        /// <summary>Absolute X in metres.</summary>
        public double X;

        /// <summary>Absolute Z in metres.</summary>
        public double Z;

        /// <summary>Height of the road surface in metres.</summary>
        public float Height;

        /// <summary>Direction of travel for increasing <see cref="S"/>, in degrees about Y (0 = +Z, 90 = +X).</summary>
        public float HeadingDeg;

        /// <summary>Slope of the road in the direction of increasing <see cref="S"/> (rise over run; positive = uphill).</summary>
        public float Grade;
    }
}
