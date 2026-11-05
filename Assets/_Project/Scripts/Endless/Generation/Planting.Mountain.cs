using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// The mountain passes' pines (2026-10-05; endless only): on the lower flanks of a pass range, in loose clumps, thickest on the
    /// valley floor's edge and the first ledges and thinning upward by height above the road (not by absolute altitude: the passes
    /// lie at the tree line), more along the gullies, none on the talus fans, on faces steeper than about 40 degrees, on the road's
    /// corridor or its verges. Planted on near and distant tiles alike from an absolute 7 m grid, so every window agrees.
    /// Randomness streams 1276-1278.
    /// </summary>
    public sealed partial class Planting
    {
        /// <summary>Randomness streams (EndlessNoise) of the pass pines: 1276-1278.</summary>
        private const int StreamPine = 1276, StreamPineClump = 1277, StreamPinePick = 1278;

        /// <summary>Edge of the absolute grid the pass pines are planted on, metres.</summary>
        public const double PineGrid = 7.0;

        /// <summary>Pines per hectare on the lower flanks of a range where everything favours them (before the clumping).</summary>
        public const float PassPinesPerHectare = 48f;

        /// <summary>Normalised height (of the height range) above which no pass pine stands (the scatter's pines stop at 0.67).</summary>
        public const float PassPineCeiling = 0.8f;

        /// <summary>Steepest ground (rise over run) a pass pine stands on; they thin from 0.45 (24 degrees).</summary>
        public const float PassPineMaxSlope = 0.85f;

        /// <summary>A pass pine keeps this far from the road's corridor edge (the scatter corridor), metres.</summary>
        public const float PassPineVerge = 2.5f;

        /// <summary>Randomness streams of the moderate slopes' low shrubs (juniper-like drifts, 2026-10-06): 1341-1343.</summary>
        private const int StreamShrub = 1341, StreamShrubDrift = 1342, StreamShrubPick = 1343;

        /// <summary>Extra share of the pass pines' density on the moderate slopes (<see cref="TerrainShaper.ModerateSlope"/>).</summary>
        public const float PassPineModerateBoost = 0.5f;

        /// <summary>Edge of the absolute grid the moderate slopes' low shrubs are planted on, metres.</summary>
        public const double ShrubGrid = 5.0;

        /// <summary>Low shrubs per hectare on the moderate slopes of a range inside a drift (before the height thinning).</summary>
        public const float PassShrubsPerHectare = 32f;

        private GradientNoise pineClump, shrubDrift;

        private void InitMountain()
        {
            pineClump = new GradientNoise(EndlessNoise.Seed(p.seed, StreamPineClump, 0));
            shrubDrift = new GradientNoise(EndlessNoise.Seed(p.seed, StreamShrubDrift, 0));
        }

        /// <summary>Share of the low shrubs' density at <paramref name="above"/> metres above the road: full to 60 m, a quarter by 160 m, none by 230 m.</summary>
        public static float PassShrubHeightFactor(float above)
        {
            if (above <= 60f) return 1f;
            if (above <= 160f) return Mathf.Lerp(1f, 0.25f, TerrainShaper.SmoothStep01((above - 60f) / 100f));
            return Mathf.Lerp(0.25f, 0f, TerrainShaper.SmoothStep01((above - 160f) / 70f));
        }

        /// <summary>
        /// Share of the pass pines' density at <paramref name="above"/> metres above the road: full up to 30 m, an eighth by 110 m, a
        /// fiftieth by 170 m.
        /// </summary>
        public static float PassPineHeightFactor(float above)
        {
            if (above <= 30f) return 1f;
            if (above <= 110f) return Mathf.Lerp(1f, 0.12f, TerrainShaper.SmoothStep01((above - 30f) / 80f));
            return Mathf.Lerp(0.12f, 0.02f, TerrainShaper.SmoothStep01((above - 110f) / 60f));
        }

        private void MountainPines(Context ctx, List<TreePlacement> trees, List<PropPlacement> bushes)
        {
            if (bushes != null) MountainShrubs(ctx, bushes);
            GenWindow w = ctx.W;
            float clearance = w.Params.ScatterCorridor + PassPineVerge;
            long gx0 = (long)Math.Floor((w.X0 + 2.0) / PineGrid), gx1 = (long)Math.Floor((w.X0 + w.Size - 2.0) / PineGrid);
            long gz0 = (long)Math.Floor((w.Z0 + 2.0) / PineGrid), gz1 = (long)Math.Floor((w.Z0 + w.Size - 2.0) / PineGrid);
            double cellArea = PineGrid * PineGrid / 10000.0;
            for (long gz = gz0; gz <= gz1; gz++)
            {
                double rowS = double.NaN;
                float rowRoad = 0f;
                for (long gx = gx0; gx <= gx1; gx++)
                {
                    double u = EndlessNoise.Unit(p.seed, StreamPine, gx, gz);
                    if (u >= PassPinesPerHectare * (1f + PassPineModerateBoost) * cellArea) continue;   // the most any cell can get
                    double x = (gx + 0.1 + 0.8 * EndlessNoise.Unit(p.seed, StreamPine, gx, gz + 7919)) * PineGrid;
                    double z = (gz + 0.1 + 0.8 * EndlessNoise.Unit(p.seed, StreamPine, gx + 7919, gz)) * PineGrid;
                    float lx = w.LocalX(x), lz = w.LocalZ(z);
                    if (lx < 2f || lz < 2f || lx > w.Size - 2f || lz > w.Size - 2f) continue;
                    CoverSample cv = w.Cover.Sample(lx, lz);
                    if (cv.mountain <= 0.05f || cv.isWater) continue;
                    if (double.IsNaN(rowS))
                    {
                        rowS = road.S(gz * PineGrid);
                        rowRoad = profile.BaseHeight(rowS);
                    }

                    float slopeFactor = 1f - TerrainShaper.SmoothStep01((cv.slope - 0.45f) / (PassPineMaxSlope - 0.45f));
                    float clump = 0.25f + 1.5f * (float)EndlessNoise.Smooth(-0.6, 0.8, EndlessNoise.FbmN(pineClump, x, z, 1.0 / 90.0, 2));
                    float density = PassPinesPerHectare * cv.mountain * PassPineHeightFactor(cv.height - rowRoad) * slopeFactor * clump
                                    * (1f - Mathf.Clamp01(cv.talus * 1.5f)) * (1f + cv.gully) * (1f - Mathf.Clamp01(cv.shore * 2f))
                                    * (1f + PassPineModerateBoost * TerrainShaper.ModerateSlope(cv.slope));
                    if (u >= density * cellArea) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamPinePick, gx, gz));
                    float scale = 0.75f + 0.55f * (float)rng.NextDouble() + (rng.NextDouble() < 0.12 ? 0.3f : 0f);
                    TryPassPine(ctx, trees, lx, lz, scale, rng, clearance);
                }
            }
        }

        /// <summary>
        /// Near tiles: low shrubs (Bush / Shrub, the juniper of the pass) in drifts (70 m noise) on the moderate slopes of a range, thinning
        /// with height above the road, none on the talus, the gully floors, the road's corridor or its verges. Absolute 5 m grid.
        /// </summary>
        private void MountainShrubs(Context ctx, List<PropPlacement> bushes)
        {
            GenWindow w = ctx.W;
            float clearance = w.Params.ScatterCorridor + PassPineVerge;
            long gx0 = (long)Math.Floor((w.X0 + 2.0) / ShrubGrid), gx1 = (long)Math.Floor((w.X0 + w.Size - 2.0) / ShrubGrid);
            long gz0 = (long)Math.Floor((w.Z0 + 2.0) / ShrubGrid), gz1 = (long)Math.Floor((w.Z0 + w.Size - 2.0) / ShrubGrid);
            double cellArea = ShrubGrid * ShrubGrid / 10000.0;
            for (long gz = gz0; gz <= gz1; gz++)
            {
                double rowS = double.NaN;
                float rowRoad = 0f;
                for (long gx = gx0; gx <= gx1; gx++)
                {
                    double u = EndlessNoise.Unit(p.seed, StreamShrub, gx, gz);
                    if (u >= PassShrubsPerHectare * cellArea) continue;
                    double x = (gx + 0.1 + 0.8 * EndlessNoise.Unit(p.seed, StreamShrub, gx, gz + 7919)) * ShrubGrid;
                    double z = (gz + 0.1 + 0.8 * EndlessNoise.Unit(p.seed, StreamShrub, gx + 7919, gz)) * ShrubGrid;
                    float lx = w.LocalX(x), lz = w.LocalZ(z);
                    if (lx < 2f || lz < 2f || lx > w.Size - 2f || lz > w.Size - 2f) continue;
                    CoverSample cv = w.Cover.Sample(lx, lz);
                    if (cv.mountain <= 0.3f || cv.isWater || cv.talus > 0.3f || cv.gully > 0.3f) continue;
                    if (double.IsNaN(rowS))
                    {
                        rowS = road.S(gz * ShrubGrid);
                        rowRoad = profile.BaseHeight(rowS);
                    }

                    float drift = (float)EndlessNoise.Smooth(0.0, 0.7, EndlessNoise.FbmN(shrubDrift, x, z, 1.0 / 70.0, 2));
                    float density = PassShrubsPerHectare * cv.mountain * TerrainShaper.ModerateSlope(cv.slope) * drift * PassShrubHeightFactor(cv.height - rowRoad);
                    if (u >= density * cellArea) continue;
                    var rng = new System.Random(EndlessNoise.Seed(p.seed, StreamShrubPick, gx, gz));
                    string role = rng.NextDouble() < 0.6 ? PropRole.Shrub : PropRole.Bush;
                    TryPassShrub(ctx, bushes, role, lx, lz, 0.7f + 0.5f * (float)rng.NextDouble(), rng, clearance);
                }
            }
        }

        /// <summary>A low shrub on a pass slope: the pass's own height ceiling (<see cref="PassPineCeiling"/>), not the scatter's tree line.</summary>
        private void TryPassShrub(Context ctx, List<PropPlacement> bushes, string role, float lx, float lz, float scale, System.Random rng, float clearance)
        {
            const float Footprint = 1.2f;
            int variant = keys.PickVariant(role, rng);
            float yaw = (float)(rng.NextDouble() * 360.0);
            if (!keys.Has(role) || !Fits(ctx, lx, lz, Footprint, clearance, PassPineMaxSlope)) return;
            float hn = HeightmapGenerator.SampleHeight(ctx.W.Map, ctx.W.Params, lx, lz) / Mathf.Max(1e-3f, p.heightRange);
            if (hn > PassPineCeiling) return;
            ctx.Occupancy.Add(new Vector3(lx, 0f, lz), Footprint);
            if (!InTile(ctx, lx, lz)) return;
            float y = ScatterPlanner.GroundHeight(ctx.W.Params, ctx.W.Map, lx, lz, Footprint * 0.5f);
            bushes.Add(new PropPlacement { role = role, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale, settlementId = -1 });
        }

        /// <summary>
        /// <see cref="TryTree"/> for a pass pine: the pass's own height ceiling (<see cref="PassPineCeiling"/>) instead of the scatter's,
        /// a 2.5 m footprint so they can stand in clumps.
        /// </summary>
        private bool TryPassPine(Context ctx, List<TreePlacement> trees, float lx, float lz, float scale, System.Random rng, float clearance)
        {
            const float Footprint = 2.5f;
            int variant = keys.PickVariant(PropRole.PineTree, rng);
            float yaw = (float)(rng.NextDouble() * 360.0);
            if (!keys.Has(PropRole.PineTree) || !Fits(ctx, lx, lz, Footprint, clearance, PassPineMaxSlope)) return false;
            float hn = HeightmapGenerator.SampleHeight(ctx.W.Map, ctx.W.Params, lx, lz) / Mathf.Max(1e-3f, p.heightRange);
            if (hn > PassPineCeiling) return false;
            ctx.Occupancy.Add(new Vector3(lx, 0f, lz), Footprint);
            ctx.Trunks?.Add(new Vector2(lx, lz));
            if (!InTile(ctx, lx, lz)) return true;
            float y = ScatterPlanner.GroundHeight(ctx.W.Params, ctx.W.Map, lx, lz, Footprint * 0.3f);
            trees.Add(new TreePlacement { role = PropRole.PineTree, variant = variant, position = new Vector3(lx, y, lz), yaw = yaw, scale = scale });
            return true;
        }
    }
}
