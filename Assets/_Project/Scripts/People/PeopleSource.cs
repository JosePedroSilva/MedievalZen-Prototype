using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;

namespace MedievalZen.People
{
    /// <summary>
    /// The endless road's places as the people planner sees them (pure; thread-safe like the generator): finds the planned place of a
    /// settlement waypoint, takes its cached dressing (<see cref="DressingCache.Settlement"/>: anchors, building walls) and a chapel
    /// landmark near it, and plans its cast (<see cref="PeoplePlanner"/>). The result is a pure function of the seed and the place.
    /// </summary>
    public static class PeopleSource
    {
        /// <summary>The cast of settlement waypoint <paramref name="w"/>, or null when it is not a planned settlement of <paramref name="g"/>.</summary>
        public static PlaceCast CastFor(EndlessGenerator g, Waypoint w)
        {
            if (g == null || w == null || !w.IsSettlement) return null;
            Place place = FindPlace(g, w.Id, w.S);
            return place == null ? null : CastFor(g, place);
        }

        /// <summary>The cast of a planned settlement.</summary>
        public static PlaceCast CastFor(EndlessGenerator g, Place place)
        {
            PlaceInput input = InputFor(g, place);
            return input == null ? null : PeoplePlanner.Plan(input);
        }

        /// <summary>The planner's view of a planned settlement (anchors, walls, the road, a chapel within reach).</summary>
        public static PlaceInput InputFor(EndlessGenerator g, Place place)
        {
            if (g == null || place == null || !place.IsSettlement) return null;
            Dressing d = g.Dressings.Settlement(place);
            var input = new PlaceInput
            {
                Seed = g.Params.seed,
                PlaceId = place.Id,
                Inn = place.Kind == PlaceKind.Inn,
                Village = place.Kind == PlaceKind.Village,
                CentreX = d.CentreX,
                CentreZ = d.CentreZ,
                X0 = d.X0,
                Z0 = d.Z0,
                HalfRoad = g.Params.roadWidth * 0.5f,
            };
            RoadCurve curve = g.Curve;
            input.RoadDistance = (x, z) =>
            {
                curve.Nearest(x, z, out double dist);
                return dist;
            };
            input.Anchors.AddRange(d.Anchors);
            for (int i = 0; i < d.Props.Count; i++)
            {
                PropPlacement prop = d.Props[i];
                if (!PropRole.IsBuilding(prop.role) && prop.role != PropRole.Cabin && prop.role != PropRole.LandmarkChapel) continue;
                BuildingShape shape = prop.role == PropRole.LandmarkChapel ? BuildingTable.Church : BuildingTable.For(prop.role, prop.variant);
                if (shape == null) continue;
                input.Bodies.Add(new BuildingBody { X = d.X0 + prop.position.x, Z = d.Z0 + prop.position.z, Yaw = prop.yaw, Shape = shape, PropIndex = i });
            }

            // A chapel landmark within reach of the place.
            var landmarks = new List<LandmarkSite>();
            double r = PeoplePlanner.ChapelReach;
            g.Landmarks.Overlapping(d.CentreX - r, d.CentreZ - r, r * 2.0, 0.0, landmarks);
            double best = r * r;
            foreach (LandmarkSite l in landmarks)
            {
                if (l.Type != LandmarkType.Chapel) continue;
                double dx = l.X - d.CentreX, dz = l.Z - d.CentreZ, dd = dx * dx + dz * dz;
                if (dd >= best) continue;
                best = dd;
                input.HasChapel = true;
                input.ChapelX = l.X;
                input.ChapelZ = l.Z;
                input.ChapelYaw = l.Yaw;
                input.ChapelRadius = LandmarkPlan.BaseRadiusOf(l.Type);
            }

            // A church village's own church (VillageComposer places the landmark chapel's preset among the houses).
            if (!input.HasChapel)
            {
                foreach (PropPlacement prop in d.Props)
                {
                    if (prop.role != PropRole.LandmarkChapel) continue;
                    input.HasChapel = true;
                    input.ChapelX = d.X0 + prop.position.x;
                    input.ChapelZ = d.Z0 + prop.position.z;
                    input.ChapelYaw = prop.yaw;
                    input.ChapelRadius = LandmarkPlan.BaseRadiusOf(LandmarkType.Chapel);
                    break;
                }
            }

            return input;
        }

        /// <summary>The planned place with id <paramref name="id"/> near road distance <paramref name="s"/>, or null.</summary>
        public static Place FindPlace(EndlessGenerator g, int id, double s)
        {
            var places = new List<Place>();
            g.Plan.Inns(s - 5.0, s + 5.0, places);
            g.Plan.RegionPlaces(s - 5.0, s + 5.0, places);
            foreach (Place p in places)
            {
                if (p.Id == id && p.IsSettlement) return p;
            }

            return null;
        }
    }
}
