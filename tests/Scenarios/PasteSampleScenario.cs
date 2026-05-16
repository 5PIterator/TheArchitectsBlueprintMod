#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class PasteSampleScenario(PayloadSample sample) : ITestScenario
    {
        public string Id => $"paste_{sample.Slug}";
        public string Description => $"Import and paste {sample.FileName}; verify world matches payload.";

        public void Run(ITestReporter reporter, Player player)
        {
            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                reporter.Fail("environment", "Paste scenario requires single-player.");
                return;
            }

            if (!PayloadSampleCatalog.TryLoad(sample, out Payload payload, out string? loadErr))
            {
                reporter.Fail("load sample", loadErr);
                return;
            }

            TestBlueprintChain config = TestBlueprintChain.Begin(player).WithTileBoundsAtCursorForPayload(payload);
            ScenarioHarness.RunImportedProjection(reporter, player, Id, config, payload, (chain, r) =>
            {
                if (!PayloadScenarioChecks.VerifyPayloadContent(payload, r, out _))
                    return false;

                Projection projection = chain.Projection!;
                if (!PasteScenarioHelpers.RunPasteToCompletion(chain, projection, r, out string? error))
                {
                    r.Fail("paste", error);
                    return false;
                }

                if (!PayloadScenarioChecks.TargetWorldMatchesPayload(projection, out error))
                {
                    r.Fail("world match", error);
                    return false;
                }

                r.Pass("world tiles and walls match payload");
                return true;
            });
        }
    }
}
#endif
