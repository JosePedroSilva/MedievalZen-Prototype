using MedievalZen.Endless;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Cinematic
{
    /// <summary>
    /// <see cref="ICinematicQueries"/> over the live scene, in both modes: ground and water from
    /// <c>GameWorld.Current.Surface</c>, solid things from physics (terrain colliders, buildings, props, trunks near the
    /// traveller; the player's own colliders ignored), trees from the endless road's instanced cells or the finite world's
    /// terrain trees, and the built area from the endless streamer (or the finite terrain's rectangle). Allocation-free.
    /// </summary>
    internal sealed class SceneQueries : ICinematicQueries
    {
        private readonly Transform player;
        private readonly RaycastHit[] hits = new RaycastHit[12];
        private IWorldSurface densitySurface;
        private ITreeDensity density;
        private Terrain indexedTerrain;
        private TerrainTreeIndex terrainTrees;

        public SceneQueries(Transform player)
        {
            this.player = player;
        }

        /// <summary>Layers that block the camera's sight.</summary>
        public LayerMask Layers { get; set; } = Physics.DefaultRaycastLayers;

        private static IWorldSurface Surface
        {
            get
            {
                GameWorld world = GameWorld.Current;
                IWorldSurface surface = world != null ? world.Surface : null;
                return surface != null && surface.HasGround ? surface : null;
            }
        }

        public float GroundHeight(Vector3 p)
        {
            IWorldSurface surface = Surface;
            if (surface != null) return surface.GroundHeight(p);
            int count = Physics.RaycastNonAlloc(new Ray(new Vector3(p.x, p.y + 200f, p.z), Vector3.down), hits, 400f, Layers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(player)) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }

            return float.IsNegativeInfinity(best) ? player.position.y : best;
        }

        public float WaterHeight(Vector3 p)
        {
            IWorldSurface surface = Surface;
            return surface != null ? surface.WaterHeight(p) : -1f;
        }

        public float TreeDensity(Vector3 p)
        {
            IWorldSurface surface = Surface;
            if (surface == null) return 0f;
            if (!ReferenceEquals(surface, densitySurface))
            {
                densitySurface = surface;
                density = surface.CreateTreeDensity(16f);
            }

            return density != null ? density.Sample(p.x, p.z) : 0f;
        }

        public bool SightBlocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 0.05f) return false;
            // Stop short of the target point: it lies inside the traveller's capsule (ignored anyway) or on the seat.
            float reach = Mathf.Max(0f, length - 0.45f);
            int count = Physics.RaycastNonAlloc(new Ray(from, d / length), hits, reach, Layers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(player)) continue;
                return true;
            }

            EndlessWorld endless = EndlessWorld.Current;
            if (endless != null) return endless.TreeBlocksSight(from, to);
            TerrainTreeIndex trees = TerrainTrees();
            return trees != null && trees.SegmentBlocked(from, to);
        }

        public bool InsideTree(Vector3 p, float pad)
        {
            EndlessWorld endless = EndlessWorld.Current;
            if (endless != null) return endless.InsideTree(p, pad);
            TerrainTreeIndex trees = TerrainTrees();
            return trees != null && trees.PointInside(p, pad);
        }

        public bool InBuiltArea(Vector3 p)
        {
            EndlessWorld endless = EndlessWorld.Current;
            if (endless != null) return endless.GroundReadyAt(p, 0f);
            GameWorld world = GameWorld.Current;
            Terrain terrain = world != null ? world.Terrain : null;
            if (terrain == null || terrain.terrainData == null) return true;
            Vector3 o = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;
            const float inset = 10f;
            return p.x > o.x + inset && p.z > o.z + inset && p.x < o.x + size.x - inset && p.z < o.z + size.z - inset;
        }

        private TerrainTreeIndex TerrainTrees()
        {
            GameWorld world = GameWorld.Current;
            Terrain terrain = world != null ? world.Terrain : null;
            if (!ReferenceEquals(terrain, indexedTerrain))
            {
                indexedTerrain = terrain;
                terrainTrees = TerrainTreeIndex.For(terrain);
            }

            return terrainTrees;
        }
    }
}
