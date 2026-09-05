using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Scans nearby chest footprints for paste inventory reach without client Projection UI.</summary>
    internal static class PasteInventoryReach
    {
        /// <summary>
        /// Collects chest tile footprints in the inventory reach ring around <paramref name="targetArea"/>.
        /// </summary>
        public static List<Rectangle> CollectChestFootprints(Rectangle targetArea)
        {
            // --- Walk Main.chest and keep footprints that sit in the reach ring, not inside the paste area.

            var footprints = new List<Rectangle>();
            if (targetArea.IsEmpty)
                return footprints;

            for (int ci = 0; ci < Main.maxChests; ci++)
            {
                Chest chest = Main.chest[ci];
                if (chest is null || chest.x < 0)
                    continue;

                // Derive multitile size from the chest's host tile.
                Tile tile = Main.tile[chest.x, chest.y];
                TileObjectData tileData = TileObjectData.GetTileData(tile);
                int w = tileData?.Width ?? 2;
                int h = tileData?.Height ?? 2;
                Rectangle footprint = new(chest.x, chest.y, w, h);

                if (InventoryManager.IsChestInInventoryReachRing(footprint, targetArea))
                    footprints.Add(footprint);
            }

            return footprints;
        }
    }
}
