using System;
using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>
    /// Pure maths behind the player's locomotion animation (<see cref="PlayerAnimator"/>) and the generated
    /// <c>PlayerLocomotion.controller</c>: the speed and direction blend layout, incline, look angles and lean. No Unity
    /// objects, no state; everything is unit-tested. (The motor's own maths lives in <see cref="MotorMath"/>.)
    /// </summary>
    public static class LocomotionMath
    {
        // ------------------------------------------------------------------ speed → gait blend

        /// <summary>Speed tree child: Idle_Standing.</summary>
        public const int IdleChild = 0;

        /// <summary>Speed tree child: Walk_F slowed down (short, careful steps).</summary>
        public const int SlowWalkChild = 1;

        /// <summary>Speed tree child: Walk_F at its natural speed.</summary>
        public const int WalkChild = 2;

        /// <summary>Speed tree child: Walk_F sped up (brisk walk).</summary>
        public const int BriskWalkChild = 3;

        /// <summary>Speed tree child: Run_F at its natural speed.</summary>
        public const int RunChild = 4;

        /// <summary>Speed tree child: Sprint_F at its natural speed.</summary>
        public const int SprintChild = 5;

        /// <summary>Number of children of the speed tree.</summary>
        public const int SpeedChildCount = 6;

        /// <summary>Playback rate of the slow-walk child (its threshold is this × the natural walk speed).</summary>
        public const float SlowWalkRate = 0.6f;

        /// <summary>Playback rate of the brisk-walk child (its threshold is this × the natural walk speed).</summary>
        public const float BriskWalkRate = 1.25f;

        /// <summary>
        /// Thresholds (m/s) of the speed blend tree: Idle 0, slow walk, walk, brisk walk, run, sprint. Each walk child
        /// plays Walk_F at <c>threshold / walkSpeed</c>, so the blended stride always matches the ground speed and the
        /// motor's 1.4–1.6 m/s stays a pure walk (no run mixed in). Run and sprint sit at their natural speeds.
        /// </summary>
        public static float[] SpeedThresholds(float walkSpeed, float runSpeed, float sprintSpeed)
        {
            walkSpeed = Mathf.Max(0.1f, walkSpeed);
            runSpeed = Mathf.Max(walkSpeed * BriskWalkRate + 0.05f, runSpeed);
            sprintSpeed = Mathf.Max(runSpeed + 0.05f, sprintSpeed);
            return new[] { 0f, walkSpeed * SlowWalkRate, walkSpeed, walkSpeed * BriskWalkRate, runSpeed, sprintSpeed };
        }

        /// <summary>Playback rate (child time scale) of each speed tree child, matching <see cref="SpeedThresholds"/>.</summary>
        public static float[] SpeedTimeScales()
        {
            return new[] { 1f, SlowWalkRate, 1f, BriskWalkRate, 1f, 1f };
        }

        /// <summary>
        /// Where a 1D blend tree with ascending <paramref name="thresholds"/> sits for <paramref name="value"/>:
        /// the lower child index and the weight (0..1) of the next child. Values outside the range clamp to the ends.
        /// </summary>
        public static void Blend1D(float value, float[] thresholds, out int lower, out float weight)
        {
            if (thresholds == null || thresholds.Length == 0) throw new ArgumentException("No thresholds.", nameof(thresholds));
            int last = thresholds.Length - 1;
            if (last == 0 || value <= thresholds[0])
            {
                lower = 0;
                weight = 0f;
                return;
            }

            if (value >= thresholds[last])
            {
                lower = last - 1;
                weight = 1f;
                return;
            }

            lower = 0;
            while (lower < last - 1 && value >= thresholds[lower + 1]) lower++;
            float span = thresholds[lower + 1] - thresholds[lower];
            weight = span > 1e-6f ? (value - thresholds[lower]) / span : 0f;
        }

        /// <summary>
        /// The value sent to the <c>Speed</c> parameter: the planar speed in m/s, with jitter below
        /// <paramref name="deadZone"/> treated as standing still and anything above <paramref name="max"/> clamped.
        /// </summary>
        public static float SpeedParameter(float planarSpeed, float deadZone = 0.05f, float max = 8f)
        {
            if (float.IsNaN(planarSpeed) || planarSpeed < deadZone) return 0f;
            return Mathf.Min(planarSpeed, max);
        }

        // ------------------------------------------------------------------ direction → directional locomotion blend

        /// <summary>Directional child: straight ahead (the forward child is the incline tree of that gait).</summary>
        public const int DirF = 0;

        /// <summary>Directional child: forward-left diagonal (<c>…_FwdStrafeFL</c>).</summary>
        public const int DirFL = 1;

        /// <summary>Directional child: forward-right diagonal (<c>…_FwdStrafeFR</c>).</summary>
        public const int DirFR = 2;

        /// <summary>Directional child: sidestep left (<c>…_FwdStrafeL</c>).</summary>
        public const int DirL = 3;

        /// <summary>Directional child: sidestep right (<c>…_FwdStrafeR</c>).</summary>
        public const int DirR = 4;

        /// <summary>Directional child: backward-left diagonal (<c>…_BckStrafeBL</c>).</summary>
        public const int DirBL = 5;

        /// <summary>Directional child: backward-right diagonal (<c>…_BckStrafeBR</c>).</summary>
        public const int DirBR = 6;

        /// <summary>Directional child: straight back (<c>…_BckStrafeB</c>).</summary>
        public const int DirB = 7;

        /// <summary>Number of directions in each walk/run ring of the locomotion tree.</summary>
        public const int DirectionCount = 8;

        /// <summary>
        /// Unit body-space direction of a directional child (x = right, y = forward), e.g. <see cref="DirL"/> = (-1, 0).
        /// </summary>
        public static Vector2 Direction(int direction)
        {
            const float d = 0.70710678f;
            switch (direction)
            {
                case DirF: return new Vector2(0f, 1f);
                case DirFL: return new Vector2(-d, d);
                case DirFR: return new Vector2(d, d);
                case DirL: return new Vector2(-1f, 0f);
                case DirR: return new Vector2(1f, 0f);
                case DirBL: return new Vector2(-d, -d);
                case DirBR: return new Vector2(d, -d);
                case DirB: return new Vector2(0f, -1f);
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, "Expected 0..7.");
            }
        }

        /// <summary>
        /// True for the speed children that become a full ring of <see cref="DirectionCount"/> directions in the 2D
        /// locomotion tree (slow walk, walk, brisk walk, run). Idle sits at the origin and sprint exists only forward.
        /// </summary>
        public static bool IsDirectionalRing(int speedChild)
        {
            return speedChild == SlowWalkChild || speedChild == WalkChild || speedChild == BriskWalkChild || speedChild == RunChild;
        }

        /// <summary>
        /// Position of a child in the 2D velocity blend tree: the clip's natural body-space velocity (m/s; x = right,
        /// y = forward) times its playback rate. A clip played at rate r covers r × its natural velocity, so placing it
        /// there keeps the blended stride matched to the ground velocity in every direction (no foot sliding).
        /// </summary>
        public static Vector2 ChildPosition(Vector2 naturalVelocity, float timeScale)
        {
            return naturalVelocity * timeScale;
        }

        /// <summary>
        /// Natural body-space velocity (m/s; x = right, y = forward) of a root-motion clip from its measured horizontal
        /// displacement over one cycle, in the source file's axes (FBX: +X = the character's left, +Z = forward).
        /// </summary>
        public static Vector2 NaturalVelocityFromFbx(float fbxDeltaX, float fbxDeltaZ, float cycleSeconds)
        {
            if (cycleSeconds <= 0f) return Vector2.zero;
            return new Vector2(-fbxDeltaX, fbxDeltaZ) / cycleSeconds;
        }

        /// <summary>
        /// The values sent to the <c>VelocityX</c> / <c>VelocityZ</c> parameters: the body-space planar velocity (m/s;
        /// x = right, y = forward). Jitter below <paramref name="deadZone"/> reads as standing still; the length is
        /// clamped to <paramref name="max"/>, keeping the direction.
        /// </summary>
        public static Vector2 VelocityParameter(Vector2 localVelocity, float deadZone = 0.05f, float max = 8f)
        {
            if (float.IsNaN(localVelocity.x) || float.IsNaN(localVelocity.y)) return Vector2.zero;
            float speed = localVelocity.magnitude;
            if (speed < deadZone) return Vector2.zero;
            return speed > max ? localVelocity * (max / speed) : localVelocity;
        }

        // ------------------------------------------------------------------ incline

        /// <summary>
        /// Slope angle in degrees along the horizontal direction <paramref name="direction"/> on ground with
        /// <paramref name="groundNormal"/>: positive when that direction goes uphill. 0 for a degenerate input.
        /// </summary>
        public static float InclineFromNormal(Vector3 groundNormal, Vector3 direction)
        {
            direction.y = 0f;
            float length = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
            if (length < 1e-5f || groundNormal.y < 1e-3f) return 0f;
            float gradient = -(groundNormal.x * direction.x + groundNormal.z * direction.z) / (length * groundNormal.y);
            return Mathf.Atan(gradient) * Mathf.Rad2Deg;
        }

        /// <summary>Climb angle of <paramref name="velocity"/> in degrees (+ uphill); 0 below <paramref name="minPlanarSpeed"/>.</summary>
        public static float InclineFromVelocity(Vector3 velocity, float minPlanarSpeed = 0.2f)
        {
            float planar = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
            if (planar < minPlanarSpeed) return 0f;
            return Mathf.Atan2(velocity.y, planar) * Mathf.Rad2Deg;
        }

        /// <summary>Clamps an incline to ±<paramref name="maxDegrees"/> (the Up25/Down25 clips cover ±25°).</summary>
        public static float ClampIncline(float degrees, float maxDegrees = 25f)
        {
            if (float.IsNaN(degrees)) return 0f;
            return Mathf.Clamp(degrees, -maxDegrees, maxDegrees);
        }

        // ------------------------------------------------------------------ look

        /// <summary>Wraps an angle to (-180, 180].</summary>
        public static float WrapAngle(float degrees)
        {
            float wrapped = degrees % 360f;
            if (wrapped > 180f) wrapped -= 360f;
            else if (wrapped <= -180f) wrapped += 360f;
            return wrapped;
        }

        /// <summary>
        /// How far the character should turn its head/body to follow the camera: the camera yaw relative to the body
        /// (+ = right), clamped to ±<paramref name="maxYaw"/>. When the camera looks further round than
        /// <paramref name="giveUpYaw"/> (towards the character's face), the target returns to 0, so looking straight
        /// back never flips the head from one shoulder to the other.
        /// </summary>
        public static float LookYaw(float cameraYaw, float bodyYaw, float maxYaw = 70f, float giveUpYaw = 135f)
        {
            float relative = WrapAngle(cameraYaw - bodyYaw);
            if (Mathf.Abs(relative) > giveUpYaw) return 0f;
            return Mathf.Clamp(relative, -maxYaw, maxYaw);
        }

        /// <summary>
        /// Look pitch in degrees, + = up, from the camera pitch (Unity convention, + = looking down) minus a neutral
        /// <paramref name="neutralPitch"/> (the third-person camera's resting downward tilt), clamped to ±<paramref name="maxPitch"/>.
        /// </summary>
        public static float LookPitch(float cameraPitch, float neutralPitch = 0f, float maxPitch = 35f)
        {
            if (float.IsNaN(cameraPitch)) return 0f;
            return Mathf.Clamp(-(cameraPitch - neutralPitch), -maxPitch, maxPitch);
        }

        /// <summary>Splits a look angle between the body (<paramref name="bodyShare"/>, 0..1) and the head (the rest); the two sum to <paramref name="total"/>.</summary>
        public static void SplitLook(float total, float bodyShare, out float body, out float head)
        {
            body = total * Mathf.Clamp01(bodyShare);
            head = total - body;
        }

        // ------------------------------------------------------------------ lean, turning, air

        /// <summary>Signed yaw rate in degrees per second between two body yaws (+ = turning right), clamped to ±<paramref name="maxRate"/>.</summary>
        public static float YawRate(float previousYaw, float yaw, float deltaTime, float maxRate = 720f)
        {
            if (deltaTime <= 0f) return 0f;
            return Mathf.Clamp(WrapAngle(yaw - previousYaw) / deltaTime, -maxRate, maxRate);
        }

        /// <summary>
        /// Lean into a turn, -1..1 (+ = lean right): the lateral acceleration (speed × yaw rate) relative to
        /// <paramref name="maxLateralAcceleration"/>, saturating at ±1. Standing still never leans.
        /// </summary>
        public static float Lean(float yawRateDegrees, float planarSpeed, float maxLateralAcceleration = 3f)
        {
            if (maxLateralAcceleration <= 0f || planarSpeed <= 0f) return 0f;
            float lateral = planarSpeed * yawRateDegrees * Mathf.Deg2Rad;
            return Mathf.Clamp(lateral / maxLateralAcceleration, -1f, 1f);
        }

        /// <summary>
        /// The <c>TurnInPlace</c> parameter (degrees per second, + = right): the body's yaw rate while nearly standing
        /// still, 0 while moving faster than <paramref name="maxSpeed"/> or turning slower than <paramref name="minRate"/>.
        /// </summary>
        public static float TurnInPlace(float yawRateDegrees, float planarSpeed, float maxSpeed = 0.25f, float minRate = 10f)
        {
            if (planarSpeed > maxSpeed || Mathf.Abs(yawRateDegrees) < minRate) return 0f;
            return yawRateDegrees;
        }

        /// <summary>Seconds spent off the ground: grows while airborne, resets to 0 on touching the ground.</summary>
        public static float AirTime(float previousAirTime, bool grounded, float deltaTime)
        {
            return grounded ? 0f : previousAirTime + Mathf.Max(0f, deltaTime);
        }
    }
}
