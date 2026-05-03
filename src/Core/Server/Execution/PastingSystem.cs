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

        /// <summary>
        /// Starts a paste operation for the given projection.
        /// </summary>
        /// <param name="projection"></param>
        /// <param name="preferences"></param>
        /// <param name="rejectReason"></param>
        /// <returns></returns>
        internal bool StartPaste(Projection projection, ClientPreferences preferences, out PasteRejectReason rejectReason)
        {
            // --- Ignore invalid or already-running projections, then prepare storage and register a new operation.

            // Validate the projection
            if (!ValidateProjection(projection, out rejectReason))
                return false;

            // If the player is on a client, request a paste from the server.
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                PasteNetworking.SendPasteRequest(Mod, Main.LocalPlayer, projection, out rejectReason);
                if (rejectReason != PasteRejectReason.None)
                {
                    return false;
                }
            }

            // Else, start the paste locally.

            // For each projection in each running operation, collect the tile and wall IDs and unify them into a single set.
            var config = ModContent.GetInstance<PastingConfig>();
            var (tileIds, wallIds) = CollectTileAndWallIds(projection);
            foreach (PastingOperation operation in _operations)
            {
                var (opTiles, opWalls) = CollectTileAndWallIds(operation.Projection);
                tileIds.UnionWith(opTiles);
                wallIds.UnionWith(opWalls);
            }
            // Use the unified set of tile and wall IDs to prepare the storage for the paste.
            ItrStorage.PrepareForPaste(tileIds, wallIds);

            // Create a new operation for the paste.
            var op = new PastingOperation(projection, config, preferences)
            {
                OverlayId = ++_nextPasteOverlayInstanceId
            };
            // If the cheat sweep is enabled, write the task map for the paste.
            if (config.CheatSweep)
                PasteTaskMapDebug.WriteTaskMap(projection, op.ApplySourceClear, op.Pipeline, op.ClearTasks, op.PlaceTasks);

            // Lock the projection to prevent it from being modified while the paste is in progress.
            projection.Lock();
            // Add the new operation to the list of running operations.
            _operations.Add(op);
            // Return true to indicate that the paste was started successfully.
            return true;
        }

        /// <summary>
        /// Stops the paste operation for the given projection, if any.
        /// </summary>
        public void StopPaste(Projection projection)
        {
            /// Find the matching operation and teardown it.

            ArgumentNullException.ThrowIfNull(projection);

            // If the player is on a client, request a paste cancel from the server.
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                PasteNetworking.SendCancelIfPending(Mod, projection);
            }

            // Iterate over the list of running operations and find the matching operation.
            foreach (PastingOperation operation in _operations)
            {
                if (ReferenceEquals(operation.Projection, projection))
                {
                    // Teardown the operation.
                    operation.Teardown();

                    // Remove the operation from the list of running operations.
                    _operations.Remove(operation);
                    break;
                }
            }
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
            if (projection.Area.Width > config.MaxPasteWidth || projection.Area.Height > config.MaxPasteHeight)
            {
                rejectReason = PasteRejectReason.AreaLimit;
                return false;
            }

            int pasteVolume = projection.PayloadSize.X * projection.PayloadSize.Y;
            if (pasteVolume > config.VolumeLimit)
            {
                rejectReason = PasteRejectReason.VolumeLimit;
                return false;
            }

            if (GetActiveOperationCountForPlayer(projection.Owner) >= config.PlayerOperationLimit)
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
            /// Walk payload cells and gather the material IDs needed by interaction-map loading.

            var tileIds = new HashSet<int>();
            var wallIds = new HashSet<int>();

            // If the projection is null or doesn't have a payload, return an empty set of tile and wall IDs.
            if (projection is null || !projection.HasPayload)
                return (tileIds, wallIds);

            // Walk all payload cells (order matches foreach on the payload grid; only IDs are collected).
            foreach (PayloadCell cell in projection.Payload.Cells)
            {
                // If the cell is empty, skip it.
                if (cell.IsEmpty)
                    continue;

                // If the cell has a tile, add the tile type to the tile IDs.
                if (cell.Detail.HasTile)
                    tileIds.Add(cell.Block.Type);

                // If the cell has a wall, add the wall type to the wall IDs.
                if (cell.Wall.Type != WallID.None)
                    wallIds.Add(cell.Wall.Type);
            }

            // Return the collected tile and wall IDs.
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
                if (_operations[i].Projection.Owner == ownerPlayerIndex)
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
                if (projection.Owner == ownerPlayerIndex && projection.Id == correlationId)
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
                if (_operations[i].Projection.Owner == player.whoAmI)
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
