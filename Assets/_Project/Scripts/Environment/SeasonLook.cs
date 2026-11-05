using System;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// The seasonal palette (2026-10-06, seasons pass, colours half): one figure per season for each colour that changes with the
    /// year, read at <see cref="Season.Year"/> through <see cref="Season.Blend(float, Vector3, Vector3, Vector3)"/>. Pure data and
    /// arithmetic (no native Unity calls), tested offline. <b>Autumn is the identity</b>: at <see cref="Season.Identity"/> every factor
    /// is exactly 1, every colour exactly today's.
    /// <para>
    /// Every figure is a per-channel factor (linear RGB) on today's colour, so a near colour and a distant colour that agree today
    /// stay as close in every season (the far tiles' baked colours take the same factor through <c>FarTerrain.shader</c>'s season tints).
    /// Ground: lush and dry grass, the flower meadow, heath and the woods' leaf litter (rock, scree, dirt, cobbles, mud, the forest
    /// floor and the field channels do not change here; crops by season are a later job). Leaves: the PNB Meadow oak, birch and
    /// fruit tree (the PolygonGeneric broadleaves, pines and dead trees keep their look: the Generic trees share one atlas material
    /// between leaves and bark, so their near leaves cannot be recoloured alone).
    /// </para>
    /// </summary>
    public static class SeasonLook
    {
        // ------------------------------------------------------------------ classes (far shader)

        /// <summary>Season classes of the far tiles' faces: which tint of <c>FarTerrain.shader</c>'s <c>_MZSeasonTints</c> a face takes (0 = none).</summary>
        public const int ClassNone = 0, ClassOak = 1, ClassBirch = 2, ClassFruit = 3, ClassLush = 4, ClassDry = 5, ClassFlower = 6,
            ClassHeath = 7, ClassWoodFloor = 8, ClassPasture = 9;

        /// <summary>Number of tint slots (the shader's array length).</summary>
        public const int ClassCount = 16;

        /// <summary>Alpha step between classes (a vertex alpha survives 8-bit storage and half-precision decoding to well within half a step).</summary>
        public const int ClassStep = 8;

        /// <summary>
        /// Vertex alpha of a face: a leaf crown (the far shader's foliage light) of class <paramref name="cls"/> is 8·cls (0..120; 0 is
        /// the old unclassed crown), any other face 255 − 8·cls (255..135; 255 is the old unclassed face).
        /// </summary>
        public static byte Alpha(bool foliage, int cls)
        {
            cls = cls < 0 ? 0 : cls >= ClassCount ? ClassCount - 1 : cls;
            return foliage ? (byte)(ClassStep * cls) : (byte)(255 - ClassStep * cls);
        }

        /// <summary>True when a vertex alpha marks a leaf crown (the far shader: alpha · 255 &lt; 127.5).</summary>
        public static bool IsFoliage(byte alpha) => alpha < 128;

        /// <summary>The season class a vertex alpha carries (as the far shader decodes it).</summary>
        public static int ClassOf(byte alpha)
        {
            int k = alpha < 128 ? alpha : 255 - alpha;
            int cls = (k + ClassStep / 2) / ClassStep;
            return cls >= ClassCount ? ClassCount - 1 : cls;
        }

        // ------------------------------------------------------------------ ground

        /// <summary>
        /// Factor on a terrain channel's colour (its layer tint, <c>TerrainLayer.diffuseRemapMax</c>) at <paramref name="year"/>;
        /// exactly 1 at the identity and for every channel that does not change. Today's colours (sRGB) and the season targets:
        /// lush grass (110, 130, 24): spring young yellow-green, summer deeper; dry grass (126, 132, 47): spring greener, summer
        /// strawier; flower meadow (121, 129, 28): spring lighter and yellower (more flowers), summer a touch deeper; heath (80, 82,
        /// 54): spring greener, summer drier; leaf litter (99, 79, 42): spring and summer moss-green, the fallen leaves thinner.
        /// </summary>
        public static Vector3 LayerFactor(int channel, float year)
        {
            if (Season.Wrap(year) == Season.Identity) return Vector3.one;
            switch (channel)
            {
                case TerrainShaper.LushGrassLayer: return Factor(year, LushSpring, LushSummer);
                case TerrainShaper.DryGrassLayer: return Factor(year, DrySpring, DrySummer);
                case TerrainShaper.FlowerLayer: return Factor(year, FlowerSpring, FlowerSummer);
                case TerrainShaper.HeathLayer: return Factor(year, HeathSpring, HeathSummer);
                case TerrainShaper.LeafLitterLayer: return Factor(year, LitterSpring, LitterSummer);
                default: return Vector3.one;
            }
        }

        /// <summary>True for the channels whose colour changes with the season (their near layers are run-time copies).</summary>
        public static bool IsSeasonalLayer(int channel) => channel == TerrainShaper.LushGrassLayer || channel == TerrainShaper.DryGrassLayer ||
            channel == TerrainShaper.FlowerLayer || channel == TerrainShaper.HeathLayer || channel == TerrainShaper.LeafLitterLayer;

        /// <summary>A channel's distant colour (linear) at <paramref name="year"/>: <see cref="GroundLook.LayerColor"/> times <see cref="LayerFactor"/>.</summary>
        public static Vector3 LayerColor(int channel, float year) => Vector3.Scale(GroundLook.LayerColor(channel), LayerFactor(channel, year));

        // Ground factors (linear), spring and summer; autumn is 1.
        internal static readonly Vector3 LushSpring = new Vector3(1.25f, 1.26f, 1.30f), LushSummer = new Vector3(0.86f, 0.92f, 1.00f);
        internal static readonly Vector3 DrySpring = new Vector3(0.90f, 1.12f, 0.90f), DrySummer = new Vector3(1.12f, 1.02f, 1.08f);
        internal static readonly Vector3 FlowerSpring = new Vector3(1.24f, 1.18f, 1.50f), FlowerSummer = new Vector3(0.95f, 0.96f, 1.05f);
        internal static readonly Vector3 HeathSpring = new Vector3(0.96f, 1.12f, 0.95f), HeathSummer = new Vector3(1.10f, 1.02f, 0.95f);
        internal static readonly Vector3 LitterSpring = new Vector3(0.60f, 1.08f, 0.90f), LitterSummer = new Vector3(0.66f, 1.00f, 0.90f);

        /// <summary>
        /// Factor on a sward detail family's leaf colours (the grass clumps' first-party materials) at <paramref name="year"/>: tall
        /// and medium clumps follow the lush grass, short clumps (the dry ground's cover) the dry grass; other families 1.
        /// </summary>
        public static Vector3 SwardFactor(string family, float year)
        {
            switch (family)
            {
                case PropRole.TallGrass:
                case PropRole.MedGrass: return LayerFactor(TerrainShaper.LushGrassLayer, year);
                case PropRole.ShortGrass: return LayerFactor(TerrainShaper.DryGrassLayer, year);
                default: return Vector3.one;
            }
        }

        // ------------------------------------------------------------------ leaves

        /// <summary>The season class of a tree prefab's leaves by name (a palette copy containing the Synty name counts).</summary>
        public static int LeafClassOfTree(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return ClassNone;
            if (prefabName.IndexOf("SM_Env_Tree_Birch_", StringComparison.OrdinalIgnoreCase) >= 0) return ClassBirch;
            if (prefabName.IndexOf("SM_Env_Tree_Meadow_", StringComparison.OrdinalIgnoreCase) >= 0) return ClassOak;
            if (prefabName.IndexOf("SM_Env_Tree_Fruit_", StringComparison.OrdinalIgnoreCase) >= 0) return ClassFruit;
            return ClassNone;
        }

        /// <summary>
        /// The season class of a Synty leaf material by name (PNB Meadow Forest): Tree_Birch_Mat_01 (birches), Tree_Mat_03 (Meadow oaks,
        /// and the Bush_03 that shares it), Tree_Mat_01 (fruit trees, and Bush_01/02). Branch materials and everything else: none.
        /// </summary>
        public static int LeafClassOfMaterial(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return ClassNone;
            if (materialName.StartsWith("Tree_Birch_Mat_01", StringComparison.Ordinal)) return ClassBirch;
            if (materialName.StartsWith("Tree_Mat_03", StringComparison.Ordinal)) return ClassOak;
            if (materialName.StartsWith("Tree_Mat_01", StringComparison.Ordinal) && !materialName.StartsWith("Tree_Mat_01_Small", StringComparison.Ordinal)) return ClassFruit;
            return ClassNone;
        }

        /// <summary>
        /// True when the class's near leaves take their colour from the material's flat colours alone (Synty foliage graph,
        /// <c>_Leaf_Flat_Color</c> 1: birch and fruit), false when the colours tint the leaf texture (the oak, flat colour 0).
        /// </summary>
        public static bool FlatLeaves(int cls) => cls == ClassBirch || cls == ClassFruit;

        /// <summary>
        /// Today's mean near leaf albedo of a class (linear; FarTreeShapes' measurement of the foliage graph over 40,000 positions, the
        /// mean of its 10th/90th percentile crowns): birch (0.479, 0.179, 0.013), Meadow oak (0.045, 0.054, 0.003), fruit (0.164, 0.248, 0.001).
        /// </summary>
        public static Vector3 NearLeafMean(int cls)
        {
            switch (cls)
            {
                case ClassBirch: return new Vector3(0.479f, 0.1785f, 0.013f);
                case ClassOak: return new Vector3(0.045f, 0.0535f, 0.003f);
                case ClassFruit: return new Vector3(0.164f, 0.2475f, 0.001f);
                default: return Vector3.zero;
            }
        }

        /// <summary>
        /// Factor on a leaf class's colour at <paramref name="year"/> (near mean and distant crowns alike); 1 at the identity and for
        /// classes that do not change. Oak: spring fresh light green, summer deep green. Birch: green in spring and summer (its
        /// autumn orange is today's). Fruit: spring pale, blossom-touched green; summer green.
        /// </summary>
        public static Vector3 LeafFactor(int cls, float year)
        {
            if (Season.Wrap(year) == Season.Identity) return Vector3.one;
            switch (cls)
            {
                case ClassOak: return Factor(year, OakSpring, OakSummer);
                case ClassBirch: return Factor(year, BirchSpring, BirchSummer);
                case ClassFruit: return Factor(year, FruitSpring, FruitSummer);
                default: return Vector3.one;
            }
        }

        // Leaf factors (linear), spring and summer; autumn is 1.
        internal static readonly Vector3 OakSpring = new Vector3(1.60f, 2.15f, 1.30f), OakSummer = new Vector3(0.85f, 1.15f, 1.00f);
        internal static readonly Vector3 BirchSpring = new Vector3(0.40f, 1.70f, 1.40f), BirchSummer = new Vector3(0.28f, 1.30f, 1.20f);
        internal static readonly Vector3 FruitSpring = new Vector3(1.40f, 1.22f, 1.60f), FruitSummer = new Vector3(0.95f, 1.05f, 1.00f);

        /// <summary>
        /// The near mean leaf albedo of a class at <paramref name="year"/> (linear): today's mean times <see cref="LeafFactor"/>, except the
        /// fruit trees' spring, whose flat leaf colours take <see cref="FruitNearSpring"/> (a factor cannot give blossom white to leaves with
        /// no blue at all; the distant crowns, already greyer, take the factor).
        /// </summary>
        public static Vector3 NearLeaf(int cls, float year)
        {
            Vector3 mean = NearLeafMean(cls);
            if (Season.Wrap(year) == Season.Identity || cls == ClassNone) return mean;
            return Season.Blend(year, NearTarget(cls, true), NearTarget(cls, false), mean);
        }

        /// <summary>Fruit trees' spring leaves near by: pale, blossom-touched green, sRGB (140, 151, 60).</summary>
        internal static readonly Vector3 FruitNearSpring = new Vector3(0.2623f, 0.3095f, 0.0452f);

        private static Vector3 NearTarget(int cls, bool spring)
        {
            if (spring && cls == ClassFruit) return FruitNearSpring;
            Vector3 mean = NearLeafMean(cls);
            switch (cls)
            {
                case ClassOak: return Vector3.Scale(mean, spring ? OakSpring : OakSummer);
                case ClassBirch: return Vector3.Scale(mean, spring ? BirchSpring : BirchSummer);
                case ClassFruit: return Vector3.Scale(mean, FruitSummer);
                default: return mean;
            }
        }

        /// <summary>
        /// One of a leaf material's colours (<c>_Leaf_Base_Color</c>, <c>_Leaf_Noise_Color</c>, <c>_Leaf_Noise_Large_Color</c>; linear) at
        /// <paramref name="year"/>; exactly <paramref name="today"/> at the identity. A textured class (oak) multiplies it by
        /// <see cref="LeafFactor"/>. A flat class (birch, fruit) is recoloured: each colour takes the season's mean hue at its own share
        /// of today's mean luminance, blended with today's colour by the season weights. The graph mixes the three colours with weights
        /// that sum to 1, so the leaves' mean lands on <see cref="NearLeaf"/> while the light and dark of the colour noise is kept; the
        /// orange-to-yellow hue swing of today's birches becomes one green hue in spring and summer.
        /// </summary>
        public static Vector3 LeafColour(int cls, Vector3 today, float year)
        {
            if (Season.Wrap(year) == Season.Identity || cls == ClassNone) return today;
            if (!FlatLeaves(cls)) return Vector3.Scale(today, LeafFactor(cls, year));
            Vector3 mean = NearLeafMean(cls);
            float share = Luminance(today) / Math.Max(1e-5f, Luminance(mean));
            Vector3 spring = NearTarget(cls, true) * share;
            Vector3 summer = NearTarget(cls, false) * share;
            return Season.Blend(year, spring, summer, today);
        }

        /// <summary>Relative luminance of a linear RGB colour (Rec. 709).</summary>
        public static float Luminance(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;

        // ------------------------------------------------------------------ far tiles

        /// <summary>
        /// The far shader's tint of a class at <paramref name="year"/> (linear factor on the baked colour): leaf classes their
        /// <see cref="LeafFactor"/>, ground classes their channel's <see cref="LayerFactor"/>, the wood floor and pasture the ratio of
        /// their mixed colours. Exactly 1 at the identity and for unused classes.
        /// </summary>
        public static Vector3 ClassTint(int cls, float year)
        {
            if (Season.Wrap(year) == Season.Identity) return Vector3.one;
            switch (cls)
            {
                case ClassOak:
                case ClassBirch:
                case ClassFruit: return LeafFactor(cls, year);
                case ClassLush: return LayerFactor(TerrainShaper.LushGrassLayer, year);
                case ClassDry: return LayerFactor(TerrainShaper.DryGrassLayer, year);
                case ClassFlower: return LayerFactor(TerrainShaper.FlowerLayer, year);
                case ClassHeath: return LayerFactor(TerrainShaper.HeathLayer, year);
                case ClassWoodFloor:
                {
                    Vector4 s = GroundLook.WoodFloorBlend(0.5f, GroundLook.WoodCanopyMean);
                    Vector3 today = GroundLook.WoodFloorMixColor(s);
                    Vector3 now = GroundLook.LayerColor(TerrainShaper.ForestFloorLayer) * s.x + GroundLook.LayerColor(TerrainShaper.MudLayer) * s.y +
                                  GroundLook.LayerColor(TerrainShaper.DirtLayer) * s.z + LayerColor(TerrainShaper.LeafLitterLayer, year) * s.w;
                    return Ratio(now, today);
                }
                case ClassPasture:
                {
                    float[] mix = GroundLook.FieldShares[GroundLook.FieldPasture];
                    Vector3 today = Vector3.zero, now = Vector3.zero;
                    for (int c = 0; c < mix.Length; c++)
                    {
                        if (mix[c] == 0f) continue;
                        today += GroundLook.LayerColor(c) * mix[c];
                        now += LayerColor(c, year) * mix[c];
                    }

                    return Ratio(now, today);
                }
                default: return Vector3.one;
            }
        }

        /// <summary>Fills <paramref name="tints"/> (length <see cref="ClassCount"/>) with every class's tint at <paramref name="year"/>, w = 1.</summary>
        public static void ClassTints(float year, Vector4[] tints)
        {
            for (int c = 0; c < tints.Length && c < ClassCount; c++)
            {
                Vector3 t = ClassTint(c, year);
                tints[c] = new Vector4(t.x, t.y, t.z, 1f);
            }
        }

        /// <summary>
        /// The season class of a distant ground face from its zone (<c>FarTileMeshData</c>: a channel, or the canopy shade) and its
        /// matched field look (−1 for none): lush and the flower meadow, dry grass, heath, the wood floor (forest floor zone and canopy
        /// shade), pasture and balk. Every other zone and field look: none (crops by season are a later job).
        /// </summary>
        public static int GroundClass(int zone, int fieldLook, bool canopy)
        {
            if (canopy) return ClassWoodFloor;
            if (fieldLook >= 0) return fieldLook == GroundLook.FieldPasture || fieldLook == GroundLook.FieldBalk ? ClassPasture : ClassNone;
            switch (zone)
            {
                case TerrainShaper.LushGrassLayer: return ClassLush;
                case TerrainShaper.DryGrassLayer: return ClassDry;
                case TerrainShaper.FlowerLayer: return ClassFlower;
                case TerrainShaper.HeathLayer: return ClassHeath;
                case TerrainShaper.ForestFloorLayer:
                case TerrainShaper.LeafLitterLayer: return ClassWoodFloor;
                default: return ClassNone;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 Factor(float year, Vector3 spring, Vector3 summer) => Season.Blend(year, spring, summer, Vector3.one);

        private static Vector3 Ratio(Vector3 a, Vector3 b) =>
            new Vector3(b.x > 1e-6f ? a.x / b.x : 1f, b.y > 1e-6f ? a.y / b.y : 1f, b.z > 1e-6f ? a.z / b.z : 1f);
    }

    /// <summary>
    /// When a seasonal colour system repaints (pure, tested): once at the start, then only when <see cref="Season.Revision"/> has
    /// moved or something new was registered. Every other frame costs one comparison.
    /// </summary>
    public sealed class SeasonGate
    {
        private int appliedRevision = int.MinValue;
        private int appliedRegistrations = -1;

        /// <summary>True when the painter must apply now (and records that it did).</summary>
        public bool Due(int revision, int registrations)
        {
            if (revision == appliedRevision && registrations == appliedRegistrations) return false;
            appliedRevision = revision;
            appliedRegistrations = registrations;
            return true;
        }

        /// <summary>Forgets what was applied (the next <see cref="Due"/> is true).</summary>
        public void Reset()
        {
            appliedRevision = int.MinValue;
            appliedRegistrations = -1;
        }
    }
}
