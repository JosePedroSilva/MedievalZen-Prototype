using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>
    /// The single locomotion path of the player: a <see cref="CharacterController"/> driven by whoever
    /// is in charge this frame (manual input or the auto-traveller). Drivers call <see cref="Move"/>
    /// (and optionally <see cref="Face"/>) once per frame from <c>Update</c>; the motor applies the
    /// movement afterwards (it runs late in the frame via <see cref="DefaultExecutionOrder"/>).
    /// If nobody calls <see cref="Move"/> in a frame, the motor eases to a stop.
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

        [SerializeField, Min(0.01f), Tooltip("How quickly the planar velocity builds up towards the requested velocity, in m/s^2.")]
        private float acceleration = 5f;

        [SerializeField, Min(0.01f), Tooltip("How quickly the planar velocity drops when input stops or turns away, in m/s^2.")]
        private float deceleration = 6f;

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
        private Vector3 requestedVelocity;
        private Vector3 planarVelocity;
        private float verticalSpeed;
        private float yaw;

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

        /// <summary>Current body yaw in degrees.</summary>
        public float Yaw => yaw;

        /// <summary>Unit forward vector of the body on the horizontal plane.</summary>
        public Vector3 Forward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        /// <summary>
        /// Requests planar movement for this frame. <paramref name="worldDirection"/> is flattened onto
        /// the horizontal plane; its magnitude (clamped to 0..1) is the fraction of walk speed.
        /// <paramref name="speedScale"/> multiplies the walk speed (1 = walk, <see cref="SprintScale"/> = sprint).
        /// Call once per frame; the last call in a frame wins.
        /// </summary>
        public void Move(Vector3 worldDirection, float speedScale = 1f)
        {
            worldDirection.y = 0f;
            float magnitude = worldDirection.magnitude;
            if (magnitude > 1f)
            {
                worldDirection /= magnitude;
            }

            requestedVelocity = worldDirection * (walkSpeed * Mathf.Max(0f, speedScale));
        }

        /// <summary>
        /// Smoothly turns the body towards <paramref name="direction"/> (flattened; ignored when near zero).
        /// <paramref name="turnSpeed"/> is an exponential sharpness in 1/s: about 5 is relaxed, 20+ near-instant.
        /// Call every frame the body should keep turning.
        /// </summary>
        public void Face(Vector3 direction, float turnSpeed)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;

            float target = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float t = 1f - Mathf.Exp(-Mathf.Max(0f, turnSpeed) * Time.deltaTime);
            yaw = Mathf.LerpAngle(yaw, target, t);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Moves the body instantly (ignoring collisions) and resets its velocity.</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            EnsureController();
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            yaw = yawDegrees;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = wasEnabled;

            requestedVelocity = Vector3.zero;
            planarVelocity = Vector3.zero;
            verticalSpeed = 0f;
            Velocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private void Awake()
        {
            EnsureController();
            controller.slopeLimit = slopeLimit;
            yaw = transform.eulerAngles.y;
        }

        private void OnValidate()
        {
            if (controller != null) controller.slopeLimit = slopeLimit;
        }

        private void EnsureController()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !controller.enabled) return;

            // Planar velocity eases towards the request: gentle starts and stops, no snapping.
            bool speedingUp = Vector3.Dot(requestedVelocity, planarVelocity) >= 0f
                              && requestedVelocity.sqrMagnitude >= planarVelocity.sqrMagnitude;
            planarVelocity = Vector3.MoveTowards(planarVelocity, requestedVelocity, (speedingUp ? acceleration : deceleration) * dt);
            requestedVelocity = Vector3.zero;   // consumed: a driver must ask again next frame

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
