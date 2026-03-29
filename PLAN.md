The Architect's Stencil/ArchitectsStencilMod (TAS)

TAS is a Terraria mod that allows players to copy and paste structures using the materials they have.

Definitions:
Blueprint: The user-selected region TAS will work with.
Projection: Secondary instance of the Blueprint acting as coordinates for the pasting process.

Actions:
- PIN: An action responsible for pinning two points of the Blueprint to the world. (White/Diamond/<>)
    - Creates a new Blueprint with each interaction.
- SET: An action responsible for setting the bounds of the Blueprint. (.../|>)
    - Interacts with an already existing Blueprint. If no Blueprint exists, acts as a PIN action.
- RESET: An action responsible for resetting the Blueprint. (.../#)
    - Clears the Blueprint bounds (and therefore its tile selection mask).
- ADD: An action responsible for adding tiles to the selection of either Blueprint or Projection. (Yellow/Topaz/+)
- REMOVE: An action responsible for removing tiles from the selection of either Blueprint or Projection. (Purple/Amethyst/-)
- COPY: An action responsible for creating a Copy Projection of Blueprint and its position. (Green/Emerald/C)
- CUT: An action responsible for creating a Cut Projection of Blueprint and its position. (Red/Ruby/X)
- PASTE: An action responsible for starting a pasting process. (Blue/Sapphire/V)
    - Pasting will use the user's inventory, or an inventory adjacent to either Blueprint or Projection.
    - Pasting Copy Projection will first remove blocks from the selected tiles, then try to fill them using the inventory.
    - Pasting Cut Projection will first remove the Blueprint and use its items to fill the Cut Projection.

FEATURES:
- PIN STICK: PIN/RESET Action. User uses this wand to create a basic Blueprint with two pinned points. (Diamond)
    - Left click: Place Pin B and create a new Blueprint.
    - Left hold: Drag Pin A to define the Blueprint bounds.
    - Left release: Finalize pins.
    - Right click: RESET the Blueprint.
    - Note: This tool does not support post-editing an existing Blueprint (click always starts a new PIN).
- ADD STICK: ADD Action. User can modify and refine the selection of either Blueprint or Projection. (Topaz)
    - Left click: Select point A of the selection.
    - Left hold: Resize the selection.
    - Left release: Select point B of the selection and apply the selection to either Blueprint or Projection.
    - Right click: Cancel selection.
- REMOVE STICK: REMOVE Action. User can modify and refine the selection of either Blueprint or Projection. (Amethyst)
    - Left click: Select point A of the deselection.
    - Left hold: Resize the deselection.
    - Left release: Select point B of the deselection and apply the deselection.
    - Right click: Cancel deselection.
- COPY STICK: COPY Action. User can copy the Blueprint and a new Projection of the selected tiles. (Emerald)
    - Left click: Create a Projection of the Blueprint underneath the cursor.
    - Left hold: Move the Projection.
    - Right click: Remove Projection.
- CUT STICK: CUT Action. User can cut the existing Blueprint and move it to a different place. (Ruby)
    - Left click: Create a Projection of the Blueprint underneath the cursor.
    - Left hold: Move the Projection.
    - Right click: Remove Projection.
- PASTE STICK: PASTE Action. User can click on a Projection and execute the pasting process. (Sapphire)
    - Left click: Start the pasting process for a Projection underneath the cursor.
    - Right click: Cancel the pasting process

Current items by tier (code):
- Tier 1 (`Content/Items/Tier1/`): `PinStick`, `AddStick`, `RemoveStick`, `CopyStick`, `CutStick`, `PasteStick`
- Tier 2 (`Content/Items/Tier2/`): `BlueprintWand`, `StencilWand` (inventory right-click swaps Add/Remove), `ProjectionWand` (inventory right-click cycles Copy/Cut/Paste)
- Tier 3 (`Content/Items/Tier3/`): `ArchitectsStencil` (currently a placeholder)
- Tier 4 (`Content/Items/Tier4/`): `ArchitectsSoul` (currently a placeholder)

- BLUEPRINT WAND: SET/RESET Action. A combined item from two PIN sticks
    - Left click: SET Action (grab/drag pins/edges/corners/inside; otherwise starts a new SET gesture).
    - Left hold: Drag the grabbed Blueprint element (pins, edges, corners, inside).
    - Left release: End drag.
    - Right click: RESET Action.
- STENCIL WAND: ADD, REMOVE Actions. A combined item from REMOVE and ADD sticks
    - Left click: ADD Action
    - Right click: REMOVE Action
- PROJECTION WAND: COPY, CUT, PASTE Actions. A combined item from COPY, CUT and PASTE sticks.
    - Left click: COPY/CUT/PASTE Action
    - Right click: Action Wheel

- ARCHITECT'S STENCIL: SET/RESET, ADD/REMOVE, COPY/CUT/PASTE Actions. A combined item from BLUEPRINT, STENCIL and PROJECTION wands.
    - Left click: Action
    - Right click: Action Wheel

- ARCHITECT'S SOUL: PIN/SET/RESET, ADD/REMOVE, COPY/CUT/PASTE Actions. An upgrade from ARCHITECT'S STENCIL.
    - Permanent boost allowing a full gui.



Notes / future optimizations:
- Copy/Cut and Blueprint/Projection share a lot of behavior (tile selection mask, rectangle bounds, edit gestures, overlay rendering). Consider unifying common logic (shared base + interfaces, or composition helpers) to reduce duplication and improve performance (e.g., avoid per-tile rebuilds/draw loops where possible).
- ActionWheel: consider caching slot centers per visible-count, avoiding RebuildVisibleEntries when entries haven't changed, pooling ActionWheelEntry lists, and a style option to skip Draw when no hover/selection change (for very low-overhead "always open" modes).
- Responsibility for when to execute the action should fall under `src/Core/ToolActionManager.cs` instead of the tool.
- RESET could be simplified to a boolean.
- BlueprintSystem and ProjectionSystem need to be cleaned up and ordered into a more logical class hierarchy.
