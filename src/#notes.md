# Internal notes

Documentation index: [docs/README.md](../docs/README.md)  
Pre-release tracker: [docs/TODO.md](../docs/TODO.md)  
Live paste/mapping issues: [src/Core/#notes.md](Core/%23notes.md)  
Browser UI issues: [src/UI/TmodGUI/#notes.md](UI/TmodGUI/%23notes.md)

## Code base changes (historical)

- Tile storage uses a 2D array instead of rectangle + tile list — better for scaling/mirroring later.
- `forceTiles` split into `ignoreItems` and `ignoreInvalid`: clearer semantics for missing items vs invalid placement.
