#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class ImportSampleScenario(PayloadSample sample) : ITestScenario
    {
        public string Id => $"import_{sample.Slug}";
        public string Description => $"Import {sample.FileName}; verify projection matches parsed payload.";

        public void Run(ITestReporter reporter, Player player)
        {
            if (!PayloadSampleCatalog.TryLoad(sample, out Payload payload, out string? loadErr))
            {
                reporter.Fail("load sample", loadErr);
                return;
            }

            TestBlueprintChain config = TestBlueprintChain.Begin(player).WithTileBoundsAtCursorForPayload(payload);
            ScenarioHarness.RunImportedProjection(reporter, player, Id, config, payload, (chain, r) =>
            {
                return PayloadScenarioChecks.VerifyImportedProjection(payload, chain.Projection!, r, out _);
            });
        }
    }
}
#endif
