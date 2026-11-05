# MedievalZen — Design & Iteration-1 Plan

## Vision
"Slow Roads, but on foot in a medieval countryside." A relaxed travel simulator with no
real objective: walk or ride the roads between towns, villages and inns; watch the weather
and the day pass; rest at an inn and set out again in the morning.

## Iteration 1 goal — "does the fantasy work?"
A seeded, finite map (default 2 km × 2 km) with gentle hills, forests and fields; 5–8
settlements (towns, villages, roadside inns) connected by dirt roads; a player who can
walk in first or third person **or** auto-travel along the roads to a chosen destination;
a day/night cycle; entering an inn rests until morning. All art is Synty placeholder.

### Non-goals for iteration 1
Combat, inventory, economy, NPC dialogue, save/load, procedural buildings from the modular
kit, custom shaders (exceptions since 2026-09-27: the full-screen atmospheric fog pass and the FireflyGlow particle shader), rivers (lakes with a water plane are in), horses/carts (auto-travel is "walking on autopilot").

## Tech baseline
- Unity **6000.3.8f1**, URP (`Assets/Settings/PC_RPAsset`), new **Input System**
  (`Assets/InputSystem_Actions.inputactions`, `Player` map: Move/Look/Sprint/Jump/Interact/
  Crouch/Next/Previous, plus ToggleCamera (V), AutoTravel (T), Map (M), Pause (Esc); `UI` map).
  AI Navigation package is present but unused.
- **No extra packages in iteration 1.** Roads are hand-rolled Catmull-Rom polylines with
  arc-length tables; the camera rig is hand-rolled. (Cinemachine/Splines can come later.)
- C# language level is 9 (Unity-generated csproj) — no file-scoped namespaces.
- Synty packs available (third-party, never edited):
  - `Assets/Synty/PolygonGeneric` — modular building kit, characters (`Prefabs/Characters/
    SM_Gen_Chr_Peasent_Male_01`, `..._Female_01`), environment tiles, gravel road pieces,
    props, `Textures/Generic_Grass.png`, `Generic_Dirt.png`.
  - `Assets/Synty/PolygonNatureBiomes/PNB_Meadow_Forest` — trees (Birch, Meadow, Fruit),
    bushes, grass clumps, rocks, `Prefabs/Props/`: stone cabin, windmill, fences, gates,
    signs, well, camp props, hand cart, scarecrow, stone arch/walls. `Terrain/` has terrain
    textures with normals: `Grass_Texture_01/02`, `Dirt_Texture_01`, `Gravel_Texture_01`,
    `Footpath_Tiles_Texture_01`, `Cobblestone_Texture_01`, etc.
  - `PNB_Arid_Desert` (unused for now), `PNB_Core` (skydome, cloud/fog rings, `WeatherControl`).
- Humanoid animation: the Synty **ANIMATION Base Locomotion** pack (imported 2026-09-26, see
  `Docs/ASSET_CATALOG.md` → Animation) provides Humanoid idle/walk/run/turn/look clips for the
  POLYGON rig; the player uses them through `PlayerLocomotion.controller` (Phase 2b).

## Project layout
```
Assets/_Project/                 all first-party content
  MedievalZen.asmdef             runtime assembly (root namespace MedievalZen)
  Scripts/
    World/        heightmap, biomes, settlement placement, scatter, WorldGenerator
    Roads/        RoadNetwork + routing (the shared contract, see below)
    Player/       PlayerMotor, AutoTraveller, PlayerCameraRig, input glue
    Travel/       destination selection, arrival/inn logic
    Environment/  DayCycle, weather hook
    UI/           map, toasts, prompts
  Editor/         MedievalZen.Editor.asmdef, menu items (Generate World, Setup Demo Scene)
  Prefabs/  Scenes/  Settings/ (WorldSettings assets)  Materials/
Assets/Synty/                    third-party — do not modify
Docs/                            this file and future notes
```

## Architecture

### World generation pipeline (deterministic from `WorldSettings.seed`)
1. **WorldSettings** (ScriptableObject): seed, world size, terrain resolution, hill
   amplitude/frequency, settlement counts per type, min settlement spacing, forest density,
   road width, day length.
2. **Heightmap** — the landscape must read as dramatic, not bland (user feedback 2026-09-26):
   total relief ≈ 200–300 m over 2 km, composed of a broad rolling base (fBm, ~60 m), a
   ridged-multifractal mountain mass masked to one side/corner of the map (so there is a
   lowland valley region where most settlements sit and a highland region the roads climb
   through passes), valley floors flattened by a remap curve, and steep escarpments that get
   a rock layer. Lakes: `WorldSettings.lakeCoverage` floods the lowest share of the map (the
   water level is derived from it); flooded cells are impassable for roads and free of
   scatter, with Synty's water plane (`Water_Lake.mat`) at that height. Written into a
   runtime-created `TerrainData` on a Unity `Terrain`. Terrain layers: 8 ready-made Synty
   Meadow `TerrainLayer`s (lush grass, dry grass, dirt for roads and settlement discs, rough
   rock on slopes/ridgelines, leaves under forest, flower meadows, mud on shores, cobblestone
   in town centres), blended by a large-scale moisture noise and height. The tallest peak and
   lakes are exposed as named landmarks for the map.
3. **Settlement placement**: Poisson-disk candidates (min spacing ≈ 350 m), scored by local
   flatness and distance from the map edge; pick N. Types: Town (2–3, largest), Village,
   Inn (placed *after* roads, near the midpoint of any road leg longer than ~500 m).
4. **Road graph**: connect settlements with a relative-neighbourhood / Delaunay-style graph,
   keep the MST plus any edge whose routed length ÷ straight-line length is below a
   threshold (so there are a few loops). Each edge is routed with **A\*** on a coarse cost
   grid (≈10 m cells; cost = 1 + slope penalty, existing roads are cheap so routes merge
   into junctions) → Douglas-Peucker simplify → Catmull-Rom → resample to a 2 m polyline
   with an arc-length table. Shared cells become `RoadNode` junctions.
5. **Road carving**: flatten the terrain across the road width with smooth shoulders, paint
   the dirt layer along the road, keep an 8 m scatter-free corridor. Optionally place
   `SM_Gen_Env_Road_Gravel_*` pieces later; painted dirt is enough for iteration 1.
6. **Scatter**: biome mask from noise + height + distance-to-settlement — lowland meadow
   (meadow trees, many wildflower patches, grass clumps), forest (dense birch/meadow trees,
   bushes, rock piles, mushrooms), highland (sparse birch, rocks, cliff pieces on steep
   slopes), farmland near settlements (crop clumps in rows, fences, scarecrows). Trees as
   Terrain tree instances (Synty tree prefabs, scale 0.8–1.3, random yaw) for performance;
   rocks, bushes and flowers as instanced prefabs under per-chunk parents. Poisson sampling
   per chunk; avoid roads, water and settlement radii. Implemented as a data-driven
   `ScatterRules` table (one line per family; ids in `PropRole`, prefab paths in
   `SyntyCatalog`) plus `DetailPlanner` terrain-detail density maps for grass/flowers; fields
   are wheat/rapeseed/sunflower prefab rows so they stay crisp (asset paths per
   `Docs/ASSET_CATALOG.md`). Grass/flower details are sized per family by
   `DetailPlanner.TargetHeight` (short 0.25–0.4, med 0.35–0.55, tall 0.5–0.75 m, sunflower
   1.2–1.6 m) from measured mesh bounds; wide Synty "patch" meshes (`Grass_Large_*`,
   `Wildflowers_Patch_*`, `Grass_Tall_Clump_05`) are never terrain details because rigid meshes
   that wide float off the ground. Terrain: `heightmapPixelError` 1, detail distance 110 m; the
   heightmap is smoothed on low slopes (4-octave base fBm) so terrain LOD never drops below the
   placed details. The palette carries `detailVersion` and refreshes stale detail prefabs itself.
7. **Settlement dressing** by prefab composition on a flattened disc (no modular kit):
   - Inn: `SM_Bld_Stone_Cabin_01` + sign + horse hitch + well + campfire + fence pieces.
   - Village: 3–5 cabins around a well + crop-field clumps + windmill + fences.
   - Town: more cabins + stone walls / stone arch as a gate + scarecrow fields around.
   Each settlement gets a generated name (syllable tables) and a trigger volume.
8. **Junction signposts** (`SM_Prop_Sign_*`) at every junction with 3+ roads.
9. Output is a `WorldData` object (settlements, nodes, segments, polylines) owned by a
   scene-level `GameWorld` component; serialisable so generation can run from the editor menu
   (`MedievalZen/Generate World`) or at Play start from the same settings.

### The mountain range and the pass (Phase 1c, 2026-09-27)
- **Range, not plateau** (`MountainRange`): a ridge skeleton - a main ridge with four summits and a lower
  col between the 2nd and 3rd (`passHeight`), side spurs, foothill knolls, two graded pass valleys cut
  from the col down each side, crag detail that grows with height, and drainage carving
  (`valleyCarving`). Relief stays about 240 m; the range covers roughly half the area the old plateau
  did. The earlier masked ridged noise is gone; `mountainMaskRadius` now sets the range length,
  `mountainMaskCentre` / `randomiseMountainSide` where it rises, `ridgeSharpness` the crag detail.
- **Mountain inn** (`mountainInn`, on top of `innCount`): the flattest dry spot within 160 m of the col,
  at 55 % of relief or higher; an ordinary `SettlementType.Inn`. Skipped with a logged reason when no
  site qualifies. Towns and villages keep 400 m away from the col.
- **Pass road** (`PassRouter`): hybrid A* over position and heading, laid in 5 m arcs (radius 8 m to
  straight), climbing at most `maxRoadGrade` (12 %); 8 m hairpins, switchback legs at least 20 m apart.
  Each side joins a lowland road; a two-sided pass closes a loop in the network. All road profiles are
  grade-limited (12 % pass, 15 % lowland, 18 % hard limit over any 10 m). Roads are benched into the
  slope with 45 degree cut banks (painted rock) and 35 degree fill banks.
- **On the climb**: cairns every ~100 m above 58 % of relief and a large one at the pass, a viewpoint
  (stone arch + rune stones, chosen by line of sight to the lowland and lakes), cliff meshes above road
  cuts, a camp about two-thirds up, a broken wagon at the lowest hairpin, signposts at both junctions.
- **Tree line** (`treeLine`, 0.7 of relief): broadleaf stops there; pines fade out by 0.86.
- **Landmarks**: `LandmarkKind` gains `Pass` and `Viewpoint`; the parchment map draws and labels them.
- **Summit architecture** (2026-09-27, after the first in-engine look): ridges have one steep and one
  gentle side, each summit gets a horn and the crest small notches, the upper range has ~13 m cliff
  bands (ledge, then a riser about three times steeper), gullies up to 6 m deep run down steep faces.
  None of it is applied near the pass valleys or the col, so the inn shelf and road are unchanged.
- **Known limits**: cliff bands can read as regular horizontal stripes from afar; seen from the inn the
  nearest summits still look rounded (the keep-out zone covers their lower flanks); the climb is a steady
  9-12 % with no level stretches; across seeds 1-16, 6 get a two-sided pass, 7 a one-way spur and 3 no
  inn; generation takes about 5 s (was about 3 s); the lowland network still gains no loops of its own.

### Terrain persistence and paint (rule learned 2026-09-27)
- **Persist, then paint.** A `TerrainData` must be an asset on disk *before* terrain layers and
  alphamaps are written to it. Painting an in-memory `TerrainData` and calling
  `AssetDatabase.CreateAsset` afterwards saves default paint (layer 0 at 100 %): the world looks right
  in the session that generated it and turns uniformly green when the scene is reopened.
- All terrain writes go through `TerrainDataWriter` (create asset → heights → splat → trees → details →
  save → verify). The verify step reloads a copy from disk, compares ~200 texels with the blueprint and
  logs per-layer coverage, or an error naming the lost-paint signature.
- `MedievalZen/Repaint Terrain` re-runs the pure generation and re-applies layers, alphamaps and
  grass/flower detail maps to the existing terrain without touching trees, props or settlements.
- Resolutions on the 2 km map: splat 2048 (1 m texels, crisp road edges); land cover, scatter and
  grass density stay on a 1024 grid (`WorldGenParams.CoverResolution`).
- Paint rules: road core is solid dirt to 0.5 m inside the road edge, 1.5 m soft shoulder, ~3 m worn
  verge. Rock blends in from 30° and is solid from 40° slope in every biome (`LandCover.Slope` is rise
  over run, 1 = 45°); only town cobbles and road dirt outrank rock. Ground within 16 m below a cliff band is painted as scree (dirt with
  rock flecks); above the tree line the grass is always the dry layer.
- **Rock layer**: the rock splat channel uses a first-party `RockWall.terrainlayer` (created by the
  palette menu from Synty's `RockWall_Texture_01`, 5 m tiles) so the ground matches the Meadow rock
  meshes, which use the same texture.
- **Rock and cliff props are seated, never dropped at a pivot** (`RockPlacer`, fed by `PropMetrics`
  measured per prefab by the palette menu): sizes are specified in metres (boulders 0.6-2.5 m, cliffs
  3-8 m, nothing over 10 m); every piece is sunk until no part of its footprint has air under it; cliff
  pieces must be backed by ground reaching 70 % of their height, face downhill, and are never placed on
  convex ground (crests, summit cones); boulders and slabs lean part-way to the ground normal. Only
  Meadow rock pieces are used in the highlands. `WorldBuilder` re-checks every piece with real bounds
  after instantiating and sinks, shrinks or removes what still fails, logging one line per family.
- `TerrainPersistenceTests` (EditMode, uses `AssetDatabase`) saves a small terrain, reloads it from disk
  and fails if the paint is gone.

### Roads runtime API — the contract between world and player work
```csharp
namespace MedievalZen.Roads
{
    public enum SettlementType { Town, Village, Inn }

    public sealed class Settlement { int Id; string Name; SettlementType Type; Vector3 Position; RoadNode Node; float Radius; }
    public sealed class RoadNode   { int Id; Vector3 Position; IReadOnlyList<RoadSegment> Segments; Settlement Settlement; /* null = plain junction */ }
    public sealed class RoadSegment
    {
        int Id; RoadNode A, B; float Length;
        Vector3 Evaluate(float distanceFromA);   // position on the polyline (arc-length parameterised)
        Vector3 Tangent(float distanceFromA);    // unit direction A→B
        IReadOnlyList<Vector3> Points;           // 2 m-resampled polyline
    }
    public struct RoadLocation { RoadSegment Segment; float Distance; Vector3 Position => Segment.Evaluate(Distance); }
    public sealed class RoadRoute
    {
        IReadOnlyList<(RoadSegment segment, bool forward)> Legs; float TotalLength;
        Vector3 Evaluate(float distance); Vector3 Tangent(float distance);
        Settlement Destination;
    }
    public sealed class RoadNetwork
    {
        IReadOnlyList<Settlement> Settlements; IReadOnlyList<RoadNode> Nodes; IReadOnlyList<RoadSegment> Segments;
        RoadLocation Project(Vector3 worldPosition);                        // nearest point on any road
        bool TryFindRoute(RoadLocation from, Settlement to, out RoadRoute route); // Dijkstra over nodes
        Settlement Nearest(Vector3 worldPosition);
    }
}
```
`RoadNetwork` must be constructible from plain data (lists of points) so the player work can
be developed and tested against a hand-built network before real generation exists.

### Player
One `PlayerRig` prefab; all three modes share it.
- `PlayerMotor` — `CharacterController` locomotion (walk 1.6 m/s, sprint = speed scale 2.2 ≈ 3.5 m/s, gravity,
  slope handling). Exposes `Move(Vector3 worldDir, float speedScale)`, `Face(Vector3 dir, float turnSpeed)`
  and `Teleport` so both manual
  input and the auto-traveller drive the same code path.
- Turning model (2026-09-27, after the animated character was seen moonwalking): in third person and
  auto-travel the motor is driven with `MoveFacing` - the body turns toward the request (300 deg/s cap),
  the velocity follows the body's forward, and speed falls as the request swings behind (full speed within
  35 deg, zero beyond 155 deg), so a reversal is slow down, arc round, walk away. First person uses `Move` +
  `Face` and may strafe; the velocity swings toward the request at 540 deg/s. Speeds are unchanged.
  Pure maths in `MotorMath`. `RouteFollower` moves the steering point on when it comes within 1.25 m
  (hairpin guard).
- Animation blend: 2D on body-relative velocity (`VelocityX`, `VelocityZ`): idle, four rings of eight
  directions (walk x0.6, x1, x1.25, run) using the pack's strafe/backward clips, sprint forward only.
  Strafe clips share the forward clips' natural speeds (walk 1.452, run 2.585 m/s).
- Grounding: the rig menus measure the lowest sole vertex over the idle and walk cycles and place the
  Visual so idle soles sit on the ground; `PlayerAnimator` lowers the Visual by the controller's skin
  width (0.04 m, kept for collision stability) plus the inspector field `groundOffset`.
- `PlayerController` — references the `InputActionAsset` (no generated C# wrapper) and resolves
  actions by path, e.g. `FindAction("Player/Move")`, `"Player/Look"`, `"Player/Sprint"`; manual
  mode: camera-relative Move, Look, Sprint. Any Move input while auto-travelling cancels it.
- `AutoTraveller` — given a `RoadRoute`, advances a distance along it at travel speed
  (default 1.4 m/s, subtle speed variation) and feeds `PlayerMotor`; the head/camera stays
  free to look around in both camera modes. Fires `Arrived`.
- `PlayerCameraRig` — one `Camera`; two anchors: first-person (eye height 1.62 m, character
  mesh set to shadows-only) and third-person (orbit, 3.5 m, pitch clamp, sphere-cast collision). Toggle
  with `V`, smooth blend. Look sensitivity/invert are serialized fields on the rig.
- Default key map: `V` camera mode, `T` toggle auto-travel (needs a destination), `M` map,
  `E` interact (`Interact` action, press), `Esc` / gamepad Start opens the Journey menu (time, weather, atmosphere, view, fast travel; frees the cursor).
- Visual: `SM_Gen_Chr_Peasent_Male_01` on the rig, animated by `PlayerAnimator` (order 110, only
  observes `PlayerMotor`; `applyRootMotion = false`) with `Assets/_Project/Animation/PlayerLocomotion.controller`:
  speed × incline blend of in-place Idle/Walk/Run/Sprint clips with playback scaled to ground speed
  (natural speeds Walk 1.45, Run 2.59, Sprint 7.24 m/s), fall/land, additive body-look, head-look and
  lean layers driven by the camera and turn rate. Uses a corrected avatar
  (`PeasantMale_Humanoid.asset`) because Synty's maps human Hips to the ground-level `Root` bone. The build
  instance must be named after the model's root (`Generic_Characters(Clone)`), not the prefab, or
  `AvatarBuilder` rejects the hierarchy.

### Travel loop
- **Destination — a physical map** (`M`): the character unfolds a parchment map and holds it
  up (user request 2026-09-26). It is a world-space quad on the player rig, not a screen
  overlay: in first person it rises into the lower half of the view, held by the hands; in
  third person the camera eases in over the shoulder to frame it. The parchment texture is
  rendered once per world from the heightmap in a hand-drawn style (hillshade, tree symbols
  for forests, lakes, dirt roads, settlement icons + names, landmarks such as the tallest
  peak), plus a "you are here" marker and the current destination. While open, look input
  moves a small pointer over the paper (ray → quad); click/`Interact` on a settlement sets it
  as destination; `M`/`Esc` folds it away. Time keeps running; walking with the map open is
  allowed at a slower pace. Signposts at junctions (`Interact`) offer the same choice for the
  settlements they point to. The parchment is painted at scene start by `MapTextureRenderer`
  (runtime, cached per session); names and live markers are a world-space canvas (`MapOverlay`).
- Auto-travel starts up to 60 m off-road with a straight approach leg to the road; if stuck
  (< 0.2 m/s for 3 s) it reprojects once, then stops with "The way is blocked".
- Auto-travel route = `RoadNetwork.TryFindRoute(Project(player), destination)`.
- Arrival: settlement trigger → toast "You arrive at <name>". At an inn, `Interact` → rest:
  fade to black, time skips to 06:00, fade in.
- **Sitting** (user request 2026-10-02): `C` (gamepad East, the existing `Crouch` action) sits the traveller where
  they stand, or on a seat within 2.5 m (log, stump, crate, chair, low rock, camp-fire surround, inn doorstep),
  facing the best view (downhill, water, away from walls; the fire at a camp). `C`, `E` or moving stands up;
  auto-travel pauses and resumes. Holding `Shift` while seated ramps time to ×20 of the chosen flow
  (`DayCycle.FastForward`). The pose is procedural (`SeatedPose`, order 120: the pack has no sit clip): hips to
  the seat, a two-bone solve per leg with planted feet, hands on thighs or knees; every angle is an inspector
  field. `PlayerSeat` (order −42) owns the state.
- **Cinematic camera** (user request 2026-10-02): `B` (gamepad d-pad up) toggles it; it starts by itself after 6 s
  without input during auto-travel (setting, default on, third person) and 1.5 s after sitting down.
  `CinematicDirector` (order −35) picks shots of 9–22 s with a seeded, context-weighted scheduler that never
  repeats a type back to back: trailing wide, side tracking, leading, roadside tripod, crane, vista, low grass;
  rest shots: push-in, profile, high orbit, over-the-shoulder, sky hold, across the fire. The camera never goes
  under ground or water or inside a tree, stays within the built near tiles and 200 m of the traveller, and
  changes shot if the traveller is hidden for a second. Nearby shots blend; others dip to black for 0.25 s.
  Look input orbits the shot; move input ends it. Pure core: `ShotSolver`, `CinematicMath`. Journey menu → View:
  Camera gains "Cinematic", plus "Cinematic when travelling" and "Letterbox" (settings version 2).
- **Journey menu** (`Esc` / Start; user request 2026-09-27): a right-hand side panel built by the HUD at
  runtime, so it needs no scene changes (a HUD is created for scenes without one). Pages Time / Sky /
  View / Where to?; every change applies live behind the panel. The time slider and the Dawn / Noon /
  Dusk / Night buttons set the hour on the current day (`DayCycle.SetHourOfDay`; the day counter never
  changes); Flow of time sets `DayCycle.TimeScale` (0, 0.5, 1, 2, 4). Weather choices use
  `WeatherSystem.Force(state, 5 s)`. Fast travel uses `FastTravelPlanner` (settlement: its own road,
  radius + 6 m out, facing the centre; landmark: nearest dry, gentle road point, facing it) with a HUD
  fade. Preferences persist in PlayerPrefs `MedievalZen.JourneySettings` (versioned JSON; the time of
  day is not saved). It only exposes controls: it never changes the defaults of the systems it drives.
- `DayCycle`: 24-minute day by default; rotates the sun, drives ambient/fog colour and the
  Synty skydome tint; time of day shown on the HUD. Weather: hook Synty `WeatherControl`
  from `PNB_Core` if it works under URP; otherwise a later iteration.

### Editor tooling
- `MedievalZen/Setup Demo Scene` / `Open Demo Scene` — generates the world via
  `WorldGeneratorMenu.GenerateWorld`, then adds `DayCycle` (owns the sun), a player-following
  `Sky` root (skydome, cloud ring, fog ring), the TimeOfDay grading volume (see below), `WeatherControl`,
  the HUD canvas + EventSystem and `AmbientFx`; saves `Assets/_Project/Scenes/Demo.unity`.
- `MedievalZen/Generate World` — runs generation for the selected `WorldSettings` (creates the
  palette if missing, places the `PlayerRig` and a sun if the scene has none). Also
  `MedievalZen/Create Default World Palette`, `MedievalZen/Regenerate With New Seed`,
  `MedievalZen/Clear Generated World`.
- `MedievalZen/Player/Create Player Rig Prefab` → `Assets/_Project/Prefabs/PlayerRig.prefab`.
- `MedievalZen/Player/Create Player Test Scene` → `Assets/_Project/Scenes/PlayerTest.unity`: flat
  ground, dirt road ribbon and the rig, built from `TestRoadNetworks.Triangle()` (3 settlements + junction).
- `MedievalZen/Player/Build Locomotion Controller` (avatar + `PlayerLocomotion.controller`; logs measured
  clip speeds and look extents) and `MedievalZen/Player/Upgrade Player Rig (Add Animation)` (patches
  `PlayerRig.prefab` in place; scenes pick it up without regenerating the world).
- `MedievalZen/Repaint Terrain` — repairs/refreshes ground paint on the existing world (see Terrain
  persistence and paint).
- `MedievalZen/Atmosphere/Apply To Open Scene` — idempotent; `Setup Demo Scene` calls it. Adds the
  "AtmosphericFog" Full Screen Pass to `PC_Renderer` (before post-processing, depth input), sets
  shadows on `PC_RPAsset` (180 m, 4 cascades, 4096 atlas), creates the TimeOfDay grading volume
  (`Assets/_Project/Settings/Atmosphere/TimeOfDay.asset`) and the Atmosphere object
  (`AtmosphereController` with the `Fog Amount` / `Haze Distance` master sliders, `WeatherSystem`,
  `MistField`, optional procedural wind audio), rain under the sky root, and `SettlementAmbience`
  (chimney smoke, window lights, campfires; 16-light global cap) on every settlement.
- Post-processing: Synty's `Meadows_Post_Processing_01.asset` is a Post Processing Stack v2 profile and
  does nothing under URP; all grading comes from the TimeOfDay volume.
- Clouds (fixed 2026-09-27 after they rendered as saturated blue cut-outs): the Synty cloud graph is
  URP Unlit and **opaque**; it blends toward `unity_FogColor` with an unclamped lerp and applies
  scattering with a per-channel weight, so it cannot be trusted to follow the atmosphere. Both are
  switched off per renderer (`_Enable_Fog = 0`, `_Enable_Scattering = 0` via MaterialPropertyBlock) and
  all cloud colours come from `CloudColorModel`, driven by `DayCycle`: sun-tinted near-white lit side,
  greyed horizon/fog mix on the shaded side, weather tint, then a pull toward the fog colour, every
  colour capped to `cloudSaturation`. In the fog pass, cloud-ring pixels are part of the sky layer and
  get a haze tail by elevation (up to 22-50 degrees, never above 60; zenith stays clear). User control:
  `Cloud Softness` (0-2) on `AtmosphereController`.
- Built-in fog while the full-screen pass is live: enabled as Linear 20-40 km with the atmosphere fog
  colour, i.e. factor 0 on anything nearer than 20 km. It exists only to keep fog shader variants and
  Lit shaders consistent; the pass does the look. Grading: profile saturation +6, golden hour +6.
- Fog fallback: if the full-screen pass is missing or unsupported (e.g. the Mobile quality level),
  `AtmosphereController` switches to built-in exponential-squared fog with the same day curves.
- Gizmos: road polylines, nodes, settlement radii.

## Delegation plan and status
Status as of 2026-09-27 (compile check green; last in-editor test run 87/87).

| Phase | Work | Depends on | Status |
|---|---|---|---|
| 0 | Scaffold: folders, asmdefs, `WorldSettings`, `MedievalZen.Roads` contract with a `RoadNetwork` builder from plain data, input wrapper, CLAUDE.md | — | done |
| 1 | World generation (steps 1–9) and the `Generate World` menu | 0 | done — relief, lakes, 8 terrain layers, scatter rules, details, horizon |
| 2 | Player rig: motor, controller, auto-traveller, camera rig, test scene with a hand-built road | 0 (runs in parallel with 1) | done — tuning validated by the user |
| 3 | Integration: demo-scene setup, held map, signposts, arrival/inn rest, day cycle, HUD | 1, 2 | done — needs a full playtest by the user |
| 1c | The mountain pass (user request 2026-09-27): massif reshaped from a plateau into a range (peaks, valleys, a saddle), a mountain inn high in the range, a pass road with grade-limited switchbacks linking it to the lowland from both sides (adds a loop), cairns / viewpoint / camp along the climb, tree line, pass and viewpoint landmarks on the map. Requires `Generate World` again | 1 | done - checked offline (previews, 26 tests, auto-travel simulation); needs an in-editor look by the user |
| E0 | Endless Road mode (user request 2026-10-01, see `Docs/ENDLESS_ROAD.md`): contract in `Scripts/Endless/Contract/` and design | — | done |
| E1 | Endless Road: pure generator (road, regions, global heights, tile blueprints reusing the finite stages with an absolute origin), tests, offline previews | E0 | done — pure generator, previews, 22 tests; finite world bit-identical (golden hashes); needs an in-editor look |
| E2 | Endless Road: streaming runtime (worker-thread generation, sliced apply, far silhouettes, floating origin), adapters for shared systems, scene menu, travel, HUD, Journey page, persistence | E0 | done — checked offline (22 tests, compile); needs an in-editor look |
| 3b | Atmosphere (user request 2026-09-27: "improve the looks adding some fog and ambience"): full-screen height + distance fog with sun scattering, `AtmosphereController`, `WeatherSystem` (clear/hazy/misty/overcast/light rain), mist patches over lakes and valleys, longer shadows, time-of-day grading, settlement lights + chimney smoke, richer ambient life; applies to an existing scene via `MedievalZen/Atmosphere/Apply To Open Scene` | 3 | done - fog and clouds verified in the editor by the user (2026-09-27); weather states, settlement lights and night still to be looked at |
| 2b | Character animation: `PlayerLocomotion.controller` built from Synty Base Locomotion in-place clips, `PlayerAnimator`, optional map-holding hand IK | 2, 3 | done - verified in the editor by the user (2026-09-27): avatar, grounding, turning feel |

Open items after 2b: user playtest of the whole loop in `Demo.unity`; first git commit (decide
how to handle the ~68 MB generated `TerrainData` and ~21 MB scene — Git LFS or regenerate from
seed); ambient audio (no audio assets in the project yet).

## Compile-checking without the editor
The Unity editor is usually open (`Temp/UnityLockfile` exists), so batch-mode compiles are
not possible and `Library/ScriptAssemblies` only refreshes when the editor recompiles. The
.NET SDK (8.0.200) is installed: build a scratch `.csproj` in the session scratchpad that
targets `netstandard2.1`, `LangVersion 9`, references every DLL in
`C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Data\Managed\UnityEngine\` and in
`Library/ScriptAssemblies/` (Unity.InputSystem, Unity.RenderPipelines.Universal.Runtime, …),
and globs `Assets/_Project/**/*.cs` (editor assembly separately, adding `UnityEditor.dll`
from `...\Editor\Data\Managed\`). `dotnet build` on it catches type and syntax errors; the
editor remains the final judge.

### Golden hashes under Mono
The editor runs C# on its embedded Mono (`Data\MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll`, float32 off): float
arithmetic is evaluated in double and rounded only where the JIT stores a value, so a new local, a branch or an inlined helper
can move last bits under Mono while CoreCLR stays bit-identical (the standalone `bin\mono.exe` is another build and gives other
values: never record goldens with it). Check "golden hashes unchanged" on that runtime with
`powershell -NoProfile -ExecutionPolicy Bypass -File Tools\MonoCheck\run.ps1 [-Filter EndlessGeneratorTests.FiniteWorld] [-Golden] [-Clr]`
(builds `Assets/_Project` and hosts the editor's runtime in PowerShell; `-Clr` runs the same build on .NET 8). Never pass a file in
`Library\ScriptAssemblies` to a build or program (copy it first), and build with `-p:UseSharedCompilation=false`.
The finite splat is a frozen copy (`TerrainShaper.ComputeFiniteSplatAsOf2026_10_02`): do not edit it.

## Later ideas (not now)
Horse/cart riding; rivers and bridges (`PolygonGeneric` river ground pieces, Meadow
`SM_Prop_Bridge_01`); a proper Synty medieval pack (POLYGON Fantasy Kingdom) for towns;
travelling NPCs and carts on the roads; seasons; animations beyond locomotion (Mixamo or further Synty animation
packs); ambient audio; save/load; a journal of places visited.
