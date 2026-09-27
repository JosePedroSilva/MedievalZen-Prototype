using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// Hand-built road networks for test scenes and unit tests (flat, y = 0). Shared by the
    /// <c>MedievalZen/Player/Create Player Test Scene</c> menu and the edit-mode tests.
    /// </summary>
    public static class TestRoadNetworks
    {
        /// <summary>Name of the town in <see cref="Triangle"/> (the player starts near it).</summary>
        public const string TownName = "Ashford";

        /// <summary>Name of the village in <see cref="Triangle"/>.</summary>
        public const string VillageName = "Brambleby";

        /// <summary>Name of the inn in <see cref="Triangle"/>.</summary>
        public const string InnName = "The Crooked Pike";

        /// <summary>Spacing of the Catmull-Rom resampling, in metres.</summary>
        public const float PointSpacing = 2f;

        /// <summary>
        /// Three settlements roughly 290–300 m apart inside a 600 × 600 m square centred on the origin:
        /// town (settlement 0, south-west), village (1, south-east) and inn (2, north), plus one plain
        /// junction near the middle (node 3). Gently curved roads: town–junction, junction–village,
        /// junction–inn, and a longer winding town–inn road that forms a loop.
        /// Segment ids: 0 town–junction, 1 junction–village, 2 junction–inn, 3 town–inn.
        /// </summary>
        public static RoadNetwork Triangle()
        {
            var town = new Vector3(-150f, 0f, -100f);
            var village = new Vector3(150f, 0f, -110f);
            var inn = new Vector3(0f, 0f, 150f);
            var junction = new Vector3(0f, 0f, -10f);

            var builder = new RoadNetworkBuilder();
            Settlement townSettlement = builder.AddSettlement(TownName, SettlementType.Town, town, 30f);
            Settlement villageSettlement = builder.AddSettlement(VillageName, SettlementType.Village, village, 20f);
            Settlement innSettlement = builder.AddSettlement(InnName, SettlementType.Inn, inn, 10f);
            RoadNode junctionNode = builder.AddNode(junction);

            builder.AddSegment(townSettlement.Node, junctionNode, Curve(
                town, new Vector3(-110f, 0f, -62f), new Vector3(-55f, 0f, -45f), junction));
            builder.AddSegment(junctionNode, villageSettlement.Node, Curve(
                junction, new Vector3(55f, 0f, -30f), new Vector3(105f, 0f, -85f), village));
            builder.AddSegment(junctionNode, innSettlement.Node, Curve(
                junction, new Vector3(-18f, 0f, 45f), new Vector3(14f, 0f, 100f), inn));
            builder.AddSegment(townSettlement.Node, innSettlement.Node, Curve(
                town, new Vector3(-175f, 0f, -20f), new Vector3(-135f, 0f, 70f), new Vector3(-65f, 0f, 128f), inn));

            return builder.Build();
        }

        private static Vector3[] Curve(params Vector3[] controlPoints)
        {
            var polyline = Polyline.CatmullRom(controlPoints, PointSpacing);
            var points = new Vector3[polyline.Count];
            for (int i = 0; i < points.Length; i++) points[i] = polyline.Points[i];
            return points;
        }
    }
}
