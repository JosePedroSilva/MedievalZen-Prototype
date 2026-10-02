using System;
using System.Collections.Generic;
using MedievalZen.Cinematic;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>The cinematic camera's pure core: springs, rotations, framing, occlusion, road path, scheduler, solver, rules.</summary>
    public sealed class CinematicTests
    {
        /// <summary>A world of plain functions for the solver and the rules.</summary>
        private sealed class FakeWorld : ICinematicQueries
        {
            public Func<float, float, float> Height = (x, z) => 0f;
            public float Water = -1f;
            public Func<float, float, float> Density = (x, z) => 0f;
            public readonly List<(Vector3 basePoint, float height, float crown, float trunk)> Trees = new List<(Vector3, float, float, float)>();
            public float BuiltRadius = 400f;

            public float GroundHeight(Vector3 p) => Height(p.x, p.z);
            public float WaterHeight(Vector3 p) => Water;
            public float TreeDensity(Vector3 p) => Density(p.x, p.z);

            public bool SightBlocked(Vector3 from, Vector3 to)
            {
                if (CinematicMath.TerrainBlocks(from, to, Height)) return true;
                foreach (var t in Trees)
                {
                    if (CinematicMath.TreeBlocks(from, to, t.basePoint, t.height, t.crown, t.trunk)) return true;
                }

                return false;
            }

            public bool InsideTree(Vector3 p, float pad)
            {
                foreach (var t in Trees)
                {
                    if (CinematicMath.InsideTree(p, pad, t.basePoint, t.height, t.crown, t.trunk)) return true;
                }

                return false;
            }

            public bool InBuiltArea(Vector3 p) => new Vector2(p.x, p.z).magnitude < BuiltRadius;
        }

        private static ShotInputs StraightRoad(FakeWorld world, float speed = 1.4f)
        {
            var path = new RoadPath();
            for (float z = -60f; z <= 200f; z += 2f) path.Add(new Vector3(0f, world.Height(0f, z), z));
            return new ShotInputs
            {
                Traveller = new Vector3(0f, world.Height(0f, 0f), 0f),
                Heading = Vector3.forward,
                Speed = speed,
                Path = path,
                Queries = world,
                Aspect = 16f / 9f,
            };
        }

        // ------------------------------------------------------------------ springs and rotations

        [Test]
        public void Damp_ConvergesWithoutOvershoot()
        {
            float x = 0f, v = 0f;
            float previous = 0f;
            for (int i = 0; i < 600; i++)
            {
                x = CinematicMath.Damp(x, 10f, ref v, 1f, 1f / 60f);
                Assert.That(x, Is.GreaterThanOrEqualTo(previous - 1e-5f), "monotonic");
                Assert.That(x, Is.LessThanOrEqualTo(10f + 1e-4f), "no overshoot");
                previous = x;
            }

            Assert.That(x, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void Damp_MovingTargetIsFollowedSmoothly()
        {
            Vector3 x = Vector3.zero, v = Vector3.zero;
            float maxAcceleration = 0f;
            Vector3 lastV = Vector3.zero;
            for (int i = 0; i < 1200; i++)
            {
                var target = new Vector3(i / 60f * 1.4f, 0f, 0f);   // walking pace
                x = CinematicMath.Damp(x, target, ref v, 1f, 1f / 60f);
                maxAcceleration = Mathf.Max(maxAcceleration, (v - lastV).magnitude * 60f);
                lastV = v;
            }

            Assert.That(v.x, Is.EqualTo(1.4f).Within(0.05f), "settles to the target's speed");
            Assert.That(maxAcceleration, Is.LessThan(4f), "no jolts");
        }

        [Test]
        public void DampLimited_NeverExceedsTheSpeedAndStillArrives()
        {
            Vector3 x = Vector3.zero, v = Vector3.zero;
            var target = new Vector3(30f, 0f, 0f);   // a long blend
            for (int i = 0; i < 60 * 30; i++)
            {
                Vector3 before = x;
                x = CinematicMath.DampLimited(x, target, ref v, 2.4f, 3.5f, 1f / 60f);
                Assert.That((x - before).magnitude * 60f, Is.LessThanOrEqualTo(3.5f + 1e-3f));
                Assert.That(x.x, Is.LessThanOrEqualTo(30f + 1e-4f));
            }

            Assert.That(x.x, Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void Rotations_MatchUnityConventions()
        {
            Vector3 right = CinematicMath.RotateYaw(Vector3.forward, 90f);
            Assert.That(right.x, Is.EqualTo(1f).Within(1e-5f));
            Vector3 down = CinematicMath.RotateAbout(Vector3.forward, Vector3.right, 30f);
            Assert.That(down.y, Is.EqualTo(-0.5f).Within(1e-5f), "positive pitch looks down, like Quaternion.AngleAxis");
            Assert.That(CinematicMath.PitchOf(down), Is.EqualTo(30f).Within(1e-3f));
            Assert.That(CinematicMath.YawOf(right), Is.EqualTo(90f).Within(1e-3f));
            Vector3 yawAbout = CinematicMath.RotateAbout(Vector3.forward, Vector3.up, 90f);
            Assert.That((yawAbout - right).magnitude, Is.LessThan(1e-5f));
            Assert.That(CinematicMath.RightOf(Vector3.forward), Is.EqualTo(Vector3.right));
        }

        // ------------------------------------------------------------------ framing

        [TestCase(1f / 3f, -1f / 3f)]
        [TestCase(-1f / 3f, 0f)]
        [TestCase(0f, 0.2f)]
        public void FrameAt_PutsTheSubjectWhereAsked(float sx, float sy)
        {
            var camera = new Vector3(12f, 3f, -4f);
            var subject = new Vector3(0f, 1.2f, 0f);
            Vector3 look = CinematicMath.FrameAt(camera, subject, Vector3.zero, 40f, 16f / 9f, sx, sy);
            Vector3 screen = CinematicMath.ScreenPosition(camera, look, subject, 40f, 16f / 9f);
            Assert.That(screen.x, Is.EqualTo(sx).Within(0.03f));
            Assert.That(screen.y, Is.EqualTo(sy).Within(0.03f));
        }

        [Test]
        public void FrameAt_LeavesRoomInTheDirectionOfTravel()
        {
            // Camera to the right of a traveller walking +Z: travel is towards the camera's left... check both ways.
            var camera = new Vector3(12f, 2f, 0f);
            var subject = new Vector3(0f, 1.2f, 0f);
            Vector3 look = CinematicMath.FrameAt(camera, subject, Vector3.forward, 40f, 16f / 9f, 1f / 3f, 0f);
            Vector3 screen = CinematicMath.ScreenPosition(camera, look, subject, 40f, 16f / 9f);
            Vector3 ahead = CinematicMath.ScreenPosition(camera, look, subject + Vector3.forward * 3f, 40f, 16f / 9f);
            Assert.That(Mathf.Abs(screen.x), Is.EqualTo(1f / 3f).Within(0.03f));
            Assert.That(Mathf.Abs(ahead.x), Is.LessThan(Mathf.Abs(screen.x)), "the space ahead of the traveller is towards the centre");
        }

        // ------------------------------------------------------------------ occlusion and clearance

        [Test]
        public void SegmentHitsCylinder_Cases()
        {
            var tree = new Vector3(0f, 0f, 0f);
            Assert.That(CinematicMath.SegmentHitsCylinder(new Vector3(-5f, 1f, 0f), new Vector3(5f, 1f, 0f), tree, 0.3f, 4f), Is.True, "through the trunk");
            Assert.That(CinematicMath.SegmentHitsCylinder(new Vector3(-5f, 1f, 1f), new Vector3(5f, 1f, 1f), tree, 0.3f, 4f), Is.False, "beside it");
            Assert.That(CinematicMath.SegmentHitsCylinder(new Vector3(-5f, 6f, 0f), new Vector3(5f, 6f, 0f), tree, 0.3f, 4f), Is.False, "above it");
            Assert.That(CinematicMath.SegmentHitsCylinder(new Vector3(-5f, 6f, 0f), new Vector3(5f, -1f, 0f), tree, 0.3f, 4f), Is.True, "sloping through");
            Assert.That(CinematicMath.SegmentHitsCylinder(new Vector3(-5f, 1f, 0f), new Vector3(-2f, 1f, 0f), tree, 0.3f, 4f), Is.False, "stops short");
            Assert.That(CinematicMath.SegmentHitsCylinder(new Vector3(0.1f, 6f, 0f), new Vector3(0.1f, -1f, 0f), tree, 0.3f, 4f), Is.True, "vertical inside");
        }

        [Test]
        public void TreeBlocks_TrunkAndCrownCoreButNotTheCrownEdge()
        {
            var tree = new Vector3(0f, 0f, 0f);
            Assert.That(CinematicMath.TreeBlocks(new Vector3(-10f, 1f, 0f), new Vector3(10f, 1f, 0f), tree, 10f, 3f, 0.3f), Is.True);
            Assert.That(CinematicMath.TreeBlocks(new Vector3(-10f, 6f, 0f), new Vector3(10f, 6f, 0f), tree, 10f, 3f, 0.3f), Is.True, "crown core");
            Assert.That(CinematicMath.TreeBlocks(new Vector3(-10f, 6f, 2.5f), new Vector3(10f, 6f, 2.5f), tree, 10f, 3f, 0.3f), Is.False, "crown edge lets light through");
            Assert.That(CinematicMath.InsideTree(new Vector3(0.2f, 1f, 0f), 0.2f, tree, 10f, 3f, 0.3f), Is.True);
            Assert.That(CinematicMath.InsideTree(new Vector3(2f, 1f, 0f), 0.2f, tree, 10f, 3f, 0.3f), Is.False, "under the crown, beside the trunk");
            Assert.That(CinematicMath.InsideTree(new Vector3(1f, 6f, 0f), 0.2f, tree, 10f, 3f, 0.3f), Is.True, "in the crown");
        }

        [Test]
        public void TerrainBlocks_AHillBetweenButNotFlatGround()
        {
            Func<float, float, float> hill = (x, z) => Mathf.Max(0f, 6f - Mathf.Abs(z - 10f));
            Assert.That(CinematicMath.TerrainBlocks(new Vector3(0f, 2f, 0f), new Vector3(0f, 1.2f, 20f), hill), Is.True);
            Assert.That(CinematicMath.TerrainBlocks(new Vector3(0f, 2f, 0f), new Vector3(0f, 1.2f, 20f), (x, z) => 0f), Is.False);
            Assert.That(CinematicMath.TerrainBlocks(new Vector3(0f, 12f, 0f), new Vector3(0f, 9f, 20f), hill), Is.False, "over the top");
        }

        [Test]
        public void Clear_LiftsAboveGroundAndWater()
        {
            Assert.That(CinematicMath.Clear(new Vector3(0f, -3f, 0f), 2f, -1f, 0.7f, 0.6f).y, Is.EqualTo(2.7f).Within(1e-5f));
            Assert.That(CinematicMath.Clear(new Vector3(0f, 2f, 0f), 0f, 3f, 0.7f, 0.6f).y, Is.EqualTo(3.6f).Within(1e-5f));
            Assert.That(CinematicMath.Clear(new Vector3(0f, 9f, 0f), 0f, 3f, 0.7f, 0.6f).y, Is.EqualTo(9f));
        }

        [Test]
        public void ClearChance_FallsWithDensityAndLength()
        {
            Assert.That(CinematicMath.ClearChance(0f, 50f, 1f), Is.EqualTo(1f));
            Assert.That(CinematicMath.ClearChance(2f, 10f, 0.6f), Is.GreaterThan(CinematicMath.ClearChance(2f, 30f, 0.6f)));
            Assert.That(CinematicMath.ClearChance(4f, 30f, 0.6f), Is.LessThan(0.6f));
        }

        // ------------------------------------------------------------------ road path

        [Test]
        public void RoadPath_EvaluatesProjectsAndExtrapolates()
        {
            var path = new RoadPath();
            for (int i = 0; i <= 10; i++) path.Add(new Vector3(0f, 0f, i * 2f));
            Assert.That(path.Length, Is.EqualTo(20f).Within(1e-4f));
            Assert.That(path.At(7f).z, Is.EqualTo(7f).Within(1e-4f));
            Assert.That(path.At(-5f).z, Is.EqualTo(-5f).Within(1e-4f), "straight on behind the start");
            Assert.That(path.At(25f).z, Is.EqualTo(25f).Within(1e-4f), "straight on past the end");
            Assert.That(path.Project(new Vector3(3f, 0f, 12.5f)), Is.EqualTo(12.5f).Within(1e-4f));
            Assert.That(path.TangentAt(10f).z, Is.EqualTo(1f).Within(1e-4f));
            path.Shift(new Vector3(256f, 0f, 0f));
            Assert.That(path.At(7f).x, Is.EqualTo(256f).Within(1e-4f));
            path.Clear();
            Assert.That(path.IsUsable, Is.False);
        }

        [Test]
        public void RoadPath_FollowsACorner()
        {
            var path = new RoadPath();
            for (int i = 0; i <= 10; i++) path.Add(new Vector3(0f, 0f, i));
            for (int i = 1; i <= 10; i++) path.Add(new Vector3(i, 0f, 10f));
            Assert.That(path.At(15f).x, Is.EqualTo(5f).Within(1e-4f));
            Assert.That(path.TangentAt(18f, 1f).x, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(path.Project(new Vector3(7f, 0f, 12f)), Is.EqualTo(17f).Within(1e-3f));
        }

        // ------------------------------------------------------------------ shot table and scheduler

        [Test]
        public void ShotTable_DurationsAndLensesAreInRange()
        {
            for (int i = 0; i < ShotLibrary.Count; i++)
            {
                ShotDefinition d = ShotLibrary.Get((ShotType)i);
                Assert.That(d.MinSeconds, Is.GreaterThanOrEqualTo(9f), d.Type.ToString());
                Assert.That(d.MaxSeconds, Is.LessThanOrEqualTo(22f), d.Type.ToString());
                Assert.That(d.MinSeconds, Is.LessThanOrEqualTo(d.MaxSeconds));
                Assert.That(d.FovMin, Is.GreaterThanOrEqualTo(28f), d.Type.ToString());
                Assert.That(d.FovMax, Is.LessThanOrEqualTo(60f), d.Type.ToString());
                Assert.That(d.Type, Is.EqualTo((ShotType)i), "table order matches the enum");
            }

            Assert.That(ShotLibrary.TravelShots.Length + ShotLibrary.RestShots.Length, Is.EqualTo(ShotLibrary.Count));
        }

        [Test]
        public void Scheduler_IsDeterministicForASeed()
        {
            var a = new ShotScheduler(12345);
            var b = new ShotScheduler(12345);
            var context = new ShotContext { Scenery = Scenery.Open, Hour = 10f };
            for (int i = 0; i < 200; i++)
            {
                ShotType ta = a.Next(context);
                ShotType tb = b.Next(context);
                Assert.That(ta, Is.EqualTo(tb));
                Assert.That(a.Duration(ta), Is.EqualTo(b.Duration(tb)));
                a.Commit(ta);
                b.Commit(tb);
            }
        }

        [Test]
        public void Scheduler_NeverRepeatsBackToBackAndUsesEveryTravelShot()
        {
            var scheduler = new ShotScheduler(7);
            var context = new ShotContext { Scenery = Scenery.Open, Hour = 18f, HasVistaSubject = true };
            var seen = new HashSet<ShotType>();
            ShotType? previous = null;
            for (int i = 0; i < 600; i++)
            {
                ShotType t = scheduler.Next(context);
                Assert.That(previous.HasValue && previous.Value == t, Is.False);
                Assert.That(ShotLibrary.Get(t).IsRest, Is.False);
                scheduler.Commit(t);
                previous = t;
                seen.Add(t);
            }

            Assert.That(seen.Count, Is.EqualTo(ShotLibrary.TravelShots.Length));
        }

        [Test]
        public void Scheduler_RestShotsWhenSeatedWithSkyAndFireRules()
        {
            var scheduler = new ShotScheduler(3);
            var noon = new ShotContext { Seated = true, Hour = 12f };
            var counts = Count(scheduler, noon, 400);
            Assert.That(counts.ContainsKey(ShotType.RestSky), Is.False, "no sky hold at noon");
            Assert.That(counts.ContainsKey(ShotType.RestFire), Is.False, "no fire shot without a fire");
            foreach (ShotType t in counts.Keys) Assert.That(ShotLibrary.Get(t).IsRest, Is.True);

            var dusk = new ShotContext { Seated = true, Hour = 18.5f };
            Assert.That(Count(new ShotScheduler(3), dusk, 400).ContainsKey(ShotType.RestSky), Is.True);
            var fireNight = new ShotContext { Seated = true, Hour = 23f, NearFire = true };
            var fire = Count(new ShotScheduler(3), fireNight, 400);
            Assert.That(fire[ShotType.RestFire], Is.GreaterThan(400 / 6), "the fire shot is favoured at night");
        }

        [Test]
        public void Scheduler_WeighsTheCountry()
        {
            var forest = new ShotContext { Scenery = Scenery.Forest, Hour = 11f, HasVistaSubject = true };
            var pass = new ShotContext { Scenery = Scenery.Pass, Hour = 11f, HasVistaSubject = true };
            Assert.That(ShotScheduler.Weight(ShotType.Vista, pass), Is.GreaterThan(ShotScheduler.Weight(ShotType.Vista, forest) * 4f));
            Assert.That(ShotScheduler.Weight(ShotType.Crane, forest), Is.LessThan(ShotScheduler.Weight(ShotType.TrailingWide, forest)));
            var lake = new ShotContext { Scenery = Scenery.Lakeside, Hour = 11f };
            Assert.That(ShotScheduler.Weight(ShotType.SideTracking, lake), Is.GreaterThan(ShotScheduler.Weight(ShotType.SideTracking, forest)));
            var night = new ShotContext { Scenery = Scenery.Pass, Hour = 23f, HasVistaSubject = true };
            Assert.That(ShotScheduler.Weight(ShotType.Vista, night), Is.LessThan(ShotScheduler.Weight(ShotType.Vista, pass)));
            var rain = new ShotContext { Scenery = Scenery.Pass, Hour = 11f, Overcast = 1f, HasVistaSubject = true };
            Assert.That(ShotScheduler.Weight(ShotType.Vista, rain), Is.LessThan(ShotScheduler.Weight(ShotType.Vista, pass)));
            Assert.That(ShotScheduler.Weight(ShotType.RestOrbit, pass), Is.EqualTo(0f), "rest shots never while walking");
        }

        [Test]
        public void Context_ClassifiesTheCountry()
        {
            Assert.That(ShotContext.Classify(0.1f, false, 10f, false, false), Is.EqualTo(Scenery.Open));
            Assert.That(ShotContext.Classify(2f, false, 10f, false, false), Is.EqualTo(Scenery.Forest));
            Assert.That(ShotContext.Classify(2f, false, 10f, true, false), Is.EqualTo(Scenery.Lakeside));
            Assert.That(ShotContext.Classify(0f, true, 10f, true, false), Is.EqualTo(Scenery.Pass));
            Assert.That(ShotContext.Classify(0f, false, 120f, false, false), Is.EqualTo(Scenery.Pass));
            Assert.That(ShotContext.Classify(0.5f, false, 80f, false, false), Is.EqualTo(Scenery.Forest), "wooded banks are not a pass");
            Assert.That(ShotContext.Classify(0f, true, 80f, true, true), Is.EqualTo(Scenery.Settlement));
        }

        private static Dictionary<ShotType, int> Count(ShotScheduler scheduler, ShotContext context, int n)
        {
            var counts = new Dictionary<ShotType, int>();
            for (int i = 0; i < n; i++)
            {
                ShotType t = scheduler.Next(context);
                scheduler.Commit(t);
                counts[t] = counts.TryGetValue(t, out int c) ? c + 1 : 1;
            }

            return counts;
        }

        // ------------------------------------------------------------------ solver

        [Test]
        public void Solver_EveryTravelShotFramesTheTravellerOnAnOpenRoad()
        {
            var world = new FakeWorld();
            ShotInputs input = StraightRoad(world);
            var random = new System.Random(5);
            var plan = new ShotPlan();
            foreach (ShotType type in ShotLibrary.TravelShots)
            {
                ShotSolver.Plan(plan, type, 14f, input, random);
                for (float t = 0f; t <= 14f; t += 3.5f)
                {
                    var moved = new ShotInputs();
                    moved.CopyFrom(input);
                    moved.Traveller = new Vector3(0f, 0f, plan.Speed * t);
                    CameraPose pose = ShotSolver.Evaluate(plan, t, moved);
                    Assert.That(pose.Position.y, Is.GreaterThan(0.2f), $"{type} at {t}: above ground");
                    Assert.That(Vector3.Distance(pose.Position, moved.Traveller), Is.LessThan(ShotRules.MaxDistance), $"{type}: near");
                    Assert.That(pose.Fov, Is.InRange(26f, 62f));
                    if (ShotSolver.IsSpent(plan, moved)) continue;
                    // The low-grass shot lets the traveller walk out past the lens at its end, by design.
                    if (type == ShotType.LowGrass && plan.Anchor.z - moved.Traveller.z < 4f) continue;
                    Vector3 screen = CinematicMath.ScreenPosition(pose.Position, pose.LookAt, moved.SubjectPoint, pose.Fov, moved.Aspect);
                    Assert.That(CinematicMath.InFrame(screen, 0.05f), Is.True, $"{type} at {t}s: traveller in frame (screen {screen})");
                }
            }
        }

        [Test]
        public void Solver_ShotsAreWhereTheirNamesSay()
        {
            var world = new FakeWorld();
            ShotInputs input = StraightRoad(world);
            var random = new System.Random(11);
            var plan = new ShotPlan();

            ShotSolver.Plan(plan, ShotType.TrailingWide, 15f, input, random);
            CameraPose trailing = ShotSolver.Evaluate(plan, 0f, input);
            Assert.That(trailing.Position.z, Is.LessThan(-10f), "trailing is behind");
            Assert.That(trailing.Position.y, Is.GreaterThan(4f), "and above");

            ShotSolver.Plan(plan, ShotType.Leading, 15f, input, random);
            CameraPose leading = ShotSolver.Evaluate(plan, 0f, input);
            Assert.That(leading.Position.z, Is.GreaterThan(10f), "leading is ahead");
            Assert.That(Vector3.Dot(leading.Forward, Vector3.back), Is.GreaterThan(0.8f), "looking back");

            ShotSolver.Plan(plan, ShotType.SideTracking, 15f, input, random);
            CameraPose side = ShotSolver.Evaluate(plan, 0f, input);
            Assert.That(Mathf.Abs(side.Position.x), Is.InRange(8.9f, 18.1f), "8-20 m off the road");

            ShotSolver.Plan(plan, ShotType.RoadsideTripod, 15f, input, random);
            CameraPose a = ShotSolver.Evaluate(plan, 0f, input);
            var later = new ShotInputs();
            later.CopyFrom(input);
            later.Traveller = new Vector3(0f, 0f, 20f);
            CameraPose b = ShotSolver.Evaluate(plan, 10f, later);
            Assert.That(Vector3.Distance(a.Position, b.Position), Is.LessThan(1e-4f), "a tripod does not move");
            Assert.That(a.Position.z, Is.GreaterThan(15f), "it stands ahead");
            Assert.That(a.Position.y, Is.LessThan(1.6f), "low");
            later.Traveller = new Vector3(0f, 0f, 80f);
            Assert.That(ShotSolver.IsSpent(plan, later), Is.True, "spent once the traveller is far past");

            ShotSolver.Plan(plan, ShotType.LowGrass, 10f, input, random);
            CameraPose grass = ShotSolver.Evaluate(plan, 0f, input);
            Assert.That(grass.Position.y, Is.LessThan(0.5f), "near the grass");
            later.Traveller = new Vector3(0f, 0f, plan.Anchor.z - 1f);
            CameraPose passing = ShotSolver.Evaluate(plan, 8f, later);
            Assert.That(CinematicMath.AngleBetween(grass.Forward, passing.Forward), Is.LessThan(25f), "the view barely pans while they walk by");
            later.Traveller = new Vector3(0f, 0f, plan.Anchor.z + 3f);
            Assert.That(ShotSolver.IsSpent(plan, later), Is.True, "over once they have walked out past the lens");

            ShotSolver.Plan(plan, ShotType.Crane, 12f, input, random);
            plan.Reverse = false;
            float low = ShotSolver.Evaluate(plan, 0f, input).Position.y;
            float high = ShotSolver.Evaluate(plan, 12f, input).Position.y;
            Assert.That(high - low, Is.GreaterThan(8f), "the crane rises");
        }

        [Test]
        public void Solver_VistaKeepsTheSubjectAndTheTravellerInFrame()
        {
            var world = new FakeWorld();
            ShotInputs input = StraightRoad(world);
            input.HasSubject = true;
            input.Subject = new Vector3(300f, 120f, 600f);
            var plan = new ShotPlan();
            ShotSolver.Plan(plan, ShotType.Vista, 16f, input, new System.Random(2));
            CameraPose pose = ShotSolver.Evaluate(plan, 0f, input);
            Vector3 subject = CinematicMath.ScreenPosition(pose.Position, pose.LookAt, input.Subject, pose.Fov, input.Aspect);
            Vector3 traveller = CinematicMath.ScreenPosition(pose.Position, pose.LookAt, input.SubjectPoint, pose.Fov, input.Aspect);
            Assert.That(CinematicMath.InFrame(subject, 0f), Is.True, $"subject {subject}");
            Assert.That(CinematicMath.InFrame(traveller, 0f), Is.True, $"traveller {traveller}");
            Assert.That(traveller.y, Is.LessThan(subject.y), "the traveller below the distant subject");
        }

        [Test]
        public void Solver_RestShotsFrameTheSeatedTraveller()
        {
            var world = new FakeWorld();
            ShotInputs input = StraightRoad(world);
            input.Seated = true;
            input.Hip = new Vector3(3f, 0.55f, 2f);
            input.Facing = Vector3.right;
            input.HasFire = true;
            input.Fire = new Vector3(4.4f, 0f, 2f);
            input.SunDirection = new Vector3(0.9f, 0.1f, 0f).normalized;
            var plan = new ShotPlan();
            var random = new System.Random(9);
            foreach (ShotType type in ShotLibrary.RestShots)
            {
                ShotSolver.Plan(plan, type, 16f, input, random);
                for (float t = 0f; t <= 16f; t += 4f)
                {
                    CameraPose pose = ShotRules.Clear(ShotSolver.Evaluate(plan, t, input), type, world, out _);
                    Vector3 screen = CinematicMath.ScreenPosition(pose.Position, pose.LookAt, input.SubjectPoint, pose.Fov, input.Aspect);
                    Assert.That(CinematicMath.InFrame(screen, 0f), Is.True, $"{type} at {t}: {screen}");
                    Assert.That(Vector3.Distance(pose.Position, input.Hip), Is.LessThan(25f));
                }
            }

            ShotSolver.Plan(plan, ShotType.RestSky, 16f, input, random);
            Assert.That(ShotSolver.Evaluate(plan, 0f, input).Forward.y, Is.GreaterThan(0.05f), "the sky hold looks up at the horizon");
            ShotSolver.Plan(plan, ShotType.RestFire, 16f, input, random);
            Assert.That(ShotSolver.Evaluate(plan, 0f, input).Position.x, Is.GreaterThan(input.Fire.x), "across the fire");
        }

        [Test]
        public void ChooseSide_LooksAcrossTowardsTheValleyAndAvoidsWalls()
        {
            // Ground falls away to the right (+x): the camera stands on the left and looks across the traveller into the valley.
            var world = new FakeWorld { Height = (x, z) => -0.3f * x };
            ShotInputs input = StraightRoad(world);
            Assert.That(ShotSolver.ChooseSide(input, 0f, 0f, 12f, new System.Random(1)), Is.EqualTo(-1));

            // A rock wall rising to the left: stand on the right instead.
            world.Height = (x, z) => x < -3f ? 20f : 0f;
            Assert.That(ShotSolver.ChooseSide(input, 0f, 0f, 12f, new System.Random(1)), Is.EqualTo(1));

            // A lake on the left: stand on the right, the water behind the traveller.
            world.Height = (x, z) => x < -20f ? -3f : 0f;
            world.Water = -1f;
            var lake = new LakeWorld(world, -2f);
            input.Queries = lake;
            Assert.That(ShotSolver.ChooseSide(input, 0f, 0f, 12f, new System.Random(1)), Is.EqualTo(1));
        }

        private sealed class LakeWorld : ICinematicQueries
        {
            private readonly FakeWorld inner;
            private readonly float level;

            public LakeWorld(FakeWorld inner, float level)
            {
                this.inner = inner;
                this.level = level;
            }

            public float GroundHeight(Vector3 p) => inner.GroundHeight(p);
            public float WaterHeight(Vector3 p) => p.x < -20f ? level : -1f;
            public float TreeDensity(Vector3 p) => 0f;
            public bool SightBlocked(Vector3 from, Vector3 to) => inner.SightBlocked(from, to);
            public bool InsideTree(Vector3 p, float pad) => false;
            public bool InBuiltArea(Vector3 p) => true;
        }

        // ------------------------------------------------------------------ rules

        [Test]
        public void Rules_OpenGroundIsGoodAndAHillOrForestIsNot()
        {
            var world = new FakeWorld();
            ShotInputs input = StraightRoad(world);
            var scratch = new ShotInputs();
            var plan = new ShotPlan();
            ShotSolver.Plan(plan, ShotType.Leading, 12f, input, new System.Random(4));
            Assert.That(ShotRules.CountGood(plan, input, scratch), Is.EqualTo(3));

            // Dense wood on both sides of a clear road: along the road the traveller stays in sight, from the side never.
            for (float z = -40f; z <= 80f; z += 3f)
            {
                for (float x = -30f; x <= 30f; x += 3f)
                {
                    if (Mathf.Abs(x) > 4f) world.Trees.Add((new Vector3(x, 0f, z), 10f, 3f, 0.4f));
                }
            }

            Assert.That(ShotRules.CountGood(plan, input, scratch), Is.EqualTo(3), "a leading shot down the road corridor");
            ShotSolver.Plan(plan, ShotType.SideTracking, 12f, input, new System.Random(4));
            Assert.That(ShotRules.CountGood(plan, input, scratch), Is.LessThan(2), "a side shot from inside the wood");
        }

        [Test]
        public void Rules_PlaceStaysInTheBuiltWorldNearTheTravellerAndOutOfTrees()
        {
            var world = new FakeWorld { BuiltRadius = 100f };
            world.Trees.Add((new Vector3(5f, 0f, 5f), 10f, 3f, 0.4f));
            Assert.That(ShotRules.PlaceOk(new Vector3(0f, 2f, 0f), Vector3.zero, world), Is.True);
            Assert.That(ShotRules.PlaceOk(new Vector3(0f, 2f, 150f), Vector3.zero, world), Is.False, "beyond the built tiles");
            Assert.That(ShotRules.PlaceOk(new Vector3(0f, 2f, 0f), new Vector3(0f, 0f, 250f), new FakeWorld { BuiltRadius = 1000f }), Is.False, "over 200 m away");
            Assert.That(ShotRules.PlaceOk(new Vector3(5f, 1f, 5f), Vector3.zero, world), Is.False, "inside a trunk");
        }

        [Test]
        public void Rules_BlendWhenNearElseDip()
        {
            var a = new CameraPose { Position = Vector3.zero, LookAt = Vector3.forward * 10f, Fov = 40f };
            var near = new CameraPose { Position = new Vector3(5f, 1f, 0f), LookAt = new Vector3(3f, 0f, 10f), Fov = 40f };
            var far = new CameraPose { Position = new Vector3(40f, 1f, 0f), LookAt = new Vector3(40f, 0f, 10f), Fov = 40f };
            var turned = new CameraPose { Position = new Vector3(2f, 0f, 0f), LookAt = new Vector3(2f, 0f, -10f), Fov = 40f };
            Assert.That(ShotRules.ShouldBlend(a, near), Is.True);
            Assert.That(ShotRules.ShouldBlend(a, far), Is.False);
            Assert.That(ShotRules.ShouldBlend(a, turned), Is.False);
        }

        [Test]
        public void Rules_ClearReportsTheLift()
        {
            var world = new FakeWorld { Height = (x, z) => 10f };
            CameraPose pose = ShotRules.Clear(new CameraPose { Position = new Vector3(0f, 5f, 0f), LookAt = Vector3.forward, Fov = 40f }, ShotType.TrailingWide, world, out float lift);
            Assert.That(pose.Position.y, Is.EqualTo(10.8f).Within(1e-4f));
            Assert.That(lift, Is.EqualTo(5.8f).Within(1e-4f));
        }
    }
}
