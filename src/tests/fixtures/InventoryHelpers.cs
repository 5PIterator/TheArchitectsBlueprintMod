#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Inventory give/remove helpers for paste Case material control.</summary>
    internal static class InventoryHelpers
    {
        /// <summary>Gives the player stacks of an item type, filling existing stacks then empty slots.</summary>
        public static int GiveItem(Player player, int itemType, int count)
        {
            // --- Top up matching stacks, then occupy empty slots up to maxStack.

            ArgumentNullException.ThrowIfNull(player);
            if (itemType <= 0 || count <= 0)
                return 0;

            Item probe = new();
            probe.SetDefaults(itemType);
            int maxStack = Math.Max(1, probe.maxStack);
            int given = 0;

            for (int i = 0; i < player.inventory.Length && given < count; i++)
            {
                Item slot = player.inventory[i];
                if (slot is null || slot.IsAir || slot.type != itemType || slot.stack >= maxStack)
                    continue;

                int add = Math.Min(maxStack - slot.stack, count - given);
                slot.stack += add;
                given += add;
            }

            for (int i = 0; i < player.inventory.Length && given < count; i++)
            {
                Item slot = player.inventory[i];
                if (slot is not null && !slot.IsAir)
                    continue;

                player.inventory[i] ??= new Item();
                player.inventory[i].SetDefaults(itemType);
                int add = Math.Min(maxStack, count - given);
                player.inventory[i].stack = add;
                given += add;
            }

            return given;
        }

        /// <summary>Removes up to <paramref name="count"/> of an item type from the player's inventory.</summary>
        public static int RemoveItem(Player player, int itemType, int count)
        {
            // --- Walk inventory slots and shrink/clear stacks of the matching type.

            ArgumentNullException.ThrowIfNull(player);
            if (itemType <= 0 || count <= 0)
                return 0;

            int removed = 0;
            for (int i = 0; i < player.inventory.Length && removed < count; i++)
            {
                Item slot = player.inventory[i];
                if (slot is null || slot.IsAir || slot.type != itemType)
                    continue;

                int take = Math.Min(slot.stack, count - removed);
                slot.stack -= take;
                removed += take;
                if (slot.stack <= 0)
                    slot.TurnToAir();
            }

            return removed;
        }

        /// <summary>Counts how many of an item type the player currently holds.</summary>
        public static int CountItem(Player player, int itemType)
        {
            // --- Sum stacks of the matching type across the inventory.
            ArgumentNullException.ThrowIfNull(player);
            if (itemType <= 0)
                return 0;

            int total = 0;
            for (int i = 0; i < player.inventory.Length; i++)
            {
                Item slot = player.inventory[i];
                if (slot is not null && !slot.IsAir && slot.type == itemType)
                    total += slot.stack;
            }

            return total;
        }

        /// <summary>
        /// Builds merged item id/count lines from projection manifest demand for chest stocking.
        /// Uses total demand counts so the chest alone can satisfy the paste.
        /// </summary>
        public static bool TryBuildRequiredItemLines(
            Projection projection,
            out List<(int ItemId, int Count)> lines,
            out string? error)
        {
            // --- Manifest TotalCount (+ one tool when distinct) merged by item id.
            lines = [];
            error = null;
            ArgumentNullException.ThrowIfNull(projection);

            if (!projection.HasPayload)
            {
                error = "Cannot build required item lines: projection has no payload.";
                return false;
            }

            Manifest manifest = ManifestBuilder.Build(projection);
            var merged = new Dictionary<int, int>();

            IReadOnlyList<ManifestEntry> entries = manifest.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                ManifestEntry entry = entries[i];
                int consumableNeed = Math.Max(entry.TotalCount, 0);
                if (consumableNeed > 0 && entry.ConsumableItemId > 0)
                {
                    merged.TryGetValue(entry.ConsumableItemId, out int existing);
                    merged[entry.ConsumableItemId] = existing + consumableNeed;
                }

                if (entry.HasDistinctTool && entry.ToolItemId > 0)
                {
                    merged.TryGetValue(entry.ToolItemId, out int existingTool);
                    merged[entry.ToolItemId] = Math.Max(existingTool, 1);
                }
            }

            foreach (KeyValuePair<int, int> pair in merged)
                lines.Add((pair.Key, pair.Value));

            return true;
        }

        /// <summary>
        /// Builds the projection paste manifest and gives every missing consumable (and tool) so the paste can succeed.
        /// </summary>
        public static bool GiveRequiredItems(
            Player player,
            Projection projection,
            ITestCaseReporter? reporter = null,
            string? caseTag = null)
        {
            // --- ManifestBuilder demand → GiveItem for each missing consumable/tool line.

            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(projection);

            if (!projection.HasPayload)
            {
                reporter?.Fail(caseTag ?? "inventory", "Cannot give required items: projection has no payload.");
                return false;
            }

            Manifest manifest = ManifestBuilder.Build(projection);
            if (manifest.Unresolved.Count > 0)
            {
                reporter?.Info(
                    $"{caseTag ?? "inventory"}: manifest has {manifest.Unresolved.Count} unresolved demand line(s); giving resolved entries only.");
            }

            int linesGiven = 0;
            IReadOnlyList<ManifestEntry> entries = manifest.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                ManifestEntry entry = entries[i];
                int consumableNeed = Math.Max(entry.MissingCount, 0);
                if (consumableNeed > 0 && entry.ConsumableItemId > 0)
                {
                    int given = GiveItem(player, entry.ConsumableItemId, consumableNeed);
                    if (given < consumableNeed)
                    {
                        reporter?.Fail(
                            caseTag ?? "inventory",
                            $"Could not give {consumableNeed} of item {entry.ConsumableItemId} (gave {given}; inventory full?).");
                        return false;
                    }

                    linesGiven++;
                }

                if (entry.HasDistinctTool && entry.ToolMissingCount > 0 && entry.ToolItemId > 0)
                {
                    int givenTool = GiveItem(player, entry.ToolItemId, entry.ToolMissingCount);
                    if (givenTool < entry.ToolMissingCount)
                    {
                        reporter?.Fail(
                            caseTag ?? "inventory",
                            $"Could not give tool item {entry.ToolItemId} (gave {givenTool}).");
                        return false;
                    }

                    linesGiven++;
                }
            }

            reporter?.Info($"{caseTag ?? "inventory"}: gave required materials for {linesGiven} manifest line(s).");
            return true;
        }

        /// <summary>
        /// Builds the projection paste manifest and strips those consumables/tools from the player so paste should fail.
        /// </summary>
        public static bool RemoveRequiredItems(
            Player player,
            Projection projection,
            ITestCaseReporter? reporter = null,
            string? caseTag = null)
        {
            // --- ManifestBuilder demand → RemoveItem for each consumable/tool type (all held stacks).

            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(projection);

            if (!projection.HasPayload)
            {
                reporter?.Fail(caseTag ?? "inventory", "Cannot remove required items: projection has no payload.");
                return false;
            }

            Manifest manifest = ManifestBuilder.Build(projection);
            int linesCleared = 0;
            IReadOnlyList<ManifestEntry> entries = manifest.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                ManifestEntry entry = entries[i];
                if (entry.ConsumableItemId > 0)
                {
                    int held = CountItem(player, entry.ConsumableItemId);
                    if (held > 0)
                    {
                        RemoveItem(player, entry.ConsumableItemId, held);
                        linesCleared++;
                    }
                }

                if (entry.HasDistinctTool && entry.ToolItemId > 0)
                {
                    int heldTool = CountItem(player, entry.ToolItemId);
                    if (heldTool > 0)
                    {
                        RemoveItem(player, entry.ToolItemId, heldTool);
                        linesCleared++;
                    }
                }
            }

            // Recompute: success means the paste is no longer fully satisfiable from the player.
            Manifest after = ManifestBuilder.Build(projection);
            if (after.IsSatisfied && after.Entries.Count > 0)
            {
                reporter?.Fail(
                    caseTag ?? "inventory",
                    "RemoveRequiredItems left the manifest satisfied (items may come from nearby chests).");
                return false;
            }

            reporter?.Info($"{caseTag ?? "inventory"}: stripped required materials from {linesCleared} inventory line(s).");
            return true;
        }
    }
}
#endif
