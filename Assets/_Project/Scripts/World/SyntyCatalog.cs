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

        /// <summary>POLYGON Fantasy Kingdom environment prefabs (stone bridge, reeds).</summary>
        private const string Kingdom = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/";
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

        /// <summary>
        /// Material the rapeseed prefabs are derived from (the models import without materials). The rapeseed clumps are the crop
        /// field clumps' geometry (same triangle counts, same UV layout), so this is the crop field's material: its texture is the
        /// one the UVs were laid out for, and it fills both the leaf and the trunk texture slot. The mesh is drawn from the trunk
        /// slot: with the flower field material used until 2026-10-06 (trunk slot empty) every clump was an opaque white card.
        /// </summary>
        public const string RapeseedMaterial = Root + "Materials/Plants/CropField_01.mat";

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
            // Rocks and cliffs: the Meadow pieces share one look (Rock_Grass_Triplanar_Meadow_01: RockWall texture with grass on
            // top) and are the only ones used in the highlands; the dark atlas-coloured PolygonGeneric rocks stay in the lowland
            // (LowlandRock) and the Generic cliffs are catalogued but not scattered (GenericCliff).
            (PropRole.Rock, Range(Env + "Rock_", 1, 6).Concat(new[] { Env + "Rock_Round_01.prefab", Env + "Rock_Small_01.prefab" })),
            (PropRole.LowlandRock, Range(GenEnv + "Rock_", 1, 10)),
            (PropRole.RockSmallPile, Range(Env + "Rock_Small_Pile_", 1, 2)),
            (PropRole.RockPile, Range(Env + "Rock_Pile_", 1, 7)),
            (PropRole.RockGround, Range(Env + "Rock_Ground_", 1, 2)),
            (PropRole.Cliff, Range(Env + "Rock_Cliff_", 1, 3)),
            (PropRole.GenericCliff, Range(GenEnv + "Cliff_", 1, 4)),
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
            (PropRole.Bridge, new[] { Kingdom + "SM_Env_Bridge_Stone_01.prefab" }),
            (PropRole.Reeds, Range(Kingdom + "SM_Env_Reeds_", 1, 3)),
            (PropRole.Goods, Range(GenProp + "Barrel_Wood_", 1, 3).Concat(Range(GenProp + "Crate_", 1, 3)).Concat(Range(GenProp + "Sack_", 1, 5)).Concat(Range(GenProp + "Sack_Stack_", 1, 2)).Concat(Range(GenProp + "Pot_", 1, 5))),
            (PropRole.Chest, Range(GenProp + "Chest_", 1, 2)),
            (PropRole.Table, new[] { GenProp + "Table_01.prefab" }),
            (PropRole.Chair, new[] { GenProp + "Chair_01.prefab" }),
            (PropRole.HorizonHill, new[] { BackgroundHill }.Concat(Range(GenEnv + "Hill_", 1, 5))),
            (PropRole.HorizonMountain, Range(GenEnv + "Background_Mountain_", 1, 3)),
            // Endless road landmarks and field boundaries (POLYGON Fantasy Kingdom; sizes measured from the FBX, see ASSET_CATALOG).
            (PropRole.LandmarkTower, new[] { KingdomPreset + "Tower_01_Optimized.prefab" }),
            (PropRole.LandmarkChapel, new[] { KingdomPreset + "Church_01_A_Optimized.prefab", KingdomPreset + "Church_01_B_Optimized.prefab" }),
            (PropRole.LandmarkMill, new[] { KingdomPreset + "House_Windmill_01_Optimized.prefab" }),
            (PropRole.LandmarkRuin, Range(KingdomBuildings + "SM_Prop_Destroyed_House_Preset_", 1, 2)),
            (PropRole.Gravestone, Range(KingdomProps + "SM_Prop_Gravestone_", 1, 5).Concat(Range(KingdomProps + "SM_Prop_Gravestone_Broken_", 1, 2)).Concat(new[] { KingdomProps + "SM_Prop_Cross_01.prefab" })),
            // The two hay piles standing on their pivot (2.5 and 1.5 m tall; _03's mesh is off its pivot: the family is centred).
            (PropRole.Haystack, new[] { KingdomProps + "SM_Prop_Hay_Pile_01.prefab", KingdomProps + "SM_Prop_Hay_Pile_03.prefab" }),
            (PropRole.WoodPile, Range(KingdomProps + "SM_Prop_Wood_Pile_", 1, 3)),
            (PropRole.Hedge, new[] { Kingdom + "SM_Env_Hedge_01.prefab" }),
            // Endless road woods (forest pass, 2026-10-05; palette v10): the floor, the undergrowth and climbers (see ASSET_CATALOG).
            (PropRole.Fern, Range(GenEnv + "Fern_", 1, 3).Concat(new[] { Kingdom + "SM_Env_Fern_01.prefab", Kingdom + "SM_Env_Fern_03.prefab" })),
            (PropRole.Undergrowth, Range(GenEnv + "Bush_", 1, 4).Concat(Range(GenEnv + "Bush_Large_", 1, 4))),
            (PropRole.Ivy, Range(Kingdom + "SM_Env_Ivy_", 1, 3).Concat(new[] { Kingdom + "SM_Env_Ivy_05.prefab", Kingdom + "SM_Env_Ivy_06.prefab" })),
            (PropRole.IvyDrape, Range(GenEnv + "Ivy_Draped_", 1, 3)),
            // Endless road mountain passes (2026-10-05; palette v11): the Fantasy Kingdom rocks, one flat grey look with the grey
            // mountain rock paint (measured: see ASSET_CATALOG and PropMetricsTable).
            (PropRole.PassCliff, Range(Kingdom + "SM_Env_Rock_Cliff_", 1, 5)),
            (PropRole.PassBoulder, Range(Kingdom + "SM_Env_Rock_Chunk_", 1, 3)),
            // Endless road inns, hamlets and villages (2026-10-05; palette v12): the Fantasy Kingdom building presets (measured
            // footprints, doors and chimneys in BuildingTable) and the yard and village props (sizes in ASSET_CATALOG).
            (PropRole.Tavern, new[] { KingdomPreset + "Tavern_01_Optimized.prefab" }),
            (PropRole.House, new[]
            {
                KingdomPreset + "House_01_A_Optimized.prefab", KingdomPreset + "House_02_A_Optimized.prefab", KingdomPreset + "House_03_Optimized.prefab",
                KingdomPreset + "House_05_Optimized.prefab", KingdomPreset + "House_07_Optimized.prefab", KingdomPreset + "House_08_Optimized.prefab",
                KingdomPreset + "House_09_A_Optimized.prefab", KingdomPreset + "House_09_B_Optimized.prefab", KingdomPreset + "House_09_C_Optimized.prefab",
            }),
            (PropRole.Hut, new[] { KingdomPreset + "Hut_01_Optimized.prefab", KingdomPreset + "Hut_02_Optimized.prefab" }),
            (PropRole.Outhouse, new[] { KingdomPreset + "Outhouse_01_Optimized.prefab" }),
            (PropRole.Shelter, new[] { KingdomPreset + "Shelter_01_Optimized.prefab", KingdomPreset + "Shelter_02_Optimized.prefab" }),
            (PropRole.Smithy, new[] { KingdomPreset + "Blacksmith_01_Optimized.prefab" }),
            (PropRole.Stables, new[] { KingdomPreset + "Stables_01_Optimized.prefab" }),
            (PropRole.Gatehouse, new[] { KingdomPreset + "House_Archway_02_Optimized.prefab" }),
            (PropRole.Bench, new[] { KingdomProps + "Furniture/SM_Prop_Bench_Seat_01.prefab", KingdomProps + "Furniture/SM_Prop_Bench_Seat_04.prefab", KingdomProps + "Furniture/SM_Prop_Bench_Seat_05.prefab", KingdomProps + "Furniture/SM_Prop_Bench_Seat_03.prefab" }),
            (PropRole.Lamp, Range(KingdomProps + "SM_Prop_Street_Lamp_", 1, 2)),
            (PropRole.MarketStall, new[] { KingdomProps + "SM_Prop_Market_Stall_03.prefab", KingdomProps + "SM_Prop_Market_Stall_06.prefab", KingdomProps + "SM_Prop_Market_Stall_08.prefab" }),
            (PropRole.YardGoods, Range(KingdomProps + "SM_Prop_Barrel_Stack_", 2, 4).Concat(Range(KingdomProps + "SM_Prop_Crate_Stack_", 3, 4)).Concat(Range(KingdomProps + "SM_Prop_Sack_Stack_", 1, 2)).Concat(new[] { KingdomProps + "SM_Prop_Barrel_01.prefab" })),
            (PropRole.FarmCart, Range(KingdomVehicles + "SM_Veh_Cart_", 1, 4).Concat(new[] { KingdomProps + "SM_Prop_Hay_Cart_01.prefab", KingdomVehicles + "SM_Veh_Wheelbarrow_01.prefab" })),
            (PropRole.GardenPlant, Range(KingdomProps + "SM_Prop_Cabbage_", 1, 2).Concat(Range(KingdomProps + "SM_Prop_Pumpkin_Plant_", 1, 2))),
            (PropRole.SmithyTool, Range(KingdomProps + "SM_Prop_Anvil_", 2, 3).Concat(new[] { KingdomProps + "SM_Prop_Grinding_Wheel_01.prefab" })),
            (PropRole.Fingerpost, Range(KingdomProps + "SM_Prop_Direction_Sign_", 1, 2)),
            // Walled cities (2026-10-06): the castle kit's curtain wall, its crenellations and round wall towers, drawn instanced.
            (PropRole.CityWall, new[] { KingdomCastle + "SM_Bld_Castle_Wall_01.prefab" }),
            (PropRole.CityBattlement, new[] { KingdomCastle + "SM_Bld_Castle_Battlements_01.prefab" }),
            (PropRole.CityTower, new[] { KingdomCastle + "SM_Bld_Castle_Wall_Tower_M_01.prefab" }),
            // The hill town's terraces (2026-10-09): capped pillars at the retaining walls' ends and corners (instanced), the stair flight (a GameObject).
            (PropRole.CityPillar, new[] { KingdomCastle + "SM_Bld_Castle_Pillar_Stone_05.prefab", KingdomCastle + "SM_Bld_Castle_Battlements_Pillar_01.prefab" }),
            (PropRole.CityStairs, new[] { KingdomCastle + "SM_Bld_Stairs_Straight_Wide_Edges_01.prefab" }),
            // City set pieces (2026-10-06): the street dressing and castle pieces of the arrangements mined from the Fantasy Kingdom demo city
            // (CitySetPieces.Data.cs; a SetPart's Variant is the index in these lists, so append only). CastleModule is drawn instanced.
            (PropRole.CityStall, new[] { KingdomPrefabs + "Props/SM_Prop_Market_Stall_04.prefab", KingdomPrefabs + "Props/SM_Prop_Market_Stall_05.prefab", KingdomPrefabs + "Props/SM_Prop_Market_Stall_07.prefab", KingdomPrefabs + "Props/SM_Prop_Market_Stall_09.prefab" }),
            (PropRole.CityGoods, new[] { KingdomPrefabs + "Props/SM_Prop_Bucket_Fish_01.prefab", KingdomPrefabs + "Props/SM_Prop_Crate_Stack_01.prefab", KingdomPrefabs + "Props/SM_Prop_Crate_Stack_02.prefab", KingdomPrefabs + "Props/SM_Prop_Crate_Wood_01.prefab", KingdomPrefabs + "Props/SM_Prop_Pot_01.prefab", KingdomPrefabs + "Props/SM_Prop_Pot_02.prefab", KingdomPrefabs + "Props/SM_Prop_Pot_Spice_01.prefab", KingdomPrefabs + "Props/SM_Prop_Pot_Spice_02.prefab", KingdomPrefabs + "Props/SM_Prop_Pot_Spice_03.prefab", KingdomPrefabs + "Props/SM_Prop_Sack_01.prefab" }),
            (PropRole.CityHanging, new[] { KingdomPrefabs + "Props/SM_Prop_Clothes_Rack_01_Preset_02.prefab", KingdomPrefabs + "Props/SM_Prop_Clothes_Rack_01_Preset_03.prefab", KingdomPrefabs + "Props/SM_Prop_Hanging_Clothes_02.prefab", KingdomPrefabs + "Props/SM_Prop_Hanging_Clothes_04.prefab", KingdomPrefabs + "Props/SM_Prop_Hanging_Clothes_05.prefab", KingdomPrefabs + "Props/SM_Prop_Meat_Hanger_01.prefab", KingdomPrefabs + "Props/SM_Prop_Pig_Hanging_01.prefab" }),
            (PropRole.CityBanner, new[] { KingdomPrefabs + "Props/SM_Prop_Awning_03.prefab", KingdomPrefabs + "Props/SM_Prop_Bunting_01.prefab", KingdomPrefabs + "Props/Banners/SM_Prop_Flag_10.prefab", KingdomPrefabs + "Props/Banners/SM_Prop_Flag_12.prefab" }),
            (PropRole.CitySign, new[] { KingdomPrefabs + "Props/SM_Prop_Quest_Board_01.prefab", KingdomPrefabs + "Props/SM_Prop_Quest_Board_01_Posters_01.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Bracket_01.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Bracket_05.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Bracket_07.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Chain_01.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Chain_02.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Iron_03.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Wood_02.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Wood_05.prefab", KingdomPrefabs + "Props/SM_Prop_Sign_Wood_11.prefab" }),
            (PropRole.CityPlanter, new[] { KingdomPrefabs + "Environments/SM_Env_Bush_Flowers_01.prefab", KingdomPrefabs + "Environments/SM_Env_Bush_Flowers_03.prefab", KingdomPrefabs + "Environments/SM_Env_Bush_Flowers_04.prefab", KingdomPrefabs + "Environments/SM_Env_Bush_Flowers_06.prefab", KingdomPrefabs + "Environments/SM_Env_Planter_Pedestal_01_Preset_02.prefab", KingdomPrefabs + "Environments/SM_Env_Planter_Pedestal_01_Preset_03.prefab", KingdomPrefabs + "Environments/SM_Env_Planter_Pedestal_01_Preset_05.prefab", KingdomPrefabs + "Environments/SM_Env_Planter_Pot_01.prefab", KingdomPrefabs + "Environments/SM_Env_Planter_Pot_02.prefab", KingdomPrefabs + "Environments/SM_Env_Planter_Pot_04.prefab", KingdomPrefabs + "Environments/SM_Env_Planter_Preset_02.prefab", KingdomPrefabs + "Props/SM_Prop_Bracket_Pot_02_Preset_01.prefab", KingdomPrefabs + "Props/SM_Prop_Bracket_Pot_02_Preset_04.prefab", KingdomPrefabs + "Props/SM_Prop_Bracket_Pot_03_Preset_02.prefab", KingdomPrefabs + "Props/SM_Prop_Bracket_Pot_03_Preset_04.prefab" }),
            (PropRole.CityFurniture, new[] { KingdomPrefabs + "Props/SM_Prop_Ladder_Tied_02.prefab", KingdomPrefabs + "Props/Furniture/SM_Prop_Stool_01.prefab", KingdomPrefabs + "Props/Furniture/SM_Prop_Stool_03.prefab", KingdomPrefabs + "Props/Furniture/SM_Prop_Table_Preset_01.prefab", KingdomPrefabs + "Props/Furniture/SM_Prop_Table_Preset_02.prefab" }),
            (PropRole.CityWorkshop, new[] { KingdomPrefabs + "Props/SM_Prop_Bellows_01.prefab", KingdomPrefabs + "Props/SM_Prop_Bellows_02.prefab", KingdomPrefabs + "Props/SM_Prop_Cooking_Block_02.prefab", KingdomPrefabs + "Props/SM_Prop_Hay_Pile_02.prefab", KingdomPrefabs + "Props/Furniture/SM_Prop_Workbench_01_Preset.prefab" }),
            (PropRole.CityMonument, new[] { KingdomPrefabs + "Environments/SM_Env_Big_Tree_Base_Paving_01.prefab", KingdomPrefabs + "Environments/SM_Env_Fountain_01.prefab", KingdomPrefabs + "Props/SM_Prop_Statue_Angel_01.prefab", KingdomPrefabs + "Props/SM_Prop_Statue_Hero_01.prefab", KingdomPrefabs + "Props/SM_Prop_Statue_Knight_02.prefab", KingdomPrefabs + "Props/SM_Prop_Statue_Plinth_01.prefab", KingdomPrefabs + "Props/SM_Prop_Well_01.prefab", KingdomPrefabs + "Props/SM_Prop_Well_Plinth_01.prefab" }),
            (PropRole.Waterside, new[] { KingdomPrefabs + "Castle/SM_Bld_Waterwheel_01.prefab", KingdomPrefabs + "Environments/SM_Env_Structure_03.prefab", KingdomPrefabs + "Vehicles/SM_Veh_Boat_Dinghy_01.prefab" }),
            (PropRole.CastleModule, new[] { KingdomPrefabs + "Castle/SM_Bld_Bridge_Pillars_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Bridge_Railing_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_01_Short.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_06.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_L_Corner_05.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_Pillar_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_Pillar_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Hoarding_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Hoarding_Corner_L_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Hoarding_Corner_S_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Hoarding_End_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Pillar_Stone_05.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Pillar_Stone_Round_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_Cap_Edge_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_L_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_L_Corner_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_L_Edge_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_L_Edge_Corner_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_L_End_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_L_Window_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_S_Corner_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_S_Edge_Corner_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_03.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_04.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_05.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Archway_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corbels_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corbels_Corner_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_L_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_L_Base_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_L_Window_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_S_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_S_Window_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Cut_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Cut_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Door_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Door_Big_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Half_Corner_L_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Half_Corner_S_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Pillars_Half_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Pillars_Half_Corner_L_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Pillars_Half_Corner_S_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Window_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Window_03.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Window_04.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Window_Fancy_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Stairs_Straight_Wide_Edges_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Stairs_Wood_Corner_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Floor_Stone_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Floor_Wood_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Floor_Wood_03.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Floor_Wood_Round_L_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Pillar_Wood_Fancy_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Pillar_Wood_Round_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Pillar_Wood_Round_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wood_Floor_Gap_02.prefab", KingdomPrefabs + "Buildings/House/SM_Bld_House_Floor_Stone_01.prefab", KingdomPrefabs + "Buildings/House/SM_Bld_House_Floor_Stone_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Keep_Pillar_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Keep_Pillar_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Keep_Pillar_03.prefab", KingdomPrefabs + "Castle/SM_Bld_Keep_Pillar_04.prefab", KingdomPrefabs + "Castle/SM_Bld_Keep_Pillar_Cap_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Walkway_Wood_Railing_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Walkway_Wood_Railing_Pillar_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Wall_Wood_Beams_03.prefab", KingdomPrefabs + "Castle/SM_Bld_Wood_Beam_Curved_01.prefab" }),
            (PropRole.CastlePiece, new[] { KingdomPrefabs + "Castle/SM_Bld_Bridge_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Bridge_Roof_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Balcony_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_Pillar_06.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Cross_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Drawbridge_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Pillar_Stone_Half_03.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_Cap_Round_03.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_Cap_Round_06.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_Cap_Round_07.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_Cap_Square_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_L_Corner_Inner_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_L_End_Beam_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_Short_Half_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Roof_Short_Half_End_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Archway_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_L_Arrowslit_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_L_Base_Slope_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_L_Base_Slope_Stairs_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_L_Door_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_S_Door_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Corner_Slope_Straight_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Gate_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Panel_Stone_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Panel_Stone_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Window_05.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Window_Big_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Window_Fancy_Tall_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Stairs_Round_L_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Stairs_Round_S_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Stairs_Round_S_Roof_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Stairs_Straight_Wide_Edges_03.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_02_Half.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Battlements_L_Corner_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Floor_Wood_Hatch_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Floor_Wood_Round_S_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wall_Panel_Wood_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Castle_Wood_Floor_Gap_01.prefab", KingdomPrefabs + "Castle/SM_Bld_Stairs_Straight_Wide_02.prefab", KingdomPrefabs + "Castle/SM_Bld_Walkway_Stone_Railing_Pillar_01.prefab" }),
        };

        private const string KingdomBuildings = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Buildings/";
        private const string KingdomPreset = KingdomBuildings + "Presets/SM_Bld_Preset_";
        private const string KingdomCastle = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/";
        private const string KingdomProps = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/";
        private const string KingdomVehicles = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Vehicles/";
        private const string KingdomPrefabs = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";

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

        /// <summary>
        /// First-party terrain layer painted on steep slopes: the Meadow <c>RockWall</c> texture and normals, i.e. the same
        /// texture the Meadow rock and cliff meshes show on their sides (<c>Rock_Grass_Triplanar_Meadow_01</c>), so painted rock
        /// and rock meshes match. Created by the palette menu (never hand-written).
        /// </summary>
        public const string RockWallLayer = "Assets/_Project/Settings/TerrainLayers/RockWall.terrainlayer";

        /// <summary>Diffuse texture of <see cref="RockWallLayer"/>.</summary>
        public const string RockWallTexture = TerrainFolder + "RockWall_Texture_01.png";

        /// <summary>Normal map of <see cref="RockWallLayer"/>.</summary>
        public const string RockWallNormals = TerrainFolder + "RockWall_Normals_01.png";

        /// <summary>
        /// Tile size (m) of <see cref="RockWallLayer"/>: the Meadow triplanar rock material samples world position × 0.2
        /// (<c>_Triplanar_Tiling_Side</c> 20 × the shader's 0.01), i.e. one texture repeat every 5 m.
        /// </summary>
        public const float RockWallTileSize = 5f;

        /// <summary>Synty rock layer used when <see cref="RockWallLayer"/> cannot be created.</summary>
        public const string RockFallbackLayer = TerrainFolder + "Terrain_Meadow_Rock_Rough_01.terrainlayer";

        /// <summary>Folder of the first-party terrain layers (created by the palette menu, never hand-written).</summary>
        public const string LayerFolder = "Assets/_Project/Settings/TerrainLayers/";

        /// <summary>First-party lush grass layer: Meadow Grass_01 tinted to the lush sward (<see cref="GroundLook"/>).</summary>
        public const string LushGrassLayer = LayerFolder + "LushGrass.terrainlayer";

        /// <summary>First-party dry grass layer: Meadow Grass_02 tinted to the dry sward.</summary>
        public const string DryGrassLayer = LayerFolder + "DryGrass.terrainlayer";

        /// <summary>First-party flower-meadow layer: Meadow Grass_Flowers_01 tinted halfway to the lush sward.</summary>
        public const string FlowerMeadowLayer = LayerFolder + "FlowerMeadow.terrainlayer";

        /// <summary>The Synty layer each tinted first-party layer copies (its textures, normals and tiling).</summary>
        public static readonly IReadOnlyDictionary<string, string> TintedLayerSources = new Dictionary<string, string>
        {
            [LushGrassLayer] = TerrainFolder + "Terrain_Meadow_Grass_01.terrainlayer",
            [DryGrassLayer] = TerrainFolder + "Terrain_Meadow_Grass_02.terrainlayer",
            [FlowerMeadowLayer] = TerrainFolder + "Terrain_Meadow_Grass_Flowers_01.terrainlayer",
        };

        /// <summary>
        /// The endless road's mountain layers (channels <see cref="TerrainShaper.MountainRockLayer"/> and after), first-party tinted copies
        /// built by the palette menu: the Synty layer each copies (textures, normals), its tile size (m) and the asset path.
        /// </summary>
        public static readonly IReadOnlyList<(string path, string source, float tileSize)> MountainLayers = new List<(string, string, float)>
        {
            (LayerFolder + "MountainRock.terrainlayer", TerrainFolder + "Terrain_Meadow_Rock_Rough_01.terrainlayer", 6f),   // 8 grey rock of the pass ranges
            (LayerFolder + "Scree.terrainlayer", TerrainFolder + "Terrain_Meadow_Gravel_01.terrainlayer", 4f),               // 9 talus fans, aprons, gully floors
            (LayerFolder + "Heath.terrainlayer", TerrainFolder + "Terrain_Meadow_Grass_Debris_01.terrainlayer", 3f),         // 10 heath on ledges and slopes
            // The farmland's field channels (2026-10-06; the list is every endless-only layer in channel order, built by the same menu step).
            (LayerFolder + "FieldGold.terrainlayer", TerrainFolder + "Terrain_Meadow_Grass_02.terrainlayer", 3f),            // 11 ripe grain, stubble
            (LayerFolder + "FieldYellow.terrainlayer", TerrainFolder + "Terrain_Meadow_Grass_01.terrainlayer", 3f),          // 12 flowering rapeseed
            (LayerFolder + "FieldGreen.terrainlayer", TerrainFolder + "Terrain_Meadow_Grass_Clover_01.terrainlayer", 3f),    // 13 young green crop, garden greens
            (LayerFolder + "LeafLitter.terrainlayer", TerrainFolder + "Terrain_Meadow_Grass_Leaves_Heavy_01.terrainlayer", 2f), // 14 the woods' leaf litter (2026-10-06)
        };

        /// <summary>
        /// Endless road only: a first-party copy of the mud layer without its gloss. The Synty layer is metallic 0.3 and smoothness
        /// 0.5 (wet mud for a lake shore); wherever the endless road paints mud in the open (the wood floor, the ring round a trunk,
        /// a worn paddock) it mirrored the sky and read as pale grey sand, or as a pond from above (tours of 2026-10-06). Every other
        /// layer in use is matt. The finite world keeps the Synty layer.
        /// </summary>
        public const string EndlessMudLayer = LayerFolder + "MudMatt.terrainlayer";

        /// <summary>The Synty layer <see cref="EndlessMudLayer"/> copies.</summary>
        public const string MudSourceLayer = TerrainFolder + "Terrain_Meadow_Mud_01.terrainlayer";

        /// <summary>Terrain layer assets in splat order (see <see cref="TerrainShaper"/> layer constants).</summary>
        public static readonly string[] TerrainLayers =
        {
            LushGrassLayer,                                                 // 0 lush lowland grass
            DryGrassLayer,                                                  // 1 dry / upland grass
            TerrainFolder + "Terrain_Meadow_Dirt_01.terrainlayer",          // 2 roads, settlement discs, tilled fields, scree
            RockWallLayer,                                                  // 3 steep slopes, cliff bands, ridgelines
            TerrainFolder + "Terrain_Meadow_Grass_Leaves_01.terrainlayer",  // 4 forest floor
            FlowerMeadowLayer,                                              // 5 flowery meadow patches
            TerrainFolder + "Terrain_Meadow_Mud_01.terrainlayer",           // 6 lake shores
            TerrainFolder + "Terrain_Meadow_Cobblestone_01.terrainlayer",   // 7 town centres
        };

        /// <summary>Material for the lake water plane.</summary>
        public const string WaterMaterial = Root + "Materials/Water_Lake.mat";

        /// <summary>Material for the endless road's stream ribbons.</summary>
        public const string StreamMaterial = Root + "Materials/Water_Stream.mat";

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
