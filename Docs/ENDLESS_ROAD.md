# Endless Road — design and plan

A second game mode beside the finite world (`Docs/DESIGN.md`), requested by the user on 2026-10-01:
"more in the direction of the original game that inspired this [Slow Roads] … procedurally generated
while the player walks, keeping the dynamic time and weather, but a single road." The finite world
(`Demo.unity`, `Generate World`) stays exactly as it is; both modes share the player, time, weather,
atmosphere and every content rule.

## The experience
You start at an inn in the morning. One road leads on. The landscape is generated around you as you
walk and changes character every couple of kilometres: meadows, deep forest, lake country, rolling
downs, farmland with a village on the road, a long climb through a mountain valley to a pass and down
the other side. Every 1.2–2.4 km there is an inn where you can rest until morning. There is no
destination and no end; you can also turn round and walk back, and the same seed always gives the same
road. Time and weather run as in the finite world, and the Journey menu still lets you change them.

## Principles
- **Road first.** The road is defined before the terrain and the terrain is shaped around it, so the
  road is always walkable (grade ≤ `maxRoadGrade`, 8 %) without a router or switchbacks.
- **Everything is a pure function of the seed and absolute position.** Any tile can be generated at any
  time, in any order, on any thread, and comes out identical. No state is carried along the walk.
- **One set of content rules.** Scatter rules, rock seating (`RockPlacer`), grass/flower details, ground
  paint and the palette are shared with the finite world; a fix in one shows in both. The finite
  world's output must not change (guarded by golden hashes).
- **Never a hitch, never a pop near the road.** Generation runs on worker threads; finished tiles are
  applied in small slices; new ground appears in the haze, not in front of the traveller.

## World model
- **Axes.** The road travels generally toward +Z. It is the graph of a function, `x = R(z)`: it winds
  but never doubles back (heading stays within about ±60° of +Z), so every absolute point has a unique
  place along the journey. Distance along the road `s` is measured from the journey's start and is
  defined for negative values too.
- **Corridor.** A slow curve `C(z)` (the valley or trend line the country follows) plus a faster meander
  gives `R(z)`. The road's height `E(s)` is an integrated grade profile set by the regions, limited to
  `maxRoadGrade` and smoothed, so climbs are long and even.
- **Regions.** A seeded sequence of stretches 1.5–3.5 km long (`RegionKind`): Meadow, Forest,
  LakeCountry, Downs, Farmland, HighlandPass. Rules keep it believable (a pass is approached through
  Downs or Forest, never two passes in a row, the journey starts in Meadow or Farmland). Neighbouring
  regions blend over 300–500 m. Each region has a generated name ("The Wexmoor Downs").
- **Terrain height** at an absolute point is the road height at that `z`, plus a lateral profile that
  depends on the region and on the distance from the corridor (flat-ish floor, then the region's
  character: gentle swells, wooded banks, lake basins set to one side, rock walls rising to ridges in
  a pass), plus global noise (domain-warped, with ridged detail on high ground and the cliff bands,
  gullies and scree of the finite world's mountains). The road bed is benched in with cut and fill
  banks exactly as in the finite world.
- **Water.** Lakes sit in basins beside the road in LakeCountry (and occasionally elsewhere); each has
  one level, and no tile ever needs two levels. The road keeps to the shore.
- **Waypoints** (`Waypoint`): inns every 1.2–2.4 km (a high refuge at the top of a pass counts as one);
  a hamlet or village in Farmland with the road running through it; viewpoints, camps, cairns along
  the upper pass and a large one at the top; milestones every 500 m. Dressing reuses the finite world's
  settlement dresser and roadside features.
- **Tiles.** A square grid of 256 m tiles in absolute coordinates. Near tiles (within ~420 m) are full
  detail: 129² heights (2 m), 256² paint (1 m), 128² grass maps, trees, props, collider. Far tiles (out
  to ~2.2 km) are silhouettes: 33² heights, 64² paint, large trees only, no collider. All tiles share
  one height range so edges meet exactly.

## The contract between generation and runtime
`Assets/_Project/Scripts/Endless/Contract/` (namespace `MedievalZen.Endless`), written before either
half so both can be built in parallel:

| Type | Role |
|---|---|
| `IEndlessGenerator` | The world as a pure function: `Road(s)`, `NearestS`, `Region(s)`, `Waypoints`, `Height`, `WaterHeight`, `GenerateTile(coord, lod)`. Deterministic, order-independent, thread-safe, no native Unity calls. |
| `TileBlueprint` | One tile as plain data: normalised heights, 8-channel splat, detail layers, trees, props, water level, waypoints. Positions are tile-local. |
| `TileCoord`, `TileLod` | Absolute tile index; `Near` or `Far`. |
| `RoadPoint`, `RegionInfo`/`RegionKind`, `Waypoint`/`WaypointKind` | Road sample, region, notable place. |
| `EndlessParams` | Plain generator inputs. |
| `EndlessSettings` | ScriptableObject: world inputs plus streaming distances and budgets. |

All coordinates in the contract are **absolute** (doubles). The runtime owns a floating origin and
subtracts it before anything reaches the scene.

## Generation (pure) — `Scripts/Endless/Generation/`
- `EndlessGenerator : IEndlessGenerator` with constructor `(EndlessParams, PaletteKeys)`.
- Road, corridor, elevation profile, region sequence and waypoint schedule are computed in fixed steps
  outward from `s = 0` and cached in a thread-safe, order-independent table.
- A tile is generated by reusing the finite world's stages on a tile-sized window: the heightmap is
  sampled from the global height function, and `LandCover`, `TerrainShaper.ComputeSplat`,
  `ScatterPlanner`, `RockPlacer`, `DetailPlanner` and the settlement dresser run on it. Those stages
  gain an absolute origin so their noise and patch patterns continue across tile edges; with origin
  (0, 0) the finite world is bit-identical to before.
- Offline verification: strip previews of several kilometres of road, ground-level views along it,
  elevation and grade profiles, seam checks (heights exact; paint, details and scatter continuous),
  determinism under shuffled and concurrent generation, and timing per tile.

## Runtime (Unity) — `Scripts/Endless/Runtime/`
- `EndlessWorld` streams tiles around the traveller: requests by priority (ahead along the road first),
  generates on worker threads, applies finished blueprints in slices within `applyBudgetMs` per frame
  (heights, paint, trees, each detail layer, pooled props, collider last), sets terrain neighbours,
  swaps Far for Near as the traveller approaches, and releases tiles that fall behind.
- **Floating origin**: scene position = absolute − origin. The origin moves by whole tiles when the
  traveller is more than `originShiftDistance` from the scene origin; everything under the world root,
  the player, the sky and pooled effects move together in one frame, and the fog's noise offset is
  compensated so nothing visibly jumps. A resumed journey starts with the origin at the traveller.
- **The shared systems keep working through adapters**, with no change to how they behave in the finite
  world: a `GameWorld` in the scene is kept supplied with a road network for the stretch around the
  traveller (so auto-travel, the HUD, arrival and inn rest work unchanged) and with nearby landmarks;
  systems that read "the terrain" (mist, ambient effects, fog base altitude, tree density) ask a small
  surface interface that both modes implement.
- **Travel.** `T` walks on to the next inn (or the previous one if you are facing back). Arriving and
  resting work as in the finite world. The HUD shows the region you are in, how far you have walked and
  how far it is to the next inn; entering a region shows its name.
- **Journey menu.** Time, Sky and View are unchanged. "Where to?" becomes "The road": distance walked,
  current region, the next and previous inn with "Go now", "New journey" (new seed) and the seed.
- **Persistence.** Seed and distance along the road are saved, so a journey can be continued.
- **Scene.** `MedievalZen/Endless Road/Setup Scene` creates `Assets/_Project/Scenes/EndlessRoad.unity`
  (streamer, player rig, day cycle, sky, atmosphere, HUD). No terrain is saved in the scene, so it stays
  small and versionable.

### Runtime as built (phase E2)
- `EndlessWorld` runs at execution order −70. Planning every 0.25 s or 8 m: Near within `nearDistance`, Far within
  `farDistance`, a tile keeps its level out to that distance + `releaseMargin`; all Near before Far, biased ahead.
- Apply is an iterator with one bounded Unity call per step (heights, LOD sync, alphamap bands, trees, one detail
  layer, one prop, water, places, collider last, swap + neighbours), run inside `applyBudgetMs`; up to 3 builds
  interleave. A replacement tile is built while the old one stays visible and swapped in one step.
- **Far tiles are plain meshes, not Unity Terrains** (since 2026-10-02): one vertex-coloured mesh each
  (`FarTileMeshData`, built on the worker; `MedievalZen/FarTerrain` shader): ground 1.5 m below the true heights
  so the coarse edge sits under the near tiles, a 6 m skirt, low-poly tree impostors, at most 8192 triangles. No
  collider, grass, props or shadows.
- **No Unity terrain trees in this mode.** Near tiles are Unity Terrains for ground, paint, grass and the
  collider only; `EndlessTreeRenderer` draws near trees instanced (32 m cells; LOD0 within 40 m, the middle LOD
  to 140 m, the lowest beyond) with pooled trunk capsules within 25 m. The finite world keeps terrain trees.
- **Why:** the first version built every far tile as a Terrain with terrain trees. With about 200 terrains the
  graphics device hung (D3D12 `DEVICE_HUNG`, not out of memory) and the editor died without a console error.
  The nine PolygonGeneric trees have no LODGroup, so Unity treats them as legacy billboard trees per terrain.
- **Safety ladder.** `endless_run.json` in `Application.persistentDataPath` records each run. A run that
  neither rendered for 20 s nor exited cleanly makes the next start one level lower: 0 everything, 1 no far
  trees, 2 no far tiles, 3 also no near trees, 4 also no grass, 5 ground, road and player only. The level and
  the feature toggles are on `EndlessWorld` and in the F3 overlay; `MedievalZen/Endless Road/Reset Safety Level`
  returns to 0. A few `Endless road load:` log lines record the load so the last one before any hang is useful.
- **Holds.** At start the screen is black until every near tile and the far tiles within 1100 m exist and the
  ground under the traveller is confirmed. If the traveller ever stands on an unbuilt tile the controller is
  switched off behind a fade until it exists. "Go now" fades, moves, waits for tiles, fades in.
- **Floating origin** sits on a tile corner and shifts by whole tiles in one frame: tile roots, the player
  (controller off for the move), sky followers, every `IOriginShiftable` (ambient effects, mist, the fog's noise
  offset), live world-space particles and the road window, with auto-travel re-issued.
- **Adapters.** `IWorldSurface` (ground, water, tree density, fog base) is implemented by `GameWorld` over its
  terrain and by the endless world; the road window (−600 … +2600 m, nodes every 400 m and at each settlement)
  is rebuilt near its ends and keeps a trip going across rebuilds and shifts.
- **Keys.** `F3` streaming overlay (tiles, generation and apply times, worst spike and its slice); `F4` forces an
  origin shift. `T` walks to the next inn in the facing direction. `M` reports that there is no map yet.
- **Menus.** `MedievalZen/Endless Road/Setup Scene` (offers Rebuild or Repair references on an existing scene, and
  verifies every reference after saving), `Open Scene`, `Forget Saved Journey`, `Reset Safety Level`. The journey (seed,
  distance, day, hour) is saved in PlayerPrefs `MedievalZen.EndlessJourney`; a saved journey overrides the seed
  in the settings asset.
- The generator is found by type name (`MedievalZen.Endless.Generation.EndlessGenerator`); a built-in stub is
  used if it is missing or `useStubGenerator` is ticked.

### Generator as built (phase E1)
- **Road.** Heading = slow swing (about 5 km wavelength) + meander (about 430 m), limited to ±57°; tightest bend
  over 100 m. Road and arc length are integrated outward from the start in 4 m steps and cached
  (`OutwardTable`: lock only when growing).
- **Elevation.** Level through settlements and wherever the road is inside a lake's zone; a constant grade up to
  each col; otherwise a pull toward the region's target altitude plus undulation. |grade| ≤ 0.97 × the limit.
  Targets: Farmland 48–66 m, Lake 44–58, Meadow 62–88, Forest 82–116, Downs 118–152, cols 230–255.
- **Regions.** Never the same kind twice; a pass only after Downs or Forest, at least 5 regions apart, followed
  by Downs or Forest; lake country at least 4 apart; every kind appears within any 8 consecutive regions (about
  22 km at most). A stricter rule produced a fixed cycle that looked mechanical.
- **Terrain recipes.** Meadow: 10 m swells and knolls. Farmland: flattest, with a field patchwork (crops planted
  only within 110 m of the road; further out it is paint). Forest: banks and hollows, dense wood with clearings.
  Downs: 24 m swells and short scarps. Lake country: lake side low, fells on the other. Highland pass: a valley
  floor 17–80 m wide, ridged mountains rising to crests 55–145 m above the col, walls only where ridges reach
  the valley, side valleys between them; cols sit at or above the tree line.
- **Water.** One or two lakes per lake-country region, level 1.8 m below the road, which is level wherever it
  passes through a lake's zone; no ground in that zone dips below the level.
- **Places.** Inns at the start, at every col (a refuge: an Inn waypoint plus a separate Pass waypoint) and
  between them at 1.4–2.4 km; one hamlet or village per Farmland region (they reuse the finite village and town
  layouts); viewpoints, camps, cairns every 100 m on the upper pass, milestones every 500 m. `Waypoints()` is
  allocation-free.
- **Tiles** reuse the finite stages through `WorldGenParams.window`; settlements are dressed once in their own
  window and cached (`DressingCache`). Near tiles carry at most 16 grass layers.
- **Cost.** Near tile about 60 ms in .NET 8 but about 250 ms under Unity's Mono; far tile about 5 ms / 18 ms.
- **Known weak points.** Pass walls are large smooth rock slopes up close; climbs are one constant ramp with no
  level stretches; villages are sparse around a large dirt square; the field grid is regular within each
  kilometre cell.

## Phases
| Phase | Work | Depends on |
|---|---|---|
| E0 | Contract and this document | — |
| E1 | Pure generator, tests, offline previews | E0 |
| E2 | Streaming runtime, adapters, scene, travel, HUD, menu page, persistence (built against a stub generator) | E0, in parallel with E1 |
| E3 | Join E1 and E2, tune in the editor from the user's screenshots | E1, E2 |
| Later | A strip map for the held parchment (`M`), streams and bridges, an arid region from the Arid Desert pack, seasons, travellers on the road | E3 |

## Out of scope for the first playable
The held map in endless mode (pressing `M` says there is no map of this road yet), streams and bridges,
junctions and side roads, switchbacks, more than one road.
