using System.Collections.Generic;
using MedievalZen.Roads;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>Tests for the pure (Unity-object-free) world generation stages.</summary>
    public sealed class WorldGenerationTests
    {
        private static WorldGenParams SmallWorld(int seed)
        {
            return new WorldGenParams
            {
                seed = seed,
                worldSize = 1000f,
                heightmapResolution = 257,
                hillAmplitude = 40f,
                townCount = 1,
                villageCount = 2,
                innCount = 1,
                minSettlementSpacing = 300f,
                alphamapResolutionOverride = 256,
            };
        }

        [Test]
        public void Heightmap_SameSeed_IsIdentical_DifferentSeed_Differs()
        {
            float[,] a = HeightmapGenerator.Generate(SmallWorld(1));
            float[,] b = HeightmapGenerator.Generate(SmallWorld(1));
            float[,] c = HeightmapGenerator.Generate(SmallWorld(2));

            Assert.That(a.GetLength(0), Is.EqualTo(257));
            bool anyDifferent = false;
            for (int z = 0; z < a.GetLength(0); z++)
            {
                for (int x = 0; x < a.GetLength(1); x++)
                {
                    Assert.That(a[z, x], Is.EqualTo(b[z, x]));
                    Assert.That(a[z, x], Is.InRange(0f, 1f));
                    if (Mathf.Abs(a[z, x] - c[z, x]) > 1e-3f) anyDifferent = true;
                }
            }

            Assert.That(anyDifferent, Is.True, "Different seeds should give different terrain.");
        }

        [Test]
        public void Settlements_SameSeed_IsIdentical_DifferentSeed_Differs()
        {
            WorldGenParams p = SmallWorld(7);
            List<SettlementSite> a = SettlementPlacer.Place(p, HeightmapGenerator.Generate(p));
            List<SettlementSite> b = SettlementPlacer.Place(p, HeightmapGenerator.Generate(p));
            WorldGenParams q = SmallWorld(8);
            List<SettlementSite> c = SettlementPlacer.Place(q, HeightmapGenerator.Generate(q));

            Assert.That(a.Count, Is.EqualTo(b.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].Name, Is.EqualTo(b[i].Name));
                Assert.That(a[i].Type, Is.EqualTo(b[i].Type));
                Assert.That(a[i].Position, Is.EqualTo(b[i].Position));
            }

            bool anyDifferent = a.Count != c.Count;
            for (int i = 0; i < Mathf.Min(a.Count, c.Count); i++)
            {
                if (a[i].Name != c[i].Name || (a[i].Position - c[i].Position).sqrMagnitude > 1f) anyDifferent = true;
            }

            Assert.That(anyDifferent, Is.True);
        }

        [Test]
        public void Settlements_RespectSpacingAndEdgeMargin([Values(1, 2, 3, 4, 5)] int seed)
        {
            WorldGenParams p = SmallWorld(seed);
            p.worldSize = 2000f;
            p.heightmapResolution = 257;
            p.townCount = 2;
            p.villageCount = 4;
            p.minSettlementSpacing = 350f;
            List<SettlementSite> sites = SettlementPlacer.Place(p, HeightmapGenerator.Generate(p));

            Assert.That(sites.Count, Is.EqualTo(6));
            var names = new HashSet<string>();
            for (int i = 0; i < sites.Count; i++)
            {
                Vector3 s = sites[i].Position;
                Assert.That(names.Add(sites[i].Name), Is.True, "Names must be unique.");
                Assert.That(Mathf.Min(Mathf.Min(s.x, s.z), Mathf.Min(p.worldSize - s.x, p.worldSize - s.z)), Is.GreaterThanOrEqualTo(SettlementPlacer.EdgeMargin));
                for (int j = i + 1; j < sites.Count; j++)
                {
                    Vector3 o = sites[j].Position;
                    float d = new Vector2(s.x - o.x, s.z - o.z).magnitude;
                    Assert.That(d, Is.GreaterThanOrEqualTo(p.minSettlementSpacing), $"{sites[i]} vs {sites[j]}");
                }
            }

            int towns = sites.FindAll(site => site.Type == SettlementType.Town).Count;
            Assert.That(towns, Is.EqualTo(2));
        }

        [Test]
        public void RoadPlanner_FlatWorld_BuildsConnectedNetwork()
        {
            var p = new WorldGenParams { seed = 3, worldSize = 1000f, heightmapResolution = 129, hillAmplitude = 40f, innCount = 0 };
            var map = new float[129, 129];
            var sites = new List<SettlementSite>
            {
                new SettlementSite("Ashford", SettlementType.Town, new Vector3(250f, 0f, 250f)),
                new SettlementSite("Wexley", SettlementType.Village, new Vector3(750f, 0f, 260f)),
                new SettlementSite("Brindlemoor", SettlementType.Village, new Vector3(720f, 0f, 760f)),
                new SettlementSite("Oakham", SettlementType.Village, new Vector3(260f, 0f, 740f)),
            };

            RoadNetwork network = RoadPlanner.PlanNetwork(p, map, sites, out RoadPlan plan);

            Assert.That(network.Settlements.Count, Is.EqualTo(4));
            Assert.That(network.Segments.Count, Is.GreaterThanOrEqualTo(3));
            foreach (RoadSegment segment in network.Segments)
            {
                Assert.That(Vector3.Distance(segment.Points[0], segment.A.Position), Is.LessThan(1e-3f));
                Assert.That(Vector3.Distance(segment.Points[segment.Points.Count - 1], segment.B.Position), Is.LessThan(1e-3f));
            }

            foreach (Settlement from in network.Settlements)
            {
                RoadLocation start = network.Project(from.Position);
                Assert.That(start.IsValid, Is.True);
                foreach (Settlement to in network.Settlements)
                {
                    if (to == from) continue;
                    Assert.That(network.TryFindRoute(start, to, out RoadRoute route), Is.True, $"{from} -> {to}");
                    Assert.That(Vector3.Distance(route.Evaluate(route.TotalLength), to.Position), Is.LessThan(1e-2f));
                }
            }

            int roadCells = 0;
            foreach (bool used in plan.RoadCells) if (used) roadCells++;
            Assert.That(roadCells, Is.GreaterThan(0));
        }

        [Test]
        public void RoadPlanner_LongRoad_GetsAnInn()
        {
            var p = new WorldGenParams { seed = 4, worldSize = 1500f, heightmapResolution = 129, innCount = 1, minSettlementSpacing = 350f };
            var map = new float[129, 129];
            var sites = new List<SettlementSite>
            {
                new SettlementSite("Ashford", SettlementType.Town, new Vector3(200f, 0f, 750f)),
                new SettlementSite("Wexley", SettlementType.Village, new Vector3(1300f, 0f, 750f)),
            };

            RoadNetwork network = RoadPlanner.PlanNetwork(p, map, sites, out _);

            Assert.That(network.Settlements.Count, Is.EqualTo(3));
            Settlement inn = network.Settlements[2];
            Assert.That(inn.Type, Is.EqualTo(SettlementType.Inn));
            Assert.That(inn.Node.Segments.Count, Is.EqualTo(2), "The inn should sit on the road, splitting it.");
            Assert.That(network.TryFindRoute(network.Project(network.Settlements[0].Position), network.Settlements[1], out RoadRoute route), Is.True);
        }

        [Test]
        public void Carving_LowersCrossSectionVarianceAlongRoads()
        {
            WorldGenParams p = SmallWorld(11);
            Relief relief = HeightmapGenerator.GenerateRelief(p);
            float[,] map = relief.Map;
            var names = new SettlementNames(p.seed + 7);
            List<SettlementSite> sites = SettlementPlacer.Place(p, relief, names);
            RoadPlan plan = RoadPlanner.Plan(p, relief, sites, names);
            TerrainShaper.FlattenSettlements(map, p, plan.Settlements);
            List<Vector3[]> profiles = RoadPlanner.ComputeProfiles(plan, p, map, out _);
            Assert.That(profiles.Count, Is.GreaterThan(0));

            float before = CrossSectionVariance(map, p, profiles);
            TerrainShaper.CarveRoads(map, p, profiles);
            float after = CrossSectionVariance(map, p, profiles);

            Assert.That(after, Is.LessThan(before * 0.5f), $"before {before}, after {after}");
        }

        [Test]
        public void Scatter_NeverLandsInsideRoadCorridor()
        {
            WorldGenParams p = SmallWorld(21);
            WorldBlueprint bp = WorldGenerator.Generate(p, PaletteKeys.Default);

            Assert.That(bp.Roads.Segments.Count, Is.GreaterThan(0));
            Assert.That(bp.Trees.Count, Is.GreaterThan(100));
            float corridor = p.ScatterCorridor;
            foreach (TreePlacement tree in bp.Trees)
            {
                Assert.That(DistanceToRoads(bp.Roads, tree.position), Is.GreaterThanOrEqualTo(corridor), $"Tree at {tree.position}");
            }

            foreach (PropPlacement prop in bp.Props)
            {
                if (prop.settlementId >= 0) continue;
                Assert.That(DistanceToRoads(bp.Roads, prop.position), Is.GreaterThanOrEqualTo(corridor), $"{prop.role} at {prop.position}");
            }
        }

        [Test]
        public void Generate_IsDeterministic()
        {
            WorldBlueprint a = WorldGenerator.Generate(SmallWorld(5), PaletteKeys.Default);
            WorldBlueprint b = WorldGenerator.Generate(SmallWorld(5), PaletteKeys.Default);

            Assert.That(a.Trees.Count, Is.EqualTo(b.Trees.Count));
            Assert.That(a.Props.Count, Is.EqualTo(b.Props.Count));
            Assert.That(a.Roads.Segments.Count, Is.EqualTo(b.Roads.Segments.Count));
            for (int i = 0; i < a.Trees.Count; i += 97) Assert.That(a.Trees[i].position, Is.EqualTo(b.Trees[i].position));
            for (int i = 0; i < a.Props.Count; i += 13) Assert.That(a.Props[i].position, Is.EqualTo(b.Props[i].position));
            Assert.That(a.DetailLayers.Count, Is.EqualTo(b.DetailLayers.Count));
            for (int l = 0; l < a.DetailLayers.Count; l++) Assert.That(a.DetailLayers[l].density, Is.EqualTo(b.DetailLayers[l].density));
            Assert.That(a.Heightmap, Is.EqualTo(b.Heightmap));
        }


        // ------------------------------------------------------------------ default-size world (seed 42)

        private static WorldBlueprint defaultWorld;

        /// <summary>The default 2 km world for seed 42 (heightmap 513 to keep the tests quick), generated once.</summary>
        private static WorldBlueprint DefaultWorld42
        {
            get
            {
                if (defaultWorld == null)
                {
                    var p = new WorldGenParams { seed = 42, heightmapResolution = 513 };
                    defaultWorld = WorldGenerator.Generate(p, PaletteKeys.Default);
                }

                return defaultWorld;
            }
        }

        [Test]
        public void Relief_DefaultSettings_HasMountainsValleysAndCliffs()
        {
            WorldBlueprint bp = DefaultWorld42;
            WorldGenParams p = bp.Params;
            float[,] map = bp.Heightmap;
            int n = map.GetLength(0);
            float spacing = p.worldSize / (n - 1);
            int low = 0, high = 0, steep = 0, total = 0;
            for (int z = 2; z < n - 2; z += 4)
            {
                for (int x = 2; x < n - 2; x += 4)
                {
                    float h = map[z, x] * p.hillAmplitude;
                    total++;
                    if (h < 0.25f * p.hillAmplitude) low++;
                    if (h > 0.6f * p.hillAmplitude) high++;
                    if (HeightmapGenerator.SampleSlope(map, p.worldSize, p.hillAmplitude, x * spacing, z * spacing) > 0.7f) steep++;
                }
            }

            Assert.That(low / (float)total, Is.GreaterThan(0.3f), "A broad lowland valley region is expected.");
            Assert.That(high / (float)total, Is.GreaterThan(0.03f), "A highland / mountain region is expected.");
            Assert.That(steep / (float)total, Is.GreaterThan(0.01f), "Some cliffs / escarpments (> 35 degrees) are expected.");
        }

        [Test]
        public void Lakes_DefaultSettingsSeed42_HaveWaterAndStayClearOfSettlementsAndRoads()
        {
            WorldBlueprint bp = DefaultWorld42;
            WorldGenParams p = bp.Params;
            Assert.That(bp.WaterHeight, Is.GreaterThan(0f), "The default lake coverage should produce lakes.");
            Assert.That(bp.Landmarks.Exists(l => l.kind == LandmarkKind.Lake), Is.True, "At least one named lake.");
            Assert.That(bp.Landmarks.FindAll(l => l.kind == LandmarkKind.Peak).Count, Is.EqualTo(1), "Exactly one named peak.");

            foreach (SettlementSite site in bp.Settlements)
            {
                float h = HeightmapGenerator.SampleHeight(bp.Heightmap, p, site.Position.x, site.Position.z);
                Assert.That(h, Is.GreaterThan(bp.WaterHeight + 0.5f), $"{site} is flooded.");
            }

            int points = 0, wet = 0;
            foreach (RoadSegment segment in bp.Roads.Segments)
            {
                foreach (Vector3 point in segment.Points)
                {
                    points++;
                    if (HeightmapGenerator.SampleHeight(bp.Heightmap, p, point.x, point.z) < bp.WaterHeight - 0.25f) wet++;
                }
            }

            Assert.That(wet, Is.LessThanOrEqualTo(points / 100), $"{wet} of {points} road points are under water.");
        }

        [Test]
        public void Roads_DefaultWorld_ConnectEverySettlementPair()
        {
            RoadNetwork network = DefaultWorld42.Roads;
            Assert.That(network.Settlements.Count, Is.GreaterThanOrEqualTo(5));
            foreach (RoadSegment segment in network.Segments)
            {
                Assert.That(Vector3.Distance(segment.Points[0], segment.A.Position), Is.LessThan(1e-3f));
                Assert.That(Vector3.Distance(segment.Points[segment.Points.Count - 1], segment.B.Position), Is.LessThan(1e-3f));
            }

            foreach (Settlement from in network.Settlements)
            {
                RoadLocation start = network.Project(from.Position);
                foreach (Settlement to in network.Settlements)
                {
                    if (to == from) continue;
                    Assert.That(network.TryFindRoute(start, to, out _), Is.True, $"{from} -> {to}");
                }
            }
        }

        [Test]
        public void Scatter_DefaultWorld_AvoidsWaterExceptLilies()
        {
            WorldBlueprint bp = DefaultWorld42;
            WorldGenParams p = bp.Params;
            Assert.That(bp.Trees.Count, Is.InRange(8000, 30000), "Tree count for a 2 km map.");
            foreach (TreePlacement tree in bp.Trees)
            {
                float h = HeightmapGenerator.SampleHeight(bp.Heightmap, p, tree.position.x, tree.position.z);
                Assert.That(h, Is.GreaterThanOrEqualTo(bp.WaterHeight), $"Tree in a lake at {tree.position}");
            }

            foreach (PropPlacement prop in bp.Props)
            {
                float h = HeightmapGenerator.SampleHeight(bp.Heightmap, p, prop.position.x, prop.position.z);
                if (prop.role == PropRole.Lilies)
                {
                    Assert.That(h, Is.LessThan(bp.WaterHeight), "Lilies float on water.");
                    Assert.That(prop.position.y, Is.EqualTo(bp.WaterHeight).Within(1e-3f));
                }
                else if (prop.settlementId < 0 && prop.role != PropRole.WaterWheel)
                {
                    Assert.That(h, Is.GreaterThanOrEqualTo(bp.WaterHeight - 0.05f), $"{prop.role} under water at {prop.position}");
                }
            }
        }

        [Test]
        public void Scatter_DefaultWorld_UsesTheWholeInventory()
        {
            WorldBlueprint bp = DefaultWorld42;
            var families = new HashSet<string>();
            foreach (PropPlacement prop in bp.Props) families.Add(prop.role);
            foreach (TreePlacement tree in bp.Trees) families.Add(tree.role);
            string[] expected =
            {
                PropRole.ForestTree, PropRole.MeadowTree, PropRole.FruitTree, PropRole.PineTree, PropRole.Bush, PropRole.Rock,
                PropRole.Cliff, PropRole.Mushroom, PropRole.Leaves, PropRole.Cabin, PropRole.Windmill, PropRole.Well,
                PropRole.CropField, PropRole.Rapeseed, PropRole.Fence, PropRole.StoneWall, PropRole.Lilies,
            };

            foreach (string family in expected)
            {
                Assert.That(families.Contains(family), Is.True, "No " + family + " placed.");
            }

            Assert.That(bp.DetailLayers.Count, Is.GreaterThan(10));
            Assert.That(bp.Horizon.Count, Is.GreaterThan(8));
        }

        [Test]
        public void Splat_SumsToOne_AndDetailsStayOffRoads()
        {
            WorldBlueprint bp = DefaultWorld42;
            float[,,] splat = bp.Splat;
            int res = splat.GetLength(0);
            for (int z = 0; z < res; z += 7)
            {
                for (int x = 0; x < res; x += 7)
                {
                    float sum = 0f;
                    for (int l = 0; l < splat.GetLength(2); l++) sum += splat[z, x, l];
                    Assert.That(sum, Is.EqualTo(1f).Within(1e-3f));
                }
            }

            float half = bp.Params.roadWidth * 0.5f;
            foreach (DetailLayer layer in bp.DetailLayers)
            {
                Assert.That(layer.density.Length, Is.EqualTo(bp.DetailResolution * bp.DetailResolution));
                for (int z = 0; z < bp.DetailResolution; z++)
                {
                    for (int x = 0; x < bp.DetailResolution; x++)
                    {
                        if (bp.RoadDistance[x, z] < half && layer.density[z * bp.DetailResolution + x] != 0)
                        {
                            Assert.Fail($"{layer.family} detail on the road at texel ({x}, {z}).");
                        }
                    }
                }
            }
        }

        [Test]
        public void Resolutions_DefaultWorld_SplatIsOneMetre_CoverGridStaysTwoMetres()
        {
            var p = new WorldGenParams();
            Assert.That(p.AlphamapResolution, Is.EqualTo(2048));
            Assert.That(p.CoverResolution, Is.EqualTo(1024));
            p.alphamapResolutionOverride = 1024;
            Assert.That(p.AlphamapResolution, Is.EqualTo(1024));
            Assert.That(p.CoverResolution, Is.EqualTo(1024));
            p.alphamapResolutionOverride = 256;
            Assert.That(p.CoverResolution, Is.EqualTo(256), "The cover grid is never finer than the splat.");

            WorldBlueprint bp = DefaultWorld42;
            Assert.That(bp.Splat.GetLength(0), Is.EqualTo(bp.Params.AlphamapResolution));
            Assert.That(bp.Cover.Resolution, Is.EqualTo(bp.Params.CoverResolution));
            Assert.That(bp.DetailResolution, Is.EqualTo(bp.Params.CoverResolution));
        }

        [Test]
        public void Splat_RoadCoreIsSolidDirt_WithAWornVerge()
        {
            WorldBlueprint bp = DefaultWorld42;
            WorldGenParams p = bp.Params;
            float[,,] splat = bp.Splat;
            int res = splat.GetLength(0);
            RoadDistanceField roads = RoadDistanceField.Build(TerrainShaper.CenterLines(bp.Roads), res, p.worldSize, 30f);
            float half = p.roadWidth * 0.5f;
            float core = half - TerrainShaper.RoadCoreInset;
            Assert.That(core * 2f, Is.GreaterThanOrEqualTo(p.roadWidth - 1f - 1e-4f), "Solid dirt across at least roadWidth - 1 m.");

            int coreTexels = 0, vergeTexels = 0;
            double vergeLush = 0.0, farLush = 0.0;
            int farTexels = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    if (splat[z, x, TerrainShaper.CobbleLayer] > 0f) continue;   // town squares are cobbled
                    float d = roads[x, z];
                    if (d <= core)
                    {
                        coreTexels++;
                        Assert.That(splat[z, x, TerrainShaper.DirtLayer], Is.GreaterThanOrEqualTo(0.95f), $"Road texel ({x}, {z}) at {d:0.00} m from the centre line.");
                    }
                    else if (d > half + 0.5f && d < half + 1.5f)
                    {
                        vergeTexels++;
                        vergeLush += splat[z, x, TerrainShaper.LushGrassLayer];
                    }
                    else if (d > half + 12f && d < half + 20f)
                    {
                        farTexels++;
                        farLush += splat[z, x, TerrainShaper.LushGrassLayer];
                    }
                }
            }

            Assert.That(coreTexels, Is.GreaterThan(5000), "The default world should have kilometres of road.");
            Assert.That(vergeTexels, Is.GreaterThan(1000));
            Assert.That(vergeLush / vergeTexels, Is.LessThan(0.6 * farLush / farTexels), "The verge should be visibly worn (less lush grass than the fields beside the road).");
        }

        [Test]
        public void Splat_SlopesSteeperThan40Degrees_AreRock_AndWeightsSumToOne()
        {
            WorldBlueprint bp = DefaultWorld42;
            WorldGenParams p = bp.Params;
            float[,,] splat = bp.Splat;
            int res = splat.GetLength(0);
            float texel = p.worldSize / res;
            RoadDistanceField roads = RoadDistanceField.Build(TerrainShaper.CenterLines(bp.Roads), res, p.worldSize, 30f);
            float roadClear = p.roadWidth * 0.5f + TerrainShaper.RoadVergeWidth + 0.5f;
            float tan40 = Mathf.Tan(40f * Mathf.Deg2Rad);
            Assert.That(TerrainShaper.RockSlopeFull, Is.EqualTo(tan40).Within(1e-3f), "Slopes are rise over run: 40 degrees = tan 40.");
            Assert.That(TerrainShaper.RockSlopeStart, Is.EqualTo(Mathf.Tan(30f * Mathf.Deg2Rad)).Within(1e-3f));

            int steep = 0, gentleRock = 0, gentle = 0;
            for (int z = 0; z < res; z += 3)
            {
                float wz = (z + 0.5f) * texel;
                for (int x = 0; x < res; x += 3)
                {
                    float sum = 0f;
                    for (int l = 0; l < TerrainShaper.LayerCount; l++) sum += splat[z, x, l];
                    Assert.That(sum, Is.EqualTo(1f).Within(1e-3f));

                    if (roads[x, z] < roadClear || splat[z, x, TerrainShaper.CobbleLayer] > 0f) continue;
                    float wx = (x + 0.5f) * texel;
                    float slope = bp.Cover.SampleSlope(wx, wz);
                    if (slope > tan40)
                    {
                        steep++;
                        Assert.That(splat[z, x, TerrainShaper.RockLayer], Is.GreaterThanOrEqualTo(0.9f), $"Texel ({x}, {z}) is {Mathf.Atan(slope) * Mathf.Rad2Deg:0} degrees steep.");
                    }
                    else if (slope < 0.3f && bp.Cover.Sample(wx, wz).height < 0.6f * p.hillAmplitude)
                    {
                        gentle++;
                        if (splat[z, x, TerrainShaper.RockLayer] > 0.5f) gentleRock++;
                    }
                }
            }

            Assert.That(steep, Is.GreaterThan(500), "The default relief should have escarpments / cliffs steeper than 40 degrees.");
            Assert.That(gentleRock, Is.LessThan(gentle / 100 + 1), "Gentle lowland ground should not be painted as rock.");
        }

        [Test]
        public void Resample_BoxAveragesAndKeepsSumToOne()
        {
            var fine = new float[4, 4, 2];
            for (int z = 0; z < 4; z++)
            {
                for (int x = 0; x < 4; x++)
                {
                    fine[z, x, 0] = (x + z) % 2 == 0 ? 1f : 0.5f;
                    fine[z, x, 1] = 1f - fine[z, x, 0];
                }
            }

            Assert.That(TerrainShaper.Resample(fine, 4), Is.SameAs(fine));
            float[,,] coarse = TerrainShaper.Resample(fine, 2);
            for (int z = 0; z < 2; z++)
            {
                for (int x = 0; x < 2; x++)
                {
                    Assert.That(coarse[z, x, 0], Is.EqualTo(0.75f).Within(1e-5f));
                    Assert.That(coarse[z, x, 0] + coarse[z, x, 1], Is.EqualTo(1f).Within(1e-5f));
                }
            }
        }

        private static float CrossSectionVariance(float[,] map, WorldGenParams p, List<Vector3[]> profiles)
        {
            double total = 0.0;
            int samples = 0;
            float half = p.roadWidth * 0.5f;
            foreach (Vector3[] road in profiles)
            {
                for (int i = 1; i < road.Length; i += 5)
                {
                    Vector3 dir = road[i] - road[i - 1];
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    dir.Normalize();
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    double sum = 0.0;
                    double sumSqr = 0.0;
                    const int Steps = 5;
                    for (int k = -Steps; k <= Steps; k++)
                    {
                        Vector3 pt = road[i] + side * (half * k / Steps);
                        double h = HeightmapGenerator.SampleHeight(map, p, pt.x, pt.z);
                        sum += h;
                        sumSqr += h * h;
                    }

                    int n = 2 * Steps + 1;
                    double mean = sum / n;
                    total += sumSqr / n - mean * mean;
                    samples++;
                }
            }

            return samples > 0 ? (float)(total / samples) : 0f;
        }

        private static float DistanceToRoads(RoadNetwork network, Vector3 position)
        {
            var flat = new Vector2(position.x, position.z);
            float best = float.PositiveInfinity;
            foreach (RoadSegment segment in network.Segments)
            {
                IReadOnlyList<Vector3> points = segment.Points;
                for (int i = 1; i < points.Count; i++)
                {
                    var a = new Vector2(points[i - 1].x, points[i - 1].z);
                    var b = new Vector2(points[i].x, points[i].z);
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(flat - a, ab) / ab.sqrMagnitude) : 0f;
                    float d = (flat - (a + ab * t)).magnitude;
                    if (d < best) best = d;
                }
            }

            return best;
        }
    }
}
