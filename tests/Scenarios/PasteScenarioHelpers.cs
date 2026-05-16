#if DEBUG
#nullable enable

using System;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal static class PasteScenarioOffsets
    {
        internal static int TargetOffsetTilesX(Payload payload) => Math.Max(6, payload.Width + 3);
    }

    internal static class PasteScenarioHelpers
    {
        private const int MaxPasteSteps = 500_000;

        internal static bool RunPasteToCompletion(
            BlueprintFixtureChain chain,
            Projection projection,
            ITestReporter reporter,
            out string? error)
        {
            error = null;
            chain.ProtectPasteAreas(projection);
            PastingSystem pasting = ModContent.GetInstance<PastingSystem>();

            if (!projection.HasPayload)
            {
                error = "Projection has no payload; nothing to paste.";
                return false;
            }

            if (!pasting.StartPaste(projection, ClientPreferences.FromLocalConfigs(), out PasteRejectReason reject))
            {
                error = reject.ToString();
                return false;
            }

            reporter.Pass("start paste");

            int steps = 0;
            while (pasting.DebugStepOperation(projection))
            {
                steps++;
                if (steps > MaxPasteSteps)
                {
                    pasting.StopPaste(projection);
                    error = $"Exceeded {MaxPasteSteps} steps.";
                    return false;
                }
            }

            if (pasting.HasActiveOperationFor(projection))
            {
                pasting.StopPaste(projection);
                error = "Operation still active after stepping.";
                return false;
            }

            if (projection.IsLocked)
            {
                error = "Projection still locked.";
                return false;
            }

            if (steps == 0)
            {
                error = "Paste completed with 0 steps (no work was scheduled).";
                return false;
            }

            reporter.Pass($"paste complete ({steps} steps)");
            return true;
        }
    }
}
#endif
