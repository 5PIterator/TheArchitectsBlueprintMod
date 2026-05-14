using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArchitectsBlueprintMod.Config;
using ArchitectsBlueprintMod.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsBlueprintMod.Core.Server
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
        private const int MaxPasteRequestJsonBytes = 32 * 1024 * 1024;

        public int RequestId { get; private set; }
        public Rectangle Bounds { get; private set; } = Rectangle.Empty;
        public Rectangle SourceArea { get; private set; } = Rectangle.Empty;
        public bool CutSource { get; private set; }
        public bool IgnoreItems { get; private set; }
        public bool IgnoreInvalid { get; private set; }
        public bool TransparentPaste { get; private set; }
        public int PasteStepSize { get; private set; }
        public string Json { get; private set; } = string.Empty;

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

            // TODO: Implement Project and Player name from projection.
            string json = projection.Payload.ToJson();
            if (string.IsNullOrEmpty(json))
            {
                reason = PasteRejectReason.InvalidPayload;
                return false;
            }

            request.RequestId = requestId;
            request.Bounds = projection.Area;
            request.Json = json;
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
            byte[] utf8 = Encoding.UTF8.GetBytes(Json);
            if (utf8.Length > MaxPasteRequestJsonBytes)
            {
                throw new InvalidOperationException("Paste request payload JSON exceeds max size.");
            }

            writer.Write(RequestId);
            WriteRectangle(writer, Bounds);
            WriteRectangle(writer, SourceArea);
            writer.Write(CutSource);
            writer.Write(IgnoreItems);
            writer.Write(IgnoreInvalid);
            writer.Write(TransparentPaste);
            writer.Write(PasteStepSize);
            writer.Write(utf8.Length);
            writer.Write(utf8);
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

            int jsonLen = reader.ReadInt32();
            if (jsonLen < 0 || jsonLen > MaxPasteRequestJsonBytes)
            {
                throw new InvalidDataException("Paste request has invalid JSON length.");
            }

            byte[] utf8 = reader.ReadBytes(jsonLen);
            string json = Encoding.UTF8.GetString(utf8);
            request.Json = json;
            return request;
        }

        public bool TryBuildProjection(int ownerPlayerIndex, out Projection projection, out PasteRejectReason reason)
        {
            projection = null;
            reason = PasteRejectReason.None;

            if (Bounds.IsEmpty || string.IsNullOrEmpty(Json))
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            var payload = Payload.FromJson(Json);

            Player? shellOwner = (uint)ownerPlayerIndex < (uint)Main.maxPlayers ? Main.player[ownerPlayerIndex] : null;
            var shell = new Selection(shellOwner.whoAmI);
            shell.Define(Bounds);

            projection = new Projection(ownerPlayerIndex, shell, cutSource: !SourceArea.IsEmpty && CutSource)
            {
                Id = RequestId,
            };

            projection.SetPayload(payload);

            Rectangle targetRect = Bounds;
            if (payload.IsDefined && (Bounds.Width != payload.Width || Bounds.Height != payload.Height))
            {
                targetRect = new Rectangle(Bounds.X, Bounds.Y, payload.Width, payload.Height);
            }

            projection.Define(targetRect);
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
