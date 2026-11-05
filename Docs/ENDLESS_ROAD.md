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
  collider only; `EndlessTreeRenderer` draws near trees instanced: LOD0 within `treeFullDetailDistance` (110 m), the
  middle mesh LOD to `treeMidDetailDistance` (300 m), the simplest MESH LOD beyond, never a flat card (user: cards are
  "really noticeable and quite bad"); per-tree hysteresis; shadows from the simplest mesh within `treeShadowDistance` (300 m since 2026-10-02, with the pipeline's shadow distance raised from 180 m to 300 m and cascades at 15 / 45 / 110 / 300 m; it was 90 m and trees on the next hill looked afloat) with pooled trunk capsules within 25 m. The finite world keeps terrain trees.
- **Why:** the first version built every far tile as a Terrain with terrain trees. With about 200 terrains the
  graphics device hung (D3D12 `DEVICE_HUNG`, not out of memory) and the editor died without a console error.
  The nine PolygonGeneric trees have no LODGroup, so Unity treats them as legacy billboard trees per terrain.
- **Safety ladder.** `endless_run.json` in `Application.persistentDataPath` records each run. A run that
  neither rendered for 20 s nor exited cleanly makes the next start one level lower: 0 everything, 1 no forest extras
  (since 2026-10-05: ferns, undergrowth, ivy, forest light and mist), 2 also no far trees, 3 no far tiles, 4 also no near trees or
  animals, 5 also no grass, 6 ground, road and player only. The level and
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
  origin shift. `T` walks to the next inn in the facing direction. `M` raises the scroll map (see Scroll map).
- **Menus.** `MedievalZen/Endless Road/Setup Scene` (offers Rebuild or Repair references on an existing scene, and
  verifies every reference after saving), `Open Scene`, `Forget Saved Journey`, `Reset Safety Level`. The journey (seed,
  distance, day, hour) is saved in PlayerPrefs `MedievalZen.EndlessJourney`; a saved journey overrides the seed
  in the settings asset.
- The tile builder registers seat props with `SeatIndex`; `EndlessWorld.TreeBlocksSight` / `InsideTree` answer the
  cinematic camera from the instanced tree cells; inn door points face out of the door.
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

## Animals and animal events (2026-10-02)
Built on the polyperfect pack (`Docs/ASSET_CATALOG.md` → Animals); code in `Scripts/Wildlife`. User decisions: gentle
tone with chases where the prey always escapes; positional animal sounds only.
- **Placement** (`WildlifePlanner`, deterministic on 128 m cells, streams 901–912): herds of cows, sheep or horses in
  60 % of fallow fields; a farmyard (hens, rooster, chicks, goose or pig, dog, cat), doves and an inn paddock on a ring
  just outside each settlement; wild groups by region (forest: deer, boar, squirrels, fox, a wolf at night; meadow:
  rabbits, deer; downs: sheep, goats; lake shores: geese, beaver; passes: goats, an eagle). Never in water, on steep
  ground, astride a stream, in the road corridor or within a settlement.
- **Runtime** (`WildlifeManager`, order −32): groups activate within 220 m on built ground and release beyond 260 m;
  after the first fill nothing appears closer than 150 m. Caps: 10 groups, 60 animals. Animators pause beyond 120 m.
  Animals have no colliders and step aside from the traveller. Off from safety level 3.
- **Behaviour**: graze, wander, rest (farm animals sleep at night), alert, flee and return. Distances shrink to 35 %
  when the traveller has sat for 20 s and grow when they run.
- **Events** (`EventDirector`; one at a time, 30–150 m away, 2–4 minute cooldown): fox at the hen house, dog and
  rabbits, wolf and deer (dusk to dawn), sheepdog herding across the road, horses galloping, birds flushing, stags
  squaring up. Predators are held off by a minimum gap; nothing is caught. `F6` starts the best event nearby.
- **Cinematic**: events and herds publish `PointsOfInterest`; the director may frame them (events outrank landmarks).
- **Sounds**: `AnimalVoices`, 8 pooled 3D sources. Journey menu → Sky: Animals shown/hidden, Animal sounds volume
  (settings version 3).
- **Palette**: `MedievalZen/Wildlife/Build Wildlife Palette` (also automatic on editor load) creates stripped prefab
  variants in `Prefabs/Animals/`, measures each animal's height and checks every bool against its controller.
- **Finite world**: farmyards and wildlife only (no pastures, no trunk avoidance).

## Scroll map (2026-10-02)
`M` raises a parchment strip held between two rollers, direction of travel up. Code: `Scripts/Endless/Runtime/EndlessMap*.cs`,
driven by the existing `HeldMap`; the finite map's drawing is unchanged (pinned by a hash test).
- **Layout** (`StripProjection`): up the sheet follows a smoothed trend line of the road (meanders averaged over about
  500 m), across is the offset from it. The strip is 768 m wide at 1.5 m per pixel; the sheet shows 1,152 m at a time.
  Distortion at the edge is about 5 % typically, 23 % at worst; the mapping is exactly invertible for picking.
- **Drawn**: hillshade and contours from `Height`; any water the generator reports (lakes, streams, fords); woods from
  the land cover; inns, hamlets, villages, passes, viewpoints, camps, fords, bridges, milestones with the distance;
  region names where each begins; you-are-here, destination ring, compass, scale bar. Unknown waypoint kinds get a
  generic mark.
- **Drawn as you go**: what the traveller has walked, plus 1.5 km beyond, is inked; farther on it fades into bare
  parchment with a pencilled road, inns and villages still faintly lettered. The reached stretch is saved in
  PlayerPrefs `MedievalZen.EndlessMapReach` (cleared by Forget Saved Journey).
- **Use**: the pointer at the top or bottom edge, the mouse wheel or the gamepad shoulders roll the scroll (1.2 km
  behind to 2.5 km ahead, always including the next inn). Hovering names a place and its distance; Interact on an inn
  or village sets it as the destination, and `T` then walks there, ahead or behind.
- **Cost**: segments of 384 m are painted on worker threads (about 0.65 s each under Mono) while walking, cached (28),
  and copied to the sheet texture in small slices, so opening the map does not hitch.
- **Known limits**: narrow streams look patchy; villages are a ring and a name; flat country looks blank; the sheet does
  not flip when walking back.

## Streams, rests, villages and pass walls (2026-10-02)
- **Streams** (`StreamPlan`, `StreamNetwork`, `TileStreams`; randomness streams 1001–1090): one candidate crossing per
  700 m of road, used with a chance by region (32–60 %), so a crossing about every 2.3–3.3 km; never within 300 m of
  the start, 400 m of another crossing, a settlement or a col. A stream leaves the road at right angles, then follows
  the fall line with a gentle meander for 90–990 m; its bed and surface never rise downstream; it ends in a lake, at
  another stream, at a crag or by fading out. Channel: flat bed, banks of at most 31° on the easier side (always
  escapable).
- **Fords** (small brooks): the road dips 0.3 m over ±9 m; 6 cm of water over a gravel-painted road; a line of seated
  stepping stones beside it. **Bridges** (larger streams): Fantasy Kingdom `SM_Env_Bridge_Stone_01` (20 m long, 4.4 m
  deck, 8 convex colliders), pivot 0.24 m below a level road so the deck ends meet it flush; the terrain carries the
  road up to a narrow channel under the arch. Worst case in the auto-travel simulation: 1.1 cm step, 25.6° on the deck.
- **Contract additions**: `WaypointKind.Ford`, `WaypointKind.Bridge`; `StreamRibbon` / `StreamRibbonVertex`;
  `TileBlueprint.Streams` and `.StreamMesh`. The tile builder uploads the ribbon mesh in its own slice with
  `WorldPalette.streamMaterial` (Meadow `Water_Stream.mat`; palette version 4) and registers it with
  `StreamWaterIndex`, which `EndlessSurface.WaterHeight` reads. `IEndlessGenerator.WaterHeight` returns the stream
  surface inside a channel.
- **Climbs with rests**: the approach to a col starts 1,300 m before the pass region; level shelves 40–100 m long every
  250–450 m carry the camps, viewpoints, a log seat or a cairn. Longest pitch steeper than 3 %: from 1.2–2.1 km down to
  0.6–1.0 km.
- **Villages** (`VillageComposer`, endless only): 7–10 houses in a hamlet, 12–16 in a village along both sides of the
  road, a green with a well, fire and bench, garden fences, orchards, stone walls, an arch at each end, windmills
  beyond; dirt only on the road and worn paths. Still one cabin model: the Fantasy Kingdom buildings are the next step.
- **Pass walls**: buttresses, scree aprons, bolder cliff bands within 200 m, seated boulders at the wall foot.
- **Cost**: near tile +2–7 %. Finite world unchanged (golden hashes).

### Drawn streams (2026-10-02)
User: "the small river that I crossed is a bit weird" (flat pale sheet, jagged stair-stepped edge, no banks, grass in the
water, stones in an arbitrary row).
- **Cause, measured**: the ribbon was a flat plane `HalfRibbon` (= water + 0.55 m) wide over ground carved on the 2 m
  height map. Linear interpolation between samples lifts the bank's samples into the channel, so the visible edge was
  wherever the coarse ground crossed the water plane: on 3 seeds 33–42 % of the ribbon had ground above the water (worst
  0.52 m), 100 % of cross-sections had a ground-made edge, half of them ending 0.3 m or more inside the designed water.
- **Now the edges are the mesh's** (`TileStreams.Mesh`, built on the worker): a centre line smoothed (Catmull-Rom) and
  resampled every metre (`StreamCourse.Line`), a section per metre with a five-column water strip and on each side 13 bank
  columns: under the water's edge, a wet lip of mud, a concave bank (`StreamNetwork.BankRise`, 0.18 → 0.8 rise per metre)
  up to the ground (`BankTop`, scanned per course against the channelled ground), a drape over the ground, a skirt
  under it. Width swells and narrows (pools, riffles; streams 1151–1154). Banks fade under the ground at the source, the
  end and the road's part of the stream (a ford's road bed + 0.6 m, a bridge's span + 0.5 m), where the road makes the edge.
- **The ground is carved just below the mesh** over its whole footprint (`StreamNetwork.Carve`): each sample is lowered
  5 cm below the drawn stream at the nearest point it can shape (the 2 m grid reaches 2–2.83 m across, `StreamSample.Reach`)
  and 5 cm below the channelled ground the drape follows; outside the road's part this is cut again after the road bench.
  The drape follows the upper envelope of that ground. Result on seeds 12345/42/777 (20 km, outside the road's part):
  ground above water 0.000/0.000/0.003 % (worst 6 cm under one bridge), ground > 2 cm above the bank 0.000/0.000/0.001 %,
  every skirt buried, ground-made water edges 0.01–0.07 % of sections, visible edge within −6…+4 cm of the design.
- **Materials** (palette v8, built by the palette menu in `Materials/Streams/`): `MedievalZen/StreamWater` (transparent,
  lit by `UniversalFragmentPBR`, flow-scrolled value-noise ripples, darker and more opaque toward the middle, foam at the
  edges, riffles, rocks and over a ford's road) and `MedievalZen/StreamBank` (the terrain's own lush grass, mud and dirt
  layers in the terrain's UVs, so the drape meets the ground texel for texel; receives shadows). The banks have a
  `MeshCollider` (the traveller walks on the drawn bank; under the drape the ground lies up to ~0.4 m lower beside a brook,
  more in deep cuts). Grass and flowers keep out of cells that could reach the water or the lip; the ground around a
  stream is painted lush grass to match the drape; rocks and reeds stand on the drawn bank (`TileStreams.Ground.Drawn`).
- **Fords**: the stepping stones lie across the water 0.8 m beyond the bank's fade (about 5 m from the road's centre), a
  worn path from their ends back to the road (dirt on the bank mesh, painted on the ground beyond); thin water over the road
  shows a little foam.
- **Cost**: near tiles with a stream about +5–15 % (noisy); mesh about 5,000 vertices / 9,000 triangles per such tile
  (most 13,000 vertices). The finite world is unchanged (golden hashes).
- **Unverified without Unity**: both shaders compile with DXC against the URP 17.3 includes in three keyword variants, but
  their look (colours, ripple strength, foam, transparency at dusk and night), the drape's seam against the terrain
  (lighting differs from Terrain Lit's per-pixel normals) and the collider's cost are untested. Known limits: in the road's
  part of a stream the ground still makes the water's edge (on the road bed it is the dip's straight line); where a later
  stream joins an earlier one its water ends a few metres short of the other's.

## One look at every distance (2026-10-02)
User feedback: distant LOD rough, patchy grass ugly under strong sun, tree LODs switching too close, card trees bad.
- **Cause**: grass clumps were far brighter than the ground texture (ΔE 17–38) and cast their own hard shadows; past
  110 m the clumps stop and past 420 m far tiles showed the splat alone, so one hillside had three looks.
- **`GroundLook`** is the single source of grass and ground colours. The palette menu (version 5) builds first-party
  tinted terrain layers (`LushGrass`, `DryGrass`, `FlowerMeadow`), sward detail materials (no shadow casting, leaf colour
  matched to the ground) and detail meshes with up-facing normals. Ground-versus-clump ΔE is now 3–7. Applies to both
  modes once the palette is rebuilt and the terrain regenerated or repainted.
- **Sward** (`DetailPlanner.EndlessRules`, endless only): medium or short grass as an even base, tall grass and flowers
  as accents; bare grass-zone cells 3.3 % → 0.1 %.
- **Far tiles**: a 65² per-tile colour texture (4 m texels) with layer colours from `GroundLook`, canopy tint and streams,
  plus faint detail noise beyond 430 m; far rock paint calibrated to the near paint. Near/far ground ΔE mean about 0.3.
- **Fields**: fallow fields are grazed pasture; distant paint-only fields are stubble, flowering strips or ploughed. Inns
  have a worn yard of 9 + 9 m instead of a wide dirt disc.
- **Trees**: see the tree renderer line above; distances are in `EndlessSettings` and tunable live
  (`treeDetailScale` 0.5–2). Forest costs about 4.4–5.4 M visible triangles (was 0.7 M), shadow casters cut to a third.

## Distant terrain, third version, and review captures (2026-10-02)
User: "the far terrain mesh still looks bad at the distance"; at dusk the grass had a white tint.
- **Distant tiles are faceted low-poly meshes**: `TileLod.Mid` (to `midDistance`, 1 km; 4 m grid) and `Far` (to 2.2 km; 8 m
  grid). Every quad has its own vertices; flat normals from screen-space derivatives; one flat colour per triangle
  (`nointerpolation`) chosen from ten ground zones (`GroundLook`) with ±7 % brightness jitter; the quad diagonal follows the
  slope. Edges lie on the 8 m grid so Mid and Far tiles meet exactly. Lit by URP's `UniversalFragmentPBR` like the near
  terrain. No per-tile textures any more. Caps 36,864 / 12,288 triangles per tile; about 1.1 M typical in total.
- **They carry what near tiles carry**: every tree as a solid impostor (lathe crowns, stacked cones, dead spikes) sized
  from the mesh LOD the near renderer draws last and coloured from measured leaf colours (birches are orange-yellow, not
  green); rocks of 1.4 m and more as faceted lumps; inn and village houses as small solids.
- **Rock on open ground** (endless only): between 30° and 40° rock now appears as outcrop patches instead of a 50/50
  wash with dry grass; solid rock from 40° stays. Finding: hills that look mostly rock really are steep (51 % of the
  measured forest hill face is 30° or more); softening that means changing the terrain recipe, not the paint.
- **Dusk frost**: `DayCycle` scales `RenderSettings.reflectionIntensity` with the ambient sky brightness (the baked daytime
  reflection never dimmed), and the sward materials are fully matte (palette version 6).
- **Review captures**: in Play mode in `EndlessRoad.unity`, `MedievalZen/Review/Capture Tour` walks the next 6 km (every
  600 m plus each inn, pass, ford, bridge and village) and saves three views per stop, optionally repeated with a low sun,
  to `ReviewShots/` (git-ignored) as PNG + JSON. `F9` saves one shot; `Esc` or `F9` stops a tour. Use these instead of
  judging visuals from offline renders.
- **Review captures, second version (2026-10-06)**. Menu `MedievalZen/Review/`:
  - `Capture Tour`: every 600 m of the next 6 km plus each inn, pass, ford, bridge, hamlet, village and landmark, from where
    the traveller stands or from the road distance saved in `Tour Start Distance...` (a small window, `EditorPrefs`).
  - `Capture Places`: no regular stops; each inn, hamlet, village and landmark of the next 20 km, plus at each settlement an
    `overview` shot (`ReviewTourPlan.SettlementOverview`: 1.2 radii from the centre, 45° back from the road on the lower side,
    25-40 m above the centre and at least 10 m above the ground under the eye, looking at the centre).
  - `Low Sun Shots` (toggle) and `Weather/` (checked submenu: Keep current, Clear, Hazy, Overcast, Rain; default Clear). The
    weather is forced through `WeatherSystem.Force` + `SkipCrossFade` (as the Journey menu does) and put back afterwards.
  - The F3 overlay is hidden for the whole tour and around every F9 shot (`EndlessWorld.ShowOverlay`), and restored, with
    place, time, weather, camera, HUD and cinematic camera, at the end, on Esc/F9 and when Play mode ends.
  - **Landmark shots.** The first framed-landmark tour aimed correctly (`Waypoint.Height` is the structure's floor, `ToScene`
    got the right values) but stood 30 m before the planning point, where the ground and the wood hid the structure (St
    Aldo's, seed 12345: a crest rises between the road at 4,345 m and the chapel 46 m higher, 272 m away). Now the waypoint
    carries `ViewS` (`LandmarkPlan.BestView`: the road point within ±1.8 km where the planner's sight lines - terrain, the
    woods' cover, the roadside lining - see its upper part and it looks largest) and `StructureHeight`. Before travelling
    there the tour refines the stop off the main thread (`ReviewTourPlan.BestLandmarkView` within ±900 m and 1.3 km of it,
    lines from 2.4 m above the road to the structure's middle and 90 % height, against the ground and the trees' crowns built
    by `ReviewCanopy` from the generator's distant tiles), frames it from the road centre aimed at mid-height with a field of
    view that makes it a fifth of the frame (5-45°), and writes `landmarkVisible: false` when nothing within reach sees it.
    Seed 12345, first 20 km: all nine landmarks visible from the chosen stop, 147-216 px tall at 1080p; from the old stops
    eight of nine were hidden by the trees and two also by the ground.
  - Sidecar fields added: `tourKind` (Tour, Places, Single), `weatherRequested`; at a place `placeName`, `placeKind`,
    `placeRadius`; at a landmark `landmarkName`, `landmarkKind`, `landmarkDistance`, `landmarkBearing` (from the camera),
    `landmarkHeight`, `landmarkVisible`, `landmarkPlannedViewS`, `landmarkFramed`, and on the framed shot `landmarkPixels1080`.
    File names: `tour_<stamp>[_places]_s<seed>_<capture>_s<road m>m_<stop>_<shot>[_low].png` (sorted by run, then capture).
- **Review tool, third version (2026-10-06, NEXT_SESSION finding 7)**. Pure parts in `Scripts/Review/ReviewBench.cs`
  (`ReviewWalkBench`, `ReviewTourSummary`, `ReviewSight`, `ReviewJson`), tested in `ReviewTourTests`.
  - **Walking benchmark** (`MedievalZen/Review/Walk Benchmark`; `Walk Benchmark Options/`: speed x1, x6 (default), x12;
    distance 1, 2 (default), 5 km; weather from `Weather/`). From where the traveller stands, the screen visible, it presses T
    in effect: `PlayerTravel.ToggleAutoTravel` towards the inn ahead (the view is turned along +s first, since the endless picker
    takes the side the view faces), then the next inn on arrival, until the distance is covered. The only change to movement
    is `AutoTraveller.DebugSpeedScale` (default 1, set only by the benchmark, back to 1 at the end, on Esc/F9 and when Play mode
    ends): it multiplies the auto-travel pace (1.4 m/s, so x6 is about 8.4 m/s) and the steering look-ahead by its square
    root. Nothing else caps the speed (`PlayerMotor.MoveFacing` takes any scale, accelerates at 5 m/s²); what can hold it is
    the streamer's outrun hold, recorded with its place and duration, never hidden. Light frozen, HUD and F3 overlay hidden,
    cinematic camera off, camera third person looking where the body walks; all put back. Every frame is recorded (frame =
    unscaled delta; CPU main, render thread, GPU and present wait from `EndlessPerf.Last`, which lag a few frames; this frame's
    apply time `EndlessWorld.LastApplyMs` and its longest slice `FrameWorstSliceMs` / `FrameWorstSlice`, named when over 3 ms).
    Output `ReviewShots/walk_<stamp>_s<seed>_<from>-<to>m_x<speed>.json`: settings, weather, hour, legs (inns), metres and
    seconds, frames, mean / p50 / p95 / p99 / max frame, frames over 16.7 / 25 / 33.3 / 50 ms and per km, a per-region table
    (metres, mean and p95 frame, mean GPU, hitches over 33.3 ms per km), holds, and the 25 worst frames with road distance,
    region, slice and the nearest inn, village, hamlet, landmark, bridge or ford within 300 m. Held (black) frames are kept in
    the `.csv` of every frame but left out of the statistics. Three summary lines go to the console.
  - **Landmark eye against near trees**: the crown bands from distant tiles missed trunks and low limbs near the eye (Crow
    Abbey, seed 12345, 21,187 m: a 12.8 m oak 66 m out, 1.6 m beside the line, filled the 5° frame). At the shot, the trees of
    the built tiles within 100 m (`EndlessWorld.ActiveBlueprint`, the placements the renderer draws; bushes, ferns and
    undergrowth left out) are tested along the first 80 m of both lines (`ReviewSight.Clear`: trunk and crown cylinders, widened
    towards the target by half the landmark's radius, so its middle half is clear, not only its centre line). Blocked: the
    eye tries ±3, ±6, ±10 m along the road and ±1.5 m across, then the same raised 1.5-6 m; none clear writes
    `landmarkVisible: false`. Sidecar: `landmarkEyeRaise`, `landmarkEyeShift`, `landmarkEyeLateral`, `landmarkNearTrees`.
    Offline, Crow Abbey now moves 1.5 m across the road (with the whole radius kept clear no eye clears there at all).
  - **Start and summary**: `Capture Tour From Road Start` runs the tour from 0 m without changing the saved start; every tour's
    first console line is `Review tour starts at <m> m` in bold, with why. At the end of every tour (also when stopped)
    `tour_<stamp>[_places]_summary.json`: per region kind mean and max of `frameMsStop`, GPU, present wait and tree triangles;
    every stop with distance, label, region, noon and low-sun frame time; landmarks with visibility and the eye's move.

## Light, colour and motion (2026-10-02)
User: "the game feels bland". Shared with the finite world (atmosphere code); fog, clouds, `GroundLook` colours and the
player/camera numbers are untouched.
- **"Rich light"** (Journey menu → Sky, under "Mist and haze"; settings version 4, on by default) switches at runtime between the
  look before this pass and the new one, for comparison. `LightLook` holds the choice; `LightLookMath` is the look as pure
  functions of sun elevation and weather. By day: sun +10 % against ambient about −10 % and cooler (sky ×0.86/0.90/0.98, faded
  in with the sun's own 14° ramp), so flat sunlit ground keeps its exposure (−2…+3 %, tested) while the shade deepens; golden
  hours: sun +15 % more (slopes facing the sun +8–12 %), warmer white balance (+6), bloom +0.18 with its threshold ×0.88
  (sun-lit haze glows). Grade: contrast +8 by day (+2 at
  night, less in mist), saturation +6 (+4 more at golden hours), split toning added to the runtime profile (shadows
  0.45/0.49/0.56, highlights 0.555/0.525/0.47, golden 0.615/0.53/0.42; grey = neutral). Night lights unchanged.
- **Dark oak canopies**: not missing ambient light. The Meadow oak leaf texture is dark (linear albedo ≈ 0.05 green; birches use a
  bright flat colour plus 5 % emission and "frosting"), and every Synty foliage material ships with `_Enable_Back_Face_Lighting`
  off on double-sided leaf cards; URP Shader Graph does not flip back-face normals, so about half of a crown's cards were shaded
  as facing away from the sun. With Rich light the instanced tree copies turn it on, and take the ambient probe directly
  (light probes off; none are baked). Old look: as before.
- **SSAO** (`PC_Renderer`, upgraded once on editor load while it still holds the old values): intensity 0.4 → 0.6, radius
  0.3 → 0.6 m, direct-light strength 0.25 → 0.2. Not part of the toggle (an asset). Far tiles and instanced trees already have
  depth-normals passes. Tonemapping was already Neutral and stays (ACES would shift the validated greens).
- **Wind**: the sward materials (palette v7) and the instanced tree copies read the weather's wind
  (`_Use_Global_Weather_Controller`). Only `_WindIntensity` varies (`FoliageWind`: 0.24 + 0.46 × weather wind, ×0.8–1.25 with
  the same gust envelope as the wind sound; clear ≈ 0.35 vs the old fixed 0.2). Gale (0.2) and direction stay fixed: the shader's
  sway speed is time × f(gale, direction), so changing them mid-game made foliage race. Vertex colours keep clump bases and
  trunks still; impostors and the Generic trees and pines (no wind in their shader) do not move.
- **Drifting things** (`AmbientFxSpawner`): leaves falling from birches within 6–36 m (Meadow leaf effect recoloured orange,
  ≤ 2), pollen glints drifting on the wind over sunlit open ground (≤ 2 × 24 specks, never in rain or overcast), butterflies
  that look for flower details (`FlowerSampler`); `SkyBirds`: a flock of 3–9 procedural silhouettes crossing 40–80 m up every
  35–110 s by day (8 wing poses, flap and glide, one draw per bird; `Materials/Atmosphere/BirdSilhouette.mat`, URP Unlit).
- **Cost**: grade and lights ≈ 0; wind 0 (the shader always evaluated it); SSAO radius ≈ +0.1 ms; birds ≤ 9 draws; particles
  ≤ 128 more; tree and flower lookups a few times a minute.

## Landscape: foothills, planting and the sward at a distance (2026-10-02)
User: a steep bare hill "displaced on the world" beside an inn; "bland, tree density too low"; the ground "gets worse too
close". Measured offline within 600 m of the road, seeds 12345, 42, 777, first 20 km (harness in the session scratchpad).
- **Cause of the dropped-on hill**: not the lowland recipes (meadow, farmland, forest, downs and lake country already had 0-4 %
  of ground at 30° or more and under 2 % rock) but the pass: its range started at full height and steepness on the region
  boundary, so a 110-160 m wall with cliff bands (the terraces) rose within 200 m of the inn that often stands there. The face
  is brown because the only rock layer (`RockWall_Texture_01`, mean sRGB about 148, 128, 107; also the texture of the Meadow
  rock props) mixes with dry grass and scree dirt; seen in shade or haze it reads grey. That is the material, not a bug.
- **Foothills** (`TerrainField.PassEnvelope`): over the first and last 1,000 m of a pass the range rises from 30 % of its height,
  rounded (no ridged crests), its valley 60 m wider each side, banks of 1 in 2.6, with no buttresses, crags, horns, scree aprons
  or cliff bands; the foothills are woodier and less highland. Entrance (±200 m of the boundary): ground at 30°+ from 30-44 % to
  1-6 %, highest ground above the road from 107-140 m to 46-72 m. The col keeps its walls (14-16 % rock over the whole pass, was
  23-24 %).
- **Lowland faces** (`TerrainShaper.OutcropSlopeFull`): on open hills rock comes as outcrops from 30° and only becomes solid at
  50° (was 40°). Largest bare face in the lowland regions 2,980-7,940 m² → 1,412-3,604 m²; lowland rock 0.1-1.5 % → 0-0.5 %.
- **Lake beds** are the designed bowl (`LakeBedSlack`): ground lower than the bed is filled up to it, so no 18 m underwater cliff.
- **Planting** (`Planting`, streams 1101-1111; `ScatterRules.Endless`; the finite table is unchanged): roadside trees in stretches
  set by slow noise along the road and the region (a line on one side, an avenue, a tunnel of large trees; never at places,
  crossings, shelves, milestones, roadside slots, lakes or viewpoints, never inside the 12 m corridor); hedgerow trees and bushes
  round the farmland fields; copses of 7-20 trees with a skirt of bushes; a lone large tree on crests; bushes and ferns along
  wood edges and thinly in open country; alder-like trees on stream banks and lake margins; meadow trees more grouped; woods over
  about a fifth of the meadows and hanging woods in the downs. Trees per hectare: meadow 9 → 23, farmland 1.5 → 8.5, downs 4-7 →
  8.5-14, lake country 15-17 → 20-22, forest unchanged (81-92), pass 2.5 → 3.5. Bushes and ferns per hectare: 0.6-2.7 → 15-19
  in open country, 7.5 → 11-17 in the forest. Road with a tree within 16 m: meadow 4-7 % → 17-36 %, farmland 4-6 % → 22-38 %;
  lined stretches median 250-400 m, open 420-630 m.
- **Bushes are instanced**: countryside bushes, shrubs and ferns (`PropRole.IsInstancedPlant`) go into `TileBlueprint.Trees` and are
  drawn by `EndlessTreeRenderer` (no trunk collider; their prefabs have none) instead of pooled GameObjects; settlement gardens
  keep theirs. GameObject props per near tile 62-95 → 62-79 on average. Registered instances around the traveller: mean
  2,400-5,000 → 6,100-8,400, max 15,500-18,500 → 18,300-20,500 (cap 40,000); in a 100° view plus the 90 m shadow ring: max
  6,100. Bush meshes are 64-5,866 triangles (Meadow bushes the heaviest); estimated +0.7-2 M visible triangles. Distant tiles:
  triangles +5-8 % on average, maxima unchanged, no tree thinned. Generation: near tile 60-64 → 66-73 ms, far 5.1-5.5 → 5.6-6.6 ms
  (.NET 8, one thread).
- **The ground beyond the grass**: the clumps are still drawn to 110 m but match the ground (ΔE 3-7, normals up, no shadows), so
  past about 30-40 m (a 0.45 m clump is 11 px at 30 m, 5 px at 60 m, 3 px at 110 m on a 846 px high view) they add no contrast;
  what remains is the paint, which had almost no variation at 50 m scale (48 m blocks ΔE p50 0.2-1.0). Now (endless only) the
  dry share drifts in swathes of about 150 m (`DriftAmplitude` s.d. 0.3, every region), the flower-meadow paint covers about
  twice the area and swells with the lusher swathes, and the flower details follow it (`DetailRule.followFlowerPaint` 0.5:
  6 % → 34-37 % of flowers stand on flower paint, 83-88 % of the old count), so a flowery stretch keeps its colour after the
  flowers stop being drawn and the distant meshes carry the same zones. 48 m blocks ΔE p50 1.1-1.9, p90 2.3-3.4; neighbouring
  4 m blocks p99 ΔE 1.6 or less in open grass (no blotches). The detail distance stays 110 m (shared; more distance would draw
  clumps nobody can see).
- Not verified without Unity: the look of the drifts and of the instanced bushes, frame time, and whether Unity thins details
  with distance before 110 m (assumed not).

## Landmarks and field boundaries (2026-10-02)
User: "the game feels bland". Two additions, endless only (the finite world is bit-identical: golden hashes pass). Measured offline
on seeds 12345, 42 and 777, first 20 km (.NET 8, harness in the session scratchpad `landmarks/`).
- **Landmarks** (`LandmarkPlan`, `LandmarkDresser`, streams 1201-1209): one or two slots per region (two from 2.3 km), a kind by
  region (forest: towers, a chapel or ruin; farmland: windmills and chapels; downs: stones, towers, windmills; lake country: chapels
  and towers; meadow: all; pass: a watchtower or stones on the foothills), sited within ±240 m of the slot along the road and
  150-650 m to the side: level enough (tower and ruin ≤ 1.5 m rise under the base, sunk into the uphill side; chapel and windmill
  stand on their 2.5 m foundations), dry, ≥ 120 m from the road, clear of streams (+30 m), settlements (+140 m inns, +280 m
  villages and their fields), camps, viewpoints and the farmland fields (+12 m); among the most prominent candidates the one seen from
  the most road wins (terrain line of sight from 1.7 m above the road to 65 % of its height, 150-1600 m away, through the woods'
  canopy, weighted down where roadside trees line that side), plus prominence and height above the road. Unseen sites are dropped.
  - **Kinds** (Fantasy Kingdom presets, measured: see `ASSET_CATALOG.md`): watchtower (20.7 m), chapel with bell tower and
    churchyard (9.2 / 11.7 m), windmill with turning sails (17 m), ring of 9-13 rune stones (3.6-5.4 m, leaning a little), ruin with
    a lone stone arch (3.4 m). Dressing: rubble seated by `RockPlacer`, wood pile and goods, gravestones and a cross with dark yews,
    haystacks, sacks and a cart, bushes and a tree or two, a worn yard, a path stub toward the road (stopped before fields, streams,
    water). Dressed once in its own window (`DressingCache.Landmark`), so every tile agrees; its kept-clear circle is in the
    occupancy of near and distant tiles alike (trees agree).
  - **Measured**: 11 / 10 / 11 landmarks per 20 km (one per 1.8-2.0 km; between them median 1.8-1.9 km, longest gaps 5.3 / 5.7 /
    3.6 km over a pass and a forest); 145-741 m off the road. Road from which each is in terrain sight: median 2.2-2.5 km (of
    3.2 km possible); with the planted trees in the way (far-tile crowns between crown base and 85 % of height): median 700 m,
    range 150-1,850 m. When seen it stands 19-24 px tall (median; p10 7-12, p90 49-70) in a 1080 px, 60° view. Road with a
    landmark in terrain sight: 64-73 %. Planning: 25-34 ms per region (paid by the first tile that touches it, on a worker).
  - **Distant**: `TileLod.Mid`/`Far` draw the structure as a solid with the prefab's measured silhouette and atlas colours
    (`FarPropKind.Tower/Chapel/Mill/Stone/Ruin`: 14 / 26 / 22 / 10 per stone / 24 triangles).
  - **Hooks**: `WaypointKind.Landmark` (= 10, appended) with a generated name ("Wex Tower", "St Cuth's", "Brin Mill", "The Hart
    Stones", "Crow Abbey"); the scroll map draws a small tower sign and the name; the cinematic camera frames them
    (`LandmarkKind.Structure`, subject 7 m up); the review tour stops at them.
- **Field boundaries** (`FieldBoundaries`, streams 1210-1216): every field of the patchwork is enclosed along its super-cell's grid
  lines, so neighbours share one boundary and a grass headland (0-9 m) lies between crop and boundary. Each stretch between two
  cells is a dry-stone wall, a hedge or a fence by an "estate" noise (~700 m), the farmland's nearness to downs or a pass (walls)
  and its altitude, varied per stretch (by length over the three seeds: hedges 47-67 %, walls 13-39 %, fences 12-21 %; mostly walls next
  to the downs). Gates where `TileGenerator.Gateway` leaves the field: a 3.6 m gap between pillars in a wall, an open wooden gate swung
  75-105° back between posts in a fence and in half the hedges. Old walls lose 3.5 % of pieces and have tumbled stretches (6 %:
  the small wall piece, sunk and askew) and a broken run of 2-4 pieces on 12 % of stretches; fences lose 4 % and lean (3.5 %);
  hedges vary 0.88-1.12 in size and lose 5 %. Pieces follow the ground (pitched along the run, sunk 5-15 cm) and are dropped on the
  road corridor, water, stream banks, settlements, steep ground, inside a field, or (walls and fences) within 1.1 m of a planted
  trunk. The fields' own GameObject fences (`ScatterPlanner.FillField`) are left out; stubble fields within 320 m of the road get
  2-5 haystacks.
  - **Drawn instanced** (`TileBlueprint.Pieces`, `EndlessTreeRenderer`): the palette's wall, pillar, fence, post, gate-leaf and the
    Fantasy Kingdom hedge pieces become prototypes laid along +Z and centred (yaw offset and bounds centre baked into the parts),
    posed by `TreeCellSet.PiecePose` (yaw · pitch · roll), with their LODs (Meadow walls 2,674 / 1,662 / 852 triangles), shadows
    within 90 m, no sight blocking for the cinematic camera, and pooled box colliders (≤ 160) within 29 m of the traveller (open
    gate leaves have none). Village walls and fences stay GameObjects.
  - **Measured**: near tiles with boundaries carry 244-273 pieces on average (p90 435-506, max 623; four in five near tiles along the road
    have none); around
    the traveller 2,700-3,100 registered on average (max 6,000), in a 100° view 650-770 (max 2,045, 0.23-0.35 M triangles, max
    1.4 M), shadow casters within 90 m ≤ 142, colliders within 29 m ≤ 21. GameObject props per near tile fell (fence pieces were
    GameObjects): mean 72.7 / 88.5 / 71.4 → 69.4 / 84.5 / 67.9, max 1,431 / 2,641 / 1,549 → 1,146 / 2,323 / 1,319.
  - **Distant**: `TileLod.Mid` tiles carry `TileBlueprint.FieldEdges` (8 m stretches without the gate openings) drawn as ridges
    (hedge 2.0 m wide x 1.9 m, wall 1.1 x 1.25, fence 0.7 x 1.1; 4 triangles each) in the hedge, wall and wood colours; farmland
    mid tiles 465-610 ridge triangles on average, max 1,144 (cap 36,864: no tree thinned). `Far` (8 m grid) tiles do not draw
    them (under a pixel).
- **Cost** (.NET 8, one thread, 348 tiles per seed): near tile mean 59.9 / 57.3 / 59.1 → 60.5 / 57.6 / 60.2 ms (boundaries 0.14
  ms, landmark dressings ~0.5 ms on average), max 208-231 → 252-293 ms (the tile that plans a region's landmarks); mid 9.3-10.0 →
  9.9-10.6 ms, far 7.8-8.0 → 7.7-8.3 ms. Distant meshes stay under their caps (mid max 23,958 / 36,864; far max 7,646 / 12,288).
- **Palette v9** (`WorldPaletteMenu`): the new families (`LandmarkTower/Chapel/Mill/Ruin`, `Gravestone`, `Haystack`, `WoodPile`,
  `Hedge`) are centred, the hedge is laid along +Z like the fences, and the menu logs how each landmark and boundary family resolved.
- **Not verified without Unity**: the look of everything above (the structures' scale against the trees, the hedge colour, the
  ridges at a distance, the sails' turning on the Fantasy Kingdom windmill whose blades child is found by name), the instanced
  pieces' LOD switching and their box colliders' feel, the main-thread cost of instancing a 33k-triangle preset with its mesh
  collider in one slice, and whether walls, fences and hedges pop at the near tile edge (they are drawn to the near/far boundary,
  where the mid tiles' ridges take over). Animals in pastures have no colliders and can walk through boundaries. Known limits:
  forests and passes get few landmarks (woods hide them); the distant crop colours stay those of the eight terrain layers (a
  yellow rapeseed field needs a ninth layer or would change colour at the near/far boundary).

## The woods: the road through the forest (2026-10-05)
User, pointing at Wilderless' mystical forest: "this is the type of density and mood that I was aiming for … the current iteration
that we have is quite barebones." The review tour on seed 12345 (shots 0007-0048, Forest 1,891-4,781 m) showed parkland: scattered
trees on a lawn 12 m from the road, no understorey, open sky, Generic broadleaves reading as placeholder blobs, a near-black bush.
Endless only: the finite world is bit-identical (golden hashes pass; `ScatterRules.Default`, `DetailPlanner.DefaultRules` and
`ScatterPlanner` unchanged). Measured offline on seeds 12345, 42 and 777, the Forest regions of the first 20 km, a stop every 50 m
(.NET 8 harness in the session scratchpad `forest/`).
- **The road runs through the wood** (`Planting.Woodland.cs`, streams 1251-1262). The trunk corridor is per region
  (`Planting.TrunkCorridor`): Forest 5.5 m, lake country 6.5 m, meadow and downs woods 7 m, farmland and passes keep the scatter's
  10.5 m. Wherever the cover is wooded (forest weight ≥ 0.45 14 m out) slots every 4 m plant trees 5.5-11 m from the centre; no trunk
  collider (≤ 0.6 m × scale) within 4 m of the centre and no trunk base on the road (`Planting.MinTrunkDistance`, exact distance to the
  road curve). **Tunnels** (slow noise along the road, about 30 % of the forest road, stretches 56-360 m) plant large Meadow oaks
  (scale 1.25-1.6) 5.5-7.5 m out on both sides so their crowns meet. **Ancient oaks** (75 m cells, 32 %, deep wood only, scale
  1.45-1.8) keep a 6.5 m ring clear, which holds the scatter and the understorey back under them. **Glades** about every 380 m
  (16-28 m across, opening onto the road on one side, 4-9 m from its centre): no trees, lush and flower-meadow paint, tall grass and
  flower drifts (`GladeDetails`, `PaintGlades`, on near and distant tiles alike).
- **The floor** (near tiles): ferns (`PropRole.Fern`) and undergrowth bushes (`PropRole.Undergrowth`, the light Generic bushes) on a
  3 m grid where the cover is wooded, in drifts (45 m noise), thinner in glades, none within 4.6 m of the road centre; logs, roots and
  stumps along the road edge every ~23 m (GameObjects, logs keep their colliders, from 6 m). Endless detail table: tall grass under
  trees (forest affinity 0.12 → 0.3) and denser ground cover (1 → 1.4 per cell). The ferns are drawn to 160 m (fading over the last
  fifth); the undergrowth to the near/far boundary like the bushes, so the wood floor has relief from 30 to 150 m.
- **Restyling** (near and distant tiles alike, hashed from the position): within 150 m of the road the Generic broadleaves become the
  young birch (4 in 10), the small oak (3) or the tall birches (3) — never the 25k-triangle large oak; pines and dead trees stay. The
  Meadow `Bush_03` (the oak-leaf material, mean sRGB about 60, 65, 7: near black in its own shade, shot 0003) becomes a Generic bush.
- **Ivy**: Fantasy Kingdom ivy panels on ruins (always) and on towers and chapels in or near a wood (2-5 panels on the measured
  footprint's faces); Generic draped ivy over 42 % of the stone-wall pieces in a wood, 12 % at its edge. All drawn instanced as
  pieces without colliders.
- **Measured** (before → after; seeds 12345 / 42 / 777):
  - trunks within 12 m per 100 m of road: 0 / 0 / 0.1 → 11.0 / 9.8 / 8.8 (p90 20 / 18 / 16); nearest trunk to the centre: min 12.0 →
    5.5 m, median 12.8-13.0 → 5.8-6.3 m.
  - canopy closure over the road (crown at 85 % of its measured radius, above 3 m): 0 → mean 25 / 18 / 19 %; 50 m stretches closed
    at least half: 0 → 25 / 14 / 16 %.
  - floor cover 4-30 m (a grass detail or a 3D plant): 90 → 96-97 %; 30-150 m (plants, trunks, logs and rocks of 0.5 m or more): 2 →
    19 % (each seed).
  - instances in a 100° view (near tiles, 420 m): 1,476 / 1,481 / 1,446 → 2,785 / 2,816 / 2,766 (trees about 1,300 both; plants
    177 / 167 / 199 → 1,478 / 1,484 / 1,505); visible triangles mean 4.57 / 4.66 / 4.47 → 6.40 / 6.55 / 6.27 M, max 5.26 / 5.18 / 4.87
    → 7.17 / 7.21 / 6.95 M. Of the +1.8 M, the restyled trees are about +1.2 M (it was +2.0 M before the restyle was weighted to the
    lighter meshes and limited to 150 m), the floor +0.6 M.
  - registered instances within 420 m: 5.5 k → 24 k (max 25.5 k); per-frame cap 40,000 submitted (visible + shadow).
  - meadow and farmland: trees and trunks unchanged; plants in view in meadows 273 → 440 (the woods inside meadow regions now have a
    floor), farmland +1 %.
  - generation (one thread, the tiles within 420 m of 12345's forest): near tile mean 61 → 70 ms (max 262 → 282), far 13.7 → 14.3 ms.
    GameObject props per near tile unchanged (≈ 85-103).
- **Shadow proxies** (`ShadowProxy`, `EndlessTreeRenderer`): beyond `treeShadowProxyDistance` (100 m) a tree casts from a 26-triangle
  solid of its measured crown (the far-tile style, shrunk to 85 % so it stays inside the leaves) on a 3-sided trunk, a wall or hedge from
  its 12-triangle box; inside 100 m the simplest mesh LOD as before. Bushes, ferns, undergrowth and ivy cast only within
  `plantShadowDistance` (40 m). Material: `Materials/Endless/ShadowProxy.mat` (URP Lit, opaque, instanced, double-sided; palette v10).
  Estimated shadow load in the forest (all instances within 300 m, any direction; the renderer's own count was 3.1 M in the tour, so
  this estimate is about 1.9 × high): 5.75 / 5.89 / 5.26 M triangles from 2,808 / 2,811 / 2,622 casters with the old rules (10.3 M with
  the new floor) → 0.87 / 0.87 / 0.81 M (max 1.06 M) from about 2,600 casters with the proxies; meadow 1.9 → 0.18 M.
- **Safety ladder**: a new first rung sheds `EndlessFeatures.forestExtras` (ferns, undergrowth, ivy, forest light, mist and haze) before
  any tree: 0 everything, 1 no forest extras, 2 also no distant trees, 3 no distant tiles, 4 no near trees or animals, 5 no grass,
  6 ground, road and player. Shed at tile build (`TreeCellSet.Build(…, forestExtras)`) and in `ForestMood.Allowed`.
- **Mood** (`ForestMood`, shared atmosphere code, active only while the endless world allows it; the finite world never sets it):
  - **Canopy** 0..1 from the tree density in a 3 × 3 sample (25 m apart) round the camera: 0 below 0.35 trees per 100 m² (meadows are
    0.23), 1 from 0.9; eased over 4 s.
  - **Haze**: × (1 + 0.9 × canopy) on the haze density, a little warmer by day; in the open the canopy is 0 and the validated look is
    exactly as before; the clouds' haze ignores the term.
  - **Rich light** forest term (`LightLookMath.AddForest`): by day under a full canopy +5 white balance, −0.15 EV, +3 saturation; off
    with Rich light off (the toggle returns the old look), at night and in the open.
  - **Sun shafts** (`AmbientFxSpawner`): up to 3 emitters 8-40 m away where the trees open a little, beams 1.6-2.8 m wide and 7 × as
    long slanting along `DayCycle.LightDirection` (stretched billboards whose velocity is the light's direction), strongest with the sun
    8-25° up, gone by 55°, none in rain, thick overcast or at night (`ForestMood.ShaftStrength`); with warm dust motes in each.
    Code-built on the first-party glow material: the Synty sun-beam and dust effects use the built-in particle shaders. The Synty sun
    beam is no longer spawned in the endless woods.
  - **Mist among the trunks** (`MistField`): patches 35-110 m away where the wood is dense, at dawn and dusk with the valley mist and in
    rain or heavy overcast even by day.
- **Settings** (`EndlessSettings`, new fields, defaults apply on load): `treeShadowProxyDistance` 100, `plantShadowDistance` 40,
  `fernDrawDistance` 160. Palette v10 (rebuilt automatically) adds the families and the proxy material and logs how each resolved
  ("Endless road woods families"). No scene changes.
- **Where to look on seed 12345** (Asherford Chase, 1,891-4,781 m): tunnels 2,395-2,591, 2,635-2,931 (the tour's 2,812 m stop),
  2,995-3,199 and 4,267-4,627 m; glades at 2,207 m (right; the tour's first forest stop), 2,492, 2,825 (right, 28 m), 3,267, 3,519,
  3,941 and 4,248 m; the ruin landmarks of seeds 12345 / 42 / 777 for ivy.
- **Not built**: forest pools (still water dug into the hollows) — the global height function, the near and far meshes, the water
  queries, the animals and the map would all have to learn a second kind of water; left for a pass of its own. Ivy on cabin walls (the
  door side of the stone cabin is not measured) and on cliff faces.
- **Not verified without Unity**: everything visual — the density, the restyled colours next to each other, whether the shafts read as
  light (their brightness, where a stretched billboard's pivot lies, sorting against the fog pass), the mist patches' placement, the
  ivy's fit on the faces (bounds-based; Fantasy Kingdom walls are not flat boxes), the draped ivy's orientation over the wall top, the
  proxies' shadow shape at 100-300 m and any self-shadowing on the crowns, the proxy material in builds; the frame time (the tour ran the
  forest at 16-23 ms: the proxies remove most of the shadow pass, the floor and restyle add about 1.8 M visible triangles — measure it in
  the next tour), and the CPU cost of the instanced renderer's loop over about 24 k registered instances.

## The mountain passes: rock, scree, heath and pines (2026-10-05)
User, on the review tour of seed 12345 (shots 0055-0068, The Wex Beacon Crags, 4,781-8,269 m): "the steep mountain looks quite weird
and displaced on the world". The pass was a canyon of smooth brown slopes with faint horizontal stripes, the same from foot to crest,
no rock structure, scree, ledges or trees on the flanks, and a lawn-green strip along the floor. Endless only: the finite world is
bit-identical (golden hashes pass; `ScatterRules.Default`, `DetailPlanner.DefaultRules` and the finite paint unchanged). Measured
offline on seeds 12345, 42 and 777 in the range part of the first pass (1 km in from either end), within 600 m of the road (.NET 8
harness in the session scratchpad `pass/`).
- **Why it looked like sand**: 78-80 % of the mountainside (8 m and more above the road) was one dry-grass paint (a single patch of
  0.85-1.2 km²) and the steep faces were the warm `RockWall` rock (sRGB 148, 128, 107; b* 14), so from the road the walls read as
  tan dunes. The stripes were the old strata term: 13 m bands at a fraction of their strength (RMS 0.6-0.8 m, at most 4.6 m), too
  weak to make ledges (0.44-0.55 per 100 m of face; two thirds of the faces had none), strong enough to band the shading.
- **Rock architecture** (`TerrainField.Pass`, streams 1263-1268): the strata term is gone. The trench walls are recut, in coordinates
  along the valley and out from the wall's foot, into **cliff bands** of 10-24 m (a ledge rising at most 1 in 5 over about half the
  band, then a riser of 55-75°), the band edges wandering ±8 m, each ledge present over stretches of 50-250 m and pinching out between
  them; **gullies** 12-30 m wide and 6-16 m deep (at most 45 % of the wall) cut down the faces in three of four 80 m cells, the ribs
  between them reading as buttresses; **talus fans** below four in five gullies (cones at 32° whose apex lies 10-24 m up the gully
  at its floor's height, the toe never within 14 m of the road's centre); the crags at eye level are cut into the bands with
  the rest. The range's own steep flanks (over ~20°, slope of the ridged noise 3 m east and north) get the same cliff bands; gullies
  stay on the trench walls (a fall-line frame anchored at the world origin scrambles them on the flanks).
- **The road's shelf** (`TerrainField.ShelfAt`, streams 1270-1271): where the floor is at least 40-50 m wide on one side, the ground
  beyond 24 m from the road's centre drops 6-20 m (a 42° bank) to a lower floor that runs to the far wall; the side changes every
  kilometre or so. Never within 250 m of the col, 200 m of a stream crossing, beside a settlement's disc or in the outer foothills.
- **Higher peaks behind** (`TerrainField.BackRangeRise`): the range's crests rise up to 50 m more from 280 m to 1.1 km out, with
  taller horns there, so the peaks behind stand above the walls in layers the haze sets apart.
- **Paint** (`TerrainShaper`, endless only): the endless road's splat has 11 channels (`TerrainShaper.EndlessLayerCount`): the
  shared eight, then **mountain rock** (8), **scree** (9) and **heath** (10). In a pass range (`CoverSample.mountain`, from the
  range's strength) the rock is the grey mountain rock; scree lies on the talus fans and aprons (`CoverSample.talus`, from the height
  function), in the gully floors (`CoverSample.gully`) and in flecks under the cliff bands; the turf is heath on the slopes and
  ledges and in patches on the floor (45 % of the level turf), upland grass between. Grass details: none on mountain rock, a few on
  scree, thin tussocks on heath, and the range's turf 30 % thinner overall (`DetailPlanner.HeathThinning`, `MountainThinning`).
  Distant tiles see the fans and chutes blurred: their scree is lifted (`TerrainShaper.MountainScreeFarGain`).
- **Layers** (palette v11, built by the menu, never by hand): `MountainRock` (Meadow rock rough texture, 6 m tiles, neutral grey
  sRGB 132, 132, 129, as light as the warm rock so it lights the same), `Scree` (Meadow gravel, 4 m, pale 154, 149, 141), `Heath`
  (Meadow grass debris, 3 m, olive-brown 107, 100, 63); `WorldPalette.endlessTerrainLayers`. A near tile whose paint uses them
  (`TileBlueprint.MountainPaint`) takes the 11-layer set (URP's terrain shader draws 4 layers a pass: one more pass); every other
  tile keeps the shared 8, and a pooled TerrainData switches set only when a tile of the other kind reuses it. Missing mountain
  layers fold onto the shared rock and dry grass, with a warning.
- **Pines** (`Planting.Mountain.cs`, streams 1276-1278): on an absolute 7 m grid in loose clumps (90 m noise), up to 48 per hectare,
  by height above the road (full to 30 m, an eighth by 110 m, a fiftieth by 170 m) instead of the scatter's absolute tree line (the
  passes lie at it), more along the gullies, none on the fans, over about 40°, on the corridor (13 m from the centre) or its verges;
  near and distant tiles alike.
- **Rocks**: in a range (strength 0.35 and more) the scatter, the wall-foot boulders and the outcrops place the pass's own rocks:
  Fantasy Kingdom `Rock_Cliff_01..05` (`PropRole.PassCliff`, seated 5-9.5 m) and `Rock_Chunk_01..03` (`PassBoulder`, 0.8-3 m),
  one flat grey look with the mountain rock (`TileWindow.RockFamily`, `TileGenerator.PassRockFamily`); boulders are strewn over the
  thicker talus (absolute 9 m grid, streams 1272-1273). All seated by `RockPlacer` from `PropMetricsTable` (the Fantasy Kingdom
  entries are keyed with the pack: their names repeat the Meadow ones); distant solids in their grey (`FarPropStyles.PassRockColor`).
- **Measured** (before → after; seeds 12345 / 42 / 777):
  - walls (8 m and more above the road, 20 m and more from it): rock 20.8 / 19.2 / 21.5 → 22.2 / 21.9 / 23.8 %, scree 0 (dirt 1.1-1.3)
    → 8.1 / 8.5 / 9.4 %, heath and grass 78.1 / 79.5 / 77.2 → 69.6 / 69.5 / 66.7 % (heath 44-46, dry grass 22-24); walls of 25° and
    more: rock 45-48 → 57-59 %, scree 0 → 4.5-4.8 %.
  - floor: dry 80-83 % and lush 6-7 % → dry 35-39, heath 37-39, scree 12-13, rock 6-7, lush 4-5 %; grass and flower details per 4 m²
    2.24-2.29 → 1.10-1.14.
  - largest uniform patch of one paint (dominant 0.6 or more, 4-connected): 1.21 / 0.85 / 0.93 km² of dry grass → 0.18 / 0.12 / 0.13 km²
    (mountain rock or heath; dry grass 0.01-0.02); the risers link up along the walls, so the largest rock patch grew from
    0.09 to 0.10-0.18 km². Largest disc of one paint: 209 / 236 / 250 m → 80 / 60 / 70 m (rock 58 / 42 / 54, was 77 / 65 / 63).
  - ledges (4 m and more under 22°) per 100 m of face (slope over ±14 m of 28° or more, gaps of 16 m bridged), every 25 m both sides:
    0.55 / 0.44 / 0.53 → 1.56 / 1.95 / 2.00; risers (4 m and more over 50°) 1.03 / 1.22 / 0.92 → 2.02 / 2.41 / 2.15; face sections
    without a ledge 64 / 67 / 66 → 26 / 18 / 18 %.
  - stripe term on the walls (25° and more, within 250 m): RMS 0.57 / 0.78 / 0.67 m → none; the cliff bands that replace it move the
    ground RMS 2.2-2.4 m (at most 9-11 m).
  - pines per hectare on the lower flanks (3-100 m above the road): 2.0 / 1.9 / 1.8 → 23.0 / 21.8 / 21.7; on 12345 by height 3-35 / 35-70 /
    70-100 / 100+ m: 4.1 / 1.6 / 0.1 / 0 → 31 / 24 / 11 / 1.2. Nearest trunk to the road's centre 12.1-12.7 m (unchanged).
  - rock in sun and shade (Lambert with the day cycle's sun and ambient, tour hours): noon ΔE 24.6 / 23.4 / 24.8 → 22.9 / 22.3 / 22.9,
    low sun 22.3 / 23.2 / 22.0 → 21.2 / 22.0 / 21.5 (same lightness; the warm rock's chroma no longer adds to it); lit rock b* 14.5 →
    −0.7; rock in its own shade at noon 1-3 → 5-6 %.
  - road on a shelf (the ground 45 m out at least 6 m below the ground 12 m out, 600 m and more inside the pass): 0 → 16 / 24 / 23 %,
    drops up to 15 / 12 / 16 m. Peaks behind: highest ground 600-1,500 m out minus within 300 m, p50 19 / 12 / 8 → 48 / 30 / 46 m.
  - near / mid / far paint over the range tiles: rock 22.5 / 21.1 / 19.7 % (12345), scree 10.0 / 10.2 / 7.7 %.
  - foothills: at the pass entrance (±200 m) ground at 30°+ and the highest ground unchanged (0.9 / 5.1 % and 46 / 72 m on 12345); the
    col keeps its walls (22-34 % of the ground within 250 m at 30°+ in the bins round it, was 23-36 %).
- **Cost**: generation (one thread, seed 12345, 5,800-7,200 m): near tile 89.8 → 114.1 ms mean (heights +15 ms, paint +8 ms), mid
  27 → 35 ms, far 18.5 → 22.7 ms; lowland near tiles 70.6 → 73.0 ms (the 11-channel paint). In view (420 m, 100°, or within 90 m)
  from the range's road: pines 21-41 → 279-342 (0.13-0.16 M triangles), rock props 111-114 → 111-126 with 0.11-0.12 → 0.03-0.04 M
  triangles (the Fantasy Kingdom rocks are lighter); instanced plants and trees per range tile 40-42 → 141-147, GameObjects 36 → 34-35.
  Range tiles draw one more terrain pass.
- **Where to look on seed 12345** (The Wex Beacon Crags; the col and The Drover's Rest at 6,374 m): the tour's 4,800 m stop
  (5,812 m) stands on a shelf dropping to the right with a fan 40 m to the right and the cliff-banded wall on the left; fans and
  gullies 50-60 m left of the road at 5,980-6,180 m; no shelf for 250 m round the col (the inn's flat); fans 26-38 m left and gullies
  70 m left at 6,580-6,680 m with the drop on the right; the tour's 6,000 m stop (7,012 m) on a shelf dropping to the left. Shelves
  start at 5,801, 5,921, 6,081, 6,761 and 6,891 m.
- **Not verified without Unity**: everything visual — whether the cliff bands read as rock and ledges at 50-500 m on the real terrain
  shader (top-down projection stretches the rock texture on risers), the grey against the Synty sky and haze, the heath and scree
  colours against the sward details, the Fantasy Kingdom rocks next to the terrain rock, the pines' clumps; the frame time (one more
  terrain pass on range tiles, about 300 pines in view), and the palette upgrade (v11) creating the three layers in the editor
  (read its log). Known limits: rock still links up along the walls into patches of 0.1-0.18 km²; the range's flanks have no gullies;
  the shelf covers a sixth to a quarter of the inner pass road (the col, crossings and narrow floors exclude it); the Meadow pebble
  piles (`RockSmallPile`) and the odd sunflower clump remain in the ranges.

## The mountain passes: moderate slopes, rock skin, no pollen (2026-10-06)
Review tour 2026-10-05 (findings 3, 4, 10): the pass slopes below the rock threshold read as smooth tan dunes with faint contour
stripes (0044, 0050), the steep rock smeared into vertical streaks with hard ledge lines (0057, 0062), and the pollen glints read as
snow over the pass meadow (0073). Endless only (golden hashes pass; shared tables unchanged). Measured offline (.NET harness,
`scratchpad/pass2`) on the first pass of seeds 12345 / 42 / 777, range slopes of 10-35 degrees 8 m+ above the road and 20 m+ out.
- **What the tan slopes were**: near terrain (the shots stand 150-450 m from them). In the shots: Lab (46-49, 0, 4-11), L* s.d. 7-11.
  In the paint: 60-62 % heath (sRGB 107, 100, 63), 22-23 % dry grass, 11-12 % scree, 4-5 % rock; mean albedo sRGB (119, 116, 78),
  Lab (48, -5, 21.5), ΔE 19 to "sand" (sRGB 178, 160, 120); one heath patch of up to 7.2 ha; L* s.d. between 2 m cells 5.2-5.4. The
  stripes: the flank cliff bands (`TerrainField.Ledges`) applied from a mass slope of 0.3 (17 degrees), whose weak terraces moved
  21-22 % of these slopes by more than 0.5 m (RMS 1.3-1.45 m). Near and far already agreed (lit ΔE 1.1-1.3).
- **Paint** (`TerrainShaper`, `GroundLook`): heath retinted dark olive-green-brown, sRGB (80, 82, 54) (palette v13); upland-grass
  drifts stronger (`HeathDriftStrength` 0.22 → 0.34); on moderate slopes (`ModerateSlope`) mountain-rock outcrops from a 45 m noise
  (`MountainOutcrop`, shared with the crags), a scree skirt round each, and dark peat patches (the mud layer, at most 45 %).
- **Objects** (`Planting.Mountain.cs`, `TileGenerator.PassOutcrops`): pines 1.5× on moderate slopes; low shrubs (`Bush`/`Shrub`, near
  tiles) in 70 m drifts, 32 per ha at most, thinning above 60 m over the road (streams 1341-1343); a `PassCliff` crag (2.5-5.5 m) with
  1-3 boulders at the outcrop cores on an absolute 20 m grid, near and far (streams 1344-1345); all via `RockPlacer`, off the corridor.
- **Stripes**: flank bands only from a mass slope of 0.55 (29 degrees), full by 0.75 (`FlankBandSlopeStart/Full`); the ledges' back and
  lips rounded over 2 m of height (`LedgeSoftening`, poly smooth min/max, monotonic).
- **Steep faces: rock skin** (option b; `RockSkinMeshData`, `RockSkin`, shader `MedievalZen/RockSkin`, material
  `Resources/RockSkin` built by the palette menu): per near tile with mountain paint, a copy of the terrain over faces of 44-54 degrees
  and more, lifted 12 cm, alpha = mountain-rock paint × steepness, blended over the terrain, sampling the mountain rock texture and
  normal map triplanar; no collider, no shadow casting, depth offset. Chosen over (a) a Terrain Lit replacement (instancing, holes,
  basemap, add passes: too large to ship blind) and (c) tiling tweaks (cannot remove a 2-4x stretch on 60-75 degree faces).
- **Measured** (before → after, 12345 / 42 / 777): heath 62.0 / 60.7 / 60.3 → 47.3 / 46.2 / 46.2 %, mountain rock 4.3 / 3.9 / 4.6 →
  11.8 / 11.7 / 12.2 %, scree 10.7 / 11.2 / 12.3 → 14.9 / 15.5 / 15.9 %, peat 0 → 5.3-5.5 %, dry grass 22 → 19-20 %; mean albedo Lab
  (48.1, -5.1, 21.5) → (46.5, -5.1, 14.9), ΔE to sand 19.3 / 19.1 / 19.0 → 22.4 / 22.1 / 22.2, heath alone ΔE about 25 → 33; L* s.d. 5.2-5.4 → 8.3-8.4;
  largest uniform patch 7.2 / 5.5 / 5.7 ha of heath → 1.0 / 2.1 / 1.3 ha; objects per ha 30.8 / 30.2 / 29.6 → 46.5 / 45.7 / 44.8
  (pines 22 → 27-29, shrubs 4 → 12-13, crags 0 → 0.3-0.4); flank band term RMS 1.31 / 1.38 / 1.45 → 0.71 / 0.74 / 0.83 m, cells moved
  over 0.5 m 21-22 → 6-7 %; near/far lit ΔE noon 1.2 / 1.3 / 1.3 → 0.3 / 0.4 / 0.4, low sun 1.2 / 1.1 / 1.2 → 0.3 / 0.3 / 0.3 (mid 0.5-0.9).
  Walls (12345): ledges per 100 m of face 1.56 → 1.34, risers 2.02 → 1.85.
- **Cost**: skin on 50 of 60 range tiles, 3.9 k triangles per skinned tile (at most 9.3 k vertices, ~0.26 MB), 1.1 ms to build on the
  worker; main thread one mesh upload in its own slice ("rock skin"); GPU one blended draw per range tile (overdraw only on steep
  rock). Range tiles: instanced plants per tile up (shrubs), pines in view 308 → ~390.
- **Pollen** (`AmbientFxSpawner`): no pollen glints while the traveller is in a `HighlandPass` region.
- **Unverified without Unity**: the shader (compiled with DXC against URP 17.3 only), z-fighting of the skin at 400-750 m, whether the
  skin's lighting matches Terrain Lit at its fading edge, the heath and peat colours under haze, the palette v13 upgrade (its log
  lists the layers and "Endless road rock skin: created ...").
- **Where to look on seed 12345**: 5,267 m (0044's hillside: outcrops, peat, shrubs instead of a tan sweep), 5,885 m (the wall beside
  the shelf: skin on the risers, rounded ledges), 6,344 and 6,485 m (cliffs by The Drover's Rest), 7,085 m, 7,685 m (no pollen).

## The mountain passes: a faceted rock skin (2026-10-06, second pass)

Two paint passes changed the numbers and not the reading (P0072-P0074: smooth light-tan slopes, peat stains, the old skin's 6 m
texture tiling as scales): a low-contrast texture on a smooth 2 m grid reads as sand. This pass gives the rock geometry instead.
- **Faceted skin** (`RockSkinMeshData`, rewritten; `RockSkinField`, new, in Generation; `RockSkin`; shader `MedievalZen/RockSkin`,
  rewritten): an irregular triangulation over the range's rock (a global lattice of 144 cells per 256 m tile, every point jittered
  ±0.36 cell, each quad split along its shorter diagonal), from about 27° (slope 0.50) to vertical plus the outcrops of the moderate
  slopes, displaced outward only along the ground's normal smoothed over ±4 m: tilted, bent strata 3 m high (each bed a ledge or an
  overhang), blocks of three levels on 2.2 m cells per bed, a little noise per point. Relief: at most 0.14 m (+ 6 cm lift, so 0.2 m)
  where the slope is walkable (≤ 45°); on steeper faces 1.7 m per lattice step along the face (the lattice is horizontal, so on a 60°
  face its rows are 3.6 m apart up the face), capped at 3.0 m, ramping in between 45° and 57°. The relief is zero at every point that
  touches ground without skin and half one ring in, so the skin meets the terrain by geometry, not alpha. Never below the terrain:
  points sit on the terrain's own triangle surface plus the offset, and faces the terrain bulges through are lifted (interior points
  only). Tile edges: lattice points on a tile's edge move only along it, and the rockiness comes from the padded window (range
  strength from the cover, slope from the height map, the paint's own outcrop noise), so neighbours build identical edge points.
  Clear of the road: rockiness is zero within half the road + 2.5 m (verge and margin) and ramps in over 5 m. Streams 1346-1352.
- **Colour**: no texture. One flat colour per face: GroundLook's mountain rock (the far rock zone's colour) × strata tone
  (`StrataTone`, ±12 %, by bed, the far tiles' rock faces take the same bands) × relief tone (recesses darker, standing blocks lighter)
  × ±12 % jitter; overhangs × 0.72; ledges on cliffs lighter and leaning to heath; faces over non-rock paint take the splat's own mix.
- **Shader**: opaque (Geometry+10), stored face normals (three vertices per face), UniversalFragmentPBR with smoothness 0, receives and
  **casts** shadows (ShadowCaster pass), DepthOnly and DepthNormalsOnly (SSAO) passes, a −1 depth offset for the 6 cm edge. Material
  rebuilt by the palette menu (`EnsureRockSkinMaterial`, `CurrentDetailVersion` 17).
- **Peat**: `MountainPeat` 0.45 → 0.18 with a softer ramp: a darkening of the heath, not stains.
- **Measured** (harness `pass3`, first pass of each seed, near tiles within 600 m of the road; 12345 / 42 / 777). Coverage of rock ground (mountain-rock share ≥ 0.5): old skin (≥ 44°) 48 / 48 / 49 % → 98 / 98 / 97 %. Facets: p10 / p50 /
  p90 1.05 / 2.1 / 4.1 m². Mean tilt of a face off the smoothed ground: 18 ° on unwalkable rock, 6 ° on walkable. Normal dispersion
  within 16 m: 16.6-17.4 → 20.5-21.2 °. Lit brightness s.d. within 16 m on rock (Lambert + ambient, no shadows): noon 0.029-0.030 →
  0.048-0.050 (×1.7), low sun 0.021-0.023 → 0.040-0.041 (×1.8-1.9); self-shadowing (not in this number) adds to it. Near skin mean
  colour on rock sRGB (132, 132, 128) vs the far rock zone (132, 132, 129): ΔE 0.3-0.4; the far rock faces take the same strata bands.
- **Cost**: skin on about 60 of 95 tiles per pass; per skinned tile mean 10-11 k triangles, at most 25 k (the cap; the coverage
  threshold rises above it), 3 vertices per triangle (at most 75 k vertices, 28 bytes each, about 2.1 MB); one upload in its own slice
  ("rock skin"); one opaque draw plus a shadow-caster draw per range tile. Worker: 22-34 ms mean, p90 47-63 ms, max 60-144 ms per
  skinned tile, measured with 12 tiles generating in parallel (the old skin: 1.1 ms); the rockiness pass was trimmed afterwards (cover
  read directly, outcrop noise only where it can count), not re-measured.
- **Not done**: cladding with `PassCliff` / `PassBoulder` along risers (the skin's relief was the larger win for the budget); pines
  and shrubs on ledges (the ledges take heath colour only).
- **Unverified without Unity**: the shader (DXC against URP 17.3 only; the punctual-light shadow variant not compiled), shadow acne
  or peter-panning on the skin at the URP asset's bias, the −1 depth offset at the skin's edge at 300-750 m, how the flat colours sit
  next to the terrain's textured rock at the skin's edge, the palette v17 upgrade (log: "Endless road rock skin: updated ...").
- **Where to look on seed 12345**: 5,267 m (outcrops on the moderate slope), 5,885 m (the shelf wall: relief must stay off the verge),
  6,344 and 6,485 m (cliffs by The Drover's Rest: blocks, ledges, self-shadow), 7,085 m, and Cloudrest at 23,594 m (P0072-P0074).

## Performance pass: the forest at low sun (2026-10-05)
The review tour of seed 12345 (`tour_20261005-1940_*`) gave pass 4-5 ms, meadow 8-14 ms, forest 15-19 ms at noon and 25-32 ms at low sun
(17:22, sun 9.7° up). Target: forest under 20 ms at low sun, under 15 ms at noon, nothing visibly different within 110 m. Endless only;
the finite world is bit-identical (golden hashes pass). Measured offline on the tour's own shots (camera poses from the sidecars, the
near tiles of each stop as the streamer registers them: nearest point within 420 m) in a .NET 8 harness and, for the renderer's pass,
under Unity's own Mono (MonoBleedingEdge 6.13, release build; the editor's Debug code optimisation is slower still) — session
scratchpad `perf/`.
- **The tour's frame times were inflated.** The sidecar's `frameMs` is a rolling mean of the last 60 frames. Each capture (screen grab,
  PNG encode, file write, ~150 ms) lands in it: every shot of a stop reads ~2.5 ms more than the one before (the pass: 3.9 → 6.3 → 9.0
  ms for the same view), and at forest frame rates the window spans a second, so the low-sun shots carry the three noon captures as
  well (the forest's "25-32 ms at low sun" holds about 7 ms of capture hitches; the low-sun cost proper is likely 18-22 ms — an
  estimate). The tour now measures each shot on its own (below); `frameMs` is kept as it was for comparison.
- **Instrumentation** (`EndlessPerf`, `EndlessWorld.Perf.cs`): every frame `FrameTimingManager` (CPU main thread, render thread, GPU; a
  few frames late) and the renderer's own pass and submission (`Stopwatch`). When the manager reports nothing the main thread comes from
  the profiler's "Main Thread" counter and the render thread and GPU are null (`frameTimingSource` says which). The tour waits 4 frames
  after the shot settles (the previous capture's hitch stays out), then averages at least 30 frames and 0.5 s
  (`ReviewTourPlan.MeasureFrames/MeasureSeconds/MeasureSkipFrames`; F9 does the same). New sidecar fields: `perfFrames`, `frameMsStop`,
  `frameMsStopMax`, `cpuMainMs`, `cpuRenderMs`, `gpuMs`, `frameTimingSource`, `treeWalkMs`, `treeSubmitMs`, `treeWalkReusedShare`,
  `sunElevation`, `terrains`, `nearTiles`, `farTiles`, `treeRegistered`, `treeInstances`, `treeDraws`, `treeLod0`/`treeMid`/`treeLow`
  ([instances, triangles]), `treeVisibleTriangles`, `shadowNear`/`shadowProxies` ([instances, triangles]), `shadowTriangles`,
  `plantsDrawn`, `fernsDrawn`, `plantTriangles`, `cells` ([all, in view, shadow only, shadow culled]). The same numbers are on the F3
  overlay (three new lines) and at the end of the "running OK for 20 s" log line ("Performance: over 300 frames: …").
- **The renderer's pass** (`TreeCellWalker`, `TreeCellWalk.cs`, pure; `EndlessTreeRenderer` submits its `TreeGather`):
  - whole cells: a cell wholly inside one LOD band beyond the hysteresis margins (`TreeDrawRules.UniformBand`: Detail, Mid or Low, for
    every past band — unit tested as a property) adds each prototype's list with one `Array.Copy`; single-mesh prototypes (Generic trees,
    bushes, ferns, ivy) never decide a band per instance; only cells straddling a band edge and ferns or undergrowth near their fade go
    instance by instance, with the same hysteresis. The gather keeps every list whole (growing arrays, no per-frame allocation once warm)
    with bounds per 1,023-instance slice and is submitted after the pass, nearest band first, shadows last (`RenderMeshInstanced` with a
    start index). Checked against a copy of the old pass on the seven forest stops: the same instances in the same bands, frame after
    frame over 240 frames of walking, turning and jumping across the LOD thresholds.
  - the view is a pure frustum (`TreeFrustum.FromView`; four side planes and the far plane) so it can be opened by an angle.
  - shadow-caster culling (`EndlessSettings.treeShadowCasterCulling`): a cell out of view casts only if its box swept along the light,
    from its top down to 25 m below the lowest registered cell (`TreeDrawRules.ShadowSweep`, `ReceiverDrop`), at most twice the shadow
    distance long, can reach the view frustum (`TreeFrustum.IntersectsSwept`). A shadow that lands in view needs its caster on that
    sweep, so nothing seen changes. Off below a 1.7° light (`MinSweepSine`).
  - reuse (`treeWalkReuse`, `TreeDrawRules.CanReuse`): the last pass is submitted again while nothing registered changed (tiles,
    origin shifts, settings, toggles), the camera moved under 1 m, its forward and up turned under 2°, its field of view and aspect
    are unchanged and the light turned under 0.5°; the pass is made with the frustum opened by 2° and the boxes padded by 1 m, so what it
    gathered still covers the view; bands lag by at most a metre (margins are 4 m and more).
  - Measured, ms per frame, mean of the seven forest stops (300 frames of walking at 1.4 m/s after 60 warm-up frames), Unity Mono /
    .NET 8: old pass 1.13 / 0.75 (noon) and 1.10 / 0.78 (low sun); new pass 0.71 / 0.23; with culling 0.59 / 0.18; final settings
    (culling, undergrowth 250 m) 0.54 / 0.16 (noon), 0.58 / 0.19 (low); with reuse walking straight 0.015 / 0.005 (98 % of frames
    reused), turning 10°/s 0.04-0.05 / 0.015 (92 %; a fresh pass still costs the 0.54-0.67 ms p95), turning 40°/s 0.16-0.18 / 0.06
    (75 %). The meadow stop: 0.96 → 0.46 fresh, 0.04 with reuse. The submission itself (native) is not measured offline; it now uploads
    4,100-4,800 instances instead of 10,100-11,600 in the forest views (8,900 → 3,100 in the meadow) in ~98 visible + ~35 shadow
    instanced batches instead of ~81 + 33 (times each mesh's parts).
- **Registered instances**: the near tiles reach 750 m and more (a tile is near while its nearest point is within 420 m): 30-63 k
  instances on the forest stops (the forest pass counted 24 k within 420 m), 23 k on the meadow stop.
- **Ferns were drawn to the edge of the near tiles.** Three of the five fern variants are the Generic ferns, which are also Shrub
  variants; the prototype the Shrub family made first was reused without the fern flag, so `fernDrawDistance` applied to the two Fantasy
  Kingdom ferns only. The ferns and the woodland undergrowth now get prototypes of their own (`BuildPrototypes`, keyed by prefab and
  fern / undergrowth / other), so the countryside's shrubs and bushes are drawn as before and the ferns stop at 160 m as documented.
  Within 110 m the same instances are drawn (checked per class and distance).
- **Undergrowth draw distance** (`EndlessSettings.undergrowthDrawDistance`, 250 m, each bush fading at its own threshold from 200 m, as
  the ferns do): the woods' Generic bushes were drawn to the near tiles' edge. The floor keeps its relief from 30 to 200 m.
- **GPU load, before → after** (mean of the seven forest stops; triangles from the measured LODs): road view 6,999 → 2,893 instances,
  9.11 → 7.60 M visible triangles (plants 1.95 → 0.44 M; LOD0 band 1.43 → 1.40 M, the difference being single-mesh plants filed in other
  buckets, not fewer plants near); wide view (40°) 6.39 → 5.30 M; side view 9.79 → 7.90 M. Shadows: 3,113 casters / 1.31 M triangles →
  1,248 / 0.84 M at noon and 1,815 / 0.91 M at low sun (the near ring: 694 / 1.25 M → 502 / 0.82 M and 517 / 0.88 M); 143-231 of the
  231-253 shadow-only cells culled. Meadow road view: plants 1.9 → 0.3 M triangles, instances 8,900 → 3,100. Each shadow batch's bounds
  span its ring, so URP draws it into all four cascades: the shadow saving counts up to four times. GPU time saved: not measurable
  offline; an estimate of 1-3 ms in the forest.
- **Levers measured and not applied** (they would change the look within 110 m or did not pass the pixel test):
  - shadow proxies closer (`treeShadowProxyDistance` 100 → 70 / 60 / 50 m: shadow triangles 0.84 → 0.60 / 0.52 / 0.43 M at noon,
    0.91 → 0.61 / 0.50 / 0.42 M at low sun). The proxy's silhouette along the light, against the simplest mesh LOD's (8 bearings, 5 cm
    raster): Meadow oak 4.5 m off (Hausdorff), Meadow 02 2.3 m, birches 1.4-1.8 m, fruit trees 1.0-1.5 m, Generic trees 0.7-1.2 m, pines
    0.5-0.8 m, dead trees 1.4-4.9 m; the proxy covers 55-80 % of the crown's shadow (it is shrunk to 85 % and has five sides). Over a
    metre for every broadleaf: the proxies stay beyond 100 m. (They already cast visibly smaller shadows than the trees beyond 100 m;
    a proxy fitted to the crown's hull would be needed to move them in.)
  - plant shadows 40 → 25 / 20 m: 0.04-0.06 M shadow triangles; plants at 20-40 m are within 110 m. Unchanged.
  - fern distance 160 → 100 m: 0.1 M triangles. Unchanged.
  - mid-band mesh: the mid LOD against the low LOD, seen level: Meadow oak 0.77 m (6.6 px at 110 m, 1080p), Meadow 02 0.56 m (4.8 px),
    birches 0.28-0.56 m (2.4-4.8 px). All over a pixel: unchanged. No tree has a mesh LOD below its low one but the card.
  - trees beyond 420 m on near tiles (in the road view 770 trees, 1.9 M triangles, 21 % of the visible triangles) are drawn with the
    low mesh out to the tile edge; handing them to the far impostors would need the near/far split per tree, not per tile. Not done.
- **Not changed, estimated**: the forest mist (`MistField`, Synty `FX_Fog_01`: 5 sheets a second living 15-30 s, 10-40 m wide; up to
  10 patches, about 1,100 soft sheets at full mist) is the largest transparent overdraw of the forest at dawn and dusk; the sun shafts
  (3 emitters × 8 beams, 40 motes) are small. Both are part of the validated atmosphere and stay; the safety ladder still sheds them
  first with the ferns, undergrowth and ivy. Terrain detail density under the canopy and `MaxInstancesPerFrame` (40,000; now 4-5 k
  submitted) are unchanged.
- **Settings** (`EndlessSettings`): `undergrowthDrawDistance` 250, `treeShadowCasterCulling` true, `treeWalkReuse` true,
  `settingsVersion` (hidden). One-time upgrade (`EndlessSettingsMigration`, applied by `EndlessRoadMenu.RegisterSettingsUpgrade`, which
  folds in the 2026-10-02 tree-shadow 90 → 300 m rule): version 0 → 1 sets them, keeps any hand-tuned value and saves the asset; it
  logs "EndlessSettings.asset: settings upgraded from version 0 to 1 …" once.
- **Read in the next tour**: in the sidecars, `frameMsStop` (not `frameMs`) for each shot, `cpuMainMs` / `cpuRenderMs` / `gpuMs` to see
  which side bounds the forest at low sun, `frameTimingSource` (if it says "profiler (main thread only)" the GPU time is unavailable in
  the editor), `treeWalkMs` + `treeSubmitMs` (pass under ~0.6 ms, mostly reused), `shadowNear` / `shadowProxies` at noon and low sun
  (about 500 / 750-1,300 casters), `plantsDrawn` (about 1,000-1,500 in a forest road view) and `fernsDrawn` (about 400-600);
  in Editor.log the "Performance:" tail of the "running OK" line.
- **Not verified without Unity**: every frame time; the GPU effect of the fewer instances and shadow casters; the submission's cost per
  batch (17 more instanced batches, 60 % fewer instances); whether `FrameTimingManager` reports GPU times in the editor on this machine;
  the fade of the undergrowth at 200-250 m and the ferns at 128-160 m in the real view; that no shadow pops when turning quickly (the
  sweep is conservative, the reuse covers 2° and 1 m).

## Second performance pass: far tree band, main-thread markers, loading slices (2026-10-06)
From tour `tour_20261005-2110_*` (forest 12.5-17.4 ms noon, CPU main 11-16 ms, GPU ≈ frame; worst slices "show props" 17 ms,
"alphamap band" 10 ms). Offline harness: session scratchpad `perf2/` (modes `imp`, `bands`). Endless only; golden hashes pass.
- **Impostor band** (`TreeDrawBand.Impostor`, `TreeImpostor` in `TreeCells.cs`): beyond `treeImpostorDistance` (420 m × detail scale) a
  tree is drawn as a solid impostor, the far tiles' detailed shape (broadleaf 23, pine 18, dead 5 triangles; never a card), crown radii ×
  `CrownFit` 1.2 (the five-sided rings are inscribed; 1.2 brings the impostor's silhouette area to 0.87-1.09 of the mesh's), colour the
  middle of the style's two foliage colours, instanced with a copy of the far terrain material (`SetImpostorMaterial`, called by the tile
  builder). Hysteresis 5 % (21 m); `TreeLodDistances` keeps the impostor edge at least a metre past the mid edge's margin; whole cells
  stay whole (`UniformBand`), per-prototype distances get their own uniform state (`TreeCell.UniformOf`). Plants and boundary pieces draw
  their lowest mesh in that band. 110 / 300 m bands, shadows and proxies unchanged.
- **Silhouette rule**: impostor vs lowest mesh LOD, seen level from 8 bearings, 2 cm raster: mean Hausdorff 0.37-0.49 m (pines),
  0.52-0.80 (Generic trees), 0.62-1.23 (birches), 0.68-1.03 (fruit), 1.63 / 2.78 (Meadow 02 / 01), 0.99-2.43 (dead). One 1080p pixel
  (60°) is 0.449 m at 420 m: nothing matches within 1 px inside the near tiles. Each tree swaps at max(420 m, the distance where its mean
  outline difference is `treeImpostorMaxPixels` (3) pixels) (`TreeImpostor.DistanceFor`): Meadow 01 then stays a mesh to the tile edge
  (867 m), Meadow 02 swaps at 508 m, the rest at 420 m.
- **Triangles, road views of the forest stops** (old tour scene, before → 3 px rule → all at 420 m): 1,200 m 8.82 → 7.55 → 6.40 M;
  1,800 m 7.95 → 7.17 → 6.35; Tired Oak 6.76 → 6.23 → 5.77; 2,400 m 11.05 → 8.42 → 6.46; 3,000 m 8.57 → 7.24 → 6.18; St Aldo's 7.44 →
  6.99 → 6.50. Low band: 1.1-2.7 k trees / 2.3-6.3 M → 0.7-1.0 k / 1.8-3.6 M; impostors 0.4-1.7 k trees / ≤ 0.12 M. GPU effect: estimate
  only (1-2 ms at 3 px, 2-4 ms with `treeImpostorMaxPixels` 0).
- **Main-thread markers** (`EndlessPerf.MarkerSlots`, `MarkerWindow`): update, lateUpdate, fixed, animators, particles, ui, render, cull,
  shadowCull, terrain, details, gfxWait; each slot takes the first candidate name the engine lists (`ProfilerRecorderHandle.GetAvailable`);
  the log line "Endless perf markers: … slot=NAME|NONE …" after "running OK" says which were found and lists the relevant names available.
- **Loading** (`ApplySlicing` in `TileStreaming.cs`): alphamap bands 64 rows to 4 layers, 32 to 8, 16 for 9-12 (an 11-layer band was
  10 ms; ~2.5-3 ms expected if the upload scales with rows: estimate); prop groups close at 12 k triangles, so a 20-35 k building is
  switched on alone ("show props"; its activation cost itself is not reduced). Not done: pool pre-warm, `InstantiateAsync`.
- **Settings** (`EndlessSettingsMigration` version 2): `treeImpostorDistance` 420, `treeImpostorMaxPixels` 3 (only 0/invalid replaced).
- **Read in the next tour**: sidecar `treeImpostor`, `treeLow`, `treeVisibleTriangles`, `treeImpostorDistance`, `mainMs_*` (NaN: the marker
  reported nothing), `worstApplyMs10s`, `worstSliceMs10s`, `worstSlice10s`; Editor.log "Endless perf markers:" and "Endless road tree
  impostors". Unverified: every frame time, the impostor's colour against the mesh under real light, the marker names, slice times.
- **Addition 2026-10-06: the building hitch** (`PropWarmup` in `TileStreaming.cs`, the builder's warm-up section, `EndlessWorld.Journey.cs`).
  The heavy prefabs are simple: every Fantasy Kingdom preset is 1-4 GameObjects with 1-4 MeshRenderers, no LODGroup and one
  non-convex MeshCollider on a separate small collision mesh (tavern 664 triangles); the bridge and the ruins carry 8 convex colliders.
  So the "show props" cost is a first use (mesh, shader and shadow variants, collider cooking), not child count.
  - Warm-up: at every hold that goes black (start, Go now, New journey, outrun), each heavy prefab (≥ 5 k triangles, families at
    places: buildings, landmarks, bridge, mills, stalls, never trees, plants, boundary pieces, rocks or cliffs) that was never used
    is instantiated active on a stage 45 m in front of the camera for one frame (drawn and cast once behind the black screen,
    collider cooked), then parked in the prop pool; at most 3 per frame within 8 ms. A hold waits for it at most 1 s after the
    ground is ready; the end of a hold parks whatever is still staged. Log: "Endless road prop warm-up: N heavy prefabs …" and
    "… done / cut short: X ms over F frames (worst Y ms); heavy prefabs warmed a/b, made ahead c, idle d".
  - Stock: while walking, one idle instance per heavy prefab is kept, refilled one instantiate per frame (inactive, under the
    pool) when no tile is building or releasing and no settlement or landmark lies within 300 m of road ("stock heavy prop" slice).
  - Activation: a heavy prop's colliders are switched on a slice after its renderers ("heavy prop colliders"), and its group's
    slice is named "show <prefab>" or "show <prefab> (first use)" (never warmed). Not done: colliders gated by distance (the
    camera rig, the motor, the cinematic queries and wildlife's scene adapters raycast against them), child renderers in batches
    (at most 4 per preset: nothing to split).
  - Overlay pools line and sidecar `heavyProps` [warmed, heavy prefabs, made ahead, idle]. Expected (estimate): the worst walking
    slice at a place under about 5 ms; if "show <prefab>" (warmed) is still high the cost is per activation, not first use.

## Tree impostors that match (2026-10-06)
From tour `tour_20261006-0810_*` (finding 5: pale sage and brown "lollipops" beside dark oaks and orange birches; finding 1: the forest is
GPU-bound with a 4.28 M-triangle low band of Meadow oaks the 3 px rule never swapped). Endless only; golden hashes pass; tree positions unchanged.
- **Colour**: impostors used the leaf albedo measured from the atlases. On a flat-shaded solid lit by the same sun that is not what a crown
  looks like (leaves with holes, dark interior, self-shadow, the foliage shader). Calibrated on pairs in the shots (real crown against an
  impostor of the same species at a similar distance, fog removed with an estimated transmittance exp(-0.00045 d), sky/fog grey 190): the
  Generic/pine solids were about twice too bright, the Meadow impostor olive-yellow and too bright, the birch impostor a dull brown.
  `FarTreeShapes` now holds apparent colours (linear): Meadow 0.012/0.019/0.007-0.014/0.023/0.006, Generic 0.063/0.086/0.037, pine
  0.048/0.055/0.029, fruit 0.072/0.122/0.005-0.093/0.125/0.004, birch 0.82/0.30/0.02-0.78/0.43/0.02. ΔE76 at matched distance, measured
  before → predicted after (estimate, grey lighting factor): birch 19 → 10, Meadow 10 → 7, Generic 12 → 3.5. Near impostors and Mid/Far
  tiles read the same table, so the swap at the near tiles' edge keeps its colour; the Mid/Far tiles keep their per-tree tone (±7 %), the
  near impostors vary per lobe (±6 %), which a tree's own yaw turns into per-tree variation.
- **Shape** (`TreeLobes` in `FarTreeShapes.cs`): broadleaf crowns are faceted lobes placed from the mesh: the simplest LOD's crown vertices
  (0.5 m up, outside 1.5 trunk radii or above the crown base) clustered by k-means with a deterministic start, radii from percentiles of each
  cluster; a lobe is a five-sided ring between a top and a bottom point (10 triangles, `FlatLobes`) or with an upper ring (20); trunk to
  the fork plus two fork arms in bark colour (9). Lobe count, percentile and scale were chosen offline (session scratchpad `impostor/`,
  harness mode `lobes`) for the least mean outline difference (8 bearings, 2 cm, the mesh clipped at the ground: the Meadow oak's roots
  reach 2.1 m below the pivot and dominated the old number) and stored in a table in code. Mean outline difference near (old → new, m):
  Meadow 01 2.73 → 1.17 (109 triangles), Meadow 02 1.63 → 0.73 (99), birch 1.21/1.21/0.62 → 0.57/0.51/0.29 (59), fruit 1.03/0.97/0.65 →
  0.45/0.46/0.37, Generic 0.80/0.79/0.51 → 0.54/0.57/0.38. Pines and dead trees keep their shapes.
- **Swap**: every lobed tree now matches within 3 px at 420 m, so `treeImpostorDistance` is 320 m (settings migration version 3 moves the
  old default 420; a hand-tuned value survives); each prototype still swaps no nearer than its own 3 px distance (Meadow 01 365 m) and
  never inside the 300 m band (`TreeLodDistances` floor ≈ 333 m). 110 / 300 m bands, their meshes, shadows and proxies unchanged.
- **Mid/Far tiles**: Mid tiles draw the same lobes in a cheaper fit (two flat lobes and a trunk, 23 or 13 triangles; Meadow 01 three, 33),
  Far tiles the old 8-triangle solid, all in the same apparent colours; the dense-forest Mid tile still fits its 36,864 cap unthinned.
- **Triangles** (old forest scene stops, road at noon; 3 px rule with old impostors → lobes from 320 m): 1,200 m 7.55 → 5.48 M; 1,800 m
  7.17 → 5.39; Tired Oak 6.23 → 4.76; 2,400 m 8.42 → 5.54; 3,000 m 7.24 → 5.43; St Aldo's 6.99 → 5.72. Low band 150-180 trees / 0.4-0.55 M,
  impostors 0.1-0.27 M. Tour cameras (estimate from the sidecar bands): forest stop 9.95 → about 6.4 M, bridge 5.77 → about 2.8 M; GPU
  2-3.5 ms less in the forest (estimate only).
- **Read in the next tour**: sidecar `treeLow`, `treeImpostor`, `treeVisibleTriangles`, `treeImpostorDistance` (320), `gpuMs`; the
  Editor.log "Endless road tree impostors (triangles)" line; T0002/T0026-like telephoto shots for colour and shape at the swap.

## Rounded lobes and noon colours for the tree impostors (2026-10-06, afternoon)
From the first clear-noon shots of the impostors above (tour `20261006-0938` stop 0068 St Aldo's, 5° telephoto at about 770 m; tour
`20261006-0943_places` 0004 Hollestead, 0010 Wick windmill, 0124 Foxerley): distant trees were flat dark-grey diamonds stacked like shards,
orange diamonds for birches, no light and shade. Endless only; tree positions, the 110/300 m bands, 320 m, the 3 px rule and shadows unchanged.
- **Why flat and dark**: not the normals. `FarTerrain` builds each face's normal from screen derivatives and turns it toward the camera,
  so lighting never depends on winding (winding only decides culling; the lobes were and are wound outward, now tested). The cause was
  the albedo: the morning's oak colour (linear 0.012-0.02) lit by the noon sun gives sun-lit faces about sRGB (37, 50, 26) and shaded
  ones (18, 27, 12), i.e. near black, and at 400-800 m the haze added on top (≈ sRGB 117-125 in the Hollestead overview) is the whole
  colour: the measured impostor boxes vary by 1-3 sRGB levels between faces, where the real crowns beside them span 50 → 130. Birches at
  0.8 red clipped to a flat orange. The pointed tops were the lobe shape itself: a five-sided ring between a top and a bottom point.
- **Shape** (`TreeLobes`): a lobe is a low dome: a flat tucked underside ring (0.6 of the widest), the widest ring at the centre and a
  flat cap ring (0.6) at the top, each twisted half a side, vertices shared within the lobe and one colour per lobe (light and shade come
  only from the shader). Near impostor band: five sides, 26 triangles a lobe, trunk and fork arms 9; Mid tiles: four sides, 20 a lobe,
  trunk 3; Far tiles: the 8-triangle double pyramid is now a 4-sided low dome (flat underside at 0.9 R halfway up to the widest ring,
  flat cap 0.6 R at the top, 12 triangles, 8 vertices). Lobes refitted on the meshes for the new shape (session scratchpad `impostor2/`,
  harness modes `lobes`, `dense`, `plot`; side-view plots in `impostor2/plots/`).
- **Outline** (mean outline difference, m, near / Mid, morning → now): Meadow 01 1.17/2.25 → 1.25/1.88 (eight lobes: six gave 1.43,
  seven 1.38; 3 px at 420 m is 1.47, own swap 365 → 388 m), Meadow 02 0.73/1.07 → 0.80/1.23, birch 0.57/0.84, 0.51/0.80, 0.29/0.40 →
  0.57/0.80, 0.51/0.76, 0.26/0.34, fruit 0.45/0.70, 0.46/0.59, 0.37/0.39 → 0.51/0.64, 0.39/0.45, 0.36/0.41, Generic 0.54/0.59,
  0.57/0.65, 0.38/0.48 → 0.50/0.57, 0.50/0.58, 0.47/0.53. Every prototype stays under 3 px at its swap distance.
- **Triangles per tree** (near / Mid / Far, morning → now): Meadow 01 109/33/8 → 217/63/12, Meadow 02 99/23/8 → 139/43/12, birch 59/23/8
  → 113/43/12, fruit 69, 49, 69 / 23, 23, 13 → 113, 113, 61 / 43, 43, 23, Generic 69/13/8 → 35, 113, 87 / 23 / 12; pines and dead trees
  unchanged. The tests' densest tiles stay unthinned: Mid 21,654 → 32,474 of 36,864 triangles (vertices 55,743 → 33,132: shared within
  a lobe), Far 7,423 → 9,943 of 12,288. Near impostor band: about 1.7-2× its triangles (St Aldo's stop: 0.19 M → about 0.35 M; estimate).
- **Colour** (`FarTreeShapes` apparent colours, linear): calibrated on real crowns at 100-250 m in the 09:43 overviews (sun-lit and shaded
  20 % of each crown box; haze removed with an exp² haze fitted on two oaks at about 100 and 300 m, T(300 m) ≈ 0.93) against a solid lit
  by the noon sun (1.35 at 57.5°) and the trilight ambient (0.58, 0.66, 0.78; sides 0.62 of it): sun-lit faces ×(1.46, 1.52, 1.62),
  shaded ×(0.46, 0.51, 0.58). A solid holds 3:1 between its faces where a crown's leaves and holes hold 10:1, so the albedo minimises the
  worse of the two ΔE76: Meadow 0.078/0.084/0.029-0.088/0.092/0.033 (real lit sRGB 114, 119, 58 / shade 46, 50, 47; impostor 98, 102, 63
  / 55, 60, 37; ΔE 13 / 13), birch 0.215/0.105/0.030-0.200/0.125/0.034 (ΔE 12 / 12), fruit 0.160/0.195/0.040-0.175/0.200/0.043
  (ΔE 15 / 15). Generic and pine meshes are flat atlas colours themselves: atlas albedo × 0.9 (0.113/0.155/0.067, 0.086/0.099/0.052).
  No impostor's shade is darker than the real crown's shade. Near impostors, Mid and Far tiles read the same table.
- **Unverified**: the light factors are a model (no tonemapping, the Neutral tonemapper bends the upper tones), the haze distances of the
  overview boxes are estimates, and the backlit telephoto (heading 161°, sun south) shows the shaded sides of every crown, real and impostor.
- **Compare in the next tour**: St Aldo's prominent (0068 of tour 0938), Wick windmill prominent (0010), Hollestead overview (0004) and
  Foxerley overview (0124): no pointed crowns, sun-lit faces visibly lighter than shaded ones at 400-800 m, oak impostors mid green
  next to the real oaks, birches orange-yellow not flat orange, no change of tone or shape at 320-420 m, 750 m and 1 km.

## Distant trees that carry the light (2026-10-06, evening)

The review tours of 13:54-15:38 showed the impostors as flat, uniform slabs whenever their sun-lit faces were not in view: St Aldo's
5° telephoto looks almost into the sun (heading 161°, sun at 152.5°), so every visible face of a solid crown is in shade and all of them
take one ambient colour (oak impostor sRGB 73, 76, 71 behind real oaks of 81, 87, 79 that still show sun-lit leaves at 109, 117, 89),
and the raised overviews (The Muddy Fox, Hollestead) show grey, contrast-free blobs beyond the band where the real crowns have lit tops
and dark undersides. A crown of leaf cards has leaves facing every way, so a high sun lights some of them on every side; a solid does not.
- **Foliage light** (`FarTerrain.shader`): faces with vertex alpha 0 take the sun half-Lambert, (N·L + w) / (1 + w), with w = 1 while the
  sun stands above 30° and fading to 0 at the horizon (a low sun lights only its own side). Added as the difference on top of URP's own
  diffuse; every other face (alpha 255: ground, houses, rocks, bark, the PolygonGeneric trees and pines, which are faceted solids like
  their meshes) is lit exactly as before. No new pass, keyword or property; fog untouched; NaN bit tests unchanged.
- **Lit-top ladder** (`TreeLobes.LeafTop / LeafMid / LeafUnder`, `FarTreeStyle.LeafCrown`: birch, Meadow, fruit): each lobe's three rings
  take 0.907 / 0.72 / 0.432 of the leaf colour (top a touch warmer, underside a touch bluer, like the light and dark leaves of the Synty
  texture). Every band triangle starts and ends on the ring whose colour it takes, so the bands zig-zag light / mid / dark at no cost:
  same vertices, same triangles (near band 26 per lobe + 9, Mid tiles 20 per lobe + 3, Far tiles 12), same silhouettes and swap rule.
- **Per-tree tone**: instanced impostors ±4 % from a hash of the instance matrix's rotation and scale (stable under origin shifts); far
  tiles keep their baked ±7 %.
- **Fit**: a face-by-face model of the lobe under the tours' noon light (sun 1.35 at 57.5°, trilight ambient), with the scale chosen so
  the impostor is never lighter than the real crowns: backlit noon ×1.26 / 1.21 / 1.11 (R, G, B) of the old mean, noon overview
  ×1.06, front-lit ×0.99, low sun ×0.86-0.95. Predicted at St Aldo's: oak 73, 76, 71 → 78, 80, 73 (real 81, 87, 79), birch 82, 77, 69
  → 88, 81, 71 (real 95, 72, 52); Muddy Fox band oak 115, 117, 116 → 116, 118, 116 (real about 107, 111, 107, edges included); low sun
  within 1-2 sRGB of before (the haze sets the tone there).
- **Compare in the next tour**: the same four shots plus St Aldo's `_low`: crowns beyond 320 m show a lighter top and darker underside
  even when backlit, no grey band at 320 m in the overviews, nothing lighter than the real crowns just inside the band.

## Inns, hamlets and villages, and their people (2026-10-05)
User, on the review tour of seed 12345: every inn was one small stone cabin with a well, a shelter and a signpost ("The Crooked Stag",
"The Rusty Lantern", "The Drover's Rest", "The Old Wheel" each a hut on a lawn), every village house the same cabin. The game is about
walking from inn to inn, so inns must read as inns and villages as villages, with people in them. Endless only: the finite world is
bit-identical (golden hashes pass; `SettlementDresser` unchanged and still the inn fallback when a palette has no tavern). Measured
offline on seeds 12345, 42 and 777, every settlement of the first 26 km (.NET 8 harness in the session scratchpad `villages/`).
- **Buildings by measurement** (`World/BuildingTable.cs`): the Fantasy Kingdom presets measured from their FBX (ground-floor walls,
  full extent with roofs and steps, height, eaves, the stone base's depth, the foot of the door steps, the stone chimney or a roof vent,
  wall and roof colours, triangles; see `ASSET_CATALOG.md`). The ground is meant at the pivot (a ledge where the steps start, the floor
  1.2-1.3 m up, the base 2.5 m deep); a building is seated with the pivot 0.55 m under the highest ground of its body and refused where the
  ground under it rises more than 1.9 m. Doors face local +Z except `Hut_01` (+X) and the arch house. Families are resolved to prefabs in
  `SyntyCatalog` order (palette v12; the menu logs each family against the table).
- **The shared placing rules** (`SettlementKit`): a building is placed by the middle of its front facing a direction; its whole outline
  must keep the road's edge plus a 1.5 m verge (exact distance to the road's polyline: the window's distance field is 1.5 m conservative),
  its footprint (a grid of circles of at most 3.5 m cells) must clear other footprints by 0.9 m; props have a footprint and the same verge
  rule; rock-like pieces (the refuge's cairn) go through `RockPlacer`. It records worn paths, yards and the people's spots (`PlaceAnchor`:
  doors, the inn door, the well, benches with their seat height, garden gates at the verge, work places, stalls, the green).
- **The roadside inn** (`InnComposer`, stream 1291): the `Tavern_01` preset with its outside stair and balcony to the road across a worn
  front yard (the stair landing 5.5 m beyond the verge); two benches against the front wall under the balcony; two street lamps at the
  yard's corners; a fingerpost and the inn sign at the verge; a hitching rail along the road; stables (65 %, else an open cart shed)
  beside it, set back, with a hay pile and a cart; on the other side the well, a table with two benches and, some evenings, a fire with a
  tripod and a log; stacked barrels, crates and sacks against the side wall, firewood, a vegetable patch (cabbages, pumpkins) fenced
  behind, a privy; a birdhouse or chime. The inn's rest point (`InnRest`) stands at the foot of the tavern's stair, facing the road
  (`SettlementLife`).
- **The refuge at a col** (stream 1292) keeps its character: a hut (`Hut_01/02`) by the road, a lean-to shelter beside it, a curved
  dry-stone windbreak behind, a fire with a tripod, a bench and a log, a lantern by the door, firewood and goods, a hitch and a sign by the
  road and a cairn across it. Its rest point is the hut's door.
- **Hamlets and villages** (`VillageComposer`, stream 1090 as before): houses (nine presets, never the same twice in a row) in rows on both
  sides with their doors to the road behind front gardens of 2.5-5.5 m; a smithy in a village (anvils and a grindstone in its open
  workshop), stables (75 % of villages, 35 % of hamlets), huts at the ends, a privy behind a third of the houses; the arch house
  (`House_Archway_02`) beside the road at a village's end with a worn lane through it, because its opening is 4.3 m wide and 3.3 m high
  (`House_Archway_01`: 4.15-5.5 m) and cannot carry the 5 m road; the green with the well, benches round it, a fire in a hamlet and one or
  two market stalls in a village, fruit trees at its edge and houses facing it; per house a fence behind, firewood, stacked goods, a
  kitchen garden or a cart, a bench by the door, a birdhouse or a wind chime; street lamps; at most one short dry-stone wall run per side,
  never across a garden gate. The stone arches that stood in the road are gone. The orchard, windmills and crop fields are as before.
- **Smoke and lights** (`SettlementLife`, `EndlessTileBuilder`): every dwelling (tavern, houses, smithy, stables, huts, arch house) smokes
  from its measured chimney (or a vent near the ridge) and lights its front window at night; the lamps light up. A place spread over
  several tiles now gets smoke and lights in every tile (before, only the houses in its waypoint's tile had them). The settlements share
  the 16 point lights as before (nearest first).
- **Distant tiles** draw every preset as a solid of its measured walls, eaves, ridge and colours (`FarPropStyles.Building`: 14 triangles).
- **Seats**: the benches are seats for the traveller ("Bench" is a log seat); crate and barrel stacks, workbenches and stalls are not.
- **People** (`Scripts/People`, streams 1301 cast, 1302 per person): `PeoplePlanner` (pure) gives each place its cast from the dressing's
  spots: at an inn the innkeeper (porch, door, gate, a bench), a groom at the stables (to the well and back) and now and then one or two
  guests on the benches who stroll to the gate; in a hamlet or village two to five peasants each walking door, garden gate, then the well,
  a bench, a stall or the green on their own side of the road and back, the smith at the forge, the merchant at a stall, a groom; a monk,
  nun or priest at a chapel landmark within 750 m. Every leg is checked to keep 0.6 m beyond the road's edge and out of every building's
  walls (the smithy's open workshop excepted); a stop that cannot be reached is dropped. `VillagerBrain` (pure) walks the round out and
  back at 1.05 m/s, waits a seeded time at each stop, sits on benches, turns to look at the traveller within 7 m while standing, and goes
  home and indoors outside its hours (innkeeper 6-23.5 h, peasants 6.5-20.5 h, each with up to 0.6 h of its own either way).
  `PeopleManager` (order -31, creates itself in Play mode on the endless road; no scene change) plans casts on a worker thread for the
  places within 420 m, shows the nearest 12 within 200 m on built ground (released beyond 230 m; after the first fill nobody appears closer
  than 110 m except at their own door), pauses animators beyond 80 m, keeps positions absolute through the floating origin, and is off when
  the safety ladder sheds the animals. `Villager` feeds the Animator its speed; `VillagerPose` (order 125) lays a procedural seated pose
  over the idle (the clip set has no sitting clip).
- **Characters** (`MedievalZen/People/Build People Palette`, also run by itself when the editor loads): first-party prefabs in
  `Prefabs/People/` from the Fantasy Kingdom characters, each keeping only its own body (the Synty prefabs carry all 22), without
  colliders; `Animation/FantasyKingdom_Humanoid.asset` (Hips moved from Synty's `Root` to the pelvis, fingers left out: both hands' finger
  bones share names in the model); `VillagerMasculine` / `VillagerFeminine.controller` (a 1D blend on `Speed` from `Idle_Standing` to
  `Walk_F` at 1.05 m/s, the walk clip slowed to x0.723; in-place clips, no root motion); `Resources/PeoplePalette`. Measured offline: the
  characters use the player's POLYGON rig bone for bone (same names, rest positions and rotations); only the fingers differ.
- **Measured** (before and after; seeds 12345 / 42 / 777, 26 km each; GameObjects and LOD0 triangles of a place's own props, its crop
  fields left out):
  - inns (13 / 14 / 14): GameObjects 12 to 31-32 (max 36); triangles 17 k to 68-69 k (max 86 k, the tavern alone 34.6 k); far solids 14
    to 40-41 triangles; the refuge 12 to 15-16 GameObjects and 22-26 k triangles.
  - villages (12345: 2, 777: 1): GameObjects 114 / 114 to 127 / 153; triangles 198 / 210 k to 421 / 454 k (houses 17.5 k on average, the
    cabin 10.3 k); far solids 244 / 258 to 401 / 398 triangles; 26-27 buildings (15-18 houses, a smithy, stables or not, huts, the arch
    house, privies).
  - hamlets (42: 2, 777: 1): GameObjects 78 / 114 to 92 / 120; triangles 125 / 132 k to 242 / 220 k; far 150 / 140 to 227 / 168.
  - nearest any prop comes to the road's edge: 2.45-3.38 m before (plus the old arches in the road) and 1.85 m now (the verge rule:
    roadside pieces' centres at 1.5 m plus their radius; building outlines at 1.5 m and more).
  - dressing a place (once, on a worker thread): inns 22-26 ms before and after; villages 69-72 to 72 ms; hamlets 50-57 to 50-59 ms.
  - people: 41 / 44 / 46 over 15-16 places (1-4 at an inn, 6-7 in a village, 3-4 in a hamlet); planning a cast 0-55 ms on a worker
    (the first in a region pays for its landmark plan); two simulated days per person: never closer than 1.59 m to the road's edge, never
    inside a wall.
  - the presets' collision meshes are small (tavern 664, stables 492 triangles), so a first instance's collider costs little.
- **Tests** (`EndlessVillageTests`, 17): deterministic dressings in any order, the tavern's (and the hut's) door faces the road and
  holds the rest point, nothing on the road or verge, the arch house beside the road, houses vary, settlement waypoints pinned by hashes
  taken before the change, distant solids, casts per place within the caps, people never on the road nor in walls over a simulated day,
  the brain's sitting, looking and going home, the table's consistency; `SittingTests` gained the bench and stack cases; the village,
  start-inn and far-mesh tests were updated for the new buildings. Offline: all 223 harness tests and the 14 far-mesh tests pass.
- **Where to look on seed 12345**: The Merry Goose at the start; The Crooked Stag at 1,673 m (tour stop 1,643 m; stables beside it), The
  Tired Oak at 3,119 m, The Rusty Lantern at 4,677 m (a priest at the chapel landmark nearby), The Drover's Rest at 6,374 m (the refuge
  at the col); the first village is **Hartdenfield at 11,865 m** (18 houses, the smithy on one side across from the green with its well,
  three benches and a stall, the arch house beside the road at its far end, six people), then Hollestead at 18,500 m.
- **User steps**: none expected. On the next editor load the world palette rebuilds itself (v12; read "Endless road inn and village
  families") and the people palette builds itself (read "People palette built" and its skeleton check). If either log shows missing
  prefabs, run `MedievalZen/Create Default World Palette` or `MedievalZen/People/Build People Palette` by hand.
- **Not verified without Unity**: everything visual: the presets' seating on the ground (the 0.55 m sink comes from Synty's demo town),
  whether the door steps meet the ground, the tavern showing its stair side rather than its other door to the road, smoke vents on roofs
  without chimneys, the window lights at the measured fronts, the lamps' lantern point; the people above all: the rebuilt avatar (the
  builder may refuse it; Synty's avatar is then used, with the hips on `Root`), the clips on the Fantasy Kingdom bodies, the walk speed
  match, the procedural seated pose (hips height, the legs' swing), the rider and bartender assumed masculine, a first instance's
  main-thread cost (a 35 k-triangle preset with its collider, a character), and the frame time with a village in view (about +220 k
  triangles and their shadows against the cabins).
- **Not built**: names lettered on the inn signs (no font asset; the HUD names the inn as before), washing lines (they hang from their
  pivot and need posts), interiors, people on the road between places.

## Bridges that read as bridges, and the inns' name boards (2026-10-06)

**Why Oakbrook Bridge looked like a grey box (tour 0043/0045).** Measured from `SM_Env_Bridge_Stone_01.fbx`: 20 m long along its z (the
road), 5.68 m wide (a 4.6 m deck between 0.54 m parapets), from 3.80 m below its pivot (the piers' feet) to 3.15 m above. The deck is a
hump: 0.24 m above the pivot at its ends, 1.92 m at the crown, 47 % at its steepest. One arch, only 4.8 m wide, its soffit 1.24 m above
the pivot at the crown. The prefab, its single palette material and its 8 colliders were right and it was not buried: the arch stood
1.7–1.9 m above water 5.1–5.5 m wide. But from the road a traveller sees only the 4.6 m hump rising at 47 % between two walls (the arch
faces sideways), the water lies 0.9 m below the road in a channel whose lip hides it from 15–30 m back, and for 7 m (+2 m fade) either
side of the crossing, along the stream, the 2 m height grid made the water's edge, although the bridge covers only 2.84 m of it. The
side shot looked away from the bridge (a review-tool matter).

**What changed.** `BridgeShape` (in `StreamNetwork.cs`) holds the measured mesh and the placed scale 1.12 × 0.85 × 1.35 (across, up,
along the road: the palette material is a flat colour atlas, so the stretch does not smear a texture): 27 m long, a 5.1 m deck, the hump
1.6 m at 30 % at its steepest, the arch 6.5 m wide. The tile builder applies the scale to `PropRole.Bridge` only. The bed under the arch
is up to 4.2 m wide (was 3) and 1.25–1.6 m below the road (was 0.95–1.35); it widens again from the bridge's faces and spreads into a
pool below the arch (35–70 % wider for larger streams, over 14 m). A bridge's road part of the stream (where the ground makes the
water's edge) is now its own half width + 1.4 m (4.6 m; was 7 m), so the drawn water and banks run up to the arch. A stone against
each pier and reeds on each bank at both faces (stream 1381). The road stays level ±17 m around the crossing (8 % limit elsewhere);
the deck's hump is the bridge's own and the traveller walks it on its colliders (the walk test steps it at 1.4 m/s, 60 fps).

| Bridge (0–20 km) | arch over water before → after | deck crown over water | widest water at the arch | hump grade | ground above water in the span |
| --- | --- | --- | --- | --- | --- |
| 12345 Oakbrook 5,297 m | 1.92 → 1.77 m (4.8 → 6.5 m wide) | 2.60 → 2.35 m | 5.5 → 6.4 m | 48 → 30 % | 0 / 21 samples |
| 12345 Bellwash 8,144 m | 1.69 → 1.82 m | 2.37 → 2.40 m | 5.1 → 5.6 m | 48 → 30 % | 0 / 17 |
| 12345 Sedgebeck 19,825 m | 1.75 → 1.79 m | 2.43 → 2.37 m | 5.2 → 6.4 m | 48 → 30 % | 0 / 23 |
| 777 Oakburn 6,590 m | 1.74 → 1.83 m | 2.42 → 2.41 m | 5.2 → 5.6 m | 48 → 30 % | 0 / 17 |

Seed 42 has no bridge in its first 20 km. Drawn water within 30 m of the road beside each bridge: 23 m → 26 m a side. Not done: a
far-tile shape for bridges (Mid/Far tiles draw none; the bridge is 27 m, a few pixels at 300 m).

**Inn name boards.** `InnComposer.PlaceNameBoard` (last, so nothing earlier moves; no randomness) puts a `PropRole.InnSign` spot
beside the tavern, 1.5 m beyond the verge, faces along the road (read while walking up from either way), tried at 4, 9, 14, 19 m either
side. The tile builder hands it to `SettlementLife.PlaceNameBoard`, which builds two posts, a beam and a 2.4 × 0.62 m board hanging
1.8 m up from unit cubes in `SignWood.mat`, and letters the inn's name on both faces: a mesh of glyph quads from Unity's built-in
`LegacyRuntime.ttf` (no font asset, no package), capitals 0.25 m (or smaller to fit), in `SignLetters.mat` (URP Simple Lit, alpha
clip, the font atlas set at runtime on a copy; re-lettered when the atlas rebuilds). Lit, not emissive. The lettering has a LODGroup
culled at about 60 m; the board is 7 boxes (84 triangles), the lettering 2 quads a letter. Materials: palette v14. No door sign (the
tavern's door is at the top of its outside stair, under the balcony) and no village boards yet.

## The forest floor and shaded leaves (2026-10-06)
Review of 2026-10-05, findings 7 and 8: the floor between the trunks was the meadow's lawn with single ferns, and shaded oak crowns
read blue-black under Rich light. Endless only; golden hashes pass; `DetailPlanner` tables, `ScatterRules` and the trees are unchanged.
Measured offline on seed 12345 (scratchpad `forest2/`, Forest 1,950-4,700 m and the tour stops 2,285 / 2,885 / 3,485 / 4,085 / 4,345 m).
- **Paint** (`Planting.WoodShade.cs`, streams 1361-1366): a shade measure from the land cover's forest weight (0 below 0.4, full from
  0.75), 45 % on the road's margin rising to full by 10 m, 0 inside the glades and full 6 m outside them. Under it up to 90 % of the
  grass paint becomes the wood floor mix (`GroundLook.WoodFloorShares`: forest-floor layer with 20-45 % mud as leaf litter and 2-6 %
  dirt, the share following 22 m litter drifts), plus bare earth round the trunks on near tiles (2.2 m × scale). Near and distant
  tiles run the same pass; the far mesh's forest-floor and canopy zones use `GroundLook.WoodFloorColor`. Glades are painted after.
  Deep wood (forest ≥ 0.75, 12-200 m from the road): linear (0.140, 0.180, 0.017) L* 46.9 → (0.121, 0.138, 0.026) L* 42.2; luminance
  0.79 → 0.62 of the open meadow and 0.68 of the glades; ΔE to the meadow 5.6 → 15.9, to the glades 7.0 → 18.7; near/far ΔE 0.4.
- **Sward** (`ShadeDetails`): under the shade tall grass and flowers go, medium clumps thin by up to 75 %, short by 45 %, ground cover
  fills up to 60 % of the growing cells; mud thins the rest (DetailPlanner's grow mask). Detail cover 4-30 m 97 → 91 %.
- **Understorey** (`WoodUnderstorey`, `WoodLogs`): clusters of 5-15 ferns and undergrowth (fern drifts, undergrowth with ferns round it,
  undergrowth clumps) on a 6 m grid, likelier at glade edges and 8-9 m from the road, less under the densest canopy; plants keep only
  1.1 m off small occupied discs (trunks, bushes), so they stand inside a crown's footprint; logs in the wood (30 m cells, a third,
  within 70 m of the road) ringed by 6-10 ferns, half with mushrooms. Beyond 25 m from the road only the light fern and bush meshes.
  Plants in 2.5 m groups of 5+: 1 → 71 %; singles 86 → 40 %. Floor cover 30-150 m 19 → 22 %.
- **Budget** at the tour stops (100° view, near tiles): instances 2,858 → 4,406; plants 1,524 → 3,073; visible triangles mean 6.54 →
  6.92 M, max 6.98 → 7.31 M; shadow casters (plants to 40 m) 0.86 → 0.90 M triangles; registered instances within 420 m 24 k → 39 k.
  Test caps per tile: plants 3,200 → 5,600, all instanced 4,000 → 6,400.
- **Shaded leaves** (`LightLookMath`): the cool shadow toning (0.45, 0.49, 0.56) → (0.47, 0.49, 0.52); under a canopy the forest term
  tones the shadows green (0.5, 0.57, 0.47), contrast −4 and exposure −0.08 (was −0.15). Model (`LeafAfterGrade`, oak leaf 0.045 /
  0.053 / 0.006, N·L 0.6, crown occlusion 0.7): shaded/lit luminance 0.358 old look, 0.281 Rich light in the open, 0.341 under a full
  canopy; the shaded leaf G/R ≈ 1.9. Rich light off is the old look exactly (tested).
- **Not verified without Unity**: how the mud/leaf blend reads on the terrain shader, the clusters' look and fern/bush mix, the frame
  time with 39 k registered instances, and the leaf colour (fog and SSAO are not in the model).

## The wood floor under the crowns (2026-10-06, afternoon)
Tour 0938 (seed 12345, 11:30, clear; 2,400 / 3,600 / 4,200 m, Asherford Chase) showed the morning's wood floor as the meadow's lime lawn
with yellow flowers. Measured offline (scratchpad `forest3/`, `Harness probe|floor|roadf`, plots `forest3/plots_before|plots_after/cam_*.png`):
- **Causes.** (1) The paint was applied, but the forest-floor layer (Grass_Leaves_Texture_01) is a lime lawn with a few tan leaves: at
  2 m tiling the leaves read as the "yellow flowers" (the flower details there were 0.00-0.01 /m²), and the mix was 55-60 % of it. Paint
  under crowns vs the open meadow: luminance 0.71, ΔE 14 (8-bit means), and the rendered sunlit ground compressed that to 0.84
  ((122, 139, 6) against the meadow's (138, 149, 16)). (2) The paint followed the land cover only, so under a crown and between crowns
  were the same (ΔE 1.5). (3) The land cover clears the forest for 10-15 m either side of the road (forest weight 0 within 10 m, 0.25 at
  11 m, 0.97 from 16 m, at all three stops), so the strip seen from the road, under the roadside oaks, was painted plain lawn.
- **Paint** (`Planting.WoodShade.cs`, no new streams): `CanopyMap` rasterises the tile's crowns (radius from the measured far-tree
  shapes × scale; full cover to 0.8 r, gone by 1.2 r; 0.5 m raster, 3.7 ms a tile); under a crown the floor becomes
  `GroundLook.LeafLitterShares` (mud 0.62-0.72, dirt 0.16-0.22, the rest forest floor; up to 97 % of the grass channels) where the forest
  weight is ≥ 0.3 (full from 0.5: the scattered oaks of a wood's open parts get their patch, meadow trees not); between crowns the moss
  (`WoodFloorShares`) is darker (mud 0.44-0.64). Within 16 m of the road the floor takes the forest weight 16 m out from the road
  (`RoadsideReach`); the verge keeps 45 % shade to 3.5 m (full by 8.5 m) and 30 % of the crown litter (full by 7 m). Glades: 0.
  Distant tiles have no crowns: `ExpectedCanopy` (0.45 in a deep wood, `GroundLook.WoodCanopyMean`); near tiles blend their crowns into
  it within 6 m of the tile edge (the scatter keeps only a tile's own trees, so a crown on a seam was cut). The far mesh shows a face led
  by forest floor + mud (FF ≥ 0.2, sum ≥ 0.6) as the wood floor zone (`FarTileMeshData.ChooseZone`), whose colour is the blend at 0.45.
- **Sward** (`ShadeDetails`): under a crown tall grass and flowers go, 95 % of medium and 85 % of short clumps; grass /m² under crowns
  0.15 → 0.03, between crowns in the wood 0.12 → 0.06 (fewer instances).
- **Numbers** (within 60 m of the three cameras, ≥ 6 m from the road, against the open meadow paint of 600 / 1,200 / 1,800 m):
  under crowns sRGB (102, 109, 43) → (96, 90, 61), luminance 0.71 → 0.51, ΔE 14 → 35 (with an estimate of the grass details: 0.72 → 0.50,
  ΔE 15 → 38); between crowns 0.68 → 0.55, ΔE 15 → 27; glades vs meadow ΔE 1.7 (unchanged). Deep wood 12-200 m (1,950-4,700 m):
  luminance vs open 0.62 → 0.52, ΔE 16 → 28; near/far ΔE 0.4 → 0.7 (per tile median 0.8, max 2.6). Meadow stops: 3.7 % of the
  ground lies under a crown with forest ≥ 0.3 (wood edges); the open meadow paint is unchanged. Understorey numbers unchanged.
- **Unverified without Unity**: how a mud-led floor (0.5-0.7 mud) reads on the terrain shader (mud texture tiling, wetness), the
  sunlit-gap look between crowns, and villages inside a wood's road clearing (the roadside rule may paint litter round them). None of the
  existing layers is a leaf-litter brown: proposed (not added) a layer from Synty's `Grass_Leaves_Heavy_Texture_01` (mean linear 0.164,
  0.123, 0.022, luminance 0.125) to replace most of the mud under crowns.

## Streams that read as water, and bridges that read from the road (2026-10-06)
Review T0021 (Ottersike Ford), T0085/T0087 (Sedgebeck Bridge), 15:35, clear. Measured in the shots (sRGB): water (145–172, 144–160,
135–151), a pale grey-lilac sheet; a pale band (165, 165, 130) along each edge with a lime line (105–132, 107–126, 8–25) just outside it;
the bank itself mottled brown-olive (100–160, 90–135, 50–75) with parallel light/dark stripes, against terrain grass (110–134, 113–128,
17–27); the bridge from the side (29–45, 30–46, 34–50), from the road (96–120, 85–104, 76–91), a grey wall across the brown road.
- **Water** (`StreamWater.shader`): the old surface was URP PBR at smoothness 0.88, so at walking eye height (views 5–15° above the
  water) the Fresnel term mirrored the baked probe's pale horizon over 86 % opaque water. Now a stylised model, no probe: albedo
  from olive bed colour at the edges (alpha 0.32) to dark green-blue (0.018, 0.048, 0.050) in the middle (alpha 0.9), lit by the SH
  ambient + main light with shadows; streaks stretched along the flow (0.5 × 4 m) scroll downstream as colour, stronger where it runs;
  foam at the edges, rocks, riffles and a ford's thin water; a sky tint ≤ 0.32 × Fresnel from the SH ambient in the reflected direction
  (follows the day cycle, dark at night); a small sun glint; output ≤ 2.0 before fog.
- **Banks** (`StreamBank.shader`, `TileStreams`): most likely (not provable offline) the lime line is grass showing right at the water's edge. The bank's flat toe
  rises only 0.18 m per metre and the ground under it lay 5 cm lower and was painted lush grass, so wherever the terrain's simplified
  mesh rose through the toe a thin line of the terrain's grass appeared between the pale water edge and the mud lip; the pale band is
  the old edge foam over 10 % alpha water. Now the ground is painted mud to 0.9 m beyond the water (`ToeMud`), the bank between lip and
  drape is broken by value noise in stream space (bare earth, gravel, darker tufts; continuous across tiles: uv = metres across, + 100 on
  the right bank, and metres along), the drape (vertex alpha) is the terrain's plain lush grass, and upward-only hummocks up to 0.16 m
  (streams 1382–1385, zero at the lip and the top, so the edge, the drape and the carved ground are unchanged) break the extruded profile.
- **Fords**: the banks no longer sink in the road's part; the water there is 0.7 × the channel's (at least 0.8 m a side), never below the
  road bed (+3 cm), and the banks lie 3 cm above the uncarved road bed in road dirt. Water points on the road under the ground: 0/2880,
  0/2880, 3/2906 (worst 1.3 cm) on seeds 12345/42/777 (4 fords each).
- **Bridge**: the prefab uses the same material as the houses (`PolygonFantasyKingdom_Mat_01_A`, smoothness 0.2, metallic 0); its atlas
  cells average sRGB (90, 88, 83), 35 % darker than the tower's walls (110, 104, 97), and the face seen from the stream in the afternoon
  is in shade, lit by an ambient of about 0.12–0.2 (the shaded grass beside it measures the same ratio). The non-uniform scale does not
  change its lighting (Unity transforms the normals; the flat atlas has no texture to smear). Now a first-party copy
  (`Materials/Streams/BridgeStone.mat`, `_BaseColor` (1.6, 1.52, 1.4), smoothness 0.1; `WorldPalette.bridgeMaterial`, palette v16)
  and a road surface on the deck (`TileStreams.DeckSurface`: ±2.3 m, every 0.5 m, 4 cm above the hump, meeting the road at both ends,
  drawn once by the tile holding the bridge's centre, in the bank submesh and collider) so from the road the hump reads as the road
  rising over a bridge between stone parapets.
- **Unverified without Unity**: every look above (colours, streak and foam strength, the sky tint at dusk and night, the bridge copy's look (Generic_Basic
  multiplies the atlas by `_BaseColor` before the base colour and alpha, checked in the graph; values above 1 are untested), z-fighting of the deck surface at distance), and walking a ford on the new bank collider.
  Not done: parapet end posts or approach walls (done below).
- **Bridge walls (2026-10-06, after tour 1535 shot 0073: from the road a road-coloured deck and one grey block).** The mesh's own
  parapet, measured by slicing the FBX: inner face at least 2.163 m from the axis (unscaled), top up to 1.332 m above the deck table,
  i.e. 0.96–1.13 m once scaled; low, thin and endless, with nothing at its ends. Now `BridgeWalls` (pure, `Generation/BridgeWalls.cs`,
  stream 1386 for the stones' tones) builds per bridge one faceted mesh of 432 triangles: a parapet each side for the full span, inner
  face at 2.80 m (the 5 m road + 0.3 m), outer face just outside the bridge's side, body to 1.06 m and a coping to 1.19 m above the deck
  table (1.14–1.18 m above the walking surface: it must enclose the mesh's own parapet, so it is taller than the 0.9–1.0 m first
  asked), its top following the hump's measured stations (simplified: chord at most 1.2 cm below, 4 cm above); an end post at each
  of the four ends (0.62 m along, from the parapet's inner face to 0.1 m proud outside, 1.36 m + a 0.13 m cap above the road at the
  bridge's end); an approach wall beyond each post, 3.1 m long, 0.5 m thick, splayed 20° outwards, its top sloping from 1.08 m above
  the road to 0.42 m above the ground at its far end, its foot 0.3 m under the lowest rendered ground along it (asked of the tile's
  final heights, `Ground.LowestGround`). Flat shaded, tangents for the material's normal map, no vertex colour (`Generic_Basic` does
  not read one): each stone course takes one of the atlas's three grey stone cells (u 0.46, v 0.1565 / 0.181 / 0.207), the coping
  mostly the lightest. Box colliders: one per parapet segment on the road side (0.34 m thick, from the inner face outwards), one per
  post and approach wall; none nearer the axis than 2.80 m. Drawn with `WorldPalette.bridgeMaterial` by `BridgeWallsView` in its own
  builder slice ("bridge walls"), destroyed with the tile's root. To let the new parapet enclose the old one with the road clear,
  `BridgeShape.ScaleX` went from 1.12 to 1.31 (the bridge is 7.44 m wide, the road surface on its deck 5 m instead of 4.6 m), and the
  bridge now lies along the chord of the road over its length, half way between the crossing and the chord's middle: on the tangent
  the road's centre line strayed up to 0.45 m from the axis at the ends (Wether Bridge, seed 12345), now 0.25 m at most; over the
  first 60 km of seeds 12345/777/42 (23 bridges) no wall comes nearer the road's centre than 2.55 m. Oakbrook Bridge (12345,
  s 5297): 27 m span, clear 5.60 m between parapets, posts' caps 1.49 m above the road at its ends, the crown's walking surface 1.47 m
  and its parapet 2.62 m; from 40 m before the near end (eye 1.6 m, the road there 0.48 m lower) the caps stand 0.37 m above the eye
  (0.5°), the crown's road 0.35 m above it (the stream stays hidden behind the hump, as on any humped bridge).
  Unverified without Unity: the look of the three tones under `_BaseColor`, the joints with the mesh's own parapet, walking the deck
  between the new colliders.
- **Bridge walls in stone (2026-10-06, after tour 1717 shot 0075: from the side a tall blank grey slab hid the arch ring and the
  mesh's stonework).** The 0.96 m parapet, its outer face 0.45 m below the deck in front of the bridge's side, is gone. Sliced again
  (FBX, unscaled): the mesh parapet's outer face is the bridge's side (|x| 2.839, from 0.68 m below the deck to its top, a recessed
  band at 2.706 from 0.25 to 0.9 m above the deck), its inner face 2.163-2.272, its coping stepped: its top (tables `BridgeShape.ParapetTopHigh`
  / `ParapetTopLow` / `ParapetTopGuide`, every 0.25 m) 1.13-1.33 m above the deck table at its highest, up to 0.29 m lower between
  steps. Now on each side: a **coping** of 28 stones laid on the mesh's own parapet (stream 1387), 0.62-1.08 m long, 0.13-0.17 m deep,
  its inner face at 2.80 m, overhanging the bridge's side by 4-8 cm (each stone its own), its top 5 cm (at least 3 cm) over the mesh
  parapet's highest point under it, ±1.2 cm jitter (steps between neighbours up to 4.7 cm on the flanks), each stone its own atlas
  grey; where the mesh parapet's stepped top dips below a stone, a bed face 0.1 m inside the side closes the gap; a **lining** of two
  courses of blocks (stream 1388, joints of their own) 2 cm behind the coping's road face, from 3 cm under the deck into the coping,
  hiding the mesh parapet's inner face. Below the coping nothing stands in front of the bridge's side: the lowest point of the walls
  outside it is 0.83 m above the deck table (the mesh's own coping band starts at 0.77 m). The parapet top is now 0.96-1.26 m above the
  walking surface (lower at the ends, where the mesh's parapet is). Posts in three courses (the middle one recessed 1.2 cm), a cap with a
  7 cm hipped top; approach walls in two courses per segment under three coping stones (stream 1389). No flat panel longer than 1.2 m.
  1,358-1,368 triangles per bridge (23 bridges, seeds 12345/777/42, 0-60 km); colliders, posts' and walls' places, clear width (5.60 m)
  and nearest wall to the road's centre (2.55 m) unchanged. Unverified without Unity: the side and road views.

## A worked patchwork round the villages (2026-10-06)
Review P0038/T0055: Hollestead stood on an empty lawn with one white field. Diagnosed offline (seed 12345, harness `fields/` in the session
scratchpad) before changing anything:
- **Empty lawn**: `FieldMosaic` kept every field out of a disc of the place radius + 260 m (+ the field's half diagonal) round a hamlet
  or village: round Hollestead the nearest patchwork field was 390 m from the centre, 4 fields within 400 m, and the four tiles under the
  village carried 0, 15, 0 and 0 boundary pieces (boundaries only enclose patchwork fields). The village's own fields (`PlanFields`
  through `DressingCache`) are three small fenced plots (half sizes 11-19 m by 7-12 m) at 170-200 m. The paint under the lawn was
  lush 0.27, dry 0.67, flowers 0.04 (dirt 0.02).
- **The white field** was Hollestead's own rapeseed plot (-842, 17630) (65 rapeseed clumps) and the far white strip a planted rapeseed
  field of the patchwork (-701, 17837): the rapeseed prefabs wrap the Meadow models in `Flowers_Field_Mat_01`, whose texture
  (`Flowers_01.tga`) has white flower heads; seen from above a field is white heads (measured in the shot: sRGB 220, 199, 181). Not paint.
- **What changed** (endless only; golden hashes pass):
  - `FieldMosaic` takes `DressingCache` and keeps fields `FieldPatchwork.FootprintMargin` (9 m) off each settlement's
    `SettlementFootprint` (dressing circles, yards, trees, own fields, lanes and a core of 0.6 x the place radius) instead of the
    radius + 260 m disc; within `VillageReach` (420 m) of a hamlet or village outside forests and passes fields are laid whatever the
    farmland weight (`MosaicField.NearVillage`), and those within 90 m of the footprint are planted. Inns use their footprint too.
  - `FieldPatchwork.Paint` (post-pass after the wood floor, near and distant tiles alike, the same function of absolute position): a
    village field is 3-6 long strips cut across its short side with 1.2 m grass balks, other fields 1 (70 %) or 2 blocks. A strip's look
    follows its field's (`TileGenerator.FieldLook`: stubble fields stay stubble-led so haystacks fit, fallow fields are whole pastures for
    the herds): mixes of the eight shared layers in `GroundLook.FieldShares`, sRGB: pasture (120, 131, 39), hay meadow (130, 131, 61),
    green crop (100, 114, 32), ripe grain (134, 131, 73), rapeseed ground (127, 130, 52), stubble (141, 133, 87), ploughed (130, 114, 92);
    lawn (122, 131, 41). Each has a dominant channel, so mid and far faces take the same zone. `ThinDetails` thins the grass details
    on ploughed (none), stubble (0.3), grain (0.4), green crop (0.55) and rapeseed (0.7) strips.
  - Boundaries leave a gateway where any path (a settlement's lanes, pasture gateways, ford tracks) crosses them (`ClearOfPaths`).
  - Crops of the patchwork (wheat, rapeseed, sunflower clumps) are drawn instanced (`PropRole.IsInstancedCrop` within
    `IsInstancedPlant`); a settlement's own plots stay GameObjects. Palette v15: the rapeseed prefabs get a first-party material with
    yellow heads (`GroundLook.RapeseedFlower`, sRGB 237, 204, 41).
- **Measured** (seed 12345; before -> after): fields within 400 m of the centre 4 -> 68 (Hollestead), 61 (Hartdenfield); seeds 42 / 777:
  59-66 / 49-59 round each hamlet or village; nearest field 390 -> 113 m. Boundary pieces in the 16 tiles round Hollestead: mean 233 ->
  372 per tile (the four under the village 0-15 -> 190-599); 17.9-20.0 km: pieces per near tile 251 -> 312 (max 625 both), GameObject
  props per near tile 143 -> 35 (max 1,146 -> 160: the crops moved to instances), crop instances 308 per tile (max 1,686), in a 100°
  view within 300 m: pieces 320 (max 666), crops 917 (max 1,799), all instances 1,827 (max 4,128); 11.2-13.1 km: pieces 254 (max 531),
  crops 205 (max 1,941), all instances in view 1,266 (max 2,473). Mid tiles 10.8-11.6k triangles (max 16,887 / 36,864), far 2.9-3.1k
  (max 5,071 / 12,288). Near tile time mean 129 -> 115 ms (17.9-20.0 km), max 583 ms (the tile that dresses a village first).
- **Tests**: `EndlessFieldTests` (a patchwork of strips round every hamlet and village of three seeds, nothing in a footprint or on the
  road, lanes as gateways, budgets, near/mid/far paint agreement, no pale field paint, determinism).
- **Not verified without Unity**: the look (the strip tones are close to the lawn in hue: ploughed, stubble and grain stand out, green
  crop and hay meadow less so), the rapeseed texture bake and its yellow, the crops' instanced prototypes (draw distance, no LODGroup on
  the generated rapeseed prefabs). **Not done**: a saturated gold or yellow at a distance needs a ninth field layer (farmland near tiles
  would go from two terrain passes to three, and the splat, far zones and palette would gain a channel); headland tracks joining the
  village lanes; stooks; a gap of about one field along each 1,024 m super-cell edge remains.

### Field colours that carry, and the village's closes (2026-10-06, second pass)
- **Three field layers** (palette v19, `SyntyCatalog.MountainLayers` now lists every endless-only layer in channel order): field gold
  (11, Meadow `Grass_02` texture, low contrast, tint (7.64, 3.23, 1.55) -> sRGB 196, 152, 52), field yellow (12, `Grass_01`, tint
  (4.54, 2.59, 1.20) -> 206, 184, 40), field green (13, `Grass_Clover_01`, tint (0.40, 1.16, 1.76) -> 70, 128, 50).
  `TerrainShaper.EndlessLayerCount` 11 -> 14. `TileBlueprint.FieldPaint` marks tiles that paint them; `EndlessLayerSets` builds four
  sets (8 shared: 2 passes; +mountain or +field: 11, 3 passes; pass and farmland: 14, 4 passes). A farmland near tile goes from 2 to
  3 terrain passes. A missing layer folds onto a shared stand-in (flowers, flowers, lush) with a warning.
- **Mixes** (`GroundLook.FieldShares`, 14 wide), ΔE from the lawn (123, 131, 44) before -> after: green crop 8 -> 21, ripe grain 16
  -> 26, rapeseed 7 -> 29, stubble 23 -> 26, ploughed 37 -> 38, hay meadow 10 -> 11, pasture 2 (kept: the herds' grass). Distant faces
  whose weights match a look (`GroundLook.MatchFieldLook`) take its exact colour (`FarTileMeshData.FieldZoneColor`): near/far ΔE 0 in
  a field's interior (it was 4-18 with the leading channel). Planted wheat stands on gold, rapeseed on yellow, sunflowers on green.
  `ThinDetails` keeps 10 % of the grass on grain and stubble, 15 % rapeseed, 20 % green crop, none on ploughed, gardens and tracks.
- **Closes** (`FieldMosaic.Closes`, stream 1452): a grid cell cut by a village's footprint, or across the super-cell seam within
  420 m of a village, is divided into closes of about 24 x 20 m, each 3 m off the footprint: paddocks (fallow: herds, gateways),
  kitchen gardens beside the houses, orchards' grass, hay closes; `FieldBoundaries` encloses each close on its own lines (never across
  the houses). `SettlementFootprint.CoreShare` 0.6 -> 0.35. A composed village's outer lanes run on up to 110 m as worn field tracks
  (`FieldPatchwork.Tracks`, painted as paths, opening gateways; 14 m short of the road; every tile within 230 m of one sees it).
- **Measured** (harness `fields2`, plots `fields2/plots/plot_<seed>_<kind>_<name>_<s>.png`): ground within 300 m of the centre that is
  not plain lawn, before -> after: Hartdenfield 27 -> 39 %, Hollestead 36 -> 47 %, Selton 32 -> 36 %, Penlewell 30 -> 39 %, Ashcombe
  29 -> 38 %, Fernerhurst 42 -> 50 %; closes per village 72-145; the nearest enclosure 3-5 m off the footprint (fields 10-22 m from
  a house). Per near tile round the villages: boundary pieces 345-512 (was 272-403), crop instances 238-612, GameObject props 24-42,
  all tiles of the villages in farmland take the 11-layer set. Near tile time 128-188 ms in the plot runs (other agents' harnesses
  running at the same time); `EndlessStreamTests.NearTiles_StayWithinTheirTimeBudget` failed at 132-165 ms (limit 120).
- **Not verified without Unity**: the layers' look (texture pattern under the strong gold tint, the basemap at distance), the 4-pass
  cost of a pass-and-farmland tile (none occurs round the six villages surveyed), the far-face colour in the shot. **Not done**:
  orchard trees and haystacks in the closes, stooks as instanced pieces (stubble keeps the existing haystack GameObjects).

## Phases
| Phase | Work | Depends on |
|---|---|---|
| E0 | Contract and this document | — |
| E1 | Pure generator, tests, offline previews | E0 |
| E2 | Streaming runtime, adapters, scene, travel, HUD, menu page, persistence (built against a stub generator) | E0, in parallel with E1 |
| E3 | Join E1 and E2, tune in the editor from the user's screenshots | E1, E2 |
| Later | An arid region from the Arid Desert pack, seasons, travellers on the road | E3 |

## Out of scope for the first playable
Junctions and side roads, switchbacks, more than one road.

## Inn archetypes (2026-10-06)

"The inns are always the same 2 building layout slapped on the road." Each roadside inn now has an archetype (`InnStyles`, stream
1293; `InnComposer`, streams 1291/1292 unchanged), recognisable from the road and as a cluster of solids from afar:

- **Coaching inn**: the tavern 13-18 m back behind a yard open to the road, the stables and a barn or cart shed (`Shelter` 0/1) as
  wings with their open fronts to the yard; cart, well, table, hitching rail and a stump as mounting block, two lamps at the yard's mouth.
- **Wayside alehouse**: a large house (`InnComposer.InnHouses`: House_05, House_07, House_01_A, House_09_C) 0.6-3.6 m off the verge,
  parallel, skewed 12-22 degrees or gable-on; a meadow tree with two benches, a lean-to, barrels and a lamp by the door. The lightest.
- **Farm inn**: tavern or house 9-17 m back, skewed 15-30 degrees; a barn at right angles, haystacks, cart, well, a fenced paddock, a kitchen
  garden with three fruit trees behind (`Dressing.Trees`).
- **Forest lodge** (Forest regions only): woodshed with log piles, a woodcutter's corner (chopping stump, drying rack, piles, cart), a fire
  with tripod, bench and log seats.
- **Forge inn**: the inn close to the road and the smithy across it (the only inn holding both sides); the smith's Work anchor carries the
  far side, and `PeoplePlanner.PlanInn` gives it a smith who stays at the forge.
- **Waterside inn**: turned toward a lake or stream within 46 m of the road (`InnStyles.Water`, the field's `WaterHeight` and
  `StreamNetwork.Clearance`), a landing with a bench, tubs and reeds. Never occurs yet: the plan keeps lakes and crossings well away
  from inns (crossings at least ~140 m along the road). Making it real needs the stream plan to allow a crossing 50-90 m from an inn.
- **Refuge**: three layouts by pass ordinal (consecutive cols differ): windbreak arc; walled pen; a second lean-to and a cairn row.

Choice: inns are numbered within the interval between mandatory sites (start, col). An even-numbered inn takes its favourite (hash
plus region bonus); an odd one takes its favourite among those its two neighbours did not take, so no archetype repeats and any inn's
archetype follows from the seed and ordinals alone (only the start inn and the last inn before it, behind s = 0, can coincide). If a main
building finds no ground the coaching inn, then the alehouse, is built; `Dressing.InnStyle` says what stands. Exactly one building takes
the rest point (`InnComposer.IsRestBuilding`: the tavern, the refuge's hut or the inn's house). Measured in the offline harness on three
seeds over 30 km (47 inns): Coaching 13, Farm 9, ForgeInn 7, Alehouse 6, ForestLodge 6, Refuge 6; longest run 1; 12-44 objects (avg 22-26)
and 17-105k triangles (avg 58-63k); building corners within 48 m of the place's centre (coaching yards reach beyond the 35 m disc,
inside its 25 m blend; the rise check holds). Tests: `EndlessInnTests`.

## Village plans: places that grew (2026-10-06)

The user: "the houses are all in a single line along the road, that's not actually how most villages look." `VillageComposer` now
composes each hamlet and village from one of several plans (`VillagePlan`), picked by `VillageComposer.PlanFor(seed, journey, place)`:
a weighted pick by region (stream 1303), replaced by a second pick (1304) when it equals the plan of the settlement before it on the road
(the previous five are replayed, looking back 20-160 km by regions, so the rule holds without a chain from the start).
- **Green** (meadow and downs favour it): an irregular green (a main disc and a lobe) beside the road or, three times in ten, astride it;
  houses round its edge facing it at varied distances; the well, benches, one or two stalls on it; a lane from its back to the fields
  with the arch house astride it.
- **BackLane** (farmland): a lane leaves the road, runs 30-38 m behind the roadside plots (with a kink) and rejoins it 80-100 m on;
  roadside houses between, lane houses facing the lane, the smithy at the first junction, a well and benches where the lane widens,
  the arch house on a lane to the fields.
- **Church** (downs, passes): the landmark chapel's preset (`Church_01_A/B`, family `LandmarkChapel`, shape `BuildingTable.Church`, not in
  the name lookup so it keeps its landmark far drawing and has no smoke) on the highest of fifteen spots 40-56 m back, in a dry-stone
  walled yard with rows of gravestones and a yew; a bending lane up to its gate with houses along it and round a small green at its foot.
  `PeopleSource` takes the church as the cast's chapel (and as a wall the people walk round).
- **Street** (forest clearings and pass valleys only; elsewhere 4 %): the old two rows, now with staggered setbacks, gaps, gables to the
  road and a house or two up a short lane behind.
- Hamlets: **FarmYard** (3-4 farmsteads, each a house and a barn - open shelter, stables or hut - in an L round a shared yard up a lane),
  **LaneFork** (a lane that forks 10-14 m behind the road, farmsteads at the fork and the branch ends), **HamletGreen** (farmsteads
  round a small green); a house or two across the road.
Common rules: setbacks from the verge to 22 m (tight in the core, looser and deeper toward the ends), turns of +-7-12 degrees, one house
in five gable-on; plots behind each house (privy, a cottar's hut with its own door and path, or a fence; woodpile, kitchen garden, cart);
lanes are worn paths with 1.4 m kept clear either side (`Occupancy`, so the field mosaic sees them); every door gets a path to the nearest
lane, green rim (then across the green to its lane) or its garden gate, whichever the straight line reaches without crossing another
building; a few trees among the houses. A site that refuses part of its plan is topped up with roadside houses (villages 12 dwellings,
hamlets 6). The place's level disc (0.85 of the radius: 76 m village, 51 m hamlet) already holds 50-67 m of depth: no terrain change.

Measured (harness, three seeds, first 60 km): villages 18-25 buildings, 90-135 GameObjects, 300-385k triangles, 36-130 ms dressing;
hamlets 7-10 buildings, 35-73 objects; share of houses within 12 m of the road's edge 17-58 % per place, 25-42 % per seed (was 100 %);
depth 37-67 m; no overlaps; nearest prop 1.55 m beyond the road's edge; doors reaching the road by paths all but at most one per place;
no person nearer the road's edge than 1.82 m. Tests: `EndlessVillageTests` (plan variety, no repeats, street minority, setbacks, turns,
depth, overlaps, paths, the church and its yard).

## Stall hunt, landmark eye, pass specks (2026-10-06)
**Stalls of 13.6 s and 7.8 s** (`frameMsStopMax`, places tour 09:43 The Green Kettle 21,194 m; road-start tour 09:38 The Crooked Stag
1,643 m; both the `prominent` shot of an inn, seed 12345). Not reproduced offline and not a generator query: built cold in .NET for
seeds 12345, 42, 777 at 1,643 / 5,000 / 21,194 / 40,000 m, no main-thread query took more than 0.33 s (table below), and the
prominent shot's 902 `Height` samples took 4-28 ms. The evidence also rules out script code in Update/LateUpdate (marker means 0.2 /
0.3 ms over 31 frames, which a 13 s frame would have raised to about 440 ms) and the render thread (FrameTimingManager's CPU mean
9.6 ms); `SetShot` itself runs at the end of the previous capture's frame (the tour coroutine resumes inside the Game view's repaint),
outside the window. Left: an editor-side pause between player-loop frames, a garbage collection, or a stall in the last frames of the
window. The sidecar now says which: `stopWorstFrameMs`, `stopWorstFrameIndex` (1-based in the window), `stopGcCollections`,
`stopHeapMbChange`, `stopWorldBusy`, `setShotMs`.

What the hunt did find: every main-thread search built **regions' waypoints** lazily (places, features, landmarks and their
`BestView`, stream crossings, milestones: 20-150 ms per region in .NET, 3-4 times that in the editor). Cold in .NET, ms (warm ≤ 0.02
except the road window, 0.5-0.9):

| query (caller) | 12345 @1,643 | 12345 @21,194 | 42 (four distances) | 777 (four distances) |
|---|---|---|---|---|
| inn finder ±8 km (`UpdateInns`, HUD; on every 400 m and inn crossed) | 187 | 143 | 88-159 | 84-153 |
| village finder ±12 km (`JourneyMenu`, every 50 m) | 293 | 210 | 171-326 | 199-237 |
| waypoints ±1.5 km (`UpdateNature`, wildlife, people) | 86-167 | 41-153 | 32-121 | 56-156 |
| road window 3.2 km (`RebuildWindowNow` at a teleport; else a worker) | 93 | 78 | 36-121 | 73-176 |
| prominent target, 902 heights (review tool) | 4.5 | 28 | 4.5-20 | 9-15 |

Now `EndlessGenerator.WarmWaypoints` builds the regions `InnFinder.WarmReach` (16 km) each way round the traveller on a worker
(`EndlessWorld.WarmWaypointsAround`, again after every 1 km; 350-680 ms per 32 km cold on the worker), and the main-thread callers ask
`InnFinder.Ready` / `EndlessGenerator.WaypointsCached` first and keep their last answer until the range is cached (`UpdateInns`
unforced, `UpdateNature`, the Journey menu's villages). A teleport (`TravelRoutine`) waits, behind the black screen, for the warmer at
the target before `MoveTravellerTo`. After warming every search above does no region build (`RegionWaypointBuilds` unchanged): inn and
village finders 0.01 ms, road window 0.5-0.9 ms (3.3 once). Regions are built per region index from the seed alone, so the
waypoints do not depend on query order (tested). Not changed: `VillageComposer.PlanFor`'s look-back (only on tile workers), the people
and wildlife callers (inside the warmed ±1.5 km).

**Landmark eye** (P0010 Wick Windmill: `landmarkNearTrees` 25, eye not moved): the trees were found, but `ReviewSight.Clear` tested only
the line of sight widened by half the landmark's radius × distance / target distance (centimetres at 60 m), while the shot is a 7° (15°
wide) frame: a crown whose edge is 2 m beside the line 60 m out fills it. Now the test keeps the frame's **central third** clear
(`FrameSlopes`: tan(fov/2)·aspect/3 across and tan(fov/2)/3 up per metre along, with the fov `FrameLandmark` gives that eye and the
screen's aspect) along the first 80 m, crowns use the whole measured radius (leaning trunks carry the crown off the pivot), and raises
go to 8 m. None clear still writes `landmarkVisible: false`.

**Pass specks** (T0073): pollen was refused a new spawn in a pass, but `FX_Dust_Spots_01` (white motes over open ground) and the
forest motes still spawned there, pollen drifts alive on entering a pass only stopped emitting (their particles lived on), the pass
test read the announced region (20 m hysteresis, not refreshed by every teleport), and a pooled instance kept its particles. Now
pollen, dust and forest motes are not allowed in a pass (by `Generator.Region(world.S)`), any alive there are put away at once,
recycled instances are cleared, and the review tour calls `AmbientFxSpawner.ClearAll` after each teleport.
Tests: `EndlessStallTests` (no region build once warmed, cold work bounded by the range, order independence for three seeds, the sight
test with trees at 3 m and 60 m).

## Near tiles fast again (2026-10-06, generation time pass)

Today's features had taken a near tile from about 60 ms to 144-196 ms offline (Release .NET, seed 777, the 20 tiles of
`NearTiles_StayWithinTheirTimeBudget`), roughly half a second under Mono. Every change keeps the output **bit-identical**: a harness
mode hashes every field of 240 `TileBlueprint`s (seeds 12345, 42 and 777: 40 near tiles each along 24 km of road, covering streams,
fords and bridges, passes with rock skin, the patchwork, villages, inns and woods, plus 20 Mid and 20 Far tiles) before and after,
and the finite world's golden hashes are unchanged.

What took the time, and what changed:
- **The wood floor** (`Planting.PaintWoodFloor` / `ShadeDetails`): every texel within 16 m of the road asked `Planting.ForestAt` for the
  forest weight out at the wood's edge, three full height samples and a cover call each (about 10,100 per tile). The land-cover recipes all
  ramp their woods in from a lateral distance, so where the point lies inside both regions' ramps the weight is exactly 0:
  `TerrainField.NoWoodsAt` answers that from the road's x, heading and regions alone (the recipe constants are now named,
  `MeadowWoodsFrom` etc., and shared), and the full evaluation shares one row for its samples on the same z. About 1,700 full
  evaluations a tile remain (Forest regions, where the woods start 9 m out). `LandCover.SampleForest` interpolates only the forest weight.
- **Mathf in the per-texel loops**: calls into UnityEngine.CoreModule are not inlined, and `Clamp01`, `Clamp`, `Max`, `Min`, `Sqrt`,
  `FloorToInt` cost more than the arithmetic around them. `FastMathf` (World) carries Unity's own bodies, inlinable (checked bit for bit
  against Mathf over two million inputs with NaN, infinities and signed zeros), and replaces them in `TerrainShaper`, `LandCover`,
  `RoadDistanceField`, `DetailPlanner`, `HeightmapGenerator`, `TerrainField` and the wood shade; `GradientNoise.Sample` floors inline.
- **The painter's fields**: `FieldPatch.Contains` took a sine and a cosine per texel and field; `TerrainShaper.ComputeSplat` now takes
  them once per field and tests per row only the fields whose bounding circle reaches it (same order, same float operations).

| Near tile, seed 777 (ms) | before, warm | after, warm | before, cold | after, cold |
|---|---|---|---|---|
| whole tile (mean / max) | 147.9 / 256.5 | 68.3 / 114.7 | 219.0 / 589.7 | 155-164 / 460 |
| splat: wood floor | 44.0 | 8.4 | 59.5 | 15.5 |
| splat: painter | 23.6 | 12.9 | 31.7 | 24.4 |
| splat: road field | 5.0 | 2.0 | 5.0 | 2.1 |
| heights | 22.6 | 14.5 | 27.6 | 29.7 |
| cover | 14.3 | 7.0 | 17.8 | 17.5 |
| details: planner | 10.5 | 5.2 | 13.2 | 9.2 |
| details: shade | 8.4 | 2.3 | 10.9 | 3.8 |
| rock skin | 6.9 | 6.1 | 9.1 | 11.5 |
| dressings | 1.3 | 1.1 | 29.1 | 28.6 |
| Mid tile (mean) | 11.1 | 7.4 | | |
| Far tile (mean) | 8.0 | 6.4 | | |

"Warm" is the minimum of three rounds per tile after one round that builds the caches and JITs the code (what Mono, which compiles each
method once and fully, sees); "cold" is the scan as the time test runs it alone. The cold numbers are dominated by .NET's tiered JIT
(hot methods run unoptimised for the first tiles: `heights` is 30 ms cold and 14 ms warm) and by the first builds of the dressings (max
about 300 ms); run after the other tests, as in the full suite, the scan gives a 98 ms mean and the time test passes (about 75 ms).
The splat stage is now split into laps (`splat: road field`, `painter`, `wood floor`, `glades`, `fields`, `streams`, `paths`) and so is
"details". Test: `EndlessForestFloorTests.WoodFloor_FieldLookupsStayWithinTheirWorkBudget` counts the full forest evaluations
(`TerrainField.WoodSamples`, at most 3,000 a tile) instead of timing them; `NoWoodsAt_OnlyWhereTheCoverHasNoWoods` guards the shortcut.
Still slow: the painter's four noise lookups per texel (two of them two-octave fbm), the heights (relief noise per grid point), and a
Forest tile's wood floor (up to 33 ms warm: its woods start 9 m from the road, so the edge lookups stay).
