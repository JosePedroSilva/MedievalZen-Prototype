using MedievalZen.Player;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// The "E — Sit on the log" prompt. One instance on the player (added by <see cref="PlayerSeat"/>) stands in for every seat
    /// prop: it asks the <see cref="SeatIndex"/> a few times a second for the best seat within reach that the traveller faces,
    /// and Interact sits there. Inn doorsteps are left to the inn's own prompt (the sit key still sits on them).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeatInteractable : Interactable
    {
        private const float RefreshSeconds = 0.3f;

        private PlayerSeat seat;
        private float nextRefresh;
        private bool hasSeat;
        private SeatCandidate candidate;
        private SeatPlan seatPlan;
        private string prompt = "E — Sit down";

        /// <inheritdoc />
        public override string Prompt => prompt;

        /// <inheritdoc />
        public override bool TryFocus(Vector3 playerPosition, Vector3 viewForward, out float score)
        {
            score = float.PositiveInfinity;
            if (seat == null || seat.IsActive) return false;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                hasSeat = seat.CanSit(out _) && seat.FindSeat(out candidate, out seatPlan);
                if (hasSeat) prompt = "E — " + SeatCatalog.Verb(candidate.Kind);
            }

            if (!hasSeat || candidate.Transform == null) return false;
            // Faced (or nearly under the traveller), like the signposts and inn doors; ranked after them on a tie.
            if (!InReach(playerPosition, viewForward, seatPlan.Hip, 2.6f, 0.3f, out float distance)) return false;
            score = distance + 0.75f;
            return true;
        }

        /// <inheritdoc />
        public override void Interact(PlayerInteractor interactor)
        {
            if (seat == null || !hasSeat) return;
            seat.SitAt(candidate);
            nextRefresh = 0f;
        }

        private void Awake()
        {
            seat = GetComponent<PlayerSeat>();
        }
    }
}
