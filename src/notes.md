Code Base changes:
- change tile storage to a 2D array, instead of rectangle + list of tiles. It is better for consistency, and makes later implementation of scaling and mirroring much simpler.
- split forceTiles config into ignoreItems, ignoreInvalid. These settings are clear in what they do. IgnoreItems will not schedule tasks that are missing items to place, and mark as successful if placement failed due to missing items. ignoreInvalid will do the same for when the tile cannot be placed.


Semantic changes:
- Finalize color scheme:
PIN → Diamond White
SET → Sapphire Blue
CPY → Amethyst Purple
- Inventory LightBrown
- Clear Light Red
- Place Light Blue
CUT → Ruby Red
- source area Red
PST → Emerald Green

INC → Topaz Yellow
EXC → Amber Orange
RST → Obsidian Gray

- rename ADD/REMOVE to INCLUDE/EXCLUDE (INC/EXC)
- Adjust wording and terminology for the three main systems. Selection, Projection and Execution.
- Selection: The user-selected region TAS will work with. Client-side. (Currently Blueprint, Source Area and related systems)
- Projection: A movable instance of the blueprint data at a target location. Client-side. (Currently Projection, Target Area and related systems)
- Execution: The process of pasting the selection into the world. Server-side. (Currently Pasting System, Pasting Operation, Pasting Tasks, PasteTiles, Layer System)

Item recipe changes:
[Living Loom]
PinStick: 1x Diamond + 5x Feywood
CopyStick: 1x Amethyst + 5x Feywood
PasteStick: 1x Emerald + 5x Feywood
IncludeStick: 1x Topaz + 5x Feywood
ExcludeStick: 1x Amber + 5x Feywood

[Living Loom]
SelectionWand: 1x PinStick + 1x Sapphire + 1x Wand of Frosting + 25x Feywood
ProjectionWand: 1x CopyStick + 1x Ruby + 1x Axe of Regrowth + 25x Feywood
InclusionWand: 1x IncludeStick + 1x ExcludeStick + 1x Amber Staff + 25x Feywood
ExecutionWand: 1x PasteStick + 1x Obsidian + 1x Flask of Fire + 25x Feywood

[Demon Altar/Crimson Altar]
BlueprintCore: 1x Large Diamond + 1x Large Amethyst + 1x Large Emerald + 1x Large Sapphire + 1x Large Ruby + 1x Large Topaz + 1x Large Amber + 1x Bloody Spine/Worm Food + 50x Star Cloud + 25x Feywood

[Tinkerer's Workshop]
ArchitectsStencil: 1x SelectionWand + 1x ProjectionWand + 1x InclusionWand + 1x ExecutionWand + 1x BlueprintCore + 50x Feywood

[Hellforge]
ArchitectsHand: 1x ArchitectsStencil + 1x Hand of Creation + 100x Feywood + 100x Aetherium Block

[Shimmer]
ArchitectsBlueprint: 1x ArchitectsHand + Shimmer Transformation

1.4.9 mod compatibility:
 - Feywood -> Wood
 - Cloud -> Star Cloud


Todo:
- System determining tile interactions: DONE Discovery system and Interaction Jason files
- System executing the pasting: DONE Pasting System, Pasting Operation, Pasting Tasks, PasteTiles, Layer System.
- System determining the user selection: DONE Blueprint, Projection
- Importing structures: TODO - Selection UI listing local files. Remote files through discord??
- Rotate and mirror: TODO
- The Architect Stencil GUI: TODO
- TAS Items: In Progress - Tier 1-3 Implemented. Tier 4-5 waits for GUI.
Tier 1 have temporary sprites, other tiers do not fit.
