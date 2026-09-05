#if DEBUG
#nullable enable

using Terraria;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Basic in-world sanity check for command-based scenario plumbing.</summary>
    internal sealed class LocalPlayerScenarioTest : IScenarioTest
    {
        public string Id => "sc1";
        public string Description => "Verifies the command executes in local player context.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;

            reporter.Pass(Id, "Local player context is valid.");
        }
    }
}
#endif
