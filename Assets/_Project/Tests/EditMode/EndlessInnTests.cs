using System;
using System.Collections.Generic;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.People;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>The endless road's inn archetypes (<see cref="InnStyles"/>, <see cref="InnComposer"/>): variety, rules and costs. Pure.</summary>
    public class EndlessInnTests
    {
        private static EndlessGenerator Gen(int seed) => new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);

        private static List<Place> Inns(EndlessGenerator g, double s0, double s1)
        {
            var list = new List<Place>();
            g.Plan.Inns(s0, s1, list);
            return list;
        }

        private static double RoadDistance(EndlessGenerator g, double x, double z)
        {
            g.Curve.Nearest(x, z, out double d);
            return d;
        }

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void Archetypes_NeverRepeat_AndVary(int seed)
        {
            EndlessGenerator g = Gen(seed);
            List<Place> inns = Inns(g, -100.0, 30000.0);
            var styles = inns.Select(p => g.Dressings.Settlement(p).InnStyle).ToList();
            Assert.That(styles.All(s => s >= 0), Is.True, "every inn is composed by archetype");
            for (int i = 1; i < styles.Count; i++)
            {
                Assert.That(styles[i], Is.Not.EqualTo(styles[i - 1]), $"{inns[i].Name} repeats {inns[i - 1].Name}'s archetype");
            }

            Assert.That(styles.Distinct().Count(), Is.GreaterThanOrEqualTo(5), "at least five archetypes in 30 km");
        }

        [Test]
        public void EveryLandArchetype_Occurs()
        {
            var seen = new HashSet<int>();
            foreach (int seed in new[] { 12345, 42, 777 })
            {
                EndlessGenerator g = Gen(seed);
                foreach (Place inn in Inns(g, -100.0, 30000.0)) seen.Add(g.Dressings.Settlement(inn).InnStyle);
            }

            foreach (InnArchetype a in new[] { InnArchetype.Coaching, InnArchetype.Alehouse, InnArchetype.Farm, InnArchetype.ForestLodge, InnArchetype.ForgeInn, InnArchetype.Refuge })
            {
                Assert.That(seen.Contains((int)a), Is.True, a.ToString());
            }
        }

        [Test]
        public void Archetype_DoesNotDependOnTileOrder()
        {
            EndlessGenerator a = Gen(42), b = Gen(42);
            List<Place> inns = Inns(a, -100.0, 20000.0);
            var forward = inns.Select(p => InnStyles.For(a.Params, a.Plan, a.Curve, a.Terrain, p).Archetype).ToList();
            var backward = Enumerable.Reverse(Inns(b, -100.0, 20000.0)).Select(p => InnStyles.For(b.Params, b.Plan, b.Curve, b.Terrain, p).Archetype).Reverse().ToList();
            Assert.That(backward, Is.EqualTo(forward));
            Dressing d1 = a.Dressings.Settlement(inns[3]), d2 = Gen(42).Dressings.Settlement(inns[3]);
            Assert.That(d2.Props.Count, Is.EqualTo(d1.Props.Count));
            for (int k = 0; k < d1.Props.Count; k++) Assert.That(d2.Props[k].position, Is.EqualTo(d1.Props[k].position));
        }

        [TestCase(12345)]
        [TestCase(777)]
        public void Inn_HasOneRestBuilding_DoorBoardAndCastOffTheRoad(int seed)
        {
            EndlessGenerator g = Gen(seed);
            float half = g.Params.roadWidth * 0.5f;
            foreach (Place place in Inns(g, -100.0, 30000.0))
            {
                Dressing d = g.Dressings.Settlement(place);
                List<PropPlacement> rest = d.Props.Where(p => InnComposer.IsRestBuilding(p.role)).ToList();
                Assert.That(rest.Count, Is.EqualTo(1), $"{place.Name}: exactly one building takes the rest point");
                BuildingShape shape = BuildingTable.For(rest[0].role, rest[0].variant);
                Vector3 door = BuildingTable.ToWorld(rest[0].position, rest[0].yaw, shape.Door);
                PlaceAnchor innDoor = d.Anchors.Single(a => a.Kind == AnchorKind.InnDoor);
                Assert.That(Vector3.Distance(new Vector3(innDoor.Position.x, 0f, innDoor.Position.z), new Vector3(door.x, 0f, door.z)), Is.LessThan(0.01f), place.Name);
                double doorGap = RoadDistance(g, d.X0 + door.x, d.Z0 + door.z) - half;
                Assert.That(doorGap, Is.InRange(SettlementKit.Verge, 34.0), $"{place.Name}: the door is reachable from the road");

                List<PropPlacement> boards = d.Props.Where(p => p.role == PropRole.InnSign).ToList();
                Assert.That(boards.Count, Is.EqualTo(place.Refuge ? 0 : 1), $"{place.Name}: one name board (the refuge has its old sign)");
                if (!place.Refuge)
                {
                    double boardGap = RoadDistance(g, d.X0 + boards[0].position.x, d.Z0 + boards[0].position.z) - half - SettlementKit.Verge;
                    Assert.That(boardGap, Is.GreaterThanOrEqualTo(InnSign.FootprintRadius - 0.05), $"{place.Name}: the board keeps off the verge");
                    Assert.That(Math.Abs(boards[0].position.x + d.X0 - d.CentreX) + Math.Abs(boards[0].position.z + d.Z0 - d.CentreZ), Is.LessThan(40.0), place.Name);
                }

                foreach (PlaceAnchor a in d.Anchors)
                {
                    Assert.That(RoadDistance(g, d.X0 + a.Position.x, d.Z0 + a.Position.z), Is.GreaterThanOrEqualTo(half + SettlementKit.Verge - 0.05), $"{place.Name}: {a.Kind} anchor on the verge");
                }

                foreach (PropPlacement prop in d.Props)
                {
                    BuildingShape s = PropRole.IsBuilding(prop.role) ? BuildingTable.For(prop.role, prop.variant) : null;
                    if (s == null)
                    {
                        Assert.That(RoadDistance(g, d.X0 + prop.position.x, d.Z0 + prop.position.z), Is.GreaterThanOrEqualTo(half + SettlementKit.Verge + 0.15), $"{place.Name}: {prop.role} on the verge");
                        continue;
                    }

                    foreach (Vector3 corner in s.ExtentOutline())
                    {
                        Vector3 w = BuildingTable.ToWorld(prop.position, prop.yaw, corner);
                        Assert.That(RoadDistance(g, d.X0 + w.x, d.Z0 + w.z), Is.GreaterThanOrEqualTo(half + SettlementKit.Verge - 0.05), $"{place.Name}: {prop.role} reaches the verge");
                        double cx = d.X0 + w.x - d.CentreX, cz = d.Z0 + w.z - d.CentreZ;
                        Assert.That(Math.Sqrt(cx * cx + cz * cz), Is.LessThan(48.0), $"{place.Name}: {prop.role} beyond the levelled ground");
                    }
                }
            }
        }

        [TestCase(12345)]
        [TestCase(42)]
        public void Inns_StayWithinTheirCost(int seed)
        {
            EndlessGenerator g = Gen(seed);
            var counts = new List<int>();
            foreach (Place place in Inns(g, -100.0, 30000.0))
            {
                Dressing d = g.Dressings.Settlement(place);
                int objects = d.Props.Count(p => !PropRole.IsInstancedPlant(p.role) && !PropRole.IsTree(p.role));
                int buildings = d.Props.Count(p => PropRole.IsBuilding(p.role));
                Assert.That(objects, Is.LessThanOrEqualTo(48), $"{place.Name}: objects");
                Assert.That(buildings, Is.InRange(1, 5), $"{place.Name}: buildings");
                long triangles = d.Props.Where(p => PropRole.IsBuilding(p.role)).Sum(p => (long)BuildingTable.For(p.role, p.variant).Triangles);
                Assert.That(triangles, Is.LessThanOrEqualTo(95000), $"{place.Name}: building triangles");
                counts.Add(objects);
            }

            Assert.That(counts.Average(), Is.LessThanOrEqualTo(36.0));
        }
    }
}
