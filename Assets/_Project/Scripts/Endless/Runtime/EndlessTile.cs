using System.Collections.Generic;
using MedievalZen.Environment;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>A pooled near-tile Terrain object with its own in-memory TerrainData (ground, paint, grass and the collider; never trees).</summary>
    internal sealed class TerrainSlot
    {
        public GameObject Go;
        public Terrain Terrain;
        public TerrainCollider Collider;
        public TerrainData Data;
        public TileLod Lod;
        public bool LayersAssigned;
        public int Uses;
    }

    /// <summary>A pooled far-tile mesh object: one MeshRenderer with its own runtime Mesh (rewritten on reuse).</summary>
    internal sealed class FarMeshSlot
    {
        public GameObject Go;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public Mesh Mesh;
        public int Triangles;
    }

    /// <summary>Where a tile is in its life.</summary>
    internal enum TileState
    {
        /// <summary>Being applied slice by slice; not visible yet.</summary>
        Building,

        /// <summary>Complete and visible.</summary>
        Ready,

        /// <summary>Hidden and handing its pooled objects back.</summary>
        Releasing,

        /// <summary>Gone.</summary>
        Released,
    }

    /// <summary>One terrain tile in the scene: a root at the tile's corner holding the terrain, props, water and places.</summary>
    internal sealed class EndlessTile
    {
        public TileCoord Coord;
        public TileLod Lod;
        public TileState State;
        public TileBlueprint Blueprint;
        public TreeDensityGrid Density;
        public GameObject Root;
        public TerrainSlot Slot;
        public FarMeshSlot FarMesh;
        public FarTileMeshData FarMeshData;
        public bool FarCounted;
        public TreeCellSet Trees;
        public GameObject Water;
        public GameObject Places;
        public readonly List<(GameObject prefab, GameObject instance)> Props = new List<(GameObject, GameObject)>();
        public readonly List<GameObject> Groups = new List<GameObject>();
        public readonly List<(GameObject cabin, PropPlacement prop, GameObject prefab)> Cabins = new List<(GameObject, PropPlacement, GameObject)>();
        public readonly List<(GameObject fire, int settlementId)> Fires = new List<(GameObject, int)>();
        public IEnumerator<string> Steps;
        public double StartedAt;
        public int ReleaseCursor;

        public bool IsNear => Lod == TileLod.Near;

        public override string ToString() => $"{Lod} {Coord} {State}";
    }

    /// <summary>
    /// A generated blueprint waiting to be applied, with what the worker thread prepared from it: the tree density for the
    /// world surface, the far mesh (far tiles) or the instanced-tree cells (near tiles).
    /// </summary>
    internal sealed class GeneratedTile
    {
        public TileBlueprint Blueprint;
        public TreeDensityGrid Density;
        public FarTileMeshData FarMesh;
        public TreeCellSet Trees;
        public double Milliseconds;
    }

    /// <summary>Everything the streamer tracks for one tile coordinate.</summary>
    internal sealed class CoordState
    {
        public TileCoord Coord;
        public TileLod? Target;
        public float Priority;
        public EndlessTile Active;
        public EndlessTile Building;
        public GeneratedTile Ready;
        public GenerationJob Job;
        public int Failures;
        public float RetryAt;

        /// <summary>The level of detail this coordinate has or is getting (what hysteresis compares against).</summary>
        public TileLod? Current
        {
            get
            {
                if (Building != null) return Building.Lod;
                if (Ready != null) return Ready.Blueprint.Lod;
                if (Job != null) return Job.Lod;
                return Active?.Lod;
            }
        }

        public bool Idle => Active == null && Building == null && Ready == null && Job == null;
    }

    /// <summary>A tile being generated on a worker thread.</summary>
    internal sealed class GenerationJob
    {
        public TileCoord Coord;
        public TileLod Lod;
        public System.Threading.CancellationTokenSource Cancel;
        public System.Threading.Tasks.Task<GeneratedTile> Task;
    }
}
