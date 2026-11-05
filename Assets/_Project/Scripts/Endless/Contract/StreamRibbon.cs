namespace MedievalZen.Endless
{
    /// <summary>
    /// One vertex of a stream's water ribbon: a point of the stream's centre line with the flow direction there, the half
    /// width of the water surface and its height. Tile-local x and z (from the tile's minimum corner), absolute y.
    /// </summary>
    public struct StreamRibbonVertex
    {
        /// <summary>Tile-local X of the centre line.</summary>
        public float X;

        /// <summary>Tile-local Z of the centre line.</summary>
        public float Z;

        /// <summary>Unit flow direction (downstream) on the horizontal plane, X component.</summary>
        public float DirX;

        /// <summary>Unit flow direction (downstream) on the horizontal plane, Z component.</summary>
        public float DirZ;

        /// <summary>Half width of the water surface, metres (the ribbon's edges tuck under the banks).</summary>
        public float HalfWidth;

        /// <summary>Height of the water surface, metres (absolute).</summary>
        public float Surface;

        /// <summary>Distance along the stream from its source, metres (continuous across tiles; texture coordinate along the flow).</summary>
        public float Along;
    }

    /// <summary>
    /// The part of one stream's water surface inside a tile: a polyline of <see cref="StreamRibbonVertex"/> ordered downstream.
    /// Where a stream leaves the tile the polyline ends exactly on the tile edge with the same vertex the neighbouring tile
    /// starts from (same position, direction, width and height), so the ribbons of neighbouring tiles meet without a gap.
    /// </summary>
    public sealed class StreamRibbon
    {
        /// <summary>Stable id of the stream (the <see cref="Waypoint.Id"/> of its road crossing).</summary>
        public int StreamId;

        /// <summary>Vertices, downstream order (at least two).</summary>
        public StreamRibbonVertex[] Vertices;
    }
}
