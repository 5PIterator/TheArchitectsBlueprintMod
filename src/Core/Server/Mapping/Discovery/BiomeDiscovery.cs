#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers biome conversion interactions by:
    /// 1. Loading conversion items from BiomeConfig.itr
    /// 2. Collecting candidates from compile-time TileID/WallID.Sets.Conversion unions (+ mod ids)
    /// 3. Testing WorldGen.Convert + TileLoader/WallLoader.Convert and recording results
    /// </summary>
    internal sealed class BiomeDiscovery : DiscoveryBase
    {
        public static void Run(Mod mod, Player player, bool allowStepping = true)
        {
            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(player);

            (IEnumerator enumerator, Action onCancel) = CreateEnumeratorWithCancel(mod, player);
            DebugStepper.RunEnumerator(enumerator, player, allowStepping, onCancel: onCancel, log: LogDebug);
        }

        /// <summary>Builds the biome enumerator and an onCancel that restores the test chamber ring.</summary>
        internal static (IEnumerator Enumerator, Action OnCancel) CreateEnumeratorWithCancel(Mod mod, Player player)
        {
            // --- Pair the stepped run with a cancel hook that tears down an in-flight demonite ring.

            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(player);

            var chamberContext = new ChamberContext();
            void OnCancel()
            {
                if (chamberContext.State is not null)
                {
                    TestChamberSystem.Instance.TeardownRing(chamberContext.X, chamberContext.Y, chamberContext.State);
                    chamberContext.State = null;
                }
            }

            return (CreateSteppedRun(mod, player, chamberContext), OnCancel);
        }

        private sealed class ChamberContext
        {
            public int X { get; set; }
            public int Y { get; set; }
            public TestChamberSystem.RingSavedState? State { get; set; }
        }

        private static IEnumerator CreateSteppedRun(Mod mod, Player player, ChamberContext chamberContext)
        {
            if (!TestChamberSystem.Instance.TryResolveOrigin(player, out Point origin))
            {
                LogWarn("Player position is outside world bounds. Move in-world and retry.");
                yield break;
            }

            int testX = origin.X;
            int testY = origin.Y;

            var outputPath = GetOutputPath("Biome");

            Log($"Biome discovery started. Output: {outputPath}");
            yield return null;

            var itemsByType = LoadConversionItemsFromConfig(mod);
            var convertibleTiles = CollectConvertibleTiles();
            var convertibleWalls = CollectConvertibleWalls();

            var map = new BiomeInteractionMap();
            foreach (var kvp in itemsByType)
            {
                map.Items[kvp.Key] = new ItemEntry
                {
                    ItemId = kvp.Key,
                    Item = ContentNameResolver.GetItemName(kvp.Key),
                    Type = kvp.Value.ConversionType,
                };
            }

            var resultsByConversionType = new Dictionary<int, List<BiomeInteraction>>();
            int numConversionTypes = itemsByType.Values.Select(v => v.ConversionType).Distinct().Count();
            var placementBaseLookup = LoadPlacementBaseTileLookup(mod);
            Log($"Testing {convertibleTiles.Count} tiles × {numConversionTypes} conversion types at ({testX},{testY})...");
            yield return null;

            var chamberState = TestChamberSystem.Instance.SetupRing(testX, testY);
            chamberContext.X = testX;
            chamberContext.Y = testY;
            chamberContext.State = chamberState;
            yield return null;

            var conversionTypes = itemsByType.Values.Select(v => v.ConversionType).Distinct().ToList();
            foreach (int conversionType in conversionTypes)
            {
                var tileResults = new List<BiomeInteraction>();
                var wallResults = new List<BiomeInteraction>();

                string conversionTypeName = GetConversionTypeName(conversionType);
                var itemNames = itemsByType
                    .Where(kvp => kvp.Value.ConversionType == conversionType)
                    .Select(kvp => ContentNameResolver.GetItemName(kvp.Key))
                    .ToList();
                string itemPrefix = itemNames.Count > 0 ? $"[{string.Join(", ", itemNames)}] " : "";

                Log($"  Conversion type {conversionType} ({conversionTypeName}):");

                foreach (int tileId in convertibleTiles)
                {
                    string baseName = ContentNameResolver.GetTileName(tileId);

                    if (!WorldGen.InWorld(testX, testY))
                    {
                        LogDebug($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} = SKIP: not in world");
                        yield return null;
                        continue;
                    }

                    SuppressTileDrop.KillTile(testX, testY);
                    if (placementBaseLookup.TryGetValue(tileId, out int requiredBaseId))
                    {
                        WorldGen.PlaceTile(testX, testY, requiredBaseId, mute: true, forced: true, plr: -1, style: 0);
                    }
                    bool placed = WorldGen.PlaceTile(testX, testY, tileId, mute: true, forced: true, plr: -1, style: 0);
                    var placedTile = Main.tile[testX, testY];
                    if (!placed || !placedTile.HasTile || placedTile.TileType != tileId)
                    {
                        string got = placedTile.HasTile ? ContentNameResolver.GetTileName(placedTile.TileType) : "empty";
                        LogDebug($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} = SKIP: placement failed");
                        yield return null;
                        continue;
                    }

                    LogDebug($"    {itemPrefix}{baseName} ({tileId})");
                    yield return null;
                    WorldGen.Convert(testX, testY, conversionType, size: 3, tiles: true, walls: false);
                    TileLoader.Convert(testX, testY, conversionType);

                    var tile = Main.tile[testX, testY];
                    if (!tile.HasTile)
                    {
                        LogDebug($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} = SKIP: removed by conversion");
                        yield return null;
                        continue;
                    }
                    int resultTileId = tile.TileType;
                    bool changed = resultTileId != tileId;

                    if (changed)
                    {
                        string mainName = ContentNameResolver.GetTileName(resultTileId);
                        LogDebug($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} = {mainName} ({resultTileId})");
                        tileResults.Add(new BiomeInteraction
                        {
                            Layer = "Tile",
                            BaseId = tileId,
                            MainId = resultTileId,
                            Yield = null,
                        });
                    }
                    else
                    {
                        LogDebug($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} - not changed");
                    }
                    yield return null;
                }
                SuppressTileDrop.KillTile(testX, testY);

                foreach (int wallId in convertibleWalls)
                {
                    string baseName = ContentNameResolver.GetWallName(wallId);

                    if (!WorldGen.InWorld(testX, testY))
                    {
                        LogDebug($"    {itemPrefix}{baseName} ({wallId}) + {conversionTypeName} = SKIP: not in world");
                        yield return null;
                        continue;
                    }

                    LogDebug($"    {itemPrefix}{baseName} ({wallId})");
                    WorldGen.PlaceWall(testX, testY, (ushort)wallId, mute: true);

                    yield return null;
                    WorldGen.Convert(testX, testY, conversionType, size: 1, tiles: false, walls: true);
                    WallLoader.Convert(testX, testY, conversionType);

                    var wall = Main.tile[testX, testY];
                    int resultWallId = wall.WallType;
                    bool changed = resultWallId != wallId;

                    if (changed)
                    {
                        string mainName = ContentNameResolver.GetWallName(resultWallId);
                        LogDebug($"    {itemPrefix}{baseName} ({wallId}) + {conversionTypeName} = {mainName} ({resultWallId})" );
                        wallResults.Add(new BiomeInteraction
                        {
                            Layer = "Wall",
                            BaseId = wallId,
                            MainId = resultWallId,
                            Yield = null,
                        });
                    }
                    else
                    {
                        LogDebug($"    {itemPrefix}{baseName} ({wallId}) + {conversionTypeName} - not changed");
                    }
                    yield return null;
                }
                SuppressTileDrop.KillWall(testX, testY);

                var interactions = tileResults.Concat(wallResults).ToList();
                resultsByConversionType[conversionType] = interactions;
                Log($"    Summary: {tileResults.Count} tiles, {wallResults.Count} walls");
                yield return null;
            }

            foreach (var kvp in itemsByType)
            {
                int itemId = kvp.Key;
                int conversionType = kvp.Value.ConversionType;
                int? yield = kvp.Value.Yield;
                if (!resultsByConversionType.TryGetValue(conversionType, out var rawInteractions))
                    continue;
                var interactions = rawInteractions.Select(ia => new BiomeInteraction
                {
                    Layer = ia.Layer,
                    BaseId = ia.BaseId,
                    MainId = ia.MainId,
                    Yield = yield,
                }).ToList();
                map.Conversions[itemId] = new ConversionEntry
                {
                    ItemId = itemId,
                    Item = ContentNameResolver.GetItemName(itemId),
                    Type = conversionType,
                    Interactions = interactions,
                };
            }

            TestChamberSystem.Instance.TeardownRing(testX, testY, chamberState);
            chamberContext.State = null;
            yield return null;

            Save(mod, map);
            Log($"Biome discovery complete. Output: {GetOutputPath("Biome")}");
        }

        /// <summary>
        /// Loads tile -> required base tile lookup from placement map and custom item interactions.
        /// Used to place base tiles before conversion targets.
        /// </summary>
        private static Dictionary<int, int> LoadPlacementBaseTileLookup(Mod mod)
        {
            var result = new Dictionary<int, int>();

            void Add(string mainName, string baseName)
            {
                var mainId = ContentNameResolver.ResolveTile(mainName);
                var baseId = ContentNameResolver.ResolveTile(baseName);
                if (mainId.HasValue && baseId.HasValue && !result.ContainsKey(mainId.Value))
                    result[mainId.Value] = baseId.Value;
            }

            var placementPath = TileMappingSystem.FindFileByMetadata(TileMappingSystem.GetSourceMapsDirectory(), "tile-first", "placement");
            if (placementPath is not null)
            {
                try
                {
                    ItrFileProcessor.ProcessFile(placementPath, (dataEl, _) =>
                    {
                        if (!dataEl.TryGetProperty("tiles", out var tilesEl) && !dataEl.TryGetProperty("Tiles", out tilesEl))
                            return;
                        foreach (var tileProp in tilesEl.EnumerateObject())
                        {
                            string main = tileProp.Name;
                            if (!tileProp.Value.TryGetProperty("interactions", out var arr))
                                continue;
                            foreach (var ia in arr.EnumerateArray())
                            {
                                if (ia.TryGetProperty("Base", out var b) && b.ValueKind != JsonValueKind.Null)
                                {
                                    string? baseName = b.GetString();
                                    if (!string.IsNullOrEmpty(baseName))
                                    {
                                        Add(main, baseName);
                                        break;
                                    }
                                }
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogWarn($"Placement map (base lookup): {ex.Message}");
                }
            }
            else
            {
                Log("Placement map not found; base tile lookup from placement only.");
            }

            var customPath = TileMappingSystem.FindFileByMetadata(TileMappingSystem.GetSourceMapsDirectory(), null, "custom");
            if (customPath is not null)
            {
                try
                {
                    ItrFileProcessor.ProcessFile(customPath, (dataEl, _) =>
                    {
                        var itemEl = ItrFileProcessor.GetItemFirstRoot(dataEl);
                        if (!itemEl.HasValue) return;

                        foreach (var itemProp in itemEl.Value.EnumerateObject())
                        {
                            if (!itemProp.Value.TryGetProperty("interactions", out var arr)) continue;
                            foreach (var ia in arr.EnumerateArray())
                            {
                                var layer = ia.TryGetProperty("layer", out var l) ? l.GetString() : null;
                                if (!string.Equals(layer, "Tile", StringComparison.OrdinalIgnoreCase)) continue;
                                string? main = ia.TryGetProperty("main", out var m) ? m.GetString() : null;
                                string? baseName = ia.TryGetProperty("base", out var b) ? b.GetString() : null;
                                if (!string.IsNullOrEmpty(main) && !string.IsNullOrEmpty(baseName))
                                    Add(main, baseName);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogWarn($"Custom map (base lookup): {ex.Message}");
                }
            }

            Log($"  Placement base lookup: {result.Count} tiles require a base.");
            return result;
        }

        /// <summary>
        /// Loads biome conversion items from BiomeConfig.itr (mapType=biomeConfig).
        /// Returns itemId -> (conversionType, yield). Unresolved items are skipped.
        /// </summary>
        private static Dictionary<int, (int ConversionType, int? Yield)> LoadConversionItemsFromConfig(Mod mod)
        {
            var result = new Dictionary<int, (int, int?)>();
            var path = TileMappingSystem.FindFileByMetadata(TileMappingSystem.GetSourceMapsDirectory(), null, "biomeConfig");
            if (path is null)
            {
                LogWarn("BiomeConfig.itr not found; BiomeDiscovery has no conversion items.");
                return result;
            }

            try
            {
                ItrFileProcessor.ProcessFile(path, (dataEl, _) =>
                {
                    if (dataEl.ValueKind != JsonValueKind.Object)
                    {
                        LogWarn("BiomeConfig.itr data is not an object.");
                        return;
                    }

                    foreach (var prop in dataEl.EnumerateObject())
                    {
                        string item = prop.Name;
                        var entry = prop.Value;
                        if (!entry.TryGetProperty("type", out var ctEl) || !ctEl.TryGetInt32(out int conversionType))
                        {
                            LogDebug($"  Skipped '{item}': missing or invalid type.");
                            continue;
                        }
                        int? yield = null;
                        if (entry.TryGetProperty("yield", out var tyEl) && tyEl.ValueKind == JsonValueKind.Number && tyEl.TryGetInt32(out int ty))
                            yield = ty;

                        var itemId = ContentNameResolver.ResolveItem(item);
                        if (!itemId.HasValue || itemId.Value <= 0)
                        {
                            LogDebug($"  Skipped '{item}': could not resolve to item ID.");
                            continue;
                        }

                        if (!result.ContainsKey(itemId.Value))
                        {
                            result[itemId.Value] = (conversionType, yield);
                            LogDebug($"  Biome conversion: {item} (ItemId={itemId}) -> type {conversionType}, yield {yield?.ToString() ?? "null"}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogWarn($"Failed to load BiomeConfig.itr: {ex.Message}");
            }

            return result;
        }

        /// <summary>Unions compile-time TileID.Sets.Conversion members, then adds mod tile ids.</summary>
        private static HashSet<int> CollectConvertibleTiles()
        {
            // --- Explicit Sets.Conversion arrays (no reflection); append mod tile ids for RegisterConversion coverage.

            var tiles = UnionConvertibleIds(
                TileLoader.TileCount,
                TileID.Sets.Conversion.Dirt,
                TileID.Sets.Conversion.GolfGrass,
                TileID.Sets.Conversion.Grass,
                TileID.Sets.Conversion.HardenedSand,
                TileID.Sets.Conversion.Ice,
                TileID.Sets.Conversion.JungleGrass,
                TileID.Sets.Conversion.MergesWithDirtInASpecialWay,
                TileID.Sets.Conversion.Moss,
                TileID.Sets.Conversion.MossBrick,
                TileID.Sets.Conversion.MushroomGrass,
                TileID.Sets.Conversion.Sand,
                TileID.Sets.Conversion.Sandstone,
                TileID.Sets.Conversion.Snow,
                TileID.Sets.Conversion.Stone,
                TileID.Sets.Conversion.Thorn);

            for (int tileId = TileID.Count; tileId < TileLoader.TileCount; tileId++)
                tiles.Add(tileId);

            Log($"  Convertible tiles: {tiles.Count}");
            return tiles;
        }

        /// <summary>Unions compile-time WallID.Sets.Conversion members, then adds mod wall ids.</summary>
        private static HashSet<int> CollectConvertibleWalls()
        {
            // --- Explicit Sets.Conversion arrays (no reflection); append mod wall ids for RegisterConversion coverage.

            var walls = UnionConvertibleIds(
                WallLoader.WallCount,
                WallID.Sets.Conversion.Dirt,
                WallID.Sets.Conversion.Grass,
                WallID.Sets.Conversion.HardenedSand,
                WallID.Sets.Conversion.Ice,
                WallID.Sets.Conversion.NewWall1,
                WallID.Sets.Conversion.NewWall2,
                WallID.Sets.Conversion.NewWall3,
                WallID.Sets.Conversion.NewWall4,
                WallID.Sets.Conversion.PureSand,
                WallID.Sets.Conversion.Sandstone,
                WallID.Sets.Conversion.Snow,
                WallID.Sets.Conversion.Stone);

            for (int wallId = WallID.Count; wallId < WallLoader.WallCount; wallId++)
                walls.Add(wallId);

            Log($"  Convertible walls: {walls.Count}");
            return walls;
        }

        /// <summary>Collects every true index across the given conversion bool arrays up to <paramref name="limit"/>.</summary>
        private static HashSet<int> UnionConvertibleIds(int limit, params bool[][] sets)
        {
            // --- Walk each public Conversion set and keep ids marked convertible.

            var ids = new HashSet<int>();
            for (int s = 0; s < sets.Length; s++)
            {
                bool[] arr = sets[s];
                if (arr is null)
                    continue;

                int end = Math.Min(arr.Length, limit);
                for (int i = 0; i < end; i++)
                {
                    if (arr[i])
                        ids.Add(i);
                }
            }

            return ids;
        }

        private static string GetConversionTypeName(int type)
        {
            return type switch
            {
                BiomeConversionID.Purity => "Purity",
                BiomeConversionID.Corruption => "Corruption",
                BiomeConversionID.Hallow => "Hallow",
                BiomeConversionID.GlowingMushroom => "GlowingMushroom",
                BiomeConversionID.Crimson => "Crimson",
                BiomeConversionID.Sand => "Sand",
                BiomeConversionID.Snow => "Snow",
                BiomeConversionID.Dirt => "Dirt",
                BiomeConversionID.PurificationPowder => "PurificationPowder",
                BiomeConversionID.Chlorophyte => "Chlorophyte",
                _ => $"Conversion_{type}",
            };
        }

        private static void Save(Mod mod, BiomeInteractionMap map)
        {
            var output = ToNameBasedOutput(map);
            var doc = new InteractionMapDocument<Dictionary<string, ConversionEntryOutput>>
            {
                Metadata = InteractionMapMetadata.Create(mod, "item-first", "biome"),
                Data = output,
            };
            SaveJson(GetOutputPath("Biome"), doc, "BiomeInteractionMap");
        }

        private static Dictionary<string, ConversionEntryOutput> ToNameBasedOutput(BiomeInteractionMap map)
        {
            var output = new Dictionary<string, ConversionEntryOutput>();
            foreach (var (itemId, entry) in map.Items)
            {
                var key = ContentNameResolver.GetItemName(itemId);
                var interactions = map.Conversions.TryGetValue(itemId, out var conv)
                    ? conv.Interactions
                    : [];
                output[key] = new ConversionEntryOutput
                {
                    Type = entry.Type,
                    Interactions = interactions.Select(ia =>
                    {
                        bool isTile = string.Equals(ia.Layer, "Tile", StringComparison.OrdinalIgnoreCase);
                        return new BiomeInteractionOutput
                        {
                            Layer = ia.Layer,
                            Base = isTile ? ContentNameResolver.GetTileName(ia.BaseId) : ContentNameResolver.GetWallName(ia.BaseId),
                            BaseId = ia.BaseId,
                            Main = isTile && ia.MainId >= 0 ? ContentNameResolver.GetTileName(ia.MainId) : (!isTile && ia.MainId >= 0 ? ContentNameResolver.GetWallName(ia.MainId) : null),
                            MainId = ia.MainId >= 0 ? ia.MainId : null,
                            Yield = ia.Yield,
                        };
                    }).ToList(),
                };
            }
            return output;
        }

        // Internal model (ID-based)
        private sealed class BiomeInteractionMap
        {
            public Dictionary<int, ItemEntry> Items { get; set; } = [];
            public Dictionary<int, ConversionEntry> Conversions { get; set; } = [];
        }

        private sealed class ItemEntry
        {
            public int ItemId { get; set; }
            public string Item { get; set; } = "";
            public int Type { get; set; }
        }

        private sealed class ConversionEntry
        {
            public int ItemId { get; set; }
            public string Item { get; set; } = "";
            public int Type { get; set; }
            public List<BiomeInteraction> Interactions { get; set; } = [];
        }

        private sealed class BiomeInteraction
        {
            public string Layer { get; set; } = "";
            public int BaseId { get; set; }
            public int MainId { get; set; }
            public int? Yield { get; set; }
        }

        // JSON output: flat item name -> { conversionType, interactions }
        private sealed class ConversionEntryOutput
        {
            public int Type { get; set; }
            public List<BiomeInteractionOutput> Interactions { get; set; } = [];
        }

        private sealed class BiomeInteractionOutput
        {
            public string Layer { get; set; } = "";
            public string? Base { get; set; }   // Source tile or wall name
            public int? BaseId { get; set; }   // Source tile or wall ID
            public string? Main { get; set; }  // Result tile or wall name
            public int? MainId { get; set; }   // Result tile or wall ID
            public int? Yield { get; set; }
        }
    }
}
