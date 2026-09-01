using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
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
        /// <summary>Stack debits per placement; omit or null defaults to 1 at runtime. Use -1 (or any ≤ 0) for infinite.</summary>
        public int? Yield { get; set; }
        /// <summary>Ammo consumed instead of the tool.</summary>
        public string? Ammo { get; set; }
        /// <summary>Alternate tool item names accepted when the primary item is missing.</summary>
        public List<string>? Alias { get; set; }
        /// <summary>Tile/wall actually placed when different from <see cref="Main"/> (e.g. Echo rubble).</summary>
        public string? Fake { get; set; }

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
        /// <summary>Resolved alternate tool item IDs.</summary>
        [JsonIgnore]
        public List<int>? AliasIds { get; set; }
        /// <summary>Resolved place-target ID for <see cref="Fake"/>.</summary>
        [JsonIgnore]
        public int? FakeId { get; set; }

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
            int? ammoId = null,
            IReadOnlyList<string>? alias = null,
            IReadOnlyList<int>? aliasIds = null,
            string? fake = null,
            int? fakeId = null)
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
                Alias = alias is { Count: > 0 } ? [.. alias] : null,
                AliasIds = aliasIds is { Count: > 0 } ? [.. aliasIds] : null,
                Fake = fake,
                FakeId = fakeId,
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
            // --- Resolve item/base/ammo/main, then alias names and fake place target.

            if (!entry.ItemId.HasValue && !string.IsNullOrEmpty(entry.Item))
                entry.ItemId = ContentNameResolver.ResolveItem(entry.Item);
            if (!entry.BaseId.HasValue && !string.IsNullOrEmpty(entry.Base) && entry.Layer is not null)
                entry.BaseId = ContentNameResolver.ResolveBase(entry.Layer, entry.Base);
            if (!entry.AmmoId.HasValue && !string.IsNullOrEmpty(entry.Ammo))
                entry.AmmoId = ContentNameResolver.ResolveItem(entry.Ammo);
            if (!entry.MainId.HasValue && !string.IsNullOrEmpty(entry.Main) && entry.Layer is not null)
                entry.MainId = ContentNameResolver.ResolveMain(entry.Layer, entry.Main);

            // Resolve alias item names into AliasIds when ids are still missing.
            if (entry.Alias is { Count: > 0 })
            {
                entry.AliasIds ??= [];
                for (int i = 0; i < entry.Alias.Count; i++)
                {
                    string name = entry.Alias[i];
                    if (string.IsNullOrEmpty(name))
                        continue;
                    int? id = ContentNameResolver.ResolveItem(name);
                    if (id is > 0 && !entry.AliasIds.Contains(id.Value))
                        entry.AliasIds.Add(id.Value);
                }

                if (entry.AliasIds.Count == 0)
                    entry.AliasIds = null;
            }

            if (!entry.FakeId.HasValue && !string.IsNullOrEmpty(entry.Fake) && entry.Layer is not null)
                entry.FakeId = ContentNameResolver.ResolveMain(entry.Layer, entry.Fake);
        }

        /// <summary>Returns primary tool id plus any resolved alias tool ids.</summary>
        public IReadOnlyList<int> GetToolItemIds()
        {
            // --- Primary item first, then each alias id that is distinct.

            if (!ItemId.HasValue || ItemId.Value <= 0)
                return [];

            if (AliasIds is not { Count: > 0 })
                return [ItemId.Value];

            var ids = new List<int>(1 + AliasIds.Count) { ItemId.Value };
            for (int i = 0; i < AliasIds.Count; i++)
            {
                int aliasId = AliasIds[i];
                if (aliasId > 0 && aliasId != ItemId.Value && !ids.Contains(aliasId))
                    ids.Add(aliasId);
            }

            return ids;
        }

        /// <summary>Returns the tile/wall id to place: FakeId when set, otherwise MainId.</summary>
        public int? GetPlaceMainId() => FakeId ?? MainId;

        /// <summary>Returns the item icon for arrows and UI: distinct ammo when set, otherwise the tool item.</summary>
        public int GetVisualItemId()
        {
            // --- Match PlacementReservationLedger / ConsumableItemId used by consuming place arrows.

            if (AmmoId is int ammo && ammo > 0)
                return ammo;

            return ItemId is int item && item > 0 ? item : 0;
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

            // Index alias tool ids so Medium/Large Rubblemaker lookups find the same entries.
            if (entry.AliasIds is { Count: > 0 })
            {
                for (int i = 0; i < entry.AliasIds.Count; i++)
                {
                    int aliasId = entry.AliasIds[i];
                    if (aliasId <= 0 || aliasId == itemId)
                        continue;

                    if (!_byItem.TryGetValue(aliasId, out var aliasList))
                    {
                        aliasList = [];
                        _byItem[aliasId] = aliasList;
                    }

                    aliasList.Add(entry);
                }
            }
        }

        /// <summary>
        /// Tile-first: gets entries for this main result. Style null/negative is treated as 0 (style-agnostic).
        /// </summary>
        public IReadOnlyList<InteractionEntry> GetByMain(string? layer, int mainId, int? style = null)
        {
            // --- Exact style, then open/closed furniture alias, then style 0 so framed plants still match.

            var normLayer = ContentNameResolver.NormalizeLayer(layer);
            if (normLayer is null) return [];

            var effectiveStyle = Math.Max(0, style ?? 0);
            if (_byMain.TryGetValue((normLayer, mainId, effectiveStyle), out var list))
                return list;

            bool isTile = string.Equals(normLayer, "Tile", StringComparison.Ordinal);
            if (isTile
                && TryGetAlternateFurnitureTile(mainId, out int alternateId)
                && _byMain.TryGetValue((normLayer, alternateId, effectiveStyle), out var alternateList))
                return alternateList;

            if (effectiveStyle != 0
                && _byMain.TryGetValue((normLayer, mainId, 0), out var styleAgnostic))
                return styleAgnostic;

            if (isTile
                && effectiveStyle != 0
                && TryGetAlternateFurnitureTile(mainId, out int alternateStyle0)
                && _byMain.TryGetValue((normLayer, alternateStyle0, 0), out var alternateAgnostic))
                return alternateAgnostic;

            return [];
        }

        /// <summary>
        /// Returns the open/closed counterpart tile for doors, trapdoors, and tall gates.
        /// </summary>
        internal static bool TryGetAlternateFurnitureTile(int tileId, out int alternateTileId)
        {
            // --- Prefer TileID.Sets door pairs, then vanilla trapdoor and tall-gate counterparts.

            alternateTileId = -1;
            if (tileId < 0)
                return false;

            if (tileId < TileID.Sets.OpenDoorID.Length)
            {
                int openId = TileID.Sets.OpenDoorID[tileId];
                if (openId >= 0)
                {
                    alternateTileId = openId;
                    return true;
                }
            }

            if (tileId < TileID.Sets.CloseDoorID.Length)
            {
                int closeId = TileID.Sets.CloseDoorID[tileId];
                if (closeId >= 0)
                {
                    alternateTileId = closeId;
                    return true;
                }
            }

            alternateTileId = tileId switch
            {
                TileID.ClosedDoor => TileID.OpenDoor,
                TileID.OpenDoor => TileID.ClosedDoor,
                TileID.TrapdoorClosed => TileID.TrapdoorOpen,
                TileID.TrapdoorOpen => TileID.TrapdoorClosed,
                TileID.TallGateClosed => TileID.TallGateOpen,
                TileID.TallGateOpen => TileID.TallGateClosed,
                _ => -1,
            };
            return alternateTileId >= 0;
        }

        /// <summary>
        /// Item-first: gets entries for this item.
        /// </summary>
        public IReadOnlyList<InteractionEntry> GetByItem(int itemId)
        {
            return _byItem.TryGetValue(itemId, out var list) ? list : [];
        }

        /// <summary>
        /// Sorts all main-indexed lists by interaction priority, then ascending item main-coverage.
        /// </summary>
        internal void SortAllByPriority()
        {
            // --- Prefer specialized tools first within a priority class so unmet preferred stays color-specific.

            Dictionary<int, int> coverageByItemId = BuildItemMainCoverage();
            foreach (var list in _byMain.Values)
            {
                list.Sort((a, b) =>
                {
                    int cmp = InteractionPriorityComparer.Instance.Compare(a, b);
                    if (cmp != 0)
                        return cmp;

                    return GetItemCoverage(a, coverageByItemId).CompareTo(GetItemCoverage(b, coverageByItemId));
                });
            }
        }

        /// <summary>Counts distinct (layer, mainId) placements each tool item can perform.</summary>
        private Dictionary<int, int> BuildItemMainCoverage()
        {
            // --- Walk item-indexed entries and count unique mains per tool id.

            var coverageByItemId = new Dictionary<int, int>();
            foreach (KeyValuePair<int, List<InteractionEntry>> kvp in _byItem)
            {
                var seen = new HashSet<(string Layer, int MainId)>();
                List<InteractionEntry> entries = kvp.Value;
                for (int i = 0; i < entries.Count; i++)
                {
                    InteractionEntry entry = entries[i];
                    if (entry.Layer is null || !entry.MainId.HasValue)
                        continue;

                    seen.Add((entry.Layer, entry.MainId.Value));
                }

                coverageByItemId[kvp.Key] = seen.Count;
            }

            return coverageByItemId;
        }

        /// <summary>Returns how many distinct mains the entry's tool can place, or 0 when unknown.</summary>
        private static int GetItemCoverage(InteractionEntry entry, Dictionary<int, int> coverageByItemId)
        {
            // --- Lookup coverage for the resolved tool item id.

            if (entry.ItemId is not int itemId || itemId <= 0)
                return 0;

            return coverageByItemId.TryGetValue(itemId, out int coverage) ? coverage : 0;
        }

        /// <summary>
        /// Gets the support-base tile for the preferred placeable interaction, or null for direct/biome-only paths.
        /// </summary>
        public int? GetRequiredBaseTile(int tileId, int? style = null)
        {
            // --- Only moss/seed-style placeables require a support base; biome powders must not cascade peer tiles.

            var list = GetByMain("Tile", tileId, style);
            for (int i = 0; i < list.Count; i++)
            {
                InteractionEntry entry = list[i];
                if (!entry.BaseId.HasValue)
                    return null;

                if (IsPlaceableSupportConversion(entry))
                    return entry.BaseId;
            }

            return null;
        }

        /// <summary>True when the interaction's tool places a tile (createTile), so Base is a support underlay.</summary>
        private static bool IsPlaceableSupportConversion(InteractionEntry entry)
        {
            // --- Biome powders/solutions have no createTile; placement items (seeds, moss) do.

            if (entry.ItemId is not int itemId || itemId <= 0)
                return false;

            if (!ContentSamples.ItemsByType.TryGetValue(itemId, out Item? sample) || sample is null)
                return false;

            return sample.createTile >= TileID.Dirt;
        }

        /// <summary>
        /// Adds every conversion base tile referenced by interactions for <paramref name="tileId"/>.
        /// </summary>
        internal void CollectConversionBaseTiles(int tileId, int? style, ISet<int> tileIds)
        {
            var list = GetByMain("Tile", tileId, style);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].BaseId is int baseId)
                    tileIds.Add(baseId);
            }
        }

        /// <summary>
        /// Gets item IDs that can place this main result. Layer-agnostic.
        /// </summary>
        public IReadOnlyList<int> GetItemIds(string layer, int mainId, int? style = null)
        {
            // --- Collect place tools; keep support-base placeables, skip biome powder converts.

            bool tileLayer = string.Equals(layer, "Tile", StringComparison.OrdinalIgnoreCase);
            var list = GetByMain(layer, mainId, style);
            var ids = new List<int>();
            foreach (var e in list)
            {
                bool hasBase = e.BaseId.HasValue || !string.IsNullOrEmpty(e.Base);
                if (tileLayer && hasBase && !IsPlaceableSupportConversion(e))
                    continue;

                IReadOnlyList<int> toolIds = e.GetToolItemIds();
                for (int i = 0; i < toolIds.Count; i++)
                {
                    int toolId = toolIds[i];
                    if (toolId > 0 && !ids.Contains(toolId))
                        ids.Add(toolId);
                }
            }
            return ids;
        }

        /// <summary>Finds an interaction whose fake place target matches the given main id.</summary>
        internal InteractionEntry? FindByFakeMain(string? layer, int fakeMainId, int? style = null)
        {
            // --- Scan compiled rows; prefer exact style, then style-agnostic (0).

            var normLayer = ContentNameResolver.NormalizeLayer(layer);
            if (normLayer is null || fakeMainId <= 0)
                return null;

            int effectiveStyle = Math.Max(0, style ?? 0);
            InteractionEntry? styleMatch = null;
            InteractionEntry? styleAgnostic = null;

            foreach (KeyValuePair<(string Layer, int MainId, int Style), List<InteractionEntry>> kvp in _byMain)
            {
                if (!string.Equals(kvp.Key.Layer, normLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                List<InteractionEntry> list = kvp.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    InteractionEntry entry = list[i];
                    if (entry.FakeId != fakeMainId)
                        continue;

                    int entryStyle = entry.Style ?? 0;
                    if (entryStyle == effectiveStyle)
                        return entry;

                    if (entryStyle == 0)
                        styleAgnostic ??= entry;

                    styleMatch ??= entry;
                }
            }

            return styleAgnostic ?? styleMatch;
        }

        /// <summary>
        /// True when the interaction map registers more than one placement style for a tile type.
        /// </summary>
        internal bool HasMultipleTilePlacementStyles(int tileId)
        {
            int styleSlots = 0;
            foreach (var key in _byMain.Keys)
            {
                if (!string.Equals(key.Layer, "Tile", StringComparison.OrdinalIgnoreCase) || key.MainId != tileId)
                    continue;

                styleSlots++;
                if (styleSlots > 1)
                    return true;
            }

            return false;
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
        /// Tile: Item + Style + Base + Ammo + Fake. Wall: Item. Other layers: never duplicate.
        /// </summary>
        private static bool IsExportDuplicate(string layer, InteractionEntry a, InteractionEntry b)
        {
            // --- Keep Living Wood Wand wood families and Echo fakes as distinct compiled rows.

            if (string.Equals(layer, "Tile", StringComparison.OrdinalIgnoreCase))
            {
                return a.Item == b.Item
                    && (a.Style ?? 0) == (b.Style ?? 0)
                    && a.Base == b.Base
                    && a.Ammo == b.Ammo
                    && a.Fake == b.Fake;
            }

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

        /// <summary>True when the placement map defines multiple styles for a tile (e.g. LogicGate).</summary>
        public static bool HasMultipleTilePlacementStyles(ushort tileType) =>
            Current?.HasMultipleTilePlacementStyles(tileType) ?? false;

        /// <summary>
        /// Gets the first (preferred) interaction for the tile. For debug logging.
        /// </summary>
        internal static InteractionEntry? GetFirstTileInteraction(ushort tileType, int? style = null)
            => ResolvePreferred("Tile", tileType, style);

        /// <summary>True when the interaction map has a tile entry or placeable item ids (ignores inventory).</summary>
        internal static bool HasResolvedTileInteraction(ushort tileType, int? style = null)
        {
            // --- Prefer a mapped interaction row; fall back to any known place item ids.
            if (GetFirstTileInteraction(tileType, style) is not null)
                return true;
            return GetTileItemIds(tileType, style) is { Count: > 0 };
        }

        /// <summary>True when the interaction map has a wall entry or placeable item ids (ignores inventory).</summary>
        internal static bool HasResolvedWallInteraction(ushort wallType)
        {
            // --- Prefer a mapped Wall-layer row; fall back to any known place item ids.
            if (ResolvePreferred("Wall", wallType) is not null)
                return true;
            return GetWallItemIds(wallType) is { Count: > 0 };
        }

        /// <summary>
        /// Gets the preferred interaction for a layer/main, optionally filtered by base tile.
        /// </summary>
        internal static InteractionEntry? ResolvePreferred(
            string layer,
            int mainId,
            int? style = null,
            int? baseId = null)
        {
            // --- Exact BaseId matches first; with no base filter prefer direct (no-Base) rows over biome converts.

            foreach (InteractionEntry entry in EnumerateInteractions(layer, mainId, style, baseId))
                return entry;

            return null;
        }

        /// <summary>
        /// Yields priority-sorted interactions for a layer/main, optionally filtered by conversion base.
        /// </summary>
        internal static IEnumerable<InteractionEntry> EnumerateInteractions(
            string layer,
            int mainId,
            int? style = null,
            int? baseId = null)
        {
            // --- With a base id keep matching converts; otherwise yield only direct (no-Base) place rows.

            IReadOnlyList<InteractionEntry> list = Current?.GetByMain(layer, mainId, style) ?? [];
            if (list.Count == 0)
                yield break;

            if (baseId.HasValue)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    InteractionEntry entry = list[i];
                    if (entry.BaseId == baseId)
                        yield return entry;
                }

                yield break;
            }

            // Empty-world paste: do not treat biome powder converts as place candidates.
            for (int i = 0; i < list.Count; i++)
            {
                InteractionEntry entry = list[i];
                if (!entry.BaseId.HasValue && string.IsNullOrEmpty(entry.Base))
                    yield return entry;
            }
        }

        /// <summary>True when any tile interaction converts from the given world base tile.</summary>
        internal static bool HasConversionFromBase(int tileType, int? style, int baseTileId)
        {
            // --- Probe whether live-world conversion from baseTileId can produce tileType.

            IReadOnlyList<InteractionEntry> list = Current?.GetByMain("Tile", tileType, style) ?? [];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].BaseId == baseTileId)
                    return true;
            }

            return false;
        }

        /// <summary>Gets item IDs that can place the given layer/main, in map priority order.</summary>
        internal static IReadOnlyList<int> GetItemIds(string layer, int mainId, int? style = null) =>
            Current?.GetItemIds(layer, mainId, style) ?? [];

        internal static InteractionEntry? ResolveTileInteraction(ushort tileType, int? style, int? baseId) =>
            ResolvePreferred("Tile", tileType, style, baseId);

        /// <summary>Resolves an interaction for a placed main id, including fake place targets.</summary>
        internal static InteractionEntry? ResolvePlacedMainInteraction(
            string layer,
            int placedMainId,
            int? style = null,
            int? baseId = null)
        {
            // --- Direct main lookup first, then rows that place via Fake.

            InteractionEntry? direct = ResolvePreferred(layer, placedMainId, style, baseId);
            if (direct is not null)
                return direct;

            return Current?.FindByFakeMain(layer, placedMainId, style);
        }

        /// <summary>
        /// Yields candidate tile interactions in priority order, optionally filtered to a conversion base.
        /// </summary>
        internal static IEnumerable<InteractionEntry> EnumerateTileInteractions(int tileType, int? style, int? baseId) =>
            EnumerateInteractions("Tile", tileType, style, baseId);

        internal static InteractionEntry? ResolveWallInteraction(ushort wallType) =>
            ResolvePreferred("Wall", wallType, null);

        /// <summary>
        /// Yields wall interactions in priority order.
        /// </summary>
        internal static IEnumerable<InteractionEntry> EnumerateWallInteractions(int wallType) =>
            EnumerateInteractions("Wall", wallType);

        internal static InteractionEntry? ResolveWireInteraction(int wireKind) =>
            ResolvePreferred("Wire", wireKind, null);

        /// <summary>
        /// Yields wire/actuator interactions in priority order for the given wire kind.
        /// </summary>
        internal static IEnumerable<InteractionEntry> EnumerateWireInteractions(int wireKind) =>
            EnumerateInteractions("Wire", wireKind);

        /// <summary>
        /// Gets item IDs that can place the given wire kind (0 red, 1 green, 2 blue, 3 yellow, 4 actuator).
        /// </summary>
        internal static IReadOnlyList<int> GetWireItemIds(int wireKind) =>
            GetItemIds("Wire", wireKind);

        /// <summary>
        /// Yields paint/coating interactions in priority order for the given paint main id.
        /// </summary>
        internal static IEnumerable<InteractionEntry> EnumeratePaintInteractions(int paintMainId) =>
            EnumerateInteractions("Paint", paintMainId);

        /// <summary>Gets item IDs that can apply the given paint or coating main.</summary>
        internal static IReadOnlyList<int> GetPaintItemIds(int paintMainId) =>
            GetItemIds("Paint", paintMainId);

        internal static InteractionEntry? ResolvePaintInteraction(byte tileColor, byte wallColor)
        {
            if (tileColor != PaintID.None)
                return ResolvePreferred("Paint", tileColor, null);
            if (wallColor != PaintID.None)
                return ResolvePreferred("Paint", wallColor, null);
            return null;
        }

        internal static InteractionEntry? ResolveLiquidInteraction(int liquidType) =>
            ResolvePreferred("Liquid", liquidType, null);

        /// <summary>
        /// Yields liquid interactions in priority order for the given liquid type.
        /// </summary>
        internal static IEnumerable<InteractionEntry> EnumerateLiquidInteractions(int liquidType) =>
            EnumerateInteractions("Liquid", liquidType);

        /// <summary>
        /// Gets item IDs that can place the given liquid type (bottomless buckets, buckets, bombs).
        /// </summary>
        internal static IReadOnlyList<int> GetLiquidItemIds(int liquidType) =>
            GetItemIds("Liquid", liquidType);
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
            if (value.Fake != null)
                writer.WriteString("Fake", value.Fake);
            if (value.Alias is { Count: > 0 })
            {
                writer.WritePropertyName("Alias");
                writer.WriteStartArray();
                for (int i = 0; i < value.Alias.Count; i++)
                    writer.WriteStringValue(value.Alias[i]);
                writer.WriteEndArray();
            }
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
                    case "Fake": entry.Fake = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                    case "Alias": entry.Alias = ReadStringArray(ref reader); break;
                    case "Layer": entry.Layer = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                }
            }
            return entry;
        }

        /// <summary>Reads a JSON string array into a list, or null when empty.</summary>
        private static List<string>? ReadStringArray(ref Utf8JsonReader reader)
        {
            // --- Collect non-empty strings from the array token.

            if (reader.TokenType != JsonTokenType.StartArray)
                return null;

            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String)
                    continue;
                string? value = reader.GetString();
                if (!string.IsNullOrEmpty(value))
                    list.Add(value);
            }

            return list.Count > 0 ? list : null;
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
    /// Compares interactions for paste priority.
    /// Direct before conversion; infinite before finite; lower positive yield before higher;
    /// no ammo before ammo.
    /// </summary>
    internal sealed class InteractionPriorityComparer : IComparer<InteractionEntry>
    {
        public static readonly InteractionPriorityComparer Instance = new();

        public int Compare(InteractionEntry? a, InteractionEntry? b)
        {
            if (a is null && b is null) return 0;
            if (a is null) return 1;
            if (b is null) return -1;

            // Conversion placements sort after direct placements.
            int aBase = HasConversionBase(a) ? 1 : 0;
            int bBase = HasConversionBase(b) ? 1 : 0;
            int cmp = aBase.CompareTo(bBase);
            if (cmp != 0) return cmp;

            int aYieldValue = InteractionBudget.GetEffectiveYield(a);
            int bYieldValue = InteractionBudget.GetEffectiveYield(b);
            bool aFinite = aYieldValue > 0;
            bool bFinite = bYieldValue > 0;

            // Explicit infinite yield (<= 0) before finite spend rates.
            cmp = (aFinite ? 1 : 0).CompareTo(bFinite ? 1 : 0);
            if (cmp != 0) return cmp;

            // Among finite yields, prefer cheaper single-tile items (DirtBlock 1 before DirtBomb 25).
            if (aFinite && bFinite)
            {
                cmp = aYieldValue.CompareTo(bYieldValue);
                if (cmp != 0) return cmp;
            }

            // Prefer entries that do not require separate ammo.
            int aAmmo = a.AmmoId.HasValue || !string.IsNullOrEmpty(a.Ammo) ? 1 : 0;
            int bAmmo = b.AmmoId.HasValue || !string.IsNullOrEmpty(b.Ammo) ? 1 : 0;
            return aAmmo.CompareTo(bAmmo);
        }

        private static bool HasConversionBase(InteractionEntry entry) =>
            entry.BaseId.HasValue || !string.IsNullOrEmpty(entry.Base);
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
