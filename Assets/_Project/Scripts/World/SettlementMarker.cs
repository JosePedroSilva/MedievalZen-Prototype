using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Scene marker for a generated settlement: identity plus a trigger sphere covering its footprint.
    /// Arrival logic (Phase 3) listens to the trigger; the settlement itself is <see cref="Resolve"/>d from the
    /// <see cref="GameWorld"/> road network by id.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class SettlementMarker : MonoBehaviour
    {
        [SerializeField] private int settlementId = -1;
        [SerializeField] private string settlementName = string.Empty;
        [SerializeField] private SettlementType settlementType;
        [SerializeField] private float radius;

        /// <summary>Id of the settlement in <see cref="RoadNetwork.Settlements"/>.</summary>
        public int SettlementId => settlementId;

        /// <summary>Display name.</summary>
        public string SettlementName => settlementName;

        /// <summary>Town, village or inn.</summary>
        public SettlementType SettlementType => settlementType;

        /// <summary>Footprint radius in metres.</summary>
        public float Radius => radius;

        /// <summary>Sets the identity and sizes the trigger.</summary>
        public void Initialize(int id, string displayName, SettlementType type, float footprintRadius)
        {
            settlementId = id;
            settlementName = displayName ?? string.Empty;
            settlementType = type;
            radius = footprintRadius;
            var trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = footprintRadius;
        }

        /// <summary>The settlement this marker stands for in <paramref name="world"/>'s network, or null.</summary>
        public Settlement Resolve(GameWorld world)
        {
            if (world == null) return null;
            var settlements = world.Roads.Settlements;
            return settlementId >= 0 && settlementId < settlements.Count ? settlements[settlementId] : null;
        }
    }
}
