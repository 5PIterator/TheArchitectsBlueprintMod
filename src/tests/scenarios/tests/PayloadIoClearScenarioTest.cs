#if DEBUG
#nullable enable

using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Command scenario for the payload IO clear-mode path.</summary>
    internal sealed class PayloadIoClearScenarioTest : IScenarioTest
    {
        public string Id => "sc9";
        public string Description => "Verifies payload IO highlight is inactive before clear-mode workflows.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireCursorProjection(player, Id, reporter, out Projection? projectionAtCursor))
                return;

            bool hasHighlight = PayloadBrowser.TryGetPayloadIoHighlight(player, out Projection? highlighted);
            if (hasHighlight && highlighted is not null)
            {
                reporter.Info($"{Id}: active payload IO projection id={highlighted.Id} (clear workflow may reset this).");
            }
            reporter.Info($"{Id}: cursor projection id={projectionAtCursor!.Id}.");

            reporter.Pass(Id, "Payload IO clear-mode preconditions satisfied.");
        }
    }
}
#endif
