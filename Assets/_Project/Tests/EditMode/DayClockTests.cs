using MedievalZen.Environment;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    public sealed class DayClockTests
    {
        private const float Tolerance = 1e-3f;

        [Test]
        public void StartsAtGivenHourOnDayOne()
        {
            var clock = new DayClock(24f, 8f);
            Assert.That(clock.Hour, Is.EqualTo(8f).Within(Tolerance));
            Assert.That(clock.Day, Is.EqualTo(1));
            Assert.That(clock.IsDay, Is.True);
        }

        [Test]
        public void TwentyFourMinuteDayAdvancesOneHourPerRealMinute()
        {
            var clock = new DayClock(24f, 8f);
            clock.Advance(60f);
            Assert.That(clock.Hour, Is.EqualTo(9f).Within(Tolerance));
        }

        [Test]
        public void AdvanceWrapsPastMidnightAndCountsDays()
        {
            var clock = new DayClock(24f, 23f);
            int midnights = clock.Advance(120f);   // two in-game hours
            Assert.That(midnights, Is.EqualTo(1));
            Assert.That(clock.Hour, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(clock.Day, Is.EqualTo(2));
        }

        [Test]
        public void AdvanceCanCrossSeveralDays()
        {
            var clock = new DayClock(1f, 12f);     // one real minute per day
            clock.Advance(150f);                   // 2.5 days: noon day 1 -> midnight starting day 4
            Assert.That(clock.Day, Is.EqualTo(4));
            Assert.That(clock.Hour, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void NegativeOrZeroTimeDoesNothing()
        {
            var clock = new DayClock(24f, 8f);
            clock.Advance(-30f);
            clock.Advance(0f);
            Assert.That(clock.Hour, Is.EqualTo(8f).Within(Tolerance));
        }

        [Test]
        public void SetTimeEarlierThanNowMovesToTheNextMorning()
        {
            var clock = new DayClock(24f, 21f);
            clock.SetTime(6f);
            Assert.That(clock.Hour, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(clock.Day, Is.EqualTo(2));
        }

        [Test]
        public void SetTimeLaterTodayKeepsTheDay()
        {
            var clock = new DayClock(24f, 3f);
            clock.SetTime(6f);
            Assert.That(clock.Day, Is.EqualTo(1));
            Assert.That(clock.Hour, Is.EqualTo(6f).Within(Tolerance));
        }

        [Test]
        public void SunElevationFollowsTheDay()
        {
            Assert.That(DayClock.SunElevation(6f, 60f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(DayClock.SunElevation(18f, 60f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(DayClock.SunElevation(12f, 60f), Is.EqualTo(60f).Within(Tolerance));
            Assert.That(DayClock.SunElevation(0f, 60f), Is.EqualTo(-60f).Within(Tolerance));
            Assert.That(DayClock.SunElevation(9f, 60f), Is.GreaterThan(0f));
            Assert.That(DayClock.SunElevation(21f, 60f), Is.LessThan(0f));
            Assert.That(DayClock.SunElevation(30f, 60f), Is.EqualTo(DayClock.SunElevation(6f, 60f)).Within(Tolerance));
        }

        [Test]
        public void SunRisesInTheEastAndSetsInTheWest()
        {
            Assert.That(DayClock.SunAzimuth(6f), Is.EqualTo(90f).Within(Tolerance));
            Assert.That(DayClock.SunAzimuth(12f), Is.EqualTo(180f).Within(Tolerance));
            Assert.That(DayClock.SunAzimuth(18f), Is.EqualTo(270f).Within(Tolerance));
        }

        [Test]
        public void IsDayOnlyBetweenSunriseAndSunset()
        {
            Assert.That(new DayClock(24f, 5.5f).IsDay, Is.False);
            Assert.That(new DayClock(24f, 6.5f).IsDay, Is.True);
            Assert.That(new DayClock(24f, 17.5f).IsDay, Is.True);
            Assert.That(new DayClock(24f, 19f).IsDay, Is.False);
        }

        [Test]
        public void ProgressAndMinuteOfDay()
        {
            var clock = new DayClock(24f, 18f);
            Assert.That(clock.Progress, Is.EqualTo(0.75f).Within(Tolerance));
            Assert.That(DayClock.MinuteOfDay(13.5f), Is.EqualTo(810));
            Assert.That(DayClock.Wrap(-1f), Is.EqualTo(23f).Within(Tolerance));
            Assert.That(DayClock.Wrap(24f), Is.EqualTo(0f).Within(Tolerance));
        }
    }
}
