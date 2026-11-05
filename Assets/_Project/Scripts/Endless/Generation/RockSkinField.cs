using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// Where a near tile's faceted rock skin goes (2026-10-06, endless only): the rockiness of every node of the generator's padded
    /// window, from what the paint itself is made of (the range's strength, the slope, the outcrop noise of the moderate slopes) rather
    /// than from the tile's splat, so two neighbouring tiles agree on it along their shared edge. Clear of the road and its verge.
    /// Since 2026-10-09 also a walled city's castle rock (<see cref="CastleFace"/>): its steep cliff, on any tile the rock reaches.
    /// Pure: no Unity objects.
    /// </summary>
    public static class RockSkinField
    {
        /// <summary>Slope (rise over run) where the steep rock starts to carry skin and where it is full (about 27° and 39°).</summary>
        public const float SteepStart = 0.50f, SteepFull = 0.80f;

        /// <summary>Clearance (metres) beyond the road's half width (its 1.5 m verge and a margin) before any skin, and the ramp after it.</summary>
        public const float RoadClearance = 2.5f, RoadRamp = 5f;

        /// <summary>Castle rock weights (<see cref="CityHill.Rock"/>) between which its cliff carries skin: above the foot, below the level top.</summary>
        public const float CastleFoot = 0.02f, CastleTop = 0.985f;

        /// <summary>No castle rock skin within this many metres of the town wall's outline (its modules and towers), full skin <see cref="CastleWallRamp"/> m further.</summary>
        public const float CastleWallClear = 4f, CastleWallRamp = 3f;

        /// <summary>Most the castle skin's rim is raised over a bulge of the terrain (<see cref="RockSkinMeshData.Input.MaxRimLift"/>): it stays on the ground at the rock's top edge.</summary>
        public const float CastleRimLift = 0.2f;

        /// <summary>Rockiness of a node: range strength × (steep rock, or an outcrop on the moderate slopes) × clearance from the road.</summary>
        public static float Rockiness(float mountain, float slope, float outcrop, float roadDistance, float roadWidth)
        {
            if (mountain <= 0f) return 0f;
            float steep = TerrainShaper.SmoothStep01((slope - SteepStart) / (SteepFull - SteepStart));
            float crop = TerrainShaper.ModerateSlope(slope) *
                         TerrainShaper.SmoothStep01((outcrop - TerrainShaper.MountainOutcropStart) / (TerrainShaper.MountainOutcropFull - TerrainShaper.MountainOutcropStart));
            float clear = TerrainShaper.SmoothStep01((roadDistance - 0.5f * roadWidth - RoadClearance) / RoadRamp);
            return Mathf.Clamp01(mountain) * Math.Max(steep, crop) * clear;
        }

        /// <summary>
        /// The castle rock's part in the skin at absolute (x, z) (2026-10-09, the hill town's crag): 1 on the rock's cliff, fading to 0
        /// at its foot (where <see cref="CityHill.Rock"/> nears 0), at its level top (where it nears 1), within <see cref="CastleWallClear"/>
        /// of the town wall's outline and on the castle's footprint (grown by 1 m); 0 off every castle rock.
        /// </summary>
        public static float CastleFace(List<CityHill> hills, double x, double z)
        {
            float best = 0f;
            foreach (CityHill hill in hills)
            {
                if (hill == null || hill.Castle == null) continue;
                float r = hill.Rock(x, z);
                if (r <= CastleFoot || r >= CastleTop) continue;
                float o = Math.Abs(hill.Outside(x, z));
                if (o <= CastleWallClear) continue;
                hill.Local(x, z, out float u, out float v);
                if (hill.OnCastle(u, v, 1f)) continue;
                float f = TerrainShaper.SmoothStep01((r - CastleFoot) / 0.1f) * TerrainShaper.SmoothStep01((CastleTop - r) / 0.03f) *
                          TerrainShaper.SmoothStep01((o - CastleWallClear) / CastleWallRamp);
                best = Math.Max(best, f);
            }

            return best;
        }

        /// <summary>Rockiness of a castle rock node: its face weight × steep rock × clearance from the road (the ramps of <see cref="Rockiness"/>).</summary>
        public static float CastleRockiness(float face, float slope, float roadDistance, float roadWidth)
        {
            if (face <= 0f) return 0f;
            float steep = TerrainShaper.SmoothStep01((slope - SteepStart) / (SteepFull - SteepStart));
            float clear = TerrainShaper.SmoothStep01((roadDistance - 0.5f * roadWidth - RoadClearance) / RoadRamp);
            return face * steep * clear;
        }

        /// <summary>The range's strength at window-local (x, z), bilinear between cover texel centres as <see cref="LandCover.Sample"/>.</summary>
        private static float Mountain(LandCover cover, float x, float z)
        {
            int res = cover.Resolution, last = res - 1;
            float gx = Mathf.Clamp(x / cover.TexelSize - 0.5f, 0f, last), gz = Mathf.Clamp(z / cover.TexelSize - 0.5f, 0f, last);
            int x0 = Math.Max(0, Math.Min((int)gx, last - 1)), z0 = Math.Max(0, Math.Min((int)gz, last - 1));
            int x1 = Math.Min(x0 + 1, last), z1 = Math.Min(z0 + 1, last);
            float tx = gx - x0, tz = gz - z0;
            float[] m = cover.Mountain;
            float a = m[z0 * res + x0] + (m[z0 * res + x1] - m[z0 * res + x0]) * tx;
            float b = m[z1 * res + x0] + (m[z1 * res + x1] - m[z1 * res + x0]) * tx;
            return a + (b - a) * tz;
        }

        /// <summary>
        /// Builds the skin of the near tile <paramref name="coord"/> from its window and paint; null without rock. The pass ranges' rock
        /// comes from the paint's own rules; a walled city's castle rock (<paramref name="castleRocks"/>, absolute frame, may be null)
        /// adds its steep cliff (<see cref="CastleFace"/>) on any tile it reaches. The caller skips tiles with neither.
        /// </summary>
        public static RockSkinMeshData Build(GenWindow w, TileCoord coord, double tx, double tz, float tile, int padCells, float[,,] splat, int seed, float roadWidth, float heightRange,
            List<CityHill> castleRocks = null)
        {
            if (w?.Map == null || w.Cover == null || w.Cover.Mountain == null || splat == null) return null;
            bool castles = castleRocks != null && castleRocks.Count > 0;
            float[,] map = w.Map;
            int rows = map.GetLength(0), cols = map.GetLength(1);
            float sp = w.Size / Math.Max(1, cols - 1);
            float range = heightRange;
            var noise = new GradientNoise(w.Params.seed + 16);   // the paint's outcrop noise (TerrainShaper.ComputeSplat)
            float ox = w.Params.NoiseOriginX, oz = w.Params.NoiseOriginZ;
            var rock = new float[rows * cols];
            bool any = false;
            // Only the nodes the skin's lattice can reach (the tile and a few metres round it).
            int lo = Math.Max(2, padCells - 6), hiX = Math.Min(cols - 3, cols - 1 - padCells + 6), hiZ = Math.Min(rows - 3, rows - 1 - padCells + 6);
            for (int j = lo; j <= hiZ; j++)
            {
                for (int i = lo; i <= hiX; i++)
                {
                    float x = i * sp, z = j * sp;
                    float mountain = Mountain(w.Cover, x, z);
                    float face = castles ? CastleFace(castleRocks, w.X0 + x, w.Z0 + z) : 0f;
                    if (mountain <= 0f && face <= 0f) continue;
                    float dx = (map[j, i + 1] - map[j, i - 1]) * range / (2f * sp);
                    float dz = (map[j + 1, i] - map[j - 1, i]) * range / (2f * sp);
                    float slope = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (slope < 0.16f) continue;   // below ModerateSlope's start and the steep ramp: no rock
                    float road = w.Roads != null ? w.Roads.Sample(x, z) : 1000f;
                    float r = 0f;
                    if (mountain > 0f)
                    {
                        // The outcrop noise only where it can matter (moderate slopes short of full steep rock).
                        float o = slope < SteepFull && TerrainShaper.ModerateSlope(slope) > 0f ? TerrainShaper.MountainOutcrop(noise, x + ox, z + oz) : 0f;
                        r = Rockiness(mountain, slope, o, road, roadWidth);
                    }

                    if (face > 0f) r = Math.Max(r, CastleRockiness(face, slope, road, roadWidth));
                    rock[j * cols + i] = r;
                    any |= r > 0.2f;
                }
            }

            if (!any) return null;
            return RockSkinMeshData.Build(new RockSkinMeshData.Input
            {
                Map = map, Rock = rock, PadCells = padCells, Spacing = sp, HeightRange = range, TileSize = tile,
                TileX = coord.X, TileZ = coord.Z, Seed = seed, Splat = splat, ChannelColors = FarTileMeshData.AppearanceChannelColors,
                OriginX = tx, OriginZ = tz,
                // A city tile without pass paint: its castle rock is painted with the shared rock layer (CityPaving), so the skin takes that colour.
                RockLayer = castles && !TileGenerator.UsesChannels(splat, TerrainShaper.MountainRockLayer, TerrainShaper.MountainRockLayer + 1) ? TerrainShaper.RockLayer : TerrainShaper.MountainRockLayer,
                MaxRimLift = castles ? CastleRimLift : float.PositiveInfinity,
            });
        }
    }
}
