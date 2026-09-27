using MedievalZen.Roads;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class RoadNetworkTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 PosA = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 PosB = new Vector3(200f, 0f, 0f);
        private static readonly Vector3 PosC = new Vector3(100f, 0f, 150f);
        private static readonly Vector3 PosJ = new Vector3(100f, 0f, 50f);

        private RoadNetwork network;
        private Settlement townA;
        private Settlement villageB;
        private Settlement innC;
        private RoadSegment roadAB;
        private RoadSegment roadAJ;
        private RoadSegment roadJB;
        private RoadSegment roadJC;
        private RoadSegment roadBC;

        // Triangle A-B-C with a junction J inside:
        //   A-B 200 m, B-C ~180.3 m, A-J ~111.8 m, J-B ~111.8 m, J-C 100 m.
        [SetUp]
        public void SetUp()
        {
            var builder = new RoadNetworkBuilder();
            townA = builder.AddSettlement("Asheford", SettlementType.Town, PosA, 40f);
            villageB = builder.AddSettlement("Brambleby", SettlementType.Village, PosB, 25f);
            innC = builder.AddSettlement("The Crooked Pike", SettlementType.Inn, PosC, 10f);
            RoadNode junction = builder.AddNode(PosJ);

            roadAB = builder.AddStraightSegment(townA.Node, villageB.Node);
            roadAJ = builder.AddStraightSegment(townA.Node, junction);
            roadJB = builder.AddStraightSegment(junction, villageB.Node);
            roadJC = builder.AddStraightSegment(junction, innC.Node);
            roadBC = builder.AddSegment(villageB.Node, innC.Node, new[] { PosB, (PosB + PosC) * 0.5f, PosC });
            network = builder.Build();
        }

        [Test]
        public void Builder_AssignsIdsAndAdjacency()
        {
            Assert.That(network.Settlements.Count, Is.EqualTo(3));
            Assert.That(network.Nodes.Count, Is.EqualTo(4));
            Assert.That(network.Segments.Count, Is.EqualTo(5));
            Assert.That(network.Nodes[3].Settlement, Is.Null);
            Assert.That(network.Nodes[3].Segments.Count, Is.EqualTo(3));
            Assert.That(network.Nodes[3].IsJunction, Is.True);
            Assert.That(townA.Node.Settlement, Is.SameAs(townA));
            Assert.That(roadAB.Length, Is.EqualTo(200f).Within(Tolerance));

            for (int i = 1; i < roadAB.Points.Count; i++)
            {
                Assert.That(Vector3.Distance(roadAB.Points[i - 1], roadAB.Points[i]), Is.LessThanOrEqualTo(2f + Tolerance));
            }
        }

        [Test]
        public void Project_PicksNearestSegment()
        {
            RoadLocation onAB = network.Project(new Vector3(30f, 2f, -10f));
            Assert.That(onAB.Segment, Is.SameAs(roadAB));
            Assert.That(onAB.Distance, Is.EqualTo(30f).Within(Tolerance));

            RoadLocation onJC = network.Project(new Vector3(103f, 0f, 120f));
            Assert.That(onJC.Segment, Is.SameAs(roadJC));
            Assert.That(onJC.Distance, Is.EqualTo(70f).Within(Tolerance));
            Assert.That(Vector3.Distance(onJC.Position, new Vector3(100f, 0f, 120f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void Nearest_ReturnsClosestSettlement()
        {
            Assert.That(network.Nearest(new Vector3(180f, 0f, 20f)), Is.SameAs(villageB));
            Assert.That(network.Nearest(new Vector3(90f, 0f, 140f)), Is.SameAs(innC));
        }

        [Test]
        public void TryFindRoute_TakesShorterPathWithPartialFirstLeg()
        {
            // 20 m from A on A-B, heading for C:
            //   back to A (20) + A-J + J-C  = 20 + 111.80 + 100     = 231.80   <- shortest
            //   on to B (180) + B-C          = 180 + 180.28          = 360.28
            var start = new RoadLocation(roadAB, 20f);

            bool found = network.TryFindRoute(start, innC, out RoadRoute route);

            Assert.That(found, Is.True);
            Assert.That(route.Destination, Is.SameAs(innC));
            float expected = 20f + Vector3.Distance(PosA, PosJ) + Vector3.Distance(PosJ, PosC);
            Assert.That(route.TotalLength, Is.EqualTo(expected).Within(0.01f));
            Assert.That(route.StartDistanceOnFirstLeg, Is.EqualTo(20f).Within(Tolerance));

            Assert.That(route.Legs.Count, Is.EqualTo(3));
            Assert.That(route.Legs[0].segment, Is.SameAs(roadAB));
            Assert.That(route.Legs[0].forward, Is.False);
            Assert.That(route.Legs[1].segment, Is.SameAs(roadAJ));
            Assert.That(route.Legs[1].forward, Is.True);
            Assert.That(route.Legs[2].segment, Is.SameAs(roadJC));
            Assert.That(route.Legs[2].forward, Is.True);

            AssertVector(new Vector3(20f, 0f, 0f), route.Evaluate(0f));
            AssertVector(Vector3.left, route.Tangent(0f));
            AssertVector(PosA, route.Evaluate(20f));
            AssertVector(PosJ, route.Evaluate(20f + Vector3.Distance(PosA, PosJ)));
            AssertVector(Vector3.forward, route.Tangent(route.TotalLength - 1f));
            AssertVector(PosC, route.Evaluate(route.TotalLength));
            AssertVector(PosC, route.Evaluate(route.TotalLength + 50f));
        }

        [Test]
        public void TryFindRoute_TraversesSegmentsBackwards()
        {
            // From the middle of J-C to B: back to J (50) + J-B (111.8) beats on to C (50) + C-B (180.3).
            var start = new RoadLocation(roadJC, 50f);

            Assert.That(network.TryFindRoute(start, villageB, out RoadRoute route), Is.True);

            Assert.That(route.Legs.Count, Is.EqualTo(2));
            Assert.That(route.Legs[0].segment, Is.SameAs(roadJC));
            Assert.That(route.Legs[0].forward, Is.False);
            Assert.That(route.Legs[1].segment, Is.SameAs(roadJB));
            Assert.That(route.TotalLength, Is.EqualTo(50f + Vector3.Distance(PosJ, PosB)).Within(0.01f));
            AssertVector(PosB, route.Evaluate(route.TotalLength));
        }

        [Test]
        public void TryFindRoute_DestinationAtEndOfStartSegment_IsSinglePartialLeg()
        {
            var start = new RoadLocation(roadBC, 30f);

            Assert.That(network.TryFindRoute(start, innC, out RoadRoute route), Is.True);

            Assert.That(route.Legs.Count, Is.EqualTo(1));
            Assert.That(route.Legs[0].forward, Is.True);
            Assert.That(route.TotalLength, Is.EqualTo(roadBC.Length - 30f).Within(0.01f));
            AssertVector(PosC, route.Evaluate(route.TotalLength));
        }

        [Test]
        public void TryFindRoute_FailsWhenUnreachable()
        {
            var builder = new RoadNetworkBuilder();
            Settlement a = builder.AddSettlement("A", SettlementType.Town, Vector3.zero, 10f);
            Settlement b = builder.AddSettlement("B", SettlementType.Village, new Vector3(50f, 0f, 0f), 10f);
            Settlement island = builder.AddSettlement("Island", SettlementType.Inn, new Vector3(0f, 0f, 500f), 10f);
            builder.AddStraightSegment(a.Node, b.Node);
            RoadNetwork disconnected = builder.Build();

            Assert.That(disconnected.TryFindRoute(disconnected.Project(Vector3.zero), island, out RoadRoute route), Is.False);
            Assert.That(route, Is.Null);
            Assert.That(disconnected.TryFindRoute(default, b, out _), Is.False);
        }

        [Test]
        public void RoadNetworkData_RoundTrips()
        {
            RoadNetwork copy = RoadNetworkData.From(network).ToNetwork();
            AssertSameNetwork(network, copy);
        }

        [Test]
        public void RoadNetworkData_RoundTripsThroughJsonUtility()
        {
            string json = JsonUtility.ToJson(RoadNetworkData.From(network));
            RoadNetwork copy = JsonUtility.FromJson<RoadNetworkData>(json).ToNetwork();
            AssertSameNetwork(network, copy);
        }

        private static void AssertSameNetwork(RoadNetwork expected, RoadNetwork actual)
        {
            Assert.That(actual.Settlements.Count, Is.EqualTo(expected.Settlements.Count));
            Assert.That(actual.Nodes.Count, Is.EqualTo(expected.Nodes.Count));
            Assert.That(actual.Segments.Count, Is.EqualTo(expected.Segments.Count));

            for (int i = 0; i < expected.Settlements.Count; i++)
            {
                Settlement e = expected.Settlements[i];
                Settlement a = actual.Settlements[i];
                Assert.That(a.Id, Is.EqualTo(e.Id));
                Assert.That(a.Name, Is.EqualTo(e.Name));
                Assert.That(a.Type, Is.EqualTo(e.Type));
                Assert.That(a.Radius, Is.EqualTo(e.Radius));
                Assert.That(a.Node.Id, Is.EqualTo(e.Node.Id));
                AssertVector(e.Position, a.Position);
            }

            for (int i = 0; i < expected.Nodes.Count; i++)
            {
                AssertVector(expected.Nodes[i].Position, actual.Nodes[i].Position);
                Assert.That(actual.Nodes[i].Segments.Count, Is.EqualTo(expected.Nodes[i].Segments.Count));
            }

            for (int i = 0; i < expected.Segments.Count; i++)
            {
                RoadSegment e = expected.Segments[i];
                RoadSegment a = actual.Segments[i];
                Assert.That(a.A.Id, Is.EqualTo(e.A.Id));
                Assert.That(a.B.Id, Is.EqualTo(e.B.Id));
                Assert.That(a.Length, Is.EqualTo(e.Length).Within(Tolerance));
                Assert.That(a.Points.Count, Is.EqualTo(e.Points.Count));
                for (int p = 0; p < e.Points.Count; p++)
                {
                    AssertVector(e.Points[p], a.Points[p]);
                }
            }
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance), $"expected {expected} but was {actual}");
        }
    }
}
