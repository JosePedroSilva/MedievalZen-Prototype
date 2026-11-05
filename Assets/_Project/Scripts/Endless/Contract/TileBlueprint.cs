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
        /// (<see cref="TerrainShaper.EndlessLayerCount"/> channels: the shared eight, then mountain rock, scree, heath); weights sum to 1.
        /// </summary>
        public float[,,] Splat;

        /// <summary>
        /// True when the paint uses the mountain channels (<see cref="TerrainShaper.MountainRockLayer"/> and after): a near tile then
        /// gets the eleven-layer set (one more terrain pass); every other tile keeps the shared eight.
        /// </summary>
        public bool MountainPaint;

        /// <summary>
        /// True when the paint uses the field channels (<see cref="TerrainShaper.FieldGoldLayer"/> .. <see cref="TerrainShaper.FieldGreenLayer"/>):
        /// a near tile then adds the three field layers to its set (with <see cref="MountainPaint"/> both groups: fourteen layers).
        /// </summary>
        public bool FieldPaint;

        /// <summary>
        /// True when the paint uses the woods' leaf litter (<see cref="TerrainShaper.LeafLitterLayer"/>): a near tile then draws it in the
        /// cobblestone's slot (no extra layer), or appends it when <see cref="CobblePaint"/> is also true.
        /// </summary>
        public bool LitterPaint;

        /// <summary>True when the paint uses the cobblestone channel (a village's square).</summary>
        public bool CobblePaint;

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

        /// <summary>
        /// Water surfaces of the streams crossing this tile (tile-local polylines, see <see cref="StreamRibbon"/>); empty when
        /// there are none. Near and far tiles carry them (far tiles only tint their ground with them).
        /// </summary>
        public List<StreamRibbon> Streams = new List<StreamRibbon>();

        /// <summary>
        /// The ribbon mesh of <see cref="Streams"/> as plain vertex and index arrays, prepared on the generating thread (near tiles
        /// only; null when the tile has no stream). The main thread only uploads it.
        /// </summary>
        public StreamRibbonMeshData StreamMesh;

        /// <summary>
        /// Near tiles: the parapets, end posts and approach walls of the bridges whose centre lies in this tile, with their box
        /// colliders (<see cref="Generation.BridgeWalls"/>), or null. The main thread only uploads it.
        /// </summary>
        public Generation.BridgeWallMeshData BridgeWalls;

        /// <summary>
        /// Near tiles: the decks over a walled city's terrace banks (<see cref="Generation.TerraceDecks"/>): cobbled tops with box colliders
        /// and a height lookup for the ground queries, or null. The main thread only uploads it.
        /// </summary>
        public Generation.TerraceDeckMeshData TerraceDecks;

        /// <summary>Near tiles in a pass range or reached by a walled city's castle rock: the faceted rock skin over the steep rock (<see cref="RockSkinMeshData"/>), or null.</summary>
        public RockSkinMeshData RockSkin;

        /// <summary>
        /// Field-boundary pieces (dry-stone wall, hedge and fence pieces, pillars, posts and open gate leaves; near tiles only),
        /// tile-local. Drawn instanced, never as GameObjects. A piece's <see cref="PropPlacement.yaw"/> is the direction the
        /// boundary runs, <see cref="PropPlacement.pitch"/> tilts that direction down (negative: up the slope) and
        /// <see cref="PropPlacement.roll"/> leans it sideways (see <c>TreeCellSet.PiecePose</c>); the piece's mesh is laid along
        /// the run and centred on <see cref="PropPlacement.position"/> by the renderer.
        /// </summary>
        public List<PropPlacement> Pieces = new List<PropPlacement>();

        /// <summary>Field boundaries as straight stretches for the distant mesh (<see cref="TileLod.Mid"/> tiles only), tile-local.</summary>
        public List<FieldEdge> FieldEdges = new List<FieldEdge>();
    }

    /// <summary>What a field boundary is made of.</summary>
    public enum FieldEdgeKind : byte
    {
        /// <summary>A hedge (dense, about 1.8 m).</summary>
        Hedge = 0,

        /// <summary>A dry-stone wall (about 1.2 m).</summary>
        Wall = 1,

        /// <summary>A post-and-rail fence (about 1.4 m, see-through).</summary>
        Fence = 2,
    }

    /// <summary>A straight stretch of field boundary on the ground, tile-local (distant tiles draw it as a low ridge).</summary>
    public struct FieldEdge
    {
        /// <summary>Ends (tile-local metres).</summary>
        public float X0, Z0, X1, Z1;

        /// <summary>What it is made of.</summary>
        public FieldEdgeKind Kind;
    }
}
