using UnityEngine;

namespace MedievalZen.People
{
    /// <summary>
    /// A villager's seated pose, laid procedurally over the Animator's idle (the clip set has no sitting clip): after the Animator has
    /// written the frame (order 125, after the player's <c>SeatedPose</c>), by <see cref="Weight"/> the hips drop to the seat, the thighs
    /// swing forward to level and the shins back down to the ground, and the hands come to rest a little forward. Rotations are applied
    /// about the character's own right axis, so the Synty bone axes do not matter. Nothing is changed while <see cref="Weight"/> is 0.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(125)]
    public sealed class VillagerPose : MonoBehaviour
    {
        [SerializeField, Range(40f, 100f), Tooltip("Thigh swing forward when seated, degrees.")]
        private float thighAngle = 82f;

        [SerializeField, Range(40f, 110f), Tooltip("Shin swing back down when seated, degrees.")]
        private float shinAngle = 84f;

        [SerializeField, Range(0f, 40f), Tooltip("Upper arms forward when seated, degrees.")]
        private float armAngle = 18f;

        [SerializeField, Range(0f, 0.3f), Tooltip("Hips this far above the seat, metres.")]
        private float hipsAboveSeat = 0.1f;

        private Animator animator;
        private Transform hips, thighL, thighR, shinL, shinR, armL, armR;
        private bool bound;

        /// <summary>0 standing … 1 seated.</summary>
        public float Weight { get; set; }

        /// <summary>Seat height above the ground under the villager's root, metres.</summary>
        public float SeatHeight { get; set; } = 0.45f;

        /// <summary>Hips above the feet when standing (from the palette's measurement), metres.</summary>
        public float StandingHipHeight { get; set; } = 0.9f;

        private void Bind()
        {
            bound = true;
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            thighL = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            thighR = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            shinL = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            shinR = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            armL = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            armR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("VillagerPose");

        private void LateUpdate()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (Weight <= 0.001f) return;
            if (!bound) Bind();
            if (hips == null || thighL == null || thighR == null || shinL == null || shinR == null) return;
            float w = Mathf.Clamp01(Weight);
            Vector3 right = transform.right;
            float drop = Mathf.Max(0f, StandingHipHeight - (SeatHeight + hipsAboveSeat));
            hips.position += Vector3.down * (drop * w);
            Swing(thighL, right, -thighAngle * w);
            Swing(thighR, right, -thighAngle * w);
            Swing(shinL, right, shinAngle * w);
            Swing(shinR, right, shinAngle * w);
            if (armL != null) Swing(armL, right, -armAngle * w);
            if (armR != null) Swing(armR, right, -armAngle * w);
        }

        private static void Swing(Transform bone, Vector3 axis, float degrees) => bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }
}
