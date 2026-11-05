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
    /// <summary>Forest floor pass (2026-10-06): the wood floor's paint, sward and understorey, and the shaded leaves under Rich light.</summary>
    public sealed class EndlessForestFloorTests
    {
        private static EndlessGenerator gen;
        private static readonly Dictionary<(int, int, TileLod), TileBlueprint> Tiles = new Dictionary<(int, int, TileLod), TileBlueprint>();

        private static EndlessGenerator Gen => gen ??= new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);

        private static TileBlueprint Tile(int x, int z, TileLod lod)
        {
            if (!Tiles.TryGetValue((x, z, lod), out TileBlueprint bp)) Tiles[(x, z, lod)] = bp = Gen.GenerateTile(new TileCoord(x, z), lod);
            return bp;
        }

        /// <summary>The tile beside the road at the tour's 2,885 m stop (Asherford Chase, a tunnel of oaks).</summary>
        private static (int, int) ForestTile()
        {
            RoadPoint rp = Gen.Road(2885.0);
            float t = Gen.Params.tileSize;
            return ((int)Math.Floor(rp.X / t), (int)Math.Floor(rp.Z / t));
        }

        private static Vector3 Mix(float[,,] sp, int z, int x)
        {
            Vector3 c = Vector3.zero;
            for (int l = 0; l < Math.Min(sp.GetLength(2), GroundLook.TextureMeans.Length); l++) c += GroundLook.LayerColor(l) * sp[z, x, l];
            return c;
        }

        private static float Lum(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;

        [Test]
        public void WoodFloorMixIsDarkerThanTheLawn()
        {
            Vector3 lush = GroundLook.LayerColor(TerrainShaper.LushGrassLayer);
            for (float litter = 0f; litter <= 1f; litter += 0.25f)
            {
                Vector4 s = GroundLook.WoodFloorShares(litter), u = GroundLook.LeafLitterShares(litter);
                Assert.AreEqual(1f, s.x + s.y + s.z + s.w, 1e-5f);
                Assert.AreEqual(1f, u.x + u.y + u.z + u.w, 1e-5f);
                Assert.Greater(u.w, 0.7f, "under a crown the floor is mostly the leaf-litter layer");
                Assert.Less(s.w, u.w - 0.4f, "and thins to moss between the crowns");
                Vector3 c = GroundLook.WoodFloorColor(litter);
                Assert.Less(Lum(c), 0.72f * Lum(lush), "the wood floor reads darker than a meadow");
                Assert.Greater(GroundLook.DeltaE(c, lush), 10f);
                Assert.Greater(c.y, c.z, "still a green-olive floor, not black mud");
            }
        }

        [Test]
        public void FloorUnderCanopyIsLitterAndFarTilesAgree()
        {
            (int tx, int tz) = ForestTile();
            TileBlueprint near = Tile(tx, tz, TileLod.Near), far = Tile(tx, tz, TileLod.Far);
            float t = Gen.Params.tileSize;
            int res = near.Splat.GetLength(0), fres = far.Splat.GetLength(0);
            Vector3 sumNear = Vector3.zero, sumFar = Vector3.zero;
            double mud = 0;
            int n = 0;
            for (int z = 0; z < res; z += 2)
            {
                for (int x = 0; x < res; x += 2)
                {
                    double ax = tx * (double)t + (x + 0.5) * t / res, az = tz * (double)t + (z + 0.5) * t / res;
                    if (Planting.ForestAt(Gen.Terrain, ax, az) < 0.8f) continue;
                    RoadPoint q = Gen.Road(Gen.NearestS(ax, az, 2885.0));
                    if (Math.Sqrt((q.X - ax) * (q.X - ax) + (q.Z - az) * (q.Z - az)) < 14.0) continue;
                    n++;
                    mud += near.Splat[z, x, TerrainShaper.MudLayer] + near.Splat[z, x, TerrainShaper.LeafLitterLayer];
                    sumNear += Mix(near.Splat, z, x);
                    int fx = Math.Min(fres - 1, (int)Math.Round((x + 0.5) / res * (fres - 1))), fz = Math.Min(fres - 1, (int)Math.Round((z + 0.5) / res * (fres - 1)));
                    sumFar += Mix(far.Splat, fz, fx);
                }
            }

            Assume.That(n > 100, "the test tile has deep wood");
            Assert.Greater(mud / n, 0.4, "leaf litter under the canopy");
            Assert.Less(Lum(sumNear / n), 0.75f * Lum(GroundLook.LayerColor(TerrainShaper.LushGrassLayer)));
            Assert.Less(GroundLook.DeltaE(sumNear / n, sumFar / n), 2f, "a wood reads as dark from a distance as near by");
            Assert.Less(GroundLook.DeltaE(FarTileMeshData.ZoneColor(TerrainShaper.ForestFloorLayer, new FarMeshSettings()), GroundLook.WoodFloorColor()), 0.01f);
        }

        [Test]
        public void GladesKeepTheirLawnAndFlowers()
        {
            var tg = typeof(EndlessGenerator).GetField("tiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(Gen);
            var planting = (Planting)tg.GetType().GetField("planting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(tg);
            var glades = new List<Glade>();
            planting.Glades(2100.0, 3000.0, glades);
            Assume.That(glades.Count > 0);
            Glade g = glades[0];
            float t = Gen.Params.tileSize;
            TileBlueprint bp = Tile((int)Math.Floor(g.X / t), (int)Math.Floor(g.Z / t), TileLod.Near);
            int res = bp.Splat.GetLength(0);
            int x = (int)((g.X - Math.Floor(g.X / t) * t) / t * res), z = (int)((g.Z - Math.Floor(g.Z / t) * t) / t * res);
            float grass = bp.Splat[z, x, TerrainShaper.LushGrassLayer] + bp.Splat[z, x, TerrainShaper.FlowerLayer];
            Assert.Greater(grass, 0.5f, "a glade's centre is lush grass and flowers");
            Assert.Less(bp.Splat[z, x, TerrainShaper.MudLayer], 0.05f);
            Assert.Less(bp.Splat[z, x, TerrainShaper.LeafLitterLayer], 0.05f);
        }

        /// <summary>Texels of a near tile with their crown cover (the tile's own trees) and distance from the road.</summary>
        private static IEnumerable<(int z, int x, float k, double rd, float forest)> Texels(int tx, int tz, TileBlueprint bp, double sHint, int step)
        {
            var canopy = new Planting.CanopyMap(bp.Trees);
            float t = Gen.Params.tileSize;
            int res = bp.Splat.GetLength(0);
            for (int z = 0; z < res; z += step)
            {
                for (int x = 0; x < res; x += step)
                {
                    float lx = (x + 0.5f) * t / res, lz = (z + 0.5f) * t / res;
                    double ax = tx * (double)t + lx, az = tz * (double)t + lz;
                    RoadPoint q = Gen.Road(Gen.NearestS(ax, az, sHint));
                    double rd = Math.Sqrt((q.X - ax) * (q.X - ax) + (q.Z - az) * (q.Z - az));
                    yield return (z, x, canopy.At(lx, lz), rd, Planting.ForestAt(Gen.Terrain, ax, az));
                }
            }
        }

        private static (int, int) TileAt(double s)
        {
            RoadPoint rp = Gen.Road(s);
            float t = Gen.Params.tileSize;
            return ((int)Math.Floor(rp.X / t), (int)Math.Floor(rp.Z / t));
        }

        [Test]
        public void LeafLitterUnderCrownsIsBrownAgainstTheMeadow()
        {
            // The tour's 3,600 m stop (Asherford Chase): the ground under real crowns against an open meadow's paint (1,200 m).
            (int tx, int tz) = TileAt(3600.0);
            TileBlueprint bp = Tile(tx, tz, TileLod.Near);
            Vector3 under = Vector3.zero;
            double leaves = 0;
            int n = 0;
            foreach (var p in Texels(tx, tz, bp, 3600.0, 2))
            {
                if (p.k < 0.95f || p.rd < 10.0 || p.forest < 0.5f) continue;
                under += Mix(bp.Splat, p.z, p.x);
                leaves += bp.Splat[p.z, p.x, TerrainShaper.LeafLitterLayer];
                n++;
            }

            (int mx, int mz) = TileAt(1200.0);
            TileBlueprint mb = Tile(mx, mz, TileLod.Near);
            Vector3 meadow = Vector3.zero;
            int m = 0;
            foreach (var p in Texels(mx, mz, mb, 1200.0, 3))
            {
                if (p.k > 0f || p.rd < 10.0 || p.forest > 0.15f) continue;
                meadow += Mix(mb.Splat, p.z, p.x);
                m++;
            }

            Assume.That(n > 200 && m > 200);
            under /= n;
            meadow /= m;
            Assert.Less(Lum(under), 0.65f * Lum(meadow), "the floor under a crown is clearly darker than a meadow");
            Assert.Greater(GroundLook.DeltaE(under, meadow), 15f, "and a different colour");
            Assert.Greater(under.x, under.y * 1.2f, "brown leaf litter, not a green lawn");
            Assert.Greater(leaves / n, 0.6, "the leaf-litter layer leads under a crown");
            Vector3 lab = GroundLook.Lab(under);
            Assert.Greater(Math.Sqrt(lab.y * lab.y + lab.z * lab.z), 18.0, "a warm autumn brown, not pale grey");
            Assert.Less(lab.x, 40f, "fairly dark: the wood reads as shade");
        }

        [Test]
        public void GrassAndFlowersGoUnderCrowns()
        {
            (int tx, int tz) = TileAt(3600.0);
            TileBlueprint bp = Tile(tx, tz, TileLod.Near);
            var canopy = new Planting.CanopyMap(bp.Trees);
            float t = Gen.Params.tileSize;
            int res = bp.DetailResolution;
            double grassUnder = 0, flowersUnder = 0, grassOpen = 0;
            int cellsUnder = 0, cellsOpen = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float lx = (x + 0.5f) * t / res, lz = (z + 0.5f) * t / res;
                    float k = canopy.At(lx, lz);
                    double ax = tx * (double)t + lx, az = tz * (double)t + lz;
                    RoadPoint q = Gen.Road(Gen.NearestS(ax, az, 3600.0));
                    if (Math.Sqrt((q.X - ax) * (q.X - ax) + (q.Z - az) * (q.Z - az)) < 10.0) continue;
                    bool under = k >= 0.95f;
                    if (under) cellsUnder++;
                    else if (k <= 0f) cellsOpen++;
                    else continue;
                    foreach (DetailLayer l in bp.Details)
                    {
                        int v = l.density[z * res + x];
                        bool grass = l.family == PropRole.ShortGrass || l.family == PropRole.MedGrass || l.family == PropRole.TallGrass;
                        bool flower = !grass && l.family != PropRole.GroundCover;
                        if (under && grass) grassUnder += v;
                        if (under && flower) flowersUnder += v;
                        if (!under && grass) grassOpen += v;
                    }
                }
            }

            Assume.That(cellsUnder > 100 && cellsOpen > 100);
            Assert.Less(flowersUnder / cellsUnder, 0.01, "no flower carpet under a crown (a glade rim's flowers may reach one)");
            Assert.Less(grassUnder / cellsUnder, 0.45 * grassOpen / cellsOpen + 1e-3, "far fewer grass clumps under a crown than between them");
        }

        [Test]
        public void CrownsInGladesKeepTheLawnAndTilesAgreeAtSeams()
        {
            var tg = typeof(EndlessGenerator).GetField("tiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(Gen);
            var planting = (Planting)tg.GetType().GetField("planting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(tg);
            var glades = new List<Glade>();
            planting.Glades(3300.0, 4400.0, glades);
            float t = Gen.Params.tileSize;
            int checkedTexels = 0;
            foreach (Glade g in glades)
            {
                int tx = (int)Math.Floor(g.X / t), tz = (int)Math.Floor(g.Z / t);
                TileBlueprint bp = Tile(tx, tz, TileLod.Near);
                int res = bp.Splat.GetLength(0);
                for (double a = 0; a < 2 * Math.PI; a += 0.3)
                {
                    for (double r = 0; r < g.Radius - 2.0; r += 2.0)
                    {
                        double ax = g.X + Math.Cos(a) * r, az = g.Z + Math.Sin(a) * r;
                        int x = (int)((ax - tx * (double)t) / t * res), z = (int)((az - tz * (double)t) / t * res);
                        if (x < 0 || z < 0 || x >= res || z >= res) continue;
                        checkedTexels++;
                        Assert.Less(bp.Splat[z, x, TerrainShaper.MudLayer] + bp.Splat[z, x, TerrainShaper.LeafLitterLayer], 0.05f, "no leaf litter inside a glade");
                    }
                }
            }

            Assume.That(checkedTexels > 50);

            // The seam between two neighbouring near tiles in the wood: the litter on both sides of the edge agrees as well as two
            // neighbouring columns inside one tile do (crowns of the neighbour's trees reach over the edge through the window's pad).
            (int sx, int sz) = TileAt(3600.0);
            TileBlueprint left = Tile(sx, sz, TileLod.Near), right = Tile(sx + 1, sz, TileLod.Near);
            int n = left.Splat.GetLength(0);
            double across = 0, inside = 0;
            for (int z = 0; z < n; z++)
            {
                across += Math.Abs(left.Splat[z, n - 1, TerrainShaper.LeafLitterLayer] - right.Splat[z, 0, TerrainShaper.LeafLitterLayer]);
                inside += Math.Abs(left.Splat[z, n - 2, TerrainShaper.LeafLitterLayer] - left.Splat[z, n - 1, TerrainShaper.LeafLitterLayer]);
            }

            Assert.Less(across, 1.5 * inside + 0.02 * n, "neighbouring tiles paint the same litter at their seam");
            TileBlueprint again = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default).GenerateTile(new TileCoord(sx, sz), TileLod.Near);
            for (int z = 0; z < n; z += 7)
            {
                for (int x = 0; x < n; x += 7)
                {
                    Assert.AreEqual(left.Splat[z, x, TerrainShaper.LeafLitterLayer], again.Splat[z, x, TerrainShaper.LeafLitterLayer], "deterministic");
                }
            }
        }

        [Test]
        public void UnderstoreyGrowsInClustersOffTheRoadWithinBudget()
        {
            (int tx, int tz) = ForestTile();
            TileBlueprint bp = Tile(tx, tz, TileLod.Near);
            float t = Gen.Params.tileSize;
            var plants = bp.Trees.Where(p => p.role == PropRole.Fern || p.role == PropRole.Undergrowth).Select(p => new Vector2(p.position.x, p.position.z)).ToList();
            Assert.Greater(plants.Count, 200);
            Assert.Less(plants.Count, 6000, "per-tile cap of the understorey");
            int grouped = 0;
            foreach (Vector2 a in plants)
            {
                int near = plants.Count(b => (a - b).sqrMagnitude < 2.5f * 2.5f);
                if (near >= 3) grouped++;   // itself and at least two others within 2.5 m
                double ax = tx * (double)t + a.x, az = tz * (double)t + a.y;
                RoadPoint q = Gen.Road(Gen.NearestS(ax, az, 2885.0));
                Assert.GreaterOrEqual(Math.Sqrt((q.X - ax) * (q.X - ax) + (q.Z - az) * (q.Z - az)), Planting.PlantVerge - 0.05, "nothing on the road or its verge");
            }

            Assert.Greater((float)grouped / plants.Count, 0.5f, "ferns and undergrowth stand in drifts, not singly");
            TileBlueprint again = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default).GenerateTile(new TileCoord(tx, tz), TileLod.Near);
            Assert.AreEqual(bp.Trees.Count, again.Trees.Count, "deterministic");
            Assert.AreEqual(bp.Props.Count, again.Props.Count, "deterministic");
        }

        [Test]
        public void RichLightOffIsTheOldLookAndShadedLeavesStayGreen()
        {
            RichGrade old = LightLookMath.Grade(0f, 50f, 0f, 0f), forest = old;
            LightLookMath.AddForest(ref forest, 0f, 1f, 50f, 0f);
            Assert.AreEqual(old.Shadows.R, forest.Shadows.R);
            Assert.AreEqual(old.Shadows.B, forest.Shadows.B);
            Assert.AreEqual(old.Contrast, forest.Contrast);
            Assert.AreEqual(old.Exposure, forest.Exposure);
            Assert.AreEqual(0.5f, old.Shadows.B, 1e-6f);

            Assert.GreaterOrEqual(LightLookMath.ShadedLeafRatio(1f, 1f, 60f), 1f / 3f, "under the canopy a shaded crown keeps a third of the sunlit one's luminance");
            Assert.Greater(LightLookMath.ShadedLeafRatio(1f, 0f, 60f), 0.27f);
            Tone3 shaded = LightLookMath.LeafAfterGrade(LightLookMath.OakLeaf, false, 1f, 1f, 60f);
            Assert.Greater(shaded.G, 1.5f * shaded.R, "green, not grey");
            Assert.Greater(shaded.R, shaded.B, "not blue");
            Tone3 open = LightLookMath.LeafAfterGrade(LightLookMath.OakLeaf, false, 1f, 0f, 60f);
            Assert.Greater(open.G, open.B * 4f);
            // The ground's shade/sun ratio under a full canopy rises by at most a fifth (0.280 in the open, 0.325 under the canopy).
            var ground = new Tone3(0.12f, 0.14f, 0.03f);
            float Ratio(float canopy) => LightLookMath.LeafAfterGrade(ground, false, 1f, canopy, 60f, 0.87f, 1f).Luma / LightLookMath.LeafAfterGrade(ground, true, 1f, canopy, 60f, 0.87f, 1f).Luma;
            Assert.Less(Ratio(1f), Ratio(0f) * 1.2f);
        }

        /// <summary>
        /// Generation time pass (2026-10-06): the wood floor's roadside forest lookups stay within their work budget. On the near tiles
        /// of <c>EndlessStreamTests.NearTiles_StayWithinTheirTimeBudget</c> (seed 777) a tile makes at most 3,000 full forest
        /// evaluations (three height samples and a cover call each; about 1,700 since the pass, 10,100 before it). Counted, not timed, so
        /// a loaded machine cannot fail it.
        /// </summary>
        [Test]
        public void WoodFloor_FieldLookupsStayWithinTheirWorkBudget()
        {
            var g = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            long before = g.Terrain.WoodSamples;
            int n = 0;
            for (double s = 500.0; s < 12000.0; s += 600.0, n++)
            {
                RoadPoint rp = g.Road(s);
                g.GenerateTile(TileCoord.Containing(rp.X, rp.Z, 256f), TileLod.Near);
            }

            double perTile = (g.Terrain.WoodSamples - before) / (double)n;
            Assert.That(perTile, Is.LessThan(3000.0), $"{perTile:0} full forest evaluations per near tile.");
        }

        /// <summary>
        /// The shortcut that spares the wood floor its height samples (<see cref="TerrainField.NoWoodsAt(double, double)"/>) only ever
        /// answers true where the land cover's forest weight is exactly 0, whatever the height and slope, and agrees with its row form.
        /// </summary>
        [Test]
        public void NoWoodsAt_OnlyWhereTheCoverHasNoWoods()
        {
            foreach (int seed in new[] { 12345, 42, 777 })
            {
                var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
                TerrainField f = g.Terrain;
                var rng = new System.Random(seed);
                int none = 0;
                for (int i = 0; i < 3000; i++)
                {
                    RoadPoint rp = g.Road(rng.NextDouble() * 30000.0);
                    double x = rp.X + (rng.NextDouble() * 2.0 - 1.0) * 45.0, z = rp.Z + (rng.NextDouble() * 2.0 - 1.0) * 45.0;
                    TerrainField.Row row = f.RowAt(z);
                    bool noWoods = f.NoWoodsAt(x, z);
                    Assert.That(TerrainField.NoWoodsAt(row, x), Is.EqualTo(noWoods));
                    if (!noWoods) continue;
                    none++;
                    float h = f.Height(row, x);
                    foreach (float slope in new[] { 0f, 0.3f, 1f })
                    {
                        f.Cover(row, x, h, slope, out float forest, out _, out _, out _);
                        Assert.That(forest, Is.EqualTo(0f), $"seed {seed} at ({x:0.0}, {z:0.0})");
                    }
                }

                Assert.That(none, Is.GreaterThan(200), "the shortcut applies near the road");
            }
        }

        [Test]
        public void LeafLitterTakesTheCobbleSlot_ForestTilesKeepTwoPasses()
        {
            var all = new bool[TerrainShaper.EndlessLayerCount - TerrainShaper.LayerCount];
            for (int i = 0; i < all.Length; i++) all[i] = true;
            int kind = EndlessLayerSets.LayerSetKind(false, false, true, false);
            int[] slots = EndlessLayerSets.ChannelSlots(kind, all, out int[] appended);
            Assert.That(appended.Length, Is.EqualTo(0), "a wood's tile keeps the shared eight layers");
            Assert.That(EndlessLayerSets.Passes(TerrainShaper.LayerCount + appended.Length), Is.EqualTo(2));
            Assert.That(slots[TerrainShaper.LeafLitterLayer], Is.EqualTo(TerrainShaper.CobbleLayer));
            Assert.That(slots[TerrainShaper.CobbleLayer], Is.EqualTo(TerrainShaper.DirtLayer));
            Assert.That(EndlessLayerSets.LitterInCobbleSlot(kind, all), Is.True);
            // Litter with a village square appends it; a tile without litter keeps its cobbles.
            int both = EndlessLayerSets.LayerSetKind(false, false, true, true);
            slots = EndlessLayerSets.ChannelSlots(both, all, out appended);
            Assert.That(appended, Is.EqualTo(new[] { TerrainShaper.LeafLitterLayer }));
            Assert.That(slots[TerrainShaper.CobbleLayer], Is.EqualTo(TerrainShaper.CobbleLayer));
            slots = EndlessLayerSets.ChannelSlots(0, all, out appended);
            Assert.That(slots[TerrainShaper.CobbleLayer], Is.EqualTo(TerrainShaper.CobbleLayer));
            Assert.That(slots[TerrainShaper.LeafLitterLayer], Is.LessThan(TerrainShaper.LayerCount));

            // The deep wood's near tiles paint litter and take that set.
            (int tx, int tz) = TileAt(3600.0);
            TileBlueprint bp = Tile(tx, tz, TileLod.Near);
            Assert.That(bp.LitterPaint, Is.True);
            Assert.That(bp.CobblePaint, Is.False);
            Assert.That(bp.MountainPaint || bp.FieldPaint, Is.False, "two passes");
        }

        [Test]
        public void DistantWoodFloorFollowsTheLitterMix()
        {
            // A litter-led face (under a crown's mix) is the wood floor zone, coloured by the wood floor's mean.
            var w = new float[TerrainShaper.EndlessLayerCount];
            Vector4 u = GroundLook.LeafLitterShares(0.5f);
            w[TerrainShaper.ForestFloorLayer] = u.x; w[TerrainShaper.MudLayer] = u.y; w[TerrainShaper.DirtLayer] = u.z; w[TerrainShaper.LeafLitterLayer] = u.w;
            Assert.That(FarTileMeshData.ChooseZone(w, 0f, 0f, 12345u, 0.2f), Is.EqualTo(TerrainShaper.ForestFloorLayer));
            Vector4 m = GroundLook.WoodFloorBlend(0.5f, GroundLook.WoodCanopyMean);
            Assert.Less(GroundLook.DeltaE(GroundLook.WoodFloorColor(), GroundLook.WoodFloorMixColor(m)), 0.01f);
            Assert.Less(GroundLook.DeltaE(FarTileMeshData.ZoneColor(TerrainShaper.ForestFloorLayer, new FarMeshSettings()), GroundLook.WoodFloorColor()), 0.01f);
            Vector3 lab = GroundLook.Lab(GroundLook.LayerColor(TerrainShaper.LeafLitterLayer));
            Assert.That(lab.x, Is.InRange(30f, 40f), "the litter layer is fairly dark");
            Assert.Greater(lab.z, 20f, "and warm (b*)");
        }
    }
}
