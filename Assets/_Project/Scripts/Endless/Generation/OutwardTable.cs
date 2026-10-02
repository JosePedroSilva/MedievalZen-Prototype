using System;
using System.Threading;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// A table of chunks computed in fixed order outward from chunk 0: chunk c ≥ 1 from chunk c - 1, chunk c ≤ -1 from chunk
    /// c + 1, chunk 0 from nothing. Whatever order chunks are asked for, each is computed exactly once from the same
    /// neighbour, so results never depend on query order. Reads are lock-free (an immutable array snapshot); only growing
    /// the table takes a lock. Thread-safe.
    /// </summary>
    internal sealed class OutwardTable<T> where T : class
    {
        private readonly Func<long, T, T> compute;
        private readonly object gate = new object();
        private T[] positive = new T[0];   // chunks 0, 1, 2, ...
        private T[] negative = new T[0];   // chunks -1, -2, ...

        /// <summary>Creates a table whose chunk c is <paramref name="compute"/>(c, neighbour towards 0, or null for chunk 0).</summary>
        public OutwardTable(Func<long, T, T> compute)
        {
            this.compute = compute ?? throw new ArgumentNullException(nameof(compute));
        }

        /// <summary>Chunk <paramref name="chunk"/>, computing it (and every chunk between it and 0) first if needed.</summary>
        public T Get(long chunk)
        {
            if (chunk >= 0)
            {
                T[] arr = Volatile.Read(ref positive);
                if (chunk < arr.Length) return arr[chunk];
            }
            else
            {
                T[] arr = Volatile.Read(ref negative);
                long k = -chunk - 1;
                if (k < arr.Length) return arr[k];
            }

            lock (gate)
            {
                if (chunk >= 0)
                {
                    T[] arr = positive;
                    if (chunk >= arr.Length)
                    {
                        var exact = new T[chunk + 1];
                        Array.Copy(arr, exact, arr.Length);
                        for (long c = arr.Length; c <= chunk; c++) exact[c] = compute(c, c == 0 ? null : exact[c - 1]);
                        Volatile.Write(ref positive, exact);
                        arr = exact;
                    }

                    return arr[chunk];
                }
                else
                {
                    T zero = chunkZeroLocked();
                    T[] arr = negative;
                    long k = -chunk - 1;
                    if (k >= arr.Length)
                    {
                        var exact = new T[k + 1];
                        Array.Copy(arr, exact, arr.Length);
                        for (long j = arr.Length; j <= k; j++) exact[j] = compute(-j - 1, j == 0 ? zero : exact[j - 1]);
                        Volatile.Write(ref negative, exact);
                        arr = exact;
                    }

                    return arr[k];
                }
            }
        }

        private T chunkZeroLocked()
        {
            if (positive.Length > 0) return positive[0];
            T zero = compute(0, null);
            Volatile.Write(ref positive, new[] { zero });
            return zero;
        }
    }
}
