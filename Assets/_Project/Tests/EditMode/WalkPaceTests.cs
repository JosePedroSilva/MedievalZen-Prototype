using System;
using MedievalZen.Player;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    public sealed class WalkPaceTests
    {
        // The validated numbers (PlayerMotor / AutoTraveller inspector defaults): they must not move at pace 1.
        private const float WalkSpeed = 1.6f;
        private const float SprintScale = 2.2f;
        private const float TravelSpeed = 1.4f;
        private const float SpeedVariation = 0.08f;

        [Test]
        public void ElevenStepsOfOneTenthFromHalfToOneAndAHalf()
        {
            Assert.That(WalkPace.Count, Is.EqualTo(11));
            Assert.That(WalkPace.Default, Is.EqualTo(1f));
            for (int i = 0; i < WalkPace.Count; i++)
            {
                float pace = WalkPace.FromIndex(i);
                Assert.That(pace, Is.EqualTo(0.5f + 0.1f * i).Within(1e-6f));
                Assert.That(WalkPace.Index(pace), Is.EqualTo(i));
                Assert.That(WalkPace.Clamp(pace), Is.EqualTo(pace), "every step is a fixed point of Clamp");
            }

            Assert.That(WalkPace.FromIndex(5), Is.EqualTo(1f), "the middle step is exactly 1");
            Assert.That(WalkPace.Clamp(1f), Is.EqualTo(1f));
        }

        [Test]
        public void ClampSnapsToTheNearestStepAndKeepsTheRange()
        {
            Assert.That(WalkPace.Clamp(0.1f), Is.EqualTo(0.5f));
            Assert.That(WalkPace.Clamp(-3f), Is.EqualTo(0.5f));
            Assert.That(WalkPace.Clamp(9f), Is.EqualTo(1.5f));
            Assert.That(WalkPace.Clamp(float.PositiveInfinity), Is.EqualTo(1.5f));
            Assert.That(WalkPace.Clamp(float.NegativeInfinity), Is.EqualTo(0.5f));
            Assert.That(WalkPace.Clamp(float.NaN), Is.EqualTo(1f), "NaN falls back to the default");
            Assert.That(WalkPace.Clamp(1.04f), Is.EqualTo(1f));
            Assert.That(WalkPace.Clamp(1.06f), Is.EqualTo(1.1f).Within(1e-6f));
            Assert.That(WalkPace.Clamp(0.7f * 1f), Is.EqualTo(0.7f).Within(1e-6f), "0.7f is 6.9999… tenths: still step 7");
        }

        [Test]
        public void SteppingMovesOneTenthAndStopsAtTheEnds()
        {
            Assert.That(WalkPace.Stepped(1f, 1), Is.EqualTo(1.1f).Within(1e-6f));
            Assert.That(WalkPace.Stepped(1f, -1), Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(WalkPace.Stepped(1.5f, 1), Is.EqualTo(1.5f));
            Assert.That(WalkPace.Stepped(0.5f, -1), Is.EqualTo(0.5f));
            float pace = 1f;
            for (int i = 0; i < 5; i++) pace = WalkPace.Stepped(pace, 1);
            Assert.That(pace, Is.EqualTo(1.5f), "five steps up from 1 reach the top without drift");
            for (int i = 0; i < 5; i++) pace = WalkPace.Stepped(pace, -1);
            Assert.That(pace, Is.EqualTo(1f), "and five back land exactly on 1");
        }

        [Test]
        public void OneStepPerWheelNotchHoweverLargeTheDelta()
        {
            const float frame = 1f / 60f;
            var wheel = new WalkPaceWheel();
            Assert.That(wheel.Update(0f, frame), Is.EqualTo(0));
            Assert.That(wheel.Update(0.004f, frame), Is.EqualTo(0), "noise below the dead zone");
            Assert.That(wheel.Update(120f, frame), Is.EqualTo(1), "a Windows-sized notch is one step");
            for (int i = 0; i < 10; i++) wheel.Update(0f, frame);
            Assert.That(wheel.Update(1f, frame), Is.EqualTo(1), "a normalised notch is one step too");
            for (int i = 0; i < 10; i++) wheel.Update(0f, frame);
            Assert.That(wheel.Update(-480f, frame), Is.EqualTo(-1), "a huge delta down is still one step");
            for (int i = 0; i < 10; i++) wheel.Update(0f, frame);
            Assert.That(wheel.Update(float.NaN, frame), Is.EqualTo(0));

            // Separate notches each give one step; a smooth-scrolling stream (a delta every frame) is held to one step
            // per repeat gap instead of one per frame.
            var stream = new WalkPaceWheel();
            int steps = 0;
            for (int i = 0; i < 60; i++) steps += stream.Update(0.5f, frame);
            int most = (int)Math.Ceiling(1f / WalkPaceWheel.RepeatGap) + 1;
            Assert.That(steps, Is.GreaterThan(0));
            Assert.That(steps, Is.LessThanOrEqualTo(most), "a second of continuous scrolling");

            var notches = new WalkPaceWheel();
            int clicks = 0;
            for (int n = 0; n < 4; n++)
            {
                clicks += notches.Update(120f, frame);
                for (int i = 0; i < 9; i++) clicks += notches.Update(0f, frame);   // a notch every 150 ms
            }

            Assert.That(clicks, Is.EqualTo(4), "four notches, four steps");
        }

        [TestCase(0.5f, "Amble")]
        [TestCase(0.6f, "Amble")]
        [TestCase(0.7f, "Stroll")]
        [TestCase(0.9f, "Stroll")]
        [TestCase(1.0f, "Walk")]
        [TestCase(1.1f, "Brisk")]
        [TestCase(1.3f, "Brisk")]
        [TestCase(1.4f, "Stride")]
        [TestCase(1.5f, "Stride")]
        public void NamesFollowTheSteps(float pace, string name)
        {
            Assert.That(WalkPace.Name(pace), Is.EqualTo(name));
        }

        [Test]
        public void LabelGivesTheNameAndTheSpeed()
        {
            Assert.That(WalkPace.Label(0.7f, WalkSpeed * 0.7f), Is.EqualTo("Stroll · 1.1 m/s"));
            Assert.That(WalkPace.Label(1f, WalkSpeed), Is.EqualTo("Walk · 1.6 m/s"));
            Assert.That(WalkPace.Label(1.5f, WalkSpeed * 1.5f), Is.EqualTo("Stride · 2.4 m/s"));
            string[] labels = WalkPace.Labels(WalkSpeed);
            Assert.That(labels.Length, Is.EqualTo(WalkPace.Count));
            Assert.That(labels[0], Is.EqualTo("Amble · 0.8 m/s"));
            Assert.That(labels[5], Is.EqualTo("Walk · 1.6 m/s"));
            Assert.That(labels[10], Is.EqualTo("Stride · 2.4 m/s"));
        }

        [Test]
        public void PaceOneReproducesTodaysSpeedsExactly()
        {
            // Manual walking: the controller asked the motor for scale 1 (walk) or the sprint scale; it now asks ManualScale.
            Assert.That(WalkPace.ManualScale(WalkPace.Default, false, SprintScale), Is.EqualTo(1f));
            Assert.That(WalkSpeed * WalkPace.ManualScale(WalkPace.Default, false, SprintScale), Is.EqualTo(1.6f));
            Assert.That(WalkPace.ManualScale(WalkPace.Default, true, SprintScale), Is.EqualTo(SprintScale));

            // Auto-travel: travelSpeed * SpeedScale * (1 + variation * sin(phase)) with SpeedScale = max(0.01, 1) before.
            float before = Math.Max(0.01f, 1f);
            float now = WalkPace.TravelScale(WalkPace.Default, 1f);
            Assert.That(now, Is.EqualTo(before));
            for (int i = 0; i <= 64; i++)
            {
                float sin = (float)Math.Sin(i * Math.PI / 32.0);
                float old = TravelSpeed * before * (1f + SpeedVariation * sin);
                float paced = TravelSpeed * now * (1f + SpeedVariation * sin);
                Assert.That(BitConverter.SingleToInt32Bits(paced), Is.EqualTo(BitConverter.SingleToInt32Bits(old)), "bit-identical at phase " + i);
            }
        }

        [Test]
        public void SprintStaysTheFixedFastSpeedAtEveryPace()
        {
            for (int i = 0; i < WalkPace.Count; i++)
            {
                float pace = WalkPace.FromIndex(i);
                Assert.That(WalkPace.ManualScale(pace, true, SprintScale), Is.EqualTo(SprintScale));
                Assert.That(WalkPace.ManualScale(pace, false, SprintScale), Is.EqualTo(pace));
            }

            Assert.That(WalkSpeed * WalkPace.ManualScale(0.5f, false, SprintScale), Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(WalkSpeed * WalkPace.ManualScale(1.5f, false, SprintScale), Is.EqualTo(2.4f).Within(1e-6f));
        }

        [Test]
        public void TheBenchmarkScaleOverridesThePace()
        {
            Assert.That(WalkPace.TravelScale(0.5f, 6f), Is.EqualTo(6f));
            Assert.That(WalkPace.TravelScale(1.5f, 12f), Is.EqualTo(12f));
            Assert.That(WalkPace.TravelScale(1.3f, 1f), Is.EqualTo(1.3f).Within(1e-6f), "at benchmark scale 1 the pace applies");
            Assert.That(WalkPace.TravelScale(0.7f, 0.5f), Is.EqualTo(0.5f));
            Assert.That(WalkPace.TravelScale(1f, 0f), Is.EqualTo(0.01f), "the old floor of the benchmark scale is kept");
        }

        // ---- feet: the locomotion tree stays speed-matched over the whole pace range ------------------------------------

        // The built PlayerLocomotion.controller: forward speeds of the clips (m/s, measured by the builder) and cycle lengths.
        private const float WalkNatural = 1.4516124f, RunNatural = 2.5852206f, SprintNatural = 7.243f;
        private const float IdleCycle = 53f / 30f, WalkCycle = 31f / 30f, RunCycle = 21f / 30f;

        /// <summary>
        /// Speed the feet show on the forward axis of the locomotion tree at a ground speed: the blend weights of the two
        /// neighbouring children, and Unity's time sync (every child is stretched to the weighted cycle length, so each child
        /// covers its own cycle distance in that time).
        /// </summary>
        private static float FootSpeed(float ground)
        {
            float[] thresholds = LocomotionMath.SpeedThresholds(WalkNatural, RunNatural, SprintNatural);
            float[] rates = LocomotionMath.SpeedTimeScales();
            float[] natural = { 0f, WalkNatural, WalkNatural, WalkNatural, RunNatural, SprintNatural };
            float[] cycles = { IdleCycle, WalkCycle, WalkCycle, WalkCycle, RunCycle, RunCycle };
            LocomotionMath.Blend1D(ground, thresholds, out int lower, out float weight);
            float wa = 1f - weight, wb = weight;
            int a = lower, b = lower + 1;
            float duration = wa * cycles[a] / rates[a] + wb * cycles[b] / rates[b];
            float distance = wa * natural[a] * cycles[a] + wb * natural[b] * cycles[b];
            return distance / duration;
        }

        [TestCase(0.8f)]    // the slowest walk
        [TestCase(0.644f)]  // the slowest auto-travel (0.5 × 1.4 m/s at the bottom of its variation)
        [TestCase(1.6f)]    // today's walk
        [TestCase(1.4f)]    // today's auto-travel
        [TestCase(2.268f)]  // the fastest auto-travel (1.5 × 1.4 m/s at the top of its variation)
        [TestCase(2.4f)]    // the fastest walk
        public void FeetKeepUpWithTheGroundAcrossThePaceRange(float ground)
        {
            float feet = FootSpeed(ground);
            Assert.That(Math.Abs(feet - ground) / ground, Is.LessThan(0.03f), $"feet {feet:0.000} m/s on ground {ground:0.000} m/s");
        }
    }
}
