using System.Collections.Generic;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>One payload material line with total, available, missing, and inventory source context.</summary>
    internal sealed class ManifestEntry(
        int itemId,
        int totalCount,
        int availableCount,
        int missingCount,
        IReadOnlyList<ManifestInventorySource> sources)
    {
        public int ItemId { get; } = itemId;
        public int TotalCount { get; } = totalCount;
        public int AvailableCount { get; } = availableCount;
        public int MissingCount { get; } = missingCount;
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
                    if (Entries[i].MissingCount > 0)
                        return false;
                }

                return Unresolved.Count == 0;
            }
        }
    }
}
