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
    /// <summary>
    /// The endless road's inns, hamlets and villages and their people (2026-10-05): dressings are deterministic; the tavern (and the refuge's hut)
    /// turns its door to the road; nothing stands on the road or its verge; the arch house never spans the road (its opening is too narrow);
    /// houses vary (never the same preset twice in a row); every place has its cast (an innkeeper at each inn, the smith at a smithy), within
    /// the caps; people never step on the road nor into walls; the settlement waypoints are those of before (pinned hashes) and every inn has
    /// exactly one building its rest point goes to; distant tiles draw the presets as measured solids. Pure (no Unity objects).
    /// </summary>
    public sealed class EndlessVillageTests
    {
        private static readonly Dictionary<int, EndlessGenerator> Generators = new Dictionary<int, EndlessGenerator>();

        private static EndlessGenerator Gen(int seed)
        {
            lock (Generators)
            {
                if (!Generators.TryGetValue(seed, out EndlessGenerator g))
                {
                    g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
                    Generators[seed] = g;
                }

                return g;
            }
        }

        private static List<Place> Settlements(EndlessGenerator g, double s0, double s1)
        {
            var places = new List<Place>();
            g.Plan.Inns(s0, s1, places);
            g.Plan.RegionPlaces(s0, s1, places);
            return places.Where(p => p.IsSettlement && p.S >= s0 && p.S <= s1).OrderBy(p => p.S).ToList();
        }

        private static double RoadDistance(EndlessGenerator g, double x, double z)
        {
            g.Curve.Nearest(x, z, out double d);
            return d;
        }

        // ------------------------------------------------------------------ dressings

        [Test]
        public void Dressings_AreDeterministic()
        {
            var a = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            var b = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            List<Place> places = Settlements(a, -100.0, 26000.0);
            Assert.That(places.Any(p => p.Kind == PlaceKind.Inn), Is.True);
            Assert.That(places.Any(p => p.Kind != PlaceKind.Inn), Is.True, "a hamlet or village within 26 km");
            // The second generator dresses the places in the opposite order.
            var other = Settlements(b, -100.0, 26000.0);
            var reversed = other.AsEnumerable().Reverse().Select(p => b.Dressings.Settlement(p)).ToList();
            reversed.Reverse();
            for (int i = 0; i < places.Count; i++)
            {
                Dressing x = a.Dressings.Settlement(places[i]), y = reversed[i];
                Assert.That(y.Props.Count, Is.EqualTo(x.Props.Count), places[i].Name);
                for (int k = 0; k < x.Props.Count; k++)
                {
                    Assert.That(y.Props[k].role, Is.EqualTo(x.Props[k].role));
                    Assert.That(y.Props[k].variant, Is.EqualTo(x.Props[k].variant));
                    Assert.That(y.Props[k].position, Is.EqualTo(x.Props[k].position));
                    Assert.That(y.Props[k].yaw, Is.EqualTo(x.Props[k].yaw));
                }

                Assert.That(y.Anchors.Count, Is.EqualTo(x.Anchors.Count));
            }
        }

        [TestCase(12345)]
        [TestCase(42)]
        public void Inns_AreTaverns_WithTheDoorToTheRoad(int seed)
        {
            EndlessGenerator g = Gen(seed);
            int taverns = 0, refuges = 0;
            foreach (Place place in Settlements(g, -100.0, 20000.0).Where(p => p.Kind == PlaceKind.Inn))
            {
                Dressing d = g.Dressings.Settlement(place);
                List<PropPlacement> inn = d.Props.Where(p => InnComposer.IsRestBuilding(p.role)).ToList();   // inn archetypes: a house may be the inn
                Assert.That(inn.Count, Is.EqualTo(1), $"{place.Name}: exactly one building takes the inn's rest point");
                PropPlacement building = inn[0];
                Assert.That(building.role, place.Refuge ? Is.EqualTo(PropRole.Hut) : Is.EqualTo(PropRole.Tavern).Or.EqualTo(PropRole.House), place.Name);
                if (place.Refuge) refuges++;
                else taverns++;
                BuildingShape shape = BuildingTable.For(building.role, building.variant);
                Vector3 door = BuildingTable.ToWorld(building.position, building.yaw, shape.Door);
                Vector3 outward = BuildingTable.Direction(building.yaw, shape.Outward);
                double ax = d.X0 + door.x, az = d.Z0 + door.z;
                double s = g.NearestS(ax, az, place.S);
                RoadPoint rp = g.Road(s);
                var toRoad = new Vector3((float)(rp.X - ax), 0f, (float)(rp.Z - az)).normalized;
                Assert.That(Vector3.Dot(outward, toRoad), Is.GreaterThan(-0.2f), $"{place.Name}: the door faces the road (inn archetypes turn it up to 90 degrees)");
                double gap = RoadDistance(g, ax, az) - g.Params.roadWidth * 0.5;
                Assert.That(gap, Is.InRange(SettlementKit.Verge, 34.0), $"{place.Name}: the door is a short walk from the road (a coaching inn stands behind its yard)");
                PlaceAnchor innDoor = d.Anchors.Single(a => a.Kind == AnchorKind.InnDoor);
                Assert.That(Vector3.Distance(new Vector3(innDoor.Position.x, 0f, innDoor.Position.z), new Vector3(door.x, 0f, door.z)), Is.LessThan(0.01f));
            }

            Assert.That(taverns, Is.GreaterThanOrEqualTo(6));
            Assert.That(refuges, Is.GreaterThanOrEqualTo(seed == 12345 ? 1 : 0));
        }

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void NothingStands_OnTheRoadOrItsVerge(int seed)
        {
            EndlessGenerator g = Gen(seed);
            float half = g.Params.roadWidth * 0.5f;
            foreach (Place place in Settlements(g, -100.0, 26000.0))
            {
                Dressing d = g.Dressings.Settlement(place);
                if (d.Anchors.Count == 0) continue;   // dressed by the shared dresser (no palette tavern): not this composer's
                foreach (PropPlacement prop in d.Props)
                {
                    // A city gate's span crosses above the road by design: a piece whose underside clears the road by 4.5 m stands on nothing there.
                    if (PropRole.IsCityPiece(prop.role) && prop.position.y - g.Road(g.NearestS(d.X0 + prop.position.x, d.Z0 + prop.position.z, place.S)).Height >= CityComposer.MinGateClearance) continue;
                    BuildingShape shape = PropRole.IsBuilding(prop.role) ? BuildingTable.For(prop.role, prop.variant) : null;
                    if (shape != null)
                    {
                        foreach (Vector3 corner in shape.ExtentOutline())
                        {
                            Vector3 w = BuildingTable.ToWorld(prop.position, prop.yaw, corner);
                            Assert.That(RoadDistance(g, d.X0 + w.x, d.Z0 + w.z), Is.GreaterThanOrEqualTo(half + SettlementKit.Verge - 0.05),
                                $"{place.Name}: {prop.role} reaches the verge");
                        }

                        continue;
                    }

                    // The crop fields round a village are the shared field planner's (its own rules); the composer's props lie within reach.
                    double cx = d.X0 + prop.position.x - d.CentreX, cz = d.Z0 + prop.position.z - d.CentreZ;
                    if (prop.role == PropRole.CropField || prop.role == PropRole.Rapeseed || prop.role == PropRole.Sunflower || prop.role == PropRole.Scarecrow) continue;
                    if (place.Kind != PlaceKind.Inn && Math.Sqrt(cx * cx + cz * cz) > place.Radius + 45.0) continue;
                    Assert.That(RoadDistance(g, d.X0 + prop.position.x, d.Z0 + prop.position.z), Is.GreaterThanOrEqualTo(half + SettlementKit.Verge - 0.05),
                        $"{place.Name}: {prop.role} at the road");
                }

                foreach (TreePlacement tree in d.Trees)
                {
                    Assert.That(RoadDistance(g, d.X0 + tree.position.x, d.Z0 + tree.position.z), Is.GreaterThanOrEqualTo(half + SettlementKit.Verge));
                }

                Assert.That(d.Props.Any(p => p.role == PropRole.StoneArch), Is.False, $"{place.Name}: no stone arch standing in the road");
            }
        }

        [Test]
        public void ArchHouse_StandsBesideTheRoad_ItsOpeningIsTooNarrow()
        {
            // Measured: the lane through House_Archway_02 is 4.3 m wide and 3.3 m high; the road is 5 m.
            Assert.That(BuildingTable.ArchOpeningZ1 - BuildingTable.ArchOpeningZ0, Is.EqualTo(4.3f).Within(0.01f));
            Assert.That(BuildingTable.ArchClearsRoad(Gen(12345).Params.roadWidth, 0.5f), Is.False);
            int found = 0;
            foreach (int seed in new[] { 12345, 42, 777 })
            {
                EndlessGenerator g = Gen(seed);
                foreach (Place place in Settlements(g, -100.0, 26000.0).Where(p => p.Kind == PlaceKind.Village))
                {
                    Dressing d = g.Dressings.Settlement(place);
                    foreach (PropPlacement prop in d.Props.Where(p => p.role == PropRole.Gatehouse))
                    {
                        found++;
                        // The lane's middle, both ends of it, keep off the road.
                        float laneZ = (BuildingTable.ArchOpeningZ0 + BuildingTable.ArchOpeningZ1) * 0.5f;
                        BuildingShape s = BuildingTable.For(prop.role, prop.variant);
                        foreach (float x in new[] { s.ExtentX0, s.ExtentX1 })
                        {
                            Vector3 w = BuildingTable.ToWorld(prop.position, prop.yaw, new Vector3(x, 0f, laneZ));
                            Assert.That(RoadDistance(g, d.X0 + w.x, d.Z0 + w.z), Is.GreaterThan(g.Params.roadWidth * 0.5 + SettlementKit.Verge - 0.05));
                        }
                    }
                }
            }

            Assert.That(found, Is.GreaterThanOrEqualTo(1), "an arch house in some village of three seeds");
        }

        [TestCase(12345)]
        [TestCase(777)]
        public void Houses_Vary_NeverTheSamePresetTwiceInARow(int seed)
        {
            EndlessGenerator g = Gen(seed);
            foreach (Place place in Settlements(g, -100.0, 26000.0).Where(p => p.Kind != PlaceKind.Inn))
            {
                Dressing d = g.Dressings.Settlement(place);
                List<PropPlacement> houses = d.Props.Where(p => p.role == PropRole.House).ToList();
                // A hamlet is three or four farmsteads (a house and its barn) and a house or two on the road.
                Assert.That(houses.Count, Is.GreaterThanOrEqualTo(place.Kind == PlaceKind.Village ? 10 : 3), place.Name);
                Assert.That(houses.Select(h => h.variant).Distinct().Count(), Is.GreaterThanOrEqualTo(place.Kind == PlaceKind.Village ? 5 : 3), $"{place.Name}: several presets");
                for (int i = 1; i < houses.Count; i++)
                {
                    Assert.That(houses[i].variant, Is.Not.EqualTo(houses[i - 1].variant), $"{place.Name}: house {i} repeats its neighbour's preset");
                }

                if (place.Kind == PlaceKind.Village)
                {
                    Assert.That(d.Props.Count(p => p.role == PropRole.Smithy), Is.EqualTo(1), $"{place.Name}: a smithy");
                    Assert.That(d.Props.Count(p => p.role == PropRole.Well), Is.GreaterThanOrEqualTo(1));
                    Assert.That(d.Props.Count(p => p.role == PropRole.Bench), Is.GreaterThanOrEqualTo(2));
                }

                // Every dwelling has a door anchor (where its people start).
                int dwellings = d.Props.Count(p => PropRole.IsDwelling(p.role));
                Assert.That(d.Anchors.Count(a => a.Kind == AnchorKind.Door), Is.EqualTo(dwellings), place.Name);
            }
        }

        [Test]
        public void SettlementWaypoints_AreThoseOfBefore()
        {
            // Hashes of every settlement waypoint (id, kind, road distance, position, height, radius, name; 0.01 m) between -2 and 30 km,
            // computed with the code before this change: the villages and inns are dressed anew, but they are where they were.
            // Walled cities (2026-10-06) take the village slots of Durncaster (12345) and Carlbridge (42): their kind, radius and name differ.
            // Per runtime: under Unity's Mono (float arithmetic evaluated in double) the road frame puts the inns up to 0.9 mm away from
            // where CoreCLR puts them, and seed 42's Rusty Goose (Z 19460.595 m) then rounds to the other centimetre. Both sets were
            // recorded 2026-10-06 by running each runtime (Mono: the editor's own runtime, see Docs/DESIGN.md "Golden hashes under Mono").
            bool mono = Type.GetType("Mono.Runtime") != null;
            var expected = new Dictionary<int, ulong> { [12345] = 0xFE9F96FDC9D8BD65UL, [42] = mono ? 0x9D4F12E3DDBA6A75UL : 0x202D7950775B1B5AUL, [777] = 0x35F09E4B18571BABUL };
            foreach (KeyValuePair<int, ulong> pair in expected)
            {
                var list = new List<Waypoint>();
                Gen(pair.Key).Waypoints(-2000.0, 30000.0, list);
                ulong h = 1469598103934665603UL;
                int n = 0;
                foreach (Waypoint w in list)
                {
                    if (!w.IsSettlement) continue;
                    n++;
                    foreach (long v in new[] { (long)w.Id, (long)w.Kind, (long)Math.Round(w.S * 100.0), (long)Math.Round(w.X * 100.0), (long)Math.Round(w.Z * 100.0), (long)Math.Round(w.Height * 100.0), (long)Math.Round(w.Radius * 100.0) })
                    {
                        for (int b = 0; b < 8; b++)
                        {
                            h ^= (byte)(v >> (8 * b));
                            h *= 1099511628211UL;
                        }
                    }

                    foreach (char c in w.Name ?? string.Empty)
                    {
                        h ^= c;
                        h *= 1099511628211UL;
                    }
                }

                Assert.That(n, Is.EqualTo(18), $"seed {pair.Key}");
                Assert.That(h, Is.EqualTo(pair.Value), $"seed {pair.Key}: settlement waypoints changed");
            }
        }

        [Test]
        public void DistantTiles_DrawThePresets_AsMeasuredSolids()
        {
            foreach (string role in new[] { PropRole.Tavern, PropRole.House, PropRole.Hut, PropRole.Smithy, PropRole.Stables, PropRole.Gatehouse, PropRole.Shelter, PropRole.Outhouse })
            {
                Assert.That(TileGenerator.FarHouse(role), Is.True, role);
                Assert.That(FarTileMeshData.IsSolid(role), Is.True, role);
                for (int v = 0; v < BuildingTable.CatalogCount(role); v++)
                {
                    BuildingShape shape = BuildingTable.For(role, v);
                    Assert.That(shape, Is.Not.Null, $"{role} {v} is measured");
                    FarPropStyle style = FarPropStyles.DefaultFor(role, v);
                    Assert.That(style.Kind, Is.EqualTo(FarPropKind.House));
                    Assert.That(style.Max.y, Is.EqualTo(shape.Height));
                    Assert.That(style.Eaves, Is.LessThan(style.Max.y));
                    Assert.That(FarTileMeshData.HouseTrianglesOf(style.Kind), Is.EqualTo(14));
                }
            }
        }

        // ------------------------------------------------------------------ people

        [TestCase(12345)]
        [TestCase(42)]
        public void EveryPlace_HasItsCast_WithinTheCaps(int seed)
        {
            EndlessGenerator g = Gen(seed);
            int people = 0;
            foreach (Place place in Settlements(g, -100.0, 20000.0))
            {
                PlaceCast cast = PeopleSource.CastFor(g, place);
                PlaceCast again = PeopleSource.CastFor(g, place);
                Assert.That(cast, Is.Not.Null);
                Assert.That(again.People.Select(p => p.Character + p.Stops.Length), Is.EqualTo(cast.People.Select(p => p.Character + p.Stops.Length)), "deterministic");
                Assert.That(cast.People.Count, Is.InRange(1, PeopleCatalog.MaxPerPlace), place.Name);
                if (place.Kind == PlaceKind.Inn) Assert.That(cast.People.Count(p => p.Role == PersonRole.Innkeeper), Is.EqualTo(1), $"{place.Name}: an innkeeper");
                Dressing d = g.Dressings.Settlement(place);
                if (d.Props.Any(p => p.role == PropRole.Smithy)) Assert.That(cast.People.Any(p => p.Role == PersonRole.Smith), Is.True, $"{place.Name}: a smith at the smithy");
                if (place.Kind == PlaceKind.Village) Assert.That(cast.People.Count(p => p.Role == PersonRole.Peasant), Is.GreaterThanOrEqualTo(2), place.Name);
                foreach (PersonPlan p in cast.People)
                {
                    Assert.That(PeopleCatalog.All.Any(c => c.Name == p.Character), Is.True, p.Character);
                    Assert.That(p.Stops.Length, Is.GreaterThanOrEqualTo(1));
                }

                people += cast.People.Count;
            }

            Assert.That(people, Is.GreaterThanOrEqualTo(25));
            Assert.That(PeopleCatalog.MaxActive, Is.EqualTo(12));
            Assert.That(PeopleCatalog.ShowWithin, Is.EqualTo(200f));
            Assert.That(PeopleCatalog.AnimateWithin, Is.EqualTo(80f));
        }

        [TestCase(12345)]
        [TestCase(777)]
        public void People_NeverStepOnTheRoad_NorIntoWalls(int seed)
        {
            EndlessGenerator g = Gen(seed);
            float half = g.Params.roadWidth * 0.5f;
            foreach (Place place in Settlements(g, -100.0, 20000.0))
            {
                PlaceInput input = PeopleSource.InputFor(g, place);
                PlaceCast cast = PeoplePlanner.Plan(input);
                foreach (PersonPlan plan in cast.People)
                {
                    var brain = new VillagerBrain(plan);
                    float hour = 4f;
                    for (int k = 0; k < 6000; k++)
                    {
                        // A day in 10 minutes of steps: hours pass quickly, so the person goes home and comes out again.
                        hour += 0.004f;
                        brain.Step(0.1f, hour, double.NaN, double.NaN);
                        if (brain.Indoors) continue;
                        Assert.That(RoadDistance(g, brain.X, brain.Z), Is.GreaterThanOrEqualTo(half + PeoplePlanner.RoadMargin - 0.01), $"{place.Name}: {plan.Role} on the road");
                        foreach (BuildingBody b in input.Bodies) Assert.That(b.Contains(brain.X, brain.Z, 0f), Is.False, $"{place.Name}: {plan.Role} inside a wall");
                    }
                }
            }
        }

        [Test]
        public void Villager_LooksAtTheTraveller_SitsOnBenches_AndGoesHomeAtNight()
        {
            var plan = new PersonPlan
            {
                Character = "SM_Chr_Peasant_Male_01", Role = PersonRole.Peasant, FromHour = 7f, ToHour = 20f, Seed = 5,
                Stops = new[]
                {
                    new RouteStop { X = 0, Z = 0, Facing = 0f, Kind = StopKind.Stand, MinSeconds = 2f, MaxSeconds = 3f },
                    new RouteStop { X = 10, Z = 0, Facing = 90f, Kind = StopKind.Sit, MinSeconds = 5f, MaxSeconds = 6f, SeatHeight = 0.45f },
                },
            };
            var brain = new VillagerBrain(plan);
            bool sat = false, walked = false;
            for (int k = 0; k < 600; k++)
            {
                brain.Step(0.1f, 12f, double.NaN, double.NaN);
                sat |= brain.SitWeight > 0.99f;
                walked |= brain.Speed > 0.5f;
                Assert.That(brain.Z, Is.EqualTo(0.0).Within(1e-6), "stays on the leg");
                Assert.That(brain.X, Is.InRange(-1e-6, 10.0 + 1e-6));
            }

            Assert.That(sat && walked, Is.True);
            // Standing at the first stop with the traveller close by: turns to them.
            var standing = new VillagerBrain(new PersonPlan { Character = plan.Character, FromHour = 0f, ToHour = 24f, Seed = 1, Stops = new[] { plan.Stops[0] } });
            for (int k = 0; k < 40; k++) standing.Step(0.1f, 12f, 3.0, 3.0);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(standing.Yaw, 45f)), Is.LessThan(2f));
            // At night everyone goes home (stop 0) and indoors.
            for (int k = 0; k < 2000 && !brain.Indoors; k++) brain.Step(0.1f, 22f, double.NaN, double.NaN);
            Assert.That(brain.Indoors, Is.True);
            Assert.That(brain.X, Is.EqualTo(0.0).Within(1e-6));
            Assert.That(VillagerBrain.OutAt(23f, 22f, 2f), Is.True, "hours may wrap past midnight");
        }

        [Test]
        public void BuildingTable_IsConsistent()
        {
            foreach (BuildingShape s in BuildingTable.All)
            {
                Assert.That(s.BodyX0, Is.LessThan(s.BodyX1), s.Name);
                Assert.That(s.BodyZ0, Is.LessThan(s.BodyZ1), s.Name);
                Assert.That(s.ExtentX0, Is.LessThanOrEqualTo(s.BodyX0 + 0.01f), s.Name);
                Assert.That(s.ExtentX1, Is.GreaterThanOrEqualTo(s.BodyX1 - 0.01f), s.Name);
                Assert.That(s.ExtentZ0, Is.LessThanOrEqualTo(s.BodyZ0 + 0.01f), s.Name);
                Assert.That(s.ExtentZ1, Is.GreaterThanOrEqualTo(s.BodyZ1 - 0.01f), s.Name);
                Assert.That(s.Height, Is.GreaterThan(s.Eaves), s.Name);
                Assert.That(s.Smoke.y, Is.InRange(s.Eaves, s.Height + 0.5f), s.Name);
                // The door's foot lies outside the walls (people start there).
                (float x0, float x1, float z0, float z1) = s.Walls;
                bool inside = s.Door.x > x0 && s.Door.x < x1 && s.Door.z > z0 && s.Door.z < z1;
                Assert.That(inside, Is.False, $"{s.Name}: the door's foot is outside the walls");
            }

            foreach (var f in SyntyCatalog.Families.Where(f => PropRole.IsBuilding(f.family)))
            {
                for (int v = 0; v < f.paths.Length; v++) Assert.That(BuildingTable.For(f.family, v), Is.Not.Null, $"{f.family} {v}");
            }
        }

        // ------------------------------------------------------------------ village plans (2026-10-06)

        private static readonly int[] PlanSeeds = { 12345, 42, 777 };

        private static List<Place> Villages(EndlessGenerator g, double s0, double s1) =>
            Settlements(g, s0, s1).Where(p => p.Kind == PlaceKind.Village || p.Kind == PlaceKind.Hamlet).ToList();

        private static BuildingShape ShapeOf(PropPlacement p) => p.role == PropRole.LandmarkChapel ? BuildingTable.Church : BuildingTable.For(p.role, p.variant);

        private static List<PropPlacement> Buildings(Dressing d) =>
            d.Props.Where(p => (PropRole.IsBuilding(p.role) || p.role == PropRole.LandmarkChapel) && ShapeOf(p) != null).ToList();

        private static Vector2[] Outline(PropPlacement p)
        {
            BuildingShape s = ShapeOf(p);
            return new[] { new Vector3(s.ExtentX0, 0f, s.ExtentZ0), new Vector3(s.ExtentX1, 0f, s.ExtentZ0), new Vector3(s.ExtentX1, 0f, s.ExtentZ1), new Vector3(s.ExtentX0, 0f, s.ExtentZ1) }
                .Select(c => BuildingTable.ToWorld(p.position, p.yaw, c)).Select(w => new Vector2(w.x, w.z)).ToArray();
        }

        private static bool Overlap(Vector2[] a, Vector2[] b)
        {
            foreach (Vector2[] poly in new[] { a, b })
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector2 e = poly[(i + 1) % 4] - poly[i], n = new Vector2(-e.y, e.x);
                    float a0 = a.Min(q => Vector2.Dot(q, n)), a1 = a.Max(q => Vector2.Dot(q, n)), b0 = b.Min(q => Vector2.Dot(q, n)), b1 = b.Max(q => Vector2.Dot(q, n));
                    if (a1 <= b0 + 1e-3f || b1 <= a0 + 1e-3f) return false;
                }
            }

            return true;
        }

        [Test]
        public void Plans_Vary_NeverTheSameTwiceRunning_StreetsAreFew()
        {
            var counts = new Dictionary<VillagePlan, int>();
            foreach (int seed in PlanSeeds)
            {
                EndlessGenerator g = Gen(seed);
                VillagePlan? last = null;
                foreach (Place place in Villages(g, 0.0, 200000.0))
                {
                    VillagePlan plan = VillageComposer.PlanFor(seed, g.Terrain.Plan, place);
                    Assert.That(plan, Is.EqualTo(VillageComposer.PlanFor(seed, g.Terrain.Plan, place)), "deterministic");
                    Assert.That(plan, Is.Not.EqualTo(last), $"seed {seed} {place.Name}: the same plan as the settlement before it");
                    Assert.That(plan <= VillagePlan.Church, Is.EqualTo(place.Kind == PlaceKind.Village), $"{place.Name}: a {place.Kind} plan");
                    last = plan;
                    counts[plan] = counts.TryGetValue(plan, out int c) ? c + 1 : 1;
                }
            }

            foreach (VillagePlan plan in new[] { VillagePlan.Green, VillagePlan.BackLane, VillagePlan.Church, VillagePlan.FarmYard, VillagePlan.LaneFork, VillagePlan.HamletGreen })
            {
                Assert.That(counts.TryGetValue(plan, out int n) ? n : 0, Is.GreaterThanOrEqualTo(2), $"{plan} occurs");
            }

            int villages = counts.Where(k => k.Key <= VillagePlan.Church).Sum(k => k.Value);
            Assert.That(counts.TryGetValue(VillagePlan.Street, out int streets) ? streets : 0, Is.LessThanOrEqualTo(Math.Max(1, villages / 5)), "the street village is a minority");
        }

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void Layouts_HaveDepth_VariedSetbacksAndTurns_NoOverlaps(int seed)
        {
            EndlessGenerator g = Gen(seed);
            float half = g.Params.roadWidth * 0.5f;
            double shareSum = 0.0;
            int places = 0;
            foreach (Place place in Villages(g, 0.0, 90000.0).Where(p => !p.City))   // a walled city's streets are its own (EndlessCityTests); 90 km for three places
            {
                Dressing d = g.Dressings.Settlement(place);
                List<PropPlacement> buildings = Buildings(d);
                List<PropPlacement> houses = buildings.Where(b => b.role == PropRole.House).ToList();
                for (int a = 0; a < buildings.Count; a++)
                {
                    for (int b = a + 1; b < buildings.Count; b++)
                    {
                        Assert.That(Overlap(Outline(buildings[a]), Outline(buildings[b])), Is.False, $"{place.Name}: {buildings[a].role} overlaps {buildings[b].role}");
                    }
                }

                var setbacks = new List<double>();
                foreach (PropPlacement h in houses)
                {
                    Vector3 door = BuildingTable.ToWorld(h.position, h.yaw, ShapeOf(h).Door);
                    setbacks.Add(RoadDistance(g, d.X0 + door.x, d.Z0 + door.z) - half);
                }

                double depth = 0.0;
                foreach (PropPlacement b in buildings)
                {
                    foreach (Vector2 c in Outline(b)) depth = Math.Max(depth, RoadDistance(g, d.X0 + c.x, d.Z0 + c.y) - half);
                }

                double share = setbacks.Count(x => x < 12.0) / (double)Math.Max(1, houses.Count);
                shareSum += share;
                places++;
                Assert.That(setbacks.Max() - setbacks.Min(), Is.GreaterThan(10.0), $"{place.Name}: setbacks vary");
                Assert.That(depth, Is.GreaterThan(30.0), $"{place.Name}: the place reaches back from the road");
                // Orientation: the buildings' yaws (mod 90) spread over at least three 10-degree bins.
                Assert.That(buildings.Select(h => (int)(((h.yaw % 90f) + 90f) % 90f / 10f)).Distinct().Count(), Is.GreaterThanOrEqualTo(3), $"{place.Name}: orientations vary");
                Assert.That(share, Is.LessThan(0.75), $"{place.Name}: most houses on the road's edge");
            }

            Assert.That(places, Is.GreaterThanOrEqualTo(3));
            Assert.That(shareSum / places, Is.LessThan(0.5), "on average under half the houses within 12 m of the road's edge");
        }

        [TestCase(12345)]
        [TestCase(777)]
        public void EveryDoor_ReachesTheRoad_ByPaths(int seed)
        {
            EndlessGenerator g = Gen(seed);
            float half = g.Params.roadWidth * 0.5f;
            foreach (Place place in Villages(g, 0.0, 60000.0).Where(p => !p.City))   // a city's doors open on cobbles (EndlessCityTests)
            {
                Dressing d = g.Dressings.Settlement(place);
                int n = d.Paths.Count;
                int[] parent = Enumerable.Range(0, n).ToArray();
                int Find(int x)
                {
                    while (parent[x] != x) x = parent[x] = parent[parent[x]];
                    return x;
                }

                float Dist(Vector2 p, Vector4 s)
                {
                    var a = new Vector2(s.x, s.y);
                    var e = new Vector2(s.z - s.x, s.w - s.y);
                    float t = e.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude) : 0f;
                    return (a + e * t - p).magnitude;
                }

                for (int a = 0; a < n; a++)
                {
                    for (int b = 0; b < n; b++)
                    {
                        if (a != b && (Dist(new Vector2(d.Paths[a].x, d.Paths[a].y), d.Paths[b]) < 1f || Dist(new Vector2(d.Paths[a].z, d.Paths[a].w), d.Paths[b]) < 1f)) parent[Find(a)] = Find(b);
                    }
                }

                var rooted = new HashSet<int>();
                for (int a = 0; a < n; a++)
                {
                    Vector4 s = d.Paths[a];
                    if (RoadDistance(g, d.X0 + s.x, d.Z0 + s.y) - half < 2.5 || RoadDistance(g, d.X0 + s.z, d.Z0 + s.w) - half < 2.5) rooted.Add(Find(a));
                }

                int doors = 0, connected = 0;
                foreach (PlaceAnchor door in d.Anchors.Where(a => a.Kind == AnchorKind.Door && a.Building >= 0 && d.Props[a.Building].role != PropRole.Gatehouse))
                {
                    doors++;
                    var p = new Vector2(door.Position.x, door.Position.z);
                    for (int a = 0; a < n; a++)
                    {
                        Vector4 s = d.Paths[a];
                        if (((new Vector2(s.x, s.y) - p).magnitude < 1f || (new Vector2(s.z, s.w) - p).magnitude < 1f) && rooted.Contains(Find(a)))
                        {
                            connected++;
                            break;
                        }
                    }
                }

                Assert.That(connected, Is.GreaterThanOrEqualTo(doors - 1), $"{place.Name}: {connected} of {doors} doors reach the road by paths");
            }
        }

        [Test]
        public void ChurchVillage_HasItsChurch_InAWalledYard_AndTheCastKnowsIt()
        {
            int found = 0;
            foreach (int seed in PlanSeeds)
            {
                EndlessGenerator g = Gen(seed);
                foreach (Place place in Villages(g, 0.0, 200000.0))
                {
                    if (found >= 3) break;
                    if (place.City || VillageComposer.PlanFor(seed, g.Terrain.Plan, place) != VillagePlan.Church) continue;
                    Dressing d = g.Dressings.Settlement(place);
                    List<PropPlacement> churches = d.Props.Where(p => p.role == PropRole.LandmarkChapel).ToList();
                    if (churches.Count == 0) continue;
                    found++;
                    PropPlacement church = churches[0];
                    Assert.That(d.Props.Count(p => p.role == PropRole.Gravestone), Is.GreaterThanOrEqualTo(4), place.Name);
                    Assert.That(d.Props.Count(p => p.role == PropRole.StoneWall && (p.position - church.position).magnitude < 18f), Is.GreaterThanOrEqualTo(8), $"{place.Name}: the churchyard wall");
                    Assert.That(PeopleSource.InputFor(g, place).HasChapel, Is.True, $"{place.Name}: the cast knows its church");
                }
            }

            Assert.That(found, Is.GreaterThanOrEqualTo(2), "church villages with their church");
        }
    }
}
