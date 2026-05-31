# `src/tests` layout

## Purpose
- `cases/`: headless deterministic logic tests (run with .NET test runner). These cover metadata/JSON-shape logic that does not require the Terraria runtime.
- `scenarios/`: headful command-triggered tests that run in a loaded world. These cover payload TagIO, import, compatibility, and mod-system integration.

## Run headless `cases`
From the mod root:

`dotnet test src/tests/cases/TheArchitectsBlueprintMod.Cases.Tests.csproj`

## Run headful `scenarios` in game
Join a world, then run chat commands:

- `/tab.test list` (alias for scenario list)
- `/tab.test all` (run all scenarios)
- `/tab.test run <scenarioId>`
- `/tab.test scenarios list|all|run <scenarioId>`

Example scenario ids:

- `sc1` - Verifies local player context
- `sc2` - Core mod systems loaded
- `sc3` - SelectionSystem reachable
- `sc4` - Payload TagIO document roundtrip
- `sc5` - Payload TagIO stream roundtrip
- `sc6` - Mirror and legacy JSON import
- `sc7` - FromSparse bounds filtering
- `sc8` - Payload compatibility validate-only
- `sc9` - Payload IO clear-mode preconditions (requires projection under cursor)
- `sc10` - Payload IO fill-mode API check (requires projection under cursor)
- `sc11` - Source payload vs post-paste target diff (requires projection under cursor)

## Naming conventions
- `cases/tests`: file names end with `CasesTests.cs`; each test focuses on deterministic logic.
- `scenarios/tests`: class names end with `ScenarioTest`; each scenario has a stable `Id`.
