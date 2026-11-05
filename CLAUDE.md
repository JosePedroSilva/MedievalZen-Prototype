# MedievalZen

Relaxed medieval travel simulator ("Slow Roads, but on foot"): procedurally generated
countryside, dirt roads between towns/villages/inns, no objective. Iteration 1 uses Synty
placeholder art. Full design and the iteration plan: `Docs/DESIGN.md` — read it first.

Two modes share the player, time, weather and content rules: the finite world (`Docs/DESIGN.md`) and the
Endless Road (`Docs/ENDLESS_ROAD.md`: one road generated while the player walks). In endless code all
generator coordinates are absolute doubles; only the runtime's floating origin converts them to scene space.

## Stack
- Unity 6000.3.8f1, URP, new Input System (`Assets/InputSystem_Actions.inputactions`).
- C# language level 9 (no file-scoped namespaces). No extra packages in iteration 1.

## Layout & conventions
- All first-party code/assets live in `Assets/_Project/` (runtime asmdef `MedievalZen`,
  editor asmdef `MedievalZen.Editor`). Namespaces: `MedievalZen.<Folder>`.
- Naming traps: `MedievalZen.Environment` (day cycle, ambience) shadows `System.Environment` inside our namespaces — write `System.Environment.TickCount` etc. Two more exceptions: editor code uses namespace `MedievalZen.EditorTools` (so `Editor` does not shadow `UnityEditor.Editor`), and the scene owner class is `GameWorld` in `MedievalZen.World` (a class named `World` would clash with its own namespace).
- `Assets/Synty/**` is third-party: reference its prefabs/textures, never edit them.
- `Assets/polyperfect/**` (animated animals, horses, the project's only sounds) is third-party too. Its prefabs carry
  the pack's own wander AI, CharacterController and NavMeshAgent: strip those and drive the Animator bools from
  first-party code. See the Animals section of `Docs/ASSET_CATALOG.md`.
- Animals live in `Scripts/Wildlife` (pure core, thin `WildlifeManager`). Never reference the Polyperfect
  assemblies, never set an `isDead*` bool (several death states have no way out), and use only the stripped
  variants in `Assets/_Project/Prefabs/Animals/` built by `MedievalZen/Wildlife/Build Wildlife Palette`
  (`Resources/WildlifePalette`). New randomness streams for animals: 901–999.
- `EndlessNoise` stream numbers in use: 101–810 (generator), 901–999 (animals), 1001–1090 (streams, shelves,
  villages, pass walls), 1101–1150 (planting: roadside trees, hedgerows, copses), 1151–1199 (stream look),
  1201–1250 (landmarks, field boundaries), 1251–1262 (the woods: wood edge, tunnels, glades, ancient trees, floor, logs,
  restyling, ivy), 1263–1290 (mountain passes: cliff bands, gullies, talus fans, the road's shelf, scree boulders, pass pines;
  1263–1278 in use), 1291–1330 (inns, villages and their people: 1291 roadside inn, 1292 refuge, 1293 inn archetype, 1301 a place's cast, 1302 one
  person's day, 1303–1304 village plan pick; 1294–1300 and 1305–1330 free), 1341–1360 (pass slopes: 1341–1343 low shrubs, 1344–1345 outcrop crags,
  1346–1352 the faceted rock skin; 1353–1360 free), 1361–1380 (the forest floor: 1361 litter drifts, 1362–1363 understorey clusters, 1364 shaded sward, 1365 wood logs,
  1366 mushrooms; 1367–1380 free), 1381–1399 (bridges and inn signs: 1381 a bridge's abutment stones and reeds, 1382–1385 bank hummocks, 1386–1389 a bridge's wall stones; 1390–1399 free), 1450–1479 (the farmland
  patchwork: 1450 strips per field, 1451 a strip's crop, 1452 a village close's look, 1453 a close's orchard trees, 1454 garden plants, 1455 scarecrows, 1456 haystacks, 1457 the paddock's worn edge; 1458–1479 free). Also 1400 the walled city (1401–1429 free) and 1430–1433 road traffic
  (1434–1449 free). Take new work from 1480 up; 1331–1340 and the free ranges above are open too.
- Endless inns, hamlets and villages are composed by `InnComposer` / `VillageComposer` on `SettlementKit` from the Fantasy Kingdom
  presets measured in `BuildingTable` (footprint, door, chimney, colours): place buildings by that table, never by bounds or a pivot
  guess, and keep every footprint off the road and its 1.5 m verge. People live in `Scripts/People` (pure planner and brain, thin
  `PeopleManager` that creates itself in Play mode on the endless road, characters built by `MedievalZen/People/Build People Palette`
  into `Resources/PeoplePalette`); their rounds never touch the road.
- Endless-only tuning lives in endless-only tables (`ScatterRules.Endless`, `DetailPlanner.EndlessRules`), never in the
  tables the finite world shares. Countryside bushes, shrubs and ferns (`PropRole.IsInstancedPlant`, which includes the patchwork's crops: wheat, rapeseed, sunflowers) and field boundary
  pieces (walls, hedges, fences) are drawn by `EndlessTreeRenderer`, not as GameObjects; so are the woods' ferns, undergrowth
  and ivy (`PropRole.IsForestExtra`).
- Endless trunks keep a per-region corridor (`Planting.TrunkCorridor`: 5.5 m in a Forest, the scatter's 10.5 m in farmland and
  passes) and `Planting.MinTrunkDistance` (no trunk collider within 4 m of the road centre, no trunk base on the road). The forest
  extras (ferns, undergrowth, ivy, forest light, mist and haze: `EndlessFeatures.forestExtras`, `ForestMood`) are the first rung
  the safety ladder sheds, before any tree. Beyond `treeShadowProxyDistance` (100 m) trees cast from a low-poly proxy
  (`ShadowProxy`), not their mesh LODs.
- Ambient effects (`AmbientFxSpawner`, the Synty effect prefabs) prewarm a 5 s loop in one frame; the ones that collide with
  the world (petals, falling leaves and their season copies) must start with collision off and get it back the next frame, or
  every start costs a 12-23 ms frame. Whatever is made for the first time near a place (effects, people, animal bodies, chimney
  smoke and fires, mist, font glyphs, clips) is made once by `ArrivalWarmup` during a hold (startup, journey jump, outrun), and
  every runtime instantiate is named with `FrameCost.FirstUse` so a walk benchmark's worst frames say what was new.
- Endless streams are a drawn mesh with its own banks (`TileStreams`, shaders `StreamWater` / `StreamBank`) in a channel
  carved just below it; the water's edge must never come from where the 2 m terrain grid crosses a plane.
- A stone bridge is the Synty prefab stretched by `BridgeShape`, laid along the chord of the road over its length (the road curves,
  the bridge does not), with first-party walls round it (`BridgeWalls`, `BridgeWallsView`: parapets that enclose the Synty model's own
  low parapet, a capped post at each end, splayed approach walls buried into the ground). Nothing of a bridge may come nearer the road
  centre than 2.55 m.
- The Synty mud terrain layer is glossy (metallic 0.3, smoothness 0.5) and mirrors the sky: the endless road paints mud through the
  matt copy (`WorldPalette.endlessMudLayer`), and no village close (paddock, garden, orchard, hay plot: `CloseContents`) uses mud,
  rock or cobble. The woods' leaf litter is endless channel 14 and takes the cobblestone's slot in a tile's layer set
  (`EndlessLayerSets`), so a forest tile keeps two terrain passes: a new ground layer must not add a pass to the Forest.
- Distant leaf crowns (instanced impostors, the trees of mid and far tiles) carry a baked light top and dark underside and are
  marked with vertex alpha 0, which `FarTerrain.shader` lights with a wrapped foliage light; everything else has alpha 255 and is
  lit as before. A solid crown seen against the sun is otherwise one flat shaded colour.
- A vertex struct uploaded with `Mesh.SetVertexBufferData` must have its fields in the order of Unity's `VertexAttribute` enum:
  position, normal, tangent, colour, texture coordinates. Unity lays the buffer out in that order whatever order the descriptors are
  listed in. The stream mesh had uv before colour for four days: every vertex colour the stream shaders read was garbage and uv.y
  was infinite wherever alpha was 255 (the white glow). When a shader misbehaves, set the global `_MZDebugView` (the review tour does
  at crossings) before tuning anything.
- First-party shaders must not let a NaN or an infinity out: bloom turns one such pixel into a white glow over half the screen
  (it happened three times, at streams). Avoid `pow` with a base that can be 0, use `SafeNormalize`, guard divisions, and end
  every fragment with the bit tests `AnyIsNaN` / `AnyIsInf` and a fallback colour; `min`, `max` and `saturate` do not strip a
  NaN on every GPU (the user's is an AMD RX 6800 XT on D3D12). The camera's Stop NaNs pass is on as a last net.
- Sound lives in `Scripts/Sound` (namespace `MedievalZen.Sound`; not `Audio`, which would shadow `UnityEngine.Audio`). The direction
  is the user's (2026-10-06): like Death Stranding, ambience is the game's default state and a piece of music is a rare event
  (`MusicFrequency.Rare` is the default). Three systems meet only in `SoundMix` (sliders, loudness targets, `MusicPresence`,
  `CaptureRunning`, F3 overlay lines) and never call each other: the ambience (`Ambience*`: beds by region, tree cover, sun and
  weather; positioned water, places and landmarks; spot birds; a synthesised rain), the music (`Music*`: the pure `MusicDirector`
  keeps the silences, waits for a moment and obeys the runway rule, so no piece starts unless it can end before the next place;
  a track's Intensity 1 / Intensity 2 / Main files are aligned alternative mixes and are crossfaded, never summed; loop length
  = duration − the folder's RT, and a piece ends by letting that tail ring, never by a cut) and the footsteps (`Footstep*`: a
  synthesised placeholder per surface until a sampled pack arrives). `F7` starts or ends a piece, `F8` steps through the tracks.
- Nobody can listen offline: every clip is measured from its file (`WavFile`) and trimmed to the `SoundMix` targets, a source that
  must play above volume 1 goes through `SoundBoost`, and 3D clips are forced to mono with Unity's normalise switched off
  (`SoundImportRules.SetNormalize`: left on, a quiet loop comes in up to 16 dB louder than measured). Judge sound by the user's ears
  and the F3 lines, and say what could not be heard.
- `Assets/Sounds/**` is third-party: never move, rename or edit its files. `Resources/AmbiencePalette` and `Resources/MusicLibrary`
  are built by `MedievalZen/Sound/Build Ambience Palette` and `Build Music Library` (also on editor load when a catalog's `Version`
  is newer: bump it when a table or an import rule changes); their `enabled`, `userTrimDb` and `userWeight` fields are the user's
  and survive rebuilds. Adding or removing an `AssetPostprocessor` makes Unity reimport every asset of its type (all 602 sound
  files, minutes): do not add another for audio lightly.
- Seasons meet in one contract, `Scripts/Environment/Season.cs`: `Season.Year` runs 0..3 (spring, summer, autumn; winter is
  for later, the packs have no snow). **Autumn is the identity**: at `Season.Identity` every seasonal table, tint, weight and curve
  must give exactly what the game drew before seasons, and fixed autumn is the default. `SeasonCycle` is the only writer; the day
  and weather read the year (`DayClock.SolarHour` maps a summer day onto the validated dawn-to-dusk gradients, which are never
  edited); colours are applied by `SeasonPainter` to run-time copies only, when `Season.Revision` moves; distant trees and far
  ground carry a season class in the vertex alpha (`SeasonLook`, below 128 = a leaf crown) that `FarTerrain.shader` tints.
- A walled city (`CityComposer`, `WaypointKind.City`, `Waypoint.IsVillageLike`) takes the slot of a village the plan already has
  (`JourneyPlan.IsCitySlot`), so no other place moves: keep it that way (`EndlessParams.walledCities` switches cities off for
  comparison, and a test holds every other waypoint). Its wall pieces are boundary pieces, drawn instanced. No pack gate fits the
  5 m road: a gate is two towers and a span at least 4.5 m above the road. The city is a hill town (the user's reference is the
  pack's demo scene: height, closeness, a castle as the goal, detail everywhere; look first, optimise later). `CityHill` plans
  5 m terraces (one wall module per riser), ramps, the pack's stair flights and a rock carrying a castle mined from the demo, and
  it has **two grounds**: the surface (true steps; what the eye, the composer's `Ground` and `EndlessWorld.GroundHeightAbsolute`
  use) and the terrain (the same, but each riser's bank lies wholly behind its wall under a drawn cobbled deck, `TerraceDecks` /
  `TerraceDeckView`, because a 2 m heightmap cannot make a step: the first version stood its walls mid-bank and everything at a
  wall's foot floated). Walls stand on true low ground with the parapet on the module's top, every run ends in a pillar, no two
  parallel walls within 3 m, no cliff rock inside the town wall; `CityTerraceTests` measures all of it and the city harness plots
  cross-sections (`citysections`). Set pieces (`CitySetPieces.Data.cs`, generated by the tools in the mining scratch folder
  `citymine`: `castle.py`, `seat.py`, `gen.py`, `emit.py`; never hand-edit) carry what each part stood on or hung from
  (`SetPart.Mount`, `Support`): nothing floats, wall pieces mount on a face at 2.2 m or more, lamps, benches, planters and carts
  follow context rules; the castle is selected by prefab name with its floors and pillars and every column filled down to the
  court (`LOWER_WARD` in `castle.py` keeps the demo's lower ward out until the rock has a lower step on the gate side). Details
  are zeroed on paving (`CityPaving`), colliders follow prefab names (`PieceColliders`: boxes for walls, the prefab's own meshes
  for arches, doors and stairs, none for roofs), and `Capture Places` takes street-level shots inside a city (`CityViewpoints`).
  **Whatever decides where other things go (landmarks, crossings, the road) reads `TerrainField.PlanHeight`, the land without a
  city's hill**; only built terrain and the city itself see the hill.
- Other road users (`Scripts/Traffic`) are a closed-form plan (position is a formula of seed and time, right after any jump);
  bodies come only from a pool made during a hold, keep at least 1.65 m from the centre line, and have no colliders, so
  auto-travel is never blocked. Riding (`HorseMount`, `HorseRiding`) goes through `PlayerMotor.MoveRiding`, not a second
  movement path; the seat astride is one piece of maths (`AstrideMath`) shared by the traveller and the road's riders. The
  journal (`Scripts/Travel/Journal*`, key J) records only what the game knows and nothing during a capture.
- The "Rich light" look (`LightLook`, Journey menu toggle) sits on top of the validated fog and cloud look; tune it there,
  not in the fog or the cloud model.
- Changing the default of a field on a settings asset (`EndlessSettings`, URP assets) does not change the asset already
  loaded in the editor: add a one-time upgrade in editor code that moves the old value to the new one, and check the log.
- Before picking Synty assets, read `Docs/ASSET_CATALOG.md` (every pack by role, with fit ratings and exclusion lists; ready-made Meadow TerrainLayers; model-only assets).
- World generation must be deterministic from `WorldSettings.seed` — use `System.Random`
  seeded per stage, never `UnityEngine.Random` in generation code.
- Pure generation stages (everything that produces the `WorldBlueprint`) must not call native Unity
  code (`Quaternion.Euler`, `AnimationCurve`, `JsonUtility`, Terrain, GameObject): they are tested in an
  offline .NET harness. Unity-side work belongs in `WorldBuilder`.
- Stages shared with the endless world take `WorldGenParams.window` (absolute origin of a tile window). With it
  null the finite world must stay bit-identical: `EndlessGeneratorTests` holds golden hashes for three seeds.
- Terrain: persist, then paint. Never write terrain layers/alphamaps to a `TerrainData` that is not yet
  an asset on disk (Unity then saves default paint and the ground reverts to plain grass on reload).
  All terrain writes go through `TerrainDataWriter`.
- Rock-like props (rocks, piles, slabs, cliffs, mounds, cairns) are seated by `RockPlacer` from measured
  `PropMetrics`, with sizes in metres. Never place them at a pivot on the ground with a raw scale.
- Grass and ground colours come only from `GroundLook` (first-party tinted terrain layers and sward detail materials
  built by the palette menu). Never tint Synty materials, and never draw a tree's flat card LOD.
- A first-party copy of a Synty plant material starts from the material the mesh was built for and fills both `_Leaf_Texture`
  and `_Trunk_Texture`: the foliage shader draws some meshes (the crop clumps) from the trunk slot, and an empty slot is an
  opaque white card (the white rapeseed fields until palette v20).
- Clouds are coloured only by `DayCycle` + `CloudColorModel`; Synty's own cloud fog and scattering are
  switched off per renderer. Do not tint clouds by multiplying the Synty material colours.
- Systems that read the ground (height, water, tree density, fog base) ask `GameWorld.Current.Surface`
  (`IWorldSurface`), never `GameWorld.Terrain`: the endless mode has many terrains and a moving origin.
  Components that hold world positions across frames implement `IOriginShiftable`.
- Manual input and auto-travel both drive `PlayerMotor`; do not add a second movement path.
- Player-side scripts run in a fixed order via `[DefaultExecutionOrder]`: GameWorld −100, EndlessWorld −70, DayCycle −60, SeasonCycle −59, WeatherSystem −58,
  AtmosphereController −55, PlayerController −50, HeldMap −45, JourneyMenu −44, JournalBook −43, PlayerSeat −42, HorseMount −41, AutoTraveller −40, CinematicDirector −35, WildlifeManager −32, PeopleManager −31, PlayerInteractor −30, AmbienceDirector −29, MusicPlayer −28, RoadTrafficManager −27, HudController 50,
  PlayerMotor 100, PlayerAnimator 110 (observes the motor only, no root motion), Footsteps 115 (reads the foot bones after the Animator), SeatedPose 120 (bone blend after the Animator), RiderPose 122 (the traveller astride), VillagerPose 125 (the people's seated pose), RoadFigurePose 126 (road users' head turn, nod and riders); a driver calls the motor once per frame in Update: `MoveFacing` (third person and auto-travel: facing and travel direction are one thing) or `Move` + `Face` (first person: strafing allowed). Overlays (held map,
  signpost list, Journey menu) take input through `PlayerController.InputOverride` (`IPlayerInputOverride`),
  never a second input path.
- `PlayerCameraRig` is the only owner of the Camera. The cinematic camera takes the view only through
  `SetPoseProvider`/`ReleasePoseProvider` (`ICameraPoseProvider`); sitting moves it only through `AnchorOffset` and
  `SetLookLimits`. First- and third-person numbers are validated by the user: do not change them.
- Seats are recognised by Synty prefab name (`SeatCatalog`), with no component per prop; the endless tile builder
  registers placed props with `SeatIndex`.
- Keep the "zen" pacing: slow speeds, smooth cameras, no pop-in near the road.

## Working in this repo
- The Unity editor is usually open (`Temp/UnityLockfile` present) → never launch Unity in
  batch mode; compile-check with the scratch-csproj approach described in `Docs/DESIGN.md`.
- Never hand a file under `Library\ScriptAssemblies` to a build or a program (copy it first), and build with
  `-p:UseSharedCompilation=false` (`check.ps1` does): a lingering compiler server kept `MedievalZen.dll` mapped and Unity could not
  compile ("Copying the file failed ... user-mapped section", 2026-10-06). Kill only processes you started, by id, never by a
  name or command-line pattern (a pattern once took the user's WebView2 windows with it).
- "Golden hashes unchanged" must hold on the runtime the editor uses, not only offline: the editor's embedded Mono evaluates
  float arithmetic in double and rounds where the JIT stores, so a new local, a branch or an inlined helper in a shared stage can
  move the finite world's last bits while CoreCLR stays identical. `GoldenMono` is recorded and checked with the editor's own runtime
  (`Tools/MonoCheck/run.ps1 [-Golden] [-Filter ...]`, see "Golden hashes under Mono" in `Docs/DESIGN.md`; never with the
  standalone `mono.exe`), the finite splat runs a frozen copy
  (`TerrainShaper.ComputeFiniteSplatAsOf2026_10_02`: do not edit it), and the user's Test Runner (Window, General, Test
  Runner, EditMode, Run All: nine minutes) is the final word.
- Scenes/prefabs are YAML; create them via editor scripts (`MedievalZen/...` menu items)
  rather than hand-writing `.unity`/`.prefab` files. In those scripts load assets AFTER
  `EditorSceneManager.NewScene(Single)`: it unloads unused assets, and references taken before it serialise as
  null. Verify references after saving.
- Many Unity Terrains are expensive, and legacy terrain trees (prefabs without a LODGroup) multiply the cost:
  about 200 terrains hung the GPU. In the endless mode only near tiles are Terrains and trees are instanced.
- Visual review: the user (or you, by asking them) runs `MedievalZen/Review/Capture Tour` (6 km from where the traveller stands
  or from `Tour Start Distance...`; the first console line gives the start in bold), `Capture Tour From Road Start` (from 0 m),
  `Capture Places` (each inn, hamlet, village and landmark of the next 20 km, with a raised overview of each settlement) or
  presses `F9` in Play mode; options `Low Sun Shots` and `Weather/` (default Clear). Shots land in `ReviewShots/` as PNG + JSON
  (sidecar: tour kind, stop, shot, region, hour, weather, camera, landmark name/kind/distance/bearing/visible and eye move,
  performance) and can be read directly; each tour also writes `tour_<stamp>[_places]_summary.json` (per region, per stop,
  landmarks). `Walk Benchmark` (options: speed x1/x6/x12, 1/2/5 km) auto-travels from inn to inn with the screen visible
  (only `AutoTraveller.DebugSpeedScale` changes, back to 1 afterwards) and writes `walk_<stamp>_s<seed>_<from>-<to>m_x<speed>.json`
  (frame percentiles, hitches per km, per-region table, streaming holds, 25 worst frames with slice and nearest place) plus a
  `.csv` of every frame: use it, not tours, to judge loading hitches. Offline renders have repeatedly misjudged the look.
  The capture component lives in `Scripts/Review/` (a MonoBehaviour in an Editor folder cannot be added to a GameObject).
  After a Play session, `Editor.log` holds a "running OK for 20 s" line with tile, tree and shadow numbers: read it.
- An idea written up and not started: `Docs/DIORAMAS.md` (scenes the user builds by hand, used as points of interest and as
  biome references). Do not build any of it until the user asks.
- Open findings from the last review tour, in priority order, live in `Docs/NEXT_SESSION.md`: read it before planning
  new work, and replace it after each review.
- Don't commit unless asked. `.gitignore` already excludes `Library/`, `Temp/`, IDE files.
- Third-party packs (`Assets/Synty`, `Assets/polyperfect`, `Assets/Sounds`: about 8.5 GB) are git-ignored and never
  pushed; binaries go through Git LFS (`.gitattributes`, which also covers the finite world's
  `Assets/_Project/Generated/*.asset`). `Tools/AssetCheck/check-assets.ps1` / `.sh` read `third-party-packs.txt` and
  tell a fresh clone which packs it still lacks: add a line there whenever a new pack is imported.
