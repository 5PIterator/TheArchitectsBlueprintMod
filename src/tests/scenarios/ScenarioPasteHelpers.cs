#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Shared paste orchestration helpers for scenario tests.</summary>
    internal static class ScenarioPasteHelpers
    {
        private const int MaxPasteSteps = 400_000;

        internal static Payload CreateEmptyPayload(int width, int height) =>
            Payload.FromSparse([], width, height);

        internal static bool RunPasteAndAwait(
            Projection projection,
            IScenarioTestReporter reporter,
            string scenarioId,
            out string? error)
        {
            error = null;
            PastingSystem pastingSystem = ModContent.GetInstance<PastingSystem>();
            if (!pastingSystem.StartPaste(projection, out PasteRejectReason rejectReason))
            {
                error = $"Paste rejected: {rejectReason}.";
                return false;
            }

            int steps = 0;
            while (pastingSystem.HasActiveOperationFor(projection))
            {
                if (steps++ >= MaxPasteSteps)
                {
                    pastingSystem.StopPaste(projection);
                    error = $"Paste did not complete within {MaxPasteSteps} manual steps.";
                    return false;
                }

                bool keep = pastingSystem.DebugStepOperation(projection);
                if (!keep && pastingSystem.HasActiveOperationFor(projection))
                {
                    pastingSystem.StopPaste(projection);
                    error = "Paste stalled while operation was still active.";
                    return false;
                }
            }

            reporter.Info($"{scenarioId}: paste completed in {steps} step(s).");
            return true;
        }

        internal static void TeardownPasteArea(
            Projection projection,
            int width,
            int height,
            IScenarioTestReporter reporter,
            string scenarioId)
        {
            if (width <= 0 || height <= 0)
                return;

            Payload emptyPayload = CreateEmptyPayload(width, height);
            projection.SetPayload(emptyPayload);
            if (RunPasteAndAwait(projection, reporter, scenarioId, out string? error))
            {
                reporter.Info($"{scenarioId}: teardown clear completed.");
                return;
            }

            reporter.Error(scenarioId, $"Teardown clear failed: {error}");
        }
    }
}
#endif
