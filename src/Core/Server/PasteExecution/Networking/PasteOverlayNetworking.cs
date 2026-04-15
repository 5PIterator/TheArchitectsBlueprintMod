using System;
using System.Collections.Generic;
using System.IO;
using ArchitectsStencilMod;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.Client;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Net;

namespace ArchitectsStencilMod.Core.Server
{
    /// <summary>
    /// Syncs in-progress paste highlight tiles (world coordinates only). Tile data for a paste is sent via
    /// <see cref="PasteRequestV1"/> as compact <see cref="ArchitectsStencilMod.Core.Client.PayloadImport.PayloadDocument"/> JSON.
    /// </summary>
    internal static class PasteOverlayNetworking
    {
        private static int[]? _lastSentSnapshotCountByPlayer;

        internal static void ResetRemoteOverlayBroadcastState()
        {
            if (_lastSentSnapshotCountByPlayer is not null)
                Array.Clear(_lastSentSnapshotCountByPlayer);
        }

        internal static void BroadcastPasteOverlaysToRemoteClients(PastingSystem system)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            _lastSentSnapshotCountByPlayer ??= new int[Main.maxPlayers];

            Mod mod = ModContent.GetInstance<ArchitectsStencilMod>()!;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                if (!Main.player[i].active || !Netplay.Clients[i].IsConnected())
                    continue;

                var list = new List<PasteOverlaySnapshot>();
                foreach (var op in system.ActiveOperations)
                {
                    if (op.Projection.OwnerPlayerIndex != i)
                        continue;
                    if (op.TryBuildOverlaySnapshot(out PasteOverlaySnapshot snap))
                        list.Add(snap);
                }

                if (list.Count == 0 && _lastSentSnapshotCountByPlayer[i] == 0)
                    continue;

                ModPacket packet = mod.GetPacket();
                packet.Write((byte)PasteNetPacketType.PasteOverlaySyncV1);
                WritePasteOverlaySync(packet, list);
                packet.Send(i);
                _lastSentSnapshotCountByPlayer[i] = list.Count;
            }
        }

        internal static void WritePasteOverlaySync(BinaryWriter writer, IReadOnlyList<PasteOverlaySnapshot> snapshots)
        {
            writer.Write(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                PasteOverlaySnapshot s = snapshots[i];
                writer.Write(s.OverlayId);
                writer.Write(s.IsPlacePhase);
                WritePointArray(writer, s.Pending);
                WritePointArray(writer, s.Clear);
                WritePointArray(writer, s.Place);
                WritePointArray(writer, s.Done);
            }
        }

        internal static PasteOverlaySnapshot[] ReadPasteOverlaySync(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 256)
                throw new InvalidDataException("Invalid paste overlay snapshot count.");

            var arr = new PasteOverlaySnapshot[count];
            for (int i = 0; i < count; i++)
            {
                int overlayId = reader.ReadInt32();
                bool isPlacePhase = reader.ReadBoolean();
                Point[] pending = ReadPointArray(reader);
                Point[] clear = ReadPointArray(reader);
                Point[] place = ReadPointArray(reader);
                Point[] done = ReadPointArray(reader);
                arr[i] = new PasteOverlaySnapshot
                {
                    OverlayId = overlayId,
                    IsPlacePhase = isPlacePhase,
                    Pending = pending,
                    Clear = clear,
                    Place = place,
                    Done = done
                };
            }

            return arr;
        }

        internal static void HandlePasteOverlaySyncClient(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            PasteOverlaySnapshot[] snapshots = ReadPasteOverlaySync(reader);
            ModContent.GetInstance<PasteOverlayMonitor>().ApplyRemoteSnapshots(snapshots);
        }

        private static void WritePointArray(BinaryWriter writer, Point[] pts)
        {
            writer.Write(pts.Length);
            for (int i = 0; i < pts.Length; i++)
            {
                writer.Write(pts[i].X);
                writer.Write(pts[i].Y);
            }
        }

        private static Point[] ReadPointArray(BinaryReader reader)
        {
            int len = reader.ReadInt32();
            if (len < 0 || len > 4096)
                throw new InvalidDataException("Invalid paste overlay point array length.");

            var pts = new Point[len];
            for (int i = 0; i < len; i++)
            {
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                pts[i] = new Point(x, y);
            }

            return pts;
        }
    }
}
