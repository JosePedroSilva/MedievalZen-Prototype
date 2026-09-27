using System;

namespace MedievalZen.Roads
{
    /// <summary>Serialisable form of a <see cref="Settlement"/> (see <see cref="RoadNetworkData"/>).</summary>
    [Serializable]
    public struct SettlementData
    {
        /// <summary>Display name.</summary>
        public string name;

        /// <summary>Town, village or inn.</summary>
        public SettlementType type;

        /// <summary>Index into <see cref="RoadNetworkData.nodes"/> of the settlement's node.</summary>
        public int node;

        /// <summary>Footprint radius in metres.</summary>
        public float radius;
    }
}
