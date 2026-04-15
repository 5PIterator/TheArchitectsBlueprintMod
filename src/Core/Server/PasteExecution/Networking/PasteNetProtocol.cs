using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core.Client.PayloadImport;
using ArchitectsStencilMod.Debug;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core.Server
{
    internal enum PasteNetPacketType : byte
    {
        PasteRequestV1 = 1,
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

    internal sealed class PasteRequestV1
    {
        private const int MaxPasteRequestJsonBytes = 32 * 1024 * 1024;

        public int RequestId { get; private set; }
        public Rectangle Bounds { get; private set; } = Rectangle.Empty;
        public bool HasSourceArea { get; private set; }
        public Rectangle SourceArea { get; private set; } = Rectangle.Empty;
        public bool CutSource { get; private set; }
        public bool ForceTiles { get; private set; }
        public bool TransparentPaste { get; private set; }
        public int PasteStepSize { get; private set; }
        public PayloadDocument Document { get; private set; } = null!;

        private PasteRequestV1() { }

        public static bool TryCreate(Projection projection, int requestId, out PasteRequestV1 request, out PasteRejectReason reason)
        {
            request = new PasteRequestV1();
            reason = PasteRejectReason.None;

            if (projection is null || !projection.IsDefined || !projection.HasPayload)
            {
                reason = PasteRejectReason.InvalidProjection;
                return false;
            }

            Point payloadSize = projection.PayloadSize;
            if (payloadSize.X <= 0 || payloadSize.Y <= 0)
            {
                reason = PasteRejectReason.InvalidProjection;
                return false;
            }

            PayloadDocument doc = projection.ToPayloadDocumentForNetwork();
            if (doc.Payload.Count == 0)
            {
                reason = PasteRejectReason.InvalidProjection;
                return false;
            }

            if (doc.Width != projection.Bounds.Width || doc.Height != projection.Bounds.Height)
            {
                reason = PasteRejectReason.InvalidProjection;
                return false;
            }

            request.RequestId = requestId;
            request.Bounds = projection.Bounds;
            request.Document = doc;
            var clientCfg = ModContent.GetInstance<ClientPasteConfig>();
            var serverCfg = ModContent.GetInstance<PastingConfig>();
            request.ForceTiles = clientCfg.ForceTiles;
            request.TransparentPaste = clientCfg.TransparentPaste;
            request.PasteStepSize = Math.Max(1, Math.Min(clientCfg.PasteStepSize, serverCfg.MaxPasteStepSize));
            request.HasSourceArea = !projection.SourceArea.IsEmpty;
            request.SourceArea = projection.SourceArea;
            request.CutSource = request.HasSourceArea && projection.CutSource;

            return true;
        }

        public void Write(BinaryWriter writer)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(PayloadJson.Serialize(Document));
            if (utf8.Length > MaxPasteRequestJsonBytes)
            {
                throw new InvalidOperationException("Paste request payload JSON exceeds max size.");
            }

            writer.Write(RequestId);
            WriteRectangle(writer, Bounds);
            writer.Write(HasSourceArea);
            if (HasSourceArea)
            {
                WriteRectangle(writer, SourceArea);
            }
            writer.Write(CutSource);
            writer.Write(ForceTiles);
            writer.Write(TransparentPaste);
            writer.Write(PasteStepSize);
            writer.Write(utf8.Length);
            writer.Write(utf8);
        }

        public static PasteRequestV1 Read(BinaryReader reader)
        {
            var request = new PasteRequestV1
            {
                RequestId = reader.ReadInt32(),
                Bounds = ReadRectangle(reader),
                HasSourceArea = reader.ReadBoolean(),
            };

            if (request.HasSourceArea)
            {
                request.SourceArea = ReadRectangle(reader);
            }

            request.CutSource = reader.ReadBoolean();
            request.ForceTiles = reader.ReadBoolean();
            request.TransparentPaste = reader.ReadBoolean();
            request.PasteStepSize = reader.ReadInt32();

            int jsonLen = reader.ReadInt32();
            if (jsonLen < 0 || jsonLen > MaxPasteRequestJsonBytes)
            {
                throw new InvalidDataException("Paste request has invalid JSON length.");
            }

            byte[] utf8 = reader.ReadBytes(jsonLen);
            string json = Encoding.UTF8.GetString(utf8);
            request.Document = PayloadJson.Deserialize(json);
            return request;
        }

        public bool TryBuildProjection(int ownerPlayerIndex, out Projection projection, out PasteRejectReason reason)
        {
            projection = null;
            reason = PasteRejectReason.None;

            if (Bounds.IsEmpty || Document is null || Document.Payload.Count == 0)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            if (Bounds.Width != Document.Width || Bounds.Height != Document.Height)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            var selected = new List<Blueprint.BoundedTile>(Document.Payload.Count);
            var payload = new List<Projection.PayloadEntry>(Document.Payload.Count);
            var seen = new HashSet<Point>();
            for (int i = 0; i < Document.Payload.Count; i++)
            {
                PayloadCell cell = Document.Payload[i];
                Point relative = new(cell.X, cell.Y);
                if ((uint)relative.X >= (uint)Document.Width || (uint)relative.Y >= (uint)Document.Height)
                {
                    reason = PasteRejectReason.InvalidPacket;
                    return false;
                }

                if (!seen.Add(relative))
                {
                    reason = PasteRejectReason.InvalidPacket;
                    return false;
                }

                selected.Add(new Blueprint.BoundedTile(relative, selected: true));
                payload.Add(new Projection.PayloadEntry(relative, cell.Facets));
            }

            if (selected.Count == 0)
            {
                reason = PasteRejectReason.InvalidPacket;
                return false;
            }

            projection = new Projection(cutSource: false)
            {
                OwnerPlayerIndex = ownerPlayerIndex,
                Id = RequestId
            };

            projection.Define(Bounds);
            projection.LoadPayloadFromImport(
                payload,
                Document.Width,
                Document.Height,
                selected,
                cutSource: HasSourceArea && CutSource,
                sourceArea: HasSourceArea && CutSource ? SourceArea : null);

            // #region agent log
            if (Document.Width * Document.Height <= 64)
            {
                for (int py = 0; py < Document.Height; py++)
                {
                    for (int px = 0; px < Document.Width; px++)
                    {
                        var rel = new Point(px, py);
                        if (!projection.TryGetPayloadData(rel, out var facets))
                            continue;
                        AgentNdjsonLog.Write(
                            "H5",
                            "PasteRequestV1.TryBuildProjection",
                            "import_cell",
                            new
                            {
                                rx = px,
                                ry = py,
                                blockType = facets?.Block?.TileType,
                                blockHas = facets?.Block?.HasTile,
                            });
                    }
                }
            }
            // #endregion

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
