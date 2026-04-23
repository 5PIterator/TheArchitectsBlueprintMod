Issues:
- #Flip the points pin starts with. During set, the points visibly flip to the opposite side, which is jarring.
- #When removing a selection by new PIN gesture, the excluded tiles are not removed (Or at least not the overlay).
- #Excluded tiles within the projection are still being pasted. Selection works fine.
- FallBlocks layer is not processing clear tasks.
- #Clear/Place tasks have incorrect colors. Clear should be light red, place should be light blue. Currently they are yellow and green respectively.
- Consider removing Paste's custom behavior of holding both mouse buttons to stop pasting. Instead, move that responsibility under the RESET (RST) Action. When the PASTE action is used with Right click, it will instead invoke the RST action. There could be a possible hazard that the user accidentally clicks twice, which would additionally reset the projection underneath.
- #Inventory overlay should have similar behavior to the projection overlay: Full bounds when idle, and only border when locked (pasting).