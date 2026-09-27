using System.Collections.Generic;
using MedievalZen.Roads;
using MedievalZen.Travel;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class SignpostChoicesTests
    {
        // Star junction J at the origin with three arms: east to a village (100 m) that continues to a town (another
        // 200 m), north to an inn (60 m, segment stored towards the junction), west to a village (150 m).
        private RoadNetwork network;
        private RoadNode junction;
        private Settlement eastVillage;
        private Settlement farTown;
        private Settlement northInn;
        private Settlement westVillage;

        [SetUp]
        public void SetUp()
        {
            var builder = new RoadNetworkBuilder();
            eastVillage = builder.AddSettlement("Eastby", SettlementType.Village, new Vector3(100f, 0f, 0f), 10f);
            farTown = builder.AddSettlement("Farhold", SettlementType.Town, new Vector3(300f, 0f, 0f), 20f);
            northInn = builder.AddSettlement("The Nook", SettlementType.Inn, new Vector3(0f, 0f, 60f), 5f);
            westVillage = builder.AddSettlement("Westmere", SettlementType.Village, new Vector3(-150f, 0f, 0f), 10f);
            junction = builder.AddNode(Vector3.zero);
            builder.AddStraightSegment(junction, eastVillage.Node);
            builder.AddStraightSegment(eastVillage.Node, farTown.Node);
            builder.AddStraightSegment(northInn.Node, junction);
            builder.AddStraightSegment(junction, westVillage.Node);
            network = builder.Build();
        }

        [Test]
        public void ListsEveryReachableSettlementNearestFirst()
        {
            List<SignpostChoice> choices = SignpostChoices.Build(network, junction);
            Assert.That(choices.Count, Is.EqualTo(4));
            for (int i = 1; i < choices.Count; i++)
            {
                Assert.That(choices[i].Distance, Is.GreaterThanOrEqualTo(choices[i - 1].Distance));
            }

            Assert.That(choices[0].Settlement, Is.SameAs(northInn));
            Assert.That(choices[1].Settlement, Is.SameAs(eastVillage));
            Assert.That(choices[2].Settlement, Is.SameAs(westVillage));
            Assert.That(choices[3].Settlement, Is.SameAs(farTown));
        }

        [Test]
        public void DistancesAreRoadDistancesFromTheJunction()
        {
            List<SignpostChoice> choices = SignpostChoices.Build(network, junction);
            Assert.That(choices[0].Distance, Is.EqualTo(60f).Within(0.5f));
            Assert.That(choices[1].Distance, Is.EqualTo(100f).Within(0.5f));
            Assert.That(choices[2].Distance, Is.EqualTo(150f).Within(0.5f));
            Assert.That(choices[3].Distance, Is.EqualTo(300f).Within(0.5f));
        }

        [Test]
        public void SettlementsBeyondOneAnotherShareTheirArm()
        {
            List<SignpostChoice> choices = SignpostChoices.Build(network, junction);
            SignpostChoice east = choices.Find(c => c.Settlement == eastVillage);
            SignpostChoice far = choices.Find(c => c.Settlement == farTown);
            SignpostChoice west = choices.Find(c => c.Settlement == westVillage);
            Assert.That(far.Arm, Is.SameAs(east.Arm));
            Assert.That(west.Arm, Is.Not.SameAs(east.Arm));
            Assert.That(SignpostChoices.Compass(east.Direction), Is.EqualTo("east"));
            Assert.That(SignpostChoices.Compass(west.Direction), Is.EqualTo("west"));
            Assert.That(SignpostChoices.Compass(choices[0].Direction), Is.EqualTo("north"));
        }

        [Test]
        public void EqualDistancesAreOrderedByName()
        {
            var a = new SignpostChoice(northInn, 100f, null, Vector3.forward);
            var b = new SignpostChoice(eastVillage, 100f, null, Vector3.forward);
            var list = new List<SignpostChoice> { a, b };
            list.Sort(SignpostChoices.CompareByDistance);
            Assert.That(list[0].Settlement, Is.SameAs(eastVillage));
        }

        [Test]
        public void TriangleJunctionOffersAllThreeSettlements()
        {
            RoadNetwork triangle = TestRoadNetworks.Triangle();
            List<SignpostChoice> choices = SignpostChoices.Build(triangle, triangle.Nodes[3]);
            Assert.That(choices.Count, Is.EqualTo(3));
            for (int i = 1; i < choices.Count; i++)
            {
                Assert.That(choices[i].Distance, Is.GreaterThanOrEqualTo(choices[i - 1].Distance));
            }

            var arms = new HashSet<RoadSegment>();
            foreach (SignpostChoice choice in choices) arms.Add(choice.Arm);
            Assert.That(arms.Count, Is.EqualTo(3), "each settlement lies along its own road");
        }

        [Test]
        public void CompassCoversEightPoints()
        {
            Assert.That(SignpostChoices.Compass(new Vector3(1f, 0f, 1f)), Is.EqualTo("north-east"));
            Assert.That(SignpostChoices.Compass(new Vector3(0f, 0f, -1f)), Is.EqualTo("south"));
            Assert.That(SignpostChoices.Compass(new Vector3(-1f, 0f, -1f)), Is.EqualTo("south-west"));
        }
    }
}
