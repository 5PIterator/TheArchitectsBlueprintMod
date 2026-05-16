#if DEBUG
#nullable enable

using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal interface ITestScenario
    {
        string Id { get; }
        string Description { get; }
        void Run(ITestReporter reporter, Player player);
    }
}
#endif
