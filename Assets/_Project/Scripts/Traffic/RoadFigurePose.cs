using UnityEngine;

namespace MedievalZen.Traffic
{
    /// <summary>
    /// Bone work for a road user, after the Animator and the seated pose (order 126): the head turns towards the traveller by
    /// <see cref="Look"/> (at most 60°), nods once when <see cref="NodTime"/> runs (0.9 s), and by <see cref="Astride"/> the legs take a
    /// rider's seat (thighs forward and apart, shins back down the horse's flanks; the root is placed so the hips sit on the saddle). The
    /// villager clip set has no greeting or riding clip, so these are procedural, about the character's own axes. Nothing is changed while
    /// every weight is 0.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(126)]
    public sealed class RoadFigurePose : MonoBehaviour
    {
        private const float MaxHeadYaw = 60f, NodSeconds = 0.9f, NodAngle = 16f;

        private Transform head, thighL, thighR, shinL, shinR;
        private bool bound;

        /// <summary>Head turn towards <see cref="LookAt"/>, 0..1.</summary>
        public float Look { get; set; }

        /// <summary>Scene position the head turns to.</summary>
        public Vector3 LookAt { get; set; }

        /// <summary>Seconds since the nod began (negative: none).</summary>
        public float NodTime { get; set; } = -1f;

        /// <summary>0 standing … 1 astride a horse.</summary>
        public float Astride { get; set; }

        private void Bind()
        {
            bound = true;
            Animator animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            thighL = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            thighR = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            shinL = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            shinR = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        }

        private void LateUpdate()
        {
            bool nodding = NodTime >= 0f && NodTime < NodSeconds;
            if (Look <= 0.001f && !nodding && Astride <= 0.001f) return;
            if (!bound) Bind();
            Vector3 right = transform.right, forward = transform.forward, up = transform.up;
            // The seat is shared with the traveller's own riding pose (one fix corrects both).
            if (Astride > 0.001f) MedievalZen.Player.AstrideMath.Apply(thighL, thighR, shinL, shinR, right, forward, Astride);

            if (head == null) return;
            if (Look > 0.001f)
            {
                Vector3 to = LookAt - head.position;
                to -= up * Vector3.Dot(to, up);
                if (to.sqrMagnitude > 0.01f)
                {
                    float yaw = Mathf.Clamp(Vector3.SignedAngle(forward, to, up), -MaxHeadYaw, MaxHeadYaw);
                    Swing(head, up, yaw * Mathf.Clamp01(Look));
                }
            }

            if (nodding) Swing(head, right, NodAngle * Mathf.Sin(Mathf.PI * NodTime / NodSeconds));
        }

        private static void Swing(Transform bone, Vector3 axis, float degrees) => bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }
}
