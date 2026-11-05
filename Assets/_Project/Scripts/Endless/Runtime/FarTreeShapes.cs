using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>The low-poly solid a distant tree is drawn as.</summary>
    public enum FarTreeShape
    {
        /// <summary>A faceted crown (a lathe of five sides through the crown's measured profile) on a short trunk: broadleaf trees.</summary>
        Broadleaf = 0,

        /// <summary>Two stacked cones on a trunk: pines.</summary>
        Conifer = 1,

        /// <summary>A bare spike with one branch: dead trees.</summary>
        Dead = 2,
    }

    /// <summary>
    /// How one tree prefab is drawn far away: shape, size at scale 1 (as measured on the mesh LOD the near renderer draws last) and
    /// colours (linear). The crown is a lathe through four measured points: its base (<see cref="CrownBase"/>), its widest ring
    /// (<see cref="Radius"/> at <see cref="WidestAt"/>), an upper ring (<see cref="TopRadius"/> at 81 % of the crown) and the top.
    /// </summary>
    public struct FarTreeStyle
    {
        /// <summary>Impostor shape.</summary>
        public FarTreeShape Shape;

        /// <summary>Height of the tree above its pivot at scale 1, metres.</summary>
        public float Height;

        /// <summary>Widest crown radius at scale 1, metres.</summary>
        public float Radius;

        /// <summary>Average foliage colour, linear RGB.</summary>
        public Vector3 Color;

        /// <summary>A second foliage colour each tree is lerped toward by its own amount (the colour noise of the leaf shaders), linear.</summary>
        public Vector3 Color2;

        /// <summary>Bark colour, linear.</summary>
        public Vector3 TrunkColor;

        /// <summary>Height of the lowest foliage at scale 1, metres.</summary>
        public float CrownBase;

        /// <summary>Height of the widest ring at scale 1, metres.</summary>
        public float WidestAt;

        /// <summary>Crown radius at 81 % of the way from the crown base to the top, at scale 1.</summary>
        public float TopRadius;

        /// <summary>Trunk radius at the ground at scale 1, metres.</summary>
        public float TrunkRadius;

        /// <summary>
        /// Crown lobes fitted on the mesh (<see cref="TreeLobes"/>, impostor pass 2026-10-06), scale 1, pivot at the trunk base; null: the
        /// single lathe crown. Read-only once set (shared between copies of the style and worker threads).
        /// </summary>
        public TreeLobe[] Lobes;

        /// <summary>The <see cref="TileLod.Mid"/> tiles' lobes (at most three, fitted on the same mesh); null: <see cref="Lobes"/>.</summary>
        public TreeLobe[] MidLobes;

        /// <summary>Height of the trunk's main fork at scale 1 (lobed crowns only).</summary>
        public float ForkY;

        /// <summary>
        /// The morning fit's "lobes without an upper ring" flag. Since the domed lobes (2026-10-06, afternoon) every lobe has the same
        /// three rings; kept for the measured table's rows, it no longer changes the geometry.
        /// </summary>
        public bool FlatLobes;

        /// <summary>
        /// A crown of leaf cards (the PNB birch, Meadow and fruit trees), not a faceted solid (the PolygonGeneric trees and pines):
        /// its impostor faces carry the lit-top ladder (<see cref="TreeLobes.LeafTop"/>) and the far shader's foliage light (vertex alpha 0).
        /// </summary>
        public bool LeafCrown;

        /// <summary>
        /// Season class of the leaves (<see cref="MedievalZen.Environment.SeasonLook"/>: oak, birch, fruit; 0 = unchanged): carried in the
        /// leaf faces' vertex alpha (<see cref="TreeLobes.LeafAlpha"/>) so the far shader tints each species by the season.
        /// </summary>
        public int SeasonClass;

        /// <summary>True when the crown is drawn as lobes.</summary>
        public bool HasLobes => Lobes != null && Lobes.Length > 0 && Shape == FarTreeShape.Broadleaf;

        /// <summary>Triangles one detailed impostor (<see cref="TileLod.Mid"/> tiles) of this style costs.</summary>
        public int Triangles => FarTileMeshData.TrianglesOf(this, true);

        /// <summary>Triangles one simple impostor (<see cref="TileLod.Far"/> tiles) of this style costs.</summary>
        public int SimpleTriangles => FarTileMeshData.TrianglesOf(this, false);

        /// <summary>A style with the crown proportions of <paramref name="shape"/>'s typical tree, for prefabs that were not measured.</summary>
        public static FarTreeStyle Proportional(FarTreeShape shape, float height, float radius, Vector3 leaf, Vector3 trunk)
        {
            height = Math.Max(1f, height);
            radius = Math.Max(0.4f, radius);
            float baseFraction = shape == FarTreeShape.Conifer ? 0.3f : shape == FarTreeShape.Dead ? 0f : 0.32f;
            return new FarTreeStyle
            {
                Shape = shape,
                Height = height,
                Radius = radius,
                Color = leaf,
                Color2 = leaf,
                TrunkColor = trunk,
                CrownBase = baseFraction * height,
                WidestAt = shape == FarTreeShape.Conifer ? baseFraction * height : 0.52f * height,
                TopRadius = 0.6f * radius,
                TrunkRadius = Math.Max(0.15f, 0.12f * radius),
            };
        }
    }

    /// <summary>
    /// The measured far-tree styles of the palette's tree prefabs (offline, 2026-10-02, from the FBX of the mesh LOD the near renderer
    /// draws out to the near/far boundary: LOD2 of the PNB Meadow and birch trees, LOD1 of the fruit trees, the single mesh of the
    /// PolygonGeneric trees). Heights, crown base, widest ring and its height, the ring at 81 % of the crown (90th percentile of the
    /// foliage vertices' distance from the trunk per height band; widest = 95th percentile overall) and the trunk radius.
    /// <para>
    /// Colours (linear): PNB birch and fruit leaves are flat-coloured by the Synty foliage graph (lerp(lerp(noise, base, small
    /// noise), large colour, large noise) with each material's colours and frequencies; mean over 40,000 world positions, and the
    /// 10th/90th percentile of 8 m crown averages as the two colours a tree varies between): birch orange to yellow (sRGB
    /// 177, 95, 35 → 191, 135, 23), fruit yellow-green (106, 136, 6 → 119, 137, 0). Meadow trees: the leafPatch_04 texture's mean over
    /// opaque texels (61, 74, 24) times the graph's tint mean, (60, 65, 7). PolygonGeneric trees: the Generic_01_A atlas sampled at
    /// each foliage triangle's UV, area-weighted: broadleaf (98, 114, 76), pine (87, 93, 68), dead (57, 52, 45). Bark from the
    /// trunk textures at the trunk triangles' UVs (birch bark is white).
    /// </para>
    /// </summary>
    public static class FarTreeShapes
    {
        private struct Row
        {
            public string Name;
            public FarTreeShape Shape;
            public float H, CrownBase, R, WidestAt, TopR, TrunkR;
            public Vector3 Leaf, Leaf2, Trunk;
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // Apparent crown colours (impostor pass, 2026-10-06), linear. The leaf albedos measured above (birch 0.438/0.113/0.017 to
        // 0.520/0.244/0.009, Meadow 0.047/0.051/0.002 to 0.043/0.056/0.004, fruit 0.143/0.244/0.002 to 0.185/0.251/0, Generic
        // 0.126/0.172/0.074, pine 0.095/0.110/0.058) drawn on a flat-shaded solid did not look like the crowns they stand for: a real
        // crown is leaves with holes, dark interior and self-shadow, lit through the foliage shader. Calibrated on review shots
        // (tour 2026-10-06 08:10, T0002 and T0026: real crown against impostor of the same species at a similar distance, fog
        // removed with an estimated transmittance exp(-0.00045 d)): the solid Generic and pine impostors were about twice as bright
        // as the meshes beside them, the Meadow oak impostor olive-yellow and too bright where the oaks are dark blue-green, and the
        // birch impostor a dull brown where the birches are a saturated orange. See ENDLESS_ROAD.md, "Tree impostors that match".
        // Recalibrated the same afternoon on the first clear-noon shots (tour 2026-10-06 09:38/09:43): the morning's values were fitted
        // on hazy shots against a grey lighting factor and came out several times too dark for the oaks (linear 0.012-0.02: under the
        // noon sun a solid that dark is near black, and at 400-800 m the haze alone set its tone, so the lit and shaded faces looked
        // one flat grey) and far too bright for the birches (0.8 red clips to a flat orange). These are the albedos whose sun-lit faces
        // (sun 1.35 at 57°, the trilight ambient) and shaded faces best match a real crown's sun-lit and shaded averages measured at
        // 100-250 m with the haze removed (worst of the two ΔE76: oak 13, birch 12, fruit 15; a solid cannot hold a crown's 10:1
        // leaf-to-hole contrast, so it splits the difference and is never darker than the real crown's shade). Generic and pine
        // meshes are themselves flat atlas colours: their impostors take the atlas albedo × 0.9. See ENDLESS_ROAD.md, "Rounded lobes".
        // Since the distant-tree pass (2026-10-06 evening) the leaf crowns (birch, Meadow, fruit) draw these colours through the lit-top
        // ladder (TreeLobes.LeafTop / LeafMid / LeafUnder, 0.72 on average) and the far shader's foliage light; the values below are
        // unchanged and still the reference the ladder scales.
        private static readonly Vector3 BirchA = V(0.2150f, 0.1050f, 0.0300f), BirchB = V(0.2000f, 0.1250f, 0.0340f);
        private static readonly Vector3 MeadowA = V(0.0780f, 0.0840f, 0.0290f), MeadowB = V(0.0880f, 0.0920f, 0.0330f);
        private static readonly Vector3 FruitA = V(0.1600f, 0.1950f, 0.0400f), FruitB = V(0.1750f, 0.2000f, 0.0430f);
        private static readonly Vector3 GenLeaf = V(0.1130f, 0.1550f, 0.0670f), GenBark = V(0.1470f, 0.1119f, 0.0762f);
        private static readonly Vector3 PineLeaf = V(0.0860f, 0.0990f, 0.0520f), PineBark = V(0.0915f, 0.0707f, 0.0514f);
        private static readonly Vector3 DeadWood = V(0.0409f, 0.0343f, 0.0262f);

        private static readonly Row[] Table =
        {
            new Row { Name = "SM_Env_Tree_Birch_01", Shape = FarTreeShape.Broadleaf, H = 9.24f, CrownBase = 3.06f, R = 2.08f, WidestAt = 4.59f, TopR = 0.9f, TrunkR = 0.46f, Leaf = BirchA, Leaf2 = BirchB, Trunk = V(0.4987f, 0.4580f, 0.4400f) },
            new Row { Name = "SM_Env_Tree_Birch_02", Shape = FarTreeShape.Broadleaf, H = 8.30f, CrownBase = 2.96f, R = 2.33f, WidestAt = 4.29f, TopR = 1.5f, TrunkR = 0.35f, Leaf = BirchA, Leaf2 = BirchB, Trunk = V(0.6257f, 0.5786f, 0.5589f) },
            new Row { Name = "SM_Env_Tree_Birch_03", Shape = FarTreeShape.Broadleaf, H = 4.64f, CrownBase = 2.01f, R = 1.14f, WidestAt = 3.07f, TopR = 1.0f, TrunkR = 0.17f, Leaf = BirchA, Leaf2 = BirchB, Trunk = V(0.5720f, 0.5279f, 0.5090f) },
            new Row { Name = "SM_Env_Tree_Meadow_01", Shape = FarTreeShape.Broadleaf, H = 12.77f, CrownBase = 3.86f, R = 7.73f, WidestAt = 5.31f, TopR = 4.7f, TrunkR = 1.43f, Leaf = MeadowA, Leaf2 = MeadowB, Trunk = V(0.1262f, 0.0800f, 0.0591f) },
            new Row { Name = "SM_Env_Tree_Meadow_02", Shape = FarTreeShape.Broadleaf, H = 7.50f, CrownBase = 2.28f, R = 4.15f, WidestAt = 4.75f, TopR = 3.0f, TrunkR = 0.81f, Leaf = MeadowA, Leaf2 = MeadowB, Trunk = V(0.0969f, 0.0648f, 0.0468f) },
            new Row { Name = "SM_Env_Tree_Fruit_01", Shape = FarTreeShape.Broadleaf, H = 5.15f, CrownBase = 1.96f, R = 2.30f, WidestAt = 2.95f, TopR = 1.5f, TrunkR = 0.49f, Leaf = FruitA, Leaf2 = FruitB, Trunk = V(0.0880f, 0.0548f, 0.0472f) },
            new Row { Name = "SM_Env_Tree_Fruit_02", Shape = FarTreeShape.Broadleaf, H = 4.70f, CrownBase = 2.13f, R = 1.95f, WidestAt = 2.87f, TopR = 1.3f, TrunkR = 0.54f, Leaf = FruitA, Leaf2 = FruitB, Trunk = V(0.0886f, 0.0551f, 0.0475f) },
            new Row { Name = "SM_Env_Tree_Fruit_03", Shape = FarTreeShape.Broadleaf, H = 3.87f, CrownBase = 2.04f, R = 1.70f, WidestAt = 2.54f, TopR = 0.8f, TrunkR = 0.40f, Leaf = FruitA, Leaf2 = FruitB, Trunk = V(0.0905f, 0.0562f, 0.0484f) },
            new Row { Name = "SM_Gen_Env_Tree_01", Shape = FarTreeShape.Broadleaf, H = 8.15f, CrownBase = 2.67f, R = 2.28f, WidestAt = 4.30f, TopR = 1.5f, TrunkR = 0.29f, Leaf = GenLeaf * 0.977f, Leaf2 = GenLeaf * 0.977f, Trunk = GenBark },
            new Row { Name = "SM_Gen_Env_Tree_02", Shape = FarTreeShape.Broadleaf, H = 8.43f, CrownBase = 2.16f, R = 1.58f, WidestAt = 3.62f, TopR = 0.7f, TrunkR = 0.34f, Leaf = GenLeaf * 0.98f, Leaf2 = GenLeaf * 0.98f, Trunk = GenBark },
            new Row { Name = "SM_Gen_Env_Tree_03", Shape = FarTreeShape.Broadleaf, H = 6.04f, CrownBase = 1.87f, R = 1.46f, WidestAt = 2.72f, TopR = 0.8f, TrunkR = 0.28f, Leaf = GenLeaf * 1.044f, Leaf2 = GenLeaf * 1.044f, Trunk = GenBark },
            new Row { Name = "SM_Gen_Env_Tree_Pine_01", Shape = FarTreeShape.Conifer, H = 9.26f, CrownBase = 4.91f, R = 0.92f, WidestAt = 5.27f, TopR = 0.4f, TrunkR = 0.27f, Leaf = PineLeaf, Leaf2 = PineLeaf, Trunk = PineBark },
            new Row { Name = "SM_Gen_Env_Tree_Pine_02", Shape = FarTreeShape.Conifer, H = 6.71f, CrownBase = 2.90f, R = 0.86f, WidestAt = 3.09f, TopR = 0.4f, TrunkR = 0.25f, Leaf = PineLeaf, Leaf2 = PineLeaf, Trunk = PineBark },
            new Row { Name = "SM_Gen_Env_Tree_Pine_03", Shape = FarTreeShape.Conifer, H = 6.52f, CrownBase = 1.83f, R = 1.07f, WidestAt = 2.45f, TopR = 0.4f, TrunkR = 0.35f, Leaf = PineLeaf, Leaf2 = PineLeaf, Trunk = PineBark },
            new Row { Name = "SM_Gen_Env_Tree_Dead_01", Shape = FarTreeShape.Dead, H = 6.11f, CrownBase = 0f, R = 1.30f, WidestAt = 3.27f, TopR = 0.4f, TrunkR = 0.2f, Leaf = DeadWood, Leaf2 = DeadWood, Trunk = DeadWood },
            new Row { Name = "SM_Gen_Env_Tree_Dead_02", Shape = FarTreeShape.Dead, H = 9.35f, CrownBase = 0f, R = 2.20f, WidestAt = 6.30f, TopR = 1.5f, TrunkR = 0.25f, Leaf = DeadWood, Leaf2 = DeadWood, Trunk = DeadWood },
            new Row { Name = "SM_Gen_Env_Tree_Dead_03", Shape = FarTreeShape.Dead, H = 10.83f, CrownBase = 0f, R = 4.52f, WidestAt = 7.53f, TopR = 2.8f, TrunkR = 0.3f, Leaf = DeadWood, Leaf2 = DeadWood, Trunk = DeadWood },
        };

        /// <summary>
        /// Lobed crowns (impostor pass, 2026-10-06), measured offline: the mesh LOD the near renderer draws last, read from the FBX, its
        /// crown vertices (0.5 m and up, outside 1.5 trunk radii or above 90 % of the crown base) clustered by <see cref="TreeLobes.Fit"/>;
        /// lobe count (refitted for the domed lobes, 2026-10-06 afternoon; near: up to the triangle budget, 217 Meadow 01 (eight lobes,
        /// the fewest that keep it under 1.35 m), 139 Meadow 02, 113 the rest; Mid: four-sided lobes, at most two, three for Meadow 01), percentile
        /// and radial / vertical scale chosen for the least mean outline difference against the mesh clipped at the ground, seen level
        /// from eight bearings at 2 cm (first column near, second Mid, metres).
        /// </summary>
        private static readonly Dictionary<string, LobeRow> Lobed = new Dictionary<string, LobeRow>
        {
            ["SM_Env_Tree_Birch_01"] = new LobeRow(0.57f, 0.80f, true,
                new[] { L(-0.25f, 7.09f, 0.09f, 1.25f, 1.50f, 1.90f, 1.25f), L(0.90f, 5.47f, -0.07f, 1.18f, 1.68f, 1.29f, 1.24f), L(-0.80f, 4.70f, 0.80f, 1.39f, 1.33f, 1.44f, 1.32f), L(0.05f, 3.68f, -1.15f, 1.53f, 1.33f, 1.54f, 1.06f) },
                new[] { L(0.06f, 6.49f, 0.16f, 1.85f, 1.79f, 2.06f, 1.26f), L(-0.14f, 4.21f, -0.20f, 2.05f, 2.20f, 1.08f, 1.37f) }),
            ["SM_Env_Tree_Birch_02"] = new LobeRow(0.51f, 0.76f, true,
                new[] { L(0.30f, 6.65f, 0.51f, 1.50f, 1.42f, 1.48f, 1.37f), L(0.55f, 4.92f, -0.72f, 1.29f, 1.32f, 1.41f, 1.11f), L(-1.49f, 4.44f, 0.32f, 1.21f, 1.46f, 1.62f, 1.46f), L(0.65f, 3.39f, 0.99f, 1.20f, 1.14f, 1.60f, 0.91f) },
                new[] { L(0.42f, 6.18f, 0.21f, 1.74f, 1.90f, 1.49f, 1.36f), L(-0.60f, 4.04f, 0.30f, 2.29f, 2.02f, 1.26f, 1.19f) }),
            ["SM_Env_Tree_Birch_03"] = new LobeRow(0.26f, 0.34f, true,
                new[] { L(0.57f, 3.83f, 0.08f, 0.69f, 0.99f, 0.64f, 0.59f), L(-0.06f, 3.47f, -0.15f, 0.66f, 0.78f, 0.72f, 0.62f), L(0.59f, 2.73f, -0.02f, 0.78f, 0.71f, 0.53f, 0.54f), L(-0.31f, 2.30f, 0.06f, 0.73f, 0.81f, 0.68f, 0.46f) },
                new[] { L(0.40f, 3.58f, -0.04f, 1.02f, 0.84f, 0.85f, 0.74f), L(0.01f, 2.46f, 0.03f, 1.11f, 0.73f, 0.63f, 0.49f) }),
            ["SM_Env_Tree_Meadow_01"] = new LobeRow(1.25f, 1.88f, true,
                new[] { L(2.42f, 10.24f, 1.30f, 3.40f, 3.81f, 1.96f, 2.10f), L(-3.24f, 9.87f, 0.13f, 2.66f, 3.66f, 2.03f, 2.30f), L(1.07f, 8.47f, -4.28f, 3.12f, 3.27f, 2.90f, 2.33f), L(3.66f, 6.23f, 4.55f, 2.87f, 2.79f, 3.57f, 1.64f), L(-2.49f, 5.77f, 4.22f, 3.16f, 2.61f, 4.01f, 2.31f), L(4.84f, 5.61f, -0.08f, 3.19f, 2.48f, 1.73f, 1.91f), L(-5.00f, 4.86f, 0.03f, 2.70f, 3.21f, 1.37f, 1.53f), L(-2.45f, 4.29f, -5.69f, 3.15f, 3.44f, 1.99f, 1.69f) },
                new[] { L(3.81f, 7.39f, 1.71f, 3.85f, 4.79f, 4.77f, 3.56f), L(-0.43f, 7.26f, -4.41f, 4.87f, 3.82f, 4.95f, 4.86f), L(-3.45f, 7.21f, 1.89f, 3.69f, 4.27f, 5.01f, 4.23f) }),
            ["SM_Env_Tree_Meadow_02"] = new LobeRow(0.80f, 1.23f, true,
                new[] { L(-0.90f, 5.14f, 2.14f, 2.86f, 2.05f, 1.65f, 1.15f), L(2.04f, 5.09f, 1.03f, 2.03f, 2.43f, 1.55f, 1.27f), L(1.55f, 4.96f, -2.15f, 2.05f, 2.52f, 1.61f, 1.16f), L(-1.40f, 4.95f, -1.82f, 2.34f, 2.48f, 1.67f, 1.07f), L(-1.48f, 2.57f, 0.40f, 1.87f, 1.55f, 1.42f, 0.67f) },
                new[] { L(0.38f, 4.91f, -1.84f, 3.51f, 2.48f, 1.70f, 1.82f), L(-0.38f, 4.46f, 1.44f, 3.52f, 2.38f, 2.13f, 1.99f) }),
            ["SM_Gen_Env_Tree_01"] = new LobeRow(0.50f, 0.57f, true,
                new[] { L(0.12f, 4.46f, 0.02f, 1.97f, 2.13f, 3.32f, 2.13f) },
                new[] { L(0.12f, 4.46f, 0.02f, 1.92f, 1.89f, 3.36f, 2.21f) }),
            ["SM_Gen_Env_Tree_02"] = new LobeRow(0.50f, 0.58f, true,
                new[] { L(-0.08f, 6.80f, -0.03f, 0.78f, 0.88f, 1.77f, 1.13f), L(0.67f, 4.51f, -0.53f, 0.97f, 1.05f, 1.44f, 1.16f), L(-0.70f, 4.34f, 0.47f, 0.95f, 1.17f, 1.36f, 1.28f), L(0.05f, 2.63f, -0.12f, 1.21f, 1.16f, 0.81f, 0.77f) },
                new[] { L(-0.02f, 4.38f, -0.05f, 1.41f, 1.27f, 3.67f, 2.52f) }),
            ["SM_Gen_Env_Tree_03"] = new LobeRow(0.47f, 0.53f, true,
                new[] { L(-0.15f, 4.60f, 0.05f, 0.73f, 0.82f, 1.43f, 0.72f), L(0.74f, 3.12f, -0.37f, 0.70f, 0.92f, 1.00f, 1.12f), L(-0.29f, 2.38f, 0.13f, 0.86f, 0.98f, 1.17f, 0.74f) },
                new[] { L(0.03f, 3.18f, -0.03f, 1.22f, 1.23f, 2.59f, 1.44f) }),
            ["SM_Env_Tree_Fruit_01"] = new LobeRow(0.51f, 0.64f, true,
                new[] { L(0.59f, 4.09f, -0.39f, 1.88f, 1.73f, 0.85f, 0.99f), L(-0.59f, 3.02f, -1.32f, 1.76f, 1.32f, 1.49f, 1.08f), L(-1.05f, 2.71f, 0.57f, 1.38f, 1.53f, 1.50f, 0.93f), L(1.02f, 2.67f, 0.34f, 1.45f, 1.88f, 1.09f, 0.91f) },
                new[] { L(0.94f, 3.01f, 0.13f, 1.38f, 2.17f, 1.55f, 1.05f), L(-0.87f, 3.00f, -0.34f, 1.61f, 2.37f, 1.63f, 1.12f) }),
            ["SM_Env_Tree_Fruit_02"] = new LobeRow(0.39f, 0.45f, true,
                new[] { L(0.31f, 3.51f, -0.78f, 1.04f, 1.19f, 0.97f, 1.29f), L(-1.13f, 2.98f, -0.71f, 0.97f, 1.07f, 1.12f, 1.34f), L(0.89f, 2.94f, 0.30f, 0.93f, 1.16f, 1.16f, 1.05f), L(-0.77f, 2.90f, 0.82f, 1.18f, 0.93f, 1.50f, 1.47f) },
                new[] { L(0.56f, 3.25f, -0.32f, 1.23f, 1.73f, 1.18f, 1.37f), L(-0.95f, 2.95f, 0.14f, 1.17f, 1.82f, 1.38f, 1.51f) }),
            ["SM_Env_Tree_Fruit_03"] = new LobeRow(0.36f, 0.41f, true,
                new[] { L(-0.35f, 2.69f, 0.59f, 1.49f, 0.94f, 0.95f, 0.73f), L(-0.03f, 2.50f, -0.74f, 1.29f, 1.04f, 1.05f, 0.95f) },
                new[] { L(-0.18f, 2.59f, -0.09f, 1.61f, 1.85f, 0.92f, 0.84f) }),
        };

        private static TreeLobe L(float x, float y, float z, float rx, float rz, float up, float down) => new TreeLobe(x, y, z, rx, rz, up, down);

        private readonly struct LobeRow
        {
            public readonly float Silhouette, MidSilhouette;
            public readonly bool Flat;
            public readonly TreeLobe[] Near, Mid;

            public LobeRow(float silhouette, float midSilhouette, bool flat, TreeLobe[] near, TreeLobe[] mid)
            {
                Silhouette = silhouette;
                MidSilhouette = midSilhouette;
                Flat = flat;
                Near = near;
                Mid = mid;
            }
        }


        /// <summary>The measured mean outline difference of a lobed impostor against its mesh (metres; false when the prefab has no lobes).</summary>
        public static bool TryGetLobedSilhouette(string prefabName, out float nearMetres, out float midMetres)
        {
            nearMetres = midMetres = 0f;
            if (string.IsNullOrEmpty(prefabName)) return false;
            foreach (KeyValuePair<string, LobeRow> entry in Lobed)
            {
                if (prefabName.IndexOf(entry.Key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                nearMetres = entry.Value.Silhouette;
                midMetres = entry.Value.MidSilhouette;
                return true;
            }

            return false;
        }

        /// <summary>Number of measured prefabs.</summary>
        public static int Count => Table.Length;

        /// <summary>The measured style of a tree prefab by name (a palette copy's name containing the Synty name counts), or false.</summary>
        public static bool TryGet(string prefabName, out FarTreeStyle style)
        {
            style = default;
            if (string.IsNullOrEmpty(prefabName)) return false;
            foreach (Row r in Table)
            {
                if (prefabName.IndexOf(r.Name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                style = new FarTreeStyle
                {
                    Shape = r.Shape,
                    Height = r.H,
                    Radius = r.R,
                    Color = r.Leaf,
                    Color2 = r.Leaf2,
                    TrunkColor = r.Trunk,
                    CrownBase = r.CrownBase,
                    WidestAt = r.WidestAt,
                    TopRadius = r.TopR,
                    TrunkRadius = r.TrunkR,
                    LeafCrown = r.Shape == FarTreeShape.Broadleaf && r.Name.StartsWith("SM_Env_Tree_", StringComparison.Ordinal),
                    SeasonClass = MedievalZen.Environment.SeasonLook.LeafClassOfTree(r.Name),
                };
                if (r.Shape == FarTreeShape.Broadleaf && Lobed.TryGetValue(r.Name, out LobeRow lobes))
                {
                    style.Lobes = lobes.Near;
                    style.MidLobes = lobes.Mid;
                    style.FlatLobes = lobes.Flat;
                    style.ForkY = r.CrownBase;
                }

                return true;
            }

            return false;
        }

        private static Dictionary<string, string[]> families;

        /// <summary>The prefab name of a family's variant in the catalogue order the palette is built in (variant wrapped), or null.</summary>
        public static string CatalogName(string role, int variant)
        {
            Dictionary<string, string[]> map = families;
            if (map == null)
            {
                map = new Dictionary<string, string[]>();
                foreach (var f in SyntyCatalog.Families) map[f.family] = f.paths;
                families = map;
            }

            if (role == null || !map.TryGetValue(role, out string[] paths) || paths.Length == 0) return null;
            string path = paths[((variant % paths.Length) + paths.Length) % paths.Length];
            int slash = path.LastIndexOf('/');
            string file = slash >= 0 ? path.Substring(slash + 1) : path;
            int dot = file.LastIndexOf('.');
            return dot > 0 ? file.Substring(0, dot) : file;
        }
    }

    /// <summary>
    /// Far-tree styles per tree family and variant (resolved like the palette: variant wrapped). Built on the main thread from the
    /// palette's prefabs (<see cref="ForPrefab"/>), read-only afterwards (safe to share with worker threads). Families it does not
    /// know resolve through the catalogue and the measured table (<see cref="FarTreeShapes"/>).
    /// </summary>
    public sealed class FarTreeStyles
    {
        private readonly Dictionary<string, FarTreeStyle[]> styles = new Dictionary<string, FarTreeStyle[]>();

        /// <summary>Styles of every tree family from the catalogue and the measured table (no palette).</summary>
        public static readonly FarTreeStyles Defaults = new FarTreeStyles();

        /// <summary>Sets the styles of a family (variants in palette order).</summary>
        public void Set(string role, FarTreeStyle[] variants)
        {
            if (string.IsNullOrEmpty(role) || variants == null || variants.Length == 0) return;
            styles[role] = variants;
        }

        /// <summary>The style for a placement's family and variant.</summary>
        public FarTreeStyle Resolve(string role, int variant)
        {
            if (role != null && styles.TryGetValue(role, out FarTreeStyle[] list) && list.Length > 0)
            {
                return list[((variant % list.Length) + list.Length) % list.Length];
            }

            if (FarTreeShapes.TryGet(FarTreeShapes.CatalogName(role, variant), out FarTreeStyle measured)) return measured;
            return DefaultFor(role);
        }

        /// <summary>A sensible style for a family when nothing is known (pines tall cones, dead trees thin, the rest broadleaf).</summary>
        public static FarTreeStyle DefaultFor(string role)
        {
            if (role == PropRole.PineTree) return ForPrefab("Pine", 8f, 1f);
            if (role == PropRole.DeadTree) return ForPrefab("Dead", 8f, 2f);
            if (role == PropRole.FruitTree) return ForPrefab("Fruit", 4.6f, 2f);
            return ForPrefab(role == PropRole.MeadowTree ? "Meadow" : "Tree", 8f, 2.5f);
        }

        /// <summary>
        /// The style of a tree prefab: the measured one when its name is in <see cref="FarTreeShapes"/>, else a proportional style
        /// from its size (height above the pivot, largest horizontal half-extent) with the shape from its name.
        /// </summary>
        public static FarTreeStyle ForPrefab(string name, float height, float halfExtent)
        {
            if (FarTreeShapes.TryGet(name, out FarTreeStyle measured)) return measured;
            name = name ?? string.Empty;
            FarTreeShape shape = Contains(name, "Pine") ? FarTreeShape.Conifer : Contains(name, "Dead") ? FarTreeShape.Dead : FarTreeShape.Broadleaf;
            Vector3 leaf = shape == FarTreeShape.Conifer ? new Vector3(0.0480f, 0.0550f, 0.0290f)
                : shape == FarTreeShape.Dead ? new Vector3(0.0409f, 0.0343f, 0.0262f)
                : new Vector3(0.0630f, 0.0860f, 0.0370f);
            return FarTreeStyle.Proportional(shape, height, halfExtent * 0.85f, leaf, new Vector3(0.10f, 0.07f, 0.05f));
        }

        private static bool Contains(string s, string part) => s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>The solid a distant prop is drawn as.</summary>
    public enum FarPropKind
    {
        /// <summary>A faceted lump of rock (cliff blocks, outcrops, boulders).</summary>
        Rock = 0,

        /// <summary>A house: walls and a gabled roof.</summary>
        House = 1,

        /// <summary>A windmill: a tower, a pointed roof and crossed sails.</summary>
        Windmill = 2,

        /// <summary>A landmark watchtower: a square shaft and a pointed cap.</summary>
        Tower = 3,

        /// <summary>A landmark chapel: a gabled nave and a bell tower with a spire.</summary>
        Chapel = 4,

        /// <summary>A landmark windmill: a gabled house, a mill tower with a pointed cap and crossed sails.</summary>
        Mill = 5,

        /// <summary>A standing stone: a tapering slab.</summary>
        Stone = 6,

        /// <summary>A ruin: four broken walls round an open middle.</summary>
        Ruin = 7,
    }

    /// <summary>Size (local bounds at scale 1) and colours (linear) of a distant prop.</summary>
    public struct FarPropStyle
    {
        /// <summary>Solid kind.</summary>
        public FarPropKind Kind;

        /// <summary>Local bounds at scale 1 (pivot space).</summary>
        public Vector3 Min, Max;

        /// <summary>Height of the eaves above the pivot at scale 1 (houses, windmills).</summary>
        public float Eaves;

        /// <summary>Wall (or rock) colour, linear.</summary>
        public Vector3 Wall;

        /// <summary>Roof colour, linear.</summary>
        public Vector3 Roof;

        /// <summary>Landmarks: height of the main roof's ridge (chapel nave, mill house) at scale 1.</summary>
        public float Ridge;

        /// <summary>Landmarks: plan centre (x, z) of the tower, spire or mill tower, and its half width (y), at scale 1.</summary>
        public Vector3 Feature;

        /// <summary>Landmarks: height where the tower's walls end and its cap or spire begins, and the tip, at scale 1.</summary>
        public float FeatureTop, Peak;

        /// <summary>Mills: hub of the sails (local), their arm length, and the sails' colour (linear).</summary>
        public Vector3 Hub;

        /// <summary>Mills: arm length of the sails at scale 1.</summary>
        public float Arm;

        /// <summary>Mills: colour of the sails, linear.</summary>
        public Vector3 Trim;

        /// <summary>True when placements of this family are centred on their bounds (the solid is drawn about the placement point).</summary>
        public bool Centred;
    }

    /// <summary>
    /// A walled city's castle on the distant tiles (2026-10-09): not its 1,500 kit pieces one slab each (22,000 triangles on one 8 m
    /// tile, more than its cap, and a hedge of 5 m slabs whatever the piece), but the castle's mass as the kit pieces build it: a
    /// grid of <see cref="Cell"/> m columns over the plan, each as tall as its walls and towers, merged along rows into blocks, flat
    /// stone tops where walls and battlements end the column and gabled roofs where the kit's roofs do (towers and the keep stand
    /// out by their height). The block's size is packed into the placement's variant (<see cref="Pack"/>), so a proxy is an ordinary
    /// <see cref="World.PropPlacement"/> that <see cref="FarTileMeshData"/> draws as a house solid (14 triangles).
    /// </summary>
    public static class FarCastle
    {
        /// <summary>A stone block with a flat top (walls, battlements, towers without a roof).</summary>
        public const string Block = "FarCastleBlock";

        /// <summary>A stone block under a gabled roof (the kit's roofs, the towers' caps).</summary>
        public const string Roof = "FarCastleRoof";

        /// <summary>Column size of the castle's grid (the kit's 5 m modules at the plans' x0.9), metres.</summary>
        public const float Cell = 4.5f;

        /// <summary>Height step of a packed size, metres (127 steps: 95 m).</summary>
        public const float Step = 0.75f;

        /// <summary>Most cells merged into one block.</summary>
        public const int MaxRun = 31;

        /// <summary>The castle kit's stone grey (as <see cref="FarPropStyles.CityWall"/>) and its roofs' colour (the Fantasy Kingdom roof tiles, as the watchtower's), linear.</summary>
        public static readonly Vector3 Stone = new Vector3(0.1573f, 0.1374f, 0.1198f), RoofColor = new Vector3(0.1745f, 0.0849f, 0.0407f);

        /// <summary>True for a castle proxy's family.</summary>
        public static bool IsProxy(string role) => role == Block || role == Roof;

        /// <summary>The variant of a block <paramref name="cells"/> cells long, its walls to <paramref name="eaves"/> and its top at <paramref name="top"/> metres above its pivot.</summary>
        public static int Pack(int cells, float eaves, float top)
        {
            int e = Clamp((int)Math.Round(eaves / Step), 1, 127), t = Clamp((int)Math.Round(top / Step), e, 127);
            return e | (t << 7) | (Clamp(cells, 1, MaxRun) << 14);
        }

        /// <summary>Unpacks <see cref="Pack"/>.</summary>
        public static void Unpack(int variant, out int cells, out float eaves, out float top)
        {
            eaves = (variant & 127) * Step;
            top = ((variant >> 7) & 127) * Step;
            cells = Math.Max(1, (variant >> 14) & 31);
        }

        /// <summary>The solid of a proxy: centred on its pivot, <c>cells x Cell</c> along local x, one cell deep, from the pivot up.</summary>
        public static FarPropStyle Style(string role, int variant)
        {
            Unpack(variant, out int cells, out float eaves, out float top);
            float hx = 0.5f * cells * Cell, hz = 0.5f * Cell;
            bool roof = role == Roof && top > eaves + 0.1f;
            return new FarPropStyle
            {
                Kind = FarPropKind.House, Min = new Vector3(-hx, 0f, -hz), Max = new Vector3(hx, roof ? top : eaves, hz), Eaves = eaves,
                Wall = Stone, Roof = roof ? RoofColor : Stone, Centred = true,
            };
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>
    /// Far-prop styles per family and variant: rocks from the palette's bounds (or the offline <see cref="PropMetricsTable"/>), in
    /// the rock layer's colour (the Meadow rocks show the RockWall texture the rock paint uses); the stone cabin and windmills as
    /// measured offline on 2026-10-02 from their FBX and the Meadow atlas at their triangles' UVs, area-weighted: cabin 6.0 × 8.1 m,
    /// 4.0 m high, eaves at 2.0 m, walls sRGB (121, 113, 104), roof (121, 107, 92); windmills 11 m high, eaves at 4.8 m, walls
    /// (123, 107, 90), roof (135, 115, 96). Read-only after building; safe to share with worker threads.
    /// </summary>
    public sealed class FarPropStyles
    {
        private readonly Dictionary<string, FarPropStyle[]> styles = new Dictionary<string, FarPropStyle[]>();

        /// <summary>Styles from the catalogue's measured metrics (no palette).</summary>
        public static readonly FarPropStyles Defaults = new FarPropStyles();

        /// <summary>Measured stone cabin.</summary>
        public static readonly FarPropStyle Cabin = new FarPropStyle
        {
            Kind = FarPropKind.House, Min = new Vector3(-3f, 0f, -4.06f), Max = new Vector3(3f, 4.04f, 4.06f), Eaves = 1.99f,
            Wall = new Vector3(0.1908f, 0.1640f, 0.1386f), Roof = new Vector3(0.1899f, 0.1464f, 0.1073f),
        };

        /// <summary>Measured windmill (bounds include the sails).</summary>
        public static readonly FarPropStyle Windmill = new FarPropStyle
        {
            Kind = FarPropKind.Windmill, Min = new Vector3(-4.75f, 0f, -4.7f), Max = new Vector3(4.75f, 10.98f, 3.09f), Eaves = 4.81f,
            Wall = new Vector3(0.1966f, 0.1476f, 0.1014f), Roof = new Vector3(0.2426f, 0.1727f, 0.1178f),
        };

        /// <summary>Sets the styles of a family (variants in palette order).</summary>
        public void Set(string role, FarPropStyle[] variants)
        {
            if (string.IsNullOrEmpty(role) || variants == null || variants.Length == 0) return;
            styles[role] = variants;
        }

        // Landmarks, measured offline on 2026-10-02 from their FBX (bounds by height band) and atlases (area-weighted at the triangles'
        // UVs; roof = up-tilted faces in the upper 60 %, walls = vertical faces). Plan coordinates are about the prefab's bounds centre,
        // where the palette centres these families (centerPivot). Fantasy Kingdom atlas PolygonFantasyKingdom_01_A, Meadow atlas for the stones.
        private static Vector3 Lin(float r, float g, float b) => new Vector3(r, g, b);

        /// <summary>Measured watchtower (<c>SM_Bld_Preset_Tower_01</c>): 9.0 x 9.4 m shaft to 14.5 m, a narrow cap to 20.7 m; stone (110, 104, 97), roof (116, 82, 57).</summary>
        /// <summary>
        /// A walled city's wall module far away (2026-10-06): a flat-topped slab along the placement's +Z, 5 m long, 0.6 m thick, 6.4 m to
        /// the top of its crenellations (the crenellation pieces themselves are not drawn far); the castle kit's stone grey.
        /// </summary>
        public static readonly FarPropStyle CityWall = new FarPropStyle
        {
            Kind = FarPropKind.House, Min = new Vector3(-0.3f, 0f, -2.5f), Max = new Vector3(0.3f, 6.4f, 2.5f), Eaves = 6.4f,
            Wall = Lin(0.1573f, 0.1374f, 0.1198f), Roof = Lin(0.1573f, 0.1374f, 0.1198f),
        };

        /// <summary>A city wall tower far away: the kit tower's 3.05 m square to 5 m (times the placement's scale), flat-topped.</summary>
        public static readonly FarPropStyle CityTower = new FarPropStyle
        {
            Kind = FarPropKind.House, Min = new Vector3(-1.5f, 0f, -1.5f), Max = new Vector3(1.5f, 5f, 1.5f), Eaves = 5f,
            Wall = Lin(0.1573f, 0.1374f, 0.1198f), Roof = Lin(0.1573f, 0.1374f, 0.1198f),
        };

        /// <summary>
        /// A piece of a city's castle far away (2026-10-06): every castle-kit piece drawn as a wall module's slab (5 m along +Z, 5 m
        /// tall, 0.7 m thick, times the placement's scale), so the castle's mass and outline stand over the town from a kilometre.
        /// </summary>
        public static readonly FarPropStyle CastleModule = new FarPropStyle
        {
            Kind = FarPropKind.House, Min = new Vector3(-0.35f, 0f, -2.5f), Max = new Vector3(0.35f, 5f, 2.5f), Eaves = 5f,
            Wall = Lin(0.1573f, 0.1374f, 0.1198f), Roof = Lin(0.1573f, 0.1374f, 0.1198f),
        };
        public static readonly FarPropStyle Tower = new FarPropStyle
        {
            Kind = FarPropKind.Tower, Min = new Vector3(-4.5f, 0f, -4.69f), Max = new Vector3(4.5f, 14.5f, 4.69f), Eaves = 14.5f, Centred = true,
            Feature = new Vector3(0.03f, 1.9f, -0.18f), FeatureTop = 14.5f, Peak = 20.7f, Wall = Lin(0.1573f, 0.1374f, 0.1198f), Roof = Lin(0.1745f, 0.0849f, 0.0407f),
        };

        /// <summary>Measured chapels (<c>Church_01_A/B</c>): nave 6.4 x 10.9 m, eaves 3.55 m, ridge 7.0 m; bell tower 2.9 m square to 8 / 9 m, spire to 9.2 / 11.7 m; walls (112, 109, 105), roof (129, 86, 56).</summary>
        public static readonly FarPropStyle[] Chapel =
        {
            new FarPropStyle
            {
                Kind = FarPropKind.Chapel, Min = new Vector3(-3.22f, 0f, -5.46f), Max = new Vector3(3.22f, 7f, 5.46f), Eaves = 3.55f, Ridge = 7f, Centred = true,
                Feature = new Vector3(0f, 1.6f, 1.32f), FeatureTop = 8f, Peak = 9.23f, Wall = Lin(0.1623f, 0.1515f, 0.1391f), Roof = Lin(0.2272f, 0.0900f, 0.0328f),
            },
            new FarPropStyle
            {
                Kind = FarPropKind.Chapel, Min = new Vector3(-3.22f, 0f, -5.46f), Max = new Vector3(3.22f, 7f, 5.46f), Eaves = 3.55f, Ridge = 7f, Centred = true,
                Feature = new Vector3(0f, 1.44f, 1.32f), FeatureTop = 9f, Peak = 11.73f, Wall = Lin(0.1625f, 0.1532f, 0.1419f), Roof = Lin(0.2211f, 0.0929f, 0.0392f),
            },
        };

        /// <summary>
        /// Measured windmill (<c>House_Windmill_01</c>): house 7.9 x 8.4 m to eaves at 6.3 m, a steep roof with its ridge across (along x)
        /// at 16.9 m, sails (arm 6.7 m) on the front at 10.25 m; walls (113, 105, 98), roof (121, 82, 55), sails (168, 150, 126).
        /// </summary>
        public static readonly FarPropStyle Mill = new FarPropStyle
        {
            Kind = FarPropKind.Mill, Min = new Vector3(-3.98f, 0f, -4.98f), Max = new Vector3(3.9f, 16.9f, 3.39f), Eaves = 6.3f, Ridge = 16.9f, Centred = true,
            Feature = new Vector3(0f, 3f, -0.15f), Hub = new Vector3(-0.01f, 10.25f, 4.19f), Arm = 6.7f,
            Wall = Lin(0.1655f, 0.1409f, 0.1212f), Roof = Lin(0.1910f, 0.0852f, 0.0378f), Trim = Lin(0.3897f, 0.3066f, 0.2095f),
        };

        /// <summary>Measured rune stones (<c>SM_Prop_StoneRunes_01..03</c>): 1.3-1.6 m wide, 0.6-1.0 m thick, 3.0-3.2 m tall; stone (113, 114, 113).</summary>
        public static readonly FarPropStyle[] Stones =
        {
            new FarPropStyle { Kind = FarPropKind.Stone, Min = new Vector3(-0.67f, -0.26f, -0.29f), Max = new Vector3(0.67f, 3.06f, 0.29f), Wall = Lin(0.1656f, 0.1669f, 0.1663f) },
            new FarPropStyle { Kind = FarPropKind.Stone, Min = new Vector3(-0.69f, -0.14f, -0.5f), Max = new Vector3(0.69f, 2.95f, 0.5f), Wall = Lin(0.1656f, 0.1669f, 0.1663f) },
            new FarPropStyle { Kind = FarPropKind.Stone, Min = new Vector3(-0.78f, -0.34f, -0.5f), Max = new Vector3(0.78f, 3.15f, 0.5f), Wall = Lin(0.1656f, 0.1669f, 0.1663f) },
        };

        /// <summary>Measured ruins (<c>SM_Prop_Destroyed_House_Preset_01/02</c>): 8.4 x 8.4 / 8.4 x 5.9 m, walls to 2.9 / 3.5 m; stone (93, 88, 86).</summary>
        public static readonly FarPropStyle[] Ruin =
        {
            new FarPropStyle { Kind = FarPropKind.Ruin, Min = new Vector3(-4.19f, 0f, -4.18f), Max = new Vector3(4.19f, 2.94f, 4.18f), Centred = true, Wall = Lin(0.1092f, 0.0970f, 0.0923f) },
            new FarPropStyle { Kind = FarPropKind.Ruin, Min = new Vector3(-4.18f, 0f, -2.93f), Max = new Vector3(4.18f, 3.47f, 2.93f), Centred = true, Wall = Lin(0.1091f, 0.0993f, 0.0945f) },
        };

        /// <summary>The style of a placement's family and variant.</summary>
        public FarPropStyle Resolve(string role, int variant)
        {
            if (role != null && styles.TryGetValue(role, out FarPropStyle[] list) && list.Length > 0)
            {
                return list[((variant % list.Length) + list.Length) % list.Length];
            }

            return DefaultFor(role, variant);
        }

        /// <summary>The catalogue's style: measured houses, rocks from <see cref="PaletteKeys.Default"/>'s metrics, else a 2 m boulder.</summary>
        public static FarPropStyle DefaultFor(string role, int variant)
        {
            if (role == PropRole.Cabin) return Cabin;
            if (role == PropRole.Windmill) return Windmill;
            if (PropRole.IsBuilding(role))
            {
                BuildingShape shape = BuildingTable.For(role, variant);
                if (shape != null) return Building(shape);
            }

            if (FarCastle.IsProxy(role)) return FarCastle.Style(role, variant);
            if (role == PropRole.CityWall) return CityWall;
            if (role == PropRole.CityTower) return CityTower;
            if (role == PropRole.CastleModule || role == PropRole.CastlePiece) return CastleModule;
            if (role == PropRole.LandmarkTower) return Tower;
            if (role == PropRole.LandmarkChapel) return Chapel[((variant % Chapel.Length) + Chapel.Length) % Chapel.Length];
            if (role == PropRole.LandmarkMill) return Mill;
            if (role == PropRole.LandmarkRuin) return Ruin[((variant % Ruin.Length) + Ruin.Length) % Ruin.Length];
            if (role == PropRole.StoneRunes) return Stones[((variant % Stones.Length) + Stones.Length) % Stones.Length];
            Vector3 rock = RockColorOf(role);
            var style = new FarPropStyle { Kind = FarPropKind.Rock, Min = new Vector3(-1f, 0f, -1f), Max = new Vector3(1f, 2f, 1f), Wall = rock, Roof = rock };
            if (PaletteKeys.Default.TryGetMetrics(role, variant, out PropMetrics m) && m.IsValid)
            {
                style.Min = m.min;
                style.Max = m.max;
            }

            return style;
        }

        /// <summary>
        /// A Fantasy Kingdom building preset far away (2026-10-05): walls round its measured ground-floor body up to the eaves and a gabled
        /// roof to its full height along the longer side (14 triangles), in its measured wall and roof colours (<see cref="BuildingTable"/>).
        /// </summary>
        public static FarPropStyle Building(BuildingShape shape) => new FarPropStyle
        {
            Kind = FarPropKind.House, Min = new Vector3(shape.BodyX0, 0f, shape.BodyZ0), Max = new Vector3(shape.BodyX1, shape.Height, shape.BodyZ1),
            Eaves = Math.Max(2f, Math.Min(shape.Eaves, shape.Height - 1f)), Wall = shape.Wall, Roof = shape.Roof,
        };

        /// <summary>Rock colour: the rock terrain layer's distant colour (<see cref="GroundLook.LayerColor"/>).</summary>
        public static Vector3 RockColor => GroundLook.LayerColor(TerrainShaper.RockLayer);

        /// <summary>
        /// Colour of the pass ranges' rocks (<see cref="PropRole.IsPassRock"/>): the Fantasy Kingdom atlas swatch their meshes use,
        /// linear (0.125, 0.117, 0.107) = sRGB (99, 96, 92), measured 2026-10-05.
        /// </summary>
        public static readonly Vector3 PassRockColor = new Vector3(0.1248f, 0.1170f, 0.1070f);

        /// <summary>Distant colour of a rock family: the pass ranges' grey, else <see cref="RockColor"/>.</summary>
        public static Vector3 RockColorOf(string role) => PropRole.IsPassRock(role) ? PassRockColor : RockColor;

        /// <summary>A rock style from a prefab's local bounds.</summary>
        public static FarPropStyle RockFromBounds(Vector3 min, Vector3 max) =>
            new FarPropStyle { Kind = FarPropKind.Rock, Min = min, Max = max, Wall = RockColor, Roof = RockColor };

        /// <summary>A rock style of family <paramref name="role"/> from a prefab's local bounds (coloured by <see cref="RockColorOf"/>).</summary>
        public static FarPropStyle RockFromBounds(string role, Vector3 min, Vector3 max) =>
            new FarPropStyle { Kind = FarPropKind.Rock, Min = min, Max = max, Wall = RockColorOf(role), Roof = RockColorOf(role) };
    }

    /// <summary>One lobe of a lobed impostor crown: a faceted blob round <see cref="Centre"/> (scale 1, pivot at the trunk base).</summary>
    public struct TreeLobe
    {
        /// <summary>Centre of the lobe's widest ring, metres.</summary>
        public Vector3 Centre;

        /// <summary>Half-widths of the widest ring along x and z, metres.</summary>
        public float RadiusX, RadiusZ;

        /// <summary>Height of the lobe's top above / of its bottom below <see cref="Centre"/>, metres.</summary>
        public float Up, Down;

        public TreeLobe(float x, float y, float z, float radiusX, float radiusZ, float up, float down)
        {
            Centre = new Vector3(x, y, z);
            RadiusX = radiusX;
            RadiusZ = radiusZ;
            Up = up;
            Down = down;
        }
    }

    /// <summary>
    /// Lobed impostor crowns (impostor pass, 2026-10-06). A broadleaf crown is a few faceted blobs placed from the mesh's own geometry:
    /// <see cref="Fit"/> clusters the crown vertices of the mesh LOD the near renderer draws last (k-means, deterministic start) and
    /// gives each cluster a blob through percentiles of its vertices' offsets; <see cref="Build"/> emits the blobs, the trunk and the
    /// main fork as indexed triangles. Since the afternoon of 2026-10-06 a lobe is a low dome, never a double pyramid: a flat tucked
    /// underside ring, the widest ring and a flat cap ring, each twisted half a side (near impostor band: five sides, 26 triangles;
    /// <see cref="TileLod.Mid"/> tiles: four sides, 20), one colour per lobe (a leaf crown: one per ring, the lit-top ladder); trunk
    /// (3) and in the near band two fork arms (6).
    /// Pure and deterministic; never a card.
    /// </summary>
    public static class TreeLobes
    {
        /// <summary>Sides of the rings of a near impostor's lobe (the near impostor band) and of a <see cref="TileLod.Mid"/> tile's lobe.</summary>
        public const int NearSides = 5, MidSides = 4;

        /// <summary>
        /// Domed lobes (2026-10-06, afternoon): the radius of the flat underside and of the flat cap as a share of the widest ring. The
        /// underside sits at <see cref="TreeLobe.Down"/> below the centre, the widest ring at the centre, the cap at <see cref="TreeLobe.Up"/>
        /// above it: from the side a lobe is a rounded mass with a flat top and a tucked-in flat bottom, never a point.
        /// </summary>
        public const float UndersideRadius = 0.6f, CapRadius = 0.6f;

        /// <summary>The ring's corners stand this far out (between inscribed and circumscribed on the measured radius) for five sides, and for four.</summary>
        public const float RingFit5 = 1.12f, RingFit4 = 1.2f;

        /// <summary>
        /// Lit-top ladder of a leaf crown (<see cref="FarTreeStyle.LeafCrown"/>; distant-tree pass, 2026-10-06 evening): the share of the
        /// leaf colour the faces coloured by the cap ring (the cap and every other upper-band face: sun-lit leaves, lighter and a touch
        /// warmer), the widest ring (the faces between) and the underside ring (every other lower-band face and the underside: the
        /// crown's shade, darker and a touch bluer) take. A face's colour comes from the ring that holds two of its corners, so the
        /// bands zig-zag light / mid / dark round the lobe at no cost. Fitted with the shader's foliage light (FarTerrain.shader:
        /// half-Lambert sun on alpha-0 faces, by sun height) on the review tours of 2026-10-06 (St Aldo's 5° telephoto, noon and low
        /// sun; The Muddy Fox overview): × 0.72 of the leaf colour on average, × 1.2 / 1 / 0.6 by ring. The backlit impostor's mean
        /// rises about 20 % toward the real crowns' (it was 60–70 % of them), stays within 2 % when the sun is in front or above,
        /// and is 5–10 % darker at low sun; never lighter than the real crowns. See ENDLESS_ROAD.md, "Distant trees that carry the light".
        /// </summary>
        public static readonly Vector3 LeafTop = new Vector3(0.907f, 0.907f, 0.823f), LeafMid = new Vector3(0.72f, 0.72f, 0.72f),
            LeafUnder = new Vector3(0.432f, 0.432f, 0.497f);

        /// <summary>Vertex alpha of a leaf crown's faces: 0 tells the far shader to give them the foliage light; everything else is 255.</summary>
        public const byte LeafCrownAlpha = 0;

        /// <summary>A leaf crown's face colour: <paramref name="leaf"/> × <paramref name="share"/> per channel, sRGB-encoded, alpha <see cref="LeafCrownAlpha"/>.</summary>
        public static Color32 LeafFace(Vector3 leaf, Vector3 share, byte alpha = LeafCrownAlpha)
        {
            Color32 c = FarTileMeshData.EncodeSrgbFast(new Vector3(leaf.x * share.x, leaf.y * share.y, leaf.z * share.z));
            c.a = alpha;
            return c;
        }

        /// <summary>
        /// Vertex alpha of a tree's leaf faces (seasons pass, 2026-10-06): a leaf crown 8 × its season class (0 = the old unclassed
        /// crown; every value below 128 keeps the foliage light), any other leaf 255 − 8 × class (255 unchanged). See
        /// <see cref="MedievalZen.Environment.SeasonLook.Alpha"/>.
        /// </summary>
        public static byte LeafAlpha(bool leafCrown, int seasonClass) => MedievalZen.Environment.SeasonLook.Alpha(leafCrown, seasonClass);

        /// <summary>True when a vertex alpha marks a leaf crown's face (the far shader's foliage light): every alpha below 128.</summary>
        public static bool IsLeafCrownAlpha(byte alpha) => MedievalZen.Environment.SeasonLook.IsFoliage(alpha);

        /// <summary>
        /// Triangles of a lobed impostor with <paramref name="lobes"/> lobes of <paramref name="sides"/>-sided rings: per lobe two bands of
        /// 2·sides and two caps of sides − 2 (26 for five sides, 20 for four); the trunk 3, and 6 more for the two fork arms.
        /// </summary>
        public static int TrianglesOf(int lobes, int sides, bool forkArms) => lobes * (6 * sides - 4) + (forkArms ? 9 : 3);

        /// <summary>Vertices of the same impostor: three rings per lobe, shared within the lobe (one colour per lobe); 4 per spike.</summary>
        public static int VerticesOf(int lobes, int sides, bool forkArms) => lobes * 3 * sides + (forkArms ? 12 : 4);

        /// <summary>Triangles of the near impostor band's form of <paramref name="style"/> (five-sided lobes, fork arms).</summary>
        public static int NearTriangles(FarTreeStyle style) => TrianglesOf(style.Lobes.Length, NearSides, true);

        /// <summary>Triangles of the <see cref="TileLod.Mid"/> tiles' form (four-sided lobes, no fork arms).</summary>
        public static int MidTriangles(FarTreeStyle style) => TrianglesOf(MidOf(style).Length, MidSides, false);

        /// <summary>Vertices of the <see cref="TileLod.Mid"/> tiles' form.</summary>
        public static int MidVertices(FarTreeStyle style) => VerticesOf(MidOf(style).Length, MidSides, false);

        /// <summary>The lobes the <see cref="TileLod.Mid"/> tiles draw for <paramref name="style"/>.</summary>
        public static TreeLobe[] MidOf(FarTreeStyle style) => style.MidLobes != null && style.MidLobes.Length > 0 ? style.MidLobes : style.Lobes;

        /// <summary>
        /// Fits <paramref name="count"/> lobes to crown vertices (scale 1, pivot at the trunk base): k-means started from the highest
        /// vertex and then repeatedly the vertex furthest from every chosen centre (ties: the lower index), 24 rounds; each lobe's
        /// radii are the <paramref name="percentile"/> of its vertices' |dx|, |dz| and of their heights above / below the centre.
        /// Lobes are ordered by height, then x. Deterministic for the same input order.
        /// </summary>
        public static TreeLobe[] Fit(IReadOnlyList<Vector3> crown, int count, float percentile)
        {
            if (crown == null || crown.Count == 0 || count < 1) return Array.Empty<TreeLobe>();
            count = Math.Min(count, crown.Count);
            int n = crown.Count;
            var cx = new double[count];
            var cy = new double[count];
            var cz = new double[count];
            int first = 0;
            for (int i = 1; i < n; i++) if (crown[i].y > crown[first].y) first = i;
            cx[0] = crown[first].x; cy[0] = crown[first].y; cz[0] = crown[first].z;
            var nearest = new double[n];
            for (int i = 0; i < n; i++) nearest[i] = Dist2(crown[i], cx[0], cy[0], cz[0]);
            for (int c = 1; c < count; c++)
            {
                int far = 0;
                for (int i = 1; i < n; i++) if (nearest[i] > nearest[far]) far = i;
                cx[c] = crown[far].x; cy[c] = crown[far].y; cz[c] = crown[far].z;
                for (int i = 0; i < n; i++) nearest[i] = Math.Min(nearest[i], Dist2(crown[i], cx[c], cy[c], cz[c]));
            }

            var owner = new int[n];
            for (int round = 0; round < 24; round++)
            {
                for (int i = 0; i < n; i++)
                {
                    int best = 0;
                    double bd = double.MaxValue;
                    for (int c = 0; c < count; c++)
                    {
                        double d = Dist2(crown[i], cx[c], cy[c], cz[c]);
                        if (d < bd) { bd = d; best = c; }
                    }

                    owner[i] = best;
                }

                var sx = new double[count];
                var sy = new double[count];
                var sz = new double[count];
                var m = new int[count];
                for (int i = 0; i < n; i++)
                {
                    int c = owner[i];
                    sx[c] += crown[i].x; sy[c] += crown[i].y; sz[c] += crown[i].z; m[c]++;
                }

                for (int c = 0; c < count; c++)
                {
                    if (m[c] == 0) continue;
                    cx[c] = sx[c] / m[c]; cy[c] = sy[c] / m[c]; cz[c] = sz[c] / m[c];
                }
            }

            var lobes = new List<TreeLobe>(count);
            var dx = new List<float>();
            var dz = new List<float>();
            var up = new List<float>();
            var down = new List<float>();
            for (int c = 0; c < count; c++)
            {
                dx.Clear(); dz.Clear(); up.Clear(); down.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (owner[i] != c) continue;
                    dx.Add((float)Math.Abs(crown[i].x - cx[c]));
                    dz.Add((float)Math.Abs(crown[i].z - cz[c]));
                    double dy = crown[i].y - cy[c];
                    if (dy >= 0) up.Add((float)dy); else down.Add((float)-dy);
                }

                if (dx.Count == 0) continue;
                lobes.Add(new TreeLobe((float)cx[c], (float)cy[c], (float)cz[c], Percentile(dx, percentile), Percentile(dz, percentile),
                    Percentile(up, percentile), Percentile(down, percentile)));
            }

            lobes.Sort((a, b) => a.Centre.y != b.Centre.y ? b.Centre.y.CompareTo(a.Centre.y) : a.Centre.x.CompareTo(b.Centre.x));
            return lobes.ToArray();
        }

        /// <summary>
        /// Appends a lobed impostor (scale 1, pivot at the trunk base) as outward-wound, indexed triangles (<paramref name="triangles"/>
        /// holds indices into <paramref name="vertices"/>). Each lobe is three rings of <paramref name="sides"/> (underside, widest, cap;
        /// each twisted half a side from the last, so the bands zig-zag round the lobe) with a flat underside and a flat cap, its vertices
        /// shared and all of one colour: every lobe its own tone (±6 %, by its index) so a tree's yaw turns a different mix toward the
        /// camera. Light and shade come from the far shader's flat normals; a <paramref name="leafCrown"/> also carries the lit-top ladder
        /// (each ring its share, <see cref="LeafTop"/>) and alpha 0 for the shader's foliage light. Every band triangle starts and ends on
        /// the ring whose colour it takes (the flat colour is the provoking vertex's: first on D3D, Vulkan and Metal, last on OpenGL).
        /// Trunk and fork arms in <paramref name="bark"/>.
        /// </summary>
        public static void Build(TreeLobe[] lobes, float forkY, float trunkRadius, Vector3 leaf, Vector3 bark, int sides, bool forkArms,
            List<Vector3> vertices, List<Color32> colors, List<int> triangles, bool leafCrown = false, int seasonClass = 0)
        {
            if (lobes == null) return;
            sides = Math.Max(3, sides);
            Color32 barkC = FarTileMeshData.EncodeSrgbFast(bark);
            float tr = Math.Max(0.12f, trunkRadius);
            var fork = new Vector3(0f, Math.Max(0.5f, forkY), 0f);
            Spike(Vector3.zero, fork, tr, 0.5f, barkC, vertices, colors, triangles);
            if (forkArms)
            {
                // Two fork arms toward the two largest lobes, ending 70 % of the way to their centres.
                int a = -1, b = -1;
                for (int i = 0; i < lobes.Length; i++)
                {
                    float s = Size(lobes[i]);
                    if (a < 0 || s > Size(lobes[a])) { b = a; a = i; }
                    else if (b < 0 || s > Size(lobes[b])) b = i;
                }

                if (b < 0) b = a;
                Spike(fork, Vector3.Lerp(fork, lobes[a].Centre, 0.7f), tr * 0.6f, 1.1f, barkC, vertices, colors, triangles);
                Spike(fork, Vector3.Lerp(fork, lobes[b].Centre, 0.7f), tr * 0.6f, 2.3f, barkC, vertices, colors, triangles);
            }

            float fit = sides == 4 ? RingFit4 : sides == 5 ? RingFit5 : 1f / (float)Math.Cos(Math.PI / sides);
            for (int i = 0; i < lobes.Length; i++)
            {
                TreeLobe l = lobes[i];
                float tone = 0.94f + 0.12f * (((uint)(i * 0x9E3779B1u) >> 8) & 0xFFFF) / 65535f;
                Color32 col = FarTileMeshData.EncodeSrgbFast(leaf * tone);
                col.a = LeafAlpha(false, seasonClass);
                Color32 underC = col, widestC = col, capC = col;
                if (leafCrown)
                {
                    byte alpha = LeafAlpha(true, seasonClass);
                    underC = LeafFace(leaf * tone, LeafUnder, alpha);
                    widestC = LeafFace(leaf * tone, LeafMid, alpha);
                    capC = LeafFace(leaf * tone, LeafTop, alpha);
                }

                float shift = 0.37f * i, rx = l.RadiusX * fit, rz = l.RadiusZ * fit;
                float up = Math.Max(0.2f, l.Up), down = Math.Max(0.2f, l.Down);
                int under = AddRing(l.Centre - new Vector3(0f, down, 0f), rx * UndersideRadius, rz * UndersideRadius, shift + 0.5f, sides, underC, vertices, colors);
                int widest = AddRing(l.Centre, rx, rz, shift, sides, widestC, vertices, colors);
                int cap = AddRing(l.Centre + new Vector3(0f, up, 0f), rx * CapRadius, rz * CapRadius, shift + 0.5f, sides, capC, vertices, colors);
                // The rings alternate half a side, so ring A's edge k faces ring B's vertex k: two zig-zag bands. Each triangle takes the
                // colour of the ring holding its edge (lower band: widest / underside, upper band: widest / cap).
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    RingTri(widest + k, widest + k1, under + k, l.Centre, vertices, triangles);
                    RingTri(under + k, under + k1, widest + k1, l.Centre, vertices, triangles);
                    RingTri(widest + k, widest + k1, cap + k, l.Centre, vertices, triangles);
                    RingTri(cap + k, cap + k1, widest + k1, l.Centre, vertices, triangles);
                }

                // Flat underside and flat cap: fans from each ring's first vertex.
                for (int k = 1; k < sides - 1; k++)
                {
                    Tri(under, under + k, under + k + 1, l.Centre, vertices, triangles);
                    Tri(cap, cap + k, cap + k + 1, l.Centre, vertices, triangles);
                }
            }
        }

        /// <summary>Bounds of the lobes (scale 1): widest horizontal reach from the trunk axis and the top.</summary>
        public static void Extent(TreeLobe[] lobes, out float reach, out float top, out float bottom)
        {
            reach = 0f; top = 0f; bottom = float.MaxValue;
            if (lobes == null || lobes.Length == 0) { bottom = 0f; return; }
            foreach (TreeLobe l in lobes)
            {
                float r = (float)Math.Sqrt(l.Centre.x * l.Centre.x + l.Centre.z * l.Centre.z) + Math.Max(l.RadiusX, l.RadiusZ);
                reach = Math.Max(reach, r);
                top = Math.Max(top, l.Centre.y + l.Up);
                bottom = Math.Min(bottom, l.Centre.y - l.Down);
            }
        }

        private static float Size(TreeLobe l) => l.RadiusX * l.RadiusZ * (l.Up + l.Down);

        private static double Dist2(Vector3 p, double x, double y, double z)
        {
            double a = p.x - x, b = p.y - y, c = p.z - z;
            return a * a + b * b + c * c;
        }

        private static float Percentile(List<float> values, float q)
        {
            if (values.Count == 0) return 0.2f;
            values.Sort();
            int i = (int)Math.Round(Math.Max(0f, Math.Min(1f, q)) * (values.Count - 1));
            return Math.Max(0.2f, values[i]);
        }

        private static int AddRing(Vector3 centre, float rx, float rz, float shift, int sides, Color32 color, List<Vector3> v, List<Color32> c)
        {
            int first = v.Count;
            for (int k = 0; k < sides; k++)
            {
                double a = (k + shift) * 2.0 * Math.PI / sides;
                v.Add(centre + new Vector3((float)Math.Sin(a) * rx, 0f, (float)Math.Cos(a) * rz));
                c.Add(color);
            }

            return first;
        }

        /// <summary>A three-sided spike from a ring of radius <paramref name="radius"/> round <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static void Spike(Vector3 from, Vector3 to, float radius, float shift, Color32 color, List<Vector3> v, List<Color32> c, List<int> t)
        {
            Vector3 axis = to - from;
            Vector3 side = Math.Abs(axis.normalized.y) > 0.9f ? Vector3.right : Vector3.Cross(axis, Vector3.up).normalized;
            Vector3 other = Vector3.Cross(axis.normalized, side).normalized;
            int first = v.Count;
            for (int k = 0; k < 3; k++)
            {
                double a = shift + k * 2.0 * Math.PI / 3.0;
                v.Add(from + side * ((float)Math.Cos(a) * radius) + other * ((float)Math.Sin(a) * radius));
                c.Add(color);
            }

            int tip = v.Count;
            v.Add(to);
            c.Add(color);
            Vector3 mid = from + axis * 0.3f;
            for (int k = 0; k < 3; k++) Tri(first + k, first + (k + 1) % 3, tip, mid, v, t);
        }

        /// <summary>
        /// One indexed triangle wound to face away from <paramref name="inside"/>: Unity's front faces are clockwise seen from the
        /// camera, which is cross(b − a, c − a) pointing toward the viewer, i.e. outward.
        /// </summary>
        /// <summary>
        /// One triangle with edge <paramref name="p"/>–<paramref name="q"/> (one ring) and apex <paramref name="apex"/> (the next ring),
        /// wound to face away from <paramref name="inside"/> and written p/q, apex, q/p: its first and last index lie on the edge's ring,
        /// so either provoking-vertex convention gives the triangle that ring's colour.
        /// </summary>
        private static void RingTri(int p, int q, int apex, Vector3 inside, List<Vector3> v, List<int> t)
        {
            Vector3 pp = v[p], n = Vector3.Cross(v[apex] - pp, v[q] - pp);
            Vector3 o = (pp + v[q] + v[apex]) / 3f - inside;
            bool outward = n.x * o.x + n.y * o.y + n.z * o.z >= 0f;
            t.Add(outward ? p : q);
            t.Add(apex);
            t.Add(outward ? q : p);
        }

        private static void Tri(int a, int b, int cc, Vector3 inside, List<Vector3> v, List<int> t)
        {
            Vector3 pa = v[a], n = Vector3.Cross(v[b] - pa, v[cc] - pa);
            Vector3 o = (pa + v[b] + v[cc]) / 3f - inside;
            bool flip = n.x * o.x + n.y * o.y + n.z * o.z < 0f;
            t.Add(a);
            t.Add(flip ? cc : b);
            t.Add(flip ? b : cc);
        }
    }
}
