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
    /// The endless road's far tiles as plain meshes (<see cref="FarTileMeshData"/>), built from real generator blueprints:
    /// counts, heights, skirt, colours, determinism, the triangle cap, seams between neighbours, winding. Pure.
    /// </summary>
    public sealed class FarTileMeshTests
    {
        private static EndlessGenerator generator;

        private static EndlessGenerator Gen() => generator ??= new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);

        /// <summary>The far tile on the road at road distance <paramref name="s"/>, offset sideways by <paramref name="dx"/> tiles.</summary>
        private static TileCoord TileOnRoad(double s, int dx = 0)
        {
            RoadPoint r = Gen().Road(s);
            TileCoord c = TileCoord.Containing(r.X, r.Z, Gen().Params.tileSize);
            return new TileCoord(c.X + dx, c.Z);
        }

        /// <summary>A far tile in the first forest region with plenty of trees.</summary>
        private static TileBlueprint ForestTile()
        {
            for (double s = 0; s < 40000; s += 250)
            {
                if (Gen().Region(s).Kind != RegionKind.Forest) continue;
                for (int dx = -3; dx <= 3; dx++)
                {
                    TileBlueprint bp = Gen().GenerateTile(TileOnRoad(s + 600, dx), TileLod.Far);
                    if (bp.Trees.Count >= 250) return bp;
                }
            }

            Assert.Fail("no forest far tile with 250+ trees found");
            return null;
        }

        private static FarTileMeshData Build(TileBlueprint bp, FarMeshSettings settings = null) =>
            FarTileMeshData.Build(bp, FarTreeStyles.Defaults, settings ?? new FarMeshSettings(), Gen().Height);

        [Test]
        public void CountsMatchTheGridTheSkirtAndTheTrees()
        {
            TileBlueprint bp = Gen().GenerateTile(TileOnRoad(300), TileLod.Far);
            FarTileMeshData d = Build(bp);
            int res = bp.Heights.GetLength(0);
            Assert.That(res, Is.EqualTo(33));
            Assert.That(d.GroundTriangles, Is.EqualTo(2 * (res - 1) * (res - 1)));
            Assert.That(d.SkirtTriangles, Is.EqualTo(8 * (res - 1)));
            int treeTris = 0, treeVerts = 0;
            foreach (TreePlacement t in bp.Trees)
            {
                FarTreeStyle style = FarTreeStyles.Defaults.Resolve(t.role, t.variant);
                treeTris += style.Triangles;
                treeVerts += style.Shape == FarTreeShape.Dead ? 5 : 7;
            }

            Assert.That(d.TreesThinned, Is.EqualTo(0), "a start tile fits the default cap");
            Assert.That(d.TreesDrawn, Is.EqualTo(bp.Trees.Count));
            Assert.That(d.TreeTriangles, Is.EqualTo(treeTris));
            Assert.That(d.VertexCount, Is.EqualTo(res * res + 4 * res + treeVerts));
            Assert.That(d.IndexCount, Is.EqualTo(3 * (d.GroundTriangles + d.SkirtTriangles + d.TreeTriangles)));
            Assert.That(d.Vertices.Length, Is.EqualTo(d.VertexCount));
            Assert.That(d.Indices.Length, Is.EqualTo(d.IndexCount));
            foreach (ushort i in d.Indices) Assert.That(i, Is.LessThan(d.VertexCount));
        }

        [Test]
        public void GroundVerticesSitOnTheBlueprintHeightsMinusTheDrop()
        {
            TileBlueprint bp = Gen().GenerateTile(TileOnRoad(800), TileLod.Far);
            FarTileMeshData d = Build(bp);
            int res = bp.Heights.GetLength(0);
            float spacing = bp.TileSize / (res - 1);
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    Vector3 p = d.Vertices[j * res + i].position;
                    Assert.That(p.x, Is.EqualTo(i * spacing).Within(1e-4f));
                    Assert.That(p.z, Is.EqualTo(j * spacing).Within(1e-4f));
                    Assert.That(p.y, Is.EqualTo(bp.Heights[j, i] * bp.HeightRange - FarTileMeshData.DefaultDrop).Within(1e-3f));
                    Assert.That(d.Vertices[j * res + i].normal.magnitude, Is.EqualTo(1f).Within(1e-4f));
                    Assert.That(d.Vertices[j * res + i].normal.y, Is.GreaterThan(0f));
                }
            }

            // Ground spans the tile; trees near an edge may overhang it by a crown radius.
            Assert.That(d.BoundsMin.x, Is.InRange(-10f, 0f));
            Assert.That(d.BoundsMax.x, Is.InRange(bp.TileSize, bp.TileSize + 10f));
            Assert.That(d.BoundsMin.z, Is.InRange(-10f, 0f));
            Assert.That(d.BoundsMax.z, Is.InRange(bp.TileSize, bp.TileSize + 10f));
        }

        [Test]
        public void SkirtHangsBelowEveryEdgeAndFacesOutward()
        {
            TileBlueprint bp = Gen().GenerateTile(TileOnRoad(1200), TileLod.Far);
            var settings = new FarMeshSettings();
            FarTileMeshData d = Build(bp, settings);
            int res = bp.Heights.GetLength(0);
            int skirt = res * res;
            var outward = new[] { new Vector3(0f, 0f, -1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(-1f, 0f, 0f) };
            for (int side = 0; side < 4; side++)
            {
                for (int k = 0; k < res; k++)
                {
                    int edge = side == 0 ? k : side == 1 ? k * res + res - 1 : side == 2 ? (res - 1) * res + k : k * res;
                    Vector3 top = d.Vertices[edge].position, below = d.Vertices[skirt + side * res + k].position;
                    Assert.That(below.x, Is.EqualTo(top.x));
                    Assert.That(below.z, Is.EqualTo(top.z));
                    Assert.That(below.y, Is.EqualTo(top.y - settings.SkirtDepth).Within(1e-4f));
                }
            }

            // Ground triangles face up; skirt triangles face out of the tile.
            for (int t = 0; t < d.GroundTriangles; t++) Assert.That(Normal(d, t).y, Is.GreaterThan(0f), $"ground triangle {t}");
            for (int t = d.GroundTriangles; t < d.GroundTriangles + d.SkirtTriangles; t++)
            {
                Vector3 n = Normal(d, t);
                Vector3 c = Centroid(d, t);
                Vector3 o = c.z < 1e-3f ? outward[0] : c.x > bp.TileSize - 1e-3f ? outward[1] : c.z > bp.TileSize - 1e-3f ? outward[2] : outward[3];
                Assert.That(Vector3.Dot(n, o), Is.GreaterThan(0f), $"skirt triangle {t}");
            }
        }

        [Test]
        public void TreeImpostorsFaceAwayFromTheirAxisAndStandOnTheLoweredGround()
        {
            TileBlueprint bp = ForestTile();
            FarTileMeshData d = Build(bp);
            int first = d.GroundTriangles + d.SkirtTriangles;
            for (int t = first; t < d.Triangles; t++)
            {
                Vector3 n = Normal(d, t);
                Assert.That(n.sqrMagnitude, Is.GreaterThan(0f), $"degenerate tree triangle {t}");
            }

            // Every kept tree: some vertex at its base (ground − drop − sink) and its top at base + height · scale.
            int v = bp.Heights.GetLength(0) * bp.Heights.GetLength(0) + 4 * bp.Heights.GetLength(0);
            foreach (TreePlacement tree in bp.Trees)
            {
                FarTreeStyle style = FarTreeStyles.Defaults.Resolve(tree.role, tree.variant);
                Vector3 apex = d.Vertices[v].position;
                float baseY = tree.position.y - FarTileMeshData.DefaultDrop - FarTileMeshData.TreeSink;
                float expectedTop = style.Shape == FarTreeShape.Dead ? 0.9f : 1f;
                Assert.That(apex.x, Is.EqualTo(tree.position.x).Within(1e-3f));
                Assert.That(apex.z, Is.EqualTo(tree.position.z).Within(1e-3f));
                Assert.That(apex.y, Is.EqualTo(baseY + expectedTop * style.Height * tree.scale).Within(1e-2f));
                v += style.Shape == FarTreeShape.Dead ? 5 : 7;
            }

            Assert.That(v, Is.EqualTo(d.VertexCount));
        }

        [Test]
        public void ColoursAreTheSplatWeightedChannelColours()
        {
            TileBlueprint bp = Gen().GenerateTile(TileOnRoad(2000), TileLod.Far);
            bp.Trees.Clear();   // no canopy tint: the colour is the splat mix alone
            var settings = new FarMeshSettings();
            FarTileMeshData d = Build(bp, settings);
            int res = bp.Heights.GetLength(0);
            int sres = bp.Splat.GetLength(0), channels = bp.Splat.GetLength(2);
            Assert.That(channels, Is.EqualTo(TerrainShaper.LayerCount));

            // Corners and the centre: vertex (i, j) at u = i / (res − 1) samples texel u · (sres − 1); at the corners exactly a texel.
            foreach ((int i, int j) in new[] { (0, 0), (res - 1, 0), (0, res - 1), (res - 1, res - 1) })
            {
                int sx = i == 0 ? 0 : sres - 1, sz = j == 0 ? 0 : sres - 1;
                Vector3 expected = Vector3.zero;
                float total = 0f;
                for (int c = 0; c < channels; c++) total += bp.Splat[sz, sx, c];
                for (int c = 0; c < channels; c++) expected += FarTileMeshData.MeasuredChannelColors[c] * (bp.Splat[sz, sx, c] / total);
                Color32 got = d.Vertices[j * res + i].color;
                Color32 want = FarTileMeshData.EncodeSrgb(expected);
                Assert.That(got.r, Is.EqualTo(want.r).Within(1), $"({i},{j}) r");
                Assert.That(got.g, Is.EqualTo(want.g).Within(1), $"({i},{j}) g");
                Assert.That(got.b, Is.EqualTo(want.b).Within(1), $"({i},{j}) b");
                Assert.That(got.a, Is.EqualTo(255));
            }

            // A pure channel gives exactly that channel's colour.
            var splat = new float[2, 2, channels];
            for (int z = 0; z < 2; z++)
            {
                for (int x = 0; x < 2; x++) splat[z, x, TerrainShaper.DirtLayer] = 1f;
            }

            Vector3 dirt = FarTileMeshData.GroundColor(splat, channels, new float[channels], 0.37f, 0.81f, FarTileMeshData.MeasuredChannelColors);
            Assert.That((dirt - FarTileMeshData.MeasuredChannelColors[TerrainShaper.DirtLayer]).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void TreeCoverDarkensTheGroundTowardTheCanopy()
        {
            TileBlueprint bp = ForestTile();
            FarTileMeshData with = Build(bp);
            var bare = Gen().GenerateTile(bp.Coord, TileLod.Far);
            bare.Trees.Clear();
            FarTileMeshData without = Build(bare);
            int darker = 0, res = bp.Heights.GetLength(0);
            for (int k = 0; k < res * res; k++)
            {
                Color32 a = with.Vertices[k].color, b = without.Vertices[k].color;
                if (a.r + a.g + a.b < b.r + b.g + b.b) darker++;
            }

            Assert.That(darker, Is.GreaterThan(res * res / 10), "a forest tile's ground is visibly darker under its trees");
        }

        [Test]
        public void IsDeterministic()
        {
            TileCoord c = TileOnRoad(1600, 1);
            FarTileMeshData a = Build(Gen().GenerateTile(c, TileLod.Far));
            FarTileMeshData b = Build(Gen().GenerateTile(c, TileLod.Far));
            Assert.That(a.VertexCount, Is.EqualTo(b.VertexCount));
            Assert.That(a.IndexCount, Is.EqualTo(b.IndexCount));
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.That(a.Vertices[i].position, Is.EqualTo(b.Vertices[i].position));
                Assert.That(a.Vertices[i].normal, Is.EqualTo(b.Vertices[i].normal));
                Assert.That(a.Vertices[i].color, Is.EqualTo(b.Vertices[i].color));
            }

            for (int i = 0; i < a.IndexCount; i++) Assert.That(a.Indices[i], Is.EqualTo(b.Indices[i]));
        }

        [Test]
        public void TriangleCapIsRespectedAndThinningIsDeterministic()
        {
            TileBlueprint bp = ForestTile();
            int res = bp.Heights.GetLength(0);
            int cap = FarTileMeshData.GroundAndSkirtTriangles(res) + 600;
            var settings = new FarMeshSettings { MaxTriangles = cap };
            FarTileMeshData a = Build(bp, settings);
            FarTileMeshData b = Build(bp, settings);
            Assert.That(a.Triangles, Is.LessThanOrEqualTo(cap));
            Assert.That(a.TreesThinned, Is.GreaterThan(0));
            Assert.That(a.TreesDrawn + a.TreesThinned, Is.EqualTo(bp.Trees.Count));
            Assert.That(a.TreeTriangles, Is.GreaterThan(500), "the budget is used, not thrown away");
            Assert.That(b.TreesDrawn, Is.EqualTo(a.TreesDrawn));
            for (int i = 0; i < a.IndexCount; i++) Assert.That(b.Indices[i], Is.EqualTo(a.Indices[i]));

            // The default cap holds for the densest forest tile, with 16-bit indices.
            FarTileMeshData full = Build(bp);
            Assert.That(full.Triangles, Is.LessThanOrEqualTo(FarTileMeshData.DefaultMaxTriangles));
            Assert.That(full.VertexCount, Is.LessThanOrEqualTo(ushort.MaxValue));

            // No trees when they are switched off.
            FarTileMeshData ground = Build(bp, new FarMeshSettings { IncludeTrees = false });
            Assert.That(ground.TreeTriangles, Is.EqualTo(0));
            Assert.That(ground.Triangles, Is.EqualTo(FarTileMeshData.GroundAndSkirtTriangles(res)));
        }

        [Test]
        public void NeighbouringTilesShareTheirEdgeVertices()
        {
            TileCoord a = TileOnRoad(2600), b = new TileCoord(a.X + 1, a.Z), c = new TileCoord(a.X, a.Z + 1);
            TileBlueprint ba = Gen().GenerateTile(a, TileLod.Far), bb = Gen().GenerateTile(b, TileLod.Far), bc = Gen().GenerateTile(c, TileLod.Far);
            FarTileMeshData da = Build(ba), db = Build(bb), dc = Build(bc);
            int res = ba.Heights.GetLength(0);
            float ts = ba.TileSize;
            for (int k = 0; k < res; k++)
            {
                // a's +X edge against b's −X edge.
                Vector3 pa = da.Vertices[k * res + res - 1].position, pb = db.Vertices[k * res].position + new Vector3(ts, 0f, 0f);
                Assert.That(pb.x, Is.EqualTo(pa.x).Within(1e-3f));
                Assert.That(pb.z, Is.EqualTo(pa.z).Within(1e-3f));
                Assert.That(pb.y, Is.EqualTo(pa.y), "heights bit-identical across the seam");
                Assert.That(Vector3.Dot(da.Vertices[k * res + res - 1].normal, db.Vertices[k * res].normal), Is.GreaterThan(0.995f), "edge normals agree (no lighting seam)");

                // a's +Z edge against c's −Z edge.
                Vector3 qa = da.Vertices[(res - 1) * res + k].position, qc = dc.Vertices[k].position + new Vector3(0f, 0f, ts);
                Assert.That(qc.x, Is.EqualTo(qa.x).Within(1e-3f));
                Assert.That(qc.z, Is.EqualTo(qa.z).Within(1e-3f));
                Assert.That(qc.y, Is.EqualTo(qa.y));
            }
        }

        [Test]
        public void PrefabNamesGiveShapesAndColours()
        {
            Assert.That(FarTreeStyles.ForPrefab("SM_Gen_Env_Tree_Pine_02", 15f, 4f).Shape, Is.EqualTo(FarTreeShape.Conifer));
            Assert.That(FarTreeStyles.ForPrefab("SM_Gen_Env_Tree_Dead_01", 8f, 3f).Shape, Is.EqualTo(FarTreeShape.Dead));
            FarTreeStyle birch = FarTreeStyles.ForPrefab("SM_Env_Tree_Birch_01", 12f, 4f);
            Assert.That(birch.Shape, Is.EqualTo(FarTreeShape.Broadleaf));
            Assert.That(birch.Height, Is.EqualTo(12f));
            Assert.That(birch.Radius, Is.EqualTo(4f * 0.85f).Within(1e-5f));
            Assert.That(birch.Triangles, Is.EqualTo(10));
            Assert.That(FarTileMeshData.LinearToSrgb(FarTileMeshData.SrgbToLinear(77)), Is.EqualTo(77));

            var styles = new FarTreeStyles();
            styles.Set(PropRole.ForestTree, new[] { birch, FarTreeStyles.ForPrefab("SM_Gen_Env_Tree_Pine_01", 14f, 3f) });
            Assert.That(styles.Resolve(PropRole.ForestTree, 3).Shape, Is.EqualTo(FarTreeShape.Conifer), "variants wrap like the palette");
            Assert.That(styles.Resolve(PropRole.ForestTree, -2).Shape, Is.EqualTo(FarTreeShape.Broadleaf));
            Assert.That(styles.Resolve(PropRole.PineTree, 0).Shape, Is.EqualTo(FarTreeShape.Conifer), "unknown family: role default");
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
        public void BandsSwitchLodAndShadowsByDistance()
        {
            Assert.That(TreeDrawRules.BandFor(0f, 40f, 140f, 180f), Is.EqualTo(TreeDrawBand.Detail));
            Assert.That(TreeDrawRules.BandFor(40f, 40f, 140f, 180f), Is.EqualTo(TreeDrawBand.Detail));
            Assert.That(TreeDrawRules.BandFor(41f, 40f, 140f, 180f), Is.EqualTo(TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.BandFor(140f, 40f, 140f, 180f), Is.EqualTo(TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.BandFor(150f, 40f, 140f, 180f), Is.EqualTo(TreeDrawBand.LowShadowed));
            Assert.That(TreeDrawRules.BandFor(181f, 40f, 140f, 180f), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.BandFor(30f, 40f, 140f, 0f), Is.EqualTo(TreeDrawBand.Detail), "detail even with shadows off");
            Assert.That(TreeDrawRules.BandFor(100f, 40f, 140f, 0f), Is.EqualTo(TreeDrawBand.Mid));
            Assert.That(TreeDrawRules.BandFor(150f, 40f, 140f, 0f), Is.EqualTo(TreeDrawBand.Low));
            Assert.That(TreeDrawRules.CastsShadows(TreeDrawBand.Detail, true), Is.True);
            Assert.That(TreeDrawRules.CastsShadows(TreeDrawBand.Mid, true), Is.True);
            Assert.That(TreeDrawRules.CastsShadows(TreeDrawBand.LowShadowed, true), Is.True);
            Assert.That(TreeDrawRules.CastsShadows(TreeDrawBand.Low, true), Is.False);
            Assert.That(TreeDrawRules.CastsShadows(TreeDrawBand.Detail, false), Is.False);
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
            EndlessFeatures l0 = SafetyLadder.FeaturesFor(0), l1 = SafetyLadder.FeaturesFor(1), l2 = SafetyLadder.FeaturesFor(2);
            EndlessFeatures l3 = SafetyLadder.FeaturesFor(3), l4 = SafetyLadder.FeaturesFor(4), l5 = SafetyLadder.FeaturesFor(5);
            Assert.That(l0, Is.EqualTo(EndlessFeatures.All));
            Assert.That(l1.farTrees, Is.False);
            Assert.That(l1.farTiles && l1.nearTrees && l1.grassDetails && l1.props && l1.water, Is.True);
            Assert.That(l2.farTiles || l2.farTrees, Is.False);
            Assert.That(l2.nearTrees && l2.grassDetails && l2.props && l2.water, Is.True);
            Assert.That(l3.nearTrees || l3.treeShadows || l3.farTiles, Is.False);
            Assert.That(l3.grassDetails && l3.props && l3.water, Is.True);
            Assert.That(l4.grassDetails || l4.nearTrees || l4.farTiles, Is.False);
            Assert.That(l4.props && l4.water, Is.True);
            Assert.That(l5.props || l5.water || l5.grassDetails || l5.nearTrees || l5.farTiles || l5.farTrees, Is.False);
            Assert.That(SafetyLadder.FeaturesFor(42), Is.EqualTo(l5));
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
            Assert.That(EndlessFeatures.All.ToString(), Is.EqualTo("farTiles+ farTrees+ nearTrees+ grass+ props+ water+ treeShadows+"));
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
