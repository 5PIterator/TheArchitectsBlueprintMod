using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Defines which processes may treat local SourceMaps / CompiledMaps on disk as authoritative.
    /// Remote multiplayer clients should not seed or read these for gameplay — server owner data wins.
    /// </summary>
    internal static class TileMappingAuthority
    {
        /// <summary>
        /// True when this instance may copy template JSON to SavePath and load <see cref="ItrStorage"/> / compile maps from disk.
        /// False for joining <see cref="NetmodeID.MultiplayerClient"/> players (not listen-server host).
        /// </summary>
        internal static bool UsesLocalMappingFilesystem()
        {
            // Do not use Main.gameMenu: ModSystem.Load runs in menu when netMode is still typically SinglePlayer.

            if (Main.netMode == NetmodeID.SinglePlayer)
                return true;

            if (Main.netMode == NetmodeID.Server)
                return true;

            // Listen-server host: same machine runs server logic; needs local CompiledMaps for paste.
            if (Main.netMode == NetmodeID.MultiplayerClient && Main.myPlayer == 0)
                return true;

            return false;
        }

        /// <summary>
        /// True when this game instance should execute server-side paste packet handling (dedicated server or listen host).
        /// </summary>
        internal static bool IsAuthoritativePastePacketSide()
        {
            if (Main.netMode == NetmodeID.Server)
                return true;

            if (Main.netMode == NetmodeID.MultiplayerClient && Main.myPlayer == 0)
                return true;

            return false;
        }
    }
}
