Code Base changes:
- change tile storage to a 2D array, instead of rectangle + list of tiles. It is better for consistency, and makes later implementation of scaling and mirroring much simpler.
- split forceTiles config into ignoreItems, ignoreInvalid. These settings are clear in what they do. IgnoreItems will not schedule tasks that are missing items to place, and mark as successful if placement failed due to missing items. ignoreInvalid will do the same for when the tile cannot be placed.


Todo:
- System determining tile interactions: DONE Discovery system and Interaction Jason files
- System executing the pasting: DONE Pasting System, Pasting Operation, Pasting Tasks, PasteTiles, Layer System.
- System determining the user selection: DONE Blueprint, Projection
- Change tile storage to a 2D array: DONE (Selection, Projection snapshot mask, Payload)
- split forceTiles config into ignoreItems, ignoreInvalid: DONE
- Importing local structures: DONE - Selection UI listing local files.
- Implement a proper testing pipeline: TODO
    - Commands that import a structure, and do various operations on it. Debug stick only runs these commands.
- Implement Map Compatibility: TODO - Map overlay compatibility.
- Implement Projection Sprite Overlay: TODO
- Finalize Selection Overlay: TODO - Scaling UI needs the handles to be more visible.
- Implement Rotate and Mirror projection: TODO
- The Architect Blueprint GUI: TODO
- Rename the mod to The Architect's Blueprint?: DONE
- Extract Selection/Projection backend into a Blueprint data structure: TODO
- ABP Items: In Progress - Tier 1-3 Implemented. Tier 4-5 waits for GUI.
Tier 1 have temporary sprites, other tiers do not fit.
- Add Metadata to the export json files: TODO
- Importing remote structures: TODO - Remote files through discord??
