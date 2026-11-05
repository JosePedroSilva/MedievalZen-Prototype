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
    /// <summary>
    /// The pass ranges' moderate slopes and steep faces (2026-10-06): the 10-35 degree slopes read as smooth tan dunes (62 % one heath
    /// paint in patches of up to 7 ha, sRGB 107, 100, 63, few objects) with contour stripes from weak cliff bands; the steep faces smeared
    /// the rock texture. Now: darker heath broken by upland grass, peat, mountain-rock outcrops with scree skirts, crags, more pines and
    /// low shrubs; flank bands only on steep flanks; a triplanar rock skin over faces over ~50 degrees. Pure (no Unity objects).
    /// </summary>
    public sealed class EndlessPassSlopesTests
    {
        // "Sand": a dry tan sand, sRGB (178, 160, 120).
        private static readonly Vector3 Sand = new Vector3(0.4452f, 0.3515f, 0.1878f);

        private static Region FirstPass(EndlessGenerator g)
        {
            Region r = g.Regions.At(0.0);
            while (r.Kind != RegionKind.HighlandPass) r = g.Regions.Get(r.Index + 1);
            return r;
        }

        private static List<TileCoord> RangeTiles(EndlessGenerator g, Region pass, double step)
        {
            float ts = g.Params.tileSize;
            var set = new List<TileCoord>();
            for (double s = pass.Start + TerrainField.FoothillReach; s <= pass.End - TerrainField.FoothillReach; s += step)
            {
                RoadPoint rp = g.Road(s);
                double rad = rp.HeadingDeg * Math.PI / 180.0;
                foreach (double v in new[] { -300.0, -128.0, 128.0, 300.0 })
                {
                    TileCoord c = TileCoord.Containing(rp.X + Math.Cos(rad) * v, rp.Z - Math.Sin(rad) * v, ts);
                    if (!set.Contains(c)) set.Add(c);
                }
            }

            return set;
        }

        [Test]
        public void HeathReadsAsUplandVegetationNotSand()
        {
            Vector3 heath = GroundLook.LayerColor(TerrainShaper.HeathLayer);
            Vector3 lab = GroundLook.Lab(heath);
            Assert.Less(lab.x, 40f, "heath lightness");
            Assert.Less(lab.y, 0f, "heath leans green (a* < 0)");
            Assert.Greater(GroundLook.DeltaE(heath, Sand), 30f, "heath against sand");
        }

        [Test, Category("Slow")]
        public void ModerateSlopesAreBrokenUp([Values(12345, 42, 777)] int seed)
        {
            var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            Region pass = FirstPass(g);
            var share = new double[TerrainShaper.EndlessLayerCount];
            var lum = new List<float>();
            Vector3[] colors = GroundLook.LayerColors();
            long cells = 0;
            foreach (TileCoord c in RangeTiles(g, pass, 320.0))
            {
                TileBlueprint bp = g.GenerateTile(c, TileLod.Near);
                if (bp.Splat.GetLength(2) < TerrainShaper.EndlessLayerCount) continue;
                int n = bp.Heights.GetLength(0), sres = bp.Splat.GetLength(0);
                float sp = bp.TileSize / (n - 1);
                for (int j = 1; j < n - 1; j += 2)
                {
                    for (int i = 1; i < n - 1; i += 2)
                    {
                        Vector3 nrm = RockSkinMeshData.Normal(bp.Heights, n, i, j, sp, bp.HeightRange);
                        float slope = Mathf.Acos(nrm.y) * Mathf.Rad2Deg;
                        if (slope < 10f || slope > 35f) continue;
                        double x = c.OriginX(bp.TileSize) + i * sp, z = c.OriginZ(bp.TileSize) + j * sp;
                        TerrainField.Row row = g.Terrain.BaseRow(z);
                        if (row.S < pass.Start + TerrainField.FoothillReach || row.S > pass.End - TerrainField.FoothillReach) continue;
                        if (Math.Abs((x - row.RoadX) * row.Cos) < 20.0 || bp.Heights[j, i] * bp.HeightRange - row.Road < 8f) continue;
                        int si = Math.Min(sres - 1, i * sres / (n - 1)), sj = Math.Min(sres - 1, j * sres / (n - 1));
                        Vector3 albedo = Vector3.zero;
                        for (int l = 0; l < share.Length; l++)
                        {
                            share[l] += bp.Splat[sj, si, l];
                            albedo += colors[l] * bp.Splat[sj, si, l];
                        }

                        lum.Add(GroundLook.Lab(albedo).x);
                        cells++;
                    }
                }
            }

            Assert.Greater(cells, 2000, "moderate range slopes sampled");
            double Share(int l) => share[l] / cells;
            float mean = lum.Average();
            double sd = Math.Sqrt(lum.Sum(v => (v - mean) * (v - mean)) / lum.Count);
            Assert.Less(Share(TerrainShaper.HeathLayer), 0.56, "heath share (was 0.60-0.62)");
            Assert.Greater(Share(TerrainShaper.MountainRockLayer), 0.08, "mountain-rock outcrops (was 0.04-0.05)");
            Assert.Greater(Share(TerrainShaper.ScreeLayer), 0.12, "scree (was 0.11-0.12)");
            Assert.That(Share(TerrainShaper.MudLayer), Is.InRange(0.02, 0.10), "peat patches");
            Assert.Greater(sd, 6.5, "L* variation between cells (was 5.2-5.4)");
        }

        [Test]
        public void FarZonesMatchTheNearPaint()
        {
            var g = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);
            Region pass = FirstPass(g);
            var settings = new FarMeshSettings();
            Vector3[] colors = GroundLook.LayerColors();
            Vector3 near = Vector3.zero, far = Vector3.zero;
            int nn = 0, nf = 0;
            foreach (TileCoord c in RangeTiles(g, pass, 900.0).Take(6))
            {
                TileBlueprint bp = g.GenerateTile(c, TileLod.Near);
                foreach (float[] w in Samples(bp)) { for (int l = 0; l < w.Length; l++) near += colors[l] * w[l]; nn++; }
                TileBlueprint fb = g.GenerateTile(c, TileLod.Far);
                uint hash = 1;
                foreach (float[] w in Samples(fb))
                {
                    Vector3 mix = Vector3.zero;
                    for (int l = 0; l < w.Length; l++) mix += colors[l] * w[l];
                    hash = hash * 1664525u + 1013904223u;
                    int zone = FarTileMeshData.ChooseZone(w, 0f, 0f, hash, settings.ZoneDither, settings.CanopyThreshold);
                    far += Vector3.Lerp(FarTileMeshData.ZoneColor(zone, settings), mix, settings.BlendPull);
                    nf++;
                }
            }

            Assert.Less(GroundLook.DeltaE(near / nn, far / nf), 2.5f, "near / far mean colour over the range");
        }

        private static IEnumerable<float[]> Samples(TileBlueprint bp)
        {
            int n = bp.Splat.GetLength(0), ch = bp.Splat.GetLength(2);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                var w = new float[ch];
                float total = 0f;
                for (int l = 0; l < ch; l++) total += w[l] = bp.Splat[z, x, l];
                for (int l = 0; l < ch; l++) w[l] /= Mathf.Max(1e-6f, total);
                yield return w;
            }
        }

        [Test, Category("Slow")]
        public void NewObjectsKeepOffTheRoad()
        {
            var g = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);
            Region pass = FirstPass(g);
            int count = 0;
            foreach (TileCoord c in RangeTiles(g, pass, 480.0))
            {
                TileBlueprint bp = g.GenerateTile(c, TileLod.Near);
                double ox = c.OriginX(bp.TileSize), oz = c.OriginZ(bp.TileSize);
                var points = bp.Trees.Where(t => t.role == PropRole.Shrub || t.role == PropRole.Bush || t.role == PropRole.PineTree).Select(t => (t.role, t.position))
                    .Concat(bp.Props.Where(p => p.role == PropRole.PassCliff || p.role == PropRole.PassBoulder).Select(p => (p.role, p.position)));
                foreach (var (role, pos) in points)
                {
                    double x = ox + pos.x, z = oz + pos.z;
                    double s = g.NearestS(x, z, g.Terrain.BaseRow(z).S);
                    RoadPoint rp = g.Road(s);
                    double d = Math.Sqrt((x - rp.X) * (x - rp.X) + (z - rp.Z) * (z - rp.Z));
                    Assert.Greater(d, 6.0, $"{role} at s {s:0} is {d:0.0} m from the road's centre");
                    count++;
                }
            }

            Assert.Greater(count, 100);
        }

        // The rock skin (faceted since 2026-10-06) has its own tests: EndlessRockSkinTests.

        [Test, Category("Slow")]
        public void RangeTilesAreDeterministic()
        {
            var g1 = new EndlessGenerator(new EndlessParams { seed = 42 }, PaletteKeys.Default);
            var g2 = new EndlessGenerator(new EndlessParams { seed = 42 }, PaletteKeys.Default);
            TileCoord c = RangeTiles(g1, FirstPass(g1), 900.0)[1];
            TileBlueprint a = g1.GenerateTile(c, TileLod.Near), b = g2.GenerateTile(c, TileLod.Near);
            Assert.AreEqual(a.Trees.Count, b.Trees.Count);
            Assert.AreEqual(a.Props.Count, b.Props.Count);
            Assert.AreEqual(a.RockSkin?.VertexCount ?? 0, b.RockSkin?.VertexCount ?? 0);
            for (int i = 0; i < a.Props.Count; i++) Assert.AreEqual(a.Props[i].position, b.Props[i].position);
        }
    }
}
