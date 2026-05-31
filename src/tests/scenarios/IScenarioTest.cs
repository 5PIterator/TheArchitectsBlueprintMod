#if DEBUG
#nullable enable

using Terraria;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Contract for in-game, command-triggered scenario checks.</summary>
    internal interface IScenarioTest
    {
        string Id { get; }
        string Description { get; }
        void Run(Player player, IScenarioTestReporter reporter);
    }
}
#endif
