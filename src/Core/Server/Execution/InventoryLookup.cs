using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
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
        private readonly Dictionary<Inventory, int> _inventoryOrder = [];
        private bool _isDirty = true;

        public IReadOnlyList<Inventory> Inventories => _inventories;

        /// <summary>
        /// Replaces tracked inventories and invalidates compiled indexes.
        /// </summary>
        public void Refresh(IReadOnlyList<Inventory>? inventories)
        {
            _inventories.Clear();
            _inventoryOrder.Clear();
            if (inventories is { Count: > 0 })
            {
                for (int i = 0; i < inventories.Count; i++)
                {
                    _inventories.Add(inventories[i]);
                    _inventoryOrder[inventories[i]] = i;
                }
            }

            _itemRefsByType.Clear();
            _tileItemIdsByKey.Clear();
            _wallItemIdsByType.Clear();
            _isDirty = true;
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

        public bool HasItemForTile(ushort tileType, int style) =>
            TryFindConsumableItemByTile(tileType, style, out _, out _);

        public bool HasItemForWall(ushort wallType) =>
            TryFindConsumableItemByWall(wallType, out _, out _);

        public bool TryReserveForTileMutation(
            ushort tileType,
            int style,
            Player player,
            out int placeStyle,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest)
        {
            placeStyle = Math.Max(0, style);
            if (!TryReserveForMutation(
                player,
                (out Item? item, out Inventory? sourceInventory) =>
                    TryFindConsumableItemByTile(tileType, style, out item, out sourceInventory),
                out Item? item,
                out origin,
                out fromChest,
                out sourceChest))
            {
                itemType = 0;
                TABLog.Debug(
                    $"ReserveTile miss tile={tileType} style={style}",
                    TABLog.InventoryCategory);
                return false;
            }

            placeStyle = Math.Max(0, item!.placeStyle);
            itemType = ConsumeReservedItem(item);
            TABLog.Debug(
                $"ReserveTile ok tile={tileType} style={style} item={itemType} source={(fromChest ? "chest" : "player")} origin=({origin.X:0.##},{origin.Y:0.##})",
                TABLog.InventoryCategory);
            return true;
        }

        public bool TryReserveForWallMutation(
            ushort wallType,
            Player player,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest)
        {
            if (!TryReserveForMutation(
                player,
                (out Item? item, out Inventory? sourceInventory) =>
                    TryFindConsumableItemByWall(wallType, out item, out sourceInventory),
                out Item? item,
                out origin,
                out fromChest,
                out sourceChest))
            {
                itemType = 0;
                TABLog.Debug(
                    $"ReserveWall miss wall={wallType}",
                    TABLog.InventoryCategory);
                return false;
            }

            itemType = ConsumeReservedItem(item!);
            TABLog.Debug(
                $"ReserveWall ok wall={wallType} item={itemType} source={(fromChest ? "chest" : "player")} origin=({origin.X:0.##},{origin.Y:0.##})",
                TABLog.InventoryCategory);
            return true;
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
                        $"StoreDestination item={itemId} -> index={inventoryIndex} mode=storable",
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
                    $"StoreDestination item={itemId} -> index={inventoryIndex} mode=chestFallback",
                    TABLog.InventoryCategory);
                return true;
            }

            inventoryIndex = 0;
            destination = player.Center;
            TABLog.Debug(
                $"StoreDestination item={itemId} -> index=0 mode=playerFallback",
                TABLog.InventoryCategory);
            return true;
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

        private bool TryFindConsumableItemByTile(ushort tileType, int style, out Item? item, out Inventory? sourceInventory)
        {
            IReadOnlyList<int> itemIds = ResolveTileItemIds(tileType, style);
            if (TryFindConsumableByItemIds(itemIds, out item, out sourceInventory))
                return true;

            item = null;
            sourceInventory = null;
            if (tileType >= TileID.Count && tileType < TileLoader.TileCount)
            {
                int fallbackItemId = TileLoader.GetItemDropFromTypeAndStyle(tileType, Math.Max(0, style));
                if (fallbackItemId > 0 && TryFindFirstAvailableByItemIds([fallbackItemId], out item, out sourceInventory))
                    return true;
            }

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
            item = null;
            sourceInventory = null;
            int winnerInventoryIndex = int.MaxValue;

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

                    if (!_inventoryOrder.TryGetValue(candidate.Inventory, out int inventoryIndex))
                        continue;
                    if (inventoryIndex < 0 || inventoryIndex >= winnerInventoryIndex)
                        continue;

                    winnerInventoryIndex = inventoryIndex;
                    item = candidate.Item;
                    sourceInventory = candidate.Inventory;
                    if (winnerInventoryIndex == 0)
                        return true;
                }
            }

            return item is not null;
        }

        private IReadOnlyList<int> ResolveTileItemIds(ushort tileType, int style)
        {
            TileKey key = new(tileType, Math.Max(0, style));
            if (_tileItemIdsByKey.TryGetValue(key, out IReadOnlyList<int>? ids))
                return ids;

            ids = InteractionMapProvider.GetTileItemIds(tileType, style);
            _tileItemIdsByKey[key] = ids;
            return ids;
        }

        private IReadOnlyList<int> ResolveWallItemIds(ushort wallType)
        {
            if (_wallItemIdsByType.TryGetValue(wallType, out IReadOnlyList<int>? ids))
                return ids;

            ids = InteractionMapProvider.GetWallItemIds(wallType);
            _wallItemIdsByType[wallType] = ids;
            return ids;
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
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest)
        {
            item = null;
            origin = default;
            fromChest = false;
            sourceChest = null;
            if (!tryFind(out item, out Inventory? sourceInventory)
                || item is null
                || !TryResolveInventoryOrigin(sourceInventory, player, out origin))
                return false;

            sourceChest = sourceInventory?.Chest;
            fromChest = sourceChest is not null;
            return true;
        }

        private int ConsumeReservedItem(Item item)
        {
            int itemType = item.type;
            item.stack--;
            if (item.stack <= 0)
                item.TurnToAir();
            _isDirty = true;
            return itemType;
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
                InventoryManager.SyncChestSlot(inventory, i);
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
                InventoryManager.SyncChestSlot(inventory, i);
            }

            _isDirty = true;
            return stack;
        }
    }
}
