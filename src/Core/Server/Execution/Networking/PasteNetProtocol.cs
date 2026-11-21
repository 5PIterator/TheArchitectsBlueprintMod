using System;
using System.Collections.Generic;
using System.IO;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Phases;
using TheArchitectsBlueprintMod.Debug;
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
        PasteOwnerChat = 5,
        PasteWorldFx = 6,
        ManifestRequest = 7,
        ManifestResponse = 8,
    }

    /// <summary>Lifecycle outcome for a paste request correlation id.</summary>
    internal enum PasteAckKind : byte
    {
        Rejected = 0,
        Accepted = 1,
        Cancelled = 2,
        Finished = 3,
    }

    internal readonly struct PasteAckPacket(int requestId, PasteAckKind kind, PasteRejectReason reason)
    {
        public int RequestId { get; } = requestId;
        public PasteAckKind Kind { get; } = kind;
        public PasteRejectReason Reason { get; } = reason;

        public void Write(BinaryWriter writer)
        {
            writer.Write(RequestId);
            writer.Write((byte)Kind);
            writer.Write((byte)Reason);
        }

        public static PasteAckPacket Read(BinaryReader reader)
        {
            int requestId = reader.ReadInt32();
            var kind = (PasteAckKind)reader.ReadByte();
            var reason = (PasteRejectReason)reader.ReadByte();
            return new PasteAckPacket(requestId, kind, reason);
        }
    }

    internal readonly struct PasteCancelPacket(int requestId)
    {
        public int RequestId { get; } = requestId;

        public void Write(BinaryWriter writer) => writer.Write(RequestId);

        public static PasteCancelPacket Read(BinaryReader reader) => new(reader.ReadInt32());
    }

    internal sealed class PasteRequest
    {
        private const int MaxPasteRequestPayloadBytes = 32 * 1024 * 1024;
        private const int MaxExcludedRelativeCells = 500_000;

        public int RequestId { get; private set; }
        public Rectangle Bounds { get; private set; } = Rectangle.Empty;
        public Rectangle SourceArea { get; private set; } = Rectangle.Empty;
        public bool CutSource { get; private set; }
        public bool RetryOnMissingItems { get; private set; }
        public bool RetryOnInvalidPlacement { get; private set; }
        public ClearStyle ClearStrategy { get; private set; }
        public ClearItemDropStyle ClearItemDropStrategy { get; private set; }
        public int ArrowSpeed { get; private set; }
        public float ArrowMinSpeed { get; private set; }
        public float ArrowMaxSpeed { get; private set; }
        public PasteClientLogHints LogHints { get; private set; }
        public List<Point> ExcludedRelatives { get; private set; } = [];
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

            var hints = PasteClientHints.FromClientConfig();
            request.RequestId = requestId;
            request.Bounds = projection.Area;
            request.PayloadBytes = payloadBytes;
            request.RetryOnMissingItems = hints.RetryOnMissingItems;
            request.RetryOnInvalidPlacement = hints.RetryOnInvalidPlacement;
            request.ClearStrategy = hints.ClearStyle;
            request.ClearItemDropStrategy = hints.ClearItemDropStyle;
            request.ArrowSpeed = hints.ArrowSpeed;
            request.ArrowMinSpeed = hints.ArrowMinSpeed;
            request.ArrowMaxSpeed = hints.ArrowMaxSpeed;
            request.LogHints = PasteClientLogHints.FromClientConfig();
            request.SourceArea = projection.SourceArea;
            request.CutSource = !projection.SourceArea.IsEmpty && projection.CutSource;
            request.ExcludedRelatives = CollectExcludedRelatives(projection);
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
            writer.Write(RetryOnMissingItems);
            writer.Write(RetryOnInvalidPlacement);
            writer.Write((byte)ClearStrategy);
            writer.Write((byte)ClearItemDropStrategy);
            writer.Write(ArrowSpeed);
            writer.Write(ArrowMinSpeed);
            writer.Write(ArrowMaxSpeed);
            LogHints.Write(writer);
            writer.Write(ExcludedRelatives.Count);
            for (int i = 0; i < ExcludedRelatives.Count; i++)
            {
                writer.Write(ExcludedRelatives[i].X);
                writer.Write(ExcludedRelatives[i].Y);
            }

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
            request.RetryOnMissingItems = reader.ReadBoolean();
            request.RetryOnInvalidPlacement = reader.ReadBoolean();

            byte clearByte = reader.ReadByte();
            if (!TryParseClearStrategy(clearByte, out ClearStyle clearStrategy))
                throw new InvalidDataException($"Paste request has invalid clear strategy byte: {clearByte}.");
            request.ClearStrategy = clearStrategy;

            byte clearItemDropByte = reader.ReadByte();
            if (!TryParseClearItemDropStrategy(clearItemDropByte, out ClearItemDropStyle clearItemDropStrategy))
                throw new InvalidDataException($"Paste request has invalid clear item-drop strategy byte: {clearItemDropByte}.");
            request.ClearItemDropStrategy = clearItemDropStrategy;

            request.ArrowSpeed = reader.ReadInt32();
            request.ArrowMinSpeed = reader.ReadSingle();
            request.ArrowMaxSpeed = reader.ReadSingle();
            request.LogHints = PasteClientLogHints.Read(reader);

            int excludedCount = reader.ReadInt32();
            if (excludedCount < 0 || excludedCount > MaxExcludedRelativeCells)
                throw new InvalidDataException("Paste request has invalid excluded-cell count.");

            var excluded = new List<Point>(excludedCount);
            for (int i = 0; i < excludedCount; i++)
                excluded.Add(new Point(reader.ReadInt32(), reader.ReadInt32()));
            request.ExcludedRelatives = excluded;

            int payloadLen = reader.ReadInt32();
            if (payloadLen < 0 || payloadLen > MaxPasteRequestPayloadBytes)
                throw new InvalidDataException("Paste request has invalid payload length.");

            request.PayloadBytes = reader.ReadBytes(payloadLen);
            return request;
        }

        /// <summary>
        /// Builds a server-safe paste execution context from this request's Payload and placement metadata.
        /// </summary>
        public bool TryBuildExecutionContext(
            int ownerPlayerIndex,
            out PasteExecutionContext execution,
            out PasteRejectReason reason)
        {
            // --- Parse payload bytes, then assemble target origin / source / inclusion without Projection UI.

            execution = null!;
            reason = PasteRejectReason.None;

            if (Bounds.IsEmpty || PayloadBytes.Length == 0)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            if (!PayloadSerialization.TryParsePayloadDocument(
                    PayloadBytes,
                    out Payload payload,
                    out PayloadDocumentInfo docInfo,
                    out _)
                || !payload.IsDefined)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            if ((uint)ownerPlayerIndex >= (uint)Main.maxPlayers)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            try
            {
                execution = PasteExecutionContext.FromParsedRequest(
                    owner: ownerPlayerIndex,
                    correlationId: RequestId,
                    payload: payload,
                    targetOrigin: Bounds.Location,
                    sourceArea: SourceArea,
                    cutSource: !SourceArea.IsEmpty && CutSource,
                    excludedRelatives: ExcludedRelatives,
                    metadataModsLoaded: docInfo.ModsLoaded);
                return true;
            }
            catch (Exception)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }
        }

        private static List<Point> CollectExcludedRelatives(Projection projection)
        {
            var excluded = new List<Point>();
            if (projection.TargetTiles is null)
                return excluded;

            foreach (Blueprint.BlueprintCell cell in projection.TargetTiles)
            {
                if (!cell.Included)
                    excluded.Add(cell.Relative);
            }

            return excluded;
        }

        private static bool TryParseClearStrategy(byte value, out ClearStyle strategy)
        {
            if (value <= (byte)ClearStyle.Negative)
            {
                strategy = (ClearStyle)value;
                return true;
            }

            strategy = default;
            return false;
        }

        private static bool TryParseClearItemDropStrategy(byte value, out ClearItemDropStyle strategy)
        {
            if (value <= (byte)ClearItemDropStyle.Store)
            {
                strategy = (ClearItemDropStyle)value;
                return true;
            }

            strategy = default;
            return false;
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
