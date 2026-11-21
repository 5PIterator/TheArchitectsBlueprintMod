# Notes to address

Grouped from playtest notes. Each item has where to start.

## Paste placement and arrows

Candles and plants place correctly. `layer_R01` should record that, and cover the failures below instead of the old mushroom/candle mis-route.

- [x] **Small pebbles fail to place.** Candles and plants succeed, so this is not the Frame/Plant vs Block regression. Start at `SmallPiles` style and anchor placement (`FrameLayer` / `FrameOperations`), then add the failing tile to `layer_R01`.
- [x] **Normal mushrooms use the backup paste-arrow sprite.** `TileExecutor.ResolveCosmeticPlaceItemType` returns `0` when the interaction row has no visual item. `PasteArrow.PreDraw` then keeps the wooden-arrow projectile sprite (`Content/Projectiles/PasteArrow.cs`). Check the mushroom row's `GetVisualItemId` (`InteractionMapData.cs`): ammo, else tool item, else `0`.
- [x] **Modded tiles place, but their paste arrows use the wrong sprite.** Drawing already goes through tModLoader (`ItemLoader.ItemCount`, `ModUtils.LoadItem`, `TextureAssets.Item`), not a vanilla-only item list. `LoadItem` is async, and a texture that is still 1×1 falls back to the wooden arrow. Confirm whether the mapped item id is wrong, or the mod texture is not ready on the frame the arrow draws.
- [x] **Require Items off must still use the item channels, with a count of 0.** Today `LayerItemChecks` and `TileExecutor` return before inventory lookup when `RequireItems` is false or Sweep is on (`LayerItemChecks.cs`, `TileExecutor.cs`). The interaction map should still be consulted so those paths stay testable; the required stack is 0 and the item does not need to be present.

## Large selections and projections

- [ ] **Opening the map lags on a massive selection.** Map overlays are drawn from `ArchitectOverlayMapLayer` → `OverlaySystem.DrawMapOverlays`. Guard masks for large areas are one rectangle (`BlueprintGuard.ApplyGuardOverlay`); a selection that registers a point per tile will not be. Find which overlay walks every cell when the map opens.
- [ ] **Drawing a massive projection is laggy.** World preview already clips to on-screen tiles plus a 2-tile margin (`PayloadPreview.GetVisibleRelativeRange`). There is no draw cache and no LOD setting. Add a cached blit and/or a nearby-only mode, with client settings for both.
- [ ] **Printing a manifest for a massive payload freezes, then dumps an unusable chat log.** `ManifestPrinter.EnumerateLines` calls `Main.NewText` once per row. The info log is batched; chat is not. Two changes: batch the chat lines, and page them. An in-chat link should request the next page. Also check `ManifestBuilder`, which reloads interaction maps before the first line is printed — that is the likely freeze.

## Paste policy and safeguards

- [ ] **Move blueprint safeguards under Client Global Limits.** Reach and revealed-tile limits live in their own server config, `ArchitectBlueprintConfig`. Paste caps live in `PastingConfig.Limits.Client` (`GlobalLimitsConfig`, header `GlobalLimits`). The note asks for the safeguard toggles on that client limits block instead of a separate config entry.
- [ ] **Disabling safeguards leaves tiles black.** The mask color is `(0, 0, 0, 220)` in `BlueprintGuard`. `ApplyGuardOverlay` unregisters only when it runs. After both `LimitCaptureAndPasteToPlayerReach` and `RestrictCaptureAndPasteToRevealedTiles` are off, `NeedsDynamicGuardRefresh` is false and `RefreshClientGuardMasks` cancels the scan without clearing the existing `guarded` overlay. Turning the flags off should unregister those masks and clear `BlueprintCell.Protected`.
- [ ] **Config entry icon.** `ArchitectSystem` already has `Content/Icons/ArchitectMode`. tModLoader config entries do not show it. Find whether a `ModConfig` header or attribute can take that icon.
- [ ] **Sweep is ignored by Clear.** With Sweep active, plan-place only seeds allowed relatives and skips place identification (`PastingOperation`, around the `_sweepPhase.Active` branch). `ResolveFirstEnabledExecutionPhase` still runs Clear, then Cut, then Place, and Sweep last. When Sweep is on, skip those other checks and blit the footprint. Keep Sweep tick size and tick speed.
- [ ] **Overhaul and finish every setting description.** Labels and tooltips live in `Localization/en-US_Mods.TheArchitectsBlueprintMod.hjson` under `Configs` (`ClientLogConfig`, `ClientPasteConfig`, `PastingConfig`, `ArchitectModeConfig`, `ArchitectBlueprintConfig`, `TileMappingConfig`). Many tooltips are still `""`: log level, client clear strategy, overlay, Clear/Cut/Place Active, arrow preference, and the duplicate `LayerPolicySettings` / `LayerPolicyConfig` / `CustomLayerPolicySettings` blocks. Headers still say `Debug Header` and `Projectile Policy Header`. Rewrite each tooltip so it says what the setting does, whether the client or the host owns it, and what the default does. The filled `PastingConfig.LayerPolicy` text can seed the empty layer-policy copies.

## UI

- [ ] **Selection handles need clearer PIN/SET and hover feedback.** Handles are 12px, 16px on hover (`OverlaySystem.PointHandleSizePx`). They are easy to miss. Strengthen the idle and hover states for PIN/SET.
- [ ] **Chest and sign contents paste, with no arrow for the items.** `EntityLayer` still has `TODO(entity-arrows)`: Clear/Store should fly items out, Place/RequireItems should fly items in. Direct wipe and `LoadData` work today. Add a Clear-stage and Place-stage setting to enable or disable filling and clearing entity items.
- [ ] **The world GUI can be clicked through the payload browser.** Low priority. `TmodPayloadBrowserWidget` does not block inventory and other UI behind the window (`src/UI/TmodGUI/#notes.md`). Schedule a `/testui-ab-design` session for the browser rather than patching it ad hoc.
- [ ] **Plan a payload-toolbar window for client preferences.** Toolbar is the nine buttons in `ArchitectSystem` (search, manifest, PIN, SET, include, copy, paste, cut, exit). Add an option that opens a simple grouped checkbox list for client paste preferences and other payload settings (`ClientPasteConfig`: retry, clear style, arrow speeds). Plan only for now; do not build the window yet.
