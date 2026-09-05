using Terraria;

namespace TheArchitectsBlueprintMod.Debug
{
    /// <summary>Per-player client log prefs used by the server when forwarding OwnerChat lines.</summary>
    internal static class ClientLogRelayPrefs
    {
        private static readonly PasteClientLogHints?[] ByPlayer = new PasteClientLogHints?[Main.maxPlayers];

        /// <summary>Stores the latest diagnostic prefs from a player's paste request.</summary>
        public static void Set(int whoAmI, PasteClientLogHints hints)
        {
            // --- Bound-check the player slot, then replace any previous snapshot.

            if ((uint)whoAmI >= (uint)ByPlayer.Length)
                return;

            ByPlayer[whoAmI] = hints;
        }

        /// <summary>Clears stored prefs when a player leaves or should stop receiving relays.</summary>
        public static void Clear(int whoAmI)
        {
            // --- Drop the snapshot so stale prefs cannot leak to a reused slot.

            if ((uint)whoAmI >= (uint)ByPlayer.Length)
                return;

            ByPlayer[whoAmI] = null;
        }

        /// <summary>True when the server should forward this line to the given player.</summary>
        public static bool ShouldRelay(int whoAmI, string? category, TabLogLevel level)
        {
            // --- Require an explicit snapshot from that player's last paste request.

            if ((uint)whoAmI >= (uint)ByPlayer.Length)
                return false;

            PasteClientLogHints? hints = ByPlayer[whoAmI];
            return hints is { } h && h.ShouldRelay(category, level);
        }
    }
}
