
Simple Issues:
- [ ] Export logic should allow selecting a selection the same way Import logic does.

High Priority Issues:
- [ ] Initial calculation of operations causes lag. The current implementation is very inefficient.
- [ ] Looks like plants are attempted to be placed during the block layer, or at the very least blue mushrooms. It is possible that single-plants are being mistaken for tiles, instead of being treated as tile-anchored entities. (Similar issue has been observed with candle objects.)

Low Priority Issues:
- [ ] TAB is not very mod resistant. There is a need for a payload metadata field that tracks what mods the payload has been created with. For now, only mark mods at the time of export. In the future, each tile will be checked for modded content.
    - [ ] Make TAB resistant to missing mods. It should be able to fully process the payload with only skipping the tiles with missing mods. And if forced by cheat sweep, the missing tiles should be handled by UnknownLayer that places tmodloader's missing tiles. Most preferably, the payload should be checked before being assigned to a projection, and checked for incompatibilities and replaced with the tmod's missing tiles that will store the tile's data.
- [ ] During export, the input field does not react to key strokes. Making it impossible to choose the name of the project.
    - [#] New issue, when used command line export, hitting enter immediately confirms the export, without allowing to choose the name of the file.
    - [ ] Works just fine, only the UI needs to be adjusted. Not a priority.
- [ ] At greater scales, moving or scaling projection or selection causes severe lag. Export does the same. tested with a payload of 548x339 (213mb)
    - Yet to be tested, not a priority.
- [ ] When clearing, the process seems to get stuck on inventories. This should be fixed by skipping the tile after the first failed attempt to clear it and continuing with the next tile. Also, some information about the failure should be printed into the chat, so that the user can fix the issue by hand if they want to.
- [ ] Test inventory functionality when item is suddenly removed during operation. (inventory is now cached, but the inventory system should be able to handle it.)

Fixed Issues:
- [#] Pasting a projection created from an import causes a 'Duplicate paste Relative (0,0); each cell must appear once.' error.
 - Very likely an issue with the PasteTileLookup system still requiring a Source Tile and Source Area to work with, even though when importing a payload the source area should be empty.
 - The previous source Area persists within the projection after being a subject of a payload import.
 - Add a warning when user tries to switch
- [#] New BlueprintSystem lookup system is incorrectly handling creation of a new projection. Instead of a new projection, the last projection is being targeted.
- [#] Pasting a projection created from an import starts the pasting operation, but nothing happens:
- [#] Cut Source does not work as expected. Only select few tiles are scheduled for clearing.
- [#] Payload metadata does not contain the player name.
    - Added player name to the payload metadata.
- [#] Seems like the projection overlay is incompatible with the mod Terraria Overhaul. Some of the TAB's overlays are rendered with the Game layer, which seems Terraria Overhaul changes in some way. The incorrectly behaving overlays are:
 - Projection bounds (the borders work just fine during paste)
 - Tile overlays during paste
    - Issue seems to have been solved, not a priority.
- [#] Import seems to register every payload from the folder instead of only listing names and registering the selected payload.
    - fixed by utilizing JsonTextReader to peak into the metadata only, instead of loading the entire payload into memory. Modified payload format into a compact json to reduce file size and loading times.


- [ ]

Todo:
- [#] System determining tile interactions: DONE Discovery system and Interaction Jason files
- [#] System executing the pasting: DONE Pasting System, Pasting Operation, Pasting Tasks, PasteTiles, Layer System.
- [#] System determining the user selection: DONE Blueprint, Projection
- [#] Change tile storage to a 2D array: DONE (Selection, Projection snapshot mask, Payload)
- [#] split forceTiles config into ignoreItems, ignoreInvalid: DONE
- [#] Implement Projection Sprite Overlay: DONE
- [#] Importing local structures: DONE - Issues still present.
- [ ] Implement Mod resistance: TODO -
- [ ] Implement Map Compatibility: TODO - Map overlay compatibility.
- [ ] Finalize Selection Overlay: TODO - Scaling UI needs the handles to be more visible.
- [ ] Implement Rotate and Mirror projection: TODO
- [ ] The Architect Blueprint GUI: TODO
- [ ] Implement a proper testing pipeline: TODO
    - Commands that import a structure, and do various operations on it. Debug stick only runs these commands.
- [#] Rename the mod to The Architect's Blueprint?: DONE
- [#] Extract Selection/Projection backend into a Blueprint data structure: DONE
- [ ] TAB Items: In Progress - Tier 1-3 Implemented. Tier 4-5 waits for GUI.
Tier 1 have temporary sprites, other tiers do not fit.
- [ ] Add Metadata to the export json files: TODO
- [ ] Verify functionality of network features: TODO - Host server or dedicated server should be the only one processing paste requests.
- [ ] Importing remote structures: TODO - Remote files through discord??
- [ ] Make the payload even more compact by utilizing terraria's binary format for the tile data instead of string.
