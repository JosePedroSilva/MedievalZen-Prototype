using MedievalZen.Player;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>Pure maths of sitting down (<see cref="SeatMath"/>, <see cref="SeatCatalog"/>): limb solve, blend, alignment, view choice, time flow.</summary>
    public sealed class SittingTests
    {
        private const float Thigh = 0.43f;
        private const float Shin = 0.42f;

        // ------------------------------------------------------------------ two-bone solve

        [Test]
        public void TwoBone_ReachableTarget_EndsOnTargetWithBoneLengthsKept()
        {
            var hip = new Vector3(0f, 0.55f, 0f);
            var ankle = new Vector3(0f, 0.09f, 0.42f);
            bool reached = SeatMath.SolveTwoBone(hip, ankle, Thigh, Shin, Vector3.forward + Vector3.up * 0.25f, out Vector3 knee, out Vector3 end);
            Assert.That(reached, Is.True);
            Assert.That(Vector3.Distance(end, ankle), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(hip, knee), Is.EqualTo(Thigh).Within(1e-4f));
            Assert.That(Vector3.Distance(knee, end), Is.EqualTo(Shin).Within(1e-4f));
        }

        [Test]
        public void TwoBone_SeatedLeg_ThighRoughlyLevelAndShinRoughlyUpright()
        {
            // A 0.45 m seat (hip joint 0.1 above it), the ankle 9 cm above the ground, the foot ahead of the hip by about a thigh.
            var hip = new Vector3(0f, 0.55f, 0f);
            var ankle = new Vector3(0f, 0.09f, 0.42f);
            SeatMath.SolveTwoBone(hip, ankle, Thigh, Shin, Vector3.forward + Vector3.up * 0.25f, out Vector3 knee, out Vector3 end);
            Vector3 thigh = knee - hip;
            Vector3 shin = end - knee;
            float thighTilt = Mathf.Asin(Mathf.Abs(thigh.y) / thigh.magnitude) * Mathf.Rad2Deg;
            float shinFromVertical = Vector3.Angle(shin, Vector3.down);
            Assert.That(knee.z, Is.GreaterThan(0.3f), "knee forward of the hip");
            Assert.That(thighTilt, Is.LessThan(30f), "thigh close to level");
            Assert.That(shinFromVertical, Is.LessThan(30f), "shin close to upright");
        }

        [Test]
        public void TwoBone_BendsTowardsTheHint()
        {
            var root = Vector3.zero;
            var target = new Vector3(0f, -0.6f, 0f);
            SeatMath.SolveTwoBone(root, target, 0.4f, 0.4f, Vector3.forward, out Vector3 forwardKnee, out _);
            SeatMath.SolveTwoBone(root, target, 0.4f, 0.4f, Vector3.back, out Vector3 backKnee, out _);
            Assert.That(forwardKnee.z, Is.GreaterThan(0.1f));
            Assert.That(backKnee.z, Is.LessThan(-0.1f));
        }

        [Test]
        public void TwoBone_OutOfReach_StretchesAlongTheLine()
        {
            var root = Vector3.zero;
            var target = new Vector3(0f, -2f, 0f);
            bool reached = SeatMath.SolveTwoBone(root, target, 0.4f, 0.4f, Vector3.forward, out Vector3 joint, out Vector3 end);
            Assert.That(reached, Is.False);
            Assert.That(end.y, Is.EqualTo(-0.8f).Within(1e-3f));
            Assert.That(Mathf.Abs(end.x) + Mathf.Abs(end.z), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(root, joint), Is.EqualTo(0.4f).Within(1e-3f));
        }

        [Test]
        public void TwoBone_TooClose_FoldsAsFarAsTheBonesAllow()
        {
            var root = Vector3.zero;
            bool reached = SeatMath.SolveTwoBone(root, new Vector3(0f, -0.01f, 0f), 0.5f, 0.3f, Vector3.forward, out Vector3 joint, out Vector3 end);
            Assert.That(reached, Is.False);
            Assert.That(Vector3.Distance(root, end), Is.EqualTo(0.2f).Within(1e-3f));
            Assert.That(Vector3.Distance(root, joint), Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(Vector3.Distance(joint, end), Is.EqualTo(0.3f).Within(1e-3f));
        }

        [Test]
        public void TwoBone_DegenerateHint_StillGivesAValidJoint()
        {
            var root = Vector3.zero;
            var target = new Vector3(0f, -0.5f, 0f);
            SeatMath.SolveTwoBone(root, target, 0.4f, 0.4f, Vector3.down, out Vector3 joint, out Vector3 end);
            Assert.That(float.IsNaN(joint.x) || float.IsNaN(joint.y) || float.IsNaN(joint.z), Is.False);
            Assert.That(Vector3.Distance(root, joint), Is.EqualTo(0.4f).Within(1e-3f));
            Assert.That(Vector3.Distance(joint, end), Is.EqualTo(0.4f).Within(1e-3f));
        }

        // ------------------------------------------------------------------ blend weight

        [Test]
        public void Ease_IsZeroAtZeroOneAtOneAndMonotonic()
        {
            Assert.That(SeatMath.Ease(0f), Is.EqualTo(0f));
            Assert.That(SeatMath.Ease(1f), Is.EqualTo(1f));
            Assert.That(SeatMath.Ease(0.5f), Is.EqualTo(0.5f).Within(1e-5f));
            float previous = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float e = SeatMath.Ease(i / 100f);
                Assert.That(e, Is.GreaterThanOrEqualTo(previous));
                previous = e;
            }

            // Gentle at both ends: the first and last 10 % of the time move less than 3 %.
            Assert.That(SeatMath.Ease(0.1f), Is.LessThan(0.03f));
            Assert.That(1f - SeatMath.Ease(0.9f), Is.LessThan(0.03f));
        }

        [Test]
        public void StepProgress_SitsInTheGivenTimeAndStandsBack()
        {
            float p = 0f;
            int frames = 0;
            while (p < 1f && frames < 1000)
            {
                p = SeatMath.StepProgress(p, true, 0.8f, 1f / 60f);
                frames++;
            }

            Assert.That(frames, Is.InRange(47, 49), "about 0.8 s at 60 fps");
            p = SeatMath.StepProgress(p, false, 0.8f, 0.4f);
            Assert.That(p, Is.EqualTo(0.5f).Within(1e-4f));
            p = SeatMath.StepProgress(p, false, 0.8f, 10f);
            Assert.That(p, Is.EqualTo(0f));
        }

        // ------------------------------------------------------------------ seat alignment

        [Test]
        public void NearestOnAxis_FollowsThePlayerButKeepsOffTheEnds()
        {
            var a = new Vector3(-1f, 0f, 0f);
            var b = new Vector3(1f, 0f, 0f);
            Assert.That(SeatMath.NearestOnAxis(new Vector3(0.3f, 0f, 1f), a, b, 0.35f).x, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(SeatMath.NearestOnAxis(new Vector3(5f, 0f, 1f), a, b, 0.35f).x, Is.EqualTo(0.65f).Within(1e-4f));
            Assert.That(SeatMath.NearestOnAxis(new Vector3(-5f, 0f, 1f), a, b, 0.35f).x, Is.EqualTo(-0.65f).Within(1e-4f));
            // A short log: the middle.
            Assert.That(SeatMath.NearestOnAxis(new Vector3(5f, 0f, 1f), new Vector3(-0.2f, 0f, 0f), new Vector3(0.2f, 0f, 0f), 0.35f).x, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void FireSeat_IsOnTheRingOnThePlayersSideAndFacesTheFire()
        {
            var fire = new Vector3(10f, 2f, 10f);
            Vector3 spot = SeatMath.FireSeatPoint(fire, new Vector3(10f, 2f, 0f), 1.35f);
            Assert.That(Vector3.Distance(spot, fire), Is.EqualTo(1.35f).Within(1e-4f));
            Assert.That(spot.z, Is.LessThan(fire.z));
            Vector3 facing = SeatMath.Planar(fire - spot, Vector3.zero);
            Assert.That(Vector3.Dot(facing, Vector3.forward), Is.EqualTo(1f).Within(1e-4f));
        }

        [TestCase(0.1f, false)]
        [TestCase(0.25f, true)]
        [TestCase(0.45f, true)]
        [TestCase(0.75f, true)]
        [TestCase(0.95f, false)]
        public void SittingHeight_AcceptsLogsToCratesOnly(float height, bool expected)
        {
            Assert.That(SeatMath.IsSittingHeight(height), Is.EqualTo(expected));
        }

        [Test]
        public void HipAndBody_SitOnTheSeatWithTheBodyInFront()
        {
            var top = new Vector3(0f, 0.45f, 0f);
            Vector3 hip = SeatMath.HipOnSeat(top, Vector3.right, 0.1f, 0.04f);
            Assert.That(hip.y, Is.EqualTo(0.55f).Within(1e-5f));
            Assert.That(hip.x, Is.EqualTo(0.04f).Within(1e-5f));
            Vector3 body = SeatMath.ApproachPoint(hip, Vector3.right, 0.5f);
            Assert.That(body.x, Is.EqualTo(0.54f).Within(1e-5f));
        }

        [Test]
        public void YawHelpers_RoundTrip()
        {
            for (float yaw = -170f; yaw < 180f; yaw += 37f)
            {
                Assert.That(Mathf.DeltaAngle(SeatMath.YawOf(SeatMath.DirectionFromYaw(yaw)), yaw), Is.EqualTo(0f).Within(1e-3f));
            }

            Assert.That(SeatMath.DirectionFromYaw(90f).x, Is.EqualTo(1f).Within(1e-5f));
        }

        // ------------------------------------------------------------------ facing the view

        [Test]
        public void ViewScore_PrefersDownhillAndWaterAndPenalisesWalls()
        {
            float flat = SeatMath.ViewScore(0f, 0f, 0f, false, false, float.PositiveInfinity);
            float downhill = SeatMath.ViewScore(1f, 6f, 20f, false, false, float.PositiveInfinity);
            float uphill = SeatMath.ViewScore(-1f, -6f, -20f, false, false, float.PositiveInfinity);
            float lake = SeatMath.ViewScore(0f, 0f, 0f, true, false, float.PositiveInfinity);
            float wall = SeatMath.ViewScore(1f, 6f, 20f, false, false, 0.5f);
            Assert.That(downhill, Is.GreaterThan(flat));
            Assert.That(uphill, Is.LessThan(flat));
            Assert.That(lake, Is.GreaterThan(flat));
            Assert.That(wall, Is.LessThan(flat), "a chair back or a cabin wall in the face beats any view");
        }

        [Test]
        public void ChooseFacing_TakesTheBestViewAndUsesTheHintOnTies()
        {
            var dirs = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            var scores = new[] { 0f, 1.2f, 0f, 0f };
            Assert.That(SeatMath.ChooseFacing(scores, dirs, 4, Vector3.forward, 0.35f), Is.EqualTo(1));
            var even = new[] { 0f, 0f, 0f, 0f };
            Assert.That(SeatMath.ChooseFacing(even, dirs, 4, Vector3.left, 0.35f), Is.EqualTo(3));
            // A strong hint (a fire) wins over a modest view.
            Assert.That(SeatMath.ChooseFacing(scores, dirs, 4, Vector3.back, 3f), Is.EqualTo(2));
            // Only the first count entries are considered (a log: two directions).
            Assert.That(SeatMath.ChooseFacing(scores, dirs, 1, Vector3.right, 0.35f), Is.EqualTo(0));
        }

        [Test]
        public void Slope_FromFourHeights()
        {
            Assert.That(SeatMath.SlopeDegrees(0f, 0f, 0f, 0f, 0.6f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(SeatMath.SlopeDegrees(-0.6f, 0.6f, 0f, 0f, 0.6f), Is.EqualTo(45f).Within(1e-3f));
            Assert.That(SeatMath.SlopeDegrees(0f, 0f, -0.3f, 0.3f, 0.6f), Is.EqualTo(Mathf.Atan(0.5f) * Mathf.Rad2Deg).Within(1e-3f));
        }

        // ------------------------------------------------------------------ time while seated

        [Test]
        public void TimeFlow_RampsSmoothlyToTwentyAndBack()
        {
            float flow = 1f;
            float previous = flow;
            float t = 0f;
            const float dt = 1f / 60f;
            while (flow < 19.9f && t < 20f)
            {
                flow = SeatMath.StepTimeFlow(flow, 20f, 1.1f, dt);
                Assert.That(flow, Is.GreaterThanOrEqualTo(previous));
                Assert.That(flow, Is.LessThanOrEqualTo(20f));
                Assert.That(flow / previous, Is.LessThan(1.06f), "no jumps");
                previous = flow;
                t += dt;
            }

            Assert.That(t, Is.InRange(2f, 6f), "a few seconds to reach full speed");
            t = 0f;
            while (flow > 1f && t < 20f)
            {
                flow = SeatMath.StepTimeFlow(flow, 1f, 1.1f, dt);
                t += dt;
            }

            Assert.That(flow, Is.EqualTo(1f), "snaps exactly back to the chosen flow");
            Assert.That(t, Is.LessThan(8f));
        }

        // ------------------------------------------------------------------ recognising seats

        [TestCase("SM_Gen_Env_Log_01", SeatKind.Log)]
        [TestCase("SM_Gen_Env_Log_02(Clone)", SeatKind.Log)]
        [TestCase("SM_Gen_Env_Stump_03", SeatKind.Stump)]
        [TestCase("SM_Prop_Camp_Crate_01", SeatKind.Crate)]
        [TestCase("SM_Gen_Prop_Crate_02", SeatKind.Crate)]
        [TestCase("SM_Gen_Prop_Chair_01", SeatKind.Chair)]
        [TestCase("SM_Env_Rock_03", SeatKind.Rock)]
        [TestCase("SM_Env_Rock_Ground_02", SeatKind.Rock)]
        [TestCase("SM_Gen_Env_Rock_07", SeatKind.Rock)]
        [TestCase("SM_Prop_Camp_Fireplace_01", SeatKind.Fire)]
        [TestCase("SM_Prop_Camp_Fireplace_Stones_01", SeatKind.Fire)]
        [TestCase("SM_Env_Rock_Pile_04", SeatKind.None)]
        [TestCase("SM_Env_Rock_Small_Pile_01", SeatKind.None)]
        [TestCase("SM_Env_Rock_Cliff_02", SeatKind.None)]
        [TestCase("SM_Prop_SciFiCrate_01", SeatKind.None)]
        [TestCase("SM_Gen_Env_Root_01", SeatKind.None)]
        [TestCase("SM_Bld_Stone_Cabin_01", SeatKind.None)]
        [TestCase("SM_Env_Tree_Birch_01", SeatKind.None)]
        [TestCase("SM_Prop_Bench_Seat_01", SeatKind.Log)]
        [TestCase("SM_Prop_Bench_Seat_03(Clone)", SeatKind.Log)]
        [TestCase("SM_Prop_Crate_Stack_03", SeatKind.None)]
        [TestCase("SM_Prop_Barrel_Stack_02", SeatKind.None)]
        [TestCase("SM_Prop_Workbench_Forge_01", SeatKind.None)]
        [TestCase("SM_Bld_Preset_Tavern_01_Optimized", SeatKind.None)]
        [TestCase("SM_Bld_Preset_House_Archway_02_Optimized", SeatKind.None)]
        public void Catalog_RecognisesSeatProps(string name, SeatKind expected)
        {
            Assert.That(SeatCatalog.Classify(name), Is.EqualTo(expected));
        }

        [Test]
        public void Catalog_OnlyPropKindsCarryTheHips()
        {
            Assert.That(SeatCatalog.SitsOnProp(SeatKind.Log), Is.True);
            Assert.That(SeatCatalog.SitsOnProp(SeatKind.Rock), Is.True);
            Assert.That(SeatCatalog.SitsOnProp(SeatKind.Fire), Is.False);
            Assert.That(SeatCatalog.SitsOnProp(SeatKind.Doorstep), Is.False);
            Assert.That(SeatCatalog.SitsOnProp(SeatKind.Ground), Is.False);
            Assert.That(SeatCatalog.Verb(SeatKind.Fire), Is.EqualTo("Sit by the fire"));
        }
    }
}
