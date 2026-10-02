using MedievalZen.Player;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class MotorMathTests
    {
        // The validated speeds: walk 1.6 m/s, sprint = walk × 2.2 (PlayerMotor defaults); third-person turn sharpness 10.
        private const float Walk = 1.6f;
        private const float Sprint = Walk * 2.2f;
        private const float Sharpness = 10f;
        private const float Dt = 1f / 60f;

        private static MotorTuning Tuning => MotorTuning.Default;

        /// <summary>A body facing +Z (yaw 0) already moving at <paramref name="speed"/> along its facing.</summary>
        private static MotorState Moving(float speed) => new MotorState { Yaw = 0f, Velocity = new Vector2(0f, speed) };

        // ------------------------------------------------------------------ face-movement (third person, auto-travel)

        [TestCase(Walk)]
        [TestCase(Sprint)]
        public void ReversalNeverSlidesBackwards(float speed)
        {
            MotorState state = Moving(speed);
            Vector2 request = new Vector2(0f, -speed);   // straight behind
            float minForward = float.MaxValue;
            for (int i = 0; i < 240; i++)
            {
                MotorMath.StepFaceMovement(ref state, request, Sharpness, Tuning, Dt);
                Vector2 local = MotorMath.LocalVelocity(state.Velocity, state.Yaw);
                minForward = Mathf.Min(minForward, local.y);
                Assert.That(Mathf.Abs(local.x), Is.LessThan(0.05f), $"frame {i}: sideways slide {local.x:0.000} m/s");
            }

            Assert.That(minForward, Is.GreaterThan(-0.01f), "the feet never travel against the facing");
            Assert.That(Mathf.Abs(LocomotionMath.WrapAngle(state.Yaw - 180f)), Is.LessThan(0.5f), "ends facing the new way");
            Assert.That(state.Velocity.y, Is.EqualTo(-speed).Within(1e-3f), "and walks away at full speed");
        }

        [Test]
        public void ReversalEasesOffPivotsAndWalksAway()
        {
            MotorState state = Moving(Walk);
            Vector2 request = new Vector2(0f, -Walk);
            float minSpeed = float.MaxValue;
            float previousYaw = state.Yaw;
            float maxYawRate = 0f;
            for (int i = 0; i < 120; i++)
            {
                MotorMath.StepFaceMovement(ref state, request, Sharpness, Tuning, Dt);
                minSpeed = Mathf.Min(minSpeed, state.Velocity.magnitude);
                maxYawRate = Mathf.Max(maxYawRate, Mathf.Abs(LocomotionMath.WrapAngle(state.Yaw - previousYaw)) / Dt);
                previousYaw = state.Yaw;
            }

            Assert.That(minSpeed, Is.LessThan(0.6f * Walk), "slows down to pivot");
            Assert.That(maxYawRate, Is.LessThanOrEqualTo(Tuning.MaxTurnRate + 0.01f), "the pivot is rate-limited (no snap)");
        }

        [TestCase(Walk)]
        [TestCase(Sprint)]
        public void NinetyDegreeTurnKeepsMostOfTheSpeed(float speed)
        {
            MotorState state = Moving(speed);
            Vector2 request = new Vector2(speed, 0f);   // hard right
            float minSpeed = float.MaxValue;
            for (int i = 0; i < 120; i++)
            {
                MotorMath.StepFaceMovement(ref state, request, Sharpness, Tuning, Dt);
                minSpeed = Mathf.Min(minSpeed, state.Velocity.magnitude);
                Assert.That(MotorMath.LocalVelocity(state.Velocity, state.Yaw).y, Is.GreaterThan(0f));
            }

            Assert.That(minSpeed, Is.GreaterThanOrEqualTo(0.7f * speed));
            Assert.That(state.Yaw, Is.EqualTo(90f).Within(0.5f));
            Assert.That(state.Velocity.magnitude, Is.EqualTo(speed).Within(1e-3f));
        }

        [Test]
        public void SmallCorrectionsAreAsResponsiveAsTheExponentialTurn()
        {
            // A 20° nudge: the yaw follows the exponential ease exactly (well under the rate cap) at full speed.
            MotorState state = Moving(Walk);
            Vector2 request = MotorMath.FromYaw(20f) * Walk;
            float reference = 0f;
            for (int i = 0; i < 15; i++)
            {
                MotorMath.StepFaceMovement(ref state, request, Sharpness, Tuning, Dt);
                reference = MotorMath.FollowYaw(reference, 20f, Sharpness, Dt);
                Assert.That(state.Yaw, Is.EqualTo(reference).Within(1e-3f));
                Assert.That(state.Velocity.magnitude, Is.EqualTo(Walk).Within(1e-4f), "a nudge never costs speed");
            }
        }

        [TestCase(Walk)]
        [TestCase(Sprint)]
        public void SteadyStateSpeedsAreExact(float speed)
        {
            var faced = new MotorState();
            var strafed = new MotorState();
            Vector2 request = MotorMath.FromYaw(0f) * speed;
            for (int i = 0; i < 300; i++)
            {
                MotorMath.StepFaceMovement(ref faced, request, Sharpness, Tuning, Dt);
                MotorMath.StepStrafe(ref strafed, request, Tuning, Dt);
            }

            Assert.That(faced.Velocity.magnitude, Is.EqualTo(speed).Within(1e-5f));
            Assert.That(strafed.Velocity.magnitude, Is.EqualTo(speed).Within(1e-5f));
            Assert.That(Walk, Is.EqualTo(1.6f));
            Assert.That(Sprint, Is.EqualTo(3.52f).Within(1e-5f));
        }

        [Test]
        public void ReleasingInputStopsAlongTheFacing()
        {
            MotorState state = Moving(Walk);
            for (int i = 0; i < 60; i++) MotorMath.StepFaceMovement(ref state, Vector2.zero, Sharpness, Tuning, Dt);
            Assert.That(state.Velocity, Is.EqualTo(Vector2.zero));
            Assert.That(state.Yaw, Is.EqualTo(0f), "no request, no turn");
        }

        // ------------------------------------------------------------------ strafe (first person)

        [Test]
        public void FirstPersonStrafeLeftIsNegativeLocalX()
        {
            var state = new MotorState { Yaw = 0f };
            Vector2 left = new Vector2(-Walk, 0f);   // camera and body face +Z
            for (int i = 0; i < 120; i++) MotorMath.StepStrafe(ref state, left, Tuning, Dt);
            Vector2 local = MotorMath.LocalVelocity(state.Velocity, state.Yaw);
            Assert.That(local.x, Is.EqualTo(-Walk).Within(1e-4f));
            Assert.That(local.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(state.Yaw, Is.EqualTo(0f), "strafing does not turn the body");
        }

        [Test]
        public void StrafeTurnRotatesInsteadOfBrakingThroughZero()
        {
            // Forward → sideways: the velocity swings round keeping its speed, much quicker than decelerating through zero.
            var state = new MotorState { Velocity = new Vector2(0f, Walk) };
            Vector2 right = new Vector2(Walk, 0f);
            float minSpeed = float.MaxValue;
            int frames = 0;
            while (Vector2.Angle(state.Velocity, right) > 1f && frames < 120)
            {
                MotorMath.StepStrafe(ref state, right, Tuning, Dt);
                minSpeed = Mathf.Min(minSpeed, state.Velocity.magnitude);
                frames++;
            }

            Assert.That(minSpeed, Is.EqualTo(Walk).Within(1e-3f));
            Assert.That(frames * Dt, Is.LessThan(0.25f), "old model: ~0.4 s through a 1.1 m/s dip");
        }

        [Test]
        public void StrafeReversalBrakesAlongItsLine()
        {
            var state = new MotorState { Velocity = new Vector2(0f, Walk) };
            Vector2 back = new Vector2(0f, -Walk);
            for (int i = 0; i < 120; i++)
            {
                MotorMath.StepStrafe(ref state, back, Tuning, Dt);
                Assert.That(Mathf.Abs(state.Velocity.x), Is.LessThan(1e-4f), "no sideways arc on a reversal");
            }

            Assert.That(state.Velocity.y, Is.EqualTo(-Walk).Within(1e-4f));
        }

        // ------------------------------------------------------------------ building blocks

        [Test]
        public void AlignmentIsFullAheadAndZeroBehind()
        {
            MotorTuning t = Tuning;
            Assert.That(MotorMath.Alignment(0f, t.FullSpeedAngle, t.ZeroSpeedAngle), Is.EqualTo(1f));
            Assert.That(MotorMath.Alignment(-t.FullSpeedAngle, t.FullSpeedAngle, t.ZeroSpeedAngle), Is.EqualTo(1f));
            Assert.That(MotorMath.Alignment(t.ZeroSpeedAngle, t.FullSpeedAngle, t.ZeroSpeedAngle), Is.EqualTo(0f));
            Assert.That(MotorMath.Alignment(180f, t.FullSpeedAngle, t.ZeroSpeedAngle), Is.EqualTo(0f));
            float previous = 1f;
            for (float angle = 0f; angle <= 180f; angle += 5f)
            {
                float a = MotorMath.Alignment(angle, t.FullSpeedAngle, t.ZeroSpeedAngle);
                Assert.That(a, Is.LessThanOrEqualTo(previous + 1e-6f), "falls monotonically as the request swings behind");
                previous = a;
            }
        }

        [Test]
        public void YawStepEasesSmallTurnsAndCapsLargeOnes()
        {
            float small = MotorMath.YawStep(10f, Sharpness, 300f, Dt);
            Assert.That(small, Is.EqualTo(10f * (1f - Mathf.Exp(-Sharpness * Dt))).Within(1e-5f));
            Assert.That(MotorMath.YawStep(170f, Sharpness, 300f, Dt), Is.EqualTo(300f * Dt).Within(1e-5f));
            Assert.That(MotorMath.YawStep(-170f, Sharpness, 300f, Dt), Is.EqualTo(-300f * Dt).Within(1e-5f));
            Assert.That(MotorMath.YawStep(90f, Sharpness, 300f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void TurnDeltaKeepsTurningTheSameWayForARequestBehind()
        {
            Assert.That(MotorMath.TurnDelta(0f, 170f, 0f), Is.EqualTo(170f).Within(1e-4f));
            // Mid-pivot to the left, the request drifts to just past straight behind: keep turning left, no flip.
            Assert.That(MotorMath.TurnDelta(0f, 170f, -1f), Is.EqualTo(-190f).Within(1e-4f));
            Assert.That(MotorMath.TurnDelta(0f, 90f, -1f), Is.EqualTo(90f).Within(1e-4f), "a clear request still takes the short way");
        }

        [Test]
        public void LocalVelocityIsRightAndForwardInBodySpace()
        {
            Assert.That(MotorMath.LocalVelocity(new Vector2(0f, 2f), 0f), Is.EqualTo(new Vector2(0f, 2f)));
            Assert.That(MotorMath.LocalVelocity(new Vector2(1f, 0f), 0f), Is.EqualTo(new Vector2(1f, 0f)));
            // Facing +X (yaw 90): world +X is forward, world +Z is to the left.
            Vector2 forward = MotorMath.LocalVelocity(new Vector2(1f, 0f), 90f);
            Vector2 left = MotorMath.LocalVelocity(new Vector2(0f, 1f), 90f);
            Assert.That(forward.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(forward.y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(left.x, Is.EqualTo(-1f).Within(1e-5f));
            Assert.That(left.y, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void YawAndDirectionRoundTrip()
        {
            foreach (float yaw in new[] { 0f, 45f, 90f, 135f, 180f, -45f, -90f, -135f })
            {
                Assert.That(Mathf.Abs(LocomotionMath.WrapAngle(MotorMath.YawOf(MotorMath.FromYaw(yaw)) - yaw)), Is.LessThan(1e-3f));
            }

            Assert.That(MotorMath.FromYaw(90f).x, Is.EqualTo(1f).Within(1e-5f), "yaw 90 faces +X, as Unity's Euler yaw");
        }
    }
}
