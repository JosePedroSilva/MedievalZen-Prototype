# Next session: the hill town after two waves (2026-10-09, evening)

**State when this was written:** compile check OK (three projects); offline harness 358 of 359 (the one failure, a dropped
`steps 2 top` viewpoint on seed 42 after the terraces changed, was being fixed by the viewpoint job); finite-world golden
hashes unchanged on CoreCLR and under the editor's Mono (`Tools/MonoCheck/run.ps1`); the Unity Test Runner's last full run
(15:49) was **937 passed, 0 failed** on the first wave. The second wave below has not run in Unity. Git: one prototype
commit, everything uncommitted.

## Second wave (evening): what the first street-level capture showed, and what was done
`Capture Places` from 18,000 m (`ReviewShots/tour_20261009-1803_places_*city-durncaster*`, 56 shots) after the first wave.
Right: the overview (0004) and the view from the main street (0002) have the shape (four crisp terraces, decks, pillars,
castle on its rock, dense lower town, wall ring); `road 150 m` (0011) a good silhouette; `castle court` (0049) a real
interior; ramps clean; 4.5-7 ms a frame at every city stop. Wrong, and what was done:
1. **The capture tool stood inside things** (castle gate inside a wall module, both stair flights, a tree crown on terrace 4,
   between two gables at the square, under the ramp deck at `ramp 1 top`) and the `road 1 km` stop never moved the traveller
   (a bug in `ReviewTourRun.StartsStop`), so it showed un-streamed far land with a hamlet in the sight line. **Job F:**
   `CityViewpoints` now has a clearance model of every piece (walls, pillars, stairs, houses through `BuildingTable`, set
   parts, rocks, trees, castle parts through a 106-prefab footprint table `CastleLayout`), the eye's ground is the planned
   surface (hill surface, deck top, the castle's own level), each view searches 1 m steps up to 6 m for a free spot or is
   dropped with a console line; `steps N foot` 3 m before the flight, `castle gate` 2 m before the bridge head, new
   `castle gate inside`; the two road views are real stops with the traveller there and a sight line kept clear of other
   settlements (shift up to 300 m, logged). Test `StreetLevelViewpointsStandFree` (113 eyes on 5 cities); per-eye log with
   `MZ_CITY_LOG=1`.
2. **The far castle was 1,500 slabs of 5 x 5 m (21k triangles): nearly twice a far tile's cap, so the city's far tile also
   dropped all its trees.** Job F: `FarCastle` proxies (97 merged blocks and roofs, 1,358 triangles) from `CastleLayout`;
   the far tiles round each city are now 6.9k-9.7k triangles. The hill itself was never missing from the far mesh. Test
   `CityStandsOnTheDistantTiles`. Unseen: the pop at the 420 m near/far swap; far retaining walls drawn in the town-wall style.
3. **The castle rock read as a smooth orange dune with one giant boulder** (0051, 0053, 0055). **Job G:** the mountain pass's
   faceted rock skin (`RockSkinField.CastleFace`) on the rock's steep faces, 97-98% coverage, about 1,500 triangles per city,
   coloured from the shared rock layer (`RockSkinMeshData.Input.RockLayer`), rim lift capped at 0.2 m on castle tiles, kept
   4-7 m clear of the town wall; rocks re-seated along the rock's outline (foot row x0.9-1.4 every 8 m, a sparser row half-way
   up, small ones between): no piece over 8 m within 10 m of the wall or the outside viewpoints, 12 m anywhere; Durncaster 56
   rocks, 44 on the castle rock. `CityRockTests` (9). The skin exists on near tiles only. Variants are now picked before the
   clearance checks, so later random choices in a city shifted (endless cities only).
4. **The terraces read as a fortress** (battlements on every terrace edge) and the upper terraces were half empty. **Job H:**
   a 0.44 m stone lip (the demo's `ParapetHeight`) built into the deck mesh on every terrace edge, outer face flush with the
   wall, collider to 1.0 m; battlements only on the town wall, gatehouses and castle; houses on every bench on both sides
   (backs 0.87 m off the next riser, fronts to the deck; 6.15 m out where a ramp runs at the riser's foot; a garden tree in
   any 6 m gap); `MaxBuildings` 175. Durncaster 109 -> 127 buildings (benches 31 -> 48; a bench is 15-18 m deep, room for
   one row only), other cities 126-142. Slots: `DoorSide` at every terrace house, 8 `Corner` slots at the landings. Decks
   about 12.3k triangles with the lips. No wood railings (they would need a palette family). `CityTerraceTests` +3.
   Unseen: the lip in the deck's cobble material on its vertical faces (the least sure part), the 1 m alley in front of
   ramps where only two presets fit, roofs rising 1-3 m above the next deck.

## In Unity, in this order
1. Focus the editor (recompile; no palette change this wave). Console: any "dropped" or "shifted" viewpoint lines.
2. `Tour Start Distance...` 18000, `Capture Places` (now honours the start distance). Read first: `road 1 km`, `road 150 m`,
   `castle flank left/right`, `castle back`, `castle gate`, `castle gate inside`, `castle court`, `steps 1/2 foot/top`,
   `terrace N edge`, and 0002's angle (`prominent`): do the terraces read as a town now?
3. Walk: a terrace edge (the lip's look and being stopped at it), a ramp front with its row of houses and the alley, the
   flights from the side, a set piece up close, a barrel in sunlight, the castle's far-to-near swap walking in from 600 m.
4. `Walk Benchmark` (x6, 5 km from 0 m) for the arrival stall: still owed (see the first wave's item 5 below).
5. Test Runner, Run All, at the end of the session (the offline harness is green; Unity-only tests are the final word).

## Open after this wave
- The battlement-vs-lip choice is the user's to confirm by eye; wood railings on alternate terraces need a palette family.
- The lower ward and the gate bridge (`LOWER_WARD` in `castle.py`) need a lower rock step on the gate side.
- Far retaining walls in the town-wall style; the near/far castle swap at 420 m.
- The 277 ms arrival frame (GC or font atlas): the next walk benchmark says.

# First wave (2026-10-09, afternoon): the repair of the 2026-10-06 walk's faults

Five jobs landed today and **none of it has run in Unity**: compile check OK (three projects), 339/339 offline tests, finite-world
golden hashes unchanged on CoreCLR and under the editor's Mono (`Tools/MonoCheck/run.ps1`, now in the repo). Unity was last open
on 2026-10-06 at 20:15; the Test Runner's last run is the 18:47 one with the eight since-fixed failures. Git: still one
prototype commit, everything uncommitted (commit only when the user asks).

## What landed (against the nine faults of the 2026-10-06 walk, listed under "Baseline" below)
- **A, the structure** (`CityHill` rewritten, `CityComposer` `Walls`/`Pillar`/`Flight`/`Decks`/`Rocks`, new `TerraceDecks` +
  `TerraceDeckView`, `TileBlueprint.TerraceDecks`, `SettlementKit.Surface`, `EndlessWorld.GroundHeightAbsolute` reads decks,
  families `CityPillar` (instanced) and `CityStairs` (GameObject, own mesh collider), palette **v24** with `terraceDeckMaterial`
  built from the cobblestone layer). Four 5 m terraces (plateau 20 m over the road), each riser one wall module standing on true
  low ground, the bank behind it under a cobbled deck with box colliders, the parapet on the module's top; a pillar at every run
  end, corner and fourth module; ramps along the terrace in front of each face (their outer wall pitched about 6 degrees), a 5 m
  landing where the face opens; the pack's stair flights; rocks only outside the town wall, 6 m plus half their reach from any
  street, way, door or house. `CityTerraceTests` (16 cases, 5 cities on 3 seeds): ground 1 m outside a wall within 0.09 m of
  the low level, deck edge within 0.031 m of the wall top. Plots: `scratchpad\city\out\city_<seed>_sections.png` (harness
  mode `citysections`). Durncaster: 279 modules, 58 pillars, 2 flights, 8 decks (4,754 triangles, 202 boxes), 110 buildings,
  about 1.45 M LOD0 triangles, dressing 541 ms.
- **B, the set pieces** (mining tools `seat.py`, `meshlib.py`, `parse_q.py`; `SetPart.Mount`/`Support`/`MountMin`...;
  `BuildingShape.FrontWall`; the vignette functions). Every part is mined with what held it (ground, another part, a wall, a part it
  hung from) by ray casts against the real meshes; 42 vignettes, 179 parts: 123 on the ground, 20 on parts, 21 wall-mounted, 7
  hung, 6 left out (tilted bellows and pig, a leaning ladder, an unsupported statue, posters without a board). Placement: no
  float over 0.15 m or burial over 0.1 m; wall pieces on the house's front wall or the wall face at 2.2 m or more, under the
  eaves; occupancy per part; lamps only at corners, doors and wall feet, carts only in yards, benches with their back to a face
  and 2 m clear in front. Cause found: wall-foot slots started 0.75 m inside the retaining wall (the barrels in the wall).
  Fill per city: door sides about 25 of 52, wall feet 13-16 of 21-24, corners 5-6 of 8, stalls 6/6, plaza 2/2, yard 0/1.
- **C, ground, colliders, eyes** (`CityPaving`, `PieceColliders`, `CityViewpoints`, `EndlessTreeRenderer` collider budget,
  `ReviewCapture`/`ReviewTourPlan`). Details zeroed where cobble weight is at least 0.3 and on the castle rock; the rock's top
  cobbled and its cliff rock-painted (shared layers, no extra pass); colliders by prefab name (boxes for walls, towers, pillars,
  battlements; the prefab's own meshes for archways, gates, doors, stairs, bridges; none for roofs, hoardings, corbels);
  `MaxBoxes` 160 or `CityMaxBoxes` 1,200 with a city in reach, plus up to 300 mesh colliders (about 570 boxes at the castle);
  `Capture Places` adds street-level stops in a city: `road 1 km`, `road 150 m`, `ramp N foot/top`, `steps N foot/top`,
  `terrace N edge`, `view square`, `castle gate`, `castle court`, `castle flank right/left`, `castle back` (sidecar fields
  `cityShot`, `cityLookAt`).
- **D, the castle** (`castle.py` selects by prefab name from every demo group, `colfill.py`, `emit.py` `CI`/`CP`). `DemoCastle`
  990 -> 1,573 pieces (floors, walkways, keep and wood pillars, beams, the exterior group's walls), 189k triangles; every outer
  column reaches the court (16 columns got 33 stacked modules; in the demo they stood on raised rock); everything uniform and
  yaw-only by substitution (stretched pillars drawn thicker, 2x1x2 floors as four tiles, a mirrored corner base left out). The
  demo's east "curtain" is a **lower ward 9-13.5 m below the court**: `LOWER_WARD = False` keeps it (and the gate bridge's far
  end, the north spur) out until the rock has a lower step on the gate side. `CastleModule` 50 -> 69 prefabs, `CastlePiece`
  32 -> 40 (appended). Audit in `scratchpad\city\castle_audit.md`.
- **E, the arrival stall** (`ArrivalWarmup` + `ArrivalWarmupRules`, `FrameCost.FirstUse`, scopes, walk JSON `firsts` and
  `gcCollections`, tour sidecars now write `stopWorstScopes` as JSON rows). Found: the ~12 ms `AmbientFxSpawner` frame at every
  place is each effect's 5 s prewarm run with world collision (leaves, petals and their season copies): they now start with
  collision off. Likely: the 20-29 ms frames 250 m before places were `SettlementAmbience.Build` making and prewarming every
  chimney smoke and fire at once (now 2 per frame). Unknown: the 277 ms at The Rusty Lantern (font atlas rebuild or a full GC;
  the warm-up removes the first, the new `gcCollections` field shows the second). Audio ruled out from the code (clips preload).
  The warm-up runs once per session in any hold (5 ms a frame, about a second).

Decisions made: the market/view square always keeps its well and `AnchorKind.Well` (the people's rounds use it), the plaza
vignette stands beside it; `LOWER_WARD` off; no palette bump beyond v24 (the build on editor load sees the whole catalog).

## In Unity, in this order
1. **Focus the editor**: it recompiles everything and rebuilds the palette to **v24**. In the console look for the palette's
   "Terrace decks" material line and "City stair flight ... yaw offset" (the flight's facing is measured from its mesh; if the
   line is missing the stairs may face the wrong way).
2. **Test Runner, Run All** (nine minutes): the final word on the eight old failures, the Mono goldens and today's new tests
   (`CityTerraceTests`, `CitySetPiecesTests`, `CityPavingTests`, `ArrivalWarmupTests`).
3. **Play `EndlessRoad`, seed 12345**: `Tour Start Distance...` 18000, then **`Capture Places`**: the city now gets the
   street-level stops above. Read the shots before walking.
4. **Walk Durncaster** (18,500 m): the first ramp's foot off the main street and a ramp's tilted outer wall; stand on a terrace
   edge (parapet about 1.4 m above the deck, deck flush with the wall top); at a wall's foot the ground level right up to the
   face; the landing opening onto the terrace above; both stair flights (facing up the hill, climbable); pillar caps at run
   ends; no cliffs in the streets; wall-foot vignettes (nothing in the wall, nothing floating, ivy and flags on the face); shop
   signs and bracket pots on house fronts above head height; a barrel in sunlight (still black?); the castle from inside the
   keep's halls (shot 36's spot: upper storeys on wood floors and pillars), the court cobbled, the cliff rock-painted, walls that
   stop the traveller while archways and doors let him through (F3: "boundary boxes N (+M mesh) of K pieces"), the parapet in
   front of the castle gate at court level, the left-out corner base about 19 m left of the castle's centre line.
5. **`Walk Benchmark`** (x6, 5 km from 0 m) for the arrival stall. Read in `Editor.log`: "Endless road arrival warm-up done:
   ... ms over ... frames (worst ... ms); effects a/b, people c/d, bodies e/f, clips asked to load g/h, glyph sets i" (or "cut
   short by the end of the hold" / "stopped after 600 frames"), "Endless road prop warm-up done", "Frame cost markers:". In the
   walk JSON `worstFrames`: `culprit` (may end in "(first)"), `firsts`, `gcCollections`, and whether `engineMarkers` holds
   `GC.Collect`, `Shader.CreateGPUProgram`, `Loading.*` or `event: Font.textureRebuilt`.
6. Still untried from the 2026-10-06 wave: road traffic (after its pool fix), the pace wheel, the journal (J), the horse,
   spring and summer.

## What to expect to be wrong (each job's own doubts)
- A: the flight's orientation and its top tread (measured from the FBX, unseen); the 6-degree pitched ramp walls and their
  battlements; the pillar cap assumed 4.93 m above its pivot; Unity's heightmap LOD (pixel error) may let a sliver of bank
  show beyond the near range; the deck material's tint and tiling against the painted cobbles; ramps take lower-terrace room.
- B: wall modules assumed centred, 5 m long, 0.5 m thick; house-front mounts often fail because the eaves are only about 3.5 m
  up (shop signs need 3.2 m), so fewer signs than the demo; porches: signs go on the body's front wall.
- C: physics cost of about 570 boxes plus mesh colliders near the castle; whether the archway prefabs' convex colliders leave
  the passage open; the paving tests were not shown to fail on the old code.
- D: the keep's doors and walls at +4.5 m stand on floor halls (the demo had raised rock there: a second rock level under the
  keep is the open suggestion for `CityHill`); the x = -20 retaining wall's battlements at court level in front of the gate;
  the support test is weaker than the tool's ray cast.
- E: the 277 ms cause; the prewarm cost is inferred from the prefabs, not measured; the real cost of the hold.

## Follow-ups not started
The lower ward and the gate bridge (a lower rock step on the gate side, then `LOWER_WARD = True` and re-emit); hanging wall
banners; the mill by water and the covered bridge; a city's own people, sounds, music moment and map symbol; crops by season,
winter, generic broadleaves' season; cart wheels and horse sounds for road users; a mounting animation; sound tweaks (postponed
by the user); `Docs/DIORAMAS.md` (do not build until asked); the Green Kettle seam from above.

## Baseline: the user's walk of 2026-10-06 (shots in `ReviewShots/city_walk_20261006/31.png` to `36.png`)
1. Terrace edges were slopes, not steps (33, 34): walls stood mid-bank on the 2 m heightmap. -> A.
2. Walls 10-15 m tall and blank (31, 32, 33): 8 m terraces of stacked modules. -> A (5 m terraces, pillars).
3. Giant cliff slabs inside the town (31, 32, 33). -> A (rocks only outside the wall).
4. Barrels floating and nearly black (31, 33): unsupported mined parts, slots inside the wall, ambient-only light. -> B.
5. A shop sign lying in the ground (35): wall-mounted parts placed at their mined height in front of the porch. -> B.
6. Meadow grass through the cobbles (31, 33, 34, 35) and an unpaved castle rock (36). -> C.
7. Wall runs ending open, parallel walls 1.5 m apart (32, 33). -> A.
8. The castle hollow and walk-through (36): 520 pieces dropped by the mining, 160-box collider cap. -> D, C.
9. Props unrelated to their surroundings (31, 32): lamp mid-street, planter alone, bench facing a rock. -> B.
Lesson kept: the plan and profile plots hid all of it; every fix now has a measuring test and the city has street-level
capture stops.

# Earlier that evening: a large wave is built and unseen (2026-10-06)

**Everything in the first section below is compiled and tested offline and has never run in Unity.** Six features landed at
once, so expect problems to be tangled: try them one at a time, in the order given.

## First run of the wave (19:00, tours `tour_20261006-1859` and `tour_20261006-1904_places`, Unity test run)
- **Unity's Test Runner, first full run in ten days: 886 of 895 passed.** The eight failures are dealt with, pending the
  user's "Rerun Failed":
  - Finite world against `GoldenMono` (3): not today's work and not `FastMathf`. The Mono goldens of 2026-10-01 were recorded
    with the standalone `mono.exe`, which is another build than the editor's embedded runtime, so they never matched the
    editor. Separately, the "tonal drifts" edit of 2026-10-02 in `TerrainShaper.ComputeSplat` changed the finite splat under
    Mono only (the editor's Mono computes floats in double and rounds where the JIT stores). Fixed: the finite world runs a
    frozen copy of that method (`ComputeFiniteSplatAsOf2026_10_02`), and `GoldenMono` is re-recorded from the earliest
    sources kept (2026-10-02) under the editor's own runtime. Caveat: nothing older than 2026-10-02 survives, so identity
    with the true originals of 10-01 under the editor's runtime cannot be shown.
  - Finite map drawing and the seed 42 waypoint hash: runtime differences (an inn's Z differs by 0.3 mm between runtimes
    and rounds to another centimetre); the tests now hold a value per runtime.
  - Two stale settings-version assertions and one exact float comparison: fixed.
  - 889 pure tests pass under an editor-equivalent Mono offline (`scratchpad\monoun.ps1`, described in `Docs/DESIGN.md`:
    **it lives in the session's scratch folder and should be moved into `Tools/`**).
- **Unity could not compile for an hour** (19:15-19:30): an agent's build left a compiler server holding
  `Library\ScriptAssemblies\MedievalZen.dll`. Rules added to `CLAUDE.md`; `check.ps1` no longer leaves a server.
- **The city works** (Durncaster, 18,470 m): crenellated wall with towers, gates, cobbled main street and square with stalls
  and people, lanes with fences, the keep in its yard. Frame time 5.0 ms on the street, 7.0 ms from above: far below the
  12 ms feared. See `..._0004_s18470m_city-durncaster_overview.png` and `..._0001_..._road.png`.
- **Bridge from the side is right again**: the Synty stonework and arch ring show, with a thin coping of stones on top.
- **Road traffic never appeared**: its pool threw on every start (`AddComponent<VillagerPose>` on a prefab that already has
  one returns null). Fixed after the tours; unseen.
- **The culprit instrument works**: the worst tour frames name `AmbientFxSpawner` (22.9 ms at Crow Abbey, 12.4 ms at
  Foxerley): the particle spawner, probably creating an effect for the first time. A candidate for the stalls near places.
  Worst apply slice 25.6 ms (`alphamap band`).
- Autumn (the default) looks as before; frame times unchanged (Forest about 12.2 ms).
- Still untried by the user: pace wheel, journal, horse, spring and summer, traffic after the fix.

## City, second direction (the user, 19:20, with a screenshot of the pack's demo scene)
"Notice how dense the scene is and how rich it is. That's what I want to achieve for the cities, that they are interesting
to visit; the current city is flat and everything is far away." Then: "first achieve the look of the city correctly and then
we can think in ways to optimize it ... the bottleneck is not on the asset density"; walkability thresholds to be discussed
after the user has walked it. Two jobs running when this was written: a **hill town** (terraces with retaining walls, a
castle on top, a ramp street and steps, houses shoulder to shoulder, stone underfoot; `CityComposer`) and **mining the demo
scene** for the artists' own arrangements (vignettes and a castle as data behind `CitySetPieces.cs`). Look first; no frame
budget for now, counts reported.

**Landed by 20:00, never seen in Unity** (palette **v23**: twelve mined families). Durncaster on seed 12345: the road at the
foot of a hill; three terraces of about 8 m held by stacked wall modules with a rail; three cobbled ramps at 11 % and two
flights of sloped cobbles at about 29 degrees; 104 buildings shoulder to shoulder, nearly all ground paved; the demo's
whole castle (65 x 68 m, 51 m tall, 990 pieces, almost all instanced) on its own rock beyond the old wall line, 24.7 m
above the road, foundations to -13.5 m, 26 cliff rocks round it, the town wall ending at its flanks; mined vignettes in the
slots (stalls 6/6, corners 8/8, door sides 36/50, wall feet 22/37; some cut to fit); flags on terrace edges and walls;
17 town trees. About 1.6 M triangles, 513 GameObjects, 1,676 instanced pieces; dressing about 390 ms (cached).
All 304 generation tests pass, the three-seed no-move test included (`PlanHeight` fix: landmark sight lines saw the hill).
Plots: `scratchpad\city\out\city_12345.png` and `_profile.png` (earlier versions kept beside them).
**To check first in Unity**: does the castle sit on its rock (parts floating or clipping; some back sections start 9-18 m
up in the data), can the ramps, the steps and the gate bridge be walked, does stretched terrain show behind retaining
walls or under the cliff, the silhouette from a kilometre away, the cut-down vignettes, flags' facing, and the cost.
Not done: stair pieces (the pack's flight is 45 degrees and 5 m; ours are 7.4-8.8 m at 29 degrees), hanging wall banners,
rock at the retaining walls' feet, a tree on the view square, the mill by water, the covered bridge, a city's own people
and sounds.

## The evening wave: what to try, in this order
Expect on the first editor focus: the palette rebuild to **v22** (the city's wall, battlement and tower families).
1. **Does it still run.** Play `EndlessRoad` on seed 12345 and walk two minutes. F3 should show new lines (road traffic,
   sound). Read the log for errors from `SeasonCycle`, `SeasonPainter`, `RoadTrafficManager`, `JournalKeeper`,
   `HorseStable`. Season is fixed autumn by default: the world must look exactly as before.
2. **Walking pace**: the mouse wheel while walking ("Stroll · 1.1 m/s" on the HUD). At 1.5 the walk is a light jog.
3. **Journal**: `J` after passing the first inn. Look, page breaks, readability.
4. **Road traffic**: walk in farmland or meadow by day; a meeting every three to six minutes. Log line "Road traffic: pool
   of 18 people, 4 horses, 4 carts made during the hold". Least sure: the rider's seat and legs, the cart driver's seat,
   the horse in the shafts (seat numbers are fields on the manager). Wheels do not turn; no hoof sound for them.
5. **The horse**: at an inn, "E — Mount the horse". Wheel = gait (walk 1.9, trot 3.6, canter 6.0 m/s). Least sure: the seat
   and legs (shared maths `AstrideMath`: one fix corrects road riders too), first person over the horse's neck, turning
   feel, inns with no horse (logged once), the trot (the walk clip played fast).
6. **The city**: Journey menu, The road, go to Durncaster (18,500 m; it took Hollestead's slot; Hartdenfield stays a
   village). Wall 81-100 m out with 17 towers, two gates (two enlarged towers and a span over the road: no pack gate fits
   a 5 m road), cobbled main street, square with church and tavern, back lanes, gardens, a keep in its yard, an orchard;
   67 buildings, about 1.03 M triangles, 81 GameObjects, 525 instanced pieces. Check the gates, whether the cobbles show,
   the closed fronts (0.5-1.7 m gaps), and the frame time at the square: run `Capture Places` from 18,000 m and a
   `Walk Benchmark` through it. It borrows the village's people, sounds and map symbol for now.
7. **Seasons**: Journey menu, Time page, "The year": Spring, then Summer (and Auto: five days a season, from autumn to
   spring). Look for: spring grass actually changing (it is recoloured through copies of the detail prefabs, the least
   sure mechanism), birches green, oaks not too light, the 320 m swap matching, the summer day (04:45 to 19:15, sun 64
   degrees) on the unchanged sky gradients, blossom under fruit trees (may read grey), the HUD day label. Not yet seasonal:
   crops and field colours, generic broadleaves, pines.
8. **Bridge from the side** at Oakbrook Bridge (5,267 m): coping stones on the Synty parapet, the arch ring visible again.
9. **The stall**: any walk benchmark now names the culprit of its worst frames (`culprit`, `topScopes`, `engineMarkers`).

Offline state when this was written: compile clean for Runtime, Editor and Tests with everything joined; each job's tests
pass in its own harness (generation 301 with the city and village tests, rendering 114, journal 11, traffic 11, riding 13,
season time 8); finite-world golden hashes unchanged. New files: `Scripts/Environment/Season.cs` (contract), `SeasonCycle`,
`SeasonFx`, `SeasonLook`, `Scripts/Endless/Runtime/SeasonPainter.cs`, `Scripts/Endless/Generation/CityComposer.cs`,
`Scripts/Traffic/*`, `Scripts/Travel/Journal*.cs`, `Scripts/Player/Horse*.cs`, `RiderPose.cs`, `WalkPace.cs`,
`Scripts/Travel/HorseStable.cs`, `Scripts/Review/FrameCost.cs`. Settings version 7, journey blob version 2.

Follow-ups already known: crops by season; winter; a city's own people, sounds, music moment and map symbol; cart wheels
and horse sounds for road users; a climbing animation for mounting; the generic broadleaves' season; `Docs/DIORAMAS.md`.
**Git: still one prototype commit; all of this is uncommitted.**

# Earlier the same day: state after the 17:17 tour and the 17:21 walk benchmark

Read this before planning; replace it after the next review. Two root causes found in the 13:54 / 13:57 review are now
confirmed fixed in Unity, a first sound pass is in and waiting for the user's ears, and the walk benchmark still owes a run.

## Tours of 15:35 (road start) and 15:38 (places, from 19,237 m): both fixes confirmed
Clean runs with the sound systems on, palette v20, safety level 0, no washed-out shot.
- **Streams are right for the first time.** Debug views at Oakbrook Bridge: banks show smooth mud / drape / path data, no
  magenta, cyan or green-on-water. The side shot shows teal water with streaks and a foam line under the arch, and grass banks
  with broken earth and a mud toe. Still to judge by the user: the banks' earth patches read a little like camouflage, and
  whether the water colour suits them. No ford was in these tours (Ottersike Ford is at 15,788 m).
- **Rapeseed is yellow.** The field behind The Muddy Fox (19,396 m) flowers yellow in the overview and the side shot. It looks
  much like the crop field beside it (same plant, amber heads): fine, but two crops read as one.
- **Bridge from the road: unchanged.** Road-coloured deck, one parapet block on the right, nothing on the left, the stream
  not visible until you are on it.
- **Frame times unchanged** with sound on: Forest 11.9 / 12.4 ms mean, slowest stop 16.8 ms (Hollow Heron overview), worst
  single frame 17 ms, worst apply slice 13.5 ms (alphamap band); the 18.6 ms shelter slice did not recur.
- **Walk benchmark crashed at its finish line** (its first run ever): `FinishWalk` cleared the run and then `CloseHold` read
  it, so the travel speed stayed at x6 and no report was written. Fixed (`CloseHold` takes the run).
- **First walk benchmark that finished** (`walk_20261006-1552_s12345_22475-24474m_x6.json`): 2 km of Highland pass at x6
  (8.4 m/s), 64,867 frames in 238.8 s. Frame mean 3.7 ms, p95 4.3, p99 5.2, max 94.5; 5 frames over 16.7 ms (2.5 per km),
  1 over 33 ms; no streaming hold, no apply slice in any of the 25 worst frames. So tile streaming and the rock skin are
  hitch-free in a pass at six times walking speed. The one 94.5 ms frame is the arrival at Cloudrest (4.9 m before the inn,
  "You arrive at ..."): something in the arrival costs a frame, cause not found. A 30 ms frame 84 m past Larks Bridge has no
  slice either.
- **Second walk benchmark** (`walk_20261006-1602_s12345_0-5000m_x6.json`): 5 km from the road start at x6 through Meadow
  (1.9 km, mean 7.7 ms), the first Forest (2.9 km, mean 12.4 ms, p95 13.9) and the pass's start, past three inns. 61,292
  frames: mean 9.7 ms, p95 13.6, p99 14.3, max 29.2; 25 frames over 16.7 ms (5 per km), 5 over 25 ms, none over 33 ms; no
  hold, no apply slice in the worst frames, and no arrival hitch this time. **Streaming is hitch-free at six times walking
  speed.** The Forest is still GPU-bound at about 12 ms. A village has not been walked through in a benchmark yet.
- **Sound** (user, after the session): "fine for now, some tweaks are needed but not at this stage".
- Also fixed by hand: the stream mesh sets both sub-meshes at once (Unity warned on every pooled reuse).

## Landed after the 17:17 tour, compiled and tested offline, not yet run in Unity
- **Stall near places: instrumented, one fix.** `Scripts/Review/FrameCost.cs` times about 70 named scopes a frame (every
  manager's update, the world's phases, arrival and leg-start handlers) and reads engine markers (GC, shader program
  creation, loading, Instantiate, the editor's own loop, font atlas rebuilds). **Read in the next walk benchmark's JSON**:
  `frameCostMarkers` (which markers this editor has), then per worst frame `culprit`, `topScopes`, `engineMarkers`; the CSV
  has a `culprit` column; tour sidecars have `stopWorstCulprit`. A scope as culprit = script work; a cause marker = engine
  or editor work; only phase markers large = a native or graphics wait. Clue from the 17:21 CSV: the four frames after the
  277 ms frame have no CPU or GPU timing, which points to the editor loop or a graphics wait, not script code.
  Fixed: a place's whole cast of villagers (up to 12) was created in one frame; now two a frame.
  Not changed, now measured: `SettlementAmbience.Build` creates smoke, fire and lights unsliced at 250 m; `PickDestination`
  can rebuild the road window synchronously at a leg's start.
- **Bridge from the side**: no wall body any more. A coping of 28 separate stones a side sits on the Synty parapet (whose
  outer face is the bridge's own side), a lining of blocks covers its road face, posts and approach walls are coursed. The
  Synty side, bands and arch ring are uncovered. Parapet top 0.96-1.26 m above the deck; about 1,370 triangles a bridge.
  Check the side shot at Oakbrook Bridge and that the lower parapet at the ends still reads from the road.
- **Walking pace**: mouse wheel (RB / LB on a gamepad) steps the walk from 0.5 to 1.5 in tenths, for walking and auto-travel;
  sprint unchanged; ignored while an overlay is open or seated, and by the benchmark. HUD notice "Stroll · 1.1 m/s"; row
  "Walking pace" on the Journey menu's View page; settings version 6. At 1.5 manual walking is about three quarters run
  clip (a light jog): a faster walk ring in the locomotion controller would keep it a walk.
- **Running when this was written**: journal, travellers and carts (`Scripts/Traffic/`), a walled city (takes a village's
  slot; `WaypointKind.City`), a survey for seasons. Queued: riding a horse (after the traffic job, to share its rider pose).
  Idea discussed, not started, written up in `Docs/DIORAMAS.md`: user-built dioramas as placeable points of interest and
  as biome references. Seasons started the same evening (contract `Scripts/Environment/Season.cs`: spring, summer, autumn;
  autumn is the identity and the default; winter later, the user agreed).

## Tour of 17:17 (road start, 90 shots) and walk benchmark of 17:21 (0-5 km): the rest of the visual batch
- **Bridge from the road: works.** Oakbrook Bridge now shows splayed approach walls, a post at each end and a parapet each
  side (`..._0073_..._bridge_road.png`, and best in `..._0074_..._bridge_prominent.png`). **From the side it got worse**: the
  new outer face is a tall blank grey band that hides the Synty arch ring and reads as a concrete slab
  (`..._0075_..._bridge_side.png`). Wanted: keep the Synty stonework visible below deck level (no skirt below the deck),
  coping stones and joints with tone per stone, perhaps a string course.
- **Forest floor under closed canopy: works.** 2,400 m and 4,200 m side shots: brown leaf litter with texture, sunlit patches
  warm ochre instead of pale sand. Slightly monotone olive in full sun; the lime tufts are loud against it.
- **Distant trees through the long lens (St Aldo's):** lobes now show facets and a lighter top; still dark slabs. As above.
- **No cost from the litter layer:** walk 0-5 km mean 9.6 ms, p95 13.5, p99 14.3 (9.7 / 13.6 / 14.3 before); Forest mean
  12.3 ms. Tour means unchanged. Worst apply slice in the tour 22.7 ms (`alphamap band`), but no slice shows in the walk's
  worst frames.
- **A stall near places, third sighting:** one 277 ms frame 17 m before The Rusty Lantern in the walk (not streaming: apply
  0.02 ms, no slice). Earlier the same day: 94 ms at Cloudrest (walk), 352 ms at Lang Windmill (tour stop, no GC, world
  idle). Intermittent: the same three inns gave no stall in the 16:02 walk. Suspects, none checked: arrival and the next
  leg's route planning, a place's people or animals being created, a sound clip's first use. **Needs a culprit per frame**
  in the walk benchmark (time each manager's update and the travel handlers, and report the largest for the worst frames).

## Places tour of 16:52 (from 5,007 m, 20 stops) and walk benchmark of 17:01: first look at the visual batch
The tour from the road start was stopped after one shot (Esc), so **the bridge walls and the under-canopy stops at 2,400 /
3,600 / 4,200 m have not been seen**. Palette v21 built as expected (leaf litter and matt mud lines, all resolved).
- **Closes: work.** Hollestead and Hartdenfield overviews show bright paddocks with cattle and sheep, gardens in pale and
  green stripes with a scarecrow, orchards of small light-green trees, hay plots with a haystack; nothing reads as a pond.
  The paddock patches have soft ragged edges from above, and the garden's soil rows are rather pale. Frame time at the
  villages unchanged (road 5.1-5.7 ms, overview 7.1-7.9 ms).
- **Forest floor: leaf litter shows.** The Green Kettle and Crow Abbey side shots (wood edge) have an olive-brown floor with
  a visible leaf texture where it was flat tan. The lime grass tufts now look very bright against it.
- **Distant trees: only a little better.** From the raised views the trees beyond 320 m are still darker, greyer shapes and
  the band is still there; through the long lens (Wick Windmill) they are still cut-outs in the haze, with faint lighter
  upper faces. Fine at eye level. What is left is haze plus the lack of leaf detail, not lighting: the options are a longer
  impostor distance (GPU cost in the Forest) or leaving it.
- **Seam at The Green Kettle: still there** from the raised camera (a straight edge where the bright verge stops). Not the
  paint (continuous across the border, per the litter job) and not the base map (the endless terrains keep Unity's 1,000 m
  default). Unexplained; probably the grass details' patch culling seen from 147 m up. Not visible from the road.
- **Walk benchmark** `walk_20261006-1701_s12345_5007-10007m_x6.json` (pass and downs, over Bellwash Bridge with its new
  walls): mean 4.4 ms, p95 6.3, p99 7.2, max 24.9; 5 frames over 16.7 ms, none over 25 ms; no hold. The bridge walls slice
  costs no visible frame.
- **To watch:** the worst apply slice in the tour was 23.4 ms and 17.3 ms (`alphamap band`; 13.5 ms before the fifteenth
  splat channel), and the Forest has not been benchmarked since; one 352 ms frame at Lang Windmill's side shot (no GC, world
  idle, worst slice 4.3 ms), unexplained.

## Visual batch of the evening (four jobs, joined, palette v21): what each should show
Offline: compile clean; the generation, rendering and new tests pass together; golden hashes unchanged.
Expect on the next editor focus: the palette rebuild to **v21** with the lines "14 leaf litter: created ... LeafLitter.terrainlayer
... colour (99, 79, 42)", "all resolved" and "Endless road mud layer: created ... MudMatt.terrainlayer".
1. **Bridge from the road** (`BridgeWalls`): a parapet each side that wraps the Synty one (top 1.14-1.18 m above the deck, clear
   width 5.6 m), a capped post at each of the four ends, four approach walls splayed 20 degrees; 432 triangles and 30 collider
   boxes per bridge. The bridge is 17 % wider (`ScaleX` 1.31) and now lies along the road's chord. Check: Oakbrook Bridge road
   shot (5,267 m; posts 0.37 m above the eye from 40 m), the joints with the Synty parapet, walking the deck, the three stone
   tones, any hitch from the "bridge walls" slice.
2. **Forest floor** (leaf litter, endless channel 14, in the cobblestone's slot so forest tiles keep two passes): under a crown
   81 % litter, sRGB about (99, 83, 46), 47 % of the meadow's luminance; between crowns 29 %. Check: 2,400 / 3,600 / 4,200 m
   shots (texture at 2 m tiles, tone in shade), no artefact where a tile's layer set changes, and whether the straight seam
   at The Green Kettle (tile border z = 19,968) is gone; the litter job could not reproduce it from the paint.
3. **Glossy mud** (found by the closes job, fixed by hand): the Synty mud layer is metallic 0.3 / smoothness 0.5 and mirrored
   the sky, which is why closes read as ponds and the sunlit wood floor as pale sand. The endless road now paints mud through
   a matt copy. Check: lake shores and stream sides still read as mud, not as dry earth.
4. **Village closes** (`CloseContents`): no close uses mud; paddocks bright green with a worn band, gardens in 2 m rows with
   cabbages, pumpkins and some scarecrows, orchards with fruit trees 7-8 m apart, hay plots with haystacks. Foxerley: 39
   closes, 31 fruit trees, 56 props; Hollestead: 138 closes, 117 fruit trees, 83 props. Check: both overviews and side shots,
   low sun, and the frame time at Hollestead (most orchard trees): a `Capture Places` from 18,000 m and a walk benchmark
   through the village.
5. **Distant trees**: leaf crowns have a lighter top and darker underside baked in and a foliage light that wraps round to
   the shaded side while the sun is high; a +/-4 % tone per tree; triangles unchanged. Pines untouched. Check: St Aldo's
   prominent (noon and low), Wick Windmill prominent, the Hollestead and Muddy Fox overviews: no band at 320 m, nothing
   lighter than the real crowns, no tone jump at the swap. Colours are model predictions.

## Fixed in the 13:54 / 13:57 review (now confirmed, kept for the record)
1. **Stream glow, grey banks, flat water: one cause.** `StreamVertex` declared `uv` before `color`; Unity lays a vertex buffer
   out in `VertexAttribute` enum order (position, normal, tangent, colour, uv) whatever order the descriptors are listed in. The
   shaders therefore read the uv bytes as colour and the colour bytes as `uv.y` (infinite wherever alpha was 255: all the water,
   the banks' drape). Found with the debug views at Oakbrook Bridge (`debug_20261006-1354_*_view1/2.png`: bank drape magenta =
   uv non-finite, water green = ripple normal non-finite). Fixed: field order in `StreamRibbonMeshData.cs`, `StreamLayout` in
   `EndlessTileBuilder.cs`, test `StreamVertexFieldsAreInUnitysAttributeOrder`, rule in `CLAUDE.md`.
   **Consequence:** every stream tuning since 2026-10-02 (water colours, streaks, foam, bank mud / wet / path / drape, the lime
   line) was judged against garbage inputs. Expect the look to change; judge it fresh, then retune. The NaN guards and the
   camera's Stop NaNs stay as a safety net. In the debug views the banks should now show red / green / blue data (mud, drape,
   path) with no magenta, cyan or green-on-water.
2. **White fields: the rapeseed material's trunk slot was empty.** The generator confirms the white field beside The Muddy Fox
   (19,396 m) is the rapeseed crop. The rapeseed clumps are the crop field clumps' geometry (same triangle counts and UVs), and
   the Synty foliage shader draws them from `_Trunk_Texture`. Our material was a copy of the flower field material (other texture
   layout, trunk slot empty) so every clump was an opaque white card; the two earlier diagnoses (pale petals, alpha test) were
   wrong. Fixed in palette **v20**: the material is now a copy of `CropField_01.mat` with a first-party copy of its texture in
   both slots, the amber heads recoloured to `GroundLook.RapeseedFlower`. Expect the log line "Rapeseed material: … leaf and
   trunk slots both set" on the next editor focus. The finite world's rapeseed fields change with it (they were white too).

Offline after both fixes: compile clean (Runtime, Editor, Tests); 290 generation tests, 99 rendering tests pass; golden hashes
unchanged; `StreamBank` and `StreamWater` pass DXC.

## Sound: first version built the same afternoon, not yet heard
The user's direction: "like Death Stranding, where the music stops and there are long periods with just ambience"; music
frequency Rare by default (their choice). Built by three jobs and joined in `Scripts/Sound/SoundMix.cs`; rules in `CLAUDE.md`.
Offline: compile clean; 16 ambience, 11 music, 16 footstep and 24 Journey settings tests pass. Everything is level-matched by
measurement only: nobody has listened.
- **Ambience** (`Ambience*`, 42 clips): beds by region, tree cover, sun and weather (dawn chorus, night bed, wind by exposure,
  synthesised rain), positioned streams, lakes, farm animals, market and inn murmur, windmill, church creak, standing stones;
  spot birds. Simulated walk 0–8 km: never silent, no stuck bed. A Forest region counts as at least 0.45 cover.
- **Music** (`Music*`, 30 tracks x Main / Intensity 1 / Intensity 2 / two cuts): simulated 3 h at 1.4 m/s on seed 12345, Rare:
  6 pieces, first at 7:32, mean gap 25.8 min, music 10.3 % of the time, no early end, none planned past a place. Sometimes
  23.6 %, Often 41 %. `F7` starts or ends a piece, `F8` steps through the tracks; the title shows lower left.
- **Footsteps** (`Footstep*`): synthesised placeholder (road, grass, forest floor, rock, ford) timed from the foot bones with a
  stride fallback (1.9 steps/s walking, 2.8 sprinting). The packs hold no human footsteps, open-air rain or fire.
- **Journey menu**: fifth page "Sound" (Master, Ambience, Music, Footsteps, music frequency, song titles); the five tabs are now
  20 % wide each. Settings version 5.
- **Imports**: `Assets/Sounds` is 6 GB, 602 WAV files, of which 192 are used; untracked in git (ignore or LFS before any commit).

**Listen for, in this order** (F3 shows the three sound lines):
1. Overall balance at default sliders: ambience present but quiet, footsteps under it, a piece of music just above it.
2. Footsteps: the F3 line must say "bones"; if it says "stride" or "(no foot bones)" the foot detection failed. Do the steps
   land with the feet, in both views? Does the road sound like packed dirt?
3. Risky clips: `Busy Market` and `Populated Outdoor Restaurant` (modern sounds?), `Fairy Forest Calm Loop A` (too musical?),
   `Windy Church Creaking` at every tower and chapel, `Woods Night` everywhere at night, the gusty pass winds.
4. Music with `F7` / `F8`: the loop seam (worst candidates: Direct Sunlight, Meteor Darts; Heartbeat and Ether dip before the
   seam), the mid-loop change of intensity, and which tracks do not fit. Strong-pulse tracks already weighted low: Epiphone, Air
   Flows, Cybernetic, Angelic Dunes, Light Flourish. Switch a track off in `Resources/MusicLibrary` (`enabled`).
5. Water: a stream should grow as you approach and not jump from ear to ear in a ford.
Open: levels above volume 1 use `SoundBoost` (unheard); the rain is a placeholder; finite world has footsteps only.

## What the 13:54 (road start) and 13:57 (places) tours showed
Both ran clean: no washed-out shot, no log error, palette v19, safety level 0 throughout.
- **Villages and hamlets: good.** Hollestead reads as a clustered village with a market, people and smoke; Foxerley as a
  scattered hamlet with garden walls, tracks and closes. Inns differ (Muddy Fox: turret house, bench under a tree; Green
  Kettle: lodge, open shelter, shed, log pile; Cloudrest: thatched refuge under the cliff). Name boards read from the road.
- **Farmland: good apart from the white rapeseed.** Gold and yellow fields, hedges, fences, horses, flowers in the meadow.
  The closes behind Foxerley are flat dark slate-green rectangles from above (they read as ponds); from the ground they pass.
- **Pass: good.** Grey faceted rock, scree bands, pines, Crow Keep found. No specks seen.
- **Wood: better.** Litter follows the crowns, grass in the gaps (4,200 m side). Under closed canopy the sunlit floor is a
  pale flat sand-grey (2,400 m): the leaf-litter layer is still the open offer.
- **Bridge:** from the side it reads as a stone arch with parapets. From the road it is still a flat slab with one block.
- **Impostors:** fine at eye level and normal field of view. In the 5° telephoto shots (St Aldo's, Wick Windmill) and from the
  overviews they are flat dark grey-green hexagons with no light on top, greyer than the real trees: a band is visible at
  320 m from above. They share the far tiles' tree material by design. Low priority; a lit top face and a greener tone would do.
- **Ground seam** at The Green Kettle (21,194 m) from above: a straight edge where the bright verge grass stops (tile border,
  wood-edge tile next to an open one). Not visible from the road.
- **Tool:** the side shot at 2,400 m sits inside a tree crown (first Forest); the side eye needs the same sight test as the
  landmark eye. Orange leaves drift high over open meadow at The Muddy Fox (prominent shot).

### Frame times (`frameMsStop`, RX 6800 XT, 1718x1239 game view)
| Tour | Region | Mean ms | Max ms | Trees mean / max |
|---|---|---|---|---|
| 13:54 | Meadow | 8.7 | 14.8 | 2.2 M / 5.0 M |
| 13:54 | Forest | 11.7 | 14.3 | 5.0 M / 6.2 M |
| 13:54 | Highland pass | 5.2 | 9.7 | 0.6 M / 2.8 M |
| 13:57 | Farmland | 7.7 | 13.5 | 2.2 M / 4.1 M |
| 13:57 | Forest | 12.2 | 16.5 | 4.7 M / 6.6 M |
| 13:57 | Highland pass | 4.5 | 6.0 | 0.4 M / 1.0 M |
| 13:57 | Downs | 4.9 | 7.1 | 0.7 M / 1.1 M |
| 13:57 | Lake country | 6.6 | 7.9 | 1.0 M / 1.4 M |
| 13:57 | Meadow | 6.9 | 8.6 | 1.4 M / 1.9 M |

Slowest stop: The Hollow Heron overview (35,014 m, Forest) 16.5 ms, GPU-bound. **No stall**: the worst single frame in any
measuring window was 30.8 ms, no GC collection in any window, `setShotMs` at most 7.8 ms. The 8 s and 14 s stalls of the
earlier tours did not recur and remain unexplained. Worst apply slices: 18.6 ms (`show SM_Bld_Preset_Shelter_02_Optimized`),
12–17 ms (`show props`): the building pre-warm does not cover the shelter preset.

## Also landed since the last tour (offline only)
- **Near-tile generation time**: 148 ms → 68 ms warm, output bit-identical over 240 tiles (`FastMathf`, `TerrainField.NoWoodsAt`,
  `LandCover.SampleForest`); `NearTiles_StayWithinTheirTimeBudget` passes again.

## First thing
1. Click into Unity outside Play mode. Expect once, without warnings: the palette rebuild to **v20** with the rapeseed line above.
2. Play `EndlessRoad` on seed 12345, then `Review/Capture Tour From Road Start`, `Review/Capture Places` and
   `Review/Walk Benchmark` (defaults: 2 km at 6×). The benchmark has never been run: no `walk_*.json` exists. It is the measure
   for loading hitches and for the rock skin slice (start it at 4,700 m for the pass).
3. Read first: `debug_*_view1/2.png` at Oakbrook Bridge, the bridge side shot (banks and water with correct data), The Muddy
   Fox overview and side (19,396 m: the field behind the inn must be yellow), then the summaries.

## Open, in priority order
1. **Stream look**: now sound; the user to judge the bank patches and water colour (values in `WorldPaletteMenu`), and a ford.
2. **The stall near places** (94 to 352 ms, three sightings): instrument the walk benchmark to name the culprit, then fix.
   **Bridge from the side**: the new walls hide the stonework (see above).
3. **Wood floor in an inn's clearing** is wood-floor paint (the 16 m roadside reach borrows the wood's weight): dull olive
   round The Green Kettle. Keeping it out needs the place's sites in `Planting.WoodShade`.
4. **Sound tweaks**, when the user asks for them; `treeImpostorMaxPixels` 3 vs 0 undecided; the pine impostor is lighter
   than the one real pine sampled (not a clean comparison).
5. **A 94 ms frame on arriving at Cloudrest** in the first walk benchmark; it did not recur at three other inns.
6. **Pre-warm the shelter preset** (18.6 ms slice); **rock skin cost** (22–34 ms worker, up to 75k vertices in one slice,
   sliver triangles on the steepest faces): wait for the walk benchmark.
7. **Review tool**: sight test for the side shot's eye.
8. **Ground seam** at wood-edge tile borders (Green Kettle).

## Known shortfalls and open decisions (do not rediscover)
- **Waterside inn never occurs**: crossings and lakes are kept about 140 m from inns. Needs `StreamPlan` to allow a crossing
  50–90 m from an inn.
- **Green village**: the side of the road opposite the green is still a row of houses. Church door side is assumed (+Z).
- **Rocks and reeds on a bank** can sit up to 16 cm low after the bank hummocks. Ford walking steps from the bank collider
  into water: untested.
- **Rock skin**: no cliff-mesh cladding, no vegetation on ledges; the 25k triangle cap thins by hash and can leave small gaps.
- **Field boundaries**: a fence line can be missing between two cut cells.
- **Village name boards, a sign over the tavern door, headland tracks**: not done.
- **More settlements** (hamlets in meadow and downs, a fishing hamlet, a second one in long farmland): offered, not asked for.
- **`cpuPresentWaitMs`** reads 0 on this machine.
- **A git commit**: one commit exists from the prototype; everything since is uncommitted.
