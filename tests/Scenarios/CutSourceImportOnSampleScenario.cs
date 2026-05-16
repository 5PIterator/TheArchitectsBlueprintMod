#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class CutSourceImportOnSampleScenario(PayloadSample sample) : ITestScenario
    {
        public string Id => $"cut_on_{sample.Slug}";
        public string Description => $"Import {sample.FileName} with CutSource enabled.";

        public void Run(ITestReporter reporter, Player player)
        {
            if (!PayloadSampleCatalog.TryLoad(sample, out Payload payload, out string? loadErr))
            {
                reporter.Fail("load sample", loadErr);
                return;
            }

            TestBlueprintChain config = TestBlueprintChain.Begin(player).WithTileBoundsAtCursorForPayload(payload);
            ScenarioHarness.RunImportedProjection(reporter, player, Id, config, payload, cutSource: true, (chain, r) =>
            {
                Projection projection = chain.Projection!;

                if (!PayloadScenarioChecks.CutSourceEquals(projection, expected: true, out string? error))
                {
                    r.Fail("cut source", error);
                    return false;
                }

                r.Pass("cut source enabled");

                if (!PayloadScenarioChecks.SourceAreaMatches(projection, chain.WorldTileBounds, out error))
                {
                    r.Fail("source area", error);
                    return false;
                }

                r.Pass("source area matches footprint");
                return true;
            });
        }
    }
}
#endif
