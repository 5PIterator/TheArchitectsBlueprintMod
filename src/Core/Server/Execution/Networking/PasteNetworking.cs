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
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Server
{
    /// <summary>Multipaste control packets; paste payload uses <see cref="PasteRequest"/> (canonical sparse payload JSON via <see cref="PayloadJsonImport"/>).</summary>
    internal static class PasteNetworking
    {
        private static int _nextRequestId = 1;

        /// <summary>Game update timeout if no <see cref="PasteNetPacketType.PasteAck"/> (client-side, listen host / joiners).</summary>
        private const int ClientAckTimeoutGameUpdates = 600;

        private sealed class PendingClientAck
        {
            public int RequestId;
            public uint StartGameUpdate;
        }

        private static readonly List<PendingClientAck> ClientPendingAcks = [];

        internal static bool IsHostClient()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return true;

            // Listen-server host uses client slot 0.
            return Main.myPlayer == 0;
        }

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
                default:
                    throw new InvalidDataException($"Unknown paste packet type: {packetType}");
            }
        }

        internal static bool SendPasteRequest(Mod mod, Player player, Projection projection, out PasteRejectReason reason)
        {
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

            ModPacket packet = mod.GetPacket();
            packet.Write((byte)PasteNetPacketType.PasteRequest);
            request.Write(packet);
            packet.Send();

            projection.Lock();
            projection.Id = requestId;
            RegisterClientPendingAck(requestId);
            return true;
        }

        internal static void SendCancelIfPending(Mod mod, Projection projection)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;
            if (projection is null || projection.Id <= 0)
                return;

            int requestId = projection.Id;
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)PasteNetPacketType.PasteCancel);
            new PasteCancelPacket(requestId).Write(packet);
            packet.Send();
        }

        /// <summary>Stops the client ack timer for this request (e.g. after send cancel) so a late timeout does not fire.</summary>
        internal static void DismissClientPendingAckIfMatches(int requestId)
        {
            if (requestId > 0)
                RemoveClientPendingAck(requestId);
        }

        private static void HandlePasteRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            if (!TileMappingAuthority.IsAuthoritativePastePacketSide())
                return;

            PasteRequest request;
            try
            {
                request = PasteRequest.Read(reader);
            }
            catch (Exception ex)
            {
                TABLog.Error("Failed to read paste request packet.", ex, "PasteNet");
                SendAck(mod, whoAmI, requestId: 0, accepted: false, PasteRejectReason.InvalidPacket);
                return;
            }

            if (whoAmI != 0)
            {
                SendAck(mod, whoAmI, request.RequestId, accepted: false, PasteRejectReason.NotHost);
                return;
            }

            if (!request.TryBuildProjection(whoAmI, out var projection, out var buildReason))
            {
                SendAck(mod, whoAmI, request.RequestId, accepted: false, buildReason);
                return;
            }

            var config = ModContent.GetInstance<PastingConfig>();
            var hints = new PasteClientHints(
                request.RetryOnMissingItems,
                request.RetryOnInvalidPlacement,
                request.ClearStrategy,
                request.ClearItemDropStrategy,
                request.SweepStrategy,
                request.ArrowSpeed,
                request.ArrowMinSpeed,
                request.ArrowMaxSpeed);
            var policy = EffectivePastePolicy.Resolve(config, hints);
            var pastingSystem = ModContent.GetInstance<PastingSystem>();
            if (!pastingSystem.ExecutePaste(projection, policy, out var startReason))
            {
                SendAck(mod, whoAmI, request.RequestId, accepted: false, startReason);
                return;
            }

            SendAck(mod, whoAmI, request.RequestId, accepted: true, PasteRejectReason.None);
        }

        private static void HandlePasteCancel(Mod mod, BinaryReader reader, int whoAmI)
        {
            var cancel = PasteCancelPacket.Read(reader);

            if (TileMappingAuthority.IsAuthoritativePastePacketSide())
            {
                var pastingSystem = ModContent.GetInstance<PastingSystem>();
                bool stopped = pastingSystem.TryStopPasteByCorrelation(whoAmI, cancel.RequestId);
                SendAck(mod, whoAmI, cancel.RequestId, accepted: stopped, stopped ? PasteRejectReason.None : PasteRejectReason.InvalidProjection);
            }
        }

        private static void HandlePasteAck(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var ack = PasteAckPacket.Read(reader);
            RemoveClientPendingAck(ack.RequestId);

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            if (!projectionSystem.GetProjectionByCorrelationId(ack.RequestId, out var projection))
            {
                return;
            }

            projection.Unlock();
            projection.Id = 0;

        }

        private static void SendAck(Mod mod, int toClient, int requestId, bool accepted, PasteRejectReason reason)
        {
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)PasteNetPacketType.PasteAck);
            new PasteAckPacket(requestId, accepted, reason).Write(packet);
            packet.Send(toClient);
        }

        internal static string FormatPasteRejectReason(PasteRejectReason reason)
        {
            return reason switch
            {
                PasteRejectReason.None => "No error (internal).",
                PasteRejectReason.InvalidProjection => "Invalid projection (missing payload or not ready to paste).",
                PasteRejectReason.InvalidPayload => "Invalid payload for paste.",
                PasteRejectReason.InvalidPacket => "Invalid paste request packet or send failed (wrong game mode).",
                PasteRejectReason.NotHost => "Only host can paste in multiplayer.",
                PasteRejectReason.AreaLimit => "Projection exceeds area limits.",
                PasteRejectReason.VolumeLimit => "Projection exceeds tile volume limit.",
                PasteRejectReason.PlayerOperationLimit => "Player operation limit reached.",
                PasteRejectReason.ServerOperationLimit => "Server operation limit reached.",
                PasteRejectReason.AlreadyActive => "Paste operation already active.",
                _ => "Invalid or unsupported paste request.",
            };
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
            if (Main.netMode != NetmodeID.MultiplayerClient || ClientPendingAcks.Count == 0)
                return;

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            var pastingSystem = ModContent.GetInstance<PastingSystem>();
            uint now = Main.GameUpdateCount;

            for (int i = ClientPendingAcks.Count - 1; i >= 0; i--)
            {
                PendingClientAck p = ClientPendingAcks[i];
                if (now - p.StartGameUpdate <= ClientAckTimeoutGameUpdates)
                    continue;

                ClientPendingAcks.RemoveAt(i);

                if (!projectionSystem.GetProjectionByCorrelationId(p.RequestId, out var projection))
                    continue;

                projection.Unlock();
                projection.Id = 0;
            }
        }
    }
}
