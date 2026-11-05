using System;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// A walled city's paving rules on a near tile (2026-10-09, the user's first walk of Durncaster): the castle rock's own ground and
    /// no meadow on paving. Pure (no Unity calls), endless only: <see cref="TileGenerator"/> calls it only on tiles a city reaches.
    /// <para>The castle rock (<see cref="CityHill.Rock"/>: 1 on its level top, falling to 0 over <see cref="CityHill.CliffRun"/> m)
    /// is painted with cobble on its level top (the court, the castle's footprint and the ledge round it) and with the shared rock
    /// layer (<see cref="TerrainShaper.RockLayer"/>, already in every tile's first pass: no new terrain pass) wherever its cliff is
    /// steep. The hill passed in is the terrain's own (<see cref="SettlementDisc.Hill"/>, absolute frame), so the paint follows the
    /// ground exactly.</para>
    /// </summary>
    public static class CityPaving
    {
        /// <summary>Cobble weight from which a detail cell grows nothing (soft street edges included).</summary>
        public const float BareCobble = 0.3f;
        /// <summary>Rock weight from which the castle rock is its level top (cobbled at full weight).</summary>
        public const float TopWeight = 0.97f;
        /// <summary>Rock weight from which the ledge round the top starts to be cobbled (where it is not steep).</summary>
        public const float LedgeWeight = 0.85f;
        /// <summary>Rock weight above which no detail grows on the castle rock (the top and the upper cliff).</summary>
        public const float BareRock = 0.3f;
        /// <summary>Ground slope (rise over run) from which the cliff starts to show rock, and the run of the blend to full rock.</summary>
        public const float CliffSlope = 0.5f, CliffBlend = 0.4f;

        /// <summary>
        /// Paints the castle rocks of <paramref name="hills"/> (absolute frame) into <paramref name="splat"/> (texel (0, 0) at window
        /// position (<paramref name="offset"/>, <paramref name="offset"/>)): cobble on the level top, rock on the steep cliff, judged on
        /// the window's own heights (<see cref="GenWindow.Map"/> × <paramref name="heightRange"/>).
        /// </summary>
        public static void PaintCastleRock(float[,,] splat, GenWindow w, float offset, List<CityHill> hills, float heightRange)
        {
            if (hills.Count == 0) return;
            int res = splat.GetLength(0);
            int layers = splat.GetLength(2);
            if (TerrainShaper.CobbleLayer >= layers) return;
            float texel = (w.Size - 2f * offset) / res;
            foreach (CityHill hill in hills)
            {
                if (hill == null || hill.Castle == null) continue;
                float reach = hill.RockReach;
                int x0 = Math.Max(0, (int)Math.Floor((w.LocalX(hill.CX - reach) - offset) / texel));
                int x1 = Math.Min(res - 1, (int)Math.Ceiling((w.LocalX(hill.CX + reach) - offset) / texel));
                int z0 = Math.Max(0, (int)Math.Floor((w.LocalZ(hill.CZ - reach) - offset) / texel));
                int z1 = Math.Min(res - 1, (int)Math.Ceiling((w.LocalZ(hill.CZ + reach) - offset) / texel));
                for (int tz = z0; tz <= z1; tz++)
                {
                    float lz = offset + (tz + 0.5f) * texel;
                    double pz = w.Z0 + lz;
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        float lx = offset + (tx + 0.5f) * texel;
                        double px = w.X0 + lx;
                        float r = hill.Rock(px, pz);
                        if (r <= 0f) continue;
                        float cobble = Clamp01((r - LedgeWeight) / (TopWeight - LedgeWeight));
                        float rock = 0f;
                        if (r < TopWeight)
                        {
                            float slope = Slope(w, lx, lz, heightRange);
                            rock = Clamp01((slope - CliffSlope) / CliffBlend);
                        }

                        if (cobble > 0f) Blend(splat, tz, tx, layers, TerrainShaper.CobbleLayer, cobble);
                        if (rock > 0f) Blend(splat, tz, tx, layers, TerrainShaper.RockLayer, rock);
                    }
                }
            }
        }

        /// <summary>
        /// Zeroes the planned detail density of every cell whose cobble weight in <paramref name="coverSplat"/> (the splat resampled to
        /// the detail grid, cell (x, z) at window position (x + <paramref name="offset"/> + 0.5) × <paramref name="texel"/>) is at least
        /// <see cref="BareCobble"/>, and of every cell on a castle rock of <paramref name="hills"/> with weight above <see cref="BareRock"/>.
        /// </summary>
        public static void KeepDetailsOffPaving(List<DetailLayer> layers, float[,,] coverSplat, GenWindow w, List<CityHill> hills, int offset, int res, float texel)
        {
            if (layers.Count == 0 || coverSplat.GetLength(2) <= TerrainShaper.CobbleLayer) return;
            int n = Math.Min(res, coverSplat.GetLength(0));
            for (int z = 0; z < n; z++)
            {
                double wz = w.Z0 + (z + offset + 0.5) * texel;
                for (int x = 0; x < n; x++)
                {
                    bool bare = coverSplat[z, x, TerrainShaper.CobbleLayer] >= BareCobble;
                    if (!bare && hills.Count > 0)
                    {
                        double wx = w.X0 + (x + offset + 0.5) * texel;
                        foreach (CityHill hill in hills)
                        {
                            if (hill != null && hill.Rock(wx, wz) > BareRock) { bare = true; break; }
                        }
                    }

                    if (!bare) continue;
                    int i = z * res + x;
                    foreach (DetailLayer layer in layers) layer.density[i] = 0;
                }
            }
        }

        /// <summary>Ground slope (rise over run) at window-local (x, z), from the window's heights.</summary>
        public static float Slope(GenWindow w, float x, float z, float heightRange)
        {
            float s = w.Spacing;
            float hx = w.SampleGrid(w.Map, x + s, z) - w.SampleGrid(w.Map, x - s, z);
            float hz = w.SampleGrid(w.Map, x, z + s) - w.SampleGrid(w.Map, x, z - s);
            float k = heightRange / (2f * s);
            hx *= k;
            hz *= k;
            return (float)Math.Sqrt(hx * hx + hz * hz);
        }

        private static void Blend(float[,,] splat, int tz, int tx, int layers, int target, float a)
        {
            for (int l = 0; l < layers; l++)
            {
                float t = l == target ? 1f : 0f;
                splat[tz, tx, l] += (t - splat[tz, tx, l]) * a;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
