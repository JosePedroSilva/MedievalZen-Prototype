using System;
using System.Collections.Generic;
using System.Text;
using MedievalZen.Endless;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// Animals along the road, in both modes. Creates itself at runtime (<see cref="Ensure"/>, called by
    /// <see cref="EndlessWorld"/> and <see cref="GameWorld"/>) and loads <c>Resources/WildlifePalette</c>. Every half second
    /// the <see cref="WildlifeActivator"/> plans the 128 m cells around the traveller (on worker threads for the endless
    /// road) and activates the planned groups whose home is within <see cref="WildlifeActivator.ActivateRadius"/> on built
    /// ground (released beyond <see cref="WildlifeActivator.ReleaseRadius"/>), so animals appear in the haze, never near the
    /// road; hard caps of <see cref="WildlifeActivator.MaxGroups"/> groups and <see cref="WildlifeActivator.MaxAnimals"/>
    /// animals; pooled instances. The simulation (<see cref="WildlifeSim"/>) is pure; this class only feeds it, shows it and
    /// plays its calls. F6 starts an event. Implements <see cref="IOriginShiftable"/>: every scene position it holds moves
    /// with the endless road's origin (the simulation, the instances, the voices).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-32)]
    public sealed class WildlifeManager : MonoBehaviour, IOriginShiftable
    {
        /// <summary>Animators of animals further away are paused (they keep their pose).</summary>
        public const float AnimateWithin = 120f;

        /// <summary>Animals further away cast no shadows.</summary>
        public const float ShadowsWithin = 60f;

        private const float ActivationInterval = 0.5f;
        private const int InstantiationsPerFrame = 3;

        // The stall hunt's per-frame culprit: the phases of Update.
        private static readonly int CostSpawn = Review.FrameCost.Register("WildlifeManager.Instantiate");
        private static readonly int CostActivate = Review.FrameCost.Register("WildlifeManager.Activate (events, groups)");
        private static readonly int CostStep = Review.FrameCost.Register("WildlifeManager.SimStep");
        private static readonly int CostViews = Review.FrameCost.Register("WildlifeManager.SyncViews");
        private static readonly int CostSounds = Review.FrameCost.Register("WildlifeManager.PlaySounds");

        private enum Mode
        {
            Unknown,
            Endless,
            Finite,
        }

        private struct PendingCheck
        {
            public AnimalGroup Group;
            public float At;
        }

        private static bool paletteErrorLogged;
        private WildlifePalette palette;
        private Mode mode = Mode.Unknown;
        private EndlessWorld endless;
        private IEndlessGenerator generator;
        private IWildlifeHabitat habitat;
        private WildlifeActivator activator;
        private WildlifeSim sim;
        private SceneGround ground;
        private GeneratorRoad generatorRoad;
        private Transform viewRoot;
        private Transform poolRoot;
        private AnimalVoices voices;
        private float simTime;
        private float nextActivation;
        private float nextPlayerSearch;
        private bool cleared = true;
        private bool loggedActive;
        private int instantiatedThisFrame;
        private Func<float, float, float, bool> groundReady;

        private PlayerController controller;
        private PlayerMotor motor;
        private PlayerSeat seat;
        private PlayerCameraRig cameraRig;

        private readonly Dictionary<string, Stack<AnimalView>> pools = new Dictionary<string, Stack<AnimalView>>(StringComparer.Ordinal);
        private readonly List<PendingCheck> checks = new List<PendingCheck>(16);
        private readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        private readonly WildlifeMilestones milestones = new WildlifeMilestones();
        private readonly StringBuilder overlay = new StringBuilder(256);

        /// <summary>The manager in the scene, or null.</summary>
        public static WildlifeManager Current { get; private set; }

        /// <summary>The simulation (null until the mode is known).</summary>
        public WildlifeSim Sim => sim;

        /// <summary>Animals in the simulation now.</summary>
        public int AnimalCount => sim != null ? sim.Agents.Count : 0;

        /// <summary>Groups active now.</summary>
        public int GroupCount => sim != null ? sim.Groups.Count : 0;

        /// <summary>Creates the manager if the scene has none (play mode only).</summary>
        public static void Ensure()
        {
            if (Current != null || !Application.isPlaying) return;
            var go = new GameObject("Wildlife");
            go.AddComponent<WildlifeManager>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            paletteErrorLogged = false;
            PointsOfInterest.Current = null;
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
            viewRoot = new GameObject("Animals").transform;
            viewRoot.SetParent(transform, false);
            var pool = new GameObject("Pool");
            pool.SetActive(false);
            pool.transform.SetParent(transform, false);
            poolRoot = pool.transform;
            voices = new AnimalVoices(transform);
            groundReady = GroundReady;
            palette = Resources.Load<WildlifePalette>(WildlifePalette.ResourcePath);
            if (palette == null)
            {
                if (!paletteErrorLogged)
                {
                    paletteErrorLogged = true;
                    Debug.LogError("Wildlife: there is no WildlifePalette in a Resources folder, so no animals are shown. Run MedievalZen/Wildlife/Build Wildlife Palette " +
                                   "(it also runs by itself when the editor loads with the polyperfect animals imported).", this);
                }
            }
            else if (!palette.IsCurrent)
            {
                Debug.LogWarning("Wildlife: the WildlifePalette is out of date for this code; using it as it is. Rebuild it with MedievalZen/Wildlife/Build Wildlife Palette.", this);
            }
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (sim != null && PointsOfInterest.Current == sim.Points) PointsOfInterest.Current = null;
        }

        // ------------------------------------------------------------------ mode

        private bool ResolveMode()
        {
            EndlessWorld e = EndlessWorld.Current;
            if (e != null)
            {
                if (e.Generator == null || GameWorld.Current == null) return false;   // the streamer has no world yet
                if (mode == Mode.Endless && ReferenceEquals(generator, e.Generator)) return true;
                // A new journey (a new generator) starts the animals afresh.
                ResetAll();
                endless = e;
                generator = e.Generator;
                habitat = new EndlessHabitat(generator);
                generatorRoad = new GeneratorRoad(generator);
                ground = new SceneGround(GameWorld.Current.Surface, e);
                sim = new WildlifeSim(habitat.Seed, ground, generatorRoad, habitat.Corridor);
                activator = new WildlifeActivator(new WildlifePlanner(habitat), sim, true);
                mode = Mode.Endless;
                return true;
            }

            if (mode == Mode.Finite) return true;
            GameWorld world = GameWorld.Current;
            if (world == null || !world.Surface.HasGround) return false;   // asked again next frame (a world generated at Start)

            ResetAll();
            int seed = world.Settings != null ? world.Settings.seed : 12345;
            habitat = new FiniteHabitat(world, seed);
            ground = new SceneGround(world.Surface, null);
            sim = new WildlifeSim(seed, ground, new NetworkRoad(world.Roads), habitat.Corridor);
            activator = new WildlifeActivator(new WildlifePlanner(habitat), sim, false);
            mode = Mode.Finite;
            return true;
        }

        private void ResetAll()
        {
            if (sim != null)
            {
                sim.Clear();
                DrainRemoved();
            }

            activator?.Clear();
            checks.Clear();
            voices?.StopAll();
            cleared = true;
        }

        private bool FindPlayer()
        {
            if (controller != null) return true;
            if (Time.unscaledTime < nextPlayerSearch) return false;
            nextPlayerSearch = Time.unscaledTime + 2f;
            controller = FindFirstObjectByType<PlayerController>();
            if (controller == null) return false;
            motor = controller.GetComponent<PlayerMotor>();
            seat = controller.GetComponent<PlayerSeat>();
            cameraRig = controller.GetComponent<PlayerCameraRig>();
            return true;
        }

        private bool FeatureOn => mode != Mode.Endless || endless == null || endless.Features.animals;

        private bool GroundReady(float x, float z, float radius) => endless == null || endless.GroundReadyAt(new Vector3(x, 0f, z), radius);

        // ------------------------------------------------------------------ frame

        private static readonly int FrameCostId = Review.FrameCost.Register("WildlifeManager");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            instantiatedThisFrame = 0;
            if (palette == null || !ResolveMode() || sim == null || !FindPlayer()) return;

            if (!WildlifePreferences.AnimalsShown || !FeatureOn)
            {
                if (!cleared)
                {
                    sim.Clear();
                    DrainRemoved();
                    voices.StopAll();
                    activator.ForgetActive();
                    checks.Clear();
                    cleared = true;
                }

                if (PointsOfInterest.Current == sim.Points) PointsOfInterest.Current = null;
                HandleKeys(default, false);
                return;
            }

            cleared = false;
            PointsOfInterest.Current = sim.Points;
            if (generatorRoad != null && endless != null)
            {
                generatorRoad.OriginX = endless.Origin.X;
                generatorRoad.OriginZ = endless.Origin.Z;
                generatorRoad.HintS = endless.S;
            }

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            simTime += dt;
            WildlifeContext ctx = BuildContext();
            if (Time.unscaledTime >= nextActivation && !ctx.Holding)
            {
                nextActivation = Time.unscaledTime + ActivationInterval;
                using (Review.FrameCost.Measure(CostActivate))
                {
                    Activate(ctx);
                    RunChecks();
                }
            }

            using (Review.FrameCost.Measure(CostStep)) sim.Step(dt, ctx);
            using (Review.FrameCost.Measure(CostViews))
            {
                SyncViews(dt);
                DrainRemoved();
            }

            using (Review.FrameCost.Measure(CostSounds)) PlaySounds();
            WriteLog();
            HandleKeys(ctx, true);
            if (milestones.Update(sim.Agents.Count, sim.Groups.Count, out string line)) Debug.Log(line, this);
        }

        private WildlifeContext BuildContext()
        {
            Transform t = controller.transform;
            Vector3 p = t.position;
            Vector3 v = motor != null ? motor.Velocity : Vector3.zero;
            Vector3 f = endless != null ? endless.Facing() : cameraRig != null ? cameraRig.PlanarForward : t.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            f.Normalize();
            DayCycle day = DayCycle.Current;
            return new WildlifeContext
            {
                Time = simTime,
                HasTraveller = true,
                TravellerX = p.x,
                TravellerY = p.y,
                TravellerZ = p.z,
                VelocityX = v.x,
                VelocityZ = v.z,
                Speed = motor != null ? motor.PlanarSpeed : 0f,
                FacingX = f.x,
                FacingZ = f.z,
                Seated = seat != null && seat.IsSeated,
                SeatedTime = seat != null ? seat.SeatedTime : 0f,
                Hour = day != null ? day.Hour : 12f,
                Holding = endless != null && (endless.IsHolding || endless.IsBusy),
            };
        }

        // ------------------------------------------------------------------ activation

        private void Activate(in WildlifeContext ctx)
        {
            double ox = 0.0, oz = 0.0, tx = ctx.TravellerX, tz = ctx.TravellerZ;
            if (endless != null && endless.Origin != null)
            {
                ox = endless.Origin.X;
                oz = endless.Origin.Z;
                tx += ox;
                tz += oz;
            }

            float now = Time.unscaledTime;
            activator.Step(tx, tz, ox, oz, ctx, groundReady, now);
            if (activator.PlanningError != null && warned.Add("planning")) Debug.LogError("Wildlife: " + activator.PlanningError, this);
            foreach (AnimalGroup g in activator.Activated)
            {
                FixMembers(g);
                if (g.SettlementId >= 0)
                {
                    // Settlement props may still be switching on: look again in a moment.
                    checks.Add(new PendingCheck { Group = g, At = now + 2f });
                    checks.Add(new PendingCheck { Group = g, At = now + 6f });
                }

                if (loggedActive) continue;
                loggedActive = true;
                Debug.Log($"Wildlife: active ({(mode == Mode.Endless ? "endless road" : "finite world")}, seed {habitat.Seed}) — {palette.Entries.Count} animal prefabs, " +
                          $"{palette.Clips.Count} sounds. First group: {g}. Caps {WildlifeActivator.MaxGroups} groups / {WildlifeActivator.MaxAnimals} animals; " +
                          $"appear within {WildlifeActivator.ActivateRadius:0} m, released beyond {WildlifeActivator.ReleaseRadius:0} m. F6 starts an event.", this);
            }
        }

        /// <summary>Moves members standing in a building, fence or trunk to free ground nearby (or drops them).</summary>
        private void FixMembers(AnimalGroup g)
        {
            for (int i = g.Members.Count - 1; i >= 0; i--)
            {
                AnimalAgent a = g.Members[i];
                if (a.Scripted || a.Altitude > 0.5f) continue;
                if (!ground.IsBlocked(a.X, a.Z, 0.45f) && !ground.IsWater(a.X, a.Z)) continue;
                bool placed = false;
                for (int k = 0; k < 8 && !placed; k++)
                {
                    g.HomePoint(a.Rng.Next(), a.Rng.Next(), out float x, out float z);
                    if (ground.IsBlocked(x, z, 0.45f) || ground.IsWater(x, z)) continue;
                    a.X = a.HomeX = x;
                    a.Z = a.HomeZ = z;
                    a.Stop();
                    sim.SampleGround(a, true);
                    a.Y = a.GroundY;
                    placed = true;
                }

                if (!placed) sim.RemoveMember(a);
            }
        }

        private void RunChecks()
        {
            float now = Time.unscaledTime;
            for (int i = checks.Count - 1; i >= 0; i--)
            {
                if (checks[i].At > now) continue;
                AnimalGroup g = checks[i].Group;
                checks.RemoveAt(i);
                // Only while the traveller is far enough that nobody sees an animal step out of a wall.
                if (g.Members.Count > 0 && sim.Groups.Contains(g) && g.Event == null && g.Members[0].TravellerDistance > 60f) FixMembers(g);
            }
        }

        // ------------------------------------------------------------------ views

        private void SyncViews(float dt)
        {
            foreach (AnimalAgent a in sim.Agents)
            {
                var view = a.View as AnimalView;
                if (view == null)
                {
                    view = Acquire(a);
                    if (view == null) continue;
                    a.View = view;
                }

                if (a.TravellerDistance < WildlifeSim.NearThink && ((Time.frameCount + a.Serial) & 7) == 0) a.Pitch = sim.PitchAt(a, view.Length);
                view.Sync(a, dt, AnimateWithin, ShadowsWithin);
                if (view.Animator != null && view.AnimatorRunning)
                {
                    // A paused (distant) animator keeps its pose; the driver catches up when it runs again.
                    bool observable = view.IdleObservable;
                    a.Driver.Update(dt, observable, observable && view.InIdle, view);
                }
            }
        }

        private AnimalView Acquire(AnimalAgent a)
        {
            string name = a.Info.Prefabs[a.Variant];
            WildlifePalette.Entry entry = palette.Find(name) ?? palette.Find(a.Info.Prefabs[0]);
            if (entry == null || entry.prefab == null)
            {
                if (warned.Add("missing:" + name)) Debug.LogWarning($"Wildlife: the palette has no prefab for {name}; that animal is not shown (rebuild the palette).", this);
                return null;
            }

            AnimalView view = null;
            if (pools.TryGetValue(entry.name, out Stack<AnimalView> stack) && stack.Count > 0) view = stack.Pop();
            if (view == null)
            {
                if (instantiatedThisFrame >= InstantiationsPerFrame) return null;   // spread the cost; it is far away in the haze
                instantiatedThisFrame++;
                using Review.FrameCost.Scope cost = Review.FrameCost.Measure(CostSpawn);
                Review.FrameCost.FirstUse("Instantiate " + entry.prefab.name);
                // Created active (under the shown animals), so its Animator is initialised before its parameters are read.
                GameObject go = Instantiate(entry.prefab, new Vector3(a.X, a.Y, a.Z), Quaternion.Euler(0f, a.Yaw, 0f), viewRoot);
                go.name = entry.name;
                view = new AnimalView(entry.name, go, a.Info, entry, out string problem);
                if (problem != null && warned.Add(problem)) Debug.LogWarning("Wildlife: " + problem, this);
                view.FootOffset = entry.footOffset;
            }

            view.Info = a.Info;
            view.Scale = palette.ScaleFor(a.Info, entry);
            view.Length = Mathf.Max(0.1f, entry.length * view.Scale);
            view.Transform.SetParent(viewRoot, false);
            view.Transform.localScale = Vector3.one * view.Scale;
            view.Transform.SetPositionAndRotation(new Vector3(a.X, a.Y - view.FootOffset * view.Scale, a.Z), Quaternion.Euler(0f, a.Yaw, 0f));
            view.Root.SetActive(true);
            view.ResetAnimator();
            Pose wanted = a.Pose;
            a.Driver.Reset(a.Info);
            a.Driver.Wanted = wanted;
            return view;
        }

        private void DrainRemoved()
        {
            if (sim == null) return;
            foreach (AnimalAgent a in sim.Removed)
            {
                if (a.View is AnimalView view)
                {
                    view.Root.SetActive(false);
                    view.Transform.SetParent(poolRoot, false);
                    if (!pools.TryGetValue(view.PrefabName, out Stack<AnimalView> stack))
                    {
                        stack = new Stack<AnimalView>();
                        pools[view.PrefabName] = stack;
                    }

                    stack.Push(view);
                }

                sim.Recycle(a);
            }

            sim.Removed.Clear();
        }

        // ------------------------------------------------------------------ sound, log, keys

        private void PlaySounds()
        {
            float master = Mathf.Clamp01(WildlifePreferences.SoundVolume);
            foreach (SoundRequest r in sim.Sounds)
            {
                if (master <= 0.001f || r.Agent == null || !r.Agent.Alive || r.Agent.View == null) continue;
                AudioClip clip = palette.Clip(r.Clip);
                if (clip == null)
                {
                    if (warned.Add("clip:" + r.Clip)) Debug.LogWarning($"Wildlife: sound {r.Clip} is not in the palette.", this);
                    continue;
                }

                voices.Play(clip, r.Agent, r.Volume, r.Loop, Time.unscaledTime);
            }

            sim.Sounds.Clear();
            voices.Update(Time.unscaledTime, master);
        }

        private void WriteLog()
        {
            List<string> log = sim.Events.Log;
            if (log.Count == 0) return;
            foreach (string line in log) Debug.Log(line, this);
            log.Clear();
        }

        private void HandleKeys(in WildlifeContext ctx, bool running)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f6Key.wasPressedThisFrame) return;
            HudController hud = HudController.Instance;
            if (!running)
            {
                string why = !WildlifePreferences.AnimalsShown ? "Animals are hidden (Journey menu, Sky)" : "Animals are switched off at this safety level";
                if (hud != null) hud.ShowToast(why);
                Debug.Log("Wildlife: F6 — " + why + ".", this);
                return;
            }

            if (sim.Events.TryForce(ctx, out string reason))
            {
                AnimalEvent e = sim.Events.Active;
                float dx = e.FocusX - ctx.TravellerX, dz = e.FocusZ - ctx.TravellerZ;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float side = ctx.FacingZ * dx - ctx.FacingX * dz;
                float front = (ctx.FacingX * dx + ctx.FacingZ * dz) / Mathf.Max(0.01f, d);
                string where = front > 0.7f ? "ahead" : front < -0.7f ? "behind you" : side > 0f ? "to your right" : "to your left";
                if (hud != null) hud.ShowToast($"{e.Name}, {d:0} m {where}");
                WriteLog();
            }
            else
            {
                if (hud != null) hud.ShowToast(reason);
                Debug.Log("Wildlife: F6 — " + reason + ".", this);
            }
        }

        // ------------------------------------------------------------------ origin, overlay

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            if (sim == null) return;
            sim.Shift(delta);
            foreach (AnimalAgent a in sim.Agents)
            {
                if (a.View is AnimalView view) view.Shift(delta);
            }

            voices.Shift(delta);
            if (generatorRoad != null && endless != null && endless.Origin != null)
            {
                generatorRoad.OriginX = endless.Origin.X;
                generatorRoad.OriginZ = endless.Origin.Z;
            }
        }

        /// <summary>One line for the endless road's F3 overlay.</summary>
        public string OverlayLine()
        {
            overlay.Clear();
            if (palette == null)
            {
                overlay.Append("animals  no palette (MedievalZen/Wildlife/Build Wildlife Palette)");
                return overlay.ToString();
            }

            if (sim == null || cleared)
            {
                overlay.Append("animals  off").Append(!WildlifePreferences.AnimalsShown ? " (hidden in the Journey menu)" : !FeatureOn ? " (safety level)" : string.Empty);
                return overlay.ToString();
            }

            AnimalEvent e = sim.Events.Active;
            overlay.Append("animals  ").Append(sim.Agents.Count).Append(" in ").Append(sim.Groups.Count).Append(" groups (caps ")
                .Append(WildlifeActivator.MaxAnimals).Append('/').Append(WildlifeActivator.MaxGroups).Append(")   cells planned ").Append(activator.CellsPlanned)
                .Append("   remembered ").Append(activator.Remembered).Append("   event ").Append(e != null ? $"{e.Name} {e.Elapsed:0} s" : "-   (F6 starts one)");
            return overlay.ToString();
        }
    }
}
