using System;
using System.Collections.Generic;
using System.Diagnostics;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// A small, complete stand-in for the real endless-road generator so the streaming runtime can be built and tested on
    /// its own: a gently winding road <c>x = R(z)</c> with an even grade profile, rolling noise terrain benched around it,
    /// an inn every ~1.5 km (cabin, sign, campfire), milestones every 500 m, one lake beside the road in region 1, a few
    /// trees and seated rocks, grass paint with a dirt road, short grass details, and far tiles. Deterministic from the
    /// seed, order-independent and thread-safe; no native Unity calls (only plain structs such as Vector3).
    /// </summary>
    public sealed class StubEndlessGenerator : IEndlessGenerator
    {
        private const double ArcStep = 2.0;          // z step of the arc-length table
        private const int BlockSteps = 128;          // samples per table block (256 m of z)
        private const double InnSpacing = 1500.0;
        private const double InnJitter = 200.0;
        private const double MilestoneSpacing = 500.0;
        private const float InnOffset = 16f;         // cabin centre, metres to the right of the road
        private const float InnFlatRadius = 24f;
        private const float LakeOffset = 140f;
        private const float LakeRadius = 75f;

        private static readonly string[] NameStarts = { "Wex", "Ash", "Bram", "Cal", "Dun", "Elm", "Fen", "Gil", "Hart", "Ivy", "Kel", "Lark", "Mor", "Oak", "Ry", "Thorn" };
        private static readonly string[] NameEnds = { "moor", "ford", "ley", "wick", "dale", "mere", "stead", "holt", "combe", "well" };
        private static readonly string[] InnAdjectives = { "Drowsy", "Crooked", "Golden", "Wandering", "Quiet", "Old", "Laughing", "Green" };
        private static readonly string[] InnNouns = { "Fox", "Pike", "Lantern", "Hare", "Kettle", "Plough", "Heron", "Barrel" };

        private readonly EndlessParams p;
        private readonly PaletteKeys keys;
        private readonly double phase1, phase2, phase3, phase4;
        private readonly object gate = new object();
        private readonly List<double[]> forward = new List<double[]>();    // block k >= 0: z in [k*256, (k+1)*256]
        private readonly List<double[]> backward = new List<double[]>();   // block k >= 0: z in [-(k+1)*256, -k*256]

        /// <summary>Creates the stub for <paramref name="parameters"/>; <paramref name="paletteKeys"/> says which prefab families exist.</summary>
        public StubEndlessGenerator(EndlessParams parameters, PaletteKeys paletteKeys)
        {
            p = parameters ?? new EndlessParams();
            keys = paletteKeys ?? PaletteKeys.Default;
            var rng = new System.Random(p.seed * 7919 + 13);
            phase1 = rng.NextDouble() * Math.PI * 2.0;
            phase2 = rng.NextDouble() * Math.PI * 2.0;
            phase3 = rng.NextDouble() * Math.PI * 2.0;
            phase4 = rng.NextDouble() * Math.PI * 2.0;
        }

        /// <inheritdoc />
        public EndlessParams Params => p;

        // ------------------------------------------------------------------ road

        /// <summary>Road centre X at absolute Z.</summary>
        private double R(double z) => 110.0 * Math.Sin(z / 650.0 + phase1) + 22.0 * Math.Sin(z / 140.0 + phase2);

        private double RPrime(double z) => 110.0 / 650.0 * Math.Cos(z / 650.0 + phase1) + 22.0 / 140.0 * Math.Cos(z / 140.0 + phase2);

        /// <summary>Road surface height at road distance s (grade at most ~6 %).</summary>
        private double E(double s) => 80.0 + 30.0 * Math.Sin(s / 1100.0 + phase3) + 8.0 * Math.Sin(s / 260.0 + phase4);

        private double EPrime(double s) => 30.0 / 1100.0 * Math.Cos(s / 1100.0 + phase3) + 8.0 / 260.0 * Math.Cos(s / 260.0 + phase4);

        private double ArcIntegrand(double z)
        {
            double d = RPrime(z);
            return Math.Sqrt(1.0 + d * d);
        }

        /// <summary>Arc-length block for table index (k, forward?) — computed outward from z = 0 on demand.</summary>
        private double[] Block(int k, bool positive)
        {
            lock (gate)
            {
                List<double[]> list = positive ? forward : backward;
                while (list.Count <= k)
                {
                    int index = list.Count;
                    double start = index == 0 ? 0.0 : positive ? list[index - 1][BlockSteps] : list[index - 1][BlockSteps];
                    var block = new double[BlockSteps + 1];
                    block[0] = start;
                    double z0 = positive ? index * BlockSteps * ArcStep : -index * BlockSteps * ArcStep;
                    double dir = positive ? 1.0 : -1.0;
                    for (int i = 0; i < BlockSteps; i++)
                    {
                        double a = z0 + dir * i * ArcStep;
                        double b = a + dir * ArcStep;
                        double simpson = ArcStep / 6.0 * (ArcIntegrand(a) + 4.0 * ArcIntegrand((a + b) * 0.5) + ArcIntegrand(b));
                        block[i + 1] = block[i] + dir * simpson;   // backward blocks hold decreasing (negative) s
                    }

                    list.Add(block);
                }

                return list[k];
            }
        }

        /// <summary>Road distance of the road point at absolute Z.</summary>
        private double SAtZ(double z)
        {
            const double blockLength = BlockSteps * ArcStep;
            if (z >= 0.0)
            {
                int k = (int)Math.Floor(z / blockLength);
                double[] block = Block(k, true);
                double local = (z - k * blockLength) / ArcStep;
                int i = Math.Min(BlockSteps - 1, (int)Math.Floor(local));
                return block[i] + (block[i + 1] - block[i]) * (local - i);
            }
            else
            {
                int k = (int)Math.Floor(-z / blockLength);
                double[] block = Block(k, false);
                double local = (-z - k * blockLength) / ArcStep;
                int i = Math.Min(BlockSteps - 1, (int)Math.Floor(local));
                return block[i] + (block[i + 1] - block[i]) * (local - i);
            }
        }

        /// <summary>Absolute Z of the road point at road distance s (inverse of <see cref="SAtZ"/>).</summary>
        private double ZAtS(double s)
        {
            const double blockLength = BlockSteps * ArcStep;
            bool positive = s >= 0.0;
            double target = Math.Abs(s);
            int k = 0;
            double[] block = Block(0, positive);
            while (Math.Abs(block[BlockSteps]) < target)
            {
                k++;
                block = Block(k, positive);
            }

            int lo = 0, hi = BlockSteps;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (Math.Abs(block[mid]) <= target) lo = mid;
                else hi = mid;
            }

            double a = Math.Abs(block[lo]), b = Math.Abs(block[lo + 1]);
            double t = b > a ? (target - a) / (b - a) : 0.0;
            double local = (lo + t) * ArcStep + k * blockLength;
            return positive ? local : -local;
        }

        /// <inheritdoc />
        public RoadPoint Road(double s)
        {
            double z = ZAtS(s);
            double slope = RPrime(z);
            return new RoadPoint
            {
                S = s,
                X = R(z),
                Z = z,
                Height = (float)E(s),
                HeadingDeg = (float)(Math.Atan2(slope, 1.0) * 180.0 / Math.PI),
                Grade = (float)EPrime(s),
            };
        }

        /// <inheritdoc />
        public double NearestS(double x, double z, double hintS)
        {
            // The road is the graph of R(z) with a gentle slope, so the nearest point lies within a few tens of metres of z.
            double bestZ = z, best = double.PositiveInfinity;
            for (double zz = z - 80.0; zz <= z + 80.0; zz += 1.0)
            {
                double dx = x - R(zz), dz = z - zz;
                double d = dx * dx + dz * dz;
                if (d < best)
                {
                    best = d;
                    bestZ = zz;
                }
            }

            double lo = bestZ - 1.0, hi = bestZ + 1.0;
            for (int i = 0; i < 30; i++)
            {
                double m1 = lo + (hi - lo) / 3.0, m2 = hi - (hi - lo) / 3.0;
                double d1 = Sq(x - R(m1)) + Sq(z - m1), d2 = Sq(x - R(m2)) + Sq(z - m2);
                if (d1 < d2) hi = m2;
                else lo = m1;
            }

            return SAtZ((lo + hi) * 0.5);
        }

        // ------------------------------------------------------------------ regions and waypoints

        private static double Hash01(int seed, int a, int b)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)a * 0x85EBCA77u ^ (uint)b * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }

        private double RegionLength(int index) => p.regionLengthMin + (p.regionLengthMax - p.regionLengthMin) * Hash01(p.seed, index, 101);

        private void RegionBounds(int index, out double start, out double end)
        {
            double l0 = RegionLength(0);
            start = -0.3 * l0;
            end = 0.7 * l0;
            if (index > 0)
            {
                for (int i = 1; i <= index; i++)
                {
                    start = end;
                    end = start + RegionLength(i);
                }
            }
            else if (index < 0)
            {
                for (int i = -1; i >= index; i--)
                {
                    end = start;
                    start = end - RegionLength(i);
                }
            }
        }

        private RegionKind KindOf(int index)
        {
            if (index == 0) return RegionKind.Meadow;
            if (index == 1) return RegionKind.LakeCountry;
            RegionKind[] cycle = { RegionKind.Forest, RegionKind.Downs, RegionKind.Farmland, RegionKind.Meadow };
            return cycle[(int)(Hash01(p.seed, index, 202) * cycle.Length) % cycle.Length];
        }

        private string PlaceName(int a, int b)
        {
            string start = NameStarts[(int)(Hash01(p.seed, a, b) * NameStarts.Length) % NameStarts.Length];
            string end = NameEnds[(int)(Hash01(p.seed, a, b + 7) * NameEnds.Length) % NameEnds.Length];
            return start + end;
        }

        /// <inheritdoc />
        public RegionInfo Region(double s)
        {
            int index = 0;
            RegionBounds(0, out double start, out double end);
            while (s >= end)
            {
                index++;
                start = end;
                end = start + RegionLength(index);
            }

            while (s < start)
            {
                index--;
                end = start;
                start = end - RegionLength(index);
            }

            RegionKind kind = KindOf(index);
            string noun;
            switch (kind)
            {
                case RegionKind.Forest: noun = "Woods"; break;
                case RegionKind.LakeCountry: noun = "Lakes"; break;
                case RegionKind.Downs: noun = "Downs"; break;
                case RegionKind.Farmland: noun = "Fields"; break;
                case RegionKind.HighlandPass: noun = "Pass"; break;
                default: noun = "Meadows"; break;
            }

            return new RegionInfo { Index = index, Kind = kind, Name = $"The {PlaceName(index, 303)} {noun}", SStart = start, SEnd = end };
        }

        private double InnS(int k) => k == 0 ? 0.0 : k * InnSpacing + (Hash01(p.seed, k, 404) * 2.0 - 1.0) * InnJitter;

        private double LakeS()
        {
            RegionBounds(1, out double start, out double end);
            return (start + end) * 0.5;
        }

        /// <summary>Absolute point <paramref name="offset"/> metres to the right of the road at road distance s.</summary>
        private (double x, double z) Beside(double s, double offset)
        {
            RoadPoint r = Road(s);
            double h = r.HeadingDeg * Math.PI / 180.0;
            return (r.X + Math.Cos(h) * offset, r.Z - Math.Sin(h) * offset);
        }

        private Waypoint Inn(int k)
        {
            double s = InnS(k);
            RoadPoint r = Road(s);
            (double x, double z) = Beside(s, InnOffset);
            string name = $"The {InnAdjectives[(int)(Hash01(p.seed, k, 505) * InnAdjectives.Length) % InnAdjectives.Length]} " +
                          $"{InnNouns[(int)(Hash01(p.seed, k, 606) * InnNouns.Length) % InnNouns.Length]}";
            return new Waypoint
            {
                Id = WaypointId(WaypointKind.Inn, k), Kind = WaypointKind.Inn, Name = name, S = s, X = x, Z = z,
                Height = r.Height, Radius = 30f, RoadHeadingDeg = r.HeadingDeg,
            };
        }

        private Waypoint Lake()
        {
            double s = LakeS();
            RoadPoint r = Road(s);
            (double x, double z) = Beside(s, LakeOffset);
            return new Waypoint
            {
                Id = WaypointId(WaypointKind.Lake, 1), Kind = WaypointKind.Lake, Name = PlaceName(1, 707) + " Water", S = s, X = x, Z = z,
                Height = LakeLevel(), Radius = LakeRadius, RoadHeadingDeg = r.HeadingDeg,
            };
        }

        private static int WaypointId(WaypointKind kind, int k) => unchecked((k + 1_000_000) * 16 + (int)kind);

        /// <inheritdoc />
        public void Waypoints(double sFrom, double sTo, List<Waypoint> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (sTo < sFrom) return;
            var found = new List<Waypoint>();
            for (int k = (int)Math.Floor(sFrom / InnSpacing) - 1; k <= (int)Math.Ceiling(sTo / InnSpacing) + 1; k++)
            {
                double s = InnS(k);
                if (s >= sFrom && s <= sTo) found.Add(Inn(k));
            }

            for (long m = (long)Math.Ceiling(sFrom / MilestoneSpacing); m * MilestoneSpacing <= sTo; m++)
            {
                double s = m * MilestoneSpacing;
                if (m == 0) continue;
                RoadPoint r = Road(s);
                (double x, double z) = Beside(s, -4.0);
                found.Add(new Waypoint { Id = WaypointId(WaypointKind.Milestone, (int)m), Kind = WaypointKind.Milestone, S = s, X = x, Z = z, Height = r.Height, RoadHeadingDeg = r.HeadingDeg });
            }

            double lakeS = LakeS();
            if (lakeS >= sFrom && lakeS <= sTo) found.Add(Lake());
            found.Sort((a, b) => a.S != b.S ? a.S.CompareTo(b.S) : a.Id.CompareTo(b.Id));
            results.AddRange(found);
        }

        // ------------------------------------------------------------------ terrain

        private float LakeLevel() => (float)E(LakeS()) - 2.5f;

        private (double x, double z) LakeCentre() => Beside(LakeS(), LakeOffset);

        /// <summary>Per-z road facts shared by a row of samples.</summary>
        private struct Row
        {
            public double RoadX;
            public double CosHeading;
            public double S;
            public double RoadHeight;
        }

        private Row RowAt(double z)
        {
            double slope = RPrime(z);
            double s = SAtZ(z);
            return new Row { RoadX = R(z), CosHeading = 1.0 / Math.Sqrt(1.0 + slope * slope), S = s, RoadHeight = E(s) };
        }

        /// <summary>Signed lateral distance from the road centre (positive = right of the direction of travel).</summary>
        private static double Lateral(in Row row, double x) => (x - row.RoadX) * row.CosHeading;

        private double HeightAt(in Row row, double x, double z)
        {
            double d = Lateral(row, x);
            double ad = Math.Abs(d);
            double half = p.roadWidth * 0.5;
            double t = SmoothStep(half + 1.5, half + 32.0, ad);
            double hills = 16.0 * (Fbm(x / 170.0, z / 170.0, 3, 11) - 0.5) * 2.0 + 0.06 * Math.Max(0.0, ad - 30.0);
            double h = row.RoadHeight + t * hills;

            // Inn shelves: flat ground around each cabin.
            int k = (int)Math.Round(row.S / InnSpacing);
            for (int j = k - 1; j <= k + 1; j++)
            {
                double innS = InnS(j);
                if (Math.Abs(innS - row.S) > 60.0) continue;
                (double ix, double iz) = Beside(innS, InnOffset);
                double dist = Math.Sqrt(Sq(x - ix) + Sq(z - iz));
                double w = (1.0 - SmoothStep(InnFlatRadius * 0.6, InnFlatRadius, dist)) * SmoothStep(half + 1.0, half + 5.0, ad);   // never on the road bed
                h += (E(innS) - h) * w;
            }

            // The lake basin.
            double lakeS = LakeS();
            if (Math.Abs(row.S - lakeS) < LakeOffset + LakeRadius * 2.0)
            {
                (double lx, double lz) = LakeCentre();
                double dist = Math.Sqrt(Sq(x - lx) + Sq(z - lz));
                double level = LakeLevel();
                double basin = dist <= LakeRadius ? level - 3.0 * (1.0 - Sq(dist / LakeRadius)) : level + (dist - LakeRadius) * 0.15;
                h = Math.Min(h, basin);
            }

            return Math.Max(0.0, Math.Min(p.heightRange - 1.0, h));
        }

        /// <inheritdoc />
        public float Height(double x, double z) => (float)HeightAt(RowAt(z), x, z);

        /// <inheritdoc />
        public float WaterHeight(double x, double z)
        {
            (double lx, double lz) = LakeCentre();
            return Sq(x - lx) + Sq(z - lz) <= Sq(LakeRadius + 20.0) ? LakeLevel() : -1f;
        }

        // ------------------------------------------------------------------ tiles

        /// <inheritdoc />
        public TileBlueprint GenerateTile(TileCoord coord, TileLod lod)
        {
            var clock = Stopwatch.StartNew();
            bool near = lod == TileLod.Near;
            float ts = p.tileSize;
            int hRes = near ? p.nearHeightResolution : p.farHeightResolution;
            int sRes = near ? p.nearSplatResolution : p.farSplatResolution;
            var bp = new TileBlueprint { Coord = coord, Lod = lod, TileSize = ts, HeightRange = p.heightRange };
            double ox = coord.OriginX(ts), oz = coord.OriginZ(ts);

            // Heights: global sample index so neighbouring tiles compute their shared edge from identical coordinates.
            var heights = new float[hRes, hRes];
            int cells = hRes - 1;
            for (int j = 0; j < hRes; j++)
            {
                double z = ((long)coord.Z * cells + j) * (double)ts / cells;
                Row row = RowAt(z);
                for (int i = 0; i < hRes; i++)
                {
                    double x = ((long)coord.X * cells + i) * (double)ts / cells;
                    heights[j, i] = (float)(HeightAt(row, x, z) / p.heightRange);
                }
            }

            bp.Heights = heights;

            // Water: the lake's level for every tile its basin touches.
            (double lakeX, double lakeZ) = LakeCentre();
            double nx = Math.Max(ox, Math.Min(ox + ts, lakeX)), nz = Math.Max(oz, Math.Min(oz + ts, lakeZ));
            float level = LakeLevel();
            bp.WaterHeight = Sq(nx - lakeX) + Sq(nz - lakeZ) <= Sq(LakeRadius + 5.0) ? level : -1f;

            // Paint.
            int layers = TerrainShaper.LayerCount;
            var splat = new float[sRes, sRes, layers];
            double half = p.roadWidth * 0.5;
            for (int j = 0; j < sRes; j++)
            {
                double z = oz + (j + 0.5) * ts / sRes;
                Row row = RowAt(z);
                for (int i = 0; i < sRes; i++)
                {
                    double x = ox + (i + 0.5) * ts / sRes;
                    double ad = Math.Abs(Lateral(row, x));
                    float lx = (float)((i + 0.5) * ts / sRes), lz = (float)((j + 0.5) * ts / sRes);
                    float h = HeightmapGenerator.SampleHeight(heights, ts, p.heightRange, lx, lz);
                    float slope = HeightmapGenerator.SampleSlope(heights, ts, p.heightRange, lx, lz);

                    float dirt = (float)(1.0 - SmoothStep(half - 0.5, half + 1.0, ad));
                    float rock = (float)SmoothStep(0.58, 0.84, slope);
                    float mud = bp.WaterHeight >= 0f ? (float)(1.0 - SmoothStep(0.3, 1.2, h - level)) : 0f;
                    float dry = (float)SmoothStep(0.45, 0.6, Fbm(x / 90.0, z / 90.0, 2, 23));
                    float flowers = (float)SmoothStep(0.62, 0.72, Fbm(x / 40.0, z / 40.0, 2, 29)) * 0.6f;

                    float rest = 1f;
                    float wDirt = dirt;
                    rest -= wDirt;
                    float wRock = Math.Min(rest, rock * rest);
                    rest -= wRock;
                    float wMud = Math.Min(rest, mud * rest);
                    rest -= wMud;
                    float wFlowers = flowers * rest;
                    rest -= wFlowers;
                    splat[j, i, TerrainShaper.DirtLayer] = wDirt;
                    splat[j, i, TerrainShaper.RockLayer] = wRock;
                    splat[j, i, TerrainShaper.MudLayer] = wMud;
                    splat[j, i, TerrainShaper.FlowerLayer] = wFlowers;
                    splat[j, i, TerrainShaper.DryGrassLayer] = rest * dry;
                    splat[j, i, TerrainShaper.LushGrassLayer] = rest * (1f - dry);
                }
            }

            bp.Splat = splat;

            var rng = new System.Random(unchecked(p.seed * 31 + coord.X * 7477 + coord.Z * 104729));
            PlaceTrees(bp, coord, heights, rng, level);
            if (near)
            {
                PlaceDetails(bp, coord, heights, level);
                PlaceRocks(bp, coord, heights, rng, level);
                PlaceInns(bp, coord);
            }

            // Waypoints whose centre lies in this tile.
            var all = new List<Waypoint>();
            double sMid = NearestS(ox + ts * 0.5, oz + ts * 0.5, 0.0);
            Waypoints(sMid - ts * 2.0, sMid + ts * 2.0, all);
            foreach (Waypoint w in all)
            {
                if (w.X >= ox && w.X < ox + ts && w.Z >= oz && w.Z < oz + ts) bp.Waypoints.Add(w);
            }

            bp.GenerationMs = clock.Elapsed.TotalMilliseconds;
            return bp;
        }

        private void PlaceTrees(TileBlueprint bp, TileCoord coord, float[,] heights, System.Random rng, float level)
        {
            string role = keys.Has(PropRole.MeadowTree) ? PropRole.MeadowTree : keys.Has(PropRole.ForestTree) ? PropRole.ForestTree : null;
            if (role == null) return;
            float ts = p.tileSize;
            double ox = coord.OriginX(ts), oz = coord.OriginZ(ts);
            int count = 6 + rng.Next(10);
            for (int n = 0; n < count; n++)
            {
                float lx = (float)rng.NextDouble() * ts, lz = (float)rng.NextDouble() * ts;
                int variant = keys.PickVariant(role, rng);
                float yaw = (float)rng.NextDouble() * 360f;
                float scale = 0.8f + (float)rng.NextDouble() * 0.5f;
                double x = ox + lx, z = oz + lz;
                Row row = RowAt(z);
                if (Math.Abs(Lateral(row, x)) < 12.0) continue;
                float h = HeightmapGenerator.SampleHeight(heights, ts, p.heightRange, lx, lz);
                if (bp.WaterHeight >= 0f && h < level + 0.5f) continue;
                if (HeightmapGenerator.SampleSlope(heights, ts, p.heightRange, lx, lz) > 0.6f) continue;
                if (NearInn(x, z, 26.0)) continue;
                bp.Trees.Add(new TreePlacement { role = role, variant = variant, position = new Vector3(lx, h, lz), yaw = yaw, scale = scale });
            }
        }

        private void PlaceDetails(TileBlueprint bp, TileCoord coord, float[,] heights, float level)
        {
            int res = p.nearDetailResolution;
            float ts = p.tileSize;
            double ox = coord.OriginX(ts), oz = coord.OriginZ(ts);
            bp.DetailResolution = res;
            string[] families = { PropRole.ShortGrass, PropRole.MedGrass, PropRole.Wildflowers };
            byte[][] maps = new byte[families.Length][];
            for (int f = 0; f < families.Length; f++)
            {
                if (keys.DetailCount(families[f]) > 0) maps[f] = new byte[res * res];
            }

            double half = p.roadWidth * 0.5;
            for (int j = 0; j < res; j++)
            {
                double z = oz + (j + 0.5) * ts / res;
                Row row = RowAt(z);
                for (int i = 0; i < res; i++)
                {
                    double x = ox + (i + 0.5) * ts / res;
                    double ad = Math.Abs(Lateral(row, x));
                    if (ad < half + 1.0) continue;
                    float lx = (float)((i + 0.5) * ts / res), lz = (float)((j + 0.5) * ts / res);
                    float h = HeightmapGenerator.SampleHeight(heights, ts, p.heightRange, lx, lz);
                    if (bp.WaterHeight >= 0f && h < level + 0.3f) continue;
                    if (HeightmapGenerator.SampleSlope(heights, ts, p.heightRange, lx, lz) > 0.7f) continue;
                    float verge = (float)SmoothStep(half + 1.0, half + 4.0, ad);
                    double n = Fbm(x / 35.0, z / 35.0, 2, 41);
                    if (maps[0] != null) maps[0][j * res + i] = (byte)Math.Round(verge * (3.0 + 4.0 * n));
                    if (maps[1] != null) maps[1][j * res + i] = (byte)Math.Round(verge * Math.Max(0.0, 4.0 * (n - 0.45)));
                    if (maps[2] != null) maps[2][j * res + i] = (byte)(Fbm(x / 25.0, z / 25.0, 2, 47) > 0.66 ? 1 : 0);
                }
            }

            for (int f = 0; f < families.Length; f++)
            {
                if (maps[f] == null) continue;
                bp.Details.Add(new DetailLayer { family = families[f], variant = 0, density = maps[f] });
            }
        }

        private void PlaceRocks(TileBlueprint bp, TileCoord coord, float[,] heights, System.Random rng, float level)
        {
            string family = keys.HasMetrics(PropRole.LowlandRock) ? PropRole.LowlandRock : keys.HasMetrics(PropRole.Rock) ? PropRole.Rock : null;
            if (family == null) return;
            float ts = p.tileSize;
            double ox = coord.OriginX(ts), oz = coord.OriginZ(ts);
            var gp = new WorldGenParams { seed = p.seed, worldSize = ts, heightmapResolution = heights.GetLength(0), hillAmplitude = p.heightRange };
            var ctx = new RockPlacer.Context(gp, heights, keys, (x, z) =>
            {
                double ax = ox + x, az = oz + z;
                return Math.Abs(Lateral(RowAt(az), ax)) > 10.0 && !NearInn(ax, az, 30.0) && WaterHeight(ax, az) < 0f;
            });

            int count = 2 + rng.Next(3);
            for (int n = 0; n < count; n++)
            {
                float lx = 4f + (float)rng.NextDouble() * (ts - 8f), lz = 4f + (float)rng.NextDouble() * (ts - 8f);
                int variant = keys.PickVariant(family, rng);
                float target = 0.7f + (float)rng.NextDouble() * 1.2f;
                float yaw = (float)rng.NextDouble() * 360f;
                if (!ctx.Clear(lx, lz)) continue;
                if (RockPlacer.TryPlace(ctx, family, variant, lx, lz, target, yaw, 0f, out PropPlacement placement, out _)) bp.Props.Add(placement);
            }
        }

        private bool NearInn(double x, double z, double radius)
        {
            double s = SAtZ(z);
            int k = (int)Math.Round(s / InnSpacing);
            for (int j = k - 1; j <= k + 1; j++)
            {
                (double ix, double iz) = Beside(InnS(j), InnOffset);
                if (Sq(x - ix) + Sq(z - iz) < radius * radius) return true;
            }

            return false;
        }

        private void PlaceInns(TileBlueprint bp, TileCoord coord)
        {
            float ts = p.tileSize;
            double ox = coord.OriginX(ts), oz = coord.OriginZ(ts);
            double sMid = NearestS(ox + ts * 0.5, oz + ts * 0.5, 0.0);
            int k0 = (int)Math.Floor((sMid - ts * 2.0) / InnSpacing) - 1, k1 = (int)Math.Ceiling((sMid + ts * 2.0) / InnSpacing) + 1;
            for (int k = k0; k <= k1; k++)
            {
                Waypoint inn = Inn(k);
                if (inn.X < ox || inn.X >= ox + ts || inn.Z < oz || inn.Z >= oz + ts) continue;
                float h = (float)E(inn.S);
                float heading = inn.RoadHeadingDeg;
                var centre = new Vector3((float)(inn.X - ox), h, (float)(inn.Z - oz));

                // The cabin's door (+Z of the placement) faces the road, which lies to its left: yaw = heading - 90.
                if (keys.Has(PropRole.Cabin))
                {
                    bp.Props.Add(new PropPlacement { role = PropRole.Cabin, variant = 0, position = centre, yaw = heading - 90f, scale = 1f, settlementId = inn.Id });
                }

                (double sx, double sz) = Beside(inn.S - 8.0, 4.5);
                if (keys.Has(PropRole.Sign))
                {
                    bp.Props.Add(new PropPlacement
                    {
                        role = PropRole.Sign, variant = 0, position = new Vector3((float)(sx - ox), (float)E(inn.S - 8.0), (float)(sz - oz)),
                        yaw = heading, scale = 1f, settlementId = inn.Id,
                    });
                }

                if (keys.Has(PropRole.Fireplace))
                {
                    double hr = heading * Math.PI / 180.0;
                    var fire = centre + new Vector3((float)Math.Sin(hr), 0f, (float)Math.Cos(hr)) * 9f;
                    bp.Props.Add(new PropPlacement { role = PropRole.Fireplace, variant = 0, position = fire, yaw = 0f, scale = 1f, settlementId = inn.Id });
                }
            }
        }

        // ------------------------------------------------------------------ noise

        private double Fbm(double x, double z, int octaves, int salt)
        {
            double sum = 0.0, amplitude = 0.5, total = 0.0;
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * ValueNoise(x, z, salt + o * 17);
                total += amplitude;
                x *= 2.03;
                z *= 2.03;
                amplitude *= 0.5;
            }

            return sum / total;
        }

        private double ValueNoise(double x, double z, int salt)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            double fx = x - ix, fz = z - iz;
            double u = fx * fx * (3.0 - 2.0 * fx), v = fz * fz * (3.0 - 2.0 * fz);
            double a = Hash01(p.seed + salt, ix, iz), b = Hash01(p.seed + salt, ix + 1, iz);
            double c = Hash01(p.seed + salt, ix, iz + 1), d = Hash01(p.seed + salt, ix + 1, iz + 1);
            return (a + (b - a) * u) + ((c + (d - c) * u) - (a + (b - a) * u)) * v;
        }

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Math.Max(0.0, Math.Min(1.0, (x - edge0) / (edge1 - edge0)));
            return t * t * (3.0 - 2.0 * t);
        }

        private static double Sq(double v) => v * v;
    }
}
