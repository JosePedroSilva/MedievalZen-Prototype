# Assets/_Project — first-party content (see `Docs/DESIGN.md`)

- `MedievalZen.asmdef` — runtime assembly; namespaces follow folders (`MedievalZen.Roads`, …).
- `Scripts/World/` — `WorldSettings` (inputs), `GameWorld` (scene owner of terrain + roads), `WorldGenerator` (pure pipeline → `WorldBlueprint`: `HeightmapGenerator`, `SettlementPlacer`, `RoadPlanner`, `TerrainShaper`, `ScatterPlanner` + `ScatterRules`, `DetailPlanner`, `SettlementDresser`, `SignpostPlanner`, `LandmarkFinder`, `HorizonPlanner`), `WorldBuilder` (Unity realisation), `WorldPalette` + `SyntyCatalog` (asset roles → Synty paths).
- `Scripts/Roads/` — pure-C# road graph: `Polyline`, `RoadNetwork(Builder)`, routing, `RoadNetworkData`.
- `Scripts/Player/` — `PlayerMotor`, `PlayerCameraRig`, `PlayerController`; `Scripts/Travel/` — `RouteFollower`, `AutoTraveller`, `PlayerTravel`. `Scripts/Roads/` also holds `RoadRibbonRenderer` (debug visual) and `TestRoadNetworks`. `Scripts/Environment/` — `DayClock`/`DayCycle`, `SkyFollower`, `AmbientFxSpawner`, `TreeDensityGrid`; `Scripts/UI/` — `HudController`, `UiKit`; `Scripts/Travel/` also holds the held map (`HeldMap`, `MapTextureRenderer`, `MapOverlay`, `MapProjection`), signposts/inns (`Interactable`, `PlayerInteractor`, `SignpostInteractable`, `SignpostMenu`, `InnRest`), `ArrivalAnnouncer`, `StuckDetector`.
- `Editor/` — `MedievalZen.Editor` assembly (namespace `MedievalZen.EditorTools`): `WorldSettingsMenu`, `WorldPaletteMenu`, `WorldGeneratorMenu`, `PlayerSetupMenu` (`MedievalZen/...` menu items).
- `Tests/EditMode/` — NUnit edit-mode tests (Window > General > Test Runner).
- `Prefabs/ Scenes/ Materials/` — generated or authored assets; `Settings/` holds `WorldSettings` assets.
- Compile-check outside the editor: `powershell -File Tools/CompileCheck/check.ps1`.
- Never edit `Assets/Synty/**`; reference its prefabs and textures instead.
