using System;
using System.Collections;
using System.Collections.Generic;
using ArchitectsStencilMod.Core.Server;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core.TileMapping;
using ArchitectsStencilMod.Debug;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Global manager for all active paste operations.
    /// Owns the tick/update loop; per-projection state lives in <see cref="PastingOperation"/>.
    /// </summary>
    public sealed class PastingSystem : ModSystem
    {
        private readonly List<PastingOperation> _operations = [];
        private int _nextPasteOverlayInstanceId;

        internal IReadOnlyList<PastingOperation> ActiveOperations => _operations;

        internal bool StartPaste(Projection projection, ClientPreferences preferences, out PasteRejectReason rejectReason)
        {
            // --- Ignore invalid or already-running projections, then prepare storage and register a new operation.

            if (!ValidateProjection(projection, out rejectReason))
                return false;

            var config = ModContent.GetInstance<PastingConfig>();
            var (tileIds, wallIds) = CollectTileAndWallIds(projection);
            for (int i = 0; i < _operations.Count; i++)
            {
                var (opTiles, opWalls) = CollectTileAndWallIds(_operations[i].Projection);
                tileIds.UnionWith(opTiles);
                wallIds.UnionWith(opWalls);
            }

            ItrStorage.PrepareForPaste(tileIds, wallIds);

            var op = new PastingOperation(projection, config, preferences)
            {
                OverlayId = ++_nextPasteOverlayInstanceId
            };
            if (config.CheatSweep)
                PasteTaskMapDebug.WriteTaskMap(projection, op.ApplySourceClear, op.Pipeline, op.ClearTasks, op.PlaceTasks);

            // Task queues for normal paste are built incrementally in <see cref="PastingOperation.Step"/> (preparation phase).
            // No work after preparation (e.g. world already matches blueprint): remove on first tick when <see cref="PastingOperation.IsCompleted"/>.
            projection.Lock();
            _operations.Add(op);
            return true;
        }

        /// <summary>
        /// Single-player: starts a paste on <paramref name="projection"/> and a <see cref="DebugStepper"/> session that
        /// advances it via <see cref="DebugStepOperation"/>. If a paste is already active for this projection, only the
        /// stepper session is (re)attached.
        /// </summary>
        public void BeginDebugStepSession(Player player, Projection projection)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(projection);

            if (player.whoAmI != Main.myPlayer)
                return;

            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                Main.NewText("Debug: stepped paste is single-player only.", Color.OrangeRed);
                return;
            }

            if (!ValidateProjectionPayload(projection))
            {
                Main.NewText("Debug: projection is not ready to paste.", Color.OrangeRed);
                return;
            }

            if (!HasActiveOperationFor(projection))
            {
                if (!StartPaste(projection, ClientPreferences.FromLocalConfigs(), out var reject))
                {
                    Main.NewText($"Paste rejected: {reject}", Color.OrangeRed);
                    return;
                }
            }

            IEnumerator PasteStepEnumerator()
            {
                while (DebugStepOperation(projection))
                    yield return null;
            }

            void OnCancel() => StopPaste(projection);
            DebugStepper.StartSession(player, PasteStepEnumerator(), onComplete: null, onCancel: OnCancel, log: _ => { });
        }

        /// <summary>
        /// Stops the paste operation for the given projection, if any.
        /// </summary>
        public void StopPaste(Projection projection)
        {
            // --- Find the matching operation and terminate it through the shared teardown path.

            ArgumentNullException.ThrowIfNull(projection);

            for (int i = _operations.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_operations[i].Projection, projection))
                {
                    RemoveOperationAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// Performs one tick of all active paste operations.
        /// Operations whose owner holds the debug stick are skipped (debug stick drives them manually).
        /// </summary>
        /// <returns>True if at least one operation ran <see cref="PastingOperation.Step"/> or <see cref="PastingOperation.Sweep"/> this tick.</returns>
        internal bool ProcessOperations()
        {
            // --- Tick each operation unless delayed or debug-driven, and remove operations that are no longer valid.

            if (Main.gameMenu || _operations.Count == 0)
            {
                return false;
            }

            bool anyStepped = false;
            for (int i = _operations.Count - 1; i >= 0; i--)
            {
                var op = _operations[i];
                if (DebugStepper.IsHoldingDebugStick(op.Context.Player))
                {
                    continue;
                }

                if (op.Projection is null || !op.Projection.IsDefined)
                {
                    ASMLog.Debug("Paste ended: projection became invalid during update", "Paste");
                    RemoveOperationAt(i);
                    continue;
                }

                if (op.IsCompleted)
                {
                    ASMLog.Info($"Complete (cheatSweep={op.Context.CheatSweep})", "Paste");
                    ASMLog.Debug("Paste ended: operation completed during update", "Paste");
                    RemoveOperationAt(i);
                    continue;
                }

                if (!op.Context.CheatSweep && Environment.TickCount64 < op.NextStepTime)
                {
                    continue;
                }

                // --- Refresh slots and player reference only when stepping, not every frame while delayed.
                op.UpdateContext();
                bool keep = op.Context.CheatSweep ? op.Sweep() : op.Step();
                anyStepped = true;
                if (!keep)
                {
                    ASMLog.Debug("Paste ended: step returned no more work", "Paste");
                    RemoveOperationAt(i);
                }
            }

            return anyStepped;
        }

        /// <summary>
        /// Removes an operation via <see cref="PastingOperation.Teardown"/>, then drops storage when none remain.
        /// </summary>
        private void RemoveOperationAt(int index)
        {
            _operations[index].Teardown();
            _operations.RemoveAt(index);
            if (_operations.Count == 0)
                ItrStorage.Release();
        }

        /// <inheritdoc />
        public override void PostUpdatePlayers()
        {
            ProcessOperations();
            PasteOverlayNetworking.BroadcastPasteOverlaysToRemoteClients(this);
        }

        /// <inheritdoc />
        public override void OnWorldUnload()
        {
            for (int i = 0; i < _operations.Count; i++)
                _operations[i].Teardown();

            _operations.Clear();
            ItrStorage.Release();
            PasteOverlayNetworking.ResetRemoteOverlayBroadcastState();
        }

        /// <summary>
        /// True when <paramref name="projection"/> is non-null, has bounds, and carries source tile data.
        /// </summary>
        private static bool ValidateProjectionPayload(Projection projection) =>
            projection is { IsDefined: true, HasPayload: true };

        /// <summary>
        /// Validates that a paste may start for <paramref name="projection"/> (payload, storage, limits, duplicates).
        /// </summary>
        private bool ValidateProjection(Projection projection, out PasteRejectReason rejectReason)
        {
            rejectReason = PasteRejectReason.None;
            ArgumentNullException.ThrowIfNull(projection);

            if (!ValidateProjectionPayload(projection))
            {
                rejectReason = PasteRejectReason.InvalidProjection;
                return false;
            }

            if (!ItrStorage.CanLoadPasteInteractionMaps())
            {
                rejectReason = PasteRejectReason.InvalidProjection;
                return false;
            }

            var config = ModContent.GetInstance<PastingConfig>();
            if (projection.Bounds.Width > config.MaxPasteWidth || projection.Bounds.Height > config.MaxPasteHeight)
            {
                rejectReason = PasteRejectReason.AreaLimit;
                return false;
            }

            if (projection.SourceTiles.Count > config.VolumeLimit)
            {
                rejectReason = PasteRejectReason.VolumeLimit;
                return false;
            }

            if (GetActiveOperationCountForPlayer(projection.OwnerPlayerIndex) >= config.PlayerOperationLimit)
            {
                rejectReason = PasteRejectReason.PlayerOperationLimit;
                return false;
            }

            if (GetActiveOperationCount() >= config.ServerOperationLimit)
            {
                rejectReason = PasteRejectReason.ServerOperationLimit;
                return false;
            }

            for (int i = 0; i < _operations.Count; i++)
            {
                if (ReferenceEquals(_operations[i].Projection, projection))
                {
                    rejectReason = PasteRejectReason.AlreadyActive;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Collects unique tile and wall IDs from the projection source payload.
        /// </summary>
        private static (HashSet<int> TileIds, HashSet<int> WallIds) CollectTileAndWallIds(Projection projection)
        {
            // --- Walk source payload tiles and gather the material IDs needed by interaction-map loading.

            var tileIds = new HashSet<int>();
            var wallIds = new HashSet<int>();

            if (projection is null || !projection.HasPayload)
                return (tileIds, wallIds);

            for (int i = 0; i < projection.SourceTiles.Count; i++)
            {
                var rel = projection.SourceTiles[i].Relative;
                if (!projection.TryGetPayloadTile(rel, out var tile))
                    continue;

                if (tile.HasTile)
                    tileIds.Add(tile.TileType);
                if (tile.WallType != WallID.None)
                    wallIds.Add(tile.WallType);
            }

            return (tileIds, wallIds);
        }

        /// <summary>
        /// Returns true if there is an active paste operation for the given projection.
        /// </summary>
        internal bool HasActiveOperationFor(Projection projection)
        {
            if (projection is null) return false;
            for (int i = 0; i < _operations.Count; i++)
            {
                if (ReferenceEquals(_operations[i].Projection, projection))
                    return true;
            }
            return false;
        }

        internal int GetActiveOperationCount()
        {
            return _operations.Count;
        }

        internal int GetActiveOperationCountForPlayer(int ownerPlayerIndex)
        {
            int count = 0;
            for (int i = 0; i < _operations.Count; i++)
            {
                if (_operations[i].Projection.OwnerPlayerIndex == ownerPlayerIndex)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Stops the active paste whose projection matches <paramref name="ownerPlayerIndex"/> and <paramref name="correlationId"/>.
        /// Used when a paste was started with a non-zero correlation id (e.g. remote request tracking).
        /// </summary>
        internal bool TryStopPasteByCorrelation(int ownerPlayerIndex, int correlationId)
        {
            if (correlationId <= 0)
            {
                return false;
            }

            for (int i = _operations.Count - 1; i >= 0; i--)
            {
                Projection projection = _operations[i].Projection;
                if (projection.OwnerPlayerIndex == ownerPlayerIndex && projection.Id == correlationId)
                {
                    RemoveOperationAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the given player has an active paste operation, with <paramref name="needsItems"/> indicating item consumption.
        /// </summary>
        internal bool TryGetActivePasteForPlayer(Player player, out bool needsItems)
        {
            needsItems = false;
            if (player is null) return false;

            for (int i = 0; i < _operations.Count; i++)
            {
                if (_operations[i].Projection.OwnerPlayerIndex == player.whoAmI)
                {
                    needsItems = _operations[i].Context.NeedsItems;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Steps the operation for the given projection once. For debug mode (debug stick drives execution).
        /// Returns true if the operation has more work; false when done or not found.
        /// </summary>
        internal bool DebugStepOperation(Projection projection)
        {
            // --- Drive one manual step for the projection and remove the operation when it finishes.

            ArgumentNullException.ThrowIfNull(projection);

            for (int i = 0; i < _operations.Count; i++)
            {
                if (!ReferenceEquals(_operations[i].Projection, projection))
                    continue;

                var op = _operations[i];
                if (op.Projection is null || !op.Projection.IsDefined)
                {
                    ASMLog.Debug("Paste ended: projection became invalid during manual step", "Paste");
                    RemoveOperationAt(i);
                    return false;
                }

                if (op.IsCompleted)
                {
                    ASMLog.Info($"Complete (cheatSweep={op.Context.CheatSweep})", "Paste");
                    ASMLog.Debug("Paste ended: operation completed during manual step", "Paste");
                    RemoveOperationAt(i);
                    return false;
                }

                // --- Refresh context once for this manual step/sweep, matching automatic tick behavior.
                op.UpdateContext();
                bool keep = op.Context.CheatSweep ? op.Sweep() : op.Step();
                if (!keep)
                {
                    ASMLog.Debug("Paste ended: manual step returned no more work", "Paste");
                    RemoveOperationAt(i);
                }
                return keep;
            }

            return false;
        }
    }
}
