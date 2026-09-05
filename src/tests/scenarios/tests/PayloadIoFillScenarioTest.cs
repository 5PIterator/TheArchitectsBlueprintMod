#if DEBUG
#nullable enable

using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Command scenario for the payload IO fill-mode path.</summary>
    internal sealed class PayloadIoFillScenarioTest : IScenarioTest
    {
        public string Id => "sc10";
        public string Description => "Verifies payload browser IO APIs are reachable in-world.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireCursorProjection(player, Id, reporter, out Projection? projectionAtCursor))
                return;

            bool canQueryHighlight = PayloadBrowser.TryGetPayloadIoHighlight(player, out _);
            reporter.Info($"{Id}: cursor projection id={projectionAtCursor!.Id}.");
            if (!canQueryHighlight)
            {
                reporter.Pass(Id, "Payload IO fill-mode APIs reachable (no active highlight).");
                return;
            }

            reporter.Pass(Id, "Payload IO fill-mode APIs reachable (highlight query succeeded).");
        }
    }
}
#endif
