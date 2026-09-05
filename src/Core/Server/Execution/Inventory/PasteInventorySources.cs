using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Builds the ordered inventory source list used by paste consumption and manifest accounting.</summary>
    internal static class PasteInventorySources
    {
        /// <summary>
        /// Collects reachable chests and the in-range player inventory in paste priority order.
        /// </summary>
        public static List<Inventory> Collect(PasteExecutionContext execution)
        {
            // --- Rescan chest footprints, assign distance priorities, then append the in-range player fallback.

            var inventories = new List<Inventory>();

            execution.RefreshInventories();
            return CollectFromFootprints(execution.Inventories, execution.TargetArea, execution.Owner, inventories);
        }

        /// <summary>
        /// Collects reachable projection chests and the in-range player inventory in paste priority order.
        /// </summary>
        public static List<Inventory> Collect(Projection projection)
        {
            // --- Same reach rules as execution contexts, using the live ghost's inventory scan.

            var inventories = new List<Inventory>();

            projection.RefreshInventories();
            return CollectFromFootprints(projection.Inventories, projection.TargetArea, projection.Owner, inventories);
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

        /// <summary>Builds chest and optional player inventory entries from footprints around a target area.</summary>
        private static List<Inventory> CollectFromFootprints(
            IReadOnlyList<Rectangle> footprints,
            Rectangle targetArea,
            int owner,
            List<Inventory> inventories)
        {
            // --- Assign priorities, append player fallback, then sort.

            int discoveryIndex = 0;
            foreach (Rectangle footprint in footprints)
            {
                int priority = InventoryManager.ComputeChestPriority(footprint, targetArea);
                inventories.Add(Inventory.FromRectangle(footprint, priority, discoveryIndex));
                discoveryIndex++;
            }

            Player player = Main.player[owner];
            if (InventoryManager.IsPlayerInInventoryRange(player, targetArea))
                inventories.Add(Inventory.FromPlayer(player, Inventory.PlayerPriority, discoveryIndex));

            inventories.Sort(ComparePriority);
            return inventories;
        }
    }
}
