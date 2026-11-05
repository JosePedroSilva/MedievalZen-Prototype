using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// What a tile does with the streams that cross it: keeps scatter and grass out of their channels, paints the ground around
    /// them, places their rocks, reeds, the stepping stones of the fords and the footbridges, and builds the drawn stream (water,
    /// a wet lip, a bank rising to the ground and a strip draped over the ground) as one mesh with its water ribbons, cut at the
    /// tile's edges. Every choice is a function of the stream and absolute position, so neighbouring windows agree. Pure;
    /// thread-safe.
    /// <para>
    /// Why a mesh (2026-10-02): a near tile's height map has a sample every 2 m, too coarse for a brook 3-5 m wide. A flat water
    /// ribbon over carved ground showed wherever that coarse ground crossed the water plane: a jagged, stair-stepped edge, ground
    /// poking through a third of the water. Now the visible edges are the mesh's own (a smoothed centre line every metre), and the
    /// ground is carved just below the mesh over its whole footprint (<see cref="StreamNetwork.Carve"/>).
    /// </para>
    /// </summary>
    public static class TileStreams
    {
        /// <summary>The bridge's pivot sits this far below the road so the deck's flat ends (0.24 m above the pivot, scaled) meet the road flush.</summary>
        public const float BridgeSink = BridgeShape.Sink;

        /// <summary>Randomness stream of the stones and reeds at a bridge's abutments.</summary>
        public const int AbutmentStream = 1381;

        /// <summary>The drawn bank fades in over this length beyond the road's part of a stream, after its source and before its end, metres.</summary>
        public const float BankFadeIn = 2.5f;

        /// <summary>Columns of the water strip across a section (edge, half, centre, half, edge).</summary>
        public const int WaterColumns = 5;

        /// <summary>Columns of each bank across a section (from under the water's edge out to the skirt under the ground).</summary>
        public const int BankColumns = 13;

        /// <summary>Vertices of one section of the drawn stream.</summary>
        public const int SectionVertices = WaterColumns + 2 * BankColumns;

        /// <summary>The skirt (the bank's last column) lies this far under the ground; a fading bank sinks a little further.</summary>
        public const float SkirtDepth = 0.08f;

        /// <summary>A rock in the water (or a stepping stone), for the foam around it.</summary>
        /// <summary>
        /// A placed bridge: absolute centre (within a few decimetres of the road: the bridge lies along the road's chord over its length),
        /// unit direction of its axis, pivot height (see <see cref="BridgeShape"/>).
        /// </summary>
        public struct BridgeDeck
        {
            public double X, Z;
            public float DirX, DirZ, PivotY;
        }

        /// <summary>
        /// Half width of the road-coloured surface on a bridge's deck: the road's own 5 m (the deck is 2.80 m a side between the
        /// first-party parapets, <see cref="BridgeWalls.InnerFace"/>; was 2.3 m before the bridge was widened), metres.
        /// </summary>
        public const float DeckHalf = 2.5f;

        /// <summary>The deck's road surface lies this far above the bridge's stone deck, metres.</summary>
        public const float DeckLift = 0.04f;

        /// <summary>Sections of the deck's road surface every this many metres along the road.</summary>
        public const float DeckStep = 0.5f;

        /// <summary>
        /// Absolute positions of the road surface over a bridge's deck (<paramref name="d"/>): sections every <see cref="DeckStep"/>
        /// along the road from end to end, five columns across ±<see cref="DeckHalf"/>, at the deck's hump (<see cref="BridgeShape.DeckHeight"/>)
        /// + <see cref="DeckLift"/>; meets the road at both ends. Row-major (section, column). Pure.
        /// </summary>
        public static List<(double x, float y, double z)> DeckSurface(BridgeDeck d, out int sections)
        {
            var result = new List<(double, float, double)>();
            sections = (int)Math.Round(2f * BridgeShape.HalfLength / DeckStep) + 1;
            double rx = d.DirZ, rz = -d.DirX;
            for (int k = 0; k < sections; k++)
            {
                float along = -BridgeShape.HalfLength + k * DeckStep;
                along = Math.Min(along, BridgeShape.HalfLength);
                float h = BridgeShape.DeckHeight(along);
                if (float.IsNaN(h)) h = BridgeShape.Sink;
                for (int col = 0; col < 5; col++)
                {
                    double u = DeckHalf * (col - 2) * 0.5;
                    result.Add((d.X + d.DirX * along + rx * u, d.PivotY + h + DeckLift, d.Z + d.DirZ * along + rz * u));
                }
            }

            return result;
        }

        public struct WaterRock
        {
            /// <summary>Absolute centre.</summary>
            public double X, Z;
            /// <summary>Radius of its footprint, metres.</summary>
            public float Radius;
        }

        // ------------------------------------------------------------------ the ground around a tile's streams

        /// <summary>
        /// The ground of a tile's window around its streams: the final heights, the heights without the stream channels (the
        /// ground the banks are draped over) and the surface things stand on (the drawn bank where it lies above the ground).
        /// </summary>
        public sealed class Ground
        {
            /// <summary>The window.</summary>
            public GenWindow W;
            /// <summary>The streams that reach into the window.</summary>
            public List<StreamCourse> Courses;
            /// <summary>Their pieces (lookups).</summary>
            public StreamPiece[] Pieces;
            /// <summary>Final heights of the window's samples, metres [z, x].</summary>
            public float[,] Final;
            /// <summary>
            /// Heights with the streams' channels but without the carving under the drawn streams, metres [z, x]: the ground the banks
            /// are draped over (equal to <see cref="Final"/> away from the streams).
            /// </summary>
            public float[,] Bare;
            /// <summary>Normalised like <see cref="GenWindow.Map"/>: the higher of the ground and the drawn bank at each sample (for seating rocks).</summary>
            public float[,] Drawn;
            /// <summary>Bridges placed by <see cref="Props"/> in this window: their decks get a road-coloured surface in <see cref="Mesh"/>.</summary>
            public List<BridgeDeck> Decks = new List<BridgeDeck>();

            /// <summary>The lowest the terrain can be drawn at absolute (x, z) (<see cref="Final"/>, either split of its grid cell), metres.</summary>
            public float LowestGround(double x, double z)
            {
                Rendered(Final, x, z, out float lo, out _);
                return lo;
            }

            /// <summary>The rendered height of <paramref name="map"/> at absolute (x, z): lowest and highest of the two ways a grid cell can be split into triangles.</summary>
            public void Rendered(float[,] map, double x, double z, out float lo, out float hi)
            {
                int n = map.GetLength(0);
                double gx = (x - W.X0) / W.Spacing, gz = (z - W.Z0) / W.Spacing;
                int x0 = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(gx))), z0 = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(gz)));
                double fx = Math.Max(0.0, Math.Min(1.0, gx - x0)), fz = Math.Max(0.0, Math.Min(1.0, gz - z0));
                double a = map[z0, x0], b = map[z0, x0 + 1], c = map[z0 + 1, x0], d = map[z0 + 1, x0 + 1];
                double h1 = fx >= fz ? a + (b - a) * fx + (d - b) * fz : a + (c - a) * fz + (d - c) * fx;
                double h2 = fx + fz <= 1.0 ? a + (b - a) * fx + (c - a) * fz : d + (c - d) * (1.0 - fx) + (b - d) * (1.0 - fz);
                lo = (float)Math.Min(h1, h2);
                hi = (float)Math.Max(h1, h2);
            }

            /// <summary>The surface at absolute (x, z) that a plant or stone stands on: the drawn bank where it is above the ground, else the ground (highest triangulation).</summary>
            public float SurfaceAt(double x, double z)
            {
                Rendered(Final, x, z, out float lo, out float hi);
                StreamSample s = StreamNetwork.Nearest(Pieces, x, z);
                if (s.Course == null || s.Distance > s.Outer) return hi;
                float half = VisibleHalf(s.Course, s.Along, s.FromCrossing, s.HalfWater, s.HalfRibbon);
                float beyond = s.Distance - half;
                if (beyond < -StreamNetwork.BankUnderWater) return hi;
                Rendered(Bare, x, z, out _, out float bare);
                float bank = BankHeight(beyond, s.Surface, bare, lo, Fade(s.Course, s.Along, s.FromCrossing), false, s.BankTop);
                return Math.Max(hi, bank);
            }
        }

        /// <summary>Prepares the ground of window <paramref name="w"/> around <paramref name="courses"/> (heights of <paramref name="field"/>, metres).</summary>
        public static Ground Prepare(GenWindow w, TerrainField field, List<StreamCourse> courses, float heightRange)
        {
            int n = w.Map.GetLength(0);
            var pieces = new List<StreamPiece>();
            foreach (StreamCourse c in courses) pieces.AddRange(c.Pieces);
            if (pieces.Count == 0) return new Ground { W = w, Courses = courses, Pieces = Array.Empty<StreamPiece>(), Final = new float[2, 2], Bare = new float[2, 2], Drawn = w.Map };
            var g = new Ground { W = w, Courses = courses, Pieces = pieces.ToArray(), Final = new float[n, n], Bare = new float[n, n], Drawn = (float[,])w.Map.Clone() };
            for (int j = 0; j < n; j++)
            {
                double z = w.Z0 + j * (double)w.Spacing;
                TerrainField.Row row = null;
                for (int i = 0; i < n; i++)
                {
                    float final = w.Map[j, i] * heightRange;
                    g.Final[j, i] = final;
                    g.Bare[j, i] = final;
                    if (g.Pieces.Length == 0) continue;
                    double x = w.X0 + i * (double)w.Spacing;
                    StreamSample s = StreamNetwork.Nearest(g.Pieces, x, z);
                    // The mesh reads this map within its outer edge, from the samples of the cells there (2.83 m) plus its envelope.
                    if (s.Course == null || s.Distance > s.Outer + 3.6f) continue;
                    row ??= field.RowAt(z);
                    g.Bare[j, i] = Math.Min(field.HeightWithoutStreams(row, x), StreamNetwork.ChannelCut(row.Streams, x, z));
                }
            }

            // The surface things stand on: the drawn bank where it is above the ground (at the samples).
            for (int j = 0; j < n; j++)
            {
                double z = w.Z0 + j * (double)w.Spacing;
                for (int i = 0; i < n; i++)
                {
                    if (g.Pieces.Length == 0) break;
                    double x = w.X0 + i * (double)w.Spacing;
                    StreamSample s = StreamNetwork.Nearest(g.Pieces, x, z);
                    if (s.Course == null || s.Distance > s.Outer) continue;
                    float half = VisibleHalf(s.Course, s.Along, s.FromCrossing, s.HalfWater, s.HalfRibbon);
                    float beyond = s.Distance - half;
                    if (beyond < -StreamNetwork.BankUnderWater) continue;
                    float bank = BankHeight(beyond, s.Surface, g.Bare[j, i], g.Final[j, i], Fade(s.Course, s.Along, s.FromCrossing), false, s.BankTop);
                    if (bank > g.Final[j, i]) g.Drawn[j, i] = bank / heightRange;
                }
            }

            return g;
        }

        // ------------------------------------------------------------------ the drawn stream's shape

        /// <summary>
        /// How much of the drawn bank shows at <paramref name="along"/> (0 where it is sunk under the ground: in the road's part of
        /// the stream, at its source and its end; 1 elsewhere).
        /// </summary>
        public static float Fade(StreamCourse c, float along, float fromCrossing)
        {
            // A ford keeps its drawn banks across the road (draped over the road bed, see Positions): the mesh makes the water's edge.
            double road = IsFord(c) ? 1.0 : EndlessNoise.Smooth(c.RoadZone, c.RoadZone + c.RoadFade, Math.Abs(fromCrossing));
            double source = EndlessNoise.Smooth(0.0, BankFadeIn + 2.0, along);
            double end = EndlessNoise.Smooth(0.0, c.EndsInLake ? 14.0 : BankFadeIn + 2.0, c.Length - along);
            return (float)(road * source * end);
        }

        /// <summary>
        /// Half width of the drawn water at <paramref name="along"/>: the channel's water (<paramref name="halfWater"/>), narrowing to
        /// a point at the source and at an end that is not a lake; in the road's part of the stream the old ribbon width
        /// (<paramref name="halfRibbon"/>, tucked under the road's banks, which make the edge there).
        /// </summary>
        public static float VisibleHalf(StreamCourse c, float along, float fromCrossing, float halfWater, float halfRibbon)
        {
            float half = halfWater * (float)EndlessNoise.Smooth(-1.0, 4.0, along);
            if (!c.EndsInLake) half *= (float)EndlessNoise.Smooth(-1.0, 4.0, c.Length - along);
            float road = (float)EndlessNoise.Smooth(c.RoadZone, c.RoadZone + c.RoadFade, Math.Abs(fromCrossing));
            if (IsFord(c))
            {
                // Across the road a ford's water is shallow and narrower than its pools, its edge drawn by the mesh (2026-10-06).
                float narrow = Math.Min(half, Math.Max(FordMinHalf, half * FordNarrowing));
                return narrow + (half - narrow) * road;
            }

            return halfRibbon + (half - halfRibbon) * road;
        }

        /// <summary>Whether <paramref name="c"/> crosses the road at a ford.</summary>
        public static bool IsFord(StreamCourse c) => c.Crossing != null && c.Crossing.Kind == CrossingKind.Ford;

        /// <summary>A ford's water across the road is this fraction of the channel's water, at least <see cref="FordMinHalf"/> a side.</summary>
        public const float FordNarrowing = 0.7f;

        /// <summary>Least half width of a ford's water across the road, metres.</summary>
        public const float FordMinHalf = 0.8f;

        /// <summary>Over a ford's road bed the drawn bank lies this far above the ground (the ground is not carved there), metres.</summary>
        public const float FordBedLift = 0.03f;

        /// <summary>Weight of a ford's road part at <paramref name="fromCrossing"/> (1 on the road, 0 beyond its fade); 0 for a bridge.</summary>
        public static float FordRoad(StreamCourse c, float fromCrossing) =>
            IsFord(c) ? 1f - (float)EndlessNoise.Smooth(c.RoadZone, c.RoadZone + c.RoadFade, Math.Abs(fromCrossing)) : 0f;

        /// <summary>
        /// Upward-only hummocks on the drawn bank between the lip and the top (0 at both, so the edge and the drape are unchanged
        /// and the ground stays below), varying along the stream and per side: an irregular bank rather than an extruded profile.
        /// Absolute along and the crossing's seed only, so neighbouring tiles agree.
        /// </summary>
        public static float Hummock(int seed, int side, float along, float x, float top)
        {
            if (x <= 0.2f || x >= top) return 0f;
            double u = (x - 0.2) / Math.Max(0.05, top - 0.2);
            double window = Math.Sin(Math.PI * u);
            double a = Noise1(seed, HummockStream + side, along / 2.3);
            double b = Noise1(seed, HummockStream + 2 + side, along / 0.9 + x * 0.7);
            return (float)(HummockHeight * window * window * (0.7 * a + 0.3 * b));
        }

        /// <summary>Tallest hummock on a bank, metres.</summary>
        public const float HummockHeight = 0.16f;

        /// <summary>Randomness streams of the bank hummocks (1382–1385).</summary>
        public const int HummockStream = 1382;

        /// <summary>Smooth 1-D value noise 0..1 of <paramref name="t"/> (cells of 1) from hashes of <paramref name="seed"/>, <paramref name="stream"/>.</summary>
        public static double Noise1(int seed, int stream, double t)
        {
            double f = Math.Floor(t);
            long i = (long)f;
            double u = t - f;
            u = u * u * (3.0 - 2.0 * u);
            double h0 = EndlessNoise.Unit(seed, stream, i), h1 = EndlessNoise.Unit(seed, stream, i + 1);
            return h0 + (h1 - h0) * u;
        }

        /// <summary>
        /// Height of the drawn bank <paramref name="x"/> metres beyond the water's edge (negative: under the water) for water at
        /// <paramref name="surface"/>, ground without the channel <paramref name="bare"/> and final ground <paramref name="final"/> there:
        /// the rising bank (<see cref="StreamNetwork.BankRise"/>) until it meets the ground, then the ground itself (beyond
        /// <paramref name="top"/>, where the ground is only kept a margin below the mesh, the bank steepens sharply so it meets the ground
        /// at once); a low levee where the ground is lower than the water; under the ground (by <see cref="SkirtDepth"/>) as a skirt,
        /// and sinking under it as <paramref name="fade"/> goes to 0.
        /// </summary>
        public static float BankHeight(float x, float surface, float bare, float final, float fade, bool skirt, float top)
        {
            float h;
            if (skirt) h = final - SkirtDepth;
            else if (x <= 0f) h = surface + StreamNetwork.BankRise(x);
            else
            {
                float rise = surface + StreamNetwork.DrawnRise(x, top);
                h = Math.Max(Math.Min(rise, bare), surface + StreamNetwork.LeveeRise(x));
            }

            if (fade >= 1f) return h;
            float sunk = final - SkirtDepth - 0.04f;
            return sunk + (h - sunk) * fade * fade * (3f - 2f * fade);
        }

        /// <summary>
        /// Stones and reeds where a bridge's faces meet the stream (both faces, both banks): a stone half in the water against each
        /// pier and a clump of reeds on the bank a little further out, so the arch stands in a stream rather than on the grass.
        /// </summary>
        private static void DressAbutments(StreamCourse c, Crossing crossing, GenWindow w, PaletteKeys keys, RockPlacer.Context seating, Ground ground,
            Vector3 tangent, List<PropPlacement> placed, List<WaterRock> waterRocks)
        {
            var rng = new System.Random(EndlessNoise.Seed(crossing.Seed, AbutmentStream, 0));
            int k = c.CrossingIndex;
            float halfWater = c.HalfWater[k], surface = c.Surface[k];
            double ox = c.X[k], oz = c.Z[k];
            foreach (float face in new[] { -1f, 1f })
            {
                foreach (float bank in new[] { -1f, 1f })
                {
                    // Out from the bridge's face along the stream, across it towards one bank (along the road).
                    float outFace = BridgeShape.HalfWidth + 0.5f + 0.4f * (float)rng.NextDouble();
                    float across = Math.Min(halfWater - 0.2f, BridgeShape.ArchHalf - 0.6f);
                    double ax = ox + c.DirX[k] * face * outFace + tangent.x * bank * across, az = oz + c.DirZ[k] * face * outFace + tangent.z * bank * across;
                    if (keys.Has(PropRole.Rock))
                    {
                        int variant = rng.Next(2);
                        ground.Rendered(ground.Final, ax, az, out float bed, out _);
                        float height = Mathf.Clamp(surface - bed + 0.3f, 0.4f, 0.9f);
                        float yaw = (float)(rng.NextDouble() * 360.0);
                        if (RockPlacer.TrySeat(seating, PropRole.Rock, variant, w.LocalX(ax), w.LocalZ(az), yaw, ScaleFor(keys, PropRole.Rock, variant, height), out PropPlacement stone))
                        {
                            stone.settlementId = crossing.Id;
                            placed.Add(stone);
                            waterRocks.Add(new WaterRock { X = ax, Z = az, Radius = 0.35f * height + 0.1f });
                        }
                    }

                    if (keys.Has(PropRole.Reeds))
                    {
                        float reach = halfWater + 0.35f;
                        double rx = ox + c.DirX[k] * face * (outFace + 1.6f) + tangent.x * bank * reach, rz = oz + c.DirZ[k] * face * (outFace + 1.6f) + tangent.z * bank * reach;
                        float lx = w.LocalX(rx), lz = w.LocalZ(rz);
                        if (lx < 1f || lz < 1f || lx > w.Size - 1f || lz > w.Size - 1f) continue;
                        // On the bank, never below the water there.
                        StreamSample at = StreamNetwork.Nearest(ground.Pieces, rx, rz);
                        float y = at.Course != null ? Math.Max(ground.SurfaceAt(rx, rz), at.Surface) : ground.SurfaceAt(rx, rz);
                        placed.Add(new PropPlacement
                        {
                            role = PropRole.Reeds, variant = keys.PickVariant(PropRole.Reeds, rng), position = new Vector3(lx, y, lz),
                            yaw = (float)(rng.NextDouble() * 360.0), scale = 0.85f + 0.3f * (float)rng.NextDouble(), settlementId = -1,
                        });
                    }
                }
            }
        }

        // ------------------------------------------------------------------ occupancy, paint, grass

        /// <summary>Marks the channels and the drawn banks (water plus the bank up to the ground) as occupied so trees and props keep out of them.</summary>
        public static void Occupy(GenWindow w, List<StreamCourse> courses, OccupancyMap occupancy)
        {
            foreach (StreamCourse c in courses)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    for (int half = 0; half < 2; half++)
                    {
                        if (half == 1 && i + 1 >= c.Count) break;
                        double x = half == 0 ? c.X[i] : (c.X[i] + c.X[i + 1]) * 0.5;
                        double z = half == 0 ? c.Z[i] : (c.Z[i] + c.Z[i + 1]) * 0.5;
                        float lx = w.LocalX(x), lz = w.LocalZ(z);
                        if (lx < -16f || lz < -16f || lx > w.Size + 16f || lz > w.Size + 16f) continue;
                        int k = half == 0 ? i : i + 1;
                        float ribbon = half == 0 ? c.HalfRibbon[i] : 0.5f * (c.HalfRibbon[i] + c.HalfRibbon[k]);
                        float bank = half == 0 ? c.HalfWater[i] + c.BankTop[i] : 0.5f * (c.HalfWater[i] + c.BankTop[i] + c.HalfWater[k] + c.BankTop[k]);
                        occupancy.Add(new Vector3(lx, 0f, lz), Math.Max(ribbon + 0.6f, bank + 0.8f));
                    }
                }
            }
        }

        /// <summary>The ground is painted mud this far beyond the water's edge (under the bank's wet lip and flat toe), metres.</summary>
        public const float ToeMud = 0.9f;

        /// <summary>
        /// Paints the ground around the streams into <paramref name="splat"/>, whose texel (0, 0) starts at window position
        /// (<paramref name="offset"/>, <paramref name="offset"/>). Under the water (seen through it): mud with some dirt. Under and
        /// around the drawn bank: lush grass, the bank's own grass, fading into the ground's paint a few metres beyond its edge so the
        /// two meet unseen. In the road's part of a ford: gravel and mud over the road and its verges, wet mud on the banks (the
        /// ground makes the edge there). Weights still sum to 1.
        /// </summary>
        public static void Paint(float[,,] splat, GenWindow w, float offset, List<StreamCourse> courses, GradientNoise noise)
        {
            if (courses.Count == 0) return;
            int res = splat.GetLength(0);
            float texel = (w.Size - 2f * offset) / res;
            int layers = splat.GetLength(2);
            var target = new float[layers];
            var pieces = new List<StreamPiece>();
            foreach (StreamCourse c in courses) pieces.AddRange(c.Pieces);
            StreamPiece[] all = pieces.ToArray();
            double reach = 0.0;
            foreach (StreamCourse c in courses)
            {
                for (int i = 0; i < c.Count; i++) reach = Math.Max(reach, c.Outer[i] + 3.5);
            }

            foreach (StreamCourse c in courses)
            {
                // Texel range of the course's box grown by the reach.
                int x0 = Math.Max(0, (int)Math.Floor((w.LocalX(c.MinX) - reach - offset) / texel - 0.5));
                int x1 = Math.Min(res - 1, (int)Math.Ceiling((w.LocalX(c.MaxX) + reach - offset) / texel - 0.5));
                int z0 = Math.Max(0, (int)Math.Floor((w.LocalZ(c.MinZ) - reach - offset) / texel - 0.5));
                int z1 = Math.Min(res - 1, (int)Math.Ceiling((w.LocalZ(c.MaxZ) + reach - offset) / texel - 0.5));
                for (int tz = z0; tz <= z1; tz++)
                {
                    double pz = w.Z0 + offset + (tz + 0.5) * texel;
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        double px = w.X0 + offset + (tx + 0.5) * texel;
                        StreamSample s = StreamNetwork.Nearest(all, px, pz);
                        if (s.Course != c) continue;   // each texel once, by its nearest stream
                        float d = s.Distance;
                        float n = 0.5f + 0.5f * EndlessNoise.Sample(noise, px, pz, 0.35);
                        float a;
                        Array.Clear(target, 0, layers);
                        bool road = Math.Abs(s.FromCrossing) <= c.RoadZone + c.RoadFade;
                        if (road)
                        {
                            bool fordRoad = c.Crossing.Kind == CrossingKind.Ford && Math.Abs(s.FromCrossing) < 4.5f;
                            float edge = fordRoad ? s.HalfRibbon + 1.2f : s.HalfWater + 2.6f;
                            if (d >= edge) continue;
                            if (fordRoad)
                            {
                                // The ford: gravel and mud over the road and its verges.
                                a = 1f - Mathf.Clamp01((d - s.HalfRibbon + 0.3f) / 1.5f);
                                target[TerrainShaper.MudLayer] = 0.45f + 0.25f * n;
                                target[TerrainShaper.DirtLayer] = 1f - target[TerrainShaper.MudLayer];
                            }
                            else if (d < s.HalfWater + 0.3f)
                            {
                                a = 1f;
                                target[TerrainShaper.MudLayer] = 0.55f + 0.3f * n;
                                target[TerrainShaper.DirtLayer] = 1f - target[TerrainShaper.MudLayer];
                            }
                            else
                            {
                                // The bank just above the water: wet mud fading into the grass.
                                a = 0.85f * (1f - Mathf.Clamp01((d - s.HalfWater - 0.3f) / 2.3f));
                                target[TerrainShaper.MudLayer] = 0.65f + 0.2f * n;
                                target[TerrainShaper.LushGrassLayer] = 1f - target[TerrainShaper.MudLayer];
                            }
                        }
                        else if (d < s.HalfWater + ToeMud)
                        {
                            // The bed, seen through the water, and the ground under the bank's flat toe: mud with flecks of dirt (the
                            // wet lip of the bank is the same mud). Where the terrain's simplified mesh rises through the toe it shows
                            // mud, not a lime line of grass along the water (review 2026-10-06).
                            a = 1f;
                            target[TerrainShaper.MudLayer] = 0.7f + 0.25f * n;
                            target[TerrainShaper.DirtLayer] = 1f - target[TerrainShaper.MudLayer];
                        }
                        else
                        {
                            // Under the drawn bank and a few metres beyond its edge: the lush grass the bank shows.
                            if (d >= s.Outer + 3f) continue;
                            a = 1f - (float)EndlessNoise.Smooth(s.Outer - 0.5, s.Outer + 3.0, d);
                            target[TerrainShaper.LushGrassLayer] = 1f;
                        }

                        if (a <= 0f) continue;
                        for (int l = 0; l < layers; l++)
                        {
                            float old = splat[tz, tx, l];
                            splat[tz, tx, l] = old + (target[l] - old) * a;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Takes the grass and flowers out of every detail cell (texel <paramref name="texel"/> m, cell (0, 0) at window position
        /// <paramref name="offset"/> cells) that could put an instance in the water or on the wet lip of a stream.
        /// </summary>
        public static void KeepDetailsOut(List<DetailLayer> layers, GenWindow w, List<StreamCourse> courses, int offset, int res, float texel)
        {
            if (courses.Count == 0 || layers.Count == 0) return;
            var pieces = new List<StreamPiece>();
            foreach (StreamCourse c in courses) pieces.AddRange(c.Pieces);
            StreamPiece[] all = pieces.ToArray();
            float cellReach = texel * 0.7072f;
            for (int z = 0; z < res; z++)
            {
                double wz = w.Z0 + (z + offset + 0.5) * texel;
                for (int x = 0; x < res; x++)
                {
                    double wx = w.X0 + (x + offset + 0.5) * texel;
                    StreamSample s = StreamNetwork.Nearest(all, wx, wz);
                    if (s.Course == null) continue;
                    float half = Math.Max(s.HalfWater, VisibleHalf(s.Course, s.Along, s.FromCrossing, s.HalfWater, s.HalfRibbon));
                    if (s.Distance > half + StreamNetwork.LipWidth + cellReach) continue;
                    int i = z * res + x;
                    foreach (DetailLayer layer in layers) layer.density[i] = 0;
                }
            }
        }

        /// <summary>
        /// Paints worn paths (absolute segments with a width; a zero-length segment is a round trodden patch) as dirt with mud
        /// flecks into <paramref name="splat"/> (texel (0, 0) at window position (<paramref name="offset"/>, <paramref name="offset"/>)).
        /// </summary>
        public static void PaintPaths(float[,,] splat, GenWindow w, float offset, List<(double x0, double z0, double x1, double z1, float width)> paths, GradientNoise noise,
            int layer = -1)
        {
            if (paths.Count == 0) return;
            int res = splat.GetLength(0);
            float texel = (w.Size - 2f * offset) / res;
            int layers = splat.GetLength(2);
            if (layer >= layers) layer = TerrainShaper.DirtLayer;   // a splat without the cobblestone channel
            foreach (var (ax, az, bx, bz, width) in paths)
            {
                float half = width * 0.5f, soft = 0.9f;
                float reach = half + soft;
                int x0 = Math.Max(0, (int)Math.Floor((w.LocalX(Math.Min(ax, bx)) - reach - offset) / texel - 0.5));
                int x1 = Math.Min(res - 1, (int)Math.Ceiling((w.LocalX(Math.Max(ax, bx)) + reach - offset) / texel - 0.5));
                int z0 = Math.Max(0, (int)Math.Floor((w.LocalZ(Math.Min(az, bz)) - reach - offset) / texel - 0.5));
                int z1 = Math.Min(res - 1, (int)Math.Ceiling((w.LocalZ(Math.Max(az, bz)) + reach - offset) / texel - 0.5));
                double ex = bx - ax, ez = bz - az, len2 = ex * ex + ez * ez;
                for (int tz = z0; tz <= z1; tz++)
                {
                    double pz = w.Z0 + offset + (tz + 0.5) * texel;
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        double px = w.X0 + offset + (tx + 0.5) * texel;
                        double t = len2 > 1e-9 ? ((px - ax) * ex + (pz - az) * ez) / len2 : 0.0;
                        t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
                        double dx = px - (ax + ex * t), dz = pz - (az + ez * t);
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (d >= reach) continue;
                        float n = 0.5f + 0.5f * EndlessNoise.Sample(noise, px, pz, 0.45);
                        float a = (1f - Mathf.Clamp01((d - half + soft) / (2f * soft))) * (0.7f + 0.25f * n);
                        float mud = 0.2f * n;
                        for (int l = 0; l < layers; l++)
                        {
                            // layer >= 0: one channel, no mud (a walled city's cobbles on TerrainShaper.CobbleLayer).
                            float target = layer >= 0 ? (l == layer ? 1f : 0f) : l == TerrainShaper.DirtLayer ? 1f - mud : l == TerrainShaper.MudLayer ? mud : 0f;
                            splat[tz, tx, l] += (target - splat[tz, tx, l]) * a;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ props

        /// <summary>Distance along the stream from its road crossing to the ford's stepping stones (beyond the road's part and the bank's fade).</summary>
        public static float SteppingStonesFrom(StreamCourse c) => c.RoadZone + c.RoadFade + 0.8f;

        /// <summary>Side (+1 downstream, -1 upstream of the road) of a ford's stepping stones.</summary>
        public static float SteppingStonesSide(Crossing crossing) => EndlessNoise.Unit(crossing.Seed, 1156, 0) < 0.5 ? -1f : 1f;

        /// <summary>
        /// Rocks in and beside each stream (every 18-45 m, in and out of the water, seated by <see cref="RockPlacer"/> on the drawn
        /// bank), reeds in clumps at the water's edge, the stepping stones of each ford with a worn path from the road to them on both
        /// banks, and the footbridge at each bridge. Props whose centre lies in the tile are added to <paramref name="props"/>; each
        /// placed piece is registered in <paramref name="occupancy"/>; the rocks standing in water go to <paramref name="waterRocks"/>
        /// (for the foam) and the worn paths to <paramref name="paths"/>.
        /// </summary>
        public static void Props(GenWindow w, Ground ground, EndlessParams p, PaletteKeys keys, RoadCurve road, ElevationProfile profile,
            OccupancyMap occupancy, double tx, double tz, float tile, List<PropPlacement> props, List<WaterRock> waterRocks,
            List<(double x0, double z0, double x1, double z1, float width)> paths)
        {
            List<StreamCourse> courses = ground.Courses;
            if (courses.Count == 0) return;
            var seating = new RockPlacer.Context(w.Params, ground.Drawn, keys, null);
            float corridor = w.Params.ScatterCorridor;
            foreach (StreamCourse c in courses)
            {
                Crossing crossing = c.Crossing;
                var placed = new List<PropPlacement>();

                // Rocks along the stream (not near the road): in the water, and on the bank in small groups.
                if (keys.Has(PropRole.Rock))
                {
                    var rng = new System.Random(EndlessNoise.Seed(crossing.Seed, 1060, 0));
                    float along = 6f + (float)rng.NextDouble() * 20f;
                    float total = c.Length;
                    int segment = 0;
                    while (along < total - 4f)
                    {
                        double roll = rng.NextDouble(), side = rng.NextDouble() < 0.5 ? -1.0 : 1.0, offsetRoll = rng.NextDouble();
                        double sizeRoll = rng.NextDouble(), yaw = rng.NextDouble() * 360.0;
                        int variant = keys.PickVariant(PropRole.Rock, rng);
                        int group = rng.NextDouble() < 0.45 ? 2 + rng.Next(2) : 1;
                        float here = along;
                        along += 18f + (float)rng.NextDouble() * 27f;
                        while (segment + 1 < c.Count - 1 && c.Along[segment + 1] < here) segment++;
                        if (Math.Abs(here - c.Along[c.CrossingIndex]) < c.RoadZone + c.RoadFade + 4f) continue;
                        bool inStream = roll < 0.5;
                        for (int g = 0; g < (inStream ? 1 : group); g++)
                        {
                            float at = here + g * (0.9f + 0.5f * (float)offsetRoll);
                            int k = segment;
                            while (k + 1 < c.Count - 1 && c.Along[k + 1] < at) k++;
                            float f = Mathf.Clamp01((at - c.Along[k]) / Math.Max(0.01f, c.Along[k + 1] - c.Along[k]));
                            double cx = c.X[k] + (c.X[k + 1] - c.X[k]) * f;
                            double cz = c.Z[k] + (c.Z[k + 1] - c.Z[k]) * f;
                            float halfWater = c.HalfWater[k] + (c.HalfWater[k + 1] - c.HalfWater[k]) * f;
                            float spread = g == 0 ? (float)offsetRoll : 0.3f + 0.4f * ((g * 0.37f + (float)offsetRoll) % 1f);
                            float u = inStream ? (float)(side * offsetRoll * halfWater * 0.6) : (float)(side * (halfWater + 0.25 + 1.4 * spread));
                            double ax = cx + c.DirZ[k] * u, az = cz - c.DirX[k] * u;
                            float lx = w.LocalX(ax), lz = w.LocalZ(az);
                            if (lx < 3f || lz < 3f || lx > w.Size - 3f || lz > w.Size - 3f) continue;
                            road.Nearest(ax, az, out double toRoad);
                            if (toRoad < corridor + 2.0) continue;
                            float height = inStream ? 0.35f + 0.45f * (float)sizeRoll : (0.3f + 0.55f * (float)sizeRoll) * (g == 0 ? 1f : 0.6f);
                            float scale = ScaleFor(keys, PropRole.Rock, variant, height);
                            if (!RockPlacer.TrySeat(seating, PropRole.Rock, variant, lx, lz, (float)(yaw + 97.0 * g), scale, out PropPlacement rock)) continue;
                            rock.settlementId = -1;
                            placed.Add(rock);
                            if (inStream) waterRocks.Add(new WaterRock { X = ax, Z = az, Radius = 0.35f * height + 0.15f });
                        }
                    }
                }

                // Reeds and rushes in clumps of 3-6 at the water's edge (some standing in the shallows) every 12-30 m, lowland only.
                if (keys.Has(PropRole.Reeds))
                {
                    var rng = new System.Random(EndlessNoise.Seed(crossing.Seed, 1062, 0));
                    float along = 8f + (float)rng.NextDouble() * 20f;
                    int segment = 0;
                    float lowland = EndlessGenerator.TreeLine * p.heightRange - 40f;
                    while (along < c.Length - 6f)
                    {
                        float here = along;
                        along += 12f + (float)rng.NextDouble() * 18f;
                        double side = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
                        int clump = 3 + rng.Next(4);
                        double seed = rng.NextDouble();
                        while (segment + 1 < c.Count - 1 && c.Along[segment + 1] < here) segment++;
                        if (Math.Abs(here - c.Along[c.CrossingIndex]) < c.RoadZone + c.RoadFade + 3f || c.Surface[segment] > lowland) continue;
                        var local = new System.Random((int)(seed * int.MaxValue));
                        for (int r = 0; r < clump; r++)
                        {
                            float a = here + (float)(local.NextDouble() * 3.0 - 1.5);
                            int k = segment;
                            while (k + 1 < c.Count - 1 && c.Along[k + 1] < a) k++;
                            while (k > 0 && c.Along[k] > a) k--;
                            float f = Mathf.Clamp01((a - c.Along[k]) / Math.Max(0.01f, c.Along[k + 1] - c.Along[k]));
                            float halfWater = c.HalfWater[k] + (c.HalfWater[k + 1] - c.HalfWater[k]) * f;
                            float u = (float)(side * (halfWater - 0.2 + 0.7 * local.NextDouble()));
                            double ax = c.X[k] + (c.X[k + 1] - c.X[k]) * f + c.DirZ[k] * u;
                            double az = c.Z[k] + (c.Z[k + 1] - c.Z[k]) * f - c.DirX[k] * u;
                            float lx = w.LocalX(ax), lz = w.LocalZ(az);
                            int variant = local.Next(3);
                            float yaw = (float)(local.NextDouble() * 360.0);
                            // A few tall reeds, the rest shorter, rush-like tufts.
                            float scale = r < 2 ? 0.85f + 0.3f * (float)local.NextDouble() : 0.5f + 0.25f * (float)local.NextDouble();
                            if (lx < 1f || lz < 1f || lx > w.Size - 1f || lz > w.Size - 1f) continue;
                            road.Nearest(ax, az, out double toRoad);
                            if (toRoad < corridor) continue;
                            float y = ground.SurfaceAt(ax, az);
                            placed.Add(new PropPlacement { role = PropRole.Reeds, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale, settlementId = -1 });
                        }
                    }
                }

                // The crossing itself.
                double zc = road.ZAt(crossing.S);
                double xc = road.X(zc, out double slope, out _);
                double len = Math.Sqrt(1.0 + slope * slope);
                var tangent = new Vector3((float)(slope / len), 0f, (float)(1.0 / len));
                float roadHeight = profile.Height(crossing.S);
                if (crossing.Kind == CrossingKind.Bridge && keys.Has(PropRole.Bridge))
                {
                    // Scaled by BridgeShape (ScaleX/Y/Z) by the tile builder: a longer, gentler hump and a wider arch.
                    // The bridge is straight and the road a gentle curve (2026-10-06): the bridge lies along the chord of the road over
                    // its length, half way between the crossing and the chord's middle, so the road's centre line strays from the
                    // bridge's axis by half the curve's sagitta at most (up to 0.45 m at the ends along the tangent; the walls stand
                    // 0.3 m beyond the road's edge on the axis).
                    double za = road.ZAt(crossing.S - BridgeShape.HalfLength), zb = road.ZAt(crossing.S + BridgeShape.HalfLength);
                    double xa = road.X(za), xb = road.X(zb);
                    double chord = Math.Sqrt((xb - xa) * (xb - xa) + (zb - za) * (zb - za));
                    Vector3 axis = chord > 1.0 ? new Vector3((float)((xb - xa) / chord), 0f, (float)((zb - za) / chord)) : tangent;
                    double bx = 0.5 * (xc + 0.5 * (xa + xb)), bz = 0.5 * (zc + 0.5 * (za + zb));
                    var pos = new Vector3(w.LocalX(bx), BridgeShape.PivotY(roadHeight), w.LocalZ(bz));
                    placed.Add(new PropPlacement { role = PropRole.Bridge, variant = 0, position = pos, yaw = ScatterPlanner.Yaw(axis), scale = 1f, settlementId = crossing.Id });
                    ground.Decks.Add(new BridgeDeck { X = bx, Z = bz, DirX = axis.x, DirZ = axis.z, PivotY = BridgeShape.PivotY(roadHeight) });
                    DressAbutments(c, crossing, w, keys, seating, ground, axis, placed, waterRocks);
                }
                else if (crossing.Kind == CrossingKind.Ford && keys.Has(PropRole.Rock))
                {
                    // A footpath leaves the road before the ford, crosses the water on a line of flat stones just beside it and rejoins
                    // the road on the other bank: the stones lie across the stream (parallel to the road), a little staggered.
                    var rng = new System.Random(EndlessNoise.Seed(crossing.Seed, 1061, 0));
                    float sideAlong = SteppingStonesSide(crossing);
                    float at = c.Along[c.CrossingIndex] + sideAlong * SteppingStonesFrom(c);
                    int k = c.CrossingIndex;
                    while (k > 0 && c.Along[k] > at) k--;
                    while (k + 1 < c.Count - 1 && c.Along[k + 1] < at) k++;
                    float f = Mathf.Clamp01((at - c.Along[k]) / Math.Max(0.01f, c.Along[k + 1] - c.Along[k]));
                    double cx = c.X[k] + (c.X[k + 1] - c.X[k]) * f, cz = c.Z[k] + (c.Z[k + 1] - c.Z[k]) * f;
                    float halfWater = c.HalfWater[k] + (c.HalfWater[k + 1] - c.HalfWater[k]) * f;
                    float surface = c.Surface[k] + (c.Surface[k + 1] - c.Surface[k]) * f;
                    float span = halfWater + 0.1f;
                    int stones = Math.Max(3, (int)Math.Round(2f * span / 0.72f) + 1);
                    for (int sIndex = 0; sIndex < stones; sIndex++)
                    {
                        float u = -span + 2f * span * sIndex / (stones - 1) + (float)(rng.NextDouble() - 0.5) * 0.12f;
                        float shift = (sIndex % 2 == 0 ? -0.12f : 0.12f) + (float)(rng.NextDouble() - 0.5) * 0.1f;
                        double ax = cx + c.DirZ[k] * u + c.DirX[k] * shift, az = cz - c.DirX[k] * u + c.DirZ[k] * shift;
                        int variant = rng.Next(2);
                        ground.Rendered(ground.Final, ax, az, out float bed, out _);
                        // Tall enough to stand a hand above the water wherever it is seated.
                        float height = Mathf.Clamp(surface - bed + 0.22f, 0.32f, 0.8f) + 0.04f * (float)rng.NextDouble();
                        float scale = ScaleFor(keys, PropRole.Rock, variant, height);
                        float yaw = (float)(rng.NextDouble() * 360.0);
                        if (!RockPlacer.TrySeat(seating, PropRole.Rock, variant, w.LocalX(ax), w.LocalZ(az), yaw, scale, out PropPlacement stone)) continue;
                        stone.settlementId = crossing.Id;
                        placed.Add(stone);
                        waterRocks.Add(new WaterRock { X = ax, Z = az, Radius = 0.35f * height + 0.1f });
                    }

                    // The worn path on each bank: from the end of the stones, over the drawn bank (its colour, see Mesh), back to the road.
                    double rx = c.DirZ[k], rz = -c.DirX[k];
                    double ox = c.X[c.CrossingIndex], oz = c.Z[c.CrossingIndex];
                    float outer = c.Outer[k] - halfWater;
                    foreach (double sgn in new[] { -1.0, 1.0 })
                    {
                        double ex = cx + rx * sgn * (halfWater + outer + 0.4), ez = cz + rz * sgn * (halfWater + outer + 0.4);
                        double qx = ox + rx * sgn * (halfWater + outer + 3.0), qz = oz + rz * sgn * (halfWater + outer + 3.0);
                        paths.Add((ex, ez, qx, qz, 1.1f));
                    }
                }

                foreach (PropPlacement prop in placed)
                {
                    occupancy.Add(new Vector3(prop.position.x, 0f, prop.position.z), prop.role == PropRole.Bridge ? BridgeShape.HalfLength + 0.5f : prop.role == PropRole.Reeds ? 0.5f : 0.9f);
                    double ax = w.X0 + prop.position.x, az = w.Z0 + prop.position.z;
                    if (ax >= tx && ax < tx + tile && az >= tz && az < tz + tile) props.Add(prop);
                }
            }
        }

        private static float ScaleFor(PaletteKeys keys, string family, int variant, float height) =>
            keys.TryGetMetrics(family, variant, out PropMetrics m) ? Mathf.Clamp(height / Mathf.Max(0.05f, m.Height), 0.15f, 2f) : 0.5f;

        // ------------------------------------------------------------------ sections of the drawn stream

        /// <summary>One cross-section of a stream at a point of its smoothed line (absolute centre, values there).</summary>
        private struct Section
        {
            public double X, Z;
            public float DirX, DirZ, Surface, Bed, Half, Outer, Top, Along, FromCrossing, Riffle, Fade, Flow, FordRoad;
            public int Seed;
        }

        /// <summary>The section at line point <paramref name="k"/> + fraction <paramref name="t"/> toward the next (exactly the point when t is 0 or 1).</summary>
        private static Section At(StreamCourse c, StreamLine l, int k, double t)
        {
            if (t >= 1.0 && k + 1 < l.Count)
            {
                k++;
                t = 0.0;
            }

            int k1 = Math.Min(l.Count - 1, k + 1);
            float f = (float)t;
            var s = new Section();
            if (t <= 0.0)
            {
                s.X = l.X[k];
                s.Z = l.Z[k];
            }
            else
            {
                s.X = l.X[k] + (l.X[k1] - l.X[k]) * t;
                s.Z = l.Z[k] + (l.Z[k1] - l.Z[k]) * t;
            }

            float dx = l.DirX[k] + (l.DirX[k1] - l.DirX[k]) * f, dz = l.DirZ[k] + (l.DirZ[k1] - l.DirZ[k]) * f;
            float dl = (float)Math.Sqrt(dx * dx + dz * dz);
            s.DirX = dl > 1e-6f ? dx / dl : 0f;
            s.DirZ = dl > 1e-6f ? dz / dl : 1f;
            s.Surface = l.Surface[k] + (l.Surface[k1] - l.Surface[k]) * f;
            s.Bed = l.Bed[k] + (l.Bed[k1] - l.Bed[k]) * f;
            s.Along = l.Along[k] + (l.Along[k1] - l.Along[k]) * f;
            s.FromCrossing = s.Along - c.Along[c.CrossingIndex];
            float halfWater = l.HalfWater[k] + (l.HalfWater[k1] - l.HalfWater[k]) * f;
            float halfRibbon = l.HalfRibbon[k] + (l.HalfRibbon[k1] - l.HalfRibbon[k]) * f;
            s.Half = VisibleHalf(c, s.Along, s.FromCrossing, halfWater, halfRibbon);
            s.Outer = l.Outer[k] + (l.Outer[k1] - l.Outer[k]) * f;
            s.Top = l.BankTop[k] + (l.BankTop[k1] - l.BankTop[k]) * f;
            s.Riffle = l.Riffle[k] + (l.Riffle[k1] - l.Riffle[k]) * f;
            s.Fade = Fade(c, s.Along, s.FromCrossing);
            s.FordRoad = FordRoad(c, s.FromCrossing);
            s.Seed = c.Crossing.Seed;
            int a = Math.Max(0, k - 1), b = Math.Min(l.Count - 1, k + 2);
            float run = Math.Max(0.5f, l.Along[b] - l.Along[a]);
            s.Flow = Mathf.Clamp01((l.Surface[a] - l.Surface[b]) / run / 0.035f);
            // Beyond the water's edge the bank needs the room it was planned with, at least.
            s.Outer = Math.Max(s.Outer, s.Half + s.Top + StreamNetwork.DrapeWidth);
            return s;
        }

        /// <summary>The bank's columns, metres beyond the water's edge (from under the water out to the skirt).</summary>
        private static void BankColumnsOf(in Section s, float[] xs)
        {
            float top = Math.Max(s.Top + 0.15f, 1.3f);
            float end = Math.Max(top + 1.5f, s.Outer - s.Half);
            xs[0] = -StreamNetwork.BankUnderWater;
            xs[1] = 0f;
            xs[2] = 0.15f;
            xs[3] = 0.35f;
            xs[4] = StreamNetwork.LipWidth;
            for (int k = 1; k <= 4; k++) xs[4 + k] = StreamNetwork.LipWidth + (top - StreamNetwork.LipWidth) * k / 4f;
            for (int k = 1; k <= 3; k++) xs[8 + k] = top + (end - top) * k / 4f;
            xs[12] = end;
        }

        /// <summary>Runs of the smoothed line inside the tile: (point, fraction) pairs, a pair on each edge where the line enters or leaves.</summary>
        private static List<List<(int k, double t)>> Runs(StreamLine l, double tx, double tz, double x1, double z1)
        {
            var result = new List<List<(int, double)>>();
            var run = new List<(int, double)>();
            for (int i = 0; i + 1 < l.Count; i++)
            {
                if (!Clip(l.X[i], l.Z[i], l.X[i + 1], l.Z[i + 1], tx, tz, x1, z1, out double t0, out double t1))
                {
                    if (run.Count >= 2) result.Add(run);
                    run = new List<(int, double)>();
                    continue;
                }

                if (run.Count == 0) run.Add((i, t0));
                run.Add((i, t1));
                if (t1 < 1.0)
                {
                    if (run.Count >= 2) result.Add(run);
                    run = new List<(int, double)>();
                }
            }

            if (run.Count >= 2) result.Add(run);
            return result;
        }

        /// <summary>
        /// The water ribbons of <paramref name="courses"/> inside the tile [tx, tx + tile] × [tz, tz + tile]: each course's smoothed line
        /// clipped to the square (a vertex on the edge where it enters or leaves, interpolated from the same segment as the
        /// neighbouring tile does) with the drawn water's half width, tile-local.
        /// </summary>
        public static List<StreamRibbon> Ribbons(List<StreamCourse> courses, double tx, double tz, float tile)
        {
            var result = new List<StreamRibbon>();
            double x1 = tx + tile, z1 = tz + tile;
            foreach (StreamCourse c in courses)
            {
                if (c.MaxX < tx || c.MinX > x1 || c.MaxZ < tz || c.MinZ > z1) continue;
                StreamLine l = c.Line;
                foreach (List<(int k, double t)> run in Runs(l, tx, tz, x1, z1))
                {
                    var vertices = new StreamRibbonVertex[run.Count];
                    for (int i = 0; i < run.Count; i++) vertices[i] = RibbonVertex(At(c, l, run[i].k, run[i].t), tx, tz);
                    result.Add(new StreamRibbon { StreamId = c.Crossing.Id, Vertices = vertices });
                }
            }

            return result;
        }

        private static StreamRibbonVertex RibbonVertex(in Section s, double tx, double tz) => new StreamRibbonVertex
        {
            X = (float)(s.X - tx),
            Z = (float)(s.Z - tz),
            DirX = s.DirX,
            DirZ = s.DirZ,
            HalfWidth = s.Half,
            Surface = s.Surface,
            Along = s.Along,
        };

        // ------------------------------------------------------------------ the mesh

        /// <summary>Absolute positions of a section's vertices: water (left edge to right edge), left bank (outermost first), right bank (innermost first).</summary>
        private static void Positions(in Section s, Ground g, double[] px, double[] pz, float[] py, float[] bankX)
        {
            double rx = s.DirZ, rz = -s.DirX;   // right of the flow
            for (int col = 0; col < WaterColumns; col++)
            {
                double u = s.Half * (col - 2) * 0.5;
                px[col] = s.X + rx * u;
                pz[col] = s.Z + rz * u;
                py[col] = s.Surface;
                if (s.FordRoad > 0f)
                {
                    // Thin water over a ford's road bed: never under the bed (the ground must not make the edge there).
                    g.Rendered(g.Final, px[col], pz[col], out _, out float bedHi);
                    py[col] = Math.Max(py[col], bedHi + FordBedLift * s.FordRoad);
                }
            }

            BankColumnsOf(s, bankX);
            for (int side = 0; side < 2; side++)
            {
                for (int j = 0; j < BankColumns; j++)
                {
                    // Left bank stored outermost first, right bank innermost first: both in order of increasing u.
                    int col = side == 0 ? BankColumns - 1 - j : j;
                    float x = bankX[col];
                    double u = (side == 0 ? -1.0 : 1.0) * Math.Max(0.0, s.Half + x);
                    int v = WaterColumns + side * BankColumns + j;
                    px[v] = s.X + rx * u;
                    pz[v] = s.Z + rz * u;
                    g.Rendered(g.Final, px[v], pz[v], out float finalLo, out float finalHi);
                    g.Rendered(g.Bare, px[v], pz[v], out _, out float bareHi);
                    if (x > s.Top)
                    {
                        // Draped over the ground: the upper envelope of the ground around the vertex, so the mesh's chords between
                        // columns never cut under a crease of the 2 m ground.
                        for (int e = 0; e < 4; e++)
                        {
                            double ex = e < 2 ? rx * (e == 0 ? -0.6 : 0.6) : s.DirX * (e == 2 ? -0.5 : 0.5);
                            double ez = e < 2 ? rz * (e == 0 ? -0.6 : 0.6) : s.DirZ * (e == 2 ? -0.5 : 0.5);
                            g.Rendered(g.Bare, px[v] + ex, pz[v] + ez, out _, out float around);
                            bareHi = Math.Max(bareHi, around);
                        }
                    }

                    py[v] = BankHeight(x, s.Surface, bareHi, finalLo, s.Fade, col == BankColumns - 1, s.Top);
                    if (col == BankColumns - 1) continue;
                    py[v] += Hummock(s.Seed, side, s.Along, x, s.Top) * (1f - s.FordRoad);
                    // A ford's road bed is not carved under the mesh: lie a little above it (the bank there is the road, in dirt).
                    if (s.FordRoad > 0f) py[v] = Math.Max(py[v], finalHi + FordBedLift * s.FordRoad);
                }
            }
        }

        /// <summary>
        /// The drawn streams of the tile [tx, tx + tile] × [tz, tz + tile] as one mesh (water strip in the first submesh, banks in the
        /// second, bank triangles again for the collider) and their water ribbons, or null when no stream crosses the tile.
        /// Sections every metre of the smoothed line (and on the tile's edges); each has a water strip of five columns at the water
        /// surface and on each side a bank of <see cref="BankColumns"/> columns: under the water's edge, a wet lip, the bank rising
        /// to the ground, the drape over the ground and a skirt under it. <paramref name="waterRocks"/> foam the water around them.
        /// </summary>
        public static StreamRibbonMeshData Mesh(Ground g, double tx, double tz, float tile, List<WaterRock> waterRocks, out List<StreamRibbon> ribbons)
        {
            ribbons = new List<StreamRibbon>();
            double x1 = tx + tile, z1 = tz + tile;
            var vertices = new List<StreamVertex>();
            var water = new List<ushort>();
            var bank = new List<ushort>();
            var px = new double[SectionVertices];
            var pz = new double[SectionVertices];
            var py = new float[SectionVertices];
            var bankX = new float[BankColumns];
            var prevX = new double[SectionVertices];
            var prevZ = new double[SectionVertices];
            var prevY = new float[SectionVertices];
            var nextX = new double[SectionVertices];
            var nextZ = new double[SectionVertices];
            var nextY = new float[SectionVertices];
            foreach (StreamCourse c in g.Courses)
            {
                if (c.MaxX < tx || c.MinX > x1 || c.MaxZ < tz || c.MinZ > z1) continue;
                StreamLine l = c.Line;
                float lipPhase = (float)(2.0 * Math.PI * EndlessNoise.Unit(c.Crossing.Seed, 1155, 0));
                bool ford = c.Crossing.Kind == CrossingKind.Ford;
                float stonesAlong = ford ? c.Along[c.CrossingIndex] + SteppingStonesSide(c.Crossing) * SteppingStonesFrom(c) : float.NaN;
                foreach (List<(int k, double t)> run in Runs(l, tx, tz, x1, z1))
                {
                    if (vertices.Count + SectionVertices * run.Count > ushort.MaxValue - 1) break;
                    var line = new StreamRibbonVertex[run.Count];
                    int first = vertices.Count;
                    for (int r = 0; r < run.Count; r++)
                    {
                        (int k, double t) = run[r];
                        Section s = At(c, l, k, t);
                        line[r] = RibbonVertex(s, tx, tz);
                        Positions(s, g, px, pz, py, bankX);

                        // The neighbouring line points (for the bank's normals): for a point, the points either side; for a section
                        // between two points (on a tile edge), those two, so both tiles compute the same normals there.
                        int pk, nk;
                        if (t >= 1.0)
                        {
                            pk = k;
                            nk = k + 2;
                        }
                        else if (t > 0.0)
                        {
                            pk = k;
                            nk = k + 1;
                        }
                        else
                        {
                            pk = k - 1;
                            nk = k + 1;
                        }

                        Section sp = At(c, l, Math.Max(0, Math.Min(l.Count - 1, pk)), 0.0);
                        Section sn = At(c, l, Math.Max(0, Math.Min(l.Count - 1, nk)), 0.0);
                        Positions(sp, g, prevX, prevZ, prevY, bankX);
                        Positions(sn, g, nextX, nextZ, nextY, bankX);
                        BankColumnsOf(s, bankX);

                        var tangent = new Vector4(s.DirX, 0f, s.DirZ, 1f);
                        float v = s.Along / StreamRibbonMeshData.TextureLength;
                        // Water: depth toward the middle, flow, foam on riffles and around rocks.
                        for (int col = 0; col < WaterColumns; col++)
                        {
                            float across = (col - 2) * 0.5f;
                            float depth = Mathf.Clamp01((s.Surface - s.Bed) / 0.6f) * (1f - Math.Abs(across));
                            float foam = s.Riffle * 0.5f;
                            // The ford: thin water running over the road, a little broken.
                            if (ford && Math.Abs(s.FromCrossing) <= c.RoadZone + c.RoadFade) foam = Math.Max(foam, 0.3f * (1f - Mathf.Clamp01((s.Surface - s.Bed) / 0.3f)));
                            foreach (WaterRock rock in waterRocks)
                            {
                                double dx = px[col] - rock.X, dz = pz[col] - rock.Z;
                                double d = Math.Sqrt(dx * dx + dz * dz);
                                if (d < rock.Radius + 1.6) foam = Math.Max(foam, 1f - (float)EndlessNoise.Smooth(rock.Radius, rock.Radius + 1.6, d));
                            }

                            vertices.Add(new StreamVertex
                            {
                                position = new Vector3((float)(px[col] - tx), py[col], (float)(pz[col] - tz)),
                                normal = Vector3.up,
                                tangent = tangent,
                                uv = new Vector2(across, v),
                                color = new Color32(Byte(depth), Byte(0.25f + 0.75f * Math.Max(s.Flow, s.Riffle)), Byte(foam), 255),
                            });
                        }

                        // Banks: wet near the water, a lip of mud whose width wanders, grass beyond; the ford's path in dirt.
                        float lip = 0.32f + 0.22f * (0.5f + 0.5f * (float)Math.Sin(s.Along * 0.37f + lipPhase)) + 0.1f * (0.5f + 0.5f * (float)Math.Sin(s.Along * 1.13f + 2.1f * lipPhase));
                        float path = float.IsNaN(stonesAlong) ? 0f : 1f - (float)EndlessNoise.Smooth(0.45, 0.95, Math.Abs(s.Along - stonesAlong));
                        path = Math.Max(path, s.FordRoad);
                        for (int side = 0; side < 2; side++)
                        {
                            for (int j = 0; j < BankColumns; j++)
                            {
                                int vi = WaterColumns + side * BankColumns + j;
                                int col = side == 0 ? BankColumns - 1 - j : j;
                                float x = bankX[col];
                                int ja = Math.Max(0, j - 1), jb = Math.Min(BankColumns - 1, j + 1);
                                int va = WaterColumns + side * BankColumns + ja, vb = WaterColumns + side * BankColumns + jb;
                                var acrossT = new Vector3((float)(px[vb] - px[va]), py[vb] - py[va], (float)(pz[vb] - pz[va]));
                                var alongT = new Vector3((float)(nextX[vi] - prevX[vi]), nextY[vi] - prevY[vi], (float)(nextZ[vi] - prevZ[vi]));
                                Vector3 normal = Vector3.Cross(alongT, acrossT);
                                float nl = normal.magnitude;
                                normal = nl > 1e-6f ? normal / nl : Vector3.up;
                                if (normal.y < 0f) normal = -normal;
                                float mud = 1f - (float)EndlessNoise.Smooth(lip, lip + 0.45, x);
                                float wet = 1f - (float)EndlessNoise.Smooth(-0.05, 0.3, x);
                                // The drape over the ground shows exactly the terrain's grass (the shader's plain grass, no patches).
                                float drape = (float)EndlessNoise.Smooth(s.Top, s.Top + 0.4, x);
                                vertices.Add(new StreamVertex
                                {
                                    position = new Vector3((float)(px[vi] - tx), py[vi], (float)(pz[vi] - tz)),
                                    normal = normal,
                                    tangent = tangent,
                                    uv = new Vector2(x + side * 100f, v),
                                    color = new Color32(Byte(mud), Byte(wet), Byte(path * (1f - wet * (1f - s.FordRoad))), Byte(drape)),
                                });
                            }
                        }
                    }

                    // Triangles between consecutive sections (clockwise seen from above: left(k), left(k+1), right(k)).
                    for (int r = 0; r + 1 < run.Count; r++)
                    {
                        int s0 = first + r * SectionVertices, s1 = s0 + SectionVertices;
                        for (int col = 0; col + 1 < WaterColumns; col++) Quad(water, s0 + col, s1 + col);
                        for (int side = 0; side < 2; side++)
                        {
                            int b0 = WaterColumns + side * BankColumns;
                            for (int j = 0; j + 1 < BankColumns; j++) Quad(bank, s0 + b0 + j, s1 + b0 + j);
                        }
                    }

                    ribbons.Add(new StreamRibbon { StreamId = c.Crossing.Id, Vertices = line });
                }
            }

            // The road over each bridge whose centre is in this tile (drawn once, by that tile): dirt like the road, in the bank submesh.
            int streamVertices = vertices.Count, streamBank = bank.Count;
            foreach (BridgeDeck d in g.Decks)
            {
                if (d.X < tx || d.X >= x1 || d.Z < tz || d.Z >= z1) continue;
                List<(double x, float y, double z)> deck = DeckSurface(d, out int sections);
                if (vertices.Count + deck.Count > ushort.MaxValue - 1) break;
                int first = vertices.Count;
                var deckTangent = new Vector4(d.DirX, 0f, d.DirZ, 1f);
                for (int k = 0; k < sections; k++)
                {
                    int ka = Math.Max(0, k - 1) * 5 + 2, kb = Math.Min(sections - 1, k + 1) * 5 + 2;
                    double run = Math.Max(0.01, Math.Sqrt(Math.Pow(deck[kb].x - deck[ka].x, 2) + Math.Pow(deck[kb].z - deck[ka].z, 2)));
                    float grade = (float)((deck[kb].y - deck[ka].y) / run);
                    var normal = new Vector3(-d.DirX * grade, 1f, -d.DirZ * grade).normalized;
                    for (int col = 0; col < 5; col++)
                    {
                        (double x, float y, double z) pt = deck[k * 5 + col];
                        vertices.Add(new StreamVertex
                        {
                            position = new Vector3((float)(pt.x - tx), pt.y, (float)(pt.z - tz)),
                            normal = normal,
                            tangent = deckTangent,
                            uv = new Vector2(200f + (col - 2) * DeckHalf * 0.5f, k * DeckStep / StreamRibbonMeshData.TextureLength),
                            color = new Color32(0, 0, 255, 0),
                        });
                    }
                }

                // Clockwise from above for a right-handed (x right, z forward) section order: section k, k+1, columns left to right.
                for (int k = 0; k + 1 < sections; k++)
                {
                    for (int col = 0; col < 4; col++) Quad(bank, first + k * 5 + col, first + (k + 1) * 5 + col);
                }
            }

            if (vertices.Count == 0) return null;
            var data = new StreamRibbonMeshData
            {
                Vertices = vertices.ToArray(),
                VertexCount = vertices.Count,
                WaterIndexCount = water.Count,
                DeckVertexStart = streamVertices,
                DeckIndexStart = water.Count + streamBank,
            };
            water.AddRange(bank);
            data.Indices = water.ToArray();
            data.IndexCount = data.Indices.Length;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = -min;
            foreach (StreamVertex vtx in data.Vertices)
            {
                min = Vector3.Min(min, vtx.position);
                max = Vector3.Max(max, vtx.position);
            }

            data.BoundsMin = min - new Vector3(0f, 0.05f, 0f);
            data.BoundsMax = max + new Vector3(0f, 0.05f, 0f);

            // The collider: the bank triangles only (the traveller walks on the drawn bank, wades through the water to the bed).
            var map = new int[data.VertexCount];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var positions = new List<Vector3>();
            var colliderIndices = new int[data.IndexCount - data.WaterIndexCount];
            for (int i = data.WaterIndexCount; i < data.IndexCount; i++)
            {
                int vi = data.Indices[i];
                if (map[vi] < 0)
                {
                    map[vi] = positions.Count;
                    positions.Add(data.Vertices[vi].position);
                }

                colliderIndices[i - data.WaterIndexCount] = map[vi];
            }

            data.ColliderPositions = positions.ToArray();
            data.ColliderIndices = colliderIndices;
            return data;
        }

        private static void Quad(List<ushort> indices, int a, int c)
        {
            int b = a + 1, d = c + 1;
            indices.Add((ushort)a);
            indices.Add((ushort)c);
            indices.Add((ushort)b);
            indices.Add((ushort)b);
            indices.Add((ushort)c);
            indices.Add((ushort)d);
        }

        private static byte Byte(float v) => (byte)Math.Round(Mathf.Clamp01(v) * 255f);

        /// <summary>Liang-Barsky clip of the segment a-b against the box; the parameter range inside it.</summary>
        private static bool Clip(double ax, double az, double bx, double bz, double x0, double z0, double x1, double z1, out double t0, out double t1)
        {
            t0 = 0.0;
            t1 = 1.0;
            double dx = bx - ax, dz = bz - az;
            if (!Edge(-dx, ax - x0, ref t0, ref t1)) return false;
            if (!Edge(dx, x1 - ax, ref t0, ref t1)) return false;
            if (!Edge(-dz, az - z0, ref t0, ref t1)) return false;
            if (!Edge(dz, z1 - az, ref t0, ref t1)) return false;
            return t1 > t0;
        }

        private static bool Edge(double pp, double q, ref double t0, ref double t1)
        {
            if (Math.Abs(pp) < 1e-12) return q >= 0.0;
            double r = q / pp;
            if (pp < 0.0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }

            return true;
        }
    }
}
