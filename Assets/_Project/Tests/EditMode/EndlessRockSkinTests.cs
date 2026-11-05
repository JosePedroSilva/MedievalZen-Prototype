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
    /// <summary>The faceted rock skin of the pass ranges (2026-10-06): <see cref="RockSkinMeshData"/> and <see cref="RockSkinField"/>.</summary>
    public class EndlessRockSkinTests
    {
        private const float Tile = 256f, Spacing = 2f, Range = 600f;
        private const int Pad = 12, Nodes = 129 + 2 * Pad;

        /// <summary>A synthetic tile: the absolute height <paramref name="height"/> sampled on the padded 2 m window, rockiness from slope.</summary>
        private static RockSkinMeshData.Input Synthetic(int tileX, int tileZ, Func<double, double, double> height, float rockPaint = 1f)
        {
            var map = new float[Nodes, Nodes];
            double x0 = tileX * (double)Tile - Pad * Spacing, z0 = tileZ * (double)Tile - Pad * Spacing;
            for (int j = 0; j < Nodes; j++)
                for (int i = 0; i < Nodes; i++) map[j, i] = (float)(height(x0 + i * Spacing, z0 + j * Spacing) / Range);
            var rock = new float[Nodes * Nodes];
            for (int j = 1; j < Nodes - 1; j++)
            {
                for (int i = 1; i < Nodes - 1; i++)
                {
                    float dx = (map[j, i + 1] - map[j, i - 1]) * Range / (2f * Spacing), dz = (map[j + 1, i] - map[j - 1, i]) * Range / (2f * Spacing);
                    rock[j * Nodes + i] = RockSkinField.Rockiness(1f, Mathf.Sqrt(dx * dx + dz * dz), 0f, 1000f, 5f);
                }
            }

            var splat = new float[64, 64, TerrainShaper.EndlessLayerCount];
            for (int z = 0; z < 64; z++)
                for (int x = 0; x < 64; x++)
                {
                    splat[z, x, TerrainShaper.MountainRockLayer] = rockPaint;
                    splat[z, x, TerrainShaper.HeathLayer] = 1f - rockPaint;
                }

            return new RockSkinMeshData.Input
            {
                Map = map, Rock = rock, PadCells = Pad, Spacing = Spacing, HeightRange = Range, TileSize = Tile, TileX = tileX, TileZ = tileZ,
                Seed = 12345, Splat = splat, ChannelColors = FarTileMeshData.AppearanceChannelColors, OriginX = tileX * (double)Tile, OriginZ = tileZ * (double)Tile,
            };
        }

        // A range: flat floor, a 60-degree cliff band, a 35-degree slope above, a ridge; varying along z so nothing is a straight line.
        private static double Range1(double x, double z)
        {
            double u = x + 20.0 * Math.Sin(z * 0.013);
            double h = 0.0;
            h += Math.Max(0.0, Math.Min(u - 300.0, 40.0)) * Math.Tan(60.0 * Math.PI / 180.0);
            h += Math.Max(0.0, Math.Min(u - 340.0, 100.0)) * Math.Tan(35.0 * Math.PI / 180.0);
            h += 6.0 * Math.Sin(x * 0.05) * Math.Cos(z * 0.041);
            return h + 50.0;
        }

        /// <summary>Height of the terrain's own triangle surface at tile-local (x, z) of <paramref name="input"/>.</summary>
        private static float Terrain(RockSkinMeshData.Input input, float x, float z)
        {
            float gx = x / Spacing + Pad, gz = z / Spacing + Pad;
            int i = Mathf.Clamp(Mathf.FloorToInt(gx), 0, Nodes - 2), j = Mathf.Clamp(Mathf.FloorToInt(gz), 0, Nodes - 2);
            float fx = gx - i, fz = gz - j;
            float ha = input.Map[j, i] * Range, hb = input.Map[j, i + 1] * Range, hc = input.Map[j + 1, i] * Range, hd = input.Map[j + 1, i + 1] * Range;
            return fx >= fz ? ha + (hb - ha) * fx + (hd - hb) * fz : ha + (hd - hc) * fx + (hc - ha) * fz;
        }

        [Test]
        public void SkinNeverLiesBelowTheTerrain()
        {
            RockSkinMeshData.Input input = Synthetic(1, 0, Range1);
            RockSkinMeshData d = RockSkinMeshData.Build(input);
            Assert.NotNull(d);
            int below = 0, samples = 0;
            for (int t = 0; t < d.IndexCount; t += 3)
            {
                Vector3 a = d.Vertices[t].position, b = d.Vertices[t + 1].position, c = d.Vertices[t + 2].position;
                foreach (Vector3 w in new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(1f, 1f, 1f) / 3f, new Vector3(0.5f, 0.5f, 0f), new Vector3(0f, 0.5f, 0.5f), new Vector3(0.5f, 0f, 0.5f) })
                {
                    Vector3 p = a * w.x + b * w.y + c * w.z;
                    samples++;
                    if (p.y < Terrain(input, p.x, p.z) - 0.02f) below++;
                }
            }

            // Corners never; face interiors only by a hair where a tile-edge vertex cannot be raised.
            Assert.Less(below, samples / 500 + 1, $"{below} of {samples} skin samples below the terrain");
            for (int k = 0; k < d.VertexCount; k++)
            {
                Vector3 p = d.Vertices[k].position;
                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(d.Vertices[k].normal.y), "no NaN");
                Assert.AreEqual(1f, d.Vertices[k].normal.magnitude, 1e-3f, "unit normal");
            }
        }

        [Test]
        public void NeighbouringTilesAgreeAlongTheirEdge()
        {
            // The range moved so its cliff band straddles x = 512, the edge between tiles 1 and 2.
            Func<double, double, double> shifted = (x, z) => Range1(x - 190.0, z);
            RockSkinMeshData a = RockSkinMeshData.Build(Synthetic(1, 0, shifted)), b = RockSkinMeshData.Build(Synthetic(2, 0, shifted));
            RockSkinMeshData c = RockSkinMeshData.Build(Synthetic(1, 1, shifted));
            int shared = 0;
            foreach (KeyValuePair<(long, long), Vector3> e in a.EdgePoints)
            {
                if (b.EdgePoints.TryGetValue(e.Key, out Vector3 q))
                {
                    shared++;
                    Assert.Less((e.Value - (q + new Vector3(Tile, 0f, 0f))).magnitude, 2e-3f, $"edge point {e.Key}");
                }

                if (c.EdgePoints.TryGetValue(e.Key, out Vector3 r))
                {
                    shared++;
                    Assert.Less((e.Value - (r + new Vector3(0f, 0f, Tile))).magnitude, 2e-3f, $"edge point {e.Key}");
                }
            }

            Assert.Greater(shared, 20, "the cliff crosses both shared edges");
        }

        [Test]
        public void ReliefIsSmallWhereThePlayerCanWalk()
        {
            RockSkinMeshData d = RockSkinMeshData.Build(Synthetic(1, 0, Range1));
            Assert.LessOrEqual(d.MaxWalkableRelief, 0.2f + 1e-4f);
            Assert.Greater(d.MaxRelief, 0.8f, "steep faces carry real relief");
            Assert.LessOrEqual(d.MaxRelief, RockSkinMeshData.MaxCliffRelief + RockSkinMeshData.Lift + 1e-4f);
        }

        [Test]
        public void CoversSteepRockAndNotTheFloor()
        {
            RockSkinMeshData d = RockSkinMeshData.Build(Synthetic(1, 0, Range1));
            // Tile 1 spans x 256..512: the cliff (300-340) and the 35-degree slope (340-440) of every row, give or take the bend.
            float steep = 140f * Tile;
            Assert.Greater(d.CoveredArea, 0.7f * steep, "rock covered");
            Assert.Less(d.CoveredArea, 1.25f * steep, "floor not covered");
            Assert.IsNull(RockSkinMeshData.Build(Synthetic(0, 0, (x, z) => 50.0 + 0.1 * x)), "gentle ground: no skin");
        }

        [Test]
        public void TrianglesStayUnderTheCap()
        {
            // Rock everywhere: a 60-degree sawtooth.
            RockSkinMeshData d = RockSkinMeshData.Build(Synthetic(3, 2, (x, z) => 100.0 + Math.Abs(((x % 40.0) + 40.0) % 40.0 - 20.0) * 1.7));
            Assert.NotNull(d);
            Assert.LessOrEqual(d.Triangles, RockSkinMeshData.MaxTriangles);
            Assert.AreEqual(d.Triangles * 3, d.VertexCount);
        }

        [Test]
        public void IsDeterministic()
        {
            RockSkinMeshData a = RockSkinMeshData.Build(Synthetic(1, 0, Range1)), b = RockSkinMeshData.Build(Synthetic(1, 0, Range1));
            Assert.AreEqual(a.VertexCount, b.VertexCount);
            for (int k = 0; k < a.VertexCount; k += 97)
            {
                Assert.AreEqual(a.Vertices[k].position, b.Vertices[k].position);
                Assert.AreEqual(a.Vertices[k].color, b.Vertices[k].color);
            }
        }

        [Test]
        public void RockColourAgreesWithTheFarTiles()
        {
            RockSkinMeshData d = RockSkinMeshData.Build(Synthetic(1, 0, Range1));
            Vector3 sum = Vector3.zero;
            for (int k = 0; k < d.VertexCount; k += 3) sum += Decode(d.Vertices[k].color);
            Vector3 mean = sum / (d.VertexCount / 3);
            Vector3 far = FarTileMeshData.ZoneColor(TerrainShaper.MountainRockLayer, new FarMeshSettings());
            float dE = GroundLook.DeltaE(mean, far);
            Assert.Less(dE, 4f, $"near skin mean {mean} vs far rock {far}: dE {dE:0.0}");
        }

        [Test]
        public void SkinStaysOffTheRoad()
        {
            Assert.AreEqual(0f, RockSkinField.Rockiness(1f, 2f, 0f, 2.5f + 2.5f, 5f), "no skin within the verge and margin");
            Assert.AreEqual(1f, RockSkinField.Rockiness(1f, 2f, 0f, 30f, 5f), 1e-4f);
            Assert.AreEqual(0f, RockSkinField.Rockiness(0f, 2f, 0f, 30f, 5f), "only in a range");
        }

        [Test, Category("Slow")]
        public void RealRangeTilesKeepTheSkinAboveTheTerrain()
        {
            var g = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);
            Region pass = g.Regions.At(0.0);
            while (pass.Kind != RegionKind.HighlandPass) pass = g.Regions.Get(pass.Index + 1);
            float ts = g.Params.tileSize;
            int skins = 0, samples = 0, below = 0;
            float worst = 0f;
            for (double s = pass.Start + 600.0; s < pass.End - 600.0 && skins < 6; s += 700.0)
            {
                RoadPoint rp = g.Road(s);
                double rad = rp.HeadingDeg * Math.PI / 180.0;
                TileCoord c = TileCoord.Containing(rp.X + Math.Cos(rad) * 200.0, rp.Z - Math.Sin(rad) * 200.0, ts);
                TileBlueprint bp = g.GenerateTile(c, TileLod.Near);
                if (bp.RockSkin == null) continue;
                skins++;
                Assert.LessOrEqual(bp.RockSkin.Triangles, RockSkinMeshData.MaxTriangles);
                Assert.LessOrEqual(bp.RockSkin.MaxWalkableRelief, 0.2f + 1e-4f);
                int n = bp.Heights.GetLength(0);
                float sp = ts / (n - 1);
                for (int t = 0; t < bp.RockSkin.IndexCount; t += 3)
                {
                    Vector3 a = bp.RockSkin.Vertices[t].position, b = bp.RockSkin.Vertices[t + 1].position, d = bp.RockSkin.Vertices[t + 2].position;
                    foreach (Vector3 p in new[] { a, (a + b + d) / 3f })
                    {
                        if (p.x < 0f || p.z < 0f || p.x > ts || p.z > ts) continue;
                        float gx = p.x / sp, gz = p.z / sp;
                        int i = Mathf.Min(n - 2, (int)gx), j = Mathf.Min(n - 2, (int)gz);
                        float fx = gx - i, fz = gz - j, r = bp.HeightRange;
                        float ha = bp.Heights[j, i] * r, hb = bp.Heights[j, i + 1] * r, hc = bp.Heights[j + 1, i] * r, hd = bp.Heights[j + 1, i + 1] * r;
                        float h = fx >= fz ? ha + (hb - ha) * fx + (hd - hb) * fz : ha + (hd - hc) * fx + (hc - ha) * fz;
                        samples++;
                        worst = Mathf.Max(worst, h - p.y);
                        if (p.y < h - 0.05f) below++;
                    }
                }
            }

            Assert.Greater(skins, 0, "range tiles with skin");
            Assert.Less(below, samples / 500 + 1, $"{below} of {samples} skin samples more than 5 cm below the terrain (worst {worst:0.00} m)");
        }

        [Test, Category("Slow")]
        public void RealNeighbouringTilesAgreeAlongTheirEdge()
        {
            var g = new EndlessGenerator(new EndlessParams { seed = 42 }, PaletteKeys.Default);
            Region pass = g.Regions.At(0.0);
            while (pass.Kind != RegionKind.HighlandPass) pass = g.Regions.Get(pass.Index + 1);
            float ts = g.Params.tileSize;
            int shared = 0;
            for (double s = pass.Start + 600.0; s < pass.End - 600.0 && shared < 40; s += 500.0)
            {
                RoadPoint rp = g.Road(s);
                double rad = rp.HeadingDeg * Math.PI / 180.0;
                TileCoord c = TileCoord.Containing(rp.X + Math.Cos(rad) * 250.0, rp.Z - Math.Sin(rad) * 250.0, ts);
                RockSkinMeshData a = g.GenerateTile(c, TileLod.Near).RockSkin;
                if (a == null) continue;
                RockSkinMeshData east = g.GenerateTile(new TileCoord(c.X + 1, c.Z), TileLod.Near).RockSkin;
                RockSkinMeshData north = g.GenerateTile(new TileCoord(c.X, c.Z + 1), TileLod.Near).RockSkin;
                foreach (KeyValuePair<(long, long), Vector3> e in a.EdgePoints)
                {
                    if (east != null && east.EdgePoints.TryGetValue(e.Key, out Vector3 q))
                    {
                        shared++;
                        Assert.Less((e.Value - (q + new Vector3(ts, 0f, 0f))).magnitude, 0.01f, $"tile {c} east edge point {e.Key}");
                    }

                    if (north != null && north.EdgePoints.TryGetValue(e.Key, out Vector3 r))
                    {
                        shared++;
                        Assert.Less((e.Value - (r + new Vector3(0f, 0f, ts))).magnitude, 0.01f, $"tile {c} north edge point {e.Key}");
                    }
                }
            }

            Assert.Greater(shared, 0, "some skin crosses a tile edge");
        }

        private static Vector3 Decode(Color32 c) => new Vector3(Lin(c.r), Lin(c.g), Lin(c.b));

        private static float Lin(byte b)
        {
            float s = b / 255f;
            return s <= 0.04045f ? s / 12.92f : Mathf.Pow((s + 0.055f) / 1.055f, 2.4f);
        }
    }
}
