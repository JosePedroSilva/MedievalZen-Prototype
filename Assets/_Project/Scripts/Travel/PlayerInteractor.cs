using MedievalZen.Player;
using MedievalZen.UI;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Finds the <see cref="Interactable"/> the player is standing at and facing, shows its prompt on the HUD, and uses it
    /// when <see cref="PlayerController.InteractRequested"/> fires. Stays quiet while an input overlay (map, signpost list)
    /// is open or while <see cref="Busy"/> (e.g. resting at an inn).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-30)]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField, Tooltip("Player input; found on this GameObject if empty.")]
        private PlayerController controller;

        [SerializeField, Tooltip("Travel state; found on this GameObject if empty.")]
        private PlayerTravel travel;

        [SerializeField, Tooltip("Camera rig (for the facing test); found on this GameObject if empty.")]
        private PlayerCameraRig cameraRig;

        private Interactable focus;
        private bool promptShown;

        /// <summary>Player input glue.</summary>
        public PlayerController Controller => controller;

        /// <summary>Travel state.</summary>
        public PlayerTravel Travel => travel;

        /// <summary>The interactable currently in focus, or null.</summary>
        public Interactable Focus => focus;

        /// <summary>While true, nothing can be focused or used (set during scripted moments such as resting).</summary>
        public bool Busy { get; set; }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (travel == null) travel = GetComponent<PlayerTravel>();
            if (cameraRig == null) cameraRig = GetComponent<PlayerCameraRig>();
            if (GetComponent<SignpostMenu>() == null) gameObject.AddComponent<SignpostMenu>();
        }

        private void OnEnable()
        {
            if (controller != null) controller.InteractRequested += OnInteract;
        }

        private void OnDisable()
        {
            if (controller != null) controller.InteractRequested -= OnInteract;
            SetFocus(null);
        }

        private void Update()
        {
            bool blocked = Busy || (controller != null && controller.InputOverride != null);
            SetFocus(blocked ? null : FindBest());
        }

        private Interactable FindBest()
        {
            Vector3 position = transform.position;
            Vector3 forward = cameraRig != null ? cameraRig.PlanarForward : transform.forward;
            Interactable best = null;
            float bestScore = float.PositiveInfinity;
            var all = Interactable.All;
            for (int i = 0; i < all.Count; i++)
            {
                Interactable candidate = all[i];
                if (candidate == null) continue;
                if (candidate.TryFocus(position, forward, out float score) && score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private void SetFocus(Interactable next)
        {
            if (next == focus && promptShown == (next != null)) return;
            focus = next;
            HudController hud = HudController.Instance;
            if (hud == null) return;
            if (focus != null)
            {
                hud.ShowPrompt(focus.Prompt);
                promptShown = true;
            }
            else if (promptShown)
            {
                // Only hide what we showed: overlays manage their own prompt while open.
                if (controller == null || controller.InputOverride == null) hud.HidePrompt();
                promptShown = false;
            }
        }

        private void OnInteract()
        {
            if (Busy || focus == null) return;
            Interactable target = focus;
            SetFocus(null);
            target.Interact(this);
        }
    }
}
