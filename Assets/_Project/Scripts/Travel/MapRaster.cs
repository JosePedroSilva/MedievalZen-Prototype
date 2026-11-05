using System;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Tiny software rasteriser for the parchment maps: a colour buffer (row 0 = bottom, like
    /// <see cref="Texture2D.SetPixels(Color[])"/>) plus anti-aliased coverage masks that are drawn with max-blending and then
    /// composited in one go, so overlapping strokes (polyline joints, touching glyphs) never double-darken.
    /// Coordinates are in pixels, x right, y up. Square for the finite world's sheet, rectangular for the endless road's
    /// scroll segments. Pure managed code (no native Unity calls): safe on worker threads.
    /// </summary>
    public sealed class MapRaster
    {
        /// <summary>Creates a raster of <paramref name="size"/> × <paramref name="size"/> pixels filled with <paramref name="fill"/>.</summary>
        public MapRaster(int size, Color fill) : this(size, size, fill, null)
        {
        }

        /// <summary>
        /// Creates a raster of <paramref name="width"/> × <paramref name="height"/> pixels filled with <paramref name="fill"/>,
        /// reusing <paramref name="buffer"/> when it is large enough (it is then overwritten).
        /// </summary>
        public MapRaster(int width, int height, Color fill, Color[] buffer = null)
        {
            if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 2) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
            int count = width * height;
            Pixels = buffer != null && buffer.Length >= count ? buffer : new Color[count];
            for (int i = 0; i < count; i++) Pixels[i] = fill;
        }

        /// <summary>Pixels per side of a square raster (its width).</summary>
        public int Size => Width;

        /// <summary>Pixels per row.</summary>
        public int Width { get; }

        /// <summary>Rows.</summary>
        public int Height { get; }

        /// <summary>The colour buffer, <c>[y * Width + x]</c> (may be longer than Width × Height when a buffer was reused).</summary>
        public Color[] Pixels { get; }

        /// <summary>A new empty coverage mask.</summary>
        public float[] NewMask() => new float[Width * Height];

        /// <summary>Anti-aliased capsule (thick line) from <paramref name="a"/> to <paramref name="b"/> into <paramref name="mask"/>.</summary>
        public void MaskSegment(float[] mask, Vector2 a, Vector2 b, float radius)
        {
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.x, b.x) - radius - 1f));
            int x1 = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(a.x, b.x) + radius + 1f));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.y, b.y) - radius - 1f));
            int y1 = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(a.y, b.y) + radius + 1f));
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
            int x1 = Math.Min(Width - 1, (int)Math.Ceiling(centre.x + radius + 1f));
            int y0 = Math.Max(0, (int)Math.Floor(centre.y - radius - 1f));
            int y1 = Math.Min(Height - 1, (int)Math.Ceiling(centre.y + radius + 1f));
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
            int x1 = Math.Min(Width - 1, (int)Math.Ceiling(maxX + 1f));
            int y0 = Math.Max(0, (int)Math.Floor(minY - 1f));
            int y1 = Math.Min(Height - 1, (int)Math.Ceiling(maxY + 1f));
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
            int count = Width * Height;
            for (int i = 0; i < count; i++)
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
            int count = Width * Height;
            for (int i = 0; i < count; i++)
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
            int i = y * Width + x;
            if (coverage > mask[i]) mask[i] = coverage;
        }
    }

    /// <summary>
    /// The parchment maps' shared hand-drawn vocabulary: ink colours, the relief and water shading of one pixel, and the map
    /// symbols (settlements, tree marks, the pass sign, the viewpoint and peak marks). Used by the finite world's
    /// <see cref="MapTextureRenderer"/> and the endless road's scroll so both read as the same cartographer's hand. Pure.
    /// </summary>
    public static class MapInk
    {
        /// <summary>Parchment colour.</summary>
        public static readonly Color Paper = new Color(0.9f, 0.82f, 0.64f);

        /// <summary>Dark brown ink (outlines, symbols, contours).</summary>
        public static readonly Color InkBrown = new Color(0.24f, 0.15f, 0.08f);

        /// <summary>Road ink.</summary>
        public static readonly Color RoadInk = new Color(0.46f, 0.27f, 0.12f);

        /// <summary>Lake wash (multiplied over the paper).</summary>
        public static readonly Color WaterWash = new Color(0.62f, 0.72f, 0.74f);

        /// <summary>Shoreline and ripple ink.</summary>
        public static readonly Color WaterInk = new Color(0.2f, 0.32f, 0.4f);

        /// <summary>Forest wash (multiplied over the paper).</summary>
        public static readonly Color ForestWash = new Color(0.86f, 0.93f, 0.76f);

        /// <summary>Tree-mark ink.</summary>
        public static readonly Color ForestInk = new Color(0.2f, 0.3f, 0.15f);

        /// <summary>Hillshade light, from the sheet's upper left (north-west on a north-up sheet), fairly high.</summary>
        private static readonly Vector3 Light = new Vector3(-1f, 1.35f, 1f).normalized;

        /// <summary>
        /// One pixel of relief: <paramref name="c"/> shaded by the ground's slope (<paramref name="dx"/> = rise per metre to the
        /// sheet's right, <paramref name="dz"/> = towards its top) under the upper-left light, darkened a little with
        /// <paramref name="altitude01"/>, with faint anti-aliased contours every <paramref name="contourStep"/> metres, stronger
        /// every <paramref name="indexEvery"/>-th one, for a sheet of <paramref name="metresPerPixel"/>.
        /// </summary>
        public static Color ShadeRelief(Color c, float dx, float dz, float height, float altitude01, float metresPerPixel, float contourStep, float indexEvery)
        {
            return ShadeRelief(c, dx, dz, height, altitude01, metresPerPixel, contourStep, indexEvery, 1.25f, 0.5f);
        }

        /// <summary>
        /// <see cref="ShadeRelief(Color, float, float, float, float, float, float, float)"/> with the hillshade's
        /// <paramref name="shadeStrength"/> (1.25 on the finite map) and its <paramref name="darkest"/> shade (0.5 there).
        /// </summary>
        public static Color ShadeRelief(Color c, float dx, float dz, float height, float altitude01, float metresPerPixel, float contourStep, float indexEvery, float shadeStrength, float darkest)
        {
            var normal = new Vector3(-dx, 1f, -dz).normalized;
            float rel = Vector3.Dot(normal, Light) / Light.y;           // 1 on flat ground
            float shade = Mathf.Clamp(1f + (rel - 1f) * shadeStrength, darkest, 1.12f);
            shade *= Mathf.Lerp(1f, 0.9f, altitude01 * altitude01);
            c = new Color(c.r * shade, c.g * shade, c.b * Mathf.Lerp(shade, 1f, 0.15f), 1f);

            float level = height / contourStep;
            float gradient = Mathf.Sqrt(dx * dx + dz * dz) * metresPerPixel / contourStep; // levels per pixel
            if (gradient > 1e-4f && gradient < 0.6f)
            {
                float distance = Mathf.Abs(level - Mathf.Round(level)) / gradient; // pixels to the nearest contour
                float line = Mathf.Clamp01(0.8f - distance);
                bool index = Mathf.Abs(Mathf.Round(level) % indexEvery) < 0.01f;
                float strength = line * (index ? 0.28f : 0.14f);
                c = Color.Lerp(c, InkBrown, strength);
            }

            return c;
        }

        /// <summary>One pixel of lake: the wash with wavy ripple hatching (<paramref name="x"/>, <paramref name="y"/> in pixels; the ripples repeat every 7 rows).</summary>
        public static Color ShadeWater(Color c, float x, float y)
        {
            Color w = Color.Lerp(c, c * WaterWash * 1.1f, 0.85f);
            w.a = 1f;
            float wave = Mathf.Sin((y + 2.2f * Mathf.Sin(x * 0.09f)) * (2f * Mathf.PI / 7f));
            if (wave > 0.82f) w = Color.Lerp(w, WaterInk, 0.22f * (wave - 0.82f) / 0.18f);
            return w;
        }

        /// <summary>Marks shore pixel <paramref name="i"/> in <paramref name="mask"/> with a softer edge on its four neighbours (row length <paramref name="width"/>).</summary>
        public static void MarkShore(float[] mask, int i, int width)
        {
            mask[i] = 1f;
            mask[i - 1] = Mathf.Max(mask[i - 1], 0.5f);
            mask[i + 1] = Mathf.Max(mask[i + 1], 0.5f);
            mask[i - width] = Mathf.Max(mask[i - width], 0.5f);
            mask[i + width] = Mathf.Max(mask[i + width], 0.5f);
        }

        /// <summary>A stippled tree: a small ring with a trunk dot below it.</summary>
        public static void TreeMark(MapRaster raster, float[] mask, Vector2 c)
        {
            raster.MaskRing(mask, c, 2.3f, 1f);
            raster.MaskDisc(mask, c + new Vector2(0f, -2.8f), 0.55f);
        }

        /// <summary>Icon radius in pixels at scale 1 for a settlement type.</summary>
        public static float IconRadius(SettlementType type)
        {
            switch (type)
            {
                case SettlementType.Town: return 9f;
                case SettlementType.Village: return 6.5f;
                default: return 7f;
            }
        }

        /// <summary>Settlement icon (town = ring with dot, village = small ring, inn = house) at <paramref name="scale"/>.</summary>
        public static void SettlementIcon(MapRaster raster, float[] mask, Vector2 c, SettlementType type, float scale)
        {
            switch (type)
            {
                case SettlementType.Town:
                    raster.MaskRing(mask, c, 9f * scale, 2.2f * scale);
                    raster.MaskDisc(mask, c, 3.4f * scale);
                    break;
                case SettlementType.Village:
                    raster.MaskRing(mask, c, 6.5f * scale, 1.9f * scale);
                    raster.MaskDisc(mask, c, 1.6f * scale);
                    break;
                default:
                    float w = 5f * scale;
                    float body = 4.5f * scale;
                    raster.MaskConvex(mask, c + new Vector2(-w, -body), c + new Vector2(w, -body), c + new Vector2(w, body * 0.4f), c + new Vector2(-w, body * 0.4f));
                    raster.MaskConvex(mask, c + new Vector2(-w - 1.5f * scale, body * 0.4f), c + new Vector2(w + 1.5f * scale, body * 0.4f), c + new Vector2(0f, body * 1.6f));
                    break;
            }
        }

        /// <summary>A "^" peak mark.</summary>
        public static void PeakSign(MapRaster raster, float[] mask, Vector2 c, float scale)
        {
            raster.MaskSegment(mask, c + new Vector2(-7f, -5f) * scale, c + new Vector2(0f, 6f) * scale, 1.3f * scale);
            raster.MaskSegment(mask, c + new Vector2(0f, 6f) * scale, c + new Vector2(7f, -5f) * scale, 1.3f * scale);
            raster.MaskSegment(mask, c + new Vector2(-3f, -5f) * scale, c + new Vector2(2f, 2f) * scale, 0.7f * scale);
        }

        /// <summary>The cartographer's col sign: two arcs back to back, ")(", the road passing up between them.</summary>
        public static void PassSign(MapRaster raster, float[] mask, Vector2 c, float scale)
        {
            PassArc(raster, mask, c + new Vector2(-8.5f, 0f) * scale, 1f, scale);
            PassArc(raster, mask, c + new Vector2(8.5f, 0f) * scale, -1f, scale);
        }

        /// <summary>A viewpoint: a small ring with short rays.</summary>
        public static void ViewpointSign(MapRaster raster, float[] mask, Vector2 c, float scale)
        {
            raster.MaskRing(mask, c, 2.6f * scale, 1f * scale);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 0.25f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                raster.MaskSegment(mask, c + d * 4.5f * scale, c + d * 7.5f * scale, 0.75f * scale);
            }
        }

        /// <summary>One bracket of the pass sign, bulging towards +x (<paramref name="facing"/> 1) or -x (-1), about 14 px tall at scale 1.</summary>
        private static void PassArc(MapRaster raster, float[] mask, Vector2 centre, float facing, float scale)
        {
            const int Pieces = 6;
            Vector2 previous = default;
            for (int i = 0; i <= Pieces; i++)
            {
                float a = Mathf.Lerp(-1.05f, 1.05f, i / (float)Pieces);
                Vector2 point = centre + new Vector2(Mathf.Cos(a) * facing * 5f, Mathf.Sin(a) * 7.5f) * scale;
                if (i > 0) raster.MaskSegment(mask, previous, point, 1.2f * scale);
                previous = point;
            }
        }
    }
}
