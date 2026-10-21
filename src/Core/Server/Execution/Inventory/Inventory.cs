using System;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ObjectData;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Represents an inventory.
    /// </summary>
    public class Inventory
    {
        /// <summary>Tile reach outward from the projection used when detecting nearby inventories.</summary>
        public const int ReachTiles = 2;

        /// <summary>Lowest source priority; reserved for the in-range player inventory fallback.</summary>
        public const int PlayerPriority = 0;

        public Chest? Chest { get; set; } = null;
        public Item[] Contents { get; set; } = [];

        /// <summary>
        /// Selection priority for consume/store. Higher wins; the in-range player fallback is
        /// <see cref="PlayerPriority"/>.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Stable discovery order among equal-priority sources (lower wins ties).
        /// </summary>
        public int DiscoveryIndex { get; set; }

        /// <summary>
        /// <see cref="Player.whoAmI"/> when this wraps a player inventory; <c>-1</c> for chests.
        /// </summary>
        public int OwnerPlayerIndex { get; set; } = -1;

        /// <summary>True when this source is a player inventory rather than a chest.</summary>
        public bool IsPlayerInventory => OwnerPlayerIndex >= 0;

        /// <summary>Builds an inventory wrapper around the given player's main inventory slots.</summary>
        public static Inventory FromPlayer(Player player, int priority = PlayerPriority, int discoveryIndex = 0)
        {
            // --- Bind live player slots with fallback priority and ownership metadata.
            return new Inventory {
                Chest = null,
                Contents = player.inventory,
                Priority = priority,
                DiscoveryIndex = discoveryIndex,
                OwnerPlayerIndex = player.whoAmI
            };
        }

        /// <summary>Builds an inventory wrapper around the given chest's item array.</summary>
        public static Inventory FromChest(Chest chest, int priority, int discoveryIndex = 0)
        {
            // --- Bind chest slots with distance-derived priority and discovery order.
            return new Inventory {
                Chest = chest,
                Contents = chest.item,
                Priority = priority,
                DiscoveryIndex = discoveryIndex,
                OwnerPlayerIndex = -1
            };
        }

        /// <summary>Resolves the chest at the rectangle origin and wraps it as an inventory source.</summary>
        public static Inventory FromRectangle(Rectangle rectangle, int priority, int discoveryIndex = 0)
        {
            // --- Exact Main.chest origin match; Chest.FindChest misses CreateChest ghosts without live frames.

            // The chest is always at the top left of the rectangle.
            if (!InventoryManager.TryFindChestIndexAtExact(rectangle.X, rectangle.Y, out int chestIndex)
                || Main.chest[chestIndex] is null)
            {
                throw new InvalidOperationException("No chest found at the given rectangle.");
            }
            return FromChest(Main.chest[chestIndex], priority, discoveryIndex);
        }

    }

    /// <summary>
    /// Shared helpers for inventory world-space math, visuals, and network sync.
    /// </summary>
    public class InventoryManager
    {
        /// <summary>Returns the search rectangle used for nearby inventory detection.</summary>
        public static Rectangle GetInventorySearchArea(Rectangle targetArea)
        {
            // --- Inflate the projection footprint by the shared reach margin.
            Rectangle searchArea = targetArea;
            searchArea.Inflate(Inventory.ReachTiles, Inventory.ReachTiles);
            return searchArea;
        }

        /// <summary>True when a chest footprint sits in the outer reach ring and does not overlap the projection.</summary>
        public static bool IsChestInInventoryReachRing(Rectangle footprint, Rectangle targetArea)
        {
            // --- Keep the inflated window, then drop any chest that intersects the inner paste area.

            Rectangle searchArea = GetInventorySearchArea(targetArea);
            if (!searchArea.Contains(footprint))
                return false;

            return !targetArea.Intersects(footprint);
        }

        /// <summary>Returns true when the player's tile center lies inside the inventory search area.</summary>
        public static bool IsPlayerInInventoryRange(Player player, Rectangle targetArea)
        {
            // --- Compare the player's center tile against the same reach window used for chests.
            if (player is not { active: true })
                return false;

            Rectangle searchArea = GetInventorySearchArea(targetArea);
            Point playerTile = player.Center.ToTileCoordinates();
            return searchArea.Contains(playerTile.X, playerTile.Y);
        }

        /// <summary>
        /// Computes chest priority from Manhattan distance to the projection center so nearer chests win.
        /// </summary>
        public static int ComputeChestPriority(Rectangle footprint, Rectangle targetArea)
        {
            // --- Invert distance against a reach-aware maximum so closer chests always rank above the player.

            Point targetCenter = new(
                targetArea.X + targetArea.Width / 2,
                targetArea.Y + targetArea.Height / 2);
            Point chestCenter = new(
                footprint.X + footprint.Width / 2,
                footprint.Y + footprint.Height / 2);
            int dist = Math.Abs(targetCenter.X - chestCenter.X) + Math.Abs(targetCenter.Y - chestCenter.Y);
            int maxDist = Math.Max(1, targetArea.Width + targetArea.Height + Inventory.ReachTiles * 4);
            return Math.Max(1, maxDist - dist);
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

            TABLog.Debug(
                $"DropItemAtTile item={itemId} stack={stack} world=({worldTile.X},{worldTile.Y})",
                TABLog.InventoryCategory);

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

        /// <summary>Syncs a modified inventory slot (chest or player) to connected clients when running as server.</summary>
        internal static void SyncInventorySlot(Inventory inventory, int slot, string? reason = null)
        {
            // --- Dispatch to chest or player sync based on source kind.
            if (inventory.Chest is not null)
            {
                SyncChestSlot(inventory, slot, reason);
                return;
            }

            SyncPlayerSlot(inventory, slot, reason);
        }

        /// <summary>Syncs a modified chest slot to connected clients when running as server or listen host.</summary>
        internal static void SyncChestSlot(Inventory inventory, int slot, string? reason = null)
        {
            // --- Dedicated server and Host & Play listen host both broadcast SyncChestItem.

            if (inventory.Chest is not Chest chest)
                return;

            if (Main.netMode != NetmodeID.Server && !TileMappingAuthority.IsLocalHostClient())
                return;

            if (!TryFindChestIndexAtExact(chest.x, chest.y, out int chestIndex))
                return;

            NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, chestIndex, slot);
            if (TABLog.IsDebugEnabled && reason is not null)
            {
                Item item = inventory.Contents[slot];
                TABLog.Debug(
                    $"SyncChestSlot reason={reason} chest=({chest.x},{chest.y}) slot={slot} item={item.type} stack={item.stack}",
                    TABLog.InventoryCategory);
            }
        }

        /// <summary>Finds the Main.chest index with exact x/y, including CreateChest ghosts without live tiles.</summary>
        internal static bool TryFindChestIndexAtExact(int worldX, int worldY, out int chestIndex)
        {
            // --- Linear scan; do not use Chest.FindChest (skips records without container frames).

            chestIndex = -1;
            for (int i = 0; i < Main.maxChests; i++)
            {
                Chest? candidate = Main.chest[i];
                if (candidate is null)
                    continue;

                if (candidate.x == worldX && candidate.y == worldY)
                {
                    chestIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Syncs a modified player inventory slot to connected clients when running as server.</summary>
        internal static void SyncPlayerSlot(Inventory inventory, int slot, string? reason = null)
        {
            // --- Mirror the player slot change so remote clients see paste consumption/store.
            if (Main.netMode != NetmodeID.Server || !inventory.IsPlayerInventory)
                return;

            int playerIndex = inventory.OwnerPlayerIndex;
            if ((uint)playerIndex >= (uint)Main.maxPlayers || !Main.player[playerIndex].active)
                return;

            NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, playerIndex, slot);
            if (TABLog.IsDebugEnabled && reason is not null)
            {
                Item item = inventory.Contents[slot];
                TABLog.Debug(
                    $"SyncPlayerSlot reason={reason} player={playerIndex} slot={slot} item={item.type} stack={item.stack}",
                    TABLog.InventoryCategory);
            }
        }

        /// <summary>Returns a short label for inventory debug logs.</summary>
        internal static string DescribeSource(Inventory? inventory)
        {
            if (inventory is null)
                return "none";

            if (inventory.Chest is Chest chest)
                return $"chest@({chest.x},{chest.y}) prio={inventory.Priority}";

            return $"player#{inventory.OwnerPlayerIndex} prio={inventory.Priority}";
        }

        /// <summary>Summarizes occupied slots for debug logging.</summary>
        internal static string DescribeInventory(Inventory inventory)
        {
            Item[] slots = inventory.Contents;
            int occupied = 0;
            var summary = new StringBuilder(64);
            for (int i = 0; i < slots.Length; i++)
            {
                Item slot = slots[i];
                if (slot.type <= ItemID.None || slot.stack <= 0)
                    continue;

                occupied++;
                if (summary.Length > 0)
                    summary.Append(';');
                summary.Append($"{slot.type}x{slot.stack}");
            }

            string source = DescribeSource(inventory);
            return $"{source} discovery={inventory.DiscoveryIndex} slots={slots.Length} occupied={occupied} [{summary}]";
        }

        /// <summary>Finds the slot index holding a specific item reference, or -1.</summary>
        internal static int FindSlotIndex(Inventory inventory, Item item)
        {
            Item[] slots = inventory.Contents;
            for (int i = 0; i < slots.Length; i++)
            {
                if (ReferenceEquals(slots[i], item))
                    return i;
            }

            return -1;
        }
    }
}
