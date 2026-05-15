
Issues:
- [#] Pasting a projection created from an import causes a 'Duplicate paste Relative (0,0); each cell must appear once.' error.
 - Very likely an issue with the PasteTileLookup system still requiring a Source Tile and Source Area to work with, even though when importing a payload the source area should be empty.
 - The previous source Area persists within the projection after being a subject of a payload import.
 - Add a warning when user tries to switch

- [#] New BlueprintSystem lookup system is incorrectly handling creation of a new projection. Instead of a new projection, the last projection is being targeted.

- [ ] Pasting a projection created from an import starts the pasting operation, but nothing happens:
[20:53:50.807] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Paste started: sourceClear=False (projection=False, debug=False), targetArea=(3205,322) 7x8
TheArchitectsBlueprintMod: [20:53:50.807] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Paste started: sourceClear=False (projection=False, debug=False), targetArea=(3205,322) 7x8
[20:53:50.809] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] START | area=(3205,322) 7x8 | sourceClear=False | srcTiles=56 tgtTiles=56
TheArchitectsBlueprintMod: [20:53:50.809] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] START | area=(3205,322) 7x8 | sourceClear=False | srcTiles=56 tgtTiles=56
[20:53:50.810] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Stage: PREP (building queues: PlaceSource, then ClearTarget, then optional SourceAreaClear)
TheArchitectsBlueprintMod: [20:53:50.810] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Stage: PREP (building queues: PlaceSource, then ClearTarget, then optional SourceAreaClear)
[20:53:50.813] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: begin prepTileBudget=50 sourceTiles=56 lookupTiles=56 targetTiles=56 transparent=False sourceAreaClear=False
TheArchitectsBlueprintMod: [20:53:50.813] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: begin prepTileBudget=50 sourceTiles=56 lookupTiles=56 targetTiles=56 transparent=False sourceAreaClear=False
[20:53:50.819] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Prep: ClearTarget — queuing clears at destination
TheArchitectsBlueprintMod: [20:53:50.819] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Prep: ClearTarget — queuing clears at destination
[20:53:50.820] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage PlaceSource -> ClearTarget
TheArchitectsBlueprintMod: [20:53:50.820] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage PlaceSource -> ClearTarget
[20:53:50.821] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: batch stage=PlanClear progress=2x6 unitsThisFrame=50 remainingBudget=0
TheArchitectsBlueprintMod: [20:53:50.821] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: batch stage=PlanClear progress=2x6 unitsThisFrame=50 remainingBudget=0
[20:53:50.823] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage ClearTarget -> SourceAreaClear (opaque)
TheArchitectsBlueprintMod: [20:53:50.823] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage ClearTarget -> SourceAreaClear (opaque)
[20:53:50.824] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage SourceAreaClear skipped (no source clear) -> Done
TheArchitectsBlueprintMod: [20:53:50.824] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage SourceAreaClear skipped (no source clear) -> Done
[20:53:50.825] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Prep: queues ready | placeBatches=0 clearBatches=0
TheArchitectsBlueprintMod: [20:53:50.825] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Prep: queues ready | placeBatches=0 clearBatches=0
[20:53:50.826] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: complete placeBatches=0 clearBatches=0
TheArchitectsBlueprintMod: [20:53:50.826] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: complete placeBatches=0 clearBatches=0
[20:53:50.844] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Stage: EXEC CLEAR — running queued clear tasks
TheArchitectsBlueprintMod: [20:53:50.844] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Stage: EXEC CLEAR — running queued clear tasks
[20:54:20.381] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Paste started: sourceClear=False (projection=False, debug=False), targetArea=(3194,332) 7x8
TheArchitectsBlueprintMod: [20:54:20.381] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Paste started: sourceClear=False (projection=False, debug=False), targetArea=(3194,332) 7x8
[20:54:20.382] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] START | area=(3194,332) 7x8 | sourceClear=False | srcTiles=56 tgtTiles=56
TheArchitectsBlueprintMod: [20:54:20.382] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] START | area=(3194,332) 7x8 | sourceClear=False | srcTiles=56 tgtTiles=56
[20:54:20.383] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Stage: PREP (building queues: PlaceSource, then ClearTarget, then optional SourceAreaClear)
TheArchitectsBlueprintMod: [20:54:20.383] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Stage: PREP (building queues: PlaceSource, then ClearTarget, then optional SourceAreaClear)
[20:54:20.386] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: begin prepTileBudget=50 sourceTiles=56 lookupTiles=56 targetTiles=56 transparent=False sourceAreaClear=False
TheArchitectsBlueprintMod: [20:54:20.386] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: begin prepTileBudget=50 sourceTiles=56 lookupTiles=56 targetTiles=56 transparent=False sourceAreaClear=False
[20:54:20.389] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Prep: ClearTarget — queuing clears at destination
TheArchitectsBlueprintMod: [20:54:20.389] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Prep: ClearTarget — queuing clears at destination
[20:54:20.390] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage PlaceSource -> ClearTarget
TheArchitectsBlueprintMod: [20:54:20.390] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage PlaceSource -> ClearTarget
[20:54:20.391] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: batch stage=PlanClear progress=2x6 unitsThisFrame=50 remainingBudget=0
TheArchitectsBlueprintMod: [20:54:20.391] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: batch stage=PlanClear progress=2x6 unitsThisFrame=50 remainingBudget=0
[20:54:20.407] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage ClearTarget -> SourceAreaClear (opaque)
TheArchitectsBlueprintMod: [20:54:20.407] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage ClearTarget -> SourceAreaClear (opaque)
[20:54:20.408] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage SourceAreaClear skipped (no source clear) -> Done
TheArchitectsBlueprintMod: [20:54:20.408] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: stage SourceAreaClear skipped (no source clear) -> Done
[20:54:20.409] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Prep: queues ready | placeBatches=0 clearBatches=0
TheArchitectsBlueprintMod: [20:54:20.409] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Prep: queues ready | placeBatches=0 clearBatches=0
[20:54:20.410] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: complete placeBatches=0 clearBatches=0
TheArchitectsBlueprintMod: [20:54:20.410] [Main Thread/DEBUG] [TheArchitectsBlueprintMod]: [Paste] Prep: complete placeBatches=0 clearBatches=0
[20:54:20.411] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Stage: EXEC CLEAR — running queued clear tasks
TheArchitectsBlueprintMod: [20:54:20.411] [Main Thread/INFO] [TheArchitectsBlueprintMod]: [Paste] Stage: EXEC CLEAR — running queued clear tasks


Todo:
- [#] System determining tile interactions: DONE Discovery system and Interaction Jason files
- [#] System executing the pasting: DONE Pasting System, Pasting Operation, Pasting Tasks, PasteTiles, Layer System.
- [#] System determining the user selection: DONE Blueprint, Projection
- [#] Change tile storage to a 2D array: DONE (Selection, Projection snapshot mask, Payload)
- [#] split forceTiles config into ignoreItems, ignoreInvalid: DONE
-> [ ] Importing local structures: TODO - Issues still present.
- [ ] Implement a proper testing pipeline: TODO
    - Commands that import a structure, and do various operations on it. Debug stick only runs these commands.
- [ ] Implement Map Compatibility: TODO - Map overlay compatibility.
- [ ] Implement Projection Sprite Overlay: TODO
- [ ] Finalize Selection Overlay: TODO - Scaling UI needs the handles to be more visible.
- [ ] Implement Rotate and Mirror projection: TODO
- [ ] The Architect Blueprint GUI: TODO
- [#] Rename the mod to The Architect's Blueprint?: DONE
- [#] Extract Selection/Projection backend into a Blueprint data structure: DONE
- [ ] TAB Items: In Progress - Tier 1-3 Implemented. Tier 4-5 waits for GUI.
Tier 1 have temporary sprites, other tiers do not fit.
- [ ] Add Metadata to the export json files: TODO
- [ ] Importing remote structures: TODO - Remote files through discord??
