using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Travel;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The endless road's streamer, one per scene. Keeps full-detail tiles within <see cref="EndlessSettings.nearDistance"/>
    /// and silhouettes out to <see cref="EndlessSettings.farDistance"/> of the traveller: requests are ordered by distance with
    /// a bias ahead, generated on worker threads (no Unity API there) and applied on the main thread in slices within
    /// <see cref="EndlessSettings.applyBudgetMs"/> per frame. Owns the floating origin (<see cref="ToScene(double, double)"/>,
    /// <see cref="ToAbsolute"/>), the start-up and outrun holds, the sliding road window that keeps
    /// <see cref="GameWorld"/> supplied for travel, arrival and the HUD, the world surface for the atmosphere systems, the
    /// journey's persistence, and a debug overlay (F3; F4 forces an origin shift).
    /// Runs before the player (<see cref="PlayerController"/> −50) so tiles and the origin are settled when it reads the world.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-70)]
    public sealed partial class EndlessWorld : MonoBehaviour, ITravelDestinationPicker
    {
        /// <summary>A cabin prefab's measured chimney top (local space), recorded by the scene menu for the inns' chimney smoke.</summary>
        [Serializable]
        public struct CabinChimney
        {
            /// <summary>The cabin prefab.</summary>
            public GameObject prefab;

            /// <summary>Chimney top in the prefab root's local space.</summary>
            public Vector3 chimneyLocal;
        }

        private const int MaxConcurrentBuilds = 3;
        private const float PlanInterval = 0.25f;
        private const float PlanMoveThreshold = 8f;
        private const float GroundCheckRadius = 6f;

        [Header("World")]
        [SerializeField, Tooltip("Endless-road settings (world inputs, streaming distances and budgets).")]
        private EndlessSettings settings;

        [SerializeField, Tooltip("Prefabs, terrain layers and materials (the finite world's palette). Falls back to the GameWorld's.")]
        private WorldPalette palette;

        [SerializeField, Tooltip("Use the built-in stub generator even when the real one exists (testing the runtime on its own).")]
        private bool useStubGenerator = false;

        [SerializeField, Tooltip("Material of the distant tiles (MedievalZen/FarTerrain shader; Assets/_Project/Materials/Endless/FarTerrain.mat, assigned by Setup Scene).")]
        private Material farMaterial = null;

        [Header("Features (all on; the safety ladder may switch more off after a run that did not end cleanly)")]
        [SerializeField, Tooltip("Distant tiles (meshes out to the far distance).")]
        private bool farTiles = true;

        [SerializeField, Tooltip("Tree impostors on the distant tiles.")]
        private bool farTrees = true;

        [SerializeField, Tooltip("Instanced trees (and trunk colliders) on the walkable tiles.")]
        private bool nearTrees = true;

        [SerializeField, Tooltip("Grass and flowers on the walkable tiles.")]
        private bool grassDetails = true;

        [SerializeField, Tooltip("Props (rocks, fences, buildings) on the walkable tiles.")]
        private bool props = true;

        [SerializeField, Tooltip("Lake water planes.")]
        private bool water = true;

        [SerializeField, Tooltip("Shadows cast by the instanced trees (inside the URP shadow distance).")]
        private bool treeShadows = true;

        [Header("Start")]
        [SerializeField, Range(4f, 60f), Tooltip("Main-thread milliseconds per frame for building tiles while the screen is black (start, new journey, Go now).")]
        private float blackScreenBudgetMs = 24f;

        [SerializeField, Min(0f), Tooltip("At start, wait (on black) until far tiles within this distance exist, so the first view has its horizon.")]
        private float startFarRadius = 1100f;

        [SerializeField, Min(1f), Tooltip("Longest wait on black for the far tiles at start (near tiles are always waited for).")]
        private float startTimeout = 15f;

        [Header("Inn ambience")]
        [SerializeField, Tooltip("Chimney smoke for inn cabins (PolygonGeneric FX_Smoke_01).")]
        private GameObject smokePrefab = null;

        [SerializeField, Tooltip("Campfire flames (Meadow FX_Fire_01).")]
        private GameObject firePrefab = null;

        [SerializeField, Tooltip("Measured chimney tops of the cabin prefabs (filled by the scene menu).")]
        private CabinChimney[] cabinChimneys = Array.Empty<CabinChimney>();

        [Header("Debug")]
        [SerializeField, Tooltip("Show the streaming overlay (toggle with F3 in play).")]
        private bool showOverlay;

        // ------------------------------------------------------------------ state
        private EndlessParams parameters;
        private IEndlessGenerator generator;
        private string generatorName = "none";
        private FloatingOrigin origin;
        private EndlessTileBuilder builder;
        private EndlessTreeRenderer trees;
        private FarTreeStyles farStyles = FarTreeStyles.Defaults;
        private EndlessSurface surface;
        private Transform tilesRoot;
        private Transform poolRoot;
        private GameWorld world;
        private EndlessJourney journey;

        private readonly Dictionary<TileCoord, CoordState> states = new Dictionary<TileCoord, CoordState>();
        private readonly List<TileWant> wants = new List<TileWant>(512);
        private readonly HashSet<TileCoord> wanted = new HashSet<TileCoord>();
        private readonly List<GenerationJob> running = new List<GenerationJob>();
        private readonly List<EndlessTile> building = new List<EndlessTile>();
        private readonly List<EndlessTile> releasing = new List<EndlessTile>();
        private readonly List<CoordState> scratch = new List<CoordState>(512);
        private readonly List<TileCoord> removeScratch = new List<TileCoord>(64);
        private readonly Dictionary<int, Waypoint> knownWaypoints = new Dictionary<int, Waypoint>();
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private StreamingRanges ranges;
        private float nextPlan;
        private double planX = double.NaN, planZ = double.NaN;

        // Player.
        private Transform traveller;
        private CharacterController body;
        private PlayerMotor motor;
        private PlayerCameraRig cameraRig;
        private PlayerTravel travel;
        private bool frozenByStream;
        private bool frozenByScript;
        private bool startup = true;
        private bool scripted;
        private float startupBegan;
        private bool holdFaded;

        // Diagnostics.
        private readonly Stopwatch frameClock = new Stopwatch();
        private readonly Stopwatch sliceClock = new Stopwatch();
        private readonly float[] spikeBuckets = new float[10];
        private readonly int[] spikeStamps = new int[10];
        private double lastGenerationMs;
        private double worstGenerationMs;
        private float lastApplyMs;
        private readonly float[] sliceBuckets = new float[10];
        private readonly int[] sliceStamps = new int[10];
        private readonly string[] sliceLabels = new string[10];
        private int shiftCount;
        private float lastShiftMs;
        private int failuresLogged;

        /// <summary>The streamer in the loaded scene (set in Awake).</summary>
        public static EndlessWorld Current { get; private set; }

        /// <summary>Settings in use.</summary>
        public EndlessSettings Settings => settings;

        /// <summary>Generator inputs (seed of the current journey).</summary>
        public EndlessParams Params => parameters;

        /// <summary>Seed of the current journey.</summary>
        public int Seed => parameters != null ? parameters.seed : settings != null ? settings.seed : 0;

        /// <summary>The world as a pure function (shared with worker threads).</summary>
        public IEndlessGenerator Generator => generator;

        /// <summary>Which generator is in use (for the overlay and the log).</summary>
        public string GeneratorName => generatorName;

        /// <summary>The floating origin (scene position = absolute − origin).</summary>
        public FloatingOrigin Origin => origin;

        /// <summary>Parent of every tile root.</summary>
        internal Transform TilesRoot => tilesRoot;

        /// <summary>Measured chimneys of cabin prefabs.</summary>
        internal CabinChimney[] CabinChimneys => cabinChimneys ?? Array.Empty<CabinChimney>();

        /// <summary>Chimney smoke prefab for inn ambience.</summary>
        internal GameObject SmokePrefab => smokePrefab;

        /// <summary>Campfire prefab for inn ambience.</summary>
        internal GameObject FirePrefab => firePrefab;

        /// <summary>True while the traveller is held (start, outrun streaming, Go now, new journey).</summary>
        public bool IsHolding => frozenByStream || frozenByScript || startup;

        /// <summary>Scene position of an absolute point on the ground plane.</summary>
        public Vector3 ToScene(double x, double z) => origin.ToScene(x, z);

        /// <summary>Scene position of an absolute point.</summary>
        public Vector3 ToScene(double x, double y, double z) => origin.ToScene(x, y, z);

        /// <summary>Absolute (x, z) of a scene position.</summary>
        public (double x, double z) ToAbsolute(Vector3 scene) => origin.ToAbsolute(scene);

        /// <summary>Scene position of a tile's minimum corner.</summary>
        public Vector3 TileScenePosition(TileCoord c) => origin.ToScene(c.OriginX(parameters.tileSize), c.OriginZ(parameters.tileSize));

        // ------------------------------------------------------------------ lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }

        private void Awake()
        {
            if (Current != null && Current != this) Debug.LogWarning("Several EndlessWorld components; the last one wins.", this);
            Current = this;

            if (settings == null)
            {
                Debug.LogError("EndlessWorld has no EndlessSettings assigned; running on the defaults. Run MedievalZen/Endless Road/Setup Scene (it assigns and verifies every reference).", this);
                settings = ScriptableObject.CreateInstance<EndlessSettings>();
                settings.name = "EndlessSettings (defaults)";
            }

            BeginSafetyRun();

            world = GameWorld.Current != null ? GameWorld.Current : FindFirstObjectByType<GameWorld>();
            if (world == null)
            {
                world = new GameObject("GameWorld").AddComponent<GameWorld>();
                Debug.LogWarning("EndlessWorld: the scene had no GameWorld; created one (travel, arrival and the HUD need it).", this);
            }

            if (palette == null) palette = world.Palette;
            if (palette == null)
            {
                Debug.LogError("EndlessWorld needs a WorldPalette (run MedievalZen/Create Default World Palette, then Endless Road/Setup Scene). Disabled.", this);
                enabled = false;
                return;
            }

            world.Palette = palette;
            journey = LoadJourney();
            CreateGenerator(journey.Seed);

            tilesRoot = new GameObject("Tiles").transform;
            tilesRoot.SetParent(transform, false);
            var pool = new GameObject("Pool");
            pool.SetActive(false);
            pool.transform.SetParent(transform, false);
            poolRoot = pool.transform;
            transform.position = Vector3.zero;
            trees = new EndlessTreeRenderer(palette, tilesRoot, gameObject.layer);
            farStyles = BuildFarStyles(palette);
            builder = new EndlessTileBuilder(this, palette, poolRoot, ResolveFarMaterial(), trees);
            gameObject.AddComponent<EndlessLateDraw>().World = this;
            LogStartLine();

            surface = new EndlessSurface(this);
            world.SetSurface(surface);

            PlayerController controller = FindFirstObjectByType<PlayerController>();
            if (controller != null)
            {
                traveller = controller.transform;
                body = controller.GetComponent<CharacterController>();
                motor = controller.GetComponent<PlayerMotor>();
                cameraRig = controller.GetComponent<PlayerCameraRig>();
                travel = controller.GetComponent<PlayerTravel>();
                if (travel != null) travel.DestinationPicker = this;
            }
            else
            {
                Debug.LogWarning("EndlessWorld: no PlayerController in the scene; tiles stream around the main camera.", this);
            }

            // Origin at the traveller (a resumed journey starts there), traveller on the road, held until the ground exists.
            RoadPoint start = generator.Road(journey.S);
            origin = new FloatingOrigin(parameters.tileSize, start.X, start.Z);
            travellerS = journey.S;
            PlaceTravellerAt(journey.S, +1);
            startup = true;
            startupBegan = Time.unscaledTime;
            frozenByScript = true;
            ApplyFreeze();
        }

        private void Start()
        {
            if (!enabled) return;
            HudController hud = HudController.Instance;
            if (hud != null) hud.Fade(1f, 0f);
            holdFaded = true;
            RebuildWindowNow();
            UpdateNature(true);
            PlanNow();
        }

        private void OnDisable()
        {
            SaveJourney();
            EndSafetyRun();
        }

        private void OnApplicationQuit()
        {
            SaveJourney();
            EndSafetyRun();
        }

        private void OnDestroy()
        {
            CancelAll();
            lifetime.Cancel();
            // The scene is going: its GameObjects go with it; only the in-memory TerrainData, meshes and material copies must
            // be destroyed by hand (no re-parenting while the hierarchy is being torn down).
            builder?.DestroyAll();
            trees?.Dispose();
            if (farMaterial == null && farMaterialInUse != null) Destroy(farMaterialInUse);

            states.Clear();
            building.Clear();
            releasing.Clear();
            if (world != null) world.SetSurface(null);
            if (travel != null && ReferenceEquals(travel.DestinationPicker, this)) travel.DestinationPicker = null;
            if (Current == this) Current = null;
        }

        private void CreateGenerator(int seed)
        {
            parameters = settings.ToParams();
            parameters.seed = seed;
            ranges = StreamingRanges.From(settings);
            if (!Features.farTiles) ranges.Far = ranges.Near;   // no distant tiles at all
            generator = EndlessGeneratorFactory.Create(parameters, palette.GetKeys(), useStubGenerator, out generatorName, out bool isStub);
            Debug.Log($"Endless road: seed {seed}, generator {generatorName}{(isStub ? " — the stand-in; tiles are simple placeholders" : string.Empty)}.", this);
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (generator == null) return;
            HandleDebugKeys();
            TrackTraveller();
            if (!startup && !scripted && traveller != null && FloatingOrigin.NeedsShift(traveller.position, settings.originShiftDistance))
            {
                ShiftOrigin(origin.DeltaToward(traveller.position));
            }

            if (Time.unscaledTime >= nextPlan || MovedSincePlan()) PlanNow();
            PollJobs();
            LaunchJobs();
            ApplySlices(IsBlack() ? blackScreenBudgetMs : settings.applyBudgetMs);
            UpdateHold();
            UpdateRoadWindow();
            UpdateJourneyState();
        }

        private bool IsBlack()
        {
            HudController hud = HudController.Instance;
            return startup || (hud != null && hud.FadeAlpha > 0.98f);
        }

        // ------------------------------------------------------------------ planning

        private bool MovedSincePlan()
        {
            if (double.IsNaN(planX)) return true;
            (double x, double z) = TravellerAbsolute();
            return (x - planX) * (x - planX) + (z - planZ) * (z - planZ) > PlanMoveThreshold * PlanMoveThreshold;
        }

        private void PlanNow()
        {
            nextPlan = Time.unscaledTime + PlanInterval;
            (double px, double pz) = TravellerAbsolute();
            planX = px;
            planZ = pz;
            Vector3 dir = TravelDirection();
            TileStreaming.Plan(px, pz, dir.x, dir.z, ranges, c => states.TryGetValue(c, out CoordState s) ? s.Current : null, wants);

            wanted.Clear();
            foreach (TileWant w in wants)
            {
                wanted.Add(w.Coord);
                if (!states.TryGetValue(w.Coord, out CoordState state)) states[w.Coord] = state = new CoordState { Coord = w.Coord };
                state.Target = w.Lod;
                state.Priority = w.Priority;
            }

            removeScratch.Clear();
            foreach (KeyValuePair<TileCoord, CoordState> pair in states)
            {
                CoordState state = pair.Value;
                if (!wanted.Contains(pair.Key)) state.Target = null;
                Reconcile(state);
                if (state.Target == null && state.Idle) removeScratch.Add(pair.Key);
            }

            foreach (TileCoord c in removeScratch) states.Remove(c);
        }

        /// <summary>Drops whatever no longer matches the wanted level of detail; releases a tile nobody wants.</summary>
        private void Reconcile(CoordState state)
        {
            TileLod? target = state.Target;
            if (state.Job != null && state.Job.Lod != target)
            {
                state.Job.Cancel.Cancel();
                state.Job = null;
            }

            if (state.Ready != null && state.Ready.Blueprint.Lod != target) state.Ready = null;
            if (state.Building != null && state.Building.Lod != target) AbortBuild(state);
            if (target == null && state.Active != null)
            {
                Release(state.Active);
                state.Active = null;
                UpdateNeighbours(state.Coord);
            }
        }

        private void AbortBuild(CoordState state)
        {
            EndlessTile tile = state.Building;
            state.Building = null;
            if (tile == null) return;
            building.Remove(tile);
            Release(tile);
        }

        private void Release(EndlessTile tile)
        {
            if (tile == null || tile.State == TileState.Releasing || tile.State == TileState.Released) return;
            building.Remove(tile);   // a tile still switching its props on after the swap stops stepping too
            builder.Hide(tile);
            tile.State = TileState.Releasing;
            tile.Steps = null;
            releasing.Add(tile);
        }

        // ------------------------------------------------------------------ generation (worker threads)

        private void LaunchJobs()
        {
            int workers = Mathf.Clamp(settings.workerCount, 1, 8);
            if (running.Count >= workers) return;
            scratch.Clear();
            float now = Time.unscaledTime;
            foreach (CoordState state in states.Values)
            {
                if (state.Target == null || state.Job != null || state.Ready != null || state.Building != null) continue;
                if (state.Active != null && state.Active.Lod == state.Target) continue;
                if (now < state.RetryAt) continue;
                scratch.Add(state);
            }

            if (scratch.Count == 0) return;
            scratch.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            for (int i = 0; i < scratch.Count && running.Count < workers; i++) Launch(scratch[i]);
        }

        private void Launch(CoordState state)
        {
            TileCoord coord = state.Coord;
            TileLod lod = state.Target.Value;
            IEndlessGenerator gen = generator;
            float ts = parameters.tileSize;
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            CancellationToken token = cancel.Token;
            var job = new GenerationJob { Coord = coord, Lod = lod, Cancel = cancel };
            EndlessFeatures features = Features;
            FarTreeStyles styles = farStyles;
            TreePrototypeIndex prototypes = trees?.Index;
            var meshSettings = new FarMeshSettings { IncludeTrees = features.farTrees };
            job.Task = Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var clock = Stopwatch.StartNew();
                TileBlueprint bp = gen.GenerateTile(coord, lod);
                token.ThrowIfCancellationRequested();
                var density = new TreeDensityGrid(0f, 0f, ts, 16f);
                foreach (TreePlacement tree in bp.Trees) density.Add(tree.position.x, tree.position.z);
                var result = new GeneratedTile { Blueprint = bp, Density = density };
                if (lod == TileLod.Far)
                {
                    // The whole far mesh (ground, skirt, tree impostors) as plain arrays; the main thread only uploads it.
                    result.FarMesh = FarTileMeshData.Build(bp, styles, meshSettings, gen.Height);
                    bp.Splat = null;   // only the heights are read again (ground queries)
                }
                else if (features.nearTrees && prototypes != null)
                {
                    result.Trees = TreeCellSet.Build(bp.Trees, ts, prototypes);
                }

                result.Milliseconds = clock.Elapsed.TotalMilliseconds;
                return result;
            }, token);
            state.Job = job;
            running.Add(job);
        }

        private void PollJobs()
        {
            for (int i = running.Count - 1; i >= 0; i--)
            {
                GenerationJob job = running[i];
                if (!job.Task.IsCompleted) continue;
                running.RemoveAt(i);
                job.Cancel.Dispose();
                states.TryGetValue(job.Coord, out CoordState state);
                bool current = state != null && state.Job == job;
                if (current) state.Job = null;

                if (job.Task.Status == TaskStatus.RanToCompletion)
                {
                    GeneratedTile result = job.Task.Result;
                    lastGenerationMs = result.Milliseconds;
                    worstGenerationMs = Math.Max(worstGenerationMs, result.Milliseconds);
                    if (!current || state.Target != job.Lod || result.Blueprint == null || result.Blueprint.Heights == null) continue;
                    foreach (Waypoint w in result.Blueprint.Waypoints) Remember(w);
                    state.Ready = result;
                    state.Failures = 0;
                }
                else if (job.Task.IsFaulted && current)
                {
                    state.Failures++;
                    state.RetryAt = Time.unscaledTime + Mathf.Min(30f, Mathf.Pow(2f, state.Failures));
                    if (failuresLogged++ < 10)
                    {
                        Exception e = job.Task.Exception?.GetBaseException();
                        Debug.LogError($"Endless road: generating {job.Lod} tile {job.Coord} failed: {e}", this);
                    }
                }
            }
        }

        private void CancelAll()
        {
            foreach (GenerationJob job in running)
            {
                try
                {
                    job.Cancel.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            foreach (CoordState state in states.Values)
            {
                state.Job = null;
                state.Ready = null;
            }
        }

        // ------------------------------------------------------------------ applying (main thread, sliced)

        private void ApplySlices(float budgetMs)
        {
            frameClock.Restart();
            bool releasedOne = false;
            int guard = 0;
            EndlessTile chosen = null;
            int sinceChoice = 0;
            while (guard++ < 100000)
            {
                if (frameClock.Elapsed.TotalMilliseconds >= budgetMs) break;

                // One release slice first each frame (frees pooled objects for the builds), more only if time is left over.
                if (!releasedOne && releasing.Count > 0)
                {
                    releasedOne = true;
                    TimedRelease();
                    continue;
                }

                // Re-choose the most urgent tile every few slices (not every slice: there may be hundreds of coordinates).
                if (chosen == null || chosen.Steps == null || sinceChoice >= 8 || !building.Contains(chosen))
                {
                    chosen = NextBuild();
                    sinceChoice = 0;
                }

                sinceChoice++;
                EndlessTile next = chosen;
                if (next != null)
                {
                    sliceClock.Restart();
                    string label;
                    bool more;
                    try
                    {
                        more = next.Steps.MoveNext();
                        label = more ? next.Steps.Current : "finish";
                    }
                    catch (Exception e)
                    {
                        if (failuresLogged++ < 10) Debug.LogError($"Endless road: building {next} failed: {e}", this);
                        if (states.TryGetValue(next.Coord, out CoordState failed) && (failed.Building == next || failed.Active == next))
                        {
                            if (failed.Active == next) failed.Active = null;
                            failed.Building = null;
                            failed.Failures++;
                            failed.RetryAt = Time.unscaledTime + Mathf.Min(30f, Mathf.Pow(2f, failed.Failures));
                        }

                        building.Remove(next);
                        Release(next);
                        continue;
                    }

                    NoteSlice((float)sliceClock.Elapsed.TotalMilliseconds, label);
                    if (!more)
                    {
                        FinishBuild(next);
                        chosen = null;
                    }

                    continue;
                }

                if (releasing.Count > 0)
                {
                    TimedRelease();
                    continue;
                }

                if (startup && Prewarm()) continue;
                break;
            }

            lastApplyMs = (float)frameClock.Elapsed.TotalMilliseconds;
            NoteFrame(lastApplyMs);
        }

        private void TimedRelease()
        {
            sliceClock.Restart();
            EndlessTile tile = releasing[0];
            if (builder.ReleaseSlice(tile)) releasing.RemoveAt(0);
            NoteSlice((float)sliceClock.Elapsed.TotalMilliseconds, "release");
        }

        /// <summary>
        /// Creates pooled near terrains while waiting on black at start (far tiles are cheap meshes and need no prewarm); false
        /// when there is nothing left to prewarm.
        /// </summary>
        private bool Prewarm()
        {
            int nearWanted = 0;
            foreach (TileWant w in wants)
            {
                if (w.Lod == TileLod.Near) nearWanted++;
            }

            int nearHave = builder.PooledNear + CountTiles(TileLod.Near);
            if (nearHave >= nearWanted + 2 || builder.PooledNear >= builder.NearPoolCap) return false;
            sliceClock.Restart();
            builder.PrewarmNear();
            NoteSlice((float)sliceClock.Elapsed.TotalMilliseconds, "prewarm terrain");
            return true;
        }

        private int CountTiles(TileLod lod)
        {
            int n = 0;
            foreach (CoordState state in states.Values)
            {
                if (state.Active != null && state.Active.Lod == lod) n++;
                if (state.Building != null && state.Building.Lod == lod) n++;
            }

            return n;
        }

        /// <summary>The most urgent tile to work on: a build in progress, or a new one from the generated blueprints.</summary>
        private EndlessTile NextBuild()
        {
            EndlessTile best = null;
            float bestPriority = float.PositiveInfinity;
            foreach (EndlessTile tile in building)
            {
                float p = states.TryGetValue(tile.Coord, out CoordState s) ? s.Priority : float.PositiveInfinity;
                if (p < bestPriority)
                {
                    bestPriority = p;
                    best = tile;
                }
            }

            CoordState candidate = null;
            foreach (CoordState state in states.Values)
            {
                if (state.Ready == null || state.Building != null) continue;
                if (candidate == null || state.Priority < candidate.Priority) candidate = state;
            }

            if (candidate != null && (best == null || (candidate.Priority < bestPriority && building.Count < MaxConcurrentBuilds)))
            {
                GeneratedTile generated = candidate.Ready;
                candidate.Ready = null;
                var tile = new EndlessTile
                {
                    Coord = candidate.Coord, Lod = generated.Blueprint.Lod, State = TileState.Building, Blueprint = generated.Blueprint,
                    Density = generated.Density, FarMeshData = generated.FarMesh, Trees = generated.Trees, StartedAt = Time.realtimeSinceStartupAsDouble,
                };
                tile.Steps = builder.Build(tile);
                candidate.Building = tile;
                building.Add(tile);
                return tile;
            }

            return best;
        }

        /// <summary>Called by the builder when a tile's terrain is showing: it replaces the old tile at its coordinate.</summary>
        internal void OnTileVisible(EndlessTile tile)
        {
            if (!states.TryGetValue(tile.Coord, out CoordState state) || state.Building != tile)
            {
                // Nobody wants it any more (should not happen: aborted builds stop stepping).
                building.Remove(tile);
                Release(tile);
                return;
            }

            if (state.Active != null) Release(state.Active);
            state.Active = tile;
            state.Building = null;
            tile.State = TileState.Ready;
            UpdateNeighbours(tile.Coord);
        }

        private void FinishBuild(EndlessTile tile)
        {
            building.Remove(tile);
            tile.Steps = null;
        }

        /// <summary>Connects the terrain seams of a coordinate and its four neighbours (same level of detail only).</summary>
        private void UpdateNeighbours(TileCoord c)
        {
            Connect(c);
            Connect(new TileCoord(c.X - 1, c.Z));
            Connect(new TileCoord(c.X + 1, c.Z));
            Connect(new TileCoord(c.X, c.Z - 1));
            Connect(new TileCoord(c.X, c.Z + 1));
        }

        private void Connect(TileCoord c)
        {
            Terrain self = VisibleTerrain(c, null);
            if (self == null) return;
            TileLod lod = states[c].Active.Lod;
            self.SetNeighbors(VisibleTerrain(new TileCoord(c.X - 1, c.Z), lod), VisibleTerrain(new TileCoord(c.X, c.Z + 1), lod),
                VisibleTerrain(new TileCoord(c.X + 1, c.Z), lod), VisibleTerrain(new TileCoord(c.X, c.Z - 1), lod));
        }

        private Terrain VisibleTerrain(TileCoord c, TileLod? lod)
        {
            if (!states.TryGetValue(c, out CoordState state) || state.Active == null || state.Active.Slot == null) return null;
            if (lod.HasValue && state.Active.Lod != lod.Value) return null;
            return state.Active.Slot.Terrain;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Tree density grid of the tile at <paramref name="c"/> (tile-local, trees per 100 m²), or null when not built.</summary>
        internal TreeDensityGrid DensityAt(TileCoord c) => states.TryGetValue(c, out CoordState s) && s.Active != null ? s.Active.Density : null;

        /// <summary>Ground height at an absolute position: the built tile's heights where there is one (what the terrain shows), else the generator's.</summary>
        public float GroundHeightAbsolute(double x, double z)
        {
            float ts = parameters.tileSize;
            TileCoord c = TileCoord.Containing(x, z, ts);
            if (states.TryGetValue(c, out CoordState s) && s.Active != null && s.Active.Blueprint != null)
            {
                TileBlueprint bp = s.Active.Blueprint;
                return HeightmapGenerator.SampleHeight(bp.Heights, ts, bp.HeightRange, (float)(x - c.OriginX(ts)), (float)(z - c.OriginZ(ts)));
            }

            return generator.Height(x, z);
        }

        /// <summary>Lake level that applies at an absolute position: the tile's level where a tile is built, else the generator's (−1 = none).</summary>
        public float WaterHeightAbsolute(double x, double z)
        {
            TileCoord c = TileCoord.Containing(x, z, parameters.tileSize);
            if (states.TryGetValue(c, out CoordState s) && s.Active != null && s.Active.Blueprint != null) return s.Active.Blueprint.WaterHeight;
            return generator.WaterHeight(x, z);
        }

        /// <summary>True when every tile within <paramref name="radius"/> of a scene position is built at full detail with its collider.</summary>
        public bool GroundReadyAt(Vector3 scene, float radius = GroundCheckRadius)
        {
            (double x, double z) = origin.ToAbsolute(scene);
            float ts = parameters.tileSize;
            int x0 = (int)Math.Floor((x - radius) / ts), x1 = (int)Math.Floor((x + radius) / ts);
            int z0 = (int)Math.Floor((z - radius) / ts), z1 = (int)Math.Floor((z + radius) / ts);
            for (int tz = z0; tz <= z1; tz++)
            {
                for (int tx = x0; tx <= x1; tx++)
                {
                    if (!states.TryGetValue(new TileCoord(tx, tz), out CoordState s) || s.Active == null || s.Active.Lod != TileLod.Near) return false;
                }
            }

            return true;
        }

        /// <summary>A waypoint by id (from the road window, built tiles, or the generator around <paramref name="near"/>).</summary>
        internal Waypoint FindWaypoint(int id, TileCoord near)
        {
            if (knownWaypoints.TryGetValue(id, out Waypoint w)) return w;
            float ts = parameters.tileSize;
            double s = generator.NearestS(near.OriginX(ts) + ts * 0.5, near.OriginZ(ts) + ts * 0.5, travellerS);
            var found = new List<Waypoint>();
            generator.Waypoints(s - ts * 3.0, s + ts * 3.0, found);
            foreach (Waypoint f in found) Remember(f);
            return knownWaypoints.TryGetValue(id, out w) ? w : null;
        }

        private void Remember(Waypoint w)
        {
            if (w == null) return;
            knownWaypoints[w.Id] = w;
            if (knownWaypoints.Count > 4096) knownWaypoints.Clear();   // bounded; refilled on demand
        }

        // ------------------------------------------------------------------ diagnostics

        private void NoteSlice(float ms, string label)
        {
            int second = (int)Time.unscaledTime;
            int bucket = second % sliceBuckets.Length;
            if (sliceStamps[bucket] != second)
            {
                sliceStamps[bucket] = second;
                sliceBuckets[bucket] = 0f;
                sliceLabels[bucket] = "-";
            }

            if (ms <= sliceBuckets[bucket]) return;
            sliceBuckets[bucket] = ms;
            sliceLabels[bucket] = label;
        }

        /// <summary>The longest single streaming slice over the last 10 s, in milliseconds.</summary>
        public float WorstSliceMsLast10s => WorstSlice(out _);

        /// <summary>What that slice was (e.g. "alphamap band", "prop").</summary>
        public string WorstSliceNameLast10s
        {
            get
            {
                WorstSlice(out string label);
                return label;
            }
        }

        private float WorstSlice(out string label)
        {
            int now = (int)Time.unscaledTime;
            float worst = 0f;
            label = "-";
            for (int i = 0; i < sliceBuckets.Length; i++)
            {
                if (now - sliceStamps[i] >= sliceBuckets.Length || sliceBuckets[i] <= worst) continue;
                worst = sliceBuckets[i];
                label = sliceLabels[i];
            }

            return worst;
        }

        private void NoteFrame(float ms)
        {
            int second = (int)Time.unscaledTime;
            int bucket = second % spikeBuckets.Length;
            if (spikeStamps[bucket] != second)
            {
                spikeStamps[bucket] = second;
                spikeBuckets[bucket] = 0f;
            }

            spikeBuckets[bucket] = Mathf.Max(spikeBuckets[bucket], ms);
        }

        /// <summary>The longest streaming time spent in one frame over the last 10 s, in milliseconds.</summary>
        public float WorstApplyMsLast10s
        {
            get
            {
                int now = (int)Time.unscaledTime;
                float worst = 0f;
                for (int i = 0; i < spikeBuckets.Length; i++)
                {
                    if (now - spikeStamps[i] < spikeBuckets.Length) worst = Mathf.Max(worst, spikeBuckets[i]);
                }

                return worst;
            }
        }
    }
}
