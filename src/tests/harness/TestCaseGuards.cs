#if DEBUG
#nullable enable

using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Shared preconditions for in-game Test Cases.</summary>
    internal static class TestCaseGuards
    {
        /// <summary>Fails when the player is still on the main menu.</summary>
        public static bool RequireInWorld(ITestCaseReporter reporter)
        {
            // --- Pass only after a world is loaded.
            if (!Main.gameMenu)
                return true;

            reporter.Fail("runner", "Join a world before running Test Cases.");
            return false;
        }

        /// <summary>Fails when the runner player is not the local client player.</summary>
        public static bool RequireLocalPlayer(Player player, ITestCaseReporter reporter)
        {
            // --- Cases mutate local selection, projection, and inventory state.
            if (player.whoAmI == Main.myPlayer)
                return true;

            reporter.Fail("runner", "Test Case runner player is not the local player.");
            return false;
        }

        /// <summary>Fails when the universal test chamber origin has not been set via Debug Stick CHM.</summary>
        public static bool RequireChamberConfigured(ITestCaseReporter reporter)
        {
            // --- Delegate to TestChamberSystem so layout slots are never used unanchored.
            if (TestChamberSystem.Instance.RequireConfigured(out string? error))
                return true;

            reporter.Fail("runner", error ?? "Test chamber origin is not set.");
            return false;
        }

        /// <summary>Fails unless this client is a multiplayer client in a loaded world session.</summary>
        public static bool RequireMultiplayerClient(string caseTag, ITestCaseReporter reporter)
        {
            // --- Topology-neutral MP Cases run on listen host or remote joiner clients, not dedicated.
            if (Main.netMode == NetmodeID.MultiplayerClient && !Main.dedServ)
                return true;

            reporter.Fail(
                caseTag,
                "Requires a multiplayer client (Host & Play host, Host & Play joiner, or dedicated-server client).");
            return false;
        }

        /// <summary>Fails unless this client is the in-process Host &amp; Play listen host.</summary>
        public static bool RequireLocalHostClient(string caseTag, ITestCaseReporter reporter)
        {
            // --- Listen-host paste uses local ExecutePaste; SP / remote / dedicated must not run this path.
            if (TileMappingAuthority.IsLocalHostClient())
                return true;

            reporter.Fail(
                caseTag,
                "Requires Host & Play listen host (not single-player, remote joiner, or dedicated server).");
            return false;
        }
    }
}
#endif
