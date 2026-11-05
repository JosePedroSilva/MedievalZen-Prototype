using System;
using System.Collections.Generic;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Environment;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The endless road's forest pass (2026-10-05): the road runs through the woods (trunks from 5.5 m, tunnels, glades, ancient
    /// trees), a floor of ferns and undergrowth, logs and ivy, the Generic broadleaves and the dark bush restyled near the road, the
    /// shadow proxies, the safety ladder's first rung and the forest mood's pure maths. Pure (no Unity objects).
    /// </summary>
    public sealed class EndlessForestTests
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

        private static Region First(EndlessGenerator g, RegionKind kind)
        {
            Region r = g.Regions.At(0.0);
            while (r.Kind != kind) r = g.Regions.Get(r.Index + 1);
            return r;
        }

        /// <summary>Near tiles along the road between <paramref name="s0"/> and <paramref name="s1"/> (every 120 m, the road's tile only).</summary>
        private static List<(TileCoord coord, TileBlueprint bp)> Tiles(EndlessGenerator g, double s0, double s1)
        {
            var coords = new List<TileCoord>();
            for (double s = s0; s <= s1; s += 120.0)
            {
                RoadPoint rp = g.Road(s);
                TileCoord c = TileCoord.Containing(rp.X, rp.Z, g.Params.tileSize);
                if (!coords.Contains(c)) coords.Add(c);
            }

            return coords.Select(c => (c, g.GenerateTile(c, TileLod.Near))).ToList();
        }

        private static double RoadDistance(EndlessGenerator g, double ax, double az, double hint, out double s)
        {
            s = g.NearestS(ax, az, hint);
            RoadPoint rp = g.Road(s);
            return Math.Sqrt((rp.X - ax) * (rp.X - ax) + (rp.Z - az) * (rp.Z - az));
        }

        [Test]
        public void TrunkCorridor_IsPerRegion_AndTheOpenCountryKeepsTheScatterCorridor()
        {
            float scatter = new EndlessParams().roadWidth * 0.5f + 8f;
            Assert.That(Planting.TrunkCorridor(RegionKind.Forest, scatter), Is.EqualTo(5.5f));
            Assert.That(Planting.TrunkCorridor(RegionKind.Farmland, scatter), Is.EqualTo(scatter));
            Assert.That(Planting.TrunkCorridor(RegionKind.HighlandPass, scatter), Is.EqualTo(scatter));
            foreach (RegionKind kind in Enum.GetValues(typeof(RegionKind))) Assert.That(Planting.TrunkCorridor(kind, scatter), Is.LessThanOrEqualTo(scatter));
            // The least trunk distance keeps the verge clear of the collider and the road clear of the base, whatever the tree.
            Assert.That(Planting.MinTrunkDistance(1.43f, 1.8f, 5.5f), Is.GreaterThanOrEqualTo(2.5f + Planting.TrunkBaseMargin + 1.43f * 1.8f));
            Assert.That(Planting.MinTrunkDistance(0.2f, 1.8f, 5.5f), Is.GreaterThanOrEqualTo(Planting.Verge + Planting.MaxTrunkCollider * 1.8f));
            Assert.That(Planting.MinTrunkDistance(0.2f, 1f, 5.5f), Is.EqualTo(5.5f));
        }

        [TestCase(12345)]
        [TestCase(42)]
        public void ForestRoad_RunsThroughTheWood_AndNothingStandsOnTheRoadOrVerge(int seed)
        {
            EndlessGenerator g = Gen(seed);
            Region forest = First(g, RegionKind.Forest);
            float half = g.Params.roadWidth * 0.5f;
            int close = 0;
            double length = 0.0;
            double s0 = forest.Start + 300.0, s1 = Math.Min(forest.Start + forest.Length - 300.0, s0 + 1500.0);
            foreach ((TileCoord c, TileBlueprint bp) in Tiles(g, s0, s1))
            {
                double ox = c.OriginX(g.Params.tileSize), oz = c.OriginZ(g.Params.tileSize);
                double hint = (s0 + s1) * 0.5;
                foreach (TreePlacement t in bp.Trees)
                {
                    double ax = ox + t.position.x, az = oz + t.position.z;
                    double d = RoadDistance(g, ax, az, hint, out double s);
                    float scale = t.scale > 0f ? t.scale : 1f;
                    if (PropRole.IsTree(t.role))
                    {
                        // No trunk collider on the verge, no trunk base on the road (0.1 m: the road field's sampling).
                        Assert.That(d - Planting.MaxTrunkCollider * scale, Is.GreaterThanOrEqualTo(Planting.Verge - 0.1), $"{t.role} collider on the verge at s {s:0} ({d:0.0} m)");
                        Assert.That(d - Planting.TrunkRadiusOf(t.role, t.variant) * scale, Is.GreaterThanOrEqualTo(half + Planting.TrunkBaseMargin - 0.1), $"{t.role} base on the road at s {s:0}");
                        Assert.That(d, Is.GreaterThanOrEqualTo(Planting.WoodTrunkCorridor - 0.1), $"{t.role} inside the forest corridor at s {s:0} ({d:0.0} m)");
                        if (d < 12.0 && s >= s0 && s <= s1) close++;
                    }
                    else
                    {
                        Assert.That(d, Is.GreaterThanOrEqualTo(Planting.PlantVerge - 0.5), $"{t.role} on the verge at s {s:0} ({d:0.0} m)");
                    }
                }

                foreach (PropPlacement p in bp.Props)
                {
                    if (p.settlementId >= 0) continue;
                    double d = RoadDistance(g, ox + p.position.x, oz + p.position.z, hint, out double s);
                    Assert.That(d, Is.GreaterThan(Planting.Verge + 0.5), $"{p.role} at s {s:0} stands {d:0.0} m from the road centre");
                }

                foreach (PropPlacement p in bp.Pieces)
                {
                    if (!PropRole.IsOvergrowth(p.role)) continue;
                    double d = RoadDistance(g, ox + p.position.x, oz + p.position.z, hint, out double s);
                    Assert.That(d, Is.GreaterThan(Planting.Verge), $"{p.role} at s {s:0} on the verge");
                }
            }

            length = s1 - s0;
            double per100 = close * 100.0 / length;
            Assert.That(per100, Is.GreaterThan(4.0), $"trunks within 12 m of the forest road: {per100:0.0} per 100 m");
        }

        [TestCase(12345)]
        [TestCase(777)]
        public void OpenCountry_KeepsTheOldCorridor(int seed)
        {
            EndlessGenerator g = Gen(seed);
            Region farm = First(g, RegionKind.Farmland);
            float corridor = g.Params.roadWidth * 0.5f + 8f;
            double s0 = farm.Start + 300.0, s1 = Math.Min(farm.Start + farm.Length - 300.0, s0 + 1200.0);
            foreach ((TileCoord c, TileBlueprint bp) in Tiles(g, s0, s1))
            {
                double ox = c.OriginX(g.Params.tileSize), oz = c.OriginZ(g.Params.tileSize);
                foreach (TreePlacement t in bp.Trees)
                {
                    if (!PropRole.IsTree(t.role)) continue;
                    double d = RoadDistance(g, ox + t.position.x, oz + t.position.z, (s0 + s1) * 0.5, out double s);
                    if (g.Regions.At(s).Kind != RegionKind.Farmland) continue;
                    Assert.That(d, Is.GreaterThan(corridor - 1.0), $"A farmland tree at s {s:0} stands {d:0.0} m from the road centre.");
                }
            }
        }

        [Test]
        public void Woods_AreDeterministic_InAnyOrder()
        {
            EndlessGenerator a = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);
            EndlessGenerator b = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);
            Region forest = First(a, RegionKind.Forest);
            RoadPoint rp = a.Road(forest.Start + 700.0);
            TileCoord c = TileCoord.Containing(rp.X, rp.Z, a.Params.tileSize);
            b.GenerateTile(new TileCoord(c.X + 1, c.Z), TileLod.Near);   // a neighbour first: caches filled in another order
            b.GenerateTile(c, TileLod.Far);
            TileBlueprint x = a.GenerateTile(c, TileLod.Near), y = b.GenerateTile(c, TileLod.Near);
            Assert.That(Hash(y), Is.EqualTo(Hash(x)));
            Assert.That(x.Trees.Count(t => t.role == PropRole.Fern), Is.GreaterThan(0), "the wood has ferns");
            Assert.That(x.Trees.Count(t => t.role == PropRole.Undergrowth), Is.GreaterThan(0), "the wood has undergrowth");
        }

        private static ulong Hash(TileBlueprint bp)
        {
            ulong h = 1469598103934665603UL;
            void Mix(float v)
            {
                h ^= (ulong)BitConverter.ToUInt32(BitConverter.GetBytes(v), 0);
                h *= 1099511628211UL;
            }

            foreach (TreePlacement t in bp.Trees)
            {
                Mix(t.position.x);
                Mix(t.position.z);
                Mix(t.variant);
                Mix(t.scale);
                Mix(t.role.Length);
            }

            foreach (PropPlacement p in bp.Props.Concat(bp.Pieces))
            {
                Mix(p.position.x);
                Mix(p.position.y);
                Mix(p.variant);
            }

            foreach (DetailLayer l in bp.Details)
            {
                for (int i = 0; i < l.density.Length; i += 7) Mix(l.density[i]);
            }

            return h;
        }

        [Test]
        public void Glades_OpenOntoTheRoad_InTheWoods_AndKeepTheTreesOut()
        {
            EndlessGenerator g = Gen(12345);
            Region forest = First(g, RegionKind.Forest);
            var planting = new Planting(g.Params, g.Keys, g.Curve, g.Regions, g.Plan, g.Profile, g.Terrain, g.Fields, g.Features);
            var glades = new List<Glade>();
            planting.Glades(forest.Start, forest.Start + forest.Length, glades);
            Assert.That(glades.Count, Is.GreaterThanOrEqualTo(2), "a glade every few hundred metres");
            var again = new List<Glade>();
            planting.Glades(forest.Start, forest.Start + forest.Length, again);
            Assert.That(again.Select(x => x.S), Is.EqualTo(glades.Select(x => x.S)), "deterministic");
            int inside = 0, area = 0;
            foreach (Glade gl in glades.Take(3))
            {
                RoadPoint rp = g.Road(gl.S);
                double d = Math.Sqrt((gl.X - rp.X) * (gl.X - rp.X) + (gl.Z - rp.Z) * (gl.Z - rp.Z));
                Assert.That(d - gl.Radius, Is.InRange(4.0, 9.0), "the clearing opens onto the road");
                TileCoord c = TileCoord.Containing(gl.X, gl.Z, g.Params.tileSize);
                TileBlueprint bp = g.GenerateTile(c, TileLod.Near);
                double ox = c.OriginX(g.Params.tileSize), oz = c.OriginZ(g.Params.tileSize);
                inside += bp.Trees.Count(t => PropRole.IsTree(t.role) && gl.Contains(ox + t.position.x, oz + t.position.z, -3.0));
                area++;
            }

            Assert.That(inside, Is.LessThanOrEqualTo(2 * area), $"trees inside {area} glades: {inside}");
        }

        [TestCase(12345)]
        [TestCase(777)]
        public void NoGenericBroadleaf_NearTheRoad_AndNoDarkBush(int seed)
        {
            EndlessGenerator g = Gen(seed);
            Region forest = First(g, RegionKind.Forest);
            foreach ((TileCoord c, TileBlueprint bp) in Tiles(g, forest.Start + 200.0, forest.Start + 1400.0))
            {
                double ox = c.OriginX(g.Params.tileSize), oz = c.OriginZ(g.Params.tileSize);
                foreach (TreePlacement t in bp.Trees)
                {
                    string name = FarTreeShapes.CatalogName(t.role, t.variant);
                    if (t.role == PropRole.Bush) Assert.That(name, Is.Not.EqualTo("SM_Env_Bush_03"), "the dark Meadow bush is restyled");
                    if (!Planting.IsGenericBroadleaf(name)) continue;
                    double d = RoadDistance(g, ox + t.position.x, oz + t.position.z, forest.Start + 800.0, out _);
                    Assert.That(d, Is.GreaterThan(Planting.RestyleReach - 1.0), $"{name} {d:0} m from the road");
                }
            }
        }

        [Test]
        public void ForestTiles_StayWithinTheirBudget()
        {
            EndlessGenerator g = Gen(12345);
            Region forest = First(g, RegionKind.Forest);
            foreach ((TileCoord c, TileBlueprint bp) in Tiles(g, forest.Start + 300.0, forest.Start + 1500.0))
            {
                int trees = bp.Trees.Count(t => PropRole.IsTree(t.role));
                int plants = bp.Trees.Count - trees;
                int ivy = bp.Pieces.Count(p => PropRole.IsOvergrowth(p.role));
                Assert.That(trees, Is.LessThan(1400), $"trees in tile {c}");
                Assert.That(plants, Is.LessThan(5600), $"ferns and bushes in tile {c}");   // 3,200 until the forest floor pass (clusters)
                Assert.That(bp.Trees.Count, Is.LessThan(6400), $"instanced plants and trees in tile {c}");   // 4,000 until the forest floor pass
                Assert.That(ivy, Is.LessThan(300), $"ivy pieces in tile {c}");
                Assert.That(bp.Props.Count, Is.LessThan(260), $"GameObject props in tile {c}");
            }
        }

        [Test]
        public void ForestExtras_AreShedByTheCellBuilder()
        {
            var index = new TreePrototypeIndex();
            index.SetFamily(PropRole.ForestTree, new[] { index.AddPrototype(10f, 3f) });
            index.SetFamily(PropRole.Fern, new[] { index.AddPrototype(0.7f, 0.8f) });
            index.SetFamily(PropRole.Ivy, new[] { index.AddPrototype(2f, 1f) });
            var trees = new List<TreePlacement>
            {
                new TreePlacement { role = PropRole.ForestTree, position = new Vector3(10f, 0f, 10f), scale = 1f },
                new TreePlacement { role = PropRole.Fern, position = new Vector3(12f, 0f, 10f), scale = 1f },
            };
            var pieces = new List<PropPlacement> { new PropPlacement { role = PropRole.Ivy, position = new Vector3(20f, 0f, 20f), scale = 1f } };
            Assert.That(TreeCellSet.Build(trees, 256f, index, 32f, pieces).Total, Is.EqualTo(3));
            TreeCellSet shed = TreeCellSet.Build(trees, 256f, index, 32f, pieces, false);
            Assert.That(shed.Total, Is.EqualTo(1), "only the tree is left");
            Assert.That(PropRole.IsForestExtra(PropRole.ForestTree), Is.False);
            Assert.That(PropRole.IsInstancedPlant(PropRole.Fern) && PropRole.IsInstancedPlant(PropRole.Undergrowth), Is.True);
        }

        [Test]
        public void ShadowRings_SplitAtTheProxyDistance()
        {
            Assert.That(TreeDrawRules.ShadowRing(50f, 300f, 100f, true), Is.EqualTo(0));
            Assert.That(TreeDrawRules.ShadowRing(100f, 300f, 100f, true), Is.EqualTo(0));
            Assert.That(TreeDrawRules.ShadowRing(101f, 300f, 100f, true), Is.EqualTo(1));
            Assert.That(TreeDrawRules.ShadowRing(300f, 300f, 100f, true), Is.EqualTo(1));
            Assert.That(TreeDrawRules.ShadowRing(301f, 300f, 100f, true), Is.EqualTo(-1));
            Assert.That(TreeDrawRules.ShadowRing(50f, 300f, 100f, false), Is.EqualTo(-1));
            Assert.That(TreeDrawRules.ShadowRing(50f, 300f, 0f, true), Is.EqualTo(1), "proxy distance 0: proxies everywhere");
            for (int i = 0; i < 200; i++)
            {
                float f = TreeDrawRules.FadeDistance(160f, i * 3.7f, i * -1.3f);
                Assert.That(f, Is.InRange(128f, 160f));
            }
        }

        [Test]
        public void ShadowProxies_AreCheapClosedSolidsInsideTheCrown()
        {
            foreach (string name in new[] { "SM_Env_Tree_Meadow_01", "SM_Env_Tree_Birch_01", "SM_Gen_Env_Tree_Pine_01", "SM_Gen_Env_Tree_Dead_02" })
            {
                Assert.That(FarTreeShapes.TryGet(name, out FarTreeStyle style), Is.True, name);
                var v = new List<Vector3>();
                var t = new List<int>();
                ShadowProxy.Tree(style, v, t);
                Assert.That(t.Count / 3, Is.EqualTo(ShadowProxy.TrianglesOf(style.Shape)), name);
                Assert.That(t.All(i => i >= 0 && i < v.Count), Is.True);
                foreach (Vector3 p in v)
                {
                    Assert.That(new Vector2(p.x, p.z).magnitude, Is.LessThanOrEqualTo(Math.Max(style.Radius, style.TrunkRadius * 1.4f) + 1e-3f), name);
                    Assert.That(p.y, Is.LessThanOrEqualTo(style.Height + 1e-3f), name);
                }
            }

            var bv = new List<Vector3>();
            var bt = new List<int>();
            ShadowProxy.Box(new Vector3(0f, 0.6f, 0f), new Vector3(0.7f, 1.2f, 5f), bv, bt);
            Assert.That(bt.Count / 3, Is.EqualTo(12));
        }

        [Test]
        public void ForestMood_IsNeutralInTheOpen_AndShaftsNeedLowSunAndClearSky()
        {
            Assert.That(ForestMood.CanopyFor(0f), Is.EqualTo(0f));
            Assert.That(ForestMood.CanopyFor(0.23f), Is.EqualTo(0f), "meadow tree density: no forest term");
            Assert.That(ForestMood.CanopyFor(1.0f), Is.EqualTo(1f));
            Assert.That(ForestMood.ShaftStrength(15f, 0f, 0f, 1f), Is.GreaterThan(0.8f), "low sun, clear: strong");
            Assert.That(ForestMood.ShaftStrength(15f, 0f, 0f, 1f), Is.GreaterThan(ForestMood.ShaftStrength(45f, 0f, 0f, 1f)), "strongest at low sun");
            Assert.That(ForestMood.ShaftStrength(-5f, 0f, 0f, 1f), Is.EqualTo(0f), "none at night");
            Assert.That(ForestMood.ShaftStrength(70f, 0f, 0f, 1f), Is.EqualTo(0f), "none with a high sun");
            Assert.That(ForestMood.ShaftStrength(15f, 0.3f, 0.5f, 0.8f), Is.EqualTo(0f), "none in rain");
            Assert.That(ForestMood.ShaftStrength(15f, 0f, 0.9f, 0.5f), Is.EqualTo(0f), "none in thick overcast");

            RichGrade old = LightLookMath.Grade(0f, 30f, 0f, 0f), withForest = old;
            LightLookMath.AddForest(ref withForest, 0f, 1f, 30f, 0f);
            Assert.That(withForest.Temperature, Is.EqualTo(old.Temperature), "the old look is unchanged under the trees");
            Assert.That(withForest.Exposure, Is.EqualTo(0f));
            RichGrade rich = LightLookMath.Grade(1f, 30f, 0f, 0f), open = rich;
            LightLookMath.AddForest(ref open, 1f, 0f, 30f, 0f);
            Assert.That(open.Temperature, Is.EqualTo(rich.Temperature), "rich look in the open: unchanged");
            Assert.That(open.Exposure, Is.EqualTo(0f));
            RichGrade wood = rich;
            LightLookMath.AddForest(ref wood, 1f, 1f, 30f, 0f);
            Assert.That(wood.Temperature, Is.GreaterThan(rich.Temperature), "warmer under the canopy");
            Assert.That(wood.Exposure, Is.LessThan(0f), "a little darker under the canopy");
            RichGrade night = LightLookMath.Grade(1f, -20f, 0f, 0f), nightWood = night;
            LightLookMath.AddForest(ref nightWood, 1f, 1f, -20f, 0f);
            Assert.That(nightWood.Exposure, Is.EqualTo(0f), "nothing at night");
        }

        [Test]
        public void Ivy_HangsOnWalls_AndLandmarks()
        {
            LandmarkSite ruin = null;
            EndlessGenerator g = null;
            foreach (int seed in new[] { 12345, 42, 777 })
            {
                g = Gen(seed);
                var lms = new List<LandmarkSite>();
                g.Landmarks.Around(0.0, 30000.0, lms);
                ruin = lms.FirstOrDefault(l => l.Type == LandmarkType.Ruin);
                if (ruin != null) break;
            }

            Assert.That(ruin, Is.Not.Null, "a ruin within 30 km on one of the seeds");
            TileBlueprint bp = g.GenerateTile(TileCoord.Containing(ruin.X, ruin.Z, g.Params.tileSize), TileLod.Near);
            Assert.That(bp.Pieces.Any(p => p.role == PropRole.Ivy), Is.True, "ivy on the ruin's walls");
            Assert.That(bp.Props.Any(p => PropRole.IsOvergrowth(p.role)), Is.False, "ivy is drawn instanced, not as GameObjects");

            // Wall ivy: on seed 12345's first 20 km some stone walls in or near woods carry it.
            EndlessGenerator g2 = Gen(12345);
            int ivy = 0;
            for (double s = 500.0; s < 20000.0 && ivy == 0; s += 700.0)
            {
                RoadPoint rp = g2.Road(s);
                foreach (double off in new[] { -200.0, 200.0 })
                {
                    TileBlueprint t = g2.GenerateTile(TileCoord.Containing(rp.X + off, rp.Z, g2.Params.tileSize), TileLod.Near);
                    ivy += t.Pieces.Count(p => p.role == PropRole.IvyDrape);
                }
            }

            Assert.That(ivy, Is.GreaterThan(0), "ivy draped over some field walls");
        }
    }
}
