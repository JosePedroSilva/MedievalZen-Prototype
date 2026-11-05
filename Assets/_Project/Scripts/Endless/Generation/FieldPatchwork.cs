using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// What a settlement's dressing occupies (absolute): its kept-clear circles, own fields, trees, yards and lanes, plus a core disc.
    /// Built from <see cref="DressingCache.Settlement"/>, so it follows whatever layout the composers produce (no strip along the road
    /// is assumed). The farmland patchwork keeps its fields and boundaries off it; the lanes open gateways in the boundaries.
    /// </summary>
    public sealed class SettlementFootprint
    {
        /// <summary>Centre of the place (absolute) and the radius of everything the dressing placed.</summary>
        public double CentreX, CentreZ, Reach;
        /// <summary>True for a hamlet or village (they get a patchwork of strips round them), false for an inn.</summary>
        public bool Village;
        /// <summary>Discs (absolute x, z, radius).</summary>
        public readonly List<(double x, double z, double r)> Discs = new List<(double, double, double)>();
        /// <summary>Lanes (absolute segment and half width).</summary>
        public readonly List<(double x0, double z0, double x1, double z1, double half)> Lanes = new List<(double, double, double, double, double)>();

        /// <summary>A walled city's solid core, metres: no field inside its wall (at most 100 m out, towers 2.5 m more) or against it.</summary>
        public const double CityKeepOut = 113.0;

        /// <summary>Share of the place radius kept as a solid core whatever the dressing placed.</summary>
        public const double CoreShare = 0.35;   // 2026-10-06: 0.6 left a lawn ring round the houses (the dressing's own discs cover them)

        /// <summary>The footprint of <paramref name="place"/> from its dressing <paramref name="d"/>.</summary>
        public static SettlementFootprint Of(Place place, Dressing d)
        {
            var fp = new SettlementFootprint { CentreX = d.CentreX, CentreZ = d.CentreZ, Reach = d.Reach, Village = place.Kind != PlaceKind.Inn };
            fp.Discs.Add((d.CentreX, d.CentreZ, place.City ? CityKeepOut : place.Radius * CoreShare));
            foreach (Vector3 c in d.Circles)
            {
                double cx = d.X0 + c.x - d.CentreX, cz = d.Z0 + c.z - d.CentreZ;
                if (place.City && cx * cx + cz * cz < (CityKeepOut - c.y) * (CityKeepOut - c.y)) continue;   // inside the city's core disc already
                fp.Discs.Add((d.X0 + c.x, d.Z0 + c.z, c.y));
            }
            foreach (Vector3 y in d.Yards) fp.Discs.Add((d.X0 + y.x, d.Z0 + y.z, y.y));
            foreach (TreePlacement t in d.Trees) fp.Discs.Add((d.X0 + t.position.x, d.Z0 + t.position.z, 4.0));
            foreach (FieldPatch f in d.Fields) fp.Discs.Add((d.X0 + f.center.x, d.Z0 + f.center.z, f.halfExtents.magnitude + 2.0));
            foreach (Vector4 path in d.Paths) fp.Lanes.Add((d.X0 + path.x, d.Z0 + path.y, d.X0 + path.z, d.Z0 + path.w, 1.5));
            return fp;
        }

        /// <summary>
        /// Clearance between the footprint and a rectangle centred at (<paramref name="fx"/>, <paramref name="fz"/>) with half sizes
        /// <paramref name="halfX"/> × <paramref name="halfZ"/> turned by (<paramref name="cs"/>, <paramref name="sn"/>) (field axes as
        /// <see cref="FieldMosaic.Distance"/>); negative when they overlap.
        /// </summary>
        public double Clearance(double fx, double fz, double halfX, double halfZ, double cs, double sn)
        {
            double best = double.MaxValue;
            double reach = Math.Sqrt(halfX * halfX + halfZ * halfZ);
            foreach (var (x, z, r) in Discs)
            {
                double dx = x - fx, dz = z - fz;
                if (dx * dx + dz * dz > (reach + r + 60.0) * (reach + r + 60.0)) continue;
                best = Math.Min(best, RectDistance(fx, fz, halfX, halfZ, cs, sn, x, z) - r);
            }

            foreach (var (x0, z0, x1, z1, half) in Lanes)
            {
                double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                int n = Math.Max(1, (int)Math.Ceiling(len / 3.0));
                for (int k = 0; k <= n; k++)
                {
                    double t = (double)k / n, x = x0 + (x1 - x0) * t, z = z0 + (z1 - z0) * t;
                    double dx = x - fx, dz = z - fz;
                    if (dx * dx + dz * dz > (reach + 60.0) * (reach + 60.0)) continue;
                    best = Math.Min(best, RectDistance(fx, fz, halfX, halfZ, cs, sn, x, z) - half);
                }
            }

            return best;
        }

        /// <summary>Distance from (x, z) to a turned rectangle (0 inside).</summary>
        public static double RectDistance(double fx, double fz, double halfX, double halfZ, double cs, double sn, double x, double z)
        {
            double dx = x - fx, dz = z - fz;
            double u = Math.Abs(dx * cs - dz * sn) - halfX, v = Math.Abs(dx * sn + dz * cs) - halfZ;
            double ou = Math.Max(0.0, u), ov = Math.Max(0.0, v);
            return Math.Sqrt(ou * ou + ov * ov);
        }
    }

    /// <summary>
    /// The worked look of the endless farmland (2026-10-06): round every hamlet and village (<see cref="VillageReach"/>) the fields of
    /// the patchwork are open-field blocks of long narrow strips, each strip its own crop (ripe grain, green crop, rapeseed, stubble,
    /// ploughed earth, hay meadow) between grass balks; further out along the road a field is one or two blocks. The paint is a
    /// post-pass over the shared ground paint (TerrainShaper.ComputeSplat) run by near, mid and far tiles alike with the same function of the
    /// absolute position, so every distance shows the same patchwork. Colours are mixes of the shared layers (<see cref="GroundLook.FieldShares"/>).
    /// </summary>
    public static class FieldPatchwork
    {
        /// <summary>Fields whose centre lies within this distance of a hamlet's or village's centre form its patchwork, metres.</summary>
        public const double VillageReach = 420.0;

        /// <summary>A field's rectangle keeps this far off a settlement's footprint (its boundary line is 3-6 m further out), metres.</summary>
        public const double FootprintMargin = 9.0;

        /// <summary>Fields of a village's patchwork within this distance of its footprint are planted (instanced crops), metres.</summary>
        public const double VillagePlantedReach = 90.0;

        /// <summary>Width of the grass balk between two strips, metres.</summary>
        public const float BalkWidth = 1.2f;

        /// <summary>Soft edge of the field paint against the headland, metres.</summary>
        public const float EdgeSoftness = 1.5f;

        /// <summary>Randomness streams (1450-1479 belong to the patchwork).</summary>
        public const int StreamStripCount = 1450, StreamStripLook = 1451;

        /// <summary>A village lane runs on as a field track this far beyond its outer end, metres.</summary>
        public const double TrackLength = 110.0;

        /// <summary>Tiles this far beyond a track's reach (<see cref="TrackLength"/> plus the longest boundary edge) still take it, metres.</summary>
        public const double TrackReachMargin = TrackLength + 120.0;

        /// <summary>Width of a field track, metres.</summary>
        public const float TrackWidth = 2.2f;

        /// <summary>A track ends this far from the road's centre line (it never reaches the road or its verge), metres.</summary>
        public const double TrackRoadClearance = 14.0;

        /// <summary>
        /// The field tracks of a composed village (2026-10-06): each lane whose outer end lies at 60 % or more of the lanes' outermost reach runs on
        /// outward along its own direction for <see cref="TrackLength"/> m as a worn track (painted as a path, so the boundaries it crosses
        /// open a gateway), stopping short of the road. Pure function of the dressing: every tile agrees.
        /// </summary>
        public static void Tracks(Dressing d, Func<double, double, double> roadDistance, List<(double x0, double z0, double x1, double z1, float width)> paths)
        {
            double outer = 0.0;
            foreach (Vector4 lane in d.Paths)
            {
                outer = Math.Max(outer, Math.Sqrt((d.X0 + lane.x - d.CentreX) * (d.X0 + lane.x - d.CentreX) + (d.Z0 + lane.y - d.CentreZ) * (d.Z0 + lane.y - d.CentreZ)));
                outer = Math.Max(outer, Math.Sqrt((d.X0 + lane.z - d.CentreX) * (d.X0 + lane.z - d.CentreX) + (d.Z0 + lane.w - d.CentreZ) * (d.Z0 + lane.w - d.CentreZ)));
            }

            foreach (Vector4 lane in d.Paths)
            {
                double ax = d.X0 + lane.x, az = d.Z0 + lane.y, bx = d.X0 + lane.z, bz = d.Z0 + lane.w;
                double da = (ax - d.CentreX) * (ax - d.CentreX) + (az - d.CentreZ) * (az - d.CentreZ);
                double db = (bx - d.CentreX) * (bx - d.CentreX) + (bz - d.CentreZ) * (bz - d.CentreZ);
                if (da > db) { (ax, bx) = (bx, ax); (az, bz) = (bz, az); db = da; }
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                if (len < 4.0 || Math.Sqrt(db) < 0.6 * outer) continue;
                double ux = (bx - ax) / len, uz = (bz - az) / len;
                // Outward only (the lane's end points away from the centre).
                if (ux * (bx - d.CentreX) + uz * (bz - d.CentreZ) <= 0.0) continue;
                double reach = 0.0;
                for (double t = 4.0; t <= TrackLength; t += 4.0)
                {
                    if (roadDistance(bx + ux * t, bz + uz * t) < TrackRoadClearance) break;
                    reach = t;
                }

                if (reach >= 12.0) paths.Add((bx, bz, bx + ux * reach, bz + uz * reach, TrackWidth));
            }
        }

        /// <summary>Number of strips a field is painted in: 3-6 round a village, else 1 (70 %) or 2; pasture and planted fields are one block.</summary>
        public static int StripCount(MosaicField f, int seed)
        {
            if (f.Close || f.Fallow || f.Planted) return 1;
            double roll = EndlessNoise.Unit(seed, StreamStripCount, f.Id);
            if (f.NearVillage) return 3 + (int)(roll * 4.0);
            return roll < 0.7 ? 1 : 2;
        }

        /// <summary>The look (<see cref="GroundLook.FieldPasture"/> ..) of strip <paramref name="strip"/> of a field.</summary>
        public static int StripLook(MosaicField f, int strip, int seed)
        {
            if (f.Close) return f.Look;
            if (f.Fallow) return GroundLook.FieldPasture;
            // Planted crops stand on their own colour: wheat on gold, rapeseed on yellow, sunflowers on green.
            if (f.Planted) return f.Crop == PropRole.Rapeseed ? GroundLook.FieldRapeseed : f.Crop == PropRole.CropField ? GroundLook.FieldRipeGrain : GroundLook.FieldGreenCrop;
            string look = TileGenerator.FieldLook(f, seed);
            double roll = EndlessNoise.Unit(seed, StreamStripLook, f.Id, strip);
            // The field's own look leads (haystacks stand in stubble, herds graze the fallow); its strips vary around it.
            if (look == PropRole.StubbleField) return roll < 0.45 ? GroundLook.FieldStubble : roll < 0.75 ? GroundLook.FieldRipeGrain : GroundLook.FieldPloughed;
            if (look == PropRole.MeadowStrip) return roll < 0.4 ? GroundLook.FieldHayMeadow : roll < 0.72 ? GroundLook.FieldRapeseed : GroundLook.FieldGreenCrop;
            return roll < 0.5 ? GroundLook.FieldPloughed : roll < 0.8 ? GroundLook.FieldGreenCrop : GroundLook.FieldRipeGrain;
        }

        /// <summary>Width of a kitchen garden's row (soil and greens alternate), metres: two texels of the near tiles' 1 m paint.</summary>
        public const double GardenRowWidth = 2.0;

        /// <summary>Paint texels up to this size draw a close's fine pattern (garden rows, a paddock's worn edge); coarser paint takes the close whole, metres.</summary>
        public const float FineTexel = 1.5f;

        /// <summary>Width of the trodden band along a paddock's fence (the herds walk the line), metres; it narrows and widens along the side.</summary>
        public const double WornBand = 1.4;

        /// <summary>True for the green rows of a kitchen garden (row 0 is soil).</summary>
        public static bool IsGreenRow(int row) => (row & 1) == 1;

        /// <summary>Randomness stream of the phase of a paddock's worn edge (lived-in closes; 1453-1456 are <see cref="CloseContents"/>'s).</summary>
        public const int StreamWornEdge = 1457;

        /// <summary>The look at absolute (<paramref name="x"/>, <paramref name="z"/>) and its weight (1 inside the field, 0 outside), or -1.</summary>
        /// <remarks>
        /// <paramref name="texel"/> is the size of the paint's texel: up to <see cref="FineTexel"/> a kitchen garden is drawn in its rows
        /// and a paddock with its worn edge; coarser (the distant tiles) both are painted whole.
        /// </remarks>
        public static int LookAt(MosaicField f, int seed, double x, double z, out float weight, float texel = 1f)
        {
            weight = 0f;
            double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            double dx = x - f.X, dz = z - f.Z;
            double u = dx * cs - dz * sn, v = dx * sn + dz * cs;
            double inU = f.HalfX - Math.Abs(u), inV = f.HalfZ - Math.Abs(v);
            double inside = Math.Min(inU, inV);
            if (inside < -0.5) return -1;
            weight = Mathf.Clamp01((float)(inside + 0.5) / EdgeSoftness);
            if (f.Close && texel <= FineTexel) return CloseLookAt(f, u, v, inside);
            int n = StripCount(f, seed);
            if (n <= 1) return StripLook(f, 0, seed);
            // Strips run along the field's long side: the short side is cut into n.
            bool alongU = f.HalfX >= f.HalfZ;
            double across = alongU ? v : u, half = alongU ? f.HalfZ : f.HalfX;
            double t = (across + half) / (2.0 * half) * n;
            int strip = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(t)));
            double toLine = Math.Min(t - Math.Floor(t), Math.Ceiling(t) - t) * (2.0 * half / n);
            if (strip > 0 && t - strip < 0.5 && toLine < BalkWidth * 0.5) return GroundLook.FieldBalk;
            if (strip < n - 1 && t - strip >= 0.5 && toLine < BalkWidth * 0.5) return GroundLook.FieldBalk;
            return StripLook(f, strip, seed);
        }

        /// <summary>
        /// The fine look of a close at field coordinates (<paramref name="u"/>, <paramref name="v"/>), <paramref name="inside"/> metres in from
        /// its nearest edge: a kitchen garden's rows across its short side (soil, greens, soil ...; <see cref="GardenRowWidth"/>), a paddock's
        /// grass with a trodden band along the fence (<see cref="WornBand"/>, 0.6-1 of it, varying slowly along each side), else the close's look.
        /// </summary>
        public static int CloseLookAt(MosaicField f, double u, double v, double inside)
        {
            if (f.Look == GroundLook.FieldGarden)
            {
                bool alongU = f.HalfX >= f.HalfZ;
                double across = alongU ? v : u, half = alongU ? f.HalfZ : f.HalfX;
                int row = (int)Math.Floor((across + half) / GardenRowWidth);
                return IsGreenRow(row) ? GroundLook.FieldGardenGreens : GroundLook.FieldGardenSoil;
            }

            if (f.Look == GroundLook.FieldClose)
            {
                // The band breathes along the fence (a slow wave whose phase comes from the close's id), so the frame is not ruled.
                double along = Math.Abs(f.HalfX - Math.Abs(u)) < Math.Abs(f.HalfZ - Math.Abs(v)) ? v : u;
                double phase = (EndlessNoise.Hash(0, StreamWornEdge, f.Id) & 1023) / 1023.0 * 2.0 * Math.PI;
                double band = WornBand * (0.8 + 0.2 * Math.Sin(along * 0.45 + phase));
                if (inside < band) return GroundLook.FieldWorn;
            }

            return f.Look;
        }

        /// <summary>
        /// Share of the grass and flower details a close keeps: a paddock is grazed short grass (its tall clumps go with the pastures' grazing),
        /// its trodden edge nearly bare; a garden's rows are bare (its plants are props); the orchard keeps its sward; a hay plot is mown.
        /// </summary>
        public static float CloseDetailShare(int look)
        {
            switch (look)
            {
                case GroundLook.FieldClose: return 0.85f;
                case GroundLook.FieldWorn: return 0.15f;
                case GroundLook.FieldGarden:
                case GroundLook.FieldGardenSoil: return 0f;
                case GroundLook.FieldGardenGreens: return 0.25f;
                case GroundLook.FieldHayMeadow: return 0.3f;
                default: return DetailShare(look);
            }
        }

        /// <summary>
        /// Share of the grass and flower details a strip keeps (2026-10-06, second pass: the lawn-coloured clumps hid the field colour,
        /// so the crop and bare looks keep only a few; ploughed earth, gardens and tracks are bare; grass looks keep all).
        /// </summary>
        public static float DetailShare(int look)
        {
            switch (look)
            {
                case GroundLook.FieldPloughed:
                case GroundLook.FieldGarden:
                case GroundLook.FieldTrack: return 0f;
                case GroundLook.FieldStubble: return 0.1f;
                case GroundLook.FieldRipeGrain: return 0.1f;
                case GroundLook.FieldRapeseed: return 0.15f;
                case GroundLook.FieldGreenCrop: return 0.2f;
                case GroundLook.FieldClose: return 0.6f;
                case GroundLook.FieldHayMeadow: return 0.7f;
                default: return 1f;
            }
        }

        /// <summary>
        /// Thins a near tile's grass and flower details inside the strips that are not grass (texel (x, z) at window-local
        /// (x + offset + 0.5) · texel, as the pasture grazing), so ploughed and stubble strips are not hidden under a full sward.
        /// </summary>
        public static void ThinDetails(List<DetailLayer> layers, GenWindow w, int offset, int res, float texel, IReadOnlyList<MosaicField> fields, int seed)
        {
            if (layers == null || layers.Count == 0 || fields == null || fields.Count == 0) return;
            foreach (MosaicField f in fields)
            {
                double r = Math.Sqrt(f.HalfX * f.HalfX + f.HalfZ * f.HalfZ) + 1.0;
                int x0 = Math.Max(0, (int)Math.Floor(w.LocalX(f.X - r) / texel) - offset), x1 = Math.Min(res - 1, (int)Math.Ceiling(w.LocalX(f.X + r) / texel) - offset);
                int z0 = Math.Max(0, (int)Math.Floor(w.LocalZ(f.Z - r) / texel) - offset), z1 = Math.Min(res - 1, (int)Math.Ceiling(w.LocalZ(f.Z + r) / texel) - offset);
                for (int z = z0; z <= z1; z++)
                {
                    double az = w.Z0 + (z + offset + 0.5) * texel;
                    for (int x = x0; x <= x1; x++)
                    {
                        double ax = w.X0 + (x + offset + 0.5) * texel;
                        int look = LookAt(f, seed, ax, az, out float a, texel);
                        if (look < 0 || a < 0.5f) continue;
                        float keep = f.Close ? CloseDetailShare(look) : DetailShare(look);
                        if (keep >= 1f) continue;
                        int i = z * res + x;
                        foreach (DetailLayer layer in layers) layer.density[i] = (byte)Math.Round(layer.density[i] * keep);
                    }
                }
            }
        }

        /// <summary>
        /// Paints the fields over a tile's splat (texel (x, z) at local offset + (x + 0.5) · texel, as the wood floor), replacing the
        /// paint inside each field with its strips' mixes over all fourteen endless channels (the crops lead with the field channels).
        /// </summary>
        public static void Paint(float[,,] splat, GenWindow w, float offset, float texel, IReadOnlyList<MosaicField> fields, int seed)
        {
            if (splat == null || fields == null || fields.Count == 0) return;
            int rz = splat.GetLength(0), rx = splat.GetLength(1), layers = splat.GetLength(2);
            int shared = Math.Min(layers, GroundLook.FieldShares[0].Length);
            foreach (MosaicField f in fields)
            {
                double r = Math.Sqrt(f.HalfX * f.HalfX + f.HalfZ * f.HalfZ) + 1.0;
                float lx0 = w.LocalX(f.X - r), lx1 = w.LocalX(f.X + r), lz0 = w.LocalZ(f.Z - r), lz1 = w.LocalZ(f.Z + r);
                int x0 = Math.Max(0, (int)Math.Floor((lx0 - offset) / texel)), x1 = Math.Min(rx - 1, (int)Math.Ceiling((lx1 - offset) / texel));
                int z0 = Math.Max(0, (int)Math.Floor((lz0 - offset) / texel)), z1 = Math.Min(rz - 1, (int)Math.Ceiling((lz1 - offset) / texel));
                for (int z = z0; z <= z1; z++)
                {
                    double az = w.Z0 + offset + (z + 0.5) * texel;
                    for (int x = x0; x <= x1; x++)
                    {
                        double ax = w.X0 + offset + (x + 0.5) * texel;
                        int look = LookAt(f, seed, ax, az, out float a, texel);
                        if (look < 0 || a <= 0f) continue;
                        float[] mix = GroundLook.FieldShares[look];
                        float sum = 0f;
                        for (int c = 0; c < layers; c++)
                        {
                            float target = c < shared ? mix[c] : 0f;
                            float v = splat[z, x, c] + (target - splat[z, x, c]) * a;
                            splat[z, x, c] = v;
                            sum += v;
                        }

                        if (sum > 1e-5f && Math.Abs(sum - 1f) > 1e-4f)
                        {
                            for (int c = 0; c < layers; c++) splat[z, x, c] /= sum;
                        }
                    }
                }
            }
        }
    }
}
