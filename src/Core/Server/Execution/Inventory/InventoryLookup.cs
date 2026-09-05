using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core{
    /// <summary>
    /// Centralized lookup and mutation API for paste inventories.
    /// </summary>
    internal sealed class InventoryLookup
    {
        private delegate bool TryFindConsumableDelegate(out Item? item, out Inventory? sourceInventory);

        private readonly struct InventoryItemRef(Inventory inventory, Item item)
        {
            public Inventory Inventory { get; } = inventory;
            public Item Item { get; } = item;
        }

        private readonly struct TileKey(ushort tileType, int style)
        {
            public ushort TileType { get; } = tileType;
            public int Style { get; } = style;
        }

        private readonly List<Inventory> _inventories = [];
        private readonly Dictionary<int, List<InventoryItemRef>> _itemRefsByType = [];
        private readonly Dictionary<TileKey, IReadOnlyList<int>> _tileItemIdsByKey = [];
        private readonly Dictionary<ushort, IReadOnlyList<int>> _wallItemIdsByType = [];
        private bool _isDirty = true;

        public IReadOnlyList<Inventory> Inventories => _inventories;

        /// <summary>
        /// Replaces tracked inventories and invalidates compiled indexes.
        /// </summary>
        public void Refresh(IReadOnlyList<Inventory>? inventories)
        {
            // --- Replace sources in caller-provided priority order and rebuild indexes lazily.
            _inventories.Clear();
            if (inventories is { Count: > 0 })
            {
                for (int i = 0; i < inventories.Count; i++)
                    _inventories.Add(inventories[i]);
            }

            _itemRefsByType.Clear();
            _tileItemIdsByKey.Clear();
            _wallItemIdsByType.Clear();
            _isDirty = true;
            LogTrackedInventories("Refresh");
        }

        public bool TryGetInventory(int index, out Inventory inventory)
        {
            if ((uint)index < (uint)_inventories.Count)
            {
                inventory = _inventories[index];
                return true;
            }

            inventory = default!;
            return false;
        }

        /// <summary>
        /// Flattens tracked inventory slots into a single array.
        /// </summary>
        public Item[] GetContents()
        {
            List<Item> contents = [];
            for (int i = 0; i < _inventories.Count; i++)
                contents.AddRange(_inventories[i].Contents);
            return [.. contents];
        }

        public bool HasItemForTile(ushort tileType, int style, InteractionBudget? budget = null, int? baseId = null) =>
            GetTileItemAvailability(tileType, style, budget, baseId) == TileItemAvailability.Available;

        public bool HasItemForWall(ushort wallType, InteractionBudget? budget = null) =>
            GetWallItemAvailability(wallType, budget) == TileItemAvailability.Available;

        /// <summary>True when any priority-ordered paint/coating interaction for the main is available.</summary>
        public bool HasItemForPaint(int paintMainId, InteractionBudget? budget = null) =>
            GetPaintItemAvailability(paintMainId, budget) == TileItemAvailability.Available;

        public TileItemAvailability GetTileItemAvailability(
            ushort tileType,
            int style,
            InteractionBudget? budget = null,
            int? baseId = null)
        {
            if (_inventories.Count == 0)
                return TileItemAvailability.MissingFromInventory;

            bool sawInteraction = false;
            foreach (InteractionEntry entry in InteractionMapProvider.EnumerateTileInteractions(tileType, style, baseId))
            {
                sawInteraction = true;
                if (GetInteractionAvailability(entry, budget) == TileItemAvailability.Available)
                    return TileItemAvailability.Available;
            }

            if (sawInteraction)
            {
                if (HasInventoryItemForStyledTileDrop(tileType, style))
                    return TileItemAvailability.Available;
                return TileItemAvailability.MissingFromInventory;
            }

            RebuildIfNeeded();
            if (TryFindFirstAvailableByItemIds(ResolveTileItemIds(tileType, style), out _, out _))
                return TileItemAvailability.Available;

            return HasResolvableTileItemIds(tileType, style)
                ? TileItemAvailability.MissingFromInventory
                : TileItemAvailability.UnresolvedInteraction;
        }

        /// <summary>
        /// Returns availability for any mapped interaction of a layer/main in priority order.
        /// </summary>
        public TileItemAvailability GetMainItemAvailability(
            string layer,
            int mainId,
            InteractionBudget? budget = null,
            int? style = null,
            int? baseId = null,
            Func<IReadOnlyList<int>>? fallbackItemIds = null)
        {
            // --- Try each priority-sorted interaction, then optional fallback item ids.

            if (_inventories.Count == 0)
                return TileItemAvailability.MissingFromInventory;

            bool sawInteraction = false;
            foreach (InteractionEntry entry in InteractionMapProvider.EnumerateInteractions(layer, mainId, style, baseId))
            {
                sawInteraction = true;
                if (GetInteractionAvailability(entry, budget) == TileItemAvailability.Available)
                    return TileItemAvailability.Available;
            }

            if (sawInteraction)
                return TileItemAvailability.MissingFromInventory;

            IReadOnlyList<int> fallback = fallbackItemIds?.Invoke() ?? [];
            if (fallback.Count == 0)
                return TileItemAvailability.UnresolvedInteraction;

            RebuildIfNeeded();
            if (TryFindFirstAvailableByItemIds(fallback, out _, out _))
                return TileItemAvailability.Available;

            return TileItemAvailability.MissingFromInventory;
        }

        public TileItemAvailability GetWallItemAvailability(ushort wallType, InteractionBudget? budget = null) =>
            GetMainItemAvailability(
                "Wall",
                wallType,
                budget,
                fallbackItemIds: () => ResolveWallItemIds(wallType));

        /// <summary>Returns availability for any tool that can place the given wire kind.</summary>
        public TileItemAvailability GetWireItemAvailability(int wireKind, InteractionBudget? budget = null) =>
            GetMainItemAvailability(
                "Wire",
                wireKind,
                budget,
                fallbackItemIds: () => InteractionMapProvider.GetWireItemIds(wireKind));

        /// <summary>Returns availability for any tool that can place the given liquid type.</summary>
        public TileItemAvailability GetLiquidItemAvailability(int liquidType, InteractionBudget? budget = null) =>
            GetMainItemAvailability(
                "Liquid",
                liquidType,
                budget,
                fallbackItemIds: () => InteractionMapProvider.GetLiquidItemIds(liquidType));

        /// <summary>Returns availability for any tool that can apply the given paint or coating main.</summary>
        public TileItemAvailability GetPaintItemAvailability(int paintMainId, InteractionBudget? budget = null) =>
            GetMainItemAvailability("Paint", paintMainId, budget);

        public TileItemAvailability GetInteractionAvailability(InteractionEntry entry, InteractionBudget? budget)
        {
            if (_inventories.Count == 0)
                return TileItemAvailability.MissingFromInventory;

            if (!entry.ItemId.HasValue || entry.ItemId.Value <= 0)
                return TileItemAvailability.UnresolvedInteraction;

            RebuildIfNeeded();
            if (!TryFindFirstAvailableByItemIds(entry.GetToolItemIds(), out _, out _))
                return TileItemAvailability.MissingFromInventory;

            // Distinct ammo must be present even when yield never debits it.
            int consumableId = entry.AmmoId ?? entry.ItemId.Value;
            if (InteractionBudget.HasDistinctAmmo(entry)
                && !TryFindFirstAvailableByItemIds([consumableId], out _, out _))
                return TileItemAvailability.MissingFromInventory;

            if (InteractionBudget.IsInfiniteYield(entry))
                return TileItemAvailability.Available;

            if (budget is not null && budget.HasFreeUses(entry))
                return TileItemAvailability.Available;

            if (TryFindFirstAvailableByItemIds([consumableId], out _, out _))
                return TileItemAvailability.Available;

            return TileItemAvailability.MissingFromInventory;
        }

        /// <summary>
        /// Reserves tool and optional consumable stacks for an interaction, applying yield budget rules.
        /// </summary>
        public bool TryReserveForInteraction(
            InteractionEntry entry,
            InteractionBudget? budget,
            Player player,
            out InteractionReservation reservation)
        {
            reservation = default;
            if (!entry.ItemId.HasValue || entry.ItemId.Value <= 0)
                return false;

            RebuildIfNeeded();
            if (!TryFindFirstAvailableByItemIds(entry.GetToolItemIds(), out Item? toolItem, out Inventory? toolInventory)
                || toolItem is null
                || toolInventory is null
                || !TryResolveInventoryOrigin(toolInventory, player, out Vector2 origin))
            {
                TABLog.Debug(
                    $"ReserveInteraction miss tool={entry.ItemId} layer={entry.Layer} main={entry.MainId}",
                    TABLog.InventoryCategory);
                return false;
            }

            int consumableId = entry.AmmoId ?? entry.ItemId.Value;
            bool hasDistinctAmmo = InteractionBudget.HasDistinctAmmo(entry);
            bool needsDebit = budget is null
                ? !InteractionBudget.IsInfiniteYield(entry)
                : budget.PeekNeedsConsumableStack(entry);

            Item? consumableItem = null;
            Inventory? consumableInventory = null;

            // Distinct ammo must be present for reservation even when infinite yield skips debit.
            if (hasDistinctAmmo || needsDebit)
            {
                if (!TryFindFirstAvailableByItemIds([consumableId], out consumableItem, out consumableInventory)
                    || consumableItem is null)
                {
                    TABLog.Debug(
                        $"ReserveInteraction miss consumable={consumableId} tool={entry.ItemId} yield={entry.Yield} "
                        + $"needsDebit={needsDebit} hasDistinctAmmo={hasDistinctAmmo}",
                        TABLog.InventoryCategory);
                    return false;
                }
            }

            if (!needsDebit)
            {
                TABLog.Debug(
                    $"ReserveInteraction skipConsume tool={entry.ItemId} consumable={consumableId} yield={InteractionBudget.GetEffectiveYield(entry)} "
                    + $"infiniteYield={InteractionBudget.IsInfiniteYield(entry)} "
                    + $"freeRemaining={(budget?.GetRemainingFreeUses(entry) ?? -1)} "
                    + $"layer={entry.Layer} main={entry.MainId}",
                    TABLog.InventoryCategory);
            }

            bool consumedStack = false;
            if (budget is not null)
                consumedStack = budget.Spend(entry);

            if (consumedStack)
            {
                if (consumableItem is null
                    || !TryFindFirstAvailableByItemIds([consumableId], out consumableItem, out consumableInventory)
                    || consumableItem is null)
                {
                    budget?.RollbackSpend(entry, consumedStack: true);
                    return false;
                }

                ConsumeReservedItem(consumableItem, consumableInventory, "ReserveInteraction");
            }

            Chest? sourceChest = toolInventory.Chest;
            reservation = new InteractionReservation(
                ToolItemId: entry.ItemId.Value,
                ConsumableItemId: consumableId,
                PlaceStyle: Math.Max(0, toolItem.placeStyle),
                ConsumableItem: consumableItem ?? toolItem,
                Origin: origin,
                FromChest: sourceChest is not null,
                SourceChest: sourceChest,
                ConsumedStack: consumedStack);

            TABLog.Debug(
                $"ReserveInteraction ok tool={entry.ItemId} consumable={consumableId} yield={entry.Yield} "
                + $"freeRemaining={(budget?.GetRemainingFreeUses(entry) ?? -1)} consumedStack={consumedStack} "
                + $"layer={entry.Layer} main={entry.MainId}",
                TABLog.InventoryCategory);
            return true;
        }

        /// <summary>
        /// Reserves the first available priority-ordered interaction for a layer/main.
        /// </summary>
        public bool TryReserveForMain(
            string layer,
            int mainId,
            Player player,
            InteractionBudget? budget,
            out InteractionEntry? reservedEntry,
            out InteractionReservation reservation,
            int? style = null,
            int? baseId = null)
        {
            // --- Try each mapped interaction in priority order until one reserves.

            reservedEntry = null;
            reservation = default;

            foreach (InteractionEntry entry in InteractionMapProvider.EnumerateInteractions(layer, mainId, style, baseId))
            {
                if (TryReserveForInteraction(entry, budget, player, out reservation))
                {
                    reservedEntry = entry;
                    TABLog.Debug(
                        $"ReserveMain ok layer={layer} main={mainId} tool={entry.ItemId} item={reservation.ConsumableItemId} "
                        + $"consumedStack={reservation.ConsumedStack}",
                        TABLog.InventoryCategory);
                    return true;
                }

                TABLog.Debug(
                    $"ReserveMain interactionMiss layer={layer} main={mainId} tool={entry.ItemId}",
                    TABLog.InventoryCategory);
            }

            return false;
        }

        public bool TryReserveForTileMutation(
            ushort tileType,
            int style,
            Player player,
            InteractionBudget? budget,
            int? baseId,
            out int placeStyle,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest,
            out PlacementReservationLedger ledger)
        {
            placeStyle = Math.Max(0, style);
            itemType = 0;
            origin = default;
            fromChest = false;
            sourceChest = null;
            ledger = new PlacementReservationLedger();

            if (TryReserveForMain(
                    "Tile",
                    tileType,
                    player,
                    budget,
                    out InteractionEntry? reservedEntry,
                    out InteractionReservation reservation,
                    style,
                    baseId)
                && reservedEntry is not null)
            {
                placeStyle = reservation.PlaceStyle;
                itemType = reservation.ConsumableItemId;
                origin = reservation.Origin;
                fromChest = reservation.FromChest;
                sourceChest = reservation.SourceChest;
                ledger.Record(reservedEntry, in reservation);
                TABLog.Debug(
                    $"ReserveTile ok tile={tileType} style={style} item={itemType} consumedStack={reservation.ConsumedStack} "
                    + $"source={(fromChest ? "chest" : "player")} origin=({origin.X:0.##},{origin.Y:0.##}) path=interaction",
                    TABLog.InventoryCategory);
                return true;
            }

            if (!TryReserveForMutation(
                player,
                (out Item? item, out Inventory? sourceInventory) =>
                    TryFindConsumableItemByTile(tileType, style, out item, out sourceInventory),
                out Item? fallbackItem,
                out Inventory? fallbackInventory,
                out origin,
                out fromChest,
                out sourceChest))
            {
                TABLog.Debug(
                    $"ReserveTile miss tile={tileType} style={style}",
                    TABLog.InventoryCategory);
                return false;
            }

            placeStyle = Math.Max(0, fallbackItem!.placeStyle);
            itemType = ConsumeReservedItem(fallbackItem, fallbackInventory, "ReserveTileFallback");
            ledger.RecordFallbackDebit(itemType);
            TABLog.Debug(
                $"ReserveTile ok (fallback) tile={tileType} style={style} item={itemType} source={(fromChest ? "chest" : "player")} origin=({origin.X:0.##},{origin.Y:0.##})",
                TABLog.InventoryCategory);
            return true;
        }

        public bool TryReserveForWallMutation(
            ushort wallType,
            Player player,
            InteractionBudget? budget,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest,
            out PlacementReservationLedger ledger)
        {
            itemType = 0;
            origin = default;
            fromChest = false;
            sourceChest = null;
            ledger = new PlacementReservationLedger();

            if (TryReserveForMain(
                    "Wall",
                    wallType,
                    player,
                    budget,
                    out InteractionEntry? reservedEntry,
                    out InteractionReservation reservation)
                && reservedEntry is not null)
            {
                itemType = reservation.ConsumableItemId;
                origin = reservation.Origin;
                fromChest = reservation.FromChest;
                sourceChest = reservation.SourceChest;
                ledger.Record(reservedEntry, in reservation);
                TABLog.Debug(
                    $"ReserveWall ok wall={wallType} item={itemType} consumedStack={reservation.ConsumedStack} "
                    + $"source={(fromChest ? "chest" : "player")} origin=({origin.X:0.##},{origin.Y:0.##}) path=interaction",
                    TABLog.InventoryCategory);
                return true;
            }

            if (!TryReserveForMutation(
                player,
                (out Item? item, out Inventory? sourceInventory) =>
                    TryFindConsumableItemByWall(wallType, out item, out sourceInventory),
                out Item? fallbackItem,
                out Inventory? fallbackInventory,
                out origin,
                out fromChest,
                out sourceChest))
            {
                TABLog.Debug(
                    $"ReserveWall miss wall={wallType}",
                    TABLog.InventoryCategory);
                return false;
            }

            itemType = ConsumeReservedItem(fallbackItem!, fallbackInventory, "ReserveWallFallback");
            ledger.RecordFallbackDebit(itemType);
            TABLog.Debug(
                $"ReserveWall ok (fallback) wall={wallType} item={itemType} source={(fromChest ? "chest" : "player")} origin=({origin.X:0.##},{origin.Y:0.##})",
                TABLog.InventoryCategory);
            return true;
        }

        /// <summary>Reserves a wire/actuator placement using any available mapped tool for the wire kind.</summary>
        public bool TryReserveForWireMutation(
            int wireKind,
            Player player,
            InteractionBudget? budget,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest,
            out PlacementReservationLedger ledger)
        {
            // --- Try each mapped tool (single wrench, Wire Kite, Grand Design, etc.) until one reserves.
            itemType = 0;
            origin = default;
            fromChest = false;
            sourceChest = null;
            ledger = new PlacementReservationLedger();

            if (TryReserveForMain(
                    "Wire",
                    wireKind,
                    player,
                    budget,
                    out InteractionEntry? reservedEntry,
                    out InteractionReservation reservation)
                && reservedEntry is not null)
            {
                itemType = reservation.ConsumableItemId;
                origin = reservation.Origin;
                fromChest = reservation.FromChest;
                sourceChest = reservation.SourceChest;
                ledger.Record(reservedEntry, in reservation);
                TABLog.Debug(
                    $"ReserveWire ok kind={wireKind} tool={reservedEntry.ItemId} item={itemType} consumedStack={reservation.ConsumedStack} "
                    + $"source={(fromChest ? "chest" : "player")} origin=({origin.X:0.##},{origin.Y:0.##}) path=interaction",
                    TABLog.InventoryCategory);
                return true;
            }

            TABLog.Debug($"ReserveWire miss kind={wireKind}", TABLog.InventoryCategory);
            return false;
        }

        /// <summary>Reserves a paint/coating placement using any available mapped tool for the paint main.</summary>
        public bool TryReserveForPaintKind(
            int paintMainId,
            Player player,
            InteractionBudget? budget,
            out InteractionEntry? reservedEntry,
            out InteractionReservation reservation) =>
            TryReserveForMain("Paint", paintMainId, player, budget, out reservedEntry, out reservation);

        /// <summary>
        /// Reserves one liquid interaction once per pool cell, keeping per-cell yield economics.
        /// </summary>
        public bool TryReserveForLiquidPool(
            int liquidType,
            int cellCount,
            Player player,
            InteractionBudget? budget,
            out PlacementReservationLedger ledger,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest)
        {
            // --- Pick the first mapped liquid tool that can cover every cell; roll back partial failures.
            ledger = new PlacementReservationLedger();
            origin = default;
            fromChest = false;
            sourceChest = null;

            if (cellCount <= 0)
                return true;

            foreach (InteractionEntry entry in InteractionMapProvider.EnumerateLiquidInteractions(liquidType))
            {
                var completed = new List<InteractionReservation>(cellCount);
                bool failed = false;

                for (int i = 0; i < cellCount; i++)
                {
                    if (!TryReserveForInteraction(entry, budget, player, out InteractionReservation reservation))
                    {
                        failed = true;
                        for (int j = completed.Count - 1; j >= 0; j--)
                            UndoInteractionReservation(entry, budget, completed[j]);
                        break;
                    }

                    completed.Add(reservation);
                }

                if (failed)
                {
                    TABLog.Debug(
                        $"ReserveLiquidPool interactionMiss liquid={liquidType} tool={entry.ItemId} cells={cellCount} reservedBeforeFail={completed.Count}",
                        TABLog.InventoryCategory);
                    continue;
                }

                ledger = new PlacementReservationLedger();
                for (int i = 0; i < completed.Count; i++)
                    ledger.Record(entry, completed[i]);

                InteractionReservation first = completed[0];
                origin = first.Origin;
                fromChest = first.FromChest;
                sourceChest = first.SourceChest;

                TABLog.Debug(
                    $"ReserveLiquidPool ok liquid={liquidType} tool={entry.ItemId} cells={cellCount} "
                    + $"debited={ledger.DebitedStackCount} freeYield={ledger.FreeYieldSpendCount} "
                    + $"visual={ledger.VisualItemType} source={(fromChest ? "chest" : "player")}",
                    TABLog.InventoryCategory);
                return true;
            }

            TABLog.Debug($"ReserveLiquidPool miss liquid={liquidType} cells={cellCount}", TABLog.InventoryCategory);
            return false;
        }

        /// <summary>Reverses a consumable stack decrement from a failed multi-step reservation.</summary>
        public void UndoInteractionReservation(InteractionEntry entry, InteractionBudget? budget, in InteractionReservation reservation)
        {
            if (reservation.ConsumedStack && reservation.ConsumableItem is not null)
            {
                int before = reservation.ConsumableItem.stack;
                reservation.ConsumableItem.stack++;
                _isDirty = true;
                TABLog.Debug(
                    $"UndoConsume item={reservation.ConsumableItem.type} stack {before}->{reservation.ConsumableItem.stack} "
                    + $"tool={entry.ItemId} layer={entry.Layer} main={entry.MainId}",
                    TABLog.InventoryCategory);

                // Sync the restored slot so multiplayer clients see the refund.
                if (TryFindInventoryContainingItem(reservation.ConsumableItem, out Inventory? sourceInventory))
                {
                    int slotIndex = InventoryManager.FindSlotIndex(sourceInventory, reservation.ConsumableItem);
                    if (slotIndex >= 0)
                        InventoryManager.SyncInventorySlot(sourceInventory, slotIndex, "UndoConsume");
                }
            }

            budget?.RollbackSpend(entry, reservation.ConsumedStack);
        }

        /// <summary>
        /// Stores item stacks when a clear projectile reaches the destination inventory.
        /// </summary>
        public void ApplyStoreOnArrival(int itemId, int stack, Point worldTile, int preferredInventoryIndex = -1)
        {
            if (itemId <= 0 || stack <= 0)
                return;

            int remaining = TryStoreAtDestination(itemId, stack, preferredInventoryIndex);
            if (remaining > 0)
                InventoryManager.DropItemAtTile(itemId, remaining, worldTile);
        }

        /// <summary>
        /// Tries to store the given stack into tracked inventories and returns the unstored remainder.
        /// </summary>
        public int TryStoreAtDestination(int itemId, int stack, int preferredInventoryIndex = -1)
        {
            if (itemId <= 0 || stack <= 0)
                return stack;

            int remaining = stack;
            if (preferredInventoryIndex >= 0
                && preferredInventoryIndex < _inventories.Count)
            {
                remaining = TryStoreInInventory(_inventories[preferredInventoryIndex], itemId, remaining);
            }

            for (int i = 0; i < _inventories.Count && remaining > 0; i++)
            {
                if (i == preferredInventoryIndex)
                    continue;
                remaining = TryStoreInInventory(_inventories[i], itemId, remaining);
            }

            TABLog.Debug(
                $"StoreAttempt item={itemId} requested={stack} preferred={preferredInventoryIndex} remaining={remaining}",
                TABLog.InventoryCategory);
            return remaining;
        }

        /// <summary>
        /// Resolves the world-space destination for items stored during paste clears.
        /// </summary>
        public bool TryResolveStoreDestination(Player player, out Vector2 destination) =>
            TryResolveStoreDestinationForItem(itemId: 0, player, out destination, out _);

        /// <summary>
        /// Resolves where a stored item will land and which tracked inventory owns that destination.
        /// </summary>
        public bool TryResolveStoreDestinationForItem(int itemId, Player player, out Vector2 destination, out int inventoryIndex)
        {
            // --- Prefer storable high-priority chests, then any chest, then in-range player fallback.
            destination = default;
            inventoryIndex = -1;
            if (_inventories.Count == 0)
            {
                TABLog.Debug("StoreDestination miss: no inventories", TABLog.InventoryCategory);
                return false;
            }

            if (itemId > 0)
            {
                for (int i = 0; i < _inventories.Count; i++)
                {
                    if (!CanStoreInInventory(_inventories[i], itemId))
                        continue;

                    inventoryIndex = i;
                    destination = InventoryManager.GetInventoryWorldCenter(_inventories[i], player);
                    TABLog.Debug(
                        $"StoreDestination item={itemId} -> index={inventoryIndex} "
                        + $"source={InventoryManager.DescribeSource(_inventories[i])} mode=storable",
                        TABLog.InventoryCategory);
                    return true;
                }
            }

            for (int i = 0; i < _inventories.Count; i++)
            {
                if (_inventories[i].Chest is not Chest chest)
                    continue;

                inventoryIndex = i;
                destination = InventoryManager.GetChestWorldCenter(chest);
                TABLog.Debug(
                    $"StoreDestination item={itemId} -> index={inventoryIndex} "
                    + $"source={InventoryManager.DescribeSource(_inventories[i])} mode=chestFallback",
                    TABLog.InventoryCategory);
                return true;
            }

            for (int i = 0; i < _inventories.Count; i++)
            {
                if (!_inventories[i].IsPlayerInventory)
                    continue;

                inventoryIndex = i;
                destination = InventoryManager.GetInventoryWorldCenter(_inventories[i], player);
                TABLog.Debug(
                    $"StoreDestination item={itemId} -> index={inventoryIndex} "
                    + $"source={InventoryManager.DescribeSource(_inventories[i])} mode=playerFallback",
                    TABLog.InventoryCategory);
                return true;
            }

            TABLog.Debug(
                $"StoreDestination miss: no adjacent inventory item={itemId}",
                TABLog.InventoryCategory);
            return false;
        }

        private void RebuildIfNeeded()
        {
            if (!_isDirty)
                return;

            _itemRefsByType.Clear();
            for (int i = 0; i < _inventories.Count; i++)
            {
                Item[] slots = _inventories[i].Contents;
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    Item item = slots[slotIndex];
                    if (item.type <= ItemID.None || item.stack <= 0)
                        continue;

                    if (!_itemRefsByType.TryGetValue(item.type, out List<InventoryItemRef>? refs))
                    {
                        refs = [];
                        _itemRefsByType[item.type] = refs;
                    }

                    refs.Add(new InventoryItemRef(_inventories[i], item));
                }
            }

            _isDirty = false;
        }

        private bool HasResolvableTileItemIds(ushort tileType, int style)
        {
            IReadOnlyList<int>? itemIds = ResolveTileItemIds(tileType, style);
            if (itemIds is { Count: > 0 })
                return true;

            return ResolveStyledTileDropItemId(tileType, style) > 0;
        }

        private bool HasInventoryItemForStyledTileDrop(ushort tileType, int style)
        {
            int dropItemId = ResolveStyledTileDropItemId(tileType, style);
            if (dropItemId <= 0)
                return false;

            RebuildIfNeeded();
            return TryFindFirstAvailableByItemIds([dropItemId], out _, out _);
        }

        private static int ResolveStyledTileDropItemId(ushort tileType, int style)
        {
            if (tileType <= 0 || tileType >= TileLoader.TileCount)
                return 0;

            return TileLoader.GetItemDropFromTypeAndStyle(tileType, Math.Max(0, style));
        }

        private bool HasResolvableWallItemIds(ushort wallType)
        {
            IReadOnlyList<int>? itemIds = ResolveWallItemIds(wallType);
            return itemIds is { Count: > 0 };
        }

        private bool TryFindConsumableItemByTile(ushort tileType, int style, out Item? item, out Inventory? sourceInventory)
        {
            IReadOnlyList<int> itemIds = ResolveTileItemIds(tileType, style);
            if (TryFindConsumableByItemIds(itemIds, out item, out sourceInventory))
            {
                TABLog.Debug(
                    $"FindConsumableTile hit tile={tileType} style={style} item={item?.type} "
                    + $"stack={item?.stack} source={InventoryManager.DescribeSource(sourceInventory)} path=map",
                    TABLog.InventoryCategory);
                return true;
            }

            item = null;
            sourceInventory = null;
            int fallbackItemId = ResolveStyledTileDropItemId(tileType, style);
            if (fallbackItemId > 0 && TryFindFirstAvailableByItemIds([fallbackItemId], out item, out sourceInventory))
            {
                TABLog.Debug(
                    $"FindConsumableTile hit tile={tileType} style={style} item={item?.type} "
                    + $"stack={item?.stack} source={InventoryManager.DescribeSource(sourceInventory)} path=dropFallback",
                    TABLog.InventoryCategory);
                return true;
            }

            TABLog.Debug(
                $"FindConsumableTile miss tile={tileType} style={style} mapIds={FormatItemIdList(itemIds)} dropFallback={fallbackItemId}",
                TABLog.InventoryCategory);
            return false;
        }

        private bool TryFindConsumableItemByWall(ushort wallType, out Item? item, out Inventory? sourceInventory)
        {
            IReadOnlyList<int> itemIds = ResolveWallItemIds(wallType);
            return TryFindConsumableByItemIds(itemIds, out item, out sourceInventory);
        }

        private bool TryFindConsumableByItemIds(IReadOnlyList<int>? itemIds, out Item? item, out Inventory? sourceInventory)
        {
            item = null;
            sourceInventory = null;
            if (_inventories.Count == 0 || itemIds is not { Count: > 0 })
                return false;

            RebuildIfNeeded();
            return TryFindFirstAvailableByItemIds(itemIds, out item, out sourceInventory);
        }

        private bool TryFindFirstAvailableByItemIds(IReadOnlyList<int> itemIds, out Item? item, out Inventory? sourceInventory)
        {
            // --- Pick the matching stack from the highest-priority source; discovery index breaks ties.
            item = null;
            sourceInventory = null;
            int winnerPriority = int.MinValue;
            int winnerDiscoveryIndex = int.MaxValue;

            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                if (!_itemRefsByType.TryGetValue(itemId, out List<InventoryItemRef>? candidates))
                    continue;

                for (int j = 0; j < candidates.Count; j++)
                {
                    InventoryItemRef candidate = candidates[j];
                    if (candidate.Item.stack <= 0)
                        continue;

                    Inventory candidateInventory = candidate.Inventory;
                    int priority = candidateInventory.Priority;
                    int discoveryIndex = candidateInventory.DiscoveryIndex;
                    if (priority < winnerPriority)
                        continue;
                    if (priority == winnerPriority && discoveryIndex >= winnerDiscoveryIndex)
                        continue;

                    winnerPriority = priority;
                    winnerDiscoveryIndex = discoveryIndex;
                    item = candidate.Item;
                    sourceInventory = candidateInventory;
                }
            }

            return item is not null;
        }

        private IReadOnlyList<int> ResolveTileItemIds(ushort tileType, int style)
        {
            TileKey key = new(tileType, Math.Max(0, style));
            if (_tileItemIdsByKey.TryGetValue(key, out IReadOnlyList<int>? cached))
                return cached ?? [];

            IReadOnlyList<int>? ids = InteractionMapProvider.GetTileItemIds(tileType, style);
            IReadOnlyList<int> resolved = ids is { Count: > 0 } ? ids : [];
            _tileItemIdsByKey[key] = resolved;
            return resolved;
        }

        private IReadOnlyList<int> ResolveWallItemIds(ushort wallType)
        {
            if (_wallItemIdsByType.TryGetValue(wallType, out IReadOnlyList<int>? cached))
                return cached ?? [];

            IReadOnlyList<int>? ids = InteractionMapProvider.GetWallItemIds(wallType);
            IReadOnlyList<int> resolved = ids is { Count: > 0 } ? ids : [];
            _wallItemIdsByType[wallType] = resolved;
            return resolved;
        }

        private static bool TryResolveInventoryOrigin(Inventory? sourceInventory, Player player, out Vector2 origin)
        {
            origin = default;
            if (sourceInventory?.Chest is Chest chest)
            {
                origin = InventoryManager.GetChestWorldCenter(chest);
                return true;
            }

            if (sourceInventory is not null)
            {
                origin = player.Center;
                return true;
            }

            return false;
        }

        private bool TryReserveForMutation(
            Player player,
            TryFindConsumableDelegate tryFind,
            out Item? item,
            out Inventory? sourceInventory,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest)
        {
            item = null;
            sourceInventory = null;
            origin = default;
            fromChest = false;
            sourceChest = null;
            if (!tryFind(out item, out sourceInventory)
                || item is null
                || !TryResolveInventoryOrigin(sourceInventory, player, out origin))
            {
                TABLog.Debug("ReserveMutation miss: item not found or origin unresolved", TABLog.InventoryCategory);
                return false;
            }

            sourceChest = sourceInventory?.Chest;
            fromChest = sourceChest is not null;
            TABLog.Debug(
                $"ReserveMutation found item={item.type} stack={item.stack} source={InventoryManager.DescribeSource(sourceInventory)} "
                + $"awaitingConsume=true",
                TABLog.InventoryCategory);
            return true;
        }

        private int ConsumeReservedItem(Item item, Inventory? sourceInventory, string reason)
        {
            int itemType = item.type;
            int stackBefore = item.stack;
            int slotIndex = sourceInventory is not null
                ? InventoryManager.FindSlotIndex(sourceInventory, item)
                : -1;
            item.stack--;
            int stackAfter = item.stack;
            if (item.stack <= 0)
                item.TurnToAir();
            _isDirty = true;
            TABLog.Debug(
                $"Consume {reason} item={itemType} stack {stackBefore}->{stackAfter} "
                + $"source={InventoryManager.DescribeSource(sourceInventory)} slot={slotIndex}",
                TABLog.InventoryCategory);
            if (sourceInventory is not null && slotIndex >= 0)
                InventoryManager.SyncInventorySlot(sourceInventory, slotIndex, reason);
            return itemType;
        }

        /// <summary>Finds the tracked inventory that owns the given item reference.</summary>
        private bool TryFindInventoryContainingItem(Item item, out Inventory inventory)
        {
            // --- Scan tracked sources for the exact item instance used by a reservation.
            for (int i = 0; i < _inventories.Count; i++)
            {
                if (InventoryManager.FindSlotIndex(_inventories[i], item) < 0)
                    continue;

                inventory = _inventories[i];
                return true;
            }

            inventory = null!;
            return false;
        }

        private static bool CanStoreInInventory(Inventory inventory, int itemId)
        {
            Item[] slots = inventory.Contents;
            for (int i = 0; i < slots.Length; i++)
            {
                Item slot = slots[i];
                if (slot.type == itemId && slot.stack > 0 && slot.stack < slot.maxStack)
                    return true;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                Item slot = slots[i];
                if (slot.type == ItemID.None || slot.stack <= 0)
                    return true;
            }

            return false;
        }

        private int TryStoreInInventory(Inventory inventory, int itemId, int stack)
        {
            Item[] slots = inventory.Contents;
            for (int i = 0; i < slots.Length && stack > 0; i++)
            {
                Item slot = slots[i];
                if (slot.type != itemId || slot.stack <= 0)
                    continue;

                int canAdd = slot.maxStack - slot.stack;
                if (canAdd <= 0)
                    continue;

                int add = Math.Min(canAdd, stack);
                slot.stack += add;
                stack -= add;
                InventoryManager.SyncInventorySlot(inventory, i, "StoreStack");
            }

            for (int i = 0; i < slots.Length && stack > 0; i++)
            {
                Item slot = slots[i];
                if (slot.type != ItemID.None && slot.stack > 0)
                    continue;

                slot.SetDefaults(itemId);
                int add = Math.Min(slot.maxStack, stack);
                slot.stack = add;
                stack -= add;
                InventoryManager.SyncInventorySlot(inventory, i, "StoreNew");
            }

            _isDirty = true;
            return stack;
        }

        private void LogTrackedInventories(string reason)
        {
            if (!TABLog.IsDebugEnabled)
                return;

            if (_inventories.Count == 0)
            {
                TABLog.Debug($"InventorySnapshot {reason}: no inventories", TABLog.InventoryCategory);
                return;
            }

            for (int i = 0; i < _inventories.Count; i++)
            {
                TABLog.Debug(
                    $"InventorySnapshot {reason} index={i} {InventoryManager.DescribeInventory(_inventories[i])}",
                    TABLog.InventoryCategory);
            }
        }

        private static string FormatItemIdList(IReadOnlyList<int> itemIds)
        {
            if (itemIds is not { Count: > 0 })
                return "[]";

            return $"[{string.Join(',', itemIds)}]";
        }
    }
}
