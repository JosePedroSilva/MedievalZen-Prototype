using MedievalZen.Player;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// The "E — Mount the horse" prompt. One instance on the player (added by <see cref="HorseMount"/>) stands in for every horse of the
    /// <see cref="HorseStable"/>: the nearest one in reach that the traveller faces, while on foot and not sitting. Interact gets on it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HorseInteractable : Interactable
    {
        private HorseStable stable;
        private HorseMount mount;
        private PlayerSeat seat;
        private HorseBody candidate;
        private string prompt = "E — Mount the horse";

        /// <inheritdoc />
        public override string Prompt => prompt;

        /// <inheritdoc />
        public override bool TryFocus(Vector3 playerPosition, Vector3 viewForward, out float score)
        {
            score = float.PositiveInfinity;
            candidate = null;
            if (stable == null || mount == null || mount.Mounted || (seat != null && seat.IsActive)) return false;
            candidate = stable.FindMountable(playerPosition, viewForward, out float distance);
            if (candidate == null) return false;
            prompt = candidate == stable.Own ? "E — Ride on" : "E — Mount the horse";
            score = distance + 0.5f;
            return true;
        }

        /// <inheritdoc />
        public override void Interact(PlayerInteractor interactor)
        {
            if (candidate != null && mount != null) mount.Mount(candidate);
        }

        private void Awake()
        {
            stable = GetComponent<HorseStable>();
            mount = GetComponent<HorseMount>();
        }

        private void Start()
        {
            seat = GetComponent<PlayerSeat>();
            if (stable == null) stable = GetComponent<HorseStable>();
            if (mount == null) mount = GetComponent<HorseMount>();
        }
    }
}
