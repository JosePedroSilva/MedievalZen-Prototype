using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>Result of comparing a TerrainData's alphamaps with the weights that were painted (see <see cref="TerrainDataWriter.Verify"/>).</summary>
    public struct SplatCheck
    {
        /// <summary>True when the alphamaps match the painted weights.</summary>
        public bool ok;
        /// <summary>Texels compared.</summary>
        public int samples;
        /// <summary>Texels whose weights differ by more than <see cref="TerrainDataWriter.CheckTolerance"/>.</summary>
        public int mismatches;
        /// <summary>Largest per-layer difference seen.</summary>
        public float maxError;
        /// <summary>Compared texels that read back as "first layer 100 %" although they were painted otherwise (the lost-paint signature).</summary>
        public int firstLayerOnly;
        /// <summary>Human-readable outcome.</summary>
        public string message;
    }

    /// <summary>
    /// Writes generated heights and splat weights into a <see cref="TerrainData"/> in an order that survives saving.
    /// In the editor the TerrainData asset is created on disk FIRST (<see cref="Create"/>) and only then painted: painting
    /// an in-memory TerrainData and calling <c>AssetDatabase.CreateAsset</c> afterwards saves the alphamap textures
    /// ("SplatAlpha 0/1") with default contents (first layer 100 % everywhere). At runtime (Play mode, builds) everything
    /// stays in memory. Used by <see cref="WorldBuilder"/>, the Repaint Terrain menu and the persistence tests.
    /// </summary>
    public static class TerrainDataWriter
    {
        /// <summary>Folder the generated TerrainData assets are saved to in the editor.</summary>
        public const string AssetFolder = "Assets/_Project/Generated";

        /// <summary>Largest per-layer difference (8-bit storage, renormalisation) still counted as a match.</summary>
        public const float CheckTolerance = 0.02f;

        /// <summary>Largest detail/base-map resolution used for the distant base map (1024 = 2 m texels on the default map).</summary>
        public const int MaxBaseMapResolution = 1024;

        /// <summary>Asset path of the generated TerrainData for <paramref name="seed"/>.</summary>
        public static string AssetPathFor(int seed) => $"{AssetFolder}/Terrain_{seed}.asset";

        /// <summary>
        /// A new, empty TerrainData with the heightmap resolution and size set. In the editor outside Play mode, when
        /// <paramref name="assetPath"/> is given, it is saved there immediately (replacing any asset at that path), so
        /// everything written afterwards goes into the persistent object. Otherwise it stays in memory.
        /// </summary>
        public static TerrainData Create(string name, int heightmapResolution, Vector3 size, string assetPath)
        {
            var data = new TerrainData { name = name };
            data.heightmapResolution = heightmapResolution;
            data.size = size;
#if UNITY_EDITOR
            if (!Application.isPlaying && !string.IsNullOrEmpty(assetPath))
            {
                int slash = assetPath.LastIndexOf('/');
                if (slash > 0) EnsureFolder(assetPath.Substring(0, slash));
                if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(assetPath) != null) UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                UnityEditor.AssetDatabase.CreateAsset(data, assetPath);
            }
#endif
            return data;
        }

        /// <summary>Persists an existing in-memory TerrainData at <paramref name="assetPath"/> (editor only), before it is (re)painted.</summary>
        public static void Persist(TerrainData data, string assetPath)
        {
#if UNITY_EDITOR
            if (data == null || Application.isPlaying || IsPersistent(data)) return;
            int slash = assetPath.LastIndexOf('/');
            if (slash > 0) EnsureFolder(assetPath.Substring(0, slash));
            if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(assetPath) != null) UnityEditor.AssetDatabase.DeleteAsset(assetPath);
            UnityEditor.AssetDatabase.CreateAsset(data, assetPath);
#endif
        }

        /// <summary>True when <paramref name="data"/> is an asset on disk (always false at runtime).</summary>
        public static bool IsPersistent(TerrainData data)
        {
#if UNITY_EDITOR
            return data != null && UnityEditor.EditorUtility.IsPersistent(data);
#else
            return false;
#endif
        }

        /// <summary>Writes the normalised heightmap (<c>[z, x]</c>, 0..1).</summary>
        public static void WriteHeights(TerrainData data, float[,] heights)
        {
            data.SetHeights(0, 0, heights);
        }

        /// <summary>
        /// Runtime streaming (the endless road): writes the heights without rebuilding the terrain's LOD yet, so the rebuild
        /// (<see cref="FinishHeights"/>) can run in a later frame. In-memory data only (never persisted).
        /// </summary>
        public static void WriteHeightsDeferred(TerrainData data, float[,] heights)
        {
            data.SetHeightsDelayLOD(0, 0, heights);
        }

        /// <summary>Rebuilds the LOD after <see cref="WriteHeightsDeferred"/>.</summary>
        public static void FinishHeights(TerrainData data)
        {
            data.SyncHeightmap();
        }

        /// <summary>
        /// Runtime streaming: writes rows [<paramref name="z0"/>, z0 + rows) of the alphamaps (weights per terrain layer, already
        /// folded with <see cref="FoldSplat"/> semantics), so a large splat can be written over several frames. The layers and
        /// alphamap resolution must already be set. In-memory data only (never persisted).
        /// </summary>
        public static void WriteSplatBand(TerrainData data, int z0, float[,,] band)
        {
            data.SetAlphamaps(0, z0, band);
        }

        /// <summary>
        /// Sets the alphamap resolution to the splat's, assigns the terrain layers (palette order = splat channel order;
        /// channels without a layer fold into the first available one) and writes the alphamaps. Returns the weights as
        /// written (per terrain layer), or null when the palette has no layers.
        /// </summary>
        public static float[,,] WriteSplat(TerrainData data, IReadOnlyList<TerrainLayer> paletteLayers, float[,,] splat)
        {
            int res = splat.GetLength(0);
            TerrainLayer[] layers = ResolveLayers(paletteLayers, splat.GetLength(2), out int[] channelToLayer);
            if (layers == null) return null;

            data.alphamapResolution = res;
            data.baseMapResolution = Mathf.Clamp(res, 16, MaxBaseMapResolution);
            data.terrainLayers = layers;
            float[,,] written = FoldSplat(splat, channelToLayer, layers.Length);
            data.SetAlphamaps(0, 0, written);
            data.SetBaseMapDirty();
            return written;
        }

        /// <summary>
        /// Editor, persistent data only: syncs the alphamap textures and base map, makes sure the alphamap textures are
        /// sub-assets of the TerrainData, marks it dirty and saves the asset database. No-op at runtime.
        /// </summary>
        public static void Save(TerrainData data)
        {
#if UNITY_EDITOR
            if (data == null || Application.isPlaying || !IsPersistent(data)) return;
            data.SyncTexture(TerrainData.AlphamapTextureName);
            data.SetBaseMapDirty();
            foreach (Texture2D texture in data.alphamapTextures)
            {
                if (texture != null && !UnityEditor.AssetDatabase.Contains(texture))
                {
                    UnityEditor.AssetDatabase.AddObjectToAsset(texture, data);
                    Debug.LogWarning($"Terrain alphamap texture '{texture.name}' was not part of {UnityEditor.AssetDatabase.GetAssetPath(data)}; added it.", data);
                }
            }

            UnityEditor.EditorUtility.SetDirty(data);
            UnityEditor.AssetDatabase.SaveAssets();
#endif
        }

        /// <summary>
        /// <see cref="Save"/>, then the self-check: reads the alphamaps back (from a copy of the asset loaded from disk in the
        /// editor) at ~200 texels and compares them with <paramref name="painted"/>. Logs an error if the paint did not
        /// survive (after one repaint-and-save retry), else a one-line per-layer coverage summary. Returns true on success.
        /// </summary>
        public static bool SaveAndCheck(TerrainData data, float[,,] painted, string context)
        {
            if (data == null) return false;
            Save(data);
            if (painted == null) return false;       // no terrain layers (already reported by WriteSplat)
            if (!IsPersistent(data)) return true;   // runtime: nothing is saved

            SplatCheck check = VerifySaved(data, painted, out string coverage);
            if (!check.ok)
            {
                Debug.LogWarning($"{context}: terrain paint check failed ({check.message}); repainting and saving once more.", data);
                data.SetAlphamaps(0, 0, painted);
                Save(data);
                check = VerifySaved(data, painted, out coverage);
            }

            if (!check.ok)
            {
                Debug.LogError($"{context}: the terrain paint did NOT survive saving {AssetPath(data)} — {check.message}. " +
                               "The ground will render as plain grass. Run MedievalZen/Repaint Terrain, and the TerrainPersistenceTests in the Test Runner.", data);
                return false;
            }

            Debug.Log($"{context}: terrain paint saved and verified from disk ({check.samples} texels, max error {check.maxError:0.000}), " +
                      $"{data.alphamapResolution}² alphamap — {coverage}", data);
            return true;
        }

        /// <summary>
        /// Compares ~<paramref name="samples"/> deterministic texels of <paramref name="data"/>'s alphamaps with
        /// <paramref name="expected"/> (<c>[z, x, layer]</c>, as returned by <see cref="WriteSplat"/>). Half the samples are
        /// uniform, half are texels painted mostly with layers other than the first, so lost paint cannot hide in grass.
        /// </summary>
        public static SplatCheck Verify(TerrainData data, float[,,] expected, int samples = 200)
        {
            var check = new SplatCheck();
            int res = expected.GetLength(0);
            int layerCount = expected.GetLength(2);
            if (data == null)
            {
                check.message = "no TerrainData";
                return check;
            }

            if (data.alphamapResolution != res || data.alphamapLayers != layerCount)
            {
                check.message = $"alphamap is {data.alphamapResolution}² × {data.alphamapLayers} layers, expected {res}² × {layerCount}";
                return check;
            }

            uint state = 0x9E3779B9u;
            int Next(int range)
            {
                state = state * 1664525u + 1013904223u;
                return (int)((state >> 8) % (uint)range);
            }

            for (int s = 0; s < samples; s++)
            {
                int x = Next(res);
                int z = Next(res);
                if (s % 2 == 1)
                {
                    // Look for a texel that is not mostly layer 0 (bounded search; keep the last candidate otherwise).
                    for (int tries = 0; tries < 64 && expected[z, x, 0] > 0.5f; tries++)
                    {
                        x = Next(res);
                        z = Next(res);
                    }
                }

                float[,,] got = data.GetAlphamaps(x, z, 1, 1);
                float error = 0f;
                for (int l = 0; l < layerCount; l++) error = Mathf.Max(error, Mathf.Abs(got[0, 0, l] - expected[z, x, l]));
                check.samples++;
                check.maxError = Mathf.Max(check.maxError, error);
                if (error > CheckTolerance) check.mismatches++;
                if (got[0, 0, 0] > 0.99f && expected[z, x, 0] < 0.9f) check.firstLayerOnly++;
            }

            check.ok = check.mismatches <= Mathf.Max(1, check.samples / 100);
            check.message = check.ok
                ? $"{check.samples} texels match"
                : $"{check.mismatches} of {check.samples} texels differ (max error {check.maxError:0.00})" +
                  (check.firstLayerOnly > check.samples / 4 ? $"; {check.firstLayerOnly} read back as first layer 100 % — the lost-alphamap signature" : string.Empty);
            return check;
        }

        /// <summary>One-line per-layer coverage (e.g. "lush 28 %, dry 27 %, dirt 1.4 %, …") from rows of the alphamaps.</summary>
        public static string CoverageSummary(TerrainData data)
        {
            if (data == null || data.alphamapLayers == 0) return "no terrain layers";
            int res = data.alphamapResolution;
            int layerCount = data.alphamapLayers;
            var sums = new double[layerCount];
            int step = Mathf.Max(1, res / 256);
            int rows = 0;
            for (int z = 0; z < res; z += step)
            {
                float[,,] row = data.GetAlphamaps(0, z, res, 1);
                for (int x = 0; x < res; x++)
                {
                    for (int l = 0; l < layerCount; l++) sums[l] += row[0, x, l];
                }

                rows++;
            }

            TerrainLayer[] layers = data.terrainLayers;
            var sb = new StringBuilder();
            for (int l = 0; l < layerCount; l++)
            {
                string layerName = layerCount == TerrainShaper.LayerCount ? TerrainShaper.LayerNames[l]
                    : layers != null && l < layers.Length && layers[l] != null ? layers[l].name : $"layer {l}";
                double percent = 100.0 * sums[l] / ((double)rows * res);
                if (l > 0) sb.Append(", ");
                sb.Append(layerName).Append(' ').Append(percent < 10.0 ? percent.ToString("0.0") : percent.ToString("0")).Append(" %");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Editor: verifies what is on disk — copies the saved asset, loads the copy (deserialised from the file, the live
        /// object is untouched), checks it and deletes it. Falls back to the in-memory object when that is not possible.
        /// </summary>
        private static SplatCheck VerifySaved(TerrainData data, float[,,] painted, out string coverage)
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(data);
            if (!string.IsNullOrEmpty(path))
            {
                string copyFolder = AssetFolder + "/_Verify";
                EnsureFolder(copyFolder);
                string copyPath = $"{copyFolder}/{System.IO.Path.GetFileNameWithoutExtension(path)}_check.asset";
                try
                {
                    if (UnityEditor.AssetDatabase.CopyAsset(path, copyPath))
                    {
                        var copy = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainData>(copyPath);
                        if (copy != null)
                        {
                            SplatCheck fromDisk = Verify(copy, painted);
                            coverage = CoverageSummary(copy);
                            return fromDisk;
                        }
                    }
                }
                finally
                {
                    UnityEditor.AssetDatabase.DeleteAsset(copyPath);
                    UnityEditor.AssetDatabase.DeleteAsset(copyFolder);
                }
            }
#endif
            coverage = CoverageSummary(data);
            return Verify(data, painted);
        }

        private static string AssetPath(TerrainData data)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.GetAssetPath(data);
#else
            return data != null ? data.name : "terrain";
#endif
        }

        /// <summary>
        /// The terrain layers for <paramref name="channels"/> splat channels in palette order, and which layer each channel
        /// lands in (-1 = no layer: folded into the first one, with a warning). Null (with an error) when the palette has no
        /// layers at all. Shared by <see cref="WriteSplat"/> and the endless road's tile builder.
        /// </summary>
        public static TerrainLayer[] ResolveLayers(IReadOnlyList<TerrainLayer> paletteLayers, int channels, out int[] channelToLayer)
        {
            var layers = new List<TerrainLayer>();
            channelToLayer = new int[channels];
            for (int c = 0; c < channelToLayer.Length; c++)
            {
                TerrainLayer layer = paletteLayers != null && c < paletteLayers.Count ? paletteLayers[c] : null;
                if (layer != null)
                {
                    channelToLayer[c] = layers.Count;
                    layers.Add(layer);
                }
                else
                {
                    channelToLayer[c] = -1;
                    Debug.LogWarning($"WorldPalette has no terrain layer for splat channel {c}; it is merged into the first layer.");
                }
            }

            if (layers.Count == 0)
            {
                Debug.LogError("WorldPalette has no terrain layers; the terrain will be untextured.");
                return null;
            }

            return layers.ToArray();
        }

        /// <summary>
        /// Maps splat channels onto the available layers (missing channels fold into layer 0), renormalised. Returns
        /// <paramref name="splat"/> itself when every channel has its own layer.
        /// </summary>
        public static float[,,] FoldSplat(float[,,] splat, int[] channelToLayer, int layerCount)
        {
            bool identity = layerCount == channelToLayer.Length;
            for (int c = 0; c < channelToLayer.Length && identity; c++) identity = channelToLayer[c] == c;
            if (identity) return splat;

            int h = splat.GetLength(0);
            int w = splat.GetLength(1);
            var result = new float[h, w, layerCount];
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int c = 0; c < channelToLayer.Length; c++)
                    {
                        int l = channelToLayer[c] >= 0 ? channelToLayer[c] : 0;
                        result[z, x, l] += splat[z, x, c];
                    }
                }
            }

            return result;
        }

        /// <summary>Creates <paramref name="path"/> (and its parents) as asset folders if missing (editor only; no-op at runtime).</summary>
        public static void EnsureFolder(string path)
        {
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            UnityEditor.AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
#endif
        }
    }
}
