using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>Sight queries for the cinematic camera (the instanced trees have no colliders beyond the trunks near the traveller).</summary>
    public sealed partial class EndlessWorld
    {
        /// <summary>True when the sight line between two scene points passes through a near tree's trunk or crown core.</summary>
        public bool TreeBlocksSight(Vector3 from, Vector3 to) => trees != null && trees.SegmentBlocked(from, to);

        /// <summary>True when a scene point is within <paramref name="pad"/> metres of a near tree's trunk or inside its crown core.</summary>
        public bool InsideTree(Vector3 point, float pad) => trees != null && trees.PointInside(point, pad);
    }
}
