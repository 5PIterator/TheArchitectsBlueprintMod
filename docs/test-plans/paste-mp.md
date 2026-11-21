# Test Plan: Paste (multiplayer)

## Scope

- **Feature slug:** `paste-mp` (Cases: `src/tests/test_plans/paste-mp/`)
- **In scope:** Topology-neutral MP paste on a multiplayer client (listen host or remote joiner): stone paste with chest consumption, cut, no materials, operation-limit lifecycle.
- **Out of scope:** Dedicated-server process as runner; overlay/Dig FX peer visibility (manual extras in TESTING.md).
- **Environment:** Multiplayer client in a loaded world; CHM origin; material chest at `MaterialChestOrigin` (reach ring). Missing MP topology **Abandons** the Case (not Fail) via `RequireMultiplayerClient`.

## Harness mapping

| Skill term | Project equivalent |
|------------|--------------------|
| Test Case | `ITestCase` under `test_plans/paste-mp/` |
| Test Scenario | `Description` |
| Test Report | `ITestCaseReporter` + log dump |
| Test Fixture | `PasteConfigFixture`, `MpCoordinator`, `MpNetworking`, `StonePasteFixture.Cleanup` |
| Test Trigger | `/tab.test run paste-mp_*` on the client under test |

## Fixture network

```text
ChamberOrigin → RequireMultiplayerClient
  → DefaultPaste / RetryOnMissing (PasteConfigFixture)
  → AuthorityWorldPrepared (RequestWorldSetup) → ProjectionMoved (TryCreateMovedStoneProjection)
       ├→ ChestStocked → PasteExecuted → WorldMatched → ChestConsumed → Unlock → RequestCleanup
       ├→ ChestEmpty   → PasteExecuted → TargetStaysAir → Unlock → RequestCleanup
       └→ DualProjection + ChestEmpty + RetryOnMissing → LimitReject → Cancel → Unlock (lifecycle)
```

| Fixture | Purpose | Prerequisites | Side effects / teardown |
|---------|---------|---------------|-------------------------|
| `DefaultPaste` | Pins all paste ModConfigs (reach/map limits off; no place retries; PlayerOperationLimit=1) | MP client near CHM | restores prior ModConfigs |
| `RetryOnMissing` | Same as DefaultPaste + `RetryOnMissingItems=true` | MP client near CHM | restores prior ModConfigs |
| `RequirePlayerNearChamber` | Fail if out of paste inventory reach (no teleport) | ChamberOrigin | none |
| `AuthorityWorldPrepared` | Auth place/clear stone cells | MP client | RequestCleanup |
| `ProjectionMoved` | Local projection moved to target | world prepared | Cleanup projection |
| `ChestStocked` / `ChestEmpty` | Reach-ring wood chest materials | ValidateReachRingChest | chest cleared on cleanup |
| `PlayerOperationSlot` | First paste occupies slot (lifecycle) | RetryOnMissing | cancel |

## Test Cases

### paste-mp_P01 — Multiplayer 1x1 stone paste: authority setup, chest materials, paste, consumption, cell diff

- **Type:** Positive
- **Trigger:** `/tab.test run paste-mp_P01`
- **Fixture join:** `DefaultPaste` → `AuthorityWorldPrepared` → `ProjectionMoved` → `ChestStocked`
- **Steps:** Setup → paste → target match → chest stack decreases → unlock → cleanup.
- **Expected Report:** Pass when sync + consumption asserts hold.
- **Notes:** Run on topologies A/B/C (see TESTING.md).

### paste-mp_P02 — Multiplayer 1x1 cut paste: stocked chest, target matches, source clears

- **Type:** Positive
- **Trigger:** `/tab.test run paste-mp_P02`
- **Fixture join:** `DefaultPaste` → `AuthorityWorldPrepared` → `ProjectionMoved` + CutEnabled → `ChestStocked`
- **Steps:** Cut paste → destination matches → source cleared.
- **Expected Report:** Pass when cut semantics hold under MP.

### paste-mp_N03 — Multiplayer paste with empty material chest: no place, unlock

- **Type:** Negative
- **Trigger:** `/tab.test run paste-mp_N03`
- **Fixture join:** `DefaultPaste` → `AuthorityWorldPrepared` → `ProjectionMoved` → `ChestEmpty`
- **Steps:** Paste with empty chest → no place → unlock.
- **Expected Report:** Pass when target stays air and projection unlocks.

### paste-mp_B04 — Multiplayer paste lifecycle: operation-limit reject, cancel, unlock, no tile writes

- **Type:** Boundary
- **Trigger:** `/tab.test run paste-mp_B04`
- **Fixture join:** `RetryOnMissing` → Dual `AuthorityWorldPrepared` + `ChestEmpty`
- **Steps:** Start A → B hits PlayerOperationLimit → cancel A → both unlock → no writes.
- **Expected Report:** Pass when limit/cancel/unlock path holds.

## Coverage matrix

| Behavior | Positive | Negative | Boundary | Performance | Regression | Other |
|----------|----------|----------|----------|-------------|------------|-------|
| MP paste + chest | paste-mp_P01, paste-mp_P02 | — | — | — | — | — |
| MP no materials | — | paste-mp_N03 | — | — | — | — |
| Op limit / cancel | — | — | paste-mp_B04 | — | — | — |

## Runbook

1. Prerequisites: MP client (not dedicated process), CHM origin, near chamber. On single-player, `/tab.test all` reports `[ABANDON]` for these Cases instead of Fail.
2. Per topology A/B/C, run all four `paste-mp_*` ids (see [TESTING.md](../TESTING.md)).
3. Report: chat + case log dump; optional `[PasteNet]` route check.
