#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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
    /// 2. Finding tiles/walls that can be converted via TileID/WallID.Sets.Conversion
    /// 3. Testing WorldGen.Convert on each (sourceTile, conversionType) pair and recording results
    /// </summary>
    internal sealed class BiomeDiscovery : DiscoveryBase
    {
        public static void Run(Mod mod, Player player, Action<string> msg, bool allowStepping = true)
        {
            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(msg);

            var chamberContext = new ChamberContext();
            var enumerator = CreateSteppedRun(mod, player, msg, chamberContext);
            if (allowStepping && DebugStepper.IsHoldingDebugStick(player))
            {
                void OnCancel()
                {
                    if (chamberContext.State is not null)
                    {
                        TestChamberSystem.Instance.TeardownRing(chamberContext.X, chamberContext.Y, chamberContext.State);
                        chamberContext.State = null;
                    }
                }
                DebugStepper.StartSession(player, enumerator, onComplete: null, onCancel: OnCancel, log: msg);
                return;
            }

            while (enumerator.MoveNext()) { }
        }

        private sealed class ChamberContext
        {
            public int X { get; set; }
            public int Y { get; set; }
            public TestChamberSystem.RingSavedState? State { get; set; }
        }

        private static IEnumerator CreateSteppedRun(Mod mod, Player player, Action<string> msg, ChamberContext chamberContext)
        {
            int testX = (int)(player.Center.X / 16f);
            int testY = (int)(player.Center.Y / 16f) - 5;

            var debugPath = GetDebugPath("BiomeDiscovery_Debug.txt");
            var outputPath = GetOutputPath("Biome.itr");
            using var debugWriter = new StreamWriter(debugPath, append: false);

            void Log(string s)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}";
                debugWriter.WriteLine(line);
                debugWriter.Flush();
                msg(s);
            }

            Log($"Biome discovery started. Output: {outputPath}");
            Log($"Debug log: {debugPath}");
            yield return null;

            var itemsByType = LoadConversionItemsFromConfig(mod, Log);
            var convertibleTiles = CollectConvertibleTiles(Log);
            var convertibleWalls = CollectConvertibleWalls(Log);

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
            var placementBaseLookup = LoadPlacementBaseTileLookup(mod, Log);
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
                        Log($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} = SKIP: not in world");
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
                        Log($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} = SKIP: placement failed");
                        yield return null;
                        continue;
                    }

                    Log($"    {itemPrefix}{baseName} ({tileId})");
                    yield return null;
                    WorldGen.Convert(testX, testY, conversionType, size: 3, tiles: true, walls: false);

                    var tile = Main.tile[testX, testY];
                    if (!tile.HasTile)
                    {
                        Log($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} = SKIP: removed by conversion");
                        yield return null;
                        continue;
                    }
                    int resultTileId = tile.TileType;
                    bool changed = resultTileId != tileId;

                    if (changed)
                    {
                        string mainName = ContentNameResolver.GetTileName(resultTileId);
                        Log($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} = {mainName} ({resultTileId})");
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
                        Log($"    {itemPrefix}{baseName} ({tileId}) + {conversionTypeName} - not changed");
                    }
                    yield return null;
                }
                SuppressTileDrop.KillTile(testX, testY);

                foreach (int wallId in convertibleWalls)
                {
                    string baseName = ContentNameResolver.GetWallName(wallId);

                    if (!WorldGen.InWorld(testX, testY))
                    {
                        Log($"    {itemPrefix}{baseName} ({wallId}) + {conversionTypeName} = SKIP: not in world");
                        yield return null;
                        continue;
                    }

                    Log($"    {itemPrefix}{baseName} ({wallId})");
                    WorldGen.PlaceWall(testX, testY, (ushort)wallId, mute: true);

                    yield return null;
                    WorldGen.Convert(testX, testY, conversionType, size: 1, tiles: false, walls: true);

                    var wall = Main.tile[testX, testY];
                    int resultWallId = wall.WallType;
                    bool changed = resultWallId != wallId;

                    if (changed)
                    {
                        string mainName = ContentNameResolver.GetWallName(resultWallId);
                        Log($"    {itemPrefix}{baseName} ({wallId}) + {conversionTypeName} = {mainName} ({resultWallId})" );
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
                        Log($"    {itemPrefix}{baseName} ({wallId}) + {conversionTypeName} - not changed");
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

            Save(mod, map, Log);
            Log($"Biome discovery complete. Output: {GetOutputPath("Biome.itr")}");
        }

        /// <summary>
        /// Loads tile -> required base tile lookup from placement map and custom item interactions.
        /// Used to place base tiles before conversion targets.
        /// </summary>
        private static Dictionary<int, int> LoadPlacementBaseTileLookup(Mod mod, Action<string> log)
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
                    log($"Placement map (base lookup): {ex.Message}");
                }
            }
            else
            {
                log("Placement map not found; base tile lookup from placement only.");
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
                    log($"Custom map (base lookup): {ex.Message}");
                }
            }

            log($"  Placement base lookup: {result.Count} tiles require a base.");
            return result;
        }

        /// <summary>
        /// Loads biome conversion items from BiomeConfig.itr (mapType=biomeConfig).
        /// Returns itemId -> (conversionType, yield). Unresolved items are skipped.
        /// </summary>
        private static Dictionary<int, (int ConversionType, int? Yield)> LoadConversionItemsFromConfig(Mod mod, Action<string> log)
        {
            var result = new Dictionary<int, (int, int?)>();
            var path = TileMappingSystem.FindFileByMetadata(TileMappingSystem.GetSourceMapsDirectory(), null, "biomeConfig");
            if (path is null)
            {
                log("BiomeConfig.itr not found; BiomeDiscovery has no conversion items.");
                return result;
            }

            try
            {
                ItrFileProcessor.ProcessFile(path, (dataEl, _) =>
                {
                    if (dataEl.ValueKind != JsonValueKind.Object)
                    {
                        log("BiomeConfig.itr data is not an object.");
                        return;
                    }

                    foreach (var prop in dataEl.EnumerateObject())
                    {
                        string item = prop.Name;
                        var entry = prop.Value;
                        if (!entry.TryGetProperty("type", out var ctEl) || !ctEl.TryGetInt32(out int conversionType))
                        {
                            log($"  Skipped '{item}': missing or invalid type.");
                            continue;
                        }
                        int? yield = null;
                        if (entry.TryGetProperty("yield", out var tyEl) && tyEl.ValueKind == JsonValueKind.Number && tyEl.TryGetInt32(out int ty))
                            yield = ty;

                        var itemId = ContentNameResolver.ResolveItem(item);
                        if (!itemId.HasValue || itemId.Value <= 0)
                        {
                            log($"  Skipped '{item}': could not resolve to item ID.");
                            continue;
                        }

                        if (!result.ContainsKey(itemId.Value))
                        {
                            result[itemId.Value] = (conversionType, yield);
                            log($"  Biome conversion: {item} (ItemId={itemId}) -> type {conversionType}, yield {yield?.ToString() ?? "null"}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                log($"Failed to load BiomeConfig.itr: {ex.Message}");
            }

            return result;
        }

        private static HashSet<int> CollectConvertibleTiles(Action<string> log)
        {
            var tiles = new HashSet<int>();
            var conversionType = typeof(TileID.Sets.Conversion);
            foreach (var field in conversionType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(bool[])) continue;
                var arr = field.GetValue(null) as bool[];
                if (arr is null) continue;
                for (int i = 0; i < arr.Length && i < TileLoader.TileCount; i++)
                {
                    if (arr[i]) tiles.Add(i);
                }
            }
            foreach (var prop in conversionType.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (prop.PropertyType != typeof(bool[])) continue;
                var arr = prop.GetValue(null) as bool[];
                if (arr is null) continue;
                for (int i = 0; i < arr.Length && i < TileLoader.TileCount; i++)
                {
                    if (arr[i]) tiles.Add(i);
                }
            }
            log($"  Convertible tiles: {tiles.Count}");
            return tiles;
        }

        private static HashSet<int> CollectConvertibleWalls(Action<string> log)
        {
            var walls = new HashSet<int>();
            bool Check(bool[]? arr, int wallId) => arr is not null && wallId < arr.Length && arr[wallId];

            string[] wallSetNames = ["Grass", "Stone", "Dirt", "Snow", "Ice", "Sandstone", "HardenedSand", "PureSand"];
            for (int w = 0; w < WallLoader.WallCount; w++)
            {
                bool inAny = false;
                foreach (var name in wallSetNames)
                {
                    if (Check(GetWallConversionArray(name), w)) { inAny = true; break; }
                }
                if (!inAny)
                    for (int i = 1; i <= 4; i++)
                    {
                        if (Check(GetWallConversionArray($"NewWall{i}"), w)) { inAny = true; break; }
                    }
                if (inAny) walls.Add(w);
            }

            log($"  Convertible walls: {walls.Count}");
            return walls;
        }

        private static bool[]? GetWallConversionArray(string name)
        {
            var pi = typeof(WallID.Sets.Conversion).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (pi is not null) return pi.GetValue(null) as bool[];
            var fi = typeof(WallID.Sets.Conversion).GetField(name, BindingFlags.Public | BindingFlags.Static);
            return fi?.GetValue(null) as bool[];
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

        private static void Save(Mod mod, BiomeInteractionMap map, Action<string> log)
        {
            var output = ToNameBasedOutput(map);
            var doc = new InteractionMapDocument<Dictionary<string, ConversionEntryOutput>>
            {
                Metadata = InteractionMapMetadata.Create(mod, "item-first", "biome"),
                Data = output,
            };
            SaveJson(GetOutputPath("Biome.itr"), doc, log, "BiomeInteractionMap");
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
