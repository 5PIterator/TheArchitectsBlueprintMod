# Test Plan: Layer classify

## Scope

- **Feature slug:** `layer` (Cases: `src/tests/test_plans/layer/`)
- **In scope:** User-visible layer routing for plants/frames vs blocks (normal mushrooms, water candles, small pebbles vs stone), ambient rubble Echo decode, and strict `.itr` zero-count item channels against pinned VanillaMap / ModdedMap fixtures.
- **Out of scope:** Full modular layer paste coverage (`paste_P04`); paste planning perf (`paste_F05`).
- **Environment:** In-world, CHM origin; `LayerSupport(0..3)` columns for `layer_R01`. Map Cases need a role that can load InteractionMap files (SP / listen host / dedicated).

## Harness mapping

| Skill term | Project equivalent |
|------------|--------------------|
| Test Case | `ITestCase` |
| Test Scenario | `Description` |
| Test Report | `ITestCaseReporter` + log dump |
| Test Fixture | `LayerColumnFixture`, `LayerIdentifyFixture`, `MapFixturePaths`, `InteractionMapPathFixture` |
| Test Trigger | `/tab.test run layer_R01` / `layer_R02` / `layer_R03` |

## Fixture network

```text
ChamberOrigin
  → LayerColumnsPlaced   ← LayerColumnFixture.TryJoinSupportSubject / TryJoinStoneControl
  → IdentifyReady        ← LayerIdentifyFixture.TryJoinLookup → IdentifyPayload
  → OwnershipObserve     ← TryObserveAnchoredOrPlantOwnership / TryObserveFrameOwnership / TryObserveBlockOwnership
  → ItrChannelObserve    ← TryObserveZeroCountPlantChannel / TryObserveUnresolvedWhenUnmapped
  → Teardown             ← LayerColumnFixture.ClearTile

RequireInteractionMapLoadable → MapFixturePaths → InteractionMapPathFixture
  → ItrChannelObserve (VanillaMap / ModdedMap)
```

| Fixture | Purpose | Prerequisites | Side effects / teardown |
|---------|---------|---------------|-------------------------|
| `LayerColumnsPlaced` | Stamp support/subject or stone control on `LayerSupport(i)` | ChamberOrigin | `ClearTile` in Case finally |
| `IdentifyReady` | 1×1 lookup + Identify pipeline | LayerColumnsPlaced | none |
| `OwnershipObserve` | Classifier + Identify ownership checks for the Case Report | IdentifyReady | none |
| `ItrChannelObserve` | Zero-count `.itr` availability / UnresolvedInteraction | Compiled InteractionMap or pinned fixture | `ItrStorage.Release` |
| `InteractionMapPathFixture` | Pins `CompiledInteractionMapPath` to a `src/tests/maps` fixture | Map loadable on this role | restores prior path |

## Test Cases

### layer_R01 — Normal mushrooms, water candles, and small pebbles classify as Plant/Frame; stone stays Block; `.itr` zero-count resolves mushrooms

- **Type:** Regression
- **Trigger:** `/tab.test run layer_R01`
- **Fixture join:** `LayerColumnsPlaced` → `OwnershipObserve` → `ItrChannelObserve`
- **Steps:**
  1. Join grass+`Plants` style 8 (normal mushroom), stone+`WaterCandle`, stone+`SmallPiles` (1×1 pebble style 3), and stone control.
  2. Observe Plant/Frame ownership (never Block) for mushroom/candle/pebble; Block for stone.
  3. Assert `TryResolveAmbientRubbleEcho` maps the pebble frames to `SmallPiles1x1Echo` style 3.
  4. Assert zero-count `.itr` availability for `Plants` style 8 resolves visual `ItemID.Mushroom`.
  5. Assert missing `.itr` Current yields `UnresolvedInteraction`.
  6. Teardown columns.
- **Expected Report:** Pass when classifiers and `.itr` channels match the assertions above.
- **Notes:** Uses the currently configured / default InteractionMap (not a pinned fixture). Re-run plant discovery + compile if the zero-count channel fails.

### layer_R02 — VanillaMap.itr: Plants style 8 zero-count resolves Mushroom; SmallPiles Echo maps style 3

- **Type:** Regression
- **Trigger:** `/tab.test run layer_R02`
- **Fixture join:** `RequireInteractionMapLoadable` → `MapFixturePaths(VanillaMap.itr)` → `InteractionMapPathFixture` → `ItrChannelObserve`
- **Steps:**
  1. Abandon if this role cannot load InteractionMap files.
  2. Fail if `src/tests/maps/VanillaMap.itr` is missing.
  3. Pin the fixture path, assert Plants style 8 → `ItemID.Mushroom` under zero-count.
  4. Assert `TryResolveAmbientRubbleEcho` for SmallPiles style 3 → `SmallPiles1x1Echo`.
- **Expected Report:** Pass naming the pinned path; Abandon on remote MP clients.

### layer_R03 — ModdedMap.itr: Calamity AstralDirt zero-count + AstralNormalSmallPiles Echo ammo

- **Type:** Regression
- **Trigger:** `/tab.test run layer_R03`
- **Fixture join:** `RequireInteractionMapLoadable` → `RequireModsLoaded(CalamityMod)` → `MapFixturePaths(ModdedMap.itr)` → `InteractionMapPathFixture`
- **Steps:**
  1. Abandon if maps are not loadable or `CalamityMod` is missing.
  2. Fail if `src/tests/maps/ModdedMap.itr` is missing.
  3. Pin ModdedMap; assert AstralDirt zero-count visual item.
  4. Assert AstralNormalSmallPiles style 0 has Fake Echo + AstralDirt ammo.
- **Expected Report:** Pass when Calamity is loaded and ModdedMap rows match; Abandon without Calamity.

## Coverage matrix

| Behavior | Positive | Negative | Boundary | Performance | Regression | Other |
|----------|----------|----------|----------|-------------|------------|-------|
| Plant/frame vs block | — | — | — | — | layer_R01 | — |
| Ambient rubble Echo | — | — | — | — | layer_R01, layer_R02 | — |
| Strict `.itr` zero-count | — | layer_R01 (unmapped) | — | — | layer_R01, layer_R02, layer_R03 | — |
| Pinned VanillaMap | — | — | — | — | layer_R02 | — |
| Pinned ModdedMap (Calamity) | — | — | — | — | layer_R03 | — |

## Runbook

1. Prerequisites: world + CHM origin. `layer_R02`/`layer_R03` need SP or listen host (or dedicated). `layer_R03` needs CalamityMod.
2. Trigger: `/tab.test run layer_R01` / `layer_R02` / `layer_R03`.
3. Report: chat + case log dump (`[ABANDON]` when topology/mods are wrong).
