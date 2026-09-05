using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Terraria.ModLoader.IO;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Serializes compiled interaction maps into a compact tag-based format and a readable JSON mirror.
    /// </summary>
    internal static class InteractionMapTagIO
    {
        public const string FormatId = "TheArchitectsBlueprintMod.InteractionMap";
        public const int SchemaVersion = 1;

        private const string KeyFormat = "format";
        private const string KeySchemaVersion = "schemaVersion";
        private const string KeyMetadata = "metadata";
        private const string KeyEntries = "entries";

        private const string KeyLayer = "l";
        private const string KeyMainId = "m";
        private const string KeyItemId = "i";
        private const string KeyBaseId = "b";
        private const string KeyStyle = "s";
        private const string KeyYield = "y";
        private const string KeyAmmoId = "a";
        private const string KeyFakeId = "f";
        private const string KeyAliasIds = "alias";

        public static void WriteBinaryDocument(Stream stream, InteractionMap map, InteractionMapMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(metadata);

            TagCompound root = BuildBinaryDocument(map, metadata);
            TagIO.ToStream(root, stream, compress: true);
        }

        public static void WriteJsonDocument(Stream stream, InteractionMap map, InteractionMapMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(metadata);

            var document = new InteractionMapDocument<IReadOnlyDictionary<string, Dictionary<string, TileFirstExportGroup>>>
            {
                Metadata = metadata,
                Data = map.ExportByLayer(),
            };

            string json = JsonSerializer.Serialize(document, InteractionMapJsonOptions.TileFirst);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true);
            writer.Write(json);
        }

        public static bool TryReadBinaryDocument(
            Stream stream,
            out InteractionMap map,
            IReadOnlySet<int>? tileIdsToLoad,
            IReadOnlySet<int>? wallIdsToLoad,
            out string? error)
        {
            map = new InteractionMap();
            error = null;
            try
            {
                TagCompound root = TagIO.FromStream(stream);
                return TryReadBinaryDocument(root, out map, tileIdsToLoad, wallIdsToLoad, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                map = new InteractionMap();
                return false;
            }
        }

        public static bool TryReadJsonDocument(
            string json,
            out InteractionMap map,
            IReadOnlySet<int>? tileIdsToLoad,
            IReadOnlySet<int>? wallIdsToLoad,
            out string? error)
        {
            map = new InteractionMap();
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "Interaction map JSON is empty.";
                    return false;
                }

                using var doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = "Interaction map JSON root must be an object.";
                    return false;
                }

                if (!root.TryGetProperty("data", out JsonElement dataEl) || dataEl.ValueKind != JsonValueKind.Object)
                {
                    error = "Interaction map JSON is missing data.";
                    return false;
                }

                foreach (JsonProperty layerProp in dataEl.EnumerateObject())
                    ReadLayerData(layerProp.Name, layerProp.Value, map, tileIdsToLoad, wallIdsToLoad);

                map.SortAllByPriority();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                map = new InteractionMap();
                return false;
            }
        }

        private static TagCompound BuildBinaryDocument(InteractionMap map, InteractionMapMetadata metadata)
        {
            var entries = new List<TagCompound>();
            foreach (InteractionEntry entry in map.EnumerateDedupedEntries())
            {
                string? layer = ContentNameResolver.NormalizeLayer(entry.Layer);
                int? layerKind = LayerToKind(layer);
                if (!layerKind.HasValue || !entry.MainId.HasValue || !entry.ItemId.HasValue)
                    continue;

                var entryTag = new TagCompound
                {
                    [KeyLayer] = layerKind.Value,
                    [KeyMainId] = entry.MainId.Value,
                    [KeyItemId] = entry.ItemId.Value,
                };

                if (entry.BaseId.HasValue)
                    entryTag[KeyBaseId] = entry.BaseId.Value;
                if (entry.Style.HasValue)
                    entryTag[KeyStyle] = entry.Style.Value;
                if (entry.Yield.HasValue)
                    entryTag[KeyYield] = entry.Yield.Value;
                if (entry.AmmoId.HasValue)
                    entryTag[KeyAmmoId] = entry.AmmoId.Value;
                if (entry.FakeId.HasValue)
                    entryTag[KeyFakeId] = entry.FakeId.Value;
                if (entry.AliasIds is { Count: > 0 })
                    entryTag[KeyAliasIds] = entry.AliasIds;

                entries.Add(entryTag);
            }

            return new TagCompound
            {
                [KeyFormat] = FormatId,
                [KeySchemaVersion] = SchemaVersion,
                [KeyMetadata] = MetadataToTag(metadata),
                [KeyEntries] = entries,
            };
        }

        private static bool TryReadBinaryDocument(
            TagCompound root,
            out InteractionMap map,
            IReadOnlySet<int>? tileIdsToLoad,
            IReadOnlySet<int>? wallIdsToLoad,
            out string? error)
        {
            map = new InteractionMap();
            error = null;

            if (!root.ContainsKey(KeyFormat) || !string.Equals(root.GetString(KeyFormat), FormatId, StringComparison.Ordinal))
            {
                error = "Unrecognized interaction map format.";
                return false;
            }

            int schema = root.GetInt(KeySchemaVersion);
            if (schema != SchemaVersion)
            {
                error = $"Unsupported interaction map schema version {schema}.";
                return false;
            }

            if (!root.ContainsKey(KeyEntries))
            {
                error = "Interaction map is missing entries.";
                return false;
            }

            IList<TagCompound> entries = root.GetList<TagCompound>(KeyEntries);
            for (int i = 0; i < entries.Count; i++)
            {
                TagCompound tag = entries[i];
                if (!TryGetInt32(tag, KeyLayer, out int layerKind) ||
                    !TryGetInt32(tag, KeyMainId, out int mainId) ||
                    !TryGetInt32(tag, KeyItemId, out int itemId))
                    continue;

                string? layer = KindToLayer(layerKind);
                if (layer is null || !ShouldLoadMain(layer, mainId, tileIdsToLoad, wallIdsToLoad))
                    continue;

                int? baseId = TryGetInt32(tag, KeyBaseId, out int b) ? b : null;
                int? style = TryGetInt32(tag, KeyStyle, out int s) ? s : null;
                int? yield = TryGetInt32(tag, KeyYield, out int y) ? y : null;
                int? ammoId = TryGetInt32(tag, KeyAmmoId, out int a) ? a : null;
                int? fakeId = TryGetInt32(tag, KeyFakeId, out int f) ? f : null;
                List<int>? aliasIds = TryGetIntList(tag, KeyAliasIds);

                InteractionEntry entry = InteractionEntry.Create(
                    layer: layer,
                    main: null,
                    mainId: mainId,
                    item: null,
                    itemId: itemId,
                    @base: null,
                    baseId: baseId,
                    style: style,
                    yield: yield,
                    ammo: null,
                    ammoId: ammoId,
                    aliasIds: aliasIds,
                    fakeId: fakeId);
                map.Add(entry);
            }

            map.SortAllByPriority();
            return true;
        }

        private static void ReadLayerData(
            string layerName,
            JsonElement layerData,
            InteractionMap map,
            IReadOnlySet<int>? tileIdsToLoad,
            IReadOnlySet<int>? wallIdsToLoad)
        {
            string? layer = ContentNameResolver.NormalizeLayer(layerName);
            if (layer is null || layerData.ValueKind != JsonValueKind.Object)
                return;

            foreach (JsonProperty mainProp in layerData.EnumerateObject())
            {
                string mainName = mainProp.Name;
                int? mainId = ContentNameResolver.ResolveMain(layer, mainName);
                if (!mainId.HasValue || !ShouldLoadMain(layer, mainId.Value, tileIdsToLoad, wallIdsToLoad))
                    continue;

                JsonElement mainObj = mainProp.Value;
                if (mainObj.ValueKind != JsonValueKind.Object ||
                    !mainObj.TryGetProperty("interactions", out JsonElement interactions) ||
                    interactions.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement interactionEl in interactions.EnumerateArray())
                {
                    if (interactionEl.ValueKind != JsonValueKind.Object)
                        continue;

                    string? itemName = interactionEl.TryGetProperty("Item", out JsonElement itemEl) && itemEl.ValueKind == JsonValueKind.String
                        ? itemEl.GetString()
                        : null;
                    if (string.IsNullOrEmpty(itemName))
                        continue;

                    int? itemId = ContentNameResolver.ResolveItem(itemName);
                    if (!itemId.HasValue || itemId.Value <= 0)
                        continue;

                    string? baseName = interactionEl.TryGetProperty("Base", out JsonElement baseEl) && baseEl.ValueKind == JsonValueKind.String
                        ? baseEl.GetString()
                        : null;
                    int? baseId = baseName is not null ? ContentNameResolver.ResolveBase(layer, baseName) : null;

                    int? style = interactionEl.TryGetProperty("Style", out JsonElement styleEl) && styleEl.ValueKind == JsonValueKind.Number && styleEl.TryGetInt32(out int styleValue)
                        ? styleValue
                        : null;
                    int? yield = interactionEl.TryGetProperty("Yield", out JsonElement yieldEl) && yieldEl.ValueKind == JsonValueKind.Number && yieldEl.TryGetInt32(out int yieldValue)
                        ? yieldValue
                        : null;

                    string? ammoName = interactionEl.TryGetProperty("Ammo", out JsonElement ammoEl) && ammoEl.ValueKind == JsonValueKind.String
                        ? ammoEl.GetString()
                        : null;
                    int? ammoId = ammoName is not null ? ContentNameResolver.ResolveItem(ammoName) : null;

                    string? fakeName = interactionEl.TryGetProperty("Fake", out JsonElement fakeEl) && fakeEl.ValueKind == JsonValueKind.String
                        ? fakeEl.GetString()
                        : null;
                    int? fakeId = fakeName is not null ? ContentNameResolver.ResolveMain(layer, fakeName) : null;
                    List<string>? aliasNames = ReadStringArrayProperty(interactionEl, "Alias");

                    InteractionEntry entry = InteractionEntry.Create(
                        layer: layer,
                        main: mainName,
                        mainId: mainId.Value,
                        item: itemName,
                        itemId: itemId.Value,
                        @base: baseName,
                        baseId: baseId,
                        style: style,
                        yield: yield,
                        ammo: ammoName,
                        ammoId: ammoId,
                        alias: aliasNames,
                        fake: fakeName,
                        fakeId: fakeId);
                    map.Add(entry);
                }
            }
        }

        /// <summary>Reads a JSON string-array property into a list of non-empty names.</summary>
        private static List<string>? ReadStringArrayProperty(JsonElement parent, string propertyName)
        {
            // --- Require an array of strings; skip empties.

            if (!parent.TryGetProperty(propertyName, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
                return null;

            var list = new List<string>();
            foreach (JsonElement el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String)
                    continue;
                string? value = el.GetString();
                if (!string.IsNullOrEmpty(value))
                    list.Add(value);
            }

            return list.Count > 0 ? list : null;
        }

        /// <summary>Reads a TagCompound int list used for tool alias ids.</summary>
        private static List<int>? TryGetIntList(TagCompound tag, string key)
        {
            // --- Accept List`int` from TagIO; return null when missing or empty.

            if (!tag.ContainsKey(key))
                return null;

            IList<int> raw = tag.GetList<int>(key);
            if (raw is null || raw.Count == 0)
                return null;

            var list = new List<int>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                if (raw[i] > 0)
                    list.Add(raw[i]);
            }

            return list.Count > 0 ? list : null;
        }

        private static TagCompound MetadataToTag(InteractionMapMetadata metadata)
        {
            var tag = new TagCompound();
            if (!string.IsNullOrEmpty(metadata.Format))
                tag["format"] = metadata.Format;
            if (!string.IsNullOrEmpty(metadata.MapType))
                tag["mapType"] = metadata.MapType;
            if (!string.IsNullOrEmpty(metadata.ModVersion))
                tag["modVersion"] = metadata.ModVersion;
            if (!string.IsNullOrEmpty(metadata.TerrariaVersion))
                tag["terrariaVersion"] = metadata.TerrariaVersion;
            if (metadata.ModsLoaded is { Count: > 0 })
                tag["modsLoaded"] = metadata.ModsLoaded;
            if (metadata.GeneratedAt.HasValue)
                tag["generatedAt"] = metadata.GeneratedAt.Value.ToString("O");
            return tag;
        }

        private static bool ShouldLoadMain(string layer, int mainId, IReadOnlySet<int>? tileIdsToLoad, IReadOnlySet<int>? wallIdsToLoad)
        {
            if (string.Equals(layer, "Tile", StringComparison.Ordinal))
                return tileIdsToLoad is null || tileIdsToLoad.Contains(mainId);

            if (string.Equals(layer, "Wall", StringComparison.Ordinal))
                return wallIdsToLoad is null || wallIdsToLoad.Contains(mainId);

            return true;
        }

        private static int? LayerToKind(string? layer) =>
            layer switch
            {
                "Tile" => 0,
                "Wall" => 1,
                "Liquid" => 2,
                "Wire" => 3,
                "Paint" => 4,
                _ => null,
            };

        private static string? KindToLayer(int layerKind) =>
            layerKind switch
            {
                0 => "Tile",
                1 => "Wall",
                2 => "Liquid",
                3 => "Wire",
                4 => "Paint",
                _ => null,
            };

        private static bool TryGetInt32(TagCompound tag, string key, out int value)
        {
            value = 0;
            if (!tag.ContainsKey(key))
                return false;

            object raw = tag[key];
            switch (raw)
            {
                case int i:
                    value = i;
                    return true;
                case short s:
                    value = s;
                    return true;
                case byte b:
                    value = b;
                    return true;
                case long l:
                    value = (int)l;
                    return true;
                default:
                    return false;
            }
        }
    }
}
