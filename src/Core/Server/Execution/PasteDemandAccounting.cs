using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Records paste item demand by simulating interaction selection and yield budgets without mutating inventories.
    /// </summary>
    internal sealed class PasteDemandAccounting
    {
        private readonly InteractionBudget _budget = new();
        private readonly Dictionary<int, int> _demandByItemId = [];
        private readonly HashSet<int> _presenceItemIds = [];
        private readonly List<string> _unresolved = [];
        private readonly HashSet<string> _loggedLookupKeys = [];
        private readonly InventoryStackLedger? _ledger;

        /// <summary>Creates demand accounting, optionally inventory-aware via a non-mutating ledger snapshot.</summary>
        public PasteDemandAccounting(InventoryStackLedger? ledger = null)
        {
            // --- Store the ledger used to prefer supplyable interactions over preferred unmet ones.

            _ledger = ledger;
        }

        /// <summary>Human-readable unresolved mapping notes gathered during recording.</summary>
        public IReadOnlyList<string> Unresolved => _unresolved;

        /// <summary>Merges presence requirements into demand counts and returns the final item map.</summary>
        public Dictionary<int, int> BuildFinalDemand()
        {
            // --- Ensure infinite-yield tools appear at least once, then copy recorded consumable stacks.

            var result = new Dictionary<int, int>(_demandByItemId);
            foreach (int itemId in _presenceItemIds)
            {
                if (itemId <= 0)
                    continue;
                if (!result.TryGetValue(itemId, out int existing) || existing < 1)
                    result[itemId] = 1;
            }

            return result;
        }

        /// <summary>Records one tile placement, including optional base-tile conversion prerequisites.</summary>
        public void RecordTilePlacement(ushort tileType, int style, int worldX, int worldY, bool ignoreWorldState)
        {
            // --- Mirror HasTilePlacementItems: place missing base first, then the conversion/target interaction.

            Tile target = Main.tile[worldX, worldY];
            int? requiredBase = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            int? baseId = null;

            if (requiredBase.HasValue)
            {
                ushort baseType = (ushort)requiredBase.Value;
                bool hasBase = !ignoreWorldState && target.HasTile && target.TileType == baseType;
                if (!hasBase)
                    RecordTileMutation(baseType, style: 0, baseId: null);

                // Conversion interactions are filtered by the required base tile id.
                baseId = requiredBase;
            }

            RecordTileMutation(tileType, style, baseId);
        }

        /// <summary>Records one wall placement demand unit.</summary>
        public void RecordWallPlacement(ushort wallType) =>
            RecordMainKind(
                "Wall",
                wallType,
                unresolvedKey: FormatUnresolvedWall(wallType),
                fallbackItemIds: () => InteractionMapProvider.GetWallItemIds(wallType) ?? []);

        /// <summary>Records one wire-kind placement demand unit.</summary>
        public void RecordWireKind(int wireKind)
        {
            // --- Accept any mapped wrench/multicolor tool; hard-fallback to Wire/Actuator when unmapped.

            if (RecordMainKind(
                    "Wire",
                    wireKind,
                    unresolvedKey: null,
                    fallbackItemIds: () => InteractionMapProvider.GetWireItemIds(wireKind)))
                return;

            int fallback = wireKind == 4 ? ItemID.Actuator : ItemID.Wire;
            LogLookupOnce(
                $"Wire:{wireKind}:hardFallback",
                $"Wire main={wireKind} candidates=0 fallbackHard=itemId={fallback}");
            AddDemand(fallback, 1);
        }

        /// <summary>Records one paint-kind or coating placement demand unit.</summary>
        public void RecordPaintKind(int paintMainId)
        {
            if (paintMainId == PaintID.None)
                return;

            RecordMainKind(
                "Paint",
                paintMainId,
                unresolvedKey: FormatUnresolvedPaint(paintMainId));
        }

        /// <summary>
        /// Records demand for the first ledger-met interaction, or the preferred map entry when unmet.
        /// </summary>
        public bool RecordMainKind(
            string layer,
            int mainId,
            string? unresolvedKey = null,
            Func<IReadOnlyList<int>>? fallbackItemIds = null,
            int? style = null,
            int? baseId = null)
        {
            // --- Enumerate map candidates with inventory-aware selection, then fallback ids, then unresolved.

            string lookupKey = FormatLookupKey(layer, mainId, style, baseId);
            if (TrySelectAndRecord(
                    InteractionMapProvider.EnumerateInteractions(layer, mainId, style, baseId),
                    lookupKey,
                    layer,
                    mainId))
                return true;

            if (TrySelectAndRecordFallback(fallbackItemIds?.Invoke(), lookupKey, layer, mainId))
                return true;

            if (!string.IsNullOrEmpty(unresolvedKey))
            {
                LogLookupOnce(
                    lookupKey,
                    $"{layer} main={mainId} candidates=0 unresolved={unresolvedKey}");
                NoteUnresolved(unresolvedKey);
            }

            return false;
        }

        /// <summary>Records liquid demand for every cell in a changed pool.</summary>
        public void RecordLiquidPool(int liquidType, int cellCount)
        {
            // --- Pick one interaction for the whole pool, then spend once per cell.

            if (cellCount <= 0)
                return;

            string lookupKey = FormatLookupKey("Liquid", liquidType);
            if (TrySelectInteraction(
                    InteractionMapProvider.EnumerateLiquidInteractions(liquidType),
                    out InteractionEntry? chosen,
                    out InteractionEntry? preferred,
                    out int candidateCount,
                    out bool met)
                && chosen is not null)
            {
                LogSelectedOnce(lookupKey, "Liquid", liquidType, chosen, preferred, candidateCount, met);
                for (int i = 0; i < cellCount; i++)
                    TryRecordInteraction(chosen);
                return;
            }

            if (TrySelectAndRecordFallback(
                    InteractionMapProvider.GetLiquidItemIds(liquidType),
                    lookupKey,
                    "Liquid",
                    liquidType,
                    cellCount))
                return;

            LogLookupOnce(
                lookupKey,
                $"Liquid main={liquidType} candidates=0 unresolved={FormatUnresolvedLiquid(liquidType)}");
            NoteUnresolved(FormatUnresolvedLiquid(liquidType));
        }

        private void RecordTileMutation(ushort tileType, int style, int? baseId)
        {
            // --- Inventory-aware tile interaction, then map ids, then styled drop, then unresolved.

            string lookupKey = FormatLookupKey("Tile", tileType, style, baseId);
            if (TrySelectAndRecord(
                    InteractionMapProvider.EnumerateTileInteractions(tileType, style, baseId),
                    lookupKey,
                    "Tile",
                    tileType))
                return;

            if (TrySelectAndRecordFallback(
                    InteractionMapProvider.GetTileItemIds(tileType, style),
                    lookupKey,
                    "Tile",
                    tileType))
                return;

            int drop = TileLoader.GetItemDropFromTypeAndStyle(tileType, Math.Max(0, style));
            if (drop > 0)
            {
                LogLookupOnce(
                    lookupKey,
                    $"Tile main={tileType} candidates=0 fallbackDrop=itemId={drop}");
                AddDemand(drop, 1);
                return;
            }

            string unresolvedKey = FormatUnresolvedTile(tileType, style);
            LogLookupOnce(
                lookupKey,
                $"Tile main={tileType} candidates=0 unresolved={unresolvedKey}");
            NoteUnresolved(unresolvedKey);
        }

        /// <summary>Formats an unresolved tile note using the tile display name.</summary>
        private static string FormatUnresolvedTile(ushort tileType, int style)
        {
            // --- Prefer the readable tile name; append style only when non-zero.

            string name = LayerLogFormat.FormatTileType(tileType);
            return style > 0 ? $"{name} (style {style})" : name;
        }

        /// <summary>Formats an unresolved wall note using the wall display name.</summary>
        private static string FormatUnresolvedWall(ushort wallType) =>
            LayerLogFormat.NormalizeContentName(ContentNameResolver.GetWallName(wallType));

        /// <summary>Formats an unresolved paint/coating note using the paint display name.</summary>
        private static string FormatUnresolvedPaint(int paintMainId) =>
            LayerLogFormat.NormalizeContentName(ContentNameResolver.GetPaintName(paintMainId));

        /// <summary>Formats an unresolved liquid note using the liquid display name.</summary>
        private static string FormatUnresolvedLiquid(int liquidType) =>
            LayerLogFormat.NormalizeContentName(ContentNameResolver.GetLiquidTypeName(liquidType));

        /// <summary>Selects and records the first ledger-met candidate, or the preferred entry when unmet.</summary>
        private bool TrySelectAndRecord(
            IEnumerable<InteractionEntry> candidates,
            string lookupKey,
            string layer,
            int mainId)
        {
            // --- Resolve one interaction, log once for this kind, then record a single demand unit.

            if (!TrySelectInteraction(
                    candidates,
                    out InteractionEntry? chosen,
                    out InteractionEntry? preferred,
                    out int candidateCount,
                    out bool met)
                || chosen is null)
            {
                return false;
            }

            LogSelectedOnce(lookupKey, layer, mainId, chosen, preferred, candidateCount, met);
            return TryRecordInteraction(chosen);
        }

        /// <summary>Picks the first ledger-met interaction, or the first recordable preferred entry when unmet.</summary>
        private bool TrySelectInteraction(
            IEnumerable<InteractionEntry> candidates,
            out InteractionEntry? chosen,
            out InteractionEntry? preferred,
            out int candidateCount,
            out bool met)
        {
            // --- Scan priority order for a supplyable interaction; fall back to preferred when none are met.

            preferred = null;
            InteractionEntry? metEntry = null;
            candidateCount = 0;
            met = false;

            foreach (InteractionEntry entry in candidates)
            {
                if (!IsRecordable(entry))
                    continue;

                candidateCount++;
                preferred ??= entry;

                // Without a ledger, first recordable wins (legacy behavior).
                if (_ledger is null || IsMetByLedger(entry))
                {
                    metEntry = entry;
                    met = true;
                    break;
                }
            }

            chosen = metEntry ?? preferred;
            return chosen is not null;
        }

        /// <summary>Records the first ledger-met item id, or the first valid preferred id when unmet.</summary>
        private bool TrySelectAndRecordFallback(
            IReadOnlyList<int>? itemIds,
            string lookupKey,
            string layer,
            int mainId,
            int count = 1)
        {
            // --- Same met-then-preferred rule for raw fallback item lists.

            if (itemIds is not { Count: > 0 } || count <= 0)
                return false;

            int? preferred = null;
            int? met = null;
            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                if (itemId <= 0)
                    continue;

                preferred ??= itemId;

                if (_ledger is null || HasRemainingLedgerSupply(itemId))
                {
                    met = itemId;
                    break;
                }
            }

            int? selected = met ?? preferred;
            if (selected is null)
                return false;

            LogLookupOnce(
                lookupKey,
                $"{layer} main={mainId} candidates=0 fallbackItemId={selected.Value} met={met.HasValue}");
            AddDemand(selected.Value, count);
            return true;
        }

        /// <summary>Whether the entry has a positive tool item id suitable for demand recording.</summary>
        private static bool IsRecordable(InteractionEntry entry) =>
            entry.ItemId.HasValue && entry.ItemId.Value > 0;

        /// <summary>Whether the ledger can currently supply the tool and any required consumable for this entry.</summary>
        private bool IsMetByLedger(InteractionEntry entry)
        {
            // --- Mirror GetInteractionAvailability using ledger counts minus already-recorded demand.

            if (!IsRecordable(entry))
                return false;

            int toolId = entry.ItemId!.Value;
            if (!HasRemainingLedgerSupply(toolId))
                return false;

            // Distinct ammo is required presence even when yield never debits it.
            if (InteractionBudget.HasDistinctAmmo(entry)
                && !HasRemainingLedgerSupply(entry.AmmoId!.Value))
                return false;

            if (InteractionBudget.IsInfiniteYield(entry))
                return true;

            if (_budget.HasFreeUses(entry))
                return true;

            int consumableId = entry.AmmoId ?? toolId;
            return HasRemainingLedgerSupply(consumableId);
        }

        /// <summary>Whether ledger stacks for the item still exceed demand already recorded in this pass.</summary>
        private bool HasRemainingLedgerSupply(int itemId)
        {
            // --- Compare snapshot availability against demand accumulated so far in this accounting pass.

            if (_ledger is null)
                return true;

            int available = _ledger.CountAvailable(itemId);
            _demandByItemId.TryGetValue(itemId, out int demanded);
            return available - demanded > 0;
        }

        private bool TryRecordInteraction(InteractionEntry entry)
        {
            if (!IsRecordable(entry))
                return false;

            int toolId = entry.ItemId!.Value;
            int consumableId = entry.AmmoId ?? toolId;

            // Infinite yield: never debit, but tool and distinct ammo must appear as presence.
            if (InteractionBudget.IsInfiniteYield(entry))
            {
                _presenceItemIds.Add(toolId);
                if (InteractionBudget.HasDistinctAmmo(entry))
                    _presenceItemIds.Add(consumableId);
                _budget.Spend(entry);
                return true;
            }

            // Finite yield: count a consumable stack whenever the budget requires one.
            bool consumedStack = _budget.Spend(entry);
            if (consumedStack)
                AddDemand(consumableId, 1);

            if (toolId != consumableId)
                _presenceItemIds.Add(toolId);

            return true;
        }

        private void AddDemand(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0)
                return;

            _demandByItemId.TryGetValue(itemId, out int existing);
            _demandByItemId[itemId] = existing + count;
        }

        private void NoteUnresolved(string key)
        {
            if (!_unresolved.Contains(key))
                _unresolved.Add(key);
        }

        /// <summary>Builds a stable once-per-kind key for Manifest lookup logging.</summary>
        private static string FormatLookupKey(string layer, int mainId, int? style = null, int? baseId = null)
        {
            // --- Include optional style/base so conversion lookups stay distinct from direct placement.

            if (style.HasValue || baseId.HasValue)
                return $"{layer}:{mainId}:s{style ?? 0}:b{baseId?.ToString() ?? "-"}";
            return $"{layer}:{mainId}";
        }

        /// <summary>Logs the chosen map interaction once per kind.</summary>
        private void LogSelectedOnce(
            string lookupKey,
            string layer,
            int mainId,
            InteractionEntry chosen,
            InteractionEntry? preferred,
            int candidateCount,
            bool met)
        {
            // --- Emit chosen tool/ammo plus whether inventory met the entry vs preferred fallback.

            int chosenItem = chosen.ItemId ?? 0;
            int preferredItem = preferred?.ItemId ?? chosenItem;
            string ammo = chosen.AmmoId.HasValue ? chosen.AmmoId.Value.ToString() : "-";
            LogLookupOnce(
                lookupKey,
                $"{layer} main={mainId} candidates={candidateCount} chosen=itemId={chosenItem} ammo={ammo} met={met} preferred=itemId={preferredItem}");
        }

        /// <summary>Emits a Manifest debug line the first time this lookup key is seen.</summary>
        private void LogLookupOnce(string lookupKey, string message)
        {
            // --- Deduplicate noisy per-cell demand recording into one line per kind.

            if (!_loggedLookupKeys.Add(lookupKey))
                return;

            TABLog.Debug(message, TABLog.ManifestCategory);
        }
    }
}
