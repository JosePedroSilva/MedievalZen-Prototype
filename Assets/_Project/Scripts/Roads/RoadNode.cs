using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// A vertex of the road graph: a settlement centre, a junction, or a road end.
    /// Created only through <see cref="RoadNetworkBuilder"/>.
    /// </summary>
    public sealed class RoadNode
    {
        private readonly List<RoadSegment> segments = new List<RoadSegment>(4);

        internal RoadNode(int id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        /// <summary>Index of this node in <see cref="RoadNetwork.Nodes"/>.</summary>
        public int Id { get; }

        /// <summary>World position.</summary>
        public Vector3 Position { get; }

        /// <summary>Segments that start or end at this node.</summary>
        public IReadOnlyList<RoadSegment> Segments => segments;

        /// <summary>The settlement at this node, or <c>null</c> for a plain junction.</summary>
        public Settlement Settlement { get; internal set; }

        /// <summary>True when three or more roads meet here (signpost candidate).</summary>
        public bool IsJunction => segments.Count >= 3;

        internal void AddSegment(RoadSegment segment) => segments.Add(segment);

        /// <inheritdoc />
        public override string ToString() => Settlement != null ? $"Node #{Id} ({Settlement.Name})" : $"Node #{Id}";
    }
}
