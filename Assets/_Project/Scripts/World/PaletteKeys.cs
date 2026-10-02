using System;
using System.Collections.Generic;

namespace MedievalZen.World
{
    /// <summary>
    /// What the pure generation stages need to know about the prefab palette: how many variants exist per family
    /// (0 = family unavailable, skip it), the footprint of line pieces and the measured shape (<see cref="PropMetrics"/>) of
    /// every rock, cliff and mound variant. Produced by <see cref="WorldPalette.GetKeys"/>; <see cref="Default"/> mirrors
    /// <see cref="SyntyCatalog"/> with the offline <see cref="PropMetricsTable"/>.
    /// </summary>
    public sealed class PaletteKeys
    {
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> detailCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, PropMetrics[]> metrics = new Dictionary<string, PropMetrics[]>();

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
                foreach (var (family, paths) in SyntyCatalog.Families)
                {
                    keys.SetCount(family, paths.Length);
                    var measured = new PropMetrics[paths.Length];
                    bool any = false;
                    for (int i = 0; i < paths.Length; i++) any |= PropMetricsTable.TryGetForPath(paths[i], out measured[i]);
                    if (any) keys.SetMetrics(family, measured);
                }

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

        /// <summary>Sets the measured shape of each variant of <paramref name="family"/> (parallel to the variant indices).</summary>
        public void SetMetrics(string family, PropMetrics[] variants)
        {
            if (family == null) return;
            if (variants == null) metrics.Remove(family);
            else metrics[family] = variants;
        }

        /// <summary>
        /// Measured shape of <paramref name="variant"/> (wrapped like the palette does) of <paramref name="family"/>; false when
        /// the family has no metrics or this variant was not measured.
        /// </summary>
        public bool TryGetMetrics(string family, int variant, out PropMetrics result)
        {
            result = default;
            if (family == null || !metrics.TryGetValue(family, out PropMetrics[] list) || list.Length == 0) return false;
            result = list[((variant % list.Length) + list.Length) % list.Length];
            return result.IsValid;
        }

        /// <summary>True when every variant of <paramref name="family"/> has measured metrics.</summary>
        public bool HasMetrics(string family)
        {
            if (family == null || !metrics.TryGetValue(family, out PropMetrics[] list) || list.Length == 0 || list.Length != Count(family)) return false;
            foreach (PropMetrics m in list)
            {
                if (!m.IsValid) return false;
            }

            return true;
        }

        /// <summary>Random variant index for <paramref name="family"/> (0 when the family has one or no prefabs).</summary>
        public int PickVariant(string family, Random rng)
        {
            int count = Count(family);
            return count > 1 ? rng.Next(count) : 0;
        }
    }
}
