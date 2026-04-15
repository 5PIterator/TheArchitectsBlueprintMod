# The Architect's Stencil

## Overview
The Architect's Stencil (`ArchitectsStencil`) is a Terraria mod that lets you capture a region of the world as a *Blueprint* and place copies of it elsewhere using the materials you already own. It focuses on intuitive selection tools and clear visual feedback so larger builds feel fast instead of fiddly.

## Core Concepts
- **Blueprint**: The region you select that The Architect's Stencil will work with.
- **Projection**: A movable ghost of a Blueprint that shows where the structure will be built.
- **Tools**: A family of sticks and wands that handle selecting, editing, copying, and pasting structures.

## Features
- **Selector Stick (PIN)**: Start a fresh blueprint and **RESET** it from the same item.
- **Blueprint Wand**: **SET** (edit bounds) and **RESET** in one tool.
- **Add / Remove Sticks** (and **Stencil Wand**): Refine which tiles belong to the blueprint or projection.
- **Copy Stick** (and **Projection Wand**): Create and modify a **projection**; one paste path for everyone. (Can toggle for cut/paste behavior.)
- **Paste Stick** (and combined tools): Build the projection using your inventory or nearby chests.

## Material Usage
- Pasting consumes items from your inventory or an adjacent inventory (Blueprint or Projection).
- Toggling the projection to cut/paste behavior will clear the source area before pasting and use the materials from the source area before falling back to the inventory.

## Installation
1. Ensure you have tModLoader installed.
2. Download **The Architect's Stencil** from the in-game Mod Browser or your chosen source.

## Usage
- Use the **Selector Stick** or **Blueprint Wand** to define and edit the blueprint region.
- Use **Add** / **Remove** (or **Stencil Wand**) to tune the tile mask on the blueprint or projection.
- Use **Copy** (or **Projection Wand**) to place and drag a projection.
- Toggle **Cut** on the projection to enable cut/paste behavior.
- Use **Paste** to run the build.

## Version History
- **0.1.1**: Initial Blueprint and selection mechanic implemented.
- **0.1.2**: Projections and paste using inventory materials.

## License
This project is licensed under the MIT License. See the LICENSE file for details.
