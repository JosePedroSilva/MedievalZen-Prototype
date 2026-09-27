using System.Collections.Generic;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Makes a junction <see cref="Signpost"/> readable: within <see cref="reach"/> metres and facing it, the HUD shows
    /// "E — Read signpost"; Interact opens the <see cref="SignpostMenu"/> listing where each road leads, with road distances.
    /// </summary>
    [RequireComponent(typeof(Signpost))]
    [DisallowMultipleComponent]
    public sealed class SignpostInteractable : Interactable
    {
        [SerializeField, Min(0.5f), Tooltip("How close (planar metres) the player must be to read the sign.")]
        private float reach = 3.5f;

        [SerializeField, Range(-1f, 1f), Tooltip("Minimum cosine between the view direction and the direction to the sign.")]
        private float minFacing = 0.3f;

        private Signpost signpost;
        private List<SignpostChoice> choices;

        /// <inheritdoc />
        public override string Prompt => "E — Read signpost";

        /// <summary>Destinations this sign offers, nearest first (computed on first use).</summary>
        public IReadOnlyList<SignpostChoice> Choices
        {
            get
            {
                if (choices != null) return choices;
                RoadNetwork roads = GameWorld.Current != null ? GameWorld.Current.Roads : null;
                RoadNode junction = null;
                if (roads != null && Signpost != null && Signpost.JunctionNodeId >= 0 && Signpost.JunctionNodeId < roads.Nodes.Count)
                {
                    junction = roads.Nodes[Signpost.JunctionNodeId];
                }

                choices = SignpostChoices.Build(roads, junction);
                return choices;
            }
        }

        private Signpost Signpost => signpost != null ? signpost : signpost = GetComponent<Signpost>();

        /// <inheritdoc />
        public override bool TryFocus(Vector3 playerPosition, Vector3 viewForward, out float score)
        {
            bool ok = InReach(playerPosition, viewForward, transform.position, reach, minFacing, out score);
            return ok;
        }

        /// <inheritdoc />
        public override void Interact(PlayerInteractor interactor)
        {
            var menu = interactor.GetComponent<SignpostMenu>();
            if (menu == null) menu = interactor.gameObject.AddComponent<SignpostMenu>();
            menu.Open(this, Choices);
        }
    }
}
