#if DEBUG
#nullable enable

using System;
using System.Collections;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Shared paste orchestration helpers for scenario tests.</summary>
    internal static class ScenarioPasteHelpers
    {
        private const int MaxPasteSteps = 400_000;

        /// <summary>Builds an empty defined payload used to clear a paste area.</summary>
        internal static Payload CreateEmptyPayload(int width, int height) =>
            Payload.FromSparse([], width, height);

        /// <summary>Starts a paste and yields one debug step at a time until it finishes or times out.</summary>
        internal static IEnumerator RunPasteAndAwait(
            Projection projection,
            IScenarioTestReporter reporter,
            string scenarioId,
            Action<bool, string?> onComplete)
        {
            // --- StartPaste, then DebugStepOperation each yield until idle or the step cap.

            PastingSystem pastingSystem = ModContent.GetInstance<PastingSystem>();
            if (!pastingSystem.StartPaste(projection, out PasteRejectReason rejectReason))
            {
                onComplete(false, $"Paste rejected: {rejectReason}.");
                yield break;
            }

            int steps = 0;
            while (pastingSystem.HasActiveOperationFor(projection))
            {
                if (steps++ >= MaxPasteSteps)
                {
                    pastingSystem.StopPaste(projection);
                    onComplete(false, $"Paste did not complete within {MaxPasteSteps} steps.");
                    yield break;
                }

                bool keep = pastingSystem.DebugStepOperation(projection);
                if (!keep && pastingSystem.HasActiveOperationFor(projection))
                {
                    pastingSystem.StopPaste(projection);
                    onComplete(false, "Paste stalled while operation was still active.");
                    yield break;
                }

                yield return null;
            }

            reporter.Info($"{scenarioId}: paste completed in {steps} step(s).");
            onComplete(true, null);
        }

        /// <summary>Pastes an empty payload over the projection target to restore air after a scenario.</summary>
        internal static IEnumerator TeardownPasteArea(
            Projection projection,
            int width,
            int height,
            IScenarioTestReporter reporter,
            string scenarioId)
        {
            // --- Replace the payload with empty cells, then run the same stepped paste path.

            if (width <= 0 || height <= 0)
                yield break;

            Payload emptyPayload = CreateEmptyPayload(width, height);
            projection.SetPayload(emptyPayload);

            bool ok = false;
            string? error = null;
            IEnumerator clear = RunPasteAndAwait(
                projection,
                reporter,
                scenarioId,
                (success, err) =>
                {
                    ok = success;
                    error = err;
                });

            while (clear.MoveNext())
                yield return clear.Current;

            if (ok)
            {
                reporter.Info($"{scenarioId}: teardown clear completed.");
                yield break;
            }

            reporter.Error(scenarioId, $"Teardown clear failed: {error}");
        }
    }
}
#endif
