using System.Collections.Generic;
using Terraria;
using ArchitectsBlueprintMod.Core.TileMapping;
using Microsoft.Xna.Framework;
using System;
using Terraria.ID;

namespace ArchitectsBlueprintMod.Core
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

        public static void ConsumeItem(Item item)
        {
            item.stack--;
            if (item.stack <= 0)
                item.TurnToAir();
        }
    }
}
