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
        private readonly ConcurrentDictionary<(long, long), MosaicField[]> cache = new ConcurrentDictionary<(long, long), MosaicField[]>();

        /// <summary>Creates the mosaic.</summary>
        public FieldMosaic(EndlessParams p, RoadCurve road, RegionSequence regions, JourneyPlan plan, TerrainField field)
        {
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
                    foreach (MosaicField f in cache.GetOrAdd((i, j), Compute))
                    {
                        double r = Math.Sqrt(f.HalfX * f.HalfX + f.HalfZ * f.HalfZ) + 4.0;
                        if (f.X + r < x0 || f.X - r > x0 + size || f.Z + r < z0 || f.Z - r > z0 + size) continue;
                        results.Add(f);
                    }
                }
            }
        }

        private MosaicField[] Compute((long, long) key)
        {
            (long ci, long cj) = key;
            double sx0 = ci * SuperCell, sz0 = cj * SuperCell;
            // Any farmland along the road at these z?
            double sA = road.S(sz0 - 600.0), sB = road.S(sz0 + SuperCell + 600.0);
            bool any = false;
            for (Region r = regions.At(sA); r.Start <= sB; r = regions.Get(r.Index + 1)) any |= r.Kind == RegionKind.Farmland;
            if (!any) return Array.Empty<MosaicField>();

            var places = new List<Place>();
            plan.Inns(sA - 300.0, sB + 300.0, places);
            plan.RegionPlaces(sA - 300.0, sB + 300.0, places);
            var avoid = new List<(double x, double z, double r)>();
            foreach (Place place in places)
            {
                if (place.Kind == PlaceKind.Lake) continue;
                SettlementDisc disc = field.Disc(place);
                double reach = place.Kind == PlaceKind.Inn ? place.Radius + 45.0 : place.Radius + 260.0;
                avoid.Add((disc.X, disc.Z, reach));
            }

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

                    if (!inside) continue;
                    if (!Accept(fx, fz, rx, rz, ux, uz, corridor, avoid, out double gateX, out double gateZ, out double centreDistance)) continue;
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
                        Planted = centreDistance <= PlantedReach,
                        GateX = gateX,
                        GateZ = gateZ,
                    });
                }
            }

            return list.ToArray();
        }

        private bool Accept(double fx, double fz, double rx, double rz, double ux, double uz, float corridor, List<(double x, double z, double r)> avoid,
            out double gateX, out double gateZ, out double centreDistance)
        {
            gateX = fx;
            gateZ = fz;
            // Farmland, not too far from the road.
            double zeta = road.Nearest(fx, fz, out centreDistance);
            if (centreDistance > 520.0) return false;
            Region r = regions.Weights(road.S(zeta), out double w, out Region other, out double ow);
            double farm = (r.Kind == RegionKind.Farmland ? w : 0.0) + (other != null && other.Kind == RegionKind.Farmland ? ow : 0.0);
            if (farm < 0.65) return false;
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
            return hi - lo <= 0.09 * diag;
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
