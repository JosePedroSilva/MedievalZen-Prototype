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
        /// <summary>Single rocks (Meadow boulders; the only single rocks used in the highlands).</summary>
        public const string Rock = "Rock";
        /// <summary>Dark PolygonGeneric boulders, lowland meadows and shores only.</summary>
        public const string LowlandRock = "LowlandRock";
        /// <summary>Small pebble piles.</summary>
        public const string RockSmallPile = "RockSmallPile";
        /// <summary>Rock piles.</summary>
        public const string RockPile = "RockPile";
        /// <summary>Flat rock outcrops in the highlands.</summary>
        public const string RockGround = "RockGround";
        /// <summary>Rock faces (Meadow cliff blocks) against steep slopes and above road cuts.</summary>
        public const string Cliff = "Cliff";
        /// <summary>PolygonGeneric cliff faces (atlas-coloured); catalogued, not scattered.</summary>
        public const string GenericCliff = "GenericCliff";
        /// <summary>Earthy cliff faces on lowland escarpments.</summary>
        public const string DirtCliff = "DirtCliff";
        /// <summary>
        /// Meadow <c>Ground_Cliff_Large</c>: a ~40 m raised mesa disc (top at the pivot, cliff skirt 4-8 m down all round).
        /// Catalogued, not scattered: it only fits level ground and read as floating arches on slopes.
        /// </summary>
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

        // Field looks (FieldPatch.crop markers of the endless road's paint-only fields; never prop families).

        /// <summary>A fallow field grazed as pasture: grass, a little drier, short grass details, no dirt.</summary>
        public const string PastureField = "PastureField";
        /// <summary>A distant wheat field shown as stubble / hay: straw-coloured dry grass with some earth.</summary>
        public const string StubbleField = "StubbleField";
        /// <summary>A distant rapeseed or sunflower field shown as a flowering meadow strip.</summary>
        public const string MeadowStrip = "MeadowStrip";

        /// <summary>True for a field look that is grass to the detail planner (no tilled earth, clumps allowed).</summary>
        public static bool IsGrassField(string crop) => crop == PastureField || crop == StubbleField || crop == MeadowStrip;
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
        /// <summary>
        /// Endless road: an inn's name board, built in code by the tile builder (<c>SettlementLife.PlaceNameBoard</c>, no palette family);
        /// placed by <c>InnComposer</c>.
        /// </summary>
        public const string InnSign = "InnSign";
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
        /// <summary>
        /// Bridge carrying the endless road over a stream: the Fantasy Kingdom stone arch (<c>SM_Env_Bridge_Stone_01</c>: 20 m long,
        /// deck 4.4 m between parapets, flat ends 0.24 m above the pivot, crown 1.92 m, arch opening about 4.6 m).
        /// </summary>
        public const string Bridge = "Bridge";
        /// <summary>Reed clumps on stream banks (Fantasy Kingdom <c>SM_Env_Reeds_01..03</c>, about 1.6 m tall).</summary>
        public const string Reeds = "Reeds";
        /// <summary>Barrels, crates, sacks and pots (inn yards, village centres).</summary>
        public const string Goods = "Goods";
        /// <summary>Chest.</summary>
        public const string Chest = "Chest";
        /// <summary>Outdoor table.</summary>
        public const string Table = "Table";
        /// <summary>Chair.</summary>
        public const string Chair = "Chair";

        // ------------------------------------------------ endless road: landmarks and field boundaries

        /// <summary>Landmark watchtower (Fantasy Kingdom <c>SM_Bld_Preset_Tower_01</c>: 9 x 9.4 m, 20.7 m tall).</summary>
        public const string LandmarkTower = "LandmarkTower";
        /// <summary>Landmark chapel with a bell tower (Fantasy Kingdom <c>Church_01_A/B</c>: 6.4 x 10.9 m, 9.2 / 11.7 m tall).</summary>
        public const string LandmarkChapel = "LandmarkChapel";
        /// <summary>Landmark windmill (Fantasy Kingdom <c>House_Windmill_01</c>: 17 m to the sails' tip; blades child turns).</summary>
        public const string LandmarkMill = "LandmarkMill";
        /// <summary>Landmark ruin (Fantasy Kingdom <c>SM_Prop_Destroyed_House_Preset_01/02</c>: 8.4 m square, 3-3.5 m walls).</summary>
        public const string LandmarkRuin = "LandmarkRuin";
        /// <summary>Gravestones and a cross (chapel yards).</summary>
        public const string Gravestone = "Gravestone";
        /// <summary>Haystacks (Fantasy Kingdom hay piles: by windmills, in stubble fields).</summary>
        public const string Haystack = "Haystack";
        /// <summary>Wood piles (by a watchtower).</summary>
        public const string WoodPile = "WoodPile";
        /// <summary>Hedge piece of a field boundary (Fantasy Kingdom <c>SM_Env_Hedge_01</c>: 5.0 m long, 1.4 m tall at scale 1).</summary>
        public const string Hedge = "Hedge";

        // ------------------------------------------------ endless road: the woods (forest pass, 2026-10-05)

        /// <summary>Forest ferns (Generic <c>Fern_01..03</c>, Fantasy Kingdom <c>Fern_01</c>, <c>Fern_03</c>): the wood floor, instanced.</summary>
        public const string Fern = "Fern";
        /// <summary>Undergrowth bushes between the trunks (Generic <c>Bush_01..04</c>, <c>Bush_Large_01..04</c>: 266-1,190 triangles), instanced.</summary>
        public const string Undergrowth = "Undergrowth";
        /// <summary>Ivy panels on walls (Fantasy Kingdom <c>Ivy_01..03</c>, <c>_05</c>, <c>_06</c>: a card standing out +Z of its pivot plane, centred in height).</summary>
        public const string Ivy = "Ivy";
        /// <summary>Ivy draped over the top of a low wall (Generic <c>Ivy_Draped_01..03</c>: hangs below its pivot).</summary>
        public const string IvyDrape = "IvyDrape";

        // ------------------------------------------------ endless road: the mountain passes (2026-10-05)

        /// <summary>
        /// Cliff blocks of the pass ranges (Fantasy Kingdom <c>Rock_Cliff_01..05</c>: flat grey low-poly rock, 6-21 m native, seated
        /// 5-10 m tall): one rock look with the grey mountain rock paint. The pass ranges use it instead of the Meadow cliffs.
        /// </summary>
        public const string PassCliff = "PassCliff";
        /// <summary>Boulders of the pass ranges (Fantasy Kingdom <c>Rock_Chunk_01..03</c>, 1.7-3.7 m native): wall feet, scree fans, the slopes.</summary>
        public const string PassBoulder = "PassBoulder";

        /// <summary>The pass ranges' own rock families (one grey rock look, never mixed with the Meadow rocks in a range).</summary>
        public static bool IsPassRock(string family) => family == PassCliff || family == PassBoulder;

        /// <summary>Endless road: climbers laid on a wall face, drawn by the instanced renderer as pieces without a collider.</summary>
        public static bool IsOvergrowth(string family) => family == Ivy || family == IvyDrape;

        /// <summary>
        /// Endless road: the forest pass's extras (ferns, undergrowth, climbers) that the safety ladder sheds first, before any tree.
        /// </summary>
        public static bool IsForestExtra(string family) => family == Fern || family == Undergrowth || IsOvergrowth(family);

        /// <summary>True for the main structure of a landmark (drawn as a solid on distant tiles).</summary>
        public static bool IsLandmark(string family) => family == LandmarkTower || family == LandmarkChapel || family == LandmarkMill || family == LandmarkRuin;

        /// <summary>
        /// Endless road: families of field-boundary pieces, drawn by the instanced renderer from <c>TileBlueprint.Pieces</c> (laid
        /// along their run, centred on their placement). The same families stay ordinary props elsewhere (village walls and fences).
        /// </summary>
        public static bool IsBoundaryPiece(string family) =>
            family == StoneWall || family == StoneWallSmall || family == StoneWallPillar || family == Fence || family == FencePost || family == FenceGate || family == Hedge ||
            IsCityPiece(family);

        // ------------------------------------------------ endless road: inns, hamlets and villages (2026-10-05)

        /// <summary>The roadside inn (Fantasy Kingdom <c>SM_Bld_Preset_Tavern_01</c>: body 10.2 x 7.8 m, 10.5 m tall, 34.6k triangles).</summary>
        public const string Tavern = "Tavern";
        /// <summary>Village houses (Fantasy Kingdom house presets; footprints, doors and chimneys in <see cref="BuildingTable"/>).</summary>
        public const string House = "House";
        /// <summary>Small cottages at a place's edge and the mountain refuge (<c>Hut_01/02</c>).</summary>
        public const string Hut = "Hut";
        /// <summary>Privy behind a house (<c>Outhouse_01</c>).</summary>
        public const string Outhouse = "Outhouse";
        /// <summary>Open timber shelter (<c>Shelter_01/02</c>): an inn's cart shed, the refuge's lean-to.</summary>
        public const string Shelter = "Shelter";
        /// <summary>The smithy of a village (<c>Blacksmith_01</c>).</summary>
        public const string Smithy = "Smithy";
        /// <summary>Stables (<c>Stables_01</c>): inn yards and villages.</summary>
        public const string Stables = "Stables";
        /// <summary>House with an arch over a lane (<c>House_Archway_02</c>, opening 4.3 m: too narrow for the 5 m road, so beside it).</summary>
        public const string Gatehouse = "Gatehouse";
        /// <summary>Outdoor benches (Fantasy Kingdom <c>Bench_Seat_*</c>): seats for the traveller (<c>SeatCatalog</c>: "Bench").</summary>
        public const string Bench = "Bench";
        /// <summary>Street lamps (Fantasy Kingdom <c>Street_Lamp_01/02</c>): lit at night by the settlement's ambience.</summary>
        public const string Lamp = "Lamp";
        /// <summary>Market stalls (Fantasy Kingdom <c>Market_Stall_03/06/08</c>, the lighter ones).</summary>
        public const string MarketStall = "MarketStall";

        /// <summary>A walled city's curtain wall module (Fantasy Kingdom castle kit <c>Castle_Wall_01</c>: 5 x 5 m, 0.5 m thick, 20 triangles), instanced.</summary>
        public const string CityWall = "CityWall";

        /// <summary>The crenellations on a city wall module (<c>Castle_Battlements_01</c>: 5 m long, 1.38 m tall, 146 triangles), instanced.</summary>
        public const string CityBattlement = "CityBattlement";

        /// <summary>A round wall tower of a walled city (<c>Castle_Wall_Tower_M_01</c>: 3.05 m across, 5.0 m above and 2.5 m below its pivot, 608 triangles), instanced.</summary>
        public const string CityTower = "CityTower";

        /// <summary>
        /// A walled city's stone pillar and its cap (<c>Castle_Pillar_Stone_05</c>: 0.66 m square, 5.03 m; <c>Castle_Battlements_Pillar_01</c>:
        /// 0.72 m, modelled 4.93-6.51 m above its pivot to crown a 5 m pillar): the ends, corners and relief of the hill's retaining walls,
        /// instanced (2026-10-09).
        /// </summary>
        public const string CityPillar = "CityPillar";

        /// <summary>
        /// The pack's stone stair flight between two terraces (<c>Stairs_Straight_Wide_Edges_01</c>: 5 m rise over 5 m run, 5.32 m wide with its
        /// edges, 276 triangles): a GameObject with its own walkable mesh collider, centred on its placement, climbing along the placement's
        /// yaw (the palette measures which way it climbs) (2026-10-09).
        /// </summary>
        public const string CityStairs = "CityStairs";

        /// <summary>True for the instanced pieces of a walled city's wall (see <see cref="IsBoundaryPiece"/>).</summary>
        public static bool IsCityPiece(string family) => family == CityWall || family == CityBattlement || family == CityTower || family == CastleModule || family == CityPillar;

        /// <summary>City set pieces: Market stalls of the demo city beyond <see cref="MarketStall"/> (<c>Market_Stall_04/05/07/09</c>).</summary>
        public const string CityStall = "CityStall";
        /// <summary>City set pieces: Street goods of the city set pieces: crates, a sack, pots, spice pots, a fish bucket.</summary>
        public const string CityGoods = "CityGoods";
        /// <summary>City set pieces: Hanging wares: clothes on lines and racks, a hanging pig, a meat hanger.</summary>
        public const string CityHanging = "CityHanging";
        /// <summary>City set pieces: Cloth on walls and stalls: flags hung on retaining walls, bunting, an awning.</summary>
        public const string CityBanner = "CityBanner";
        /// <summary>City set pieces: Shop signs on brackets and chains, notice boards with their posters.</summary>
        public const string CitySign = "CitySign";
        /// <summary>City set pieces: Flower pots, planter boxes, pedestal planters, hanging bracket pots and flowering bushes.</summary>
        public const string CityPlanter = "CityPlanter";
        /// <summary>City set pieces: Outdoor stools, tables and a tied ladder.</summary>
        public const string CityFurniture = "CityFurniture";
        /// <summary>City set pieces: Smithy and yard work pieces: bellows, a cooking block, a workbench, a hay pile.</summary>
        public const string CityWorkshop = "CityWorkshop";
        /// <summary>City set pieces: Square pieces: statues and plinths, the fountain, the Fantasy Kingdom well and its plinth, the great tree's paved ring.</summary>
        public const string CityMonument = "CityMonument";
        /// <summary>City set pieces: By the water: a dinghy, a small jetty (<c>Env_Structure_03</c>), the mill water wheel.</summary>
        public const string Waterside = "Waterside";
        /// <summary>City set pieces: Repeated castle kit modules of a <see cref="MedievalZen.Endless.Generation.CastlePlan"/> (walls, battlements, pillars, roof pieces), drawn instanced.</summary>
        public const string CastleModule = "CastleModule";
        /// <summary>City set pieces: Castle kit pieces used once or a few times in a castle plan (gate, drawbridge, roof caps), placed as GameObjects.</summary>
        public const string CastlePiece = "CastlePiece";
        /// <summary>Barrel, crate and sack stacks of a yard (Fantasy Kingdom).</summary>
        public const string YardGoods = "YardGoods";
        /// <summary>Carts, a hay cart and a wheelbarrow (Fantasy Kingdom).</summary>
        public const string FarmCart = "FarmCart";
        /// <summary>Kitchen-garden plants (Fantasy Kingdom cabbages and pumpkin plants).</summary>
        public const string GardenPlant = "GardenPlant";
        /// <summary>Anvils and a grinding wheel by the smithy.</summary>
        public const string SmithyTool = "SmithyTool";
        /// <summary>A fingerpost by the road at an inn (Fantasy Kingdom <c>Direction_Sign_01/02</c>, 3 m).</summary>
        public const string Fingerpost = "Fingerpost";

        /// <summary>
        /// Endless road: the Fantasy Kingdom building presets of inns and villages (placed by their measured footprint and door, see
        /// <see cref="BuildingTable"/>; drawn as solids far away).
        /// </summary>
        public static bool IsBuilding(string family) =>
            family == Tavern || family == House || family == Hut || family == Outhouse || family == Shelter || family == Smithy || family == Stables || family == Gatehouse;

        /// <summary>The building an inn's rest point belongs to: the cabin (shared dresser), the tavern or the refuge's hut.</summary>
        public static bool IsInnBuilding(string family) => family == Cabin || family == Tavern || family == Hut;

        /// <summary>
        /// Buildings people live or work in: chimney smoke (or a roof vent) and a lit window at night (the stone cabin and every
        /// endless-road building but the privy and the open shelter).
        /// </summary>
        public static bool IsDwelling(string family) =>
            family == Cabin || family == Tavern || family == House || family == Hut || family == Smithy || family == Stables || family == Gatehouse;

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

        /// <summary>
        /// True for rock-like families placed by <see cref="RockPlacer"/> (measured footprint, embedded in the ground, cliffs
        /// backed by the slope) and checked again by the builder's safety net.
        /// </summary>
        public static bool IsRock(string family) =>
            family == Rock || family == LowlandRock || family == RockSmallPile || family == RockPile || family == RockGround || family == Cliff ||
            family == GenericCliff || family == DirtCliff || family == GroundCliff || family == Mound || family == PassCliff || family == PassBoulder;

        /// <summary>True for the cliff families (backing rule: a face needs higher ground behind it).</summary>
        public static bool IsCliff(string family) => family == Cliff || family == GenericCliff || family == DirtCliff || family == GroundCliff || family == PassCliff;

        /// <summary>
        /// Endless road: bushes, shrubs and ferns drawn by the instanced tree renderer on near tiles (no GameObject, no collider)
        /// instead of as pooled props.
        /// </summary>
        public static bool IsInstancedPlant(string family) => family == Bush || family == Shrub || family == Fern || family == Undergrowth || IsInstancedCrop(family);

        /// <summary>
        /// Endless road (2026-10-06): the crops of the farmland patchwork (wheat, rapeseed and sunflower clumps) are drawn instanced like
        /// the bushes; a settlement's own fields keep GameObjects (their props carry the settlement's id).
        /// </summary>
        public static bool IsInstancedCrop(string family) => family == CropField || family == Rapeseed || family == Sunflower;

        /// <summary>True for families realised as terrain tree instances rather than GameObjects.</summary>
        public static bool IsTree(string family) =>
            family == ForestTree || family == MeadowTree || family == FruitTree || family == PineTree || family == DeadTree;
    }
}
