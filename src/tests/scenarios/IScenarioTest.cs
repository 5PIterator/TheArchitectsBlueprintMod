#if DEBUG
#nullable enable

using System.Collections;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Contract for in-game, command-triggered scenario checks.</summary>
    internal interface IScenarioTest
    {
        string Id { get; }
        string Description { get; }

        /// <summary>Runs the scenario; may yield across frames for long operations such as paste.</summary>
        IEnumerator Run(Player player, IScenarioTestReporter reporter);
    }
}
#endif
