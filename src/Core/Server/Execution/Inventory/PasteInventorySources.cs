using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Builds the ordered inventory source list used by paste consumption and manifest accounting.</summary>
    internal static class PasteInventorySources
    {
        /// <summary>
        /// Collects reachable projection chests and the in-range player inventory in paste priority order.
        /// </summary>
        public static List<Inventory> Collect(Projection projection)
        {
            // --- Rescan chest footprints, assign distance priorities, then append the in-range player fallback.

            var inventories = new List<Inventory>();

            // Refresh chest footprints from live world state.
            projection.RefreshInventories();
            Rectangle targetArea = projection.TargetArea;

            int discoveryIndex = 0;
            foreach (Rectangle footprint in projection.Inventories)
            {
                int priority = InventoryManager.ComputeChestPriority(footprint, targetArea);
                inventories.Add(Inventory.FromRectangle(footprint, priority, discoveryIndex));
                discoveryIndex++;
            }

            // Player inventory is only eligible inside the same reach window used for chests.
            Player player = Main.player[projection.Owner];
            if (InventoryManager.IsPlayerInInventoryRange(player, targetArea))
                inventories.Add(Inventory.FromPlayer(player, Inventory.PlayerPriority, discoveryIndex));

            // Prefer nearer chests first; keep discovery order for equal distances.
            inventories.Sort(ComparePriority);
            return inventories;
        }

        /// <summary>Orders inventory sources by descending priority, then ascending discovery index.</summary>
        public static int ComparePriority(Inventory a, Inventory b)
        {
            // --- Higher priority first; equal priorities keep discovery order.
            int byPriority = b.Priority.CompareTo(a.Priority);
            if (byPriority != 0)
                return byPriority;
            return a.DiscoveryIndex.CompareTo(b.DiscoveryIndex);
        }
    }
}
