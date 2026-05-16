#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class CutSourceImportOffSampleScenario(PayloadSample sample) : ITestScenario
    {
        public string Id => $"cut_off_{sample.Slug}";
        public string Description => $"Import {sample.FileName} with CutSource disabled.";

        public void Run(ITestReporter reporter, Player player)
        {
            if (!PayloadSampleCatalog.TryLoad(sample, out Payload payload, out string? loadErr))
            {
                reporter.Fail("load sample", loadErr);
                return;
            }

            TestBlueprintChain config = TestBlueprintChain.Begin(player).WithTileBoundsAtCursorForPayload(payload);
            ScenarioHarness.RunImportedProjection(reporter, player, Id, config, payload, cutSource: false, (chain, r) =>
            {
                if (!PayloadScenarioChecks.CutSourceEquals(chain.Projection!, expected: false, out string? error))
                {
                    r.Fail("cut source", error);
                    return false;
                }

                r.Pass("cut source disabled");
                return true;
            });
        }
    }
}
#endif
