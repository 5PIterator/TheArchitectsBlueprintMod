#if DEBUG
#nullable enable

using System.Collections.Generic;
using System.Text;
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

        /// <summary>Abandons unless this client is a multiplayer client in a loaded world session.</summary>
        public static bool RequireMultiplayerClient(string caseTag, ITestCaseReporter reporter)
        {
            // --- Topology-neutral MP Cases run on listen host or remote joiner clients, not dedicated.
            if (Main.netMode == NetmodeID.MultiplayerClient && !Main.dedServ)
                return true;

            reporter.Abandon(
                caseTag,
                "Requires a multiplayer client (Host & Play host, Host & Play joiner, or dedicated-server client).");
            return false;
        }

        /// <summary>Abandons unless this client is the in-process Host &amp; Play listen host.</summary>
        public static bool RequireLocalHostClient(string caseTag, ITestCaseReporter reporter)
        {
            // --- Listen-host paste uses local ExecutePaste; SP / remote / dedicated must not run this path.
            if (TileMappingAuthority.IsLocalHostClient())
                return true;

            reporter.Abandon(
                caseTag,
                "Requires Host & Play listen host (not single-player, remote joiner, or dedicated server).");
            return false;
        }

        /// <summary>Abandons when this role cannot load CompiledMaps from disk for paste lookups.</summary>
        public static bool RequireInteractionMapLoadable(string caseTag, ITestCaseReporter reporter)
        {
            // --- Remote multiplayer clients never read InteractionMap files from disk.
            if (ItrStorage.CanLoadPasteInteractionMaps())
                return true;

            reporter.Abandon(
                caseTag,
                "Requires single-player, dedicated server, or Host & Play listen host to load InteractionMap files.");
            return false;
        }

        /// <summary>Abandons when any required mod is not loaded in this session.</summary>
        public static bool RequireModsLoaded(string caseTag, ITestCaseReporter reporter, params string[] modNames)
        {
            // --- Cases that assert modded .itr rows cannot invent missing mods at runtime.

            if (modNames is null || modNames.Length == 0)
                return true;

            List<string> missing = [];
            for (int i = 0; i < modNames.Length; i++)
            {
                string name = modNames[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (!ModLoader.HasMod(name))
                    missing.Add(name);
            }

            if (missing.Count == 0)
                return true;

            var sb = new StringBuilder("Requires loaded mod(s): ");
            for (int i = 0; i < missing.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(missing[i]);
            }

            reporter.Abandon(caseTag, sb.ToString());
            return false;
        }
    }
}
#endif
