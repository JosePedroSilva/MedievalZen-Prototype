using MedievalZen.Endless;
using MedievalZen.World;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The third performance pass of the endless road (2026-10-06): which prop families are warmed behind a hold, the heavy
    /// threshold, the stock refill rule and the hold's extension for the warm-up. Pure.
    /// </summary>
    public sealed class EndlessPerf3Tests
    {
        [Test]
        public void PlaceBuildingsLandmarksAndBridgesAreWarmed()
        {
            string[] warmed =
            {
                PropRole.Tavern, PropRole.House, PropRole.Hut, PropRole.Shelter, PropRole.Smithy, PropRole.Stables, PropRole.Gatehouse,
                PropRole.LandmarkTower, PropRole.LandmarkChapel, PropRole.LandmarkMill, PropRole.LandmarkRuin, PropRole.Bridge,
                PropRole.Windmill, PropRole.MarketStall, PropRole.Cabin, PropRole.Well, PropRole.WaterWheel,
            };
            foreach (string family in warmed) Assert.That(PropWarmup.IsCandidateFamily(family), Is.True, family);
        }

        [Test]
        public void InstancedAndEverywhereFamiliesAreNotWarmed()
        {
            string[] skipped =
            {
                PropRole.ForestTree, PropRole.PineTree, PropRole.DeadTree, PropRole.Bush, PropRole.Shrub, PropRole.Fern, PropRole.Undergrowth,
                PropRole.Ivy, PropRole.IvyDrape, PropRole.StoneWall, PropRole.Fence, PropRole.Hedge, PropRole.Rock, PropRole.Cliff,
                PropRole.PassCliff, PropRole.PassBoulder, PropRole.HorizonHill, PropRole.HorizonMountain, null, string.Empty,
            };
            foreach (string family in skipped) Assert.That(PropWarmup.IsCandidateFamily(family), Is.False, family ?? "null");
        }

        [Test]
        public void HeavyFromFiveThousandTriangles()
        {
            Assert.That(PropWarmup.IsHeavy(4999), Is.False);
            Assert.That(PropWarmup.IsHeavy(5000), Is.True);
            Assert.That(PropWarmup.IsHeavy(34559), Is.True, "the tavern");
            Assert.That(PropWarmup.IsHeavy(2503), Is.False, "the outhouse");
        }

        [Test]
        public void HeavyPropsStayAloneInTheirGroup()
        {
            // A heavy building opens a group of its own and closes it (the group budget is below the presets' weight).
            Assert.That(ApplySlicing.StartsNewGroup(5, 3000, 34559, 32), Is.True);
            Assert.That(ApplySlicing.StartsNewGroup(1, 34559, 200, 32), Is.True);
            Assert.That(ApplySlicing.StartsNewGroup(0, 0, 34559, 32), Is.False);
        }

        [Test]
        public void StockRefillRule()
        {
            Assert.That(PropWarmup.ShouldStock(true, false, double.PositiveInfinity), Is.True);
            Assert.That(PropWarmup.ShouldStock(true, false, double.NaN), Is.True, "no place known");
            Assert.That(PropWarmup.ShouldStock(true, false, 301.0), Is.True);
            Assert.That(PropWarmup.ShouldStock(true, false, 300.0), Is.False, "within the clearance of a place");
            Assert.That(PropWarmup.ShouldStock(true, false, 40.0), Is.False);
            Assert.That(PropWarmup.ShouldStock(true, true, 5000.0), Is.False, "never while tiles stream");
            Assert.That(PropWarmup.ShouldStock(false, false, 5000.0), Is.False, "every heavy prefab already has an idle instance");
        }

        [Test]
        public void HoldWaitsAtMostASecondForTheWarmup()
        {
            Assert.That(PropWarmup.HoldsForWarmup(true, true, 0f), Is.True);
            Assert.That(PropWarmup.HoldsForWarmup(true, true, 0.99f), Is.True);
            Assert.That(PropWarmup.HoldsForWarmup(true, true, 1f), Is.False);
            Assert.That(PropWarmup.HoldsForWarmup(false, true, 0f), Is.False, "nothing pending");
            Assert.That(PropWarmup.HoldsForWarmup(true, false, 0f), Is.False, "not black: the warm-up does not run, so never wait for it");
        }
    }
}
