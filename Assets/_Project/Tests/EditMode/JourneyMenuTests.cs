using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.UI;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class JourneySettingsTests
    {
        private static JourneySettings Custom() => new JourneySettings
        {
            Flow = FlowOfTime.VeryFast,
            Weather = WeatherOverride.Misty,
            FogAmount = 1.5f,
            HazeDistance = 0.25f,
            CloudSoftness = 2f,
            WindSound = true,
            WindVolume = 0.12f,
            Camera = CameraMode.FirstPerson,
            LookSpeed = 1.75f,
            InvertY = true,
            ShowHud = false,
            CinematicCamera = true,
            CinematicWhenTravelling = false,
            Letterbox = false,
            HudAlways = true,
        };

        [Test]
        public void DefaultsAreInRangeAndUnchangedByClamp()
        {
            var defaults = new JourneySettings();
            Assert.That(defaults.Clone().Clamp().SameAs(defaults), Is.True);
            Assert.That(defaults.Flow, Is.EqualTo(FlowOfTime.Normal));
            Assert.That(defaults.Weather, Is.EqualTo(WeatherOverride.Auto));
            Assert.That(defaults.ShowHud, Is.True);
        }

        [Test]
        public void ClampBringsEveryValueIntoRange()
        {
            var s = new JourneySettings
            {
                FogAmount = 9f, HazeDistance = -1f, CloudSoftness = float.NaN, WindVolume = 5f, LookSpeed = 0f,
                Flow = (FlowOfTime)42, Weather = (WeatherOverride)17, Camera = (CameraMode)(-3),
            }.Clamp();

            Assert.That(s.FogAmount, Is.EqualTo(2f));
            Assert.That(s.HazeDistance, Is.EqualTo(0f));
            Assert.That(s.CloudSoftness, Is.EqualTo(1f), "NaN falls back to the field's default");
            Assert.That(s.WindVolume, Is.EqualTo(JourneySettings.WindVolumeMax));
            Assert.That(s.LookSpeed, Is.EqualTo(JourneySettings.LookSpeedMin));
            Assert.That(s.Flow, Is.EqualTo(FlowOfTime.Normal));
            Assert.That(s.Weather, Is.EqualTo(WeatherOverride.Auto));
            Assert.That(s.Camera, Is.EqualTo(CameraMode.ThirdPerson));
        }

        [Test]
        public void JsonRoundTripKeepsEveryField()
        {
            JourneySettings original = Custom();
            string json = original.ToJson();
            Assert.That(JourneySettings.TryFromJson(json, new JourneySettings(), out JourneySettings read), Is.True);
            Assert.That(read.SameAs(original), Is.True, json);
        }

        [Test]
        public void JsonIsVersionedAndWritesEnumsByName()
        {
            string json = Custom().ToJson();
            StringAssert.StartsWith("{\"version\":" + JourneySettings.CurrentVersion + ",", json);
            StringAssert.Contains("\"flow\":\"VeryFast\"", json);
            StringAssert.Contains("\"weather\":\"Misty\"", json);
            StringAssert.Contains("\"camera\":\"FirstPerson\"", json);
        }

        [Test]
        public void JsonUsesInvariantNumbersUnderAnyCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string json = Custom().ToJson();
                StringAssert.Contains("\"fog\":1.5", json);
                Assert.That(JourneySettings.TryFromJson(json, new JourneySettings(), out JourneySettings read), Is.True);
                Assert.That(read.FogAmount, Is.EqualTo(1.5f).Within(1e-4f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void MissingKeysKeepTheDefaultsAndUnknownKeysAreIgnored()
        {
            JourneySettings defaults = Custom();
            string json = "{\"version\":1,\"fog\":0.5,\"somethingNew\":\"hello\",\"nested\":null}";
            Assert.That(JourneySettings.TryFromJson(json, defaults, out JourneySettings read), Is.True);
            Assert.That(read.FogAmount, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(read.Flow, Is.EqualTo(FlowOfTime.VeryFast));
            Assert.That(read.Camera, Is.EqualTo(CameraMode.FirstPerson));
            Assert.That(defaults.FogAmount, Is.EqualTo(1.5f), "the defaults object is not modified");
        }

        [Test]
        public void OlderOrUnversionedBlobIsMigratedFieldByField()
        {
            string unversioned = "{\"flow\":\"Slow\",\"weather\":\"Overcast\",\"lookSpeed\":2}";
            Assert.That(JourneySettings.TryFromJson(unversioned, new JourneySettings(), out JourneySettings a), Is.True);
            Assert.That(a.Flow, Is.EqualTo(FlowOfTime.Slow));
            Assert.That(a.Weather, Is.EqualTo(WeatherOverride.Overcast));
            Assert.That(a.LookSpeed, Is.EqualTo(2f));
            StringAssert.StartsWith("{\"version\":" + JourneySettings.CurrentVersion, a.ToJson(), "re-stamped on save");

            string versionZero = "{\"version\":0,\"showHud\":false,\"fog\":7}";
            Assert.That(JourneySettings.TryFromJson(versionZero, new JourneySettings(), out JourneySettings b), Is.True);
            Assert.That(b.ShowHud, Is.False);
            Assert.That(b.FogAmount, Is.EqualTo(JourneySettings.AtmosphereMax), "migrated values are clamped");
        }

        [Test]
        public void VersionOneBlobLoadsWithTheCinematicDefaults()
        {
            // A blob saved before the cinematic camera existed (version 1): every old field is read, the new ones default.
            string v1 = "{\"version\":1,\"flow\":\"Fast\",\"weather\":\"Auto\",\"fog\":1,\"haze\":1,\"clouds\":1,\"wind\":false," +
                        "\"windVolume\":0.06,\"camera\":\"FirstPerson\",\"lookSpeed\":1.5,\"invertY\":false,\"showHud\":true}";
            Assert.That(JourneySettings.TryFromJson(v1, new JourneySettings(), out JourneySettings read), Is.True);
            Assert.That(read.Flow, Is.EqualTo(FlowOfTime.Fast));
            Assert.That(read.Camera, Is.EqualTo(CameraMode.FirstPerson));
            Assert.That(read.LookSpeed, Is.EqualTo(1.5f));
            Assert.That(read.CinematicCamera, Is.False);
            Assert.That(read.CinematicWhenTravelling, Is.True);
            Assert.That(read.Letterbox, Is.True);
            Assert.That(read.HudAlways, Is.False);
            StringAssert.StartsWith("{\"version\":2,", read.ToJson(), "re-stamped as version 2 on the next save");
        }

        [Test]
        public void CinematicChoicesRoundTripAndCountForSameAs()
        {
            var a = new JourneySettings { CinematicCamera = true, CinematicWhenTravelling = false, Letterbox = false, HudAlways = true };
            string json = a.ToJson();
            StringAssert.Contains("\"cinematic\":true", json);
            StringAssert.Contains("\"cinematicTravel\":false", json);
            StringAssert.Contains("\"letterbox\":false", json);
            StringAssert.Contains("\"hudAlways\":true", json);
            Assert.That(JourneySettings.TryFromJson(json, new JourneySettings(), out JourneySettings read), Is.True);
            Assert.That(read.SameAs(a), Is.True);
            Assert.That(new JourneySettings().SameAs(a), Is.False);
            var defaults = new JourneySettings();
            Assert.That(defaults.CinematicCamera, Is.False, "the cinematic camera is opt-in as the camera choice");
            Assert.That(defaults.CinematicWhenTravelling, Is.True, "auto-travel is filmed by default");
            Assert.That(defaults.Letterbox, Is.True);
        }

        [Test]
        public void NewerVersionIsRejectedAndDefaultsAreUsed()
        {
            JourneySettings defaults = Custom();
            string json = "{\"version\":" + (JourneySettings.CurrentVersion + 1) + ",\"fog\":0.1,\"flow\":\"Paused\"}";
            Assert.That(JourneySettings.TryFromJson(json, defaults, out JourneySettings read), Is.False);
            Assert.That(read.SameAs(defaults), Is.True);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        [TestCase("{\"fog\":}")]
        [TestCase("{\"fog\":1")]
        [TestCase("{\"fog\":{\"a\":1}}")]
        [TestCase("[1,2]")]
        [TestCase("{\"fog\":1} trailing")]
        public void MalformedBlobIsRejected(string json)
        {
            JourneySettings defaults = Custom();
            Assert.That(JourneySettings.TryFromJson(json, defaults, out JourneySettings read), Is.False);
            Assert.That(read.SameAs(defaults), Is.True);
        }

        [Test]
        public void WrongTypesAndNumericEnumsAreSkipped()
        {
            string json = "{\"version\":1,\"fog\":\"lots\",\"wind\":1,\"flow\":\"3\",\"weather\":2,\"camera\":\"Sideways\",\"invertY\":true}";
            Assert.That(JourneySettings.TryFromJson(json, new JourneySettings(), out JourneySettings read), Is.True);
            Assert.That(read.FogAmount, Is.EqualTo(1f));
            Assert.That(read.WindSound, Is.False);
            Assert.That(read.Flow, Is.EqualTo(FlowOfTime.Normal));
            Assert.That(read.Weather, Is.EqualTo(WeatherOverride.Auto));
            Assert.That(read.Camera, Is.EqualTo(CameraMode.ThirdPerson));
            Assert.That(read.InvertY, Is.True);
        }

        [Test]
        public void FlatJsonReadsEscapesAndNumbers()
        {
            Assert.That(FlatJson.TryParse("{ \"a\" : \"x\\\"y\\u0041\" , \"b\": -1.5e2, \"c\": true, \"d\": null }", out Dictionary<string, object> v), Is.True);
            Assert.That(v["a"], Is.EqualTo("x\"yA"));
            Assert.That(v["b"], Is.EqualTo(-150.0));
            Assert.That(v["c"], Is.EqualTo(true));
            Assert.That(v["d"], Is.Null);
            Assert.That(FlatJson.TryParse("{}", out v), Is.True);
            Assert.That(v.Count, Is.Zero);
        }
    }

    public sealed class JourneyTimeTests
    {
        [Test]
        public void FlowScalesMultiplyThePassageOfTime()
        {
            Assert.That(JourneyTime.FlowScale(FlowOfTime.Paused), Is.EqualTo(0f));
            Assert.That(JourneyTime.FlowScale(FlowOfTime.Slow), Is.EqualTo(0.5f));
            Assert.That(JourneyTime.FlowScale(FlowOfTime.Normal), Is.EqualTo(1f));
            Assert.That(JourneyTime.FlowScale(FlowOfTime.Fast), Is.EqualTo(2f));
            Assert.That(JourneyTime.FlowScale(FlowOfTime.VeryFast), Is.EqualTo(4f));
        }

        [Test]
        public void FlowScaleTimesTheClockGivesTheRealDayLength()
        {
            // DayCycle advances its clock by deltaTime × TimeScale; a 24-minute world day at Fast lasts 12 real minutes.
            var clock = new DayClock(24f, 0f);
            float scale = JourneyTime.FlowScale(FlowOfTime.Fast);
            clock.Advance(6f * 60f * scale);
            Assert.That(clock.Hour, Is.EqualTo(12f).Within(1e-3f));
            Assert.That(JourneyTime.RealDayMinutes(24f, scale), Is.EqualTo(12f).Within(1e-4f));
            Assert.That(JourneyTime.RealDayMinutes(24f, 0.5f), Is.EqualTo(48f).Within(1e-4f));
            Assert.That(float.IsPositiveInfinity(JourneyTime.RealDayMinutes(24f, 0f)), Is.True);
            Assert.That(JourneyTime.DayLengthText(24f, 0f), Is.EqualTo("Time stands still"));
            Assert.That(JourneyTime.DayLengthText(24f, 1f), Is.EqualTo("A day lasts 24 minutes"));
            Assert.That(JourneyTime.DayLengthText(60f, 0.5f), Is.EqualTo("A day lasts about 2 hours"));
        }

        [Test]
        public void PausedFlowStopsTheClock()
        {
            var clock = new DayClock(24f, 9f);
            clock.Advance(600f * JourneyTime.FlowScale(FlowOfTime.Paused));
            Assert.That(clock.Hour, Is.EqualTo(9f));
        }

        [TestCase(0f, FlowOfTime.Paused)]
        [TestCase(-2f, FlowOfTime.Paused)]
        [TestCase(0.2f, FlowOfTime.Paused)]
        [TestCase(0.5f, FlowOfTime.Slow)]
        [TestCase(1f, FlowOfTime.Normal)]
        [TestCase(1.5f, FlowOfTime.Normal)]
        [TestCase(3f, FlowOfTime.Fast)]
        [TestCase(10f, FlowOfTime.VeryFast)]
        public void NearestFlowPicksTheClosestChoice(float scale, FlowOfTime expected)
        {
            Assert.That(JourneyTime.NearestFlow(scale), Is.EqualTo(expected));
        }

        [Test]
        public void SettingAnEarlierHourStaysOnTheSameDay()
        {
            var clock = new DayClock(24f, 20f, 3);
            int day = JourneyTime.SetHourSameDay(clock, 6f);
            Assert.That(day, Is.EqualTo(3));
            Assert.That(clock.Day, Is.EqualTo(3));
            Assert.That(clock.Hour, Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void SettingALaterHourStaysOnTheSameDay()
        {
            var clock = new DayClock(24f, 6f, 3);
            JourneyTime.SetHourSameDay(clock, 23f);
            Assert.That(clock.Day, Is.EqualTo(3));
            Assert.That(clock.Hour, Is.EqualTo(23f).Within(1e-4f));
        }

        [Test]
        public void SliderEndsNeverReachAnotherDay()
        {
            var clock = new DayClock(24f, 12f, 5);
            JourneyTime.SetHourSameDay(clock, 24f);    // right end
            Assert.That(clock.Day, Is.EqualTo(5));
            Assert.That(clock.Hour, Is.LessThan(24f));
            Assert.That(clock.Hour, Is.GreaterThan(23.9f));
            JourneyTime.SetHourSameDay(clock, 0f);     // left end
            Assert.That(clock.Day, Is.EqualTo(5));
            Assert.That(clock.Hour, Is.EqualTo(0f));
            JourneyTime.SetHourSameDay(clock, -3f);
            Assert.That(clock.Hour, Is.EqualTo(0f));
            JourneyTime.SetHourSameDay(clock, float.NaN);
            Assert.That(clock.Day, Is.EqualTo(5));
        }

        [Test]
        public void AfterScrubbingTheNaturalFlowStillCountsMidnight()
        {
            var clock = new DayClock(24f, 12f, 2);
            JourneyTime.SetHourSameDay(clock, 23.5f);
            clock.Advance(60f);   // one in-game hour
            Assert.That(clock.Day, Is.EqualTo(3));
            Assert.That(clock.Hour, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void RestingAtAnInnStillMovesToTheNextMorning()
        {
            // The inn keeps DayClock.SetTime's forward-only rule; only the menu uses the same-day rule.
            var clock = new DayClock(24f, 21f, 2);
            clock.SetTime(6f);
            Assert.That(clock.Day, Is.EqualTo(3));
        }

        [Test]
        public void GlideStaysWithinTheDayAndEndsOnTarget()
        {
            Assert.That(JourneyTime.GlideHour(22f, 6f, 0f), Is.EqualTo(22f).Within(1e-4f));
            Assert.That(JourneyTime.GlideHour(22f, 6f, 1f), Is.EqualTo(6f).Within(1e-4f));
            Assert.That(JourneyTime.GlideHour(22f, 6f, 2f), Is.EqualTo(6f).Within(1e-4f));
            float previous = 22f;
            for (int i = 1; i <= 20; i++)
            {
                float h = JourneyTime.GlideHour(22f, 6f, i / 20f);
                Assert.That(h, Is.LessThanOrEqualTo(previous + 1e-5f), "backwards through the day, never across midnight");
                Assert.That(h, Is.InRange(6f, 22f));
                previous = h;
            }
        }

        [Test]
        public void QuickTimesAreInOrderAndInsideTheDay()
        {
            Assert.That(JourneyTime.DawnHour, Is.GreaterThanOrEqualTo(DayClock.Sunrise));
            Assert.That(JourneyTime.NoonHour, Is.EqualTo(12f));
            Assert.That(JourneyTime.DuskHour, Is.LessThanOrEqualTo(DayClock.Sunset));
            Assert.That(JourneyTime.NightHour, Is.InRange(DayClock.Sunset + 2f, 24f));
            Assert.That(DayClock.SunElevation(JourneyTime.NightHour), Is.LessThan(0f));
        }

        [Test]
        public void FormatHourShowsHoursAndMinutes()
        {
            Assert.That(JourneyTime.FormatHour(0f), Is.EqualTo("00:00"));
            Assert.That(JourneyTime.FormatHour(7.0834f), Is.EqualTo("07:05"));
            Assert.That(JourneyTime.FormatHour(24f), Is.EqualTo("24:00"));
        }
    }

    public sealed class FastTravelPlannerTests
    {
        private const float Tolerance = 0.05f;

        // Town A at the origin (radius 10) with a short road east to village B and a longer road south to inn C.
        private RoadNetwork network;
        private Settlement town;
        private Settlement village;
        private Settlement inn;

        [SetUp]
        public void SetUp()
        {
            var builder = new RoadNetworkBuilder();
            town = builder.AddSettlement("A", SettlementType.Town, Vector3.zero, 10f);
            village = builder.AddSettlement("B", SettlementType.Village, new Vector3(200f, 0f, 0f), 8f);
            inn = builder.AddSettlement("C", SettlementType.Inn, new Vector3(0f, 0f, -300f), 5f);
            builder.AddStraightSegment(town.Node, village.Node);   // segment 0, 200 m along +X
            builder.AddStraightSegment(inn.Node, town.Node);       // segment 1, 300 m, stored C -> A
            network = builder.Build();
        }

        private static float Planar(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static Vector3 Forward(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));

        [Test]
        public void SettlementSpotIsOnItsLongestRoadJustOutsideFacingTheCentre()
        {
            Assert.That(FastTravelPlanner.TryPlanSettlement(network, town, new FastTravelGround(), out FastTravelSpot spot), Is.True);
            Assert.That(spot.Road.Segment, Is.SameAs(network.Segments[1]), "the longer road is the main road");
            Assert.That(Planar(spot.Position, town.Position), Is.EqualTo(town.Radius + FastTravelPlanner.SettlementStandOff).Within(Tolerance));
            Assert.That(spot.Position.z, Is.LessThan(0f));
            Vector3 toCentre = (town.Position - spot.Position).normalized;
            Assert.That(Vector3.Dot(Forward(spot.Yaw), toCentre), Is.GreaterThan(0.99f));
        }

        [Test]
        public void SettlementSpotWorksForRoadsStoredEitherWay()
        {
            // Segment 1 is stored C -> A, so for the inn the road starts at its node (fromA) and for the town it ends there.
            Assert.That(FastTravelPlanner.TryPlanSettlement(network, inn, new FastTravelGround(), out FastTravelSpot spot), Is.True);
            Assert.That(Planar(spot.Position, inn.Position), Is.EqualTo(inn.Radius + FastTravelPlanner.SettlementStandOff).Within(Tolerance));
            Assert.That(Vector3.Dot(Forward(spot.Yaw), (inn.Position - spot.Position).normalized), Is.GreaterThan(0.99f));
        }

        [Test]
        public void SettlementSpotAvoidsWater()
        {
            // The main road south of the town is flooded (ground at 0 < water 1); the dry east road is used instead.
            var ground = new FastTravelGround { WaterHeight = 1f, HeightAt = (x, z) => z < -2f ? 0f : 5f };
            Assert.That(FastTravelPlanner.TryPlanSettlement(network, town, ground, out FastTravelSpot spot), Is.True);
            Assert.That(spot.Road.Segment, Is.SameAs(network.Segments[0]));
            Assert.That(spot.Position.y, Is.EqualTo(5f));
            Assert.That(FastTravelPlanner.IsWalkable(spot.Position, ground), Is.True);
        }

        [Test]
        public void SettlementSpotAvoidsSteepGround()
        {
            // Everything south of the town is a 45-degree face; the east road is flat.
            var ground = new FastTravelGround { HeightAt = (x, z) => z < -2f ? -z : 0f };
            Assert.That(FastTravelPlanner.TryPlanSettlement(network, town, ground, out FastTravelSpot spot), Is.True);
            Assert.That(spot.Road.Segment, Is.SameAs(network.Segments[0]));
            Assert.That(FastTravelPlanner.IsWalkable(spot.Position, ground), Is.True);
        }

        [Test]
        public void SpotTakesTheGroundHeight()
        {
            var ground = new FastTravelGround { HeightAt = (x, z) => 42f };
            Assert.That(FastTravelPlanner.TryPlanSettlement(network, village, ground, out FastTravelSpot spot), Is.True);
            Assert.That(spot.Position.y, Is.EqualTo(42f));
        }

        [Test]
        public void LandmarkSpotIsTheNearestRoadPointFacingTheLandmark()
        {
            var peak = new Vector3(120f, 80f, 60f);   // 60 m north of the east road
            Assert.That(FastTravelPlanner.TryPlanNear(network, peak, new FastTravelGround(), out FastTravelSpot spot), Is.True);
            Assert.That(spot.Position.x, Is.EqualTo(120f).Within(0.5f));
            Assert.That(spot.Position.z, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(spot.Yaw, Is.EqualTo(0f).Within(0.5f), "faces north, towards the peak");
        }

        [Test]
        public void LandmarkSpotWalksAlongTheRoadOutOfTheWater()
        {
            // A lake covers x in [80, 150] and the road through it; its centre is nearest to x = 120.
            var lake = new Vector3(120f, 0f, 30f);
            var ground = new FastTravelGround { WaterHeight = 2f, HeightAt = (x, z) => x >= 80f && x <= 150f ? 0f : 4f };
            Assert.That(FastTravelPlanner.TryPlanNear(network, lake, ground, out FastTravelSpot spot), Is.True);
            Assert.That(spot.Position.x, Is.GreaterThan(150f).Or.LessThan(80f));
            Assert.That(Mathf.Min(Mathf.Abs(spot.Position.x - 150f), Mathf.Abs(spot.Position.x - 80f)), Is.LessThanOrEqualTo(FastTravelPlanner.SearchStep + 1f),
                "the nearest dry point along the road");
            Assert.That(spot.Position.y, Is.EqualTo(4f));
            Assert.That(Vector3.Dot(Forward(spot.Yaw), new Vector3(lake.x - spot.Position.x, 0f, lake.z - spot.Position.z).normalized), Is.GreaterThan(0.99f));
        }

        [Test]
        public void TargetOnTheRoadFacesAlongTheRoad()
        {
            Assert.That(FastTravelPlanner.TryPlanNear(network, new Vector3(100f, 0f, 1f), new FastTravelGround(), out FastTravelSpot spot), Is.True);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(spot.Yaw, 90f)), Is.LessThan(0.5f), "segment 0 runs towards +X");
        }

        [Test]
        public void UnknownGroundIsNeverUsed()
        {
            // Heights are unknown (outside the terrain) except east of x = 60.
            var ground = new FastTravelGround { HeightAt = (x, z) => x > 60f ? 1f : float.NaN };
            Assert.That(FastTravelPlanner.TryPlanNear(network, new Vector3(10f, 0f, 5f), ground, out FastTravelSpot spot), Is.True);
            Assert.That(spot.Position.x, Is.GreaterThan(60f));
        }

        [Test]
        public void NoRoadsOrNoDryGroundMeansNoSpot()
        {
            RoadNetwork empty = new RoadNetworkBuilder().Build();
            Assert.That(FastTravelPlanner.TryPlanNear(empty, Vector3.zero, new FastTravelGround(), out _), Is.False);
            var flooded = new FastTravelGround { WaterHeight = 10f, HeightAt = (x, z) => 0f };
            Assert.That(FastTravelPlanner.TryPlanSettlement(network, town, flooded, out _), Is.False);
            Assert.That(FastTravelPlanner.TryPlanSettlement(null, town, null, out _), Is.False);
        }

        [Test]
        public void YawConvention()
        {
            Assert.That(FastTravelPlanner.YawOf(Vector3.forward), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(FastTravelPlanner.YawOf(Vector3.right), Is.EqualTo(90f).Within(1e-4f));
            Assert.That(Mathf.Abs(FastTravelPlanner.YawOf(Vector3.back)), Is.EqualTo(180f).Within(1e-4f));
            Assert.That(FastTravelPlanner.YawOf(Vector3.up), Is.EqualTo(0f));
        }
    }
}
