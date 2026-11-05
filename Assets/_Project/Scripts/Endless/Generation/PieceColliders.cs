using System;

namespace MedievalZen.Endless.Generation
{
    /// <summary>The collider an instanced piece gets near the traveller (<see cref="PieceColliders.Kind"/>).</summary>
    public enum PieceCollider
    {
        /// <summary>None: overhead or decorative (hoardings, corbels, roofs, balconies), ivy and gate leaves.</summary>
        None,
        /// <summary>One box from the piece's bounds: walls, towers, pillars, battlements, field walls, hedges and fences.</summary>
        Box,
        /// <summary>The prefab's own colliders (its mesh and box colliders): openings one walks through or on (archways, gates, doors, stairs, bridges).</summary>
        Own,
    }

    /// <summary>
    /// Which instanced boundary and castle pieces stand solid, and how (2026-10-09, after the user walked into Durncaster's castle):
    /// the renderer (<c>EndlessTreeRenderer</c>) gives a piece colliders near the traveller by <see cref="Kind"/>. Walls, towers,
    /// pillars and battlements get one box from their bounds; anything one walks through or on (archways, gates, doors, stairs,
    /// bridges) keeps the prefab's own colliders, since a box from its bounds would close the opening; anything overhead (hoardings,
    /// corbels, roofs, balconies) gets none. Pure: decided by the family and the prefab's name.
    /// </summary>
    public static class PieceColliders
    {
        /// <summary>Name fragments that keep a box whatever else the name holds (a bridge's railing and pillars stand solid).</summary>
        public static readonly string[] SolidNames = { "Railing", "Bridge_Pillars" };

        /// <summary>
        /// Name fragments of openings that keep the prefab's own colliders (checked after <see cref="SolidNames"/>); a castle floor with a
        /// stair gap or a hatch, and a round tower's quarter floors, too (2026-10-09: a box from their bounds would close the gap or reach
        /// outside the tower). Plain floors, walkways and their railings keep one thin box from their bounds, which one walks on.
        /// </summary>
        public static readonly string[] OwnNames = { "Archway", "Gate", "Door", "Stairs", "Bridge", "Floor_Gap", "Floor_Wood_Round", "Hatch" };

        /// <summary>Name fragments of overhead pieces with no collider (checked after <see cref="OwnNames"/>).</summary>
        public static readonly string[] OpenNames = { "Hoarding", "Corbels", "Roof", "Balcony", "Cross", "Beam_Curved" };

        /// <summary>The collider of a piece of <paramref name="family"/> drawn from prefab <paramref name="prefabName"/>.</summary>
        public static PieceCollider Kind(string family, string prefabName)
        {
            if (!MedievalZen.World.PropRole.IsBoundaryPiece(family) || family == MedievalZen.World.PropRole.FenceGate) return PieceCollider.None;   // ivy, gate leaves
            if (!MedievalZen.World.PropRole.IsCityPiece(family) || string.IsNullOrEmpty(prefabName)) return PieceCollider.Box;   // field walls, hedges, fences
            if (Has(prefabName, SolidNames)) return PieceCollider.Box;
            if (Has(prefabName, OwnNames)) return PieceCollider.Own;
            if (Has(prefabName, OpenNames)) return PieceCollider.None;
            return PieceCollider.Box;
        }

        /// <summary>Whether the piece gets one box from its bounds (<see cref="PieceCollider.Box"/>).</summary>
        public static bool HasBox(string family, string prefabName) => Kind(family, prefabName) == PieceCollider.Box;

        private static bool Has(string name, string[] fragments)
        {
            foreach (string s in fragments)
            {
                if (name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }
    }
}
