using System;
using System.Collections.Generic;

namespace MedievalZen.World
{
    /// <summary>
    /// What the pure generation stages need to know about the prefab palette: how many variants exist per family
    /// (0 = family unavailable, skip it) and the footprint of line pieces. Produced by
    /// <see cref="WorldPalette.GetKeys"/>; <see cref="Default"/> mirrors <see cref="SyntyCatalog"/>.
    /// </summary>
    public sealed class PaletteKeys
    {
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> detailCounts = new Dictionary<string, int>();

        /// <summary>Length in metres of one straight fence piece.</summary>
        public float fenceLength = 3f;

        /// <summary>Length in metres of one long stone-wall piece.</summary>
        public float wallLength = 4f;

        /// <summary>Keys matching the default Synty catalog (used by tests and when no palette is given).</summary>
        public static PaletteKeys Default
        {
            get
            {
                var keys = new PaletteKeys();
                foreach (var (family, paths) in SyntyCatalog.Families) keys.SetCount(family, paths.Length);
                foreach (var (family, paths) in SyntyCatalog.Details) keys.SetDetailCount(family, paths.Length);
                return keys;
            }
        }

        /// <summary>Number of prefab variants for <paramref name="family"/>.</summary>
        public int Count(string family) => family != null && counts.TryGetValue(family, out int c) ? c : 0;

        /// <summary>True for families realised as terrain tree instances rather than GameObjects.</summary>
        public static bool IsTree(string family) => PropRole.IsTree(family);

        /// <summary>True when at least one prefab exists for <paramref name="family"/>.</summary>
        public bool Has(string family) => Count(family) > 0;

        /// <summary>Sets the variant count for <paramref name="family"/>.</summary>
        public void SetCount(string family, int count) => counts[family] = Math.Max(0, count);

        /// <summary>Number of detail prototypes in detail family <paramref name="family"/>.</summary>
        public int DetailCount(string family) => family != null && detailCounts.TryGetValue(family, out int c) ? c : 0;

        /// <summary>Sets the number of detail prototypes for <paramref name="family"/>.</summary>
        public void SetDetailCount(string family, int count) => detailCounts[family] = Math.Max(0, count);

        /// <summary>Random variant index for <paramref name="family"/> (0 when the family has one or no prefabs).</summary>
        public int PickVariant(string family, Random rng)
        {
            int count = Count(family);
            return count > 1 ? rng.Next(count) : 0;
        }
    }
}
