# Tests notes

Use personal skill **draft-test-plan** when requesting new tests for a feature.

- One feature → one Test Plan (`docs/test-plans/{feature-slug}.md`) → several Cases in `src/tests/test_plans/{feature-slug}/`
- **Case Tag** `{feature}_{T}{nn}` = id / Trigger; file/class same with `-` → `_`
- **Test Scenario** = descriptive title only (`Description`)
- Shared runner: `harness/`; shared Fixture network: `fixtures/`
- Runbook: [docs/TESTING.md](../../docs/TESTING.md)
