using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.Server
{
    /// <summary>
    /// Client requests a paste manifest; the authoritative side builds it and replies with a paged chat payload.
    /// </summary>
    internal static class PasteManifestNetworking
    {
        private const int ManifestRequestPacketCapacity = 1_048_576;

        /// <summary>Sends a ManifestRequest for the live projection, or builds locally in single-player.</summary>
        public static void RequestPrint(Mod mod, Projection projection)
        {
            // --- SP builds in-process; MP clients only ship planning bytes and wait for ManifestResponse.

            if (Main.dedServ)
                return;

            if (projection is not { IsDefined: true })
            {
                ManifestPrinter.EmitLocal(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.NoProjection"),
                    ManifestPrinter.WarningColor);
                TABLog.Flush();
                return;
            }

            if (!projection.HasPayload)
            {
                ManifestPrinter.EmitLocal(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.NoPayload"),
                    ManifestPrinter.WarningColor);
                TABLog.Flush();
                return;
            }

            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                DeliverBuiltManifest(mod, toClient: -1, PasteExecutionContext.FromProjection(projection));
                return;
            }

            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            if (!PasteRequest.TryCreate(projection, requestId: 0, out PasteRequest request, out PasteRejectReason reason))
            {
                ManifestPrinter.EmitLocal(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.NoPayload"),
                    ManifestPrinter.WarningColor);
                TABLog.Warn($"ManifestRequest create failed reason={reason}", TABLog.ManifestCategory);
                TABLog.Flush();
                return;
            }

            ModPacket packet = mod.GetPacket(ManifestRequestPacketCapacity);
            packet.Write((byte)PasteNetPacketType.ManifestRequest);
            request.Write(packet);
            packet.Send();
        }

        /// <summary>Handles ManifestRequest on the dedicated or listen server.</summary>
        public static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Rebuild execution from the request, build demand, then send one ManifestResponse.

            if (Main.netMode != NetmodeID.Server)
                return;

            PasteRequest request;
            try
            {
                request = PasteRequest.Read(reader);
            }
            catch (IOException ex)
            {
                TABLog.Warn($"ManifestRequest read failed whoAmI={whoAmI}: {ex.Message}", TABLog.ManifestCategory);
                return;
            }

            if (!request.TryBuildExecutionContext(whoAmI, out PasteExecutionContext execution, out PasteRejectReason reason))
            {
                PasteNetworking.SendOwnerChatLine(
                    mod,
                    whoAmI,
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Manifest.NoPayload"),
                    category: string.Empty,
                    ManifestPrinter.WarningColor);
                TABLog.Warn($"ManifestRequest build failed whoAmI={whoAmI} reason={reason}", TABLog.ManifestCategory);
                return;
            }

            DeliverBuiltManifest(mod, whoAmI, execution);
        }

        /// <summary>Handles ManifestResponse on a multiplayer client.</summary>
        public static void HandleResponse(BinaryReader reader)
        {
            // --- Deserialize all rows, then page the first screen of chat locally.

            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            int count = reader.ReadInt32();
            if (count < 0 || count > 100_000)
            {
                TABLog.Warn($"ManifestResponse rejected lineCount={count}", TABLog.ManifestCategory);
                return;
            }

            var lines = new List<(string Text, Color Color)>(count);
            for (int i = 0; i < count; i++)
            {
                string message = reader.ReadString();
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                byte a = reader.ReadByte();
                lines.Add((message, new Color(r, g, b, a)));
            }

            ManifestPrinter.ReceiveAndPrintFirstPage(lines);
        }

        /// <summary>Builds the manifest and delivers formatted lines to the owner client or local chat.</summary>
        private static void DeliverBuiltManifest(Mod mod, int toClient, PasteExecutionContext execution)
        {
            // --- Authoritative map + inventory snapshot, then one response (SP local or MP packet).

            Manifest manifest = ManifestBuilder.Build(execution);
            string playerName = ResolveOwnerName(execution.Owner);
            var lines = new List<(string Text, Color Color)>(64);
            ManifestPrinter.EnumerateLines(manifest, playerName, (message, color) => lines.Add((message, color)));

            if (Main.netMode == NetmodeID.Server)
            {
                SendManifestResponse(mod, toClient, lines);
                return;
            }

            ManifestPrinter.ReceiveAndPrintFirstPage(lines);
        }

        /// <summary>Sends every formatted manifest line to one client in a single packet.</summary>
        private static void SendManifestResponse(Mod mod, int toClient, IReadOnlyList<(string Text, Color Color)> lines)
        {
            // --- One ManifestResponse replaces hundreds of PasteOwnerChat packets.

            if (Main.netMode != NetmodeID.Server || toClient < 0 || toClient >= Main.maxPlayers)
                return;

            ModPacket packet = mod.GetPacket();
            packet.Write((byte)PasteNetPacketType.ManifestResponse);
            packet.Write(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                (string text, Color color) = lines[i];
                packet.Write(text ?? string.Empty);
                packet.Write(color.R);
                packet.Write(color.G);
                packet.Write(color.B);
                packet.Write(color.A);
            }

            packet.Send(toClient);
        }

        private static string ResolveOwnerName(int owner)
        {
            if (owner >= 0 && owner < Main.player.Length)
            {
                Player player = Main.player[owner];
                if (player is { active: true } && !string.IsNullOrWhiteSpace(player.name))
                    return player.name;
            }

            return Main.LocalPlayer?.name ?? string.Empty;
        }
    }
}
