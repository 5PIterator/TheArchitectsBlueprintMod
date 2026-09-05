using System.Collections.Generic;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>One payload material line with tool/ammo pairing, totals, and inventory sources.</summary>
    internal sealed class ManifestEntry(
        string mainName,
        int toolItemId,
        int consumableItemId,
        int totalCount,
        int availableCount,
        int missingCount,
        int toolAvailableCount,
        int toolMissingCount,
        IReadOnlyList<ManifestInventorySource> sources)
    {
        public string MainName { get; } = mainName;
        public int ToolItemId { get; } = toolItemId;
        public int ConsumableItemId { get; } = consumableItemId;
        public int ItemId => ConsumableItemId;
        public int TotalCount { get; } = totalCount;
        public int AvailableCount { get; } = availableCount;
        public int MissingCount { get; } = missingCount;
        public int ToolAvailableCount { get; } = toolAvailableCount;
        public int ToolMissingCount { get; } = toolMissingCount;
        public bool HasDistinctTool => ToolItemId != ConsumableItemId;
        public IReadOnlyList<ManifestInventorySource> Sources { get; } = sources;
    }

    /// <summary>Computed bill of materials for a projection paste.</summary>
    internal sealed class Manifest(
        IReadOnlyList<ManifestEntry> entries,
        IReadOnlyList<string> unresolved)
    {
        public IReadOnlyList<ManifestEntry> Entries { get; } = entries;
        public IReadOnlyList<string> Unresolved { get; } = unresolved;
        public bool IsEmpty => Entries.Count == 0 && Unresolved.Count == 0;
        public bool IsSatisfied
        {
            get
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    ManifestEntry entry = Entries[i];
                    if (entry.MissingCount > 0 || entry.ToolMissingCount > 0)
                        return false;
                }

                return Unresolved.Count == 0;
            }
        }
    }
}
