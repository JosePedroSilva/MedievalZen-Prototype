using System;
using MedievalZen.Endless;
using MedievalZen.Player;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class HorseRidingTests
    {
        // The validated on-foot numbers (PlayerMotor / AutoTraveller inspector defaults).
        private const float TravelSpeed = 1.4f;

        [Test]
        public void GaitSpeedsNamesAndLabels()
        {
            Assert.That(HorseGaits.Speed(HorseGait.Walk), Is.EqualTo(1.9f));
            Assert.That(HorseGaits.Speed(HorseGait.Trot), Is.EqualTo(3.6f));
            Assert.That(HorseGaits.Speed(HorseGait.Canter), Is.EqualTo(6.0f));
            Assert.That(HorseGaits.Label(HorseGait.Trot, HorseGaits.Speed(HorseGait.Trot)), Is.EqualTo("Trot · 3.6 m/s"));
            Assert.That(HorseGaits.Label(HorseGait.Canter, float.NaN), Is.EqualTo("Canter · 0.0 m/s"));
            Assert.That(HorseGaits.TravelDefault, Is.EqualTo(HorseGait.Trot));
        }

        [Test]
        public void WheelStepsOneGaitPerNotchAndClampsAtBothEnds()
        {
            Assert.That(HorseGaits.Stepped(HorseGait.Walk, 1), Is.EqualTo(HorseGait.Trot));
            Assert.That(HorseGaits.Stepped(HorseGait.Trot, 1), Is.EqualTo(HorseGait.Canter));
            Assert.That(HorseGaits.Stepped(HorseGait.Canter, 1), Is.EqualTo(HorseGait.Canter));
            Assert.That(HorseGaits.Stepped(HorseGait.Walk, -1), Is.EqualTo(HorseGait.Walk));
            Assert.That(HorseGaits.Stepped(HorseGait.Canter, -1), Is.EqualTo(HorseGait.Trot));
            Assert.That(HorseGaits.Clamp(99), Is.EqualTo(HorseGait.Canter));
            Assert.That(HorseGaits.Clamp(-3), Is.EqualTo(HorseGait.Walk));

            // The same wheel logic as the pace: a fast spin steps once per repeat gap.
            var wheel = new WalkPaceWheel();
            Assert.That(wheel.Update(120f, 0.016f), Is.EqualTo(1));
            Assert.That(wheel.Update(120f, 0.016f), Is.EqualTo(0));
            Assert.That(wheel.Update(-1f, 0.06f), Is.EqualTo(-1));
        }

        [Test]
        public void SprintIsACanterBurst()
        {
            Assert.That(HorseGaits.ManualSpeed(HorseGait.Walk, true), Is.EqualTo(HorseGaits.CanterSpeed));
            Assert.That(HorseGaits.ManualSpeed(HorseGait.Trot, false), Is.EqualTo(HorseGaits.TrotSpeed));
        }

        [Test]
        public void TurnRadiusGrowsWithTheGaitAndTheRateFalls()
        {
            RideTurn t = RideTurn.Default;
            float rw = HorseGaits.TurnRadius(HorseGaits.WalkSpeed, t), rt = HorseGaits.TurnRadius(HorseGaits.TrotSpeed, t), rc = HorseGaits.TurnRadius(HorseGaits.CanterSpeed, t);
            Assert.That(rw, Is.LessThan(rt));
            Assert.That(rt, Is.LessThan(rc));
            Assert.That(HorseGaits.TurnRate(HorseGaits.TrotSpeed, t), Is.LessThan(HorseGaits.TurnRate(HorseGaits.WalkSpeed, t)));
            Assert.That(HorseGaits.TurnRate(HorseGaits.CanterSpeed, t), Is.LessThan(HorseGaits.TurnRate(HorseGaits.TrotSpeed, t)));
            Assert.That(HorseGaits.TurnRate(0f, t), Is.EqualTo(t.MinRate), "a standing horse turns slowly, it never spins");
            for (float v = 0f; v < 12f; v += 0.25f) Assert.That(HorseGaits.TurnRate(v, t), Is.InRange(t.MinRate, t.MaxRate));
        }

        [Test]
        public void SimulatedUTurnIsAnArcWhoseWidthGrowsWithTheGait()
        {
            float walk = UTurnWidth(HorseGaits.WalkSpeed), canter = UTurnWidth(HorseGaits.CanterSpeed);
            Assert.That(walk, Is.GreaterThan(1f), "reversing is an arc, not a spin on the spot");
            float trot = UTurnWidth(HorseGaits.TrotSpeed);
            Assert.That(trot, Is.GreaterThan(walk * 1.2f), $"walk {walk:0.00} m, trot {trot:0.00} m");
            Assert.That(canter, Is.GreaterThan(trot * 1.2f), $"trot {trot:0.00} m, canter {canter:0.00} m");
        }

        // Rides straight along +Z at speed, then asks for -Z; returns how far sideways the horse swung (the arc's width).
        private static float UTurnWidth(float speed)
        {
            MotorTuning tuning = MotorTuning.Default;
            RideTurn turn = RideTurn.Default;
            var s = new MotorState { Yaw = 0f };
            const float dt = 1f / 60f;
            for (int i = 0; i < 600; i++) RidingMath.Step(ref s, new Vector2(0f, speed), 10f, turn, tuning, dt);
            Assert.That(s.Velocity.magnitude, Is.EqualTo(speed).Within(0.01f));
            float x = 0f, maxX = 0f, minSpeed = float.MaxValue;
            for (int i = 0; i < 1200; i++)
            {
                RidingMath.Step(ref s, new Vector2(0.001f, -speed), 10f, turn, tuning, dt);
                x += s.Velocity.x * dt;
                maxX = Math.Max(maxX, Math.Abs(x));
                minSpeed = Math.Min(minSpeed, s.Velocity.magnitude);
            }

            Assert.That(minSpeed, Is.GreaterThan(0.5f), "it never stops to pivot");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(s.Yaw, 180f)), Is.LessThan(3f), "it came round");
            return maxX;
        }

        [Test]
        public void OnFootTravelSpeedIsUnchangedToTheBit()
        {
            for (int tenths = WalkPace.MinTenths; tenths <= WalkPace.MaxTenths; tenths++)
            {
                float pace = WalkPace.FromTenths(tenths);
                foreach (float scale in new[] { 1f, 6f, 12f })
                {
                    float before = TravelSpeed * WalkPace.TravelScale(pace, scale);
                    float now = HorseGaits.TravelSpeed(false, HorseGait.Canter, TravelSpeed, pace, scale);
                    Assert.That(BitConverter.SingleToInt32Bits(now), Is.EqualTo(BitConverter.SingleToInt32Bits(before)));
                }
            }

            Assert.That(HorseGaits.TravelSpeed(false, HorseGait.Trot, TravelSpeed, 1f, 1f), Is.EqualTo(1.4f));
        }

        [Test]
        public void BenchmarkScaleOverridesTheGait()
        {
            Assert.That(HorseGaits.TravelSpeed(true, HorseGait.Trot, TravelSpeed, 1f, 1f), Is.EqualTo(3.6f));
            Assert.That(HorseGaits.TravelSpeed(true, HorseGait.Canter, TravelSpeed, 1.5f, 12f), Is.EqualTo(TravelSpeed * 12f));
            Assert.That(HorseGaits.TravelSpeed(true, HorseGait.Walk, TravelSpeed, 0.5f, 6f), Is.EqualTo(TravelSpeed * 6f));
            Assert.That(HorseGaits.TravelRides(true, 1f), Is.True);
            Assert.That(HorseGaits.TravelRides(true, 12f), Is.False, "a benchmark keeps the walker's steering");
            Assert.That(HorseGaits.TravelRides(false, 1f), Is.False);
        }

        [Test]
        public void OnFootCameraAnchorIsExactlyZero()
        {
            Assert.That(RideCamera.Offset(0f, 0.4f, 0.03f), Is.EqualTo(Vector3.zero));
            Assert.That(RideCamera.Offset(float.NaN, 0.4f, 0.03f), Is.EqualTo(Vector3.zero));
            Assert.That(RideCamera.Offset(1f, 0.4f, 0f).y, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(RideCamera.Offset(0.5f, 0.4f, 0f).y, Is.EqualTo(0.2f).Within(1e-6f));
            // The validated motor numbers stay as they were.
            MotorTuning d = MotorTuning.Default;
            Assert.That(d.MaxTurnRate, Is.EqualTo(300f));
            Assert.That(d.Acceleration, Is.EqualTo(5f));
            Assert.That(WalkPace.ManualScale(1f, false, 2.2f), Is.EqualTo(1f));
        }

        [Test]
        public void MountAndDismountRules()
        {
            Assert.That(MountStateMachine.CanMount(MountPhase.OnFoot, 2f, false, false), Is.True);
            Assert.That(MountStateMachine.CanMount(MountPhase.OnFoot, 3.5f, false, false), Is.False, "out of reach");
            Assert.That(MountStateMachine.CanMount(MountPhase.OnFoot, 1f, true, false), Is.False, "seated");
            Assert.That(MountStateMachine.CanMount(MountPhase.Riding, 1f, false, false), Is.False);
            Assert.That(MountStateMachine.CanDismount(MountPhase.Riding, 0f, false), Is.True);
            Assert.That(MountStateMachine.CanDismount(MountPhase.Riding, HorseGaits.WalkSpeed, false), Is.True, "at a walk");
            Assert.That(MountStateMachine.CanDismount(MountPhase.Riding, HorseGaits.TrotSpeed, false), Is.False, "not at a trot");
            Assert.That(MountStateMachine.CanDismount(MountPhase.Mounting, 0f, false), Is.False);
            Assert.That(MountStateMachine.LeftBehind(450f, false, false), Is.True);
            Assert.That(MountStateMachine.LeftBehind(450f, true, false), Is.False, "still in sight");
            Assert.That(MountStateMachine.LeftBehind(350f, false, false), Is.False);
        }

        [Test]
        public void MountAndDismountAreShortBlends()
        {
            var m = new MountStateMachine();
            Assert.That(m.Weight, Is.EqualTo(0f));
            Assert.That(m.BeginDismount(), Is.False);
            Assert.That(m.BeginMount(), Is.True);
            Assert.That(m.HoldsBody, Is.True);
            float last = 0f;
            int frames = 0;
            while (m.Phase == MountPhase.Mounting && frames++ < 1000)
            {
                m.Update(1f / 60f);
                Assert.That(m.Weight, Is.GreaterThanOrEqualTo(last));
                Assert.That(m.Weight - last, Is.LessThan(0.08f), "no pop");
                last = m.Weight;
            }

            Assert.That(m.Phase, Is.EqualTo(MountPhase.Riding));
            Assert.That(m.Weight, Is.EqualTo(1f));
            Assert.That(frames / 60f, Is.InRange(0.5f, 1.5f));
            Assert.That(m.BeginDismount(), Is.True);
            for (int i = 0; i < 120; i++) m.Update(1f / 60f);
            Assert.That(m.Phase, Is.EqualTo(MountPhase.OnFoot));
            Assert.That(m.Weight, Is.EqualTo(0f));
        }

        [Test]
        public void HoofRhythmHasFourTwoAndThreeBeatsPerCycle()
        {
            Assert.That(BeatsPerCycle(HorseGaits.WalkSpeed), Is.EqualTo(4));
            Assert.That(BeatsPerCycle(HorseGaits.TrotSpeed), Is.EqualTo(2));
            Assert.That(BeatsPerCycle(HorseGaits.CanterSpeed), Is.EqualTo(3));
            var r = new HoofRhythm();
            Assert.That(r.Update(0.1f, 0f), Is.EqualTo(0), "standing: silent");
            Assert.That(HoofRhythm.Beats(HorseGait.Canter)[2], Is.LessThan(0.6f), "the canter's suspension after its three beats");
        }

        private static int BeatsPerCycle(float speed)
        {
            var r = new HoofRhythm();
            const float dt = 1f / 120f;
            float seconds = 10f / HorseGaits.CycleRate(speed);   // ten cycles
            int beats = 0;
            for (float t = 0f; t < seconds - dt * 0.5f; t += dt) beats += r.Update(dt, speed);
            return (int)Math.Round(beats / 10.0);
        }

        [Test]
        public void ClipsShowTheGait()
        {
            Assert.That(HorseGaits.Clip(0f, out _), Is.EqualTo(HorseClip.Idle));
            Assert.That(HorseGaits.Clip(HorseGaits.WalkSpeed, out float pw), Is.EqualTo(HorseClip.Walk));
            Assert.That(pw, Is.EqualTo(HorseGaits.WalkSpeed / HorseGaits.WalkClipSpeed).Within(1e-5f));
            Assert.That(HorseGaits.Clip(HorseGaits.TrotSpeed, out float pt), Is.EqualTo(HorseClip.Walk), "no trot clip: the walk played fast");
            Assert.That(pt, Is.GreaterThan(pw));
            Assert.That(HorseGaits.Clip(HorseGaits.CanterSpeed, out float pc), Is.EqualTo(HorseClip.Run));
            Assert.That(pc, Is.InRange(0.75f, 1f));
            for (float p = 0f; p < 1f; p += 0.05f)
            {
                Assert.That(Mathf.Abs(HorseGaits.Bob(HorseGait.Trot, p)), Is.LessThanOrEqualTo(0.045f));
                Assert.That(Mathf.Abs(HorseGaits.Bob(HorseGait.Canter, p)), Is.LessThanOrEqualTo(0.045f));
            }
        }

        [Test]
        public void JourneyMountedRoundTripAndMigration()
        {
            var j = new EndlessJourney { Seed = 7, S = 1234.5, Day = 2, Hour = 9f, Mounted = true, Gait = 2 };
            string json = j.ToJson();
            StringAssert.StartsWith("{\"version\":2,", json);
            Assert.That(EndlessJourney.TryFromJson(json, out EndlessJourney back), Is.True);
            Assert.That(back.Mounted, Is.True);
            Assert.That(back.Gait, Is.EqualTo(2));

            var foot = new EndlessJourney { Seed = 7, S = 10.0 };
            Assert.That(foot.ToJson(), Does.Not.Contain("mounted"));
            Assert.That(EndlessJourney.TryFromJson(foot.ToJson(), out EndlessJourney footBack), Is.True);
            Assert.That(footBack.Mounted, Is.False);

            // A version-1 blob (before riding) resumes on foot at the walk.
            Assert.That(EndlessJourney.TryFromJson("{\"version\":1,\"seed\":5,\"s\":12.5,\"day\":3,\"hour\":14.5}", out EndlessJourney old), Is.True);
            Assert.That(old.Mounted, Is.False);
            Assert.That(old.Gait, Is.EqualTo(0));
            Assert.That(old.S, Is.EqualTo(12.5));
            Assert.That(EndlessJourney.TryFromJson("{\"version\":2,\"seed\":5,\"mounted\":1,\"gait\":9}", out EndlessJourney odd), Is.True);
            Assert.That(odd.Gait, Is.EqualTo(2), "clamped");
            Assert.That(EndlessJourney.TryFromJson("{\"version\":3,\"seed\":5}", out _), Is.False, "newer is not half-read");
        }
    }
}
