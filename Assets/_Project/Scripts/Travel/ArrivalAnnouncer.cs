using MedievalZen.Roads;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Toasts "You arrive at &lt;name&gt;" once per visit: when the player enters a settlement's footprint (its
    /// <see cref="SettlementMarker"/> radius; polled a few times a second rather than relying on trigger physics) or when
    /// auto-travel reports <see cref="PlayerTravel.Arrived"/>. Re-arms once the player is well outside again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArrivalAnnouncer : MonoBehaviour
    {
        [SerializeField, Tooltip("Travel state; found on this GameObject if empty.")]
        private PlayerTravel travel;

        [SerializeField, Min(0.05f), Tooltip("Seconds between footprint checks.")]
        private float checkInterval = 0.25f;

        [SerializeField, Min(0f), Tooltip("Extra metres beyond the footprint radius before a visit counts as over.")]
        private float leaveMargin = 20f;

        // Keyed by name (unique in a world) rather than index, so the state survives the endless road replacing its network.
        private readonly System.Collections.Generic.HashSet<string> announced = new System.Collections.Generic.HashSet<string>();
        private float nextCheck;

        private void Awake()
        {
            if (travel == null) travel = GetComponent<PlayerTravel>();
        }

        private void OnEnable()
        {
            if (travel != null) travel.Arrived += Announce;
        }

        private void OnDisable()
        {
            if (travel != null) travel.Arrived -= Announce;
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("ArrivalAnnouncer");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            RoadNetwork roads = GameWorld.Current != null ? GameWorld.Current.Roads : null;
            if (roads == null) return;
            var settlements = roads.Settlements;

            Vector3 p = transform.position;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                float dx = s.Position.x - p.x;
                float dz = s.Position.z - p.z;
                float sqr = dx * dx + dz * dz;
                bool done = announced.Contains(s.Name);
                if (!done && sqr <= s.Radius * s.Radius)
                {
                    Announce(s);
                }
                else if (done)
                {
                    float leave = s.Radius + leaveMargin;
                    if (sqr > leave * leave) announced.Remove(s.Name);
                }
            }
        }

        private static readonly int CostAnnounce = Review.FrameCost.Register("ArrivalAnnouncer.Announce");

        private void Announce(Settlement settlement)
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(CostAnnounce);   // stall hunt: the per-frame culprit
            if (settlement == null) return;
            if (!announced.Add(settlement.Name)) return;

            HudController hud = HudController.Instance;
            if (hud == null) return;
            hud.ShowToast(settlement.Type == SettlementType.Inn
                ? $"You arrive at {settlement.Name}. Rest at the door (E)."
                : $"You arrive at {settlement.Name}");
        }
    }
}
