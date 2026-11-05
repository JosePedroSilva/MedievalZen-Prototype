using System;
using System.Collections.Generic;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>The roadside inns' name boards (<see cref="PropRole.InnSign"/>, placed by <see cref="InnComposer"/>). Pure.</summary>
    public class EndlessInnSignTests
    {
        private static EndlessGenerator Gen(int seed) => new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);

        private static List<Place> Inns(EndlessGenerator g, double s0, double s1)
        {
            var places = new List<Place>();
            g.Plan.Inns(s0, s1, places);
            return places.Where(p => p.Kind == PlaceKind.Inn && p.S >= s0 && p.S <= s1).OrderBy(p => p.S).ToList();
        }

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void RoadsideInns_HaveANameBoard_OffTheVerge_FacingAlongTheRoad(int seed)
        {
            EndlessGenerator g = Gen(seed);
            float half = g.Params.roadWidth * 0.5f;
            int inns = 0, boards = 0;
            foreach (Place place in Inns(g, -100.0, 20000.0))
            {
                Dressing d = g.Dressings.Settlement(place);
                if (!d.Props.Any(p => p.role == PropRole.Tavern)) continue;   // a refuge (the stone hut) has no board
                inns++;
                List<PropPlacement> signs = d.Props.Where(p => p.role == PropRole.InnSign).ToList();
                Assert.That(signs.Count, Is.LessThanOrEqualTo(1), place.Name);
                if (signs.Count == 0) continue;
                boards++;
                PropPlacement sign = signs[0];
                Assert.That(sign.settlementId, Is.EqualTo(place.Id), $"{place.Name}: the board knows its inn (for the name).");
                // The whole frame (both posts) is off the road and its verge.
                float yaw = sign.yaw * Mathf.Deg2Rad;
                double ax = Math.Cos(yaw), az = -Math.Sin(yaw);   // local +x (across the board) in the world
                foreach (float side in new[] { -1f, 1f })
                {
                    float reach = InnSign.BoardWidth * 0.5f + 0.12f + InnSign.PostSize * 0.5f;
                    double px = d.X0 + sign.position.x + ax * side * reach, pz = d.Z0 + sign.position.z + az * side * reach;
                    g.Curve.Nearest(px, pz, out double toRoad);
                    Assert.That(toRoad, Is.GreaterThanOrEqualTo(half + SettlementKit.Verge - 0.05), $"{place.Name}: a post of the board stands on the road or verge.");
                }

                // Its faces look along the road (read by a traveller walking up from either way): local +z within 15 degrees of the road.
                g.Curve.Nearest(d.X0 + sign.position.x, d.Z0 + sign.position.z, out double near);
                Assert.That(near, Is.LessThan(half + SettlementKit.Verge + InnSign.FromVerge + 1.0), $"{place.Name}: the board stands back from the road.");
                RoadPoint rp = g.Road(place.S);
                float heading = Mathf.DeltaAngle(sign.yaw, rp.HeadingDeg);
                Assert.That(Math.Min(Math.Abs(heading), 180f - Math.Abs(heading)), Is.LessThan(25f), $"{place.Name}: the board does not face along the road.");
            }

            Assert.That(inns, Is.GreaterThan(3));
            Assert.That(boards, Is.GreaterThanOrEqualTo(inns - 1), "Most roadside inns find a spot for their board.");
        }

        [Test]
        public void NameBoards_AreDeterministic()
        {
            EndlessGenerator a = Gen(12345), b = Gen(12345);
            foreach (Place place in Inns(a, -100.0, 8000.0))
            {
                PropPlacement[] x = a.Dressings.Settlement(place).Props.Where(p => p.role == PropRole.InnSign).ToArray();
                PropPlacement[] y = b.Dressings.Settlement(place).Props.Where(p => p.role == PropRole.InnSign).ToArray();
                Assert.That(y.Length, Is.EqualTo(x.Length), place.Name);
                for (int i = 0; i < x.Length; i++)
                {
                    Assert.That(y[i].position, Is.EqualTo(x[i].position), place.Name);
                    Assert.That(y[i].yaw, Is.EqualTo(x[i].yaw), place.Name);
                }
            }
        }
    }
}
