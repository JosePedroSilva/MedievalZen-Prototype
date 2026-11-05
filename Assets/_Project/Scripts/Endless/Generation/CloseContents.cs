using System;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>One thing standing in a village close (absolute position).</summary>
    public struct CloseItem
    {
        /// <summary>Family (<see cref="PropRole.FruitTree"/>, <see cref="PropRole.GardenPlant"/>, <see cref="PropRole.Scarecrow"/>, <see cref="PropRole.Haystack"/>).</summary>
        public string Role;
        /// <summary>Absolute position.</summary>
        public double X, Z;
        /// <summary>Yaw in degrees and uniform scale.</summary>
        public float Yaw, Scale;
        /// <summary>Hash the variant is picked from (<c>Pick % count</c>), so the planner needs no palette.</summary>
        public uint Pick;
    }

    /// <summary>
    /// What stands in the closes round a hamlet or village (2026-10-06, "lived-in villages"): fruit trees in a loose grid in an
    /// orchard (drawn instanced like every tree), a few cabbages and pumpkins along the green rows of a kitchen garden and now and
    /// then a scarecrow, one to three haystacks on two hay plots in three. Paddocks stay open grass for the game's own herds. Pure function of
    /// the close, the seed and its super-cell's settlements (<see cref="CloseSite"/>): every tile and every distance agrees, and no
    /// item stands on the road or its verge, a lane, a field track, a gateway or a settlement's footprint.
    /// </summary>
    public static class CloseContents
    {
        /// <summary>Randomness streams (1453 orchard trees, reserved with the closes; 1454 garden plants, 1455 scarecrows, 1456 haystacks).</summary>
        public const int StreamOrchard = 1453, StreamGarden = 1454, StreamScarecrow = 1455, StreamHay = 1456;

        /// <summary>Spacing of the orchard's grid, metres (6-8 m with the jitter).</summary>
        public const double OrchardSpacing = 7.0;

        /// <summary>How far a tree may stray from its grid point, metres (each axis).</summary>
        public const double OrchardJitter = 0.45;

        /// <summary>A fruit tree keeps this far inside its close's edge (the fence line is 1.6 m further out), metres.</summary>
        public const double TreeInset = 2.8;

        /// <summary>A garden plant, scarecrow or haystack keeps this far inside its close's edge, metres.</summary>
        public const double PropInset = 2.2;

        /// <summary>Share of an orchard's grid points left empty (a gap where an old tree came down).</summary>
        public const double OrchardGap = 0.12;

        /// <summary>Most garden plants in a kitchen garden.</summary>
        public const int MaxGardenPlants = 5;

        /// <summary>Share of kitchen gardens with a scarecrow.</summary>
        public const double ScarecrowShare = 0.4;

        /// <summary>Share of hay plots with no stack left (mown and carted).</summary>
        public const double HayCarted = 0.35;

        /// <summary>Each item keeps this far (plus its own radius) from a lane, track, footprint disc or the road's verge, metres.</summary>
        public const double Margin = 1.0;

        /// <summary>The close's local frame: right = (cos, −sin), forward = (sin, cos) (as <see cref="FieldMosaic.Distance"/>).</summary>
        private static void ToWorld(MosaicField f, double u, double v, out double x, out double z)
        {
            double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            x = f.X + u * cs + v * sn;
            z = f.Z - u * sn + v * cs;
        }

        /// <summary>Radius an item of <paramref name="role"/> keeps clear around it, metres.</summary>
        public static double Radius(string role) => role == PropRole.FruitTree ? 1.2 : role == PropRole.Haystack ? 1.8 : role == PropRole.Scarecrow ? 0.8 : 0.6;

        /// <summary>
        /// Appends what stands in close <paramref name="f"/> to <paramref name="items"/>. <paramref name="site"/> holds its super-cell's
        /// footprints and field tracks; <paramref name="roadDistance"/> is the horizontal distance from a point to the road's centre line,
        /// <paramref name="roadHalfWidth"/> the road's half width. Nothing for a field that is not a close or for a paddock.
        /// </summary>
        public static void Plan(MosaicField f, int seed, CloseSite site, Func<double, double, double> roadDistance, double roadHalfWidth, List<CloseItem> items)
        {
            if (!f.Close) return;
            switch (f.Look)
            {
                case GroundLook.FieldOrchard: Orchard(f, seed, site, roadDistance, roadHalfWidth, items); break;
                case GroundLook.FieldGarden: Garden(f, seed, site, roadDistance, roadHalfWidth, items); break;
                case GroundLook.FieldHayMeadow: Hay(f, seed, site, roadDistance, roadHalfWidth, items); break;
            }
        }

        /// <summary>True when an item of radius <paramref name="r"/> at (x, z) is clear of the road's verge, the lanes, tracks and footprints.</summary>
        public static bool Clear(double x, double z, double r, CloseSite site, Func<double, double, double> roadDistance, double roadHalfWidth)
        {
            if (roadDistance(x, z) < roadHalfWidth + 1.5 + r + Margin) return false;
            return site == null || site.Clearance(x, z) >= r + Margin;
        }

        private static void Orchard(MosaicField f, int seed, CloseSite site, Func<double, double, double> roadDistance, double roadHalf, List<CloseItem> items)
        {
            var rng = new Random(EndlessNoise.Seed(seed, StreamOrchard, f.Id));
            double spanU = f.HalfX - TreeInset, spanV = f.HalfZ - TreeInset;
            if (spanU < 0.0 || spanV < 0.0) return;
            int nu = 1 + (int)Math.Floor(2.0 * spanU / OrchardSpacing), nv = 1 + (int)Math.Floor(2.0 * spanV / OrchardSpacing);
            // The grid is centred in the close; a spacing of 6-8 m along each axis (the span shared out between the rows).
            double du = nu > 1 ? 2.0 * spanU / (nu - 1) : 0.0, dv = nv > 1 ? 2.0 * spanV / (nv - 1) : 0.0;
            du = Math.Min(du, 8.0);
            dv = Math.Min(dv, 8.0);
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    double ju = (rng.NextDouble() * 2.0 - 1.0) * OrchardJitter, jv = (rng.NextDouble() * 2.0 - 1.0) * OrchardJitter;
                    double gap = rng.NextDouble(), yaw = rng.NextDouble() * 360.0, scale = 0.85 + 0.3 * rng.NextDouble();
                    uint pick = (uint)rng.Next();
                    if (gap < OrchardGap) continue;
                    double u = (i - (nu - 1) * 0.5) * du + ju, v = (j - (nv - 1) * 0.5) * dv + jv;
                    u = Math.Max(-spanU, Math.Min(spanU, u));
                    v = Math.Max(-spanV, Math.Min(spanV, v));
                    ToWorld(f, u, v, out double x, out double z);
                    if (!Clear(x, z, Radius(PropRole.FruitTree), site, roadDistance, roadHalf)) continue;
                    items.Add(new CloseItem { Role = PropRole.FruitTree, X = x, Z = z, Yaw = (float)yaw, Scale = (float)scale, Pick = pick });
                }
            }
        }

        private static void Garden(MosaicField f, int seed, CloseSite site, Func<double, double, double> roadDistance, double roadHalf, List<CloseItem> items)
        {
            var rng = new Random(EndlessNoise.Seed(seed, StreamGarden, f.Id));
            // Plants along the centre lines of the green rows (FieldPatchwork.GardenRow), every 2.5-4 m along the row.
            bool alongU = f.HalfX >= f.HalfZ;
            double half = alongU ? f.HalfZ : f.HalfX, length = (alongU ? f.HalfX : f.HalfZ) - PropInset;
            int rows = (int)Math.Floor(2.0 * half / FieldPatchwork.GardenRowWidth);
            int want = 3 + rng.Next(MaxGardenPlants - 2), placed = 0;
            int firstRow = rng.Next(Math.Max(1, rows));
            for (int k = 0; k < rows && placed < want; k++)
            {
                int row = (firstRow + k) % rows;
                if (!FieldPatchwork.IsGreenRow(row)) continue;
                double across = -half + (row + 0.5) * FieldPatchwork.GardenRowWidth;
                if (Math.Abs(across) > half - PropInset * 0.5) continue;
                for (double along = -length + rng.NextDouble() * 2.0; along <= length && placed < want; along += 2.5 + 1.5 * rng.NextDouble())
                {
                    double yaw = rng.NextDouble() * 360.0, scale = 0.9 + 0.2 * rng.NextDouble();
                    uint pick = (uint)rng.Next();
                    if (rng.NextDouble() < 0.35) continue;   // a patchy row, not a hedge of cabbages
                    double u = alongU ? along : across, v = alongU ? across : along;
                    ToWorld(f, u, v, out double x, out double z);
                    if (!Clear(x, z, Radius(PropRole.GardenPlant), site, roadDistance, roadHalf)) continue;
                    items.Add(new CloseItem { Role = PropRole.GardenPlant, X = x, Z = z, Yaw = (float)yaw, Scale = (float)scale, Pick = pick });
                    placed++;
                }
            }

            var crow = new Random(EndlessNoise.Seed(seed, StreamScarecrow, f.Id));
            if (crow.NextDouble() >= ScarecrowShare) return;
            // On a soil row in the middle part of the garden, clear of the plants (a few tries).
            int soilRows = (rows + 1) / 2;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int row = 2 * crow.Next(Math.Max(1, soilRows));
                double across = -half + (row + 0.5) * FieldPatchwork.GardenRowWidth;
                double along = (crow.NextDouble() * 2.0 - 1.0) * Math.Max(0.0, length) * 0.6;
                double cyaw = crow.NextDouble() * 360.0;
                uint cpick = (uint)crow.Next();
                if (Math.Abs(across) > half - PropInset) continue;
                ToWorld(f, alongU ? along : across, alongU ? across : along, out double sx, out double sz);
                bool apart = true;
                foreach (CloseItem it in items)
                {
                    double dx = it.X - sx, dz = it.Z - sz;
                    apart &= it.Role != PropRole.GardenPlant || dx * dx + dz * dz >= 2.0 * 2.0;
                }

                if (!apart || !Clear(sx, sz, Radius(PropRole.Scarecrow), site, roadDistance, roadHalf)) continue;
                items.Add(new CloseItem { Role = PropRole.Scarecrow, X = sx, Z = sz, Yaw = (float)cyaw, Scale = 1f, Pick = cpick });
                break;
            }
        }

        private static void Hay(MosaicField f, int seed, CloseSite site, Func<double, double, double> roadDistance, double roadHalf, List<CloseItem> items)
        {
            var rng = new Random(EndlessNoise.Seed(seed, StreamHay, f.Id));
            // A third of the hay plots are mown and carted (the paint alone); the rest keep one or two stacks drying, three on a large plot.
            if (rng.NextDouble() < HayCarted) return;
            int count = 1 + rng.Next(f.HalfX * f.HalfZ * 4f > 420f ? 3 : 2);
            double spanU = Math.Max(0.0, f.HalfX - PropInset - 1.0), spanV = Math.Max(0.0, f.HalfZ - PropInset - 1.0);
            int start = items.Count;
            for (int k = 0; k < count * 3 && items.Count - start < count; k++)
            {
                double u = (rng.NextDouble() * 2.0 - 1.0) * spanU, v = (rng.NextDouble() * 2.0 - 1.0) * spanV;
                double yaw = rng.NextDouble() * 360.0, scale = 0.85 + 0.3 * rng.NextDouble();
                uint pick = (uint)rng.Next();
                ToWorld(f, u, v, out double x, out double z);
                bool apart = true;
                for (int i = start; i < items.Count && apart; i++)
                {
                    double dx = items[i].X - x, dz = items[i].Z - z;
                    apart = dx * dx + dz * dz >= 5.0 * 5.0;
                }

                if (!apart || !Clear(x, z, Radius(PropRole.Haystack), site, roadDistance, roadHalf)) continue;
                items.Add(new CloseItem { Role = PropRole.Haystack, X = x, Z = z, Yaw = (float)yaw, Scale = (float)scale, Pick = pick });
            }
        }
    }

    /// <summary>
    /// The settlements round a super-cell of the patchwork, as the closes see them: their footprints (kept-clear discs and lanes) and
    /// their field tracks (absolute). Built once per super-cell by <see cref="FieldMosaic"/>.
    /// </summary>
    public sealed class CloseSite
    {
        /// <summary>Footprints of the hamlets, villages and inns near the super-cell.</summary>
        public readonly List<SettlementFootprint> Footprints = new List<SettlementFootprint>();
        /// <summary>Field tracks of the composed villages (absolute segment and width).</summary>
        public readonly List<(double x0, double z0, double x1, double z1, float width)> Tracks = new List<(double, double, double, double, float)>();

        /// <summary>Clearance of point (x, z) from every footprint disc, lane and field track (negative inside), metres.</summary>
        public double Clearance(double x, double z)
        {
            double best = double.MaxValue;
            foreach (SettlementFootprint fp in Footprints)
            {
                double dx = x - fp.CentreX, dz = z - fp.CentreZ;
                if (dx * dx + dz * dz > (fp.Reach + 80.0) * (fp.Reach + 80.0)) continue;
                foreach (var (cx, cz, r) in fp.Discs) best = Math.Min(best, Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) - r);
                foreach (var (x0, z0, x1, z1, half) in fp.Lanes) best = Math.Min(best, SegmentDistance(x, z, x0, z0, x1, z1) - half);
            }

            foreach (var (x0, z0, x1, z1, width) in Tracks) best = Math.Min(best, SegmentDistance(x, z, x0, z0, x1, z1) - width * 0.5);
            return best;
        }

        /// <summary>Distance from (x, z) to the segment (x0, z0)-(x1, z1).</summary>
        public static double SegmentDistance(double x, double z, double x0, double z0, double x1, double z1)
        {
            double ex = x1 - x0, ez = z1 - z0, len2 = ex * ex + ez * ez;
            double t = len2 > 1e-9 ? Math.Max(0.0, Math.Min(1.0, ((x - x0) * ex + (z - z0) * ez) / len2)) : 0.0;
            double px = x0 + ex * t - x, pz = z0 + ez * t - z;
            return Math.Sqrt(px * px + pz * pz);
        }
    }
}
