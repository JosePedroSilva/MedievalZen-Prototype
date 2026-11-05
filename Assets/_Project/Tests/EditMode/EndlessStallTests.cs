using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Review;
using MedievalZen.World;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The stall hunt of 2026-10-06: the main-thread searches (inn finder 8 km, village finder 12 km, nature and people 1.5 km)
    /// read only regions the worker has warmed, so their cold cost is counted in region builds and must be zero once warmed;
    /// the waypoints do not depend on query order; the landmark sight test keeps the frame's central third clear.
    /// </summary>
    public sealed class EndlessStallTests
    {
        private static EndlessGenerator Make(int seed) => new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);

        [TestCase(12345, 1643.0)]
        [TestCase(12345, 21194.0)]
        [TestCase(42, 5000.0)]
        [TestCase(777, 40000.0)]
        public void WarmedSearchesBuildNoRegion(int seed, double s)
        {
            EndlessGenerator g = Make(seed);
            Assert.That(InnFinder.Ready(g, s, 12000.0), Is.False, "a cold generator is not ready: the caller keeps its last answer");
            g.WarmWaypoints(s - InnFinder.WarmReach, s + InnFinder.WarmReach);
            int built = g.RegionWaypointBuilds;
            Assert.That(built, Is.GreaterThan(0));

            // Every main-thread query after the warmer, also after the traveller moved one warm step on.
            foreach (double at in new[] { s, s + 999.0, s - 999.0 })
            {
                Assert.That(InnFinder.Ready(g, at, 8000.0), Is.True);
                Assert.That(InnFinder.Ready(g, at, 12000.0), Is.True);
                InnFinder.Find(g, at, +1);
                InnFinder.Find(g, at, -1);
                InnFinder.FindVillage(g, at, +1);
                InnFinder.FindVillage(g, at, -1);
                g.Waypoints(at - 1500.0, at + 1500.0, new List<Waypoint>());
                Assert.That(g.WaypointsCached(at - 1500.0, at + 1500.0), Is.True);
            }

            Assert.That(g.RegionWaypointBuilds, Is.EqualTo(built), "no lazy region build on the main thread once warmed");
        }

        [Test]
        public void ColdSearchWorkIsBoundedByItsRange()
        {
            // A cold search builds at most the regions its range overlaps (no look-back beyond it).
            EndlessGenerator g = Make(12345);
            double s = 21194.0;
            int overlapping = 0;
            Region r = g.Regions.At(s - 12000.0 - InnFinder.SearchStep);
            while (r.Start <= s + 12000.0 + InnFinder.SearchStep)
            {
                overlapping++;
                r = g.Regions.Get(r.Index + 1);
            }

            InnFinder.FindVillage(g, s, +1);
            InnFinder.FindVillage(g, s, -1);
            Assert.That(g.RegionWaypointBuilds, Is.LessThanOrEqualTo(overlapping));
        }

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void WaypointsDoNotDependOnQueryOrder(int seed)
        {
            const double from = 15000.0, to = 27000.0;
            EndlessGenerator forward = Make(seed);
            forward.WarmWaypoints(from, to);
            var a = new List<Waypoint>();
            forward.Waypoints(from, to, a);

            EndlessGenerator backward = Make(seed);
            for (double x = to; x > from; x -= 1500.0) backward.Waypoints(x - 1500.0, x, new List<Waypoint>());
            InnFinder.FindVillage(backward, to, -1);
            var b = new List<Waypoint>();
            backward.Waypoints(from, to, b);

            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Id, Is.EqualTo(a[i].Id));
                Assert.That(b[i].S, Is.EqualTo(a[i].S));
                Assert.That(b[i].X, Is.EqualTo(a[i].X));
                Assert.That(b[i].Z, Is.EqualTo(a[i].Z));
                Assert.That(b[i].ViewS, Is.EqualTo(a[i].ViewS));
                Assert.That(b[i].Name, Is.EqualTo(a[i].Name));
            }
        }

        private static ReviewSight.Tree Oak(double x, double z) => new ReviewSight.Tree
        {
            X = x, Z = z, Ground = -1f, CrownBottom = 3f, Top = 12f, TrunkRadius = 0.6f, CrownRadius = 3f,
        };

        [Test]
        public void SightKeepsTheFramesCentralThirdClear()
        {
            // The Wick Windmill shot: 700 m to a 17 m mill, a 7° (vertical) frame at 16:9.
            ReviewSight.FrameSlopes(7f, 16f / 9f, out float across, out float up);
            float target = 8f;

            // A tree 3 m ahead on the line fills the frame; 4 m to the side it is outside a 7° frame.
            Assert.That(ReviewSight.Clear(new[] { Oak(0.3, 3.0) }, 0, 0, 2.4f, 0, 700, target, ReviewSight.Reach, 0f, across, up), Is.False);
            Assert.That(ReviewSight.Clear(new[] { Oak(4.0, 3.0) }, 0, 0, 2.4f, 0, 700, target, ReviewSight.Reach, 0f, across, up), Is.True);

            // A crown whose edge is 2 m beside the line 60 m out: the line alone passes, the frame's central third does not.
            Assert.That(ReviewSight.Clear(new[] { Oak(5.0, 60.0) }, 0, 0, 2.4f, 0, 700, target), Is.True, "the old line-only test");
            Assert.That(ReviewSight.Clear(new[] { Oak(5.0, 60.0) }, 0, 0, 2.4f, 0, 700, target, ReviewSight.Reach, 0f, across, up), Is.False);

            // Well to the side at 60 m (outside the central third), or beyond the tested 80 m: clear.
            Assert.That(ReviewSight.Clear(new[] { Oak(9.0, 60.0) }, 0, 0, 2.4f, 0, 700, target, ReviewSight.Reach, 0f, across, up), Is.True);
            Assert.That(ReviewSight.Clear(new[] { Oak(1.0, 95.0) }, 0, 0, 2.4f, 0, 700, target, ReviewSight.Reach, 0f, across, up), Is.True);

            // Raising the eye over the 60 m crown clears it (the frame's top third rises with it).
            Assert.That(ReviewSight.Clear(new[] { Oak(5.0, 60.0) }, 0, 0, 14f, 0, 700, target, ReviewSight.Reach, 0f, across, up), Is.True);
            Assert.That(ReviewSight.Raises[ReviewSight.Raises.Length - 1], Is.EqualTo(8f));
        }
    }
}
