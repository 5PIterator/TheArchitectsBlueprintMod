using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core
{
    internal readonly struct ArrowFlightProfile
    {
        public int TimeoutTicks { get; init; }
        public int ManeuverTicks { get; init; }
        public float MinSpeed { get; init; }
        public float MaxSpeed { get; init; }
    }

    internal struct PasteArrowSlot
    {
        public int ArrowId;
        public int MutationIndex;
        public Vector2 Destination;
        public int ProjectileIndex;
        public uint ArriveByUpdate;
        public int TimeoutTicks;
        public int ManeuverTicks;
        public float MinSpeed;
        public float MaxSpeed;
        public bool ArrivedAtDestination;
        public bool ReadyToResolve;
        public Point DropTile;
        public int ItemType;
        public bool IsHeadless => ProjectileIndex < 0;
    }

    /// <summary>Per-operation queue and in-flight projectile tracking for tile effects.</summary>
    internal sealed class TileEffectSession
    {
        private static int _nextOperationId = 1;

        private int _nextMutationId = 1;

        private readonly List<TileMutation> _mutations = [];
        private readonly List<PasteArrowSlot> _activeArrows = [];
        private readonly Dictionary<int, int> _arrowSlotById = new();
        private readonly Dictionary<int, LiquidPoolCell[]> _liquidPools = new();
        private readonly Dictionary<int, PlacementReservationLedger> _placementLedgers = new();
        private readonly List<int> _resolveScratch = [];

        public TileEffectSession(int operationId, Func<PasteContext> contextFactory)
        {
            OperationId = operationId;
            ContextFactory = contextFactory;
        }

        public int OperationId { get; }

        public Queue<TileMutation> Pending { get; } = new();

        public Func<PasteContext> ContextFactory { get; }

        public int PendingMutationCount => Pending.Count;

        public int PendingImpactCount => _activeArrows.Count;

        public bool IsIdle => Pending.Count == 0 && _activeArrows.Count == 0;

        /// <summary>Returns true when a frame placement mutation for the tile is queued or still in flight.</summary>
        public bool HasPendingOrInFlightPlacementAt(Point world, ushort tileType)
        {
            foreach (TileMutation mutation in Pending)
            {
                if (IsFramePlacementKind(mutation.Kind)
                    && mutation.World == world
                    && mutation.Type == tileType)
                    return true;
            }

            for (int i = 0; i < _mutations.Count; i++)
            {
                TileMutation mutation = _mutations[i];
                if (!IsFramePlacementKind(mutation.Kind)
                    || mutation.World != world
                    || mutation.Type != tileType
                    || !IsMutationInFlight(i))
                    continue;

                return true;
            }

            return false;
        }

        public bool CanLaunchArrow() => _activeArrows.Count < TileExecutor.GetMaxConcurrentArrows();

        internal List<int> ResolveScratch => _resolveScratch;

        /// <summary>Allocates a compact operation id safe to store in projectile ai slots (float-exact).</summary>
        internal static int AllocateOperationId() => _nextOperationId++;

        internal static void ResetOperationIdSequence() => _nextOperationId = 1;

        internal static int ReadProjectileOperationId(Projectile projectile) => (int)projectile.ai[1];

        internal static int ReadProjectileMutationId(Projectile projectile) => (int)projectile.ai[2];

        public int NextMutationId() => _nextMutationId++;

        public void Enqueue(TileMutation mutation)
        {
            Pending.Enqueue(mutation);
            TileExecutorLog.Registered(in mutation);
        }

        /// <summary>Attaches the pool cells written when a liquid placement mutation arrives.</summary>
        public void AttachLiquidPool(int mutationId, LiquidPoolCell[] cells)
        {
            // --- Store pool payload beside the single SetLiquid mutation that owns the arrow.
            if (cells is null || cells.Length == 0)
                return;

            _liquidPools[mutationId] = cells;
        }

        /// <summary>Returns the pool cells for a liquid placement mutation when one was attached.</summary>
        public bool TryGetLiquidPool(int mutationId, out LiquidPoolCell[] cells) =>
            _liquidPools.TryGetValue(mutationId, out cells!);

        /// <summary>Drops pool payload after apply or abort settlement.</summary>
        public void ClearLiquidPool(int mutationId) => _liquidPools.Remove(mutationId);

        /// <summary>Attaches ownership metadata for an in-flight place arrow.</summary>
        public void AttachPlacementLedger(int mutationId, PlacementReservationLedger ledger)
        {
            // --- Keep refundable debits/budget spends off the visual mutation ItemType/ItemStack fields.
            if (ledger is null)
                return;

            _placementLedgers[mutationId] = ledger;
        }

        /// <summary>Takes ownership ledger for settlement, removing it from the session.</summary>
        public bool TryTakePlacementLedger(int mutationId, out PlacementReservationLedger ledger) =>
            _placementLedgers.Remove(mutationId, out ledger!);

        /// <summary>Discards ownership ledger after a successful placement commits.</summary>
        public void ClearPlacementLedger(int mutationId) => _placementLedgers.Remove(mutationId);

        public bool TryDequeue(out TileMutation mutation)
        {
            if (Pending.Count == 0)
            {
                mutation = default;
                return false;
            }

            mutation = Pending.Dequeue();
            return true;
        }

        public int StoreMutation(TileMutation mutation)
        {
            int index = _mutations.Count;
            _mutations.Add(mutation);
            return index;
        }

        public TileMutation GetMutation(int mutationIndex) => _mutations[mutationIndex];

        public void RegisterArrow(in PasteArrowSlot slot)
        {
            int slotIndex = _activeArrows.Count;
            _activeArrows.Add(slot);
            _arrowSlotById[slot.ArrowId] = slotIndex;
        }

        public int ActiveArrowCount => _activeArrows.Count;

        public void CopyActiveArrowIds(List<int> destination)
        {
            destination.Clear();
            for (int i = 0; i < _activeArrows.Count; i++)
                destination.Add(_activeArrows[i].ArrowId);
        }

        public void CollectArrowsToResolve(uint now, List<int> resolveIds)
        {
            resolveIds.Clear();
            for (int i = 0; i < _activeArrows.Count; i++)
            {
                PasteArrowSlot slot = _activeArrows[i];
                if (slot.ReadyToResolve)
                {
                    resolveIds.Add(slot.ArrowId);
                    continue;
                }

                if (slot.IsHeadless && now >= slot.ArriveByUpdate)
                {
                    slot.ArrivedAtDestination = true;
                    slot.ReadyToResolve = true;
                    _activeArrows[i] = slot;
                    TileMutation mutation = GetMutation(slot.MutationIndex);
                    TileExecutorLog.ArrowState(in mutation, state: "Arrive", detail: "synthetic");
                    resolveIds.Add(slot.ArrowId);
                    continue;
                }

                if (!slot.IsHeadless)
                {
                    TileMutation mutation = GetMutation(slot.MutationIndex);
                    if (!TileExecutor.IsTrackedBoundArrowActive(in slot, in mutation))
                    {
                        slot.ArrivedAtDestination = false;
                        slot.ReadyToResolve = true;
                        _activeArrows[i] = slot;
                        TileExecutorLog.ArrowState(in mutation, state: "Arrive", detail: "orphaned");
                        resolveIds.Add(slot.ArrowId);
                    }
                }
            }
        }

        public bool TryGetArrowSlot(int arrowId, out int slotIndex, out PasteArrowSlot slot)
        {
            if (!_arrowSlotById.TryGetValue(arrowId, out slotIndex))
            {
                slot = default;
                return false;
            }

            slot = _activeArrows[slotIndex];
            return true;
        }

        public bool TryGetArrowDestination(int arrowId, out Vector2 destination)
        {
            if (!TryGetArrowSlot(arrowId, out _, out PasteArrowSlot slot))
            {
                destination = default;
                return false;
            }

            destination = slot.Destination;
            return destination != Vector2.Zero;
        }

        public bool TryGetArrowItemType(int arrowId, out int itemType)
        {
            if (!TryGetArrowSlot(arrowId, out _, out PasteArrowSlot slot))
            {
                itemType = 0;
                return false;
            }

            itemType = slot.ItemType > 0 ? slot.ItemType : GetMutation(slot.MutationIndex).ItemType;
            return itemType > 0;
        }

        public bool TryGetArrowFlightProfile(int arrowId, out ArrowFlightProfile profile)
        {
            if (!TryGetArrowSlot(arrowId, out _, out PasteArrowSlot slot))
            {
                profile = default;
                return false;
            }

            profile = new ArrowFlightProfile
            {
                TimeoutTicks = slot.TimeoutTicks,
                ManeuverTicks = slot.ManeuverTicks,
                MinSpeed = slot.MinSpeed,
                MaxSpeed = slot.MaxSpeed,
            };
            return true;
        }

        public bool TryMarkArrowArrived(int arrowId, bool arrivedAtDestination, Point dropTile)
        {
            if (!TryGetArrowSlot(arrowId, out int slotIndex, out PasteArrowSlot slot))
                return false;

            if (dropTile != default)
                slot.DropTile = dropTile;
            slot.ArrivedAtDestination = slot.ArrivedAtDestination || arrivedAtDestination;
            slot.ReadyToResolve = true;
            _activeArrows[slotIndex] = slot;

            TileMutation mutation = GetMutation(slot.MutationIndex);
            TileExecutorLog.ArrowState(
                in mutation,
                state: "Arrive",
                detail: $"arrived={(arrivedAtDestination ? "yes" : "no")} mode={(slot.IsHeadless ? "headless" : "bound")}");
            return true;
        }

        public bool TryTakeArrow(int arrowId, out PasteArrowSlot slot, out TileMutation mutation)
        {
            if (!TryGetArrowSlot(arrowId, out int slotIndex, out slot))
            {
                mutation = default;
                return false;
            }

            mutation = GetMutation(slot.MutationIndex);
            RemoveSlotAt(slotIndex, arrowId);
            return true;
        }

        public void Clear()
        {
            Pending.Clear();
            _mutations.Clear();
            _activeArrows.Clear();
            _arrowSlotById.Clear();
            _resolveScratch.Clear();
        }

        private static bool IsFramePlacementKind(TileMutationKind kind) =>
            kind is TileMutationKind.PlaceObject or TileMutationKind.PlaceTile;

        private bool IsMutationInFlight(int mutationIndex)
        {
            for (int i = 0; i < _activeArrows.Count; i++)
            {
                if (_activeArrows[i].MutationIndex == mutationIndex)
                    return true;
            }

            return false;
        }

        private void RemoveSlotAt(int slotIndex, int arrowId)
        {
            int lastIndex = _activeArrows.Count - 1;
            if (slotIndex != lastIndex)
            {
                PasteArrowSlot moved = _activeArrows[lastIndex];
                _activeArrows[slotIndex] = moved;
                _arrowSlotById[moved.ArrowId] = slotIndex;
            }

            _activeArrows.RemoveAt(lastIndex);
            _arrowSlotById.Remove(arrowId);
        }
    }
}
