using System;
using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Selects paste interactions with finite yield preferred over infinite tools.
    /// </summary>
    internal static class InteractionSelection
    {
        /// <summary>
        /// Picks among candidates preferring ledger-met finite rows, then infinite, then unmet preferred.
        /// </summary>
        public static bool TrySelectFiniteFirst(
            IEnumerable<InteractionEntry> candidates,
            Func<InteractionEntry, bool>? isMet,
            out InteractionEntry? chosen,
            out InteractionEntry? preferred,
            out int candidateCount,
            out bool met)
        {
            // --- Finite shopping-list paths beat infinite tools; same-priority ties keep the last entry.

            InteractionEntry? preferredFinite = null;
            InteractionEntry? preferredInfinite = null;
            InteractionEntry? firstMetFinite = null;
            InteractionEntry? firstMetInfinite = null;
            InteractionEntry? lastMetFiniteInClass = null;
            InteractionEntry? lastMetInfiniteInClass = null;
            candidateCount = 0;

            foreach (InteractionEntry entry in candidates)
            {
                // Skip rows without a usable tool item id.
                if (!IsRecordable(entry))
                    continue;

                candidateCount++;
                bool infinite = InteractionBudget.IsInfiniteYield(entry);

                // Track preferred unmet candidates with finite first (seeds before Staff).
                if (infinite)
                    preferredInfinite ??= entry;
                else
                    preferredFinite ??= entry;

                // When a met predicate is supplied, skip entries the ledger cannot supply.
                if (isMet is not null && !isMet(entry))
                    continue;

                // First met in priority order within the finite/infinite class.
                if (infinite)
                    firstMetInfinite ??= entry;
                else
                    firstMetFinite ??= entry;

                // Among same priority class, keep the last (multipurpose after specialized).
                InteractionEntry? classPreferred = infinite ? preferredInfinite : preferredFinite;
                if (classPreferred is null
                    || InteractionPriorityComparer.Instance.Compare(entry, classPreferred) != 0)
                    continue;

                if (infinite)
                    lastMetInfiniteInClass = entry;
                else
                    lastMetFiniteInClass = entry;
            }

            preferred = preferredFinite ?? preferredInfinite;
            chosen = lastMetFiniteInClass
                ?? firstMetFinite
                ?? lastMetInfiniteInClass
                ?? firstMetInfinite
                ?? preferred;
            met = firstMetFinite is not null || firstMetInfinite is not null;
            return chosen is not null;
        }

        /// <summary>True when the entry has a positive tool item id suitable for selection.</summary>
        public static bool IsRecordable(InteractionEntry entry) =>
            entry.ItemId.HasValue && entry.ItemId.Value > 0;
    }
}
