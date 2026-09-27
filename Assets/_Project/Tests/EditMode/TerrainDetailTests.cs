using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>Tests for the heightmap smoothing and the terrain-detail (grass) sizing.</summary>
    public sealed class TerrainDetailTests
    {
        private const float Spacing = 2f;
        private const float HeightScale = 100f;

        /// <summary>A 65×65 map: gentle 0.4 m bumps on the left half, a 30 m cliff step in the middle.</summary>
        private static float[,] BumpyMapWithCliff()
        {
            const int n = 65;
            var map = new float[n, n];
            var rng = new System.Random(7);
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float bump = (float)rng.NextDouble() * 0.4f / HeightScale;
                    float cliff = x >= n / 2 ? 30f / HeightScale : 0f;
                    map[z, x] = 0.2f + bump + cliff;
                }
            }

            return map;
        }

        private static float Roughness(float[,] map, int x0, int x1)
        {
            int n = map.GetLength(0);
            double sum = 0;
            int count = 0;
            for (int z = 1; z < n - 1; z++)
            {
                for (int x = x0; x < x1; x++)
                {
                    float laplacian = map[z, x - 1] + map[z, x + 1] + map[z - 1, x] + map[z + 1, x] - 4f * map[z, x];
                    sum += Mathf.Abs(laplacian) * HeightScale;
                    count++;
                }
            }

            return (float)(sum / count);
        }

        [Test]
        public void SmoothLowSlopes_IsDeterministic_SmoothsBumps_KeepsCliff()
        {
            float[,] original = BumpyMapWithCliff();
            float[,] a = (float[,])original.Clone();
            float[,] b = (float[,])original.Clone();
            HeightmapGenerator.SmoothLowSlopes(a, Spacing, HeightScale);
            HeightmapGenerator.SmoothLowSlopes(b, Spacing, HeightScale);

            int n = a.GetLength(0);
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++) Assert.That(a[z, x], Is.EqualTo(b[z, x]));
            }

            Assert.That(Roughness(a, 2, n / 2 - 3), Is.LessThan(Roughness(original, 2, n / 2 - 3) * 0.5f), "Gentle bumps should be smoothed.");

            // The cliff (slope 30 m over 2 m) is left exactly as it was.
            for (int z = 0; z < n; z++)
            {
                Assert.That(a[z, n / 2 - 1], Is.EqualTo(original[z, n / 2 - 1]));
                Assert.That(a[z, n / 2], Is.EqualTo(original[z, n / 2]));
            }
        }

        [Test]
        public void Heightmap_WithSmoothing_SameSeed_IsIdentical()
        {
            var p = new WorldGenParams { seed = 5, worldSize = 800f, heightmapResolution = 129, hillAmplitude = 120f, alphamapResolutionOverride = 128 };
            float[,] a = HeightmapGenerator.Generate(p);
            float[,] b = HeightmapGenerator.Generate(p);
            Assert.That(a, Is.EqualTo(b));
        }

        [TestCase(PropRole.ShortGrass, 0.22f, 1.9f)]
        [TestCase(PropRole.MedGrass, 0.47f, 1.9f)]
        [TestCase(PropRole.TallGrass, 1.0f, 4.6f)]
        [TestCase(PropRole.Wildflowers, 0.64f, 0.8f)]
        [TestCase(PropRole.GenFlowers, 1.1f, 0.7f)]
        [TestCase(PropRole.FlatFlowers, 0.15f, 2.2f)]
        [TestCase(PropRole.GroundCover, 1.5f, 2.9f)]
        [TestCase(PropRole.SunflowerDetail, 1.58f, 0.8f)]
        public void PrototypeScale_LandsInTargetHeight_AndCapsWidth(string family, float nativeHeight, float nativeWidth)
        {
            Vector2 target = DetailPlanner.TargetHeight(family);
            var (minWidth, maxWidth, minHeight, maxHeight) = DetailPlanner.PrototypeScale(family, nativeHeight, nativeWidth);

            Assert.That(minHeight * nativeHeight, Is.EqualTo(target.x).Within(1e-4f));
            Assert.That(maxHeight * nativeHeight, Is.EqualTo(target.y).Within(1e-4f));
            Assert.That(minWidth, Is.GreaterThan(0f));
            Assert.That(minWidth, Is.LessThanOrEqualTo(maxWidth));
            Assert.That(maxWidth * nativeWidth, Is.LessThanOrEqualTo(DetailPlanner.MaxDetailWidth + 1e-4f));
        }

        [Test]
        public void TargetHeights_GrassIsAtMostKneeHigh()
        {
            foreach (string family in new[] { PropRole.ShortGrass, PropRole.MedGrass, PropRole.TallGrass, PropRole.Wildflowers, PropRole.GenFlowers, PropRole.FlatFlowers, PropRole.GroundCover })
            {
                Vector2 target = DetailPlanner.TargetHeight(family);
                Assert.That(target.x, Is.GreaterThan(0f), family);
                Assert.That(target.x, Is.LessThan(target.y), family);
                Assert.That(target.y, Is.LessThanOrEqualTo(0.75f), family + " should stay below knee height.");
            }
        }

        [Test]
        public void Catalog_HasNoWidePatchMeshesAsDetails()
        {
            foreach (var (family, paths) in SyntyCatalog.Details)
            {
                foreach (string path in paths)
                {
                    Assert.That(path, Does.Not.Contain("Grass_Large_"), family);
                    Assert.That(path, Does.Not.Contain("Wildflowers_Patch_"), family);
                    Assert.That(path, Does.Not.Contain("Grass_Tall_Clump_05"), family);
                }
            }

            foreach (DetailRule rule in DetailPlanner.DefaultRules())
            {
                Assert.That(PaletteKeys.Default.DetailCount(rule.family), Is.GreaterThan(0), rule.family + " has no catalog entry.");
            }
        }
    }
}
