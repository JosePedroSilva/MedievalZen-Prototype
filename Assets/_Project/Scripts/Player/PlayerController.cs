using System;
using MedievalZen.Travel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedievalZen.Player
{
    /// <summary>
    /// Input glue for the player rig. Reads the <c>Player</c> map of an <see cref="InputActionAsset"/>
    /// (no generated wrapper) and drives the <see cref="PlayerMotor"/> and <see cref="PlayerCameraRig"/>
    /// in manual mode. Toggles auto-travel; any real movement input cancels it.
    /// Sitting (<c>Crouch</c>: C / gamepad East) and the cinematic camera (<c>Cinematic</c>: B / d-pad up) are forwarded to
    /// <see cref="PlayerSeat"/> and <see cref="MedievalZen.Cinematic.CinematicDirector"/>, which are added at runtime when the
    /// rig has none. While the seat holds the body (walking to a seat, seated, getting up) this controller does not drive the
    /// motor; Move input, the sit key, Interact or auto-travel stand the traveller up instead.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerController : MonoBehaviour
    {
        private const string MapName = "Player";

        [Header("References")]
        [SerializeField, Tooltip("Input actions asset containing the Player map (Assets/InputSystem_Actions.inputactions).")]
        private InputActionAsset actions = null;

        [SerializeField, Tooltip("Camera rig; found on this GameObject if empty.")]
        private PlayerCameraRig cameraRig;

        [SerializeField, Tooltip("Travel state; found on this GameObject if empty.")]
        private PlayerTravel travel;

        [Header("Movement")]
        [SerializeField, Range(0f, 1f), Tooltip("Movement input magnitude below this is ignored.")]
        private float moveDeadZone = 0.1f;

        [SerializeField, Range(0f, 1f), Tooltip("Movement input magnitude above this cancels auto-travel.")]
        private float cancelTravelThreshold = 0.2f;

        [SerializeField, Min(0.1f), Tooltip("Body turn sharpness (1/s) towards the movement direction in third person (large turns are capped by the motor's max turn rate).")]
        private float thirdPersonTurnSpeed = 10f;

        [SerializeField, Min(0.1f), Tooltip("Body turn sharpness (1/s) towards the camera yaw in first person.")]
        private float firstPersonTurnSpeed = 25f;

        private PlayerMotor motor;
        private InputActionMap map;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction sprintAction;
        private InputAction toggleCameraAction;
        private InputAction autoTravelAction;
        private InputAction mapAction;
        private InputAction interactAction;
        private InputAction pauseAction;
        private InputAction sitAction;
        private InputAction cinematicAction;
        private PlayerSeat seat;
        private MedievalZen.Cinematic.CinematicDirector director;

        /// <summary>Raised when the Map action is performed (Phase 3 opens the map UI).</summary>
        public event Action MapRequested;

        /// <summary>Raised when the Interact action is performed (Phase 3: signposts, inns).</summary>
        public event Action InteractRequested;

        /// <summary>Raised when the Pause action toggles the cursor lock (argument: true = paused / cursor free).</summary>
        public event Action<bool> PauseToggled;

        /// <summary>Movement input this frame (raw, before overlays and dead zones).</summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>Look input this frame (raw: pointer pixels or stick deflection; zero while the cursor is free).</summary>
        public Vector2 LookInput { get; private set; }

        /// <summary>True when <see cref="LookInput"/> came from a pointer (pixels) rather than a stick.</summary>
        public bool LookIsPointer { get; private set; }

        /// <summary>True while the Sprint button is held (the seated traveller lets time run fast with it).</summary>
        public bool SprintHeld => sprintAction != null && map != null && map.enabled && sprintAction.IsPressed();

        /// <summary>Movement input magnitude above this cancels auto-travel (and stands the seated traveller up).</summary>
        public float CancelThreshold => cancelTravelThreshold;

        /// <summary>
        /// Overlay that currently takes over part of the input (held map, signpost list), or null. Its hooks run before the
        /// default handling of look, move, Interact, Map, AutoTravel and Pause; <see cref="IPlayerInputOverride.MoveSpeedScale"/>
        /// scales the walking pace while it is set.
        /// </summary>
        public IPlayerInputOverride InputOverride { get; set; }

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            if (cameraRig == null) cameraRig = GetComponent<PlayerCameraRig>();
            if (travel == null) travel = GetComponent<PlayerTravel>();

            if (actions == null)
            {
                Debug.LogError($"{nameof(PlayerController)} on '{name}' has no InputActionAsset assigned.", this);
                enabled = false;
                return;
            }

            map = actions.FindActionMap(MapName, true);
            moveAction = actions.FindAction(MapName + "/Move", true);
            lookAction = actions.FindAction(MapName + "/Look", true);
            sprintAction = actions.FindAction(MapName + "/Sprint", true);
            toggleCameraAction = actions.FindAction(MapName + "/ToggleCamera", true);
            autoTravelAction = actions.FindAction(MapName + "/AutoTravel", true);
            mapAction = actions.FindAction(MapName + "/Map", true);
            interactAction = actions.FindAction(MapName + "/Interact", true);
            pauseAction = actions.FindAction(MapName + "/Pause", true);
            // Optional (older action assets may lack them): sitting uses the Crouch action, the cinematic camera its own.
            sitAction = actions.FindAction(MapName + "/Crouch", false);
            cinematicAction = actions.FindAction(MapName + "/Cinematic", false);

            // Zero setup: both scenes get sitting and the cinematic camera without running a menu.
            seat = GetComponent<PlayerSeat>();
            if (seat == null) seat = gameObject.AddComponent<PlayerSeat>();
            if (GetComponent<SeatedPose>() == null) gameObject.AddComponent<SeatedPose>();
            director = GetComponent<MedievalZen.Cinematic.CinematicDirector>();
            if (director == null) director = gameObject.AddComponent<MedievalZen.Cinematic.CinematicDirector>();
        }

        private void OnEnable()
        {
            if (map == null) return;
            toggleCameraAction.performed += OnToggleCamera;
            autoTravelAction.performed += OnAutoTravel;
            mapAction.performed += OnMap;
            interactAction.performed += OnInteract;
            pauseAction.performed += OnPause;
            if (sitAction != null) sitAction.performed += OnSit;
            if (cinematicAction != null) cinematicAction.performed += OnCinematic;
            map.Enable();
        }

        private void OnDisable()
        {
            if (map == null) return;
            toggleCameraAction.performed -= OnToggleCamera;
            autoTravelAction.performed -= OnAutoTravel;
            mapAction.performed -= OnMap;
            interactAction.performed -= OnInteract;
            pauseAction.performed -= OnPause;
            if (sitAction != null) sitAction.performed -= OnSit;
            if (cinematicAction != null) cinematicAction.performed -= OnCinematic;
            map.Disable();
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
        }

        private void Update()
        {
            bool lookActive = cameraRig != null && cameraRig.CursorLocked;
            LookInput = Vector2.zero;
            if (lookActive)
            {
                InputControl control = lookAction.activeControl;
                bool isPointer = control == null || control.device is Pointer;
                Vector2 look = lookAction.ReadValue<Vector2>();
                LookInput = look;
                LookIsPointer = isPointer;
                if (InputOverride == null || !InputOverride.OnLook(look, isPointer)) cameraRig.AddLook(look, isPointer);
            }

            Vector2 move = moveAction.ReadValue<Vector2>();
            MoveInput = move;
            if (InputOverride != null && InputOverride.OnMove(move)) move = Vector2.zero;
            float magnitude = move.magnitude;

            if (seat != null && seat.IsActive)
            {
                // Walking to a seat, seated or getting up: real movement input stands the traveller up (and, as everywhere,
                // ends a trip that was paused); the seat drives the body until control comes back.
                if (InputOverride == null && magnitude > cancelTravelThreshold) seat.StandUp(PlayerSeat.StandReason.Moved);
                if (seat.HoldsBody) return;
            }

            if (travel != null && travel.IsTravelling)
            {
                if (magnitude <= cancelTravelThreshold) return;   // the auto-traveller drives the motor
                travel.Cancel();
            }

            if (magnitude < moveDeadZone)
            {
                move = Vector2.zero;
            }
            else if (magnitude > 1f)
            {
                move /= magnitude;
            }

            Vector3 forward = cameraRig != null ? cameraRig.PlanarForward : transform.forward;
            Vector3 right = cameraRig != null ? cameraRig.PlanarRight : transform.right;
            Vector3 direction = forward * move.y + right * move.x;

            float scale = sprintAction.IsPressed() ? motor.SprintScale : 1f;
            if (InputOverride != null) scale *= InputOverride.MoveSpeedScale;

            if (cameraRig != null && cameraRig.Mode == CameraMode.FirstPerson)
            {
                // First person: the body follows the camera, the feet may strafe or back up.
                motor.Move(direction, scale);
                motor.Face(forward, firstPersonTurnSpeed);
            }
            else
            {
                // Third person: the body turns towards the input and walks where it faces (no backwards sliding).
                motor.MoveFacing(direction, scale, thirdPersonTurnSpeed);
            }
        }

        private void OnToggleCamera(InputAction.CallbackContext context)
        {
            if (cameraRig != null) cameraRig.Toggle();
        }

        private void OnSit(InputAction.CallbackContext context)
        {
            if (InputOverride != null || seat == null) return;
            seat.ToggleSit();
        }

        private void OnCinematic(InputAction.CallbackContext context)
        {
            if (InputOverride != null || director == null) return;
            director.ToggleManual();
        }

        private void OnAutoTravel(InputAction.CallbackContext context)
        {
            if (InputOverride != null && InputOverride.OnAutoTravel()) return;
            if (seat != null && seat.IsActive)
            {
                // Seated: get up and walk on (a paused trip resumes; otherwise a new one starts once standing).
                seat.StandUp(PlayerSeat.StandReason.Travel);
                return;
            }

            if (travel != null) travel.ToggleAutoTravel();
        }

        private void OnMap(InputAction.CallbackContext context)
        {
            if (InputOverride != null && InputOverride.OnMap()) return;
            MapRequested?.Invoke();
        }

        private void OnInteract(InputAction.CallbackContext context)
        {
            if (InputOverride != null && InputOverride.OnInteract()) return;
            if (seat != null && seat.IsActive)
            {
                seat.StandUp(PlayerSeat.StandReason.Key);
                return;
            }

            InteractRequested?.Invoke();
        }

        private void OnPause(InputAction.CallbackContext context)
        {
            if (InputOverride != null && InputOverride.OnPause()) return;
            if (cameraRig == null) return;
            bool paused = cameraRig.CursorLocked;
            cameraRig.SetCursorLocked(!paused);
            PauseToggled?.Invoke(paused);
        }
    }
}
