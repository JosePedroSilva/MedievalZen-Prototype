using System.Collections.Generic;
using System.Diagnostics;
using MedievalZen.Environment;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Paints the parchment map of a generated world once (at scene start): paper with fibre noise, stains and darkened
    /// edges; a north-west hillshade and faint contour lines from the terrain heightmap; lakes as a blue-grey wash with an
    /// inked shoreline and ripple hatching; forests as a green wash with stippled tree dots from the terrain's tree density;
    /// the roads in brown ink; settlement icons (town = ring with dot, village = small ring, inn = house), a "^" for the
    /// tallest peak, ")(" for the mountain pass and a small rayed ring for the viewpoint; a double frame and a north arrow. Names are not rasterised here: the map overlay lays them out as text.
    /// </summary>
    public static class MapTextureRenderer
    {
        /// <summary>Blank border of the sheet (fraction per side).</summary>
        public const float SheetMargin = 0.07f;

        private static readonly Color Paper = new Color(0.9f, 0.82f, 0.64f);
        private static readonly Color InkBrown = new Color(0.24f, 0.15f, 0.08f);
        private static readonly Color RoadInk = new Color(0.46f, 0.27f, 0.12f);
        private static readonly Color WaterWash = new Color(0.62f, 0.72f, 0.74f);
        private static readonly Color WaterInk = new Color(0.2f, 0.32f, 0.4f);
        private static readonly Color ForestWash = new Color(0.86f, 0.93f, 0.76f);
        private static readonly Color ForestInk = new Color(0.2f, 0.3f, 0.15f);

        /// <summary>The projection used for <paramref name="world"/>'s terrain (its square footprint).</summary>
        public static MapProjection ProjectionFor(GameWorld world)
        {
            Terrain terrain = world != null ? world.Terrain : null;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 origin = terrain.GetPosition();
                Vector3 size = terrain.terrainData.size;
                return new MapProjection(origin.x, origin.z, Mathf.Max(size.x, size.z), SheetMargin);
            }

            // No terrain (e.g. the player test scene): frame the road network instead.
            RoadNetwork roads = world != null ? world.Roads : null;
            float minX = -300f, minZ = -300f, maxX = 300f, maxZ = 300f;
            if (roads != null && roads.Nodes.Count > 0)
            {
                minX = minZ = float.MaxValue;
                maxX = maxZ = float.MinValue;
                foreach (RoadNode node in roads.Nodes)
                {
                    minX = Mathf.Min(minX, node.Position.x);
                    minZ = Mathf.Min(minZ, node.Position.z);
                    maxX = Mathf.Max(maxX, node.Position.x);
                    maxZ = Mathf.Max(maxZ, node.Position.z);
                }
            }

            float side = Mathf.Max(maxX - minX, maxZ - minZ) * 1.2f + 50f;
            return new MapProjection((minX + maxX - side) * 0.5f, (minZ + maxZ - side) * 0.5f, side, SheetMargin);
        }

        /// <summary>Renders the map of <paramref name="world"/> into a new texture of <paramref name="size"/> pixels.</summary>
        public static Texture2D Render(GameWorld world, MapProjection projection, int size = 1024)
        {
            var clock = Stopwatch.StartNew();
            var raster = new MapRaster(size, Paper);
            float[] mask = raster.NewMask();
            Terrain terrain = world != null ? world.Terrain : null;
            TerrainData data = terrain != null ? terrain.terrainData : null;
            float waterHeight = world != null ? world.WaterHeight : -1f;

            PaintPaper(raster);
            if (data != null) PaintRelief(raster, mask, terrain, projection, waterHeight);
            if (data != null) PaintForest(raster, mask, terrain, projection);
            PaintFrame(raster, mask, projection);

            RoadNetwork roads = world != null ? world.Roads : null;
            if (roads != null)
            {
                PaintRoads(raster, mask, roads, projection);
                PaintSettlements(raster, mask, roads, projection);
            }

            if (world != null) PaintLandmarks(raster, mask, world.Landmarks, projection);
            PaintNorthArrow(raster, mask);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "ParchmentMap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            texture.SetPixels(raster.Pixels);
            texture.Apply(true, true);
            Debug.Log($"Map: rendered the {size}px parchment map in {clock.ElapsedMilliseconds} ms.");
            return texture;
        }

        private static Vector2 ToPixel(MapProjection projection, Vector3 world, int size)
        {
            Vector2 uv = projection.WorldToMapUV(world);
            return uv * size;
        }

        // ------------------------------------------------------------------ paper

        private static void PaintPaper(MapRaster raster)
        {
            int n = raster.Size;
            Color[] px = raster.Pixels;
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n;
                    float blotch = Mathf.PerlinNoise(u * 3.1f + 11.3f, v * 3.1f + 7.9f);
                    float grain = Mathf.PerlinNoise(u * 38f + 3.7f, v * 38f + 1.1f);
                    float fibre = Mathf.PerlinNoise(u * 140f, v * 9f + 5.3f);
                    float shade = 0.9f + 0.1f * blotch + 0.05f * (grain - 0.5f) + 0.03f * (fibre - 0.5f);

                    // Darker, slightly ragged edges (aged paper).
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float ragged = 0.012f * Mathf.PerlinNoise(u * 25f + 40f, v * 25f + 17f);
                    float border = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edge - ragged) / 0.07f));
                    shade *= Mathf.Lerp(0.62f, 1f, border);

                    // A couple of faint tea stains.
                    float stain = Mathf.PerlinNoise(u * 5.3f + 91f, v * 5.3f + 33f);
                    if (stain > 0.72f) shade *= 1f - (stain - 0.72f) * 0.35f;

                    Color c = Paper * shade;
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            }
        }

        // ------------------------------------------------------------------ relief and water

        private static void PaintRelief(MapRaster raster, float[] mask, Terrain terrain, MapProjection projection, float waterHeight)
        {
            TerrainData data = terrain.terrainData;
            int hr = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, hr, hr);
            Vector3 origin = terrain.GetPosition();
            Vector3 tsize = data.size;
            int n = raster.Size;
            Color[] px = raster.Pixels;
            float mpp = projection.MetresPerUv / n;            // metres per pixel
            var light = new Vector3(-1f, 1.35f, 1f).normalized; // from the north-west, fairly high
            const float contourStep = 20f;

            var h = new float[n * n];
            var onWorld = new bool[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector3 w = projection.MapUVToWorld(new Vector2((x + 0.5f) / n, (y + 0.5f) / n));
                    float tx = (w.x - origin.x) / tsize.x;
                    float tz = (w.z - origin.z) / tsize.z;
                    int i = y * n + x;
                    onWorld[i] = tx >= 0f && tx <= 1f && tz >= 0f && tz <= 1f;
                    h[i] = origin.y + SampleHeights(heights, hr, tx, tz) * tsize.y;
                }
            }

            bool hasWater = waterHeight > 0f;
            for (int y = 1; y < n - 1; y++)
            {
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (!onWorld[i]) continue;
                    float dx = (h[i + 1] - h[i - 1]) / (2f * mpp);
                    float dz = (h[i + n] - h[i - n]) / (2f * mpp);
                    var normal = new Vector3(-dx, 1f, -dz).normalized;
                    float rel = Vector3.Dot(normal, light) / light.y;           // 1 on flat ground
                    float shade = Mathf.Clamp(1f + (rel - 1f) * 1.25f, 0.5f, 1.12f);
                    float altitude = Mathf.Clamp01((h[i] - origin.y) / Mathf.Max(1f, tsize.y));
                    shade *= Mathf.Lerp(1f, 0.9f, altitude * altitude);

                    Color c = px[i];
                    c = new Color(c.r * shade, c.g * shade, c.b * Mathf.Lerp(shade, 1f, 0.15f), 1f);

                    // Faint anti-aliased contour lines every 20 m, stronger every 100 m.
                    float level = h[i] / contourStep;
                    float gradient = Mathf.Sqrt(dx * dx + dz * dz) * mpp / contourStep; // levels per pixel
                    if (gradient > 1e-4f && gradient < 0.6f)
                    {
                        float distance = Mathf.Abs(level - Mathf.Round(level)) / gradient; // pixels to the nearest contour
                        float line = Mathf.Clamp01(0.8f - distance);
                        bool index = Mathf.Abs(Mathf.Round(level) % 5f) < 0.01f;
                        float strength = line * (index ? 0.28f : 0.14f);
                        c = Color.Lerp(c, InkBrown, strength);
                    }

                    px[i] = c;
                }
            }

            if (!hasWater) return;

            var wet = new bool[n * n];
            for (int i = 0; i < wet.Length; i++) wet[i] = onWorld[i] && h[i] < waterHeight;
            for (int y = 1; y < n - 1; y++)
            {
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (!wet[i]) continue;
                    bool shore = !wet[i - 1] || !wet[i + 1] || !wet[i - n] || !wet[i + n];
                    Color c = Color.Lerp(px[i], px[i] * WaterWash * 1.1f, 0.85f);
                    c.a = 1f;

                    // Wavy ripple hatching.
                    float wave = Mathf.Sin((y + 2.2f * Mathf.Sin(x * 0.09f)) * (2f * Mathf.PI / 7f));
                    if (wave > 0.82f) c = Color.Lerp(c, WaterInk, 0.22f * (wave - 0.82f) / 0.18f);
                    px[i] = c;
                    if (shore)
                    {
                        mask[i] = 1f;
                        mask[i - 1] = Mathf.Max(mask[i - 1], 0.5f);
                        mask[i + 1] = Mathf.Max(mask[i + 1], 0.5f);
                        mask[i - n] = Mathf.Max(mask[i - n], 0.5f);
                        mask[i + n] = Mathf.Max(mask[i + n], 0.5f);
                    }
                }
            }

            raster.Composite(mask, WaterInk, 0.75f);
        }

        private static float SampleHeights(float[,] heights, int resolution, float tx, float tz)
        {
            float gx = Mathf.Clamp01(tx) * (resolution - 1);
            float gz = Mathf.Clamp01(tz) * (resolution - 1);
            int x0 = Mathf.Min(resolution - 2, (int)gx);
            int z0 = Mathf.Min(resolution - 2, (int)gz);
            float fx = gx - x0;
            float fz = gz - z0;
            float a = Mathf.Lerp(heights[z0, x0], heights[z0, x0 + 1], fx);
            float b = Mathf.Lerp(heights[z0 + 1, x0], heights[z0 + 1, x0 + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }

        // ------------------------------------------------------------------ forest

        private static void PaintForest(MapRaster raster, float[] mask, Terrain terrain, MapProjection projection)
        {
            TreeDensityGrid grid = TreeDensityGrid.FromTerrain(terrain, 12f);
            if (grid.MaxDensity <= 0f) return;
            int n = raster.Size;

            // Soft green wash where trees stand.
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector3 w = projection.MapUVToWorld(new Vector2((x + 0.5f) / n, (y + 0.5f) / n));
                    float d = grid.Sample(w.x, w.z);
                    if (d > 0.05f) mask[y * n + x] = Mathf.Clamp01((d - 0.05f) / 1.2f);
                }
            }

            raster.CompositeMultiply(mask, ForestWash, 0.8f);

            // Stippled tree dots on a jittered lattice, denser where the forest is denser.
            const int step = 6;
            var rng = new System.Random(1234);
            for (int y = step / 2; y < n; y += step)
            {
                for (int x = step / 2; x < n; x += step)
                {
                    float jx = x + (float)(rng.NextDouble() * 4.0 - 2.0);
                    float jy = y + (float)(rng.NextDouble() * 4.0 - 2.0);
                    double roll = rng.NextDouble();
                    Vector3 w = projection.MapUVToWorld(new Vector2(jx / n, jy / n));
                    float d = grid.Sample(w.x, w.z);
                    float p = Mathf.Clamp((d - 0.12f) / 1.1f, 0f, 0.95f);
                    if (roll >= p) continue;
                    var c = new Vector2(jx, jy);
                    raster.MaskRing(mask, c, 2.3f, 1f);
                    raster.MaskDisc(mask, c + new Vector2(0f, -2.8f), 0.55f);
                }
            }

            raster.Composite(mask, ForestInk, 0.7f);
        }

        // ------------------------------------------------------------------ roads, settlements, landmarks

        private static void PaintRoads(MapRaster raster, float[] mask, RoadNetwork roads, MapProjection projection)
        {
            int n = raster.Size;
            foreach (RoadSegment segment in roads.Segments)
            {
                IReadOnlyList<Vector3> points = segment.Points;
                Vector2 previous = ToPixel(projection, points[0], n);
                for (int i = 1; i < points.Count; i++)
                {
                    Vector2 next = ToPixel(projection, points[i], n);
                    if ((next - previous).sqrMagnitude < 1f && i < points.Count - 1) continue;   // skip sub-pixel steps
                    raster.MaskSegment(mask, previous, next, 1.35f);
                    previous = next;
                }
            }

            raster.Composite(mask, RoadInk, 0.92f);
        }

        private static void PaintSettlements(MapRaster raster, float[] mask, RoadNetwork roads, MapProjection projection)
        {
            int n = raster.Size;
            float scale = n / 1024f;

            // Clear a little paper halo so icons read cleanly over roads and stipple.
            foreach (Settlement s in roads.Settlements)
            {
                raster.MaskDisc(mask, ToPixel(projection, s.Position, n), IconRadius(s.Type) * scale + 3f * scale);
            }

            raster.Composite(mask, Paper * 0.97f, 0.9f);

            foreach (Settlement s in roads.Settlements)
            {
                Vector2 c = ToPixel(projection, s.Position, n);
                switch (s.Type)
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

            raster.Composite(mask, InkBrown, 0.95f);
        }

        /// <summary>Icon radius in pixels at 1024 px for a settlement type (the overlay uses it to place labels).</summary>
        public static float IconRadius(SettlementType type)
        {
            switch (type)
            {
                case SettlementType.Town: return 9f;
                case SettlementType.Village: return 6.5f;
                default: return 7f;
            }
        }

        private static void PaintLandmarks(MapRaster raster, float[] mask, IReadOnlyList<Landmark> landmarks, MapProjection projection)
        {
            if (landmarks == null) return;
            int n = raster.Size;
            float scale = n / 1024f;
            foreach (Landmark landmark in landmarks)
            {
                Vector2 c = ToPixel(projection, landmark.position, n);
                switch (landmark.kind)
                {
                    case LandmarkKind.Peak:
                        raster.MaskSegment(mask, c + new Vector2(-7f, -5f) * scale, c + new Vector2(0f, 6f) * scale, 1.3f * scale);
                        raster.MaskSegment(mask, c + new Vector2(0f, 6f) * scale, c + new Vector2(7f, -5f) * scale, 1.3f * scale);
                        raster.MaskSegment(mask, c + new Vector2(-3f, -5f) * scale, c + new Vector2(2f, 2f) * scale, 0.7f * scale);
                        break;
                    case LandmarkKind.Pass:
                        // The cartographer's col sign: two arcs back to back, ")(".
                        PassArc(raster, mask, c + new Vector2(-8.5f, 0f) * scale, 1f, scale);
                        PassArc(raster, mask, c + new Vector2(8.5f, 0f) * scale, -1f, scale);
                        break;
                    case LandmarkKind.Viewpoint:
                        // A small eye-like mark: a ring with short rays.
                        raster.MaskRing(mask, c, 2.6f * scale, 1f * scale);
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k * Mathf.PI * 0.25f;
                            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                            raster.MaskSegment(mask, c + d * 4.5f * scale, c + d * 7.5f * scale, 0.75f * scale);
                        }

                        break;
                }
            }

            raster.Composite(mask, InkBrown, 0.95f);
        }

        /// <summary>One bracket of the pass sign, bulging towards +x (<paramref name="facing"/> 1) or -x (-1), about 14 px tall at 1024 px.</summary>
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

        private static void PaintFrame(MapRaster raster, float[] mask, MapProjection projection)
        {
            int n = raster.Size;
            float inner = projection.Margin * n;
            float outer = inner - 0.012f * n;
            Rectangle(raster, mask, inner, n - inner, 1.1f);
            Rectangle(raster, mask, outer, n - outer, 0.7f);
            raster.Composite(mask, InkBrown, 0.8f);
        }

        private static void Rectangle(MapRaster raster, float[] mask, float lo, float hi, float radius)
        {
            raster.MaskSegment(mask, new Vector2(lo, lo), new Vector2(hi, lo), radius);
            raster.MaskSegment(mask, new Vector2(hi, lo), new Vector2(hi, hi), radius);
            raster.MaskSegment(mask, new Vector2(hi, hi), new Vector2(lo, hi), radius);
            raster.MaskSegment(mask, new Vector2(lo, hi), new Vector2(lo, lo), radius);
        }

        private static void PaintNorthArrow(MapRaster raster, float[] mask)
        {
            int n = raster.Size;
            float s = n / 1024f;
            var c = new Vector2(n * 0.9f, n * 0.86f);
            raster.MaskSegment(mask, c + new Vector2(0f, -22f) * s, c + new Vector2(0f, 14f) * s, 1.2f * s);
            raster.MaskConvex(mask, c + new Vector2(-6f, 10f) * s, c + new Vector2(6f, 10f) * s, c + new Vector2(0f, 26f) * s);
            raster.MaskRing(mask, c + new Vector2(0f, -4f) * s, 11f * s, 1.2f * s);
            raster.Composite(mask, InkBrown, 0.85f);
        }
    }
}
