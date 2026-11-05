using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>Impostor pass (2026-10-06): lobed crowns fitted on the meshes, apparent colours, the swap rule and near/far consistency.</summary>
    public class EndlessImpostorTests
    {
        private static readonly string[] Broadleaf =
        {
            "SM_Env_Tree_Birch_01", "SM_Env_Tree_Birch_02", "SM_Env_Tree_Birch_03", "SM_Env_Tree_Meadow_01", "SM_Env_Tree_Meadow_02",
            "SM_Env_Tree_Fruit_01", "SM_Env_Tree_Fruit_02", "SM_Env_Tree_Fruit_03", "SM_Gen_Env_Tree_01", "SM_Gen_Env_Tree_02", "SM_Gen_Env_Tree_03",
        };

        private static List<Vector3> Clusters(int seed)
        {
            var rng = new System.Random(seed);
            var points = new List<Vector3>();
            Vector3[] centres = { new Vector3(-4f, 6f, 0f), new Vector3(4f, 6f, 1f), new Vector3(0f, 10f, -1f) };
            foreach (Vector3 c in centres)
            {
                for (int i = 0; i < 200; i++)
                {
                    points.Add(c + new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)));
                }
            }

            return points;
        }

        [Test]
        public void LobesFromAMeshAreDeterministic()
        {
            TreeLobe[] a = TreeLobes.Fit(Clusters(7), 3, 0.95f), b = TreeLobes.Fit(Clusters(7), 3, 0.95f);
            Assert.That(a.Length, Is.EqualTo(3));
            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(a[i].Centre, Is.EqualTo(b[i].Centre));
                Assert.That(a[i].RadiusX, Is.EqualTo(b[i].RadiusX));
                Assert.That(a[i].Up, Is.EqualTo(b[i].Up));
            }

            Assert.That(a[0].Centre.y, Is.EqualTo(10f).Within(0.3f), "ordered by height: the top cluster first");
            Assert.That(Math.Abs(a[1].Centre.x), Is.EqualTo(4f).Within(0.3f));
            Assert.That(a[1].RadiusX, Is.InRange(0.7f, 1.1f), "radii from the cluster's own spread");
        }

        [Test]
        public void LobedImpostorsStayWithinTheirTriangleCaps()
        {
            foreach (string name in Broadleaf)
            {
                Assert.That(FarTreeShapes.TryGet(name, out FarTreeStyle style), Is.True, name);
                Assert.That(style.HasLobes, Is.True, name);
                var v = new List<Vector3>();
                var t = new List<int>();
                var c = new List<Color32>();
                TreeImpostor.Build(style, v, t, c);
                int tris = t.Count / 3;
                Assert.That(tris, Is.EqualTo(TreeImpostor.TrianglesOf(style)), name);
                Assert.That(c.Count, Is.EqualTo(v.Count));
                // Rounded lobes (2026-10-06 afternoon): 26 triangles a lobe near (at most 8 / 5 / 4 lobes), 20 on Mid tiles (2, Meadow 01 3).
                int cap = name.Contains("Meadow_01") ? 217 : name.Contains("Meadow_02") ? 139 : 113;
                Assert.That(tris, Is.LessThanOrEqualTo(cap), name);
                Assert.That(v.Count, Is.EqualTo(TreeLobes.VerticesOf(style.Lobes.Length, TreeLobes.NearSides, true)), name + ": vertices shared within a lobe");
                Assert.That(TreeLobes.MidTriangles(style), Is.LessThanOrEqualTo(name.Contains("Meadow_01") ? 63 : 43), name + ": Mid tiles (Meadow 01: three lobes)");
                Assert.That(FarTileMeshData.TrianglesOf(style, true), Is.EqualTo(TreeLobes.MidTriangles(style)));
                Assert.That(FarTileMeshData.VerticesOf(style, true), Is.EqualTo(TreeLobes.MidVertices(style)));
                Assert.That(FarTileMeshData.TrianglesOf(style, false), Is.EqualTo(12), "Far tiles: the low dome");

                // Never a card: the solid is wide in both horizontal directions.
                float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
                foreach (Vector3 p in v)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }

                Assert.That(Mathf.Min(maxX - minX, maxZ - minZ), Is.GreaterThan(0.3f * style.Height), name);
            }
        }

        [Test]
        public void TheMeadowOaksSwapWithinThreePixels()
        {
            double threePx = 3.0 * TreeImpostor.PixelMetres(420.0, 60.0, 1080);
            foreach (string name in new[] { "SM_Env_Tree_Meadow_01", "SM_Env_Tree_Meadow_02" })
            {
                float err = TreeImpostor.SilhouetteError(name, 10f);
                Assert.That(err, Is.LessThan(threePx), name + " matches within 3 px at 420 m");
                float own = TreeImpostor.DistanceFor(EndlessSettingsMigration.LobedImpostorDistance, err, 3f);
                Assert.That(own, Is.LessThanOrEqualTo(420f), name);
                Assert.That(own, Is.GreaterThanOrEqualTo(EndlessSettingsMigration.LobedImpostorDistance), "never nearer than the frame's distance");
            }

            foreach (string name in Broadleaf)
            {
                Assert.That(FarTreeShapes.TryGetLobedSilhouette(name, out float near, out float mid), Is.True, name);
                Assert.That(near, Is.LessThan(threePx), name);
                Assert.That(mid, Is.LessThan(3.0 * TreeImpostor.PixelMetres(860.0, 60.0, 1080)), name + ": Mid lobes within 3 px at the near tiles' edge");
            }

            Assert.That(TreeImpostor.DistanceFor(320f, 2.43f, 3f), Is.GreaterThan(700f), "the dead oak still swaps far out");
        }

        [Test]
        public void LivingCrownsAreNeverBrown()
        {
            foreach (string name in Broadleaf)
            {
                FarTreeShapes.TryGet(name, out FarTreeStyle style);
                foreach (Vector3 c in new[] { style.Color, style.Color2 })
                {
                    if (name.Contains("Birch"))
                    {
                        Assert.That(c.x, Is.GreaterThan(1.5f * c.y), name + ": orange, not a dull brown");
                        Assert.That(c.z, Is.LessThan(0.2f * c.x), name + ": saturated");
                    }
                    else
                    {
                        Assert.That(c.y, Is.GreaterThan(c.x), name + ": green over red");
                        Assert.That(c.y, Is.GreaterThan(c.z), name);
                    }
                }
            }

            FarTreeShapes.TryGet("SM_Env_Tree_Meadow_01", out FarTreeStyle oak);
            FarTreeShapes.TryGet("SM_Gen_Env_Tree_01", out FarTreeStyle generic);
            Assert.That(oak.Color.y, Is.LessThan(generic.Color.y), "the oak is the darkest green");
        }

        [Test]
        public void NearAndMidImpostorsAgree()
        {
            foreach (string name in Broadleaf)
            {
                FarTreeShapes.TryGet(name, out FarTreeStyle style);
                TreeLobes.Extent(style.Lobes, out float reach, out float top, out _);
                TreeLobes.Extent(TreeLobes.MidOf(style), out float midReach, out float midTop, out _);
                Assert.That(midReach, Is.EqualTo(reach).Within(0.35f * reach), name + ": the same crown width");
                Assert.That(midTop, Is.EqualTo(top).Within(0.15f * top), name + ": the same height");
                Assert.That(top, Is.EqualTo(style.Height).Within(0.2f * style.Height), name);

                // The same apparent colour: the near impostor's base colour lies between the colours the Mid tiles vary between.
                Vector3 near = TreeImpostor.LeafColor(style);
                Assert.That(near.y, Is.InRange(Math.Min(style.Color.y, style.Color2.y) - 1e-4f, Math.Max(style.Color.y, style.Color2.y) + 1e-4f), name);
            }
        }

        // ------------------------------------------------------------------ rounded lobes (2026-10-06, afternoon)

        private static void Lobe(int sides, out List<Vector3> v, out List<int> t)
        {
            v = new List<Vector3>();
            t = new List<int>();
            var c = new List<Color32>();
            TreeLobes.Build(new[] { new TreeLobe(0f, 6f, 0f, 2f, 1.6f, 2.5f, 1.5f) }, 3f, 0.3f, new Vector3(0.08f, 0.09f, 0.03f), new Vector3(0.1f, 0.07f, 0.05f), sides, false, v, c, t);
        }

        [Test]
        public void NoLobeHasAPointedApex([Values(TreeLobes.NearSides, TreeLobes.MidSides)] int sides)
        {
            Lobe(sides, out List<Vector3> v, out List<int> t);
            Assert.That(t.Count / 3, Is.EqualTo(TreeLobes.TrianglesOf(1, sides, false)));
            Assert.That(v.Count, Is.EqualTo(TreeLobes.VerticesOf(1, sides, false)));
            // The lobe's own vertices follow the trunk's four: its top and its underside are flat faces with area, not single points.
            float top = float.MinValue, bottom = float.MaxValue;
            for (int i = 4; i < v.Count; i++) { top = Math.Max(top, v[i].y); bottom = Math.Min(bottom, v[i].y); }
            float topArea = 0f, bottomArea = 0f;
            int topVerts = 0, bottomVerts = 0;
            for (int i = 4; i < v.Count; i++)
            {
                if (Math.Abs(v[i].y - top) < 1e-4f) topVerts++;
                if (Math.Abs(v[i].y - bottom) < 1e-4f) bottomVerts++;
            }

            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                float area = 0.5f * Vector3.Cross(b - a, c - a).magnitude;
                if (Math.Abs(a.y - top) < 1e-4f && Math.Abs(b.y - top) < 1e-4f && Math.Abs(c.y - top) < 1e-4f) topArea += area;
                if (Math.Abs(a.y - bottom) < 1e-4f && Math.Abs(b.y - bottom) < 1e-4f && Math.Abs(c.y - bottom) < 1e-4f) bottomArea += area;
            }

            Assert.That(topVerts, Is.EqualTo(sides), "the top is a ring, not a point");
            Assert.That(bottomVerts, Is.EqualTo(sides), "the underside is a ring, not a point");
            Assert.That(topArea, Is.GreaterThan(1f), "a flat cap");
            Assert.That(bottomArea, Is.GreaterThan(1f), "a flat underside");
            Assert.That(bottomArea, Is.LessThan(0.6f * Mathf.PI * 2f * 1.6f), "tucked in under the widest ring");
        }

        [Test]
        public void LobesAreWoundOutward([Values(TreeLobes.NearSides, TreeLobes.MidSides)] int sides)
        {
            // Unity's front faces are clockwise seen from the camera: cross(b − a, c − a) points toward the viewer, so outward on a
            // closed convex lobe. (The far shader's normal is turned to the camera, so a reversed winding would show the far side's
            // inner faces through culling, not a dark face.)
            Lobe(sides, out List<Vector3> v, out List<int> t);
            var centre = new Vector3(0f, 6f, 0f);
            for (int i = 9; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Assert.That(Vector3.Dot(n, (a + b + c) / 3f - centre), Is.GreaterThan(0f), $"triangle {i / 3} faces out");
            }

            // Every edge of the lobe is shared by exactly two triangles in opposite directions: closed, consistently wound.
            var edges = new Dictionary<long, int>();
            for (int i = 9; i < t.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    long e = (long)t[i + k] << 32 | (uint)t[i + (k + 1) % 3];
                    edges[e] = edges.TryGetValue(e, out int n) ? n + 1 : 1;
                }
            }

            foreach (KeyValuePair<long, int> e in edges)
            {
                long reverse = (e.Key & 0xFFFFFFFFL) << 32 | (uint)(e.Key >> 32);
                Assert.That(e.Value, Is.EqualTo(1));
                Assert.That(edges.ContainsKey(reverse), Is.True, "closed lobe");
            }
        }

        // Clear noon of the review tours: sun 1.35 at 57.5°, trilight ambient sky (0.58, 0.66, 0.78), sides 0.62 of it.
        private static Vector3 LitFace(Vector3 albedo) => Vector3.Scale(albedo, new Vector3(1.46f, 1.52f, 1.62f));

        private static Vector3 ShadedFace(Vector3 albedo) => Vector3.Scale(albedo, new Vector3(0.46f, 0.51f, 0.58f));

        // The same noon under the far shader's foliage light (half-Lambert sun, w = 1 above 30°): the cap (N·L 0.84 → 0.92) with the sky
        // and the upper band toward the sun (0.62 → 0.81) with the sides' ambient; shaded: faces turned away (0 → 0.5, 0.15 → 0.575).
        private static Vector3 FoliageLitFace(Vector3 albedo) => Vector3.Scale(albedo, new Vector3(1.638f, 1.702f, 1.791f));

        private static Vector3 FoliageShadedFace(Vector3 albedo) => Vector3.Scale(albedo, new Vector3(1.086f, 1.135f, 1.205f));

        private static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        [Test]
        public void LeafCrownsCarryALitTopAndAShadedUnderside()
        {
            foreach (string name in Broadleaf)
            {
                FarTreeShapes.TryGet(name, out FarTreeStyle style);
                var v = new List<Vector3>();
                var t = new List<int>();
                var c = new List<Color32>();
                TreeImpostor.Build(style, v, t, c);
                Assert.That(t.Count / 3, Is.EqualTo(TreeImpostor.TrianglesOf(style)), name + ": no extra triangles");
                for (int i = 0; i < t.Count; i += 3)
                {
                    // Flat colour per triangle whichever vertex is the provoking one (first: D3D, Vulkan, Metal; last: OpenGL).
                    Assert.That(c[t[i]], Is.EqualTo(c[t[i + 2]]), $"{name}: triangle {i / 3} starts and ends on one colour");
                }

                int leafVerts = 0;
                for (int i = 0; i < v.Count; i++)
                {
                    if (TreeLobes.IsLeafCrownAlpha(c[i].a)) leafVerts++;   // any season class (seasons pass)
                    else Assert.That(c[i].a, Is.EqualTo(255), name + ": every other face keeps alpha 255");
                }

                if (!style.LeafCrown)
                {
                    Assert.That(leafVerts, Is.EqualTo(0), name + ": a faceted solid (Generic, pine) is lit as before");
                    continue;
                }

                Assert.That(leafVerts, Is.GreaterThan(v.Count / 2), name + ": the crown is foliage");
                // Per lobe (after the trunk's 4 and the fork arms' 8 vertices): underside ring darker than the widest, the cap lightest.
                int sides = TreeLobes.NearSides;
                for (int l = 0, first = 12; l < style.Lobes.Length; l++, first += 3 * sides)
                {
                    float under = Luma(c[first]), widest = Luma(c[first + sides]), cap = Luma(c[first + 2 * sides]);
                    Assert.That(cap, Is.GreaterThan(widest), $"{name} lobe {l}: lit top");
                    Assert.That(widest, Is.GreaterThan(under * 1.15f), $"{name} lobe {l}: shaded underside");
                    Assert.That(c[first + 2 * sides].b * 1.0f / Math.Max(1, (int)c[first + 2 * sides].g), Is.LessThanOrEqualTo(c[first].b * 1.0f / Math.Max(1, (int)c[first].g) + 1e-3f),
                        $"{name} lobe {l}: the top warmer than the shade");
                }
            }
        }

        private static float LabL(Vector3 linear)
        {
            float y = 0.2126f * linear.x + 0.7152f * linear.y + 0.0722f * linear.z;
            return y > 0.008856f ? 116f * Mathf.Pow(y, 1f / 3f) - 16f : 903.3f * y;
        }

        [Test]
        public void ImpostorsMatchTheRealCrownsUnderNoonLight()
        {
            // Real crowns of the clear-noon tour (2026-10-06 09:43), sun-lit and shaded averages, linear, haze removed (ENDLESS_ROAD.md).
            var real = new Dictionary<string, (Vector3 lit, Vector3 shade)>
            {
                ["SM_Env_Tree_Meadow_01"] = (new Vector3(0.168f, 0.184f, 0.042f), new Vector3(0.027f, 0.032f, 0.028f)),
                ["SM_Env_Tree_Birch_01"] = (new Vector3(0.366f, 0.227f, 0.041f), new Vector3(0.078f, 0.045f, 0.030f)),
                ["SM_Env_Tree_Fruit_01"] = (new Vector3(0.309f, 0.371f, 0.042f), new Vector3(0.061f, 0.082f, 0.044f)),
            };
            foreach (KeyValuePair<string, (Vector3 lit, Vector3 shade)> r in real)
            {
                FarTreeShapes.TryGet(r.Key, out FarTreeStyle style);
                Assert.That(style.LeafCrown, Is.True, r.Key);
                // Distant-tree pass (2026-10-06 evening): a leaf crown's sun-lit faces are its cap-ring faces (LeafTop) under the
                // shader's foliage light, its shade the underside-ring faces (LeafUnder) facing away from the sun.
                Vector3 a = TreeImpostor.LeafColor(style);
                float litL = LabL(FoliageLitFace(Vector3.Scale(a, TreeLobes.LeafTop)));
                float shadeL = LabL(FoliageShadedFace(Vector3.Scale(a, TreeLobes.LeafUnder)));
                Assert.That(litL, Is.EqualTo(LabL(r.Value.lit)).Within(12f), r.Key + ": sun-lit faces as light as the crown's sun-lit leaves");
                Assert.That(litL, Is.LessThanOrEqualTo(LabL(r.Value.lit) + 2f), r.Key + ": never lighter than the real crown's lit leaves");
                Assert.That(shadeL, Is.GreaterThanOrEqualTo(LabL(r.Value.shade)), r.Key + ": no darker than the crown's shade");
                Assert.That(litL - shadeL, Is.GreaterThan(15f), r.Key + ": light and shade read on the solid");
            }

            // Every living crown is light enough for its sun-lit faces to show through the haze (the morning's oaks were near black).
            foreach (string name in Broadleaf)
            {
                FarTreeShapes.TryGet(name, out FarTreeStyle style);
                Assert.That(LabL(LitFace(TreeImpostor.LeafColor(style))), Is.GreaterThan(35f), name);
            }
        }

        [Test]
        public void TheMeadowOakKeepsItsSwapAfterRounding()
        {
            Assert.That(FarTreeShapes.TryGetLobedSilhouette("SM_Env_Tree_Meadow_01", out float near, out _), Is.True);
            Assert.That(near, Is.LessThanOrEqualTo(1.35f), "rounding the lobes must not push the Meadow oak over 1.35 m (3 px at 420 m is 1.47)");
        }
    }
}
