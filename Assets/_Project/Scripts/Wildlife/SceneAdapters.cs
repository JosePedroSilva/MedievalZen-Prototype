using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// The ground the animals walk on in the scene: heights and water from <see cref="IWorldSurface"/>
    /// (<c>GameWorld.Current.Surface</c>, which is the built terrain in both modes), trunks from the endless road's
    /// instanced trees, and buildings, fences and props from physics (terrain and the traveller ignored). Main thread only.
    /// </summary>
    internal sealed class SceneGround : IWildlifeGround
    {
        private readonly Collider[] hits = new Collider[8];
        private readonly IWorldSurface surface;
        private readonly EndlessWorld endless;

        public SceneGround(IWorldSurface surface, EndlessWorld endless)
        {
            this.surface = surface;
            this.endless = endless;
        }

        public float Height(float x, float z) => surface.GroundHeight(new Vector3(x, 0f, z));

        public bool IsWater(float x, float z)
        {
            var p = new Vector3(x, 0f, z);
            float w = surface.WaterHeight(p);
            return w > 0f && w > surface.GroundHeight(p) + 0.05f;
        }

        public bool IsBlocked(float x, float z, float radius)
        {
            float h = Height(x, z);
            if (endless != null && endless.InsideTree(new Vector3(x, h + 0.5f, z), radius)) return true;
            return Physical(new Vector3(x, h, z), radius);
        }

        /// <summary>True when a collider other than the terrain or a character stands within <paramref name="radius"/> of a point at body height.</summary>
        public bool Physical(Vector3 groundPoint, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(groundPoint + Vector3.up * (radius + 0.35f), radius, hits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i];
                if (c == null || c is TerrainCollider || c is CharacterController) continue;
                return true;
            }

            return false;
        }
    }

    /// <summary>The finite world's road network for events that cross the road (scene coordinates are absolute there).</summary>
    internal sealed class NetworkRoad : IWildlifeRoad
    {
        private readonly RoadNetwork roads;

        public NetworkRoad(RoadNetwork roads)
        {
            this.roads = roads;
        }

        public bool Nearest(float x, float z, out float roadX, out float roadZ, out float dirX, out float dirZ, out float distance)
        {
            roadX = roadZ = dirX = 0f;
            dirZ = 1f;
            distance = float.MaxValue;
            if (roads == null || roads.Segments.Count == 0) return false;
            RoadLocation here = roads.Project(new Vector3(x, 0f, z));
            if (!here.IsValid) return false;
            Vector3 p = here.Position, t = here.Segment.Tangent(here.Distance);
            roadX = p.x;
            roadZ = p.z;
            var flat = new Vector2(t.x, t.z);
            if (flat.sqrMagnitude > 1e-6f) flat.Normalize();
            dirX = flat.x;
            dirZ = flat.y;
            distance = new Vector2(p.x - x, p.z - z).magnitude;
            return true;
        }

        public bool Along(float x, float z, float dirX, float dirZ, float ahead, out float roadX, out float roadZ)
        {
            roadX = roadZ = 0f;
            if (roads == null || roads.Segments.Count == 0) return false;
            RoadLocation here = roads.Project(new Vector3(x, 0f, z));
            if (!here.IsValid) return false;
            Vector3 t = here.Segment.Tangent(here.Distance);
            float sign = t.x * dirX + t.z * dirZ >= 0f ? 1f : -1f;
            float at = here.Distance + sign * ahead;
            if (at < 0f || at > here.Segment.Length) return false;
            Vector3 p = here.Segment.Evaluate(at);
            roadX = p.x;
            roadZ = p.z;
            return true;
        }
    }

    /// <summary>
    /// The finite world's land for the planner (main thread: it reads the terrain): heights, water and slope from the terrain,
    /// woods from the tree density, settlements and road distance from the road network. No pasture data is kept at runtime
    /// in the finite world, so it has farmyards and wildlife only. Scene coordinates are absolute there.
    /// </summary>
    internal sealed class FiniteHabitat : IWildlifeHabitat
    {
        private readonly GameWorld world;
        private readonly IWorldSurface surface;
        private readonly ITreeDensity trees;
        private readonly float maxHeight;

        public FiniteHabitat(GameWorld world, int seed)
        {
            this.world = world;
            surface = world.Surface;
            Seed = seed;
            trees = surface.HasGround ? surface.CreateTreeDensity(16f) : null;
            Terrain t = world.Terrain;
            maxHeight = t != null && t.terrainData != null ? t.terrainData.size.y : 200f;
        }

        public int Seed { get; }
        public bool ThreadSafe => false;
        public float Corridor => 12f;

        public HabitatSample Sample(double x, double z)
        {
            var p = new Vector3((float)x, 0f, (float)z);
            var s = new HabitatSample { Valid = surface.HasGround };
            if (!s.Valid) return s;
            float h = surface.GroundHeight(p);
            float hx = surface.GroundHeight(p + new Vector3(2f, 0f, 0f)), hz = surface.GroundHeight(p + new Vector3(0f, 0f, 2f));
            s.Height = h;
            s.Slope = Mathf.Sqrt((hx - h) * (hx - h) + (hz - h) * (hz - h)) / 2f;
            float water = surface.WaterHeight(p);
            s.Water = water > 0f && h < water + 0.05f;
            s.Shore = water > 0f && !s.Water && h < water + 1.5f ? 0.7f : 0f;
            s.Forest = trees != null ? Mathf.Clamp01(trees.Sample(p.x, p.z) / 2.5f) : 0f;
            s.Highland = Mathf.Clamp01((h / Mathf.Max(1f, maxHeight) - 0.45f) * 3f);
            s.RoadDistance = RoadDistance(p);
            s.Region = s.Highland > 0.6f ? RegionKind.HighlandPass : s.Forest > 0.5f ? RegionKind.Forest : RegionKind.Meadow;
            return s;
        }

        public bool IsWater(double x, double z)
        {
            var p = new Vector3((float)x, 0f, (float)z);
            float water = surface.WaterHeight(p);
            return water > 0f && surface.GroundHeight(p) < water + 0.05f;
        }

        private float RoadDistance(Vector3 p)
        {
            RoadNetwork roads = world.Roads;
            if (roads == null || roads.Segments.Count == 0) return 999f;
            RoadLocation here = roads.Project(p);
            if (!here.IsValid) return 999f;
            Vector3 q = here.Position;
            return new Vector2(q.x - p.x, q.z - p.z).magnitude;
        }

        public void Settlements(double x0, double z0, double size, List<HabitatSettlement> results)
        {
            RoadNetwork roads = world.Roads;
            if (roads == null) return;
            foreach (Settlement s in roads.Settlements)
            {
                Vector3 c = s.Position;
                double nx = System.Math.Max(x0, System.Math.Min(c.x, x0 + size)), nz = System.Math.Max(z0, System.Math.Min(c.z, z0 + size));
                double reach = s.Radius + 120.0;
                if ((nx - c.x) * (nx - c.x) + (nz - c.z) * (nz - c.z) > reach * reach) continue;
                float heading = 0f;
                RoadLocation here = roads.Project(c);
                if (here.IsValid)
                {
                    Vector3 t = here.Segment.Tangent(here.Distance);
                    heading = Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg;
                }

                results.Add(new HabitatSettlement
                {
                    Id = s.Id,
                    Kind = s.Type == SettlementType.Inn ? HabitatSettlementKind.Inn : s.Type == SettlementType.Town ? HabitatSettlementKind.Town : HabitatSettlementKind.Village,
                    X = c.x,
                    Z = c.z,
                    Radius = s.Radius,
                    RoadHeadingDeg = heading,
                });
            }
        }

        public void Pastures(double x0, double z0, double size, List<HabitatPasture> results)
        {
        }
    }
}
