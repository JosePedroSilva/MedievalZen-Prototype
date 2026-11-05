# MedievalZen

A relaxed medieval travel simulator ("Slow Roads, but on foot"): procedurally generated
countryside and dirt roads between towns, villages and inns, with no objective. Unity 6000.3.8f1,
URP. The design and the iteration plan are in `Docs/DESIGN.md`; the one-road endless mode is in
`Docs/ENDLESS_ROAD.md`.

## What is and is not in this repository

- **In git:** everything first-party under `Assets/_Project/`, the scenes, project settings,
  docs and tools. Binary files (textures, audio, models, fonts, the generated finite-world
  terrain) go through **Git LFS**, see `.gitattributes`.
- **Not in git:** the third-party packs under `Assets/Synty/`, `Assets/polyperfect/` and
  `Assets/Sounds/`. They are licensed and weigh about 8.5 GB together, so each clone imports
  them locally. The full list, with folders and sources, is `Tools/AssetCheck/third-party-packs.txt`.

## Setting up a clone

1. Install [Git LFS](https://git-lfs.com) and run `git lfs install` once per machine.
2. Clone the repository. LFS files download with the clone; in a clone made before LFS was
   installed, run `git lfs pull`.
3. Import the third-party packs into the folders named in `Tools/AssetCheck/third-party-packs.txt`.
   Import each pack with its own `.meta` files (the `.unitypackage` from the store, or a copy of
   the folder from another checkout): first-party prefabs, scenes and palettes reference these
   assets by GUID.
4. Run the asset check and fix whatever it lists, then open the project in Unity.

### The asset check

The check reads the pack manifest, verifies each pack's folder, file count and a few sentinel
files the project references, and flags unfetched Git LFS pointers. Exit code 0 means everything
is present.

| Platform | Command |
|---|---|
| Windows | `Tools\AssetCheck\check-assets.cmd` (double-click works; it runs the `.ps1` with Windows PowerShell) |
| macOS / Linux | `Tools/AssetCheck/check-assets.sh` |
| Anywhere with PowerShell 7 | `pwsh Tools/AssetCheck/check-assets.ps1` |

When a new third-party pack is imported, add a line for it to `third-party-packs.txt`.

## Working in the project

`CLAUDE.md` holds the conventions of the codebase (layout, naming, determinism rules, the
review tools). Read it before changing anything.
