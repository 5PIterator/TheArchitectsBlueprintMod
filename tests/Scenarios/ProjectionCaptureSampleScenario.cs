#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class ProjectionCaptureSampleScenario(PayloadSample sample) : ITestScenario
    {
        public string Id => $"projection_capture_{sample.Slug}";
        public string Description => $"World capture projection for {sample.FileName}.";

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
                if (!PayloadScenarioChecks.PayloadMatchesSize(projection.Payload, payload.Width, payload.Height, out string? error))
                {
                    r.Fail("payload size", error);
                    return false;
                }

                r.Pass($"payload size {payload.Width}×{payload.Height}");

                if (projection.TargetArea != chain.WorldTileBounds)
                {
                    r.Fail("target area", $"Expected {chain.WorldTileBounds}, got {projection.TargetArea}.");
                    return false;
                }

                r.Pass("target area matches selection");
                return PayloadScenarioChecks.VerifyPayloadContent(projection.Payload, r, out _);
            });
        }
    }
}
#endif
