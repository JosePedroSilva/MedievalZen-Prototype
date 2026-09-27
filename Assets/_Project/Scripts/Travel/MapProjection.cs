using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Maps world X/Z onto the parchment's UV square (0..1, v up = north) and back. The world square
    /// [origin, origin + size] fills the inner part of the sheet; <see cref="Margin"/> is left blank on every side for the
    /// paper border. Pure maths, no Unity objects.
    /// </summary>
    public readonly struct MapProjection
    {
        /// <summary>Creates a projection of the square starting at (<paramref name="originX"/>, <paramref name="originZ"/>).</summary>
        public MapProjection(float originX, float originZ, float size, float margin)
        {
            OriginX = originX;
            OriginZ = originZ;
            Size = Mathf.Max(1e-3f, size);
            Margin = Mathf.Clamp(margin, 0f, 0.45f);
        }

        /// <summary>World X of the map's west edge.</summary>
        public float OriginX { get; }

        /// <summary>World Z of the map's south edge.</summary>
        public float OriginZ { get; }

        /// <summary>Side length in metres of the mapped world square.</summary>
        public float Size { get; }

        /// <summary>Blank border on each side of the sheet, as a fraction of the sheet (0..0.45).</summary>
        public float Margin { get; }

        /// <summary>Fraction of the sheet covered by the world square.</summary>
        public float Inner => 1f - 2f * Margin;

        /// <summary>Metres of world per unit of UV.</summary>
        public float MetresPerUv => Size / Inner;

        /// <summary>UV (0..1 on the sheet) of a world position (height ignored). Outside the world gives UVs in the margin or beyond.</summary>
        public Vector2 WorldToMapUV(Vector3 world)
        {
            return new Vector2(
                Margin + (world.x - OriginX) / Size * Inner,
                Margin + (world.z - OriginZ) / Size * Inner);
        }

        /// <summary>World position (y = <paramref name="height"/>) under a sheet UV.</summary>
        public Vector3 MapUVToWorld(Vector2 uv, float height = 0f)
        {
            return new Vector3(
                OriginX + (uv.x - Margin) / Inner * Size,
                height,
                OriginZ + (uv.y - Margin) / Inner * Size);
        }

        /// <summary>True if <paramref name="uv"/> lies on the mapped world (not in the border).</summary>
        public bool IsOnWorld(Vector2 uv)
        {
            return uv.x >= Margin && uv.x <= 1f - Margin && uv.y >= Margin && uv.y <= 1f - Margin;
        }
    }
}
