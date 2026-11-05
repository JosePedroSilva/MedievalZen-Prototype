using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Finds autumn-coloured trees near the traveller for the falling-leaf effect: the birches (their crowns are orange and
    /// yellow in the Meadow pack). On the endless road it asks the instanced tree renderer through
    /// <see cref="MedievalZen.Endless.EndlessWorld.NearestTree"/>; in the finite world it scans the terrain trees once (the world
    /// never changes). Scene positions throughout.
    /// </summary>
    internal sealed class AutumnTreeFinder
    {
        /// <summary>Prototype / prefab names containing this are autumn trees.</summary>
        public const string NameKey = "Birch";

        /// <summary>Prototype / prefab names containing this are fruit trees (spring blossom, 2026-10-06).</summary>
        public const string FruitKey = "Tree_Fruit";

        private const int MaxFiniteTrees = 20000;

        private readonly string nameKey;

        /// <summary>A finder for the trees whose names contain <paramref name="key"/> (the birches by default).</summary>
        public AutumnTreeFinder(string key = NameKey)
        {
            nameKey = string.IsNullOrEmpty(key) ? NameKey : key;
        }

        private readonly List<Vector4> finiteTrees = new List<Vector4>();   // xyz base, w height
        private readonly List<float> finiteCrowns = new List<float>();
        private bool scanned;

        /// <summary>
        /// The nearest autumn tree whose base is between <paramref name="minDistance"/> and <paramref name="maxDistance"/>
        /// (horizontal) from <paramref name="near"/>; its base, height and crown radius in metres.
        /// </summary>
        public bool TryFind(Vector3 near, float minDistance, float maxDistance, out Vector3 position, out float height, out float crown)
        {
            MedievalZen.Endless.EndlessWorld endless = MedievalZen.Endless.EndlessWorld.Current;
            if (endless != null) return endless.NearestTree(near, minDistance, maxDistance, nameKey, out position, out height, out crown);

            if (!scanned) ScanFiniteWorld();
            position = default;
            height = crown = 0f;
            float best = float.MaxValue, min2 = minDistance * minDistance, max2 = maxDistance * maxDistance;
            int found = -1;
            for (int i = 0; i < finiteTrees.Count; i++)
            {
                Vector4 t = finiteTrees[i];
                float dx = t.x - near.x, dz = t.z - near.z, d2 = dx * dx + dz * dz;
                if (d2 < min2 || d2 > max2 || d2 >= best) continue;
                best = d2;
                found = i;
            }

            if (found < 0) return false;
            Vector4 f = finiteTrees[found];
            position = new Vector3(f.x, f.y, f.z);
            height = f.w;
            crown = finiteCrowns[found];
            return true;
        }

        private void ScanFiniteWorld()
        {
            scanned = true;
            finiteTrees.Clear();
            finiteCrowns.Clear();
            var sizes = new Dictionary<GameObject, Bounds>();
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                TerrainData data = terrain != null ? terrain.terrainData : null;
                if (data == null) continue;
                TreePrototype[] prototypes = data.treePrototypes;
                var autumn = new bool[prototypes.Length];
                for (int p = 0; p < prototypes.Length; p++)
                {
                    GameObject prefab = prototypes[p].prefab;
                    autumn[p] = prefab != null && prefab.name.IndexOf(nameKey, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    if (autumn[p] && !sizes.ContainsKey(prefab)) sizes[prefab] = WorldBuilder.LocalBounds(prefab);
                }

                Vector3 origin = terrain.GetPosition();
                Vector3 size = data.size;
                foreach (TreeInstance tree in data.treeInstances)
                {
                    if (tree.prototypeIndex < 0 || tree.prototypeIndex >= autumn.Length || !autumn[tree.prototypeIndex]) continue;
                    Bounds b = sizes[prototypes[tree.prototypeIndex].prefab];
                    Vector3 p = origin + Vector3.Scale(tree.position, size);
                    finiteTrees.Add(new Vector4(p.x, p.y, p.z, Mathf.Max(1f, b.max.y) * tree.heightScale));
                    finiteCrowns.Add(Mathf.Max(0.8f, Mathf.Max(b.extents.x, b.extents.z)) * tree.widthScale);
                    if (finiteTrees.Count >= MaxFiniteTrees) return;
                }
            }
        }
    }

    /// <summary>
    /// How many flower details grow around a point, read from whichever terrain is under it (the finite world's one terrain,
    /// or an endless near tile): the detail layers whose prototype name contains "Flower", summed over a 5 × 5 cell window
    /// and averaged. Called a few times a minute, so it simply asks the terrain each time (pooled endless terrains change
    /// their detail layers from tile to tile).
    /// </summary>
    internal static class FlowerSampler
    {
        private const int Window = 5;

        /// <summary>Average flower details per detail cell around <paramref name="p"/>; −1 when no terrain with details is there.</summary>
        public static float Density(Vector3 p)
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                TerrainData data = terrain != null ? terrain.terrainData : null;
                if (data == null) continue;
                Vector3 origin = terrain.GetPosition();
                Vector3 size = data.size;
                float u = (p.x - origin.x) / Mathf.Max(1f, size.x), v = (p.z - origin.z) / Mathf.Max(1f, size.z);
                if (u < 0f || u >= 1f || v < 0f || v >= 1f) continue;

                int w = data.detailWidth, h = data.detailHeight;
                DetailPrototype[] prototypes = data.detailPrototypes;
                if (w < Window || h < Window || prototypes.Length == 0) return -1f;

                int x0 = Mathf.Clamp((int)(u * w) - Window / 2, 0, w - Window);
                int y0 = Mathf.Clamp((int)(v * h) - Window / 2, 0, h - Window);
                int sum = 0;
                for (int layer = 0; layer < prototypes.Length; layer++)
                {
                    if (!IsFlower(prototypes[layer])) continue;
                    int[,] cells = data.GetDetailLayer(x0, y0, Window, Window, layer);
                    foreach (int c in cells) sum += c;
                }

                return sum / (float)(Window * Window);
            }

            return -1f;
        }

        private static bool IsFlower(DetailPrototype prototype)
        {
            string name = prototype.prototype != null ? prototype.prototype.name : prototype.prototypeTexture != null ? prototype.prototypeTexture.name : null;
            return name != null && name.IndexOf("Flower", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
