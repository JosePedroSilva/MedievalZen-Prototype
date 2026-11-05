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

        /// <summary>
        /// The nearest near tree whose prototype name contains <paramref name="nameKey"/>, between <paramref name="minDistance"/>
        /// and <paramref name="maxDistance"/> of a scene point (base, height and crown radius); used by the ambient effects.
        /// </summary>
        public bool NearestTree(Vector3 point, float minDistance, float maxDistance, string nameKey, out Vector3 position, out float height, out float crown)
        {
            position = default;
            height = crown = 0f;
            return trees != null && trees.NearestTree(point, minDistance, maxDistance, nameKey, out position, out height, out crown);
        }
    }
}
