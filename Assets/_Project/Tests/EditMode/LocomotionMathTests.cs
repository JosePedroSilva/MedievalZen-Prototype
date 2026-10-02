using MedievalZen.Player;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class LocomotionMathTests
    {
        // Natural speeds of the Synty Walk_F / Run_F / Sprint_F clips (measured from the root-motion twins).
        private const float Walk = 1.452f;
        private const float Run = 2.585f;
        private const float Sprint = 7.243f;
        private const float Tolerance = 1e-4f;

        private static float[] Thresholds => LocomotionMath.SpeedThresholds(Walk, Run, Sprint);

        /// <summary>Ground speed the speed tree produces at a blend position: thresholds are the children's ground speeds.</summary>
        private static float BlendedGroundSpeed(float speed)
        {
            float[] thresholds = Thresholds;
            LocomotionMath.Blend1D(speed, thresholds, out int lower, out float weight);
            return Mathf.Lerp(thresholds[lower], thresholds[lower + 1], weight);
        }

        /// <summary>Unit normal of ground rising towards +Z at <paramref name="degrees"/>.</summary>
        private static Vector3 SlopeNormal(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Cos(radians), -Mathf.Sin(radians));
        }

        // ------------------------------------------------------------------ speed mapping

        [Test]
        public void ThresholdsAscendAndWalkChildrenMatchTheirPlaybackRate()
        {
            float[] thresholds = Thresholds;
            float[] rates = LocomotionMath.SpeedTimeScales();
            Assert.That(thresholds.Length, Is.EqualTo(LocomotionMath.SpeedChildCount));
            Assert.That(rates.Length, Is.EqualTo(LocomotionMath.SpeedChildCount));
            for (int i = 1; i < thresholds.Length; i++) Assert.That(thresholds[i], Is.GreaterThan(thresholds[i - 1]));

            // Walk_F played at rate r covers r × its natural speed, so feet stay planted.
            foreach (int child in new[] { LocomotionMath.SlowWalkChild, LocomotionMath.WalkChild, LocomotionMath.BriskWalkChild })
            {
                Assert.That(thresholds[child], Is.EqualTo(Walk * rates[child]).Within(Tolerance));
            }

            Assert.That(thresholds[LocomotionMath.RunChild], Is.EqualTo(Run).Within(Tolerance));
            Assert.That(thresholds[LocomotionMath.SprintChild], Is.EqualTo(Sprint).Within(Tolerance));
        }

        [Test]
        public void StandingStillIsPureIdle()
        {
            LocomotionMath.Blend1D(LocomotionMath.SpeedParameter(0f), Thresholds, out int lower, out float weight);
            Assert.That(lower, Is.EqualTo(LocomotionMath.IdleChild));
            Assert.That(weight, Is.EqualTo(0f));
            Assert.That(LocomotionMath.SpeedParameter(0.02f), Is.EqualTo(0f), "jitter below the dead zone counts as standing");
        }

        [TestCase(1.4f)]
        [TestCase(1.6f)]
        public void MotorWalkingSpeedsArePureWalkWithoutFootSliding(float speed)
        {
            LocomotionMath.Blend1D(LocomotionMath.SpeedParameter(speed), Thresholds, out int lower, out float weight);
            Assert.That(lower, Is.GreaterThanOrEqualTo(LocomotionMath.SlowWalkChild));
            Assert.That(lower + 1, Is.LessThanOrEqualTo(LocomotionMath.BriskWalkChild), "no run or idle mixed into a relaxed walk");
            Assert.That(weight, Is.InRange(0f, 1f));
            Assert.That(BlendedGroundSpeed(speed), Is.EqualTo(speed).Within(1e-3f));
        }

        [Test]
        public void SprintSpeedBlendsMostlyRunWithSomeSprint()
        {
            const float speed = 3.5f;
            LocomotionMath.Blend1D(LocomotionMath.SpeedParameter(speed), Thresholds, out int lower, out float weight);
            Assert.That(lower, Is.EqualTo(LocomotionMath.RunChild));
            Assert.That(weight, Is.GreaterThan(0f).And.LessThan(0.3f));
            Assert.That(BlendedGroundSpeed(speed), Is.EqualTo(speed).Within(1e-3f));
        }

        [Test]
        public void Blend1DClampsOutsideTheRange()
        {
            float[] thresholds = Thresholds;
            LocomotionMath.Blend1D(-1f, thresholds, out int lower, out float weight);
            Assert.That(lower, Is.EqualTo(0));
            Assert.That(weight, Is.EqualTo(0f));
            LocomotionMath.Blend1D(100f, thresholds, out lower, out weight);
            Assert.That(lower, Is.EqualTo(thresholds.Length - 2));
            Assert.That(weight, Is.EqualTo(1f));
            Assert.That(LocomotionMath.SpeedParameter(20f, 0.05f, 8f), Is.EqualTo(8f));
        }

        // ------------------------------------------------------------------ direction (2D velocity blend)

        [Test]
        public void DirectionsAreUnitAndMirrored()
        {
            for (int i = 0; i < LocomotionMath.DirectionCount; i++)
            {
                Assert.That(LocomotionMath.Direction(i).magnitude, Is.EqualTo(1f).Within(Tolerance));
            }

            Assert.That(LocomotionMath.Direction(LocomotionMath.DirF), Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(LocomotionMath.Direction(LocomotionMath.DirL).x, Is.EqualTo(-1f), "left is -x");
            Assert.That(LocomotionMath.Direction(LocomotionMath.DirR).x, Is.EqualTo(1f));
            Assert.That(LocomotionMath.Direction(LocomotionMath.DirB).y, Is.EqualTo(-1f));
            Assert.That(LocomotionMath.Direction(LocomotionMath.DirFL) + LocomotionMath.Direction(LocomotionMath.DirBR), Is.EqualTo(Vector2.zero));
            Assert.That(LocomotionMath.Direction(LocomotionMath.DirFR) + LocomotionMath.Direction(LocomotionMath.DirBL), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void FbxDisplacementBecomesBodySpaceVelocity()
        {
            // Walk_FwdStrafeL root-motion twin: Root +150 cm along FBX X over 31 frames at 30 fps → moving left at 1.452 m/s.
            Vector2 left = LocomotionMath.NaturalVelocityFromFbx(1.5f, 0f, 31f / 30f);
            Assert.That(left.x, Is.EqualTo(-Walk).Within(1e-3f));
            Assert.That(left.y, Is.EqualTo(0f));
            Vector2 backRight = LocomotionMath.NaturalVelocityFromFbx(-1.06f, -1.06f, 31f / 30f);
            Assert.That(backRight.x, Is.GreaterThan(0f));
            Assert.That(backRight.y, Is.LessThan(0f));
            Assert.That(backRight.magnitude, Is.EqualTo(Walk).Within(0.01f));
        }

        [TestCase(1.4f)]
        [TestCase(1.6f)]
        public void WalkingInAnyDirectionStaysBetweenTheWalkRings(float speed)
        {
            // Every walk strafe clip measured 1.45 m/s like Walk_F, so in each direction the slow ring lies below and the
            // brisk ring above the motor's walking speeds: a relaxed sidestep or back step is a pure walk, stride-matched.
            float[] rates = LocomotionMath.SpeedTimeScales();
            for (int direction = 0; direction < LocomotionMath.DirectionCount; direction++)
            {
                Vector2 natural = LocomotionMath.Direction(direction) * Walk;
                float slow = LocomotionMath.ChildPosition(natural, rates[LocomotionMath.SlowWalkChild]).magnitude;
                float brisk = LocomotionMath.ChildPosition(natural, rates[LocomotionMath.BriskWalkChild]).magnitude;
                float run = LocomotionMath.ChildPosition(LocomotionMath.Direction(direction) * Run, rates[LocomotionMath.RunChild]).magnitude;
                Assert.That(slow, Is.LessThan(speed));
                Assert.That(brisk, Is.GreaterThanOrEqualTo(speed));
                Assert.That(run, Is.GreaterThan(brisk));
            }
        }

        [Test]
        public void RingsAreTheWalkAndRunChildren()
        {
            Assert.That(LocomotionMath.IsDirectionalRing(LocomotionMath.IdleChild), Is.False);
            Assert.That(LocomotionMath.IsDirectionalRing(LocomotionMath.SlowWalkChild), Is.True);
            Assert.That(LocomotionMath.IsDirectionalRing(LocomotionMath.WalkChild), Is.True);
            Assert.That(LocomotionMath.IsDirectionalRing(LocomotionMath.BriskWalkChild), Is.True);
            Assert.That(LocomotionMath.IsDirectionalRing(LocomotionMath.RunChild), Is.True);
            Assert.That(LocomotionMath.IsDirectionalRing(LocomotionMath.SprintChild), Is.False);
        }

        [Test]
        public void VelocityParameterHasADeadZoneAndKeepsDirectionWhenClamped()
        {
            Assert.That(LocomotionMath.VelocityParameter(new Vector2(0.02f, -0.03f)), Is.EqualTo(Vector2.zero));
            Assert.That(LocomotionMath.VelocityParameter(new Vector2(-1.6f, 0f)), Is.EqualTo(new Vector2(-1.6f, 0f)));
            Vector2 clamped = LocomotionMath.VelocityParameter(new Vector2(0f, -20f), 0.05f, 8f);
            Assert.That(clamped, Is.EqualTo(new Vector2(0f, -8f)));
            Assert.That(LocomotionMath.VelocityParameter(new Vector2(float.NaN, 1f)), Is.EqualTo(Vector2.zero));
        }

        // ------------------------------------------------------------------ incline

        [Test]
        public void InclineIsPositiveUphillAndNegativeDownhill()
        {
            // Ground rising towards +Z at 10°: its normal leans back towards -Z.
            Vector3 normal = SlopeNormal(10f);
            Assert.That(normal.z, Is.LessThan(0f));
            Assert.That(LocomotionMath.InclineFromNormal(normal, Vector3.forward), Is.EqualTo(10f).Within(0.01f));
            Assert.That(LocomotionMath.InclineFromNormal(normal, Vector3.back), Is.EqualTo(-10f).Within(0.01f));
            Assert.That(LocomotionMath.InclineFromNormal(normal, Vector3.right), Is.EqualTo(0f).Within(0.01f), "walking across the slope is level");
            Assert.That(LocomotionMath.InclineFromNormal(Vector3.up, Vector3.forward), Is.EqualTo(0f));
        }

        [Test]
        public void InclineFromVelocityHasTheSameSign()
        {
            Assert.That(LocomotionMath.InclineFromVelocity(new Vector3(0f, 0.5f, 1.5f)), Is.GreaterThan(0f));
            Assert.That(LocomotionMath.InclineFromVelocity(new Vector3(1.5f, -0.5f, 0f)), Is.LessThan(0f));
            Assert.That(LocomotionMath.InclineFromVelocity(new Vector3(0f, -2f, 0.05f)), Is.EqualTo(0f), "falling straight down is not a slope");
        }

        [Test]
        public void InclineClampsToTheSlopeClips()
        {
            Assert.That(LocomotionMath.ClampIncline(40f), Is.EqualTo(25f));
            Assert.That(LocomotionMath.ClampIncline(-40f), Is.EqualTo(-25f));
            Assert.That(LocomotionMath.ClampIncline(12f), Is.EqualTo(12f));
            Assert.That(LocomotionMath.ClampIncline(float.NaN), Is.EqualTo(0f));
            Vector3 steep = SlopeNormal(60f);
            Assert.That(LocomotionMath.ClampIncline(LocomotionMath.InclineFromNormal(steep, Vector3.forward)), Is.EqualTo(25f));
        }

        // ------------------------------------------------------------------ look

        [TestCase(190f, -170f)]
        [TestCase(-190f, 170f)]
        [TestCase(540f, 180f)]
        [TestCase(-180f, 180f)]
        [TestCase(359f, -1f)]
        [TestCase(45f, 45f)]
        public void WrapAngleMapsIntoMinus180To180(float input, float expected)
        {
            Assert.That(LocomotionMath.WrapAngle(input), Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void LookYawWrapsAcrossZero()
        {
            Assert.That(LocomotionMath.LookYaw(10f, 350f), Is.EqualTo(20f).Within(Tolerance));
            Assert.That(LocomotionMath.LookYaw(350f, 10f), Is.EqualTo(-20f).Within(Tolerance));
            Assert.That(LocomotionMath.LookYaw(725f, 0f), Is.EqualTo(5f).Within(Tolerance));
        }

        [Test]
        public void LookYawClampsAndGivesUpBehind()
        {
            Assert.That(LocomotionMath.LookYaw(100f, 0f, 70f, 135f), Is.EqualTo(70f));
            Assert.That(LocomotionMath.LookYaw(-100f, 0f, 70f, 135f), Is.EqualTo(-70f));
            // Around ±180° (camera looking at the face) the target is straight ahead from either side: no flip.
            Assert.That(LocomotionMath.LookYaw(179f, 0f, 70f, 135f), Is.EqualTo(0f));
            Assert.That(LocomotionMath.LookYaw(181f, 0f, 70f, 135f), Is.EqualTo(0f));
        }

        [Test]
        public void LookPitchIsPositiveUpAndClamped()
        {
            Assert.That(LocomotionMath.LookPitch(-20f), Is.EqualTo(20f), "camera pitched up (negative) → look up");
            Assert.That(LocomotionMath.LookPitch(8f, 8f), Is.EqualTo(0f), "resting third-person tilt reads as level");
            Assert.That(LocomotionMath.LookPitch(80f, 0f, 35f), Is.EqualTo(-35f));
            Assert.That(LocomotionMath.LookPitch(-80f, 0f, 35f), Is.EqualTo(35f));
        }

        [TestCase(70f, 0.35f)]
        [TestCase(-42f, 0.35f)]
        [TestCase(20f, 0f)]
        [TestCase(20f, 1f)]
        [TestCase(-33f, 1.7f)]
        public void BodyHeadSplitSumsToTheInput(float total, float share)
        {
            LocomotionMath.SplitLook(total, share, out float body, out float head);
            Assert.That(body + head, Is.EqualTo(total).Within(Tolerance));
            Assert.That(Mathf.Abs(body), Is.LessThanOrEqualTo(Mathf.Abs(total) + Tolerance));
            if (share > 0f && share < 1f) Assert.That(Mathf.Sign(body), Is.EqualTo(Mathf.Sign(head)));
        }

        [Test]
        public void BodyTakesItsShare()
        {
            LocomotionMath.SplitLook(70f, 0.35f, out float body, out float head);
            Assert.That(body, Is.EqualTo(24.5f).Within(Tolerance));
            Assert.That(head, Is.EqualTo(45.5f).Within(Tolerance));
        }

        // ------------------------------------------------------------------ lean, turning, air

        [Test]
        public void LeanFollowsTurnDirectionAndSaturates()
        {
            Assert.That(LocomotionMath.Lean(30f, 1.5f, 3f), Is.GreaterThan(0f).And.LessThan(0.5f), "gentle right curve at walking pace");
            Assert.That(LocomotionMath.Lean(-30f, 1.5f, 3f), Is.LessThan(0f));
            Assert.That(LocomotionMath.Lean(720f, 3.5f, 3f), Is.EqualTo(1f));
            Assert.That(LocomotionMath.Lean(-720f, 3.5f, 3f), Is.EqualTo(-1f));
            Assert.That(LocomotionMath.Lean(90f, 0f, 3f), Is.EqualTo(0f), "turning on the spot does not lean");
        }

        [Test]
        public void YawRateWrapsAndClamps()
        {
            Assert.That(LocomotionMath.YawRate(350f, 10f, 0.5f), Is.EqualTo(40f).Within(Tolerance));
            Assert.That(LocomotionMath.YawRate(10f, 350f, 0.5f), Is.EqualTo(-40f).Within(Tolerance));
            Assert.That(LocomotionMath.YawRate(0f, 90f, 0.01f, 720f), Is.EqualTo(720f), "a teleport's yaw snap is clamped");
            Assert.That(LocomotionMath.YawRate(0f, 90f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void TurnInPlaceOnlyWhenNearlyStill()
        {
            Assert.That(LocomotionMath.TurnInPlace(90f, 0f), Is.EqualTo(90f));
            Assert.That(LocomotionMath.TurnInPlace(-90f, 0.1f), Is.EqualTo(-90f));
            Assert.That(LocomotionMath.TurnInPlace(90f, 1.4f), Is.EqualTo(0f));
            Assert.That(LocomotionMath.TurnInPlace(4f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void AirTimeAccumulatesAndResets()
        {
            float t = 0f;
            for (int i = 0; i < 10; i++) t = LocomotionMath.AirTime(t, false, 0.05f);
            Assert.That(t, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(LocomotionMath.AirTime(t, true, 0.05f), Is.EqualTo(0f));
        }
    }
}
