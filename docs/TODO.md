# The Architect's Blueprint — Pre-Release (0.2.0)

Engineering tracker for the pre-release. Player-facing summary: [README](../README.md).

**Needs improvement** — the feature is in the build, but incomplete or still wrong. **Remaining** — not implemented, or not signed off.

## Needs improvement

### Correctness
- [ ] Plants / tile-anchored objects — `FrameLayer` and `PlantLayer` exist, and `BlockLayer` refuses frame tiles, but glowing-mushroom plants and candles are still the open case. Confirm with `layer_R01`. Notes: [src/Core/#notes.md](../src/Core/%23notes.md).
- [ ] Mod resistance — session `modsLoaded`, skip/Unloaded placeholders, and `ComputeMissingMods` (`payload_N08`) are in. Export still writes every loaded mod (`CreateExportMetadata`); per-tile export tracking is not (`payload_R09` fails until it lands).
- [ ] Paste planning lag — Identify / `BuildLookup` still spikes on moderate payloads. `paste_F05` only times the spike (2s tripwire). Notes: [src/Core/#notes.md](../src/Core/%23notes.md).

### UX / polish
- [ ] Payload browser — import/export works. Text fields, hint text replacing input, layout, click-through, and which payload is selected still need work. Notes: [src/UI/TmodGUI/#notes.md](../src/UI/TmodGUI/%23notes.md).
- [ ] Export target — export still always uses the topmost projection with a payload. No picker like import.
- [ ] Selection overlay — SET resize handles exist (12px, 16px on hover) and are still too easy to miss.
- [ ] Entity paste — chest/sign/TE sidecars wipe and rewrite in place. Store/clear still has no paste arrows (`TODO(entity-arrows)` in [EntityLayer.cs](../src/Core/Layers/Operations/EntityLayer.cs)). The entity layer defaults off, so a cut can leave source chests and signs behind.

## Remaining (pre-release / known issues)

### Multiplayer
- [ ] Playtest paste on dedicated server + remote client + host-and-play. Cases `paste-mp_P01`–`B04` are written; topologies A/B/C in [TESTING.md](TESTING.md) are not signed off.

### UX / polish
- [ ] Clear operations may hang on stubborn inventories — skip after first failure + chat message is not implemented.
- [ ] Large payload move/scale/export performance (untested at ~548×339).
- [ ] Pasting an empty payload should clear the area. Empty cells are skipped during place planning and do not clear the target.

### Testing
- [ ] Expand pre-release coverage: run `/tab.test all` in release-adjacent debug builds; smoke-check `paste_P04` with Journey materials — plans in [test-plans/](test-plans/).

## Roadmap / vNext

- [ ] Architect Mode recipes: re-enable Hand recipe + Hand→Blueprint shimmer. Mode itself works (cheat-spawn, map interaction requires Mode). Both are still commented out in [ArchitectsHand.cs](../Content/Items/Tier4/ArchitectsHand.cs). See [PLAN.md](PLAN.md) Temporary implementation.
- [ ] Rotate and mirror a projection.
- [ ] Remote payload import (Discord / URL).
- [ ] Denser tile encoding in TagIO — cells still store string tile and wall names.
- [ ] Payload browser source filters: Session / Local / Remote. Current filters are validity, legacy, and missing mods.
- [ ] Item art for all tiers (textures not tracked in repo).
- [ ] Inventory edge case: item removed mid-operation.

## Done (archive)

- Map overlays + map cursor input — [ArchitectOverlayMapLayer.cs](../src/UI/ArchitectOverlayMapLayer.cs), [ArchitectSystem.cs](../src/Core/Client/Architect/ArchitectSystem.cs)
- Projection sprite preview overlay — `PayloadPreview`, `ProjectionSystem`
- Blueprint data model — `Blueprint` / `Selection` / `Projection`, `BlueprintSystem`
- Local import/export browser — `PayloadBrowser`, `TmodPayloadBrowserWidget`
- Export metadata — project name, author, player name, mods, versions, thumbnail
- Host-authoritative multiplayer paste — separate operations per player, per-player and server caps, owner-only ACK and overlay — `PasteNetworking`, `PastingSystem`
- Automated MP paste cases — `paste-mp_P01`, `P02`, `N03`, `B04` (playtest still open above)
- Discovery + interaction mapping + phased paste stack
- Mod rename to The Architect's Blueprint
- Tier 1–3 tools + Stencil; Tier 4 Hand (accessory); Tier 5 Blueprint unlocks and opens Architect Mode
- In-game Test Case suite + Test Plans — [TESTING.md](TESTING.md), [test-plans/](test-plans/)
