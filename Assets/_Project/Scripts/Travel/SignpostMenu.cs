using System.Collections.Generic;
using MedievalZen.Player;
using MedievalZen.UI;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// The list a signpost shows (on the HUD choice panel): one line per destination with its road direction and distance.
    /// While open it takes over input through <see cref="PlayerController.InputOverride"/>: Move up/down chooses a line,
    /// Interact sets that settlement as the destination, AutoTravel sets it and sets off, Map/Pause close the list.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SignpostMenu : MonoBehaviour, IPlayerInputOverride
    {
        private const int WaitForNeutral = int.MaxValue;

        [SerializeField, Min(1f), Tooltip("The list closes when the player ends up further than this from the signpost.")]
        private float closeDistance = 6f;

        [SerializeField, Range(0.1f, 0.95f), Tooltip("Stick/key deflection that moves the selection by one line.")]
        private float stepThreshold = 0.5f;

        private PlayerController controller;
        private PlayerTravel travel;
        private IReadOnlyList<SignpostChoice> choices;
        private Transform source;
        private int selected;
        private int lastStep;

        /// <summary>True while the list is shown.</summary>
        public bool IsOpen { get; private set; }

        /// <inheritdoc />
        public float MoveSpeedScale => 0f;

        /// <summary>Shows the list for <paramref name="signpost"/>.</summary>
        public void Open(SignpostInteractable signpost, IReadOnlyList<SignpostChoice> entries)
        {
            EnsureReferences();
            HudController hud = HudController.Instance;
            if (entries == null || entries.Count == 0)
            {
                if (hud != null) hud.ShowToast("The signpost's arms are weathered and blank.");
                return;
            }

            if (controller == null || (controller.InputOverride != null && !ReferenceEquals(controller.InputOverride, this))) return;
            choices = entries;
            source = signpost != null ? signpost.transform : null;
            selected = 0;
            lastStep = WaitForNeutral;
            IsOpen = true;
            controller.InputOverride = this;

            var lines = new string[Mathf.Min(entries.Count, 9)];
            for (int i = 0; i < lines.Length; i++)
            {
                SignpostChoice c = entries[i];
                lines[i] = $"{c.Settlement.Name}  <i>({c.Settlement.Type.ToString().ToLowerInvariant()})</i>  —  {UiKit.FormatDistance(c.Distance)}, {SignpostChoices.Compass(c.Direction)} road";
            }

            if (hud != null)
            {
                hud.HidePrompt();
                hud.ShowMenu("The signpost reads", lines, selected, "W/S choose  ·  E set destination  ·  T set off  ·  Esc close");
            }
        }

        /// <summary>Hides the list and gives input back.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (controller != null && ReferenceEquals(controller.InputOverride, this)) controller.InputOverride = null;
            if (HudController.Instance != null) HudController.Instance.HideMenu();
        }

        /// <inheritdoc />
        public bool OnLook(Vector2 input, bool isPointerDelta) => false;

        /// <inheritdoc />
        public bool OnMove(Vector2 move)
        {
            int step = move.y > stepThreshold ? -1 : move.y < -stepThreshold ? 1 : 0;
            if (lastStep == WaitForNeutral && step != 0) return true;   // still holding the key used to walk up
            if (step != 0 && step != lastStep && choices != null)
            {
                int count = Mathf.Min(choices.Count, 9);
                selected = (selected + step + count) % count;
                if (HudController.Instance != null) HudController.Instance.SetMenuSelection(selected);
            }

            lastStep = step;
            return true;
        }

        /// <inheritdoc />
        public bool OnInteract()
        {
            Choose(false);
            return true;
        }

        /// <inheritdoc />
        public bool OnAutoTravel()
        {
            Choose(true);
            return true;
        }

        /// <inheritdoc />
        public bool OnMap()
        {
            Close();
            return false;   // let the map open
        }

        /// <inheritdoc />
        public bool OnPause()
        {
            Close();
            return true;
        }

        private void Choose(bool setOff)
        {
            if (choices == null || selected < 0 || selected >= choices.Count) return;
            SignpostChoice choice = choices[selected];
            Close();
            if (travel == null) return;
            travel.SetDestination(choice.Settlement);
            if (setOff && !travel.IsTravelling) travel.ToggleAutoTravel();
            else if (HudController.Instance != null) HudController.Instance.ShowToast($"Destination: {choice.Settlement.Name}");
        }

        private void EnsureReferences()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (travel == null) travel = GetComponent<PlayerTravel>();
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("SignpostMenu");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (!IsOpen || source == null) return;
            Vector3 offset = source.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > closeDistance * closeDistance) Close();
        }

        private void OnDisable()
        {
            Close();
        }
    }
}
