# The Architect's Blueprint / TheArchitectsBlueprintMod (TAB)

TAB is a Terraria mod that allows players to copy and paste structures using the materials they have.

## Definitions

- **Blueprint:** Data structure representing a user-selected region TAB will work with. It is a 2D grid of tiles that are included or excluded.
- **Selection:** An instance of a Blueprint, acting as a selected region of the world that TAB will consider as a source of tiles to copy.
- **Projection:** An instance of a Blueprint, acting as a movable ghost of a Selection that TAB will consider as a target of tiles to paste.

**CUT**: When enabled on a projection, a paste also clears tiles in that projection's source capture area, so the operation behaves like a move instead of a duplicate (`Projection.CutSource` / `PastingOperation.ApplySourceClear`).

## Internal vs dedicated actions

- **PIN**, **INCLUDE**, **EXCLUDE**, **COPY**, and **PASTE** map to dedicated tier‑1 sticks and to combined wands.
- **SET** and **RESET** are **selection / projection manipulators**: they appear on the **Selection Wand**, **Projection Wand**, **Execution Wand**, **Architect's Stencil**, and Architect Mode (SET only), not as separate tier‑1 sticks.
- **CUT** is not a `ToolAction`. It is a per-projection flag.

## Actions

- **PIN:** Pins two points to define a new Blueprint. (White/Diamond/<>)
  - Creates a new Blueprint with each interaction.
- **SET:** Edits bounds of an existing Blueprint (grab pins, edges, corners, inside). (.../|>)
  - If no Blueprint exists, behaves like PIN.
- **RESET:** Clears the Blueprint bounds and tile selection mask. (.../#)
  - Internal to blueprint tools; pairs well with a **red / ruby** accent in UI (see theme table below).
- **ADD:** Adds tiles to the selection of the Blueprint or Projection. (Yellow/Topaz/+)
- **REMOVE:** Removes tiles from the selection of the Blueprint or Projection. (Purple/Amethyst/-)
- **COPY:** Creates or moves a **Projection** of the current Blueprint payload at the cursor. (Green/Emerald/C)
  - There is a single projection type; optional cut is not a separate “Cut Projection.”
- **PASTE:** Starts the pasting process for a Projection under the cursor. (Blue/Sapphire/V)
  - Uses the player’s inventory or an inventory adjacent to the Blueprint or Projection.
  - Clears tiles in the **target** area according to paste rules, then fills from inventory. If **CUT** is on for that projection, the paste pipeline also clears the **source capture area**.
- **CUT** (per projection, optional): **Target UX** — toggle with an interactive control on the projection overlay. **Current build** — see Temporary implementation below.

## Action and accent colors

Source of truth: `src/UI/Style/Palette.cs`.

| Affordance | Color | Gem / accent | Notes |
|------------|-------|--------------|-------|
| PIN | White | Diamond | Selection bounds; Pin Stick. |
| SET | Sapphire | Sapphire | Pin B / bounds edit; Selection Wand. |
| INCLUDE | Yellow | Topaz | Include Stick. |
| EXCLUDE | Amber | Amber | Exclude Stick. |
| COPY | Amethyst | Amethyst | Copy Stick; projection border when cut is off. |
| PASTE | Emerald | Emerald | Paste Stick. |
| CUT | Ruby | Ruby | Projection chrome when cut-from-source is on. Not a tier‑1 stick. |
| RESET | Obsidian | Obsidian | Internal manipulator. |

## Current items by tier (`Content/Items/`)

- **Tier 1:** `PinStick`, `IncludeStick`, `ExcludeStick`, `CopyStick`, `PasteStick`
- **Tier 2:** `SelectionWand`, `InclusionWand`, `ProjectionWand`, `ExecutionWand`, `BlueprintCore` (crafting ingredient)
- **Tier 3:** `ArchitectsStencil`, `DebugStick`
- **Tier 4:** `ArchitectsHand` (accessory; temporary Architect Mode access while equipped)
- **Tier 5:** `ArchitectsBlueprint` (consumable; permanent Architect Mode unlock)

### PIN STICK — PIN

- Left click: Place Pin B and start a new Selection.
- Left hold: Drag Pin A to define bounds.
- Left release: Finalize pins.
- Right click: RESET the Selection (also cancels an in-progress PIN drag).
- Does not SET an existing Selection; click always starts a new PIN flow.

### INCLUDE STICK — INCLUDE

- Left click / hold / release: Drag a rectangle; release includes tiles in the mask.
- Right click: Cancel the rectangle preview.

### EXCLUDE STICK — EXCLUDE

- Left click / hold / release: Drag a rectangle; release excludes tiles from the mask.
- Right click: Cancel the rectangle preview.

### COPY STICK — COPY

- Left click / hold / release: Place or drag the Projection.
- Right click: Remove the Projection under the cursor.

### PASTE STICK — PASTE

- Left click: Start pasting on the projection under the cursor.
- Right click / hold left + right: Stop an active paste on that projection.

### SELECTION WAND — PIN / SET / RESET

- Left click: Execute the action selected in the action wheel.
- Right click: Open the action wheel.
- Action wheel: PIN, SET, RESET.

### INCLUSION WAND — INCLUDE / EXCLUDE

- Left click: Execute the action selected in the action wheel (rectangle gesture).
- Right click: Open the action wheel (or cancel an active rectangle preview).
- Action wheel: INCLUDE, EXCLUDE.

### PROJECTION WAND — COPY / PASTE / RESET

- Left click: Execute the action selected in the action wheel.
- Right click: Open the action wheel.
- Action wheel: COPY, PASTE, RESET.
- CUT is not on this wheel.

### EXECUTION WAND — PASTE / RESET

- Left click: Execute the action selected in the action wheel.
- Right click: Open the action wheel.
- Action wheel: PASTE, RESET.

### ARCHITECT'S STENCIL — PIN / SET / INCLUDE / EXCLUDE / COPY / PASTE / RESET

- Left click: Execute the action selected in the action wheel.
- Right click: Open the action wheel.
- Action wheel: PIN, SET, INC, EXC, COPY, PASTE, RESET.

### DEBUG STICK — debug helpers (not a player progression tool)

- Right click: Action wheel — CUT (toggle `CutSource` on the projection under the cursor), STP (stepped paste). Debug builds also get TST (scenario tests).

### ARCHITECT'S HAND / ARCHITECT'S BLUEPRINT

- **Hand:** Accessory. While equipped, the player can open Architect Mode.
- **Blueprint:** Consumable. Using it permanently unlocks Architect Mode. If already unlocked, using it opens Architect Mode.
- **Crafting / shimmer currently disabled** — see Temporary implementation. Cheat-spawn for testing.

## Architect Mode

Camera Mode–style session owned by `ArchitectSystem` (`src/Core/Client/Architect/`): equip-page tab, HUD suppression, dedicated toolbar, fullscreen-map interaction, TmodGUI hosting, overlays, toolbox, and action wheel. Held sticks/wands do not fire while the session is open.

**Entry:** Equip-page tab (inventory open) when `ArchitectModePlayer.CanAccessArchitectGui` is true (Hand equipped or Blueprint unlock). Esc / inventory key exits.

**Toolbar:** payload browser, print manifest, PIN, SET, Include/Exclude toggle, COPY, PASTE, CUT toggle, exit. RESET is not a toolbar slot (right-click still cancels/resets per the selected action).

**Map:** Fullscreen map tool interaction only works while Architect Mode is active (`ArchitectSystem.TryGetCursorTile`). With Mode off, map overlays can still render, but held items cannot edit on the map.

**World toolbox** (`ProjectionToolboxControl`, hosted by `ArchitectSystem`): payload browser + print manifest + CUT toggle on the frontmost defined selection or projection. Hidden during Architect Mode and on the map.

## Temporary implementation (current build)

- **CUT** player toggle is the Architect Mode toolbar, world toolbox, and Debug Stick (plus `EnableCutPasteProcess` as a paste-policy debug override).
- **Architect Mode recipes are disabled** until Mode is more stable:
  - Architect's Hand has no craft recipe; Hand → Blueprint shimmer is off (`Content/Items/Tier4/ArchitectsHand.cs`).
  - Hand and Blueprint items can still be spawned with cheats for testing.
  - Fullscreen map tool interaction only works while Architect Mode is active (map stays render-only when Mode is gated off).
  - **Panic button:** client config `Architect Mode` → *Clear permanent Architect Mode unlock* (one-shot; save while in a world) clears accidental permanent unlocks from the Blueprint item.

## Notes / future optimizations

- Blueprint and Projection share behavior (tile mask, bounds, gestures, overlay). Consider shared helpers to reduce duplication and cost (e.g. fewer per-tile rebuilds).
- ActionWheel: consider caching slot centers, avoiding redundant RebuildVisibleEntries, pooling entries, optional skip Draw when idle.
- Execution policy belongs in `src/Core/Client/ToolActionManager.cs` rather than scattered in items.
- RESET could be simplified to a boolean.
- BlueprintSystem and ProjectionSystem: cleaner class hierarchy over time.
- Re-enable Hand recipe and Hand → Blueprint shimmer when Architect Mode is stable.
