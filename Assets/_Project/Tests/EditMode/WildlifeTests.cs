using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Wildlife;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;
using Pose = MedievalZen.Wildlife.Pose;

namespace MedievalZen.Tests
{
    /// <summary>A made-up landscape for the planner: the road on x = 0, bands of regions along z, a lake, a steep strip, places and fields.</summary>
    internal sealed class FakeHabitat : IWildlifeHabitat
    {
        public const double LakeX = 160, LakeZ = 520, LakeR = 45;

        public int Seed { get; set; } = 7;
        public bool ThreadSafe => true;
        public float Corridor => 12f;

        public readonly List<HabitatSettlement> Places = new List<HabitatSettlement>
        {
            new HabitatSettlement { Id = 11, Kind = HabitatSettlementKind.Inn, X = 0, Z = 300, Radius = 30f, RoadHeadingDeg = 0f },
            new HabitatSettlement { Id = 22, Kind = HabitatSettlementKind.Hamlet, X = 0, Z = 2300, Radius = 40f, RoadHeadingDeg = 0f },
            new HabitatSettlement { Id = 33, Kind = HabitatSettlementKind.Village, X = 0, Z = 2700, Radius = 60f, RoadHeadingDeg = 0f },
        };

        public static RegionKind RegionAt(double z) =>
            z < 1000 ? RegionKind.Meadow : z < 2000 ? RegionKind.Forest : z < 3000 ? RegionKind.Farmland : z < 4000 ? RegionKind.Downs :
            z < 5000 ? RegionKind.LakeCountry : RegionKind.HighlandPass;

        public HabitatSample Sample(double x, double z)
        {
            bool water = IsWater(x, z);
            double dl = Math.Sqrt((x - LakeX) * (x - LakeX) + (z - LakeZ) * (z - LakeZ));
            RegionKind r = RegionAt(z);
            return new HabitatSample
            {
                Valid = true,
                Height = (float)(50.0 + (x > 300 && x < 320 ? (x - 300) * 2.0 : 0.0)),
                Water = water,
                Slope = x > 300 && x < 320 ? 2f : 0.05f,
                RoadDistance = (float)Math.Abs(x),
                Forest = r == RegionKind.Forest ? 0.8f : 0.1f,
                Highland = r == RegionKind.HighlandPass ? 0.8f : 0f,
                Farm = r == RegionKind.Farmland ? 0.7f : 0f,
                Moist = 0.5f,
                Shore = !water && dl < LakeR + 12 ? 0.8f : 0f,
                Region = r,
            };
        }

        public bool IsWater(double x, double z) => (x - LakeX) * (x - LakeX) + (z - LakeZ) * (z - LakeZ) < LakeR * LakeR;

        public void Settlements(double x0, double z0, double size, List<HabitatSettlement> results)
        {
            foreach (HabitatSettlement p in Places)
            {
                double nx = Math.Max(x0, Math.Min(p.X, x0 + size)), nz = Math.Max(z0, Math.Min(p.Z, z0 + size));
                double reach = p.Radius + 120.0;
                if ((nx - p.X) * (nx - p.X) + (nz - p.Z) * (nz - p.Z) <= reach * reach) results.Add(p);
            }
        }

        /// <summary>Fallow fields on a 100 m grid beside the road in the farmland band (every other one).</summary>
        public void Pastures(double x0, double z0, double size, List<HabitatPasture> results)
        {
            foreach (HabitatPasture f in AllFields())
            {
                if (f.X >= x0 && f.X < x0 + size && f.Z >= z0 && f.Z < z0 + size) results.Add(f);
            }
        }

        public static IEnumerable<HabitatPasture> AllFields()
        {
            for (int j = 0; j < 10; j++)
            {
                for (int i = -3; i <= 3; i++)
                {
                    if (i == 0 || ((i + j) & 1) == 1) continue;
                    yield return new HabitatPasture { Id = 1000 + j * 10 + i, X = i * 100.0, Z = 2000 + j * 100.0 + 50, HalfX = 35f, HalfZ = 25f, Yaw = 15f };
                }
            }
        }
    }

    /// <summary>Flat ground (height 0) with an optional pond; nothing blocks.</summary>
    internal sealed class FlatGround : IWildlifeGround
    {
        public float PondX = 1e6f, PondZ = 1e6f, PondR = 0f;
        public float OffsetX, OffsetZ;   // a moving origin: scene = absolute − offset

        public float Height(float x, float z) => 0f;

        public bool IsWater(float x, float z)
        {
            float dx = x + OffsetX - PondX, dz = z + OffsetZ - PondZ;
            return dx * dx + dz * dz < PondR * PondR;
        }

        public bool IsBlocked(float x, float z, float radius) => false;
    }

    /// <summary>A straight road along +Z through scene x = −OffsetX.</summary>
    internal sealed class StraightRoad : IWildlifeRoad
    {
        public float OffsetX, OffsetZ;

        public bool Nearest(float x, float z, out float roadX, out float roadZ, out float dirX, out float dirZ, out float distance)
        {
            roadX = -OffsetX;
            roadZ = z;
            dirX = 0f;
            dirZ = 1f;
            distance = Math.Abs(x + OffsetX);
            return true;
        }

        public bool Along(float x, float z, float dirX, float dirZ, float ahead, out float roadX, out float roadZ)
        {
            roadX = -OffsetX;
            roadZ = z + (dirZ >= 0f ? ahead : -ahead);
            return true;
        }
    }

    public sealed class WildlifeTests
    {
        private const string PackControllers = "Assets/polyperfect/Low Poly Animated Animals/Animator Controllers";

        // ------------------------------------------------------------------ catalog and controllers

        [Test]
        public void CatalogRowsAreCompleteAndGentle()
        {
            Assert.That(SpeciesCatalog.Count, Is.EqualTo(Enum.GetValues(typeof(Species)).Length));
            for (int i = 0; i < SpeciesCatalog.Count; i++)
            {
                SpeciesInfo s = SpeciesCatalog.All[i];
                Assert.That((int)s.Id, Is.EqualTo(i), s.Name);
                Assert.That(s.Prefabs.Length, Is.GreaterThan(0), s.Name);
                Assert.That(s.TargetHeight, Is.InRange(0.08f, 2.2f), s.Name);
                Assert.That(s.WalkSpeed, Is.GreaterThan(0f), s.Name);
                Assert.That(s.RunSpeed, Is.GreaterThan(s.WalkSpeed), s.Name);
                Assert.That(s.FleeDistance, Is.LessThanOrEqualTo(s.AlertDistance), s.Name);
                foreach (PoseBinding b in s.Poses)
                {
                    StringAssert.DoesNotContain("Dead", b.Bool, $"{s.Name} never dies");
                    if (b.Bool == "isAttacking" || b.Bool == "Attack")
                    {
                        Assert.That(b.Pose, Is.EqualTo(Pose.Display), $"{s.Name}: attack clips only as harmless display");
                        Assert.That(s.Id == Species.Stag || s.Id == Species.Horse, Is.True, $"{s.Name} must not attack");
                    }

                    Assert.That(s.Bools[b.BoolIndex], Is.EqualTo(b.Bool));
                }
            }

            Assert.That(SpeciesCatalog.Get(Species.Retriever).TryGetBinding(Pose.Bark, out PoseBinding bark), Is.True);
            Assert.That(bark.Kind, Is.EqualTo(BindKind.Pulse), "a bark returns by exit time: a pulse");
            Assert.That(SpeciesCatalog.Get(Species.Goose).Has(Pose.Fly), Is.False, "the goose controller has no flying state");
            Assert.That(SpeciesCatalog.Get(Species.Stag).ScaleLike, Is.EqualTo(Species.Deer));
        }

        private static string ControllerPath(string name)
        {
            string p = Path.Combine(PackControllers, name + ".controller");
            if (File.Exists(p)) return p;
            string root = System.Environment.GetEnvironmentVariable("MEDIEVALZEN_ROOT");
            if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, p))) return Path.Combine(root, p);
            return null;
        }

        [Test]
        public void CatalogMatchesTheControllers()
        {
            int checkedRows = 0;
            var problems = new List<string>();
            foreach (SpeciesInfo s in SpeciesCatalog.All)
            {
                string path = ControllerPath(s.Controller);
                if (path == null) continue;
                ControllerGraph g = ControllerGraph.Parse(File.ReadAllText(path));
                problems.AddRange(g.Check(s));
                checkedRows++;
            }

            if (checkedRows == 0) Assert.Ignore("polyperfect animals not imported");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(checkedRows, Is.EqualTo(SpeciesCatalog.Count));
        }

        [Test]
        public void BoolDriverDrivesEveryControllerThroughIdleAndNeverSticks()
        {
            int species = 0;
            foreach (SpeciesInfo s in SpeciesCatalog.All)
            {
                string path = ControllerPath(s.Controller);
                if (path == null) continue;
                species++;
                ControllerGraph g = ControllerGraph.Parse(File.ReadAllText(path));
                RunDriver(s, g, 160);
            }

            if (species == 0) Assert.Ignore("polyperfect animals not imported");
        }

        [Test]
        public void BoolDriverHandlesChainsAndPulsesOnAModel()
        {
            // A model controller with the pack's quirks, so the driver is tested even without the pack.
            var g = new ControllerGraph { DefaultState = "Idle" };
            foreach (string p in new[] { "isWalking", "isRunning", "isSitting", "isBarking", "isDead" }) g.Parameters[p] = "bool";
            ControllerGraph.State S(string n)
            {
                var st = new ControllerGraph.State { Name = n };
                g.States[n] = st;
                return st;
            }

            ControllerGraph.Transition T(string to, string param, bool value, float exit = -1f)
            {
                var t = new ControllerGraph.Transition { To = to, HasExitTime = exit >= 0f, ExitTime = Math.Max(0f, exit) };
                if (param != null) t.Conditions.Add(new KeyValuePair<string, bool>(param, value));
                return t;
            }

            ControllerGraph.State idle = S("Idle"), walk = S("Walk"), run = S("Run"), sit = S("Sit"), stand = S("Stand"), bark = S("Bark");
            S("Death");
            idle.Out.Add(T("Walk", "isWalking", true));
            idle.Out.Add(T("Run", "isRunning", true));
            idle.Out.Add(T("Sit", "isSitting", true));
            idle.Out.Add(T("Bark", "isBarking", true));
            idle.Out.Add(T("Death", "isDead", true));
            walk.Out.Add(T("Idle", "isWalking", false));
            run.Out.Add(T("Idle", "isRunning", false, 0.9f));
            sit.Out.Add(T("Stand", "isSitting", false));
            stand.Out.Add(T("Idle", null, false, 0.85f));
            bark.Out.Add(T("Idle", null, false, 0.18f));
            SpeciesInfo dog = SpeciesCatalog.Get(Species.GreatDane);
            Assert.That(g.Check(dog), Is.Empty, string.Join("\n", g.Check(dog)));
            Assert.That(g.IsSticky("Death"), Is.True);
            RunDriver(dog, g, 200);
        }

        private static void RunDriver(SpeciesInfo s, ControllerGraph g, int changes)
        {
            var model = new ControllerModel(g, s);
            var driver = new AnimatorBoolDriver(s);
            var rng = new System.Random(s.Name.GetHashCode() & 0x7fff);
            var poses = (Pose[])Enum.GetValues(typeof(Pose));
            const float dt = 0.05f;
            for (int c = 0; c < changes; c++)
            {
                Pose want = poses[rng.Next(poses.Length)];
                bool pulse = s.TryGetBinding(want, out PoseBinding b) && b.Kind == BindKind.Pulse;
                if (pulse) driver.Pulse(want);
                else driver.Wanted = want;
                float t = 0f;
                for (; t < 12f; t += dt)
                {
                    driver.Update(dt, true, model.InIdle, model);
                    model.Step(dt);
                    if (!driver.Busy && (pulse || t > 1.5f)) break;
                }

                Assert.That(t, Is.LessThan(12f), $"{s.Name}: stuck going to {want} (model in '{model.Current}')");
                Assert.That(model.MaxTrueAtOnce, Is.LessThanOrEqualTo(1), $"{s.Name}: one bool at a time");
                if (pulse || !s.TryGetBinding(want, out b))
                {
                    if (!pulse) Assert.That(model.Current, Is.EqualTo(g.DefaultState), $"{s.Name}: a pose the controller lacks shows Idle");
                    continue;
                }

                // The model must have entered the state the bool leads to (or the end of its chain: lie down → sleep).
                var chain = new List<string>();
                foreach (string target in g.TargetsOf(b.Bool))
                {
                    chain.Add(target);
                    string at = target;
                    for (int k = 0; k < 4; k++)
                    {
                        ControllerGraph.Transition next = g.States[at].Out.Find(x => x.Conditions.Count == 0);
                        if (next == null || next.To == null) break;
                        at = next.To;
                        chain.Add(at);
                    }
                }

                Assert.That(chain, Does.Contain(model.Current), $"{s.Name}: wanted {want} ({b.Bool}) but the model is in '{model.Current}'");
            }

            foreach (string set in model.EverSet) StringAssert.DoesNotContain("Dead", set, $"{s.Name}: a death bool was set");
        }

        // ------------------------------------------------------------------ planner

        private static string Describe(List<GroupPlan> plans)
        {
            var sb = new StringBuilder();
            foreach (GroupPlan g in plans)
            {
                sb.Append(g.Id).Append(' ').Append(g.Kind).Append(' ').Append(g.Main).Append(':');
                foreach (MemberPlan m in g.Members) sb.Append($"{m.Species}@{m.X:0.000},{m.Z:0.000};");
                sb.Append('\n');
            }

            return sb.ToString();
        }

        [Test]
        public void PlannerIsDeterministicAndOrderIndependent()
        {
            var a = new WildlifePlanner(new FakeHabitat());
            var b = new WildlifePlanner(new FakeHabitat());
            var cells = new List<(long, long)>();
            for (long z = -2; z < 50; z++)
            {
                for (long x = -3; x <= 3; x++) cells.Add((x, z));
            }

            var first = new Dictionary<(long, long), string>();
            foreach ((long x, long z) in cells) first[(x, z)] = Describe(a.PlanCell(x, z));
            cells.Reverse();
            int groups = 0;
            foreach ((long x, long z) in cells)
            {
                List<GroupPlan> plans = b.PlanCell(x, z);
                groups += plans.Count;
                Assert.That(Describe(plans), Is.EqualTo(first[(x, z)]), $"cell ({x}, {z})");
            }

            Assert.That(groups, Is.GreaterThan(20), "the fake land holds animals");
            var other = new WildlifePlanner(new FakeHabitat { Seed = 8 });
            int differ = 0;
            foreach ((long x, long z) in cells)
            {
                if (Describe(other.PlanCell(x, z)) != first[(x, z)]) differ++;
            }

            Assert.That(differ, Is.GreaterThan(10), "another seed plans other animals");
        }

        [Test]
        public void PlannerKeepsOutOfWaterSteepGroundTheRoadAndSettlements()
        {
            var habitat = new FakeHabitat();
            var planner = new WildlifePlanner(habitat);
            int pastures = 0, farmyards = 0, wild = 0, shore = 0;
            var fields = new List<HabitatPasture>(FakeHabitat.AllFields());
            for (long z = -2; z < 50; z++)
            {
                for (long x = -4; x <= 4; x++)
                {
                    List<GroupPlan> plans = planner.PlanCell(x, z);
                    Assert.That(plans.Count, Is.LessThanOrEqualTo(WildlifePlanner.MaxGroupsPerCell));
                    foreach (GroupPlan g in plans)
                    {
                        foreach (MemberPlan m in g.Members)
                        {
                            HabitatSample s = habitat.Sample(m.X, m.Z);
                            SpeciesInfo info = SpeciesCatalog.Get(m.Species);
                            Assert.That(s.Water, Is.False, $"{g}: {m.Species} in water");
                            if (!info.Flies) Assert.That(s.Slope, Is.LessThanOrEqualTo(info.MaxSlope), $"{g}: {m.Species} on steep ground");
                            Assert.That(s.RoadDistance, Is.GreaterThanOrEqualTo(habitat.Corridor), $"{g}: {m.Species} on the road");
                            foreach (HabitatSettlement p in habitat.Places)
                            {
                                double d = Math.Sqrt((m.X - p.X) * (m.X - p.X) + (m.Z - p.Z) * (m.Z - p.Z));
                                Assert.That(d, Is.GreaterThan(p.Radius - 0.5), $"{g}: {m.Species} inside {p.Kind} #{p.Id}'s dressed radius");
                            }
                        }

                        switch (g.Kind)
                        {
                            case GroupKind.Pasture:
                                pastures++;
                                HabitatPasture f = fields.Find(q => Math.Abs(q.X - g.RectX) < 1e-6 && Math.Abs(q.Z - g.RectZ) < 1e-6);
                                Assert.That(f.HalfX, Is.GreaterThan(0f), "a pasture herd stands in a fallow field");
                                foreach (MemberPlan m in g.Members)
                                {
                                    var agent = new AnimalGroup { HasRect = true, RectX = (float)g.RectX, RectZ = (float)g.RectZ, HalfX = g.HalfX, HalfZ = g.HalfZ, Yaw = g.Yaw };
                                    Assert.That(agent.InHome((float)m.X, (float)m.Z, 0.01f), Is.True, $"{g}: member outside its field");
                                }

                                Assert.That(g.Main == Species.Cow || g.Main == Species.Sheep || g.Main == Species.Horse, Is.True);
                                break;
                            case GroupKind.Farmyard:
                                farmyards++;
                                Assert.That(g.SettlementId, Is.GreaterThan(0));
                                Assert.That(g.CountOf(Species.Hen), Is.GreaterThan(0));
                                break;
                            case GroupKind.Shore:
                                shore++;
                                break;
                            case GroupKind.Wild:
                                wild++;
                                Assert.That(Math.Abs(g.HomeX), Is.GreaterThanOrEqualTo(WildlifePlanner.WildRoadClearance));
                                break;
                        }
                    }
                }
            }

            Assert.That(pastures, Is.GreaterThan(3), "fallow fields hold herds");
            Assert.That(farmyards, Is.EqualTo(3), "one farmyard per settlement, planned once");
            Assert.That(wild, Is.GreaterThan(5));
            Assert.That(shore, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void WildChoiceFollowsTheHabitat()
        {
            var forest = new HabitatSample { Valid = true, Region = RegionKind.Forest, Forest = 0.9f };
            var seen = new HashSet<Species>();
            for (int i = 0; i < 200; i++)
            {
                if (WildlifePlanner.ChooseWild(forest, 0.0, i / 200.0, out Species s, out int count, out _))
                {
                    seen.Add(s);
                    Assert.That(count, Is.GreaterThan(0));
                }
            }

            Assert.That(seen, Has.Member(Species.Deer).And.Member(Species.Boar).And.Member(Species.Squirrel));
            Assert.That(seen, Has.No.Member(Species.Goose).And.No.Member(Species.Eagle));
            var shore = new HabitatSample { Valid = true, Region = RegionKind.LakeCountry, Shore = 0.9f };
            Assert.That(WildlifePlanner.ChooseWild(shore, 0.1, 0.1, out Species bird, out _, out GroupKind kind), Is.True);
            Assert.That(bird, Is.EqualTo(Species.Goose));
            Assert.That(kind, Is.EqualTo(GroupKind.Shore));
            var pass = new HabitatSample { Valid = true, Region = RegionKind.HighlandPass };
            Assert.That(WildlifePlanner.ChooseWild(pass, 0.1, 0.7, out Species eagle, out _, out GroupKind sky), Is.True);
            Assert.That(eagle, Is.EqualTo(Species.Eagle));
            Assert.That(sky, Is.EqualTo(GroupKind.Sky));
            Assert.That(WildlifePlanner.ChooseWild(pass, 0.99, 0.7, out _, out _, out _), Is.False, "most spots are empty");
        }

        [Test]
        public void RealGeneratorPlansStayDryOffTheRoadAndInFallowFields()
        {
            var gen = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);
            var habitat = new EndlessHabitat(gen);
            var planner = new WildlifePlanner(habitat);
            int groups = 0, pastures = 0;
            var fields = new List<MosaicField>();
            for (double s = 0; s < 3000; s += 300)
            {
                RoadPoint p = gen.Road(s);
                WildlifePlanner.CellOf(p.X + 140, p.Z, out long cx, out long cz);
                foreach (GroupPlan g in planner.PlanCell(cx, cz))
                {
                    groups++;
                    foreach (MemberPlan m in g.Members)
                    {
                        Assert.That(gen.WaterHeight(m.X, m.Z), Is.LessThan(0f), $"{g}: in water");
                        gen.Curve.Nearest(m.X, m.Z, out double d);
                        Assert.That(d, Is.GreaterThanOrEqualTo(habitat.Corridor), $"{g}: on the road");
                    }

                    if (g.Kind != GroupKind.Pasture) continue;
                    pastures++;
                    fields.Clear();
                    gen.Fields.Overlapping(g.RectX - 1, g.RectZ - 1, 2, fields);
                    Assert.That(fields.Exists(f => f.Fallow && Math.Abs(f.X - g.RectX) < 1e-6 && Math.Abs(f.Z - g.RectZ) < 1e-6), Is.True, $"{g}: not a fallow field");
                }
            }

            Assert.That(groups, Is.GreaterThanOrEqualTo(0));
            Assert.That(pastures, Is.LessThanOrEqualTo(groups));
        }

        // ------------------------------------------------------------------ brains

        private static WildlifeContext At(float time, float x, float z, float speed = 1.3f, bool seated = false, float seatedTime = 0f, float hour = 12f) =>
            new WildlifeContext
            {
                Time = time, HasTraveller = true, TravellerX = x, TravellerZ = z, Speed = speed, VelocityZ = speed, FacingX = 0f, FacingZ = 1f,
                Seated = seated, SeatedTime = seatedTime, Hour = hour,
            };

        private static GroupPlan Plan(long id, GroupKind kind, Species species, int count, double x, double z, float radius = 10f)
        {
            var g = new GroupPlan { Id = id, Kind = kind, Main = species, HomeX = x, HomeZ = z, HomeRadius = radius };
            for (int i = 0; i < count; i++) g.Members.Add(new MemberPlan { Species = species, X = x + (i % 3) * 2.5, Z = z + (i / 3) * 2.5 });
            return g;
        }

        private static WildlifeSim NewSim(out FlatGround ground, out StraightRoad road)
        {
            ground = new FlatGround();
            road = new StraightRoad();
            var sim = new WildlifeSim(5, ground, road, 8f);
            sim.Events.Automatic = false;
            return sim;
        }

        [Test]
        public void DeerLookUpThenFleeThenCalmAndReturn()
        {
            WildlifeSim sim = NewSim(out _, out _);
            AnimalGroup g = sim.AddGroup(Plan(1, GroupKind.Wild, Species.Deer, 3, 60, 100), 0, 0);
            float t = 0f;
            // The traveller walks up the road towards them.
            bool alerted = false, fled = false;
            for (; t < 60f; t += 0.05f)
            {
                float tz = Math.Min(100f, t * 2.0f + 20f);
                sim.Step(0.05f, At(t, 30f, tz, 2.0f));
                foreach (AnimalAgent a in g.Members)
                {
                    alerted |= a.Mode == BrainMode.Alert;
                    fled |= a.Mode == BrainMode.Flee;
                }

                if (fled) break;
            }

            Assert.That(alerted, Is.True, "deer look up first");
            Assert.That(fled, Is.True, "then bolt");
            Assert.That(g.Disturbed, Is.True);
            // The traveller walks away; the deer calm down and drift home.
            for (float u = 0f; u < 240f; u += 0.1f) sim.Step(0.1f, At(t + u, 30f, -400f, 1.3f));
            g.Centroid(out float cx, out float cz);
            Assert.That(Math.Sqrt((cx - 60) * (cx - 60) + (cz - 100) * (cz - 100)), Is.LessThan(g.HomeRadius + 8f), "back home");
            foreach (AnimalAgent a in g.Members) Assert.That(a.Mode, Is.Not.EqualTo(BrainMode.Flee));
        }

        [Test]
        public void ASeatedTravellerIsLeftInPeace()
        {
            WildlifeSim sim = NewSim(out _, out _);
            AnimalGroup g = sim.AddGroup(Plan(2, GroupKind.Wild, Species.Rabbit, 3, 40, 0, 6f), 0, 0);
            // Seated 31 m away for a long while: rabbits (alert 22 m) graze on; after the calm-down they may drift closer.
            for (float t = 0f; t < 120f; t += 0.1f) sim.Step(0.1f, At(t, 9f, 0f, 0f, true, 30f + t));
            foreach (AnimalAgent a in g.Members) Assert.That(a.Mode, Is.Not.EqualTo(BrainMode.Flee), "no flight from a still, seated traveller");
            // The same distance walking briskly makes them bolt.
            WildlifeSim sim2 = NewSim(out _, out _);
            AnimalGroup g2 = sim2.AddGroup(Plan(2, GroupKind.Wild, Species.Rabbit, 3, 40, 0, 6f), 0, 0);
            bool fled = false;
            for (float t = 0f; t < 20f && !fled; t += 0.05f)
            {
                sim2.Step(0.05f, At(t, 30f + t * 0.5f, 0f, 1.5f));
                foreach (AnimalAgent a in g2.Members) fled |= a.Mode == BrainMode.Flee;
            }

            Assert.That(fled, Is.True);
        }

        [Test]
        public void FarmAnimalsSleepAtNightAndHensStepAside()
        {
            WildlifeSim sim = NewSim(out _, out _);
            AnimalGroup cows = sim.AddGroup(Plan(3, GroupKind.Pasture, Species.Cow, 4, 50, 50, 20f), 0, 0);
            for (float t = 0f; t < 60f; t += 0.1f) sim.Step(0.1f, At(t, 0f, -300f, 0f, false, 0f, 23f));
            int asleep = 0;
            foreach (AnimalAgent a in cows.Members) asleep += a.Mode == BrainMode.Rest && a.Pose == Pose.Sleep ? 1 : 0;
            Assert.That(asleep, Is.EqualTo(cows.Members.Count), "cows sleep at night");

            AnimalGroup yard = sim.AddGroup(Plan(4, GroupKind.Farmyard, Species.Hen, 1, 20, 0, 5f), 0, 0);
            AnimalAgent hen = yard.Members[0];
            float min = float.MaxValue;
            for (float t = 0f; t < 20f; t += 0.02f)
            {
                // Walk straight through the hen.
                float tz = -10f + t * 1.4f;
                sim.Step(0.02f, At(60f + t, 20f, tz, 1.4f, false, 0f, 12f));
                if (t > 1f) min = Math.Min(min, hen.DistanceTo(20f, tz));
            }

            Assert.That(min, Is.GreaterThanOrEqualTo(0.85f), "the hen is never under the traveller's feet");
        }

        [Test]
        public void DovesTakeOffCircleAndLand()
        {
            WildlifeSim sim = NewSim(out _, out _);
            AnimalGroup doves = sim.AddGroup(Plan(5, GroupKind.Doves, Species.Dove, 4, 0, 50, 4f), 0, 0);
            bool flew = false;
            for (float t = 0f; t < 10f; t += 0.05f)
            {
                sim.Step(0.05f, At(t, 0f, 40f + t * 1.4f, 1.4f));
                foreach (AnimalAgent a in doves.Members) flew |= a.Altitude > 3f;
            }

            Assert.That(flew, Is.True, "doves flush when the traveller comes close");
            for (float t = 10f; t < 80f; t += 0.05f) sim.Step(0.05f, At(t, 0f, -200f, 0f));
            foreach (AnimalAgent a in doves.Members)
            {
                Assert.That(a.Altitude, Is.LessThan(0.05f), "landed");
                Assert.That(a.DistanceTo(a.HomeX, a.HomeZ), Is.LessThan(8f));
            }
        }

        // ------------------------------------------------------------------ events

        private static AnimalGroup Farmyard(WildlifeSim sim, float x, float z, bool dog = true)
        {
            var plan = new GroupPlan { Id = 77, Kind = GroupKind.Farmyard, Main = Species.Hen, HomeX = x, HomeZ = z, HomeRadius = 7f, SettlementId = 9 };
            for (int i = 0; i < 4; i++) plan.Members.Add(new MemberPlan { Species = Species.Hen, X = x + i, Z = z + (i % 2) });
            plan.Members.Add(new MemberPlan { Species = Species.Rooster, X = x - 2, Z = z });
            if (dog) plan.Members.Add(new MemberPlan { Species = Species.Retriever, X = x, Z = z - 3 });
            return sim.AddGroup(plan, 0, 0);
        }

        private static AnimalEvent RunEvent(WildlifeSim sim, EventKind kind, WildlifeContext ctx, float shiftAt = -1f, FlatGround ground = null, StraightRoad road = null)
        {
            Assert.That(sim.Events.TryStart(kind, ctx, out string why), Is.True, why);
            AnimalEvent e = sim.Events.Active;
            float t = ctx.Time;
            bool shifted = false;
            float closest = float.MaxValue;
            while (sim.Events.Active != null && t < ctx.Time + 400f)
            {
                t += 0.05f;
                WildlifeContext c = ctx;
                c.Time = t;
                if (shiftAt >= 0f && !shifted && t - ctx.Time > shiftAt)
                {
                    // A forced origin shift mid-event (F4): everything moves by whole tiles, the world (ground, road) too.
                    shifted = true;
                    var delta = new Vector3(-256f, 0f, 256f);
                    sim.Shift(delta);
                    ground.OffsetX -= delta.x;
                    ground.OffsetZ -= delta.z;
                    road.OffsetX -= delta.x;
                    road.OffsetZ -= delta.z;
                    ctx.TravellerX += delta.x;
                    ctx.TravellerZ += delta.z;
                    c.TravellerX = ctx.TravellerX;
                    c.TravellerZ = ctx.TravellerZ;
                }

                sim.Step(0.05f, c);
                foreach (AnimalAgent a in sim.Agents) closest = Math.Min(closest, a.Altitude > 0.5f ? float.MaxValue : a.DistanceTo(c.TravellerX, c.TravellerZ));
            }

            Assert.That(e.Finished, Is.True, $"{kind} ended");
            Assert.That(e.EndReason, Does.Not.Contain("timed out"), $"{kind} ran to its end");
            foreach (AnimalAgent a in sim.Agents) Assert.That(a.Scripted, Is.False, $"{a} still scripted after {kind}");
            foreach (AnimalGroup g in sim.Groups) Assert.That(g.Event, Is.Null);
            Assert.That(closest, Is.GreaterThanOrEqualTo(0.85f), "no animal walks through the traveller");
            return e;
        }

        [Test]
        public void FoxAtTheHenHouseEndsWithEveryoneSafe()
        {
            WildlifeSim sim = NewSim(out _, out _);
            Farmyard(sim, 40f, 80f);
            AnimalEvent e = RunEvent(sim, EventKind.FoxAtHenHouse, At(10f, 0f, 0f, 0f));
            Assert.That(e.HasChase, Is.True);
            Assert.That(e.MinGap, Is.GreaterThanOrEqualTo(FoxAtHenHouseEvent.FoxGap - 0.6f), "the fox never reaches a hen");
            Assert.That(sim.Groups.Exists(g => g.Main == Species.Fox && g.Visitor), Is.True, "the fox stays, far off");
        }

        [Test]
        public void DogAndRabbitsEndsWithTheRabbitsAway()
        {
            WildlifeSim sim = NewSim(out _, out _);
            Farmyard(sim, 40f, 60f);
            AnimalEvent e = RunEvent(sim, EventKind.DogAndRabbits, At(10f, 0f, 0f, 0f));
            Assert.That(e.MinGap, Is.GreaterThanOrEqualTo(DogAndRabbitsEvent.Gap - 0.6f), "the dog never reaches a rabbit");
        }

        [Test]
        public void WolfAndDeerCrossWellAheadAndTheWolfStaysBack()
        {
            WildlifeSim sim = NewSim(out _, out _);
            AnimalGroup deer = sim.AddGroup(Plan(6, GroupKind.Wild, Species.Deer, 3, 70, 120, 12f), 0, 0);
            AnimalEvent e = RunEvent(sim, EventKind.WolfAndDeer, At(10f, 0f, 0f, 1.3f, false, 0f, 20f));
            Assert.That(e.MinGap, Is.GreaterThanOrEqualTo(WolfAndDeerEvent.Gap - 0.6f), "the wolf never reaches a deer");
            foreach (AnimalAgent d in deer.Members) Assert.That(d.X, Is.LessThan(0f), "the deer crossed the road");
            Assert.That(deer.HomeX, Is.LessThan(0f), "and live on the far side now");
        }

        [Test]
        public void WolfAndDeerWaitForDusk()
        {
            WildlifeSim sim = NewSim(out _, out _);
            sim.AddGroup(Plan(6, GroupKind.Wild, Species.Deer, 3, 70, 120, 12f), 0, 0);
            Assert.That(WolfAndDeerEvent.Prepare(sim, At(10f, 0f, 0f, 1.3f, false, 0f, 12f), false, out _, out string why), Is.Null);
            StringAssert.Contains("dusk", why);
        }

        [Test]
        public void SheepdogHerdingMovesTheFlockOverTheRoad()
        {
            WildlifeSim sim = NewSim(out _, out _);
            AnimalGroup flock = sim.AddGroup(Plan(7, GroupKind.Pasture, Species.Sheep, 6, 45, 160, 14f), 0, 0);
            AnimalEvent e = RunEvent(sim, EventKind.SheepdogHerding, At(10f, 0f, 0f, 1.3f));
            Assert.That(e.MinGap, Is.GreaterThanOrEqualTo(SheepdogHerdingEvent.Gap - 0.6f));
            flock.Centroid(out float cx, out _);
            Assert.That(cx, Is.LessThan(0f), "the flock is across the road");
        }

        [Test]
        public void HorsesGallopALoopAndSettle()
        {
            WildlifeSim sim = NewSim(out _, out _);
            var plan = Plan(8, GroupKind.Pasture, Species.Horse, 3, 60, 100, 20f);
            plan.HasRect = true;
            plan.RectX = 60;
            plan.RectZ = 100;
            plan.HalfX = 30f;
            plan.HalfZ = 22f;
            AnimalGroup herd = sim.AddGroup(plan, 0, 0);
            float maxSpeed = 0f;
            Assert.That(sim.Events.TryStart(EventKind.HorsesRunning, At(10f, 0f, 0f, 0f), out string why), Is.True, why);
            for (float t = 10f; sim.Events.Active != null && t < 200f; t += 0.05f)
            {
                sim.Step(0.05f, At(t, 0f, 0f, 0f));
                foreach (AnimalAgent a in herd.Members)
                {
                    maxSpeed = Math.Max(maxSpeed, a.Speed);
                    Assert.That(herd.InHome(a.X, a.Z, 6f), Is.True, "the gallop stays in the field");
                }
            }

            Assert.That(sim.Events.Active, Is.Null);
            Assert.That(maxSpeed, Is.GreaterThan(4f), "they galloped");
        }

        [Test]
        public void BirdsFlushAndStagsSquareUpWithoutTouching()
        {
            WildlifeSim sim = NewSim(out _, out _);
            sim.AddGroup(Plan(9, GroupKind.Doves, Species.Dove, 5, 30, 60, 4f), 0, 0);
            RunEvent(sim, EventKind.BirdsFlushing, At(10f, 0f, 0f, 0f));

            WildlifeSim sim2 = NewSim(out _, out _);
            var plan = Plan(10, GroupKind.Wild, Species.Deer, 2, 60, 80, 12f);
            plan.Members.Add(new MemberPlan { Species = Species.Stag, X = 55, Z = 78 });
            plan.Members.Add(new MemberPlan { Species = Species.Stag, X = 66, Z = 84 });
            sim2.AddGroup(plan, 0, 0);
            Assert.That(sim2.Events.TryStart(EventKind.StagsSquaringUp, At(10f, 0f, 0f, 0f), out string why), Is.True, why);
            var stags = (StagsSquaringUpEvent)sim2.Events.Active;
            float min = float.MaxValue;
            for (float t = 10f; sim2.Events.Active != null && t < 200f; t += 0.05f)
            {
                sim2.Step(0.05f, At(t, 0f, 0f, 0f));
                min = Math.Min(min, stags.StagDistance);
            }

            Assert.That(stags.Finished, Is.True);
            Assert.That(min, Is.GreaterThanOrEqualTo(StagsSquaringUpEvent.Gap - 0.6f), "a feint, never a blow");
        }

        [Test]
        public void AForcedOriginShiftMidEventChangesNothing()
        {
            WildlifeSim sim = NewSim(out FlatGround ground, out StraightRoad road);
            AnimalGroup deer = sim.AddGroup(Plan(11, GroupKind.Wild, Species.Deer, 3, 70, 120, 12f), 0, 0);
            AnimalEvent e = RunEvent(sim, EventKind.WolfAndDeer, At(10f, 0f, 0f, 1.3f, false, 0f, 20f), 4f, ground, road);
            Assert.That(e.MinGap, Is.GreaterThanOrEqualTo(WolfAndDeerEvent.Gap - 0.6f));
            // Scene x = absolute − offset: the deer are across the road (absolute x < 0).
            foreach (AnimalAgent d in deer.Members) Assert.That(d.X + ground.OffsetX, Is.LessThan(0f));
        }

        [Test]
        public void ForcingWithNothingNearSaysWhy()
        {
            WildlifeSim sim = NewSim(out _, out _);
            Assert.That(sim.Events.TryForce(At(0f, 0f, 0f), out string why), Is.False);
            Assert.That(why, Is.EqualTo("No animals nearby for an event"));
            sim.AddGroup(Plan(12, GroupKind.Wild, Species.Squirrel, 2, 40, 40, 5f), 0, 0);
            sim.Step(0.05f, At(0.05f, 0f, 0f));
            Assert.That(sim.Events.TryForce(At(0.1f, 0f, 0f), out why), Is.False);
            StringAssert.StartsWith("No animals in the right place", why);
        }

        [Test]
        public void AutomaticEventsWaitForTheCooldownAndNeverDuringAHold()
        {
            WildlifeSim sim = NewSim(out _, out _);
            sim.Events.Automatic = true;
            Farmyard(sim, 40f, 80f);
            WildlifeContext held = At(0f, 0f, 0f, 0f);
            held.Holding = true;
            for (float t = 0f; t < 600f; t += 0.5f)
            {
                held.Time = t;
                sim.Step(0.5f, held);
            }

            Assert.That(sim.Events.History, Is.Empty, "nothing starts during a streaming hold");
            for (float t = 600f; t < 1200f; t += 0.5f) sim.Step(0.5f, At(t, 0f, 0f, 0f));
            Assert.That(sim.Events.History.Count, Is.GreaterThan(0), "events happen now and then");
            Assert.That(sim.Events.History.Count, Is.LessThanOrEqualTo(600f / EventDirector.CooldownMin + 1f), "with a few minutes between them");
        }

        // ------------------------------------------------------------------ points of interest, shift, safety, milestones

        [Test]
        public void PointsOfInterestExpireShiftAndOutrankLandmarksOnlyWhenTheyShould()
        {
            var poi = new PointsOfInterest(4);
            poi.Tick(0f);
            poi.Publish(1, new Vector3(0f, 0f, 100f), 10f, 1.6f, 5f);
            poi.Publish(2, new Vector3(0f, 0f, 60f), 8f, 0.5f, 5f);
            Assert.That(poi.TryBest(Vector3.zero, Vector3.forward, false, out Vector3 s, out float score), Is.True);
            Assert.That(s.z, Is.EqualTo(100f).Within(0.01f), "the event beats the herd");
            Assert.That(score, Is.GreaterThan(1f), "an event in front outranks any landmark (whose score is at most 1)");
            Assert.That(poi.TryBest(Vector3.zero, Vector3.back, false, out _, out _), Is.False, "nothing behind unless seated");
            Assert.That(poi.TryBest(Vector3.zero, Vector3.back, true, out _, out _), Is.True, "rest shots look any way");
            poi.Shift(new Vector3(10f, 0f, -20f));
            poi.TryBest(Vector3.zero, Vector3.forward, false, out s, out _);
            Assert.That(s.x, Is.EqualTo(10f).Within(0.01f));
            Assert.That(s.z, Is.EqualTo(80f).Within(0.01f));
            poi.Remove(1);
            poi.TryBest(Vector3.zero, Vector3.forward, false, out _, out score);
            Assert.That(score, Is.LessThan(0.5f), "a herd alone is a modest subject");
            poi.Tick(6f);
            Assert.That(poi.Count, Is.EqualTo(0), "expired");
            Assert.That(poi.TryBest(Vector3.zero, Vector3.forward, true, out _, out _), Is.False);
            poi.Publish(3, new Vector3(0f, 0f, 10f), 1f, 1.6f, 20f);
            Assert.That(poi.TryBest(Vector3.zero, Vector3.forward, true, out _, out _), Is.False, "too close to frame");
        }

        [Test]
        public void ShiftMovesEveryPosition()
        {
            WildlifeSim sim = NewSim(out _, out _);
            AnimalGroup g = sim.AddGroup(Plan(13, GroupKind.Wild, Species.Deer, 2, 50, 50, 10f), 0, 0);
            AnimalAgent a = g.Members[0];
            a.GoTo(60f, 60f, false);
            float x = a.X, hx = a.HomeX, tx = a.TargetX, gx = g.HomeX;
            sim.Shift(new Vector3(-256f, 0f, 0f));
            Assert.That(a.X, Is.EqualTo(x - 256f));
            Assert.That(a.HomeX, Is.EqualTo(hx - 256f));
            Assert.That(a.TargetX, Is.EqualTo(tx - 256f));
            Assert.That(g.HomeX, Is.EqualTo(gx - 256f));
        }

        [Test]
        public void AnimalsAreASafetyFeature()
        {
            Assert.That(SafetyLadder.FeaturesFor(0).animals, Is.True);
            Assert.That(SafetyLadder.FeaturesFor(3).animals, Is.True);
            Assert.That(SafetyLadder.FeaturesFor(4).animals, Is.False, "off from level 4 (the forest pass put a rung for the woods' extras first)");
            Assert.That(SafetyLadder.FeaturesFor(5).animals, Is.False);
            EndlessFeatures off = EndlessFeatures.All;
            off.animals = false;
            Assert.That(EndlessFeatures.And(off, EndlessFeatures.All).animals, Is.False);
            StringAssert.Contains("animals-", off.ToString());
            StringAssert.Contains("animals", SafetyLadder.Describe(4));
        }

        [Test]
        public void MilestonesLogOnce()
        {
            var m = new WildlifeMilestones();
            Assert.That(m.Update(5, 1, out _), Is.False);
            Assert.That(m.Update(12, 3, out string line), Is.True);
            StringAssert.Contains("animals 10", line);
            StringAssert.Contains("groups 3", line);
            Assert.That(m.Update(12, 3, out _), Is.False);
            Assert.That(m.Update(8, 2, out _), Is.False);
            Assert.That(m.Update(45, 6, out line), Is.True);
            StringAssert.Contains("animals 40", line);
        }
    }
}
