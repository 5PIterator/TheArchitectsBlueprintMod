# The Architect's Stencil

## Overview
The Architect's Stencil (`ArchitectsStencil`) is a Terraria mod that lets you capture a region of the world as a *Selection* and place copies of it elsewhere using the materials you already own. It focuses on intuitive selection tools and clear visual feedback so larger builds feel fast instead of fiddly.

## Core Concepts
- **Selection**: The rectangular region you define for The Architect's Stencil to work with. Individual tiles within it can be included or excluded from operations.
- **Projection**: A movable ghost of a Selection that shows where the structure will be built.
- **Tools**: A family of sticks and wands that handle selecting, editing, copying, and pasting structures.

## Features
- **Pin Stick (PIN)**: Start a fresh selection and **RESET** it from the same item.
- **Selection Wand**: **SET** (edit bounds) and **RESET** in one tool.
- **Add / Remove Sticks** (and **Inclusion Wand**): Include or exclude tiles within the selection or a projection.
- **Copy Stick** (and **Projection Wand**): Create and modify a **projection**; one paste path for everyone. (Can toggle for cut/paste behavior.)
- **Paste Stick** (and combined tools): Build the projection using your inventory or nearby chests.

## Material Usage
- Pasting consumes items from your inventory or an adjacent inventory (Selection or Projection).
- Toggling the projection to cut/paste behavior will clear the source area before pasting and use the materials from the source area before falling back to the inventory.

## Installation
1. Ensure you have tModLoader installed.
2. Download **The Architect's Stencil** from the in-game Mod Browser or your chosen source.

## Usage
- Use the **Pin Stick** or **Selection Wand** to define and edit the selection region.
- Use **Add** / **Remove** (or **Inclusion Wand**) to include or exclude tiles on the selection or projection.
- Use **Copy** (or **Projection Wand**) to place and drag a projection.
- Toggle **Cut** on the projection to enable cut/paste behavior.
- Use **Paste** to run the build.

## Version History
- **0.1.1**: Initial selection and inclusion mechanic implemented.
- **0.1.2**: Projections and paste using inventory materials.
- **0.1.3**: Semantic rename — Selection replaces Blueprint; Include/Exclude replace Add/Remove.
