using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Added at runtime next to <see cref="EndlessWorld"/>: submits the instanced trees after every camera has moved this frame
    /// (late in LateUpdate, after the camera rig at the default order), so their frustum culling and LOD use this frame's view.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("")]
    internal sealed class EndlessLateDraw : MonoBehaviour
    {
        /// <summary>The streamer that draws.</summary>
        public EndlessWorld World;

        private static readonly int FrameCostId = Review.FrameCost.Register("EndlessLateDraw (trees)");

        private void LateUpdate()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (World != null && World.isActiveAndEnabled) World.LateDraw();
        }
    }
}
