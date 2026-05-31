#if DEBUG
#nullable enable

using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Verifies core mod systems are registered and reachable in-game.</summary>
    internal sealed class ModSystemsLoadedScenarioTest : IScenarioTest
    {
        public string Id => "sc2";
        public string Description => "Blueprint, projection, and selection systems resolve via ModContent.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;

            BlueprintSystem blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
            ProjectionSystem projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();

            if (blueprintSystem is null || projectionSystem is null || selectionSystem is null)
            {
                reporter.Fail(Id, "One or more core systems failed to resolve.");
                return;
            }

            reporter.Pass(Id, "Core client systems are loaded.");
        }
    }
}
#endif
