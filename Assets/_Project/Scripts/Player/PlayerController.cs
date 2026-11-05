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
    /// The walking pace (<see cref="Pace"/>, <see cref="WalkPace"/>) scales the walk it asks of the motor and is handed to the
    /// <see cref="AutoTraveller"/>; the mouse wheel and the gamepad bumpers step it.
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
        private AutoTraveller traveller;
        private HorseMount mount;
        private float pace = WalkPace.Default;
        private readonly WalkPaceWheel paceWheel = new WalkPaceWheel();

        /// <summary>
        /// Walking pace (<see cref="WalkPace"/>: 0.5..1.5 in steps of 0.1, 1 = the validated speeds). Scales manual walking and
        /// auto-travel (not sprint). Setting it is silent (the Journey menu, loading the settings); the wheel and the bumpers
        /// change it through <see cref="PaceChanged"/>.
        /// </summary>
        public float Pace
        {
            get => pace;
            set
            {
                pace = WalkPace.Clamp(value);
                if (traveller != null) traveller.Pace = pace;
            }
        }

        /// <summary>
        /// Raised when the player steps the pace (mouse wheel, gamepad bumpers). Arguments: the new pace and the speed it gives
        /// now in m/s (auto-travel's average speed while travelling, else the walking speed).
        /// </summary>
        public event Action<float, float> PaceChanged;

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
            traveller = GetComponent<AutoTraveller>();
            if (traveller != null) traveller.Pace = pace;

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
            // Riding (HorseMount, its stable of inn horses, the rider's pose): also zero setup.
            mount = GetComponent<HorseMount>();
            if (mount == null) mount = gameObject.AddComponent<HorseMount>();
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

        private static readonly int FrameCostId = Review.FrameCost.Register("PlayerController");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
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

            UpdatePace();

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

            if (mount != null && mount.HoldsBody) return;   // getting on or off the horse: the mount slides the body

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

            // The pace scales the walk only: sprint stays the fixed fast speed (walk speed x sprint scale).
            float scale = WalkPace.ManualScale(pace, sprintAction.IsPressed(), motor.SprintScale);
            if (InputOverride != null) scale *= InputOverride.MoveSpeedScale;

            if (mount != null && mount.Riding)
            {
                // On horseback, both views: the horse turns towards the input (camera-relative) at its gait's turn and goes where it
                // faces; the gait sets the speed, the sprint key asks for a canter while held.
                float ride = HorseGaits.ManualSpeed(mount.Gait, sprintAction.IsPressed());
                if (InputOverride != null) ride *= InputOverride.MoveSpeedScale;
                motor.MoveRiding(direction, ride, thirdPersonTurnSpeed, mount.Turn);
            }
            else if (cameraRig != null && cameraRig.Mode == CameraMode.FirstPerson)
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

        /// <summary>
        /// Mouse wheel (up = faster) and gamepad bumpers (RB faster, LB slower) step the walking pace, read straight from the
        /// devices as the debug keys are (the actions asset stays untouched; the Player map binds neither). Ignored while an
        /// overlay holds the input (the held map rolls its scroll with the same wheel and bumpers, the Journey menu turns its
        /// pages with the bumpers) and while the seat holds or keeps the traveller (nothing to pace there).
        /// </summary>
        private void UpdatePace()
        {
            int steps = 0;
            Mouse mouse = Mouse.current;
            steps += paceWheel.Update(mouse != null ? mouse.scroll.ReadValue().y : 0f, Time.unscaledDeltaTime);
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.rightShoulder.wasPressedThisFrame) steps++;
                if (pad.leftShoulder.wasPressedThisFrame) steps--;
            }

            if (steps == 0 || InputOverride != null || (seat != null && seat.IsActive)) return;
            if (mount != null && mount.Mounted)
            {
                // On horseback the wheel steps the gait (walk, trot, canter) instead of the walking pace.
                if (mount.Riding) mount.StepGait(steps);
                return;
            }

            Pace = WalkPace.Stepped(pace, steps);   // at either end it stays, and the message still shows where it stands
            bool travelling = travel != null && travel.IsTravelling && traveller != null;
            float speed = (travelling ? traveller.TravelSpeed : motor.WalkSpeed) * pace;
            PaceChanged?.Invoke(pace, speed);
        }

        private void OnToggleCamera(InputAction.CallbackContext context)
        {
            if (cameraRig != null) cameraRig.Toggle();
        }

        private void OnSit(InputAction.CallbackContext context)
        {
            if (InputOverride != null || seat == null) return;
            if (mount != null && mount.Mounted)
            {
                mount.Notice("Get down from the horse first");
                return;
            }

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
