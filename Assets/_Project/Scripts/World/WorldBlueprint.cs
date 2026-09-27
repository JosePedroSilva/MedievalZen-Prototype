using System.Collections.Generic;
using System.Text;
using MedievalZen.Roads;

namespace MedievalZen.World
{
    /// <summary>
    /// Everything the pure generation stages produce; <see cref="WorldBuilder"/> turns it into Unity objects.
    /// Plain data apart from the (immutable) <see cref="RoadNetwork"/>.
    /// </summary>
    public sealed class WorldBlueprint
    {
        /// <summary>Inputs the blueprint was generated from.</summary>
        public WorldGenParams Params;

        /// <summary>Final normalised heightmap, <c>[z, x]</c>, values 0..1 (× hillAmplitude metres).</summary>
        public float[,] Heightmap;

        /// <summary>Heightmap plus water level and highland mask (its <see cref="Relief.Map"/> is <see cref="Heightmap"/>).</summary>
        public Relief Relief;

        /// <summary>Lake level in metres, or a negative value when the world has no lakes.</summary>
        public float WaterHeight = -1f;

        /// <summary>Named natural landmarks: the tallest peak, then lakes (largest first).</summary>
        public List<Landmark> Landmarks = new List<Landmark>();

        /// <summary>Per-texel biome/land-cover fields on the alphamap grid.</summary>
        public LandCover Cover;

        /// <summary>Terrain detail density maps, one per (family, variant); resolution = <see cref="DetailResolution"/>.</summary>
        public List<DetailLayer> DetailLayers = new List<DetailLayer>();

        /// <summary>Detail cells per side (equals the alphamap resolution).</summary>
        public int DetailResolution;

        /// <summary>Background hills/mountains outside the playable area.</summary>
        public List<HorizonPlacement> Horizon = new List<HorizonPlacement>();

        /// <summary>Splat weights <c>[z, x, layer]</c>; layers as in <see cref="TerrainShaper"/>.</summary>
        public float[,,] Splat;

        /// <summary>Distance-to-road field on the alphamap grid.</summary>
        public RoadDistanceField RoadDistance;

        /// <summary>Road plan (grid, nodes, raw segments) the network was built from.</summary>
        public RoadPlan RoadPlan;

        /// <summary>The road network (settlements, nodes, segments with terrain-following heights).</summary>
        public RoadNetwork Roads;

        /// <summary>All settlements including inns; index = <see cref="Settlement.Id"/> in <see cref="Roads"/>.</summary>
        public List<SettlementSite> Settlements = new List<SettlementSite>();

        /// <summary>Crop fields.</summary>
        public List<FieldPatch> Fields = new List<FieldPatch>();

        /// <summary>Terrain tree instances.</summary>
        public List<TreePlacement> Trees = new List<TreePlacement>();

        /// <summary>Prefab instances (settlement dressing and countryside scatter).</summary>
        public List<PropPlacement> Props = new List<PropPlacement>();

        /// <summary>Junction signposts.</summary>
        public List<SignpostRecord> Signposts = new List<SignpostRecord>();

        /// <summary>Stage name and duration in milliseconds, in execution order.</summary>
        public List<(string stage, double milliseconds)> StageTimings = new List<(string stage, double milliseconds)>();

        /// <summary>One-line-per-stage summary for logging.</summary>
        public string Summary()
        {
            var sb = new StringBuilder();
            double total = 0.0;
            foreach (var (stage, ms) in StageTimings) total += ms;
            sb.Append($"World seed {Params?.seed}: {Settlements.Count} settlements, {Roads?.Nodes.Count ?? 0} road nodes, ")
              .Append($"{Roads?.Segments.Count ?? 0} road segments, {Signposts.Count} signposts, {Trees.Count} trees, ")
              .Append($"{Props.Count} props, {Fields.Count} fields, {DetailLayers.Count} detail layers, {Landmarks.Count} landmarks, ")
              .Append(WaterHeight > 0f ? $"water at {WaterHeight:0.0} m. " : "no lakes. ")
              .Append($"Generation {total:0} ms");
            foreach (var (stage, ms) in StageTimings) sb.Append($"\n  {stage}: {ms:0} ms");
            return sb.ToString();
        }
    }
}
