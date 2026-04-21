using System;
using System.IO;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using ArchitectsStencilMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core.Server
{
    /// <summary>Multipaste control packets; paste payload uses <see cref="PasteRequest"/> (JSON <see cref="ArchitectsStencilMod.Core.Client.PayloadImport.PayloadDocument"/>).</summary>
    internal static class PasteNetworking
    {
        private static int _nextRequestId = 1;

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
                ASMLog.Error("Failed to read paste request packet.", ex, "PasteNet");
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
            var clientPrefs = ClientPreferences.FromNetwork(request.ForceTiles, request.TransparentPaste, request.PasteStepSize, config);
            var pastingSystem = ModContent.GetInstance<PastingSystem>();
            if (!pastingSystem.StartPaste(projection, clientPrefs, out var startReason))
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
            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            if (!projectionSystem.TryGetProjectionByCorrelationId(Main.LocalPlayer, ack.RequestId, out var projection))
            {
                return;
            }

            projection.Unlock();
            projection.Id = 0;

            if (!ack.Accepted)
            {
                Main.NewText($"Paste rejected: {FormatReason(ack.Reason)}", Color.OrangeRed);
            }
        }

        private static void SendAck(Mod mod, int toClient, int requestId, bool accepted, PasteRejectReason reason)
        {
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)PasteNetPacketType.PasteAck);
            new PasteAckPacket(requestId, accepted, reason).Write(packet);
            packet.Send(toClient);
        }

        private static string FormatReason(PasteRejectReason reason)
        {
            return reason switch
            {
                PasteRejectReason.NotHost => "Only host can paste in multiplayer.",
                PasteRejectReason.AreaLimit => "Projection exceeds area limits.",
                PasteRejectReason.VolumeLimit => "Projection exceeds tile volume limit.",
                PasteRejectReason.PlayerOperationLimit => "Player operation limit reached.",
                PasteRejectReason.ServerOperationLimit => "Server operation limit reached.",
                PasteRejectReason.AlreadyActive => "Paste operation already active.",
                _ => "Invalid or unsupported paste request.",
            };
        }
    }
}
