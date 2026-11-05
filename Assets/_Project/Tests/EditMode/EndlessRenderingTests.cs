using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The endless road's distant tiles as plain faceted meshes (<see cref="FarTileMeshData"/>), built from real generator blueprints
    /// (<see cref="TileLod.Mid"/> on the 4 m grid, <see cref="TileLod.Far"/> on the 8 m grid): counts, flat per-face colours under either
    /// provoking-vertex convention, heights and edge stitching, crack-free seams between grids, skirts, ground zones and their colours,
    /// jitter, diagonals, tree impostors (every tree, measured shapes), rocks and houses, the triangle cap and determinism. Pure.
    /// </summary>
    public sealed class FarTileMeshTests
    {
        private static EndlessGenerator generator;

        private static EndlessGenerator Gen() => generator ??= new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);

        /// <summary>The tile on the road at road distance <paramref name="s"/>, offset sideways by <paramref name="dx"/> tiles.</summary>
        private static TileCoord TileOnRoad(double s, int dx = 0)
        {
            RoadPoint r = Gen().Road(s);
            TileCoord c = TileCoord.Containing(r.X, r.Z, Gen().Params.tileSize);
            return new TileCoord(c.X + dx, c.Z);
        }

        /// <summary>A distant tile in the first forest region with plenty of trees.</summary>
        private static TileBlueprint ForestTile(TileLod lod = TileLod.Far)
        {
            for (double s = 0; s < 40000; s += 250)
            {
                if (Gen().Region(s).Kind != RegionKind.Forest) continue;
                for (int dx = -3; dx <= 3; dx++)
                {
                    TileBlueprint bp = Gen().GenerateTile(TileOnRoad(s + 600, dx), lod);
                    if (bp.Trees.Count >= 400) return bp;
                }
            }

            Assert.Fail("no forest tile with 400+ trees found");
            return null;
        }

        private static FarTileMeshData Build(TileBlueprint bp, FarMeshSettings settings = null) =>
            FarTileMeshData.Build(bp, FarTreeStyles.Defaults, settings ?? new FarMeshSettings());

        [Test]
        public void CountsMatchTheGridTheSkirtTheSolidsAndTheTrees([Values(TileLod.Far, TileLod.Mid)] TileLod lod)
        {
            TileBlueprint bp = Gen().GenerateTile(TileOnRoad(300), lod);
            FarTileMeshData d = Build(bp);
            int res = bp.Heights.GetLength(0);
            Assert.That(res, Is.EqualTo(lod == TileLod.Mid ? Gen().Params.midHeightResolution : Gen().Params.farHeightResolution));
            Assert.That(d.GroundTriangles, Is.EqualTo(2 * (res - 1) * (res - 1)));
            Assert.That(d.SkirtTriangles, Is.EqualTo(8 * (res - 1)));
            bool detailed = lod == TileLod.Mid;
            int treeTris = 0, treeVerts = 0;
            foreach (TreePlacement t in bp.Trees)
            {
                FarTreeStyle treeStyle = FarTreeStyles.Defaults.Resolve(t.role, t.variant);
                treeTris += FarTileMeshData.TrianglesOf(treeStyle, detailed);
                treeVerts += FarTileMeshData.VerticesOf(treeStyle, detailed);
            }

            Assert.That(d.TreesThinned, Is.EqualTo(0), "a start tile fits the default cap");
            Assert.That(d.TreesDrawn, Is.EqualTo(bp.Trees.Count));
            Assert.That(d.TreeTriangles, Is.EqualTo(treeTris));
            Assert.That(d.RockTriangles, Is.EqualTo(d.RocksDrawn * FarTileMeshData.RockTris));
            int houseVerts = 0, houseTris = 0;
            foreach (PropPlacement p in bp.Props)
            {
                if (!FarTileMeshData.IsSolid(p.role)) continue;
                FarPropKind kind = FarPropStyles.Defaults.Resolve(p.role, p.variant).Kind;
                houseVerts += FarTileMeshData.HouseVerticesOf(kind);
                houseTris += FarTileMeshData.HouseTrianglesOf(kind);
            }

            Assert.That(d.HouseTriangles, Is.EqualTo(houseTris));
            Assert.That(d.VertexCount, Is.EqualTo(FarTileMeshData.GroundAndSkirtVertices(res) + treeVerts + d.RocksDrawn * FarTileMeshData.RockVerts + houseVerts));
            Assert.That(d.IndexCount, Is.EqualTo(3 * (d.GroundTriangles + d.SkirtTriangles + d.RockTriangles + d.HouseTriangles + d.TreeTriangles)));
            Assert.That(d.Vertices.Length, Is.EqualTo(d.VertexCount));
            Assert.That(d.Indices.Length, Is.EqualTo(d.IndexCount));
            Assert.That(d.VertexCount, Is.LessThanOrEqualTo(ushort.MaxValue));
            foreach (ushort i in d.Indices) Assert.That(i, Is.LessThan(d.VertexCount));
            Assert.That(d.FaceZones.Length, Is.EqualTo(d.GroundTriangles));
        }

        [Test]
        public void EveryGroundTriangleIsOneFlatColourUnderEitherProvokingConvention([Values(TileLod.Far, TileLod.Mid)] TileLod lod)
        {
            TileBlueprint bp = ForestTile(lod);
            FarTileMeshData d = Build(bp);
            int seasonFaces = 0, woodFloorFaces = 0;
            for (int t = 0; t < d.GroundTriangles + d.SkirtTriangles; t++)
            {
                Color32 first = d.Vertices[d.Indices[3 * t]].color, last = d.Vertices[d.Indices[3 * t + 2]].color;
                Assert.That(last, Is.EqualTo(first), $"triangle {t}: first and last vertex carry the face colour");
                // Seasons pass: the alpha carries the face's season class (and so survives either convention with the colour); never foliage.
                Assert.That(TreeLobes.IsLeafCrownAlpha(first.a), Is.False, $"triangle {t}: the ground is never foliage");
                int seasonClass = MedievalZen.Environment.SeasonLook.ClassOf(first.a);
                if (seasonClass != 0) seasonFaces++;
                if (seasonClass == MedievalZen.Environment.SeasonLook.ClassWoodFloor) woodFloorFaces++;
            }

            Assert.That(seasonFaces, Is.GreaterThan(0), "a forest tile's ground has seasonal faces");
            Assert.That(woodFloorFaces, Is.GreaterThan(0), "a forest tile's floor takes the wood floor's season class");

            // Two triangles of a quad mostly differ (zones, jitter): the ground is not smooth vertex colour.
            int differ = 0;
            for (int q = 0; q < d.GroundTriangles / 2; q++)
            {
                if (!d.Vertices[d.Indices[6 * q]].color.Equals(d.Vertices[d.Indices[6 * q + 3]].color)) differ++;
            }

            Assert.That(differ, Is.GreaterThan(d.GroundTriangles / 4), "per-face colours");
        }

        [Test]
        public void LeafCrownsAreFoliageWithALitTopUnderEitherProvokingConvention([Values(TileLod.Far, TileLod.Mid)] TileLod lod)
        {
            // Distant-tree pass (2026-10-06 evening): a leaf crown's faces carry alpha 0 (the far shader's foliage light) and the lit-top
            // ladder; every one of its triangles starts and ends on one colour, and nothing else is foliage.
            TileBlueprint bp = ForestTile(lod);
            FarTileMeshData d = Build(bp);
            int leafCrowns = 0;
            foreach (TreePlacement t in bp.Trees)
            {
                if (FarTreeStyles.Defaults.Resolve(t.role, t.variant).LeafCrown) leafCrowns++;
            }

            int foliage = 0, shades = 0;
            var tones = new HashSet<Color32>();
            for (int t = 0; t < d.IndexCount / 3; t++)
            {
                Color32 first = d.Vertices[d.Indices[3 * t]].color, last = d.Vertices[d.Indices[3 * t + 2]].color;
                if (!TreeLobes.IsLeafCrownAlpha(first.a) && !TreeLobes.IsLeafCrownAlpha(last.a)) continue;   // any season class (seasons pass)
                Assert.That(t, Is.GreaterThanOrEqualTo(d.GroundTriangles + d.SkirtTriangles), "the ground is never foliage");
                Assert.That(last, Is.EqualTo(first), $"triangle {t}: first and last vertex carry the face colour");
                foliage++;
                if (tones.Add(first)) shades++;
            }

            if (leafCrowns == 0)
            {
                Assert.That(foliage, Is.EqualTo(0), "no foliage without a leaf crown");
                return;
            }

            Assert.That(foliage, Is.GreaterThan(0), "leaf crowns are foliage");
            Assert.That(shades, Is.GreaterThanOrEqualTo(lod == TileLod.Mid ? 3 : 2), "light, mid and shade tones");
        }

        [Test]
        public void GroundSitsOnTheHeightsMinusTheDropAndItsEdgesOnTheEightMetreGrid()
        {
            TileBlueprint bp = Gen().GenerateTile(TileOnRoad(800), TileLod.Mid);
            FarTileMeshData d = Build(bp);
            int res = bp.Heights.GetLength(0);
            float range = bp.HeightRange, drop = FarTileMeshData.DefaultDrop;
            float[] stitched = FarTileMeshData.EdgeStitchedHeights(bp.Heights, range, bp.TileSize);
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    bool edge = i == 0 || j == 0 || i == res - 1 || j == res - 1;
                    bool odd = (i % 2 == 1 && (j == 0 || j == res - 1)) || (j % 2 == 1 && (i == 0 || i == res - 1));
                    float expected = bp.Heights[j, i] * range;
                    if (odd)
                    {
                        float a = j == 0 || j == res - 1 ? bp.Heights[j, i - 1] * range : bp.Heights[j - 1, i] * range;
                        float b = j == 0 || j == res - 1 ? bp.Heights[j, i + 1] * range : bp.Heights[j + 1, i] * range;
                        expected = (a + b) * 0.5f;
                    }

                    // Within a hair: Mono and CoreCLR differ in the last bit of the mean (Unity's own test run, 2026-10-06).
                    Assert.That(stitched[j * res + i], Is.EqualTo(expected).Within(1e-4f), $"({i},{j}) {(edge ? "edge" : "inside")}");
                }
            }

            // Quad (i, j)'s first vertex is its (i, j) corner.
            float spacing = bp.TileSize / (res - 1);
            for (int q = 0; q < (res - 1) * (res - 1); q += 37)
            {
                int i = q % (res - 1), j = q / (res - 1);
                Vector3 p = d.Vertices[4 * q].position;
                Assert.That(p.x, Is.EqualTo(i * spacing).Within(1e-4f));
                Assert.That(p.z, Is.EqualTo(j * spacing).Within(1e-4f));
                Assert.That(p.y, Is.EqualTo(stitched[j * res + i] - drop).Within(1e-3f));
            }

            for (int t = 0; t < d.GroundTriangles; t++) Assert.That(Normal(d, t).y, Is.GreaterThan(0f), $"ground triangle {t} faces up");
        }

        [Test]
        public void NeighboursMeetWithoutCracksOnEitherGrid()
        {
            // a (Mid) | b (Far) along x; a | c (Mid) along z; b | e (Far) along z. Every vertex of an edge lies on the neighbour's edge
            // polyline, bit-identical where the two share a sample.
            TileCoord a = TileOnRoad(2600), b = new TileCoord(a.X + 1, a.Z), c = new TileCoord(a.X, a.Z + 1), e = new TileCoord(a.X + 1, a.Z + 1);
            float ts = Gen().Params.tileSize;
            TileBlueprint ba = Gen().GenerateTile(a, TileLod.Mid), bb = Gen().GenerateTile(b, TileLod.Far), bc = Gen().GenerateTile(c, TileLod.Mid), be = Gen().GenerateTile(e, TileLod.Far);
            float[] ha = FarTileMeshData.EdgeStitchedHeights(ba.Heights, ba.HeightRange, ts), hb = FarTileMeshData.EdgeStitchedHeights(bb.Heights, bb.HeightRange, ts);
            float[] hc = FarTileMeshData.EdgeStitchedHeights(bc.Heights, bc.HeightRange, ts), he = FarTileMeshData.EdgeStitchedHeights(be.Heights, be.HeightRange, ts);
            int rm = ba.Heights.GetLength(0), rf = bb.Heights.GetLength(0);
            Assert.That(rm, Is.EqualTo(2 * rf - 1));
            for (int k = 0; k < rm; k++)
            {
                // a's +X edge (x = ts) against b's −X edge (x = 0): b has a sample every second a-sample.
                float ya = ha[k * rm + rm - 1];
                float yb = k % 2 == 0 ? hb[(k / 2) * rf] : (hb[(k / 2) * rf] + hb[(k / 2 + 1) * rf]) * 0.5f;
                Assert.That(ya, Is.EqualTo(yb), $"Mid|Far seam at k = {k}");
                // a's +Z edge against c's −Z edge (both Mid): identical.
                Assert.That(hc[k], Is.EqualTo(ha[(rm - 1) * rm + k]), $"Mid|Mid seam at k = {k}");
            }

            for (int k = 0; k < rf; k++) Assert.That(he[k], Is.EqualTo(hb[(rf - 1) * rf + k]), $"Far|Far seam at k = {k}");
        }

        [Test]
        public void SkirtHangsBelowEveryEdgeAndFacesOutward()
        {
            TileBlueprint bp = Gen().GenerateTile(TileOnRoad(1200), TileLod.Mid);
            var settings = new FarMeshSettings();
            FarTileMeshData d = Build(bp, settings);
            var outward = new[] { new Vector3(0f, 0f, -1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(-1f, 0f, 0f) };
            float ts = bp.TileSize;
            for (int t = d.GroundTriangles; t < d.GroundTriangles + d.SkirtTriangles; t++)
            {
                Vector3 n = Normal(d, t);
                Vector3 c = Centroid(d, t);
                Vector3 o = c.z < 1e-3f ? outward[0] : c.x > ts - 1e-3f ? outward[1] : c.z > ts - 1e-3f ? outward[2] : outward[3];
                Assert.That(Vector3.Dot(n, o), Is.GreaterThan(0f), $"skirt triangle {t}");
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int k = 0; k < 3; k++)
                {
                    float y = d.Vertices[d.Indices[3 * t + k]].position.y;
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }

                Assert.That(maxY - minY, Is.GreaterThanOrEqualTo(settings.SkirtDepth - 1e-3f), "the skirt hangs its full depth");
            }
        }

        [Test]
        public void ZonesFollowThePaintCanopyAndWaterDeterministically()
        {
            var w = new float[TerrainShaper.LayerCount];
            w[TerrainShaper.RockLayer] = 0.85f;
            w[TerrainShaper.DryGrassLayer] = 0.15f;
            for (uint h = 0; h < 200; h++)
            {
                Assert.That(FarTileMeshData.ChooseZone(w, 0f, 0f, h, 0.2f), Is.EqualTo(TerrainShaper.RockLayer), "a clear majority wins whatever the dither");
                Assert.That(FarTileMeshData.ChooseZone(w, 0.9f, 0f, h, 0.2f), Is.EqualTo(FarTileMeshData.ZoneCanopy), "dense woods: the canopy shade");
                Assert.That(FarTileMeshData.ChooseZone(w, 0f, 0.9f, h, 0.2f), Is.EqualTo(FarTileMeshData.ZoneWater), "on the stream: water");
                Assert.That(FarTileMeshData.ChooseZone(w, 0.1f, 0.1f, h, 0.2f), Is.EqualTo(TerrainShaper.RockLayer), "light cover, far from water: the paint");
            }

            // A 50/50 blend comes out as a mix of whole faces, about half each, the same every time.
            w[TerrainShaper.RockLayer] = 0.5f;
            w[TerrainShaper.DryGrassLayer] = 0.5f;
            int rock = 0;
            for (uint h = 0; h < 2000; h++)
            {
                int z = FarTileMeshData.ChooseZone(w, 0f, 0f, h * 2654435761u, 0.2f);
                Assert.That(z, Is.EqualTo(FarTileMeshData.ChooseZone(w, 0f, 0f, h * 2654435761u, 0.2f)));
                Assert.That(z == TerrainShaper.RockLayer || z == TerrainShaper.DryGrassLayer, Is.True);
                if (z == TerrainShaper.RockLayer) rock++;
            }

            Assert.That(rock / 2000.0, Is.InRange(0.4, 0.6));

            // Whole tiles: the same mesh twice.
            TileCoord c = TileOnRoad(1600, 1);
            FarTileMeshData a = Build(Gen().GenerateTile(c, TileLod.Mid)), b = Build(Gen().GenerateTile(c, TileLod.Mid));
            Assert.That(a.VertexCount, Is.EqualTo(b.VertexCount));
            Assert.That(a.IndexCount, Is.EqualTo(b.IndexCount));
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.That(a.Vertices[i].position, Is.EqualTo(b.Vertices[i].position));
                Assert.That(a.Vertices[i].color, Is.EqualTo(b.Vertices[i].color));
            }

            for (int i = 0; i < a.IndexCount; i++) Assert.That(a.Indices[i], Is.EqualTo(b.Indices[i]));
            Assert.That(a.FaceZones, Is.EqualTo(b.FaceZones));
        }

        [Test]
        public void ZoneColoursComeFromGroundLookWithASmallJitter()
        {
            var s = new FarMeshSettings();
            // A wood's floor is drawn as the leaf-litter mix the near tiles paint under canopy (forest floor pass, 2026-10-06),
            // so a wood reads darker than a field from afar; every other zone is its layer's own colour.
            Vector3 floor = GroundLook.WoodFloorColor();
            for (int c = 0; c < TerrainShaper.LayerCount; c++)
            {
                Vector3 expected = c == TerrainShaper.ForestFloorLayer ? floor : GroundLook.LayerColor(c);
                Assert.That((FarTileMeshData.ZoneColor(c, s) - expected).magnitude, Is.LessThan(1e-6f), TerrainShaper.LayerNames[c]);
            }

            Assert.That(floor.y, Is.LessThan(GroundLook.LayerColor(TerrainShaper.ForestFloorLayer).y), "the wood floor is darker than the plain forest-floor layer");
            Assert.That((FarTileMeshData.ZoneColor(FarTileMeshData.ZoneCanopy, s) - (floor + (s.CanopyColor - floor) * s.CoverTint)).magnitude, Is.LessThan(1e-6f));
            Assert.That(FarTileMeshData.ZoneColor(FarTileMeshData.ZoneWater, s), Is.EqualTo(s.StreamColor));
            float lo = float.MaxValue, hi = float.MinValue, mean = 0f;
            for (uint h = 0; h < 5000; h++)
            {
                float j = FarTileMeshData.JitterOf(h * 7919u, s.Jitter);
                Assert.That(j, Is.EqualTo(FarTileMeshData.JitterOf(h * 7919u, s.Jitter)));
                lo = Math.Min(lo, j);
                hi = Math.Max(hi, j);
                mean += j / 5000f;
            }

            Assert.That(lo, Is.GreaterThanOrEqualTo(1f - s.Jitter));
            Assert.That(hi, Is.LessThanOrEqualTo(1f + s.Jitter));
            Assert.That(hi - lo, Is.GreaterThan(s.Jitter), "the jitter is used");
            Assert.That(mean, Is.EqualTo(1f).Within(0.005f), "no brightness bias");

            // A tile's faces average to its paint: the mean face colour of a far tile is within a few ΔE of its splat mix.
            TileBlueprint bp = Gen().GenerateTile(TileOnRoad(2000), TileLod.Mid);
            bp.Trees.Clear();
            bp.Streams.Clear();
            FarTileMeshData d = Build(bp, s);
            Vector3 faces = Vector3.zero, mix = Vector3.zero;
            int channels = bp.Splat.GetLength(2), sres = bp.Splat.GetLength(0);
            var sample = new float[channels];
            for (int t = 0; t < d.GroundTriangles; t++) faces += FarTileMeshData.DecodeSrgb(d.Vertices[d.Indices[3 * t]].color) / d.GroundTriangles;
            for (int z = 0; z < 64; z++)
            {
                for (int x = 0; x < 64; x++) mix += FarTileMeshData.GroundColor(bp.Splat, channels, sample, (x + 0.5f) / 64f, (z + 0.5f) / 64f, s.ChannelColors) / 4096f;
            }

            Assert.That(GroundLook.DeltaE(faces, mix), Is.LessThan(3f), $"faces {faces} vs paint {mix}");
        }

        [Test]
        public void DiagonalsFollowTheTerrainOrVaryByHash()
        {
            Assert.That(FarTileMeshData.DiagonalAD(10f, 12f, 12f, 10.1f, 0, 0), Is.True, "a valley along a–d keeps its line");
            Assert.That(FarTileMeshData.DiagonalAD(14f, 10f, 10.2f, 11f, 0, 0), Is.False, "a valley along b–c keeps its line");
            int ad = 0;
            for (long q = 0; q < 1000; q++)
            {
                bool v = FarTileMeshData.DiagonalAD(5f, 5f, 5f, 5f, q, q * 3 + 1);
                Assert.That(v, Is.EqualTo(FarTileMeshData.DiagonalAD(5f, 5f, 5f, 5f, q, q * 3 + 1)));
                if (v) ad++;
            }

            Assert.That(ad, Is.InRange(400, 600), "flat ground: both diagonals, no regular grid");
        }

        [Test]
        public void EveryTreeOfTheNearTileIsDrawnAsItsSpeciesAtItsMeasuredSize()
        {
            TileBlueprint far = ForestTile(TileLod.Mid);
            TileBlueprint near = Gen().GenerateTile(far.Coord, TileLod.Near);
            // Near tiles also carry their bushes and ferns as instanced plants; the distant tiles carry the trees only.
            int nearTrees = near.Trees.FindAll(t => PropRole.IsTree(t.role)).Count;
            Assert.That(far.Trees.Count, Is.EqualTo(nearTrees).Within(Math.Max(8, nearTrees / 20)), "the distant tile carries every tree, not only the large ones");
            FarTileMeshData d = Build(far);
            Assert.That(d.TreesDrawn, Is.EqualTo(far.Trees.Count));
            Assert.That(d.TreesThinned, Is.EqualTo(0));

            // Walk the impostors in order: each spans its measured height above its sunk base.
            int v = d.VertexCount;
            for (int t = far.Trees.Count - 1; t >= 0; t--)
            {
                TreePlacement tree = far.Trees[t];
                FarTreeStyle style = FarTreeStyles.Defaults.Resolve(tree.role, tree.variant);
                int n = FarTileMeshData.VerticesOf(style, true);
                v -= n;
                float top = float.MinValue;
                for (int k = v; k < v + n; k++) top = Math.Max(top, d.Vertices[k].position.y);
                float baseY = tree.position.y - FarTileMeshData.DefaultDrop - FarTileMeshData.TreeSink;
                // Lobed crowns (impostor pass, 2026-10-06) reach the top of their fitted lobes, within 20 % of the measured height.
                float height = style.Height;
                if (style.HasLobes)
                {
                    TreeLobes.Extent(TreeLobes.MidOf(style), out _, out height, out _);
                    Assert.That(height, Is.EqualTo(style.Height).Within(0.2f * style.Height));
                }

                Assert.That(top, Is.EqualTo(baseY + height * tree.scale).Within(0.01f), $"tree {t} ({tree.role}) height");
            }

            // Simple impostors on the 8 m tiles: cheaper, still every tree.
            FarTileMeshData coarse = Build(Gen().GenerateTile(far.Coord, TileLod.Far));
            Assert.That(coarse.TreeTriangles, Is.LessThan(d.TreeTriangles / 2));
        }

        [Test]
        public void TriangleCapIsRespectedAndThinningIsDeterministic()
        {
            TileBlueprint bp = ForestTile(TileLod.Far);
            int res = bp.Heights.GetLength(0);
            int cap = FarTileMeshData.GroundAndSkirtTriangles(res) + 600;
            var settings = new FarMeshSettings { MaxTriangles = cap, IncludeRocks = false, IncludeHouses = false };
            FarTileMeshData a = Build(bp, settings);
            FarTileMeshData b = Build(bp, settings);
            Assert.That(a.Triangles, Is.LessThanOrEqualTo(cap));
            Assert.That(a.TreesThinned, Is.GreaterThan(0));
            Assert.That(a.TreesDrawn + a.TreesThinned, Is.EqualTo(bp.Trees.Count));
            Assert.That(a.TreeTriangles, Is.GreaterThan(500), "the budget is used, not thrown away");
            Assert.That(a.CrownGrowth, Is.GreaterThan(1f).And.LessThanOrEqualTo(FarTileMeshData.MaxThinningGrowth), "kept crowns grow to keep the wood closed");
            Assert.That(b.TreesDrawn, Is.EqualTo(a.TreesDrawn));
            for (int i = 0; i < a.IndexCount; i++) Assert.That(b.Indices[i], Is.EqualTo(a.Indices[i]));

            // The default caps hold for the densest forest tiles of both grids, with 16-bit indices.
            foreach (TileLod lod in new[] { TileLod.Far, TileLod.Mid })
            {
                TileBlueprint dense = ForestTile(lod);
                FarTileMeshData full = Build(dense);
                Assert.That(full.Triangles, Is.LessThanOrEqualTo(lod == TileLod.Mid ? FarTileMeshData.MidMaxTriangles : FarTileMeshData.FarMaxTriangles));
                Assert.That(full.VertexCount, Is.LessThanOrEqualTo(ushort.MaxValue));
                Assert.That(full.TreesThinned, Is.EqualTo(0), $"{lod}: dense forest fits without thinning");
            }

            // No trees when they are switched off.
            FarTileMeshData ground = Build(bp, new FarMeshSettings { IncludeTrees = false, IncludeRocks = false, IncludeHouses = false });
            Assert.That(ground.TreeTriangles, Is.EqualTo(0));
            Assert.That(ground.Triangles, Is.EqualTo(FarTileMeshData.GroundAndSkirtTriangles(res)));
        }

        [Test]
        public void InnsAndCragsShowAsSolids()
        {
            // The first inn after the start: its tile carries the inn's house and draws it.
            var places = new List<Waypoint>();
            Gen().Waypoints(200.0, 6000.0, places);
            Waypoint inn = places.Find(w => w.Kind == WaypointKind.Inn && w.S > 200.0);
            Assert.That(inn, Is.Not.Null);
            TileBlueprint bp = Gen().GenerateTile(TileCoord.Containing(inn.X, inn.Z, Gen().Params.tileSize), TileLod.Far);
            int houses = bp.Props.FindAll(p => TileGenerator.FarHouse(p.role)).Count;
            Assert.That(houses, Is.GreaterThan(0), "the inn's house is on its far tile");
            FarTileMeshData d = Build(bp);
            Assert.That(d.HousesDrawn, Is.EqualTo(houses));
            Assert.That(d.HouseTriangles, Is.GreaterThanOrEqualTo(14 * houses));

            // A pass: large rocks on the far tiles, drawn as solids of the rock colour.
            int rocks = 0;
            for (double s = 0; s < 40000 && rocks == 0; s += 300)
            {
                if (Gen().Region(s).Kind != RegionKind.HighlandPass) continue;
                for (int dx = -2; dx <= 2 && rocks == 0; dx++)
                {
                    TileBlueprint t = Gen().GenerateTile(TileOnRoad(s, dx), TileLod.Mid);
                    FarTileMeshData m = Build(t);
                    rocks = m.RocksDrawn;
                    if (rocks == 0) continue;
                    foreach (PropPlacement p in t.Props)
                    {
                        if (!PropRole.IsRock(p.role)) continue;
                        Assert.That(Gen().Keys.TryGetMetrics(p.role, p.variant, out PropMetrics pm), Is.True);
                        Assert.That(pm.Height * p.scale, Is.GreaterThanOrEqualTo(TileGenerator.FarRockMinHeight - 1e-3f), "only the large rocks travel to the far tiles");
                    }
                }
            }

            Assert.That(rocks, Is.GreaterThan(0), "crags show on distant pass tiles");
        }

        [Test]
        public void PrefabNamesGiveTheMeasuredShapesAndColours()
        {
            Assert.That(FarTreeStyles.ForPrefab("SM_Gen_Env_Tree_Pine_02", 15f, 4f).Shape, Is.EqualTo(FarTreeShape.Conifer));
            Assert.That(FarTreeStyles.ForPrefab("SM_Gen_Env_Tree_Dead_01", 8f, 3f).Shape, Is.EqualTo(FarTreeShape.Dead));
            FarTreeStyle birch = FarTreeStyles.ForPrefab("SM_Env_Tree_Birch_01", 12f, 4f);
            Assert.That(birch.Shape, Is.EqualTo(FarTreeShape.Broadleaf));
            Assert.That(birch.Height, Is.EqualTo(9.24f), "measured, not the passed bounds");
            Assert.That(birch.Radius, Is.EqualTo(2.08f));
            Assert.That(birch.Color.x, Is.GreaterThan(birch.Color.y * 2f), "birches are autumn orange");
            Assert.That(birch.Color2.y, Is.GreaterThan(birch.Color.y), "…to yellow");
            FarTreeStyle fruit = FarTreeStyles.ForPrefab("SM_Env_Tree_Fruit_02(Clone)", 1f, 1f);
            Assert.That(fruit.Height, Is.EqualTo(4.70f));
            Assert.That(fruit.Color.y, Is.GreaterThan(fruit.Color.x), "fruit trees are yellow-green");
            Assert.That(FarTreeShapes.Count, Is.EqualTo(17));
            Assert.That(birch.Triangles, Is.EqualTo(43), "two four-sided domed lobes and a trunk (rounded lobes, 2026-10-06)");
            Assert.That(birch.SimpleTriangles, Is.EqualTo(12), "the low dome on the 8 m tiles");
            Assert.That(FarTileMeshData.LinearToSrgb(FarTileMeshData.SrgbToLinear(77)), Is.EqualTo(77));

            // Without a palette, a family resolves through the catalogue to the measured prefab.
            Assert.That(FarTreeStyles.Defaults.Resolve(PropRole.ForestTree, 0).Height, Is.EqualTo(9.24f), "ForestTree 0 = Birch_01");
            Assert.That(FarTreeStyles.Defaults.Resolve(PropRole.PineTree, 1).Shape, Is.EqualTo(FarTreeShape.Conifer));

            var styles = new FarTreeStyles();
            styles.Set(PropRole.ForestTree, new[] { birch, FarTreeStyles.ForPrefab("SM_Gen_Env_Tree_Pine_01", 14f, 3f) });
            Assert.That(styles.Resolve(PropRole.ForestTree, 3).Shape, Is.EqualTo(FarTreeShape.Conifer), "variants wrap like the palette");
            Assert.That(styles.Resolve(PropRole.ForestTree, -2).Shape, Is.EqualTo(FarTreeShape.Broadleaf));
            Assert.That(styles.Resolve(PropRole.PineTree, 0).Shape, Is.EqualTo(FarTreeShape.Conifer), "unknown family: the catalogue's");

            // Per-tree foliage varies between the two measured colours, the same for the same tree.
            var a = new TreePlacement { role = PropRole.ForestTree, position = new Vector3(10f, 0f, 20f), scale = 1f };
            var b = new TreePlacement { role = PropRole.ForestTree, position = new Vector3(40f, 0f, 70f), scale = 1f };
            Assert.That(FarTileMeshData.TreeLeafColor(birch, a), Is.EqualTo(FarTileMeshData.TreeLeafColor(birch, a)));
            Assert.That(FarTileMeshData.TreeLeafColor(birch, a), Is.Not.EqualTo(FarTileMeshData.TreeLeafColor(birch, b)));
        }

        private static Vector3 Normal(FarTileMeshData d, int t)
        {
            Vector3 a = d.Vertices[d.Indices[3 * t]].position, b = d.Vertices[d.Indices[3 * t + 1]].position, c = d.Vertices[d.Indices[3 * t + 2]].position;
            return Vector3.Cross(b - a, c - a);
        }

        private static Vector3 Centroid(FarTileMeshData d, int t) =>
            (d.Vertices[d.Indices[3 * t]].position + d.Vertices[d.Indices[3 * t + 1]].position + d.Vertices[d.Indices[3 * t + 2]].position) / 3f;
    }

    /// <summary>Near-tile trees bucketed for the instanced renderer (<see cref="TreeCellSet"/>) and its draw rules. Pure.</summary>
    public sealed class EndlessTreeCellTests
    {
        private static TreePrototypeIndex Index()
        {
            var index = new TreePrototypeIndex();
            int a = index.AddPrototype(12f, 4f), b = index.AddPrototype(15f, 3f), c = index.AddPrototype(6f, 3f);
            index.SetFamily(PropRole.ForestTree, new[] { a, b });
            index.SetFamily(PropRole.MeadowTree, new[] { a });
            index.SetFamily(PropRole.PineTree, new[] { b });
            index.SetFamily(PropRole.FruitTree, new[] { c });
            index.SetFamily(PropRole.DeadTree, new[] { c });
            // The endless road's bushes, shrubs and ferns are drawn by the same renderer (2026-10-02).
            int d = index.AddPrototype(1.6f, 1.2f);
            index.SetFamily(PropRole.Bush, new[] { d });
            index.SetFamily(PropRole.Shrub, new[] { d });
            // And the woods' floor (forest pass, 2026-10-05).
            index.SetFamily(PropRole.Fern, new[] { d });
            index.SetFamily(PropRole.Undergrowth, new[] { d });
            return index;
        }

        private static List<TreePlacement> RealNearTrees(out float tileSize)
        {
            var g = new EndlessGenerator(new EndlessParams { seed = 42 }, PaletteKeys.Default);
            tileSize = g.Params.tileSize;
            for (double s = 0; s < 40000; s += 300)
            {
                if (g.Region(s).Kind != RegionKind.Forest) continue;
                RoadPoint r = g.Road(s + 500);
                TileBlueprint bp = g.GenerateTile(TileCoord.Containing(r.X + 300, r.Z, tileSize), TileLod.Near);
                if (bp.Trees.Count > 200) return bp.Trees;
            }

            Assert.Fail("no near forest tile found");
            return null;
        }

        [Test]
        public void EveryTreeLandsInExactlyOneCellWithItsPose()
        {
            List<TreePlacement> trees = RealNearTrees(out float ts);
            TreePrototypeIndex index = Index();
            TreeCellSet set = TreeCellSet.Build(trees, ts, index);
            Assert.That(set.CellsPerSide, Is.EqualTo(8));
            Assert.That(set.CellSize, Is.EqualTo(32f));
            Assert.That(set.Total + set.Skipped, Is.EqualTo(trees.Count));
            Assert.That(set.Skipped, Is.EqualTo(0), "every tree family has a prototype");

            // Match every placement to exactly one matrix (by position), with the right prototype, yaw and scale.
            var seen = new HashSet<(TreeCell, int, int)>();
            int sum = 0;
            foreach (TreeCell cell in set.Cells)
            {
                sum += cell.Count;
                int inCell = 0;
                foreach (Matrix4x4[] list in cell.ByPrototype) inCell += list?.Length ?? 0;
                Assert.That(inCell, Is.EqualTo(cell.Count));
            }

            Assert.That(sum, Is.EqualTo(trees.Count));
            foreach (TreePlacement tree in trees)
            {
                int proto = index.PrototypeOf(tree.role, tree.variant);
                int found = 0;
                foreach (TreeCell cell in set.Cells)
                {
                    Matrix4x4[] list = cell.ByPrototype[proto];
                    if (list == null) continue;
                    for (int i = 0; i < list.Length; i++)
                    {
                        Vector3 p = list[i].MultiplyPoint3x4(Vector3.zero);
                        if ((p - tree.position).sqrMagnitude > 1e-8f) continue;
                        Vector3 forward = list[i].MultiplyVector(Vector3.forward);
                        float yaw = tree.yaw * Mathf.Deg2Rad;
                        Assert.That(forward.x, Is.EqualTo(Mathf.Sin(yaw) * tree.scale).Within(1e-4f));
                        Assert.That(forward.z, Is.EqualTo(Mathf.Cos(yaw) * tree.scale).Within(1e-4f));
                        Assert.That(list[i].MultiplyVector(Vector3.up).y, Is.EqualTo(tree.scale).Within(1e-5f));
                        Assert.That(p.x, Is.InRange(cell.Min.x, cell.Max.x));
                        Assert.That(p.z, Is.InRange(cell.Min.z, cell.Max.z));
                        Assert.That(seen.Add((cell, proto, i)), Is.True, "one matrix per tree");
                        found++;
                    }
                }

                Assert.That(found, Is.GreaterThanOrEqualTo(1), $"tree at {tree.position}");
            }

            Assert.That(seen.Count, Is.EqualTo(trees.Count));
        }

        [Test]
        public void PoseMatchesUnitysYawConvention()
        {
            Matrix4x4 m = TreeCellSet.Pose(new Vector3(10f, 20f, 30f), 90f, 2f);
            Vector3 f = m.MultiplyVector(Vector3.forward);
            Assert.That(f.x, Is.EqualTo(2f).Within(1e-5f), "positive yaw turns +Z toward +X (Quaternion.Euler(0, yaw, 0))");
            Assert.That(f.z, Is.EqualTo(0f).Within(1e-5f));
            Vector3 r = m.MultiplyVector(Vector3.right);
            Assert.That(r.z, Is.EqualTo(-2f).Within(1e-5f));
            Assert.That(m.MultiplyPoint3x4(new Vector3(0f, 1f, 0f)), Is.EqualTo(new Vector3(10f, 22f, 30f)));
        }

        [Test]
        public void OffsetMovesMatricesAndBoundsAndAccumulates()
        {
            var trees = new List<TreePlacement>
            {
                new TreePlacement { role = PropRole.ForestTree, variant = 0, position = new Vector3(10f, 50f, 10f), yaw = 30f, scale = 1.2f },
                new TreePlacement { role = PropRole.PineTree, variant = 0, position = new Vector3(200f, 60f, 130f), yaw = 0f, scale = 1f },
                new TreePlacement { role = "NoSuchTree", variant = 0, position = new Vector3(5f, 50f, 5f), yaw = 0f, scale = 1f },
            };
            TreeCellSet set = TreeCellSet.Build(trees, 256f, Index());
            Assert.That(set.Total, Is.EqualTo(2));
            Assert.That(set.Skipped, Is.EqualTo(1));
            Assert.That(set.Cells.Length, Is.EqualTo(2));
            Vector3 before = set.Cells[0].ByPrototype[0][0].MultiplyPoint3x4(Vector3.zero);
            Vector3 minBefore = set.Cells[0].Min;

            var tile = new Vector3(-512f, 0f, 768f);
            set.Offset(tile);
            var shift = new Vector3(4096f, 0f, -4096f);
            set.Offset(shift);
            Assert.That(set.Applied, Is.EqualTo(tile + shift));
            Assert.That(set.Cells[0].ByPrototype[0][0].MultiplyPoint3x4(Vector3.zero), Is.EqualTo(before + tile + shift));
            Assert.That(set.Cells[0].Min, Is.EqualTo(minBefore + tile + shift));
            Vector3 f = set.Cells[0].ByPrototype[0][0].MultiplyVector(Vector3.forward);
            Assert.That(f.magnitude, Is.EqualTo(1.2f).Within(1e-5f), "rotation and scale untouched");
        }

        [Test]
        public void VariantsWrapLikeThePalette()
        {
            TreePrototypeIndex index = Index();
            Assert.That(index.PrototypeOf(PropRole.ForestTree, 0), Is.EqualTo(0));
            Assert.That(index.PrototypeOf(PropRole.ForestTree, 3), Is.EqualTo(1));
            Assert.That(index.PrototypeOf(PropRole.ForestTree, -1), Is.EqualTo(1));
            Assert.That(index.PrototypeOf("Nope", 0), Is.EqualTo(-1));
            Assert.That(index.HeightOf(1), Is.EqualTo(15f));
        }

        [Test]
        public void BandsSwitchLodByDistanceAndScale()
        {
            var d = new TreeLodDistances(110f, 300f);
            Assert.That(TreeDrawRules.BandFor(0f, d), Is.EqualTo(TreeDrawBand.Detail));
            Assert.That(TreeDrawRules.BandFor(110f, d), Is.EqualTo(TreeDrawBand.Detail));
            Assert.That(TreeDrawRules.BandFor(111f, d), Is.EqualTo(TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.BandFor(300f, d), Is.EqualTo(TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.BandFor(301f, d), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.BandFor(5000f, d), Is.EqualTo(TreeDrawBand.Low), "the lowest band is a mesh LOD, whatever the distance");

            // The scale multiplies both distances and is clamped to 0.5..2.
            TreeLodDistances half = TreeLodDistances.From(110f, 300f, 0.5f);
            Assert.That(half.Full, Is.EqualTo(55f).Within(1e-4f));
            Assert.That(half.Mid, Is.EqualTo(150f).Within(1e-4f));
            Assert.That(TreeLodDistances.From(110f, 300f, 9f).Mid, Is.EqualTo(600f).Within(1e-4f), "clamped to 2");
            Assert.That(TreeLodDistances.From(110f, 300f, 0.1f).Full, Is.EqualTo(55f).Within(1e-4f), "clamped to 0.5");
            Assert.That(TreeLodDistances.From(110f, 300f, float.NaN).Full, Is.EqualTo(110f));
            Assert.That(new TreeLodDistances(200f, 100f).Mid, Is.EqualTo(200f), "mid never below full");
            Assert.That(TreeDrawRules.WholeCellLow(320f, d), Is.True);
            Assert.That(TreeDrawRules.WholeCellLow(310f, d), Is.False, "inside the hysteresis margin a cell is decided per tree");
        }

        [Test]
        public void HysteresisKeepsATreeInItsBandNearAThreshold()
        {
            var d = new TreeLodDistances(110f, 300f);
            Assert.That(d.FullMargin, Is.EqualTo(5.5f).Within(1e-4f));
            Assert.That(d.MidMargin, Is.EqualTo(15f).Within(1e-4f));
            Assert.That(TreeDrawRules.Hysteresis(20f), Is.EqualTo(4f), "at least 4 m");

            // Walking away from a LOD0 tree: it stays LOD0 until 5.5 m past the threshold.
            Assert.That(TreeDrawRules.BandFor(114f, (byte)TreeDrawBand.Detail, d), Is.EqualTo(TreeDrawBand.Detail));
            Assert.That(TreeDrawRules.BandFor(116f, (byte)TreeDrawBand.Detail, d), Is.EqualTo(TreeDrawBand.Mid));
            // Walking back: it stays Mid until 5.5 m inside.
            Assert.That(TreeDrawRules.BandFor(106f, (byte)TreeDrawBand.Mid, d), Is.EqualTo(TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.BandFor(104f, (byte)TreeDrawBand.Mid, d), Is.EqualTo(TreeDrawBand.Detail));
            Assert.That(TreeDrawRules.BandFor(310f, (byte)TreeDrawBand.Mid, d), Is.EqualTo(TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.BandFor(316f, (byte)TreeDrawBand.Mid, d), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.BandFor(290f, (byte)TreeDrawBand.Low, d), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.BandFor(284f, (byte)TreeDrawBand.Low, d), Is.EqualTo(TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.BandFor(50f, (byte)TreeDrawBand.Low, d), Is.EqualTo(TreeDrawBand.Detail), "a jump (teleport) resets the band");
            Assert.That(TreeDrawRules.BandFor(112f, TreeDrawRules.Unknown, d), Is.EqualTo(TreeDrawBand.Mid), "no memory: the plain band");

            // Pacing to and fro across the threshold by less than the margin never switches.
            TreeDrawBand band = TreeDrawRules.BandFor(108f, TreeDrawRules.Unknown, d);
            int switches = 0;
            for (int step = 0; step < 200; step++)
            {
                float distance = 110f + 4f * (float)Math.Sin(step * 0.3);
                TreeDrawBand next = TreeDrawRules.BandFor(distance, (byte)band, d);
                if (next != band) switches++;
                band = next;
            }

            Assert.That(switches, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void LodPlanNeverPicksACard()
        {
            // Meadow tree: LOD0 25.3k, LOD1 12.7k, LOD2 6.6k, LOD3 a 12-triangle card of crossed quads.
            var meadow = TreeLodPlan.Choose(new[] { 25278, 12700, 6562, 12 }, new[] { 0.6f, 0.6f, 0.6f, 0.85f });
            Assert.That(meadow, Is.EqualTo((0, 1, 2)));
            // Fruit tree: LOD0 7.5k, LOD1 4.1k, LOD2 card.
            Assert.That(TreeLodPlan.Choose(new[] { 7500, 4064, 12 }, null), Is.EqualTo((0, 1, 1)));
            // A flat card with more triangles is still a card (thinness).
            Assert.That(TreeLodPlan.Choose(new[] { 9000, 3000, 200 }, new[] { 0.5f, 0.5f, 0.001f }), Is.EqualTo((0, 1, 1)));
            // No LODGroup / a single LOD: LOD0 for everything.
            Assert.That(TreeLodPlan.Choose(new[] { 688 }, null), Is.EqualTo((0, 0, 0)));
            Assert.That(TreeLodPlan.Choose(new int[0], null), Is.EqualTo((0, 0, 0)));
            // A small but genuine mesh LOD of a small tree is not a card (a 40-triangle LOD of a 120-triangle tree).
            Assert.That(TreeLodPlan.IsCard(40, 120, 0.5f), Is.False);
            Assert.That(TreeLodPlan.IsCard(12, 7500, 0.9f), Is.True);
            Assert.That(TreeLodPlan.IsCard(0, 7500, 0.9f), Is.True);
        }

        [Test]
        public void ShadowsComeFromNearTreesInTwoRings()
        {
            Assert.That(TreeDrawRules.ShadowDistance(90f, 180f), Is.EqualTo(90f));
            Assert.That(TreeDrawRules.ShadowDistance(90f, 50f), Is.EqualTo(50f), "never beyond the pipeline's shadow distance");
            Assert.That(TreeDrawRules.ShadowDistance(90f, 0f), Is.EqualTo(0f));
            Assert.That(TreeDrawRules.ShadowRing(10f, 90f, true), Is.EqualTo(0));
            Assert.That(TreeDrawRules.ShadowRing(36f, 90f, true), Is.EqualTo(0));
            Assert.That(TreeDrawRules.ShadowRing(37f, 90f, true), Is.EqualTo(1));
            Assert.That(TreeDrawRules.ShadowRing(90f, 90f, true), Is.EqualTo(1));
            Assert.That(TreeDrawRules.ShadowRing(91f, 90f, true), Is.EqualTo(-1));
            Assert.That(TreeDrawRules.ShadowRing(10f, 90f, false), Is.EqualTo(-1), "tree shadows off");
            Assert.That(TreeDrawRules.ShadowRing(10f, 0f, true), Is.EqualTo(-1), "pipeline shadows off");
        }

        [Test]
        public void CellsStartWithNoRememberedBands()
        {
            List<TreePlacement> trees = RealNearTrees(out float ts);
            TreeCellSet set = TreeCellSet.Build(trees, ts, Index());
            foreach (TreeCell cell in set.Cells)
            {
                Assert.That(cell.Stale, Is.True);
                for (int p = 0; p < cell.ByPrototype.Length; p++)
                {
                    if (cell.ByPrototype[p] == null)
                    {
                        Assert.That(cell.LastBand[p], Is.Null);
                        continue;
                    }

                    Assert.That(cell.LastBand[p].Length, Is.EqualTo(cell.ByPrototype[p].Length));
                    foreach (byte b in cell.LastBand[p]) Assert.That(b, Is.EqualTo(TreeDrawRules.Unknown));
                }
            }
        }

        [Test]
        public void CapsLimitInstancesAndCalls()
        {
            Assert.That(TreeDrawRules.CallsFor(0, 2), Is.EqualTo(0));
            Assert.That(TreeDrawRules.CallsFor(1023, 2), Is.EqualTo(2));
            Assert.That(TreeDrawRules.CallsFor(1024, 2), Is.EqualTo(4));
            Assert.That(TreeDrawRules.Allowed(500, 2, 0, 0, 40000, 600), Is.EqualTo(500));
            Assert.That(TreeDrawRules.Allowed(500, 2, 39800, 0, 40000, 600), Is.EqualTo(200), "instance cap");
            Assert.That(TreeDrawRules.Allowed(500, 2, 0, 599, 40000, 600), Is.EqualTo(0), "one call left, a chunk needs two");
            Assert.That(TreeDrawRules.Allowed(500, 2, 0, 598, 40000, 600), Is.EqualTo(500));
            Assert.That(TreeDrawRules.Allowed(5000, 3, 0, 594, 40000, 600), Is.EqualTo(2 * 1023), "two chunks of calls left");
        }
    }

    /// <summary>The self-diagnosing safety ladder and the load milestones (pure state machines).</summary>
    public sealed class EndlessSafetyTests
    {
        [Test]
        public void FirstRunStartsWithEverything()
        {
            SafetyDecision d = SafetyLadder.Decide(false, default);
            Assert.That(d.Level, Is.EqualTo(0));
            Assert.That(d.SteppedDown, Is.False);
            Assert.That(d.Message, Is.Null);
        }

        [Test]
        public void ARunThatDiedEarlyStepsDownOneLevel()
        {
            for (int level = 0; level < SafetyLadder.MaxLevel; level++)
            {
                SafetyDecision d = SafetyLadder.Decide(true, new SafetyRecord { Level = level, Phase = RunPhase.Started });
                Assert.That(d.Level, Is.EqualTo(level + 1));
                Assert.That(d.SteppedDown, Is.True);
                StringAssert.StartsWith("The last run did not end cleanly; starting " + SafetyLadder.Describe(level + 1), d.Message);
            }

            SafetyDecision bottom = SafetyLadder.Decide(true, new SafetyRecord { Level = SafetyLadder.MaxLevel, Phase = RunPhase.Started });
            Assert.That(bottom.Level, Is.EqualTo(SafetyLadder.MaxLevel));
            Assert.That(bottom.SteppedDown, Is.True);
            Assert.That(bottom.Message, Does.Contain("lowest"));
            Assert.That(SafetyLadder.Decide(true, new SafetyRecord { Level = 99, Phase = RunPhase.Started }).Level, Is.EqualTo(SafetyLadder.MaxLevel));
            Assert.That(SafetyLadder.Decide(true, new SafetyRecord { Level = -3, Phase = RunPhase.Started }).Level, Is.EqualTo(1));
        }

        [Test]
        public void ARunThatRanOkOrEndedCleanlyKeepsItsLevel()
        {
            for (int level = 0; level <= SafetyLadder.MaxLevel; level++)
            {
                foreach (RunPhase phase in new[] { RunPhase.RunningOk, RunPhase.CleanExit })
                {
                    SafetyDecision d = SafetyLadder.Decide(true, new SafetyRecord { Level = level, Phase = phase });
                    Assert.That(d.Level, Is.EqualTo(level));
                    Assert.That(d.SteppedDown, Is.False);
                    Assert.That(d.Message, Is.Null);
                }
            }
        }

        [Test]
        public void RecordTransitions()
        {
            SafetyRecord start = SafetyLadder.OnStart(2);
            Assert.That(start.Level, Is.EqualTo(2));
            Assert.That(start.Phase, Is.EqualTo(RunPhase.Started));
            SafetyRecord ok = SafetyLadder.OnRunningOk(start);
            Assert.That(ok.Phase, Is.EqualTo(RunPhase.RunningOk));
            Assert.That(ok.Level, Is.EqualTo(2));
            SafetyRecord clean = SafetyLadder.OnCleanExit(ok);
            Assert.That(clean.Phase, Is.EqualTo(RunPhase.CleanExit));
            Assert.That(SafetyLadder.OnRunningOk(clean).Phase, Is.EqualTo(RunPhase.CleanExit), "a clean exit stays clean");
            Assert.That(SafetyLadder.OnCleanExit(start).Phase, Is.EqualTo(RunPhase.CleanExit), "a short run that ends normally is clean");
            SafetyRecord reset = SafetyLadder.Reset();
            Assert.That(reset.Level, Is.EqualTo(0));
            Assert.That(SafetyLadder.Decide(true, reset).Level, Is.EqualTo(0));
            Assert.That(SafetyLadder.OnStart(17).Level, Is.EqualTo(SafetyLadder.MaxLevel));

            foreach (RunPhase phase in new[] { RunPhase.Started, RunPhase.RunningOk, RunPhase.CleanExit })
            {
                Assert.That(SafetyLadder.ParsePhase(SafetyLadder.PhaseName(phase)), Is.EqualTo(phase));
            }

            Assert.That(SafetyLadder.ParsePhase("garbage"), Is.EqualTo(RunPhase.Started), "unknown reads as the cautious answer");
            Assert.That(SafetyLadder.ParsePhase(null), Is.EqualTo(RunPhase.Started));
        }

        [Test]
        public void EachLevelSwitchesOffItsFeatures()
        {
            EndlessFeatures l0 = SafetyLadder.FeaturesFor(0), l1 = SafetyLadder.FeaturesFor(1), l2 = SafetyLadder.FeaturesFor(2), l3 = SafetyLadder.FeaturesFor(3);
            EndlessFeatures l4 = SafetyLadder.FeaturesFor(4), l5 = SafetyLadder.FeaturesFor(5), l6 = SafetyLadder.FeaturesFor(6);
            Assert.That(l0, Is.EqualTo(EndlessFeatures.All));
            // The forest pass's extras go first, before any tree, near or far.
            Assert.That(l1.forestExtras, Is.False);
            Assert.That(l1.farTrees && l1.farTiles && l1.nearTrees && l1.treeShadows && l1.grassDetails && l1.props && l1.water && l1.animals, Is.True);
            Assert.That(l2.farTrees || l2.forestExtras, Is.False);
            Assert.That(l2.farTiles && l2.nearTrees && l2.grassDetails && l2.props && l2.water, Is.True);
            Assert.That(l3.farTiles || l3.farTrees, Is.False);
            Assert.That(l3.nearTrees && l3.grassDetails && l3.props && l3.water, Is.True);
            Assert.That(l4.nearTrees || l4.treeShadows || l4.farTiles, Is.False);
            Assert.That(l4.grassDetails && l4.props && l4.water, Is.True);
            Assert.That(l5.grassDetails || l5.nearTrees || l5.farTiles, Is.False);
            Assert.That(l5.props && l5.water, Is.True);
            Assert.That(l6.props || l6.water || l6.grassDetails || l6.nearTrees || l6.farTiles || l6.farTrees || l6.forestExtras, Is.False);
            Assert.That(SafetyLadder.FeaturesFor(42), Is.EqualTo(l6));
            for (int level = 1; level <= SafetyLadder.MaxLevel; level++) Assert.That(SafetyLadder.FeaturesFor(level).forestExtras, Is.False, $"level {level}");
        }

        [Test]
        public void TogglesCombineWithTheLevel()
        {
            EndlessFeatures inspector = EndlessFeatures.All;
            inspector.grassDetails = false;
            inspector.farTiles = false;
            EndlessFeatures f = EndlessFeatures.And(inspector, SafetyLadder.FeaturesFor(0));
            Assert.That(f.grassDetails, Is.False);
            Assert.That(f.farTiles, Is.False);
            Assert.That(f.farTrees, Is.False, "no far trees without far tiles");
            Assert.That(f.nearTrees, Is.True);
            EndlessFeatures noNear = EndlessFeatures.All;
            noNear.nearTrees = false;
            Assert.That(EndlessFeatures.And(noNear, EndlessFeatures.All).treeShadows, Is.False, "no tree shadows without near trees");
            Assert.That(EndlessFeatures.All.ToString(), Is.EqualTo("farTiles+ farTrees+ nearTrees+ grass+ props+ water+ treeShadows+ animals+ forestExtras+"));
        }

        [Test]
        public void MilestonesLogOncePerThreshold()
        {
            var m = new LoadMilestones();
            Assert.That(m.Update(new EndlessLoad(), out string none), Is.False);
            Assert.That(none, Is.Null);
            Assert.That(m.Update(new EndlessLoad { NearTiles = 16, FarTiles = 40 }, out string first), Is.True);
            Assert.That(first, Does.Contain("near tiles 16").And.Contain("far tiles 32"));
            Assert.That(m.Update(new EndlessLoad { NearTiles = 16, FarTiles = 40 }, out _), Is.False, "same load: nothing new");
            Assert.That(m.Update(new EndlessLoad { NearTiles = 3, FarTiles = 10 }, out _), Is.False, "falling back logs nothing");
            Assert.That(m.Update(new EndlessLoad { NearTiles = 16, FarTiles = 40 }, out _), Is.False, "rising again to a passed milestone logs nothing");
            Assert.That(m.Update(new EndlessLoad { NearTiles = 16, FarTiles = 130, FarTriangles = 600000, TreeInstances = 5200, TreeDrawCalls = 90 }, out string second), Is.True);
            Assert.That(second, Does.Contain("far tiles 128").And.Contain("far triangles 500000").And.Contain("tree instances 5000").And.Contain("tree draws 50"));
            Assert.That(second, Does.Contain("near 16, far 130 (600k tris), trees 5200 in 90 draws"));
            Assert.That(m.Lines, Is.EqualTo(2));
            Assert.That(m.Update(new EndlessLoad { NearTiles = int.MaxValue, FarTiles = int.MaxValue, FarTriangles = int.MaxValue, TreeInstances = int.MaxValue, TreeDrawCalls = int.MaxValue }, out _), Is.True);
            Assert.That(m.Update(new EndlessLoad { NearTiles = int.MaxValue, FarTiles = int.MaxValue, FarTriangles = int.MaxValue, TreeInstances = int.MaxValue, TreeDrawCalls = int.MaxValue }, out _), Is.False, "every milestone passed once");
        }
    }
}
