using System;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>Serialisable form of a <see cref="RoadNode"/> (see <see cref="RoadNetworkData"/>).</summary>
    [Serializable]
    public struct RoadNodeData
    {
        /// <summary>World position.</summary>
        public Vector3 position;
    }
}
