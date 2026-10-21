#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PasteTests
{
    /// <summary>Paste with cut enabled: stocks materials, fills target, clears source.</summary>
    internal sealed class paste_P02 : ITestCase
    {
        public string Id => "paste_P02";
        public string Description => "1x1 cut paste with required items: target matches and source clears";

        public TestCaseType Type => TestCaseType.Positive;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- DefaultPaste config → ProjectionMoved → cut → materials → paste+diff.

            IEnumerator body = RunBody(player, reporter);
            IEnumerator run = PasteConfigFixture.RunWith(PasteConfigProfile.DefaultPaste, body);
            while (run.MoveNext())
                yield return run.Current;
        }

        private IEnumerator RunBody(Player player, ITestCaseReporter reporter)
        {
            Core.Projection? projection = null;
            Payload expected = Payload.Empty;
            Point source = default;
            Point target = default;
            bool joined = false;
            string? setupError = null;

            IEnumerator join = StonePasteFixture.JoinProjectionMoved(
                player,
                reporter,
                Id,
                (ok, proj, payload, src, tgt, err) =>
                {
                    joined = ok;
                    projection = proj;
                    expected = payload;
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

            projection.SetCut(true);
            if (!projection.CutSource)
            {
                reporter.Fail(Id, "Failed to enable CutSource.");
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator clearCut = StonePasteFixture.ClearWorldCells(player, source, target, reporter, Id);
                while (clearCut.MoveNext())
                    yield return clearCut.Current;
                yield break;
            }

            if (!InventoryHelpers.GiveRequiredItems(player, projection, reporter, Id))
            {
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator clearMats = StonePasteFixture.ClearWorldCells(player, source, target, reporter, Id);
                while (clearMats.MoveNext())
                    yield return clearMats.Current;
                yield break;
            }

            IEnumerator paste = StonePasteFixture.PasteDiffAndTeardown(
                player,
                projection,
                expected,
                Id,
                reporter,
                "Cut paste matched the target and cleared the source.",
                alsoAssertSourceCleared: true,
                sourceTile: source);
            while (paste.MoveNext())
                yield return paste.Current;

            IEnumerator cleanup = StonePasteFixture.ClearWorldCells(player, source, target, reporter, Id);
            while (cleanup.MoveNext())
                yield return cleanup.Current;
        }
    }
}
#endif
