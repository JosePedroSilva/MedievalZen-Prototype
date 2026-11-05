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

        /// <summary>
        /// Distant tiles within this distance are wanted at <see cref="TileLod.Mid"/> (the finer distant mesh); 0 or anything not
        /// beyond <see cref="Near"/> = no Mid ring.
        /// </summary>
        public float Mid;

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
            Mid = Math.Min(Math.Max(settings.midDistance, settings.nearDistance), Math.Max(settings.farDistance, settings.nearDistance)),
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

        /// <summary>Sort key: near tiles, then mid, then far ones, each by distance weighted toward the direction of travel.</summary>
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

        /// <summary>Priority offset that puts every mid tile after every near tile and before every far tile.</summary>
        public const float MidPriorityOffset = 50000f;

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
        /// now: a near tile stays near until it is <see cref="StreamingRanges.Margin"/> beyond the near distance, a mid tile stays
        /// mid until it is the margin beyond the mid distance, a far tile stays until it is the margin beyond the far distance; a
        /// missing tile is only requested inside the plain distances. Null = release (or do not request).
        /// </summary>
        public static TileLod? Desired(TileLod? current, float distance, in StreamingRanges r)
        {
            if (distance <= r.Near) return TileLod.Near;
            if (current == TileLod.Near && distance <= r.Near + r.Margin) return TileLod.Near;
            bool mid = r.Mid > r.Near;
            if (mid && distance <= r.Mid) return TileLod.Mid;
            if (mid && current == TileLod.Mid && distance <= r.Mid + r.Margin) return TileLod.Mid;
            if (distance <= r.Far) return TileLod.Far;
            if (current.HasValue && distance <= r.Far + r.Margin) return TileLod.Far;
            return null;
        }

        /// <summary>
        /// Request order: near tiles first, then mid, then far ones; within each by distance scaled down for tiles ahead of the
        /// traveller (direction (<paramref name="dirX"/>, <paramref name="dirZ"/>), unit or zero) and up for tiles behind.
        /// </summary>
        public static float Priority(TileLod lod, float distance, double px, double pz, TileCoord c, float tileSize, float dirX, float dirZ, float aheadBias)
        {
            double cx = c.OriginX(tileSize) + tileSize * 0.5 - px;
            double cz = c.OriginZ(tileSize) + tileSize * 0.5 - pz;
            double length = Math.Sqrt(cx * cx + cz * cz);
            float cos = length > 1e-3 ? (float)((cx * dirX + cz * dirZ) / length) : 0f;
            float weighted = distance * (1f - aheadBias * cos);
            return lod == TileLod.Near ? weighted : lod == TileLod.Mid ? weighted + MidPriorityOffset : weighted + FarPriorityOffset;
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

    /// <summary>
    /// Second performance pass (2026-10-06): how the main-thread apply of a near tile is cut into slices (pure, unit tested). The worst
    /// slices of the 2026-10-05 tour were "show props" (17 ms: switching on a group of up to 32 props that held a 20-35k-triangle
    /// building with its colliders) and "alphamap band" (10 ms: 64 rows of an 11-layer splat map, three control textures).
    /// </summary>
    public static class ApplySlicing
    {
        /// <summary>Rows per alphamap band for a terrain of up to four layers (one control texture).</summary>
        public const int BaseSplatRows = 64;

        /// <summary>Fewest rows per band.</summary>
        public const int MinSplatRows = 16;

        /// <summary>Most triangles a group of props switched on in one slice may hold (a heavier prop gets a group of its own).</summary>
        public const long GroupTriangleBudget = 12000;

        /// <summary>
        /// Rows per alphamap band for <paramref name="layers"/> terrain layers: the upload grows with the control textures (one per
        /// four layers), so the band shrinks with them — 64 rows to 4 layers, 32 to 8, 16 for 9-12 — in multiples of 8, at least 16.
        /// </summary>
        public static int SplatRows(int layers)
        {
            int textures = Math.Max(1, (layers + 3) / 4);
            int rows = BaseSplatRows / (textures >= 3 ? 4 : textures);
            return Math.Max(MinSplatRows, rows / 8 * 8);
        }

        /// <summary>
        /// True when a prop of <paramref name="triangles"/> must start a new group: the current group has <paramref name="maxProps"/>
        /// props, or adding it would take the group over <paramref name="budget"/> triangles (an empty group always takes it, so a
        /// heavy building is switched on alone in its own slice).
        /// </summary>
        public static bool StartsNewGroup(int inGroup, long groupTriangles, long triangles, int maxProps, long budget = GroupTriangleBudget)
        {
            if (inGroup <= 0) return false;
            return inGroup >= maxProps || groupTriangles + Math.Max(0L, triangles) > budget;
        }
    }

    /// <summary>
    /// Third performance pass (2026-10-06): the rules for warming the heavy props (Fantasy Kingdom building presets, landmarks, the
    /// bridge: 5-35 k triangles each) before the road needs them. Behind a hold's black screen one instance of each heavy prefab is
    /// switched on in front of the camera for one frame (its renderers register and draw once, its collider cooks) and then parked
    /// in the prop pool; while walking, a small stock is kept (one idle instance per heavy prefab, refilled one per frame away from
    /// places and never while tiles stream). Pure.
    /// </summary>
    public static class PropWarmup
    {
        /// <summary>A prefab with at least this many triangles (all LODs) is heavy: warmed, stocked, its colliders switched on a slice later.</summary>
        public const long HeavyTriangles = 5000;

        /// <summary>Stock is not refilled within this road distance (metres) of a settlement or landmark: the road is about to need it.</summary>
        public const double StockClearance = 300.0;

        /// <summary>Most a hold is extended (seconds after the ground is ready) to finish the warm-up.</summary>
        public const float HoldExtraSeconds = 1f;

        /// <summary>Heavy prefabs touched per frame behind a black screen.</summary>
        public const int WarmPerFrame = 3;

        /// <summary>Main-thread milliseconds per frame the warm-up may take behind a black screen (it stops after the item that crosses it).</summary>
        public const float WarmBudgetMs = 8f;

        /// <summary>
        /// Families whose heavy prefabs are warmed: GameObject props that appear only at places (buildings, landmarks, bridges, mills,
        /// stalls). Trees, instanced plants, the woods' extras and boundary pieces are drawn instanced; rocks and cliffs are on nearly
        /// every tile, so the start area warms them anyway.
        /// </summary>
        public static bool IsCandidateFamily(string family)
        {
            if (string.IsNullOrEmpty(family)) return false;
            if (MedievalZen.World.PropRole.IsTree(family) || MedievalZen.World.PropRole.IsInstancedPlant(family)) return false;
            if (MedievalZen.World.PropRole.IsForestExtra(family) || MedievalZen.World.PropRole.IsBoundaryPiece(family)) return false;
            if (MedievalZen.World.PropRole.IsRock(family) || MedievalZen.World.PropRole.IsCliff(family) || MedievalZen.World.PropRole.IsPassRock(family)) return false;
            return family != MedievalZen.World.PropRole.HorizonHill && family != MedievalZen.World.PropRole.HorizonMountain;
        }

        /// <summary>True for a prefab heavy enough to warm (see <see cref="HeavyTriangles"/>).</summary>
        public static bool IsHeavy(long triangles) => triangles >= HeavyTriangles;

        /// <summary>
        /// True when one stock instance may be made this frame: some heavy prefab has no idle instance, no tile is building or
        /// releasing, and the traveller is more than <see cref="StockClearance"/> from the nearest settlement or landmark
        /// (NaN or infinity: none known nearby).
        /// </summary>
        public static bool ShouldStock(bool anyPoolEmpty, bool streamingBusy, double nearestPlaceMetres)
        {
            if (!anyPoolEmpty || streamingBusy) return false;
            return double.IsNaN(nearestPlaceMetres) || nearestPlaceMetres > StockClearance;
        }

        /// <summary>
        /// True while a hold should wait for the warm-up: it is still pending, the screen is black (it only touches props then) and
        /// the ground has been ready for less than <see cref="HoldExtraSeconds"/>.
        /// </summary>
        public static bool HoldsForWarmup(bool warmPending, bool black, float readyForSeconds)
        {
            return warmPending && black && readyForSeconds < HoldExtraSeconds;
        }
    }
}