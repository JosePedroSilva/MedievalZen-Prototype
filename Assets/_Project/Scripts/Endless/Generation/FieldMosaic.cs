using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>A farmland field in absolute coordinates.</summary>
    public struct MosaicField
    {
        /// <summary>Stable id (seeds its crops).</summary>
        public long Id;
        /// <summary>Centre (absolute).</summary>
        public double X, Z;
        /// <summary>Half size along the field's local X and Z axes.</summary>
        public float HalfX, HalfZ;
        /// <summary>Rotation about Y in degrees.</summary>
        public float Yaw;
        /// <summary>Crop family (<see cref="PropRole.CropField"/>, <see cref="PropRole.Rapeseed"/>, <see cref="PropRole.Sunflower"/>).</summary>
        public string Crop;
        /// <summary>Ploughed and bare (painted only, nothing planted).</summary>
        public bool Fallow;
        /// <summary>
        /// Crops are planted as props only in fields beside the road (<see cref="FieldMosaic.PlantedReach"/>); further out the
        /// patchwork is paint, which keeps the prop count of a farmland tile in the low thousands.
        /// </summary>
        public bool Planted;
        /// <summary>Absolute road point the field's gate faces.</summary>
        public double GateX, GateZ;
        /// <summary>The super-cell the field belongs to.</summary>
        public long CellI, CellJ;
        /// <summary>The field's cell on its super-cell's grid (see <see cref="MosaicGrid"/>).</summary>
        public int A, B;
        /// <summary>The field lies within <see cref="FieldPatchwork.VillageReach"/> of a hamlet or village (painted in strips).</summary>
        public bool NearVillage;
        /// <summary>
        /// A close of the village's inner ring (2026-10-06): a small enclosure cut from a grid cell the settlement's footprint
        /// overlaps (<see cref="FieldMosaic.Closes"/>), painted whole in <see cref="Look"/> (paddock, kitchen garden, orchard, hay).
        /// </summary>
        public bool Close;
        /// <summary>The paint look of a close (<see cref="GroundLook.FieldClose"/> ..); read only when <see cref="Close"/>.</summary>
        public int Look;
        /// <summary>A close's place in its cell's subdivision (<see cref="Iu"/> of <see cref="Nu"/> along right, <see cref="Iv"/> of <see cref="Nv"/> along forward).</summary>
        public byte Iu, Iv, Nu, Nv;
    }

    /// <summary>The turned grid of one super-cell: field (a, b) is centred near Centre + a·CellU·right + b·CellV·forward.</summary>
    public struct MosaicGrid
    {
        /// <summary>Centre of the super-cell (absolute).</summary>
        public double CentreX, CentreZ;
        /// <summary>Cosine and sine of the grid's turn (right = (cos, −sin), forward = (sin, cos)).</summary>
        public double Cos, Sin;
        /// <summary>Cell sizes along right and forward, metres.</summary>
        public double CellU, CellV;
        /// <summary>Turn in degrees (the fields' yaw).</summary>
        public double Angle;
        /// <summary>Absolute bounds of the super-cell.</summary>
        public double X0, Z0;

        /// <summary>Absolute position of grid point (u, v) (metres along right and forward from the centre).</summary>
        public void ToWorld(double u, double v, out double x, out double z)
        {
            x = CentreX + u * Cos + v * Sin;
            z = CentreZ - u * Sin + v * Cos;
        }
    }

    /// <summary>
    /// The patchwork of farmland: fields laid on a grid in 1024 m super-cells of absolute space, each super-cell turned by its
    /// own seeded angle, a field per grid cell where the ground is farmland, gentle, dry, clear of the road, the settlements
    /// and their own fields. A third lie fallow (ploughed earth, painted only); the rest are planted, fenced and given
    /// scarecrows by the finite world's field filler. Pure functions of the seed, cached per super-cell, so every tile that
    /// sees a field agrees on it. Thread-safe.
    /// </summary>
    public sealed class FieldMosaic
    {
        /// <summary>Edge of a super-cell, metres.</summary>
        public const double SuperCell = 1024.0;

        /// <summary>Fields whose centre is within this distance of the road are planted (crops, fences, scarecrows).</summary>
        public const double PlantedReach = 110.0;

        private readonly EndlessParams p;
        private readonly RoadCurve road;
        private readonly RegionSequence regions;
        private readonly JourneyPlan plan;
        private readonly TerrainField field;
        private readonly DressingCache dressings;
        private readonly ConcurrentDictionary<(long, long), CellData> cache = new ConcurrentDictionary<(long, long), CellData>();

        /// <summary>A super-cell's fields and the settlements its closes keep clear of.</summary>
        private sealed class CellData
        {
            public MosaicField[] Fields;
            public CloseSite Site;
        }

        /// <summary>Creates the mosaic.</summary>
        /// <remarks>
        /// With <paramref name="dressings"/> (2026-10-06) the fields keep off each settlement's dressed footprint
        /// (<see cref="SettlementFootprint"/>) instead of a disc of the place radius + 260 m, and the land round every hamlet and
        /// village becomes fields whatever the region's farmland weight (not in forests or passes).
        /// </remarks>
        public FieldMosaic(EndlessParams p, RoadCurve road, RegionSequence regions, JourneyPlan plan, TerrainField field, DressingCache dressings = null)
        {
            this.dressings = dressings;
            this.p = p;
            this.road = road;
            this.regions = regions;
            this.plan = plan;
            this.field = field;
        }

        /// <summary>Appends the fields that overlap the absolute square [x0, x0 + size] × [z0, z0 + size].</summary>
        public void Overlapping(double x0, double z0, double size, List<MosaicField> results)
        {
            long i0 = (long)Math.Floor(x0 / SuperCell), i1 = (long)Math.Floor((x0 + size) / SuperCell);
            long j0 = (long)Math.Floor(z0 / SuperCell), j1 = (long)Math.Floor((z0 + size) / SuperCell);
            for (long j = j0; j <= j1; j++)
            {
                for (long i = i0; i <= i1; i++)
                {
                    foreach (MosaicField f in cache.GetOrAdd((i, j), Compute).Fields)
                    {
                        double r = Math.Sqrt(f.HalfX * f.HalfX + f.HalfZ * f.HalfZ) + 4.0;
                        if (f.X + r < x0 || f.X - r > x0 + size || f.Z + r < z0 || f.Z - r > z0 + size) continue;
                        results.Add(f);
                    }
                }
            }
        }

        /// <summary>The fields of super-cell (<paramref name="ci"/>, <paramref name="cj"/>) (cached; shared, do not modify).</summary>
        public MosaicField[] Cell(long ci, long cj) => cache.GetOrAdd((ci, cj), Compute).Fields;

        /// <summary>The settlements round super-cell (<paramref name="ci"/>, <paramref name="cj"/>): footprints and field tracks (cached; shared).</summary>
        public CloseSite SiteOf(long ci, long cj) => cache.GetOrAdd((ci, cj), Compute).Site;

        /// <summary>The grid of super-cell (<paramref name="ci"/>, <paramref name="cj"/>) (the same numbers its fields were laid out with).</summary>
        public MosaicGrid GridOf(long ci, long cj)
        {
            double angle = (EndlessNoise.Unit(p.seed, 801, ci, cj) - 0.5) * 70.0;
            double rad = angle * Math.PI / 180.0;
            return new MosaicGrid
            {
                CentreX = ci * SuperCell + SuperCell * 0.5,
                CentreZ = cj * SuperCell + SuperCell * 0.5,
                Cos = Math.Cos(rad),
                Sin = Math.Sin(rad),
                CellU = 66.0 + 18.0 * EndlessNoise.Unit(p.seed, 802, ci, cj),
                CellV = 46.0 + 14.0 * EndlessNoise.Unit(p.seed, 803, ci, cj),
                Angle = angle,
                X0 = ci * SuperCell,
                Z0 = cj * SuperCell,
            };
        }

        /// <summary>Horizontal distance from (x, z) to the field's rectangle (0 inside).</summary>
        public static double Distance(MosaicField f, double x, double z)
        {
            double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            double dx = x - f.X, dz = z - f.Z;
            double u = Math.Abs(dx * cs - dz * sn) - f.HalfX, v = Math.Abs(dx * sn + dz * cs) - f.HalfZ;
            double ou = Math.Max(0.0, u), ov = Math.Max(0.0, v);
            return Math.Sqrt(ou * ou + ov * ov);
        }

        private CellData Compute((long, long) key)
        {
            (long ci, long cj) = key;
            double sx0 = ci * SuperCell, sz0 = cj * SuperCell;
            // Any farmland along the road at these z?
            double sA = road.S(sz0 - 600.0), sB = road.S(sz0 + SuperCell + 600.0);
            bool any = false;
            for (Region r = regions.At(sA); r.Start <= sB; r = regions.Get(r.Index + 1)) any |= r.Kind == RegionKind.Farmland;

            var places = new List<Place>();
            plan.Inns(sA - 300.0, sB + 300.0, places);
            plan.RegionPlaces(sA - 300.0, sB + 300.0, places);
            places.Sort((x, y) => x.S != y.S ? x.S.CompareTo(y.S) : x.Id.CompareTo(y.Id));
            var avoid = new List<(double x, double z, double r)>();
            var footprints = new List<SettlementFootprint>();
            var site = new CloseSite();
            foreach (Place place in places)
            {
                if (place.Kind == PlaceKind.Lake) continue;
                if (dressings != null && place.IsSettlement)
                {
                    Dressing dressing = dressings.Settlement(place);
                    SettlementFootprint fp = SettlementFootprint.Of(place, dressing);
                    footprints.Add(fp);
                    site.Footprints.Add(fp);
                    // The village's field tracks (as the tiles paint them): nothing stands on them in a close.
                    if (fp.Village && !dressing.PaintSite) FieldPatchwork.Tracks(dressing, (x, z) => { road.Nearest(x, z, out double rd); return rd; }, site.Tracks);
                    RegionKind kind = regions.At(place.S).Kind;
                    if (fp.Village && kind != RegionKind.Forest && kind != RegionKind.HighlandPass) any = true;
                    continue;
                }

                SettlementDisc disc = field.Disc(place);
                double reach = place.Kind == PlaceKind.Inn ? place.Radius + 45.0 : place.Radius + 260.0;
                avoid.Add((disc.X, disc.Z, reach));
            }

            if (!any) return new CellData { Fields = Array.Empty<MosaicField>(), Site = site };

            double angle = (EndlessNoise.Unit(p.seed, 801, ci, cj) - 0.5) * 70.0;
            double rad = angle * Math.PI / 180.0;
            double cs = Math.Cos(rad), sn = Math.Sin(rad);
            double cellU = 66.0 + 18.0 * EndlessNoise.Unit(p.seed, 802, ci, cj);
            double cellV = 46.0 + 14.0 * EndlessNoise.Unit(p.seed, 803, ci, cj);
            double cx = sx0 + SuperCell * 0.5, cz = sz0 + SuperCell * 0.5;
            var list = new List<MosaicField>();
            int n = (int)Math.Ceiling(SuperCell * 0.75 / Math.Min(cellU, cellV));
            float corridor = p.roadWidth * 0.5f + 8f;
            for (int b = -n; b <= n; b++)
            {
                for (int a = -n; a <= n; a++)
                {
                    long id = ((ci * 73856093L) ^ (cj * 19349663L)) * 4096 + (b + 2048) * 64 + (a + 32);
                    ulong h = EndlessNoise.Hash(p.seed, 804, id);
                    double u = a * cellU + (EndlessNoise.Unit(h) - 0.5) * 6.0;
                    double v = b * cellV + (EndlessNoise.Unit(EndlessNoise.Hash(p.seed, 805, id)) - 0.5) * 6.0;
                    // Local axes: right = (cos, -sin), forward = (sin, cos) for Unity yaw (as FieldPatch.Contains).
                    double fx = cx + u * cs + v * sn, fz = cz - u * sn + v * cs;
                    float halfX = (float)(cellU * 0.5 - 3.0 - 3.0 * EndlessNoise.Unit(p.seed, 806, id));
                    float halfZ = (float)(cellV * 0.5 - 3.0 - 3.0 * EndlessNoise.Unit(p.seed, 807, id));
                    double rx = halfX * cs, rz = -halfX * sn, ux = halfZ * sn, uz = halfZ * cs;
                    // Inside this super-cell (no overlap with a neighbour's differently turned grid).
                    bool inside = true;
                    for (int k = 0; k < 4 && inside; k++)
                    {
                        double px = fx + ((k & 1) == 0 ? -1 : 1) * rx + ((k & 2) == 0 ? -1 : 1) * ux;
                        double pz = fz + ((k & 1) == 0 ? -1 : 1) * rz + ((k & 2) == 0 ? -1 : 1) * uz;
                        inside = px > sx0 + 4.0 && px < sx0 + SuperCell - 4.0 && pz > sz0 + 4.0 && pz < sz0 + SuperCell - 4.0;
                    }

                    // A cell across the super-cell's edge is no field, but round a village its closes inside the edge are kept (2026-10-06:
                    // the seam left a lawn band one field wide through some villages).
                    if (!inside && !NearAnyVillage(fx, fz, footprints)) continue;
                    // Round a hamlet or village (not in a forest or a pass) the land is fields whatever the farmland weight.
                    bool nearVillage = false;
                    double footprintClearance = double.MaxValue;
                    foreach (SettlementFootprint fp in footprints)
                    {
                        double dx = fx - fp.CentreX, dz = fz - fp.CentreZ;
                        double d2 = dx * dx + dz * dz;
                        if (d2 > (fp.Reach + FieldPatchwork.VillageReach) * (fp.Reach + FieldPatchwork.VillageReach)) continue;
                        if (fp.Village && d2 < FieldPatchwork.VillageReach * FieldPatchwork.VillageReach)
                        {
                            RegionKind kind = regions.At(road.S(fz)).Kind;
                            nearVillage |= kind != RegionKind.Forest && kind != RegionKind.HighlandPass;
                        }

                        footprintClearance = Math.Min(footprintClearance, fp.Clearance(fx, fz, halfX, halfZ, cs, sn));
                    }

                    if (!inside || footprintClearance < FieldPatchwork.FootprintMargin)
                    {
                        // The village's inner ring: the cell the houses cut becomes closes, gardens and orchards (2026-10-06).
                        if (nearVillage) Closes(id, a, b, cellU, cellV, cx, cz, sx0, sz0, cs, sn, angle, corridor, avoid, footprints, ci, cj, list);
                        continue;
                    }

                    if (!Accept(fx, fz, rx, rz, ux, uz, corridor, avoid, nearVillage, out double gateX, out double gateZ, out double centreDistance)) continue;
                    double roll = EndlessNoise.Unit(p.seed, 808, id);
                    string crop = roll < 0.55 ? PropRole.CropField : roll < 0.88 ? PropRole.Rapeseed : PropRole.Sunflower;
                    list.Add(new MosaicField
                    {
                        Id = id,
                        X = fx,
                        Z = fz,
                        HalfX = halfX,
                        HalfZ = halfZ,
                        Yaw = (float)angle,
                        Crop = crop,
                        Fallow = EndlessNoise.Unit(p.seed, 809, id) < 0.34,
                        Planted = centreDistance <= PlantedReach || (nearVillage && footprintClearance <= FieldPatchwork.VillagePlantedReach),
                        NearVillage = nearVillage,
                        GateX = gateX,
                        GateZ = gateZ,
                        CellI = ci,
                        CellJ = cj,
                        A = a,
                        B = b,
                    });
                }
            }

            return new CellData { Fields = list.ToArray(), Site = site };
        }

        /// <summary>A close keeps this far off the settlement's footprint (its fence or hedge stands on the cell's lines between), metres.</summary>
        public const double CloseMargin = 3.0;

        /// <summary>Target size of a close along the grid's right and forward axes, metres.</summary>
        public const double CloseU = 24.0, CloseV = 20.0;

        /// <summary>Randomness streams of the closes (1452 look, 1453 orchard trees).</summary>
        public const int StreamCloseLook = 1452, StreamOrchardTrees = 1453;

        /// <summary>
        /// The village's inner ring (2026-10-06): grid cell (<paramref name="a"/>, <paramref name="b"/>) is overlapped by a hamlet's or
        /// village's footprint, so instead of a field it is cut into closes of about <see cref="CloseU"/> × <see cref="CloseV"/> m on the
        /// cell's own grid, and every close clear of the footprint (<see cref="CloseMargin"/>), the road and the other settlements is kept:
        /// paddocks next to the plots (grazed, trodden, fallow for the herds), kitchen gardens hard by the houses, orchards and hay closes.
        /// The fields of the patchwork then begin in the next cell, within about one cell (46-84 m) of the outer houses.
        /// </summary>
        private void Closes(long id, int a, int b, double cellU, double cellV, double cx, double cz, double sx0, double sz0, double cs, double sn, double angle, float corridor,
            List<(double x, double z, double r)> avoid, List<SettlementFootprint> footprints, long ci, long cj, List<MosaicField> list)
        {
            int nu = Math.Max(1, (int)Math.Round(cellU / CloseU)), nv = Math.Max(1, (int)Math.Round(cellV / CloseV));
            double su = cellU / nu, sv = cellV / nv;
            float halfX = (float)(su * 0.5 - 1.6), halfZ = (float)(sv * 0.5 - 1.6);
            double rx = halfX * cs, rz = -halfX * sn, ux = halfZ * sn, uz = halfZ * cs;
            for (int iv = 0; iv < nv; iv++)
            {
                for (int iu = 0; iu < nu; iu++)
                {
                    double u = (a - 0.5) * cellU + (iu + 0.5) * su, v = (b - 0.5) * cellV + (iv + 0.5) * sv;
                    double fx = cx + u * cs + v * sn, fz = cz - u * sn + v * cs;
                    double clearance = double.MaxValue;
                    foreach (SettlementFootprint fp in footprints)
                    {
                        double dx = fx - fp.CentreX, dz = fz - fp.CentreZ;
                        if (dx * dx + dz * dz > (fp.Reach + 120.0) * (fp.Reach + 120.0)) continue;
                        clearance = Math.Min(clearance, fp.Clearance(fx, fz, halfX, halfZ, cs, sn));
                    }

                    if (clearance < CloseMargin) continue;
                    bool inside = true;
                    for (int k = 0; k < 4 && inside; k++)
                    {
                        double px = fx + ((k & 1) == 0 ? -1 : 1) * rx + ((k & 2) == 0 ? -1 : 1) * ux;
                        double pz = fz + ((k & 1) == 0 ? -1 : 1) * rz + ((k & 2) == 0 ? -1 : 1) * uz;
                        inside = px > sx0 + 4.0 && px < sx0 + SuperCell - 4.0 && pz > sz0 + 4.0 && pz < sz0 + SuperCell - 4.0;
                    }

                    if (!inside) continue;
                    if (!Accept(fx, fz, rx, rz, ux, uz, corridor, avoid, true, out double gateX, out double gateZ, out _)) continue;
                    long closeId = unchecked(id * 64 + 0x3C1053L + iv * 8 + iu);
                    double roll = EndlessNoise.Unit(p.seed, StreamCloseLook, closeId);
                    // Gardens lie hard by the houses; paddocks are the commonest; orchards and hay closes further out. Lived-in closes
                    // (2026-10-06): gardens up to 18 m from the footprint (was 14), orchards 14 % (was 20 %: ~5 instanced fruit trees each).
                    int look = clearance < 18.0 && roll < 0.4 ? GroundLook.FieldGarden
                        : roll < 0.62 ? GroundLook.FieldClose
                        : roll < 0.76 ? GroundLook.FieldOrchard
                        : GroundLook.FieldHayMeadow;
                    list.Add(new MosaicField
                    {
                        Id = closeId,
                        X = fx,
                        Z = fz,
                        HalfX = halfX,
                        HalfZ = halfZ,
                        Yaw = (float)angle,
                        Crop = PropRole.Sunflower,   // never stubble: no haystacks of the patchwork's own in a close
                        Fallow = look == GroundLook.FieldClose,   // the paddocks are pasture for the herds
                        Planted = false,
                        NearVillage = true,
                        GateX = gateX,
                        GateZ = gateZ,
                        CellI = ci,
                        CellJ = cj,
                        A = a,
                        B = b,
                        Close = true,
                        Look = look,
                        Iu = (byte)iu,
                        Iv = (byte)iv,
                        Nu = (byte)nu,
                        Nv = (byte)nv,
                    });
                }
            }
        }

        /// <summary>True when (x, z) lies within <see cref="FieldPatchwork.VillageReach"/> of a hamlet's or village's centre.</summary>
        private static bool NearAnyVillage(double x, double z, List<SettlementFootprint> footprints)
        {
            foreach (SettlementFootprint fp in footprints)
            {
                double dx = x - fp.CentreX, dz = z - fp.CentreZ;
                if (fp.Village && dx * dx + dz * dz < FieldPatchwork.VillageReach * FieldPatchwork.VillageReach) return true;
            }

            return false;
        }

        private bool Accept(double fx, double fz, double rx, double rz, double ux, double uz, float corridor, List<(double x, double z, double r)> avoid,
            bool nearVillage, out double gateX, out double gateZ, out double centreDistance)
        {
            gateX = fx;
            gateZ = fz;
            // Farmland, not too far from the road.
            double zeta = road.Nearest(fx, fz, out centreDistance);
            if (centreDistance > 520.0) return false;
            Region r = regions.Weights(road.S(zeta), out double w, out Region other, out double ow);
            double farm = (r.Kind == RegionKind.Farmland ? w : 0.0) + (other != null && other.Kind == RegionKind.Farmland ? ow : 0.0);
            if (farm < 0.65 && !nearVillage) return false;
            gateX = road.X(zeta);
            gateZ = zeta;
            foreach (var (x, z, rad) in avoid)
            {
                double dx = fx - x, dz = fz - z;
                double reach = rad + Math.Sqrt(rx * rx + rz * rz + ux * ux + uz * uz);
                if (dx * dx + dz * dz < reach * reach) return false;
            }

            // Clear of the road, gentle and dry over a 3 × 3 grid of the field (plus a fence margin).
            float lo = float.MaxValue, hi = float.MinValue;
            for (int b = -1; b <= 1; b++)
            {
                for (int a = -1; a <= 1; a++)
                {
                    double px = fx + a * rx * 1.08 + b * ux * 1.08, pz = fz + a * rz * 1.08 + b * uz * 1.08;
                    road.Nearest(px, pz, out double d);
                    if (d < corridor + 4.0) return false;
                    float h = field.Height(px, pz);
                    if (field.WaterHeight(px, pz) > 0f) return false;
                    lo = Mathf.Min(lo, h);
                    hi = Mathf.Max(hi, h);
                }
            }

            double diag = 2.0 * Math.Sqrt(rx * rx + rz * rz + ux * ux + uz * uz);
            if (hi - lo > 0.09 * diag) return false;

            // Clear of the streams (their banks included).
            return field.Streams == null || field.Streams.Clearance(fx, fz, diag * 0.5 + 10.0) > diag * 0.5 + 4.0;
        }

        /// <summary>The field as a <see cref="FieldPatch"/> in a window whose local origin is (x0, z0).</summary>
        public static FieldPatch ToPatch(MosaicField f, double x0, double z0, float y)
        {
            return new FieldPatch
            {
                center = new Vector3((float)(f.X - x0), y, (float)(f.Z - z0)),
                halfExtents = new Vector2(f.HalfX, f.HalfZ),
                yaw = f.Yaw,
                settlementId = -1,
                crop = f.Crop,
            };
        }
    }
}
