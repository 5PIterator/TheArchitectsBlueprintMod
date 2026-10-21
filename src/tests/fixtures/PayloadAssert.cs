#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Shared payload equality asserts for Test Cases.</summary>
    internal static class PayloadAssert
    {
        private const int MaxDiffLines = 20;

        /// <summary>Diffs payloads and reports Fail on mismatch without emitting Pass.</summary>
        public static bool TryMatch(
            string caseTag,
            ITestCaseReporter reporter,
            Payload expected,
            Payload actual,
            Microsoft.Xna.Framework.Rectangle targetArea)
        {
            // --- Diff every facet and print a capped mismatch list; leave Pass to the caller.

            PayloadDiffResult diff = PayloadDiff.Compare(expected, actual, targetArea);
            reporter.Info(
                $"{caseTag}: compared={diff.ComparedCells}, missing={diff.MissingCells}, unexpected={diff.UnexpectedCells}, fieldMismatch={diff.Mismatches.Count}.");

            if (diff.MismatchCount == 0)
                return true;

            int toShow = diff.Mismatches.Count < MaxDiffLines ? diff.Mismatches.Count : MaxDiffLines;
            for (int i = 0; i < toShow; i++)
            {
                PayloadDiffItem mismatch = diff.Mismatches[i];
                reporter.Info(
                    $"{caseTag}: rel=({mismatch.Relative.X},{mismatch.Relative.Y}) world=({mismatch.World.X},{mismatch.World.Y}) " +
                    $"{mismatch.Facet} expected={mismatch.Expected} actual={mismatch.Actual}");
            }

            if (diff.Mismatches.Count > MaxDiffLines)
                reporter.Info($"{caseTag}: ... {diff.Mismatches.Count - MaxDiffLines} additional field mismatches not shown.");

            reporter.Fail(
                caseTag,
                $"Payload diff failed (missing={diff.MissingCells}, unexpected={diff.UnexpectedCells}, fieldMismatch={diff.Mismatches.Count}).");
            return false;
        }

        /// <summary>Fails the Case when payloads differ; Passes when they match.</summary>
        public static bool PayloadsMatch(
            string caseTag,
            ITestCaseReporter reporter,
            Payload expected,
            Payload actual,
            Microsoft.Xna.Framework.Rectangle targetArea,
            string passMessage)
        {
            // --- Delegate to TryMatch, then Pass on success.
            if (!TryMatch(caseTag, reporter, expected, actual, targetArea))
                return false;

            reporter.Pass(caseTag, passMessage);
            return true;
        }
    }
}
#endif
