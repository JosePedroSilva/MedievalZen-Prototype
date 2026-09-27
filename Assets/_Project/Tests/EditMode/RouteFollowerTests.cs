using MedievalZen.Roads;
using MedievalZen.Travel;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class RouteFollowerTests
    {
        private const float Tolerance = 1e-2f;

        // L-shaped road: A (0,0,0) -> B (100,0,0) -> C (100,0,100), plus a spur B -> D (200,0,0).
        private RoadNetwork network;
        private Settlement settlementA;
        private Settlement settlementC;

        [SetUp]
        public void SetUp()
        {
            var builder = new RoadNetworkBuilder();
            settlementA = builder.AddSettlement("A", SettlementType.Town, Vector3.zero, 10f);
            settlementC = builder.AddSettlement("C", SettlementType.Village, new Vector3(100f, 0f, 100f), 10f);
            Settlement settlementD = builder.AddSettlement("D", SettlementType.Inn, new Vector3(200f, 0f, 0f), 5f);
            RoadNode b = builder.AddNode(new Vector3(100f, 0f, 0f));
            builder.AddStraightSegment(settlementA.Node, b);                 // segment 0
            builder.AddStraightSegment(settlementC.Node, b);                 // segment 1, stored C -> B (travelled backwards)
            builder.AddStraightSegment(b, settlementD.Node);                 // segment 2 (a spur not on the A-C route)
            network = builder.Build();
        }

        private RouteFollower FollowerFromAToC()
        {
            Assert.That(network.TryFindRoute(network.Project(Vector3.zero), settlementC, out RoadRoute route), Is.True);
            return new RouteFollower(route);
        }

        [Test]
        public void StartsAtRouteStart()
        {
            RouteFollower follower = FollowerFromAToC();
            Assert.That(follower.Distance, Is.EqualTo(0f));
            Assert.That(follower.TotalLength, Is.EqualTo(200f).Within(Tolerance));
            Assert.That(follower.IsFinished, Is.False);
            Assert.That(Vector3.Distance(follower.Position, Vector3.zero), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(follower.Tangent, Vector3.right), Is.LessThan(Tolerance));
        }

        [Test]
        public void Advance_MovesAlongRoute()
        {
            RouteFollower follower = FollowerFromAToC();
            Assert.That(follower.Advance(30f), Is.EqualTo(30f).Within(Tolerance));
            Assert.That(Vector3.Distance(follower.Position, new Vector3(30f, 0f, 0f)), Is.LessThan(Tolerance));

            follower.Advance(100f);
            Assert.That(Vector3.Distance(follower.Position, new Vector3(100f, 0f, 30f)), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(follower.Tangent, Vector3.forward), Is.LessThan(Tolerance));
            Assert.That(follower.Remaining, Is.EqualTo(70f).Within(Tolerance));
        }

        [Test]
        public void Advance_ClampsAndFinishes()
        {
            RouteFollower follower = FollowerFromAToC();
            float moved = follower.Advance(1000f);
            Assert.That(moved, Is.EqualTo(200f).Within(Tolerance));
            Assert.That(follower.Distance, Is.EqualTo(follower.TotalLength));
            Assert.That(follower.IsFinished, Is.True);
            Assert.That(follower.Remaining, Is.EqualTo(0f));
            Assert.That(Vector3.Distance(follower.Position, settlementC.Position), Is.LessThan(Tolerance));

            follower.Advance(-1000f);
            Assert.That(follower.Distance, Is.EqualTo(0f));
            Assert.That(follower.IsFinished, Is.False);
        }

        [Test]
        public void LookAhead_StaysOnRouteAndClampsAtEnd()
        {
            RouteFollower follower = FollowerFromAToC();
            var probe = new RouteFollower(follower.Route);
            for (float d = 0f; d <= follower.TotalLength; d += 3.7f)
            {
                follower.SetDistance(d);
                Vector3 ahead = follower.LookAhead(2.5f);
                probe.ClosestDistance(ahead, out float separation);
                Assert.That(separation, Is.LessThan(Tolerance), $"LookAhead left the route at d = {d}");
                Assert.That(Vector3.Distance(ahead, follower.Route.Evaluate(Mathf.Min(d + 2.5f, follower.TotalLength))), Is.LessThan(Tolerance));
            }

            follower.SetDistance(follower.TotalLength - 1f);
            Assert.That(Vector3.Distance(follower.LookAhead(2.5f), settlementC.Position), Is.LessThan(Tolerance));
            Assert.That(follower.Distance, Is.EqualTo(follower.TotalLength - 1f).Within(Tolerance), "LookAhead must not move the follower");
        }

        [Test]
        public void Reproject_ResyncsAfterOffset()
        {
            RouteFollower follower = FollowerFromAToC();

            float separation = follower.Reproject(new Vector3(50f, 0f, 4f));
            Assert.That(follower.Distance, Is.EqualTo(50f).Within(Tolerance));
            Assert.That(separation, Is.EqualTo(4f).Within(Tolerance));

            follower.Reproject(new Vector3(103f, 0f, 60f));   // pushed sideways off the second leg
            Assert.That(follower.Distance, Is.EqualTo(160f).Within(Tolerance));

            follower.Reproject(new Vector3(-20f, 0f, -5f));   // before the start clamps to 0
            Assert.That(follower.Distance, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Reproject_RespectsPartialFirstLeg()
        {
            // Start mid-way along A-B, heading back to A: only the 0..60 m part of that road is on the route.
            Assert.That(network.TryFindRoute(network.Project(new Vector3(60f, 0f, 1f)), settlementA, out RoadRoute route), Is.True);
            var follower = new RouteFollower(route);
            Assert.That(follower.TotalLength, Is.EqualTo(60f).Within(Tolerance));

            follower.Reproject(new Vector3(20f, 0f, 2f));
            Assert.That(follower.Distance, Is.EqualTo(40f).Within(Tolerance));

            follower.Reproject(new Vector3(90f, 0f, 0f));      // behind the start of the route
            Assert.That(follower.Distance, Is.EqualTo(0f).Within(Tolerance));
        }
    }
}
