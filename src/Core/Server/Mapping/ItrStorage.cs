using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ArchitectsBlueprintMod.Config;
using ArchitectsBlueprintMod.Core;
using ArchitectsBlueprintMod.Debug;
using Terraria.ModLoader;

namespace ArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Low-level .itr file parsing. Parses metadata+data wrapper and extracts item-first root.
    /// Used by ItrLoader and discovery config loaders.
    /// </summary>
    internal static class ItrFileProcessor
    {
        /// <summary>
        /// Parses a JSON file and invokes the callback with the data root and metadata. Requires metadata+data wrapper.
        /// </summary>
        public static void ProcessFile(string path, Action<JsonElement, InteractionMapMetadata> process)
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("metadata", out var metaEl) || !root.TryGetProperty("data", out var dataEl))
                throw new InvalidOperationException($"File lacks metadata+data wrapper: {path}");

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var meta = JsonSerializer.Deserialize<InteractionMapMetadata>(metaEl.GetRawText(), opts)
                ?? throw new InvalidOperationException($"Invalid metadata in: {path}");
            process(dataEl, meta);
        }

        /// <summary>
        /// Gets the item-first root from a data element. Supports both data (direct) and data.itemInteractions (legacy).
        /// </summary>
        public static JsonElement? GetItemFirstRoot(JsonElement dataEl)
        {
            if (dataEl.TryGetProperty("itemInteractions", out var nested))
                return nested;
            if (dataEl.ValueKind == JsonValueKind.Object)
                return dataEl;
            return null;
        }
    }

    /// <summary>
    /// Ultimate authority for the CompiledMaps directory. Owns path resolution, loading at paste time,
    /// and the cached InteractionMap. Compile layers write via GetCompiledMapPath.
    /// </summary>
    /// <remarks>
    /// <para><b>Pipeline overview</b></para>
    /// <list type="number">
    ///   <item><b>Compile-time</b>: TileMappingSystem.Compile writes Tile.itr, Wall.itr, etc. via GetCompiledMapPath.</item>
    ///   <item><b>Paste start</b>: PastingSystem calls PrepareForPaste with tile/wall IDs from the projection.</item>
    ///   <item><b>Lookup</b>: Layers and PastingInventory use InteractionMapProvider.</item>
    ///   <item><b>Paste end</b>: When no operations remain, Release() clears the cache.</item>
    /// </list>
    /// </remarks>
    internal static class ItrStorage
    {
        private const string CompiledMapsDir = "CompiledMaps";

        private static InteractionMap? _map;

        /// <summary>
        /// Gets the CompiledMaps directory. Creates it if missing.
        /// </summary>
        public static string GetCompiledMapsDirectory()
        {
            var dir = Path.Combine(TileMappingSystem.GetModDirectory(), CompiledMapsDir);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Gets full path for a file in ArchitectsBlueprintMod\CompiledMaps.
        /// </summary>
        public static string GetCompiledMapPath(string fileName) =>
            Path.Combine(GetCompiledMapsDirectory(), fileName);

        /// <summary>
        /// True when this game instance may load CompiledMaps from disk for paste
        /// (single-player, dedicated server, or listen-server host).
        /// </summary>
        internal static bool CanLoadPasteInteractionMaps() => TileMappingAuthority.UsesLocalMappingFilesystem();

        /// <summary>
        /// Prepares the interaction map for paste by loading only the given tile and wall IDs from JSON.
        /// Call at paste start before any lookups. Replaces any existing cached map.
        /// </summary>
        /// <param name="tileIds">Tile IDs to load from Tile.itr. Null = load all.</param>
        /// <param name="wallIds">Wall IDs to load from Wall.itr. Null = load all.</param>
        /// <remarks>Remote multiplayer clients do not load from disk; only single-player, dedicated server, or listen-server host.</remarks>
        public static void PrepareForPaste(IReadOnlySet<int>? tileIds, IReadOnlySet<int>? wallIds)
        {
            if (!CanLoadPasteInteractionMaps())
                return;

            _map = new InteractionMap();
            InteractionMapProvider.Current = _map;
            var dir = GetCompiledMapsDirectory();

            if (!Directory.Exists(dir))
                return;

            foreach (var path in Directory.EnumerateFiles(dir, "*.itr"))
            {
                if (_map is null) break;

                var layer = GetLayerFromFileName(path);
                if (layer is null) continue;

                IReadOnlySet<int>? filter = layer switch
                {
                    "Tile" => tileIds,
                    "Wall" => wallIds,
                    _ => null,
                };

                try
                {
                    LoadFileInto(path, _map, layer, filter);
                }
                catch
                {
                    _map = null;
                    InteractionMapProvider.Current = null;
                    break;
                }
            }

            _map?.SortAllByPriority();
        }

        private static string? GetLayerFromFileName(string path)
        {
            if (!path.EndsWith(".itr", StringComparison.OrdinalIgnoreCase))
                return null;
            var fileName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(fileName) ? null : ContentNameResolver.NormalizeLayer(fileName);
        }

        /// <summary>
        /// Releases the cached interaction map. Call when no paste operations remain.
        /// </summary>
        public static void Release()
        {
            _map = null;
            InteractionMapProvider.Current = null;
        }

        private static void LoadFileInto(string path, InteractionMap map, string layer, IReadOnlySet<int>? mainIdsToLoad)
        {
            var bytes = File.ReadAllBytes(path);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return;

            string? format = null;
            string? mapType = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                var propName = reader.GetString();
                reader.Read();

                if (string.Equals(propName, "metadata", StringComparison.Ordinal))
                {
                    ReadMetadata(ref reader, out format, out mapType);
                    continue;
                }

                if (string.Equals(propName, "data", StringComparison.Ordinal))
                {
                    if (!string.Equals(format, "tile-first", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(mapType, "compiled", StringComparison.OrdinalIgnoreCase))
                        return;

                    ReadData(ref reader, map, layer, mainIdsToLoad);
                    return;
                }

                reader.Skip();
            }
        }

        private static void ReadMetadata(ref Utf8JsonReader reader, out string? format, out string? mapType)
        {
            format = null;
            mapType = null;
            if (reader.TokenType != JsonTokenType.StartObject) return;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                var name = reader.GetString();
                reader.Read();

                if (string.Equals(name, "format", StringComparison.Ordinal))
                    format = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                else if (string.Equals(name, "mapType", StringComparison.Ordinal))
                    mapType = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                else
                    reader.Skip();
            }
        }

        private static void ReadData(ref Utf8JsonReader reader, InteractionMap map, string layer, IReadOnlySet<int>? mainIdsToLoad)
        {
            if (reader.TokenType != JsonTokenType.StartObject) return;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                string main = reader.GetString() ?? "";
                reader.Read();

                var mainId = ContentNameResolver.ResolveMain(layer, main);
                if (!mainId.HasValue)
                {
                    reader.Skip();
                    continue;
                }

                bool shouldLoad = mainIdsToLoad is null || mainIdsToLoad.Contains(mainId.Value);
                if (!shouldLoad)
                {
                    reader.Skip();
                    continue;
                }

                ReadMainValue(ref reader, map, layer, main, mainId.Value);
            }
        }

        private static void ReadMainValue(ref Utf8JsonReader reader, InteractionMap map, string layer, string main, int mainId)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                return;

            string? itemNamesLayer = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                var propName = reader.GetString();
                reader.Read();

                if (string.Equals(propName, "interactions", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.StartArray)
                {
                    ReadInteractionsArray(ref reader, map, layer, main, mainId);
                    continue;
                }

                if (string.Equals(propName, "itemNames", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.StartArray)
                {
                    var useLayer = ContentNameResolver.NormalizeLayer(itemNamesLayer ?? layer);
                    if (useLayer is not null)
                        ReadItemNamesArray(ref reader, map, useLayer, main, mainId);
                    else
                        reader.Skip();
                    continue;
                }

                if ((string.Equals(propName, "layer", StringComparison.Ordinal) || string.Equals(propName, "Layer", StringComparison.Ordinal)) && reader.TokenType == JsonTokenType.String)
                    itemNamesLayer = reader.GetString();
                else
                    reader.Skip();
            }
        }

        private static void ReadInteractionsArray(ref Utf8JsonReader reader, InteractionMap map, string layer, string main, int mainId)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) return;
                if (reader.TokenType != JsonTokenType.StartObject) continue;

                var (interactionLayer, item, @base, style) = ReadInteractionObject(ref reader);
                if (string.IsNullOrEmpty(item))
                    continue;

                var normLayer = ContentNameResolver.NormalizeLayer(interactionLayer ?? layer);
                if (normLayer is null)
                {
                    ABPLog.Warn($"Interaction skipped: layer missing (main={main}, item={item})", "InteractionMap");
                    continue;
                }

                var itemId = ContentNameResolver.ResolveItem(item);
                if (!itemId.HasValue || itemId.Value <= 0)
                {
                    ABPLog.Warn($"Interaction skipped: item unresolved (layer={normLayer}, main={main}, item={item})", "InteractionMap");
                    continue;
                }

                var entry = InteractionEntry.Create(normLayer,
                    main: main,
                    mainId: mainId,
                    item: item,
                    itemId: itemId.Value,
                    @base: @base,
                    style: style);
                map.Add(entry);
            }
        }

        private static (string? Layer, string? Item, string? Base, int? Style) ReadInteractionObject(ref Utf8JsonReader reader)
        {
            string? layer = null;
            string? item = null;
            string? @base = null;
            int? style = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                var name = reader.GetString();
                reader.Read();

                if (string.Equals(name, "Layer", StringComparison.Ordinal) || string.Equals(name, "layer", StringComparison.Ordinal))
                    layer = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                else if (string.Equals(name, "Item", StringComparison.Ordinal))
                    item = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                else if (string.Equals(name, "Base", StringComparison.Ordinal))
                    @base = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                else if (string.Equals(name, "Style", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.Number)
                    style = reader.TryGetInt32(out int s) ? s : null;
                else
                    reader.Skip();
            }

            return (layer, item, @base, style);
        }

        private static void ReadItemNamesArray(ref Utf8JsonReader reader, InteractionMap map, string layer, string main, int mainId)
        {
            var normLayer = ContentNameResolver.NormalizeLayer(layer);
            if (normLayer is null) return;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) return;
                var name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;

                var itemId = ContentNameResolver.ResolveItem(name);
                if (!itemId.HasValue || itemId.Value <= 0)
                {
                    ABPLog.Warn($"Interaction skipped: item unresolved (layer={normLayer}, main={main}, item={name})", "InteractionMap");
                    continue;
                }

                var entry = InteractionEntry.Create(normLayer,
                    main: main,
                    mainId: mainId,
                    item: name,
                    itemId: itemId.Value,
                    @base: null,
                    style: null);
                map.Add(entry);
            }
        }
    }

    /// <summary>
    /// Loads source .itr files into InteractionMap. Standardized pipeline for custom, placement, biome, shimmer.
    /// Config files (baseContext, biomeConfig) are loaded separately by their consumers.
    /// </summary>
    internal static class ItrLoader
    {
        private static readonly string[] InteractionMapTypes = ["custom", "placement", "biome", "shimmer"];

        /// <summary>
        /// Loads all interaction .itr files from the directory into the map.
        /// Order: custom, placement, biome, shimmer (shimmer applies Shimmer Bucket transform).
        /// </summary>
        public static void LoadAllInto(string directory, InteractionMap map, Action<string> msg)
        {
            var cfg = ModContent.GetInstance<TileMappingConfig>();
            foreach (var mapType in InteractionMapTypes)
            {
                if (!IsMapTypeEnabled(cfg, mapType))
                    continue;

                var path = TileMappingSystem.FindFileByMetadata(directory, null, mapType);
                if (path is null) continue;

                try
                {
                    ItrFileProcessor.ProcessFile(path, (dataEl, meta) =>
                    {
                        var format = meta.Format ?? "";
                        if (string.Equals(mapType, "shimmer", StringComparison.OrdinalIgnoreCase))
                            LoadShimmer(dataEl, map, msg);
                        else if (string.Equals(format, "item-first", StringComparison.OrdinalIgnoreCase))
                            LoadItemFirst(dataEl, map, msg);
                        else if (string.Equals(format, "tile-first", StringComparison.OrdinalIgnoreCase))
                            LoadTileFirst(dataEl, map, msg);
                    });
                }
                catch (Exception ex)
                {
                    msg($"{mapType}: {ex.Message}");
                }
            }
        }

        private static bool IsMapTypeEnabled(TileMappingConfig cfg, string mapType) =>
            mapType switch
            {
                "custom" => cfg.EnableCustomInteractionMap,
                "placement" => cfg.EnablePlacementDiscovery,
                "biome" => cfg.EnableBiomeConversions,
                "shimmer" => cfg.EnableShimmerTransforms,
                _ => true,
            };

        private static void LoadItemFirst(JsonElement dataEl, InteractionMap map, Action<string> msg)
        {
            var itemEl = ItrFileProcessor.GetItemFirstRoot(dataEl);
            if (!itemEl.HasValue) return;

            foreach (var itemProp in itemEl.Value.EnumerateObject())
            {
                string item = itemProp.Name;
                if (!itemProp.Value.TryGetProperty("interactions", out var arr)) continue;

                foreach (var ia in arr.EnumerateArray())
                {
                    var entry = ParseItemFirstInteraction(ia, item, msg);
                    if (entry is not null)
                        map.Add(entry);
                }
            }
        }

        private static void LoadShimmer(JsonElement dataEl, InteractionMap map, Action<string> msg)
        {
            var shimmerBucket = ShimmerDiscovery.BucketName;
            var itemEl = ItrFileProcessor.GetItemFirstRoot(dataEl);
            if (!itemEl.HasValue) return;

            foreach (var itemProp in itemEl.Value.EnumerateObject())
            {
                string sourceItem = itemProp.Name;
                if (!itemProp.Value.TryGetProperty("interactions", out var arr)) continue;

                foreach (var ia in arr.EnumerateArray())
                {
                    var entry = ParseItemFirstInteraction(ia, shimmerBucket, msg);
                    if (entry is not null)
                    {
                        entry.Ammo = sourceItem;
                        map.Add(entry);
                    }
                }
            }
        }

        private static InteractionEntry? ParseItemFirstInteraction(JsonElement ia, string item, Action<string> msg)
        {
            var layer = ia.TryGetProperty("layer", out var l) ? l.GetString() : null;
            if (string.IsNullOrEmpty(layer))
            {
                msg($"Interaction skipped (layer missing, item={item})");
                return null;
            }
            var normLayer = ContentNameResolver.NormalizeLayer(layer);
            if (normLayer is null) return null;

            string? main = ResolveMainFromJson(ia, normLayer);
            if (string.IsNullOrEmpty(main))
            {
                msg($"Interaction skipped (main missing, item={item})");
                return null;
            }

            var entry = new InteractionEntry
            {
                Layer = normLayer,
                Item = item,
                Main = main,
                Yield = ia.TryGetProperty("yield", out var y) && y.TryGetInt32(out int yv) ? yv : null,
                Ammo = ia.TryGetProperty("ammo", out var a) ? a.GetString() : null,
            };

            if (string.Equals(normLayer, "Tile", StringComparison.OrdinalIgnoreCase) || string.Equals(normLayer, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                entry.Base = ia.TryGetProperty("base", out var b) && b.ValueKind != JsonValueKind.Null ? b.GetString() : null;
                entry.Style = ia.TryGetProperty("style", out var s) && s.TryGetInt32(out int sv) ? sv : null;
            }

            return entry;
        }

        private static string? ResolveMainFromJson(JsonElement ia, string layer)
        {
            if (ia.TryGetProperty("main", out var m))
            {
                var s = m.GetString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
            if (string.Equals(layer, "Wire", StringComparison.OrdinalIgnoreCase) &&
                ia.TryGetProperty("resultWireKind", out var rwk) && rwk.TryGetInt32(out int kind))
            {
                return ContentNameResolver.GetWireKindName(kind);
            }
            return null;
        }

        private static void LoadTileFirst(JsonElement dataEl, InteractionMap map, Action<string> msg)
        {
            if (dataEl.TryGetProperty("tiles", out var tilesEl) || dataEl.TryGetProperty("Tiles", out tilesEl))
            {
                foreach (var tileProp in tilesEl.EnumerateObject())
                {
                    string main = tileProp.Name;
                    if (!tileProp.Value.TryGetProperty("interactions", out var arr)) continue;

                    foreach (var ia in arr.EnumerateArray())
                    {
                        var entry = ParseTileFirstInteraction(ia, "Tile", main, msg);
                        if (entry is not null)
                            map.Add(entry);
                    }
                }
            }

            if (dataEl.TryGetProperty("walls", out var wallsEl) || dataEl.TryGetProperty("Walls", out wallsEl))
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
                            if (string.IsNullOrEmpty(item))
                            {
                                msg($"Placement: interaction skipped (item missing, main={main})");
                                continue;
                            }
                            map.Add(new InteractionEntry { Layer = "Wall", Main = main, Item = item });
                        }
                    }
                    else if (val.TryGetProperty("interactions", out var arr))
                    {
                        foreach (var ia in arr.EnumerateArray())
                        {
                            var entry = ParseTileFirstInteraction(ia, "Wall", main, msg);
                            if (entry is not null)
                                map.Add(entry);
                        }
                    }
                }
            }
        }

        private static InteractionEntry? ParseTileFirstInteraction(JsonElement ia, string layer, string main, Action<string> msg)
        {
            string? item = ia.TryGetProperty("Item", out var i) ? i.GetString() : null;
            if (string.IsNullOrEmpty(item))
            {
                msg("Placement: interaction skipped (item missing)");
                return null;
            }

            var entry = new InteractionEntry
            {
                Layer = layer,
                Main = main,
                Item = item,
                Base = ia.TryGetProperty("Base", out var b) && b.ValueKind != JsonValueKind.Null ? b.GetString() : null,
                Style = ia.TryGetProperty("Style", out var s) && s.TryGetInt32(out int sv) ? sv : null,
                Yield = ia.TryGetProperty("Yield", out var y) && y.TryGetInt32(out int yv) ? yv : null,
                Ammo = ia.TryGetProperty("Ammo", out var a) ? a.GetString() : null,
            };

            if (string.Equals(layer, "Wall", StringComparison.OrdinalIgnoreCase))
                entry.Base = null;

            return entry;
        }
    }
}
