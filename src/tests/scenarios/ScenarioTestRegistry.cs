#if DEBUG
#nullable enable

using System.Collections.Generic;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Source-of-truth list of in-game scenario tests.</summary>
    internal static class ScenarioTestRegistry
    {
        private static readonly IReadOnlyList<IScenarioTest> _scenarios =
        [
            new LocalPlayerScenarioTest(),
            new ModSystemsLoadedScenarioTest(),
            new SelectionSystemScenarioTest(),
            new PayloadTagRoundtripScenarioTest(),
            new PayloadTagIOStreamScenarioTest(),
            new PayloadJsonImportScenarioTest(),
            new PayloadFromSparseScenarioTest(),
            new PayloadCompatibilityScenarioTest(),
            new PayloadIoClearScenarioTest(),
            new PayloadIoFillScenarioTest(),
            new PayloadPasteDiffScenarioTest(),
        ];

        public static IReadOnlyList<IScenarioTest> Scenarios => _scenarios;
    }
}
#endif
