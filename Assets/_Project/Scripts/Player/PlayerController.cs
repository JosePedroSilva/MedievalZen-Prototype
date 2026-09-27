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

        [SerializeField, Min(0.1f), Tooltip("Body turn sharpness (1/s) towards the movement direction in third person.")]
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

        /// <summary>Raised when the Map action is performed (Phase 3 opens the map UI).</summary>
        public event Action MapRequested;

        /// <summary>Raised when the Interact action is performed (Phase 3: signposts, inns).</summary>
        public event Action InteractRequested;

        /// <summary>Raised when the Pause action toggles the cursor lock (argument: true = paused / cursor free).</summary>
        public event Action<bool> PauseToggled;

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
        }

        private void OnEnable()
        {
            if (map == null) return;
            toggleCameraAction.performed += OnToggleCamera;
            autoTravelAction.performed += OnAutoTravel;
            mapAction.performed += OnMap;
            interactAction.performed += OnInteract;
            pauseAction.performed += OnPause;
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
            map.Disable();
        }

        private void Update()
        {
            bool lookActive = cameraRig != null && cameraRig.CursorLocked;
            if (lookActive)
            {
                InputControl control = lookAction.activeControl;
                bool isPointer = control == null || control.device is Pointer;
                Vector2 look = lookAction.ReadValue<Vector2>();
                if (InputOverride == null || !InputOverride.OnLook(look, isPointer)) cameraRig.AddLook(look, isPointer);
            }

            Vector2 move = moveAction.ReadValue<Vector2>();
            if (InputOverride != null && InputOverride.OnMove(move)) move = Vector2.zero;
            float magnitude = move.magnitude;

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
            motor.Move(direction, scale);

            if (cameraRig != null && cameraRig.Mode == CameraMode.FirstPerson)
            {
                motor.Face(forward, firstPersonTurnSpeed);
            }
            else if (direction.sqrMagnitude > 1e-4f)
            {
                motor.Face(direction, thirdPersonTurnSpeed);
            }
        }

        private void OnToggleCamera(InputAction.CallbackContext context)
        {
            if (cameraRig != null) cameraRig.Toggle();
        }

        private void OnAutoTravel(InputAction.CallbackContext context)
        {
            if (InputOverride != null && InputOverride.OnAutoTravel()) return;
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
