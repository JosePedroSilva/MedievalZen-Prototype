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
    /// <summary>The worked patchwork round the endless road's hamlets and villages (2026-10-06): see FieldPatchwork.</summary>
    public sealed class EndlessFieldTests
    {
        private static readonly int[] Seeds = { 12345, 42, 777 };
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

        /// <summary>Hamlets and villages in the first 20 km outside forests and passes.</summary>
        private static List<Place> Villages(EndlessGenerator g)
        {
            var places = new List<Place>();
            g.Plan.RegionPlaces(0, 20000, places);
            return places.Where(p => (p.Kind == PlaceKind.Village || p.Kind == PlaceKind.Hamlet) &&
                                     g.Regions.At(p.S).Kind != RegionKind.Forest && g.Regions.At(p.S).Kind != RegionKind.HighlandPass).OrderBy(p => p.S).ToList();
        }

        private static List<MosaicField> FieldsAround(EndlessGenerator g, double x, double z, double radius)
        {
            var list = new List<MosaicField>();
            g.Fields.Overlapping(x - radius, z - radius, 2 * radius, list);
            return list.Where(f => Math.Sqrt((f.X - x) * (f.X - x) + (f.Z - z) * (f.Z - z)) < radius).ToList();
        }

        [Test]
        public void EveryVillageHasAPatchworkOfStrips()
        {
            foreach (int seed in Seeds)
            {
                EndlessGenerator g = Gen(seed);
                List<Place> villages = Villages(g);
                Assert.That(villages.Count, Is.GreaterThan(0), $"seed {seed}: no village in 20 km");
                foreach (Place place in villages)
                {
                    Dressing d = g.Dressings.Settlement(place);
                    List<MosaicField> fields = FieldsAround(g, d.CentreX, d.CentreZ, 400.0);
                    Assert.That(fields.Count, Is.GreaterThanOrEqualTo(30), $"seed {seed} {place.Name}: {fields.Count} fields within 400 m");
                    Assert.That(fields.Count(f => f.NearVillage && FieldPatchwork.StripCount(f, seed) >= 3), Is.GreaterThanOrEqualTo(10), $"seed {seed} {place.Name}: too few strip fields");
                    double nearest = fields.Min(f => Math.Sqrt((f.X - d.CentreX) * (f.X - d.CentreX) + (f.Z - d.CentreZ) * (f.Z - d.CentreZ)));
                    Assert.That(nearest, Is.LessThan(200.0), $"seed {seed} {place.Name}: the nearest field is {nearest:0} m out");
                }
            }
        }

        [Test]
        public void NothingInsideAFootprintOrOnTheRoadAndLanesAreGateways()
        {
            foreach (int seed in Seeds)
            {
                EndlessGenerator g = Gen(seed);
                float t = g.Params.tileSize;
                foreach (Place place in Villages(g).Take(2))
                {
                    Dressing d = g.Dressings.Settlement(place);
                    SettlementFootprint fp = SettlementFootprint.Of(place, d);
                    foreach (MosaicField f in FieldsAround(g, d.CentreX, d.CentreZ, 450.0))
                    {
                        double rad = f.Yaw * Math.PI / 180.0;
                        Assert.That(fp.Clearance(f.X, f.Z, f.HalfX, f.HalfZ, Math.Cos(rad), Math.Sin(rad)), Is.GreaterThanOrEqualTo((f.Close ? FieldMosaic.CloseMargin : FieldPatchwork.FootprintMargin) - 1e-6),
                            $"seed {seed} {place.Name}: field {f.Id} inside the footprint");
                    }

                    int tx0 = (int)Math.Floor((d.CentreX - 160) / t), tx1 = (int)Math.Floor((d.CentreX + 160) / t);
                    int tz0 = (int)Math.Floor((d.CentreZ - 160) / t), tz1 = (int)Math.Floor((d.CentreZ + 160) / t);
                    for (int tz = tz0; tz <= tz1; tz++)
                    {
                        for (int tx = tx0; tx <= tx1; tx++)
                        {
                            TileBlueprint bp = g.GenerateTile(new TileCoord(tx, tz), TileLod.Near);
                            foreach (PropPlacement pc in bp.Pieces)
                            {
                                if (!PropRole.IsBoundaryPiece(pc.role)) continue;
                                double x = tx * (double)t + pc.position.x, z = tz * (double)t + pc.position.z;
                                foreach (var (cx, cz, r) in fp.Discs)
                                    Assert.That(Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)), Is.GreaterThanOrEqualTo(r), $"seed {seed} {place.Name}: {pc.role} inside the footprint");
                                foreach (var lane in fp.Lanes)
                                    Assert.That(FieldBoundaries.ClearOfPaths(new[] { (lane.x0, lane.z0, lane.x1, lane.z1, 3f) }, x, z, 1f), Is.True, $"seed {seed} {place.Name}: {pc.role} across a lane");
                                g.Curve.Nearest(x, z, out double roadDistance);
                                Assert.That(roadDistance, Is.GreaterThan(g.Params.roadWidth * 0.5 + 1.5), $"seed {seed}: {pc.role} on the road or its verge");
                            }

                            Assert.That(bp.Pieces.Count, Is.LessThanOrEqualTo(1200), $"seed {seed} tile ({tx},{tz}): boundary pieces over budget");
                            Assert.That(bp.Trees.Count(p => PropRole.IsInstancedCrop(p.role)), Is.LessThanOrEqualTo(2500), $"seed {seed} tile ({tx},{tz}): crop instances over budget");
                        }
                    }
                }
            }
        }

        [Test]
        public void NearAndFarTilesPaintTheSameStrips()
        {
            EndlessGenerator g = Gen(12345);
            float t = g.Params.tileSize;
            Place place = Villages(g)[0];
            Dressing d = g.Dressings.Settlement(place);
            MosaicField probe = FieldsAround(g, d.CentreX, d.CentreZ, 400.0).Where(f => !f.Fallow && f.NearVillage).OrderBy(f => Math.Abs(f.X - d.CentreX) + Math.Abs(f.Z - d.CentreZ)).First();
            int tx = (int)Math.Floor(probe.X / t), tz = (int)Math.Floor(probe.Z / t);
            int checkedTexels = 0;
            var tracks = new List<(double x0, double z0, double x1, double z1, float width)>();
            FieldPatchwork.Tracks(d, (x, z) => { g.Curve.Nearest(x, z, out double rd); return rd; }, tracks);
            bool OnTrack(double x, double z) => !FieldBoundaries.ClearOfPaths(tracks.Select(k => (k.x0, k.z0, k.x1, k.z1, k.width + 4f)).ToList(), x, z, 0f);
            foreach (TileLod lod in new[] { TileLod.Near, TileLod.Mid, TileLod.Far })
            {
                TileBlueprint bp = g.GenerateTile(new TileCoord(tx, tz), lod);
                var fields = new List<MosaicField>();
                g.Fields.Overlapping(tx * (double)t, tz * (double)t, t, fields);
                int res = bp.Splat.GetLength(0);
                // Near: texel centres; mid and far: grid points (texel k at k · texel from the corner).
                double step = lod == TileLod.Near ? t / res : t / (res - 1), shift = lod == TileLod.Near ? 0.5 : 0.0;
                for (int j = 0; j < res; j += 3)
                {
                    for (int i = 0; i < res; i += 3)
                    {
                        double x = tx * (double)t + (i + shift) * step, z = tz * (double)t + (j + shift) * step;
                        foreach (MosaicField f in fields)
                        {
                            int look = FieldPatchwork.LookAt(f, 12345, x, z, out float a, (float)step);   // distant paint takes a close whole
                            g.Curve.Nearest(x, z, out double roadDistance);
                            if (look < 0 || a < 1f || look == GroundLook.FieldPasture || f.Fallow || roadDistance < 20.0) continue;   // pastures carry their worn gateway
                            if (OnTrack(x, z)) continue;   // the village's field tracks are near-tile paths
                            float[] mix = GroundLook.FieldShares[look];
                            for (int c = 0; c < mix.Length; c++)
                                Assert.That(bp.Splat[j, i, c], Is.EqualTo(mix[c]).Within(0.02f), $"{lod} ({x:0},{z:0}) {GroundLook.FieldLookNames[look]} channel {c}");
                            checkedTexels++;
                        }
                    }
                }
            }

            Assert.That(checkedTexels, Is.GreaterThan(100));
        }

        [Test]
        public void NoFieldPaintIsNearWhite()
        {
            for (int look = 0; look < GroundLook.FieldShares.Length; look++)
            {
                Assert.That(GroundLook.FieldShares[look].Sum(), Is.EqualTo(1f).Within(1e-4f), GroundLook.FieldLookNames[look]);
                Vector3 c = GroundLook.FieldColor(look);
                Vector3 lab = GroundLook.Lab(c);
                // 2026-10-06 second pass: the gold and yellow are saturated (linear red up to 0.6), so judge paleness by lightness and chroma.
                Assert.That(lab.x, Is.LessThan(75f), $"{GroundLook.FieldLookNames[look]} is too light: L* {lab.x}");
                Assert.That(lab.x < 62f || new Vector2(lab.y, lab.z).magnitude > 25f, Is.True, $"{GroundLook.FieldLookNames[look]} is pale and grey: {lab}");
            }

            Color flower = GroundLook.RapeseedFlower;
            Assert.That(flower.b, Is.LessThan(0.35f * flower.r), "rapeseed heads must be yellow, not white");
        }

        /// <summary>The lawn round the villages (lush 0.27, dry 0.67, flowers 0.04, dirt 0.02), measured under Hollestead.</summary>
        private static Vector3 Lawn() => GroundLook.LayerColor(0) * 0.27f + GroundLook.LayerColor(1) * 0.67f + GroundLook.LayerColor(5) * 0.04f + GroundLook.LayerColor(2) * 0.02f;

        [Test]
        public void FieldColoursStandOutFromTheLawn()
        {
            int[] crops = { GroundLook.FieldGreenCrop, GroundLook.FieldRipeGrain, GroundLook.FieldRapeseed, GroundLook.FieldStubble, GroundLook.FieldPloughed };
            foreach (int look in crops)
                Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(look), Lawn()), Is.GreaterThanOrEqualTo(20f), GroundLook.FieldLookNames[look]);
            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldHayMeadow), Lawn()), Is.GreaterThanOrEqualTo(10f), "hay meadow");
            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldGreenCrop), GroundLook.FieldColor(GroundLook.FieldPasture)), Is.GreaterThanOrEqualTo(18f), "green crop against pasture");
            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldRipeGrain), GroundLook.FieldColor(GroundLook.FieldRapeseed)), Is.GreaterThanOrEqualTo(10f), "grain against rapeseed");
            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldRipeGrain), GroundLook.FieldColor(GroundLook.FieldStubble)), Is.GreaterThanOrEqualTo(10f), "grain against stubble");
            // The crops lead with their own field channel.
            Assert.That(GroundLook.FieldShares[GroundLook.FieldRipeGrain][TerrainShaper.FieldGoldLayer], Is.GreaterThanOrEqualTo(0.85f));
            Assert.That(GroundLook.FieldShares[GroundLook.FieldRapeseed][TerrainShaper.FieldYellowLayer], Is.GreaterThanOrEqualTo(0.8f));
            Assert.That(GroundLook.FieldShares[GroundLook.FieldGreenCrop][TerrainShaper.FieldGreenLayer], Is.GreaterThanOrEqualTo(0.85f));
            Assert.That(GroundLook.TextureMeans.Length, Is.EqualTo(TerrainShaper.EndlessLayerCount));
            Assert.That(GroundLook.LayerTints.Length, Is.EqualTo(TerrainShaper.EndlessLayerCount));
            Assert.That(GroundLook.FieldShares.All(s => s.Length == TerrainShaper.EndlessLayerCount), Is.True);
        }

        [Test]
        public void DistantFacesTakeTheFieldColour()
        {
            Vector3[] channels = GroundLook.LayerColors();
            for (int look = 0; look < GroundLook.FieldShares.Length; look++)
            {
                if (!GroundLook.IsMatchedFieldLook(look)) continue;
                float[] w = (float[])GroundLook.FieldShares[look].Clone();
                Assert.That(GroundLook.MatchFieldLook(w, w.Length), Is.EqualTo(look), GroundLook.FieldLookNames[look]);
                Vector3 far = FarTileMeshData.FieldZoneColor(look, channels);
                Assert.That(GroundLook.DeltaE(far, GroundLook.FieldColor(look)), Is.LessThan(3f), GroundLook.FieldLookNames[look]);
            }

            // Plain lawn is not mistaken for a field.
            var lawn = new float[TerrainShaper.EndlessLayerCount];
            lawn[0] = 0.27f; lawn[1] = 0.67f; lawn[5] = 0.04f; lawn[2] = 0.02f;
            Assert.That(GroundLook.MatchFieldLook(lawn, lawn.Length), Is.EqualTo(-1));
        }

        [Test]
        public void CloseColoursAreBrightAndTheirOwn()
        {
            Vector3 pasture = GroundLook.FieldColor(GroundLook.FieldPasture);
            float Lum(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;
            int[] looks = { GroundLook.FieldClose, GroundLook.FieldGarden, GroundLook.FieldOrchard, GroundLook.FieldHayMeadow, GroundLook.FieldWorn, GroundLook.FieldGardenSoil, GroundLook.FieldGardenGreens };
            foreach (int look in looks)
            {
                string name = GroundLook.FieldLookNames[look];
                float[] mix = GroundLook.FieldShares[look];
                // No metallic, glossy mud (the slate "ponds"), no rock or cobble.
                Assert.That(mix[TerrainShaper.MudLayer] + mix[TerrainShaper.RockLayer] + mix[TerrainShaper.CobbleLayer], Is.EqualTo(0f), $"{name} carries mud, rock or cobble");
                Vector3 c = GroundLook.FieldColor(look), lab = GroundLook.Lab(c);
                Assert.That(Lum(c), Is.GreaterThanOrEqualTo(0.85f * Lum(pasture)), $"{name} is dark");
                Assert.That(new Vector2(lab.y, lab.z).magnitude, Is.GreaterThan(18f), $"{name} is grey: {lab}");
                Assert.That(lab.z, Is.GreaterThan(15f), $"{name} is cold (slate): {lab}");
            }

            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldClose), pasture), Is.GreaterThanOrEqualTo(8f), "paddock against pasture");
            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldGarden), pasture), Is.GreaterThanOrEqualTo(12f), "garden against pasture");
            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldOrchard), pasture), Is.GreaterThanOrEqualTo(4f), "orchard against pasture");
            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldGardenSoil), GroundLook.FieldColor(GroundLook.FieldGardenGreens)), Is.GreaterThanOrEqualTo(25f), "soil rows against green rows");
            Assert.That(GroundLook.DeltaE(GroundLook.FieldColor(GroundLook.FieldWorn), GroundLook.FieldColor(GroundLook.FieldClose)), Is.GreaterThanOrEqualTo(15f), "worn edge against the paddock");
            // A garden from afar is the mean of its rows.
            for (int c = 0; c < TerrainShaper.EndlessLayerCount && c < GroundLook.FieldShares[GroundLook.FieldGarden].Length; c++)
                Assert.That(GroundLook.FieldShares[GroundLook.FieldGarden][c], Is.EqualTo(0.5f * (GroundLook.FieldShares[GroundLook.FieldGardenSoil][c] + GroundLook.FieldShares[GroundLook.FieldGardenGreens][c])).Within(1e-4f), $"garden channel {c}");
        }

        /// <summary>Every close round the first villages of a seed with the items planned for it.</summary>
        private static List<(Place place, MosaicField close, List<CloseItem> items)> Closes(EndlessGenerator g, int seed, int villages)
        {
            var result = new List<(Place, MosaicField, List<CloseItem>)>();
            foreach (Place place in Villages(g).Take(villages))
            {
                Dressing d = g.Dressings.Settlement(place);
                foreach (MosaicField f in FieldsAround(g, d.CentreX, d.CentreZ, 450.0).Where(f => f.Close))
                {
                    var items = new List<CloseItem>();
                    CloseContents.Plan(f, seed, g.Fields.SiteOf(f.CellI, f.CellJ), (x, z) => { g.Curve.Nearest(x, z, out double rd); return rd; }, g.Params.roadWidth * 0.5, items);
                    result.Add((place, f, items));
                }
            }

            return result;
        }

        [Test]
        public void ClosesAreLivedInAndKeepClear()
        {
            foreach (int seed in Seeds)
            {
                EndlessGenerator g = Gen(seed);
                var closes = Closes(g, seed, 3);
                Assert.That(closes.Count, Is.GreaterThan(0), $"seed {seed}: no closes");
                int orchards = 0, orchardTrees = 0, gardens = 0;
                foreach (var (place, f, items) in closes)
                {
                    string at = $"seed {seed} {place.Name} close {f.Id} ({GroundLook.FieldLookNames[f.Look]})";
                    Dressing d = g.Dressings.Settlement(place);
                    SettlementFootprint fp = SettlementFootprint.Of(place, d);
                    var tracks = new List<(double x0, double z0, double x1, double z1, float width)>();
                    FieldPatchwork.Tracks(d, (x, z) => { g.Curve.Nearest(x, z, out double rd); return rd; }, tracks);
                    Assert.That(items.Count(i => i.Role == PropRole.FruitTree), Is.LessThanOrEqualTo(30), $"{at}: too many trees");
                    Assert.That(items.Count(i => i.Role != PropRole.FruitTree), Is.LessThanOrEqualTo(8), $"{at}: too many GameObjects");
                    if (f.Look == GroundLook.FieldClose) Assert.That(items, Is.Empty, $"{at}: paddocks stay open");
                    foreach (CloseItem it in items)
                    {
                        Assert.That(FieldMosaic.Distance(f, it.X, it.Z), Is.EqualTo(0.0), $"{at}: {it.Role} outside its close");
                        g.Curve.Nearest(it.X, it.Z, out double roadDistance);
                        Assert.That(roadDistance, Is.GreaterThan(g.Params.roadWidth * 0.5 + 1.5 + CloseContents.Radius(it.Role)), $"{at}: {it.Role} on the road or its verge");
                        foreach (var (cx, cz, r) in fp.Discs)
                            Assert.That(Math.Sqrt((it.X - cx) * (it.X - cx) + (it.Z - cz) * (it.Z - cz)), Is.GreaterThanOrEqualTo(r + CloseContents.Radius(it.Role)), $"{at}: {it.Role} in the footprint");
                        Assert.That(FieldBoundaries.ClearOfPaths(fp.Lanes.Select(l => (l.x0, l.z0, l.x1, l.z1, (float)(2.0 * l.half))).ToList(), it.X, it.Z, (float)CloseContents.Radius(it.Role)), Is.True, $"{at}: {it.Role} on a lane");
                        Assert.That(FieldBoundaries.ClearOfPaths(tracks, it.X, it.Z, (float)CloseContents.Radius(it.Role)), Is.True, $"{at}: {it.Role} on a field track");
                        Assert.That(FieldMosaic.Distance(f, it.X, it.Z) == 0.0 && Math.Min(f.HalfX, f.HalfZ) > 0, Is.True);
                        if (it.Role == PropRole.FruitTree)
                        {
                            float corridor = Planting.TrunkCorridor(g.Regions.At(g.Curve.S(it.Z)).Kind, g.Params.roadWidth * 0.5f + 8f);   // WorldGenParams.ScatterCorridor
                            Assert.That(roadDistance, Is.GreaterThanOrEqualTo(Planting.MinTrunkDistance(0.5f, it.Scale, corridor, g.Params.roadWidth * 0.5f)), $"{at}: trunk too near the road");
                        }
                    }

                    var trees = items.Where(i => i.Role == PropRole.FruitTree).ToList();
                    for (int a = 0; a < trees.Count; a++)
                        for (int b = a + 1; b < trees.Count; b++)
                            Assert.That(Math.Sqrt((trees[a].X - trees[b].X) * (trees[a].X - trees[b].X) + (trees[a].Z - trees[b].Z) * (trees[a].Z - trees[b].Z)), Is.GreaterThanOrEqualTo(6.0), $"{at}: fruit trees too close");
                    if (f.Look == GroundLook.FieldOrchard) { orchards++; orchardTrees += trees.Count; }
                    if (f.Look == GroundLook.FieldGarden)
                    {
                        gardens++;
                        // Rows: soil and greens alternate across the garden on the near tiles; the distant paint takes it whole.
                        var seen = new HashSet<int>();
                        double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
                        bool alongU = f.HalfX >= f.HalfZ;
                        double half = alongU ? f.HalfZ : f.HalfX;
                        for (double across = -half + 0.5; across < half - 0.5; across += 1.0)
                        {
                            double u = alongU ? 0.0 : across, v = alongU ? across : 0.0;
                            double x = f.X + u * cs + v * sn, z = f.Z - u * sn + v * cs;
                            seen.Add(FieldPatchwork.LookAt(f, seed, x, z, out _, 1f));
                            Assert.That(FieldPatchwork.LookAt(f, seed, x, z, out _, 4f), Is.EqualTo(GroundLook.FieldGarden), $"{at}: distant garden paint");
                        }

                        Assert.That(seen.Contains(GroundLook.FieldGardenSoil) && seen.Contains(GroundLook.FieldGardenGreens), Is.True, $"{at}: no rows");
                    }
                }

                if (orchards > 0) Assert.That(orchardTrees, Is.GreaterThanOrEqualTo(2 * orchards), $"seed {seed}: orchards with too few trees");
            }
        }

        [Test]
        public void CloseItemsAgreeBetweenTilesAndAreDeterministic()
        {
            const int seed = 777;
            EndlessGenerator g = Gen(seed);
            var fresh = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            var a = Closes(g, seed, 1);
            var b = Closes(fresh, seed, 1);
            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int k = 0; k < a.Count; k++)
            {
                Assert.That(b[k].items.Count, Is.EqualTo(a[k].items.Count));
                for (int i = 0; i < a[k].items.Count; i++)
                    Assert.That(b[k].items[i].X == a[k].items[i].X && b[k].items[i].Z == a[k].items[i].Z && b[k].items[i].Pick == a[k].items[i].Pick, Is.True, "close items differ between generators");
            }

            // The fruit trees of every orchard stand in its tiles, near and far alike (no pop-in between distances).
            float t = g.Params.tileSize;
            var planned = a.SelectMany(c => c.items).Where(i => i.Role == PropRole.FruitTree).ToList();
            Assert.That(planned.Count, Is.GreaterThan(0), "no orchard trees round the first village of seed 777");
            foreach (var group in planned.GroupBy(i => (tx: (int)Math.Floor(i.X / t), tz: (int)Math.Floor(i.Z / t))))
            {
                foreach (TileLod lod in new[] { TileLod.Near, TileLod.Far })
                {
                    TileBlueprint bp = g.GenerateTile(new TileCoord(group.Key.tx, group.Key.tz), lod);
                    foreach (CloseItem it in group)
                    {
                        double lx = it.X - group.Key.tx * (double)t, lz = it.Z - group.Key.tz * (double)t;
                        Assert.That(bp.Trees.Any(tr => tr.role == PropRole.FruitTree && Math.Abs(tr.position.x - lx) < 0.01 && Math.Abs(tr.position.z - lz) < 0.01), Is.True, $"{lod} tile {group.Key}: orchard tree at ({it.X:0.0},{it.Z:0.0}) missing");
                    }
                }
            }
        }

        [Test]
        public void LayerSetsPerTileKind()
        {
            var all = Enumerable.Repeat(true, TerrainShaper.EndlessLayerCount - TerrainShaper.LayerCount).ToArray();
            int[] expected = { 8, 11, 11, 14 };
            for (int kind = 0; kind < 4; kind++)
            {
                int[] slots = EndlessLayerSets.ChannelSlots(kind, all, out int[] appended);
                Assert.That(TerrainShaper.LayerCount + appended.Length, Is.EqualTo(expected[kind]), $"kind {kind}");
                Assert.That((TerrainShaper.LayerCount + appended.Length + 3) / 4, Is.EqualTo(kind == 0 ? 2 : kind == 3 ? 4 : 3), "terrain passes");
                for (int c = TerrainShaper.LayerCount; c < TerrainShaper.EndlessLayerCount; c++)
                {
                    bool mountain = c <= TerrainShaper.HeathLayer;
                    bool kept = c != TerrainShaper.LeafLitterLayer && (mountain ? (kind & 1) != 0 : (kind & 2) != 0);   // the leaf litter: EndlessForestFloorTests
                    if (kept) Assert.That(slots[c], Is.GreaterThanOrEqualTo(TerrainShaper.LayerCount), $"kind {kind} channel {c}");
                    else Assert.That(slots[c], Is.LessThan(TerrainShaper.LayerCount), $"kind {kind} channel {c} folds onto a shared layer");
                }

                Assert.That(slots.Where(s => s >= TerrainShaper.LayerCount).Distinct().Count(), Is.EqualTo(appended.Length));
            }

            // A pass-and-farmland tile takes both groups; a missing field layer folds onto its stand-in.
            Assert.That(EndlessLayerSets.LayerSetKind(true, true), Is.EqualTo(3));
            var noGold = (bool[])all.Clone();
            noGold[TerrainShaper.FieldGoldLayer - TerrainShaper.LayerCount] = false;
            int[] s3 = EndlessLayerSets.ChannelSlots(3, noGold, out int[] app3);
            Assert.That(app3.Length, Is.EqualTo(5));
            Assert.That(s3[TerrainShaper.FieldGoldLayer], Is.EqualTo(TerrainShaper.FlowerLayer));
            // The generator's flags: a splat with both kinds of paint asks for the fourteen-layer set.
            var splat = new float[4, 4, TerrainShaper.EndlessLayerCount];
            splat[0, 0, TerrainShaper.ScreeLayer] = 0.5f;
            splat[1, 1, TerrainShaper.FieldGoldLayer] = 0.5f;
            Assert.That(TileGenerator.UsesMountainChannels(splat) && TileGenerator.UsesFieldChannels(splat), Is.True);
            splat[0, 0, TerrainShaper.ScreeLayer] = 0f;
            Assert.That(TileGenerator.UsesMountainChannels(splat), Is.False, "field paint alone is no mountain paint");
        }

        [Test]
        public void TheRingRoundTheHousesIsFilledAndLanesRunOnAsTracks()
        {
            foreach (int seed in Seeds)
            {
                EndlessGenerator g = Gen(seed);
                foreach (Place place in Villages(g))
                {
                    Dressing d = g.Dressings.Settlement(place);
                    SettlementFootprint fp = SettlementFootprint.Of(place, d);
                    List<MosaicField> fields = FieldsAround(g, d.CentreX, d.CentreZ, 420.0);
                    List<MosaicField> closes = fields.Where(f => f.Close).ToList();
                    Assert.That(closes.Count, Is.GreaterThanOrEqualTo(10), $"seed {seed} {place.Name}: {closes.Count} closes");
                    double nearest = double.MaxValue;
                    foreach (MosaicField f in fields)
                    {
                        double rad = f.Yaw * Math.PI / 180.0;
                        double c = fp.Clearance(f.X, f.Z, f.HalfX, f.HalfZ, Math.Cos(rad), Math.Sin(rad));
                        if (f.Close) Assert.That(c, Is.GreaterThanOrEqualTo(FieldMosaic.CloseMargin - 1e-6), $"seed {seed} {place.Name}: a close in the footprint");
                        nearest = Math.Min(nearest, c);
                    }

                    Assert.That(nearest, Is.LessThan(30.0), $"seed {seed} {place.Name}: the nearest enclosure is {nearest:0} m off the footprint");
                    Assert.That(closes.Select(f => f.Look).Distinct().Count(), Is.GreaterThanOrEqualTo(2), $"seed {seed} {place.Name}: one kind of close");
                    var tracks = new List<(double x0, double z0, double x1, double z1, float width)>();
                    FieldPatchwork.Tracks(d, (x, z) => { g.Curve.Nearest(x, z, out double rd); return rd; }, tracks);
                    foreach (var t in tracks)
                    {
                        g.Curve.Nearest(t.x1, t.z1, out double rd);
                        Assert.That(rd, Is.GreaterThanOrEqualTo(FieldPatchwork.TrackRoadClearance - 1e-6), $"seed {seed} {place.Name}: a track reaches the road");
                    }
                }
            }
        }

        [Test]
        public void ThePatchworkIsDeterministic()
        {
            var a = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            var b = new EndlessGenerator(new EndlessParams { seed = 777 }, PaletteKeys.Default);
            Place place = Villages(a)[0];
            Dressing d = a.Dressings.Settlement(place);
            var fa = FieldsAround(a, d.CentreX, d.CentreZ, 400.0);
            var fb = FieldsAround(b, d.CentreX, d.CentreZ, 400.0);
            Assert.That(fb.Select(f => (f.Id, f.Planted, f.NearVillage)), Is.EqualTo(fa.Select(f => (f.Id, f.Planted, f.NearVillage))));
            float t = a.Params.tileSize;
            var coord = new TileCoord((int)Math.Floor(d.CentreX / t) + 1, (int)Math.Floor(d.CentreZ / t));
            TileBlueprint ta = a.GenerateTile(coord, TileLod.Near), tb = b.GenerateTile(coord, TileLod.Near);
            Assert.That(tb.Pieces.Count, Is.EqualTo(ta.Pieces.Count));
            Assert.That(tb.Splat.Cast<float>().SequenceEqual(ta.Splat.Cast<float>()), Is.True);
        }
    }
}
