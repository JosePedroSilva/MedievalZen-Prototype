using System;
using MedievalZen.Player;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// "Walking on autopilot": follows a <see cref="RoadRoute"/> by feeding <see cref="PlayerMotor.Move"/>
    /// and <see cref="PlayerMotor.Face"/> each frame, exactly like manual input does. The camera stays
    /// free to look around; the body faces the road, not the camera.
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

        [SerializeField, Min(0.1f), Tooltip("Body turn sharpness (1/s) while travelling.")]
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
            approachTarget = roadPoint;
            phase = 0f;
            nextReprojectTime = 0f;
            ResetStuck();
            Started?.Invoke(route);
        }

        /// <summary>Stops travelling (the body eases to a halt). Does nothing when inactive.</summary>
        public void Cancel()
        {
            if (follower == null) return;
            RoadRoute route = follower.Route;
            follower = null;
            approaching = false;
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

        private void OnDisable()
        {
            Cancel();
        }

        private void Update()
        {
            if (follower == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 body = transform.position;

            // Not making progress (blocked by a rock, a wall, a steep bank): resync once, then give up.
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
                    motor.Move(heading, travelSpeed / Mathf.Max(0.01f, motor.WalkSpeed));
                    motor.Face(heading, turnSpeed);
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

            Vector3 steer = Flatten(follower.LookAhead(lookAhead) - body);
            float steerLength = steer.magnitude;
            if (steerLength < 1e-4f) steer = Flatten(follower.Tangent);
            else steer /= steerLength;

            phase += dt * (2f * Mathf.PI / variationPeriod);
            if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;
            float speed = travelSpeed * (1f + speedVariation * Mathf.Sin(phase));
            float ease = Mathf.Clamp(Mathf.Min(follower.Remaining, toEnd.magnitude) / arrivalSlowdown, 0.35f, 1f);

            motor.Move(steer, speed * ease / Mathf.Max(0.01f, motor.WalkSpeed));
            motor.Face(steer, turnSpeed);
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
