#if DEBUG
#nullable enable

using System.Collections;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.TestCases.PasteTests
{
    /// <summary>
    /// Pastes <c>TABModularTest.tab</c> (3×4 grid of 7×7 layer modules) and diffs the target world.
    /// Module map from bottom-left: entity, block, wall, frame / unloaded, plant, wire, liquid / empty, slope, paint, tree.
    /// </summary>
    internal sealed class paste_P04 : ITestCase
    {
        private const string FixtureFileName = "TABModularTest.tab";

        public string Id => "paste_P04";
        public string Description => "Pastes TABModularTest.tab (3x4 of 7x7 layer modules) and diffs the target";

        public TestCaseType Type => TestCaseType.Positive;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- DefaultPaste config → TabPasteFixture at FixtureOrigin.

            IEnumerator body = TabPasteFixture.Run(
                player,
                reporter,
                Id,
                FixtureFileName,
                "Modular layer fixture paste matched the source payload.");
            IEnumerator run = PasteConfigFixture.RunWith(PasteConfigProfile.DefaultPaste, body);
            while (run.MoveNext())
                yield return run.Current;
        }
    }
}
#endif
