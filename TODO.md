- Pasting a projection created from an import causes a 'Duplicate paste Relative (0,0); each cell must appear once.' error.
 - Very likely an issue with the PasteTileLookup system still requiring a Source Tile and Source Area to work with, even though when importing a payload the source area should be empty.
 - The previous source Area persists within the projection after being a subject of a payload import.
 - Add a warning when user tries to switch


- New BlueprintSystem lookup system is incorrectly handling creation of a new projection. Instead of a new projection, the last projection is being targeted.