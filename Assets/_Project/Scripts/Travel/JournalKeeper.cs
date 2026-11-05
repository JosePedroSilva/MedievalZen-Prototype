using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.UI;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Keeps the traveller's journal on the endless road: a few times a second it shows <see cref="JournalRecorder"/> where the
    /// traveller is and the places round them (read only from the generator's warmed waypoints), feeds it auto-travel arrivals,
    /// rests on a seat and nights at an inn, and saves the journal of the current road seed to PlayerPrefs when an entry is
    /// added. Nothing is recorded while a review tour or benchmark runs, during a streaming hold or a scripted move, or in the
    /// settling ticks after a jump. Creates itself in Play mode on scenes with an <see cref="EndlessWorld"/>, with the book
    /// (<see cref="JournalBook"/>, J) on the same object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JournalKeeper : MonoBehaviour
    {
        /// <summary>PlayerPrefs flag: the "J  Journal" hint has been shown once.</summary>
        public const string HintPrefsKey = "MedievalZen.JournalHintShown";

        private const float TickSeconds = 0.25f;
        private const double NearReach = 2500.0;
        private const double RefetchStep = 300.0;
        private const float RestSeconds = 40f;

        private EndlessWorld world;
        private PlayerController controller;
        private PlayerSeat seat;
        private PlayerTravel travel;
        private Journal journal;
        private JournalRecorder recorder;
        private readonly List<Waypoint> near = new List<Waypoint>();
        private readonly List<Waypoint> fetch = new List<Waypoint>();
        private readonly Dictionary<int, string> landmarkKinds = new Dictionary<int, string>();
        private double fetchedAt = double.NaN;
        private float nextTick;
        private bool restWritten;
        private bool dirty;
        private int savedWalkedKm;

        /// <summary>The keeper of the running scene, or null.</summary>
        public static JournalKeeper Current { get; private set; }

        /// <summary>The journal of the current road (null before the world is ready).</summary>
        public Journal Journal => journal;

        /// <summary>Raised after an entry is added (and saved).</summary>
        public event System.Action<JournalEntry> EntryAdded;

        /// <summary>Creates the keeper (and its book) if there is none; Play mode only.</summary>
        public static void Ensure()
        {
            if (Current != null || !Application.isPlaying) return;
            var go = new GameObject("Journal");
            go.AddComponent<JournalKeeper>();
            go.AddComponent<JournalBook>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>No scene change needed: the keeper is created in Play mode whenever a scene with the endless road loads.</summary>
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
        }

        private void OnEnable()
        {
            InnRest.Rested += OnInnRested;
            controller = null;   // Bind finds the player again and subscribes to its arrivals
        }

        private void OnDisable()
        {
            InnRest.Rested -= OnInnRested;
            if (travel != null) travel.Arrived -= OnArrived;
            travel = null;
            Save(true);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void OnApplicationQuit() => Save(true);

        private void Bind()
        {
            if (world == null) world = EndlessWorld.Current;
            if (controller == null)
            {
                controller = FindFirstObjectByType<PlayerController>();
                if (controller != null)
                {
                    seat = controller.GetComponent<PlayerSeat>();
                    travel = controller.GetComponent<PlayerTravel>();
                    if (travel != null) travel.Arrived += OnArrived;
                }
            }
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("JournalKeeper");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickSeconds;
            Bind();
            if (world == null || world.Generator == null || controller == null) return;

            // One journal per road: a new journey (new seed) starts a new one, an old seed's comes back.
            if (journal == null || journal.Seed != world.Seed) Open(world.Seed);

            JournalTick tick = MakeTick();
            if (!tick.Suppressed) RefreshNear(tick.S);
            if (double.IsNaN(fetchedAt)) tick.Suppressed = true;   // no places yet: wait for the warmer (the setting out names the inn it starts at)
            recorder.Tick(tick, near);
            WatchSeat(tick);
            if ((int)(journal.Walked / 1000.0) != savedWalkedKm) dirty = true;   // saved with the next entry or on leaving
        }

        private JournalTick MakeTick()
        {
            DayCycle day = DayCycle.Current;
            WeatherSystem weather = WeatherSystem.Current;
            (double x, double z) = world.ToAbsolute(controller.transform.position);
            RegionInfo region = world.CurrentRegion;
            return new JournalTick
            {
                S = world.S,
                X = x,
                Z = z,
                Day = day != null ? day.Day : 1,
                Hour = day != null ? day.Hour : 12f,
                Weather = weather != null ? (int)weather.State : -1,
                RegionIndex = region.Index,
                RegionName = world.RegionName,
                Capture = Sound.SoundMix.CaptureRunning,
                Suppressed = world.IsHolding || world.IsBusy || string.IsNullOrEmpty(world.RegionName),
            };
        }

        /// <summary>The waypoints round the traveller, from regions the world's worker has already built (never cold on the main thread).</summary>
        private void RefreshNear(double s)
        {
            if (!double.IsNaN(fetchedAt) && System.Math.Abs(s - fetchedAt) < RefetchStep) return;
            IEndlessGenerator generator = world.Generator;
            if (!InnFinder.Ready(generator, s, NearReach)) return;   // keep the last list until the warmer has caught up
            fetch.Clear();
            generator.Waypoints(s - NearReach, s + NearReach, fetch);
            near.Clear();
            near.AddRange(fetch);
            fetchedAt = s;
        }

        private void Open(int seed)
        {
            if (journal != null) Save(true);
            string json = PlayerPrefs.GetString(Journal.PrefsKeyFor(seed), string.Empty);
            journal = Journal.LoadOrNew(seed, json);
            recorder = new JournalRecorder(journal) { LandmarkKind = LandmarkKindOf };
            recorder.Added += OnAdded;
            near.Clear();
            fetchedAt = double.NaN;
            landmarkKinds.Clear();
            savedWalkedKm = (int)(journal.Walked / 1000.0);
            dirty = false;
        }

        private void OnAdded(JournalEntry entry)
        {
            dirty = true;
            Save(false);
            if (PlayerPrefs.GetInt(HintPrefsKey, 0) == 0)
            {
                HudController hud = HudController.Instance;
                if (hud != null)
                {
                    hud.ShowToast("J  Journal");
                    PlayerPrefs.SetInt(HintPrefsKey, 1);
                }
            }

            EntryAdded?.Invoke(entry);
        }

        private void Save(bool flush)
        {
            if (journal == null || !dirty) return;
            PlayerPrefs.SetString(Journal.PrefsKeyFor(journal.Seed), journal.ToJson());
            if (flush) PlayerPrefs.Save();
            savedWalkedKm = (int)(journal.Walked / 1000.0);
            dirty = false;
        }

        /// <summary>"windmill", "chapel" ... from the landmark planner (its regions are cached once their waypoints are).</summary>
        private string LandmarkKindOf(Waypoint w)
        {
            if (landmarkKinds.TryGetValue(w.Id, out string word)) return word;
            word = null;
            if (world != null && world.Generator is EndlessGenerator g && g.WaypointsCached(w.S - 1.0, w.S + 1.0))
            {
                foreach (LandmarkSite site in g.Landmarks.Of(g.Region(w.S).Index))
                {
                    if (site.Id == w.Id)
                    {
                        word = LandmarkWord(site.Type);
                        break;
                    }
                }
            }

            landmarkKinds[w.Id] = word;
            return word;
        }

        /// <summary>The journal's word for a landmark type.</summary>
        public static string LandmarkWord(LandmarkType type)
        {
            switch (type)
            {
                case LandmarkType.Tower: return "watchtower";
                case LandmarkType.Chapel: return "chapel";
                case LandmarkType.Mill: return "windmill";
                case LandmarkType.Stones: return "standing stones";
                case LandmarkType.Ruin: return "ruin";
                default: return null;
            }
        }

        // ------------------------------------------------------------------ arrivals, seats, nights

        private void OnArrived(Settlement settlement)
        {
            if (settlement == null || recorder == null || world == null || controller == null) return;
            for (int i = 0; i < near.Count; i++)
            {
                Waypoint w = near[i];
                if (w != null && w.Name == settlement.Name)
                {
                    recorder.Arrive(w, MakeTick());
                    return;
                }
            }
        }

        private void WatchSeat(in JournalTick tick)
        {
            if (seat == null) return;
            if (!seat.IsActive)
            {
                restWritten = false;
                return;
            }

            if (restWritten || !seat.IsSeated || seat.SeatedTime < RestSeconds) return;
            restWritten = true;
            SeatPlan plan = seat.Plan;
            recorder.Rest(SeatPhrase(plan.Kind, plan.Prop != null ? plan.Prop.name : null), SettlementAround(tick), tick);
        }

        private string SettlementAround(in JournalTick tick)
        {
            for (int i = 0; i < near.Count; i++)
            {
                Waypoint w = near[i];
                if (w == null || !w.IsSettlement) continue;
                double dx = w.X - tick.X, dz = w.Z - tick.Z;
                if (dx * dx + dz * dz <= (w.Radius + 20.0) * (w.Radius + 20.0)) return w.Name;
            }

            return null;
        }

        /// <summary>"on a log", "on a bench", "by a fire" ... for the seat sat on.</summary>
        public static string SeatPhrase(SeatKind kind, string propName)
        {
            switch (kind)
            {
                case SeatKind.Ground: return "on the grass";
                case SeatKind.Log: return "on a log";
                case SeatKind.Stump: return "on a stump";
                case SeatKind.Crate: return "on a crate";
                case SeatKind.Chair:
                    return propName != null && propName.IndexOf("Bench", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "on a bench" : "on a chair";
                case SeatKind.Rock: return "on a rock";
                case SeatKind.Fire: return "by a fire";
                case SeatKind.Doorstep: return "on a doorstep";
                default: return "by the road";
            }
        }

        private void OnInnRested(string innName)
        {
            if (recorder == null || world == null || controller == null) return;
            JournalTick tick = MakeTick();
            tick.Suppressed = false;   // the rest itself holds the controller, not the world
            recorder.Night(string.IsNullOrEmpty(innName) ? null : innName, tick);
        }
    }
}
