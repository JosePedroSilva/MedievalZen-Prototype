using System;
using MedievalZen.Player;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// "Walking on autopilot": follows a <see cref="RoadRoute"/> by feeding <see cref="PlayerMotor.MoveFacing"/>
    /// each frame, exactly like third-person manual input does (the body turns towards the road ahead at a bounded
    /// rate and walks where it faces). The camera stays free to look around; the body faces the road, not the camera.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class AutoTraveller : MonoBehaviour
    {
        [SerializeField, Min(0.1f), Tooltip("Average travel speed in m/s.")]
        private float travelSpeed = 1.4f;

        [SerializeField, Range(0f, 0.5f), Tooltip("Relative amplitude of the slow speed variation (0.08 = +/-8 %).")]
        private float speedVariation = 0.08f;

        [SerializeField, Min(0.5f), Tooltip("Period in seconds of the slow speed variation.")]
        private float variationPeriod = 9f;

        [SerializeField, Min(0.5f), Tooltip("Steer towards the route point this many metres ahead; larger values round off curves more.")]
        private float lookAhead = 2.5f;

        [SerializeField, Min(0.5f), Tooltip("If the body ends up further than this from its route point (pushed, blocked), resync to the nearest point of the route.")]
        private float reprojectDistance = 3f;

        [SerializeField, Min(0f), Tooltip("Minimum seconds between two resyncs.")]
        private float reprojectInterval = 0.5f;

        [SerializeField, Min(0.1f), Tooltip("Body turn sharpness (1/s) while travelling (large turns are capped by the motor's max turn rate).")]
        private float turnSpeed = 4f;

        [SerializeField, Min(0.1f), Tooltip("Ease off over this many metres before the destination.")]
        private float arrivalSlowdown = 2f;

        [SerializeField, Min(0.01f), Tooltip("Travel ends when the body is within this many metres of the route end.")]
        private float arrivalTolerance = 0.4f;

        [Header("Off-road approach & stuck detection")]
        [SerializeField, Min(0.1f), Tooltip("An off-road approach leg ends (and road following begins) within this many metres of the road point.")]
        private float approachArrivalDistance = 1f;

        [SerializeField, Min(0f), Tooltip("Planar speed in m/s below which the traveller counts as stuck.")]
        private float stuckSpeed = 0.2f;

        [SerializeField, Min(0.5f), Tooltip("Seconds below the stuck speed before reprojecting once, then giving up.")]
        private float stuckSeconds = 3f;

        private PlayerMotor motor;
        private RouteFollower follower;
        private float phase;
        private float nextReprojectTime;
        private readonly StuckDetector stuck = new StuckDetector();
        private bool approaching;
        private Vector3 approachTarget;

        /// <summary>Raised when travel starts along a route.</summary>
        public event Action<RoadRoute> Started;

        /// <summary>Raised when the end of the route is reached.</summary>
        public event Action<RoadRoute> Arrived;

        /// <summary>Raised when travel is cancelled before arriving.</summary>
        public event Action<RoadRoute> Cancelled;

        /// <summary>Raised (just before <see cref="Cancelled"/>) when travel gives up because the body stopped making progress.</summary>
        public event Action<RoadRoute> Blocked;

        /// <summary>True while walking the straight off-road approach leg towards the start of the route.</summary>
        public bool IsApproaching => follower != null && approaching;

        /// <summary>Metres left: the rest of the route plus, while approaching, the straight leg to the road (0 when inactive).</summary>
        public float RemainingDistance
        {
            get
            {
                if (follower == null) return 0f;
                float remaining = follower.Remaining;
                if (approaching) remaining += Flatten(approachTarget - transform.position).magnitude;
                return remaining;
            }
        }

        /// <summary>True while following a route.</summary>
        public bool IsActive => follower != null;

        /// <summary>
        /// While true an active trip is held where it is (the traveller sat down): the motor is not driven, the stuck detector
        /// does not run, the route is kept. Cleared by <see cref="Begin(RoadRoute)"/> and <see cref="Cancel"/>.
        /// </summary>
        public bool Paused { get; set; }

        /// <summary>
        /// Review tool only (the walking benchmark, <c>MedievalZen/Review/Walk Benchmark</c>): multiplies the travel pace, and the
        /// steering look-ahead by its square root so the faster body does not weave. Always 1 in play; the benchmark sets it and
        /// puts it back to 1 when it ends, is stopped, or Play mode ends. Nothing else reads it.
        /// </summary>
        public float DebugSpeedScale { get; set; } = 1f;

        /// <summary>
        /// The player's walking pace (<see cref="WalkPace"/>, set by <see cref="PlayerController.Pace"/>): multiplies the travel
        /// speed, variation included. Ignored while <see cref="DebugSpeedScale"/> is not 1, so benchmarks stay comparable.
        /// </summary>
        public float Pace { get; set; } = WalkPace.Default;

        /// <summary>Average travel speed in m/s at pace 1 (the inspector value).</summary>
        public float TravelSpeed => travelSpeed;

        private float SpeedScale => WalkPace.TravelScale(Pace, DebugSpeedScale);

        private float LookAheadNow => DebugSpeedScale > 1f ? lookAhead * Mathf.Sqrt(DebugSpeedScale) : lookAhead;

        /// <summary>The route being followed, or <c>null</c>.</summary>
        public RoadRoute Route => follower?.Route;

        /// <summary>Progress along the current route, or <c>null</c> when inactive.</summary>
        public RouteFollower Follower => follower;

        /// <summary>Starts following <paramref name="route"/> from the point nearest to the body (replaces any current route).</summary>
        public void Begin(RoadRoute route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            follower = new RouteFollower(route);
            follower.Reproject(transform.position);
            approaching = false;
            Paused = false;
            phase = 0f;
            nextReprojectTime = 0f;
            ResetStuck();
            Started?.Invoke(route);
        }

        /// <summary>
        /// Starts with a straight approach leg from the body to <paramref name="roadPoint"/> (the route start, projected onto
        /// the road), driven through the motor like the rest of the trip, then follows <paramref name="route"/> from there.
        /// </summary>
        public void Begin(RoadRoute route, Vector3 roadPoint)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            follower = new RouteFollower(route);
            approaching = true;
            Paused = false;
            approachTarget = roadPoint;
            phase = 0f;
            nextReprojectTime = 0f;
            ResetStuck();
            Started?.Invoke(route);
        }

        /// <summary>
        /// Swaps the route of an ongoing trip for <paramref name="route"/> (the same journey in a rebuilt road network) without
        /// raising <see cref="Started"/> and without resetting the pace variation or the stuck detector, so the walk carries on
        /// seamlessly. <paramref name="originDelta"/> is how far every scene position just moved (floating-origin shift), applied
        /// to the off-road approach target. Does nothing when inactive.
        /// </summary>
        public void ContinueOn(RoadRoute route, Vector3 originDelta)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (follower == null) return;
            follower = new RouteFollower(route);
            follower.Reproject(transform.position);
            if (approaching) approachTarget += originDelta;
        }

        /// <summary>Stops travelling (the body eases to a halt). Does nothing when inactive.</summary>
        public void Cancel()
        {
            if (follower == null) return;
            RoadRoute route = follower.Route;
            follower = null;
            approaching = false;
            Paused = false;
            Cancelled?.Invoke(route);
        }

        private void ResetStuck()
        {
            stuck.MinSpeed = stuckSpeed;
            stuck.StuckSeconds = stuckSeconds;
            stuck.RecoverSeconds = stuckSeconds;
            stuck.Reset();
        }

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
        }

        private HorseMount mount;

        /// <summary>True when this frame's drive uses the horse's gait and turn (riding, and no benchmark scale).</summary>
        private bool RidesNow
        {
            get
            {
                if (mount == null) mount = HorseMount.Current != null && HorseMount.Current.gameObject == gameObject ? HorseMount.Current : null;
                return mount != null && HorseGaits.TravelRides(mount.Riding, DebugSpeedScale);
            }
        }

        /// <summary>
        /// Average speed (m/s) before the slow variation: on foot exactly travel speed × <see cref="SpeedScale"/> as before; riding, the
        /// gait's speed; the benchmark's scale overrides both (<see cref="HorseGaits.TravelSpeed"/>).
        /// </summary>
        private float BaseSpeed => HorseGaits.TravelSpeed(mount != null && mount.Riding, mount != null ? mount.Gait : HorseGait.Walk, travelSpeed, Pace, DebugSpeedScale);

        /// <summary>The one motor request of a frame: the riding turn on horseback, the walker's face-movement otherwise (unchanged).</summary>
        private void Drive(Vector3 direction, float metresPerSecond, bool riding)
        {
            if (riding) motor.MoveRiding(direction, metresPerSecond, turnSpeed, mount.Turn);
            else motor.MoveFacing(direction, metresPerSecond / Mathf.Max(0.01f, motor.WalkSpeed), turnSpeed);
        }

        private void OnDisable()
        {
            Cancel();
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("AutoTraveller");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (follower == null || Paused) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            bool riding = RidesNow;
            if (mount != null && mount.HoldsBody) return;   // getting on or off: the mount holds the body

            Vector3 body = transform.position;

            // Not making progress (blocked by a rock, a wall, a steep bank): resync once, then give up. A slow pace lowers the
            // threshold with it (the slowest pace near arrival is barely above 0.2 m/s); at pace 1 or faster it is unchanged.
            stuck.MinSpeed = stuckSpeed * Mathf.Min(1f, SpeedScale);
            switch (stuck.Update(dt, motor.PlanarSpeed))
            {
                case StuckAction.Reproject:
                    approaching = false;
                    follower.Reproject(body);
                    nextReprojectTime = Time.time + reprojectInterval;
                    break;
                case StuckAction.GiveUp:
                    RoadRoute blocked = follower.Route;
                    follower = null;
                    approaching = false;
                    Blocked?.Invoke(blocked);
                    Cancelled?.Invoke(blocked);
                    return;
            }

            if (approaching)
            {
                Vector3 toRoad = Flatten(approachTarget - body);
                float gap = toRoad.magnitude;
                if (gap > approachArrivalDistance)
                {
                    Vector3 heading = toRoad / gap;
                    Drive(heading, BaseSpeed, riding);
                    return;
                }

                approaching = false;
                follower.Reproject(body);
            }

            // Track the body's progress: advance the route point by how far the body has moved past it
            // along the road direction (converges on the projection within a frame or two).
            Vector3 offset = Flatten(body - follower.Position);
            float ahead = Vector3.Dot(offset, Flatten(follower.Tangent));
            if (ahead > 0f) follower.Advance(ahead);

            // At a hairpin the body can cut the corner and reach the point it steers for while still "behind" on the
            // first leg; move the route point on so the steering target stays ahead instead of circling on the spot.
            float look = LookAheadNow;
            if (riding) look = Mathf.Max(look, HorseGaits.TurnRadius(BaseSpeed, mount.Turn) + 1f);   // a wider-turning horse steers further ahead
            follower.AdvanceWhileLookAheadWithin(body, look, look * 0.5f);

            // Pushed off the road or snagged: resync to the nearest point of the route (rate-limited).
            if (Time.time >= nextReprojectTime && Flatten(body - follower.Position).sqrMagnitude > reprojectDistance * reprojectDistance)
            {
                follower.Reproject(body);
                nextReprojectTime = Time.time + reprojectInterval;
            }

            Vector3 toEnd = Flatten(follower.Route.Evaluate(follower.TotalLength) - body);
            if (follower.Remaining <= arrivalTolerance && toEnd.magnitude <= arrivalTolerance + 0.5f)
            {
                follower.Advance(follower.Remaining);
            }

            if (follower.IsFinished)
            {
                RoadRoute route = follower.Route;
                follower = null;
                Arrived?.Invoke(route);
                return;
            }

            Vector3 steer = Flatten(follower.LookAhead(look) - body);
            float steerLength = steer.magnitude;
            if (steerLength < 1e-4f) steer = Flatten(follower.Tangent);
            else steer /= steerLength;

            phase += dt * (2f * Mathf.PI / variationPeriod);
            if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;
            float speed = BaseSpeed * (1f + speedVariation * Mathf.Sin(phase));
            float ease = Mathf.Clamp(Mathf.Min(follower.Remaining, toEnd.magnitude) / arrivalSlowdown, 0.35f, 1f);

            Drive(steer, speed * ease, riding);
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
