#if DEBUG
#nullable enable

using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Shared preconditions for in-game scenario tests.</summary>
    internal static class ScenarioTestGuards
    {
        /// <summary>Fails when the player is still on the main menu.</summary>
        public static bool RequireInWorld(IScenarioTestReporter reporter)
        {
            // --- Pass only after a world is loaded.
            if (!Main.gameMenu)
                return true;

            reporter.Fail("runner", "Join a world before running scenario tests.");
            return false;
        }

        /// <summary>Fails when the runner player is not the local client player.</summary>
        public static bool RequireLocalPlayer(Player player, IScenarioTestReporter reporter)
        {
            // --- Scenarios mutate local selection, projection, and inventory state.
            if (player.whoAmI == Main.myPlayer)
                return true;

            reporter.Fail("runner", "Scenario runner player is not the local player.");
            return false;
        }

        /// <summary>Fails when the session is not single-player.</summary>
        public static bool RequireSinglePlayer(string scenarioId, IScenarioTestReporter reporter)
        {
            // --- Paste scenarios drive DebugStepOperation, which is single-player only.
            if (Main.netMode == NetmodeID.SinglePlayer)
                return true;

            reporter.Fail(scenarioId, "Paste scenarios require single-player.");
            return false;
        }
    }
}
#endif
