#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Discovers shimmer transformation results via ItemID.Sets.ShimmerTransformToItem and recipe decrafting.
    /// Part 1: Saves raw discovery to ShimmerMap.itr.
    /// Part 2: Pairs result items with Placement.itr tiles and exports Shimmer.itr (item-first, loadable as interaction map).
    /// </summary>
    internal sealed class ShimmerDiscovery : DiscoveryBase
    {
        public const int BucketId = ItemID.BottomlessShimmerBucket;

        public static string BucketName => ContentNameResolver.GetItemName(BucketId);

        /// <summary>
        /// Runs shimmer discovery (both parts). Part 1: ShimmerMap.itr. Part 2: Shimmer.itr.
        /// When player is provided and holding DebugStick, steps on left-click; right-click cancels.
        /// </summary>
        public static void Run(Mod mod, Action<string> msg, Player? player = null, bool allowStepping = true)
        {
            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(msg);

            var enumerator = CreateSteppedRun(mod, msg);
            if (allowStepping && player is not null && DebugStepper.IsHoldingDebugStick(player))
            {
                DebugStepper.StartSession(player, enumerator, onComplete: null, onCancel: null, log: msg);
                return;
            }

            while (enumerator.MoveNext()) { }
        }

        private static IEnumerator CreateSteppedRun(Mod mod, Action<string> msg)
        {
            var debugPath = GetDebugPath("ShimmerDiscovery_Debug.txt");
            using var debugWriter = new StreamWriter(debugPath, append: false);

            void Log(string s)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {s}";
                debugWriter.WriteLine(line);
                debugWriter.Flush();
                msg(s);
            }

            var map = new ShimmerMapInternal();
            int discovered = 0;

            Log($"Shimmer discovery started. Part 1: ShimmerMap.itr, Part 2: Shimmer.itr");
            Log("Part 1: Discovering shimmer transforms...");
            yield return null;

            for (int sourceType = 1; sourceType < ItemLoader.ItemCount; sourceType++)
            {
                var results = GetResultItems(sourceType);
                if (results is null || results.Count == 0) continue;

                var entries = results.Select(r => new ShimmerResultEntry
                {
                    ItemId = r.type,
                    Yield = r.stack,
                }).ToList();
                map.Transforms[sourceType] = entries;
                discovered++;
                yield return null;
            }

            Log($"Discovered {discovered} items with shimmer transforms.");
            SaveShimmerMap(mod, map, Log);
            Log($"Part 1 complete. Output: {GetOutputPath("ShimmerMap.itr")}");
            yield return null;

            Log("Part 2: Pairing with Placement.itr and exporting Shimmer.itr...");
            BuildAndSaveShimmerItr(mod, map, Log);
            Log($"Shimmer discovery complete. Output: {GetOutputPath("Shimmer.itr")}");
        }

        /// <summary>
        /// Gets shimmer result items for a source type. Used by Run and by fallback when no map is loaded.
        /// </summary>
        public static List<(int type, int stack)> GetResultItems(int sourceType)
        {
            var results = new List<(int, int)>();

            if (sourceType < 0 || sourceType >= ItemLoader.ItemCount) return results;

            if (ItemID.Sets.ShimmerTransformToItem is not null && sourceType < ItemID.Sets.ShimmerTransformToItem.Length)
            {
                int direct = ItemID.Sets.ShimmerTransformToItem[sourceType];
                if (direct > 0)
                {
                    results.Add((direct, 1));
                    return results;
                }
            }

            int decraftIndex = GetDecraftingRecipeIndex(sourceType);
            if (decraftIndex >= 0 && decraftIndex < Recipe.numRecipes)
            {
                var recipe = Main.recipe[decraftIndex];
                if (recipe?.requiredItem is null) return results;

                foreach (var ing in recipe.requiredItem)
                {
                    if (ing is null || ing.IsAir) continue;
                    if (ing.type <= ItemID.None) continue;
                    results.Add((ing.type, Math.Max(1, ing.stack)));
                }
            }

            return results;
        }

        private static int GetDecraftingRecipeIndex(int itemType)
        {
            for (int i = 0; i < Recipe.numRecipes; i++)
            {
                var r = Main.recipe[i];
                if (r is null || r.createItem.IsAir) continue;
                if (r.createItem.type == itemType && r.createItem.stack > 0)
                    return i;
            }
            return -1;
        }

        private static void SaveShimmerMap(Mod mod, ShimmerMapInternal map, Action<string> log)
        {
            var output = ToNameBasedShimmerMap(map);
            var doc = new InteractionMapDocument<Dictionary<string, List<ShimmerResultOutput>>>
            {
                Metadata = InteractionMapMetadata.Create(mod, "item-first", "shimmerMap"),
                Data = output,
            };
            SaveJson(GetOutputPath("ShimmerMap.itr"), doc, log, "ShimmerMap");
        }

        private static Dictionary<string, List<ShimmerResultOutput>> ToNameBasedShimmerMap(ShimmerMapInternal map)
        {
            var output = new Dictionary<string, List<ShimmerResultOutput>>();
            foreach (var (sourceId, entries) in map.Transforms)
            {
                var key = ContentNameResolver.GetItemName(sourceId);
                output[key] = entries.Select(e => new ShimmerResultOutput
                {
                    Item = ContentNameResolver.GetItemName(e.ItemId),
                    Yield = e.Yield,
                }).ToList();
            }
            return output;
        }

        /// <summary>
        /// Part 2: Loads ShimmerMap (from in-memory map), builds item->placements from Placement.itr,
        /// pairs result items with tiles, and saves Shimmer.itr in item-first format (same layout as Custom.itr).
        /// </summary>
        private static void BuildAndSaveShimmerItr(Mod mod, ShimmerMapInternal shimmerMap, Action<string> log)
        {
            var itemToPlacements = BuildItemToPlacementsLookup(log);
            var output = new Dictionary<string, ShimmerItemInteractions>(StringComparer.OrdinalIgnoreCase);

            foreach (var (sourceId, entries) in shimmerMap.Transforms)
            {
                var sourceName = ContentNameResolver.GetItemName(sourceId);
                var interactions = new List<ShimmerInteractionOutput>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in entries)
                {
                    var resultItemName = ContentNameResolver.GetItemName(entry.ItemId);
                    if (!itemToPlacements.TryGetValue(resultItemName, out var placements))
                        continue;

                    foreach (var p in placements)
                    {
                        var key = $"{p.Layer}|{p.Main}|{p.Style ?? 0}|{p.Base ?? ""}";
                        if (seen.Add(key))
                        {
                            var interaction = new ShimmerInteractionOutput
                            {
                                Layer = p.Layer,
                                Main = p.Main,
                                Base = p.Base,
                                Style = p.Style,
                                Yield = entry.Yield,
                                Ammo = p.Ammo,
                            };
                            interactions.Add(interaction);
                        }
                    }
                }

                if (interactions.Count > 0)
                    output[sourceName] = new ShimmerItemInteractions { Interactions = interactions };
            }

            var doc = new InteractionMapDocument<Dictionary<string, ShimmerItemInteractions>>
            {
                Metadata = InteractionMapMetadata.Create(mod, "item-first", "shimmer"),
                Data = output,
            };
            SaveJson(GetOutputPath("Shimmer.itr"), doc, log, "Shimmer");
        }

        /// <summary>
        /// Builds item name -> list of placement interactions from Placement.itr (tile-first).
        /// Inverts tile->items to item->(layer, main, style, base, yield, ammo).
        /// </summary>
        private static Dictionary<string, List<ShimmerInteractionOutput>> BuildItemToPlacementsLookup(Action<string> log)
        {
            var result = new Dictionary<string, List<ShimmerInteractionOutput>>(StringComparer.OrdinalIgnoreCase);
            var directory = TileMappingSystem.GetSourceMapsDirectory();
            var path = TileMappingSystem.FindFileByMetadata(directory, "tile-first", "placement");
            if (path is null)
            {
                log("Placement.itr not found; Shimmer.itr will have no tile pairings.");
                return result;
            }

            try
            {
                ItrFileProcessor.ProcessFile(path, (dataEl, _) =>
                {
                    if (dataEl.TryGetProperty("Tiles", out var tilesEl) || dataEl.TryGetProperty("tiles", out tilesEl))
                    {
                        foreach (var tileProp in tilesEl.EnumerateObject())
                        {
                            string main = tileProp.Name;
                            if (!tileProp.Value.TryGetProperty("interactions", out var arr)) continue;

                            foreach (var ia in arr.EnumerateArray())
                            {
                                string? item = ia.TryGetProperty("Item", out var i) ? i.GetString() : null;
                                if (string.IsNullOrEmpty(item)) continue;

                                var entry = new ShimmerInteractionOutput
                                {
                                    Layer = "Tile",
                                    Main = main,
                                    Base = ia.TryGetProperty("Base", out var b) && b.ValueKind != JsonValueKind.Null ? b.GetString() : null,
                                    Style = ia.TryGetProperty("Style", out var s) && s.TryGetInt32(out int sv) ? sv : null,
                                    Yield = ia.TryGetProperty("Yield", out var y) && y.TryGetInt32(out int yv) ? yv : null,
                                    Ammo = ia.TryGetProperty("Ammo", out var a) ? a.GetString() : null,
                                };

                                if (!result.TryGetValue(item, out var list))
                                {
                                    list = [];
                                    result[item] = list;
                                }
                                list.Add(entry);
                            }
                        }
                    }

                    if (dataEl.TryGetProperty("Walls", out var wallsEl) || dataEl.TryGetProperty("walls", out wallsEl))
                    {
                        foreach (var wallProp in wallsEl.EnumerateObject())
                        {
                            string main = wallProp.Name;
                            var val = wallProp.Value;

                            if (val.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var el in val.EnumerateArray())
                                {
                                    string? item = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
                                    if (string.IsNullOrEmpty(item)) continue;

                                    var entry = new ShimmerInteractionOutput { Layer = "Wall", Main = main };
                                    if (!result.TryGetValue(item, out var list))
                                    {
                                        list = [];
                                        result[item] = list;
                                    }
                                    list.Add(entry);
                                }
                            }
                            else if (val.TryGetProperty("interactions", out var arr))
                            {
                                foreach (var ia in arr.EnumerateArray())
                                {
                                    string? item = ia.TryGetProperty("Item", out var i) ? i.GetString() : null;
                                    if (string.IsNullOrEmpty(item)) continue;

                                    var entry = new ShimmerInteractionOutput { Layer = "Wall", Main = main };
                                    if (!result.TryGetValue(item, out var list))
                                    {
                                        list = [];
                                        result[item] = list;
                                    }
                                    list.Add(entry);
                                }
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                log($"Placement.itr load failed: {ex.Message}");
            }

            return result;
        }

        private sealed class ShimmerMapInternal
        {
            public Dictionary<int, List<ShimmerResultEntry>> Transforms { get; set; } = [];
        }

        private sealed class ShimmerResultEntry
        {
            public int ItemId { get; set; }
            public int Yield { get; set; }
        }

        private sealed class ShimmerResultOutput
        {
            public string Item { get; set; } = "";
            public int Yield { get; set; }
        }

        /// <summary>Item-first format matching Custom.itr: item -> { interactions: [...] }.</summary>
        private sealed class ShimmerItemInteractions
        {
            public List<ShimmerInteractionOutput> Interactions { get; set; } = [];
        }

        private sealed class ShimmerInteractionOutput
        {
            public string Layer { get; set; } = "";
            public string Main { get; set; } = "";
            public string? Base { get; set; }
            public int? Style { get; set; }
            public int? Yield { get; set; }
            public string? Ammo { get; set; }
        }
    }
}
