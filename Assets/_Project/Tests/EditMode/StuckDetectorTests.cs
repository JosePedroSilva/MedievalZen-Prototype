using MedievalZen.Travel;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    public sealed class StuckDetectorTests
    {
        private const float Frame = 0.1f;

        private static StuckAction Run(StuckDetector detector, float seconds, float speed)
        {
            StuckAction result = StuckAction.None;
            int frames = (int)System.Math.Round(seconds / Frame);
            for (int i = 0; i < frames; i++)
            {
                StuckAction action = detector.Update(Frame, speed);
                if (action != StuckAction.None) result = action;
            }

            return result;
        }

        [Test]
        public void MovingNormallyNeverTriggers()
        {
            var detector = new StuckDetector();
            Assert.That(Run(detector, 30f, 1.4f), Is.EqualTo(StuckAction.None));
        }

        [Test]
        public void ShortStopsDoNotTrigger()
        {
            var detector = new StuckDetector();
            Assert.That(Run(detector, 2.5f, 0.05f), Is.EqualTo(StuckAction.None));
            Assert.That(Run(detector, 1f, 1.2f), Is.EqualTo(StuckAction.None));
            Assert.That(Run(detector, 2.5f, 0.05f), Is.EqualTo(StuckAction.None));
        }

        [Test]
        public void FirstStallReprojectsThenSecondGivesUp()
        {
            var detector = new StuckDetector(0.2f, 3f, 3f);
            Assert.That(Run(detector, 3.1f, 0.1f), Is.EqualTo(StuckAction.Reproject));
            Assert.That(detector.HasReprojected, Is.True);
            Assert.That(Run(detector, 3.1f, 0.1f), Is.EqualTo(StuckAction.GiveUp));
            Assert.That(detector.HasReprojected, Is.False);
        }

        [Test]
        public void RecoveringAfterReprojectionForgetsTheAttempt()
        {
            var detector = new StuckDetector(0.2f, 3f, 3f);
            Assert.That(Run(detector, 3.1f, 0f), Is.EqualTo(StuckAction.Reproject));
            Assert.That(Run(detector, 4f, 1.3f), Is.EqualTo(StuckAction.None));
            Assert.That(detector.HasReprojected, Is.False);
            Assert.That(Run(detector, 3.1f, 0f), Is.EqualTo(StuckAction.Reproject));
        }

        [Test]
        public void BriefProgressBetweenStallsDoesNotForgetTheAttempt()
        {
            var detector = new StuckDetector(0.2f, 3f, 3f);
            Assert.That(Run(detector, 3.1f, 0f), Is.EqualTo(StuckAction.Reproject));
            Assert.That(Run(detector, 1f, 0.8f), Is.EqualTo(StuckAction.None));
            Assert.That(Run(detector, 3.1f, 0f), Is.EqualTo(StuckAction.GiveUp));
        }

        [Test]
        public void ResetClearsHistory()
        {
            var detector = new StuckDetector();
            Run(detector, 3.1f, 0f);
            detector.Reset();
            Assert.That(detector.HasReprojected, Is.False);
            Assert.That(detector.SlowTime, Is.EqualTo(0f));
        }
    }
}
