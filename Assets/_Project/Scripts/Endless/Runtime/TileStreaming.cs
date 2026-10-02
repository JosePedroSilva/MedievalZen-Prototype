using System;
using System.Collections.Generic;

namespace MedievalZen.Endless
{
    /// <summary>Streaming distances (a plain copy of the <see cref="EndlessSettings"/> streaming block).</summary>
    public struct StreamingRanges
    {
        /// <summary>Tile edge length in metres.</summary>
        public float TileSize;

        /// <summary>Tiles whose nearest point is within this distance are wanted at <see cref="TileLod.Near"/>.</summary>
        public float Near;

        /// <summary>Tiles out to this distance are wanted at <see cref="TileLod.Far"/>.</summary>
        public float Far;

        /// <summary>Hysteresis: a tile keeps its level of detail this much beyond the distance that asked for it.</summary>
        public float Margin;

        /// <summary>0..1: how much closer a tile ahead (along the direction of travel) counts when ordering requests.</summary>
        public float AheadBias;

        /// <summary>The ranges of <paramref name="settings"/>.</summary>
        public static StreamingRanges From(EndlessSettings settings) => new StreamingRanges
        {
            TileSize = settings.tileSize,
            Near = settings.nearDistance,
            Far = Math.Max(settings.farDistance, settings.nearDistance),
            Margin = settings.releaseMargin,
            AheadBias = 0.35f,
        };
    }

    /// <summary>A tile the streamer wants, with its order of urgency (lower = sooner).</summary>
    public struct TileWant
    {
        /// <summary>Which tile.</summary>
        public TileCoord Coord;

        /// <summary>Level of detail wanted.</summary>
        public TileLod Lod;

        /// <summary>Distance from the traveller to the nearest point of the tile, metres.</summary>
        public float Distance;

        /// <summary>Sort key: near tiles before far ones, each by distance weighted toward the direction of travel.</summary>
        public float Priority;
    }

    /// <summary>
    /// Which tiles should exist around the traveller and at what level of detail, with hysteresis so a traveller standing on
    /// a boundary (or turning round) never makes tiles flip back and forth. Pure (unit tested).
    /// </summary>
    public static class TileStreaming
    {
        /// <summary>Priority offset that puts every far tile after every near tile.</summary>
        public const float FarPriorityOffset = 100000f;

        /// <summary>Horizontal distance from absolute (px, pz) to the nearest point of tile <paramref name="c"/> (0 inside it).</summary>
        public static float DistanceToTile(double px, double pz, TileCoord c, float tileSize)
        {
            double x0 = c.OriginX(tileSize), z0 = c.OriginZ(tileSize);
            double dx = px < x0 ? x0 - px : px > x0 + tileSize ? px - (x0 + tileSize) : 0.0;
            double dz = pz < z0 ? z0 - pz : pz > z0 + tileSize ? pz - (z0 + tileSize) : 0.0;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// The level of detail a tile at <paramref name="distance"/> should have given what it has (or is being built at)
        /// now: a near tile stays near until it is <see cref="StreamingRanges.Margin"/> beyond the near distance, a far tile
        /// stays until it is the margin beyond the far distance; a missing tile is only requested inside the plain distances.
        /// Null = release (or do not request).
        /// </summary>
        public static TileLod? Desired(TileLod? current, float distance, in StreamingRanges r)
        {
            if (distance <= r.Near) return TileLod.Near;
            if (current == TileLod.Near && distance <= r.Near + r.Margin) return TileLod.Near;
            if (distance <= r.Far) return TileLod.Far;
            if (current.HasValue && distance <= r.Far + r.Margin) return TileLod.Far;
            return null;
        }

        /// <summary>
        /// Request order: near tiles first, then far ones; within each by distance scaled down for tiles ahead of the
        /// traveller (direction (<paramref name="dirX"/>, <paramref name="dirZ"/>), unit or zero) and up for tiles behind.
        /// </summary>
        public static float Priority(TileLod lod, float distance, double px, double pz, TileCoord c, float tileSize, float dirX, float dirZ, float aheadBias)
        {
            double cx = c.OriginX(tileSize) + tileSize * 0.5 - px;
            double cz = c.OriginZ(tileSize) + tileSize * 0.5 - pz;
            double length = Math.Sqrt(cx * cx + cz * cz);
            float cos = length > 1e-3 ? (float)((cx * dirX + cz * dirZ) / length) : 0f;
            float weighted = distance * (1f - aheadBias * cos);
            return lod == TileLod.Near ? weighted : weighted + FarPriorityOffset;
        }

        /// <summary>
        /// Fills <paramref name="wants"/> with every tile that should exist around absolute (px, pz), sorted by priority.
        /// <paramref name="current"/> tells the level of detail a tile has or is being built at (null = none) so hysteresis
        /// applies; tiles it knows about that are not in the result should be released.
        /// </summary>
        public static void Plan(double px, double pz, float dirX, float dirZ, in StreamingRanges r, Func<TileCoord, TileLod?> current, List<TileWant> wants)
        {
            wants.Clear();
            float reach = r.Far + r.Margin;
            float ts = r.TileSize;
            int x0 = (int)Math.Floor((px - reach) / ts), x1 = (int)Math.Floor((px + reach) / ts);
            int z0 = (int)Math.Floor((pz - reach) / ts), z1 = (int)Math.Floor((pz + reach) / ts);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var c = new TileCoord(x, z);
                    float d = DistanceToTile(px, pz, c, ts);
                    if (d > reach) continue;
                    TileLod? lod = Desired(current?.Invoke(c), d, r);
                    if (!lod.HasValue) continue;
                    wants.Add(new TileWant { Coord = c, Lod = lod.Value, Distance = d, Priority = Priority(lod.Value, d, px, pz, c, ts, dirX, dirZ, r.AheadBias) });
                }
            }

            wants.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority)
                : a.Coord.Z != b.Coord.Z ? a.Coord.Z.CompareTo(b.Coord.Z) : a.Coord.X.CompareTo(b.Coord.X));
        }
    }
}
