using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
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
    /// and the cached InteractionMap loaded from a unified InteractionMap document.
    /// </summary>
    /// <remarks>
    /// <para><b>Pipeline overview</b></para>
    /// <list type="number">
    ///   <item><b>Compile-time</b>: TileMappingSystem.Compile writes InteractionMap.itr and InteractionMap.itr.json.</item>
    ///   <item><b>Paste start</b>: PastingSystem calls PrepareForPaste with tile/wall IDs from the projection.</item>
    ///   <item><b>Lookup</b>: Layers and PastingInventory use InteractionMapProvider.</item>
    ///   <item><b>Paste end</b>: When no operations remain, Release() clears the cache.</item>
    /// </list>
    /// </remarks>
    internal static class ItrStorage
    {
        private const string CompiledMapsDir = "CompiledMaps";
        private const string CompiledInteractionMapFileName = "InteractionMap.itr";
        private const string CompiledInteractionMapJsonFileName = "InteractionMap.itr.json";

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
        /// Gets full path for a file in TheArchitectsBlueprintMod\CompiledMaps.
        /// </summary>
        public static string GetCompiledMapPath(string fileName) =>
            Path.Combine(GetCompiledMapsDirectory(), fileName);

        /// <summary>
        /// Gets full path for the compact compiled interaction map.
        /// </summary>
        public static string GetCompiledInteractionMapPath() =>
            GetCompiledMapPath(CompiledInteractionMapFileName);

        /// <summary>
        /// Gets full path for the readable compiled interaction map.
        /// </summary>
        public static string GetCompiledInteractionMapJsonPath() =>
            GetCompiledMapPath(CompiledInteractionMapJsonFileName);

        /// <summary>
        /// Resolves binary and JSON paths for the InteractionMap used at paste load time.
        /// </summary>
        /// <remarks>
        /// Empty config uses CompiledMaps/InteractionMap; otherwise absolute/relative path with .itr / .itr.json stem rules.
        /// Compile output still always uses <see cref="GetCompiledInteractionMapPath"/>.
        /// </remarks>
        internal static void ResolveLoadPaths(out string binaryPath, out string jsonPath)
        {
            // --- Prefer config override; empty falls back to default compile output names.

            string? configured = TileMappingConfig.Instance?.CompiledInteractionMapPath;
            if (string.IsNullOrWhiteSpace(configured))
            {
                binaryPath = GetCompiledInteractionMapPath();
                jsonPath = GetCompiledInteractionMapJsonPath();
                return;
            }

            // Trim and resolve relative paths against the mod directory.
            string trimmed = configured.Trim();
            string resolved = Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(TileMappingSystem.GetModDirectory(), trimmed);

            // Split into binary (.itr) and readable (.itr.json) siblings from stem or extension.
            if (resolved.EndsWith(".itr.json", StringComparison.OrdinalIgnoreCase))
            {
                jsonPath = resolved;
                binaryPath = resolved[..^5]; // strip ".json"
            }
            else if (resolved.EndsWith(".itr", StringComparison.OrdinalIgnoreCase))
            {
                binaryPath = resolved;
                jsonPath = resolved + ".json";
            }
            else
            {
                binaryPath = resolved + ".itr";
                jsonPath = resolved + ".itr.json";
            }
        }

        /// <summary>
        /// True when this game instance may load CompiledMaps from disk for paste
        /// (single-player, dedicated server, or listen-server host).
        /// </summary>
        internal static bool CanLoadPasteInteractionMaps() => TileMappingAuthority.UsesLocalMappingFilesystem();

        /// <summary>
        /// Prepares the interaction map for paste by loading only the given tile and wall IDs.
        /// Call at paste start before any lookups. Replaces any existing cached map.
        /// </summary>
        /// <param name="tileIds">Tile IDs to load from InteractionMap.itr. Null = load all.</param>
        /// <param name="wallIds">Wall IDs to load from InteractionMap.itr. Null = load all.</param>
        /// <remarks>Remote multiplayer clients do not load from disk; only single-player, dedicated server, or listen-server host.</remarks>
        public static void PrepareForPaste(IReadOnlySet<int>? tileIds, IReadOnlySet<int>? wallIds)
        {
            if (!CanLoadPasteInteractionMaps())
                return;

            _map = null;
            InteractionMapProvider.Current = null;

            if (tileIds is null)
            {
                TryLoadInteractionMap(null, wallIds);
                return;
            }

            var expandedTileIds = new HashSet<int>(tileIds);
            if (!TryLoadInteractionMap(expandedTileIds, wallIds) || _map is null)
                return;

            // Expand conversion bases and Echo/fake place targets, then reload the filtered map.
            bool expandedBases = ExpandTileIdsWithRequiredBases(expandedTileIds, _map);
            bool expandedFakes = ExpandTileIdsWithFakePlaceTargets(expandedTileIds, _map);
            if (!expandedBases && !expandedFakes)
                return;

            _map = null;
            InteractionMapProvider.Current = null;
            TryLoadInteractionMap(expandedTileIds, wallIds);

            if (TABLog.IsDebugEnabled)
            {
                TABLog.Debug(
                    $"Interaction map loaded with {expandedTileIds.Count} tile ids (payload + conversion bases + fakes)",
                    "InteractionMap");
            }
        }

        private static bool TryLoadInteractionMap(IReadOnlySet<int>? tileIds, IReadOnlySet<int>? wallIds)
        {
            // --- Resolve configured load paths, try binary then JSON sibling.

            ResolveLoadPaths(out string binaryPath, out string jsonPath);
            TABLog.Info($"Loading interaction map binary='{binaryPath}' json='{jsonPath}'", "InteractionMap");

            if (File.Exists(binaryPath))
            {
                using var stream = File.OpenRead(binaryPath);
                if (InteractionMapTagIO.TryReadBinaryDocument(stream, out InteractionMap map, tileIds, wallIds, out string? error))
                {
                    _map = map;
                    InteractionMapProvider.Current = _map;
                    return true;
                }

                TABLog.Warn($"Failed to load compiled interaction map '{binaryPath}': {error}", "InteractionMap");
            }

            if (!File.Exists(jsonPath))
                return false;

            try
            {
                string json = File.ReadAllText(jsonPath);
                if (InteractionMapTagIO.TryReadJsonDocument(json, out InteractionMap map, tileIds, wallIds, out string? error))
                {
                    _map = map;
                    InteractionMapProvider.Current = _map;
                    return true;
                }

                TABLog.Warn($"Failed to load readable interaction map '{jsonPath}': {error}", "InteractionMap");
            }
            catch (Exception ex)
            {
                TABLog.Warn($"Failed to read readable interaction map '{jsonPath}': {ex.Message}", "InteractionMap");
            }

            return false;
        }

        /// <summary>Adds interaction-map base tile ids required by conversion placements in <paramref name="tileIds"/>.</summary>
        private static bool ExpandTileIdsWithRequiredBases(HashSet<int> tileIds, InteractionMap map)
        {
            int[] snapshot = [.. tileIds];

            for (int i = 0; i < snapshot.Length; i++)
                map.CollectConversionBaseTiles(snapshot[i], 0, tileIds);

            return tileIds.Count > snapshot.Length;
        }

        /// <summary>Adds FakeId / Echo place targets so filtered paste maps include Rubblemaker rows.</summary>
        private static bool ExpandTileIdsWithFakePlaceTargets(HashSet<int> tileIds, InteractionMap map)
        {
            // --- Snapshot first so newly added Echo ids are not re-walked in this pass.

            int[] snapshot = [.. tileIds];
            foreach ((int tileId, List<InteractionEntry> list) in map.GetAllTileEntries())
            {
                if (!tileIds.Contains(tileId))
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    InteractionEntry entry = list[i];
                    if (entry.FakeId is int fakeId && fakeId > 0)
                        tileIds.Add(fakeId);
                    if (entry.GetPlaceMainId() is int placeMain && placeMain > 0)
                        tileIds.Add(placeMain);
                }
            }

            // Ambient rubble payloads also need their Echo TileObjectData counterparts.
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] == TileID.SmallPiles)
                {
                    tileIds.Add(TileID.SmallPiles1x1Echo);
                    tileIds.Add(TileID.SmallPiles2x1Echo);
                }
                else if (snapshot[i] == TileID.LargePiles)
                    tileIds.Add(TileID.LargePilesEcho);
                else if (snapshot[i] == TileID.LargePiles2)
                    tileIds.Add(TileID.LargePiles2Echo);
                else if (snapshot[i] == TileID.PlantDetritus)
                {
                    tileIds.Add(TileID.PlantDetritus3x2Echo);
                    tileIds.Add(TileID.PlantDetritus2x2Echo);
                }
            }

            return tileIds.Count > snapshot.Length;
        }

        /// <summary>
        /// Releases the cached interaction map. Call when no paste operations remain.
        /// </summary>
        public static void Release()
        {
            _map = null;
            InteractionMapProvider.Current = null;
        }
    }

    /// <summary>
    /// Loads source .itr.json files into InteractionMap. Standardized pipeline for custom, placement, biome, shimmer.
    /// Config files (baseContext, biomeConfig) are loaded separately by their consumers.
    /// </summary>
    internal static class ItrLoader
    {
        private static readonly string[] InteractionMapTypes = ["custom", "wand", "rubble", "paint", "plant", "placement", "conversion", "biome", "shimmer"];

        /// <summary>
        /// Loads all interaction .itr.json files from the directory into the map.
        /// Order: custom, wand, rubble, paint, plant, placement, conversion, biome, shimmer (shimmer applies Shimmer Bucket transform).
        /// Custom loads before generated maps so same-item curated overrides win export dedupe;
        /// cross-item priority still comes from <see cref="InteractionPriorityComparer"/> (e.g. yield 1 before bombs).
        /// </summary>
        public static void LoadAllInto(string directory, InteractionMap map)
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
                            LoadShimmer(dataEl, map);
                        else if (string.Equals(format, "item-first", StringComparison.OrdinalIgnoreCase))
                            LoadItemFirst(dataEl, map);
                        else if (string.Equals(format, "tile-first", StringComparison.OrdinalIgnoreCase))
                            LoadTileFirst(dataEl, map);
                    });
                }
                catch (Exception ex)
                {
                    TABLog.Warn($"{mapType}: {ex.Message}", "TileMapping");
                }
            }
        }

        private static bool IsMapTypeEnabled(TileMappingConfig cfg, string mapType) =>
            mapType switch
            {
                "custom" => cfg.EnableCustomInteractionMap,
                "wand" => cfg.EnableWandDiscovery,
                "rubble" => cfg.EnableRubbleDiscovery,
                "paint" => cfg.EnablePaintDiscovery,
                "plant" => cfg.EnablePlantDiscovery,
                "placement" => cfg.EnablePlacementDiscovery,
                "conversion" => cfg.EnableConversionDiscovery,
                "biome" => cfg.EnableBiomeConversions,
                "shimmer" => cfg.EnableShimmerTransforms,
                _ => true,
            };

        private static void LoadItemFirst(JsonElement dataEl, InteractionMap map)
        {
            var itemEl = ItrFileProcessor.GetItemFirstRoot(dataEl);
            if (!itemEl.HasValue) return;

            foreach (var itemProp in itemEl.Value.EnumerateObject())
            {
                string item = itemProp.Name;
                if (!itemProp.Value.TryGetProperty("interactions", out var arr)) continue;

                foreach (var ia in arr.EnumerateArray())
                {
                    var entry = ParseItemFirstInteraction(ia, item);
                    if (entry is not null)
                        map.Add(entry);
                }
            }
        }

        private static void LoadShimmer(JsonElement dataEl, InteractionMap map)
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
                    var entry = ParseItemFirstInteraction(ia, shimmerBucket);
                    if (entry is not null)
                    {
                        entry.Ammo = sourceItem;
                        map.Add(entry);
                    }
                }
            }
        }

        private static InteractionEntry? ParseItemFirstInteraction(JsonElement ia, string item)
        {
            var layer = ia.TryGetProperty("layer", out var l) ? l.GetString() : null;
            if (string.IsNullOrEmpty(layer))
            {
                TABLog.Warn($"Interaction skipped (layer missing, item={item})", "InteractionMap");
                return null;
            }
            var normLayer = ContentNameResolver.NormalizeLayer(layer);
            if (normLayer is null) return null;

            string? main = ResolveMainFromJson(ia, normLayer);
            if (string.IsNullOrEmpty(main))
            {
                TABLog.Warn($"Interaction skipped (main missing, item={item})", "InteractionMap");
                return null;
            }

            var entry = new InteractionEntry
            {
                Layer = normLayer,
                Item = item,
                Main = main,
                Yield = ia.TryGetProperty("yield", out var y) && y.TryGetInt32(out int yv) ? yv : null,
                Ammo = ia.TryGetProperty("ammo", out var a) ? a.GetString() : null,
                Fake = ia.TryGetProperty("fake", out var f) ? f.GetString() : null,
                Alias = ReadAliasNames(ia),
            };

            if (string.Equals(normLayer, "Tile", StringComparison.OrdinalIgnoreCase) || string.Equals(normLayer, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                entry.Base = ia.TryGetProperty("base", out var b) && b.ValueKind != JsonValueKind.Null ? b.GetString() : null;
                entry.Style = ia.TryGetProperty("style", out var s) && s.TryGetInt32(out int sv) ? sv : null;
            }

            return entry;
        }

        /// <summary>Reads camelCase or PascalCase alias string arrays from an interaction element.</summary>
        private static List<string>? ReadAliasNames(JsonElement ia)
        {
            // --- Prefer camelCase alias, then PascalCase Alias.

            if (ia.TryGetProperty("alias", out var aliasEl) || ia.TryGetProperty("Alias", out aliasEl))
            {
                if (aliasEl.ValueKind != JsonValueKind.Array)
                    return null;

                var list = new List<string>();
                foreach (var el in aliasEl.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.String)
                        continue;
                    string? name = el.GetString();
                    if (!string.IsNullOrEmpty(name))
                        list.Add(name);
                }

                return list.Count > 0 ? list : null;
            }

            return null;
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

        private static void LoadTileFirst(JsonElement dataEl, InteractionMap map)
        {
            if (dataEl.TryGetProperty("tiles", out var tilesEl) || dataEl.TryGetProperty("Tiles", out tilesEl))
            {
                foreach (var tileProp in tilesEl.EnumerateObject())
                {
                    string main = tileProp.Name;
                    if (!tileProp.Value.TryGetProperty("interactions", out var arr)) continue;

                    foreach (var ia in arr.EnumerateArray())
                    {
                        var entry = ParseTileFirstInteraction(ia, "Tile", main);
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
                                TABLog.Warn($"Placement: interaction skipped (item missing, main={main})", "InteractionMap");
                                continue;
                            }
                            map.Add(new InteractionEntry { Layer = "Wall", Main = main, Item = item });
                        }
                    }
                    else if (val.TryGetProperty("interactions", out var arr))
                    {
                        foreach (var ia in arr.EnumerateArray())
                        {
                            var entry = ParseTileFirstInteraction(ia, "Wall", main);
                            if (entry is not null)
                                map.Add(entry);
                        }
                    }
                }
            }
        }

        private static InteractionEntry? ParseTileFirstInteraction(JsonElement ia, string layer, string main)
        {
            string? item = ia.TryGetProperty("Item", out var i) ? i.GetString() : null;
            if (string.IsNullOrEmpty(item))
            {
                TABLog.Warn("Placement: interaction skipped (item missing)", "InteractionMap");
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
                Fake = ia.TryGetProperty("Fake", out var f) ? f.GetString() : null,
                Alias = ReadAliasNames(ia),
            };

            if (string.Equals(layer, "Wall", StringComparison.OrdinalIgnoreCase))
                entry.Base = null;

            return entry;
        }
    }
}
