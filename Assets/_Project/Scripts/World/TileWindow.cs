using System;

namespace MedievalZen.World
{
    /// <summary>
    /// Where a generation window sits in an endless world. The finite world's stages run on local coordinates
    /// 0..<see cref="WorldGenParams.worldSize"/>; with a window attached (<see cref="WorldGenParams.window"/>) the same stages
    /// treat local (0, 0) as the absolute position (<see cref="originX"/>, <see cref="originZ"/>), so noise lookups, patch
    /// patterns, scatter chunks and per-cell dithering are functions of absolute position and continue across window edges.
    /// Plain data; pure.
    /// </summary>
    public sealed class TileWindow
    {
        /// <summary>
        /// Period (metres) by which absolute coordinates are wrapped before they become float noise offsets: keeps noise
        /// coordinates precise (6 cm or better) on journeys of any length. Patterns jump once every 1048 km.
        /// </summary>
        public const double NoiseWrap = 1048576.0;

        /// <summary>Absolute X (metres) of local x = 0.</summary>
        public double originX;

        /// <summary>Absolute Z (metres) of local z = 0.</summary>
        public double originZ;

        /// <summary><see cref="originX"/> wrapped by <see cref="NoiseWrap"/>: added to local x before noise lookups.</summary>
        public float noiseOriginX;

        /// <summary><see cref="originZ"/> wrapped by <see cref="NoiseWrap"/>: added to local z before noise lookups.</summary>
        public float noiseOriginZ;

        /// <summary>Edge (metres) of the absolute scatter chunks; must divide the tile size so chunks never straddle tiles.</summary>
        public float chunkSize = 128f;

        /// <summary>
        /// Absolute rectangle [keepMinX, keepMaxX) × [keepMinZ, keepMaxZ) whose scatter chunks are kept: candidates in other
        /// chunks of the window are still evaluated (they occupy space) but not returned, so a prop or tree belongs to exactly
        /// one tile.
        /// </summary>
        public double keepMinX, keepMinZ, keepMaxX, keepMaxZ;

        /// <summary>
        /// Optional (null: none): the rock family the scatter places instead of a rule's own at window-local (x, z), or null to keep
        /// it. The endless road's pass ranges swap the Meadow rocks for their grey rock families. A function of position only, so
        /// every window agrees.
        /// </summary>
        public Func<string, float, float, string> RockFamily;

        /// <summary>Creates a window whose local (0, 0) is the absolute (<paramref name="x"/>, <paramref name="z"/>).</summary>
        public TileWindow(double x, double z)
        {
            originX = x;
            originZ = z;
            noiseOriginX = (float)Wrap(x);
            noiseOriginZ = (float)Wrap(z);
            keepMinX = keepMinZ = double.NegativeInfinity;
            keepMaxX = keepMaxZ = double.PositiveInfinity;
        }

        /// <summary><paramref name="v"/> wrapped into [0, <see cref="NoiseWrap"/>).</summary>
        public static double Wrap(double v) => v - NoiseWrap * Math.Floor(v / NoiseWrap);

        /// <summary>Index of the absolute scatter chunk containing absolute coordinate <paramref name="v"/>.</summary>
        public long ChunkIndex(double v) => (long)Math.Floor(v / chunkSize);

        /// <summary>True when the chunk with absolute indices (cx, cz) lies in the keep rectangle.</summary>
        public bool Keeps(long cx, long cz)
        {
            double x = cx * (double)chunkSize;
            double z = cz * (double)chunkSize;
            return x >= keepMinX && x < keepMaxX && z >= keepMinZ && z < keepMaxZ;
        }

        /// <summary>
        /// Hash of an absolute cell (for per-cell dithering that must agree between windows), salted.
        /// </summary>
        public static uint CellHash(long cx, long cz, uint salt)
        {
            unchecked
            {
                ulong h = (ulong)cx * 0x9E3779B97F4A7C15UL ^ (ulong)cz * 0xC2B2AE3D27D4EB4FUL ^ salt * 0x165667B19E3779F9UL;
                h ^= h >> 31;
                h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 29;
                return (uint)(h ^ (h >> 32));
            }
        }
    }
}
