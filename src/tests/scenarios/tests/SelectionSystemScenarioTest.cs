#if DEBUG
#nullable enable

using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Checks selection system is reachable and reports current selection state.</summary>
    internal sealed class SelectionSystemScenarioTest : IScenarioTest
    {
        public string Id => "sc3";
        public string Description => "SelectionSystem resolves and exposes a selection reference.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;

            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();
            if (selectionSystem is null)
            {
                reporter.Fail(Id, "SelectionSystem failed to resolve.");
                return;
            }

            Selection? selection = selectionSystem.GetSelection();
            if (selection is null || !selection.IsDefined)
            {
                reporter.Pass(Id, "SelectionSystem reachable (no active selection).");
                return;
            }

            reporter.Pass(Id, $"SelectionSystem reachable (active area {selection.Area.Width}x{selection.Area.Height}).");
        }
    }
}
#endif
