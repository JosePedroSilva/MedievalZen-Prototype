using System;
using MedievalZen.Environment;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The endless road's ground for the shared atmosphere and ambience systems (<see cref="IWorldSurface"/>): heights and
    /// lake levels from the streamed tiles' blueprints where a tile is built (exactly what the terrain shows), else from the
    /// generator; tree density from per-tile grids made on the worker threads; the fog base from the land around the
    /// traveller, so the fog follows the road up a pass.
    /// </summary>
    internal sealed class EndlessSurface : IWorldSurface
    {
        private const int FogSamples = 9;
        private const float FogReach = 400f;

        private readonly EndlessWorld world;
        private readonly float[] fogHeights = new float[FogSamples * FogSamples];

        public EndlessSurface(EndlessWorld world)
        {
            this.world = world;
        }

        public bool HasGround => world != null && world.Generator != null;

        public bool IsDynamic => true;

        public float GroundHeight(Vector3 scenePosition)
        {
            (double x, double z) = world.Origin.ToAbsolute(scenePosition);
            return world.GroundHeightAbsolute(x, z);
        }

        public float WaterHeight(Vector3 scenePosition)
        {
            (double x, double z) = world.Origin.ToAbsolute(scenePosition);
            return world.WaterHeightAbsolute(x, z);
        }

        public ITreeDensity CreateTreeDensity(float cellSize) => new Density(world);

        public bool TryGetFogBase(Vector3 scenePosition, out float altitude)
        {
            altitude = 0f;
            if (!HasGround) return false;
            float water = WaterHeight(scenePosition);
            if (water > 0f)
            {
                altitude = water;
                return true;
            }

            // No lake here: the 10th percentile of the land within FogReach stands in for the valley floor.
            for (int j = 0; j < FogSamples; j++)
            {
                for (int i = 0; i < FogSamples; i++)
                {
                    var p = new Vector3(scenePosition.x + (i / (FogSamples - 1f) * 2f - 1f) * FogReach, 0f,
                        scenePosition.z + (j / (FogSamples - 1f) * 2f - 1f) * FogReach);
                    fogHeights[j * FogSamples + i] = GroundHeight(p);
                }
            }

            Array.Sort(fogHeights);
            altitude = fogHeights[fogHeights.Length / 10];
            return true;
        }

        /// <summary>Tree density from the tile under each point (0 where no tile is built yet).</summary>
        private sealed class Density : ITreeDensity
        {
            private readonly EndlessWorld world;

            public Density(EndlessWorld world)
            {
                this.world = world;
            }

            public float Sample(float x, float z)
            {
                (double ax, double az) = world.Origin.ToAbsolute(new Vector3(x, 0f, z));
                float ts = world.Params.tileSize;
                TileCoord c = TileCoord.Containing(ax, az, ts);
                TreeDensityGrid grid = world.DensityAt(c);
                if (grid == null) return 0f;
                return grid.Sample((float)(ax - c.OriginX(ts)), (float)(az - c.OriginZ(ts)));
            }
        }
    }
}
