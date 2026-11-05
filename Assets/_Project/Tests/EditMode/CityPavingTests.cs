using System;
using System.Collections.Generic;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>
    /// A walled city's paving (2026-10-09, after the user's walk of Durncaster): no meadow on paving, the castle rock's own ground
    /// (a cobbled top, rock-painted cliffs), which pieces stand solid, and the places tour's street-level stops.
    /// </summary>
    public class CityPavingTests
    {
        private static readonly int[] Seeds = { 12345, 42, 777 };

        private static List<Place> Cities(EndlessGenerator g, double s1)
        {
            var list = new List<Place>();
            for (Region r = g.Regions.At(0); r.Start < s1; r = g.Regions.Get(r.Index + 1))
            {
                list.AddRange(g.Plan.PlacesOf(r.Index).Where(p => p.City));
            }

            return list;
        }

        /// <summary>The near tiles a city's hill and castle rock reach.</summary>
        private static List<TileBlueprint> CityTiles(EndlessGenerator g, CityHill hill)
        {
            float t = g.Params.tileSize;
            double reach = Math.Max(CityHill.MaxWallRadius + CityHill.Flank, hill.RockReach + Math.Sqrt(hill.CastleV * hill.CastleV)) + 10.0;
            var a = TileCoord.Containing(hill.CX - reach, hill.CZ - reach, t);
            var b = TileCoord.Containing(hill.CX + reach, hill.CZ + reach, t);
            var tiles = new List<TileBlueprint>();
            for (int z = a.Z; z <= b.Z; z++)
            {
                for (int x = a.X; x <= b.X; x++) tiles.Add(g.GenerateTile(new TileCoord(x, z), TileLod.Near));
            }

            return tiles;
        }

        private static IEnumerable<(EndlessGenerator g, Place city, CityHill hill, List<TileBlueprint> tiles)> AllCities()
        {
            foreach (int seed in Seeds)
            {
                var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
                foreach (Place city in Cities(g, 60000.0))
                {
                    CityHill hill = g.Terrain.Disc(city)?.Hill;
                    Assert.That(hill, Is.Not.Null, $"seed {seed}: the city at {city.S:0} m has a hill");
                    yield return (g, city, hill, CityTiles(g, hill));
                }
            }
        }

        [Test]
        public void NoMeadowOnPaving()
        {
            int cities = 0;
            foreach (var (g, city, hill, tiles) in AllCities())
            {
                cities++;
                int paved = 0, rockCells = 0;
                foreach (TileBlueprint bp in tiles)
                {
                    int res = bp.DetailResolution;
                    float[,,] cover = TerrainShaper.Resample(bp.Splat, res);
                    float texel = bp.TileSize / res;
                    double x0 = bp.Coord.OriginX(bp.TileSize), z0 = bp.Coord.OriginZ(bp.TileSize);
                    for (int z = 0; z < res; z++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            bool cobble = cover[z, x, TerrainShaper.CobbleLayer] >= CityPaving.BareCobble;
                            bool rock = hill.Rock(x0 + (x + 0.5) * texel, z0 + (z + 0.5) * texel) > CityPaving.BareRock;
                            if (!cobble && !rock) continue;
                            if (cobble) paved++;
                            if (rock) rockCells++;
                            int i = z * res + x;
                            foreach (DetailLayer layer in bp.Details)
                            {
                                Assert.That(layer.density[i], Is.EqualTo(0),
                                    $"seed {g.Params.seed}, city at {city.S:0} m: {layer.family} grows at ({x0 + (x + 0.5) * texel:0}, {z0 + (z + 0.5) * texel:0}) on {(cobble ? "cobble" : "the castle rock")}");
                            }
                        }
                    }
                }

                Assert.That(paved, Is.GreaterThan(500), $"seed {g.Params.seed}, city at {city.S:0} m: paved cells");
                if (hill.Castle != null) Assert.That(rockCells, Is.GreaterThan(200), $"seed {g.Params.seed}, city at {city.S:0} m: castle rock cells");
            }

            Assert.That(cities, Is.EqualTo(5), "cities on the three seeds within 60 km");
        }

        [Test]
        public void CastleRockIsCobbledOnTopAndRockOnItsFaces()
        {
            foreach (var (g, city, hill, tiles) in AllCities())
            {
                if (hill.Castle == null) continue;
                int top = 0, topCobbled = 0, face = 0, faceRock = 0;
                foreach (TileBlueprint bp in tiles)
                {
                    int res = bp.Splat.GetLength(0);
                    float texel = bp.TileSize / res;
                    double x0 = bp.Coord.OriginX(bp.TileSize), z0 = bp.Coord.OriginZ(bp.TileSize);
                    for (int z = 0; z < res; z++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            double px = x0 + (x + 0.5) * texel, pz = z0 + (z + 0.5) * texel;
                            float r = hill.Rock(px, pz);
                            if (r <= 0f) continue;
                            if (r >= 0.999f)
                            {
                                top++;
                                if (bp.Splat[z, x, TerrainShaper.CobbleLayer] >= 0.9f) topCobbled++;
                                continue;
                            }

                            if (r > 0.85f || r < 0.15f) continue;
                            // A face: where the ground really falls (the town side of the rock is level with the plateau).
                            float sx = g.Height(px + 1.0, pz) - g.Height(px - 1.0, pz), sz = g.Height(px, pz + 1.0) - g.Height(px, pz - 1.0);
                            float slope = 0.5f * (float)Math.Sqrt(sx * sx + sz * sz);
                            if (slope < 1.2f) continue;
                            face++;
                            if (bp.Splat[z, x, TerrainShaper.RockLayer] >= 0.5f) faceRock++;
                        }
                    }
                }

                string where = $"seed {g.Params.seed}, city at {city.S:0} m";
                Assert.That(top, Is.GreaterThan(1000), $"{where}: the rock's top texels");
                Assert.That(topCobbled, Is.EqualTo(top), $"{where}: the rock's top is cobbled");
                Assert.That(face, Is.GreaterThan(100), $"{where}: steep face texels");
                Assert.That(faceRock, Is.GreaterThanOrEqualTo((int)(0.95 * face)), $"{where}: the cliff is rock-painted ({faceRock} of {face})");
            }
        }

        [Test]
        public void PieceCollidersByName()
        {
            // Walls, towers, pillars and battlements stand solid with a box.
            foreach (string name in new[] { "SM_Bld_Castle_Wall_01", "SM_Bld_Castle_Wall_Window_01", "SM_Bld_Castle_Wall_Corner_L_Base_01", "SM_Bld_Castle_Pillar_Stone_05",
                         "SM_Bld_Castle_Battlements_02", "SM_Bld_Castle_Battlements_Pillar_01", "SM_Bld_Bridge_Pillars_01", "SM_Bld_Bridge_Railing_01" })
            {
                Assert.That(PieceColliders.Kind(PropRole.CastleModule, name), Is.EqualTo(PieceCollider.Box), name);
            }

            Assert.That(PieceColliders.HasBox(PropRole.CityWall, "SM_Bld_Castle_Wall_01"), Is.True);
            Assert.That(PieceColliders.HasBox(PropRole.CityTower, "SM_Bld_Castle_Wall_Tower_M_01"), Is.True);
            Assert.That(PieceColliders.HasBox(PropRole.CityBattlement, "SM_Bld_Castle_Battlements_01"), Is.True);

            // Openings keep the prefab's own colliders (a box from the bounds would close them).
            foreach (string name in new[] { "SM_Bld_Castle_Wall_Archway_01", "SM_Bld_Castle_Wall_Door_01", "SM_Bld_Castle_Wall_Door_Big_01", "SM_Bld_Castle_Wall_Gate_01",
                         "SM_Bld_Stairs_Straight_Wide_Edges_01", "SM_Bld_Stairs_Wood_Corner_01" })
            {
                Assert.That(PieceColliders.Kind(PropRole.CastleModule, name), Is.EqualTo(PieceCollider.Own), name);
            }

            // Overhead pieces have none.
            foreach (string name in new[] { "SM_Bld_Castle_Hoarding_01", "SM_Bld_Castle_Hoarding_Corner_L_01", "SM_Bld_Castle_Wall_Corbels_01", "SM_Bld_Castle_Roof_L_01", "SM_Bld_Castle_Roof_Cap_Edge_01" })
            {
                Assert.That(PieceColliders.Kind(PropRole.CastleModule, name), Is.EqualTo(PieceCollider.None), name);
            }

            // The countryside's pieces as before: walls, hedges and fences boxed, gate leaves and ivy open.
            Assert.That(PieceColliders.HasBox(PropRole.StoneWall, "SM_Env_Wall_01"), Is.True);
            Assert.That(PieceColliders.HasBox(PropRole.Hedge, "SM_Env_Hedge_01"), Is.True);
            Assert.That(PieceColliders.HasBox(PropRole.FenceGate, "SM_Prop_Fence_Gate_01"), Is.False);
            Assert.That(PieceColliders.HasBox(PropRole.CityStall, "SM_Prop_Market_Stall_04"), Is.False);

            // Every catalogued castle module has a rule that is not a box on an opening.
            foreach (string name in new[] { "Archway", "Gate", "Door", "Stairs" })
            {
                Assert.That(PieceColliders.HasBox(PropRole.CastleModule, "SM_Bld_Castle_X_" + name + "_01"), Is.False, name);
            }
        }

        [Test]
        public void CityHasStreetLevelViewpoints()
        {
            var g = new EndlessGenerator(new EndlessParams { seed = 12345 }, PaletteKeys.Default);
            Place city = Cities(g, 20000.0).First();
            Assert.That(city.S, Is.EqualTo(18500.0).Within(1.0), "Durncaster");
            Dressing d = g.Dressings.Settlement(city);
            CityHill hill = g.Terrain.Disc(city).Hill;
            List<CityViewpoint> views = CityViewpoints.Plan(hill, d, city.S);
            string[] names = views.Select(v => v.Name).ToArray();
            foreach (string must in new[] { "road 1 km", "road 150 m", "ramp 1 foot", "ramp 1 top", "terrace 1 edge", "view square", "castle gate", "castle court", "castle flank right", "castle flank left", "castle back", "steps 1 foot", "steps 1 top" })
            {
                Assert.That(names, Does.Contain(must), must);
            }

            Assert.That(names.Count(n => n.StartsWith("ramp ") && n.EndsWith(" foot")), Is.EqualTo(hill.Ways.Count(w => !w.Stair)), "each ramp's foot");
            Assert.That(names.Count(n => n.StartsWith("terrace ")), Is.GreaterThanOrEqualTo(hill.Faces - 1), "the terraces' edges");
            foreach (CityViewpoint v in views)
            {
                if (v.Name.StartsWith("road ")) { Assert.That(v.StandS, Is.LessThan(city.S)); continue; }
                Assert.That(double.IsNaN(v.EyeX), Is.False, v.Name);
                double dx = v.LookX - v.EyeX, dz = v.LookZ - v.EyeZ;
                Assert.That(Math.Sqrt(dx * dx + dz * dz), Is.GreaterThan(3.0), $"{v.Name}: looks somewhere");
                if (v.Name.StartsWith("castle flank") || v.Name == "castle back") Assert.That(hill.Outside(v.EyeX, v.EyeZ), Is.GreaterThan(5f), $"{v.Name}: outside the town wall");
                else Assert.That(hill.Outside(v.EyeX, v.EyeZ), Is.LessThan(0f), $"{v.Name}: inside the town wall");
            }
        }
    
        /// <summary>
        /// Every street-level eye of every city (seeds 12345, 42, 777) stands free: no piece (wall module, battlement, pillar, tower,
        /// flight, castle piece), house footprint, set piece, tree or rock within <see cref="CityClearance.Clear"/> of it; its ground is
        /// the hill's planned surface (inside the town wall) and no built ground (terrain or deck) rises above it; it is within the near
        /// range of the road point the traveller stands on. No view of the old faults is dropped (2026-10-09: inside a wall, a flight,
        /// a crown, between two gables).
        /// </summary>
        [Test]
        public void StreetLevelViewpointsStandFree()
        {
            int cities = 0, views = 0, moved = 0;
            var failures = new List<string>();
            foreach (var (g, city, hill, tiles) in AllCities())
            {
                cities++;
                Dressing d = g.Dressings.Settlement(city);
                var dropped = new List<string>();
                List<CityViewpoint> list = CityViewpoints.Plan(hill, d, city.S, dropped);
                var clear = new CityClearance(hill, d);
                string where = $"seed {g.Params.seed} city at {city.S:0} m ({hill.Castle?.Name ?? "no castle"})";
                foreach (string drop in dropped)
                {
                    // A flight's top may be dropped (the deck behind its head is narrow: lips, houses' backs); every other view must stand.
                    Log($"{where}: dropped {drop}");
                    if (!MayDrop(drop)) failures.Add($"{where}: dropped {drop}");
                }

                foreach (CityViewpoint v in list)
                {
                    RoadPoint stand = g.Road(v.StandS);
                    if (double.IsNaN(v.EyeX)) continue;
                    views++;
                    if (v.Moved > 0f) moved++;
                    double rx = v.EyeX - stand.X, rz = v.EyeZ - stand.Z;
                    double reach = Math.Sqrt(rx * rx + rz * rz);
                    if (reach > 380.0) failures.Add($"{where} {v.Name}: {reach:0} m from where the traveller stands");
                    bool inside = hill.Outside(v.EyeX, v.EyeZ) < 0f;
                    if (inside && Math.Abs(v.Ground - CityViewpoints.GroundAt(hill, clear, v.EyeX, v.EyeZ)) > 1e-3f) failures.Add($"{where} {v.Name}: ground {v.Ground:0.00} is not the surface");
                    float built = BuiltGround(tiles, v.EyeX, v.EyeZ);
                    // A deck segment straddling a tile edge is in neither tile's height grid (TerraceDecks: a tile takes the segments whose
                    // middle lies in it): the composer's own decks count as built.
                    if (clear.DeckHeight(v.EyeX, v.EyeZ, out float deckTop)) built = Math.Max(built, deckTop);
                    float ground = float.IsNaN(v.Ground) ? built : v.Ground;
                    if (Math.Abs(built - ground) > 0.3f) failures.Add($"{where} {v.Name}: the built ground {built:0.00} is not the eye's ground {ground:0.00}");
                    if (!clear.Free(v.EyeX, v.EyeZ, ground, v.EyeAbove, CityClearance.Clear, out string what, out float distance))
                        failures.Add($"{where} {v.Name}: {what} {distance:0.00} m from the eye");
                    Log($"{where} {v.Name}: moved {v.Moved:0} m, surface {CityViewpoints.SurfaceAt(hill, v.EyeX, v.EyeZ):0.00} castle {clear.CastleGround:0.00}, nearest {what} {distance:0.0} m, ground {ground:0.00} (built {built:0.00})");
                }
            }

            Log($"{cities} cities, {views} street-level eyes, {moved} moved to stand free");
            Assert.That(cities, Is.GreaterThanOrEqualTo(3));
            Assert.That(failures, Is.Empty, string.Join("; ", failures));
        }

        /// <summary>The views a city may lack (named on the console when dropped): a flight's top. The castle court and gate, the view square, the ramps and the terrace edges must stand.</summary>
        private static bool MayDrop(string drop) => drop.StartsWith("steps ", StringComparison.Ordinal) && drop.Contains(" top:");

        private static void Log(string line)
        {
            if (System.Environment.GetEnvironmentVariable("MZ_CITY_LOG") == "1") Console.WriteLine(line);
            else TestContext.Progress.WriteLine(line);
        }

        /// <summary>The built ground at an absolute point: a terrace deck's top where one covers it, else the near tile's terrain (as <c>EndlessWorld.GroundHeightAbsolute</c>).</summary>
        private static float BuiltGround(List<TileBlueprint> tiles, double x, double z)
        {
            foreach (TileBlueprint bp in tiles)
            {
                double ox = bp.Coord.OriginX(bp.TileSize), oz = bp.Coord.OriginZ(bp.TileSize);
                if (x < ox || z < oz || x >= ox + bp.TileSize || z >= oz + bp.TileSize) continue;
                float lx = (float)(x - ox), lz = (float)(z - oz);
                if (bp.TerraceDecks != null && bp.TerraceDecks.TryHeight(lx, lz, out float deck)) return deck;
                return HeightmapGenerator.SampleHeight(bp.Heights, bp.TileSize, bp.HeightRange, lx, lz);
            }

            return float.NaN;
        }

        /// <summary>
        /// A city seen from a kilometre (2026-10-09: from the road 1 km before Durncaster the far mesh showed neither the hill nor the
        /// town): on the distant tiles (8 m and 4 m grids) the ground at the plateau lies within 1 m of the hill's surface (less the far
        /// mesh's drop), and the city's distant tiles carry solids for its town wall, towers, houses and its castle (several proxies by
        /// kind: a mass, towers and the keep, not one box), within the far tiles' triangle cap.
        /// </summary>
        [Test]
        public void CityStandsOnTheDistantTiles()
        {
            int cities = 0;
            foreach (int seed in Seeds)
            {
                var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
                foreach (Place city in Cities(g, 60000.0))
                {
                    CityHill hill = g.Terrain.Disc(city).Hill;
                    cities++;
                    string where = $"seed {seed} city at {city.S:0} m";
                    foreach (TileLod lod in new[] { TileLod.Far, TileLod.Mid })
                    {
                        List<TileBlueprint> tiles = CityTiles(g, hill, lod);
                        // The plateau: the hill's surface at the castle's front and either side of the view square.
                        int samples = 0;
                        float worst = 0f;
                        // The plateau: 12 m or more behind the top face's line (beyond its bank and the grid's reach), 12 m inside the wall.
                        for (float u = -40f; u <= 40f; u += 8f)
                        for (float pv = CityHill.PlateauV; pv <= CityHill.PlateauV + 40f; pv += 8f)
                        {
                            hill.World(u, pv, out double x, out double z);
                            if (hill.Outside(x, z) > -12f || hill.Behind(hill.Faces - 1, u, pv) < 12f || hill.OnWay(u, pv, out _)) continue;
                            float surface = hill.Level + hill.SurfaceOffset(x, z);
                            float far = FarGround(tiles, x, z);
                            if (float.IsNaN(far)) continue;
                            samples++;
                            worst = Math.Max(worst, Math.Abs(far - surface));
                        }

                        Assert.That(samples, Is.GreaterThan(0), $"{where} {lod}: plateau samples");
                        Assert.That(worst, Is.LessThan(1f), $"{where} {lod}: the far ground at the plateau within 1 m of the surface");

                        var roles = new Dictionary<string, int>();
                        int tris = 0, maxTris = 0, houseTris = 0;
                        foreach (TileBlueprint bp in tiles)
                        {
                            foreach (PropPlacement p in bp.Props) roles[p.role] = roles.TryGetValue(p.role, out int c) ? c + 1 : 1;
                            FarTileMeshData mesh = FarTileMeshData.Build(bp, null, null);
                            tris += mesh.Triangles;
                            maxTris = Math.Max(maxTris, mesh.Triangles);
                            houseTris += mesh.HouseTriangles;
                            Assert.That(mesh.VertexCount, Is.LessThanOrEqualTo(ushort.MaxValue), $"{where} {lod}: 16-bit vertices");
                        }

                        Log($"{where} {lod}: {tiles.Count} tiles, {tris} triangles (largest tile {maxTris}, solids {houseTris}), plateau within {worst:0.00} m; " +
                            string.Join(", ", roles.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
                        Assert.That(roles.ContainsKey(PropRole.CityWall), Is.True, $"{where} {lod}: the town wall");
                        Assert.That(roles.ContainsKey(PropRole.CityTower), Is.True, $"{where} {lod}: its towers");
                        Assert.That(roles.Keys.Count(PropRole.IsBuilding), Is.GreaterThan(0), $"{where} {lod}: its houses");
                        if (hill.Castle != null)
                        {
                            Assert.That(roles.Keys.Count(FarCastle.IsProxy), Is.GreaterThanOrEqualTo(2), $"{where} {lod}: the castle by kinds of proxy");
                            Assert.That(roles.ContainsKey(PropRole.CastleModule), Is.False, $"{where} {lod}: no castle piece drawn one by one");
                        }

                        Assert.That(maxTris, Is.LessThanOrEqualTo(lod == TileLod.Mid ? FarTileMeshData.MidMaxTriangles : FarTileMeshData.FarMaxTriangles), $"{where} {lod}: within the cap");
                    }
                }
            }

            Assert.That(cities, Is.GreaterThanOrEqualTo(3));
        }

        /// <summary>The distant tiles a city's hill and castle rock reach, at <paramref name="lod"/>.</summary>
        private static List<TileBlueprint> CityTiles(EndlessGenerator g, CityHill hill, TileLod lod)
        {
            float t = g.Params.tileSize;
            double reach = Math.Max(CityHill.MaxWallRadius + CityHill.Flank, hill.RockReach + Math.Sqrt(hill.CastleV * hill.CastleV)) + 10.0;
            var a = TileCoord.Containing(hill.CX - reach, hill.CZ - reach, t);
            var b = TileCoord.Containing(hill.CX + reach, hill.CZ + reach, t);
            var tiles = new List<TileBlueprint>();
            for (int z = a.Z; z <= b.Z; z++)
            {
                for (int x = a.X; x <= b.X; x++) tiles.Add(g.GenerateTile(new TileCoord(x, z), lod));
            }

            return tiles;
        }

        /// <summary>The distant tiles' ground (bilinear on their grid) at an absolute point.</summary>
        private static float FarGround(List<TileBlueprint> tiles, double x, double z)
        {
            foreach (TileBlueprint bp in tiles)
            {
                double ox = bp.Coord.OriginX(bp.TileSize), oz = bp.Coord.OriginZ(bp.TileSize);
                if (x < ox || z < oz || x >= ox + bp.TileSize || z >= oz + bp.TileSize) continue;
                return HeightmapGenerator.SampleHeight(bp.Heights, bp.TileSize, bp.HeightRange, (float)(x - ox), (float)(z - oz));
            }

            return float.NaN;
        }
    }
}
