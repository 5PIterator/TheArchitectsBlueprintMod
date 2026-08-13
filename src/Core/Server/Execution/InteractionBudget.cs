using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Tracks partial yield uses per tool/consumable pair for one paste operation.</summary>
    internal readonly record struct InteractionBudgetKey(int ToolItemId, int ConsumableItemId);

    /// <summary>
    /// Per-operation yield budget. Yield is the consumable debit rate (<c>AmmoId ?? ItemId</c>);
    /// infinite yield never debits stacks but distinct ammo must still be present.
    /// </summary>
    internal sealed class InteractionBudget
    {
        private readonly Dictionary<InteractionBudgetKey, int> _remainingFreeUses = [];

        /// <summary>True when yield is null or non-positive (no stack debit).</summary>
        public static bool IsInfiniteYield(int? yield) => !yield.HasValue || yield.Value <= 0;

        /// <summary>
        /// Resolves the yield budget for an interaction. Direct tile/wall placement without
        /// base or ammo defaults to 1; explicit null on conversion/tool entries stays infinite.
        /// </summary>
        public static int? GetEffectiveYield(in InteractionEntry entry)
        {
            if (entry.Yield.HasValue)
                return entry.Yield;

            if (IsDirectConsumablePlacement(entry))
                return 1;

            return null;
        }

        public static bool IsInfiniteYield(in InteractionEntry entry) =>
            IsInfiniteYield(GetEffectiveYield(entry));

        /// <summary>True when the entry names ammo that is distinct from the tool item.</summary>
        public static bool HasDistinctAmmo(in InteractionEntry entry)
        {
            // --- Ammo presence is required independently of whether yield debits that ammo.

            if (!entry.ItemId.HasValue || entry.ItemId.Value <= 0)
                return false;

            return entry.AmmoId.HasValue
                && entry.AmmoId.Value > 0
                && entry.AmmoId.Value != entry.ItemId.Value;
        }

        private static bool IsDirectConsumablePlacement(in InteractionEntry entry)
        {
            if (!string.Equals(entry.Layer, "Tile", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entry.Layer, "Wall", StringComparison.OrdinalIgnoreCase))
                return false;

            if (entry.BaseId.HasValue || !string.IsNullOrEmpty(entry.Base))
                return false;

            if (entry.AmmoId.HasValue || !string.IsNullOrEmpty(entry.Ammo))
                return false;

            return true;
        }

        public static InteractionBudgetKey KeyFor(in InteractionEntry entry)
        {
            int toolId = entry.ItemId ?? 0;
            int consumableId = entry.AmmoId ?? toolId;
            return new InteractionBudgetKey(toolId, consumableId);
        }

        /// <summary>
        /// Whether a placement can proceed without needing a new consumable debit.
        /// </summary>
        public bool HasFreeUses(in InteractionEntry entry)
        {
            if (IsInfiniteYield(entry))
                return true;

            InteractionBudgetKey key = KeyFor(entry);
            return _remainingFreeUses.TryGetValue(key, out int remaining) && remaining > 0;
        }

        /// <summary>
        /// Whether the next placement will debit a consumable stack (vs using a free yield slot).
        /// Infinite yield never debits; distinct ammo may still need to be present.
        /// </summary>
        public bool PeekNeedsConsumableStack(in InteractionEntry entry) =>
            !IsInfiniteYield(entry) && !HasFreeUses(entry);

        /// <summary>
        /// Records a successful placement against the yield budget. Returns true when a stack was consumed.
        /// </summary>
        public bool Spend(in InteractionEntry entry)
        {
            if (IsInfiniteYield(entry))
                return false;

            int yield = GetEffectiveYield(entry)!.Value;
            InteractionBudgetKey key = KeyFor(entry);
            if (_remainingFreeUses.TryGetValue(key, out int remaining) && remaining > 0)
            {
                _remainingFreeUses[key] = remaining - 1;
                return false;
            }

            _remainingFreeUses[key] = yield - 1;
            return true;
        }

        /// <summary>Restores budget state when reservation fails after Spend was called.</summary>
        public void RollbackSpend(in InteractionEntry entry, bool consumedStack)
        {
            if (IsInfiniteYield(entry))
                return;

            int yield = GetEffectiveYield(entry)!.Value;
            InteractionBudgetKey key = KeyFor(entry);
            if (consumedStack)
            {
                if (_remainingFreeUses.TryGetValue(key, out int remaining))
                {
                    if (remaining >= yield - 1)
                        _remainingFreeUses.Remove(key);
                    else
                        _remainingFreeUses[key] = remaining + 1;
                }
                return;
            }

            if (_remainingFreeUses.TryGetValue(key, out int free) && free < yield - 1)
                _remainingFreeUses[key] = free + 1;
        }

        public int GetRemainingFreeUses(in InteractionEntry entry)
        {
            if (IsInfiniteYield(entry))
                return int.MaxValue;

            InteractionBudgetKey key = KeyFor(entry);
            return _remainingFreeUses.TryGetValue(key, out int remaining) ? remaining : 0;
        }
    }

    /// <summary>Result of reserving inventory for an interaction placement.</summary>
    internal readonly record struct InteractionReservation(
        int ToolItemId,
        int ConsumableItemId,
        int PlaceStyle,
        Item? ConsumableItem,
        Vector2 Origin,
        bool FromChest,
        Chest? SourceChest,
        bool ConsumedStack);

    /// <summary>
    /// Tracks actual inventory debits and yield-budget spends for one in-flight place arrow.
    /// Arrow sprite uses the consumable/ammo when present; refunds use debit ownership separately.
    /// </summary>
    internal sealed class PlacementReservationLedger
    {
        private readonly List<(InteractionEntry Entry, bool ConsumedStack)> _spends = [];

        public PlacementReservationLedger(int visualItemType = 0)
        {
            VisualItemType = visualItemType;
        }

        public int VisualItemType { get; private set; }

        public int DebitedItemType { get; private set; }

        public int DebitedStackCount { get; private set; }

        public int FreeYieldSpendCount { get; private set; }

        public int SpendCount => _spends.Count;

        /// <summary>Records one interaction reservation against this arrow's ownership ledger.</summary>
        public void Record(InteractionEntry entry, in InteractionReservation reservation)
        {
            // --- Prefer ammo/consumable for the arrow sprite; fall back to the tool when no consumable exists.
            if (VisualItemType <= 0)
            {
                VisualItemType = reservation.ConsumableItemId > 0
                    ? reservation.ConsumableItemId
                    : reservation.ToolItemId;
            }

            _spends.Add((entry, reservation.ConsumedStack));

            if (reservation.ConsumedStack)
            {
                DebitedItemType = reservation.ConsumableItemId;
                DebitedStackCount++;
                return;
            }

            if (!InteractionBudget.IsInfiniteYield(entry))
                FreeYieldSpendCount++;
        }

        /// <summary>Records a fallback inventory debit that did not use interaction yield budget.</summary>
        public void RecordFallbackDebit(int itemType, int stack = 1)
        {
            // --- Fallback paths always remove stacks and have no budget entry to roll back.
            if (itemType <= 0 || stack <= 0)
                return;

            if (VisualItemType <= 0)
                VisualItemType = itemType;

            DebitedItemType = itemType;
            DebitedStackCount += stack;
        }

        /// <summary>Restores yield-budget state in reverse spend order without restoring inventory stacks.</summary>
        public void RollbackBudget(InteractionBudget? budget)
        {
            // --- Undo spends newest-first so interleaved free/stack accounting stays consistent.
            if (budget is null || _spends.Count == 0)
                return;

            for (int i = _spends.Count - 1; i >= 0; i--)
            {
                (InteractionEntry entry, bool consumedStack) = _spends[i];
                budget.RollbackSpend(entry, consumedStack);
            }
        }
    }
}
