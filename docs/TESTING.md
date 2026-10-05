# Testing

## Purpose

In-game, command-triggered scenario tests under `src/tests/scenarios/`. They run in a loaded single-player world and cover payload TagIO/import/compatibility, selection and projection APIs, and paste diffs.

## Run scenarios in game

Join a world (single-player for paste scenarios), then:

- `/tab.test list`
- `/tab.test all`
- `/tab.test run <scenarioId>`

Debug Stick action **TST** also runs `/tab.test all` (auto-stepped across frames).

## Folder layout

| Folder | System under test |
|--------|-------------------|
| `scenarios/Payload/` | TagIO, JSON import, FromSparse, compatibility |
| `scenarios/Selection/` | Bounds, include/exclude mask, translate |
| `scenarios/Projection/` | Copy from selection, cut flag, MoveTo |
| `scenarios/Paste/` | Stone/cut paste and fixture world diffs |

Shared runner code stays in `scenarios/`. Scenario folders are `Payload/`, `Selection/`, `Projection/`, and `Paste/` (namespaces use a `*Tests` suffix so they do not collide with core type names).

## Scenario ids

Format: `{system}.{name}` (lowercase, one dot).

| Id | Description |
|----|-------------|
| `payload.tag` | TagIO BuildDocument / TryReadDocument roundtrip |
| `payload.stream` | Compressed TagIO stream roundtrip |
| `payload.json` | Mirror and legacy JSON import |
| `payload.sparse` | FromSparse bounds filtering |
| `payload.compat` | Validate-only clean stone + unresolvable report |
| `payload.skip` | SkipIncompatible remaps unresolvable tiles |
| `payload.modded` | Loads `SS_MassiveModded.tab` (defined document only) |
| `selection.bounds` | SetPoints defines size; ClearSelection clears |
| `selection.mask` | IncludeTiles / ExcludeTiles |
| `selection.move` | TranslateSelection keeps size |
| `projection.copy` | NewProjection payload size matches selection |
| `projection.cut` | SetCut toggle; empty payload forces off |
| `projection.move` | MoveTo relocates target; relative cells stay |
| `paste.stone` | 1x1 stone capture → paste → cell diff |
| `paste.cut` | 1x1 cut paste; source cleared |
| `paste.layer` | `TTP_LayerTestVanilla.tab` paste diff |
| `paste.medium` | `TMM_MediumVanilla.tab` paste diff |

Fixture pastes need the structure’s materials in inventory (Journey mode is the practical setup). A material miss shows up as a payload diff failure.

## Naming conventions

- Folder names match the system (`Payload`, `Selection`, `Projection`, `Paste`).
- Class / file names are PascalCase feature + `Scenario` (e.g. `TagRoundtripScenario`).
- Each scenario exposes a stable dotted `Id`.
- New scenarios auto-register: any concrete `IScenarioTest` with a parameterless constructor is discovered by `ScenarioTestRegistry` (sorted by id). No hand-maintained list.
