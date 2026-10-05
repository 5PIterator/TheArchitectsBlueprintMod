#if DEBUG
#nullable enable

using System.Collections;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PasteTests
{
    /// <summary>Pastes the layer-test vanilla fixture and diffs the target world.</summary>
    internal sealed class LayerScenario : IScenarioTest
    {
        private const string FixtureFileName = "TTP_LayerTestVanilla.tab";

        public string Id => "paste.layer";
        public string Description => "Pastes TTP_LayerTestVanilla.tab and diffs the target.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Load fixture, place empty projection at target size, paste, diff, teardown.

            if (!ScenarioTestGuards.RequireSinglePlayer(Id, reporter))
                yield break;

            IEnumerator run = FixturePasteScenario.Run(
                player,
                reporter,
                Id,
                FixtureFileName,
                offsetX: 10,
                offsetY: -20,
                "Layer fixture paste matched the source payload.");
            while (run.MoveNext())
                yield return run.Current;
        }
    }
}
#endif
