# The Architect's Blueprint — Pre-Release (0.2.0)

Engineering tracker for the pre-release. Player-facing summary: [README](../README.md).

## Remaining (pre-release / known issues)

### Correctness
- [ ] Plants / tile-anchored objects (blue mushrooms, candles) mis-routed through the block layer instead of plant/frame — see [src/Core/#notes.md](../src/Core/%23notes.md)
- [ ] Mod resistance: session-level `modsLoaded` + skip/Unloaded placeholders only; per-tile export tracking and pre-projection compatibility checks not complete
- [ ] Paste planning lag on moderate payloads (identification / `BuildLookup`) — see [src/Core/#notes.md](../src/Core/%23notes.md)

### Multiplayer
- [ ] Playtest paste on dedicated server + remote client + host-and-play (architecture exists; needs verification)

### UX / polish
- [ ] Export browser: pick export target like import file picker (currently always topmost projection with a payload)
- [ ] Payload browser UI polish (text fields, layout) — see [src/UI/TmodGUI/#notes.md](../src/UI/TmodGUI/%23notes.md)
- [ ] Clear operations may hang on stubborn inventories — skip after first failure + chat message
- [ ] Large payload move/scale/export performance (untested at ~548×339)

### Testing
- [ ] Expand pre-release coverage: run scenario suite in release builds; add CI for `dotnet test src/tests/cases/`
- [ ] MP paste smoke-test checklist

## Roadmap / vNext

- [ ] Architect's Blueprint GUI (Tier 5 item is a stub)
- [ ] Rotate and mirror projection
- [ ] Selection overlay: more visible SET resize handles
- [ ] Remote payload import (Discord / URL)
- [ ] Denser tile encoding in TagIO (numeric/binary tile ids vs string names)
- [ ] Entity-layer paste arrows for chest store/clear — `TODO(entity-arrows)` in [EntityLayer.cs](../src/Core/Layers/Operations/EntityLayer.cs)
- [ ] Payload browser filters: Session / Local / Remote
- [ ] Pasting an empty payload should clear the area
- [ ] Item art for all tiers (textures not tracked in repo)
- [ ] Inventory edge case: item removed mid-operation

## Done (archive)

- Map overlays + map cursor input — [ArchitectOverlayMapLayer.cs](../src/UI/ArchitectOverlayMapLayer.cs), [ArchitectMapInput.cs](../src/Core/Client/ArchitectMapInput.cs)
- Projection sprite preview overlay — `PayloadPreview`, `ProjectionSystem`
- Blueprint data model — `Blueprint` / `Selection` / `Projection`, `BlueprintSystem`
- Local import/export browser — `PayloadBrowser`, `TmodPayloadBrowserWidget`
- Export metadata — project name, author, player name, mods, versions, thumbnail
- Host-authoritative multiplayer paste — `PasteNetworking`, server-side execution
- Discovery + interaction mapping + phased paste stack
- Mod rename to The Architect's Blueprint
- Tier 1–3 tools + Stencil; Tier 4 Hand (accessory), Tier 5 Blueprint (animation stub)
- Headless + in-game test scaffolding — [TESTING.md](TESTING.md)
