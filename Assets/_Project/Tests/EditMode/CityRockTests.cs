using System;
using System.Collections.Generic;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The castle rock's faces (2026-10-09, after the review tour of Durncaster: a smooth slope painted rock read as a flat orange dune,
    /// and one giant cliff boulder filled the back view): the faceted rock skin (<see cref="RockSkinField"/>, <see cref="RockSkinMeshData"/>)
    /// on the rock's steep faces in every city tile, laid on the built terrain; the rocks round it at a believable size and outside the wall.
    /// </summary>
    public class CityRockTests
    {
        private sealed class City
        {
            public int Seed;
            public EndlessGenerator G;
            public Place Place;
            public Dressing D;
            public CityHill Hill;
            public string Name => $"{Place.Name} (seed {Seed}, {Place.S:0} m)";
        }

        private static readonly Dictionary<int, List<City>> cache = new Dictionary<int, List<City>>();
        private static readonly Dictionary<(int, int, int), TileBlueprint> tiles = new Dictionary<(int, int, int), TileBlueprint>();

        private static List<City> Cities(int seed)
        {
            if (cache.TryGetValue(seed, out List<City> list)) return list;
            list = new List<City>();
            var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            for (Region r = g.Regions.At(0); r.Start < 60000.0; r = g.Regions.Get(r.Index + 1))
            {
                foreach (Place p in g.Plan.PlacesOf(r.Index).Where(p => p.City))
                {
                    CityHill hill = g.Terrain.Disc(p).Hill;
                    if (hill == null || hill.Castle == null) continue;
                    list.Add(new City { Seed = seed, G = g, Place = p, D = g.Dressings.Settlement(p), Hill = hill });
                }
            }

            cache[seed] = list;
            return list;
        }

        /// <summary>The near tiles the castle rock reaches.</summary>
        private static List<TileBlueprint> RockTiles(City c)
        {
            float size = c.G.Params.tileSize;
            CityHill h = c.Hill;
            float reach = h.RockReach;
            TileCoord a = TileCoord.Containing(h.CX - reach, h.CZ - reach, size), b = TileCoord.Containing(h.CX + reach, h.CZ + reach, size);
            var list = new List<TileBlueprint>();
            for (int z = a.Z; z <= b.Z; z++)
            {
                for (int x = a.X; x <= b.X; x++)
                {
                    if (!tiles.TryGetValue((c.Seed, x, z), out TileBlueprint bp)) tiles[(c.Seed, x, z)] = bp = c.G.GenerateTile(new TileCoord(x, z), TileLod.Near);
                    list.Add(bp);
                }
            }

            return list;
        }

        /// <summary>The built terrain of a near tile at tile-local (x, z): its heightmap over the cell's two triangles as the skin reads it.</summary>
        private static float Built(TileBlueprint bp, float x, float z)
        {
            int n = bp.Heights.GetLength(0);
            float sp = bp.TileSize / (n - 1);
            float gx = Mathf.Clamp(x / sp, 0f, n - 1.001f), gz = Mathf.Clamp(z / sp, 0f, n - 1.001f);
            int i = (int)gx, j = (int)gz;
            float fx = gx - i, fz = gz - j;
            float r = bp.HeightRange;
            float ha = bp.Heights[j, i] * r, hb = bp.Heights[j, i + 1] * r, hc = bp.Heights[j + 1, i] * r, hd = bp.Heights[j + 1, i + 1] * r;
            return fx >= fz ? ha + (hb - ha) * fx + (hd - hb) * fz : ha + (hd - hc) * fx + (hc - ha) * fz;
        }

        private static float Slope(TileBlueprint bp, float x, float z)
        {
            float dx = (Built(bp, x + 1f, z) - Built(bp, x - 1f, z)) * 0.5f, dz = (Built(bp, x, z + 1f) - Built(bp, x, z - 1f)) * 0.5f;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The 1 m cells (tile-local) whose centres the skin's triangles cover seen from above.</summary>
        private static HashSet<(int, int)> Covered(RockSkinMeshData skin)
        {
            var set = new HashSet<(int, int)>();
            if (skin == null) return set;
            for (int t = 0; t + 2 < skin.IndexCount; t += 3)
            {
                Vector3 a = skin.Vertices[skin.Indices[t]].position, b = skin.Vertices[skin.Indices[t + 1]].position, c = skin.Vertices[skin.Indices[t + 2]].position;
                int x0 = (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x))), x1 = (int)Math.Ceiling(Math.Max(a.x, Math.Max(b.x, c.x)));
                int z0 = (int)Math.Floor(Math.Min(a.z, Math.Min(b.z, c.z))), z1 = (int)Math.Ceiling(Math.Max(a.z, Math.Max(b.z, c.z)));
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, pz = z + 0.5f;
                        float d1 = Cross(a, b, px, pz), d2 = Cross(b, c, px, pz), d3 = Cross(c, a, px, pz);
                        bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
                        if (!(neg && pos)) set.Add((x, z));
                    }
                }
            }

            return set;
        }

        private static float Cross(Vector3 a, Vector3 b, float px, float pz) => (b.x - a.x) * (pz - a.z) - (b.z - a.z) * (px - a.x);

        /// <summary>The skin covers at least 90% of the castle rock's steep faces (clear of the road, the town wall and the castle, as the skin's rule).</summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void TheCastleRockWearsTheSkin(int seed)
        {
            foreach (City c in Cities(seed))
            {
                var hills = new List<CityHill> { c.Hill };
                float half = 0.5f * c.G.Params.roadWidth;
                int steep = 0, covered = 0, tris = 0, skins = 0;
                foreach (TileBlueprint bp in RockTiles(c))
                {
                    double ox = bp.Coord.OriginX(bp.TileSize), oz = bp.Coord.OriginZ(bp.TileSize);
                    HashSet<(int, int)> cells = Covered(bp.RockSkin);
                    if (bp.RockSkin != null) { tris += bp.RockSkin.Triangles; skins++; }
                    for (int z = 2; z < (int)bp.TileSize - 2; z++)
                    {
                        for (int x = 2; x < (int)bp.TileSize - 2; x++)
                        {
                            double ax = ox + x + 0.5, az = oz + z + 0.5;
                            if (RockSkinField.CastleFace(hills, ax, az) < 0.5f) continue;
                            if (Slope(bp, x + 0.5f, z + 0.5f) < RockSkinField.SteepFull) continue;
                            c.G.Curve.Nearest(ax, az, out double road);
                            if (road < half + RockSkinField.RoadClearance + RockSkinField.RoadRamp) continue;
                            steep++;
                            if (cells.Contains((x, z))) covered++;
                        }
                    }
                }

                float share = steep > 0 ? (float)covered / steep : 1f;
                Console.WriteLine($"{c.Name}: steep castle rock face {steep} m², skin over {covered} m² ({100f * share:0.0}%), {skins} tiles with skin, {tris} skin triangles");
                Assert.That(steep, Is.GreaterThan(200), $"{c.Name}: the castle rock has hardly any steep face");
                Assert.That(share, Is.GreaterThanOrEqualTo(0.9f), $"{c.Name}: the skin covers {100f * share:0.0}% of the steep face");
            }
        }

        /// <summary>
        /// Every skin vertex in a city tile lies on the built terrain: none more than 0.3 m below it, the skin's own edge within 0.3 m of
        /// it (it meets the ground by geometry), and none farther from the ground than the skin's relief allows plus 0.3 m.
        /// </summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void TheSkinLiesOnTheBuiltTerrain(int seed)
        {
            float allowed = RockSkinMeshData.MaxCliffRelief + RockSkinMeshData.Lift + 0.3f;
            foreach (City c in Cities(seed))
            {
                int vertices = 0, edges = 0;
                float worstBelow = 0f, worstEdge = 0f, worstAway = 0f;
                foreach (TileBlueprint bp in RockTiles(c))
                {
                    RockSkinMeshData skin = bp.RockSkin;
                    if (skin == null) continue;
                    // The skin's boundary: edges of one triangle only (vertices are per face: match by position).
                    var edgeUse = new Dictionary<(long, long, long, long), int>();
                    for (int t = 0; t + 2 < skin.IndexCount; t += 3)
                    {
                        for (int e = 0; e < 3; e++)
                        {
                            var key = EdgeKey(skin.Vertices[skin.Indices[t + e]].position, skin.Vertices[skin.Indices[t + (e + 1) % 3]].position);
                            edgeUse.TryGetValue(key, out int k);
                            edgeUse[key] = k + 1;
                        }
                    }

                    var rim = new HashSet<(long, long)>();
                    foreach (var kv in edgeUse)
                    {
                        if (kv.Value != 1) continue;
                        rim.Add((kv.Key.Item1, kv.Key.Item2));
                        rim.Add((kv.Key.Item3, kv.Key.Item4));
                    }

                    var tileEdge = new HashSet<(long, long)>(skin.EdgePoints.Values.Select(e => (Q(e.x), Q(e.z))));
                    for (int k = 0; k < skin.VertexCount; k++)
                    {
                        Vector3 p = skin.Vertices[k].position;
                        vertices++;
                        float below = Built(bp, p.x, p.z) - p.y;
                        worstBelow = Math.Max(worstBelow, below);
                        Assert.That(below, Is.LessThanOrEqualTo(0.3f), $"{c.Name}: a skin vertex {below:0.00} m under the terrain at tile {bp.Coord.X},{bp.Coord.Z} ({p.x:0.0}, {p.z:0.0})");
                        // Points on the tile's edge lines (the skin goes on in the next tile; displaced with their relief, so known by the skin's own list).
                        bool onTileEdge = tileEdge.Contains((Q(p.x), Q(p.z)));
                        if (!onTileEdge && rim.Contains((Q(p.x), Q(p.z))))
                        {
                            edges++;
                            // Distance to the terrain's plane there (the vertical gap shrinks by the slope's cosine: on a 65° face 0.4 m up is 0.17 m off).
                            float s = Slope(bp, p.x, p.z);
                            float gap = Math.Abs(p.y - Built(bp, p.x, p.z)) / Mathf.Sqrt(1f + s * s);
                            worstEdge = Math.Max(worstEdge, gap);
                            Assert.That(gap, Is.LessThanOrEqualTo(0.3f), $"{c.Name}: the skin's edge {gap:0.00} m off the terrain at ({p.x:0.0}, {p.z:0.0})");
                        }

                        // Distance to the terrain: nearest of its points within the relief's reach (0.5 m grid).
                        float best = float.MaxValue;
                        for (float dz = -allowed; dz <= allowed && best > 0.3f; dz += 0.5f)
                        {
                            for (float dx = -allowed; dx <= allowed; dx += 0.5f)
                            {
                                float qx = p.x + dx, qz = p.z + dz;
                                float dy = Built(bp, qx, qz) - p.y;
                                best = Math.Min(best, (float)Math.Sqrt(dx * dx + dy * dy + dz * dz));
                            }
                        }

                        worstAway = Math.Max(worstAway, best);
                        Assert.That(best, Is.LessThanOrEqualTo(allowed), $"{c.Name}: a skin vertex {best:0.00} m from the terrain at ({p.x:0.0}, {p.z:0.0})");
                    }
                }

                Console.WriteLine($"{c.Name}: {vertices} skin vertices, worst {worstBelow:0.00} m under the terrain, {edges} edge vertices within {worstEdge:0.00} m, farthest {worstAway:0.00} m from it");
            }
        }

        private static long Q(float v) => (long)Math.Round(v * 1000.0);

        private static (long, long, long, long) EdgeKey(Vector3 a, Vector3 b)
        {
            (long, long) p = (Q(a.x), Q(a.z)), q = (Q(b.x), Q(b.z));
            return p.CompareTo(q) <= 0 ? (p.Item1, p.Item2, q.Item1, q.Item2) : (q.Item1, q.Item2, p.Item1, p.Item2);
        }

        /// <summary>
        /// No rock piece wider than 8 m within 10 m of the town wall's outline or of the three outside castle views; every cliff piece outside
        /// the wall (by half its size); the castle rock keeps its rocks. Reports the counts.
        /// </summary>
        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void RocksOfABelievableSize(int seed)
        {
            PaletteKeys keys = PaletteKeys.Default;
            foreach (City c in Cities(seed))
            {
                Dressing d = c.D;
                CityHill h = c.Hill;
                var views = CityViewpoints.Plan(h, d, c.Place.S)
                    .Where(v => !double.IsNaN(v.EyeX) && (v.Name.StartsWith("castle flank", StringComparison.Ordinal) || v.Name == "castle back"))
                    .Select(v => (v.EyeX, v.EyeZ)).ToList();
                Assert.That(views.Count, Is.EqualTo(3), $"{c.Name}: the three outside castle views");
                int rocks = 0, near = 0;
                float largest = 0f, largestNear = 0f;
                foreach (PropPlacement p in d.Props)
                {
                    if (!PropRole.IsRock(p.role) && !PropRole.IsCliff(p.role)) continue;
                    if (!keys.TryGetMetrics(p.role, p.variant, out PropMetrics m)) continue;
                    // The footprint's centre (the seat puts the bounds' centre where it was asked, not the pivot).
                    Vector3 mc = 0.5f * (m.min + m.max) * p.scale;
                    float yr = p.yaw * Mathf.Deg2Rad, cs = Mathf.Cos(yr), sn = Mathf.Sin(yr);
                    double x = d.X0 + p.position.x + mc.x * cs + mc.z * sn, z = d.Z0 + p.position.z - mc.x * sn + mc.z * cs;
                    float size = m.Length * p.scale, halfSize = 0.5f * size;
                    float outside = h.Outside(x, z);
                    bool close = Math.Abs(outside) - halfSize < CityComposer.NearRockReach;
                    foreach (var (vx, vz) in views) close |= Math.Sqrt((vx - x) * (vx - x) + (vz - z) * (vz - z)) - halfSize < CityComposer.NearRockReach;
                    rocks++;
                    largest = Math.Max(largest, size);
                    if (close)
                    {
                        near++;
                        largestNear = Math.Max(largestNear, size);
                        Assert.That(size, Is.LessThanOrEqualTo(CityComposer.NearRockMax + 0.01f), $"{c.Name}: a {size:0.0} m {p.role} within 10 m of the wall or a castle view");
                    }

                    if (PropRole.IsCliff(p.role)) Assert.That(outside, Is.GreaterThan(halfSize), $"{c.Name}: a {size:0.0} m {p.role} reaching inside the town wall ({outside:0.0} m out)");
                }

                Console.WriteLine($"{c.Name}: {rocks} rocks ({d.City.Rocks} seated by the composer, {d.City.CastleRocks} on the castle rock), {near} near the wall or a view " +
                                  $"(largest {largestNear:0.0} m), largest anywhere {largest:0.0} m");
                Assert.That(d.City.CastleRocks, Is.GreaterThanOrEqualTo(8), $"{c.Name}: the castle rock has only {d.City.CastleRocks} rocks");
            }
        }
    }
}
