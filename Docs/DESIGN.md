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
kit, custom shaders, rivers (lakes with a water plane are in), horses/carts (auto-travel is "walking on autopilot").

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
  POLYGON rig. The player is still a static mesh until "Phase 2b" wires them in.

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
  `E` interact (`Interact` action, press), `Esc` pause (toggles cursor lock).
- Visual: `SM_Gen_Chr_Peasent_Male_01` on the rig — static until Phase 2b adds an Animator
  built from the Base Locomotion clips (in-place clips only; `PlayerMotor` stays the movement authority).

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
- `DayCycle`: 24-minute day by default; rotates the sun, drives ambient/fog colour and the
  Synty skydome tint; time of day shown on the HUD. Weather: hook Synty `WeatherControl`
  from `PNB_Core` if it works under URP; otherwise a later iteration.

### Editor tooling
- `MedievalZen/Setup Demo Scene` / `Open Demo Scene` — generates the world via
  `WorldGeneratorMenu.GenerateWorld`, then adds `DayCycle` (owns the sun), a player-following
  `Sky` root (skydome, cloud ring, fog ring), the Meadow post-processing Volume, `WeatherControl`,
  the HUD canvas + EventSystem and `AmbientFx`; saves `Assets/_Project/Scenes/Demo.unity`.
- `MedievalZen/Generate World` — runs generation for the selected `WorldSettings` (creates the
  palette if missing, places the `PlayerRig` and a sun if the scene has none). Also
  `MedievalZen/Create Default World Palette`, `MedievalZen/Regenerate With New Seed`,
  `MedievalZen/Clear Generated World`.
- `MedievalZen/Player/Create Player Rig Prefab` → `Assets/_Project/Prefabs/PlayerRig.prefab`.
- `MedievalZen/Player/Create Player Test Scene` → `Assets/_Project/Scenes/PlayerTest.unity`: flat
  ground, dirt road ribbon and the rig, built from `TestRoadNetworks.Triangle()` (3 settlements + junction).
- Gizmos: road polylines, nodes, settlement radii.

## Delegation plan and status
Status as of 2026-09-27 (compile check green; last in-editor test run 87/87).

| Phase | Work | Depends on | Status |
|---|---|---|---|
| 0 | Scaffold: folders, asmdefs, `WorldSettings`, `MedievalZen.Roads` contract with a `RoadNetwork` builder from plain data, input wrapper, CLAUDE.md | — | done |
| 1 | World generation (steps 1–9) and the `Generate World` menu | 0 | done — relief, lakes, 8 terrain layers, scatter rules, details, horizon |
| 2 | Player rig: motor, controller, auto-traveller, camera rig, test scene with a hand-built road | 0 (runs in parallel with 1) | done — tuning validated by the user |
| 3 | Integration: demo-scene setup, held map, signposts, arrival/inn rest, day cycle, HUD | 1, 2 | done — needs a full playtest by the user |
| 2b | Character animation: `PlayerLocomotion.controller` built from Synty Base Locomotion in-place clips, `PlayerAnimator`, optional map-holding hand IK | 2, 3 | in progress (started 2026-09-27) |

Open items after 2b: user playtest of the whole loop in `Demo.unity`; first git commit (decide
how to handle the ~35 MB generated `TerrainData` and ~21 MB scene — Git LFS or regenerate from
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

## Later ideas (not now)
Horse/cart riding; rivers and bridges (`PolygonGeneric` river ground pieces, Meadow
`SM_Prop_Bridge_01`); a proper Synty medieval pack (POLYGON Fantasy Kingdom) for towns;
travelling NPCs and carts on the roads; seasons; animations beyond locomotion (Mixamo or further Synty animation
packs); ambient audio; save/load; a journal of places visited.
