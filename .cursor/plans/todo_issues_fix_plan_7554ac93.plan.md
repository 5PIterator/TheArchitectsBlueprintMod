---
name: TODO Issues Fix Plan
overview: Five open TODO items with user-confirmed choices — robust export UI (UIInputTextField), full metadata UX (no networking), JsonTextReader import with structure verification and CheatSheet raw-grid support, plus overlay matrix and performance fixes.
todos:
  - id: export-ui-uistate
    content: "Replace hand-drawn export text field with UIState + UIInputTextField/UserInterface (robust fix)"
    status: pending
  - id: add-player-metadata-ux
    content: "Add playerName to export metadata (schema v2), show in import list details; no PasteNetProtocol changes"
    status: pending
  - id: fix-preview-matrix
    content: Fix ProjectionPayloadPreview SpriteBatch restore (GameViewMatrix, not UIScaleMatrix); optional OverlayCatalog guard
    status: pending
  - id: overhaul-layer-pass
    content: "If needed after matrix fix: add late Game interface layer for RegTileFill / paste overlays"
    status: pending
  - id: import-jsontextreader
    content: "CreateDialogEntry via JsonTextReader: metadata-only for wrapped files, raw-grid peek for CheatSheet; structure validation"
    status: pending
  - id: defer-import-capture
    content: "PayloadImportCommand: avoid LoadPayloadFromWorld before user selects file"
    status: pending
  - id: perf-drag
    content: "MoveTo/selection drag: skip RefreshTargetTiles on translate, dirty overlay, defer full rebuild to drag end"
    status: pending
  - id: perf-export
    content: "Export: single-pass JSON write, no JToken.Parse, compact formatting for large grids"
    status: pending
isProject: false
---

# Fix plan: TODO.md lines 10–16

## User decisions (plan iteration)

| Item | Decision |
|------|----------|
| **1. Export input** | **Robust fix only** — tModLoader `UIInputTextField` inside `UIState` + `UserInterface`, not the minimal `GetInputText`-in-draw workaround |
| **2. Player metadata** | Implement **all UX** (import list details, etc.); **network/paste metadata out of scope** (`PasteNetProtocol` unchanged) |
| **5. Import listing** | **Option A: `JsonTextReader`** — metadata-only for wrapped files; doubles as structure verification; must still list **raw payloads without metadata** (CheatSheet compatibility) |

---

## Issue map

| # | Symptom | Root cause | Primary files |
|---|---------|------------|---------------|
| 1 | Export name field ignores keystrokes | Hand-drawn modal calls `GetInputText` in `UpdateUI` (wrong phase) | [`PayloadFileDialogs.cs`](src/Core/Client/Payload/PayloadFileDialogs.cs) → new UI state |
| 2 | No player name in export JSON | `BuildExportWrapper` omits `playerName` | [`PayloadSerialization.cs`](src/Core/Client/Payload/PayloadSerialization.cs) |
| 3 | Overhaul breaks filled bounds + paste tile fills | `ProjectionPayloadPreview` leaves batch on `UIScaleMatrix` | [`ProjectionPayloadPreview.cs`](src/UI/ProjectionPayloadPreview.cs), [`OverlaySystem.cs`](src/UI/OverlaySystem.cs) |
| 4 | Lag at 548×339 | Per-frame grid realloc, overlay teardown, triple JSON on export | [`Projection.cs`](src/Core/Client/Projection/Projection.cs), [`ProjectionSystem.cs`](src/Core/Client/Projection/ProjectionSystem.cs), [`SelectionControl.cs`](src/Core/Client/Selection/SelectionControl.cs) |
| 5 | Import loads every payload | `JToken.Parse` entire file per list row | [`PayloadSerialization.cs`](src/Core/Client/Payload/PayloadSerialization.cs) |

---

## 1. Export input — robust UI fix (chosen approach)

**Drop:** Minimal fix (move `GetInputText` to draw / `clrInput` only).

**Implement:** Vanilla tModLoader UI stack for the export-name dialog.

### New types

- **`PayloadExportDialogState : UIState`** — panel layout matching current modal dimensions (`DialogWidth` 520, `ExportDialogHeight` 190).
- **`UIInputTextField`** (or `UITextPanel<string>` with editing enabled) for project name, max length 80, default text from `defaultProjectName`.
- **`UITextPanel`** labels: title, optional read-only **“Exported by: {playerName}”** (ties to issue 2 UX).
- **`UIButton`** or `UITextPanel` click handlers for Export / Cancel.

### Host changes in [`PayloadFileDialogs`](src/Core/Client/Payload/PayloadFileDialogs.cs)

- Add `UserInterface _exportUi` (import list stays hand-drawn).
- `ShowExportNameDialog`: `Main.clrInput()` on open; build `PayloadExportDialogState`; `_exportUi.SetState(exportState)`.
- `UpdateUI`: `_exportUi.Update(gameTime)` when export active; `mouseInterface = true`.
- `ModifyInterfaceLayers`: draw `_exportUi` in existing UI layer hook.
- Remove hand-drawn export text field / `GetInputText` path.

**Verify:** `/tab.export` — type, backspace, IME; saved `metadata.projectName` matches field.

---

## 2. Payload metadata + UX (network out of scope)

### Export metadata ([`BuildExportWrapper`](src/Core/Client/Payload/PayloadSerialization.cs))

- `["playerName"] = Main.player[projection.Owner].name` (guard owner active)
- `["schemaVersion"] = 2`

### UX (in scope)

| Surface | Change |
|---------|--------|
| Import list row | `playerName` in details when in metadata |
| Export dialog | Read-only “Author: {LocalPlayer.name}” label |
| Test samples | Add `playerName` to [`tests/Samples/*.tab.json`](tests/Samples/) if needed |

### Out of scope

- **`PasteNetProtocol`** — no wire metadata changes.

---

## 3. Terraria Overhaul overlay incompatibility

**Phase A:** Fix [`ProjectionPayloadPreview.Draw`](src/UI/ProjectionPayloadPreview.cs) — restore prior `SpriteBatch` state in `finally`.

**Phase B (if needed):** Late Game interface layer for fills.

---

## 4. Severe lag at large scales

**4a** drag deferrals → **4b** export single-pass → **4c** sparse capture (optional).

---

## 5. Import listing — JsonTextReader (Option A, chosen)

New `TryReadPayloadListInfo` using `JsonTextReader` + `StreamReader` in [`PayloadSerialization.cs`](src/Core/Client/Payload/PayloadSerialization.cs).

### A. Wrapped TAB (`StartObject` at root)

- Read `metadata` object → `projectName`, `width`, `height`, `createdUtc`, `playerName`.
- On `payload` → verify `StartArray`, then **`reader.Skip()`** (never load tiles).
- Invalid if wrapper has metadata but `payload` is not an array.

### B. Raw grid / CheatSheet (`StartArray` at root)

- No metadata — compatible with CheatSheet `SerializeObject(TileData[,])`.
- **Dimension peek:** stream outer/inner array lengths by counting array starts and skipping cell tokens (no `Payload.FromJson`).
- `displayName` = filename; details e.g. `Raw grid | WxH | modified …`.

### C. Invalid

- Neither object nor array → mark invalid / skip with error detail in list.

### Discovery & import

- Keep `*.json` enumeration for CheatSheet files in Payloads folder.
- Full parse only on **selected** file in `ImportProjection` (unchanged).

### Defer world capture

[`PayloadImportCommand`](src/Commands/PayloadCmd/PayloadImportCommand.cs): no `LoadPayloadFromWorld()` until after user picks file.

---

## Implementation order

1. Export UIState + UIInputTextField  
2. Player metadata + UX  
3. JsonTextReader listing + defer import capture  
4. ProjectionPayloadPreview matrix  
5. Performance 4a → 4b  

**Not touched:** `PasteNetProtocol.cs`.
