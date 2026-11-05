using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Player
{
    /// <summary>
    /// Something that can take the camera over from the rig (the cinematic director). The rig stays the only owner of the
    /// <see cref="Camera"/>: it asks the provider once per frame, inside its own <c>LateUpdate</c>, and blends between its
    /// first/third-person pose and the provider's.
    /// </summary>
    public interface ICameraPoseProvider
    {
        /// <summary>
        /// This frame's camera pose. Return false to keep the previous one (the rig holds the last pose it was given).
        /// <paramref name="fieldOfView"/> is the vertical field of view in degrees.
        /// </summary>
        bool TryGetCameraPose(float deltaTime, out Vector3 position, out Quaternion rotation, out float fieldOfView);
    }

    /// <summary>
    /// Hand-rolled first/third-person camera on the player rig. Owns one child <see cref="Camera"/>
    /// (with its AudioListener) and positions it every <c>LateUpdate</c>. The look direction (yaw/pitch)
    /// is shared by both modes and independent of the body; toggling blends the camera position
    /// between the eye anchor and the over-the-shoulder orbit point.
    /// Two narrow hooks extend it without changing either mode: an external pose provider
    /// (<see cref="SetPoseProvider"/>, the cinematic camera) blended in and out over the normal pose, and an
    /// <see cref="AnchorOffset"/> plus optional look limits (sitting down lowers the eyes and the orbit pivot).
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
        private float lookSensitivityScale = 1f;

        // External pose (cinematic camera).
        private ICameraPoseProvider poseProvider;
        private bool externalActive;
        private float externalWeight;          // linear 0..1, eased when applied
        private float externalBlendSeconds = 1f;
        private bool hasExternalPose;
        private Vector3 externalPosition;
        private Quaternion externalRotation = Quaternion.identity;
        private float externalFov = 60f;
        private float baseFieldOfView = 60f;
        private float appliedFov = -1f;

        // Look limits (seated, first person).
        private bool lookLimited;
        private float limitCentreYaw;
        private float limitYawHalfRange = 180f;
        private float limitMinPitch = -89f;
        private float limitMaxPitch = 89f;

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

        /// <summary>
        /// Player preference multiplying both the mouse and the stick sensitivity (1 = the inspector values). Set by the
        /// Journey menu's "Look speed"; clamped to 0.1..5.
        /// </summary>
        public float LookSensitivityScale
        {
            get => lookSensitivityScale;
            set => lookSensitivityScale = Mathf.Clamp(value, 0.1f, 5f);
        }

        /// <summary>Invert vertical look (starts at the inspector value).</summary>
        public bool InvertY
        {
            get => invertY;
            set => invertY = value;
        }

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
            Vector2 degrees = (isPointerDelta ? input * mouseSensitivity : input * (stickSensitivity * Time.deltaTime)) * lookSensitivityScale;
            targetYaw += degrees.x;
            targetPitch = Mathf.Clamp(targetPitch + (invertY ? degrees.y : -degrees.y), minPitch, maxPitch);
            ApplyLookLimits();
        }

        /// <summary>
        /// Converts look input to degrees exactly as <see cref="AddLook"/> would (sensitivity, look speed and invert applied:
        /// x = yaw right, y = pitch down), without moving the rig. The cinematic camera's manual orbit uses it.
        /// </summary>
        public Vector2 LookDegrees(Vector2 input, bool isPointerDelta)
        {
            Vector2 degrees = (isPointerDelta ? input * mouseSensitivity : input * (stickSensitivity * Time.deltaTime)) * lookSensitivityScale;
            return new Vector2(degrees.x, invertY ? degrees.y : -degrees.y);
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

        // ------------------------------------------------------------------ external pose (cinematic camera)

        /// <summary>
        /// Hands the camera to <paramref name="provider"/>, blending from the current first/third-person pose to the provider's
        /// over <paramref name="blendSeconds"/> (eased). First/third-person keep running underneath, unchanged.
        /// </summary>
        public void SetPoseProvider(ICameraPoseProvider provider, float blendSeconds)
        {
            if (provider == null) return;
            if (!ReferenceEquals(provider, poseProvider)) hasExternalPose = false;
            poseProvider = provider;
            externalActive = true;
            externalBlendSeconds = Mathf.Max(0.01f, blendSeconds);
        }

        /// <summary>
        /// Gives the camera back from <paramref name="provider"/> (no-op for another provider), blending back over
        /// <paramref name="blendSeconds"/>; the provider keeps being asked for its pose until the blend has finished.
        /// </summary>
        public void ReleasePoseProvider(ICameraPoseProvider provider, float blendSeconds)
        {
            if (!ReferenceEquals(provider, poseProvider)) return;
            externalActive = false;
            externalBlendSeconds = Mathf.Max(0.01f, blendSeconds);
        }

        /// <summary>True while a pose provider has (or is handing back) the camera.</summary>
        public bool HasPoseProvider => poseProvider != null;

        /// <summary>Eased share (0..1) of the external pose in the camera this frame.</summary>
        public float ExternalWeight => Mathf.SmoothStep(0f, 1f, externalWeight);

        /// <summary>The camera's vertical field of view outside the cinematic camera.</summary>
        public float BaseFieldOfView => baseFieldOfView;

        /// <summary>Points the look direction at <paramref name="yawDegrees"/> / <paramref name="pitchDegrees"/> immediately.</summary>
        public void SnapLook(float yawDegrees, float pitchDegrees)
        {
            yaw = targetYaw = yawDegrees;
            pitch = targetPitch = Mathf.Clamp(pitchDegrees, minPitch, maxPitch);
        }

        // ------------------------------------------------------------------ seated anchor and look limits

        /// <summary>
        /// World-space offset added to the first-person eye and the third-person pivot (zero = as designed). The seated traveller
        /// lowers both; whoever sets it eases it in and out.
        /// </summary>
        public Vector3 AnchorOffset { get; set; }

        /// <summary>The first-person eye position without <see cref="AnchorOffset"/>.</summary>
        public Vector3 StandingEyePosition => eyeAnchor != null ? eyeAnchor.position : transform.position + Vector3.up * eyeHeight;

        /// <summary>
        /// Keeps the look within <paramref name="yawHalfRange"/> degrees of <paramref name="centreYaw"/> and between the pitches
        /// (gentle: the target is pulled back in, the camera follows with the usual smoothing). Undo with <see cref="ClearLookLimits"/>.
        /// </summary>
        public void SetLookLimits(float centreYaw, float yawHalfRange, float minPitchDegrees, float maxPitchDegrees)
        {
            lookLimited = true;
            limitCentreYaw = centreYaw;
            limitYawHalfRange = Mathf.Clamp(yawHalfRange, 5f, 180f);
            limitMinPitch = Mathf.Min(minPitchDegrees, maxPitchDegrees);
            limitMaxPitch = Mathf.Max(minPitchDegrees, maxPitchDegrees);
        }

        /// <summary>Removes the look limits.</summary>
        public void ClearLookLimits()
        {
            lookLimited = false;
        }

        private void ApplyLookLimits()
        {
            if (!lookLimited) return;
            float delta = Mathf.DeltaAngle(limitCentreYaw, targetYaw);
            float clamped = Mathf.Clamp(delta, -limitYawHalfRange, limitYawHalfRange);
            targetYaw += clamped - delta;
            targetPitch = Mathf.Clamp(targetPitch, Mathf.Max(minPitch, limitMinPitch), Mathf.Min(maxPitch, limitMaxPitch));
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
            else
            {
                baseFieldOfView = viewCamera.fieldOfView;
                // A single NaN pixel from any shader is turned by bloom into a white glow over half the screen (the streams, twice).
                // URP's Stop NaNs pass replaces NaN and infinite pixels before post-processing: one cheap full-screen pass.
                var cameraData = viewCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (cameraData != null) cameraData.stopNaN = true;
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

        private static readonly int FrameCostId = Review.FrameCost.Register("PlayerCameraRig");

        private void LateUpdate()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (cameraTransform == null) return;
            float dt = Time.deltaTime;

            ApplyLookLimits();
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
            Vector3 firstPerson = StandingEyePosition + AnchorOffset;
            Vector3 thirdPerson = ThirdPersonPosition(rotation, dt);
            Vector3 position = Vector3.LerpUnclamped(firstPerson, thirdPerson, eased);
            float fieldOfView = baseFieldOfView;

            // External pose (cinematic camera), blended over the normal one; asked once per frame while it has any share.
            externalWeight = Mathf.MoveTowards(externalWeight, externalActive ? 1f : 0f, dt / externalBlendSeconds);
            float external = 0f;
            if (poseProvider != null)
            {
                if (poseProvider.TryGetCameraPose(dt, out Vector3 p, out Quaternion r, out float f))
                {
                    externalPosition = p;
                    externalRotation = r;
                    externalFov = f;
                    hasExternalPose = true;
                }

                if (hasExternalPose)
                {
                    external = Mathf.SmoothStep(0f, 1f, externalWeight);
                    position = Vector3.LerpUnclamped(position, externalPosition, external);
                    rotation = Quaternion.Slerp(rotation, externalRotation, external);
                    fieldOfView = Mathf.Lerp(baseFieldOfView, externalFov, external);
                }

                if (!externalActive && externalWeight <= 0f)
                {
                    poseProvider = null;
                    hasExternalPose = false;
                }
            }

            cameraTransform.SetPositionAndRotation(position, rotation);
            if (poseProvider != null)
            {
                if (!Mathf.Approximately(appliedFov, fieldOfView)) viewCamera.fieldOfView = fieldOfView;
                appliedFov = fieldOfView;
            }
            else if (appliedFov >= 0f)
            {
                viewCamera.fieldOfView = baseFieldOfView;   // handed back: restore the lens exactly
                appliedFov = -1f;
            }
            else
            {
                baseFieldOfView = viewCamera.fieldOfView;   // not ours: follow whatever the camera is set to
            }

            SetBodyHidden(eased < hideBodyBelowBlend && external < 0.5f);
        }

        private Vector3 ThirdPersonPosition(Quaternion rotation, float dt)
        {
            Vector3 pivotPosition = (pivot != null ? pivot.position : transform.position + Vector3.up * pivotHeight) + AnchorOffset;
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
