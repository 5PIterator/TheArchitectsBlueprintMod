#if DEBUG
#nullable enable

using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Compares source payload cells to target world cells after paste.</summary>
    internal sealed class PayloadPasteDiffScenarioTest : IScenarioTest
    {
        private const int MaxDiffLines = 20;
        private const string FixtureFileName = "TTP_LayerTestVanilla.tab";

        public string Id => "sc11";
        public string Description => "Compares source payload to target tiles after paste.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireCursorProjection(player, Id, reporter, out Projection? projection))
                return;
            if (projection is null)
            {
                reporter.Fail(Id, "No projection under cursor.");
                return;
            }

            if (!ScenarioPayloadFixturePaths.TryResolve(FixtureFileName, out string fixturePath, out string? pathError))
            {
                reporter.Fail(Id, pathError ?? "Fixture path resolve failed.");
                return;
            }

            if (!PayloadSerialization.TryReadPayloadFromFile(fixturePath, out Payload source, out PayloadDocumentInfo info, out string? readError))
            {
                reporter.Fail(Id, readError ?? "Fixture payload read failed.");
                return;
            }

            projection.SetPayload(source, info.ModsLoaded);
            if (!projection.HasPayload)
            {
                reporter.Fail(Id, "Projection payload is not defined after fixture load.");
                return;
            }

            Payload expected = Payload.FromClone(projection.Payload);
            int expectedWidth = expected.Width;
            int expectedHeight = expected.Height;
            reporter.Info($"{Id}: fixture loaded from {fixturePath} ({expectedWidth}x{expectedHeight}, sparse={expected.Entries.Count}).");

            try
            {
                if (!ScenarioPasteHelpers.RunPasteAndAwait(projection, reporter, Id, out string? pasteError))
                {
                    reporter.Fail(Id, pasteError ?? "Paste execution failed.");
                    return;
                }

                Payload actual = Payload.FromWorld(projection.TargetArea);
                ScenarioPayloadDiffResult diff = ScenarioPayloadDiff.Compare(expected, actual, projection.TargetArea);
                reporter.Info(
                    $"{Id}: compared={diff.ComparedCells}, missing={diff.MissingCells}, unexpected={diff.UnexpectedCells}, fieldMismatch={diff.Mismatches.Count}.");

                if (diff.MismatchCount == 0)
                {
                    reporter.Pass(Id, "Source payload matches target world tiles.");
                    return;
                }

                int toShow = diff.Mismatches.Count < MaxDiffLines ? diff.Mismatches.Count : MaxDiffLines;
                for (int i = 0; i < toShow; i++)
                {
                    ScenarioPayloadDiffItem mismatch = diff.Mismatches[i];
                    reporter.Info(
                        $"{Id}: rel=({mismatch.Relative.X},{mismatch.Relative.Y}) world=({mismatch.World.X},{mismatch.World.Y}) " +
                        $"{mismatch.Facet} expected={mismatch.Expected} actual={mismatch.Actual}");
                }

                if (diff.Mismatches.Count > MaxDiffLines)
                    reporter.Info($"{Id}: ... {diff.Mismatches.Count - MaxDiffLines} additional field mismatches not shown.");

                reporter.Fail(
                    Id,
                    $"Payload diff failed (missing={diff.MissingCells}, unexpected={diff.UnexpectedCells}, fieldMismatch={diff.Mismatches.Count}).");
            }
            finally
            {
                ScenarioPasteHelpers.TeardownPasteArea(projection, expectedWidth, expectedHeight, reporter, Id);
            }
        }
    }
}
#endif
