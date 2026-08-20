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
    /// <summary>Identifies one placed-main + tool+consumable demand unit for manifest pairing.</summary>
    internal readonly record struct DemandPairKey(string MainName, int ToolItemId, int ConsumableItemId);

    /// <summary>Orders demand pairs by consumable item id, then tool item id, then main name.</summary>
    internal sealed class DemandPairKeyComparer : IComparer<DemandPairKey>
    {
        public static readonly DemandPairKeyComparer Instance = new();

        /// <summary>Compares two demand pairs for SortedSet ordering.</summary>
        public int Compare(DemandPairKey x, DemandPairKey y)
        {
            // --- Sort by ammo first so related tools for the same consumable stay adjacent.

            int byConsumable = x.ConsumableItemId.CompareTo(y.ConsumableItemId);
            if (byConsumable != 0)
                return byConsumable;

            int byTool = x.ToolItemId.CompareTo(y.ToolItemId);
            if (byTool != 0)
                return byTool;

            return string.Compare(x.MainName, y.MainName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Records paste item demand by simulating interaction selection and yield budgets without mutating inventories.
    /// </summary>
    internal sealed class PasteDemandAccounting
    {
        private readonly InteractionBudget _budget = new();
        private readonly Dictionary<DemandPairKey, int> _demandByPair = [];
        private readonly Dictionary<int, int> _consumableDemandByItemId = [];
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

        /// <summary>Returns recorded tool+consumable pair demand counts.</summary>
        public Dictionary<DemandPairKey, int> BuildFinalPairs()
        {
            // --- Copy pair totals; presence-only pairs already have count >= 1.

            return new Dictionary<DemandPairKey, int>(_demandByPair);
        }

        /// <summary>Records one tile placement, including optional support-base or live conversion prerequisites.</summary>
        public void RecordTilePlacement(ushort tileType, int style, int worldX, int worldY, bool ignoreWorldState)
        {
            // --- Demand support underlays for placeables; use live biome converts only when the world already has that base.

            Tile target = Main.tile[worldX, worldY];
            int? requiredBase = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            int? baseId = null;

            if (requiredBase.HasValue)
            {
                ushort baseType = (ushort)requiredBase.Value;
                bool hasBase = !ignoreWorldState && target.HasTile && target.TileType == baseType;
                if (!hasBase)
                    RecordTileMutation(baseType, style: 0, baseId: null);

                // Support placements are filtered by the required underlay tile id.
                baseId = requiredBase;
            }
            else if (!ignoreWorldState
                && target.HasTile
                && InteractionMapProvider.HasConversionFromBase(tileType, style, target.TileType))
            {
                // Destination already holds a convertible peer (e.g. CrimsonThorns → CorruptThorns).
                baseId = target.TileType;
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
            AddPairDemand(FormatMainLabel("Wire", wireKind), fallback, fallback, 1);
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
                    mainId,
                    style))
                return true;

            if (TrySelectAndRecordFallback(fallbackItemIds?.Invoke(), lookupKey, layer, mainId, style: style))
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
                string mainName = FormatMainLabel("Liquid", liquidType);
                for (int i = 0; i < cellCount; i++)
                    TryRecordInteraction(chosen, mainName);
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
                    tileType,
                    style))
                return;

            if (TrySelectAndRecordFallback(
                    InteractionMapProvider.GetTileItemIds(tileType, style),
                    lookupKey,
                    "Tile",
                    tileType,
                    style: style))
                return;

            int drop = TileLoader.GetItemDropFromTypeAndStyle(tileType, Math.Max(0, style));
            if (drop > 0)
            {
                LogLookupOnce(
                    lookupKey,
                    $"Tile main={tileType} candidates=0 fallbackDrop=itemId={drop}");
                AddPairDemand(FormatMainLabel("Tile", tileType, style), drop, drop, 1);
                return;
            }

            string unresolvedKey = FormatUnresolvedTile(tileType, style);
            LogLookupOnce(
                lookupKey,
                $"Tile main={tileType} candidates=0 unresolved={unresolvedKey}");
            NoteUnresolved(unresolvedKey);
        }

        /// <summary>Formats an unresolved tile note with the same <c>Name:style</c> suffix as resolved mains.</summary>
        private static string FormatUnresolvedTile(ushort tileType, int style) =>
            FormatMainLabel("Tile", tileType, style);

        /// <summary>Formats an unresolved wall note using the same main label as resolved walls.</summary>
        private static string FormatUnresolvedWall(ushort wallType) =>
            FormatMainLabel("Wall", wallType);

        /// <summary>Formats an unresolved paint/coating note using the same main label as resolved paints.</summary>
        private static string FormatUnresolvedPaint(int paintMainId) =>
            FormatMainLabel("Paint", paintMainId);

        /// <summary>Formats an unresolved liquid note using the same main label as resolved liquids.</summary>
        private static string FormatUnresolvedLiquid(int liquidType) =>
            FormatMainLabel("Liquid", liquidType);

        /// <summary>Selects and records the first ledger-met candidate, or the preferred entry when unmet.</summary>
        private bool TrySelectAndRecord(
            IEnumerable<InteractionEntry> candidates,
            string lookupKey,
            string layer,
            int mainId,
            int? style = null)
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
            return TryRecordInteraction(chosen, FormatMainLabel(layer, mainId, style));
        }

        /// <summary>
        /// Picks a ledger-met interaction, preferring finite yield over infinite; unmet preferred is first finite then infinite.
        /// </summary>
        private bool TrySelectInteraction(
            IEnumerable<InteractionEntry> candidates,
            out InteractionEntry? chosen,
            out InteractionEntry? preferred,
            out int candidateCount,
            out bool met)
        {
            // --- Manifest-only: finite shopping-list paths beat infinite tools (Staff / Spectre).
            // --- Prefer the cheapest met finite; among same-priority ties keep the last (e.g. Grand Design).
            // --- Fall back to any other met finite (LavaBomb vs LavaBucket) before infinite / unmet preferred.

            InteractionEntry? preferredFinite = null;
            InteractionEntry? preferredInfinite = null;
            InteractionEntry? firstMetFinite = null;
            InteractionEntry? firstMetInfinite = null;
            InteractionEntry? lastMetFiniteInClass = null;
            InteractionEntry? lastMetInfiniteInClass = null;
            candidateCount = 0;

            foreach (InteractionEntry entry in candidates)
            {
                if (!IsRecordable(entry))
                    continue;

                candidateCount++;
                bool infinite = InteractionBudget.IsInfiniteYield(entry);

                // Track preferred unmet candidates with finite first (seeds before Staff).
                if (infinite)
                    preferredInfinite ??= entry;
                else
                    preferredFinite ??= entry;

                if (_ledger is not null && !IsMetByLedger(entry))
                    continue;

                // First met in priority order (LavaBucket before LavaBomb when both present).
                if (infinite)
                    firstMetInfinite ??= entry;
                else
                    firstMetFinite ??= entry;

                // Among same priority class, keep the last (multipurpose after specialized).
                InteractionEntry? classPreferred = infinite ? preferredInfinite : preferredFinite;
                if (classPreferred is null
                    || InteractionPriorityComparer.Instance.Compare(entry, classPreferred) != 0)
                    continue;

                if (infinite)
                    lastMetInfiniteInClass = entry;
                else
                    lastMetFiniteInClass = entry;
            }

            preferred = preferredFinite ?? preferredInfinite;
            chosen = lastMetFiniteInClass
                ?? firstMetFinite
                ?? lastMetInfiniteInClass
                ?? firstMetInfinite
                ?? preferred;
            met = firstMetFinite is not null || firstMetInfinite is not null;
            return chosen is not null;
        }

        /// <summary>Records the first ledger-met item id, or the first valid preferred id when unmet.</summary>
        private bool TrySelectAndRecordFallback(
            IReadOnlyList<int>? itemIds,
            string lookupKey,
            string layer,
            int mainId,
            int count = 1,
            int? style = null)
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
            AddPairDemand(FormatMainLabel(layer, mainId, style), selected.Value, selected.Value, count);
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
            if (!HasToolPresence(entry))
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

        /// <summary>Whether any of the interaction's tool ids (primary or alias) is present in the ledger.</summary>
        private bool HasToolPresence(InteractionEntry entry)
        {
            // --- Accept primary item or any alias as the same tool presence.

            if (_ledger is null)
                return true;

            IReadOnlyList<int> toolIds = entry.GetToolItemIds();
            for (int i = 0; i < toolIds.Count; i++)
            {
                if (_ledger.CountAvailable(toolIds[i]) > 0)
                    return true;
            }

            return false;
        }

        /// <summary>Whether ledger stacks for the consumable still exceed demand already recorded in this pass.</summary>
        private bool HasRemainingLedgerSupply(int itemId)
        {
            // --- Compare snapshot availability against consumable demand accumulated so far.

            if (_ledger is null)
                return true;

            int available = _ledger.CountAvailable(itemId);
            _consumableDemandByItemId.TryGetValue(itemId, out int demanded);
            return available - demanded > 0;
        }

        private bool TryRecordInteraction(InteractionEntry entry, string mainName)
        {
            if (!IsRecordable(entry))
                return false;

            int toolId = entry.ItemId!.Value;
            int consumableId = entry.AmmoId ?? toolId;

            // Infinite yield: never debit; keep a presence pair of at least one.
            if (InteractionBudget.IsInfiniteYield(entry))
            {
                EnsurePairAtLeast(mainName, toolId, consumableId, 1);
                _budget.Spend(entry);
                return true;
            }

            // Finite yield: count a consumable stack whenever the budget requires one.
            bool consumedStack = _budget.Spend(entry);
            if (consumedStack)
                AddPairDemand(mainName, toolId, consumableId, 1);
            else if (toolId != consumableId)
                EnsurePairAtLeast(mainName, toolId, consumableId, 1);

            return true;
        }

        /// <summary>Records demand for a pre-resolved interaction without inventory-aware reselection.</summary>
        public bool RecordInteraction(InteractionEntry entry)
        {
            // --- Used when the caller already chose the family ammo (e.g. tree wood type).
            return TryRecordInteraction(entry, FormatMainLabel(entry));
        }

        /// <summary>Notes an unresolved tile mapping using the same <c>Name:style</c> label as resolved mains.</summary>
        public void NoteUnresolvedTile(ushort tileType, int style) =>
            NoteUnresolved(FormatUnresolvedTile(tileType, style));

        /// <summary>Formats the placed main as a Terraria:/Mod: display label for manifest lines.</summary>
        private static string FormatMainLabel(string layer, int mainId, int? style = null)
        {
            // --- Wires use Terraria:Wire:{kind}; tiles append :{style}; others use content names.

            if (string.Equals(layer, "Wire", StringComparison.OrdinalIgnoreCase))
                return $"{ContentNameResolver.TerrariaContentPrefix}:Wire:{mainId}";

            if (string.Equals(layer, "Tile", StringComparison.OrdinalIgnoreCase))
            {
                string? tileName = ContentNameResolver.GetTileName(mainId);
                string normalized = LayerLogFormat.NormalizeContentName(tileName ?? $"Tile_{mainId}");
                return $"{normalized}:{style ?? 0}";
            }

            string? name = ContentNameResolver.ResolveMainIdToName(layer, mainId);
            return LayerLogFormat.NormalizeContentName(name ?? $"{layer}_{mainId}");
        }

        /// <summary>Formats the placed main label from a resolved interaction entry.</summary>
        private static string FormatMainLabel(InteractionEntry entry)
        {
            // --- Prefer resolved layer+id; fall back to the entry's Main string.

            if (!string.IsNullOrEmpty(entry.Layer) && entry.MainId.HasValue)
                return FormatMainLabel(entry.Layer, entry.MainId.Value, entry.Style);

            if (!string.IsNullOrEmpty(entry.Main))
                return LayerLogFormat.NormalizeContentName(entry.Main);

            return LayerLogFormat.NormalizeContentName("Unknown");
        }

        /// <summary>Ensures a pair exists with at least the given consumable count (presence paths).</summary>
        private void EnsurePairAtLeast(string mainName, int toolItemId, int consumableItemId, int minimum)
        {
            // --- Raise the pair total without reducing other pairs that share the same ammo.

            if (string.IsNullOrEmpty(mainName) || toolItemId <= 0 || consumableItemId <= 0 || minimum <= 0)
                return;

            var key = new DemandPairKey(mainName, toolItemId, consumableItemId);
            _demandByPair.TryGetValue(key, out int existing);
            if (existing >= minimum)
                return;

            int delta = minimum - existing;
            _demandByPair[key] = minimum;
            _consumableDemandByItemId.TryGetValue(consumableItemId, out int consumableExisting);
            _consumableDemandByItemId[consumableItemId] = consumableExisting + delta;
        }

        /// <summary>Adds consumable demand for a main+tool+ammo pair.</summary>
        private void AddPairDemand(string mainName, int toolItemId, int consumableItemId, int count)
        {
            // --- Accumulate pair and flat consumable totals used by ledger met-checks.

            if (string.IsNullOrEmpty(mainName) || toolItemId <= 0 || consumableItemId <= 0 || count <= 0)
                return;

            var key = new DemandPairKey(mainName, toolItemId, consumableItemId);
            _demandByPair.TryGetValue(key, out int existing);
            _demandByPair[key] = existing + count;
            _consumableDemandByItemId.TryGetValue(consumableItemId, out int consumableExisting);
            _consumableDemandByItemId[consumableItemId] = consumableExisting + count;
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
