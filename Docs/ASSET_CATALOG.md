# Asset Catalog — Synty packs in this project

Inventory of every third-party art pack under `Assets/Synty/`, grouped by *role in
MedievalZen* rather than by pack, so world-generation and scene-dressing code can pick
assets without re-scanning the folders. Verified against disk on 2026-09-26.

Fit legend: **✅ core** (medieval countryside, use freely) · **☑ usable** (fits with care or
in a specific place) · **⚠ off-theme** (modern/sci-fi/fantasy — avoid unless noted) ·
**❌ exclude** (never place).

## Packs

| Pack | Root (under `Assets/Synty/`) | Prefabs | Notes |
|---|---|---|---|
| PolygonNatureBiomes — Meadow Forest | `PolygonNatureBiomes/PNB_Meadow_Forest/` | 182 (14 FX) | The main biome. Vegetation, rocks, rural props, cabin, windmills, **28 ready-made TerrainLayers**, skybox, water, fog, post-processing profile. |
| PolygonGeneric | `PolygonGeneric/` | 495 | Generic kit: modular building pieces, characters, environment (trees, rocks, cliffs, ground tiles, roads), props (many modern), FX, tools. |
| PolygonNatureBiomes — Arid Desert | `PolygonNatureBiomes/PNB_Arid_Desert/` | 152 (16 FX) | Future dry/badlands biome: sand TerrainLayers, cacti, succulents, red rocks, bones, dirt-road pieces. Lava/sulphur/sci-fi parts are ❌. |
| PNB_Core | `PNB_Core/` | 10 | Shared sky/weather kit: skydome, cloud rings, fog ring, water plane, rain/snow FX, `WeatherControl`. |
| ANIMATION — Base Locomotion | `AnimationBaseLocomotion/` | 721 FBX clips, 4 sample prefabs | Humanoid locomotion clips (idle/walk/run/sprint, slopes, strafes, turns, starts/stops, jumps/falls/lands, head/body-look additives) for the POLYGON rig and the Sidekick rig, masculine + feminine; master Animator controllers; sample player/camera scripts and demo/gallery scenes. See the "Animation" section. |

Demo scenes worth opening for reference (lighting, fog, layer painting):
`PNB_Meadow_Forest/Scene/Demo.unity` (URP), `PNB_Arid_Desert/Scene/Demo_URP.unity`,
`PolygonGeneric/Scenes/Overview.unity` (every generic prefab laid out).

## Naming conventions and gotchas

- `SM_` static mesh prefab, `FX_` particle-system prefab, `SM_Gen_Chr_` character.
  `_01, _02…` are variants of one family; families are what the tables below list —
  glob `SM_Env_Rock_Pile_*` to get all variants.
- `Models/*_Collision.fbx` are collision meshes referenced by the prefabs — never place them.
- **Model-only assets (no prefab)** in Meadow: `SM_Env_Rapeseed_Clump_01/02` (yellow crop),
  `SM_Env_Background_Hill_01` (horizon dressing), `SM_Prop_StoneWall_01/02`, `SM_Prop_Sword_01`.
  Instantiate the `.fbx` asset directly or wrap in a first-party prefab under
  `Assets/_Project/Prefabs/Synty/`.
- Each pack uses one atlas material (`PolygonNatureBiomesMeadow_Mat_01`, `Generic_*`), so
  colour variety comes from *which* prefabs you place, not from material tweaks. Meadow also
  ships `BASEMat_02_Saturated` (punchier palette) and `NoWind_Mat_01` (vegetation without
  wind sway) as drop-in alternatives.
- Synty shaders are Shader Graph (URP-compatible). Tree prefabs have `LOD_Cards` billboards.
- Off-theme items sit next to good ones in the same folders (e.g. `SM_Prop_SciFiCrate` beside
  `SM_Prop_Camp_Crate`); always filter by the exclusion lists below.

## Terrain

### TerrainLayers (ready-made, Meadow) — `PNB_Meadow_Forest/Terrain/Terrain_Meadow_*.terrainlayer`
| Role | Layers |
|---|---|
| Lush lowland grass | `Grass_01`, patches of `Grass_Flowers_01`, `Grass_Clover_01` |
| Dry / upland grass | `Grass_02`, `Grass_Pebbles_01`, `Grass_Debris_01` |
| Forest floor | `Grass_Leaves_01`, `Grass_Leaves_Heavy_01`, `Leaves_01`, `Moss_01` |
| Roads | `Dirt_01` (accents: `Gravel_01`, `Mud_01`, `Mud_Gravel_01`, `Dirt_Cracked_*` for dry areas) |
| Settlement ground | `Dirt_01`; town centres `Cobblestone_01`; paths `Footpath_Tile_01`; ruins `Ruin_Tiles_01` |
| Steep slopes / ridges | `Rock_01`, `Rock_Rough_01`, `Rock_Moss_01`, `Rock_Rough_Moss_01`, `Moss_Rock_01` |
| Lake shores / wet ground | `Mud_01`, `Mud_Debris_01` |

Source textures (+ `_Normals`) for the same names live beside them if a custom layer is
ever needed. Unity terrains render up to 8 layers in one pass — pick ≤ 8 per world.

### TerrainLayers (Arid, for a future dry biome) — `PNB_Arid_Desert/Terrain/*.terrainlayer`
`Sand_01..04`, `RedSand`, `SaltCracks`, `SaltCracks_Purple`; ❌ `Lava`, `LavaRock`,
`Lavarock_Hot_Terrain`, `Sulphur`.

## Sky, weather, water, atmosphere

| Role | Asset | Fit |
|---|---|---|
| Skydome mesh | `PNB_Core/Prefabs/SM_Env_Skydome_01` (material `PNB_Core/Materials/Skybox_Mat_01`); Meadow-tinted `PNB_Meadow_Forest/Materials/Skybox_Meadows_Mat_01`; alt `PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Skydome_01` | ✅ |
| Cloud rings (horizon clouds) | `PNB_Core/Prefabs/SM_Env_Cloud_Ring_01/02` (`Synty_Clouds_Meadows.mat`), `PolygonGeneric/.../SM_Gen_Env_Cloud_01..03`, `SM_Gen_Env_Cloud_Ring_01` | ✅ |
| Fog ring (horizon haze) | `PNB_Core/Prefabs/SM_Env_Fog_Ring_01`; `PNB_Meadow_Forest/FX/FX_Materials/Fog_Meadows_01.mat` | ✅ |
| Weather | `PNB_Core/Prefabs/WeatherControl` (script `PNB_Core/Scripts/WeatherControl.cs`: two globals, `windIntensity` and `weatherIntensity`, pushed to the vegetation shaders), `FX_Rain_01`, `FX_Rain_Splash_01`, `FX_Snow_01` | ✅ |
| Post-processing | `PNB_Meadow_Forest/Meadows_Post_Processing_01.asset` (URP Volume profile used by the demo scene) | ✅ start here for Phase 3 |
| Water plane | `PNB_Meadow_Forest/Prefabs/SM_Env_Water_Plane_01` with `Materials/Water_Lake.mat` (lakes) or `Water_Stream.mat`; `FX/FX_Water_Movement_01` ripples; also `PNB_Core/Prefabs/SM_Env_Water_Plane_01`, `PolygonGeneric/.../SM_Gen_Env_Water_Plane_01`, `SM_Gen_Env_Water_Dip_01` | ✅ |
| Waterfalls | `PolygonGeneric/.../SM_Gen_Env_Waterfall_01/02` + `FX/FX_Waterfall_Foam_01` | ☑ (streams later) |
| Ambient particles | Meadow `FX_Butterflies_Blue/Cabbage/Lunar/Monarch_01`, `FX_Petals_Red/Yellow_01`, `FX_Leaves_Meadows_01`, `FX_Leaves_Blowing_Meadows_01`, `FX_Dust_Spots_01`, `FX_SunBeam_01`, `FX_Wind_01`, `FX_Wind_Streaks_01`; Generic `FX_Flies_01`, `FX_Leaves_01`, `FX_SunBeam_01`, `FX_Fog_01`, `LightRay_Cube/Round`; Arid `FX_Vulture_01` (circling birds), `FX_TumbleWeed_01`, `FX_Dust_Blowing_01` | ✅ |
| Fire / smoke | Generic `FX_Fire_01`, `FX_Fire_Card_Small/Large/Huge_01`, `FX_Smoke_01`, `FX_Candle_Flame_01`; Meadow `FX_Fire_01` | ✅ (campfires, chimneys, inn hearth) |
| ❌ | `FX_Blood_Splatter`, Arid `FX_Lava_Ember`, `FX_Sulphur_*`, `FX_Electricity`, `FX_Ash` | |

## Vegetation

### Trees
| Family | Path | Count | Fit / use |
|---|---|---|---|
| `SM_Env_Tree_Birch` | Meadow `Prefabs/` | 3 | ✅ highland, forest edges |
| `SM_Env_Tree_Meadow` | Meadow `Prefabs/` | 2 | ✅ lowland broadleaf, the default tree |
| `SM_Env_Tree_Fruit` | Meadow `Prefabs/` | 3 | ✅ orchards near settlements |
| `SM_Gen_Env_Tree` | Generic `Prefabs/Environment/` | 3 | ✅ extra broadleaf variety |
| `SM_Gen_Env_Tree_Pine` | Generic | 3 | ✅ highland/north slopes, dark forest |
| `SM_Gen_Env_Tree_Dead` | Generic (3), Arid (2) | 5 | ☑ bogs, burnt patches, rare |
| `SM_Gen_Env_Stump`, `SM_Gen_Env_Log`, `SM_Gen_Env_Root` | Generic | 3 / 2 / 2 | ✅ forest floor, roadside |

### Bushes, shrubs, climbers
`SM_Env_Bush_01..03`, `SM_Env_Grass_Bush_01` (Meadow); `SM_Gen_Env_Bush_01..04`,
`SM_Gen_Env_Bush_Large_01..04`, `SM_Gen_Env_Bush_Part_01..06`, `SM_Gen_Env_Shrub_01..03`,
`SM_Gen_Env_Fern_01..03` (Generic) — ✅. `SM_Gen_Env_Ivy_01..13`, `SM_Gen_Env_Ivy_Draped_01..03`,
`SM_Gen_Env_Vines_01..05` — ✅ on cabin walls, stone walls, ruins, cliffs. Arid
`SM_Env_Bush_Bramble_01..02` ☑ dry areas.

### Grass and ground cover (best as Terrain *detail* meshes)
Meadow: `SM_Env_Grass_Tall_Clump_01..04`, `Grass_Med_Clump_01..03`, `Grass_Short_Clump_01..03`,
`SM_Env_Ground_Cover_01..03`; Generic: `SM_Gen_Env_Grass_01..07`, `SM_Gen_Env_Grass_Tall_01..04`;
PNB_Core `SM_Env_Grass_01`. Native heights (measured 2026-09-26): tall clumps 1.0 m, med 0.5 m,
short 0.25 m, ground cover 0.5–1.5 m ferns — `DetailPlanner.TargetHeight` rescales them.
**❌ as terrain details (they float — rigid meshes far wider than a terrain cell):**
`SM_Env_Grass_Large_01..04` (≈34 m sculpted patches with 3–4 m of built-in height variation),
`SM_Env_Grass_Tall_Clump_05` (≈7 m patch), `Grass_Tall/Med/Short_Plane_01` (large flat cards).
They are only usable as hand-placed set dressing on flat ground.
Forest litter: Meadow `SM_Prop_Leaves_Pile_01..02`, `SM_Prop_Leaves_Branch_01`; Generic
`SM_Gen_Env_Leaves_01..03`, `SM_Gen_Env_Leaves_Pile_01..02`, `SM_Gen_Env_Twig_01..04`.
Mushrooms: Meadow `SM_Prop_Mushroom_01..06`, `Mushroom_Sparse_01..05`, `Mushroom_Group_02..05`;
Generic `SM_Gen_Env_Mushroom_01..03` — ✅ forest floor. ❌ `SM_Prop_MushroomHouse_01..02`.

### Flowers and crops (colour!)
| Family | Path | Count | Use |
|---|---|---|---|
| `SM_Env_Wildflowers` | Meadow | 3 | ✅ meadows, roadside (terrain detail, 0.64 m native) |
| `SM_Env_Wildflowers_Patch` | Meadow | 3 | ❌ as terrain detail (≈18 m sculpted patches float); hand-placed dressing only |
| `SM_Env_Flowers_Flat` | Meadow | 3 | ✅ detail layer |
| `SM_Gen_Env_Flowers` | Generic | 8 | ✅ more colours |
| `SM_Env_Sunflower_01` | Meadow | 1 | ✅ sunflower fields near villages (`Sunflowers_Mat_01`) |
| `SM_Env_Rapeseed_Clump_01..02` | Meadow **Models only** | 2 | ✅ bright yellow field rows |
| `SM_Env_CropField_Clump_01..02` | Meadow | 2 | ✅ crop rows |
| `SM_Env_Lillies_01..03`, `SM_Gen_Env_Lilypads_01..03` | Meadow / Generic | 3 + 3 | ✅ on lake water |

## Rocks, cliffs, terrain features

| Family | Path | Count | Use |
|---|---|---|---|
| `SM_Env_Rock_01..06`, `Rock_Round_01`, `Rock_Small_01`, `Rock_Small_Pile_01..02`, `Rock_Pile_01..07`, `Rock_Ground_01..02` | Meadow | 20 | ✅ everywhere; `Rock_Ground` = flat outcrops |
| `SM_Env_Rock_Cliff_01..03`, `SM_Env_Ground_Cliff_Large_01..02` | Meadow | 5 | ✅ escarpments, steep slopes |
| `SM_Env_Ground_Mound_Large_01..04` | Meadow | 4 | ✅ terrain-breaking mounds |
| `SM_Gen_Env_Rock_01..10`, `Rock_Pebbles_01..05` | Generic | 15 | ✅ |
| `SM_Gen_Env_Cliff_01..04`, `Cliff_Arch_01..02`, `Cliff_Pillar_01`, `Dirt_Cliff_01..08` | Generic | 15 | ✅ cliffs, road cuttings, river banks |
| `SM_Prop_StonePile_01..03`, `StoneStack_01..02`, `StoneRunes_01..03`, `Stone_Hole_01`, `StoneArch_01` | Meadow Props | 10 | ✅ cairns on passes, standing stones as rare landmarks, town gate arch |
| `SM_Env_Rock_*`, `Rock_Cliff_01..16`, `Rock_Arch*`, `Rocks_Spikey*`, `Crater*` | Arid | ~50 | ☑ red/sand tone — only for a dry biome |
| `SM_Gen_Env_Stalactite_01..04` | Generic | 4 | ☑ caves only |

### Horizon and backdrop (outside the playable area)
`SM_Env_Background_Hill_01` (Meadow, model only), `SM_Env_Backrgound_Hill_01..03` (Arid, sic),
`SM_Gen_Env_Background_Mountain_01..03`, `SM_Gen_Env_Mountain_01..03`, `SM_Gen_Env_Hill_01..05`
(Generic) — ✅ ring the map edge, scale up, fade with fog. ❌ `SM_Gen_Env_Skyline_01`,
`SM_Gen_Bld_Background_01..11` (city skylines).

### Modular ground tiles (Generic `Prefabs/Environment/`)
`SM_Gen_Env_Ground_Grass/Dirt(_Large)_*`, `Ground_Edge_01..05`, `Ground_Slope_Grass/Dirt_*`,
`Ground_River_Grass/Dirt(_Large)_*` — ☑ not needed with a Unity Terrain; the **river pieces**
are the route to streams later.

## Roads and paths

| Family | Path | Count | Fit |
|---|---|---|---|
| `SM_Gen_Env_Road_Gravel_Straight_01..03`, `Gravel_Corner_Large_01..02`, `Gravel_Corner_Small_01..02`, `Gravel_End_01` | Generic | 8 | ✅ dirt/gravel road meshes if painted terrain is not enough |
| `SM_Env_DirtRoad_Straight/Corner/End/Slope_Up/Slope_Down_01` | Arid | 5 | ☑ sand-coloured dirt road |
| `SM_Gen_Env_Road_01`, `Road_Half`, `Road_Small`, `Road_Crossing`, `Road_Intersection`, `Road_Parking`, `Road_Ramp`, `Road_Warning`, `Sidewalk*`, `Tyremarks*` | Generic | 21 | ❌ asphalt / modern |
| `SM_Prop_Bridge_01` (Meadow), `SM_Prop_Bridge_01..02` (Arid) | | 3 | ✅ road over stream/ditch |
| `SM_Prop_Sign_01..03` (Meadow), `SM_Prop_Sign_01..02` (Arid) | | 5 | ✅ junction signposts (Meadow first) |

## Buildings and structures

| Family | Path | Count | Fit / use |
|---|---|---|---|
| `SM_Bld_Stone_Cabin_01` | Meadow `Prefabs/Props/` | 1 | ✅ the house/inn for iteration 1 |
| `SM_Bld_Windmill_01..02` (+ `_Blades` children) | Meadow Props | 2 | ✅ villages; animate blades with `PNB_Meadow_Forest/FX/Animations/RotateX.controller` / `RotateZ.controller` |
| `SM_Prop_Well_01`, `SM_Prop_WaterWheel_01` | Meadow Props | 2 | ✅ village centre; mill by water |
| `SM_Bld_Base_*` modular kit (walls, half/quarter walls, corners, doors, windows, floors, ceilings, roofs + caps/trims, pillars, stairs, destroyed walls) | Generic `Prefabs/Base/` | ~70 | ☑ build custom houses/ruins procedurally later; plain untextured base look |
| `SM_Gen_Bld_Beam_01..03`, `SM_Gen_Bld_Ladder_01` | Generic `Prefabs/Building/` | 4 | ✅ timber details |
| `SM_Gen_Bld_Pipe_*`, `SM_Gen_Bld_Background_*` | Generic | 21 | ❌ industrial |
| `SM_Bld_Warpgate_01` | Meadow | 1 | ❌ |

### Walls, fences, gates (Meadow)
`SM_Prop_Meadow_Fence_01..07`, `Fence_Post_01..03`, `Fence_Railing_01`, `Fence_Gate_01..02`,
`Fence_Gate_Set_01..02`, `SM_Prop_Gate_Small_01` (+ `Gate_Small_Door_01`),
`SM_Prop_Stonewall_Long_01`, `Stonewall_Small_01..02`, `Stonewall_End_01`,
`Stonewall_Pillar_01..02`, `SM_Prop_StoneWall_01..02` (models) — ✅ field boundaries, town
perimeters, gate entries.

## Rural and roadside props

| Group | Assets | Fit |
|---|---|---|
| Farm | Meadow `SM_Prop_ScareCrow_01`, `HandCart_01`, `Wagon_Broken_01`, `HorseHitch_01`, `Birdhouse_01..02`, `WindChime_01..02`, `Kite_01..03` (rare, near villages) | ✅ |
| Camp (roadside rest, outside inns) | Meadow `SM_Prop_Camp_Tent_01`, `Camp_Fireplace_01..02`, `Camp_Fireplace_Stones_01`, `Camp_Fire_Tripod_01`, `Camp_Tanning_Rack_01`, `Camp_Crate_01`, `Camp_Bucket_01..02`, `Camp_Can_01..02`, `Camp_Pouch_01`; Arid `SM_Prop_Tent_01` | ✅ |
| Storage & goods | Generic `SM_Gen_Prop_Barrel_Wood_01..03`, `Crate_01..03` (+`Crate_Preset`), `Sack_01..05`, `Sack_Stack_01..02`, `Pot_01..05`, `Chest_01..02`, `Plank_01..02`, `Rope_01..04`, `Rope_Knot_01`, `Chain_01..03`, `Chain_Anchor_01`, `Hook_01` | ✅ inn yards, markets, carts |
| Furniture & tableware | Generic `Table_01`, `Chair_01`, `Shelf_01..03`, `Mug_01`, `Plate_01`, `Bottle_01..05`, `Food_Bread_01..02`, `Food_Meat_01..06`, `Food_Vegetable_01..06`, `Papers_01..06`, `Key_01`, `Coin_01`, `Coin_Pile_01..02`, `Coin_Stack_01..02`, `Potion_01` | ✅ inn interiors, market stalls |
| Monuments | Generic `Statue_01..05`, `Plinth_01..02`, `Skull_01`; Arid `SM_Prop_Bones_01..09`, `SM_Prop_Artefact_01` | ☑ town squares, shrines; bones at roadside/wastes |
| Tools | Generic `Prefabs/Weapons/`: `SM_Gen_Wep_Axe_01`, `Pickaxe_01`, `Spade_01` | ✅ farm/wood-cutter dressing |
| ❌ modern/industrial | Generic `Aircon*`, `Barrel_Metal`, `Cardboard_Box*`, `Button`, `Keypad`, `Screen`, `Switch`, `Lever`, `Clock`, `Manhole`, `Medkit`, `Light_Roof`, `Light_Wall`; Arid `Satellite`, `Solar_Panels`, `Wind_Turbine`, `Pipe`, `Hose`, `Survey_Level`, `Marker_Beacon`; Meadow `SciFiCrate_01..02`, `SciFiCable_01..02`, `SM_Veh_Steam_Car_01`, `SM_Prop_Sword_Stone_01` | |

## Characters (Generic `Prefabs/Characters/`)

| Prefab | Fit / use |
|---|---|
| `SM_Gen_Chr_Peasent_Male_01`, `SM_Gen_Chr_Peasent_Female_01` (sic "Peasent") | ✅ player and villagers |
| `SM_Gen_Chr_Prisoner_Male/Female_01` | ☑ ragged travellers, beggars |
| `SM_Gen_Chr_Underwear_Male/Female_01` | ☑ base body for custom outfits |
| `SM_Gen_Chr_Skeleton_01` | ☑ ruins/crypt dressing only |
| `Attachments/`: `Attach_Hair_04..11` (9), `Beard_01..02`, `Moustache_01`, `Chops_01`, `Bun_01`, `Ponytail_01`, `Hat_01..02` | ✅ NPC variety |
| `Attach_Beanie`, `Headset*`, `Sunglasses` | ❌ |
| `Business_*`, `Jumpsuit_*`, `Street_*`, `Space_Male`, `Robot`, `Charred` | ❌ |

Humanoid animation for these characters comes from the Base Locomotion pack (next section);
the environment packs themselves only ship `RotateX/Y/Z` prop controllers.

## Animation — Synty ANIMATION Base Locomotion (`AnimationBaseLocomotion/`)

Imported 2026-09-26. Layout:

| Folder | Contents |
|---|---|
| `Animations/Polygon/{Masculine,Feminine}/` | 171 clips each for the classic **POLYGON rig** (= `SM_Gen_Chr_*`), prefix `A_…_Masc` / `A_…_Femn`, organised as `Idles/`, `InAir/`, `Locomotion/{Crouch,Runs,Shuffles,Sprints,Turns,Walks}/`, `Transitions/…` |
| `Animations/Polygon/Neutral/` | 4 additive clips: `A_HeadLook_Additive_Neut`, `A_BodyLook_Additive_Neut`, `A_Lean_Additive_Neut` (+ MOD variant) — layer these for looking around / leaning into curves |
| `Animations/Sidekick/…` | The same set re-exported for Synty's **Sidekick** modular rig, prefix `A_MOD_BL_…`. Not needed unless Sidekick characters are added. |
| `Animations/Polygon/AC_Polygon_Masculine.controller`, `AC_Polygon_Feminine.controller` (+ Sidekick pair) | Synty's full master Animator controllers: 1 base layer, 55 blend trees, 27 parameters (see below). |
| `Samples/Animations/Polygon/…` (174 `AC_*.controller`) | One tiny controller per clip, for previewing in the Gallery scenes. Ignore in code. |
| `Samples/Scripts/` | `SamplePlayerAnimationController.cs` (1,280 lines; drives the master controller *and* owns a `CharacterController` movement loop), `SampleCameraController.cs`, `SampleObjectLockOn.cs`, `InputSystem/InputReader.cs` + `Controls.inputactions` (Move/Look/Sprint/Jump/Crouch/Aim/LockOn/ToggleWalk) |
| `Samples/Prefabs/` | `PF_PolygonPlayer`, `PF_SidekickPlayer`, `PF_SyntyCamera`, `PF_LockOnTarget` |
| `Samples/Scenes/` | `Demo_01_Polygon`, `Demo_01_Sidekick` (playable), `Gallery_01_Polygon`, `Gallery_01_Sidekick` (every clip on a pedestal) |
| `Meshes/`, `Samples/Meshes/` | `PolygonSyntyCharacter.fbx` / `SidekickSyntyCharacter.fbx` (reference rigs), demo obstacles, skydome, background |
| `Documentation/ANIMATION_BaseLocomotion_UserGuide.pdf`, `LICENSE.MD`, `ModularFbxImport.preset` | Official guide (open it in the editor/browser for parameter semantics) |

### Clip families (POLYGON rig, per gender; most exist as an in-place clip and a `…RootMotion` / `…_RM` twin)
| Group | Clips |
|---|---|
| Idles | `Idle_Standing`, `Idle_Crouching` |
| Walk | `Walk_F`, `Walk_Up25F`, `Walk_Down25F` (25° slopes), 8-way `Walk_FwdStrafe{F,FL,FR,L,R,BR}`, `Walk_BckStrafe{B,BL,BR,FL,L,R}` |
| Run | `Run_F`, `Run_Up25F`, `Run_Down25F`, same 8-way fwd/back strafes |
| Sprint | `Sprint_F`, `Sprint_Up25F`, `Sprint_Down25F` |
| Crouch | `Crouch_FwdStrafe*` / `Crouch_BckStrafe*` (8-way), `Shuffle_Crouching_{F,B,L,R}`, `Turn_Crouching_90{L,R}` |
| Shuffles | `Shuffle_Standing_{F,B,L,R}` (small repositioning steps) |
| Turns in place | `Turn_Standing_90{L,R}`, `Turn_Standing_180{L,R}` |
| Transitions | `Idle_ToWalk{F,90L,90R,180L,180R}`, `Idle_ToRun{F,90L,90R,180L,180R}`, `Walk_ToIdleF_{LFoot,RFoot}`, `Run_ToIdleF_{LFoot,RFoot}`, `Stand_ToCrouch`, `Crouch_ToStand`, `Sprint_ToCrouch` |
| In air | `Jump_{Idle,Walking,Running,Sprinting}` (+ `RootMotionVertical` twins), `InAir_Fall{Short,Large}`, `Land_Idle{Soft,Medium,Hard}` |
| Additive (neutral) | `HeadLook`, `BodyLook`, `Lean` |

### Master controller parameters (`AC_Polygon_*`)
Bools: `MovementInputTapped`, `MovementInputPressed`, `MovementInputHeld`, `IsStopped`,
`IsTurningInPlace`, `IsStarting`, `IsWalking`, `IsCrouching`, `IsJumping`, `IsGrounded`.
Floats: `MoveSpeed`, `InclineAngle`, `LocomotionStartDirection`, `StrafeDirectionX/Z`,
`ShuffleDirectionX/Z`, `ForwardStrafe`, `IsStrafing`, `FallingDuration`, `LeanValue`,
`HeadLookX/Y`, `BodyLookX/Y`, `CameraRotationOffset`. Int: `CurrentGait`.
`SamplePlayerAnimationController.cs` shows how each is fed (search `StringToHash`).

### Import facts and gotchas
- Clips are imported as **Humanoid** (`animationType: 3`, `loopTime` set on cycles) → they
  retarget to any Humanoid avatar. The target character must also import as Humanoid:
  check the rig tab of the FBX behind `SM_Gen_Chr_Peasent_Male_01` / `_Female_01` before use.
- `…RootMotion` clips move the root; the plain clips are in-place. MedievalZen's `PlayerMotor`
  is the movement authority (CLAUDE.md: one movement path), so use the **in-place** clips and
  keep `Animator.applyRootMotion = false`.
- The master controller expects the sample script's full parameter set every frame; feeding
  it partially leaves it stuck in Idle. Either port the parameter feeding (not the movement)
  from `SamplePlayerAnimationController`, or build a small purpose-made controller.
- Masculine and Feminine sets are separate clips (different posture), not a blend.

### Recommended use in MedievalZen
- **Player (iteration 1 → "Phase 2b")**: a purpose-built Animator controller generated by an
  editor script (`UnityEditor.Animations.AnimatorController` API, saved under
  `Assets/_Project/Animation/`): base layer = 1D blend tree on normalised speed
  (`Idle_Standing` → `Walk_F` → `Run_F`/`Sprint_F`) with `Walk/Run_Up25F/Down25F` blended by
  incline; `Idle_ToWalk*` starts and `Walk_ToIdleF_*` stops as short transitions;
  `Turn_Standing_90/180` for in-place turns in third person; an additive layer with
  `HeadLook`/`BodyLook` driven by the camera yaw/pitch (looking around while auto-travelling)
  and `Lean` by path curvature. Driven from `PlayerMotor` velocity/incline; no root motion.
- **NPCs later**: the same controller on peasant villagers and travellers (walk cycles,
  idles, shuffles); Feminine set for female characters.
- **Not needed**: Sidekick clips, crouch/strafe families, lock-on camera, the sample
  `Controls.inputactions` (our own `InputSystem_Actions` stays the input source).

## Arid Desert pack — quick reference for a future dry biome
✅/☑: `SM_Env_Cactus_01..03`, `SM_Env_Succulent_01..09`, `SM_Env_Bush_Bramble_01..02`,
`SM_Env_GroundCover_01..03`, `SM_Env_Tree_Dead_01..02`, rocks/cliffs/arches (red tones),
`SM_Env_Ground_Mound_Large/Small_01`, `SM_Env_Crater_01..03`, `SM_Prop_Bones_01..09`,
`SM_Prop_Tumbleweed_01` + `FX_TumbleWeed_01`, `FX_Vulture_01`, `FX_Dust_Blowing_01`,
`SM_Env_Pool_Water_01`, `Synty_SkyDome_AridDesert.mat`, `Synty_Clouds_Arid.mat`.
❌: everything lava/sulphur, `Satellite`, `Solar_Panels`, `Wind_Turbine`, `Pipe`, `Hose`.

## Suggested `WorldPalette` roles → assets (iteration 1)

| Role | Assets |
|---|---|
| trees.lowland | Meadow `Tree_Meadow_01..02`, Generic `Tree_01..03` |
| trees.highland | Meadow `Tree_Birch_01..03`, Generic `Tree_Pine_01..03` |
| trees.orchard | Meadow `Tree_Fruit_01..03` |
| trees.dead (rare) | Generic `Tree_Dead_01..03` |
| details.grass | Meadow `Grass_Med/Short_Clump_*`, `Grass_Tall_Clump_01..04`, `Ground_Cover_*` (never `Grass_Large_*`) |
| details.flowers | Meadow `Wildflowers_01..03`, `Flowers_Flat_*`, Generic `Flowers_01..08` (never `Wildflowers_Patch_*`) |
| details.crops | Meadow `CropField_Clump_*`, `Rapeseed_Clump_*` (models), `Sunflower_01` |
| forestFloor | Meadow mushrooms (15), `Leaves_Pile_*`, `Leaves_Branch_01`, `Rock_Pile_*`, `Bush_*`, Generic `Stump_*`, `Log_*`, `Twig_*` |
| rocks.meadow | Meadow `Rock_01..06`, `Rock_Round_01`, `Rock_Small*`, Generic `Rock_01..10` |
| rocks.highland | Meadow `Rock_Ground_*`, `Rock_Cliff_*`, `Ground_Cliff_Large_*`, `Ground_Mound_Large_*`, `StoneStack_*`, `StonePile_*`, Generic `Cliff_*`, `Dirt_Cliff_*` |
| landmarks (rare) | Meadow `StoneRunes_01..03`, `StoneArch_01`, Generic `Statue_*` on `Plinth_*` |
| water | Meadow `SM_Env_Water_Plane_01` + `Water_Lake.mat`, `Lillies_*`, `WaterWheel_01`, `Bridge_01` |
| buildings | Meadow `Stone_Cabin_01`, `Windmill_01..02` (+ rotate controller), `Well_01` |
| fences/walls | Meadow fence family (12), stone wall family (8), `Gate_Small_01`, `StoneArch_01` |
| settlementProps | Meadow `HorseHitch_01`, `Sign_01..03`, `ScareCrow_01`, `HandCart_01`, `Birdhouse_*`, `WindChime_*`; Generic `Barrel_Wood_*`, `Crate_*`, `Sack_*`, `Pot_*`, `Table_01`, `Chair_01`, `Chest_*` |
| roadside | Meadow `Wagon_Broken_01`, `HandCart_01`, `StonePile_*`, camp set; Arid `Bones_*` (rare) |
| horizon | Meadow `Background_Hill_01` (model), Generic `Background_Mountain_01..03`, `Hill_01..05` |
| sky/atmosphere | PNB_Core skydome + cloud ring + fog ring, `Skybox_Meadows_Mat_01`, `Meadows_Post_Processing_01`, `Fog_Meadows_01.mat`, `WeatherControl` |
| ambientFx | Meadow butterflies (4), petals (2), leaves (2), `SunBeam_01`, `Wind_01`; Generic `Flies_01`; campfire `FX_Fire_01` + `FX_Smoke_01` |
