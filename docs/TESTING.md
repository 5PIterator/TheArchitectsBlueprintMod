# Testing

In-game, trigger-driven Test Cases under `src/tests/test_plans/{feature-slug}/`. They run in a loaded world via Test Triggers (`/tab.test`, Debug Stick **TST**). One feature → one Test Plan → several Cases. Plans: [test-plans/](test-plans/).

Terms: **Test Plan** (Case list for one feature), **Test Scenario** (Case title), **Case Tag** (`{feature}_{T}{nn}`), **Test Case** (`ITestCase`), **Test Report** (reporter + log), **Test Fixture** (shared setup chain), **Test Trigger** (in-game run command).

## Test Triggers

**Prerequisite:** pin the universal test chamber origin. Equip the Debug Stick, select wheel action **CHM**, and left-click a flat open area. `/tab.test all|run` and Debug Stick **TST** refuse to start until the origin is set (persisted in the world file; synced to peers in multiplayer).

Join a world (SP or multiplayer client for `paste_*`; multiplayer client for `paste-mp_*`), then:

- `/tab.test list` — Case Tag, Test Type, Test Scenario
- `/tab.test all`
- `/tab.test run <CaseTag>`

Debug Stick **TST** runs `/tab.test all` (one enumerator step per frame).

Test Report dumps:

`{Terraria}/tModLoader/TheArchitectsBlueprintMod/tests/case-<label>-<timestamp>.log`

Chat announces the dump path when a run starts and when it finishes.

## Layout

```text
src/tests/
  harness/       # runner, registry, guards, reporter
  fixtures/      # cross-feature Fixture network (+ fixtures/Mp/)
  payloads/      # .tab assets
  test_plans/
    selection/   # ↔ docs/test-plans/selection.md
    projection/  # ↔ docs/test-plans/projection.md
    payload/     # ↔ docs/test-plans/payload.md
    paste/       # ↔ docs/test-plans/paste.md
    paste-mp/    # ↔ docs/test-plans/paste-mp.md
    layer/       # ↔ docs/test-plans/layer.md
```

Case Tags: `{feature}_{T}{nn}` (file/class: same, with `-` in the feature slug → `_`). Type codes: `P` Positive, `N` Negative, `B` Boundary, `F` Performance, `R` Regression, `O` Other. Concrete `ITestCase` types with a parameterless constructor auto-register via `TestCaseRegistry`.

## Test Plans

| Feature slug | Plan | Cases folder |
|--------------|------|--------------|
| `payload` | [test-plans/payload.md](test-plans/payload.md) | `src/tests/test_plans/payload/` |
| `selection` | [test-plans/selection.md](test-plans/selection.md) | `src/tests/test_plans/selection/` |
| `projection` | [test-plans/projection.md](test-plans/projection.md) | `src/tests/test_plans/projection/` |
| `paste` | [test-plans/paste.md](test-plans/paste.md) | `src/tests/test_plans/paste/` |
| `paste-mp` | [test-plans/paste-mp.md](test-plans/paste-mp.md) | `src/tests/test_plans/paste-mp/` |
| `layer` | [test-plans/layer.md](test-plans/layer.md) | `src/tests/test_plans/layer/` |

Index: [test-plans/README.md](test-plans/README.md).

## Test chamber layout

All Case geometry is offset from the CHM origin (not the player). Stand near the chamber before running Cases — paste inventory reach and MP authority Chebyshev checks fail (no teleport) when you are out of range.

| Slot | Offset from origin | Used by |
|------|--------------------|---------|
| `StoneSource` | (-1, -2) | `paste_P01`, `paste_P02`, `paste_N03`, `paste-mp_*` |
| `StoneTarget` | (1, -2) | same |
| `StoneTargetB` | (2, -2) | `paste-mp_B04` second paste |
| `MaterialChestOrigin` | (1, -4) | `paste-mp_*` wood-chest chamber |
| `FixtureOrigin(w,h)` | (0, 0) bottom-left | `paste_P04`, `paste_F05` |
| `LayerSupport(i)` | (-2+2i, -1) | `layer_R01` columns 0..2 |
| `SelectionOrigin` | (8, -4) | `selection_*` |
| `ProjectionOrigin` | (20, -6) | `projection_*` |

Discovery (`PlacementDiscovery` / `BiomeDiscovery`) uses the CHM origin when set; otherwise it falls back to `(playerTile.X, playerTile.Y - 5)`.

## Multiplayer paste topologies

Automated coverage for three topologies. Record build/mod list, world, and date at the top of each run. Run Triggers on the **client under test** (listen host or remote joiner). Case details: [test-plans/paste-mp.md](test-plans/paste-mp.md).

Expected `[PasteNet]` StartPaste routes:

| Topology | Who pastes | Expected route |
|----------|------------|----------------|
| Dedicated + remote client | Joiner | `RequestPaste` (`localHost=false`) |
| Host & Play | Listen host | `ExecutePaste` (`localHost=true`) |
| Host & Play | Remote joiner | `RequestPaste` (`localHost=false`) |

For each topology A/B/C, run:

```text
/tab.test run paste-mp_P01
/tab.test run paste-mp_P02
/tab.test run paste-mp_N03
/tab.test run paste-mp_B04
```

| Topology | Client under test |
|----------|-------------------|
| A — Dedicated + remote | Remote client joined to dedicated server |
| B — Host & Play host | Listen host |
| C — Host & Play joiner | Remote joiner in the same H&P session |

### Manual extras (optional)

1. [ ] Overlay / Dig FX visible to peers where applicable
2. [ ] `[PasteNet]` log route matches the table above

**Pass:** all four `paste-mp_*` Cases green on A, B, and C.

## Naming conventions

- Feature folder slug matches `docs/test-plans/{feature-slug}.md`.
- **Case Tag** = id / Trigger: `{feature}_{T}{nn}`; file/class: same with `-` → `_` (e.g. `paste-mp_P01` → `paste_mp_P01.cs`).
- **Test Scenario** = `Description` title only (not the tag).
- `Type` must match the tag's type code.
