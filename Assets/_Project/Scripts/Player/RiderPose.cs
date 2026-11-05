using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>
    /// The rider's seat, shared by the traveller (<see cref="RiderPose"/>) and the road's riders (<c>Traffic.RoadFigurePose</c>) so one
    /// fix corrects both: thighs forward and apart, shins back down the horse's flanks, about the character's own axes, and the hips on
    /// a saddle at <see cref="SaddleFraction"/> of the horse's measured height. Rated low confidence: only the user can judge it in Unity.
    /// </summary>
    public static class AstrideMath
    {
        /// <summary>Degrees: thighs forward, thighs apart, shins back.</summary>
        public const float Thigh = 55f, Spread = 22f, Shin = 60f;

        /// <summary>Saddle height as a fraction of the horse's measured (head) height.</summary>
        public const float SaddleFraction = 0.66f;

        /// <summary>Bends the legs into the seat by <paramref name="weight"/> (0..1), after the Animator has posed them.</summary>
        public static void Apply(Transform thighL, Transform thighR, Transform shinL, Transform shinR, Vector3 right, Vector3 forward, float weight)
        {
            if (thighL == null || thighR == null || shinL == null || shinR == null) return;
            float w = Mathf.Clamp01(weight);
            Swing(thighL, right, -Thigh * w);
            Swing(thighR, right, -Thigh * w);
            Swing(thighL, forward, -Spread * w);
            Swing(thighR, forward, Spread * w);
            Swing(shinL, right, Shin * w);
            Swing(shinR, right, Shin * w);
        }

        /// <summary>Rotates a bone about a world axis.</summary>
        public static void Swing(Transform bone, Vector3 axis, float degrees) => bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    /// <summary>
    /// The traveller astride (a bone pass after the Animator and the seated pose: order 122). By <see cref="Weight"/> the hips rise
    /// by <see cref="Rise"/> plus the gait's <see cref="Bob"/> and the legs take <see cref="AstrideMath"/>'s seat. The
    /// <see cref="PlayerAnimator"/> is told to stand still meanwhile (<see cref="PlayerAnimator.StandStill"/>) so the walk cycle stops.
    /// Nothing is changed while the weight is 0. Driven by <see cref="HorseMount"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(122)]
    public sealed class RiderPose : MonoBehaviour
    {
        private Transform hips, thighL, thighR, shinL, shinR;
        private bool bound;
        private float hipHeight = 0.95f;
        private bool hipMeasured;
        private PlayerSeat seat;

        /// <summary>0 on foot … 1 astride.</summary>
        public float Weight { get; set; }

        /// <summary>Metres the hips rise to the saddle at full weight.</summary>
        public float Rise { get; set; }

        /// <summary>The gait's rise and fall this frame, metres (applied at full weight).</summary>
        public float Bob { get; set; }

        /// <summary>Height of the standing hips above the body's root (measured on foot; 0.95 m until then).</summary>
        public float HipHeight => hipHeight;

        private void Bind()
        {
            PlayerAnimator pa = GetComponent<PlayerAnimator>();
            Animator animator = pa != null ? pa.Animator : GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            bound = true;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            thighL = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            thighR = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            shinL = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            shinR = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        }

        private void LateUpdate()
        {
            if (!bound) Bind();
            if (hips == null) return;
            if (Weight <= 0.001f)
            {
                // Standing and untouched: measure the hips for the saddle's rise.
                float h = hips.position.y - transform.position.y;
                if (seat == null) seat = GetComponent<PlayerSeat>();
                if (h > 0.5f && h < 1.4f && (seat == null || !seat.IsActive))   // not while sitting (the seated pose lowers the hips)
                {
                    hipHeight = hipMeasured ? Mathf.Lerp(hipHeight, h, 0.05f) : h;
                    hipMeasured = true;
                }

                return;
            }

            float w = Mathf.Clamp01(Weight);
            hips.position += Vector3.up * ((Rise + Bob) * w);
            AstrideMath.Apply(thighL, thighR, shinL, shinR, transform.right, transform.forward, w);
        }
    }
}
