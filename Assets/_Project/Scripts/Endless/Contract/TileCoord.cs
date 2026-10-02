using System;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Index of a square terrain tile on the endless world's absolute grid. Tile (X, Z) covers
    /// <c>[X * tileSize, (X + 1) * tileSize)</c> by <c>[Z * tileSize, (Z + 1) * tileSize)</c> in absolute metres.
    /// </summary>
    public readonly struct TileCoord : IEquatable<TileCoord>
    {
        /// <summary>Tile index along absolute X.</summary>
        public readonly int X;

        /// <summary>Tile index along absolute Z (the road's general direction of travel).</summary>
        public readonly int Z;

        /// <summary>Creates a tile index.</summary>
        public TileCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>The tile that contains an absolute position.</summary>
        public static TileCoord Containing(double x, double z, float tileSize)
        {
            return new TileCoord((int)Math.Floor(x / tileSize), (int)Math.Floor(z / tileSize));
        }

        /// <summary>Absolute X of the tile's minimum corner.</summary>
        public double OriginX(float tileSize) => (double)X * tileSize;

        /// <summary>Absolute Z of the tile's minimum corner.</summary>
        public double OriginZ(float tileSize) => (double)Z * tileSize;

        /// <inheritdoc />
        public bool Equals(TileCoord other) => X == other.X && Z == other.Z;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is TileCoord other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => unchecked((X * 397) ^ Z);

        /// <summary>Equality by index.</summary>
        public static bool operator ==(TileCoord a, TileCoord b) => a.Equals(b);

        /// <summary>Inequality by index.</summary>
        public static bool operator !=(TileCoord a, TileCoord b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => $"({X}, {Z})";
    }
}
