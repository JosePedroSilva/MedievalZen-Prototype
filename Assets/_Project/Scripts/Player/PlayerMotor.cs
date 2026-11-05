using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>
    /// The single locomotion path of the player: a <see cref="CharacterController"/> driven by whoever
    /// is in charge this frame (manual input or the auto-traveller). Drivers call once per frame from <c>Update</c>
    /// either <see cref="MoveFacing"/> (third person, auto-travel: the body turns towards the request at a bounded
    /// rate and walks where it faces, never sliding backwards) or <see cref="Move"/> + <see cref="Face"/> (first person:
    /// velocity free to strafe, yaw set separately). The motor applies the movement afterwards (it runs late in the
    /// frame via <see cref="DefaultExecutionOrder"/>); the maths lives in <see cref="MotorMath"/>.
    /// If nobody asks in a frame, the motor eases to a stop along its current heading.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Speed")]
        [SerializeField, Min(0f), Tooltip("Walking speed in m/s at full input (direction magnitude 1, speed scale 1).")]
        private float walkSpeed = 1.6f;

        [SerializeField, Min(1f), Tooltip("Speed scale drivers pass while sprinting (walk speed x this).")]
        private float sprintScale = 2.2f;

        [SerializeField, Min(0.01f), Tooltip("How quickly the speed builds up towards the requested speed, in m/s^2.")]
        private float acceleration = 5f;

        [SerializeField, Min(0.01f), Tooltip("How quickly the speed drops when input stops or points away, in m/s^2.")]
        private float deceleration = 6f;

        [Header("Direction changes")]
        [SerializeField, Min(10f), Tooltip("Face-movement (third person, auto-travel): fastest body turn in degrees per second. Small corrections ease in with the driver's sharpness; large ones pivot at this rate.")]
        private float maxTurnRate = 300f;

        [SerializeField, Range(0f, 180f), Tooltip("Face-movement: a request up to this many degrees off the facing keeps full speed.")]
        private float fullSpeedAngle = 35f;

        [SerializeField, Range(0f, 180f), Tooltip("Face-movement: a request this many degrees or more off the facing asks for zero speed, so a reversal eases off and pivots instead of sliding backwards.")]
        private float zeroSpeedAngle = 155f;

        [SerializeField, Min(10f), Tooltip("Face-movement: degrees per second at which the velocity swings round to the body's forward (kept above the max turn rate).")]
        private float velocityTurnRate = 720f;

        [SerializeField, Min(10f), Tooltip("Strafe (first person): degrees per second at which the velocity swings towards a new direction.")]
        private float strafeTurnRate = 540f;

        [SerializeField, Range(90f, 180f), Tooltip("Strafe: a new direction further than this from the current velocity brakes and restarts instead of swinging round.")]
        private float strafeReverseAngle = 125f;

        [Header("Gravity & ground")]
        [SerializeField, Tooltip("Gravity acceleration in m/s^2 (negative = down).")]
        private float gravity = -19.6f;

        [SerializeField, Min(0f), Tooltip("Downward speed applied while grounded so the capsule keeps touching the ground.")]
        private float groundStickSpeed = 2f;

        [SerializeField, Min(0f), Tooltip("After leaving the ground while walking (crest, downhill), snap back down to ground within this distance instead of bouncing.")]
        private float groundSnapDistance = 0.45f;

        [SerializeField, Range(0f, 89f), Tooltip("Steepest walkable slope in degrees (applied to the CharacterController).")]
        private float slopeLimit = 45f;

        [SerializeField, Tooltip("Layers the ground snap treats as walkable ground.")]
        private LayerMask groundLayers = ~0;

        private CharacterController controller;
        private Vector2 requestedVelocity;     // planar (x, z) m/s for this frame
        private bool hasRequest;
        private MoveStyle requestStyle = MoveStyle.FaceMovement;
        private float requestTurnSharpness = 10f;
        private MotorState state;
        private float verticalSpeed;

        /// <summary>Walking speed in m/s at full input.</summary>
        public float WalkSpeed => walkSpeed;

        /// <summary>Speed scale to pass to <see cref="Move"/> while sprinting.</summary>
        public float SprintScale => sprintScale;

        /// <summary>Actual velocity of the last movement step (m/s), including vertical motion.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Horizontal speed of the last movement step (m/s).</summary>
        public float PlanarSpeed => Mathf.Sqrt(Velocity.x * Velocity.x + Velocity.z * Velocity.z);

        /// <summary>True when the capsule touched the ground during the last movement step.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>Current body yaw in degrees, (-180, 180].</summary>
        public float Yaw => state.Yaw;

        /// <summary>Unit forward vector of the body on the horizontal plane.</summary>
        public Vector3 Forward
        {
            get
            {
                Vector2 forward = MotorMath.FromYaw(state.Yaw);
                return new Vector3(forward.x, 0f, forward.y);
            }
        }

        /// <summary>
        /// Horizontal velocity of the last movement step in body space (m/s): x = rightwards, y = forwards. What the
        /// animation should show: negative y is a backward step, x a sidestep.
        /// </summary>
        public Vector2 LocalPlanarVelocity => MotorMath.LocalVelocity(new Vector2(Velocity.x, Velocity.z), state.Yaw);

        /// <summary>The direction-change tuning currently in use.</summary>
        public MotorTuning Tuning => new MotorTuning
        {
            Acceleration = acceleration,
            Deceleration = deceleration,
            MaxTurnRate = maxTurnRate,
            FullSpeedAngle = fullSpeedAngle,
            ZeroSpeedAngle = Mathf.Max(fullSpeedAngle + 1f, zeroSpeedAngle),
            VelocityTurnRate = Mathf.Max(velocityTurnRate, maxTurnRate + 60f),
            StrafeTurnRate = strafeTurnRate,
            StrafeReverseAngle = strafeReverseAngle,
        };

        /// <summary>
        /// Strafe-style request for this frame (first person): the velocity steers towards <paramref name="worldDirection"/>
        /// (flattened; magnitude clamped to 0..1 = fraction of walk speed) while the facing is left to <see cref="Face"/>.
        /// <paramref name="speedScale"/> multiplies the walk speed (1 = walk, <see cref="SprintScale"/> = sprint).
        /// Call once per frame; the last Move/MoveFacing call in a frame wins.
        /// </summary>
        public void Move(Vector3 worldDirection, float speedScale = 1f)
        {
            Request(worldDirection, speedScale, MoveStyle.Strafe, requestTurnSharpness);
        }

        /// <summary>
        /// Face-movement request for this frame (third person, auto-travel): the body turns towards
        /// <paramref name="worldDirection"/> (exponentially with <paramref name="turnSharpness"/>, 1/s, for small changes;
        /// at most the motor's max turn rate for large ones) and walks where it faces, slowing while the request is far
        /// off its facing. Magnitude and <paramref name="speedScale"/> as in <see cref="Move"/>. A zero direction keeps the
        /// facing and eases to a stop. Call once per frame; the last Move/MoveFacing call in a frame wins.
        /// </summary>
        public void MoveFacing(Vector3 worldDirection, float speedScale, float turnSharpness)
        {
            Request(worldDirection, speedScale, MoveStyle.FaceMovement, turnSharpness);
        }

        /// <summary>
        /// Riding request for this frame (the traveller on a horse, manual or auto-travel): like <see cref="MoveFacing"/> but at
        /// <paramref name="metresPerSecond"/> (the gait's speed; the walk speed and the pace do not apply) and with the horse's turn
        /// (<paramref name="turn"/>, <see cref="RidingMath"/>). Call once per frame; the last request in a frame wins.
        /// </summary>
        public void MoveRiding(Vector3 worldDirection, float metresPerSecond, float turnSharpness, in RideTurn turn)
        {
            Request(worldDirection, Mathf.Max(0f, metresPerSecond) / Mathf.Max(0.01f, walkSpeed), MoveStyle.Riding, turnSharpness);
            rideTurn = turn;
        }

        private RideTurn rideTurn = RideTurn.Default;

        private void Request(Vector3 worldDirection, float speedScale, MoveStyle style, float turnSharpness)
        {
            var planar = new Vector2(worldDirection.x, worldDirection.z);
            float magnitude = planar.magnitude;
            if (magnitude > 1f) planar /= magnitude;

            requestedVelocity = planar * (walkSpeed * Mathf.Max(0f, speedScale));
            requestStyle = style;
            requestTurnSharpness = Mathf.Max(0f, turnSharpness);
            hasRequest = true;
        }

        /// <summary>
        /// Smoothly turns the body towards <paramref name="direction"/> (flattened; ignored when near zero).
        /// <paramref name="turnSpeed"/> is an exponential sharpness in 1/s: about 5 is relaxed, 20+ near-instant.
        /// Meant for strafe-style movement (first person); <see cref="MoveFacing"/> turns the body itself.
        /// Call every frame the body should keep turning.
        /// </summary>
        public void Face(Vector3 direction, float turnSpeed)
        {
            var planar = new Vector2(direction.x, direction.z);
            if (planar.sqrMagnitude < 1e-6f) return;

            state.Yaw = MotorMath.FollowYaw(state.Yaw, MotorMath.YawOf(planar), turnSpeed, Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, state.Yaw, 0f);
        }

        /// <summary>Moves the body instantly (ignoring collisions) and resets its velocity.</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            EnsureController();
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            state = new MotorState { Yaw = LocomotionMath.WrapAngle(yawDegrees) };
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, state.Yaw, 0f));
            controller.enabled = wasEnabled;

            requestedVelocity = Vector2.zero;
            hasRequest = false;
            verticalSpeed = 0f;
            Velocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private void Awake()
        {
            EnsureController();
            controller.slopeLimit = slopeLimit;
            state.Yaw = LocomotionMath.WrapAngle(transform.eulerAngles.y);
        }

        private void OnValidate()
        {
            if (controller != null) controller.slopeLimit = slopeLimit;
        }

        private void EnsureController()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("PlayerMotor");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            float dt = Time.deltaTime;
            if (dt <= 0f || !controller.enabled) return;

            // Facing and velocity advance together (MotorMath): face-movement turns the body and walks where it faces;
            // strafe steers the velocity only. Without a request this frame the body eases to a stop along its heading.
            MotorTuning tuning = Tuning;
            if (!hasRequest)
            {
                state.Velocity = MotorMath.SteerVelocity(state.Velocity, Vector2.zero, 0f, 0f, 180f, tuning.Acceleration, tuning.Deceleration, dt);
            }
            else if (requestStyle == MoveStyle.FaceMovement)
            {
                float previousYaw = state.Yaw;
                MotorMath.StepFaceMovement(ref state, requestedVelocity, requestTurnSharpness, tuning, dt);
                if (state.Yaw != previousYaw) transform.rotation = Quaternion.Euler(0f, state.Yaw, 0f);
            }
            else if (requestStyle == MoveStyle.Riding)
            {
                float previousYaw = state.Yaw;
                RidingMath.Step(ref state, requestedVelocity, requestTurnSharpness, rideTurn, tuning, dt);
                if (state.Yaw != previousYaw) transform.rotation = Quaternion.Euler(0f, state.Yaw, 0f);
            }
            else
            {
                MotorMath.StepStrafe(ref state, requestedVelocity, tuning, dt);
            }

            requestedVelocity = Vector2.zero;   // consumed: a driver must ask again next frame
            hasRequest = false;
            var planarVelocity = new Vector3(state.Velocity.x, 0f, state.Velocity.y);

            bool wasGrounded = controller.isGrounded;
            if (wasGrounded && verticalSpeed <= 0f)
            {
                verticalSpeed = -groundStickSpeed;
            }
            else
            {
                verticalSpeed += gravity * dt;
            }

            Vector3 start = transform.position;
            CollisionFlags flags = controller.Move((planarVelocity + Vector3.up * verticalSpeed) * dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
            {
                verticalSpeed = 0f;
            }

            // Walking over a crest or downhill faster than the stick speed leaves the ground for a
            // frame and makes the capsule bounce; snap back down when ground is close below.
            if (wasGrounded && !controller.isGrounded && verticalSpeed <= 0f)
            {
                SnapToGround();
            }

            IsGrounded = controller.isGrounded;
            Velocity = (transform.position - start) / dt;
        }

        private void SnapToGround()
        {
            float radius = controller.radius;
            Vector3 origin = transform.position + controller.center;
            float toBottom = Mathf.Max(0f, controller.height * 0.5f - radius);

            // A cast that starts inside our own capsule does not report it, so no self-filtering is needed.
            if (Physics.SphereCast(origin, radius * 0.95f, Vector3.down, out RaycastHit hit,
                    toBottom + controller.skinWidth + groundSnapDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                float drop = hit.distance - toBottom - controller.skinWidth;
                if (drop > 0f && Vector3.Angle(hit.normal, Vector3.up) <= slopeLimit + 1f)
                {
                    controller.Move(Vector3.down * (drop + 0.01f));
                    verticalSpeed = -groundStickSpeed;
                }
            }
        }
    }
}
