using System.Collections.Generic;

namespace MedievalZen.World
{
    /// <summary>
    /// Default contents of the <see cref="WorldPalette"/>: for every family, the asset paths of its Synty
    /// POLYGON Nature Biomes (Meadow) prefabs. The editor menu <c>MedievalZen/Create Default World Palette</c>
    /// loads these paths; <see cref="PaletteKeys.Default"/> uses the counts so pure tests match the real palette.
    /// Off-theme prefabs (Warpgate, SciFi*, Steam_Car, Sword_Stone, MushroomHouse) are deliberately absent.
    /// </summary>
    public static class SyntyCatalog
    {
        /// <summary>Root of the Meadow pack.</summary>
        public const string Root = "Assets/Synty/PolygonNatureBiomes/PNB_Meadow_Forest/";

        /// <summary>Where generated helper prefabs (single-mesh details, rapeseed) are saved.</summary>
        public const string GeneratedPrefabFolder = "Assets/_Project/Prefabs/Synty";

        private const string Env = Root + "Prefabs/SM_Env_";
        private const string EnvProp = Root + "Prefabs/SM_Prop_";
        private const string Prop = Root + "Prefabs/Props/SM_Prop_";
        private const string Bld = Root + "Prefabs/Props/SM_Bld_";
        private const string GenEnv = "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_";
        private const string GenProp = "Assets/Synty/PolygonGeneric/Prefabs/Props/SM_Gen_Prop_";

        /// <summary>Water plane placed at lake level.</summary>
        public const string WaterPlane = Root + "Prefabs/SM_Env_Water_Plane_01.prefab";

        /// <summary>Fallback water plane in the core pack.</summary>
        public const string WaterPlaneFallback = "Assets/Synty/PolygonNatureBiomes/PNB_Core/Prefabs/SM_Env_Water_Plane_01.prefab";

        /// <summary>Background hill model (ships without materials).</summary>
        public const string BackgroundHill = Root + "Models/SM_Env_Background_Hill_01.fbx";

        /// <summary>Material for the background hills.</summary>
        public const string BackgroundHillMaterial = Root + "Materials/Grass_Background_Mat_01.mat";

        /// <summary>Rapeseed models (no prefab in the pack; the palette menu wraps them into prefabs).</summary>
        public static readonly string[] RapeseedModels = { Root + "Models/SM_Env_Rapeseed_Clump_01.fbx", Root + "Models/SM_Env_Rapeseed_Clump_02.fbx" };

        /// <summary>Material given to the rapeseed prefabs (the models import without materials).</summary>
        public const string RapeseedMaterial = Root + "Materials/Plants/Flowers_Field_Mat_01.mat";

        /// <summary>Blade rotation controllers shipped with the pack.</summary>
        public const string RotateXController = Root + "FX/Animations/RotateX.controller";

        /// <summary>Blade rotation controller around local Z.</summary>
        public const string RotateZController = Root + "FX/Animations/RotateZ.controller";

        /// <summary>Folder of the terrain textures and layer assets.</summary>
        public const string TerrainFolder = Root + "Terrain/";

        /// <summary>Prefab families (GameObjects and terrain trees).</summary>
        public static readonly IReadOnlyList<(string family, string[] paths)> Families = new List<(string, string[])>
        {
            (PropRole.ForestTree, Range(Env + "Tree_Birch_", 1, 3).Concat(Range(Env + "Tree_Meadow_", 1, 2)).Concat(Range(GenEnv + "Tree_", 1, 3))),
            (PropRole.PineTree, Range(GenEnv + "Tree_Pine_", 1, 3)),
            (PropRole.DeadTree, Range(GenEnv + "Tree_Dead_", 1, 3)),
            (PropRole.MeadowTree, Range(Env + "Tree_Meadow_", 1, 2).Concat(new[] { Env + "Tree_Birch_02.prefab" })),
            (PropRole.FruitTree, Range(Env + "Tree_Fruit_", 1, 3)),
            (PropRole.Bush, Range(Env + "Bush_", 1, 3).Concat(new[] { Env + "Grass_Bush_01.prefab" }).Concat(Range(GenEnv + "Bush_", 1, 4)).Concat(Range(GenEnv + "Bush_Large_", 1, 4))),
            (PropRole.Shrub, Range(GenEnv + "Shrub_", 1, 3).Concat(Range(GenEnv + "Fern_", 1, 3))),
            (PropRole.Stump, Range(GenEnv + "Stump_", 1, 3).Concat(Range(GenEnv + "Log_", 1, 2)).Concat(Range(GenEnv + "Root_", 1, 2))),
            (PropRole.Twig, Range(GenEnv + "Twig_", 1, 4)),
            (PropRole.Rock, Range(Env + "Rock_", 1, 6).Concat(new[] { Env + "Rock_Round_01.prefab", Env + "Rock_Small_01.prefab" }).Concat(Range(GenEnv + "Rock_", 1, 10))),
            (PropRole.RockSmallPile, Range(Env + "Rock_Small_Pile_", 1, 2)),
            (PropRole.RockPile, Range(Env + "Rock_Pile_", 1, 7)),
            (PropRole.RockGround, Range(Env + "Rock_Ground_", 1, 2)),
            (PropRole.Cliff, Range(Env + "Rock_Cliff_", 1, 3).Concat(Range(GenEnv + "Cliff_", 1, 4))),
            (PropRole.DirtCliff, Range(GenEnv + "Dirt_Cliff_", 1, 8)),
            (PropRole.GroundCliff, Range(Env + "Ground_Cliff_Large_", 1, 2)),
            (PropRole.Mound, Range(Env + "Ground_Mound_Large_", 1, 4)),
            (PropRole.Mushroom, Range(Prop + "Mushroom_", 1, 6).Concat(Range(Prop + "Mushroom_Sparse_", 1, 5))),
            (PropRole.MushroomGroup, Range(Prop + "Mushroom_Group_", 2, 5).Concat(Range(GenEnv + "Mushroom_", 1, 3))),
            (PropRole.Leaves, Range(EnvProp + "Leaves_Pile_", 1, 2).Concat(new[] { EnvProp + "Leaves_Branch_01.prefab" })),
            (PropRole.Lilies, Range(Env + "Lillies_", 1, 3).Concat(Range(GenEnv + "Lilypads_", 1, 3))),
            (PropRole.StoneStack, Range(Prop + "StoneStack_", 1, 2)),
            (PropRole.StonePile, Range(Prop + "StonePile_", 1, 3)),
            (PropRole.StoneRunes, Range(Prop + "StoneRunes_", 1, 3)),
            (PropRole.CropField, Range(Env + "CropField_Clump_", 1, 2)),
            (PropRole.Rapeseed, Range(GeneratedPrefabFolder + "/SM_Env_Rapeseed_Clump_", 1, 2)),
            (PropRole.Sunflower, new[] { Env + "Sunflower_01.prefab" }),
            (PropRole.Scarecrow, new[] { Prop + "ScareCrow_01.prefab" }),
            (PropRole.Fence, Range(Prop + "Meadow_Fence_", 1, 7)),
            (PropRole.FencePost, Range(Prop + "Meadow_Fence_Post_", 1, 3)),
            (PropRole.FenceGate, Range(Prop + "Meadow_Fence_Gate_", 1, 2)),
            (PropRole.Cabin, new[] { Bld + "Stone_Cabin_01.prefab" }),
            (PropRole.Windmill, Range(Bld + "Windmill_", 1, 2)),
            (PropRole.Well, new[] { Prop + "Well_01.prefab" }),
            (PropRole.Sign, Range(Prop + "Sign_", 1, 3)),
            (PropRole.HorseHitch, new[] { Prop + "HorseHitch_01.prefab" }),
            (PropRole.Fireplace, Range(Prop + "Camp_Fireplace_", 1, 2).Concat(new[] { Prop + "Camp_Fireplace_Stones_01.prefab" })),
            (PropRole.FireTripod, new[] { Prop + "Camp_Fire_Tripod_01.prefab" }),
            (PropRole.Tent, new[] { Prop + "Camp_Tent_01.prefab" }),
            (PropRole.TanningRack, new[] { Prop + "Camp_Tanning_Rack_01.prefab" }),
            (PropRole.CampItem, Range(Prop + "Camp_Bucket_", 1, 2).Concat(Range(Prop + "Camp_Can_", 1, 2)).Concat(new[] { Prop + "Camp_Pouch_01.prefab" })),
            (PropRole.Crate, new[] { Prop + "Camp_Crate_01.prefab" }),
            (PropRole.Cart, new[] { Prop + "HandCart_01.prefab", Prop + "Wagon_Broken_01.prefab" }),
            (PropRole.StoneArch, new[] { Prop + "StoneArch_01.prefab" }),
            (PropRole.StoneWall, new[] { EnvProp + "Stonewall_Long_01.prefab" }),
            (PropRole.StoneWallSmall, Range(EnvProp + "Stonewall_Small_", 1, 2)),
            (PropRole.StoneWallEnd, new[] { EnvProp + "Stonewall_End_01.prefab" }),
            (PropRole.StoneWallPillar, Range(EnvProp + "Stonewall_Pillar_", 1, 2)),
            (PropRole.Gate, new[] { Prop + "Gate_Small_01.prefab" }),
            (PropRole.Birdhouse, Range(Prop + "Birdhouse_", 1, 2)),
            (PropRole.WindChime, Range(Prop + "WindChime_", 1, 2)),
            (PropRole.Kite, Range(Prop + "Kite_", 1, 3)),
            (PropRole.WaterWheel, new[] { Prop + "WaterWheel_01.prefab" }),
            (PropRole.Goods, Range(GenProp + "Barrel_Wood_", 1, 3).Concat(Range(GenProp + "Crate_", 1, 3)).Concat(Range(GenProp + "Sack_", 1, 5)).Concat(Range(GenProp + "Sack_Stack_", 1, 2)).Concat(Range(GenProp + "Pot_", 1, 5))),
            (PropRole.Chest, Range(GenProp + "Chest_", 1, 2)),
            (PropRole.Table, new[] { GenProp + "Table_01.prefab" }),
            (PropRole.Chair, new[] { GenProp + "Chair_01.prefab" }),
            (PropRole.HorizonHill, new[] { BackgroundHill }.Concat(Range(GenEnv + "Hill_", 1, 5))),
            (PropRole.HorizonMountain, Range(GenEnv + "Background_Mountain_", 1, 3)),
        };

        /// <summary>
        /// Terrain detail families. The pack's grass/flower prefabs are LOD groups, which terrain details cannot
        /// use, so the palette menu saves a single-mesh copy (LOD0) of each into <see cref="GeneratedPrefabFolder"/>/Details.
        /// Only compact clumps belong here: a detail is a rigid mesh placed at one terrain point, so wide sculpted
        /// patches cannot follow the ground and float / sink at their edges. Hence excluded: <c>Grass_Large_01..04</c>
        /// (≈ 34 m patches with 3-4 m of built-in relief), <c>Wildflowers_Patch_01..03</c> (≈ 18 m patches) and
        /// <c>Grass_Tall_Clump_05</c> (≈ 7 m patch whose base dips 40 cm). Sizes are normalised per family by
        /// <see cref="DetailPlanner.TargetHeight"/>.
        /// </summary>
        public static readonly IReadOnlyList<(string family, string[] paths)> Details = new List<(string, string[])>
        {
            (PropRole.TallGrass, Range(Env + "Grass_Tall_Clump_", 1, 4)),
            (PropRole.MedGrass, Range(Env + "Grass_Med_Clump_", 1, 3)),
            (PropRole.ShortGrass, Range(Env + "Grass_Short_Clump_", 1, 3)),
            (PropRole.Wildflowers, Range(Env + "Wildflowers_", 1, 3)),
            (PropRole.FlatFlowers, Range(Env + "Flowers_Flat_", 1, 3)),
            (PropRole.GroundCover, Range(Env + "Ground_Cover_", 1, 3)),
            (PropRole.SunflowerDetail, new[] { Env + "Sunflower_01.prefab" }),
            (PropRole.GenFlowers, Range(GenEnv + "Flowers_", 1, 8)),
        };

        /// <summary>Terrain layer assets shipped with the pack, in splat order (see <see cref="TerrainShaper"/> layer constants).</summary>
        public static readonly string[] TerrainLayers =
        {
            TerrainFolder + "Terrain_Meadow_Grass_01.terrainlayer",         // 0 lush lowland grass
            TerrainFolder + "Terrain_Meadow_Grass_02.terrainlayer",         // 1 dry / upland grass
            TerrainFolder + "Terrain_Meadow_Dirt_01.terrainlayer",          // 2 roads, settlement discs, tilled fields
            TerrainFolder + "Terrain_Meadow_Rock_Rough_01.terrainlayer",    // 3 steep slopes, ridgelines
            TerrainFolder + "Terrain_Meadow_Grass_Leaves_01.terrainlayer",  // 4 forest floor
            TerrainFolder + "Terrain_Meadow_Grass_Flowers_01.terrainlayer", // 5 flowery meadow patches
            TerrainFolder + "Terrain_Meadow_Mud_01.terrainlayer",           // 6 lake shores
            TerrainFolder + "Terrain_Meadow_Cobblestone_01.terrainlayer",   // 7 town centres
        };

        /// <summary>Material for the lake water plane.</summary>
        public const string WaterMaterial = Root + "Materials/Water_Lake.mat";

        private static string[] Range(string prefix, int from, int to)
        {
            var result = new string[to - from + 1];
            for (int i = from; i <= to; i++) result[i - from] = $"{prefix}{i:00}.prefab";
            return result;
        }

        private static string[] Concat(this string[] a, string[] b)
        {
            var result = new string[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);
            return result;
        }
    }
}
