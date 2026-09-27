using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Player
{
    /// <summary>
    /// Hand-rolled first/third-person camera on the player rig. Owns one child <see cref="Camera"/>
    /// (with its AudioListener) and positions it every <c>LateUpdate</c>. The look direction (yaw/pitch)
    /// is shared by both modes and independent of the body; toggling blends the camera position
    /// between the eye anchor and the over-the-shoulder orbit point.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [Header("References")]
        [SerializeField, Tooltip("The camera this rig drives (a child of the rig).")]
        private Camera viewCamera;

        [SerializeField, Tooltip("Third-person orbit pivot (about 1.4 m above the feet). If empty, pivotHeight above the rig root is used.")]
        private Transform pivot = null;

        [SerializeField, Tooltip("First-person eye position (about 1.62 m above the feet). If empty, eyeHeight above the rig root is used.")]
        private Transform eyeAnchor = null;

        [SerializeField, Tooltip("Root of the character's visual; its renderers switch to shadows-only in first person.")]
        private Transform visualRoot = null;

        [Header("Mode")]
        [SerializeField, Tooltip("Camera mode when play starts.")]
        private CameraMode startMode = CameraMode.ThirdPerson;

        [SerializeField, Min(0.01f), Tooltip("Seconds the camera takes to blend between first and third person.")]
        private float blendDuration = 0.35f;

        [Header("Look")]
        [SerializeField, Min(0f), Tooltip("Degrees of rotation per pixel of mouse movement.")]
        private float mouseSensitivity = 0.08f;

        [SerializeField, Min(0f), Tooltip("Degrees per second at full gamepad stick deflection.")]
        private float stickSensitivity = 120f;

        [SerializeField, Tooltip("Invert vertical look.")]
        private bool invertY = false;

        [SerializeField, Min(0f), Tooltip("Look smoothing sharpness in 1/s (0 = raw input). Higher follows the input more tightly.")]
        private float lookSmoothing = 18f;

        [SerializeField, Range(-89f, 0f), Tooltip("Lowest pitch in degrees (looking up is negative).")]
        private float minPitch = -80f;

        [SerializeField, Range(0f, 89f), Tooltip("Highest pitch in degrees (looking down is positive).")]
        private float maxPitch = 80f;

        [SerializeField, Range(-80f, 80f), Tooltip("Pitch when play starts.")]
        private float startPitch = 8f;

        [Header("First person")]
        [SerializeField, Min(0f), Tooltip("Eye height above the feet, used when no eye anchor is assigned.")]
        private float eyeHeight = 1.62f;

        [SerializeField, Range(0f, 1f), Tooltip("Below this blend value (0 = at the eyes, 1 = orbit) the body renders shadows only.")]
        private float hideBodyBelowBlend = 0.25f;

        [Header("Third person")]
        [SerializeField, Min(0f), Tooltip("Orbit pivot height above the feet, used when no pivot is assigned.")]
        private float pivotHeight = 1.4f;

        [SerializeField, Min(0.5f), Tooltip("Distance of the camera behind the pivot.")]
        private float distance = 3.5f;

        [SerializeField, Tooltip("Sideways offset of the orbit (positive = over the right shoulder).")]
        private float shoulderOffset = 0.35f;

        [SerializeField, Min(0.01f), Tooltip("Radius of the sphere cast that keeps the camera out of walls and hills.")]
        private float collisionRadius = 0.2f;

        [SerializeField, Min(0f), Tooltip("Closest the camera may be pulled in towards the pivot.")]
        private float minDistance = 0.4f;

        [SerializeField, Min(0.01f), Tooltip("Sharpness (1/s) with which the camera eases back out after an obstruction clears. Pull-in is immediate.")]
        private float distanceRecovery = 3f;

        [SerializeField, Tooltip("Layers that block the third-person camera.")]
        private LayerMask collisionLayers = Physics.DefaultRaycastLayers;

        [Header("Cursor")]
        [SerializeField, Tooltip("Lock and hide the cursor when the rig becomes active.")]
        private bool lockCursorOnEnable = true;

        private Transform cameraTransform;
        private Renderer[] bodyRenderers = Array.Empty<Renderer>();
        private ShadowCastingMode[] bodyShadowModes = Array.Empty<ShadowCastingMode>();
        private bool bodyHidden;
        private float yaw;
        private float pitch;
        private float targetYaw;
        private float targetPitch;
        private float blend;            // 0 = first person, 1 = third person
        private float currentDistance;
        private float framingWeight;    // 0 = normal orbit, 1 = framing override
        private bool framingActive;
        private float framingDistance;
        private float framingShoulder;
        private float framingPivotRaise;
        private float framingBlendDuration = 0.5f;

        /// <summary>Raised after <see cref="Mode"/> changes.</summary>
        public event Action<CameraMode> ModeChanged;

        /// <summary>Current (target) camera mode. The position may still be blending towards it.</summary>
        public CameraMode Mode { get; private set; }

        /// <summary>The camera driven by this rig.</summary>
        public Camera Camera => viewCamera;

        /// <summary>Camera yaw in degrees (0..360), independent of the body.</summary>
        public float Yaw => Mathf.Repeat(yaw, 360f);

        /// <summary>Camera pitch in degrees (positive looks down).</summary>
        public float Pitch => pitch;

        /// <summary>Unit horizontal forward of the camera: the basis for camera-relative movement.</summary>
        public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        /// <summary>Unit horizontal right of the camera.</summary>
        public Vector3 PlanarRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        /// <summary>Whether the cursor is currently locked by this rig (look input is active).</summary>
        public bool CursorLocked { get; private set; }

        /// <summary>Switches between first and third person with a smooth blend.</summary>
        public void Toggle()
        {
            SetMode(Mode == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson);
        }

        /// <summary>Sets the camera mode; <paramref name="instant"/> skips the blend.</summary>
        public void SetMode(CameraMode mode, bool instant = false)
        {
            bool changed = mode != Mode;
            Mode = mode;
            if (instant) blend = mode == CameraMode.ThirdPerson ? 1f : 0f;
            if (changed) ModeChanged?.Invoke(mode);
        }

        /// <summary>
        /// Adds look input. Pointer input is a per-frame delta in pixels; stick input is a -1..1
        /// deflection that is integrated over <c>Time.deltaTime</c>.
        /// </summary>
        public void AddLook(Vector2 input, bool isPointerDelta)
        {
            Vector2 degrees = isPointerDelta ? input * mouseSensitivity : input * (stickSensitivity * Time.deltaTime);
            targetYaw += degrees.x;
            targetPitch = Mathf.Clamp(targetPitch + (invertY ? degrees.y : -degrees.y), minPitch, maxPitch);
        }

        /// <summary>Points the camera along <paramref name="yawDegrees"/> immediately (e.g. after a teleport).</summary>
        public void SnapYaw(float yawDegrees)
        {
            yaw = targetYaw = yawDegrees;
        }

        /// <summary>
        /// Eases the third-person orbit to a different framing (e.g. close over the shoulder while reading the map) over
        /// <paramref name="blendSeconds"/>. First person is unaffected. Undo with <see cref="ClearFramingOverride"/>.
        /// </summary>
        public void SetFramingOverride(float orbitDistance, float orbitShoulderOffset, float pivotRaise = 0f, float blendSeconds = 0.5f)
        {
            framingActive = true;
            framingDistance = Mathf.Max(0.3f, orbitDistance);
            framingShoulder = orbitShoulderOffset;
            framingPivotRaise = pivotRaise;
            framingBlendDuration = Mathf.Max(0.01f, blendSeconds);
        }

        /// <summary>Eases back to the normal third-person orbit.</summary>
        public void ClearFramingOverride(float blendSeconds = 0.5f)
        {
            framingActive = false;
            framingBlendDuration = Mathf.Max(0.01f, blendSeconds);
        }

        /// <summary>Locks (hidden, centred) or frees the cursor.</summary>
        public void SetCursorLocked(bool locked)
        {
            CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Awake()
        {
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
            cameraTransform = viewCamera != null ? viewCamera.transform : null;
            if (cameraTransform == null)
            {
                Debug.LogError($"{nameof(PlayerCameraRig)} on '{name}' has no camera.", this);
            }

            if (visualRoot != null)
            {
                bodyRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
                bodyShadowModes = new ShadowCastingMode[bodyRenderers.Length];
                for (int i = 0; i < bodyRenderers.Length; i++)
                {
                    bodyShadowModes[i] = bodyRenderers[i].shadowCastingMode;
                }
            }

            yaw = targetYaw = transform.eulerAngles.y;
            pitch = targetPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);
            currentDistance = distance;
            Mode = startMode;
            blend = startMode == CameraMode.ThirdPerson ? 1f : 0f;
        }

        private void OnEnable()
        {
            if (lockCursorOnEnable) SetCursorLocked(true);
        }

        private void OnDisable()
        {
            if (CursorLocked) SetCursorLocked(false);
            SetBodyHidden(false);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // The editor drops the lock when the game view loses focus; restore it on return.
            if (hasFocus && CursorLocked) SetCursorLocked(true);
        }

        private void LateUpdate()
        {
            if (cameraTransform == null) return;
            float dt = Time.deltaTime;

            float k = lookSmoothing > 0f ? 1f - Mathf.Exp(-lookSmoothing * dt) : 1f;
            yaw += (targetYaw - yaw) * k;
            pitch += (targetPitch - pitch) * k;
            if (Mathf.Abs(targetYaw) > 3600f)
            {
                // Keep the unwrapped angles small without changing the direction.
                float wrap = Mathf.Floor(targetYaw / 360f) * 360f;
                targetYaw -= wrap;
                yaw -= wrap;
            }

            float targetBlend = Mode == CameraMode.ThirdPerson ? 1f : 0f;
            blend = Mathf.MoveTowards(blend, targetBlend, dt / blendDuration);
            float eased = Mathf.SmoothStep(0f, 1f, blend);
            framingWeight = Mathf.MoveTowards(framingWeight, framingActive ? 1f : 0f, dt / framingBlendDuration);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 firstPerson = eyeAnchor != null ? eyeAnchor.position : transform.position + Vector3.up * eyeHeight;
            Vector3 thirdPerson = ThirdPersonPosition(rotation, dt);

            cameraTransform.SetPositionAndRotation(Vector3.LerpUnclamped(firstPerson, thirdPerson, eased), rotation);
            SetBodyHidden(eased < hideBodyBelowBlend);
        }

        private Vector3 ThirdPersonPosition(Quaternion rotation, float dt)
        {
            Vector3 pivotPosition = pivot != null ? pivot.position : transform.position + Vector3.up * pivotHeight;
            Vector3 right = rotation * Vector3.right;
            Vector3 back = rotation * Vector3.back;

            float frame = Mathf.SmoothStep(0f, 1f, framingWeight);
            float distance = Mathf.Lerp(this.distance, framingDistance, frame);
            float shoulderOffset = Mathf.Lerp(this.shoulderOffset, framingShoulder, frame);
            pivotPosition += Vector3.up * (framingPivotRaise * frame);

            // Casts start inside the player's own capsule, which queries ignore, so no self-filtering needed.
            Vector3 shoulder = pivotPosition + right * shoulderOffset;
            float side = Mathf.Abs(shoulderOffset);
            if (side > 1e-3f && Physics.SphereCast(pivotPosition, collisionRadius, right * Mathf.Sign(shoulderOffset), out RaycastHit sideHit,
                    side, collisionLayers, QueryTriggerInteraction.Ignore))
            {
                shoulder = pivotPosition + right * (Mathf.Sign(shoulderOffset) * sideHit.distance);
            }

            float wanted = distance;
            if (Physics.SphereCast(shoulder, collisionRadius, back, out RaycastHit hit, distance, collisionLayers, QueryTriggerInteraction.Ignore))
            {
                wanted = Mathf.Max(minDistance, hit.distance);
            }

            currentDistance = wanted < currentDistance
                ? wanted
                : Mathf.Lerp(currentDistance, wanted, 1f - Mathf.Exp(-distanceRecovery * dt));

            return shoulder + back * currentDistance;
        }

        private void SetBodyHidden(bool hidden)
        {
            if (hidden == bodyHidden) return;
            bodyHidden = hidden;
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] == null) continue;
                bodyRenderers[i].shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : bodyShadowModes[i];
            }
        }
    }
}
