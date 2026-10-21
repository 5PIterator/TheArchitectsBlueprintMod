# Test Plan: Payload

## Scope

- **Feature slug:** `payload` (Cases: `src/tests/test_plans/payload/`)
- **In scope:** Import/export document roundtrips, JSON import, sparse bounds, validate/skip incompatible tiles, missing-mod reporting, modded `.tab` load, export `modsLoaded` content awareness.
- **Out of scope:** World paste diffs (see paste plans); private TagIO field layout.
- **Environment:** In-world with CHM origin set. No SP/MP paste required.

## Harness mapping

| Skill term | Project equivalent |
|------------|--------------------|
| Test Case | `ITestCase` |
| Test Scenario | `Description` |
| Test Report | `ITestCaseReporter` + `case-*.log` |
| Test Fixture | `PayloadCellFixture`, `PayloadJsonFixture`, `PayloadFixturePaths` |
| Test Trigger | `/tab.test run <id>` |

## Fixture network

```text
PayloadCellStone ─────────────────────────┬→ payload_P01 / P02 / R09 / N08
                                          ├→ payload_P05 / P06 (+ UnresolvableCell)
PayloadJsonEmbedded ──────────────────────┴→ payload_P03
PayloadFixturePaths(.tab) ─────────→ payload_O07
```

| Fixture | Purpose | Prerequisites | Side effects / teardown |
|---------|---------|---------------|-------------------------|
| `PayloadCellStone` | Synthetic stone cell/payload | none | none |
| `UnresolvableCell` | Bad tile for validate/skip | none | none |
| `PayloadJsonEmbedded` | Mirror + legacy JSON strings | none | none |
| `TabFileResolved` | Resolve `src/tests/payloads/*.tab` | file on disk | none |

## Test Cases

### payload_P01 — TagIO BuildDocument / TryReadDocument roundtrip of a stone cell

- **Type:** Positive
- **Trigger:** `/tab.test run payload_P01`
- **Fixture join:** `PayloadCellStone`
- **Steps:** Build stone payload → BuildDocument → TryReadDocument → compare cells.
- **Expected Report:** Pass when roundtrip cells match.

### payload_P02 — WriteDocument / TryReadDocument memory-stream roundtrip

- **Type:** Positive
- **Trigger:** `/tab.test run payload_P02`
- **Fixture join:** `PayloadCellStone`
- **Steps:** Write compressed stream → read back → compare.
- **Expected Report:** Pass when stream roundtrip matches.

### payload_P03 — Mirror and legacy sparse JSON import with cell equality

- **Type:** Positive
- **Trigger:** `/tab.test run payload_P03`
- **Fixture join:** `PayloadCellStone` + `PayloadJsonEmbedded`
- **Steps:** Import mirror JSON and legacy sparse JSON → assert cells.
- **Expected Report:** Pass when both imports match expected stone cells.

### payload_B04 — FromSparse keeps in-bounds cells and drops out-of-bounds placements

- **Type:** Boundary
- **Trigger:** `/tab.test run payload_B04`
- **Fixture join:** `PayloadCellStone`
- **Steps:** FromSparse with in/out of bounds placements → assert filtering.
- **Expected Report:** Pass when OOB cells are dropped and in-bounds kept.

### payload_P05 — Validate-only reports clean stone and flags an unresolvable tile

- **Type:** Positive
- **Trigger:** `/tab.test run payload_P05`
- **Fixture join:** `PayloadCellStone` + `UnresolvableCell`
- **Steps:** ValidateOnly clean stone → ValidateOnly with bad tile → assert reports.
- **Expected Report:** Pass when clean is ok and unresolvable is flagged.

### payload_P06 — SkipIncompatible keeps stone and remaps an unresolvable tile

- **Type:** Positive
- **Trigger:** `/tab.test run payload_P06`
- **Fixture join:** `PayloadCellStone` + `UnresolvableCell`
- **Steps:** Prepare with SkipIncompatible → assert stone kept and bad remapped.
- **Expected Report:** Pass when remap behavior matches.

### payload_O07 — Loads SS_MassiveModded.tab metadata and cells when style-valid

- **Type:** Other
- **Trigger:** `/tab.test run payload_O07`
- **Fixture join:** `TabFileResolved` (`SS_MassiveModded.tab`)
- **Steps:** Read file → assert DocumentInfo / cells when style-valid.
- **Expected Report:** Pass when load succeeds under style-valid conditions.

### payload_N08 — ComputeMissingMods flags unknown mods; ValidateOnly surfaces MissingModsFromMetadata

- **Type:** Negative
- **Trigger:** `/tab.test run payload_N08`
- **Fixture join:** `PayloadCellStone`
- **Steps:** Probe fake missing mod + this mod → ComputeMissingMods / Prepare ValidateOnly.
- **Expected Report:** Pass when fake mod is reported and this mod is not.

### payload_R09 — Vanilla-only stone export must not list unused session mods

- **Type:** Regression
- **Trigger:** `/tab.test run payload_R09`
- **Fixture join:** `PayloadCellStone`
- **Steps:** CreateExportMetadata for vanilla stone → assert ModsLoaded empty/content-aware.
- **Expected Report:** Pass when unused session mods are absent; currently expected red until per-tile tracking.
- **Notes:** Known failing gate in TODO.

## Coverage matrix

| Behavior | Positive | Negative | Boundary | Performance | Regression | Other |
|----------|----------|----------|----------|-------------|------------|-------|
| Tag/stream roundtrip | payload_P01, payload_P02 | — | — | — | — | — |
| JSON import | payload_P03 | — | — | — | — | — |
| Sparse bounds | — | — | payload_B04 | — | — | — |
| Compat / skip | payload_P05, payload_P06 | — | — | — | — | — |
| Missing mods | — | payload_N08 | — | — | — | — |
| Modded load | — | — | — | — | — | payload_O07 |
| Export mods list | — | — | — | — | payload_R09 | — |

## Runbook

1. Prerequisites: join world, set CHM origin (Debug Stick **CHM**).
2. Trigger: `/tab.test run payload_{T}{nn}` or `/tab.test all`.
3. Report: chat + `{SavePath}/TheArchitectsBlueprintMod/tests/case-*.log`.
