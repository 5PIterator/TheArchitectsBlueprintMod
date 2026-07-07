using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
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
            return FromChest(Main.chest[chestIndex]);
        }

    }

    /// <summary>
    /// Shared helpers for inventory world-space math, visuals, and network sync.
    /// </summary>
    public class InventoryManager
    {
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

        /// <summary>Plays the chest-open animation on the exact chest instance, without tile lookup.</summary>
        public static void PlayChestEatAnimation(Chest chest)
        {
            if (chest is null)
                return;

            int duration = Main.rand.Next(60, 80) + 10;
            chest.eatingAnimationTime = Math.Max(chest.eatingAnimationTime, duration);
        }

        /// <summary>Returns the world center for a paste inventory, using player center for non-chest sources.</summary>
        public static Vector2 GetInventoryWorldCenter(Inventory inventory, Player player) =>
            inventory.Chest is Chest chest ? GetChestWorldCenter(chest) : player.Center;

        /// <summary>Drops an item stack in the world at the given tile and syncs it on server.</summary>
        public static void DropItemAtTile(int itemId, int stack, Point worldTile)
        {
            if (itemId <= 0 || stack <= 0)
                return;

            int dropped = Item.NewItem(
                new EntitySource_TileBreak(worldTile.X, worldTile.Y),
                worldTile.X * 16,
                worldTile.Y * 16,
                16,
                16,
                itemId,
                stack);
            if (dropped >= 0 && Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncItem, -1, -1, null, dropped);
        }

        /// <summary>Syncs a modified chest slot to connected clients when running as server.</summary>
        internal static void SyncChestSlot(Inventory inventory, int slot)
        {
            if (Main.netMode != NetmodeID.Server || inventory.Chest is not Chest chest)
                return;

            int chestIndex = Chest.FindChest(chest.x, chest.y);
            if (chestIndex >= 0)
                NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, chestIndex, slot);
        }
    }
}
