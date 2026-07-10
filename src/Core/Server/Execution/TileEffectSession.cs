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
