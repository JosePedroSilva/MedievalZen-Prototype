using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Everything needed to build one terrain tile, as plain data produced off the main thread. Positions are
    /// tile-local: x and z in [0, <see cref="TileSize"/>] from the tile's minimum corner, y in absolute metres.
    /// </summary>
    public sealed class TileBlueprint
    {
        /// <summary>Which tile this is.</summary>
        public TileCoord Coord;

        /// <summary>Level of detail it was generated at.</summary>
        public TileLod Lod;

        /// <summary>Edge length in metres.</summary>
        public float TileSize;

        /// <summary>Vertical extent the heights are normalised by (<see cref="EndlessParams.heightRange"/>).</summary>
        public float HeightRange;

        /// <summary>
        /// Heights as <c>[z, x]</c>, normalised to 0..1 of <see cref="HeightRange"/>, ready for
        /// <c>TerrainData.SetHeights</c>. The first and last rows/columns lie exactly on the tile's edges and are
        /// identical to the neighbouring tiles' edge samples.
        /// </summary>
        public float[,] Heights;

        /// <summary>
        /// Ground paint as <c>[z, x, layer]</c> with the channel order of <see cref="TerrainShaper"/>
        /// (<see cref="TerrainShaper.LayerCount"/> channels); weights sum to 1.
        /// </summary>
        public float[,,] Splat;

        /// <summary>Cells per edge of the detail density maps (0 when there are none).</summary>
        public int DetailResolution;

        /// <summary>Grass and flower density maps (empty for far tiles).</summary>
        public List<DetailLayer> Details = new List<DetailLayer>();

        /// <summary>Terrain trees (tile-local positions).</summary>
        public List<TreePlacement> Trees = new List<TreePlacement>();

        /// <summary>
        /// Prefab instances (tile-local positions). <see cref="PropPlacement.settlementId"/> holds the
        /// <see cref="Waypoint.Id"/> of the place a prop belongs to, or -1 for countryside scatter.
        /// </summary>
        public List<PropPlacement> Props = new List<PropPlacement>();

        /// <summary>Lake level in absolute metres for this tile, or -1 when the tile has no water. A tile never has two levels.</summary>
        public float WaterHeight = -1f;

        /// <summary>Waypoints whose centre lies in this tile.</summary>
        public List<Waypoint> Waypoints = new List<Waypoint>();

        /// <summary>How long the pure generation took, in milliseconds (diagnostics).</summary>
        public double GenerationMs;
    }
}
