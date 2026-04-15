using System.Collections.Generic;
using Terraria;
using ArchitectsStencilMod.Core.TileMapping;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Ordered inventory slots to search for paste items, instead of wiring every inventory type.
    /// Handles index into this list; find methods scan the list, <see cref="ConsumeAt"/> mutates the backing slot.
    /// </summary>
    internal interface IPasteInventorySlots
    {
        /// <summary>Finds a slot containing any of the given item ids, or -1.</summary>
        int FindItemSlot(IReadOnlyList<int> itemIds);

        /// <summary>Finds a slot containing an item that places the given tile type/style, or -1.</summary>
        int FindTileSlot(ushort tileType, int style);

        /// <summary>Finds a slot containing an item that places the given wall type, or -1.</summary>
        int FindWallSlot(ushort wallType);

        /// <summary>Consumes one item at the given slot handle.</summary>
        void ConsumeAt(int handle);

        /// <summary>Returns the place style of the item at the given handle.</summary>
        int GetPlaceStyle(int handle);
    }

    /// <summary>
    /// Builds <see cref="IPasteInventorySlots"/> views (e.g. player hotbar + main inventory as one flat slot list).
    /// </summary>
    internal static class PastingInventory
    {
        /// <summary>
        /// Live player inventory as paste slots. Returns null if player is null.
        /// </summary>
        public static IPasteInventorySlots GetPasteInventorySlots(Player player)
        {
            if (player is null) return null;
            return new PasteInventorySlots(player.inventory);
        }

        /// <summary>
        /// Cloned player inventory as paste slots for planning (mutations do not affect the real inventory).
        /// </summary>
        public static IPasteInventorySlots GetPlanningSlots(Player player)
        {
            if (player is null) return null;

            var clonedInventory = new Item[player.inventory.Length];
            for (int i = 0; i < player.inventory.Length; i++)
                clonedInventory[i] = player.inventory[i].Clone();

            return new PasteInventorySlots(clonedInventory);
        }

        private sealed class PasteInventorySlots : IPasteInventorySlots
        {
            private readonly (Item[] items, int index)[] _slots;

            public PasteInventorySlots(Item[] inventory)
            {
                var list = new List<(Item[], int)>();
                for (int i = 0; i < inventory.Length; i++)
                    list.Add((inventory, i));
                _slots = list.ToArray();
            }

            public int FindTileSlot(ushort tileType, int style)
            {
                var itemIds = InteractionMapProvider.GetTileItemIds(tileType, style);
                return FindItemSlot(itemIds);
            }

            public int FindWallSlot(ushort wallType)
            {
                var itemIds = InteractionMapProvider.GetWallItemIds(wallType);
                return FindItemSlot(itemIds);
            }

            public int FindItemSlot(IReadOnlyList<int> itemIds)
            {
                if (itemIds is null || itemIds.Count == 0) return -1;

                for (int i = 0; i < _slots.Length; i++)
                {
                    var item = _slots[i].items[_slots[i].index];
                    if (item is null || item.stack <= 0 || item.IsAir) continue;
                    for (int j = 0; j < itemIds.Count; j++)
                    {
                        if (itemIds[j] == item.type)
                            return i;
                    }
                }
                return -1;
            }

            public void ConsumeAt(int handle)
            {
                if (handle < 0 || handle >= _slots.Length) return;
                var (items, idx) = _slots[handle];
                Item item = items[idx];
                if (item is null || item.IsAir) return;
                item.stack--;
                if (item.stack <= 0)
                    item.TurnToAir();
                items[idx] = item;
            }

            public int GetPlaceStyle(int handle)
            {
                if (handle < 0 || handle >= _slots.Length) return 0;
                return _slots[handle].items[_slots[handle].index].placeStyle;
            }
        }
    }
}
