#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PasteTests
{
    /// <summary>Strips required paste materials and asserts place does not write tiles.</summary>
    internal sealed class paste_N03 : ITestCase
    {
        public string Id => "paste_N03";
        public string Description => "Removes required items then expects paste to leave the target empty";

        public TestCaseType Type => TestCaseType.Negative;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- DefaultPaste (no retry) → strip materials → paste → target stays air.

            IEnumerator body = RunBody(player, reporter);
            IEnumerator run = PasteConfigFixture.RunWith(PasteConfigProfile.DefaultPaste, body);
            while (run.MoveNext())
                yield return run.Current;
        }

        private IEnumerator RunBody(Player player, ITestCaseReporter reporter)
        {
            Core.Projection? projection = null;
            Point source = default;
            Point target = default;
            bool joined = false;
            string? setupError = null;

            IEnumerator join = StonePasteFixture.JoinProjectionMoved(
                player,
                reporter,
                Id,
                (ok, proj, _, src, tgt, err) =>
                {
                    joined = ok;
                    projection = proj;
                    source = src;
                    target = tgt;
                    setupError = err;
                });
            while (join.MoveNext())
                yield return join.Current;

            if (!joined || projection is null)
            {
                reporter.Fail(Id, setupError ?? "Stone paste setup failed.");
                yield break;
            }

            // Ensure something is present to strip, then remove required lines.
            InventoryHelpers.GiveRequiredItems(player, projection, reporter: null, caseTag: Id);
            if (!InventoryHelpers.RemoveRequiredItems(player, projection, reporter, Id))
            {
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator clear = StonePasteFixture.ClearWorldCells(player, source, target, reporter, Id);
                while (clear.MoveNext())
                    yield return clear.Current;
                yield break;
            }

            IEnumerator paste = StonePasteFixture.PasteExpectNoPlaceAndCleanup(
                player,
                projection,
                Id,
                reporter,
                "Paste left the target empty without required materials.",
                clearSource: source,
                clearTarget: target);
            while (paste.MoveNext())
                yield return paste.Current;
        }
    }
}
#endif
