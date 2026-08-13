#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers vine and reverse-vine tile interactions from TileID.Sets.IsVine / ReverseVineThreads.
    /// Vines have no placeable item in vanilla; maps each vine to a drop item, anchor block, or Staff of Regrowth fallback.
    /// Writes Vines.itr in tile-first format.
    /// </summary>
    internal sealed class VineDiscovery : DiscoveryBase
    {
        private const string FallbackPlacementItem = "StaffofRegrowth";
        private const int DecorativeYield = -1;

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

        /// <summary>Scans vine tile sets and writes Vines.itr to SourceMaps.</summary>
        public static void Run(Mod mod, Action<string> msg)
        {
            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(msg);

            var debugPath = GetDebugPath("VineDiscovery_Debug.txt");
            var outputPath = GetOutputPath("Vines.itr");
            using var debugWriter = new StreamWriter(debugPath, append: false);

            void Log(string s)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}";
                debugWriter.WriteLine(line);
                debugWriter.Flush();
                msg(s);
            }

            Log($"Vine discovery started. Output: {outputPath}");
            Log($"Debug log: {debugPath}");

            // --- Build createTile index, then walk every tile id flagged as vine or reverse vine.
            IReadOnlyDictionary<int, string> createTileItems = BuildCreateTileItemIndex();
            var map = new PlacementInteractionMap();
            int discovered = 0;

            for (int tileId = 0; tileId < TileLoader.TileCount; tileId++)
            {
                if (!IsVineTile(tileId))
                    continue;

                string tileName = ContentNameResolver.GetTileName(tileId);
                if (!TryResolvePlacementItem(tileId, createTileItems, out string itemName, out int? yield, out string source))
                {
                    Log($"  SKIP {tileName} ({tileId}): could not resolve placement item.");
                    continue;
                }

                map.AddTile(tileId, InteractionEntry.Create(
                    "Tile",
                    item: itemName,
                    yield: yield,
                    style: null));
                discovered++;
                Log($"  {tileName} ({tileId}) -> {itemName} yield={yield} ({source})");
            }

            Log($"Discovered {discovered} vine interactions.");
            Save(mod, map, Log);
            Log($"Vine discovery complete. Output: {outputPath}");
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
                if (item.IsAir || item.type != itemId || item.createTile < 0)
                    continue;

                if (!index.ContainsKey(item.createTile))
                    index[item.createTile] = ContentNameResolver.GetItemName(itemId);
            }

            return index;
        }

        /// <summary>Resolves the inventory item used when pasting a vine tile.</summary>
        private static bool TryResolvePlacementItem(
            int vineTileId,
            IReadOnlyDictionary<int, string> createTileItems,
            out string itemName,
            out int? yield,
            out string source)
        {
            itemName = "";
            yield = null;
            source = "";

            int dropItemId = TileLoader.GetItemDropFromTypeAndStyle(vineTileId, 0);
            if (dropItemId > 0)
            {
                itemName = ContentNameResolver.GetItemName(dropItemId);
                yield = DecorativeYield;
                source = "tile drop";
                return true;
            }

            if (createTileItems.TryGetValue(vineTileId, out string? directItem))
            {
                itemName = directItem;
                yield = DecorativeYield;
                source = "createTile item";
                return true;
            }

            if (VineAnchorTiles.TryGetValue(vineTileId, out int anchorTileId)
                && TryResolveAnchorPlacementItem(anchorTileId, createTileItems, out string? anchorItem)
                && anchorItem is not null)
            {
                itemName = anchorItem;
                yield = DecorativeYield;
                source = $"anchor {ContentNameResolver.GetTileName(anchorTileId)}";
                return true;
            }

            itemName = FallbackPlacementItem;
            yield = DecorativeYield;
            source = "staff fallback";
            return ContentNameResolver.ResolveItem(FallbackPlacementItem).HasValue;
        }

        /// <summary>Maps a host grass or ground tile to the block item players typically carry for that biome.</summary>
        private static bool TryResolveAnchorPlacementItem(
            int anchorTileId,
            IReadOnlyDictionary<int, string> createTileItems,
            out string? itemName)
        {
            if (createTileItems.TryGetValue(anchorTileId, out string? direct))
            {
                itemName = direct;
                return true;
            }

            itemName = anchorTileId switch
            {
                TileID.Grass or TileID.CorruptGrass or TileID.HallowedGrass or TileID.CrimsonGrass => "DirtBlock",
                TileID.JungleGrass or TileID.MushroomGrass => "MudBlock",
                TileID.AshGrass => "AshBlock",
                _ => null,
            };
            return itemName is not null;
        }

        private static void Save(Mod mod, PlacementInteractionMap map, Action<string> log)
        {
            var output = ToNameBasedOutput(map);
            var doc = new InteractionMapDocument<PlacementOutput>
            {
                Metadata = InteractionMapMetadata.Create(mod, "tile-first", "vine"),
                Data = output,
            };
            SavePlacementJson(GetOutputPath("Vines.itr"), doc, log, "VineInteractionMap");
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

        private static void SavePlacementJson<T>(string path, T output, Action<string> log, string errorContext)
        {
            try
            {
                string json = JsonSerializer.Serialize(output, InteractionMapJsonOptions.TileFirst);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                log($"Failed to save {errorContext}: {ex.Message}");
            }
        }

        private sealed class PlacementOutput
        {
            public Dictionary<string, TileFirstExportGroup> Tiles { get; set; } = [];
            public Dictionary<string, TileFirstExportGroup> Walls { get; set; } = [];
        }
    }
}
