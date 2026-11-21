using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core{
    /// <summary>
    /// Centralized lookup and mutation API for paste inventories.
    /// </summary>
    internal sealed class InventoryLookup
    {
        private readonly struct InventoryItemRef(Inventory inventory, Item item)
        {
            public Inventory Inventory { get; } = inventory;
            public Item Item { get; } = item;
        }

        private readonly List<Inventory> _inventories = [];
        private readonly Dictionary<int, List<InventoryItemRef>> _itemRefsByType = [];
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

        public bool HasItemForTile(
            ushort tileType,
            int style,
            InteractionBudget? budget = null,
            int? baseId = null,
            bool requireInventory = true) =>
            GetTileItemAvailability(tileType, style, budget, baseId, requireInventory) == TileItemAvailability.Available;

        public bool HasItemForWall(ushort wallType, InteractionBudget? budget = null, bool requireInventory = true) =>
            GetWallItemAvailability(wallType, budget, requireInventory) == TileItemAvailability.Available;

        /// <summary>True when any priority-ordered paint/coating interaction for the main is available.</summary>
        public bool HasItemForPaint(int paintMainId, InteractionBudget? budget = null, bool requireInventory = true) =>
            GetPaintItemAvailability(paintMainId, budget, requireInventory) == TileItemAvailability.Available;

        /// <summary>Returns tile availability from InteractionMap only; inventory presence is optional.</summary>
        public TileItemAvailability GetTileItemAvailability(
            ushort tileType,
            int style,
            InteractionBudget? budget = null,
            int? baseId = null,
            bool requireInventory = true)
        {
            // --- Walk .itr rows only; never fall back to TileLoader drops at runtime.

            bool sawInteraction = false;
            foreach (InteractionEntry entry in InteractionMapProvider.EnumerateTileInteractions(tileType, style, baseId))
            {
                sawInteraction = true;
                if (GetInteractionAvailability(entry, budget, requireInventory) == TileItemAvailability.Available)
                    return TileItemAvailability.Available;
            }

            if (sawInteraction)
                return requireInventory ? TileItemAvailability.MissingFromInventory : TileItemAvailability.UnresolvedInteraction;

            return TileItemAvailability.UnresolvedInteraction;
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
            bool requireInventory = true)
        {
            // --- Walk priority-sorted .itr rows; unresolved when the map has no usable entry.

            bool sawInteraction = false;
            foreach (InteractionEntry entry in InteractionMapProvider.EnumerateInteractions(layer, mainId, style, baseId))
            {
                sawInteraction = true;
                if (GetInteractionAvailability(entry, budget, requireInventory) == TileItemAvailability.Available)
                    return TileItemAvailability.Available;
            }

            if (sawInteraction)
                return requireInventory ? TileItemAvailability.MissingFromInventory : TileItemAvailability.UnresolvedInteraction;

            return TileItemAvailability.UnresolvedInteraction;
        }

        public TileItemAvailability GetWallItemAvailability(
            ushort wallType,
            InteractionBudget? budget = null,
            bool requireInventory = true) =>
            GetMainItemAvailability("Wall", wallType, budget, requireInventory: requireInventory);

        /// <summary>Returns availability for any tool that can place the given wire kind.</summary>
        public TileItemAvailability GetWireItemAvailability(
            int wireKind,
            InteractionBudget? budget = null,
            bool requireInventory = true) =>
            GetMainItemAvailability("Wire", wireKind, budget, requireInventory: requireInventory);

        /// <summary>Returns availability for any tool that can place the given liquid type.</summary>
        public TileItemAvailability GetLiquidItemAvailability(
            int liquidType,
            InteractionBudget? budget = null,
            bool requireInventory = true) =>
            GetMainItemAvailability("Liquid", liquidType, budget, requireInventory: requireInventory);

        /// <summary>Returns availability for any tool that can apply the given paint or coating main.</summary>
        public TileItemAvailability GetPaintItemAvailability(
            int paintMainId,
            InteractionBudget? budget = null,
            bool requireInventory = true) =>
            GetMainItemAvailability("Paint", paintMainId, budget, requireInventory: requireInventory);

        /// <summary>Returns whether a single .itr interaction is available for the optional inventory requirement.</summary>
        public TileItemAvailability GetInteractionAvailability(
            InteractionEntry entry,
            InteractionBudget? budget,
            bool requireInventory = true)
        {
            // --- Require a mapped tool id; skip inventory presence when requireInventory is false.

            if (!entry.ItemId.HasValue || entry.ItemId.Value <= 0)
                return TileItemAvailability.UnresolvedInteraction;

            if (!requireInventory)
                return TileItemAvailability.Available;

            if (_inventories.Count == 0)
                return TileItemAvailability.MissingFromInventory;

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
            out InteractionReservation reservation,
            bool requireInventory = true)
        {
            // --- Map-only zero-count path when !requireInventory; otherwise debit from tracked inventories.

            reservation = default;
            if (!entry.ItemId.HasValue || entry.ItemId.Value <= 0)
                return false;

            int consumableId = entry.AmmoId ?? entry.ItemId.Value;
            int placeStyle = entry.Style ?? 0;

            if (!requireInventory)
            {
                // Prefer an inventory origin when the tool happens to be present; else fly from the player.
                Vector2 origin = player.Center;
                bool fromChest = false;
                Chest? sourceChest = null;
                Item? visualItem = null;
                RebuildIfNeeded();
                if (TryFindFirstAvailableByItemIds(entry.GetToolItemIds(), out Item? toolItem, out Inventory? toolInventory)
                    && toolItem is not null
                    && toolInventory is not null
                    && TryResolveInventoryOrigin(toolInventory, player, out origin))
                {
                    placeStyle = entry.Style ?? Math.Max(0, toolItem.placeStyle);
                    visualItem = toolItem;
                    sourceChest = toolInventory.Chest;
                    fromChest = sourceChest is not null;
                }

                reservation = new InteractionReservation(
                    ToolItemId: entry.ItemId.Value,
                    ConsumableItemId: consumableId,
                    PlaceStyle: Math.Max(0, placeStyle),
                    ConsumableItem: visualItem,
                    Origin: origin,
                    FromChest: fromChest,
                    SourceChest: sourceChest,
                    ConsumedStack: false);

                TABLog.Debug(
                    $"ReserveInteraction zeroCount tool={entry.ItemId} consumable={consumableId} "
                    + $"layer={entry.Layer} main={entry.MainId}",
                    TABLog.InventoryCategory);
                return true;
            }

            RebuildIfNeeded();
            if (!TryFindFirstAvailableByItemIds(entry.GetToolItemIds(), out Item? reservedTool, out Inventory? reservedToolInventory)
                || reservedTool is null
                || reservedToolInventory is null
                || !TryResolveInventoryOrigin(reservedToolInventory, player, out Vector2 reservedOrigin))
            {
                TABLog.Debug(
                    $"ReserveInteraction miss tool={entry.ItemId} layer={entry.Layer} main={entry.MainId}",
                    TABLog.InventoryCategory);
                return false;
            }

            placeStyle = entry.Style ?? Math.Max(0, reservedTool.placeStyle);
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

            Chest? reservedSourceChest = reservedToolInventory.Chest;
            reservation = new InteractionReservation(
                ToolItemId: entry.ItemId.Value,
                ConsumableItemId: consumableId,
                PlaceStyle: Math.Max(0, placeStyle),
                ConsumableItem: consumableItem ?? reservedTool,
                Origin: reservedOrigin,
                FromChest: reservedSourceChest is not null,
                SourceChest: reservedSourceChest,
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
            int? baseId = null,
            bool requireInventory = true)
        {
            // --- Prefer finite-first mapped rows when inventory is optional; otherwise try each until one reserves.

            reservedEntry = null;
            reservation = default;

            if (!requireInventory)
            {
                if (!InteractionSelection.TrySelectFiniteFirst(
                        InteractionMapProvider.EnumerateInteractions(layer, mainId, style, baseId),
                        isMet: null,
                        out InteractionEntry? chosen,
                        out _,
                        out _,
                        out _)
                    || chosen is null)
                {
                    return false;
                }

                if (!TryReserveForInteraction(chosen, budget, player, out reservation, requireInventory: false))
                    return false;

                reservedEntry = chosen;
                TABLog.Debug(
                    $"ReserveMain zeroCount layer={layer} main={mainId} tool={chosen.ItemId} item={reservation.ConsumableItemId}",
                    TABLog.InventoryCategory);
                return true;
            }

            foreach (InteractionEntry entry in InteractionMapProvider.EnumerateInteractions(layer, mainId, style, baseId))
            {
                if (TryReserveForInteraction(entry, budget, player, out reservation, requireInventory: true))
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

        /// <summary>Reserves a tile place interaction from .itr only; fails when the map has no row.</summary>
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
            out PlacementReservationLedger ledger,
            bool requireInventory = true)
        {
            // --- InteractionMap is the only authority; TileLoader drops are discovery-only.

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
                    baseId,
                    requireInventory)
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
                    + $"requireInventory={requireInventory} source={(fromChest ? "chest" : "player")} "
                    + $"origin=({origin.X:0.##},{origin.Y:0.##}) path=interaction",
                    TABLog.InventoryCategory);
                return true;
            }

            TABLog.Debug(
                $"ReserveTile miss tile={tileType} style={style} requireInventory={requireInventory}",
                TABLog.InventoryCategory);
            return false;
        }

        /// <summary>Reserves a wall place interaction from .itr only; fails when the map has no row.</summary>
        public bool TryReserveForWallMutation(
            ushort wallType,
            Player player,
            InteractionBudget? budget,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest,
            out PlacementReservationLedger ledger,
            bool requireInventory = true)
        {
            // --- InteractionMap is the only authority for wall place tools.

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
                    out InteractionReservation reservation,
                    requireInventory: requireInventory)
                && reservedEntry is not null)
            {
                itemType = reservation.ConsumableItemId;
                origin = reservation.Origin;
                fromChest = reservation.FromChest;
                sourceChest = reservation.SourceChest;
                ledger.Record(reservedEntry, in reservation);
                TABLog.Debug(
                    $"ReserveWall ok wall={wallType} item={itemType} consumedStack={reservation.ConsumedStack} "
                    + $"requireInventory={requireInventory} source={(fromChest ? "chest" : "player")} "
                    + $"origin=({origin.X:0.##},{origin.Y:0.##}) path=interaction",
                    TABLog.InventoryCategory);
                return true;
            }

            TABLog.Debug(
                $"ReserveWall miss wall={wallType} requireInventory={requireInventory}",
                TABLog.InventoryCategory);
            return false;
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
            out PlacementReservationLedger ledger,
            bool requireInventory = true)
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
                    out InteractionReservation reservation,
                    requireInventory: requireInventory)
                && reservedEntry is not null)
            {
                itemType = reservation.ConsumableItemId;
                origin = reservation.Origin;
                fromChest = reservation.FromChest;
                sourceChest = reservation.SourceChest;
                ledger.Record(reservedEntry, in reservation);
                TABLog.Debug(
                    $"ReserveWire ok kind={wireKind} tool={reservedEntry.ItemId} item={itemType} consumedStack={reservation.ConsumedStack} "
                    + $"requireInventory={requireInventory} source={(fromChest ? "chest" : "player")} "
                    + $"origin=({origin.X:0.##},{origin.Y:0.##}) path=interaction",
                    TABLog.InventoryCategory);
                return true;
            }

            TABLog.Debug(
                $"ReserveWire miss kind={wireKind} requireInventory={requireInventory}",
                TABLog.InventoryCategory);
            return false;
        }

        /// <summary>Reserves a paint/coating placement using any available mapped tool for the paint main.</summary>
        public bool TryReserveForPaintKind(
            int paintMainId,
            Player player,
            InteractionBudget? budget,
            out InteractionEntry? reservedEntry,
            out InteractionReservation reservation,
            bool requireInventory = true) =>
            TryReserveForMain(
                "Paint",
                paintMainId,
                player,
                budget,
                out reservedEntry,
                out reservation,
                requireInventory: requireInventory);

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
            out Chest? sourceChest,
            bool requireInventory = true)
        {
            // --- Pick the first mapped liquid tool that can cover every cell; roll back partial failures.
            ledger = new PlacementReservationLedger();
            origin = default;
            fromChest = false;
            sourceChest = null;

            if (cellCount <= 0)
                return true;

            IEnumerable<InteractionEntry> candidates = InteractionMapProvider.EnumerateLiquidInteractions(liquidType);
            if (!requireInventory)
            {
                if (!InteractionSelection.TrySelectFiniteFirst(
                        candidates,
                        isMet: null,
                        out InteractionEntry? chosen,
                        out _,
                        out _,
                        out _)
                    || chosen is null)
                {
                    TABLog.Debug(
                        $"ReserveLiquidPool miss liquid={liquidType} cells={cellCount} requireInventory=false",
                        TABLog.InventoryCategory);
                    return false;
                }

                candidates = [chosen];
            }

            foreach (InteractionEntry entry in candidates)
            {
                var completed = new List<InteractionReservation>(cellCount);
                bool failed = false;

                for (int i = 0; i < cellCount; i++)
                {
                    if (!TryReserveForInteraction(
                            entry,
                            budget,
                            player,
                            out InteractionReservation reservation,
                            requireInventory))
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
                    + $"visual={ledger.VisualItemType} requireInventory={requireInventory} "
                    + $"source={(fromChest ? "chest" : "player")}",
                    TABLog.InventoryCategory);
                return true;
            }

            TABLog.Debug(
                $"ReserveLiquidPool miss liquid={liquidType} cells={cellCount} requireInventory={requireInventory}",
                TABLog.InventoryCategory);
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

    }
}
