using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core.Server
{
    /// <summary>
    /// Broadcasts paste Dig audio so every client plays one cue when a mutation applies.
    /// </summary>
    internal static class PasteWorldFxNetworking
    {
        /// <summary>Routes a Dig cue for SP, dedicated server, or listen-host paste execution.</summary>
        public static void Notify(int tileX, int tileY)
        {
            // --- SP plays locally; server broadcasts; listen-host relays through the server so all clients hear once.

            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                PlayLocal(tileX, tileY);
                return;
            }

            if (Main.netMode == NetmodeID.Server)
            {
                Broadcast(tileX, tileY);
                return;
            }

            // Listen-host applies paste on the MultiplayerClient; relay via server to peers + self.
            if (TileMappingAuthority.IsLocalHostClient())
                SendToServerForRelay(tileX, tileY);
        }

        /// <summary>Handles a PasteWorldFx packet on server (relay) or client (play).</summary>
        public static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Server rebroadcasts; clients play Dig at the tile center.

            int tileX = reader.ReadInt32();
            int tileY = reader.ReadInt32();

            if (Main.netMode == NetmodeID.Server)
            {
                Broadcast(tileX, tileY);
                return;
            }

            if (Main.netMode == NetmodeID.MultiplayerClient)
                PlayLocal(tileX, tileY);
        }

        /// <summary>Plays SoundID.Dig at the given tile center.</summary>
        public static void PlayLocal(int tileX, int tileY)
        {
            // --- Single Dig cue attenuated from the tile center.

            if (Main.dedServ)
                return;

            Vector2 position = new(tileX * 16f + 8f, tileY * 16f + 8f);
            SoundEngine.PlaySound(SoundID.Dig, position);
        }

        private static void Broadcast(int tileX, int tileY)
        {
            Mod mod = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>()!;
            ModPacket packet = mod.GetPacket();
            WritePacket(packet, tileX, tileY);
            packet.Send(-1);
        }

        private static void SendToServerForRelay(int tileX, int tileY)
        {
            Mod mod = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>()!;
            ModPacket packet = mod.GetPacket();
            WritePacket(packet, tileX, tileY);
            packet.Send();
        }

        private static void WritePacket(ModPacket packet, int tileX, int tileY)
        {
            packet.Write((byte)PasteNetPacketType.PasteWorldFx);
            packet.Write(tileX);
            packet.Write(tileY);
        }
    }
}
