using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Runs the pure generation stages in order and returns a <see cref="WorldBlueprint"/>. Deterministic from
    /// the seed; touches no Unity objects, so it can run in tests (and, in principle, off the main thread).
    /// </summary>
    public static class WorldGenerator
    {
        /// <summary>Generates from a settings asset.</summary>
        public static WorldBlueprint Generate(WorldSettings settings, PaletteKeys keys, Action<string, float> progress = null)
        {
            return Generate(WorldGenParams.From(settings), keys, progress);
        }

        /// <summary>
        /// Generates a world. <paramref name="progress"/> (optional) receives the upcoming stage name and overall
        /// progress 0..1 before each stage. Per-stage timings are in <see cref="WorldBlueprint.StageTimings"/>.
        /// </summary>
        public static WorldBlueprint Generate(WorldGenParams p, PaletteKeys keys, Action<string, float> progress = null)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            keys ??= PaletteKeys.Default;
            var bp = new WorldBlueprint { Params = p };
            var clock = new Stopwatch();
            const int StageCount = 12;
            int stageIndex = 0;

            void Begin(string stage)
            {
                progress?.Invoke(stage, (float)stageIndex / StageCount);
                clock.Restart();
            }

            void End(string stage)
            {
                bp.StageTimings.Add((stage, clock.Elapsed.TotalMilliseconds));
                stageIndex++;
            }

            Begin("Heightmap");
            Relief relief = HeightmapGenerator.GenerateRelief(p);
            float[,] map = relief.Map;
            bp.Relief = relief;
            bp.Heightmap = map;
            bp.WaterHeight = relief.HasWater ? relief.WaterHeight : -1f;
            End("Heightmap");

            Begin("Settlements");
            var names = new SettlementNames(p.seed + 7);
            List<SettlementSite> sites = SettlementPlacer.Place(p, relief, names);
            bp.Landmarks = LandmarkFinder.Find(relief, names);
            End("Settlements");

            Begin("Road routing");
            RoadPlan plan = RoadPlanner.Plan(p, relief, sites, names);
            bp.RoadPlan = plan;
            bp.Settlements = plan.Settlements;
            End("Road routing");

            Begin("Terrain shaping");
            TerrainShaper.FlattenSettlements(map, p, plan.Settlements);
            List<Vector3[]> profiles = RoadPlanner.ComputeProfiles(plan, p, map, out float[] nodeHeights);
            TerrainShaper.CarveRoads(map, p, profiles);
            bp.Roads = RoadPlanner.BuildNetwork(plan, profiles, nodeHeights);
            End("Terrain shaping");

            Begin("Road distance field");
            int res = p.AlphamapResolution;
            float maxDistance = Mathf.Max(30f, p.ScatterCorridor + 12f);
            bp.RoadDistance = RoadDistanceField.Build(TerrainShaper.CenterLines(bp.Roads), res, p.worldSize, maxDistance);
            End("Road distance field");

            Begin("Land cover");
            bp.Cover = LandCover.Build(p, relief, plan.Settlements, res);
            End("Land cover");

            var occupancy = new OccupancyMap(p.worldSize);
            Begin("Settlement dressing");
            SettlementDresser.Dress(p, map, bp.Roads, bp.RoadDistance, keys, occupancy, bp.Props);
            bp.Signposts = SignpostPlanner.Plan(p, map, bp.Roads, keys, occupancy);
            End("Settlement dressing");

            Begin("Fields");
            bp.Fields = ScatterPlanner.PlanFields(p, map, bp.RoadDistance, bp.Cover, plan.Settlements, occupancy, keys, bp.Props);
            End("Fields");

            Begin("Splat");
            bp.Splat = TerrainShaper.ComputeSplat(p, bp.RoadDistance, bp.Cover, plan.Settlements, bp.Fields);
            End("Splat");

            Begin("Features");
            ScatterPlanner.PlaceFeatures(p, map, bp.Roads, bp.RoadDistance, bp.Cover, plan.Settlements, bp.Fields, bp.Landmarks, occupancy, keys, bp.Props);
            bp.Horizon = HorizonPlanner.Plan(p, relief, keys);
            End("Features");

            Begin("Scatter");
            ScatterPlanner.Scatter(p, map, bp.RoadDistance, bp.Cover, plan.Settlements, bp.Fields, occupancy, keys, ScatterRules.Default(), bp.Trees, bp.Props);
            End("Scatter");

            Begin("Details");
            bp.DetailResolution = res;
            bp.DetailLayers = DetailPlanner.Plan(p, bp.RoadDistance, bp.Cover, bp.Splat, plan.Settlements, bp.Fields, keys, DetailPlanner.DefaultRules());
            End("Details");

            progress?.Invoke("Done", 1f);
            return bp;
        }
    }
}
