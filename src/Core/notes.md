Issues:
- There is an issue with the scheduling system. There is a large lag spike even for a moderately sized payload. It is either due to the identification process, or the BuildLookup system.
- The mushroom plants are being placed during the block layer, instead of the tileAnchored layer. This is likely due to the fact that the mushroom plants are being treated as tiles, instead of being treated as tile-anchored entities.
    - Possible solution, group all tile-anchored entities into a single layer called ObjectLayer. While some objects depend on other objects to be placed first, this can be handled by simply re-trying or skipping until all objects are placed.
- The browser's text input field is not working correctly. the hint text is overriding the input text, and the layout is wrong. Investigate how the native duplication menu in journey mode is implemented.
- Transparent paste is bypassed by sweep layer.
- The Browser should indicate which payload is currently selected in the UI.
- The browser should have three types of payloads that can be filtered by: Session, Local, and Remote.
- Pasting empty payload does not clear the area.