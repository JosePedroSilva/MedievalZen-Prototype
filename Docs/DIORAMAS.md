# Dioramas: hand-built scenes for the generated world

**Status: an idea, discussed on 2026-10-06, not started and not yet approved to build.** The user raised it, we weighed it in
conversation, and the user asked for it to be written down so it is not forgotten. Nothing in the code refers to it.
What follows separates what the user proposed, what Claude recommended, and what is still open.

## The idea
The user builds small scenes by hand in the Unity editor with the packs' assets (a wayside shrine, a camp, a broken cart, a
hermit's hut), and the generator uses them. Two uses, which should be judged separately:

1. **Placeable points of interest.** A diorama is processed once and then placed along the endless road by rules, like any
   other prop.
2. **Biome references.** A small hand-made patch shows how a biome is meant to look, and the scatter rules are tuned to
   match it.

## Why it is worth considering
- Composing a small scene is what the generation rules do worst, and what Claude cannot see: every look is judged through
  the user's tours. A scene the user builds is a guaranteed good moment.
- On an endless road, anything that recurs is noticed quickly (the user's remark about inns and villages on 2026-10-06).
  The road needs more kinds of place worth walking to than the handful of landmarks the code makes today.
- For biomes, it replaces taste words ("feels bland") with something that can be measured in the game's own assets and
  lighting, which reference screenshots from other games could not give.

## The pipeline as discussed
What the user proposed:
- **A dedicated pipeline**: a new diorama is processed once, then used by the code as a prefab.
- **Claude makes the first ones** from what already exists, for the biomes and the points of interest the game has, so the
  user tweaks rather than starts from nothing.
- **Variations**: when a new point of interest is processed, Claude generates about ten variations (rotations, added
  elements) and the user approves or tweaks them.

What Claude recommended, with the changes to the above:
1. **Export what exists.**
   - Points of interest: export what the code generates today (a mill with its dressing, a kirk, the standing stones) as
     editable prefabs; the user's edited version becomes the source. Only small standalone places: inns and villages are
     laid out along the road's curve and would lose that if frozen.
   - Biomes: export a patch of generated land (the Forest first) as a scene the user reworks.
2. **Process once.** A menu turns a diorama into data the generator can read, because the generator is pure code and
   cannot open a prefab while it runs: footprint and radius, the slope it tolerates, which pieces are drawn instanced
   (trees, bushes, fences) and which stay GameObjects, seats, lights, anchors for people, a sound kind, how it is named.
   The menu also validates (budget of triangles and objects, no raw Polyperfect animals, only pack props). Rebuilt
   automatically when stale, as the palettes are.
3. **Place by rules**: which regions, how far from the road, facing it or not, how rare, and never the same diorama again
   within a set distance. Each prop is seated on the real ground, since a diorama is built on the flat.
4. **Variation without review**: rotation, mirroring and swapping a prop for a sibling happen at placement and need no
   approval.
5. **Three or four authored states instead of ten variations** (for example tidy, weathered, overgrown, with someone
   resting there), proposed by Claude and approved or tweaked by the user. Variations that add elements are composition
   work, which is exactly what Claude is blind to: ten of them would mean ten reviews with a low hit rate.
6. **A thumbnail sheet**: a menu that renders every diorama and state to one image. The user approves at a glance, and
   Claude can read images, so it gives both a look at this content without a tour.
7. **Biome reference by difference**: the user reworks the exported patch, and Claude measures what changed (plants per
   square metre by kind, the mix, sizes, spacing, canopy cover, ground shares), tunes the rules towards it at a stated
   fraction of its density, and keeps the patch as a yardstick for later changes.

## Costs and risks
- **Repetition** is the main risk of placed scenes: it needs a few dozen dioramas, the variation above and the spacing rule.
- **Terrain fit**: footprints, slope limits and per-prop seating; nothing may land on the road, its verge, a lane or a
  building.
- **A second way of making the same kind of thing.** It must be decided per kind who owns it: dioramas for small places,
  code for settlements.
- **Every system adds a requirement on dioramas**: seasons (recolouring), night (lamps), people (anchors), sound, the
  journal (names), far tiles (a silhouette for the large ones). The descriptor has to stay small, with defaults.
- **Performance**: a hand-dense biome patch can be several times what the world can hold for kilometres at about 12 ms a
  frame; the match is to a fraction, and that has to be accepted. Placed scenes must load through the tile builder's
  slices and keep trees and plants in the instanced renderer.
- **It makes content the user's job.** Fine if enjoyed; each diorama is minutes of work once the pipeline exists.
- **Building the pipeline** is roughly one or two delegated jobs (exporter and thumbnail sheet; processing and placement).

## Open questions
- Which kinds of place become dioramas and which stay generated.
- Whether the user accepts three or four states in place of ten variations, and the thumbnail sheet as the way to approve.
- Where dioramas live (suggested: `Assets/_Project/Dioramas/`) and the conventions for building one (pivot on the ground,
  one axis towards the road, a maximum radius, no terrain of its own).
- Ground paint inside a diorama (a prefab carries no terrain paint).
- How a biome reference handles ground cover and grass, which the game draws as terrain details.

## Suggested order, when the time comes
Not during the wave of 2026-10-06 (city, travellers, journal, seasons, horse). After those are tested:
1. The exporter and the thumbnail sheet (they give both of us eyes).
2. Processing and placement for small points of interest, starting from the exported landmarks.
3. The Forest reference and its comparison.
4. Authored states.

## Rules of the project that will apply
See `CLAUDE.md`: trees, plants and boundary pieces are drawn instanced; nothing is created outside the tile builder's
slices; generation is pure and deterministic through `EndlessNoise` streams (take new ones from the free ranges);
`Assets/Synty/**` and `Assets/polyperfect/**` are never edited; scenes and prefabs are made through editor menus, not by
hand-written YAML; the finite world stays bit-identical.
