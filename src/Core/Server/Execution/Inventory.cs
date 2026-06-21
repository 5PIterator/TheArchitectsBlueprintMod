using System.Collections.Generic;
using Terraria;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Microsoft.Xna.Framework;
using System;
using Terraria.ID;
using Terraria.ObjectData;

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
        /// Decrements one item from a paste inventory slot and plays the flying-item transfer effect toward the placed tile.
        /// </summary>
        public static void ConsumeItem(Item item, Point destinationTile, IReadOnlyList<Inventory>? inventories, Player player)
        {
            // --- Resolve source chest/player position, spawn transfer particle, then decrement stack.

            VisualizeItemConsumption(item, destinationTile, inventories, player);

            item.stack--;
            if (item.stack <= 0)
                item.TurnToAir();
        }

        /// <summary>
        /// Spawns Terraria's chest item-transfer particle from the source inventory toward a world tile.
        /// </summary>
        private static void VisualizeItemConsumption(Item item, Point destinationTile, IReadOnlyList<Inventory>? inventories, Player player)
        {
            // --- Skip when no destination origin can be resolved.

            if (!TryResolveItemOrigin(item, inventories, player, out Vector2 origin))
                return;

            Vector2 destination = GetTileWorldCenter(destinationTile);
            Chest.VisualizeChestTransfer(origin, destination, item, 1);
        }

        /// <summary>
        /// Finds the inventory list entry that owns the given item slot reference.
        /// </summary>
        private static Inventory? FindInventoryForItem(Item item, IReadOnlyList<Inventory>? inventories)
        {
            // --- Match by slot reference because flattened paste items share chest array instances.

            if (inventories is null)
                return null;

            foreach (Inventory inventory in inventories)
            {
                for (int i = 0; i < inventory.Contents.Length; i++)
                {
                    if (ReferenceEquals(inventory.Contents[i], item))
                        return inventory;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves the world-space origin for an item consumed during paste.
        /// </summary>
        private static bool TryResolveItemOrigin(Item item, IReadOnlyList<Inventory>? inventories, Player player, out Vector2 origin)
        {
            // --- Prefer the owning chest center; fall back to the paste owner's position.

            origin = default;
            Inventory? source = FindInventoryForItem(item, inventories);
            if (source?.Chest is Chest chest)
            {
                origin = GetChestWorldCenter(chest);
                return true;
            }

            if (source is not null)
            {
                origin = player.Center;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the pixel center of a chest's tile footprint in world space.
        /// </summary>
        private static Vector2 GetChestWorldCenter(Chest chest)
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
        private static Vector2 GetTileWorldCenter(Point tile) =>
            new(tile.X * 16 + 8, tile.Y * 16 + 8);
    }
}
