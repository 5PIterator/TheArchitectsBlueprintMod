using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Identifies one paste inventory source for manifest attribution.</summary>
    internal readonly record struct ManifestInventorySource(
        bool IsPlayer,
        int IconItemId,
        int ChestX,
        int ChestY);

    /// <summary>
    /// Non-mutating snapshot of paste inventory stacks with source attribution.
    /// </summary>
    internal sealed class InventoryStackLedger
    {
        private readonly Dictionary<int, int> _countsByItemId = [];
        private readonly Dictionary<int, List<ManifestInventorySource>> _sourcesByItemId = [];

        /// <summary>Builds a ledger from the same ordered inventory sources paste execution uses.</summary>
        public static InventoryStackLedger FromInventories(IReadOnlyList<Inventory> inventories)
        {
            // --- Snapshot live stacks without retaining Item references so dry-runs never mutate inventory.

            var ledger = new InventoryStackLedger();
            for (int i = 0; i < inventories.Count; i++)
            {
                Inventory inventory = inventories[i];
                ManifestInventorySource source = DescribeSource(inventory);
                Item[] slots = inventory.Contents;
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    Item item = slots[slot];
                    if (item.type <= ItemID.None || item.stack <= 0)
                        continue;

                    ledger.AddStack(item.type, item.stack, source);
                }
            }

            return ledger;
        }

        /// <summary>Returns the total available stack count for an item type.</summary>
        public int CountAvailable(int itemId)
        {
            if (itemId <= 0)
                return 0;
            return _countsByItemId.TryGetValue(itemId, out int count) ? count : 0;
        }

        /// <summary>Returns every distinct inventory that currently holds the item.</summary>
        public IReadOnlyList<ManifestInventorySource> GetContributingSources(int itemId)
        {
            if (itemId <= 0 || !_sourcesByItemId.TryGetValue(itemId, out List<ManifestInventorySource>? sources))
                return [];
            return sources;
        }

        /// <summary>Builds a source descriptor for a paste inventory wrapper.</summary>
        public static ManifestInventorySource DescribeSource(Inventory inventory)
        {
            // --- Player sources need only the player flag; chests carry container icon and tile coords.

            if (inventory.IsPlayerInventory)
            {
                return new ManifestInventorySource(
                    IsPlayer: true,
                    IconItemId: ItemID.None,
                    ChestX: -1,
                    ChestY: -1);
            }

            Chest? chest = inventory.Chest;
            int iconItemId = ItemID.Chest;
            int chestX = -1;
            int chestY = -1;
            if (chest is not null)
            {
                chestX = chest.x;
                chestY = chest.y;
                iconItemId = ResolveChestIconItemId(chest);
            }

            return new ManifestInventorySource(
                IsPlayer: false,
                IconItemId: iconItemId,
                ChestX: chestX,
                ChestY: chestY);
        }

        private void AddStack(int itemId, int stack, ManifestInventorySource source)
        {
            // --- Accumulate counts and track distinct contributing inventories.

            _countsByItemId.TryGetValue(itemId, out int existing);
            _countsByItemId[itemId] = existing + stack;
            TrackSource(itemId, source);
        }

        private void TrackSource(int itemId, ManifestInventorySource source)
        {
            if (!_sourcesByItemId.TryGetValue(itemId, out List<ManifestInventorySource>? sources))
            {
                sources = [];
                _sourcesByItemId[itemId] = sources;
            }

            for (int i = 0; i < sources.Count; i++)
            {
                ManifestInventorySource existing = sources[i];
                if (existing.IsPlayer == source.IsPlayer
                    && existing.ChestX == source.ChestX
                    && existing.ChestY == source.ChestY
                    && existing.IconItemId == source.IconItemId)
                {
                    return;
                }
            }

            sources.Add(source);
        }

        private static int ResolveChestIconItemId(Chest chest)
        {
            // --- Prefer the chest tile's styled drop so chat shows the actual container icon.

            Tile tile = Main.tile[chest.x, chest.y];
            if (!tile.HasTile)
                return ItemID.Chest;

            int style = 0;
            int alternate = 0;
            TileObjectData.GetTileInfo(tile, ref style, ref alternate);
            int drop = TileLoader.GetItemDropFromTypeAndStyle(tile.TileType, style);
            return drop > 0 ? drop : ItemID.Chest;
        }
    }
}
