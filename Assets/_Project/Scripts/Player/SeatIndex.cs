using System.Collections.Generic;
using MedievalZen.Travel;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>One prop the traveller could sit on (or by), as kept by <see cref="SeatIndex"/>.</summary>
    public struct SeatCandidate
    {
        /// <summary>The prop instance (or the inn door point).</summary>
        public Transform Transform;

        /// <summary>What it is.</summary>
        public SeatKind Kind;

        /// <summary>Mesh bounds in the prop root's local space (unscaled).</summary>
        public Bounds LocalBounds;

        /// <summary>Doorsteps: horizontal direction out of the door, or zero when unknown.</summary>
        public Vector3 Outward;

        /// <summary>Planar distance from the query point (set by <see cref="SeatIndex.Gather"/>).</summary>
        public float Distance;

        /// <summary>True when this candidate refers to something.</summary>
        public bool IsValid => Transform != null && Kind != SeatKind.None;
    }

    /// <summary>
    /// Every seat-like prop in the loaded world without a component per prop: the finite world's baked props are found
    /// once by name under the <see cref="GameWorld"/> (logs, stumps, crates, chairs, low rocks, camp fireplaces, see
    /// <see cref="SeatCatalog"/>), the endless road's pooled props register as the tile builder places them, and inn
    /// doorsteps come from the <see cref="InnRest"/> interactables. Entries keep their transform, so positions are always
    /// current (floating-origin shifts included); pooled props that are switched off are skipped.
    /// </summary>
    public static class SeatIndex
    {
        private static readonly List<SeatCandidate> Entries = new List<SeatCandidate>(512);
        private static readonly Dictionary<int, int> ByInstance = new Dictionary<int, int>(512);
        private static readonly Dictionary<string, Bounds> BoundsByName = new Dictionary<string, Bounds>();
        private static GameWorld scannedWorld;
        private static float nextCompact;

        /// <summary>Number of registered prop entries (doorsteps not included).</summary>
        public static int Count => Entries.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Entries.Clear();
            ByInstance.Clear();
            BoundsByName.Clear();
            scannedWorld = null;
            nextCompact = 0f;
        }

        /// <summary>
        /// Registers a placed prop instance of <paramref name="prefab"/> if it is a seat kind (called by the endless tile builder
        /// for every placed prop; cheap and idempotent for pooled instances that come back).
        /// </summary>
        public static void Register(GameObject instance, GameObject prefab)
        {
            if (instance == null || prefab == null) return;
            SeatKind kind = SeatCatalog.Classify(prefab.name);
            if (kind == SeatKind.None) return;
            int id = instance.GetInstanceID();
            if (ByInstance.ContainsKey(id)) return;
            Bounds local = WorldBuilder.LocalBounds(prefab);
            ByInstance[id] = Entries.Count;
            Entries.Add(new SeatCandidate { Transform = instance.transform, Kind = kind, LocalBounds = local });
        }

        /// <summary>
        /// Finds the finite world's baked seat props once (by name, under <paramref name="world"/>). Does nothing in the endless
        /// mode (its props register themselves) or when the same world was scanned before.
        /// </summary>
        public static void ScanWorld(GameWorld world)
        {
            if (world == null || ReferenceEquals(world, scannedWorld)) return;
            scannedWorld = world;
            if (MedievalZen.Endless.EndlessWorld.Current != null) return;
            int found = 0;
            foreach (Transform t in world.GetComponentsInChildren<Transform>(true))
            {
                Transform parent = t.parent;
                if (parent == null) continue;
                // World builder layout: countryside props under "Chunk_x_z", settlement props under "<settlement>/Props".
                string parentName = parent.name;
                if (!parentName.StartsWith("Chunk_") && parentName != "Props") continue;
                SeatKind kind = SeatCatalog.Classify(t.name);
                if (kind == SeatKind.None) continue;
                int id = t.gameObject.GetInstanceID();
                if (ByInstance.ContainsKey(id)) continue;
                ByInstance[id] = Entries.Count;
                Entries.Add(new SeatCandidate { Transform = t, Kind = kind, LocalBounds = BoundsOf(t) });
                found++;
            }

            Debug.Log($"Seats: found {found} seat-like props in the world (logs, stumps, crates, chairs, low rocks, fireplaces).");
        }

        /// <summary>
        /// Every seat within <paramref name="maxDistance"/> metres (planar) of <paramref name="position"/>, nearest first, into
        /// <paramref name="results"/> (cleared first): active props, plus inn doorsteps when <paramref name="includeDoorsteps"/>.
        /// A log counts from its nearest sitting point (its pivot may be a metre away), a fire from its ring. Returns the count;
        /// allocation-free once <paramref name="results"/> has grown.
        /// </summary>
        public static int Gather(Vector3 position, float maxDistance, bool includeDoorsteps, List<SeatCandidate> results)
        {
            results.Clear();
            bool sawDead = false;
            for (int i = 0; i < Entries.Count; i++)
            {
                SeatCandidate c = Entries[i];
                if (c.Transform == null)
                {
                    sawDead = true;
                    continue;
                }

                Vector3 p = c.Transform.position;
                float dx = p.x - position.x, dz = p.z - position.z;
                float sqr = dx * dx + dz * dz;
                float slack = c.Kind == SeatKind.Log ? 1.5f : c.Kind == SeatKind.Fire ? 1.4f : 0f;
                float reach = maxDistance + slack;
                if (sqr > reach * reach || Mathf.Abs(p.y - position.y) > 2.5f || !c.Transform.gameObject.activeInHierarchy) continue;
                c.Distance = Mathf.Max(0f, Mathf.Sqrt(sqr) - slack);
                if (c.Distance <= maxDistance) Insert(results, c);
            }

            if (includeDoorsteps)
            {
                var all = Interactable.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (!(all[i] is InnRest door) || door == null) continue;
                    Vector3 p = door.transform.position;
                    float dx = p.x - position.x, dz = p.z - position.z;
                    float sqr = dx * dx + dz * dz;
                    if (sqr > maxDistance * maxDistance || Mathf.Abs(p.y - position.y) > 2.5f) continue;
                    Insert(results, new SeatCandidate
                    {
                        Transform = door.transform, Kind = SeatKind.Doorstep, Outward = DoorOutward(door.transform), Distance = Mathf.Sqrt(sqr),
                        LocalBounds = new Bounds(Vector3.zero, Vector3.zero),
                    });
                }
            }

            if (sawDead && Time.unscaledTime >= nextCompact) Compact();
            return results.Count;
        }

        private static void Insert(List<SeatCandidate> list, SeatCandidate c)
        {
            int at = list.Count;
            while (at > 0 && list[at - 1].Distance > c.Distance) at--;
            list.Insert(at, c);
        }

        /// <summary>Finds the nearest active camp fireplace within <paramref name="maxDistance"/> metres (planar).</summary>
        public static bool FindFire(Vector3 position, float maxDistance, out Vector3 fire)
        {
            fire = default;
            float bestSqr = maxDistance * maxDistance;
            bool any = false;
            for (int i = 0; i < Entries.Count; i++)
            {
                SeatCandidate c = Entries[i];
                if (c.Kind != SeatKind.Fire || c.Transform == null || !c.Transform.gameObject.activeInHierarchy) continue;
                Vector3 p = WorldCentre(c);
                float dx = p.x - position.x, dz = p.z - position.z;
                float sqr = dx * dx + dz * dz;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                fire = p;
                any = true;
            }

            return any;
        }

        /// <summary>World position of the centre of a candidate's mesh bounds (at the bounds' bottom).</summary>
        public static Vector3 WorldCentre(SeatCandidate c)
        {
            Vector3 centre = c.LocalBounds.center;
            centre.y = c.LocalBounds.min.y;
            return c.Transform.TransformPoint(centre);
        }

        private static void Compact()
        {
            nextCompact = Time.unscaledTime + 5f;
            int write = 0;
            for (int read = 0; read < Entries.Count; read++)
            {
                if (Entries[read].Transform == null) continue;
                Entries[write++] = Entries[read];
            }

            Entries.RemoveRange(write, Entries.Count - write);
            ByInstance.Clear();
            for (int i = 0; i < Entries.Count; i++) ByInstance[Entries[i].Transform.gameObject.GetInstanceID()] = i;
        }

        /// <summary>Mesh bounds of a scene instance in its own local space, cached per prefab name.</summary>
        private static Bounds BoundsOf(Transform t)
        {
            string key = t.name;
            int clone = key.IndexOf(" (", System.StringComparison.Ordinal);
            if (clone > 0) key = key.Substring(0, clone);
            if (BoundsByName.TryGetValue(key, out Bounds cached)) return cached;
            Matrix4x4 toRoot = t.worldToLocalMatrix;
            bool any = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter filter in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }

            if (!any) bounds = new Bounds(new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.5f));
            BoundsByName[key] = bounds;
            return bounds;
        }

        /// <summary>
        /// Out of the door of an inn: the door point's own forward when the builder set it (worlds built after 2026-10-02), else
        /// away from the nearest cabin among its siblings, else zero (the view decides).
        /// </summary>
        private static Vector3 DoorOutward(Transform door)
        {
            if (door.rotation != Quaternion.identity) return SeatMath.Planar(door.forward, Vector3.zero);
            Transform parent = door.parent;
            if (parent == null) return Vector3.zero;
            float bestSqr = 30f * 30f;
            Vector3 outward = Vector3.zero;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling == door || sibling.name.IndexOf("Cabin", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Vector3 d = door.position - sibling.position;
                d.y = 0f;
                if (d.sqrMagnitude >= bestSqr) continue;
                bestSqr = d.sqrMagnitude;
                outward = SeatMath.Planar(d, Vector3.zero);
            }

            return outward;
        }
    }
}
