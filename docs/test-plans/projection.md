# Test Plan: Projection

## Scope

- **Feature slug:** `projection` (Cases: `src/tests/test_plans/projection/`)
- **In scope:** Capture selection into projection, cut flag, MoveTo target.
- **Out of scope:** Paste world writes (see paste plans).
- **Environment:** In-world, CHM origin; uses `ProjectionOrigin` + `PayloadCellFixture` for synthetic payloads.

## Harness mapping

| Skill term | Project equivalent |
|------------|--------------------|
| Test Case | `ITestCase` |
| Test Scenario | `Description` |
| Test Report | `ITestCaseReporter` + log dump |
| Test Fixture | Chamber `ProjectionOrigin`, `PayloadCellFixture` |
| Test Trigger | `/tab.test run <id>` |

## Fixture network

```text
ChamberOrigin → ProjectionOriginReady ┬→ copy (capture world)
                                      └→ SyntheticStonePayload → cut / move
```

| Fixture | Purpose | Prerequisites | Side effects / teardown |
|---------|---------|---------------|-------------------------|
| `ProjectionOriginReady` | Work at `ProjectionOrigin` | ChamberOrigin | Cases remove projection / clear selection |
| `SyntheticStonePayload` | In-memory stone via `PayloadCellFixture` | none | none |

## Test Cases

### projection_P01 — NewProjection captures a selection into a matching payload size

- **Type:** Positive
- **Trigger:** `/tab.test run projection_P01`
- **Fixture join:** `ProjectionOriginReady`
- **Steps:** Define selection → NewProjection(capture) → assert payload size.
- **Expected Report:** Pass when payload size matches selection.

### projection_P02 — SetCut toggles CutSource; empty payload forces it off

- **Type:** Positive
- **Trigger:** `/tab.test run projection_P02`
- **Fixture join:** `ProjectionOriginReady` + `SyntheticStonePayload`
- **Steps:** SetPayload → SetCut on/off → empty payload → assert CutSource forced off.
- **Expected Report:** Pass when toggle and empty-payload force-off work.

### projection_P03 — MoveTo changes target origin; relative payload cells stay

- **Type:** Positive
- **Trigger:** `/tab.test run projection_P03`
- **Fixture join:** `ProjectionOriginReady` + `SyntheticStonePayload`
- **Steps:** SetPayload → MoveTo → assert target origin and relative cells.
- **Expected Report:** Pass when target moves and cells stay relative.

## Coverage matrix

| Behavior | Positive | Negative | Boundary | Performance | Regression | Other |
|----------|----------|----------|----------|-------------|------------|-------|
| Capture | projection_P01 | — | — | — | — | — |
| Cut flag | projection_P02 | — | — | — | — | — |
| MoveTo | projection_P03 | — | — | — | — | — |

## Runbook

1. Prerequisites: world + CHM origin.
2. Trigger: `/tab.test run projection_{T}{nn}`.
3. Report: chat + case log dump.
