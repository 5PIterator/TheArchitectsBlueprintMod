using System;
using System.Collections;
using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core.Server;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
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
        /// Entry point for paste from client tools. Remote clients request only; authoritative instances execute locally.
        /// </summary>
        internal bool StartPaste(Projection projection, out PasteRejectReason rejectReason)
        {
            rejectReason = PasteRejectReason.None;
            ArgumentNullException.ThrowIfNull(projection);

            var config = ModContent.GetInstance<PastingConfig>();
            WarnIfClientPreferencesDisabled(config);

            if (Main.netMode == NetmodeID.MultiplayerClient && !TileMappingAuthority.IsAuthoritativePastePacketSide())
                return RequestPaste(projection, out rejectReason);

            var policy = EffectivePastePolicy.Resolve(config, PasteClientHints.FromClientConfig());
            return ExecutePaste(projection, policy, out rejectReason);
        }

        /// <summary>
        /// Sends a paste request to the authoritative server without creating a local operation.
        /// </summary>
        internal bool RequestPaste(Projection projection, out PasteRejectReason rejectReason)
        {
            rejectReason = PasteRejectReason.None;
            ArgumentNullException.ThrowIfNull(projection);

            if (!ValidateForPasteRequest(projection, out rejectReason))
                return false;

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                rejectReason = PasteRejectReason.InvalidPacket;
                return false;
            }

            return PasteNetworking.SendPasteRequest(Mod, Main.LocalPlayer, projection, out rejectReason);
        }

        /// <summary>
        /// Validates and starts a paste operation on the authoritative execution side.
        /// </summary>
        internal bool ExecutePaste(Projection projection, EffectivePastePolicy policy, out PasteRejectReason rejectReason)
        {
            rejectReason = PasteRejectReason.None;
            ArgumentNullException.ThrowIfNull(projection);

            if (!ValidateForPasteExecution(projection, out rejectReason))
                return false;

            var config = ModContent.GetInstance<PastingConfig>();

            var (tileIds, wallIds) = CollectTileAndWallIds(projection);
            foreach (PastingOperation operation in _operations)
            {
                var (opTiles, opWalls) = CollectTileAndWallIds(operation.Projection);
                tileIds.UnionWith(opTiles);
                wallIds.UnionWith(opWalls);
            }

            ItrStorage.PrepareForPaste(tileIds, wallIds);

            var op = new PastingOperation(projection, config, policy)
            {
                OverlayId = ++_nextPasteOverlayInstanceId
            };

            projection.Lock();
            _operations.Add(op);
            TileExecutorSystem.RegisterSession(op.EffectSession);
            return true;
        }

        private static void WarnIfClientPreferencesDisabled(PastingConfig config)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            var disabledPhases = new List<string>(3);
            if (!config.Phases.Plan.AllowClientPreferences)
                disabledPhases.Add("Plan");
            if (!config.Phases.Clear.AllowClientPreferences)
                disabledPhases.Add("Clear");
            if (!config.Phases.Place.AllowClientPreferences)
                disabledPhases.Add("Place");
            if (!config.Phases.Sweep.AllowClientPreferences)
                disabledPhases.Add("Sweep");

            if (disabledPhases.Count == 0)
                return;

            string phases = string.Join(", ", disabledPhases);
            Main.NewText($"TAB: Client paste preferences are disabled for {phases}. Server policy will be used.", Color.Orange);
        }

        /// <summary>
        /// Stops the paste operation for the given projection, if any.
        /// </summary>
        public void StopPaste(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                PasteNetworking.SendCancelIfPending(Mod, projection);

            foreach (PastingOperation operation in _operations)
            {
                if (ReferenceEquals(operation.Projection, projection))
                {
                    TileExecutorSystem.UnregisterSession(operation.EffectSession.OperationId);
                    operation.Teardown();
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
                if (!StartPaste(projection, out var reject))
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
            if (Main.gameMenu || _operations.Count == 0)
                return false;

            bool anyStepped = false;
            for (int i = _operations.Count - 1; i >= 0; i--)
            {
                var op = _operations[i];
                if (DebugStepper.IsHoldingDebugStick(op.Context.Player))
                    continue;

                if (op.Projection is null || !op.Projection.IsDefined)
                {
                    TABLog.Debug("Paste ended: projection became invalid during update", "Paste");
                    RemoveOperationAt(i);
                    continue;
                }

                if (op.IsCompleted)
                {
                    TABLog.Debug($"Complete (sweepStrategy={op.Context.SweepStrategy})", "Paste");
                    TABLog.Debug("Paste ended: operation completed during update", "Paste");
                    RemoveOperationAt(i);
                    continue;
                }

                if (Environment.TickCount64 < op.NextStepTime)
                    continue;

                op.UpdateContext();
                bool keep = op.Step();
                anyStepped = true;
                if (!keep)
                {
                    TABLog.Debug("Paste ended: step returned no more work", "Paste");
                    RemoveOperationAt(i);
                }
            }

            return anyStepped;
        }

        private void RemoveOperationAt(int index)
        {
            TileExecutorSystem.UnregisterSession(_operations[index].EffectSession.OperationId);
            _operations[index].Teardown();
            _operations.RemoveAt(index);
            if (_operations.Count == 0)
                ItrStorage.Release();
        }

        /// <inheritdoc />
        public override void PostUpdatePlayers()
        {
            ProcessOperations();
            TileExecutorSystem.ProcessSessions();
            PasteOverlayNetworking.BroadcastPasteOverlaysToRemoteClients(this);
        }

        /// <inheritdoc />
        public override void OnWorldUnload()
        {
            for (int i = 0; i < _operations.Count; i++)
            {
                TileExecutorSystem.UnregisterSession(_operations[i].EffectSession.OperationId);
                _operations[i].Teardown();
            }

            _operations.Clear();
            ItrStorage.Release();
            PasteOverlayNetworking.ResetRemoteOverlayBroadcastState();
        }

        private static bool ValidateProjectionPayload(Projection projection) =>
            projection is { IsDefined: true, HasPayload: true };

        /// <summary>Lightweight validation for outbound paste requests from non-authoritative clients.</summary>
        private static bool ValidateForPasteRequest(Projection projection, out PasteRejectReason rejectReason)
        {
            rejectReason = PasteRejectReason.None;
            if (!ValidateProjectionPayload(projection))
            {
                rejectReason = PasteRejectReason.InvalidProjection;
                return false;
            }

            var config = ModContent.GetInstance<PastingConfig>();
            if (PasteLimits.ExceedsArea(projection, config))
            {
                rejectReason = PasteRejectReason.AreaLimit;
                return false;
            }

            if (PasteLimits.ExceedsVolume(projection, config))
            {
                rejectReason = PasteRejectReason.VolumeLimit;
                return false;
            }

            return true;
        }

        /// <summary>Authoritative validation before creating a paste operation.</summary>
        private bool ValidateForPasteExecution(Projection projection, out PasteRejectReason rejectReason)
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
            if (PasteLimits.ExceedsArea(projection, config))
            {
                rejectReason = PasteRejectReason.AreaLimit;
                return false;
            }

            if (PasteLimits.ExceedsVolume(projection, config))
            {
                rejectReason = PasteRejectReason.VolumeLimit;
                return false;
            }

            if (GetActiveOperationCountForPlayer(projection.Owner) >= config.Limits.Client.PlayerOperationLimit)
            {
                rejectReason = PasteRejectReason.PlayerOperationLimit;
                return false;
            }

            if (GetActiveOperationCount() >= config.Limits.Server.ServerOperationLimit)
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

        private static (HashSet<int> TileIds, HashSet<int> WallIds) CollectTileAndWallIds(Projection projection)
        {
            var tileIds = new HashSet<int>();
            var wallIds = new HashSet<int>();

            if (projection is null || !projection.HasPayload)
                return (tileIds, wallIds);

            IReadOnlyList<Payload.Entry> entries = projection.Payload.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                PayloadCell cell = entries[i].Cell;
                if (cell.IsEmpty)
                    continue;

                if (cell.Detail.HasTile)
                    tileIds.Add(cell.Block.Type);

                if (cell.Wall.Type != WallID.None)
                    wallIds.Add(cell.Wall.Type);
            }

            return (tileIds, wallIds);
        }

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

        internal int GetActiveOperationCount() => _operations.Count;

        internal int GetActiveOperationCountForPlayer(int ownerPlayerIndex)
        {
            int count = 0;
            for (int i = 0; i < _operations.Count; i++)
            {
                if (_operations[i].Projection.Owner == ownerPlayerIndex)
                    count++;
            }
            return count;
        }

        internal bool TryStopPasteByCorrelation(int ownerPlayerIndex, int correlationId)
        {
            if (correlationId <= 0)
                return false;

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

        internal bool TryGetActivePasteForPlayer(Player player, out bool requireItems)
        {
            requireItems = false;
            if (player is null) return false;

            for (int i = 0; i < _operations.Count; i++)
            {
                if (_operations[i].Projection.Owner == player.whoAmI)
                {
                    requireItems = _operations[i].Context.RequireItems;
                    return true;
                }
            }
            return false;
        }

        internal bool DebugStepOperation(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);

            for (int i = 0; i < _operations.Count; i++)
            {
                if (!ReferenceEquals(_operations[i].Projection, projection))
                    continue;

                var op = _operations[i];
                if (op.Projection is null || !op.Projection.IsDefined)
                {
                    TABLog.Debug("Paste ended: projection became invalid during manual step", "Paste");
                    RemoveOperationAt(i);
                    return false;
                }

                if (op.IsCompleted)
                {
                    TABLog.Debug($"Complete (sweepStrategy={op.Context.SweepStrategy})", "Paste");
                    TABLog.Debug("Paste ended: operation completed during manual step", "Paste");
                    RemoveOperationAt(i);
                    return false;
                }

                op.UpdateContext();
                bool keep = op.Step();
                TileExecutorSystem.ProcessSessions();
                if (!keep)
                {
                    TABLog.Debug("Paste ended: manual step returned no more work", "Paste");
                    RemoveOperationAt(i);
                }
                return keep;
            }

            return false;
        }
    }
}
