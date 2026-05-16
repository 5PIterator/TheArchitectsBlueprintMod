#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class CutSourceToggleSampleScenario(PayloadSample sample) : ITestScenario
    {
        public string Id => $"cut_source_toggle_{sample.Slug}";
        public string Description => $"Toggle cut source after capturing {sample.FileName} from world.";

        public void Run(ITestReporter reporter, Player player)
        {
            if (!PayloadSampleCatalog.TryLoad(sample, out Payload payload, out string? loadErr))
            {
                reporter.Fail("load sample", loadErr);
                return;
            }

            if (PayloadScenarioChecks.CountCellsWithTile(payload) == 0)
            {
                reporter.Info($"[{Id}] skipped — payload has no tiles to place for capture.");
                return;
            }

            TestBlueprintChain config = TestBlueprintChain.Begin(player).WithTileBoundsAtCursorForPayload(payload);

            ScenarioHarness.RunProjection(
                reporter,
                player,
                Id,
                config,
                chain => WorldScenarioSetup.PlacePayload(chain, chain.WorldTileBounds, payload),
                (chain, r) =>
            {
                Projection projection = chain.Projection!;

                if (!projection.HasPayload)
                {
                    r.Fail("payload", "Projection has no payload after world capture.");
                    return false;
                }

                r.Pass("payload captured");

                projection.SetCut(true);
                if (!PayloadScenarioChecks.CutSourceEquals(projection, expected: true, out string? error))
                {
                    r.Fail("set cut on", error);
                    return false;
                }

                r.Pass("set cut on");

                if (!projection.ToggleCut())
                {
                    r.Fail("toggle off", "ToggleCut returned false.");
                    return false;
                }

                if (!PayloadScenarioChecks.CutSourceEquals(projection, expected: false, out error))
                {
                    r.Fail("toggle off", error);
                    return false;
                }

                r.Pass("toggle off");

                if (!projection.ToggleCut())
                {
                    r.Fail("toggle on", "ToggleCut returned false.");
                    return false;
                }

                if (!PayloadScenarioChecks.CutSourceEquals(projection, expected: true, out error))
                {
                    r.Fail("toggle on", error);
                    return false;
                }

                r.Pass("toggle on");
                return true;
            });
        }
    }
}
#endif
