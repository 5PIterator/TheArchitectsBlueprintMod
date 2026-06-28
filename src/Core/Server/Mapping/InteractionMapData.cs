using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Unified per-interaction format for all layers. Layer determines how Main/Base are interpreted.
    /// Names are used for JSON; IDs are populated when resolved for faster lookup.
    /// </summary>
    public sealed class InteractionEntry
    {
        /// <summary>Layer: "Tile" | "Wall" | "Liquid" | "Wire" | "Paint". Required; no default.</summary>
        public string? Layer { get; set; }
        /// <summary>Main result: tile/wall/liquid/wire/paint name. In item-first, this is the result; in tile-first, it is the key.</summary>
        public string? Main { get; set; }
        /// <summary>Item that can create this. In tile-first, this is per-entry; in item-first, it is the key.</summary>
        public string? Item { get; set; }
        /// <summary>Source tile/wall the item must interact with. Null for direct placement.</summary>
        public string? Base { get; set; }
        /// <summary>Placement style for direct placement (e.g. torch variant). Null for style-agnostic; interpreted as 0 when indexing.</summary>
        public int? Style { get; set; }
        /// <summary>Interactions per item; null = infinite.</summary>
        public int? Yield { get; set; }
        /// <summary>Ammo consumed instead of the tool.</summary>
        public string? Ammo { get; set; }

        /// <summary>Resolved Main ID. Tile: TileID, Wall: WallID, Liquid: liquid type, Wire: wire kind, Paint: PaintID.</summary>
        [JsonIgnore]
        public int? MainId { get; set; }
        /// <summary>Resolved Item ID.</summary>
        [JsonIgnore]
        public int? ItemId { get; set; }
        /// <summary>Resolved Base ID. Tile: TileID, Wall: WallID.</summary>
        [JsonIgnore]
        public int? BaseId { get; set; }
        /// <summary>Resolved Ammo Item ID.</summary>
        [JsonIgnore]
        public int? AmmoId { get; set; }

        /// <summary>
        /// Creates an InteractionEntry with optional ID resolution from names.
        /// Does not default layer; validation and logging happen at Add time.
        /// </summary>
        public static InteractionEntry Create(
            string? layer,
            string? main = null,
            int? mainId = null,
            string? item = null,
            int? itemId = null,
            string? @base = null,
            int? baseId = null,
            int? style = null,
            int? yield = null,
            string? ammo = null,
            int? ammoId = null)
        {
            var entry = new InteractionEntry
            {
                Layer = layer,
                Main = main,
                MainId = mainId,
                Item = item,
                ItemId = itemId,
                Base = @base,
                BaseId = baseId,
                Style = style,
                Yield = yield,
                Ammo = ammo,
                AmmoId = ammoId,
            };
            ResolveIds(entry);
            return entry;
        }

        /// <summary>
        /// Resolves name fields to IDs where IDs are missing. Idempotent.
        /// Uses ContentNameResolver for layer-aware Main and Base resolution.
        /// Skips resolution for Main/Base if layer is invalid.
        /// </summary>
        public static void ResolveIds(InteractionEntry entry)
        {
            if (!entry.ItemId.HasValue && !string.IsNullOrEmpty(entry.Item))
                entry.ItemId = ContentNameResolver.ResolveItem(entry.Item);
            if (!entry.BaseId.HasValue && !string.IsNullOrEmpty(entry.Base) && entry.Layer is not null)
                entry.BaseId = ContentNameResolver.ResolveBase(entry.Layer, entry.Base);
            if (!entry.AmmoId.HasValue && !string.IsNullOrEmpty(entry.Ammo))
                entry.AmmoId = ContentNameResolver.ResolveItem(entry.Ammo);
            if (!entry.MainId.HasValue && !string.IsNullOrEmpty(entry.Main) && entry.Layer is not null)
                entry.MainId = ContentNameResolver.ResolveMain(entry.Layer, entry.Main);
        }
    }

    /// <summary>
    /// Unified storage for all layer interactions. Dual-indexed for efficient tile-first and item-first lookup.
    /// </summary>
    public sealed class InteractionMap
    {
        private readonly Dictionary<(string Layer, int MainId, int Style), List<InteractionEntry>> _byMain = [];
        private readonly Dictionary<int, List<InteractionEntry>> _byItem = [];

        /// <summary>
        /// Adds an entry and indexes it for both Main and Item lookup.
        /// Skips and logs if required fields (layer, main, item) are missing.
        /// </summary>
        public void Add(InteractionEntry entry)
        {
            var normLayer = ContentNameResolver.NormalizeLayer(entry.Layer);
            if (normLayer is null)
            {
                TABLog.Warn($"Interaction skipped: layer missing or invalid (main={entry.Main}, item={entry.Item})", "InteractionMap");
                return;
            }
            entry.Layer = normLayer;
            InteractionEntry.ResolveIds(entry);

            var mainId = entry.MainId ?? -1;
            if (mainId < 0)
            {
                TABLog.Warn($"Interaction skipped: main unresolved (layer={normLayer}, main={entry.Main}, item={entry.Item})", "InteractionMap");
                return;
            }
            if (!entry.ItemId.HasValue || entry.ItemId.Value <= 0)
            {
                TABLog.Warn($"Interaction skipped: item unresolved (layer={normLayer}, main={entry.Main}, item={entry.Item})", "InteractionMap");
                return;
            }

            var style = entry.Style ?? 0;
            var mainKey = (entry.Layer, mainId, style);

            if (!_byMain.TryGetValue(mainKey, out var mainList))
            {
                mainList = [];
                _byMain[mainKey] = mainList;
            }
            mainList.Add(entry);

            var itemId = entry.ItemId ?? -1;
            if (itemId >= 0)
            {
                if (!_byItem.TryGetValue(itemId, out var itemList))
                {
                    itemList = [];
                    _byItem[itemId] = itemList;
                }
                itemList.Add(entry);
            }
        }

        /// <summary>
        /// Tile-first: gets entries for this main result. Style null/negative is treated as 0 (style-agnostic).
        /// </summary>
        public IReadOnlyList<InteractionEntry> GetByMain(string? layer, int mainId, int? style = null)
        {
            var normLayer = ContentNameResolver.NormalizeLayer(layer);
            if (normLayer is null) return [];
            var effectiveStyle = Math.Max(0, style ?? 0);
            return _byMain.TryGetValue((normLayer, mainId, effectiveStyle), out var list) ? list : [];
        }

        /// <summary>
        /// Item-first: gets entries for this item.
        /// </summary>
        public IReadOnlyList<InteractionEntry> GetByItem(int itemId)
        {
            return _byItem.TryGetValue(itemId, out var list) ? list : [];
        }

        /// <summary>
        /// Sorts all main-indexed lists by interaction priority (direct before conversion, etc.).
        /// Call after loading to ensure correct lookup order.
        /// </summary>
        internal void SortAllByPriority()
        {
            foreach (var list in _byMain.Values)
                list.Sort(InteractionPriorityComparer.Instance);
        }

        /// <summary>
        /// Gets the base tile ID for the preferred interaction (first in priority-sorted list).
        /// Null for direct placement; non-null for conversion.
        /// </summary>
        public int? GetRequiredBaseTile(int tileId, int? style = null)
        {
            var list = GetByMain("Tile", tileId, style);
            return list.Count > 0 ? list[0].BaseId : null;
        }

        /// <summary>
        /// Gets item IDs that can place this main result. Layer-agnostic.
        /// </summary>
        public IReadOnlyList<int> GetItemIds(string layer, int mainId, int? style = null)
        {
            var list = GetByMain(layer, mainId, style);
            var ids = new List<int>();
            foreach (var e in list)
            {
                if (e.ItemId.HasValue && e.ItemId.Value > 0 && !ids.Contains(e.ItemId.Value))
                    ids.Add(e.ItemId.Value);
            }
            return ids;
        }

        /// <summary>
        /// Gets item IDs that can place this tile. For export/iteration.
        /// </summary>
        public IReadOnlyList<int> GetTileItemIds(int tileId, int? style = null) =>
            GetItemIds("Tile", tileId, style);

        /// <summary>
        /// Gets item IDs that can place this wall.
        /// </summary>
        public IReadOnlyList<int> GetWallItemIds(int wallId) =>
            GetItemIds("Wall", wallId, null);

        /// <summary>
        /// Iterates all Tile layer entries grouped by (tileId, style). For PlacementDiscovery export.
        /// </summary>
        public IEnumerable<(int TileId, List<InteractionEntry> List)> GetAllTileEntries()
        {
            foreach (var kvp in _byMain)
            {
                if (!string.Equals(kvp.Key.Layer, "Tile", StringComparison.OrdinalIgnoreCase)) continue;
                yield return (kvp.Key.MainId, kvp.Value);
            }
        }

        /// <summary>
        /// Iterates all Wall layer entries grouped by wallId. For PlacementDiscovery export.
        /// </summary>
        public IEnumerable<(int WallId, List<InteractionEntry> List)> GetAllWallEntries()
        {
            foreach (var kvp in _byMain)
            {
                if (!string.Equals(kvp.Key.Layer, "Wall", StringComparison.OrdinalIgnoreCase)) continue;
                yield return (kvp.Key.MainId, kvp.Value);
            }
        }

        /// <summary>
        /// Exports all entries grouped by layer and main name. For ItrCompiler.
        /// Merges entries across styles for the same main. Deduplicates by (Item, Style, Base) for Tile, (Item) for Wall.
        /// </summary>
        public IReadOnlyDictionary<string, Dictionary<string, TileFirstExportGroup>> ExportByLayer()
        {
            var byLayer = new Dictionary<string, Dictionary<string, TileFirstExportGroup>>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _byMain)
            {
                var (layer, mainId, _) = kvp.Key;
                var mainName = ContentNameResolver.ResolveMainIdToName(layer, mainId);
                if (string.IsNullOrEmpty(mainName)) continue;

                if (!byLayer.TryGetValue(layer, out var byMain))
                {
                    byMain = new Dictionary<string, TileFirstExportGroup>(StringComparer.OrdinalIgnoreCase);
                    byLayer[layer] = byMain;
                }
                if (!byMain.TryGetValue(mainName, out var group))
                {
                    group = new TileFirstExportGroup();
                    byMain[mainName] = group;
                }

                foreach (var entry in kvp.Value)
                {
                    entry.Main = mainName;
                    if (!group.Interactions.Exists(x => IsExportDuplicate(layer, x, entry)))
                        group.Interactions.Add(entry);
                }
                group.Interactions.Sort(InteractionPriorityComparer.Instance);
            }

            return byLayer;
        }

        /// <summary>
        /// Enumerates export-canonical interaction entries (same dedupe rules as <see cref="ExportByLayer"/>).
        /// </summary>
        public IEnumerable<InteractionEntry> EnumerateDedupedEntries()
        {
            foreach (var layerKvp in ExportByLayer())
            {
                foreach (var mainKvp in layerKvp.Value)
                {
                    List<InteractionEntry> interactions = mainKvp.Value.Interactions;
                    for (int i = 0; i < interactions.Count; i++)
                        yield return interactions[i];
                }
            }
        }

        /// <summary>
        /// Whether two entries represent the same export interaction for the given layer.
        /// Tile: Item + Style + Base. Wall: Item. Other layers: never duplicate.
        /// </summary>
        private static bool IsExportDuplicate(string layer, InteractionEntry a, InteractionEntry b)
        {
            if (string.Equals(layer, "Tile", StringComparison.OrdinalIgnoreCase))
                return a.Item == b.Item && (a.Style ?? 0) == (b.Style ?? 0) && a.Base == b.Base;
            if (string.Equals(layer, "Wall", StringComparison.OrdinalIgnoreCase))
                return a.Item == b.Item;
            return false;
        }
    }

    /// <summary>
    /// Provides the current InteractionMap for paste operations. Set by ITRStorage.
    /// Layers and PastingInventory use this for lookups; gathering information from interactions
    /// is the responsibility of InteractionMap.
    /// </summary>
    internal static class InteractionMapProvider
    {
        internal static InteractionMap? Current { get; set; }

        /// <summary>
        /// Gets item IDs that can place the given tile type and style.
        /// </summary>
        /// <returns>List of item IDs, or null if no map is loaded or no items can place this tile.</returns>
        public static IReadOnlyList<int>? GetTileItemIds(ushort tileType, int? style = null)
        {
            var ids = Current?.GetTileItemIds(tileType, style);
            return ids is { Count: > 0 } ? ids : null;
        }

        /// <summary>
        /// Gets item IDs that can place the given wall type.
        /// </summary>
        /// <returns>List of item IDs, or null if no map is loaded or no items can place this wall.</returns>
        public static IReadOnlyList<int>? GetWallItemIds(ushort wallType)
        {
            var ids = Current?.GetWallItemIds(wallType);
            return ids is { Count: > 0 } ? ids : null;
        }

        /// <summary>
        /// Gets the base tile required for conversion placement.
        /// </summary>
        /// <returns>Tile ID of the base tile, or null if direct placement or no map loaded.</returns>
        public static int? GetRequiredBaseTile(ushort tileType, int? style = null)
            => Current?.GetRequiredBaseTile(tileType, style);

        /// <summary>
        /// Gets the first (preferred) interaction for the tile. For debug logging.
        /// </summary>
        internal static InteractionEntry? GetFirstTileInteraction(ushort tileType, int? style = null)
        {
            var list = Current?.GetByMain("Tile", tileType, style);
            return list is { Count: > 0 } ? list[0] : null;
        }
    }

    /// <summary>
    /// Tile-first map: Main (tile/wall/liquid name) -> list of interactions.
    /// Used for discovery output and compile merge.
    /// </summary>
    public sealed class TileFirstMap
    {
        private readonly Dictionary<string, List<InteractionEntry>> _entries = [];

        public void Add(string mainKey, InteractionEntry entry)
        {
            if (!_entries.TryGetValue(mainKey, out var list))
            {
                list = [];
                _entries[mainKey] = list;
            }
            list.Add(entry);
        }

        public IReadOnlyDictionary<string, List<InteractionEntry>> Entries => _entries;
    }

    /// <summary>
    /// Item-first map: Item name -> list of interactions.
    /// Used for CustomInteractionMap and ItemMap export.
    /// </summary>
    public sealed class ItemFirstMap
    {
        private readonly Dictionary<string, List<InteractionEntry>> _entries = [];

        public void Add(string itemKey, InteractionEntry entry)
        {
            if (!_entries.TryGetValue(itemKey, out var list))
            {
                list = [];
                _entries[itemKey] = list;
            }
            list.Add(entry);
        }

        public IReadOnlyDictionary<string, List<InteractionEntry>> Entries => _entries;
    }

    /// <summary>
    /// JSON converter for InteractionEntry. Tile-first: skips Main. Item-first: skips Item. Always skips null values.
    /// </summary>
    public sealed class InteractionEntryJsonConverter : JsonConverter<InteractionEntry>
    {
        private readonly bool _tileFirst;

        public InteractionEntryJsonConverter(bool tileFirst)
        {
            _tileFirst = tileFirst;
        }

        public override void Write(Utf8JsonWriter writer, InteractionEntry value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (_tileFirst && value.Item != null)
                writer.WriteString("Item", value.Item);
            if (!_tileFirst && value.Main != null)
                writer.WriteString("Main", value.Main);
            if (value.Base != null)
                writer.WriteString("Base", value.Base);
            if (value.Style.HasValue)
                writer.WriteNumber("Style", value.Style.Value);
            if (value.Yield.HasValue)
                writer.WriteNumber("Yield", value.Yield.Value);
            if (value.Ammo != null)
                writer.WriteString("Ammo", value.Ammo);
            if (value.Layer != null)
                writer.WriteString("Layer", value.Layer);
            writer.WriteEndObject();
        }

        public override InteractionEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                return null;
            var entry = new InteractionEntry();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var prop = reader.GetString();
                reader.Read();
                switch (prop)
                {
                    case "Item": entry.Item = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                    case "Main": entry.Main = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                    case "Base": entry.Base = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                    case "Style": entry.Style = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : null; break;
                    case "Yield": entry.Yield = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : null; break;
                    case "Ammo": entry.Ammo = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                    case "Layer": entry.Layer = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                }
            }
            return entry;
        }
    }

    /// <summary>
    /// Shared JSON options for interaction map serialization. Ignores null values. Tile-first skips Main; item-first skips Item.
    /// </summary>
    public static class InteractionMapJsonOptions
    {
        public static JsonSerializerOptions TileFirst { get; } = CreateTileFirst();
        public static JsonSerializerOptions ItemFirst { get; } = CreateItemFirst();

        private static JsonSerializerOptions CreateTileFirst()
        {
            var o = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            o.Converters.Add(new InteractionEntryJsonConverter(tileFirst: true));
            return o;
        }

        private static JsonSerializerOptions CreateItemFirst()
        {
            var o = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            o.Converters.Add(new InteractionEntryJsonConverter(tileFirst: false));
            return o;
        }
    }

    /// <summary>
    /// Compares interactions for paste priority. Direct placement before conversion; infinite before finite; no ammo before ammo.
    /// </summary>
    internal sealed class InteractionPriorityComparer : IComparer<InteractionEntry>
    {
        public static readonly InteractionPriorityComparer Instance = new();

        public int Compare(InteractionEntry? a, InteractionEntry? b)
        {
            if (a is null && b is null) return 0;
            if (a is null) return 1;
            if (b is null) return -1;

            int aBase = string.IsNullOrEmpty(a.Base) ? 0 : 1;
            int bBase = string.IsNullOrEmpty(b.Base) ? 0 : 1;
            int cmp = aBase.CompareTo(bBase);
            if (cmp != 0) return cmp;

            int aYield = a.Yield.HasValue ? 1 : 0;
            int bYield = b.Yield.HasValue ? 1 : 0;
            cmp = aYield.CompareTo(bYield);
            if (cmp != 0) return cmp;

            int aAmmo = string.IsNullOrEmpty(a.Ammo) ? 0 : 1;
            int bAmmo = string.IsNullOrEmpty(b.Ammo) ? 0 : 1;
            return aAmmo.CompareTo(bAmmo);
        }
    }

    /// <summary>
    /// Tile-first export group: main key -> interactions list. Used for JSON serialization.
    /// </summary>
    public sealed class TileFirstExportGroup
    {
        [JsonPropertyName("interactions")]
        public List<InteractionEntry> Interactions { get; set; } = [];
    }

    /// <summary>
    /// In-memory map of tile/wall -> interactions. Used by PlacementDiscovery.
    /// Wraps InteractionMap for Tile and Wall layers. placementBaseTiles is a layer-specific helper during discovery.
    /// </summary>
    public sealed class PlacementInteractionMap
    {
        private readonly InteractionMap _map = new();

        public void AddTile(int mainTileId, InteractionEntry entry)
        {
            entry.Layer = "Tile";
            entry.Main ??= ContentNameResolver.GetTileName(mainTileId);
            entry.MainId = mainTileId;
            InteractionEntry.ResolveIds(entry);
            _map.Add(entry);
        }

        public void AddWall(int mainWallId, InteractionEntry entry)
        {
            entry.Layer = "Wall";
            entry.Main ??= ContentNameResolver.GetWallName(mainWallId);
            entry.MainId = mainWallId;
            InteractionEntry.ResolveIds(entry);
            _map.Add(entry);
        }

        public IEnumerable<(int TileId, List<InteractionEntry> List)> GetAllTileEntries() =>
            _map.GetAllTileEntries();

        public IEnumerable<(int WallId, List<InteractionEntry> List)> GetAllWallEntries() =>
            _map.GetAllWallEntries();
    }
}
