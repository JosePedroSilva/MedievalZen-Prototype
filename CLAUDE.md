# MedievalZen

Relaxed medieval travel simulator ("Slow Roads, but on foot"): procedurally generated
countryside, dirt roads between towns/villages/inns, no objective. Iteration 1 uses Synty
placeholder art. Full design and the iteration plan: `Docs/DESIGN.md` — read it first.

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
- Manual input and auto-travel both drive `PlayerMotor`; do not add a second movement path.
- Player-side scripts run in a fixed order via `[DefaultExecutionOrder]`: DayCycle −60,
  PlayerController −50, HeldMap −45, AutoTraveller −40, PlayerInteractor −30, HudController 50,
  PlayerMotor 100; a driver calls `PlayerMotor.Move` once per frame in Update. Overlays (held map,
  signpost list) take input through `PlayerController.InputOverride` (`IPlayerInputOverride`),
  never a second input path.
- Keep the "zen" pacing: slow speeds, smooth cameras, no pop-in near the road.

## Working in this repo
- The Unity editor is usually open (`Temp/UnityLockfile` present) → never launch Unity in
  batch mode; compile-check with the scratch-csproj approach described in `Docs/DESIGN.md`.
- Scenes/prefabs are YAML; create them via editor scripts (`MedievalZen/...` menu items)
  rather than hand-writing `.unity`/`.prefab` files.
- Don't commit unless asked. `.gitignore` already excludes `Library/`, `Temp/`, IDE files.
