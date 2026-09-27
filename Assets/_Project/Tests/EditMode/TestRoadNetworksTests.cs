using MedievalZen.Roads;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class TestRoadNetworksTests
    {
        private const float Tolerance = 1e-2f;

        [Test]
        public void Triangle_HasExpectedShape()
        {
            RoadNetwork network = TestRoadNetworks.Triangle();
            Assert.That(network.Settlements.Count, Is.EqualTo(3));
            Assert.That(network.Nodes.Count, Is.EqualTo(4));
            Assert.That(network.Segments.Count, Is.EqualTo(4));
            Assert.That(network.Nodes[3].Settlement, Is.Null);
            Assert.That(network.Nodes[3].IsJunction, Is.True);

            for (int i = 0; i < network.Settlements.Count; i++)
            {
                Vector3 p = network.Settlements[i].Position;
                Assert.That(Mathf.Abs(p.x) < 300f && Mathf.Abs(p.z) < 300f, $"{network.Settlements[i]} is outside the 600 m test ground");
                for (int j = i + 1; j < network.Settlements.Count; j++)
                {
                    float d = Vector3.Distance(p, network.Settlements[j].Position);
                    Assert.That(d, Is.InRange(200f, 350f), $"{network.Settlements[i]} to {network.Settlements[j]}");
                }
            }

            foreach (RoadSegment segment in network.Segments)
            {
                Assert.That(Vector3.Distance(segment.Points[0], segment.A.Position), Is.LessThan(Tolerance));
                Assert.That(Vector3.Distance(segment.Points[segment.Points.Count - 1], segment.B.Position), Is.LessThan(Tolerance));
                for (int i = 1; i < segment.Points.Count; i++)
                {
                    Assert.That(Vector3.Distance(segment.Points[i - 1], segment.Points[i]), Is.LessThanOrEqualTo(2f + Tolerance));
                }
            }
        }

        [Test]
        public void Triangle_RoutesBetweenEveryPairOfSettlements()
        {
            RoadNetwork network = TestRoadNetworks.Triangle();
            foreach (Settlement from in network.Settlements)
            {
                RoadLocation start = network.Project(from.Position);
                Assert.That(start.IsValid, Is.True);
                Assert.That(Vector3.Distance(start.Position, from.Position), Is.LessThan(Tolerance));

                foreach (Settlement to in network.Settlements)
                {
                    if (to == from) continue;
                    Assert.That(network.TryFindRoute(start, to, out RoadRoute route), Is.True, $"{from.Name} -> {to.Name}");
                    Assert.That(route.Destination, Is.SameAs(to));
                    Assert.That(route.TotalLength, Is.GreaterThan(Vector3.Distance(from.Position, to.Position) - Tolerance));
                    Assert.That(route.TotalLength, Is.LessThan(2f * Vector3.Distance(from.Position, to.Position)), "route should not wander");
                    Assert.That(Vector3.Distance(route.Evaluate(route.TotalLength), to.Position), Is.LessThan(Tolerance));
                    Assert.That(Vector3.Distance(route.Evaluate(0f), from.Position), Is.LessThan(Tolerance));
                }
            }
        }

        [Test]
        public void Triangle_RoutesFromMidRoadPositions()
        {
            RoadNetwork network = TestRoadNetworks.Triangle();
            foreach (RoadSegment segment in network.Segments)
            {
                Vector3 offRoad = segment.Evaluate(segment.Length * 0.4f) + new Vector3(3f, 0f, 3f);
                RoadLocation start = network.Project(offRoad);
                foreach (Settlement to in network.Settlements)
                {
                    Assert.That(network.TryFindRoute(start, to, out RoadRoute route), Is.True, $"{segment} -> {to.Name}");
                    Assert.That(Vector3.Distance(route.Evaluate(route.TotalLength), to.Position), Is.LessThan(Tolerance));
                }
            }
        }
    }
}
