using System.Collections;
using MedievalZen.Endless;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Cinematic
{
    /// <summary>
    /// The cinematic camera. Takes the view over through <see cref="PlayerCameraRig.SetPoseProvider"/> (the rig stays the
    /// only owner of the Camera and blends in and out) and films the traveller in slow, wide shots: seven for walking, six
    /// for resting (<see cref="ShotType"/>), chosen by a seeded <see cref="ShotScheduler"/> weighted by the country, the hour
    /// and the weather, and placed by the pure <see cref="ShotSolver"/>. Every pose is kept above ground and water, out of
    /// trees, inside the built world and within 200 m of the traveller (<see cref="ShotRules"/>), and damped by critically
    /// damped springs. A traveller hidden for more than a second, or a shot that has run its course, leads to the next shot:
    /// a slow blend when the two are close, else a short dip through black (the HUD fade).
    /// <para>When it runs: the <c>Cinematic</c> key (B / d-pad up) at any time; after a few seconds without input during
    /// auto-travel when "Cinematic when travelling" is on (third person); while seated (third person, rest shots); or after a
    /// few idle seconds whenever the Journey menu's camera choice is "Cinematic". Look input orbits the current shot gently and
    /// holds it; Move input ends it. Overlays (map, signposts, the Journey menu) and the endless road's holds suspend it.</para>
    /// Order −35: after the input glue, the auto-traveller and the seat; the camera pose itself is produced inside the rig's
    /// LateUpdate.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-35)]
    public sealed class CinematicDirector : MonoBehaviour, ICameraPoseProvider, IOriginShiftable
    {
        /// <summary>
        /// Chance (0..1) that a travel shot becomes the wide vista shot, written by the music player while a piece plays (0 otherwise).
        /// Only the first pick of a shot reads it; the vista still needs its usual weight and is never chosen twice in a row.
        /// </summary>
        public static float MusicWideBias;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMusicWideBias() => MusicWideBias = 0f;

        [Header("When it runs")]
        [SerializeField, Tooltip("The Journey menu's camera choice \"Cinematic\": the director takes over after a few idle seconds in any situation.")]
        private bool cinematicCamera = false;

        [SerializeField, Tooltip("Film auto-travel after a few seconds without look or move input (third person).")]
        private bool cinematicWhenTravelling = true;

        [SerializeField, Tooltip("Thin black bars while the cinematic camera has the view.")]
        private bool letterbox = true;

        [SerializeField, Min(1f), Tooltip("Seconds without look or move input before the camera takes over on its own.")]
        private float idleSeconds = 6f;

        [SerializeField, Min(0f), Tooltip("Seconds after sitting down before the rest shots begin.")]
        private float seatedDelay = 1.5f;

        [SerializeField, Min(0.5f), Tooltip("Seconds after the last look input before shot changes resume.")]
        private float resumeAfterLook = 4f;

        [Header("Blends")]
        [SerializeField, Min(0.1f), Tooltip("Seconds to blend from the normal camera into the first shot.")]
        private float blendInSeconds = 1.8f;

        [SerializeField, Min(0.1f), Tooltip("Seconds to blend back to the normal camera.")]
        private float blendOutSeconds = 1.2f;

        [SerializeField, Min(0.1f), Tooltip("Seconds to hand the camera back when an overlay (map, menu) opens.")]
        private float suspendSeconds = 0.5f;

        [SerializeField, Min(1f), Tooltip("Smoothing multiplier while drifting from one shot to the next (slow blend).")]
        private float shotBlendSlowdown = 3.2f;

        [SerializeField, Min(0.05f), Tooltip("Seconds of each half of the dip through black between distant shots.")]
        private float dipSeconds = 0.25f;

        [Header("Manual orbit (look input during a shot)")]
        [SerializeField, Range(0f, 2f), Tooltip("Share of the normal look speed used to orbit a shot.")]
        private float orbitSensitivity = 0.5f;

        [SerializeField, Range(0f, 180f), Tooltip("Largest orbit around the traveller, left or right (degrees).")]
        private float orbitYawLimit = 70f;

        [SerializeField, Range(0f, 60f), Tooltip("Largest orbit up or down (degrees).")]
        private float orbitPitchLimit = 22f;

        [Header("Sight")]
        [SerializeField, Min(0.1f), Tooltip("Seconds the traveller may be hidden before the shot changes.")]
        private float occludedCut = 1f;

        [SerializeField, Min(0.05f), Tooltip("Seconds between sight checks (each is two short raycasts and a tree test).")]
        private float sightInterval = 0.25f;

        [SerializeField, Tooltip("Layers that block the camera's sight of the traveller.")]
        private LayerMask sightLayers = Physics.DefaultRaycastLayers;

        private PlayerController controller;
        private PlayerCameraRig rig;
        private PlayerTravel travel;
        private PlayerMotor motor;
        private PlayerSeat seat;
        private PlayerInteractor interactor;
        private SceneQueries queries;
        private ShotScheduler scheduler;
        private readonly ShotPlan plan = new ShotPlan();
        private readonly ShotPlan candidate = new ShotPlan();
        private readonly ShotPlan spare = new ShotPlan();
        private readonly ShotInputs inputs = new ShotInputs();
        private readonly ShotInputs scratch = new ShotInputs();
        private readonly RoadPath path = new RoadPath(140);
        private ShotContext context;

        private bool ready;
        private bool running;
        private bool hasShot;
        private bool restMode;
        private bool manual;
        private bool autoSuppressed;
        private bool wasTravelling;
        private bool wasSeatActive;
        private float idleTimer;
        private float lastLookTime = -100f;
        private Vector2 orbit;
        private float shotTime;
        private float blendTimer;
        private float blockedTime;
        private float nextSight;
        private float nextPath;
        private float nextContext;

        private bool springsPrimed;
        private bool snapSprings;
        private Vector3 cameraPosition;
        private Vector3 cameraVelocity;
        private Vector3 lookPosition;
        private Vector3 lookVelocity;
        private float fov = 50f;
        private float fovVelocity;

        private Coroutine dipRoutine;
        private int dipPhase;   // 0 none, 1 fading out, 2 fading in

        /// <summary>Raised when the cinematic camera takes the view (true) or hands it back (false).</summary>
        public event System.Action<bool> RunningChanged;

        /// <summary>The Journey menu's camera choice "Cinematic".</summary>
        public bool CinematicCamera
        {
            get => cinematicCamera;
            set => cinematicCamera = value;
        }

        /// <summary>Film auto-travel on its own (third person).</summary>
        public bool CinematicWhenTravelling
        {
            get => cinematicWhenTravelling;
            set => cinematicWhenTravelling = value;
        }

        /// <summary>Letterbox bars during cinematic shots.</summary>
        public bool Letterbox
        {
            get => letterbox;
            set => letterbox = value;
        }

        /// <summary>True while the cinematic camera has the view.</summary>
        public bool IsRunning => running;

        /// <summary>The shot on screen (meaningful while <see cref="IsRunning"/>).</summary>
        public ShotType CurrentShot => plan.Type;

        /// <summary>The cinematic key: start filming now, or stop (and stay off until the next input).</summary>
        public void ToggleManual()
        {
            if (running || manual)
            {
                manual = false;
                autoSuppressed = true;
                return;
            }

            manual = true;
            autoSuppressed = false;
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            rig = GetComponent<PlayerCameraRig>();
            travel = GetComponent<PlayerTravel>();
            motor = GetComponent<PlayerMotor>();
            seat = GetComponent<PlayerSeat>();
            queries = new SceneQueries(transform) { Layers = sightLayers };
            inputs.Path = path;
            inputs.Queries = queries;
        }

        private void Start()
        {
            interactor = GetComponent<PlayerInteractor>();
            if (seat == null) seat = GetComponent<PlayerSeat>();
            int seed = 0;
            if (Streamer() != null) seed = Streamer().Seed;
            else if (GameWorld.Current != null && GameWorld.Current.Settings != null) seed = GameWorld.Current.Settings.seed;
            scheduler = new ShotScheduler(unchecked(seed * 7919 + 1301));
            if (rig != null) rig.ModeChanged += OnModeChanged;
            ready = controller != null && rig != null && motor != null;
        }

        private void OnDestroy()
        {
            if (rig != null) rig.ModeChanged -= OnModeChanged;
        }

        private void OnDisable()
        {
            if (running) Stop(blendOutSeconds, true);
            manual = false;
        }

        private void OnModeChanged(CameraMode mode)
        {
            // V is the player taking the camera: hand it back (manual filming survives a change of the base mode).
            idleTimer = 0f;
        }

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            cameraPosition += delta;
            lookPosition += delta;
            plan.Shift(delta);
            candidate.Shift(delta);
            path.Shift(delta);
            inputs.Subject += delta;
            inputs.Hip += delta;
            inputs.Fire += delta;
            inputs.Traveller += delta;
        }

        // ------------------------------------------------------------------ deciding

        private static readonly int FrameCostId = Review.FrameCost.Register("CinematicDirector");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (!ready) return;
            float dt = Time.deltaTime;
            bool overlay = controller.InputOverride != null;
            bool userMove = !overlay && controller.MoveInput.magnitude > controller.CancelThreshold;
            bool userLook = !overlay && controller.LookInput.sqrMagnitude > 1e-4f;
            if (userMove)
            {
                idleTimer = 0f;
                manual = false;
                autoSuppressed = false;
            }
            else if (userLook)
            {
                if (!running) idleTimer = 0f;
            }
            else
            {
                idleTimer += dt;
            }

            if (userLook && running)
            {
                lastLookTime = Time.time;
                Vector2 degrees = rig.LookDegrees(controller.LookInput, controller.LookIsPointer) * orbitSensitivity;
                orbit.x = Mathf.Clamp(orbit.x + degrees.x, -orbitYawLimit, orbitYawLimit);
                orbit.y = Mathf.Clamp(orbit.y + degrees.y, -orbitPitchLimit, orbitPitchLimit);
            }

            EndlessWorld endless = Streamer();
            bool holding = endless != null && endless.IsHolding;
            bool suspended = overlay || !controller.isActiveAndEnabled || (interactor != null && interactor.Busy) || holding;
            bool seated = seat != null && seat.IsSeated && seat.Weight > 0.6f;
            AutoTraveller traveller = travel != null ? travel.Traveller : null;
            bool travelling = traveller != null && traveller.IsActive && !traveller.Paused;
            bool third = rig.Mode == CameraMode.ThirdPerson;
            bool seatActive = seat != null && seat.IsActive;
            // "Not now" (the key pressed to stop filming) lasts until the situation changes: walking, a new trip, sitting or standing.
            if ((travelling && !wasTravelling) || seatActive != wasSeatActive) autoSuppressed = false;
            wasTravelling = travelling;
            wasSeatActive = seatActive;
            bool idle = idleTimer >= idleSeconds && !autoSuppressed;

            bool want = !userMove && !suspended && (
                manual ||
                (running && seatActive && !seated) ||   // keep filming while they walk to a seat, sit down or get up
                (cinematicCamera && idle) ||
                (cinematicWhenTravelling && third && travelling && idle) ||
                (seated && !autoSuppressed && (third || cinematicCamera) && seat.SeatedTime >= seatedDelay && idleTimer > 1f));

            if (want && !running) Begin(seated);
            else if (!want && running) Stop(suspended ? suspendSeconds : blendOutSeconds, !holding);
            else if (running && dipPhase == 0 && ((seated && !restMode) || (!seated && restMode && (seat == null || !seat.IsActive)))) SwitchMode(seated);

            if (running) StepShot(dt);
            if (blendTimer > 0f) blendTimer -= dt;

            HudController hud = HudController.Instance;
            if (hud != null)
            {
                hud.CinematicMode = running;
                hud.Letterbox = running && letterbox;
            }
        }

        private void Begin(bool rest)
        {
            running = true;
            restMode = rest;
            orbit = Vector2.zero;
            blockedTime = 0f;
            scheduler.Forget();
            RefreshPath();
            RefreshContext();
            FillInputs();
            ChooseShot(null, plan);
            shotTime = 0f;
            hasShot = true;
            // Still handing the view back from a moment ago: glide from where the camera is instead of jumping to the new shot.
            bool stillBlending = rig.HasPoseProvider && springsPrimed;
            snapSprings = !stillBlending;
            blendTimer = stillBlending ? 4f : 0f;
            rig.SetPoseProvider(this, blendInSeconds);
            RunningChanged?.Invoke(true);
        }

        private void Stop(float blendSeconds, bool restoreFade)
        {
            running = false;
            if (dipRoutine != null)
            {
                StopCoroutine(dipRoutine);
                dipRoutine = null;
                HudController hud = HudController.Instance;
                if (restoreFade && hud != null && dipPhase != 0) hud.Fade(0f, dipSeconds);
                dipPhase = 0;
            }

            // Hand back looking the way the shot looked, so the normal camera swings in instead of jumping round.
            if (springsPrimed && rig != null)
            {
                Vector3 forward = lookPosition - cameraPosition;
                if (forward.sqrMagnitude > 1e-4f) rig.SnapLook(CinematicMath.YawOf(forward), Mathf.Clamp(CinematicMath.PitchOf(forward), -10f, 25f));
            }

            if (rig != null) rig.ReleasePoseProvider(this, blendSeconds);
            idleTimer = 0f;
            RunningChanged?.Invoke(false);
        }

        private void SwitchMode(bool rest)
        {
            restMode = rest;
            scheduler.Forget();
            RefreshContext();
            FillInputs();
            NextShot(false);
        }

        // ------------------------------------------------------------------ shots

        private void StepShot(float dt)
        {
            if (Time.unscaledTime >= nextPath) RefreshPath();
            if (Time.unscaledTime >= nextContext) RefreshContext();
            FillInputs();

            bool lookHold = Time.time - lastLookTime < resumeAfterLook;
            if (!lookHold || shotTime < plan.Duration) shotTime += dt;

            if (Time.unscaledTime >= nextSight)
            {
                nextSight = Time.unscaledTime + sightInterval;
                if (springsPrimed && dipPhase == 0 && !ShotRules.CanSee(cameraPosition, inputs)) blockedTime += sightInterval;
                else blockedTime = 0f;
            }

            if (lookHold || dipPhase != 0) return;
            bool occluded = blockedTime > occludedCut;
            if (shotTime >= plan.Duration || ShotSolver.IsSpent(plan, inputs) || occluded) NextShot(occluded);
        }

        private void NextShot(bool occluded)
        {
            blockedTime = 0f;
            ShotType? failed = occluded ? plan.Type : (ShotType?)null;
            var current = new CameraPose { Position = cameraPosition, LookAt = lookPosition, Fov = fov };
            ChooseShot(failed, candidate);
            CameraPose next = ShotRules.Clear(ShotSolver.Evaluate(candidate, 0f, inputs), candidate.Type, queries, out _);
            // Prefer a next shot the camera can drift to: one more pick when the first would need a dip.
            if (springsPrimed && !occluded && !ShotRules.ShouldBlend(current, next))
            {
                ShotType first = candidate.Type;
                ChooseShot(first, spare);
                CameraPose other = ShotRules.Clear(ShotSolver.Evaluate(spare, 0f, inputs), spare.Type, queries, out _);
                if (ShotRules.ShouldBlend(current, other))
                {
                    candidate.CopyFrom(spare);
                    next = other;
                }
                else
                {
                    scheduler.Commit(first);
                }
            }

            HudController hud = HudController.Instance;
            if (!springsPrimed || ShotRules.ShouldBlend(current, next) || hud == null || hud.FadeAlpha > 0.01f)
            {
                plan.CopyFrom(candidate);
                shotTime = 0f;
                orbit = Vector2.zero;
                blendTimer = 4f;
                return;
            }

            dipRoutine = StartCoroutine(Dip(hud));
        }

        private IEnumerator Dip(HudController hud)
        {
            dipPhase = 1;
            hud.Fade(1f, dipSeconds);
            float waited = 0f;
            while (hud.FadeAlpha < 0.98f && waited < dipSeconds + 0.3f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            plan.CopyFrom(candidate);
            shotTime = 0f;
            orbit = Vector2.zero;
            blendTimer = 0f;
            snapSprings = true;
            yield return null;   // one frame on black with the camera already at the new shot
            dipPhase = 2;
            hud.Fade(0f, dipSeconds);
            float back = 0f;
            while (back < dipSeconds)
            {
                back += Time.unscaledDeltaTime;
                yield return null;
            }

            dipPhase = 0;
            dipRoutine = null;
        }

        /// <summary>Picks, plans and validates the next shot into <paramref name="target"/> (up to four tries, then a safe fallback).</summary>
        private void ChooseShot(ShotType? exclude, ShotPlan target)
        {
            context.Seated = restMode;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                ShotType type = scheduler.Next(context, exclude);
                if (attempt == 0 && !restMode && MusicWideBias > 0f && type != ShotType.Vista && exclude != ShotType.Vista &&
                    scheduler.Previous != ShotType.Vista && ShotScheduler.Weight(ShotType.Vista, context) > 0f &&
                    scheduler.Random.NextDouble() < MusicWideBias)
                    type = ShotType.Vista;   // a piece of music is playing: Death Stranding's pull-back to the wide view
                ShotSolver.Plan(target, type, scheduler.Duration(type), inputs, scheduler.Random);
                if (ShotRules.CountGood(target, inputs, scratch, 3) >= 2)
                {
                    scheduler.Commit(type);
                    return;
                }

                exclude = type;
            }

            ShotType fallback = restMode ? ShotType.RestOverShoulder : ShotType.TrailingWide;
            ShotSolver.Plan(target, fallback, scheduler.Duration(fallback), inputs, scheduler.Random);
            scheduler.Commit(fallback);
        }

        // ------------------------------------------------------------------ the pose (asked by the rig in its LateUpdate)

        /// <inheritdoc />
        public bool TryGetCameraPose(float deltaTime, out Vector3 position, out Quaternion rotation, out float fieldOfView)
        {
            position = default;
            rotation = Quaternion.identity;
            fieldOfView = fov;
            if (!hasShot) return false;

            FillInputs();
            CameraPose desired = ShotSolver.Evaluate(plan, shotTime, inputs);
            if (orbit.sqrMagnitude > 1e-4f) desired = Orbit(desired, inputs.SubjectPoint);
            desired = ShotRules.Clear(desired, plan.Type, queries, out _);

            ShotDefinition def = ShotLibrary.Get(plan.Type);
            float slow = blendTimer > 0f ? Mathf.Lerp(1f, shotBlendSlowdown, Mathf.Clamp01(blendTimer / 1.5f)) : 1f;
            if (snapSprings || !springsPrimed)
            {
                cameraPosition = desired.Position;
                lookPosition = desired.LookAt;
                fov = desired.Fov;
                cameraVelocity = lookVelocity = Vector3.zero;
                fovVelocity = 0f;
                snapSprings = false;
                springsPrimed = true;
            }
            else
            {
                cameraPosition = CinematicMath.DampLimited(cameraPosition, desired.Position, ref cameraVelocity, def.PositionSmooth * slow, MaxCameraSpeed, deltaTime);
                lookPosition = CinematicMath.Damp(lookPosition, desired.LookAt, ref lookVelocity, def.LookSmooth * slow, deltaTime);
                fov = CinematicMath.Damp(fov, desired.Fov, ref fovVelocity, 1.5f * slow, deltaTime);
            }

            // Never under the ground or the water, even while the spring cuts a corner (the desired poses keep each shot's own
            // margin; this safety net uses the lowest one, so a change of shot never makes the camera hop).
            cameraPosition = CinematicMath.Clear(cameraPosition, queries.GroundHeight(cameraPosition), queries.WaterHeight(cameraPosition),
                ShotRules.SafetyMargin, ShotRules.WaterMargin * 0.5f);

            Vector3 forward = lookPosition - cameraPosition;
            if (forward.sqrMagnitude < 1e-6f) forward = transform.forward;
            position = cameraPosition;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            fieldOfView = fov;
            return true;
        }

        /// <summary>Fastest the camera may travel (m/s); the spring never exceeds it, even across a long blend.</summary>
        private const float MaxCameraSpeed = 3.5f;

        private CameraPose Orbit(CameraPose pose, Vector3 pivot)
        {
            Vector3 offset = pose.Position - pivot;
            Vector3 look = pose.LookAt - pivot;
            offset = CinematicMath.RotateYaw(offset, orbit.x);
            look = CinematicMath.RotateYaw(look, orbit.x);
            Vector3 axis = CinematicMath.RightOf(CinematicMath.Planar(-offset, Vector3.forward));
            offset = CinematicMath.RotateAbout(offset, axis, orbit.y);   // looking down raises the camera
            pose.Position = pivot + offset;
            pose.LookAt = pivot + look;
            return pose;
        }

        // ------------------------------------------------------------------ inputs

        private void FillInputs()
        {
            inputs.Traveller = transform.position;
            inputs.Speed = motor.PlanarSpeed;
            Vector3 velocity = motor.Velocity;
            Vector3 heading = new Vector3(velocity.x, 0f, velocity.z);
            inputs.Heading = path.IsUsable ? path.TangentAt(path.Project(inputs.Traveller))
                : heading.sqrMagnitude > 0.04f ? heading.normalized : motor.Forward;
            bool seatedNow = seat != null && seat.IsActive;
            inputs.Seated = restMode && seatedNow;
            if (seatedNow)
            {
                SeatPlan p = seat.Plan;
                inputs.Hip = p.Hip;
                inputs.Facing = p.Facing;
                inputs.HasFire = p.HasFire;
                inputs.Fire = p.Fire;
            }

            DayCycle day = DayCycle.Current;
            if (day != null) inputs.SunDirection = day.LightDirection;
            Camera view = rig.Camera;
            if (view != null) inputs.Aspect = Mathf.Max(0.5f, view.aspect);
        }

        /// <summary>Samples the road around the traveller: the auto-travel route, else the nearest road, else none (straight line).</summary>
        private void RefreshPath()
        {
            nextPath = Time.unscaledTime + 0.25f;
            path.Clear();
            Vector3 position = transform.position;
            AutoTraveller traveller = travel != null ? travel.Traveller : null;
            RouteFollower follower = traveller != null && traveller.IsActive ? traveller.Follower : null;
            if (follower != null)
            {
                RoadRoute route = follower.Route;
                float d = follower.Distance;
                for (float k = -60f; k <= 200f; k += 2f)
                {
                    float at = d + k;
                    if (at < 0f || at > route.TotalLength) continue;
                    path.Add(route.Evaluate(at));
                }

                return;
            }

            GameWorld world = GameWorld.Current;
            RoadNetwork roads = world != null ? world.Roads : null;
            if (roads == null || roads.Segments.Count == 0) return;
            RoadLocation here = roads.Project(position);
            if (here.Segment == null) return;
            Vector3 onRoad = here.Position;
            if (new Vector2(onRoad.x - position.x, onRoad.z - position.z).sqrMagnitude > 60f * 60f) return;
            RoadSegment segment = here.Segment;
            float sign = Vector3.Dot(segment.Tangent(here.Distance), motor.Forward) >= 0f ? 1f : -1f;
            for (float k = -60f; k <= 200f; k += 2f)
            {
                float at = here.Distance + sign * k;
                if (at < 0f || at > segment.Length) continue;
                path.Add(segment.Evaluate(at));
            }
        }

        /// <summary>Classifies the country around the traveller and finds a vista subject (once a second).</summary>
        private void RefreshContext()
        {
            nextContext = Time.unscaledTime + 1f;
            Vector3 p = transform.position;
            // Woodland is judged around the road, not on it (the road corridor is kept clear of trees).
            float density = queries.TreeDensity(p) * 0.2f;
            for (int i = 0; i < 8; i++) density += queries.TreeDensity(p + CinematicMath.FromYaw(i * 45f) * 20f) * 0.1f;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            bool water = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = CinematicMath.FromYaw(i * 45f);
                for (int ring = 0; ring < 2; ring++)
                {
                    Vector3 q = p + d * (ring == 0 ? 50f : 110f);
                    float g = queries.GroundHeight(q);
                    lo = Mathf.Min(lo, g);
                    hi = Mathf.Max(hi, g);
                    float w = queries.WaterHeight(q);
                    if (w >= 0f && w > g) water = true;
                }
            }

            bool nearPass = false, nearSettlement = false;
            GameWorld world = GameWorld.Current;
            Vector3 heading = inputs.Heading;
            inputs.HasSubject = false;
            float best = 0f;
            if (world != null)
            {
                float maxDistance = Streamer() != null ? 1100f : 1800f;
                var landmarks = world.Landmarks;
                for (int i = 0; i < landmarks.Count; i++)
                {
                    Landmark l = landmarks[i];
                    Vector3 to = l.position - p;
                    to.y = 0f;
                    float distance = to.magnitude;
                    if (l.kind == LandmarkKind.Pass && distance < 600f) nearPass = true;
                    if (distance < 150f || distance > maxDistance) continue;
                    float front = Vector3.Dot(to / distance, heading);
                    if (!restMode && front < 0.25f) continue;
                    float kind = l.kind == LandmarkKind.Lake ? 0.9f : l.kind == LandmarkKind.Viewpoint ? 0.7f : 1f;
                    float score = kind * (restMode ? 1f : front) * (1f - Mathf.Abs(distance - 600f) / 2400f);
                    if (score <= best) continue;
                    best = score;
                    inputs.HasSubject = true;
                    // A structure (tower, chapel, windmill) is framed at about half its height, so the shot holds all of it.
                    inputs.Subject = l.position + Vector3.up * (l.kind == LandmarkKind.Lake ? 1f : l.kind == LandmarkKind.Structure ? 7f : 4f);
                }

                RoadNetwork roads = world.Roads;
                if (roads != null)
                {
                    var settlements = roads.Settlements;
                    for (int i = 0; i < settlements.Count; i++)
                    {
                        Vector3 to = settlements[i].Position - p;
                        to.y = 0f;
                        float r = settlements[i].Radius + 60f;
                        if (to.sqrMagnitude < r * r) nearSettlement = true;
                    }
                }
            }

            // Animals: an event (or a large herd) close by can be the subject too; landmark scoring above is unchanged and an
            // event outweighs any landmark, a herd only wins when no landmark is in view. Seated rest shots look any way.
            MedievalZen.Wildlife.PointsOfInterest animals = MedievalZen.Wildlife.PointsOfInterest.Current;
            if (animals != null && animals.TryBest(p, heading, restMode, out Vector3 animalSubject, out float animalScore) && animalScore > best)
            {
                best = animalScore;
                inputs.HasSubject = true;
                inputs.Subject = animalSubject;
            }

            context.Scenery = ShotContext.Classify(density, nearPass, hi - lo, water, nearSettlement);
            DayCycle day = DayCycle.Current;
            context.Hour = day != null ? day.SolarHour : 12f;   // golden hour and night follow the light (= Hour in spring and autumn)
            context.Overcast = Overcast();
            context.Seated = restMode;
            context.NearFire = seat != null && seat.IsActive && seat.Plan.HasFire;
            context.HasVistaSubject = inputs.HasSubject;
        }

        private static float Overcast()
        {
            WeatherSystem weather = WeatherSystem.Current;
            if (weather == null) return 0f;
            switch (weather.State)
            {
                case WeatherState.Overcast:
                case WeatherState.LightRain: return 1f;
                case WeatherState.Misty: return 0.7f;
                case WeatherState.Hazy: return 0.3f;
                default: return 0f;
            }
        }

        private static EndlessWorld Streamer() => EndlessWorld.Current;
    }
}
