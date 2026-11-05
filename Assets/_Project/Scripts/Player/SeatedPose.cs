using MedievalZen.Travel;
using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>
    /// The seated pose, built procedurally on top of the Animator (the animation pack has no sitting clip). Every LateUpdate,
    /// after the Animator has written this frame's idle pose (and after <see cref="PlayerAnimator"/>, order 110), it solves a
    /// full seated pose on the humanoid bones and blends towards it by <see cref="PlayerSeat.Weight"/> (eased over ~0.8 s):
    /// the hips go to the plan's hip point, the spine leans, each leg is a two-bone solve from the hip to an ankle target
    /// planted on the ground (<see cref="SeatMath.SolveTwoBone"/>) with the foot kept flat as in the idle, and the arms rest on
    /// the thighs, on the knees or behind on the ground. Neck and head are left to the Animator, so the head-look layers
    /// still follow the camera. Nothing is changed while standing. Rotations are blended per bone in local space.
    /// All angles and offsets below are inspector fields for tuning in Play mode.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(120)]
    public sealed class SeatedPose : MonoBehaviour
    {
        private const int Spine = 0, Chest = 1, Head = 2;
        private const int UpperLegL = 3, LowerLegL = 4, FootL = 5, UpperLegR = 6, LowerLegR = 7, FootR = 8;
        private const int UpperArmL = 9, LowerArmL = 10, HandL = 11, UpperArmR = 12, LowerArmR = 13, HandR = 14;
        private const int BoneCount = 15;

        [Header("References (found automatically when empty)")]
        [SerializeField, Tooltip("The character's Animator (humanoid).")]
        private Animator animator;

        [Header("Spine (degrees, + = lean forward)")]
        [SerializeField, Range(-30f, 40f), Tooltip("Lean of the upper body on a seat.")]
        private float seatLean = 7f;

        [SerializeField, Range(-30f, 40f), Tooltip("Lean on the ground with the knees up.")]
        private float kneesUpLean = 14f;

        [SerializeField, Range(-40f, 30f), Tooltip("Lean on the ground with the legs out (negative = leaning back on the hands).")]
        private float legsOutLean = -16f;

        [SerializeField, Range(0f, 1f), Tooltip("Share of the lean taken by the chest (the rest by the lower spine).")]
        private float chestShare = 0.4f;

        [SerializeField, Range(0f, 1f), Tooltip("How much the head counters the lean so the gaze stays level.")]
        private float keepHeadLevel = 0.8f;

        [SerializeField, Range(-30f, 30f), Tooltip("Pelvis tilt (degrees, + = rolled forward).")]
        private float pelvisTilt = 0f;

        [Header("Legs")]
        [SerializeField, Range(0f, 1f), Tooltip("Knees bend outwards this much (bend-hint sideways share).")]
        private float kneesOut = 0.15f;

        [SerializeField, Range(0f, 2f), Tooltip("On a seat: how strongly the knees point forward (vs up).")]
        private float seatKneesForward = 1f;

        [SerializeField, Range(-45f, 60f), Tooltip("Legs out: toes tilt up this many degrees (heels on the ground).")]
        private float legsOutToesUp = 30f;

        [SerializeField, Range(0f, 30f), Tooltip("Feet turn outwards by this many degrees.")]
        private float feetOut = 8f;

        [SerializeField, Range(0f, 0.3f), Tooltip("Ankle height above the ground when it cannot be measured from the idle pose (m).")]
        private float fallbackAnkleHeight = 0.09f;

        [Header("Arms")]
        [SerializeField, Range(0f, 1f), Tooltip("How much the arms take the resting pose (0 = keep the animation's arms).")]
        private float armWeight = 1f;

        [SerializeField, Range(0.2f, 1f), Tooltip("On a seat: hands rest this far along the thigh, hip to knee.")]
        private float handAlongThigh = 0.62f;

        [SerializeField, Range(-0.1f, 0.2f), Tooltip("Hands this far above the thigh / knee line (m).")]
        private float handLift = 0.07f;

        [SerializeField, Range(-0.15f, 0.15f), Tooltip("Hands this far inwards of the thigh (m).")]
        private float handInward = 0.03f;

        [SerializeField, Range(0.05f, 0.5f), Tooltip("Legs out: hands on the ground this far behind the hips (m).")]
        private float handsBehind = 0.26f;

        [SerializeField, Range(0.05f, 0.5f), Tooltip("Legs out: hands this far to the side of the hips (m).")]
        private float handsSide = 0.24f;

        [SerializeField, Range(0f, 1.5f), Tooltip("Elbows point outwards this much (bend-hint sideways share).")]
        private float elbowsOut = 0.6f;

        private PlayerSeat seat;
        private HeldMap heldMap;
        private Transform hips;
        private readonly Transform[] bones = new Transform[BoneCount];
        private readonly Quaternion[] animated = new Quaternion[BoneCount];
        private readonly Quaternion[] target = new Quaternion[BoneCount];
        private bool resolved;
        private bool usable;
        private bool measured;
        private float ankleHeight;
        private float armBlend = 1f;

        private void Awake()
        {
            seat = GetComponent<PlayerSeat>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
        }

        private void Start()
        {
            heldMap = GetComponent<HeldMap>();
        }

        private bool Resolve()
        {
            if (resolved) return usable;
            resolved = true;
            if (animator == null || !animator.isHuman || animator.avatar == null)
            {
                usable = false;
                return false;
            }

            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            bones[Spine] = animator.GetBoneTransform(HumanBodyBones.Spine);
            bones[Chest] = animator.GetBoneTransform(HumanBodyBones.Chest);
            bones[Head] = animator.GetBoneTransform(HumanBodyBones.Head);
            bones[UpperLegL] = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            bones[LowerLegL] = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            bones[FootL] = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            bones[UpperLegR] = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            bones[LowerLegR] = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            bones[FootR] = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            bones[UpperArmL] = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            bones[LowerArmL] = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            bones[HandL] = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            bones[UpperArmR] = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            bones[LowerArmR] = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            bones[HandR] = animator.GetBoneTransform(HumanBodyBones.RightHand);

            usable = hips != null && bones[Spine] != null;
            for (int i = UpperLegL; i <= HandR; i++) usable &= bones[i] != null;
            if (!usable)
            {
                Debug.LogWarning("SeatedPose: the character's avatar lacks humanoid leg/arm bones; sitting keeps the standing pose.", this);
                return false;
            }

            if (hips.position.y - transform.position.y < 0.45f)
            {
                // Synty's own avatar maps Hips to the ground-level Root bone; the corrected avatar is needed.
                Debug.LogWarning("SeatedPose: the avatar's Hips bone sits at the feet (uncorrected Synty avatar?); sitting keeps the standing pose. Run MedievalZen/Player/Build Locomotion Controller.", this);
                usable = false;
            }

            return usable;
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("SeatedPose");

        private void LateUpdate()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            float weight = seat != null && seat.IsActive ? seat.Weight : 0f;
            if (weight <= 0f)
            {
                measured = false;
                return;
            }

            if (!Resolve() || !animator.isActiveAndEnabled) return;
            SeatPlan plan = seat.Plan;

            // The Animator wrote this frame's pose: remember it (local rotations, the hips' local position).
            for (int i = 0; i < BoneCount; i++)
            {
                if (bones[i] != null) animated[i] = bones[i].localRotation;
            }

            Quaternion hipsAnimatedRotation = hips.localRotation;
            Vector3 hipsAnimatedPosition = hips.localPosition;
            Quaternion footLWorld = bones[FootL].rotation;
            Quaternion footRWorld = bones[FootR].rotation;
            if (!measured)
            {
                // First seated frame: the idle pose still stands on the ground, so the ankles' height above it is known.
                float ground = seat.GroundAt(transform.position);
                float ankle = 0.5f * (bones[FootL].position.y + bones[FootR].position.y) - ground;
                ankleHeight = ankle > 0.02f && ankle < 0.3f ? ankle : fallbackAnkleHeight;
                measured = true;
            }

            armBlend = Mathf.MoveTowards(armBlend, heldMap != null && heldMap.IsOpen ? 0f : 1f, Time.deltaTime / 0.4f);

            // ---- the full seated pose
            Vector3 up = Vector3.up;
            Vector3 f = SeatMath.Planar(plan.Facing, transform.forward);
            Vector3 r = new Vector3(f.z, 0f, -f.x);
            hips.position = plan.Hip;
            if (Mathf.Abs(pelvisTilt) > 0.01f) hips.rotation = Quaternion.AngleAxis(pelvisTilt, r) * hips.rotation;

            float lean = plan.Pose == SeatPoseKind.Seat ? seatLean : plan.Pose == SeatPoseKind.GroundKneesUp ? kneesUpLean : legsOutLean;
            Transform chest = bones[Chest] != null ? bones[Chest] : null;
            float spineLean = chest != null ? lean * (1f - chestShare) : lean;
            bones[Spine].rotation = Quaternion.AngleAxis(spineLean, r) * bones[Spine].rotation;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(lean * chestShare, r) * chest.rotation;
            if (bones[Head] != null && keepHeadLevel > 0f) bones[Head].rotation = Quaternion.AngleAxis(-lean * keepHeadLevel, r) * bones[Head].rotation;

            SolveLeg(UpperLegL, LowerLegL, FootL, plan.FootLeft, -1f, plan.Pose, f, r, footLWorld, out Vector3 kneeL);
            SolveLeg(UpperLegR, LowerLegR, FootR, plan.FootRight, 1f, plan.Pose, f, r, footRWorld, out Vector3 kneeR);

            float arms = armWeight * armBlend;
            if (arms > 0f)
            {
                SolveArm(UpperArmL, LowerArmL, HandL, HandTarget(plan, f, r, -1f, UpperLegL, kneeL), ElbowHint(plan.Pose, f, r, -1f));
                SolveArm(UpperArmR, LowerArmR, HandR, HandTarget(plan, f, r, 1f, UpperLegR, kneeR), ElbowHint(plan.Pose, f, r, 1f));
            }

            // ---- blend from the animated pose to it, per bone in local space
            Vector3 hipsSeatedPosition = hips.localPosition;
            Quaternion hipsSeatedRotation = hips.localRotation;
            for (int i = 0; i < BoneCount; i++)
            {
                if (bones[i] != null) target[i] = bones[i].localRotation;
            }

            hips.localPosition = Vector3.Lerp(hipsAnimatedPosition, hipsSeatedPosition, weight);
            hips.localRotation = Quaternion.Slerp(hipsAnimatedRotation, hipsSeatedRotation, weight);
            for (int i = 0; i < BoneCount; i++)
            {
                if (bones[i] == null) continue;
                bool arm = i >= UpperArmL;
                float w = arm ? weight * arms : weight;
                bones[i].localRotation = Quaternion.Slerp(animated[i], target[i], w);
            }
        }

        private void SolveLeg(int upperIndex, int lowerIndex, int footIndex, Vector3 groundPoint, float side, SeatPoseKind pose, Vector3 f, Vector3 r,
            Quaternion footWorld, out Vector3 knee)
        {
            Transform upper = bones[upperIndex], lower = bones[lowerIndex], foot = bones[footIndex];
            Vector3 hipJoint = upper.position;
            Vector3 kneeNow = lower.position;
            float thigh = Vector3.Distance(hipJoint, kneeNow);
            float shin = Vector3.Distance(kneeNow, foot.position);
            Vector3 ankle = groundPoint + Vector3.up * ankleHeight;

            Vector3 hint;
            switch (pose)
            {
                case SeatPoseKind.Seat: hint = f * seatKneesForward + Vector3.up * 0.25f + r * (side * kneesOut); break;
                case SeatPoseKind.GroundKneesUp: hint = Vector3.up + f * 0.35f + r * (side * kneesOut); break;
                default: hint = Vector3.up + f * 0.2f + r * (side * kneesOut * 0.5f); break;
            }

            SeatMath.SolveTwoBone(hipJoint, ankle, thigh, shin, hint, out knee, out Vector3 end);
            upper.rotation = Quaternion.FromToRotation(kneeNow - hipJoint, knee - hipJoint) * upper.rotation;
            Vector3 kneeAfter = lower.position;
            lower.rotation = Quaternion.FromToRotation(foot.position - kneeAfter, end - kneeAfter) * lower.rotation;

            // The foot keeps its idle (flat) world orientation, turned out a little; legs out: heels down, toes up.
            Quaternion flat = Quaternion.AngleAxis(side * feetOut, Vector3.up) * footWorld;
            if (pose == SeatPoseKind.GroundLegsForward) flat = Quaternion.AngleAxis(-legsOutToesUp, r) * flat;
            foot.rotation = flat;
        }

        private Vector3 HandTarget(SeatPlan plan, Vector3 f, Vector3 r, float side, int upperLegIndex, Vector3 knee)
        {
            switch (plan.Pose)
            {
                case SeatPoseKind.Seat:
                {
                    Vector3 hipJoint = bones[upperLegIndex].position;
                    return Vector3.Lerp(hipJoint, knee, handAlongThigh) + Vector3.up * handLift - r * (side * handInward);
                }

                case SeatPoseKind.GroundKneesUp:
                    return knee + Vector3.up * (handLift * 0.6f) - f * 0.05f - r * (side * handInward);
                default:
                {
                    Vector3 p = plan.Hip - f * handsBehind + r * (side * handsSide);
                    p.y = seat.GroundAt(p) + 0.05f;
                    return p;
                }
            }
        }

        private Vector3 ElbowHint(SeatPoseKind pose, Vector3 f, Vector3 r, float side)
        {
            switch (pose)
            {
                case SeatPoseKind.Seat: return r * (side * elbowsOut) - f * 0.3f - Vector3.up * 0.5f;
                case SeatPoseKind.GroundKneesUp: return r * (side * elbowsOut) - Vector3.up * 0.3f - f * 0.2f;
                default: return -f * 0.6f + r * (side * elbowsOut * 0.6f) + Vector3.up * 0.1f;
            }
        }

        private void SolveArm(int upperIndex, int lowerIndex, int handIndex, Vector3 handTarget, Vector3 hint)
        {
            Transform upper = bones[upperIndex], lower = bones[lowerIndex], hand = bones[handIndex];
            Vector3 shoulder = upper.position;
            Vector3 elbowNow = lower.position;
            float upperLength = Vector3.Distance(shoulder, elbowNow);
            float lowerLength = Vector3.Distance(elbowNow, hand.position);
            SeatMath.SolveTwoBone(shoulder, handTarget, upperLength, lowerLength, hint, out Vector3 elbow, out Vector3 end);
            upper.rotation = Quaternion.FromToRotation(elbowNow - shoulder, elbow - shoulder) * upper.rotation;
            Vector3 elbowAfter = lower.position;
            lower.rotation = Quaternion.FromToRotation(hand.position - elbowAfter, end - elbowAfter) * lower.rotation;
        }
    }
}
