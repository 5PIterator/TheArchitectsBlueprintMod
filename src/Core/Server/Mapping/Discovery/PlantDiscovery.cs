#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheArchitectsBlueprintMod.Core.Layers;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers plant-family interactions: vines, thorns, ground plants, sunflower styles, and styled Living Wood Wand trees.
    /// Writes Plants.itr in tile-first format.
    /// </summary>
    internal sealed class PlantDiscovery : DiscoveryBase
    {
        private const string StaffOfRegrowthItem = "StaffofRegrowth";
        private const string LivingWoodWandItem = "LivingWoodWand";
        private const string SunflowerItem = "Sunflower";
        private const int DecorativeYield = -1;
        private const int ConsumableYield = 1;

        private static readonly Dictionary<int, int> VineAnchorTiles = new()
        {
            [TileID.Vines] = TileID.Grass,
            [TileID.VineFlowers] = TileID.Grass,
            [TileID.JungleVines] = TileID.JungleGrass,
            [TileID.HallowedVines] = TileID.HallowedGrass,
            [TileID.CrimsonVines] = TileID.CrimsonGrass,
            [TileID.MushroomVines] = TileID.MushroomGrass,
            [TileID.CorruptVines] = TileID.CorruptGrass,
            [TileID.AshVines] = TileID.AshGrass,
        };

        private static readonly Dictionary<int, int> ThornAnchorTiles = new()
        {
            [TileID.CorruptThorns] = TileID.CorruptGrass,
            [TileID.CrimsonThorns] = TileID.CrimsonGrass,
            [TileID.JungleThorns] = TileID.JungleGrass,
            [TileID.PlanteraThorns] = TileID.JungleGrass,
        };

        private static readonly (int TileId, int AmmoItemId)[] FixedTreeAmmo =
        [
            (TileID.Cactus, ItemID.Cactus),
            (TileID.Bamboo, ItemID.BambooBlock),
            (TileID.TreeAsh, ItemID.AshWood),
            (TileID.TreeAmethyst, ItemID.Amethyst),
            (TileID.TreeTopaz, ItemID.Topaz),
            (TileID.TreeSapphire, ItemID.Sapphire),
            (TileID.TreeEmerald, ItemID.Emerald),
            (TileID.TreeRuby, ItemID.Ruby),
            (TileID.TreeDiamond, ItemID.Diamond),
            (TileID.TreeAmber, ItemID.Amber),
        ];

        /// <summary>Scans plant families and writes Plants.itr to SourceMaps.</summary>
        public static void Run(Mod mod)
        {
            // --- Index placeable items, then discover vines, thorns, plants, sunflower styles, and tree wand pairs.

            ArgumentNullException.ThrowIfNull(mod);

            var outputPath = GetOutputPath("Plants");

            Log($"Plant discovery started. Output: {outputPath}");

            IReadOnlyDictionary<int, string> createTileItems = BuildCreateTileItemIndex();
            var map = new PlacementInteractionMap();
            int vines = DiscoverVines(map, createTileItems);
            int thorns = DiscoverThorns(map, createTileItems);
            int plants = DiscoverGroundPlants(map, createTileItems);
            int sunflowers = DiscoverSunflowerStyles(map);
            int trees = DiscoverTreeWandEntries(map);

            Log($"Discovered vines={vines} thorns={thorns} plants={plants} sunflowerStyles={sunflowers} treeWand={trees}.");
            Save(mod, map);
            Log($"Plant discovery complete. Output: {outputPath}");
        }

        /// <summary>Maps vine and reverse-vine tiles onto Staff of Regrowth with optional seed fallback.</summary>
        private static int DiscoverVines(
            PlacementInteractionMap map,
            IReadOnlyDictionary<int, string> createTileItems)
        {
            // --- Prefer Staff; add a finite seed fallback only when a plant consumable exists.
            Log("Family: Vines");
            int discovered = 0;

            for (int tileId = 0; tileId < TileLoader.TileCount; tileId++)
            {
                if (!IsVineTile(tileId))
                    continue;

                string tileName = ContentNameResolver.GetTileName(tileId);
                AddStaffAndOptionalSeed(map, tileId, createTileItems, preferAnchorSeed: true, tileName);
                discovered++;
            }

            Log($"  Vines summary: {discovered} tiles.");
            return discovered;
        }

        /// <summary>Maps biome thorn bushes onto Staff of Regrowth with matching grass seed fallbacks.</summary>
        private static int DiscoverThorns(
            PlacementInteractionMap map,
            IReadOnlyDictionary<int, string> createTileItems)
        {
            // --- Staff first; Corrupt/Crimson/Jungle seeds charge the finite shopping-list path.

            Log("Family: Thorns");
            int discovered = 0;

            // Emit Staff + biome seed for each known thorn tile.
            foreach (int tileId in ThornAnchorTiles.Keys)
            {
                string tileName = ContentNameResolver.GetTileName(tileId);
                AddStaffAndOptionalSeed(map, tileId, createTileItems, preferAnchorSeed: true, tileName);
                discovered++;
            }

            Log($"  Thorns summary: {discovered} tiles.");
            return discovered;
        }

        /// <summary>Maps PlantLayer-owned tiles onto Staff of Regrowth with optional seed fallback.</summary>
        private static int DiscoverGroundPlants(
            PlacementInteractionMap map,
            IReadOnlyDictionary<int, string> createTileItems)
        {
            // --- Skip placeable tiles except sunflower/Plants; keep classifiers in sync with PlantLayer.
            Log("Family: GroundPlants");
            int discovered = 0;

            // TileID.Plants is skipped by createTile FlowerPackets; emit Staff + GrassSeeds + style-8 Mushroom.
            discovered += DiscoverPlantsTile(map);

            for (int tileId = 0; tileId < TileLoader.TileCount; tileId++)
            {
                if (!PlantLayer.Definition.IsCustom((ushort)tileId))
                    continue;

                if (tileId == TileID.Sunflower || tileId == TileID.Plants)
                    continue;

                // Thorns have a dedicated family with biome seed anchors.
                if (PlantLayer.IsThornTileType((ushort)tileId))
                    continue;

                if (createTileItems.ContainsKey(tileId))
                {
                    LogDebug($"  SKIP {ContentNameResolver.GetTileName(tileId)} ({tileId}): already has createTile item.");
                    continue;
                }

                string tileName = ContentNameResolver.GetTileName(tileId);
                AddStaffAndOptionalSeed(map, tileId, createTileItems, preferAnchorSeed: false, tileName);
                discovered++;
            }

            Log($"  GroundPlants summary: {discovered} tiles.");
            return discovered;
        }

        /// <summary>Emits StaffofRegrowth, GrassSeeds, and style-8 Mushroom rows for TileID.Plants.</summary>
        private static int DiscoverPlantsTile(PlacementInteractionMap map)
        {
            // --- Style-agnostic Staff/GrassSeeds plus exact style 8 Staff/Mushroom for normal mushrooms.

            const int mushroomStyle = 8;
            int added = 0;
            string tileName = ContentNameResolver.GetTileName(TileID.Plants);

            if (ContentNameResolver.ResolveItem(StaffOfRegrowthItem).HasValue)
            {
                map.AddTile(TileID.Plants, InteractionEntry.Create(
                    "Tile",
                    item: StaffOfRegrowthItem,
                    yield: DecorativeYield,
                    style: null));
                map.AddTile(TileID.Plants, InteractionEntry.Create(
                    "Tile",
                    item: StaffOfRegrowthItem,
                    yield: DecorativeYield,
                    style: mushroomStyle));
                added += 2;
                LogDebug($"  {tileName} ({TileID.Plants}) -> {StaffOfRegrowthItem} yield={DecorativeYield} (+style {mushroomStyle})");
            }
            else
            {
                LogWarn($"  WARN {tileName} ({TileID.Plants}): StaffofRegrowth unresolved.");
            }

            if (ContentNameResolver.ResolveItem("GrassSeeds").HasValue)
            {
                map.AddTile(TileID.Plants, InteractionEntry.Create(
                    "Tile",
                    item: "GrassSeeds",
                    yield: ConsumableYield,
                    style: null));
                added++;
                LogDebug($"  {tileName} ({TileID.Plants}) -> GrassSeeds yield={ConsumableYield}");
            }

            if (ContentNameResolver.ResolveItem("Mushroom").HasValue)
            {
                map.AddTile(TileID.Plants, InteractionEntry.Create(
                    "Tile",
                    item: "Mushroom",
                    yield: ConsumableYield,
                    style: mushroomStyle));
                added++;
                LogDebug($"  {tileName} ({TileID.Plants}) style={mushroomStyle} -> Mushroom yield={ConsumableYield}");
            }

            return added > 0 ? 1 : 0;
        }

        /// <summary>Emits the Sunflower item for every TileObjectData placement style.</summary>
        private static int DiscoverSunflowerStyles(PlacementInteractionMap map)
        {
            // --- FrameLayer places sunflowers; style keys must match captured RandomStyleRange variants.
            Log("Family: SunflowerStyles");
            if (!ContentNameResolver.ResolveItem(SunflowerItem).HasValue)
            {
                LogWarn("  SKIP Sunflower: item unresolved.");
                return 0;
            }

            int added = 0;
            foreach (int style in EnumeratePlacementStyles(TileID.Sunflower))
            {
                map.AddTile(TileID.Sunflower, InteractionEntry.Create(
                    "Tile",
                    item: SunflowerItem,
                    style: style,
                    yield: ConsumableYield));
                added++;
                LogDebug($"  Sunflower style {style} -> {SunflowerItem}");
            }

            Log($"  SunflowerStyles summary: {added} styles.");
            return added;
        }

        /// <summary>Emits Living Wood Wand interactions for cactus, bamboo, styled Trees/PalmTree, and gem trees.</summary>
        private static int DiscoverTreeWandEntries(PlacementInteractionMap map)
        {
            // --- Style-keyed Trees/Palm rows plus fixed wand+ammo families (cactus, bamboo, gems).

            Log("Family: TreeWand");
            if (!ContentNameResolver.ResolveItem(LivingWoodWandItem).HasValue)
            {
                LogWarn("  SKIP TreeWand: LivingWoodWand item unresolved.");
                return 0;
            }

            int added = 0;
            for (int i = 0; i < FixedTreeAmmo.Length; i++)
            {
                (int tileId, int ammoItemId) = FixedTreeAmmo[i];
                added += AddTreeWandEntry(map, tileId, ammoItemId, style: 0);
            }

            foreach ((int style, int ammoItemId) in TreeLayer.EnumerateTreesStyleAmmo())
                added += AddTreeWandEntry(map, TileID.Trees, ammoItemId, style);

            foreach ((int style, int ammoItemId) in TreeLayer.EnumeratePalmStyleAmmo())
                added += AddTreeWandEntry(map, TileID.PalmTree, ammoItemId, style);

            for (int tileId = TileID.Count; tileId < TileLoader.TileCount; tileId++)
            {
                if (!TreeLayer.IsTreeType((ushort)tileId))
                    continue;

                int drop = TileLoader.GetItemDropFromTypeAndStyle(tileId, 0);
                if (drop <= 0)
                    drop = ItemID.Wood;

                added += AddTreeWandEntry(map, tileId, drop, style: 0);
            }

            Log($"  TreeWand summary: {added} interactions.");
            return added;
        }

        /// <summary>Adds Staff of Regrowth and an optional finite seed/drop fallback for a plant tile.</summary>
        private static void AddStaffAndOptionalSeed(
            PlacementInteractionMap map,
            int tileId,
            IReadOnlyDictionary<int, string> createTileItems,
            bool preferAnchorSeed,
            string tileName)
        {
            // --- Infinite Staff first; finite seed only when it does not place a different tile family.
            if (ContentNameResolver.ResolveItem(StaffOfRegrowthItem).HasValue)
            {
                map.AddTile(tileId, InteractionEntry.Create(
                    "Tile",
                    item: StaffOfRegrowthItem,
                    yield: DecorativeYield,
                    style: null));
                LogDebug($"  {tileName} ({tileId}) -> {StaffOfRegrowthItem} yield={DecorativeYield}");
            }
            else
            {
                LogWarn($"  WARN {tileName} ({tileId}): StaffofRegrowth unresolved.");
            }

            if (!TryResolveSeedFallback(tileId, createTileItems, preferAnchorSeed, out string seedItem, out string source))
                return;

            map.AddTile(tileId, InteractionEntry.Create(
                "Tile",
                item: seedItem,
                yield: ConsumableYield,
                style: null));
            LogDebug($"  {tileName} ({tileId}) -> {seedItem} yield={ConsumableYield} ({source})");
        }

        /// <summary>Resolves a finite seed/drop fallback that is safe to charge for the plant tile.</summary>
        private static bool TryResolveSeedFallback(
            int tileId,
            IReadOnlyDictionary<int, string> createTileItems,
            bool preferAnchorSeed,
            out string itemName,
            out string source)
        {
            // --- Accept createTile matches, seed-named drops, then vine/thorn host-grass seeds.

            itemName = "";
            source = "";

            if (createTileItems.TryGetValue(tileId, out string? directItem))
            {
                itemName = directItem;
                source = "createTile item";
                return true;
            }

            int dropItemId = TileLoader.GetItemDropFromTypeAndStyle(tileId, 0);
            if (dropItemId > 0 && IsReasonablePlantConsumable(dropItemId, tileId))
            {
                itemName = ContentNameResolver.GetItemName(dropItemId);
                source = "tile drop";
                return true;
            }

            if (preferAnchorSeed
                && TryResolvePlantAnchorSeed(tileId, out string? anchorSeed, out string? anchorName)
                && anchorSeed is not null
                && anchorName is not null)
            {
                itemName = anchorSeed;
                source = $"anchor seed {anchorName}";
                return true;
            }

            return false;
        }

        /// <summary>Resolves a biome grass seed from vine or thorn host-grass anchors.</summary>
        private static bool TryResolvePlantAnchorSeed(int tileId, out string? itemName, out string? anchorName)
        {
            // --- Prefer vine anchors, then thorn anchors, then map host grass to a seed item.

            itemName = null;
            anchorName = null;
            int anchorTileId;
            if (VineAnchorTiles.TryGetValue(tileId, out anchorTileId)
                || ThornAnchorTiles.TryGetValue(tileId, out anchorTileId))
            {
                if (!TryResolveAnchorSeedItem(anchorTileId, out itemName) || itemName is null)
                    return false;

                anchorName = ContentNameResolver.GetTileName(anchorTileId);
                return true;
            }

            return false;
        }

        /// <summary>Whether a drop/create item is a plant consumable for the target tile rather than a grass block seed.</summary>
        private static bool IsReasonablePlantConsumable(int itemId, int plantTileId)
        {
            // --- Reject items that place a different tile; accept seed-named items and exact createTile matches.
            var item = new Item();
            item.SetDefaults(itemId);
            if (item.IsAir || item.type != itemId)
                return false;

            if (item.createTile == plantTileId)
                return true;

            if (item.createTile >= TileID.Dirt && item.createTile != plantTileId)
                return false;

            string name = ContentNameResolver.GetItemName(itemId);
            return name.Contains("Seed", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Acorn", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Herb", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Maps host-grass tiles onto biome grass seed item names.</summary>
        private static bool TryResolveAnchorSeedItem(int anchorTileId, out string? itemName)
        {
            // --- Prefer grass seeds over dirt/mud blocks for vine and thorn fallbacks.
            itemName = anchorTileId switch
            {
                TileID.Grass => "GrassSeeds",
                TileID.CorruptGrass => "CorruptSeeds",
                TileID.CrimsonGrass => "CrimsonSeeds",
                TileID.HallowedGrass => "HallowedSeeds",
                TileID.JungleGrass => "JungleGrassSeeds",
                TileID.MushroomGrass => "MushroomGrassSeeds",
                TileID.AshGrass => "AshGrassSeeds",
                _ => null,
            };
            return itemName is not null && ContentNameResolver.ResolveItem(itemName).HasValue;
        }

        /// <summary>Adds one Living Wood Wand + ammo interaction for a tree/cactus main tile and style.</summary>
        private static int AddTreeWandEntry(
            PlacementInteractionMap map,
            int tileId,
            int ammoItemId,
            int style)
        {
            // --- Skip unresolved ammo; TreeLayer selects the styled row at paste/manifest time.
            if (ammoItemId <= 0)
                return 0;

            string ammoName = ContentNameResolver.GetItemName(ammoItemId);
            map.AddTile(tileId, InteractionEntry.Create(
                "Tile",
                item: LivingWoodWandItem,
                ammo: ammoName,
                ammoId: ammoItemId,
                yield: ConsumableYield,
                style: style));
            LogDebug($"  {ContentNameResolver.GetTileName(tileId)} ({tileId}) style={style} -> {LivingWoodWandItem} ammo={ammoName}");
            return 1;
        }

        /// <summary>Returns true when the tile is a hanging or reverse vine thread.</summary>
        private static bool IsVineTile(int tileId) =>
            tileId >= 0
            && tileId < TileLoader.TileCount
            && (TileID.Sets.IsVine[tileId] || TileID.Sets.ReverseVineThreads[tileId]);

        /// <summary>Builds createTile item id -> internal name for direct placeable lookups.</summary>
        private static Dictionary<int, string> BuildCreateTileItemIndex()
        {
            var index = new Dictionary<int, string>();
            for (int itemId = 0; itemId < ItemLoader.ItemCount; itemId++)
            {
                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || item.type != itemId || item.createTile < TileID.Dirt)
                    continue;

                if (!index.ContainsKey(item.createTile))
                    index[item.createTile] = ContentNameResolver.GetItemName(itemId);
            }

            return index;
        }

        private static void Save(Mod mod, PlacementInteractionMap map)
        {
            var output = ToNameBasedOutput(map);
            var doc = new InteractionMapDocument<PlacementOutput>
            {
                Metadata = InteractionMapMetadata.Create(mod, "tile-first", "plant"),
                Data = output,
            };
            SavePlacementJson(GetOutputPath("Plants"), doc, "PlantInteractionMap");
        }

        private static PlacementOutput ToNameBasedOutput(PlacementInteractionMap map)
        {
            var output = new PlacementOutput();
            foreach (var (tileId, list) in map.GetAllTileEntries())
            {
                string mainTile = ContentNameResolver.GetTileName(tileId);
                if (!output.Tiles.TryGetValue(mainTile, out TileFirstExportGroup? group))
                {
                    group = new TileFirstExportGroup();
                    output.Tiles[mainTile] = group;
                }

                group.Interactions.AddRange(list);
            }

            return output;
        }

        private static void SavePlacementJson<T>(string path, T output, string errorContext)
        {
            try
            {
                string json = JsonSerializer.Serialize(output, InteractionMapJsonOptions.TileFirst);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                LogWarn($"Failed to save {errorContext}: {ex.Message}");
            }
        }

        private sealed class PlacementOutput
        {
            public Dictionary<string, TileFirstExportGroup> Tiles { get; set; } = [];
            public Dictionary<string, TileFirstExportGroup> Walls { get; set; } = [];
        }
    }
}
