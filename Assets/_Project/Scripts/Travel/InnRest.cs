using System.Collections;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.UI;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// The door of an inn cabin (placed by the world builder in front of each inn's cabin). Within <see cref="reach"/> metres
    /// the HUD shows "E — Rest at the inn"; Interact stops auto-travel, fades to black, skips time to 06:00 the next
    /// morning, shows "You rest until dawn" and fades back in.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InnRest : Interactable
    {
        [SerializeField] private int settlementId = -1;
        [SerializeField] private string innName = string.Empty;

        [SerializeField, Min(0.5f), Tooltip("How close (planar metres) to the door the player must be.")]
        private float reach = 3f;

        [SerializeField, Range(0f, 24f), Tooltip("Hour the player wakes up.")]
        private float wakeHour = 6f;

        [SerializeField, Min(0.05f), Tooltip("Seconds of the fade to black.")]
        private float fadeOutSeconds = 0.8f;

        [SerializeField, Min(0f), Tooltip("Seconds spent on black.")]
        private float holdSeconds = 1.4f;

        [SerializeField, Min(0.05f), Tooltip("Seconds of the fade back in.")]
        private float fadeInSeconds = 1.4f;

        /// <summary>Raised with the inn's name once a night's rest has moved the clock to the waking hour (the traveller's journal).</summary>
        public static event System.Action<string> Rested;

        /// <summary>Id of the inn in <see cref="Roads.RoadNetwork.Settlements"/>.</summary>
        public int SettlementId => settlementId;

        /// <summary>Name of the inn.</summary>
        public string InnName => innName;

        /// <inheritdoc />
        public override string Prompt => "E — Rest at the inn";

        /// <summary>Sets the inn this door belongs to.</summary>
        public void Initialize(int id, string displayName)
        {
            settlementId = id;
            innName = displayName ?? string.Empty;
        }

        /// <inheritdoc />
        public override bool TryFocus(Vector3 playerPosition, Vector3 viewForward, out float score)
        {
            score = float.PositiveInfinity;
            if (HorseMount.TravellerMounted) return false;   // on horseback: get down first (Interact then dismounts)
            return InReach(playerPosition, viewForward, transform.position, reach, -0.2f, out score);
        }

        /// <inheritdoc />
        public override void Interact(PlayerInteractor interactor)
        {
            if (interactor.Busy) return;
            interactor.StartCoroutine(Rest(interactor));
        }

        private IEnumerator Rest(PlayerInteractor interactor)
        {
            interactor.Busy = true;
            if (interactor.Travel != null) interactor.Travel.Cancel();
            PlayerController controller = interactor.Controller;
            bool controllerWasEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;

            HudController hud = HudController.Instance;
            if (hud != null)
            {
                hud.HidePrompt();
                yield return hud.Fade(1f, fadeOutSeconds);
            }
            else
            {
                yield return new WaitForSeconds(fadeOutSeconds);
            }

            if (DayCycle.Current != null) DayCycle.Current.SetTime(wakeHour);
            Rested?.Invoke(innName);   // the journal writes the night
            if (hud != null) hud.ShowToast("You rest until dawn");
            yield return new WaitForSeconds(holdSeconds);
            if (hud != null) yield return hud.Fade(0f, fadeInSeconds);

            if (controller != null && controllerWasEnabled) controller.enabled = true;
            interactor.Busy = false;
        }
    }
}
