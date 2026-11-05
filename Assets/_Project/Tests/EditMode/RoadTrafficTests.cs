using System;
using System.Collections.Generic;
using System.Linq;
using MedievalZen.Endless.Generation;
using MedievalZen.Traffic;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>The road traffic plan (pure): determinism, continuity, manners, caps, the night, bridges.</summary>
    public class RoadTrafficTests
    {
        private const double Hps = 1.0 / 60.0;   // the 24-minute day

        /// <summary>A road with a settlement every 1.8 km (villages and inns), farmland but for a pass between 20 and 24 km.</summary>
        private sealed class FakeRoad : ITrafficRoad
        {
            public int Calls;

            public bool Places(double sFrom, double sTo, List<TrafficPlace> results)
            {
                Calls++;
                for (long k = (long)Math.Ceiling(sFrom / 1800.0); k * 1800.0 <= sTo; k++)
                {
                    results.Add(new TrafficPlace { Id = (int)k, S = k * 1800.0, Radius = k % 3 == 0 ? 90f : 40f, Village = k % 3 == 0, Inn = k % 3 == 1 });
                }

                return true;
            }

            public float Density(double s) => s > 20000 && s < 24000 ? 0.1f : 1f;
        }

        private static TrafficQuery NewQuery(int seed = 12345) => new TrafficQuery { Seed = seed, HoursPerSecond = Hps, Road = new FakeRoad() };

        private static double TauAt(int day, float hour) => (day * 24.0 + hour) / Hps;

        private static List<RoadUser> Users(TrafficQuery q, double tau, double a, double b)
        {
            var list = new List<RoadUser>();
            Assert.IsTrue(RoadTrafficPlan.Query(q, tau, a, b, list));
            return list.OrderBy(u => u.Id).ToList();
        }

        [Test]
        public void PlanIsDeterministicAndOrderIndependent()
        {
            double tau = TauAt(3, 10.5f);
            TrafficQuery q1 = NewQuery(), q2 = NewQuery();
            Users(q2, TauAt(7, 14f), 30000, 31000);   // a different question first
            List<RoadUser> a = Users(q1, tau, 5000, 6000), b = Users(q2, tau, 5000, 6000);
            Assert.Greater(a.Count, 0, "a day road has people on it");
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);

            // A wide window holds exactly the users of its two halves.
            List<RoadUser> whole = Users(q1, tau, 4000, 8000);
            List<RoadUser> halves = Users(q1, tau, 4000, 6000).Concat(Users(q1, tau, 6000, 8000)).GroupBy(u => u.Id).Select(x => x.First()).OrderBy(u => u.Id).ToList();
            CollectionAssert.AreEqual(whole.Select(u => u.Id), halves.Select(u => u.Id));
        }

        [Test]
        public void PositionsAreContinuousAndRightAfterAJump()
        {
            TrafficQuery q = NewQuery();
            double tau = TauAt(2, 11f);
            List<RoadUser> users = Users(q, tau, 0, 20000);
            Assert.Greater(users.Count, 10);
            foreach (RoadUser u in users)
            {
                for (double t = u.Appear; t < u.Gone; t += 0.5)
                {
                    u.Sample(t, out double s0, out double l0);
                    u.Sample(t + 0.05, out double s1, out double l1);
                    Assert.LessOrEqual(Math.Abs(s1 - s0) + Math.Abs(l1 - l0), u.Speed * 0.05 * 1.01 + 1e-6, $"user {u.Id} jumps at {t}");
                }

                // Asked again after a jump in time (a fresh query, as after a teleport) the user is where its formula says.
                double later = Math.Min(u.Gone - 1.0, tau + 30.0);
                u.Sample(later, out double sl, out _);
                if (later <= tau) continue;
                List<RoadUser> again = Users(NewQuery(), later, sl - 1.0, sl + 1.0);
                Assert.IsTrue(again.Any(x => x.Id == u.Id), $"user {u.Id} missing after a jump");
            }
        }

        private static void Walk(TrafficQuery q, double tau0, double sStart, float travellerLateral, double travellerSpeed, double seconds, Action<TrafficBrain, double, double, bool> check)
        {
            var brains = new Dictionary<long, TrafficBrain>();
            var list = new List<RoadUser>();
            for (double t = 0; t < seconds; t += 0.1)
            {
                double tau = tau0 + t, ts = sStart + travellerSpeed * t;
                list.Clear();
                RoadTrafficPlan.Query(q, tau, ts - 60, ts + 60, list);
                foreach (RoadUser u in list)
                {
                    if (!brains.TryGetValue(u.Id, out TrafficBrain b))
                    {
                        b = new TrafficBrain();
                        b.Reset(u, tau);
                        brains[u.Id] = b;
                    }

                    bool bridge = Math.Abs((b.S % 1000.0) - 500.0) < 14.0;   // pretend a bridge every km
                    b.Step(0.1f, tau, ts, travellerLateral, bridge, (float)Math.Abs(b.S - ts));
                    check(b, ts, travellerLateral, bridge);
                }
            }
        }

        [Test]
        public void NobodyNearTheCentreLineAndAutoTravelStaysClear()
        {
            int checkedNear = 0;
            Walk(NewQuery(), TauAt(1, 9f), 1000, 0f, 1.4, 900, (b, ts, tl, bridge) =>
            {
                if (b.Phase == TrafficPhase.Absent) return;
                float inner = b.Lateral - RoadTrafficPlan.HalfWidth(b.User.Kind);
                bool cart = b.User.Kind == RoadUserKind.Cart;
                float least = bridge && cart ? 0.6f : 1.6f;
                Assert.GreaterOrEqual(inner, least - 1e-3f, $"{b.User.Kind} {b.User.Id} at {b.Lateral:0.00} m (bridge {bridge})");
                if (Math.Abs(b.S - ts) < 12) checkedNear++;
            });
            Assert.Greater(checkedNear, 50, "the walk met people");
        }

        [TestCase(1.9f)]
        [TestCase(2.3f)]
        [TestCase(-2.2f)]
        [TestCase(3.4f)]
        public void NobodyWalksThroughAStandingTraveller(float travellerLateral)
        {
            int close = 0;
            foreach (double at in new[] { 700.0, 2500.0, 4300.0, 6100.0, 7900.0, 9700.0 })
            Walk(NewQuery(), TauAt(1, 8.5f), at, travellerLateral, 0.0, 600, (b, ts, tl, bridge) =>
            {
                if (b.Phase != TrafficPhase.Road) return;
                float half = RoadTrafficPlan.HalfWidth(b.User.Kind);
                float length = b.User.Kind == RoadUserKind.Cart ? 4.5f : b.User.Kind == RoadUserKind.Rider ? 1.4f : 0.4f;
                if (Math.Abs(b.S - ts) > length) return;
                close++;
                float userLat = (float)tl * b.User.Dir;   // the traveller in the user's frame
                float span = b.User.Kind == RoadUserKind.Pair && !bridge ? RoadTrafficPlan.PairSpacing : 0f;
                float gap = Math.Max(b.Lateral - userLat, userLat - (b.Lateral + span));
                Assert.GreaterOrEqual(gap, TrafficBrain.TravellerRadius + half - 1e-3f, $"{b.User.Kind} passes through the traveller");
            });
            Assert.Greater(close, 0, "someone passed the traveller");
        }

        [Test]
        public void CapsHold()
        {
            var rng = new Random(5);
            var cands = new List<TrafficCandidate>();
            var chosen = new List<int>();
            for (int round = 0; round < 200; round++)
            {
                cands.Clear();
                int n = rng.Next(0, 30);
                for (int i = 0; i < n; i++)
                {
                    var kind = (RoadUserKind)rng.Next(0, 6);
                    cands.Add(new TrafficCandidate { User = new RoadUser { Id = i, Kind = kind }, Distance = (float)(rng.NextDouble() * 320), Shown = rng.Next(2) == 0, MaySpawn = rng.Next(4) > 0 });
                }

                TrafficSelector.Select(cands, chosen);
                int walkers = chosen.Where(i => !cands[i].User.IsVehicle).Sum(i => cands[i].User.Members);
                int vehicles = chosen.Count(i => cands[i].User.IsVehicle);
                Assert.LessOrEqual(walkers, TrafficSelector.MaxWalkers);
                Assert.LessOrEqual(vehicles, TrafficSelector.MaxVehicles);
                foreach (int i in chosen)
                {
                    TrafficCandidate c = cands[i];
                    Assert.IsTrue(c.Shown ? c.Distance <= TrafficSelector.HideBeyond : c.MaySpawn && c.Distance <= TrafficSelector.ShowWithin);
                }
            }
        }

        [Test]
        public void TheNightRoadIsEmptyButForRareLanterns()
        {
            TrafficQuery q = NewQuery();
            int others = 0, lanterns = 0, samples = 0;
            for (int day = 1; day <= 6; day++)
            {
                for (float hour = 21.5f; hour < 29f; hour += 0.5f)
                {
                    samples++;
                    foreach (RoadUser u in Users(q, TauAt(day, hour), 0, 20000))
                    {
                        if (u.Kind == RoadUserKind.Lantern) lanterns++;
                        else others++;
                    }
                }
            }

            Assert.AreEqual(0, others, "day journeys are over by dusk");
            Assert.Less(lanterns / (double)samples, 1.0, "fewer than one lantern on 20 km of night road");
            int day1 = Users(q, TauAt(1, 11f), 0, 20000).Count;
            Assert.Greater(day1, 5);
        }

        [Test]
        public void HeightsOnABridgeFollowTheDeck()
        {
            var bridges = new List<TrafficBridge> { new TrafficBridge { S = 1000.0, RoadHeight = 50f } };
            var deck = TileStreams.DeckSurface(new TileStreams.BridgeDeck { X = 0, Z = 1000.0, DirX = 0f, DirZ = 1f, PivotY = BridgeShape.PivotY(50f) }, out int sections);
            for (int k = 0; k < sections; k++)
            {
                var centre = deck[k * 5 + 2];
                double s = centre.z;
                Assert.AreEqual(centre.y, TrafficGround.Height(bridges, s, 40f), 1e-3f, $"deck at {s - 1000.0:0.0} m");
            }

            Assert.Greater(TrafficGround.Height(bridges, 1000.0, 40f), 51f, "the crown stands above the road");
            Assert.AreEqual(40f, TrafficGround.Height(bridges, 1000.0 + BridgeShape.HalfLength + 0.5, 40f), "off the bridge: the ground");
            Assert.IsTrue(float.IsNaN(TrafficGround.DeckHeight(bridges, 900.0)));
        }

        [Test]
        public void RealRoadHasPeopleByDay()
        {
            var g = new EndlessGenerator(new MedievalZen.Endless.EndlessParams { seed = 12345 }, MedievalZen.World.PaletteKeys.Default);
            var q = new TrafficQuery { Seed = 12345, HoursPerSecond = Hps, Road = new TrafficSource(g) { Warm = true } };
            var a = new List<RoadUser>();
            Assert.IsTrue(RoadTrafficPlan.Query(q, TauAt(1, 12f), 0, 6000, a));
            Assert.Greater(a.Count, 2);
            var cold = new TrafficQuery { Seed = 12345, HoursPerSecond = Hps, Road = new TrafficSource(new EndlessGenerator(new MedievalZen.Endless.EndlessParams { seed = 12345 }, MedievalZen.World.PaletteKeys.Default)) };
            Assert.IsFalse(RoadTrafficPlan.Query(cold, TauAt(1, 12f), 0, 6000, new List<RoadUser>()), "nothing is planned from regions that are not warmed");
        }
    }
}
