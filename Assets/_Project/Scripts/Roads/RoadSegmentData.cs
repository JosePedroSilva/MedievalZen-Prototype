using System;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>Serialisable form of a <see cref="RoadSegment"/> (see <see cref="RoadNetworkData"/>).</summary>
    [Serializable]
    public struct RoadSegmentData
    {
        /// <summary>Index into <see cref="RoadNetworkData.nodes"/> of the start node.</summary>
        public int nodeA;

        /// <summary>Index into <see cref="RoadNetworkData.nodes"/> of the end node.</summary>
        public int nodeB;

        /// <summary>Polyline vertices from A to B.</summary>
        public Vector3[] points;
    }
}
