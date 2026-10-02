namespace MedievalZen.Endless
{
    /// <summary>Level of detail a tile is generated at.</summary>
    public enum TileLod
    {
        /// <summary>Walkable tile near the player: full heightmap, paint, grass details, trees, props, water.</summary>
        Near = 0,

        /// <summary>Distant silhouette tile: coarse heightmap and paint, large trees only, no details or props, no collider.</summary>
        Far = 1,
    }
}
