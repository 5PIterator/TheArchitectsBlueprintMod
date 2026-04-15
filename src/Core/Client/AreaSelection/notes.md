The current overlay handling for projection is messy.

The projection needs to relay state information about these things to the client:
- Is locked
    - Conveyed by transparent bounds (Only border is visible for locked, both border and fill are visible for unlocked)
    - Handled by the ProjectionSystem
- Is cutting source
    - Conveyed by the color of the bounds of the projection (red for CUT, lime green for COPY)
    - Handled by the ProjectionSystem
- The source area it is cutting from
    - Conveyed by the gray bounds of SourceArea
    - Handled by the ProjectionSystem
- Deselected tiles
    - Conveyed by the black fill of the deselected tiles
    - Handled by the SelectionSystem. Since the fill is transparent, black, it should be agnostic of the projection's color.

The projection should also relay the state of the paste operation to the client:
- The Phase of the operation
    - Conveyed by the color of the pending tiles (lime green for Place, orange for Clear)
    - Prepare stage is not visialized. during this stage, the projection should be transparent through the Is Locked state.
    - Handled by the PastingSystem
- All remaining Clear/Place Tasks of the current layer
    - Conveyed by the transparency of the tiles (Pending: 0.35, In Progress: 0.6, Done: 0.9)
    - Handled by the PastingSystem
