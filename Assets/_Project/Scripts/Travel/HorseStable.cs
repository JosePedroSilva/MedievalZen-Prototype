using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Player;
using MedievalZen.Wildlife;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// The horses of the endless road (created on the player by <see cref="HorseMount"/>): a saddled horse hitched at every inn near
    /// the traveller, and the traveller's own horse once taken (ridden, or left grazing where the rider got down; gone when left more
    /// than 400 m behind and unseen, and the inn it came from has one again). A small pool of stripped palette horses, built once.
    /// <para>The hitching spot is found at run time from what the tile builder registered: the inn's <see cref="InnRest"/> door and the
    /// road nearest to it. Candidates stand beside the road on the door's side, parallel to it, a few metres along from the door, off
    /// the road and its 1.5 m verge with a margin; the first on gentle dry ground with no collider in the horse's box wins (none: no
    /// horse at that inn).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HorseStable : MonoBehaviour, IOriginShiftable
    {
        private const int PoolSize = 3;
        private const float ShowWithin = 220f, ReleaseBeyond = 300f, ScanSeconds = 0.5f;
        private static readonly string[] Prefabs = { "Horse_Brown", "Horse_Grey", "Horse_Black" };
        private static readonly float[] AlongOffsets = { 5f, -5f, 8f, -8f, 2.5f, -2.5f, 11f, -11f };

        private readonly List<HorseBody> pool = new List<HorseBody>(PoolSize);
        private readonly Dictionary<int, (bool ok, Vector3 at, float yaw)> spots = new Dictionary<int, (bool, Vector3, float)>();
        private readonly Collider[] overlap = new Collider[8];
        private Transform root;
        private bool built, warned;
        private float nextScan;
        private HorseBody own;

        /// <summary>The traveller's own horse (ridden or left), or null.</summary>
        internal HorseBody Own => own;

        private bool Build()
        {
            if (built) return pool.Count > 0;
            built = true;
            WildlifePalette palette = Resources.Load<WildlifePalette>(WildlifePalette.ResourcePath);
            root = new GameObject("TravellerHorses").transform;
            for (int i = 0; i < PoolSize && palette != null; i++)
            {
                HorseBody b = HorseBody.Create(palette, Prefabs[i % Prefabs.Length], root);
                if (b != null) pool.Add(b);
            }

            if (pool.Count == 0) Debug.LogWarning("Horses: no horse in Resources/WildlifePalette (run MedievalZen/Wildlife/Build Wildlife Palette); riding is off.");
            return pool.Count > 0;
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            foreach (HorseBody b in pool) b.Position += delta;
            var keys = new List<int>(spots.Keys);
            foreach (int k in keys)
            {
                (bool ok, Vector3 at, float yaw) s = spots[k];
                spots[k] = (s.ok, s.at + delta, s.yaw);
            }
        }

        /// <summary>The nearest horse the traveller faces within reach (own horse first on a tie), or null.</summary>
        internal HorseBody FindMountable(Vector3 position, Vector3 forward, out float distance)
        {
            distance = float.PositiveInfinity;
            HorseBody best = null;
            foreach (HorseBody b in pool)
            {
                if (b.Holder == HorseBody.Role.Free || !b.Go.activeSelf) continue;
                if (b == own && HorseMount.Current != null && HorseMount.Current.Mounted) continue;
                Vector3 to = b.Position - position;
                to.y = 0f;
                float d = to.magnitude;
                if (d > MountStateMachine.MountReach || Mathf.Abs(b.Position.y - position.y) > 2f) continue;
                if (d > 1f && Vector3.Dot(forward.normalized, to / d) < 0.2f) continue;
                if (b == own) d -= 0.1f;
                if (d < distance)
                {
                    distance = d;
                    best = b;
                }
            }

            return best;
        }

        /// <summary>The horse becomes the traveller's own (its inn shows none while it lives).</summary>
        internal void Take(HorseBody b)
        {
            if (own != null && own != b) Release(own);
            own = b;
            b.Holder = HorseBody.Role.Own;
        }

        /// <summary>A resumed journey: the traveller's horse appears under them (null without a palette horse).</summary>
        internal HorseBody SpawnOwn(Vector3 position, float yaw)
        {
            if (!Build()) return null;
            HorseBody b = own ?? FreeBody();
            if (b == null) return null;
            b.InnId = -1;
            b.Position = position;
            b.Yaw = yaw;
            b.Show(true);
            Take(b);
            return b;
        }

        private HorseBody FreeBody()
        {
            foreach (HorseBody b in pool) if (b.Holder == HorseBody.Role.Free) return b;
            // All in use: the furthest hitched horse makes room.
            HorseBody far = null;
            float farD = -1f;
            Vector3 p = transform.position;
            foreach (HorseBody b in pool)
            {
                if (b.Holder != HorseBody.Role.Hitched) continue;
                float d = (b.Position - p).sqrMagnitude;
                if (d > farD) { farD = d; far = b; }
            }

            if (far != null) Release(far);
            return far;
        }

        private void Release(HorseBody b)
        {
            if (b == own) own = null;
            b.Holder = HorseBody.Role.Free;
            b.InnId = -1;
            b.Show(false);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            EndlessWorld endless = EndlessWorld.Current;
            if (endless == null || endless.Generator == null) return;   // the endless road only
            if (!built && !Build()) return;
            if (pool.Count == 0) return;

            HorseMount mount = HorseMount.Current;
            bool mounted = mount != null && mount.Mounted;
            Vector3 traveller = transform.position;

            // The own horse left behind and out of sight is gone; its inn has one again.
            if (own != null && !mounted)
            {
                float d = Vector3.Distance(Flat(own.Position), Flat(traveller));
                if (MountStateMachine.LeftBehind(d, own.Visible, false)) Release(own);
            }

            if (Time.unscaledTime >= nextScan && !endless.IsHolding)
            {
                nextScan = Time.unscaledTime + ScanSeconds;
                Scan(endless, traveller);
            }

            foreach (HorseBody b in pool)
            {
                if (b.Holder == HorseBody.Role.Free || (b == own && mounted)) continue;
                b.Place(dt);
                b.Animate(0f, b == own, dt);   // hitched horses stand tied; the traveller's own grazes where it was left
            }
        }

        private void Scan(EndlessWorld endless, Vector3 traveller)
        {
            // Hitched horses whose inn went away or fell behind are put back.
            foreach (HorseBody b in pool)
            {
                if (b.Holder != HorseBody.Role.Hitched) continue;
                if ((Flat(b.Position) - Flat(traveller)).magnitude > ReleaseBeyond || !InnPresent(b.InnId)) Release(b);
            }

            if (MedievalZen.Sound.SoundMix.CaptureRunning) return;   // a review tour or benchmark sees the road as it was
            var all = Interactable.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is InnRest inn) || inn == null || inn.SettlementId < 0) continue;
                Vector3 door = inn.transform.position;
                if ((Flat(door) - Flat(traveller)).magnitude > ShowWithin) continue;
                if (own != null && own.InnId == inn.SettlementId) continue;
                if (HasHitched(inn.SettlementId)) continue;
                if (!spots.TryGetValue(inn.SettlementId, out (bool ok, Vector3 at, float yaw) spot))
                {
                    spot = FindSpot(endless, inn);
                    spots[inn.SettlementId] = spot;
                    if (!spot.ok && !warned)
                    {
                        warned = true;
                        Debug.Log($"Horses: no clear hitching spot at '{inn.InnName}' (no horse there).");
                    }
                }

                if (!spot.ok) continue;
                HorseBody b = FreeBody();
                if (b == null) return;
                b.Holder = HorseBody.Role.Hitched;
                b.InnId = inn.SettlementId;
                b.Position = spot.at;
                b.Yaw = spot.yaw;
                b.Show(true);
                b.Place(0f);
            }

            if (spots.Count > 64) spots.Clear();
        }

        private bool HasHitched(int innId)
        {
            foreach (HorseBody b in pool) if (b.Holder == HorseBody.Role.Hitched && b.InnId == innId) return true;
            return false;
        }

        private static bool InnPresent(int innId)
        {
            var all = Interactable.All;
            for (int i = 0; i < all.Count; i++) if (all[i] is InnRest inn && inn != null && inn.SettlementId == innId) return true;
            return false;
        }

        private (bool, Vector3, float) FindSpot(EndlessWorld endless, InnRest inn)
        {
            IEndlessGenerator g = endless.Generator;
            float half = (g.Params != null ? g.Params.roadWidth : 5f) * 0.5f;
            float keep = half + 1.5f + 1.1f;   // road, verge, half a horse and a margin
            Vector3 door = inn.transform.position;
            (double dx, double dz) = endless.ToAbsolute(door);
            double s = g.NearestS(dx, dz, endless.S);
            RoadPoint r = g.Road(s);
            Vector3 road = door + new Vector3((float)(r.X - dx), 0f, (float)(r.Z - dz));
            Vector3 tangent = Quaternion.Euler(0f, r.HeadingDeg, 0f) * Vector3.forward;
            Vector3 side = Flat(door - road);
            float doorGap = side.magnitude;
            side = doorGap > 0.1f ? side / doorGap : new Vector3(tangent.z, 0f, -tangent.x);
            float offset = Mathf.Min(keep + 0.6f, Mathf.Max(keep, doorGap - 2f));
            IWorldSurface surface = GameWorld.Current != null ? GameWorld.Current.Surface : null;

            foreach (float along in AlongOffsets)
            {
                Vector3 at = road + tangent * along + side * offset;
                if (!Clear(endless, g, at, tangent, keep, surface, out float y)) continue;
                at.y = y;
                float yaw = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
                if (Vector3.Dot(tangent, Flat(door - at)) < 0f) yaw += 180f;   // faces along the road towards the inn's door
                return (true, at, yaw);
            }

            return (false, Vector3.zero, 0f);
        }

        private bool Clear(EndlessWorld endless, IEndlessGenerator g, Vector3 at, Vector3 tangent, float keep, IWorldSurface surface, out float y)
        {
            y = at.y;
            // Both ends of the horse off the road and its verge (the road may bend here).
            for (int e = -1; e <= 1; e++)
            {
                Vector3 p = at + tangent * (1.1f * e);
                (double ax, double az) = endless.ToAbsolute(p);
                RoadPoint rp = g.Road(g.NearestS(ax, az, endless.S));
                double d = System.Math.Sqrt((rp.X - ax) * (rp.X - ax) + (rp.Z - az) * (rp.Z - az));
                if (d < keep - 0.3) return false;
            }

            if (surface == null || !surface.HasGround) return false;
            float h0 = surface.GroundHeight(at), h1 = surface.GroundHeight(at + tangent * 1.2f), h2 = surface.GroundHeight(at - tangent * 1.2f);
            if (float.IsNaN(h0) || Mathf.Abs(h1 - h2) > 0.6f || Mathf.Abs(h0 - (h1 + h2) * 0.5f) > 0.25f) return false;   // ready ground: gentle
            float water = surface.WaterHeight(at);
            if (water > h0 + 0.02f) return false;   // standing water (a negative or NaN height means none)
            y = h0;

            // Clear of footprints, props and trees: no collider but the ground in the horse's box.
            Quaternion rot = Quaternion.LookRotation(tangent, Vector3.up);
            int n = Physics.OverlapBoxNonAlloc(at + Vector3.up * 1.1f, new Vector3(0.7f, 0.8f, 1.5f), overlap, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = overlap[i];
                if (c == null || c is TerrainCollider || c.transform.IsChildOf(transform)) continue;
                return false;
            }

            return true;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
