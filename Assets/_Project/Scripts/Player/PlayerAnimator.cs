using MedievalZen.Travel;
using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>
    /// Drives the character's <see cref="Animator"/> (<c>PlayerLocomotion.controller</c>) by observing the
    /// <see cref="PlayerMotor"/> and the <see cref="PlayerCameraRig"/>; it never moves the body (no root motion; the
    /// motor stays the single movement authority). Because it only watches the motor, manual walking and auto-travel
    /// animate identically. Runs after the motor (order 110) so it sees this frame's movement; every parameter is
    /// damped. The locomotion blend is fed the body-space velocity (<c>VelocityX</c> right, <c>VelocityZ</c> forward),
    /// so a sidestep or a step back plays a matching strafe clip instead of a forward walk. The camera direction
    /// relative to the body becomes head/body look additives, so the traveller looks where the player looks.
    /// In Play mode it also places the <c>Visual</c> transform: down by the <see cref="CharacterController"/>'s skin
    /// width (the capsule rests that far above the ground) plus an inspector nudge, <see cref="GroundOffset"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        /// <summary>Animator parameter names shared with the controller builder (editor menu).</summary>
        public static class Parameters
        {
            public const string Speed = "Speed";
            public const string VelocityX = "VelocityX";
            public const string VelocityZ = "VelocityZ";
            public const string Incline = "Incline";
            public const string Grounded = "Grounded";
            public const string TurnInPlace = "TurnInPlace";
            public const string LookYaw = "LookYaw";
            public const string LookPitch = "LookPitch";
            public const string BodyYaw = "BodyYaw";
            public const string BodyPitch = "BodyPitch";
            public const string HeadYaw = "HeadYaw";
            public const string HeadPitch = "HeadPitch";
            public const string Lean = "Lean";
            public const string MapOpen = "MapOpen";
        }

        private static readonly int SpeedId = Animator.StringToHash(Parameters.Speed);
        private static readonly int VelocityXId = Animator.StringToHash(Parameters.VelocityX);
        private static readonly int VelocityZId = Animator.StringToHash(Parameters.VelocityZ);
        private static readonly int InclineId = Animator.StringToHash(Parameters.Incline);
        private static readonly int GroundedId = Animator.StringToHash(Parameters.Grounded);
        private static readonly int TurnInPlaceId = Animator.StringToHash(Parameters.TurnInPlace);
        private static readonly int LookYawId = Animator.StringToHash(Parameters.LookYaw);
        private static readonly int LookPitchId = Animator.StringToHash(Parameters.LookPitch);
        private static readonly int BodyYawId = Animator.StringToHash(Parameters.BodyYaw);
        private static readonly int BodyPitchId = Animator.StringToHash(Parameters.BodyPitch);
        private static readonly int HeadYawId = Animator.StringToHash(Parameters.HeadYaw);
        private static readonly int HeadPitchId = Animator.StringToHash(Parameters.HeadPitch);
        private static readonly int LeanId = Animator.StringToHash(Parameters.Lean);
        private static readonly int MapOpenId = Animator.StringToHash(Parameters.MapOpen);

        [Header("References (found automatically when empty)")]
        [SerializeField, Tooltip("Animator on the character model (child of Visual).")]
        private Animator animator;

        [SerializeField, Tooltip("Camera rig whose yaw/pitch the character follows with its head and body.")]
        private PlayerCameraRig cameraRig;

        [SerializeField, Tooltip("The 'Visual' transform holding the character (parent of the Animator); the grounding below moves it.")]
        private Transform visual;

        [Header("Grounding")]
        [SerializeField, Range(-0.2f, 0.2f), Tooltip("Nudge in metres (+ = up) applied to the Visual on top of the measured grounding. The feet-to-rig offset itself is measured and baked by MedievalZen/Player/Upgrade Player Rig; use this only for fine tuning (live in Play mode, then copy the value to the prefab).")]
        private float groundOffset = 0f;

        [SerializeField, Tooltip("Lower the Visual by the CharacterController's skin width: the capsule rests that far above the ground, so without this the soles hover by the skin width.")]
        private bool compensateSkinWidth = true;

        [Header("Damping (seconds)")]
        [SerializeField, Min(0f)] private float speedDamping = 0.12f;
        [SerializeField, Min(0f)] private float inclineDamping = 0.3f;
        [SerializeField, Min(0f)] private float lookDamping = 0.35f;
        [SerializeField, Min(0f)] private float leanDamping = 0.4f;
        [SerializeField, Min(0f)] private float turnDamping = 0.15f;

        [Header("Look (head and body follow the camera)")]
        [SerializeField, Range(0f, 90f), Tooltip("Largest look yaw either side of the body, in degrees.")]
        private float maxLookYaw = 70f;

        [SerializeField, Range(90f, 180f), Tooltip("When the camera looks further round than this (at the face), the character looks ahead again.")]
        private float giveUpLookYaw = 135f;

        [SerializeField, Range(0f, 60f), Tooltip("Largest look pitch up or down, in degrees.")]
        private float maxLookPitch = 35f;

        [SerializeField, Range(0f, 1f), Tooltip("Share of the look taken by the body (spine); the head takes the rest.")]
        private float bodyShare = 0.35f;

        [SerializeField, Range(-30f, 30f), Tooltip("Third-person camera pitch that counts as looking straight ahead (the orbit looks down on the character).")]
        private float thirdPersonNeutralPitch = 8f;

        [SerializeField, Range(-45f, 0f), Tooltip("Look pitch while the map is held (negative = down at the map).")]
        private float mapLookPitch = -22f;

        [Header("Lean, turning, incline, air")]
        [SerializeField, Min(0.1f), Tooltip("Lateral acceleration (speed × turn rate, m/s²) that gives the full lean.")]
        private float fullLeanAcceleration = 3f;

        [SerializeField, Tooltip("Step on the spot when the body turns while standing (third person). First person shows only the shadow.")]
        private bool turnInPlaceInFirstPerson = false;

        [SerializeField, Range(0f, 45f), Tooltip("Largest incline fed to the controller (the slope clips are 25°).")]
        private float maxIncline = 25f;

        [SerializeField, Tooltip("Layers probed for the ground normal (incline).")]
        private LayerMask groundLayers = Physics.DefaultRaycastLayers;

        [SerializeField, Min(0f), Tooltip("Seconds off the ground before the falling pose starts (the game has no jump; this ignores small bumps).")]
        private float fallDelay = 0.25f;

        private PlayerMotor motor;
        private CharacterController body;
        private HeldMap heldMap;
        private float previousYaw;
        private float airTime;
        private bool primed;

        /// <summary>The driven animator (may be null when the rig has no animated character).</summary>
        public Animator Animator => animator;

        /// <summary>
        /// While true (the traveller rides, set by <see cref="HorseMount"/>) the locomotion parameters ask for standing still whatever the
        /// motor does, so the walk cycle stops under the rider's pose. False on foot: nothing changes.
        /// </summary>
        public bool StandStill { get; set; }

        /// <summary>Extra vertical offset of the Visual in metres (+ = up), on top of the skin-width compensation.</summary>
        public float GroundOffset
        {
            get => groundOffset;
            set
            {
                groundOffset = value;
                ApplyGrounding();
            }
        }

        /// <summary>Local height the Visual is placed at in Play mode: the nudge minus the capsule's skin width (when compensated).</summary>
        public float VisualHeight => groundOffset - (compensateSkinWidth && body != null ? body.skinWidth : 0f);

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            body = GetComponent<CharacterController>();
            if (cameraRig == null) cameraRig = GetComponent<PlayerCameraRig>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;                              // the motor moves the body
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;      // the first-person shadow keeps walking
                Transform parent = animator.transform.parent;
                if (visual == null && parent != null && parent != transform) visual = parent;
            }

            ApplyGrounding();
        }

        private void OnValidate()
        {
            // Live nudging from the inspector in Play mode; in edit mode the prefab keeps the Visual at 0.
            if (Application.isPlaying && body != null) ApplyGrounding();
        }

        private void ApplyGrounding()
        {
            if (visual == null || !Application.isPlaying) return;
            Vector3 local = visual.localPosition;
            float height = VisualHeight;
            if (!Mathf.Approximately(local.y, height)) visual.localPosition = new Vector3(local.x, height, local.z);
        }

        private void Start()
        {
            heldMap = GetComponent<HeldMap>();
            previousYaw = motor.Yaw;
        }

        private void OnEnable()
        {
            primed = false;
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("PlayerAnimator");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            ApplyGrounding();   // cheap compare; follows inspector changes of the skin width too
            float dt = Time.deltaTime;
            if (dt <= 0f || animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;

            // On the first frame (and after re-enabling) jump straight to the targets instead of easing in from zero.
            float damp = primed ? 1f : 0f;
            primed = true;

            // Astride (HorseMount): the horse carries the body, so the legs stand still for the rider's pose to bend.
            float speed = StandStill ? 0f : LocomotionMath.SpeedParameter(motor.PlanarSpeed);
            Vector2 local = StandStill ? Vector2.zero : LocomotionMath.VelocityParameter(motor.LocalPlanarVelocity);
            bool grounded = motor.IsGrounded;
            float yaw = motor.Yaw;
            float yawRate = StandStill ? 0f : LocomotionMath.YawRate(previousYaw, yaw, dt);
            previousYaw = yaw;

            // Grounded flips false only after fallDelay seconds in the air.
            airTime = LocomotionMath.AirTime(airTime, grounded, dt);
            animator.SetBool(GroundedId, airTime <= fallDelay);

            SetFloat(SpeedId, speed, speedDamping * damp, dt);
            SetFloat(VelocityXId, local.x, speedDamping * damp, dt);
            SetFloat(VelocityZId, local.y, speedDamping * damp, dt);
            SetFloat(InclineId, grounded ? ComputeIncline(speed) : 0f, inclineDamping * damp, dt);

            bool thirdPerson = cameraRig == null || cameraRig.Mode == CameraMode.ThirdPerson;
            float turn = thirdPerson || turnInPlaceInFirstPerson ? LocomotionMath.TurnInPlace(yawRate, speed) : 0f;
            SetFloat(TurnInPlaceId, turn, turnDamping * damp, dt);
            SetFloat(LeanId, LocomotionMath.Lean(yawRate, speed, fullLeanAcceleration), leanDamping * damp, dt);

            bool mapOpen = heldMap != null && heldMap.IsOpen;
            animator.SetBool(MapOpenId, mapOpen);

            float lookYaw = 0f;
            float lookPitch = 0f;
            if (mapOpen)
            {
                lookPitch = mapLookPitch;
            }
            else if (cameraRig != null)
            {
                lookYaw = LocomotionMath.LookYaw(cameraRig.Yaw, yaw, maxLookYaw, giveUpLookYaw);
                lookPitch = LocomotionMath.LookPitch(cameraRig.Pitch, thirdPerson ? thirdPersonNeutralPitch : 0f, maxLookPitch);
            }

            LocomotionMath.SplitLook(lookYaw, bodyShare, out float bodyYaw, out float headYaw);
            LocomotionMath.SplitLook(lookPitch, bodyShare, out float bodyPitch, out float headPitch);
            float lookDamp = lookDamping * damp;
            SetFloat(LookYawId, lookYaw, lookDamp, dt);
            SetFloat(LookPitchId, lookPitch, lookDamp, dt);
            SetFloat(BodyYawId, bodyYaw, lookDamp, dt);
            SetFloat(BodyPitchId, bodyPitch, lookDamp, dt);
            SetFloat(HeadYawId, headYaw, lookDamp, dt);
            SetFloat(HeadPitchId, headPitch, lookDamp, dt);
        }

        private void SetFloat(int id, float value, float dampTime, float deltaTime)
        {
            if (dampTime <= 0f) animator.SetFloat(id, value);
            else animator.SetFloat(id, value, dampTime, deltaTime);
        }

        /// <summary>Slope along the walking direction (or facing when still): from the ground normal below the feet, else from the velocity.</summary>
        private float ComputeIncline(float speed)
        {
            Vector3 velocity = motor.Velocity;
            Vector3 direction = speed > 0.2f ? new Vector3(velocity.x, 0f, velocity.z) : motor.Forward;
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            float incline = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.2f, groundLayers, QueryTriggerInteraction.Ignore)
                ? LocomotionMath.InclineFromNormal(hit.normal, direction)
                : LocomotionMath.InclineFromVelocity(velocity);
            return LocomotionMath.ClampIncline(incline, maxIncline);
        }
    }
}
