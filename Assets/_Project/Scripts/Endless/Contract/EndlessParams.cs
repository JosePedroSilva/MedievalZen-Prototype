namespace MedievalZen.Endless
{
    /// <summary>
    /// Plain inputs of the endless-road generator (no Unity objects, safe to read from any thread). Built from
    /// <see cref="EndlessSettings"/>; the same values always produce the same world.
    /// </summary>
    public sealed class EndlessParams
    {
        /// <summary>World seed.</summary>
        public int seed = 12345;

        /// <summary>Edge length of a terrain tile in metres.</summary>
        public float tileSize = 256f;

        /// <summary>Height samples per edge of a near tile (2^n + 1, shared with neighbours along the edges).</summary>
        public int nearHeightResolution = 129;

        /// <summary>Splat (ground paint) texels per edge of a near tile.</summary>
        public int nearSplatResolution = 256;

        /// <summary>Grass/flower density cells per edge of a near tile.</summary>
        public int nearDetailResolution = 128;

        /// <summary>Height samples per edge of a far tile (2^n + 1).</summary>
        public int farHeightResolution = 33;

        /// <summary>Splat texels per edge of a far tile.</summary>
        public int farSplatResolution = 64;

        /// <summary>
        /// Vertical extent of every tile's terrain in metres. All heights lie in [0, heightRange]; tile heightmaps are
        /// normalised by it so neighbouring tiles meet exactly.
        /// </summary>
        public float heightRange = 420f;

        /// <summary>Road width in metres.</summary>
        public float roadWidth = 5f;

        /// <summary>Steepest grade the road may have (rise over run).</summary>
        public float maxRoadGrade = 0.08f;

        /// <summary>Shortest and longest stretch of road between two inns, in metres.</summary>
        public float innSpacingMin = 1200f;

        /// <summary>See <see cref="innSpacingMin"/>.</summary>
        public float innSpacingMax = 2400f;

        /// <summary>Shortest and longest length of a region (one kind of landscape) along the road, in metres.</summary>
        public float regionLengthMin = 1500f;

        /// <summary>See <see cref="regionLengthMin"/>.</summary>
        public float regionLengthMax = 3500f;
    }
}
