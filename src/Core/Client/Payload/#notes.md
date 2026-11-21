# Payload entities (TE-01)

Capture tiles and entities separately. Referencing matches Terraria: entities are keyed by
**payload-relative coordinates** (`ByPosition` / `Chest.FindChest` style). Cells stay
tile-only — no entity id on the cell. World runtime ids are not part of the blueprint contract.

Unified envelope: `(x, y, n, d)` where `n` is FullName and `d` is an opaque TagCompound.

Synthetic names: `Terraria:Chest`, `Terraria:Sign`. TileEntities use their FullName
(`Terraria:TEDisplayDoll`, mod `Mod:Name`, etc.).

In memory after load: `Dictionary<(x,y), PayloadEntity>` via `Payload.TryGetEntity`.

Security is enforced in three layers:
- At capture (client building the payload) — e.g. large chest stacks
- At import (external payload load) — maliciously edited documents
- On the server when receiving a paste packet

Paste applies entity `d` through the Entity layer (disabled by default). Place/Clear run after/before Frame
and currently override live contents without consuming inventory (Needs Items = false / Sweep-equivalent).
Sweep (TE-03) always runs a post-blit entity step: purge hosts in the footprint, then blind-create and
apply every payload sidecar at exact Relative (ghosts allowed; no TopLeft placement hooks).

Proper item consumption is planned separately:
- Clear + Store: paste arrows that pull items out of the entity into player/storage.
- Place + RequireItems: paste arrows that consume inventory and insert into the entity.
Until then, EntityLayer wipes then LoadData/chest-slot writes (replace, not merge).

Capture and apply share a generic TileEntity path (`TileObjectData.TopLeft`, `SaveData`/`LoadData`)
with a FullName table of custom handlers: `Terraria:Chest`, `Terraria:Sign`, and `TEDisplayDoll`
(full `ItemIO` capture). Hosts are created by Frame PlaceObject via `TileObject.Place` plus
`TileObjectData.CallPostPlacementPlayerHook` (`HookPostPlaceMyPlayer`), the same sequence as
player `PlaceThing`. `WorldGen.PlaceObject` writes tiles only and never runs that hook.
EntityLayer finds that live host and wipes/writes sidecar `d`. A missing or mismatched host is
`InvalidPlacement`; this layer does not create chests, signs, or TileEntities.

`TEItemFrame` stores the displayed item in public `item`; `SaveData` writes `d.item` via `ItemIO`.
`TEDisplayDoll` stores private `_items` / `_dyes` (8+8); vanilla `SaveData` uses `PlayerIO.SaveInventory`
(mod items only), so capture rewrites those lists as chest-style identity slots.

### Entity item identity (no GlobalItem)

Entity item tags store **identity only** (`item` name / stack / prefix) — the same model as chests:

- **Chests and DisplayDoll** emit sparse identity slot lists. Legacy ItemIO slots (`mod`/`id`/`globalData`)
  are converted once at sanitize; unresolved slots are omitted and logged under `EntityCapture`.
- **TE `d.item`** (e.g. item frames) must stay ItemIO-shaped for `LoadData`, but sanitize rewrites the blob
  without `globalData` so JSON-widened GlobalItem fields (e.g. SellAll) cannot break place/preview.
- **Preview** (`EntityPreview`) and **place** (`EntityPlace`) best-effort load identity tags, log misses,
  and continue other slots. Display dolls use per-slot apply; generic TE `LoadData` is try/caught.

Log categories: `EntityCapture`, `EntityPlace`, `EntityPreview`.

Example:

```json
{
  "metadata": { "...": "..." },
  "payload": [
    { "x": 22, "y": 0, "c": { "tile": "Containers", "style": 0, "tFX": 0, "tFY": 0 } },
    { "x": 23, "y": 0, "c": { "tile": "Containers", "style": 0, "tFX": 18, "tFY": 0 } },
    { "x": 22, "y": 1, "c": { "tile": "Containers", "style": 0, "tFX": 0, "tFY": 18 } },
    { "x": 23, "y": 1, "c": { "tile": "Containers", "style": 0, "tFX": 18, "tFY": 18 } }
  ],
  "entities": [
    {
      "x": 22,
      "y": 0,
      "n": "Terraria:Chest",
      "d": {
        "name": "Loot",
        "items": [
          { "slot": 0, "item": "GoldCoin", "stack": 100 },
          { "slot": 5, "item": "Torch", "stack": 99, "prefix": 0 }
        ]
      }
    }
  ]
}
```
