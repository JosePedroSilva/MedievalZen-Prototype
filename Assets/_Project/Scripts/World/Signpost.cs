using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>One arm of a <see cref="Signpost"/>: a road leaving the junction and where it leads.</summary>
    [Serializable]
    public sealed class SignpostDirection
    {
        /// <summary>Road segment leaving the junction.</summary>
        public int segmentId;

        /// <summary>Node at the far end of that segment.</summary>
        public int towardNodeId;

        /// <summary>Horizontal unit direction of the road leaving the junction.</summary>
        public Vector3 direction;

        /// <summary>Nearest settlement reached along this road (empty if none).</summary>
        public string nearestSettlement = string.Empty;

        /// <summary>Road distance to <see cref="nearestSettlement"/> in metres.</summary>
        public float nearestDistance;

        /// <summary>Ids of all settlements whose shortest route from the junction starts on this road, nearest first.</summary>
        public int[] settlementIds = Array.Empty<int>();

        /// <summary>Names matching <see cref="settlementIds"/>.</summary>
        public string[] settlementNames = Array.Empty<string>();

        /// <summary>Road distances matching <see cref="settlementIds"/>.</summary>
        public float[] distances = Array.Empty<float>();
    }

    /// <summary>Signpost at a road junction with three or more roads (interaction is Phase 3).</summary>
    public sealed class Signpost : MonoBehaviour
    {
        [SerializeField] private int junctionNodeId = -1;
        [SerializeField] private List<SignpostDirection> directions = new List<SignpostDirection>();

        /// <summary>Id of the junction node in <see cref="Roads.RoadNetwork.Nodes"/>.</summary>
        public int JunctionNodeId => junctionNodeId;

        /// <summary>One entry per road leaving the junction.</summary>
        public IReadOnlyList<SignpostDirection> Directions => directions;

        /// <summary>Sets the junction and its arms.</summary>
        public void Initialize(int nodeId, List<SignpostDirection> arms)
        {
            junctionNodeId = nodeId;
            directions = arms ?? new List<SignpostDirection>();
        }
    }
}
