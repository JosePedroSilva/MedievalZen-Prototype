using System;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Tiny software rasteriser for the parchment map: a square colour buffer (row 0 = bottom, like
    /// <see cref="Texture2D.SetPixels(Color[])"/>) plus anti-aliased coverage masks that are drawn with max-blending and then
    /// composited in one go, so overlapping strokes (polyline joints, touching glyphs) never double-darken.
    /// Coordinates are in pixels, x right, y up.
    /// </summary>
    public sealed class MapRaster
    {
        /// <summary>Creates a raster of <paramref name="size"/> × <paramref name="size"/> pixels filled with <paramref name="fill"/>.</summary>
        public MapRaster(int size, Color fill)
        {
            if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
            Size = size;
            Pixels = new Color[size * size];
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = fill;
        }

        /// <summary>Pixels per side.</summary>
        public int Size { get; }

        /// <summary>The colour buffer, <c>[y * Size + x]</c>.</summary>
        public Color[] Pixels { get; }

        /// <summary>A new empty coverage mask.</summary>
        public float[] NewMask() => new float[Size * Size];

        /// <summary>Anti-aliased capsule (thick line) from <paramref name="a"/> to <paramref name="b"/> into <paramref name="mask"/>.</summary>
        public void MaskSegment(float[] mask, Vector2 a, Vector2 b, float radius)
        {
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.x, b.x) - radius - 1f));
            int x1 = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(a.x, b.x) + radius + 1f));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.y, b.y) - radius - 1f));
            int y1 = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(a.y, b.y) + radius + 1f));
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = lengthSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr) : 0f;
                    float d = (a + ab * t - p).magnitude;
                    Put(mask, x, y, Mathf.Clamp01(radius - d + 0.5f));
                }
            }
        }

        /// <summary>Anti-aliased filled disc.</summary>
        public void MaskDisc(float[] mask, Vector2 centre, float radius)
        {
            MaskSegment(mask, centre, centre, radius);
        }

        /// <summary>Anti-aliased ring of outer radius <paramref name="radius"/> and line <paramref name="width"/>.</summary>
        public void MaskRing(float[] mask, Vector2 centre, float radius, float width)
        {
            int x0 = Math.Max(0, (int)Math.Floor(centre.x - radius - 1f));
            int x1 = Math.Min(Size - 1, (int)Math.Ceiling(centre.x + radius + 1f));
            int y0 = Math.Max(0, (int)Math.Floor(centre.y - radius - 1f));
            int y1 = Math.Min(Size - 1, (int)Math.Ceiling(centre.y + radius + 1f));
            float mid = radius - width * 0.5f;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float d = new Vector2(x + 0.5f - centre.x, y + 0.5f - centre.y).magnitude;
                    Put(mask, x, y, Mathf.Clamp01(width * 0.5f - Mathf.Abs(d - mid) + 0.5f));
                }
            }
        }

        /// <summary>Anti-aliased convex polygon (vertices in counter-clockwise order).</summary>
        public void MaskConvex(float[] mask, params Vector2[] vertices)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector2 v in vertices)
            {
                minX = Math.Min(minX, v.x);
                minY = Math.Min(minY, v.y);
                maxX = Math.Max(maxX, v.x);
                maxY = Math.Max(maxY, v.y);
            }

            int x0 = Math.Max(0, (int)Math.Floor(minX - 1f));
            int x1 = Math.Min(Size - 1, (int)Math.Ceiling(maxX + 1f));
            int y0 = Math.Max(0, (int)Math.Floor(minY - 1f));
            int y1 = Math.Min(Size - 1, (int)Math.Ceiling(maxY + 1f));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float inside = float.MaxValue;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector2 a = vertices[i];
                        Vector2 b = vertices[(i + 1) % vertices.Length];
                        Vector2 edge = b - a;
                        float len = edge.magnitude;
                        if (len < 1e-6f) continue;
                        // Signed distance to the edge line, positive inside for CCW polygons.
                        float signed = (edge.x * (p.y - a.y) - edge.y * (p.x - a.x)) / len;
                        inside = Math.Min(inside, signed);
                    }

                    Put(mask, x, y, Mathf.Clamp01(inside + 0.5f));
                }
            }
        }

        /// <summary>Blends <paramref name="ink"/> over the pixels by <paramref name="mask"/> × <paramref name="opacity"/>, then clears the mask.</summary>
        public void Composite(float[] mask, Color ink, float opacity)
        {
            for (int i = 0; i < mask.Length; i++)
            {
                float a = mask[i];
                if (a <= 0f) continue;
                a *= opacity;
                Color c = Pixels[i];
                Pixels[i] = new Color(c.r + (ink.r - c.r) * a, c.g + (ink.g - c.g) * a, c.b + (ink.b - c.b) * a, 1f);
                mask[i] = 0f;
            }
        }

        /// <summary>Multiplies the pixels by <paramref name="tint"/> weighted by <paramref name="mask"/> × <paramref name="opacity"/>, then clears the mask.</summary>
        public void CompositeMultiply(float[] mask, Color tint, float opacity)
        {
            for (int i = 0; i < mask.Length; i++)
            {
                float a = mask[i];
                if (a <= 0f) continue;
                a *= opacity;
                Color c = Pixels[i];
                Pixels[i] = new Color(c.r * (1f + (tint.r - 1f) * a), c.g * (1f + (tint.g - 1f) * a), c.b * (1f + (tint.b - 1f) * a), 1f);
                mask[i] = 0f;
            }
        }

        private void Put(float[] mask, int x, int y, float coverage)
        {
            if (coverage <= 0f) return;
            int i = y * Size + x;
            if (coverage > mask[i]) mask[i] = coverage;
        }
    }
}
