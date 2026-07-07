using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Represents an inventory.
    /// </summary>
    public class Inventory
    {
        public Chest? Chest { get; set; } = null;
        public Item[] Contents { get; set; } = [];

        public static Inventory FromPlayer(Player player)
        {
            return new Inventory {
                Chest = null,
                Contents = player.inventory
            };
        }

        public static Inventory FromChest(Chest chest)
        {
            return new Inventory {
                Chest = chest,
                Contents = chest.item
            };
        }

        public static Inventory FromRectangle(Rectangle rectangle)
        {
            /// Find the chest at the given rectangle.

            // The chest is always at the top left of the rectangle.
            int chestIndex = Chest.FindChest(rectangle.X, rectangle.Y);
            if (chestIndex == -1)
            {
                throw new InvalidOperationException("No chest found at the given rectangle.");
            }
            Chest chest = Main.chest[chestIndex];
            return new Inventory {
                Chest = chest,
                Contents = chest.item
            };
        }

        public Item? FindItem(int itemId)
        {
            for (int i = 0; i < Contents.Length; i++)
            {
                if (Contents[i].type == itemId && Contents[i].stack > 0)
                {
                    return Contents[i];
                }
            }
            return null;
        }

        public bool IsEmpty()
        {
            return Contents.Length == 0;
        }

        public int GetItemCount()
        {
            return Contents.Length;
        }

        public bool Validate()
        {
            if (Chest is not null && Chest.item.Length != Contents.Length)
            {
                return false;
            }
            return true;
        }

    }

    /// <summary>
    /// Manages the inventory handled by the paste system.
    /// </summary>
    public class InventoryManager
    {
        /// <summary>
        /// Consolidates the contents of all inventories into a single array.
        /// </summary>
        /// <param name="inventories"></param>
        /// <returns></returns>
        public static Item[] GetContentsFromInventories(IReadOnlyList<Inventory> inventories)
        {
            List<Item> contents = [];
            foreach (Inventory inventory in inventories)
            {
                contents.AddRange(inventory.Contents);
            }
            return [.. contents];
        }

        public static Item? FindItemById(Item[] items, int itemId)
        {
            foreach (Item item in items)
            {
                if (item.type == itemId && item.stack > 0)
                {
                    return item;
                }
            }
            return null;
        }

        public static Item? FindItemByTile(Item[] items, ushort tileType, int style = 0)
        {
            IReadOnlyList<int> itemIds = InteractionMapProvider.GetTileItemIds(tileType, style);
            if (itemIds is { Count: > 0 })
            {
                foreach (int itemId in itemIds)
                {
                    Item? item = FindItemById(items, itemId);
                    if (item is not null)
                        return item;
                }
            }

            // Fallback for modded tiles that register tile->item drop mappings directly through tModLoader.
            if (tileType >= TileID.Count && tileType < TileLoader.TileCount)
            {
                int fallbackItemId = TileLoader.GetItemDropFromTypeAndStyle(tileType, Math.Max(0, style));
                if (fallbackItemId > 0)
                    return FindItemById(items, fallbackItemId);
            }

            return null;
        }

        /// <summary>
        /// Finds the first consumable tile item across paste inventories, preserving per-chest ownership.
        /// </summary>
        public static bool TryFindConsumableItemByTile(
            IReadOnlyList<Inventory>? inventories,
            ushort tileType,
            int style,
            out Item? item,
            out Inventory? sourceInventory)
        {
            item = null;
            sourceInventory = null;
            if (inventories is null || inventories.Count == 0)
                return false;

            // --- Walk inventories in paste order so reservation and visuals stay on the same chest.

            IReadOnlyList<int> itemIds = InteractionMapProvider.GetTileItemIds(tileType, style);
            if (itemIds is { Count: > 0 })
            {
                foreach (Inventory inventory in inventories)
                {
                    foreach (int itemId in itemIds)
                    {
                        Item? found = inventory.FindItem(itemId);
                        if (found is null)
                            continue;

                        item = found;
                        sourceInventory = inventory;
                        return true;
                    }
                }
            }

            if (tileType >= TileID.Count && tileType < TileLoader.TileCount)
            {
                int fallbackItemId = TileLoader.GetItemDropFromTypeAndStyle(tileType, Math.Max(0, style));
                if (fallbackItemId > 0)
                {
                    foreach (Inventory inventory in inventories)
                    {
                        Item? found = inventory.FindItem(fallbackItemId);
                        if (found is null)
                            continue;

                        item = found;
                        sourceInventory = inventory;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Finds the first consumable wall item across paste inventories, preserving per-chest ownership.
        /// </summary>
        public static bool TryFindConsumableItemByWall(
            IReadOnlyList<Inventory>? inventories,
            ushort wallType,
            out Item? item,
            out Inventory? sourceInventory)
        {
            item = null;
            sourceInventory = null;
            if (inventories is null || inventories.Count == 0)
                return false;

            IReadOnlyList<int> itemIds = InteractionMapProvider.GetWallItemIds(wallType);
            if (itemIds is null || itemIds.Count == 0)
                return false;

            foreach (Inventory inventory in inventories)
            {
                foreach (int itemId in itemIds)
                {
                    Item? found = inventory.FindItem(itemId);
                    if (found is null)
                        continue;

                    item = found;
                    sourceInventory = inventory;
                    return true;
                }
            }

            return false;
        }

        public static Item? FindItemByWall(Item[] items, ushort wallType)
        {
            IReadOnlyList<int> itemIds = InteractionMapProvider.GetWallItemIds(wallType);
            if (itemIds == null || itemIds.Count == 0)
                return null;
            foreach (int itemId in itemIds)
            {
                Item? item = FindItemById(items, itemId);
                if (item is not null)
                    return item;
            }
            return null;
        }

        /// <summary>
        /// Reserves one wall item for a place mutation, returning origin and consumed item metadata.
        /// </summary>
        public static bool TryReserveItemForWallMutation(
            Item[]? items,
            ushort wallType,
            IReadOnlyList<Inventory>? inventories,
            Player player,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest)
        {
            itemType = 0;
            origin = default;
            fromChest = false;
            sourceChest = null;
            if (items is null)
                return false;

            if (!TryFindConsumableItemByWall(inventories, wallType, out Item? item, out Inventory? sourceInventory)
                || item is null
                || !TryResolveInventoryOrigin(sourceInventory, player, out origin))
                return false;

            sourceChest = sourceInventory?.Chest;
            fromChest = sourceChest is not null;
            itemType = item.type;
            item.stack--;
            if (item.stack <= 0)
                item.TurnToAir();
            return true;
        }

        /// <summary>
        /// Reserves one tile item for a place mutation, returning origin and consumed item metadata.
        /// </summary>
        public static bool TryReserveItemForTileMutation(
            Item[]? items,
            ushort tileType,
            int style,
            IReadOnlyList<Inventory>? inventories,
            Player player,
            out int placeStyle,
            out int itemType,
            out Vector2 origin,
            out bool fromChest,
            out Chest? sourceChest)
        {
            placeStyle = Math.Max(0, style);
            itemType = 0;
            origin = default;
            fromChest = false;
            sourceChest = null;
            if (items is null)
                return false;

            if (!TryFindConsumableItemByTile(inventories, tileType, style, out Item? item, out Inventory? sourceInventory)
                || item is null
                || !TryResolveInventoryOrigin(sourceInventory, player, out origin))
                return false;

            placeStyle = Math.Max(0, item.placeStyle);
            sourceChest = sourceInventory?.Chest;
            fromChest = sourceChest is not null;
            itemType = item.type;
            item.stack--;
            if (item.stack <= 0)
                item.TurnToAir();
            return true;
        }

        /// <summary>Stores item stacks when a clear projectile reaches the destination inventory.</summary>
        public static void ApplyStoreOnArrival(
            int itemId,
            int stack,
            Point worldTile,
            IReadOnlyList<Inventory>? inventories,
            int preferredInventoryIndex = -1)
        {
            if (itemId <= 0 || stack <= 0)
                return;

            int remaining = stack;
            if (preferredInventoryIndex >= 0
                && inventories is not null
                && preferredInventoryIndex < inventories.Count)
            {
                remaining = TryStoreInInventory(inventories[preferredInventoryIndex], itemId, remaining);
            }

            if (remaining > 0)
                TryStoreThenDropWorld(itemId, remaining, worldTile, inventories);
        }

        /// <summary>
        /// Resolves the world-space origin for an inventory consumed during paste.
        /// </summary>
        private static bool TryResolveInventoryOrigin(Inventory? sourceInventory, Player player, out Vector2 origin)
        {
            // --- Prefer the owning chest center; fall back to the paste owner's position.

            origin = default;
            if (sourceInventory?.Chest is Chest chest)
            {
                origin = GetChestWorldCenter(chest);
                return true;
            }

            if (sourceInventory is not null)
            {
                origin = player.Center;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the pixel center of a chest's tile footprint in world space.
        /// </summary>
        public static Vector2 GetChestWorldCenter(Chest chest)
        {
            // --- Use tile-object width/height so multi-tile containers center correctly.

            Tile tile = Main.tile[chest.x, chest.y];
            TileObjectData? tileData = TileObjectData.GetTileData(tile);
            int w = tileData?.Width ?? 2;
            int h = tileData?.Height ?? 2;
            return new Vector2((chest.x + w * 0.5f) * 16f, (chest.y + h * 0.5f) * 16f);
        }

        /// <summary>
        /// Returns the pixel center of a tile in world space.
        /// </summary>
        public static Vector2 GetTileWorldCenter(Point tile) =>
            new(tile.X * 16 + 8, tile.Y * 16 + 8);

        /// <summary>
        /// Resolves the world-space destination for items stored during paste clears.
        /// </summary>
        public static bool TryResolveStoreDestination(IReadOnlyList<Inventory>? inventories, Player player, out Vector2 destination) =>
            TryResolveStoreDestinationForItem(itemId: 0, inventories, player, out destination, out _);

        /// <summary>
        /// Resolves where a stored item will land and which paste inventory owns that destination.
        /// </summary>
        public static bool TryResolveStoreDestinationForItem(
            int itemId,
            IReadOnlyList<Inventory>? inventories,
            Player player,
            out Vector2 destination,
            out int inventoryIndex)
        {
            // --- Match TryStoreInInventory order so projectile flight and storage use the same chest.

            destination = default;
            inventoryIndex = -1;
            if (inventories is null || inventories.Count == 0)
                return false;

            if (itemId > 0)
            {
                for (int i = 0; i < inventories.Count; i++)
                {
                    if (!CanStoreInInventory(inventories[i], itemId, 1))
                        continue;

                    inventoryIndex = i;
                    destination = TryGetInventoryWorldCenter(inventories[i], player);
                    return true;
                }
            }

            for (int i = 0; i < inventories.Count; i++)
            {
                if (inventories[i].Chest is not Chest)
                    continue;

                inventoryIndex = i;
                destination = GetChestWorldCenter(inventories[i].Chest!);
                return true;
            }

            inventoryIndex = 0;
            destination = player.Center;
            return true;
        }

        /// <summary>Plays the chest-open animation on the exact chest instance, without tile lookup.</summary>
        public static void PlayChestEatAnimation(Chest chest)
        {
            if (chest is null)
                return;

            int duration = Main.rand.Next(60, 80) + 10;
            chest.eatingAnimationTime = Math.Max(chest.eatingAnimationTime, duration);
        }

        /// <summary>Returns whether an inventory can accept at least one stack of the given item.</summary>
        private static bool CanStoreInInventory(Inventory inventory, int itemId, int stack)
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

        private static Vector2 TryGetInventoryWorldCenter(Inventory inventory, Player player) =>
            inventory.Chest is Chest chest ? GetChestWorldCenter(chest) : player.Center;

        /// <summary>Returns the world center for a paste inventory, using player center for non-chest sources.</summary>
        public static Vector2 GetInventoryWorldCenter(Inventory inventory, Player player) =>
            TryGetInventoryWorldCenter(inventory, player);

        /// <summary>
        /// Tries to place <paramref name="stack"/> of <paramref name="itemId"/> into adjacent paste inventories;
        /// spawns any remainder in the world at <paramref name="worldTile"/>.
        /// </summary>
        public static void TryStoreThenDropWorld(int itemId, int stack, Point worldTile, IReadOnlyList<Inventory>? inventories)
        {
            if (itemId <= 0 || stack <= 0)
                return;

            int remaining = stack;
            if (inventories is not null)
            {
                for (int i = 0; i < inventories.Count && remaining > 0; i++)
                    remaining = TryStoreInInventory(inventories[i], itemId, remaining);
            }

            if (remaining <= 0)
                return;

            int dropped = Item.NewItem(
                new EntitySource_TileBreak(worldTile.X, worldTile.Y),
                worldTile.X * 16,
                worldTile.Y * 16,
                16,
                16,
                itemId,
                remaining);
            if (dropped >= 0 && Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncItem, -1, -1, null, dropped);
        }

        /// <summary>Stacks into matching slots first, then empty slots; returns the stack count still unstored.</summary>
        private static int TryStoreInInventory(Inventory inventory, int itemId, int stack)
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
                SyncChestSlot(inventory, i);
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
                SyncChestSlot(inventory, i);
            }

            return stack;
        }

        /// <summary>Syncs a modified chest slot to connected clients when running as server.</summary>
        private static void SyncChestSlot(Inventory inventory, int slot)
        {
            if (Main.netMode != NetmodeID.Server || inventory.Chest is not Chest chest)
                return;

            int chestIndex = Chest.FindChest(chest.x, chest.y);
            if (chestIndex >= 0)
                NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, chestIndex, slot);
        }
    }
}
