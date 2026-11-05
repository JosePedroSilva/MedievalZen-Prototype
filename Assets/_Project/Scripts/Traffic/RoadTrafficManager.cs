using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Environment;
using MedievalZen.People;
using MedievalZen.Player;
using MedievalZen.Wildlife;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Traffic
{
    /// <summary>
    /// Other people on the endless road (2026-10-06), the thin Unity side of <see cref="RoadTrafficPlan"/> and <see cref="TrafficBrain"/>:
    /// asks the plan twice a second who is within 300 m of the traveller (from warmed waypoints only), gives bodies to the nearest under
    /// <see cref="TrafficSelector"/>'s caps on built ground, and places them every frame from absolute coordinates through the floating
    /// origin, on the ground or a bridge's deck. Every body comes from a pool made during a hold (people from <c>Resources/PeoplePalette</c>,
    /// horses from the stripped <c>Resources/WildlifePalette</c> variants walked through <see cref="AnimatorBoolDriver"/>, carts from the
    /// world palette's FarmCart family without colliders), so nothing is instantiated while walking. Nothing new appears during a review
    /// capture or a streaming hold. Creates itself in Play mode on the endless road.
    /// </summary>
    [DefaultExecutionOrder(-27)]
    public sealed class RoadTrafficManager : MonoBehaviour, IOriginShiftable
    {
        /// <summary>A measured Fantasy Kingdom cart (2026-10-06, from the prefabs' wheel transforms and the convex hulls' bounds).</summary>
        private sealed class CartShape
        {
            public string Name;
            public float FrontAxleZ, RearAxleZ, Track = 0.75f, WheelRadius = 0.479f, ShaftTipZ, RearZ, SeatZ;
            public bool FourWheels;
            public float CentreZ => 0.5f * (ShaftTipZ + RearZ);
        }

        // Wheels at x Â±0.750, axle 0.479 m up; shafts (or a pole) reach 2.31 m ahead of the front axle; all 1.96 m wide.
        private static readonly CartShape[] Carts =
        {
            new CartShape { Name = "SM_Veh_Cart_03", FrontAxleZ = 0f, RearAxleZ = 0f, ShaftTipZ = 2.314f, RearZ = -0.951f, SeatZ = 0.55f },
            new CartShape { Name = "SM_Veh_Cart_04", FrontAxleZ = 0f, RearAxleZ = 0f, ShaftTipZ = 2.314f, RearZ = -0.886f, SeatZ = 0.55f },
            new CartShape { Name = "SM_Veh_Cart_01", FrontAxleZ = 0f, RearAxleZ = -1.279f, ShaftTipZ = 2.324f, RearZ = -2.164f, SeatZ = 0.45f, FourWheels = true },
            new CartShape { Name = "SM_Veh_Cart_02", FrontAxleZ = 0f, RearAxleZ = -1.279f, ShaftTipZ = 2.324f, RearZ = -2.182f, SeatZ = 0.45f, FourWheels = true },
        };

        private static readonly string[] WalkerLooks = { "SM_Chr_Peasant_Male_01", "SM_Chr_Peasant_Female_01", "SM_Chr_Merchant_01", "SM_Chr_Hermit_01", "SM_Chr_Monk_01", "SM_Chr_Nun_01", "SM_Chr_Priest_01", "SM_Chr_Peasant_Male_01" };
        private static readonly string[] FarmerLooks = { "SM_Chr_Peasant_Male_01", "SM_Chr_Peasant_Female_01", "SM_Chr_Hermit_01" };
        private static readonly string[] DriverLooks = { "SM_Chr_Peasant_Male_01", "SM_Chr_Merchant_01", "SM_Chr_Bartender_01" };
        private static readonly string[] RiderLooks = { "SM_Chr_Rider_01", "SM_Chr_Rider_01", "SM_Chr_Soldier_Male_01", "SM_Chr_Merchant_01" };
        private static readonly string[] HorseNames = { "Horse_Brown", "Horse_Grey", "Horse_Black", "Horse_Brown" };
        private static readonly (string look, int count)[] PoolLooks =
        {
            ("SM_Chr_Peasant_Male_01", 4), ("SM_Chr_Peasant_Female_01", 3), ("SM_Chr_Merchant_01", 2), ("SM_Chr_Hermit_01", 2), ("SM_Chr_Monk_01", 1),
            ("SM_Chr_Nun_01", 1), ("SM_Chr_Priest_01", 1), ("SM_Chr_Bartender_01", 1), ("SM_Chr_Rider_01", 2), ("SM_Chr_Soldier_Male_01", 1),
        };

        [SerializeField, Tooltip("Driver's seat on a cart (x across, y seat height, z along is the cart's own SeatZ + this z), metres.")]
        private Vector3 driverSeat = new Vector3(-0.35f, 0.95f, 0f);

        [SerializeField, Range(0.5f, 0.85f), Tooltip("Saddle height as a fraction of the horse's measured height (head height).")]
        private float saddleFraction = MedievalZen.Player.AstrideMath.SaddleFraction;

        [SerializeField, Tooltip("Horse centre this far behind the shaft tips, as a fraction of the horse's length.")]
        private float horseBehindTip = 0.05f;

        private sealed class PersonBody
        {
            public GameObject Go;
            public Animator Animator;
            public VillagerPose Seat;
            public RoadFigurePose Figure;
            public Light Lamp;
            public string Look;
            public bool Busy;
            public float HipHeight;
        }

        private sealed class HorseBody
        {
            public AnimalView View;
            public AnimatorBoolDriver Driver;
            public bool Busy;
            public float Length, Height, Lift;
        }

        private sealed class CartBody
        {
            public GameObject Go;
            public CartShape Shape;
            public bool Busy;
        }

        private sealed class Shown
        {
            public RoadUser User;
            public readonly TrafficBrain Brain = new TrafficBrain();
            public PersonBody A, B;
            public HorseBody Horse;
            public CartBody Cart;
            public float Yaw;
            public bool YawSet;
        }

        private static readonly int SpeedHash = Animator.StringToHash(Villager.SpeedParameter);
        private static readonly int FrameCostId = Review.FrameCost.Register("RoadTrafficManager");
        private static bool paletteWarned;

        private readonly List<PersonBody> people = new List<PersonBody>();
        private readonly List<HorseBody> horses = new List<HorseBody>();
        private readonly List<CartBody> carts = new List<CartBody>();
        private readonly Dictionary<long, Shown> shown = new Dictionary<long, Shown>();
        private readonly List<RoadUser> planned = new List<RoadUser>();
        private readonly List<RoadUser> fresh = new List<RoadUser>();
        private readonly List<TrafficBridge> bridges = new List<TrafficBridge>();
        private readonly List<TrafficCandidate> candidates = new List<TrafficCandidate>();
        private readonly List<int> chosen = new List<int>();
        private readonly HashSet<long> keep = new HashSet<long>();
        private readonly List<long> drop = new List<long>();
        private readonly TrafficQuery query = new TrafficQuery();

        private PeoplePalette peoplePalette;
        private WildlifePalette wildlifePalette;
        private EndlessWorld endless;
        private EndlessGenerator generator;
        private TrafficSource source;
        private Transform traveller, poolRoot, liveRoot;
        private bool poolBuilt;
        private float nextPlan;
        private double travellerS = double.NaN, lastTau = double.NaN;

        /// <summary>The scene's manager, or null.</summary>
        public static RoadTrafficManager Current { get; private set; }

        /// <summary>Creates the manager if the scene has none (play mode, endless road only).</summary>
        public static void Ensure()
        {
            if (Current != null || !Application.isPlaying) return;
            new GameObject("RoadTraffic").AddComponent<RoadTrafficManager>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            paletteWarned = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            if (FindFirstObjectByType<EndlessWorld>() != null) Ensure();
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (FindFirstObjectByType<EndlessWorld>() != null) Ensure();
        }

        private void Awake()
        {
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }

            Current = this;
            var pool = new GameObject("Pool");
            pool.SetActive(false);
            pool.transform.SetParent(transform, false);
            poolRoot = pool.transform;
            liveRoot = new GameObject("OnTheRoad").transform;
            liveRoot.SetParent(transform, false);
            peoplePalette = Resources.Load<PeoplePalette>(PeoplePalette.ResourcePath);
            wildlifePalette = Resources.Load<WildlifePalette>(WildlifePalette.ResourcePath);
            if ((peoplePalette == null || wildlifePalette == null) && !paletteWarned)
            {
                paletteWarned = true;
                Debug.LogWarning("Road traffic: the PeoplePalette or the WildlifePalette is missing from Resources, so the road stays empty.", this);
            }
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private bool Resolve()
        {
            EndlessWorld e = EndlessWorld.Current;
            if (e == null || !(e.Generator is EndlessGenerator g)) return false;
            if (!ReferenceEquals(endless, e) || !ReferenceEquals(generator, g))
            {
                ReleaseAll();
                endless = e;
                generator = g;
                source = new TrafficSource(g);
                planned.Clear();
                travellerS = double.NaN;
            }

            if (traveller == null)
            {
                PlayerController player = FindFirstObjectByType<PlayerController>();
                if (player == null) return false;
                traveller = player.transform;
            }

            return true;
        }

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);
            if (peoplePalette == null || wildlifePalette == null || !Resolve()) return;
            if (!poolBuilt)
            {
                if (endless.IsHolding) BuildPool();   // the start hold (or any later one): never while walking
                return;
            }

            if (!endless.Features.animals)
            {
                ReleaseAll();
                return;
            }

            (double tx, double tz) = endless.ToAbsolute(traveller.position);
            travellerS = generator.NearestS(tx, tz, travellerS);
            RoadPoint tp = generator.Road(travellerS);
            double th = tp.HeadingDeg * Math.PI / 180.0;
            float travellerLat = (float)((tx - tp.X) * Math.Cos(th) - (tz - tp.Z) * Math.Sin(th));

            DayClock clock = DayCycle.Current != null ? DayCycle.Current.Clock : null;
            double hps = clock != null && clock.HoursPerRealSecond > 0f ? clock.HoursPerRealSecond : 1.0 / 60.0;
            double tau = clock != null ? (clock.Day * 24.0 + clock.Hour) / hps : 12.0 / hps + Time.time;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float tauRate = double.IsNaN(lastTau) || dt <= 0f ? 1f : Mathf.Clamp((float)((tau - lastTau) / Math.Max(Time.deltaTime, 1e-4f)), 0f, 8f);
            if (!double.IsNaN(lastTau) && Math.Abs(tau - lastTau) > 30.0) nextPlan = 0f;   // a jump in time: ask again now
            lastTau = tau;

            if (Time.unscaledTime >= nextPlan)
            {
                nextPlan = Time.unscaledTime + 0.5f;
                query.Seed = endless.Seed;
                query.HoursPerSecond = hps;
                query.Road = source;
                fresh.Clear();
                if (RoadTrafficPlan.Query(query, tau, travellerS - 300.0, travellerS + 300.0, fresh))
                {
                    planned.Clear();
                    planned.AddRange(fresh);
                    bridges.Clear();
                    source.Bridges(travellerS - 320.0, travellerS + 320.0, bridges);
                }
            }

            Camera cam = Camera.main;
            bool quiet = endless.IsOutrunHold || endless.IsBusy || MedievalZen.Sound.SoundMix.CaptureRunning;
            IWorldSurface surface = GameWorld.Current != null ? GameWorld.Current.Surface : null;

            // Candidates: the planned users near the traveller, and every shown one (it keeps its body until it is out of sight).
            candidates.Clear();
            keep.Clear();
            foreach (RoadUser u in planned)
            {
                if (!keep.Add(u.Id)) continue;
                AddCandidate(u, tau, tx, tz, cam, quiet, surface);
            }

            foreach (Shown sh in shown.Values)
            {
                if (keep.Add(sh.User.Id)) AddCandidate(sh.User, tau, tx, tz, cam, quiet, surface);
            }

            TrafficSelector.Select(candidates, chosen);
            keep.Clear();
            foreach (int i in chosen) keep.Add(candidates[i].User.Id);
            drop.Clear();
            foreach (KeyValuePair<long, Shown> kv in shown)
            {
                if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            }

            foreach (long id in drop) Release(id);
            foreach (int i in chosen)
            {
                RoadUser u = candidates[i].User;
                if (!shown.ContainsKey(u.Id)) Bind(u, tau);
            }

            Vector3 travellerScene = traveller.position;
            foreach (Shown sh in shown.Values) Place(sh, dt, tau, tauRate, travellerLat, travellerScene, surface);
        }

        private void AddCandidate(RoadUser u, double tau, double tx, double tz, Camera cam, bool quiet, IWorldSurface surface)
        {
            TrafficPhase phase = u.Sample(tau, out double s, out double lat);
            RoadPoint p = generator.Road(s);
            double h = p.HeadingDeg * Math.PI / 180.0, rl = lat * u.Dir;
            double x = p.X + Math.Cos(h) * rl, z = p.Z - Math.Sin(h) * rl;
            float d = (float)Math.Sqrt((x - tx) * (x - tx) + (z - tz) * (z - tz));
            bool isShown = shown.TryGetValue(u.Id, out Shown sh);
            Vector3 scene = endless.ToScene(x, p.Height, z);
            bool inView = cam != null && InView(cam, scene);
            if (isShown && phase == TrafficPhase.Absent && (!inView || d > 90f)) d = float.MaxValue;   // arrived and out of sight: released
            bool may = !quiet && phase != TrafficPhase.Absent && surface != null && surface.HasGround && endless.GroundReadyAt(scene, 2f)
                       && !(phase == TrafficPhase.Joining && inView && d < 90f);   // nobody appears out of thin air under the traveller's eyes
            candidates.Add(new TrafficCandidate { User = u, Distance = d, Shown = isShown, MaySpawn = may });
        }

        private static bool InView(Camera cam, Vector3 p)
        {
            Vector3 v = cam.WorldToViewportPoint(p + Vector3.up);
            return v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.2f && v.y < 1.2f;
        }

        // ------------------------------------------------------------------ bodies

        private void Bind(RoadUser u, double tau)
        {
            var sh = new Shown { User = u };
            string[] looks = u.Kind == RoadUserKind.Farmer ? FarmerLooks : u.Kind == RoadUserKind.Cart ? DriverLooks : u.Kind == RoadUserKind.Rider ? RiderLooks : WalkerLooks;
            sh.A = TakePerson(looks[u.Look % looks.Length]);
            if (sh.A == null) return;
            if (u.Kind == RoadUserKind.Pair)
            {
                sh.B = TakePerson(WalkerLooks[(u.Look / 7 + 1) % WalkerLooks.Length]);
                if (sh.B == null)
                {
                    FreePerson(sh.A);
                    return;
                }
            }

            if (u.IsVehicle)
            {
                sh.Horse = TakeHorse(u.Look / 13);
                if (sh.Horse == null)
                {
                    FreePerson(sh.A);
                    return;
                }

                if (u.Kind == RoadUserKind.Cart)
                {
                    sh.Cart = TakeCart(u.Look / 29);
                    if (sh.Cart == null)
                    {
                        FreePerson(sh.A);
                        sh.Horse.Busy = false;
                        sh.Horse.View.Root.transform.SetParent(poolRoot, false);
                        return;
                    }
                }
            }

            sh.Brain.Reset(u, tau);
            if (sh.A.Lamp != null) sh.A.Lamp.enabled = u.Kind == RoadUserKind.Lantern;
            shown[u.Id] = sh;
        }

        private void Release(long id)
        {
            if (!shown.TryGetValue(id, out Shown sh)) return;
            FreePerson(sh.A);
            FreePerson(sh.B);
            if (sh.Horse != null)
            {
                sh.Horse.Busy = false;
                sh.Horse.View.Root.transform.SetParent(poolRoot, false);
            }

            if (sh.Cart != null)
            {
                sh.Cart.Busy = false;
                sh.Cart.Go.transform.SetParent(poolRoot, false);
            }

            if (PointsOfInterest.Current != null) PointsOfInterest.Current.Remove(PoiId(id));
            shown.Remove(id);
        }

        private void ReleaseAll()
        {
            drop.Clear();
            drop.AddRange(shown.Keys);
            foreach (long id in drop) Release(id);
        }

        private PersonBody TakePerson(string look)
        {
            PersonBody any = null;
            foreach (PersonBody p in people)
            {
                if (p.Busy) continue;
                if (p.Look == look)
                {
                    any = p;
                    break;
                }

                if (any == null) any = p;
            }

            if (any == null) return null;
            any.Busy = true;
            any.Go.transform.SetParent(liveRoot, false);
            return any;
        }

        private void FreePerson(PersonBody p)
        {
            if (p == null) return;
            p.Busy = false;
            if (p.Seat != null) p.Seat.Weight = 0f;
            p.Figure.Look = 0f;
            p.Figure.NodTime = -1f;
            p.Figure.Astride = 0f;
            if (p.Lamp != null) p.Lamp.enabled = false;
            p.Go.transform.SetParent(poolRoot, false);
        }

        private HorseBody TakeHorse(int pick)
        {
            for (int k = 0; k < horses.Count; k++)
            {
                HorseBody h = horses[(pick + k) % horses.Count];
                if (h.Busy) continue;
                h.Busy = true;
                h.View.Root.transform.SetParent(liveRoot, false);   // active first: the Animator is reset live
                h.View.ResetAnimator();
                h.Driver.Reset(h.View.Info);
                h.Driver.Wanted = MedievalZen.Wildlife.Pose.Walk;
                return h;
            }

            return null;
        }

        private CartBody TakeCart(int pick)
        {
            for (int k = 0; k < carts.Count; k++)
            {
                CartBody c = carts[(pick + k) % carts.Count];
                if (c.Busy) continue;
                c.Busy = true;
                c.Go.transform.SetParent(liveRoot, false);
                return c;
            }

            return null;
        }

        private void BuildPool()
        {
            poolBuilt = true;
            foreach ((string look, int count) in PoolLooks)
            {
                PeoplePalette.Entry e = peoplePalette.Find(look);
                if (e == null) continue;
                for (int i = 0; i < count; i++)
                {
                    GameObject go = Instantiate(e.prefab, poolRoot);
                    go.name = "Traveller_" + look;
                    var body = new PersonBody { Go = go, Look = look, Animator = go.GetComponentInChildren<Animator>(), HipHeight = e.hipHeight > 0.3f ? e.hipHeight : 0.9f };
                    if (body.Animator != null)
                    {
                        body.Animator.applyRootMotion = false;
                        body.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    }

                    // The palette's characters already carry a VillagerPose, and the component allows only one: adding a second
                    // returns null (the pool threw here on every start and the road stayed empty, first run 2026-10-06).
                    body.Seat = go.GetComponent<VillagerPose>();
                    if (body.Seat == null) body.Seat = go.AddComponent<VillagerPose>();
                    if (body.Seat != null) body.Seat.StandingHipHeight = body.HipHeight;
                    body.Figure = go.GetComponent<RoadFigurePose>();
                    if (body.Figure == null) body.Figure = go.AddComponent<RoadFigurePose>();
                    var lamp = new GameObject("Lantern").AddComponent<Light>();
                    lamp.transform.SetParent(go.transform, false);
                    lamp.transform.localPosition = new Vector3(0.32f, 0.95f, 0.2f);
                    lamp.type = LightType.Point;
                    lamp.range = 7f;
                    lamp.intensity = 1.4f;
                    lamp.color = new Color(1f, 0.72f, 0.4f);
                    lamp.shadows = LightShadows.None;
                    lamp.enabled = false;
                    body.Lamp = lamp;
                    people.Add(body);
                }
            }

            SpeciesInfo horse = SpeciesCatalog.Get(Species.Horse);
            foreach (string name in HorseNames)
            {
                WildlifePalette.Entry e = wildlifePalette.Find(name);
                if (e == null || e.prefab == null) continue;
                GameObject go = Instantiate(e.prefab, poolRoot);
                go.name = "RoadHorse_" + name;
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                var view = new AnimalView(name, go, horse, e, out _);
                float scale = wildlifePalette.ScaleFor(horse, e);
                view.Scale = scale;
                go.transform.localScale = Vector3.one * scale;
                horses.Add(new HorseBody { View = view, Driver = new AnimatorBoolDriver(horse), Length = Mathf.Max(1.6f, e.length * scale), Height = Mathf.Max(1.2f, e.height * scale), Lift = -e.footOffset * scale });
            }

            WorldPalette world = endless.Palette;
            PrefabFamily family = world != null ? world.FindFamily(PropRole.FarmCart) : null;
            if (family != null)
            {
                foreach (CartShape shape in Carts)
                {
                    foreach (GameObject prefab in family.prefabs)
                    {
                        if (prefab == null || prefab.name != shape.Name) continue;
                        GameObject go = Instantiate(prefab, poolRoot);
                        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                        carts.Add(new CartBody { Go = go, Shape = shape });
                        break;
                    }
                }
            }

            Debug.Log($"Road traffic: pool of {people.Count} people, {horses.Count} horses, {carts.Count} carts made during the hold.");
        }

        // ------------------------------------------------------------------ placing

        private Vector3 ScenePoint(double s, double roadLat, IWorldSurface surface, out double headingRad)
        {
            RoadPoint p = generator.Road(s);
            headingRad = p.HeadingDeg * Math.PI / 180.0;
            double x = p.X + Math.Cos(headingRad) * roadLat, z = p.Z - Math.Sin(headingRad) * roadLat;
            Vector3 scene = endless.ToScene(x, p.Height, z);
            float ground = surface != null && surface.HasGround ? surface.GroundHeight(scene) : p.Height;
            scene.y = TrafficGround.Height(bridges, s, ground);
            return scene;
        }

        private float GroundAt(Vector3 scene, double s, IWorldSurface surface)
        {
            float ground = surface != null && surface.HasGround ? surface.GroundHeight(scene) : scene.y;
            return TrafficGround.Height(bridges, s, ground);
        }

        private void Place(Shown sh, float dt, double tau, float tauRate, float travellerLat, Vector3 travellerScene, IWorldSurface surface)
        {
            RoadUser u = sh.User;
            bool onBridge = TrafficGround.BridgeAt(bridges, sh.Brain.S) >= 0;
            float distance = Vector3.Distance(sh.A.Go.transform.position, travellerScene);
            double prevS = sh.Brain.S;
            float prevLat = sh.Brain.Lateral;
            sh.Brain.Step(dt * tauRate, tau, travellerS, travellerLat, onBridge, distance);
            Vector3 pos = ScenePoint(sh.Brain.S, sh.Brain.Lateral * u.Dir, surface, out double heading);

            // Facing from the motion (along the road, or across it on the joining and leaving paths).
            float along = (float)((sh.Brain.S - prevS) * u.Dir), across = sh.Brain.Lateral - prevLat;
            float baseYaw = (float)(heading * 180.0 / Math.PI) + (u.Dir < 0 ? 180f : 0f);
            if (along * along + across * across > 1e-6f)
            {
                float target = baseYaw + Mathf.Atan2(across, along) * Mathf.Rad2Deg;
                sh.Yaw = sh.YawSet ? Mathf.MoveTowardsAngle(sh.Yaw, target, 150f * dt) : target;
                sh.YawSet = true;
            }
            else if (!sh.YawSet)
            {
                sh.Yaw = baseYaw;
                sh.YawSet = true;
            }

            Quaternion rot = Quaternion.Euler(0f, sh.Yaw, 0f);
            bool animate = distance < TrafficSelector.AnimateWithin;
            float walk = sh.Brain.Moving ? u.Speed * tauRate : 0f;
            Vector3 lookAt = travellerScene + Vector3.up * 1.6f;

            if (u.Kind == RoadUserKind.Cart && sh.Cart != null && sh.Horse != null)
            {
                CartShape c = sh.Cart.Shape;
                Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right;
                Vector3 origin = pos - fwd * c.CentreZ;   // the plan's point is the rig's middle
                Vector3 axle = origin + fwd * c.RearAxleZ, horseAt = origin + fwd * (c.ShaftTipZ - horseBehindTip * sh.Horse.Length);
                float yAxle = GroundAt(axle, sh.Brain.S, surface), yFront = GroundAt(c.FourWheels ? origin + fwd * c.FrontAxleZ : horseAt, sh.Brain.S, surface);
                float run = c.FourWheels ? c.FrontAxleZ - c.RearAxleZ : c.ShaftTipZ - horseBehindTip * sh.Horse.Length - c.RearAxleZ;
                float pitch = Mathf.Atan2(yFront - yAxle, Mathf.Max(0.5f, run)) * Mathf.Rad2Deg;
                float yl = GroundAt(axle - right * c.Track, sh.Brain.S, surface), yr = GroundAt(axle + right * c.Track, sh.Brain.S, surface);
                float roll = Mathf.Atan2(yl - yr, 2f * c.Track) * Mathf.Rad2Deg;
                Quaternion cartRot = rot * Quaternion.Euler(-pitch, 0f, roll);
                axle.y = yAxle;
                Vector3 cartPos = axle - cartRot * new Vector3(0f, 0f, c.RearAxleZ);
                sh.Cart.Go.transform.SetPositionAndRotation(cartPos, cartRot);
                horseAt.y = GroundAt(horseAt, sh.Brain.S, surface);
                PlaceHorse(sh.Horse, horseAt, rot, pitch, walk, animate, dt);
                Vector3 seat = cartPos + cartRot * new Vector3(driverSeat.x, driverSeat.y - 0.45f, c.SeatZ + driverSeat.z);
                PlacePerson(sh.A, seat, cartRot, 0f, animate, 1f, 0.45f, 0f);
            }
            else if (u.Kind == RoadUserKind.Rider && sh.Horse != null)
            {
                pos.y = GroundAt(pos, sh.Brain.S, surface);
                PlaceHorse(sh.Horse, pos, rot, 0f, walk, animate, dt);
                Vector3 saddle = pos + rot * new Vector3(0f, saddleFraction * sh.Horse.Height - sh.A.HipHeight, -0.05f * sh.Horse.Length);
                PlacePerson(sh.A, saddle, rot, 0f, animate, 0f, 0.45f, 1f);
            }
            else
            {
                PlacePerson(sh.A, pos, rot, walk, animate, 0f, 0.45f, 0f);
                if (sh.B != null)
                {
                    // The pair walks side by side; on a bridge the second falls in behind.
                    Vector3 b = onBridge ? ScenePoint(sh.Brain.S - u.Dir * 0.9, sh.Brain.Lateral * u.Dir, surface, out _)
                                         : ScenePoint(sh.Brain.S - u.Dir * 0.25, (sh.Brain.Lateral + RoadTrafficPlan.PairSpacing) * u.Dir, surface, out _);
                    PlacePerson(sh.B, b, rot, walk, animate, 0f, 0.45f, 0f);
                    sh.B.Figure.LookAt = sh.A.Go.transform.position + Vector3.up * 1.6f;   // talking: they look at each other
                    sh.B.Figure.Look = sh.Brain.Look > 0.5f ? sh.Brain.Look : 0.6f;
                }
            }

            sh.A.Figure.LookAt = u.Kind == RoadUserKind.Pair && sh.Brain.Look < 0.5f && sh.B != null ? sh.B.Go.transform.position + Vector3.up * 1.6f : lookAt;
            sh.A.Figure.Look = u.Kind == RoadUserKind.Pair ? Mathf.Max(0.6f, sh.Brain.Look) : sh.Brain.Look;
            sh.A.Figure.NodTime = sh.Brain.NodTime;

            // The cinematic camera may frame a meeting ahead.
            PointsOfInterest poi = PointsOfInterest.Current;
            if (poi != null && sh.Brain.Phase == TrafficPhase.Road && distance > PointsOfInterest.MinDistance && distance < 120f)
            {
                poi.Publish(PoiId(u.Id), pos + Vector3.up, u.IsVehicle ? 5f : 3f, u.IsVehicle ? 1.2f : 0.9f, poi.Now + 1.5f);
            }
        }

        private static long PoiId(long id) => 0x7A11_0000_0000_0000L | (id & 0x0000_FFFF_FFFF_FFFFL);

        private static void PlacePerson(PersonBody p, Vector3 pos, Quaternion rot, float speed, bool animate, float sit, float seatHeight, float astride)
        {
            p.Go.transform.SetPositionAndRotation(pos, rot);
            if (p.Animator != null)
            {
                if (p.Animator.enabled != animate) p.Animator.enabled = animate;
                if (animate)
                {
                    p.Animator.SetFloat(SpeedHash, speed > 0.05f ? VillagerBrain.WalkSpeed : 0f);
                    p.Animator.speed = speed > 0.05f ? Mathf.Clamp(speed / VillagerBrain.WalkSpeed, 0.6f, 2f) : 1f;
                }
            }

            p.Seat.Weight = animate ? sit : 0f;
            p.Seat.SeatHeight = seatHeight;
            p.Figure.Astride = animate ? astride : 0f;
        }

        private static void PlaceHorse(HorseBody h, Vector3 pos, Quaternion rot, float pitch, float speed, bool animate, float dt)
        {
            h.View.Transform.SetPositionAndRotation(pos + Vector3.up * h.Lift, rot * Quaternion.Euler(-pitch, 0f, 0f));
            Animator a = h.View.Animator;
            if (a == null) return;
            if (a.enabled != animate) a.enabled = animate;
            if (!animate) return;
            h.Driver.Wanted = speed > 0.05f ? MedievalZen.Wildlife.Pose.Walk : MedievalZen.Wildlife.Pose.Idle;
            h.Driver.Update(dt, h.View.IdleObservable, h.View.IdleObservable && h.View.InIdle, h.View);
            a.speed = speed > 0.05f ? Mathf.Clamp(speed / h.View.Info.WalkClipSpeed, 0.55f, 2.2f) : 1f;
        }

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            foreach (Shown sh in shown.Values)
            {
                if (sh.A != null) sh.A.Go.transform.position += delta;
                if (sh.B != null) sh.B.Go.transform.position += delta;
                if (sh.Horse != null) sh.Horse.View.Shift(delta);
                if (sh.Cart != null) sh.Cart.Go.transform.position += delta;
            }
        }

        /// <summary>One line for the F3 overlay.</summary>
        public string OverlayLine()
        {
            int walkers = 0, vehicles = 0;
            foreach (Shown sh in shown.Values)
            {
                if (sh.User.IsVehicle) vehicles++;
                else walkers += sh.User.Members;
            }

            return $"Road traffic: {planned.Count} planned within 300 m, {walkers} walkers + {vehicles} carts/riders shown, pool {(poolBuilt ? $"{people.Count}p {horses.Count}h {carts.Count}c" : "not built (waits for a hold)")}";
        }
    }
}
