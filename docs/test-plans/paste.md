# Test Plan: Paste (single-player)

## Scope

- **Feature slug:** `paste` (Cases: `src/tests/test_plans/paste/`)
- **In scope:** Copy → stock materials → paste → world cell match; cut paste; refuse place without materials; modular layer `.tab` paste; plan/Identify timing tripwire.
- **Out of scope:** Multiplayer authority (see [paste-mp.md](paste-mp.md)); classifier ownership (see [layer.md](layer.md)).
- **Environment:** SP or multiplayer client (listen host / remote joiner), CHM origin, stand near chamber (reach). Journey materials recommended for `paste_P04`.

## Harness mapping

| Skill term | Project equivalent |
|------------|--------------------|
| Test Case | `ITestCase` |
| Test Scenario | `Description` |
| Test Report | `ITestCaseReporter` + log dump |
| Test Fixture | `PasteConfigFixture`, `StonePasteFixture`, `TabPasteFixture`, `InventoryHelpers`, `MpCoordinator` |
| Test Trigger | `/tab.test run <id>` / Debug Stick **TST** (all) |

## Fixture network

```text
ChamberOrigin → RequirePlayerNearChamber → DefaultPaste (PasteConfigFixture)
  → StoneWorldPrepared → ProjectionCopied → ProjectionMoved   ← StonePasteFixture.JoinProjectionMoved
       ├→ MaterialsGiven  → PasteExecuted → WorldMatched → Teardown   (stone, cut)
       ├→ MaterialsAbsent → PasteExecuted → TargetStaysAir → Teardown (paste_N03)
       └→ (planperf stone Build sample)
  → FixtureLoaded → ProjectionCopied → MaterialsGiven → PasteExecuted → Teardown
       ← TabPasteFixture.Run (paste_P04)
  → FixtureLoaded → ProjectionCopied → (Build + Identify timing only)  (planperf)
```

| Fixture | Purpose | Prerequisites | Side effects / teardown |
|---------|---------|---------------|-------------------------|
| `DefaultPaste` | Pins all ClientPasteConfig + PastingConfig (reach/map safeguards off; no place retries) | stand near CHM | restores prior ModConfigs |
| `RequirePlayerNearChamber` | Fail if out of paste inventory reach (no teleport) | ChamberOrigin | none |
| `ProjectionMoved` | 1x1 stone at StoneSource moved to StoneTarget (authority setup) | ChamberOrigin | `ClearWorldCells`; Cleanup projection |
| `MaterialsGiven` | `GiveRequiredItems` from manifest | ProjectionMoved | inventory dirty |
| `MaterialsAbsent` | strip required items | ProjectionMoved | inventory dirty |
| `FixtureLoaded` | `.tab` at FixtureOrigin | ChamberOrigin + file | TeardownPasteArea |
| `PasteExecuted` | `PasteHelpers.RunPasteAndAwait` | materials branch | op completes |

## Test Cases

### paste_P01 — 1x1 stone copy, give required items, paste onto air, cell diff

- **Type:** Positive
- **Trigger:** `/tab.test run paste_P01`
- **Fixture join:** `DefaultPaste` → `ProjectionMoved` → `MaterialsGiven`
- **Steps:** Join projection moved → give items → paste → diff target → teardown.
- **Expected Report:** Pass when target matches copied cell.

### paste_P02 — 1x1 cut paste with required items: target matches and source clears

- **Type:** Positive
- **Trigger:** `/tab.test run paste_P02`
- **Fixture join:** `DefaultPaste` → `ProjectionMoved` → CutEnabled → `MaterialsGiven`
- **Steps:** Enable cut → give items → paste → assert target match + source cleared.
- **Expected Report:** Pass when target matches and source is air.

### paste_N03 — Removes required items then expects paste to leave the target empty

- **Type:** Negative
- **Trigger:** `/tab.test run paste_N03`
- **Fixture join:** `DefaultPaste` → `ProjectionMoved` → `MaterialsAbsent`
- **Steps:** Strip materials → paste → assert target stays air.
- **Expected Report:** Pass when no place occurs (op may finish quickly).

### paste_P04 — Pastes TABModularTest.tab (3×4 of 7×7 layer modules) and diffs the target

- **Type:** Positive
- **Trigger:** `/tab.test run paste_P04`
- **Fixture join:** `DefaultPaste` → `FixtureLoaded` (`TABModularTest.tab`)
- **Steps:** TabPasteFixture.Run → paste → world diff.
- **Expected Report:** Pass when world matches payload.
- **Notes:** See `src/tests/payloads/README.md` for module map; Journey materials help.

### paste_F05 — Times PastePlanningLookup.Build and Identify on TABModularTest.tab

- **Type:** Performance
- **Trigger:** `/tab.test run paste_F05`
- **Fixture join:** `DefaultPaste` → optional `ProjectionMoved` (stone sample) + `FixtureLoaded` for modular timing
- **Steps:** Time Build on stone; load modular fixture; time Build + Identify; teardown.
- **Expected Report:** Pass when Build stays under tripwire (2000 ms); metrics logged.
- **Metrics:** stone Build ms, modular Build ms, Identify ms.

## Coverage matrix

| Behavior | Positive | Negative | Boundary | Performance | Regression | Other |
|----------|----------|----------|----------|-------------|------------|-------|
| Paste with materials | paste_P01, paste_P02, paste_P04 | — | — | — | — | — |
| Paste without materials | — | paste_N03 | — | — | — | — |
| Plan/Identify cost | — | — | — | paste_F05 | — | — |

## Runbook

1. Prerequisites: world (SP or MP client), CHM origin, near chamber.
2. Trigger: `/tab.test run paste_P01` (or other `paste_*` Case Tag) or `/tab.test all`.
3. Report: chat + case log dump.
