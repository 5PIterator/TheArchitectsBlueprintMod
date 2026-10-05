#if DEBUG
#nullable enable

using System.Collections;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PasteTests
{
    /// <summary>Pastes the medium vanilla fixture and diffs the target world.</summary>
    internal sealed class MediumScenario : IScenarioTest
    {
        private const string FixtureFileName = "TMM_MediumVanilla.tab";

        public string Id => "paste.medium";
        public string Description => "Pastes TMM_MediumVanilla.tab and diffs the target.";

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
                offsetX: 40,
                offsetY: -30,
                "Medium fixture paste matched the source payload.");
            while (run.MoveNext())
                yield return run.Current;
        }
    }
}
#endif
