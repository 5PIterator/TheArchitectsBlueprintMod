# The Architect's Blueprint

**Version 0.2.1 (pre-release)**

Copy a region of your world, refine which tiles belong in the build, and paste a ghost copy elsewhere using materials from your inventory and nearby chests. Save structures locally and load them back later.

## What you can do

- **Select** a rectangle and **include/exclude** tiles inside it
- **Copy** the region into a movable **projection** (ghost preview)
- **Paste** using inventory and adjacent chests; optional **cut** clears the source and reuses those materials first
- **Import/export** local `.tab` payloads via the in-game browser
- Use tools on the **fullscreen map** as well as in the world
- In **multiplayer**, the host or dedicated server runs pastes; other players send requests with a payload
- **Customize paste behavior** by editing interaction maps and running discovery — see [Interaction maps & discovery](docs/INTERACTION_MAPS.md)

## Core concepts

| Term | Meaning |
|------|---------|
| **Selection** | The rectangular region that will be copied and pasted |
| **Projection** | A movable ghost of a selection showing where the build will go |
| **Payload** | A saved structure (`.tab` / `.tab.json`) with name, author, dimensions, and mod list |

## Tools at a glance

| Item | Role |
|------|------|
| **Pin Stick** | Start a new selection (PIN) |
| **Include / Exclude Stick** | Add or remove tiles from the selection or projection (INC/EXC) |
| **Copy Stick** | Create and move a projection (CPY) |
| **Paste Stick** | Run a paste against a projection (PST)|
| **Selection Wand** | Start a new selection and edit its bounds, RESET via action wheel (PIN/SET/RST) |
| **Inclusion Wand** | Include / exclude via action wheel (INC/EXC) |
| **Projection Wand** | Copy, paste, reset via action wheel (CPY/PST/RST) |
| **Execution Wand** | Paste-focused wand (PST/RST)|
| **Blueprint Core** | Crafting ingredient for the Stencil |
| **Architect's Stencil** | All-in-one tool: PIN, SET, INC, EXC, CPY, PST, RST |
| **Architect's Hand** | Mid-game accessory; Provides benefits for both Builder's Hand and opens Architect's GUI |
| **Architect's Blueprint** | Mid-game permanent buff; Turns Architect's Hand into a permanent buff (full behavior not yet implemented) |

Crafting progresses from tier-1 sticks → tier-2 wands + Blueprint Core → Architect's Stencil → Architect's Hand → Architect's Blueprint.

Tool details and action design: [docs/PLAN.md](docs/PLAN.md)

## Quick start

1. Use the **Pin Stick** or **Selection Wand** to define a selection.
2. Use **Include** / **Exclude** to refine which tiles are part of the build.
3. Use **Copy** to place and move a projection.
4. Toggle **cut** on the projection if you want cut/paste behavior.
5. Use **Paste** to build, or export from the payload browser for later.

On tier-2 wands and the **Architect's Stencil**, **right-click** opens the action wheel.

## Save and load

Payloads live in `SavePath/TheArchitectsBlueprintMod/Payloads/` as `.tab` (compact) or `.tab.json` (readable). Open the browser from the projection toolbox to import, export, preview, and filter.

## Not yet / roadmap

See [docs/TODO.md](docs/TODO.md) for the full tracker. Highlights:

- Rotate and mirror projections
- Remote / shared payload import
- Architect's Blueprint GUI and tier-5 behavior
- More visible selection resize handles
- Performance tuning for very large structures
- Stronger mod-compatibility for heavily modded payloads (TAB supports mods natively and discovery should cover most cases, plus the interactions can be modified manually. There is still a chance that some mods will conflict.)

## Documentation

| Guide | Description |
|-------|-------------|
| [docs/INTERACTION_MAPS.md](docs/INTERACTION_MAPS.md) | Custom paste behavior — discovery, SourceMaps, compile |
| [docs/PLAN.md](docs/PLAN.md) | Actions, tools, and target UX |
| [docs/TODO.md](docs/TODO.md) | Pre-release tracker and roadmap |
| [docs/TESTING.md](docs/TESTING.md) | Running headless and in-game tests |
| [docs/README.md](docs/README.md) | Full documentation index |

## Installation

1. Install [tModLoader](https://store.steampowered.com/app/1281930/tModLoader/).
2. Enable **The Architect's Blueprint** from the Mod Browser or your mod source.

## Version history

- **0.2.1 (pre-release)** — Removed the on-screen cursor tile-coordinate overlay.
- **0.2.0 (pre-release)** — Local import/export, payload browser, map tool support, multiplayer paste authority, Architect's Stencil, phased paste with inventory consumption, interaction maps.
- **0.1.x** — Initial selection, inclusion, projection, and paste mechanics.
