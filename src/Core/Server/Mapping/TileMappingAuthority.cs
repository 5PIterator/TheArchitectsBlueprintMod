using Terraria;
using Terraria.ID;
using Terraria.Net;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Defines which processes may treat local SourceMaps / CompiledMaps on disk as authoritative.
    /// Remote multiplayer clients should not seed or read these for gameplay — server owner data wins.
    /// </summary>
    internal static class TileMappingAuthority
    {
        /// <summary>
        /// True when this MultiplayerClient is the in-process Host &amp; Play listen host.
        /// </summary>
        internal static bool IsLocalHostClient()
        {
            // --- Localhost alone is not enough: dedicated joiners also connect via 127.0.0.1.

            if (Main.netMode != NetmodeID.MultiplayerClient)
                return false;

            try
            {
                if (Netplay.Connection?.Socket?.GetRemoteAddress()?.IsLocalHost() != true)
                    return false;
            }
            catch
            {
                return false;
            }

            // Host & Play runs StartServer in this process; a dedicated-server joiner only has a client Connection.
            return HasActiveListenServerClients();
        }

        /// <summary>True when this process owns active <see cref="Netplay.Clients"/> slots (listen server).</summary>
        private static bool HasActiveListenServerClients()
        {
            // --- Scan server-side client slots; pure joiners leave them inactive.

            RemoteClient[] clients = Netplay.Clients;
            if (clients is null)
                return false;

            for (int i = 0; i < clients.Length; i++)
            {
                if (clients[i]?.IsActive == true)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True when this instance may copy template JSON to SavePath and load <see cref="ItrStorage"/> / compile maps from disk.
        /// </summary>
        internal static bool UsesLocalMappingFilesystem()
        {
            // Do not use Main.gameMenu: ModSystem.Load runs in menu when netMode is still typically SinglePlayer.

            if (Main.netMode == NetmodeID.SinglePlayer)
                return true;

            if (Main.netMode == NetmodeID.Server)
                return true;

            // Host & Play host needs local CompiledMaps when executing paste on the host client.
            if (IsLocalHostClient())
                return true;

            return false;
        }

        /// <summary>
        /// True when this game instance should execute paste packet handling (dedicated/listen server process).
        /// Host &amp; Play host pastes locally via <see cref="IsLocalHostClient"/> instead of handling packets here.
        /// </summary>
        internal static bool IsAuthoritativePastePacketSide()
        {
            return Main.netMode == NetmodeID.Server;
        }

        /// <summary>
        /// True when paste operations should tick on this process (SP, server, or Host &amp; Play host client).
        /// </summary>
        internal static bool ShouldTickPasteExecution()
        {
            if (Main.netMode == NetmodeID.SinglePlayer || Main.netMode == NetmodeID.Server)
                return true;

            return IsLocalHostClient();
        }
    }
}
