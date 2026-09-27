namespace MedievalZen.World
{
    /// <summary>
    /// Prefab family ids. A placement record names a family plus a variant index; the <see cref="WorldPalette"/>
    /// maps family + variant to a prefab (see <see cref="SyntyCatalog"/> for the default contents). Families are
    /// plain strings so adding one is a line here, a line in the catalog and a line in <see cref="ScatterRules"/>.
    /// </summary>
    public static class PropRole
    {
        // ------------------------------------------------ trees (terrain tree instances)

        /// <summary>Trees in forest areas.</summary>
        public const string ForestTree = "ForestTree";
        /// <summary>Lone trees in meadows and the highlands.</summary>
        public const string MeadowTree = "MeadowTree";
        /// <summary>Fruit trees near villages and towns.</summary>
        public const string FruitTree = "FruitTree";
        /// <summary>Pines on the highland and in dark forest cores.</summary>
        public const string PineTree = "PineTree";
        /// <summary>Dead trees (rare).</summary>
        public const string DeadTree = "DeadTree";

        // ------------------------------------------------ countryside scatter

        /// <summary>Bushes.</summary>
        public const string Bush = "Bush";
        /// <summary>Shrubs and ferns (forest floor).</summary>
        public const string Shrub = "Shrub";
        /// <summary>Stumps, logs and roots (forest floor).</summary>
        public const string Stump = "Stump";
        /// <summary>Twigs (forest floor).</summary>
        public const string Twig = "Twig";
        /// <summary>Single rocks.</summary>
        public const string Rock = "Rock";
        /// <summary>Small pebble piles.</summary>
        public const string RockSmallPile = "RockSmallPile";
        /// <summary>Rock piles.</summary>
        public const string RockPile = "RockPile";
        /// <summary>Flat rock outcrops in the highlands.</summary>
        public const string RockGround = "RockGround";
        /// <summary>Rock faces on steep slopes.</summary>
        public const string Cliff = "Cliff";
        /// <summary>Earthy cliff faces on lowland escarpments.</summary>
        public const string DirtCliff = "DirtCliff";
        /// <summary>Large ground cliffs on the steepest slopes.</summary>
        public const string GroundCliff = "GroundCliff";
        /// <summary>Large grassy ground mounds.</summary>
        public const string Mound = "Mound";
        /// <summary>Single / sparse mushrooms.</summary>
        public const string Mushroom = "Mushroom";
        /// <summary>Mushroom groups.</summary>
        public const string MushroomGroup = "MushroomGroup";
        /// <summary>Leaf piles and fallen branches.</summary>
        public const string Leaves = "Leaves";
        /// <summary>Water lilies (placed at water level).</summary>
        public const string Lilies = "Lilies";
        /// <summary>Stone stacks (cairns) on crests.</summary>
        public const string StoneStack = "StoneStack";
        /// <summary>Stone piles (cairns, roadside).</summary>
        public const string StonePile = "StonePile";
        /// <summary>Rune stones (rare landmarks).</summary>
        public const string StoneRunes = "StoneRunes";

        // ------------------------------------------------ farmland

        /// <summary>Crop-field clump (wheat).</summary>
        public const string CropField = "CropField";
        /// <summary>Rapeseed clump (yellow fields).</summary>
        public const string Rapeseed = "Rapeseed";
        /// <summary>Sunflower.</summary>
        public const string Sunflower = "Sunflower";
        /// <summary>Scarecrow.</summary>
        public const string Scarecrow = "Scarecrow";
        /// <summary>Straight fence piece (yaw = direction the fence line runs).</summary>
        public const string Fence = "Fence";
        /// <summary>Fence post (field corners).</summary>
        public const string FencePost = "FencePost";
        /// <summary>Fence gate (yaw = direction the fence line runs).</summary>
        public const string FenceGate = "FenceGate";

        // ------------------------------------------------ settlements

        /// <summary>Stone cabin.</summary>
        public const string Cabin = "Cabin";
        /// <summary>Windmill (blades get a rotation animator).</summary>
        public const string Windmill = "Windmill";
        /// <summary>Well.</summary>
        public const string Well = "Well";
        /// <summary>Sign / signpost.</summary>
        public const string Sign = "Sign";
        /// <summary>Horse hitch.</summary>
        public const string HorseHitch = "HorseHitch";
        /// <summary>Camp fireplace.</summary>
        public const string Fireplace = "Fireplace";
        /// <summary>Cooking tripod over a fire.</summary>
        public const string FireTripod = "FireTripod";
        /// <summary>Camp tent.</summary>
        public const string Tent = "Tent";
        /// <summary>Tanning rack.</summary>
        public const string TanningRack = "TanningRack";
        /// <summary>Small camp items (bucket, can, pouch).</summary>
        public const string CampItem = "CampItem";
        /// <summary>Crate.</summary>
        public const string Crate = "Crate";
        /// <summary>Hand cart or broken wagon.</summary>
        public const string Cart = "Cart";
        /// <summary>Stone arch over a road (yaw = road direction).</summary>
        public const string StoneArch = "StoneArch";
        /// <summary>Long straight stone-wall piece (yaw = direction the wall runs).</summary>
        public const string StoneWall = "StoneWall";
        /// <summary>Short stone-wall piece.</summary>
        public const string StoneWallSmall = "StoneWallSmall";
        /// <summary>Stone-wall end piece.</summary>
        public const string StoneWallEnd = "StoneWallEnd";
        /// <summary>Stone-wall pillar.</summary>
        public const string StoneWallPillar = "StoneWallPillar";
        /// <summary>Small gate in a town wall.</summary>
        public const string Gate = "Gate";
        /// <summary>Birdhouse near cabins.</summary>
        public const string Birdhouse = "Birdhouse";
        /// <summary>Wind chime near cabins.</summary>
        public const string WindChime = "WindChime";
        /// <summary>Kite (rare).</summary>
        public const string Kite = "Kite";
        /// <summary>Water wheel on a shore near a village.</summary>
        public const string WaterWheel = "WaterWheel";
        /// <summary>Barrels, crates, sacks and pots (inn yards, village centres).</summary>
        public const string Goods = "Goods";
        /// <summary>Chest.</summary>
        public const string Chest = "Chest";
        /// <summary>Outdoor table.</summary>
        public const string Table = "Table";
        /// <summary>Chair.</summary>
        public const string Chair = "Chair";

        // ------------------------------------------------ horizon (outside the playable area)

        /// <summary>Background hills ringed around the map.</summary>
        public const string HorizonHill = "HorizonHill";
        /// <summary>Background mountains behind the highland side.</summary>
        public const string HorizonMountain = "HorizonMountain";

        // ------------------------------------------------ terrain details (grass/flowers, per-cell density maps)

        /// <summary>Tall grass clumps.</summary>
        public const string TallGrass = "TallGrass";
        /// <summary>Medium grass clumps.</summary>
        public const string MedGrass = "MedGrass";
        /// <summary>Short grass clumps.</summary>
        public const string ShortGrass = "ShortGrass";
        /// <summary>Wildflowers.</summary>
        public const string Wildflowers = "Wildflowers";
        /// <summary>Flat flowers.</summary>
        public const string FlatFlowers = "FlatFlowers";
        /// <summary>Forest ground cover.</summary>
        public const string GroundCover = "GroundCover";
        /// <summary>Sunflowers scattered as a detail.</summary>
        public const string SunflowerDetail = "SunflowerDetail";
        /// <summary>Extra-colour flowers from the generic pack.</summary>
        public const string GenFlowers = "GenFlowers";

        /// <summary>True for families realised as terrain tree instances rather than GameObjects.</summary>
        public static bool IsTree(string family) =>
            family == ForestTree || family == MeadowTree || family == FruitTree || family == PineTree || family == DeadTree;
    }
}
