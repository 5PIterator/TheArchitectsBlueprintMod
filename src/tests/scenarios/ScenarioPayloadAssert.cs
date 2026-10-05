#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Shared payload equality asserts for scenario tests.</summary>
    internal static class ScenarioPayloadAssert
    {
        private const int MaxDiffLines = 20;

        /// <summary>Diffs payloads and reports Fail on mismatch without emitting Pass.</summary>
        public static bool TryMatch(
            string scenarioId,
            IScenarioTestReporter reporter,
            Payload expected,
            Payload actual,
            Microsoft.Xna.Framework.Rectangle targetArea)
        {
            // --- Diff every facet and print a capped mismatch list; leave Pass to the caller.

            ScenarioPayloadDiffResult diff = ScenarioPayloadDiff.Compare(expected, actual, targetArea);
            reporter.Info(
                $"{scenarioId}: compared={diff.ComparedCells}, missing={diff.MissingCells}, unexpected={diff.UnexpectedCells}, fieldMismatch={diff.Mismatches.Count}.");

            if (diff.MismatchCount == 0)
                return true;

            int toShow = diff.Mismatches.Count < MaxDiffLines ? diff.Mismatches.Count : MaxDiffLines;
            for (int i = 0; i < toShow; i++)
            {
                ScenarioPayloadDiffItem mismatch = diff.Mismatches[i];
                reporter.Info(
                    $"{scenarioId}: rel=({mismatch.Relative.X},{mismatch.Relative.Y}) world=({mismatch.World.X},{mismatch.World.Y}) " +
                    $"{mismatch.Facet} expected={mismatch.Expected} actual={mismatch.Actual}");
            }

            if (diff.Mismatches.Count > MaxDiffLines)
                reporter.Info($"{scenarioId}: ... {diff.Mismatches.Count - MaxDiffLines} additional field mismatches not shown.");

            reporter.Fail(
                scenarioId,
                $"Payload diff failed (missing={diff.MissingCells}, unexpected={diff.UnexpectedCells}, fieldMismatch={diff.Mismatches.Count}).");
            return false;
        }

        /// <summary>Fails the scenario when payloads differ; Passes when they match.</summary>
        public static bool PayloadsMatch(
            string scenarioId,
            IScenarioTestReporter reporter,
            Payload expected,
            Payload actual,
            Microsoft.Xna.Framework.Rectangle targetArea,
            string passMessage)
        {
            // --- Delegate to TryMatch, then Pass on success.
            if (!TryMatch(scenarioId, reporter, expected, actual, targetArea))
                return false;

            reporter.Pass(scenarioId, passMessage);
            return true;
        }
    }
}
#endif
