using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>Trees per 100 m² around a scene position (see <c>TreeDensityGrid</c>).</summary>
    public interface ITreeDensity
    {
        /// <summary>Bilinearly interpolated tree density in trees per 100 m² at scene position (x, z).</summary>
        float Sample(float x, float z);
    }

    /// <summary>
    /// "The ground" as the shared atmosphere and ambience systems see it (mist, ambient effects, fog base altitude, tree
    /// density), independent of how the world is made: the finite world answers from its one baked terrain
    /// (<see cref="GameWorld"/>, <see cref="TerrainSurface"/>), the endless road from its streamed tiles and generator.
    /// All positions are scene positions.
    /// </summary>
    public interface IWorldSurface
    {
        /// <summary>True when there is ground to ask about (false: no terrain in the scene).</summary>
        bool HasGround { get; }

        /// <summary>True when the answers change as the traveller moves (the endless road); false for a fixed world.</summary>
        bool IsDynamic { get; }

        /// <summary>Ground height in metres at a scene position (only the x and z of <paramref name="scenePosition"/> are used).</summary>
        float GroundHeight(Vector3 scenePosition);

        /// <summary>Lake level in metres that applies around a scene position, or a negative value when there is no lake there.</summary>
        float WaterHeight(Vector3 scenePosition);

        /// <summary>A tree-density sampler with cells of about <paramref name="cellSize"/> metres.</summary>
        ITreeDensity CreateTreeDensity(float cellSize);

        /// <summary>
        /// The altitude the height fog settles on near <paramref name="scenePosition"/>, before the atmosphere's own offset:
        /// the lake level where there are lakes, else a low percentile of the ground. False when unknown (no ground yet).
        /// </summary>
        bool TryGetFogBase(Vector3 scenePosition, out float altitude);
    }

    /// <summary>
    /// <see cref="IWorldSurface"/> over one Unity terrain (the finite world's, or one assigned in an inspector), with the
    /// lake level of <see cref="GameWorld.Current"/>. Answers exactly what the atmosphere systems computed from the terrain
    /// before the surface interface existed.
    /// </summary>
    public sealed class TerrainSurface : IWorldSurface
    {
        private readonly Terrain terrain;
        private readonly System.Func<float> water;

        /// <summary>A surface over <paramref name="terrain"/>; <paramref name="waterLevel"/> gives the lake level (null: GameWorld.Current's).</summary>
        public TerrainSurface(Terrain terrain, System.Func<float> waterLevel = null)
        {
            this.terrain = terrain;
            water = waterLevel ?? (() => GameWorld.Current != null ? GameWorld.Current.WaterHeight : -1f);
        }

        /// <inheritdoc />
        public bool HasGround => terrain != null && terrain.terrainData != null;

        /// <inheritdoc />
        public bool IsDynamic => false;

        /// <inheritdoc />
        public float GroundHeight(Vector3 scenePosition) => Height(terrain, scenePosition);

        /// <inheritdoc />
        public float WaterHeight(Vector3 scenePosition) => water();

        /// <inheritdoc />
        public ITreeDensity CreateTreeDensity(float cellSize) => MedievalZen.Environment.TreeDensityGrid.FromTerrain(terrain, cellSize);

        /// <inheritdoc />
        public bool TryGetFogBase(Vector3 scenePosition, out float altitude) => FogBase(terrain, water(), out altitude);

        /// <summary>Terrain height at a scene position (SampleHeight plus the terrain's own height offset).</summary>
        public static float Height(Terrain terrain, Vector3 scenePosition) => terrain.SampleHeight(scenePosition) + terrain.GetPosition().y;

        /// <summary>
        /// The finite world's fog base: the lake level when there are lakes, else the 10th percentile of a 16 × 16 sample of
        /// the whole terrain (the valley floor). False without lakes and terrain.
        /// </summary>
        public static bool FogBase(Terrain terrain, float waterHeight, out float altitude)
        {
            altitude = 0f;
            if (waterHeight > 0f)
            {
                altitude = waterHeight;
                return true;
            }

            if (terrain == null || terrain.terrainData == null) return false;
            const int n = 16;
            var heights = new float[n * n];
            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.GetPosition();
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector3(origin.x + (x + 0.5f) / n * size.x, 0f, origin.z + (z + 0.5f) / n * size.z);
                    heights[z * n + x] = terrain.SampleHeight(p) + origin.y;
                }
            }

            System.Array.Sort(heights);
            altitude = heights[heights.Length / 10];
            return true;
        }
    }

    /// <summary>
    /// Implemented by components that keep world positions of their own (pooled effects, anchors, wind offsets) and must
    /// move them when the endless road shifts its floating origin. The delta is added to every scene position.
    /// </summary>
    public interface IOriginShiftable
    {
        /// <summary>Every scene position just moved by <paramref name="delta"/> (horizontal, whole tiles).</summary>
        void ShiftOrigin(Vector3 delta);
    }
}
