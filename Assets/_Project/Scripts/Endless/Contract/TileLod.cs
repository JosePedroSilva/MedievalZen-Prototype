namespace MedievalZen.Endless
{
    /// <summary>Level of detail a tile is generated at.</summary>
    public enum TileLod
    {
        /// <summary>Walkable tile near the player: full heightmap, paint, grass details, trees, props, water.</summary>
        Near = 0,

        /// <summary>Distant tile: an 8 m height grid and 4 m paint, every tree, large rocks and the houses of places (drawn as
        /// low-poly solids), no details, no collider.</summary>
        Far = 1,

        /// <summary>
        /// A distant tile in the first ring beyond the near tiles (added 2026-10-02): as <see cref="Far"/> but on a 4 m height grid
        /// (<see cref="EndlessParams.midHeightResolution"/>) for a finer silhouette and facets.
        /// </summary>
        Mid = 2,
    }
}
