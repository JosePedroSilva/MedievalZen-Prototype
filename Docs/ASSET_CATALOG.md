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
| POLYGON Fantasy Kingdom | `PolygonFantasyKingdom/` | 2,187 | The medieval town pack: 26 ready-made buildings (tavern, houses, church, smithy, stables, windmill), house and castle kits, village and farm props, 22 Humanoid characters, trees, hedges, a stone bridge. See its own section. |
| polyperfect — Low Poly Animated Animals | `Assets/polyperfect/` (not under Synty) | 132 animal prefabs | Animated animals with Generic rigs and URP materials, including three horses; 48 sounds. See the Animals section. |

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
ever needed. Unity terrains render up to 8 layers in one pass — pick ≤ 8 per world. (URP's Terrain Lit draws 4 layers per pass: the
endless road's pass-range tiles carry 11 layers, its mountain layers after the shared eight, so they take one more pass; every other
tile keeps 8.)

**Endless pass layers and the rock skin (2026-10-06):** the first-party `Heath` layer (Meadow grass debris texture) is now tinted
dark olive-green-brown, sRGB (80, 82, 54) (`GroundLook.LayerTints`, palette v13); the moderate pass slopes also use the shared
`Mud` layer as dark peat patches. The steep mountain rock is redrawn by a first-party triplanar overlay, material
`Assets/_Project/Resources/RockSkin.mat` (shader `MedievalZen/RockSkin`, the `MountainRock` layer's texture, normal map, tint and
6 m tile), built by the palette menu; never assign it by hand. The pass slopes' low shrubs are the countryside `Bush` / `Shrub`
roles (instanced), their crags `PassCliff` + `PassBoulder` (Fantasy Kingdom rocks, seated by `RockPlacer`).

### TerrainLayers (Arid, for a future dry biome) — `PNB_Arid_Desert/Terrain/*.terrainlayer`
`Sand_01..04`, `RedSand`, `SaltCracks`, `SaltCracks_Purple`; ❌ `Lava`, `LavaRock`,
`Lavarock_Hot_Terrain`, `Sulphur`.

## Sky, weather, water, atmosphere

| Role | Asset | Fit |
|---|---|---|
| Skydome mesh | `PNB_Core/Prefabs/SM_Env_Skydome_01` (material `PNB_Core/Materials/Skybox_Mat_01`); Meadow-tinted `PNB_Meadow_Forest/Materials/Skybox_Meadows_Mat_01`; alt `PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Skydome_01` | ✅ |
| Cloud rings (horizon clouds) | `PNB_Core/Prefabs/SM_Env_Cloud_Ring_01/02` (`Synty_Clouds_Meadows.mat`), `PolygonGeneric/.../SM_Gen_Env_Cloud_01..03`, `SM_Gen_Env_Cloud_Ring_01` | ✅ |
| Cloud shader quirks | `PNB_Core/Shaders/Clouds.shadergraph` is URP Unlit and **opaque** (writes depth). Its fog term is an unclamped lerp toward `unity_FogColor` and its scattering weight is per-channel, so default colours render as saturated blue/pink. In this project both are disabled per renderer and colours come from `CloudColorModel` | ☑ |
| Fog ring (horizon haze) | `PNB_Core/Prefabs/SM_Env_Fog_Ring_01`; `PNB_Meadow_Forest/FX/FX_Materials/Fog_Meadows_01.mat` | ✅ |
| Weather | `PNB_Core/Prefabs/WeatherControl` (script `PNB_Core/Scripts/WeatherControl.cs`: two globals, `windIntensity` and `weatherIntensity`, pushed to the vegetation shaders), `FX_Rain_01`, `FX_Rain_Splash_01`, `FX_Snow_01` | ✅ |
| Post-processing | `PNB_Meadow_Forest/Meadows_Post_Processing_01.asset` is a **Post Processing Stack v2** profile, not a URP `VolumeProfile` — it does nothing in this project. Grading comes from `Assets/_Project/Settings/Atmosphere/TimeOfDay.asset` | ❌ |
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
`SM_Gen_Env_Vines_01..05` — ✅ on cabin walls, stone walls, ruins, cliffs (the endless woods use `Ivy_Draped` and the Fantasy Kingdom ivy; see that section). Arid
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
| `SM_Env_Rapeseed_Clump_01..02` | Meadow **Models only** | 2 | ✅ bright yellow field rows — but only with the first-party material: the palette (v15) bakes `Assets/_Project/Materials/Details/Rapeseed_Field.mat`, the Synty `Flowers_Field_Mat_01` with its leaf texture swapped for `Rapeseed_Flowers.png` (`Flowers_01.tga` with the pale heads recoloured to `GroundLook.RapeseedFlower`, sRGB (237, 204, 41)). With `Flowers_Field_Mat_01` as such the heads are white and a field reads as snow (review 2026-10-06). Endless: wheat, rapeseed and sunflower clumps of the patchwork are drawn instanced (`PropRole.IsInstancedCrop`) |
| `SM_Env_CropField_Clump_01..02` | Meadow | 2 | ✅ crop rows |
| `SM_Env_Lillies_01..03`, `SM_Gen_Env_Lilypads_01..03` | Meadow / Generic | 3 + 3 | ✅ on lake water |

- Endless forest floor (2026-10-06): no new assets. Under the canopy the paint mixes the existing forest-floor (Grass_Leaves), mud and
  dirt layers (`GroundLook.WoodFloorShares`); away from the road the understorey uses only the light ferns (catalogue 1-3) and the
  Generic bushes 1-4 and large bushes 2 and 4 (large 1 and 3 are 1,058-1,190 triangles); logs (Stump 3-4) and `MushroomGroup` in the wood.
- Endless leaf litter (2026-10-06): endless channel 14, `_Project/Settings/TerrainLayers/LeafLitter.terrainlayer`, a first-party copy of Meadow
  `Terrain_Meadow_Grass_Leaves_Heavy_01` (texture mean linear 0.167, 0.124, 0.022) tinted to (0.125, 0.079, 0.023), sRGB (99, 79, 42), by
  `GroundLook.LayerTints`; under the crowns it leads the floor (`GroundLook.LeafLitterShares`). A wood's tiles draw it in the cobblestone's
  slot (`EndlessLayerSets`), so they keep two terrain passes.

## Rocks, cliffs, terrain features

| Family | Path | Count | Use |
|---|---|---|---|
| `SM_Env_Rock_01..06`, `Rock_Round_01`, `Rock_Small_01`, `Rock_Small_Pile_01..02`, `Rock_Pile_01..07`, `Rock_Ground_01..02` | Meadow | 20 | ✅ everywhere; `Rock_Ground` = flat outcrops |
| `SM_Env_Rock_Cliff_01..03` | Meadow | 3 | ✅ cliff blocks 8-10 m tall (5x8x5, 12x8x8, 22x10x9 m); seat them against rising ground, never on crests |
| `SM_Env_Ground_Cliff_Large_01..02` | Meadow | 2 | ⚠ NOT a wall: a raised disc ~40 m across with its pivot at the top and a 4-8 m cliff edge hanging down all round. Fits level ground only; on a slope or summit it hangs in the air. Not scattered |
| `SM_Env_Ground_Mound_Large_01..04` | Meadow | 4 | ✅ terrain-breaking mounds |
| `SM_Gen_Env_Rock_01..10`, `Rock_Pebbles_01..05` | Generic | 15 | ☑ lowland only (dark atlas colour clashes with Meadow rock). `Rock_01..03` and `08..10` have pivots 9-18 m away from the mesh: always centre by bounds |
| `SM_Gen_Env_Cliff_01..04`, `Cliff_Arch_01..02`, `Cliff_Pillar_01`, `Dirt_Cliff_01..08` | Generic | 15 | ⚠ large (cliffs 8-15 m wide, 10-14 m tall; dirt cliffs up to 25 m) and a different colour family from Meadow rock. Catalogued but not used in the mountains |
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
Measured 2026-10-02 (FBX; the pieces lie along +X from a pivot at one end): `Stonewall_Long_01` 5.37 x 1.24 x 0.72 m with LODs
2,674 / 1,662 / 852 tris; `Stonewall_Small_01/02` 2.85 m, 1,326 / 844 / 428; `Stonewall_Pillar_01/02` 0.8-0.9 x 1.4 m;
`Meadow_Fence_01/03/05/06/07` 2.5-2.7 x 1.5 m, 238-312 tris, no LODs (`_02/_04` are 1.3 m and dropped by the palette); posts
74-166 tris; `Fence_Gate_01` 2.28 m (its pivot is the hinge), `_02` 1.0 m. The endless road's field boundaries draw these (and
the Fantasy Kingdom hedge) instanced, see `Docs/ENDLESS_ROAD.md`. Rune stones `StoneRunes_01..03`: 1.3-1.6 x 0.6-1.0 m, 3.0-3.2 m
tall with 0.15-0.35 m below the pivot (standing-stone rings).

## Rural and roadside props

| Group | Assets | Fit |
|---|---|---|
| Farm | Meadow `SM_Prop_ScareCrow_01`, `HandCart_01`, `Wagon_Broken_01`, `HorseHitch_01`, `Birdhouse_01..02`, `WindChime_01..02`, `Kite_01..03` (rare, near villages) | ✅ |
| Picked by variant index | The generator selects some variants by index: cart variant 1 = `SM_Prop_Wagon_Broken_01` (lowest hairpin), stump-family variants 3-4 = logs (stacked by the mountain inn) | ✅ |
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
- The peasant's own Synty avatar (`PolygonGeneric/Models/Generic_Characters.fbx`, Humanoid) maps human
  Hips to the ground-level `Root` bone, so hip motion is lost; `MedievalZen/Player/Build Locomotion
  Controller` builds a corrected copy (`Assets/_Project/Animation/PeasantMale_Humanoid.asset`). The pack's
  reference rig (`Meshes/PolygonSyntyCharacter.fbx`) is NOT a usable avatar source: same bone names but
  different rest rotations and finger names.
- Strafe and backward clips have the same natural speeds as the forward ones (walk 1.452 m/s, run
  2.585-2.589 m/s, all eight directions); the player controller uses them in a 2D blend on
  body-relative velocity.
- Natural ground speeds of the forward cycles (measured from the root-motion twins): Walk_F 1.45 m/s,
  Run_F 2.59 m/s, Sprint_F 7.24 m/s. Scale playback by ground speed ÷ natural speed to avoid foot sliding.

### Recommended use in MedievalZen
- **Player (iteration 1 → "Phase 2b")**: a purpose-built Animator controller generated by an
  editor script (`UnityEditor.Animations.AnimatorController` API, saved under
  `Assets/_Project/Animation/`): base layer = 1D blend tree on normalised speed
  (`Idle_Standing` → `Walk_F` → `Run_F`/`Sprint_F`) with `Walk/Run_Up25F/Down25F` blended by
  incline; starts/stops are blend-tree cross-fades (the `Idle_ToWalk*` / `Walk_ToIdleF_*` clips last ~1 s, longer
  than the motor's ~0.3 s acceleration, so they would slide);
  `Turn_Standing_90/180` for in-place turns in third person; an additive layer with
  `HeadLook`/`BodyLook` driven by the camera yaw/pitch (looking around while auto-travelling)
  and `Lean` by path curvature. Driven from `PlayerMotor` velocity/incline; no root motion.
- **NPCs later**: the same controller on peasant villagers and travellers (walk cycles,
  idles, shuffles); Feminine set for female characters.
- **Not needed**: Sidekick clips, crouch families, lock-on camera, the sample
  `Controls.inputactions` (our own `InputSystem_Actions` stays the input source).

## POLYGON Fantasy Kingdom (`Assets/Synty/PolygonFantasyKingdom/`)

Imported 2026-10-02. The medieval town pack: 2,187 prefabs, 305 MB. Third-party: reference it, never edit it.
Verified against disk. Prefab paths below are under `PolygonFantasyKingdom/Prefabs/`.

### Technical facts
- **Materials are URP-ready**: 45 of 51 use the Synty `Generic_Basic` / `Generic_Standard` Shader Graphs that
  PolygonGeneric already uses; the other 6 are particle materials.
- **One shared atlas with colour alternates**: `Materials/Alts/PolygonFantasyKingdom_Mat_01_A` .. `_04_C` (four
  palettes, three alternates each). Swapping the material on a building or prop recolours it, which is the cheap
  way to vary a village.
- **No LODGroups anywhere** (trees, buildings, props are single meshes). Never use these trees as Unity terrain
  trees (legacy billboard path, see CLAUDE.md); the endless mode's instanced tree renderer is fine with them.
- **Building presets are exterior shells**: one merged mesh plus a glass child and one concave `MeshCollider`.
  Doors do not open and there is no interior. Interiors exist only as the modular kit.
- **Characters are Humanoid** on the POLYGON rig (`Models/FantasyKingdom_Characters.fbx`), so the Base Locomotion
  clips and the project's `PlayerLocomotion.controller` should drive them. The avatar's hips mapping must be
  checked the same way as for the peasant (see Animation).
- **Sizes are not recorded here**: measure each prefab before placing (as `PropMetrics` does for rocks).
- **No live horse.** `Materials/Misc/Horse_01..03` and `SM_Chr_Rider` exist, but the only horse meshes are in
  `DeadBodies`. Horses come from the polyperfect pack.
- Demo scenes: `Scenes/Demo.unity` (full town), `Demo_ExteriorOnly_Optimized.unity`,
  `Demo_Layout_Houses_With_Interiors.unity`, `Overview.unity` (every prefab laid out). Worth opening to see how
  Synty composes a village.
- The import re-saved one shared file, `PolygonGeneric/Materials/Generic_Mountains.mat`. Harmless.

### Ready-made buildings (`Buildings/Presets/`, 26) ✅
| Use | Prefabs (`SM_Bld_Preset_..._Optimized`) |
|---|---|
| The inn | `Tavern_01` |
| Houses | `House_01_A`, `House_02_A`, `House_03` .. `House_08`, `House_09_A/B/C`, `House_10` (13 in all) |
| Cottages and outbuildings | `Hut_01`, `Hut_02`, `Outhouse_01`, `Shelter_01`, `Shelter_02` |
| Trades | `Blacksmith_01`, `Stables_01`, `House_Windmill_01` (separate blades child: can turn) |
| Church | `Church_01_A`, `Church_01_B` |
| Over the road | `House_Archway_01`, `House_Archway_02` (a house with an arch to walk through: a village gate) |
| Landmark | `Tower_01` |

Also in `Buildings/`: tents (`SM_Bld_Tent_Large`, `_Open` x2, `_Round`, `_Single`, `_Small`, `_Square`; burnt
variants ❌), `SM_Bld_Wooden_Tower` x2 (watchtower), `SM_Prop_Destroyed_House*` (18 pieces; a ruin by the
road ☑, burnt variants ⚠).

### Modular kits ☑
| Kit | Folder | Count | Contents |
|---|---|---|---|
| House kit | `Buildings/House/` | 241 | Timber-frame walls (plain, window, door, window box, half, peak), thatch and tile roofs (full sets of edges, corners, caps, dormers), chimneys x15, floors, stairs, railings, stone and wood arches, round and angled towers, support beams. Enough to build any house with an interior |
| Castle kit | `Castle/` | 348 | Walls, corners in three sizes, battlements (6 styles), arrow slits, gates and doors, drawbridge x2, wall towers S/M/L, round stairs, roofs S/M/L with every edge piece, hoardings, walkways, pillars, floors, **ruined wall pieces with rubble** (`SM_Bld_Castle_DestroyedWall_*`), a covered timber bridge (`SM_Bld_Bridge*`), `SM_Bld_Waterwheel` |

Use the kits for a landmark seen from the road (a castle or a ruin on a hill) or for inn interiors later. Assembling
them procedurally is real work; the presets cover villages without it.

### Trees, plants and ground (`Environments/`, 189; `Generic/`, 31)
| Group | Prefabs | Fit |
|---|---|---|
| Trees | `SM_Env_Tree_Round_01..04`, `Tree_Thin_01..06`, `Tree_Large_01..02`, `Tree_Pine_Cluster_01..04`, `Big_Tree_01` (+ `_Base`, `_Base_Paving`: a village-green tree with a seat round it); `SM_Generic_Tree_01..04`, `TreeStump` | ✅ a second tree style beside Meadow's |
| Dead and burnt | `Tree_Dead_01..04`, `Tree_Burnt_01..02`, `SM_Generic_TreeDead` | ☑ rare |
| Bushes and hedges | `Bush_01..06`, `Bush_Cluster_01..06`, `Bush_Flowers_01..06`, `Bush_Small_01..05`, `Hedge_01..04` | ✅ hedgerows along fields and lanes |
| Flowers and small plants | `Flowers_01..24`, `Sunflower_01..03`, `Fern_01..04`, `Grass_Tuft_01..03`, `Reeds_01..03`, `Lily_01..03`, `Leaves_01..03` | ✅ reeds and lilies for streams and lake shores |
| Climbers | `Ivy_01..09`, `Ivy_Corner_01..02`, `Ivy_Pot`, `Grass_WallEdge_01..02` | ✅ on walls and ruins |
| Garden pieces | `Planter*` (17), `Fountain_01`, `SM_Prop_PlanterBox*` (11) | ✅ village greens, inn yards |
| Rocks | `Rock_01..04`, `Rock_Chunk_01..03`, `Rock_Cliff_01..05`, `SM_Generic_Small_Rocks_01..02` | ☑ a different rock look from Meadow's: do not mix in one view; seat with `RockPlacer`. The endless road's pass ranges use `Rock_Cliff_01..05` and `Rock_Chunk_01..03` (see "mountain passes" below) |
| Walls | `StoneWall_01..03`, `StoneWall_Pillar` | ✅ field and churchyard walls |
| Water | **`Bridge_Stone_01`** (8 colliders, built to be walked on) + `Bridge_Stone_Path_01`, `River_Edge_01..03`, `Riverbed_01`, `Water_Plane`, `Water_Slope` | ✅ the stone bridge is the best bridge in the project (measured 2026-10-06: 20 x 5.68 m, deck 0.24 m above the pivot at its ends and 1.92 m at the crown, 47 % hump, one arch 4.8 m wide with its soffit 1.24 m up; the endless road stretches it 1.12 x 0.85 x 1.35, see `BridgeShape`) |
| Farm ground | `Ground_Farm_Flat`, `Ground_Farm_Row_01..05` (ploughed furrows) | ✅ fields |
| Paths | `Path_01..05`, `Path_Brick_Group_01..07`, `Path_Raised`, `Path_StepEdge`; `Props/Paths/` (27: brick, rock, steps) | ✅ worn paths to doors, stepping stones at fords |
| Ground tiles, ramps, craters | `Ground_Flat*`, `Ground_Raised`, `Ground_Ramp`, `Ground_Crater_01..03`, `SM_Generic_Ground_01..09`, `Structure*` | ☑ not needed with terrain |
| Backdrop | `SM_Generic_Mountains_Grass_01..02`, `Mountains_Snow_01..02`, `Cloud_01..04`, `CloudRing` | ✅ snow mountains on the horizon |

### Village and farm props (`Props/`, 499 + subfolders)
| Group | Prefabs | Fit |
|---|---|---|
| Market and trade | `Market_Stall_01..09`, `Market_Tray_01..03`, `Market_Label`, `Awning_01..07`, `Bread_Crate`, `Bucket_Fish`, `Basket_01..02` | ✅ a market in a village |
| Signs | `Sign_Wood_01..15`, `Sign_Iron_01..10`, `Sign_Bracket_01..07`, `Sign_Chain`, `Sign_Ornate`, `Direction_Sign_01..02`, `Quest_Board` (+ posters) | ✅ inn and shop signs, signposts |
| Farm | `Hay_Bale`, `Hay_Cart`, `Hay_Pile_01..03`, `Scarecrow`, `Plow_01..02`, `Wheat_Bunch`, `Pumpkin_01..03`, `Pumpkin_Plant`, `Cabbage_01..02`, `Beehive`, `Windmill_01..03` | ✅ |
| Yard | `Well_01` (+ `Well_Plinth`), `Horse_Hitching_Post`, `Wood_Pile_01..03`, `Log_01..02`, `Ground_Logs`, `Barrel`, `Barrel_Stack_01..04`, `Crate_Wood`, `Crate_Stack_01..04`, `Sack_01..06`, `Sack_Stack`, `Ladder*`, `Grinding_Wheel`, `Fence_01..04`, `Fence_Gate` | ✅ |
| Washing and street | `WashingLine*` (5), `Hanging_Clothes*` (15), `Street_Lamp_01..02`, `Torch_01..04`, `Bracket_Lamp`, `Bunting_01..04`, `Bell`, `Weathervane` | ✅ lamps and torches can carry the night lights |
| Fire | `FirePit`, `BattleGround/Camp_Firepit`, `Camp_Brazier`, `Camp_Stove_01..02`, `Camp_Lanturn_01..03`, `Spit_Roaster`, `Cooking_Rack`, `Cauldron_01..04` | ✅ camps and inn yards |
| Churchyard and monuments | `Gravestone_01..05`, `Gravestone_Broken`, `Cross`, `Statue_Angel/Hero/King/Knight` on `Statue_Plinth_01..05`, `Gazebo` | ✅ quiet places to sit |
| Water's edge | `Boat`, `Oar`, `Vehicles/SM_Veh_Boat_Dinghy` | ✅ on lake shores |
| Seats | `Furniture/Bench_Seat_01..06`, `Bench_Seat_Pew`, `Stool_01..06`, `Chair_Wood_01..05`, `Camp_Chair`, `Table_Log`, `Table_Barrel` | ✅ add to `SeatCatalog` |
| Banners and flags | `Banners/` (43): `Banner_01..04`, `Flag_01..20`, `Flag_Horizontal`, `Flag_Pole_01..03`; four symbols in four colours | ☑ a festival, a castle |
| Interiors | `Furniture/` (116: beds, tables, cabinets, wardrobes, chests, stoves, thrones), `Bar*`, `Cooking_*`, `Shelf`, `Rug*`, `Curtain*`, `Chandelier*`, `Painting*`, `Mirror*`, `Books*`, `Candle*` | ☑ only with interiors |
| Blacksmith | `Anvil_01..03`, `Forge_01..04`, `Bellows`, `Workbench_Forge`, `Ore_*` | ☑ beside the Blacksmith preset |
| Trophies and shop dressing | `Animal_Head_*`, `Animal_Pelt*`, `Mannequin*`, `Jewellery_Display*`, `Knight_Armor` | ⚠ |
| Grim or violent | `Gallows*`, `Guillotine`, `Cage*`, `BloodPool*`, `Poster_Plague`, `Sign_Plague`, all of `DeadBodies/` (44), most of `BattleGround/` (arrows, spikes, relics, training dummies) | ❌ wrong mood |
| Fantasy | `Fairy_House*`, `Crystal`, `Planetarium`, `Alchemy_*`, `Fireworks*`, `Animal_Head_Troll`, `Zulu_Shield` | ❌ |

### Vehicles, items, weapons
| Folder | Contents | Fit |
|---|---|---|
| `Vehicles/` (12) | `SM_Veh_Cart_01..04`, `Cart_Damaged`, `TraderWagon`, `Wheelbarrow`, `Boat_Dinghy` | ✅ on the road and in yards; `Cart_Fireworks`, `Arrow_Guard`, `Hot_Oil` ❌ |
| `Items/` (260) | Food (bread, cheese, pies, meat, fish), tableware, bottles and jugs, tools (`Pitchfork`, `Shovel`, `Saw`, `Hammer`, `Broom`, `Bucket`, `Watering_Can`, `Pickaxe`), books, keys, candlesticks | ☑ small dressing for stalls, tables, sheds. Jewels, crystals, war-game pieces ⚠ |
| `Weapons/` (183), `SiegeEngines/` (15) | Swords, axes, spears, shields, bows, modular weapons; catapult, trebuchet, ballista, cannon, siege tower | ❌ no combat in this game |

### Characters (`Characters/`, 22 + 10 attachments)
| Fit | Prefabs (`SM_Chr_...`) |
|---|---|
| ✅ Villagers and travellers | `Peasant_Male`, `Peasant_Female`, `Merchant`, `Bartender`, `Blacksmith_Male`, `Blacksmith_Female`, `Monk`, `Nun`, `Priest`, `Hermit`, `Rider` |
| ☑ Occasional | `Soldier_Male`, `Soldier_Female` (a patrol, a gate guard), `Jester`, `FortuneTeller` (a fair) |
| ⚠ Court | `King`, `Queen`, `Prince`, `Princess` (only with a castle) |
| ❌ | `Mage`, `Fairy`, `Headsman` |

`Rider` or `Peasant_Male` are candidates for the traveller; the rest make villagers and people met on the road.

### Effects (`FX/`, 21)
✅ `FX_Smoke_Chimney`, `FX_Smoke_Light`, `FX_Fire`, `FX_CandleFlame_Single`, `FX_Fog_Big/Small`, `FX_Dust_Big/Small`,
`FX_SunBeams`, `FX_Fountain`, `FX_WaterWheel`, `FX_Rain`. ❌ arrow and catapult barrages, `FX_Magic_Lights`,
`FX_Smoke_Dark`.

### Textures worth knowing
`Textures/Ground/`: `Grass_01` (+ `_Dark`), `Grass_02`, `Grass_03`, `Mud_01..03`, `Sand_01..03` with normal maps:
candidates for extra terrain layers. `Textures/Castle/`: wall and roof tiles for the castle kit.

### Used by the endless road's landmarks and field boundaries (2026-10-02, palette v9)
Measured offline from the FBX (bounds by height band, atlas colours at the triangles' UVs); the palette menu logs the bounds it
finds ("Endless road landmark and boundary families") and warns about any family that resolves to nothing.

| Family (`PropRole`) | Prefabs | Measured |
|---|---|---|
| `LandmarkTower` | `Buildings/Presets/SM_Bld_Preset_Tower_01_Optimized` | 9.0 x 9.4 m shaft to 14.5 m, a narrow cap to 20.7 m; 33k tris; base 0.75 m below the pivot; stone (110, 104, 97), roof (116, 82, 57) |
| `LandmarkChapel` | `Church_01_A_Optimized`, `Church_01_B_Optimized` | nave 6.4 x 10.9 m, eaves 3.55 m, ridge 7 m, bell tower 2.9 m square, spire to 9.2 / 11.7 m; foundation 2.51 m below the pivot, which sits near one corner (the family is centred) |
| `LandmarkMill` | `House_Windmill_01_Optimized` | house 7.9 x 8.4 m, eaves 6.3 m, steep roof to 16.9 m, sails (child `..._Blades_Optimized`, hub at 10.25 m, arm 6.7 m) turned by `RotateZ` |
| `LandmarkRuin` | `Buildings/SM_Prop_Destroyed_House_Preset_01/02` | 8.4 x 8.4 / 8.4 x 5.9 m, walls 2.9 / 3.5 m; 8 mesh colliders; pivot at a corner (centred) |
| `Gravestone` | `Props/SM_Prop_Gravestone_01..05`, `Gravestone_Broken_01..02`, `Cross_01` (last variant) | stones 0.85 x 1.3-1.6 m; the cross 2.1 x 3 m |
| `Haystack` | `Props/SM_Prop_Hay_Pile_01`, `Hay_Pile_03` | 2.9 x 2.5 x 3.1 m and 3.9 x 1.5 x 3.3 m (`_03`'s mesh is 4 m off its pivot: centred). Not `Hay_Bale_01` (pivot at its middle) or the flat `Hay_Pile_02` |
| `WoodPile` | `Props/SM_Prop_Wood_Pile_01..03` | 1-2 m long |

(Inn archetypes, 2026-10-06: no new families. The endless inns reuse `Stables`, `Smithy`, `Shelter` 0/1, the larger `House` presets as the
inn, `SmithyTool`, `TanningRack` (the woodcutter's drying rack), `Reeds` (the waterside landing), `MeadowTree`/`FruitTree` as dressing
trees, `Haystack`, `WoodPile`, `FarmCart`, `Fence`. A jetty or a trough would need a new measured family.)
| `Hedge` | `Environments/SM_Env_Hedge_01` | 5.03 x 1.41 x 1.05 m, 164 tris, no collider; laid along +Z (yaw offset −90°) and centred like the fences. `Hedge_02..04` (3.1, 2.1, 1.05 m) are not used |

Considered and left out: the castle kit's `DestroyedWall_*` pieces (2.5 m wide slabs meant for a castle wall's breach, a keep from
them is dozens of pieces), `SM_Bld_Wooden_Tower_01/02` (6.7-7.3 m: too small for the horizon), `Castle_Wall_Tower_S/M/L` (7.5 m).

### Used by the endless road's woods (forest pass, 2026-10-05, palette v10)
Measured offline from the FBX (`Measure lods`); the palette menu logs each family's prefabs, triangles and bounds ("Endless road woods
families") and warns about any that resolved to nothing.

| Family (`PropRole`) | Prefabs | Measured |
|---|---|---|
| `Fern` (instanced, drawn to 160 m) | Generic `SM_Gen_Env_Fern_01..03`, Fantasy Kingdom `Environments/SM_Env_Fern_01`, `_03` | 1.2-2.0 m wide, 0.6-1.8 m tall; 248-546 triangles; pivots centred |
| `Undergrowth` (instanced) | Generic `SM_Gen_Env_Bush_01..04`, `Bush_Large_01..04` | 2.6-6.3 m wide, 1.4-3.0 m tall; 266-1,190 triangles; Generic atlas colour (98, 114, 76) |
| `Ivy` (instanced piece, no collider) | Fantasy Kingdom `SM_Env_Ivy_01..03`, `_05`, `_06` | flat panels 1.4-2.7 m wide, 2.1-4.2 m tall, centred in height, standing 0.03-0.3 m out along +Z of the pivot plane; 56-187 triangles |
| `IvyDrape` (instanced piece, no collider) | Generic `SM_Gen_Env_Ivy_Draped_01..03` | 0.9-3.2 m wide, hangs up to 1.7 m below its pivot, -0.5..+0.26 m in Z; 64-116 triangles; `Generic_Ivy.mat` |

Tree LOD triangles (LOD0 / LOD1 / LOD2 / card): Meadow `Tree_Meadow_01` 25,278 / 12,700 / 6,562 / 12, `Tree_Meadow_02` 11,872 / 6,464 /
3,500 / 12, `Tree_Birch_01..03` 11,162 / 5,710 / 2,892 (01), 11,580 / 5,814 / 2,831 (02), 2,945 / 1,408 / 684 (03); Meadow bushes
`Bush_01` 2,276 / 1,366, `Bush_02` 2,326 / 1,488, `Bush_03` 5,894 / 2,914 / 1,456; Generic trees 530-724 (one mesh), pines 374-559.
Meadow stone wall `Stonewall_Long_01`: 5.2 m along X, 1.17 m tall, 0.72 m thick (faces at ±0.36).

Considered and left out:
- **Generic broadleaves** (`SM_Gen_Env_Tree_01..03`): faceted light grey-green blobs that read as placeholder impostors beside the
  detailed Meadow trees (review tour 2026-10-05). The endless road draws them as birches or the small oak within 150 m of the road.
- **Meadow `SM_Env_Bush_03`**: its material is the oak leaf (`Tree_Mat_03`, leafPatch_04, mean sRGB about 60, 65, 7); a dense bush of
  it is nearly black in its own shade at 20-60 m. The endless road draws it as a Generic bush.
- **Fantasy Kingdom `FX_SunBeams`, `FX_Dust_*`, `FX_Fog_*`** and Meadow `FX_SunBeam_01`, `FX_Dust_Spots_01`: their materials use
  shaders from Unity's built-in resources (file ids 200/202: the legacy particle shaders), not URP or Shader Graph ones; how URP draws
  them was not checked in the editor. The forest shafts and dust are code-built
  particle systems on the first-party additive glow material instead; `FX_Fog_01` (Generic, Shader Graph) stays the mist.
- Fantasy Kingdom bushes and trees (`Bush_*`, `Bush_Cluster_*`, `Tree_Large_*`): pivots hundreds of metres from the mesh.
- Generic `SM_Gen_Env_Vines_01..05`: no readable mesh in the FBX the prefab points to (0 triangles measured offline).

### Used by the endless road's mountain passes (2026-10-05, palette v11)
The pass ranges have one grey rock look: grey terrain rock, pale scree, olive-brown heath and the Fantasy Kingdom rocks (flat grey
atlas swatch). Measured offline from the textures and FBX; the palette menu logs what it built ("Endless road mountain terrain
layers", "Endless road mountain pass families") and whether every layer and family resolved.

| Asset (first-party, built by the palette menu) | Source | Measured / set |
|---|---|---|
| `Settings/TerrainLayers/MountainRock.terrainlayer` (splat channel 8) | Meadow `Terrain_Meadow_Rock_Rough_01` (texture mean sRGB 132, 123, 111) | 6 m tiles, tint (1.006, 1.160, 1.370): neutral grey sRGB (132, 132, 129), L* 55 like the warm `RockWall` layer (148, 128, 107; b* 14) |
| `Settings/TerrainLayers/Scree.terrainlayer` (channel 9) | Meadow `Terrain_Meadow_Gravel_01` (129, 124, 116; strong pebble pattern) | 4 m tiles, tint (1.464, 1.500, 1.539): pale gravel (154, 149, 141), L* 62 |
| `Settings/TerrainLayers/Heath.terrainlayer` (channel 10) | Meadow `Terrain_Meadow_Grass_Debris_01` (107, 113, 41) | 3 m tiles, tint (0.990, 0.763, 2.204): olive-brown turf (107, 100, 63), L* 42 |

| Family (`PropRole`) | Prefabs (Fantasy Kingdom `Environments/`) | Measured |
|---|---|---|
| `PassCliff` | `SM_Env_Rock_Cliff_01..05` | 9.1 x 18.5 x 9.8, 11.5 x 20.6 x 12.4, 9.9 x 13.8 x 8.9, 16.6 x 9.7 x 20.3, 6.3 x 5.9 x 6.4 m; 968 / 986 / 958 / 1,999 / 200 triangles; one mesh, a mesh collider, pivots 0.5-4.8 m above the base; seated 5-9.5 m tall |
| `PassBoulder` | `SM_Env_Rock_Chunk_01..03` | 1.7 x 1.3 x 1.9, 2.7 x 2.0 x 3.3, 3.7 x 3.1 x 3.3 m; 82 / 152 / 228 triangles; pivot at the centre; seated 0.8-3 m |

- Atlas colour of both families (`PolygonFantasyKingdom_Mat_01_A`): cliffs and chunks sRGB (99, 96, 92), the small `Rock_01..04` (86, 84,
  79): the distant solids use (99, 96, 92). Their names repeat the Meadow rocks', so `PropMetricsTable` keys them with the pack.
- Pines: Generic `SM_Gen_Env_Tree_Pine_01..03` (559 / 488 / 374 triangles, 9.5 / 6.9 / 6.8 m), as before.
- The Meadow rocks (`Rock_*`, `Rock_Pile_*`, `Rock_Ground_*`, `Rock_Cliff_*`) keep the warm `RockWall` texture with grass tops: they stay
  in the lowlands and the outer foothills (pass strength under 0.35) and are not mixed with the grey rocks in a range.
- Considered and left out: Meadow `Rock_01` / `Moss_Rock_01` / `Rock_Moss_01` terrain textures (darker, mossy: read as green-brown from
  afar), Generic cliffs (dark atlas, a third rock look), the Fantasy Kingdom `Rock_01..04` pebbles (0.3-0.6 m: too small to read).

### Used by the endless road's inns, hamlets and villages (2026-10-05, palette v12)
Measured offline from the FBX behind each prefab (Unity axes, the prefab's pivot space; session tools in the scratchpad `villages/`), kept in
`BuildingTable` (footprints, door feet, chimneys, colours) and logged by the palette menu ("Endless road inn and village families").
The presets stand on a stone base: the ground is meant at the pivot (a ledge at y = 0 where the door steps start, the floor 1.2-1.3 m up,
the base 2.5 m deep below the pivot; in Synty's demo town the ground lies 0.4-0.8 m above the pivot); the composer sinks the pivot 0.55 m
under the highest ground of the body. Doors face local +Z (the steps rise from z > 0) except `Hut_01` (+X, a deck 1.2 m up) and the
arch house (+X, its lane). Only a few presets have a stone chimney (`House_03/05/07/09_*`, `Blacksmith_01`); the others smoke from a vent
near the ridge.

| Family (`PropRole`) | Prefabs (`Buildings/Presets/SM_Bld_Preset_*_Optimized` unless noted) | Measured (body = ground-floor walls; extent with roofs and steps; triangles) |
|---|---|---|
| `Tavern` | `Tavern_01` | body 10.2 x 7.8 m, extent 14.3 x 14.4 m, 10.5 m tall, eaves 3.8 m; balcony on posts and an outside stair on +Z; 34,559 tris |
| `House` | `House_01_A`, `02_A`, `03`, `05`, `07`, `08`, `09_A/B/C` | bodies 4.0-10.2 m wide, 4.7-10.1 m deep; 6.7-11.9 m tall; 9,815-25,887 tris (mean 17,500) |
| `Hut` | `Hut_01`, `Hut_02` | 6.9 x 7.3 / 3.5 x 6.8 m, 6.7 m tall; 8,562 / 5,521 tris (the refuge and the ends of a place) |
| `Outhouse` | `Outhouse_01` | 3.9 x 3.6 m, 4.5 m, stands on feet (no base; lowest point 0.25 m above the pivot); 2,503 tris |
| `Shelter` | `Shelter_01`, `Shelter_02` | 8.5 x 6.8 m, 7.6 m; 16,842 / 5,110 tris (an inn's cart shed, the refuge's lean-to) |
| `Smithy` | `Blacksmith_01` | 12.7 x 10.3 m, 8.5 m; open workshop with the forge hearth, a closed room at +x; 19,318 tris |
| `Stables` | `Stables_01` | 14.1 x 8.8 m, 9.2 m; groom's house on the left, stalls open to +Z; 29,446 tris |
| `Gatehouse` | `House_Archway_02` | 8.7 x 11.9 m, 9.9 m; the lane through it is **4.3 m wide, 3.3 m high** (`House_Archway_01`: 4.15-5.5 m, 52k tris): too narrow for the 5 m road, so it stands beside the road with a lane through it; 22,144 tris |
| `Bench` | `Props/Furniture/SM_Prop_Bench_Seat_01/04/05/03` | 2.9 / 2.5 / 2.5 / 1.8 m long, 0.5-0.6 m (03: back to 1.0 m); 140-610 tris; a seat for the traveller (`SeatCatalog` "Bench" → log seat) |
| `Lamp` | `Props/SM_Prop_Street_Lamp_01/02` | 2.9 m, lantern on an arm 0.69 m toward -Z (02: both ways); 750 / 1,202 tris; lit at night |
| `MarketStall` | `Props/SM_Prop_Market_Stall_03/06/08` | 3.8 x 4.7 / 3.4 x 3.3 / 2.6 x 4.6 m, 4.1 / 4.1 / 3.3 m; 4,404 / 5,023 / 7,554 tris (the others are 13-25k) |
| `YardGoods` | `Props/SM_Prop_Barrel_Stack_02..04`, `Crate_Stack_03/04`, `Sack_Stack_01/02`, `Barrel_01` | 0.7-2.7 m, 0.8-2.0 m tall; 440-3,926 tris (stacks are not seats) |
| `FarmCart` | `Vehicles/SM_Veh_Cart_01..04`, `Props/SM_Prop_Hay_Cart_01`, `Vehicles/SM_Veh_Wheelbarrow_01` | carts 1.9 x 3.2-4.4 m; 800-3,704 tris |
| `GardenPlant` | `Props/SM_Prop_Cabbage_01/02`, `Pumpkin_Plant_01/02` | 0.5 m and 1.3-1.9 m; 42-333 tris |
| `SmithyTool` | `Props/SM_Prop_Anvil_02/03`, `Grinding_Wheel_01` | 0.7-0.9 m; 366-1,326 tris |
| `Fingerpost` | `Props/SM_Prop_Direction_Sign_01/02` | 3.0 m (0.32 m below the pivot); 516-556 tris |

Kept from the Meadow pack: the well, sign, hitching rail, fences, dry-stone walls, windmills, fruit trees, wood piles and hay piles.
Considered and left out: `House_04` (door at a gable end), `House_06` (different base, 14.7 m), `House_10` (35.7k tris, 18.6 m deep),
`House_Archway_01` (52k tris), the heavy market stalls, `WashingLine_*` (hang from their pivot, need posts), `Sign_Wood_*` boards
(under 1 m: too small for a legible name; the inns' name boards are built in code and lettered from the built-in font, see ENDLESS_ROAD 2026-10-06), `Hay_Bale_01` (pivot at its middle).

**Characters** (`Characters/SM_Chr_*_01`; the people palette's first-party prefabs in `Assets/_Project/Prefabs/People/`): peasant male and
female, bartender (innkeeper), blacksmith male and female, merchant, rider and hermit (guests), monk, nun and priest (at a chapel), a
male soldier now and then as a guest. **Rig, measured from the model's import settings:** the Fantasy Kingdom characters use the POLYGON rig
of the player's peasant bone for bone (Root at 0.028 m, Hips 0.876 m above it, the same spine, arm and leg lengths and rest rotations to
seven digits; 73 skeleton entries); Synty's avatar maps the human Hips to `Root` as it does for the peasant, so `MedievalZen/People/Build
People Palette` builds `Animation/FantasyKingdom_Humanoid.asset` with Hips on the pelvis bone. The finger bones differ: the model names both
hands' fingers alike (`Thumb_01` twice; the prefab renames the right hand's `Thumb_01 1`), so the people's avatar leaves the fingers out.
Each Synty character prefab carries all 22 bodies with 21 switched off; the people's prefabs keep only their own. The clips are the
Base Locomotion pack's in-place `Idle_Standing` and `Walk_F` (masculine or feminine); there is no sitting clip, so villagers sit with a
procedural pose (`VillagerPose`).

### What it changes for this game
- **Villages and inns can look like villages**: timber houses, a tavern, a church, a smithy, stables, a windmill,
  a gate arch over the road, market stalls, hedges, wells, washing lines. Today every building is one stone cabin.
- **People**: Humanoid villagers and travellers that can use the locomotion clips already in the project.
- **A proper stone bridge** for streams.
- **Landmarks**: a watchtower, a church spire, a ruin, or a castle on a hill assembled from the kit.
- **Hedgerows, ploughed fields, hay** for farmland; reeds and lilies for water; boats on lakes.

## Animals — polyperfect Low Poly Animated Animals (`Assets/polyperfect/`)

Imported 2026-10-02. Third-party: reference it, never edit it. 683 MB, 132 animal prefabs (about 66 species
with colour variants), 68 Animator controllers, 48 sounds, 17 demo scenes. Verified against disk.

### Layout
| Folder (under `polyperfect/Low Poly Animated Animals/`) | Contents |
|---|---|
| `Prefabs/Animals/` | One prefab per animal and colour. `Demo/` and `Legacy/` subfolders hold demo-scene and old-rig copies: ignore them. Colour variants such as `Horse_Black`, `Cow_White` are prefab variants of a base prefab |
| `Animator Controllers/CTL_<Animal>.controller` | One controller per species, driven by bool parameters (see below). `Legacy/` = old rigs |
| `Meshes/Animals/<Animal>/` | `SKM_<Animal>_Rig.fbx` (mesh + skeleton) and `SKM_<Animal>_Animations.fbx` (clips); some older animals use loose `.anim` files instead |
| `Stats/STAT_<Animal>.asset` | Numbers for the pack's own predator/prey AI (dominance, stamina, aggression). Not needed |
| `Sounds/` | 48 `.ogg` files (see Sounds below) |
| `Scripts/`, `../Common/` | The pack's wander AI and helpers (assemblies `Polyperfect.Animals`, `Polyperfect.Common`) |
| `DEMO_Scenes/`, `Prefabs/Nature`, `Terrains/`, `Textures/` | Demo content. Not needed |

### Technical facts
- **Materials are already URP/Lit** (171 of 173), so nothing renders pink. One shared atlas per animal.
- **Rigs are Generic**, not Humanoid: clips cannot be shared between species, and nothing retargets to the Synty
  traveller.
- **Every controller uses bool parameters**: `isWalking`, `isRunning`, plus per-species extras (`isEating`,
  `isSleeping`, `isSitting`, `isBarking`, `isFlying`, `isHowling`, `isLooking`, `isIdle2`...), and `isAttacking` /
  `isDead` which this game never sets. Idle is the default state with all bools false.
- **Each prefab carries** an Animator, a CharacterController, a NavMeshAgent and the pack's `Animal_WanderScript`
  (972 lines: wandering, fleeing, hunting, dying). That script knows nothing about our streaming tiles or floating
  origin and its animals attack each other. For this game: instantiate the prefab, remove or disable the wander
  script, CharacterController and NavMeshAgent, and drive the Animator bools from first-party code.
- **Authored speeds** (from the prefabs' movement states) are a starting point only. Walks are sensible
  (0.8-1.5 m/s for farm animals); many runs are arcade values (horse, zebra and camel 15 m/s).
- **Sizes need measuring in the editor.** The skinned-mesh bounds stored in the prefabs include animation extents
  and are not trustworthy (a sheep reads as 2.1 m tall). Measure renderer bounds in the idle pose before placing.
- 683 MB on disk; `README_LowPolyAnimatedAnimals.pdf` is the official guide; `URP_LowPolyAnimatedAnimals.unitypackage`
  is a URP material patch that is already applied.

### Found while building the animals (2026-10-02)
- `CTL_Goose` and `CTL_Chick` have no flying state. `CTL_Squirrel` and `CTL_Beaver` use Any State transitions.
- Death states in Rooster/Hen, Pig, Cat, Eagle, Dove, Boar and Rabbit have no way out: never set `isDead*`.
- `Deer_Gray` uses the same mesh as `Deer` (it is not a separate stag model).
- Several rigs carry a x100 scale on a child transform (cow, hen, chick, dogs, fox, wolf, eagle), so a naive mesh
  measurement reads about 1 cm. Measure against the bone span.
- Heights at scale 1, measured in the editor: sheep 0.86 m, goat 0.83, pig 0.67, horse 1.90, rooster 0.64, goose
  0.90, cat 0.36, dove 0.27, deer 2.42, rabbit 0.27, squirrel 0.13, boar 0.75, beaver 0.36.

### The horse (for riding)
| | |
|---|---|
| Prefabs | `Horse_Brown`, `Horse_Grey`, `Horse_Black` (+ `Legacy/Horse Legacy White` and three more on the old rig) |
| Controller | `CTL_Horse`: `isWalking`, `isRunning`, `isEating`, `isSleeping` (also `isBarking`, `isAttacking`, `isDead`) |
| Clips (`SKM_Horse_Animations.fbx`) | `Idle`, `Walk`, `Run` (gallop), `Eating` (grazing), `Sleeping`, `Sleep_Stand` (lying down / getting up), `Attack` (rearing), `Death` |
| Missing | **No trot or canter**: only walk and gallop, so mid speeds need the walk played faster or the gallop slower. **No saddle, bridle or reins** on the mesh, and no riding animation for a rider anywhere in the project |
| Skeleton | `Root_M` > `Spine1_M` > `Chest_M`, `Neck_M`..`Neck4_M`, `Head_M`, legs `Scapula/Shoulder/Elbow/Wrist/Fingers` (front) and `Hip/Knee/Ankle/Toes` (hind), `Tail0..7_M`. Mount a rider on `Spine1_M` (between `Chest_M` and the hips) so the rider follows the back's motion |
| Authored speeds | walk 1.5 m/s, run 15 m/s (too fast for this game; a gallop of 7-9 m/s suits the streaming) |
| Sounds | `SFX_Horse_Foot`, `SFX_Horse_Run`, `SFX_Horse_Whinny` |

The rider's pose has to be procedural, like sitting: `SeatedPose` already builds a seated humanoid from bones and
is the natural base for a riding pose.

### Fit for a medieval countryside
| Fit | Animals (prefab names) | Clips beyond idle / walk / run |
|---|---|---|
| ✅ Farm and village | `Cow_Brown/Black/White`, `Cow_Horns_Brown/Black/White` | `Eat`, `Idle2`, `Sleep`, `Sleep-Stand` |
| ✅ | `Sheep`, `Sheep_Wool` | `Eat`, `Idle 2`, `Sleep`, `Sleep_To_Stand`, idle/walk/run transitions |
| ✅ | `Goat` | `Eating`, `Scream`, `Sleep`, `Sleep-Stand` |
| ✅ | `Pig` | `Eat`, `Sleep`, `Sleep_To_Stand`, `idle2`, transitions |
| ✅ | `Hen`, `Rooster`, `Chick` | `Eat`, `Fly`, `Idle_2`, `Idle_3`, `Idle_Roost`, `Sleep`, `Sleep_Stand` |
| ✅ | `Goose` | `Eat`, `Fly`, four idles, `Sleep`, `Sleep_Stand` |
| ✅ | `Dog_GoldenRetriever`, `Dog_GreatDane`, `Dog_GreatDane_Black` | `Bark`, `Sit`, `Stand` (get up) |
| ✅ | `Cat_Black`, `Cat_Orange`, `Cat_White` | `Sleep`, `Stand_To_Sleep`, transitions |
| ✅ | `Horse_Brown/Grey/Black` | see The horse |
| ✅ | `Dove_Rock` (rooftops, village squares) | `Fly`, `Idle Left/Right`, take-off and landing transitions |
| ✅ | `Rat` (barns, rare) | `Idle_1` |
| ✅ Wild | `Deer`, `Deer_Gray` (stag with antlers) | `Eat`, `idle 2`, `idle breath`, transitions |
| ✅ | `Fox` | transitions |
| ✅ | `Rabbit_Brown`, `Rabbit_White` | `Jump_Walk` (hop), `Jump_Up`, `LookOut` (sits up alert) |
| ✅ | `Squirrel` | `Idle 2`, transitions |
| ✅ | `Boar` (forest) | `Idle Breathing`, `Idle Left Right`, transitions |
| ✅ | `Wolf` (distant, never hostile) | `Howl` |
| ✅ | `Beaver` (lake shores) | transitions |
| ✅ | `Eagle` (passes, high ground) | `Flying`, `Fly idle` (glide), take-off and landing transitions |
| ✅ | `Fish` (lakes) | `Swim` only |
| ✅ | `Honeybee`, `Bumblebee` (flower meadows, close up) | `Fly`, `IdleA`, `IdleB` |
| ☑ With care | `Bear_Grizzly`, `Bear_Grizzly_Black` (+ `_Wild` variants): far off on a mountainside | `Rub` (scratches against a tree), `Attack_Bite`, `Attack_Swipe` |
| ☑ | `Reindeer` (high passes, if snow is added) | `Eating_In/Loop/Out` |
| ☑ | `Seagul` (large lakes, a future coast) | `Fly`, `Sitting` |
| ☑ | `Vulture` (downs, crags) | `Fly` |
| ☑ | `Snake_Red/Yellow`, `Crab` | small, easy to miss |
| ⚠ Other biomes | `Camel_Bactrain`, `Camel_Dromedary`, `Meerkat` for an arid region; `Buffalo` | |
| ❌ Exotic | Elephant, Giraffe, Lion, Tiger, Zebra, Rhino, Hippo, Gorilla, Chimpanzee, Panda, Ostrich, Flamingo, Parrot, Tucan, Anteater, Capybara, Tapir, Crocodile | |
| ❌ Sea and polar | Shark, Orca, Whale, Dolphin, Octopus, Squid, Jellyfish, Seahorse, Starfish, Seal, Walrus, Penguin, `Bear_Polar` | |
| ❌ Other | `Spider_*` (about 0.9 m across), Cockroach, `Dog_Chihuahua*` | |

Most animals also have `Attack` and `Death` clips. This game has no combat: never play them.

### What the clips make possible without new art
- **Grazing herds**: cows, sheep, goats and horses alternate `Idle`, `Eat` and a few steps of `Walk`; at night `Sleep`.
- **Farmyards**: hens, a rooster and chicks pecking (`Eat`), a goose, a pig, a dog that sits and barks, a cat asleep
  on a doorstep.
- **Wildlife that reacts**: deer and rabbits that look up (`LookOut`, `idle 2`) and run when the traveller comes
  close; a fox crossing the road; squirrels near trees; a boar in deep forest.
- **Birds**: doves taking off from a village square, an eagle gliding over a pass, geese by a lake.
- **Night**: a wolf's `Howl` far off.

### Sounds (`Sounds/*.ogg`): the first audio in the project
| Use | Files |
|---|---|
| Horse | `SFX_Horse_Foot`, `SFX_Horse_Run`, `SFX_Horse_Whinny` |
| Farm | `SFX_Cow_Moo`, `SFX_Cow_Bell`, `SFX_Cow_Background`, `SFX_Eating_Grass`, `SFX_Dog_Bark_Sigle`, `SFX_Dog_Barking`, `SFX_Dog_Whining`, `SFX_Cat_Meow`, `SFX_Cat_Purr`, `SFX_Cat_Hungry`, `SFX_Cat_Angry` |
| Wild | `SFX_Deer`, `SFX_Deer_Walk`, `SFX_Wolf_Howl`, `SFX_Bear_Calm`, `SFX_Bear_Growl`, `SFX_Bear_Growl_2`, `SFX_Seagull`, `SFX_Seagull_2` |
| Ambience beds | `SFX_Environment_Forest_Autumn` ✅ (forest and meadow); `SFX_Environment_Sea` ☑ (lake shore); `SFX_Water_Bubbles` ☑; Africa, Arctic, Desert, Jungle ❌ |
| Not for this game | elephant, gorilla, penguin, snake, spider, `SFX_Ice_Squeels`, `SFX_Piano_Sad` |

There are no sounds for sheep, goats, pigs, hens, geese or birdsong.

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
| sky/atmosphere | PNB_Core skydome + cloud ring + fog ring, `Skybox_Meadows_Mat_01`, `Fog_Meadows_01.mat`, `WeatherControl` |
| ambientFx | Meadow butterflies (4), petals (2), leaves (2), `SunBeam_01`, `Wind_01`; Generic `Flies_01`; campfire `FX_Fire_01` + `FX_Smoke_01` |

- **Endless field layers (2026-10-06, palette v19)**: first-party `FieldGold` / `FieldYellow` / `FieldGreen` terrain layers (Settings/TerrainLayers) copy the Meadow `Terrain_Meadow_Grass_02`, `Grass_01` and `Grass_Clover_01` layers with tints from `GroundLook.LayerTints` (sRGB 196,152,52 / 206,184,40 / 70,128,50). `Grass_Flowers_01` was rejected as a source: its white heads go above 1.0 under a yellow tint.
