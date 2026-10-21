# TAB Modular Layer Fixture

Canonical paste fixture for layer coverage: [`TABModularTest.tab`](TABModularTest.tab).

Driven by Case `paste_P04` (`/tab.test all` or `/tab.test run paste_P04`).

## Layout

3×4 grid of **7×7** modules (21×28 tiles total). Coordinates are **column,row from the bottom-left** of the payload.

| | Col 1 | Col 2 | Col 3 | Col 4 |
|---|---|---|---|---|
| **Row 3** | Empty (ignore) | Slope — dynasty wood slopes / half-blocks | Paint — cloud blocks/walls with paints and coatings | Tree — cactus, three trees, bamboo |
| **Row 2** | Modded/Unloaded (ignore) | Plant — plant frames, vines, plant blocks | Wire — wires and wire-related frames | Liquid — liquids, blocks, coatings |
| **Row 1** | Entity — gold room: mannequin, sign, chest, item frame | Block — blocks, grass, wand placement | Wall — walls and blocks | Frame — stacked frames 1×1, 2×2, 2×3 |

### Active modules

- `1,1` Entity
- `1,2` Block
- `1,3` Wall
- `1,4` Frame
- `2,2` Plant
- `2,3` Wire
- `2,4` Liquid
- `3,2` Slope
- `3,3` Paint
- `3,4` Tree

### Ignored modules

- `2,1` Modded/Unloaded — non-functional placeholder
- `3,1` Empty — intentionally bare

Journey mode (or a full material loadout) is required for a clean world diff after paste.
