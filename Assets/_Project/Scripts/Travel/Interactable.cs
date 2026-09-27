using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Something in the world the player can use with <c>Interact</c> (signposts, inn doors). Enabled instances register in
    /// <see cref="All"/>; the <see cref="PlayerInteractor"/> picks the best one in range each frame and shows its prompt.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        private static readonly List<Interactable> Registry = new List<Interactable>(32);

        /// <summary>All enabled interactables in the loaded scenes.</summary>
        public static IReadOnlyList<Interactable> All => Registry;

        /// <summary>Prompt shown while this is the focused interactable (e.g. "E — Read signpost").</summary>
        public abstract string Prompt { get; }

        /// <summary>
        /// Whether the player at <paramref name="playerPosition"/>, looking along the horizontal <paramref name="viewForward"/>,
        /// can use this now. <paramref name="score"/> ranks candidates (lower = better, e.g. distance).
        /// </summary>
        public abstract bool TryFocus(Vector3 playerPosition, Vector3 viewForward, out float score);

        /// <summary>Uses this interactable.</summary>
        public abstract void Interact(PlayerInteractor interactor);

        /// <summary>Registers the instance.</summary>
        protected virtual void OnEnable()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
        }

        /// <summary>Unregisters the instance.</summary>
        protected virtual void OnDisable()
        {
            Registry.Remove(this);
        }

        /// <summary>Planar distance and facing test shared by the implementations.</summary>
        protected static bool InReach(Vector3 playerPosition, Vector3 viewForward, Vector3 target, float radius, float minFacing,
            out float planarDistance)
        {
            Vector3 offset = target - playerPosition;
            float dy = offset.y;
            offset.y = 0f;
            planarDistance = offset.magnitude;
            if (planarDistance > radius || Mathf.Abs(dy) > 4f) return false;
            if (planarDistance < 1f) return true;
            viewForward.y = 0f;
            if (viewForward.sqrMagnitude < 1e-4f) return true;
            return Vector3.Dot(viewForward.normalized, offset / planarDistance) >= minFacing;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
        }
    }
}
