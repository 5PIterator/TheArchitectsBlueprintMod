# Test Plan: Selection

## Scope

- **Feature slug:** `selection` (Cases: `src/tests/test_plans/selection/`)
- **In scope:** Defining selection bounds, include/exclude mask, translating selection.
- **Out of scope:** Paste, projection copy internals.
- **Environment:** In-world, CHM origin; uses `SelectionOrigin` chamber slot.

## Harness mapping

| Skill term | Project equivalent |
|------------|--------------------|
| Test Case | `ITestCase` |
| Test Scenario | `Description` |
| Test Report | `ITestCaseReporter` + log dump |
| Test Fixture | Chamber `SelectionOrigin` via `TestChamberSystem` |
| Test Trigger | `/tab.test run <id>` |

## Fixture network

```text
ChamberOrigin → SelectionOriginReady → (bounds | mask | move)
```

| Fixture | Purpose | Prerequisites | Side effects / teardown |
|---------|---------|---------------|-------------------------|
| `ChamberOrigin` | CHM pinned (runner guard) | Debug Stick CHM | persisted in world |
| `SelectionOriginReady` | Work at `SelectionOrigin` | ChamberOrigin | Cases clear selection |

## Test Cases

### selection_P01 — SetPoints defines width/height; ClearSelection clears bounds

- **Type:** Positive
- **Trigger:** `/tab.test run selection_P01`
- **Fixture join:** `SelectionOriginReady`
- **Steps:** NewSelection → SetPoints → assert size → ClearSelection → assert cleared.
- **Expected Report:** Pass when size matches and clear empties bounds.

### selection_P02 — IncludeTiles / ExcludeTiles update the selection mask

- **Type:** Positive
- **Trigger:** `/tab.test run selection_P02`
- **Fixture join:** `SelectionOriginReady`
- **Steps:** Define selection → IncludeTiles / ExcludeTiles → assert mask.
- **Expected Report:** Pass when mask updates match.

### selection_P03 — TranslateSelection shifts origin and keeps size

- **Type:** Positive
- **Trigger:** `/tab.test run selection_P03`
- **Fixture join:** `SelectionOriginReady`
- **Steps:** Define selection → TranslateSelection → assert origin/size.
- **Expected Report:** Pass when origin shifts and size is unchanged.

## Coverage matrix

| Behavior | Positive | Negative | Boundary | Performance | Regression | Other |
|----------|----------|----------|----------|-------------|------------|-------|
| Bounds define/clear | selection_P01 | — | — | — | — | — |
| Mask include/exclude | selection_P02 | — | — | — | — | — |
| Translate | selection_P03 | — | — | — | — | — |

## Runbook

1. Prerequisites: world + CHM origin.
2. Trigger: `/tab.test run selection_{T}{nn}`.
3. Report: chat + case log dump.
