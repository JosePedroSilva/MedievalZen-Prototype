using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// A named destination on the road network. Always sits on exactly one <see cref="RoadNode"/>.
    /// Created only through <see cref="RoadNetworkBuilder"/>.
    /// </summary>
    public sealed class Settlement
    {
        internal Settlement(int id, string name, SettlementType type, RoadNode node, float radius)
        {
            Id = id;
            Name = name ?? string.Empty;
            Type = type;
            Node = node;
            Radius = radius;
        }

        /// <summary>Index of this settlement in <see cref="RoadNetwork.Settlements"/>.</summary>
        public int Id { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Town, village or inn.</summary>
        public SettlementType Type { get; }

        /// <summary>World position of the settlement centre (same as <see cref="Node"/>'s position).</summary>
        public Vector3 Position => Node.Position;

        /// <summary>The road node at the settlement centre.</summary>
        public RoadNode Node { get; }

        /// <summary>Radius of the settlement footprint in metres (arrival trigger, scatter exclusion).</summary>
        public float Radius { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Type} '{Name}' #{Id}";
    }
}
