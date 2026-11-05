using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.People
{
    /// <summary>
    /// The people of the endless road's inns and villages (2026-10-05), the thin Unity side of <see cref="PeoplePlanner"/> and
    /// <see cref="VillagerBrain"/>: every half second it asks the generator for the settlements within <see cref="CastRadius"/> of the
    /// traveller and plans their casts on a worker thread (pure, from the cached dressings), keeps their brains stepping while the place
    /// is near, and shows the nearest <see cref="MaxActive"/> people within <see cref="ShowWithin"/> m on built ground as pooled
    /// characters from <c>Resources/PeoplePalette</c>; animators pause beyond <see cref="AnimateWithin"/> m. Positions are absolute
    /// (through the floating origin; <see cref="IOriginShiftable"/>). After the first fill nobody appears closer than
    /// <see cref="NoPopWithin"/> m except at their own door. Off when the safety ladder sheds the animals. Execution order −31 (after
    /// the wildlife, before the interactor).
    /// </summary>
    [DefaultExecutionOrder(-31)]
    public sealed class PeopleManager : MonoBehaviour, IOriginShiftable
    {
        /// <summary>People are shown within this distance of the traveller, metres.</summary>
        public const float ShowWithin = PeopleCatalog.ShowWithin;

        /// <summary>Shown people are released beyond this distance, metres.</summary>
        public const float ReleaseBeyond = 230f;

        /// <summary>Animators run within this distance, metres.</summary>
        public const float AnimateWithin = PeopleCatalog.AnimateWithin;

        /// <summary>After the first fill, nobody appears this close except at their own door, metres.</summary>
        public const float NoPopWithin = 110f;

        /// <summary>Most people shown at once.</summary>
        public const int MaxActive = PeopleCatalog.MaxActive;

        /// <summary>Settlements within this distance (planar, metres) have their casts planned and stepped.</summary>
        public const double CastRadius = 420.0;

        private sealed class Person
        {
            public VillagerBrain Brain;
            public Villager View;
            public bool Shown;
            public bool WasIndoors;
            public float Distance;
        }

        private sealed class CastState
        {
            public Task<PlaceCast> Task;
            public PlaceCast Cast;
            public readonly List<Person> People = new List<Person>();
            public double X, Z;
        }

        private readonly Dictionary<int, CastState> casts = new Dictionary<int, CastState>();
        private readonly Dictionary<GameObject, Stack<Villager>> pool = new Dictionary<GameObject, Stack<Villager>>();
        private readonly List<Person> candidates = new List<Person>();
        private readonly List<Waypoint> waypoints = new List<Waypoint>();
        private readonly HashSet<int> wantedScratch = new HashSet<int>();
        private readonly List<int> dropScratch = new List<int>();
        private PeoplePalette palette;
        private EndlessWorld endless;
        private EndlessGenerator generator;
        private Transform traveller;
        private Transform root;
        private Transform poolRoot;
        private float nextPlan;
        private float fillUntil;

        // New characters created per frame: a skinned character with its Animator costs milliseconds (more the first time a
        // prefab is used), and a place's whole cast can come into reach in one frame; the rest show over the next frames.
        private const int InstantiationsPerFrame = 2;
        private int instantiatedThisFrame;
        private static readonly int CostSpawn = Review.FrameCost.Register("PeopleManager.Instantiate");
        private double lastX = double.NaN, lastZ = double.NaN;
        private int shownCount;
        private static bool paletteErrorLogged;

        /// <summary>The manager in the scene, if any.</summary>
        public static PeopleManager Current { get; private set; }

        /// <summary>People shown now.</summary>
        public int ShownCount => shownCount;

        /// <summary>Places whose casts are loaded.</summary>
        public int CastCount => casts.Count;

        /// <summary>Creates the manager if the scene has none (play mode, endless road only).</summary>
        public static void Ensure()
        {
            if (Current != null || !Application.isPlaying) return;
            new GameObject("People").AddComponent<PeopleManager>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            paletteErrorLogged = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>No scene change needed: the manager is created in Play mode whenever a scene with the endless road loads.</summary>
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
            transform.position = Vector3.zero;
            root = new GameObject("Villagers").transform;
            root.SetParent(transform, false);
            var pooled = new GameObject("Pool");
            pooled.SetActive(false);
            pooled.transform.SetParent(transform, false);
            poolRoot = pooled.transform;
            palette = Resources.Load<PeoplePalette>(PeoplePalette.ResourcePath);
            if (palette == null && !paletteErrorLogged)
            {
                paletteErrorLogged = true;
                Debug.LogWarning("People: there is no PeoplePalette in a Resources folder, so the villages have no people. Run MedievalZen/People/Build People Palette " +
                                 "(it also runs by itself when the editor loads).", this);
            }
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private bool Resolve()
        {
            EndlessWorld e = EndlessWorld.Current;
            if (e == null || e.Generator == null) return false;
            if (!ReferenceEquals(endless, e) || !ReferenceEquals(generator, e.Generator))
            {
                ClearAll();
                endless = e;
                generator = e.Generator as EndlessGenerator;
            }

            if (generator == null) return false;
            if (traveller == null)
            {
                PlayerController player = FindFirstObjectByType<PlayerController>();
                if (player == null) return false;
                traveller = player.transform;
            }

            return true;
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("PeopleManager");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (palette == null || !Resolve()) return;
            if (!endless.Features.animals)
            {
                if (casts.Count > 0) ClearAll();
                return;
            }

            (double tx, double tz) = endless.ToAbsolute(traveller.position);
            if (Time.unscaledTime >= nextPlan)
            {
                nextPlan = Time.unscaledTime + 0.5f;
                PlanCasts(tx, tz);
            }

            float hour = DayCycle.Current != null ? DayCycle.Current.Hour : 12f;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            candidates.Clear();
            foreach (CastState state in casts.Values)
            {
                if (state.Cast == null) continue;
                foreach (Person p in state.People)
                {
                    p.Brain.Step(dt, hour, tx, tz);
                    double dx = p.Brain.X - tx, dz = p.Brain.Z - tz;
                    p.Distance = (float)Math.Sqrt(dx * dx + dz * dz);
                    candidates.Add(p);
                }
            }

            Show(dt, tx, tz);
        }

        // ------------------------------------------------------------------ casts

        private void PlanCasts(double tx, double tz)
        {
            // A jump (a fast journey, a new start) refills without the no-pop rule for a moment.
            if (double.IsNaN(lastX) || (tx - lastX) * (tx - lastX) + (tz - lastZ) * (tz - lastZ) > 150.0 * 150.0) fillUntil = Time.unscaledTime + 3f;
            lastX = tx;
            lastZ = tz;

            double s = endless.S;
            waypoints.Clear();
            generator.Waypoints(s - CastRadius - 200.0, s + CastRadius + 200.0, waypoints);
            HashSet<int> wanted = wantedScratch;   // third performance pass: reused, no garbage twice a second
            wanted.Clear();
            foreach (Waypoint w in waypoints)
            {
                if (w == null || !w.IsSettlement) continue;
                double dx = w.X - tx, dz = w.Z - tz;
                if (dx * dx + dz * dz > CastRadius * CastRadius) continue;
                wanted.Add(w.Id);
                if (casts.ContainsKey(w.Id)) continue;
                EndlessGenerator g = generator;
                Waypoint captured = w;
                casts[w.Id] = new CastState { X = w.X, Z = w.Z, Task = Task.Run(() => PeopleSource.CastFor(g, captured)) };
            }

            List<int> drop = dropScratch;
            drop.Clear();
            foreach (KeyValuePair<int, CastState> pair in casts)
            {
                CastState state = pair.Value;
                if (state.Cast == null && state.Task != null && state.Task.IsCompleted)
                {
                    if (state.Task.Status == TaskStatus.RanToCompletion && state.Task.Result != null)
                    {
                        state.Cast = state.Task.Result;
                        foreach (PersonPlan plan in state.Cast.People)
                        {
                            if (plan.Stops == null || plan.Stops.Length == 0 || palette.Find(plan.Character) == null) continue;
                            state.People.Add(new Person { Brain = new VillagerBrain(plan) });
                        }
                    }
                    else if (state.Task.IsFaulted)
                    {
                        Debug.LogWarning($"People: planning the people of place {pair.Key} failed: {state.Task.Exception?.GetBaseException().Message}");
                        state.Cast = new PlaceCast { PlaceId = pair.Key };
                    }
                }

                if (!wanted.Contains(pair.Key)) drop.Add(pair.Key);
            }

            foreach (int id in drop)
            {
                foreach (Person p in casts[id].People) Hide(p);
                casts.Remove(id);
            }
        }

        // ------------------------------------------------------------------ showing

        private void Show(float dt, double tx, double tz)
        {
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            IWorldSurface surface = GameWorld.Current != null ? GameWorld.Current.Surface : null;
            bool filling = Time.unscaledTime < fillUntil;
            int shown = 0;
            instantiatedThisFrame = 0;
            double ox = tx - traveller.position.x, oz = tz - traveller.position.z;   // the origin, absolute
            foreach (Person p in candidates)
            {
                bool indoors = p.Brain.Indoors;
                bool leftDoor = p.WasIndoors && !indoors;
                p.WasIndoors = indoors;
                bool want = !indoors && shown < MaxActive && p.Distance < (p.Shown ? ReleaseBeyond : ShowWithin);
                if (want && !p.Shown && !filling && !leftDoor && p.Distance < NoPopWithin) want = false;
                Vector3 scene = new Vector3((float)(p.Brain.X - ox), 0f, (float)(p.Brain.Z - oz));
                if (want && !p.Shown && (surface == null || !surface.HasGround || !endless.GroundReadyAt(scene, 2f))) want = false;
                if (!want)
                {
                    if (p.Shown) Hide(p);
                    continue;
                }

                if (!p.Shown && !Spawn(p))
                {
                    if (leftDoor) p.WasIndoors = true;   // deferred to a later frame: still "just left the door" then (no-pop rule)
                    continue;
                }

                shown++;
                float ground = surface != null ? surface.GroundHeight(scene) : 0f;
                p.View.Apply(ox, oz, ground, p.Distance < AnimateWithin, dt);
            }

            shownCount = shown;
        }

        private bool Spawn(Person p)
        {
            PeoplePalette.Entry entry = palette.Find(p.Brain.Plan.Character);
            if (entry == null || entry.prefab == null) return false;
            if (!pool.TryGetValue(entry.prefab, out Stack<Villager> stack))
            {
                stack = new Stack<Villager>();
                pool[entry.prefab] = stack;
            }

            Villager view = stack.Count > 0 ? stack.Pop() : null;
            if (view == null)
            {
                if (instantiatedThisFrame >= InstantiationsPerFrame) return false;   // spread the cost: shown on a later frame
                instantiatedThisFrame++;
                using Review.FrameCost.Scope cost = Review.FrameCost.Measure(CostSpawn);
                Review.FrameCost.FirstUse("Instantiate " + entry.prefab.name);
                GameObject go = Instantiate(entry.prefab, poolRoot);
                go.name = entry.name;
                view = go.GetComponent<Villager>();
                if (view == null) view = go.AddComponent<Villager>();
            }

            view.transform.SetParent(root, false);
            view.Bind(p.Brain, entry);
            view.gameObject.SetActive(true);
            p.View = view;
            p.Shown = true;
            return true;
        }

        private void Hide(Person p)
        {
            if (!p.Shown || p.View == null)
            {
                p.Shown = false;
                return;
            }

            Villager view = p.View;
            PeoplePalette.Entry entry = view.Entry;
            view.Unbind();
            view.transform.SetParent(poolRoot, false);
            if (entry != null && entry.prefab != null && pool.TryGetValue(entry.prefab, out Stack<Villager> stack)) stack.Push(view);
            else Destroy(view.gameObject);
            p.View = null;
            p.Shown = false;
        }

        // ------------------------------------------------------------------ arrival warm-up (2026-10-09)

        private int warmNext;
        private Villager warmView;
        private GameObject warmPrefab;

        /// <summary>True once the palette is loaded (Awake) and until every character has been made once (and the last one parked).</summary>
        public bool WarmPending => palette != null && (warmNext < palette.Entries.Count || warmView != null);

        /// <summary>Characters in the palette (what the warm-up makes, one each).</summary>
        public int WarmCount => palette != null ? palette.Entries.Count : 0;

        /// <summary>
        /// One step of the arrival warm-up, behind a hold's black screen: parks the character shown last frame in the pool, then makes
        /// the next palette character at <paramref name="at"/>, shown and animated for this frame (its meshes, materials and
        /// Animator controller load, its Animator initialises, its shaders meet the GPU). False when there was nothing left to make.
        /// </summary>
        public bool WarmStep(Vector3 at)
        {
            ParkWarm();
            if (palette == null || root == null) return false;
            while (warmNext < palette.Entries.Count)
            {
                PeoplePalette.Entry entry = palette.Entries[warmNext++];
                if (entry == null || entry.prefab == null) continue;
                if (pool.TryGetValue(entry.prefab, out Stack<Villager> stack) && stack.Count > 0) continue;
                using Review.FrameCost.Scope cost = Review.FrameCost.Measure(CostSpawn);
                Review.FrameCost.FirstUse("Instantiate " + entry.prefab.name);
                GameObject go = Instantiate(entry.prefab, at, Quaternion.identity, root);
                go.name = entry.name;
                Villager view = go.GetComponent<Villager>();
                if (view == null) view = go.AddComponent<Villager>();
                view.Bind(null, entry);
                go.SetActive(true);
                warmView = view;
                warmPrefab = entry.prefab;
                return true;
            }

            return false;
        }

        /// <summary>Parks the character the warm-up showed (the end of a hold).</summary>
        public void EndWarm() => ParkWarm();

        private void ParkWarm()
        {
            if (warmView == null)
            {
                warmPrefab = null;
                return;
            }

            warmView.Unbind();
            warmView.transform.SetParent(poolRoot, false);
            if (!pool.TryGetValue(warmPrefab, out Stack<Villager> stack))
            {
                stack = new Stack<Villager>();
                pool[warmPrefab] = stack;
            }

            stack.Push(warmView);
            warmView = null;
            warmPrefab = null;
        }

        private void ClearAll()
        {
            foreach (CastState state in casts.Values)
            {
                foreach (Person p in state.People) Hide(p);
            }

            casts.Clear();
            lastX = double.NaN;
        }

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            // Positions are absolute; only the shown transforms move now so nothing lags a frame.
            foreach (CastState state in casts.Values)
            {
                foreach (Person p in state.People)
                {
                    if (p.Shown && p.View != null) p.View.transform.position += delta;
                }
            }
        }

        /// <summary>One line for an overlay: places, people shown.</summary>
        public string OverlayLine() => $"People: {casts.Count} places, {shownCount} shown (cap {MaxActive})";
    }
}
