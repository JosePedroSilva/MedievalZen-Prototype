using System;
using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>What the traveller sits on; decides where the hips go, which way they may face and which pose is built.</summary>
    public enum SeatKind
    {
        /// <summary>Not a seat.</summary>
        None = 0,
        /// <summary>Bare ground where the traveller stands.</summary>
        Ground = 1,
        /// <summary>A fallen log: sit along its axis, facing across it.</summary>
        Log = 2,
        /// <summary>A tree stump: round, any facing.</summary>
        Stump = 3,
        /// <summary>A crate: any facing.</summary>
        Crate = 4,
        /// <summary>A chair (or bench): faces away from its back.</summary>
        Chair = 5,
        /// <summary>A low rock or stone slab: any facing, only when its top is at sitting height.</summary>
        Rock = 6,
        /// <summary>A camp fireplace: sit on the ground at the ring, facing the fire.</summary>
        Fire = 7,
        /// <summary>An inn's doorstep: sit on the ground outside the door, facing away from the cabin.</summary>
        Doorstep = 8,
    }

    /// <summary>Which procedural pose <see cref="SeatedPose"/> builds.</summary>
    public enum SeatPoseKind
    {
        /// <summary>Hips on a seat at sitting height, thighs forward, shins down, hands on the thighs.</summary>
        Seat = 0,
        /// <summary>On the ground, knees drawn up, hands resting on the knees.</summary>
        GroundKneesUp = 1,
        /// <summary>On the ground, legs out in front, leaning back on the hands.</summary>
        GroundLegsForward = 2,
    }

    /// <summary>
    /// Recognises seat props by prefab / instance name (the world builders keep the Synty prefab names), so the finite
    /// world's baked props and the endless road's pooled props can be found without a component per prop.
    /// </summary>
    public static class SeatCatalog
    {
        /// <summary>The seat kind of a prop named <paramref name="name"/> (instance names may carry "(Clone)").</summary>
        public static SeatKind Classify(string name)
        {
            if (string.IsNullOrEmpty(name)) return SeatKind.None;
            if (Has(name, "SciFi") || Has(name, "Pile") || Has(name, "Cliff") || Has(name, "Spikey") || Has(name, "Arch") || Has(name, "Crater")) return SeatKind.None;
            // Fantasy Kingdom yard props that only sound like seats: crate and barrel stacks (2 m tall), the forge's workbench, stalls.
            if (Has(name, "_Stack") || Has(name, "Workbench") || Has(name, "Market_Stall")) return SeatKind.None;
            if (Has(name, "Camp_Fireplace")) return SeatKind.Fire;
            if (Has(name, "_Log_") || Has(name, "Bench")) return SeatKind.Log;
            if (Has(name, "_Stump_") || Has(name, "Stool")) return SeatKind.Stump;
            if (Has(name, "_Chair_")) return SeatKind.Chair;
            if (Has(name, "Crate")) return SeatKind.Crate;
            if (Has(name, "_Rock_") && !Has(name, "Wall")) return SeatKind.Rock;
            return SeatKind.None;
        }

        /// <summary>"Sit on the log", "Sit by the fire" … for the Interact prompt.</summary>
        public static string Verb(SeatKind kind)
        {
            switch (kind)
            {
                case SeatKind.Log: return "Sit on the log";
                case SeatKind.Stump: return "Sit on the stump";
                case SeatKind.Crate: return "Sit on the crate";
                case SeatKind.Chair: return "Sit on the chair";
                case SeatKind.Rock: return "Sit on the stone";
                case SeatKind.Fire: return "Sit by the fire";
                case SeatKind.Doorstep: return "Sit on the doorstep";
                default: return "Sit down";
            }
        }

        /// <summary>True for kinds whose hips rest on the prop (the others sit on the ground).</summary>
        public static bool SitsOnProp(SeatKind kind) => kind == SeatKind.Log || kind == SeatKind.Stump || kind == SeatKind.Crate ||
                                                        kind == SeatKind.Chair || kind == SeatKind.Rock;

        private static bool Has(string s, string part) => s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Pure maths of sitting down: the two-bone limb solve, the pose blend weight, where the hips go on a seat, which way the
    /// traveller faces, and the fast flow of time while seated. No Unity objects and no native calls (Vector3 / Mathf only),
    /// so it is unit-tested offline.
    /// </summary>
    public static class SeatMath
    {
        // ------------------------------------------------------------------ two-bone solve

        /// <summary>
        /// Two-bone limb solve (hip-knee-ankle, shoulder-elbow-wrist): from <paramref name="root"/> towards
        /// <paramref name="target"/> with bone lengths <paramref name="upper"/> and <paramref name="lower"/>, bending towards
        /// <paramref name="bendHint"/>. <paramref name="end"/> is the target, pulled in or pushed out along the same line when
        /// it is out of reach (full stretch, or folded as far as the bones allow); <paramref name="joint"/> sits exactly
        /// <paramref name="upper"/> from the root and <paramref name="lower"/> from the end. Returns false when the target was
        /// out of reach.
        /// </summary>
        public static bool SolveTwoBone(Vector3 root, Vector3 target, float upper, float lower, Vector3 bendHint, out Vector3 joint, out Vector3 end)
        {
            upper = Mathf.Max(1e-4f, upper);
            lower = Mathf.Max(1e-4f, lower);
            Vector3 toTarget = target - root;
            float distance = toTarget.magnitude;
            Vector3 dir = distance > 1e-6f ? toTarget / distance : Perpendicular(bendHint, Vector3.down);
            float maxReach = (upper + lower) * 0.9999f;
            float minReach = Mathf.Abs(upper - lower) + 1e-4f;
            float reach = Mathf.Clamp(distance, minReach, maxReach);
            end = root + dir * reach;

            // Law of cosines: distance from the root along dir to the joint's foot, and the joint's height off that line.
            float along = (upper * upper - lower * lower + reach * reach) / (2f * reach);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 bend = bendHint - dir * Vector3.Dot(bendHint, dir);
            bend = bend.sqrMagnitude > 1e-8f ? bend.normalized : Perpendicular(dir, Vector3.forward);
            joint = root + dir * along + bend * height;
            return Mathf.Abs(distance - reach) < 1e-4f;
        }

        private static Vector3 Perpendicular(Vector3 v, Vector3 fallback)
        {
            Vector3 p = Vector3.Cross(v, Vector3.up);
            if (p.sqrMagnitude < 1e-8f) p = Vector3.Cross(v, Vector3.right);
            if (p.sqrMagnitude < 1e-8f) return fallback;
            return p.normalized;
        }

        // ------------------------------------------------------------------ blend weight

        /// <summary>Smoothstep ease of a 0..1 progress (zero slope at both ends).</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Advances a linear 0..1 pose progress towards 1 (sitting) or 0 (standing) over <paramref name="seconds"/>; the pose
        /// weight is <see cref="Ease"/> of it.
        /// </summary>
        public static float StepProgress(float progress, bool sitting, float seconds, float deltaTime)
        {
            float step = seconds > 1e-4f ? Mathf.Max(0f, deltaTime) / seconds : 1f;
            return Mathf.Clamp01(progress + (sitting ? step : -step));
        }

        // ------------------------------------------------------------------ seat alignment

        /// <summary>Unit horizontal direction of <paramref name="v"/>, or <paramref name="fallback"/> when it has no horizontal part.</summary>
        public static Vector3 Planar(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            float m = v.magnitude;
            return m > 1e-5f ? v / m : fallback;
        }

        /// <summary>Unit horizontal direction of a yaw in degrees (0 = +Z, 90 = +X; Unity's convention).</summary>
        public static Vector3 DirectionFromYaw(float yawDegrees)
        {
            float a = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        /// <summary>Yaw in degrees of a horizontal direction (inverse of <see cref="DirectionFromYaw"/>).</summary>
        public static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        /// <summary>
        /// Where on a log (axis from <paramref name="a"/> to <paramref name="b"/>) to sit for someone at <paramref name="from"/>:
        /// the nearest point of the axis, kept <paramref name="endInset"/> metres from either end (the middle for a short log).
        /// </summary>
        public static Vector3 NearestOnAxis(Vector3 from, Vector3 a, Vector3 b, float endInset)
        {
            Vector3 ab = b - a;
            float length = ab.magnitude;
            if (length < 1e-4f) return a;
            Vector3 dir = ab / length;
            float inset = Mathf.Min(Mathf.Max(0f, endInset), length * 0.5f);
            float t = Mathf.Clamp(Vector3.Dot(from - a, dir), inset, length - inset);
            return a + dir * t;
        }

        /// <summary>The spot on a fire's ring (radius <paramref name="ringRadius"/>) on the side of <paramref name="from"/>, at the fire's height.</summary>
        public static Vector3 FireSeatPoint(Vector3 fire, Vector3 from, float ringRadius)
        {
            Vector3 dir = Planar(from - fire, Vector3.back);
            return fire + dir * ringRadius;
        }

        /// <summary>True when a seat top <paramref name="heightAboveGround"/> metres up is a sitting height.</summary>
        public static bool IsSittingHeight(float heightAboveGround, float min = 0.22f, float max = 0.78f) =>
            !float.IsNaN(heightAboveGround) && heightAboveGround >= min && heightAboveGround <= max;

        /// <summary>Hip joint target on a seat whose top centre is <paramref name="seatTop"/>.</summary>
        public static Vector3 HipOnSeat(Vector3 seatTop, Vector3 facing, float hipAboveSeat, float forwardShift) =>
            seatTop + Vector3.up * hipAboveSeat + Planar(facing, Vector3.forward) * forwardShift;

        /// <summary>Where the body (capsule) stands while seated: in front of the hips by <paramref name="forward"/> metres.</summary>
        public static Vector3 ApproachPoint(Vector3 hip, Vector3 facing, float forward)
        {
            Vector3 p = hip + Planar(facing, Vector3.forward) * forward;
            p.y = hip.y;
            return p;
        }

        /// <summary>
        /// How good a view one direction offers (higher is better): falling ground (downhill, over a valley), water, and room to
        /// look (anything solid closer than 5 m costs a lot). Drops are ground here minus ground there (positive = downhill) at
        /// 8, 25 and 60 m; <paramref name="blockedAt"/> is the distance to the first obstruction at eye level (∞ = none).
        /// </summary>
        public static float ViewScore(float drop8, float drop25, float drop60, bool water25, bool water60, float blockedAt)
        {
            float score = 0.3f * Mathf.Clamp(drop8 / 2f, -1f, 1f) + 0.6f * Mathf.Clamp(drop25 / 6f, -1f, 1f) + 0.6f * Mathf.Clamp(drop60 / 15f, -1f, 1f);
            if (water25) score += 0.8f;
            else if (water60) score += 0.5f;
            if (blockedAt < 5f) score -= (5f - Mathf.Max(0f, blockedAt)) / 5f * 1.5f + 0.5f;
            return score;
        }

        /// <summary>
        /// Picks the direction with the best score plus a bonus for agreeing with <paramref name="hint"/> (weight
        /// <paramref name="hintWeight"/> × cosine). Only the first <paramref name="count"/> entries are considered; returns the index.
        /// </summary>
        public static int ChooseFacing(float[] scores, Vector3[] directions, int count, Vector3 hint, float hintWeight)
        {
            int best = 0;
            float bestScore = float.NegativeInfinity;
            Vector3 h = Planar(hint, Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                float s = scores[i] + (h.sqrMagnitude > 0f ? hintWeight * Vector3.Dot(Planar(directions[i], Vector3.zero), h) : 0f);
                if (s > bestScore + 1e-6f)
                {
                    bestScore = s;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Slope in degrees from four ground heights <paramref name="span"/> metres apart (−x, +x, −z, +z).</summary>
        public static float SlopeDegrees(float westHeight, float eastHeight, float southHeight, float northHeight, float span)
        {
            if (span <= 1e-4f) return 0f;
            float gx = (eastHeight - westHeight) / (2f * span);
            float gz = (northHeight - southHeight) / (2f * span);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------ time while seated

        /// <summary>
        /// Eases the time multiplier towards <paramref name="target"/> in log space (so ×1 → ×20 feels even), with sharpness
        /// <paramref name="rate"/> per second. Snaps when within half a percent.
        /// </summary>
        public static float StepTimeFlow(float current, float target, float rate, float deltaTime)
        {
            current = Mathf.Max(1e-3f, current);
            target = Mathf.Max(1e-3f, target);
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, rate) * Mathf.Max(0f, deltaTime));
            float next = Mathf.Exp(Mathf.Lerp(Mathf.Log(current), Mathf.Log(target), k));
            return Mathf.Abs(next / target - 1f) < 0.005f ? target : next;
        }
    }
}
