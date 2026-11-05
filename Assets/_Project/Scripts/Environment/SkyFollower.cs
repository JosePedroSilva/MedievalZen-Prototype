using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Keeps the sky root (skydome, cloud ring, horizon fog ring) centred on the player: copies the target's position,
    /// never its rotation, every <c>LateUpdate</c>, so the player can never walk up to the edge of the sky.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyFollower : MonoBehaviour
    {
        [SerializeField, Tooltip("Transform to follow (the player rig). Falls back to the main camera when empty.")]
        private Transform target;

        [SerializeField, Tooltip("Follow the target's height as well (otherwise only X/Z).")]
        private bool followHeight = true;

        /// <summary>Transform the sky follows.</summary>
        public Transform Target
        {
            get => target;
            set => target = value;
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("SkyFollower");

        private void LateUpdate()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (target == null)
            {
                Camera main = Camera.main;
                if (main == null) return;
                target = main.transform;
            }

            Vector3 p = target.position;
            if (!followHeight) p.y = transform.position.y;
            transform.position = p;
        }
    }
}
