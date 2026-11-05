using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// The endless road's land for the wildlife planner, read from the generator's public API (pure and thread-safe, like the
    /// generator). With the real <see cref="EndlessGenerator"/> it also knows land cover (<see cref="TerrainField.Cover(TerrainField.Row, double, float, float, out float, out float, out float, out float)"/>),
    /// lake shores and the fallow fields of the farmland mosaic; the built-in stub generator gives regions, heights, water and
    /// places only (no pastures).
    /// </summary>
    public sealed class EndlessHabitat : IWildlifeHabitat
    {
        private readonly IEndlessGenerator generator;
        private readonly EndlessGenerator real;
        private readonly float corridor;

        /// <summary>Wraps a generator.</summary>
        public EndlessHabitat(IEndlessGenerator generator)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            real = generator as EndlessGenerator;
            corridor = (generator.Params != null ? generator.Params.roadWidth : 4f) * 0.5f + 10f;
        }

        /// <inheritdoc />
        public int Seed => generator.Params != null ? generator.Params.seed : 0;

        /// <inheritdoc />
        public bool ThreadSafe => true;

        /// <inheritdoc />
        public float Corridor => corridor;

        /// <summary>True when the generator is the real one (fields and land cover are known).</summary>
        public bool IsDetailed => real != null;

        /// <inheritdoc />
        public HabitatSample Sample(double x, double z)
        {
            var sample = new HabitatSample { Valid = true };
            double s;
            if (real != null)
            {
                double zeta = real.Curve.Nearest(x, z, out double d);
                s = real.Curve.S(zeta);
                sample.RoadDistance = (float)d;
                TerrainField.Row row = real.Terrain.RowAt(z);
                float h = real.Terrain.Height(row, x);
                sample.Height = h;
                float hx = real.Terrain.Height(row, x + 2.0);
                float hz = real.Terrain.Height(x, z + 2.0);
                sample.Slope = (float)Math.Sqrt((hx - h) * (hx - h) + (hz - h) * (hz - h)) / 2f;
                sample.Water = real.Terrain.WaterHeight(x, z) > 0f;
                real.Terrain.Cover(row, x, h, sample.Slope, out sample.Forest, out sample.Highland, out sample.Moist, out sample.Farm);
                sample.Shore = real.Terrain.Shore(row, x, h);
            }
            else
            {
                s = generator.NearestS(x, z, double.NaN);
                RoadPoint p = generator.Road(s);
                sample.RoadDistance = (float)Math.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));
                float h = generator.Height(x, z);
                sample.Height = h;
                float hx = generator.Height(x + 2.0, z), hz = generator.Height(x, z + 2.0);
                sample.Slope = (float)Math.Sqrt((hx - h) * (hx - h) + (hz - h) * (hz - h)) / 2f;
                sample.Water = generator.WaterHeight(x, z) > 0f;
                RegionKind kind = generator.Region(s).Kind;
                sample.Forest = kind == RegionKind.Forest ? 0.8f : 0.1f;
                sample.Highland = kind == RegionKind.HighlandPass ? 0.8f : kind == RegionKind.Downs ? 0.3f : 0f;
                sample.Farm = kind == RegionKind.Farmland ? 0.6f : 0f;
                sample.Moist = kind == RegionKind.LakeCountry ? 0.85f : 0.5f;
                if (!sample.Water)
                {
                    for (int i = 0; i < 4 && sample.Shore == 0f; i++)
                    {
                        double a = i * Math.PI * 0.5;
                        if (generator.WaterHeight(x + Math.Cos(a) * 10.0, z + Math.Sin(a) * 10.0) > 0f) sample.Shore = 0.8f;
                    }
                }
            }

            sample.Region = generator.Region(s).Kind;
            return sample;
        }

        /// <inheritdoc />
        public bool IsWater(double x, double z) => generator.WaterHeight(x, z) > 0f;

        /// <inheritdoc />
        public void Settlements(double x0, double z0, double size, List<HabitatSettlement> results)
        {
            double sA = generator.NearestS(x0, z0, double.NaN), sB = generator.NearestS(x0 + size, z0 + size, double.NaN);
            double sC = generator.NearestS(x0 + size, z0, double.NaN), sD = generator.NearestS(x0, z0 + size, double.NaN);
            double lo = Math.Min(Math.Min(sA, sB), Math.Min(sC, sD)) - 400.0, hi = Math.Max(Math.Max(sA, sB), Math.Max(sC, sD)) + 400.0;
            var found = new List<Waypoint>();
            generator.Waypoints(lo, hi, found);
            foreach (Waypoint w in found)
            {
                if (!w.IsSettlement) continue;
                float radius = w.Radius > 1f ? w.Radius : SettlementSite.RadiusFor(w.Kind == WaypointKind.Inn ? MedievalZen.Roads.SettlementType.Inn : MedievalZen.Roads.SettlementType.Village);
                double nx = Math.Max(x0, Math.Min(w.X, x0 + size)), nz = Math.Max(z0, Math.Min(w.Z, z0 + size));
                double reach = radius + 120.0;
                if ((nx - w.X) * (nx - w.X) + (nz - w.Z) * (nz - w.Z) > reach * reach) continue;
                results.Add(new HabitatSettlement
                {
                    Id = w.Id,
                    Kind = w.Kind == WaypointKind.Inn ? HabitatSettlementKind.Inn : w.Kind == WaypointKind.Hamlet ? HabitatSettlementKind.Hamlet : HabitatSettlementKind.Village,
                    X = w.X,
                    Z = w.Z,
                    Radius = radius,
                    RoadHeadingDeg = w.RoadHeadingDeg,
                });
            }
        }

        /// <inheritdoc />
        public void Pastures(double x0, double z0, double size, List<HabitatPasture> results)
        {
            if (real == null) return;
            var fields = new List<MosaicField>();
            real.Fields.Overlapping(x0, z0, size, fields);
            foreach (MosaicField f in fields)
            {
                if (!f.Fallow || f.X < x0 || f.X >= x0 + size || f.Z < z0 || f.Z >= z0 + size) continue;
                results.Add(new HabitatPasture { Id = f.Id, X = f.X, Z = f.Z, HalfX = f.HalfX, HalfZ = f.HalfZ, Yaw = f.Yaw });
            }
        }
    }
}
