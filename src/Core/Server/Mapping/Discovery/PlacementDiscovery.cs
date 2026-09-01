#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers placement interactions for items with createTile/createWall.
    /// 1. Frame-classified tiles → anchor scaffold + TileObject/TileLoader.CanPlace, then map.
    /// 2. Try direct PlaceTile; map successes. 3. Failures → try baseTileContext from BaseContext.itr.
    /// Run via command to produce Placement.itr.
    /// </summary>
    internal sealed class PlacementDiscovery : DiscoveryBase
    {
        public static void Run(Mod mod, Player player, bool allowStepping = true)
        {
            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(player);

            int testX = (int)(player.Center.X / 16f);
            int testY = (int)(player.Center.Y / 16f) - 5;
            if (!WorldGen.InWorld(testX, testY))
            {
                LogWarn("Player position is outside world bounds. Move in-world and retry.");
                return;
            }

            var enumerator = CreateSteppedRun(mod, player, testX, testY);
            DebugStepper.RunEnumerator(enumerator, player, allowStepping, log: LogDebug);
        }

        /// <summary>Yields placement discovery work for composition into a larger stepped run.</summary>
        internal static IEnumerator CreateEnumerator(Mod mod, Player player)
        {
            // --- Resolve the chamber origin beside the player, then run the same stepped body as Run.

            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(player);

            int testX = (int)(player.Center.X / 16f);
            int testY = (int)(player.Center.Y / 16f) - 5;
            if (!WorldGen.InWorld(testX, testY))
            {
                LogWarn("Player position is outside world bounds. Move in-world and retry.");
                yield break;
            }

            IEnumerator inner = CreateSteppedRun(mod, player, testX, testY);
            while (inner.MoveNext())
                yield return inner.Current;
        }

        private static IEnumerator CreateSteppedRun(Mod mod, Player player, int testX, int testY)
        {
            var outputPath = GetOutputPath("Placement");

            Log($"Placement discovery started. Output: {outputPath}");
            Log($"Test position: ({testX},{testY})");
            yield return null;

            var chamber = TestChamberSystem.Instance;
            var chamberState = chamber.SetupRing(testX, testY);
            var scaffoldCache = new TestChamberSystem.FrameScaffoldCache(chamber, testX, testY);
            yield return null;

            var map = new PlacementInteractionMap();
            var baseTileContext = LoadBaseTileContext(mod).ToList();
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
                    int added = ProcessTileItem(map, createTile, i, item, testX, testY, baseTileContext, scaffoldCache);
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

            int modDropFallbacksAdded = AddModTileDropFallbackEntries(map, testX, testY, scaffoldCache);
            if (modDropFallbacksAdded > 0)
                Log($"Added {modDropFallbacksAdded} mod tile drop fallback mappings from TileLoader.GetItemDropFromTypeAndStyle.");

            // Drop the shared frame scaffold before restoring the demonite ring.
            scaffoldCache.Clear();
            chamber.TeardownRing(testX, testY, chamberState);
            yield return null;

            Log($"Discovered {tilesAdded} tile placements, {wallsAdded} wall placements.");
            Save(mod, map);
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
            TestChamberSystem.FrameScaffoldCache scaffoldCache)
        {
            var chamber = TestChamberSystem.Instance;
            int placeStyle = item.placeStyle >= 0 ? item.placeStyle : 0;
            string itemName = ContentNameResolver.GetItemName(itemId);
            string tileName = ContentNameResolver.GetTileName(createTile);
            string itemPrefix = $"[{itemName}] ";

            if (FrameLayer.IsFrameTileType((ushort)createTile))
                return ProcessFrameTileItem(map, createTile, itemId, item, itemName, tileName, itemPrefix, testX, testY, scaffoldCache);

            // Non-frame probes use the single focal cell; drop any leftover frame scaffold first.
            scaffoldCache.Clear();

            var saved = chamber.SaveCell(testX, testY);

            SuppressTileDrop.KillTile(testX, testY);
            bool directOk = WorldGen.PlaceTile(testX, testY, createTile, mute: true, forced: true, plr: -1, style: placeStyle);
            var placedTile = Main.tile[testX, testY];
            bool directSuccess = directOk && placedTile.HasTile && placedTile.TileType == createTile;

            if (directSuccess)
            {
                chamber.RestoreCell(testX, testY, saved);
                return AddStyledTileAndAlternates(map, createTile, itemName, itemId, item.placeStyle >= 0 ? item.placeStyle : null);
            }

            int added = 0;
            foreach (int baseTileId in baseTileContext)
            {
                chamber.ClearAndSetTile(testX, testY, baseTileId);
                WorldGen.PlaceTile(testX, testY, createTile, mute: true, forced: true, plr: -1, style: placeStyle);
                var tile = Main.tile[testX, testY];
                if (!tile.HasTile)
                    continue;

                // Some items (e.g. moss) produce a different tile on certain bases than item.createTile
                // (BrownMoss on GrayBrick -> BrownMossBrick, not BrownMoss).
                int resultTileId = tile.TileType;
                if (resultTileId == baseTileId)
                    continue;

                map.AddTile(resultTileId, InteractionEntry.Create(
                    "Tile",
                    baseId: baseTileId,
                    @base: ContentNameResolver.GetTileName(baseTileId),
                    item: itemName,
                    itemId: itemId,
                    style: item.placeStyle >= 0 ? item.placeStyle : null));
                added++;
            }

            if (added == 0)
            {
                string directGot = placedTile.HasTile ? ContentNameResolver.GetTileName(placedTile.TileType) : "empty";
                LogDebug($"    {itemPrefix}{tileName} ({createTile}) = SKIP: no valid base (direct got {directGot}, tried {baseTileContext.Count} bases)");
            }

            chamber.RestoreCell(testX, testY, saved);
            return added;
        }

        /// <summary>Validates frame tiles via CanPlace on an anchor scaffold, then maps successful styles.</summary>
        private static int ProcessFrameTileItem(
            PlacementInteractionMap map,
            int createTile,
            int itemId,
            Item item,
            string itemName,
            string tileName,
            string itemPrefix,
            int testX,
            int testY,
            TestChamberSystem.FrameScaffoldCache scaffoldCache)
        {
            // --- Collect candidate styles, scaffold anchors, CanPlace-check, then emit map rows.

            var styles = CollectFrameStyles(createTile, item);
            int added = 0;
            foreach (int style in styles)
            {
                if (!TryValidateFrameCanPlace(createTile, style, testX, testY, scaffoldCache, out string reason))
                {
                    LogDebug($"    {itemPrefix}{tileName} ({createTile}) style={style} = SKIP: {reason}");
                    continue;
                }

                added += AddStyledTileAndAlternates(
                    map,
                    createTile,
                    itemName,
                    itemId,
                    style);
            }

            return added;
        }

        /// <summary>Builds style candidates from the item placeStyle and TileObjectData ranges when this item owns the tile.</summary>
        private static HashSet<int> CollectFrameStyles(int createTile, Item item)
        {
            // --- Always include this item's placeStyle; expand RandomStyleRange only when no peer consumable exists.

            var styles = new HashSet<int> { item.placeStyle >= 0 ? item.placeStyle : 0 };

            bool otherConsumableExists = false;
            foreach (KeyValuePair<int, Item> kvp in ContentSamples.ItemsByType)
            {
                Item sample = kvp.Value;
                if (sample is null
                    || sample.type == item.type
                    || sample.createTile != createTile
                    || !sample.consumable
                    || sample.maxStack <= 0)
                {
                    continue;
                }

                otherConsumableExists = true;
                break;
            }

            if (!otherConsumableExists)
            {
                foreach (int style in EnumeratePlacementStyles(createTile))
                    styles.Add(style);
            }

            return styles;
        }

        /// <summary>Returns true when TileLoader/TileObject CanPlace accepts the style at the chamber origin.</summary>
        private static bool TryValidateFrameCanPlace(
            int tileType,
            int style,
            int originX,
            int originY,
            TestChamberSystem.FrameScaffoldCache scaffoldCache,
            out string reason)
        {
            // --- Reuse a matching scaffold when possible, then run the same CanPlace gate paste uses.

            reason = "CanPlace rejected";
            scaffoldCache.Ensure(tileType, style);

            if (DiscoveryUsesTilePlacement(tileType, style))
            {
                if (TileLoader.CanPlace(originX, originY, tileType))
                    return true;
                reason = "TileLoader.CanPlace rejected";
                return false;
            }

            FrameOperations.FramePlacementVariant variant = FrameOperations.ResolveFramePlacementVariant(
                tileType,
                frameX: 0,
                frameY: 0,
                cachedStyle: style,
                cachedAlternate: 0);
            int direction = variant.Direction;
            if (TileObject.CanPlace(originX, originY, tileType, style, direction, out _, onlyCheck: true))
                return true;

            // Try the opposite facing when the object declares a directional place.
            if (direction != 0
                && TileObject.CanPlace(originX, originY, tileType, style, -direction, out _, onlyCheck: true))
                return true;

            reason = "TileObject.CanPlace rejected";
            return false;
        }

        /// <summary>Returns true when discovery should validate via TileLoader.CanPlace instead of TileObject.CanPlace.</summary>
        private static bool DiscoveryUsesTilePlacement(int tileType, int style)
        {
            // --- Mirror FrameOperations.UsesTilePlacement without a live InteractionMap.

            if (TileID.Sets.Grass[tileType] || tileType == TileID.MinecartTrack)
                return true;

            TileObjectData? data = TileObjectData.GetTileData(tileType, style)
                ?? TileObjectData.GetTileData(tileType, 0);
            if (data is null)
                return true;

            if (FrameOperations.HasFrameAnchor(tileType, style) || data.Width > 1 || data.Height > 1)
                return false;

            return true;
        }

        /// <summary>
        /// Maps a placeable tile plus furniture and herb growth-stage counterparts for the same item and style.
        /// </summary>
        private static int AddStyledTileAndAlternates(
            PlacementInteractionMap map,
            int createTile,
            string itemName,
            int itemId,
            int? style)
        {
            // --- Emit createTile, then open/closed furniture twins, then Mature/Blooming herb rows with Immature fake.

            map.AddTile(createTile, InteractionEntry.Create("Tile", item: itemName, itemId: itemId, style: style));
            int added = 1;

            // Doors, trapdoors, and tall gates share one item across open/closed tile types.
            if (InteractionMap.TryGetAlternateFurnitureTile(createTile, out int alternateTile) && alternateTile != createTile)
            {
                map.AddTile(alternateTile, InteractionEntry.Create("Tile", item: itemName, itemId: itemId, style: style));
                LogDebug($"  {itemName}: {ContentNameResolver.GetTileName(createTile)} style={style ?? 0} also {ContentNameResolver.GetTileName(alternateTile)} ({alternateTile})");
                added++;
            }

            // Seeds place ImmatureHerbs; captured Mature/Blooming stages place Immature via fake.
            if (createTile == TileID.ImmatureHerbs)
            {
                string immatureName = ContentNameResolver.GetTileName(TileID.ImmatureHerbs);
                foreach (int stageTile in new[] { TileID.MatureHerbs, TileID.BloomingHerbs })
                {
                    map.AddTile(stageTile, InteractionEntry.Create(
                        "Tile",
                        item: itemName,
                        itemId: itemId,
                        style: style,
                        fake: immatureName,
                        fakeId: TileID.ImmatureHerbs));
                    LogDebug($"  {itemName}: {ContentNameResolver.GetTileName(stageTile)} style={style ?? 0} fake={immatureName}");
                    added++;
                }
            }

            return added;
        }

        /// <summary>
        /// Adds tile->item mappings for mod tiles that register drop mappings through TileLoader.
        /// This complements the item-driven probe when tiles are not reliably discoverable via createTile.
        /// </summary>
        private static int AddModTileDropFallbackEntries(
            PlacementInteractionMap map,
            int testX,
            int testY,
            TestChamberSystem.FrameScaffoldCache scaffoldCache)
        {
            int added = 0;
            for (int tileId = TileID.Count; tileId < TileLoader.TileCount; tileId++)
            {
                int itemId = TileLoader.GetItemDropFromTypeAndStyle(tileId, 0);
                if (itemId <= 0)
                    continue;

                var item = new Item();
                item.SetDefaults(itemId);
                if (item.IsAir || !item.consumable || item.createTile != tileId)
                    continue;

                // Frame mod tiles must pass the same CanPlace gate as the item-driven path.
                if (FrameLayer.IsFrameTileType((ushort)tileId))
                {
                    int style = item.placeStyle >= 0 ? item.placeStyle : 0;
                    if (!TryValidateFrameCanPlace(tileId, style, testX, testY, scaffoldCache, out string reason))
                    {
                        LogDebug($"  Mod drop fallback SKIP {ContentNameResolver.GetTileName(tileId)} style={style}: {reason}");
                        continue;
                    }
                }

                added += AddStyledTileAndAlternates(
                    map,
                    tileId,
                    ContentNameResolver.GetItemName(itemId),
                    itemId,
                    item.placeStyle >= 0 ? item.placeStyle : null);
            }

            return added;
        }

        private static IEnumerable<int> LoadBaseTileContext(Mod mod)
        {
            var list = new List<int>();
            var path = TileMappingSystem.FindFileByMetadata(TileMappingSystem.GetSourceMapsDirectory(), null, "baseContext");
            if (path is null)
            {
                LogWarn("BaseContext.itr not found; baseTileContext empty.");
                return list;
            }

            try
            {
                ItrFileProcessor.ProcessFile(path, (dataEl, _) =>
                {
                    if (dataEl.ValueKind != JsonValueKind.Array)
                    {
                        LogWarn("BaseContext.itr data is not an array.");
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
                            LogDebug($"  Skipped placementBaseTile '{name}': could not resolve.");
                    }
                });
            }
            catch (Exception ex)
            {
                LogWarn($"Failed to load BaseContext.itr: {ex.Message}");
            }
            return list;
        }

        private static void Save(Mod mod, PlacementInteractionMap map)
        {
            var output = ToNameBasedOutput(map);
            var doc = new InteractionMapDocument<PlacementOutput>
            {
                Metadata = InteractionMapMetadata.Create(mod, "tile-first", "placement"),
                Data = output,
            };
            SavePlacementJson(GetOutputPath("Placement"), doc, "PlacementInteractionMap");
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

        private static void SavePlacementJson<T>(string path, T output, string errorContext)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(output, InteractionMapJsonOptions.TileFirst);
                System.IO.File.WriteAllText(path, json);
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
