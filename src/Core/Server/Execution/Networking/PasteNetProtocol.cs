using System;
using System.Collections.Generic;
using System.IO;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Server
{
    internal enum PasteNetPacketType : byte
    {
        PasteRequest = 1,
        PasteAck = 2,
        PasteCancel = 3,
        PasteOverlaySyncV1 = 4,
    }

    internal readonly struct PasteAckPacket(int requestId, bool accepted, PasteRejectReason reason)
    {
        public int RequestId { get; } = requestId;
        public bool Accepted { get; } = accepted;
        public PasteRejectReason Reason { get; } = reason;

        public void Write(BinaryWriter writer)
        {
            writer.Write(RequestId);
            writer.Write(Accepted);
            writer.Write((byte)Reason);
        }

        public static PasteAckPacket Read(BinaryReader reader)
        {
            int requestId = reader.ReadInt32();
            bool accepted = reader.ReadBoolean();
            var reason = (PasteRejectReason)reader.ReadByte();
            return new PasteAckPacket(requestId, accepted, reason);
        }
    }

    internal readonly struct PasteCancelPacket(int requestId)
    {
        public int RequestId { get; } = requestId;

        public void Write(BinaryWriter writer)
        {
            writer.Write(RequestId);
        }

        public static PasteCancelPacket Read(BinaryReader reader)
        {
            return new PasteCancelPacket(reader.ReadInt32());
        }
    }

    internal sealed class PasteRequest
    {
        private const int MaxPasteRequestPayloadBytes = 32 * 1024 * 1024;

        public int RequestId { get; private set; }
        public Rectangle Bounds { get; private set; } = Rectangle.Empty;
        public Rectangle SourceArea { get; private set; } = Rectangle.Empty;
        public bool CutSource { get; private set; }
        public bool IgnoreItems { get; private set; }
        public bool IgnoreInvalid { get; private set; }
        public bool TransparentPaste { get; private set; }
        public int PasteStepSize { get; private set; }
        public byte[] PayloadBytes { get; private set; } = [];

        private PasteRequest() { }

        public static bool TryCreate(Projection projection, int requestId, out PasteRequest request, out PasteRejectReason reason)
        {
            request = new PasteRequest();
            reason = PasteRejectReason.None;

            if (projection is null || !projection.IsDefined || !projection.HasPayload)
            {
                reason = PasteRejectReason.InvalidProjection;
                return false;
            }

            Point payloadSize = projection.PayloadSize;
            if (payloadSize.X <= 0 || payloadSize.Y <= 0)
            {
                reason = PasteRejectReason.InvalidPayload;
                return false;
            }

            byte[] payloadBytes = PayloadSerialization.BuildExportDocumentBytes(projection, $"Paste_{requestId}");
            if (payloadBytes.Length == 0)
            {
                reason = PasteRejectReason.InvalidPayload;
                return false;
            }

            request.RequestId = requestId;
            request.Bounds = projection.Area;
            request.PayloadBytes = payloadBytes;
            var clientCfg = ModContent.GetInstance<ClientPasteConfig>();
            var serverCfg = ModContent.GetInstance<PastingConfig>();
            request.IgnoreItems = clientCfg.IgnoreItems;
            request.IgnoreInvalid = clientCfg.IgnoreInvalid;
            request.TransparentPaste = clientCfg.TransparentPaste;
            request.PasteStepSize = Math.Max(1, Math.Min(clientCfg.PasteStepSize, serverCfg.MaxPasteStepSize));
            request.SourceArea = projection.SourceArea;
            request.CutSource = !projection.SourceArea.IsEmpty && projection.CutSource;

            return true;
        }

        public void Write(BinaryWriter writer)
        {
            if (PayloadBytes.Length > MaxPasteRequestPayloadBytes)
                throw new InvalidOperationException("Paste request payload exceeds max size.");

            writer.Write(RequestId);
            WriteRectangle(writer, Bounds);
            WriteRectangle(writer, SourceArea);
            writer.Write(CutSource);
            writer.Write(IgnoreItems);
            writer.Write(IgnoreInvalid);
            writer.Write(TransparentPaste);
            writer.Write(PasteStepSize);
            writer.Write(PayloadBytes.Length);
            writer.Write(PayloadBytes);
        }

        public static PasteRequest Read(BinaryReader reader)
        {
            var request = new PasteRequest
            {
                RequestId = reader.ReadInt32(),
                Bounds = ReadRectangle(reader),
                SourceArea = ReadRectangle(reader),
            };

            request.CutSource = reader.ReadBoolean();
            request.IgnoreItems = reader.ReadBoolean();
            request.IgnoreInvalid = reader.ReadBoolean();
            request.TransparentPaste = reader.ReadBoolean();
            request.PasteStepSize = reader.ReadInt32();

            int payloadLen = reader.ReadInt32();
            if (payloadLen < 0 || payloadLen > MaxPasteRequestPayloadBytes)
                throw new InvalidDataException("Paste request has invalid payload length.");

            request.PayloadBytes = reader.ReadBytes(payloadLen);
            return request;
        }

        public bool TryBuildProjection(int ownerPlayerIndex, out Projection projection, out PasteRejectReason reason)
        {
            projection = null;
            reason = PasteRejectReason.None;

            if (Bounds.IsEmpty || PayloadBytes.Length == 0)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            if (!PayloadSerialization.TryParsePayloadDocument(PayloadBytes, out Payload payload, out PayloadDocumentInfo docInfo, out string? parseError)
                || !payload.IsDefined)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            Player? shellOwner = (uint)ownerPlayerIndex < (uint)Main.maxPlayers ? Main.player[ownerPlayerIndex] : null;
            var shell = new Selection(shellOwner.whoAmI);
            shell.Define(Bounds);

            projection = new Projection(ownerPlayerIndex, shell, cutSource: !SourceArea.IsEmpty && CutSource)
            {
                Id = RequestId,
            };

            projection.SetPayload(payload, docInfo.ModsLoaded);
            return true;
        }

        private static void WriteRectangle(BinaryWriter writer, Rectangle rectangle)
        {
            writer.Write(rectangle.X);
            writer.Write(rectangle.Y);
            writer.Write(rectangle.Width);
            writer.Write(rectangle.Height);
        }

        private static Rectangle ReadRectangle(BinaryReader reader)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            return new Rectangle(x, y, width, height);
        }
    }
}
