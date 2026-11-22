#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PasteTests
{
    /// <summary>Places one stone, stocks required items from the manifest, pastes, and diffs.</summary>
    internal sealed class paste_P01 : ITestCase
    {
        public string Id => "paste_P01";
        public string Description => "1x1 stone copy, give required items, paste onto air, cell diff";

        public TestCaseType Type => TestCaseType.Positive;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- DefaultPaste config → ProjectionMoved → GiveRequiredItems → paste+diff+teardown.

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

            if (!InventoryHelpers.GiveRequiredItems(player, projection, reporter, Id))
            {
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator clear = StonePasteFixture.ClearWorldCells(player, source, target, reporter, Id);
                while (clear.MoveNext())
                    yield return clear.Current;
                yield break;
            }

            IEnumerator paste = StonePasteFixture.PasteDiffAndTeardown(
                player,
                projection,
                expected,
                Id,
                reporter,
                "1x1 stone paste matched the copied cell.");
            while (paste.MoveNext())
                yield return paste.Current;

            IEnumerator cleanup = StonePasteFixture.ClearWorldCells(player, source, target, reporter, Id);
            while (cleanup.MoveNext())
                yield return cleanup.Current;
        }
    }
}
#endif
