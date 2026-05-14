#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ArchitectsBlueprintMod.Core;
using ArchitectsBlueprintMod.Core.Layers;
using ArchitectsBlueprintMod.Debug;
using ArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
namespace ArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers placement interactions for items with createTile/createWall.
    /// 1. Frames (multitiles) → direct placement. 2. Try direct placement, map successes. 3. Failures → try baseTileContext from BaseContext.itr.
    /// Run via command to produce Placement.itr.
    /// </summary>
    internal sealed class PlacementDiscovery : DiscoveryBase
    {
        public static void Run(Mod mod, Player player, Action<string> msg, bool allowStepping = true)
        {
            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(msg);

            int testX = (int)(player.Center.X / 16f);
            int testY = (int)(player.Center.Y / 16f) - 5;
            if (!WorldGen.InWorld(testX, testY))
            {
                msg("Player position is outside world bounds. Move in-world and retry.");
                return;
            }

            var enumerator = CreateSteppedRun(mod, player, testX, testY, msg);
            if (allowStepping && DebugStepper.IsHoldingDebugStick(player))
            {
                DebugStepper.StartSession(player, enumerator, onComplete: null, onCancel: null, log: msg);
                return;
            }

            while (enumerator.MoveNext()) { }
        }

        private static IEnumerator CreateSteppedRun(Mod mod, Player player, int testX, int testY, Action<string> msg)
        {
            var debugPath = GetDebugPath("PlacementDiscovery_Debug.txt");
            var outputPath = GetOutputPath("Placement.itr");
            using var debugWriter = new System.IO.StreamWriter(debugPath, append: false);
            void Log(string s)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}";
                debugWriter.WriteLine(line);
                debugWriter.Flush();
                msg(s);
            }

            Log($"Placement discovery started. Output: {outputPath}");
            Log($"Test position: ({testX},{testY})");
            yield return null;

            var chamber = TestChamberSystem.Instance;
            var chamberState = chamber.SetupRing(testX, testY);
            yield return null;

            var map = new PlacementInteractionMap();
            var baseTileContext = LoadBaseTileContext(mod, Log).ToList();
            if (baseTileContext.Count == 0)
                Log("baseTileContext empty; conversion fallback disabled.");

            int tilesAdded = 0, wallsAdded = 0;

            for (int i = 0; i < ItemLoader.ItemCount; i++)
            {
                var item = new Item();
                item.SetDefaults(i);
                if (item.IsAir || item.type != i) continue;

                int createTile = item.createTile;
                if (createTile >= 0 && item.consumable)
                {
                    yield return null;
                    int added = ProcessTileItem(map, createTile, i, item, testX, testY, baseTileContext, Log);
                    tilesAdded += added;
                }

                int createWall = item.createWall;
                if (createWall >= 0 && item.consumable)
                {
                    yield return null;
                    map.AddWall(createWall, InteractionEntry.Create("Wall", item: ContentNameResolver.GetItemName(i), itemId: i, style: null));
                    wallsAdded++;
                }
            }

            chamber.TeardownRing(testX, testY, chamberState);
            yield return null;

            Log($"Discovered {tilesAdded} tile placements, {wallsAdded} wall placements.");
            Save(mod, map, Log);
            Log($"Placement discovery complete. Output: {outputPath}");
        }

        private static int ProcessTileItem(
            PlacementInteractionMap map,
            int createTile,
            int itemId,
            Item item,
            int testX,
            int testY,
            List<int> baseTileContext,
            Action<string> log)
        {
            var chamber = TestChamberSystem.Instance;
            int placeStyle = item.placeStyle >= 0 ? item.placeStyle : 0;
            string itemName = ContentNameResolver.GetItemName(itemId);
            string tileName = ContentNameResolver.GetTileName(createTile);
            string itemPrefix = $"[{itemName}] ";

            if (IsFrameTile(createTile))
            {
                map.AddTile(createTile, InteractionEntry.Create("Tile", item: itemName, itemId: itemId, style: item.placeStyle >= 0 ? item.placeStyle : null));
                return 1;
            }

            var saved = chamber.SaveCell(testX, testY);

            SuppressTileDrop.KillTile(testX, testY);
            bool directOk = WorldGen.PlaceTile(testX, testY, createTile, mute: true, forced: true, plr: -1, style: placeStyle);
            var placedTile = Main.tile[testX, testY];
            bool directSuccess = directOk && placedTile.HasTile && placedTile.TileType == createTile;

            if (directSuccess)
            {
                chamber.RestoreCell(testX, testY, saved);
                map.AddTile(createTile, InteractionEntry.Create("Tile", item: itemName, itemId: itemId, style: item.placeStyle >= 0 ? item.placeStyle : null));
                return 1;
            }

            int added = 0;
            foreach (int baseTileId in baseTileContext)
            {
                chamber.ClearAndSetTile(testX, testY, baseTileId);
                WorldGen.PlaceTile(testX, testY, createTile, mute: true, forced: true, plr: -1, style: placeStyle);
                var tile = Main.tile[testX, testY];
                if (tile.HasTile && tile.TileType == createTile)
                {
                    map.AddTile(createTile, InteractionEntry.Create("Tile", baseId: baseTileId, @base: ContentNameResolver.GetTileName(baseTileId), item: itemName, itemId: itemId, yield: 1, style: item.placeStyle >= 0 ? item.placeStyle : null));
                    added++;
                }
            }

            if (added == 0)
            {
                string directGot = placedTile.HasTile ? ContentNameResolver.GetTileName(placedTile.TileType) : "empty";
                log($"    {itemPrefix}{tileName} ({createTile}) = SKIP: no valid base (direct got {directGot}, tried {baseTileContext.Count} bases)");
            }

            chamber.RestoreCell(testX, testY, saved);
            return added;
        }

        /// <summary>
        /// Returns true for any tile that should be registered directly without chamber simulation.
        /// condition (tile-anchored, wall-anchored, or other-anchored). The layer system handles
        /// anchor validation at paste time, so discovery does not need to simulate anchor requirements.
        /// </summary>
        private static bool IsFrameTile(int tileType)
        {
            if (tileType < 0 || tileType >= TileLoader.TileCount) return false;
            var data = TileObjectData.GetTileData(tileType, 0);
            if (data is null) return false;
            return FrameOperations.HasTileAnchor(tileType, 0)
                || FrameOperations.HasWallAnchor(tileType, 0)
                || FrameOperations.HasOtherAnchor(tileType, 0);
        }

        private static IEnumerable<int> LoadBaseTileContext(Mod mod, Action<string> log)
        {
            var list = new List<int>();
            var path = TileMappingSystem.FindFileByMetadata(TileMappingSystem.GetSourceMapsDirectory(), null, "baseContext");
            if (path is null)
            {
                log("BaseContext.itr not found; baseTileContext empty.");
                return list;
            }

            try
            {
                ItrFileProcessor.ProcessFile(path, (dataEl, _) =>
                {
                    if (dataEl.ValueKind != JsonValueKind.Array)
                    {
                        log("BaseContext.itr data is not an array.");
                        return;
                    }
                    foreach (var el in dataEl.EnumerateArray())
                    {
                        string? name = el.GetString();
                        if (string.IsNullOrEmpty(name)) continue;

                        var tileId = ContentNameResolver.ResolveTile(name);
                        if (tileId.HasValue)
                            list.Add(tileId.Value);
                        else
                            log($"  Skipped placementBaseTile '{name}': could not resolve.");
                    }
                });
            }
            catch (Exception ex)
            {
                log($"Failed to load BaseContext.itr: {ex.Message}");
            }
            return list;
        }

        private static void Save(Mod mod, PlacementInteractionMap map, Action<string> log)
        {
            var output = ToNameBasedOutput(map);
            var doc = new InteractionMapDocument<PlacementOutput>
            {
                Metadata = InteractionMapMetadata.Create(mod, "tile-first", "placement"),
                Data = output,
            };
            SavePlacementJson(GetOutputPath("Placement.itr"), doc, log, "PlacementInteractionMap");
        }

        private static PlacementOutput ToNameBasedOutput(PlacementInteractionMap map)
        {
            var output = new PlacementOutput();
            foreach (var (tileId, list) in map.GetAllTileEntries())
            {
                var mainTile = ContentNameResolver.GetTileName(tileId);
                if (!output.Tiles.TryGetValue(mainTile, out var group))
                {
                    group = new TileFirstExportGroup();
                    output.Tiles[mainTile] = group;
                }
                group.Interactions.AddRange(list);
            }
            foreach (var (wallId, list) in map.GetAllWallEntries())
            {
                var wallName = ContentNameResolver.GetWallName(wallId);
                if (!output.Walls.TryGetValue(wallName, out var group))
                {
                    group = new TileFirstExportGroup();
                    output.Walls[wallName] = group;
                }
                group.Interactions.AddRange(list);
            }
            return output;
        }

        private static void SavePlacementJson<T>(string path, T output, Action<string> log, string errorContext)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(output, InteractionMapJsonOptions.TileFirst);
                System.IO.File.WriteAllText(path, json);
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
