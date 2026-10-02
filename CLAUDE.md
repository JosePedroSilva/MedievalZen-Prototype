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
- Clouds are coloured only by `DayCycle` + `CloudColorModel`; Synty's own cloud fog and scattering are
  switched off per renderer. Do not tint clouds by multiplying the Synty material colours.
- Systems that read the ground (height, water, tree density, fog base) ask `GameWorld.Current.Surface`
  (`IWorldSurface`), never `GameWorld.Terrain`: the endless mode has many terrains and a moving origin.
  Components that hold world positions across frames implement `IOriginShiftable`.
- Manual input and auto-travel both drive `PlayerMotor`; do not add a second movement path.
- Player-side scripts run in a fixed order via `[DefaultExecutionOrder]`: GameWorld −100, EndlessWorld −70, DayCycle −60, WeatherSystem −58,
  AtmosphereController −55, PlayerController −50, HeldMap −45, JourneyMenu −44, PlayerSeat −42, AutoTraveller −40, CinematicDirector −35, PlayerInteractor −30, HudController 50,
  PlayerMotor 100, PlayerAnimator 110 (observes the motor only, no root motion), SeatedPose 120 (bone blend after the Animator); a driver calls the motor once per frame in Update: `MoveFacing` (third person and auto-travel: facing and travel direction are one thing) or `Move` + `Face` (first person: strafing allowed). Overlays (held map,
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
- Scenes/prefabs are YAML; create them via editor scripts (`MedievalZen/...` menu items)
  rather than hand-writing `.unity`/`.prefab` files. In those scripts load assets AFTER
  `EditorSceneManager.NewScene(Single)`: it unloads unused assets, and references taken before it serialise as
  null. Verify references after saving.
- Many Unity Terrains are expensive, and legacy terrain trees (prefabs without a LODGroup) multiply the cost:
  about 200 terrains hung the GPU. In the endless mode only near tiles are Terrains and trees are instanced.
- Don't commit unless asked. `.gitignore` already excludes `Library/`, `Temp/`, IDE files.
