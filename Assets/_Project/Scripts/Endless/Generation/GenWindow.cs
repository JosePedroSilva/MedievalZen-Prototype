using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// A square of the endless world prepared for the finite world's stages: heights sampled from the global terrain
    /// function on a grid aligned to absolute coordinates, generation parameters whose <see cref="WorldGenParams.window"/>
    /// makes every noise, pattern and scatter stream absolute, the road as a local centre line and distance field, and
    /// land cover filled from the region functions. Local coordinates run 0..<see cref="Size"/> from (<see cref="X0"/>,
    /// <see cref="Z0"/>).
    /// </summary>
    public sealed class GenWindow
    {
        /// <summary>Absolute position of local (0, 0).</summary>
        public double X0, Z0;
        /// <summary>Edge length (metres).</summary>
        public float Size;
        /// <summary>Height sample spacing (metres).</summary>
        public float Spacing;
        /// <summary>Normalised heights (÷ heightRange), [z, x].</summary>
        public float[,] Map;
        /// <summary>Parameters for the shared stages.</summary>
        public WorldGenParams Params;
        /// <summary>Road centre line in local coordinates (2 m steps), covering the window and 60 m beyond.</summary>
        public List<Vector3> RoadLine;
        /// <summary>Road distances of <see cref="RoadLine"/> points.</summary>
        public List<double> RoadS;
        /// <summary>Distance field on the land-cover grid.</summary>
        public RoadDistanceField Roads;
        /// <summary>Land cover (null until <see cref="BuildCover"/>).</summary>
        public LandCover Cover;
        /// <summary>Lake level in the window or -1.</summary>
        public float WaterLevel = -1f;
        /// <summary>Talus of the pass walls at the height samples (scree fans, aprons), [z, x]; 0 elsewhere.</summary>
        public float[,] Talus;
        /// <summary>Gully weight of the pass walls at the height samples, [z, x]; 0 elsewhere.</summary>
        public float[,] Gully;

        /// <summary>Local float of an absolute x.</summary>
        public float LocalX(double x) => (float)(x - X0);

        /// <summary>Local float of an absolute z.</summary>
        public float LocalZ(double z) => (float)(z - Z0);

        /// <summary>
        /// Samples heights for a window at (<paramref name="x0"/>, <paramref name="z0"/>) of <paramref name="cells"/> cells of
        /// <paramref name="spacing"/> metres; absolute sample positions are x0 + i·spacing exactly (multiples of the spacing),
        /// so shared samples of neighbouring windows are bit-identical.
        /// </summary>
        public static GenWindow Create(EndlessParams p, TerrainField field, RoadCurve road, double x0, double z0, int cells, float spacing, float waterLevel)
        {
            var w = new GenWindow { X0 = x0, Z0 = z0, Spacing = spacing, Size = cells * spacing, WaterLevel = waterLevel };
            int n = cells + 1;
            w.Map = new float[n, n];
            w.Talus = new float[n, n];
            w.Gully = new float[n, n];
            float inv = 1f / p.heightRange;
            for (int j = 0; j < n; j++)
            {
                double z = z0 + j * (double)spacing;
                TerrainField.Row row = field.RowAt(z);
                for (int i = 0; i < n; i++)
                {
                    w.Map[j, i] = field.Height(row, x0 + i * (double)spacing, out float talus, out float gully) * inv;
                    w.Talus[j, i] = talus;
                    w.Gully[j, i] = gully;
                }
            }

            w.Params = new WorldGenParams
            {
                seed = p.seed,
                worldSize = w.Size,
                heightmapResolution = n,
                hillAmplitude = p.heightRange,
                roadWidth = p.roadWidth,
                treeLine = EndlessGenerator.TreeLine,
                lakeCoverage = 0f,
                window = new TileWindow(x0, z0),
            };

            // Road centre line over the window's z range (the road is a graph over z) and 60 m beyond.
            w.RoadLine = new List<Vector3>();
            w.RoadS = new List<double>();
            double sA = road.S(z0 - 60.0), sB = road.S(z0 + w.Size + 60.0);
            double first = Math.Floor(sA / 2.0) * 2.0;
            for (double s = first; s <= sB + 2.0; s += 2.0)
            {
                double z = road.ZAt(s);
                double x = road.X(z);
                w.RoadLine.Add(new Vector3((float)(x - x0), 0f, (float)(z - z0)));
                w.RoadS.Add(s);
            }

            return w;
        }

        /// <summary>Builds the road distance field on a grid of <paramref name="resolution"/> texels over the window.</summary>
        public RoadDistanceField DistanceField(int resolution, float maxDistance)
        {
            return RoadDistanceField.Build(new List<IReadOnlyList<Vector3>> { RoadLine }, resolution, Size, maxDistance);
        }

        /// <summary>A distance field for the sub-square starting at local (<paramref name="offset"/>, <paramref name="offset"/>) of <paramref name="size"/> metres.</summary>
        public RoadDistanceField DistanceField(float offset, float size, int resolution, float maxDistance)
        {
            var shifted = new Vector3[RoadLine.Count];
            for (int i = 0; i < shifted.Length; i++) shifted[i] = new Vector3(RoadLine[i].x - offset, 0f, RoadLine[i].z - offset);
            return RoadDistanceField.Build(new List<IReadOnlyList<Vector3>> { shifted }, resolution, size, maxDistance);
        }

        /// <summary>
        /// Fills the land cover on a grid of <paramref name="resolution"/> texels: heights and slopes from the map, region fields
        /// from <paramref name="field"/>, shore from the window's lake level, scree as in the finite world.
        /// </summary>
        public void BuildCover(EndlessParams p, TerrainField field, int resolution)
        {
            LandCover cover = LandCover.Create(resolution, Size, WaterLevel);
            cover.EnableMountain();
            float texel = cover.TexelSize;
            WorldGenParams wp = Params;
            for (int j = 0; j < resolution; j++)
            {
                float lz = (j + 0.5f) * texel;
                TerrainField.Row row = field.RowAt(Z0 + (j + 0.5) * texel);
                for (int i = 0; i < resolution; i++)
                {
                    float lx = (i + 0.5f) * texel;
                    int k = j * resolution + i;
                    float h = HeightmapGenerator.SampleHeight(Map, wp, lx, lz);
                    float slope = HeightmapGenerator.SampleSlope(Map, wp.worldSize, wp.hillAmplitude, lx, lz);
                    cover.Height[k] = h;
                    cover.Slope[k] = slope;
                    field.Cover(row, X0 + (i + 0.5) * texel, h, slope, out float forest, out float highland, out float moist, out float farm, out float mountain);
                    cover.Mountain[k] = mountain;
                    if (mountain > 0f)
                    {
                        cover.Talus[k] = SampleGrid(Talus, lx, lz);
                        cover.Gully[k] = SampleGrid(Gully, lx, lz);
                    }

                    cover.Forest[k] = forest;
                    cover.Highland[k] = highland;
                    cover.Moisture[k] = moist;
                    cover.Farm[k] = farm;
                    cover.Shore[k] = field.Shore(row, X0 + (i + 0.5) * texel, h);
                }
            }

            cover.ComputeScree(p.heightRange);
            Cover = cover;
            // In the pass ranges the scatter places their own grey rocks instead of the Meadow ones (one rock look per range).
            Params.window.RockFamily = (family, x, z) => TileGenerator.PassRockFamily(family, cover.Mountain[cover.Index(x, z)]);
        }

        /// <summary>A grid laid out like <see cref="Map"/> (samples every <see cref="Spacing"/> from local 0), bilinear at local (x, z).</summary>
        public float SampleGrid(float[,] grid, float x, float z)
        {
            int n = grid.GetLength(0);
            float gx = Mathf.Clamp(x / Spacing, 0f, n - 1.001f), gz = Mathf.Clamp(z / Spacing, 0f, n - 1.001f);
            int i = (int)gx, j = (int)gz;
            float tx = gx - i, tz = gz - j;
            float a = grid[j, i] + (grid[j, i + 1] - grid[j, i]) * tx;
            float b = grid[j + 1, i] + (grid[j + 1, i + 1] - grid[j + 1, i]) * tx;
            return a + (b - a) * tz;
        }
    }
}
