# Testing

One feature → one Test Plan → several Test Cases under a matching folder.

**Case Tag:** `{feature}_{T}{nn}` (id / Trigger). File/class: same, with `-` → `_`.  
**Test Scenario:** descriptive title only.

```text
src/tests/
  harness/       # runner / registry / guards
  fixtures/      # cross-feature Fixture network
  payloads/      # .tab assets
  test_plans/
    {feature}/   # Cases for that feature (paste_P01.cs, …) — no cases/ subfolder
```

| Feature | Cases | Plan |
|---------|-------|------|
| `selection` | `test_plans/selection/` | [docs/test-plans/selection.md](../../docs/test-plans/selection.md) |
| `projection` | `test_plans/projection/` | [docs/test-plans/projection.md](../../docs/test-plans/projection.md) |
| `payload` | `test_plans/payload/` | [docs/test-plans/payload.md](../../docs/test-plans/payload.md) |
| `paste` | `test_plans/paste/` | [docs/test-plans/paste.md](../../docs/test-plans/paste.md) |
| `paste-mp` | `test_plans/paste-mp/` | [docs/test-plans/paste-mp.md](../../docs/test-plans/paste-mp.md) |
| `layer` | `test_plans/layer/` | [docs/test-plans/layer.md](../../docs/test-plans/layer.md) |

Type codes: `P` Positive, `N` Negative, `B` Boundary, `F` Performance, `R` Regression, `O` Other.

Runbook: [docs/TESTING.md](../../docs/TESTING.md).
