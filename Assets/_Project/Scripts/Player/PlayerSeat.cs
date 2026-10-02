using System.Collections.Generic;
using MedievalZen.Environment;
using MedievalZen.Travel;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>Where and how the traveller sits (planned once when sitting starts; shifted with the floating origin).</summary>
    public struct SeatPlan
    {
        /// <summary>What is sat on.</summary>
        public SeatKind Kind;
        /// <summary>Which pose is built.</summary>
        public SeatPoseKind Pose;
        /// <summary>Hip joint target (world).</summary>
        public Vector3 Hip;
        /// <summary>Unit horizontal direction the traveller faces.</summary>
        public Vector3 Facing;
        /// <summary>Where the body (capsule) stands while seated.</summary>
        public Vector3 BodyPoint;
        /// <summary>Ground points under the left and right foot (the pose adds the ankle height).</summary>
        public Vector3 FootLeft, FootRight;
        /// <summary>Height of the seat top above the ground (0 on the ground).</summary>
        public float SeatHeight;
        /// <summary>A camp fire is near.</summary>
        public bool HasFire;
        /// <summary>The fire.</summary>
        public Vector3 Fire;
        /// <summary>The prop sat on (null on the ground).</summary>
        public Transform Prop;

        /// <summary>Moves every point by <paramref name="delta"/>.</summary>
        public void Shift(Vector3 delta)
        {
            Hip += delta;
            BodyPoint += delta;
            FootLeft += delta;
            FootRight += delta;
            Fire += delta;
        }
    }

    /// <summary>
    /// Sitting down and watching. The sit key (the <c>Crouch</c> action) sits the traveller where they stand, or — when a log,
    /// stump, crate, chair, low rock, camp fire or inn doorstep is within reach (<see cref="SeatIndex"/>) — walks them to it,
    /// turns them to the open view and sits them on it. While seated the camera lowers (and the cinematic director takes over
    /// with rest shots in third person), the HUD steps back to a short hint, and holding Sprint lets time run up to ×20 of the
    /// chosen flow (<see cref="DayCycle.FastForward"/>). Move input, the sit key again, Interact or auto-travel stand them up; an
    /// auto-travel trip under way is paused while seated and resumes on standing (unless they walk off).
    /// The pose itself is built by <see cref="SeatedPose"/> from <see cref="Plan"/> and <see cref="Weight"/>.
    /// Runs before the motor (order −42) and drives it only while walking to the seat and turning.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-42)]
    public sealed class PlayerSeat : MonoBehaviour, IOriginShiftable
    {
        /// <summary>The sitting state.</summary>
        public enum SeatPhase
        {
            /// <summary>Standing as usual.</summary>
            Idle = 0,
            /// <summary>Walking to the seat.</summary>
            Approaching = 1,
            /// <summary>Turning to face the view.</summary>
            Turning = 2,
            /// <summary>Sitting down or seated.</summary>
            Seated = 3,
            /// <summary>Getting up.</summary>
            Standing = 4,
        }

        /// <summary>Why the traveller stands up (decides what happens to a paused trip).</summary>
        public enum StandReason
        {
            /// <summary>The sit key or Interact: a paused trip resumes.</summary>
            Key = 0,
            /// <summary>Move input: a paused trip ends (walking off by hand).</summary>
            Moved = 1,
            /// <summary>The auto-travel key: a paused trip resumes, or a new one starts once standing.</summary>
            Travel = 2,
        }

        [Header("Finding a seat")]
        [SerializeField, Min(0.5f), Tooltip("Seats (logs, stumps, crates, chairs, low rocks, fires, inn doorsteps) within this many metres are used; otherwise the traveller sits on the ground.")]
        private float seatReach = 2.5f;

        [SerializeField, Tooltip("Inn doorsteps count as seats for the sit key (Interact at the door still rests at the inn).")]
        private bool doorstepsAreSeats = true;

        [SerializeField, Range(0.2f, 1f), Tooltip("Lowest seat top above the ground that counts as a seat (m).")]
        private float minSeatHeight = 0.22f;

        [SerializeField, Range(0.3f, 1.2f), Tooltip("Highest seat top above the ground that counts as a seat (m).")]
        private float maxSeatHeight = 0.78f;

        [SerializeField, Min(0.5f), Tooltip("Distance from a camp fire's centre to where the traveller sits by it (m).")]
        private float fireRingRadius = 1.35f;

        [SerializeField, Range(5f, 45f), Tooltip("Steepest ground (degrees) the traveller will sit down on.")]
        private float maxGroundSlope = 24f;

        [Header("Seat geometry (metres)")]
        [SerializeField, Range(0f, 0.3f), Tooltip("Hip joint height above the seat top.")]
        private float hipAboveSeat = 0.1f;

        [SerializeField, Range(0f, 0.4f), Tooltip("Hip joint height above the ground when sitting on the ground.")]
        private float hipAboveGround = 0.14f;

        [SerializeField, Range(-0.2f, 0.3f), Tooltip("Hips forward of the seat's centre, along the facing.")]
        private float seatForwardShift = 0.04f;

        [SerializeField, Range(0f, 1f), Tooltip("Where the body (capsule) stands in front of the hips while seated on a prop.")]
        private float bodyForwardOnSeat = 0.5f;

        [SerializeField, Range(0f, 1f), Tooltip("Where the body stands in front of the hips while seated on the ground.")]
        private float bodyForwardOnGround = 0.3f;

        [SerializeField, Range(0.1f, 0.9f), Tooltip("Feet in front of the hips on a seat.")]
        private float footForwardSeat = 0.42f;

        [SerializeField, Range(0.1f, 0.9f), Tooltip("Feet in front of the hips on the ground, knees up.")]
        private float footForwardKneesUp = 0.4f;

        [SerializeField, Range(0.3f, 1.2f), Tooltip("Feet in front of the hips on the ground, legs out.")]
        private float footForwardLegsOut = 0.82f;

        [SerializeField, Range(0f, 0.4f), Tooltip("Half the distance between the feet.")]
        private float footSpread = 0.13f;

        [SerializeField, Tooltip("Pose on the ground (bare ground, by a fire, on a doorstep).")]
        private SeatPoseKind groundPose = SeatPoseKind.GroundKneesUp;

        [Header("Getting there")]
        [SerializeField, Range(0.3f, 1.2f), Tooltip("Walking pace to the seat (x walk speed).")]
        private float approachSpeedScale = 0.7f;

        [SerializeField, Min(0.05f), Tooltip("Close enough to the standing point (m).")]
        private float approachTolerance = 0.2f;

        [SerializeField, Min(0.5f), Tooltip("Give up walking (props may block the last steps) and sit from where the body is after this many seconds.")]
        private float approachTimeout = 4f;

        [SerializeField, Min(0.5f), Tooltip("Turning sharpness (1/s) towards the view.")]
        private float turnSharpness = 5f;

        [SerializeField, Range(1f, 30f), Tooltip("Facing within this many degrees counts as turned.")]
        private float turnTolerance = 6f;

        [Header("Timing")]
        [SerializeField, Range(0.2f, 3f), Tooltip("Seconds to sit down (eased).")]
        private float sitSeconds = 0.8f;

        [SerializeField, Range(0.2f, 3f), Tooltip("Seconds to get up (eased).")]
        private float standSeconds = 0.8f;

        [SerializeField, Range(0f, 1f), Tooltip("Getting up: walking control returns once the pose weight has fallen below this.")]
        private float controlBackBelow = 0.35f;

        [Header("Camera while seated")]
        [SerializeField, Range(0.3f, 1f), Tooltip("Eye height above the hip joint when seated (m).")]
        private float eyeAboveHip = 0.64f;

        [SerializeField, Range(-0.2f, 0.4f), Tooltip("Eyes forward of the hips when seated (m).")]
        private float eyeForward = 0.12f;

        [SerializeField, Range(30f, 180f), Tooltip("First person seated: how far either side of the facing the view may turn (degrees).")]
        private float seatedYawRange = 110f;

        [SerializeField, Range(-89f, 0f), Tooltip("First person seated: highest look (degrees, negative = up).")]
        private float seatedMinPitch = -60f;

        [SerializeField, Range(0f, 89f), Tooltip("First person seated: lowest look (degrees).")]
        private float seatedMaxPitch = 70f;

        [Header("Time while seated")]
        [SerializeField, Range(1f, 60f), Tooltip("Holding Sprint while seated multiplies the flow of time by up to this.")]
        private float fastTimeMultiplier = 20f;

        [SerializeField, Range(0.2f, 5f), Tooltip("How quickly time speeds up and slows down again (1/s, in log space).")]
        private float fastTimeRate = 1.1f;

        [SerializeField, Min(0f), Tooltip("Seconds the seated hint stays on screen.")]
        private float hintSeconds = 6f;

        private PlayerMotor motor;
        private PlayerController controller;
        private PlayerCameraRig cameraRig;
        private PlayerTravel travel;
        private PlayerInteractor interactor;
        private CharacterController body;
        private readonly List<SeatCandidate> candidates = new List<SeatCandidate>(16);
        private readonly float[] viewScores = new float[16];
        private readonly Vector3[] viewDirections = new Vector3[16];
        private readonly RaycastHit[] hits = new RaycastHit[8];

        private SeatPlan plan;
        private float progress;
        private float phaseTimer;
        private Vector3 bodyAnchor;
        private bool tripPaused;
        private StandReason standReason;
        private bool limitsSet;
        private bool offsetApplied;
        private float timeFlow = 1f;
        private float hintTimer;
        private bool hintShown;
        private Vector3 lastApproachPosition;
        private float stillTimer;

        /// <summary>Current phase.</summary>
        public SeatPhase Phase { get; private set; }

        /// <summary>True from the sit key until the traveller has fully stood up again.</summary>
        public bool IsActive => Phase != SeatPhase.Idle;

        /// <summary>True while seated (sitting down, sat, not yet getting up).</summary>
        public bool IsSeated => Phase == SeatPhase.Seated;

        /// <summary>True while the seat (not the player's input) owns the body's movement.</summary>
        public bool HoldsBody => Phase == SeatPhase.Approaching || Phase == SeatPhase.Turning || Phase == SeatPhase.Seated ||
                                 (Phase == SeatPhase.Standing && Weight > controlBackBelow);

        /// <summary>Eased pose weight, 0 = standing, 1 = fully seated.</summary>
        public float Weight => SeatMath.Ease(progress);

        /// <summary>Seconds since the traveller finished sitting down (0 while not seated).</summary>
        public float SeatedTime { get; private set; }

        /// <summary>The current plan (valid while <see cref="IsActive"/>).</summary>
        public SeatPlan Plan => plan;

        /// <summary>Current multiplier on the flow of time (1 = normal).</summary>
        public float TimeFlow => timeFlow;

        // ------------------------------------------------------------------ public API

        /// <summary>The sit key: sit down (on a seat within reach, else the ground), or stand up when already sitting.</summary>
        public void ToggleSit()
        {
            if (IsActive)
            {
                StandUp(StandReason.Key);
                return;
            }

            TrySit(null);
        }

        /// <summary>Sits on <paramref name="seat"/> (Interact at a seat), or wherever is best when it cannot be used.</summary>
        public bool SitAt(SeatCandidate seat)
        {
            if (IsActive) return false;
            return TrySit(seat);
        }

        /// <summary>Gets up (eased); <paramref name="reason"/> decides what happens to a paused trip.</summary>
        public void StandUp(StandReason reason)
        {
            switch (Phase)
            {
                case SeatPhase.Idle:
                case SeatPhase.Standing:
                    if (Phase == SeatPhase.Standing && reason == StandReason.Moved) standReason = StandReason.Moved;
                    return;
                case SeatPhase.Approaching:
                case SeatPhase.Turning:
                    // Not sat yet: let go at once.
                    standReason = reason;
                    Finish();
                    return;
                default:
                    standReason = reason;
                    Phase = SeatPhase.Standing;
                    phaseTimer = 0f;
                    HideHint();
                    return;
            }
        }

        /// <summary>
        /// The best seat within reach of the traveller and the plan for it (the Interact prompt asks this). False when there is
        /// no usable seat; doorsteps are left to the inn's own prompt.
        /// </summary>
        public bool FindSeat(out SeatCandidate seat, out SeatPlan seatPlan)
        {
            seat = default;
            seatPlan = default;
            SeatIndex.Gather(transform.position, seatReach, false, candidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!TryPlan(candidates[i], out seatPlan)) continue;
                seat = candidates[i];
                return true;
            }

            return false;
        }

        /// <summary>True when the traveller could sit down now (grounded, no overlay, not busy).</summary>
        public bool CanSit(out string reason)
        {
            reason = null;
            if (motor == null || controller == null || !controller.isActiveAndEnabled) return false;
            if (controller.InputOverride != null) return false;
            if (interactor != null && interactor.Busy) return false;
            if (MedievalZen.Endless.EndlessWorld.Current != null && MedievalZen.Endless.EndlessWorld.Current.IsHolding) return false;
            if (!motor.IsGrounded)
            {
                reason = "Find firm ground to sit on";
                return false;
            }

            return true;
        }

        /// <summary>Ground height under a position: the world surface, else a ray down, else the body's own height.</summary>
        public float GroundAt(Vector3 p)
        {
            GameWorld world = GameWorld.Current;
            IWorldSurface surface = world != null ? world.Surface : null;
            if (surface != null && surface.HasGround) return surface.GroundHeight(p);
            int count = Physics.RaycastNonAlloc(new Ray(p + Vector3.up * 2f, Vector3.down), hits, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(transform)) continue;
                best = Mathf.Max(best, hits[i].point.y);
            }

            return float.IsNegativeInfinity(best) ? transform.position.y : best;
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            controller = GetComponent<PlayerController>();
            cameraRig = GetComponent<PlayerCameraRig>();
            travel = GetComponent<PlayerTravel>();
            body = GetComponent<CharacterController>();
            if (GetComponent<SeatInteractable>() == null) gameObject.AddComponent<SeatInteractable>();
        }

        private void Start()
        {
            interactor = GetComponent<PlayerInteractor>();
            SeatIndex.ScanWorld(GameWorld.Current);
        }

        private void OnDisable()
        {
            ResetNow();
            SetTimeFlow(1f, true);
        }

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            plan.Shift(delta);
            bodyAnchor += delta;
            lastApproachPosition += delta;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (Phase == SeatPhase.Idle)
            {
                StepTime(false, dt);
                if (offsetApplied) ApplyCamera();
                return;
            }

            // Moved by something else (fast travel, "Go now", a respawn): drop the seat at once.
            if (Phase != SeatPhase.Approaching && Planar(transform.position - bodyAnchor) > 1.5f)
            {
                ResetNow();
                return;
            }

            phaseTimer += dt;
            switch (Phase)
            {
                case SeatPhase.Approaching:
                    StepApproach(dt);
                    break;
                case SeatPhase.Turning:
                    StepTurn();
                    break;
                case SeatPhase.Seated:
                    progress = SeatMath.StepProgress(progress, true, sitSeconds, dt);
                    if (progress >= 1f) SeatedTime += dt;
                    StepHint(dt);
                    break;
                case SeatPhase.Standing:
                    progress = SeatMath.StepProgress(progress, false, standSeconds, dt);
                    if (progress <= 0f) Finish();
                    break;
            }

            StepTime(Phase == SeatPhase.Seated && progress >= 0.95f, dt);
            ApplyCamera();
            HudController hud = HudController.Instance;
            if (hud != null) hud.SeatedMode = Phase == SeatPhase.Seated || (Phase == SeatPhase.Standing && progress > 0.5f);
        }

        // ------------------------------------------------------------------ sitting down

        private bool TrySit(SeatCandidate? preferred)
        {
            if (!CanSit(out string reason))
            {
                if (reason != null) Toast(reason);
                return false;
            }

            SeatPlan chosen = default;
            bool found = false;
            if (preferred.HasValue && preferred.Value.IsValid) found = TryPlan(preferred.Value, out chosen);
            if (!found)
            {
                SeatIndex.Gather(transform.position, seatReach, doorstepsAreSeats, candidates);
                for (int i = 0; i < candidates.Count && !found; i++) found = TryPlan(candidates[i], out chosen);
            }

            if (!found)
            {
                if (!TryPlanGround(transform.position, CameraForward(), out chosen))
                {
                    Toast("Too steep to sit here");
                    return false;
                }
            }

            Begin(chosen);
            return true;
        }

        private void Begin(SeatPlan seatPlan)
        {
            plan = seatPlan;
            progress = 0f;
            phaseTimer = 0f;
            stillTimer = 0f;
            SeatedTime = 0f;
            standReason = StandReason.Key;
            lastApproachPosition = transform.position;
            bodyAnchor = transform.position;
            AutoTraveller traveller = travel != null ? travel.Traveller : null;
            tripPaused = traveller != null && traveller.IsActive;
            if (tripPaused) traveller.Paused = true;
            bool walk = Planar(plan.BodyPoint - transform.position) > approachTolerance;
            Phase = walk ? SeatPhase.Approaching : SeatPhase.Turning;
            HudController hud = HudController.Instance;
            if (hud != null) hud.HidePrompt();
        }

        private void StepApproach(float dt)
        {
            Vector3 position = transform.position;
            Vector3 toPoint = plan.BodyPoint - position;
            toPoint.y = 0f;
            float distance = toPoint.magnitude;

            // Blocked by the prop itself (they have colliders) or by anything else: note how long the body has not moved.
            stillTimer = Planar(position - lastApproachPosition) < 0.15f * dt ? stillTimer + dt : 0f;
            lastApproachPosition = position;

            if (distance <= approachTolerance || phaseTimer > approachTimeout || (phaseTimer > 0.6f && stillTimer > 0.5f))
            {
                // Sit from wherever the body ended up, if the seat is still within a stride; else on the ground right here.
                Vector3 hipFlat = plan.Hip - position;
                hipFlat.y = 0f;
                if (hipFlat.magnitude > 1.1f && !TryPlanGround(position, plan.Facing, out plan))
                {
                    Finish();
                    return;
                }

                plan.BodyPoint = new Vector3(position.x, plan.BodyPoint.y, position.z);
                Phase = SeatPhase.Turning;
                phaseTimer = 0f;
                bodyAnchor = position;
                return;
            }

            float pace = approachSpeedScale * Mathf.Clamp01(distance / 0.5f + 0.35f);
            motor.MoveFacing(toPoint / distance, pace, 8f);
        }

        private void StepTurn()
        {
            motor.Face(plan.Facing, turnSharpness);
            float off = Mathf.Abs(Mathf.DeltaAngle(motor.Yaw, SeatMath.YawOf(plan.Facing)));
            if (off > turnTolerance && phaseTimer < 2.5f) return;
            Phase = SeatPhase.Seated;
            phaseTimer = 0f;
            bodyAnchor = transform.position;
            ShowHint();
        }

        private void Finish()
        {
            SeatPhase was = Phase;
            Phase = SeatPhase.Idle;
            progress = 0f;
            SeatedTime = 0f;
            HideHint();
            HudController hud = HudController.Instance;
            if (hud != null) hud.SeatedMode = false;
            AutoTraveller traveller = travel != null ? travel.Traveller : null;
            if (tripPaused && traveller != null)
            {
                if (standReason == StandReason.Moved && travel != null) travel.Cancel();
                traveller.Paused = false;
            }
            else if (standReason == StandReason.Travel && travel != null && !travel.IsTravelling && was != SeatPhase.Idle)
            {
                travel.ToggleAutoTravel();
            }

            tripPaused = false;
            ApplyCamera();
        }

        /// <summary>Drops the seat this frame (no blend): the body was moved away, or this component is switched off.</summary>
        private void ResetNow()
        {
            if (Phase == SeatPhase.Idle && !offsetApplied) return;
            AutoTraveller traveller = travel != null ? travel.Traveller : null;
            if (tripPaused && traveller != null) traveller.Paused = false;
            tripPaused = false;
            Phase = SeatPhase.Idle;
            progress = 0f;
            SeatedTime = 0f;
            HideHint();
            HudController hud = HudController.Instance;
            if (hud != null) hud.SeatedMode = false;
            ApplyCamera();
        }

        // ------------------------------------------------------------------ planning

        private bool TryPlan(SeatCandidate seat, out SeatPlan result)
        {
            result = default;
            if (!seat.IsValid) return false;
            switch (seat.Kind)
            {
                case SeatKind.Fire:
                    return TryPlanFire(seat, out result);
                case SeatKind.Doorstep:
                    return TryPlanDoorstep(seat, out result);
                default:
                    return TryPlanProp(seat, out result);
            }
        }

        private bool TryPlanProp(SeatCandidate seat, out SeatPlan result)
        {
            result = default;
            Transform t = seat.Transform;
            Bounds b = seat.LocalBounds;
            Vector3 position = transform.position;

            // Seat top: the centre of the top face, or the nearest point along a log's axis.
            Vector3 top;
            Vector3 axis = Vector3.zero;
            if (seat.Kind == SeatKind.Log)
            {
                Vector3 scale = t.lossyScale;
                bool alongX = b.extents.x * Mathf.Abs(scale.x) >= b.extents.z * Mathf.Abs(scale.z);
                Vector3 half = alongX ? new Vector3(b.extents.x, 0f, 0f) : new Vector3(0f, 0f, b.extents.z);
                Vector3 a = t.TransformPoint(b.center - half);
                Vector3 c = t.TransformPoint(b.center + half);
                Vector3 onAxis = SeatMath.NearestOnAxis(position, a, c, 0.35f);
                float halfHeight = b.extents.y * Mathf.Abs(scale.y);
                top = onAxis + Vector3.up * halfHeight;
                axis = SeatMath.Planar(c - a, Vector3.forward);
            }
            else
            {
                top = t.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
                if (seat.Kind == SeatKind.Rock) top.y -= 0.03f;   // domed tops: sit a little into the curve
            }

            float ground = GroundAt(top);
            float height = top.y - ground;
            if (!SeatMath.IsSittingHeight(height, minSeatHeight, maxSeatHeight)) return false;
            if (seat.Kind == SeatKind.Rock)
            {
                Vector3 size = Vector3.Scale(b.size, t.lossyScale);
                float footprint = Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.z));
                if (footprint < 0.45f || footprint > 3.5f) return false;
            }

            // Facing: across a log (two ways), a chair's four sides, or any of sixteen directions; towards a fire when one is near.
            int count;
            if (seat.Kind == SeatKind.Log)
            {
                Vector3 across = CinematicRight(axis);
                viewDirections[0] = across;
                viewDirections[1] = -across;
                count = 2;
            }
            else if (seat.Kind == SeatKind.Chair)
            {
                Vector3 f = SeatMath.Planar(t.forward, Vector3.forward);
                Vector3 r = CinematicRight(f);
                viewDirections[0] = f;
                viewDirections[1] = -f;
                viewDirections[2] = r;
                viewDirections[3] = -r;
                count = 4;
            }
            else
            {
                count = FillCompass();
            }

            bool hasFire = SeatIndex.FindFire(top, 4.5f, out Vector3 fire);
            Vector3 hint = hasFire ? fire - top : CameraForward();
            float hintWeight = hasFire ? 3f : 0.35f;
            Vector3 facing = ChooseView(top + Vector3.up * 0.3f, count, hint, hintWeight, t);

            Vector3 hip = SeatMath.HipOnSeat(top, facing, hipAboveSeat, seat.Kind == SeatKind.Chair || seat.Kind == SeatKind.Crate ? 0f : seatForwardShift);
            result = new SeatPlan
            {
                Kind = seat.Kind,
                Pose = SeatPoseKind.Seat,
                Hip = hip,
                Facing = facing,
                SeatHeight = height,
                HasFire = hasFire,
                Fire = fire,
                Prop = t,
            };

            FinishPlan(ref result, footForwardSeat, bodyForwardOnSeat);
            // The feet must find ground at a sensible height in front of the seat (not a drop or a bank).
            return Mathf.Abs(result.FootLeft.y - ground) < 0.45f && Mathf.Abs(result.FootRight.y - ground) < 0.45f;
        }

        private bool TryPlanFire(SeatCandidate seat, out SeatPlan result)
        {
            Vector3 fire = SeatIndex.WorldCentre(seat);
            Vector3 spot = SeatMath.FireSeatPoint(fire, transform.position, fireRingRadius);
            Vector3 facing = SeatMath.Planar(fire - spot, Vector3.forward);
            if (!TryPlanGround(spot, facing, out result, true)) return false;
            result.Kind = SeatKind.Fire;
            result.HasFire = true;
            result.Fire = fire;
            return true;
        }

        private bool TryPlanDoorstep(SeatCandidate seat, out SeatPlan result)
        {
            Vector3 door = seat.Transform.position;
            int count = FillCompass();
            Vector3 hint = seat.Outward.sqrMagnitude > 0.1f ? seat.Outward : CameraForward();
            float weight = seat.Outward.sqrMagnitude > 0.1f ? 2f : 0.35f;
            Vector3 facing = ChooseView(new Vector3(door.x, GroundAt(door) + 0.75f, door.z), count, hint, weight, null);
            if (!TryPlanGround(door, facing, out result, true)) return false;
            result.Kind = SeatKind.Doorstep;
            return true;
        }

        /// <summary>A plan for sitting on the ground at <paramref name="at"/>, facing <paramref name="facing"/>; false on steep ground.</summary>
        private bool TryPlanGround(Vector3 at, Vector3 facing, out SeatPlan result, bool hipAtPoint = false)
        {
            result = default;
            facing = SeatMath.Planar(facing, transform.forward);
            const float span = 0.6f;
            float slope = SeatMath.SlopeDegrees(GroundAt(at - Vector3.right * span), GroundAt(at + Vector3.right * span),
                GroundAt(at - Vector3.forward * span), GroundAt(at + Vector3.forward * span), span);
            if (slope > maxGroundSlope) return false;

            // Standing here: the hips go a little behind the body; at a fire or doorstep the hips go on the spot itself.
            Vector3 hipFlat = hipAtPoint ? at : at - facing * bodyForwardOnGround;
            Vector3 hip = new Vector3(hipFlat.x, GroundAt(hipFlat) + hipAboveGround, hipFlat.z);
            bool hasFire = SeatIndex.FindFire(hip, 4.5f, out Vector3 fire);
            result = new SeatPlan
            {
                Kind = SeatKind.Ground,
                Pose = groundPose == SeatPoseKind.Seat ? SeatPoseKind.GroundKneesUp : groundPose,
                Hip = hip,
                Facing = facing,
                SeatHeight = 0f,
                HasFire = hasFire,
                Fire = fire,
            };

            float forward = result.Pose == SeatPoseKind.GroundLegsForward ? footForwardLegsOut : footForwardKneesUp;
            FinishPlan(ref result, forward, bodyForwardOnGround);
            return true;
        }

        private void FinishPlan(ref SeatPlan p, float footForward, float bodyForward)
        {
            Vector3 right = CinematicRight(p.Facing);
            Vector3 hipFlat = new Vector3(p.Hip.x, 0f, p.Hip.z);
            Vector3 left = hipFlat + p.Facing * footForward - right * footSpread;
            Vector3 rightFoot = hipFlat + p.Facing * footForward + right * footSpread;
            p.FootLeft = new Vector3(left.x, GroundAt(left), left.z);
            p.FootRight = new Vector3(rightFoot.x, GroundAt(rightFoot), rightFoot.z);
            Vector3 bodyPoint = SeatMath.ApproachPoint(p.Hip, p.Facing, bodyForward);
            p.BodyPoint = new Vector3(bodyPoint.x, GroundAt(bodyPoint), bodyPoint.z);
        }

        private int FillCompass()
        {
            for (int i = 0; i < 16; i++) viewDirections[i] = SeatMath.DirectionFromYaw(i * 22.5f);
            return 16;
        }

        /// <summary>Scores the first <paramref name="count"/> view directions from <paramref name="eye"/> and returns the best.</summary>
        private Vector3 ChooseView(Vector3 eye, int count, Vector3 hint, float hintWeight, Transform ignore)
        {
            GameWorld world = GameWorld.Current;
            IWorldSurface surface = world != null ? world.Surface : null;
            bool hasGround = surface != null && surface.HasGround;
            float here = GroundAt(eye);
            for (int i = 0; i < count; i++)
            {
                Vector3 d = viewDirections[i];
                float drop8 = 0f, drop25 = 0f, drop60 = 0f;
                bool water25 = false, water60 = false;
                if (hasGround)
                {
                    drop8 = here - surface.GroundHeight(eye + d * 8f);
                    Vector3 p25 = eye + d * 25f, p60 = eye + d * 60f;
                    float g25 = surface.GroundHeight(p25), g60 = surface.GroundHeight(p60);
                    drop25 = here - g25;
                    drop60 = here - g60;
                    float w25 = surface.WaterHeight(p25), w60 = surface.WaterHeight(p60);
                    water25 = w25 >= 0f && w25 > g25;
                    water60 = w60 >= 0f && w60 > g60;
                }

                viewScores[i] = SeatMath.ViewScore(drop8, drop25, drop60, water25, water60, Obstruction(eye, d, ignore));
            }

            return viewDirections[SeatMath.ChooseFacing(viewScores, viewDirections, count, hint, hintWeight)];
        }

        /// <summary>Distance to the first solid thing at eye level along <paramref name="direction"/> (ignoring the player and the seat's own base).</summary>
        private float Obstruction(Vector3 eye, Vector3 direction, Transform ignore)
        {
            int count = Physics.RaycastNonAlloc(new Ray(eye, direction), hits, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;
                // A chair's or bench's own back counts (it decides the facing); other seats' bodies do not.
                if (ignore != null && c.transform.IsChildOf(ignore) && hits[i].distance < 0.2f) continue;
                nearest = Mathf.Min(nearest, hits[i].distance);
            }

            return nearest;
        }

        // ------------------------------------------------------------------ camera, time, HUD

        private void ApplyCamera()
        {
            if (cameraRig == null) return;
            float w = Weight;
            bool active = Phase != SeatPhase.Idle && w > 0f;
            if (active)
            {
                Vector3 eye = plan.Hip + Vector3.up * eyeAboveHip + plan.Facing * eyeForward;
                cameraRig.AnchorOffset = (eye - cameraRig.StandingEyePosition) * w;
                offsetApplied = true;
            }
            else if (offsetApplied)
            {
                cameraRig.AnchorOffset = Vector3.zero;
                offsetApplied = false;
            }

            bool limit = Phase == SeatPhase.Seated && w > 0.5f && cameraRig.Mode == CameraMode.FirstPerson;
            if (limit && !limitsSet)
            {
                cameraRig.SetLookLimits(SeatMath.YawOf(plan.Facing), seatedYawRange, seatedMinPitch, seatedMaxPitch);
                limitsSet = true;
            }
            else if (!limit && limitsSet)
            {
                cameraRig.ClearLookLimits();
                limitsSet = false;
            }
        }

        private void StepTime(bool seated, float dt)
        {
            bool fast = seated && controller != null && controller.isActiveAndEnabled && controller.InputOverride == null && controller.SprintHeld;
            float target = fast ? fastTimeMultiplier : 1f;
            if (Mathf.Approximately(timeFlow, target) && Mathf.Approximately(target, 1f)) return;
            SetTimeFlow(SeatMath.StepTimeFlow(timeFlow, target, fastTimeRate, Time.unscaledDeltaTime), false);
        }

        private void SetTimeFlow(float value, bool force)
        {
            if (!force && Mathf.Approximately(value, timeFlow)) return;
            timeFlow = value;
            DayCycle day = DayCycle.Current;
            if (day != null) day.FastForward = value;
            HudController hud = HudController.Instance;
            if (hud != null) hud.ForceClock = value > 1.5f;
        }

        private void ShowHint()
        {
            HudController hud = HudController.Instance;
            if (hud == null || hintSeconds <= 0f) return;
            hud.ShowPrompt(DayCycle.Current != null ? "Hold Shift — let time pass   ·   C — stand up" : "C — stand up");
            hintShown = true;
            hintTimer = hintSeconds;
        }

        private void StepHint(float dt)
        {
            if (!hintShown) return;
            hintTimer -= dt;
            if (hintTimer <= 0f) HideHint();
        }

        private void HideHint()
        {
            if (!hintShown) return;
            hintShown = false;
            HudController hud = HudController.Instance;
            if (hud != null && (controller == null || controller.InputOverride == null)) hud.HidePrompt();
        }

        private static void Toast(string text)
        {
            HudController hud = HudController.Instance;
            if (hud != null) hud.ShowToast(text);
        }

        private Vector3 CameraForward() => cameraRig != null ? cameraRig.PlanarForward : transform.forward;

        private static Vector3 CinematicRight(Vector3 forward) => new Vector3(forward.z, 0f, -forward.x);

        private static float Planar(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);
    }
}
