using System;
using System.Collections.Generic;
using System.IO;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Server
{
    /// <summary>Multipaste control packets; paste payload uses <see cref="PasteRequest"/> (canonical sparse payload JSON via <see cref="PayloadJsonImport"/>).</summary>
    internal static class PasteNetworking
    {
        private static int _nextRequestId = 1;

        /// <summary>Game update timeout if no <see cref="PasteNetPacketType.PasteAck"/> (client-side).</summary>
        private const int ClientAckTimeoutGameUpdates = 600;

        /// <summary>Paste requests carry full document bytes; default ModPacket capacity is too small.</summary>
        private const int PasteRequestPacketCapacity = 1_048_576;

        private sealed class PendingClientAck
        {
            public int RequestId;
            public uint StartGameUpdate;
        }

        private static readonly List<PendingClientAck> ClientPendingAcks = [];

        internal static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI)
        {
            var packetType = (PasteNetPacketType)reader.ReadByte();
            switch (packetType)
            {
                case PasteNetPacketType.PasteRequest:
                    HandlePasteRequest(mod, reader, whoAmI);
                    return;
                case PasteNetPacketType.PasteAck:
                    HandlePasteAck(reader);
                    return;
                case PasteNetPacketType.PasteCancel:
                    HandlePasteCancel(mod, reader, whoAmI);
                    return;
                case PasteNetPacketType.PasteOverlaySyncV1:
                    PasteOverlayNetworking.HandlePasteOverlaySyncClient(reader);
                    return;
                case PasteNetPacketType.PasteOwnerChat:
                    HandlePasteOwnerChat(reader);
                    return;
                case PasteNetPacketType.PasteWorldFx:
                    PasteWorldFxNetworking.HandlePacket(mod, reader, whoAmI);
                    return;
                case PasteNetPacketType.ManifestRequest:
                    PasteManifestNetworking.HandlePacket(mod, reader, whoAmI);
                    return;
                default:
                    throw new InvalidDataException($"Unknown paste packet type: {packetType}");
            }
        }

        /// <summary>Sends one owner-scoped chat/log line from the dedicated server to that client.</summary>
        internal static void SendOwnerChatLine(Mod mod, int toClient, string message, string? category, Color color)
        {
            // --- Server-only; filtering already happened via ClientLogRelayPrefs.

            if (Main.netMode != NetmodeID.Server || toClient < 0 || toClient >= Main.maxPlayers)
                return;

            if (string.IsNullOrEmpty(message))
                return;

            ModPacket packet = mod.GetPacket();
            packet.Write((byte)PasteNetPacketType.PasteOwnerChat);
            packet.Write(category ?? string.Empty);
            packet.Write(message);
            packet.Write(color.R);
            packet.Write(color.G);
            packet.Write(color.B);
            packet.Write(color.A);
            packet.Send(toClient);
        }

        /// <summary>Shows a server-forwarded diagnostic line on the owning client.</summary>
        private static void HandlePasteOwnerChat(BinaryReader reader)
        {
            // --- Server already applied the client's prefs; always show the line.

            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            string category = reader.ReadString();
            string message = reader.ReadString();
            byte r = reader.ReadByte();
            byte g = reader.ReadByte();
            byte b = reader.ReadByte();
            byte a = reader.ReadByte();
            Color color = new(r, g, b, a);

            string full = string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
            Main.NewText(full, color);
        }

        /// <summary>Locks the projection and sends a paste request packet to the server.</summary>
        internal static bool SendPasteRequest(Mod mod, Player player, Projection projection, out PasteRejectReason reason)
        {
            // --- Build request bytes, send, then lock locally until Accepted/Rejected/timeout.

            reason = PasteRejectReason.None;

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            int requestId = _nextRequestId++;
            if (!PasteRequest.TryCreate(projection, requestId, out var request, out reason))
            {
                return false;
            }

            try
            {
                ModPacket packet = mod.GetPacket(PasteRequestPacketCapacity);
                packet.Write((byte)PasteNetPacketType.PasteRequest);
                request.Write(packet);
                packet.Send();
            }
            catch (Exception ex)
            {
                LogNet($"SendPasteRequest failed id={requestId}: {ex.Message}");
                TABLog.Error("Failed to send paste request packet.", ex, "PasteNet");
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            LogNet($"SendPasteRequest id={requestId} bytes={request.PayloadBytes.Length} who={player.whoAmI}");
            projection.Lock();
            projection.Id = requestId;
            RegisterClientPendingAck(requestId);
            return true;
        }

        /// <summary>Sends a cancel for the projection correlation id and dismisses the pending ACK timer.</summary>
        internal static void SendCancelIfPending(Mod mod, Projection projection)
        {
            // --- Notify the server, then drop the client timeout so a late unlock cannot fire after cancel.

            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;
            if (projection is null || projection.Id <= 0)
                return;

            int requestId = projection.Id;
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)PasteNetPacketType.PasteCancel);
            new PasteCancelPacket(requestId).Write(packet);
            packet.Send();

            DismissClientPendingAckIfMatches(requestId);
        }

        /// <summary>Stops the client ack timer for this request so a late timeout does not fire.</summary>
        internal static void DismissClientPendingAckIfMatches(int requestId)
        {
            if (requestId > 0)
                RemoveClientPendingAck(requestId);
        }

        /// <summary>Notifies the requesting client that a server-side paste operation finished.</summary>
        internal static void SendFinished(Mod mod, int toClient, int requestId)
        {
            // --- Only the authoritative server sends lifecycle ACKs to the owning client.

            if (Main.netMode != NetmodeID.Server || requestId <= 0)
                return;

            SendAck(mod, toClient, requestId, PasteAckKind.Finished, PasteRejectReason.None);
        }

        private static void HandlePasteRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Server-only: parse request, rebuild projection for whoAmI, execute, always ACK.

            int requestId = 0;
            try
            {
                if (!TileMappingAuthority.IsAuthoritativePastePacketSide())
                {
                    LogNet($"PasteRequest ignored (not authoritative) whoAmI={whoAmI} netMode={Main.netMode}");
                    return;
                }

                PasteRequest request;
                try
                {
                    request = PasteRequest.Read(reader);
                    requestId = request.RequestId;
                }
                catch (Exception ex)
                {
                    LogNet($"PasteRequest read failed whoAmI={whoAmI}: {ex.Message}");
                    TABLog.Error("Failed to read paste request packet.", ex, "PasteNet");
                    SendAck(mod, whoAmI, requestId, PasteAckKind.Rejected, PasteRejectReason.InvalidPacket);
                    return;
                }

                LogNet($"PasteRequest received id={requestId} whoAmI={whoAmI} payloadBytes={request.PayloadBytes.Length}");

                ClientLogRelayPrefs.Set(whoAmI, request.LogHints);

                if (!request.TryBuildExecutionContext(whoAmI, out var execution, out var buildReason))
                {
                    LogNet($"PasteRequest build failed id={requestId} reason={buildReason}");
                    SendAck(mod, whoAmI, requestId, PasteAckKind.Rejected, buildReason);
                    return;
                }

                var config = ModContent.GetInstance<PastingConfig>();
                var hints = new PasteClientHints(
                    request.RetryOnMissingItems,
                    request.RetryOnInvalidPlacement,
                    request.ClearStrategy,
                    request.ClearItemDropStrategy,
                    request.ArrowSpeed,
                    request.ArrowMinSpeed,
                    request.ArrowMaxSpeed);
                var policy = EffectivePastePolicy.Resolve(config, hints);
                var pastingSystem = ModContent.GetInstance<PastingSystem>();
                if (!pastingSystem.ExecutePaste(execution, policy, out var startReason))
                {
                    LogNet($"PasteRequest execute failed id={requestId} reason={startReason}");
                    SendAck(mod, whoAmI, requestId, PasteAckKind.Rejected, startReason);
                    return;
                }

                LogNet($"PasteRequest accepted id={requestId} whoAmI={whoAmI}");
                SendAck(mod, whoAmI, requestId, PasteAckKind.Accepted, PasteRejectReason.None);
            }
            catch (Exception ex)
            {
                LogNet($"PasteRequest handler exception whoAmI={whoAmI} id={requestId}: {ex.Message}");
                TABLog.Error("Paste request handler failed.", ex, "PasteNet");
                if (TileMappingAuthority.IsAuthoritativePastePacketSide())
                    SendAck(mod, whoAmI, requestId, PasteAckKind.Rejected, PasteRejectReason.InvalidPacket);
            }
        }

        private static void HandlePasteCancel(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Server stops the correlated operation when present; ACK Cancelled or Rejected.

            var cancel = PasteCancelPacket.Read(reader);

            if (!TileMappingAuthority.IsAuthoritativePastePacketSide())
                return;

            var pastingSystem = ModContent.GetInstance<PastingSystem>();
            bool stopped = pastingSystem.TryStopPasteByCorrelation(whoAmI, cancel.RequestId);
            if (stopped)
                SendAck(mod, whoAmI, cancel.RequestId, PasteAckKind.Cancelled, PasteRejectReason.None);
            else
                SendAck(mod, whoAmI, cancel.RequestId, PasteAckKind.Rejected, PasteRejectReason.InvalidProjection);
        }

        private static void HandlePasteAck(BinaryReader reader)
        {
            // --- Client: Accepted keeps lock; Rejected/Cancelled/Finished unlock and clear correlation.

            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var ack = PasteAckPacket.Read(reader);
            LogNet($"PasteAck kind={ack.Kind} id={ack.RequestId} reason={ack.Reason}");

            RemoveClientPendingAck(ack.RequestId);

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            if (!projectionSystem.GetProjectionByCorrelationId(ack.RequestId, out var projection))
            {
                // Parse failures may ACK with requestId 0; still unlock the pending paste.
                if (ack.Kind == PasteAckKind.Rejected && ack.RequestId <= 0)
                    UnlockAllPendingClientProjections(projectionSystem);
                return;
            }

            switch (ack.Kind)
            {
                case PasteAckKind.Accepted:
                    // Paste is in progress on the server; keep locked with correlation id.
                    return;

                case PasteAckKind.Rejected:
                    projection.Unlock();
                    projection.Id = 0;
                    NotifyReject(ack.Reason);
                    return;

                case PasteAckKind.Cancelled:
                case PasteAckKind.Finished:
                    projection.Unlock();
                    projection.Id = 0;
                    return;

                default:
                    projection.Unlock();
                    projection.Id = 0;
                    return;
            }
        }

        private static void UnlockAllPendingClientProjections(ProjectionSystem projectionSystem)
        {
            // --- Clear every pending ACK and unlock its projection so a zero-id reject cannot soft-lock.

            for (int i = ClientPendingAcks.Count - 1; i >= 0; i--)
            {
                int id = ClientPendingAcks[i].RequestId;
                ClientPendingAcks.RemoveAt(i);
                if (!projectionSystem.GetProjectionByCorrelationId(id, out var projection))
                    continue;

                projection.Unlock();
                projection.Id = 0;
            }
        }

        private static void SendAck(Mod mod, int toClient, int requestId, PasteAckKind kind, PasteRejectReason reason)
        {
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)PasteNetPacketType.PasteAck);
            new PasteAckPacket(requestId, kind, reason).Write(packet);
            packet.Send(toClient);
            LogNet($"SendAck kind={kind} id={requestId} toClient={toClient} reason={reason}");
        }

        /// <summary>Returns the localized chat string for a paste reject reason.</summary>
        internal static string FormatPasteRejectReason(PasteRejectReason reason)
        {
            // --- Map enum to Mods.TheArchitectsBlueprintMod.UI.Paste localization keys.

            string key = reason switch
            {
                PasteRejectReason.None => "Mods.TheArchitectsBlueprintMod.UI.Paste.None",
                PasteRejectReason.InvalidProjection => "Mods.TheArchitectsBlueprintMod.UI.Paste.InvalidProjection",
                PasteRejectReason.InvalidPayload => "Mods.TheArchitectsBlueprintMod.UI.Paste.InvalidPayload",
                PasteRejectReason.InvalidPacket => "Mods.TheArchitectsBlueprintMod.UI.Paste.InvalidPacket",
                PasteRejectReason.AreaLimit => "Mods.TheArchitectsBlueprintMod.UI.Paste.AreaLimit",
                PasteRejectReason.VolumeLimit => "Mods.TheArchitectsBlueprintMod.UI.Paste.VolumeLimit",
                PasteRejectReason.PlayerOperationLimit => "Mods.TheArchitectsBlueprintMod.UI.Paste.PlayerOperationLimit",
                PasteRejectReason.ServerOperationLimit => "Mods.TheArchitectsBlueprintMod.UI.Paste.ServerOperationLimit",
                PasteRejectReason.AlreadyActive => "Mods.TheArchitectsBlueprintMod.UI.Paste.AlreadyActive",
                PasteRejectReason.OutOfWorldTiles => "Mods.TheArchitectsBlueprintMod.UI.Paste.OutOfWorldTiles",
                PasteRejectReason.UnrevealedTiles => "Mods.TheArchitectsBlueprintMod.UI.Paste.UnrevealedTiles",
                PasteRejectReason.OutOfRange => "Mods.TheArchitectsBlueprintMod.UI.Paste.OutOfRange",
                _ => "Mods.TheArchitectsBlueprintMod.UI.Paste.Unsupported",
            };

            return Language.GetTextValue(key);
        }

        /// <summary>Prints a localized paste-reject chat line on the local client.</summary>
        internal static void NotifyReject(PasteRejectReason reason)
        {
            // --- Skip server process; clients need visible reject feedback with a shared prefix.

            if (Main.netMode == NetmodeID.Server)
                return;

            Main.NewText(
                Language.GetTextValue(
                    "Mods.TheArchitectsBlueprintMod.UI.Paste.RejectedPrefix",
                    FormatPasteRejectReason(reason)),
                Color.OrangeRed);
        }

        /// <summary>Prints the localized ACK-timeout chat line on the local client.</summary>
        internal static void NotifyAckTimeout()
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            Main.NewText(
                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Paste.AckTimeout"),
                Color.OrangeRed);
        }

        private static void RegisterClientPendingAck(int requestId)
        {
            ClientPendingAcks.Add(new PendingClientAck
            {
                RequestId = requestId,
                StartGameUpdate = Main.GameUpdateCount,
            });
        }

        private static void RemoveClientPendingAck(int requestId)
        {
            for (int i = ClientPendingAcks.Count - 1; i >= 0; i--)
            {
                if (ClientPendingAcks[i].RequestId == requestId)
                    ClientPendingAcks.RemoveAt(i);
            }
        }

        /// <summary>Client only: drops stale pending requests if the server never answered in time.</summary>
        internal static void TickClientPendingAckTimeouts()
        {
            // --- Unlock projections whose Accept/Reject ACK never arrived within the timeout window.

            if (Main.netMode != NetmodeID.MultiplayerClient || ClientPendingAcks.Count == 0)
                return;

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            uint now = Main.GameUpdateCount;

            for (int i = ClientPendingAcks.Count - 1; i >= 0; i--)
            {
                PendingClientAck p = ClientPendingAcks[i];
                if (now - p.StartGameUpdate <= ClientAckTimeoutGameUpdates)
                    continue;

                ClientPendingAcks.RemoveAt(i);

                if (!projectionSystem.GetProjectionByCorrelationId(p.RequestId, out var projection))
                    continue;

                LogNet($"PasteAck timeout id={p.RequestId}");
                projection.Unlock();
                projection.Id = 0;
                NotifyAckTimeout();
            }
        }

        /// <summary>Always-on networking diagnostics (independent of client log-tag toggles).</summary>
        private static void LogNet(string message)
        {
            var mod = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>();
            mod?.Logger.Info($"[PasteNet] {message}");
        }
    }
}
