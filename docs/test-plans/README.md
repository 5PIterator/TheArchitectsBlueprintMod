# Test Plans

One **feature** → one **Test Plan** → several **Test Cases**.  
Plan: `docs/test-plans/{feature-slug}.md`  
Cases: `src/tests/test_plans/{feature-slug}/` (file/class: `{feature}_{T}{nn}.cs`, hyphens → underscores)  
Case Tag / Trigger: `{feature}_{T}{nn}`

Terminology and workflow: personal skill `draft-test-plan`.

| Feature slug | Plan | Example tags |
|--------------|------|--------------|
| `payload` | [payload.md](payload.md) | `payload_P01` … `payload_R09` |
| `selection` | [selection.md](selection.md) | `selection_P01` … `selection_P03` |
| `projection` | [projection.md](projection.md) | `projection_P01` … `projection_P03` |
| `paste` | [paste.md](paste.md) | `paste_P01` … `paste_F05` |
| `paste-mp` | [paste-mp.md](paste-mp.md) | `paste-mp_P01` … `paste-mp_B04` |
| `layer` | [layer.md](layer.md) | `layer_R01` |

Type codes: `P` Positive, `N` Negative, `B` Boundary, `F` Performance, `R` Regression, `O` Other.

Shared harness/fixtures: `src/tests/harness/`, `src/tests/fixtures/`.  
How to run: [../TESTING.md](../TESTING.md).
