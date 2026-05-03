---
name: Architect items crafting
overview: Add vanilla-station crafting recipes from [Content/Items/notes.md](Content/Items/notes.md) via `ModItem.AddRecipes()`, introduce the missing `ArchitectsHand` item required for Tier 4–5, and register Tier 5 as a Shimmer transform from Hand to Soul (not a workbench recipe).
todos:
  - id: add-architects-hand
    content: Add ArchitectsHand ModItem (accessory defaults), texture, localization, and ShimmerTransformToItem -> ArchitectsSoul in SetStaticDefaults
    status: completed
  - id: recipes-tier1-4
    content: Add AddRecipes() to Pin/Add/Remove/Copy/Paste sticks, three Tier-2 wands, ArchitectsStencil, and ArchitectsHand with correct tiles and ingredients
    status: completed
  - id: verify-build-ids
    content: Build mod and fix any ItemID/TileID naming mismatches for current tML/Terraria version
    status: completed
isProject: false
---

# Crafting implementation: Architect’s Stencil items

## Current state

- All tool items under [`Content/Items/`](Content/Items/) are plain `ModItem` classes with **no** `AddRecipes()` (repo-wide search shows no `CreateRecipe` usage in mod code).
- [`Content/Items/Tier4/ArchitectsSoul.cs`](Content/Items/Tier4/ArchitectsSoul.cs) exists; **`ArchitectsHand` does not** — it must exist before Tier 4 crafting and the Tier 5 Shimmer step can work.
- Root mod class [`src/ArchitectsStencilMod.cs`](src/ArchitectsStencilMod.cs) stays minimal; recipes belong on items (or a small dedicated system if you prefer consolidation — default here is **per-output `AddRecipes()`** on the item that is crafted, which matches common tModLoader style).

## Recipe matrix (from notes, with one adjustment)

**Adjustment:** Wand of Sparking is **not** in the `ArchitectsStencil` recipe; it is required only for **`PinStick`** (Living Loom), so the wand is “baked into” the pin-tool line from the start of the chain.

| Tier | Station | Output | Ingredients |
|------|---------|--------|-------------|
| 1 | Living Loom | `PinStick` | 1× Diamond + 1× Wand of Sparking |
| 1 | Living Loom | `AddStick` / `RemoveStick` / `CopyStick` / `PasteStick` | 1× gem + 1× Feywood (per stick type) |
| 2 | Tinkerer’s Workshop | `BlueprintWand` | 2× `PinStick` |
| 2 | Tinkerer’s Workshop | `StencilWand` | 1× `AddStick` + 1× `RemoveStick` |
| 2 | Tinkerer’s Workshop | `ProjectionWand` | 1× `CopyStick` + 1× `PasteStick` + 1× Ruby |
| 3 | Tinkerer’s Workshop | `ArchitectsStencil` | 1× each: `BlueprintWand`, `StencilWand`, `ProjectionWand`, Actuation Rod |
| 4 | Tinkerer’s Workshop | `ArchitectsHand` | 1× `ArchitectsStencil` + 1× Hand of Creation + 25× Feywood + 25× Aetherium Block |
| 5 | Shimmer | `ArchitectsSoul` | **Not a recipe** — Shimmer transform: 1× `ArchitectsHand` → `ArchitectsSoul` |

Progression labels in the notes (“Exploration / Goblin / Dungeon …”) read as **design guidance**, not automatic gates. This plan only encodes **stations + ingredients**. If you later want `Condition` gates (e.g. defeated Goblin Army), that would be a separate additive pass.

## Vanilla IDs to use (verify against your tML/Terraria reference assembly)

- **Stations:** `TileID.LivingLoom`, `TileID.TinkerersWorkshop`.
- **Gems:** `ItemID.Diamond`, `ItemID.Topaz`, `ItemID.Amethyst`, `ItemID.Emerald`, `ItemID.Sapphire`, `ItemID.Ruby`.
- **Wood / block:** `ItemID.Feywood`, `ItemID.AetheriumBlock` (names must match your game version; if a name fails to compile, use the wiki/IDE symbol list for the exact `ItemID` member).
- **Other vanilla:** `ItemID.WandofSparking`, `ItemID.ActuationRod`, `ItemID.HandOfCreation`.
- **Mod ingredients:** `ModContent.ItemType<...>()` for sticks, wands, and stencil.

## Implementation steps

### 1. Add `ArchitectsHand` item

- New file e.g. [`Content/Items/Tier4/ArchitectsHand.cs`](Content/Items/Tier4/ArchitectsHand.cs) (or align folder with your tier convention).
- **`SetDefaults`:** configure as an **accessory** (`Item.accessory = true`, appropriate width/height, rarity/value, `Item.UseSound` if needed) so it can sit in an accessory slot as described in the notes. **Gameplay** (“Hand of Creation effects + GUI”) is called out in notes but is separate from crafting; the minimum for this task is a valid item type with correct slot behavior and stats consistent with other Tier-4 gear.
- **`SetStaticDefaults`:** register Shimmer output to Soul:

```csharp
ItemID.Sets.ShimmerTransformToItem[Type] = ModContent.ItemType<ArchitectsSoul>();
```

(Place on `ArchitectsHand` so throwing the Hand into Shimmer yields Soul, matching “Tier 5: ArchitectsSoul = 1× ArchitectsHand”.)

### 2. Register workbench recipes (`CreateRecipe`)

On **each crafted output**’s `ModItem`, add:

```csharp
public override void AddRecipes()
{
    CreateRecipe()
        .AddIngredient(...)
        .AddTile(TileID.LivingLoom) // or TinkerersWorkshop
        .Register();
}
```

Concrete mapping:

- **`PinStick`:** Diamond + Wand of Sparking @ Living Loom.
- **`AddStick`:** Topaz + Feywood @ Living Loom.
- **`RemoveStick`:** Amethyst + Feywood @ Living Loom.
- **`CopyStick`:** Emerald + Feywood @ Living Loom.
- **`PasteStick`:** Sapphire + Feywood @ Living Loom.
- **`BlueprintWand`:** 2× `PinStick` @ Tinkerer’s Workshop.
- **`StencilWand`:** `AddStick` + `RemoveStick` @ Tinkerer’s Workshop.
- **`ProjectionWand`:** `CopyStick` + `PasteStick` + Ruby @ Tinkerer’s Workshop.
- **`ArchitectsStencil`:** three wands + Actuation Rod @ Tinkerer’s Workshop.
- **`ArchitectsHand`:** `ArchitectsStencil` + Hand of Creation + 25 Feywood + 25 Aetherium Block @ Tinkerer’s Workshop.

**`ArchitectsSoul`:** no `CreateRecipe`; only the Shimmer set above.

### 3. Localization

- Add display name (and tooltip if desired) for **`ArchitectsHand`** in [`Localization/en-US_Mods.ArchitectsStencilMod.hjson`](Localization/en-US_Mods.ArchitectsStencilMod.hjson) so the recipe UI and Shimmer result read cleanly.

### 4. Assets

- Add **`ArchitectsHand.png`** next to the new class (same pattern as other items: [`Content/Items/Tier1/PinStick.cs`](Content/Items/Tier1/PinStick.cs) + sibling texture). Reuse sizing conventions from `ArchitectsSoul` (28×28) or match other tools (32×32) for visual consistency.

### 5. Build verification

- `dotnet build` the mod project (per your existing [`ArchitectsStencilMod.csproj`](ArchitectsStencilMod.csproj) + `tModLoader.targets`) and fix any `ItemID` renames your tML version uses.

## Optional follow-ups (out of strict “crafting” scope)

- **Reverse recipes / decrafting:** only if you want parity with vanilla Shimmer decraft behavior; not in the notes.
- **`DebugStick`:** no recipe in notes — leave uncraftable unless you decide otherwise.
- **ArchitectsHand effects** (Hand of Creation mirror + GUI unlock): implement in `UpdateAccessory` / shared player flag / UI hooks after the item exists.
