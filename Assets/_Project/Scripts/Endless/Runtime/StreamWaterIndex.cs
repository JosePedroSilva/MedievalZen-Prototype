using System;
using System.Collections.Generic;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The stream ribbons of the near tiles in the scene, by tile (main thread only): lets the world surface answer "is there
    /// water here" for streams from exactly what is drawn, cheaply. Filled by the tile builder when a tile shows, emptied when it
    /// hides.
    /// </summary>
    internal static class StreamWaterIndex
    {
        private static readonly Dictionary<TileCoord, TileBlueprint> Tiles = new Dictionary<TileCoord, TileBlueprint>();

        /// <summary>Registers the streams of a tile that is now visible.</summary>
        public static void Register(TileCoord coord, TileBlueprint bp) => Tiles[coord] = bp;

        /// <summary>Forgets a tile's streams (only if <paramref name="bp"/> is still the registered blueprint).</summary>
        public static void Unregister(TileCoord coord, TileBlueprint bp)
        {
            if (Tiles.TryGetValue(coord, out TileBlueprint have) && ReferenceEquals(have, bp)) Tiles.Remove(coord);
        }

        /// <summary>Forgets everything (scene end, new journey).</summary>
        public static void Clear() => Tiles.Clear();

        /// <summary>True when the tile at <paramref name="coord"/> is registered (its streams are known exactly).</summary>
        public static bool Knows(TileCoord coord) => Tiles.ContainsKey(coord);

        /// <summary>
        /// The surface of a stream at tile-local (x, z) of tile <paramref name="coord"/> where a ribbon covers it and the ground
        /// (<paramref name="ground"/>) lies below the water, else -1.
        /// </summary>
        public static float Surface(TileCoord coord, float x, float z, float ground)
        {
            if (!Tiles.TryGetValue(coord, out TileBlueprint bp) || bp.Streams == null) return -1f;
            foreach (StreamRibbon r in bp.Streams)
            {
                StreamRibbonVertex[] v = r.Vertices;
                for (int i = 0; i + 1 < v.Length; i++)
                {
                    float ex = v[i + 1].X - v[i].X, ez = v[i + 1].Z - v[i].Z;
                    float len2 = ex * ex + ez * ez;
                    float t = len2 > 1e-6f ? ((x - v[i].X) * ex + (z - v[i].Z) * ez) / len2 : 0f;
                    t = t < 0f ? 0f : t > 1f ? 1f : t;
                    float dx = x - (v[i].X + ex * t), dz = z - (v[i].Z + ez * t);
                    float half = v[i].HalfWidth + (v[i + 1].HalfWidth - v[i].HalfWidth) * t - 0.4f;
                    if (dx * dx + dz * dz > half * half) continue;
                    float surface = v[i].Surface + (v[i + 1].Surface - v[i].Surface) * t;
                    if (ground < surface) return surface;
                }
            }

            return -1f;
        }
    }
}
