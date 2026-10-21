# Test Plan: Layer classify

## Scope

- **Feature slug:** `layer` (Cases: `src/tests/test_plans/layer/`)
- **In scope:** User-visible layer routing for plants/frames vs blocks (mushroom plants, candles vs stone).
- **Out of scope:** Full modular layer paste coverage (`paste_P04`); paste planning perf (`paste_F05`).
- **Environment:** In-world, CHM origin; `LayerSupport(0..2)` columns.

## Harness mapping

| Skill term | Project equivalent |
|------------|--------------------|
| Test Case | `ITestCase` |
| Test Scenario | `Description` |
| Test Report | `ITestCaseReporter` + log dump |
| Test Fixture | `LayerColumnFixture`, `LayerIdentifyFixture` |
| Test Trigger | `/tab.test run layer_R01` |

## Fixture network

```text
ChamberOrigin
  → LayerColumnsPlaced   ← LayerColumnFixture.TryJoinSupportSubject / TryJoinStoneControl
  → IdentifyReady        ← LayerIdentifyFixture.TryJoinLookup → IdentifyPayload
  → OwnershipObserve     ← TryObserveAnchoredOrPlantOwnership / TryObserveBlockOwnership
  → Teardown             ← LayerColumnFixture.ClearTile
```

| Fixture | Purpose | Prerequisites | Side effects / teardown |
|---------|---------|---------------|-------------------------|
| `LayerColumnsPlaced` | Stamp support/subject or stone control on `LayerSupport(i)` | ChamberOrigin | `ClearTile` in Case finally |
| `IdentifyReady` | 1×1 lookup + Identify pipeline | LayerColumnsPlaced | none |
| `OwnershipObserve` | Classifier + Identify ownership checks for the Case Report | IdentifyReady | none |

## Test Cases

### layer_R01 — Glowing mushroom plants and candles classify as Frame/Plant, not Block; stone stays Block

- **Type:** Regression
- **Trigger:** `/tab.test run layer_R01`
- **Fixture join:** `LayerColumnsPlaced` → `OwnershipObserve`
- **Steps:** Join mushroom + candle + stone columns → observe Frame/Plant vs Block → teardown.
- **Expected Report:** Pass when plants/candles are not Block and stone stays Block.
- **Notes:** Gates known mis-route bug in TODO.

## Coverage matrix

| Behavior | Positive | Negative | Boundary | Performance | Regression | Other |
|----------|----------|----------|----------|-------------|------------|-------|
| Plant/frame vs block | — | — | — | — | layer_R01 | — |

## Runbook

1. Prerequisites: world + CHM origin.
2. Trigger: `/tab.test run layer_R01`.
3. Report: chat + case log dump.
