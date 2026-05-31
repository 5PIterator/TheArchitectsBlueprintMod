I am in need of consistent utilization of this data structure throughout the codebase. I believe that there are some obsolete uses of a dense 2D array of tiles, instead of a sparse list of cells each with coordinates.

Current structure:
- Metadata: This is a struct that contains the metadata for the payload. It is used for some general information about the payload, such as the project name, player name, creation date, mod version and other.
- Payload: This is a struct that contains the cells of the payload. It is used for storing the tile data and the wall data.
- Example Payload:
```
{
    "metadata": {
        "projectName": "My Project",
        "playerName": "My Player",
        "createdUtc": "2026-05-24T00:00:00.0000000Z",
        "modVersion": "1.0.0"
    },
    "payload": [
        { // Missing cell coordinates are interpreted as empty cells.
            "x": 22,
            "y": 0,
            "c": { // Empty cells are interpreted as air.
                "tile": "Platforms",
                "wall": "MushroomWall",
                "tFX": 54,
                "tFY": 324,
                "wFX": 180,
                "wFY": 72,
                "wFN": 2,
                "style": 18,
                "alt": 3,
                "flags": 1
            }
        }
    ]
}
```