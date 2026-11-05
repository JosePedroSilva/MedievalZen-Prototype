using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// One Fantasy Kingdom building preset as measured offline (2026-10-05, from the FBX behind each prefab; Unity axes, x = -FBX x,
    /// metres, the prefab's pivot space at scale 1). The presets stand on a stone base: the ground is meant at the pivot (y = 0, where
    /// the base has a ledge and the door steps start), the floor is 1.2-1.3 m up, and the base reaches <see cref="BaseDepth"/> below
    /// the pivot for sloping ground. Pure data (no Unity objects).
    /// </summary>
    public sealed class BuildingShape
    {
        /// <summary>Prefab name (Synty), e.g. <c>SM_Bld_Preset_Tavern_01_Optimized</c>.</summary>
        public string Name;

        /// <summary>Walls of the ground floor (1.5-2.4 m up, stairs left out): x0, x1, z0, z1.</summary>
        public float BodyX0, BodyX1, BodyZ0, BodyZ1;

        /// <summary>Everything (roof overhangs, steps, balconies, stilts): x0, x1, z0, z1.</summary>
        public float ExtentX0, ExtentX1, ExtentZ0, ExtentZ1;

        /// <summary>Top of the roof (or chimney) above the pivot, and the eaves (10th percentile of the roof faces' lowest corner).</summary>
        public float Height, Eaves;

        /// <summary>How far the stone base reaches below the pivot (negative: the building stands on its own feet above it).</summary>
        public float BaseDepth;

        /// <summary>Which way the front (the door side) faces in the prefab: 0 = +Z, 90 = +X (degrees about +Y).</summary>
        public float FrontYaw;

        /// <summary>The foot of the door steps (or the doorway) on the ground, local.</summary>
        public Vector3 Door;

        /// <summary>Where smoke rises: the top of a measured stone chimney, else a vent near the ridge over the middle of the body.</summary>
        public Vector3 Smoke;

        /// <summary>True when <see cref="Smoke"/> is a measured chimney top.</summary>
        public bool Chimney;

        /// <summary>A spot where someone works or waits (the smith at the forge, the stable door), local; zero when none.</summary>
        public Vector3 Work;

        /// <summary>
        /// The closed part people cannot walk through, when it is smaller than the body (the smithy's workshop is open on two sides): x0, x1,
        /// z0, z1; all zero means the whole body.
        /// </summary>
        public float SolidX0, SolidX1, SolidZ0, SolidZ1;

        /// <summary>The walls people walk round: <see cref="SolidX0"/>.. when set, else the body (x0, x1, z0, z1).</summary>
        public (float x0, float x1, float z0, float z1) Walls =>
            SolidX1 > SolidX0 ? (SolidX0, SolidX1, SolidZ0, SolidZ1) : (BodyX0, BodyX1, BodyZ0, BodyZ1);

        /// <summary>Triangles of the prefab's meshes (glass and collision left out).</summary>
        public int Triangles;

        /// <summary>Mean wall and roof colours, linear (Fantasy Kingdom atlas of the prefab's material at the triangles' UVs, area-weighted).</summary>
        public Vector3 Wall, Roof;

        /// <summary>Unit direction out of the front, local.</summary>
        public Vector3 Outward => new Vector3(Mathf.Sin(FrontYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(FrontYaw * Mathf.Deg2Rad));

        /// <summary>Middle of the body in plan, local.</summary>
        public Vector3 BodyCentre => new Vector3((BodyX0 + BodyX1) * 0.5f, 0f, (BodyZ0 + BodyZ1) * 0.5f);

        /// <summary>Middle of the extent in plan, local.</summary>
        public Vector3 ExtentCentre => new Vector3((ExtentX0 + ExtentX1) * 0.5f, 0f, (ExtentZ0 + ExtentZ1) * 0.5f);

        /// <summary>Farthest the extent reaches out of the front (along <see cref="Outward"/>), local.</summary>
        public float FrontReach => FrontYaw > 45f ? ExtentX1 : ExtentZ1;

        /// <summary>Local coordinate of the front wall of the body (the face a sign or a hanging pot is fixed to; a porch stands in front of it).</summary>
        public float FrontWall => FrontYaw > 45f ? BodyX1 : BodyZ1;

        /// <summary>Width of the extent across the front, and its depth front to back.</summary>
        public float Width => FrontYaw > 45f ? ExtentZ1 - ExtentZ0 : ExtentX1 - ExtentX0;

        /// <summary>Depth of the extent, front to back.</summary>
        public float Depth => FrontYaw > 45f ? ExtentX1 - ExtentX0 : ExtentZ1 - ExtentZ0;

        /// <summary>The window light: middle of the front wall, 2.2 m up (on the floor 1.2 m above the ground), 0.5 m out.</summary>
        public Vector3 WindowLight
        {
            get
            {
                Vector3 c = BodyCentre;
                return FrontYaw > 45f ? new Vector3(BodyX1 + 0.5f, 2.2f, c.z) : new Vector3(c.x, 2.2f, BodyZ1 + 0.5f);
            }
        }

        /// <summary>The extent's four corners and edge midpoints in plan, local.</summary>
        public Vector3[] ExtentOutline()
        {
            float mx = (ExtentX0 + ExtentX1) * 0.5f, mz = (ExtentZ0 + ExtentZ1) * 0.5f;
            return new[]
            {
                new Vector3(ExtentX0, 0f, ExtentZ0), new Vector3(mx, 0f, ExtentZ0), new Vector3(ExtentX1, 0f, ExtentZ0), new Vector3(ExtentX1, 0f, mz),
                new Vector3(ExtentX1, 0f, ExtentZ1), new Vector3(mx, 0f, ExtentZ1), new Vector3(ExtentX0, 0f, ExtentZ1), new Vector3(ExtentX0, 0f, mz),
            };
        }
    }

    /// <summary>
    /// The measured building presets of the endless road's inns and villages (see <see cref="BuildingShape"/>; measured with the
    /// session's FBX tools: door steps found as the treads rising from y = 0 to the floor, chimneys as grey stone stacks standing
    /// 0.7 m or more above the roof round them). Placements name a family and a variant; the variant is resolved to a prefab in
    /// <see cref="SyntyCatalog"/> order, as the palette builds it.
    /// </summary>
    public static class BuildingTable
    {
        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        private static BuildingShape B(string name, float bx0, float bx1, float bz0, float bz1, float ex0, float ex1, float ez0, float ez1, float height, float eaves,
            float baseDepth, Vector3 door, Vector3 smoke, bool chimney, int tris, Vector3 wall, Vector3 roof, float frontYaw = 0f, Vector3 work = default,
            float sx0 = 0f, float sx1 = 0f, float sz0 = 0f, float sz1 = 0f)
        {
            return new BuildingShape
            {
                SolidX0 = sx0, SolidX1 = sx1, SolidZ0 = sz0, SolidZ1 = sz1,
                Name = "SM_Bld_Preset_" + name + "_Optimized", BodyX0 = bx0, BodyX1 = bx1, BodyZ0 = bz0, BodyZ1 = bz1, ExtentX0 = ex0, ExtentX1 = ex1,
                ExtentZ0 = ez0, ExtentZ1 = ez1, Height = height, Eaves = eaves, BaseDepth = baseDepth, Door = door, Smoke = smoke, Chimney = chimney,
                Triangles = tris, Wall = wall, Roof = roof, FrontYaw = frontYaw, Work = work,
            };
        }

        private static readonly BuildingShape[] Rows =
        {
            // The inn: body 10.2 x 7.8 m, a balcony on posts and an outside stair on the +Z side (the front), whose foot is the door.
            B("Tavern_01", -7.60f, 2.62f, -10.13f, -2.37f, -9.65f, 4.65f, -10.61f, 3.77f, 10.48f, 3.76f, 2.5f, V(-0.6f, 0f, 2.6f), V(-2.49f, 9.9f, -6.25f), false, 34559,
                V(0.1098f, 0.0917f, 0.0814f), V(0.1903f, 0.0816f, 0.0340f), 0f, V(-3.2f, 0f, 0.6f)),
            B("House_01_A", -5.10f, 3.06f, -5.15f, 0.15f, -6.97f, 3.11f, -7.32f, 2.34f, 10.98f, 4.19f, 2.5f, V(-1.6f, 0f, 1.0f), V(-1.02f, 10.4f, -2.5f), false, 25887,
                V(0.1167f, 0.1036f, 0.0866f), V(0.0611f, 0.0540f, 0.0468f)),
            B("House_02_A", -5.12f, 0.12f, -5.59f, 0.15f, -5.72f, 0.72f, -5.61f, 2.46f, 6.73f, 3.57f, 2.5f, V(-3.4f, 0f, 0.6f), V(-2.5f, 6.3f, -2.7f), false, 9815,
                V(0.2474f, 0.2093f, 0.1640f), V(0.1630f, 0.1002f, 0.0629f)),
            B("House_03", -0.56f, 5.12f, -5.10f, 1.85f, -0.89f, 5.89f, -5.71f, 1.95f, 7.99f, 4.01f, 2.5f, V(0.9f, 0f, 2.3f), V(4.4f, 8.05f, -4.5f), true, 11158,
                V(0.1694f, 0.1531f, 0.1346f), V(0.3119f, 0.2033f, 0.1044f)),
            B("House_05", -1.11f, 8.87f, -8.56f, 1.32f, -1.79f, 9.47f, -8.61f, 2.46f, 11.87f, 3.58f, 2.5f, V(1.7f, 0f, 1.8f), V(8.5f, 8.05f, -3.4f), true, 23482,
                V(0.1040f, 0.0874f, 0.0779f), V(0.0818f, 0.0805f, 0.0367f)),
            B("House_07", -5.12f, 5.12f, -9.31f, 0.81f, -5.72f, 5.53f, -9.36f, 1.09f, 8.40f, 3.54f, 2.5f, V(1.25f, 0f, 1.3f), V(-4.0f, 8.55f, -4.1f), true, 18937,
                V(0.1590f, 0.1186f, 0.1144f), V(0.0377f, 0.0573f, 0.0791f)),
            B("House_08", -3.87f, 0.12f, -4.58f, 0.13f, -4.47f, 1.49f, -4.65f, 1.64f, 10.48f, 3.56f, 2.5f, V(-2.5f, 0f, 1.9f), V(-1.9f, 10.0f, -2.2f), false, 12543,
                V(0.1113f, 0.0914f, 0.0816f), V(0.0821f, 0.0782f, 0.0386f)),
            B("House_09_A", -5.12f, 2.62f, -6.40f, 0.13f, -5.53f, 3.03f, -6.97f, 1.64f, 8.40f, 3.54f, 2.5f, V(-1.25f, 0f, 1.9f), V(-1.3f, 8.55f, -5.2f), true, 17945,
                V(0.1672f, 0.1515f, 0.1369f), V(0.2226f, 0.0888f, 0.0326f)),
            B("House_09_B", -5.12f, 2.62f, -6.40f, 0.13f, -5.48f, 2.99f, -7.14f, 1.64f, 8.40f, 3.38f, 2.5f, V(-1.25f, 0f, 1.9f), V(1.3f, 8.55f, -5.4f), true, 16958,
                V(0.1747f, 0.1578f, 0.1385f), V(0.3118f, 0.2022f, 0.1030f)),
            B("House_09_C", -5.12f, 3.89f, -6.40f, 0.13f, -5.53f, 4.52f, -6.97f, 1.64f, 9.62f, 3.57f, 2.5f, V(-1.25f, 0f, 1.9f), V(-1.3f, 8.55f, -5.2f), true, 19950,
                V(0.2332f, 0.2014f, 0.1575f), V(0.1530f, 0.0941f, 0.0592f)),
            // Hut_01's door is in the +X gable, on a deck 1.2 m up.
            B("Hut_01", -6.37f, -1.12f, -6.37f, 0.07f, -6.76f, 0.11f, -7.14f, 0.11f, 6.73f, 3.47f, 2.5f, V(0.6f, 0f, -3.2f), V(-3.7f, 6.3f, -3.2f), false, 8562,
                V(0.1253f, 0.1020f, 0.0791f), V(0.3026f, 0.1959f, 0.0996f), 90f),
            B("Hut_02", -2.78f, 0.28f, -5.88f, 0.12f, -2.99f, 0.49f, -5.89f, 0.89f, 6.73f, 3.40f, 2.5f, V(-1.25f, 0f, 1.2f), V(-1.25f, 6.3f, -2.9f), false, 5521,
                V(0.1393f, 0.1156f, 0.0920f), V(0.3323f, 0.2147f, 0.1084f)),
            B("Outhouse_01", -2.64f, 0.14f, -2.64f, 0.14f, -3.22f, 0.72f, -3.03f, 0.53f, 4.48f, 2.87f, -0.25f, V(-1.25f, 0f, 0.8f), V(-1.25f, 4.2f, -1.25f), false, 2503,
                V(0.0966f, 0.0737f, 0.0551f), V(0.1147f, 0.0405f, 0.0264f)),
            B("Shelter_01", -7.62f, 0.13f, -5.12f, 0.27f, -7.99f, 0.49f, -5.89f, 0.89f, 7.56f, 3.52f, 2.5f, V(-3.75f, 0f, 1.4f), V(-3.75f, 7.2f, -2.4f), false, 16842,
                V(0.1220f, 0.0983f, 0.0739f), V(0.2515f, 0.1669f, 0.0893f)),
            B("Shelter_02", -7.65f, 0.15f, -5.15f, 0.15f, -7.99f, 0.49f, -5.89f, 0.89f, 7.56f, 3.57f, 1.25f, V(-3.75f, 0f, 1.4f), V(-3.75f, 7.2f, -2.4f), false, 5110,
                V(0.1328f, 0.0924f, 0.0560f), V(0.2524f, 0.1683f, 0.0909f)),
            // The smithy: an open-sided workshop (forge hearth near (-1.6, -1.3)) beside a closed room whose steps face +Z.
            B("Blacksmith_01", -5.12f, 6.40f, -2.62f, 5.00f, -5.72f, 6.97f, -3.49f, 6.85f, 8.47f, 3.57f, 2.5f, V(5.0f, 0f, 2.3f), V(-3.0f, 8.6f, -2.2f), true, 19318,
                V(0.1506f, 0.1308f, 0.1126f), V(0.1850f, 0.0860f, 0.0397f), 0f, V(-1.6f, 0f, 1.2f), 2.9f, 6.4f, -2.62f, 0.6f),
            // Stables: the groom's house on the left (steps at +Z), stalls open to +Z on the right.
            B("Stables_01", -5.15f, 7.62f, -5.56f, 2.65f, -6.09f, 8.03f, -5.72f, 3.03f, 9.23f, 3.76f, 2.5f, V(-3.5f, 0f, 3.3f), V(-2.6f, 8.7f, -2.5f), false, 29446,
                V(0.2521f, 0.2110f, 0.1623f), V(0.1457f, 0.0929f, 0.0594f), 0f, V(3.8f, 0f, 3.6f)),
            // Arch house: a lane runs through it along x (opening z -7.15..-2.85, 4.3 m wide, 3.3 m high: too narrow for the road).
            B("House_Archway_02", -5.22f, 0.22f, -10.95f, 0.22f, -6.84f, 1.84f, -10.95f, 0.90f, 9.87f, 3.64f, 1.25f, V(-5.8f, 0f, -10.0f), V(-2.5f, 9.0f, -5.4f), false, 22144,
                V(0.1509f, 0.1419f, 0.1288f), V(0.0715f, 0.1261f, 0.0652f), 90f, V(2.6f, 0f, -5.0f)),
        };

        /// <summary>
        /// The Meadow stone cabin (6.0 x 8.1 m, 4.0 m tall, centred on its pivot, standing on the ground, door on +Z; 10,310 triangles): the
        /// stand-in house when a palette has no Fantasy Kingdom houses.
        /// </summary>
        public static readonly BuildingShape StoneCabin = new BuildingShape
        {
            Name = "SM_Bld_Stone_Cabin_01", BodyX0 = -3f, BodyX1 = 3f, BodyZ0 = -4.06f, BodyZ1 = 4.06f, ExtentX0 = -3f, ExtentX1 = 3f, ExtentZ0 = -4.06f, ExtentZ1 = 4.06f,
            Height = 4.04f, Eaves = 1.99f, BaseDepth = 0f, Door = new Vector3(0f, 0f, 4.6f), Smoke = new Vector3(0f, 4.2f, 0f), Triangles = 10310,
            Wall = new Vector3(0.1908f, 0.1640f, 0.1386f), Roof = new Vector3(0.1899f, 0.1464f, 0.1073f),
        };

        /// <summary>
        /// The village church (the landmark chapel's Fantasy Kingdom <c>Church_01_A/B</c>, family <see cref="PropRole.LandmarkChapel"/>): nave
        /// 6.44 x 10.92 m centred on the pivot with the bell tower inside that outline, eaves 3.55 m, spire 9.2 / 11.7 m, foundation 2.51 m
        /// below the pivot, front (door) on +Z as the landmark plan stands its people. Deliberately not in <see cref="All"/> or the name
        /// lookup: the village composer places it by this shape, and it keeps the landmark's far drawing and has no smoke or window light.
        /// </summary>
        public static readonly BuildingShape Church = new BuildingShape
        {
            Name = "SM_Bld_Preset_Church_01_A_Optimized", BodyX0 = -3.22f, BodyX1 = 3.22f, BodyZ0 = -5.46f, BodyZ1 = 5.46f, ExtentX0 = -3.22f, ExtentX1 = 3.22f,
            ExtentZ0 = -5.46f, ExtentZ1 = 5.46f, Height = 9.23f, Eaves = 3.55f, BaseDepth = 2.51f, Door = new Vector3(0f, 0f, 6.1f), Smoke = new Vector3(0f, 7f, 0f),
            Wall = new Vector3(0.1623f, 0.1515f, 0.1391f), Roof = new Vector3(0.2272f, 0.0900f, 0.0328f),
        };

        /// <summary>Arch house: the lane through it (local z range) and its clear height, from the measured silhouette along x.</summary>
        public const float ArchOpeningZ0 = -7.15f, ArchOpeningZ1 = -2.85f, ArchClearHeight = 3.3f;

        /// <summary>Whether the arch house's opening could carry a road of <paramref name="roadWidth"/> with <paramref name="margin"/> each side.</summary>
        public static bool ArchClearsRoad(float roadWidth, float margin) => ArchOpeningZ1 - ArchOpeningZ0 >= roadWidth + 2f * margin;

        private static Dictionary<string, BuildingShape> byName;
        private static Dictionary<string, string[]> catalog;

        /// <summary>Every measured preset.</summary>
        public static IReadOnlyList<BuildingShape> All => Rows;

        /// <summary>The measured shape of a prefab by name (a name containing the Synty name counts), or null.</summary>
        public static BuildingShape ForPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            Dictionary<string, BuildingShape> map = byName;
            if (map == null)
            {
                map = new Dictionary<string, BuildingShape>(StringComparer.OrdinalIgnoreCase);
                foreach (BuildingShape r in Rows) map[r.Name] = r;
                map[StoneCabin.Name] = StoneCabin;
                byName = map;
            }

            if (map.TryGetValue(prefabName, out BuildingShape exact)) return exact;
            if (prefabName.IndexOf(StoneCabin.Name, StringComparison.OrdinalIgnoreCase) >= 0) return StoneCabin;
            foreach (BuildingShape r in Rows)
            {
                if (prefabName.IndexOf(r.Name, StringComparison.OrdinalIgnoreCase) >= 0) return r;
            }

            return null;
        }

        /// <summary>The shape of a family's variant (catalogue order, wrapped), or null for families that are not measured buildings.</summary>
        public static BuildingShape For(string family, int variant) => ForPrefab(CatalogName(family, variant));

        /// <summary>Number of catalogued variants of a family.</summary>
        public static int CatalogCount(string family) => Catalog().TryGetValue(family ?? string.Empty, out string[] paths) ? paths.Length : 0;

        /// <summary>The prefab name of a family's variant in catalogue order (variant wrapped), or null.</summary>
        public static string CatalogName(string family, int variant)
        {
            if (family == null || !Catalog().TryGetValue(family, out string[] paths) || paths.Length == 0) return null;
            string path = paths[((variant % paths.Length) + paths.Length) % paths.Length];
            int slash = path.LastIndexOf('/');
            string file = slash >= 0 ? path.Substring(slash + 1) : path;
            int dot = file.LastIndexOf('.');
            return dot > 0 ? file.Substring(0, dot) : file;
        }

        private static Dictionary<string, string[]> Catalog()
        {
            Dictionary<string, string[]> map = catalog;
            if (map != null) return map;
            map = new Dictionary<string, string[]>();
            foreach (var f in SyntyCatalog.Families) map[f.family] = f.paths;
            catalog = map;
            return map;
        }

        /// <summary>Local point to world (placement at <paramref name="pivot"/>, yaw in degrees, scale 1), as Unity's yaw rotation.</summary>
        public static Vector3 ToWorld(Vector3 pivot, float yaw, Vector3 local)
        {
            float r = yaw * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new Vector3(pivot.x + local.x * cs + local.z * sn, pivot.y + local.y, pivot.z - local.x * sn + local.z * cs);
        }

        /// <summary>World direction of a local horizontal direction under a yaw (degrees).</summary>
        public static Vector3 Direction(float yaw, Vector3 local) => ToWorld(Vector3.zero, yaw, local);
    }
}
