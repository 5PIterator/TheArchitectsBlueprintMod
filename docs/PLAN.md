# The Architect's Blueprint / TheArchitectsBlueprintMod (TAB)

TAB is a Terraria mod that allows players to copy and paste structures using the materials they have.

## Definitions

- **Blueprint:** Data structure representing a user-selected region TAB will work with. It is a 2D grid of tiles that are included or excluded.
- **Selection:** An instance of a Blueprint, acting as a selected region of the world that TAB will consider as a source of tiles to copy.
- **Projection:** An instance of a Blueprint, acting as a movable ghost of a Selection that TAB will consider as a target of tiles to paste.

**CUT**: When enabled, a paste also clears tiles in the Selection's source area, so the operation behaves like a move instead of a duplicate.

## Internal vs dedicated actions

- **PIN**, **ADD**, **REMOVE**, **COPY**, and **PASTE** map to dedicated tier‑1 sticks and to combined wands.
- **SET** and **RESET** are **selection manipulators** used while editing the blueprint: they appear as gestures on the **Pin Stick** and **Blueprint Wand** (and later on all‑in‑one tools), not as separate tier‑1 “Set Stick” / “Reset Stick” products.

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
  - Clears tiles in the **target** area according to paste rules, then fills from inventory. If **CUT** is on for that projection, the paste pipeline also clears the **source capture area** before/during placement (see `Projection.CutSource` / `PastingOperation.ApplySourceClear`).
- **CUT** (per projection, optional): **Final UX** — toggled with an **interactive control on the projection overlay** (button or clear on/off state on the ghost). **Current build** — see “Temporary implementation” below.

## Action and accent colors (target matrix, org §B)

| Affordance | Intended color | Gem / accent | Notes |
|------------|----------------|--------------|-------|
| PIN | White | Diamond | Primary boundary; dedicated Pin Stick (**B1**). |
| SET | (neutral / UI) | (not required to match Diamond) | Internal; on Blueprint Wand (**B1**). |
| RESET | Red | Ruby | Internal; “stop / clear bounds” read (**B5**). |
| ADD | Yellow *(may change)* | Topaz *(legacy)* | Hue not locked (**B2**). |
| REMOVE | Purple *(may change)* | Amethyst *(legacy)* | Hue not locked (**B3**). |
| COPY | Green | Emerald | **B4**; “cut under copy” is this flag, not a separate stick. |
| CUT | — | Projection chrome | Lives on the projection UI, not a tier‑1 gem slot in the final design. |
| PASTE | Blue | Sapphire | **B6**. |

## Features (target design)

- **PIN STICK:** PIN / RESET. Create a basic Blueprint with two pins. (Diamond)
  - Left click: Place Pin B and start a new Blueprint.
  - Left hold: Drag Pin A to define bounds.
  - Left release: Finalize pins.
  - Right click: RESET the Blueprint.
  - Does not edit an existing Blueprint with the same gesture as SET (click always starts a new PIN flow).
- **ADD STICK:** ADD. Refine Blueprint or Projection selection. (Topaz)
- **REMOVE STICK:** REMOVE. Refine Blueprint or Projection selection. (Amethyst)
- **COPY STICK:** COPY. Create and move a Projection. (Emerald)
  - Left click / hold / release: Place or drag the Projection.
  - Right click: Remove the Projection under the cursor (RESET-modified COPY).
- **PASTE STICK:** PASTE. Run the paste process on a Projection. (Sapphire)
  - Left click: Start pasting.
  - Right click: Stop / cancel an active paste on the projection under the cursor.

## Temporary implementation (current build)

- **CUT** is toggled with the configuration option in the config menu.

## Current items by tier (code)

- **Tier 1** (`Content/Items/Tier1/`): `PinStick`, `AddStick`, `RemoveStick`, `CopyStick`, `PasteStick`.
- **Tier 2** (`Content/Items/Tier2/`): `SelectionWand`, `InclusionWand` (Add/Remove; right click opens action wheel or cancels active selection preview), `ProjectionWand` (action wheel: Copy, CUT, Paste)
- **Tier 3** (`Content/Items/Tier3/`): `ArchitectsStencil` (placeholder)
- **Tier 4** (`Content/Items/Tier4/`): `ArchitectsSoul` (placeholder)

- **SELECTION WAND:** PIN / SET / RESET
  - Left click: Execute the action selected in the action wheel.
  - Right click: Open the action wheel.
  - Action wheel:
    - 1: PIN (place pin B and drag to define bounds)
    - 2: PIN behavior if no Blueprint exists, otherwise SET (grab/drag pins, edges, corners, inside)
    - 3: RESET (clear the Blueprint that is clicked)
- **INCLUSION WAND:** ADD / REMOVE
  - Left click: Execute the action selected in the action wheel.
  - Right click: Open the action wheel.
  - Action wheel:
    - 1: ADD (add tiles to the selection of the Blueprint or Projection)
    - 2: REMOVE (remove tiles from the selection of the Blueprint or Projection)

- **PROJECTION WAND:** COPY / PASTE / RESET
  - Left click: Execute the action selected in the action wheel.
  - Right click: Open the action wheel.
  - Action wheel:
    - 1: COPY (create and move a Projection) (CUT can be toggled on the projection)
    - 2: PASTE (start pasting on the projection)
    - 3: RESET (remove the Projection under the cursor)

- **ARCHITECT'S STENCIL:** PIN / SET / ADD / REMOVE / COPY / PASTE / RESET
  - Left click: Execute the action selected in the action wheel.
  - Right click: Open the action wheel.
  - Action wheel:
    - 1: PIN (place pin B and drag to define bounds)
    - 2: SET (grab/drag pins, edges, corners, inside)
    - 3: ADD (add tiles to the selection of the Blueprint or Projection)
    - 4: REMOVE (remove tiles from the selection of the Blueprint or Projection)
    - 5: COPY (create and move a Projection) (CUT can be toggled on the projection)
    - 6: PASTE (start pasting on the projection)
    - 7: RESET (clear the Blueprint or Projection that is clicked)

- **ARCHITECT'S SOUL:** A consumable item that permanently unlocks the Architect's Blueprint GUI. (work in progress)

## Notes / future optimizations

- Blueprint and Projection share behavior (tile mask, bounds, gestures, overlay). Consider shared helpers to reduce duplication and cost (e.g. fewer per-tile rebuilds).
- ActionWheel: consider caching slot centers, avoiding redundant RebuildVisibleEntries, pooling entries, optional skip Draw when idle.
- Execution policy belongs in `src/Core/ToolActionManager.cs` rather than scattered in items.
- RESET could be simplified to a boolean.
- BlueprintSystem and ProjectionSystem: cleaner class hierarchy over time.
