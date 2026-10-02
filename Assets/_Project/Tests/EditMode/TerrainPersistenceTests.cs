using MedievalZen.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// Regression tests for the lost-alphamap bug: the generated TerrainData must keep its splat paint when it is saved and
    /// loaded back from disk. Uses the production path (<see cref="TerrainDataWriter"/>: create the asset, then paint, then
    /// save) on a small terrain in a temporary folder that is deleted afterwards.
    /// </summary>
    public sealed class TerrainPersistenceTests
    {
        private const string Folder = "Assets/_Project/Generated/_Test";
        private const string TerrainPath = Folder + "/TerrainPersistenceTest.asset";
        private const int HeightmapResolution = 129;
        private const int AlphamapResolution = 64;
        private const int Layers = 4;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Folder);
            TerrainDataWriter.EnsureFolder(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void PersistThenPaint_AlphamapsSurviveReloadFromDisk()
        {
            TerrainLayer[] layers = CreateTempLayers();
            float[,,] splat = Pattern(AlphamapResolution, Layers);
            float[,] heights = Slope(HeightmapResolution);

            // Production path: asset on disk first, then heights and paint, then save.
            TerrainData data = TerrainDataWriter.Create("TerrainPersistenceTest", HeightmapResolution, new Vector3(128f, 20f, 128f), TerrainPath);
            Assert.That(AssetDatabase.GetAssetPath(data), Is.EqualTo(TerrainPath), "The TerrainData must be an asset before it is painted.");
            TerrainDataWriter.WriteHeights(data, heights);
            float[,,] painted = TerrainDataWriter.WriteSplat(data, layers, splat);
            Assert.That(painted, Is.Not.Null);
            TerrainDataWriter.Save(data);

            TerrainData reloaded = ReloadFromDisk(data, TerrainPath);

            Assert.That(reloaded, Is.Not.Null, "The TerrainData asset could not be loaded back.");
            Assert.That(reloaded.alphamapResolution, Is.EqualTo(AlphamapResolution));
            Assert.That(reloaded.alphamapLayers, Is.EqualTo(Layers));
            AssertAlphamapsMatch(reloaded, splat);

            SplatCheck check = TerrainDataWriter.Verify(reloaded, painted);
            Assert.That(check.ok, Is.True, check.message);

            float[,] back = reloaded.GetHeights(0, 0, HeightmapResolution, HeightmapResolution);
            for (int i = 0; i < HeightmapResolution; i += 16)
            {
                Assert.That(back[i, i], Is.EqualTo(heights[i, i]).Within(1e-3f), "Heights must survive too.");
            }
        }

        [Test]
        public void Verify_DetectsTheLostPaintSignature()
        {
            TerrainLayer[] layers = CreateTempLayers();
            float[,,] splat = Pattern(AlphamapResolution, Layers);
            TerrainData data = TerrainDataWriter.Create("TerrainPersistenceTest", HeightmapResolution, new Vector3(128f, 20f, 128f), TerrainPath);
            float[,,] painted = TerrainDataWriter.WriteSplat(data, layers, splat);

            // What the broken asset looked like: layer 0 at 100 % everywhere.
            var lost = new float[AlphamapResolution, AlphamapResolution, Layers];
            for (int z = 0; z < AlphamapResolution; z++)
            {
                for (int x = 0; x < AlphamapResolution; x++) lost[z, x, 0] = 1f;
            }

            data.SetAlphamaps(0, 0, lost);
            SplatCheck check = TerrainDataWriter.Verify(data, painted);
            Assert.That(check.ok, Is.False);
            Assert.That(check.firstLayerOnly, Is.GreaterThan(check.samples / 4));
            StringAssert.Contains("lost-alphamap", check.message);
        }

        /// <summary>
        /// Documents the Unity trap behind the bug (paint an in-memory TerrainData, CreateAsset afterwards). Explicit because
        /// it asserts engine behaviour, not ours: if it fails, this Unity version keeps the paint and the cause was elsewhere.
        /// </summary>
        [Test, Explicit("Diagnosis of the old paint-then-persist order; run it by hand.")]
        public void Diagnosis_PaintThenPersist_LosesAlphamaps()
        {
            TerrainLayer[] layers = CreateTempLayers();
            float[,,] splat = Pattern(AlphamapResolution, Layers);
            var data = new TerrainData { name = "TerrainPersistenceTest" };
            data.heightmapResolution = HeightmapResolution;
            data.size = new Vector3(128f, 20f, 128f);
            data.alphamapResolution = AlphamapResolution;
            data.terrainLayers = layers;
            data.SetAlphamaps(0, 0, splat);
            AssetDatabase.CreateAsset(data, TerrainPath);
            AssetDatabase.SaveAssets();

            TerrainData reloaded = ReloadFromDisk(data, TerrainPath);
            float[,,] back = reloaded.GetAlphamaps(0, 0, AlphamapResolution, AlphamapResolution);
            int firstLayerOnly = 0;
            for (int z = 0; z < AlphamapResolution; z++)
            {
                for (int x = 0; x < AlphamapResolution; x++)
                {
                    if (back[z, x, 0] > 0.99f) firstLayerOnly++;
                }
            }

            Assert.That(firstLayerOnly, Is.EqualTo(AlphamapResolution * AlphamapResolution),
                "Paint-then-persist kept the alphamaps in this Unity version.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Unloads the in-memory TerrainData (and its alphamap textures) and loads the asset again from the file.</summary>
        private static TerrainData ReloadFromDisk(TerrainData data, string path)
        {
            foreach (Texture2D texture in data.alphamapTextures)
            {
                if (texture != null) Resources.UnloadAsset(texture);
            }

            Resources.UnloadAsset(data);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        }

        private static void AssertAlphamapsMatch(TerrainData data, float[,,] expected)
        {
            int res = expected.GetLength(0);
            float[,,] back = data.GetAlphamaps(0, 0, res, res);
            int firstLayerOnly = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    if (back[z, x, 0] > 0.99f) firstLayerOnly++;
                    for (int l = 0; l < expected.GetLength(2); l++)
                    {
                        Assert.That(back[z, x, l], Is.EqualTo(expected[z, x, l]).Within(TerrainDataWriter.CheckTolerance),
                            $"Alphamap texel ({x}, {z}) layer {l} after reloading from disk.");
                    }
                }
            }

            Assert.That(firstLayerOnly, Is.LessThan(res * res / 2), "The reloaded alphamap is 'first layer 100 %' — the paint was lost.");
        }

        /// <summary>Four quadrant layers with soft diagonal blends; every texel sums to 1 and layer 0 covers only a quarter.</summary>
        private static float[,,] Pattern(int res, int layers)
        {
            var splat = new float[res, res, layers];
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int main = (x * 2 / res) + 2 * (z * 2 / res);
                    float blend = ((x + z) % 8) / 16f;   // 0 .. 0.44
                    int other = (main + 1) % layers;
                    splat[z, x, main] = 1f - blend;
                    splat[z, x, other] += blend;
                }
            }

            return splat;
        }

        private static float[,] Slope(int n)
        {
            var heights = new float[n, n];
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++) heights[z, x] = 0.1f + 0.6f * x / (n - 1f);
            }

            return heights;
        }

        /// <summary>Temporary TerrainLayer assets in the test folder (persistent terrain data must reference assets).</summary>
        private static TerrainLayer[] CreateTempLayers()
        {
            var layers = new TerrainLayer[Layers];
            for (int l = 0; l < Layers; l++)
            {
                layers[l] = new TerrainLayer { name = $"TestLayer{l}", tileSize = new Vector2(4f, 4f) };
                AssetDatabase.CreateAsset(layers[l], $"{Folder}/TestLayer{l}.terrainlayer");
            }

            return layers;
        }
    }
}
